// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Runtime.InteropServices;
using Trayloom.Core;
using Trayloom.Core.Native;

namespace Trayloom.Features.Island;

/// <summary>
/// The connected displays as <see cref="IslandMonitor"/> records: bounds, work area (an auto-hide taskbar's edge excluded), DPI scale, a stable key
/// and a friendly name. Plain Win32 calls, safe on any thread; callers cache the list and refresh it when the display configuration changes.
/// </summary>
internal static class IslandDisplays
{
    private const uint QdcOnlyActivePaths = 2;
    private const uint EddGetDeviceInterfaceName = 1;
    private const uint AbmGetAutoHideBarEx = 0x0B;

    /// <summary>The auto-hide bar overlaps its edge by this much while hidden; the work area keeps that strip free so the island is not under it.</summary>
    private const int AutoHideStrip = 2;

    /// <summary>Every display, the main one first, the others left to right then top to bottom. Never empty (without a screen the primary metrics stand in for one).</summary>
    public static IReadOnlyList<IslandMonitor> Enumerate()
    {
        var list = new List<IslandMonitor>();
        try
        {
            var targets = QueryTargets();
            var handles = new List<nint>();
            EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
            {
                handles.Add(monitor);
                return true;
            }, 0);

            foreach (var handle in handles)
            {
                var monitor = Describe(handle, targets, list.Count + 1);
                if (monitor is not null)
                {
                    list.Add(monitor);
                }
            }
        }
        catch (Exception)
        {
            // fall through to whatever was found, or to the primary metrics
        }

        if (list.Count == 0)
        {
            return [Fallback()];
        }

        return
        [
            .. list.Where(m => m.IsPrimary).Take(1),
            .. list.Where(m => !m.IsPrimary).OrderBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top),
        ];
    }

    private static IslandMonitor? Describe(nint handle, Dictionary<string, (string Name, bool Internal)> targets, int ordinal)
    {
        try
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (!GetMonitorInfoEx(handle, ref info))
            {
                return null;
            }

            var gdi = info.Device ?? "";
            targets.TryGetValue(gdi, out var target);
            var key = InterfacePath(gdi);
            var number = GdiNumber(gdi) ?? ordinal;
            var name = !string.IsNullOrWhiteSpace(target.Name) ? target.Name.Trim()
                : target.Internal ? Loc.T("Built-in display")
                : Loc.F("Display {0}", number);
            return new IslandMonitor(
                key.Length > 0 ? key : gdi,
                IslandPlacement.HardwareIdOf(key),
                gdi,
                name,
                (info.Flags & 1) != 0,
                target.Internal,
                info.Monitor,
                AutoHideAdjusted(info.Monitor, info.Work),
                Scale(handle),
                handle);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IslandMonitor Fallback()
    {
        var w = Math.Max(800, GetSystemMetrics(0));
        var h = Math.Max(600, GetSystemMetrics(1));
        var all = new RECT { Left = 0, Top = 0, Right = w, Bottom = h };
        return new IslandMonitor(@"\\.\DISPLAY1", "", @"\\.\DISPLAY1", Loc.F("Display {0}", 1), true, false, all, all, 1.0, 0);
    }

    /// <summary>Effective DPI scale (1.0 = 96 dpi) of a monitor.</summary>
    private static double Scale(nint monitor)
    {
        try
        {
            if (GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 && dpiX > 0)
            {
                return dpiX / 96.0;
            }
        }
        catch (Exception)
        {
            // older systems: fall through
        }

        return 1.0;
    }

    /// <summary>The stable monitor id: the device interface path Windows reports for the display attached to the GDI device (empty when it has none).</summary>
    private static string InterfacePath(string gdiName)
    {
        try
        {
            var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            if (EnumDisplayDevicesW(gdiName, 0, ref device, EddGetDeviceInterfaceName))
            {
                return device.DeviceId?.Trim() ?? "";
            }
        }
        catch (Exception)
        {
            // no stable id: the GDI name stands in
        }

        return "";
    }

    /// <summary>The 5 of <c>\\.\DISPLAY5</c>, for the "Display 5" fallback name.</summary>
    private static int? GdiNumber(string gdiName)
    {
        const string prefix = "DISPLAY";
        var at = gdiName.LastIndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        return at >= 0 && int.TryParse(gdiName.AsSpan(at + prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    /// <summary>
    /// An auto-hide taskbar reserves nothing, so the work area reaches the screen edge it slides in from; the work area is pulled in by a
    /// couple of pixels on that edge so the island never sits in the strip that reveals the taskbar.
    /// </summary>
    private static RECT AutoHideAdjusted(RECT bounds, RECT work)
    {
        try
        {
            var result = work;
            for (uint edge = 0; edge < 4; edge++)
            {
                var data = new AppBarData { Size = Marshal.SizeOf<AppBarData>(), Edge = edge, Rect = bounds };
                if (SHAppBarMessage(AbmGetAutoHideBarEx, ref data) == 0)
                {
                    continue;
                }

                switch (edge)
                {
                    case 0 when result.Left <= bounds.Left:
                        result.Left = bounds.Left + AutoHideStrip;
                        break;
                    case 1 when result.Top <= bounds.Top:
                        result.Top = bounds.Top + AutoHideStrip;
                        break;
                    case 2 when result.Right >= bounds.Right:
                        result.Right = bounds.Right - AutoHideStrip;
                        break;
                    case 3 when result.Bottom >= bounds.Bottom:
                        result.Bottom = bounds.Bottom - AutoHideStrip;
                        break;
                }
            }

            return result.Width > 0 && result.Height > 0 ? result : work;
        }
        catch (Exception)
        {
            return work;
        }
    }

    /// <summary>Maps GDI device names (\\.\DISPLAY1) to the friendly monitor name and whether the output is an internal panel.</summary>
    private static Dictionary<string, (string Name, bool Internal)> QueryTargets()
    {
        var map = new Dictionary<string, (string, bool)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount) != 0 || pathCount == 0)
            {
                return map;
            }

            var paths = new byte[pathCount * 72];
            var modes = new byte[Math.Max(1, modeCount) * 64];
            if (QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, 0) != 0)
            {
                return map;
            }

            for (var i = 0; i < pathCount; i++)
            {
                var o = i * 72;
                var sourceAdapter = BitConverter.ToInt64(paths, o);
                var sourceId = BitConverter.ToUInt32(paths, o + 8);
                var targetAdapter = BitConverter.ToInt64(paths, o + 20);
                var targetId = BitConverter.ToUInt32(paths, o + 28);
                var tech = BitConverter.ToUInt32(paths, o + 36);

                var source = new byte[84];
                WriteHeader(source, 1, 84, sourceAdapter, sourceId);
                if (DisplayConfigGetDeviceInfo(source) != 0)
                {
                    continue;
                }

                var gdi = System.Text.Encoding.Unicode.GetString(source, 20, 64).TrimEnd('\0');

                var target = new byte[420];
                WriteHeader(target, 2, 420, targetAdapter, targetId);
                var name = "";
                if (DisplayConfigGetDeviceInfo(target) == 0)
                {
                    name = System.Text.Encoding.Unicode.GetString(target, 36, 128).TrimEnd('\0');
                }

                // INTERNAL (0x80000000), DISPLAYPORT_EMBEDDED (11), UDI_EMBEDDED (13)
                var isInternal = tech is 0x80000000 or 11 or 13;
                map[gdi] = (name, isInternal);
            }
        }
        catch (Exception)
        {
        }

        return map;
    }

    private static void WriteHeader(byte[] packet, int type, int size, long adapter, uint id)
    {
        BitConverter.TryWriteBytes(packet.AsSpan(0, 4), type);
        BitConverter.TryWriteBytes(packet.AsSpan(4, 4), size);
        BitConverter.TryWriteBytes(packet.AsSpan(8, 8), adapter);
        BitConverter.TryWriteBytes(packet.AsSpan(16, 4), id);
    }

    private delegate bool MonitorEnumProc(nint monitor, nint dc, nint rect, nint data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public RECT Monitor;
        public RECT Work;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Device;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public int Size;
        public nint Hwnd;
        public uint CallbackMessage;
        public uint Edge;
        public RECT Rect;
        public nint Param;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoEx(nint monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevicesW(string? device, uint index, ref DisplayDevice displayDevice, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, byte[] paths, ref uint modeCount, byte[] modes, nint topology);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(byte[] packet);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("shell32.dll")]
    private static extern nint SHAppBarMessage(uint message, ref AppBarData data);
}
