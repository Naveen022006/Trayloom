// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;

namespace Trayloom.Features.Agents;

/// <summary>
/// The account the Claude desktop app is signed in to, found without a sign-in: the app's data folder, its saved plan history and the
/// one non-secret identity field of its <c>config.json</c> (<c>lastKnownAccountUuid</c>). Every other value of that file, tokens
/// included, is skipped while it streams past and never kept. Neither the account id nor an organization id is ever logged.
/// </summary>
/// <param name="Folder">The app's data folder (the one holding <c>plan-usage-history.json</c> and <c>config.json</c>).</param>
/// <param name="HistoryFile">The plan history file in that folder, when it exists.</param>
/// <param name="AccountUuid">The account the app was last signed in to; null when unknown.</param>
/// <param name="AccountOrganizations">The organizations that account has worked in on this PC (the app names its session folders after them).</param>
/// <param name="Freshness">The newest write among the folder's history, config and lock file.</param>
internal sealed record ClaudeAppAccount(string Folder, string? HistoryFile, string? AccountUuid, IReadOnlyList<string> AccountOrganizations, DateTime Freshness)
{
    private const string HistoryName = "plan-usage-history.json";
    private const string ConfigName = "config.json";
    private const string LockName = "lockfile";
    private const string LeftMarker = ".user-data-left.";
    private const int MaximumConfigSize = 4 << 20;
    private const int MaximumOrganizations = 32;

    private static readonly string[] SessionFolders = ["claude-code-sessions", "local-agent-mode-sessions", "scratch-workspaces"];

    /// <summary>One candidate data folder and the times that rank it.</summary>
    internal sealed record Pick(string Folder, string Name, bool ClaudeData, string? HistoryFile, DateTime? ConfigModified, DateTime? HistoryModified, DateTime Freshness, bool Marked);

    /// <summary>Never prints the account id: a record's text ends up in logs and diagnostics.</summary>
    public override string ToString() => "ClaudeAppAccount(" + Path.GetFileName(Folder.TrimEnd('\\', '/')) + ")";

    /// <summary>The app's current account, or null when no Claude app data folder with a history or a config is found.</summary>
    public static ClaudeAppAccount? Locate(string? localAppData = null, string? roamingAppData = null)
    {
        if (Choose(localAppData, roamingAppData) is not { } pick)
        {
            return null;
        }

        var account = ReadAccountUuid(Path.Combine(pick.Folder, ConfigName));
        return new ClaudeAppAccount(pick.Folder, pick.HistoryFile, account, account is null ? [] : Organizations(pick.Folder, account), pick.Freshness);
    }

    /// <summary>
    /// The folder the app uses now: every <c>Claude-Data\claude_*</c> (MSIX), <c>%APPDATA%\Claude</c> (Squirrel) and the virtualised copy
    /// under <c>Packages\Claude_*</c> are compared by their newest write; a tie goes to <c>Claude-Data</c>. The marker the MSIX app leaves in
    /// the roaming folder (<c>.user-data-left.&lt;name&gt;</c>) says its data moved to <c>Claude-Data\&lt;name&gt;</c>.
    /// </summary>
    internal static Pick? Choose(string? localAppData = null, string? roamingAppData = null)
    {
        var local = localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = roamingAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var picks = new List<Pick>();
        var markers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roamingClaude = Path.Combine(roaming, "Claude");
        try
        {
            if (Directory.Exists(roamingClaude))
            {
                foreach (var marker in Directory.EnumerateFiles(roamingClaude, LeftMarker + "*"))
                {
                    markers.Add(Path.GetFileName(marker)[LeftMarker.Length..]);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        Add(picks, Path.Combine(local, "Claude-Data"), "claude_*", true, markers);
        Add(picks, roamingClaude, null, false, markers);
        try
        {
            var packages = Path.Combine(local, "Packages");
            if (Directory.Exists(packages))
            {
                foreach (var package in Directory.EnumerateDirectories(packages, "Claude_*"))
                {
                    Add(picks, Path.Combine(package, "LocalCache", "Roaming", "Claude"), null, false, markers);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        return picks.OrderByDescending(p => p.Marked).ThenByDescending(p => p.Freshness).ThenByDescending(p => p.ClaudeData).FirstOrDefault();
    }

    /// <summary>Adds the folder itself, or each of its subfolders matching <paramref name="pattern"/>, when it holds a history or a config.</summary>
    private static void Add(List<Pick> picks, string folder, string? pattern, bool claudeData, HashSet<string> markers)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return;
            }

            if (pattern is null)
            {
                Consider(picks, folder, Path.GetFileName(folder), claudeData, markers);
                return;
            }

            foreach (var sub in Directory.EnumerateDirectories(folder, pattern))
            {
                Consider(picks, sub, Path.GetFileName(sub), claudeData, markers);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void Consider(List<Pick> picks, string folder, string name, bool claudeData, HashSet<string> markers)
    {
        try
        {
            var history = new FileInfo(Path.Combine(folder, HistoryName));
            var config = new FileInfo(Path.Combine(folder, ConfigName));
            var historyTime = history.Exists ? history.LastWriteTimeUtc : (DateTime?)null;
            var configTime = config.Exists ? config.LastWriteTimeUtc : (DateTime?)null;
            if (historyTime is null && configTime is null)
            {
                return;
            }

            var freshness = new[] { historyTime, configTime, Modified(Path.Combine(folder, LockName)) }.Where(t => t is not null).Max() ?? DateTime.MinValue;
            picks.Add(new Pick(folder, name, claudeData, history.Exists ? history.FullName : null, configTime, historyTime, freshness, claudeData && markers.Contains(name)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static DateTime? Modified(string file)
    {
        var info = new FileInfo(file);
        return info.Exists ? info.LastWriteTimeUtc : null;
    }

    /// <summary>
    /// <c>lastKnownAccountUuid</c> of <c>config.json</c>, read with a forward-only reader that skips every other value (the file also
    /// holds the app's sign-in tokens, which are never looked at). Only a string shaped like a GUID is accepted.
    /// </summary>
    internal static string? ReadAccountUuid(string configFile)
    {
        byte[]? buffer = null;
        try
        {
            using var stream = new FileStream(configFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > MaximumConfigSize)
            {
                return null;
            }

            buffer = new byte[16 * 1024];
            var filled = 0;
            var final = false;
            var inRoot = false;
            string? found = null;
            var state = new JsonReaderState(new JsonReaderOptions { MaxDepth = 256, AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            while (true)
            {
                if (!final)
                {
                    if (filled == buffer.Length)
                    {
                        if (buffer.Length >= MaximumConfigSize)
                        {
                            return null;
                        }

                        var larger = new byte[buffer.Length * 2];
                        Buffer.BlockCopy(buffer, 0, larger, 0, filled);
                        Array.Clear(buffer);
                        buffer = larger;
                    }

                    var read = stream.Read(buffer, filled, buffer.Length - filled);
                    if (read == 0)
                    {
                        final = true;
                    }
                    else
                    {
                        filled += read;
                    }
                }

                var reader = new Utf8JsonReader(buffer.AsSpan(0, filled), final, state);
                // The last position between two members: a member that does not fit in the buffer yet is read again from here.
                var safe = 0L;
                var safeState = state;
                var done = false;
                while (true)
                {
                    safe = reader.BytesConsumed;
                    safeState = reader.CurrentState;
                    if (!reader.Read())
                    {
                        break;
                    }

                    if (!inRoot)
                    {
                        if (reader.TokenType != JsonTokenType.StartObject)
                        {
                            return null;
                        }

                        inRoot = true;
                        continue;
                    }

                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        done = true;
                        break;
                    }

                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        return null;
                    }

                    var wanted = reader.ValueTextEquals("lastKnownAccountUuid"u8);
                    if (!reader.Read())
                    {
                        break;
                    }

                    if (wanted && reader.TokenType == JsonTokenType.String)
                    {
                        var value = reader.GetString();
                        found = value is { Length: <= 64 } && Guid.TryParse(value, out _) ? value : null;
                    }
                    else if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray && !reader.TrySkip())
                    {
                        break;
                    }
                }

                if (done || final)
                {
                    return found;
                }

                // Keep what was not consumed and ask for more.
                var keep = filled - (int)safe;
                Buffer.BlockCopy(buffer, (int)safe, buffer, 0, keep);
                filled = keep;
                state = safeState;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return null;
        }
        finally
        {
            if (buffer is not null)
            {
                Array.Clear(buffer);
            }
        }
    }

    /// <summary>The organizations the account has worked in: GUID-named folders under its folder in each of the app's session stores.</summary>
    private static List<string> Organizations(string folder, string account)
    {
        var result = new List<string>();
        foreach (var store in SessionFolders)
        {
            try
            {
                var accountFolder = Path.Combine(folder, store, account);
                if (!Directory.Exists(accountFolder))
                {
                    continue;
                }

                foreach (var org in Directory.EnumerateDirectories(accountFolder))
                {
                    var name = Path.GetFileName(org);
                    if (Guid.TryParse(name, out _) && !result.Contains(name, StringComparer.OrdinalIgnoreCase) && result.Count < MaximumOrganizations)
                    {
                        result.Add(name);
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
            }
        }

        return result;
    }

    /// <summary>
    /// Who the Claude app's card belongs to. The organization is the newest saved sample's. A Claude Code login lends its email and plan
    /// (the app saves neither) only when it is the same account; one that differs is never used. <c>OrgMismatch</c> means the newest
    /// sample belongs to an organization the app's account does not have: the app switched accounts and has not checked since.
    /// </summary>
    internal static (ClaudeAccountProfile.Identity? Identity, AgentPlan? Plan, string? Organization, bool OrgMismatch, bool MatchedClaudeCode) Resolve(
        ClaudeAppAccount? app, List<ClaudeAppUsage.Sample> samples, string home)
    {
        var org = samples.Count > 0 ? samples[^1].Organization : null;
        var mismatch = app is not null && app.AccountOrganizations.Count > 0 && org is not null
                       && !app.AccountOrganizations.Contains(org, StringComparer.OrdinalIgnoreCase);
        if (app is null)
        {
            return (org is null ? null : new ClaudeAccountProfile.Identity(null, null, org), null, org, false, false);
        }

        var profiles = new List<ClaudeAccountProfile> { new() };
        try
        {
            profiles.AddRange(ClaudeAccountProfile.Discover(home));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        foreach (var profile in profiles)
        {
            var reading = profile.Read(home);
            if (reading.Identity is not { } id)
            {
                continue;
            }

            var same = app.AccountUuid is not null
                ? string.Equals(id.Account, app.AccountUuid, StringComparison.OrdinalIgnoreCase)
                : org is not null && string.Equals(id.Organization, org, StringComparison.OrdinalIgnoreCase);
            if (!same)
            {
                continue;
            }

            // The same account can belong to several organizations; the plan is the one of the organization the app is in.
            var compatible = id.Organization is null || string.Equals(id.Organization, org, StringComparison.OrdinalIgnoreCase)
                             || (org is null && app.AccountOrganizations.Count == 0)
                             || ((org is null || mismatch) && app.AccountOrganizations.Contains(id.Organization, StringComparer.OrdinalIgnoreCase));
            if (compatible)
            {
                return (new ClaudeAccountProfile.Identity(app.AccountUuid ?? id.Account, id.Email, org ?? id.Organization, id.Person), reading.Plan, org, mismatch, true);
            }
        }

        return (new ClaudeAccountProfile.Identity(app.AccountUuid, null, org), null, org, mismatch, false);
    }
}
