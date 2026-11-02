// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WpfPath = System.Windows.Shapes.Path;
using WinKnife.Core.Native;
using static WinKnife.Core.Native.NativeConstants;

namespace WinKnife.Features.Tools;

/// <summary>
/// Borderless topmost window that shows a frozen copy of the whole virtual desktop at exact pixel positions,
/// regardless of the DPI of each monitor. Subclasses draw on <see cref="Surface"/> using device-independent units
/// and convert with <see cref="ToPx"/> / <see cref="ToDip"/>.
/// </summary>
internal abstract class FrozenScreenWindow : Window
{
    protected FrozenScreenWindow(ScreenFrame frame)
    {
        Frame = frame;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Black;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Cursor = Cursors.Cross;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        BackImage = new Image { Source = frame.Bitmap, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(BackImage, BitmapScalingMode.NearestNeighbor);
        Surface = new Canvas { ClipToBounds = true, Background = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        Surface.Children.Add(BackImage);
        Content = Surface;

        // Pre-size so WPF does not try to auto-size; the exact pixel rectangle is applied after the handle exists.
        Left = frame.X;
        Top = frame.Y;
        Width = Math.Max(1, frame.Width);
        Height = Math.Max(1, frame.Height);
    }

    protected ScreenFrame Frame { get; }

    protected Canvas Surface { get; }

    protected Image BackImage { get; }

    protected double Scale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    /// <summary>Converts a point on <see cref="Surface"/> to frame pixels.</summary>
    protected Point ToPx(Point dip) => new(dip.X * Scale, dip.Y * Scale);

    /// <summary>Converts frame pixels to a point on <see cref="Surface"/>.</summary>
    protected Point ToDip(Point px) => new(px.X / Scale, px.Y / Scale);

    protected Rect ToDip(Rect px) => new(px.X / Scale, px.Y / Scale, px.Width / Scale, px.Height / Scale);

    /// <summary>Called after the layout scale changed (also once at start). Redraw dependent visuals here.</summary>
    protected virtual void OnLayoutChanged()
    {
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyBounds();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ApplyBounds();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Cancel();
        }
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        Cancel();
    }

    /// <summary>Called when the user cancels (Esc / right click).</summary>
    protected abstract void Cancel();

    public void ShowAndActivate()
    {
        Show();
        ApplyBounds();
        Activate();
        Focus();
        NativeMethods.SetForegroundWindow(new WindowInteropHelper(this).Handle);
    }

    private void ApplyBounds()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0)
        {
            return;
        }

        NativeMethods.SetWindowPos(handle, HWND_TOPMOST, Frame.X, Frame.Y, Frame.Width, Frame.Height, 0);
        var scale = Scale;
        var w = Frame.Width / scale;
        var h = Frame.Height / scale;
        Surface.Width = w;
        Surface.Height = h;
        BackImage.Width = w;
        BackImage.Height = h;
        OnLayoutChanged();
    }
}

/// <summary>Frozen-screen overlay that lets the user drag a region (or click a window) and returns it in frame pixels.</summary>
internal sealed class RegionSelectorWindow : FrozenScreenWindow
{
    private readonly TaskCompletionSource<Int32Rect?> _result = new();
    private readonly List<(nint Handle, string Title, Int32Rect Bounds)>? _windows;
    private readonly WpfPath _dim = new() { Fill = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), IsHitTestVisible = false };
    private readonly Rectangle _selection = new() { Stroke = Brushes.White, StrokeThickness = 1.5, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Rectangle _hover = new() { Stroke = new SolidColorBrush(Color.FromRgb(0x4F, 0x8C, 0xFF)), StrokeThickness = 3, Fill = new SolidColorBrush(Color.FromArgb(40, 0x4F, 0x8C, 0xFF)), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Border _label = new() { Background = new SolidColorBrush(Color.FromArgb(220, 20, 20, 24)), CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 3, 8, 3), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly TextBlock _labelText = new() { Foreground = Brushes.White, FontSize = 12 };
    private readonly Border _hint = new() { Background = new SolidColorBrush(Color.FromArgb(220, 20, 20, 24)), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 8, 14, 8), IsHitTestVisible = false };
    private Point? _start;
    private Point _startPx;
    private Rect _currentPx;
    private bool _dragging;
    private bool _done;

    public RegionSelectorWindow(ScreenFrame frame, string hint, List<(nint Handle, string Title, Int32Rect Bounds)>? windows = null)
        : base(frame)
    {
        _windows = windows;
        _label.Child = _labelText;
        _hint.Child = new TextBlock { Text = hint, Foreground = Brushes.White, FontSize = 13 };
        Surface.Children.Add(_dim);
        Surface.Children.Add(_hover);
        Surface.Children.Add(_selection);
        Surface.Children.Add(_label);
        Surface.Children.Add(_hint);
        Closed += (_, _) => _result.TrySetResult(null);
    }

    /// <summary>Shows the selector and completes with the chosen rectangle in frame pixels, or null when cancelled.</summary>
    public Task<Int32Rect?> PickAsync()
    {
        ShowAndActivate();
        return _result.Task;
    }

    protected override void OnLayoutChanged()
    {
        UpdateDim(null);
        Canvas.SetLeft(_hint, Math.Max(10, (Surface.Width - 380) / 2));
        Canvas.SetTop(_hint, 40);
    }

    protected override void Cancel() => Finish(null);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _start = e.GetPosition(Surface);
        _startPx = ToPx(_start.Value);
        _dragging = false;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var pos = e.GetPosition(Surface);
        var px = ToPx(pos);
        _hint.Visibility = Visibility.Collapsed;
        if (_start is null)
        {
            UpdateHover(px);
            return;
        }

        if (!_dragging && (Math.Abs(px.X - _startPx.X) > 4 || Math.Abs(px.Y - _startPx.Y) > 4))
        {
            _dragging = true;
            _hover.Visibility = Visibility.Collapsed;
        }

        if (_dragging)
        {
            _currentPx = new Rect(
                Math.Min(_startPx.X, px.X),
                Math.Min(_startPx.Y, px.Y),
                Math.Abs(px.X - _startPx.X),
                Math.Abs(px.Y - _startPx.Y));
            ShowRect(_currentPx, $"{(int)_currentPx.Width} x {(int)_currentPx.Height}");
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();
        if (_start is null)
        {
            return;
        }

        _start = null;
        if (_dragging)
        {
            var r = _currentPx;
            if (r.Width >= 3 && r.Height >= 3)
            {
                Finish(new Int32Rect((int)r.X, (int)r.Y, (int)Math.Round(r.Width), (int)Math.Round(r.Height)));
                return;
            }

            Finish(null);
            return;
        }

        // A plain click picks the window under the pointer.
        var window = WindowAt(ToPx(e.GetPosition(Surface)));
        if (window is { } w)
        {
            Finish(w);
        }
    }

    private Int32Rect? WindowAt(Point px)
    {
        if (_windows is null)
        {
            return null;
        }

        var sx = px.X + Frame.X;
        var sy = px.Y + Frame.Y;
        foreach (var (_, _, b) in _windows)
        {
            if (sx >= b.X && sy >= b.Y && sx < b.X + b.Width && sy < b.Y + b.Height)
            {
                var x = Math.Max(0, b.X - Frame.X);
                var y = Math.Max(0, b.Y - Frame.Y);
                var right = Math.Min(Frame.Width, b.X - Frame.X + b.Width);
                var bottom = Math.Min(Frame.Height, b.Y - Frame.Y + b.Height);
                return new Int32Rect(x, y, Math.Max(1, right - x), Math.Max(1, bottom - y));
            }
        }

        return null;
    }

    private void UpdateHover(Point px)
    {
        if (WindowAt(px) is { } r)
        {
            var title = _windows!.FirstOrDefault(w =>
            {
                var sx = px.X + Frame.X;
                var sy = px.Y + Frame.Y;
                return sx >= w.Bounds.X && sy >= w.Bounds.Y && sx < w.Bounds.X + w.Bounds.Width && sy < w.Bounds.Y + w.Bounds.Height;
            }).Title;
            var dip = ToDip(new Rect(r.X, r.Y, r.Width, r.Height));
            Canvas.SetLeft(_hover, dip.X);
            Canvas.SetTop(_hover, dip.Y);
            _hover.Width = dip.Width;
            _hover.Height = dip.Height;
            _hover.Visibility = Visibility.Visible;
            _labelText.Text = title;
            PlaceLabel(dip);
        }
        else
        {
            _hover.Visibility = Visibility.Collapsed;
            _label.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowRect(Rect px, string text)
    {
        var dip = ToDip(px);
        Canvas.SetLeft(_selection, dip.X);
        Canvas.SetTop(_selection, dip.Y);
        _selection.Width = dip.Width;
        _selection.Height = dip.Height;
        _selection.Visibility = Visibility.Visible;
        UpdateDim(dip);
        _labelText.Text = text;
        PlaceLabel(dip);
    }

    private void PlaceLabel(Rect dip)
    {
        _label.Visibility = Visibility.Visible;
        Canvas.SetLeft(_label, Math.Max(4, dip.X));
        Canvas.SetTop(_label, dip.Y > 30 ? dip.Y - 28 : dip.Y + 6);
    }

    private void UpdateDim(Rect? hole)
    {
        Geometry full = new RectangleGeometry(new Rect(0, 0, Surface.Width, Surface.Height));
        _dim.Data = hole is { } h
            ? new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(h))
            : full;
    }

    private void Finish(Int32Rect? rect)
    {
        if (_done)
        {
            return;
        }

        _done = true;
        _result.TrySetResult(rect);
        Close();
    }
}

/// <summary>Frozen-screen overlay with a magnifier loupe; returns the colour under the pointer on click.</summary>
internal sealed class ColorPickerWindow : FrozenScreenWindow
{
    private const int Grid = 15;
    private const int Zoom = 10;
    private readonly TaskCompletionSource<Color?> _result = new();
    private readonly WriteableBitmap _zoom = new(Grid, Grid, 96, 96, PixelFormats.Bgra32, null);
    private readonly Border _loupe;
    private readonly TextBlock _caption = new() { Foreground = Brushes.White, FontSize = 12, FontFamily = new FontFamily("Cascadia Mono, Consolas"), TextAlignment = TextAlignment.Center };
    private readonly Rectangle _swatch = new() { Width = 14, Height = 14, Stroke = Brushes.White, StrokeThickness = 1, Margin = new Thickness(0, 0, 6, 0) };
    private bool _done;

    public ColorPickerWindow(ScreenFrame frame)
        : base(frame)
    {
        var image = new Image { Source = _zoom, Width = Grid * Zoom, Height = Grid * Zoom, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        var centre = new Rectangle
        {
            Width = Zoom,
            Height = Zoom,
            Stroke = Brushes.White,
            StrokeThickness = 1.5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var inner = new Rectangle
        {
            Width = Zoom + 2,
            Height = Zoom + 2,
            Stroke = Brushes.Black,
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var disc = new Grid { Width = Grid * Zoom, Height = Grid * Zoom, Clip = new EllipseGeometry(new Point(Grid * Zoom / 2.0, Grid * Zoom / 2.0), Grid * Zoom / 2.0, Grid * Zoom / 2.0) };
        disc.Children.Add(image);
        disc.Children.Add(inner);
        disc.Children.Add(centre);
        var ring = new Border
        {
            Width = Grid * Zoom + 8,
            Height = Grid * Zoom + 8,
            CornerRadius = new CornerRadius((Grid * Zoom + 8) / 2.0),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(3),
            Child = disc,
            Background = Brushes.Black,
        };
        var caption = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(230, 20, 20, 24)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _swatch, _caption } },
        };
        var stack = new StackPanel { Children = { ring, caption }, IsHitTestVisible = false };
        _loupe = new Border { Child = stack, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        Surface.Children.Add(_loupe);
        Closed += (_, _) => _result.TrySetResult(null);
    }

    public Task<Color?> PickAsync()
    {
        ShowAndActivate();
        UpdateLoupe(Mouse.GetPosition(Surface));
        return _result.Task;
    }

    protected override void Cancel() => Finish(null);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        UpdateLoupe(e.GetPosition(Surface));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var px = ToPx(e.GetPosition(Surface));
        Finish(Frame.PixelAt((int)px.X, (int)px.Y));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Arrow keys nudge the pointer by one physical pixel for precise picking; Enter confirms.
        var (dx, dy) = e.Key switch
        {
            Key.Left => (-1, 0),
            Key.Right => (1, 0),
            Key.Up => (0, -1),
            Key.Down => (0, 1),
            _ => (0, 0),
        };
        if (dx != 0 || dy != 0)
        {
            e.Handled = true;
            NativeMethods.GetCursorPos(out var pt);
            NativeMethods.SetCursorPos(pt.X + dx, pt.Y + dy);
            return;
        }

        if (e.Key is Key.Enter or Key.Space)
        {
            e.Handled = true;
            NativeMethods.GetCursorPos(out var pt);
            Finish(Frame.PixelAt(pt.X - Frame.X, pt.Y - Frame.Y));
            return;
        }

        base.OnKeyDown(e);
    }

    private void UpdateLoupe(Point dipPosition)
    {
        var px = ToPx(dipPosition);
        var cx = (int)px.X;
        var cy = (int)px.Y;
        var buffer = new byte[Grid * Grid * 4];
        var half = Grid / 2;
        var rect = new Int32Rect(cx - half, cy - half, Grid, Grid);
        var clipX = Math.Max(0, rect.X);
        var clipY = Math.Max(0, rect.Y);
        var clipR = Math.Min(Frame.Width, rect.X + Grid);
        var clipB = Math.Min(Frame.Height, rect.Y + Grid);
        for (var i = 3; i < buffer.Length; i += 4)
        {
            buffer[i] = 255;
        }

        if (clipR > clipX && clipB > clipY)
        {
            var w = clipR - clipX;
            var h = clipB - clipY;
            var tmp = new byte[w * h * 4];
            Frame.Bitmap.CopyPixels(new Int32Rect(clipX, clipY, w, h), tmp, w * 4, 0);
            for (var row = 0; row < h; row++)
            {
                var dest = ((clipY - rect.Y + row) * Grid + (clipX - rect.X)) * 4;
                for (var col = 0; col < w; col++)
                {
                    var src = (row * w + col) * 4;
                    buffer[dest + col * 4] = tmp[src];
                    buffer[dest + col * 4 + 1] = tmp[src + 1];
                    buffer[dest + col * 4 + 2] = tmp[src + 2];
                }
            }
        }

        _zoom.WritePixels(new Int32Rect(0, 0, Grid, Grid), buffer, Grid * 4, 0);
        var color = Frame.PixelAt(cx, cy) ?? Colors.Black;
        _swatch.Fill = new SolidColorBrush(color);
        _caption.Text = ColorFormats.Format(color, ColorFormat.Hex) + "   " + ColorFormats.Format(color, ColorFormat.Rgb);

        var size = Grid * Zoom + 8;
        var x = dipPosition.X + 28;
        var y = dipPosition.Y + 28;
        if (x + size + 10 > Surface.Width)
        {
            x = dipPosition.X - 28 - size;
        }

        if (y + size + 50 > Surface.Height)
        {
            y = dipPosition.Y - 28 - size - 40;
        }

        Canvas.SetLeft(_loupe, x);
        Canvas.SetTop(_loupe, y);
        _loupe.Visibility = Visibility.Visible;
    }

    private void Finish(Color? color)
    {
        if (_done)
        {
            return;
        }

        _done = true;
        _result.TrySetResult(color);
        Close();
    }
}
