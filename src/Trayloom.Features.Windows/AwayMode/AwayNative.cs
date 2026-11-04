// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;

namespace Trayloom.Features.WindowTools.AwayMode;

/// <summary>
/// The few Win32 calls Away mode needs that the project's <c>WinApi</c> does not declare. Window calls it already has
/// (<c>ShowWindowAsync</c>, <c>GetAncestor</c>, <c>IsZoomed</c>, <c>GetWindowPlacement</c>) are used from there.
/// </summary>
internal static class AwayNative
{
    /// <summary><c>QUNS_BUSY</c>, <c>QUNS_RUNNING_D3D_FULL_SCREEN</c> and <c>QUNS_PRESENTATION_MODE</c>.</summary>
    private static readonly int[] FullScreenStates = [2, 3, 4];

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();

    /// <summary>The tick count (32-bit, wraps) of the last keyboard or mouse input; 0 when it cannot be read.</summary>
    public static uint LastInputTick()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref info) ? info.dwTime : 0;
    }

    /// <summary>Milliseconds since the last input (wrap-safe).</summary>
    public static long IdleMilliseconds()
    {
        var last = LastInputTick();
        return last == 0 ? 0 : unchecked((uint)Environment.TickCount - last);
    }

    /// <summary>True while an app is full screen (Direct3D or busy) or presenting.</summary>
    public static bool IsFullScreenOrPresenting()
    {
        try
        {
            return SHQueryUserNotificationState(out var state) == 0 && Array.IndexOf(FullScreenStates, state) >= 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Locks the PC (the "Lock the PC too" option). Never called while the developer restriction is active.</summary>
    public static bool Lock() => LockWorkStation();
}
