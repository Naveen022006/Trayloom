// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Trayloom.Features.WindowTools;

/// <summary>
/// Apps that live in the notification area (spec 05 A7: the Windows twin of macOS menu-bar apps). Source: the per-app notification
/// icon records Windows 11 keeps in <c>HKCU\Control Panel\NotifyIconSettings</c> (executable path of every app that ever showed an icon).
/// </summary>
internal static class TrayApps
{
    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, nint token, [MarshalAs(UnmanagedType.LPWStr)] out string path);

    private static HashSet<string>? _known;
    private static DateTime _knownAt;

    /// <summary>Lower-cased executable paths of apps that own (or owned) a notification icon.</summary>
    public static HashSet<string> Known()
    {
        if (_known is not null && DateTime.UtcNow - _knownAt < TimeSpan.FromSeconds(30))
        {
            return _known;
        }

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings");
            if (root is not null)
            {
                foreach (var name in root.GetSubKeyNames())
                {
                    using var key = root.OpenSubKey(name);
                    if (key?.GetValue("ExecutablePath") is string raw && Resolve(raw) is { } path)
                    {
                        set.Add(path.ToLowerInvariant());
                    }
                }
            }
        }
        catch (Exception)
        {
            // registry unavailable: no tray apps known
        }

        _known = set;
        _knownAt = DateTime.UtcNow;
        return set;
    }

    private static string? Resolve(string raw)
    {
        if (raw.StartsWith('{') && raw.IndexOf('}') is var end and > 0 && Guid.TryParse(raw[1..end], out var guid))
        {
            return SHGetKnownFolderPath(guid, 0, 0, out var folder) == 0 ? Path.Combine(folder, raw[(end + 1)..].TrimStart('\\')) : null;
        }

        return raw;
    }

    /// <summary>Executable paths of running processes.</summary>
    public static Dictionary<string, uint> RunningPaths()
    {
        var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (AppIdentity.PathOf((uint)process.Id) is { } path)
                {
                    map.TryAdd(path.ToLowerInvariant(), (uint)process.Id);
                }
            }
            catch (Exception)
            {
                // process went away
            }
            finally
            {
                process.Dispose();
            }
        }

        return map;
    }

    /// <summary>Running tray apps that have no window entry yet.</summary>
    public static IEnumerable<(string Path, uint Pid)> RunningWithoutWindows(HashSet<string> present)
    {
        var known = Known();
        foreach (var (path, pid) in RunningPaths())
        {
            if (known.Contains(path) && !present.Contains(path))
            {
                yield return (path, pid);
            }
        }
    }

    /// <summary>True when the executable path belongs to a notification-area app.</summary>
    public static bool IsTrayApp(string? path) => path is not null && Known().Contains(path.ToLowerInvariant());
}
