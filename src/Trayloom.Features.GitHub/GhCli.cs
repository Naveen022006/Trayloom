// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace Trayloom.Features.GitHub;

/// <summary>The outcome of one gh run.</summary>
internal sealed record GhResult(int ExitCode, string Stdout, string Stderr, bool TimedOut, bool TooLarge, bool StartFailed)
{
    /// <summary>Exit code 4 or a "gh auth login" hint: gh is signed out.</summary>
    public bool SignedOut => ExitCode == 4 || Stderr.Contains("gh auth login", StringComparison.Ordinal);
}

/// <summary>The status line, headers and body of <c>gh api -i</c> output.</summary>
internal sealed record GhHttp(int Status, Dictionary<string, string> Headers, string Body)
{
    public string? Header(string name) => Headers.TryGetValue(name, out var v) ? v : null;
}

/// <summary>Finds and runs the GitHub CLI (spec 06 section 2.2 A). Trayloom never sees the token: gh uses its own credential store.</summary>
internal static class GhCli
{
    private const int TimeoutSeconds = 20;
    private const int MaxOutput = 4 * 1024 * 1024;

    private static readonly ConcurrentDictionary<int, Process> Running = new();
    private static string? _path;
    private static string? _version;

    /// <summary>The first existing gh.exe on PATH or in the usual install folders.</summary>
    public static string? Find(bool refresh = false)
    {
        if (!refresh && _path is not null && File.Exists(_path))
        {
            return _path;
        }

        _version = null;
        var candidates = new List<string>();
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                candidates.Add(Path.Combine(dir.Trim().Trim('"'), "gh.exe"));
            }
            catch (ArgumentException)
            {
                // a malformed PATH entry
            }
        }

        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GitHub CLI", "gh.exe"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "GitHub CLI", "gh.exe"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "GitHub CLI", "gh.exe"));
        _path = candidates.FirstOrDefault(File.Exists);
        return _path;
    }

    /// <summary>The version found by the last <see cref="VersionAsync"/>, without running gh again.</summary>
    public static string? CachedVersion => _version;

    /// <summary>"2.102.0" from the first line of <c>gh --version</c>, or null.</summary>
    public static async Task<string?> VersionAsync(CancellationToken ct)
    {
        if (_version is not null)
        {
            return _version;
        }

        var result = await RunAsync(["--version"], ct).ConfigureAwait(false);
        var first = result.Stdout.Split('\n', 2)[0].Trim();
        const string prefix = "gh version ";
        if (result.ExitCode == 0 && first.StartsWith(prefix, StringComparison.Ordinal))
        {
            _version = first[prefix.Length..].Split(' ')[0];
        }

        return _version;
    }

    /// <summary>
    /// Runs gh with arguments only through <see cref="ProcessStartInfo.ArgumentList"/> (never a shell). Timeout 20 s; on timeout, cancellation
    /// or <see cref="KillAll"/> the process tree is killed. Output beyond 4 MB is an error.
    /// </summary>
    public static async Task<GhResult> RunAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        var path = Find();
        if (path is null)
        {
            return new GhResult(-1, "", "", false, false, true);
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
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        psi.Environment["GH_PROMPT_DISABLED"] = "1";
        psi.Environment["GH_NO_UPDATE_NOTIFIER"] = "1";
        psi.Environment["GH_NO_EXTENSION_UPDATE_NOTIFIER"] = "1";
        psi.Environment["NO_COLOR"] = "1";
        psi.Environment["GH_PAGER"] = "";
        psi.Environment["GH_SPINNER_DISABLED"] = "1";
        psi.Environment["CLICOLOR"] = "0";

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("no process");
        }
        catch (Exception)
        {
            return new GhResult(-1, "", "", false, false, true);
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
                    return new GhResult(-1, "", "", true, false, false);
                }

                return new GhResult(process.ExitCode, stdout.Result, stderr.Result, false, tooLarge, false);
            }
            finally
            {
                Running.TryRemove(id, out _);
            }
        }
    }

    /// <summary>Kills every gh process this app started (<c>Stop()</c> and uninstall).</summary>
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

    /// <summary>Parses <c>gh api -i</c> stdout: status line, headers, blank line, body. Null when there is no status line.</summary>
    public static GhHttp? ParseHttp(string stdout)
    {
        if (!stdout.StartsWith("HTTP/", StringComparison.Ordinal))
        {
            return null;
        }

        var text = stdout.Replace("\r\n", "\n");
        var split = text.IndexOf("\n\n", StringComparison.Ordinal);
        var head = split < 0 ? text : text[..split];
        var body = split < 0 ? "" : text[(split + 2)..];
        var lines = head.Split('\n');
        var parts = lines[0].Split(' ', 3);
        if (parts.Length < 2 || !int.TryParse(parts[1], out var status))
        {
            return null;
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon > 0)
            {
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }
        }

        return new GhHttp(status, headers, body);
    }
}
