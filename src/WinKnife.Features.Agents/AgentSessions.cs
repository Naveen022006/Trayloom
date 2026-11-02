// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text;
using System.Text.Json;
using WinKnife.Core;

namespace WinKnife.Features.Agents;

public enum AgentSessionState
{
    Working,
    Waiting,
    Idle,
}

/// <summary>How a "waiting for you" state was learned; the heuristic is the only one that can be wrong.</summary>
public enum WaitingBasis
{
    None,

    /// <summary>A Claude Code hook told WinKnife (opt-in, reliable).</summary>
    Hook,

    /// <summary>The agent's own status file said so.</summary>
    Status,

    /// <summary>The agent logged an approval request.</summary>
    Approval,

    /// <summary>A tool call has had no result for a while and nothing is running: probably a permission prompt.</summary>
    Heuristic,
}

/// <summary>One live agent session: what it is, what it is doing and whether it waits for the person.</summary>
public sealed class AgentSession
{
    public string Key { get; set; } = "";

    public AgentProvider Provider { get; set; }

    public string Id { get; set; } = "";

    public string Project { get; set; } = "";

    /// <summary>The name the person gave the session, when it has one.</summary>
    public string? Title { get; set; }

    /// <summary>Where it runs: "Claude app", "Terminal", "VS Code"...</summary>
    public string Origin { get; set; } = "";

    public int? Pid { get; set; }

    public AgentSessionState State { get; set; }

    public WaitingBasis Basis { get; set; }

    /// <summary>"Thinking", "Replying", "Editing Program.cs", "Waiting for permission: Bash"...</summary>
    public string Activity { get; set; } = "";

    /// <summary>The tool a permission request is about.</summary>
    public string? Tool { get; set; }

    /// <summary>Start of the current turn while working; of the wait while waiting; of the quiet while idle.</summary>
    public DateTime Since { get; set; }

    public DateTime LastActivity { get; set; }

    /// <summary>Subagents working for this session right now.</summary>
    public int Agents { get; set; }

    public bool IsWaiting => State == AgentSessionState.Waiting;
}

/// <summary>A session started waiting for the person (raised once per wait).</summary>
public sealed record AgentAttention(string Key, AgentProvider Provider, string Project, string? Tool, string Message, WaitingBasis Basis, string SessionId, int? Pid, string Origin, DateTime At)
{
    /// <summary>The words of the island notice.</summary>
    public string Title => Message.Length > 0 ? Message : Provider.DisplayName() + " needs your permission";
}

/// <summary>Where the monitor looks. The defaults are the real folders; tests point them at fixtures.</summary>
public sealed class AgentSessionPaths
{
    public string ClaudeDir { get; set; } = "";

    public string CodexDir { get; set; } = "";

    public string EventsDir { get; set; } = "";

    public Func<int, long?, bool>? IsAlive { get; set; }

    public Func<bool>? CodexRunning { get; set; }

    /// <summary>Points the monitor at fixture folders (screenshots and tests); null in the app.</summary>
    public static AgentSessionPaths? Override { get; set; }

    public static AgentSessionPaths Default(ClaudeAccountProfile? profile = null)
    {
        if (Override is { } fixture)
        {
            return fixture;
        }

        var home = AgentLogRoot.Home;
        var codex = Environment.GetEnvironmentVariable("CODEX_HOME");
        return new AgentSessionPaths
        {
            ClaudeDir = (profile ?? new ClaudeAccountProfile()).ConfigurationFolder(home),
            CodexDir = !string.IsNullOrWhiteSpace(codex) ? codex : Path.Combine(home, ".codex"),
            EventsDir = AgentHooks.EventsFolder,
        };
    }
}

/// <summary>
/// Finds the agent sessions that are live right now and what each is doing, from files the agents write themselves: Claude Code's
/// <c>sessions\*.json</c> records (kept while a process runs) and transcript tails, Codex's rollout logs, plus the events of the
/// optional Claude Code hook. Read-only; transcript text is looked at only for its structure and never kept.
/// </summary>
public sealed class AgentSessionMonitor : IDisposable
{
    /// <summary>A tool call without a result for this long, with nothing running and nothing written, is probably a permission prompt.</summary>
    public static readonly TimeSpan PendingAfter = TimeSpan.FromSeconds(8);

    /// <summary>Tools that wait on the network or another server take longer to answer in the ordinary way.</summary>
    public static readonly TimeSpan SlowPendingAfter = TimeSpan.FromSeconds(20);

    /// <summary>An idle session that has not moved for this long drops off the list (its process may well still run).</summary>
    public static readonly TimeSpan IdleKeep = TimeSpan.FromHours(8);

    private readonly object _gate = new();
    private readonly AgentSessionPaths _paths;
    private readonly Func<DateTime> _clock;
    private readonly Dictionary<string, TailInfo> _tails = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (long Length, DateTime Time)> _tailKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _codexProjects = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _waitingSince = new();
    private readonly Dictionary<string, int> _pendingSeen = new();
    private readonly Dictionary<string, (DateTime At, string? Tool)> _raised = new();
    private readonly HashSet<string> _approvedHooks = new();
    private Timer? _timer;
    private TimeSpan _period = TimeSpan.FromSeconds(3);
    private bool _busy;
    private string _signature = "";

    public AgentSessionMonitor(AgentSessionPaths? paths = null, Func<DateTime>? clock = null)
    {
        _paths = paths ?? AgentSessionPaths.Default();
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    /// <summary>The latest scan: waiting sessions first, then working, then idle by recency.</summary>
    public IReadOnlyList<AgentSession> Current { get; private set; } = [];

    /// <summary>Whether this provider's sessions are looked for.</summary>
    public bool ClaudeEnabled { get; set; } = true;

    public bool CodexEnabled { get; set; } = true;

    /// <summary>Raised (on the timer thread) when the list changed in a way a display shows.</summary>
    public event Action? Changed;

    /// <summary>Raised (on the timer thread) when a session starts waiting for the person.</summary>
    public event Action<AgentAttention>? Attention;

    public void Start(TimeSpan? period = null)
    {
        _period = period ?? _period;
        _timer?.Dispose();
        _timer = new Timer(_ => Tick(), null, TimeSpan.FromMilliseconds(300), _period);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        Current = [];
        _signature = "";
        lock (_gate)
        {
            _tails.Clear();
            _tailKeys.Clear();
            _waitingSince.Clear();
            _pendingSeen.Clear();
            _raised.Clear();
        }
    }

    /// <summary>A quicker look while something is on screen that shows the sessions.</summary>
    public void SetWatching(bool watching)
    {
        var period = watching ? TimeSpan.FromSeconds(1.5) : TimeSpan.FromSeconds(4);
        if (period != _period && _timer is not null)
        {
            _period = period;
            _timer.Change(TimeSpan.Zero, period);
        }
    }

    public void Dispose() => Stop();

    private void Tick()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            Refresh();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>One look at the machine. Public so a harness can drive it with a fixed clock.</summary>
    public IReadOnlyList<AgentSession> Refresh()
    {
        var now = _clock();
        List<AgentSession> sessions;
        List<AgentAttention> raised = [];
        lock (_gate)
        {
            var hooks = AgentHooks.ReadEvents(_paths.EventsDir, now);
            var all = AgentProcesses.All();
            sessions = [];
            if (ClaudeEnabled)
            {
                sessions.AddRange(ScanClaude(now, all, hooks));
            }

            if (CodexEnabled)
            {
                sessions.AddRange(ScanCodex(now, all));
            }

            sessions = sessions.OrderBy(s => s.State switch { AgentSessionState.Waiting => 0, AgentSessionState.Working => 1, _ => 2 })
                .ThenByDescending(s => s.State == AgentSessionState.Idle ? s.LastActivity : s.Since).ToList();
            ApplyWaiting(sessions, now, raised);
            DropStale(sessions);
        }

        var signature = string.Join("|", sessions.Select(s => $"{s.Key}:{s.State}:{s.Activity}:{s.Agents}:{s.Project}"));
        Current = sessions;
        foreach (var a in raised)
        {
            Attention?.Invoke(a);
        }

        if (signature != _signature)
        {
            _signature = signature;
            Changed?.Invoke();
        }

        return sessions;
    }

    // ------------------------------------------------------------------------------------------ waiting bookkeeping

    private void ApplyWaiting(List<AgentSession> sessions, DateTime now, List<AgentAttention> raised)
    {
        foreach (var s in sessions)
        {
            if (!s.IsWaiting)
            {
                _waitingSince.Remove(s.Key);
                _pendingSeen.Remove(s.Key);
                continue;
            }

            // The heuristic must hold on two looks in a row, so a result that lands a moment later never flashes a notice.
            if (s.Basis == WaitingBasis.Heuristic)
            {
                _pendingSeen[s.Key] = _pendingSeen.GetValueOrDefault(s.Key) + 1;
                if (_pendingSeen[s.Key] < 2)
                {
                    s.State = AgentSessionState.Working;
                    s.Basis = WaitingBasis.None;
                    s.Activity = s.Tool is { } t ? "Running " + t : Loc.T("Working");
                    continue;
                }
            }

            if (!_waitingSince.ContainsKey(s.Key))
            {
                _waitingSince[s.Key] = s.Since;
            }

            if (_raised.TryGetValue(s.Key, out var last) && last.Tool == s.Tool && now - last.At < TimeSpan.FromMinutes(10))
            {
                continue;
            }

            _raised[s.Key] = (now, s.Tool);
            raised.Add(new AgentAttention(s.Key, s.Provider, s.Project, s.Tool, Phrase(s), s.Basis, s.Id, s.Pid, s.Origin, now));
        }

        foreach (var key in _raised.Keys.Where(k => sessions.All(s => s.Key != k || !s.IsWaiting)).ToList())
        {
            _raised.Remove(key);
        }
    }

    private static string Phrase(AgentSession s)
    {
        var who = s.Provider.DisplayName();
        return s.Tool is { } t && t.StartsWith("Question", StringComparison.Ordinal) ? who + " has a question for you"
            : s.Tool is "Plan" ? who + " needs your approval of its plan"
            : who + " needs your permission";
    }

    private void DropStale(List<AgentSession> sessions)
    {
        sessions.RemoveAll(s => s.State == AgentSessionState.Idle && _clock() - s.LastActivity > IdleKeep);
    }

    // ------------------------------------------------------------------------------------------ Claude Code

    private sealed record ClaudeRecord(int Pid, string Id, string Cwd, string Entrypoint, string? Name, string? Status, DateTime? StatusAt, DateTime? UpdatedAt, DateTime? Started, long? ProcStart, string Config);

    private IEnumerable<AgentSession> ScanClaude(DateTime now, List<AgentProcess> all, Dictionary<string, HookEvent> hooks)
    {
        var configs = new List<string>();
        if (_paths.ClaudeDir.Length > 0)
        {
            configs.Add(_paths.ClaudeDir);
        }

        var records = new List<ClaudeRecord>();
        foreach (var config in configs)
        {
            var folder = Path.Combine(config, "sessions");
            string[] files;
            try
            {
                files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.json") : [];
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (!int.TryParse(Path.GetFileNameWithoutExtension(file), out _) || ReadRecord(file, config) is not { } record)
                {
                    continue;
                }

                var alive = _paths.IsAlive is { } custom ? custom(record.Pid, record.ProcStart) : AgentProcesses.IsSameProcess(record.Pid, record.ProcStart);
                if (alive)
                {
                    records.Add(record);
                }
            }
        }

        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records.GroupBy(r => r.Id).Select(g => g.OrderByDescending(r => r.UpdatedAt ?? DateTime.MinValue).First()))
        {
            known.Add(record.Id);
            if (BuildClaude(record, now, all, hooks) is { } session)
            {
                yield return session;
            }
        }

        // Claude Code too old to keep records, or a config folder this app was not told of: a log written in the last moments is a session.
        foreach (var config in configs)
        {
            var projects = Path.Combine(config, "projects");
            if (!Directory.Exists(projects))
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateDirectories(projects).SelectMany(d => Directory.EnumerateFiles(d, "*.jsonl")).ToList();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                var id = Path.GetFileNameWithoutExtension(file);
                DateTime written;
                try
                {
                    written = File.GetLastWriteTimeUtc(file);
                }
                catch (IOException)
                {
                    continue;
                }

                if (known.Contains(id) || now - written > TimeSpan.FromSeconds(20))
                {
                    continue;
                }

                var tail = Tail(file, false);
                yield return new AgentSession
                {
                    Key = "claude:" + id, Provider = AgentProvider.Claude, Id = id, Project = tail.Project ?? ProjectOf(Path.GetFileName(Path.GetDirectoryName(file)) ?? ""),
                    Origin = "Claude Code", State = AgentSessionState.Working, Activity = Activity(tail, null), Since = tail.TurnStart ?? written, LastActivity = written,
                };
            }
        }
    }

    private static ClaudeRecord? ReadRecord(string file, string config)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length >= 1 << 16)
            {
                return null;
            }

            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            if (root.Prop("pid").Num() is not { } pid || root.Str("sessionId") is not { Length: > 0 } id)
            {
                return null;
            }

            if (root.Str("pidDomain") is { } domain && !domain.StartsWith("win32", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            long? procStart = long.TryParse(root.Str("procStart"), out var ps) ? ps : null;
            return new ClaudeRecord((int)Math.Min(pid, int.MaxValue), id, root.Str("cwd") ?? "", root.Str("entrypoint") ?? "", root.Str("name"), root.Str("status"),
                root.Prop("statusUpdatedAt").Seconds(), root.Prop("updatedAt").Seconds(), root.Prop("startedAt").Seconds(), procStart, config);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private AgentSession? BuildClaude(ClaudeRecord record, DateTime now, List<AgentProcess> all, Dictionary<string, HookEvent> hooks)
    {
        var projects = Path.Combine(record.Config, "projects");
        var file = TranscriptPath(projects, record.Cwd, record.Id);
        var tail = file is null ? new TailInfo() : Tail(file, false);
        DateTime written = DateTime.MinValue;
        try
        {
            if (file is not null)
            {
                written = File.GetLastWriteTimeUtc(file);
            }
        }
        catch (IOException)
        {
        }

        var agents = file is null ? [] : Subagents(Path.Combine(Path.GetDirectoryName(file)!, record.Id, "subagents"), now);
        var lastWrite = new[] { written, agents.Count > 0 ? agents.Max(a => a.Written) : DateTime.MinValue }.Max();
        var statusAt = record.StatusAt ?? record.UpdatedAt ?? record.Started ?? now;
        var last = lastWrite > statusAt ? lastWrite : statusAt;
        var status = (record.Status ?? "").ToLowerInvariant();
        var session = new AgentSession
        {
            Key = "claude:" + record.Id,
            Provider = AgentProvider.Claude,
            Id = record.Id,
            Project = ProjectOf(record.Cwd),
            Title = record.Name,
            Origin = record.Entrypoint switch { "claude-desktop" => "Claude app", "cli" => "Terminal", "claude-vscode" => "VS Code", "" => "Claude Code", var e => e },
            Pid = record.Pid,
            LastActivity = last,
            Since = tail.TurnStart ?? statusAt,
            Agents = agents.Count(a => !a.Ended),
        };

        // 1. A hook said so (opt-in, reliable) and nothing has been written since.
        if (hooks.TryGetValue(record.Id, out var hook) && hook.At > now.AddMinutes(-15) && written <= hook.At.AddSeconds(1.5) && status != "idle")
        {
            session.State = AgentSessionState.Waiting;
            session.Basis = WaitingBasis.Hook;
            session.Tool = hook.Tool ?? tail.Pending.Values.FirstOrDefault()?.Display;
            session.Since = hook.At;
            session.Activity = hook.Kind == HookKind.Input ? Loc.T("Waiting for your reply") : "Waiting for permission" + (session.Tool is { } t ? ": " + t : "");
            if (hook.Kind == HookKind.Input)
            {
                session.Tool = "Question";
            }

            return session;
        }

        // 2. Claude Code's own status says it waits.
        if (status.Contains("wait") || status.Contains("permission") || status.Contains("approval") || status.Contains("blocked") || status.Contains("input"))
        {
            session.State = AgentSessionState.Waiting;
            session.Basis = WaitingBasis.Status;
            session.Tool = tail.Pending.Values.FirstOrDefault()?.Display;
            session.Since = statusAt;
            session.Activity = "Waiting for you" + (session.Tool is { } t ? ": " + t : "");
            return session;
        }

        var quiet = now - lastWrite;
        // 3. A tool call without a result, nothing running for it and nothing written: probably a permission prompt.
        var pending = tail.Pending.Values.OrderBy(p => p.At).FirstOrDefault();
        var waitingAgent = agents.Where(a => !a.Ended && a.Pending is not null && now - a.Written >= PendingAfter).OrderBy(a => a.Written).FirstOrDefault();
        var busyAgents = agents.Any(a => !a.Ended && (a.Pending is null || now - a.Written < PendingAfter));
        if (status != "idle" && (pending is not null || waitingAgent is not null))
        {
            var candidate = waitingAgent?.Pending ?? (busyAgents ? null : pending);
            var since = waitingAgent?.Pending?.At ?? pending?.At ?? now;
            var limit = candidate is { Slow: true } ? SlowPendingAfter : PendingAfter;
            if (candidate is not null && !candidate.IsAgentCall && quiet >= limit && now - candidate.At >= limit && !ToolRunning(record.Pid, all, candidate))
            {
                session.State = AgentSessionState.Waiting;
                session.Basis = WaitingBasis.Heuristic;
                session.Tool = candidate.Display;
                session.Since = since;
                session.Activity = "Waiting for you (probably a permission prompt): " + candidate.Display;
                return session;
            }
        }

        // Working or idle.
        var working = status == "busy" || (status.Length == 0 && !tail.Ended && quiet < TimeSpan.FromSeconds(90)) || (status == "idle" && quiet < TimeSpan.FromSeconds(2.5) && !tail.Ended);
        if (working)
        {
            session.State = AgentSessionState.Working;
            session.Activity = Activity(tail, agents);
            if (tail.TurnStart is null)
            {
                session.Since = statusAt;
            }
        }
        else
        {
            session.State = AgentSessionState.Idle;
            session.Activity = Loc.T("Idle");
            session.Since = last;
        }

        return session;
    }

    /// <summary>A tool that began after the call was logged and still runs shows as a child of the agent's process.</summary>
    private static bool ToolRunning(int pid, List<AgentProcess> all, PendingTool tool)
    {
        if (tool.InProcess)
        {
            return false;
        }

        return AgentProcesses.ChildrenStartedSince(all, pid, tool.At).Any(p => !p.Name.Equals("conhost.exe", StringComparison.OrdinalIgnoreCase));
    }

    private static string Activity(TailInfo tail, List<SubagentInfo>? agents)
    {
        if (agents is { Count: > 0 } && agents.Where(a => !a.Ended).OrderByDescending(a => a.Written).FirstOrDefault() is { } agent)
        {
            return "Agent: " + (agent.Pending is { } p ? p.Activity : agent.Kind);
        }

        if (tail.Pending.Values.OrderByDescending(p => p.At).FirstOrDefault() is { } pending)
        {
            return pending.Activity;
        }

        return tail.LastKind switch
        {
            "assistant-text" => "Replying",
            "assistant-thinking" => "Thinking",
            "tool-result" => "Thinking",
            "user" => "Thinking",
            _ => Loc.T("Working"),
        };
    }

    private static string? TranscriptPath(string projects, string cwd, string id)
    {
        try
        {
            if (!Directory.Exists(projects))
            {
                return null;
            }

            if (cwd.Length > 0)
            {
                var mangled = new string(cwd.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray());
                var direct = Path.Combine(projects, mangled, id + ".jsonl");
                if (File.Exists(direct))
                {
                    return direct;
                }
            }

            foreach (var dir in Directory.EnumerateDirectories(projects))
            {
                var candidate = Path.Combine(dir, id + ".jsonl");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        return null;
    }

    private static string ProjectOf(string cwdOrFolder)
    {
        var name = AgentLogParser.ProjectName(cwdOrFolder);
        return name.Length > 0 ? name : cwdOrFolder;
    }

    private sealed record SubagentInfo(string Path, DateTime Written, bool Ended, PendingTool? Pending, string Kind);

    /// <summary>Subagent logs written in the last moments: these are the "+N agents" of a session.</summary>
    private List<SubagentInfo> Subagents(string folder, DateTime now)
    {
        var result = new List<SubagentInfo>();
        try
        {
            if (!Directory.Exists(folder))
            {
                return result;
            }

            foreach (var file in Directory.EnumerateFiles(folder, "agent-*.jsonl", SearchOption.AllDirectories))
            {
                var written = File.GetLastWriteTimeUtc(file);
                if (now - written > TimeSpan.FromSeconds(90))
                {
                    continue;
                }

                var tail = Tail(file, true);
                var kind = "working";
                result.Add(new SubagentInfo(file, written, tail.Ended && now - written > TimeSpan.FromSeconds(3), tail.Pending.Values.OrderBy(p => p.At).FirstOrDefault(), kind));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        return result;
    }

    // ------------------------------------------------------------------------------------------ transcripts

    internal sealed record PendingTool(string Id, string Name, string Display, string Activity, DateTime At, bool Slow, bool InProcess, bool IsAgentCall);

    internal sealed class TailInfo
    {
        public Dictionary<string, PendingTool> Pending { get; } = new(StringComparer.Ordinal);

        public string LastKind { get; set; } = "";

        public DateTime? TurnStart { get; set; }

        public DateTime? LastTime { get; set; }

        /// <summary>The last assistant message ended its turn and nothing came after it.</summary>
        public bool Ended { get; set; }

        public string? Project { get; set; }
    }

    /// <summary>Analyses the end of a transcript, once per change of the file.</summary>
    private TailInfo Tail(string path, bool sidechain)
    {
        try
        {
            var info = new FileInfo(path);
            var key = (info.Length, info.LastWriteTimeUtc);
            if (_tailKeys.TryGetValue(path, out var seen) && seen == key && _tails.TryGetValue(path, out var cached))
            {
                return cached;
            }

            var tail = AnalyzeTail(path, sidechain);
            _tails[path] = tail;
            _tailKeys[path] = key;
            return tail;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new TailInfo();
        }
    }

    internal static TailInfo AnalyzeTail(string path, bool sidechain)
    {
        var info = new TailInfo();
        var size = 262_144;
        byte[] buffer;
        long start;
        List<(int Offset, int Length)> lines;
        while (true)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            start = Math.Max(0, stream.Length - size);
            stream.Seek(start, SeekOrigin.Begin);
            buffer = new byte[stream.Length - start];
            var read = 0;
            while (read < buffer.Length)
            {
                var n = stream.Read(buffer, read, buffer.Length - read);
                if (n <= 0)
                {
                    break;
                }

                read += n;
            }

            lines = SplitLines(buffer, read, start > 0);
            // One enormous tool result can fill the window; look further back, up to 16 MB, before giving up.
            if (lines.Count > 0 || start == 0 || size >= 16 << 20)
            {
                break;
            }

            size *= 4;
        }

        var span = buffer.AsSpan();
        foreach (var (offset, length) in lines)
        {
            var line = span.Slice(offset, length);
            var isAssistant = Contains(line, "\"type\":\"assistant\""u8);
            var isUser = !isAssistant && Contains(line, "\"type\":\"user\""u8);
            if (!isAssistant && !isUser)
            {
                continue;
            }

            if (length > 1 << 20)
            {
                // A huge line is a tool result: only its tool id matters.
                if (isUser && FindToolUseId(line) is { } big)
                {
                    info.Pending.Remove(big);
                    info.LastKind = "tool-result";
                    info.Ended = false;
                }

                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(buffer.AsMemory(offset, length));
                var root = doc.RootElement;
                if (!sidechain && root.Prop("isSidechain").Bool() == true)
                {
                    continue;
                }

                var time = root.Prop("timestamp").Timestamp();
                if (time is not null)
                {
                    info.LastTime = time;
                }

                if (root.Str("cwd") is { Length: > 0 } cwd)
                {
                    info.Project = ProjectOf(cwd);
                }

                var content = root.Prop("message").Prop("content");
                if (isAssistant)
                {
                    ReadAssistant(root, content, time, info);
                }
                else
                {
                    ReadUser(root, content, time, info);
                }
            }
            catch (JsonException)
            {
            }
        }

        return info;
    }

    private static void ReadAssistant(JsonElement root, JsonElement? content, DateTime? time, TailInfo info)
    {
        var stop = root.Prop("message").Str("stop_reason");
        var blocks = content is { ValueKind: JsonValueKind.Array } ? content.Value.EnumerateArray().ToList() : [];
        foreach (var block in blocks)
        {
            switch (block.Str("type"))
            {
                case "tool_use":
                {
                    var name = block.Str("name") ?? "tool";
                    if (block.Str("id") is { Length: > 0 } id)
                    {
                        info.Pending[id] = Describe(id, name, block.Prop("input"), time ?? DateTime.UtcNow);
                    }

                    info.LastKind = "assistant-tool";
                    break;
                }

                case "thinking":
                    info.LastKind = "assistant-thinking";
                    break;
                case "text":
                    info.LastKind = "assistant-text";
                    break;
            }
        }

        info.Ended = stop is "end_turn" or "stop_sequence" or "max_tokens" or "refusal";
        if (info.Ended)
        {
            info.Pending.Clear();
        }
    }

    private static void ReadUser(JsonElement root, JsonElement? content, DateTime? time, TailInfo info)
    {
        if (root.Prop("isMeta").Bool() == true)
        {
            return;
        }

        if (content is { ValueKind: JsonValueKind.Array } blocks)
        {
            var results = 0;
            foreach (var block in blocks.EnumerateArray())
            {
                if (block.Str("type") == "tool_result")
                {
                    results++;
                    if (block.Str("tool_use_id") is { } id)
                    {
                        info.Pending.Remove(id);
                    }
                }
            }

            if (results > 0)
            {
                info.LastKind = "tool-result";
                info.Ended = false;
                return;
            }
        }

        // A prompt of the person's opens a turn.
        info.LastKind = "user";
        info.Ended = false;
        info.TurnStart = time ?? info.TurnStart;
        info.Pending.Clear();
        var text = content is { ValueKind: JsonValueKind.String } ? content.Value.GetString() : null;
        if (text is not null && (text.StartsWith("[Request interrupted", StringComparison.Ordinal) || text.StartsWith("<local-command", StringComparison.Ordinal)))
        {
            info.Ended = true;
        }
    }

    private static PendingTool Describe(string id, string name, JsonElement? input, DateTime at)
    {
        string? target = null;
        if (input is { ValueKind: JsonValueKind.Object } i)
        {
            var path = i.Str("file_path") ?? i.Str("notebook_path") ?? i.Str("path");
            target = path is { Length: > 0 } ? Path.GetFileName(path.TrimEnd('\\', '/')) : null;
        }

        var isAgent = name is "Task" or "Agent";
        var slow = name.StartsWith("mcp__", StringComparison.Ordinal) || name is "WebFetch" or "WebSearch";
        var inProcess = name is "Read" or "Edit" or "Write" or "MultiEdit" or "NotebookEdit" or "Glob" or "Grep" or "LS" or "TodoWrite" or "ExitPlanMode" or "AskUserQuestion";
        string display = name switch
        {
            "AskUserQuestion" => "Question",
            "ExitPlanMode" => "Plan",
            _ when name.StartsWith("mcp__", StringComparison.Ordinal) => name.Split("__") is { Length: >= 3 } p ? p[1] + ": " + p[2] : name,
            _ => name + (target is not null ? " " + target : ""),
        };
        var activity = name switch
        {
            "Bash" or "PowerShell" => "Running a command",
            "Read" => "Reading " + (target ?? "a file"),
            "Edit" or "MultiEdit" or "Write" or "NotebookEdit" => "Editing " + (target ?? "a file"),
            "Grep" or "Glob" or "LS" => "Searching the project",
            "WebFetch" or "WebSearch" => "Browsing the web",
            "Task" or "Agent" => "Running an agent",
            "TodoWrite" => "Planning",
            "AskUserQuestion" => "Asking a question",
            "ExitPlanMode" => "Presenting a plan",
            _ when name.StartsWith("mcp__", StringComparison.Ordinal) => "Using " + display,
            _ => "Using " + name,
        };
        return new PendingTool(id, name, display, activity, at, slow, inProcess, isAgent);
    }

    private static List<(int, int)> SplitLines(byte[] buffer, int length, bool dropFirst)
    {
        var lines = new List<(int, int)>();
        var start = 0;
        var first = true;
        for (var i = 0; i < length; i++)
        {
            if (buffer[i] != (byte)'\n')
            {
                continue;
            }

            if (!(first && dropFirst) && i > start)
            {
                lines.Add((start, i - start));
            }

            first = false;
            start = i + 1;
        }

        // The last line counts only when it is whole: JSON that stops short fails to parse and is skipped by the caller.
        if (start < length && !(first && dropFirst))
        {
            lines.Add((start, length - start));
        }

        return lines;
    }

    internal static bool Contains(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle) => haystack.IndexOf(needle) >= 0;

    private static string? FindToolUseId(ReadOnlySpan<byte> line)
    {
        var marker = "\"tool_use_id\":\""u8;
        var at = line.IndexOf(marker);
        if (at < 0)
        {
            return null;
        }

        var rest = line[(at + marker.Length)..];
        var end = rest.IndexOf((byte)'"');
        return end > 0 ? Encoding.UTF8.GetString(rest[..end]) : null;
    }

    // ------------------------------------------------------------------------------------------ Codex

    private IEnumerable<AgentSession> ScanCodex(DateTime now, List<AgentProcess> all)
    {
        var root = Path.Combine(_paths.CodexDir, "sessions");
        if (!Directory.Exists(root))
        {
            yield break;
        }

        var running = _paths.CodexRunning is { } custom ? custom() : all.Any(p => p.Name.Equals("codex.exe", StringComparison.OrdinalIgnoreCase));
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var day in new[] { now, now.AddDays(-1), now.ToLocalTime(), now.ToLocalTime().AddDays(-1) })
        {
            dirs.Add(Path.Combine(root, day.Year.ToString("0000"), day.Month.ToString("00"), day.Day.ToString("00")));
        }

        foreach (var dir in dirs.Where(Directory.Exists))
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(dir, "rollout-*.jsonl");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                DateTime written;
                try
                {
                    written = File.GetLastWriteTimeUtc(file);
                }
                catch (IOException)
                {
                    continue;
                }

                var age = now - written;
                if (age > TimeSpan.FromMinutes(30) || (age > TimeSpan.FromSeconds(120) && !running))
                {
                    continue;
                }

                if (BuildCodex(file, written, now, running) is { } session)
                {
                    yield return session;
                }
            }
        }
    }

    private AgentSession? BuildCodex(string file, DateTime written, DateTime now, bool running)
    {
        var state = CodexTail(file);
        var id = Path.GetFileNameWithoutExtension(file);
        if (!_codexProjects.TryGetValue(file, out var project))
        {
            project = CodexProject(file) ?? "";
            _codexProjects[file] = project;
        }

        var quiet = now - written;
        var session = new AgentSession
        {
            Key = "codex:" + id, Provider = AgentProvider.Codex, Id = id, Project = project, Origin = "Codex", LastActivity = written,
            Since = state.TurnStart ?? written,
        };
        if (state.Approval is { } approval)
        {
            session.State = AgentSessionState.Waiting;
            session.Basis = WaitingBasis.Approval;
            session.Tool = approval;
            session.Since = state.ApprovalAt ?? written;
            session.Activity = "Waiting for approval: " + approval;
            return session;
        }

        if (state.PendingCall is { } call && quiet >= TimeSpan.FromSeconds(12) && state.PendingSince is { } since && now - since >= TimeSpan.FromSeconds(12) && !state.Running)
        {
            session.State = AgentSessionState.Waiting;
            session.Basis = WaitingBasis.Heuristic;
            session.Tool = call;
            session.Since = since;
            session.Activity = "Waiting for you (probably an approval): " + call;
            return session;
        }

        if (state.Open && quiet < TimeSpan.FromMinutes(3) || quiet < TimeSpan.FromSeconds(20))
        {
            session.State = AgentSessionState.Working;
            session.Activity = state.PendingCall is { } running2 ? "Running " + running2 : state.LastKind switch { "message" => "Replying", "reasoning" => "Thinking", _ => Loc.T("Working") };
            return session;
        }

        session.State = AgentSessionState.Idle;
        session.Activity = Loc.T("Idle");
        session.Since = written;
        return session;
    }

    private sealed class CodexState
    {
        public bool Open { get; set; }

        public DateTime? TurnStart { get; set; }

        public string LastKind { get; set; } = "";

        public string? Approval { get; set; }

        public DateTime? ApprovalAt { get; set; }

        public string? PendingCall { get; set; }

        public DateTime? PendingSince { get; set; }

        public bool Running { get; set; }
    }

    private static readonly HashSet<string> CodexApprovals = ["exec_approval_request", "apply_patch_approval_request", "request_permissions", "request_user_input", "elicitation_request"];

    private static readonly HashSet<string> CodexProgress = ["exec_command_begin", "exec_command_end", "patch_apply_begin", "patch_apply_end", "task_complete", "turn_aborted", "task_started", "mcp_tool_call_begin", "mcp_tool_call_end"];

    private static CodexState CodexTail(string path)
    {
        var state = new CodexState();
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var start = Math.Max(0, stream.Length - 262_144);
            stream.Seek(start, SeekOrigin.Begin);
            var buffer = new byte[stream.Length - start];
            var read = stream.Read(buffer, 0, buffer.Length);
            var calls = new Dictionary<string, (string Name, DateTime At)>();
            foreach (var (offset, length) in SplitLines(buffer, read, start > 0))
            {
                if (length > 1 << 20)
                {
                    continue;
                }

                try
                {
                    using var doc = JsonDocument.Parse(buffer.AsMemory(offset, length));
                    var root = doc.RootElement;
                    var type = root.Str("type");
                    var payload = root.Prop("payload");
                    var kind = payload.Str("type") ?? "";
                    var time = root.Prop("timestamp").Timestamp() ?? DateTime.UtcNow;
                    if (type == "event_msg")
                    {
                        if (kind == "task_started")
                        {
                            state.Open = true;
                            state.TurnStart = time;
                        }
                        else if (kind is "task_complete" or "turn_aborted")
                        {
                            state.Open = false;
                            state.Approval = null;
                            calls.Clear();
                        }

                        if (CodexApprovals.Contains(kind))
                        {
                            state.Approval = kind.StartsWith("apply_patch", StringComparison.Ordinal) ? "Patch" : kind == "request_user_input" ? "Question" : "Command";
                            state.ApprovalAt = time;
                        }
                        else if (CodexProgress.Contains(kind) && kind != "task_started")
                        {
                            state.Approval = null;
                        }

                        if (kind is "exec_command_begin" or "patch_apply_begin" or "mcp_tool_call_begin")
                        {
                            state.Running = true;
                        }
                        else if (kind is "exec_command_end" or "patch_apply_end" or "mcp_tool_call_end")
                        {
                            state.Running = false;
                        }
                    }
                    else if (type == "response_item")
                    {
                        state.LastKind = kind;
                        if (kind is "function_call" or "custom_tool_call" or "local_shell_call")
                        {
                            var id = payload.Str("call_id") ?? payload.Str("id") ?? Guid.NewGuid().ToString();
                            calls[id] = (payload.Str("name") is { Length: > 0 } n ? n : "command", time);
                            state.Approval = null;
                        }
                        else if (kind is "function_call_output" or "custom_tool_call_output")
                        {
                            if (payload.Str("call_id") is { } done)
                            {
                                calls.Remove(done);
                            }

                            state.Running = false;
                        }
                    }
                }
                catch (JsonException)
                {
                }
            }

            if (calls.Count > 0)
            {
                var oldest = calls.Values.OrderBy(c => c.At).First();
                state.PendingCall = oldest.Name;
                state.PendingSince = oldest.At;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        return state;
    }

    private static string? CodexProject(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[Math.Min(stream.Length, 1 << 20)];
            var read = stream.Read(buffer, 0, buffer.Length);
            var end = Array.IndexOf(buffer, (byte)'\n', 0, read);
            if (end <= 0)
            {
                return null;
            }

            using var doc = JsonDocument.Parse(buffer.AsMemory(0, end));
            return doc.RootElement.Prop("payload").Str("cwd") is { Length: > 0 } cwd ? ProjectOf(cwd) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
