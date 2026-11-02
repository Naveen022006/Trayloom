// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
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
    private readonly Grid _closedHost = new() { ClipToBounds = false };
    private readonly Grid _openHost = new() { ClipToBounds = false, Opacity = 0, IsHitTestVisible = false };
    private readonly Canvas _quick = new();
    private readonly Spring _sw = new(0.44, 0.25, 109);
    private readonly Spring _sh = new(0.38, 0.22, 24);
    private readonly Stopwatch _clock = new();
    private double _lastTick;
    private bool _rendering;

    private MonitorArea _area;
    private double _scale = 1;
    private double _winW = 160;
    private double _winH = 60;
    private double _closedW = 109;
    private double _closedH = 24;
    private double _openW = 560;
    private double _openH = 300;
    private bool _shrinkPending;

    private IslandMode _mode = IslandMode.Closed;
    private bool _suppressed;
    private bool _concealed;
    private bool _disposed;
    private int _holds;
    private bool _pinned;
    private bool _openedByHover;
    private bool _keyFocus;
    private bool _exploring;
    private string _module = "controls";
    private IslandPage? _page;
    private string? _detail;
    private DateTime _lastChange = DateTime.MinValue;

    public IslandWindow(IslandCore core)
    {
        _core = core;
        Title = "WinKnife Island";
        Content = _stage;
        UseLayoutRounding = false;
        SnapsToDevicePixels = false;
        _stage.Children.Add(_surface);
        _stage.Children.Add(_closedHost);
        _stage.Children.Add(_openHost);
        _stage.Children.Add(_quick);
        _surface.GeometryChanged += () => { };
        AllowDrop = true;
        _clock.Start();
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

    public bool IsCapsule => _core.App.Get<string>(K.Silhouette) != "notch";

    internal MonitorArea Area => _area;

    internal double Scale => _scale;

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

    private bool GuttersWanted => _mode == IslandMode.Open && _core.IslandOn && !_exploringHidesQuick;

    private bool _exploringHidesQuick;

    private QuickAccessLayout _layout = QuickAccessLayout.Initial();

    internal QuickAccessLayout QuickLayout => _layout;

    // ---------------------------------------------------------------- placement

    /// <summary>Moves the window to a monitor and re-applies the geometry.</summary>
    public void PlaceOn(MonitorArea area)
    {
        _area = area;
        var centre = new POINT { X = (int)(area.Bounds.X + (area.Bounds.Width / 2)), Y = (int)(area.Bounds.Y + (area.Bounds.Height / 2)) };
        var monitor = NativeMethods.MonitorFromPoint(centre, NativeConstants.MONITOR_DEFAULTTONEAREST);
        _scale = IslandNative.MonitorScale(monitor);
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
            _scale = IslandNative.MonitorScale(NativeMethods.MonitorFromPoint(c, NativeConstants.MONITOR_DEFAULTTONEAREST));
        }

        var gx = GuttersWanted ? SideGutter : 0;
        var gb = GuttersWanted && _layout.On(QuickSide.Bottom).Any() ? SideGutter : 0;
        var emph = _hoverEmphasis ? 24 : 0;
        _winW = Math.Min(ScreenWidthDip, Math.Max(Math.Max(_sw.Position, _sw.Target), 20) + (2 * gx) + emph + 4);
        _winH = Math.Max(Math.Max(_sh.Position, _sh.Target), 8) + SurfaceTop + gb + 10;
        if (_mode == IslandMode.Open || _shrinkPending)
        {
            _winH += 4;
        }

        for (var pass = 0; pass < 2; pass++)
        {
            var pw = (int)Math.Ceiling(_winW * _scale);
            var ph = (int)Math.Ceiling(_winH * _scale);
            var x = (int)Math.Round(_area.Bounds.X + ((_area.Bounds.Width - pw) / 2));
            var y = (int)Math.Round(_area.Bounds.Y);
            SetPhysicalBounds(x, y, pw, ph);
            var actual = VisualTreeHelper.GetDpi(this).DpiScaleX;
            if (Math.Abs(actual - _scale) < 0.001)
            {
                break;
            }

            _scale = actual;
        }

        _stage.Width = _winW;
        _stage.Height = _winH;
        LayoutHosts();
        PublishPill();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
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
        _quick.Width = _winW;
        _quick.Height = _winH;
        PositionQuick();
        UpdateSurface();
    }

    // ---------------------------------------------------------------- spring animation

    internal void SetTarget(double w, double h, bool snap = false)
    {
        w = Math.Round(w);
        h = Math.Round(h);
        if (snap || D.ReduceMotion)
        {
            _sw.Snap(w);
            _sh.Snap(h);
        }

        _sw.Target = w;
        _sh.Target = h;
        _shrinkPending = true;
        ApplyWindow();
        StartRendering();
    }

    private void StartRendering()
    {
        if (_rendering)
        {
            return;
        }

        _rendering = true;
        _lastTick = _clock.Elapsed.TotalSeconds;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var dt = Math.Min(0.05, Math.Max(0.001, now - _lastTick));
        _lastTick = now;
        // direction dependent spring character (growing springs bounce, shrinking ones do not)
        StepDir(_sw, dt, 0.44, 0.25, 0.30, 0);
        StepDir(_sh, dt, 0.38, 0.22, 0.26, 0);
        UpdateSurface();
        var settled = _sw.Settled && _sh.Settled;
        var needGrow = _sw.Position > _winW - ((GuttersWanted ? 2 * SideGutter : 0) + 8) || _sh.Position + SurfaceTop > _winH - 4;
        if (needGrow)
        {
            ApplyWindow();
        }

        if (settled)
        {
            CompositionTarget.Rendering -= OnRendering;
            _rendering = false;
            _shrinkPending = false;
            ApplyWindow();
            OnSettled();
        }
    }

    private static void StepDir(Spring s, double dt, double growDur, double growBounce, double shrinkDur, double shrinkBounce)
    {
        var grow = s.Target >= s.Position;
        var tmp = grow ? new Spring(growDur, growBounce) : new Spring(shrinkDur, shrinkBounce);
        tmp.Position = s.Position;
        tmp.Velocity = s.Velocity;
        tmp.Target = s.Target;
        tmp.Step(dt);
        // overshoot is limited to 12 DIP
        var limit = 12.0;
        var pos = tmp.Position;
        if (grow && pos > tmp.Target + limit)
        {
            pos = tmp.Target + limit;
            tmp.Velocity = Math.Min(tmp.Velocity, 0);
        }

        s.Position = pos;
        s.Velocity = tmp.Velocity;
    }

    private void UpdateSurface()
    {
        var w = Math.Max(0, _sw.Position);
        var h = Math.Max(0, _sh.Position);
        _surface.Capsule = IsCapsule;
        _surface.Open = _mode == IslandMode.Open || h > 60;
        _surface.Outline = _core.App.Get<bool>(K.Outline);
        _surface.Translucent = _core.App.Get<bool>(K.Translucent);
        var x = (_winW - w) / 2;
        _surface.Set(x, SurfaceTop, w, h);
        ClipHost(_closedHost, x, w, h);
        ClipHost(_openHost, x, w, h);
        var emphasis = _hoverEmphasis ? 1 : 0;
        _ = emphasis;
    }

    private void ClipHost(Grid host, double surfaceX, double w, double h)
    {
        var hostX = (_winW - host.Width) / 2;
        var geo = IslandSurface.Build(surfaceX - hostX, 0, w, h, IsCapsule, _surface.Open);
        host.Clip = geo;
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
        if (_suppressed || _concealed || !_core.IslandOn)
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
