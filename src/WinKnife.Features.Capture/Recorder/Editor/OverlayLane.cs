// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WinKnife.Features.Capture;

/// <summary>One block on a lane (a zoom, a text, a picture or a blur).</summary>
internal sealed record LaneItem(Guid Id, double Start, double End, string Label, string Glyph);

internal enum LaneEdit { Move, ResizeStart, ResizeEnd }

/// <summary>
/// A timeline lane of blocks (Zoom / Text / Image / Blur). One mouse-down decides the gesture: an edge zone (11 px or 40 % of the block)
/// resizes, the body selects and then moves, an empty spot deselects or adds. Snaps to 0, the end, the playhead, every click and the
/// edges of the other blocks (8 px). One gesture is one undo step (the owner snapshots on <see cref="BeginEdit"/>).
/// </summary>
internal sealed class OverlayLaneControl : FrameworkElement
{
    private readonly Color _tint;
    private readonly string _hint;
    private readonly double _ramp;
    private double _duration;
    private Guid? _hit;
    private LaneEdit _mode;
    private double _downX;
    private double _grab;
    private bool _dragging;
    private bool _began;
    private double _hoverX = -1;
    private double _origStart;
    private double _origEnd;

    public OverlayLaneControl(Color tint, string emptyHint, double ramp)
    {
        _tint = tint;
        _hint = emptyHint;
        _ramp = ramp;
        Height = 30;
        Focusable = true;
        ClipToBounds = true;
    }

    public Func<IReadOnlyList<LaneItem>> Items { get; set; } = () => Array.Empty<LaneItem>();

    /// <summary>Extra snap targets (clicks); the lane adds 0, the end, the playhead and neighbouring blocks itself.</summary>
    public Func<IEnumerable<double>> SnapTargets { get; set; } = () => Array.Empty<double>();

    public Guid? Selected { get; set; }

    public double Playhead { get; set; }

    public double Duration
    {
        get => _duration;
        set
        {
            _duration = value;
            InvalidateVisual();
        }
    }

    public event Action<double>? AddRequested;

    public event Action<Guid?>? SelectionChanged;

    public event Action? BeginEdit;

    /// <summary>(id, desired start, desired end, which edit) - the owner applies its own constraints and writes the document.</summary>
    public event Action<Guid, double, double, LaneEdit>? Edited;

    public event Action<Guid>? RemoveRequested;

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(10, 255, 255, 255)), null, new Rect(0, 0, w, h), 6, 6);
        var items = Items();
        if (items.Count == 0)
        {
            var hint = new FormattedText(_hint, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10.5, new SolidColorBrush(Color.FromArgb(102, 255, 255, 255)), 1.0);
            dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(56, 255, 255, 255)), 1) { DashStyle = new DashStyle(new[] { 5.0, 4.0 }, 0) }, new Rect(1, 1, w - 2, h - 2), 6, 6);
            dc.DrawText(hint, new Point((w - hint.Width) / 2, (h - hint.Height) / 2));
        }

        foreach (var it in items)
        {
            var r = BlockRect(it, w, h);
            var sel = it.Id == Selected;
            var fill = Color.FromArgb(56, _tint.R, _tint.G, _tint.B);
            dc.DrawRoundedRectangle(new SolidColorBrush(fill), null, r, 4, 4);
            if (_ramp > 0)
            {
                // the hold part after the ramp-in is painted a little lighter
                var rampW = TimelineMath.X(_ramp, _duration, w);
                var hold = new Rect(r.X + Math.Min(rampW, r.Width), r.Y, Math.Max(0, r.Width - rampW), r.Height);
                dc.PushClip(new RectangleGeometry(r, 4, 4));
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(41, _tint.R, _tint.G, _tint.B)), null, hold);
                dc.Pop();
            }

            dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(sel ? (byte)255 : (byte)166, _tint.R, _tint.G, _tint.B)), sel ? 2 : 1), r, 4, 4);
            if (r.Width > 34)
            {
                var grip = new SolidColorBrush(Color.FromArgb(230, _tint.R, _tint.G, _tint.B));
                dc.DrawRoundedRectangle(grip, null, new Rect(r.X + 3.5, r.Y + r.Height * 0.32, 2, r.Height * 0.36), 1, 1);
                dc.DrawRoundedRectangle(grip, null, new Rect(r.Right - 5.5, r.Y + r.Height * 0.32, 2, r.Height * 0.36), 1, 1);
            }

            var text = r.Width > 56 ? $"{it.Glyph} {it.Label}" : it.Glyph;
            var label = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 10, new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)), 1.0);
            if (label.Width + 10 < r.Width)
            {
                dc.DrawText(label, new Point(r.X + (r.Width - label.Width) / 2, r.Y + (r.Height - label.Height) / 2));
            }
        }

        if (_hoverX >= 0)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(31, 255, 255, 255)), null, new Rect(_hoverX, 0, 1, h));
        }

        var px = TimelineMath.X(Playhead, _duration, w);
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), 1), new Point(px, 0), new Point(px, h));
    }

    private Rect BlockRect(LaneItem it, double w, double h)
    {
        var x1 = TimelineMath.X(it.Start, _duration, w);
        var x2 = Math.Max(x1 + 6, TimelineMath.X(it.End, _duration, w));
        return new Rect(x1, 2, x2 - x1, h - 4);
    }

    private LaneItem? HitBlock(double x, out LaneEdit mode)
    {
        mode = LaneEdit.Move;
        var w = ActualWidth;
        foreach (var it in Items())
        {
            var r = BlockRect(it, w, ActualHeight);
            if (x < r.X || x > r.Right)
            {
                continue;
            }

            var edge = Math.Min(11, r.Width * 0.4);
            mode = x - r.X <= edge ? LaneEdit.ResizeStart : r.Right - x <= edge ? LaneEdit.ResizeEnd : LaneEdit.Move;
            return it;
        }

        return null;
    }

    private double Snap(double time, Guid? ignore)
    {
        var tol = 8.0 / Math.Max(1, ActualWidth) * _duration;
        var best = time;
        var bestDist = tol;
        void Try(double target)
        {
            var d = Math.Abs(target - time);
            if (d < bestDist)
            {
                bestDist = d;
                best = target;
            }
        }

        Try(0);
        Try(_duration);
        Try(Playhead);
        foreach (var t in SnapTargets())
        {
            Try(t);
        }

        foreach (var it in Items())
        {
            if (it.Id == ignore)
            {
                continue;
            }

            Try(it.Start);
            Try(it.End);
        }

        return best;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        var p = e.GetPosition(this);
        var hit = HitBlock(p.X, out var mode);
        if (hit is null)
        {
            if (Selected is not null)
            {
                Selected = null;
                SelectionChanged?.Invoke(null);
            }
            else
            {
                AddRequested?.Invoke(Snap(TimelineMath.T(p.X, _duration, ActualWidth), null));
            }

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        Selected = hit.Id;
        _hit = hit.Id;
        _mode = mode;
        _downX = p.X;
        _origStart = hit.Start;
        _origEnd = hit.End;
        _grab = TimelineMath.T(p.X, _duration, ActualWidth) - hit.Start;
        _dragging = false;
        _began = false;
        CaptureMouse();
        SelectionChanged?.Invoke(hit.Id);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        _hoverX = p.X;
        if (_hit is not { } id)
        {
            var over = HitBlock(p.X, out var m);
            Cursor = over is null ? Cursors.Arrow : m == LaneEdit.Move ? Cursors.Hand : Cursors.SizeWE;
            InvalidateVisual();
            return;
        }

        var travel = Math.Abs(p.X - _downX);
        if (!_dragging && travel < (_mode == LaneEdit.Move ? 4 : 3))
        {
            return;
        }

        _dragging = true;
        if (!_began)
        {
            BeginEdit?.Invoke();
            _began = true;
        }

        var t = TimelineMath.T(p.X, _duration, ActualWidth);
        switch (_mode)
        {
            case LaneEdit.Move:
            {
                var len = _origEnd - _origStart;
                var start = t - _grab;
                // snap whichever edge lands closest to a target
                var s1 = Snap(start, id);
                var s2 = Snap(start + len, id) - len;
                start = Math.Abs(s1 - start) <= Math.Abs(s2 - start) ? s1 : s2;
                Edited?.Invoke(id, start, start + len, LaneEdit.Move);
                break;
            }

            case LaneEdit.ResizeStart:
                Edited?.Invoke(id, Snap(t, id), _origEnd, LaneEdit.ResizeStart);
                break;
            default:
                Edited?.Invoke(id, _origStart, Snap(t, id), LaneEdit.ResizeEnd);
                break;
        }

        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hoverX = -1;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _hit = null;
        _dragging = false;
        ReleaseMouseCapture();
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        var hit = HitBlock(e.GetPosition(this).X, out _);
        if (hit is null)
        {
            return;
        }

        var menu = new System.Windows.Controls.ContextMenu();
        var item = new System.Windows.Controls.MenuItem { Header = RS.Remove };
        item.Click += (_, _) => RemoveRequested?.Invoke(hit.Id);
        menu.Items.Add(item);
        menu.IsOpen = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Delete or Key.Back && Selected is { } id)
        {
            RemoveRequested?.Invoke(id);
            e.Handled = true;
        }
    }
}

/// <summary>The click ruler above the Zoom lane (12 px): a scrub surface with a tick for every click-down and the playhead.</summary>
internal sealed class ClickRuler : FrameworkElement
{
    private bool _drag;

    public ClickRuler()
    {
        Height = 12;
        Cursor = Cursors.Arrow;
    }

    public double Duration { get; set; }

    public double Playhead { get; set; }

    public IReadOnlyList<double> Ticks { get; set; } = Array.Empty<double>();

    public event Action<double>? Seek;

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(13, 255, 255, 255)), null, new Rect(0, h - 1, w, 1));
        var tick = new SolidColorBrush(Color.FromArgb(89, 255, 255, 255));
        foreach (var t in Ticks)
        {
            dc.DrawRectangle(tick, null, new Rect(TimelineMath.X(t, Duration, w) - 0.75, h - 8, 1.5, 7));
        }

        dc.DrawRectangle(Brushes.White, null, new Rect(TimelineMath.X(Playhead, Duration, w) - 1, 0, 2, h));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _drag = true;
        CaptureMouse();
        Seek?.Invoke(TimelineMath.T(e.GetPosition(this).X, Duration, ActualWidth));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_drag)
        {
            Seek?.Invoke(TimelineMath.T(e.GetPosition(this).X, Duration, ActualWidth));
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _drag = false;
        ReleaseMouseCapture();
    }
}
