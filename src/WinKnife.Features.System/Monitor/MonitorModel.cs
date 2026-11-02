// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace WinKnife.Features.SystemTools;

/// <summary>What a sampling pass has to read (the hub ORs the needs of every visible surface, tray metric and alert).</summary>
[Flags]
internal enum Needs
{
    None = 0,
    Cpu = 1,
    CpuCores = 2,
    CpuTemp = 4,
    Gpu = 8,
    GpuTemp = 16,
    Memory = 32,
    Network = 64,
    Disk = 128,
    DiskSpace = 256,
    Power = 512,
    PowerDraw = 1024,
    Peripherals = 2048,
    Devices = 4096,
    Uptime = 8192,
    Smart = 16384,
}

internal enum Pressure
{
    Unknown,
    Normal,
    Caution,
    Critical,
}

internal sealed record MemoryInfo(ulong Used, ulong Total, ulong App, ulong Compressed, ulong Cached, ulong SwapUsed, Pressure Pressure, double Percent);

internal sealed record VolumeInfo(string Letter, string Label, string FileSystem, bool External, long Total, long Free)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? Letter : Label;

    public double UsedFraction => Total <= 0 ? 0 : 1.0 - ((double)Free / Total);
}

internal sealed record PowerInfo(bool HasBattery, bool OnAc, int Percent, bool Charging, int SecondsRemaining, double? DischargeWatts, double? ChargeWatts, int? HealthPercent, int? Cycles, double? TemperatureC);

internal sealed record PeripheralBattery(string Name, string Kind, int Percent);

internal sealed record UsbDevice(string Name, string Vendor);

internal sealed record SmartInfo(string Status, string? TotalWritten, string? TotalRead, string? Temperature, string? Health, string? PowerCycles, string? PowerOnHours);

internal sealed class MonitorSnapshot
{
    public DateTime At { get; init; } = DateTime.UtcNow;

    public double? Cpu { get; set; }

    public double[] Cores { get; set; } = [];

    public string[] CoreClasses { get; set; } = [];

    public double? CpuTemp { get; set; }

    /// <summary>When the temperature readings were taken (they are carried between reads).</summary>
    public DateTime TempReadAt { get; set; }

    public double? Gpu { get; set; }

    public bool GpuAvailable { get; set; }

    public double? GpuTemp { get; set; }

    public MemoryInfo? Memory { get; set; }

    public double NetDown { get; set; }

    public double NetUp { get; set; }

    public double NetTotalDown { get; set; }

    public double NetTotalUp { get; set; }

    public bool NetKnown { get; set; }

    public double DiskRead { get; set; }

    public double DiskWrite { get; set; }

    public Dictionary<string, (double Read, double Write)> DiskByLetter { get; set; } = [];

    public IReadOnlyList<VolumeInfo> Volumes { get; set; } = [];

    public PowerInfo? Power { get; set; }

    public IReadOnlyList<PeripheralBattery> Peripherals { get; set; } = [];

    public IReadOnlyList<UsbDevice> Devices { get; set; } = [];

    public TimeSpan? Uptime { get; set; }

    public Dictionary<string, SmartInfo> Smart { get; set; } = [];

    /// <summary>Net rate in bytes per second (download + upload), used for auto-scaled graphs.</summary>
    public double NetPeak => Math.Max(NetDown, NetUp);
}

/// <summary>A row of the "Apps using ..." lists.</summary>
internal sealed record ProcessUsage(string Name, int Pid, double Value, string Display, System.Windows.Media.ImageSource? Icon);
