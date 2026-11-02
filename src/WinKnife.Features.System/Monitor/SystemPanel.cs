// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.SystemTools;

/// <summary>SYSTEM tab (spec 01 5.4): temperatures, hardware usage with sparklines and the core matrix, memory, uptime and connected devices.</summary>
public sealed class SystemPanelSection : IPanelSection
{
    public IReadOnlyList<string> FeatureIds => ["monitor-cpu", "monitor-gpu", "monitor-memory", "connected-devices"];

    public string TabId => PanelTabs.System;

    public bool SupportsEditing => true;

    public FrameworkElement CreateView(IPanelContext c)
    {
        SystemDefaults.Register(c.App);
        DetailRouter.Consume(c, PanelTabs.System);
        var blocks = new List<BlockCard.Block>
        {
            new("temps", "monitorSysTemps", () => SystemBlocks.Temps(c), c.Runtime.IsAvailable("monitor-cpu") || c.Runtime.IsAvailable("monitor-gpu")),
            new("usage", null, () => SystemBlocks.Usage(c), c.Runtime.IsAvailable("monitor-cpu") || c.Runtime.IsAvailable("monitor-gpu")),
            new("memory", "monitorSysMemory", () => SystemBlocks.MemoryBlock(c), c.Runtime.IsAvailable("monitor-memory")),
            new("uptime", "monitorSysUptime", () => SystemBlocks.Uptime(c)),
            new("connectedDevices", "monitorSysConnectedDevices", () => SystemBlocks.Devices(c), c.Runtime.IsAvailable("connected-devices")),
        };
        return BlockCard.Create(c, "panelSystemOrder", "temps,usage,memory,uptime,connectedDevices", blocks);
    }
}

internal static class SystemBlocks
{
    private static string BarKey(double percent) => percent < 60 ? ThemeKeys.Accent : percent < 85 ? ThemeKeys.Yellow : ThemeKeys.Red;

    private static TextBlock SubLabel(string text)
    {
        var label = UiKit.Text(text, 10.5, FontWeights.SemiBold, ThemeKeys.TextSecondary);
        return label;
    }

    // ------------------------------------------------------------------------------------------------ temperatures

    public static FrameworkElement? Temps(IPanelContext c)
    {
        var app = c.App;
        var stack = new StackPanel();
        stack.Children.Add(SubLabel(Strings.Monitor.Temperatures));
        var unavailable = UiKit.Text(Strings.Monitor.SensorsUnavailable, 10, FontWeights.Normal, ThemeKeys.TextTertiary);
        unavailable.Margin = new Thickness(0, 6, 0, 0);
        var grid = new UniformGrid { Rows = 1 };
        var values = new List<(TextBlock Value, string Kind)>();
        foreach (var (kind, label, glyph, feature) in new[] { ("cpu", Loc.T("CPU"), Gl.Cpu, "monitor-cpu"), ("gpu", Loc.T("GPU"), Gl.Gpu, "monitor-gpu") })
        {
            if (!c.Runtime.IsAvailable(feature))
            {
                continue;
            }

            var head = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            head.Children.Add(UiKit.Icon(glyph, 9, ThemeKeys.TextSecondary));
            var l = UiKit.Text(label, 10, FontWeights.Normal, ThemeKeys.TextSecondary);
            l.Margin = new Thickness(4, 0, 0, 0);
            head.Children.Add(l);
            var value = UiKit.Text("-", 15, FontWeights.SemiBold, ThemeKeys.TextPrimary);
            value.FontFamily = UiKit.DisplayFont;
            value.HorizontalAlignment = HorizontalAlignment.Center;
            var cell = new StackPanel();
            cell.Children.Add(head);
            value.Margin = new Thickness(0, 3, 0, 0);
            cell.Children.Add(value);
            var border = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(0, 6, 0, 6), Margin = new Thickness(0, 0, 8, 0), Child = cell };
            border.SetResourceReference(Border.BackgroundProperty, ThemeKeys.TileFill);
            grid.Children.Add(border);
            values.Add((value, kind));
        }

        grid.Margin = new Thickness(0, 6, 8, 0);
        stack.Children.Add(grid);
        stack.Children.Add(unavailable);
        var live = new LiveHost(stack, Needs.CpuTemp | Needs.GpuTemp);
        live.Add(s =>
        {
            var fahrenheit = app.Get<string>("temperatureUnit") == "fahrenheit";
            var any = false;
            foreach (var (value, kind) in values)
            {
                var t = kind == "cpu" ? s.CpuTemp : s.GpuTemp;
                value.Text = t is { } v ? Fmt.Temperature(v, fahrenheit) : "-";
                any |= t is not null;
            }

            grid.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            unavailable.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        });
        return stack;
    }

    // ------------------------------------------------------------------------------------------------ hardware usage

    public static FrameworkElement? Usage(IPanelContext c)
    {
        var app = c.App;
        var stack = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(SubLabel(Strings.Monitor.HardwareUsage));
        var taskManager = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = Strings.Monitor.OpenTaskManager, Child = UiKit.Icon(Gl.OpenWindow, 10, ThemeKeys.TextSecondary, FontWeights.Medium) };
        taskManager.MouseEnter += (_, _) => taskManager.SetResourceReference(Border.BackgroundProperty, ThemeKeys.RowHover);
        taskManager.MouseLeave += (_, _) => taskManager.Background = Brushes.Transparent;
        taskManager.MouseLeftButtonUp += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
            }
            catch (Exception)
            {
            }
        };
        Grid.SetColumn(taskManager, 1);
        head.Children.Add(taskManager);
        stack.Children.Add(head);

        var live = new LiveHost(stack, Needs.Cpu | Needs.CpuCores | Needs.Gpu);
        if (c.Runtime.IsAvailable("monitor-cpu") && app.Get<bool>("monitorSysCPU"))
        {
            stack.AddSpaced(UsageRow(c, live, Loc.T("CPU"), ThemeKeys.Accent, "cpu", "monitorGraphCPU", expandCores: app.Get<bool>("monitorSysCPUCores"), kind: UsageKind.Cpu), 6);
        }

        if (c.Runtime.IsAvailable("monitor-gpu") && app.Get<bool>("monitorSysGPU"))
        {
            stack.AddSpaced(UsageRow(c, live, Loc.T("GPU"), ThemeKeys.Cyan, "gpu", "monitorGraphGPU", expandCores: false, kind: UsageKind.Gpu), 6);
        }

        return stack.Children.Count > 1 ? stack : null;
    }

    private static FrameworkElement UsageRow(IPanelContext c, LiveHost live, string label, string graphKey, string series, string graphSetting, bool expandCores, UsageKind kind)
    {
        var app = c.App;
        var root = new StackPanel();
        var row = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        var chevron = UiKit.Icon(Gl.Chevron, 8, ThemeKeys.TextSecondary, FontWeights.SemiBold);
        chevron.RenderTransformOrigin = new Point(0.3, 0.5);
        chevron.RenderTransform = new RotateTransform(0);
        chevron.HorizontalAlignment = HorizontalAlignment.Left;
        row.Children.Add(chevron);
        var name = UiKit.Text(label, 11, FontWeights.Normal, ThemeKeys.TextSecondary);
        Grid.SetColumn(name, 1);
        row.Children.Add(name);
        var bar = new UsageBar(0, ThemeKeys.Accent, 5) { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(bar, 2);
        row.Children.Add(bar);
        var percent = UiKit.Text("-", 11, FontWeights.Medium, ThemeKeys.TextPrimary);
        percent.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        percent.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(percent, 3);
        row.Children.Add(percent);
        root.Children.Add(row);

        var graph = new GraphView(app, graphKey, 30) { Margin = new Thickness(0, 4, 0, 0) };
        var showGraph = app.Get<bool>(graphSetting);
        if (showGraph)
        {
            root.Children.Add(graph);
        }

        var details = new StackPanel { Visibility = Visibility.Collapsed };
        CoreMatrix? matrix = null;
        if (expandCores)
        {
            matrix = new CoreMatrix { Margin = new Thickness(0, 6, 0, 0) };
            details.Children.Add(matrix);
        }

        var appsHost = new StackPanel();
        Func<UIElement> makeApps = () => new ProcessList(kind, Strings.Monitor.NoEnergy, 38, allowForceQuit: c.Runtime.IsAvailable("kill-process"));
        if (expandCores)
        {
            appsHost.Children.Add(Bits.Disclosure(Strings.Monitor.AppsUsingCpu, () => makeApps(), expanded: false, indent: 0, fontSize: 10.5, chevronSize: 8));
            appsHost.Margin = new Thickness(0, 6, 0, 0);
        }

        details.Children.Add(appsHost);
        root.Children.Add(details);
        var expanded = false;
        row.MouseLeftButtonUp += (_, _) =>
        {
            expanded = !expanded;
            details.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            ((RotateTransform)chevron.RenderTransform).Angle = expanded ? 90 : 0;
            if (expanded && !expandCores && appsHost.Children.Count == 0)
            {
                var apps = (FrameworkElement)makeApps();
                apps.Margin = new Thickness(38, 6, 0, 0);
                appsHost.Children.Add(apps);
            }

            c.RequestRelayout();
        };

        live.Add(s =>
        {
            var value = series == "cpu" ? s.Cpu : s.Gpu;
            if (series == "gpu" && !s.GpuAvailable)
            {
                root.Visibility = Visibility.Collapsed;
                return;
            }

            root.Visibility = Visibility.Visible;
            percent.Text = value is { } v ? Fmt.Percent(v) : "-";
            bar.Value = (value ?? 0) / 100.0;
            bar.FillKey = BarKey(value ?? 0);
            if (showGraph)
            {
                graph.Set(MonitorService.Series(series), 100, "100%");
            }

            if (matrix is not null && s.Cores.Length > 0)
            {
                matrix.Set(s.Cores, s.CoreClasses);
            }
        });
        return root;
    }

    // ------------------------------------------------------------------------------------------------ memory

    public static FrameworkElement? MemoryBlock(IPanelContext c)
    {
        var app = c.App;
        var stack = new StackPanel();
        stack.Children.Add(SubLabel(Strings.Monitor.Memory));
        var row = new Grid { Margin = new Thickness(0, 6, 0, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var chevron = UiKit.Icon(Gl.Chevron, 8, ThemeKeys.TextSecondary, FontWeights.SemiBold);
        chevron.RenderTransformOrigin = new Point(0.3, 0.5);
        chevron.RenderTransform = new RotateTransform(0);
        chevron.HorizontalAlignment = HorizontalAlignment.Left;
        row.Children.Add(chevron);
        var pressureLabel = UiKit.Text(Strings.Monitor.Pressure, 11, FontWeights.Normal, ThemeKeys.TextSecondary);
        pressureLabel.Margin = new Thickness(0, 0, 8, 0);
        pressureLabel.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(pressureLabel, 1);
        row.Children.Add(pressureLabel);
        var pillHost = new ContentControl { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(pillHost, 2);
        row.Children.Add(pillHost);
        var total = UiKit.Text("-", 11, FontWeights.Medium, ThemeKeys.TextSecondary);
        total.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        total.HorizontalAlignment = HorizontalAlignment.Right;
        total.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(total, 3);
        row.Children.Add(total);
        stack.Children.Add(row);

        var graph = new GraphView(app, ThemeKeys.Mint, 30) { Margin = new Thickness(0, 6, 0, 0) };
        var showGraph = app.Get<bool>("monitorGraphMemory");
        if (showGraph)
        {
            stack.Children.Add(graph);
        }

        var details = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(16, 6, 0, 0) };
        var compressed = DetailLine(Strings.Monitor.Compressed);
        var cached = DetailLine(Strings.Monitor.CachedFiles);
        var swap = DetailLine(Strings.Monitor.SwapUsed);
        details.Children.Add(compressed.Row);
        details.Children.Add(cached.Row);
        details.Children.Add(swap.Row);
        var apps = new ProcessList(UsageKind.Memory, Strings.Monitor.NoEnergy, 0, allowForceQuit: c.Runtime.IsAvailable("kill-process")) { Margin = new Thickness(0, 6, 0, 0) };
        details.Children.Add(apps);
        stack.Children.Add(details);
        var expanded = false;
        row.MouseLeftButtonUp += (_, _) =>
        {
            expanded = !expanded;
            details.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            ((RotateTransform)chevron.RenderTransform).Angle = expanded ? 90 : 0;
            c.RequestRelayout();
        };

        var live = new LiveHost(stack, Needs.Memory);
        live.Add(s =>
        {
            if (s.Memory is not { } m)
            {
                return;
            }

            pillHost.Content = m.Pressure switch
            {
                Pressure.Normal => Bits.Pill(Strings.Monitor.Normal, ThemeKeys.Green),
                Pressure.Caution => Bits.Pill(Strings.Monitor.Caution, ThemeKeys.Yellow),
                Pressure.Critical => Bits.Pill(Strings.Monitor.Critical, ThemeKeys.Red),
                _ => Bits.Pill("-", ThemeKeys.TextSecondary),
            };
            var shown = app.Get<string>("monitorMemoryMetric") == "app" ? m.App : m.Used;
            total.Text = $"{Fmt.BytesBinary(shown)} / {Fmt.BytesBinary(m.Total)}";
            compressed.Value.Text = Fmt.BytesBinary(m.Compressed);
            cached.Value.Text = Fmt.BytesBinary(m.Cached);
            swap.Value.Text = Fmt.BytesBinary(m.SwapUsed);
            if (showGraph)
            {
                graph.Set(MonitorService.Series("mem"), 100, "100%");
            }
        });
        return stack;
    }

    private static (Grid Row, TextBlock Value) DetailLine(string label)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(UiKit.Text(label, 10.5, FontWeights.Normal, ThemeKeys.TextSecondary));
        var value = UiKit.Text("-", 11, FontWeights.Medium, ThemeKeys.TextPrimary);
        value.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        Grid.SetColumn(value, 1);
        row.Children.Add(value);
        return (row, value);
    }

    // ------------------------------------------------------------------------------------------------ uptime and devices

    public static FrameworkElement? Uptime(IPanelContext c)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(UiKit.Icon(Gl.Clock, 9, ThemeKeys.TextSecondary));
        var text = UiKit.Text(Strings.Monitor.UpFor("-"), 10.5, FontWeights.Medium, ThemeKeys.TextSecondary);
        text.Margin = new Thickness(6, 0, 0, 0);
        row.Children.Add(text);
        var initial = SystemNative.Uptime();
        if (initial is { } span)
        {
            text.Text = Strings.Monitor.UpFor(Fmt.Uptime(span));
        }

        var live = new LiveHost(row, Needs.Uptime);
        live.Add(s =>
        {
            if (s.Uptime is { } u)
            {
                text.Text = Strings.Monitor.UpFor(Fmt.Uptime(u));
            }
        });
        return row;
    }

    public static FrameworkElement? Devices(IPanelContext c)
    {
        var row = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(UiKit.Icon(Gl.Usb, 11, ThemeKeys.TextSecondary));
        var label = UiKit.Text(Strings.Monitor.ConnectedDevices, 11, FontWeights.Medium, ThemeKeys.TextPrimary);
        label.Margin = new Thickness(6, 0, 0, 0);
        title.Children.Add(label);
        row.Children.Add(title);
        var count = UiKit.Text("-", 11, FontWeights.Medium, ThemeKeys.TextSecondary);
        count.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        count.Margin = new Thickness(8, 0, 6, 0);
        Grid.SetColumn(count, 1);
        row.Children.Add(count);
        var arrow = UiKit.Icon(Gl.Chevron, 8, ThemeKeys.TextTertiary, FontWeights.SemiBold);
        Grid.SetColumn(arrow, 2);
        row.Children.Add(arrow);
        row.MouseLeftButtonUp += (_, _) => c.ShowDetail(Strings.Monitor.ConnectedDevices, Gl.Usb, MetricDetail.Create(c, MetricKind.Devices));
        var live = new LiveHost(row, Needs.Devices);
        live.Add(s => count.Text = s.Devices.Count.ToString());
        return row;
    }
}
