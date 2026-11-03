// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Runtime.InteropServices;
using Trayloom.Core;

namespace Trayloom.Features.SystemTools;

/// <summary>Native readings used by the sampler: CPU core classes, memory, boot time, storage bus type, NVIDIA temperature.</summary>
internal static class SystemNative
{
    // ------------------------------------------------------------------------------------------------ CPU classes

    /// <summary>
    /// The class name of every logical processor ("Performance", "Efficiency" or "CPU" when all cores are alike), by logical index, from the
    /// CPU set efficiency classes.
    /// </summary>
    public static string[] CoreClasses(int logicalCount)
    {
        var result = Enumerable.Repeat("CPU", logicalCount).ToArray();
        try
        {
            GetSystemCpuSetInformation(0, 0, out var length, 0, 0);
            if (length == 0)
            {
                return result;
            }

            var buffer = Marshal.AllocHGlobal((int)length);
            try
            {
                if (!GetSystemCpuSetInformation(buffer, length, out length, 0, 0))
                {
                    return result;
                }

                var classes = new List<(int Logical, int Efficiency)>();
                var offset = 0;
                while (offset < length)
                {
                    var size = Marshal.ReadInt32(buffer, offset);
                    if (size <= 0)
                    {
                        break;
                    }

                    var type = Marshal.ReadInt32(buffer, offset + 4);
                    if (type == 0)
                    {
                        var logical = Marshal.ReadByte(buffer, offset + 14);
                        var efficiency = Marshal.ReadByte(buffer, offset + 18);
                        classes.Add((logical, efficiency));
                    }

                    offset += size;
                }

                var distinct = classes.Select(c => c.Efficiency).Distinct().OrderBy(x => x).ToList();
                if (distinct.Count < 2)
                {
                    return result;
                }

                foreach (var (logical, efficiency) in classes)
                {
                    if (logical < result.Length)
                    {
                        var rank = distinct.IndexOf(efficiency);
                        result[logical] = rank == distinct.Count - 1 ? "Performance" : rank == 0 ? "Efficiency" : "Performance";
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception)
        {
        }

        return result;
    }

    // ------------------------------------------------------------------------------------------------ boot time

    /// <summary>Wall-clock uptime from the boot time (not GetTickCount64, which includes Fast Startup drift).</summary>
    public static TimeSpan? Uptime()
    {
        try
        {
            var buffer = Marshal.AllocHGlobal(48);
            try
            {
                if (NtQuerySystemInformation(3, buffer, 48, out _) == 0)
                {
                    var boot = Marshal.ReadInt64(buffer, 0);
                    var current = Marshal.ReadInt64(buffer, 8);
                    if (boot > 0 && current > boot)
                    {
                        return TimeSpan.FromTicks(current - boot);
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception)
        {
        }

        return TimeSpan.FromMilliseconds(Environment.TickCount64);
    }

    // ------------------------------------------------------------------------------------------------ memory

    /// <summary>Physical memory status.</summary>
    public static (ulong Total, ulong Available, ulong PageFileTotal, ulong PageFileAvailable)? Memory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? (status.TotalPhys, status.AvailPhys, status.TotalPageFile, status.AvailPageFile) : null;
    }

    /// <summary>Working set of the "Memory Compression" process (the compressed store), or 0.</summary>
    public static ulong CompressedBytes()
    {
        try
        {
            var processes = Process.GetProcessesByName("Memory Compression");
            ulong total = 0;
            foreach (var p in processes)
            {
                total += (ulong)Math.Max(0, p.WorkingSet64);
                p.Dispose();
            }

            return total;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    // ------------------------------------------------------------------------------------------------ storage

    /// <summary>STORAGE_BUS_TYPE of the volume (7 = USB), or -1 when unknown.</summary>
    public static int BusType(string letter)
    {
        var handle = CreateFileW($"\\\\.\\{letter.TrimEnd('\\')}", 0, 3, 0, 3, 0, 0);
        if (handle == -1 || handle == 0)
        {
            return -1;
        }

        try
        {
            var query = new byte[12];
            var output = new byte[1024];
            if (!DeviceIoControl(handle, 0x2D1400, query, query.Length, output, output.Length, out _, 0))
            {
                return -1;
            }

            return BitConverter.ToInt32(output, 28);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    // ------------------------------------------------------------------------------------------------ NVIDIA

    private static bool _nvmlTried;
    private static nint _nvmlDevice;
    private static NvmlGetTemperature? _nvmlTemp;

    private delegate int NvmlInit();

    private delegate int NvmlGetHandle(uint index, out nint device);

    private delegate int NvmlGetTemperature(nint device, int sensor, out uint temperature);

    /// <summary>GPU temperature through NVML when an NVIDIA driver is installed (no driver of ours is involved), else null.</summary>
    public static double? NvidiaTemperature()
    {
        if (!_nvmlTried)
        {
            _nvmlTried = true;
            try
            {
                string[] candidates = ["nvml.dll", @"C:\Program Files\NVIDIA Corporation\NVSMI\nvml.dll", @"C:\Windows\System32\nvml.dll"];
                foreach (var candidate in candidates)
                {
                    if (!NativeLibrary.TryLoad(candidate, out var lib))
                    {
                        continue;
                    }

                    var init = Marshal.GetDelegateForFunctionPointer<NvmlInit>(NativeLibrary.GetExport(lib, "nvmlInit_v2"));
                    if (init() != 0)
                    {
                        continue;
                    }

                    var getHandle = Marshal.GetDelegateForFunctionPointer<NvmlGetHandle>(NativeLibrary.GetExport(lib, "nvmlDeviceGetHandleByIndex_v2"));
                    if (getHandle(0, out _nvmlDevice) != 0)
                    {
                        continue;
                    }

                    _nvmlTemp = Marshal.GetDelegateForFunctionPointer<NvmlGetTemperature>(NativeLibrary.GetExport(lib, "nvmlDeviceGetTemperature"));
                    break;
                }
            }
            catch (Exception)
            {
                _nvmlTemp = null;
            }
        }

        try
        {
            if (_nvmlTemp is not null && _nvmlTemp(_nvmlDevice, 0, out var t) == 0)
            {
                return t;
            }
        }
        catch (Exception)
        {
        }

        return null;
    }

    // ------------------------------------------------------------------------------------------------ interop

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemCpuSetInformation(nint information, uint bufferLength, out uint returnedLength, nint process, uint flags);

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int informationClass, nint buffer, int length, out int returned);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateFileW(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(nint device, uint code, byte[] input, int inputSize, byte[] output, int outputSize, out int returned, nint overlapped);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
