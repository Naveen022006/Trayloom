// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace WinKnife.Features.Agents;

/// <summary>
/// One conversation with an agent CLI over its standard input and output, a JSON message a line. Every answer waits on one deadline,
/// so a server that stalls costs the conversation's time at most. Blocks its caller: run it on a work thread of its own.
/// </summary>
internal class AgentCliConversation : IDisposable
{
    /// <summary>Far above any answer here; a server that writes more is not one.</summary>
    private const int MaximumBuffer = 4 << 20;

    private readonly Process _process;
    private readonly BlockingCollection<string> _lines = [];
    private readonly DateTime _deadline;
    private readonly CancellationToken _cancel;
    private int _characters;
    private bool _ended;

    private AgentCliConversation(Process process, TimeSpan timeout, CancellationToken cancel)
    {
        _process = process;
        _deadline = DateTime.UtcNow + timeout;
        _cancel = cancel;
    }

    /// <summary>Starts <paramref name="executable"/> with piped input/output (stderr discarded, no window, in a kill-on-close job).</summary>
    public static AgentCliConversation? Start(string executable, IEnumerable<string> arguments, Dictionary<string, string> environment, TimeSpan timeout,
        CancellationToken cancel = default, string? workingDirectory = null)
    {
        var info = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = Encoding.UTF8,
            // Nothing here needs a folder; the system folder keeps a project's files out of the server's way.
            WorkingDirectory = workingDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.System),
        };
        var ext = Path.GetExtension(executable);
        if (ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || ext.Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            // A shim is started as cmd.exe /d /c "<path>" <args>.
            info.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            var line = new StringBuilder("/d /s /c \"").Append('"').Append(executable).Append('"');
            foreach (var a in arguments)
            {
                line.Append(' ').Append(QuoteForShim(a));
            }

            line.Append('"');
            info.Arguments = line.ToString();
        }
        else
        {
            info.FileName = executable;
            foreach (var a in arguments)
            {
                info.ArgumentList.Add(a);
            }
        }

        info.Environment.Clear();
        foreach (var (k, v) in environment)
        {
            info.Environment[k] = v;
        }

        Process? process;
        try
        {
            process = Process.Start(info);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }

        if (process is null)
        {
            return null;
        }

        AgentNative.BindToJob(process);
        var conversation = new AgentCliConversation(process, timeout, cancel);
        conversation.StartReaders();
        return conversation;
    }

    private static string QuoteForShim(string arg)
    {
        if (arg.Length == 0)
        {
            return "\"\"";
        }

        return "\"" + arg.Replace("\"", "\\\"") + "\"";
    }

    private void StartReaders()
    {
        var reader = new Thread(() =>
        {
            try
            {
                string? line;
                while ((line = _process.StandardOutput.ReadLine()) is not null)
                {
                    _characters += line.Length;
                    if (_characters > MaximumBuffer)
                    {
                        break;
                    }

                    _lines.Add(line);
                }
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or ObjectDisposedException)
            {
            }
            finally
            {
                try
                {
                    _lines.CompleteAdding();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        })
        { IsBackground = true, Name = "agent-cli-out" };
        reader.Start();

        // stderr is discarded, but must be drained so the child never blocks on a full pipe.
        var drain = new Thread(() =>
        {
            try
            {
                var buffer = new char[1024];
                while (_process.StandardError.Read(buffer, 0, buffer.Length) > 0)
                {
                }
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or ObjectDisposedException)
            {
            }
        })
        { IsBackground = true, Name = "agent-cli-err" };
        drain.Start();
    }

    public bool Send(string json)
    {
        if (_cancel.IsCancellationRequested || DateTime.UtcNow >= _deadline)
        {
            return false;
        }

        try
        {
            _process.StandardInput.Write(json + "\n");
            _process.StandardInput.Flush();
            return true;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>The next JSON object message (caller disposes), or null once the server has ended or time is up.</summary>
    public JsonDocument? Next()
    {
        while (true)
        {
            if (_cancel.IsCancellationRequested)
            {
                return null;
            }

            var remaining = _deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return null;
            }

            string? line;
            try
            {
                if (!_lines.TryTake(out line, (int)Math.Min(remaining.TotalMilliseconds, 100), _cancel))
                {
                    if (_lines.IsCompleted)
                    {
                        return null;
                    }

                    continue;
                }
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (ObjectDisposedException)
            {
                return null;
            }

            try
            {
                var doc = JsonDocument.Parse(line);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    return doc;
                }

                doc.Dispose();
            }
            catch (JsonException)
            {
            }
        }
    }

    /// <summary>Closing its input ends the server; one that lingers is stopped.</summary>
    public void End()
    {
        if (_ended)
        {
            return;
        }

        _ended = true;
        try
        {
            _process.StandardInput.Close();
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ObjectDisposedException)
        {
        }

        var process = _process;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_cancel.IsCancellationRequested ? 300 : 2000);
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
            }
        });
    }

    /// <summary>After <see cref="End"/>: waits for the server to exit, so what it saves on its way out is on disk.</summary>
    public bool WaitForExit(TimeSpan timeout)
    {
        try
        {
            return _process.WaitForExit((int)timeout.TotalMilliseconds);
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    public void Dispose()
    {
        End();
    }
}

/// <summary>Why a Claude Code check did not produce a reading.</summary>
public enum ClaudeFailure
{
    Missing,
    NeedsSignIn,
    Outdated,
    Unreachable,
}

/// <summary>A Claude Code answer for the account.</summary>
internal sealed record ClaudeSummary(AgentLimits Limits, ClaudeAccountProfile.Identity? Identity, List<string>? Raw = null);

/// <summary>
/// Reads the same account-wide data as /usage through Claude Code's structured control protocol. Claude handles authentication itself.
/// No user message, model request, MCP server or tool is run.
/// </summary>
internal static class ClaudeServer
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(25);

    /// <summary>While the AI page is on screen a reading this old is asked again.</summary>
    public static readonly TimeSpan PageFreshness = TimeSpan.FromMinutes(3);

    public static readonly TimeSpan BackgroundFreshness = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan SettledRetry = TimeSpan.FromMinutes(30);

    /// <summary>Claude Code answers from the copy it saved, without asking the account, while that copy is younger than this.</summary>
    public static readonly TimeSpan Reuse = TimeSpan.FromSeconds(60);

    private static readonly string[] OptOuts = ["DISABLE_TELEMETRY", "DISABLE_ERROR_REPORTING", "CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"];
    private static readonly string[] NetworkSettings = ["HTTPS_PROXY", "HTTP_PROXY", "NO_PROXY", "NODE_EXTRA_CA_CERTS"];

    /// <summary>How long after the last check Claude Code is asked again.</summary>
    public static TimeSpan Interval(ClaudeFailure? failure, int repeated, bool pageVisible) => failure switch
    {
        null => pageVisible ? PageFreshness : BackgroundFreshness,
        ClaudeFailure.Unreachable => TimeSpan.FromSeconds(Math.Min(SettledRetry.TotalSeconds, RetryAfter.TotalSeconds * (1 << Math.Min(3, Math.Max(0, repeated - 1))))),
        _ => SettledRetry,
    };

    /// <summary>Folders Claude Code can live in on Windows (the Claude desktop app's own copy is left out: it has no sign-in of its own).</summary>
    public static List<string> ToolFolders(string home)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return
        [
            Path.Combine(home, ".local", "bin"),
            Path.Combine(home, ".claude", "local"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"),
            Path.Combine(local, "Volta", "bin"),
            Path.Combine(local, "pnpm"),
            Path.Combine(home, ".bun", "bin"),
            Path.Combine(home, ".npm-global"),
        ];
    }

    public static IEnumerable<string> Candidates(string home, string? searchPath)
    {
        var folders = ToolFolders(home).Concat((searchPath ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim().Trim('"')));
        foreach (var folder in folders)
        {
            if (!Path.IsPathRooted(folder) || folder.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase) || folder.Contains("AnthropicClaude", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return Path.Combine(folder, "claude.exe");
            yield return Path.Combine(folder, "claude.cmd");
        }
    }

    /// <summary>The PATH a check gives Claude Code: the app's, then the tool folders.</summary>
    public static string SearchPath(string path, string home) => string.Join(';', new[] { path }.Concat(ToolFolders(home)).Where(p => p.Length > 0));

    public static string? Executable(string home, string? searchPath) =>
        Candidates(home, searchPath).FirstOrDefault(c => File.Exists(c));

    /// <summary>The app's environment for a check: Claude Code never updates itself from one, and the opt-outs and proxy the person keeps apply.</summary>
    public static Dictionary<string, string> BuildEnvironment(IDictionary baseEnvironment, string home, ClaudeAccountProfile profile)
    {
        // The app may itself have been launched by another agent or account. Its tokens, provider routing and config overrides must not
        // replace the chosen profile's own sign-in. Network settings still apply.
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry e in baseEnvironment)
        {
            var key = (string)e.Key;
            if (OptOuts.Contains(key, StringComparer.OrdinalIgnoreCase) || (!key.StartsWith("CLAUDE_", StringComparison.OrdinalIgnoreCase) && !key.StartsWith("ANTHROPIC_", StringComparison.OrdinalIgnoreCase)))
            {
                result[key] = e.Value?.ToString() ?? "";
            }
        }

        result.Remove("CLAUDECODE");
        if (!profile.IsDefault)
        {
            result["CLAUDE_CONFIG_DIR"] = profile.Directory;
        }

        result["DISABLE_AUTOUPDATER"] = "1";
        try
        {
            var settings = Path.Combine(profile.ConfigurationFolder(home), "settings.json");
            if (!File.Exists(settings))
            {
                return result;
            }

            using var stream = new FileStream(settings, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > 1 << 20)
            {
                return result;
            }

            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.Prop("env") is not { ValueKind: JsonValueKind.Object } env)
            {
                return result;
            }

            // Claude Code lets its settings override the environment, and reads a number or a switch there as text.
            foreach (var key in OptOuts.Concat(NetworkSettings))
            {
                if (env.Prop(key) is not { } v)
                {
                    continue;
                }

                result[key] = v.ValueKind switch
                {
                    JsonValueKind.String => v.GetString() ?? "",
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Number => v.GetRawText(),
                    _ => result.GetValueOrDefault(key, ""),
                };
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
        }

        return result;
    }

    private static readonly (string Key, string Id, LimitKind Kind, int Minutes, string? Scope)[] Mapped =
    [
        ("five_hour", "fh", LimitKind.Session, 300, null),
        ("seven_day", "sd", LimitKind.Weekly, 10_080, null),
        ("seven_day_opus", "so", LimitKind.Weekly, 10_080, "Opus"),
        ("seven_day_sonnet", "sn", LimitKind.Weekly, 10_080, "Sonnet"),
    ];

    /// <summary>
    /// The <c>rate_limits</c> object of a control answer (or <c>cachedUsageUtilization.utilization</c>) as limit windows. The
    /// <c>limits</c> array is read first (it carries severity and whether a window is running); the older per-window keys fill
    /// whatever it leaves out. Nothing is invented: a window with no reset time keeps none.
    /// </summary>
    public static AgentLimits? Limits(JsonElement rateLimits, DateTime observed, string origin = LimitOrigins.ClaudeCli)
    {
        var windows = new List<LimitWindow>();
        var have = new HashSet<string>();
        foreach (var item in rateLimits.Prop("limits").Items())
        {
            if (item.Prop("percent").Num() is not { } percent)
            {
                continue;
            }

            var kind = item.Str("kind") ?? "";
            var group = item.Str("group") ?? "";
            DateTime? end = null;
            var resets = item.Prop("resets_at");
            if (resets is { ValueKind: not JsonValueKind.Null })
            {
                end = resets.Str() is { } text ? AgentTime.Parse(text) : null;
                if (end is not { } e || e <= observed)
                {
                    continue;
                }
            }

            string id;
            LimitKind limitKind;
            int? minutes;
            string? scope = item.Str("scope");
            string? label = null;
            if (kind == "session" || group == "session")
            {
                (id, limitKind, minutes) = ("claude.fh", LimitKind.Session, 300);
            }
            else if (kind.StartsWith("weekly", StringComparison.Ordinal) || group == "weekly")
            {
                var suffix = kind.StartsWith("weekly_", StringComparison.Ordinal) ? kind[7..] : "";
                (id, limitKind, minutes) = (suffix switch { "all" or "" => "claude.sd", "opus" => "claude.so", "sonnet" => "claude.sn", _ => "claude." + kind }, LimitKind.Weekly, 10_080);
                scope ??= suffix switch { "all" or "" => null, "opus" => "Opus", "sonnet" => "Sonnet", _ => Pretty(suffix) };
            }
            else
            {
                (id, limitKind, minutes) = ("claude." + (kind.Length == 0 ? "other" : kind), LimitKind.Other, null);
                label = Pretty(kind);
            }

            if (!have.Add(id))
            {
                continue;
            }

            windows.Add(new LimitWindow(id, limitKind, minutes, scope, Math.Min(100, Math.Max(0, percent)), end)
            {
                Severity = item.Str("severity"),
                Active = item.Prop("is_active").Bool() ?? (end is not null ? true : null),
                Label = label,
            });
        }

        foreach (var m in Mapped)
        {
            if (have.Contains("claude." + m.Id) || rateLimits.Prop(m.Key) is not { ValueKind: JsonValueKind.Object } window || window.Prop("utilization").Num() is not { } percent)
            {
                continue;
            }

            // An idle window has no renewal yet and keeps its percentage; one that renewed before the reading says nothing about the window now.
            DateTime? end = null;
            var resets = window.Prop("resets_at");
            if (resets is { ValueKind: not JsonValueKind.Null })
            {
                end = resets.Str() is { } text ? AgentTime.Parse(text) : null;
                if (end is not { } e || e <= observed)
                {
                    continue;
                }
            }

            windows.Add(new LimitWindow("claude." + m.Id, m.Kind, m.Minutes, m.Scope, Math.Min(100, Math.Max(0, percent)), end) { Active = end is not null });
        }

        List<LimitShare>? breakdown = null;
        DateTime? asOf = null;
        if (rateLimits.Prop("seven_day_breakdown") is { ValueKind: JsonValueKind.Object } b)
        {
            asOf = b.Str("as_of") is { } s ? AgentTime.Parse(s) : null;
            breakdown = [];
            foreach (var row in b.Prop("rows").Items())
            {
                if (row.Prop("percent").Num() is { } p)
                {
                    var key = row.Str("key") ?? "";
                    breakdown.Add(new LimitShare(key, row.Str("display_name") ?? Pretty(key), Math.Min(100, Math.Max(0, p))));
                }
            }

            if (breakdown.Count == 0)
            {
                breakdown = null;
            }
        }

        windows = windows.OrderBy(w => w.Kind).ThenBy(w => w.Scope is not null).ToList();
        return windows.Count == 0 ? null : new AgentLimits(AgentProvider.Claude, windows, observed, LimitSource.Account) { Origin = origin, Breakdown = breakdown, BreakdownAsOf = asOf };
    }

    private static string Pretty(string key) => key.Length == 0 ? "Other" : char.ToUpperInvariant(key[0]) + key[1..].Replace('_', ' ');

    /// <summary>The figures of an answer exactly as the source gave them, one line each (the diagnostics list).</summary>
    public static List<string> Describe(JsonElement rateLimits)
    {
        var lines = new List<string>();
        foreach (var m in Mapped)
        {
            if (rateLimits.Prop(m.Key) is { ValueKind: JsonValueKind.Object } w)
            {
                lines.Add($"{m.Key}: utilization {Raw(w.Prop("utilization"))}, resets_at {Raw(w.Prop("resets_at"))}");
            }
        }

        foreach (var item in rateLimits.Prop("limits").Items())
        {
            lines.Add($"limits[] {item.Str("kind")}: percent {Raw(item.Prop("percent"))}, severity {item.Str("severity") ?? "-"}, is_active {Raw(item.Prop("is_active"))}, resets_at {Raw(item.Prop("resets_at"))}");
        }

        if (rateLimits.Prop("seven_day_breakdown") is { ValueKind: JsonValueKind.Object } b)
        {
            lines.Add("seven_day_breakdown (as of " + (b.Str("as_of") ?? "?") + "): " + string.Join(", ", b.Prop("rows").Items().Select(r => (r.Str("display_name") ?? r.Str("key")) + " " + Raw(r.Prop("percent")) + "%")));
        }

        return lines;
    }

    private static string Raw(JsonElement? e) => e is { } x ? (x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : x.GetRawText()) : "absent";

    /// <summary>Expires each window without inventing zero usage after a reset.</summary>
    public static AgentLimits? Current(AgentLimits? reading, DateTime now)
    {
        if (reading is null)
        {
            return null;
        }

        var age = now - reading.ObservedAt;
        var windows = reading.Windows.Where(w =>
        {
            if (w.ResetsAt is { } resets)
            {
                return resets > now;
            }

            return age < (w.Kind == LimitKind.Session && w.Active != false ? TimeSpan.FromMinutes(w.Minutes ?? 300) : TimeSpan.FromHours(24));
        }).ToList();
        return windows.Count == 0 ? null : reading with { Windows = windows };
    }

    /// <summary>A cancellable, short-lived CLI invocation; pause/disable closes it too.</summary>
    public sealed class Request
    {
        private readonly CancellationTokenSource _cancel = new();
        private AgentCliConversation? _conversation;
        private readonly object _gate = new();

        public bool IsCancelled => _cancel.IsCancellationRequested;

        public void Cancel()
        {
            AgentCliConversation? active;
            lock (_gate)
            {
                active = _conversation;
            }

            _cancel.Cancel();
            active?.End();
        }

        public (ClaudeSummary? Summary, ClaudeFailure? Failure) Run(string executable, string home, Dictionary<string, string> environment, ClaudeAccountProfile profile, TimeSpan? timeout = null)
        {
            if (_cancel.IsCancellationRequested)
            {
                return (null, ClaudeFailure.Unreachable);
            }

            if (!profile.IsAvailable(home))
            {
                return (null, ClaudeFailure.NeedsSignIn);
            }

            var started = DateTime.UtcNow;
            var identity = profile.Read(home).Identity;
            string[] arguments =
            [
                "--print", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose", "--no-session-persistence",
                "--tools", "", "--strict-mcp-config", "--mcp-config", "{\"mcpServers\":{}}", "--setting-sources", "", "--settings", "{\"disableAllHooks\":true}",
            ];
            using var channel = AgentCliConversation.Start(executable, arguments, environment, timeout ?? Timeout, _cancel.Token);
            if (channel is null)
            {
                return (null, ClaudeFailure.Unreachable);
            }

            lock (_gate)
            {
                if (_cancel.IsCancellationRequested)
                {
                    channel.End();
                    return (null, ClaudeFailure.Unreachable);
                }

                _conversation = channel;
            }

            try
            {
                var init = Ask(channel, "initialize", "{\"subtype\":\"initialize\"}");
                init.Result?.Dispose();
                if (init.Failure is { } f)
                {
                    return (null, f);
                }

                var usage = Ask(channel, "usage", "{\"subtype\":\"get_usage\",\"skip_behaviors\":true}");
                if (usage.Failure is { } uf)
                {
                    return (null, uf);
                }

                using var doc = usage.Result!;
                var result = doc.RootElement;
                if (_cancel.IsCancellationRequested)
                {
                    return (null, ClaudeFailure.Unreachable);
                }

                if (result.Prop("rate_limits_available").Bool() != true)
                {
                    return (null, ClaudeFailure.NeedsSignIn);
                }

                // The saved copy is read once Claude Code has exited, so what it fetched for this answer is on disk.
                channel.End();
                channel.WaitForExit(TimeSpan.FromSeconds(3));
                var saved = profile.Read(home);
                if (_cancel.IsCancellationRequested || identity != saved.Identity || !profile.IsAvailable(home))
                {
                    return (null, ClaudeFailure.Unreachable);
                }

                // A copy saved before this check began was answered in place of the account, which was not reached.
                if (saved.UsageFetched is { } fetched && started - fetched > Reuse)
                {
                    return (null, ClaudeFailure.Unreachable);
                }

                var observed = DateTime.UtcNow;
                if (saved.UsageFetched is { } at && at < observed)
                {
                    observed = at;
                }

                var raw = result.Prop("rate_limits") is { ValueKind: JsonValueKind.Object } rl ? Describe(rl) : [];
                var reading = result.Prop("rate_limits") is { ValueKind: JsonValueKind.Object } limits ? Limits(limits, observed) : null;
                return reading is null ? (null, ClaudeFailure.Unreachable) : (new ClaudeSummary(reading, identity, raw), null);
            }
            finally
            {
                lock (_gate)
                {
                    _conversation = null;
                }

                channel.End();
            }
        }

        private (JsonDocument? Result, ClaudeFailure? Failure) Ask(AgentCliConversation channel, string id, string request)
        {
            if (!channel.Send("{\"type\":\"control_request\",\"request_id\":\"" + id + "\",\"request\":" + request + "}"))
            {
                return (null, ClaudeFailure.Unreachable);
            }

            while (channel.Next() is { } message)
            {
                using (message)
                {
                    var root = message.RootElement;
                    if (root.Str("type") != "control_response" || root.Prop("response") is not { ValueKind: JsonValueKind.Object } response || response.Str("request_id") != id)
                    {
                        continue;
                    }

                    if (response.Str("subtype") != "success")
                    {
                        var error = (response.Str("error") ?? "").ToLowerInvariant();
                        // A CLI from before the request answers "Unsupported control request subtype".
                        if (error.Contains("supported") || error.Contains("unknown"))
                        {
                            return (null, ClaudeFailure.Outdated);
                        }

                        if (error.Contains("auth") || error.Contains("login") || error.Contains("sign in"))
                        {
                            return (null, ClaudeFailure.NeedsSignIn);
                        }

                        return (null, ClaudeFailure.Unreachable);
                    }

                    // Keep the inner answer alive past the message's disposal.
                    var inner = response.Prop("response");
                    var copy = inner is { ValueKind: JsonValueKind.Object } o ? JsonDocument.Parse(o.GetRawText()) : JsonDocument.Parse("{}");
                    return (copy, null);
                }
            }

            return (null, ClaudeFailure.Unreachable);
        }
    }
}
