// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.GitHub;

/// <summary>The settings keys of "GitHub while you code" (spec 09 part 2).</summary>
internal static class CodingKeys
{
    public const string Enabled = "notchGitHubCoding";
    public const string Strip = "notchGitHubCodingStrip";
    public const string Alerts = "notchGitHubCodingAlerts";
    public const string Folders = "notchGitHubCodingFolders";

    public static void Register(IAppSettings app)
    {
        app.Register(Enabled, true);
        app.Register(Strip, true);
        app.Register(Alerts, true);
        app.Register(Folders, "");
    }
}

/// <summary>Every user-visible string of "GitHub while you code"; English verbatim from spec 09.</summary>
internal static class CodingText
{
    // settings
    public static string Title => Loc.T("GitHub while you code");

    public static string Follow => Loc.T("Follow the repository you are working in");

    public static string FollowCaption =>
        Loc.T("Trayloom reads the title of the editor or terminal in front, Claude Code’s open sessions and recent Git activity to find the repository. It runs read-only Git commands on this PC. With GitHub connected, it asks GitHub for that branch’s pull request and checks.");

    public static string ShowStrip => Loc.T("Show it in the closed island");

    public static string ShowStripCaption => Loc.T("Branch, changes and checks while you code. Music, timers and agents come first; click the island to switch.");

    public static string AlertWhenFinished => Loc.T("Alert when checks finish");

    public static string AlertCaption => Loc.T("Passed or failed, once for each push.");

    public static string ProjectFolders => Loc.T("Project folders");

    public static string ProjectFoldersCaption => Loc.T("Optional. Repositories inside these folders are recognised from editor titles that show only a folder name.");

    public static string AddFolder => Loc.T("Add folder…");

    public static string RemoveFolder => Loc.T("Remove");

    public static string WorkingIn(string repo, string branch) => Loc.F("Working in {0} on {1}", repo, branch);

    public static string NotCoding => Loc.T("Not coding right now");

    // git errors
    public static string GitMissing => Loc.T("Git was not found. Install Git for Windows to follow repositories.");

    public static string GitNotTrusted => Loc.T("Git does not trust this folder. Run “git config --global --add safe.directory <path>” in a terminal.");

    public static string GitTimedOut => Loc.T("Git did not answer in time.");

    public static string? GitErrorText(GitError error) => error switch
    {
        GitError.Missing => GitMissing,
        GitError.NotTrusted => GitNotTrusted,
        GitError.TimedOut => GitTimedOut,
        _ => null,
    };

    // card and strip
    public static string Changed(int n) => Loc.F("{0} changed", n);

    public static string NoChanges => Loc.T("No changes");

    public static string ToPush(int n) => Loc.F("{0} to push", n);

    public static string ToPull(int n) => Loc.F("{0} to pull", n);

    public static string NotPushed => Loc.T("Not pushed yet");

    public static string PullTitle(int number, string title) => Loc.F("#{0} {1}", number, title);

    public static string NoPull => Loc.T("No pull request for this branch");

    public static string ChecksRunning(int done, int total) => Loc.F("Checks running · {0} of {1} done", done, total);

    public static string AllPassed(int n) => Loc.F("All {0} checks passed", n);

    public static string Failing(int n, string names) => Loc.F("{0} failing: {1}", n, names);

    public static string NoChecks => Loc.T("No checks");

    public static string ChecksPassed => Loc.T("Checks passed");

    public static string ConnectLine => Loc.T("Connect GitHub to see the pull request and checks of this branch.");

    public static string OpenPull => Loc.T("Open pull request");

    public static string OpenChecks => Loc.T("Open checks");

    public static string CreatePull => Loc.T("Create pull request");

    public static string OpenRepo => Loc.T("Open repository");
}
