// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Trayloom.Features.GitHub;

/// <summary>The outcome of one git run.</summary>
internal sealed record GitResult(int ExitCode, string Stdout, string Stderr, bool TimedOut, bool TooLarge, bool StartFailed);

/// <summary>What one refresh learned about a repository.</summary>
internal sealed record GitProbe(GitState? State, GitError Error, string? PushedSha, GhSlug? Slug, string? DefaultBranch);

/// <summary>Values that only change when the repository's config does, so they are not asked for on every refresh.</summary>
internal sealed class GitProbeCache
{
    private readonly object _lock = new();
    private string _root = "";
    private DateTime _configTime;
    private string _remote = "";
    private GhSlug? _slug;
    private string? _defaultBranch;
    private bool _defaultKnown;

    public (bool Hit, GhSlug? Slug) Slug(string root, string remote, DateTime configTime)
    {
        lock (_lock)
        {
            return root == _root && remote == _remote && configTime == _configTime && _remote.Length > 0 ? (true, _slug) : (false, null);
        }
    }

    public void StoreSlug(string root, string remote, DateTime configTime, GhSlug? slug)
    {
        lock (_lock)
        {
            if (root != _root)
            {
                _defaultKnown = false;
                _defaultBranch = null;
            }

            _root = root;
            _remote = remote;
            _configTime = configTime;
            _slug = slug;
        }
    }

    public (bool Hit, string? Branch) DefaultBranch(string root)
    {
        lock (_lock)
        {
            return root == _root && _defaultKnown ? (true, _defaultBranch) : (false, null);
        }
    }

    public void StoreDefaultBranch(string root, string? branch)
    {
        lock (_lock)
        {
            if (root == _root)
            {
                _defaultKnown = true;
                _defaultBranch = branch;
            }
        }
    }
}

/// <summary>
/// Runs read-only git commands (modelled on <see cref="GhCli"/>): arguments only through <see cref="ProcessStartInfo.ArgumentList"/>, a 6 s
/// timeout, a 1 MB output cap, and the optional locks turned off so a refresh can never create <c>index.lock</c> or rewrite the index.
/// </summary>
internal static class GitCli
{
    private const int TimeoutSeconds = 6;
    private const int MaxOutput = 1024 * 1024;

    private static readonly ConcurrentDictionary<int, Process> Running = new();
    private static readonly Regex RemoteName = new(@"^[A-Za-z0-9._-]{1,64}$", RegexOptions.Compiled);
    private static readonly Regex OwnerRule = new(@"^[A-Za-z0-9-]{1,39}$", RegexOptions.Compiled);
    private static readonly Regex RepoRule = new(@"^[A-Za-z0-9._-]{1,100}$", RegexOptions.Compiled);
    private static readonly Regex Sha40 = new(@"^[0-9a-f]{40}$", RegexOptions.Compiled);
    private static readonly Regex HttpsRemote = new(@"^https://(?:[^@/\s]+@)?github\.com/([^/\s]+)/([^/\s]+?)(?:\.git)?/?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ScpRemote = new(@"^[^@/\s]+@github\.com:([^/\s]+)/([^/\s]+?)(?:\.git)?/?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SshRemote = new(@"^ssh://(?:[^@/\s]+@)?github\.com(?::\d+)?/([^/\s]+)/([^/\s]+?)(?:\.git)?/?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string? _path;

    /// <summary>The first existing git.exe on PATH, then in the usual install folders.</summary>
    public static string? Find(bool refresh = false)
    {
        if (!refresh && _path is not null && File.Exists(_path))
        {
            return _path;
        }

        var candidates = new List<string>();
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                candidates.Add(Path.Combine(dir.Trim().Trim('"'), "git.exe"));
            }
            catch (ArgumentException)
            {
                // a malformed PATH entry
            }
        }

        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "cmd", "git.exe"));
        _path = candidates.FirstOrDefault(File.Exists);
        return _path;
    }

    /// <summary>Runs <c>git -C root --no-optional-locks args</c>. Kill-on-timeout and kill-on-cancel include the process tree.</summary>
    public static async Task<GitResult> RunAsync(string root, IReadOnlyList<string> args, CancellationToken ct)
    {
        var path = Find();
        if (path is null)
        {
            return new GitResult(-1, "", "", false, false, true);
        }

        var psi = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Environment.SystemDirectory,
        };

        // The built-in file system monitor would leave a daemon running beside the repository; a status without it is only slower.
        foreach (var a in new[] { "-C", root, "--no-optional-locks", "-c", "core.fsmonitor=false" })
        {
            psi.ArgumentList.Add(a);
        }

        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        psi.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["LC_ALL"] = "C";
        psi.Environment["GIT_PAGER"] = "";

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("no process");
        }
        catch (Exception)
        {
            return new GitResult(-1, "", "", false, false, true);
        }

        using (process)
        {
            var id = process.Id;
            Running[id] = process;
            try
            {
                process.StandardInput.Close();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
                var tooLarge = false;
                var stdout = ReadCappedAsync(process.StandardOutput, () => tooLarge = true);
                var stderr = ReadCappedAsync(process.StandardError, () => tooLarge = true);
                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                    await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Kill(process);
                    ct.ThrowIfCancellationRequested();
                    return new GitResult(-1, "", "", true, false, false);
                }

                return new GitResult(process.ExitCode, stdout.Result, stderr.Result, false, tooLarge, false);
            }
            finally
            {
                Running.TryRemove(id, out _);
            }
        }
    }

    /// <summary>Kills every git process this app started (stop and uninstall).</summary>
    public static void KillAll()
    {
        foreach (var process in Running.Values.ToList())
        {
            Kill(process);
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // already gone
        }
    }

    private static async Task<string> ReadCappedAsync(StreamReader reader, Action onOverflow)
    {
        var sb = new StringBuilder();
        var buffer = new char[8192];
        int n;
        while ((n = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
        {
            if (sb.Length + n > MaxOutput)
            {
                onOverflow();
                continue;
            }

            sb.Append(buffer, 0, n);
        }

        return sb.ToString();
    }

    // ---------------------------------------------------------------- one refresh

    /// <summary>
    /// Reads the state of a repository: status, the remote's github.com name (cached until the config changes), the commit the upstream
    /// branch points at, and the default branch (cached). Nothing here writes to the repository.
    /// </summary>
    public static async Task<GitProbe> ProbeAsync(string root, GitProbeCache cache, CancellationToken ct)
    {
        var unknownUntracked = false;
        var status = await RunAsync(root, ["status", "--porcelain=v2", "--branch", "--untracked-files=normal"], ct).ConfigureAwait(false);
        if (status.TimedOut)
        {
            // A huge working tree can make the untracked scan slow; the counts that matter most come without it.
            unknownUntracked = true;
            status = await RunAsync(root, ["status", "--porcelain=v2", "--branch", "--untracked-files=no"], ct).ConfigureAwait(false);
        }

        if (status.StartFailed)
        {
            return new GitProbe(null, GitError.Missing, null, null, null);
        }

        if (status.TimedOut)
        {
            return new GitProbe(null, GitError.TimedOut, null, null, null);
        }

        if (status.ExitCode != 0 || status.TooLarge)
        {
            var trust = status.Stderr.Contains("dubious ownership", StringComparison.OrdinalIgnoreCase);
            return new GitProbe(null, trust ? GitError.NotTrusted : GitError.Failed, null, null, null);
        }

        var state = ParseStatus(status.Stdout);
        if (state is null)
        {
            return new GitProbe(null, GitError.Failed, null, null, null);
        }

        state = state with { UntrackedUnknown = unknownUntracked };
        var remote = "origin";
        if (state.Upstream is { Length: > 0 } upstream && upstream.IndexOf('/') is var slash and > 0 && RemoteName.IsMatch(upstream[..slash]))
        {
            remote = upstream[..slash];
        }

        var slug = await ReadSlugAsync(root, remote, cache, ct).ConfigureAwait(false);
        string? pushed = null;
        if (state.Upstream is not null)
        {
            var sha = await RunAsync(root, ["rev-parse", "--verify", "--quiet", "@{u}"], ct).ConfigureAwait(false);
            var text = sha.Stdout.Trim();
            if (sha.ExitCode == 0 && Sha40.IsMatch(text))
            {
                pushed = text;
            }
        }

        string? defaultBranch;
        if (cache.DefaultBranch(root) is { Hit: true } known)
        {
            defaultBranch = known.Branch;
        }
        else
        {
            var head = await RunAsync(root, ["symbolic-ref", "--quiet", "--short", "refs/remotes/origin/HEAD"], ct).ConfigureAwait(false);
            var name = head.ExitCode == 0 ? head.Stdout.Trim() : "";
            defaultBranch = name.StartsWith("origin/", StringComparison.Ordinal) && name.Length > 7 ? name[7..] : null;
            cache.StoreDefaultBranch(root, defaultBranch);
        }

        return new GitProbe(state, GitError.None, pushed, slug, defaultBranch);
    }

    private static async Task<GhSlug?> ReadSlugAsync(string root, string remote, GitProbeCache cache, CancellationToken ct)
    {
        var configTime = DateTime.MinValue;
        if (RepoIndex.GitDir(root) is { } dir)
        {
            try
            {
                var config = new FileInfo(Path.Combine(dir, "config"));
                configTime = config.Exists ? config.LastWriteTimeUtc : DateTime.MinValue;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // the config time only decides whether the cache is reused
            }
        }

        if (cache.Slug(root, remote, configTime) is { Hit: true } hit)
        {
            return hit.Slug;
        }

        var result = await RunAsync(root, ["config", "--get", "remote." + remote + ".url"], ct).ConfigureAwait(false);
        var slug = result.ExitCode == 0 ? ParseRemote(result.Stdout.Trim()) : null;
        cache.StoreSlug(root, remote, configTime, slug);
        return slug;
    }

    // ---------------------------------------------------------------- pure parsing

    /// <summary>Reads <c>git status --porcelain=v2 --branch</c>; null when there is no branch header at all.</summary>
    public static GitState? ParseStatus(string porcelainV2)
    {
        string? head = null;
        string? oid = null;
        string? upstream = null;
        int ahead = 0, behind = 0, staged = 0, unstaged = 0, untracked = 0, conflicts = 0, both = 0;
        foreach (var raw in (porcelainV2 ?? "").Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("# branch.oid ", StringComparison.Ordinal))
            {
                oid = line["# branch.oid ".Length..].Trim();
            }
            else if (line.StartsWith("# branch.head ", StringComparison.Ordinal))
            {
                head = line["# branch.head ".Length..].Trim();
            }
            else if (line.StartsWith("# branch.upstream ", StringComparison.Ordinal))
            {
                upstream = line["# branch.upstream ".Length..].Trim();
            }
            else if (line.StartsWith("# branch.ab ", StringComparison.Ordinal))
            {
                foreach (var part in line["# branch.ab ".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (part.Length > 1 && int.TryParse(part[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                    {
                        if (part[0] == '+')
                        {
                            ahead = n;
                        }
                        else if (part[0] == '-')
                        {
                            behind = n;
                        }
                    }
                }
            }
            else if ((line.StartsWith("1 ", StringComparison.Ordinal) || line.StartsWith("2 ", StringComparison.Ordinal)) && line.Length >= 4)
            {
                var x = line[2] != '.';
                var y = line[3] != '.';
                staged += x ? 1 : 0;
                unstaged += y ? 1 : 0;
                both += x && y ? 1 : 0;
            }
            else if (line.StartsWith("u ", StringComparison.Ordinal))
            {
                conflicts++;
            }
            else if (line.StartsWith("? ", StringComparison.Ordinal))
            {
                untracked++;
            }
        }

        if (head is null)
        {
            return null;
        }

        var detached = head == "(detached)";
        var sha = oid is null || oid == "(initial)" ? null : oid;
        return new GitState(detached ? null : head, detached, sha, upstream, ahead, behind, staged, unstaged, untracked, conflicts, false, both);
    }

    /// <summary>
    /// The github.com repository a remote url points at, or null. The url is never logged or shown: it can carry a token.
    /// Owner and name follow the same rule as <see cref="GitHubQuery"/>.
    /// </summary>
    public static GhSlug? ParseRemote(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var text = url.Trim();
        var match = HttpsRemote.Match(text);
        if (!match.Success)
        {
            match = ScpRemote.Match(text);
        }

        if (!match.Success)
        {
            match = SshRemote.Match(text);
        }

        return match.Success ? Slug(match.Groups[1].Value, match.Groups[2].Value) : null;
    }

    /// <summary>A validated slug, or null when the owner or the name would not be accepted by <see cref="GitHubQuery"/>.</summary>
    public static GhSlug? Slug(string owner, string name) =>
        OwnerRule.IsMatch(owner) && RepoRule.IsMatch(name) && name.Trim('.').Length > 0 ? new GhSlug(owner, name) : null;
}
