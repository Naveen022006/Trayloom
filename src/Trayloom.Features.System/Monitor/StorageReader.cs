// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using Windows.Devices.Enumeration;

namespace Trayloom.Features.SystemTools;

/// <summary>Disk health (the storage reliability counters) and the drive eject sequence.</summary>
internal static class StorageReader
{
    /// <summary>SMART-style health per volume letter ("C:"). Fields the drive or Windows does not expose stay null.</summary>
    public static Dictionary<string, SmartInfo> ReadSmart(IReadOnlyList<VolumeInfo> volumes)
    {
        var result = new Dictionary<string, SmartInfo>(StringComparer.OrdinalIgnoreCase);
        try
        {
            const string ns = @"root\Microsoft\Windows\Storage";
            var partitions = Wmi.Query(ns, "SELECT DriveLetter, DiskNumber FROM MSFT_Partition", "DriveLetter", "DiskNumber");
            var disks = Wmi.Query(ns, "SELECT DeviceId, HealthStatus FROM MSFT_PhysicalDisk", "DeviceId", "HealthStatus");
            var counters = Wmi.Query(ns, "SELECT DeviceId, Temperature, Wear, PowerOnHours, StartStopCycleCount FROM MSFT_StorageReliabilityCounter", "DeviceId", "Temperature", "Wear", "PowerOnHours", "StartStopCycleCount");
            foreach (var partition in partitions)
            {
                var letterValue = Wmi.ToInt(partition["DriveLetter"]);
                if (letterValue < 65)
                {
                    continue;
                }

                var letter = $"{(char)letterValue}:";
                if (volumes.All(v => !string.Equals(v.Letter, letter, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var disk = Wmi.ToInt(partition["DiskNumber"], -1).ToString();
                var health = disks.FirstOrDefault(d => Convert.ToString(d["DeviceId"]) == disk);
                var counter = counters.FirstOrDefault(d => Convert.ToString(d["DeviceId"]) == disk);
                var status = health is null ? "Unknown" : Wmi.ToInt(health["HealthStatus"], -1) switch { 0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", _ => "Unknown" };
                string? Number(string key, string suffix = "")
                {
                    if (counter is null || counter[key] is null)
                    {
                        return null;
                    }

                    var v = Wmi.ToInt(counter[key], -1);
                    return v < 0 ? null : v + suffix;
                }

                var wear = Number("Wear");
                var healthPercent = wear is not null && int.TryParse(wear, out var w) ? $"{Math.Clamp(100 - w, 0, 100)}%" : null;
                var temperature = Number("Temperature", " °C");
                result[letter] = new SmartInfo(status, null, null, temperature is "0 °C" ? null : temperature, healthPercent, Number("StartStopCycleCount"), Number("PowerOnHours"));
            }
        }
        catch (Exception)
        {
        }

        return result;
    }

    // ------------------------------------------------------------------------------------------------ eject

    /// <summary>Locks, dismounts and ejects the volume (<c>E:</c>). Returns false when it is in use or cannot be ejected.</summary>
    public static bool Eject(string letter)
    {
        var path = $"\\\\.\\{letter.TrimEnd('\\')}";
        var handle = CreateFileW(path, 0xC0000000, 3, 0, 3, 0, 0);
        if (handle == -1 || handle == 0)
        {
            return false;
        }

        try
        {
            var ok = false;
            for (var i = 0; i < 4 && !ok; i++)
            {
                ok = DeviceIoControl(handle, 0x90018, 0, 0, 0, 0, out _, 0);
                if (!ok)
                {
                    Thread.Sleep(250);
                }
            }

            if (!ok)
            {
                return false;
            }

            DeviceIoControl(handle, 0x90020, 0, 0, 0, 0, out _, 0);
            var prevent = new byte[] { 0 };
            var handlePrevent = GCHandle.Alloc(prevent, GCHandleType.Pinned);
            try
            {
                DeviceIoControl(handle, 0x2D4804, handlePrevent.AddrOfPinnedObject(), 1, 0, 0, out _, 0);
            }
            finally
            {
                handlePrevent.Free();
            }

            return DeviceIoControl(handle, 0x2D4808, 0, 0, 0, 0, out _, 0);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateFileW(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(nint device, uint code, nint input, int inputSize, nint output, int outputSize, out int returned, nint overlapped);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}

/// <summary>Battery levels of connected Bluetooth peripherals (keyboards, mice, headphones) through device enumeration.</summary>
internal static class PeripheralReader
{
    private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

    public static IReadOnlyList<PeripheralBattery> Read()
    {
        var list = new List<PeripheralBattery>();
        try
        {
            // The battery level lives on the Bluetooth device node (not the association endpoint).
            var task = DeviceInformation.FindAllAsync(string.Empty, [BatteryKey, "System.Devices.Connected"], DeviceInformationKind.Device).AsTask();
            if (!task.Wait(20000))
            {
                return list;
            }

            foreach (var device in task.Result)
            {
                if (!device.Properties.TryGetValue(BatteryKey, out var value) || value is null)
                {
                    continue;
                }

                if (device.Properties.TryGetValue("System.Devices.Connected", out var connected) && connected is false)
                {
                    continue;
                }

                var percent = Convert.ToInt32(value);
                if (percent is < 0 or > 100)
                {
                    continue;
                }

                var name = device.Name.Replace(" Hands-Free AG", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
                if (list.All(x => x.Name != name))
                {
                    list.Add(new PeripheralBattery(name, KindOf(name), percent));
                }
            }
        }
        catch (Exception)
        {
        }

        return list;
    }

    private static string KindOf(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("keyboard") || n.Contains("keys"))
        {
            return "keyboard";
        }

        if (n.Contains("mouse") || n.Contains("mx master") || n.Contains("anywhere"))
        {
            return "mouse";
        }

        if (n.Contains("trackpad") || n.Contains("touchpad"))
        {
            return "trackpad";
        }

        if (n.Contains("head") || n.Contains("airpods") || n.Contains("buds") || n.Contains("earbuds") || n.Contains("wh-"))
        {
            return "audio";
        }

        return "other";
    }
}
