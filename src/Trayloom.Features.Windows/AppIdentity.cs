// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Trayloom.Core.Native;

namespace Trayloom.Features.WindowTools;

/// <summary>Who owns a window: process id, executable path (lower-case identity) and AppUserModelId.</summary>
internal sealed record WindowOwner(uint Pid, string? Path, string? Aumid)
{
    /// <summary>File name without extension, lower-cased ("chrome").</summary>
    public string Name => Path is null ? string.Empty : System.IO.Path.GetFileNameWithoutExtension(Path).ToLowerInvariant();

    /// <summary>File name with extension, lower-cased ("chrome.exe").</summary>
    public string FileName => Path is null ? string.Empty : System.IO.Path.GetFileName(Path).ToLowerInvariant();
}

/// <summary>App identity helper (spec 05 G1.4): process image name / package, icons, display names, list matching.</summary>
internal static class AppIdentity
{
    private static readonly ConcurrentDictionary<uint, string?> PathCache = new();
    private static readonly ConcurrentDictionary<string, BitmapSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string> NameCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Executable path of a process (cached per pid).</summary>
    public static string? PathOf(uint pid)
    {
        if (pid == 0)
        {
            return null;
        }

        if (PathCache.Count > 3000)
        {
            PathCache.Clear();
        }

        return PathCache.GetOrAdd(pid, static p => WinApi.GetProcessPath(p));
    }

    /// <summary>Resolves the owner of a top-level window; UWP frames resolve to the hosted app's process.</summary>
    public static WindowOwner OwnerOf(nint hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        var path = PathOf(pid);
        if (path is not null && path.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
        {
            uint hosted = 0;
            WinApi.EnumChildWindows(hwnd, (child, _) =>
            {
                NativeMethods.GetWindowThreadProcessId(child, out var childPid);
                if (childPid != 0 && childPid != pid)
                {
                    hosted = childPid;
                    return false;
                }

                return true;
            }, 0);
            if (hosted != 0)
            {
                pid = hosted;
                path = PathOf(hosted) ?? path;
            }
        }

        return new WindowOwner(pid, path?.ToLowerInvariant(), null);
    }

    /// <summary>Lower-cased identity of the executable path.</summary>
    public static string? Normalize(string? path) => string.IsNullOrWhiteSpace(path) ? null : path.Trim().ToLowerInvariant();

    /// <summary>True when the stored <paramref name="identity"/> (full path, file name or name) names the executable <paramref name="path"/>.</summary>
    public static bool Matches(string identity, string? path)
    {
        if (path is null || string.IsNullOrWhiteSpace(identity))
        {
            return false;
        }

        var id = identity.Trim();
        if (id.Contains('\\') || id.Contains('/'))
        {
            return string.Equals(id, path, StringComparison.OrdinalIgnoreCase);
        }

        var file = System.IO.Path.GetFileName(path);
        return string.Equals(id, file, StringComparison.OrdinalIgnoreCase)
            || string.Equals(id, System.IO.Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when any entry of <paramref name="list"/> names <paramref name="path"/>.</summary>
    public static bool InList(IEnumerable<string>? list, string? path)
    {
        if (list is null || path is null)
        {
            return false;
        }

        foreach (var entry in list)
        {
            if (Matches(entry, path))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The rule/value stored for the executable, or null.</summary>
    public static string? RuleFor(IReadOnlyDictionary<string, string>? rules, string? path)
    {
        if (rules is null || path is null)
        {
            return null;
        }

        foreach (var (identity, rule) in rules)
        {
            if (Matches(identity, path))
            {
                return rule;
            }
        }

        return null;
    }

    /// <summary>Friendly name: file description, else the file name without extension.</summary>
    public static string DisplayName(string identityOrPath)
    {
        return NameCache.GetOrAdd(identityOrPath, static p =>
        {
            try
            {
                if (System.IO.File.Exists(p))
                {
                    var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(p);
                    if (!string.IsNullOrWhiteSpace(info.FileDescription))
                    {
                        return info.FileDescription.Trim();
                    }
                }
            }
            catch (Exception)
            {
                // fall through
            }

            var name = System.IO.Path.GetFileNameWithoutExtension(p);
            return string.IsNullOrEmpty(name) ? p : char.ToUpperInvariant(name[0]) + name[1..];
        });
    }

    /// <summary>Product name used for app names in the switcher (file description, shortened).</summary>
    public static string AppName(string? path) => path is null ? string.Empty : DisplayName(path);

    /// <summary>Icon of an executable (frozen, shared).</summary>
    public static BitmapSource? IconOf(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        return IconCache.GetOrAdd(path, static p =>
        {
            try
            {
                if (!System.IO.File.Exists(p))
                {
                    return null;
                }

                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(p);
                return icon is null ? null : FromHIcon(icon.Handle);
            }
            catch (Exception)
            {
                return null;
            }
        });
    }

    /// <summary>Icon of a window (WM_GETICON / class icon), falling back to its executable.</summary>
    public static BitmapSource? IconOfWindow(nint hwnd, string? exePath)
    {
        try
        {
            foreach (var kind in new nint[] { 2, 1, 0 })
            {
                if (WinApi.SendMessageTimeoutW(hwnd, WinApi.WM_GETICON, kind, 0, WinApi.SMTO_ABORTIFHUNG, 40, out var icon) != 0 && icon != 0)
                {
                    return FromHIcon(icon);
                }
            }

            var classIcon = WinApi.GetClassLongPtr(hwnd, WinApi.GCLP_HICON);
            if (classIcon == 0)
            {
                classIcon = WinApi.GetClassLongPtr(hwnd, WinApi.GCLP_HICONSM);
            }

            if (classIcon != 0)
            {
                return FromHIcon(classIcon);
            }
        }
        catch (Exception)
        {
            // use the executable icon
        }

        return IconOf(exePath);
    }

    private static BitmapSource? FromHIcon(nint handle)
    {
        var source = Imaging.CreateBitmapSourceFromHIcon(handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }

    /// <summary>True when Trayloom is not elevated.</summary>
    public static bool IsAdmin()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
