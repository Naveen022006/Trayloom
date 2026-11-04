// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.GitHub;

/// <summary>Every user-visible string of the GitHub extension (spec 06 section 2); English verbatim from the spec.</summary>
internal static class GhText
{
    public const string FeatureId = "notch-github";

    public static string Title => Loc.T("GitHub");

    public static string Summary => Loc.T("Notifications, pull requests and checks of your GitHub account.");

    // island page
    public static string ConnectPrompt => Loc.T("Connect your GitHub account to see notifications, pull requests and checks here.");

    public static string NothingSent => Loc.T("Nothing is sent to GitHub until you connect.");

    public static string ConnectButton => Loc.T("Connect…");

    public static string TileUnread => Loc.T("Unread");

    public static string TileMine => Loc.T("Your PRs");

    public static string TileReview => Loc.T("To review");

    public static string TileFailing => Loc.T("Failing");

    public static string NoUnread => Loc.T("No unread notifications.");

    public static string NeedsClassic => Loc.T("Notifications need a classic token.");

    public static string NoPulls => Loc.T("No open pull requests.");

    public static string NoReviews => Loc.T("Nothing waits for your review.");

    public static string ShowAll => Loc.T("Show all on GitHub");

    public static string NoRepos => Loc.T("No repositories to check.");

    public static string AllPass => Loc.T("All checks pass.");

    public static string NotFound => Loc.T("Not found");

    public static string Draft => Loc.T("Draft");

    public static string Approved => Loc.T("Approved");

    public static string ChangesRequested => Loc.T("Changes requested");

    public static string ReviewRequired => Loc.T("Review required");

    public static string Passing => Loc.T("Passing");

    public static string FailingWord => Loc.T("Failing");

    public static string Running => Loc.T("Running");

    public static string NoChecks => Loc.T("No checks");

    public static string Refresh => Loc.T("Refresh");

    public static string OpenGitHub => Loc.T("Open GitHub");

    public static string TryAgain => Loc.T("Try again");

    public static string SettingsButton => Loc.T("Settings…");

    public static string Footer(string login, string relative) => Loc.F("@{0} · Updated {1}", login, relative);

    public static string RepoCaption(string repo, string relative) => Loc.F("{0} · {1}", repo, relative);

    public static string ReleasedCaption(string tag, string relative) => Loc.F(" · {0} released {1}", tag, relative);

    public static string JustNow => Loc.T("just now");

    public static string MinutesAgo(int m) => Loc.F("{0} min ago", m);

    public static string HoursAgo(int h) => Loc.F("{0} h ago", h);

    public static string DaysAgo(int d) => Loc.F("{0} d ago", d);

    public static string ErrOffline(string time) => Loc.F("Offline. Showing what was loaded at {0}.", time);

    public static string ErrSignedOut => Loc.T("The GitHub CLI is signed out. Run “gh auth login”, then press Try again.");

    public static string ErrTokenRejected => Loc.T("GitHub no longer accepts the token. Connect again in Settings.");

    public static string ErrRateLimited(string time) => Loc.F("GitHub asked to wait until {0}.", time);

    public static string ErrGhMissing => Loc.T("The GitHub CLI is no longer installed.");

    public static string ErrUnexpected(string time) => Loc.F("Could not update. Trying again at {0}.", time);

    // notices
    public static string NoticeChecks => Loc.T("Checks failed");

    public static string NoticeReview => Loc.T("Review requested");

    public static string NoticeNew => Loc.T("New on GitHub");

    public static string NewNotifications(int n) => n == 1 ? Loc.T("1 new notification") : Loc.F("{0} new notifications", n);

    // settings
    public static string SettingsNotConnected => Loc.T("Not connected");

    public static string SettingsNotConnectedCaption =>
        Loc.T("Nothing is sent to GitHub until you connect. Trayloom then asks every few minutes for your notifications, pull requests, checks and releases. It never changes anything on GitHub.");

    public static string ConnectCli => Loc.T("Connect with GitHub CLI");

    public static string Connecting => Loc.T("Connecting…");

    public static string UseToken => Loc.T("Use a token instead…");

    public static string CliFound(string version) => Loc.F("GitHub CLI {0} found.", version);

    public static string CliMissing => Loc.T("GitHub CLI not found. Install it from cli.github.com, run “gh auth login” in a terminal, then come back.");

    public static string CliSite => Loc.T("cli.github.com");

    public static string FailSignedOut => Loc.T("The GitHub CLI is not signed in. Run “gh auth login” in a terminal, then try again.");

    public static string FailTimeout => Loc.T("The GitHub CLI did not answer in time.");

    public static string FailStart => Loc.T("The GitHub CLI could not be started.");

    public static string ConnectedAs(string login) => Loc.F("Connected as @{0}", login);

    public static string ThroughCli => Loc.T("Through the GitHub CLI. Sign out there with “gh auth logout”.");

    public static string WithToken => Loc.T("With a personal access token, encrypted for your Windows account. You can revoke it on GitHub at any time.");

    public static string Disconnect => Loc.T("Disconnect");

    public static string Updated(string relative) => Loc.F("Updated {0}", relative);

    public static string RefreshNow => Loc.T("Refresh now");

    public static string CheckEvery => Loc.T("Check every");

    public static string Minutes(int m) => Loc.F("{0} minutes", m);

    public static string Repositories => Loc.T("Repositories");

    public static string RepositoriesCaption => Loc.T("Checks and releases of these repositories. Leave empty for the 5 you pushed to most recently.");

    public static string ReposPlaceholder => Loc.T("owner/name, owner/name");

    public static string ReposInvalid => Loc.T("Use owner/name, separated by commas (up to 10).");

    public static string ShowAlerts => Loc.T("Show alerts in the island");

    public static string ShowAlertsCaption => Loc.T("New notifications, review requests and failed checks appear in the closed island for a moment.");

    // token dialog
    public static string TokenTitle => Loc.T("Connect with a token");

    public static string TokenIntro => Loc.T("Paste a GitHub personal access token. Trayloom encrypts it for your Windows account and never shows it again.");

    public static string TokenScopes =>
        Loc.T("Use a classic token with the “notifications” and “repo” scopes. A fine-grained token works too, but GitHub does not let it read notifications.");

    public static string TokenCreate => Loc.T("Create a classic token on GitHub");

    public static string Cancel => Loc.T("Cancel");

    public static string Connect => Loc.T("Connect");

    public static string Checking => Loc.T("Checking…");

    public static string TokenRejected => Loc.T("GitHub did not accept this token.");

    public static string TokenOffline => Loc.T("Could not reach GitHub. Check your connection and try again.");
}
