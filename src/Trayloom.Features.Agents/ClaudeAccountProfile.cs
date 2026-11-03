// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Trayloom.Features.Agents;

/// <summary>
/// A Claude Code configuration directory, already set up by its owner. An empty path keeps Claude's usual login (default profile:
/// <c>~\.claude</c> + <c>~\.claude.json</c>). Only metadata is inspected; credentials belong to Claude and are never opened.
/// </summary>
public sealed class ClaudeAccountProfile : IEquatable<ClaudeAccountProfile>
{
    public sealed record Identity(string? Account, string? Email, string? Organization);

    public sealed class Reading
    {
        public Identity? Identity { get; set; }

        public AgentPlan? Plan { get; set; }

        public DateTime? UsageFetched { get; set; }

        /// <summary>The copy of the account's limits Claude Code keeps in its profile file (<c>cachedUsageUtilization</c>).</summary>
        public AgentLimits? CachedLimits { get; set; }

        /// <summary>The saved copy's figures as written, for the diagnostics list.</summary>
        public List<string> CachedRaw { get; set; } = [];
    }

    public ClaudeAccountProfile(string directory = "")
    {
        Directory = directory ?? "";
    }

    public string Directory { get; }

    public bool IsDefault => Directory.Length == 0;

    public string Id => Directory;

    public static ClaudeAccountProfile Selected(Trayloom.Core.IAppSettings app) => new(app.Get(AgentKeys.ClaudeProfile, ""));

    /// <summary>The folder Claude Code reads its configuration from; the default one honours <c>CLAUDE_CONFIG_DIR</c>.</summary>
    public string ConfigurationFolder(string home)
    {
        if (!IsDefault)
        {
            return Directory;
        }

        var env = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        return !string.IsNullOrWhiteSpace(env) ? env : Path.Combine(home, ".claude");
    }

    /// <summary>The profile file: <c>%USERPROFILE%\.claude.json</c> for the default account, <c>&lt;dir&gt;\.claude.json</c> otherwise.</summary>
    public string ProfileFile(string home)
    {
        if (!IsDefault)
        {
            return Path.Combine(Directory, ".claude.json");
        }

        var env = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        return !string.IsNullOrWhiteSpace(env) ? Path.Combine(env, ".claude.json") : Path.Combine(home, ".claude.json");
    }

    /// <summary>Never silently fall back to the default account if a chosen folder disappears.</summary>
    public bool IsAvailable(string home)
    {
        if (IsDefault)
        {
            return true;
        }

        try
        {
            if (!Path.IsPathRooted(Directory) || Encoding.UTF8.GetByteCount(Directory) > 4096)
            {
                return false;
            }

            var folder = new DirectoryInfo(Directory);
            var full = Path.GetFullPath(Directory).TrimEnd('\\', '/');
            if (folder.Parent is null || string.Equals(full, Path.GetFullPath(home).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)
                || !folder.Exists || (folder.Attributes & FileAttributes.ReparsePoint) != 0 || !OwnedByUser(folder) || WritableByOthers(folder))
            {
                return false;
            }

            var profile = new FileInfo(ProfileFile(home));
            if (!profile.Exists || (profile.Attributes & FileAttributes.ReparsePoint) != 0 || !OwnedByUser(profile))
            {
                return false;
            }

            using var handle = File.OpenHandle(profile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return AgentNative.LinkCount(handle) == 1;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            return false;
        }
    }

    internal static bool OwnedByUser(FileSystemInfo info)
    {
        var current = WindowsIdentity.GetCurrent().User;
        var owner = info is DirectoryInfo d
            ? d.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier
            : ((FileInfo)info).GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        return owner is not null && ((current is not null && owner.Equals(current)) || owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid));
    }

    /// <summary>Whether the DACL grants write access to Everyone, Users or Authenticated Users.</summary>
    internal static bool WritableByOthers(FileSystemInfo info)
    {
        var rules = info is DirectoryInfo d
            ? d.GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier))
            : ((FileInfo)info).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier));
        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.AccessControlType != AccessControlType.Allow || rule.IdentityReference is not SecurityIdentifier sid)
            {
                continue;
            }

            if (!(sid.IsWellKnown(WellKnownSidType.WorldSid) || sid.IsWellKnown(WellKnownSidType.BuiltinUsersSid) || sid.IsWellKnown(WellKnownSidType.AuthenticatedUserSid)))
            {
                continue;
            }

            const FileSystemRights write = FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories;
            if ((rule.FileSystemRights & write) != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The <c>projects</c> folders of this profile (default: <c>~\.claude</c> and <c>~\.config\claude</c>, plus <c>CLAUDE_CONFIG_DIR</c>).</summary>
    public List<string> LogFolders(string home)
    {
        if (!IsAvailable(home))
        {
            return [];
        }

        var folders = new List<string>();
        if (IsDefault)
        {
            folders.Add(Path.Combine(home, ".claude"));
            folders.Add(Path.Combine(home, ".config", "claude"));
            var env = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            if (!string.IsNullOrWhiteSpace(env))
            {
                folders.Add(env);
            }
        }
        else
        {
            folders.Add(Directory);
        }

        return folders.Distinct(StringComparer.OrdinalIgnoreCase).Select(f => Path.Combine(f, "projects")).ToList();
    }

    /// <summary>Only account metadata and the usage timestamp survive decoding, in memory. No email, id or token enters the usage archive.</summary>
    public Reading Read(string home)
    {
        var reading = new Reading();
        try
        {
            var file = ProfileFile(home);
            if (!IsAvailable(home) || !File.Exists(file))
            {
                return reading;
            }

            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > 4 << 20)
            {
                return reading;
            }

            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            if (root.Prop("oauthAccount") is { ValueKind: JsonValueKind.Object } account)
            {
                reading.Identity = new Identity(Text(account.Str("accountUuid")), Text(account.Str("emailAddress")), Text(account.Str("organizationUuid")));
                reading.Plan = AgentPlans.Claude(account.Str("organizationType"), account.Str("organizationRateLimitTier") ?? account.Str("userRateLimitTier"));
            }

            var cached = root.Prop("cachedUsageUtilization");
            if (cached.Prop("fetchedAtMs").Num() is { } ms && ms > 0)
            {
                reading.UsageFetched = AgentTime.Epoch.AddMilliseconds(ms);
                reading.CachedLimits = CachedLimits(cached, reading.UsageFetched.Value);
                if (cached.Prop("utilization") is { ValueKind: JsonValueKind.Object } raw)
                {
                    reading.CachedRaw = ClaudeServer.Describe(raw);
                }
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // An unreadable or half-written profile reads as nothing.
        }

        return reading;
    }

    /// <summary><c>cachedUsageUtilization.utilization.*</c> in the shape of the CLI's answer (Windows fallback).</summary>
    private static AgentLimits? CachedLimits(JsonElement? cached, DateTime fetched)
    {
        var utilization = cached.Prop("utilization");
        return utilization is { ValueKind: JsonValueKind.Object } u ? ClaudeServer.Limits(u, fetched, LimitOrigins.ClaudeCache) : null;
    }

    public string Name(string defaultName)
    {
        if (IsDefault)
        {
            return defaultName;
        }

        var name = Path.GetFileName(Directory.TrimEnd('\\', '/'));
        var trimmed = name.StartsWith(".claude-", StringComparison.Ordinal) ? name[8..] : name;
        return trimmed.Length > 80 ? trimmed[..80] : trimmed;
    }

    /// <summary>Common profile locations only; arbitrary folders are chosen explicitly.</summary>
    public static List<ClaudeAccountProfile> Discover(string home)
    {
        var candidates = new List<string>();
        try
        {
            candidates.AddRange(new DirectoryInfo(home).EnumerateDirectories().Where(d => d.Name == ".claude" || d.Name.StartsWith(".claude-", StringComparison.Ordinal)).Select(d => d.FullName));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        candidates.Add(Path.Combine(home, ".config", "claude"));
        return candidates.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).Take(32).Select(c => new ClaudeAccountProfile(c)).Where(p => p.IsAvailable(home)).ToList();
    }

    private static string? Text(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var clean = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length == 0 ? null : clean.Length > 254 ? clean[..254] : clean;
    }

    public bool Equals(ClaudeAccountProfile? other) => other is not null && string.Equals(Directory, other.Directory, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => Equals(obj as ClaudeAccountProfile);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Directory);
}
