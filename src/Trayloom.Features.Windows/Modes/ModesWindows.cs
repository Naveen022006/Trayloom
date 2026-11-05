// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using Trayloom.Core.Native;
using Trayloom.Features.WindowTools.AwayMode;
using static Trayloom.Core.Native.NativeConstants;

namespace Trayloom.Features.WindowTools.Modes;

/// <summary>A window a mode opened or adopted. It is valid while the handle exists and the process that owns it started at <see cref="PidStart"/>.</summary>
/// <param name="Hwnd">The window handle.</param>
/// <param name="Pid">The process that owns the window.</param>
/// <param name="PidStart">The creation time of that process (a FILETIME), so a reused process id is never mistaken for it; 0 when unreadable.</param>
/// <param name="OpenedByMode">True when Trayloom started the app for this window; only those windows are ever closed on leaving.</param>
internal sealed record Tracked(nint Hwnd, uint Pid, long PidStart, bool OpenedByMode);

/// <summary>
/// The windows each mode item owns right now, in memory only: nothing about a window survives a restart of Trayloom. Used from the
/// UI thread and from the switch thread, so every member takes the lock.
/// </summary>
internal sealed class ModesTracking
{
    private readonly object _gate = new();
    private readonly Dictionary<(string ModeId, string ItemId), List<Tracked>> _map = [];

    /// <summary>True while the window still exists and belongs to the same process instance.</summary>
    public static bool IsValid(Tracked tracked)
    {
        if (!WinApi.IsWindow(tracked.Hwnd))
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(tracked.Hwnd, out var pid);
        return pid == tracked.Pid && (tracked.PidStart == 0 || AwayProcess.StartTime(pid) == tracked.PidStart);
    }

    /// <summary>Records a window for an item (replacing an earlier record of the same handle).</summary>
    public static Tracked Describe(nint hwnd, bool openedByMode)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return new Tracked(hwnd, pid, AwayProcess.StartTime(pid), openedByMode);
    }

    public void Add(string modeId, string itemId, Tracked tracked)
    {
        lock (_gate)
        {
            if (!_map.TryGetValue((modeId, itemId), out var list))
            {
                _map[(modeId, itemId)] = list = [];
            }

            list.RemoveAll(t => t.Hwnd == tracked.Hwnd);
            list.Add(tracked);
        }
    }

    /// <summary>The valid windows of an item; invalid records are dropped on the way.</summary>
    public List<Tracked> Live(string modeId, string itemId)
    {
        lock (_gate)
        {
            if (!_map.TryGetValue((modeId, itemId), out var list))
            {
                return [];
            }

            list.RemoveAll(t => !IsValid(t));
            return [.. list];
        }
    }

    public void Remove(string modeId, string itemId, nint hwnd)
    {
        lock (_gate)
        {
            if (_map.TryGetValue((modeId, itemId), out var list))
            {
                list.RemoveAll(t => t.Hwnd == hwnd);
            }
        }
    }

    public void DropMode(string modeId)
    {
        lock (_gate)
        {
            foreach (var key in _map.Keys.Where(k => k.ModeId == modeId).ToList())
            {
                _map.Remove(key);
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _map.Clear();
        }
    }

    /// <summary>Drops every record whose window or process is gone.</summary>
    public void Prune()
    {
        lock (_gate)
        {
            foreach (var list in _map.Values)
            {
                list.RemoveAll(t => !IsValid(t));
            }
        }
    }

    /// <summary>Away mode closed and reopened apps: the new window takes the place of the old handle in every record that had it.</summary>
    public void Replace(IReadOnlyDictionary<long, nint> replaced)
    {
        lock (_gate)
        {
            foreach (var list in _map.Values)
            {
                for (var i = 0; i < list.Count; i++)
                {
                    if (replaced.TryGetValue((long)list[i].Hwnd, out var now) && now != 0 && WinApi.IsWindow(now))
                    {
                        var fresh = Describe(now, list[i].OpenedByMode);
                        list[i] = fresh;
                    }
                }
            }
        }

        Prune();
    }

    /// <summary>The number of valid windows the whole table holds (for the dev dump).</summary>
    public int Count
    {
        get
        {
            Prune();
            lock (_gate)
            {
                return _map.Values.Sum(l => l.Count);
            }
        }
    }
}

/// <summary>
/// Finding, placing and minimizing windows for Modes (spec 08 section 2.4). Nothing here hides, makes transparent or moves a window
/// off-screen: a window is only minimized, restored without activation, or placed inside the work area of its own monitor, so the
/// window guardian has nothing to track.
/// </summary>
internal static class ModesWindows
{
    private static readonly HashSet<string> DefaultShellFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "applicationframehost.exe", "shellexperiencehost.exe", "startmenuexperiencehost.exe", "searchhost.exe", "textinputhost.exe",
        "lockapp.exe", "dwm.exe",
    };

    /// <summary>True for a window an item can claim: visible, unowned, titled and not a tool window.</summary>
    public static bool IsCandidate(nint hwnd)
    {
        if (!WinApi.IsWindowVisible(hwnd) || AwayProcess.OwnerWindow(hwnd) != 0 || WinApi.GetTitle(hwnd).Length == 0)
        {
            return false;
        }

        var ex = NativeMethods.GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        return (ex & WS_EX_TOOLWINDOW) == 0;
    }

    /// <summary>True for the shell and the system hosts, which are never an app of a workspace.</summary>
    public static bool IsShellFile(string? path) => !string.IsNullOrEmpty(path) && DefaultShellFiles.Contains(Path.GetFileName(path));

    /// <summary>Every switchable (Alt+Tab) window, minimized ones included, topmost first, without Trayloom's own.</summary>
    public static List<nint> Switchable()
    {
        var result = new List<nint>();
        foreach (var hwnd in AwayProcess.TopLevelWindows())
        {
            if (!WindowEnumerator.IsSwitchable(hwnd))
            {
                continue;
            }

            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != (uint)Environment.ProcessId)
            {
                result.Add(hwnd);
            }
        }

        return result;
    }

    // ------------------------------------------------------------------------------------------ finding what is open

    /// <summary>
    /// The switchable windows of an app item: for a packaged app the windows with its AppUserModelId; otherwise the windows of its
    /// executable whose process was started with the same arguments (an unreadable command line counts as a match, the rule of Away mode's reopen).
    /// </summary>
    public static List<nint> FindAppWindows(ModeItem item)
    {
        var result = new List<nint>();
        foreach (var hwnd in Switchable())
        {
            if (!string.IsNullOrEmpty(item.Aumid))
            {
                if (WinApi.GetWindowAumid(hwnd) == item.Aumid)
                {
                    result.Add(hwnd);
                }

                continue;
            }

            var owner = AppIdentity.OwnerOf(hwnd);
            if (owner.Path is null || !string.Equals(owner.Path, item.Exe, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var command = AwayProcess.CommandLine(owner.Pid);
            if (command is null || string.Equals(AwayProcess.ArgumentsOf(command).Trim(), item.Args.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                result.Add(hwnd);
            }
        }

        return result;
    }

    /// <summary>Every switchable window owned by an executable (the fallback after a restart, when nothing is tracked).</summary>
    public static List<nint> WindowsOfExe(string exe) =>
        [.. Switchable().Where(h => string.Equals(AppIdentity.OwnerOf(h).Path, exe, StringComparison.OrdinalIgnoreCase))];

    /// <summary>The File Explorer windows that show a local folder, as (window, folder); needs a thread in a single-threaded apartment.</summary>
    public static List<(nint Hwnd, string Folder)> ExplorerFolders()
    {
        var result = new List<(nint, string)>();
        object? shellObject = null;
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is null)
            {
                return result;
            }

            shellObject = Activator.CreateInstance(type);
            dynamic shell = shellObject!;
            dynamic windows = shell.Windows();
            var count = (int)windows.Count;
            for (var i = 0; i < count; i++)
            {
                object? item = null;
                try
                {
                    item = windows.Item(i);
                    if (item is null)
                    {
                        continue;
                    }

                    dynamic window = item;
                    string url = window.LocationURL;
                    if (!url.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    {
                        continue;
                    }

                    var folder = uri.LocalPath;
                    if (Directory.Exists(folder))
                    {
                        result.Add(((nint)(long)window.HWND, folder));
                    }
                }
                catch (Exception)
                {
                    // a window that closed while it was being read, or one without a location
                }
                finally
                {
                    if (item is not null && Marshal.IsComObject(item))
                    {
                        Marshal.ReleaseComObject(item);
                    }
                }
            }
        }
        catch (Exception)
        {
            // no shell automation: no folders are found, and the caller opens a new window
        }
        finally
        {
            if (shellObject is not null && Marshal.IsComObject(shellObject))
            {
                Marshal.ReleaseComObject(shellObject);
            }
        }

        return result;
    }

    /// <summary>True when both name the same folder (case and trailing separators ignored).</summary>
    public static bool SameFolder(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ------------------------------------------------------------------------------------------ placing and putting away

    /// <summary>Brings a minimized window back without taking the focus.</summary>
    public static void RestoreQuietly(nint hwnd)
    {
        if (WinApi.IsIconic(hwnd))
        {
            WinApi.ShowWindowAsync(hwnd, WinApi.SW_SHOWNOACTIVATE);
        }
    }

    public static void Minimize(nint hwnd)
    {
        if (WinApi.IsWindow(hwnd) && !WinApi.IsIconic(hwnd))
        {
            WinApi.ShowWindowAsync(hwnd, WinApi.SW_SHOWMINNOACTIVE);
        }
    }

    /// <summary>
    /// Puts a window into its zone. The frame always lies inside the work area of the window's own monitor, and a window already
    /// there is left alone. It does not use <c>Apply</c>, which would cycle sizes on a repeat.
    /// </summary>
    public static void ApplyZone(WindowLayoutEngine engine, nint hwnd, string? zone)
    {
        if (LayoutActions.Get(zone) is not { } action || !WinApi.IsWindow(hwnd))
        {
            return;
        }

        if (action.Raw == "maximize")
        {
            WinApi.ShowWindowAsync(hwnd, WinApi.SW_MAXIMIZE);
            return;
        }

        var frame = engine.FrameFor(action, WindowLayoutEngine.WorkAreaOf(hwnd));
        if (!WindowLayoutEngine.Near(WinApi.VisibleFrame(hwnd), frame))
        {
            engine.PlaceVisible(hwnd, frame, action.Raw);
        }
    }

    /// <summary>The zone a window sits in right now (maximize, or the halves, thirds or corner it fills), or null.</summary>
    public static string? DetectZone(WindowLayoutEngine engine, nint hwnd)
    {
        if (WinApi.IsIconic(hwnd))
        {
            return null;
        }

        if (WinApi.IsZoomed(hwnd))
        {
            return "maximize";
        }

        var frame = WinApi.VisibleFrame(hwnd);
        var work = WindowLayoutEngine.WorkAreaOf(hwnd);
        foreach (var action in LayoutActions.All.Where(a => a.Group is LayoutGroup.Halves or LayoutGroup.Thirds or LayoutGroup.Corners))
        {
            if (WindowLayoutEngine.Near(frame, engine.FrameFor(action, work)))
            {
                return action.Raw;
            }
        }

        return null;
    }
}
