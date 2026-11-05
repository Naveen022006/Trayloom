// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Trayloom.Core.Ui;

/// <summary>Direction a <see cref="SmoothScrollViewer"/> or <see cref="SmoothScroll"/> engine scrolls in.</summary>
public enum ScrollAxis
{
    /// <summary>Up and down (the wheel moves the vertical offset).</summary>
    Vertical,

    /// <summary>Left and right (the wheel moves the horizontal offset, as on a rail of cards).</summary>
    Horizontal,
}

/// <summary>
/// The one smooth-scroll engine of the app. Wheel notches become an accumulating target that the offset follows with a
/// critically damped spring (about 150 ms, frame-rate independent, driven from <see cref="CompositionTarget.Rendering"/>);
/// precision-touchpad deltas (anything that is not a whole 120 notch) move the content 1:1 with no animation; the offset never
/// overshoots the ends; a gesture that began inside keeps its momentum instead of leaking to a parent list.
/// Use <see cref="SmoothScrollViewer"/> for new lists (it adds a thin overlay scroll bar), or <see cref="Enable(ScrollViewer, ScrollAxis)"/> to give an
/// existing <see cref="ScrollViewer"/> the same motion.
/// </summary>
public static class SmoothScroll
{
    private static readonly DependencyProperty EngineProperty = DependencyProperty.RegisterAttached("Engine", typeof(SmoothScroller), typeof(SmoothScroll));

    /// <summary>True when the user asked Windows to cut animations (Settings, Accessibility, Visual effects).</summary>
    public static bool ReduceMotion => !SystemParameters.ClientAreaAnimation;

    /// <summary>Gives <paramref name="viewer"/> smooth wheel scrolling along <paramref name="axis"/>.</summary>
    public static void Enable(ScrollViewer viewer, ScrollAxis axis = ScrollAxis.Vertical)
    {
        Enable(viewer, axis, tunnel: true);
    }

    /// <summary>
    /// <paramref name="tunnel"/> true hooks the preview wheel (right for a stock viewer: nothing inside it wants the wheel);
    /// false leaves the hooking to the caller, which forwards bubbled wheel events to <see cref="EngineOf"/> so sliders inside win.
    /// </summary>
    internal static SmoothScroller Enable(ScrollViewer viewer, ScrollAxis axis, bool tunnel)
    {
        if (viewer.GetValue(EngineProperty) is SmoothScroller existing)
        {
            existing.Axis = axis;
            return existing;
        }

        var engine = new SmoothScroller(viewer, axis);
        viewer.SetValue(EngineProperty, engine);
        if (tunnel)
        {
            viewer.PreviewMouseWheel += (_, e) => engine.OnWheel(e);
        }

        return engine;
    }

    /// <summary>The engine behind <paramref name="viewer"/> (null when <see cref="Enable(ScrollViewer, ScrollAxis)"/> was never called).</summary>
    internal static SmoothScroller? EngineOf(ScrollViewer viewer) => viewer.GetValue(EngineProperty) as SmoothScroller;

    /// <summary>Scrolls to <paramref name="offset"/> along the viewer's axis, gliding there (jumping when motion is reduced).</summary>
    public static void ScrollTo(ScrollViewer viewer, double offset, bool animate = true)
    {
        var engine = EngineOf(viewer);
        if (engine is null)
        {
            if (viewer.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled)
            {
                viewer.ScrollToHorizontalOffset(offset);
            }
            else
            {
                viewer.ScrollToVerticalOffset(offset);
            }

            return;
        }

        engine.GlideTo(offset, animate);
    }

    /// <summary>Scrolls by <paramref name="delta"/> device-independent pixels from where the viewer is heading.</summary>
    public static void ScrollBy(ScrollViewer viewer, double delta, bool animate = true)
    {
        var engine = EngineOf(viewer);
        if (engine is null)
        {
            ScrollTo(viewer, (viewer.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled ? viewer.HorizontalOffset : viewer.VerticalOffset) + delta, animate);
            return;
        }

        engine.GlideTo(engine.Heading + delta, animate);
    }

    /// <summary>True while an animated scroll is in flight.</summary>
    public static bool IsAnimating(ScrollViewer viewer) => EngineOf(viewer)?.Active ?? false;
}

/// <summary>Per-viewer state of the smooth-scroll engine.</summary>
internal sealed class SmoothScroller
{
    /// <summary>Spring stiffness (rad/s): a notch is 90 % done after ~120 ms and settled after ~200 ms.</summary>
    private const double Omega = 30;

    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    private readonly ScrollViewer _viewer;
    private double _pos;
    private double _target;
    private double _vel;
    private double _applied;
    private double _last;
    private double _ownedUntil;
    private bool _active;
    private bool _pushed;
    private double _ppd = 1;

    public SmoothScroller(ScrollViewer viewer, ScrollAxis axis)
    {
        _viewer = viewer;
        Axis = axis;
        // ScrollViewer reports a new offset one layout pass after it was set: until then our own last value is the truth
        viewer.ScrollChanged += (_, _) => _pushed = false;
    }

    public ScrollAxis Axis { get; set; }

    public bool Active => _active;

    /// <summary>Pixels one wheel notch (120) moves: the Windows "lines per notch" setting.</summary>
    public double NotchPixels
    {
        get
        {
            var lines = SystemParameters.WheelScrollLines;
            if (lines < 1)
            {
                return Math.Max(64, Viewport * 0.9);
            }

            return Math.Clamp(lines * 16, 32, 160);
        }
    }

    /// <summary>Where the offset is heading (its target while animating, else where it is).</summary>
    public double Heading => _active ? _target : Current;

    private double Offset => Axis == ScrollAxis.Vertical ? _viewer.VerticalOffset : _viewer.HorizontalOffset;

    private double Current => _pushed ? _applied : Offset;

    private double Max => Axis == ScrollAxis.Vertical ? _viewer.ScrollableHeight : _viewer.ScrollableWidth;

    private double Viewport => Axis == ScrollAxis.Vertical ? _viewer.ViewportHeight : _viewer.ViewportWidth;

    private static double Now => Clock.Elapsed.TotalSeconds;

    /// <summary>Handles one wheel event; marks it handled when this viewer consumed it.</summary>
    public void OnWheel(MouseWheelEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        var max = Max;
        if (max <= 0.5 || e.Delta == 0)
        {
            return;
        }

        var cur = Current;
        if (!_active)
        {
            _pos = _target = cur;
            _vel = 0;
        }

        var notches = e.Delta / 120.0;
        var px = -notches * NotchPixels;
        var precise = Math.Abs(e.Delta) < 120 || e.Delta % 120 != 0 || SmoothScroll.ReduceMotion;
        var basis = precise ? cur : _target;
        var movable = px > 0 ? basis < max - 0.5 : basis > 0.5;
        var now = Now;
        if (!movable && now > _ownedUntil)
        {
            // at the end already and the gesture did not start here: let a parent list have it
            return;
        }

        _ownedUntil = now + 0.35;
        e.Handled = true;
        if (!movable)
        {
            return;
        }

        if (precise)
        {
            // touchpad and reduced motion: the content follows the fingers 1:1
            Stop();
            Apply(Math.Clamp(cur + px, 0, max));
            return;
        }

        _target = Math.Clamp(_target + px, 0, max);
        Start();
    }

    /// <summary>Moves to <paramref name="offset"/> (clamped), gliding unless motion is reduced.</summary>
    public void GlideTo(double offset, bool animate)
    {
        var max = Max;
        var target = Math.Clamp(offset, 0, Math.Max(0, max));
        if (!animate || SmoothScroll.ReduceMotion || Viewport <= 0)
        {
            Stop();
            Apply(target);
            return;
        }

        if (!_active)
        {
            _pos = Current;
            _vel = 0;
            _applied = _pos;
        }

        _target = target;
        Start();
    }

    private void Start()
    {
        if (_active)
        {
            return;
        }

        _active = true;
        _ppd = DevicePixelsPerDip();
        _last = Now;
        _applied = _pos;
        CompositionTarget.Rendering += OnFrame;
    }

    private void Stop()
    {
        if (!_active)
        {
            return;
        }

        _active = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (!_viewer.IsLoaded)
        {
            Stop();
            return;
        }

        var now = Now;
        var dt = Math.Clamp(now - _last, 0, 0.05);
        _last = now;
        var max = Max;
        if (!_pushed && Math.Abs(Offset - _applied) > 0.9)
        {
            // someone else (a scroll bar drag, a programmatic jump) moved the content: do not fight it
            Stop();
            return;
        }

        _target = Math.Clamp(_target, 0, Math.Max(0, max));
        var x = _pos - _target;
        var decay = Math.Exp(-Omega * dt);
        var k = _vel + (Omega * x);
        _pos = _target + ((x + (k * dt)) * decay);
        _vel = (_vel - (Omega * k * dt)) * decay;
        if (Math.Abs(_pos - _target) < 0.2 && Math.Abs(_vel) < 3)
        {
            Stop();
            Apply(_target);
            return;
        }

        _applied = Snap(Math.Clamp(_pos, 0, Math.Max(0, max)));
        Push(_applied);
    }

    /// <summary>Device pixels per DIP as drawn: the monitor DPI times any scale above the viewer (the island zoom).</summary>
    private double DevicePixelsPerDip()
    {
        var dpi = VisualTreeHelper.GetDpi(_viewer).PixelsPerDip;
        if (PresentationSource.FromVisual(_viewer)?.RootVisual is not Visual root)
        {
            return dpi;
        }

        try
        {
            var transform = _viewer.TransformToAncestor(root);
            var origin = transform.Transform(new Point(0, 0));
            var probe = transform.Transform(Axis == ScrollAxis.Vertical ? new Point(0, 100) : new Point(100, 0));
            var scale = (Axis == ScrollAxis.Vertical ? Math.Abs(probe.Y - origin.Y) : Math.Abs(probe.X - origin.X)) / 100;
            return scale > 0.01 ? dpi * scale : dpi;
        }
        catch (InvalidOperationException)
        {
            // the viewer is not connected to the root (being torn down): the monitor DPI alone is the best answer
            return dpi;
        }
    }

    /// <summary>Rounds an offset to a whole device pixel at the current scale, so the content never rests between pixels.</summary>
    private double Snap(double value) => Math.Round(value * _ppd) / _ppd;

    private void Apply(double offset)
    {
        // land on whole device pixels (zoom included) so text is crisp at rest
        _ppd = DevicePixelsPerDip();
        offset = Snap(offset);
        _pos = _applied = offset;
        Push(offset);
    }

    private void Push(double offset)
    {
        _pushed = true;
        if (Axis == ScrollAxis.Vertical)
        {
            _viewer.ScrollToVerticalOffset(offset);
        }
        else
        {
            _viewer.ScrollToHorizontalOffset(offset);
        }
    }
}

/// <summary>
/// A <see cref="ScrollViewer"/> that glides: smooth wheel stepping, 1:1 touchpad, no bars taking space, and a thin overlay
/// thumb that fades in while scrolling (draggable). Scrolls whole device pixels (<c>CanContentScroll = false</c>).
/// </summary>
public class SmoothScrollViewer : ScrollViewer
{
    private static ControlTemplate? _template;

    private readonly DispatcherTimer _hide;
    private Border? _thumb;
    private Grid? _overlay;
    private bool _dragging;
    private double _dragGrab;
    private ScrollAxis _axis = ScrollAxis.Vertical;
    private readonly SmoothScroller _engine;

    /// <summary>Creates a vertical viewer (bars Auto → overlay thumb).</summary>
    public SmoothScrollViewer()
    {
        Template = _template ??= BuildTemplate();
        CanContentScroll = false;
        Focusable = false;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _engine = SmoothScroll.Enable(this, ScrollAxis.Vertical, tunnel: false);
        _hide = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(900) };
        _hide.Tick += (_, _) =>
        {
            _hide.Stop();
            if (!_dragging)
            {
                FadeThumb(0, 320);
            }
        };
        ScrollChanged += (_, e) => OnScrolled(e);
        SizeChanged += (_, _) => PlaceThumb();
        Unloaded += (_, _) => _hide.Stop();
    }

    /// <summary>A vertical viewer around <paramref name="content"/>.</summary>
    public static SmoothScrollViewer Vertical(UIElement content) => new() { Content = content };

    /// <summary>A horizontal rail around <paramref name="content"/> (the vertical wheel scrolls sideways).</summary>
    public static SmoothScrollViewer Horizontal(UIElement content) => new() { Content = content, ScrollOrientation = ScrollAxis.Horizontal };

    /// <summary>Which way the viewer scrolls; <see cref="ScrollAxis.Horizontal"/> turns the vertical wheel into sideways movement.</summary>
    public ScrollAxis ScrollOrientation
    {
        get => _axis;
        set
        {
            _axis = value;
            if (value == ScrollAxis.Horizontal)
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                if (HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled)
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                }
            }
            else
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                if (VerticalScrollBarVisibility == ScrollBarVisibility.Disabled)
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                }
            }

            _engine.Axis = value;
        }
    }

    /// <summary>Where the offset is heading: the target of the glide in flight, else the current offset.</summary>
    public double ScrollTarget => _engine.Heading;

    /// <summary>Glides to an offset along the viewer's axis.</summary>
    public void SmoothScrollTo(double offset) => SmoothScroll.ScrollTo(this, offset);

    /// <summary>Glides by a distance from where the viewer is heading.</summary>
    public void SmoothScrollBy(double delta) => SmoothScroll.ScrollBy(this, delta);

    /// <summary>The wheel bubbled up unhandled (no slider or text box inside took it): glide instead of the stock line stepping.</summary>
    protected override void OnMouseWheel(MouseWheelEventArgs e) => _engine.OnWheel(e);

    private bool ShowsThumb => _axis == ScrollAxis.Vertical
        ? VerticalScrollBarVisibility is ScrollBarVisibility.Auto or ScrollBarVisibility.Visible
        : HorizontalScrollBarVisibility is ScrollBarVisibility.Auto or ScrollBarVisibility.Visible;

    private static ControlTemplate BuildTemplate() => (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
        "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"ScrollViewer\">"
        + "<Grid Background=\"{TemplateBinding Background}\">"
        + "<ScrollContentPresenter x:Name=\"PART_ScrollContentPresenter\" CanContentScroll=\"{TemplateBinding CanContentScroll}\" Margin=\"{TemplateBinding Padding}\"/>"
        + "<Grid x:Name=\"PART_Overlay\"/>"
        + "</Grid></ControlTemplate>");

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _overlay = GetTemplateChild("PART_Overlay") as Grid;
        if (_overlay is null)
        {
            return;
        }

        _thumb = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF)),
            CornerRadius = new CornerRadius(2),
            Opacity = 0,
            Visibility = Visibility.Collapsed,
            Cursor = Cursors.Arrow,
        };
        _thumb.MouseLeftButtonDown += OnThumbDown;
        _thumb.MouseMove += OnThumbMove;
        _thumb.MouseLeftButtonUp += OnThumbUp;
        _thumb.MouseEnter += (_, _) =>
        {
            _hide.Stop();
            FadeThumb(0.9, 90);
        };
        _thumb.MouseLeave += (_, _) =>
        {
            if (!_dragging)
            {
                _hide.Stop();
                _hide.Start();
            }
        };
        _overlay.Children.Add(_thumb);
        PlaceThumb();
    }

    private void OnScrolled(ScrollChangedEventArgs e)
    {
        PlaceThumb();
        if (_thumb is null || !ShowsThumb || (e.VerticalChange == 0 && e.HorizontalChange == 0))
        {
            return;
        }

        FadeThumb(0.55, 90);
        _hide.Stop();
        _hide.Start();
    }

    private void PlaceThumb()
    {
        if (_thumb is null)
        {
            return;
        }

        var vertical = _axis == ScrollAxis.Vertical;
        var extent = vertical ? ExtentHeight : ExtentWidth;
        var viewport = vertical ? ViewportHeight : ViewportWidth;
        var offset = vertical ? VerticalOffset : HorizontalOffset;
        var track = vertical ? ActualHeight : ActualWidth;
        if (!ShowsThumb || extent <= viewport + 0.5 || track < 24)
        {
            _thumb.Visibility = Visibility.Collapsed;
            return;
        }

        const double inset = 3;
        var room = Math.Max(1, track - (2 * inset));
        var len = Math.Clamp(viewport / extent * room, 28, room);
        var travel = room - len;
        var pos = inset + (travel * (offset / Math.Max(1, extent - viewport)));
        _thumb.Visibility = Visibility.Visible;
        if (vertical)
        {
            _thumb.Width = 3;
            _thumb.Height = len;
            _thumb.HorizontalAlignment = HorizontalAlignment.Right;
            _thumb.VerticalAlignment = VerticalAlignment.Top;
            _thumb.Margin = new Thickness(0, pos, 2, 0);
        }
        else
        {
            _thumb.Height = 3;
            _thumb.Width = len;
            _thumb.VerticalAlignment = VerticalAlignment.Bottom;
            _thumb.HorizontalAlignment = HorizontalAlignment.Left;
            _thumb.Margin = new Thickness(pos, 0, 0, 2);
        }
    }

    private void FadeThumb(double to, double ms)
    {
        if (_thumb is null)
        {
            return;
        }

        if (SmoothScroll.ReduceMotion)
        {
            _thumb.BeginAnimation(OpacityProperty, null);
            _thumb.Opacity = to;
            return;
        }

        _thumb.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void OnThumbDown(object sender, MouseButtonEventArgs e)
    {
        if (_thumb is null)
        {
            return;
        }

        _dragging = true;
        _hide.Stop();
        _thumb.CaptureMouse();
        var p = e.GetPosition(_thumb);
        _dragGrab = _axis == ScrollAxis.Vertical ? p.Y : p.X;
        FadeThumb(0.9, 60);
        e.Handled = true;
    }

    private void OnThumbMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || _thumb is null)
        {
            return;
        }

        var vertical = _axis == ScrollAxis.Vertical;
        var p = e.GetPosition(_overlay);
        var len = vertical ? _thumb.Height : _thumb.Width;
        var track = vertical ? ActualHeight : ActualWidth;
        var travel = Math.Max(1, track - 6 - len);
        var frac = Math.Clamp(((vertical ? p.Y : p.X) - _dragGrab - 3) / travel, 0, 1);
        var scrollable = vertical ? ScrollableHeight : ScrollableWidth;
        if (vertical)
        {
            ScrollToVerticalOffset(frac * scrollable);
        }
        else
        {
            ScrollToHorizontalOffset(frac * scrollable);
        }
    }

    private void OnThumbUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging || _thumb is null)
        {
            return;
        }

        _dragging = false;
        _thumb.ReleaseMouseCapture();
        _hide.Stop();
        _hide.Start();
        e.Handled = true;
    }
}
