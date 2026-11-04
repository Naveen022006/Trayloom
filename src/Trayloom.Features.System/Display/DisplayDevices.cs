// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using Trayloom.Core.Native;
using Trayloom.Core;

namespace Trayloom.Features.SystemTools;

/// <summary>A monitor whose brightness can be read and changed (0-100 %).</summary>
internal abstract class DisplayDevice : IDisposable
{
    private readonly object _gate = new();

    public required string Id { get; init; }

    public required string Name { get; init; }

    public required nint HMonitor { get; init; }

    public required bool IsInternal { get; init; }

    /// <summary>Last known brightness (percent).</summary>
    public int Percent { get; protected set; }

    public string Kind => IsInternal ? "Built-in display (WMI)" : "External display (DDC/CI)";

    public bool Write(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        lock (_gate)
        {
            if (!WriteCore(percent))
            {
                return false;
            }

            Percent = percent;
            return true;
        }
    }

    /// <summary>
    /// Takes a level read or announced from outside (brightness keys, another slider) as the current one. Skipped while a write
    /// is running: that write sets the level itself.
    /// </summary>
    public void Observe(int percent)
    {
        if (!Monitor.TryEnter(_gate))
        {
            return;
        }

        try
        {
            Percent = Math.Clamp(percent, 0, 100);
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    public virtual void Dispose()
    {
    }

    protected abstract bool WriteCore(int percent);
}

/// <summary>Internal panel controlled through root\WMI WmiMonitorBrightnessMethods.</summary>
internal sealed class WmiDisplay : DisplayDevice
{
    public WmiDisplay(int current)
    {
        Percent = current;
    }

    protected override bool WriteCore(int percent)
    {
        // marked before and after: the change notification may land after the call returns, and the island must not show it as foreign
        BrightnessWrites.MarkOwn();
        var ok = Wmi.ForEach(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=TRUE", o => Wmi.Invoke(o, "WmiSetBrightness", 1, percent));
        BrightnessWrites.MarkOwn();
        return ok;
    }
}

/// <summary>External monitor controlled through DDC/CI (dxva2 high-level monitor configuration API).</summary>
internal sealed class DdcDisplay : DisplayDevice
{
    private readonly nint _handle;
    private readonly uint _min;
    private readonly uint _max;
    private readonly DisplayNative.PhysicalMonitor[] _owner;

    public DdcDisplay(DisplayNative.PhysicalMonitor[] owner, nint handle, uint min, uint current, uint max)
    {
        _owner = owner;
        _handle = handle;
        _min = min;
        _max = max;
        Percent = ToPercent(current);
    }

    public override void Dispose()
    {
        if (_owner.Length > 0 && _owner[0].Handle == _handle)
        {
            DisplayNative.DestroyPhysicalMonitors((uint)_owner.Length, _owner);
        }
    }

    protected override bool WriteCore(int percent)
    {
        var value = _min + (uint)Math.Round((_max - _min) * percent / 100.0);
        return DisplayNative.SetMonitorBrightness(_handle, value);
    }

    private int ToPercent(uint value) => _max <= _min ? 0 : (int)Math.Round((value - _min) * 100.0 / (_max - _min));
}

internal static class DisplayNative
{
    private const uint QdcOnlyActivePaths = 2;

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)]
    public struct PhysicalMonitor
    {
        public nint Handle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
    }

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

    /// <summary>True when a non-internal display is attached and active.</summary>
    public static bool HasExternalDisplay()
    {
        try
        {
            var targets = QueryTargets();
            if (targets.Count > 0)
            {
                return targets.Values.Any(t => !t.Internal);
            }
        }
        catch (Exception)
        {
        }

        return GetSystemMetrics(80) > 1;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    /// <summary>Physical-pixel bounds of the monitor with the GDI device name (e.g. \.\DISPLAY1), or null when it is not found.</summary>
    public static (nint Handle, RECT Bounds)? FindMonitor(string gdiName)
    {
        (nint, RECT)? found = null;
        EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (GetMonitorInfoEx(monitor, ref info) && string.Equals(info.Device, gdiName, StringComparison.OrdinalIgnoreCase))
            {
                found = (monitor, info.Monitor);
                return false;
            }

            return true;
        }, 0);
        return found;
    }

    private delegate bool MonitorEnumProc(nint monitor, nint dc, nint rect, nint data);

    /// <summary>Lists every adjustable display (internal panels through WMI, external ones through DDC/CI). Slow: call off the UI thread.</summary>
    public static List<DisplayDevice> Enumerate()
    {
        var result = new List<DisplayDevice>();
        var config = QueryTargets();
        var monitors = new List<(nint Handle, string Device)>();
        EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (GetMonitorInfoEx(monitor, ref info))
            {
                monitors.Add((monitor, info.Device));
            }

            return true;
        }, 0);

        var internalBrightness = Wmi.Query(@"root\WMI", "SELECT CurrentBrightness, Active FROM WmiMonitorBrightness", "CurrentBrightness", "Active")
            .Where(r => r["Active"] is not false)
            .Select(r => Wmi.ToInt(r["CurrentBrightness"], -1))
            .Where(b => b >= 0)
            .ToList();
        var internalAssigned = false;

        foreach (var (handle, device) in monitors)
        {
            config.TryGetValue(device, out var target);
            var isInternal = target.Internal;
            var friendly = string.IsNullOrWhiteSpace(target.Name)
                ? (isInternal ? Loc.T("Built-in display") : device.TrimStart('\\', '.'))
                : target.Name;
            if (DisplayMath.AssumeInternalWithoutTopology(config.Count, monitors.Count, internalBrightness.Count, internalAssigned))
            {
                // Display topology unavailable: a lone monitor is the laptop panel; with several, guessing could credit WMI writes to an external screen.
                isInternal = true;
            }

            if (isInternal && internalBrightness.Count > 0 && !internalAssigned)
            {
                internalAssigned = true;
                result.Add(new WmiDisplay(internalBrightness[0]) { Id = device, Name = friendly, HMonitor = handle, IsInternal = true });
                continue;
            }

            if (TryCreateDdc(handle, device, friendly, isInternal) is { } ddc)
            {
                result.AddRange(ddc);
            }
        }

        return result;
    }

    private static List<DisplayDevice>? TryCreateDdc(nint handle, string device, string friendly, bool isInternal)
    {
        try
        {
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(handle, out var count) || count == 0)
            {
                return null;
            }

            var physical = new PhysicalMonitor[count];
            if (!GetPhysicalMonitorsFromHMONITOR(handle, count, physical))
            {
                return null;
            }

            var list = new List<DisplayDevice>();
            var anyUsable = false;
            for (var i = 0; i < count; i++)
            {
                if (GetMonitorBrightness(physical[i].Handle, out var min, out var current, out var max) && max > min)
                {
                    anyUsable = true;
                    var name = count > 1 ? $"{friendly} ({i + 1})" : friendly;
                    list.Add(new DdcDisplay(physical, physical[i].Handle, min, current, max) { Id = $"{device}#{i}", Name = name, HMonitor = handle, IsInternal = isInternal });
                }
            }

            if (!anyUsable)
            {
                DestroyPhysicalMonitors(count, physical);
                return null;
            }

            return list;
        }
        catch (Exception)
        {
            return null;
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

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorEnumProc callback, nint data);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoEx(nint monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, byte[] paths, ref uint modeCount, byte[] modes, nint topology);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(byte[] packet);

    [DllImport("dxva2.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(nint monitor, out uint count);

    [DllImport("dxva2.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(nint monitor, uint count, [Out] PhysicalMonitor[] monitors);

    [DllImport("dxva2.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorBrightness(nint monitor, out uint min, out uint current, out uint max);

    [DllImport("dxva2.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetMonitorBrightness(nint monitor, uint brightness);

    [DllImport("dxva2.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyPhysicalMonitors(uint count, [In] PhysicalMonitor[] monitors);
}
