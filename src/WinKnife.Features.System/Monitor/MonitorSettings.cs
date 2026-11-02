// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.SystemTools;

/// <summary>Strings of the Monitor settings page (spec 02a section 5).</summary>
internal static partial class Strings
{
    public static class MonitorPage
    {
        public static  string Preview => Loc.T("Tray preview");
        public static  string InTray => Loc.T("In the tray");
        public static  string InPanel => Loc.T("In the panel");
        public static  string ReadingsAndAlerts => Loc.T("Readings and alerts");
        public static  string TrayIntro => Loc.T("Choose what appears next to the icon in the tray.");
        public static  string DragCaption => Loc.T("Drag to reorder. Click the checkmark to show or hide.");
        public static  string UsageDisplay => Loc.T("Usage display");
        public static  string Values => Loc.T("Values");
        public static  string Bars => Loc.T("Bars");
        public static  string BarsCaption => Loc.T("Bars apply to CPU, GPU, memory and disk usage. Other readings stay numeric.");
        public static  string BarColors => Loc.T("Bar colors and limits");
        public static  string NormalColor => Loc.T("Normal color");
        public static  string MediumColor => Loc.T("Medium color");
        public static  string HighColor => Loc.T("High color");
        public static  string MediumFrom => Loc.T("Medium from");
        public static  string HighFrom => Loc.T("High from");
        public static  string Combine => Loc.T("Combine usage and temperature");
        public static  string CombineCaption => Loc.T("When usage and temperature for the same item are enabled, show them in one block.");
        public static  string Spacing => Loc.T("Tray spacing");
        public static  string Standard => Loc.T("Standard");
        public static  string Compact => Loc.T("Compact");
        public static  string HideIcon => Loc.T("Hide the app icon while metrics are shown");
        public static  string HideIconCaption => Loc.T("The icon returns by itself when metrics leave the tray and when there is something to signal (an update ready, “Keep awake” running or the microphone muted).");
        public static  string Separate => Loc.T("Separate metrics into their own items");
        public static  string SeparateCaption => Loc.T("Separates active blocks in the tray and keeps usage and temperature together when combine is on.");
        public static  string PanelCaption => Loc.T("Open a block to choose what it shows.");
        public static  string GraphScale => Loc.T("Show graph scale");
        public static  string UpdateEvery => Loc.T("Update every");
        public static  string Temperatures => Loc.T("Temperatures");
        public static  string NetworkUnit => Loc.T("Network speed unit");
        public static  string MeasureMemory => Loc.T("Measure memory as");
        public static  string MemoryUsed => Loc.T("Memory Used");
        public static  string AppMemory => Loc.T("App Memory");
        public static  string MoreOptions => Loc.T("More options");
        public static  string PressureDot => Loc.T("Pressure dot");
        public static  string UploadFirst => Loc.T("Upload above download");
        public static  string DiskDisplay => Loc.T("Disk display");
        public static  string UsedPercentage => Loc.T("Used percentage");
        public static  string AvailableSpace => Loc.T("Available space");
        public static  string UsedSpace => Loc.T("Used space");
        public static  string ShowInPanel => Loc.T("Show in panel");
    }
}

/// <summary>Temperature sources are probed once; tiles and alerts that need one are hidden when none exists.</summary>
internal static class TempProbe
{
    private static bool? _cpu;
    private static bool? _battery;

    public static bool Cpu => _cpu ??= Probe();

    public static bool Battery => _battery ??= false;

    private static bool Probe()
    {
        var rows = Wmi.Query(@"root\CIMV2", "SELECT Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation", "Temperature");
        if (rows.Any(r => Wmi.ToInt(r["Temperature"]) > 273))
        {
            return true;
        }

        return Wmi.Query(@"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature", "CurrentTemperature").Any(r => Wmi.ToInt(r["CurrentTemperature"]) > 2732);
    }
}

internal static class MonitorSettingsUi
{
    private static (string Id, string Title, string Glyph)[] Metrics => _loc_Metrics.Value;

    private static readonly LocValue<(string Id, string Title, string Glyph)[]> _loc_Metrics = new(() => [
        ("cpu", Loc.T("CPU"), Gl.Cpu), ("cpuTemperature", Loc.T("CPU temperature"), Gl.Cpu), ("gpu", Loc.T("GPU"), Gl.Gpu), ("gpuTemperature", Loc.T("GPU temperature"), Gl.Gpu),
        ("memory", Loc.T("Memory"), Gl.Memory), ("battery", Loc.T("Battery"), Gl.Battery), ("batteryTime", Loc.T("Battery time remaining"), Gl.Clock),
        ("batteryTemperature", Loc.T("Battery temperature"), Gl.Battery), ("peripheralBattery", Loc.T("Peripheral battery"), Gl.Keyboard), ("network", Loc.T("Network"), Gl.Network),
        ("diskUsage", Loc.T("Disk usage"), Gl.Disk), ("diskActivity", Loc.T("Live activity"), Gl.Disk), ("connectedDevices", Strings.Monitor.ConnectedDevices, Gl.Usb), ("power", Loc.T("Power"), Gl.Plug),
    ]);

    private static FrameworkElement Heading(string text, string glyph)
    {
        // Group heading: a divider and the label (title3 semibold) with its icon.
        var stack = new StackPanel();
        stack.Children.Add(UiKit.Divider());
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var icon = UiKit.Icon(glyph, 14, ThemeKeys.TextPrimary, FontWeights.SemiBold);
        icon.Margin = new Thickness(0, 0, 8, 0);
        row.Children.Add(icon);
        row.Children.Add(UiKit.Text(text, 15, FontWeights.SemiBold, ThemeKeys.TextPrimary));
        stack.Children.Add(row);
        return stack;
    }

    private static bool Available(IContributionServices c, string id)
    {
        if (!c.Runtime.IsAvailable(MetricBlocks.FeatureOf(id)))
        {
            return false;
        }

        return id switch
        {
            "battery" or "batteryTime" or "peripheralBattery" or "power" => PowerStatus.HasBattery || id == "peripheralBattery",
            "batteryTemperature" => PowerStatus.HasBattery && TempProbe.Battery,
            "cpuTemperature" => TempProbe.Cpu,
            "gpuTemperature" => true,
            _ => true,
        };
    }

    // ------------------------------------------------------------------------------------------------ preview

    public static FrameworkElement Preview(ISettingsContext c)
    {
        var app = c.App;
        var stack = new StackPanel();
        stack.Children.Add(UiKit.Text(Strings.MonitorPage.Preview, 11, FontWeights.Normal, ThemeKeys.TextSecondary));
        var bar = new Border { Height = 32, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromArgb(0xD1, 0, 0, 0)), Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(14, 0, 14, 0) };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var dim = new SolidColorBrush(Color.FromArgb(0x80, 255, 255, 255));
        row.Children.Add(new TextBlock { Text = Gl.Wifi, FontFamily = UiKit.IconFont, FontSize = 12, Foreground = dim, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
        if (PowerStatus.HasBattery)
        {
            row.Children.Add(new TextBlock { Text = "", FontFamily = UiKit.IconFont, FontSize = 12, Foreground = dim, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
        }

        var image = new Image { Stretch = Stretch.None, SnapsToDevicePixels = true, VerticalAlignment = VerticalAlignment.Center };
        var mark = BrandMark.Create(20, ThemeKeys.OnAccent);
        mark.Fill = Brushes.White;
        row.Children.Add(image);
        row.Children.Add(mark);
        bar.Child = row;
        stack.Children.Add(bar);

        MonitorSnapshot? shown = null;
        void Render(MonitorSnapshot? s)
        {
            shown = s ?? shown ?? new MonitorSnapshot();
            var blocks = MetricBlocks.Build(app, c.Runtime, shown);
            var palette = MetricBlocks.PaletteFor(app, Colors.White);
            var compact = app.Get<string>("menuBarMetricSpacing") == "compact";
            var dpi = VisualTreeHelper.GetDpi(bar).PixelsPerDip;
            var separate = app.Get<bool>("menuBarSeparateMetrics");
            if (blocks.Count == 0)
            {
                image.Source = null;
                image.Margin = new Thickness(0);
            }
            else
            {
                var (visual, width, _) = MetricBlocks.Draw(blocks, palette, compact, dpi, separate ? 16 : compact ? 7 : 10);
                image.Source = MetricBlocks.Render(visual, width, 21, dpi);
                image.Margin = new Thickness(0, 0, 12, 0);
            }

            mark.Visibility = app.Get<bool>("menuBarHideIconWithMetrics") && blocks.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        IDisposable? lease = null;
        void OnUpdated(MonitorSnapshot s) => stack.Dispatcher.BeginInvoke(() => Render(s));
        void OnSetting(string key)
        {
            if (key.StartsWith("menuBar", StringComparison.Ordinal) || key is "temperatureUnit" or "networkSpeedUnit" or "monitorMemoryMetric")
            {
                stack.Dispatcher.BeginInvoke(() => Render(MonitorService.Last));
            }
        }

        Bits.WhileVisible(stack, visible =>
        {
            if (visible)
            {
                lease = MonitorService.Foreground(MetricBlocks.NeedsOf(app, c.Runtime));
                MonitorService.Updated += OnUpdated;
                app.Changed += OnSetting;
                Render(MonitorService.Last);
            }
            else
            {
                MonitorService.Updated -= OnUpdated;
                app.Changed -= OnSetting;
                lease?.Dispose();
                lease = null;
            }
        });
        Render(MonitorService.Last);
        return stack;
    }

    // ------------------------------------------------------------------------------------------------ menu bar cards

    public static FrameworkElement MenuBar(ISettingsContext c)
    {
        var app = c.App;
        var root = new StackPanel();
        root.Children.Add(Heading(Strings.MonitorPage.InTray, Gl.Equalizer));

        var tiles = new SettingsCard();
        tiles.Add(UiKit.Text(Strings.MonitorPage.TrayIntro, 13, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true));
        tiles.Add(MetricTiles(c));
        tiles.AddCaption(Strings.MonitorPage.DragCaption);
        tiles.Margin = new Thickness(0, 10, 0, 0);
        root.Children.Add(tiles);

        var style = new SettingsCard(Strings.MonitorPage.UsageDisplay);
        var cells = new Grid();
        cells.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cells.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        cells.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var detail = new StackPanel();
        var valuesCell = StyleCell(Strings.MonitorPage.Values, false);
        var barsCell = StyleCell(Strings.MonitorPage.Bars, true);
        Grid.SetColumn(barsCell.Cell, 2);
        cells.Children.Add(valuesCell.Cell);
        cells.Children.Add(barsCell.Cell);

        void Style()
        {
            var bars = app.Get<string>("menuBarMetricAppearance") == "bars";
            valuesCell.Select(!bars);
            barsCell.Select(bars);
            detail.Children.Clear();
            if (bars)
            {
                detail.Children.Add(BarSettings(app));
            }
            else
            {
                detail.Children.Add(new SettingsRow(Gl.Thermo, Strings.MonitorPage.Combine, Strings.MonitorPage.CombineCaption, new MacSwitch(app.Get<bool>("menuBarCombineTemperatures"), SwitchSize.Regular, on => app.Set("menuBarCombineTemperatures", on))));
            }
        }

        valuesCell.Cell.MouseLeftButtonUp += (_, _) =>
        {
            app.Set("menuBarMetricAppearance", "values");
            Style();
        };
        barsCell.Cell.MouseLeftButtonUp += (_, _) =>
        {
            app.Set("menuBarMetricAppearance", "bars");
            Style();
        };
        style.Add(cells);
        style.AddCaption(Strings.MonitorPage.BarsCaption);
        style.Add(detail);
        Style();
        style.AddDivider();
        var spacing = new SegmentedControl([Strings.MonitorPage.Standard, Strings.MonitorPage.Compact], app.Get<string>("menuBarMetricSpacing") == "standard" ? 0 : 1, false, 12);
        spacing.SelectionChanged += i => app.Set("menuBarMetricSpacing", i == 0 ? "standard" : "compact");
        style.Add(new SettingsRow(Gl.Handle, Strings.MonitorPage.Spacing, null, spacing));
        style.Add(new SettingsRow(Gl.EyeOff, Strings.MonitorPage.HideIcon, Strings.MonitorPage.HideIconCaption, new MacSwitch(app.Get<bool>("menuBarHideIconWithMetrics"), SwitchSize.Regular, on => app.Set("menuBarHideIconWithMetrics", on))));
        var separateCaption = app.Get<string>("menuBarMetricAppearance") == "bars" ? null : Strings.MonitorPage.SeparateCaption;
        style.Add(new SettingsRow(Gl.Equalizer, Strings.MonitorPage.Separate, separateCaption, new MacSwitch(app.Get<bool>("menuBarSeparateMetrics"), SwitchSize.Regular, on => app.Set("menuBarSeparateMetrics", on))));
        style.Margin = new Thickness(0, 20, 0, 0);
        root.Children.Add(style);
        return root;
    }

    private static (Border Cell, Action<bool> Select) StyleCell(string title, bool bars)
    {
        var mock = new Border { Height = 36, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromArgb(0xD1, 0, 0, 0)) };
        var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (label, value, fraction) in new[] { (Loc.T("CPU"), "42%", 0.42), ("RAM", "61%", 0.61) })
        {
            if (bars)
            {
                var g = new Grid { Margin = new Thickness(6, 0, 6, 0) };
                var outline = new Border { Width = 9, Height = 18, CornerRadius = new CornerRadius(2.2), BorderBrush = Brushes.White, BorderThickness = new Thickness(1.15), HorizontalAlignment = HorizontalAlignment.Right };
                var fill = new Border { Width = 4.8, Height = 13.8 * fraction, Background = new SolidColorBrush(Color.FromRgb(0x64, 0xD2, 0xFF)), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Right, CornerRadius = new CornerRadius(1), Margin = new Thickness(0, 0, 2.1, 2.1) };
                var letters = new TextBlock { Text = string.Join("\n", label.Select(ch => ch.ToString())), FontSize = 6.1, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, LineHeight = 6.2, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Margin = new Thickness(0, 0, 22, 0), VerticalAlignment = VerticalAlignment.Center };
                g.Children.Add(letters);
                g.Children.Add(outline);
                g.Children.Add(fill);
                g.Width = 30;
                content.Children.Add(g);
            }
            else
            {
                var col = new StackPanel { Margin = new Thickness(7, 0, 7, 0) };
                col.Children.Add(new TextBlock { Text = label, FontSize = 6.6, FontWeight = FontWeights.Medium, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center });
                col.Children.Add(new TextBlock { Text = value, FontSize = 12, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Cascadia Mono, Consolas"), Foreground = Brushes.White });
                content.Children.Add(col);
            }
        }

        mock.Child = content;
        var label2 = UiKit.Text(title, 11, FontWeights.Medium, ThemeKeys.TextPrimary);
        label2.HorizontalAlignment = HorizontalAlignment.Center;
        label2.Margin = new Thickness(0, 8, 0, 0);
        var stack = new StackPanel();
        stack.Children.Add(mock);
        stack.Children.Add(label2);
        var cell = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(8), Child = stack, BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
        void Select(bool on)
        {
            cell.SetResourceReference(Border.BackgroundProperty, on ? ThemeKeys.AccentFill12 : ThemeKeys.ControlFill);
            if (on)
            {
                cell.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Accent);
            }
            else
            {
                cell.BorderBrush = Brushes.Transparent;
            }

            label2.SetResourceReference(TextBlock.ForegroundProperty, on ? ThemeKeys.Accent : ThemeKeys.TextPrimary);
        }

        return (cell, Select);
    }

    private static FrameworkElement BarSettings(IAppSettings app)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        stack.Children.Add(UiKit.Text(Strings.MonitorPage.BarColors, 12, FontWeights.SemiBold, ThemeKeys.TextPrimary));
        foreach (var (label, key) in new[] { (Strings.MonitorPage.NormalColor, "menuBarUsageBarNormalColor"), (Strings.MonitorPage.MediumColor, "menuBarUsageBarElevatedColor"), (Strings.MonitorPage.HighColor, "menuBarUsageBarCriticalColor") })
        {
            stack.AddSpaced(ColorRow(app, label, key), 8);
        }

        stack.AddSpaced(UiKit.Divider(), 8);
        stack.AddSpaced(new Stepper(Strings.MonitorPage.MediumFrom, app.Get<int>("menuBarUsageBarMediumThreshold"), 1, 99, 1, v => v + "%", v =>
        {
            var high = app.Get<int>("menuBarUsageBarHighThreshold");
            var medium = Math.Min(Math.Max(1, v), Math.Max(1, high - 1));
            app.Set("menuBarUsageBarMediumThreshold", medium);
        }, 260), 8);
        stack.AddSpaced(new Stepper(Strings.MonitorPage.HighFrom, app.Get<int>("menuBarUsageBarHighThreshold"), 2, 100, 1, v => v + "%", v =>
        {
            var medium = app.Get<int>("menuBarUsageBarMediumThreshold");
            app.Set("menuBarUsageBarHighThreshold", Math.Min(100, Math.Max(medium + 1, v)));
        }, 260), 8);
        return stack;
    }

    private static FrameworkElement ColorRow(IAppSettings app, string label, string key)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(UiKit.Text(label, 12, FontWeights.Normal, ThemeKeys.TextPrimary));
        var swatch = new Border { Width = 26, Height = 18, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        swatch.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Divider);
        var box = new TextBox { Width = 84, Text = app.Get<string>(key), FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 11.5 };
        box.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.TextBoxStyle);

        void Paint()
        {
            try
            {
                swatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(app.Get<string>(key)));
            }
            catch (Exception)
            {
                swatch.Background = Brushes.Transparent;
            }
        }

        void Commit()
        {
            var text = box.Text.Trim();
            if (!text.StartsWith('#'))
            {
                text = "#" + text;
            }

            if (text.Length == 7 && System.Text.RegularExpressions.Regex.IsMatch(text, "^#[0-9A-Fa-f]{6}$"))
            {
                app.Set(key, text.ToUpperInvariant());
                Paint();
            }
            else
            {
                box.Text = app.Get<string>(key);
            }
        }

        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
            }
        };
        Grid.SetColumn(swatch, 1);
        Grid.SetColumn(box, 2);
        row.Children.Add(swatch);
        row.Children.Add(box);
        Paint();
        return row;
    }

    /// <summary>The draggable metric tiles (spec 02a 5.2).</summary>
    private static FrameworkElement MetricTiles(ISettingsContext c)
    {
        var app = c.App;
        var inset = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10) };
        inset.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
        var grid = new UniformGrid { Columns = 3 };
        inset.Child = grid;
        var order = MetricBlocks.Order(app);
        var tokens = new Dictionary<string, MonitorToken>();
        foreach (var id in order)
        {
            var metric = Metrics.FirstOrDefault(m => m.Id == id);
            if (metric.Id is null || !Available(c, id))
            {
                continue;
            }

            var key = SystemDefaults.MetricKey(id);
            var options = OptionsOf(app, id, out var summary);
            var token = new MonitorToken(metric.Title, metric.Glyph, () => app.Get<bool>(key), on => app.Set(key, on), options, summary) { Margin = new Thickness(4), Tag = id };
            tokens[id] = token;
            grid.Children.Add(token);
            WireDrag(token, grid, app);
        }

        return inset;
    }

    private static IReadOnlyList<TokenOption>? OptionsOf(IAppSettings app, string id, out Func<string>? summary)
    {
        summary = null;
        switch (id)
        {
            case "memory":
                summary = () => app.Get<string>("menuBarMemoryStyle") != "percent" ? Strings.MonitorPage.PressureDot : Strings.MonitorPage.MoreOptions;
                return [new TokenOption(Gl.Circle, Strings.MonitorPage.PressureDot, () => app.Get<string>("menuBarMemoryStyle") != "percent", on => app.Set("menuBarMemoryStyle", on ? "both" : "percent"), ThemeKeys.Green)];
            case "network":
                summary = () => app.Get<bool>("menuBarNetworkUploadFirst") ? "↑ ↓" : "↓ ↑";
                return [new TokenOption(Gl.Down, Strings.MonitorPage.UploadFirst, () => app.Get<bool>("menuBarNetworkUploadFirst"), on => app.Set("menuBarNetworkUploadFirst", on))];
            default:
                return null;
        }
    }

    private static void WireDrag(MonitorToken token, UniformGrid grid, IAppSettings app)
    {
        Point? start = null;
        var dragging = false;
        token.PreviewMouseLeftButtonDown += (_, e) =>
        {
            start = e.GetPosition(grid);
            dragging = false;
        };
        token.PreviewMouseMove += (_, e) =>
        {
            if (start is null || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var p = e.GetPosition(grid);
            if (!dragging && (Math.Abs(p.X - start.Value.X) > 6 || Math.Abs(p.Y - start.Value.Y) > 6))
            {
                dragging = true;
                token.CaptureMouse();
                token.Opacity = 0.6;
            }

            if (!dragging)
            {
                return;
            }

            var target = -1;
            for (var i = 0; i < grid.Children.Count; i++)
            {
                var child = (FrameworkElement)grid.Children[i];
                var origin = child.TranslatePoint(new Point(0, 0), grid);
                if (p.X >= origin.X && p.X <= origin.X + child.ActualWidth && p.Y >= origin.Y && p.Y <= origin.Y + child.ActualHeight)
                {
                    target = i;
                    break;
                }
            }

            var current = grid.Children.IndexOf(token);
            if (target >= 0 && target != current)
            {
                grid.Children.RemoveAt(current);
                grid.Children.Insert(target, token);
            }
        };
        token.PreviewMouseLeftButtonUp += (_, e) =>
        {
            start = null;
            if (!dragging)
            {
                return;
            }

            dragging = false;
            token.ReleaseMouseCapture();
            token.Opacity = 1;
            e.Handled = true;
            var visible = grid.Children.OfType<FrameworkElement>().Select(t => (string)t.Tag).ToList();
            var all = MetricBlocks.Order(app);
            var merged = new List<string>(visible);
            merged.AddRange(all.Where(i => !visible.Contains(i)));
            app.Set("menuBarMetricOrder", string.Join(',', merged));
        };
    }

    // ------------------------------------------------------------------------------------------------ panel

    public static FrameworkElement Panel(ISettingsContext c)
    {
        var app = c.App;
        var root = new StackPanel();
        root.Children.Add(Heading(Strings.MonitorPage.InPanel, ""));
        var card = new SettingsCard();
        card.Margin = new Thickness(0, 10, 0, 0);
        var blocksGrid = new UniformGrid { Columns = 3 };
        var itemsHost = new StackPanel();
        string? selected = null;
        var blockDefs = new List<(string Id, string Title, string Glyph, string Key, bool Available)>
        {
            ("system", Loc.T("System"), Gl.Cpu, "monitorShowSystem", c.Runtime.AnyAvailable(["monitor-cpu", "monitor-gpu", "monitor-memory", "connected-devices"])),
            ("network", Loc.T("Network"), Gl.Network, "monitorShowNetwork", c.Runtime.IsAvailable("monitor-network")),
            ("disk", Loc.T("Disks"), Gl.Disk, "monitorShowDisk", c.Runtime.IsAvailable("monitor-disk")),
            ("power", Loc.T("Power"), Gl.Bolt, "monitorShowPower", c.Runtime.IsAvailable("monitor-power")),
            ("mixer", Loc.T("Volume mixer"), Gl.Speaker, "monitorShowMixer", c.Runtime.AnyAvailable(["mixer", "audio-priority"])),
        };
        var tokens = new List<(string Id, MonitorToken Token)>();

        void RenderItems()
        {
            itemsHost.Children.Clear();
            foreach (var (id, token) in tokens)
            {
                token.IsSelected = id == selected;
            }

            if (selected is null or "mixer")
            {
                return;
            }

            var def = blockDefs.First(b => b.Id == selected);
            var heading = UiKit.Text(def.Title, 12, FontWeights.Medium, ThemeKeys.TextPrimary);
            heading.Margin = new Thickness(0, 10, 0, 6);
            itemsHost.Children.Add(heading);
            var inset = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10), Opacity = app.Get<bool>(def.Key) ? 1 : 0.5, IsEnabled = app.Get<bool>(def.Key) };
            inset.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
            var items = new UniformGrid { Columns = 3 };
            foreach (var item in PanelItems(c, selected))
            {
                items.Children.Add(item);
            }

            inset.Child = items;
            itemsHost.Children.Add(inset);
        }

        foreach (var def in blockDefs.Where(b => b.Available))
        {
            var d = def;
            var token = new MonitorToken(d.Title, d.Glyph, () => app.Get<bool>(d.Key), on =>
            {
                app.Set(d.Key, on);
                RenderItems();
            }, large: true, select: () =>
            {
                selected = d.Id;
                RenderItems();
            }) { Margin = new Thickness(0, 0, 10, 10) };
            tokens.Add((d.Id, token));
            blocksGrid.Children.Add(token);
            selected ??= d.Id;
        }

        RenderItems();
        card.Add(blocksGrid);
        card.Add(itemsHost);
        card.AddCaption(Strings.MonitorPage.PanelCaption);
        card.Add(new SettingsRow(Gl.Up, Strings.MonitorPage.GraphScale, null, new MacSwitch(app.Get<bool>("monitorGraphScale"), SwitchSize.Regular, on => app.Set("monitorGraphScale", on))));
        root.Children.Add(card);
        return root;
    }

    private static IEnumerable<UIElement> PanelItems(ISettingsContext c, string block)
    {
        var app = c.App;
        MonitorToken Item(string title, string glyph, string key, params TokenOption[] options)
        {
            Func<string>? summary = options.Length == 0 ? null : () =>
            {
                var on = options.Where(o => o.Get()).Select(o => o.Title).ToList();
                return on.Count == 0 ? Strings.MonitorPage.MoreOptions : string.Join(" · ", on);
            };
            return new MonitorToken(title, glyph, () => app.Get<bool>(key), on => app.Set(key, on), options.Length == 0 ? null : options, summary, large: true) { Margin = new Thickness(0, 0, 10, 10) };
        }

        TokenOption Graph(string key, string title = "Graphs") => new(Gl.Equalizer, title, () => app.Get<bool>(key), on => app.Set(key, on));
        var cpuOrGpu = c.Runtime.IsAvailable("monitor-cpu") || c.Runtime.IsAvailable("monitor-gpu");
        switch (block)
        {
            case "system":
                if (cpuOrGpu)
                {
                    yield return Item(Strings.MonitorPage.Temperatures, Gl.Thermo, "monitorSysTemps");
                }

                if (c.Runtime.IsAvailable("monitor-cpu"))
                {
                    yield return Item(Loc.T("CPU"), Gl.Cpu, "monitorSysCPU", Graph("monitorGraphCPU"), new TokenOption(Gl.Equalizer, Strings.Monitor.PerCore, () => app.Get<bool>("monitorSysCPUCores"), on => app.Set("monitorSysCPUCores", on)));
                }

                if (c.Runtime.IsAvailable("monitor-gpu"))
                {
                    yield return Item(Loc.T("GPU"), Gl.Gpu, "monitorSysGPU", Graph("monitorGraphGPU"));
                }

                if (c.Runtime.IsAvailable("monitor-memory"))
                {
                    yield return Item(Loc.T("Memory"), Gl.Memory, "monitorSysMemory", Graph("monitorGraphMemory"));
                }

                yield return Item(Loc.T("Uptime"), Gl.Clock, "monitorSysUptime");
                if (c.Runtime.IsAvailable("connected-devices"))
                {
                    yield return Item(Strings.Monitor.ConnectedDevices, Gl.Usb, "monitorSysConnectedDevices");
                }

                break;
            case "network":
                yield return Item(Loc.T("Live speed"), Gl.Gauge, "monitorNetSpeed", Graph("monitorGraphNetwork"));
                yield return Item(Loc.T("Apps using network"), Gl.AllApps, "monitorNetApps");
                yield return Item(Loc.T("Session totals"), "", "monitorNetTotals");
                yield return Item(Loc.T("IP addresses"), Gl.Network, "monitorNetAddresses");
                yield return Item(Loc.T("Speed test"), Gl.Gauge, "monitorNetTest");
                break;
            case "disk":
                yield return Item(Loc.T("Disk usage"), Gl.Disk, "monitorDiskUsage");
                yield return Item(Loc.T("Live activity"), Gl.Up, "monitorDiskActivity", Graph("monitorGraphDisk"));
                yield return Item(Loc.T("SMART"), Gl.Heart, "monitorDiskSMART");
                yield return Item(Loc.T("External protection"), "", "monitorDiskProtection");
                yield return Item(Loc.T("Tools"), "", "monitorDiskTools");
                break;
            case "power":
                yield return Item(Loc.T("System"), Gl.Bolt, "monitorPwrSystem", Graph("monitorGraphPower"));
                yield return Item(Loc.T("Adapter"), Gl.Plug, "monitorPwrAdapter");
                if (PowerStatus.HasBattery)
                {
                    yield return Item(Loc.T("Charge"), Gl.Battery, "monitorSysBattery", Graph("monitorGraphBattery"));
                    yield return Item(Loc.T("Battery"), Gl.BatteryCharging, "monitorPwrBattery");
                    yield return Item(Loc.T("Battery time remaining"), Gl.Clock, "monitorPwrTimeRemaining");
                    yield return Item(Loc.T("Battery temperature"), Gl.Thermo, "monitorPwrTemperature");
                    yield return Item(Loc.T("Battery health"), Gl.Heart, "monitorPwrHealth");
                }

                yield return Item(Loc.T("Peripheral battery"), Gl.Keyboard, "monitorPwrPeripherals");
                break;
        }
    }

    // ------------------------------------------------------------------------------------------------ readings and alerts

    public static FrameworkElement Readings(ISettingsContext c)
    {
        var app = c.App;
        var root = new StackPanel();
        root.Children.Add(Heading(Strings.MonitorPage.ReadingsAndAlerts, Gl.Equalizer));
        var card = new SettingsCard();
        card.Margin = new Thickness(0, 10, 0, 0);
        var seconds = new[] { 1, 2, 5 };
        var chips = new StackPanel { Orientation = Orientation.Horizontal };
        var chipList = new List<(int S, Chip Chip)>();
        foreach (var s in seconds)
        {
            var value = s;
            var chip = new Chip(value == 1 ? Loc.T("1 second") : value + " seconds", null, app.Get<int>("monitorIntervalSeconds") == value, height: 26, radius: 8, tooltip: null) { Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 0, 10, 0) };
            chip.MouseLeftButtonUp += (_, _) =>
            {
                app.Set("monitorIntervalSeconds", value);
                foreach (var (sec, ch) in chipList)
                {
                    ch.IsSelected = sec == value;
                }
            };
            chipList.Add((s, chip));
            chips.Children.Add(chip);
        }

        card.Add(new SettingsRow(Gl.Timer, Strings.MonitorPage.UpdateEvery, null, chips));
        var unit = new SegmentedControl(["°C", "°F"], app.Get<string>("temperatureUnit") == "fahrenheit" ? 1 : 0, false, 12);
        unit.SelectionChanged += i => app.Set("temperatureUnit", i == 1 ? "fahrenheit" : "celsius");
        card.Add(new SettingsRow(Gl.Thermo, Strings.MonitorPage.Temperatures, null, unit));
        if (c.Runtime.IsAvailable("monitor-network"))
        {
            var speed = new SegmentedControl(["MB/s", "Mbps"], app.Get<string>("networkSpeedUnit") == "bits" ? 1 : 0, false, 12);
            speed.SelectionChanged += i => app.Set("networkSpeedUnit", i == 1 ? "bits" : "bytes");
            card.Add(new SettingsRow(Gl.Network, Strings.MonitorPage.NetworkUnit, null, speed));
        }

        if (c.Runtime.IsAvailable("monitor-memory"))
        {
            var memory = new SegmentedControl([Strings.MonitorPage.MemoryUsed, Strings.MonitorPage.AppMemory], app.Get<string>("monitorMemoryMetric") == "app" ? 1 : 0, false, 12);
            memory.SelectionChanged += i => app.Set("monitorMemoryMetric", i == 1 ? "app" : "used");
            card.Add(new SettingsRow(Gl.Memory, Strings.MonitorPage.MeasureMemory, null, memory));
        }

        root.Children.Add(card);
        return root;
    }

    public static FrameworkElement Alerts(ISettingsContext c)
    {
        var app = c.App;
        var card = new SettingsCard(Strings.Alerts.Section);
        var inset = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10) };
        inset.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
        var grid = new UniformGrid { Columns = 3 };
        inset.Child = grid;
        var battery = PowerStatus.HasBattery && c.Runtime.IsAvailable("monitor-power");
        var fahrenheit = app.Get<string>("temperatureUnit") == "fahrenheit";

        void Add(string title, string glyph, string key, string? limitLabel, string? limitKey, int min, int max, bool visible, bool temperature = false)
        {
            if (!visible)
            {
                return;
            }

            Func<UIElement>? custom = null;
            Func<string>? summary = null;
            IReadOnlyList<TokenOption>? options = null;
            if (limitKey is not null && limitLabel is not null)
            {
                string Show(int v) => temperature ? Fmt.Temperature(v, fahrenheit, false) : v + "%";
                summary = () => Show(app.Get<int>(limitKey));
                custom = () => new Stepper(limitLabel!, app.Get<int>(limitKey), min, max, 5, Show, v => app.Set(limitKey, v), 200);
                options = [];
            }

            var token = new MonitorToken(title, glyph, () => app.Get<bool>(key), on => app.Set(key, on), options, summary, customOptions: custom) { Margin = new Thickness(4) };
            grid.Children.Add(token);
        }

        var cpu = c.Runtime.IsAvailable("monitor-cpu");
        Add(Strings.Alerts.HighCpu, Gl.Cpu, "monitorAlertCPU", Strings.Alerts.CpuAbove, "monitorAlertCPUThreshold", 50, 100, cpu);
        Add(Strings.Alerts.HighCpuTemp, Gl.Thermo, "monitorAlertCPUTemperature", Strings.Alerts.TemperatureAbove, "monitorAlertCPUTemperatureThreshold", 70, 105, cpu && TempProbe.Cpu, true);
        Add(Strings.Alerts.CriticalMemory, Gl.Memory, "monitorAlertMemory", null, null, 0, 0, c.Runtime.IsAvailable("monitor-memory"));
        Add(Strings.Alerts.LowDisk, Gl.Disk, "monitorAlertDisk", Strings.Alerts.FreeBelow, "monitorAlertDiskFreePercent", 5, 30, c.Runtime.IsAvailable("monitor-disk"));
        Add(Strings.Alerts.HighBatteryTemp, Gl.Thermo, "monitorAlertBatteryTemperature", Strings.Alerts.TemperatureAbove, "monitorAlertBatteryTemperatureThreshold", 30, 50, battery && TempProbe.Battery, true);
        Add(Strings.Alerts.LowBattery, Gl.BatteryLow, "monitorAlertBattery", Strings.Alerts.BatteryBelow, "monitorAlertBatteryPercent", 5, 50, battery);
        card.Add(inset);

        var cooldown = new[] { 2, 5, 15, 30, 60 };
        var labels = new[] { Loc.T("2 minutes"), Loc.T("5 minutes"), Loc.T("15 minutes"), Loc.T("30 minutes"), Loc.T("1 hour") };
        var index = Math.Max(0, Array.IndexOf(cooldown, app.Get<int>("monitorAlertCooldownMinutes")));
        var combo = Bits.MiniCombo(labels, index, i => app.Set("monitorAlertCooldownMinutes", cooldown[i]), 130, 12);
        var anyOn = new[] { "monitorAlertCPU", "monitorAlertCPUTemperature", "monitorAlertMemory", "monitorAlertDisk", "monitorAlertBatteryTemperature", "monitorAlertBattery" }.Any(k => app.Get<bool>(k));
        var row = new SettingsRow(Gl.Bell, Strings.Alerts.Repeat, null, combo);
        row.IsEnabled = anyOn;
        row.Opacity = anyOn ? 1 : 0.5;
        card.Add(row);
        card.AddCaption(Strings.Alerts.Caption);
        void Repaint(string key)
        {
            if (!key.StartsWith("monitorAlert", StringComparison.Ordinal))
            {
                return;
            }

            var on = new[] { "monitorAlertCPU", "monitorAlertCPUTemperature", "monitorAlertMemory", "monitorAlertDisk", "monitorAlertBatteryTemperature", "monitorAlertBattery" }.Any(k => app.Get<bool>(k));
            card.Dispatcher.BeginInvoke(() =>
            {
                row.IsEnabled = on;
                row.Opacity = on ? 1 : 0.5;
            });
        }

        card.Loaded += (_, _) => app.Changed += Repaint;
        card.Unloaded += (_, _) => app.Changed -= Repaint;
        return card;
    }
}

/// <summary>Monitor page: the pinned tray preview.</summary>
public sealed class MonitorPreviewSection : ISettingsSection
{
    public IReadOnlyList<string> FeatureIds => ["monitor-cpu", "monitor-gpu", "monitor-memory", "monitor-network", "monitor-disk", "monitor-power", "connected-devices"];

    public string PageId => SettingsPages.Monitor;

    public string? Title => Loc.T("Tray preview");

    public int Order => 0;

    public IReadOnlyList<string> Keywords => ["menu bar preview", "preview"];

    public FrameworkElement CreateView(ISettingsContext context)
    {
        SystemDefaults.Register(context.App);
        return MonitorSettingsUi.Preview(context);
    }
}

/// <summary>Monitor page: "In the tray" (metric tiles) and the Usage display card.</summary>
public sealed class MonitorTraySection : ISettingsSection
{
    public IReadOnlyList<string> FeatureIds => ["monitor-cpu", "monitor-gpu", "monitor-memory", "monitor-network", "monitor-disk", "monitor-power", "connected-devices"];

    public string PageId => SettingsPages.Monitor;

    public string? Anchor => "tray";

    public string? Title => Loc.T("In the tray");

    public int Order => 10;

    public IReadOnlyList<string> Keywords =>
    [
        "usage display", "values", "bars", "bar colors and limits", "normal color", "medium color", "high color", "combine usage and temperature",
        "spacing", "hide the app icon", "separate metrics into their own items", "drag to reorder", "pressure dot", "upload above download", "disk display",
    ];

    public FrameworkElement CreateView(ISettingsContext context)
    {
        SystemDefaults.Register(context.App);
        return MonitorSettingsUi.MenuBar(context);
    }
}

/// <summary>Monitor page: "In the panel" blocks and items.</summary>
public sealed class MonitorPanelSettingsSection : ISettingsSection
{
    public IReadOnlyList<string> FeatureIds => ["monitor-cpu", "monitor-gpu", "monitor-memory", "monitor-network", "monitor-disk", "monitor-power", "connected-devices", "mixer"];

    public string PageId => SettingsPages.Monitor;

    public string? Anchor => "panel";

    public string? Title => Loc.T("In the panel");

    public int Order => 20;

    public IReadOnlyList<string> Keywords => ["show graph scale", "temperatures", "uptime", "live speed", "session totals", "ip addresses", "speed test", "smart", "adapter", "battery health"];

    public FrameworkElement CreateView(ISettingsContext context)
    {
        SystemDefaults.Register(context.App);
        return MonitorSettingsUi.Panel(context);
    }
}

/// <summary>Monitor page: "Readings and alerts" (update interval, units).</summary>
public sealed class MonitorReadingsSection : ISettingsSection
{
    public IReadOnlyList<string> FeatureIds => ["monitor-cpu", "monitor-gpu", "monitor-memory", "monitor-network", "monitor-disk", "monitor-power", "connected-devices"];

    public string PageId => SettingsPages.Monitor;

    public string? Anchor => "readings";

    public string? Title => Loc.T("Readings and alerts");

    public int Order => 30;

    public IReadOnlyList<string> Keywords => ["update every", "1 second", "2 seconds", "5 seconds", "temperatures", "network speed unit", "measure memory as"];

    public FrameworkElement CreateView(ISettingsContext context)
    {
        SystemDefaults.Register(context.App);
        return MonitorSettingsUi.Readings(context);
    }
}

/// <summary>Monitor page: the Alerts card.</summary>
public sealed class MonitorAlertsSection : ISettingsSection
{
    public IReadOnlyList<string> FeatureIds => ["monitor-cpu", "monitor-memory", "monitor-disk", "monitor-power"];

    public string PageId => SettingsPages.Monitor;

    public string? Anchor => "alerts";

    public string? Title => Loc.T("Alerts");

    public int Order => 40;

    public IReadOnlyList<string> Keywords => ["high cpu", "high cpu temperature", "critical memory pressure", "low disk space", "low battery", "repeat the same alert after"];

    public FrameworkElement CreateView(ISettingsContext context)
    {
        SystemDefaults.Register(context.App);
        return MonitorSettingsUi.Alerts(context);
    }
}
