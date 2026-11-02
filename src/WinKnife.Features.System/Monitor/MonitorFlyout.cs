// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinKnife.Core.Ui;

namespace WinKnife.Features.SystemTools;

internal static class MonitorFormat
{
    public static string Bytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var i = 0;
        while (bytes >= 1024 && i < units.Length - 1)
        {
            bytes /= 1024;
            i++;
        }

        return string.Create(CultureInfo.CurrentCulture, $"{bytes:0.#} {units[i]}");
    }

    public static string Rate(double bytesPerSecond) => Bytes(bytesPerSecond) + "/s";

    public static string Percent(double value) => string.Create(CultureInfo.CurrentCulture, $"{value:0}%");

    public static string Duration(int seconds)
    {
        if (seconds < 0)
        {
            return "";
        }

        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours} h {span.Minutes} min" : $"{span.Minutes} min";
    }
}

/// <summary>The shared "System monitor" flyout: one compact card per installed monitor feature.</summary>
internal sealed class MonitorFlyout : FlyoutWindow
{
    private sealed class Card
    {
        public required TextBlock Value { get; init; }

        public required StackPanel Content { get; init; }

        public Sparkline? Graph { get; init; }
    }

    private readonly MonitorMetric _metrics;
    private readonly Dictionary<MonitorMetric, Card> _cards = [];
    private readonly Action _onShown;
    private readonly Action _onHidden;
    private CoreBars? _coreBars;
    private TextBlock? _cpuDetail;
    private TextBlock? _netDetail;
    private TextBlock? _diskDetail;
    private TextBlock? _powerDetail;
    private TextBlock? _memDetail;
    private StackPanel? _drivePanel;
    private StackPanel? _devicePanel;
    private ProgressBar? _memBar;
    private ProgressBar? _batteryBar;
    private int _driveSignature = -1;
    private int _deviceSignature = -1;

    public MonitorFlyout(MonitorMetric metrics, Action onShown, Action onHidden)
        : base("System monitor", "", 360)
    {
        _metrics = metrics;
        _onShown = onShown;
        _onHidden = onHidden;
        Build();
    }

    public MonitorMetric Metrics => _metrics;

    public void Update(SystemSnapshot s, IReadOnlyDictionary<string, List<double>> history)
    {
        if (_cards.TryGetValue(MonitorMetric.Cpu, out var cpu))
        {
            cpu.Value.Text = MonitorFormat.Percent(s.Cpu);
            cpu.Graph!.Update(history["cpu"], null, 100);
            _coreBars?.Update(s.Cores);
            _cpuDetail!.Text = (s.CpuTempC is { } t ? string.Create(CultureInfo.CurrentCulture, $"{t:0} °C   ·   ") : "") + $"{s.Cores.Length} logical cores";
        }

        if (_cards.TryGetValue(MonitorMetric.Gpu, out var gpu))
        {
            gpu.Value.Text = s.GpuAvailable ? MonitorFormat.Percent(s.Gpu ?? 0) : "n/a";
            gpu.Graph!.Update(history["gpu"], null, 100);
        }

        if (_cards.TryGetValue(MonitorMetric.Memory, out var mem))
        {
            mem.Value.Text = MonitorFormat.Percent(s.MemPercent);
            mem.Graph!.Update(history["mem"], null, 100);
            _memBar!.Value = s.MemPercent;
            _memDetail!.Text = $"{MonitorFormat.Bytes(s.MemUsed)} used of {MonitorFormat.Bytes(s.MemTotal)}";
        }

        if (_cards.TryGetValue(MonitorMetric.Network, out var net))
        {
            net.Value.Text = $"↓ {MonitorFormat.Rate(s.NetDown)}   ↑ {MonitorFormat.Rate(s.NetUp)}";
            var peak = Math.Max(1024, Math.Max(history["down"].DefaultIfEmpty(0).Max(), history["up"].DefaultIfEmpty(0).Max()));
            net.Graph!.Update(history["down"], history["up"], peak);
            _netDetail!.Text = $"Graph peak {MonitorFormat.Rate(peak)}  ·  accent = download, grey = upload";
        }

        if (_cards.TryGetValue(MonitorMetric.Disk, out var disk))
        {
            disk.Value.Text = MonitorFormat.Percent(s.DiskActivity);
            disk.Graph!.Update(history["disk"], null, 100);
            _diskDetail!.Text = $"Read {MonitorFormat.Rate(s.DiskRead)}   Write {MonitorFormat.Rate(s.DiskWrite)}";
            UpdateDrives(s.Drives);
        }

        if (_cards.TryGetValue(MonitorMetric.Power, out var power) && s.Power is { } p)
        {
            if (p.HasBattery)
            {
                power.Value.Text = MonitorFormat.Percent(p.Percent);
                _batteryBar!.Value = p.Percent;
                _batteryBar.Visibility = Visibility.Visible;
                var state = p.Charging ? "Charging" : p.OnAc ? "Plugged in" : "On battery";
                var left = !p.OnAc && p.SecondsRemaining > 0 ? $"  ·  {MonitorFormat.Duration(p.SecondsRemaining)} left" : "";
                _powerDetail!.Text = state + left;
            }
            else
            {
                power.Value.Text = "AC";
                _batteryBar!.Visibility = Visibility.Collapsed;
                _powerDetail!.Text = "No battery - running on mains power";
            }
        }

        if (_cards.ContainsKey(MonitorMetric.Devices))
        {
            UpdateDevices(s.Devices);
        }
    }

    protected override void OnShown() => _onShown();

    protected override void OnHidden() => _onHidden();

    private void Build()
    {
        if (_metrics.HasFlag(MonitorMetric.Cpu))
        {
            var card = AddCard(MonitorMetric.Cpu, "CPU", "", withGraph: true);
            _coreBars = new CoreBars { Height = 26, Margin = new Thickness(0, 8, 0, 0) };
            _cpuDetail = Muted();
            card.Content.Children.Add(_coreBars);
            card.Content.Children.Add(_cpuDetail);
        }

        if (_metrics.HasFlag(MonitorMetric.Gpu))
        {
            AddCard(MonitorMetric.Gpu, "GPU", "", withGraph: true);
        }

        if (_metrics.HasFlag(MonitorMetric.Memory))
        {
            var card = AddCard(MonitorMetric.Memory, "Memory", "", withGraph: true);
            _memBar = new ProgressBar { Height = 6, Maximum = 100, Margin = new Thickness(0, 8, 0, 0) };
            _memDetail = Muted();
            card.Content.Children.Add(_memBar);
            card.Content.Children.Add(_memDetail);
        }

        if (_metrics.HasFlag(MonitorMetric.Network))
        {
            var card = AddCard(MonitorMetric.Network, "Network", "", withGraph: true);
            _netDetail = Muted();
            card.Content.Children.Add(_netDetail);
        }

        if (_metrics.HasFlag(MonitorMetric.Disk))
        {
            var card = AddCard(MonitorMetric.Disk, "Disks", "", withGraph: true);
            _diskDetail = Muted();
            _drivePanel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            card.Content.Children.Add(_diskDetail);
            card.Content.Children.Add(_drivePanel);
        }

        if (_metrics.HasFlag(MonitorMetric.Power))
        {
            var card = AddCard(MonitorMetric.Power, "Power", "", withGraph: false);
            _batteryBar = new ProgressBar { Height = 6, Maximum = 100, Margin = new Thickness(0, 8, 0, 0) };
            _powerDetail = Muted();
            card.Content.Children.Add(_batteryBar);
            card.Content.Children.Add(_powerDetail);
        }

        if (_metrics.HasFlag(MonitorMetric.Devices))
        {
            var card = AddCard(MonitorMetric.Devices, "Connected devices", "", withGraph: false);
            _devicePanel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            card.Content.Children.Add(_devicePanel);
            card.Value.Text = "";
        }
    }

    private Card AddCard(MonitorMetric metric, string title, string glyph, bool withGraph)
    {
        var header = new DockPanel();
        var icon = new TextBlock { Text = glyph, FontSize = 14, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.IconTextStyle);
        DockPanel.SetDock(icon, Dock.Left);
        var value = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
        value.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Text);
        DockPanel.SetDock(value, Dock.Right);
        var name = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center };
        name.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextMuted);
        header.Children.Add(icon);
        header.Children.Add(value);
        header.Children.Add(name);

        var content = new StackPanel();
        content.Children.Add(header);
        Sparkline? graph = null;
        if (withGraph)
        {
            graph = new Sparkline(36) { Margin = new Thickness(0, 8, 0, 0) };
            content.Children.Add(graph);
        }

        var border = new Border { Margin = new Thickness(0, 0, 0, 8), Child = content };
        border.SetResourceReference(Border.StyleProperty, ThemeKeys.CardStyle);
        Body.Children.Add(border);

        var card = new Card { Value = value, Content = content, Graph = graph };
        _cards[metric] = card;
        return card;
    }

    private static TextBlock Muted()
    {
        var block = new TextBlock { Margin = new Thickness(0, 6, 0, 0), FontSize = 12 };
        block.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.MutedTextStyle);
        return block;
    }

    private void UpdateDrives(IReadOnlyList<DriveSnap> drives)
    {
        var signature = drives.Aggregate(drives.Count, (acc, d) => HashCode.Combine(acc, d.Name, d.FreeBytes / (256L * 1024 * 1024)));
        if (signature == _driveSignature || _drivePanel is null)
        {
            return;
        }

        _driveSignature = signature;
        _drivePanel.Children.Clear();
        foreach (var d in drives)
        {
            var used = d.TotalBytes - d.FreeBytes;
            var label = string.IsNullOrEmpty(d.Label) ? d.Name : $"{d.Name}  {d.Label}";
            var line = new TextBlock { Text = $"{label}   {MonitorFormat.Bytes(d.FreeBytes)} free of {MonitorFormat.Bytes(d.TotalBytes)}", Margin = new Thickness(0, 4, 0, 3), FontSize = 12 };
            line.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Text);
            var bar = new ProgressBar { Height = 5, Maximum = 100, Value = d.TotalBytes > 0 ? used * 100.0 / d.TotalBytes : 0 };
            _drivePanel.Children.Add(line);
            _drivePanel.Children.Add(bar);
        }
    }

    private void UpdateDevices(IReadOnlyList<string> devices)
    {
        var signature = devices.Aggregate(devices.Count, (acc, d) => HashCode.Combine(acc, d));
        if (signature == _deviceSignature || _devicePanel is null)
        {
            return;
        }

        _deviceSignature = signature;
        _devicePanel.Children.Clear();
        if (devices.Count == 0)
        {
            _devicePanel.Children.Add(Muted());
            ((TextBlock)_devicePanel.Children[0]).Text = "No USB devices found.";
            return;
        }

        foreach (var name in devices.Take(12))
        {
            var block = new TextBlock { Text = name, Margin = new Thickness(0, 2, 0, 2), TextTrimming = TextTrimming.CharacterEllipsis };
            block.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Text);
            _devicePanel.Children.Add(block);
        }

        if (devices.Count > 12)
        {
            var more = Muted();
            more.Text = $"+{devices.Count - 12} more";
            _devicePanel.Children.Add(more);
        }
    }

    /// <summary>One thin bar per logical core.</summary>
    private sealed class CoreBars : FrameworkElement
    {
        private double[] _values = [];

        public void Update(double[] values)
        {
            _values = values;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            var count = _values.Length;
            if (count == 0 || ActualWidth < 4)
            {
                return;
            }

            var track = TryFindResource(ThemeKeys.SurfaceHover) as Brush ?? Brushes.LightGray;
            var fill = TryFindResource(ThemeKeys.Accent) as Brush ?? Brushes.SteelBlue;
            var gap = 2.0;
            var w = Math.Max(2, (ActualWidth - ((count - 1) * gap)) / count);
            for (var i = 0; i < count; i++)
            {
                var x = i * (w + gap);
                dc.DrawRoundedRectangle(track, null, new Rect(x, 0, w, ActualHeight), 1.5, 1.5);
                var h = Math.Max(1, ActualHeight * _values[i] / 100);
                dc.DrawRoundedRectangle(fill, null, new Rect(x, ActualHeight - h, w, h), 1.5, 1.5);
            }
        }
    }
}
