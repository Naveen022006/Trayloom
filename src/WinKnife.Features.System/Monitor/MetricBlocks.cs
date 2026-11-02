// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.SystemTools;

/// <summary>How a tray metric block draws itself.</summary>
internal enum BlockStyle
{
    /// <summary>Label (small) over value.</summary>
    Text,

    /// <summary>Two stacked mono lines (network, disk activity).</summary>
    TwoLines,

    /// <summary>Battery symbol + percentage.</summary>
    Battery,

    /// <summary>Vertical gauge (Usage = Bars).</summary>
    Gauge,
}

/// <summary>A renderable block of the tray metrics (spec 01 2.5).</summary>
internal sealed record TrayBlock(
    string Id,
    string Group,
    string GroupTitle,
    MetricKind Detail,
    BlockStyle Style,
    string Label,
    string Value,
    string ReservedValue,
    string? Line1 = null,
    string? Line2 = null,
    double Fraction = 0,
    string? ValueColor = null,
    int BatteryPercent = 0,
    bool Charging = false,
    bool PressureDot = false,
    string? DotColor = null);

/// <summary>Builds the visible blocks from a snapshot and the saved settings, and draws them (spec 01 2.5).</summary>
internal static class MetricBlocks
{
    public static readonly string[] All = SystemDefaults.MetricOrder;
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    /// <summary>The metric order saved by the user, sanitised (known ids, no duplicates, missing ones appended).</summary>
    public static List<string> Order(IAppSettings app)
    {
        var saved = app.Get<string>("menuBarMetricOrder").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var order = new List<string>();
        foreach (var id in saved)
        {
            if (id == "temperature")
            {
                foreach (var t in new[] { "cpuTemperature", "gpuTemperature", "batteryTemperature" })
                {
                    if (!order.Contains(t))
                    {
                        order.Add(t);
                    }
                }

                continue;
            }

            if (All.Contains(id) && !order.Contains(id))
            {
                order.Add(id);
            }
        }

        foreach (var id in All.Where(i => !order.Contains(i)))
        {
            order.Add(id);
        }

        return order;
    }

    /// <summary>The hub feature a metric needs.</summary>
    public static string FeatureOf(string id) => id switch
    {
        "cpu" or "cpuTemperature" => "monitor-cpu",
        "gpu" or "gpuTemperature" => "monitor-gpu",
        "memory" => "monitor-memory",
        "network" => "monitor-network",
        "diskUsage" or "diskActivity" => "monitor-disk",
        "battery" or "batteryTime" or "batteryTemperature" or "peripheralBattery" or "power" => "monitor-power",
        "connectedDevices" => "connected-devices",
        _ => "fan-control",
    };

    public static bool IsEnabled(IAppSettings app, string id) => app.Get<bool>(SystemDefaults.MetricKey(id));

    /// <summary>What the pinned metrics need while nothing else is visible.</summary>
    public static Needs NeedsOf(IAppSettings app, IFeatureRuntime runtime)
    {
        var needs = Needs.None;
        foreach (var id in All)
        {
            if (!IsEnabled(app, id) || !runtime.IsAvailable(FeatureOf(id)))
            {
                continue;
            }

            needs |= id switch
            {
                "cpu" => Needs.Cpu,
                "cpuTemperature" => Needs.CpuTemp,
                "gpu" => Needs.Gpu,
                "gpuTemperature" => Needs.GpuTemp,
                "memory" => Needs.Memory,
                "battery" or "batteryTime" or "batteryTemperature" => Needs.Power,
                "power" => Needs.Power | Needs.PowerDraw,
                "peripheralBattery" => Needs.Peripherals,
                "network" => Needs.Network,
                "diskUsage" => Needs.DiskSpace,
                "diskActivity" => Needs.Disk,
                "connectedDevices" => Needs.Devices,
                _ => Needs.None,
            };
        }

        return needs;
    }

    private static string Degrees(double celsius, bool fahrenheit) => ((int)Math.Round(fahrenheit ? (celsius * 9 / 5) + 32 : celsius)).ToString(CultureInfo.InvariantCulture) + "°";

    /// <summary>Builds the blocks that currently have something to show, in the saved order.</summary>
    public static List<TrayBlock> Build(IAppSettings app, IFeatureRuntime runtime, MonitorSnapshot s)
    {
        var bars = app.Get<string>("menuBarMetricAppearance") == "bars";
        var combine = app.Get<bool>("menuBarCombineTemperatures") && !bars;
        var fahrenheit = app.Get<string>("temperatureUnit") == "fahrenheit";
        var bits = app.Get<string>("networkSpeedUnit") == "bits";
        var blocks = new List<TrayBlock>();
        var consumed = new HashSet<string>();
        foreach (var id in Order(app))
        {
            if (consumed.Contains(id) || !IsEnabled(app, id) || !runtime.IsAvailable(FeatureOf(id)))
            {
                continue;
            }

            switch (id)
            {
                case "cpu":
                case "gpu":
                {
                    var name = id == "cpu" ? "CPU" : "GPU";
                    var usage = id == "cpu" ? s.Cpu : s.Gpu;
                    var tempId = id + "Temperature";
                    var temp = id == "cpu" ? s.CpuTemp : s.GpuTemp;
                    var detail = id == "cpu" ? MetricKind.Cpu : MetricKind.Gpu;
                    if (bars)
                    {
                        blocks.Add(new TrayBlock(id, id, name, detail, BlockStyle.Gauge, name, usage is { } u ? Fmt.Percent(u) : "--", "100%", Fraction: (usage ?? double.NaN) / 100));
                        break;
                    }

                    if (combine && IsEnabled(app, tempId) && runtime.IsAvailable(FeatureOf(id)))
                    {
                        consumed.Add(tempId);
                        var value = (usage is { } cu ? Fmt.Percent(cu) : "--%") + (temp is { } ct ? " " + Degrees(ct, fahrenheit) : string.Empty);
                        blocks.Add(new TrayBlock(id, id, name, detail, BlockStyle.Text, name, value, "100% 999°"));
                    }
                    else
                    {
                        blocks.Add(new TrayBlock(id, id, name, detail, BlockStyle.Text, name, usage is { } u2 ? Fmt.Percent(u2) : "--%", "100%"));
                    }

                    break;
                }

                case "cpuTemperature":
                case "gpuTemperature":
                {
                    var cpu = id == "cpuTemperature";
                    var temp = cpu ? s.CpuTemp : s.GpuTemp;
                    if (temp is null)
                    {
                        break;
                    }

                    blocks.Add(new TrayBlock(id, cpu ? "cpu" : "gpu", cpu ? "CPU" : "GPU", cpu ? MetricKind.Cpu : MetricKind.Gpu, BlockStyle.Text, (cpu ? "CPU" : "GPU") + (fahrenheit ? "°F" : "°C"), Degrees(temp.Value, fahrenheit), "999°"));
                    break;
                }

                case "memory":
                {
                    if (s.Memory is not { } m)
                    {
                        break;
                    }

                    var style = app.Get<string>("menuBarMemoryStyle");
                    var shown = app.Get<string>("monitorMemoryMetric") == "app" ? m.App : m.Used;
                    var pct = m.Total == 0 ? 0 : shown * 100.0 / m.Total;
                    var dotColor = m.Pressure switch { Pressure.Critical => ThemeKeys.Red, Pressure.Caution => ThemeKeys.Yellow, _ => ThemeKeys.Green };
                    if (bars)
                    {
                        blocks.Add(new TrayBlock(id, id, "Memory", MetricKind.Memory, BlockStyle.Gauge, "RAM", Fmt.Percent(pct), "100%", Fraction: pct / 100, PressureDot: style != "percent", DotColor: dotColor));
                    }
                    else
                    {
                        blocks.Add(new TrayBlock(id, id, "Memory", MetricKind.Memory, BlockStyle.Text, "RAM", style == "dot" ? string.Empty : Fmt.Percent(pct), "100%", PressureDot: style != "percent", DotColor: dotColor));
                    }

                    break;
                }

                case "battery":
                case "batteryTemperature":
                {
                    if (s.Power is not { HasBattery: true } p)
                    {
                        break;
                    }

                    if (id == "battery")
                    {
                        var color = p.Percent <= 10 ? ThemeKeys.Red : p.Percent <= 20 ? ThemeKeys.Orange : null;
                        blocks.Add(new TrayBlock(id, "battery", "Battery", MetricKind.Battery, BlockStyle.Battery, "BAT", p.Percent + "%", "100%", ValueColor: color, BatteryPercent: p.Percent, Charging: p.Charging || p.OnAc));
                    }
                    else if (p.TemperatureC is { } bt)
                    {
                        blocks.Add(new TrayBlock(id, "battery", "Battery", MetricKind.Battery, BlockStyle.Text, "BAT" + (fahrenheit ? "°F" : "°C"), Degrees(bt, fahrenheit), "999°"));
                    }

                    break;
                }

                case "batteryTime":
                {
                    if (s.Power is not { HasBattery: true } p || p.OnAc || p.Charging)
                    {
                        break;
                    }

                    blocks.Add(new TrayBlock(id, id, "Battery time remaining", MetricKind.Power, BlockStyle.Text, "BAT", p.SecondsRemaining > 0 ? Fmt.HoursMinutes(p.SecondsRemaining) : "...", "99h 59m"));
                    break;
                }

                case "peripheralBattery":
                {
                    if (s.Peripherals.Count == 0)
                    {
                        break;
                    }

                    var low = s.Peripherals.OrderBy(x => x.Percent).First();
                    var label = low.Kind switch { "keyboard" => "KBD", "mouse" => "MOU", "trackpad" => "TRK", "audio" => "AUD", _ => "PER" };
                    var extra = s.Peripherals.Count - 1;
                    blocks.Add(new TrayBlock(id, id, "Peripheral battery", MetricKind.Battery, BlockStyle.Text, label, low.Percent + "%" + (extra > 0 ? "+" + Math.Min(9, extra) : string.Empty), "100%+9"));
                    break;
                }

                case "network":
                {
                    if (!s.NetKnown)
                    {
                        break;
                    }

                    var down = "↓" + Fmt.RateCompact(s.NetDown, bits);
                    var up = "↑" + Fmt.RateCompact(s.NetUp, bits);
                    var first = app.Get<bool>("menuBarNetworkUploadFirst");
                    blocks.Add(new TrayBlock(id, id, "Network", MetricKind.Network, BlockStyle.TwoLines, string.Empty, string.Empty, "↓999Kb", first ? up : down, first ? down : up));
                    break;
                }

                case "diskUsage":
                {
                    var v = s.Volumes.FirstOrDefault(x => x.Letter.StartsWith(Environment.GetEnvironmentVariable("SystemDrive") ?? "C:", StringComparison.OrdinalIgnoreCase)) ?? s.Volumes.FirstOrDefault();
                    if (v is null)
                    {
                        break;
                    }

                    var style = app.Get<string>("menuBarDiskStyle");
                    var pct = v.UsedFraction * 100;
                    if (bars && style == "percent")
                    {
                        blocks.Add(new TrayBlock(id, id, "Disks", MetricKind.Disk, BlockStyle.Gauge, "DSK", Fmt.Percent(pct), "100%", Fraction: pct / 100));
                    }
                    else
                    {
                        var value = style switch { "free" => Fmt.BytesDecimal(v.Free), "used" => Fmt.BytesDecimal(v.Total - v.Free), _ => Fmt.Percent(pct) };
                        blocks.Add(new TrayBlock(id, id, "Disks", MetricKind.Disk, BlockStyle.Text, "DSK", value, style == "percent" ? "100%" : "1000 GB"));
                    }

                    break;
                }

                case "diskActivity":
                {
                    blocks.Add(new TrayBlock(id, id, "Disks", MetricKind.Disk, BlockStyle.TwoLines, string.Empty, string.Empty, "R8888M", "R" + Fmt.RateCompact(s.DiskRead, false), "W" + Fmt.RateCompact(s.DiskWrite, false)));
                    break;
                }

                case "connectedDevices":
                    blocks.Add(new TrayBlock(id, id, Strings.Monitor.ConnectedDevices, MetricKind.Devices, BlockStyle.Text, "USB", s.Devices.Count.ToString(), "99"));
                    break;

                case "power":
                {
                    var w = s.Power?.DischargeWatts ?? s.Power?.ChargeWatts;
                    if (w is null)
                    {
                        break;
                    }

                    blocks.Add(new TrayBlock(id, id, "Power", MetricKind.Power, BlockStyle.Text, "PWR", Math.Round(w.Value) + "W", "99W"));
                    break;
                }
            }
        }

        return blocks;
    }

    // ------------------------------------------------------------------------------------------------ drawing

    /// <summary>Colours used by the renderer.</summary>
    public sealed record Palette(Color Text, Color Normal, Color Elevated, Color Critical, int Medium, int High);

    public static Palette PaletteFor(IAppSettings app, Color text)
    {
        static Color Hex(string s, Color fallback)
        {
            try
            {
                return (Color)ColorConverter.ConvertFromString(s);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        var medium = Math.Clamp(app.Get<int>("menuBarUsageBarMediumThreshold"), 1, 99);
        var high = Math.Clamp(app.Get<int>("menuBarUsageBarHighThreshold"), medium + 1, 100);
        return new Palette(
            text,
            Hex(app.Get<string>("menuBarUsageBarNormalColor"), Color.FromRgb(0x64, 0xD2, 0xFF)),
            Hex(app.Get<string>("menuBarUsageBarElevatedColor"), Color.FromRgb(0xFF, 0xD6, 0x0A)),
            Hex(app.Get<string>("menuBarUsageBarCriticalColor"), Color.FromRgb(0xFF, 0x45, 0x3A)),
            medium,
            high);
    }

    private static FormattedText Format(string text, double size, FontWeight weight, Brush brush, double dpi, FontFamily? family = null) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(family ?? Mono, FontStyles.Normal, weight, FontStretches.Normal), size, brush, dpi);

    /// <summary>The width of a block at 1x (label / reserved value / gauge).</summary>
    public static double Width(TrayBlock b, bool compact, double dpi)
    {
        var brush = Brushes.Black;
        switch (b.Style)
        {
            case BlockStyle.Gauge:
                return 19 + (b.PressureDot ? 7 : 0);
            case BlockStyle.TwoLines:
            {
                var t = Format(b.ReservedValue, 9.2, FontWeights.SemiBold, brush, dpi);
                return Math.Ceiling(t.WidthIncludingTrailingWhitespace) + 1;
            }

            case BlockStyle.Battery:
            {
                var t = Format(b.Value, 12, FontWeights.SemiBold, brush, dpi);
                return 18 + 4 + Math.Ceiling(compact ? t.WidthIncludingTrailingWhitespace : Format(b.ReservedValue, 12, FontWeights.SemiBold, brush, dpi).WidthIncludingTrailingWhitespace) + 0.5;
            }

            default:
            {
                var label = Format(b.Label, 6.6, FontWeights.Medium, brush, dpi).WidthIncludingTrailingWhitespace;
                var valueText = compact ? b.Value : b.ReservedValue;
                if (compact)
                {
                    valueText = b.Value.Length < 2 ? new string('8', 2) : b.Value;
                }

                var value = Format(valueText.Length == 0 ? "00" : valueText, 12, FontWeights.SemiBold, brush, dpi).WidthIncludingTrailingWhitespace + (b.PressureDot ? 9 : 0);
                return Math.Ceiling(Math.Max(label, value)) + 0.5;
            }
        }
    }

    /// <summary>Draws all blocks side by side (21 DIP high) into a drawing; returns its width and the x ranges of each block.</summary>
    public static (DrawingVisual Visual, double Width, List<(double From, double To, TrayBlock Block)> Hits) Draw(IReadOnlyList<TrayBlock> blocks, Palette palette, bool compact, double dpi, double gap = 8)
    {
        var visual = new DrawingVisual();
        var hits = new List<(double, double, TrayBlock)>();
        var x = 0.0;
        using (var dc = visual.RenderOpen())
        {
            var text = new SolidColorBrush(palette.Text);
            foreach (var block in blocks)
            {
                var w = Width(block, compact, dpi);
                DrawBlock(dc, block, palette, text, x, w, dpi);
                hits.Add((x, x + w, block));
                x += w + gap;
            }
        }

        return (visual, Math.Max(0, x - gap), hits);
    }

    private static Brush ColorBrush(string? key, Brush fallback)
    {
        if (key is null)
        {
            return fallback;
        }

        return WinKnife.Core.Ui.UiKit.BrushOf(key);
    }

    private static void DrawBlock(DrawingContext dc, TrayBlock b, Palette p, Brush text, double x, double width, double dpi)
    {
        switch (b.Style)
        {
            case BlockStyle.Gauge:
            {
                var fraction = b.Fraction;
                var gaugeX = x;
                // Three stacked letters of the label at the left (6.1 pt semibold).
                var letters = b.Label.Length > 3 ? b.Label[..3] : b.Label;
                for (var i = 0; i < letters.Length; i++)
                {
                    var f = Format(letters[i].ToString(), 6.1, FontWeights.SemiBold, text, dpi);
                    dc.DrawText(f, new Point(x + 1, 1.2 + (i * 6.3)));
                }

                gaugeX = x + 7 + (b.PressureDot ? 7 : 0);
                if (b.PressureDot)
                {
                    var color = (ColorBrush(b.DotColor, text) as SolidColorBrush)?.Color ?? p.Text;
                    dc.DrawEllipse(new SolidColorBrush(color), null, new Point(x + 9.4, 10.5), 2.4, 2.4);
                }

                var rect = new Rect(gaugeX + 0.6, 1.5, 9, 18);
                dc.DrawRoundedRectangle(null, new Pen(text, 1.15), rect, 2.2, 2.2);
                if (!double.IsNaN(fraction))
                {
                    var pct = fraction * 100;
                    var color = pct >= p.High ? p.Critical : pct >= p.Medium ? p.Elevated : p.Normal;
                    var h = Math.Clamp(fraction, 0, 1) * 13.8;
                    if (h > 0.2)
                    {
                        dc.DrawRoundedRectangle(new SolidColorBrush(color), null, new Rect(gaugeX + 3, 2.1 + 15.3 - h, 4.8, h), 1.2, 1.2);
                    }
                }
                else
                {
                    dc.DrawLine(new Pen(text, 1) { DashStyle = DashStyles.Dash }, new Point(gaugeX + 3, 10.5), new Point(gaugeX + 7.8, 10.5));
                }

                break;
            }

            case BlockStyle.TwoLines:
            {
                var t1 = Format(b.Line1 ?? string.Empty, 9.2, FontWeights.SemiBold, text, dpi);
                var t2 = Format(b.Line2 ?? string.Empty, 9.2, FontWeights.SemiBold, text, dpi);
                dc.DrawText(t1, new Point(x + width - t1.WidthIncludingTrailingWhitespace, 0.5));
                dc.DrawText(t2, new Point(x + width - t2.WidthIncludingTrailingWhitespace, 10.5));
                break;
            }

            case BlockStyle.Battery:
            {
                var level = Math.Clamp(b.BatteryPercent, 0, 100) / 100.0;
                var body = new Rect(x + 0.6, 5.5, 14.5, 9);
                dc.DrawRoundedRectangle(null, new Pen(text, 1), body, 2, 2);
                dc.DrawRectangle(text, null, new Rect(x + 15.6, 8, 1.6, 4));
                if (level > 0)
                {
                    dc.DrawRoundedRectangle(ColorBrush(b.ValueColor, text), null, new Rect(x + 1.8, 6.7, 12.1 * level, 6.6), 1, 1);
                }

                var value = Format(b.Value, 12, FontWeights.SemiBold, ColorBrush(b.ValueColor, text), dpi);
                dc.DrawText(value, new Point(x + 22, (21 - value.Height) / 2));
                break;
            }

            default:
            {
                var label = Format(b.Label, 6.6, FontWeights.Medium, text, dpi);
                var value = Format(b.Value, 12, FontWeights.SemiBold, text, dpi);
                dc.DrawText(label, new Point(x + ((width - label.WidthIncludingTrailingWhitespace) / 2), -0.5));
                var valueWidth = value.WidthIncludingTrailingWhitespace + (b.PressureDot ? 9 : 0);
                var vx = x + ((width - valueWidth) / 2);
                if (b.PressureDot)
                {
                    var color = (ColorBrush(b.DotColor, text) as SolidColorBrush)?.Color ?? p.Text;
                    dc.DrawEllipse(new SolidColorBrush(color), null, new Point(vx + 2.4, 14.6), 2.4, 2.4);
                    vx += 9;
                }

                dc.DrawText(value, new Point(vx, 6.5));
                break;
            }
        }
    }

    /// <summary>Renders a visual to a frozen bitmap (physical pixels at the given scale).</summary>
    public static BitmapSource Render(DrawingVisual visual, double width, double height, double scale)
    {
        var pw = Math.Max(1, (int)Math.Ceiling(width * scale));
        var ph = Math.Max(1, (int)Math.Ceiling(height * scale));
        var bitmap = new RenderTargetBitmap(pw, ph, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
