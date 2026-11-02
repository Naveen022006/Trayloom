// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.SystemTools;

/// <summary>Strings of the monitor panel tabs (spec 01 sections 3.7 and 5.4-5.7).</summary>
internal static partial class Strings
{
    public static class Monitor
    {
        public const string Temperatures = "Temperatures";
        public const string SensorsUnavailable = "Sensors unavailable on this PC";
        public const string HardwareUsage = "Hardware usage";
        public const string OpenTaskManager = "Open Task Manager";
        public const string AppsUsingCpu = "Apps using CPU";
        public const string AppsUsingGpu = "Apps using GPU";
        public const string AppsUsingMemory = "Apps using memory";
        public const string AppsUsingNetwork = "Apps using network";
        public const string AppsUsingEnergy = "Apps using significant energy";
        public const string Measuring = "Measuring…";
        public const string NoNetworkApps = "No apps using network now";
        public const string NetworkAppsAdmin = "Run as administrator to see apps using the network";
        public const string NoEnergy = "No significant energy use";
        public const string Memory = "Memory";
        public const string Pressure = "Pressure";
        public const string Compressed = "Compressed";
        public const string CachedFiles = "Cached files";
        public const string SwapUsed = "Swap used";
        public const string PerCore = "Per core";
        public const string Graphs = "Graphs";
        public const string Normal = "Normal";
        public const string Caution = "Caution";
        public const string Critical = "Critical";
        public const string Download = "Download";
        public const string Upload = "Upload";
        public const string ThisSession = "This session";
        public const string LocalIPv4 = "Local IPv4";
        public const string SpeedTest = "Speed test";
        public const string TestAgain = "Test again";
        public const string Testing = "Testing…";
        public const string TestFailed = "Test failed";
        public const string NoDisks = "No mounted disks found.";
        public const string SelectDisk = "Select disk";
        public const string DiskUsage = "Disk usage";
        public const string LiveActivity = "Live activity";
        public const string Smart = "SMART";
        public const string SmartUnavailable = "SMART unavailable for this disk";
        public const string ExternalProtection = "External protection";
        public const string Tools = "Tools";
        public const string Eject = "Eject";
        public const string EjectAll = "Eject all";
        public const string EjectBefore = "Eject before unplugging.";
        public const string NoExternal = "No external disk ready to eject.";
        public const string Ejecting = "Ejecting…";
        public const string ReadyToRemove = "Ready to remove";
        public const string EjectFailed = "Could not eject";
        public const string Open = "Open";
        public const string Storage = "Storage";
        public const string Read = "Read";
        public const string Write = "Write";
        public const string PowerUnavailable = "Power metrics unavailable on this PC";
        public const string Charge = "Battery";
        public const string BatteryTemperature = "Battery temperature";
        public const string System = "System";
        public const string Adapter = "Adapter";
        public const string PluggedIn = "Plugged in";
        public const string BatteryRow = "Battery";
        public const string Charging = "Charging";
        public const string OnBattery = "On battery";
        public const string TimeRemaining = "Battery time remaining";
        public const string SystemEstimate = "System estimate";
        public const string Calculating = "Calculating…";
        public const string Health = "Battery health";
        public const string PeripheralBattery = "Peripheral battery";
        public const string ConnectedDevices = "Connected Devices";
        public const string NoDevices = "No external devices connected";
        public const string UsbDevice = "USB Device";
        public const string NoPeripherals = "No devices found";
        public const string Logical = "Logical CPU cores";
        public const string CoresCaption = "Each bar is one logical core. Its fill height shows utilization, not remaining performance.";

        public static string UpFor(string value) => $"Up for {value}";

        public static string Cycles(int n) => n == 1 ? "1 Cycle" : $"{n} Cycles";

        public static string DeviceCount(int n) => n == 1 ? "1 device connected" : $"{n} devices connected";

        public static string Core(int n) => $"Core {n}";
    }
}

/// <summary>The graph used by the panel blocks: Core sparklines (optionally two series) plus the dashed-rule ceiling label.</summary>
internal sealed class GraphView : Grid
{
    private readonly Sparkline _primary;
    private readonly Sparkline? _secondary;
    private readonly TextBlock _ceiling;
    private readonly IAppSettings _app;

    public GraphView(IAppSettings app, string lineKey, double height = 30, string? secondKey = null)
    {
        _app = app;
        Height = height;
        _primary = new Sparkline(lineKey, height);
        Children.Add(_primary);
        if (secondKey is not null)
        {
            _secondary = new Sparkline(secondKey, height);
            Children.Add(_secondary);
        }

        _ceiling = UiKit.Text(string.Empty, 9, FontWeights.Normal, ThemeKeys.TextSecondary);
        var chip = new Border { CornerRadius = new CornerRadius(3), Padding = new Thickness(3, 0, 3, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0), Child = _ceiling };
        chip.SetResourceReference(Border.BackgroundProperty, ThemeKeys.PanelBase);
        chip.Opacity = 0.9;
        Children.Add(chip);
        chip.Visibility = Visibility.Collapsed;
        _chip = chip;
    }

    private readonly Border _chip;

    public void Set(IReadOnlyList<double> primary, double ceiling, string label, IReadOnlyList<double>? secondary = null)
    {
        Visibility = primary.Count >= 2 ? Visibility.Visible : Visibility.Collapsed;
        _primary.SetValues(primary, ceiling);
        _secondary?.SetValues(secondary ?? [], ceiling);
        _ceiling.Text = label;
        _chip.Visibility = _app.Get<bool>("monitorGraphScale") && primary.Count >= 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>A "round" ceiling (1, 2, 5 x 10^n) at or above the value.</summary>
    public static double RoundCeiling(double value, double unit)
    {
        var scaled = Math.Max(value, unit) / unit;
        var power = Math.Pow(10, Math.Floor(Math.Log10(scaled)));
        foreach (var step in new[] { 1.0, 2.0, 5.0, 10.0 })
        {
            if (scaled <= step * power)
            {
                return step * power * unit;
            }
        }

        return 10 * power * unit;
    }
}

/// <summary>A card that samples while it is visible: collects updaters and keeps the lease on the monitor hub.</summary>
internal sealed class LiveHost
{
    private readonly List<Action<MonitorSnapshot>> _updaters = [];
    private IDisposable? _lease;
    private readonly Needs _needs;
    private readonly Action<MonitorSnapshot> _handler;

    public LiveHost(FrameworkElement owner, Needs needs)
    {
        _needs = needs;
        _handler = s => owner.Dispatcher.BeginInvoke(() => Apply(s));
        Bits.WhileVisible(owner, visible =>
        {
            if (visible)
            {
                _lease ??= MonitorService.Foreground(_needs);
                MonitorService.Updated -= _handler;
                MonitorService.Updated += _handler;
                if (MonitorService.Last is { } last)
                {
                    Apply(last);
                }

                MonitorService.RequestNow();
            }
            else
            {
                MonitorService.Updated -= _handler;
                _lease?.Dispose();
                _lease = null;
            }
        });
    }

    public void Add(Action<MonitorSnapshot> updater) => _updaters.Add(updater);

    private void Apply(MonitorSnapshot s)
    {
        foreach (var updater in _updaters.ToList())
        {
            try
            {
                updater(s);
            }
            catch (Exception)
            {
            }
        }
    }
}

/// <summary>The expandable per-app breakdown (up to 15 rows) with "Measuring…" and an empty text; refreshes while visible.</summary>
internal sealed class ProcessList : StackPanel
{
    private readonly UsageKind _kind;
    private readonly string _empty;
    private readonly DispatcherTimer _timer;
    private ProcessSampler? _sampler;
    private bool _forceQuit;

    public ProcessList(UsageKind kind, string emptyText, double indent = 38, bool allowForceQuit = false, bool networkNotice = false)
    {
        _kind = kind;
        _empty = emptyText;
        _forceQuit = allowForceQuit;
        Margin = new Thickness(indent, 4, 0, 0);
        var seconds = kind == UsageKind.Memory ? 4 : Math.Max(1, MonitorService.App?.Get<int>("monitorIntervalSeconds", 2) ?? 2);
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(seconds) };
        _timer.Tick += (_, _) => Refresh();
        if (networkNotice)
        {
            Children.Add(UiKit.Text(Strings.Monitor.NetworkAppsAdmin, 10.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true));
            return;
        }

        Children.Add(UiKit.Text(Strings.Monitor.Measuring, 10.5, FontWeights.Normal, ThemeKeys.TextSecondary));
        Bits.WhileVisible(this, visible =>
        {
            if (visible)
            {
                _sampler ??= new ProcessSampler(_kind);
                Refresh();
                _timer.Start();
            }
            else
            {
                _timer.Stop();
                _sampler?.Dispose();
                _sampler = null;
            }
        });
    }

    private void Refresh()
    {
        if (_sampler is null)
        {
            return;
        }

        var sampler = _sampler;
        Task.Run(() => sampler.Sample()).ContinueWith(t => Dispatcher.BeginInvoke(() => Render(t.IsCompletedSuccessfully ? t.Result : null)));
    }

    private void Render(List<ProcessUsage>? rows)
    {
        Children.Clear();
        if (rows is null)
        {
            Children.Add(UiKit.Text(Strings.Monitor.Measuring, 10.5, FontWeights.Normal, ThemeKeys.TextSecondary));
            return;
        }

        if (rows.Count == 0)
        {
            Children.Add(UiKit.Text(_empty, 10.5, FontWeights.Normal, ThemeKeys.TextSecondary));
            return;
        }

        foreach (var row in rows)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2), Background = Brushes.Transparent };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (row.Icon is not null)
            {
                grid.Children.Add(new Image { Source = row.Icon, Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center });
            }
            else
            {
                grid.Children.Add(UiKit.Icon(Gl.AllApps, 11, ThemeKeys.TextSecondary));
            }

            var name = UiKit.Text(row.Name, 10.5, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
            name.Margin = new Thickness(7, 0, 6, 0);
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);
            var value = UiKit.Text(row.Display, 10.5, FontWeights.Medium, ThemeKeys.TextSecondary);
            value.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            Grid.SetColumn(value, 2);
            grid.Children.Add(value);
            if (_forceQuit)
            {
                var pid = row.Pid;
                var menu = new ContextMenu();
                var item = new MenuItem { Header = "Force Quit" };
                item.Click += (_, _) => ForceQuit(pid, row.Name);
                menu.Items.Add(item);
                grid.ContextMenu = menu;
            }

            Children.Add(grid);
        }
    }

    private static void ForceQuit(int pid, string name)
    {
        if (MessageBox.Show($"Force quit {name}?", "Force Quit", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            p.Kill();
        }
        catch (Exception)
        {
        }
    }
}

/// <summary>One bar per logical core, grouped by core class (spec 01 5.4 "CPUCoreMatrix").</summary>
internal sealed class CoreMatrix : FrameworkElement
{
    private double[] _cores = [];
    private string[] _classes = [];

    public CoreMatrix()
    {
        ToolTip = Strings.Monitor.CoresCaption;
        ThemeManager.ThemeChanged += _ => Dispatcher.BeginInvoke(InvalidateVisual);
    }

    public void Set(double[] cores, string[] classes)
    {
        var relayout = cores.Length != _cores.Length || !classes.SequenceEqual(_classes);
        _cores = cores;
        _classes = classes;
        if (relayout)
        {
            InvalidateMeasure();
        }

        InvalidateVisual();
    }

    private List<(string Name, int Start, int Count)> Groups()
    {
        var groups = new List<(string, int, int)>();
        var order = new List<string>();
        for (var i = 0; i < _cores.Length; i++)
        {
            var c = i < _classes.Length ? _classes[i] : "CPU";
            if (!order.Contains(c))
            {
                order.Add(c);
            }
        }

        // Performance first, then the rest in order of appearance.
        foreach (var name in order.OrderBy(n => n == "Performance" ? 0 : n == "Efficiency" ? 2 : 1))
        {
            var indices = Enumerable.Range(0, _cores.Length).Where(i => (i < _classes.Length ? _classes[i] : "CPU") == name).ToList();
            groups.Add((name, indices.First(), indices.Count));
        }

        return groups;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 270 : availableSize.Width;
        return new Size(width, _cores.Length == 0 ? 0 : 88 + 16);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_cores.Length == 0)
        {
            return;
        }

        var border = new Pen(UiKit.BrushOf(ThemeKeys.Divider), 1);
        dc.DrawRoundedRectangle(UiKit.BrushOf(ThemeKeys.TileFill), border, new Rect(0.5, 0.5, ActualWidth - 1, ActualHeight - 1), 5, 5);
        var groups = Groups();
        var inner = ActualWidth - 16;
        var weights = groups.Select(g => g.Name is "Super" ? 1.5 : g.Name is "Performance" ? 1.25 : 1.0).ToList();
        var natural = groups.Select((g, i) => (g.Count * 14 * weights[i]) + ((g.Count - 1) * 4)).Sum() + ((groups.Count - 1) * 12);
        var scale = Math.Min(1.0, inner / Math.Max(1, natural));
        var x = 8.0;
        var fill = UiKit.BrushOf(ThemeKeys.TextPrimary);
        var track = UiKit.BrushOf(ThemeKeys.TileFill);
        var outline = new Pen(UiKit.BrushOf(ThemeKeys.TextTertiary), 0.75);
        for (var gi = 0; gi < groups.Count; gi++)
        {
            var (name, start, count) = groups[gi];
            var barWidth = 14 * weights[gi] * scale;
            var left = x;
            for (var i = 0; i < count; i++)
            {
                var index = start + i;
                var v = Math.Clamp(_cores[index] / 100.0, 0, 1);
                var rect = new Rect(x, 8, barWidth, 60);
                dc.DrawRoundedRectangle(track, outline, rect, 2, 2);
                var h = Math.Max(0, v * 60);
                if (h > 0)
                {
                    var bg = new SolidColorBrush(((SolidColorBrush)fill).Color) { Opacity = 0.78 };
                    dc.DrawRoundedRectangle(bg, null, new Rect(x, 8 + 60 - h, barWidth, h), 2, 2);
                }

                x += barWidth + (4 * scale);
            }

            var label = name == "CPU" ? Strings.Monitor.Logical : name + " ×" + count;
            var text = new FormattedText(label, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(UiKit.TextFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 10, UiKit.BrushOf(ThemeKeys.TextSecondary), VisualTreeHelper.GetDpi(this).PixelsPerDip)
            {
                MaxTextWidth = Math.Max(20, x - left - (4 * scale)),
                MaxLineCount = 2,
                Trimming = TextTrimming.CharacterEllipsis,
            };
            dc.DrawText(text, new Point(left, 8 + 60 + 2));
            x += 12;
        }
    }
}

/// <summary>Builds a card of reorderable, hideable blocks (the System / Network / Disks / Power tabs) with the edit mode of the host.</summary>
internal static class BlockCard
{
    public sealed record Block(string Id, string? Key, Func<FrameworkElement?> Build, bool Available = true);

    public static FrameworkElement Create(IPanelContext c, string orderKey, string defaultOrder, IReadOnlyList<Block> blocks, string? emptyText = null, bool bare = false)
    {
        var app = c.App;
        var host = new StackPanel();
        var card = bare ? null : new PanelCard(host, 10);

        void Render()
        {
            host.Children.Clear();
            var order = app.Get<string>(orderKey, defaultOrder).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            foreach (var id in defaultOrder.Split(','))
            {
                if (!order.Contains(id))
                {
                    order.Add(id);
                }
            }

            var ordered = order.Select(id => blocks.FirstOrDefault(b => b.Id == id)).Where(b => b is not null && b.Available).Cast<Block>().ToList();
            var any = false;
            foreach (var block in ordered)
            {
                var on = block.Key is null || app.Get<bool>(block.Key);
                if (!on && !c.IsEditing)
                {
                    continue;
                }

                var view = block.Build();
                if (view is null)
                {
                    continue;
                }

                any = true;
                FrameworkElement shown = view;
                if (c.IsEditing)
                {
                    shown = EditRow(c, view, block, on, host, app, orderKey, Render);
                }

                if (host.Children.Count > 0)
                {
                    var divider = UiKit.Divider();
                    divider.Margin = new Thickness(0, 10, 0, 10);
                    host.Children.Add(divider);
                }

                host.Children.Add(shown);
            }

            if (!any)
            {
                host.Children.Add(UiKit.Text(emptyText ?? string.Empty, 10.5, FontWeights.Normal, ThemeKeys.TextTertiary));
            }
        }

        void OnEditing() => host.Dispatcher.BeginInvoke(Render);
        void OnReset()
        {
            app.Set(orderKey, defaultOrder);
            foreach (var b in blocks.Where(b => b.Key is not null))
            {
                app.Set(b.Key!, b.Key == "monitorPwrPeripherals" ? false : true);
            }

            host.Dispatcher.BeginInvoke(Render);
        }

        host.Loaded += (_, _) =>
        {
            c.EditingChanged += OnEditing;
            c.ResetRequested += OnReset;
        };
        host.Unloaded += (_, _) =>
        {
            c.EditingChanged -= OnEditing;
            c.ResetRequested -= OnReset;
        };
        Render();
        return bare ? host : card!;
    }

    private static FrameworkElement EditRow(IPanelContext c, FrameworkElement view, Block block, bool on, StackPanel host, IAppSettings app, string orderKey, Action render)
    {
        var grid = new Grid { Tag = block.Id };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(9) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var handle = new Border { Width = 16, Background = Brushes.Transparent, Child = UiKit.Icon(Gl.Handle, 11, ThemeKeys.TextTertiary, FontWeights.SemiBold), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
        grid.Children.Add(handle);
        view.IsHitTestVisible = false;
        view.Opacity = on ? 1 : 0.4;
        Grid.SetColumn(view, 2);
        grid.Children.Add(view);
        var eye = Bits.FlatIcon(on ? Gl.Eye : Gl.EyeOff, null, () =>
        {
            if (block.Key is not null)
            {
                app.Set(block.Key, !on);
            }

            render();
        }, 11, 22, on ? ThemeKeys.Accent : ThemeKeys.TextSecondary);
        eye.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(eye, 3);
        if (block.Key is not null)
        {
            grid.Children.Add(eye);
        }
        Bits.AttachReorder(host, grid, handle, rows =>
        {
            var ids = rows.OfType<Grid>().Where(g => g.Tag is string).Select(g => (string)g.Tag).ToList();
            var existing = app.Get<string>(orderKey).Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
            foreach (var id in existing.Where(i => !ids.Contains(i)))
            {
                ids.Add(id);
            }

            app.Set(orderKey, string.Join(',', ids));
        });
        _ = c;
        return grid;
    }
}
