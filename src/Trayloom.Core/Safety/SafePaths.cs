// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.RegularExpressions;

namespace Trayloom.Core;

/// <summary>
/// Path arithmetic shared by every feature that deletes, moves or elevates on a path: normalisation, "is under" tests and
/// the list of folders that must never be removed. Pure string work plus environment folders; nothing here touches the disk.
/// </summary>
public static class SafePaths
{
    private static readonly Lazy<IReadOnlyList<string>> s_critical = new(BuildCritical);

    /// <summary>
    /// <see cref="Path.GetFullPath(string)"/> with the trailing separator trimmed, except on a root (<c>C:</c> becomes <c>C:\</c>).
    /// </summary>
    public static string Normalize(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        if (Regex.IsMatch(path, @"^[A-Za-z]:$"))
        {
            return path + "\\";
        }

        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? string.Empty;
        if (full.Length > root.Length)
        {
            full = full.TrimEnd('\\', '/');
        }

        return full;
    }

    /// <summary>True when <paramref name="path"/> equals or lies below <paramref name="root"/> (case-insensitive); false for an empty root.</summary>
    public static bool IsUnder(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrEmpty(path))
        {
            return false;
        }

        try
        {
            var p = Normalize(path);
            var r = Normalize(root);
            if (p.Equals(r, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var prefix = r.EndsWith('\\') ? r : r + "\\";
            return p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Number of path segments after the drive or share root; 0 when the path cannot be parsed.</summary>
    public static int SegmentCount(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full) ?? string.Empty;
            return full[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries).Length;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>True for <c>C:\</c> and for a share root such as <c>\\server\share</c>.</summary>
    public static bool IsDriveRoot(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full) ?? string.Empty;
            return root.Length > 0 && full.TrimEnd('\\').Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Folders that must never be removed as a whole: Windows, Program Files, ProgramData, the user profile and its well-known
    /// folders, OneDrive roots. Every entry is non-empty and normalised.
    /// </summary>
    public static IReadOnlyList<string> CriticalRoots() => s_critical.Value;

    /// <summary>
    /// True for a drive root, a critical root, or any folder that contains a critical root (so removing it would remove one).
    /// There is deliberately no Windows clause: the Cleaner empties <c>Windows\Temp</c>; callers that need "not under Windows" also call
    /// <see cref="IsUnderWindows"/>.
    /// </summary>
    public static bool IsCriticalOrAncestor(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return true;
            }

            if (IsDriveRoot(path))
            {
                return true;
            }

            var full = Normalize(path);
            foreach (var critical in CriticalRoots())
            {
                if (IsUnder(critical, full))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>True when the path equals or is under <c>%WINDIR%</c>.</summary>
    public static bool IsUnderWindows(string path) => IsUnder(path, Environment.GetFolderPath(Environment.SpecialFolder.Windows));

    /// <summary>
    /// True for the folders many programs share (Common Files, ProgramData\Microsoft, Package Cache, per-user Programs and Microsoft).
    /// Equality only: a program's own folder below one of them is fine.
    /// </summary>
    public static bool IsSharedSystemFolder(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var full = Normalize(path);
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var shared = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86),
            };
            if (programData.Length > 0)
            {
                shared.Add(Path.Combine(programData, "Microsoft"));
                shared.Add(Path.Combine(programData, "Package Cache"));
            }

            if (local.Length > 0)
            {
                shared.Add(Path.Combine(local, "Programs"));
                shared.Add(Path.Combine(local, "Microsoft"));
            }

            foreach (var folder in shared)
            {
                if (folder.Length > 0 && Normalize(folder).Equals(full, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<string> BuildCritical()
    {
        var raw = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetEnvironmentVariable("ProgramW6432") ?? string.Empty,
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Path.Combine(Environment.GetEnvironmentVariable("SystemDrive") is { Length: > 0 } drive ? drive + "\\" : @"C:\", "Users"),
            Environment.GetEnvironmentVariable("PUBLIC") ?? string.Empty,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        };
        foreach (var key in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            raw.Add(Environment.GetEnvironmentVariable(key) ?? string.Empty);
        }

        var result = new List<string>();
        foreach (var entry in raw)
        {
            if (string.IsNullOrWhiteSpace(entry) || !Path.IsPathRooted(entry))
            {
                continue;
            }

            try
            {
                var normal = Normalize(entry);
                if (!result.Contains(normal, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(normal);
                }
            }
            catch
            {
                // an unparsable environment value is skipped
            }
        }

        return result;
    }
}
