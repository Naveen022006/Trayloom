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

internal sealed partial class IslandWindow
{
    private DispatcherTimer? _poll;
    private IDisposable? _mouseSub;
    private nint _fgHook;
    private IslandNative.WinEventProc? _fgProc;
    private bool _hoverEmphasis;
    private DateTime? _hoverDeadline;
    private DateTime? _leaveAt;
    private bool _inside;
    private bool _suppressHover;
    private bool _dragOver;
    private volatile bool _lmbDown;
    private volatile int _pressX;
    private volatile int _pressY;
    private volatile bool _pressOutside;
    private volatile ZoneSnap? _idleZone;
    private DateTime _pointerSince = DateTime.MinValue;
    private MonitorArea _pointerArea;
    private DateTime _pointerAreaSince = DateTime.MinValue;
    private DateTime _fullscreenCheck = DateTime.MinValue;
    private bool _fullscreen;
    private DateTime _noticeHoverSince = DateTime.MinValue;
    private readonly List<MirrorWindow> _mirrors = [];

    private void InitInput()
    {
        PreviewKeyDown += OnKeyDown;
        _closedHost.MouseLeftButtonUp += OnClosedClick;
        _closedHost.Background = Brushes.Transparent;
        MouseRightButtonUp += OnContextMenu;
        DragEnter += OnDragEnter;
        DragOver += OnDragOver;
        DragLeave += (_, _) => { _dragOver = false; RefreshClosed(); };
        Drop += OnDrop;
        Deactivated += (_, _) => { };
    }

    private void StartPolling()
    {
        _poll = new DispatcherTimer(DispatcherPriority.Input, Dispatcher) { Interval = TimeSpan.FromMilliseconds(40) };
        _poll.Tick += (_, _) => Poll();
        _poll.Start();
        _mouseSub = _core.Ctx.Hooks.SubscribeMouse(OnMouseHook, 5);
        _fgProc = (_, _, hwnd, _, _, _, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, () => OnForeground(hwnd));
        _fgHook = IslandNative.SetWinEventHook(IslandNative.EventSystemForeground, IslandNative.EventSystemForeground, 0, _fgProc, 0, 0, IslandNative.WinEventOutOfContext | IslandNative.WinEventSkipOwnProcess);
    }

    private void StopPolling()
    {
        _poll?.Stop();
        _mouseSub?.Dispose();
        _mouseSub = null;
        if (_fgHook != 0)
        {
            IslandNative.UnhookWinEvent(_fgHook);
            _fgHook = 0;
        }

        foreach (var m in _mirrors)
        {
            m.Close();
        }

        _mirrors.Clear();
    }

    private void TeardownInput() => StopPolling();

    // ---------------------------------------------------------------- global hooks (hook thread: tiny work only)

    private bool OnMouseHook(MouseHookEvent e)
    {
        if (e.Kind == MouseEventKind.LeftDown)
        {
            // where the press started decides later whether this is a file drag (outside the island and its drop zone) or a plain click
            var down = IslandRects.Pill;
            var zone = _idleZone;
            var onPill = down is not null && e.X >= down.Left && e.X < down.Right && e.Y >= down.Top && e.Y < down.Bottom;
            var onZone = zone is not null && e.X >= zone.Left && e.X < zone.Right && e.Y >= zone.Top && e.Y < zone.Bottom;
            _pressX = e.X;
            _pressY = e.Y;
            _pressOutside = !onPill && !onZone;
            _lmbDown = true;
        }
        else if (e.Kind == MouseEventKind.LeftUp)
        {
            _lmbDown = false;
        }

        if (!e.IsButtonDown || e.IsFromWinKnife)
        {
            return false;
        }

        var pill = IslandRects.Pill;
        if (pill is not { Expanded: true } || _pinned)
        {
            return false;
        }

        // gutters hold the quick-access buttons; a child popup (menu) belongs to the island too
        var gut = (int)(SideGutter * _scale);
        var inside = e.X >= pill.Left - gut && e.X < pill.Right + gut && e.Y >= pill.Top && e.Y < pill.Bottom + gut;
        if (!inside)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                if (Expanded && !_pinned && _holds == 0 && !PointerOverOwnedWindow())
                {
                    Collapse();
                }
            });
        }

        return false;
    }

    private static bool PointerOverOwnedWindow()
    {
        NativeMethods.GetCursorPos(out var pt);
        var hwnd = NativeMethods_WindowFromPoint(pt);
        if (hwnd == 0)
        {
            return false;
        }

        IslandNative.GetWindowThreadProcessId(hwnd, out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "WindowFromPoint")]
    private static extern nint WindowFromPointNative(POINT p);

    private static nint NativeMethods_WindowFromPoint(POINT p) => WindowFromPointNative(p);

    private void OnForeground(nint hwnd)
    {
        if (!Expanded || _pinned || _holds > 0)
        {
            return;
        }

        if (_openedByHover && IsPointerInside())
        {
            return;
        }

        if (IsHiddenAppleMusic(hwnd))
        {
            // the Apple Music driver keeps its window parked or minimised and flickers the foreground: that is not the user leaving
            return;
        }

        Collapse();
    }

    private const string AppleMusicProcess = "AppleMusic";
    private const string AppleMusicWindowClass = "WinUIDesktopWin32WindowClass";

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetLayeredWindowAttributes(nint hwnd, out uint key, out byte alpha, out uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetSystemMetrics")]
    private static extern int GetSystemMetricsNative(int index);

    /// <summary>True when <paramref name="hwnd"/> belongs to Apple Music and its main window is minimised, parked off screen or fully transparent.</summary>
    private static bool IsHiddenAppleMusic(nint hwnd)
    {
        try
        {
            if (hwnd == 0)
            {
                return false;
            }

            var pid = AmNative.ProcessOf(hwnd);
            if (pid == 0)
            {
                return false;
            }

            using (var process = System.Diagnostics.Process.GetProcessById((int)pid))
            {
                if (!string.Equals(process.ProcessName, AppleMusicProcess, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            var mains = AmNative.ClassOf(hwnd) == AppleMusicWindowClass
                ? new List<nint> { hwnd }
                : AmNative.TopLevelWindows(pid).Where(h => AmNative.ClassOf(h) == AppleMusicWindowClass).ToList();
            if (mains.Count == 0)
            {
                return false;
            }

            foreach (var main in mains)
            {
                if (!(AmNative.IsIconic(main) || !AmNative.IsWindowVisible(main) || AppleMusicKeeper.Instance?.IsKept(main) == true || ParkedOrInvisible(main)))
                {
                    return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool ParkedOrInvisible(nint main)
    {
        if (AmNative.GetWindowRect(main, out var r) && r.Left < -10000)
        {
            return true;
        }

        var ex = (long)AmNative.GetWindowLongPtr(main, AmNative.GwlExStyle);
        return (ex & AmNative.WsExLayered) != 0 && GetLayeredWindowAttributes(main, out _, out var alpha, out var flags) && (flags & AmNative.LwaAlpha) != 0 && alpha == 0;
    }

    // ---------------------------------------------------------------- polling

    private Rect SurfaceScreenRect()
    {
        var w = Math.Max(_sw.Position, 1) * _scale;
        var h = Math.Max(_sh.Position, 1) * _scale;
        var left = _area.Bounds.X + ((_area.Bounds.Width - w) / 2);
        var top = _area.Bounds.Y + (SurfaceTop * _scale);
        return new Rect(left, top, w, h);
    }

    private bool IsPointerInside()
    {
        var c = Screens.CursorPosition();
        var r = SurfaceScreenRect();
        if (r.Contains(c))
        {
            return true;
        }

        if (Expanded && _quick.Children.Count > 0)
        {
            var g = new Rect(r.Left - ((SideGutter + 4) * _scale), r.Top, r.Width + (2 * (SideGutter + 4) * _scale), r.Height + (SideGutter * _scale * (_layout.On(QuickSide.Bottom).Any() ? 1 : 0)));
            return g.Contains(c);
        }

        return false;
    }

    private void Poll()
    {
        if (_disposed || !_core.IslandOn)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var cursor = Screens.CursorPosition();
        UpdateFullscreen();
        FollowPointerDisplay(cursor, now);
        if (_mode == IslandMode.Open)
        {
            // an inner layer of the page opened or closed: the Back mark follows
            RefreshNav();
        }

        var inside = IsPointerInside();
        if (_mode == IslandMode.Closed && !_rendering && _envH > EnvelopeBaseHeight + 0.5 && now - _lastChange > TimeSpan.FromSeconds(20))
        {
            // a tall page grew the window; after a long rest closed it goes back to the base height (shrinking shows no blank)
            _envH = 0;
            ApplyWindow();
        }

        var rest = new Rect(_area.Bounds.X + ((_area.Bounds.Width - (BareWidth * _scale)) / 2) - 6, _area.Bounds.Y, (BareWidth * _scale) + 12, (BodyHeight + SurfaceTop + 2) * _scale);

        // Idle economy: poll fast only while the pointer is near the island (or something is going on); otherwise 150 ms.
        var wake = rest;
        wake.Inflate(220 * _scale, 160 * _scale);
        var busy = _mode != IslandMode.Closed || inside || _lmbDown || _core.Mascot.Enabled || wake.Contains(cursor);
        var wanted = TimeSpan.FromMilliseconds(busy ? 40 : 150);
        if (_poll is { } poll && poll.Interval != wanted)
        {
            poll.Interval = wanted;
        }

        if (_suppressed)
        {
            if (_core.App.Get<bool>(K.HoverInFullscreen) && _core.App.Get<bool>(K.HideInFullscreen) && rest.Contains(cursor))
            {
                _hoverDeadline ??= DateTime.UtcNow.AddSeconds(Math.Clamp(_core.App.Get<double>(K.HoverDelay), 0.1, 1));
                if (DateTime.UtcNow >= _hoverDeadline)
                {
                    _hoverDeadline = null;
                    _suppressed = false;
                    Show();
                    Open(byHover: true, takeFocus: false);
                }
            }
            else
            {
                _hoverDeadline = null;
            }

            return;
        }

        // hidden until hover: the pointer reaching the place reveals the closed island
        var hideUntil = _core.App.Get<bool>(K.HideUntilHover) && _core.App.Get<bool>(K.OpenOnHover);
        if (hideUntil && _mode == IslandMode.Closed && !inside && rest.Contains(cursor))
        {
            inside = true;
        }

        if (inside != _inside)
        {
            _inside = inside;
            OnHover(inside);
        }
        else if (inside)
        {
            ContinueHover(now);
        }
        else
        {
            ContinueLeave(now);
        }

        ApplyHiddenAlpha();
        DetectDrag(cursor);
        PollMascotGaze(cursor);
    }

    private void PollMascotGaze(Point cursor)
    {
        if (_mode != IslandMode.Closed || !_core.Mascot.Enabled)
        {
            return;
        }

        var r = SurfaceScreenRect();
        var cx = r.Left + (r.Width / 2);
        var cy = r.Top + (r.Height / 2);
        var s = 18 * _scale;
        _core.Mascot.Actor.LookAt((cursor.X - cx) / (3 * s), (cursor.Y - cy) / (2 * s));
        if (r.Contains(cursor))
        {
            _core.Mascot.PointerArrived();
            if (DateTime.UtcNow - _pointerSince > TimeSpan.FromSeconds(1) && _pointerSince != DateTime.MinValue)
            {
                _pointerSince = DateTime.MinValue;
                _core.Mascot.Pet();
            }
            else if (_pointerSince == DateTime.MinValue)
            {
                _pointerSince = DateTime.UtcNow;
            }
        }
        else
        {
            _pointerSince = DateTime.MinValue;
        }
    }

    private void OnHover(bool entered)
    {
        var app = _core.App;
        var hover = app.Get<bool>(K.OpenOnHover);
        if (entered)
        {
            _leaveAt = null;
            if (_suppressHover || _suppressed || _holds > 0)
            {
                return;
            }

            if (_mode == IslandMode.Closed)
            {
                var live = _core.LiveActivities();
                if (live.Count > 1 && _core.Notices.Current is null)
                {
                    _picker = true;
                    RefreshClosed();
                    return;
                }

                if (_core.Notices.Current is { Banner: true })
                {
                    _core.Notices.Hold(true);
                    _noticeHoverSince = DateTime.UtcNow;
                }

                var willOpen = hover && (app.Get<bool>(K.HoverExpands) || live.Count == 0 || true);
                if (!willOpen && !D.ReduceMotion)
                {
                    SetEmphasis(true);
                }

                if (hover)
                {
                    _hoverDeadline = DateTime.UtcNow.AddSeconds(Math.Clamp(app.Get<double>(K.HoverDelay), 0.1, 1));
                }
                else if (!D.ReduceMotion)
                {
                    SetEmphasis(true);
                }
            }
        }
        else
        {
            _hoverDeadline = null;
            _suppressHover = false;
            SetEmphasis(false);
            if (_picker)
            {
                _picker = false;
                RefreshClosed();
            }

            if (_core.Notices.Held || _noticeExpanded)
            {
                _core.Notices.Hold(false);
                if (_noticeExpanded)
                {
                    _core.Notices.Clear();
                }
            }

            if ((_openedByHover && Expanded) || _mode == IslandMode.Peek)
            {
                _leaveAt = DateTime.UtcNow.AddSeconds(_mode == IslandMode.Peek ? 0.12 : Math.Clamp(app.Get<double>(K.CloseDelay), 0.1, 2));
            }
        }
    }

    private void ContinueHover(DateTime now)
    {
        if (_hoverDeadline is { } d && now >= d && _mode == IslandMode.Closed && _holds == 0)
        {
            _hoverDeadline = null;
            if (_core.Notices.Current is { Banner: true } && _core.App.Get<bool>(K.OpenOnHover))
            {
                _noticeExpanded = true;
                RefreshClosed();
                return;
            }

            if (_core.App.Get<bool>(K.HoverExpands))
            {
                SetEmphasis(false);
                Open(byHover: true, takeFocus: false);
            }
            else
            {
                SetEmphasis(false);
                _mode = IslandMode.Peek;
                RefreshClosed();
            }
        }
    }

    private void ContinueLeave(DateTime now)
    {
        if (_leaveAt is { } at && now >= at && _holds == 0 && !PointerOverOwnedWindow())
        {
            _leaveAt = null;
            if (_mode == IslandMode.Peek)
            {
                _mode = IslandMode.Closed;
                RefreshClosed();
            }
            else if (_openedByHover && Expanded && !_pinned)
            {
                Collapse();
            }
        }
    }

    private void SetEmphasis(bool on)
    {
        if (_hoverEmphasis == on)
        {
            return;
        }

        _hoverEmphasis = on;
        if (_mode == IslandMode.Closed)
        {
            RefreshClosed(rebuild: false);
        }
    }

    private void ApplyHiddenAlpha()
    {
        var hideUntil = _core.App.Get<bool>(K.HideUntilHover) && _core.App.Get<bool>(K.OpenOnHover);
        var showActivity = _core.App.Get<bool>(K.HiddenShowsActivity) && (_core.LiveActivities().Count > 0 || _core.Notices.Current is not null);
        var hidden = hideUntil && _mode == IslandMode.Closed && !_inside && !showActivity && !_placeholder;
        var target = hidden ? 0 : 1;
        if (Math.Abs(_surface.Alpha - target) > 0.01)
        {
            _surface.Alpha = target;
            _closedHost.Opacity = _mode == IslandMode.Closed && _choreo == Choreo.None && _fadeIn is null ? target : _closedHost.Opacity;
            _surface.InvalidateVisual();
            ClickThrough = hidden;
        }
    }

    // ---------------------------------------------------------------- display handling

    public void ApplyDisplay()
    {
        if (_disposed)
        {
            return;
        }

        var mode = _core.App.Get<string>(K.Display);
        var area = mode switch
        {
            "pointer" or "all" => Screens.UnderCursor(),
            _ => Screens.Primary(),
        };
        if (_area.Bounds != area.Bounds || _area.Bounds.Width <= 0)
        {
            PlaceOn(area);
        }

        UpdateVisibility();
        SyncMirrors();
    }

    private void OnDisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        _area = default;
        ApplyDisplay();
        RefreshClosed(snap: true);
    });

    private void FollowPointerDisplay(Point cursor, DateTime now)
    {
        var mode = _core.App.Get<string>(K.Display);
        if (mode is not ("pointer" or "all") || _mode != IslandMode.Closed || _inside)
        {
            return;
        }

        var under = Screens.FromPoint(cursor);
        if (under.Bounds == _area.Bounds)
        {
            _pointerAreaSince = DateTime.MinValue;
            return;
        }

        if (_pointerArea.Bounds != under.Bounds)
        {
            _pointerArea = under;
            _pointerAreaSince = DateTime.UtcNow;
            return;
        }

        if ((DateTime.UtcNow - _pointerAreaSince).TotalMilliseconds >= 200)
        {
            PlaceOn(under);
            RefreshClosed(snap: true);
            SyncMirrors();
        }
    }

    private void UpdateFullscreen()
    {
        if ((DateTime.UtcNow - _fullscreenCheck).TotalMilliseconds < 1000)
        {
            return;
        }

        _fullscreenCheck = DateTime.UtcNow;
        var want = false;
        if (_core.App.Get<bool>(K.HideInFullscreen))
        {
            var (monitor, bounds) = MonitorInfo();
            try
            {
                want = IslandNative.IsFullscreenForeground(monitor, bounds);
            }
            catch
            {
                want = false;
            }
        }

        if (want != _fullscreen)
        {
            _fullscreen = want;
            SetSuppressed(want);
        }
    }

    public void SetSuppressed(bool suppressed)
    {
        if (_suppressed == suppressed)
        {
            return;
        }

        _suppressed = suppressed;
        if (suppressed)
        {
            _core.Notices.Clear();
            Collapse(immediate: true);
            Hide();
            IslandRects.Pill = null;
        }
        else
        {
            UpdateVisibility();
        }
    }

    private void UpdateVisibility()
    {
        var show = _core.IslandOn && !_suppressed && _core.Session.CanPresent;
        if (show && !IsVisible)
        {
            ShowNoActivate();
            PlaceOn(_area);
        }
        else if (!show && IsVisible)
        {
            Hide();
        }

        PublishPill();
    }

    private void SyncMirrors()
    {
        var all = _core.App.Get<string>(K.Display) == "all";
        foreach (var m in _mirrors.ToList())
        {
            m.Close();
        }

        _mirrors.Clear();
        if (!all)
        {
            return;
        }

        foreach (var area in MonitorsAll())
        {
            if (area.Bounds == _area.Bounds)
            {
                continue;
            }

            var mirror = new MirrorWindow(this, area);
            _mirrors.Add(mirror);
            mirror.Show();
        }
    }

    private static List<MonitorArea> MonitorsAll()
    {
        var list = new List<MonitorArea>();
        foreach (var s in System.Windows.Forms.Screen.AllScreens)
        {
            var b = s.Bounds;
            list.Add(Screens.FromPoint(new Point(b.X + (b.Width / 2), b.Y + (b.Height / 2))));
        }

        return list;
    }

    internal void MoveHere(MonitorArea area)
    {
        PlaceOn(area);
        RefreshClosed(snap: true);
        SyncMirrors();
        Open(takeFocus: true);
    }

    internal FrameworkElement MirrorSource => _closedHost;

    // ---------------------------------------------------------------- key focus + keyboard

    private void SetKeyFocus(bool focus)
    {
        _keyFocus = focus;
        var hwnd = Handle;
        if (hwnd == 0)
        {
            return;
        }

        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeConstants.GWL_EXSTYLE).ToInt64();
        if (focus)
        {
            style &= ~NativeConstants.WS_EX_NOACTIVATE;
        }
        else
        {
            style |= NativeConstants.WS_EX_NOACTIVATE;
        }

        style |= NativeConstants.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLongPtr(hwnd, NativeConstants.GWL_EXSTYLE, (nint)style);
        if (focus)
        {
            try
            {
                ShowActivated = true;
                Activate();
                Focus();
            }
            catch
            {
            }
        }
    }

    internal void EnsureKeyboard()
    {
        if (!_keyFocus && Expanded)
        {
            SetKeyFocus(true);
        }
    }

    /// <summary>Whether the island currently holds the keyboard focus (it is activated).</summary>
    internal bool KeyboardActive => _keyFocus;

    /// <summary>Gives the keyboard back (the island becomes non-activating again) and returns to the window the user was in.</summary>
    internal void ReleaseKeyboard()
    {
        if (!_keyFocus)
        {
            return;
        }

        SetKeyFocus(false);
        if (_prevForeground != 0 && _prevForeground != Handle && WinKnife.Core.Native.NativeMethods.GetForegroundWindow() == Handle)
        {
            WinKnife.Core.Native.NativeMethods.SetForegroundWindow(_prevForeground);
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!Expanded)
        {
            return;
        }

        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (e.Key == Key.ImeProcessed)
        {
            return;
        }

        if (_page?.HandleKey(e) == true)
        {
            e.Handled = true;
            return;
        }

        if (key == Key.Escape)
        {
            StepBack();
            e.Handled = true;
            return;
        }

        if (key == Key.Back && !ctrl && !alt && !shift && Keyboard.FocusedElement is not (System.Windows.Controls.Primitives.TextBoxBase or PasswordBox))
        {
            // Backspace is Back, unless a text box owns the key
            if (CanGoBack())
            {
                GoBack();
                e.Handled = true;
            }

            return;
        }

        if (ctrl && !alt && key == Key.K)
        {
            ToggleExplore();
            e.Handled = true;
        }
        else if (ctrl && !alt && key == Key.OemComma)
        {
            _core.Ctx.Shell.OpenSettings(SettingsPages.Island);
            Collapse();
            e.Handled = true;
        }
        else if (ctrl && key == Key.Tab)
        {
            var mods = _core.Modules();
            var i = mods.FindIndex(m => m.Id == _module);
            if (mods.Count > 0)
            {
                i = ((i + (shift ? -1 : 1)) % mods.Count + mods.Count) % mods.Count;
                ShowModule(mods[i].Id);
            }

            e.Handled = true;
        }
        else if (ctrl && alt && key >= Key.A && key <= Key.Z)
        {
            var letter = char.ToLowerInvariant((char)('a' + (key - Key.A)));
            var m = _core.Modules().FirstOrDefault(x => x.Key == letter);
            if (m is not null)
            {
                ShowModule(m.Id);
                e.Handled = true;
            }
        }
        else if (key == Key.Space && !ctrl && !alt && _module == "music" && !_exploring && Keyboard.FocusedElement is not TextBox)
        {
            _core.Playback.TogglePlayPause();
            e.Handled = true;
        }
    }

    // ---------------------------------------------------------------- context menu

    private void OnContextMenu(object sender, MouseButtonEventArgs e)
    {
        if (!Expanded)
        {
            return;
        }

        var items = new List<IslandMenu.Item>
        {
            new(_exploring ? Strings.Back : Strings.Explore, ToggleExplore, Glyph: G.Explore),
            new(_pinned ? Strings.Unpin : Strings.Pin, () => { _pinned = !_pinned; BuildOpen(false); }, _pinned, Glyph: G.Pin),
            new(Strings.SettingsEllipsis, () => { _core.Ctx.Shell.OpenSettings(SettingsPages.Island); Collapse(); }, Glyph: G.Gear),
            IslandMenu.Sep(),
            new(Strings.Collapse, () => Collapse(), Glyph: G.ChevronUp),
        };
        IslandMenu.Show(_surface, items, () => HoldOpen());
        e.Handled = true;
    }

    // ---------------------------------------------------------------- file drag (drop placeholder)

    private Rect DropZone(double heightDip) =>
        new(_area.Bounds.X + ((_area.Bounds.Width - (BareWidth * _scale)) / 2) - (60 * _scale), _area.Bounds.Y, (BareWidth * _scale) + (120 * _scale), heightDip * _scale);

    /// <summary>The resting drop zone as an immutable snapshot for the hook thread (called whenever the geometry is published).</summary>
    private void PublishIdleZone()
    {
        var z = DropZone(12);
        _idleZone = new ZoneSnap(z.Left, z.Top, z.Right, z.Bottom);
    }

    private void DetectDrag(Point cursor)
    {
        if (!_core.App.Get<bool>(K.Shelf) || !_core.App.Get<bool>(K.DragReveal) || !_core.ModuleVisible("files") || _mode == IslandMode.Open)
        {
            if (_placeholder && _mode != IslandMode.Open)
            {
                _placeholder = false;
                RefreshClosed();
            }

            return;
        }

        var zone = DropZone(_placeholder ? 90 : 12);
        // a file drag: the press began outside the island (and its zone), the button is still down and the pointer has travelled
        // past the system drag distance; a plain click on the closed island never counts
        var travel = Math.Max(4, GetSystemMetricsNative(68));
        var moved = Math.Abs(cursor.X - _pressX) > travel || Math.Abs(cursor.Y - _pressY) > travel;
        var want = _lmbDown && _pressOutside && moved && zone.Contains(cursor);
        if (want != _placeholder)
        {
            _placeholder = want;
            _dragOver = false;
            RefreshClosed();
            ApplyWindow();
        }
    }

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        _dragOver = true;
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        if (_placeholder)
        {
            RefreshClosed();
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        _dragOver = false;
        _placeholder = false;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            IslandShelf.Add(files);
            _core.Mascot.React("celebrate");
            if (_core.App.Get<bool>(K.Shelf))
            {
                Open("files");
            }
        }
        else
        {
            RefreshClosed();
        }

        e.Handled = true;
    }

    // ---------------------------------------------------------------- quick access buttons

    private readonly Dictionary<string, FrameworkElement> _bubbles = [];

    private (string Glyph, string Title, bool Available, bool Selected) ActionInfo(QuickButton b)
    {
        var id = b.ActionId;
        string title;
        string glyph;
        bool available = true;
        bool selected = false;
        switch (id)
        {
            case "explore":
                title = _exploring ? Strings.Back : Strings.Explore;
                glyph = _exploring ? G.Back : G.Explore;
                selected = _exploring;
                break;
            case "settings":
                title = Strings.SettingsEllipsis;
                glyph = G.Gear;
                break;
            case "pin":
                title = _pinned ? Strings.Unpin : Strings.Pin;
                glyph = _pinned ? G.PinFilled : G.Pin;
                selected = _pinned;
                break;
            default:
                if (id.StartsWith("control.", StringComparison.Ordinal))
                {
                    var c = IslandCatalog.Control(id[8..]);
                    title = c?.Title ?? id;
                    glyph = c?.Glyph ?? G.Dot;
                    available = c is not null && ControlAvailable(c);
                    if (id == "control.keepAwake")
                    {
                        selected = _core.KeepAwake?.IsActive == true;
                        glyph = G.Cup;
                    }
                    else if (id == "control.microphone")
                    {
                        var muted = _core.Audio.MicMuted;
                        title = muted ? Loc.T("Unmute microphone") : Loc.T("Mute microphone");
                        glyph = muted ? G.MicOff : G.Mic;
                        selected = muted;
                    }
                }
                else
                {
                    var m = _core.AllModules().FirstOrDefault(x => x.Id == id);
                    title = m?.Title ?? id;
                    glyph = m?.Glyph ?? G.Dot;
                    available = _core.ModuleVisible(id);
                    selected = !_exploring && _module == id && Expanded;
                }

                break;
        }

        return (glyph, string.IsNullOrEmpty(b.Label) ? title : b.Label, available, selected);
    }

    internal bool ControlAvailable(ControlDef c)
    {
        var rt = _core.Ctx.Runtime;
        if (c.Features.Length > 0 && !rt.AnyAvailable(c.Features))
        {
            return false;
        }

        return c.Id switch
        {
            "music" => _core.ModuleVisible("music"),
            "mixer" => _core.ModuleVisible("mixer"),
            "timer" => _core.ModuleVisible("timer"),
            "calendar" => _core.ModuleVisible("calendar"),
            "speedTest" => _core.ModuleVisible("system") && rt.IsAvailable("monitor-network"),
            "brightness" => rt.IsAvailable("brightness") && _core.App.Get<bool>(K.BrightnessControl),
            _ => true,
        };
    }

    private void BuildQuick()
    {
        _quick.Children.Clear();
        _bubbles.Clear();
        if (_mode != IslandMode.Open)
        {
            return;
        }

        foreach (var b in _layout.Buttons)
        {
            var (glyph, title, available, selected) = ActionInfo(b);
            if (!available)
            {
                continue;
            }

            var button = new HoverButton(() => Activate(b.ActionId))
            {
                Width = 44,
                Height = 44,
                CornerRadius = new CornerRadius(22),
                Background = Brushes.Black,
                BorderBrush = D.White(selected ? 0.6 : 0.16),
                BorderThickness = new Thickness(0.75),
                Fill = Brushes.Black,
                Selected = false,
            };
            var icon = D.I(glyph, 17, selected ? Brushes.White : D.White(0.85));
            icon.FontWeight = FontWeights.Medium;
            button.Child = icon;
            button.Icon = icon;
            button.SelectedForeground = Brushes.White;
            button.IdleForeground = selected ? Brushes.White : D.White(0.85);
            D.Tip(button, title);
            _quick.Children.Add(button);
            _bubbles[b.Id] = button;
        }

        PositionQuick();
        if (D.ReduceMotion)
        {
            return;
        }

        var i = 0;
        foreach (var el in _bubbles.Values)
        {
            var st = new ScaleTransform(0.4, 0.4);
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            el.RenderTransform = st;
            el.Opacity = 0;
            var delay = TimeSpan.FromMilliseconds(110 + (40 * i++));
            var ease = new DoubleAnimation(1, TimeSpan.FromMilliseconds(380)) { BeginTime = delay, EasingFunction = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut } };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, ease);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, ease);
            el.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(260)) { BeginTime = delay });
        }
    }

    private void PositionQuick()
    {
        if (_mode != IslandMode.Open || _bubbles.Count == 0)
        {
            return;
        }

        var left = (_winW - _openW) / 2;
        var right = left + _openW;
        var top = SurfaceTop;
        var bottom = top + _openH;
        foreach (var side in new[] { QuickSide.Left, QuickSide.Right, QuickSide.Bottom })
        {
            var list = _layout.On(side).Where(b => _bubbles.ContainsKey(b.Id)).ToList();
            var n = list.Count;
            for (var i = 0; i < n; i++)
            {
                var el = _bubbles[list[i].Id];
                double cx, cy;
                if (side == QuickSide.Bottom)
                {
                    cx = ((left + right) / 2) + ((i - ((n - 1) / 2.0)) * 54);
                    cy = bottom + 12 + 22;
                }
                else
                {
                    var span = (n - 1) * 54.0;
                    var y0 = Math.Max(top + 22 + 12, Math.Min(top + (IsCapsule ? 12 : 0) + 24, top + (_openH / 2) - (span / 2)));
                    cx = side == QuickSide.Left ? left - 48 : right + 48;
                    cy = y0 + (i * 54);
                }

                Canvas.SetLeft(el, cx - 22);
                Canvas.SetTop(el, cy - 22);
            }
        }
    }

    internal void Activate(string action)
    {
        switch (action)
        {
            case "explore":
                ToggleExplore();
                return;
            case "settings":
                _core.Ctx.Shell.OpenSettings(SettingsPages.Island);
                Collapse();
                return;
            case "pin":
                _pinned = !_pinned;
                BuildOpen(false);
                BuildQuick();
                return;
        }

        if (action.StartsWith("control.", StringComparison.Ordinal))
        {
            RunControl(action[8..]);
            return;
        }

        ShowModule(action);
    }

    /// <summary>Runs a Controls shortcut (tile) — used by the quick buttons and the Controls page.</summary>
    internal void RunControl(string id)
    {
        switch (id)
        {
            case "music":
                ShowModule("music");
                break;
            case "mixer":
                ShowModule("mixer");
                break;
            case "timer":
                ShowModule("timer");
                break;
            case "calendar":
                ShowModule("calendar");
                break;
            case "keepAwake":
                if (_core.KeepAwake is { } ka)
                {
                    if (ka.IsActive)
                    {
                        ka.Deactivate();
                    }
                    else
                    {
                        ka.Activate(null);
                    }

                    Dispatcher.BeginInvoke(() => { BuildOpen(false); });
                }

                break;
            case "microphone":
                _core.Audio.SetMicMuted(!_core.Audio.MicMuted);
                Dispatcher.BeginInvoke(() => BuildOpen(false));
                break;
            case "screenshot":
                _core.Perform(() => _core.InvokeFeatureAction("screenshot", "area", "capture", "screenshot"));
                break;
            case "recording":
                _core.Perform(() => _core.InvokeFeatureAction("screen-recorder", "record", "toggle"));
                break;
            case "speedTest":
                ShowModule("system");
                break;
            case "panel":
                if (_core.App.Get<bool>(K.AppPanel))
                {
                }

                Collapse();
                _core.Ctx.Shell.OpenPanel(PanelTabs.Controls);
                break;
            case "commandBar":
                _core.Perform(() => _core.InvokeFeatureAction("command-bar", "open", "show", "bar"));
                break;
            case "scratchpad":
                if (_core.App.Get<bool>(K.Scratchpad) && _core.ModuleVisible("scratchpad"))
                {
                    ShowModule("scratchpad");
                }
                else
                {
                    _core.Perform(() => _core.InvokeFeatureAction("scratchpad", "open", "new", "show"));
                }

                break;
        }
    }
}

internal sealed record ZoneSnap(double Left, double Top, double Right, double Bottom);

/// <summary>A closed copy of the island on another display (Display = All displays).</summary>
internal sealed class MirrorWindow : OverlayWindow
{
    private readonly IslandWindow _owner;
    private readonly MonitorArea _area;
    private readonly System.Windows.Shapes.Rectangle _view = new();

    public MirrorWindow(IslandWindow owner, MonitorArea area)
    {
        _owner = owner;
        _area = area;
        _view.RadiusX = 12;
        _view.RadiusY = 12;
        // the island is drawn larger by its zoom; the copy keeps the same proportions
        _view.LayoutTransform = Math.Abs(owner.Zoom - 1) < 0.0005 ? Transform.Identity : new ScaleTransform(owner.Zoom, owner.Zoom);
        Content = _view;
        Width = 140;
        Height = 40;
        MouseLeftButtonUp += (_, _) => _owner.MoveHere(_area);
        Loaded += (_, _) => Update();
        DecorativeClock.Subscribe(Update);
        Closed += (_, _) => DecorativeClock.Unsubscribe(Update);
    }

    private void Update()
    {
        var src = _owner.MirrorSource;
        var w = Math.Max(20, src.ActualWidth);
        var h = Math.Max(10, src.ActualHeight);
        _view.Width = w;
        _view.Height = h;
        _view.Fill = new VisualBrush(src) { Stretch = Stretch.None };
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX * _owner.Zoom;
        SetPhysicalBounds((int)(_area.Bounds.X + ((_area.Bounds.Width - (w * scale)) / 2)), (int)_area.Bounds.Y, (int)Math.Ceiling(w * scale), (int)Math.Ceiling(h * scale) + 4);
    }
}
