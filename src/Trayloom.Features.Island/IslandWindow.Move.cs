// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Island;

/// <summary>
/// Moving the island: dragging the closed island (a ghost follows the pointer and shows the snapped place, the island itself stays put, dimmed,
/// until the drop) and the "Move island" mode (a closed view with Reset and Done buttons, for hover modes, hidden-until-hover and a locked
/// position). Everything here runs on the UI thread except where noted: the hook thread only sets volatile flags and queues work.
/// </summary>
internal sealed partial class IslandWindow
{
    /// <summary>How visible the island itself stays while its ghost is dragged around.</summary>
    private const double DragDim = 0.35;

    /// <summary>Travel in physical pixels that starts a drag in "Move island" mode (the mode already says what a drag means).</summary>
    private const double MoveModeThreshold = 3;

    /// <summary>A click right after a move or a cancelled move is not a click on the island.</summary>
    private const long ClickSwallowMs = 300;

    /// <summary>"Move island" mode ends by itself after this long without a press on the island.</summary>
    private const long MoveIdleMs = 60_000;

    // ---- Move mode
    private bool _moveMode;
    private volatile bool _moveModeHook;
    private IReadOnlyList<IslandMonitor>? _moveMonitors;
    private DispatcherTimer? _moveWatch;
    private long _moveActivityTick;

    // ---- one press: classified once, on the first poll that sees it
    private bool _pressChecked;
    private bool _pressOnButton;
    private bool _pressConsumed;

    // ---- the drag
    private bool _dragging;
    private bool _draining;
    private volatile bool _dragFlag;
    private volatile bool _cancelRequested;
    private volatile bool _swallowRightUp;
    private IslandGhostWindow? _ghost;
    private IReadOnlyList<IslandMonitor>? _dragMonitors;
    private (IslandMonitor Monitor, IslandPosition Position)? _candidate;
    private double _grabX;
    private double _grabY;
    private double _dragW;
    private double _dragH;
    private long _upSeenAt;
    private long _moveEndedAt = long.MinValue / 2;

    /// <summary>True while the island is in "Move island" mode (buttons instead of the resting view, forced visible, not click-through).</summary>
    internal bool MoveMode => _moveMode;

    // ---------------------------------------------------------------- Move mode

    /// <summary>Enters "Move island" mode (menus, settings, the <c>move</c> action).</summary>
    internal void EnterMoveMode()
    {
        if (_disposed)
        {
            return;
        }

        if (_moveMode)
        {
            MoveActivity();
            return;
        }

        if (!_core.IslandOn || _suppressed || !_core.Session.CanPresent)
        {
            return;
        }

        if (Expanded)
        {
            Collapse(immediate: true);
        }

        // whatever the closed island was showing gives way to the mode's own view
        _picker = false;
        _noticeExpanded = false;
        _placeholder = false;
        _hoverDeadline = null;
        _leaveAt = null;
        if (_mode == IslandMode.Peek)
        {
            _mode = IslandMode.Closed;
        }

        SetEmphasis(false);
        _moveMode = true;
        _moveModeHook = true;
        _moveMonitors = _monitors;
        MoveActivity();
        StartMoveWatch();
        RefreshClosed();
        KeepMoveModeVisible();
        _core.Ctx.Log.Info("move mode: on");
    }

    /// <summary>Leaves "Move island" mode; <paramref name="commit"/> keeps a position already dropped (false abandons a drag still in flight).</summary>
    internal void ExitMoveMode(bool commit = true)
    {
        if (_dragging)
        {
            EndMove(commit);
        }

        if (!_moveMode)
        {
            return;
        }

        _moveMode = false;
        _moveModeHook = false;
        _moveMonitors = null;
        _moveWatch?.Stop();
        _core.Ctx.Log.Info("move mode: off");
        if (!_disposed)
        {
            RefreshClosed();
        }
    }

    /// <summary>
    /// Shutdown: ends a drag in flight (nothing is saved), lets go of the mouse, closes the ghost and leaves Move mode without touching the layers
    /// again. Called after <c>_disposed</c> is set, so the pill stays unpublished.
    /// </summary>
    private void TeardownMove()
    {
        CompositionTarget.Rendering -= OnMoveFrame;
        if (_dragging)
        {
            EndMove(false);
        }

        _draining = false;
        _dragFlag = false;
        _cancelRequested = false;
        _swallowRightUp = false;
        ReleaseDragCapture();
        _ghost?.Close();
        _ghost = null;
        _candidate = null;
        _dragMonitors = null;
        _moveWatch?.Stop();
        _moveWatch = null;
        _moveMode = false;
        _moveModeHook = false;
        _moveMonitors = null;
    }

    /// <summary>The closed view of Move mode, or null while not in it (called at the top of <c>BuildClosed</c>).</summary>
    private ClosedView? MoveModeView()
    {
        if (!_moveMode)
        {
            return null;
        }

        var glyph = D.I(G.Move, 15, D.White(0.85));
        glyph.Margin = new Thickness(0, 0, 8, 0);
        var label = D.T(Loc.T("Drag to move"), 12, FontWeights.Medium, D.White(0.9));
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Margin = new Thickness(0, 0, 10, 0);
        var reset = new MoveModeButton(Loc.T("Reset"), () =>
        {
            MoveActivity();
            ResetPosition();
        }, prominent: false, automationId: "Island.Move.Reset");
        var done = new MoveModeButton(Loc.T("Done"), () => ExitMoveMode(), prominent: true, automationId: "Island.Move.Done") { Margin = new Thickness(4, 0, 0, 0) };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Height = 36,
            Margin = new Thickness(16, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
        };
        row.Children.Add(glyph);
        row.Children.Add(label);
        row.Children.Add(reset);
        row.Children.Add(done);
        return new ClosedView(row, 360);
    }

    /// <summary>The mode forces the island visible and clickable, whatever "hidden until hover" would do (that runs just before this on every poll).</summary>
    private void KeepMoveModeVisible()
    {
        if (_surface.Alpha < 0.99)
        {
            _surface.Alpha = 1;
            _surface.InvalidateVisual();
            if (_choreo == Choreo.None && _fadeIn is null)
            {
                _closedHost.Opacity = 1;
            }
        }

        if (ClickThrough)
        {
            ClickThrough = false;
        }
    }

    private void StartMoveWatch()
    {
        _moveWatch ??= new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TimeSpan.FromMilliseconds(250) };
        _moveWatch.Tick -= MoveWatchTick;
        _moveWatch.Tick += MoveWatchTick;
        _moveWatch.Start();
    }

    /// <summary>
    /// The mode's own watch (the island's poll stops while it is suppressed or off): leaves the mode when the island is suppressed (fullscreen), turned
    /// off, opened some other way, the displays changed, or it has been idle for a minute.
    /// </summary>
    private void MoveWatchTick(object? sender, EventArgs e)
    {
        if (_disposed || !_moveMode)
        {
            _moveWatch?.Stop();
            return;
        }

        if (_suppressed || !_core.IslandOn || Expanded || !SameDisplays(_monitors, _moveMonitors))
        {
            ExitMoveMode(commit: false);
            return;
        }

        if (!_dragging && !_draining && Environment.TickCount64 - Volatile.Read(ref _moveActivityTick) > MoveIdleMs)
        {
            ExitMoveMode();
        }
    }

    private void MoveActivity() => Volatile.Write(ref _moveActivityTick, Environment.TickCount64);

    /// <summary>
    /// True when two reads of the display list describe the same displays: same ids, bounds, work areas and DPI. The list is re-read (a new object
    /// every time) on every work-area notification, which Explorer and appbars broadcast without anything having changed, so identity is no test.
    /// </summary>
    private static bool SameDisplays(IReadOnlyList<IslandMonitor> a, IReadOnlyList<IslandMonitor>? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (b is null || a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!SameDisplay(a[i], b[i]) || !SameRect(a[i].Bounds, b[i].Bounds) || !SameRect(a[i].Work, b[i].Work) || Math.Abs(a[i].Dpi - b[i].Dpi) > 0.001)
            {
                return false;
            }
        }

        return true;
    }

    // ---------------------------------------------------------------- starting a drag

    /// <summary>
    /// Called by the UI poll (40 ms while busy) with the pointer in physical pixels: starts a drag once the button has travelled far enough from where
    /// it went down on the closed island. A press that ends with less travel stays a click (the island opens as usual).
    /// </summary>
    private void PollMove(Point cursor)
    {
        if (_disposed)
        {
            return;
        }

        if (_moveMode)
        {
            KeepMoveModeVisible();
        }

        if (_dragging)
        {
            if (!_lmbDown)
            {
                // the frame handler normally ends the drag; this is the backstop if frames stop coming
                OnMoveFrame(null, EventArgs.Empty);
            }

            return;
        }

        if (_draining)
        {
            // a cancelled drag keeps the pointer captured until the button is up, so the release does not click what is underneath
            if (!_lmbDown && Environment.TickCount64 - _upSeenAt > 400)
            {
                _draining = false;
                ReleaseDragCapture();
            }

            return;
        }

        if (!_lmbDown)
        {
            _pressChecked = false;
            _pressOnButton = false;
            _pressConsumed = false;
            return;
        }

        // only a press the island's own window received counts: over the pill's rectangle the press may belong to another window (a region
        // selector, a pinned image, a share bar, the island's own menu, a transparent corner, a click-through island), which keeps its mouse
        if (!PressOnIsland || _pressConsumed || _suppressed || Expanded || _placeholder)
        {
            return;
        }

        // gesture 1 (drag the closed island) is off while locked; Move mode is the way to move a locked island
        if (!_moveMode && _core.App.Get<bool>(K.PositionLocked))
        {
            return;
        }

        if (!_pressChecked)
        {
            _pressChecked = true;
            _pressOnButton = PressWasOnButton();
        }

        if (_pressOnButton)
        {
            return;
        }

        var threshold = _moveMode ? MoveModeThreshold : Math.Max(8, 2 * GetSystemMetricsNative(68));
        var dx = cursor.X - _pressX;
        var dy = cursor.Y - _pressY;
        if ((dx * dx) + (dy * dy) >= threshold * threshold)
        {
            BeginMove();
        }
    }

    /// <summary>Did the press that is down begin on a button (or a slider) in the closed view? Those keep their own press; they never drag the island.</summary>
    private bool PressWasOnButton()
    {
        try
        {
            var p = _closedHost.PointFromScreen(new Point(_pressX, _pressY));
            var hit = VisualTreeHelper.HitTest(_closedHost, p);
            for (DependencyObject? d = hit?.VisualHit; d is not null && !ReferenceEquals(d, _closedHost); d = VisualTreeHelper.GetParent(d))
            {
                if (d is ButtonBase or HoverButton or LevelSlider)
                {
                    return true;
                }
            }
        }
        catch (InvalidOperationException)
        {
        }

        return false;
    }

    private void BeginMove()
    {
        var body = SurfaceScreenRect();
        if (body.Width < 1 || body.Height < 1)
        {
            return;
        }

        _pressConsumed = true;
        _dragW = Math.Max(_closedW, 1);
        _dragH = Math.Max(_closedH, 1);

        // the grab point, in island units from the body's centre: it is kept as the pointer crosses displays of another scale
        var s = Math.Max(_scale, 0.1);
        _grabX = (_pressX - (body.Left + (body.Width / 2))) / s;
        _grabY = (_pressY - (body.Top + (body.Height / 2))) / s;

        _hoverDeadline = null;
        SetEmphasis(false);
        _cancelRequested = false;
        _candidate = null;
        _dragMonitors = _monitors;
        _upSeenAt = 0;
        MoveActivity();

        try
        {
            _ghost = new IslandGhostWindow(_closedHost, _dragW, _dragH, excludeFromCapture: !_core.App.Get<bool>(K.ShowInCaptures));
            _ghost.ShowNoActivate();
        }
        catch (Exception ex)
        {
            _core.Ctx.Log.Warn("move: the preview window failed: " + ex.Message);
            _ghost = null;
            return;
        }

        _dragging = true;
        _dragFlag = true;
        _stage.Opacity = DragDim;
        IslandRects.Pill = null;

        // the press went down over this window: owning the mouse until the release keeps that release from clicking the app underneath
        _closedHost.LostMouseCapture += OnMoveLostCapture;
        _closedHost.PreviewMouseLeftButtonUp += OnMoveButtonUp;
        _closedHost.CaptureMouse();
        CompositionTarget.Rendering += OnMoveFrame;
        UpdateDrag();
        _core.Ctx.Log.Info("move: drag started");
    }

    // ---------------------------------------------------------------- the drag

    /// <summary>One rendering frame of a drag (its own handler, never the island's animation frame): end conditions, then the ghost follows the pointer.</summary>
    private void OnMoveFrame(object? sender, EventArgs e)
    {
        if (!_dragging)
        {
            CompositionTarget.Rendering -= OnMoveFrame;
            return;
        }

        if (_cancelRequested || _disposed || _suppressed || !_core.IslandOn || !SameDisplays(_monitors, _dragMonitors))
        {
            // right click, the island went away, or the displays changed under the drag
            EndMove(false);
            return;
        }

        if (!_lmbDown)
        {
            // the hook saw the release; WPF sees it a moment later: ending after it keeps the capture until then, with a bound in case it never comes
            var now = Environment.TickCount64;
            if (_upSeenAt == 0)
            {
                _upSeenAt = now;
            }

            if (Mouse.LeftButton == MouseButtonState.Released || now - _upSeenAt > 150)
            {
                EndMove(true);
            }

            return;
        }

        UpdateDrag();
    }

    /// <summary>Works out the snapped candidate for the pointer and puts the ghost on its exact rectangle.</summary>
    private void UpdateDrag()
    {
        if (_ghost is null)
        {
            return;
        }

        var cursor = Screens.CursorPosition();
        var ms = _monitors;

        // the display under the pointer sets the scale the grab point is kept in; the candidate may still land on the display next to it
        var under = IslandPlacement.Resolve("pointer", "", ms, (int)cursor.X, (int)cursor.Y);
        var underScale = Math.Max(0.1, under.Dpi * _zoom);
        var cx = cursor.X - (_grabX * underScale);
        var cy = cursor.Y - (_grabY * underScale);
        var candidate = IslandPlacement.FromDrop(ms, cx, cy, _dragW, _dragH, _zoom);
        _candidate = candidate;

        var frame = IslandPlacement.Frame(candidate.Monitor, candidate.Position, _zoom);
        var capsule = IslandSilhouette.DrawsCapsule(ChosenSilhouette, frame.Position);
        var rect = IslandPlacement.PillRectPx(frame, _dragW, _dragH, NearGapOf(capsule));
        _ghost.Place(rect, frame.Scale, capsule, frame.OpensUp);

        // gestures and outside-click stay off for the whole drag (something may have published the pill again in between)
        IslandRects.Pill = null;
    }

    /// <summary>The capsule gap plus the "Capsule fit" drop for a silhouette that is, or is not, a capsule (the notch sits flush).</summary>
    internal double NearGapOf(bool capsule) =>
        capsule ? 2 + Math.Clamp(_core.App.Get<double>(K.CapsuleFitDrop), 0, 20) : 0;

    /// <summary>The release reached the island window: the drag is over and the drop is the last candidate.</summary>
    private void OnMoveButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging)
        {
            EndMove(true);
            e.Handled = true;
        }
        else if (_draining)
        {
            _draining = false;
            _moveEndedAt = Environment.TickCount64;
            ReleaseDragCapture();
            e.Handled = true;
        }
    }

    /// <summary>Safety net: something took the mouse capture away mid-drag. The drag ends where it is and keeps its place.</summary>
    private void OnMoveLostCapture(object sender, MouseEventArgs e)
    {
        if (_dragging)
        {
            EndMove(true);
        }
        else if (_draining)
        {
            _draining = false;
            ReleaseDragCapture();
        }
    }

    /// <summary>Takes the handlers off the closed layer and lets go of the mouse.</summary>
    private void ReleaseDragCapture()
    {
        _closedHost.LostMouseCapture -= OnMoveLostCapture;
        _closedHost.PreviewMouseLeftButtonUp -= OnMoveButtonUp;
        if (_closedHost.IsMouseCaptured)
        {
            _closedHost.ReleaseMouseCapture();
        }
    }

    /// <summary>
    /// Ends the drag: the ghost goes, the island is bright again, and with <paramref name="commit"/> the candidate is saved and applied. A drag
    /// that is cancelled while the button is still down keeps the capture until the button is up (<see cref="_draining"/>).
    /// </summary>
    private void EndMove(bool commit)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        _dragFlag = false;
        CompositionTarget.Rendering -= OnMoveFrame;
        _ghost?.Close();
        _ghost = null;
        _stage.Opacity = 1;
        _moveEndedAt = Environment.TickCount64;
        MoveActivity();

        var candidate = _candidate;
        _candidate = null;
        _dragMonitors = null;
        if (!commit && _lmbDown)
        {
            _draining = true;
            _upSeenAt = Environment.TickCount64;
        }
        else
        {
            ReleaseDragCapture();
        }

        PublishPill();
        _core.Ctx.Log.Info(commit ? "move: drag ended" : "move: drag cancelled");
        if (commit && candidate is { } c && !_disposed)
        {
            CommitMove(c);
        }
    }

    /// <summary>Saves the dropped place (map, plain position and, when it is another display, the display rule) and applies it.</summary>
    private void CommitMove((IslandMonitor Monitor, IslandPosition Position) c)
    {
        try
        {
            if (SameDisplay(c.Monitor, _monitor) && IslandPlacement.Frame(c.Monitor, c.Position, _zoom).Position == _frame.Position)
            {
                return;
            }

            IslandPlacementStore.Save(_core.App, c.Monitor, c.Position, _monitor);
            _core.Ctx.Log.Info($"move: dropped on \"{c.Monitor.Name}\" at {c.Position.Format()}");
            if (ChosenSilhouette == IslandSilhouette.Notch)
            {
                _core.Ctx.Log.Info(IslandPlacement.Frame(c.Monitor, c.Position, _zoom).Position.Attached
                    ? "move: at the edge, the notch shows"
                    : "move: away from the edge, the notch shows as a capsule");
            }

            ApplyDisplay();
            RefreshClosed(snap: true);
        }
        catch (Exception ex)
        {
            _core.Ctx.Log.Warn("move: saving the position failed: " + ex.Message);
        }
    }

    /// <summary>True when a click on the closed island must be ignored (it just ended a move, or Move mode owns the island); called at the top of <c>OnClosedClick</c>.</summary>
    private bool SwallowClosedClick() =>
        _moveMode || _dragging || _draining || Environment.TickCount64 - _moveEndedAt < ClickSwallowMs;

    /// <summary>True while hover must not open or emphasise the island (Move mode).</summary>
    private bool MoveBlocksHover => _moveMode;

    // ---------------------------------------------------------------- hook thread

    /// <summary>
    /// A left press was seen by the mouse hook. HOOK THREAD: only set volatile flags and <c>BeginInvoke</c>, never hold work here.
    /// <paramref name="onPill"/> is a press on the closed or peeking island, <paramref name="outside"/> one outside it and its drop zone.
    /// </summary>
    private void OnMoveHookDown(bool onPill, bool outside)
    {
        if (!_moveModeHook)
        {
            return;
        }

        if (onPill)
        {
            MoveActivity();
        }
        else if (outside)
        {
            // a press elsewhere ends the mode (the press itself goes on to whatever is there)
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                if (_moveMode && !_dragging)
                {
                    ExitMoveMode();
                }
            });
        }
    }

    /// <summary>
    /// Every mouse hook event, before the island's own handling. HOOK THREAD (flags only). Returns true to swallow the event: the right button that
    /// cancels a drag, down and up, so the click does not reach the app underneath or open the island's menu.
    /// </summary>
    private bool OnMoveHook(MouseHookEvent e)
    {
        switch (e.Kind)
        {
            case MouseEventKind.RightDown:
                if (_dragFlag)
                {
                    _swallowRightUp = true;
                    _cancelRequested = true;
                    Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
                    {
                        if (_dragging)
                        {
                            EndMove(false);
                        }
                    });
                    return true;
                }

                _swallowRightUp = false;
                return false;
            case MouseEventKind.RightUp:
                if (_swallowRightUp)
                {
                    _swallowRightUp = false;
                    return true;
                }

                return false;
            default:
                return false;
        }
    }

    // ---------------------------------------------------------------- reset

    /// <summary>Puts the island back at the top centre of its display and the display choice back to automatic (the Reset position actions).</summary>
    internal void ResetPosition()
    {
        IslandPlacementStore.Reset(_core.App);
        ApplyDisplay();
        RefreshClosed(snap: true);
        if (Expanded)
        {
            BuildOpen(rebuildPage: false);
        }
    }
}

/// <summary>
/// A capsule text button of the Move-mode view. A real <see cref="Button"/> (not the island's drawn <c>HoverButton</c>) so that it has an
/// automation peer: it can be found and invoked through UI Automation by its name.
/// </summary>
internal sealed class MoveModeButton : Button
{
    public MoveModeButton(string text, Action onClick, bool prominent, string automationId)
    {
        var label = D.T(text, 12, prominent ? FontWeights.SemiBold : FontWeights.Medium, prominent ? D.Text : D.White(0.7));
        label.VerticalAlignment = VerticalAlignment.Center;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        Content = label;
        Height = 26;
        Padding = new Thickness(13, 0, 13, 0);
        Margin = new Thickness(0);
        Focusable = false;
        Cursor = Cursors.Arrow;
        Background = prominent ? D.White(0.14) : Brushes.Transparent;
        BorderThickness = new Thickness(0);
        VerticalAlignment = VerticalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetName(this, text);
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, automationId);

        var plate = new FrameworkElementFactory(typeof(Border), "plate");
        plate.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        plate.SetValue(Border.CornerRadiusProperty, new CornerRadius(13));
        plate.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        plate.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = plate };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, prominent ? D.White(0.22) : D.Hover, "plate"));
        var pressed = new Trigger { Property = IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(OpacityProperty, 0.8));
        template.Triggers.Add(hover);
        template.Triggers.Add(pressed);
        Template = template;

        Click += (_, _) => onClick();
    }
}
