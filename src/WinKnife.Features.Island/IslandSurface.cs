// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Media;

namespace WinKnife.Features.Island;

/// <summary>
/// Draws the black island silhouette (capsule or hanging notch) at an animated rectangle inside the transparent window,
/// plus the optional outline. The GPU animates only this shape; content is laid out once at its final size.
/// </summary>
internal sealed class IslandSurface : FrameworkElement
{
    public const double Shoulder = 14;

    public double X { get; private set; }

    public double Y { get; private set; }

    public double W { get; private set; }

    public double H { get; private set; }

    public bool Capsule { get; set; } = true;

    public bool Outline { get; set; }

    public bool Translucent { get; set; }

    public double Alpha { get; set; } = 1;

    public bool Open { get; set; }

    public Geometry? CurrentGeometry { get; private set; }

    public event Action? GeometryChanged;

    public void Set(double x, double y, double w, double h)
    {
        X = x;
        Y = y;
        W = Math.Max(0, w);
        H = Math.Max(0, h);
        CurrentGeometry = Build(X, Y, W, H, Capsule, Open);
        GeometryChanged?.Invoke();
        InvalidateVisual();
    }

    /// <summary>Corner radius used for the body at this size (spec 4.1/4.2).</summary>
    public static double BottomRadius(double h, bool open) => Math.Min(open ? 28 : h / 2, Math.Min(28, Math.Max(2, h * 0.34 * (open ? 1 : 1.5))));

    public static Geometry Build(double x, double y, double w, double h, bool capsule, bool open)
    {
        if (w <= 0 || h <= 0)
        {
            return Geometry.Empty;
        }

        var radius = open ? Math.Min(28, h * 0.34) : Math.Min(h / 2, 28);
        radius = Math.Min(radius, Math.Min(w / 2, h / 2));
        if (capsule)
        {
            var g = new RectangleGeometry(new Rect(x, y, w, h), radius, radius);
            g.Freeze();
            return g;
        }

        // Hanging notch: flat top, shoulders flare outward, straight sides, rounded bottom corners.
        var sh = Math.Min(Shoulder, h * 0.19);
        var left = x;
        var right = x + w;
        var top = y;
        var bottom = y + h;
        var r = Math.Min(radius, Math.Min(h / 2, (w - (2 * sh)) / 2));
        const double k = 0.55228475;
        var fig = new PathFigure { StartPoint = new Point(left - sh, top), IsClosed = true };
        fig.Segments.Add(new LineSegment(new Point(right + sh, top), true));
        fig.Segments.Add(new BezierSegment(new Point(right + sh - (sh * k), top), new Point(right, top + sh - (sh * k)), new Point(right, top + sh), true));
        fig.Segments.Add(new LineSegment(new Point(right, bottom - r), true));
        fig.Segments.Add(new BezierSegment(new Point(right, bottom - r + (r * (1 - k))), new Point(right - r + (r * (1 - k)), bottom), new Point(right - r, bottom), true));
        fig.Segments.Add(new LineSegment(new Point(left + r, bottom), true));
        fig.Segments.Add(new BezierSegment(new Point(left + r - (r * (1 - k)), bottom), new Point(left, bottom - r + (r * (1 - k))), new Point(left, bottom - r), true));
        fig.Segments.Add(new LineSegment(new Point(left, top + sh), true));
        fig.Segments.Add(new BezierSegment(new Point(left, top + sh - (sh * k)), new Point(left - sh + (sh * k), top), new Point(left - sh, top), true));
        var geo = new PathGeometry([fig]);
        geo.Freeze();
        return geo;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (CurrentGeometry is null || W <= 0)
        {
            return;
        }

        Brush fill = Translucent && Open ? new SolidColorBrush(Color.FromArgb(0xE6, 0x0A, 0x0A, 0x0C)) : Brushes.Black;
        Pen? pen = Outline ? new Pen(new SolidColorBrush(Color.FromArgb(166, 255, 255, 255)), 2) : null;
        if (!Outline && Open)
        {
            pen = new Pen(new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)), 0.5);
        }

        dc.PushOpacity(Alpha);
        dc.DrawGeometry(fill, pen, CurrentGeometry);
        dc.Pop();
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
    {
        return CurrentGeometry is not null && CurrentGeometry.FillContains(hitTestParameters.HitPoint) ? new PointHitTestResult(this, hitTestParameters.HitPoint) : null;
    }
}
