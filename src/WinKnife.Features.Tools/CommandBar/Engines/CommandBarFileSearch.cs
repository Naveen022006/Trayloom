// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.IO;
using Windows.Storage;
using Windows.Storage.Search;

namespace WinKnife.Features.Tools;

/// <summary>File search through Windows Search with the ignore rules of spec 1.7.9.</summary>
internal static class CommandBarFileSearch
{
    private const int MaxCandidates = 1000;
    private const int MaxResults = 200;
    private const int FallbackDepth = 6;

    private static readonly string[] ShippedIgnores = ["node_modules", ".git", "DerivedData", "Pods", ".build", "vendor"];

    /// <summary>True when a path must never be offered: a hidden (dot) folder in its parents, a shipped ignore or a user ignore.</summary>
    /// <remarks>On Windows the dot rule only applies to parent folders (a file called <c>.log</c> is a normal file); the Hidden / System file
    /// attributes are checked where the file system is touched (<see cref="SearchAsync"/>).</remarks>
    public static bool IsIgnored(string path, IReadOnlyList<string> userIgnores)
    {
        var parts = Split(path);
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].StartsWith('.') && parts[i].Length > 1)
            {
                return true;
            }
        }

        foreach (var part in parts)
        {
            if (ShippedIgnores.Any(s => s.Equals(part, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return IsUserIgnored(path, userIgnores);
    }

    /// <summary>User ignores only: a whole component (case-insensitive) or an extension pattern <c>*.log</c> / <c>.log</c> matching the last component's suffix.</summary>
    public static bool IsUserIgnored(string path, IReadOnlyList<string> userIgnores)
    {
        var parts = Split(path);
        if (parts.Length == 0)
        {
            return false;
        }

        var last = parts[^1];
        foreach (var raw in userIgnores)
        {
            var pattern = raw.Trim();
            if (pattern.Length == 0)
            {
                continue;
            }

            if (pattern.StartsWith("*.", StringComparison.Ordinal) || (pattern.StartsWith('.') && pattern.Length > 1))
            {
                var suffix = pattern.StartsWith('*') ? pattern[1..] : pattern;
                if (last.Length > suffix.Length && last.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                continue;
            }

            if (parts.Any(p => p.Equals(pattern, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Expands scope strings (newline list, "~" = home): home expands to its visible children; only existing directories, no duplicates.</summary>
    public static IReadOnlyList<string> ResolveScopes(IEnumerable<string> scopes)
    {
        var home = Trim(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in scopes)
        {
            var scope = raw.Trim();
            if (scope.Length == 0)
            {
                continue;
            }

            if (scope == "~")
            {
                scope = home;
            }
            else if (scope.StartsWith("~/", StringComparison.Ordinal) || scope.StartsWith("~\\", StringComparison.Ordinal))
            {
                scope = Path.Combine(home, scope[2..]);
            }

            scope = Trim(Environment.ExpandEnvironmentVariables(scope));
            try
            {
                if (!Directory.Exists(scope))
                {
                    continue;
                }

                if (scope.Equals(home, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var child in Directory.EnumerateDirectories(scope))
                    {
                        if (!IsHiddenOrSystem(child) && !Path.GetFileName(child).StartsWith('.') && !Path.GetFileName(child).Equals("AppData", StringComparison.OrdinalIgnoreCase) && seen.Add(child))
                        {
                            result.Add(child);
                        }
                    }

                    continue;
                }

                if (seen.Add(scope))
                {
                    result.Add(scope);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // unreadable scope: skip
            }
        }

        return result;
    }

    /// <summary>Finds files whose name contains every word of the query (min 2 chars). At most 1000 candidates, 200 results, sorted by name.</summary>
    public static async Task<IReadOnlyList<string>> SearchAsync(string query, IReadOnlyList<string> scopes, IReadOnlyList<string> userIgnores, CancellationToken cancel)
    {
        var trimmed = (query ?? string.Empty).Trim();
        if (trimmed.Length < 2 || scopes.Count == 0)
        {
            return [];
        }

        var words = CommandBarSearchText.Normalize(trimmed).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var candidates = new List<string>();
        foreach (var scope in scopes)
        {
            cancel.ThrowIfCancellationRequested();
            if (candidates.Count >= MaxCandidates)
            {
                break;
            }

            var found = await QueryScopeAsync(scope, words, MaxCandidates - candidates.Count, cancel).ConfigureAwait(false);
            candidates.AddRange(found);
        }

        cancel.ThrowIfCancellationRequested();
        var matches = candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(p => NameHasWords(p, words) && !IsIgnored(p, userIgnores) && !IsHiddenOrSystem(p))
            .OrderBy(p => Path.GetFileName(p), StringComparer.Create(CultureInfo.CurrentCulture, true))
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Take(MaxResults)
            .ToList();
        return matches;
    }

    private static bool NameHasWords(string path, string[] words)
    {
        var name = CommandBarSearchText.Normalize(Path.GetFileName(path));
        return words.All(w => name.Contains(w, StringComparison.Ordinal));
    }

    private static async Task<List<string>> QueryScopeAsync(string scope, string[] words, int limit, CancellationToken cancel)
    {
        try
        {
            var folder = await StorageFolder.GetFolderFromPathAsync(scope).AsTask(cancel).ConfigureAwait(false);
            var options = new QueryOptions(CommonFileQuery.DefaultQuery, null)
            {
                FolderDepth = FolderDepth.Deep,
                IndexerOption = IndexerOption.UseIndexerWhenAvailable,
                ApplicationSearchFilter = string.Join(' ', words.Select(w => "System.FileName:~=\"" + w.Replace("\"", string.Empty) + "\"")),
            };
            var query = folder.CreateFileQueryWithOptions(options);
            var files = await query.GetFilesAsync(0, (uint)Math.Min(limit, MaxCandidates)).AsTask(cancel).ConfigureAwait(false);
            var list = files.Select(f => f.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
            if (list.Count > 0)
            {
                return list;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // fall through to the bounded enumeration
        }

        return await Task.Run(() => Enumerate(scope, words, limit, cancel), cancel).ConfigureAwait(false);
    }

    /// <summary>Bounded fallback for folders the index does not cover: depth-limited, cancellable.</summary>
    private static List<string> Enumerate(string scope, string[] words, int limit, CancellationToken cancel)
    {
        var result = new List<string>();
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = System.IO.FileAttributes.Hidden | System.IO.FileAttributes.System, RecurseSubdirectories = false };
        var pending = new Queue<(string Path, int Depth)>();
        pending.Enqueue((scope, 0));
        while (pending.Count > 0 && result.Count < limit)
        {
            cancel.ThrowIfCancellationRequested();
            var (dir, depth) = pending.Dequeue();
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*", options))
                {
                    if (NameHasWords(file, words))
                    {
                        result.Add(file);
                        if (result.Count >= limit)
                        {
                            break;
                        }
                    }
                }

                if (depth >= FallbackDepth)
                {
                    continue;
                }

                foreach (var sub in Directory.EnumerateDirectories(dir, "*", options))
                {
                    var name = Path.GetFileName(sub);
                    if (name.StartsWith('.') || ShippedIgnores.Any(s => s.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    pending.Enqueue((sub, depth + 1));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // skip unreadable folder
            }
        }

        return result;
    }

    private static bool IsHiddenOrSystem(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & (System.IO.FileAttributes.Hidden | System.IO.FileAttributes.System)) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static string[] Split(string path) => path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);

    private static string Trim(string path) => path.Length > 3 ? path.TrimEnd('\\', '/') : path;
}
