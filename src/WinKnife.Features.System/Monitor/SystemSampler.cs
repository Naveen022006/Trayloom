// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace WinKnife.Features.SystemTools;

/// <summary>
/// Collects the system metrics (spec 05 F1). Not thread safe: the hub calls <see cref="Sample"/> from one timer thread at a time. Counters
/// exist only for the metrics requested; slow-changing readings (volumes, USB devices, SMART, peripherals, temperatures) are cached per
/// their own period and carried between reads.
/// </summary>
internal sealed class SystemSampler : IDisposable
{
    private static readonly string[] VirtualNic =
    [
        "loopback", "isatap", "teredo", "pseudo", "virtual", "vmware", "hyper-v", "vethernet", "miniport", "vpn", "tap-", "tunnel",
        "bluetooth", "6to4", "npcap", "wintun", "wireguard", "tailscale", "zerotier", "docker",
    ];

    private PdhQuery? _query;
    private Needs _queryNeeds;
    private nint _cpuTotal;
    private nint _cpuCores;
    private nint _gpu;
    private nint _netIn;
    private nint _netOut;
    private nint _diskRead;
    private nint _diskWrite;
    private nint _swap;
    private nint _standby1;
    private nint _standby2;
    private nint _standby3;
    private nint _modified;
    private DateTime _last = DateTime.MinValue;
    private double _totalDown;
    private double _totalUp;
    private string[] _coreClasses = [];

    private DateTime _volumesAt = DateTime.MinValue;
    private IReadOnlyList<VolumeInfo> _volumes = [];
    private readonly Dictionary<string, bool> _external = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<UsbDevice> _devices = [];
    private DateTime _powerAt = DateTime.MinValue;
    private PowerInfo? _power;
    private DateTime _tempAt = DateTime.MinValue;
    private double? _cpuTemp;
    private double? _gpuTemp;
    private bool _tempWmiFailed;
    private DateTime _compressedAt = DateTime.MinValue;
    private ulong _compressed;
    private IReadOnlyList<PeripheralBattery> _peripherals = [];
    private Dictionary<string, SmartInfo> _smart = [];
    private bool _batteryHealthRead;
    private int? _health;
    private int? _cycles;

    private readonly Dictionary<string, (DateTime At, int Running)> _kick = [];
    private readonly object _kickGate = new();

    /// <summary>Runs a slow reader (WMI, device enumeration) on the thread pool at most once per period; the sample uses its last result.</summary>
    private void Kick(string key, TimeSpan period, DateTime now, Action work)
    {
        lock (_kickGate)
        {
            var (at, running) = _kick.GetValueOrDefault(key);
            if (running == 1 || (at != default && now - at < period))
            {
                return;
            }

            _kick[key] = (now, 1);
        }

        Task.Run(() =>
        {
            try
            {
                work();
            }
            catch (Exception)
            {
            }
            finally
            {
                lock (_kickGate)
                {
                    _kick[key] = (_kick.GetValueOrDefault(key).At, 0);
                }
            }
        });
    }

    /// <summary>Reads the metrics named by <paramref name="needs"/>. <paramref name="background"/> relaxes the cadence of slow readings.</summary>
    public MonitorSnapshot Sample(Needs needs, bool background)
    {
        var now = DateTime.UtcNow;
        var snap = new MonitorSnapshot { At = now };
        EnsureQuery(needs);
        _query?.Collect();
        var dt = _last == DateTime.MinValue ? 0 : Math.Max(0.1, (now - _last).TotalSeconds);
        _last = now;

        if (needs.HasFlag(Needs.Cpu))
        {
            snap.Cpu = _query?.Value(_cpuTotal) is { } c ? Math.Clamp(c, 0, 100) : null;
            if (needs.HasFlag(Needs.CpuCores) && _query is not null)
            {
                var count = Environment.ProcessorCount;
                var cores = new double[count];
                foreach (var (instance, value) in _query.Array(_cpuCores))
                {
                    // Instance names are "group,core" ("0,3"); the totals are "_Total" / "0,_Total".
                    var comma = instance.LastIndexOf(',');
                    var token = comma >= 0 ? instance[(comma + 1)..] : instance;
                    if (int.TryParse(token, out var index) && index >= 0 && index < count)
                    {
                        cores[index] = Math.Clamp(value, 0, 100);
                    }
                }

                snap.Cores = cores;
                if (_coreClasses.Length != count)
                {
                    _coreClasses = SystemNative.CoreClasses(count);
                }

                snap.CoreClasses = _coreClasses;
            }
        }

        if (needs.HasFlag(Needs.CpuTemp) || needs.HasFlag(Needs.GpuTemp))
        {
            var wantCpu = needs.HasFlag(Needs.CpuTemp);
            var wantGpu = needs.HasFlag(Needs.GpuTemp);
            Kick("temps", TimeSpan.FromSeconds(background ? 15 : 5), now, () =>
            {
                if (wantCpu)
                {
                    _cpuTemp = ReadCpuTemperature();
                }

                if (wantGpu)
                {
                    _gpuTemp = SystemNative.NvidiaTemperature();
                }

                _tempAt = DateTime.UtcNow;
            });
            snap.CpuTemp = _cpuTemp;
            snap.GpuTemp = _gpuTemp;
            snap.TempReadAt = _tempAt;
        }

        if (needs.HasFlag(Needs.Gpu))
        {
            snap.GpuAvailable = _gpu != 0;
            if (_query is not null && _gpu != 0)
            {
                // Task-manager style: sum the utilisation of every process per engine, then take the busiest engine.
                var perEngine = new Dictionary<string, double>();
                foreach (var (instance, value) in _query.Array(_gpu))
                {
                    var at = instance.IndexOf("_luid_", StringComparison.Ordinal);
                    var key = at >= 0 ? instance[at..] : instance;
                    perEngine[key] = perEngine.GetValueOrDefault(key) + value;
                }

                snap.Gpu = perEngine.Count == 0 ? 0 : Math.Clamp(perEngine.Values.Max(), 0, 100);
            }
        }

        if (needs.HasFlag(Needs.Memory))
        {
            snap.Memory = ReadMemory(now);
        }

        if (needs.HasFlag(Needs.Network) && _query is not null)
        {
            double down = 0;
            double up = 0;
            foreach (var (instance, value) in _query.Array(_netIn))
            {
                if (IsPhysical(instance))
                {
                    down += value;
                }
            }

            foreach (var (instance, value) in _query.Array(_netOut))
            {
                if (IsPhysical(instance))
                {
                    up += value;
                }
            }

            snap.NetDown = down;
            snap.NetUp = up;
            snap.NetKnown = _netIn != 0;
            if (dt > 0)
            {
                _totalDown += down * dt;
                _totalUp += up * dt;
            }

            snap.NetTotalDown = _totalDown;
            snap.NetTotalUp = _totalUp;
        }

        if (needs.HasFlag(Needs.Disk) && _query is not null)
        {
            var byLetter = new Dictionary<string, (double, double)>(StringComparer.OrdinalIgnoreCase);
            foreach (var (instance, value) in _query.Array(_diskRead))
            {
                AddDisk(byLetter, instance, value, 0);
            }

            foreach (var (instance, value) in _query.Array(_diskWrite))
            {
                AddDisk(byLetter, instance, 0, value);
            }

            snap.DiskByLetter = byLetter.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);
            snap.DiskRead = byLetter.Values.Sum(v => v.Item1);
            snap.DiskWrite = byLetter.Values.Sum(v => v.Item2);
        }

        if (needs.HasFlag(Needs.DiskSpace))
        {
            var period = background ? 10 : 5;
            if ((now - _volumesAt).TotalSeconds >= period)
            {
                _volumesAt = now;
                _volumes = ReadVolumes();
            }

            snap.Volumes = _volumes;
        }

        if (needs.HasFlag(Needs.Smart) && _volumes.Count > 0)
        {
            var volumes = _volumes;
            Kick("smart", TimeSpan.FromSeconds(60), now, () => _smart = StorageReader.ReadSmart(volumes));
        }

        snap.Smart = _smart;

        if (needs.HasFlag(Needs.Power))
        {
            var period = background ? 15 : 2;
            if (_power is null || (now - _powerAt).TotalSeconds >= period)
            {
                _powerAt = now;
                _power = ReadPower(needs.HasFlag(Needs.PowerDraw));
            }

            snap.Power = _power;
        }

        if (needs.HasFlag(Needs.Peripherals))
        {
            Kick("peripherals", TimeSpan.FromSeconds(background ? 60 : 15), now, () => _peripherals = PeripheralReader.Read());
        }

        snap.Peripherals = _peripherals;

        if (needs.HasFlag(Needs.Devices))
        {
            Kick("devices", TimeSpan.FromSeconds(background ? 10 : 2), now, () => _devices = ReadUsbDevices());

            snap.Devices = _devices;
        }

        if (needs.HasFlag(Needs.Uptime))
        {
            snap.Uptime = SystemNative.Uptime();
        }

        return snap;
    }

    /// <summary>Forgets cached slow data (a surface became visible and wants fresh numbers).</summary>
    public void Invalidate()
    {
        _volumesAt = _powerAt = DateTime.MinValue;
        lock (_kickGate)
        {
            _kick.Clear();
        }
    }

    public void Dispose()
    {
        _query?.Dispose();
        _query = null;
    }

    // ------------------------------------------------------------------------------------------------ PDH

    private void EnsureQuery(Needs needs)
    {
        var wanted = needs & (Needs.Cpu | Needs.CpuCores | Needs.Gpu | Needs.Network | Needs.Disk | Needs.Memory);
        if (wanted == _queryNeeds && (_query is not null || wanted == Needs.None))
        {
            return;
        }

        _query?.Dispose();
        _queryNeeds = wanted;
        _cpuTotal = _cpuCores = _gpu = _netIn = _netOut = _diskRead = _diskWrite = _swap = _standby1 = _standby2 = _standby3 = _modified = 0;
        if (wanted == Needs.None)
        {
            _query = null;
            return;
        }

        _query = new PdhQuery();
        if (wanted.HasFlag(Needs.Cpu))
        {
            _cpuTotal = _query.Add(@"\Processor Information(_Total)\% Processor Utility");
            if (_cpuTotal == 0)
            {
                _cpuTotal = _query.Add(@"\Processor(_Total)\% Processor Time");
            }

            if (wanted.HasFlag(Needs.CpuCores))
            {
                _cpuCores = _query.Add(@"\Processor Information(*)\% Processor Utility");
                if (_cpuCores == 0)
                {
                    _cpuCores = _query.Add(@"\Processor(*)\% Processor Time");
                }
            }
        }

        if (wanted.HasFlag(Needs.Gpu))
        {
            _gpu = _query.Add(@"\GPU Engine(*)\Utilization Percentage");
        }

        if (wanted.HasFlag(Needs.Network))
        {
            _netIn = _query.Add(@"\Network Interface(*)\Bytes Received/sec");
            _netOut = _query.Add(@"\Network Interface(*)\Bytes Sent/sec");
        }

        if (wanted.HasFlag(Needs.Disk))
        {
            _diskRead = _query.Add(@"\PhysicalDisk(*)\Disk Read Bytes/sec");
            _diskWrite = _query.Add(@"\PhysicalDisk(*)\Disk Write Bytes/sec");
        }

        if (wanted.HasFlag(Needs.Memory))
        {
            _swap = _query.Add(@"\Paging File(_Total)\% Usage");
            _standby1 = _query.Add(@"\Memory\Standby Cache Normal Priority Bytes");
            _standby2 = _query.Add(@"\Memory\Standby Cache Reserve Bytes");
            _standby3 = _query.Add(@"\Memory\Standby Cache Core Bytes");
            _modified = _query.Add(@"\Memory\Modified Page List Bytes");
        }

        // Rate counters need two samples; take the baseline now.
        _query.Collect();
    }

    private static bool IsPhysical(string instance)
    {
        if (instance.Equals("_Total", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var marker in VirtualNic)
        {
            if (instance.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static void AddDisk(Dictionary<string, (double, double)> map, string instance, double read, double write)
    {
        // Instances look like "0 C:", "1 D: E:" or "_Total".
        if (instance.Equals("_Total", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var letters = instance.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Where(t => t.Length == 2 && t[1] == ':').ToList();
        if (letters.Count == 0)
        {
            letters = ["#" + instance];
        }

        foreach (var letter in letters)
        {
            var (r, w) = map.GetValueOrDefault(letter);
            map[letter] = (r + read, w + write);
        }
    }

    // ------------------------------------------------------------------------------------------------ memory

    private MemoryInfo? ReadMemory(DateTime now)
    {
        if (SystemNative.Memory() is not { } m)
        {
            return null;
        }

        var used = m.Total - m.Available;
        if ((now - _compressedAt).TotalSeconds >= 4)
        {
            _compressedAt = now;
            _compressed = SystemNative.CompressedBytes();
        }

        double cached = 0;
        double swapPercent = 0;
        if (_query is not null)
        {
            cached = (_query.Value(_standby1) ?? 0) + (_query.Value(_standby2) ?? 0) + (_query.Value(_standby3) ?? 0) + (_query.Value(_modified) ?? 0);
            swapPercent = _query.Value(_swap) ?? 0;
        }

        var pageFileSize = m.PageFileTotal > m.Total ? m.PageFileTotal - m.Total : 0;
        var swap = (ulong)(pageFileSize * Math.Clamp(swapPercent, 0, 100) / 100.0);
        var availablePercent = m.Total == 0 ? 100 : (double)m.Available / m.Total * 100;
        var commit = m.PageFileTotal == 0 ? 0 : (1.0 - ((double)m.PageFileAvailable / m.PageFileTotal)) * 100;
        var pressure = availablePercent < 8 || (commit > 90 && availablePercent < 15)
            ? Pressure.Critical
            : availablePercent < 15 || commit > 80 ? Pressure.Caution : Pressure.Normal;
        var app = used > _compressed ? used - _compressed : used;
        return new MemoryInfo(used, m.Total, app, _compressed, (ulong)cached, swap, pressure, m.Total == 0 ? 0 : (double)used / m.Total * 100);
    }

    // ------------------------------------------------------------------------------------------------ temperatures

    private double? ReadCpuTemperature()
    {
        if (!_tempWmiFailed)
        {
            // The thermal-zone performance counters need no administrator rights; the zone is usually the CPU package or chassis.
            var rows = Wmi.Query(@"root\CIMV2", "SELECT Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation", "Temperature");
            var kelvin = rows.Select(r => Wmi.ToInt(r["Temperature"])).Where(t => t > 273 && t < 400).DefaultIfEmpty(0).Max();
            if (kelvin > 0)
            {
                return kelvin - 273.15;
            }
        }

        var acpi = Wmi.Query(@"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature", "CurrentTemperature");
        var best = acpi.Select(r => Wmi.ToInt(r["CurrentTemperature"])).Where(t => t > 2732).DefaultIfEmpty(0).Max();
        if (best > 0)
        {
            var celsius = (best / 10.0) - 273.15;
            return celsius is > 0 and < 130 ? celsius : null;
        }

        _tempWmiFailed = true;
        return null;
    }

    // ------------------------------------------------------------------------------------------------ volumes, power, devices

    private IReadOnlyList<VolumeInfo> ReadVolumes()
    {
        var list = new List<VolumeInfo>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable) || !drive.IsReady)
                {
                    continue;
                }

                var letter = drive.Name.TrimEnd('\\');
                if (!_external.TryGetValue(letter, out var external))
                {
                    external = drive.DriveType == DriveType.Removable || SystemNative.BusType(letter) == 7;
                    _external[letter] = external;
                }

                list.Add(new VolumeInfo(letter, drive.VolumeLabel, drive.DriveFormat, external, drive.TotalSize, drive.AvailableFreeSpace));
            }
        }
        catch (Exception)
        {
        }

        return list;
    }

    private PowerInfo ReadPower(bool draw)
    {
        var s = PowerStatus.Read();
        if (!s.HasBattery)
        {
            return new PowerInfo(false, s.OnAc, 0, false, -1, null, null, null, null, null);
        }

        double? discharge = null;
        double? charge = null;
        if (draw)
        {
            var rows = Wmi.Query(@"root\WMI", "SELECT ChargeRate, DischargeRate, Charging, Discharging FROM BatteryStatus", "ChargeRate", "DischargeRate", "Charging", "Discharging");
            if (rows.Count > 0)
            {
                var d = Wmi.ToInt(rows[0]["DischargeRate"]);
                var c = Wmi.ToInt(rows[0]["ChargeRate"]);
                discharge = d > 0 ? d / 1000.0 : null;
                charge = c > 0 ? c / 1000.0 : null;
            }
        }

        if (!_batteryHealthRead)
        {
            _batteryHealthRead = true;
            var full = Wmi.Query(@"root\WMI", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity", "FullChargedCapacity");
            var design = Wmi.Query(@"root\WMI", "SELECT DesignedCapacity FROM BatteryStaticData", "DesignedCapacity");
            if (full.Count > 0 && design.Count > 0)
            {
                var f = Wmi.ToInt(full[0]["FullChargedCapacity"]);
                var dsg = Wmi.ToInt(design[0]["DesignedCapacity"]);
                _health = dsg > 0 && f > 0 ? Math.Clamp((int)Math.Round(f * 100.0 / dsg), 1, 100) : null;
            }

            var cycles = Wmi.Query(@"root\WMI", "SELECT CycleCount FROM BatteryCycleCount", "CycleCount");
            if (cycles.Count > 0 && Wmi.ToInt(cycles[0]["CycleCount"]) > 0)
            {
                _cycles = Wmi.ToInt(cycles[0]["CycleCount"]);
            }
        }

        return new PowerInfo(true, s.OnAc, s.Percent, s.Charging, s.SecondsRemaining, discharge, charge, _health, _cycles, null);
    }

    private static List<UsbDevice> ReadUsbDevices()
    {
        var rows = Wmi.Query(
            @"root\CIMV2",
            "SELECT Name, Manufacturer, PNPClass, Service FROM Win32_PnPEntity WHERE DeviceID LIKE 'USB\\\\VID%' AND Present = TRUE",
            "Name",
            "Manufacturer",
            "PNPClass",
            "Service");
        var list = new List<UsbDevice>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var name = row["Name"] as string;
            var pnp = row["PNPClass"] as string ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name)
                || name.Contains("Hub", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Host Controller", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Composite Device", StringComparison.OrdinalIgnoreCase)
                || pnp.Equals("Bluetooth", StringComparison.OrdinalIgnoreCase)
                || pnp.Equals("Camera", StringComparison.OrdinalIgnoreCase) && name.Contains("Integrated", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (seen.Add(name))
            {
                var vendor = row["Manufacturer"] as string;
                list.Add(new UsbDevice(name, string.IsNullOrWhiteSpace(vendor) || vendor.StartsWith("(Standard", StringComparison.OrdinalIgnoreCase) ? string.Empty : vendor));
            }
        }

        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return list;
    }

    /// <summary>Local IPv4 addresses of the interfaces that are up (nothing leaves the machine).</summary>
    public static List<(string Interface, string Address)> LocalAddresses()
    {
        var list = new List<(string, string)>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        list.Add((nic.Name, ua.Address.ToString()));
                    }
                }
            }
        }
        catch (Exception)
        {
        }

        return list;
    }
}
