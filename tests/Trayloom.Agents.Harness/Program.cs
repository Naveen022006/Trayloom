// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Text;
using Trayloom.Features.Agents;

namespace Trayloom.Agents.Harness;

/// <summary>
/// Console harness for "Claude means the Claude desktop app": runs <see cref="ClaudeAppUsage.Limits"/> on a copy of
/// <c>plan-usage-history.json</c> (pass its path; the real file is never needed) and checks the app-only transcript filter of
/// <see cref="ClaudeReader"/> on synthetic lines, and checks the desktop account rules in DesktopAccountChecks.cs. Nothing is read from Claude Code
/// or the real Claude app, nothing is started. Exit code 0 = all passed.
/// </summary>
internal static partial class Program
{
    private static int s_failed;
    private static int s_checks;

    private static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            History(args[0]);
        }
        else
        {
            Console.WriteLine("(no history file given: pass a copy of plan-usage-history.json to see the weekly reading)");
        }

        TranscriptFilter();
        SessionReportedRule();
        DesktopAccount(args.Length > 0 ? args[0] : null);
        Mascot();
        Hooks();
        Console.WriteLine($"{s_checks - s_failed}/{s_checks} checks passed");
        return s_failed == 0 ? 0 : 1;
    }

    private static void Check(bool ok, string what)
    {
        s_checks++;
        if (!ok)
        {
            s_failed++;
        }

        Console.WriteLine((ok ? "PASS " : "FAIL ") + what);
    }

    // ------------------------------------------------------------------------------------------ limits from the app's history

    private static void History(string path)
    {
        var samples = ClaudeAppUsage.ReadFile(path);
        Check(samples is { Count: > 0 }, "history file parses (" + (samples?.Count ?? 0) + " samples)");
        if (samples is not { Count: > 0 })
        {
            return;
        }

        var last = samples[^1];
        Console.WriteLine($"newest sample: {last.Date.ToLocalTime():yyyy-MM-dd HH:mm} local, org {(last.Organization is null ? "-" : "set")}, " +
                          string.Join(", ", last.Used.Select(kv => kv.Key + " " + kv.Value.ToString("0.#", CultureInfo.InvariantCulture) + "%")));
        foreach (var (label, now) in new[] { ("now", DateTime.UtcNow), ("14 minutes after the newest sample", last.Date.AddMinutes(14)) })
        {
            // The service passes no organization in app-only mode: the app's history is the app's own account.
            var start = ClaudeAppUsage.SessionStart([], samples, null);
            var limits = ClaudeAppUsage.Limits(samples, now, start, null);
            Console.WriteLine($"-- at {label} ({now.ToLocalTime():yyyy-MM-dd HH:mm} local)");
            if (limits is null)
            {
                Console.WriteLine("   no reading (newest sample is too old or has no running window)");
                continue;
            }

            Console.WriteLine($"   origin={limits.Origin} observed={limits.ObservedAt.ToLocalTime():yyyy-MM-dd HH:mm} SessionReported={limits.SessionReported?.ToString() ?? "null"}");
            foreach (var w in limits.Windows)
            {
                var resets = AgentLimitSupport.DisplayedReset(w, now);
                Console.WriteLine($"   {AgentLimitPlan.Name(w)}: used {w.UsedPercent:0.#}% / left {100 - w.UsedPercent:0.#}%, resets {(resets is { } r ? r.ToLocalTime().ToString("ddd yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " local (" + LimitPresenter.ResetText(w, now, true) + ")" : "unknown")}");
            }

            var plan = new AgentPlan("Max 20x", null);
            var rows = LimitPresenter.Rows(AgentProvider.Claude, limits, now, false, plan);
            Console.WriteLine("   card rows (Max 20x plan): " + string.Join(" | ", rows.Select(r => r.Label + " = " + r.Value + (r.Reset is null ? "" : " [" + r.Reset + "]"))));
            Console.WriteLine("   source line: " + LimitPresenter.SourceLine(limits, now).Text);
            Console.WriteLine("   breakdown: " + (LimitPresenter.Breakdown(limits) ?? "none"));
            if (last.Used.ContainsKey("fh") == false)
            {
                Check(limits.SessionReported == false, "newest sample without a 5-hour figure => SessionReported is false");
                Check(rows.All(r => r.Label != AgentStrings.SessionLimit), "no 5-hour row is listed for it");
            }

            Check(limits.Windows.Any(w => w.Kind == LimitKind.Weekly && w.Scope is null), "weekly window present");
            Check(limits.Origin == LimitOrigins.ClaudeDesktop, "origin is the Claude app");
            Check(LimitPresenter.SourceLine(limits, now).Text.StartsWith("From the Claude app", StringComparison.Ordinal), "source line says the Claude app");
        }
    }

    private static void SessionReportedRule()
    {
        var now = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
        ClaudeAppUsage.Sample S(DateTime t, params (string Key, double Value)[] used) => new(t, null, used.ToDictionary(u => u.Key, u => u.Value));
        var withSession = ClaudeAppUsage.Limits([S(now.AddMinutes(-20), ("sd", 40)), S(now.AddMinutes(-5), ("fh", 10), ("sd", 41))], now);
        var withoutSession = ClaudeAppUsage.Limits([S(now.AddMinutes(-20), ("fh", 10), ("sd", 40)), S(now.AddMinutes(-5), ("sd", 41))], now);
        Check(withSession?.SessionReported == true, "newest sample with fh => SessionReported true");
        Check(withoutSession?.SessionReported == false, "newest sample without fh (older one had it) => SessionReported false");
        var plan = new AgentPlan("Max 5x", null);
        Check(LimitPresenter.Rows(AgentProvider.Claude, withoutSession, now, false, plan).All(r => r.Label != AgentStrings.SessionLimit), "plan row rule: no 5-hour placeholder when the app reports none");
        var sessionRow = LimitPresenter.Rows(AgentProvider.Claude, withSession, now, false, plan).Any(r => r.Label == AgentStrings.SessionLimit);
        Check(sessionRow, "a 5-hour row is shown when the app reports one");
        var gone = withSession! with { Windows = withSession.Windows.Where(w => w.Kind != LimitKind.Session).ToList() };
        Check(LimitPresenter.Rows(AgentProvider.Claude, gone, now, false, plan).Any(r => r.Label == AgentStrings.SessionLimit && r.Value == AgentStrings.NoReading),
            "reported but expired 5-hour window still reads \"No reading yet\"");
    }

    // ------------------------------------------------------------------------------------------ transcript filter

    private static void TranscriptFilter()
    {
        var reader = new ClaudeReader();
        var now = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

        string User(string? entrypoint, string content) =>
            "{\"type\":\"user\"," + (entrypoint is null ? "" : "\"entrypoint\":\"" + entrypoint + "\",") +
            "\"sessionId\":\"s1\",\"cwd\":\"C:\\\\work\\\\demo\",\"timestamp\":\"2026-11-03T11:59:00.000Z\",\"message\":{\"role\":\"user\",\"content\":" + content + "}}";
        string Assistant(string? entrypoint) =>
            "{\"type\":\"assistant\"," + (entrypoint is null ? "" : "\"entrypoint\":\"" + entrypoint + "\",") +
            "\"sessionId\":\"s1\",\"cwd\":\"C:\\\\work\\\\demo\",\"timestamp\":\"2026-11-03T11:59:30.000Z\",\"requestId\":\"r1\",\"message\":{\"id\":\"m1\",\"model\":\"claude-sonnet-4-5\",\"stop_reason\":\"end_turn\",\"content\":[],\"usage\":{\"input_tokens\":10,\"output_tokens\":20}}}";
        // The same text a conversation about this very setting would hold: escaped inside the message, so it is not the field.
        var escaped = "\"I saw {\\\"entrypoint\\\":\\\"claude-desktop\\\"} in a log\"";

        int Count(string line)
        {
            var state = new AgentLogState();
            return reader.Parse(Encoding.UTF8.GetBytes(line), state, now).Count;
        }

        var cases = new (string Name, string Line, int AppOnlyExpects, int AllExpects)[]
        {
            ("desktop user line", User("claude-desktop", "\"hello\""), 1, 1),
            ("terminal (cli) user line", User("cli", "\"hello\""), 0, 1),
            ("vscode user line", User("claude-vscode", "\"hello\""), 0, 1),
            ("user line without entrypoint", User(null, "\"hello\""), 0, 1),
            ("cli line quoting the desktop entrypoint escaped in its message", User("cli", escaped), 0, 1),
            ("line without entrypoint quoting it escaped", User(null, escaped), 0, 1),
            ("desktop assistant line (usage)", Assistant("claude-desktop"), 1, 1),
            ("terminal assistant line (usage)", Assistant("cli"), 0, 1),
        };
        foreach (var c in cases)
        {
            ClaudeReader.AppOnly = true;
            var app = Count(c.Line);
            ClaudeReader.AppOnly = false;
            var all = Count(c.Line);
            Check(app == c.AppOnlyExpects && all == c.AllExpects, $"{c.Name}: app-only {app} (want {c.AppOnlyExpects}), all {all} (want {c.AllExpects})");
        }

        ClaudeReader.AppOnly = false;
    }
}
