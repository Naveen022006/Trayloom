// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using WinKnife.Core;
using WinKnife.Core.Native;
using static WinKnife.Core.Native.NativeConstants;

namespace WinKnife.Features.WindowTools;

/// <summary>Journaled system changes of the taskbar features (spec 05 G1.3): written before the change, restored on stop and after a crash.</summary>
internal static class TaskbarSystem
{
    private const string AdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    /// <summary>Delays Windows' own hover thumbnails so they do not pop up under the WinKnife panel.</summary>
    public static void HoldNativeHover(IAppSettings app)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AdvancedKey, writable: true);
            if (key is null)
            {
                return;
            }

            if (!app.IsSet(Keys.HoverTimeJournal))
            {
                app.Set(Keys.HoverTimeJournal, key.GetValue("ExtendedUIHoverTime") is int existing ? existing.ToString() : "absent");
            }

            key.SetValue("ExtendedUIHoverTime", 600000, RegistryValueKind.DWord);
            Broadcast();
        }
        catch (Exception)
        {
            // registry not writable: the native flyout may show as well
        }
    }

    /// <summary>Puts the previous value back (also used at start to recover from a crash).</summary>
    public static void RestoreNativeHover(IAppSettings app)
    {
        if (!app.IsSet(Keys.HoverTimeJournal))
        {
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AdvancedKey, writable: true);
            var journal = app.Get<string>(Keys.HoverTimeJournal);
            if (key is not null)
            {
                if (int.TryParse(journal, out var original))
                {
                    key.SetValue("ExtendedUIHoverTime", original, RegistryValueKind.DWord);
                }
                else
                {
                    key.DeleteValue("ExtendedUIHoverTime", throwOnMissingValue: false);
                }
            }

            app.Reset(Keys.HoverTimeJournal);
            Broadcast();
        }
        catch (Exception)
        {
            // leave the journal so the next start retries
        }
    }

    private static void Broadcast()
    {
        WinApi.PostMessageW(0xFFFF, WinApi.WM_SETTINGCHANGE, 0, 0);
    }

    /// <summary>True when the taskbar auto-hides.</summary>
    public static bool IsAutoHide()
    {
        var data = new APPBARDATA { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>() };
        return ((uint)WinApi.SHAppBarMessage(WinApi.ABM_GETSTATE, ref data) & WinApi.ABS_AUTOHIDE) != 0;
    }

    public static void SetAutoHide(bool on)
    {
        var data = new APPBARDATA { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>(), lParam = (nint)(on ? WinApi.ABS_AUTOHIDE : WinApi.ABS_ALWAYSONTOP) };
        WinApi.SHAppBarMessage(WinApi.ABM_SETSTATE, ref data);
    }
}

internal static class TaskbarTrackerHub
{
    /// <summary>The tracker shared by Taskbar Preview and Taskbar clicks (created by whichever starts first).</summary>
    public static TaskbarTracker Get(IFeatureContext context)
    {
        var tracker = context.Services.Get<TaskbarTracker>();
        if (tracker is null)
        {
            tracker = new TaskbarTracker(context.Ui);
            context.Services.Provide(tracker);
        }

        return tracker;
    }
}

/// <summary>Taskbar Preview (spec 05 A2): hover an open app's taskbar button to see its windows, then click the one you want.</summary>
public sealed class TaskbarPreviewFeature : IFeature
{
    private IFeatureContext? _context;
    private TaskbarTracker? _tracker;
    private TaskbarPreviewPanel? _panel;
    private TaskbarButton? _shown;
    private DispatcherTimer? _openTimer;
    private DispatcherTimer? _hideTimer;
    private Action<string>? _changed;
    private bool _running;
    private bool _autohideHeld;
    private bool _wasAutohide;

    public string Id => "dock-preview";

    public void Start(IFeatureContext context)
    {
        _context = context;
        Keys.Register(context.App);
        Ambient.App = context.App;
        TaskbarSystem.RestoreNativeHover(context.App);
        RestoreAutohide(context.App);
        _changed = key =>
        {
            if (key is Keys.DockPreviewEnabled)
            {
                context.Ui.BeginInvoke(Sync);
            }
        };
        context.App.Changed += _changed;
        Sync();
    }

    public void Stop()
    {
        if (_context is not null && _changed is not null)
        {
            _context.App.Changed -= _changed;
        }

        Deactivate();
        _context = null;
    }

    private void Sync()
    {
        if (_context?.App.Get<bool>(Keys.DockPreviewEnabled) == true)
        {
            Activate();
        }
        else
        {
            Deactivate();
        }
    }

    private void Activate()
    {
        var context = _context;
        if (context is null || _running)
        {
            return;
        }

        _running = true;
        WindowUseTracker.Acquire();
        TaskbarSystem.HoldNativeHover(context.App);
        _tracker = TaskbarTrackerHub.Get(context);
        _tracker.Acquire(context.Hooks);
        _tracker.Changed += OnButton;
    }

    private void Deactivate()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        _openTimer?.Stop();
        _hideTimer?.Stop();
        if (_tracker is not null)
        {
            _tracker.Changed -= OnButton;
            _tracker.Release();
        }

        _tracker = null;
        HidePanel();
        WindowUseTracker.Release();
        if (_context is not null)
        {
            TaskbarSystem.RestoreNativeHover(_context.App);
        }
    }

    private void OnButton(TaskbarButton? button)
    {
        var context = _context;
        if (context is null || !_running)
        {
            return;
        }

        if (button is null)
        {
            _openTimer?.Stop();
            ScheduleHide();
            return;
        }

        _hideTimer?.Stop();
        if (_panel is { IsVisible: true } && _shown?.Key == button.Key)
        {
            return;
        }

        var delay = _panel is { IsVisible: true, IsPinned: false } ? 100 : Math.Clamp(context.App.Get<int>(Keys.DockOpenDelay), 200, 900);
        if (_panel is { IsVisible: true, IsPinned: true })
        {
            return;
        }

        _openTimer?.Stop();
        _openTimer = new DispatcherTimer(DispatcherPriority.Input, context.Ui) { Interval = TimeSpan.FromMilliseconds(delay) };
        var target = button;
        _openTimer.Tick += (_, _) =>
        {
            _openTimer?.Stop();
            if (_tracker?.Current?.Key == target.Key)
            {
                ShowFor(_tracker.Current);
            }
        };
        _openTimer.Start();
    }

    private void ScheduleHide()
    {
        var context = _context;
        if (context is null || _panel is not { IsVisible: true })
        {
            return;
        }

        _hideTimer?.Stop();
        _hideTimer = new DispatcherTimer(DispatcherPriority.Input, context.Ui) { Interval = TimeSpan.FromMilliseconds(220) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer?.Stop();
            if (_panel is { IsPinned: false, IsMouseOver: false } && _tracker?.Current is null)
            {
                HidePanel();
            }
        };
        _hideTimer.Start();
    }

    private void ShowFor(TaskbarButton button)
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        TaskbarMapper.Fill(button);
        var app = context.App;
        var windows = button.Windows.Where(w => !(app.Get<bool>(Keys.DockCurrentDesktop) && w.OtherDesktop)).ToList();
        windows = app.Get<bool>(Keys.DockOrderByCreation)
            ? [.. windows.OrderBy(w => w.Hwnd)]
            : [.. windows.OrderBy(w => w.Rank).ThenBy(w => w.ZIndex)];
        if (windows.Count == 0)
        {
            return;
        }

        HidePanel();
        var foreground = SwitcherModel.ForegroundPath();
        var options = new PreviewPanelOptions
        {
            Scale = SwitcherOptions.ScaleOf(app.Get<string>(Keys.PreviewSize)),
            Minimal = app.Get<bool>(Keys.MinimalPreviews),
            QuitOnX = app.Get<bool>(Keys.DockQuitOnX),
            Opacity = app.Get<double>(Keys.DockOpacity),
            Thumbnails = !AppIdentity.InList(app.Get<List<string>>(Keys.PreviewExcluded), foreground),
        };
        var panel = new TaskbarPreviewPanel(options);
        var name = AppIdentity.AppName(button.AppPath);
        panel.SetWindows(name.Length > 0 ? name : TaskbarTracker.CleanName(button.Name), AppIdentity.IconOf(button.AppPath), windows);
        panel.Chosen += hwnd => Choose(hwnd, hide: !panel.IsPinned);
        panel.CloseRequested += hwnd => CloseWindow(panel, button, hwnd);
        panel.MinimizeToggled += hwnd => ToggleMinimize(panel, button, hwnd);
        panel.PanelClosed += HidePanel;
        panel.Dropped += OnDropped;
        panel.MouseLeave += (_, _) =>
        {
            if (!panel.IsPinned)
            {
                ScheduleHide();
            }
        };
        _panel = panel;
        _shown = button;
        panel.ShowFor(button);

        if (app.Get<bool>(Keys.DockKeepVisible) && TaskbarSystem.IsAutoHide())
        {
            _wasAutohide = true;
            _autohideHeld = true;
            app.Set(Keys.AutohideJournal, true);
            TaskbarSystem.SetAutoHide(false);
        }

        if (options.Thumbnails)
        {
            foreach (var window in windows.Where(w => !w.Minimized && !w.OtherDesktop))
            {
                var hwnd = window.Hwnd;
                _ = WindowCapture.CaptureAsync(hwnd).ContinueWith(t =>
                {
                    if (t.Result is not null)
                    {
                        context.Ui.BeginInvoke(() => _panel?.ThumbReady(hwnd));
                    }
                });
            }
        }
    }

    private void HidePanel()
    {
        var panel = _panel;
        _panel = null;
        _shown = null;
        if (panel is not null)
        {
            panel.Close();
        }

        if (_autohideHeld)
        {
            _autohideHeld = false;
            if (_wasAutohide)
            {
                TaskbarSystem.SetAutoHide(true);
            }

            _context?.App.Reset(Keys.AutohideJournal);
        }
    }

    private static void RestoreAutohide(IAppSettings app)
    {
        if (app.IsSet(Keys.AutohideJournal) && app.Get<bool>(Keys.AutohideJournal))
        {
            TaskbarSystem.SetAutoHide(true);
            app.Reset(Keys.AutohideJournal);
        }
    }

    private void Choose(nint hwnd, bool hide)
    {
        WindowUseTracker.Touch(hwnd);
        WinUtil.Activate(hwnd);
        if (hide)
        {
            HidePanel();
        }
    }

    private void CloseWindow(TaskbarPreviewPanel panel, TaskbarButton button, nint hwnd)
    {
        var app = _context?.App;
        if (app is not null && app.Get<bool>(Keys.DockQuitOnX))
        {
            foreach (var window in button.Windows)
            {
                WinApi.PostMessageW(window.Hwnd, WinApi.WM_CLOSE, 0, 0);
            }

            HidePanel();
            return;
        }

        WinApi.PostMessageW(hwnd, WinApi.WM_CLOSE, 0, 0);
        button.Windows.RemoveAll(w => w.Hwnd == hwnd);
        if (button.Windows.Count == 0)
        {
            HidePanel();
            return;
        }

        panel.SetWindows(AppIdentity.AppName(button.AppPath), AppIdentity.IconOf(button.AppPath), button.Windows);
    }

    private static void ToggleMinimize(TaskbarPreviewPanel panel, TaskbarButton button, nint hwnd)
    {
        if (WinApi.IsIconic(hwnd))
        {
            WinApi.ShowWindow(hwnd, WinApi.SW_RESTORE);
        }
        else
        {
            WinApi.ShowWindow(hwnd, WinApi.SW_MINIMIZE);
        }

        foreach (var window in TaskbarMapper.Windows(button, out _))
        {
            if (window.Hwnd == hwnd)
            {
                var index = button.Windows.FindIndex(w => w.Hwnd == hwnd);
                if (index >= 0)
                {
                    button.Windows[index] = window;
                }
            }
        }

        panel.SetWindows(AppIdentity.AppName(button.AppPath), AppIdentity.IconOf(button.AppPath), button.Windows);
    }

    /// <summary>A card was dragged out: the window's top-left goes to the pointer (un-minimized, size kept, clamped to its monitor).</summary>
    private void OnDropped(nint hwnd, Point pointer)
    {
        if (!WinApi.IsWindow(hwnd))
        {
            return;
        }

        using var dpi = WinApiDpi.EnterWindowDpiContext(hwnd);
        if (WinApi.IsIconic(hwnd) || WinApi.IsZoomed(hwnd))
        {
            WinApi.ShowWindow(hwnd, WinApi.SW_RESTORE);
        }

        NativeMethods.GetWindowRect(hwnd, out var rect);
        var monitor = NativeMethods.MonitorFromPoint(new POINT { X = (int)pointer.X, Y = (int)pointer.Y }, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        NativeMethods.GetMonitorInfoW(monitor, ref info);
        var x = Math.Clamp((int)pointer.X, info.rcWork.Left, Math.Max(info.rcWork.Left, info.rcWork.Right - 80));
        var y = Math.Clamp((int)pointer.Y, info.rcWork.Top, Math.Max(info.rcWork.Top, info.rcWork.Bottom - 60));
        NativeMethods.SetWindowPos(hwnd, 0, x, y, rect.Width, rect.Height, SWP_NOZORDER | SWP_NOACTIVATE);
        WinUtil.Activate(hwnd);
        HidePanel();
    }
}

/// <summary>
/// Taskbar clicks (spec 05 A3): a click on the active app's taskbar button minimizes its windows (a second click brings them back),
/// hides the app (minimize all, including other desktops), or cycles through its windows. Modifier clicks and drags keep their native
/// behaviour; the press is held until release so a drag can be handed back to the shell.
/// </summary>
public sealed class TaskbarClickFeature : IFeature
{
    private const double ToggleIntentSeconds = 1.5;
    private const int DragSlop = 6;
    private const double RepeatGap = 0.25;

    private sealed class MinimizeRecord
    {
        public required List<nint> Windows { get; init; }

        public DateTime At { get; init; }
    }

    private IFeatureContext? _context;
    private TaskbarTracker? _tracker;
    private IDisposable? _mouse;
    private Action<string>? _changed;
    private bool _running;
    private readonly Dictionary<string, MinimizeRecord> _records = [];
    private DateTime _lastHandled = DateTime.MinValue;

    // hook-thread state
    private volatile TaskbarButton? _pressed;
    private bool _swallowing;
    private int _downX;
    private int _downY;
    private bool _moved;

    public string Id => "dock-click";

    public void Start(IFeatureContext context)
    {
        _context = context;
        Keys.Register(context.App);
        Ambient.App = context.App;
        _changed = key =>
        {
            if (key is Keys.DockClickMinimize or Keys.DockClickHide or Keys.DockClickCycle)
            {
                // minimize and hide exclude each other
                if (key == Keys.DockClickMinimize && context.App.Get<bool>(Keys.DockClickMinimize))
                {
                    context.App.Set(Keys.DockClickHide, false);
                }
                else if (key == Keys.DockClickHide && context.App.Get<bool>(Keys.DockClickHide))
                {
                    context.App.Set(Keys.DockClickMinimize, false);
                }

                context.Ui.BeginInvoke(Sync);
            }
        };
        context.App.Changed += _changed;
        Sync();
    }

    public void Stop()
    {
        if (_context is not null && _changed is not null)
        {
            _context.App.Changed -= _changed;
        }

        Deactivate();
        _context = null;
    }

    private bool AnyOn(IAppSettings app) =>
        app.Get<bool>(Keys.DockClickMinimize) || app.Get<bool>(Keys.DockClickHide) || app.Get<bool>(Keys.DockClickCycle);

    private void Sync()
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        if (AnyOn(context.App))
        {
            Activate();
        }
        else
        {
            Deactivate();
        }
    }

    private void Activate()
    {
        var context = _context;
        if (context is null || _running)
        {
            return;
        }

        _running = true;
        _tracker = TaskbarTrackerHub.Get(context);
        _tracker.Acquire(context.Hooks);
        _tracker.Changed += OnHover;
        _mouse = context.Hooks.SubscribeMouse(OnMouse, priority: 60);
    }

    private void Deactivate()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        _mouse?.Dispose();
        _mouse = null;
        if (_tracker is not null)
        {
            _tracker.Changed -= OnHover;
            _tracker.Release();
        }

        _tracker = null;
        _pressed = null;
        _records.Clear();
    }

    private void OnHover(TaskbarButton? button)
    {
        // keep the window list of the hovered button fresh for the hook thread
        if (button is not null)
        {
            TaskbarMapper.Fill(button);
        }
    }

    private bool OnMouse(MouseHookEvent e)
    {
        if (e.IsFromWinKnife)
        {
            return false;
        }

        switch (e.Kind)
        {
            case MouseEventKind.LeftDown:
            {
                var button = _tracker?.Current;
                if (button is null || e.X < button.Bounds.Left || e.X >= button.Bounds.Right || e.Y < button.Bounds.Top || e.Y >= button.Bounds.Bottom)
                {
                    _swallowing = false;
                    return false;
                }

                if (e.Modifiers.HasFlag(ModifierKeys.Shift) || e.Modifiers.HasFlag(ModifierKeys.Control) || e.Modifiers.HasFlag(ModifierKeys.Alt))
                {
                    _swallowing = false;
                    return false;
                }

                if ((DateTime.UtcNow - _lastHandled).TotalSeconds < RepeatGap || !Wanted(button))
                {
                    _swallowing = false;
                    return false;
                }

                _pressed = button;
                _swallowing = true;
                _moved = false;
                _downX = e.X;
                _downY = e.Y;
                return true;
            }

            case MouseEventKind.Move when _swallowing && !_moved:
                if (Math.Abs(e.X - _downX) > DragSlop || Math.Abs(e.Y - _downY) > DragSlop)
                {
                    // A drag (reordering the buttons): hand the press back to the shell.
                    _moved = true;
                    _swallowing = false;
                    var downX = _downX;
                    var downY = _downY;
                    var nowX = e.X;
                    var nowY = e.Y;
                    ThreadPool.QueueUserWorkItem(_ => ReplayDrag(downX, downY, nowX, nowY));
                }

                return false;
            case MouseEventKind.LeftUp when _swallowing:
            {
                _swallowing = false;
                var button = _pressed;
                _pressed = null;
                if (button is not null)
                {
                    _context?.Ui.BeginInvoke(() => Handle(button));
                }

                return true;
            }

            default:
                return false;
        }
    }

    private static void ReplayDrag(int downX, int downY, int nowX, int nowY)
    {
        var width = Math.Max(1, WinApi.GetSystemMetrics(78) - 1);
        var height = Math.Max(1, WinApi.GetSystemMetrics(79) - 1);
        var left = WinApi.GetSystemMetrics(76);
        var top = WinApi.GetSystemMetrics(77);
        INPUT Abs(int x, int y, uint flags) => new()
        {
            type = INPUT_MOUSE,
            u = new INPUTUNION
            {
                mi = new MOUSEINPUT
                {
                    dx = (int)((x - left) * 65535.0 / width),
                    dy = (int)((y - top) * 65535.0 / height),
                    dwFlags = flags | 0x8000 | 0x4000 | MOUSEEVENTF_MOVE,
                    dwExtraInfo = InputSender.WinKnifeExtraInfo,
                },
            },
        };
        InputSender.Send(Abs(downX, downY, MOUSEEVENTF_LEFTDOWN), Abs(nowX, nowY, 0));
    }

    private bool Wanted(TaskbarButton button)
    {
        var app = _context?.App;
        if (app is null)
        {
            return false;
        }

        var foreground = NativeMethods.GetForegroundWindow();
        var active = button.Windows.Any(w => w.Hwnd == foreground || WinApi.GetAncestor(foreground, WinApi.GA_ROOT) == w.Hwnd);
        if (_records.TryGetValue(button.Key, out var record) && record.Windows.Any(WinApi.IsIconic))
        {
            return true; // the next click toggles from our own record
        }

        if (!active || button.Windows.Count == 0)
        {
            return false;
        }

        if (app.Get<bool>(Keys.DockClickCycle) || app.Get<bool>(Keys.DockClickHide))
        {
            return button.Windows.Count > 1 || app.Get<bool>(Keys.DockClickHide);
        }

        return app.Get<bool>(Keys.DockClickMinimize) && button.Windows.Count > 1;
    }

    private void Handle(TaskbarButton button)
    {
        var app = _context?.App;
        if (app is null)
        {
            return;
        }

        _lastHandled = DateTime.UtcNow;
        try
        {
            // second click: bring back what we minimized, rearmost first so the front window lands on top
            if (_records.Remove(button.Key, out var record) && record.Windows.Any(WinApi.IsIconic))
            {
                foreach (var hwnd in Enumerable.Reverse(record.Windows).Where(WinApi.IsWindow))
                {
                    WinApi.ShowWindow(hwnd, WinApi.SW_SHOWNOACTIVATE);
                    if (WinApi.IsIconic(hwnd))
                    {
                        WinApi.ShowWindow(hwnd, WinApi.SW_RESTORE);
                    }
                }

                if (record.Windows.FirstOrDefault(WinApi.IsWindow) is var front and not 0)
                {
                    WinUtil.Activate(front);
                }

                return;
            }

            TaskbarMapper.Fill(button);
            var windows = button.Windows;
            if (app.Get<bool>(Keys.DockClickHide) || app.Get<bool>(Keys.DockClickMinimize))
            {
                var hide = app.Get<bool>(Keys.DockClickHide);
                var targets = windows.Where(w => !w.Minimized && !w.Fullscreen && (hide || !w.OtherDesktop)).OrderBy(w => w.ZIndex).Select(w => w.Hwnd).ToList();
                foreach (var hwnd in Enumerable.Reverse(targets))
                {
                    WinApi.ShowWindow(hwnd, WinApi.SW_SHOWMINNOACTIVE);
                }

                if (targets.Count > 0)
                {
                    _records[button.Key] = new MinimizeRecord { Windows = targets, At = DateTime.UtcNow };
                }

                return;
            }

            if (app.Get<bool>(Keys.DockClickCycle))
            {
                var order = windows.Where(w => !w.Minimized && !w.OtherDesktop).OrderBy(w => w.Hwnd).Select(w => w.Hwnd).ToList();
                if (order.Count < 2)
                {
                    return;
                }

                var foreground = WinApi.GetAncestor(NativeMethods.GetForegroundWindow(), WinApi.GA_ROOT);
                var index = Math.Max(0, order.IndexOf(foreground));
                var next = order[(index + 1) % order.Count];
                WindowUseTracker.Touch(next);
                WinUtil.Activate(next);
            }
        }
        catch (Exception ex)
        {
            _context?.Log.Error("Taskbar click action failed", ex);
        }
    }
}
