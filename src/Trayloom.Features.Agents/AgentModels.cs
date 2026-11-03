// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json.Serialization;
using Trayloom.Core;

namespace Trayloom.Features.Agents;

/// <summary>The coding agents whose session logs the island reads.</summary>
public enum AgentProvider
{
    Claude,
    Codex,
    OpenCode,
    Copilot,
}

/// <summary>Display helpers for <see cref="AgentProvider"/>.</summary>
public static class AgentProviders
{
    public static readonly AgentProvider[] All = [AgentProvider.Claude, AgentProvider.Codex, AgentProvider.OpenCode, AgentProvider.Copilot];

    public static string Raw(this AgentProvider p) => p switch
    {
        AgentProvider.Claude => "claude",
        AgentProvider.Codex => "codex",
        AgentProvider.OpenCode => "opencode",
        _ => "copilot",
    };

    public static string DisplayName(this AgentProvider p) => p switch
    {
        AgentProvider.Claude => "Claude",
        AgentProvider.Codex => "Codex",
        AgentProvider.OpenCode => "OpenCode",
        _ => Loc.T("GitHub Copilot"),
    };

    /// <summary>Whether the provider records a plan allowance in its local logs.</summary>
    public static bool ReportsLimits(this AgentProvider p) => p != AgentProvider.Copilot;

    /// <summary>Settings key that switches the provider on.</summary>
    public static string SettingKey(this AgentProvider p) => p switch
    {
        AgentProvider.Claude => AgentKeys.Claude,
        AgentProvider.Codex => AgentKeys.Codex,
        AgentProvider.OpenCode => AgentKeys.OpenCode,
        _ => AgentKeys.Copilot,
    };
}

/// <summary>Token counts. <c>Input</c> excludes cache traffic, <c>Output</c> already contains reasoning.</summary>
public record struct AgentTokens(long Input, long CacheWrite, long CacheRead, long Output, long Reasoning)
{
    public readonly long Total => Input + CacheWrite + CacheRead + Output;

    /// <summary>Everything the model read, cached or not.</summary>
    public readonly long Prompt => Input + CacheWrite + CacheRead;

    public readonly double? CacheHitRate => Prompt > 0 ? (double)CacheRead / Prompt : null;

    public static AgentTokens operator +(AgentTokens a, AgentTokens b) =>
        new(a.Input + b.Input, a.CacheWrite + b.CacheWrite, a.CacheRead + b.CacheRead, a.Output + b.Output, a.Reasoning + b.Reasoning);

    public static AgentTokens operator -(AgentTokens a, AgentTokens b) =>
        new(a.Input - b.Input, a.CacheWrite - b.CacheWrite, a.CacheRead - b.CacheRead, a.Output - b.Output, a.Reasoning - b.Reasoning);

    /// <summary>Streamed replies are logged once per block with growing output; the largest reading is final.</summary>
    public readonly AgentTokens Merged(AgentTokens other) =>
        new(Math.Max(Input, other.Input), Math.Max(CacheWrite, other.CacheWrite), Math.Max(CacheRead, other.CacheRead),
            Math.Max(Output, other.Output), Math.Max(Reasoning, other.Reasoning));
}

/// <summary>What one response bills, beyond its model.</summary>
public record struct AgentBillable
{
    public AgentTokens Tokens { get; set; }

    /// <summary>Session totals: priced at base rates without long-context inference.</summary>
    public bool IsAggregate { get; set; }

    public long LongCacheWrite { get; set; }

    public bool Fast { get; set; }

    public bool Domestic { get; set; }

    public long WebSearches { get; set; }
}

/// <summary>One billed model response.</summary>
public sealed class UsageRecord
{
    public AgentProvider Provider { get; set; }

    public DateTime Date { get; set; }

    public string Model { get; set; } = "";

    public string Project { get; set; } = "";

    public string Session { get; set; } = "";

    public int Requests { get; set; } = 1;

    public AgentTokens Tokens { get; set; }

    /// <summary>API list price of the response in US dollars; null when the model has no known price.</summary>
    public double? Cost { get; set; }

    public double Savings { get; set; }

    public bool ReportedCost { get; set; }

    public UsageRecord Clone() => (UsageRecord)MemberwiseClone();
}

public enum LimitKind
{
    Session,
    Weekly,
    Other,
}

/// <summary>A usage allowance and how much of it is spent, as the provider reports it.</summary>
public sealed record LimitWindow(string Id, LimitKind Kind, int? Minutes, string? Scope, double UsedPercent, DateTime? ResetsAt, DateTime? EstimatedResetsAt = null)
{
    /// <summary>The source's own severity word (<c>normal</c>, <c>warning</c>, <c>critical</c>) when it gave one.</summary>
    public string? Severity { get; init; }

    /// <summary>False when the source says the window is not running (nothing started it yet); null when it did not say.</summary>
    public bool? Active { get; init; }

    /// <summary>The source's name for a window this app has no word for.</summary>
    public string? Label { get; init; }

    /// <summary>Why the window reads as it does when the source's own figures were adjusted (diagnostics only).</summary>
    public string? Note { get; init; }

    /// <summary>The reset time passed before the next reading: the window renewed and nothing says what it holds now.</summary>
    public bool Renewed { get; init; }

    /// <summary>Not running and nothing used: the next request starts it. Never shown as a percentage or a countdown.</summary>
    [JsonIgnore]
    public bool NotStarted => !Renewed && ResetsAt is null && UsedPercent <= 0 && Active != true;

    [JsonIgnore]
    public double UsedFraction => Math.Min(1, Math.Max(0, UsedPercent / 100));

    [JsonIgnore]
    public double RemainingFraction => 1 - UsedFraction;
}

public enum LimitSource
{
    ClaudeApp,
    SessionLog,
    Account,
}

public sealed record AgentLimits(AgentProvider Provider, List<LimitWindow> Windows, DateTime ObservedAt, LimitSource Source)
{
    /// <summary>Which reading this is (see <see cref="LimitOrigins"/>); null for readings saved by an older build.</summary>
    public string? Origin { get; init; }

    /// <summary>Claude's weekly use by product, when the source gave it.</summary>
    public List<LimitShare>? Breakdown { get; init; }

    public DateTime? BreakdownAsOf { get; init; }

    /// <summary>Claude app readings: whether its newest sample had a 5-hour figure; null for other sources.</summary>
    public bool? SessionReported { get; init; }
}

/// <summary>One product's share of the week's use (Claude Code, Chats, Cowork...).</summary>
public sealed record LimitShare(string Key, string Name, double Percent);

/// <summary>The places a limit reading can come from, with the words shown next to the numbers.</summary>
public static class LimitOrigins
{
    public const string ClaudeCli = "claude-cli";
    public const string ClaudeCache = "claude-cache";
    public const string ClaudeDesktop = "claude-desktop";
    public const string CodexServer = "codex-server";
    public const string CodexLog = "codex-log";

    public static string Name(string? origin) => origin switch
    {
        ClaudeCli => "Claude Code",
        ClaudeCache => "Claude Code (saved copy)",
        ClaudeDesktop => "Claude app history",
        CodexServer => "Codex app-server",
        CodexLog => "Codex session log",
        _ => "Local readings",
    };

    /// <summary>Order of trust when two readings are from the same moment.</summary>
    public static int Rank(string? origin) => origin switch { ClaudeCli => 0, CodexServer => 0, ClaudeCache => 1, ClaudeDesktop => 2, _ => 3 };
}

/// <summary>The subscription an account is on.</summary>
public sealed record AgentPlan(string Name, double? MonthlyPrice);

/// <summary>A turn an agent is working on right now.</summary>
public sealed class LiveSession
{
    public string Id { get; set; } = "";

    public AgentProvider Provider { get; set; }

    public DateTime Started { get; set; }

    public DateTime LastActivity { get; set; }

    public string Model { get; set; } = "";

    public string Project { get; set; } = "";

    public AgentTokens Tokens { get; set; }

    public double Cost { get; set; }

    public LiveSession Clone() => (LiveSession)MemberwiseClone();
}

/// <summary>Something worth a moment in the closed island.</summary>
public abstract record AgentUsageEvent
{
    public sealed record Finished(AgentProvider Provider, TimeSpan Duration, double Cost, long Tokens, string Project) : AgentUsageEvent;

    public sealed record LimitWarning(AgentProvider Provider, LimitWindow Window) : AgentUsageEvent;

    public sealed record LimitReset(AgentProvider Provider, LimitWindow Window) : AgentUsageEvent;

    public sealed record BudgetReached(double Spent, double Budget) : AgentUsageEvent;
}

public enum AgentPeriod
{
    Today,
    Week,
    Month,
}

public static class AgentPeriods
{
    public static int Days(this AgentPeriod p) => p switch { AgentPeriod.Today => 1, AgentPeriod.Week => 7, _ => 30 };

    public static string Raw(this AgentPeriod p) => p switch { AgentPeriod.Today => "today", AgentPeriod.Week => "week", _ => "month" };

    public static AgentPeriod Parse(string? s) => s switch { "week" => AgentPeriod.Week, "month" => AgentPeriod.Month, _ => AgentPeriod.Today };
}

/// <summary>The cards of the AI page; raw ids are stored in the saved order.</summary>
public enum AgentCard
{
    Limits,
    Spend,
    Live,
    Trend,
    Models,
    Projects,
    Activity,
    Resets,
}

public static class AgentCards
{
    public static readonly AgentCard[] All =
    [
        AgentCard.Limits, AgentCard.Spend, AgentCard.Live, AgentCard.Trend,
        AgentCard.Models, AgentCard.Projects, AgentCard.Activity, AgentCard.Resets,
    ];

    public static string Raw(this AgentCard c) => c switch
    {
        AgentCard.Limits => "limits",
        AgentCard.Spend => "spend",
        AgentCard.Live => "live",
        AgentCard.Trend => "trend",
        AgentCard.Models => "models",
        AgentCard.Projects => "projects",
        AgentCard.Activity => "activity",
        _ => "resets",
    };

    public static AgentCard? Parse(string s) => All.Cast<AgentCard?>().FirstOrDefault(c => c!.Value.Raw() == s);

    /// <summary>Segoe Fluent Icons glyph for the SF symbol of the card.</summary>
    public static string Glyph(this AgentCard c) => c switch
    {
        AgentCard.Limits => "",
        AgentCard.Spend => "",
        AgentCard.Live => "",
        AgentCard.Trend => "",
        AgentCard.Models => "",
        AgentCard.Projects => "",
        AgentCard.Activity => "",
        _ => "",
    };

    /// <summary>Charts need the island's width; everything else pairs up.</summary>
    public static bool FullWidth(this AgentCard c) => c is AgentCard.Trend or AgentCard.Activity or AgentCard.Live;

    /// <summary>Cards that cover the period chosen in the island's top row.</summary>
    public static bool FollowsPeriod(this AgentCard c) => c is AgentCard.Spend or AgentCard.Trend or AgentCard.Models or AgentCard.Projects;
}

public enum AgentReadout
{
    Elapsed,
    Tokens,
    Cost,
    Limit,

    /// <summary>The time and, beside it, the limit closest to running out: "2:43 · Wk 100%".</summary>
    TimeAndLimit,
}

public enum AgentLimitFocus
{
    MostUsed,
    Session,
    Weekly,
}

/// <summary>Totals of a period, a day or an hour.</summary>
public struct AgentTotals
{
    public AgentTokens Tokens;
    public double Cost;
    public double Savings;
    public long Requests;

    /// <summary>Responses from a model without a known price, left out of <see cref="Cost"/>.</summary>
    public int Unpriced;

    public void Add(UsageRecord r)
    {
        Tokens += r.Tokens;
        Requests += Math.Max(0, r.Requests);
        Savings += r.Savings;
        if (r.Cost is { } c)
        {
            Cost += c;
        }
        else
        {
            Unpriced++;
        }
    }

    public void Add(in AgentTotals o)
    {
        Tokens += o.Tokens;
        Cost += o.Cost;
        Savings += o.Savings;
        Requests += o.Requests;
        Unpriced += o.Unpriced;
    }

    /// <summary>Cost when every response had a price, tokens otherwise.</summary>
    public readonly double Weight(bool byCost) => byCost ? Cost : Tokens.Total;
}

/// <summary>One row of a breakdown: a model or a project.</summary>
public sealed class AgentShare
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public AgentProvider? Provider { get; set; }

    public AgentTotals Totals;
}

public sealed class AgentPeriodUsage
{
    public AgentTotals Total;
    public Dictionary<AgentProvider, AgentTotals> ByProvider = [];
    public List<AgentShare> Models = [];
    public List<AgentShare> Projects = [];

    public bool FullyPriced => Total.Unpriced == 0;
}

/// <summary>A day or an hour of use.</summary>
public sealed class AgentBucket
{
    public DateTime Start { get; set; }

    public Dictionary<AgentProvider, AgentTotals> ByProvider = [];

    public AgentTotals Total
    {
        get
        {
            var t = new AgentTotals();
            foreach (var v in ByProvider.Values)
            {
                t.Add(v);
            }

            return t;
        }
    }
}

/// <summary>Claude's allowance renews five hours after the first request that follows a renewal.</summary>
public sealed class AgentBlock
{
    public DateTime Start { get; set; }

    public DateTime End { get; set; }

    public AgentTotals Totals;
}

/// <summary>Everything the page, the strip and the settings show.</summary>
public sealed class AgentUsageSnapshot
{
    public const int DayCount = 91;

    public bool Loaded { get; set; }

    public DateTime Now { get; set; } = DateTime.MinValue;

    public Dictionary<AgentPeriod, AgentPeriodUsage> Periods = [];

    /// <summary>Oldest first, ending with today (local days, <see cref="AgentBucket.Start"/> is local midnight).</summary>
    public List<AgentBucket> Days = [];

    /// <summary>Today's 24 hours.</summary>
    public List<AgentBucket> Hours = [];

    public Dictionary<AgentProvider, AgentLimits> Limits = [];

    public Dictionary<AgentProvider, AgentPlan> Plans = [];

    public List<LiveSession> Live = [];

    public AgentBlock? ClaudeBlock { get; set; }

    public Dictionary<AgentProvider, AgentTotals> BurnRate = [];

    public Dictionary<AgentProvider, DateTime> LastActivity = [];

    public HashSet<AgentProvider> Seen = [];

    /// <summary>Every limit source with its raw readings, for "Where these numbers come from".</summary>
    public List<LimitSourceInfo> LimitSources = [];

    public AgentPeriodUsage Usage(AgentPeriod p) => Periods.TryGetValue(p, out var u) ? u : new AgentPeriodUsage();

    public IEnumerable<LiveSession> Working(AgentProvider p) => Live.Where(s => s.Provider == p);
}

/// <summary>Settings key names (Vorssaint <c>DefaultsKey</c> names).</summary>
public static class AgentKeys
{
    public const string Enabled = "notchAgentsEnabled";
    public const string Claude = "notchAgentsClaude";
    public const string Codex = "notchAgentsCodex";
    public const string OpenCode = "notchAgentsOpenCode";
    public const string Copilot = "notchAgentsCopilot";
    public const string ClaudeProfile = "notchAgentsClaudeProfile";
    public const string IncludeClaudeCode = "notchAgentsIncludeClaudeCode";
    public const string CodexProfiles = "notchAgentsCodexProfiles";
    public const string CardOrder = "notchAgentsCardOrder";
    public const string HiddenCards = "notchAgentsHiddenCards";
    public const string Period = "notchAgentsPeriod";
    public const string LimitDisplay = "notchAgentsLimitDisplay";
    public const string LimitFocus = "notchAgentsLimitFocus";
    public const string LiveActivity = "notchAgentsLiveActivity";
    public const string Readout = "notchAgentsReadout";
    public const string FinishAlert = "notchAgentsFinishAlert";
    public const string FinishMinimum = "notchAgentsFinishMinimum";
    public const string LimitAlert = "notchAgentsLimitAlert";
    public const string LimitThreshold = "notchAgentsLimitThreshold";
    public const string DailyBudget = "notchAgentsDailyBudget";
    public const string PriceUpdates = "notchAgentsPriceUpdates";
    public const string Attention = "notchAgentsAttention";
    public const string IdleContent = "notchIdleContent";
    public const string HiddenModules = "notchHiddenModules";
    public const string FeatureId = "notch-agents";
    public const string ModuleId = "agents";
}

/// <summary>One place a limit reading can come from, as it stands now (diagnostics).</summary>
public sealed class LimitSourceInfo
{
    public string Origin { get; set; } = "";

    public AgentProvider Provider { get; set; }

    public string Name { get; set; } = "";

    /// <summary>The file or command the reading came from.</summary>
    public string Where { get; set; } = "";

    public DateTime? ObservedAt { get; set; }

    /// <summary>What happened when this source was read (used, older than another, unavailable and why).</summary>
    public string Status { get; set; } = "";

    /// <summary>The values exactly as the source gave them.</summary>
    public List<string> Raw { get; set; } = [];

    public bool InUse { get; set; }
}
