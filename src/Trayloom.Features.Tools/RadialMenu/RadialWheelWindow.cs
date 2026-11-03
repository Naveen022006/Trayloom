// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Tools;

/// <summary>A damped spring (SwiftUI <c>spring(response:dampingFraction:)</c> converted to stiffness / damping, mass 1).</summary>
internal sealed class Spring
{
    private readonly double _omega;
    private readonly double _zeta;

    public Spring(double response, double damping, double value = 0)
    {
        _omega = 2 * Math.PI / response;
        _zeta = damping;
        Value = value;
        Target = value;
    }

    public double Value { get; set; }

    public double Velocity { get; set; }

    public double Target { get; set; }

    public bool Settled => Math.Abs(Value - Target) < 0.0005 && Math.Abs(Velocity) < 0.002;

    public void Snap(double value)
    {
        Value = value;
        Target = value;
        Velocity = 0;
    }

    public void Step(double dt)
    {
        var steps = Math.Max(1, (int)Math.Ceiling(dt / 0.004));
        var h = dt / steps;
        for (var i = 0; i < steps; i++)
        {
            var accel = -_omega * _omega * (Value - Target) - 2 * _zeta * _omega * Velocity;
            Velocity += accel * h;
            Value += Velocity * h;
        }

        if (Settled)
        {
            Snap(Target);
        }
    }
}

/// <summary>
/// The wheel panel (spec 3.6): a 400x400 transparent no-activate window with the glass disc, slice guides, wedge, ring of
/// chips, hub and the open / close / highlight animations. All decisions live in <see cref="RadialMenuFeature"/>.
/// </summary>
internal sealed class RadialWheelWindow : OverlayWindow
{
    private sealed class Chip
    {
        public required Canvas Root { get; init; }
        public required Ellipse Fill { get; init; }
        public required Border IconHost { get; init; }
        public required ScaleTransform Scale { get; init; }
        public required TranslateTransform Move { get; init; }
        public required Canvas Glow { get; init; }
        public required RadialItem Item { get; init; }
        public Spring Bloom { get; } = new(0.36, 0.75);
        public Spring Highlight { get; } = new(0.24, 0.72, 1);
        public double Delay { get; set; }
        public bool Highlighted { get; set; }
        public bool Started { get; set; }
    }

    private readonly Canvas _canvas = new() { Width = RadialGeometry.PanelSize, Height = RadialGeometry.PanelSize, Background = Brushes.Transparent };
    private readonly ScaleTransform _wheelScale = new(1, 1, 200, 200);
    private readonly List<Chip> _chips = [];
    private readonly Ellipse _plate = new() { Width = 300, Height = 300 };
    private readonly Ellipse _rim = new() { Width = 300, Height = 300 };
    private readonly Ellipse _border = new() { Width = 300, Height = 300 };
    private readonly Canvas _guides = new() { Width = 400, Height = 400, IsHitTestVisible = false };
    private readonly System.Windows.Shapes.Path _wedge = new() { IsHitTestVisible = false };
    private readonly Canvas _ring = new() { Width = 400, Height = 400, IsHitTestVisible = false };
    private readonly Ellipse _hubPlate = new() { Width = 76, Height = 76, IsHitTestVisible = false };
    private readonly ContentControl _hubFace = new() { Width = 76, Height = 76, IsHitTestVisible = false, RenderTransformOrigin = new Point(0.5, 0.5) };
    private readonly Spring _scale = new(0.27, 0.86, 0.9);
    private readonly Spring _wedgeAngle = new(0.2, 0.86);
    private double _wedgeOpacity;
    private double _wedgeOpacityTarget;
    private bool _wedgePlaced;
    private double _openTime;
    private double _closeTime = -1;
    private double _opacityTarget;
    private double _opacity;
    private Action? _closeDone;
    private bool _animating;
    private TimeSpan _last;
    private bool _light = true;
    private Color _profile = Colors.DodgerBlue;
    private string? _faceKey;
    private bool _reduceMotion;
    private int _count;

    public RadialWheelWindow()
    {
        Width = RadialGeometry.PanelSize;
        Height = RadialGeometry.PanelSize;
        var scaled = new Grid { Width = 400, Height = 400, RenderTransform = _wheelScale };
        _canvas.Children.Add(_plate);
        _canvas.Children.Add(_rim);
        _canvas.Children.Add(_border);
        foreach (var e in new[] { _plate, _rim, _border })
        {
            Canvas.SetLeft(e, 50);
            Canvas.SetTop(e, 50);
            e.IsHitTestVisible = false;
        }

        _canvas.Children.Add(_guides);
        _canvas.Children.Add(_wedge);
        _canvas.Children.Add(_ring);
        Canvas.SetLeft(_hubPlate, 162);
        Canvas.SetTop(_hubPlate, 162);
        Canvas.SetLeft(_hubFace, 162);
        Canvas.SetTop(_hubFace, 162);
        _canvas.Children.Add(_hubPlate);
        _canvas.Children.Add(_hubFace);
        scaled.Children.Add(_canvas);
        Content = scaled;
        Opacity = 0;
        MouseLeftButtonDown += (_, e) =>
        {
            var p = e.GetPosition(_canvas);
            Clicked?.Invoke(new Point(p.X - 200, p.Y - 200));
            e.Handled = true;
        };
        MouseRightButtonDown += (_, e) => e.Handled = true;
        ThemeManager.ThemeChanged += OnTheme;
        Closed += (_, _) => ThemeManager.ThemeChanged -= OnTheme;
        ApplyTheme();
    }

    /// <summary>A click inside the panel; the point is relative to the wheel centre in DIPs (y down).</summary>
    public event Action<Point>? Clicked;

    public int Count => _count;

    private void OnTheme(bool light)
    {
        ApplyTheme();
        Redraw();
    }

    private void ApplyTheme()
    {
        _light = ThemeManager.IsLight;
        _plate.Fill = new SolidColorBrush(_light ? Color.FromArgb(0xF0, 0xFA, 0xFA, 0xFA) : Color.FromArgb(0xF0, 0x26, 0x26, 0x28));
        _plate.Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 8, Direction = 270, Opacity = _light ? 0.22 : 0.55, Color = Colors.Black };
        _plate.CacheMode = new BitmapCache(1);
        _border.Stroke = new SolidColorBrush(_light ? Color.FromArgb(0x17, 0, 0, 0) : Color.FromArgb(0x1C, 255, 255, 255));
        _border.StrokeThickness = 0.8;
        _rim.StrokeThickness = 1.2;
        _rim.Stroke = new LinearGradientBrush(
            _light ? Color.FromArgb(0xF2, 255, 255, 255) : Color.FromArgb(0x4D, 255, 255, 255),
            _light ? Color.FromArgb(0x1F, 255, 255, 255) : Color.FromArgb(0x0A, 255, 255, 255),
            90);
        _hubPlate.Fill = new SolidColorBrush(_light ? Color.FromArgb(0x0B, 0, 0, 0) : Color.FromArgb(0x13, 255, 255, 255));
    }

    // ------------------------------------------------------------------ content

    /// <summary>Rebuilds the ring for a level. <paramref name="bloom"/> replays the chip bloom.</summary>
    public void SetItems(IReadOnlyList<RadialItem> items, Color profile, Func<RadialItem, FrameworkElement> iconFactory, bool bloom)
    {
        _profile = profile;
        _count = items.Count;
        _reduceMotion = !SystemParameters.ClientAreaAnimation;
        _ring.Children.Clear();
        _chips.Clear();
        _guides.Children.Clear();
        _wedgePlaced = false;
        _wedgeOpacity = 0;
        _wedgeOpacityTarget = 0;
        _wedge.Data = Geometry.Empty;
        var n = items.Count;
        if (n > 1)
        {
            for (var i = 0; i < n; i++)
            {
                var angle = 2 * Math.PI * (i + 0.5) / n;
                var inner = 47.0;
                var outer = 143.0;
                var line = new Line
                {
                    X1 = 200 + Math.Sin(angle) * inner,
                    Y1 = 200 - Math.Cos(angle) * inner,
                    X2 = 200 + Math.Sin(angle) * outer,
                    Y2 = 200 - Math.Cos(angle) * outer,
                    StrokeThickness = 1,
                    Stroke = new SolidColorBrush(_light ? Color.FromArgb(18, 0, 0, 0) : Color.FromArgb(23, 255, 255, 255)),
                    SnapsToDevicePixels = false,
                };
                _guides.Children.Add(line);
            }
        }

        for (var i = 0; i < n; i++)
        {
            var scale = new ScaleTransform(1, 1);
            var move = new TranslateTransform();
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(move);
            var fill = new Ellipse { Width = 52, Height = 52, Opacity = 0 };
            Ellipse Disc(double size, Color color, double x, double y)
            {
                var e = new Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(color), IsHitTestVisible = false };
                Canvas.SetLeft(e, x);
                Canvas.SetTop(e, y);
                return e;
            }

            var shadow = new Canvas { Width = 52, Height = 52, IsHitTestVisible = false };
            shadow.Children.Add(Disc(60, Color.FromArgb(_light ? (byte)10 : (byte)28, 0, 0, 0), -4, -1));
            shadow.Children.Add(Disc(56, Color.FromArgb(_light ? (byte)14 : (byte)40, 0, 0, 0), -2, 1));
            var glow = new Canvas { Width = 52, Height = 52, IsHitTestVisible = false, Opacity = 0 };
            glow.Children.Add(Disc(76, Color.FromArgb(22, profile.R, profile.G, profile.B), -12, -9));
            glow.Children.Add(Disc(66, Color.FromArgb(40, profile.R, profile.G, profile.B), -7, -4));
            glow.Children.Add(Disc(58, Color.FromArgb(60, profile.R, profile.G, profile.B), -3, 0));
            var baseCircle = new Ellipse
            {
                Width = 52,
                Height = 52,
                Fill = new SolidColorBrush(_light ? Color.FromArgb(0xE0, 255, 255, 255) : Color.FromArgb(0x24, 255, 255, 255)),
                Stroke = new SolidColorBrush(_light ? Color.FromArgb(0x12, 0, 0, 0) : Color.FromArgb(0x29, 255, 255, 255)),
                StrokeThickness = 0.8,
            };
            fill.Fill = new SolidColorBrush(profile);
            var host = new Border { Width = 52, Height = 52 };
            host.Child = iconFactory(items[i]);
            var root = new Canvas { Width = 52, Height = 52, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = group };
            root.Children.Add(shadow);
            root.Children.Add(glow);
            root.Children.Add(baseCircle);
            root.Children.Add(fill);
            root.Children.Add(host);
            Canvas.SetLeft(root, 174);
            Canvas.SetTop(root, 174);
            _ring.Children.Add(root);
            _chips.Add(new Chip { Root = root, Fill = fill, IconHost = host, Scale = scale, Move = move, Glow = glow, Item = items[i], Delay = n <= 1 ? 0 : 0.17 * i / (n - 1) });
        }

        _openTime = 0;
        foreach (var chip in _chips)
        {
            chip.Bloom.Snap(bloom && !_reduceMotion ? 0 : 1);
            chip.Bloom.Target = 1;
            chip.Started = !bloom || _reduceMotion;
            chip.Highlight.Snap(1);
            chip.Highlighted = false;
            ApplyChip(chip);
        }

        if (bloom && !_reduceMotion)
        {
            StartAnimating();
        }

        Redraw();
    }

    /// <summary>The icon of a chip must be recoloured when it is highlighted (glyphs go white).</summary>
    public void SetHighlight(int? index)
    {
        for (var i = 0; i < _chips.Count; i++)
        {
            var on = i == index;
            if (_chips[i].Highlighted != on)
            {
                _chips[i].Highlighted = on;
                _chips[i].Highlight.Target = on ? 1.14 : 1;
                ColorIcon(_chips[i], on);
            }
        }

        if (index is { } idx && _count > 0)
        {
            var step = 360.0 / _count;
            var target = idx * step;
            if (!_wedgePlaced)
            {
                _wedgeAngle.Snap(target);
                _wedgePlaced = true;
            }
            else
            {
                var delta = ((target - _wedgeAngle.Value) % 360 + 540) % 360 - 180;
                _wedgeAngle.Target = _wedgeAngle.Value + delta;
            }

            _wedgeOpacityTarget = 1;
            if (_reduceMotion)
            {
                _wedgeAngle.Snap(target);
                _wedgeOpacity = 1;
            }
        }
        else
        {
            _wedgeOpacityTarget = 0;
            if (_reduceMotion)
            {
                _wedgeOpacity = 0;
            }
        }

        if (_reduceMotion)
        {
            foreach (var c in _chips)
            {
                c.Highlight.Snap(c.Highlighted ? 1.14 : 1);
                ApplyChip(c);
            }

            Redraw();
        }
        else
        {
            StartAnimating();
        }
    }

    /// <summary>Hub face: a highlighted item name, "back" with the parent name, or the brand mark.</summary>
    public void SetHubFace(string key, Func<FrameworkElement> factory)
    {
        if (_faceKey == key)
        {
            return;
        }

        _faceKey = key;
        _hubFace.Content = factory();
        if (!_reduceMotion)
        {
            _hubFace.RenderTransform = new ScaleTransform(0.92, 0.92);
            var duration = TimeSpan.FromMilliseconds(140);
            _hubFace.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            ((ScaleTransform)_hubFace.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.92, 1, duration));
            ((ScaleTransform)_hubFace.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, 1, duration));
        }
    }

    public void ResetFace() => _faceKey = null;

    private void ColorIcon(Chip chip, bool white)
    {
        if (chip.IconHost.Child is TextBlock tb)
        {
            tb.Foreground = white ? Brushes.White : new SolidColorBrush(_light ? Color.FromArgb(0xD9, 0, 0, 0) : Color.FromArgb(0xD9, 255, 255, 255));
        }
    }

    // ------------------------------------------------------------------ show / hide

    /// <summary>Shows the wheel (alpha 0 to 1, scale 0.9 to 1, chip bloom).</summary>
    public void Open()
    {
        _closeDone = null;
        _closeTime = -1;
        _reduceMotion = !SystemParameters.ClientAreaAnimation;
        _opacityTarget = 1;
        _scale.Target = 1;
        if (_reduceMotion)
        {
            _scale.Snap(1);
            _opacity = 1;
            Opacity = 1;
        }
        else
        {
            _scale.Snap(0.9);
            _scale.Target = 1;
            _opacity = Opacity;
        }

        _wheelScale.ScaleX = _wheelScale.ScaleY = _scale.Value;
        if (!IsVisible)
        {
            ShowNoActivate();
        }

        StartAnimating();
    }

    /// <summary>Fades the wheel out (0.13 s), folds the chips, then calls <paramref name="done"/>.</summary>
    public void Close(Action done)
    {
        if (!IsVisible)
        {
            done();
            return;
        }

        _closeDone = done;
        if (_reduceMotion)
        {
            Finish();
            return;
        }

        _closeTime = 0;
        _opacityTarget = 0;
        _scale.Target = 0.9;
        StartAnimating();
    }

    public void HideNow()
    {
        _closeDone = null;
        _closeTime = -1;
        _animating = false;
        CompositionTarget.Rendering -= OnFrame;
        Opacity = 0;
        if (IsVisible)
        {
            Hide();
        }
    }

    private void Finish()
    {
        var done = _closeDone;
        _closeDone = null;
        _closeTime = -1;
        Opacity = 0;
        Hide();
        done?.Invoke();
    }

    // ------------------------------------------------------------------ animation loop

    private void StartAnimating()
    {
        if (_animating)
        {
            return;
        }

        _animating = true;
        _last = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        var dt = _last == TimeSpan.Zero ? 0.016 : Math.Clamp((now - _last).TotalSeconds, 0.001, 0.05);
        _last = now;
        var busy = false;

        // window opacity: opening 0.18 s ease-out, closing 0.13 s
        var rate = _opacityTarget > _opacity ? 1 / 0.18 : 1 / 0.13;
        if (Math.Abs(_opacity - _opacityTarget) > 0.001)
        {
            _opacity += Math.Sign(_opacityTarget - _opacity) * Math.Min(Math.Abs(_opacityTarget - _opacity), rate * dt);
            Opacity = _opacity;
            busy = true;
        }

        _scale.Step(dt);
        _wheelScale.ScaleX = _wheelScale.ScaleY = _scale.Value;
        busy |= !_scale.Settled;

        _openTime += dt;
        foreach (var chip in _chips)
        {
            if (!chip.Started && _openTime >= chip.Delay)
            {
                chip.Started = true;
            }

            if (chip.Started)
            {
                chip.Bloom.Step(dt);
                busy |= !chip.Bloom.Settled;
            }
            else
            {
                busy = true;
            }

            chip.Highlight.Step(dt);
            busy |= !chip.Highlight.Settled;
        }

        if (_closeTime >= 0)
        {
            _closeTime += dt;
            busy = true;
        }

        // wedge
        _wedgeAngle.Step(dt);
        busy |= !_wedgeAngle.Settled;
        if (Math.Abs(_wedgeOpacity - _wedgeOpacityTarget) > 0.001)
        {
            _wedgeOpacity += Math.Sign(_wedgeOpacityTarget - _wedgeOpacity) * Math.Min(Math.Abs(_wedgeOpacityTarget - _wedgeOpacity), dt / 0.12);
            busy = true;
        }

        foreach (var chip in _chips)
        {
            ApplyChip(chip);
        }

        Redraw();
        if (_closeTime >= 0 && (_closeTime >= 0.14 || _opacity <= 0.001))
        {
            _animating = false;
            CompositionTarget.Rendering -= OnFrame;
            Finish();
            return;
        }

        if (!busy)
        {
            _animating = false;
            CompositionTarget.Rendering -= OnFrame;
        }
    }

    private void ApplyChip(Chip chip)
    {
        var index = _chips.IndexOf(chip);
        var n = _chips.Count;
        var radius = 8 + (RadialGeometry.RingRadius - 8) * chip.Bloom.Value;
        var bloomScale = 0.35 + 0.65 * chip.Bloom.Value;
        var fold = 0.0;
        if (_closeTime >= 0)
        {
            var delay = chip.Delay * 0.25;
            var t = Math.Clamp((_closeTime - delay) / 0.1, 0, 1);
            fold = t * t;
        }

        var r = radius * (1 - fold) + 8 * fold;
        var (x, y) = RadialGeometry.ChipOffset(index, Math.Max(1, n), r);
        chip.Move.X = x;
        chip.Move.Y = y;
        var s = bloomScale * chip.Highlight.Value * (1 - 0.65 * fold);
        chip.Scale.ScaleX = chip.Scale.ScaleY = Math.Max(0.01, s);
        chip.Root.Opacity = Math.Clamp(chip.Bloom.Value * 1.6, 0, 1) * (1 - fold);
        chip.Fill.Opacity = Math.Clamp((chip.Highlight.Value - 1) / 0.14, 0, 1);
        chip.Glow.Opacity = chip.Fill.Opacity;
    }

    private void Redraw()
    {
        if (_count == 0 || _wedgeOpacity <= 0.002)
        {
            _wedge.Data = Geometry.Empty;
            return;
        }

        var step = 360.0 / _count;
        var center = _wedgeAngle.Value;
        var a0 = (center - step / 2 - 90) * Math.PI / 180;
        var a1 = (center + step / 2 - 90) * Math.PI / 180;
        const double inner = RadialGeometry.DeadZone;
        const double outer = 146;
        Point P(double r, double a) => new(200 + r * Math.Cos(a), 200 + r * Math.Sin(a));
        var figure = new PathFigure { StartPoint = P(inner, a0), IsClosed = true };
        figure.Segments.Add(new LineSegment(P(outer, a0), true));
        figure.Segments.Add(new ArcSegment(P(outer, a1), new Size(outer, outer), 0, step > 180, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(P(inner, a1), true));
        figure.Segments.Add(new ArcSegment(P(inner, a0), new Size(inner, inner), 0, step > 180, SweepDirection.Counterclockwise, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        _wedge.Data = geometry;
        var innerA = (byte)Math.Round((_light ? 0.05 : 0.08) * 255 * _wedgeOpacity);
        var outerA = (byte)Math.Round((_light ? 0.20 : 0.30) * 255 * _wedgeOpacity);
        _wedge.Fill = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            GradientOrigin = new Point(200, 200),
            Center = new Point(200, 200),
            RadiusX = 150,
            RadiusY = 150,
            GradientStops =
            [
                new GradientStop(Color.FromArgb(0, _profile.R, _profile.G, _profile.B), 0),
                new GradientStop(Color.FromArgb(innerA, _profile.R, _profile.G, _profile.B), DeadZoneOffset),
                new GradientStop(Color.FromArgb(outerA, _profile.R, _profile.G, _profile.B), 1),
            ],
        };
    }

    private const double DeadZoneOffset = RadialGeometry.DeadZone / 150.0;

    // ------------------------------------------------------------------ helpers used by the feature

    /// <summary>Creates the icon element of a chip (real file icon, favicon, glyph).</summary>
    public static FrameworkElement MakeIcon(RadialItem item, bool light, ImageSource? special = null, double glyphSize = 20, double imageSize = 34)
    {
        ImageSource? image = special;
        if (image is null && item.SymbolName.Length == 0)
        {
            if (item.Kind == RadialKinds.Url && item.CustomIconData is { Length: > 0 } data)
            {
                image = DecodeIcon(data);
            }
            else if (item.Kind is RadialKinds.App or RadialKinds.File)
            {
                var path = item.Payload.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ? item.Payload : RadialModel.Expand(item.Payload);
                image = Plat.ShellImage(path, 34);
            }
        }

        if (image is not null)
        {
            return new Image { Source = image, Width = imageSize, Height = imageSize, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, SnapsToDevicePixels = true };
        }

        return new TextBlock
        {
            Text = RadialModel.GlyphOf(item),
            FontFamily = UiKit.IconFont,
            FontSize = glyphSize,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Foreground = new SolidColorBrush(light ? Color.FromArgb(0xD9, 0, 0, 0) : Color.FromArgb(0xD9, 255, 255, 255)),
        };
    }

    private static readonly Dictionary<string, ImageSource?> IconCache = [];

    public static ImageSource? DecodeIcon(string base64)
    {
        if (IconCache.TryGetValue(base64, out var cached))
        {
            return cached;
        }

        try
        {
            var bytes = Convert.FromBase64String(base64);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new System.IO.MemoryStream(bytes);
            bmp.EndInit();
            bmp.Freeze();
            IconCache[base64] = bmp;
            return bmp;
        }
        catch
        {
            IconCache[base64] = null;
            return null;
        }
    }
}
