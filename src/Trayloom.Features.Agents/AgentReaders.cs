// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;

namespace Trayloom.Features.Agents;

/// <summary>One harness's log line parser; what it carries from line to line lives in <see cref="AgentLogState"/>.</summary>
internal interface IAgentHarnessReader
{
    List<LogEntry> Parse(ReadOnlyMemory<byte> line, AgentLogState state, DateTime now);

    /// <summary>True when a log's first reading goes through the Copilot filter that passes on only its event lines.</summary>
    bool ReadsFullHistoryFirst => false;
}

internal static class AgentReaders
{
    private static readonly ClaudeReader Claude = new();
    private static readonly CodexReader Codex = new();
    private static readonly OpenCodeReader OpenCode = new();
    private static readonly CopilotReader Copilot = new();

    public static IAgentHarnessReader For(AgentProvider p) => p switch
    {
        AgentProvider.Claude => Claude,
        AgentProvider.Codex => Codex,
        AgentProvider.OpenCode => OpenCode,
        _ => Copilot,
    };
}

/// <summary>Reads Claude Code session logs. A prompt opens a turn, a reply keeps it going and records its usage.</summary>
internal sealed class ClaudeReader : IAgentHarnessReader
{
    private static readonly List<LogEntry> None = [];

    public List<LogEntry> Parse(ReadOnlyMemory<byte> memory, AgentLogState state, DateTime now)
    {
        var line = memory.Span;
        if (AgentLogParser.Contains(line, "\"type\":\"assistant\""u8))
        {
            return Assistant(memory, state, now);
        }

        if (!AgentLogParser.Contains(line, "\"type\":\"user\""u8))
        {
            return None;
        }

        // A local command prints its output without asking the model anything, so the command line that opened a turn closes it again.
        if (AgentLogParser.Contains(line, "[Request interrupted by user"u8) || AgentLogParser.Contains(line, "<local-command-std"u8))
        {
            using var doc = AgentLogParser.Parse(memory);
            if (doc is not null && doc.RootElement.Str("type") == "user" && EndsTurn(doc.RootElement))
            {
                var open = state.TurnOpen;
                state.TurnOpen = false;
                return open ? [new LogEntry.TurnEnded(null, false, null)] : None;
            }
        }

        if (AgentLogParser.Contains(line, "\"toolEndsTurn\":true"u8))
        {
            using var doc = AgentLogParser.Parse(memory);
            if (doc is not null && doc.RootElement.Str("type") == "user" && doc.RootElement.Prop("toolEndsTurn").Bool() == true
                && doc.RootElement.Prop("isSidechain").Bool() != true)
            {
                var open = state.TurnOpen;
                state.TurnOpen = false;
                return open ? [new LogEntry.TurnEnded(doc.RootElement.Prop("timestamp").Timestamp() ?? now, true, null)] : None;
            }
        }

        // Tool results arrive inside a turn and can be large; while a turn is open, the line only has to say that work goes on.
        if (state.TurnOpen)
        {
            if (state.RunningCommands.Count > 0)
            {
                using var doc = AgentLogParser.Parse(memory);
                if (doc is not null && doc.RootElement.Str("type") == "user" && doc.RootElement.Prop("isMeta").Bool() != true
                    && doc.RootElement.Prop("isSidechain").Bool() != true)
                {
                    var content = doc.RootElement.Prop("message").Prop("content");
                    var results = content.Items().Where(c => c.Str("type") == "tool_result").ToList();
                    if (results.Count > 0)
                    {
                        foreach (var r in results)
                        {
                            if (r.Str("tool_use_id") is { } id)
                            {
                                state.RunningCommands.Remove(id);
                            }
                        }
                    }
                    else
                    {
                        // A prompt inside an open turn, as when a session killed in the middle of a command resumes, leaves it behind.
                        state.RunningCommands.Clear();
                    }
                }
            }

            return [new LogEntry.TurnActive(null)];
        }

        // A subagent's prompts and tool output never open a turn.
        if (AgentLogParser.Contains(line, "\"isSidechain\":true"u8))
        {
            return None;
        }

        using var json = AgentLogParser.Parse(memory);
        if (json is null || json.RootElement.Str("type") != "user" || json.RootElement.Prop("isMeta").Bool() == true
            || json.RootElement.Prop("isSidechain").Bool() == true)
        {
            return None;
        }

        Adopt(json.RootElement, state);
        state.TurnOpen = true;
        state.RunningCommands.Clear();
        return [new LogEntry.TurnBegan(json.RootElement.Prop("timestamp").Timestamp() ?? now)];
    }

    private static List<LogEntry> Assistant(ReadOnlyMemory<byte> memory, AgentLogState state, DateTime now)
    {
        using var doc = AgentLogParser.Parse(memory);
        if (doc is null)
        {
            return None;
        }

        var json = doc.RootElement;
        if (json.Str("type") != "assistant" || json.Prop("message") is not { ValueKind: JsonValueKind.Object } message)
        {
            return None;
        }

        Adopt(json, state);
        var date = json.Prop("timestamp").Timestamp() ?? now;
        var entries = new List<LogEntry>();
        var model = message.Str("model") ?? "";
        if (message.Prop("usage") is { ValueKind: JsonValueKind.Object } usage && model.Length > 0 && !model.StartsWith('<'))
        {
            state.Model = model;
            var billable = new AgentBillable
            {
                Tokens = new AgentTokens(
                    usage.Prop("input_tokens").Int(),
                    usage.Prop("cache_creation_input_tokens").Int(),
                    usage.Prop("cache_read_input_tokens").Int(),
                    usage.Prop("output_tokens").Int(),
                    usage.Prop("output_tokens_details").Prop("thinking_tokens").Int()),
                LongCacheWrite = usage.Prop("cache_creation").Prop("ephemeral_1h_input_tokens").Int(),
                Fast = usage.Str("speed") == "fast",
                Domestic = usage.Str("inference_geo") == "us",
                WebSearches = usage.Prop("server_tool_use").Prop("web_search_requests").Int(),
            };
            var id = message.Str("id") ?? "";
            var request = json.Str("requestId") ?? "";
            var key = id.Length == 0 && request.Length == 0
                ? $"claude:{state.Session}:{date.Ticks}"
                : $"claude:{id}:{request}";
            var priced = AgentPricing.Cost(billable, model);
            entries.Add(new LogEntry.Usage(key, new UsageRecord
            {
                Provider = AgentProvider.Claude,
                Date = date,
                Model = model,
                Project = state.Project,
                Session = state.Session,
                Tokens = billable.Tokens,
                Cost = priced.Cost,
                Savings = priced.Savings,
            }, billable));
        }

        // A subagent's own ending is not the end of the turn it serves.
        if (json.Prop("isSidechain").Bool() == true)
        {
            return entries;
        }

        switch (message.Str("stop_reason"))
        {
            case "end_turn" or "stop_sequence" or "max_tokens" or "refusal":
                // An error written in place of a reply, like a spent limit, stops the turn without finishing it.
                var failed = json.Prop("isApiErrorMessage").Bool() == true || model.StartsWith('<');
                if (state.TurnOpen)
                {
                    entries.Add(new LogEntry.TurnEnded(date, !failed, null));
                }

                state.TurnOpen = false;
                break;
            default:
                foreach (var block in message.Prop("content").Items())
                {
                    if (block.Str("type") == "tool_use" && block.Str("name") == "Bash" && block.Str("id") is { } toolId)
                    {
                        state.RunningCommands.Add(toolId);
                    }
                }

                // Ahead of the usage, so a reply that opens the turn again counts toward it.
                entries.Insert(0, state.TurnOpen ? new LogEntry.TurnActive(date) : new LogEntry.TurnBegan(date));
                state.TurnOpen = true;
                break;
        }

        return entries;
    }

    /// <summary>The interruption or local command output the person's message holds, never text inside a tool result.</summary>
    private static bool EndsTurn(JsonElement json)
    {
        var content = json.Prop("message").Prop("content");
        var texts = new List<string>();
        if (content.Str() is { } text)
        {
            texts.Add(text);
        }
        else
        {
            foreach (var b in content.Items())
            {
                if (b.Str("type") == "text" && b.Str("text") is { } t)
                {
                    texts.Add(t);
                }
            }
        }

        return texts.Any(t => t.StartsWith("[Request interrupted by user", StringComparison.Ordinal) || t.StartsWith("<local-command-std", StringComparison.Ordinal));
    }

    private static void Adopt(JsonElement json, AgentLogState state)
    {
        if (json.Str("sessionId") is { Length: > 0 } session)
        {
            state.Session = session;
        }

        if (json.Str("cwd") is { Length: > 0 } cwd)
        {
            state.Project = AgentLogParser.ProjectName(cwd);
        }
    }
}

/// <summary>Reads Codex session logs.</summary>
internal sealed class CodexReader : IAgentHarnessReader
{
    private static readonly List<LogEntry> None = [];

    public List<LogEntry> Parse(ReadOnlyMemory<byte> memory, AgentLogState state, DateTime now)
    {
        var line = memory.Span;
        if (AgentLogParser.FirstType(line) is not { } kind)
        {
            return None;
        }

        switch (kind.Name)
        {
            case "token_usage_record" or "turn_context" or "session_meta":
                break;
            case "event_msg":
                if (AgentLogParser.FirstType(line, kind.End) is not { } ev || !AgentLogParser.CodexEvents.Contains(ev.Name))
                {
                    return None;
                }

                break;
            default:
                return None;
        }

        using var doc = AgentLogParser.Parse(memory);
        if (doc is null || doc.RootElement.Prop("payload") is not { ValueKind: JsonValueKind.Object } payload)
        {
            return None;
        }

        var json = doc.RootElement;
        var date = json.Prop("timestamp").Timestamp() ?? now;
        switch (json.Str("type"))
        {
            case "session_meta":
            {
                // History copied from another thread repeats that thread's metadata; only the first line describes this log's own thread.
                var id = payload.Str("id") ?? "";
                if (id.Length > 0)
                {
                    state.CodexTotalsSession = id;
                }

                if (state.CodexHeaderRead)
                {
                    return None;
                }

                state.CodexHeaderRead = true;
                if (id.Length > 0)
                {
                    state.Session = id;
                }

                if (payload.Str("cwd") is { Length: > 0 } cwd)
                {
                    state.Project = AgentLogParser.ProjectName(cwd);
                }

                var source = payload.Prop("source");
                state.CodexSubagent = source.Prop("subagent") is not null || source.Prop("internal") is not null;
                var root = payload.Str("session_id") ?? payload.Str("parent_thread_id") ?? "";
                if (state.CodexSubagent && root.Length > 0 && root != state.Session)
                {
                    state.CodexRoot = root;
                }

                return None;
            }

            case "turn_context":
                if (payload.Str("model") is { Length: > 0 } model)
                {
                    state.Model = model;
                }

                if (payload.Str("cwd") is { Length: > 0 } c)
                {
                    state.Project = AgentLogParser.ProjectName(c);
                }

                if (payload.Str("service_tier") is { } tier)
                {
                    state.Fast = AgentLogParser.FastTier(tier);
                }

                return None;
            case "token_usage_record":
            {
                if (payload.Prop("usage") is not { ValueKind: JsonValueKind.Object } usage)
                {
                    return None;
                }

                state.SawUsageRecords = true;
                if (payload.Str("session_id") is { Length: > 0 } sid && state.Session.Length == 0)
                {
                    state.Session = sid;
                }

                var response = payload.Str("response_id") ?? "";
                var key = response.Length == 0 ? $"codex:{state.Session}:{date.Ticks}" : $"codex:{response}";
                return [UsageEntry(AgentLogParser.CodexTokens(usage), key, date, state)];
            }

            case "event_msg":
                return Event(payload, date, state);
            default:
                return None;
        }
    }

    private static List<LogEntry> Event(JsonElement payload, DateTime date, AgentLogState state)
    {
        switch (payload.Str("type"))
        {
            case "token_count":
            {
                var entries = new List<LogEntry>();
                if (payload.Prop("rate_limits") is { ValueKind: JsonValueKind.Object } limits)
                {
                    if (AgentLogParser.IsMainBucket(limits.Str("limit_id")))
                    {
                        var windows = AgentLogParser.CodexWindows(limits, date, AgentLogParser.LogWindowKeys);
                        if (windows.Count > 0)
                        {
                            entries.Add(new LogEntry.Limits(new AgentLimits(AgentProvider.Codex, windows, date, LimitSource.SessionLog) { Origin = LimitOrigins.CodexLog }));
                        }
                    }

                    if (limits.Str("plan_type") is { Length: > 0 } plan)
                    {
                        entries.Add(new LogEntry.Plan(plan, date));
                    }
                }

                // Running totals repeat when only the limits changed; a response is whatever the total grew by since the previous reading.
                if (!state.SawUsageRecords && payload.Prop("info") is { ValueKind: JsonValueKind.Object } info
                    && info.Prop("total_token_usage") is { ValueKind: JsonValueKind.Object } totalUsage)
                {
                    var total = AgentLogParser.CodexTokens(totalUsage);
                    if (state.LastTotal != total && total.Total > (state.LastTotal?.Total ?? 0))
                    {
                        AgentTokens delta;
                        if (info.Prop("last_token_usage") is { ValueKind: JsonValueKind.Object } last)
                        {
                            delta = AgentLogParser.CodexTokens(last);
                        }
                        else
                        {
                            var p = state.LastTotal ?? default;
                            delta = new AgentTokens(Math.Max(0, total.Input - p.Input), Math.Max(0, total.CacheWrite - p.CacheWrite),
                                Math.Max(0, total.CacheRead - p.CacheRead), Math.Max(0, total.Output - p.Output), Math.Max(0, total.Reasoning - p.Reasoning));
                        }

                        entries.Add(UsageEntry(delta, $"codex:{state.CodexTotalsSession}:total:{total.Total}", date, state));
                    }

                    state.LastTotal = total;
                }

                return entries;
            }

            case "thread_settings_applied":
                if (payload.Prop("thread_settings").Str("service_tier") is { } tier)
                {
                    state.Fast = AgentLogParser.FastTier(tier);
                }

                return None;
            case "task_started":
                state.TurnOpen = true;
                return [new LogEntry.TurnBegan(payload.Prop("started_at").Seconds() ?? date)];
            case "task_complete" or "turn_aborted":
            {
                var open = state.TurnOpen;
                state.TurnOpen = false;
                if (!open)
                {
                    return None;
                }

                TimeSpan? duration = payload.Prop("duration_ms").Num() is { } ms ? TimeSpan.FromSeconds(ms / 1000) : null;
                return [new LogEntry.TurnEnded(payload.Prop("completed_at").Seconds() ?? date, payload.Str("type") == "task_complete", duration)];
            }

            default:
                return None;
        }
    }

    private static LogEntry UsageEntry(AgentTokens tokens, string key, DateTime date, AgentLogState state)
    {
        var billable = new AgentBillable { Tokens = tokens, Fast = state.Fast };
        var priced = AgentPricing.Cost(billable, state.Model);
        return new LogEntry.Usage(key, new UsageRecord
        {
            Provider = AgentProvider.Codex,
            Date = date,
            Model = state.Model,
            Project = state.Project,
            Session = state.Session,
            Tokens = tokens,
            Cost = priced.Cost,
            Savings = priced.Savings,
        }, billable);
    }
}

/// <summary>Reads the append-only event log Copilot CLI and the GitHub Copilot app share.</summary>
internal sealed class CopilotReader : IAgentHarnessReader
{
    private static readonly List<LogEntry> None = [];

    public bool ReadsFullHistoryFirst => true;

    /// <summary>Whether a line is one of the structural events (first-read filter).</summary>
    public static bool HistoryLine(ReadOnlySpan<byte> line) => AgentLogParser.CopilotEvent(AgentLogParser.TopLevelString(line, "type"));

    public List<LogEntry> Parse(ReadOnlyMemory<byte> memory, AgentLogState state, DateTime now)
    {
        if (!HistoryLine(memory.Span))
        {
            return None;
        }

        using var doc = AgentLogParser.Parse(memory);
        if (doc is null)
        {
            return None;
        }

        var envelope = doc.RootElement;
        var type = envelope.Str("type");
        if (type is null || envelope.Prop("data") is not { ValueKind: JsonValueKind.Object } data)
        {
            return None;
        }

        var agent = envelope.Str("agentId") is { Length: > 0 } a ? a : null;
        var root = agent is null;
        // Subagents share the root log. Their responses count as activity, but their lifecycle and model changes never change the root task.
        if (!root && type != "assistant.message")
        {
            return None;
        }

        var date = envelope.Prop("timestamp").Timestamp() ?? now;
        switch (type)
        {
            case "user.message" or "assistant.turn_start":
            {
                state.CopilotFinalResponse = false;
                state.CopilotTurnID = type == "assistant.turn_start" ? data.Str("turnId") : null;
                if (data.Str("model") is { Length: > 0 } m)
                {
                    state.Model = m;
                }

                LogEntry entry = state.TurnOpen ? new LogEntry.TurnActive(date) : new LogEntry.TurnBegan(date);
                state.TurnOpen = true;
                return [entry, new LogEntry.TurnContext(state.Model, state.Project)];
            }

            case "assistant.message":
            {
                var model = data.Str("model") is { Length: > 0 } dm ? dm : root ? state.Model : "";
                var entries = Activity(data, envelope, agent, model, date, state);
                if (!root)
                {
                    return entries;
                }

                state.Model = model;
                // Each tool iteration has its own turn_end. Only an iteration delivering a final response can finish the person's task.
                state.CopilotFinalResponse = !data.Prop("toolRequests").HasItems() && data.Str("phase") is not ("thinking" or "commentary");
                if (state.TurnOpen)
                {
                    entries.Add(new LogEntry.TurnContext(state.Model, state.Project));
                    entries.Add(new LogEntry.TurnActive(date));
                }

                return entries;
            }

            case "assistant.turn_end":
                if (!state.TurnOpen || (state.CopilotTurnID is not null && state.CopilotTurnID != data.Str("turnId")))
                {
                    return None;
                }

                if (!state.CopilotFinalResponse)
                {
                    return [new LogEntry.TurnActive(date)];
                }

                state.TurnOpen = false;
                state.CopilotFinalResponse = false;
                return [new LogEntry.TurnEnded(date, true, null)];
            case "session.start":
            {
                if (data.Str("sessionId") is { Length: > 0 } sid)
                {
                    state.Session = sid;
                }

                if (data.Str("selectedModel") is { Length: > 0 } sm)
                {
                    state.Model = sm;
                }

                if (data.Prop("context") is { ValueKind: JsonValueKind.Object } ctx && ContextPath(ctx) is { } path)
                {
                    state.Project = AgentLogParser.ProjectName(path);
                }

                return None;
            }

            case "session.context_changed":
                if (ContextPath(data) is { } p2)
                {
                    state.Project = AgentLogParser.ProjectName(p2);
                }

                return state.TurnOpen ? [new LogEntry.TurnContext(state.Model, state.Project)] : None;
            case "session.model_change":
                if (data.Str("newModel") is { Length: > 0 } nm)
                {
                    state.Model = nm;
                }

                return state.TurnOpen ? [new LogEntry.TurnContext(state.Model, state.Project)] : None;
            case "abort":
                if (!state.TurnOpen)
                {
                    return None;
                }

                state.TurnOpen = false;
                state.CopilotFinalResponse = false;
                return [new LogEntry.TurnEnded(date, false, null)];
            case "session.shutdown":
            {
                var entries = Usage(data.Prop("modelMetrics"), envelope.Str("id"), date, state);
                if (state.TurnOpen)
                {
                    state.TurnOpen = false;
                    entries.Add(new LogEntry.TurnEnded(date, false, null));
                }

                return entries;
            }

            default:
                return None;
        }
    }

    private static string? ContextPath(JsonElement e)
    {
        foreach (var k in new[] { "gitRoot", "cwd", "repository" })
        {
            if (e.Str(k) is { Length: > 0 } v)
            {
                return v;
            }
        }

        return null;
    }

    private static List<LogEntry> Activity(JsonElement data, JsonElement envelope, string? agent, string model, DateTime date, AgentLogState state)
    {
        var call = data.Str("apiCallId") is { Length: > 0 } c ? c : null;
        var message = data.Str("messageId") is { Length: > 0 } mid ? mid : envelope.Str("id") ?? date.Ticks.ToString();
        // Each API call can emit several messages. Older logs lack apiCallId, so they retain message-level deduplication.
        var request = (agent is not null ? "agent:" + agent : "root") + ":" + (call is not null ? "api:" + call : "message:" + message);
        var key = $"copilot:{state.Session}:{request}:activity";
        if (state.CopilotRequestModels.TryGetValue(request, out var previous))
        {
            if (previous.Length != 0 || model.Length == 0)
            {
                return [];
            }

            state.CopilotRequestModels[request] = model;
            state.CopilotRequests[previous] = state.CopilotRequests.GetValueOrDefault(previous) - 1;
            state.CopilotRequests[model] = state.CopilotRequests.GetValueOrDefault(model) + 1;
            return [new LogEntry.UsageModel(key, model)];
        }

        state.CopilotRequestModels[request] = model;
        state.CopilotRequests[model] = state.CopilotRequests.GetValueOrDefault(model) + 1;
        return
        [
            new LogEntry.Usage(key, new UsageRecord
            {
                Provider = AgentProvider.Copilot,
                Date = date,
                Model = model,
                Project = state.Project,
                Session = state.Session,
                Tokens = default,
                Cost = 0,
                Savings = 0,
            }, new AgentBillable { IsAggregate = true }),
        ];
    }

    private static List<LogEntry> Usage(JsonElement? metrics, string? eventId, DateTime date, AgentLogState state)
    {
        var entries = new List<LogEntry>();
        if (metrics is not { ValueKind: JsonValueKind.Object } m)
        {
            return entries;
        }

        var growth = new Dictionary<string, AgentTokens>();
        foreach (var prop in m.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var total = Tokens(prop.Value);
            growth[prop.Name] = Growth(total, state.CopilotTotals.TryGetValue(prop.Name, out var prev) ? prev : null);
            state.CopilotTotals[prop.Name] = total;
            var reported = (int)prop.Value.Prop("requests").Prop("count").Int();
            state.CopilotReportedRequests[prop.Name] = Math.Max(state.CopilotReportedRequests.GetValueOrDefault(prop.Name), Math.Max(reported, total.Total > 0 ? 1 : 0));
        }

        var deficits = state.CopilotReportedRequests.Select(kv => (Model: kv.Key, Count: Math.Max(0, kv.Value - state.CopilotRequests.GetValueOrDefault(kv.Key)))).ToList();
        var unresolved = state.CopilotRequests.GetValueOrDefault("");
        var missing = Math.Max(0, deficits.Sum(d => d.Count) - unresolved);
        var requests = new Dictionary<string, int>();
        // Unknown responses can cover any model's deficit, but only once across the whole session.
        foreach (var d in deficits)
        {
            var certain = Math.Max(0, d.Count - unresolved);
            requests[d.Model] = certain;
            missing -= certain;
        }

        if (missing > 0)
        {
            requests[""] = requests.GetValueOrDefault("") + missing;
        }

        foreach (var model in growth.Keys.Union(requests.Keys).OrderBy(x => x, StringComparer.Ordinal))
        {
            var tokens = growth.TryGetValue(model, out var g) ? g : default;
            var count = requests.GetValueOrDefault(model);
            state.CopilotRequests[model] = state.CopilotRequests.GetValueOrDefault(model) + count;
            if (tokens.Total <= 0 && count <= 0)
            {
                continue;
            }

            var billable = new AgentBillable { Tokens = tokens, IsAggregate = true };
            var priced = AgentPricing.Cost(billable, model);
            var checkpoint = string.IsNullOrEmpty(eventId) ? date.Ticks.ToString() : eventId;
            entries.Add(new LogEntry.Usage($"copilot:{state.Session}:{checkpoint}:{model}", new UsageRecord
            {
                Provider = AgentProvider.Copilot,
                Date = date,
                Model = model,
                Project = state.Project,
                Session = state.Session,
                Requests = count,
                Tokens = tokens,
                Cost = priced.Cost,
                Savings = priced.Savings,
            }, billable));
        }

        return entries;
    }

    private static AgentTokens Tokens(JsonElement metric)
    {
        var details = metric.Prop("tokenDetails");
        var usage = metric.Prop("usage");

        long Count(string detail, string fallback) =>
            details.Prop(detail).Prop("tokenCount") is { } v ? ((JsonElement?)v).Int() : usage.Prop(fallback).Int();

        return new AgentTokens(Count("input", "inputTokens"), Count("cache_write", "cacheWriteTokens"), Count("cache_read", "cacheReadTokens"),
            Count("output", "outputTokens"), usage.Prop("reasoningTokens").Int());
    }

    private static AgentTokens Growth(AgentTokens total, AgentTokens? previous)
    {
        if (previous is not { } p || total.Total < p.Total)
        {
            return total;
        }

        return new AgentTokens(Math.Max(0, total.Input - p.Input), Math.Max(0, total.CacheWrite - p.CacheWrite), Math.Max(0, total.CacheRead - p.CacheRead),
            Math.Max(0, total.Output - p.Output), Math.Max(0, total.Reasoning - p.Reasoning));
    }
}

/// <summary>Reads OpenCode database rows as handed over by <see cref="OpenCodeDatabase"/>.</summary>
internal sealed class OpenCodeReader : IAgentHarnessReader
{
    private static readonly List<LogEntry> None = [];

    /// <summary>A turn silent this long is not shown as working.</summary>
    public static readonly TimeSpan IdleTurn = TimeSpan.FromMinutes(10);

    public List<LogEntry> Parse(ReadOnlyMemory<byte> memory, AgentLogState state, DateTime now)
    {
        var span = memory.Span;
        if (!AgentLogParser.Contains(span, "\"role\":\""u8) && !AgentLogParser.Contains(span, "\"type\":\"reset\""u8))
        {
            return None;
        }

        using var doc = AgentLogParser.Parse(memory);
        if (doc is null)
        {
            return None;
        }

        var json = doc.RootElement;
        if (json.Str("type") == "reset")
        {
            state.OpenCodeSessions.Clear();
            state.TurnOpen = false;
            return [new LogEntry.Reset(now)];
        }

        if (json.Str("role") is not { } role)
        {
            return None;
        }

        var id = json.Str("id") ?? "";
        var session = json.Str("session_id") ?? "";
        var sessionID = session.Length == 0 ? state.Session : session;
        state.Session = sessionID;
        state.ParentSession = json.Str("parent_session_id") ?? "";

        if (!state.OpenCodeSessions.TryGetValue(sessionID, out var s))
        {
            s = new OpenCodeSessionState();
        }

        var cwd = json.Prop("path").Str("cwd") ?? json.Str("directory") ?? "";
        if (cwd.Length > 0)
        {
            s.Project = AgentLogParser.ProjectName(cwd);
        }

        var timeCreated = json.Prop("time").Prop("created") ?? json.Prop("time_created");
        var date = timeCreated.Seconds() ?? now;
        // A session quiet this long with no reply being written starts its next prompt as a task of its own.
        if (!state.OpenCodeSessions.ContainsKey(sessionID))
        {
            var drop = state.OpenCodeSessions.Where(kv => kv.Value.WritingReplyID.Length == 0 && (date - (kv.Value.LastActivity ?? date)) >= IdleTurn).Select(kv => kv.Key).ToList();
            foreach (var k in drop)
            {
                state.OpenCodeSessions.Remove(k);
            }
        }

        var lastActivity = s.LastActivity;
        s.LastActivity = lastActivity is { } la && la > date ? la : date;

        switch (role)
        {
            case "user":
            {
                if (id.Length > 0)
                {
                    // A prompt read again, as when its summary is saved, is no new one.
                    if (s.SeenUserMessageIDs.Contains(id))
                    {
                        return None;
                    }

                    Remember(id, s.SeenUserMessageIDs);
                    s.ActiveUserMessageID = id;
                }

                var entries = new List<LogEntry>();
                bool quiet;
                if (s.SettledAt is { } settled)
                {
                    quiet = date - settled >= AgentLogParser.OpenCodeSettle;
                }
                else if (s.WritingReplyID.Length == 0 && lastActivity is { } last)
                {
                    quiet = date - last >= IdleTurn;
                }
                else
                {
                    quiet = false;
                }

                s.SettledAt = null;
                s.ActivePromptDate = date;
                if (s.TurnOpen && !quiet)
                {
                    entries.Add(new LogEntry.TurnActive(date));
                }
                else
                {
                    if (s.TurnOpen)
                    {
                        entries.Add(new LogEntry.TurnEnded(lastActivity, false, null));
                    }

                    s.TurnOpen = true;
                    s.TurnStarted = date;
                    entries.Add(new LogEntry.TurnBegan(date));
                }

                if (ModelOf(json) is { Length: > 0 } um)
                {
                    s.Model = um;
                }

                state.OpenCodeSessions[sessionID] = s;
                state.Project = s.Project;
                state.Model = s.Model;
                state.TurnOpen = true;
                return entries;
            }

            case "assistant":
            {
                if (ModelOf(json) is { Length: > 0 } am)
                {
                    s.Model = am;
                }

                var parentID = json.Str("parentID") ?? "";
                var isAlreadyCompleted = (id.Length > 0 && s.CompletedAssistantMessageIDs.Contains(id))
                                         || (parentID.Length > 0 && s.CompletedUserMessageIDs.Contains(parentID));
                var matchesActivePrompt = parentID.Length == 0 || parentID == s.ActiveUserMessageID;
                var completed = json.Prop("time").Prop("completed").Seconds();
                var hasError = json.Prop("error") is { ValueKind: not JsonValueKind.Null };
                var finish = json.Str("finish");
                if (completed is { } c)
                {
                    s.LastActivity = s.LastActivity is { } l && l > c ? l : c;
                }

                // A step that starts after the last one settled is the loop going on.
                if (s.SettledAt is { } st && date >= st)
                {
                    s.SettledAt = null;
                }

                var entries = new List<LogEntry>();
                // A reply to a newer prompt while an older one never completed means OpenCode stopped in the middle of it.
                if (id.Length > 0 && s.WritingReplyID.Length > 0 && s.WritingReplyID != id && s.TurnOpen && parentID.Length > 0
                    && parentID == s.ActiveUserMessageID && s.ActivePromptDate is { } prompt && s.TurnStarted is { } started && prompt > started)
                {
                    entries.Add(new LogEntry.TurnEnded(null, false, null));
                    entries.Add(new LogEntry.TurnBegan(prompt));
                    s.TurnStarted = prompt;
                }

                if (completed is null && finish is null && !hasError && id.Length > 0)
                {
                    s.WritingReplyID = id;
                }
                else if (s.WritingReplyID == id)
                {
                    s.WritingReplyID = "";
                }

                if (!isAlreadyCompleted && matchesActivePrompt && !s.TurnOpen)
                {
                    s.TurnOpen = true;
                    s.TurnStarted = date;
                    entries.Add(new LogEntry.TurnBegan(date));
                }

                if (Usage(json, id, sessionID, date, s.Model, s.Project) is { } usage)
                {
                    entries.Add(usage);
                }

                // OpenCode's loop goes on after tool calls; any other finish stops it, as does an error or a reply that completes without a finish.
                bool ends;
                if (hasError)
                {
                    ends = true;
                }
                else if (finish is not null)
                {
                    ends = finish is not ("tool-calls" or "unknown") && json.Prop("tool_calls").Bool() != true && json.Prop("auto_compaction").Bool() != true;
                }
                else
                {
                    ends = completed is not null;
                }

                if (ends)
                {
                    if (!isAlreadyCompleted && matchesActivePrompt && s.TurnOpen)
                    {
                        var endDate = completed ?? json.Prop("time_updated").Seconds() ?? date;
                        TimeSpan? duration = null;
                        if (s.TurnStarted is { } ts)
                        {
                            if (endDate >= ts)
                            {
                                duration = endDate - ts;
                            }
                        }
                        else if (endDate >= date)
                        {
                            duration = endDate - date;
                        }

                        s.TurnOpen = false;
                        s.TurnStarted = null;
                        s.ActiveUserMessageID = "";
                        s.SettledAt = null;
                        entries.Add(new LogEntry.TurnEnded(endDate, !hasError, duration));
                    }

                    if (id.Length > 0)
                    {
                        Remember(id, s.CompletedAssistantMessageIDs);
                    }

                    if (parentID.Length > 0)
                    {
                        Remember(parentID, s.CompletedUserMessageIDs);
                    }
                }
                else if (!isAlreadyCompleted && matchesActivePrompt)
                {
                    // A step that completed hands over to the next one at once, unless the loop stopped there.
                    if (completed is { } done && s.TurnOpen)
                    {
                        if (json.Prop("version").Num() == 2)
                        {
                            entries.Add(new LogEntry.TurnActive(done));
                        }
                        else
                        {
                            s.SettledAt = done;
                            entries.Add(new LogEntry.TurnSettled(done));
                        }
                    }
                    else
                    {
                        entries.Add(new LogEntry.TurnActive(date));
                    }
                }

                state.OpenCodeSessions[sessionID] = s;
                state.Project = s.Project;
                state.Model = s.Model;
                state.TurnOpen = s.TurnOpen;
                return entries;
            }

            case "compaction":
            {
                var own = json.Prop("model").Str("id");
                var entries = new List<LogEntry>();
                if (Usage(json, id, sessionID, date, own is { Length: > 0 } ? own : s.Model, s.Project) is { } usage)
                {
                    entries.Add(usage);
                }

                if (s.TurnOpen)
                {
                    var updated = json.Prop("time_updated").Seconds() ?? date;
                    var moment = updated > date ? updated : date;
                    s.LastActivity = s.LastActivity is { } l && l > moment ? l : moment;
                    s.SettledAt = null;
                    entries.Add(new LogEntry.TurnActive(moment));
                }

                state.OpenCodeSessions[sessionID] = s;
                state.Project = s.Project;
                state.Model = s.Model;
                state.TurnOpen = s.TurnOpen;
                return entries;
            }

            case "idle":
            {
                s.WritingReplyID = "";
                s.SettledAt = null;
                var entries = new List<LogEntry>();
                if (s.TurnOpen)
                {
                    TimeSpan? duration = null;
                    if (s.TurnStarted is { } ts && date >= ts)
                    {
                        duration = date - ts;
                    }

                    s.TurnOpen = false;
                    s.TurnStarted = null;
                    s.ActiveUserMessageID = "";
                    entries.Add(new LogEntry.TurnEnded(date, json.Str("outcome") == "succeeded", duration));
                }

                state.OpenCodeSessions[sessionID] = s;
                state.TurnOpen = false;
                return entries;
            }

            default:
                return None;
        }
    }

    private static string? ModelOf(JsonElement json) =>
        json.Str("model_id") ?? json.Str("modelID") ?? json.Prop("model").Str("modelID") ?? json.Prop("model").Str("id") ?? json.Str("model");

    private static void Remember(string id, HashSet<string> ids)
    {
        if (ids.Count > 100)
        {
            ids.Remove(ids.First());
        }

        ids.Add(id);
    }

    private static LogEntry? Usage(JsonElement json, string id, string session, DateTime date, string model, string project)
    {
        var tokensEl = json.Prop("tokens");
        var reasoning = tokensEl.Prop("reasoning").Int();
        var cache = tokensEl.Prop("cache");
        var tokens = new AgentTokens(tokensEl.Prop("input").Int(), cache.Prop("write").Int(), cache.Prop("read").Int(), tokensEl.Prop("output").Int() + reasoning, reasoning);
        double? recorded = json.Prop("cost").Num();
        if (tokens.Total <= 0 && !((recorded ?? 0) > 0))
        {
            return null;
        }

        var billable = new AgentBillable { Tokens = tokens };
        var priced = AgentPricing.Cost(billable, model);
        // A model the list does not know costs what OpenCode recorded.
        double? cost;
        bool isReported;
        if (priced.Cost is { } calc)
        {
            cost = calc;
            isReported = false;
        }
        else if (recorded is { } rec)
        {
            cost = rec;
            isReported = rec > 0;
        }
        else
        {
            cost = null;
            isReported = false;
        }

        var key = $"opencode:{session}:{(id.Length == 0 ? date.Ticks.ToString() : id)}";
        return new LogEntry.Usage(key, new UsageRecord
        {
            Provider = AgentProvider.OpenCode,
            Date = date,
            Model = model,
            Project = project,
            Session = session,
            Tokens = tokens,
            Cost = cost,
            Savings = priced.Savings,
            ReportedCost = isReported,
        }, billable);
    }
}
