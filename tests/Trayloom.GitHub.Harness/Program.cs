// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;
using Trayloom.Features.GitHub;

namespace Trayloom.GitHub.Harness;

/// <summary>
/// Console harness for "GitHub while you code": title parsing, the focus rules, <c>git status</c> and remote parsing, the GraphQL query and the
/// check summary. Pure functions and fixtures only; no network and no real repository (the index checks use empty <c>.git</c> folders in a
/// temporary directory that is deleted at the end). Pass a directory as the first argument to choose where it is made. Exit code 0 = all passed.
/// </summary>
internal static class Program
{
    private static int s_failed;
    private static int s_checks;

    private static int Main(string[] args)
    {
        Titles();
        Focus();
        Status();
        Remotes();
        Query();
        Summaries();
        Finished();
        PullChoice();
        Strings();
        Index(args.Length > 0 ? args[0] : Path.GetTempPath());
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

        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}");
    }

    private static bool Is(TitleHint hint, TitleHintKind kind, params string[] candidates) =>
        hint.Kind == kind && hint.Candidates.SequenceEqual(candidates);

    // ---------------------------------------------------------------- titles

    private static void Titles()
    {
        Check(Is(CodingTitles.Parse("Code.exe", "Program.cs - trayloom - Visual Studio Code"), TitleHintKind.Folder, "trayloom", "Program.cs"), "VS Code: file and folder");
        Check(Is(CodingTitles.Parse("Code.exe", "● Program.cs - trayloom - Visual Studio Code"), TitleHintKind.Folder, "trayloom", "Program.cs"), "VS Code: unsaved dot is dropped");
        Check(Is(CodingTitles.Parse("Code.exe", "trayloom - Visual Studio Code"), TitleHintKind.Folder, "trayloom"), "VS Code: folder only");
        Check(CodingTitles.Parse("Code.exe", "main.py - api [WSL: Ubuntu] - Visual Studio Code").Kind == TitleHintKind.None, "VS Code: WSL window is not followed");
        Check(CodingTitles.Parse("Code.exe", "main.py - api [SSH: box] - Visual Studio Code").Kind == TitleHintKind.None, "VS Code: SSH window is not followed");
        Check(Is(CodingTitles.Parse("Code.exe", "README.md - trayloom (Workspace) - Visual Studio Code"), TitleHintKind.Folder, "trayloom", "README.md"), "VS Code: (Workspace) is removed");
        Check(Is(CodingTitles.Parse("Code - Insiders.exe", "a.ts - web - Visual Studio Code - Insiders"), TitleHintKind.Folder, "web", "a.ts"), "VS Code Insiders");
        Check(Is(CodingTitles.Parse("Cursor.exe", "x.rs - engine - Cursor"), TitleHintKind.Folder, "engine", "x.rs"), "Cursor");
        Check(Is(CodingTitles.Parse("code.exe", "x.rs - engine - Visual Studio Code"), TitleHintKind.Folder, "engine", "x.rs"), "image name ignores case");
        Check(CodingTitles.Parse("Code.exe", "Welcome").Kind == TitleHintKind.None, "VS Code: a title without the product suffix is not read");
        Check(Is(CodingTitles.Parse("devenv.exe", "Trayloom (Running) - Microsoft Visual Studio"), TitleHintKind.Folder, "Trayloom"), "Visual Studio: debug group is dropped");
        Check(Is(CodingTitles.Parse("rider64.exe", "Trayloom – App.xaml.cs"), TitleHintKind.Folder, "Trayloom"), "JetBrains: first segment");
        Check(Is(CodingTitles.Parse("idea64.exe", "Trayloom [E:\\x\\Trayloom] – App.java"), TitleHintKind.Folder, "Trayloom"), "JetBrains: bracket is dropped");
        Check(Is(CodingTitles.Parse("WindowsTerminal.exe", "PS E:\\REA TEST 1\\trayloom> "), TitleHintKind.Path, "E:\\REA TEST 1\\trayloom"), "terminal: PowerShell prompt path");
        Check(Is(CodingTitles.Parse("WindowsTerminal.exe", "Administrator: PS C:\\src\\app>"), TitleHintKind.Path, "C:\\src\\app"), "terminal: elevated prompt path");
        Check(Is(CodingTitles.Parse("mintty.exe", "MINGW64:/e/REA TEST 1/trayloom"), TitleHintKind.Path, "E:\\REA TEST 1\\trayloom"), "terminal: Git Bash path");
        Check(Is(CodingTitles.Parse("mintty.exe", "/c/src/app"), TitleHintKind.Path, "C:\\src\\app"), "terminal: bare unix path");
        Check(CodingTitles.Parse("WindowsTerminal.exe", "✳ Fix the tray menu").Kind == TitleHintKind.None, "terminal: Claude Code task title has no path");
        Check(CodingTitles.Parse("chrome.exe", "GitHub - Google Chrome").Kind == TitleHintKind.None, "browser is not a coding app");
        Check(CodingTitles.IsCodingApp("Code.exe") && CodingTitles.IsCodingApp("DEVENV.EXE") && CodingTitles.IsCodingApp("pwsh.exe") && !CodingTitles.IsCodingApp("chrome.exe"), "IsCodingApp");
    }

    // ---------------------------------------------------------------- focus

    private static CodingInput Input(DateTime now, RepoRef? front = null, bool frontIsCoding = false, (RepoRef, DateTime)? last = null, (RepoRef, DateTime)[]? agents = null, (RepoRef, DateTime)[]? git = null) =>
        new(front, frontIsCoding, last, agents ?? [], git ?? [], now);

    private static void Focus()
    {
        var now = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
        var a = new RepoRef("C:\\a", "a");
        var b = new RepoRef("C:\\b", "b");
        var c = new RepoRef("C:\\c", "c");

        var r = CodingFocus.Decide(Input(now, a, true, agents: [(b, now.AddMinutes(-1))]));
        Check(r.Repo == a && r.Reason == CodingReason.Foreground, "focus: foreground repo beats a Claude session");
        r = CodingFocus.Decide(Input(now, null, false, (a, now.AddMinutes(-5))));
        Check(r.Repo == a && r.Reason == CodingReason.Recent, "focus: browser in front, last coding 5 min ago stays");
        r = CodingFocus.Decide(Input(now, null, false, (a, now.AddMinutes(-20)), [(b, now.AddMinutes(-2))]));
        Check(r.Repo == b && r.Reason == CodingReason.Agent, "focus: last coding 20 min ago, Claude session 2 min ago");
        r = CodingFocus.Decide(Input(now, git: [(c, now.AddMinutes(-3))]));
        Check(r.Repo == c && r.Reason == CodingReason.Git, "focus: git activity 3 min ago");
        r = CodingFocus.Decide(Input(now, git: [(c, now.AddMinutes(-12))]));
        Check(r.Repo is null && r.Reason is null, "focus: git activity 12 min ago is not coding");
        r = CodingFocus.Decide(Input(now, null, true, agents: [(b, now.AddMinutes(-30))]));
        Check(r.Repo is null, "focus: a Claude session idle for 30 min does not count");
        r = CodingFocus.Decide(Input(now, null, true, agents: [(a, now.AddMinutes(-9)), (b, now.AddMinutes(-2))]));
        Check(r.Repo == b, "focus: the newest Claude session wins");
        r = CodingFocus.Decide(Input(now, null, true, (a, now.AddMinutes(-1))));
        Check(r.Repo == a && r.Reason == CodingReason.Recent, "focus: a coding app with no repository falls back to the last one");
    }

    // ---------------------------------------------------------------- git

    private static void Status()
    {
        const string sample = """
            # branch.oid 0123456789abcdef0123456789abcdef01234567
            # branch.head feature/x
            # branch.upstream origin/feature/x
            # branch.ab +2 -1
            1 .M N... 100644 100644 100644 aaaa bbbb src/a.cs
            1 M. N... 100644 100644 100644 aaaa bbbb src/b.cs
            2 R. N... 100644 100644 100644 aaaa bbbb R100 src/c.cs	src/old.cs
            u UU N... 100644 100644 100644 100644 aaaa bbbb cccc src/d.cs
            ? notes.txt
            """;
        var s = GitCli.ParseStatus(sample)!;
        Check(s.Ahead == 2 && s.Behind == 1, "status: ahead 2, behind 1");
        Check(s.Staged == 2 && s.Unstaged == 1 && s.Conflicts == 1 && s.Untracked == 1, "status: staged 2, unstaged 1, conflicts 1, untracked 1");
        Check(s.Changes == 5, "status: five changed files");
        Check(s.Branch == "feature/x" && !s.Detached && s.Upstream == "origin/feature/x" && s.HeadSha is { Length: 40 }, "status: branch, upstream and sha");

        var both = GitCli.ParseStatus("# branch.oid abc\n# branch.head main\n1 MM N... 100644 100644 100644 a b f.cs\n")!;
        Check(both.Staged == 1 && both.Unstaged == 1 && both.Changes == 1, "status: a file that is staged and unstaged counts once");

        var detached = GitCli.ParseStatus("# branch.oid 0123456789abcdef0123456789abcdef01234567\n# branch.head (detached)\n")!;
        Check(detached.Detached && detached.Branch is null && detached.HeadSha is not null, "status: detached head");
        var initial = GitCli.ParseStatus("# branch.oid (initial)\n# branch.head main\n")!;
        Check(initial.HeadSha is null && initial.Branch == "main" && initial.Upstream is null && initial.Ahead == 0, "status: initial commit has no sha and no upstream");
        Check(GitCli.ParseStatus("") is null && GitCli.ParseStatus("garbage") is null, "status: nothing recognisable gives null");
        Check(GitCli.ParseStatus("# branch.oid x\r\n# branch.head main\r\n? a\r\n")!.Untracked == 1, "status: CRLF output");
    }

    private static void Remotes()
    {
        foreach (var url in new[]
        {
            "https://github.com/o/n", "https://github.com/o/n.git", "https://github.com/o/n/", "https://github.com/o/n.git/",
            "git@github.com:o/n.git", "git@github.com:o/n", "ssh://git@github.com/o/n.git", "https://user:tok@github.com/o/n", "https://tok@github.com/o/n.git",
            "HTTPS://GitHub.com/o/n",
        })
        {
            Check(GitCli.ParseRemote(url) is { Owner: "o", Name: "n" }, "remote: " + (url.Contains("tok") ? "url with a token" : url));
        }

        Check(GitCli.ParseRemote("https://gitlab.com/o/n.git") is null, "remote: gitlab is not followed");
        Check(GitCli.ParseRemote("https://github.example.com/o/n") is null, "remote: github.example.com is not github.com");
        Check(GitCli.ParseRemote("https://github.com.evil.com/o/n") is null, "remote: look-alike host");
        Check(GitCli.ParseRemote("https://github.com/o") is null && GitCli.ParseRemote("") is null, "remote: incomplete url");
        Check(GitCli.ParseRemote("https://github.com/o/n.name.git") is { Name: "n.name" }, "remote: dots in the name");
        Check(GitCli.ParseRemote("https://github.com/bad_owner/n") is null, "remote: an owner with an underscore is invalid");
    }

    // ---------------------------------------------------------------- query

    private static void Query()
    {
        var slug = new GhSlug("octo", "repo.x");
        var sha = new string('a', 40);
        var q = CodingChecks.BuildQuery(slug, "feat/\"q\"\\é", sha, true);
        Check(q.Contains("repository(owner: \"octo\", name: \"repo.x\")"), "query: owner and name are literals");
        Check(q.Contains("headRefName: \"feat/\\u0022q\\u0022\\\\\\u00E9\""), "query: quote, backslash and é are escaped");
        Check(!q.Contains("\"q\""), "query: no raw quote from the branch");
        Check(q.Contains("pushed: object(oid: \"" + sha + "\")"), "query: pushed commit asked for");
        Check(q.Contains("parent {"), "query: parent repository asked for");
        Check(!CodingChecks.BuildQuery(slug, "main", sha, false).Contains("parent {"), "query: no parent when not asked");
        Check(!CodingChecks.BuildQuery(slug, "main", "xyz", true).Contains("pushed:"), "query: an invalid sha leaves out pushed");
        Check(!CodingChecks.BuildQuery(slug, "main", new string('A', 40), true).Contains("pushed:"), "query: an upper-case sha leaves out pushed");
        Check(!CodingChecks.BuildQuery(slug, "main", null, true).Contains("pushed:"), "query: no sha, no pushed");
        Check(Throws(() => CodingChecks.BuildQuery(new GhSlug("bad\"owner", "n"), "main", null, false)), "query: an invalid owner throws");
        Check(Throws(() => CodingChecks.BuildQuery(new GhSlug("o", "n\") { evil"), "main", null, false)), "query: an invalid name throws");
        Check(Throws(() => CodingChecks.BuildQuery(slug, "", null, false)), "query: an empty branch throws");
        Check(q.Contains("fragment P on PullRequest") && q.Contains("fragment R on StatusCheckRollup") && q.Contains("rateLimit"), "query: fragments and rate limit");
    }

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    // ---------------------------------------------------------------- checks

    private static string Rollup(string state, params string[] nodes) =>
        $"{{\"state\":\"{state}\",\"contexts\":{{\"totalCount\":{nodes.Length},\"nodes\":[{string.Join(',', nodes)}]}}}}";

    private static string Run(string status, string? conclusion, string name = "build") =>
        $"{{\"__typename\":\"CheckRun\",\"name\":\"{name}\",\"status\":\"{status}\",\"conclusion\":{(conclusion is null ? "null" : "\"" + conclusion + "\"")},\"detailsUrl\":\"https://github.com/o/n/actions/runs/1\"}}";

    private static string Ctx(string state, string name = "ci") =>
        $"{{\"__typename\":\"StatusContext\",\"context\":\"{name}\",\"state\":\"{state}\",\"targetUrl\":\"https://ci.example.com/1\"}}";

    private static CheckSummary Sum(string json, GhSlug? slug = null, string? sha = null)
    {
        using var doc = JsonDocument.Parse(json);
        return CodingChecks.Summarize(doc.RootElement.Clone(), slug, sha);
    }

    private static void Summaries()
    {
        foreach (var (conclusion, expected) in new[]
        {
            ("SUCCESS", "passed"), ("NEUTRAL", "skipped"), ("SKIPPED", "skipped"), ("STALE", "skipped"),
            ("FAILURE", "failed"), ("TIMED_OUT", "failed"), ("CANCELLED", "failed"), ("ACTION_REQUIRED", "failed"), ("STARTUP_FAILURE", "failed"),
        })
        {
            var s = Sum(Rollup("SUCCESS", Run("COMPLETED", conclusion)));
            var ok = expected switch
            {
                "passed" => s.Passed == 1 && s.State == CheckState.Passing,
                "skipped" => s.Skipped == 1 && s.State == CheckState.Passing,
                _ => s.Failed == 1 && s.State == CheckState.Failing,
            };
            Check(ok, "summary: CheckRun " + conclusion + " is " + expected);
        }

        Check(Sum(Rollup("PENDING", Run("IN_PROGRESS", null))) is { Running: 1, State: CheckState.Running }, "summary: IN_PROGRESS is running");
        Check(Sum(Rollup("PENDING", Run("QUEUED", null))) is { Running: 1 }, "summary: QUEUED is running");
        Check(Sum(Rollup("SUCCESS", Ctx("SUCCESS"))) is { Passed: 1, State: CheckState.Passing }, "summary: StatusContext SUCCESS");
        Check(Sum(Rollup("PENDING", Ctx("PENDING"))) is { Running: 1 } && Sum(Rollup("PENDING", Ctx("EXPECTED"))) is { Running: 1 }, "summary: StatusContext PENDING and EXPECTED run");
        Check(Sum(Rollup("FAILURE", Ctx("FAILURE"))) is { Failed: 1 } && Sum(Rollup("ERROR", Ctx("ERROR"))) is { Failed: 1 }, "summary: StatusContext FAILURE and ERROR fail");
        Check(Sum(Rollup("PENDING", Ctx("PENDING"), Run("COMPLETED", "SUCCESS"))) is { State: CheckState.Running, Running: 1, Passed: 1, Total: 2 }, "summary: pending status plus a passed run is running");
        var mixed = Sum(Rollup("PENDING", Run("COMPLETED", "FAILURE", "lint"), Run("IN_PROGRESS", null, "test")));
        Check(mixed.State == CheckState.Failing && mixed.Running == 1 && mixed.Failed == 1 && mixed.Failing.SequenceEqual(["lint"]), "summary: a failure while another check runs is failing with running above zero");
        Check(Sum("{\"state\":\"SUCCESS\"}") is { State: CheckState.Passing, Total: 0 }, "summary: no contexts, rollup SUCCESS is passing");
        Check(Sum("{\"state\":\"FAILURE\"}").State == CheckState.Failing && Sum("{\"state\":\"PENDING\"}").State == CheckState.Running, "summary: no contexts follows the rollup state");
        Check(Sum("null").State == CheckState.None && Sum("{}").State == CheckState.None, "summary: no rollup is no checks");
        var many = Sum(Rollup("SUCCESS", Run("COMPLETED", "FAILURE", "a"), Run("COMPLETED", "FAILURE", "b"), Run("COMPLETED", "FAILURE", "c"), Run("COMPLETED", "FAILURE", "d")));
        Check(many.Failing.SequenceEqual(["a", "b", "c"]) && many.Failed == 4, "summary: the first three failing names");
        var slug = new GhSlug("o", "n");
        var sha = new string('b', 40);
        Check(Sum(Rollup("FAILURE", Run("COMPLETED", "FAILURE")), slug, sha).Url == "https://github.com/o/n/actions/runs/1", "summary: failing url comes from the check");
        Check(Sum(Rollup("FAILURE", Ctx("FAILURE")), slug, sha).Url == $"https://github.com/o/n/commit/{sha}/checks", "summary: a url outside github.com falls back to the commit checks page");
        Check(Sum(Rollup("SUCCESS", Run("COMPLETED", "SUCCESS")), slug, sha).Url == $"https://github.com/o/n/commit/{sha}/checks", "summary: passing url is the commit checks page");

        // 30 contexts are asked for; a rollup that is worse than what came back still counts.
        var hidden = "{\"state\":\"FAILURE\",\"contexts\":{\"totalCount\":45,\"nodes\":[" + Run("COMPLETED", "SUCCESS") + "]}}";
        Check(Sum(hidden) is { State: CheckState.Failing, Total: 45 }, "summary: a failure in contexts beyond the first page still shows");
    }

    private static void Finished()
    {
        var running = new CheckSummary(CheckState.Running, 3, 1, 0, 2, 0, [], null);
        var passing = new CheckSummary(CheckState.Passing, 3, 3, 0, 0, 0, [], null);
        var failing = new CheckSummary(CheckState.Failing, 3, 2, 1, 0, 0, ["x"], null);
        var none = new CheckSummary(CheckState.None, 0, 0, 0, 0, 0, [], null);
        Check(CodingChecks.Finished(running, passing, true), "finished: running to passing on the same sha");
        Check(CodingChecks.Finished(running, failing, true), "finished: running to failing on the same sha");
        Check(!CodingChecks.Finished(null, passing, true), "finished: first sight passing is not an alert");
        Check(!CodingChecks.Finished(running, passing, false), "finished: a different sha is not an alert");
        Check(!CodingChecks.Finished(running, running, true), "finished: still running");
        Check(!CodingChecks.Finished(passing, passing, true), "finished: passing to passing");
        Check(!CodingChecks.Finished(running, none, true), "finished: no checks left is not an alert");
    }

    private static void PullChoice()
    {
        var slug = new GhSlug("me", "app");
        var pushed = new string('c', 40);
        var other = new string('d', 40);
        string Pr(int number, string owner, string oid, string rollup) =>
            $"{{\"number\":{number},\"title\":\"T{number}\",\"url\":\"https://github.com/me/app/pull/{number}\",\"isDraft\":false,\"reviewDecision\":\"APPROVED\",\"headRepositoryOwner\":{{\"login\":\"{owner}\"}},\"commits\":{{\"nodes\":[{{\"commit\":{{\"oid\":\"{oid}\",\"statusCheckRollup\":{rollup}}}}}]}}}}";
        var passing = Rollup("SUCCESS", Run("COMPLETED", "SUCCESS"));
        var failing = Rollup("FAILURE", Run("COMPLETED", "FAILURE"));

        string Data(string repoPulls, string parentPulls = "[]", string? pushedObject = null) =>
            "{\"repository\":{\"nameWithOwner\":\"me/app\",\"defaultBranchRef\":{\"name\":\"main\"},\"pullRequests\":{\"nodes\":" + repoPulls + "},"
            + "\"parent\":{\"nameWithOwner\":\"up/app\",\"pullRequests\":{\"nodes\":" + parentPulls + "}},"
            + (pushedObject is null ? "" : "\"pushed\":" + pushedObject + ",")
            + "\"url\":\"https://github.com/me/app\"},\"rateLimit\":{\"cost\":1,\"remaining\":4000}}";

        CodingRemote Read(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return CodingChecks.Parse(doc.RootElement.Clone(), slug, pushed);
        }

        var one = Read(Data("[" + Pr(7, "me", pushed, passing) + "]"));
        Check(one.Found && one.DefaultBranch == "main" && one.Pull is { Number: 7, Repo: "me/app", ReviewDecision: "APPROVED" } && one.Remaining == 4000, "pull: the branch pull request and the default branch");
        Check(one.Checks is { State: CheckState.Passing } && one.ChecksSha == pushed, "pull: checks come from the pull request's last commit when it is the pushed one");

        var fork = Read(Data("[" + Pr(1, "someone", pushed, passing) + "," + Pr(2, "ME", pushed, failing) + "]"));
        Check(fork.Pull?.Number == 2, "pull: only pull requests opened from your own repository count, owner compared without case");

        var parent = Read(Data("[]", "[" + Pr(9, "me", pushed, failing) + "]"));
        Check(parent.Pull is { Number: 9, Repo: "up/app" }, "pull: the parent repository is searched for a fork workflow");

        var stale = Read(Data("[" + Pr(3, "me", other, passing) + "]", pushedObject: "{\"oid\":\"" + pushed + "\",\"statusCheckRollup\":" + failing + "}"));
        Check(stale.Pull?.Number == 3 && stale.Checks is { State: CheckState.Failing } && stale.ChecksSha == pushed, "pull: when the pull request is behind the push, the pushed commit's checks are used");

        var noPull = Read(Data("[]", pushedObject: "{\"oid\":\"" + pushed + "\",\"statusCheckRollup\":" + passing + "}"));
        Check(noPull.Pull is null && noPull.Checks is { State: CheckState.Passing }, "pull: no pull request still shows the checks of the push");

        var missing = Read("{\"repository\":null,\"rateLimit\":{\"cost\":1,\"remaining\":10}}");
        Check(!missing.Found && missing.Pull is null && missing.Remaining == 10, "pull: a repository you cannot see is not found");
        Check(Read("{}").Remaining == int.MaxValue, "pull: no rate limit block means unknown");
    }

    // ---------------------------------------------------------------- strings and lines

    private static void Strings()
    {
        var snapshot = new CodingSnapshot(
            new RepoRef("C:\\r", "r"), CodingReason.Foreground,
            new GitState("main", false, new string('a', 40), "origin/main", 1, 2, 1, 1, 1, 0, false, 1), GitError.None,
            new GhSlug("o", "r"), "main", null, new CheckSummary(CheckState.Running, 4, 2, 0, 2, 0, [], null), DateTime.UtcNow, DateTime.UtcNow, GhError.None);
        Check(snapshot.Git!.Changes == 2, "lines: changes count");
        Check(CodingLines.Tooltip(snapshot) == "r · main · 2 changed · 1 to push · 2 to pull · Checks running · 2 of 4 done", "lines: strip tooltip");
        Check(CodingLines.StateLine(snapshot) == "2 changed · 1 to push · 2 to pull", "lines: card state line");
        Check(CodingLines.StateLine(snapshot with { Git = snapshot.Git with { Upstream = null, Ahead = 0, Behind = 0 } }) == "2 changed · Not pushed yet", "lines: not pushed yet");
        Check(CodingLines.StateLine(snapshot with { Git = snapshot.Git with { Staged = 0, Unstaged = 0, Untracked = 0, Both = 0, Ahead = 0, Behind = 0 } }) == "No changes", "lines: no changes");
        Check(CodingLines.StateLine(snapshot with { Git = null, GitError = GitError.Missing }) == "Git was not found. Install Git for Windows to follow repositories.", "lines: git missing");
        Check(CodingLines.ChecksText(new CheckSummary(CheckState.Passing, 5, 5, 0, 0, 0, [], null)) == "All 5 checks passed", "lines: all checks passed");
        Check(CodingLines.ChecksText(new CheckSummary(CheckState.Failing, 5, 3, 2, 0, 0, ["lint", "test"], null)) == "2 failing: lint, test", "lines: failing checks");
        Check(CodingLines.PullLine(new BranchPull(12, "Fix", "https://github.com/o/r/pull/12", true, null, "o/r")) == "#12 Fix · Draft", "lines: draft pull request");
        Check(CodingLines.PullLine(new BranchPull(12, "Fix", "https://github.com/o/r/pull/12", false, "CHANGES_REQUESTED", "o/r")) == "#12 Fix · Changes requested", "lines: changes requested");
        Check(CodingLines.Status(CodingSnapshot.Empty) == "Not coding right now" && CodingLines.Status(snapshot) == "Working in r on main", "lines: settings status");
        Check(snapshot.BranchLabel == "main" && (snapshot with { Git = snapshot.Git with { Detached = true, Branch = null } }).BranchLabel == new string('a', 7), "lines: detached head shows the short sha");

        var compare = CodingIsland.CreatePullUrl(snapshot with { Git = snapshot.Git with { Branch = "feat/x y" } });
        Check(compare == "https://github.com/o/r/compare/feat%2Fx%20y?expand=1", "card: create pull request url");
        Check(CodingIsland.CreatePullUrl(snapshot) is null, "card: no create pull request on the default branch");
        Check(CodingIsland.CreatePullUrl(snapshot with { Git = snapshot.Git with { Branch = "x", Upstream = null } }) is null, "card: no create pull request before the branch is pushed");
        Check(CodingIsland.CreatePullUrl(snapshot with { Git = snapshot.Git with { Branch = "x" }, Pull = new BranchPull(1, "t", "u", false, null, "o/r") }) is null, "card: no create pull request when one exists");
        Check(CodingIsland.CreatePullUrl(snapshot with { Git = snapshot.Git with { Branch = "x" }, GitHubAt = null }) is null, "card: no create pull request before GitHub answered");

        var shape = CodingTracker.Shape(snapshot);
        Check(shape != CodingTracker.Shape(snapshot with { Git = snapshot.Git with { Staged = 20, Unstaged = 0, Both = 0, Untracked = 0 } }), "shape: a second digit changes the strip shape");
        Check(shape == CodingTracker.Shape(snapshot with { Git = snapshot.Git with { Staged = 1, Unstaged = 2, Both = 0, Untracked = 0 } }), "shape: the same number of digits keeps it");
        Check(CodingTracker.Signature(snapshot) != CodingTracker.Signature(snapshot with { Pull = new BranchPull(1, "t", "u", false, null, "o/r") }), "signature: a new pull request is a change");
    }

    // ---------------------------------------------------------------- repository index

    private static void Index(string baseDir)
    {
        var root = Path.Combine(baseDir, "trayloom-github-harness-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var one = Path.Combine(root, "one", "Proj");
            var two = Path.Combine(root, "two", "proj");
            var worktree = Path.Combine(root, "wt");
            var plain = Path.Combine(root, "plain", "deep");
            Directory.CreateDirectory(Path.Combine(one, ".git"));
            Directory.CreateDirectory(Path.Combine(two, ".git"));
            Directory.CreateDirectory(Path.Combine(one, "src", "inner"));
            Directory.CreateDirectory(worktree);
            Directory.CreateDirectory(Path.Combine(root, "real-git"));
            File.WriteAllText(Path.Combine(worktree, ".git"), "gitdir: ../real-git\n");
            Directory.CreateDirectory(plain);

            Check(RepoIndex.FindRoot(Path.Combine(one, "src", "inner"))?.Root == one, "index: FindRoot walks up to the folder with .git");
            Check(RepoIndex.FindRoot(one)?.Name == "Proj", "index: the name is the folder name");
            Check(RepoIndex.FindRoot(worktree)?.Root == worktree && RepoIndex.GitDir(worktree) == Path.GetFullPath(Path.Combine(root, "real-git")), "index: a .git file (worktree) is a repository and points to its git dir");
            Check(RepoIndex.FindRoot(plain) is null && RepoIndex.FindRoot("") is null && RepoIndex.FindRoot("Z:\\no\\such\\folder") is null, "index: no repository");

            File.WriteAllText(Path.Combine(one, ".git", "index"), "x");
            File.SetLastWriteTimeUtc(Path.Combine(one, ".git", "index"), DateTime.UtcNow.AddMinutes(-30));
            File.WriteAllText(Path.Combine(two, ".git", "index"), "x");
            File.SetLastWriteTimeUtc(Path.Combine(two, ".git", "index"), DateTime.UtcNow.AddMinutes(-1));

            var file = Path.Combine(root, "coding-repos.json");
            var index = new RepoIndex(file);
            index.Add(new RepoRef(one, "Proj"), DateTime.UtcNow.AddHours(-1));
            Check(index.ByName("PROJ")?.Root == one, "index: ByName ignores case and finds a unique match");
            index.Add(new RepoRef(two, "proj"), DateTime.UtcNow.AddHours(-2));
            Check(index.ByName("proj")?.Root == two, "index: on a tie the repository whose index file changed last wins");
            Check(index.ByName("nothing") is null, "index: unknown name");

            index.Save();
            var again = new RepoIndex(file);
            again.Load();
            Check(again.Count == 2 && again.ByName("proj")?.Root == two, "index: saved and loaded again");
            Check(File.ReadAllText(file).StartsWith("{\"v\":1,\"repos\":[", StringComparison.Ordinal), "index: file format");

            var big = new RepoIndex(null);
            for (var i = 0; i < 250; i++)
            {
                big.Add(new RepoRef("C:\\r" + i, "r" + i), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i));
            }

            var kept = big.Recent(1000);
            Check(kept.Count == RepoIndex.MaxEntries && kept[0].Root == "C:\\r249" && kept.All(r => r.Root != "C:\\r0"), "index: at most 200 entries, least recently seen dropped first");

            var scan = new RepoIndex(null);
            Directory.CreateDirectory(Path.Combine(root, "projects", "a", ".git"));
            Directory.CreateDirectory(Path.Combine(root, "projects", "group", "b", ".git"));
            Directory.CreateDirectory(Path.Combine(root, "projects", "group", "c", "d", ".git"));
            Directory.CreateDirectory(Path.Combine(root, "projects", "node_modules", "e", ".git"));
            scan.ScanFolders(Path.Combine(root, "projects") + "|" + Path.Combine(root, "missing"));
            var names = scan.Recent(10).Select(r => r.Name).OrderBy(n => n).ToArray();
            Check(names.SequenceEqual(["a", "b"]), "index: project folders are scanned two levels deep, skipping node_modules");

            var storageHome = Path.Combine(root, "appdata");
            Directory.CreateDirectory(Path.Combine(storageHome, "Code", "User", "globalStorage"));
            var uri = new Uri(one).AbsoluteUri;
            File.WriteAllText(Path.Combine(storageHome, "Code", "User", "globalStorage", "storage.json"),
                JsonSerializer.Serialize(new Dictionary<string, object> { ["backupWorkspaces"] = new { folders = new[] { new { folderUri = uri }, new { folderUri = "file:///Z:/gone" } } }, ["other"] = "text" }));
            var fromStorage = new RepoIndex(null);
            fromStorage.ScanEditorStorage(storageHome);
            Check(fromStorage.Recent(5).Select(r => r.Root).SequenceEqual([one]), "index: editor storage file:/// folders inside a repository");

            index.Erase();
            Check(!File.Exists(file) && index.Count == 0, "index: erase deletes the file");
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine("note: could not delete the temporary folder");
            }
        }
    }
}
