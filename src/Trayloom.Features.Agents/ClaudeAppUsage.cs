// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;

namespace Trayloom.Features.Agents;

/// <summary>
/// The Claude app checks the plan's limits itself and keeps a month of them on this PC (a percentage per window every few minutes)
/// in <c>plan-usage-history.json</c>. Trayloom reads that file, without a sign-in, when Claude Code cannot check the account itself.
/// A version or an entry this reader does not know is left out rather than guessed.
/// </summary>
internal static class ClaudeAppUsage
{
    public const string DownloadUrl = "https://claude.ai/download";

    /// <summary>The app checks every five to fifteen minutes; a reading older than this has missed a check.</summary>
    public static readonly TimeSpan Freshness = TimeSpan.FromMinutes(30);

    private const int MaximumSize = 4 << 20;

    public sealed record Sample(DateTime Date, string? Organization, Dictionary<string, double> Used);

    private static readonly (string Key, LimitKind Kind, int Minutes, string? Scope)[] Windows =
    [
        ("fh", LimitKind.Session, 300, null), ("sd", LimitKind.Weekly, 10_080, null),
        ("so", LimitKind.Weekly, 10_080, "Opus"), ("sn", LimitKind.Weekly, 10_080, "Sonnet"),
    ];

    /// <summary>The newest history file: <c>%LOCALAPPDATA%\Claude-Data\claude_*\</c> (MSIX) or <c>%APPDATA%\Claude\</c>.</summary>
    public static string? HistoryPath()
    {
        var candidates = new List<string>();
        try
        {
            var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Claude-Data");
            if (Directory.Exists(local))
            {
                foreach (var dir in Directory.EnumerateDirectories(local, "claude_*"))
                {
                    candidates.Add(Path.Combine(dir, "plan-usage-history.json"));
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "plan-usage-history.json"));
        string? best = null;
        var bestTime = DateTime.MinValue;
        foreach (var c in candidates)
        {
            try
            {
                var info = new FileInfo(c);
                if (info.Exists && info.LastWriteTimeUtc >= bestTime)
                {
                    best = c;
                    bestTime = info.LastWriteTimeUtc;
                }
            }
            catch (IOException)
            {
            }
        }

        return best;
    }

    /// <summary>Whether the Claude desktop app looks installed (its history folder or its MSIX package exists).</summary>
    public static bool AppInstalled()
    {
        try
        {
            if (HistoryPath() is not null)
            {
                return true;
            }

            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Directory.Exists(Path.Combine(local, "Packages")) && Directory.EnumerateDirectories(Path.Combine(local, "Packages"), "Claude_*").Any()
                   || File.Exists(Path.Combine(local, "AnthropicClaude", "claude.exe"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static DateTime? LastCheck() => ReadFile(HistoryPath())?.LastOrDefault()?.Date;

    public static List<Sample>? ReadFile(string? path)
    {
        if (path is null)
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > MaximumSize)
            {
                return null;
            }

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return Samples(ms.ToArray());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Readings oldest first; null for a file this reader does not know.</summary>
    public static List<Sample>? Samples(byte[] data)
    {
        if (data.Length > MaximumSize)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            if (root.Prop("version").Num() is not { } version || (version != 1 && version != 2) || root.Prop("samples") is not { ValueKind: JsonValueKind.Array } entries)
            {
                return null;
            }

            var result = new List<Sample>();
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.Prop("t").Num() is not { } ms || ms <= 0)
                {
                    continue;
                }

                // The first version kept its windows beside the time.
                var values = version == 1 ? entry : entry.Prop("u") ?? default;
                var used = new Dictionary<string, double>();
                foreach (var w in Windows)
                {
                    if (values.ValueKind == JsonValueKind.Object && values.Prop(w.Key).Num() is { } n)
                    {
                        used[w.Key] = Math.Min(100, Math.Max(0, n));
                    }
                }

                var date = AgentTime.Epoch.AddMilliseconds(ms);
                result.Add(new Sample(date, entry.Str("org"), used));
            }

            return result.OrderBy(s => s.Date).ToList();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The latest reading as limit windows. The file keeps percentages, not renewal times: a session renews five hours after its first
    /// request, which the history brackets and Claude Code's own first request can narrow, and a week renews every seven days on the
    /// schedule its drops keep.
    /// </summary>
    public static AgentLimits? Limits(List<Sample> allSamples, DateTime now, DateTime? sessionStart = null, string? organization = null)
    {
        var samples = Readings(allSamples, organization);
        if (samples.Count == 0)
        {
            return null;
        }

        var latest = samples[^1];
        if (latest.Date > now.AddSeconds(300) || now - latest.Date >= TimeSpan.FromDays(7))
        {
            return null;
        }

        var history = samples.Where(s => s.Organization == latest.Organization).ToList();
        var result = new List<LimitWindow>();
        foreach (var window in Windows)
        {
            if (!latest.Used.TryGetValue(window.Key, out var used))
            {
                continue;
            }

            var length = TimeSpan.FromMinutes(window.Minutes);
            // An old reading says nothing about a session that renewed since.
            if (window.Kind == LimitKind.Session && now - latest.Date >= length)
            {
                continue;
            }

            (DateTime Latest, DateTime? Estimate)? session = window.Kind == LimitKind.Session ? SessionEnd(history, used, sessionStart, length) : null;
            var resets = window.Kind == LimitKind.Session ? session?.Latest : Renewal(history, window.Key, length, latest.Date);
            // A window that renewed after the reading has spent an unknown amount since, and without a date a day-old week may have too.
            if (resets is { } r && r <= now)
            {
                continue;
            }

            if (resets is null && now - latest.Date >= TimeSpan.FromHours(24))
            {
                continue;
            }

            result.Add(new LimitWindow("claude." + window.Key, window.Kind, window.Minutes, window.Scope, used, resets, session?.Estimate) { Active = used > 0 });
        }

        return result.Count == 0 ? null : new AgentLimits(AgentProvider.Claude, result, latest.Date, LimitSource.ClaudeApp)
        {
            Origin = LimitOrigins.ClaudeDesktop,
            // The newest sample is what says whether the account has a 5-hour limit right now; an older one that had it does not.
            SessionReported = latest.Used.ContainsKey("fh"),
        };
    }

    /// <summary>Where Claude Code's own first request places the session the newest reading saw.</summary>
    public static DateTime? SessionStart(IReadOnlyList<UsageRecord> records, List<Sample> samples, string? organization = null)
    {
        var readings = Readings(samples, organization);
        if (readings.Count == 0)
        {
            return null;
        }

        var reading = readings[^1].Date;
        var recent = records.Where(r => r.Provider == AgentProvider.Claude && r.Date <= reading && reading - r.Date < AgentUsageSummary.BlockHistory).ToList();
        return AgentUsageSummary.CurrentBlock(recent, reading)?.Start;
    }

    /// <summary>The app may be signed in to another account than Claude Code; its limits say nothing about this one.</summary>
    private static List<Sample> Readings(List<Sample> samples, string? organization) =>
        organization is null ? samples : samples.Where(s => s.Organization is null || s.Organization == organization).ToList();

    private static (DateTime Latest, DateTime? Estimate)? SessionEnd(List<Sample> history, double used, DateTime? start, TimeSpan length)
    {
        if (used <= 0 || history.Count == 0)
        {
            return null;
        }

        var first = history.Count - 1;
        // The run of readings above zero that ends with the latest one. A drop is a renewal too.
        while (first > 0 && history[first - 1].Used.TryGetValue("fh", out var before) && before > 0
               && before <= (history[first].Used.GetValueOrDefault("fh")) + 1
               && history[first].Date - history[first - 1].Date < length)
        {
            first--;
        }

        var latest = history[first].Date;
        var earliest = first > 0 ? history[first - 1].Date : latest - length;
        if (start is { } s && s > earliest && s <= latest)
        {
            return (s + length, null);
        }

        var estimate = UnseenStart(earliest, latest, first > 0, length);
        // The midpoint can precede the real renewal. Keep the latest possible end for expiring usage and alerts.
        return (latest + length, estimate < latest ? estimate + length : null);
    }

    /// <summary>Where a session began when only the readings place it.</summary>
    private static DateTime UnseenStart(DateTime earliest, DateTime latest, bool bracketed, TimeSpan length)
    {
        if (!bracketed || latest - earliest <= Freshness)
        {
            return latest;
        }

        var lower = earliest > latest - length ? earliest : latest - length;
        return lower + (latest - lower) / 2;
    }

    /// <summary>The span a renewal lies in: after the earlier reading, which still shows the old usage, and no later than the later one.</summary>
    private readonly record struct Drop(DateTime Earliest, DateTime Latest)
    {
        /// <summary>Allowances renew on the hour, when the span holds one.</summary>
        public DateTime Moment
        {
            get
            {
                var next = HourOf(Earliest).AddHours(1);
                return next <= Latest ? next : Latest;
            }
        }

        public Drop? Overlap(Drop other, TimeSpan shift = default)
        {
            var start = Earliest > other.Earliest + shift ? Earliest : other.Earliest + shift;
            var end = Latest < other.Latest + shift ? Latest : other.Latest + shift;
            return start < end ? new Drop(start, end) : null;
        }
    }

    /// <summary>Allowances renew on the hour in UTC.</summary>
    public static DateTime HourOf(DateTime d) => new(d.Ticks - d.Ticks % TimeSpan.TicksPerHour, DateTimeKind.Utc);

    /// <summary>The first renewal after <paramref name="reading"/>. A week renews on a fixed schedule; drops confirmed whole weeks apart fix it.</summary>
    private static DateTime? Renewal(List<Sample> history, string key, TimeSpan length, DateTime reading)
    {
        var drops = new List<Drop>();
        for (var i = 1; i < history.Count; i++)
        {
            if (history[i - 1].Used.TryGetValue(key, out var before) && history[i].Used.TryGetValue(key, out var after) && after + 1 < before)
            {
                drops.Add(new Drop(history[i - 1].Date, history[i].Date));
            }
        }

        if (drops.Count == 0)
        {
            return null;
        }

        var moment = drops[^1].Moment;
        for (var index = drops.Count - 1; index >= 0; index--)
        {
            var spans = new List<Drop>();
            for (var j = 0; j < drops.Count; j++)
            {
                if (j != index && Confirmation(drops[index], drops[j], length) is { } c)
                {
                    spans.Add(c);
                }
            }

            if (spans.Count == 0)
            {
                continue;
            }

            // Partners that disagree on where the renewal lies leave the newest to speak.
            Drop? agreed = drops[index];
            foreach (var s in spans)
            {
                agreed = agreed?.Overlap(s);
            }

            var confirmed = (agreed ?? spans[^1]).Moment;
            if (!Missed(confirmed, history, drops, key, length, reading))
            {
                moment = confirmed;
            }

            break;
        }

        while (moment <= reading)
        {
            moment += length;
        }

        return moment;
    }

    /// <summary>Whether a renewal the schedule places went by in plain sight: readings within the hour on both sides show no drop.</summary>
    private static bool Missed(DateTime moment, List<Sample> history, List<Drop> drops, string key, TimeSpan length, DateTime until)
    {
        var next = moment + length;
        while (next <= until)
        {
            var start = next.AddHours(-1);
            var end = next.AddHours(1);
            var before = history.LastOrDefault(s => s.Date >= start && s.Date < next && s.Used.ContainsKey(key))?.Used[key];
            var after = history.FirstOrDefault(s => s.Date > next && s.Date <= end && s.Used.ContainsKey(key))?.Used[key];
            if (before is { } b && after is { } a && b >= 10 && a + 1 >= b && !drops.Any(d => d.Latest >= start && d.Earliest <= end))
            {
                return true;
            }

            next += length;
        }

        return false;
    }

    /// <summary>Where <paramref name="drop"/> lies if <paramref name="other"/>, whole weeks away, confirms it.</summary>
    private static Drop? Confirmation(Drop drop, Drop other, TimeSpan length)
    {
        if (drop.Latest - drop.Earliest >= TimeSpan.FromHours(24) || other.Latest - other.Earliest >= TimeSpan.FromHours(24))
        {
            return null;
        }

        var weeks = Math.Round((drop.Moment - other.Moment) / length);
        return weeks == 0 ? null : drop.Overlap(other, length * weeks);
    }
}
