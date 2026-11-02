// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace WinKnife.Features.Capture;

/// <summary>
/// Turns a plan plus a source picture into the finished frame. One visual tree serves both the editor stage (source = the live
/// MediaElement) and the export (source = an Image fed with decoded frames), so what is watched and what is written cannot disagree.
/// Z-order: plate, [source, blurs, ring, pointer] zoomed inside the rounded card, images, then texts (never zoomed).
/// </summary>
internal sealed class RecorderComposer
{
    private static readonly Dictionary<string, BitmapSource> TextCache = new();
    private readonly RecorderPlan _plan;
    private readonly FrameworkElement _sourceElement;
    private readonly bool _preview;
    private readonly Canvas _root = new() { ClipToBounds = true, Background = Brushes.Black };
    private readonly Image _plate = new();
    private readonly Canvas _cardHost = new();
    private readonly Canvas _scaleHost = new() { ClipToBounds = true };
    private readonly Canvas _zoomHost = new();
    private readonly Canvas _blurLayer = new() { IsHitTestVisible = false };
    private readonly ScaleTransform _zoomScale = new();
    private readonly TranslateTransform _zoomShift = new();
    private readonly ScaleTransform _cardScale = new();
    private readonly Ellipse _ring = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Image _pointer = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed, Stretch = Stretch.Fill };
    private readonly Path _fallback = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed, Fill = Brushes.Black, Stroke = Brushes.White, StrokeLineJoin = PenLineJoin.Round, Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Direction = 270, Opacity = 0.35 } };
    private readonly Canvas _imageLayer = new() { IsHitTestVisible = false };
    private readonly Canvas _textLayer = new() { IsHitTestVisible = false };
    private readonly List<Image> _imageViews = new();
    private readonly List<Image?> _imageSprites = new();
    private readonly List<Image> _textViews = new();
    private readonly List<(Rectangle Rect, int Index)> _blurViews = new();
    private bool _raw;
    private Guid? _selectedBlur;
    private RenderTargetBitmap? _target;
    private readonly List<(string Key, BitmapSource Sprite)> _textSprites = new();

    public RecorderComposer(RecorderPlan plan, FrameworkElement sourceElement, bool preview)
    {
        _plan = plan;
        _sourceElement = sourceElement;
        _preview = preview;
        Build();
    }

    public Canvas Root => _root;

    public Size CanvasSize => _raw ? _plan.Source : _plan.Canvas;

    public bool Raw => _raw;

    private void Build()
    {
        var p = _plan;
        _root.Width = p.Canvas.Width;
        _root.Height = p.Canvas.Height;
        if (p.NeedsCanvas)
        {
            _plate.Source = BuildPlate(p);
            _plate.Width = p.Canvas.Width;
            _plate.Height = p.Canvas.Height;
            RenderOptions.SetBitmapScalingMode(_plate, BitmapScalingMode.NearestNeighbor);
            _root.Children.Add(_plate);
        }

        _cardHost.Width = p.Card.Width;
        _cardHost.Height = p.Card.Height;
        _cardHost.ClipToBounds = true;
        if (p.Corner > 0)
        {
            _cardHost.Clip = new RectangleGeometry(new Rect(0, 0, p.Card.Width, p.Card.Height), p.Corner, p.Corner);
        }

        Canvas.SetLeft(_cardHost, p.Card.X);
        Canvas.SetTop(_cardHost, p.Card.Y);
        _cardScale.ScaleX = p.Card.Width / p.Source.Width;
        _cardScale.ScaleY = p.Card.Height / p.Source.Height;
        _scaleHost.Width = p.Source.Width;
        _scaleHost.Height = p.Source.Height;
        _scaleHost.RenderTransform = _cardScale;
        _zoomHost.Width = p.Source.Width;
        _zoomHost.Height = p.Source.Height;
        _zoomHost.RenderTransform = new TransformGroup { Children = { _zoomScale, _zoomShift } };
        _sourceElement.Width = p.Source.Width;
        _sourceElement.Height = p.Source.Height;
        RenderOptions.SetBitmapScalingMode(_sourceElement, BitmapScalingMode.HighQuality);
        _zoomHost.Children.Add(_sourceElement);
        _blurLayer.Width = p.Source.Width;
        _blurLayer.Height = p.Source.Height;
        _zoomHost.Children.Add(_blurLayer);
        _ring.Stroke = Brushes.White;
        _zoomHost.Children.Add(_ring);
        RenderOptions.SetBitmapScalingMode(_pointer, BitmapScalingMode.HighQuality);
        _zoomHost.Children.Add(_pointer);
        _zoomHost.Children.Add(_fallback);
        _scaleHost.Children.Add(_zoomHost);
        _cardHost.Children.Add(_scaleHost);
        _root.Children.Add(_cardHost);

        _imageLayer.Width = _textLayer.Width = p.Canvas.Width;
        _imageLayer.Height = _textLayer.Height = p.Canvas.Height;
        for (var i = 0; i < p.Images.Count; i++)
        {
            var sprite = ImageSprite(p.Images[i], p.Canvas);
            var view = new Image { Source = sprite, Width = sprite?.PixelWidth ?? 0, Height = sprite?.PixelHeight ?? 0, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            if (sprite is not null)
            {
                var o = Anchors.Origin(p.Images[i].Anchor, new Size(sprite.PixelWidth, sprite.PixelHeight), p.Canvas);
                Canvas.SetLeft(view, o.X);
                Canvas.SetTop(view, o.Y);
            }

            RenderOptions.SetBitmapScalingMode(view, BitmapScalingMode.HighQuality);
            _imageViews.Add(view);
            _imageSprites.Add(sprite is null ? null : view);
            _imageLayer.Children.Add(view);
        }

        for (var i = 0; i < p.Texts.Count; i++)
        {
            var sprite = TextSprite(p.Texts[i], p.Canvas.Height);
            var view = new Image { Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            if (sprite is not null)
            {
                view.Source = sprite;
                view.Width = sprite.PixelWidth;
                view.Height = sprite.PixelHeight;
                var o = Anchors.Origin(p.Texts[i].Anchor, new Size(sprite.PixelWidth, sprite.PixelHeight), p.Canvas);
                Canvas.SetLeft(view, o.X);
                Canvas.SetTop(view, o.Y);
            }

            _textViews.Add(view);
            _textLayer.Children.Add(view);
        }

        _root.Children.Add(_imageLayer);
        _root.Children.Add(_textLayer);

        if (_preview)
        {
            for (var i = 0; i < p.Blurs.Count; i++)
            {
                var b = p.Blurs[i];
                var rect = new Rectangle
                {
                    Visibility = Visibility.Collapsed,
                    Fill = new VisualBrush(_sourceElement) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.RelativeToBoundingBox, Viewbox = new Rect(b.X, b.Y, b.Width, b.Height) },
                    IsHitTestVisible = false,
                };
                _blurViews.Add((rect, i));
                _blurLayer.Children.Add(rect);
            }
        }

        _root.Measure(p.Canvas);
        _root.Arrange(new Rect(new Point(0, 0), p.Canvas));
        _root.UpdateLayout();
    }

    /// <summary>The recording as captured: no frame, zoom, pointer, blur or overlay (blur drawing and aiming use it).</summary>
    public void SetRaw(bool raw)
    {
        _raw = raw;
        var p = _plan;
        var size = raw ? p.Source : p.Canvas;
        _root.Width = size.Width;
        _root.Height = size.Height;
        _plate.Visibility = raw ? Visibility.Collapsed : Visibility.Visible;
        _imageLayer.Visibility = _textLayer.Visibility = raw ? Visibility.Collapsed : Visibility.Visible;
        _blurLayer.Visibility = raw ? Visibility.Collapsed : Visibility.Visible;
        if (raw)
        {
            _cardHost.Clip = null;
            _cardHost.Width = p.Source.Width;
            _cardHost.Height = p.Source.Height;
            Canvas.SetLeft(_cardHost, 0);
            Canvas.SetTop(_cardHost, 0);
            _cardScale.ScaleX = _cardScale.ScaleY = 1;
            _zoomScale.ScaleX = _zoomScale.ScaleY = 1;
            _zoomShift.X = _zoomShift.Y = 0;
            _ring.Visibility = _pointer.Visibility = _fallback.Visibility = Visibility.Collapsed;
        }
        else
        {
            _cardHost.Width = p.Card.Width;
            _cardHost.Height = p.Card.Height;
            if (p.Corner > 0)
            {
                _cardHost.Clip = new RectangleGeometry(new Rect(0, 0, p.Card.Width, p.Card.Height), p.Corner, p.Corner);
            }

            Canvas.SetLeft(_cardHost, p.Card.X);
            Canvas.SetTop(_cardHost, p.Card.Y);
            _cardScale.ScaleX = p.Card.Width / p.Source.Width;
            _cardScale.ScaleY = p.Card.Height / p.Source.Height;
        }

        _root.Measure(size);
        _root.Arrange(new Rect(new Point(0, 0), size));
    }

    public void SetSelectedBlur(Guid? id) => _selectedBlur = id;

    /// <summary>Takes the source element (the live MediaElement) out of this composer's tree so another composer can host it.</summary>
    public void DetachSource() => _zoomHost.Children.Remove(_sourceElement);

    // ---- per frame -----------------------------------------------------------------------------------------------------

    /// <param name="index">Plan frame index.</param>
    /// <param name="editedSeconds">Time on the edited (unscaled) clock, used for the blur safeguard between plan samples.</param>
    public void Apply(int index, double editedSeconds)
    {
        if (_raw)
        {
            _root.UpdateLayout();
            return;
        }

        var p = _plan;
        index = Math.Clamp(index, 0, Math.Max(0, p.Frames - 1));
        var z = index < p.Zoom.Length ? p.Zoom[index] : 1;
        if (z > 1.001 && index < p.Travel.Length)
        {
            var origin = RecorderMotion.ViewportOrigin(p.Travel[index], z);
            _zoomScale.ScaleX = _zoomScale.ScaleY = z;
            _zoomShift.X = -origin.X * p.Source.Width * z;
            _zoomShift.Y = -origin.Y * p.Source.Height * z;
        }
        else
        {
            _zoomScale.ScaleX = _zoomScale.ScaleY = 1;
            _zoomShift.X = _zoomShift.Y = 0;
        }

        UpdatePointer(index);
        UpdateBlurs(editedSeconds);
        for (var i = 0; i < _imageViews.Count; i++)
        {
            var o = i < p.ImageOpacity.Length && index < p.ImageOpacity[i].Length ? p.ImageOpacity[i][index] : 0;
            _imageViews[i].Visibility = o > 0.01 && _imageSprites[i] is not null ? Visibility.Visible : Visibility.Collapsed;
            _imageViews[i].Opacity = o;
        }

        for (var i = 0; i < _textViews.Count; i++)
        {
            var o = i < p.TextOpacity.Length && index < p.TextOpacity[i].Length ? p.TextOpacity[i][index] : 0;
            _textViews[i].Visibility = o > 0.01 && _textViews[i].Source is not null ? Visibility.Visible : Visibility.Collapsed;
            _textViews[i].Opacity = o;
        }

        _root.UpdateLayout();
    }

    private void UpdatePointer(int index)
    {
        var p = _plan;
        _ring.Visibility = _pointer.Visibility = _fallback.Visibility = Visibility.Collapsed;
        if (!p.ShowsPointer || index >= p.Positions.Length || !(index < p.Visible.Length ? p.Visible[index] : true))
        {
            return;
        }

        var opacity = index < p.PointerOpacity.Length ? p.PointerOpacity[index] : 1;
        var uv = p.Positions[index];
        if (opacity <= 0.01 || uv.X <= -0.2 || uv.X >= 1.2 || uv.Y <= -0.2 || uv.Y >= 1.2)
        {
            return;
        }

        var press = index < p.PressScale.Length ? p.PressScale[index] : 1;
        var x = uv.X * p.Source.Width;
        var y = uv.Y * p.Source.Height;
        if (p.ShowsClickRing && index < p.Ring.Length && p.Ring[index] >= 0)
        {
            var progress = p.Ring[index];
            var eased = 1 - Math.Pow(1 - progress, 3);
            var unit = p.PointerPixelSize / 34;
            var radius = RecorderMotion.RingRadius * unit * eased;
            var width = Math.Max(0.6, 2.4 * (1 - progress)) * unit;
            var alpha = 0.35 * (1 - progress * progress);
            if (radius > 1 && alpha > 0.01)
            {
                _ring.Width = _ring.Height = radius * 2;
                _ring.StrokeThickness = width;
                _ring.Opacity = alpha;
                Canvas.SetLeft(_ring, x - radius);
                Canvas.SetTop(_ring, y - radius);
                _ring.Visibility = Visibility.Visible;
            }
        }

        var shapeIndex = index < p.ShapeIndex.Length ? p.ShapeIndex[index] : 0;
        CursorShape? shape = p.Shapes.Count == 0 ? null : p.Shapes[Math.Clamp(shapeIndex, 0, p.Shapes.Count - 1)];
        if (shape?.Image is { } sprite && shape.Width > 0 && shape.Height > 0)
        {
            var scale = p.PointerScale * press;
            _pointer.Source = sprite;
            _pointer.Width = shape.Width * scale;
            _pointer.Height = shape.Height * scale;
            _pointer.Opacity = opacity;
            Canvas.SetLeft(_pointer, x - shape.HotX * scale);
            Canvas.SetTop(_pointer, y - shape.HotY * scale);
            _pointer.Visibility = Visibility.Visible;
            return;
        }

        // the take never managed to read a real pointer: draw the vector arrow (black fill, white outline, soft shadow)
        var size = Math.Max(24, p.Source.Height * 0.028) * (p.PointerScale) * press;
        var u = size / 28.0;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            Point[] outline = [new(4.5, 4.0), new(4.5, 31.5), new(10.5, 25.5), new(14.5, 35.5), new(18.5, 33.8), new(14.6, 24.0), new(22.5, 23.5)];
            ctx.BeginFigure(new Point(outline[0].X * u, outline[0].Y * u), true, true);
            ctx.PolyLineTo(outline.Skip(1).Select(o => new Point(o.X * u, o.Y * u)).ToList(), true, true);
        }

        g.Freeze();
        _fallback.Data = g;
        _fallback.StrokeThickness = u * 2.4;
        _fallback.Opacity = opacity;
        Canvas.SetLeft(_fallback, x - 4.5 * u);
        Canvas.SetTop(_fallback, y - 4.0 * u);
        _fallback.Visibility = Visibility.Visible;
    }

    private void UpdateBlurs(double editedSeconds)
    {
        foreach (var (rect, i) in _blurViews)
        {
            var b = _plan.Blurs[i];
            var on = _plan.BlurCovered(i, editedSeconds);
            if (!on)
            {
                rect.Visibility = Visibility.Collapsed;
                continue;
            }

            var w = _plan.Source.Width;
            var h = _plan.Source.Height;
            rect.Width = Math.Max(1, b.Width * w);
            rect.Height = Math.Max(1, b.Height * h);
            Canvas.SetLeft(rect, b.X * w);
            Canvas.SetTop(rect, b.Y * h);
            var block = RecorderComposer.BlurBlock(rect.Width, rect.Height, b.Strength);
            rect.Effect = new BlurEffect { Radius = Math.Max(4, block * 0.6), KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
            rect.Stroke = b.Id == _selectedBlur ? new SolidColorBrush(Color.FromRgb(0x30, 0xB0, 0xC7)) : null;
            rect.StrokeThickness = 2;
            rect.Visibility = Visibility.Visible;
        }
    }

    private static readonly double[] BlurFactors = [0.4, 0.65, 1, 1.5, 2.2];

    /// <summary>Mosaic block size in source pixels, sized from the blurred area: base = clamp(round(min(w,h)/3), 8, 48) times the strength factor.</summary>
    public static double BlurBlock(double w, double h, int strength)
    {
        var side = Math.Min(w, h);
        var baseSize = side > 0 && double.IsFinite(side) ? Math.Min(48, Math.Max(8, Math.Round(side / 3))) : 8;
        return Math.Max(2, Math.Round(baseSize * BlurFactors[Math.Clamp(strength, 1, 5) - 1]));
    }

    // ---- export --------------------------------------------------------------------------------------------------------

    /// <summary>Renders the current state into reusable top-down BGRA bytes at the canvas size.</summary>
    public byte[] RenderBgra()
    {
        var w = (int)Math.Round(_root.Width);
        var h = (int)Math.Round(_root.Height);
        if (_target is null || _target.PixelWidth != w || _target.PixelHeight != h)
        {
            _target = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        }

        _target.Clear();
        _target.Render(_root);
        var bytes = new byte[w * h * 4];
        _target.CopyPixels(bytes, w * 4, 0);
        return bytes;
    }

    // ---- plate ---------------------------------------------------------------------------------------------------------

    private static BitmapSource BuildPlate(RecorderPlan p)
    {
        var w = p.Canvas.Width;
        var h = p.Canvas.Height;
        var root = new Canvas { Width = w, Height = h, ClipToBounds = true, Background = Brushes.Black };
        var style = p.Style;
        var fill = style.CreateBrush(w, h);
        if (fill is not null)
        {
            var radius = style.Blur * Math.Min(w, h) * 0.035;
            var bleed = radius > 0 ? radius * 3 : 0;
            var bg = new Rectangle { Width = w + bleed * 2, Height = h + bleed * 2, Fill = fill };
            if (radius > 0)
            {
                bg.Effect = new BlurEffect { Radius = radius, KernelType = KernelType.Gaussian };
            }

            Canvas.SetLeft(bg, -bleed);
            Canvas.SetTop(bg, -bleed);
            root.Children.Add(bg);
        }

        // the recording sits on the background rather than in it: a soft shadow is the whole difference
        var card = new Rectangle
        {
            Width = p.Card.Width, Height = p.Card.Height, RadiusX = p.Corner, RadiusY = p.Corner, Fill = Brushes.Black,
            Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.42, BlurRadius = Math.Min(h * 0.045, 90), ShadowDepth = Math.Min(h * 0.012, 26), Direction = 270 },
        };
        Canvas.SetLeft(card, p.Card.X);
        Canvas.SetTop(card, p.Card.Y);
        root.Children.Add(card);
        root.Measure(p.Canvas);
        root.Arrange(new Rect(new Point(0, 0), p.Canvas));
        root.UpdateLayout();
        var rtb = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(root);
        rtb.Freeze();
        return rtb;
    }

    // ---- sprites -------------------------------------------------------------------------------------------------------

    private BitmapSource? TextSprite(TextOverlay overlay, double canvasHeight)
    {
        if (string.IsNullOrWhiteSpace(overlay.Text))
        {
            return null;
        }

        var pointSize = Math.Max(8, Math.Round(canvasHeight * overlay.Size));
        var key = $"{overlay.Text}|{pointSize}|{overlay.Palette}";
        lock (TextCache)
        {
            if (TextCache.TryGetValue(key, out var hit))
            {
                return hit;
            }
        }

        var (r, g, b) = TextPalette.Components(overlay.Palette);
        var color = Color.FromRgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
        var tb = new TextBlock
        {
            Text = overlay.Text, FontSize = pointSize, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            Foreground = new SolidColorBrush(color), TextAlignment = TextAlignment.Center,
            Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.55, BlurRadius = 0.18 * pointSize, ShadowDepth = 0.04 * pointSize, Direction = 270 },
        };
        var host = new Border { Child = tb, Padding = new Thickness(0.5 * pointSize), Background = Brushes.Transparent };
        host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = host.DesiredSize;
        host.Arrange(new Rect(new Point(0, 0), size));
        host.UpdateLayout();
        var rtb = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(size.Width)), Math.Max(1, (int)Math.Ceiling(size.Height)), 96, 96, PixelFormats.Pbgra32);
        rtb.Render(host);
        rtb.Freeze();
        lock (TextCache)
        {
            if (TextCache.Count >= 48)
            {
                TextCache.Clear();
            }

            TextCache[key] = rtb;
        }

        return rtb;
    }

    private static BitmapSource? ImageSprite(ImageOverlay overlay, Size canvas)
    {
        try
        {
            if (!File.Exists(overlay.Path))
            {
                return null;
            }

            var probe = BitmapFrame.Create(new Uri(overlay.Path), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var drawn = ImageOverlay.DrawnSize(new Size(probe.PixelWidth, probe.PixelHeight), overlay.Size, canvas);
            if (drawn is not { } d)
            {
                return null;
            }

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(overlay.Path);
            bmp.DecodePixelWidth = (int)d.Width;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.EndInit();
            bmp.Freeze();
            var converted = bmp.Format == PixelFormats.Pbgra32 || bmp.Format == PixelFormats.Bgra32 ? (BitmapSource)bmp : new FormatConvertedBitmap(bmp, PixelFormats.Pbgra32, null, 0);
            converted.Freeze();
            return converted;
        }
        catch
        {
            return null;
        }
    }
}
