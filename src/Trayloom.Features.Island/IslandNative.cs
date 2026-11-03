// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Text;
using Trayloom.Core.Native;

namespace Trayloom.Features.Island;

/// <summary>Island-specific Win32 interop (fullscreen detection, per-monitor DPI, foreground events, power status).</summary>
internal static class IslandNative
{
    internal delegate void WinEventProc(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time);

    internal const uint EventSystemForeground = 0x0003;
    internal const uint WinEventOutOfContext = 0x0000;
    internal const uint WinEventSkipOwnProcess = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [DllImport("shell32.dll")]
    internal static extern int SHQueryUserNotificationState(out int state);

    [DllImport("user32.dll")]
    internal static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint module, WinEventProc proc, uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint hwnd, StringBuilder text, int max);

    [DllImport("user32.dll")]
    internal static extern nint GetDesktopWindow();

    [DllImport("user32.dll")]
    internal static extern nint GetShellWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);

    [DllImport("user32.dll")]
    private static extern nint GetKeyboardLayout(uint thread);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int vKey);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out RECT rect, int size);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    /// <summary>Effective DPI scale (1.0 = 96 dpi) of a monitor.</summary>
    internal static double MonitorScale(nint monitor)
    {
        try
        {
            if (GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 && dpiX > 0)
            {
                return dpiX / 96.0;
            }
        }
        catch
        {
            // Older systems: fall through.
        }

        return 1.0;
    }

    internal static string ClassName(nint hwnd)
    {
        var sb = new StringBuilder(64);
        return GetClassNameW(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
    }

    /// <summary>Battery presence, charge (0..100) and plug state.</summary>
    internal static (bool HasBattery, int Percent, bool Plugged, bool Charging) Power()
    {
        if (!GetSystemPowerStatus(out var s))
        {
            return (false, 0, true, false);
        }

        var has = s.BatteryFlag != 128 && s.BatteryFlag != 255;
        return (has, s.BatteryLifePercent <= 100 ? s.BatteryLifePercent : 0, s.ACLineStatus == 1, (s.BatteryFlag & 8) != 0);
    }

    /// <summary>Layout identifier of the foreground window's thread (input source notice).</summary>
    internal static long ForegroundLayout()
    {
        var fg = NativeMethods.GetForegroundWindow();
        if (fg == 0)
        {
            return 0;
        }

        var thread = GetWindowThreadProcessId(fg, out _);
        return GetKeyboardLayout(thread).ToInt64();
    }

    /// <summary>
    /// True when a fullscreen app/game (or presentation) is in front on the monitor <paramref name="monitor"/>.
    /// Uses SHQueryUserNotificationState and whether the foreground window covers the whole monitor.
    /// </summary>
    internal static bool IsFullscreenForeground(nint monitor, RECT monitorBounds)
    {
        var fg = NativeMethods.GetForegroundWindow();
        if (fg == 0 || fg == GetDesktopWindow() || fg == GetShellWindow() || !IsWindowVisible(fg))
        {
            return false;
        }

        var cls = ClassName(fg);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
        {
            return false;
        }

        if (NativeMethods.MonitorFromWindow(fg, NativeConstants.MONITOR_DEFAULTTONEAREST) != monitor)
        {
            return false;
        }

        RECT frame;
        if (DwmGetWindowAttribute(fg, 9, out frame, Marshal.SizeOf<RECT>()) != 0)
        {
            NativeMethods.GetWindowRect(fg, out frame);
        }

        var covers = frame.Left <= monitorBounds.Left && frame.Top <= monitorBounds.Top
            && frame.Right >= monitorBounds.Right && frame.Bottom >= monitorBounds.Bottom;
        if (covers)
        {
            return true;
        }

        return SHQueryUserNotificationState(out var state) == 0 && state is 3 or 4;
    }
}
