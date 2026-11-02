// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Capture;

/// <summary>Loupe constants and maths shared by the screenshot selection and the colour picker.</summary>
internal static class LoupeMath
{
    public const double MinZoom = 0.5;
    public const double MaxZoom = 13.0 / 3.0;

    public static int SampleSide(double zoom)
    {
        var raw = 13.0 / zoom;
        var odd = 2 * (int)Math.Floor(raw / 2) + 1;
        return Math.Max(3, odd);
    }
}

/// <summary>One borderless topmost overlay window per monitor. Content is laid out in physical pixels of that monitor.</summary>
internal sealed class SelectionWindow : Window
{
    private readonly CaptureSession _session;
    private readonly Grid _root = new() { Background = Brushes.Transparent };
    private readonly Image _back = new() { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly SelectionView _view;
    private readonly ChromeLayer _chrome;
    private bool _closing;

    public SelectionWindow(CaptureSession session, MonitorInfo monitor)
    {
        _session = session;
        Monitor = monitor;
        // Closed by something else (Alt+F4, a task manager): end the session so the next capture can start.
        Closed += (_, _) =>
        {
            if (!_closing)
            {
                _session.WindowClosedExternally();
            }
        };
        Title = string.Empty;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        AllowsTransparency = !session.Frozen;
        Background = session.Frozen ? Brushes.Black : new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        WindowStartupLocation = WindowStartupLocation.Manual;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Left = monitor.Bounds.X;
        Top = monitor.Bounds.Y;
        Width = 100;
        Height = 100;

        _root.Width = monitor.Bounds.Width;
        _root.Height = monitor.Bounds.Height;
        RenderOptions.SetBitmapScalingMode(_back, BitmapScalingMode.NearestNeighbor);
        if (session.Frozen && session.Frame is { } frame)
        {
            _back.Source = frame.Crop(new Int32Rect(monitor.Bounds.X - frame.X, monitor.Bounds.Y - frame.Y, monitor.Bounds.Width, monitor.Bounds.Height));
            _back.Width = monitor.Bounds.Width;
            _back.Height = monitor.Bounds.Height;
        }

        _root.Children.Add(_back);
        _view = new SelectionView(session, monitor) { Cursor = Cursors.Cross, Background = Brushes.Transparent };
        _root.Children.Add(_view);
        _chrome = new ChromeLayer(session, monitor);
        _root.Children.Add(_chrome);
        Content = _root;

        _view.MouseLeftButtonDown += (_, e) =>
        {
            _view.CaptureMouse();
            session.MouseDown(this, GlobalPoint());
            e.Handled = true;
        };
        _view.MouseMove += (_, _) =>
        {
            if (!IsActive)
            {
                ActivateAndFocus();
            }

            session.MouseMove(this, GlobalPoint());
        };
        _view.MouseLeftButtonUp += (_, e) =>
        {
            _view.ReleaseMouseCapture();
            session.MouseUp(this, GlobalPoint());
            e.Handled = true;
        };
        _view.MouseRightButtonDown += (_, _) => CaptureSession.CancelCurrent();
        MouseWheel += (_, e) => session.Wheel(e.Delta, Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
        PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl)
            {
                return;
            }

            session.KeyDown(key, Keyboard.Modifiers, e.IsRepeat);
            e.Handled = true;
        };
    }

    public MonitorInfo Monitor { get; }

    private Point GlobalPoint()
    {
        var (x, y) = ScreenGrab.CursorPos();
        return new Point(x, y);
    }

    public void SetFrame(ScreenFrame? frame)
    {
        _view.InvalidateVisual();
    }

    public void Refresh()
    {
        if (_closing)
        {
            return;
        }

        _view.InvalidateVisual();
        _chrome.Refresh();
    }

    public void ActivateAndFocus()
    {
        Activate();
        Focus();
        var h = new WindowInteropHelper(this).Handle;
        if (h != 0)
        {
            N.SetForegroundWindow(h);
        }
    }

    public void CloseOverlay()
    {
        _closing = true;
        try
        {
            Close();
        }
        catch
        {
            // already closed
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var h = new WindowInteropHelper(this).Handle;
        var style = N.GetWindowLongPtr(h, N.GWL_EXSTYLE).ToInt64() | N.WS_EX_TOOLWINDOW;
        N.SetWindowLongPtr(h, N.GWL_EXSTYLE, (nint)style);
        N.ExcludeFromCapture(h);
        ApplyBounds();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ApplyBounds();
    }

    private void ApplyBounds()
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h == 0)
        {
            return;
        }

        N.SetWindowPos(h, -1, Monitor.Bounds.X, Monitor.Bounds.Y, Monitor.Bounds.Width, Monitor.Bounds.Height, 0x0010);
        var dpi = VisualTreeHelper.GetDpi(this);
        _root.LayoutTransform = new ScaleTransform(1.0 / dpi.DpiScaleX, 1.0 / dpi.DpiScaleY);
    }
}

/// <summary>Draws dim, selection, window highlight, ghost outline, size badge and the loupe (units = physical pixels).</summary>
internal sealed class SelectionView : Canvas
{
    private static readonly Color Blue = Color.FromArgb(250, 46, 140, 255);
    private readonly CaptureSession _s;
    private readonly MonitorInfo _m;

    public SelectionView(CaptureSession session, MonitorInfo monitor)
    {
        _s = session;
        _m = monitor;
        Width = monitor.Bounds.Width;
        Height = monitor.Bounds.Height;
        IsHitTestVisible = true;
    }

    private double S => _m.Scale;

    private Rect ToLocal(Rect screen) => new(screen.X - _m.Bounds.X, screen.Y - _m.Bounds.Y, screen.Width, screen.Height);

    private static Geometry Rounded(Rect r, double radius) => new RectangleGeometry(r, radius, radius);

    protected override void OnRender(DrawingContext dc)
    {
        var full = new Rect(0, 0, _m.Bounds.Width, _m.Bounds.Height);
        var app = _s.Services.App;
        var dimBrush = new SolidColorBrush(Color.FromArgb((byte)((_s.Frozen ? 0.22 : 0.18) * 255), 0, 0, 0));
        Rect? lit = null;
        double litRadius = 8 * S;
        var scrolling = _s.Scrolling;
        if (_s.Tool == CaptureTool.Color)
        {
            dc.DrawRectangle(dimBrush, null, full);
        }
        else if (_s.Dragging && _s.DragMonitor == _m.Index && !_s.Selection.IsEmpty && _s.Selection.Width > 0)
        {
            lit = ToLocal(_s.Selection);
        }
        else if (!_s.Dragging && _s.Hover is { } hover && app.Get<bool>(K.HighlightWindows) && !scrolling)
        {
            var hb = ToLocal(new Rect(hover.Bounds.X, hover.Bounds.Y, hover.Bounds.Width, hover.Bounds.Height));
            if (hb.IntersectsWith(full))
            {
                hb.Intersect(full);
                lit = hb;
                litRadius = 9 * S;
            }
        }

        if (lit is { } l)
        {
            var hole = Rounded(l, litRadius);
            var dim = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(full), hole);
            dc.DrawGeometry(dimBrush, null, dim);
            if (_s.Dragging)
            {
                // outer glow + stroke, inner hairline
                var glow = new Pen(new SolidColorBrush(Color.FromArgb(110, 46, 140, 255)), 8 * S);
                dc.DrawGeometry(null, glow, Rounded(Rect.Inflate(l, 1.5 * S, 1.5 * S), 9 * S));
                dc.DrawGeometry(null, new Pen(new SolidColorBrush(Blue), 3 * S), Rounded(Rect.Inflate(l, 1.5 * S, 1.5 * S), 9 * S));
                dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(242, 255, 255, 255)), 1 * S), Rounded(Rect.Inflate(l, 0.5 * S, 0.5 * S), 8 * S));
                DrawBadge(dc, l, full);
            }
            else
            {
                var fill = new SolidColorBrush(Color.FromArgb(36, 89, 158, 255));
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(242, 89, 158, 255)), 2.5 * S);
                dc.DrawGeometry(fill, pen, Rounded(Rect.Inflate(l, -1.25 * S, -1.25 * S), 9 * S));
            }
        }
        else if (_s.Tool != CaptureTool.Color)
        {
            dc.DrawRectangle(dimBrush, null, full);
        }

        // ghost outline of the last region
        if (!_s.Dragging && app.Get<bool>(K.ShowLastRegion) && _s.Tool != CaptureTool.Color && _s.LastRegion(_m.Index) is { } last)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(166, 255, 255, 255)), 1 * S) { DashStyle = new DashStyle(new[] { 5.0, 4.0 }, 0) };
            dc.DrawRectangle(null, pen, new Rect(last.X, last.Y, last.Width, last.Height));
        }

        if (_s.LoupeOn && _s.Frame is not null && !(Keyboard.IsKeyDown(Key.Space) && _s.Dragging))
        {
            DrawLoupe(dc, full);
        }
    }

    private FormattedText Text(string text, double size, Brush brush, FontWeight? weight = null, bool mono = false)
    {
        var typeface = new Typeface(mono ? new FontFamily("Segoe UI Variable Text, Segoe UI") : UiKit.TextFont, FontStyles.Normal, weight ?? FontWeights.SemiBold, FontStretches.Normal);
        return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size * S, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    private void DrawBadge(DrawingContext dc, Rect sel, Rect full)
    {
        var w = (int)Math.Round(_s.Selection.Width);
        var h = (int)Math.Round(_s.Selection.Height);
        var ft = Text($"{w} × {h}", 11, Brushes.White);
        var bw = ft.Width + 14 * S;
        var bh = ft.Height + 6 * S;
        var x = Math.Clamp(sel.X + sel.Width / 2 - bw / 2, 6 * S, Math.Max(6 * S, full.Width - bw - 6 * S));
        var y = sel.Bottom + 10 * S;
        if (y + bh > full.Height - 6 * S)
        {
            y = Math.Max(6 * S, sel.Bottom - bh - 10 * S);
            if (y < sel.Y + 4)
            {
                y = sel.Y + 8 * S;
            }
        }

        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(184, 0, 0, 0)), null, new Rect(x, y, bw, bh), 5 * S, 5 * S);
        dc.DrawText(ft, new Point(x + 7 * S, y + 3 * S));
    }

    private void DrawLoupe(DrawingContext dc, Rect full)
    {
        var frame = _s.Frame!;
        var p = _s.Pointer;
        var local = new Point(p.X - _m.Bounds.X, p.Y - _m.Bounds.Y);
        if (local.X < 0 || local.Y < 0 || local.X >= full.Width || local.Y >= full.Height)
        {
            return;
        }

        var fx = (int)Math.Floor(p.X) - frame.X;
        var fy = (int)Math.Floor(p.Y) - frame.Y;
        var side = Math.Min(LoupeMath.SampleSide(_s.Zoom), Math.Min(frame.Width, frame.Height));
        var x0 = Math.Clamp(fx - side / 2, 0, Math.Max(0, frame.Width - side));
        var y0 = Math.Clamp(fy - side / 2, 0, Math.Max(0, frame.Height - side));
        var crop = new CroppedBitmap(frame.Bitmap, new Int32Rect(x0, y0, side, side));
        RenderOptions.SetBitmapScalingMode(crop, BitmapScalingMode.NearestNeighbor);

        var size = 132 * S;
        var gap = 6 * S;
        var barH = 24 * S;
        var inset = 8 * S;
        var ox = local.X + 16 * S;
        var oy = local.Y + 16 * S;
        if (ox + size > full.Width - inset)
        {
            ox = local.X - 16 * S - size;
        }

        if (oy + size + gap + barH > full.Height - inset)
        {
            oy = local.Y - 16 * S - size - gap - barH;
        }

        ox = Math.Clamp(ox, inset, Math.Max(inset, full.Width - size - inset));
        oy = Math.Clamp(oy, inset, Math.Max(inset, full.Height - size - gap - barH - inset));
        var frameRect = new Rect(ox, oy, size, size);

        dc.PushClip(Rounded(frameRect, 9 * S));
        dc.DrawImage(crop, frameRect);
        var cell = size / side;
        if (cell >= 6)
        {
            var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(71, 0, 0, 0)), 1);
            for (var i = 1; i < side; i++)
            {
                dc.DrawLine(gridPen, new Point(ox + i * cell, oy), new Point(ox + i * cell, oy + size));
                dc.DrawLine(gridPen, new Point(ox, oy + i * cell), new Point(ox + size, oy + i * cell));
            }
        }

        // reticle: ring around the target pixel + four arms
        var tx = (fx - x0) * cell;
        var ty = (fy - y0) * cell;
        var ring = new Rect(ox + tx - 1, oy + ty - 1, cell + 2, cell + 2);
        foreach (var (ringColor, width) in new[] { (Color.FromArgb(194, 0, 0, 0), 3.0 * S), (Color.FromArgb(235, 255, 255, 255), 1.0 * S) })
        {
            var pen = new Pen(new SolidColorBrush(ringColor), width);
            dc.DrawRectangle(null, pen, ring);
            var cx = ring.X + ring.Width / 2;
            var cy = ring.Y + ring.Height / 2;
            dc.DrawLine(pen, new Point(ox, cy), new Point(ring.Left - 2, cy));
            dc.DrawLine(pen, new Point(ring.Right + 2, cy), new Point(ox + size, cy));
            dc.DrawLine(pen, new Point(cx, oy), new Point(cx, ring.Top - 2));
            dc.DrawLine(pen, new Point(cx, ring.Bottom + 2), new Point(cx, oy + size));
        }

        dc.Pop();
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(242, 255, 255, 255)), 1.5 * S), Rounded(frameRect, 9 * S));

        // info bar
        var color = _s.SampleColor(p);
        var coord = $"{(int)local.X}, {(int)local.Y}";
        var label = _s.FlashText ?? (color is { } c ? $"{_s.FormatColor(c)}   {coord}" : coord);
        var ft = Text(label, 10.5, Brushes.White);
        var swatchW = color is null || _s.FlashText is not null ? 0 : 12 * S + 7 * S;
        var barW = Math.Max(size, ft.Width + swatchW + 20 * S);
        var bx = Math.Clamp(ox + size / 2 - barW / 2, 6 * S, Math.Max(6 * S, full.Width - barW - 6 * S));
        var by = oy + size + gap;
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(184, 0, 0, 0)), null, new Rect(bx, by, barW, barH), 7 * S, 7 * S);
        var contentW = ft.Width + swatchW;
        var cxp = bx + (barW - contentW) / 2;
        if (swatchW > 0 && color is { } cv)
        {
            var sw = new Rect(cxp, by + (barH - 12 * S) / 2, 12 * S, 12 * S);
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb((byte)Math.Round(cv.R * 255), (byte)Math.Round(cv.G * 255), (byte)Math.Round(cv.B * 255))), new Pen(new SolidColorBrush(Color.FromArgb(217, 255, 255, 255)), 1), sw);
            cxp += swatchW;
        }

        dc.DrawText(ft, new Point(cxp, by + (barH - ft.Height) / 2));
    }
}
