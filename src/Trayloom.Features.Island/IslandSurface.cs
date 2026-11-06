// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Media;

namespace Trayloom.Features.Island;

/// <summary>
/// Draws the black island silhouette (capsule or hanging notch) at an animated rectangle inside the transparent window,
/// plus the optional outline. The GPU animates only this shape; content is laid out once at its final size.
/// </summary>
internal sealed class IslandSurface : FrameworkElement
{
    public const double Shoulder = 14;

    private static readonly Brush Solid = Brushes.Black;
    private static readonly Brush GlassFill = Freeze(new SolidColorBrush(Color.FromArgb(0xE6, 0x0A, 0x0A, 0x0C)));
    private static readonly Pen OutlinePen = FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(166, 255, 255, 255)), 2));
    private static readonly Pen HairlinePen = FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)), 0.5));

    // One live geometry per silhouette: the animation moves its numbers (no allocation, no re-record of the visual, no layout).
    private readonly RectangleGeometry _rect = new();
    private readonly PathGeometry _path;
    private readonly LineSegment _top;
    private readonly BezierSegment _tr;
    private readonly LineSegment _right;
    private readonly BezierSegment _br;
    private readonly LineSegment _bottom;
    private readonly BezierSegment _bl;
    private readonly LineSegment _left;
    private readonly BezierSegment _tl;
    private readonly PathFigure _figure;

    // The notch's flat edge lies on the screen edge, so the outline must not be stroked along it: the pen is clipped to everything but that edge.
    private readonly RectangleGeometry _edgeClip = new();
    private bool _capsule = true;
    private bool _open;
    private bool _outline;
    private bool _translucent;
    private bool _flip;
    private double _alpha = 1;

    public IslandSurface()
    {
        _top = new LineSegment();
        _tr = new BezierSegment();
        _right = new LineSegment();
        _br = new BezierSegment();
        _bottom = new LineSegment();
        _bl = new BezierSegment();
        _left = new LineSegment();
        _tl = new BezierSegment();
        _figure = new PathFigure { IsClosed = true };
        foreach (PathSegment seg in new PathSegment[] { _top, _tr, _right, _br, _bottom, _bl, _left, _tl })
        {
            _figure.Segments.Add(seg);
        }

        _path = new PathGeometry([_figure]);
    }

    private static SolidColorBrush Freeze(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }

    private static Pen FreezePen(Pen p)
    {
        p.Freeze();
        return p;
    }

    public double X { get; private set; }

    public double Y { get; private set; }

    public double W { get; private set; }

    public double H { get; private set; }

    public bool Capsule
    {
        get => _capsule;
        set
        {
            if (_capsule != value)
            {
                _capsule = value;
                InvalidateVisual();
            }
        }
    }

    public bool Outline
    {
        get => _outline;
        set
        {
            if (_outline != value)
            {
                _outline = value;
                InvalidateVisual();
            }
        }
    }

    public bool Translucent
    {
        get => _translucent;
        set
        {
            if (_translucent != value)
            {
                _translucent = value;
                InvalidateVisual();
            }
        }
    }

    public double Alpha
    {
        get => _alpha;
        set
        {
            if (Math.Abs(_alpha - value) > 0.001)
            {
                _alpha = value;
                InvalidateVisual();
            }
        }
    }

    /// <summary>The island rests on the bottom edge: the notch silhouette is drawn mirrored (flat edge on the screen edge, rounded corners away from it).</summary>
    public bool FlipVertical
    {
        get => _flip;
        set
        {
            if (_flip != value)
            {
                _flip = value;
                InvalidateVisual();
            }
        }
    }

    public bool Open
    {
        get => _open;
        set
        {
            if (_open != value)
            {
                _open = value;
                InvalidateVisual();
            }
        }
    }

    /// <summary>The live silhouette (shared with the content clip: it moves when <see cref="Set"/> is called).</summary>
    public Geometry CurrentGeometry => _capsule ? _rect : _path;

    public event Action? GeometryChanged;

    /// <summary>Moves the silhouette (cheap: only numbers of the live geometry change).</summary>
    public void Set(double x, double y, double w, double h)
    {
        X = x;
        Y = y;
        W = Math.Max(0, w);
        H = Math.Max(0, h);
        if (_capsule)
        {
            var radius = Math.Min(_open ? Math.Min(28, H * 0.34) : Math.Min(H / 2, 28), Math.Min(W / 2, H / 2));
            _rect.Rect = new Rect(X, Y, W, H);
            _rect.RadiusX = radius;
            _rect.RadiusY = radius;
        }
        else
        {
            UpdateNotch();
            var sh = Math.Min(Shoulder, H * 0.19);
            var clipW = Math.Max(0, W + (2 * sh) + 8);
            var clipH = Math.Max(0, H + 3);
            _edgeClip.Rect = _flip
                ? new Rect(X - sh - 4, Y - 4, clipW, clipH)
                : new Rect(X - sh - 4, Y + 1, clipW, clipH);
        }

        GeometryChanged?.Invoke();
    }

    private void UpdateNotch()
    {
        var radius = Math.Min(_open ? Math.Min(28, H * 0.34) : Math.Min(H / 2, 28), Math.Min(W / 2, H / 2));
        var sh = Math.Min(Shoulder, H * 0.19);
        var left = X;
        var right = X + W;
        var top = Y;
        var bottom = Y + H;
        var r = Math.Max(0, Math.Min(radius, Math.Min(H / 2, (W - (2 * sh)) / 2)));
        const double k = 0.55228475;

        // a bottom island is the same shape mirrored around the body's horizontal axis (y' = 2Y + H - y): flat edge on the screen edge, rounded corners away from it
        var mirror = 2 * Y + H;
        double M(double y) => _flip ? mirror - y : y;
        _figure.StartPoint = new Point(left - sh, M(top));
        _top.Point = new Point(right + sh, M(top));
        _tr.Point1 = new Point(right + sh - (sh * k), M(top));
        _tr.Point2 = new Point(right, M(top + sh - (sh * k)));
        _tr.Point3 = new Point(right, M(top + sh));
        _right.Point = new Point(right, M(bottom - r));
        _br.Point1 = new Point(right, M(bottom - r + (r * (1 - k))));
        _br.Point2 = new Point(right - r + (r * (1 - k)), M(bottom));
        _br.Point3 = new Point(right - r, M(bottom));
        _bottom.Point = new Point(left + r, M(bottom));
        _bl.Point1 = new Point(left + r - (r * (1 - k)), M(bottom));
        _bl.Point2 = new Point(left, M(bottom - r + (r * (1 - k))));
        _bl.Point3 = new Point(left, M(bottom - r));
        _left.Point = new Point(left, M(top + sh));
        _tl.Point1 = new Point(left, M(top + sh - (sh * k)));
        _tl.Point2 = new Point(left - sh + (sh * k), M(top));
        _tl.Point3 = new Point(left - sh, M(top));
    }

    /// <summary>Corner radius used for the body at this size (spec 4.1/4.2).</summary>
    public static double BottomRadius(double h, bool open) => Math.Min(open ? 28 : h / 2, Math.Min(28, Math.Max(2, h * 0.34 * (open ? 1 : 1.5))));

    public static Geometry Build(double x, double y, double w, double h, bool capsule, bool open, bool flip = false)
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
        var mirror = (2 * y) + h;
        double M(double v) => flip ? mirror - v : v;
        var fig = new PathFigure { StartPoint = new Point(left - sh, M(top)), IsClosed = true };
        fig.Segments.Add(new LineSegment(new Point(right + sh, M(top)), true));
        fig.Segments.Add(new BezierSegment(new Point(right + sh - (sh * k), M(top)), new Point(right, M(top + sh - (sh * k))), new Point(right, M(top + sh)), true));
        fig.Segments.Add(new LineSegment(new Point(right, M(bottom - r)), true));
        fig.Segments.Add(new BezierSegment(new Point(right, M(bottom - r + (r * (1 - k)))), new Point(right - r + (r * (1 - k)), M(bottom)), new Point(right - r, M(bottom)), true));
        fig.Segments.Add(new LineSegment(new Point(left + r, M(bottom)), true));
        fig.Segments.Add(new BezierSegment(new Point(left + r - (r * (1 - k)), M(bottom)), new Point(left, M(bottom - r + (r * (1 - k)))), new Point(left, M(bottom - r)), true));
        fig.Segments.Add(new LineSegment(new Point(left, M(top + sh)), true));
        fig.Segments.Add(new BezierSegment(new Point(left, M(top + sh - (sh * k))), new Point(left - sh + (sh * k), M(top)), new Point(left - sh, M(top)), true));
        var geo = new PathGeometry([fig]);
        geo.Freeze();
        return geo;
    }

    protected override void OnRender(DrawingContext dc)
    {
        // recorded once per style change; the geometry itself is live
        Brush fill = _translucent && _open ? GlassFill : Solid;
        var pen = _outline ? OutlinePen : (_open ? HairlinePen : null);
        if (_alpha < 0.999)
        {
            dc.PushOpacity(_alpha);
            DrawBody(dc, fill, pen);
            dc.Pop();
            return;
        }

        DrawBody(dc, fill, pen);
    }

    /// <summary>Fills the silhouette; the notch strokes its pen through <see cref="_edgeClip"/> so no line is drawn along the screen edge.</summary>
    private void DrawBody(DrawingContext dc, Brush fill, Pen? pen)
    {
        if (_capsule || pen is null)
        {
            dc.DrawGeometry(fill, pen, CurrentGeometry);
            return;
        }

        dc.DrawGeometry(fill, null, _path);
        dc.PushClip(_edgeClip);
        dc.DrawGeometry(null, pen, _path);
        dc.Pop();
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
    {
        return W > 0 && CurrentGeometry.FillContains(hitTestParameters.HitPoint) ? new PointHitTestResult(this, hitTestParameters.HitPoint) : null;
    }
}
