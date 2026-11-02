// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinKnife.Core;

namespace WinKnife.Features.Capture;

/// <summary>
/// The shared selection overlay ("unified capture chooser"): one frozen window per monitor, area drag, window click, full screen,
/// pixel loupe, scrolling mode, colour picking. Coordinates inside are physical virtual-screen pixels.
/// </summary>
internal sealed class CaptureSession
{
    private static CaptureSession? _current;
    private static readonly Dictionary<int, Int32Rect> LastRegions = new();

    private readonly CaptureRequest _request;
    private readonly List<SelectionWindow> _windows = new();
    private readonly List<MonitorInfo> _monitors;
    private readonly IContributionServices _s;
    private List<WindowInfo> _windowList = new();
    private ScreenFrame? _frame;
    private bool _finished;
    private readonly DispatcherTimer _flashTimer = new() { Interval = TimeSpan.FromMilliseconds(1400) };

    private CaptureSession(CaptureRequest request)
    {
        _request = request;
        _s = CaptureHost.Services;
        _monitors = ScreenGrab.Monitors();
        Tool = request.Tool;
        Scrolling = request.ScrollingOnly;
        LoupeOn = Tool == CaptureTool.Color || _s.App.Get<bool>(K.LoupeStartsOn);
        var zoom = _s.App.Get<bool>(K.LoupeRememberZoom) ? _s.App.Get<double>(K.LoupeLastZoom) : _s.App.Get<double>(K.LoupeDefaultZoom);
        Zoom = Math.Clamp(zoom <= 0 ? 1 : zoom, LoupeMath.MinZoom, LoupeMath.MaxZoom);
        _flashTimer.Tick += (_, _) =>
        {
            _flashTimer.Stop();
            FlashText = null;
            Invalidate();
        };
    }

    public static bool IsActive => _current is not null;

    public CaptureTool Tool { get; private set; }

    public bool Scrolling { get; private set; }

    public bool ScrollingOnly => _request.ScrollingOnly;

    public bool ShowMenu => _request.ShowMenu;

    public bool LoupeOn { get; private set; }

    public double Zoom { get; private set; }

    public string? FlashText { get; private set; }

    public IContributionServices Services => _s;

    public IReadOnlyList<MonitorInfo> Monitors => _monitors;

    public ScreenFrame? Frame => _frame;

    public IReadOnlyList<WindowInfo> WindowList => _windowList;

    public bool Frozen { get; private set; }

    // pointer / drag state (virtual px)
    public bool Dragging { get; private set; }

    public int DragMonitor { get; private set; } = -1;

    public Point DragOrigin { get; private set; }

    public Point Pointer { get; private set; }

    public Rect Selection { get; private set; } = Rect.Empty;

    public WindowInfo? Hover { get; private set; }

    public bool Pending { get; private set; }

    public bool RepeatAvailable(int monitorIndex) => LastRegions.ContainsKey(monitorIndex);

    public Int32Rect? LastRegion(int monitorIndex) => LastRegions.TryGetValue(monitorIndex, out var r) ? r : null;

    /// <summary>Opens the overlay. <paramref name="request"/>.Done is always called exactly once.</summary>
    public static bool Begin(CaptureRequest request)
    {
        if (_current is not null || CaptureHost.SessionOnScreen)
        {
            return false;
        }

        var session = new CaptureSession(request);
        _current = session;
        CaptureHost.SessionOnScreen = true;
        try
        {
            session.Open();
        }
        catch (Exception ex)
        {
            CaptureHost.Log("capture overlay failed", ex);
            session.Finish(new CancelledOutcome());
            QuickToolHud.Show(G.Camera, Strings.CaptureFailed);
        }

        return true;
    }

    public static void CancelCurrent() => _current?.Finish(new CancelledOutcome());

    private bool NeedsFreeze(CaptureTool tool) => tool != CaptureTool.Screenshot || _s.App.Get<bool>(K.Freeze);

    private void Open()
    {
        var ownPid = Environment.ProcessId;
        Frozen = NeedsFreeze(Tool);
        var hideOwn = _s.App.Get<bool>(K.HideOwn);
        if (Frozen)
        {
            _frame = ScreenGrab.CaptureVirtualScreen(Tool == CaptureTool.Screenshot && _s.App.Get<bool>(K.IncludePointer));
        }

        _windowList = ScreenGrab.TopLevelWindows().Where(w => !(hideOwn && w.ProcessId == ownPid)).ToList();
        foreach (var m in _monitors)
        {
            var w = new SelectionWindow(this, m);
            _windows.Add(w);
        }

        foreach (var w in _windows)
        {
            w.Show();
        }

        var cursor = ScreenGrab.CursorPos();
        Pointer = new Point(cursor.X, cursor.Y);
        var active = _windows.FirstOrDefault(w => w.Monitor.Index == ScreenGrab.MonitorAt(cursor.X, cursor.Y).Index) ?? _windows[0];
        active.ActivateAndFocus();
        UpdateHover();
        Invalidate();
    }

    // ---- state changes -----------------------------------------------------------------------------------

    public void Invalidate()
    {
        foreach (var w in _windows)
        {
            w.Refresh();
        }
    }

    public void SetTool(CaptureTool tool)
    {
        if (tool == Tool || _finished)
        {
            return;
        }

        if (!CaptureTools.Installed(_s).Contains(tool) || !ToolsAvailable().Contains(tool))
        {
            return;
        }

        Tool = tool;
        Scrolling = false;
        if (tool == CaptureTool.Color)
        {
            LoupeOn = true;
        }

        if (_frame is null && NeedsFreeze(tool))
        {
            EnsureFrame();
        }

        Invalidate();
    }

    /// <summary>Tools offered by the mode bar (during a recording only the recording tool is offered).</summary>
    public IEnumerable<CaptureTool> ToolsAvailable()
    {
        var recording = CaptureHost.Recorder?.IsRecording == true;
        return CaptureTools.Installed(_s).Where(t => !recording || t == _request.Tool);
    }

    public void ToggleScrolling()
    {
        if (Tool != CaptureTool.Screenshot || _request.ScrollingOnly)
        {
            return;
        }

        Scrolling = !Scrolling;
        Invalidate();
    }

    public void CaptureMonitor(MonitorInfo mon) => CommitMonitor(mon);

    public void ToggleLoupe()
    {
        LoupeOn = !LoupeOn;
        if (LoupeOn && !Frozen)
        {
            EnsureFrame();
        }

        Invalidate();
    }

    public void SetZoom(double zoom)
    {
        Zoom = Math.Clamp(zoom, LoupeMath.MinZoom, LoupeMath.MaxZoom);
        _s.App.Set(K.LoupeLastZoom, Zoom);
        Invalidate();
    }

    public void SetPointer(Point px)
    {
        Pointer = px;
        if (!Dragging)
        {
            UpdateHover();
        }

        Invalidate();
    }

    private void UpdateHover()
    {
        Hover = null;
        if (Dragging || Pending)
        {
            return;
        }

        var x = (int)Pointer.X;
        var y = (int)Pointer.Y;
        foreach (var w in _windowList)
        {
            var b = w.Bounds;
            if (x >= b.X && y >= b.Y && x < b.X + b.Width && y < b.Y + b.Height)
            {
                Hover = w;
                break;
            }
        }
    }

    /// <summary>Hides the overlays, takes a fresh frame of the screen and shows them again (live mode needing pixels).</summary>
    private void EnsureFrame()
    {
        foreach (var w in _windows)
        {
            w.Hide();
        }

        Thread.Sleep(120);
        try
        {
            _frame = ScreenGrab.CaptureVirtualScreen(Tool == CaptureTool.Screenshot && _s.App.Get<bool>(K.IncludePointer));
        }
        catch (Exception ex)
        {
            CaptureHost.Log("frame capture failed", ex);
        }

        foreach (var w in _windows)
        {
            w.Show();
            w.SetFrame(_frame);
        }
    }

    // ---- mouse -------------------------------------------------------------------------------------------

    public void MouseDown(SelectionWindow window, Point px)
    {
        if (Pending || _finished)
        {
            return;
        }

        Pointer = px;
        if (Tool == CaptureTool.Color)
        {
            Dragging = true;
            DragMonitor = window.Monitor.Index;
            Invalidate();
            return;
        }

        Dragging = true;
        DragMonitor = window.Monitor.Index;
        DragOrigin = px;
        Selection = new Rect(px, new Size(0, 0));
        Hover = null;
        Invalidate();
    }

    public void MouseMove(SelectionWindow window, Point px)
    {
        if (Pending || _finished)
        {
            return;
        }

        var previous = Pointer;
        Pointer = px;
        if (!Dragging)
        {
            UpdateHover();
            Invalidate();
            return;
        }

        if (Tool == CaptureTool.Color)
        {
            Invalidate();
            return;
        }

        var mon = _monitors[DragMonitor];
        var bounds = new Rect(mon.Bounds.X, mon.Bounds.Y, mon.Bounds.Width, mon.Bounds.Height);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        var space = Keyboard.IsKeyDown(Key.Space);
        if (space)
        {
            // Space held: move the whole selection without resizing.
            var dx = px.X - previous.X;
            var dy = px.Y - previous.Y;
            var moved = new Rect(Selection.X + dx, Selection.Y + dy, Selection.Width, Selection.Height);
            moved.X = Math.Clamp(moved.X, bounds.Left, Math.Max(bounds.Left, bounds.Right - moved.Width));
            moved.Y = Math.Clamp(moved.Y, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - moved.Height));
            DragOrigin = new Point(DragOrigin.X + (moved.X - Selection.X), DragOrigin.Y + (moved.Y - Selection.Y));
            Selection = moved;
            Invalidate();
            return;
        }

        var cx = Math.Clamp(px.X, bounds.Left, bounds.Right);
        var cy = Math.Clamp(px.Y, bounds.Top, bounds.Bottom);
        var ox = DragOrigin.X;
        var oy = DragOrigin.Y;
        var w = cx - ox;
        var h = cy - oy;
        if (shift)
        {
            var side = Math.Min(Math.Abs(w), Math.Abs(h));
            w = Math.Sign(w == 0 ? 1 : w) * side;
            h = Math.Sign(h == 0 ? 1 : h) * side;
        }

        Rect r;
        if (alt)
        {
            r = new Rect(ox - Math.Abs(w), oy - Math.Abs(h), Math.Abs(w) * 2, Math.Abs(h) * 2);
            r.Intersect(bounds);
        }
        else
        {
            r = new Rect(Math.Min(ox, ox + w), Math.Min(oy, oy + h), Math.Abs(w), Math.Abs(h));
        }

        Selection = r;
        Invalidate();
    }

    public void MouseUp(SelectionWindow window, Point px)
    {
        if (!Dragging || Pending || _finished)
        {
            return;
        }

        Pointer = px;
        Dragging = false;
        if (Tool == CaptureTool.Color)
        {
            PickColorAt(px, finish: true);
            return;
        }

        var travelX = Math.Abs(px.X - DragOrigin.X);
        var travelY = Math.Abs(px.Y - DragOrigin.Y);
        var scale = _monitors[DragMonitor].Scale;
        if (travelX < 4 * scale && travelY < 4 * scale)
        {
            // A click: capture the hovered window (not while scrolling needs a dragged region).
            Selection = Rect.Empty;
            UpdateHover();
            if (!Scrolling && Hover is { } hovered)
            {
                CommitWindow(hovered);
            }
            else
            {
                Invalidate();
            }

            return;
        }

        if (Selection.Width < 2 || Selection.Height < 2)
        {
            Selection = Rect.Empty;
            Invalidate();
            return;
        }

        CommitRect(Selection, DragMonitor);
    }

    // ---- keys --------------------------------------------------------------------------------------------

    public void KeyDown(Key key, ModifierKeys mods, bool repeat)
    {
        if (_finished || Pending)
        {
            return;
        }

        var plain = (mods & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0;
        switch (key)
        {
            case Key.Escape:
                Finish(new CancelledOutcome());
                return;
            case Key.Return or Key.Enter when !repeat:
                if (Scrolling && Tool == CaptureTool.Screenshot)
                {
                    return;
                }

                var cursor = ScreenGrab.CursorPos();
                if (Tool == CaptureTool.Color)
                {
                    PickColorAt(new Point(cursor.X, cursor.Y), finish: true);
                }
                else
                {
                    CommitMonitor(ScreenGrab.MonitorAt(cursor.X, cursor.Y));
                }

                return;
        }

        if (!plain || Dragging && key != Key.Space)
        {
            return;
        }

        switch (key)
        {
            case Key.D1 or Key.NumPad1: SetTool(CaptureTool.Screenshot); break;
            case Key.D2 or Key.NumPad2: SetTool(CaptureTool.Recording); break;
            case Key.D3 or Key.NumPad3: SetTool(CaptureTool.Text); break;
            case Key.D4 or Key.NumPad4: SetTool(CaptureTool.Color); break;
            case Key.S when !repeat: ToggleScrolling(); break;
            case Key.Z when !repeat: ToggleLoupe(); break;
            case Key.R when !repeat && Tool != CaptureTool.Color: RepeatLast(); break;
            case Key.C when !repeat && LoupeOn: CopyLoupeColor(); break;
            case Key.Left or Key.Right or Key.Up or Key.Down when LoupeOn:
                Nudge(key, mods.HasFlag(ModifierKeys.Shift) ? 10 : 1);
                break;
        }
    }

    private void Nudge(Key key, int step)
    {
        var (x, y) = ScreenGrab.CursorPos();
        var dx = key == Key.Left ? -step : key == Key.Right ? step : 0;
        var dy = key == Key.Up ? -step : key == Key.Down ? step : 0;
        var v = ScreenGrab.VirtualScreen();
        x = Math.Clamp(x + dx, v.X, v.X + v.Width - 1);
        y = Math.Clamp(y + dy, v.Y, v.Y + v.Height - 1);
        N.SetCursorPos(x, y);
        SetPointer(new Point(x, y));
        var win = _windows.FirstOrDefault(w => w.Monitor.Index == ScreenGrab.MonitorAt(x, y).Index);
        if (win is not null && !win.IsActive)
        {
            win.ActivateAndFocus();
        }
    }

    public void Wheel(int delta, bool alt)
    {
        if (!LoupeOn)
        {
            return;
        }

        var stepped = _s.App.Get<bool>(K.LoupeStepped) ^ alt;
        if (stepped)
        {
            var side = LoupeMath.SampleSide(Zoom) + (delta > 0 ? -2 : 2);
            side = Math.Clamp(side, 3, LoupeMath.SampleSide(LoupeMath.MinZoom));
            SetZoom(side <= 3 ? LoupeMath.MaxZoom : 13.0 / side);
            return;
        }

        var notches = Math.Abs(delta) >= 120 ? 3 : 1;
        var factor = Math.Pow(1.15, notches);
        SetZoom(delta > 0 ? Zoom * factor : Zoom / factor);
    }

    // ---- colour ------------------------------------------------------------------------------------------

    public ColorValue? SampleColor(Point px)
    {
        if (_frame is null)
        {
            return null;
        }

        var c = _frame.PixelAt((int)px.X - _frame.X, (int)px.Y - _frame.Y);
        return c is { } col ? ColorValue.FromBytes(col.R, col.G, col.B) : null;
    }

    public string FormatColor(ColorValue c) =>
        c.ToText(_s.App.Get<string>(K.ColorFormat) ?? "hex", _s.App.Get<bool>(K.ColorBareHex));

    private void PickColorAt(Point px, bool finish)
    {
        var c = SampleColor(px);
        if (c is null)
        {
            Finish(new CancelledOutcome());
            QuickToolHud.Show(G.Camera, Strings.CaptureFailed);
            return;
        }

        if (finish)
        {
            Finish(new ColorOutcome(c.Value, (int)px.X, (int)px.Y));
        }
    }

    private void CopyLoupeColor()
    {
        var c = SampleColor(Pointer);
        if (c is null)
        {
            return;
        }

        var text = FormatColor(c.Value);
        Output.CopyText(text);
        FlashText = "✓ " + text;
        _flashTimer.Stop();
        _flashTimer.Start();
        Invalidate();
    }

    // ---- commits -----------------------------------------------------------------------------------------

    private void RepeatLast()
    {
        var (x, y) = ScreenGrab.CursorPos();
        var mon = ScreenGrab.MonitorAt(x, y);
        if (LastRegions.TryGetValue(mon.Index, out var r))
        {
            var rect = new Rect(mon.Bounds.X + r.X, mon.Bounds.Y + r.Y, r.Width, r.Height);
            CommitRect(rect, mon.Index, remember: false);
        }
    }

    private void CommitMonitor(MonitorInfo mon)
    {
        var rect = new Rect(mon.Bounds.X, mon.Bounds.Y, mon.Bounds.Width, mon.Bounds.Height);
        CommitRect(rect, mon.Index, remember: false);
    }

    private void CommitWindow(WindowInfo window)
    {
        var center = new Point(window.Bounds.X + window.Bounds.Width / 2.0, window.Bounds.Y + window.Bounds.Height / 2.0);
        var mon = ScreenGrab.MonitorAt((int)center.X, (int)center.Y);
        var screen = Intersect(window.Bounds, mon.Bounds);
        var pick = new RegionPick(mon, new Int32Rect(screen.X - mon.Bounds.X, screen.Y - mon.Bounds.Y, screen.Width, screen.Height), screen, window.Handle, window.ProcessId);
        if (Tool == CaptureTool.Recording)
        {
            Finish(new GeometryOutcome(pick, Tool, false));
            return;
        }

        Pending = true;
        Hover = null;
        Invalidate();
        var tool = Tool;
        var includePointer = tool == CaptureTool.Screenshot && _s.App.Get<bool>(K.IncludePointer);
        var frame = _frame;
        var scale = mon.Scale;
        Finish(null, () =>
        {
            BitmapSource? image = null;
            if (!window.Bounds.IsEmpty && window.Handle != 0)
            {
                image = ScreenGrab.CaptureWindow(window, includePointer);
            }

            image ??= frame is not null ? frame.Crop(new Int32Rect(window.Bounds.X - frame.X, window.Bounds.Y - frame.Y, window.Bounds.Width, window.Bounds.Height)) : ScreenGrab.CaptureRect(window.Bounds, includePointer);
            return new PixelsOutcome(new Capture(image, scale, window.Bounds), tool);
        });
    }

    private void CommitRect(Rect rect, int monitorIndex, bool remember = true)
    {
        var mon = _monitors[monitorIndex];
        var screen = new Int32Rect((int)Math.Round(rect.X), (int)Math.Round(rect.Y), (int)Math.Round(rect.Width), (int)Math.Round(rect.Height));
        screen = Intersect(screen, mon.Bounds);
        if (remember)
        {
            LastRegions[monitorIndex] = new Int32Rect(screen.X - mon.Bounds.X, screen.Y - mon.Bounds.Y, screen.Width, screen.Height);
        }

        var tool = Tool;
        if (tool == CaptureTool.Recording || Scrolling)
        {
            if (tool == CaptureTool.Recording)
            {
                screen = RecorderSupport.SnapRect(screen, mon.Bounds);
            }

            var pick = new RegionPick(mon, new Int32Rect(screen.X - mon.Bounds.X, screen.Y - mon.Bounds.Y, screen.Width, screen.Height), screen, 0, 0);
            Finish(new GeometryOutcome(pick, tool, Scrolling));
            return;
        }

        Pending = true;
        Invalidate();
        var frame = _frame;
        var includePointer = tool == CaptureTool.Screenshot && _s.App.Get<bool>(K.IncludePointer);
        var scale = mon.Scale;
        Finish(null, () =>
        {
            BitmapSource image = frame is not null
                ? frame.Crop(new Int32Rect(screen.X - frame.X, screen.Y - frame.Y, screen.Width, screen.Height))
                : ScreenGrab.CaptureRect(screen, includePointer);
            return new PixelsOutcome(new Capture(image, scale, screen), tool);
        });
    }

    private static Int32Rect Intersect(Int32Rect a, Int32Rect b)
    {
        var x1 = Math.Max(a.X, b.X);
        var y1 = Math.Max(a.Y, b.Y);
        var x2 = Math.Min(a.X + a.Width, b.X + b.Width);
        var y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);
        return new Int32Rect(x1, y1, Math.Max(1, x2 - x1), Math.Max(1, y2 - y1));
    }

    /// <summary>Closes every window and reports. With <paramref name="deferred"/> the outcome is produced after the overlays left the screen.</summary>
    private void Finish(CaptureOutcome? outcome, Func<CaptureOutcome>? deferred = null)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        _flashTimer.Stop();
        foreach (var w in _windows)
        {
            w.CloseOverlay();
        }

        _windows.Clear();
        if (ReferenceEquals(_current, this))
        {
            _current = null;
        }

        CaptureHost.SessionOnScreen = false;
        var request = _request;
        var live = !Frozen;
        CaptureHost.Ui.BeginInvoke(DispatcherPriority.Background, () =>
        {
            CaptureOutcome result;
            try
            {
                if (deferred is not null)
                {
                    if (live)
                    {
                        Thread.Sleep(120);
                    }

                    result = deferred();
                }
                else
                {
                    result = outcome ?? new CancelledOutcome();
                }
            }
            catch (Exception ex)
            {
                CaptureHost.Log("capture failed", ex);
                QuickToolHud.Show(G.Camera, Strings.CaptureFailed);
                result = new CancelledOutcome();
            }

            request.Done(result);
        });
    }
}
