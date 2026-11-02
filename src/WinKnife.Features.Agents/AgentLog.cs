// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinKnife.Features.Agents;

/// <summary>
/// What one log line says, reduced to the few facts the island keeps. Only usage counters, model names, times and folder names
/// leave a line (plus the ids of Claude's running shell commands, in memory only).
/// </summary>
public abstract record LogEntry
{
    public sealed record Usage(string Key, UsageRecord Record, AgentBillable Billable) : LogEntry;

    public sealed record UsageModel(string Key, string Model) : LogEntry;

    public sealed record Limits(AgentLimits Reading) : LogEntry;

    public sealed record Plan(string PlanType, DateTime ObservedAt) : LogEntry;

    public sealed record TurnBegan(DateTime At) : LogEntry;

    public sealed record TurnContext(string Model, string Project) : LogEntry;

    public sealed record TurnActive(DateTime? At) : LogEntry;

    public sealed record TurnSettled(DateTime At) : LogEntry;

    public sealed record TurnEnded(DateTime? At, bool Completed, TimeSpan? Duration) : LogEntry;

    public sealed record Reset(DateTime At) : LogEntry;
}

/// <summary>Per-session turn and model tracking for OpenCode databases.</summary>
public sealed class OpenCodeSessionState
{
    public string Project { get; set; } = "";

    public string Model { get; set; } = "";

    public bool TurnOpen { get; set; }

    public DateTime? TurnStarted { get; set; }

    public string ActiveUserMessageID { get; set; } = "";

    public DateTime? LastActivity { get; set; }

    public DateTime? SettledAt { get; set; }

    public string WritingReplyID { get; set; } = "";

    public DateTime? ActivePromptDate { get; set; }

    public HashSet<string> SeenUserMessageIDs { get; set; } = [];

    public HashSet<string> CompletedAssistantMessageIDs { get; set; } = [];

    public HashSet<string> CompletedUserMessageIDs { get; set; } = [];
}

/// <summary>Per-file context carried from line to line.</summary>
public sealed class AgentLogState
{
    public string Session { get; set; } = "";

    public string Project { get; set; } = "";

    public string Model { get; set; } = "";

    public bool TurnOpen { get; set; }

    /// <summary>Newer Codex logs write one usage record per response; older ones only carry running totals.</summary>
    public bool SawUsageRecords { get; set; }

    public AgentTokens? LastTotal { get; set; }

    public bool Fast { get; set; }

    public bool CodexSubagent { get; set; }

    public bool CodexHeaderRead { get; set; }

    public string CodexRoot { get; set; } = "";

    public string CodexTotalsSession { get; set; } = "";

    public Dictionary<string, AgentTokens> CopilotTotals { get; set; } = [];

    public Dictionary<string, int> CopilotRequests { get; set; } = [];

    public Dictionary<string, string> CopilotRequestModels { get; set; } = [];

    public Dictionary<string, int> CopilotReportedRequests { get; set; } = [];

    public string? CopilotTurnID { get; set; }

    public bool CopilotFinalResponse { get; set; }

    public string ParentSession { get; set; } = "";

    [JsonIgnore]
    public Dictionary<string, OpenCodeSessionState> OpenCodeSessions { get; set; } = [];

    /// <summary>Claude's shell commands still waiting for their result, by tool call.</summary>
    public HashSet<string> RunningCommands { get; set; } = [];
}

/// <summary>JSON and byte helpers shared by the harness readers (spec 03 A4).</summary>
internal static class AgentLogParser
{
    public static readonly HashSet<string> CodexEvents = ["token_count", "task_started", "task_complete", "turn_aborted", "thread_settings_applied"];

    /// <summary>OpenCode starts the next step the moment a step's tools return; a turn silent this long after a step has stopped.</summary>
    public static readonly TimeSpan OpenCodeSettle = TimeSpan.FromSeconds(30);

    public static bool Contains(ReadOnlySpan<byte> line, ReadOnlySpan<byte> needle) => line.IndexOf(needle) >= 0;

    /// <summary>The first <c>"type":"…"</c> value at or after <paramref name="start"/> and where it ends; searched within the first bytes only.</summary>
    public static (string Name, int End)? FirstType(ReadOnlySpan<byte> line, int start = 0)
    {
        ReadOnlySpan<byte> key = "\"type\":\""u8;
        if (start >= line.Length)
        {
            return null;
        }

        var found = line[start..].IndexOf(key);
        if (found < 0)
        {
            return null;
        }

        var value = start + found + key.Length;
        var limit = Math.Min(line.Length, value + 64);
        if (value >= limit)
        {
            return null;
        }

        var quote = line[value..limit].IndexOf((byte)'"');
        if (quote < 0)
        {
            return null;
        }

        var end = value + quote;
        return (System.Text.Encoding.UTF8.GetString(line[value..end]), end + 1);
    }

    /// <summary>The value of a top-level string property, skipping nested values (null on invalid JSON or when absent).</summary>
    public static string? TopLevelString(ReadOnlySpan<byte> line, string name)
    {
        try
        {
            var reader = new Utf8JsonReader(line, new JsonReaderOptions { MaxDepth = 128 });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return null;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    return null;
                }

                var isName = reader.ValueTextEquals(name);
                if (!reader.Read())
                {
                    return null;
                }

                if (isName)
                {
                    return reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                }

                reader.Skip();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    public static JsonDocument? Parse(ReadOnlyMemory<byte> line)
    {
        try
        {
            return JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 128 });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static JsonElement? Prop(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : null;

    public static JsonElement? Prop(this JsonElement? e, string name) => e is { } x ? x.Prop(name) : null;

    public static string? Str(this JsonElement? e) => e is { ValueKind: JsonValueKind.String } x ? x.GetString() : null;

    public static string? Str(this JsonElement e, string name) => e.Prop(name).Str();

    public static string? Str(this JsonElement? e, string name) => e.Prop(name).Str();

    public static bool? Bool(this JsonElement? e) => e is { } x && x.ValueKind is JsonValueKind.True or JsonValueKind.False ? x.GetBoolean() : null;

    public static double? Num(this JsonElement? e) => e is { ValueKind: JsonValueKind.Number } x && x.TryGetDouble(out var d) && double.IsFinite(d) ? d : null;

    /// <summary>A count: negative / NaN become 0, capped at 1e12.</summary>
    public static long Int(this JsonElement? e)
    {
        if (e is not { ValueKind: JsonValueKind.Number } x || !x.TryGetDouble(out var d) || !double.IsFinite(d) || d <= 0)
        {
            return 0;
        }

        return (long)Math.Min(d, 1e12);
    }

    public static IEnumerable<JsonElement> Items(this JsonElement? e) =>
        e is { ValueKind: JsonValueKind.Array } x ? x.EnumerateArray() : [];

    public static bool HasItems(this JsonElement? e) => e is { ValueKind: JsonValueKind.Array } x && x.GetArrayLength() > 0;

    /// <summary>Unix seconds or milliseconds.</summary>
    public static DateTime? Seconds(this JsonElement? e) => e.Num() is { } n ? AgentTime.FromSeconds(n) : null;

    public static DateTime? Timestamp(this JsonElement? e) =>
        e is { ValueKind: JsonValueKind.String } x ? AgentTime.Parse(x.GetString()) : e.Seconds();

    /// <summary>Codex logs a reading for the main allowance (unnamed or "codex") and one per model; only the main one is kept.</summary>
    public static bool IsMainBucket(string? limitId)
    {
        var id = (limitId ?? "").ToLowerInvariant();
        return id.Length == 0 || id == "codex";
    }

    public static AgentTokens CodexTokens(JsonElement? usage)
    {
        var input = usage.Prop("input_tokens").Int();
        var cached = Math.Min(input, usage.Prop("cached_input_tokens").Int());
        var written = Math.Min(input - cached, usage.Prop("cache_write_input_tokens").Int());
        return new AgentTokens(input - cached - written, written, cached, usage.Prop("output_tokens").Int(), usage.Prop("reasoning_output_tokens").Int());
    }

    public static bool FastTier(string? tier) => tier is not null && (tier.Equals("fast", StringComparison.OrdinalIgnoreCase) || tier.Equals("priority", StringComparison.OrdinalIgnoreCase));

    public static bool CopilotEvent(string? type) => type is "user.message" or "assistant.turn_start" or "assistant.message" or "assistant.turn_end"
        or "session.start" or "session.context_changed" or "session.model_change" or "session.shutdown" or "abort";

    /// <summary>Key names a log gives a window's figures; Codex's server spells the same ones in camel case.</summary>
    public readonly record struct WindowKeys(string Used, string Minutes, string Resets, string Individual, string Remaining);

    public static readonly WindowKeys LogWindowKeys = new("used_percent", "window_minutes", "resets_at", "individual_limit", "remaining_percent");

    public static readonly WindowKeys ServerWindowKeys = new("usedPercent", "windowDurationMins", "resetsAt", "individualLimit", "remainingPercent");

    /// <summary>Windows are told apart by their length, never by their slot.</summary>
    public static List<LimitWindow> CodexWindows(JsonElement limits, DateTime observed, WindowKeys keys)
    {
        var windows = new List<LimitWindow>();
        foreach (var slot in new[] { "primary", "secondary" })
        {
            var window = limits.Prop(slot);
            if (window is not { ValueKind: JsonValueKind.Object } w || w.Prop(keys.Used).Num() is not { } used)
            {
                continue;
            }

            int? minutes = w.Prop(keys.Minutes).Num() is { } m ? (int)m : null;
            var resets = w.Prop(keys.Resets).Seconds();
            if (resets is null && w.Prop("resets_in_seconds").Num() is { } delay)
            {
                resets = observed.AddSeconds(Math.Max(0, delay));
            }

            // A window that has not started is reported as ending one full window from now: no such end was ever scheduled, so none is kept.
            var running = true;
            string? note = null;
            if (resets is { } r && used <= 0 && minutes is { } length && Math.Abs((r - observed).TotalMinutes - length) <= 2)
            {
                note = $"reset time {r:u} is exactly one {length}-minute window after the reading, so no window is running";
                resets = null;
                running = false;
            }

            windows.Add(new LimitWindow("codex." + (minutes?.ToString() ?? slot), KindOf(minutes), minutes, null, Math.Min(100, Math.Max(0, used)), resets)
            {
                Active = running ? (resets is not null ? true : null) : false,
                Note = note,
            });
        }

        if (windows.Count == 0 && limits.Prop(keys.Individual) is { ValueKind: JsonValueKind.Object } individual
            && individual.Prop(keys.Remaining).Num() is { } remaining)
        {
            windows.Add(new LimitWindow("codex.individual", LimitKind.Other, null, null, Math.Min(100, Math.Max(0, 100 - remaining)), individual.Prop(keys.Resets).Seconds()));
        }

        return windows.OrderBy(w => w.Minutes ?? int.MaxValue).ToList();
    }

    public static LimitKind KindOf(int? minutes)
    {
        if (minutes is not > 0)
        {
            return LimitKind.Other;
        }

        if (minutes <= 12 * 60)
        {
            return LimitKind.Session;
        }

        return minutes is >= 6 * 1440 and <= 8 * 1440 ? LimitKind.Weekly : LimitKind.Other;
    }

    /// <summary>The folder an agent ran in names the project; a worktree kept inside the repository belongs to that repository.</summary>
    public static string ProjectName(string path)
    {
        foreach (var marker in new[] { "/.claude/worktrees/", "\\.claude\\worktrees\\" })
        {
            var i = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (i >= 0)
            {
                path = path[..i];
            }
        }

        path = path.TrimEnd('\\', '/');
        var cut = path.LastIndexOfAny(['\\', '/']);
        return cut >= 0 ? path[(cut + 1)..] : path;
    }
}
