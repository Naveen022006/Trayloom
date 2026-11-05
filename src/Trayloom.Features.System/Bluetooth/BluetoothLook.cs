// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Media;
using Trayloom.Core;
using Trayloom.Features.SystemTools.Wifi;

namespace Trayloom.Features.SystemTools.Bluetooth;

/// <summary>A small ring filled by the battery level, drawn on the island's black (track white at 0.18).</summary>
internal sealed class BatteryRing : FrameworkElement
{
    private readonly double _value;
    private readonly Pen _track;
    private readonly Pen _fill;
    private readonly double _line;

    public BatteryRing(int percent, int threshold, double size = 11, double lineWidth = 2)
    {
        _value = Math.Clamp(percent, 0, 100) / 100.0;
        _line = lineWidth;
        Width = size;
        Height = size;
        IsHitTestVisible = false;
        _track = new Pen(WifiLook.White(0.18), lineWidth);
        _fill = new Pen(WifiLook.Of(BtLook.Tint(BtRules.RingTint(percent, threshold))), lineWidth) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = (size - _line) / 2;
        dc.DrawEllipse(null, _track, center, radius, radius);
        if (_value <= 0)
        {
            return;
        }

        if (_value >= 0.999)
        {
            dc.DrawEllipse(null, _fill, center, radius, radius);
            return;
        }

        var angle = _value * 2 * Math.PI;
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, angle > Math.PI, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        dc.DrawGeometry(null, _fill, geometry);
    }
}

/// <summary>Glyphs and colours shared by the Bluetooth views.</summary>
internal static class BtLook
{
    /// <summary>The Segoe Fluent glyph of a device kind.</summary>
    public static string KindGlyph(BtKind kind) => kind switch
    {
        BtKind.Headphones => "",
        BtKind.Speaker => "",
        BtKind.Keyboard => "",
        BtKind.Mouse => "",
        BtKind.Gamepad => "",
        BtKind.Phone => "",
        BtKind.Computer => "",
        BtKind.Watch => "",
        _ => "",
    };

    /// <summary>A <c>#RRGGBB</c> text as a colour.</summary>
    public static Color Tint(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    /// <summary>The ring and glyph colour of a level: the same rule as the ring, so the glyph matches it.</summary>
    public static Color LevelTint(int percent, int threshold) => Tint(BtRules.RingTint(percent, threshold));
}

/// <summary>Starts async work and logs a failure instead of letting an <c>async void</c> take the process down.</summary>
internal static class BtGuard
{
    internal static async void FireAndLog(Func<Task> work, ILog log, string what)
    {
        try
        {
            await work();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            try
            {
                log.Warn($"[bluetooth] {what} failed: {ex.GetType().Name} 0x{ex.HResult:X8}");
            }
            catch
            {
                // a logger that fails has nothing left to report to
            }
        }
    }
}
