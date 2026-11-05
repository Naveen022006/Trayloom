// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.ApplicationModel.Appointments;
using Trayloom.Core;

namespace Trayloom.Features.Island;

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

    /// <summary>A video-call link (Meet, Zoom, Teams…) found in the event, or null.</summary>
    public string? JoinUrl { get; init; }

    /// <summary>Where the event can be opened on the web (Google Calendar's day view), or null.</summary>
    public string? WebUrl { get; init; }

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

/// <summary>Calendar data from Google Calendar (secret iCal addresses), the Windows appointment store and ICS files/URLs (spec Part C 6.4/6.7, spec 09 part 4).</summary>
internal sealed class CalendarService : IDisposable
{
    private readonly IAppSettings _app;
    private readonly Dispatcher _ui;
    private readonly ILog _log;
    private readonly DispatcherTimer _timer;
    private const int MaxIcsBytes = 8 * 1024 * 1024;
    private static readonly TimeSpan IcsTimeout = TimeSpan.FromSeconds(20);
    private readonly HttpClient _http = new(new SocketsHttpHandler { MaxAutomaticRedirections = 5, AllowAutoRedirect = true }) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    private readonly GoogleCalendarSource _google;
    private AppointmentStore? _store;
    private List<CalEvent> _events = [];
    private List<CalendarInfo> _calendars = [];
    private DateTime _rangeStart;
    private DateTime _rangeEnd;
    private bool _loading;
    private bool _disposed;
    private bool _again;
    private readonly Dictionary<string, DateTime> _lastIcs = [];
    private readonly Dictionary<string, string> _icsCache = [];

    public CalendarService(IAppSettings app, Dispatcher ui, ILog log)
    {
        _app = app;
        _ui = ui;
        _log = log;
        _timer = new DispatcherTimer(DispatcherPriority.Background, ui);
        _timer.Tick += (_, _) => Refresh();
        Microsoft.Win32.SystemEvents.TimeChanged += OnClock;
        GoogleCalendarKeys.Register(app);
        _google = new GoogleCalendarSource(app, log);
        _google.Changed += OnGoogleChanged;
        _google.RefreshRequested += OnGoogleRefresh;
        _app.Changed += OnSetting;
    }

    /// <summary>The Google calendars added through secret iCal addresses.</summary>
    public GoogleCalendarSource Google => _google;

    /// <summary>The sources chosen by the "Show events from" setting (Automatic prefers Google once a calendar is added).</summary>
    public CalendarSourceMode SourceMode => GoogleRules.Resolve(_app.Get<string>(GoogleCalendarKeys.Source), _google.Feeds.Count);

    /// <summary>True when Windows Calendar feeds the island.</summary>
    public bool UsesWindows => SourceMode is CalendarSourceMode.Windows or CalendarSourceMode.Both;

    /// <summary>True when Google calendars feed the island.</summary>
    public bool UsesGoogle => SourceMode is CalendarSourceMode.Google or CalendarSourceMode.Both;

    private void OnGoogleChanged() => _ui.BeginInvoke(() => Changed?.Invoke());

    private void OnGoogleRefresh() => _ui.BeginInvoke(Refresh);

    /// <summary>A changed source or check interval re-reads the calendars so the page and the closed strip follow at once.</summary>
    private void OnSetting(string key)
    {
        if (key is GoogleCalendarKeys.Source or GoogleCalendarKeys.Interval)
        {
            _ui.BeginInvoke(Refresh);
        }
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
            Refresh();
        }
    }

    /// <summary>Reads the calendars again in the background; a failure is logged, never unobserved.</summary>
    public void Refresh() => Guarded.FireAndLog(RefreshAsync, _log, "Calendar refresh");

    private void OnClock(object? sender, EventArgs e) => _ui.BeginInvoke(Refresh);

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
                _store.StoreChanged += (_, _) => _ui.BeginInvoke(Refresh);
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
        if (_disposed)
        {
            return;
        }

        if (_loading)
        {
            // A change that arrives mid-read (a calendar added, a setting flipped) must not be lost, so read once more afterwards.
            _again = true;
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
            if (UsesWindows && _store is null && Access == CalendarAccess.Unknown)
            {
                await RequestAccessAsync();
            }

            var excluded = _app.Get<List<string>>(K.CalendarExcluded) ?? [];
            var events = new List<CalEvent>();
            var calendars = new List<CalendarInfo>();
            if (UsesWindows && _store is not null)
            {
                await ReadStore(events, calendars, excluded);
            }

            if (UsesGoogle)
            {
                await _google.ReadAsync(events, calendars, excluded, _rangeStart, _rangeEnd);
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
        if (_again)
        {
            _again = false;
            Refresh();
        }
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
                    accounts[cal.UserDataAccountId] = acc?.UserDisplayName ?? Loc.T("Other");
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

    /// <summary>
    /// The https address to download for an ICS source: <c>webcal://</c> becomes <c>https://</c>, <c>https://</c> is kept, and plain
    /// <c>http://</c> or anything else gives null. A user-info part, a missing host or control characters are refused too.
    /// </summary>
    internal static string? NormalizeIcsUrl(string? s)
    {
        var text = s?.Trim() ?? "";
        if (text.Length == 0 || text.Length > 2048 || text.Any(char.IsControl))
        {
            return null;
        }

        if (text.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase))
        {
            text = "https://" + text["webcal://".Length..];
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length > 0)
        {
            return null;
        }

        return uri.AbsoluteUri;
    }

    /// <summary>True for an ICS source written as a web address (so it is downloaded, never opened as a file).</summary>
    internal static bool LooksLikeUrl(string s) =>
        s.StartsWith("http:", StringComparison.OrdinalIgnoreCase) || s.StartsWith("https:", StringComparison.OrdinalIgnoreCase) || s.StartsWith("webcal:", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the address is a Google Calendar iCal address, which carries a secret and belongs in the Google setting.</summary>
    internal static bool IsGoogleCalendarAddress(string s) =>
        s.Contains("calendar.google.com", StringComparison.OrdinalIgnoreCase) || s.Contains("google.com/calendar/ical", StringComparison.OrdinalIgnoreCase);

    private async Task ReadIcs(List<CalEvent> events, List<CalendarInfo> calendars, List<string> excluded)
    {
        var sources = _app.Get<List<string>>(K.CalendarSources) ?? [];
        var green = Color.FromRgb(0x5E, 0xC8, 0x6B);
        foreach (var src in sources.Where(s => !string.IsNullOrWhiteSpace(s)).Take(20))
        {
            try
            {
                string text;
                if (LooksLikeUrl(src))
                {
                    var key = src.Trim();
                    if (IsGoogleCalendarAddress(key))
                    {
                        // The address holds a secret that must not sit in plain text in settings; never download or log it here.
                        _log.Warn("ICS source skipped: Google calendar addresses belong in the Google Calendar setting");
                        calendars.Add(new CalendarInfo("ics-note:google", Loc.T("Add Google calendars in the Google Calendar setting."), "ICS", green, "ics"));
                        continue;
                    }

                    var url = NormalizeIcsUrl(key);
                    if (url is null)
                    {
                        _log.Warn("ICS source skipped: not an https address");
                        calendars.Add(new CalendarInfo("ics-note:https", Loc.T("Use an https:// address for this calendar."), "ICS", green, "ics"));
                        continue;
                    }

                    if (!_icsCache.TryGetValue(key, out var cached) || !_lastIcs.TryGetValue(key, out var at) || (DateTime.UtcNow - at).TotalMinutes > 15)
                    {
                        cached = await DownloadIcsAsync(url);
                        _icsCache[key] = cached;
                        _lastIcs[key] = DateTime.UtcNow;
                    }

                    text = cached;
                }
                else if (!src.StartsWith(@"\\", StringComparison.Ordinal) && File.Exists(src))
                {
                    // A network path would make Windows send credentials to the named server, so only local files are read.
                    if (new FileInfo(src).Length > MaxIcsBytes)
                    {
                        _log.Warn("ICS file skipped: larger than 8 MB");
                        continue;
                    }

                    text = await File.ReadAllTextAsync(src);
                }
                else
                {
                    continue;
                }

                var name = LooksLikeUrl(src) ? "" : Path.GetFileNameWithoutExtension(src);
                var id = "ics:" + src;
                var shown = string.IsNullOrEmpty(name) ? Loc.T("Calendar") : name;
                calendars.Add(new CalendarInfo(id, shown, "ICS", green, "ics"));
                if (excluded.Contains(id))
                {
                    continue;
                }

                var from = _rangeStart;
                var to = _rangeEnd;
                var parsed = await Task.Run(() => IcsReader.Read(text, from, to, id, string.IsNullOrEmpty(name) ? "Calendar" : name, green, "ics", _log).ToList());
                events.AddRange(parsed);
            }
            catch (Exception ex)
            {
                // The exception text of a download can name the address, so only its type is logged.
                _log.Warn($"ICS source failed: {ex.GetType().Name}");
            }
        }
    }

    /// <summary>Downloads an ICS body over https with a 20 s limit and an 8 MB cap, reading the headers first.</summary>
    private async Task<string> DownloadIcsAsync(string url)
    {
        using var cts = new CancellationTokenSource(IcsTimeout);
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxIcsBytes)
        {
            throw new InvalidDataException("ICS body too large");
        }

        using var buffer = new MemoryStream();
        await using (var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false))
        {
            var chunk = new byte[81920];
            int n;
            while ((n = await stream.ReadAsync(chunk, cts.Token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + n > MaxIcsBytes)
                {
                    throw new InvalidDataException("ICS body too large");
                }

                buffer.Write(chunk, 0, n);
            }
        }

        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length).TrimStart('\uFEFF');
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

        if (UsesGoogle && _google.Feeds.Count > 0 && _google.NextDueUtc.ToLocalTime() < next)
        {
            next = _google.NextDueUtc.ToLocalTime();
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
        if (e.Source == "google" && e.WebUrl is { } web)
        {
            OpenLink(web);
            return;
        }

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

    /// <summary>Opens an https link (a video call or Google Calendar) in the default browser; anything else is ignored.</summary>
    public static void OpenLink(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // No browser to open it with; nothing sensible to do.
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        Microsoft.Win32.SystemEvents.TimeChanged -= OnClock;
        _app.Changed -= OnSetting;
        _google.Changed -= OnGoogleChanged;
        _google.RefreshRequested -= OnGoogleRefresh;
        _google.Dispose();
        _http.Dispose();
    }
}
