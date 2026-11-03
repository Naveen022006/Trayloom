// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Text;
using Trayloom.Core;

namespace Trayloom.Features.SystemTools;

/// <summary>A connected display (active or switched off) as the display configuration reports it.</summary>
/// <param name="GdiName">GDI device name of its source (\\.\DISPLAY1); only known while active.</param>
/// <param name="Name">Friendly monitor name.</param>
/// <param name="IsBuiltIn">Internal / embedded panel.</param>
/// <param name="IsActive">Part of the desktop.</param>
/// <param name="TargetId">Display target id (stable per output).</param>
/// <param name="Adapter">Adapter LUID of the target.</param>
internal sealed record DisplayInfo(string GdiName, string Name, bool IsBuiltIn, bool IsActive, uint TargetId, long Adapter)
{
    public string Key => $"{Adapter:X}:{TargetId}";
}

/// <summary>Display topology queries and the per-display switch on / off (SetDisplayConfig).</summary>
internal static class DisplayTopology
{
    private const uint QdcAllPaths = 1;
    private const uint QdcOnlyActivePaths = 2;
    private const uint SdcApply = 0x80;
    private const uint SdcUseSuppliedDisplayConfig = 0x20;
    private const uint SdcAllowChanges = 0x400;
    private const uint SdcTopologyExtend = 0x4;
    private const int PathSize = 72;
    private const int ModeSize = 64;

    /// <summary>Lists every connected display (active ones first) with its power state.</summary>
    public static List<DisplayInfo> Enumerate()
    {
        var result = new List<DisplayInfo>();
        try
        {
            if (GetDisplayConfigBufferSizes(QdcAllPaths, out var pathCount, out var modeCount) != 0 || pathCount == 0)
            {
                return result;
            }

            var paths = new byte[pathCount * PathSize];
            var modes = new byte[Math.Max(1, modeCount) * ModeSize];
            if (QueryDisplayConfig(QdcAllPaths, ref pathCount, paths, ref modeCount, modes, 0) != 0)
            {
                return result;
            }

            var seen = new HashSet<string>();
            for (var i = 0; i < pathCount; i++)
            {
                var o = i * PathSize;
                var targetAdapter = BitConverter.ToInt64(paths, o + 20);
                var targetId = BitConverter.ToUInt32(paths, o + 28);
                var tech = BitConverter.ToUInt32(paths, o + 36);
                var available = BitConverter.ToUInt32(paths, o + 64) != 0;
                var flags = BitConverter.ToUInt32(paths, o + 68);
                var active = (flags & 1) != 0;
                if (!available && !active)
                {
                    continue;
                }

                var key = $"{targetAdapter:X}:{targetId}";
                if (!seen.Add(key) && !active)
                {
                    continue;
                }

                var target = new byte[420];
                WriteHeader(target, 2, 420, targetAdapter, targetId);
                var name = string.Empty;
                if (DisplayConfigGetDeviceInfo(target) == 0)
                {
                    name = Encoding.Unicode.GetString(target, 36, 128).TrimEnd('\0');
                }

                var gdi = string.Empty;
                if (active)
                {
                    var sourceAdapter = BitConverter.ToInt64(paths, o);
                    var sourceId = BitConverter.ToUInt32(paths, o + 8);
                    var source = new byte[84];
                    WriteHeader(source, 1, 84, sourceAdapter, sourceId);
                    if (DisplayConfigGetDeviceInfo(source) == 0)
                    {
                        gdi = Encoding.Unicode.GetString(source, 20, 64).TrimEnd('\0');
                    }
                }

                var internalPanel = tech is 0x80000000 or 11 or 13;
                var info = new DisplayInfo(gdi, string.IsNullOrWhiteSpace(name) ? (internalPanel ? Loc.T("Built-in display") : Loc.T("Display")) : name, internalPanel, active, targetId, targetAdapter);
                var existing = result.FindIndex(r => r.Key == key);
                if (existing >= 0)
                {
                    if (active)
                    {
                        result[existing] = info;
                    }
                }
                else
                {
                    result.Add(info);
                }
            }
        }
        catch (Exception)
        {
        }

        return [.. result.OrderByDescending(r => r.IsActive).ThenByDescending(r => r.IsBuiltIn)];
    }

    /// <summary>Removes the display's path from the active configuration (switches it off). False when it fails.</summary>
    public static bool SwitchOff(DisplayInfo display)
    {
        try
        {
            if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount) != 0 || pathCount < 2)
            {
                return false;
            }

            var paths = new byte[pathCount * PathSize];
            var modes = new byte[Math.Max(1, modeCount) * ModeSize];
            if (QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, 0) != 0)
            {
                return false;
            }

            var kept = new List<byte[]>();
            for (var i = 0; i < pathCount; i++)
            {
                var path = new byte[PathSize];
                Array.Copy(paths, i * PathSize, path, 0, PathSize);
                if (BitConverter.ToInt64(path, 20) == display.Adapter && BitConverter.ToUInt32(path, 28) == display.TargetId)
                {
                    continue;
                }

                kept.Add(path);
            }

            if (kept.Count == 0 || kept.Count == pathCount)
            {
                return false;
            }

            var buffer = new byte[kept.Count * PathSize];
            for (var i = 0; i < kept.Count; i++)
            {
                Array.Copy(kept[i], 0, buffer, i * PathSize, PathSize);
            }

            return SetDisplayConfig((uint)kept.Count, buffer, modeCount, modes, SdcApply | SdcUseSuppliedDisplayConfig | SdcAllowChanges) == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Re-extends the desktop to every connected display (switches the displays that are off back on).</summary>
    public static bool SwitchOn()
    {
        try
        {
            return SetDisplayConfig(0, null, 0, null, SdcApply | SdcTopologyExtend) == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void WriteHeader(byte[] packet, int type, int size, long adapter, uint id)
    {
        BitConverter.TryWriteBytes(packet.AsSpan(0, 4), type);
        BitConverter.TryWriteBytes(packet.AsSpan(4, 4), size);
        BitConverter.TryWriteBytes(packet.AsSpan(8, 8), adapter);
        BitConverter.TryWriteBytes(packet.AsSpan(16, 4), id);
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, byte[] paths, ref uint modeCount, byte[] modes, nint topology);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(byte[] packet);

    [DllImport("user32.dll")]
    private static extern int SetDisplayConfig(uint pathCount, byte[]? paths, uint modeCount, byte[]? modes, uint flags);
}
