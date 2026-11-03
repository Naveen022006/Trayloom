// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Trayloom.Features.Tools;

/// <summary>A user shortcut (link, place or script).</summary>
internal sealed class CommandBarLink
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Name { get; set; } = "";

    /// <summary>"link" | "place" | "script".</summary>
    public string Kind { get; set; } = "link";

    public string Destination { get; set; } = "";

    public bool RunsWithoutArgument { get; set; }

    public bool RunsDirectly { get; set; }

    public bool TakesQuery => Destination.Contains("{query}", StringComparison.Ordinal);

    public bool TakesArgument => Kind == "script" || TakesQuery;
}

/// <summary>Link placeholders, trailing arguments and the script runner (spec 1.7.8).</summary>
internal static class CommandBarLinks
{
    private const int OutputCap = 64 * 1024;

    private static readonly TimeSpan ScriptTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex Scheme = new(@"^[A-Za-z][A-Za-z0-9+.\-]*:", RegexOptions.Compiled);

    /// <summary>Normalised query must start with the folded name + " "; returns the remaining original words, else null.</summary>
    public static string? TrailingArgument(string query, string name)
    {
        var normalizedName = CommandBarSearchText.Normalize(name);
        if (normalizedName.Length == 0 || !CommandBarSearchText.Normalize(query).StartsWith(normalizedName + " ", StringComparison.Ordinal))
        {
            return null;
        }

        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).SelectMany(w => w.Split('　', StringSplitOptions.RemoveEmptyEntries)).ToArray();
        var nameWords = normalizedName.Split(' ').Length;
        if (words.Length <= nameWords || CommandBarSearchText.Normalize(string.Join(' ', words.Take(nameWords))) != normalizedName)
        {
            return null;
        }

        return string.Join(' ', words.Skip(nameWords));
    }

    /// <summary>Expands {query} {clipboard} {selection} {date} (links percent-encode, places insert raw). Returns the string to open.</summary>
    public static string Expand(CommandBarLink link, string? argument, string? clipboard, string? selection, DateTime now)
    {
        if (link.Kind == "script")
        {
            return link.Destination;
        }

        var encode = link.Kind == "link";
        string Value(string? v) => encode ? PercentEncode(v ?? string.Empty) : v ?? string.Empty;
        var result = link.Destination
            .Replace("{query}", Value(argument), StringComparison.Ordinal)
            .Replace("{clipboard}", Value(clipboard), StringComparison.Ordinal)
            .Replace("{selection}", Value(selection), StringComparison.Ordinal)
            .Replace("{date}", Value(now.ToString("d", CultureInfo.CurrentCulture)), StringComparison.Ordinal);
        result = result.Trim();
        if (encode)
        {
            return HasScheme(result) ? result : "https://" + result;
        }

        return ExpandHome(result);
    }

    /// <summary>Runs a script with one argument (5 s timeout, 64 KiB output cap). Returns trimmed stdout, or null when it failed / produced nothing and exited non-zero.</summary>
    public static async Task<(string? Output, bool Failed)> RunScriptAsync(CommandBarLink link, string? argument, CancellationToken cancel)
    {
        var path = ExpandHome(link.Destination.Trim());
        if (path.Length == 0 || !File.Exists(path))
        {
            return (null, true);
        }

        var info = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory,
        };
        if (Path.GetExtension(path).Equals(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            info.FileName = "powershell.exe";
            foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", path })
            {
                info.ArgumentList.Add(a);
            }
        }
        else
        {
            info.FileName = path;
        }

        if (!string.IsNullOrEmpty(argument))
        {
            info.ArgumentList.Add(argument);
        }

        Process? process;
        try
        {
            process = Process.Start(info);
        }
        catch (Exception)
        {
            return (null, true);
        }

        if (process is null)
        {
            return (null, true);
        }

        using (process)
        {
            using var timeout = new CancellationTokenSource(ScriptTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, timeout.Token);
            try
            {
                var output = ReadCapped(process.StandardOutput, linked.Token);
                _ = process.StandardError.ReadToEndAsync(linked.Token);
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
                var text = (await output.ConfigureAwait(false)).Trim();
                if (text.Length > 0)
                {
                    return (text, false);
                }

                return (null, process.ExitCode != 0);
            }
            catch (OperationCanceledException)
            {
                Kill(process);
                return (null, timeout.IsCancellationRequested && !cancel.IsCancellationRequested);
            }
            catch (Exception)
            {
                Kill(process);
                return (null, true);
            }
        }
    }

    private static async Task<string> ReadCapped(StreamReader reader, CancellationToken cancel)
    {
        var sb = new StringBuilder();
        var buffer = new char[4096];
        var bytes = 0;
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancel).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (bytes < OutputCap)
            {
                var take = Math.Min(read, Math.Max(0, OutputCap - bytes));
                sb.Append(buffer, 0, take);
                bytes += Encoding.UTF8.GetByteCount(buffer, 0, take);
            }
        }

        return sb.ToString();
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

    private static bool HasScheme(string text)
    {
        var m = Scheme.Match(text);
        if (!m.Success)
        {
            return false;
        }

        if (text.Contains("://", StringComparison.Ordinal))
        {
            return true;
        }

        // "host:8080" / "localhost:3000" is a host with a port, "mailto:x" / "tel:1" are schemes.
        var after = text[m.Length..];
        var before = text[..(m.Length - 1)];
        if (after.Length > 0 && char.IsAsciiDigit(after[0]) && (before.Contains('.') || before.Equals("localhost", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return true;
    }

    private static string ExpandHome(string path)
    {
        if (path == "~")
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        }

        return Environment.ExpandEnvironmentVariables(path);
    }

    private static string PercentEncode(string value)
    {
        var sb = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            var c = (char)b;
            if (b < 0x80 && (char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~'))
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return sb.ToString();
    }
}
