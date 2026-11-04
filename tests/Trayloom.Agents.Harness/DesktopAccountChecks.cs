// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Text;
using Trayloom.Core;
using Trayloom.Features.Agents;

namespace Trayloom.Agents.Harness;

/// <summary>
/// The Claude card follows the Claude desktop app's account (<see cref="ClaudeAppAccount"/>), never a terminal login for another account.
/// Everything is built in a temp directory from synthetic ids that keep the relations of real ones; nothing real is read or written.
/// </summary>
internal static partial class Program
{
    // The desktop and the matching terminal login share the first pair, another terminal login has the second pair, a switched account the third.
    private const string DesktopAccountId = "11111111-1111-4111-8111-111111111111";
    private const string DesktopOrg = "22222222-2222-4222-8222-222222222222";
    private const string OtherAccount = "33333333-3333-4333-8333-333333333333";
    private const string OtherOrg = "44444444-4444-4444-8444-444444444444";
    private const string SwitchedOrg = "66666666-6666-4666-8666-666666666666";

    private static string Cli(string account, string org, string email, string type, string tier) =>
        "{\"oauthAccount\":{\"accountUuid\":\"" + account + "\",\"organizationUuid\":\"" + org + "\",\"emailAddress\":\"" + email + "\",\"organizationType\":\"" + type +
        "\",\"organizationRateLimitTier\":\"" + tier + "\",\"userRateLimitTier\":null}}";

    private static long Ms(DateTime t) => (long)(t - AgentTime.Epoch).TotalMilliseconds;

    /// <summary>An hourly history of one organization whose weekly figure climbs to 40% with no drop (so no renewal is inferred).</summary>
    private static string SyntheticHistory(string org, DateTime last)
    {
        var sb = new StringBuilder("{\"version\":2,\"samples\":[");
        for (var i = 0; i < 41; i++)
        {
            sb.Append(i == 0 ? "" : ",").Append("{\"t\":").Append(Ms(last.AddHours(i - 40))).Append(",\"org\":\"").Append(org).Append("\",\"u\":{\"sd\":").Append(i).Append("}}");
        }

        return sb.Append("]}").ToString();
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    private static string WriteTemp(string root, string name, string content)
    {
        var path = Path.Combine(root, name);
        Write(path, content);
        return path;
    }

    private static void DesktopAccount(string? historyPath)
    {
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", null);
        var root = Path.Combine(Path.GetTempPath(), "trayloom-agents-harness-" + Guid.NewGuid().ToString("N"));
        try
        {
            RunDesktopAccount(root, historyPath);
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static void RunDesktopAccount(string root, string? historyPath)
    {
        var local = Path.Combine(root, "local");
        var roaming = Path.Combine(root, "roaming");
        var empty = Path.Combine(root, "empty");
        Directory.CreateDirectory(empty);
        var newest = DateTime.UtcNow.AddHours(-3);
        var history = historyPath is not null && File.Exists(historyPath) ? File.ReadAllText(historyPath) : SyntheticHistory(DesktopOrg, newest);
        var folder = Path.Combine(local, "Claude-Data", "claude_testpkg");
        Write(Path.Combine(folder, "config.json"),
            "{\"updaterLastSeenVersion\":\"4.0.0\",\"lastKnownAccountUuid\":\"" + DesktopAccountId + "\",\"oauth:tokenCache\":\"DUMMY-NOT-A-TOKEN\",\"oauth:tokenCacheV2\":\"DUMMY-NOT-A-TOKEN\"," +
            "\"nested\":{\"lastKnownAccountUuid\":\"" + OtherAccount + "\",\"list\":[1,2,{\"x\":\"y\"}]}}");
        Write(Path.Combine(folder, "plan-usage-history.json"), history);
        Write(Path.Combine(folder, "lockfile"), "");
        Directory.CreateDirectory(Path.Combine(folder, "claude-code-sessions", DesktopAccountId, DesktopOrg));
        Directory.CreateDirectory(Path.Combine(folder, "claude-code-sessions", OtherAccount, OtherOrg));
        Directory.CreateDirectory(Path.Combine(folder, "local-agent-mode-sessions", DesktopAccountId, DesktopOrg));
        Directory.CreateDirectory(Path.Combine(folder, "scratch-workspaces", DesktopAccountId, "not-a-guid"));
        var samples = ClaudeAppUsage.ReadFile(Path.Combine(folder, "plan-usage-history.json"))!;
        var org = samples[^1].Organization ?? DesktopOrg;
        var home = Path.Combine(root, "home");
        Directory.CreateDirectory(home);

        // 1. the folder, the account, the organizations - and nothing else from config.json
        var app = ClaudeAppAccount.Locate(local, empty);
        Check(app is not null && app.Folder == folder && app.HistoryFile == Path.Combine(folder, "plan-usage-history.json"), "Locate picks the fixture data folder");
        Check(app?.AccountUuid == DesktopAccountId, "AccountUuid is the top-level lastKnownAccountUuid (not the nested decoy)");
        Check(app is { AccountOrganizations.Count: 1 } && app.AccountOrganizations[0] == DesktopOrg, "AccountOrganizations holds the desktop account's organization only (non-GUID folders and other accounts left out)");
        Check(app is not null && !(app.ToString() + app.Folder + app.HistoryFile + app.AccountUuid + string.Join(",", app.AccountOrganizations)).Contains("DUMMY", StringComparison.Ordinal),
            "no token value from config.json reaches the result");
        Write(Path.Combine(folder, "config.json"), "{\"lastKnownAccountUuid\":\"not-a-guid\"}");
        Check(ClaudeAppAccount.Locate(local, empty)?.AccountUuid is null, "a lastKnownAccountUuid that is not a GUID is refused");
        var big = new StringBuilder("{\"pad\":\"").Append('x', 70_000).Append("\",\"lastKnownAccountUuid\":\"" + DesktopAccountId + "\"}");
        Write(Path.Combine(folder, "config.json"), big.ToString());
        Check(ClaudeAppAccount.Locate(local, empty)?.AccountUuid == DesktopAccountId, "the streaming reader finds the id after a value larger than its buffer");
        Write(Path.Combine(folder, "config.json"), "{\"lastKnownAccountUuid\":\"" + DesktopAccountId + "\"}");

        // 2. freshness
        var old = Path.Combine(local, "Claude-Data", "claude_old");
        Write(Path.Combine(old, "plan-usage-history.json"), history);
        File.SetLastWriteTimeUtc(Path.Combine(old, "plan-usage-history.json"), DateTime.UtcNow.AddDays(-1));
        Check(ClaudeAppAccount.Locate(local, empty)?.Folder == folder, "an older data folder is not picked");
        var squirrel = Path.Combine(roaming, "Claude");
        Write(Path.Combine(squirrel, "plan-usage-history.json"), history);
        File.SetLastWriteTimeUtc(Path.Combine(squirrel, "plan-usage-history.json"), DateTime.UtcNow.AddDays(1));
        Check(ClaudeAppAccount.Locate(local, roaming)?.Folder == squirrel, "a newer roaming Claude history is picked");
        File.SetLastWriteTimeUtc(Path.Combine(squirrel, "plan-usage-history.json"), DateTime.UtcNow.AddDays(-2));
        Check(ClaudeAppAccount.Locate(local, roaming)?.Folder == folder, "an older roaming Claude history is not picked");
        File.SetLastWriteTimeUtc(Path.Combine(squirrel, "plan-usage-history.json"), DateTime.UtcNow.AddDays(1));
        Write(Path.Combine(squirrel, ".user-data-left.claude_testpkg"), "");
        Check(ClaudeAppAccount.Locate(local, roaming)?.Folder == folder, "the user-data-left marker prefers Claude-Data over a newer roaming copy");

        // 3. a terminal login for another account lends nothing
        File.WriteAllText(Path.Combine(home, ".claude.json"), Cli(OtherAccount, OtherOrg, "cli-user@example.invalid", "claude_pro", "default_claude_ai"));
        var different = ClaudeAppAccount.Resolve(app, samples, home);
        Check(different.Identity?.Email is null && different.Plan is null && !different.MatchedClaudeCode && different.Organization == org,
            "other Claude Code account: no email, no plan, the desktop's organization");
        Check(AgentCardViews.ClaudeAppName(new AgentServiceStatus { ClaudeAccountIdentity = different.Identity }) == "Claude app", "other Claude Code account: the account line reads just \"Claude app\"");

        // 4. the same account lends its email and plan
        File.WriteAllText(Path.Combine(home, ".claude.json"), Cli(DesktopAccountId, DesktopOrg, "desktop-user@example.invalid", "claude_max", "default_claude_max_20x"));
        var same = ClaudeAppAccount.Resolve(app, samples, home);
        Check(same.MatchedClaudeCode && same.Identity?.Email == "desktop-user@example.invalid" && same.Plan?.Name is { } planName && planName.StartsWith("Max", StringComparison.Ordinal), "same account: email and a Max plan, named by the price list (got " + (same.Plan?.Name ?? "none") + ")");
        Check(AgentCardViews.ClaudeAppName(new AgentServiceStatus { ClaudeAccountIdentity = same.Identity }) == "Claude app · desktop-user@example.invalid", "same account: the account line adds the email");
        File.WriteAllText(Path.Combine(home, ".claude.json"), Cli(DesktopAccountId, OtherOrg, "desktop-user@example.invalid", "claude_pro", "default_claude_ai"));
        Check(!ClaudeAppAccount.Resolve(app, samples, home).MatchedClaudeCode, "same account but another organization than the app's: not borrowed");

        // 5. the weekly row with no plan
        var last = samples[^1];
        var at = last.Date.AddMinutes(14);
        var limits = ClaudeAppUsage.Limits(samples, at, ClaudeAppUsage.SessionStart([], samples, org), org);
        var weekly = limits?.Windows.FirstOrDefault(w => w.Kind == LimitKind.Weekly && w.Scope is null);
        Check(weekly is not null, "desktop reading has a weekly window (" + (weekly is null ? "none" : weekly.UsedPercent.ToString("0.#", CultureInfo.InvariantCulture) + "% used") + ")");
        if (limits is not null && weekly is not null)
        {
            var snapshot = new AgentUsageSnapshot { Loaded = true, Now = at };
            snapshot.Limits[AgentProvider.Claude] = limits;
            var part = AgentSettings.LimitPart(snapshot, AgentProvider.Claude, false, AgentLimitFocus.Weekly, at);
            var expected = "Wk " + AgentFormat.Percent(1 - weekly.UsedPercent / 100) + " left";
            Check(part?.Text == expected, "strip reads \"" + expected + "\" (got \"" + part?.Text + "\")");
            var rows = LimitPresenter.Rows(AgentProvider.Claude, limits, at, false, null);
            Check(rows.Any(r => r.Label == AgentStrings.WeeklyLimit) && rows.All(r => r.Label != AgentStrings.SessionLimit || last.Used.ContainsKey("fh")),
                "card rows without a plan: weekly row present, no invented 5-hour row");
            Check(LimitPresenter.SourceLine(limits, at).Text.StartsWith("From the Claude app", StringComparison.Ordinal), "source line starts \"From the Claude app\"");
        }

        // 6. the account switched: the newest sample belongs to an organization the account does not have
        var switched = ClaudeAppUsage.ReadFile(WriteTemp(root, "switched.json", SyntheticHistory(SwitchedOrg, newest)))!;
        var moved = ClaudeAppAccount.Resolve(app, switched, home);
        Check(moved.OrgMismatch && moved.Organization == SwitchedOrg, "newest sample of another organization => OrgMismatch (the service then reads no limits)");
        Check(!ClaudeAppAccount.Resolve(app, samples, home).OrgMismatch, "newest sample of the account's organization => no mismatch");

        // 7. Claude Code included: the history is filtered by Claude Code's organization
        Check(ClaudeAppUsage.Limits(samples, at, null, OtherOrg) is null, "old mode: another organization's filter leaves no desktop reading");

        // 8. a torn read
        var torn = WriteTemp(root, "torn.json", history[..(history.Length / 2)]);
        Check(ClaudeAppUsage.ReadFile(torn) is null, "half a history file reads as null (the service keeps its last samples and retries)");

        // 9. a week that renewed since the last sample keeps its row
        var start = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var sb = new StringBuilder("{\"version\":2,\"samples\":[");
        var first = true;
        const int hours = 9 * 24 + 12;
        for (var h = 0; h <= hours; h++)
        {
            var sd = h <= 72 ? h * 60 / 72 : 2 + (h - 73) * 28 / (hours - 73);
            sb.Append(first ? "" : ",").Append("{\"t\":").Append(Ms(start.AddHours(h))).Append(",\"org\":\"").Append(DesktopOrg).Append("\",\"u\":{\"sd\":").Append(sd).Append("}}");
            first = false;
        }

        var renewedSamples = ClaudeAppUsage.ReadFile(WriteTemp(root, "renewed.json", sb.Append("]}").ToString()))!;
        var later = renewedSamples[^1].Date.AddHours(14);
        var kept = ClaudeAppUsage.Limits(renewedSamples, later, null, DesktopOrg);
        var week = kept?.Windows.FirstOrDefault(w => w.Kind == LimitKind.Weekly && w.Scope is null);
        Check(week is { ResetsAt: { } r } && r <= later, "weekly window whose inferred renewal has passed is kept with that time");
        if (kept is not null && week is not null)
        {
            Check(AgentLimitSupport.Current(week, later).Renewed, "...and reads as renewed");
            var renewedRows = LimitPresenter.Rows(AgentProvider.Claude, kept, later, false, null);
            Check(renewedRows.Any(r => r.Label == AgentStrings.WeeklyLimit && r.Value == Loc.T("Renewed")), "...and its row says \"Renewed\"");
            var snapshot = new AgentUsageSnapshot { Loaded = true, Now = later };
            snapshot.Limits[AgentProvider.Claude] = kept;
            Check(AgentSettings.LimitPart(snapshot, AgentProvider.Claude, false, AgentLimitFocus.Weekly, later) is null, "...and the closed island shows nothing for it");
        }

        Check(ClaudeAppUsage.Limits(renewedSamples, renewedSamples[^1].Date.AddDays(8), null, DesktopOrg) is null, "a week-old reading is still dropped");
    }
}
