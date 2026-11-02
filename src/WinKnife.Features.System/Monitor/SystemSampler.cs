// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace WinKnife.Features.SystemTools;

[Flags]
internal enum MonitorMetric
{
    None = 0,
    Cpu = 1,
    Gpu = 2,
    Memory = 4,
    Network = 8,
    Disk = 16,
    Power = 32,
    Devices = 64,
}

internal sealed record DriveSnap(string Name, string Label, long TotalBytes, long FreeBytes);

internal sealed record PowerSnap(bool HasBattery, bool OnAc, int Percent, int SecondsRemaining, bool Charging);

internal sealed class SystemSnapshot
{
    public double Cpu { get; set; }

    public double[] Cores { get; set; } = [];

    public double? CpuTempC { get; set; }

    public double? Gpu { get; set; }

    public bool GpuAvailable { get; set; }

    public double MemPercent { get; set; }

    public ulong MemUsed { get; set; }

    public ulong MemTotal { get; set; }

    public double NetDown { get; set; }

    public double NetUp { get; set; }

    public double DiskActivity { get; set; }

    public double DiskRead { get; set; }

    public double DiskWrite { get; set; }

    public IReadOnlyList<DriveSnap> Drives { get; set; } = [];

    public PowerSnap? Power { get; set; }

    public IReadOnlyList<string> Devices { get; set; } = [];
}

/// <summary>
/// Collects the system metrics. Not thread safe: the hub calls <see cref="Sample"/> from one timer thread at a time.
/// Counters are only created for the requested metrics.
/// </summary>
internal sealed class SystemSampler : IDisposable
{
    private PdhQuery? _query;
    private MonitorMetric _queryMetrics;
    private nint _cpuTotal;
    private nint _cpuCores;
    private nint _gpu;
    private nint _diskTime;
    private nint _diskRead;
    private nint _diskWrite;

    private DateTime _lastNetTime;
    private long _lastRx;
    private long _lastTx;
    private bool _haveNet;
    private NetworkInterface[] _nics = [];
    private DateTime _nicsRefreshed = DateTime.MinValue;

    private DateTime _drivesAt = DateTime.MinValue;
    private IReadOnlyList<DriveSnap> _drives = [];
    private DateTime _devicesAt = DateTime.MinValue;
    private IReadOnlyList<string> _devices = [];

    private bool _tempChecked;
    private bool _tempAvailable;
    private DateTime _tempAt = DateTime.MinValue;
    private double? _temp;

    public SystemSnapshot Sample(MonitorMetric metrics)
    {
        var snap = new SystemSnapshot();
        EnsureQuery(metrics);
        _query?.Collect();

        if (metrics.HasFlag(MonitorMetric.Cpu))
        {
            snap.Cpu = Math.Clamp(_query?.Value(_cpuTotal) ?? 0, 0, 100);
            var cores = new double[Environment.ProcessorCount];
            if (_query is not null)
            {
                foreach (var (instance, value) in _query.Array(_cpuCores))
                {
                    if (int.TryParse(instance, out var index) && index >= 0 && index < cores.Length)
                    {
                        cores[index] = Math.Clamp(value, 0, 100);
                    }
                }
            }

            snap.Cores = cores;
            snap.CpuTempC = SampleTemperature();
        }

        if (metrics.HasFlag(MonitorMetric.Gpu))
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

        if (metrics.HasFlag(MonitorMetric.Memory))
        {
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref status))
            {
                snap.MemTotal = status.TotalPhys;
                snap.MemUsed = status.TotalPhys - status.AvailPhys;
                snap.MemPercent = status.MemoryLoad;
            }
        }

        if (metrics.HasFlag(MonitorMetric.Network))
        {
            SampleNetwork(snap);
        }

        if (metrics.HasFlag(MonitorMetric.Disk))
        {
            snap.DiskActivity = Math.Clamp(_query?.Value(_diskTime) ?? 0, 0, 100);
            snap.DiskRead = _query?.Value(_diskRead) ?? 0;
            snap.DiskWrite = _query?.Value(_diskWrite) ?? 0;
            if ((DateTime.UtcNow - _drivesAt).TotalSeconds >= 10)
            {
                _drivesAt = DateTime.UtcNow;
                _drives = ReadDrives();
            }

            snap.Drives = _drives;
        }

        if (metrics.HasFlag(MonitorMetric.Power))
        {
            snap.Power = ReadPower();
        }

        if (metrics.HasFlag(MonitorMetric.Devices))
        {
            if ((DateTime.UtcNow - _devicesAt).TotalSeconds >= 10)
            {
                _devicesAt = DateTime.UtcNow;
                _devices = ReadUsbDevices();
            }

            snap.Devices = _devices;
        }

        return snap;
    }

    public void Dispose()
    {
        _query?.Dispose();
        _query = null;
    }

    /// <summary>Forgets cached slow-changing data (e.g. after the set of enabled metrics changed).</summary>
    public void Reset()
    {
        _haveNet = false;
        _drivesAt = DateTime.MinValue;
        _devicesAt = DateTime.MinValue;
    }

    private void EnsureQuery(MonitorMetric metrics)
    {
        var needed = metrics & (MonitorMetric.Cpu | MonitorMetric.Gpu | MonitorMetric.Disk);
        if (needed == _queryMetrics && (_query is not null || needed == MonitorMetric.None))
        {
            return;
        }

        _query?.Dispose();
        _queryMetrics = needed;
        _cpuTotal = _cpuCores = _gpu = _diskTime = _diskRead = _diskWrite = 0;
        if (needed == MonitorMetric.None)
        {
            _query = null;
            return;
        }

        _query = new PdhQuery();
        if (needed.HasFlag(MonitorMetric.Cpu))
        {
            _cpuTotal = _query.Add(@"\Processor(_Total)\% Processor Time");
            _cpuCores = _query.Add(@"\Processor(*)\% Processor Time");
        }

        if (needed.HasFlag(MonitorMetric.Gpu))
        {
            _gpu = _query.Add(@"\GPU Engine(*)\Utilization Percentage");
        }

        if (needed.HasFlag(MonitorMetric.Disk))
        {
            _diskTime = _query.Add(@"\PhysicalDisk(_Total)\% Disk Time");
            _diskRead = _query.Add(@"\PhysicalDisk(_Total)\Disk Read Bytes/sec");
            _diskWrite = _query.Add(@"\PhysicalDisk(_Total)\Disk Write Bytes/sec");
        }

        // Rate counters need two samples; take the baseline now.
        _query.Collect();
    }

    private double? SampleTemperature()
    {
        if (!_tempChecked)
        {
            _tempChecked = true;
            _temp = ReadTemperature();
            _tempAvailable = _temp is not null;
            _tempAt = DateTime.UtcNow;
        }
        else if (_tempAvailable && (DateTime.UtcNow - _tempAt).TotalSeconds >= 5)
        {
            _tempAt = DateTime.UtcNow;
            _temp = ReadTemperature();
        }

        return _tempAvailable ? _temp : null;
    }

    private static double? ReadTemperature()
    {
        // Needs administrator rights on most machines; simply unavailable otherwise.
        var rows = Wmi.Query(@"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature", "CurrentTemperature");
        var best = rows.Select(r => Wmi.ToInt(r["CurrentTemperature"])).Where(t => t > 2732).DefaultIfEmpty(0).Max();
        if (best <= 0)
        {
            return null;
        }

        var celsius = (best / 10.0) - 273.15;
        return celsius is > 0 and < 130 ? celsius : null;
    }

    private void SampleNetwork(SystemSnapshot snap)
    {
        try
        {
            if ((DateTime.UtcNow - _nicsRefreshed).TotalSeconds >= 10 || _nics.Length == 0)
            {
                _nicsRefreshed = DateTime.UtcNow;
                _nics = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                    .ToArray();
                _haveNet = false;
            }

            long rx = 0;
            long tx = 0;
            foreach (var nic in _nics)
            {
                var stats = nic.GetIPv4Statistics();
                rx += stats.BytesReceived;
                tx += stats.BytesSent;
            }

            var now = DateTime.UtcNow;
            if (_haveNet)
            {
                var seconds = Math.Max(0.1, (now - _lastNetTime).TotalSeconds);
                snap.NetDown = Math.Max(0, (rx - _lastRx) / seconds);
                snap.NetUp = Math.Max(0, (tx - _lastTx) / seconds);
            }

            _lastRx = rx;
            _lastTx = tx;
            _lastNetTime = now;
            _haveNet = true;
        }
        catch (Exception)
        {
            _nics = [];
        }
    }

    private static List<DriveSnap> ReadDrives()
    {
        var list = new List<DriveSnap>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable) || !drive.IsReady)
                {
                    continue;
                }

                list.Add(new DriveSnap(drive.Name.TrimEnd('\\'), drive.VolumeLabel, drive.TotalSize, drive.AvailableFreeSpace));
            }
        }
        catch (Exception)
        {
        }

        return list;
    }

    private static PowerSnap ReadPower()
    {
        if (!GetSystemPowerStatus(out var s) || (s.BatteryFlag & 128) != 0)
        {
            return new PowerSnap(false, true, 0, -1, false);
        }

        var percent = s.BatteryLifePercent == 255 ? 0 : s.BatteryLifePercent;
        var seconds = s.BatteryLifeTime == 0xFFFFFFFF ? -1 : (int)s.BatteryLifeTime;
        return new PowerSnap(true, s.ACLineStatus == 1, percent, seconds, (s.BatteryFlag & 8) != 0);
    }

    private static List<string> ReadUsbDevices()
    {
        var rows = Wmi.Query(
            @"root\CIMV2",
            "SELECT Name, PNPClass FROM Win32_PnPEntity WHERE DeviceID LIKE 'USB\\\\VID%' AND Present = TRUE",
            "Name",
            "PNPClass");
        var names = new List<string>();
        foreach (var row in rows)
        {
            var name = row["Name"] as string;
            if (string.IsNullOrWhiteSpace(name)
                || name.Contains("Hub", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Host Controller", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Composite Device", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!names.Contains(name))
            {
                names.Add(name);
            }
        }

        names.Sort(StringComparer.CurrentCultureIgnoreCase);
        return names;
    }

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
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
