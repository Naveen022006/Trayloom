// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;

namespace Trayloom.Features.GitHub;

/// <summary>
/// The repositories this PC has seen, kept in <c>coding-repos.json</c> so a folder name in an editor title can be turned back into a
/// repository. The file holds local paths, so it is never logged or uploaded, and it is deleted when the extension is uninstalled.
/// </summary>
internal sealed class RepoIndex
{
    public const int MaxEntries = 200;
    private const long MaxStorageBytes = 2 * 1024 * 1024;
    private const int MaxScanDirectories = 4000;

    private readonly string? _file;
    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private bool _dirty;

    private sealed record Entry(string Root, string Name, DateTime SeenUtc);

    /// <param name="file">The json file; null keeps the index in memory only (tests).</param>
    public RepoIndex(string? file)
    {
        _file = file;
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>Reads the file; a missing or damaged file leaves the index empty.</summary>
    public void Load()
    {
        if (_file is null || !File.Exists(_file))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(_file));
            if (doc.RootElement.Obj("repos") is { ValueKind: JsonValueKind.Array } repos)
            {
                lock (_lock)
                {
                    foreach (var item in repos.EnumerateArray())
                    {
                        var root = item.Str("root");
                        if (root.Length > 0)
                        {
                            var seen = item.Date("seenUtc") ?? DateTime.UtcNow.AddDays(-30);
                            _entries[root] = new Entry(root, NameOf(root), seen);
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // an unreadable index is rebuilt as repositories are seen again
        }
    }

    /// <summary>Writes the file when something changed since the last save.</summary>
    public void Save()
    {
        if (_file is null)
        {
            return;
        }

        List<Entry> list;
        lock (_lock)
        {
            if (!_dirty)
            {
                return;
            }

            list = [.. _entries.Values.OrderByDescending(e => e.SeenUtc)];
            _dirty = false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var body = JsonSerializer.Serialize(new { v = 1, repos = list.Select(e => new { root = e.Root, name = e.Name, seenUtc = e.SeenUtc }) });
            File.WriteAllText(_file + ".tmp", body);
            File.Move(_file + ".tmp", _file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lock (_lock)
            {
                _dirty = true;
            }
        }
    }

    /// <summary>Deletes the file (uninstall) and forgets everything.</summary>
    public void Erase()
    {
        lock (_lock)
        {
            _entries.Clear();
            _dirty = false;
        }

        if (_file is null)
        {
            return;
        }

        foreach (var path in new[] { _file, _file + ".tmp" })
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // a locked file is not worth failing the uninstall
            }
        }
    }

    /// <summary>Remembers a repository as seen at <paramref name="seenUtc"/>; an older sighting never replaces a newer one.</summary>
    public void Add(RepoRef repo, DateTime seenUtc)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(repo.Root, out var old))
            {
                if (old.SeenUtc >= seenUtc)
                {
                    return;
                }
            }

            _entries[repo.Root] = new Entry(repo.Root, repo.Name, seenUtc);
            _dirty = true;
            if (_entries.Count > MaxEntries)
            {
                foreach (var drop in _entries.Values.OrderBy(e => e.SeenUtc).Take(_entries.Count - MaxEntries).ToList())
                {
                    _entries.Remove(drop.Root);
                }
            }
        }
    }

    /// <summary>The most recently seen repositories, newest first.</summary>
    public IReadOnlyList<RepoRef> Recent(int count)
    {
        lock (_lock)
        {
            return [.. _entries.Values.OrderByDescending(e => e.SeenUtc).Take(count).Select(e => new RepoRef(e.Root, e.Name))];
        }
    }

    /// <summary>The indexed repository with this folder name (any case). On a tie the one whose index file changed last wins.</summary>
    public RepoRef? ByName(string folder)
    {
        List<Entry> matches;
        lock (_lock)
        {
            matches = [.. _entries.Values.Where(e => e.Name.Equals(folder, StringComparison.OrdinalIgnoreCase))];
        }

        matches = [.. matches.Where(e => HasGit(e.Root))];
        if (matches.Count == 0)
        {
            return null;
        }

        var best = matches.Count == 1 ? matches[0] : matches.OrderByDescending(e => ActivityTime(e.Root)).First();
        return new RepoRef(best.Root, best.Name);
    }

    // ---------------------------------------------------------------- the file system

    /// <summary>Walks up from <paramref name="path"/> (at most 30 levels) to the folder that holds a <c>.git</c> directory or file.</summary>
    public static RepoRef? FindRoot(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var current = Path.GetFullPath(path.Trim());
            if (File.Exists(current))
            {
                current = Path.GetDirectoryName(current) ?? current;
            }

            for (var level = 0; level < 30 && current is not null; level++)
            {
                if (HasGit(current))
                {
                    return new RepoRef(current, NameOf(current));
                }

                current = Path.GetDirectoryName(current);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // an odd path in a title is not a repository
        }

        return null;
    }

    private static bool HasGit(string root)
    {
        try
        {
            var git = Path.Combine(root, ".git");
            return Directory.Exists(git) || File.Exists(git);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static string NameOf(string root)
    {
        var trimmed = root.TrimEnd('\\', '/');
        var name = Path.GetFileName(trimmed);
        return name.Length > 0 ? name : trimmed;
    }

    /// <summary>The real git directory: <c>.git</c> itself, or the target of a worktree or submodule <c>.git</c> file.</summary>
    public static string? GitDir(string root)
    {
        try
        {
            var git = Path.Combine(root, ".git");
            if (Directory.Exists(git))
            {
                return git;
            }

            if (File.Exists(git))
            {
                foreach (var line in File.ReadLines(git).Take(3))
                {
                    if (line.StartsWith("gitdir:", StringComparison.Ordinal))
                    {
                        var target = line["gitdir:".Length..].Trim();
                        var full = Path.GetFullPath(Path.IsPathRooted(target) ? target : Path.Combine(root, target));
                        return Directory.Exists(full) ? full : null;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // a damaged .git file has no usable directory
        }

        return null;
    }

    /// <summary>The newest change time of the files Git touches on a commit, checkout or add; <see cref="DateTime.MinValue"/> when none exist.</summary>
    public static DateTime ActivityTime(string root)
    {
        var dir = GitDir(root);
        if (dir is null)
        {
            return DateTime.MinValue;
        }

        var newest = DateTime.MinValue;
        foreach (var file in new[] { Path.Combine(dir, "index"), Path.Combine(dir, "HEAD"), Path.Combine(dir, "logs", "HEAD") })
        {
            try
            {
                var info = new FileInfo(file);
                if (info.Exists && info.LastWriteTimeUtc > newest)
                {
                    newest = info.LastWriteTimeUtc;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // a locked file is skipped
            }
        }

        return newest;
    }

    // ---------------------------------------------------------------- feeding the index

    /// <summary>
    /// Adds the repositories inside the user's project folders (<c>|</c>-separated), looking two levels down. Hidden folders and
    /// <c>node_modules</c> are skipped, and at most a few thousand folders are looked at.
    /// </summary>
    public void ScanFolders(string setting)
    {
        var budget = MaxScanDirectories;
        foreach (var raw in setting.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var top = Path.GetFullPath(raw);
                if (Directory.Exists(top))
                {
                    Scan(top, 0, ref budget);
                }
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // a folder that went away is skipped
            }
        }
    }

    private void Scan(string folder, int depth, ref int budget)
    {
        if (budget-- <= 0)
        {
            return;
        }

        if (HasGit(folder))
        {
            var activity = ActivityTime(folder);
            Add(new RepoRef(folder, NameOf(folder)), activity == DateTime.MinValue ? DateTime.UtcNow.AddDays(-30) : activity);
            return;
        }

        if (depth >= 2)
        {
            return;
        }

        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var child in children)
        {
            var name = Path.GetFileName(child);
            if (name.StartsWith('.') || name.Equals("node_modules", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Scan(child, depth + 1, ref budget);
        }
    }

    /// <summary>
    /// Adds the folders VS Code and Cursor remember in their <c>storage.json</c>: every <c>file:///</c> value (or key) whose path is
    /// inside a repository. A file over 2 MB is skipped.
    /// </summary>
    public void ScanEditorStorage(string appData)
    {
        foreach (var editor in new[] { "Code", "Cursor" })
        {
            var file = Path.Combine(appData, editor, "User", "globalStorage", "storage.json");
            try
            {
                var info = new FileInfo(file);
                if (!info.Exists || info.Length > MaxStorageBytes)
                {
                    continue;
                }

                using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
                var seen = info.LastWriteTimeUtc;
                foreach (var uri in FileUris(doc.RootElement).Distinct(StringComparer.OrdinalIgnoreCase).Take(400))
                {
                    if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile && FindRoot(parsed.LocalPath) is { } repo)
                    {
                        var activity = ActivityTime(repo.Root);
                        Add(repo, activity == DateTime.MinValue ? seen : activity);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // an editor that is not installed or a file being rewritten
            }
        }
    }

    private static IEnumerable<string> FileUris(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                if (element.GetString() is { } s && s.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                {
                    yield return s;
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                    {
                        yield return property.Name;
                    }

                    foreach (var inner in FileUris(property.Value))
                    {
                        yield return inner;
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var inner in FileUris(item))
                    {
                        yield return inner;
                    }
                }

                break;
        }
    }
}
