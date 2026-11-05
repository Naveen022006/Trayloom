// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Core.Native;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Island;

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

    /// <summary>
    /// The body's centre across the work area, in units from the work area's left edge (so it does not depend on where the window sits).
    /// It moves between the closed centre and the open centre (where the open page and its gutters fit) with the width timing and no bounce.
    /// </summary>
    private readonly Spring _sx = new(0.44, 0, 0);
    private readonly ScaleTransform _openScale = new(1, 1);
    private TimeSpan _lastRender;
    private bool _firstFrame;
    private bool _rendering;
    private bool? _capsuleCache;
    private bool? _outlineCache;
    private bool? _translucentCache;

    /// <summary>The connected displays, refreshed on the UI thread when the display configuration or a work area changes.</summary>
    private IReadOnlyList<IslandMonitor> _monitors = IslandDisplays.Enumerate();

    /// <summary>The display the window is on. Its <see cref="IslandMonitor.Dpi"/> follows the window's real DPI once Windows has settled it.</summary>
    private IslandMonitor _monitor;

    /// <summary>The geometry of the current placement: display, position and zoom resolved into units and pixels (see <see cref="IslandPlacement"/>).</summary>
    private IslandFrame _frame;

    /// <summary>The raw work area Windows reported for <see cref="_monitor"/> when it was placed: a change means the taskbar moved or resized.</summary>
    private RECT _rawWork;

    /// <summary>The window's origin on screen (physical pixels) as last applied; the layout of the layers is relative to it.</summary>
    private int _winX;
    private int _winY;

    /// <summary>The monitor's DPI scale (physical pixels per DIP); <see cref="_scale"/> adds the island zoom on top.</summary>
    private double _dpi => _monitor.Dpi;

    /// <summary>The uniform island zoom (setting <see cref="ZoomKey"/>): everything inside the window is drawn this much larger.</summary>
    private double _zoom = DefaultZoom;

    /// <summary>Physical pixels per island unit: DPI times zoom. All screen-rect math of the island uses this.</summary>
    private double _scale => _dpi * _zoom;

    /// <summary>The setting that scales the whole island (length and breadth) — clamped to 0.9 to 1.5 (<see cref="IslandSizes.MinZoom"/> to <see cref="IslandSizes.MaxZoom"/>).</summary>
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
        Title = Loc.T("Trayloom Island");
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
        _monitor = _monitors[0];
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

    /// <summary>The capsule silhouette: chosen, or forced while the island floats away from its edge (the notch only exists attached).</summary>
    public bool IsCapsule => _capsuleCache ??= _core.App.Get<string>(K.Silhouette) != "notch" || !_frame.Position.Attached;

    private bool OutlineOn => _outlineCache ??= _core.App.Get<bool>(K.Outline);

    private bool TranslucentOn => _translucentCache ??= _core.App.Get<bool>(K.Translucent);

    /// <summary>Settings that shape the silhouette are read once, not on every animation frame.</summary>
    private void InvalidateStyleCache()
    {
        _capsuleCache = null;
        _outlineCache = null;
        _translucentCache = null;
    }

    /// <summary>The display the island is on (the main display until something else is chosen).</summary>
    internal IslandMonitor? CurrentMonitor => _monitor;

    /// <summary>The geometry of the current placement.</summary>
    internal IslandFrame Frame => _frame;

    internal double Scale => _scale;

    internal double Zoom => _zoom;

    private double ReadZoom()
    {
        var z = _core.App.Get(ZoomKey, DefaultZoom);
        return IslandSizes.ClampZoom(z, DefaultZoom);
    }

    /// <summary>Reads the zoom setting and scales the whole stage (layout transform: text stays sharp, hit tests follow).</summary>
    private void ApplyZoom()
    {
        _zoom = ReadZoom();
        CurrentZoom = _zoom;
        _stage.LayoutTransform = Math.Abs(_zoom - 1) < 0.0005 ? Transform.Identity : new ScaleTransform(_zoom, _zoom);
        RebuildFrame();
    }

    /// <summary>Resolves the display, the saved position of that display and the zoom into <see cref="_frame"/>.</summary>
    private void RebuildFrame() =>
        _frame = IslandPlacement.Frame(_monitor, IslandPlacementStore.Load(_core.App, _monitor), _zoom);

    // ---------------------------------------------------------------- geometry helpers

    private double BodyHeight => IsCapsule ? Math.Clamp(20 + _core.App.Get<double>(K.CapsuleFitHeight), 16, 24) : BarHeight;

    private double Gap => IsCapsule ? 2 : 0;

    private double DropOffset => IsCapsule ? Math.Clamp(_core.App.Get<double>(K.CapsuleFitDrop), 0, 20) : 0;

    /// <summary>The capsule's gap plus the "Capsule fit" drop (units): the space between the edge (plus the offset) and the body's near side.</summary>
    internal double NearGap => Gap + DropOffset;

    /// <summary>Units from the work-area edge the island rests on to the body's near side: <see cref="NearGap"/> plus the position's offset.</summary>
    internal double NearInset => NearGap + _frame.Inset;

    /// <summary>The island rests on the bottom edge: pages open upward and the notch is drawn mirrored.</summary>
    internal bool OpensUp => _frame.OpensUp;

    internal IslandEdge Edge => _frame.Edge;

    private double FitWidth => IsCapsule ? Math.Clamp(_core.App.Get<double>(K.CapsuleFitWidth), -40, 40) : 0;

    internal double StripHeight => BodyHeight;

    internal double BareWidth => IsCapsule ? Math.Round((Math.Max(5 * BodyHeight, 2 * BodyHeight) + FitWidth) + (2 * Math.Min(14, BodyHeight * 0.19))) : CameraWidth * BodyHeight / BarHeight;

    /// <summary>Width of the display's work area in island units (what the island may use).</summary>
    internal double ScreenWidthDip => _frame.UsableWidth;

    /// <summary>Height of the display's work area in island units (the taskbar is not part of it).</summary>
    internal double ScreenHeightDip => _frame.UsableHeight;

    /// <summary>
    /// Height in units an open page may take on the far side of the body: the work height minus the near inset, the margin kept free at the far end
    /// and <paramref name="farGutter"/> (the quick-button row on that side, 0 when none).
    /// </summary>
    internal double FarRoomDip(double farGutter) => _frame.FarRoom(NearGap, farGutter);

    private bool GuttersWanted => _mode == IslandMode.Open && _core.IslandOn && true;

    /// <summary>Width the open page's quick-button columns and row take on one side (units); 0 for a side without buttons or while not open.</summary>
    private double GutterOf(QuickSide side) => GuttersWanted && _layout.On(side).Any() ? SideGutter : 0;

    /// <summary>
    /// The body's centre in window units for a body <paramref name="w"/> wide with quick-button gutters, kept inside the work area: where the
    /// layers of that body are placed.
    /// </summary>
    internal double BodyCentre(double w, double gutterL, double gutterR) => _frame.WorkLeftIn(_winX) + CentreUnits(w, gutterL, gutterR);

    /// <summary>The body's centre in units from the work area's left edge (the unit of <see cref="_sx"/>).</summary>
    private double CentreUnits(double w, double gutterL, double gutterR) =>
        IslandPlacement.ClampCentre(_frame.CentreX, w, gutterL, gutterR, _frame.UsableWidth);

    /// <summary>
    /// Where the spring <see cref="_sx"/> is heading for a body <paramref name="w"/> wide: the open centre (with its gutters) while open, the closed
    /// centre otherwise.
    /// </summary>
    private double TargetCentre(double w) => _mode == IslandMode.Open
        ? CentreUnits(w, GutterOf(QuickSide.Left), GutterOf(QuickSide.Right))
        : CentreUnits(w, 0, 0);

    /// <summary>
    /// The top of a body <paramref name="h"/> tall in window units: from the near edge down for a top island, up from it for a bottom island
    /// (the body's near side stays on the resting line while it grows away from it).
    /// </summary>
    internal double BodyTop(double h) => OpensUp
        ? ((_frame.Monitor.Work.Bottom - _winY) / _frame.Scale) - NearInset - h
        : _frame.WorkTopIn(_winY) + NearInset;

    private QuickAccessLayout _layout = QuickAccessLayout.Initial();

    internal QuickAccessLayout QuickLayout => _layout;

    // ---------------------------------------------------------------- placement

    /// <summary>
    /// Puts the island on a display and re-applies the geometry (the saved position of that display, the window rectangle, the layers). A move to
    /// another display hides the window first, moves and resizes it, lets Windows settle its DPI, and shows it again on the next idle turn: a visible
    /// window that crosses displays of different DPI flickers at the wrong size.
    /// </summary>
    public void PlaceOn(IslandMonitor monitor)
    {
        var moving = IsVisible && !string.Equals(monitor.Key, _monitor.Key, StringComparison.OrdinalIgnoreCase);
        if (moving)
        {
            Hide();
        }

        _monitor = monitor;
        _rawWork = RawWork(monitor.Handle);
        RebuildFrame();
        InvalidateStyleCache();
        ResetEnvelope();
        _lastBounds = default;
        _sx.Snap(TargetCentre(Math.Max(_sw.Target, 1)));
        ApplyWindow();
        LogPlacement();
        if (moving)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                if (!_disposed)
                {
                    UpdateVisibility();
                }
            });
        }
    }

    /// <summary>The display and its bounds, for the fullscreen check.</summary>
    public (nint Monitor, RECT Bounds) MonitorInfo() => (_monitor.Handle, _monitor.Bounds);

    /// <summary>The work area Windows reports for a display handle right now (no auto-hide adjustment); empty when the call fails.</summary>
    private static RECT RawWork(nint handle)
    {
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        return handle != 0 && NativeMethods.GetMonitorInfoW(handle, ref info) ? info.rcWork : default;
    }

    private string _lastPlacementLog = "";

    /// <summary>One line per placement in the log (skipped while nothing in it changed), to see where the island went and why.</summary>
    private void LogPlacement()
    {
        try
        {
            var pill = IslandPlacement.PillRectPx(_frame, Math.Max(_sw.Target, 1), Math.Max(_sh.Target, 1), NearGap);
            var line = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"placement: display={_core.App.Get<string>(K.Display)} monitor=\"{_monitor.Name}\" key={_monitor.HardwareId} dpi={_monitor.Dpi:0.##} zoom={_zoom:0.00} pos={_frame.Position.Format()} window={_lastBounds.X},{_lastBounds.Y},{_lastBounds.W},{_lastBounds.H} pill={pill.Left},{pill.Top},{pill.Right},{pill.Bottom} opensUp={OpensUp}");
            if (line != _lastPlacementLog)
            {
                _lastPlacementLog = line;
                _core.Ctx.Log.Info(line);
            }
        }
        catch (Exception ex)
        {
            _core.Ctx.Log.Warn("placement log failed: " + ex.Message);
        }
    }

    /// <summary>Re-reads the displays (on the UI thread) and keeps <see cref="_monitor"/> pointing at the fresh record of its display.</summary>
    private void RefreshMonitors()
    {
        _monitors = IslandDisplays.Enumerate();
    }

    private void ApplyWindow()
    {
        if (_monitor.Work.Width <= 0 || _monitor.Work.Height <= 0)
        {
            RefreshMonitors();
            _monitor = _monitors[0];
            _rawWork = RawWork(_monitor.Handle);
            RebuildFrame();
            InvalidateStyleCache();
            ResetEnvelope();
        }

        for (var pass = 0; pass < 2; pass++)
        {
            ComputeEnvelope();
            var (x, y, pw, ph) = _frame.WindowRect(_winW, _winH);
            _winX = x;
            _winY = y;
            if (_lastBounds != (x, y, pw, ph))
            {
                _lastBounds = (x, y, pw, ph);
                SetPhysicalBounds(x, y, pw, ph);
            }

            // A hidden window (PlaceOn hides it before moving it to another display) may not get WM_DPICHANGED, so what it reports is the DPI of
            // the display it was on, not the new one: only a shown window's DPI is a measurement. The display's own DPI stands until the window
            // is shown again, and the placement made then settles it.
            var actual = VisualTreeHelper.GetDpi(this).DpiScaleX;
            if (!IsVisible || Math.Abs(actual - _dpi) < 0.001)
            {
                break;
            }

            // Windows put the window on a display of another DPI than the one we measured: the layout follows the real one
            _monitor = _monitor with { Dpi = actual };
            RebuildFrame();
            ResetEnvelope();

            // the centre spring is in units of the old scale
            _sx.Snap(TargetCentre(Math.Max(_sw.Target, 1)));
        }

        _stage.Width = _winW;
        _stage.Height = _winH;
        LayoutHosts();
        PublishPill();
    }

    /// <summary>
    /// The window is the envelope of everything the island can be, not of the current frame. Its width only grows and always has
    /// room for both gutters, the hover emphasis and the spring swing, so the centred x never changes while animating (a layered
    /// window moves its stale bitmap at once but WPF re-renders a frame later: any x change shows as a one-frame jump). The height of a top
    /// island may grow at any time (top and left stay put) and shrinks only once the motion has settled; a bottom island's window is always
    /// the full work-area height (it is anchored by its bottom, and growing such a window shows a shifted stale frame).
    /// </summary>
    private void ComputeEnvelope()
    {
        var wide = Math.Min(ScreenWidthDip, Math.Max(600, Math.Max(_sw.Position, _sw.Target)) + OvershootLimit + (2 * SideGutter) + 24 + 4);
        _envW = Math.Max(_envW, wide);
        _winW = _envW;
        if (OpensUp)
        {
            _envH = ScreenHeightDip;
            _winH = _envH;
            return;
        }

        var gb = GutterOf(QuickSide.Bottom);
        var swing = _shrinkPending ? OvershootLimit : 0;
        var h = Math.Max(Math.Max(_sh.Position, _sh.Target) + swing, 8) + NearInset + gb + 10;
        if (_mode == IslandMode.Open || _shrinkPending)
        {
            h += 4;
        }

        // growing a layered window blanks it for a frame or two until WPF has rendered at the new size, so the height starts at what
        // every normal page needs (nothing resizes while opening) and only grows for a taller page; it returns to that base after a
        // long rest closed (shrinking shows no blank). A floating island starts that base at its offset.
        _envH = Math.Max(_envH, Math.Min(Math.Max(h, EnvelopeBaseHeight + _frame.Inset), ScreenHeightDip));
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
        // each layer sits at the final place of its own body: the silhouette moves between them and clips what shows
        // while not open the body centre is the spring's target, which is clamped for the body's real width: the hover emphasis widens the body by 10 or 20
        // units, and at the left or right end of the work area that shifts the clamped centre, so the content sits on the body's centre, not on its own clamp
        var closedCentre = _mode == IslandMode.Open ? BodyCentre(_closedW, 0, 0) : _frame.WorkLeftIn(_winX) + _sx.Target;
        Canvas.SetLeft(_closedHost, closedCentre - (_closedW / 2));
        Canvas.SetTop(_closedHost, BodyTop(_closedH));
        Canvas.SetLeft(_openHost, BodyCentre(_openW, GutterOf(QuickSide.Left), GutterOf(QuickSide.Right)) - (_openW / 2));
        Canvas.SetTop(_openHost, BodyTop(_openH));
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
        var centre = TargetCentre(w);
        if (!snap && !_rendering && Math.Abs(_sw.Target - w) < 0.5 && Math.Abs(_sh.Target - h) < 0.5 && Math.Abs(_sx.Target - centre) < 0.5 && _sw.Settled && _sh.Settled && _sx.Settled)
        {
            // a refresh that changed nothing (a timer tick, a playback update): no window resize, no frame loop
            return;
        }

        if (snap || D.ReduceMotion)
        {
            _sw.Snap(w);
            _sh.Snap(h);
            _sx.Snap(centre);
        }
        else
        {
            // the centre moves with the width timing, so a body that has to shift to fit (a corner, quick buttons) grows and slides as one
            ConfigureCentre(_sx, w > _sw.Position, steady);
            ConfigureSide(_sw, w, width: true, steady);
            ConfigureSide(_sh, h, width: false, steady);
        }

        _sw.Target = w;
        _sh.Target = h;
        _sx.Target = centre;
        _shrinkPending = true;
        ApplyWindow();
        UpdateSurface();
        StartRendering();
    }

    /// <summary>The centre spring takes the width's duration and never bounces (a swing sideways would leave the work area).</summary>
    private static void ConfigureCentre(Spring s, bool grow, bool steady) => s.Configure(steady ? 0.5 : (grow ? 0.44 : 0.30), 0);

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
        _sx.Step(dt);
        var choreoDone = StepChoreography(dt);
        UpdateSurface();
        var settled = _sw.Settled && _sh.Settled && _sx.Settled;
        if (_sw.Position > _winW - 4 || (!OpensUp && _sh.Position + NearInset > _winH - 2))
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
        _surface.FlipVertical = OpensUp;
        _surface.Open = _mode == IslandMode.Open || h > 60;
        _surface.Outline = OutlineOn;
        _surface.Translucent = TranslucentOn;
        _surface.Set(_frame.WorkLeftIn(_winX) + _sx.Position - (w / 2), BodyTop(h), w, h);
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

        // while a drag is in flight (gestures and the outside-click test stay off) and after shutdown no pill is published, whatever asked for it
        if (_dragging || _disposed || _suppressed || !_core.IslandOn)
        {
            IslandRects.Pill = null;
            return;
        }

        // the larger of the live and the target silhouette, on the final centre: the rectangle the gesture hook and the outside-click test use
        var r = IslandPlacement.BodyRectPx(_frame, _sx.Target, Math.Max(_sw.Target, _sw.Position), Math.Max(_sh.Target, _sh.Position), NearGap);
        var gut = (int)(SideGutter * _scale);
        IslandRects.Pill = new PillRect(
            r.Left,
            r.Top,
            r.Right,
            r.Bottom,
            (int)(r.Top + ((HeaderRow + NearGap + 12) * _scale)),
            Expanded,
            _page?.ScrollsContent ?? false,
            _core.Playback.Current.HasTrack && (Expanded ? _module == "music" : _core.CurrentActivity() == IslandCore.ActMusic || _core.Playback.Current.IsPlaying),
            _mode == IslandMode.Open ? WheelZonesOnScreen() : [],
            OpensUp,
            (r.Left - gut, OpensUp ? r.Top - gut : r.Top, r.Right + gut, OpensUp ? r.Bottom : r.Bottom + gut));
    }

    /// <summary>Where the open page's wheel-driven sliders are on screen (physical pixels), so the gesture hook leaves the wheel to them.</summary>
    private List<(int L, int T, int R, int B)> WheelZonesOnScreen()
    {
        var zones = new List<(int L, int T, int R, int B)>();
        void Walk(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is LevelSlider { IsVisible: true, IsEnabled: true, Scrollable: true, ActualWidth: > 0, ActualHeight: > 0 } slider && PresentationSource.FromVisual(slider) is not null)
                {
                    var a = slider.PointToScreen(new Point(0, 0));
                    var b = slider.PointToScreen(new Point(slider.ActualWidth, slider.ActualHeight));
                    zones.Add(((int)Math.Floor(Math.Min(a.X, b.X)), (int)Math.Floor(Math.Min(a.Y, b.Y)), (int)Math.Ceiling(Math.Max(a.X, b.X)), (int)Math.Ceiling(Math.Max(a.Y, b.Y))));
                }
                else if (child is UIElement { IsVisible: false })
                {
                    continue;
                }

                Walk(child);
            }
        }

        Walk(_openHost);
        return zones;
    }
}

/// <summary>Immutable snapshot of where the island is on screen (physical pixels), readable from the hook thread.</summary>
/// <param name="WheelZones">Screen rectangles (left, top, right, bottom) of the open page's sliders: the wheel over them belongs to the slider.</param>
/// <param name="OpensUp">The island rests on the bottom edge: pages open upward, so the wheel direction that opens and closes it is the opposite.</param>
/// <param name="Interactive">The pill plus the quick-button gutters on its left, right and far side: a press outside it collapses the open island.</param>
internal sealed record PillRect(int Left, int Top, int Right, int Bottom, int HeaderBottom, bool Expanded, bool ContentScrolls, bool MusicShowing, IReadOnlyList<(int L, int T, int R, int B)> WheelZones, bool OpensUp, (int L, int T, int R, int B) Interactive);

internal static class IslandRects
{
    public static volatile PillRect? Pill;
}
