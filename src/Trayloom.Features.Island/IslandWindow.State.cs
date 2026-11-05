// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Trayloom.Core;

namespace Trayloom.Features.Island;

internal sealed partial class IslandWindow
{
    private ClosedView? _closed;
    private Notice? _shownNotice;
    private bool _noticeExpanded;
    private bool _placeholder;
    private bool _picker;
    private DateTime _trackSince = DateTime.MinValue;
    private string _trackIdentity = "";
    private bool _openedFresh;
    private string _closedKey = "";
    private nint _prevForeground;

    public DateTime TrackSince => _trackSince;

    // ---------------------------------------------------------------- lifecycle

    public void Begin()
    {
        _layout = QuickAccessLayout.Load(_core.App);
        _core.Notices.Changed += OnNotice;
        _core.Playback.Changed += OnPlayback;
        _core.Timer.Changed += RefreshClosedSoon;
        _core.Watch.Changed += RefreshClosedSoon;
        _core.Downloads.Changed += RefreshClosedSoon;
        _core.Mascot.Changed += RefreshClosedSoon;
        _core.ExternalInvalidated += OnExternalInvalidated;
        _core.Session.Changed += OnSessionChanged;
        _core.Calendar.Changed += RefreshClosedSoon;
        _core.App.Changed += OnSetting;
        SystemParameters.StaticPropertyChanged += OnSystemParameter;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        ApplyDisplay();
        RefreshClosed(snap: true);
        ShowNoActivate();
        ApplyCaptureExclusion();
        PlaceOn(_monitor);
        StartPolling();
    }

    public void Shutdown()
    {
        _disposed = true;
        TeardownMove();
        StopPolling();
        _core.Notices.Changed -= OnNotice;
        _core.Playback.Changed -= OnPlayback;
        _core.Timer.Changed -= RefreshClosedSoon;
        _core.Watch.Changed -= RefreshClosedSoon;
        _core.Downloads.Changed -= RefreshClosedSoon;
        _core.Mascot.Changed -= RefreshClosedSoon;
        _core.ExternalInvalidated -= OnExternalInvalidated;
        _core.Session.Changed -= OnSessionChanged;
        _core.Calendar.Changed -= RefreshClosedSoon;
        _core.App.Changed -= OnSetting;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        SystemParameters.StaticPropertyChanged -= OnSystemParameter;
        CompositionTarget.Rendering -= OnRendering;
        _page?.Hidden();
        _page = null;
        TeardownInput();
        IslandRects.Pill = null;
        if (ReferenceEquals(_core.Window, this))
        {
            _core.Window = null;
        }

        _core.SyncSpotifyTimeline();
        Close();
    }

    private readonly HashSet<string> _pendingKeys = [];
    private bool _settingsQueued;

    private void OnSetting(string key)
    {
        if (!key.StartsWith("notch", StringComparison.Ordinal) && !key.StartsWith("mixer", StringComparison.Ordinal))
        {
            return;
        }

        InvalidateStyleCache();
        Interlocked.Increment(ref _frameStamp);

        // several keys change at once (a saved position writes the map, the plain position and maybe the display): one relayout for all of them
        bool queue;
        lock (_pendingKeys)
        {
            _pendingKeys.Add(key);
            queue = !_settingsQueued;
            _settingsQueued = true;
        }

        if (queue)
        {
            Dispatcher.BeginInvoke(ApplyPendingSettings);
        }
    }

    private void ApplyPendingSettings()
    {
        HashSet<string> keys;
        lock (_pendingKeys)
        {
            keys = [.. _pendingKeys];
            _pendingKeys.Clear();
            _settingsQueued = false;
        }

        if (_disposed)
        {
            return;
        }

        bool Has(string k) => keys.Contains(k);
        bool Any(params string[] ks) => ks.Any(keys.Contains);

        if (Has(ZoomKey))
        {
            ApplyZoom();
        }

        if (Has(ZoomKey) || (_mode == IslandMode.Closed && Any(K.Size, K.CustomWidth, K.CustomHeight, K.Silhouette)))
        {
            // the envelope may shrink again: a new size or silhouette is a deliberate change, not an animation
            ResetEnvelope();
        }

        if (Has(K.QuickAccessLayout))
        {
            _layout = QuickAccessLayout.Load(_core.App);
            if (Expanded)
            {
                BuildQuick();
            }
        }

        if (Has(K.ShowInCaptures))
        {
            ApplyCaptureExclusion();
        }

        var placement = Any(K.Display, K.DisplayId, K.Position, K.PositionsByDisplay, K.PositionLocked);
        if (placement || Any(K.Enabled, K.HideInFullscreen))
        {
            ApplyDisplay();
        }

        if (Expanded && (placement || Any(ZoomKey, K.Size, K.CustomWidth, K.CustomHeight, K.Silhouette, K.ModuleOrder, K.HiddenModules)))
        {
            BuildOpen(rebuildPage: false);
        }

        RefreshClosed(snap: placement);
        if (Any(ZoomKey, K.CapsuleFitWidth, K.CapsuleFitHeight, K.CapsuleFitDrop, K.Silhouette))
        {
            PlaceOn(_monitor);
        }

        UpdateSurface();
    }

    private void ApplyCaptureExclusion()
    {
        try
        {
            IslandNative.SetWindowDisplayAffinity(Handle, _core.App.Get<bool>(K.ShowInCaptures) ? 0u : 0x11u);
        }
        catch
        {
        }
    }

    private void OnSessionChanged()
    {
        if (!_core.Session.CanPresent && Expanded)
        {
            Collapse(immediate: true);
        }

        UpdateVisibility();
        RefreshClosed();
    }

    // ---------------------------------------------------------------- closed content

    private void RefreshClosedSoon() => Dispatcher.BeginInvoke(DispatcherPriority.Background, () => RefreshClosed());

    private void OnExternalInvalidated()
    {
        _core.ResetStrips();
        _core.PollExternalNotices();
        if (_mode == IslandMode.Open && _choreo == Choreo.None)
        {
            // The closed layer is hidden behind the open page; Collapse rebuilds it before it returns, so skip the work now.
            return;
        }

        RefreshClosed();
    }

    private void OnNotice()
    {
        _noticeExpanded = false;
        RefreshClosed();
    }

    private void OnPlayback()
    {
        var p = _core.Playback.Current;
        if (p.HasTrack && p.SongKey != _trackIdentity && p.IsPlaying)
        {
            // SongKey, not Identity: a length the Web API adds or drops (Spotify, island opened or closed) is not a new song
            _trackIdentity = p.SongKey;
            _trackSince = DateTime.UtcNow;
        }

        RefreshClosed();
    }

    public void ReMeasureClosed()
    {
        if (_closed is null || _mode == IslandMode.Open)
        {
            return;
        }

        RefreshClosed(rebuild: false);
    }

    /// <summary>Rebuilds the closed layer (resting, notice, strip, peek, picker, placeholder) and sizes the island to it.</summary>
    public void RefreshClosed(bool snap = false, bool rebuild = true)
    {
        if (_disposed)
        {
            return;
        }

        var steady = false;
        if (rebuild || _closed is null)
        {
            var hadContent = _closed is not null;
            var previousKey = _closedKey;
            _closed = BuildClosed();
            _closedHost.Children.Clear();
            _closed.Element.HorizontalAlignment = HorizontalAlignment.Center;
            _closed.Element.VerticalAlignment = VerticalAlignment.Center;
            _closedHost.Children.Add(_closed.Element);
            _closedHost.Background = Brushes.Transparent;
            // the same kind of content with a new reading eases its width; other content cross-fades in (spec 5: replace 0.18 s)
            steady = hadContent && previousKey == _closedKey && _mode != IslandMode.Open;
            if (hadContent && previousKey != _closedKey && _mode != IslandMode.Open && _choreo == Choreo.None && !snap && !D.ReduceMotion)
            {
                _fadeIn = _closed.Element;
                _fadeT = 0;
                _fadeIn.Opacity = 0;
                StartRendering();
            }
            else if (_fadeIn is not null && !ReferenceEquals(_fadeIn, _closed.Element))
            {
                _fadeIn = null;
            }
        }

        var body = BodyHeight;
        var view = _closed;
        FrameworkElement el = view.Element;
        el.Width = double.NaN;
        el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var desired = el.DesiredSize.Width;
        var width = view.Fill ? view.MaxWidth : Math.Clamp(desired, 0, view.MaxWidth) + 2;
        width = Math.Max(width, Math.Max(view.MinWidth, BareWidth));
        if (view.Fill)
        {
            el.Width = width;
        }

        width = Math.Min(width, ScreenWidthDip - 24);
        var height = _placeholder ? 66 : (el.DesiredSize.Height > body + 1 ? el.DesiredSize.Height : body);
        if (_picker || _mode == IslandMode.Peek || _noticeExpanded)
        {
            height = Math.Max(height, el.DesiredSize.Height);
        }

        _closedW = width;
        _closedH = height;
        if (_hoverEmphasis && !_picker && _mode == IslandMode.Closed)
        {
            width += IsCapsule ? 10 : 20;
            height += IsCapsule ? 0 : 5;
        }

        if (_mode != IslandMode.Open)
        {
            SetTarget(width, height, snap, steady);
            if (_choreo == Choreo.None)
            {
                _closedHost.Opacity = _surface.Alpha;
            }

            _closedHost.IsHitTestVisible = true;
            SyncClosedVisibility();
        }

        LayoutHosts();
        ApplyHiddenAlpha();
        PublishPill();
    }

    private ClosedView BuildClosed()
    {
        if (MoveModeView() is { } moveView)
        {
            _closedKey = "move";
            return moveView;
        }

        var body = BodyHeight;
        if (_placeholder)
        {
            _closedKey = "placeholder";
            return BuildPlaceholder();
        }

        if (_core.Notices.Current is { } n && _mode != IslandMode.Peek)
        {
            _shownNotice = n;
            if (_noticeExpanded && n.Banner)
            {
                _closedKey = "noticePreview:" + n.Event.Id;
                return BuildNoticePreview(n);
            }

            _closedKey = "notice:" + n.Event.Id;
            return ClosedViews.NoticeView(_core, this, n);
        }

        _shownNotice = null;
        if (_mode == IslandMode.Peek)
        {
            _closedKey = "peek";
            return BuildPeek();
        }

        var live = _core.LiveActivities();
        if (_picker && live.Count > 1)
        {
            _closedKey = "picker";
            return BuildPicker(live);
        }

        var act = _core.CurrentActivity();
        if (act is not null)
        {
            var v = BuildActivity(act);
            if (v is not null)
            {
                _closedKey = "act:" + act;
                return v;
            }
        }

        _closedKey = "rest";
        return ClosedViews.Resting(_core, this);
    }

    private ClosedView? BuildActivity(string act) => act switch
    {
        IslandCore.ActTimer => ClosedViews.TimerStrip(_core, this),
        IslandCore.ActWatch => ClosedViews.WatchStrip(_core, this),
        IslandCore.ActDownloads => ClosedViews.DownloadStrip(_core, this),
        IslandCore.ActAgents => ClosedViews.ExternalStrip(_core, this, "agents"),
        IslandCore.ActCalendar => ClosedViews.CalendarStrip(_core, this),
        IslandCore.ActMusic => ClosedViews.MusicStrip(_core, this),
        IslandCore.ActKeepAwake => ClosedViews.KeepAwakeStrip(_core, this),
        _ => ClosedViews.ExternalStrip(_core, this, act),
    };

    internal static string ModuleForActivity(string act) => act switch
    {
        IslandCore.ActTimer => "timer",
        IslandCore.ActWatch => "watch",
        IslandCore.ActDownloads => "downloads",
        IslandCore.ActAgents => "agents",
        IslandCore.ActCalendar => "calendar",
        IslandCore.ActMusic => "music",
        IslandCore.ActKeepAwake => "controls",
        IslandCore.ActMicrophone => "controls",
        _ => act,
    };

    private ClosedView BuildPlaceholder()
    {
        var w = Math.Min(ScreenWidthDip - 24, Math.Max((CameraWidth * BodyHeight / BarHeight) + 110, 340));
        var grid = new Grid { Width = w, Height = 66, Background = Brushes.Transparent };
        var dash = new System.Windows.Shapes.Rectangle
        {
            RadiusX = 16,
            RadiusY = 16,
            Stroke = D.White(_dragOver ? 0.6 : 0.24),
            StrokeThickness = 1,
            StrokeDashArray = [5, 4],
            Margin = new Thickness(8),
        };
        var text = D.T(Strings.DropHint, 13, FontWeights.Medium, D.Text);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(dash);
        grid.Children.Add(text);
        return new ClosedView(grid, w, w, true);
    }

    private ClosedView BuildPeek()
    {
        var w = Math.Min(ScreenWidthDip - 24, Math.Max((CameraWidth * BodyHeight / BarHeight) + 110, 340));
        var h = (IsCapsule ? 16 : BodyHeight + 10) + 52 - (IsCapsule ? 8 : 0);
        var grid = new Grid { Width = w, Height = h };

        // the row sits on the far side of the pill: below it for a top island, above it for one resting on the bottom edge
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = OpensUp ? VerticalAlignment.Top : VerticalAlignment.Bottom,
            Margin = OpensUp ? new Thickness(16, 10, 16, 0) : new Thickness(16, 0, 16, 10),
            Height = 36,
        };
        var p = _core.Playback.Current;
        var mod = _core.Modules().FirstOrDefault(m => m.Id == _module) ?? _core.Modules().FirstOrDefault();
        if (p.HasTrack && _core.ModuleVisible("music"))
        {
            row.Children.Add(Cover.For(p, 34));
            var texts = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Width = w - 120 };
            texts.Children.Add(D.T(p.Display, 13, FontWeights.SemiBold, D.Text, false, TextTrimming.CharacterEllipsis));
            texts.Children.Add(D.T(p.Subtitle, 11, null, D.Secondary, false, TextTrimming.CharacterEllipsis));
            row.Children.Add(texts);
        }
        else if (mod is not null)
        {
            var tile = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(mod.Tint), Child = D.I(mod.Glyph, 16, mod.DarkGlyph ? D.White(0.75) : Brushes.White) };
            row.Children.Add(tile);
            var t = D.T(mod.Title, 13, FontWeights.SemiBold);
            t.Margin = new Thickness(10, 0, 0, 0);
            t.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(t);
        }

        grid.Children.Add(row);
        return new ClosedView(grid, w, w, true);
    }

    private ClosedView BuildPicker(List<string> live)
    {
        var current = _core.CurrentActivity() ?? live[0];
        var strip = BuildActivity(current) ?? ClosedViews.Resting(_core, this);
        var w = Math.Min(ScreenWidthDip - 24, Math.Max(280, Math.Min(live.Count, 3) * 110 + 48));
        var panel = new StackPanel { Width = w };
        var stripHost = new Grid { Height = BodyHeight, HorizontalAlignment = HorizontalAlignment.Center };
        stripHost.Children.Add(strip.Element);
        panel.Children.Add(stripHost);
        var chips = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(24, 12, 24, 4) };
        foreach (var a in live)
        {
            var mod = ModuleForActivity(a);
            var def = _core.AllModules().FirstOrDefault(m => m.Id == mod);
            var title = a == IslandCore.ActKeepAwake ? Loc.T("Keep awake") : (a == IslandCore.ActMicrophone ? Loc.T("Microphone") : (def?.Title ?? a));
            var glyph = a == IslandCore.ActKeepAwake ? G.Cup : (a == IslandCore.ActMicrophone ? G.Mic : (def?.Glyph ?? G.Dot));
            var selected = a == current;
            var chip = new HoverButton(() => { _core.PickedActivity = a; RefreshClosed(); }) { Height = 32, CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(3), Fill = selected ? Brushes.White : D.White(0.12) };
            var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(D.I(glyph, 12, selected ? Brushes.Black : D.Text));
            var label = D.T(title, 12, FontWeights.Medium, selected ? Brushes.Black : D.Text);
            label.Margin = new Thickness(6, 0, 0, 0);
            content.Children.Add(label);
            chip.Child = content;
            chips.Children.Add(chip);
        }

        var combine = BuildCombine(live);
        if (OpensUp)
        {
            // mirrored: the strip stays on the near (bottom) side, the chips and Combine grow upward away from the edge
            panel.Children.Clear();
            chips.Margin = new Thickness(24, 4, 24, 12);
            if (combine is not null)
            {
                combine.Margin = new Thickness(0, 0, 0, 2);
                panel.Children.Add(combine);
            }

            panel.Children.Add(chips);
            panel.Children.Add(stripHost);
            panel.Margin = new Thickness(0, 10, 0, 0);
            return new ClosedView(panel, w, w, true);
        }

        panel.Children.Add(chips);
        if (combine is not null)
        {
            panel.Children.Add(combine);
        }

        panel.Margin = new Thickness(0, 0, 0, 10);
        return new ClosedView(panel, w, w, true);
    }

    private FrameworkElement? BuildCombine(List<string> live)
    {
        if (!live.Contains(IslandCore.ActTimer) && !live.Contains(IslandCore.ActCalendar))
        {
            return null;
        }

        var b = new HoverButton(() => { })
        {
            Height = 24,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 0, 10, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(D.I(G.Plus, 9, D.White(0.8)));
        var t = D.T(Strings.Combine, 11, FontWeights.Medium, D.White(0.8));
        t.Margin = new Thickness(5, 0, 0, 0);
        row.Children.Add(t);
        b.Child = row;
        return b;
    }

    private ClosedView BuildNoticePreview(Notice n)
    {
        var w = Math.Min(Math.Max(400, (CameraWidth * BodyHeight / BarHeight) + 200), 560);

        // the preview grows away from the edge the island rests on and never past the room there is on that side; the reading order stays,
        // the small margin moves to the near side and the larger one to the far side
        var far = Math.Max(120, FarRoomDip(0));
        var panel = new StackPanel { Width = w, Margin = OpensUp ? new Thickness(0, 14, 0, 0) : new Thickness(0, 0, 0, 14), MaxHeight = far };
        var header = new Grid { Height = 28, Margin = OpensUp ? new Thickness(16, 0, 16, 0) : new Thickness(16, 8, 16, 0) };
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (n.Image is not null)
        {
            left.Children.Add(new Image { Source = n.Image, Width = 26, Height = 26, Margin = new Thickness(0, 0, 8, 0) });
        }

        left.Children.Add(D.T(n.Detail.Length > 0 && n.Banner ? (_core.Notifications.Items.FirstOrDefault()?.App ?? "") : "", 12, FontWeights.SemiBold, D.White(0.7)));
        header.Children.Add(left);
        panel.Children.Add(header);
        var body = new StackPanel { Margin = new Thickness(16, 6, 16, OpensUp ? 8 : 0) };
        body.Children.Add(D.T(n.Title, 14, FontWeights.SemiBold, D.Text, true));
        if (_core.Notifications.Items.FirstOrDefault() is { Subtitle.Length: > 0 } item)
        {
            body.Children.Add(D.T(item.Subtitle, 13, FontWeights.Medium, D.White(0.9), false, TextTrimming.CharacterEllipsis));
        }

        var msg = D.T(n.FullBody ?? n.Detail, 13, null, D.White(0.82), true);
        msg.MaxHeight = Math.Clamp(far - 190, 36, 110);
        msg.TextTrimming = TextTrimming.CharacterEllipsis;
        msg.Margin = new Thickness(0, 6, 0, 0);
        body.Children.Add(msg);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        if (n.OnClick is not null)
        {
            actions.Children.Add(D.PillButton(Loc.T("Open"), () => n.OnClick(), true));
        }

        body.Children.Add(actions);
        panel.Children.Add(body);
        return new ClosedView(panel, w, w, true);
    }

    // ---------------------------------------------------------------- opening / closing

    public string ReopenDestination()
    {
        var modules = _core.Modules();
        string? Pick(string id) => modules.Any(m => m.Id == id) ? id : null;
        if (_core.App.Get<bool>(K.ReturnHome))
        {
            var home = _core.App.Get<string>(K.HomeModule);
            if (home == "explore")
            {
                return "explore";
            }

            if (Pick(home) is { } h)
            {
                return h;
            }
        }
        else if (Pick(_module) is { } last && _openedFresh)
        {
            return last;
        }

        return Pick("controls") ?? modules.FirstOrDefault()?.Id ?? "controls";
    }

    public void Open(string? module = null, bool pinned = false, bool takeFocus = true, bool byHover = false)
    {
        if (!_core.IslandOn || _suppressed || _disposed || !_core.Session.CanPresent)
        {
            return;
        }

        // opening hides the closed layer, which would take the mouse capture of a drag (a wheel notch over the dimmed island, a shortcut) and
        // commit it half way; Move mode likewise does not open by hover (any other open ends the mode, see MoveWatchTick)
        if (_dragging || _draining || (_moveMode && byHover))
        {
            return;
        }

        var destination = module;
        if (destination is null && _core.App.Get<bool>(K.OpensActivity) && _core.CurrentActivity() is { } act)
        {
            destination = ModuleForActivity(act);
        }

        destination ??= ReopenDestination();
        var wasOpen = Expanded;
        var origin = HistoryOrigin();
        _core.Notices.Clear();
        _core.Timer.StopAlarm();
        _picker = false;
        _placeholder = false;
        _mode = IslandMode.Open;
        _pinned |= pinned;
        _openedByHover = byHover && !wasOpen;
        _leaveAt = null;
        _hoverEmphasis = false;
        _hoverDeadline = null;
        if (destination == "explore")
        {
            _exploring = true;
        }
        else
        {
            _exploring = false;
            var target = _core.Modules().Any(m => m.Id == destination) || _core.External(destination) is not null
                ? destination
                : _core.Modules().FirstOrDefault()?.Id ?? "controls";
            if (wasOpen && target != _module)
            {
                // asked from outside while the island is open (a shortcut, a quick button): a step Back can undo
                PushHistory(origin);
            }

            _module = target;
        }

        if (!wasOpen)
        {
            _history.Clear();
        }

        _openedFresh = true;
        if (_core.Clipboard is { } clip)
        {
            clip.PasteTarget = NativeForeground();
        }

        if (!wasOpen)
        {
            ResetOpenLayer();
        }

        BuildOpen(rebuildPage: true);
        if (!wasOpen)
        {
            BeginOpening();
        }

        if (!wasOpen)
        {
            _prevForeground = NativeForeground();
        }

        SetKeyFocus(takeFocus && !byHover);
        _lastChange = DateTime.UtcNow;
        PublishPill();
        _core.SyncSpotifyTimeline();
    }

    private static nint NativeForeground() => Trayloom.Core.Native.NativeMethods.GetForegroundWindow();

    public void Collapse(bool immediate = false)
    {
        if (_mode == IslandMode.Closed && !_placeholder)
        {
            return;
        }

        if (_mode == IslandMode.Open && _holds > 0 && !immediate)
        {
            return;
        }

        var wasOpen = Expanded;
        _mode = IslandMode.Closed;
        _pinned = false;
        _openedByHover = false;
        _exploring = false;
        _placeholder = false;
        _picker = false;
        _leaveAt = null;
        _suppressHover = IsPointerInside();
        _lastChange = DateTime.UtcNow;
        _history.Clear();
        _backSlot = null;
        if (wasOpen)
        {
            _page?.Hidden();
            var old = _page;
            _page = null;
            _ = old;
            var hadFocus = _keyFocus;
            SetKeyFocus(false);
            if (hadFocus && _prevForeground != 0 && _prevForeground != Handle && Trayloom.Core.Native.NativeMethods.GetForegroundWindow() == Handle)
            {
                Trayloom.Core.Native.NativeMethods.SetForegroundWindow(_prevForeground);
            }

            _openHost.IsHitTestVisible = false;
            BeginClosing(immediate);
            _core.Playback.Reevaluate();
        }

        RefreshClosed(snap: immediate);
        ApplyWindow();
        PublishPill();
        _core.SyncSpotifyTimeline();
    }

    public void Toggle()
    {
        if (Expanded)
        {
            Collapse();
        }
        else
        {
            Open(takeFocus: true);
        }
    }

    public void ToggleExplore()
    {
        if (!Expanded)
        {
            Open("explore");
            return;
        }

        _exploring = !_exploring;
        BuildOpen(rebuildPage: true);
    }

    /// <summary>Esc: close a page layer, then a detail, then Explore, then the island.</summary>
    public void StepBack()
    {
        if (_page is { } p && p.StepBack())
        {
            return;
        }

        if (_exploring)
        {
            _exploring = false;
            BuildOpen(rebuildPage: true);
            return;
        }

        Collapse();
    }

    public void ShowModule(string id)
    {
        if (!Expanded)
        {
            Open(id);
            return;
        }

        if (id != _module)
        {
            PushHistory(HistoryOrigin());
        }

        _exploring = false;
        _module = id;
        BuildOpen(rebuildPage: true);
    }

    // ---------------------------------------------------------------- navigation (Back / Close marks in the top row)

    private const int HistoryCap = 10;
    private const string ExploreLocation = "explore";
    private readonly List<string> _history = [];
    private FrameworkElement? _backSlot;
    private bool _backReserved;
    private bool _backVisible;
    private bool _navStatic;

    /// <summary>The pages visited from inside the open island, oldest first (module ids or "explore"); cleared when the island closes.</summary>
    internal IReadOnlyList<string> NavigationHistory => _history;

    /// <summary>Whether the Back button is showing right now.</summary>
    internal bool BackVisible => _backVisible;

    /// <summary>The page the island goes back to at the end: the home page.</summary>
    internal string HomeLocation()
    {
        var modules = _core.Modules();
        if (_core.App.Get<bool>(K.ReturnHome))
        {
            var home = _core.App.Get<string>(K.HomeModule);
            if (home == ExploreLocation)
            {
                return ExploreLocation;
            }

            if (modules.Any(m => m.Id == home))
            {
                return home;
            }
        }

        return modules.Any(m => m.Id == "controls") ? "controls" : modules.FirstOrDefault()?.Id ?? "controls";
    }

    /// <summary>Where the person is now, for the history: Explore only when Explore is the home page (otherwise it is an overlay on the module).</summary>
    private string HistoryOrigin() => _exploring && HomeLocation() == ExploreLocation ? ExploreLocation : _module;

    private void PushHistory(string location)
    {
        if (location.Length == 0 || (_history.Count > 0 && _history[^1] == location))
        {
            return;
        }

        _history.Add(location);
        if (_history.Count > HistoryCap)
        {
            _history.RemoveAt(0);
        }
    }

    private bool LocationExists(string location) =>
        location == ExploreLocation || _core.Modules().Any(m => m.Id == location) || _core.External(location) is not null;

    /// <summary>The closest earlier page that still exists, or null (pages removed from the island meanwhile are skipped).</summary>
    private string? PreviousLocation()
    {
        for (var i = _history.Count - 1; i >= 0; i--)
        {
            if (LocationExists(_history[i]))
            {
                return _history[i];
            }
        }

        return null;
    }

    /// <summary>Whether Back has somewhere to go: an inner layer, Explore, an earlier page, or the home page.</summary>
    internal bool CanGoBack()
    {
        if (_mode != IslandMode.Open)
        {
            return false;
        }

        return _page is { CanStepBack: true } || StaticCanGoBack();
    }

    /// <summary>The part of Back that does not depend on the page's inner layers: Explore, an earlier page, or the home page.</summary>
    private bool StaticCanGoBack()
    {
        var home = HomeLocation();
        if (_exploring)
        {
            return home != ExploreLocation || PreviousLocation() is not null;
        }

        return PreviousLocation() is not null || _module != home;
    }

    /// <summary>
    /// The Back button and Backspace: close the page's inner layer, else leave Explore, else the previous page, else the home page.
    /// Unlike Esc, it never closes the island.
    /// </summary>
    internal void GoBack()
    {
        if (_mode != IslandMode.Open)
        {
            return;
        }

        if (!CanGoBack())
        {
            return;
        }

        if (_page is { } p && p.StepBack())
        {
            RefreshNav();
            return;
        }

        if (_exploring && HomeLocation() != ExploreLocation)
        {
            _exploring = false;
            BuildOpen(rebuildPage: true);
            return;
        }

        string? target = null;
        while (_history.Count > 0)
        {
            var last = _history[^1];
            _history.RemoveAt(_history.Count - 1);
            if (LocationExists(last) && (last != _module || _exploring))
            {
                target = last;
                break;
            }
        }

        if (target is null)
        {
            var home = HomeLocation();
            if (home == _module && !_exploring)
            {
                return;
            }

            target = home;
        }

        NavigateTo(target);
    }

    /// <summary>Shows a page without recording it in the history (Back and the home fallback).</summary>
    private void NavigateTo(string location)
    {
        if (location == ExploreLocation)
        {
            _exploring = true;
        }
        else
        {
            _exploring = false;
            _module = location;
        }

        BuildOpen(rebuildPage: true);
    }

    /// <summary>Keeps the Back button in step with the page (an inner layer opening or closing) without rebuilding the row.</summary>
    internal void RefreshNav()
    {
        if (_backSlot is null || _mode != IslandMode.Open)
        {
            return;
        }

        // every 40 ms while open: only the page's own state is read, the rest was settled when the row was built
        var show = _page is { CanStepBack: true } || _navStatic;
        if (show == _backVisible)
        {
            return;
        }

        if (show && !_backReserved)
        {
            // a page that never announced layers opened one: rebuild the row once so the button has its place
            BuildOpen(rebuildPage: false);
            return;
        }

        SetBackVisible(show, animate: true);
    }

    private void SetBackVisible(bool show, bool animate)
    {
        _backVisible = show;
        if (_backSlot is not { } slot)
        {
            return;
        }

        slot.IsHitTestVisible = show;
        slot.BeginAnimation(OpacityProperty, null);
        if (!animate || D.ReduceMotion)
        {
            slot.Opacity = show ? 1 : 0;
            return;
        }

        slot.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private HoverButton NavButton(string glyph, string title, Action onClick, double size, double glyphSize)
    {
        var b = new HoverButton(onClick)
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Fill = D.White(0.10),
            HoverFill = D.White(0.18),
            Lifts = false,
        };
        var icon = D.I(glyph, glyphSize, D.White(0.78));
        b.Child = icon;
        b.Icon = icon;
        b.IdleForeground = D.White(0.78);
        b.SelectedForeground = D.Text;
        D.Tip(b, title);
        return b;
    }

    /// <summary>The Back button in a slot that keeps its room (fully transparent and inert while there is nowhere to go back to).</summary>
    private FrameworkElement BuildBackSlot(double size, double glyphSize)
    {
        var slot = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        slot.Children.Add(NavButton(G.ChevronLeft, Strings.Back, GoBack, size, glyphSize));
        _backSlot = slot;
        _backReserved = true;
        _navStatic = StaticCanGoBack();
        SetBackVisible(CanGoBack(), animate: false);
        return slot;
    }

    /// <summary>Whether the row keeps room for the Back button: there is somewhere to go back to now, or the page can open a layer later.</summary>
    private bool WantsBackSlot(IslandPage page) => page.HasLayers || CanGoBack();

    private HoverButton BuildCloseButton(double size, double glyphSize) => NavButton(G.Close, Loc.T("Close"), () => Collapse(), size, glyphSize);

    /// <summary>The marks for a page without a top row: two slim buttons in the top corners, clear of the page's first row.</summary>
    private FrameworkElement BuildCornerMarks(IslandPage page, double topInset)
    {
        const double size = 22;
        var top = topInset > 0 ? 10.0 : 6.0;
        var overlay = new Grid { VerticalAlignment = VerticalAlignment.Top, Height = top + size };
        if (WantsBackSlot(page))
        {
            var slot = BuildBackSlot(size, 9);
            slot.HorizontalAlignment = HorizontalAlignment.Left;
            slot.VerticalAlignment = VerticalAlignment.Top;
            slot.Margin = new Thickness(8, top, 0, 0);
            overlay.Children.Add(slot);
        }

        var close = BuildCloseButton(size, 9);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Margin = new Thickness(0, top, 8, 0);
        overlay.Children.Add(close);
        return overlay;
    }

    public IDisposable HoldOpen()
    {
        _holds++;
        _leaveAt = null;
        return new Hold(this);
    }

    private sealed class Hold(IslandWindow owner) : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (_done)
            {
                return;
            }

            _done = true;
            owner._holds = Math.Max(0, owner._holds - 1);
        }
    }

    internal void ReopenPage() => BuildOpen(rebuildPage: true);

    // ---------------------------------------------------------------- open layer

    private double ContentWidthFor(double islandW) => islandW - (2 * 28);

    private (double W, double Budget, double Cap) OpenMetrics(IslandPage page)
    {
        var preset = _core.App.Get<string>(K.Size);
        double chosen = preset switch
        {
            "compact" => 480,
            "custom" => Math.Clamp(_core.App.Get<double>(K.CustomWidth), 360, 600),
            _ => 560,
        };
        if (preset != "custom" && page.NaturalWidth is { } nw)
        {
            chosen = Math.Min(chosen, nw + 56);
        }

        var w = Math.Min(Math.Max(chosen, (CameraWidth * BodyHeight / BarHeight) + 36), ScreenWidthDip - 24 - (2 * SideGutter));
        var bottom = _layout.On(QuickSide.Bottom).Any() ? SideGutter : 0;

        // the room is measured away from the edge the island rests on: the work area minus the near inset, the free margin at the far end and
        // the quick-button row on the far side (the taskbar is not part of the work area, so a page never reaches under it)
        var cap = Math.Max(120, FarRoomDip(bottom));
        var chrome = page.HasTopRow ? (IsCapsule ? 12 : 0) + 64 : 32;
        double budget = preset switch
        {
            "compact" => 180,
            "custom" => Math.Max(120, Math.Clamp(_core.App.Get<double>(K.CustomHeight), 260, 640) - chrome),
            _ => 264,
        };
        if (preset != "custom" && page is not (null))
        {
            budget = page.ModuleId is "mixer" or "calendar" or "clipboard" or "captures" or "files" or "notifications" or "downloads" or "camera" or "scratchpad" or "watch" or "explore" ? budget : Math.Max(budget, 264);
        }

        if (page is not null && page.GrowsBeyondBudget && preset != "custom")
        {
            budget = cap - chrome;
        }

        budget = Math.Min(budget, cap - chrome);
        return (w, budget, cap);
    }

    /// <summary>Builds the open layer (top row, page) for the current module / Explore and sizes the island to it.</summary>
    internal void BuildOpen(bool rebuildPage)
    {
        if (_mode != IslandMode.Open)
        {
            return;
        }

        if (rebuildPage || _page is null)
        {
            _page?.Hidden();
            _page = _exploring ? new ExplorePage(_core, this) : PageFactory.Create(_core, this, _module);
            _page ??= new EmptyPage(_core, this);
        }

        var page = _page;
        var (w, budget, cap) = OpenMetrics(page);
        var contentW = ContentWidthFor(w);
        var ch = Math.Clamp(page.Height(contentW, budget), 40, budget);
        var topInset = IsCapsule ? 12.0 : 0.0;
        var hasRow = page.HasTopRow;
        var h = hasRow ? topInset + 64 + ch : 16 + ch + 16;
        h = Math.Min(h, cap);
        _openW = w;
        _openH = h;
        var root = new Grid { Width = w, Height = h, Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        // page
        var pageTop = hasRow ? topInset + 48 : 16;
        var host = new Grid { Margin = new Thickness(28, pageTop, 28, 16), ClipToBounds = true };
        host.Children.Add(Detach.From(page.View)!);
        root.Children.Add(host);
        // Back and Close are part of the row from the first frame (built here, before the spring starts), never added afterwards
        _backSlot = null;
        _backReserved = false;
        _backVisible = false;
        root.Children.Add(hasRow ? BuildBand(page, topInset, w) : BuildCornerMarks(page, topInset));

        SwapOpenRoot(root, rebuildPage);
        _openHost.Width = w;
        _openHost.Height = h;
        LayoutHosts();
        // the whole page is measured and arranged now, before the first animation frame, never during it
        _openHost.UpdateLayout();
        SetTarget(w, h);
        if (_quick.Children.Count == 0)
        {
            BuildQuick();
        }
        else
        {
            PositionQuick();
        }

        page.Resized(contentW, ch);
        page.Shown();
        PublishPill();
        _core.Watch.SetVisiblePage(_module == "watch");
    }

    private FrameworkElement BuildBand(IslandPage page, double topInset, double w)
    {
        // back | page row (leading, trailing or one filling element) | pin, close
        var band = new Grid { Height = HeaderRow, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(28, topInset + 6, 28, 0) };
        band.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        band.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        band.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (WantsBackSlot(page))
        {
            var slot = BuildBackSlot(28, 12);
            slot.Margin = new Thickness(0, 0, 8, 0);
            band.Children.Add(slot);
        }

        var middle = new Grid();
        Grid.SetColumn(middle, 1);
        band.Children.Add(middle);
        if (page.BandFill is { } fill)
        {
            fill.VerticalAlignment = VerticalAlignment.Center;
            middle.Children.Add(Detach.From(fill)!);
        }
        else
        {
            var leading = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var trailing = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var hasExploreButton = _layout.Buttons.Any(b => b.ActionId == "explore");
            if (_exploring)
            {
                // the Explore page builds its search in Leading; the Back button above leaves Explore
            }
            else if (!hasExploreButton)
            {
                leading.Children.Add(D.IconButton(G.Explore, Strings.Explore, ToggleExplore, size: 28, glyphSize: 12));
            }

            if (page.Leading is { } l)
            {
                if (leading.Children.Count > 0)
                {
                    l.Margin = new Thickness(8, 0, 0, 0);
                }

                leading.Children.Add(Detach.From(l)!);
            }

            if (page.Trailing is { } t)
            {
                trailing.Children.Add(Detach.From(t)!);
            }

            middle.Children.Add(leading);
            middle.Children.Add(trailing);
        }

        var edge = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(edge, 2);
        if (_pinned)
        {
            var pin = D.IconButton(G.PinFilled, Strings.Unpin, () => { _pinned = false; BuildOpen(rebuildPage: false); }, selected: true, size: 28, glyphSize: 12);
            pin.Margin = new Thickness(0, 0, 6, 0);
            edge.Children.Add(pin);
        }

        edge.Children.Add(BuildCloseButton(28, 12));
        band.Children.Add(edge);
        return band;
    }

    // ---------------------------------------------------------------- click on the closed island

    private void OnClosedClick(object sender, MouseButtonEventArgs e)
    {
        if (SwallowClosedClick())
        {
            e.Handled = true;
            return;
        }

        if (_mode == IslandMode.Open || e.Handled)
        {
            return;
        }

        _core.Timer.StopAlarm();
        var n = _shownNotice;
        if (n is not null)
        {
            _core.Notices.Clear();
            if (n.OnClick is not null)
            {
                n.OnClick();
                return;
            }

            Open(n.OpensModule ?? n.Event.OpensModule);
            e.Handled = true;
            return;
        }

        if (_mode == IslandMode.Peek)
        {
            Open(takeFocus: true);
            e.Handled = true;
            return;
        }

        if (_picker)
        {
            Open(takeFocus: true);
            e.Handled = true;
            return;
        }

        Open(takeFocus: true);
        e.Handled = true;
    }
}
