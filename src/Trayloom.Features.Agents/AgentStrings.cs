// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.Agents;

/// <summary>English strings of the AI Agents extension (spec 03 A9, Windows wording).</summary>
public static class AgentStrings
{
    public static  string Title => Loc.T("AI Agents");
    public static  string HubDescription => Loc.T("Follow plan limits, tokens, API value and the work in progress of Claude, Codex, OpenCode and GitHub Copilot in the Dynamic Island.");
    public static  string Summary => Loc.T("Claude Code, Codex, OpenCode and GitHub Copilot usage, limits and costs.");
    public static  string SettingsDescription => Loc.T("Reads the usage Claude Code, Codex, OpenCode and GitHub Copilot record on this PC and the plan limits the Claude app saves. Prompts, replies and files are never kept, and your usage never leaves your PC.");
    public static  string RestingTitle => Loc.T("AI limits");

    public static  string LimitsCard => Loc.T("Limits");
    public static  string SpendCard => Loc.T("Spending");
    public static  string LiveCard => Loc.T("Now");
    public static  string TrendCard => Loc.T("Trend");
    public static  string ModelsCard => Loc.T("Models");
    public static  string ProjectsCard => Loc.T("Projects");
    public static  string ActivityCard => Loc.T("Activity");
    public static  string ResetsCard => Loc.T("Resets");

    public static  string Today => Loc.T("Today");
    public static  string Week7 => Loc.T("7 days");
    public static  string Month30 => Loc.T("30 days");
    public static  string Session => Loc.T("Session");
    public static  string Weekly => Loc.T("Week");
    public static  string SessionLimit => Loc.T("5-hour limit");
    public static  string WeeklyLimit => Loc.T("Weekly limit");
    public static  string FocusSession => Loc.T("5-hour");
    public static  string FocusWeekly => Loc.T("Weekly");
    public static  string NoReading => Loc.T("No reading yet");
    public static  string NoReadingNote => Loc.T("Not reported by Claude yet");
    public static  string LimitReached => Loc.T("Limit reached");
    public static  string StartsWithMessage => Loc.T("Starts with your next message");
    public static  string StartsWithTask => Loc.T("Starts with your next task");
    public static  string Estimated => Loc.T("Estimated from this PC");
    public static  string ClaudeLimitsHint => Loc.T("Set up plan limits…");
    public static  string WaitingForLimits => Loc.T("Limits appear after the next reply");
    public static  string NoSession => Loc.T("No session running");
    public static  string ApiValue => Loc.T("API value");
    public static  string ApiEquivalent => Loc.T("API-equivalent value");
    public static  string NotWhatYouPay => Loc.T("not what you pay");
    public static  string Idle => Loc.T("Idle");
    public static  string NoActivity => Loc.T("Nothing in this period");
    public static  string Empty => Loc.T("No usage from Claude Code, Codex, OpenCode or GitHub Copilot yet. It appears here as soon as any of them works on this PC.");
    public static  string Loading => Loc.T("Reading usage…");
    public static  string NoCards => Loc.T("Choose what this page shows in Dynamic Island settings.");
    public static  string Unpriced => Loc.T("Some models have no known price, so this is a minimum.");
    public static  string Streak => Loc.T("Days in a row");
    public static  string ActiveDays => Loc.T("Active days");
    public static  string BusiestDay => Loc.T("Busiest day");
    public static  string LimitRenewed => Loc.T("Limit renewed");
    public static  string BudgetTitle => Loc.T("Daily budget");
    public static  string Agents => Loc.T("Agents");
    public static  string Found => Loc.T("Found on this PC");
    public static  string NotFound => Loc.T("Not found on this PC");
    public static  string CardsTitle => Loc.T("Cards");
    public static  string CardsHint => Loc.T("Drag to reorder. Charts take the full width of the island.");
    public static  string LimitsAs => Loc.T("Show limits as");
    public static  string Remaining => Loc.T("Left");
    public static  string Used => Loc.T("Used");
    public static  string LimitFocus => Loc.T("Limit to show");
    public static  string MostUsed => Loc.T("Most used");
    public static  string LiveTitle => Loc.T("While an agent works");
    public static  string LiveActivity => Loc.T("Show it in the closed Dynamic Island");
    public static  string Readout => Loc.T("Beside the island centre");
    public static  string ReadoutElapsed => Loc.T("Time");
    public static  string ReadoutTokens => Loc.T("Tokens written");
    public static  string ReadoutCost => Loc.T("API value");
    public static  string ReadoutLimit => Loc.T("Limit");
    public static  string ReadoutTimeLimit => Loc.T("Time and limit");
    public static  string Alerts => Loc.T("Alerts");
    public static  string FinishAlert => Loc.T("When a task finishes");
    public static  string FinishAfter => Loc.T("For tasks longer than");
    public static  string AnyLength => Loc.T("Any length");
    public static  string LimitAlert => Loc.T("Near a plan limit");
    public static  string LimitAt => Loc.T("Warn at");
    public static  string ForecastAlert => Loc.T("Before a limit runs out");
    public static  string ForecastAlertHint => Loc.T("Warns when, at your pace so far, a limit would run out before it resets.");
    public static  string ForecastLead => Loc.T("Warn");
    public static  string MinutesBefore(int minutes) => Loc.F("{0} min before", minutes);
    public static  string RunsOutAround(string time) => Loc.F("Runs out around {0}", time);
    public static  string RunsOutPace(string time) => Loc.F("Runs out around {0} at this pace", time);
    public static  string Budget => Loc.T("Daily API value budget");
    public static  string Off => Loc.T("Off");
    public static  string ClaudeLimitsTitle => Loc.T("Claude plan limits");
    public static  string ClaudeLimitsMenuBar => Loc.T("Claude checks your limits only while its icon is in the notification area (system tray). Turn the icon on in the Claude app settings, and your limits appear here within minutes.");
    public static  string ClaudeLimitsNoApp => Loc.T("Plan limits come from Claude Code or from the Claude app, which is not on this PC. Until then, the 5-hour session is estimated from Claude Code activity.");
    public static  string ClaudeLimitsPrivacy => Loc.T("Claude Code checks your account’s limits, including use on other machines, with its own sign-in, which Trayloom never reads. No prompt is sent. When it cannot check, the percentages the Claude app saves on this PC are used.");
    public static  string ClaudeCodeMissing => Loc.T("Claude Code was not found on this PC.");
    public static  string ClaudeCodeSignedOut => Loc.T("Sign in to Claude Code so it can check your account’s limits.");
    public static  string ClaudeCodeOutdated => Loc.T("Update Claude Code so it can check your account’s limits.");
    public static  string ClaudeCodeUnreachable => Loc.T("Claude Code could not check your account’s limits. It tries again later.");
    public static  string OpenClaude => Loc.T("Open Claude");
    public static  string GetClaude => Loc.T("Get the Claude app");
    public static  string PriceUpdates => Loc.T("Keep prices up to date");
    public static  string PriceUpdatesHint => Loc.T("Downloads the public price list from the Trayloom project once a day, so new models get an API value without an app update. Nothing about your usage is sent.");
    public static  string ValueNote => Loc.T("API value is what the same work would cost at API list prices. Plans charge a fixed price instead.");
    public static  string UseReset => Loc.T("Use a reset");
    public static  string ResetConfirm => Loc.T("Reset the session and weekly limits now?");
    public static  string ConfirmReset => Loc.T("Reset");
    public static  string Resetting => Loc.T("Resetting…");
    public static  string ResetDone => Loc.T("Limits reset");
    public static  string ResetNotNeeded => Loc.T("Your usage doesn’t need a reset yet");
    public static  string ResetTaken => Loc.T("That reset was already used");
    public static  string ResetFailed => Loc.T("Couldn’t use the reset");
    public static  string ResetsNone => Loc.T("No resets available");
    public static  string ResetsNeedCodex => Loc.T("Needs the Codex app or CLI");
    public static  string ResetsSignIn => Loc.T("Sign in to Codex with a plan to see resets");
    public static  string ResetsUpdate => Loc.T("Update Codex to use resets here");
    public static  string ResetsCheckFailed => Loc.T("Couldn’t check resets");
    public static  string ResetsHelp => Loc.T("A reset renews the Codex session and weekly limits at once. Codex checks your resets with its own sign-in, which Trayloom never reads.");
    public static  string Cancel => Loc.T("Cancel");
    public static  string Retry => Loc.T("Retry");
    public static  string MoveUp => Loc.T("Move up");
    public static  string MoveDown => Loc.T("Move down");
    public static  string CheckLimits => Loc.T("Check limits");

    // Claude account
    public static  string ClaudeAccountTitle => Loc.T("Claude account");
    public static  string DefaultAccount => Loc.T("Default account");
    public static  string ChooseAccountFolder => Loc.T("Choose account folder…");
    public static  string ClaudeAccountExplanation => Loc.T("Choose a profile already signed in to Claude Code. Usage comes from that profile’s sessions, and Claude Code checks its account’s limits. Changing this selection does not change the account in your other apps.");
    public static  string ClaudeInvalidFolder => Loc.T("Choose an existing Claude Code profile folder that belongs to you and contains its own .claude.json file. The folder must not be writable by others, and the file must not be a link.");
    public static  string UnknownAccount => Loc.T("No account identified in this profile.");
    public static  string NoLimitsYet => Loc.T("Claude Code has not returned limits for this account yet.");
    public static  string SignInHelp => Loc.T("How to sign in to another Claude account");
    public const string SignInHelpUrl = "https://code.claude.com/docs/en/authentication#log-in-with-multiple-accounts";
    public const string GetClaudeUrl = "https://claude.ai/download";

    // Codex accounts
    public static  string CodexAccountsTitle => Loc.T("Codex accounts");
    public static  string CodexAccountsCaption => Loc.T("Each CODEX_HOME folder you add gets its own limits card. While the AI Agents page is open, Codex checks each one about every five minutes with that folder’s own sign-in. No prompt is sent.");
    public static  string AccountName => Loc.T("Account name");
    public static  string RemoveAccount => Loc.T("Remove this account’s card");
    public static  string FindAccounts => Loc.T("Find local accounts");
    public static  string AddFolder => Loc.T("Add folder…");
    public static  string CodexFallback => Loc.T("With no accounts added, Codex limits come from its local sessions.");
    public static  string CodexResetsNote => Loc.T("The Resets card still belongs to the default Codex account.");
    public static  string ChooseCodexFolder => Loc.T("Choose a Codex folder");
    public static  string CodexInvalidFolder => Loc.T("Choose a separate CODEX_HOME folder with config.toml or auth.json in it. The folder must be yours and not writable by others, and auth.json must be private and not a link. Codex may refresh its own sign-in and caches there.");
    public static  string FolderProblem => Loc.T("Choose this account’s folder again in Settings.");
    public static  string CodexDefaultName => Loc.T("Default");
    public static  string RefreshHint => Loc.T("Refresh to check this account’s limits.");
    public static  string CodexSignIn => Loc.T("Sign in with ChatGPT in this Codex folder.");
    public static  string CodexUpdate => Loc.T("Update Codex to check limits.");
    public static  string CodexFailed => Loc.T("Could not check limits. Try again.");
    public static  string Ok => Loc.T("OK");

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
