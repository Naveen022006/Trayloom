// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace WinKnife.Features.Agents;

/// <summary>English strings of the AI Agents extension (spec 03 A9, Windows wording).</summary>
public static class AgentStrings
{
    public const string Title = "AI Agents";
    public const string HubDescription = "Follow plan limits, tokens, API value and the work in progress of Claude, Codex, OpenCode and GitHub Copilot in the Dynamic Island.";
    public const string Summary = "Claude Code, Codex, OpenCode and GitHub Copilot usage, limits and costs.";
    public const string SettingsDescription = "Reads the usage Claude Code, Codex, OpenCode and GitHub Copilot record on this PC and the plan limits the Claude app saves. Prompts, replies and files are never kept, and your usage never leaves your PC.";
    public const string RestingTitle = "AI limits";

    public const string LimitsCard = "Limits";
    public const string SpendCard = "Spending";
    public const string LiveCard = "Now";
    public const string TrendCard = "Trend";
    public const string ModelsCard = "Models";
    public const string ProjectsCard = "Projects";
    public const string ActivityCard = "Activity";
    public const string ResetsCard = "Resets";

    public const string Today = "Today";
    public const string Week7 = "7 days";
    public const string Month30 = "30 days";
    public const string Session = "Session";
    public const string Weekly = "Week";
    public const string Estimated = "Estimated from this PC";
    public const string ClaudeLimitsHint = "Set up plan limits…";
    public const string WaitingForLimits = "Limits appear after the next reply";
    public const string NoSession = "No session running";
    public const string ApiValue = "API value";
    public const string ApiEquivalent = "API-equivalent value";
    public const string NotWhatYouPay = "not what you pay";
    public const string Idle = "Idle";
    public const string NoActivity = "Nothing in this period";
    public const string Empty = "No usage from Claude Code, Codex, OpenCode or GitHub Copilot yet. It appears here as soon as any of them works on this PC.";
    public const string Loading = "Reading usage…";
    public const string NoCards = "Choose what this page shows in Dynamic Island settings.";
    public const string Unpriced = "Some models have no known price, so this is a minimum.";
    public const string Streak = "Days in a row";
    public const string ActiveDays = "Active days";
    public const string BusiestDay = "Busiest day";
    public const string LimitRenewed = "Limit renewed";
    public const string BudgetTitle = "Daily budget";
    public const string Agents = "Agents";
    public const string Found = "Found on this PC";
    public const string NotFound = "Not found on this PC";
    public const string CardsTitle = "Cards";
    public const string CardsHint = "Drag to reorder. Charts take the full width of the island.";
    public const string LimitsAs = "Show limits as";
    public const string Remaining = "Left";
    public const string Used = "Used";
    public const string LimitFocus = "Limit to show";
    public const string MostUsed = "Most used";
    public const string LiveTitle = "While an agent works";
    public const string LiveActivity = "Show it in the closed Dynamic Island";
    public const string Readout = "Beside the island centre";
    public const string ReadoutElapsed = "Time";
    public const string ReadoutTokens = "Tokens written";
    public const string ReadoutCost = "API value";
    public const string ReadoutLimit = "Limit";
    public const string Alerts = "Alerts";
    public const string FinishAlert = "When a task finishes";
    public const string FinishAfter = "For tasks longer than";
    public const string AnyLength = "Any length";
    public const string LimitAlert = "Near a plan limit";
    public const string LimitAt = "Warn at";
    public const string Budget = "Daily API value budget";
    public const string Off = "Off";
    public const string ClaudeLimitsTitle = "Claude plan limits";
    public const string ClaudeLimitsMenuBar = "Claude checks your limits only while its icon is in the notification area (system tray). Turn the icon on in the Claude app settings, and your limits appear here within minutes.";
    public const string ClaudeLimitsNoApp = "Plan limits come from Claude Code or from the Claude app, which is not on this PC. Until then, the 5-hour session is estimated from Claude Code activity.";
    public const string ClaudeLimitsPrivacy = "Claude Code checks your account’s limits, including use on other machines, with its own sign-in, which WinKnife never reads. No prompt is sent. When it cannot check, the percentages the Claude app saves on this PC are used.";
    public const string ClaudeCodeMissing = "Claude Code was not found on this PC.";
    public const string ClaudeCodeSignedOut = "Sign in to Claude Code so it can check your account’s limits.";
    public const string ClaudeCodeOutdated = "Update Claude Code so it can check your account’s limits.";
    public const string ClaudeCodeUnreachable = "Claude Code could not check your account’s limits. It tries again later.";
    public const string OpenClaude = "Open Claude";
    public const string GetClaude = "Get the Claude app";
    public const string PriceUpdates = "Keep prices up to date";
    public const string PriceUpdatesHint = "Downloads the public price list from the WinKnife project once a day, so new models get an API value without an app update. Nothing about your usage is sent.";
    public const string ValueNote = "API value is what the same work would cost at API list prices. Plans charge a fixed price instead.";
    public const string UseReset = "Use a reset";
    public const string ResetConfirm = "Reset the session and weekly limits now?";
    public const string ConfirmReset = "Reset";
    public const string Resetting = "Resetting…";
    public const string ResetDone = "Limits reset";
    public const string ResetNotNeeded = "Your usage doesn’t need a reset yet";
    public const string ResetTaken = "That reset was already used";
    public const string ResetFailed = "Couldn’t use the reset";
    public const string ResetsNone = "No resets available";
    public const string ResetsNeedCodex = "Needs the Codex app or CLI";
    public const string ResetsSignIn = "Sign in to Codex with a plan to see resets";
    public const string ResetsUpdate = "Update Codex to use resets here";
    public const string ResetsCheckFailed = "Couldn’t check resets";
    public const string ResetsHelp = "A reset renews the Codex session and weekly limits at once. Codex checks your resets with its own sign-in, which WinKnife never reads.";
    public const string Cancel = "Cancel";
    public const string Retry = "Retry";
    public const string MoveUp = "Move up";
    public const string MoveDown = "Move down";
    public const string CheckLimits = "Check limits";

    // Claude account
    public const string ClaudeAccountTitle = "Claude account";
    public const string DefaultAccount = "Default account";
    public const string ChooseAccountFolder = "Choose account folder…";
    public const string ClaudeAccountExplanation = "Choose a profile already signed in to Claude Code. Usage comes from that profile’s sessions, and Claude Code checks its account’s limits. Changing this selection does not change the account in your other apps.";
    public const string ClaudeInvalidFolder = "Choose an existing Claude Code profile folder that belongs to you and contains its own .claude.json file. The folder must not be writable by others, and the file must not be a link.";
    public const string UnknownAccount = "No account identified in this profile.";
    public const string NoLimitsYet = "Claude Code has not returned limits for this account yet.";
    public const string SignInHelp = "How to sign in to another Claude account";
    public const string SignInHelpUrl = "https://code.claude.com/docs/en/authentication#log-in-with-multiple-accounts";
    public const string GetClaudeUrl = "https://claude.ai/download";

    // Codex accounts
    public const string CodexAccountsTitle = "Codex accounts";
    public const string CodexAccountsCaption = "Each CODEX_HOME folder you add gets its own limits card. While the AI Agents page is open, Codex checks each one about every five minutes with that folder’s own sign-in. No prompt is sent.";
    public const string AccountName = "Account name";
    public const string RemoveAccount = "Remove this account’s card";
    public const string FindAccounts = "Find local accounts";
    public const string AddFolder = "Add folder…";
    public const string CodexFallback = "With no accounts added, Codex limits come from its local sessions.";
    public const string CodexResetsNote = "The Resets card still belongs to the default Codex account.";
    public const string ChooseCodexFolder = "Choose a Codex folder";
    public const string CodexInvalidFolder = "Choose a separate CODEX_HOME folder with config.toml or auth.json in it. The folder must be yours and not writable by others, and auth.json must be private and not a link. Codex may refresh its own sign-in and caches there.";
    public const string FolderProblem = "Choose this account’s folder again in Settings.";
    public const string CodexDefaultName = "Default";
    public const string RefreshHint = "Refresh to check this account’s limits.";
    public const string CodexSignIn = "Sign in with ChatGPT in this Codex folder.";
    public const string CodexUpdate = "Update Codex to check limits.";
    public const string CodexFailed = "Could not check limits. Try again.";
    public const string Ok = "OK";

    public static string Finished(string provider) => provider + " finished";
    public static string Left(string v) => v + " left";
    public static string UsedShare(string v) => v + " used";
    public static string Tokens(string v) => v + " tokens";
    public static string Cached(string v) => v + " from cache";
    public static string Written(string v) => v + " written";
    public static string Saved(string v) => v + " saved by the cache";
    public static string PlanMultiple(string multiple, string plan) => multiple + " the price of " + plan;
    public static string Updated(string v) => "Updated " + v;
    public static string ClaudeLimitsCurrent(string v) => "Read from the Claude app, updated " + v;
    public static string ClaudeLimitsStale(string v) => "The Claude app last checked them " + v + ".";
    public static string PricesFrom(string v) => "Price list of " + v;
    public static string ResetsExpiry(string v) => "Next expires " + v;

    /// <summary>The span a spending figure covers, in words that name the local day.</summary>
    public static string PeriodRange(AgentPeriod p) => p switch
    {
        AgentPeriod.Today => "Today · since midnight, your local time",
        AgentPeriod.Week => "Last 7 days, ending now",
        _ => "Last 30 days, ending now",
    };

    public static string PeriodHelp(AgentPeriod p) => p == AgentPeriod.Today
        ? "Counts everything since 12:00 AM on this PC's clock, not the last 24 hours."
        : "Counts whole local days, including today so far.";

    public static string Period(AgentPeriod p) => p switch { AgentPeriod.Today => Today, AgentPeriod.Week => Week7, _ => Month30 };

    public static string Card(AgentCard c) => c switch
    {
        AgentCard.Limits => LimitsCard,
        AgentCard.Spend => SpendCard,
        AgentCard.Live => LiveCard,
        AgentCard.Trend => TrendCard,
        AgentCard.Models => ModelsCard,
        AgentCard.Projects => ProjectsCard,
        AgentCard.Activity => ActivityCard,
        _ => ResetsCard,
    };

    public static string ClaudeCode(ClaudeFailure f) => f switch
    {
        ClaudeFailure.Missing => ClaudeCodeMissing,
        ClaudeFailure.NeedsSignIn => ClaudeCodeSignedOut,
        ClaudeFailure.Outdated => ClaudeCodeOutdated,
        _ => ClaudeCodeUnreachable,
    };
}
