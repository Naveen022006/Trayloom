// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinKnife.Features.Capture;

/// <summary>Shared time axis of the recorder editor timeline (source seconds mapped to x).</summary>
internal static class TimelineMath
{
    public static double X(double t, double duration, double width) => duration <= 0 ? 0 : t / duration * width;

    public static double T(double x, double duration, double width) => width <= 0 ? 0 : Math.Clamp(x / width * duration, 0, duration);
}

/// <summary>The filmstrip row: thumbnails, trim handles, cut seams, cut selection and playhead. Click or drag scrubs; Shift+drag picks a cut range.</summary>
internal sealed class FilmstripControl : FrameworkElement
{
    private readonly List<BitmapSource?> _thumbs = new();
    private double _duration;
    private int _drag; // 0 none, 1 scrub, 2 trim start, 3 trim end, 4 cut pick
    private double _cutAnchor;

    public FilmstripControl()
    {
        Height = 54;
        Cursor = Cursors.Arrow;
        ClipToBounds = true;
        Focusable = true;
    }

    public EditDocument Document { get; set; } = new();

    public double Duration
    {
        get => _duration;
        set
        {
            _duration = value;
            InvalidateVisual();
        }
    }

    public double Playhead { get; set; }

    public (double Start, double End)? CutSelection { get; set; }

    public event Action<double>? Seek;

    public event Action<double, double>? TrimChanged;

    public event Action? TrimCommitted;

    public event Action<double>? RestoreCutAt;

    public event Action? CutSelectionChanged;

    public void SetThumbs(IEnumerable<BitmapSource?> thumbs)
    {
        _thumbs.Clear();
        _thumbs.AddRange(thumbs);
        InvalidateVisual();
    }

    private (double S, double E) Trim => RecorderSupport.SanitizedTrim(Document.TrimStart, Document.TrimEnd, _duration);

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        var clip = new RectangleGeometry(new Rect(0, 0, w, h), 8, 8);
        dc.PushClip(clip);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(46, 46, 46)), null, new Rect(0, 0, w, h));
        var n = Math.Max(1, _thumbs.Count == 0 ? 14 : _thumbs.Count);
        for (var i = 0; i < _thumbs.Count; i++)
        {
            if (_thumbs[i] is { } t)
            {
                var slot = new Rect(i * w / n, 0, w / n + 1, h);
                var ratio = (double)t.PixelWidth / t.PixelHeight;
                var drawW = h * ratio;
                dc.PushClip(new RectangleGeometry(slot));
                dc.DrawImage(t, new Rect(slot.X + (slot.Width - drawW) / 2, 0, drawW, h));
                dc.Pop();
            }
        }

        var (s, e) = Trim;
        var xs = TimelineMath.X(s, _duration, w);
        var xe = TimelineMath.X(e, _duration, w);
        var dim = new SolidColorBrush(Color.FromArgb(158, 0, 0, 0));
        dc.DrawRectangle(dim, null, new Rect(0, 0, xs, h));
        dc.DrawRectangle(dim, null, new Rect(xe, 0, Math.Max(0, w - xe), h));
        foreach (var c in Document.Cuts)
        {
            var cx1 = TimelineMath.X(Math.Max(c.Start, s), _duration, w);
            var cx2 = TimelineMath.X(Math.Min(c.End, e), _duration, w);
            if (cx2 > cx1)
            {
                dc.DrawRectangle(dim, null, new Rect(cx1, 0, cx2 - cx1, h));
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(230, 255, 159, 10)), null, new Rect(cx1 - 1.5, 0, 3, h));
            }
        }

        if (CutSelection is { } sel)
        {
            var r = new Rect(TimelineMath.X(sel.Start, _duration, w), 0, Math.Max(1, TimelineMath.X(sel.End - sel.Start, _duration, w)), h);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(71, 255, 69, 58)), new Pen(new SolidColorBrush(Color.FromArgb(230, 255, 69, 58)), 1.5), r);
        }

        var accent = new SolidColorBrush(Color.FromArgb(230, 10, 132, 255));
        dc.DrawRoundedRectangle(null, new Pen(accent, 2), new Rect(xs, 1, Math.Max(2, xe - xs), h - 2), 8, 8);
        foreach (var x in new[] { xs, xe - 12 })
        {
            dc.DrawRoundedRectangle(accent, null, new Rect(Math.Clamp(x, 0, w - 12), 0, 12, h), 4, 4);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(217, 255, 255, 255)), null, new Rect(Math.Clamp(x, 0, w - 12) + 5, h * 0.32, 2, h * 0.36), 1, 1);
        }

        var px = TimelineMath.X(Playhead, _duration, w);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), null, new Rect(px - 2, 0, 4, h));
        dc.DrawRectangle(Brushes.White, null, new Rect(px - 1, 0, 2, h));
        dc.Pop();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        var p = e.GetPosition(this);
        var w = ActualWidth;
        var (s, en) = Trim;
        var xs = TimelineMath.X(s, _duration, w);
        var xe = TimelineMath.X(en, _duration, w);
        CaptureMouse();
        // restore a cut by clicking its seam
        foreach (var c in Document.Cuts)
        {
            if (Math.Abs(p.X - TimelineMath.X(c.Start, _duration, w)) <= 4)
            {
                RestoreCutAt?.Invoke(c.Start);
                ReleaseMouseCapture();
                return;
            }
        }

        if (Math.Abs(p.X - (xs + 6)) <= 12)
        {
            _drag = 2;
        }
        else if (Math.Abs(p.X - (xe - 6)) <= 12)
        {
            _drag = 3;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            _drag = 4;
            _cutAnchor = TimelineMath.T(p.X, _duration, w);
            CutSelection = (_cutAnchor, _cutAnchor);
        }
        else
        {
            _drag = 1;
            CutSelection = null;
            CutSelectionChanged?.Invoke();
            Seek?.Invoke(TimelineMath.T(p.X, _duration, w));
        }

        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_drag == 0)
        {
            return;
        }

        var w = ActualWidth;
        var t = TimelineMath.T(e.GetPosition(this).X, _duration, w);
        var (s, en) = Trim;
        var handle = 12 / Math.Max(1, w) * _duration;
        switch (_drag)
        {
            case 1:
                Seek?.Invoke(t);
                break;
            case 2:
                TrimChanged?.Invoke(Math.Min(t, en - Math.Max(0.2, 2 * handle)), en);
                Seek?.Invoke(Math.Min(t, en - 0.2));
                break;
            case 3:
                TrimChanged?.Invoke(s, Math.Max(t, s + Math.Max(0.2, 2 * handle)));
                Seek?.Invoke(Math.Max(s, t - 0.05));
                break;
            case 4:
                CutSelection = (Math.Min(_cutAnchor, t), Math.Max(_cutAnchor, t));
                CutSelectionChanged?.Invoke();
                InvalidateVisual();
                break;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_drag is 2 or 3)
        {
            TrimCommitted?.Invoke();
        }

        if (_drag == 4 && CutSelection is { } sel && sel.End - sel.Start < 0.1)
        {
            CutSelection = null;
            CutSelectionChanged?.Invoke();
        }

        _drag = 0;
        ReleaseMouseCapture();
        InvalidateVisual();
    }
}
