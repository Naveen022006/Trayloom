// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Collections;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Text;
using System.Text.Json;

namespace WinKnife.Features.Agents;

/// <summary>Why a Codex server conversation failed.</summary>
public enum CodexFailure
{
    /// <summary>Neither the Codex app nor its command-line tool is on this PC.</summary>
    Missing,

    /// <summary>Codex is signed out, or signed in with a key or a cloud account, which have no plan and so no resets.</summary>
    NeedsSignIn,

    /// <summary>A Codex from before resets could be read from outside it.</summary>
    Outdated,

    /// <summary>No answer that says what happened.</summary>
    Unreachable,

    /// <summary>It answered with an error of its own.</summary>
    Refused,
}

/// <summary>What using a reset did, in the server's words.</summary>
public enum CodexOutcome
{
    Reset,
    NothingToReset,
    NoCredit,
    AlreadyRedeemed,
}

/// <summary>A banked reset: one renewal of the Codex session and weekly limits at once.</summary>
public sealed record CodexReset(string Id, DateTime? ExpiresAt);

/// <summary>The account's banked resets, as Codex reads them.</summary>
public sealed record CodexResetSummary(int Available, List<CodexReset> Resets, AgentLimits? Limits, DateTime CheckedAt)
{
    public DateTime? NextExpiry => Resets.Where(r => r.ExpiresAt is not null).Select(r => r.ExpiresAt!.Value).DefaultIfEmpty().Min() is var m && m != default ? m : null;
}

/// <summary>
/// Codex's own server, started for one short conversation at a time, answers for the account. It signs in with Codex's credentials,
/// which WinKnife never reads, and hears only the questions asked here.
/// </summary>
internal static class CodexServer
{
    public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan RedeemTimeout = TimeSpan.FromSeconds(30);

    public static string Version
    {
        get
        {
            var v = typeof(CodexServer).Assembly.GetName().Version;
            return v is null ? "0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    /// <summary>
    /// Where the tool can be: the desktop app's copy, the npm vendor binary (preferred over the .cmd shim), the installer's folder, then PATH.
    /// <c>%ProgramFiles%\WindowsApps</c> is never enumerated.
    /// </summary>
    public static IEnumerable<string> Candidates(string home, string? searchPath)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var bin = Path.Combine(local, "OpenAI", "Codex", "bin");
        if (Directory.Exists(bin))
        {
            IEnumerable<DirectoryInfo> dirs;
            try
            {
                dirs = new DirectoryInfo(bin).EnumerateDirectories().OrderByDescending(d => d.LastWriteTimeUtc).ToList();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                dirs = [];
            }

            foreach (var d in dirs)
            {
                yield return Path.Combine(d.FullName, "codex.exe");
            }

            yield return Path.Combine(bin, "codex.exe");
        }

        var npm = Path.Combine(roaming, "npm", "node_modules", "@openai", "codex", "node_modules");
        yield return Path.Combine(npm, "@openai", "codex-win32-x64", "vendor", "x86_64-pc-windows-msvc", "bin", "codex.exe");
        yield return Path.Combine(npm, "@openai", "codex-win32-arm64", "vendor", "aarch64-pc-windows-msvc", "bin", "codex.exe");
        yield return Path.Combine(home, ".local", "bin", "codex.exe");
        var folders = (searchPath ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim().Trim('"')).Where(Path.IsPathRooted)
            .Where(f => !f.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase));
        foreach (var f in folders)
        {
            yield return Path.Combine(f, "codex.exe");
        }

        yield return Path.Combine(roaming, "npm", "codex.cmd");
        foreach (var f in folders)
        {
            yield return Path.Combine(f, "codex.cmd");
        }
    }

    public static string? Executable(string home, string? searchPath) => Candidates(home, searchPath).FirstOrDefault(File.Exists);

    /// <summary>
    /// The PATH Windows gives a process of this app, plus the folders <c>where.exe codex</c> names. Asked only when the person looks again
    /// for a Codex that was not found. Never starts a login shell profile.
    /// </summary>
    public static string? WhereCodexPath()
    {
        try
        {
            var info = new ProcessStartInfo("where.exe", "codex") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
            using var p = Process.Start(info);
            if (p is null)
            {
                return null;
            }

            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            var folders = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(Path.GetDirectoryName).Where(d => !string.IsNullOrEmpty(d));
            var text = string.Join(';', folders!);
            return text.Length == 0 ? null : text;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    /// <summary>The environment for a conversation: the executable's folder, the extra search path, then the inherited PATH.</summary>
    public static Dictionary<string, string> BuildEnvironment(string executable, string? searchPath, IDictionary? baseEnvironment = null)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry e in baseEnvironment ?? Environment.GetEnvironmentVariables())
        {
            result[(string)e.Key] = e.Value?.ToString() ?? "";
        }

        var folders = new List<string> { Path.GetDirectoryName(executable) ?? "" };
        if (!string.IsNullOrEmpty(searchPath))
        {
            folders.Add(searchPath);
        }

        if (result.TryGetValue("PATH", out var inherited))
        {
            folders.Add(inherited);
        }

        result["PATH"] = string.Join(';', folders.Where(f => f.Length > 0));
        return result;
    }

    /// <summary>The account's resets and limits. An account without a plan has no resets, so its limits are not asked for either.</summary>
    public static (CodexResetSummary? Summary, CodexFailure? Failure) Check(string executable, Dictionary<string, string> environment)
    {
        using var conversation = CodexConversation.Start(executable, environment, CheckTimeout);
        if (conversation is null)
        {
            return (null, CodexFailure.Unreachable);
        }

        try
        {
            if (conversation.Initialize() is { } f)
            {
                return (null, f);
            }

            var (account, accountFailure) = conversation.Ask("account/read");
            using (account)
            {
                if (accountFailure is { } af)
                {
                    return (null, af);
                }

                if (SignInFailure(account!.RootElement) is { } sf)
                {
                    return (null, sf);
                }
            }

            return conversation.Summary();
        }
        finally
        {
            conversation.End();
        }
    }

    /// <summary>
    /// Uses one reset, the one <paramref name="credit"/> names or else the account's next, and reads the account again. The same key for a
    /// second try of one use never spends two. Only ever called from an explicit user click.
    /// </summary>
    public static (CodexOutcome? Outcome, CodexFailure? Failure, CodexResetSummary? Summary) Redeem(string executable, Dictionary<string, string> environment, string? credit, string key)
    {
        using var conversation = CodexConversation.Start(executable, environment, RedeemTimeout);
        if (conversation is null)
        {
            return (null, CodexFailure.Unreachable, null);
        }

        try
        {
            if (conversation.Initialize() is { } f)
            {
                return (null, f, null);
            }

            var parameters = new Dictionary<string, object?> { ["idempotencyKey"] = key };
            if (credit is not null)
            {
                parameters["creditId"] = credit;
            }

            var (result, failure) = conversation.Ask("account/rateLimitResetCredit/consume", parameters);
            CodexOutcome? outcome = null;
            using (result)
            {
                if (failure is null)
                {
                    outcome = OutcomeOf(result!.RootElement);
                    if (outcome is null)
                    {
                        failure = CodexFailure.Unreachable;
                    }
                }
            }

            var summary = conversation.Summary().Summary;
            return (outcome, failure, summary);
        }
        finally
        {
            conversation.End();
        }
    }

    /// <summary>Null while the account has a plan.</summary>
    public static CodexFailure? SignInFailure(JsonElement result) =>
        result.Prop("account").Str("type") == "chatgpt" ? null : CodexFailure.NeedsSignIn;

    /// <summary>Null for a Codex that does not report resets at all; one that reports none leaves the count at zero.</summary>
    public static CodexResetSummary? Summary(JsonElement result, DateTime now)
    {
        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("rateLimitResetCredits", out _))
        {
            return null;
        }

        var credits = result.Prop("rateLimitResetCredits");
        var listed = new List<CodexReset>();
        foreach (var credit in credits.Prop("credits").Items())
        {
            if (credit.Str("id") is not { Length: > 0 } id || (credit.Str("status") ?? "available") != "available")
            {
                continue;
            }

            var expires = credit.Prop("expiresAt").Seconds();
            if (expires is { } e && e <= now)
            {
                continue;
            }

            listed.Add(new CodexReset(id, expires));
        }

        var resets = listed.OrderBy(r => r.ExpiresAt ?? DateTime.MaxValue).ToList();
        var count = (int)Math.Max(resets.Count, credits.Prop("availableCount").Int());
        return new CodexResetSummary(count, resets, Limits(result, now), now);
    }

    /// <summary>The main allowance's windows, as a log names them.</summary>
    public static AgentLimits? Limits(JsonElement result, DateTime observed)
    {
        JsonElement? snapshot = result.Prop("rateLimitsByLimitId").Prop("codex");
        if (snapshot is not { ValueKind: JsonValueKind.Object } && result.Prop("rateLimits") is { ValueKind: JsonValueKind.Object } single
            && AgentLogParser.IsMainBucket(single.Str("limitId")))
        {
            snapshot = single;
        }

        if (snapshot is not { ValueKind: JsonValueKind.Object } s)
        {
            return null;
        }

        var windows = AgentLogParser.CodexWindows(s, observed, AgentLogParser.ServerWindowKeys);
        return windows.Count == 0 ? null : new AgentLimits(AgentProvider.Codex, windows, observed, LimitSource.Account);
    }

    public static CodexOutcome? OutcomeOf(JsonElement result) => result.Str("outcome") switch
    {
        "reset" => CodexOutcome.Reset,
        "nothingToReset" => CodexOutcome.NothingToReset,
        "noCredit" => CodexOutcome.NoCredit,
        "alreadyRedeemed" => CodexOutcome.AlreadyRedeemed,
        _ => null,
    };

    /// <summary>The server words its refusals: a question it does not know is an unknown variant to it, and a signed-out account needs authentication.</summary>
    public static CodexFailure FailureOf(JsonElement? error)
    {
        var message = (error.Prop("message").Str() ?? "").ToLowerInvariant();
        if (error.Prop("code").Num() == -32601 || message.Contains("unknown variant") || message.Contains("method not found"))
        {
            return CodexFailure.Outdated;
        }

        if (message.Contains("authentication") || message.Contains("not logged in"))
        {
            return CodexFailure.NeedsSignIn;
        }

        return CodexFailure.Refused;
    }
}

/// <summary>Codex's JSON-RPC protocol on the shared bounded CLI transport.</summary>
internal sealed class CodexConversation : IDisposable
{
    private readonly AgentCliConversation _cli;
    private int _lastId;

    private CodexConversation(AgentCliConversation cli) => _cli = cli;

    public static CodexConversation? Start(string executable, Dictionary<string, string> environment, TimeSpan timeout, CancellationToken cancel = default)
    {
        // Account metadata does not need plugins or marketplace updates.
        var cli = AgentCliConversation.Start(executable, ["-c", "features.plugins=false", "app-server"], environment, timeout, cancel);
        return cli is null ? null : new CodexConversation(cli);
    }

    /// <summary>Introduces the app, as every conversation must begin. Null on success.</summary>
    public CodexFailure? Initialize()
    {
        var (result, failure) = Ask("initialize", new Dictionary<string, object?>
        {
            ["clientInfo"] = new Dictionary<string, object?> { ["name"] = "winknife", ["title"] = "WinKnife", ["version"] = CodexServer.Version },
            ["capabilities"] = null,
        });
        result?.Dispose();
        if (failure is { } f)
        {
            return f;
        }

        return _cli.Send("{\"method\":\"initialized\"}") ? null : CodexFailure.Unreachable;
    }

    public (JsonDocument? Result, CodexFailure? Failure) Ask(string method, Dictionary<string, object?>? parameters = null)
    {
        var id = ++_lastId;
        var request = JsonSerializer.Serialize(new Dictionary<string, object?> { ["id"] = id, ["method"] = method, ["params"] = parameters ?? [] });
        if (!_cli.Send(request))
        {
            return (null, CodexFailure.Unreachable);
        }

        while (_cli.Next() is { } message)
        {
            var root = message.RootElement;
            // Notifications and the server's own requests are not the answer.
            if (root.TryGetProperty("method", out _) || root.Prop("id").Num() != id)
            {
                message.Dispose();
                continue;
            }

            if (root.Prop("result") is { ValueKind: JsonValueKind.Object } result)
            {
                var copy = JsonDocument.Parse(result.GetRawText());
                message.Dispose();
                return (copy, null);
            }

            var failure = CodexServer.FailureOf(root.Prop("error"));
            message.Dispose();
            return (null, failure);
        }

        return (null, CodexFailure.Unreachable);
    }

    public (CodexResetSummary? Summary, CodexFailure? Failure) Summary(DateTime? now = null)
    {
        var (result, failure) = Ask("account/rateLimits/read");
        using (result)
        {
            if (failure is { } f)
            {
                return (null, f);
            }

            var summary = CodexServer.Summary(result!.RootElement, now ?? DateTime.UtcNow);
            return summary is null ? (null, CodexFailure.Outdated) : (summary, null);
        }
    }

    public void End() => _cli.End();

    public void Dispose() => _cli.Dispose();
}

/// <summary>A Codex home of its own, a <c>CODEX_HOME</c>, whose account gets a limits card.</summary>
public sealed class CodexAccountProfile
{
    public const int Maximum = 8;

    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string Directory { get; set; } = "";

    public static List<CodexAccountProfile> Decode(string value)
    {
        if (string.IsNullOrEmpty(value) || Encoding.UTF8.GetByteCount(value) > 64 * 1024)
        {
            return [];
        }

        try
        {
            var decoded = JsonSerializer.Deserialize<List<CodexAccountProfile>>(value, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return decoded.Take(Maximum).Where(p =>
                Guid.TryParse(p.Id, out _) && !string.IsNullOrWhiteSpace(p.Name) && p.Name.Length <= 40 && Path.IsPathRooted(p.Directory)
                && ids.Add(p.Id) && paths.Add(Path.GetFullPath(p.Directory).TrimEnd('\\', '/'))).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string Encode(IEnumerable<CodexAccountProfile> profiles) =>
        JsonSerializer.Serialize(profiles.Select(p => new { id = p.Id, name = p.Name, directory = p.Directory }));

    /// <summary>
    /// Only metadata is looked at: Codex, not WinKnife, opens the sign-in (<c>auth.json</c> is never opened). A home with only a config is
    /// valid too, since Codex can keep its sign-in in the credential store. Null when the folder is not a private Codex home.
    /// </summary>
    public string? CanonicalDirectory()
    {
        try
        {
            if (!Path.IsPathRooted(Directory))
            {
                return null;
            }

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var full = Path.GetFullPath(Directory).TrimEnd('\\', '/');
            var folder = new DirectoryInfo(full);
            if (folder.Parent is null || string.Equals(full, Path.GetFullPath(home).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)
                || !folder.Exists || (folder.Attributes & FileAttributes.ReparsePoint) != 0
                || !ClaudeAccountProfile.OwnedByUser(folder) || ClaudeAccountProfile.WritableByOthers(folder))
            {
                return null;
            }

            var auth = new FileInfo(Path.Combine(full, "auth.json"));
            if (auth.Exists)
            {
                // A link would make two homes one account. Metadata only: never read.
                if ((auth.Attributes & FileAttributes.ReparsePoint) != 0 || !ClaudeAccountProfile.OwnedByUser(auth) || PrivateToOthers(auth) is false)
                {
                    return null;
                }

                using var handle = File.OpenHandle(auth.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return AgentNative.LinkCount(handle) == 1 ? full : null;
            }

            var config = new FileInfo(Path.Combine(full, "config.toml"));
            return config.Exists && (config.Attributes & FileAttributes.ReparsePoint) == 0 ? full : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Whether the file's DACL grants no access at all to Everyone, Users or Authenticated Users.</summary>
    private static bool? PrivateToOthers(FileInfo file)
    {
        var rules = file.GetAccessControl().GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier));
        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.AccessControlType == AccessControlType.Allow && rule.IdentityReference is System.Security.Principal.SecurityIdentifier sid
                && (sid.IsWellKnown(System.Security.Principal.WellKnownSidType.WorldSid) || sid.IsWellKnown(System.Security.Principal.WellKnownSidType.BuiltinUsersSid)
                    || sid.IsWellKnown(System.Security.Principal.WellKnownSidType.AuthenticatedUserSid)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary><c>~\.codex</c> and the <c>~\.codex-&lt;name&gt;</c> homes beside it. The usual one is named "Default".</summary>
    public static List<CodexAccountProfile> Discover(string home)
    {
        var result = new List<CodexAccountProfile>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var d in new DirectoryInfo(home).EnumerateDirectories().Where(d => d.Name == ".codex" || d.Name.StartsWith(".codex-", StringComparison.Ordinal)).OrderBy(d => d.FullName, StringComparer.OrdinalIgnoreCase))
            {
                var profile = new CodexAccountProfile { Id = Guid.NewGuid().ToString(), Name = NameFor(d.Name), Directory = d.FullName };
                if (profile.CanonicalDirectory() is { } canonical && paths.Add(canonical))
                {
                    profile.Directory = canonical;
                    result.Add(profile);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        return result;
    }

    /// <summary><c>.codex-work</c> is Work; the usual <c>.codex</c> takes the default name.</summary>
    public static string NameFor(string folderName)
    {
        var name = folderName.StartsWith(".codex-", StringComparison.Ordinal) ? folderName[".codex-".Length..] : folderName.TrimStart('.');
        if (name.Length == 0 || folderName == ".codex")
        {
            return AgentStrings.CodexDefaultName;
        }

        name = char.ToUpperInvariant(name[0]) + name[1..];
        return name.Length > 40 ? name[..40] : name;
    }
}

/// <summary>What stopped an account check.</summary>
public enum CodexAccountError
{
    InvalidProfile,
    MissingCli,
    NeedsSignIn,
    Outdated,
    Unavailable,
    Cancelled,
}

internal sealed record CodexAccountReading(AgentLimits Limits, string? Plan);

/// <summary>Asks the account behind one Codex home for its plan and limits through the same server and tool as the Resets card.</summary>
internal static class CodexAccountUsageReader
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan RenewedInterval = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan SettledRetry = TimeSpan.FromMinutes(30);

    public static TimeSpan Interval(CodexAccountError? failure, bool renewed) => failure switch
    {
        null => renewed ? RenewedInterval : RefreshInterval,
        CodexAccountError.Unavailable or CodexAccountError.Cancelled => RetryAfter,
        _ => SettledRetry,
    };

    private static readonly string[] Allowed =
    [
        "HOME", "USER", "LOGNAME", "TMPDIR", "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY", "http_proxy", "https_proxy", "all_proxy", "no_proxy",
        "SSL_CERT_FILE", "SSL_CERT_DIR", "NODE_EXTRA_CA_CERTS", "DO_NOT_TRACK", "DISABLE_TELEMETRY",
        "USERPROFILE", "APPDATA", "LOCALAPPDATA", "SystemRoot", "TEMP", "TMP", "PATH", "PATHEXT", "ComSpec",
    ];

    public static Dictionary<string, string> BuildEnvironment(string directory, string executable, string? searchPath)
    {
        // Neither a sign-in, a provider nor a shared state folder WinKnife was opened with may point a check at another account.
        var filtered = new Hashtable(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry e in Environment.GetEnvironmentVariables())
        {
            if (Allowed.Contains((string)e.Key, StringComparer.OrdinalIgnoreCase))
            {
                filtered[e.Key] = e.Value;
            }
        }

        var env = CodexServer.BuildEnvironment(executable, searchPath, filtered);
        env["CODEX_HOME"] = directory;
        env["LANG"] = "en_US.UTF-8";
        return env;
    }

    /// <summary>Asks only for the account and its limits, never refreshing its sign-in on purpose and never using a reset.</summary>
    public static (CodexAccountReading? Reading, CodexAccountError? Error) Read(CodexAccountProfile profile, string executable, string? searchPath, CancellationToken cancel)
    {
        if (profile.CanonicalDirectory() is not { } directory)
        {
            return (null, CodexAccountError.InvalidProfile);
        }

        if (cancel.IsCancellationRequested)
        {
            return (null, CodexAccountError.Cancelled);
        }

        using var conversation = CodexConversation.Start(executable, BuildEnvironment(directory, executable, searchPath), CodexServer.CheckTimeout, cancel);
        if (conversation is null)
        {
            return (null, cancel.IsCancellationRequested ? CodexAccountError.Cancelled : CodexAccountError.Unavailable);
        }

        try
        {
            if (conversation.Initialize() is { } init)
            {
                return (null, Map(init));
            }

            var (account, failure) = conversation.Ask("account/read", new Dictionary<string, object?> { ["refreshToken"] = false });
            using (account)
            {
                if (failure is { } f)
                {
                    return (null, Map(f));
                }

                if (CodexServer.SignInFailure(account!.RootElement) is not null)
                {
                    return (null, CodexAccountError.NeedsSignIn);
                }

                var (limitsResult, limitsFailure) = conversation.Ask("account/rateLimits/read");
                using (limitsResult)
                {
                    if (limitsFailure is { } lf)
                    {
                        return (null, Map(lf));
                    }

                    if (CodexServer.Limits(limitsResult!.RootElement, DateTime.UtcNow) is not { } limits)
                    {
                        return (null, CodexAccountError.Unavailable);
                    }

                    var reading = new CodexAccountReading(limits, PlanName(account.RootElement));
                    return cancel.IsCancellationRequested ? (null, CodexAccountError.Cancelled) : (reading, null);
                }
            }
        }
        finally
        {
            conversation.End();
        }
    }

    private static CodexAccountError Map(CodexFailure failure) => failure switch
    {
        CodexFailure.Missing => CodexAccountError.MissingCli,
        CodexFailure.NeedsSignIn => CodexAccountError.NeedsSignIn,
        CodexFailure.Outdated => CodexAccountError.Outdated,
        _ => CodexAccountError.Unavailable,
    };

    private static string? PlanName(JsonElement account)
    {
        var plan = account.Prop("account").Str("planType");
        return plan is "free" or "go" or "plus" or "pro" or "team" or "business" or "enterprise" or "edu" ? AgentPlans.Codex(plan)?.Name : null;
    }
}
