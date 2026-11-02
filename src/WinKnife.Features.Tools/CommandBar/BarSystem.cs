// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Windows.Devices.Radios;
using WinKnife.Core.Native;
using WinKnife.Core;

namespace WinKnife.Features.Tools;

/// <summary>Machine facts and system operations the Command Bar rows use (battery, memory, storage, volume, brightness, power, Wi-Fi).</summary>
internal static class BarSystem
{
    // ------------------------------------------------------------------ battery / memory / storage

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);
    [DllImport("powrprof.dll")] private static extern bool SetSuspendState(bool hibernate, bool force, bool disableWakeEvent);

    /// <summary>Battery percentage and state; null on a desktop without a battery.</summary>
    public static (int Percent, string State)? Battery()
    {
        if (!GetSystemPowerStatus(out var s) || s.BatteryFlag == 128 || s.BatteryFlag == 255 || s.BatteryLifePercent > 100)
        {
            return null;
        }

        var state = (s.BatteryFlag & 8) != 0 ? "charging" : s.ACLineStatus == 1 ? "on power" : Loc.T("Battery");
        return (s.BatteryLifePercent, state);
    }

    /// <summary>Memory load percent, used and total bytes.</summary>
    public static (int Percent, ulong Used, ulong Total)? Memory()
    {
        var m = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref m) ? ((int)m.dwMemoryLoad, m.ullTotalPhys - m.ullAvailPhys, m.ullTotalPhys) : null;
    }

    /// <summary>Free and total bytes of the system drive.</summary>
    public static (long Free, long Total)? Storage()
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            return (drive.AvailableFreeSpace, drive.TotalSize);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Decimal byte formatting like ByteCountFormatter(.file).</summary>
    public static string Bytes(double bytes)
    {
        string[] units = ["bytes", "KB", "MB", "GB", "TB"];
        var unit = 0;
        while (bytes >= 1000 && unit < units.Length - 1)
        {
            bytes /= 1000;
            unit++;
        }

        return unit == 0 ? $"{bytes:0} {units[0]}" : string.Format(CultureInfo.CurrentCulture, "{0:0.#} {1}", bytes, units[unit]);
    }

    // ------------------------------------------------------------------ power

    public static void Sleep() => SetSuspendState(false, false, false);

    public static void Shutdown(string arg) => Run("shutdown.exe", arg + " /t 0");

    private static void Run(string exe, string args)
    {
        try
        {
            Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true });
        }
        catch
        {
            // refused
        }
    }

    // ------------------------------------------------------------------ volume

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorCom
    {
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out nint devices);

        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, nint activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(nint notify);

        [PreserveSig] int UnregisterControlChangeNotify(nint notify);

        [PreserveSig] int GetChannelCount(out uint count);

        [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);

        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);

        [PreserveSig] int GetMasterVolumeLevel(out float level);

        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);

        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);

        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);

        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);

        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);

        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);

        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    private static IAudioEndpointVolume? DefaultRender()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            if (enumerator.GetDefaultAudioEndpoint(0, 1, out var device) != 0)
            {
                return null;
            }

            var iid = typeof(IAudioEndpointVolume).GUID;
            return device.Activate(ref iid, 23, 0, out var obj) == 0 ? obj as IAudioEndpointVolume : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Sets the default output volume (0-100). False when it cannot.</summary>
    public static bool SetVolume(int percent)
    {
        var volume = DefaultRender();
        var ctx = Guid.Empty;
        return volume is not null && volume.SetMasterVolumeLevelScalar(Math.Clamp(percent, 0, 100) / 100f, ref ctx) == 0;
    }

    /// <summary>The mute state of the default output (null when unreadable).</summary>
    public static bool? Muted()
    {
        var volume = DefaultRender();
        return volume is not null && volume.GetMute(out var m) == 0 ? m : null;
    }

    public static bool SetMuted(bool mute)
    {
        var volume = DefaultRender();
        var ctx = Guid.Empty;
        return volume is not null && volume.SetMute(mute, ref ctx) == 0;
    }

    // ------------------------------------------------------------------ brightness

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PhysicalMonitor
    {
        public nint Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
    }

    [DllImport("dxva2.dll")] private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(nint monitor, out uint count);
    [DllImport("dxva2.dll")] private static extern bool GetPhysicalMonitorsFromHMONITOR(nint monitor, uint count, [Out] PhysicalMonitor[] monitors);
    [DllImport("dxva2.dll")] private static extern bool SetMonitorBrightness(nint monitor, uint brightness);
    [DllImport("dxva2.dll")] private static extern bool DestroyPhysicalMonitors(uint count, PhysicalMonitor[] monitors);

    /// <summary>Sets the brightness of the display under the pointer: DDC/CI first, then the laptop panel through WMI.</summary>
    public static bool SetBrightness(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        try
        {
            NativeMethods.GetCursorPos(out var pt);
            var monitor = NativeMethods.MonitorFromPoint(pt, NativeConstants.MONITOR_DEFAULTTONEAREST);
            if (GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var count) && count > 0)
            {
                var list = new PhysicalMonitor[count];
                if (GetPhysicalMonitorsFromHMONITOR(monitor, count, list))
                {
                    var ok = false;
                    foreach (var m in list)
                    {
                        ok |= SetMonitorBrightness(m.Handle, (uint)percent);
                    }

                    DestroyPhysicalMonitors(count, list);
                    if (ok)
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
            // fall through to WMI
        }

        try
        {
            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -Command \"(Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightnessMethods | Invoke-CimMethod -MethodName WmiSetBrightness -Arguments @{{Timeout=1;Brightness={percent}}}) | Out-Null\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            return p is not null && p.WaitForExit(4000) && p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    // ------------------------------------------------------------------ Wi-Fi

    /// <summary>The Wi-Fi radio state (null when there is no Wi-Fi radio).</summary>
    public static async Task<bool?> WifiOnAsync()
    {
        try
        {
            var radios = await Radio.GetRadiosAsync();
            var wifi = radios.FirstOrDefault(r => r.Kind == RadioKind.WiFi);
            return wifi is null ? null : wifi.State == RadioState.On;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<bool> SetWifiAsync(bool on)
    {
        try
        {
            var access = await Radio.RequestAccessAsync();
            if (access != RadioAccessStatus.Allowed)
            {
                return false;
            }

            var radios = await Radio.GetRadiosAsync();
            var wifi = radios.FirstOrDefault(r => r.Kind == RadioKind.WiFi);
            return wifi is not null && await wifi.SetStateAsync(on ? RadioState.On : RadioState.Off) == RadioAccessStatus.Allowed;
        }
        catch
        {
            return false;
        }
    }
}
