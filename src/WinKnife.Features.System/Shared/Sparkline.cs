// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WinKnife.Core.Ui;

namespace WinKnife.Features.SystemTools;

/// <summary>Compact line graph made of one or two <see cref="Polyline"/>s (newest value on the right).</summary>
internal sealed class Sparkline : Canvas
{
    private readonly Polyline _primary;
    private readonly Polyline _secondary;
    private readonly Polygon _fill;
    private IReadOnlyList<double> _a = [];
    private IReadOnlyList<double>? _b;
    private double _max = 100;

    public Sparkline(double height = 34)
    {
        Height = height;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        _fill = new Polygon { Opacity = 0.16, StrokeThickness = 0 };
        _fill.SetResourceReference(Shape.FillProperty, ThemeKeys.Accent);
        _primary = new Polyline { StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round };
        _primary.SetResourceReference(Shape.StrokeProperty, ThemeKeys.Accent);
        _secondary = new Polyline { StrokeThickness = 1.4, StrokeLineJoin = PenLineJoin.Round, Opacity = 0.9 };
        _secondary.SetResourceReference(Shape.StrokeProperty, ThemeKeys.TextMuted);
        Children.Add(_fill);
        Children.Add(_secondary);
        Children.Add(_primary);
        SizeChanged += (_, _) => Redraw();
    }

    /// <summary>Number of samples that fit the full width.</summary>
    public int Capacity { get; set; } = 60;

    public void Update(IReadOnlyList<double> primary, IReadOnlyList<double>? secondary, double max)
    {
        _a = primary;
        _b = secondary;
        _max = Math.Max(max, 0.0001);
        Redraw();
    }

    private void Redraw()
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 4 || height < 4)
        {
            return;
        }

        Fill(_primary, _a, width, height);
        if (_b is null)
        {
            _secondary.Points = [];
        }
        else
        {
            Fill(_secondary, _b, width, height);
        }

        var points = new PointCollection(_primary.Points);
        if (points.Count > 1)
        {
            points.Add(new Point(points[^1].X, height));
            points.Add(new Point(points[0].X, height));
        }

        _fill.Points = points.Count > 3 ? points : [];
    }

    private void Fill(Polyline line, IReadOnlyList<double> values, double width, double height)
    {
        var points = new PointCollection();
        var count = Math.Min(values.Count, Capacity);
        var step = width / Math.Max(1, Capacity - 1);
        var start = values.Count - count;
        var left = width - ((count - 1) * step);
        for (var i = 0; i < count; i++)
        {
            var v = Math.Clamp(values[start + i] / _max, 0, 1);
            points.Add(new Point(left + (i * step), (height - 2) - (v * (height - 4)) + 1));
        }

        line.Points = points;
    }
}
