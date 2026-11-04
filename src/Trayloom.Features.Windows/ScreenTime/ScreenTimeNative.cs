// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;

namespace Trayloom.Features.WindowTools.ScreenTime;

/// <summary>The two Win32 calls Screen time needs that the shared native layers do not have. Both only read state.</summary>
internal static class ScreenTimeNative
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    /// <summary>Milliseconds since the last keyboard or mouse input (unsigned difference, so the tick count may wrap), or 0 when unknown.</summary>
    public static uint IdleMilliseconds()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        return GetLastInputInfo(ref info) ? unchecked((uint)Environment.TickCount - info.Time) : 0;
    }

    /// <summary>True when the shell reports a full-screen Direct3D app, a busy full-screen app or presentation mode.</summary>
    public static bool IsFullScreenOrPresenting()
    {
        // 2 = QUNS_BUSY, 3 = QUNS_RUNNING_D3D_FULL_SCREEN, 4 = QUNS_PRESENTATION_MODE.
        return SHQueryUserNotificationState(out var state) == 0 && state is 2 or 3 or 4;
    }
}
