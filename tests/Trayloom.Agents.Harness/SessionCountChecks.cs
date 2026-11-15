// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using Trayloom.Features.Agents;

namespace Trayloom.Agents.Harness;

/// <summary>
/// How many agent sessions the monitor counts and what the closed strip says about them: transcript files that merely got a metadata line
/// are not sessions, dead and duplicate records collapse, subagents belong to their parent, and the summary rules are pure functions. All
/// data is synthetic and lives in temp folders; nothing real is read.
/// </summary>
internal static partial class Program
{
    private static void SessionCounts()
    {
        NoSessionsFolder();
        WithSessionsFolder();
        Subagents();
        CodexSubagents();
        SummaryRules();
        IdleHookAndCache();
    }

    /// <summary>Fixture folders for one case, removed afterwards.</summary>
    private sealed class ScFixture : IDisposable
    {
        public ScFixture(bool sessionsFolder)
        {
            Root = Path.Combine(Path.GetTempPath(), "tl-sessioncounts-" + Guid.NewGuid().ToString("N"));
            ClaudeDir = Path.Combine(Root, "claude");
            CodexDir = Path.Combine(Root, "codex");
            EventsDir = Path.Combine(Root, "events");
            Directory.CreateDirectory(ProjectDir);
            Directory.CreateDirectory(CodexDir);
            Directory.CreateDirectory(EventsDir);
            if (sessionsFolder)
            {
                Directory.CreateDirectory(Path.Combine(ClaudeDir, "sessions"));
            }
        }

        public string Root { get; }

        public string ClaudeDir { get; }

        public string CodexDir { get; }

        public string EventsDir { get; }

        public string ProjectDir => Path.Combine(ClaudeDir, "projects", "C--work-demo");

        public HashSet<int> Alive { get; } = [];

        public string Transcript(string id) => Path.Combine(ProjectDir, id + ".jsonl");

        /// <summary>Writes the lines, then sets the file's time.</summary>
        public static void Write(string path, double fileAgeSeconds, params string[] lines)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, string.Join("\n", lines) + "\n");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(-fileAgeSeconds));
        }

        /// <summary>Appends lines, then sets the file's time again.</summary>
        public static void Append(string path, double fileAgeSeconds, params string[] lines)
        {
            File.AppendAllText(path, string.Join("\n", lines) + "\n");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(-fileAgeSeconds));
        }

        public void Record(int pid, string id, string status, long updatedAt)
        {
            File.WriteAllText(Path.Combine(ClaudeDir, "sessions", pid + ".json"),
                "{\"pid\":" + pid + ",\"sessionId\":\"" + id + "\",\"cwd\":\"C:\\\\work\\\\demo\",\"entrypoint\":\"cli\",\"status\":\"" + status +
                "\",\"updatedAt\":" + updatedAt + ",\"statusUpdatedAt\":" + updatedAt + "}");
        }

        public AgentSessionMonitor Monitor() => new(new AgentSessionPaths
        {
            ClaudeDir = ClaudeDir,
            CodexDir = CodexDir,
            EventsDir = EventsDir,
            IsAlive = (pid, _) => Alive.Contains(pid),
            CodexRunning = () => true,
        });

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (Exception)
            {
                // temp folder: leave it
            }
        }
    }

    private static string ScStamp(double secondsAgo) => DateTime.UtcNow.AddSeconds(-secondsAgo).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static long ScUnixNow() => new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds();

    private static string ScUser(double ago, string content = "\"do it\"", bool sidechain = false) =>
        "{\"type\":\"user\"," + (sidechain ? "\"isSidechain\":true," : "") + "\"cwd\":\"C:\\\\work\\\\demo\",\"timestamp\":\"" + ScStamp(ago) + "\",\"message\":{\"role\":\"user\",\"content\":" + content + "}}";

    private static string ScAssistant(double ago, string? stop, string blocks, bool sidechain = false) =>
        "{\"type\":\"assistant\"," + (sidechain ? "\"isSidechain\":true," : "") + "\"cwd\":\"C:\\\\work\\\\demo\",\"timestamp\":\"" + ScStamp(ago) + "\",\"message\":{\"role\":\"assistant\",\"stop_reason\":" +
        (stop is null ? "null" : "\"" + stop + "\"") + ",\"content\":" + blocks + "}}";

    private const string ScText = "[{\"type\":\"text\",\"text\":\"x\"}]";

    private static string ScTasks(params string[] ids) => "[" + string.Join(",", ids.Select(i => "{\"type\":\"tool_use\",\"id\":\"" + i + "\",\"name\":\"Task\",\"input\":{}}")) + "]";

    private static string ScResult(double ago, string id) => ScUser(ago, "[{\"type\":\"tool_result\",\"tool_use_id\":\"" + id + "\",\"content\":\"ok\"}]");

    private const string ScCostState = "{\"type\":\"cost-state\",\"costUSD\":1.5}";

    private static string ScId() => Guid.NewGuid().ToString();

    private static void ScMeta(string subagentFile, string type, string description, string toolUseId) =>
        File.WriteAllText(Path.ChangeExtension(subagentFile, ".meta.json"),
            "{\"agentType\":\"" + type + "\",\"description\":\"" + description + "\",\"toolUseId\":\"" + toolUseId + "\",\"spawnDepth\":1,\"model\":\"m\"}");

    private static AgentSession ScSession(AgentSessionState state, double idleMinutes = 0) =>
        new() { State = state, LastActivity = DateTime.UtcNow.AddMinutes(-idleMinutes), Project = "p", Activity = "a" };

    // ------------------------------------------------------------------------------------------ transcript fallback

    private static void NoSessionsFolder()
    {
        // 1. Four old transcripts that only got a metadata line (no timestamp) and a fresh file time: not sessions.
        using (var fx = new ScFixture(false))
        {
            for (var i = 0; i < 4; i++)
            {
                ScFixture.Write(fx.Transcript(ScId()), 0, ScUser(172_900), ScAssistant(172_800, "end_turn", ScText), ScCostState);
            }

            Check(fx.Monitor().Refresh().Count == 0, "sessions 1: four old transcripts with a fresh file time and a metadata line are no sessions");
        }

        // 2. A prompt from five seconds ago is a session; an agent-* file with the same lines is not.
        using (var fx = new ScFixture(false))
        {
            for (var i = 0; i < 3; i++)
            {
                ScFixture.Write(fx.Transcript(ScId()), 0, ScUser(172_900), ScAssistant(172_800, "end_turn", ScText), ScCostState);
            }

            var live = new[] { ScUser(60), ScAssistant(40, "end_turn", ScText), ScCostState, ScUser(5) };
            ScFixture.Write(fx.Transcript(ScId()), 0, live);
            ScFixture.Write(Path.Combine(fx.ProjectDir, "agent-abc.jsonl"), 0, live);
            var sessions = fx.Monitor().Refresh();
            Check(sessions.Count == 1 && sessions[0].State == AgentSessionState.Working, "sessions 2: a prompt from 5 s ago is one Working session; an agent-*.jsonl file is none (" + sessions.Count + ")");
        }
    }

    // ------------------------------------------------------------------------------------------ records

    private static void WithSessionsFolder()
    {
        // 3. The sessions folder exists: only records create sessions, a loose fresh transcript does not.
        using (var fx = new ScFixture(true))
        {
            var a = ScId();
            var b = ScId();
            fx.Alive.Add(100);
            fx.Record(100, a, "busy", ScUnixNow());
            ScFixture.Write(fx.Transcript(a), 0, ScUser(3));
            ScFixture.Write(fx.Transcript(b), 2, ScUser(2));
            var sessions = fx.Monitor().Refresh();
            Check(sessions.Count == 1 && sessions[0].Id == a, "sessions 3: with a sessions folder, a fresh transcript without a record is not a session");
        }

        // 4. A dead process's record never counts, and its transcript does not come back through the fallback.
        using (var fx = new ScFixture(true))
        {
            var id = ScId();
            fx.Record(200, id, "busy", ScUnixNow());
            ScFixture.Write(fx.Transcript(id), 0, ScUser(1));
            Check(fx.Monitor().Refresh().Count == 0, "sessions 4: a dead process's record with a just-written transcript is no session");
        }

        // 5. Two records of one session collapse to one.
        using (var fx = new ScFixture(true))
        {
            var id = ScId();
            fx.Alive.Add(301);
            fx.Alive.Add(302);
            fx.Record(301, id, "busy", ScUnixNow() - 60);
            fx.Record(302, id, "busy", ScUnixNow());
            ScFixture.Write(fx.Transcript(id), 0, ScUser(3));
            Check(fx.Monitor().Refresh().Count == 1, "sessions 5: two alive records with one sessionId are one session");
        }
    }

    // ------------------------------------------------------------------------------------------ subagents

    private static void Subagents()
    {
        using (var fx = new ScFixture(true))
        {
            var id = ScId();
            fx.Alive.Add(400);
            fx.Record(400, id, "busy", ScUnixNow());
            var parent = fx.Transcript(id);
            ScFixture.Write(parent, 1, ScUser(120), ScAssistant(100, "tool_use", ScTasks("tu1", "tu2", "tu3")));
            var folder = Path.Combine(fx.ProjectDir, id, "subagents");
            var s1 = Path.Combine(folder, "agent-s1.jsonl");
            var s2 = Path.Combine(folder, "agent-s2.jsonl");
            var s3 = Path.Combine(folder, "agent-s3.jsonl");
            ScFixture.Write(s1, 5, ScUser(20, sidechain: true), ScAssistant(5, null, ScText, true));
            ScFixture.Write(s2, 5, ScUser(30, sidechain: true), ScAssistant(5, "end_turn", ScText, true));
            ScFixture.Write(s3, 300, ScUser(360, sidechain: true), ScAssistant(300, null, ScText, true));
            ScMeta(s1, "Explore", "Explore the parser", "tu1");
            ScMeta(s2, "Plan", "Plan it", "tu2");
            ScMeta(s3, "Review", "Review it", "tu3");
            var monitor = fx.Monitor();
            var sessions = monitor.Refresh();
            var session = sessions.FirstOrDefault();
            Check(sessions.Count == 1 && session is { Agents: 2 } && session.Subagents[0].Description == "Explore the parser" && session.Subagents[0].Type == "Explore",
                "sessions 6: a running, a quiet-but-awaited and an ended subagent: Count 1, Agents 2, newest first with its meta description");
            Check(session is not null && session.Activity == "Running 3 agents", "sessions 6: the session's own activity is not replaced by its agents (" + session?.Activity + ")");

            // 7. The parent got the result for s3 and s3 has been quiet for two minutes: it no longer runs.
            ScFixture.Append(parent, 1, ScResult(1, "tu3"));
            File.SetLastWriteTimeUtc(s3, DateTime.UtcNow.AddSeconds(-120));
            sessions = monitor.Refresh();
            Check(sessions.Count == 1 && sessions[0].Agents == 1, "sessions 7: after its tool result and 2 quiet minutes a subagent is gone (" + sessions.FirstOrDefault()?.Agents + ")");
        }

        // 8. An idle session has no subagents, whatever the files say.
        using (var fx = new ScFixture(true))
        {
            var id = ScId();
            fx.Alive.Add(500);
            fx.Record(500, id, "idle", ScUnixNow() - 600);
            ScFixture.Write(fx.Transcript(id), 600, ScUser(700), ScAssistant(600, "end_turn", ScText));
            var s1 = Path.Combine(fx.ProjectDir, id, "subagents", "agent-s1.jsonl");
            ScFixture.Write(s1, 5, ScUser(20, sidechain: true), ScAssistant(5, null, ScText, true));
            ScMeta(s1, "Explore", "Explore", "none");
            var sessions = fx.Monitor().Refresh();
            Check(sessions.Count == 1 && sessions[0].State == AgentSessionState.Idle && sessions[0].Agents == 0, "sessions 8: an idle session has no subagents");
        }
    }

    private static void CodexSubagents()
    {
        using var fx = new ScFixture(true);
        var day = Path.Combine(fx.CodexDir, "sessions", DateTime.UtcNow.ToString("yyyy", CultureInfo.InvariantCulture), DateTime.UtcNow.ToString("MM", CultureInfo.InvariantCulture), DateTime.UtcNow.ToString("dd", CultureInfo.InvariantCulture));
        string Meta(string id, string source) =>
            "{\"timestamp\":\"" + ScStamp(30) + "\",\"type\":\"session_meta\",\"payload\":{\"id\":\"" + id + "\",\"cwd\":\"C:\\\\work\\\\demo\"," + source + "}}";
        string Started() => "{\"timestamp\":\"" + ScStamp(5) + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}";
        ScFixture.Write(Path.Combine(day, "rollout-2026-11-15T10-00-00-root.jsonl"), 1, Meta("root-id", "\"source\":\"cli\""), Started());
        ScFixture.Write(Path.Combine(day, "rollout-2026-11-15T10-00-05-child.jsonl"), 1,
            Meta("child-id", "\"session_id\":\"root-id\",\"source\":{\"subagent\":{\"thread_spawn\":{\"parent_thread_id\":\"root-id\",\"agent_role\":\"explorer\"}}}"), Started());
        var sessions = fx.Monitor().Refresh();
        Check(sessions.Count(s => s.Provider == AgentProvider.Codex) == 1 && sessions.FirstOrDefault(s => s.Provider == AgentProvider.Codex)?.Agents == 1,
            "sessions 9: a Codex subagent rollout is not a session; it is the root's one agent (" + sessions.Count + " sessions)");
    }

    // ------------------------------------------------------------------------------------------ pure rules

    private static void SummaryRules()
    {
        var w = AgentSessionState.Working;
        var wait = AgentSessionState.Waiting;
        var idle = AgentSessionState.Idle;
        string? Lead(params AgentSessionState[] states) => AgentSessionSummary.StripLead(states.Select(s => ScSession(s)).ToList());

        Check(Lead(w) is null && Lead(w, idle, idle) is null, "sessions 10: one working session has no lead (its clock is shown)");
        Check(Lead(w, w, w) == "3 working", "sessions 10: [W,W,W] => \"3 working\" (" + Lead(w, w, w) + ")");
        Check(Lead(wait) == "Needs you", "sessions 10: [Wait] => \"Needs you\"");
        Check(Lead(wait, w, w) == "Needs you · 2 working", "sessions 10: [Wait,W,W] => \"Needs you · 2 working\" (" + Lead(wait, w, w) + ")");
        Check(Lead(wait, wait, w) == "2 need you · 1 working", "sessions 10: [Wait,Wait,W] => \"2 need you · 1 working\" (" + Lead(wait, wait, w) + ")");
        Check(Lead(wait, wait, idle, idle) == "2 need you", "sessions 10: idle sessions are ignored (" + Lead(wait, wait, idle, idle) + ")");

        Check(AgentSessionSummary.OutranksMusic(new[] { w, w, wait, w, idle }.Select(s => ScSession(s)).ToList()), "sessions 11: a waiting session outranks the music");
        Check(!AgentSessionSummary.OutranksMusic(new[] { w, w, w, idle, idle }.Select(s => ScSession(s)).ToList()) && !AgentSessionSummary.OutranksMusic([]), "sessions 11: working and idle sessions never do");

        var all = new List<AgentSession> { ScSession(w), ScSession(idle, 10), ScSession(idle, 120) };
        var visible = AgentSessionSummary.Visible(all, DateTime.UtcNow);
        Check(visible.Count == 2 && visible.Contains(all[1]) && !visible.Contains(all[2]) && all.Count - visible.Count == 1, "sessions 12: an idle session of 10 minutes is listed, one of 2 hours is hidden and counted as idle");

        var composed = AgentSessionSummary.Compose("3 working", new StripText("", "42%", null, AgentProvider.Claude));
        Check(composed.Text == "3 working · 42%" && composed.Limit == "42%", "sessions 10: with usage readings the count still leads, joined with the limit (" + composed.Text + ")");
        Check(AgentSessionSummary.Compose("3 working", new StripText("01:05", null, null, null)).Text == "3 working · 01:05" && AgentSessionSummary.Compose("3 working", new StripText("", null, null, null)).Text == "3 working",
            "sessions 10: a plain reading follows the count, no reading leaves the count alone");

        var withAgents = ScSession(w);
        withAgents.Subagents = [new AgentSubagent("a", "Explore", "Find it", "Working", DateTime.UtcNow), new AgentSubagent("b", "Plan", "", "Working", DateTime.UtcNow)];
        Check(AgentSessionSummary.SubagentLine(withAgents) == "2 agents · Explore: Find it", "sessions 12: the agents line names the newest agent's kind and task (" + AgentSessionSummary.SubagentLine(withAgents) + ")");
    }

    // ------------------------------------------------------------------------------------------ hooks and caches

    private static void IdleHookAndCache()
    {
        string Event(string id, string type, string message) =>
            "{\"receivedAt\":\"" + ScStamp(0) + "\",\"session_id\":\"" + id + "\",\"event\":\"Notification\",\"notification_type\":\"" + type + "\",\"message\":\"" + message + "\"}";

        // 13. idle_prompt says a finished turn is waiting for the next prompt: not a session that needs the person.
        foreach (var (type, expectWaiting) in new[] { ("idle_prompt", false), ("elicitation_dialog", true) })
        {
            using var fx = new ScFixture(true);
            var id = ScId();
            fx.Alive.Add(600);
            fx.Record(600, id, "busy", ScUnixNow());
            ScFixture.Write(fx.Transcript(id), 20, ScUser(30));
            File.WriteAllText(Path.Combine(fx.EventsDir, "e.json"), Event(id, type, "Claude is waiting for your input"));
            var sessions = fx.Monitor().Refresh();
            Check(sessions.Count == 1 && sessions[0].IsWaiting == expectWaiting, "sessions 13: hook " + type + " => waiting " + expectWaiting);
        }

        // 14. A subagent file that vanished leaves no cached tail behind.
        using (var fx = new ScFixture(true))
        {
            var id = ScId();
            fx.Alive.Add(700);
            fx.Record(700, id, "busy", ScUnixNow());
            ScFixture.Write(fx.Transcript(id), 1, ScUser(30), ScAssistant(20, "tool_use", ScTasks("tu1")));
            var s1 = Path.Combine(fx.ProjectDir, id, "subagents", "agent-s1.jsonl");
            ScFixture.Write(s1, 5, ScUser(20, sidechain: true), ScAssistant(5, null, ScText, true));
            ScMeta(s1, "Explore", "Explore", "tu1");
            var monitor = fx.Monitor();
            monitor.Refresh();
            var before = monitor.CachedTails;
            File.Delete(s1);
            File.Delete(Path.ChangeExtension(s1, ".meta.json"));
            monitor.Refresh();
            Check(before == 2 && monitor.CachedTails == 1, "sessions 14: a deleted subagent file is dropped from the tail cache (" + before + " -> " + monitor.CachedTails + ")");
        }
    }
}
