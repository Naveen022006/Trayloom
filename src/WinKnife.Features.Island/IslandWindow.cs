// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Island;

internal enum IslandMode
{
    Closed,
    Peek,
    Open,
}

/// <summary>
/// The Dynamic Island window: transparent topmost no-activate overlay with an animated black silhouette, a closed layer
/// (resting, notices, activity strips) and an open layer (top row + page / Explore) — spec Part B.
/// </summary>
internal sealed partial class IslandWindow : OverlayWindow
{
    internal const double BarHeight = 24;
    internal const double CameraWidth = 180 * BarHeight / 32;
    internal const double SideGutter = 72;
    internal const double HeaderRow = 36;

    private readonly IslandCore _core;
    private readonly Canvas _stage = new() { ClipToBounds = false };
    private readonly IslandSurface _surface = new();
    private readonly Canvas _clipLayer = new() { ClipToBounds = false };
    private readonly Grid _closedHost = new() { ClipToBounds = false };
    private readonly Grid _openHost = new() { ClipToBounds = false, Opacity = 0, IsHitTestVisible = false };
    private readonly Canvas _quick = new();
    private readonly Spring _sw = new(0.44, 0.25, 109);
    private readonly Spring _sh = new(0.38, 0.22, 24);
    private readonly ScaleTransform _openScale = new(1, 1);
    private TimeSpan _lastRender;
    private bool _firstFrame;
    private bool _rendering;
    private bool? _capsuleCache;
    private bool? _outlineCache;
    private bool? _translucentCache;

    private MonitorArea _area;

    /// <summary>The monitor's DPI scale (physical pixels per DIP); <see cref="_scale"/> adds the island zoom on top.</summary>
    private double _dpi = 1;

    /// <summary>The uniform island zoom (setting <see cref="ZoomKey"/>): everything inside the window is drawn this much larger.</summary>
    private double _zoom = DefaultZoom;

    /// <summary>Physical pixels per island unit: DPI times zoom. All screen-rect math of the island uses this.</summary>
    private double _scale => _dpi * _zoom;

    /// <summary>The setting that scales the whole island (length and breadth) — clamped to 0.9 to 1.3.</summary>
    internal const string ZoomKey = "notchZoom";

    internal const double DefaultZoom = 1.10;

    /// <summary>The zoom the live island uses (read by pop-up menus and tips, which are separate windows).</summary>
    internal static double CurrentZoom { get; private set; } = 1;

    /// <summary>Grow-only width of the window in island units: sized for the widest state (both gutters, hover emphasis, swing) so x never moves.</summary>
    private double _envW;

    /// <summary>Grow-only height of the window in island units (see <see cref="ComputeEnvelope"/>).</summary>
    private double _envH;

    private (int X, int Y, int W, int H) _lastBounds;
    private double _winW = 160;
    private double _winH = 60;
    private double _closedW = 109;
    private double _closedH = 24;
    private double _openW = 560;
    private double _openH = 300;
    private bool _shrinkPending;

    private IslandMode _mode = IslandMode.Closed;
    private bool _suppressed;
    private bool _disposed;
    private int _holds;
    private bool _pinned;
    private bool _openedByHover;
    private bool _keyFocus;
    private bool _exploring;
    private string _module = "controls";
    private IslandPage? _page;
    private DateTime _lastChange = DateTime.MinValue;

    public IslandWindow(IslandCore core)
    {
        _core = core;
        Title = Loc.T("WinKnife Island");
        Content = _stage;
        UseLayoutRounding = false;
        SnapsToDevicePixels = false;
        _stage.Children.Add(_surface);
        // both content layers share one clip: the live silhouette, so content never shows outside the growing shape
        _clipLayer.Children.Add(_closedHost);
        _clipLayer.Children.Add(_openHost);
        _stage.Children.Add(_clipLayer);
        _stage.Children.Add(_quick);
        _openHost.RenderTransformOrigin = new Point(0.5, 0);
        ApplyZoom();
        AllowDrop = true;
        InitInput();
        core.Window = this;
    }

    public bool Expanded => _mode == IslandMode.Open;

    public bool Peeking => _mode == IslandMode.Peek;

    public bool Pinned => _pinned;

    public bool Suppressed => _suppressed;

    public bool CaptureUp { get; set; }

    public string CurrentModule => _module;

    public bool Exploring => _exploring;

    public bool IsCapsule => _capsuleCache ??= _core.App.Get<string>(K.Silhouette) != "notch";

    private bool OutlineOn => _outlineCache ??= _core.App.Get<bool>(K.Outline);

    private bool TranslucentOn => _translucentCache ??= _core.App.Get<bool>(K.Translucent);

    /// <summary>Settings that shape the silhouette are read once, not on every animation frame.</summary>
    private void InvalidateStyleCache()
    {
        _capsuleCache = null;
        _outlineCache = null;
        _translucentCache = null;
    }

    internal MonitorArea Area => _area;

    internal double Scale => _scale;

    internal double Zoom => _zoom;

    private double ReadZoom()
    {
        var z = _core.App.Get(ZoomKey, DefaultZoom);
        return double.IsNaN(z) || double.IsInfinity(z) ? DefaultZoom : Math.Clamp(z, 0.9, 1.3);
    }

    /// <summary>Reads the zoom setting and scales the whole stage (layout transform: text stays sharp, hit tests follow).</summary>
    private void ApplyZoom()
    {
        _zoom = ReadZoom();
        CurrentZoom = _zoom;
        _stage.LayoutTransform = Math.Abs(_zoom - 1) < 0.0005 ? Transform.Identity : new ScaleTransform(_zoom, _zoom);
    }

    // ---------------------------------------------------------------- geometry helpers

    private double BodyHeight => IsCapsule ? Math.Clamp(20 + _core.App.Get<double>(K.CapsuleFitHeight), 16, 24) : BarHeight;

    private double Gap => IsCapsule ? 2 : 0;

    private double DropOffset => IsCapsule ? Math.Clamp(_core.App.Get<double>(K.CapsuleFitDrop), 0, 20) : 0;

    private double SurfaceTop => Gap + DropOffset;

    private double FitWidth => IsCapsule ? Math.Clamp(_core.App.Get<double>(K.CapsuleFitWidth), -40, 40) : 0;

    internal double StripHeight => BodyHeight;

    internal double BareWidth => IsCapsule ? Math.Round((Math.Max(5 * BodyHeight, 2 * BodyHeight) + FitWidth) + (2 * Math.Min(14, BodyHeight * 0.19))) : CameraWidth * BodyHeight / BarHeight;

    internal double ScreenWidthDip => _area.Bounds.Width / Math.Max(0.5, _scale);

    internal double ScreenHeightDip => _area.Bounds.Height / Math.Max(0.5, _scale);

    private bool GuttersWanted => _mode == IslandMode.Open && _core.IslandOn && true;

    private QuickAccessLayout _layout = QuickAccessLayout.Initial();

    internal QuickAccessLayout QuickLayout => _layout;

    // ---------------------------------------------------------------- placement

    /// <summary>Moves the window to a monitor and re-applies the geometry.</summary>
    public void PlaceOn(MonitorArea area)
    {
        _area = area;
        var centre = new POINT { X = (int)(area.Bounds.X + (area.Bounds.Width / 2)), Y = (int)(area.Bounds.Y + (area.Bounds.Height / 2)) };
        var monitor = NativeMethods.MonitorFromPoint(centre, NativeConstants.MONITOR_DEFAULTTONEAREST);
        _dpi = IslandNative.MonitorScale(monitor);
        ResetEnvelope();
        _lastBounds = default;
        ApplyWindow();
    }

    public (nint Monitor, RECT Bounds) MonitorInfo()
    {
        var b = _area.Bounds;
        var bounds = new RECT { Left = (int)b.Left, Top = (int)b.Top, Right = (int)b.Right, Bottom = (int)b.Bottom };
        var monitor = NativeMethods.MonitorFromPoint(new POINT { X = (int)(b.X + (b.Width / 2)), Y = (int)(b.Y + (b.Height / 2)) }, NativeConstants.MONITOR_DEFAULTTONEAREST);
        return (monitor, bounds);
    }

    private void ApplyWindow()
    {
        if (_area.Bounds.Width <= 0)
        {
            _area = Screens.Primary();
            var c = new POINT { X = (int)(_area.Bounds.X + (_area.Bounds.Width / 2)), Y = (int)(_area.Bounds.Y + (_area.Bounds.Height / 2)) };
            _dpi = IslandNative.MonitorScale(NativeMethods.MonitorFromPoint(c, NativeConstants.MONITOR_DEFAULTTONEAREST));
            ResetEnvelope();
        }

        for (var pass = 0; pass < 2; pass++)
        {
            ComputeEnvelope();
            var pw = (int)Math.Ceiling(_winW * _scale);
            var ph = (int)Math.Ceiling(_winH * _scale);
            var x = (int)Math.Round(_area.Bounds.X + ((_area.Bounds.Width - pw) / 2));
            var y = (int)Math.Round(_area.Bounds.Y);
            if (_lastBounds != (x, y, pw, ph))
            {
                _lastBounds = (x, y, pw, ph);
                SetPhysicalBounds(x, y, pw, ph);
            }

            var actual = VisualTreeHelper.GetDpi(this).DpiScaleX;
            if (Math.Abs(actual - _dpi) < 0.001)
            {
                break;
            }

            _dpi = actual;
            ResetEnvelope();
        }

        _stage.Width = _winW;
        _stage.Height = _winH;
        LayoutHosts();
        PublishPill();
    }

    /// <summary>
    /// The window is the envelope of everything the island can be, not of the current frame. Its width only grows and always has
    /// room for both gutters, the hover emphasis and the spring swing, so the centred x never changes while animating (a layered
    /// window moves its stale bitmap at once but WPF re-renders a frame later: any x change shows as a one-frame jump). The height may
    /// grow at any time (top and left stay put) and shrinks only once the motion has settled.
    /// </summary>
    private void ComputeEnvelope()
    {
        var wide = Math.Min(ScreenWidthDip, Math.Max(600, Math.Max(_sw.Position, _sw.Target)) + OvershootLimit + (2 * SideGutter) + 24 + 4);
        _envW = Math.Max(_envW, wide);
        _winW = _envW;
        var gb = GuttersWanted && _layout.On(QuickSide.Bottom).Any() ? SideGutter : 0;
        var swing = _shrinkPending ? OvershootLimit : 0;
        var h = Math.Max(Math.Max(_sh.Position, _sh.Target) + swing, 8) + SurfaceTop + gb + 10;
        if (_mode == IslandMode.Open || _shrinkPending)
        {
            h += 4;
        }

        // growing a layered window blanks it for a frame or two until WPF has rendered at the new size, so the height starts at what
        // every normal page needs (nothing resizes while opening) and only grows for a taller page; it returns to that base after a
        // long rest closed (shrinking shows no blank)
        _envH = Math.Max(_envH, Math.Min(Math.Max(h, EnvelopeBaseHeight), ScreenHeightDip));
        _winH = _envH;
    }

    /// <summary>Height (island units) the window has at least: the tallest budget-sized page with its bottom quick buttons.</summary>
    private const double EnvelopeBaseHeight = 460;

    private void ResetEnvelope()
    {
        _envW = 0;
        _envH = 0;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ResetEnvelope();
        _lastBounds = default;
        Dispatcher.BeginInvoke(ApplyWindow, DispatcherPriority.Background);
    }

    private void LayoutHosts()
    {
        _closedHost.Width = _closedW;
        _closedHost.Height = _closedH;
        _openHost.Width = _openW;
        _openHost.Height = _openH;
        Canvas.SetLeft(_closedHost, (_winW - _closedW) / 2);
        Canvas.SetTop(_closedHost, SurfaceTop);
        Canvas.SetLeft(_openHost, (_winW - _openW) / 2);
        Canvas.SetTop(_openHost, SurfaceTop);
        _surface.Width = _winW;
        _surface.Height = _winH;
        Canvas.SetLeft(_surface, 0);
        Canvas.SetTop(_surface, 0);
        _clipLayer.Width = _winW;
        _clipLayer.Height = _winH;
        _quick.Width = _winW;
        _quick.Height = _winH;
        PositionQuick();
        UpdateSurface();
    }

    // ---------------------------------------------------------------- spring animation

    /// <summary>The farthest a side may pass its target (the display keeps this much free around the island).</summary>
    private const double OvershootLimit = 6;

    /// <summary>
    /// Points the silhouette at a size. Each side follows its own spring as in the macOS original (growing: 0.44 / 0.38 s with a
    /// small swing, shrinking: 0.30 / 0.26 s with none; a strip that only fits a new reading eases 0.5 s without a swing).
    /// The window is resized here, once, to the envelope of the move; the frames only move numbers of the live shape.
    /// </summary>
    internal void SetTarget(double w, double h, bool snap = false, bool steady = false)
    {
        w = Math.Round(w);
        h = Math.Round(h);
        if (!snap && !_rendering && Math.Abs(_sw.Target - w) < 0.5 && Math.Abs(_sh.Target - h) < 0.5 && _sw.Settled && _sh.Settled)
        {
            // a refresh that changed nothing (a timer tick, a playback update): no window resize, no frame loop
            return;
        }

        if (snap || D.ReduceMotion)
        {
            _sw.Snap(w);
            _sh.Snap(h);
        }
        else
        {
            ConfigureSide(_sw, w, width: true, steady);
            ConfigureSide(_sh, h, width: false, steady);
        }

        _sw.Target = w;
        _sh.Target = h;
        _shrinkPending = true;
        ApplyWindow();
        UpdateSurface();
        StartRendering();
    }

    private static void ConfigureSide(Spring s, double target, bool width, bool steady)
    {
        var grow = target > s.Position;
        double duration;
        double bounce;
        if (steady && width)
        {
            duration = 0.5;
            bounce = 0;
        }
        else if (grow)
        {
            duration = width ? 0.44 : 0.38;
            bounce = Spring.LimitedBounce(width ? 0.12 : 0.10, Math.Abs(target - s.Position), OvershootLimit);
        }
        else
        {
            duration = width ? 0.30 : 0.26;
            bounce = 0;
        }

        s.Configure(duration, bounce);
    }

    private void StartRendering()
    {
        if (_rendering)
        {
            return;
        }

        // time starts at the first frame after the move was requested, so the page build and its layout never eat into the animation
        _rendering = true;
        _firstFrame = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopRendering()
    {
        if (!_rendering)
        {
            return;
        }

        CompositionTarget.Rendering -= OnRendering;
        _rendering = false;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = e is RenderingEventArgs ra ? ra.RenderingTime : TimeSpan.Zero;
        double dt;
        if (_firstFrame)
        {
            _firstFrame = false;
            dt = 0;
        }
        else
        {
            dt = (now - _lastRender).TotalSeconds;
            if (dt <= 0)
            {
                // the same composition frame reported twice
                return;
            }

            dt = Math.Min(dt, 0.25);
        }

        _lastRender = now;
        _sw.Step(dt);
        _sh.Step(dt);
        var choreoDone = StepChoreography(dt);
        UpdateSurface();
        var settled = _sw.Settled && _sh.Settled;
        if (_sw.Position > _winW - 4 || _sh.Position + SurfaceTop > _winH - 2)
        {
            // only a retarget in flight can get here (the envelope covers a normal swing)
            ApplyWindow();
        }

        if (settled && choreoDone)
        {
            StopRendering();
            _shrinkPending = false;
            FinishChoreography();
            ApplyWindow();
            OnSettled();
        }
    }

    private void UpdateSurface()
    {
        var w = Math.Max(0, _sw.Position);
        var h = Math.Max(0, _sh.Position);
        _surface.Capsule = IsCapsule;
        _surface.Open = _mode == IslandMode.Open || h > 60;
        _surface.Outline = OutlineOn;
        _surface.Translucent = TranslucentOn;
        _surface.Set((_winW - w) / 2, SurfaceTop, w, h);
        if (!ReferenceEquals(_clipLayer.Clip, _surface.CurrentGeometry))
        {
            _clipLayer.Clip = _surface.CurrentGeometry;
        }
    }

    private void OnSettled()
    {
        PublishPill();
        Settled?.Invoke();
    }

    public event Action? Settled;

    // ---------------------------------------------------------------- published pill rect (gesture hook)

    private void PublishPill()
    {
        PublishIdleZone();
        if (_suppressed || !_core.IslandOn)
        {
            IslandRects.Pill = null;
            return;
        }

        var w = Math.Max(_sw.Target, _sw.Position) * _scale;
        var h = Math.Max(_sh.Target, _sh.Position) * _scale;
        var left = _area.Bounds.X + ((_area.Bounds.Width - w) / 2);
        var top = _area.Bounds.Y + (SurfaceTop * _scale);
        IslandRects.Pill = new PillRect(
            (int)left,
            (int)top,
            (int)Math.Ceiling(left + w),
            (int)Math.Ceiling(top + h),
            (int)(top + ((HeaderRow + SurfaceTop + 12) * _scale)),
            Expanded,
            _page?.ScrollsContent ?? false,
            _core.Playback.Current.HasTrack && (Expanded ? _module == "music" : _core.CurrentActivity() == IslandCore.ActMusic || _core.Playback.Current.IsPlaying));
    }
}

/// <summary>Immutable snapshot of where the island is on screen (physical pixels), readable from the hook thread.</summary>
internal sealed record PillRect(int Left, int Top, int Right, int Bottom, int HeaderBottom, bool Expanded, bool ContentScrolls, bool MusicShowing);

internal static class IslandRects
{
    public static volatile PillRect? Pill;
}
