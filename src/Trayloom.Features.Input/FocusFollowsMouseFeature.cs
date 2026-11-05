// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Core.Native;

namespace Trayloom.Features.Input;

/// <summary>
/// "Focuses the window under the pointer." Once the pointer has rested over a window for the hover delay (100-3000 ms),
/// that window gets keyboard focus. "Bring the window to the front" (default on) raises it too; with it off the window
/// is focused and put back at its old position in the z-order, and "Wait for the pointer to stop" (default on) holds
/// focus until the pointer rests. No input hook is needed: a 30 ms poll on the UI thread follows the pointer, so it
/// adds no latency to mouse input. It acts once per "entry" into a window, so Alt-Tabbing or clicking elsewhere while
/// the pointer sits still is never undone.
/// Skipped: nothing happens while a mouse button or a modifier key is down, while a menu is open or a window is being
/// moved, on the desktop / taskbar / tray flyouts / Start / Search / Alt-Tab shells, menus and tooltips, on Trayloom's
/// own windows, on cloaked, minimized or non-activatable windows, in apps of the "Apps to leave alone" list, and (by
/// default) when the foreground window is full screen.
/// </summary>
public sealed class FocusFollowsMouseFeature : InputFeatureBase
{
    private static readonly HashSet<string> SkippedClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland", "#32768", "#32769", "tooltips_class32", "IME", "MSCTFIME UI",
        "MultitaskingViewFrame", "XamlExplorerHostIslandWindow", "ForegroundStaging", "TaskListThumbnailWnd",
        "TaskListOverlayWnd", "Windows.UI.Core.CoreWindow", "SysShadow", "DV2ControlHost", "NativeHWNDHost",
        "ApplicationManager_DesktopShellWindow", "TaskSwitcherWnd", "XamlWindow",
    };

    private DispatcherTimer? _timer;
    private AppRuleSet? _rules;
    private volatile int _delayMs = 250;
    private volatile bool _raise = true;
    private volatile bool _waitForStop = true;
    private volatile bool _skipFullscreen = true;
    private nint _candidate;
    private bool _handled;
    private long _since;
    private POINT _lastPoint;
    private long _lastMove;

    public override string Id => "focus-follows-mouse";

    protected override bool IsEnabled() => App.Get<bool>(K.FocusEnabled);

    protected override void Engage()
    {
        _candidate = 0;
        _handled = true;
        _rules = new AppRuleSet(App, K.FocusExceptions);
        Context!.Ui.BeginInvoke(() =>
        {
            if (!IsEngaged || _timer is not null)
            {
                return;
            }

            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Normal, OnTick, Context!.Ui);
            _timer.Start();
        });
    }

    protected override void Disengage()
    {
        var timer = _timer;
        _timer = null;
        _rules?.Dispose();
        _rules = null;
        if (timer is not null)
        {
            Context?.Ui.BeginInvoke(timer.Stop);
        }
    }

    protected override void LoadSettings()
    {
        var delay = App.Get<int>(K.FocusDelay);
        _delayMs = Math.Clamp(delay, 100, 3000);
        _raise = App.Get<bool>(K.FocusRaise);
        _waitForStop = App.Get<bool>(K.FocusWaitForStop);
        _skipFullscreen = App.Get<bool>(K.FocusSkipFullscreen);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        try
        {
            NativeMethods.GetCursorPos(out var point);
            var now = Environment.TickCount64;
            if (point.X != _lastPoint.X || point.Y != _lastPoint.Y)
            {
                _lastPoint = point;
                _lastMove = now;
            }

            var root = RootWindowAt(point);
            if (root != _candidate)
            {
                _candidate = root;
                _since = now;
                _handled = root == 0;
                return;
            }

            if (_handled || now - _since < _delayMs)
            {
                return;
            }

            if (!_raise && _waitForStop && now - _lastMove < 120)
            {
                return; // wait until the pointer rests
            }

            _handled = true;
            if (ShouldFocus(root))
            {
                Activate(root, _raise);
            }
        }
        catch (Exception ex)
        {
            Context?.Log.Error("Focus follows mouse tick failed", ex);
        }
    }

    private static nint RootWindowAt(POINT point)
    {
        var hwnd = Win32.WindowFromPoint(point);
        return hwnd == 0 ? 0 : Win32.GetAncestor(hwnd, Win32.GA_ROOT);
    }

    private bool ShouldFocus(nint target)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == target)
        {
            return false;
        }

        // Dragging, a button held down, or a chord in progress.
        if (NativeMethods.GetAsyncKeyState(Win32.VK_LBUTTON) < 0 ||
            NativeMethods.GetAsyncKeyState(Win32.VK_RBUTTON) < 0 ||
            NativeMethods.GetAsyncKeyState(Win32.VK_MBUTTON) < 0 ||
            Win32.AnyModifierDown())
        {
            return false;
        }

        if (!Win32.IsWindowVisible(target) || Win32.IsIconic(target) || Win32.IsCloaked(target))
        {
            return false;
        }

        if (AppIdentity.IsOwnWindow(target))
        {
            return false; // Trayloom's own windows and overlays
        }

        var exStyle = NativeMethods.GetWindowLongPtr(target, NativeConstants.GWL_EXSTYLE).ToInt64();
        if ((exStyle & NativeConstants.WS_EX_NOACTIVATE) != 0)
        {
            return false;
        }

        if (SkippedClasses.Contains(Win32.ClassName(target)))
        {
            return false;
        }

        if (_rules is { IsEmpty: false } rules && rules.MatchesWindow(target))
        {
            return false;
        }

        if (foreground != 0)
        {
            var thread = NativeMethods.GetWindowThreadProcessId(foreground, out _);
            var info = new GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<GUITHREADINFO>() };
            if (Win32.GetGUIThreadInfo(thread, ref info) &&
                (info.flags & (Win32.GUI_INMENUMODE | Win32.GUI_POPUPMENUMODE | Win32.GUI_INMOVESIZE)) != 0)
            {
                return false;
            }

            if (_skipFullscreen && IsFullscreen(foreground))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsFullscreen(nint hwnd)
    {
        var className = Win32.ClassName(hwnd);
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd")
        {
            return false;
        }

        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return false;
        }

        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeConstants.MONITOR_DEFAULTTONEAREST);
        if (!NativeMethods.GetMonitorInfoW(monitor, ref info))
        {
            return false;
        }

        var bounds = info.rcMonitor;
        return rect.Left <= bounds.Left && rect.Top <= bounds.Top && rect.Right >= bounds.Right && rect.Bottom >= bounds.Bottom;
    }

    // The nearest window above the target in the same z-order band: where the target goes back to when it must not be raised.
    private static nint WindowAbove(nint target)
    {
        var targetTop = IsTopmost(target);
        var guard = 0;
        for (var w = Win32.GetWindow(target, Win32.GW_HWNDPREV); w != 0 && guard < 400; w = Win32.GetWindow(w, Win32.GW_HWNDPREV), guard++)
        {
            if (IsTopmost(w) == targetTop && Win32.IsWindowVisible(w))
            {
                return w;
            }
        }

        return 0;
    }

    private static bool IsTopmost(nint hwnd) =>
        (NativeMethods.GetWindowLongPtr(hwnd, NativeConstants.GWL_EXSTYLE).ToInt64() & 0x8) != 0;

    private void Activate(nint target, bool raise)
    {
        var above = raise ? 0 : WindowAbove(target);
        var foreground = NativeMethods.GetForegroundWindow();
        var current = NativeMethods.GetCurrentThreadId();
        var foregroundThread = foreground == 0 ? 0 : NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var attached = foregroundThread != 0 && foregroundThread != current && Win32.AttachThreadInput(current, foregroundThread, true);
        try
        {
            NativeMethods.SetForegroundWindow(target);
        }
        finally
        {
            if (attached)
            {
                Win32.AttachThreadInput(current, foregroundThread, false);
            }
        }

        if (NativeMethods.GetForegroundWindow() != target)
        {
            // Foreground lock refused: a tap of Alt with an unassigned key in between lets the next call through
            // without opening any menu.
            // One array, so no other process can slip a key in between Alt down and Alt up.
            InputSender.Send(
                KeySender.KeyInput(0x12, false),
                KeySender.KeyInput(Win32.VK_UNASSIGNED, false),
                KeySender.KeyInput(Win32.VK_UNASSIGNED, true),
                KeySender.KeyInput(0x12, true));
            NativeMethods.SetForegroundWindow(target);
        }

        if (!raise && above != 0 && Win32.IsWindow(above))
        {
            // Put the focused window back where it was (focus alone, no raise).
            NativeMethods.SetWindowPos(target, above, 0, 0, 0, 0, NativeConstants.SWP_NOMOVE | NativeConstants.SWP_NOSIZE | NativeConstants.SWP_NOACTIVATE);
        }
    }
}
