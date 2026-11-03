// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Interop;
using Trayloom.Core;
using Trayloom.Core.Native;
using static Trayloom.Core.Native.NativeConstants;

namespace Trayloom.Features.WindowTools;

/// <summary>Foreground / placement helpers shared by the window features.</summary>
internal static class WinUtil
{
    /// <summary>Restores a minimized window and brings it to the foreground.</summary>
    public static void Activate(nint hwnd)
    {
        if (hwnd == 0 || !WinApi.IsWindow(hwnd))
        {
            return;
        }

        if (WinApi.IsIconic(hwnd))
        {
            WinApi.ShowWindow(hwnd, WinApi.SW_RESTORE);
        }

        ForceForeground(hwnd);
    }

    /// <summary>SetForegroundWindow with the AttachThreadInput workaround for the foreground lock.</summary>
    public static bool ForceForeground(nint hwnd)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == hwnd)
        {
            return true;
        }

        var currentThread = NativeMethods.GetCurrentThreadId();
        var foregroundThread = foreground == 0 ? 0 : NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var attached = foregroundThread != 0 && foregroundThread != currentThread
            && WinApi.AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            WinApi.BringWindowToTop(hwnd);
            return NativeMethods.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
            {
                WinApi.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }

    /// <summary>
    /// Creates the window handle, moves the window onto the monitor containing the physical point (so WPF picks that
    /// monitor's DPI) and returns the resulting scale factor. Call before <c>Show()</c>.
    /// </summary>
    public static double PrepareOnMonitorAt(Window window, int x, int y)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        // Move only: a placeholder size here would leave the window ~100 px wide after Show().
        NativeMethods.SetWindowPos(hwnd, 0, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        return NativeMethods.GetDpiForWindow(hwnd) / 96.0;
    }

    /// <summary>Moves and sizes a window in physical pixels without activating it.</summary>
    public static void SetBounds(Window window, int x, int y, int width, int height)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        NativeMethods.SetWindowPos(hwnd, 0, x, y, width, height, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    /// <summary>Asks Windows to round the corners of a borderless window.</summary>
    public static void RoundCorners(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            var value = DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref value, sizeof(int));
        }
        catch (Exception)
        {
            // Windows 10: square corners.
        }
    }

    /// <summary>Hides a window from Alt+Tab / the taskbar (adds WS_EX_TOOLWINDOW).</summary>
    public static void MakeToolWindow(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var style = NativeMethods.GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        NativeMethods.SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)(style | WS_EX_TOOLWINDOW));
    }

    /// <summary>True while every modifier in <paramref name="modifiers"/> is physically held.</summary>
    public static bool AnyModifierHeld(HotkeyModifiers modifiers)
    {
        return (modifiers.HasFlag(HotkeyModifiers.Alt) && Down(VK_MENU))
            || (modifiers.HasFlag(HotkeyModifiers.Control) && Down(VK_CONTROL))
            || (modifiers.HasFlag(HotkeyModifiers.Shift) && Down(VK_SHIFT))
            || (modifiers.HasFlag(HotkeyModifiers.Win) && (Down(VK_LWIN) || Down(VK_RWIN)));
    }

    /// <summary>True when any of Ctrl, Alt, Shift or Win is down.</summary>
    public static bool AnyModifierDown() =>
        Down(VK_MENU) || Down(VK_CONTROL) || Down(VK_SHIFT) || Down(VK_LWIN) || Down(VK_RWIN);

    public static bool Down(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>All connected monitors, ordered left to right then top to bottom.</summary>
    public static List<(nint Handle, RECT Bounds, RECT Work)> Monitors()
    {
        var list = new List<(nint, RECT, RECT)>();
        WinApi.EnumDisplayMonitors(0, 0, (nint monitor, nint hdc, ref RECT rect, nint lParam) =>
        {
            var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
            if (NativeMethods.GetMonitorInfoW(monitor, ref info))
            {
                list.Add((monitor, info.rcMonitor, info.rcWork));
            }

            return true;
        }, 0);
        return [.. list.OrderBy(m => m.Item2.Left).ThenBy(m => m.Item2.Top)];
    }
}

internal static class ForegroundHelper
{
    /// <summary>
    /// When Trayloom itself (a flyout, the command bar) holds the foreground, hands it to the topmost foreign window
    /// and returns true so the caller can wait a moment before sending keys.
    /// </summary>
    public static bool HandBackFromOwnWindow()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(foreground, out var pid);
        if (foreground == 0 || pid != (uint)Environment.ProcessId)
        {
            return false;
        }

        foreach (var window in WindowEnumerator.Enumerate(includeMinimized: false))
        {
            NativeMethods.GetWindowThreadProcessId(window.Handle, out var windowPid);
            if (windowPid != (uint)Environment.ProcessId)
            {
                WinUtil.Activate(window.Handle);
                return true;
            }
        }

        return false;
    }
}

internal static class ModifierHelper
{
    /// <summary>
    /// Waits (up to <paramref name="timeoutMs"/>) until the modifier keys of the shortcut that triggered an action
    /// are released, then force-releases anything still held so a synthetic Ctrl+V is not turned into Ctrl+Alt+V.
    /// </summary>
    public static async Task ReleaseAsync(int timeoutMs = 1200)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (WinUtil.AnyModifierDown() && timer.ElapsedMilliseconds < timeoutMs)
        {
            await Task.Delay(15);
        }

        foreach (var vk in new[] { VK_MENU, VK_SHIFT, VK_LWIN, VK_RWIN })
        {
            if (WinUtil.Down(vk))
            {
                InputSender.SendKey((ushort)vk, keyUp: true);
            }
        }
    }
}
