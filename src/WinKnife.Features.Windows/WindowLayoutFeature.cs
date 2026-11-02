// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Input;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;
using static WinKnife.Core.Native.NativeConstants;

namespace WinKnife.Features.WindowTools;

/// <summary>Keyboard shortcuts that snap the active window to halves, thirds, quarters, other monitors and back.</summary>
public sealed class WindowLayoutFeature : IFeature
{
    private enum Kind
    {
        Tile,
        Maximize,
        Center,
        Restore,
        NextDisplay,
        PreviousDisplay,
    }

    private sealed record LayoutDef(string Id, string Title, Kind Kind, double X0, double Y0, double X1, double Y1, Hotkey Hotkey, string Glyph);

    private sealed record SavedState(RECT Normal, bool WasMaximized);

    private static readonly HotkeyModifiers CA = HotkeyModifiers.Control | HotkeyModifiers.Alt;
    private static readonly HotkeyModifiers CAS = HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift;

    private static readonly LayoutDef[] Layouts =
    [
        new("left-half", "Left half", Kind.Tile, 0, 0, 0.5, 1, new(CA, Key.Left), ""),
        new("right-half", "Right half", Kind.Tile, 0.5, 0, 1, 1, new(CA, Key.Right), ""),
        new("top-half", "Top half", Kind.Tile, 0, 0, 1, 0.5, new(CA, Key.Up), ""),
        new("bottom-half", "Bottom half", Kind.Tile, 0, 0.5, 1, 1, new(CA, Key.Down), ""),
        new("top-left", "Top left quarter", Kind.Tile, 0, 0, 0.5, 0.5, new(CA, Key.U), ""),
        new("top-right", "Top right quarter", Kind.Tile, 0.5, 0, 1, 0.5, new(CA, Key.I), ""),
        new("bottom-left", "Bottom left quarter", Kind.Tile, 0, 0.5, 0.5, 1, new(CA, Key.J), ""),
        new("bottom-right", "Bottom right quarter", Kind.Tile, 0.5, 0.5, 1, 1, new(CA, Key.K), ""),
        new("left-third", "Left third", Kind.Tile, 0, 0, 1.0 / 3, 1, new(CA, Key.D), ""),
        new("center-third", "Center third", Kind.Tile, 1.0 / 3, 0, 2.0 / 3, 1, new(CA, Key.F), ""),
        new("right-third", "Right third", Kind.Tile, 2.0 / 3, 0, 1, 1, new(CA, Key.G), ""),
        new("left-two-thirds", "Left two thirds", Kind.Tile, 0, 0, 2.0 / 3, 1, new(CA, Key.E), ""),
        new("right-two-thirds", "Right two thirds", Kind.Tile, 1.0 / 3, 0, 1, 1, new(CA, Key.T), ""),
        new("maximize", "Maximize", Kind.Maximize, 0, 0, 1, 1, new(CA, Key.Return), ""),
        new("center", "Center", Kind.Center, 0, 0, 1, 1, new(CA, Key.C), ""),
        new("restore", "Restore previous size", Kind.Restore, 0, 0, 1, 1, new(CA, Key.Back), ""),
        new("next-display", "Move to next monitor", Kind.NextDisplay, 0, 0, 1, 1, new(CAS, Key.Right), ""),
        new("previous-display", "Move to previous monitor", Kind.PreviousDisplay, 0, 0, 1, 1, new(CAS, Key.Left), ""),
    ];

    private static readonly string[] QuickActionIds =
        ["left-half", "right-half", "top-half", "bottom-half", "maximize", "center", "restore", "next-display"];

    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
    };

    private readonly Dictionary<nint, SavedState> _saved = [];
    private readonly Dictionary<nint, RECT> _applied = [];
    private IFeatureContext? _context;
    private HotkeyBindings? _bindings;

    public string Id => "window-layout";

    public string Name => "Window layout";

    public string Description => "Snap the active window to halves, thirds, quarters, other monitors or a centered spot with keyboard shortcuts.";

    public FeatureGroup Group => FeatureGroup.WindowsTaskbar;

    public IReadOnlyList<FeatureAction> Actions =>
    [
        .. QuickActionIds.Select(id =>
        {
            var def = Layouts.First(l => l.Id == id);
            return new FeatureAction(def.Id, def.Title, () => Apply(def), def.Glyph);
        }),
    ];

    public void Start(IFeatureContext context)
    {
        _context = context;
        _bindings = new HotkeyBindings(context,
            [.. Layouts.Select(def => new HotkeyBinding($"hotkey.{def.Id}", def.Title, def.Hotkey, () => Apply(def)))]);
    }

    public void Stop()
    {
        _bindings?.Dispose();
        _bindings = null;
        _saved.Clear();
        _applied.Clear();
        _context = null;
    }

    public FrameworkElement? CreateSettingsView()
    {
        if (_context is null)
        {
            return null;
        }

        var builder = new SettingsViewBuilder(_context.Settings)
            .AddIntSlider("gap", "Gap between windows", 0, 32, 0, "Pixels left between tiled windows (half of it at the screen edges).")
            .AddNote("All shortcuts act on the active window within the work area of the monitor it is on. Clear a shortcut to disable it.")
            .AddNote("Edge snapping while dragging is not included because Windows Snap (drag to an edge or corner, Win+Arrow keys, Snap layouts) already provides it.");
        foreach (var def in Layouts)
        {
            builder.AddHotkey($"hotkey.{def.Id}", def.Title, def.Hotkey);
        }

        return builder.Build();
    }

    private nint ResolveTarget()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (hwnd != 0 && pid == (uint)Environment.ProcessId)
        {
            // Invoked from WinKnife's own panel: act on the topmost regular window instead.
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

        if (hwnd == 0 || !WinApi.IsWindow(hwnd) || !WinApi.IsWindowVisible(hwnd) || hwnd == WinApi.GetShellWindow())
        {
            return 0;
        }

        return IgnoredClasses.Contains(WinApi.GetClass(hwnd)) ? 0 : hwnd;
    }

    private void Apply(LayoutDef def)
    {
        var hwnd = ResolveTarget();
        if (hwnd == 0)
        {
            return;
        }

        PruneDeadWindows();
        var canResize = (NativeMethods.GetWindowLongPtr(hwnd, WinApi.GWL_STYLE).ToInt64() & WinApi.WS_THICKFRAME) != 0;
        try
        {
            switch (def.Kind)
            {
                case Kind.Tile when canResize:
                    Tile(hwnd, def);
                    break;
                case Kind.Maximize when canResize:
                    SaveIfNeeded(hwnd);
                    WinApi.ShowWindow(hwnd, WinApi.SW_MAXIMIZE);
                    _applied.Remove(hwnd);
                    break;
                case Kind.Center:
                    Center(hwnd);
                    break;
                case Kind.Restore:
                    Restore(hwnd);
                    break;
                case Kind.NextDisplay:
                    MoveToDisplay(hwnd, 1);
                    break;
                case Kind.PreviousDisplay:
                    MoveToDisplay(hwnd, -1);
                    break;
            }
        }
        catch (Exception ex)
        {
            _context?.Log.Error($"Window layout '{def.Id}' failed", ex);
        }
    }

    private void Tile(nint hwnd, LayoutDef def)
    {
        SaveIfNeeded(hwnd);
        var work = WorkAreaOf(hwnd);
        var gap = (_context?.Settings.Get("gap", 0) ?? 0) / 2;
        var target = new RECT
        {
            Left = work.Left + (int)Math.Round(work.Width * def.X0) + gap,
            Top = work.Top + (int)Math.Round(work.Height * def.Y0) + gap,
            Right = work.Left + (int)Math.Round(work.Width * def.X1) - gap,
            Bottom = work.Top + (int)Math.Round(work.Height * def.Y1) - gap,
        };
        PlaceVisible(hwnd, target);
    }

    private void Center(nint hwnd)
    {
        SaveIfNeeded(hwnd);
        var wasMaximized = WinApi.IsZoomed(hwnd);
        var size = CurrentNormalRect(hwnd);
        var work = WorkAreaOf(hwnd);
        var width = Math.Min(size.Width, work.Width);
        var height = Math.Min(size.Height, work.Height);
        var left = work.Left + (work.Width - width) / 2;
        var top = work.Top + (work.Height - height) / 2;
        if (wasMaximized)
        {
            WinApi.ShowWindow(hwnd, WinApi.SW_RESTORE);
        }

        PlaceVisible(hwnd, new RECT { Left = left, Top = top, Right = left + width, Bottom = top + height });
    }

    private void Restore(nint hwnd)
    {
        if (_saved.Remove(hwnd, out var state))
        {
            _applied.Remove(hwnd);
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
        }
        else if (WinApi.IsZoomed(hwnd))
        {
            WinApi.ShowWindow(hwnd, WinApi.SW_RESTORE);
        }
    }

    private void MoveToDisplay(nint hwnd, int direction)
    {
        var monitors = WinUtil.Monitors();
        if (monitors.Count < 2)
        {
            return;
        }

        var current = NativeMethods.MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var index = monitors.FindIndex(m => m.Handle == current);
        if (index < 0)
        {
            index = 0;
        }

        var next = monitors[((index + direction) % monitors.Count + monitors.Count) % monitors.Count];
        var oldWork = monitors[index].Work;
        var wasMaximized = WinApi.IsZoomed(hwnd);
        var normal = CurrentNormalRect(hwnd);
        if (wasMaximized)
        {
            WinApi.ShowWindow(hwnd, WinApi.SW_RESTORE);
        }

        // Keep the same relative position and size within the work area.
        double Frac(int value, int origin, int length) => Math.Clamp((value - origin) / (double)Math.Max(1, length), 0, 1);
        var fx0 = Frac(normal.Left, oldWork.Left, oldWork.Width);
        var fy0 = Frac(normal.Top, oldWork.Top, oldWork.Height);
        var fx1 = Frac(normal.Right, oldWork.Left, oldWork.Width);
        var fy1 = Frac(normal.Bottom, oldWork.Top, oldWork.Height);
        var w = next.Work;
        var target = new RECT
        {
            Left = w.Left + (int)Math.Round(w.Width * fx0),
            Top = w.Top + (int)Math.Round(w.Height * fy0),
            Right = w.Left + (int)Math.Round(w.Width * fx1),
            Bottom = w.Top + (int)Math.Round(w.Height * fy1),
        };

        // The saved "original" size now belongs to another monitor; moving counts as a fresh start.
        _saved.Remove(hwnd);
        _applied.Remove(hwnd);
        PlaceVisible(hwnd, target);
        if (wasMaximized)
        {
            WinApi.ShowWindow(hwnd, WinApi.SW_MAXIMIZE);
        }
    }

    /// <summary>Remembers the window's size before the first layout action (and again after the user moved it).</summary>
    private void SaveIfNeeded(nint hwnd)
    {
        if (_saved.ContainsKey(hwnd) && _applied.TryGetValue(hwnd, out var last))
        {
            var now = WinApi.VisibleFrame(hwnd);
            if (!WinApi.IsZoomed(hwnd) && SameRect(now, last))
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

    /// <summary>The window rectangle ignoring maximization (full window rect including invisible borders).</summary>
    private static RECT CurrentNormalRect(nint hwnd)
    {
        if (WinApi.IsZoomed(hwnd))
        {
            var placement = new WINDOWPLACEMENT { length = System.Runtime.InteropServices.Marshal.SizeOf<WINDOWPLACEMENT>() };
            if (WinApiPlacement.GetWindowPlacement(hwnd, ref placement))
            {
                return placement.rcNormalPosition;
            }
        }

        NativeMethods.GetWindowRect(hwnd, out var rect);
        return rect;
    }

    private static RECT WorkAreaOf(nint hwnd)
    {
        var monitor = NativeMethods.MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        NativeMethods.GetMonitorInfoW(monitor, ref info);
        return info.rcWork;
    }

    /// <summary>
    /// Positions the window so its VISIBLE frame equals <paramref name="target"/>: the invisible resize borders
    /// (difference between GetWindowRect and DWMWA_EXTENDED_FRAME_BOUNDS) are added around it. Repeated once because
    /// moving across monitors with different DPI can change those borders.
    /// </summary>
    private void PlaceVisible(nint hwnd, RECT target)
    {
        if (WinApi.IsZoomed(hwnd))
        {
            WinApi.ShowWindow(hwnd, WinApi.SW_RESTORE);
        }

        for (var pass = 0; pass < 3; pass++)
        {
            NativeMethods.GetWindowRect(hwnd, out var full);
            var frame = WinApi.VisibleFrame(hwnd);
            if (pass > 0 && SameRect(frame, target))
            {
                break;
            }

            var left = frame.Left - full.Left;
            var top = frame.Top - full.Top;
            var right = full.Right - frame.Right;
            var bottom = full.Bottom - frame.Bottom;
            NativeMethods.SetWindowPos(
                hwnd,
                0,
                target.Left - left,
                target.Top - top,
                target.Width + left + right,
                target.Height + top + bottom,
                SWP_NOZORDER | SWP_NOACTIVATE);
        }

        _applied[hwnd] = WinApi.VisibleFrame(hwnd);
    }

    private static bool SameRect(RECT a, RECT b) =>
        a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;

    private void PruneDeadWindows()
    {
        if (_saved.Count + _applied.Count < 32)
        {
            return;
        }

        foreach (var hwnd in _saved.Keys.Concat(_applied.Keys).Distinct().ToList())
        {
            if (!WinApi.IsWindow(hwnd))
            {
                _saved.Remove(hwnd);
                _applied.Remove(hwnd);
            }
        }
    }
}
