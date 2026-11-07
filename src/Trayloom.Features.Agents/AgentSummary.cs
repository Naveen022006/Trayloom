// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace Trayloom.Features.Agents;

internal static class AgentUsageSummary
{
    public static readonly TimeSpan BlockLength = TimeSpan.FromHours(5);

    /// <summary>Each window starts where the one before it ended; a day of history covers any real stretch.</summary>
    public static readonly TimeSpan BlockHistory = TimeSpan.FromHours(24);

    public static readonly TimeSpan BurnWindow = TimeSpan.FromMinutes(30);

    public static AgentUsageSnapshot Snapshot(IReadOnlyList<UsageRecord> records, Dictionary<AgentProvider, AgentLimits> limits, List<LiveSession> live,
        Dictionary<AgentProvider, AgentPlan> plans, HashSet<AgentProvider> providers, DateTime nowUtc)
    {
        var snapshot = new AgentUsageSnapshot { Loaded = true, Now = nowUtc };
        // A plan without a 5-hour window (Claude Ultra) never shows one, even when a source sends it.
        snapshot.Limits = limits.Where(kv => providers.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => AgentLimitPlan.Shape(kv.Key, kv.Value, plans.GetValueOrDefault(kv.Key))!);
        snapshot.Plans = plans.Where(kv => providers.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        snapshot.Live = live.Where(s => providers.Contains(s.Provider)).OrderBy(s => s.Started).ToList();
        snapshot.Seen = new HashSet<AgentProvider>(snapshot.Limits.Keys);
        snapshot.Seen.UnionWith(snapshot.Live.Select(s => s.Provider));

        var today = nowUtc.ToLocalTime().Date;
        var starts = new List<DateTime>(AgentUsageSnapshot.DayCount);
        for (var i = AgentUsageSnapshot.DayCount - 1; i >= 0; i--)
        {
            starts.Add(today.AddDays(-i));
        }

        var tomorrow = today.AddDays(1);
        var hourStarts = Enumerable.Range(0, 24).Select(h => today.AddHours(h)).ToList();

        // The local day and hour boundaries in UTC, so a response is placed with plain comparisons.
        var startsUtc = starts.Select(s => s.ToUniversalTime()).ToList();
        var tomorrowUtc = tomorrow.ToUniversalTime();
        var hourStartsUtc = hourStarts.Select(h => h.ToUniversalTime()).ToList();
        var days = starts.Select(s => new AgentBucket { Start = s }).ToList();
        var hours = hourStarts.Select(s => new AgentBucket { Start = s }).ToList();
        var periods = new Dictionary<AgentPeriod, AgentPeriodUsage>();
        var models = new Dictionary<AgentPeriod, Dictionary<string, AgentShare>>();
        var projects = new Dictionary<AgentPeriod, Dictionary<string, AgentShare>>();
        var claude = new List<UsageRecord>();
        var allPeriods = Enum.GetValues<AgentPeriod>();
        var recent = nowUtc - BurnWindow;
        var lastDay = starts.Count - 1;
        foreach (var p in allPeriods)
        {
            periods[p] = new AgentPeriodUsage();
            models[p] = [];
            projects[p] = [];
        }

        // A snapshot is built every couple of seconds while an agent works, over every response of 13 weeks (over a hundred thousand
        // for a heavy user). Each response is first added to its group (day, hour, provider, model, project), then the few thousand
        // groups are spread over the days, hours, periods, models and projects: one lookup per response instead of up to nine.
        var groups = new Dictionary<SnapshotGroup, AgentTotals>();
        foreach (var record in records)
        {
            if (!providers.Contains(record.Provider))
            {
                continue;
            }

            snapshot.Seen.Add(record.Provider);
            if (!snapshot.LastActivity.TryGetValue(record.Provider, out var last) || last < record.Date)
            {
                snapshot.LastActivity[record.Provider] = record.Date;
            }

            if (record.Provider == AgentProvider.Claude && record.Date > nowUtc - BlockHistory && record.Date <= nowUtc)
            {
                claude.Add(record);
            }

            if (record.Date >= recent && record.Date <= nowUtc)
            {
                var burn = snapshot.BurnRate.GetValueOrDefault(record.Provider);
                burn.Add(record);
                snapshot.BurnRate[record.Provider] = burn;
            }

            // Dates are read as UTC; a local one is converted, an unspecified one counts as UTC (as ToLocalTime takes it).
            var utc = record.Date.Kind == DateTimeKind.Local ? record.Date.ToUniversalTime() : record.Date;
            if (utc < startsUtc[0] || utc >= tomorrowUtc || IndexOf(utc, startsUtc) is not { } day)
            {
                continue;
            }

            var hour = day == lastDay && IndexOf(utc, hourStartsUtc) is { } h ? h : -1;
            ref var totals = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(
                groups, new SnapshotGroup(day, hour, record.Provider, record.Model, record.Project), out _);
            totals.Add(record);
        }

        var names = new Dictionary<(AgentProvider, string), (string Name, string Id)>();
        foreach (var (group, totals) in groups)
        {
            Add(days[group.Day].ByProvider, group.Provider, totals);
            if (group.Hour >= 0)
            {
                Add(hours[group.Hour].ByProvider, group.Provider, totals);
            }

            if (!names.TryGetValue((group.Provider, group.Model), out var named))
            {
                var display = AgentPricing.DisplayName(group.Model);
                named = (display, group.Provider.Raw() + ":" + display);
                names[(group.Provider, group.Model)] = named;
            }

            var (name, modelId) = named;
            foreach (var period in allPeriods)
            {
                if (group.Day <= lastDay - period.Days())
                {
                    continue;
                }

                var usage = periods[period];
                usage.Total.Add(totals);
                Add(usage.ByProvider, group.Provider, totals);
                if (!models[period].TryGetValue(modelId, out var share))
                {
                    share = new AgentShare { Id = modelId, Name = name.Length == 0 ? "?" : name, Provider = group.Provider };
                    models[period][modelId] = share;
                }

                share.Totals.Add(totals);
                if (group.Project.Length > 0)
                {
                    if (!projects[period].TryGetValue(group.Project, out var ps))
                    {
                        ps = new AgentShare { Id = group.Project, Name = group.Project };
                        projects[period][group.Project] = ps;
                    }

                    ps.Totals.Add(totals);
                }
            }
        }

        foreach (var period in Enum.GetValues<AgentPeriod>())
        {
            var usage = periods[period];
            var byCost = usage.FullyPriced;
            usage.Models = Sorted(models[period].Values, byCost);
            usage.Projects = Sorted(projects[period].Values, byCost);
        }

        var scale = 3600 / BurnWindow.TotalSeconds;
        foreach (var provider in snapshot.BurnRate.Keys.ToList())
        {
            var rate = snapshot.BurnRate[provider];
            rate.Cost *= scale;
            rate.Savings *= scale;
            rate.Tokens = new AgentTokens((long)(rate.Tokens.Input * scale), (long)(rate.Tokens.CacheWrite * scale), (long)(rate.Tokens.CacheRead * scale),
                (long)(rate.Tokens.Output * scale), (long)(rate.Tokens.Reasoning * scale));
            snapshot.BurnRate[provider] = rate;
        }

        snapshot.Periods = periods;
        snapshot.Days = days;
        snapshot.Hours = hours;
        snapshot.ClaudeBlock = CurrentBlock(claude, nowUtc);
        return snapshot;
    }

    private static void Add(Dictionary<AgentProvider, AgentTotals> map, AgentProvider provider, in AgentTotals totals)
    {
        var t = map.GetValueOrDefault(provider);
        t.Add(totals);
        map[provider] = t;
    }

    /// <summary>
    /// The responses a snapshot counts together. Model and project compare by reference: the store keeps one instance of each, and
    /// two equal strings that are not the same instance only make two groups that add up to the same shares.
    /// </summary>
    private readonly struct SnapshotGroup(int day, int hour, AgentProvider provider, string model, string project) : IEquatable<SnapshotGroup>
    {
        public int Day { get; } = day;

        public int Hour { get; } = hour;

        public AgentProvider Provider { get; } = provider;

        public string Model { get; } = model;

        public string Project { get; } = project;

        public bool Equals(SnapshotGroup other) =>
            Day == other.Day && Hour == other.Hour && Provider == other.Provider && ReferenceEquals(Model, other.Model) && ReferenceEquals(Project, other.Project);

        public override bool Equals(object? obj) => obj is SnapshotGroup other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(Day, Hour, Provider, System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Model), System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Project));
    }

    /// <summary>Whether a snapshot reads differently at <paramref name="nowUtc"/> with nothing new read.</summary>
    public static bool MovesWithClock(AgentUsageSnapshot snapshot, DateTime nowUtc) =>
        snapshot.BurnRate.Count > 0 || snapshot.ClaudeBlock is not null || snapshot.Now.ToLocalTime().Date != nowUtc.ToLocalTime().Date;

    public static List<AgentShare> Sorted(IEnumerable<AgentShare> shares, bool byCost) =>
        shares.OrderByDescending(s => s.Totals.Weight(byCost)).ThenBy(s => s.Name, StringComparer.Ordinal).ToList();

    /// <summary>The last start at or before <paramref name="date"/>, by bisection.</summary>
    public static int? IndexOf(DateTime date, List<DateTime> starts)
    {
        if (starts.Count == 0 || date < starts[0])
        {
            return null;
        }

        int low = 0, high = starts.Count - 1;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (starts[middle] <= date)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    /// <summary>Windows start at the first request after the previous one ended and last five hours from that moment.</summary>
    public static AgentBlock? CurrentBlock(List<UsageRecord> records, DateTime now)
    {
        AgentBlock? block = null;
        foreach (var record in records.OrderBy(r => r.Date))
        {
            if (block is not null && record.Date < block.End)
            {
                block.Totals.Add(record);
                continue;
            }

            block = new AgentBlock { Start = record.Date, End = record.Date + BlockLength };
            block.Totals.Add(record);
        }

        return block is not null && block.End > now ? block : null;
    }
}

/// <summary>How much of a window's time has passed, from 0 to 1.</summary>
public readonly record struct AgentPace(double Elapsed);

public static class AgentLimitSupport
{
    /// <summary>A window that renewed since it was read has nothing spent on it yet.</summary>
    public static LimitWindow Current(LimitWindow window, DateTime now)
    {
        if (window.ResetsAt is not { } resets || resets > now)
        {
            return window;
        }

        // Renewed since it was read: what it holds now is unknown until the next reading, never shown as a fresh zero.
        return window with { UsedPercent = 0, ResetsAt = null, EstimatedResetsAt = null, Renewed = true };
    }

    /// <summary>Once an estimate has passed, the countdown is unknown until another reading places it.</summary>
    public static DateTime? DisplayedReset(LimitWindow window, DateTime now)
    {
        var reset = window.EstimatedResetsAt ?? window.ResetsAt;
        return reset is { } r && r > now ? r : null;
    }

    public static AgentPace? Pace(LimitWindow window, DateTime now)
    {
        if (window.Minutes is not > 0 || DisplayedReset(window, now) is not { } resets)
        {
            return null;
        }

        var length = TimeSpan.FromMinutes(window.Minutes.Value);
        var elapsed = now - (resets - length);
        if (elapsed <= TimeSpan.Zero)
        {
            return null;
        }

        return new AgentPace(Math.Min(1, elapsed / length));
    }

    /// <summary>The window that will stop work first: the most spent, and on a tie the one renewing last.</summary>
    public static LimitWindow? Binding(AgentLimits? limits, DateTime now)
    {
        LimitWindow? best = null;
        if (limits is null)
        {
            return null;
        }

        // A window that renewed since it was read holds an unknown amount, so it only counts when nothing else is known.
        var current = limits.Windows.Select(w => Current(w, now)).ToList();
        foreach (var w in current.Any(x => !x.Renewed) ? current.Where(x => !x.Renewed) : current)
        {
            if (best is null || w.UsedPercent > best.UsedPercent
                || (w.UsedPercent == best.UsedPercent && (w.ResetsAt ?? DateTime.MinValue) >= (best.ResetsAt ?? DateTime.MinValue)))
            {
                best = w;
            }
        }

        return best;
    }

    /// <summary>Windows that reached <paramref name="threshold"/> percent between two readings of the same account.</summary>
    public static List<LimitWindow> Crossings(AgentLimits? previous, AgentLimits current, double threshold)
    {
        if (threshold <= 0)
        {
            return [];
        }

        var result = new List<LimitWindow>();
        foreach (var window in current.Windows)
        {
            if (window.UsedPercent < threshold || previous is null)
            {
                continue;
            }

            var before = previous.Windows.FirstOrDefault(w => w.Id == window.Id);
            if (before is null)
            {
                continue;
            }

            if (before.UsedPercent < threshold || Renewed(before, window, current.ObservedAt)
                || (current.Source == LimitSource.Account && Began(window, previous.ObservedAt)))
            {
                result.Add(window);
            }
        }

        return result;
    }

    /// <summary>Whether the account's exact renewal places the start of <paramref name="window"/> after <paramref name="time"/>.</summary>
    private static bool Began(LimitWindow window, DateTime time) =>
        window.Minutes is { } minutes && window.ResetsAt is { } resets && resets.AddMinutes(-minutes) > time;

    /// <summary>Whether <paramref name="window"/>, read at <paramref name="observedAt"/>, comes after the one <paramref name="before"/> saw.</summary>
    public static bool Renewed(LimitWindow before, LimitWindow window, DateTime observedAt)
    {
        if (window.UsedPercent + 1 < before.UsedPercent)
        {
            return true;
        }

        if (before.ResetsAt is not { } was || window.ResetsAt is not { } resets)
        {
            return false;
        }

        return (resets - was).TotalSeconds > 60 && observedAt >= was;
    }
}
