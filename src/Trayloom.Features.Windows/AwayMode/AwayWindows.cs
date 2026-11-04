// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Runtime.InteropServices;
using Trayloom.Core;
using Trayloom.Core.Native;

namespace Trayloom.Features.WindowTools.AwayMode;

/// <summary>What a restore did, for the log line and the "verified" decision.</summary>
internal sealed record AwayRestoreResult(int Restored, int Total, int Late, int Closed, bool ZOrderOk, bool FocusOk, bool Verified);

/// <summary>
/// Snapshot, minimize, sweep and restore of foreign windows (spec 06 sections 4.4 and 4.5). Only minimize / restore / Z order
/// (and on-screen sanitising) are ever applied: nothing is hidden, made transparent or parked off-screen, so the window guardian
/// has nothing to track. Every cross-process call is either asynchronous or a cheap query.
/// </summary>
internal static class AwayWindows
{
    /// <summary>At most this many windows are recorded.</summary>
    public const int MaxWindows = 200;

    private const int ShowMaximized = 3;
    private const int ShowRestore = 9;

    /// <summary>
    /// Developer aid (TRAYLOOM_DEV=1 only): process names (lower-case, no extension) listed in <c>away-mode-only.txt</c>. When the file
    /// exists, only windows of those processes are ever touched. Null when the restriction is not active.
    /// </summary>
    public static string[]? DevOnlyProcesses()
    {
        if (Environment.GetEnvironmentVariable("TRAYLOOM_DEV") != "1")
        {
            return null;
        }

        try
        {
            var path = Path.Combine(AppPaths.Root, "away-mode-only.txt");
            if (!File.Exists(path))
            {
                return null;
            }

            return [.. File.ReadAllLines(path).Select(l => l.Trim().ToLowerInvariant()).Where(l => l.Length > 0)];
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The windows that would be recorded now, top-down (not yet recorded, not iconic).</summary>
    private static List<nint> Candidates(ISet<string> ignored, string[]? only)
    {
        var all = new List<nint>();
        WinApi.EnumWindows((hwnd, _) =>
        {
            if (WindowEnumerator.IsSwitchable(hwnd) && !WinApi.IsIconic(hwnd))
            {
                all.Add(hwnd);
            }

            return true;
        }, 0);

        var result = new List<nint>();
        foreach (var hwnd in all)
        {
            var owner = AppIdentity.OwnerOf(hwnd);
            if (owner.Pid == (uint)Environment.ProcessId)
            {
                continue;
            }

            if (owner.Path is not null && ignored.Contains(owner.Path))
            {
                continue;
            }

            if (only is not null && !only.Contains(owner.Name))
            {
                continue;
            }

            result.Add(hwnd);
            if (result.Count >= MaxWindows)
            {
                break;
            }
        }

        return result;
    }

    private static AwayRecord Describe(nint hwnd, int z)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        var owner = AppIdentity.OwnerOf(hwnd);
        var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        var normal = new[] { 0, 0, 0, 0 };
        if (WinApiPlacement.GetWindowPlacement(hwnd, ref placement))
        {
            var r = placement.rcNormalPosition;
            normal = [r.Left, r.Top, r.Right, r.Bottom];
        }

        return new AwayRecord
        {
            Hwnd = hwnd,
            Pid = pid,
            Process = owner.Name,
            Cls = WinApi.GetClass(hwnd),
            Z = z,
            Max = WinApi.IsZoomed(hwnd),
            Normal = normal,
        };
    }

    /// <summary>Records every switchable window (top-down) that Away mode may touch. Does not change any window.</summary>
    public static List<AwayRecord> Snapshot(ISet<string> ignored, string[]? only, out nint active)
    {
        var list = new List<AwayRecord>();
        foreach (var hwnd in Candidates(ignored, only))
        {
            list.Add(Describe(hwnd, list.Count));
        }

        // The window the person was working in; a Trayloom window or the desktop in front hands the focus to the topmost record.
        var foreground = NativeMethods.GetForegroundWindow();
        var root = foreground == 0 ? 0 : WinApi.GetAncestor(foreground, WinApi.GA_ROOTOWNER);
        active = list.Any(r => r.Handle == root) ? root : list.Count > 0 ? list[0].Handle : 0;
        return list;
    }

    /// <summary>Asks every recorded window to minimize without activating (asynchronous: a hung app cannot block Trayloom).</summary>
    public static void MinimizeAll(IEnumerable<AwayRecord> records)
    {
        foreach (var record in records)
        {
            record.MinimizedAt = Environment.TickCount64;
            WinApi.ShowWindowAsync(record.Handle, WinApi.SW_SHOWMINNOACTIVE);
        }
    }

    /// <summary>
    /// Removes the records of windows that are still not minimized (an elevated window refuses; UIPI) and of windows that are gone.
    /// Returns how many were dropped for staying visible.
    /// </summary>
    public static int DropNotMinimized(List<AwayRecord> records, long minAgeMs)
    {
        var dropped = 0;
        var now = Environment.TickCount64;
        records.RemoveAll(r =>
        {
            if (now - r.MinimizedAt < minAgeMs)
            {
                return false;
            }

            if (!WinApi.IsWindow(r.Handle))
            {
                return true;
            }

            if (!WinApi.IsIconic(r.Handle))
            {
                dropped++;
                return true;
            }

            return false;
        });
        return dropped;
    }

    /// <summary>
    /// Minimizes windows that appeared while away (a chat app opening a window must not undo the privacy screen) and records them above
    /// the existing ones. Returns the number added.
    /// </summary>
    public static int Sweep(List<AwayRecord> records, ISet<string> ignored, string[]? only)
    {
        if (records.Count >= MaxWindows)
        {
            return 0;
        }

        var known = records.Select(r => r.Handle).ToHashSet();
        var fresh = new List<AwayRecord>();
        foreach (var hwnd in Candidates(ignored, only))
        {
            if (!known.Contains(hwnd) && records.Count + fresh.Count < MaxWindows)
            {
                fresh.Add(Describe(hwnd, fresh.Count));
            }
        }

        if (fresh.Count == 0)
        {
            return 0;
        }

        foreach (var existing in records)
        {
            existing.Z += fresh.Count;
        }

        records.InsertRange(0, fresh);
        MinimizeAll(fresh);
        return fresh.Count;
    }

    /// <summary>True while the record still names the same window (not closed, same process, same class).</summary>
    public static bool StillMatches(AwayRecord record)
    {
        var hwnd = record.Handle;
        if (!WinApi.IsWindow(hwnd))
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return pid == record.Pid && string.Equals(WinApi.GetClass(hwnd), record.Cls, StringComparison.Ordinal);
    }

    /// <summary>
    /// Puts the recorded windows back (spec 06 section 4.5). Runs on a background thread. <paramref name="focusActive"/> is false for
    /// the start-up recovery (no focus stealing at login).
    /// </summary>
    public static AwayRestoreResult Restore(IReadOnlyList<AwayRecord> records, nint active, bool focusActive, bool onlyIconic = false)
    {
        var total = records.Count;
        var valid = records.Where(StillMatches).Where(r => !onlyIconic || WinApi.IsIconic(r.Handle)).OrderByDescending(r => r.Z).ToList();
        var closed = total - valid.Count;
        var late = 0;
        var budget = Stopwatch.StartNew();

        // 1. Bottom-up, so each restored window lands above the ones restored before it.
        foreach (var record in valid)
        {
            var hwnd = record.Handle;
            if (!WinApi.IsIconic(hwnd))
            {
                continue;
            }

            WinApi.ShowWindowAsync(hwnd, record.Max ? ShowMaximized : WinApi.SW_SHOWNOACTIVATE);
            var windowClock = Stopwatch.StartNew();
            var retried = false;
            while (WinApi.IsIconic(hwnd) && windowClock.ElapsedMilliseconds < 1000 && budget.ElapsedMilliseconds < 10000)
            {
                Thread.Sleep(25);
                if (!retried && windowClock.ElapsedMilliseconds >= 400 && WinApi.IsIconic(hwnd))
                {
                    // Some windows ignore SW_SHOWNOACTIVATE while minimized; a plain restore is enough (the focus is fixed below).
                    retried = true;
                    WinApi.ShowWindowAsync(hwnd, ShowRestore);
                }
            }

            if (WinApi.IsIconic(hwnd))
            {
                late++;
            }
        }

        // 2. Re-stack in the recorded order without activating: the first on top, each next one directly below the one before.
        var stack = valid.Where(r => WinApi.IsWindow(r.Handle) && !WinApi.IsIconic(r.Handle)).OrderBy(r => r.Z).ToList();
        const uint flags = NativeConstants.SWP_NOMOVE | NativeConstants.SWP_NOSIZE | NativeConstants.SWP_NOACTIVATE | WinApi.SWP_ASYNCWINDOWPOS;
        for (var i = 0; i < stack.Count; i++)
        {
            NativeMethods.SetWindowPos(stack[i].Handle, i == 0 ? 0 : stack[i - 1].Handle, 0, 0, 0, 0, flags);
        }

        // 3. A display may have been unplugged while away: never leave a window off-screen.
        foreach (var record in stack.Where(r => !r.Max))
        {
            Sanitize(record.Handle);
        }

        // 4. The window the person was working in gets the focus again (AttachThreadInput path; no input is synthesized).
        if (focusActive && active != 0 && WinApi.IsWindow(active) && !WinApi.IsIconic(active))
        {
            WinUtil.ForceForeground(active);
        }

        // 5. Let the windows settle, then check.
        Thread.Sleep(1500);
        var restored = valid.Count(r => WinApi.IsWindow(r.Handle) && !WinApi.IsIconic(r.Handle));
        var zOk = ZOrderMatches(stack);
        var foreground = NativeMethods.GetForegroundWindow();
        var foregroundRoot = foreground == 0 ? 0 : WinApi.GetAncestor(foreground, WinApi.GA_ROOTOWNER);
        var focusOk = !focusActive || active == 0 || foregroundRoot == active;
        return new AwayRestoreResult(restored, total, late, closed, zOk, focusOk, restored == valid.Count);
    }

    private static void Sanitize(nint hwnd)
    {
        try
        {
            using var _ = WinApiDpi.EnterWindowDpiContext(hwnd);
            if (!NativeMethods.GetWindowRect(hwnd, out var rect))
            {
                return;
            }

            var monitors = WindowPlacementMath.GetMonitors();
            if (WindowPlacementMath.IsComfortablyVisible(rect, monitors))
            {
                return;
            }

            var target = WindowPlacementMath.Sanitize(rect, monitors);
            NativeMethods.SetWindowPos(hwnd, 0, target.Left, target.Top, target.Width, target.Height,
                NativeConstants.SWP_NOZORDER | NativeConstants.SWP_NOACTIVATE | WinApi.SWP_ASYNCWINDOWPOS);
        }
        catch (Exception)
        {
            // placement safety is best effort; the window is at least visible in the taskbar
        }
    }

    /// <summary>True when the restored windows are stacked in the recorded order (top first).</summary>
    private static bool ZOrderMatches(IReadOnlyList<AwayRecord> expectedTopDown)
    {
        var wanted = expectedTopDown.Select(r => r.Handle).ToList();
        if (wanted.Count < 2)
        {
            return true;
        }

        var set = wanted.ToHashSet();
        var actual = new List<nint>();
        WinApi.EnumWindows((hwnd, _) =>
        {
            if (set.Contains(hwnd))
            {
                actual.Add(hwnd);
            }

            return true;
        }, 0);
        return actual.SequenceEqual(wanted);
    }
}
