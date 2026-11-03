// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Island;

/// <summary>
/// Perceptual-duration spring (SwiftUI / Core Animation definition: duration + bounce) solved in closed form, so a step of any
/// length lands exactly where the continuous spring is (frame-rate independent, never unstable on a slow frame) and a retarget
/// in flight keeps position and velocity.
/// </summary>
internal sealed class Spring
{
    private double _omega;
    private double _zeta;

    public double Position;
    public double Velocity;
    public double Target;

    public Spring(double duration, double bounce, double start = 0)
    {
        Configure(duration, bounce);
        Position = Target = start;
    }

    /// <summary>Changes the character of the spring (position and velocity carry on).</summary>
    public void Configure(double duration, double bounce)
    {
        _omega = 2 * Math.PI / Math.Max(0.05, duration);
        _zeta = Math.Clamp(1 - bounce, 0.2, 1);
    }

    /// <summary>
    /// How far past its target a spring of this bounce swings, as a share of its travel (Mac <c>NotchMotion.Spring.overshoot</c>).
    /// </summary>
    public static double Overshoot(double bounce)
    {
        if (bounce <= 0)
        {
            return 0;
        }

        var damping = 1 - bounce;
        return Math.Exp(-Math.PI * damping / Math.Sqrt(1 - (damping * damping)));
    }

    /// <summary>The bounce, reduced just enough that a swing over <paramref name="travel"/> stays within <paramref name="limit"/> (Mac <c>limited</c>).</summary>
    public static double LimitedBounce(double bounce, double travel, double limit)
    {
        if (bounce <= 0 || travel <= 0 || travel * Overshoot(bounce) <= limit)
        {
            return bounce;
        }

        var share = Math.Log(Math.Max(limit, 0.01) / travel);
        return 1 + (share / Math.Sqrt((Math.PI * Math.PI) + (share * share)));
    }

    public bool Settled => Math.Abs(Position - Target) < 0.3 && Math.Abs(Velocity) < 6;

    public void Snap(double value)
    {
        Position = Target = value;
        Velocity = 0;
    }

    public void Step(double dt)
    {
        if (dt <= 0)
        {
            return;
        }

        var x0 = Position - Target;
        var v0 = Velocity;
        var w = _omega;
        double x;
        double v;
        if (_zeta >= 0.9999)
        {
            var e = Math.Exp(-w * dt);
            var k = v0 + (w * x0);
            x = (x0 + (k * dt)) * e;
            v = (v0 - (w * k * dt)) * e;
        }
        else
        {
            var wd = w * Math.Sqrt(1 - (_zeta * _zeta));
            var zw = _zeta * w;
            var e = Math.Exp(-zw * dt);
            var c = (v0 + (zw * x0)) / wd;
            var cos = Math.Cos(wd * dt);
            var sin = Math.Sin(wd * dt);
            x = e * ((x0 * cos) + (c * sin));
            v = e * (((c * wd) - (zw * x0)) * cos - (((x0 * wd) + (zw * c)) * sin));
        }

        Position = Target + x;
        Velocity = v;
        if (Settled)
        {
            Position = Target;
            Velocity = 0;
        }
    }
}

/// <summary>Island palette (always dark) and small themed-free helpers.</summary>
internal static class D
{
    public static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public static SolidColorBrush White(double alpha = 1) => Frozen(Color.FromArgb((byte)Math.Round(alpha * 255), 255, 255, 255));

    public static readonly Brush Text = White();
    public static readonly Brush Secondary = White(0.55);
    public static readonly Brush Tertiary = White(0.40);
    public static readonly Brush Surface = White(0.065);
    public static readonly Brush SurfaceSelected = White(0.12);
    public static readonly Brush Track = White(0.14);
    public static readonly Brush Hover = White(0.09);
    public static readonly Brush Orange = Frozen(Color.FromRgb(0xFF, 0x9F, 0x0A));
    public static readonly Brush Red = Frozen(Color.FromRgb(0xFF, 0x45, 0x3A));
    public static readonly Brush Yellow = Frozen(Color.FromRgb(0xFF, 0xD6, 0x0A));
    public static readonly Brush Mint = Frozen(Color.FromRgb(0x63, 0xE6, 0xE2));
    public static readonly Brush Green = Frozen(Color.FromRgb(0x30, 0xD1, 0x58));
    public static readonly Brush Blue = Frozen(Color.FromRgb(0x0A, 0x84, 0xFF));
    public static readonly Brush Purple = Frozen(Color.FromRgb(0xBF, 0x5A, 0xF2));
    public static readonly Brush CalendarAccent = Frozen(Color.FromRgb(255, 92, 100));
    public static readonly Brush Black = Frozen(Colors.Black);

    public static readonly FontFamily Font = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily Display = new("Segoe UI Variable Display, Segoe UI");
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    public static bool ReduceMotion => !SystemParameters.ClientAreaAnimation;

    public static TextBlock T(string text, double size = 12, FontWeight? weight = null, Brush? brush = null, bool wrap = false, TextTrimming trimming = TextTrimming.None)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontFamily = Font,
            FontWeight = weight ?? FontWeights.Normal,
            Foreground = brush ?? Text,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = trimming,
        };
        // Ideal: Display mode snaps glyphs to whole pixels and blurs under the island's zoom transform
        TextOptions.SetTextFormattingMode(t, TextFormattingMode.Ideal);
        return t;
    }

    /// <summary>Digits keep a fixed width (monospaced digits).</summary>
    public static TextBlock Digits(string text, double size = 12, FontWeight? weight = null, Brush? brush = null)
    {
        var t = T(text, size, weight, brush);
        System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        return t;
    }

    public static TextBlock I(string glyph, double size = 14, Brush? brush = null)
    {
        var t = new TextBlock
        {
            Text = glyph,
            FontFamily = Icons,
            FontSize = size,
            Foreground = brush ?? Text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        TextOptions.SetTextRenderingMode(t, TextRenderingMode.Auto);
        return GlyphBlock.Realize(t);
    }

    public static Brush Tint(Color c, double alpha = 1) => Frozen(Color.FromArgb((byte)(alpha * 255), c.R, c.G, c.B));

    public static void Tip(FrameworkElement e, string text)
    {
        e.ToolTip = IslandWindow.CurrentZoom is var z && Math.Abs(z - 1) > 0.0005
            ? new ToolTip { Content = text, LayoutTransform = new ScaleTransform(z, z) }
            : text;
        System.Windows.Automation.AutomationProperties.SetName(e, text);
    }

    /// <summary>A flat square icon button (28x28, radius 9, glyph 12) — <c>NotchIconButton</c>.</summary>
    public static HoverButton IconButton(string glyph, string title, Action onClick, bool selected = false, double size = 28, double glyphSize = 12, Brush? tint = null)
    {
        var b = new HoverButton(onClick) { Width = size, Height = size, CornerRadius = new CornerRadius(9), Selected = selected };
        var icon = I(glyph, glyphSize, tint ?? (selected ? Text : Secondary));
        b.Child = icon;
        b.Icon = icon;
        b.SelectedForeground = Text;
        b.IdleForeground = tint ?? Secondary;
        Tip(b, title);
        return b;
    }

    /// <summary>A capsule text button (<c>NotchPillButton</c>): height 28, padding 14, 12 pt.</summary>
    public static HoverButton PillButton(string text, Action onClick, bool prominent = false, Brush? foreground = null, Brush? fill = null, double height = 28)
    {
        var label = T(text, 12, prominent ? FontWeights.SemiBold : FontWeights.Medium, foreground ?? (prominent ? Text : White(0.7)));
        label.VerticalAlignment = VerticalAlignment.Center;
        var b = new HoverButton(onClick)
        {
            Height = height,
            CornerRadius = new CornerRadius(height / 2),
            Padding = new Thickness(14, 0, 14, 0),
            Child = label,
            Fill = fill ?? (prominent ? White(0.14) : Brushes.Transparent),
        };
        return b;
    }

    public static FrameworkElement Spacer(double w, double h = 0) => new Border { Width = w, Height = h };

    /// <summary>The empty / unavailable page (<c>NotchEmptyView</c>).</summary>
    public static FrameworkElement EmptyView(string glyph, string message, params UIElement[] actions)
    {
        var tile = new Border
        {
            Width = 56,
            Height = 56,
            CornerRadius = new CornerRadius(18),
            Background = White(0.05),
            Child = I(glyph, 26, White(0.65)),
        };
        if (tile.Child is TextBlock g)
        {
            g.FontWeight = FontWeights.Light;
        }

        var label = T(message, 12, null, Secondary, true);
        label.TextAlignment = TextAlignment.Center;
        label.MaxWidth = 250;
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12) };
        tile.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(tile);
        label.Margin = new Thickness(0, 12, 0, 0);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(label);
        if (actions.Length > 0)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
            foreach (var a in actions)
            {
                row.Children.Add(a);
            }

            stack.Children.Add(row);
        }

        return stack;
    }

    /// <summary>Capsule track + fill (<c>NotchMeter</c>), height 5.</summary>
    public static Meter MeterBar(double value, Brush? tint = null, double height = 5) => new(value, tint, height);

    public static string Clock(TimeSpan t, bool compact = false)
    {
        var total = (int)Math.Max(0, Math.Floor(t.TotalSeconds));
        var h = total / 3600;
        var m = total % 3600 / 60;
        var s = total % 60;
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m:00}:{s:00}";
    }

    /// <summary>Track time <c>m:ss</c> or <c>h:mm:ss</c>.</summary>
    public static string TrackTime(double seconds)
    {
        var total = (int)Math.Clamp(Math.Floor(seconds), 0, 604800);
        var h = total / 3600;
        var m = total % 3600 / 60;
        var s = total % 60;
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
    }

    public static Color Lerp(Color a, Color b, double t) => Color.FromArgb(
        (byte)(a.A + ((b.A - a.A) * t)), (byte)(a.R + ((b.R - a.R) * t)), (byte)(a.G + ((b.G - a.G) * t)), (byte)(a.B + ((b.B - a.B) * t)));

    public static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        while (start is not null)
        {
            if (start is T t)
            {
                return t;
            }

            start = start is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(start) : LogicalTreeHelper.GetParent(start);
        }

        return null;
    }
}

/// <summary>Flat button with the island hover wash (9 %), press scale and optional selected plate — <c>NotchButtonStyle</c>.</summary>
internal sealed class HoverButton : Border
{
    private readonly Action _onClick;
    private bool _hover;
    private bool _down;
    private bool _selected;
    private Brush? _fill;

    public HoverButton(Action onClick)
    {
        _onClick = onClick;
        Background = Brushes.Transparent;
        Cursor = Cursors.Arrow;
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = new ScaleTransform(1, 1);
        Focusable = false;
        SnapsToDevicePixels = true;
    }

    public TextBlock? Icon { get; set; }

    public Brush? SelectedForeground { get; set; }

    public Brush? IdleForeground { get; set; }

    /// <summary>Whether the hover wash lifts the button (scale 1.022).</summary>
    public bool Lifts { get; set; } = true;

    /// <summary>The fill while the pointer is over the button (null = the usual hover wash).</summary>
    public Brush? HoverFill { get; set; }

    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            Refresh();
        }
    }

    public Brush? Fill
    {
        get => _fill;
        set
        {
            _fill = value;
            Refresh();
        }
    }

    public new bool IsEnabled
    {
        get => base.IsEnabled;
        set
        {
            base.IsEnabled = value;
            Opacity = value ? 1 : 0.4;
        }
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        _hover = true;
        Refresh();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hover = false;
        _down = false;
        Refresh();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _down = true;
        CaptureMouse();
        Refresh();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        var fire = _down && IsMouseOver;
        _down = false;
        ReleaseMouseCapture();
        Refresh();
        e.Handled = true;
        if (fire && IsEnabled)
        {
            _onClick();
        }
    }

    private void Refresh()
    {
        Brush? fill = _selected ? D.SurfaceSelected : (_hover && IsEnabled ? (HoverFill ?? D.Hover) : _fill);
        if (_selected && _hover)
        {
            fill = D.White(0.16);
        }

        Background = fill ?? Brushes.Transparent;
        if (Icon is not null)
        {
            Icon.Foreground = _selected || _hover ? (SelectedForeground ?? D.Text) : (IdleForeground ?? D.Secondary);
        }

        if (RenderTransform is ScaleTransform s)
        {
            var scale = _down ? 0.965 : (_hover && Lifts ? 1.022 : 1);
            s.ScaleX = s.ScaleY = scale;
        }

        Opacity = IsEnabled ? (_down ? 0.8 : 1) : 0.4;
    }
}

/// <summary>Read-only capsule meter (<c>NotchMeter</c>).</summary>
internal sealed class Meter : FrameworkElement
{
    private double _value;
    private Brush _tint;

    public Meter(double value, Brush? tint, double height = 5)
    {
        _value = Math.Clamp(value, 0, 1);
        _tint = tint ?? D.White(0.9);
        Height = height;
        MinWidth = 24;
        IsHitTestVisible = false;
    }

    public double Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0, 1);
            InvalidateVisual();
        }
    }

    public Brush Tint
    {
        get => _tint;
        set
        {
            _tint = value;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var r = Height / 2;
        dc.DrawRoundedRectangle(D.Track, null, new Rect(0, 0, ActualWidth, Height), r, r);
        if (_value > 0)
        {
            var w = Math.Max(Height, ActualWidth * _value);
            dc.DrawRoundedRectangle(_tint, null, new Rect(0, 0, Math.Min(w, ActualWidth), Height), r, r);
        }
    }
}

/// <summary>
/// Horizontal or vertical level slider (<c>NotchLevelSlider</c>): no knob, capsule track, fill from the left/bottom, optional marker.
/// </summary>
internal sealed class LevelSlider : FrameworkElement
{
    private readonly bool _vertical;
    private double _value;
    private double _drawn;
    private bool _dragging;
    private DispatcherTimerLite? _ease;

    public LevelSlider(bool vertical, double min = 0, double max = 1, double thickness = 8, Brush? tint = null)
    {
        _vertical = vertical;
        Minimum = min;
        Maximum = max;
        Thickness = thickness;
        Tint = tint ?? D.White(0.92);
        Cursor = Cursors.Arrow;
        Focusable = false;
        Background = Brushes.Transparent;
        if (vertical)
        {
            Width = 40;
        }
        else
        {
            Height = 24;
        }
    }

    public double Minimum { get; }

    public double Maximum { get; }

    public double Thickness { get; set; }

    public Brush Tint { get; set; }

    public Brush? Background { get; set; }

    /// <summary>Optional marker position as a value (drawn as a 1 pt line).</summary>
    public double? Marker { get; set; }

    public bool Scrollable { get; set; } = true;

    public bool EasesChanges { get; set; }

    public double WheelStep { get; set; } = 0.05;

    public bool IsDragging => _dragging;

    public event Action<double>? ValueChanged;

    public event Action<double>? ValueCommitted;

    public double Value
    {
        get => _value;
        set => SetFromOutside(value);
    }

    private double Normalised(double v) => Maximum <= Minimum ? 0 : Math.Clamp((v - Minimum) / (Maximum - Minimum), 0, 1);

    private void SetFromOutside(double v)
    {
        v = Math.Clamp(v, Minimum, Maximum);
        if (Math.Abs(v - _value) < 1e-9)
        {
            return;
        }

        var from = _drawn;
        _value = v;
        if (_dragging)
        {
            _drawn = Normalised(v);
            InvalidateVisual();
            return;
        }

        if (EasesChanges && IsVisible && !D.ReduceMotion && Math.Abs(Normalised(v) - from) > 0.002)
        {
            _ease?.Stop();
            var start = DateTime.UtcNow;
            var target = Normalised(v);
            _ease = new DispatcherTimerLite(16, () =>
            {
                var p = Math.Clamp((DateTime.UtcNow - start).TotalSeconds / 0.22, 0, 1);
                var eased = 1 - Math.Pow(1 - p, 3);
                _drawn = from + ((target - from) * eased);
                InvalidateVisual();
                if (p >= 1)
                {
                    _ease?.Stop();
                }
            });
            _ease.Start();
        }
        else
        {
            _drawn = Normalised(v);
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        var t = Thickness;
        if (_vertical)
        {
            var x = (w - t) / 2;
            dc.DrawRoundedRectangle(D.Track, null, new Rect(x, 0, t, h), t / 2, t / 2);
            var fill = Math.Max(_drawn > 0 ? t : 0, h * _drawn);
            if (fill > 0)
            {
                dc.DrawRoundedRectangle(Tint, null, new Rect(x, h - fill, t, fill), t / 2, t / 2);
            }

            if (Marker is { } m)
            {
                var y = h - (h * Normalised(m));
                dc.DrawRectangle(D.White(0.45), null, new Rect(x, y - 0.5, t, 1));
            }
        }
        else
        {
            var y = (h - t) / 2;
            dc.DrawRoundedRectangle(D.Track, null, new Rect(0, y, w, t), t / 2, t / 2);
            var fill = Math.Max(_drawn > 0 ? t : 0, w * _drawn);
            if (fill > 0)
            {
                dc.DrawRoundedRectangle(Tint, null, new Rect(0, y, Math.Min(fill, w), t), t / 2, t / 2);
            }

            if (Marker is { } m)
            {
                var x = w * Normalised(m);
                dc.DrawRectangle(D.White(0.45), null, new Rect(x - 0.5, y, 1, t));
            }
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _dragging = true;
        CaptureMouse();
        Apply(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging)
        {
            Apply(e.GetPosition(this));
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            ReleaseMouseCapture();
            ValueCommitted?.Invoke(_value);
        }

        e.Handled = true;
    }

    /// <summary>Capture was taken away mid-drag (another window, a menu, Alt+Tab): the drag ends here, as if the button had been released.</summary>
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_dragging)
        {
            _dragging = false;
            ValueCommitted?.Invoke(_value);
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (!Scrollable)
        {
            return;
        }

        // a mouse notch (120) moves one WheelStep; a touchpad sends many small deltas that add up to the same distance
        var step = (Maximum - Minimum) * WheelStep * (e.Delta / 120.0);
        var v = Math.Clamp(_value + step, Minimum, Maximum);
        _value = v;
        _drawn = Normalised(v);
        InvalidateVisual();
        ValueChanged?.Invoke(v);
        ValueCommitted?.Invoke(v);
        e.Handled = true;
    }

    private void Apply(Point p)
    {
        var f = _vertical ? 1 - (p.Y / Math.Max(1, ActualHeight)) : p.X / Math.Max(1, ActualWidth);
        f = Math.Clamp(f, 0, 1);
        var v = Minimum + ((Maximum - Minimum) * f);
        _value = v;
        _drawn = f;
        InvalidateVisual();
        ValueChanged?.Invoke(v);
    }
}

/// <summary>Tiny wrapper over <see cref="System.Windows.Threading.DispatcherTimer"/> with an interval in ms and an action.</summary>
internal sealed class DispatcherTimerLite
{
    private readonly System.Windows.Threading.DispatcherTimer _t;

    public DispatcherTimerLite(double ms, Action tick, System.Windows.Threading.DispatcherPriority priority = System.Windows.Threading.DispatcherPriority.Normal)
    {
        _t = new System.Windows.Threading.DispatcherTimer(priority) { Interval = TimeSpan.FromMilliseconds(ms) };
        _t.Tick += (_, _) => tick();
    }

    public bool IsEnabled => _t.IsEnabled;

    public void Start() => _t.Start();

    public void Stop() => _t.Stop();

    public void SetInterval(double ms) => _t.Interval = TimeSpan.FromMilliseconds(ms);
}

/// <summary>A pop-up menu styled for the dark island (checked items, separators, destructive items).</summary>
internal static class IslandMenu
{
    public sealed record Item(string Title, Action? OnClick, bool Checked = false, bool Enabled = true, string? Glyph = null, bool Separator = false, bool Destructive = false);

    public static Item Sep() => new("", null, Separator: true);

    /// <summary>Shows the menu below <paramref name="anchor"/>; <paramref name="hold"/> is called to keep the island open while it is shown.</summary>
    public static void Show(FrameworkElement anchor, IEnumerable<Item> items, Func<IDisposable?>? hold = null, bool above = false)
    {
        var holdToken = hold?.Invoke();
        var menu = new ContextMenu
        {
            Placement = above ? System.Windows.Controls.Primitives.PlacementMode.Top : System.Windows.Controls.Primitives.PlacementMode.Bottom,
            PlacementTarget = anchor,
            StaysOpen = false,
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1E)),
            BorderBrush = D.White(0.16),
            BorderThickness = new Thickness(1),
            Foreground = Brushes.White,
            Padding = new Thickness(4),
            FontFamily = D.Font,
            FontSize = 12,
            HasDropShadow = false,
        };
        // a menu is its own window and does not inherit the island's zoom
        if ((anchor is null ? null : Window.GetWindow(anchor)) is IslandWindow owner && Math.Abs(owner.Zoom - 1) > 0.0005)
        {
            menu.LayoutTransform = new ScaleTransform(owner.Zoom, owner.Zoom);
        }

        foreach (var item in items)
        {
            if (item.Separator)
            {
                menu.Items.Add(new Separator { Background = D.White(0.12), Margin = new Thickness(4, 3, 4, 3) });
                continue;
            }

            var grid = new Grid { MinWidth = 150 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (item.Checked)
            {
                grid.Children.Add(D.I(G.Check, 10, Brushes.White));
            }

            var label = D.T(item.Title, 12, null, item.Destructive ? D.Red : (item.Enabled ? Brushes.White : D.White(0.4)));
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);
            var mi = new MenuItem
            {
                Header = grid,
                IsEnabled = item.Enabled,
                Template = ItemTemplate(),
                Padding = new Thickness(6, 4, 6, 4),
            };
            var click = item.OnClick;
            mi.Click += (_, _) => click?.Invoke();
            menu.Items.Add(mi);
        }

        menu.Closed += (_, _) => holdToken?.Dispose();
        menu.IsOpen = true;
    }

    private static ControlTemplate ItemTemplate()
    {
        var xaml = "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TargetType=\"MenuItem\">"
            + "<Border x:Name=\"b\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" CornerRadius=\"6\" Background=\"Transparent\" Padding=\"{TemplateBinding Padding}\">"
            + "<ContentPresenter ContentSource=\"Header\"/></Border>"
            + "<ControlTemplate.Triggers><Trigger Property=\"IsHighlighted\" Value=\"True\"><Setter TargetName=\"b\" Property=\"Background\" Value=\"#1FFFFFFF\"/></Trigger></ControlTemplate.Triggers>"
            + "</ControlTemplate>";
        return (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
    }
}

internal static class Detach
{
    /// <summary>Takes an element out of its current parent so it can be placed in a rebuilt layer.</summary>
    public static T? From<T>(T? element) where T : FrameworkElement
    {
        switch (element?.Parent)
        {
            case Panel p:
                p.Children.Remove(element);
                break;
            case Decorator d:
                d.Child = null;
                break;
            case ContentControl c:
                c.Content = null;
                break;
        }

        return element;
    }
}
