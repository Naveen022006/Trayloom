// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Core.Native;
using static Trayloom.Core.Native.NativeConstants;

namespace Trayloom.Features.WindowTools;

/// <summary>Outcome of a layout action (the result line of the page and the hosted panel).</summary>
internal enum LayoutResult
{
    Done,
    Restored,
    NoWindow,
    NoRestore,
    Failed,
}

/// <summary>
/// The placement engine (spec 05 A6.2): usable-area maths with screen and window gaps, size cycling, display moves,
/// restore history, DWM invisible-border compensation and a settle re-check.
/// </summary>
internal sealed class WindowLayoutEngine
{
    /// <summary>Tolerance (px) deciding "same placement" (Vorssaint <c>frameTolerance</c>).</summary>
    public const int FrameTolerance = 8;

    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "XamlExplorerHostIslandWindow", "Windows.UI.Core.CoreWindow",
    };

    private sealed record SavedState(RECT Normal, bool WasMaximized);

    private readonly IAppSettings _app;
    private readonly ILog _log;
    private readonly Dispatcher _ui;
    private readonly Dictionary<nint, SavedState> _saved = [];
    private readonly Dictionary<nint, (string Raw, RECT Frame)> _last = [];

    public WindowLayoutEngine(IAppSettings app, ILog log, Dispatcher ui)
    {
        _app = app;
        _log = log;
        _ui = ui;
    }

    /// <summary>Raised (UI thread) after every apply with its result.</summary>
    public event Action<LayoutResult>? ResultPublished;

    public void Clear()
    {
        _saved.Clear();
        _last.Clear();
    }

    /// <summary>The window an action applies to: the foreground window, or the topmost foreign window when Trayloom itself is in front.</summary>
    public nint ResolveTarget()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (hwnd != 0 && pid == (uint)Environment.ProcessId)
        {
            hwnd = 0;
            foreach (var window in WindowEnumerator.Enumerate(includeMinimized: false))
            {
                NativeMethods.GetWindowThreadProcessId(window.Handle, out var windowPid);
                if (windowPid != (uint)Environment.ProcessId)
                {
                    hwnd = window.Handle;
                    break;
                }
            }
        }

        if (hwnd != 0)
        {
            hwnd = WinApi.GetAncestor(hwnd, WinApi.GA_ROOT) is var root and not 0 ? root : hwnd;
        }

        return IsMovable(hwnd) ? hwnd : 0;
    }

    /// <summary>True for a normal top-level window this feature may move.</summary>
    public static bool IsMovable(nint hwnd)
    {
        if (hwnd == 0 || !WinApi.IsWindow(hwnd) || !WinApi.IsWindowVisible(hwnd) || hwnd == WinApi.GetShellWindow() || hwnd == WinApi.GetDesktopWindow())
        {
            return false;
        }

        return !IgnoredClasses.Contains(WinApi.GetClass(hwnd));
    }

    /// <summary>True when the window belongs to an app on the "Ignore apps" list.</summary>
    public bool IsIgnored(nint hwnd)
    {
        var list = _app.Get<List<string>>(Keys.LayoutIgnored);
        return list is { Count: > 0 } && AppIdentity.InList(list, AppIdentity.OwnerOf(hwnd).Path);
    }

    /// <summary>The foreground app is on the ignore list (inputs are inert).</summary>
    public bool ForegroundIgnored()
    {
        var list = _app.Get<List<string>>(Keys.LayoutIgnored);
        if (list is not { Count: > 0 })
        {
            return false;
        }

        var hwnd = NativeMethods.GetForegroundWindow();
        return hwnd != 0 && AppIdentity.InList(list, AppIdentity.OwnerOf(hwnd).Path);
    }

    /// <summary>Applies an action to the target window and publishes the result.</summary>
    public LayoutResult Apply(LayoutAction action, nint target = 0)
    {
        var result = ApplyCore(action, target);
        ResultPublished?.Invoke(result);
        return result;
    }

    private LayoutResult ApplyCore(LayoutAction action, nint target)
    {
        var hwnd = target != 0 ? target : ResolveTarget();
        if (hwnd == 0)
        {
            return LayoutResult.NoWindow;
        }

        Prune();
        try
        {
            using var dpi = WinApiDpi.EnterWindowDpiContext(hwnd);
            switch (action.Kind)
            {
                case PlacementKind.Restore:
                    return Restore(hwnd) ? LayoutResult.Restored : LayoutResult.NoRestore;
                case PlacementKind.PreviousDisplay:
                    return MoveToDisplay(hwnd, -1) ? LayoutResult.Done : LayoutResult.Failed;
                case PlacementKind.NextDisplay:
                    return MoveToDisplay(hwnd, 1) ? LayoutResult.Done : LayoutResult.Failed;
                case PlacementKind.Center:
                    return Center(hwnd) ? LayoutResult.Done : LayoutResult.Failed;
                case PlacementKind.FullScreen:
                    return FullScreen(hwnd) ? LayoutResult.Done : LayoutResult.Failed;
            }

            if (!CanResize(hwnd))
            {
                return LayoutResult.Failed;
            }

            SaveIfNeeded(hwnd);
            var chosen = ChooseCycle(hwnd, action, out var work);
            var frame = FrameFor(chosen, work);
            return PlaceVisible(hwnd, frame, chosen.Raw) ? LayoutResult.Done : LayoutResult.Failed;
        }
        catch (Exception ex)
        {
            _log.Error($"Window layout '{action.Raw}' failed", ex);
            return LayoutResult.Failed;
        }
    }

    // ---------------------------------------------------------------------------------------------- frames

    /// <summary>Usable area of a work area: minus the screen gap on every edge.</summary>
    public RECT Usable(RECT work)
    {
        var gap = Math.Max(0, _app.Get<int>(Keys.ScreenGap));
        return new RECT { Left = work.Left + gap, Top = work.Top + gap, Right = work.Right - gap, Bottom = work.Bottom - gap };
    }

    /// <summary>The frame an action produces inside <paramref name="work"/> (work area of the target monitor), gaps applied.</summary>
    public RECT FrameFor(LayoutAction action, RECT work)
    {
        var usable = Usable(work);
        var windowGap = Math.Max(0, _app.Get<int>(Keys.WindowGap));
        switch (action.Kind)
        {
            case PlacementKind.Maximize:
                return usable;
            case PlacementKind.FullScreen:
                return work;
            case PlacementKind.MarginMaximize:
            {
                var percent = Math.Clamp(_app.Get<double>(Keys.MarginPercent), 0, 25) / 100.0;
                var dx = (int)Math.Round(usable.Width * percent);
                var dy = (int)Math.Round(usable.Height * percent);
                return new RECT { Left = usable.Left + dx, Top = usable.Top + dy, Right = usable.Right - dx, Bottom = usable.Bottom - dy };
            }
        }

        var half = windowGap / 2;
        var left = usable.Left + (int)Math.Round(usable.Width * action.X0);
        var top = usable.Top + (int)Math.Round(usable.Height * action.Y0);
        var right = usable.Left + (int)Math.Round(usable.Width * action.X1);
        var bottom = usable.Top + (int)Math.Round(usable.Height * action.Y1);
        if (action.X0 > 0.0001) left += half;
        if (action.X1 < 0.9999) right -= half;
        if (action.Y0 > 0.0001) top += half;
        if (action.Y1 < 0.9999) bottom -= half;
        return new RECT { Left = left, Top = top, Right = Math.Max(left + 1, right), Bottom = Math.Max(top + 1, bottom) };
    }

    /// <summary>Work area of the monitor containing the window.</summary>
    public static RECT WorkAreaOf(nint hwnd)
    {
        var monitor = NativeMethods.MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        NativeMethods.GetMonitorInfoW(monitor, ref info);
        return info.rcWork;
    }

    /// <summary>Half -> two thirds -> one third on repeat, or the adjacent display on repeat (spec A6.2).</summary>
    private LayoutAction ChooseCycle(nint hwnd, LayoutAction action, out RECT work)
    {
        work = WorkAreaOf(hwnd);
        if (action.Raw is not ("leftHalf" or "rightHalf"))
        {
            return action;
        }

        var left = action.Raw == "leftHalf";
        var current = WinApi.VisibleFrame(hwnd);
        if (WinApi.IsZoomed(hwnd))
        {
            return action;
        }

        if (_app.Get<bool>(Keys.SideRepeat))
        {
            var cycle = left ? new[] { "leftHalf", "leftTwoThirds", "leftThird" } : new[] { "rightHalf", "rightTwoThirds", "rightThird" };
            for (var i = 0; i < cycle.Length; i++)
            {
                if (Near(current, FrameFor(LayoutActions.Require(cycle[i]), work)))
                {
                    return LayoutActions.Require(cycle[(i + 1) % cycle.Length]);
                }
            }

            return action;
        }

        if (Near(current, FrameFor(action, work)))
        {
            var monitors = WinUtil.Monitors();
            var index = monitors.FindIndex(m => m.Handle == NativeMethods.MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST));
            var next = index + (left ? -1 : 1);
            if (index >= 0 && next >= 0 && next < monitors.Count)
            {
                work = monitors[next].Work;
                // Continue the movement: the window lands on the facing half of the adjacent display.
                return LayoutActions.Require(left ? "rightHalf" : "leftHalf");
            }
        }

        return action;
    }

    // ---------------------------------------------------------------------------------------------- operations

    private bool Center(nint hwnd)
    {
        SaveIfNeeded(hwnd);
        var wasMaximized = WinApi.IsZoomed(hwnd);
        var size = CurrentNormalRect(hwnd);
        var visible = wasMaximized ? size : WinApi.VisibleFrame(hwnd);
        var usable = Usable(WorkAreaOf(hwnd));
        var width = Math.Min(visible.Width, usable.Width);
        var height = Math.Min(visible.Height, usable.Height);
        var left = usable.Left + (usable.Width - width) / 2;
        var top = usable.Top + (usable.Height - height) / 2;
        return PlaceVisible(hwnd, new RECT { Left = left, Top = top, Right = left + width, Bottom = top + height }, "center");
    }

    private bool FullScreen(nint hwnd)
    {
        var monitor = NativeMethods.MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        NativeMethods.GetMonitorInfoW(monitor, ref info);
        if (!WinApi.IsZoomed(hwnd) && Near(WinApi.VisibleFrame(hwnd), info.rcMonitor) && _saved.ContainsKey(hwnd))
        {
            return Restore(hwnd);
        }

        SaveIfNeeded(hwnd);
        return PlaceVisible(hwnd, info.rcMonitor, "fullScreen");
    }

    private bool Restore(nint hwnd)
    {
        if (!_saved.Remove(hwnd, out var state))
        {
            return false;
        }

        _last.Remove(hwnd);
        if (WinApi.IsZoomed(hwnd))
        {
            WinApi.ShowWindow(hwnd, WinApi.SW_RESTORE);
        }

        var r = state.Normal;
        NativeMethods.SetWindowPos(hwnd, 0, r.Left, r.Top, r.Width, r.Height, SWP_NOZORDER | SWP_NOACTIVATE);
        if (state.WasMaximized)
        {
            WinApi.ShowWindow(hwnd, WinApi.SW_MAXIMIZE);
        }

        return true;
    }

    private bool MoveToDisplay(nint hwnd, int direction)
    {
        var monitors = WinUtil.Monitors();
        if (monitors.Count < 2)
        {
            return false;
        }

        var current = NativeMethods.MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var index = Math.Max(0, monitors.FindIndex(m => m.Handle == current));
        var next = monitors[((index + direction) % monitors.Count + monitors.Count) % monitors.Count];
        var oldWork = monitors[index].Work;
        var wasMaximized = WinApi.IsZoomed(hwnd);
        var normal = wasMaximized ? CurrentNormalRect(hwnd) : WinApi.VisibleFrame(hwnd);
        if (wasMaximized)
        {
            WinApi.ShowWindow(hwnd, WinApi.SW_RESTORE);
        }

        double Frac(int value, int origin, int length) => Math.Clamp((value - origin) / (double)Math.Max(1, length), 0, 1);
        var w = next.Work;
        var target = new RECT
        {
            Left = w.Left + (int)Math.Round(w.Width * Frac(normal.Left, oldWork.Left, oldWork.Width)),
            Top = w.Top + (int)Math.Round(w.Height * Frac(normal.Top, oldWork.Top, oldWork.Height)),
            Right = w.Left + (int)Math.Round(w.Width * Frac(normal.Right, oldWork.Left, oldWork.Width)),
            Bottom = w.Top + (int)Math.Round(w.Height * Frac(normal.Bottom, oldWork.Top, oldWork.Height)),
        };

        _saved.Remove(hwnd);
        _last.Remove(hwnd);
        var ok = PlaceVisible(hwnd, target, "display");
        if (wasMaximized)
        {
            WinApi.ShowWindow(hwnd, WinApi.SW_MAXIMIZE);
        }

        return ok;
    }

    private static bool CanResize(nint hwnd) => (NativeMethods.GetWindowLongPtr(hwnd, WinApi.GWL_STYLE).ToInt64() & WinApi.WS_THICKFRAME) != 0;

    /// <summary>Remembers the window's frame before the first layout change (and again after the user moved it).</summary>
    private void SaveIfNeeded(nint hwnd)
    {
        if (_saved.ContainsKey(hwnd) && _last.TryGetValue(hwnd, out var last))
        {
            if (!WinApi.IsZoomed(hwnd) && Near(WinApi.VisibleFrame(hwnd), last.Frame, 2))
            {
                return;
            }
        }

        if (WinApi.IsZoomed(hwnd) && _saved.ContainsKey(hwnd))
        {
            return;
        }

        _saved[hwnd] = new SavedState(CurrentNormalRect(hwnd), WinApi.IsZoomed(hwnd));
    }

    private static RECT CurrentNormalRect(nint hwnd)
    {
        if (WinApi.IsZoomed(hwnd))
        {
            var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
            if (WinApiPlacement.GetWindowPlacement(hwnd, ref placement))
            {
                return placement.rcNormalPosition;
            }
        }

        NativeMethods.GetWindowRect(hwnd, out var rect);
        return rect;
    }

    /// <summary>
    /// Positions the window so its VISIBLE frame equals <paramref name="target"/>: the invisible resize borders
    /// (GetWindowRect vs DWMWA_EXTENDED_FRAME_BOUNDS) are added around it; repeated because moving across monitors with
    /// another DPI changes them. Schedules one settle re-check for apps that commit their size late.
    /// </summary>
    public bool PlaceVisible(nint hwnd, RECT target, string raw)
    {
        if (WinApi.IsZoomed(hwnd))
        {
            WinApi.ShowWindow(hwnd, WinApi.SW_RESTORE);
        }

        var ok = Write(hwnd, target);
        _last[hwnd] = (raw, WinApi.VisibleFrame(hwnd));
        _ui.BeginInvoke(async () =>
        {
            await Task.Delay(220);
            if (!WinApi.IsWindow(hwnd))
            {
                return;
            }

            using var dpi = WinApiDpi.EnterWindowDpiContext(hwnd);
            var frame = WinApi.VisibleFrame(hwnd);
            if (!Near(frame, target, 2))
            {
                Write(hwnd, target);
                _last[hwnd] = (raw, WinApi.VisibleFrame(hwnd));
            }
        });
        return ok;
    }

    private static bool Write(nint hwnd, RECT target)
    {
        var ok = true;
        for (var pass = 0; pass < 3; pass++)
        {
            NativeMethods.GetWindowRect(hwnd, out var full);
            var frame = WinApi.VisibleFrame(hwnd);
            if (pass > 0 && Near(frame, target, 1))
            {
                break;
            }

            var left = frame.Left - full.Left;
            var top = frame.Top - full.Top;
            var right = full.Right - frame.Right;
            var bottom = full.Bottom - frame.Bottom;
            ok = NativeMethods.SetWindowPos(hwnd, 0, target.Left - left, target.Top - top, target.Width + left + right, target.Height + top + bottom, SWP_NOZORDER | SWP_NOACTIVATE);
        }

        return ok;
    }

    public static bool Near(RECT a, RECT b, int tolerance = FrameTolerance) =>
        Math.Abs(a.Left - b.Left) <= tolerance && Math.Abs(a.Top - b.Top) <= tolerance
        && Math.Abs(a.Right - b.Right) <= tolerance && Math.Abs(a.Bottom - b.Bottom) <= tolerance;

    private void Prune()
    {
        if (_saved.Count + _last.Count < 32)
        {
            return;
        }

        foreach (var hwnd in _saved.Keys.Concat(_last.Keys).Distinct().ToList())
        {
            if (!WinApi.IsWindow(hwnd))
            {
                _saved.Remove(hwnd);
                _last.Remove(hwnd);
            }
        }
    }

    /// <summary>Text of a result for the result line.</summary>
    public static string Message(LayoutResult result) => result switch
    {
        LayoutResult.Done => Strings.ResultDone,
        LayoutResult.Restored => Strings.ResultRestored,
        LayoutResult.NoWindow => Strings.ResultNoWindow,
        LayoutResult.NoRestore => Strings.ResultNoRestore,
        _ => Strings.ResultFailed,
    };
}
