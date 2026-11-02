// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;

namespace WinKnife.Features.SystemTools;

/// <summary>A reading of <c>GetSystemPowerStatus</c>.</summary>
internal readonly record struct PowerReading(bool HasBattery, bool OnAc, int Percent, int SecondsRemaining, bool Charging);

/// <summary>Battery / AC state helpers (cheap; no WMI).</summary>
internal static class PowerStatus
{
    /// <summary>Reads the current state (no battery = <c>HasBattery</c> false, on AC).</summary>
    public static PowerReading Read()
    {
        if (!GetSystemPowerStatus(out var s) || (s.BatteryFlag & 128) != 0 || s.BatteryFlag == 255)
        {
            return new PowerReading(false, true, 0, -1, false);
        }

        var percent = s.BatteryLifePercent == 255 ? 0 : s.BatteryLifePercent;
        var seconds = s.BatteryLifeTime == 0xFFFFFFFF ? -1 : (int)s.BatteryLifeTime;
        return new PowerReading(true, s.ACLineStatus == 1, percent, seconds, (s.BatteryFlag & 8) != 0);
    }

    /// <summary>True when the PC has an internal battery.</summary>
    public static bool HasBattery => Read().HasBattery;

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
