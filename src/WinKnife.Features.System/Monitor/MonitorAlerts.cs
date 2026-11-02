// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using WinKnife.Core;

namespace WinKnife.Features.SystemTools;

/// <summary>Strings of the monitor alerts (spec 02a 5.5).</summary>
internal static partial class Strings
{
    public static class Alerts
    {
        public static  string Section => Loc.T("Alerts");
        public static  string Caption => Loc.T("Alerts fire when their selected limits are reached. CPU use and temperature alerts ignore spikes shorter than about 12 seconds. The repeat setting only limits repeats of the same alert.");
        public static  string Repeat => Loc.T("Repeat the same alert after");
        public static  string Denied => Loc.T("Notifications for WinKnife are off in Windows Settings, so alerts cannot appear.");
        public static  string HighCpu => Loc.T("High CPU");
        public static  string HighCpuTemp => Loc.T("High CPU temperature");
        public static  string CriticalMemory => Loc.T("Critical memory pressure");
        public static  string LowDisk => Loc.T("Low disk space");
        public static  string HighBatteryTemp => Loc.T("High battery temperature");
        public static  string LowBattery => Loc.T("Low battery");
        public static  string CpuAbove => Loc.T("CPU above");
        public static  string TemperatureAbove => Loc.T("Temperature above");
        public static  string FreeBelow => Loc.T("Free space below");
        public static  string BatteryBelow => Loc.T("Battery below");

        public static string CpuBody(int percent) => Loc.F("CPU stayed above {0}% for a few seconds.", percent);

        public static string DiskBody(string volume, int percent) => Loc.F("{0} has less than {1}% free.", volume, percent);
    }
}

/// <summary>
/// Monitor alerts (spec 05 F2): all off by default and throttled by a per-alert cooldown. CPU use, CPU temperature and battery temperature must stay
/// above their limit across separate fresh readings for 12 seconds; memory, disk and battery fire on the reading. Costs nothing while no alert is on.
/// </summary>
internal sealed class MonitorAlerts
{
    private const double SustainedSeconds = 12;

    private sealed class Gate
    {
        public DateTime? Since { get; set; }

        public DateTime LastRead { get; set; }

        public int Reads { get; set; }

        /// <summary>True when the reading has been at or above the limit for 12 s across at least two distinct reads.</summary>
        public bool Update(bool above, DateTime readAt)
        {
            if (!above)
            {
                Since = null;
                Reads = 0;
                return false;
            }

            if (readAt == LastRead)
            {
                return Since is { } s0 && Reads >= 2 && (readAt - s0).TotalSeconds >= SustainedSeconds;
            }

            LastRead = readAt;
            Since ??= readAt;
            Reads++;
            return Reads >= 2 && (readAt - Since.Value).TotalSeconds >= SustainedSeconds;
        }
    }

    private readonly IFeatureContext _ctx;
    private readonly IAppSettings _app;
    private readonly Gate _cpu = new();
    private readonly Gate _cpuTemp = new();
    private readonly Gate _batteryTemp = new();
    private readonly Dictionary<string, DateTime> _last = [];
    private bool _started;

    public MonitorAlerts(IFeatureContext ctx)
    {
        _ctx = ctx;
        _app = ctx.App;
    }

    public void Start()
    {
        _started = true;
        _app.Changed += OnSetting;
        MonitorService.Updated += OnUpdated;
        Refresh();
    }

    public void Stop()
    {
        _started = false;
        _app.Changed -= OnSetting;
        MonitorService.Updated -= OnUpdated;
        MonitorService.SetBackground("alerts", Needs.None);
    }

    public void Refresh()
    {
        if (!_started)
        {
            return;
        }

        var needs = Needs.None;
        var runtime = _ctx.Runtime;
        if (_app.Get<bool>("monitorAlertCPU") && runtime.IsAvailable("monitor-cpu"))
        {
            needs |= Needs.Cpu;
        }

        if (_app.Get<bool>("monitorAlertCPUTemperature") && runtime.IsAvailable("monitor-cpu"))
        {
            needs |= Needs.CpuTemp;
        }

        if (_app.Get<bool>("monitorAlertMemory") && runtime.IsAvailable("monitor-memory"))
        {
            needs |= Needs.Memory;
        }

        if (_app.Get<bool>("monitorAlertDisk") && runtime.IsAvailable("monitor-disk"))
        {
            needs |= Needs.DiskSpace;
        }

        if ((_app.Get<bool>("monitorAlertBattery") || _app.Get<bool>("monitorAlertBatteryTemperature")) && runtime.IsAvailable("monitor-power"))
        {
            needs |= Needs.Power;
        }

        MonitorService.SetBackground("alerts", needs);
    }

    private void OnSetting(string key)
    {
        if (key.StartsWith("monitorAlert", StringComparison.Ordinal))
        {
            _ctx.Ui.BeginInvoke(Refresh);
        }
    }

    private void OnUpdated(MonitorSnapshot s)
    {
        if (!_started)
        {
            return;
        }

        var app = _app;
        var cooldown = TimeSpan.FromMinutes(Math.Clamp(app.Get<int>("monitorAlertCooldownMinutes"), 1, 60));
        if (app.Get<bool>("monitorAlertCPU") && s.Cpu is { } cpu)
        {
            var threshold = Clamp(app.Get<int>("monitorAlertCPUThreshold"), 50, 100, 90);
            if (_cpu.Update(cpu >= threshold, s.At))
            {
                Fire("cpu", cooldown, Loc.T("High CPU"), Strings.Alerts.CpuBody(threshold));
            }
        }

        if (app.Get<bool>("monitorAlertCPUTemperature"))
        {
            var threshold = Clamp(app.Get<int>("monitorAlertCPUTemperatureThreshold"), 70, 105, 90);
            if (s.CpuTemp is { } t && _cpuTemp.Update(t >= threshold, s.TempReadAt))
            {
                Fire("cpuTemp", cooldown, Loc.T("Hot CPU"), Loc.F("CPU reached {0}.", Fmt.Temperature(t, app.Get<string>("temperatureUnit") == "fahrenheit", false)));
            }
            else if (s.CpuTemp is null)
            {
                _cpuTemp.Update(false, s.TempReadAt);
            }
        }

        if (app.Get<bool>("monitorAlertMemory") && s.Memory is { Pressure: Pressure.Critical })
        {
            Fire("memory", cooldown, Loc.T("Critical memory"), Loc.T("Memory pressure reached the critical level."));
        }

        if (app.Get<bool>("monitorAlertDisk"))
        {
            var threshold = Clamp(app.Get<int>("monitorAlertDiskFreePercent"), 5, 30, 10);
            foreach (var v in s.Volumes)
            {
                if (v.Total > 0 && (double)v.Free / v.Total * 100 < threshold)
                {
                    Fire("disk:" + v.Letter, cooldown, Loc.T("Low disk space"), Strings.Alerts.DiskBody(v.DisplayName, threshold));
                }
            }
        }

        if (s.Power is { HasBattery: true } p)
        {
            if (app.Get<bool>("monitorAlertBattery"))
            {
                var threshold = Clamp(app.Get<int>("monitorAlertBatteryPercent"), 5, 50, 15);
                if (p.Percent <= threshold && !p.Charging && !p.OnAc)
                {
                    Fire("battery", cooldown, Loc.T("Low battery"), Loc.F("Battery is at {0}%.", p.Percent));
                }
            }

            if (app.Get<bool>("monitorAlertBatteryTemperature") && p.TemperatureC is { } bt)
            {
                var threshold = Clamp(app.Get<int>("monitorAlertBatteryTemperatureThreshold"), 30, 50, 40);
                if (_batteryTemp.Update(bt >= threshold, s.At))
                {
                    Fire("batteryTemp", cooldown, Loc.T("Hot battery"), Loc.F("Battery reached {0}.", Fmt.Temperature(bt, app.Get<string>("temperatureUnit") == "fahrenheit", false)));
                }
            }
        }
    }

    private static int Clamp(int value, int min, int max, int fallback) => value < min || value > max ? fallback : value;

    private void Fire(string key, TimeSpan cooldown, string title, string body)
    {
        if (_last.TryGetValue(key, out var at) && DateTime.UtcNow - at < cooldown)
        {
            return;
        }

        _last[key] = DateTime.UtcNow;
        _ctx.Notifier.Show(title, body, NotificationLevel.Warning);
    }
}
