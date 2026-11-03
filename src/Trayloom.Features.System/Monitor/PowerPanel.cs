// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.SystemTools;

/// <summary>POWER tab (spec 01 5.7): charge, temperature, system power, adapter, battery, time remaining, health and peripheral batteries.</summary>
public sealed class PowerPanelSection : IPanelSection
{
    public string? FeatureId => "monitor-power";

    public string TabId => PanelTabs.Power;

    public bool SupportsEditing => true;

    public FrameworkElement CreateView(IPanelContext c)
    {
        SystemDefaults.Register(c.App);
        DetailRouter.Consume(c, PanelTabs.Power);
        var battery = PowerStatus.HasBattery;
        var onAc = PowerStatus.Read().OnAc;
        var blocks = new List<BlockCard.Block>
        {
            new("charge", "monitorSysBattery", () => PowerBlocks.Charge(c), battery),
            new("temperature", "monitorPwrTemperature", () => PowerBlocks.Temperature(c), battery),
            new("system", "monitorPwrSystem", () => PowerBlocks.SystemPower(c), battery && PowerProbe.HasDraw),
            new("adapter", "monitorPwrAdapter", () => PowerBlocks.Adapter(c), onAc),
            new("battery", "monitorPwrBattery", () => PowerBlocks.Battery(c), battery),
            new("remaining", "monitorPwrTimeRemaining", () => PowerBlocks.Remaining(c), battery),
            new("health", "monitorPwrHealth", () => PowerBlocks.Health(c), battery),
            new("peripherals", "monitorPwrPeripherals", () => PowerBlocks.Peripherals(c)),
        };
        return BlockCard.Create(c, "panelPowerOrder", "charge,temperature,system,adapter,battery,remaining,health,peripherals", blocks, Strings.Monitor.PowerUnavailable);
    }
}

/// <summary>Probes once whether the battery reports a charge / discharge rate (so the System power row is only offered when derivable).</summary>
internal static class PowerProbe
{
    private static bool? _hasDraw;

    public static bool HasDraw => _hasDraw ??= Wmi.Query(@"root\WMI", "SELECT DischargeRate FROM BatteryStatus", "DischargeRate").Count > 0;
}

internal static class PowerBlocks
{
    private static (Grid Row, TextBlock Caption, TextBlock Value, TextBlock Icon) Row(string glyph, string iconKey, string title, double valueSize = 13)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = UiKit.Icon(glyph, 11, iconKey, FontWeights.SemiBold);
        row.Children.Add(icon);
        var label = UiKit.Text(title, 11, FontWeights.Normal, ThemeKeys.TextPrimary);
        label.Margin = new Thickness(6, 0, 0, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        var caption = UiKit.Text(string.Empty, 9.5, FontWeights.Normal, ThemeKeys.TextSecondary);
        caption.Margin = new Thickness(6, 0, 6, 0);
        caption.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(caption, 2);
        row.Children.Add(caption);
        var value = UiKit.Text("-", valueSize, FontWeights.SemiBold, ThemeKeys.TextPrimary);
        value.FontFamily = UiKit.DisplayFont;
        value.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(value, 3);
        row.Children.Add(value);
        return (row, caption, value, icon);
    }

    private static string Watts(double w) => w < 10 ? w.ToString("0.0", CultureInfo.CurrentCulture) + " W" : w.ToString("0", CultureInfo.CurrentCulture) + " W";

    private static string BatteryGlyph(int percent, bool charging) => charging ? Gl.BatteryCharging : percent >= 90 ? "" : percent >= 60 ? "" : percent >= 30 ? "" : "";

    public static FrameworkElement? Charge(IPanelContext c)
    {
        var app = c.App;
        var stack = new StackPanel();
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        var icon = UiKit.Icon(Gl.Battery, 9, ThemeKeys.TextSecondary, FontWeights.SemiBold);
        row.Children.Add(icon);
        var name = UiKit.Text(Strings.Monitor.Charge, 11, FontWeights.Normal, ThemeKeys.TextSecondary);
        name.Margin = new Thickness(2, 0, 0, 0);
        Grid.SetColumn(name, 1);
        row.Children.Add(name);
        var bar = new UsageBar(0, ThemeKeys.Green, 5) { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(bar, 2);
        row.Children.Add(bar);
        var percent = UiKit.Text("-", 11, FontWeights.Medium, ThemeKeys.TextPrimary);
        percent.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        percent.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(percent, 3);
        row.Children.Add(percent);
        stack.Children.Add(row);
        var graph = new GraphView(app, ThemeKeys.Green, 30) { Margin = new Thickness(0, 6, 0, 0) };
        var showGraph = app.Get<bool>("monitorGraphBattery");
        if (showGraph)
        {
            stack.Children.Add(graph);
        }

        var energy = Bits.Disclosure(Strings.Monitor.AppsUsingEnergy, () => new ProcessList(UsageKind.Energy, Strings.Monitor.NoEnergy, 16), expanded: false, indent: 0, fontSize: 10.5, chevronSize: 8);
        energy.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(energy);
        var live = new LiveHost(stack, Needs.Power);
        live.Add(s =>
        {
            if (s.Power is not { HasBattery: true } p)
            {
                return;
            }

            percent.Text = p.Percent + "%";
            bar.Value = p.Percent / 100.0;
            bar.FillKey = p.Percent < 20 ? ThemeKeys.Red : p.Percent < 40 ? ThemeKeys.Yellow : ThemeKeys.Green;
            icon.Text = p.Charging || p.OnAc ? Gl.BatteryCharging : BatteryGlyph(p.Percent, false);
            if (showGraph)
            {
                graph.Set(MonitorService.Series("battery"), 100, "100%");
            }
        });
        return stack;
    }

    public static FrameworkElement? Temperature(IPanelContext c)
    {
        var (row, _, value, _) = Row(Gl.Thermo, ThemeKeys.TextSecondary, Strings.Monitor.BatteryTemperature, 11);
        value.FontSize = 11;
        var live = new LiveHost(row, Needs.Power);
        live.Add(s => value.Text = s.Power?.TemperatureC is { } t ? Fmt.Temperature(t, c.App.Get<string>("temperatureUnit") == "fahrenheit") : "-");
        return row;
    }

    public static FrameworkElement? SystemPower(IPanelContext c)
    {
        var app = c.App;
        var stack = new StackPanel();
        var (row, _, value, _) = Row(Gl.Bolt, ThemeKeys.Orange, Strings.Monitor.System);
        stack.Children.Add(row);
        var graph = new GraphView(app, ThemeKeys.Orange, 30) { Margin = new Thickness(0, 6, 0, 0) };
        var showGraph = app.Get<bool>("monitorGraphPower");
        if (showGraph)
        {
            stack.Children.Add(graph);
        }

        var live = new LiveHost(stack, Needs.Power | Needs.PowerDraw);
        live.Add(s =>
        {
            var w = s.Power?.DischargeWatts ?? s.Power?.ChargeWatts;
            value.Text = w is { } v ? Watts(v) : "-";
            if (showGraph)
            {
                var series = MonitorService.Series("power");
                var ceiling = Math.Max(1, Math.Ceiling(series.DefaultIfEmpty(0).Max()));
                graph.Set(series, ceiling, Watts(ceiling));
            }
        });
        return stack;
    }

    public static FrameworkElement? Adapter(IPanelContext c)
    {
        var (row, caption, value, _) = Row("", ThemeKeys.Accent, Strings.Monitor.Adapter);
        caption.Text = Strings.Monitor.PluggedIn;
        value.Text = string.Empty;
        return row;
    }

    public static FrameworkElement? Battery(IPanelContext c)
    {
        var (row, caption, value, icon) = Row(Gl.BatteryCharging, ThemeKeys.Green, Strings.Monitor.BatteryRow);
        var live = new LiveHost(row, Needs.Power | Needs.PowerDraw);
        live.Add(s =>
        {
            if (s.Power is not { HasBattery: true } p)
            {
                return;
            }

            caption.Text = p.Charging ? Strings.Monitor.Charging : p.OnAc ? Strings.Monitor.PluggedIn : Strings.Monitor.OnBattery;
            icon.Text = p.Charging ? Gl.BatteryCharging : "";
            icon.SetResourceReference(TextBlock.ForegroundProperty, p.Charging ? ThemeKeys.Green : ThemeKeys.TextSecondary);
            var w = p.Charging ? p.ChargeWatts : p.DischargeWatts;
            value.Text = w is { } v ? Watts(v) : "-";
        });
        return row;
    }

    public static FrameworkElement? Remaining(IPanelContext c)
    {
        var (row, caption, value, _) = Row(Gl.Clock, ThemeKeys.Green, Strings.Monitor.TimeRemaining);
        var live = new LiveHost(row, Needs.Power);
        live.Add(s =>
        {
            if (s.Power is not { HasBattery: true } p)
            {
                return;
            }

            var visible = !p.OnAc && !p.Charging;
            row.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (!visible)
            {
                return;
            }

            if (p.SecondsRemaining > 0)
            {
                caption.Text = Strings.Monitor.SystemEstimate;
                value.Text = Fmt.HoursMinutes(p.SecondsRemaining);
            }
            else
            {
                caption.Text = Strings.Monitor.Calculating;
                value.Text = "...";
            }
        });
        return row;
    }

    public static FrameworkElement? Health(IPanelContext c)
    {
        var (row, caption, value, _) = Row(Gl.Heart, ThemeKeys.Pink, Strings.Monitor.Health);
        var live = new LiveHost(row, Needs.Power);
        live.Add(s =>
        {
            if (s.Power is not { HasBattery: true } p)
            {
                return;
            }

            value.Text = p.HealthPercent is { } h ? h + "%" : "-";
            caption.Text = p.Cycles is { } cycles ? Strings.Monitor.Cycles(cycles) : string.Empty;
        });
        return row;
    }

    public static FrameworkElement? Peripherals(IPanelContext c)
    {
        var stack = new StackPanel();
        stack.Children.Add(UiKit.Text(Strings.Monitor.PeripheralBattery, 10.5, FontWeights.SemiBold, ThemeKeys.TextSecondary));
        var rows = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        stack.Children.Add(rows);
        var live = new LiveHost(stack, Needs.Peripherals);
        live.Add(s =>
        {
            rows.Children.Clear();
            foreach (var p in s.Peripherals.Take(5))
            {
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(UiKit.Icon(p.Kind == "keyboard" ? Gl.Keyboard : p.Kind == "audio" ? Gl.Speaker : Gl.Battery, 11, ThemeKeys.TextSecondary));
                var name = UiKit.Text(p.Name, 10.5, FontWeights.Medium, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
                Grid.SetColumn(name, 1);
                row.Children.Add(name);
                var value = UiKit.Text(p.Percent + "%", 10.5, FontWeights.SemiBold, ThemeKeys.TextSecondary);
                Grid.SetColumn(value, 2);
                row.Children.Add(value);
                rows.Children.Add(row);
            }

            if (s.Peripherals.Count > 5)
            {
                rows.Children.Add(UiKit.Text($"+{s.Peripherals.Count - 5}", 10, FontWeights.Normal, ThemeKeys.TextTertiary));
            }

            if (s.Peripherals.Count == 0)
            {
                rows.Children.Add(UiKit.Text(Strings.Monitor.NoPeripherals, 10.5, FontWeights.Normal, ThemeKeys.TextSecondary));
            }
        });
        return stack;
    }
}
