// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.SystemTools;

/// <summary>The metric detail kinds (spec 01 3.7) opened from a tray metric or the connected-devices row.</summary>
internal enum MetricKind
{
    Cpu,
    Gpu,
    Memory,
    Network,
    Disk,
    Battery,
    Power,
    Devices,
}

/// <summary>The detail view of one metric: summary card (big value + history), detail rows and the per-app list.</summary>
internal static class MetricDetail
{
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    /// <summary>The panel tab a metric belongs to (Back returns there).</summary>
    public static string TabOf(MetricKind kind) => kind switch
    {
        MetricKind.Cpu or MetricKind.Gpu or MetricKind.Memory or MetricKind.Devices => PanelTabs.System,
        MetricKind.Network => PanelTabs.Network,
        MetricKind.Disk => PanelTabs.Disk,
        _ => PanelTabs.Power,
    };

    public static (string Title, string Glyph) Describe(MetricKind kind) => kind switch
    {
        MetricKind.Cpu => ("CPU", Gl.Cpu),
        MetricKind.Gpu => ("GPU", Gl.Gpu),
        MetricKind.Memory => ("Memory", Gl.Memory),
        MetricKind.Network => ("Network", Gl.Network),
        MetricKind.Disk => ("Disks", Gl.Disk),
        MetricKind.Battery => ("Battery", Gl.Battery),
        MetricKind.Power => ("Power", Gl.Bolt),
        _ => (Strings.Monitor.ConnectedDevices, Gl.Usb),
    };

    private static string ColorOf(MetricKind kind) => kind switch
    {
        MetricKind.Cpu or MetricKind.Network => ThemeKeys.Accent,
        MetricKind.Gpu or MetricKind.Devices => ThemeKeys.Cyan,
        MetricKind.Memory => ThemeKeys.Mint,
        MetricKind.Disk => ThemeKeys.Yellow,
        MetricKind.Battery => ThemeKeys.Green,
        _ => ThemeKeys.Orange,
    };

    private static Needs NeedsOf(MetricKind kind) => kind switch
    {
        MetricKind.Cpu => Needs.Cpu | Needs.CpuCores | Needs.CpuTemp | Needs.Uptime,
        MetricKind.Gpu => Needs.Gpu | Needs.GpuTemp,
        MetricKind.Memory => Needs.Memory,
        MetricKind.Network => Needs.Network,
        MetricKind.Disk => Needs.Disk | Needs.DiskSpace,
        MetricKind.Battery or MetricKind.Power => Needs.Power | Needs.PowerDraw | Needs.Peripherals,
        _ => Needs.Devices,
    };

    public static FrameworkElement Create(IContributionServices c, MetricKind kind)
    {
        var app = c.App;
        var fahrenheit = () => app.Get<string>("temperatureUnit") == "fahrenheit";
        var root = new StackPanel { Width = 308 };
        var live = new LiveHost(root, NeedsOf(kind));
        var color = ColorOf(kind);
        var (title, glyph) = Describe(kind);

        // Summary card.
        var summary = new StackPanel();
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.Children.Add(UiKit.Icon(glyph, 15, color, FontWeights.SemiBold));
        var primary = UiKit.Text("-", 28, FontWeights.SemiBold, ThemeKeys.TextPrimary);
        primary.FontFamily = UiKit.DisplayFont;
        primary.Margin = new Thickness(8, 0, 10, 0);
        Grid.SetColumn(primary, 1);
        top.Children.Add(primary);
        var secondary = UiKit.Text(string.Empty, 10.5, FontWeights.Medium, ThemeKeys.TextSecondary, wrap: true);
        secondary.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(secondary, 2);
        top.Children.Add(secondary);
        summary.Children.Add(top);
        var graph = new GraphView(app, color, 38, kind is MetricKind.Network or MetricKind.Disk ? ThemeKeys.Green : null) { Margin = new Thickness(0, 8, 0, 0) };
        if (kind != MetricKind.Devices)
        {
            summary.Children.Add(graph);
        }

        root.Children.Add(new PanelCard(summary));

        // Detail card.
        var detail = new StackPanel();
        var detailCard = new PanelCard(detail);
        detailCard.Margin = new Thickness(0, 10, 0, 0);
        root.Children.Add(detailCard);

        (Grid Row, TextBlock Value) Line(string label)
        {
            var row = new Grid { Margin = new Thickness(0, detail.Children.Count == 0 ? 0 : 7, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(UiKit.Text(label, 10.5, FontWeights.Normal, ThemeKeys.TextSecondary));
            var value = UiKit.Text("-", 11, FontWeights.Medium, ThemeKeys.TextPrimary);
            value.FontFamily = Mono;
            value.TextTrimming = TextTrimming.CharacterEllipsis;
            value.MaxWidth = 190;
            Grid.SetColumn(value, 1);
            row.Children.Add(value);
            detail.Children.Add(row);
            return (row, value);
        }

        switch (kind)
        {
            case MetricKind.Cpu:
            {
                var usage = Line(Strings.Monitor.HardwareUsage);
                var temps = Line(Strings.Monitor.Temperatures);
                var up = Line("Up for");
                live.Add(s =>
                {
                    primary.Text = s.Cpu is { } v ? Fmt.Percent(v) : "-";
                    secondary.Text = s.Cores.Length > 0 ? $"{s.Cores.Length} logical cores" : string.Empty;
                    usage.Value.Text = s.Cpu is { } u ? Fmt.Percent(u) : "-";
                    temps.Value.Text = s.CpuTemp is { } t ? Fmt.Temperature(t, fahrenheit()) : "-";
                    up.Value.Text = s.Uptime is { } span ? Fmt.Uptime(span) : "-";
                    graph.Set(MonitorService.Series("cpu"), 100, "100%");
                });
                break;
            }

            case MetricKind.Gpu:
            {
                var usage = Line(Strings.Monitor.HardwareUsage);
                var temps = Line(Strings.Monitor.Temperatures);
                live.Add(s =>
                {
                    primary.Text = s.Gpu is { } v ? Fmt.Percent(v) : "-";
                    usage.Value.Text = primary.Text;
                    temps.Value.Text = s.GpuTemp is { } t ? Fmt.Temperature(t, fahrenheit()) : "-";
                    graph.Set(MonitorService.Series("gpu"), 100, "100%");
                });
                break;
            }

            case MetricKind.Memory:
            {
                var used = Line(app.Get<string>("monitorMemoryMetric") == "app" ? "App Memory" : "Memory Used");
                var pressure = Line(Strings.Monitor.Pressure);
                var compressed = Line(Strings.Monitor.Compressed);
                var cached = Line(Strings.Monitor.CachedFiles);
                var swap = Line(Strings.Monitor.SwapUsed);
                live.Add(s =>
                {
                    if (s.Memory is not { } m)
                    {
                        return;
                    }

                    var shown = app.Get<string>("monitorMemoryMetric") == "app" ? m.App : m.Used;
                    primary.Text = Fmt.Percent(m.Total == 0 ? 0 : shown * 100.0 / m.Total);
                    secondary.Text = $"{Fmt.BytesBinary(shown)} / {Fmt.BytesBinary(m.Total)}";
                    used.Value.Text = secondary.Text;
                    pressure.Value.Text = m.Pressure switch { Pressure.Normal => Strings.Monitor.Normal, Pressure.Caution => Strings.Monitor.Caution, Pressure.Critical => Strings.Monitor.Critical, _ => "-" };
                    compressed.Value.Text = Fmt.BytesBinary(m.Compressed);
                    cached.Value.Text = Fmt.BytesBinary(m.Cached);
                    swap.Value.Text = Fmt.BytesBinary(m.SwapUsed);
                    graph.Set(MonitorService.Series("mem"), 100, "100%");
                });
                break;
            }

            case MetricKind.Network:
            {
                var down = Line(Strings.Monitor.Download);
                var upLine = Line(Strings.Monitor.Upload);
                var total = Line(Strings.Monitor.ThisSession);
                live.Add(s =>
                {
                    var bits = app.Get<string>("networkSpeedUnit") == "bits";
                    primary.Text = Fmt.Rate(s.NetDown, bits);
                    primary.FontSize = 22;
                    secondary.Text = "↑ " + Fmt.Rate(s.NetUp, bits);
                    down.Value.Text = Fmt.Rate(s.NetDown, bits);
                    upLine.Value.Text = Fmt.Rate(s.NetUp, bits);
                    total.Value.Text = $"↓{Fmt.BytesBinary(s.NetTotalDown)}  ↑{Fmt.BytesBinary(s.NetTotalUp)}";
                    var d = MonitorService.Series("down");
                    var u = MonitorService.Series("up");
                    var peak = Math.Max(d.DefaultIfEmpty(0).Max(), u.DefaultIfEmpty(0).Max());
                    var ceiling = GraphView.RoundCeiling(Math.Max(1, peak), 1);
                    graph.Set(d, ceiling, Fmt.Rate(ceiling, bits), u);
                });
                var test = NetworkBlocks.Test(new DetailContext(c));
                if (test is not null)
                {
                    var card = new PanelCard(test) { Margin = new Thickness(0, 10, 0, 0) };
                    root.Children.Add(card);
                }

                break;
            }

            case MetricKind.Disk:
            {
                var diskUsage = Line("Used");
                var read = Line(Strings.Monitor.Read);
                var write = Line(Strings.Monitor.Write);
                live.Add(s =>
                {
                    var v = s.Volumes.FirstOrDefault();
                    primary.Text = v is null ? "-" : Fmt.Percent(v.UsedFraction * 100);
                    secondary.Text = v is null ? string.Empty : $"{v.DisplayName}\n{Fmt.BytesDecimal(v.Free)} available";
                    diskUsage.Value.Text = primary.Text;
                    read.Value.Text = Fmt.Rate(s.DiskRead, false);
                    write.Value.Text = Fmt.Rate(s.DiskWrite, false);
                    var r = MonitorService.Series("diskR");
                    var w = MonitorService.Series("diskW");
                    var ceiling = GraphView.RoundCeiling(Math.Max(1, Math.Max(r.DefaultIfEmpty(0).Max(), w.DefaultIfEmpty(0).Max())), 1);
                    graph.Set(r, ceiling, Fmt.Rate(ceiling, false), w);
                });
                break;
            }

            case MetricKind.Battery:
            {
                var charge = Line("Charge");
                var batt = Line(Strings.Monitor.BatteryRow);
                var temps = Line(Strings.Monitor.Temperatures);
                var health = Line(Strings.Monitor.Health);
                var cycles = Line("Cycles");
                var peripherals = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
                detail.Children.Add(peripherals);
                live.Add(s =>
                {
                    if (s.Power is not { HasBattery: true } p)
                    {
                        primary.Text = "-";
                        return;
                    }

                    primary.Text = p.Percent + "%";
                    secondary.Text = p.Charging ? Strings.Monitor.Charging : p.OnAc ? Strings.Monitor.PluggedIn : Strings.Monitor.OnBattery;
                    charge.Value.Text = p.Percent + "%";
                    var w = p.Charging ? p.ChargeWatts : p.DischargeWatts;
                    batt.Value.Text = $"{(w is { } v ? v.ToString("0.0") + " W · " : string.Empty)}{(p.Charging ? Strings.Monitor.Charging : p.OnAc ? Strings.Monitor.PluggedIn : Strings.Monitor.OnBattery)}";
                    temps.Value.Text = p.TemperatureC is { } t ? Fmt.Temperature(t, fahrenheit()) : "-";
                    health.Value.Text = p.HealthPercent is { } h ? h + "%" : "-";
                    cycles.Value.Text = p.Cycles?.ToString() ?? "-";
                    graph.Set(MonitorService.Series("battery"), 100, "100%");
                    peripherals.Children.Clear();
                    foreach (var per in s.Peripherals.Take(5))
                    {
                        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        row.Children.Add(UiKit.Text(per.Name, 10.5, FontWeights.Medium, ThemeKeys.TextPrimary));
                        var pv = UiKit.Text(per.Percent + "%", 10.5, FontWeights.SemiBold, ThemeKeys.TextSecondary);
                        Grid.SetColumn(pv, 1);
                        row.Children.Add(pv);
                        peripherals.Children.Add(row);
                    }

                    if (s.Peripherals.Count == 0)
                    {
                        peripherals.Children.Add(UiKit.Text(Strings.Monitor.NoPeripherals, 10.5, FontWeights.Normal, ThemeKeys.TextSecondary));
                    }
                });
                break;
            }

            case MetricKind.Power:
            {
                var system = Line(Strings.Monitor.System);
                var adapter = Line(Strings.Monitor.Adapter);
                var batt = Line(Strings.Monitor.BatteryRow);
                var remaining = Line(Strings.Monitor.TimeRemaining);
                live.Add(s =>
                {
                    if (s.Power is not { } p)
                    {
                        return;
                    }

                    var w = p.DischargeWatts ?? p.ChargeWatts;
                    primary.Text = w is { } v ? v.ToString(v < 10 ? "0.0" : "0") + " W" : "-";
                    secondary.Text = p.OnAc ? Strings.Monitor.PluggedIn : Strings.Monitor.OnBattery;
                    system.Value.Text = primary.Text;
                    adapter.Value.Text = p.OnAc ? Strings.Monitor.PluggedIn : "-";
                    batt.Value.Text = p.HasBattery ? $"{p.Percent}% · {(p.Charging ? Strings.Monitor.Charging : Strings.Monitor.OnBattery)}" : "-";
                    remaining.Value.Text = p.SecondsRemaining > 0 && !p.OnAc ? Fmt.HoursMinutes(p.SecondsRemaining) : "-";
                    graph.Set(MonitorService.Series("power"), Math.Max(1, Math.Ceiling(MonitorService.Series("power").DefaultIfEmpty(0).Max())), "W");
                });
                break;
            }

            case MetricKind.Devices:
            {
                var list = new StackPanel();
                detail.Children.Add(list);
                live.Add(s =>
                {
                    primary.Text = s.Devices.Count.ToString();
                    secondary.Text = Strings.Monitor.DeviceCount(s.Devices.Count);
                    list.Children.Clear();
                    if (s.Devices.Count == 0)
                    {
                        list.Children.Add(UiKit.Text(Strings.Monitor.NoDevices, 10.5, FontWeights.Normal, ThemeKeys.TextSecondary));
                        return;
                    }

                    foreach (var d in s.Devices)
                    {
                        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        row.Children.Add(UiKit.Text(string.IsNullOrWhiteSpace(d.Name) ? Strings.Monitor.UsbDevice : d.Name, 11, FontWeights.Medium, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis));
                        var vendor = UiKit.Text(d.Vendor, 10, FontWeights.Normal, ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis);
                        vendor.MaxWidth = 100;
                        Grid.SetColumn(vendor, 1);
                        row.Children.Add(vendor);
                        list.Children.Add(row);
                    }
                });
                break;
            }
        }

        // Process card.
        UsageKind? processKind = kind switch
        {
            MetricKind.Cpu => UsageKind.Cpu,
            MetricKind.Gpu => UsageKind.Gpu,
            MetricKind.Memory => UsageKind.Memory,
            MetricKind.Power or MetricKind.Battery => UsageKind.Energy,
            _ => null,
        };
        if (processKind is { } pk || kind == MetricKind.Network)
        {
            var heading = kind switch
            {
                MetricKind.Network => Strings.Monitor.AppsUsingNetwork,
                MetricKind.Power or MetricKind.Battery => Strings.Monitor.AppsUsingEnergy,
                _ => Strings.Monitor.HardwareUsage,
            };
            var card = new StackPanel();
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(UiKit.Text(heading, 10, FontWeights.Medium, ThemeKeys.TextTertiary));
            var taskManager = Bits.FlatIcon(Gl.OpenWindow, Strings.Monitor.OpenTaskManager, () =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
                }
                catch (Exception)
                {
                }
            }, 10, 18);
            Grid.SetColumn(taskManager, 1);
            head.Children.Add(taskManager);
            card.Children.Add(head);
            var list = kind == MetricKind.Network
                ? new ProcessList(UsageKind.Cpu, Strings.Monitor.NoNetworkApps, 0, networkNotice: true)
                : new ProcessList(processKind!.Value, kind is MetricKind.Power or MetricKind.Battery ? Strings.Monitor.NoEnergy : Strings.Monitor.NoEnergy, 0, allowForceQuit: c.Runtime.IsAvailable("kill-process"));
            list.Margin = new Thickness(0, 6, 0, 0);
            card.Children.Add(list);
            root.Children.Add(new PanelCard(card) { Margin = new Thickness(0, 10, 0, 0) });
        }

        _ = title;
        return root;
    }

    /// <summary>A minimal panel context for blocks reused inside the detail view (no editing, nothing hosted).</summary>
    private sealed class DetailContext(IContributionServices c) : ContributionServicesBase(c), IPanelContext
    {
        public string TabId => PanelTabs.Network;

        public bool IsEditing => false;

        public event Action? EditingChanged
        {
            add { }
            remove { }
        }

        public event Action? ResetRequested
        {
            add { }
            remove { }
        }

        public bool IsActive => true;

        public event Action? ActiveChanged
        {
            add { }
            remove { }
        }

        public void RequestRelayout()
        {
        }

        public void ClosePanel()
        {
        }

        public void InvokeAfterClose(Action action, int delayMs = 150)
        {
        }

        public void SetKeepOpen(bool keepOpen)
        {
        }

        public void ShowDetail(string title, string glyph, FrameworkElement view)
        {
        }

        public void CloseDetail()
        {
        }

        public void HostUtility(string utilityId)
        {
        }
    }
}
