// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using Trayloom.Core;

namespace Trayloom.Features.Island;

/// <summary>How an iCalendar date or time is anchored.</summary>
internal enum IcsTimeKind
{
    /// <summary>A UTC instant (<c>…Z</c>).</summary>
    Utc,

    /// <summary>A wall-clock time with no zone, read in the local zone.</summary>
    Floating,

    /// <summary>A wall-clock time in the zone named by <c>TZID</c>.</summary>
    Zoned,

    /// <summary>A calendar date (all-day).</summary>
    Date,
}

/// <summary>A parsed date or time. <paramref name="Value"/> is UTC for <see cref="IcsTimeKind.Utc"/> and wall-clock time otherwise.</summary>
internal sealed record IcsTime(DateTime Value, IcsTimeKind Kind, string? Tzid);

/// <summary>One VEVENT, with the fields the island uses.</summary>
internal sealed record IcsEvent(
    string Uid,
    IcsTime? RecurrenceId,
    string Summary,
    string Location,
    string Description,
    IcsTime Start,
    IcsTime? End,
    TimeSpan? Duration,
    string? RRule,
    IReadOnlyList<IcsTime> ExDates,
    string Status,
    string? Conference,
    string? Url);

/// <summary>A parsed calendar file.</summary>
internal sealed record IcsCalendar(string? Name, string? DefaultTz, IReadOnlyList<IcsEvent> Events);

/// <summary>One concrete occurrence of an event, in local time. An all-day end is exclusive.</summary>
internal sealed record IcsOccurrence(IcsEvent Event, DateTime StartLocal, DateTime EndLocal, bool AllDay);

/// <summary>
/// A pure iCalendar reader: parsing plus expansion of recurring events into occurrences. It handles the parts Google Calendar uses
/// (time zones, DAILY/WEEKLY/MONTHLY/YEARLY rules, EXDATE, moved and cancelled instances); anything else degrades to the first
/// occurrence instead of showing wrong times.
/// </summary>
internal static class Ics
{
    /// <summary>Most periods walked for one event, so a pathological rule cannot stall a refresh.</summary>
    private const int MaxIterations = 2000;

    /// <summary>Most events read from one file; the rest are ignored so a hostile or huge file cannot exhaust memory.</summary>
    internal const int MaxEvents = 5000;

    /// <summary>Most occurrences produced from one file for the visible window.</summary>
    internal const int MaxOccurrences = 5000;

    /// <summary>Longest unfolded line kept; anything past it is dropped.</summary>
    internal const int MaxLineLength = 16384;

    /// <summary>Largest INTERVAL honoured; a rule asking for more shows its first occurrence only.</summary>
    internal const int MaxInterval = 1000;

    private const int MaxZones = 64;

    private static readonly char[] LineBreaks = ['\r', '\n'];

    private static readonly ConcurrentDictionary<string, TimeZoneInfo?> Zones = new(StringComparer.Ordinal);

    private static readonly Regex DurationRule = new(@"^([+-])?P(?:(\d+)W)?(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HashSet<string> SupportedRuleParts = new(StringComparer.Ordinal)
    {
        "FREQ", "INTERVAL", "COUNT", "UNTIL", "BYDAY", "BYMONTHDAY", "BYMONTH", "BYSETPOS", "WKST",
    };

    // ---------------------------------------------------------------- parsing

    /// <summary>Reads a calendar file. Never throws on malformed lines; events without a readable start are dropped.</summary>
    public static IcsCalendar Parse(string text)
    {
        string? name = null;
        string? defaultTz = null;
        var events = new List<IcsEvent>();
        EventBuilder? cur = null;

        // VALARM and VTIMEZONE bodies carry properties (TRIGGER, DTSTART, RRULE) that must not leak into the event.
        string? skipping = null;
        foreach (var line in Unfold(text))
        {
            if (!SplitLine(line, out var prop, out var pars, out var value))
            {
                continue;
            }

            if (skipping is not null)
            {
                if (prop == "END" && string.Equals(value.Trim(), skipping, StringComparison.OrdinalIgnoreCase))
                {
                    skipping = null;
                }

                continue;
            }

            if (prop == "BEGIN")
            {
                var comp = value.Trim().ToUpperInvariant();
                if (comp == "VEVENT")
                {
                    cur = new EventBuilder();
                }
                else if (comp is "VALARM" or "VTIMEZONE")
                {
                    skipping = comp;
                }

                continue;
            }

            if (prop == "END")
            {
                if (cur is not null && string.Equals(value.Trim(), "VEVENT", StringComparison.OrdinalIgnoreCase))
                {
                    if (events.Count < MaxEvents && cur.Build(events.Count) is { } ev)
                    {
                        events.Add(ev);
                    }

                    cur = null;
                }

                continue;
            }

            if (cur is null)
            {
                if (prop == "X-WR-CALNAME")
                {
                    name = Unescape(value).Trim();
                }
                else if (prop == "X-WR-TIMEZONE")
                {
                    defaultTz = NormalizeTzid(value);
                }

                continue;
            }

            cur.Add(prop, pars, value);
        }

        return new IcsCalendar(string.IsNullOrEmpty(name) ? null : name, defaultTz, events);
    }

    /// <summary>Parses an ISO 8601 duration such as <c>PT45M</c> or <c>P1DT2H</c>; null when it is not one.</summary>
    public static TimeSpan? ParseDuration(string value)
    {
        var m = DurationRule.Match(value.Trim());
        if (!m.Success)
        {
            return null;
        }

        double Part(int g) => m.Groups[g].Success ? double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture) : 0;
        var span = TimeSpan.FromDays((Part(2) * 7) + Part(3)) + TimeSpan.FromHours(Part(4)) + TimeSpan.FromMinutes(Part(5)) + TimeSpan.FromSeconds(Part(6));
        return m.Groups[1].Value == "-" ? -span : span;
    }

    /// <summary>Reads <c>20261110T090000Z</c>, <c>20261110T090000</c> (with a <c>TZID</c> parameter) or a date.</summary>
    public static IcsTime? ParseTime(IReadOnlyDictionary<string, string> pars, string value)
    {
        value = value.Trim();
        var isDate = value.Length == 8 || (pars.TryGetValue("VALUE", out var vt) && string.Equals(vt, "DATE", StringComparison.OrdinalIgnoreCase));
        if (isDate)
        {
            return DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                ? new IcsTime(day, IcsTimeKind.Date, null)
                : null;
        }

        var utc = value.EndsWith('Z');
        if (!DateTime.TryParseExact(value.TrimEnd('Z'), "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var wall))
        {
            return null;
        }

        if (utc)
        {
            return new IcsTime(DateTime.SpecifyKind(wall, DateTimeKind.Utc), IcsTimeKind.Utc, null);
        }

        return pars.TryGetValue("TZID", out var tzid) && NormalizeTzid(tzid) is { Length: > 0 } id
            ? new IcsTime(wall, IcsTimeKind.Zoned, id)
            : new IcsTime(wall, IcsTimeKind.Floating, null);
    }

    /// <summary>Strips quotes and the <c>/mozilla.org/20050126_1/</c> style prefix some producers put in front of the zone name.</summary>
    public static string NormalizeTzid(string raw)
    {
        var id = raw.Trim().Trim('"');
        if (id.StartsWith('/'))
        {
            var parts = id.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 && parts[0].Contains('.'))
            {
                return string.Join('/', parts[2..]);
            }

            return parts.Length >= 2 ? string.Join('/', parts[^2..]) : id.TrimStart('/');
        }

        return id;
    }

    /// <summary>Resolves a zone id (IANA or Windows); null when the system does not know it.</summary>
    public static TimeZoneInfo? FindZone(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 100)
        {
            return null;
        }

        if (Zones.TryGetValue(id, out var cached))
        {
            return cached;
        }

        var zone = Lookup(id);
        if (Zones.Count < MaxZones)
        {
            // The cache is bounded because the ids come from files anyone can publish.
            Zones.TryAdd(id, zone);
        }

        return zone;

        static TimeZoneInfo? Lookup(string name)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(name);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException or System.Security.SecurityException)
            {
            }

            try
            {
                return TimeZoneInfo.TryConvertIanaIdToWindowsId(name, out var windows) && windows is not null ? TimeZoneInfo.FindSystemTimeZoneById(windows) : null;
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException or System.Security.SecurityException)
            {
                return null;
            }
        }
    }

    // ---------------------------------------------------------------- expansion

    /// <summary>
    /// The occurrences that overlap [<paramref name="fromLocal"/>, <paramref name="toLocal"/>), in local time. Recurrences are expanded in the
    /// event's own zone so a 09:00 meeting stays at 09:00 across a daylight-saving change, then converted to <paramref name="local"/>.
    /// </summary>
    public static IEnumerable<IcsOccurrence> Expand(IcsCalendar c, DateTime fromLocal, DateTime toLocal, TimeZoneInfo local, ILog? log = null)
    {
        var overrides = c.Events.Where(e => e.RecurrenceId is not null).ToList();
        var overridden = new HashSet<(string Uid, long Key)>();
        foreach (var o in overrides)
        {
            overridden.Add((o.Uid, InstantKey(o.RecurrenceId!, c.DefaultTz, local)));
        }

        var result = new List<IcsOccurrence>();
        foreach (var e in c.Events)
        {
            if (result.Count >= MaxOccurrences)
            {
                break;
            }

            if (e.RecurrenceId is null)
            {
                if (!string.Equals(e.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandMaster(e, c, fromLocal, toLocal, local, overridden, log, result);
                }
            }
            else if (!string.Equals(e.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            {
                // A moved instance stands on its own: its start and end are the new ones.
                AddIfVisible(Make(e, e.Start.Value, c.DefaultTz, local), fromLocal, toLocal, result);
            }
        }

        return result;
    }

    private static void ExpandMaster(IcsEvent e, IcsCalendar c, DateTime fromLocal, DateTime toLocal, TimeZoneInfo local, HashSet<(string Uid, long Key)> overridden, ILog? log, List<IcsOccurrence> result)
    {
        var zone = ZoneFor(e.Start, c.DefaultTz, local);
        var exdates = new HashSet<long>();
        foreach (var x in e.ExDates)
        {
            exdates.Add(InstantKey(x, c.DefaultTz, local));
        }

        IEnumerable<DateTime> starts = [e.Start.Value];
        if (!string.IsNullOrWhiteSpace(e.RRule))
        {
            var rule = Rule.Parse(e.RRule);
            if (rule is null || rule.Unsupported)
            {
                log?.Info("[island] Calendar: unsupported RRULE part, showing the first occurrence only");
            }
            else
            {
                var length = Make(e, e.Start.Value, c.DefaultTz, local) is { } first ? first.EndLocal - first.StartLocal : TimeSpan.FromHours(1);
                var lowWall = fromLocal.AddDays(-2) - length;
                var highWall = toLocal.AddDays(2);
                starts = Generate(rule, e.Start.Value, lowWall, highWall, zone);
            }
        }

        foreach (var wall in starts)
        {
            if (result.Count >= MaxOccurrences)
            {
                break;
            }

            var key = InstantKey(e.Start with { Value = wall }, c.DefaultTz, local);
            if (exdates.Contains(key) || overridden.Contains((e.Uid, key)))
            {
                continue;
            }

            AddIfVisible(Make(e, wall, c.DefaultTz, local), fromLocal, toLocal, result);
        }
    }

    private static void AddIfVisible(IcsOccurrence? occ, DateTime from, DateTime to, List<IcsOccurrence> result)
    {
        if (occ is null)
        {
            return;
        }

        if (occ.StartLocal < to && (occ.EndLocal > from || (occ.EndLocal == occ.StartLocal && occ.StartLocal >= from)))
        {
            result.Add(occ);
        }
    }

    /// <summary>Builds the occurrence of <paramref name="e"/> that starts at <paramref name="startWall"/> (in the event's zone).</summary>
    private static IcsOccurrence? Make(IcsEvent e, DateTime startWall, string? calTz, TimeZoneInfo local)
    {
        try
        {
            if (e.Start.Kind == IcsTimeKind.Date)
            {
                var days = AllDayLength(e);
                return new IcsOccurrence(e, startWall.Date, startWall.Date.AddDays(days), true);
            }

            var startUtc = e.Start.Kind == IcsTimeKind.Utc ? DateTime.SpecifyKind(startWall, DateTimeKind.Utc) : WallToUtc(startWall, ZoneFor(e.Start, calTz, local));
            var endUtc = startUtc + Length(e, calTz, local);
            return new IcsOccurrence(e, TimeZoneInfo.ConvertTimeFromUtc(startUtc, local), TimeZoneInfo.ConvertTimeFromUtc(endUtc, local), false);
        }
        catch (ArgumentException)
        {
            // A date at the edge of the supported range; nothing sensible to show.
            return null;
        }
    }

    private static int AllDayLength(IcsEvent e)
    {
        if (e.End is { } end)
        {
            return Math.Max(1, (int)(end.Value.Date - e.Start.Value.Date).TotalDays);
        }

        return e.Duration is { } d ? Math.Max(1, (int)Math.Ceiling(d.TotalDays)) : 1;
    }

    private static TimeSpan Length(IcsEvent e, string? calTz, TimeZoneInfo local)
    {
        if (e.Duration is { } d)
        {
            return d < TimeSpan.Zero ? TimeSpan.Zero : d;
        }

        if (e.End is { } end)
        {
            var span = Instant(end, calTz, local) - Instant(e.Start, calTz, local);
            return span < TimeSpan.Zero ? TimeSpan.Zero : span;
        }

        // The standard says "no end means no length", but a zero-length event would vanish from the island.
        return TimeSpan.FromHours(1);
    }

    private static DateTime Instant(IcsTime t, string? calTz, TimeZoneInfo local) =>
        t.Kind == IcsTimeKind.Utc ? DateTime.SpecifyKind(t.Value, DateTimeKind.Utc) : t.Kind == IcsTimeKind.Date ? DateTime.SpecifyKind(t.Value, DateTimeKind.Utc) : WallToUtc(t.Value, ZoneFor(t, calTz, local));

    private static long InstantKey(IcsTime t, string? calTz, TimeZoneInfo local)
    {
        if (t.Kind == IcsTimeKind.Date)
        {
            return t.Value.Date.Ticks;
        }

        return Instant(t, calTz, local).Ticks;
    }

    /// <summary>The zone a time is read in: its TZID, else the calendar's default, else the local zone.</summary>
    private static TimeZoneInfo ZoneFor(IcsTime t, string? calTz, TimeZoneInfo local)
    {
        switch (t.Kind)
        {
            case IcsTimeKind.Utc:
                return TimeZoneInfo.Utc;
            case IcsTimeKind.Zoned:
                return FindZone(t.Tzid) ?? FindZone(calTz) ?? local;
            default:
                return local;
        }
    }

    private static DateTime WallToUtc(DateTime wall, TimeZoneInfo zone)
    {
        wall = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(wall))
        {
            // Inside the spring-forward gap: the clock skips this time, so use the same time after the jump.
            wall = wall.AddHours(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(wall, zone);
    }

    // ---------------------------------------------------------------- recurrence

    /// <summary>The start wall-clock times of a rule, ascending, from the first one up to <paramref name="highWall"/>.</summary>
    private static IEnumerable<DateTime> Generate(Rule rule, DateTime start, DateTime lowWall, DateTime highWall, TimeZoneInfo zone)
    {
        DateTime? until = null;
        if (rule.Until is { } u)
        {
            until = u.Kind == IcsTimeKind.Utc ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(u.Value, DateTimeKind.Utc), zone) : u.Value;
        }

        var untilIsDate = rule.Until?.Kind == IcsTimeKind.Date;
        long first = 0;
        if (rule.Count == 0)
        {
            // Without COUNT nothing before the visible window matters, so skip ahead instead of walking years of old occurrences.
            first = Math.Max(0, (PeriodsBetween(rule, start, lowWall) / rule.Interval) - 1);
        }

        var produced = 0;
        for (long n = first, steps = 0; steps < MaxIterations; n++, steps++)
        {
            List<DateTime> candidates;
            try
            {
                candidates = Period(rule, start, n * rule.Interval);
            }
            catch (ArgumentOutOfRangeException)
            {
                yield break;
            }

            foreach (var cand in candidates)
            {
                if (cand < start)
                {
                    continue;
                }

                if (until is { } limit && (untilIsDate ? cand.Date > limit.Date : cand > limit))
                {
                    yield break;
                }

                produced++;
                if (rule.Count > 0 && produced > rule.Count)
                {
                    yield break;
                }

                if (cand > highWall)
                {
                    yield break;
                }

                yield return cand;
            }

            // An empty period (a month without a 31st, say) never reaches the ceiling check above, so look ahead here.
            if (candidates.Count == 0 && NextStartBeyond(rule, start, (n + 1) * rule.Interval, highWall))
            {
                yield break;
            }
        }
    }

    private static long PeriodsBetween(Rule rule, DateTime start, DateTime to)
    {
        if (to <= start)
        {
            return 0;
        }

        switch (rule.Freq)
        {
            case "DAILY":
                return (long)(to.Date - start.Date).TotalDays;
            case "WEEKLY":
                return (long)((to.Date - WeekStart(start.Date, rule.WeekStart)).TotalDays / 7);
            case "MONTHLY":
                return ((to.Year - start.Year) * 12L) + to.Month - start.Month;
            default:
                return to.Year - start.Year;
        }
    }

    private static bool NextStartBeyond(Rule rule, DateTime start, long units, DateTime ceiling)
    {
        try
        {
            return PeriodStart(rule, start, units) > ceiling;
        }
        catch (ArgumentOutOfRangeException)
        {
            return true;
        }
    }

    private static DateTime PeriodStart(Rule rule, DateTime start, long units) => rule.Freq switch
    {
        "DAILY" => start.Date.AddDays(units),
        "WEEKLY" => WeekStart(start.Date, rule.WeekStart).AddDays(7 * units),
        "MONTHLY" => new DateTime(start.Year, start.Month, 1).AddMonths((int)units),
        _ => new DateTime(start.Year, 1, 1).AddYears((int)units),
    };

    /// <summary>The candidates of the period that begins <paramref name="units"/> days/weeks/months/years after the first one.</summary>
    private static List<DateTime> Period(Rule rule, DateTime start, long units)
    {
        var tod = start.TimeOfDay;
        var result = new List<DateTime>();
        switch (rule.Freq)
        {
            case "DAILY":
            {
                var day = start.Date.AddDays(units);
                var okDay = rule.ByDay.Count == 0 || rule.ByDay.Any(b => b.Day == day.DayOfWeek);
                var okMonthDay = rule.ByMonthDay.Count == 0 || rule.ByMonthDay.Contains(day.Day) || rule.ByMonthDay.Contains(day.Day - DateTime.DaysInMonth(day.Year, day.Month) - 1);
                var okMonth = rule.ByMonth.Count == 0 || rule.ByMonth.Contains(day.Month);
                if (okDay && okMonthDay && okMonth)
                {
                    result.Add(day + tod);
                }

                break;
            }

            case "WEEKLY":
            {
                var weekStart = WeekStart(start.Date, rule.WeekStart).AddDays(7 * units);
                var days = rule.ByDay.Count == 0 ? [start.DayOfWeek] : rule.ByDay.Select(b => b.Day).Distinct().ToList();
                foreach (var off in days.Select(d => ((int)d - (int)rule.WeekStart + 7) % 7).OrderBy(x => x))
                {
                    var cand = weekStart.AddDays(off);
                    if (rule.ByMonth.Count == 0 || rule.ByMonth.Contains(cand.Month))
                    {
                        result.Add(cand + tod);
                    }
                }

                break;
            }

            case "MONTHLY":
            {
                var month = new DateTime(start.Year, start.Month, 1).AddMonths((int)units);
                if (rule.ByMonth.Count == 0 || rule.ByMonth.Contains(month.Month))
                {
                    result.AddRange(MonthDays(rule, month.Year, month.Month, start.Day).Select(d => new DateTime(month.Year, month.Month, d) + tod));
                    result = SetPos(rule, result);
                }

                break;
            }

            default:
            {
                var year = start.Year + (int)units;
                var months = rule.ByMonth.Count == 0 ? [start.Month] : rule.ByMonth.OrderBy(x => x).ToList();
                foreach (var m in months)
                {
                    result.AddRange(MonthDays(rule, year, m, start.Day).Select(d => new DateTime(year, m, d) + tod));
                }

                result = SetPos(rule, result);
                break;
            }
        }

        return result;
    }

    /// <summary>The days of one month a rule selects: BYMONTHDAY and BYDAY (with ordinals), their intersection when both are given.</summary>
    private static List<int> MonthDays(Rule rule, int year, int month, int startDay)
    {
        var dim = DateTime.DaysInMonth(year, month);
        List<int>? byMonthDay = null;
        List<int>? byDay = null;
        if (rule.ByMonthDay.Count > 0)
        {
            byMonthDay = [.. rule.ByMonthDay.Select(d => d > 0 ? d : dim + d + 1).Where(d => d >= 1 && d <= dim)];
        }

        if (rule.ByDay.Count > 0)
        {
            byDay = [];
            foreach (var (ord, dow) in rule.ByDay)
            {
                var matching = Enumerable.Range(1, dim).Where(d => new DateTime(year, month, d).DayOfWeek == dow).ToList();
                if (ord == 0)
                {
                    byDay.AddRange(matching);
                }
                else
                {
                    var idx = ord > 0 ? ord - 1 : matching.Count + ord;
                    if (idx >= 0 && idx < matching.Count)
                    {
                        byDay.Add(matching[idx]);
                    }
                }
            }
        }

        List<int> days;
        if (byMonthDay is not null && byDay is not null)
        {
            days = [.. byMonthDay.Intersect(byDay)];
        }
        else if (byMonthDay is not null || byDay is not null)
        {
            days = byMonthDay ?? byDay!;
        }
        else
        {
            // No selector: the same day number as the first occurrence; months that lack it (31st, 30 Feb) are skipped.
            days = startDay <= dim ? [startDay] : [];
        }

        return [.. days.Distinct().OrderBy(x => x)];
    }

    private static List<DateTime> SetPos(Rule rule, List<DateTime> sorted)
    {
        if (rule.BySetPos.Count == 0 || sorted.Count == 0)
        {
            return sorted;
        }

        var picked = new List<DateTime>();
        foreach (var pos in rule.BySetPos)
        {
            var idx = pos > 0 ? pos - 1 : sorted.Count + pos;
            if (idx >= 0 && idx < sorted.Count)
            {
                picked.Add(sorted[idx]);
            }
        }

        return [.. picked.Distinct().OrderBy(x => x)];
    }

    private static DateTime WeekStart(DateTime date, DayOfWeek wkst) => date.AddDays(-(((int)date.DayOfWeek - (int)wkst + 7) % 7));

    /// <summary>A recurrence rule reduced to the parts that are supported.</summary>
    private sealed class Rule
    {
        public string Freq { get; private init; } = "";

        public int Interval { get; private init; } = 1;

        public int Count { get; private init; }

        public IcsTime? Until { get; private init; }

        public DayOfWeek WeekStart { get; private init; } = DayOfWeek.Monday;

        public List<(int Ordinal, DayOfWeek Day)> ByDay { get; private init; } = [];

        public List<int> ByMonthDay { get; private init; } = [];

        public List<int> ByMonth { get; private init; } = [];

        public List<int> BySetPos { get; private init; } = [];

        public bool Unsupported { get; private init; }

        public static Rule? Parse(string text)
        {
            var parts = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var piece in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = piece.IndexOf('=');
                if (eq > 0)
                {
                    parts[piece[..eq].Trim().ToUpperInvariant()] = piece[(eq + 1)..].Trim();
                }
            }

            if (!parts.TryGetValue("FREQ", out var freq))
            {
                return null;
            }

            freq = freq.ToUpperInvariant();
            var unsupported = freq is not ("DAILY" or "WEEKLY" or "MONTHLY" or "YEARLY") || parts.Keys.Any(k => !SupportedRuleParts.Contains(k));
            unsupported |= freq == "WEEKLY" && (parts.ContainsKey("BYMONTHDAY") || parts.ContainsKey("BYSETPOS"));
            unsupported |= freq is "DAILY" or "WEEKLY" && parts.ContainsKey("BYSETPOS");
            unsupported |= freq == "YEARLY" && parts.ContainsKey("BYDAY") && !parts.ContainsKey("BYMONTH");

            var interval = parts.TryGetValue("INTERVAL", out var iv) && int.TryParse(iv, NumberStyles.None, CultureInfo.InvariantCulture, out var i) ? Math.Clamp(i, 1, MaxInterval) : 1;
            // An INTERVAL that is too large (or does not parse) would silently change the schedule, so show the first occurrence only.
            unsupported |= parts.ContainsKey("INTERVAL") && (!int.TryParse(parts["INTERVAL"], NumberStyles.None, CultureInfo.InvariantCulture, out var rawInterval) || rawInterval > MaxInterval);
            var count = parts.TryGetValue("COUNT", out var cv) && int.TryParse(cv, NumberStyles.None, CultureInfo.InvariantCulture, out var cn) ? cn : 0;
            IcsTime? until = null;
            if (parts.TryGetValue("UNTIL", out var uv))
            {
                until = ParseTime(new Dictionary<string, string>(), uv);
                unsupported |= until is null;
            }

            var byDay = new List<(int, DayOfWeek)>();
            if (parts.TryGetValue("BYDAY", out var bd))
            {
                foreach (var token in bd.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (token.Length >= 2 && DayOf(token[^2..]) is { } dow && (token.Length == 2 || int.TryParse(token[..^2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _)))
                    {
                        byDay.Add((token.Length == 2 ? 0 : int.Parse(token[..^2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture), dow));
                    }
                    else
                    {
                        unsupported = true;
                    }
                }
            }

            // Ordinals only make sense for months (and BYMONTH years); in a weekly rule they are ignored, as the standard allows.
            if (freq is "WEEKLY" or "DAILY")
            {
                byDay = [.. byDay.Select(b => (0, b.Item2))];
            }

            return new Rule
            {
                Freq = freq,
                Interval = interval,
                Count = count,
                Until = until,
                WeekStart = parts.TryGetValue("WKST", out var wk) && DayOf(wk) is { } w ? w : DayOfWeek.Monday,
                ByDay = byDay,
                ByMonthDay = Ints(parts, "BYMONTHDAY", -31, 31, ref unsupported),
                ByMonth = Ints(parts, "BYMONTH", 1, 12, ref unsupported),
                BySetPos = Ints(parts, "BYSETPOS", -366, 366, ref unsupported),
                Unsupported = unsupported,
            };
        }

        private static List<int> Ints(Dictionary<string, string> parts, string key, int min, int max, ref bool unsupported)
        {
            var list = new List<int>();
            if (!parts.TryGetValue(key, out var text))
            {
                return list;
            }

            foreach (var token in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v) && v >= min && v <= max && v != 0)
                {
                    list.Add(v);
                }
                else
                {
                    unsupported = true;
                }
            }

            return list;
        }

        private static DayOfWeek? DayOf(string s) => s.ToUpperInvariant() switch
        {
            "MO" => DayOfWeek.Monday,
            "TU" => DayOfWeek.Tuesday,
            "WE" => DayOfWeek.Wednesday,
            "TH" => DayOfWeek.Thursday,
            "FR" => DayOfWeek.Friday,
            "SA" => DayOfWeek.Saturday,
            "SU" => DayOfWeek.Sunday,
            _ => null,
        };
    }

    // ---------------------------------------------------------------- lines

    /// <summary>Joins folded lines: a line that starts with a space or tab continues the previous one.</summary>
    private static IEnumerable<string> Unfold(string text)
    {
        var current = new System.Text.StringBuilder();
        var started = false;
        var pos = 0;
        while (true)
        {
            var end = text.IndexOfAny(LineBreaks, pos);
            var line = end < 0 ? text[pos..] : text.Substring(pos, end - pos);
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
            {
                AppendCapped(current, line, 1);
                started = true;
            }
            else
            {
                if (started)
                {
                    yield return current.ToString();
                }

                current.Clear();
                AppendCapped(current, line, 0);
                started = true;
            }

            if (end < 0)
            {
                break;
            }

            pos = end + (text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1);
        }

        if (started)
        {
            yield return current.ToString();
        }
    }

    private static void AppendCapped(System.Text.StringBuilder sb, string line, int from)
    {
        var room = MaxLineLength - sb.Length;
        if (room > 0 && from < line.Length)
        {
            sb.Append(line, from, Math.Min(room, line.Length - from));
        }
    }

    /// <summary>Splits <c>NAME;PARAM=x:value</c> at the first colon that is not inside quotes.</summary>
    private static bool SplitLine(string line, out string name, out Dictionary<string, string> pars, out string value)
    {
        name = "";
        value = "";
        pars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var quoted = false;
        var colon = -1;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
            {
                quoted = !quoted;
            }
            else if (line[i] == ':' && !quoted)
            {
                colon = i;
                break;
            }
        }

        if (colon <= 0)
        {
            return false;
        }

        value = line[(colon + 1)..];
        var head = line[..colon];
        var pieces = SplitOutsideQuotes(head, ';');
        name = pieces[0].Trim().ToUpperInvariant();
        for (var i = 1; i < pieces.Count; i++)
        {
            var eq = pieces[i].IndexOf('=');
            if (eq > 0)
            {
                pars[pieces[i][..eq].Trim()] = pieces[i][(eq + 1)..].Trim().Trim('"');
            }
        }

        return name.Length > 0;
    }

    private static List<string> SplitOutsideQuotes(string text, char separator)
    {
        var parts = new List<string>();
        var quoted = false;
        var begin = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '"')
            {
                quoted = !quoted;
            }
            else if (text[i] == separator && !quoted)
            {
                parts.Add(text[begin..i]);
                begin = i + 1;
            }
        }

        parts.Add(text[begin..]);
        return parts;
    }

    /// <summary>Undoes iCalendar text escaping (<c>\n</c>, <c>\,</c>, <c>\;</c>, <c>\\</c>) in one pass so <c>\\n</c> stays a backslash and an n.</summary>
    private static string Unescape(string s)
    {
        if (s.IndexOf('\\') < 0)
        {
            return s;
        }

        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                var next = s[i + 1];
                if (next is 'n' or 'N')
                {
                    sb.Append('\n');
                    i++;
                    continue;
                }

                if (next is ',' or ';' or '\\')
                {
                    sb.Append(next);
                    i++;
                    continue;
                }
            }

            sb.Append(s[i]);
        }

        return sb.ToString();
    }

    /// <summary>Collects the properties of one VEVENT.</summary>
    private sealed class EventBuilder
    {
        private readonly List<IcsTime> _exDates = [];
        private string _uid = "";
        private IcsTime? _recurrenceId;
        private string _summary = "";
        private string _location = "";
        private string _description = "";
        private IcsTime? _start;
        private IcsTime? _end;
        private TimeSpan? _duration;
        private string? _rrule;
        private string _status = "";
        private string? _conference;
        private string? _url;

        public void Add(string prop, Dictionary<string, string> pars, string value)
        {
            switch (prop)
            {
                case "UID":
                    _uid = value.Trim();
                    break;
                case "RECURRENCE-ID":
                    _recurrenceId = ParseTime(pars, value);
                    break;
                case "SUMMARY":
                    _summary = Unescape(value);
                    break;
                case "LOCATION":
                    _location = Unescape(value);
                    break;
                case "DESCRIPTION":
                    _description = Unescape(value);
                    break;
                case "DTSTART":
                    _start = ParseTime(pars, value);
                    break;
                case "DTEND":
                    _end = ParseTime(pars, value);
                    break;
                case "DURATION":
                    _duration = ParseDuration(value);
                    break;
                case "RRULE":
                    _rrule = value.Trim();
                    break;
                case "EXDATE":
                    foreach (var piece in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (ParseTime(pars, piece) is { } t)
                        {
                            _exDates.Add(t);
                        }
                    }

                    break;
                case "STATUS":
                    _status = value.Trim().ToUpperInvariant();
                    break;
                case "X-GOOGLE-CONFERENCE":
                    _conference = value.Trim();
                    break;
                case "URL":
                    _url = value.Trim();
                    break;
            }
        }

        /// <summary>The finished event, or null without a readable start. A missing UID gets a stable stand-in so overrides still pair up.</summary>
        public IcsEvent? Build(int index)
        {
            if (_start is null)
            {
                return null;
            }

            var uid = _uid.Length > 0 ? _uid : "no-uid-" + index.ToString(CultureInfo.InvariantCulture);
            return new IcsEvent(uid, _recurrenceId, _summary, _location, _description, _start, _end, _duration, _rrule, _exDates, _status, _conference, _url);
        }
    }
}

/// <summary>Turns parsed calendars into the island's <see cref="CalEvent"/> list.</summary>
internal static class IcsReader
{
    /// <summary>Parses <paramref name="text"/> and returns its events inside the window.</summary>
    public static IEnumerable<CalEvent> Read(string text, DateTime from, DateTime to, string calId, string calName, Color color, string source, ILog? log = null) =>
        ToEvents(Ics.Parse(text), from, to, calId, calName, color, source, log);

    /// <summary>The events of <paramref name="cal"/> inside the window. Ids are <c>{calId}|{uid}</c>; Google events also get a day-view link.</summary>
    public static List<CalEvent> ToEvents(IcsCalendar cal, DateTime from, DateTime to, string calId, string calName, Color color, string source, ILog? log = null)
    {
        var list = new List<CalEvent>();
        foreach (var occ in Ics.Expand(cal, from, to, TimeZoneInfo.Local, log))
        {
            var e = occ.Event;
            var id = $"{calId}|{e.Uid}";
            list.Add(new CalEvent
            {
                Id = id,
                // A series shares one id, so the countdown key (LocalId plus start) is what tells its occurrences apart.
                LocalId = id,
                Title = e.Summary,
                Start = occ.StartLocal,
                End = occ.EndLocal,
                AllDay = occ.AllDay,
                Location = e.Location,
                CalendarId = calId,
                CalendarName = calName,
                Color = color,
                Source = source,
                JoinUrl = GoogleRules.MeetingLink(e.Conference, e.Url, e.Location, e.Description),
                WebUrl = source == "google" ? GoogleRules.DayUrl(occ.StartLocal) : null,
            });
        }

        return list;
    }
}
