// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.ApplicationModel.Appointments;
using WinKnife.Core;

namespace WinKnife.Features.Island;

internal sealed class CalEvent
{
    public string Id { get; init; } = "";

    public string Title { get; init; } = "";

    public DateTime Start { get; init; }

    public DateTime End { get; init; }

    public bool AllDay { get; init; }

    public string Location { get; init; } = "";

    public string CalendarId { get; init; } = "";

    public string CalendarName { get; init; } = "";

    public Color Color { get; init; } = Color.FromRgb(90, 165, 255);

    public string Source { get; init; } = "";

    public string? LocalId { get; init; }

    public string CountdownKey => LocalId is null ? Id : $"{LocalId}@{Start.Ticks}";

    public bool Ongoing(DateTime now) => !AllDay && Start <= now && End > now;
}

internal sealed record CalendarInfo(string Id, string Name, string Account, Color Color, string Source);

internal enum CalendarAccess
{
    Unknown,
    Allowed,
    Denied,
    Unavailable,
}

/// <summary>Calendar data from the Windows appointment store and ICS files/URLs (spec Part C 6.4/6.7).</summary>
internal sealed class CalendarService : IDisposable
{
    private readonly IAppSettings _app;
    private readonly Dispatcher _ui;
    private readonly ILog _log;
    private readonly DispatcherTimer _timer;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private AppointmentStore? _store;
    private List<CalEvent> _events = [];
    private List<CalendarInfo> _calendars = [];
    private DateTime _rangeStart;
    private DateTime _rangeEnd;
    private bool _loading;
    private bool _disposed;
    private DateTime _lastIcs = DateTime.MinValue;
    private readonly Dictionary<string, string> _icsCache = [];

    public CalendarService(IAppSettings app, Dispatcher ui, ILog log)
    {
        _app = app;
        _ui = ui;
        _log = log;
        _timer = new DispatcherTimer(DispatcherPriority.Background, ui);
        _timer.Tick += (_, _) => _ = RefreshAsync();
        Microsoft.Win32.SystemEvents.TimeChanged += OnClock;
    }

    public CalendarAccess Access { get; private set; }

    public bool Loading => _loading;

    public bool RequestFailed { get; private set; }

    public IReadOnlyList<CalEvent> Events => _events;

    public IReadOnlyList<CalendarInfo> Calendars => _calendars;

    public event Action? Changed;

    /// <summary>The window of days kept in memory (extended by the month grid).</summary>
    public (DateTime From, DateTime To) Range => (_rangeStart, _rangeEnd);

    public void WantRange(DateTime from, DateTime to)
    {
        if (from < _rangeStart || to > _rangeEnd || _rangeStart == default)
        {
            _rangeStart = from;
            _rangeEnd = to;
            _ = RefreshAsync();
        }
    }

    private void OnClock(object? sender, EventArgs e) => _ui.BeginInvoke(() => _ = RefreshAsync());

    public async Task<bool> RequestAccessAsync()
    {
        RequestFailed = false;
        try
        {
            var task = AppointmentManager.RequestStoreAsync(AppointmentStoreAccessType.AllCalendarsReadOnly).AsTask();
            if (await Task.WhenAny(task, Task.Delay(8000)) != task)
            {
                Access = CalendarAccess.Unavailable;
                RequestFailed = true;
                Changed?.Invoke();
                return false;
            }

            _store = await task;
            Access = _store is null ? CalendarAccess.Denied : CalendarAccess.Allowed;
            if (_store is not null)
            {
                _store.StoreChanged += (_, _) => _ui.BeginInvoke(() => _ = RefreshAsync());
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"Calendar access failed: {ex.Message}");
            Access = CalendarAccess.Unavailable;
            RequestFailed = true;
        }

        Changed?.Invoke();
        if (Access == CalendarAccess.Allowed)
        {
            await RefreshAsync();
        }

        return Access == CalendarAccess.Allowed;
    }

    public async Task RefreshAsync()
    {
        if (_disposed || _loading)
        {
            return;
        }

        if (_rangeStart == default)
        {
            var today = DateTime.Today;
            _rangeStart = today;
            _rangeEnd = today.AddDays(8);
        }

        _loading = true;
        Changed?.Invoke();
        try
        {
            if (_store is null && Access == CalendarAccess.Unknown)
            {
                await RequestAccessAsync();
            }

            var excluded = _app.Get<List<string>>(K.CalendarExcluded) ?? [];
            var events = new List<CalEvent>();
            var calendars = new List<CalendarInfo>();
            if (_store is not null)
            {
                await ReadStore(events, calendars, excluded);
            }

            await ReadIcs(events, calendars, excluded);
            _calendars = calendars;
            _events = [.. events
                .Where(e => e.End > e.Start || e.AllDay)
                .GroupBy(e => $"{e.Id}:{e.Start.Ticks}")
                .Select(g => g.First())
                .OrderBy(e => e.Start).ThenBy(e => e.End).ThenBy(e => e.Id, StringComparer.Ordinal)];
        }
        catch (Exception ex)
        {
            _log.Warn($"Calendar refresh failed: {ex.Message}");
        }
        finally
        {
            _loading = false;
        }

        ScheduleNext();
        Changed?.Invoke();
    }

    private async Task ReadStore(List<CalEvent> events, List<CalendarInfo> calendars, List<string> excluded)
    {
        var cals = await _store!.FindAppointmentCalendarsAsync(FindAppointmentCalendarsOptions.IncludeHidden);
        Dictionary<string, string> accounts = [];
        try
        {
            var acStore = await Windows.ApplicationModel.UserDataAccounts.UserDataAccountManager.RequestStoreAsync(Windows.ApplicationModel.UserDataAccounts.UserDataAccountStoreAccessType.AllAccountsReadOnly);
            foreach (var cal in cals)
            {
                if (!string.IsNullOrEmpty(cal.UserDataAccountId) && !accounts.ContainsKey(cal.UserDataAccountId))
                {
                    var acc = await acStore.GetAccountAsync(cal.UserDataAccountId);
                    accounts[cal.UserDataAccountId] = acc?.UserDisplayName ?? "Other";
                }
            }
        }
        catch
        {
        }

        var allowed = new List<string>();
        foreach (var cal in cals)
        {
            var c = cal.DisplayColor;
            calendars.Add(new CalendarInfo(cal.LocalId, cal.DisplayName, accounts.GetValueOrDefault(cal.UserDataAccountId ?? "", "Windows"), Color.FromRgb(c.R, c.G, c.B), "store"));
            if (!excluded.Contains(cal.LocalId))
            {
                allowed.Add(cal.LocalId);
            }
        }

        if (allowed.Count == 0)
        {
            return;
        }

        var options = new FindAppointmentsOptions { MaxCount = 800 };
        foreach (var id in allowed)
        {
            options.CalendarIds.Add(id);
        }

        foreach (var p in new[] { AppointmentProperties.Subject, AppointmentProperties.Location, AppointmentProperties.StartTime, AppointmentProperties.Duration, AppointmentProperties.AllDay })
        {
            options.FetchProperties.Add(p);
        }

        var found = await _store.FindAppointmentsAsync(new DateTimeOffset(_rangeStart), _rangeEnd - _rangeStart, options);
        foreach (var a in found)
        {
            if (a.IsCanceledMeeting)
            {
                continue;
            }

            var start = a.StartTime.LocalDateTime;
            var info = calendars.FirstOrDefault(x => x.Id == a.CalendarId);
            events.Add(new CalEvent
            {
                Id = a.LocalId,
                LocalId = a.LocalId,
                Title = a.Subject ?? "",
                Start = a.AllDay ? start.Date : start,
                End = a.AllDay ? start.Date.AddDays(Math.Max(1, Math.Round(a.Duration.TotalDays))) : start + a.Duration,
                AllDay = a.AllDay,
                Location = a.Location ?? "",
                CalendarId = a.CalendarId ?? "",
                CalendarName = info?.Name ?? "",
                Color = info?.Color ?? Color.FromRgb(90, 165, 255),
                Source = "store",
            });
        }
    }

    // ---------------------------------------------------------------- ICS

    private async Task ReadIcs(List<CalEvent> events, List<CalendarInfo> calendars, List<string> excluded)
    {
        var sources = _app.Get<List<string>>(K.CalendarSources) ?? [];
        foreach (var src in sources.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            try
            {
                string text;
                if (src.StartsWith("http", StringComparison.OrdinalIgnoreCase) || src.StartsWith("webcal", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_icsCache.TryGetValue(src, out var cached) || (DateTime.UtcNow - _lastIcs).TotalMinutes > 15)
                    {
                        cached = await _http.GetStringAsync(src.Replace("webcal://", "https://", StringComparison.OrdinalIgnoreCase));
                        _icsCache[src] = cached;
                        _lastIcs = DateTime.UtcNow;
                    }

                    text = cached;
                }
                else if (File.Exists(src))
                {
                    text = await File.ReadAllTextAsync(src);
                }
                else
                {
                    continue;
                }

                var name = Path.GetFileNameWithoutExtension(src);
                var id = "ics:" + src;
                calendars.Add(new CalendarInfo(id, string.IsNullOrEmpty(name) ? "Calendar" : name, "ICS", Color.FromRgb(0x5E, 0xC8, 0x6B), "ics"));
                if (excluded.Contains(id))
                {
                    continue;
                }

                events.AddRange(IcsParser.Parse(text, _rangeStart, _rangeEnd, id, string.IsNullOrEmpty(name) ? "Calendar" : name));
            }
            catch (Exception ex)
            {
                _log.Warn($"ICS source failed: {ex.Message}");
            }
        }
    }

    // ---------------------------------------------------------------- scheduling

    private void ScheduleNext()
    {
        var now = DateTime.Now;
        var next = now.AddMinutes(15);
        var midnight = now.Date.AddDays(1);
        if (midnight < next)
        {
            next = midnight;
        }

        foreach (var e in _events.Where(e => !e.AllDay))
        {
            if (e.Start > now && e.Start < next)
            {
                next = e.Start;
            }

            if (e.End > now && e.End < next)
            {
                next = e.End;
            }
        }

        _timer.Stop();
        _timer.Interval = TimeSpan.FromSeconds(Math.Max(5, (next - now).TotalSeconds + 1));
        _timer.Start();
    }

    // ---------------------------------------------------------------- queries

    public IEnumerable<CalEvent> On(DateTime day)
    {
        var start = day.Date;
        var end = start.AddDays(1);
        return _events
            .Where(e => e.Start < end && (e.End > start || (e.AllDay && e.Start == start)))
            .OrderBy(e => e.AllDay ? 0 : 1).ThenBy(e => e.Start).ThenBy(e => e.Id, StringComparer.Ordinal);
    }

    public CalEvent? Next(DateTime now) => _events.FirstOrDefault(e => !e.AllDay && e.End > now);

    /// <summary>The countdown target (spec 6.5): nearest upcoming event inside its lead, or an ongoing one in its last hour.</summary>
    public (CalEvent Event, bool Ongoing, DateTime Boundary)? CountdownTarget(DateTime now)
    {
        var all = _app.Get<bool>(K.CalendarCountdown);
        var timeLeft = _app.Get<bool>(K.CalendarTimeLeft);
        var lead = TimeSpan.FromMinutes(Math.Clamp(_app.Get<int>(K.CalendarLead), 5, 240));
        var chosen = _app.Get<Dictionary<string, string>>(K.CalendarChosen) ?? [];
        (CalEvent, bool, DateTime)? best = null;
        foreach (var e in _events.Where(e => !e.AllDay))
        {
            if (e.Start > now)
            {
                var wants = all || chosen.ContainsKey(e.CountdownKey);
                if (wants && e.Start - now <= lead && (best is null || (!best.Value.Item2 && e.Start < best.Value.Item1.Start)))
                {
                    best = (e, false, e.Start);
                }
            }
            else if (e.End > now && timeLeft && e.End - now <= TimeSpan.FromMinutes(60))
            {
                var ongoingBest = best is { Item2: true } b ? b.Item1.End : DateTime.MaxValue;
                if (e.End < ongoingBest || best is null || !best.Value.Item2)
                {
                    // ties prefer the ongoing event
                    if (best is null || best.Value.Item2 || e.End - now <= best.Value.Item3 - now)
                    {
                        best = (e, true, e.End);
                    }
                }
            }
        }

        return best;
    }

    public void ToggleChosen(CalEvent e)
    {
        var chosen = new Dictionary<string, string>(_app.Get<Dictionary<string, string>>(K.CalendarChosen) ?? []);
        var key = e.CountdownKey;
        if (!chosen.Remove(key))
        {
            chosen[key] = e.End.ToString("o", CultureInfo.InvariantCulture);
        }

        foreach (var k in chosen.Where(kv => DateTime.TryParse(kv.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var end) && end < DateTime.Now).Select(kv => kv.Key).ToList())
        {
            chosen.Remove(k);
        }

        _app.Set(K.CalendarChosen, chosen);
        Changed?.Invoke();
    }

    public bool IsChosen(CalEvent e) => (_app.Get<Dictionary<string, string>>(K.CalendarChosen) ?? []).ContainsKey(e.CountdownKey);

    public void OpenEvent(CalEvent e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("outlookcal:") { UseShellExecute = true });
        }
        catch
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-calendar:") { UseShellExecute = true });
            }
            catch
            {
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        Microsoft.Win32.SystemEvents.TimeChanged -= OnClock;
        _http.Dispose();
    }
}

/// <summary>Minimal iCalendar reader (VEVENT with basic RRULE/EXDATE expansion).</summary>
internal static class IcsParser
{
    public static IEnumerable<CalEvent> Parse(string text, DateTime from, DateTime to, string calId, string calName)
    {
        var lines = Unfold(text);
        var color = Color.FromRgb(0x5E, 0xC8, 0x6B);
        Dictionary<string, string>? cur = null;
        List<DateTime> ex = [];
        foreach (var raw in lines)
        {
            if (raw.StartsWith("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                cur = new(StringComparer.OrdinalIgnoreCase);
                ex = [];
                continue;
            }

            if (raw.StartsWith("END:VEVENT", StringComparison.OrdinalIgnoreCase) && cur is not null)
            {
                foreach (var e in Expand(cur, ex, from, to, calId, calName, color))
                {
                    yield return e;
                }

                cur = null;
                continue;
            }

            if (cur is null)
            {
                continue;
            }

            var colon = raw.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            var head = raw[..colon];
            var value = raw[(colon + 1)..];
            var name = head.Split(';')[0].ToUpperInvariant();
            if (name == "EXDATE")
            {
                foreach (var v in value.Split(','))
                {
                    if (ParseDate(head, v, out var d, out _))
                    {
                        ex.Add(d);
                    }
                }

                continue;
            }

            cur[name] = value;
            cur[name + "_HEAD"] = head;
        }
    }

    private static IEnumerable<CalEvent> Expand(Dictionary<string, string> ev, List<DateTime> ex, DateTime from, DateTime to, string calId, string calName, Color color)
    {
        if (!ev.TryGetValue("DTSTART", out var ds) || !ParseDate(ev.GetValueOrDefault("DTSTART_HEAD", ""), ds, out var start, out var allDay))
        {
            yield break;
        }

        var end = start.AddHours(1);
        if (ev.TryGetValue("DTEND", out var de) && ParseDate(ev.GetValueOrDefault("DTEND_HEAD", ""), de, out var e2, out _))
        {
            end = e2;
        }
        else if (allDay)
        {
            end = start.AddDays(1);
        }

        var length = end - start;
        var title = Unescape(ev.GetValueOrDefault("SUMMARY", ""));
        var loc = Unescape(ev.GetValueOrDefault("LOCATION", ""));
        var uid = ev.GetValueOrDefault("UID", Guid.NewGuid().ToString());
        var starts = new List<DateTime>();
        if (ev.TryGetValue("RRULE", out var rrule))
        {
            starts.AddRange(Recur(rrule, start, from, to));
        }
        else
        {
            starts.Add(start);
        }

        foreach (var s in starts)
        {
            if (ex.Contains(s) || s + length <= from || s >= to)
            {
                continue;
            }

            yield return new CalEvent
            {
                Id = $"{calId}|{uid}",
                Title = title,
                Start = s,
                End = s + length,
                AllDay = allDay,
                Location = loc,
                CalendarId = calId,
                CalendarName = calName,
                Color = color,
                Source = "ics",
            };
        }
    }

    private static IEnumerable<DateTime> Recur(string rule, DateTime start, DateTime from, DateTime to)
    {
        var parts = rule.Split(';').Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0].ToUpperInvariant(), p => p[1]);
        var freq = parts.GetValueOrDefault("FREQ", "");
        var interval = int.TryParse(parts.GetValueOrDefault("INTERVAL", "1"), out var iv) ? Math.Max(1, iv) : 1;
        var count = int.TryParse(parts.GetValueOrDefault("COUNT", "0"), out var c) ? c : 0;
        DateTime? until = parts.TryGetValue("UNTIL", out var u) && ParseDate("", u, out var ud, out _) ? ud : null;
        var byDay = parts.TryGetValue("BYDAY", out var bd) ? bd.Split(',').Select(DayOf).Where(d => d is not null).Select(d => d!.Value).ToList() : [];
        var produced = 0;
        for (var i = 0; i < 4000; i++)
        {
            IEnumerable<DateTime> batch;
            switch (freq)
            {
                case "DAILY":
                    batch = [start.AddDays(i * interval)];
                    break;
                case "WEEKLY":
                    var weekStart = start.AddDays(7 * i * interval - (int)start.DayOfWeek);
                    batch = byDay.Count == 0 ? [start.AddDays(7 * i * interval)] : byDay.OrderBy(d => (int)d).Select(d => weekStart.AddDays((int)d).Date + start.TimeOfDay).Where(d => d >= start);
                    break;
                case "MONTHLY":
                    batch = [start.AddMonths(i * interval)];
                    break;
                case "YEARLY":
                    batch = [start.AddYears(i * interval)];
                    break;
                default:
                    yield break;
            }

            foreach (var s in batch)
            {
                if ((until is not null && s > until) || s >= to || (count > 0 && produced >= count))
                {
                    yield break;
                }

                produced++;
                yield return s;
            }
        }
    }

    private static DayOfWeek? DayOf(string s)
    {
        s = s.Length > 2 ? s[^2..] : s;
        return s switch { "MO" => DayOfWeek.Monday, "TU" => DayOfWeek.Tuesday, "WE" => DayOfWeek.Wednesday, "TH" => DayOfWeek.Thursday, "FR" => DayOfWeek.Friday, "SA" => DayOfWeek.Saturday, "SU" => DayOfWeek.Sunday, _ => null };
    }

    private static bool ParseDate(string head, string v, out DateTime date, out bool allDay)
    {
        allDay = false;
        date = default;
        v = v.Trim();
        if (v.Length == 8 && DateTime.TryParseExact(v, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            allDay = true;
            return true;
        }

        var utc = v.EndsWith('Z');
        if (DateTime.TryParseExact(v.TrimEnd('Z'), "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            if (utc)
            {
                date = DateTime.SpecifyKind(date, DateTimeKind.Utc).ToLocalTime();
            }

            return true;
        }

        return false;
    }

    private static string Unescape(string s) => s.Replace("\\n", "\n").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\");

    private static IEnumerable<string> Unfold(string text)
    {
        string? current = null;
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith(' ') || line.StartsWith('\t'))
            {
                current += line[1..];
                continue;
            }

            if (current is not null)
            {
                yield return current;
            }

            current = line;
        }

        if (current is not null)
        {
            yield return current;
        }
    }
}
