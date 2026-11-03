// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Trayloom.Core.Native;
using static Trayloom.Core.Native.NativeConstants;

namespace Trayloom.Core.Ui;

/// <summary>A monitor rectangle in physical pixels.</summary>
/// <param name="Bounds">Full monitor bounds.</param>
/// <param name="WorkArea">Area not covered by the taskbar.</param>
public readonly record struct MonitorArea(Rect Bounds, Rect WorkArea);

/// <summary>Screen helpers (all coordinates are physical pixels).</summary>
public static class Screens
{
    /// <summary>The cursor position in physical pixels.</summary>
    public static Point CursorPosition()
    {
        NativeMethods.GetCursorPos(out var pt);
        return new Point(pt.X, pt.Y);
    }

    /// <summary>The monitor containing (or nearest to) the physical-pixel point.</summary>
    public static MonitorArea FromPoint(Point physicalPoint)
    {
        var monitor = NativeMethods.MonitorFromPoint(new POINT { X = (int)physicalPoint.X, Y = (int)physicalPoint.Y }, MONITOR_DEFAULTTONEAREST);
        return FromHandle(monitor);
    }

    /// <summary>The primary monitor.</summary>
    public static MonitorArea Primary() =>
        FromHandle(NativeMethods.MonitorFromPoint(new POINT { X = 0, Y = 0 }, MONITOR_DEFAULTTOPRIMARY));

    /// <summary>The monitor under the mouse cursor.</summary>
    public static MonitorArea UnderCursor() => FromPoint(CursorPosition());

    private static MonitorArea FromHandle(nint monitor)
    {
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        NativeMethods.GetMonitorInfoW(monitor, ref info);
        return new MonitorArea(ToRect(info.rcMonitor), ToRect(info.rcWork));
    }

    private static Rect ToRect(RECT r) => new(r.Left, r.Top, r.Width, r.Height);
}

/// <summary>
/// Base class for floating overlays (island, toasts, previews): borderless, transparent-capable, topmost,
/// never takes focus (WS_EX_NOACTIVATE), hidden from Alt-Tab (WS_EX_TOOLWINDOW) and per-monitor DPI aware.
/// </summary>
public class OverlayWindow : Window
{
    private bool _clickThrough;

    /// <summary>Creates the overlay with its defaults applied.</summary>
    public OverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
    }

    /// <summary>When true, mouse input passes through the window to whatever is below (WS_EX_TRANSPARENT).</summary>
    public bool ClickThrough
    {
        get => _clickThrough;
        set
        {
            _clickThrough = value;
            ApplyExtendedStyles();
        }
    }

    /// <summary>The window handle (0 before the source is initialised).</summary>
    public nint Handle => new WindowInteropHelper(this).Handle;

    /// <inheritdoc />
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyExtendedStyles();
    }

    /// <summary>Shows the window without activating it.</summary>
    public void ShowNoActivate()
    {
        ShowActivated = false;
        Show();
        ApplyExtendedStyles();
    }

    /// <summary>
    /// Moves and sizes the window using physical screen pixels (independent of the DPI of the monitor it lands on)
    /// without activating it.
    /// </summary>
    public void SetPhysicalBounds(int x, int y, int width, int height)
    {
        var handle = Handle;
        if (handle == 0)
        {
            // Not created yet: force creation without showing.
            handle = new WindowInteropHelper(this).EnsureHandle();
        }

        NativeMethods.SetWindowPos(handle, HWND_TOPMOST, x, y, width, height, SWP_NOACTIVATE);
    }

    /// <summary>Converts device-independent units to physical pixels using this window's current DPI.</summary>
    public double ToPhysical(double dips) => dips * VisualTreeHelper.GetDpi(this).DpiScaleX;

    /// <summary>Converts physical pixels to device-independent units using this window's current DPI.</summary>
    public double ToDips(double pixels) => pixels / VisualTreeHelper.GetDpi(this).DpiScaleX;

    private void ApplyExtendedStyles()
    {
        var handle = Handle;
        if (handle == 0)
        {
            return;
        }

        var style = NativeMethods.GetWindowLongPtr(handle, GWL_EXSTYLE).ToInt64();
        style |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        if (_clickThrough)
        {
            style |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
        }
        else
        {
            style &= ~WS_EX_TRANSPARENT;
        }

        NativeMethods.SetWindowLongPtr(handle, GWL_EXSTYLE, (nint)style);
    }
}
