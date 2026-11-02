// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Input;

/// <summary>
/// Gives keyboard focus to the top-level window under the pointer once the pointer has rested over it for a short,
/// configurable delay. No input hook is needed: a 30 ms poll on the UI thread follows the pointer, so it adds no
/// latency to mouse input. It acts once per "entry" into a window, which means that Alt-Tabbing or clicking
/// elsewhere while the pointer sits still is never undone.
/// Skipped targets: the desktop, taskbar and tray flyouts, Start/Search/Alt-Tab shells, menus and tooltips, WinKnife's
/// own windows, cloaked/minimized/non-activatable windows. Nothing happens while a mouse button is held (dragging),
/// while a menu is open or a window is being moved, or (by default) when the foreground window is full screen.
/// Note: Windows can only activate a window by bringing it to the front, so the window is raised as well.
/// </summary>
public sealed class FocusFollowsMouseFeature : InputFeatureBase
{
    private static readonly HashSet<string> SkippedClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland", "#32768", "#32769", "tooltips_class32", "IME", "MSCTFIME UI",
        "MultitaskingViewFrame", "XamlExplorerHostIslandWindow", "ForegroundStaging", "TaskListThumbnailWnd",
        "TaskListOverlayWnd", "Windows.UI.Core.CoreWindow", "SysShadow", "DV2ControlHost", "NativeHWNDHost",
    };

    private DispatcherTimer? _timer;
    private volatile int _delayMs = 250;
    private volatile bool _skipFullscreen = true;
    private nint _candidate;
    private bool _handled;
    private long _since;

    public override string Id => "focus-follows-mouse";

    public override string Name => "Focus follows mouse";

    public override string Description => "Focus the window under the pointer after a short delay, without clicking.";

    protected override void OnStart()
    {
        _candidate = 0;
        _handled = true;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Normal, OnTick, Context!.Ui);
        _timer.Start();
    }

    protected override void OnStop()
    {
        _timer?.Stop();
        _timer = null;
    }

    protected override void LoadSettings()
    {
        if (Context is null)
        {
            return;
        }

        _delayMs = Math.Clamp(Context.Settings.Get("delayMs", 250), 50, 3000);
        _skipFullscreen = Context.Settings.Get("skipFullscreen", true);
    }

    protected override void BuildSettings(SettingsViewBuilder builder) => builder
        .AddIntSlider("delayMs", "Delay before focusing (ms)", 50, 1500, 250,
            "How long the pointer has to stay over a window before it gets focus.")
        .AddToggle("skipFullscreen", "Don't leave full-screen apps", true,
            "Keeps games and video players in front even when the pointer drifts onto another monitor.");

    private void OnTick(object? sender, EventArgs e)
    {
        if (Paused)
        {
            _candidate = 0;
            return;
        }

        try
        {
            NativeMethods.GetCursorPos(out var point);
            var root = RootWindowAt(point);
            var now = Environment.TickCount64;
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

            _handled = true;
            if (ShouldFocus(root))
            {
                Activate(root);
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

        // Dragging, or a button simply held down.
        if (NativeMethods.GetAsyncKeyState(Win32.VK_LBUTTON) < 0 ||
            NativeMethods.GetAsyncKeyState(Win32.VK_RBUTTON) < 0 ||
            NativeMethods.GetAsyncKeyState(Win32.VK_MBUTTON) < 0)
        {
            return false;
        }

        if (!Win32.IsWindowVisible(target) || Win32.IsIconic(target) || Win32.IsCloaked(target))
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(target, out var pid);
        if (pid == Environment.ProcessId)
        {
            return false; // WinKnife's own windows and overlays
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

    private void Activate(nint target)
    {
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
            KeySender.SendKeys([0x12], keyUp: false);
            KeySender.Tap(Win32.VK_UNASSIGNED);
            KeySender.SendKeys([0x12], keyUp: true);
            NativeMethods.SetForegroundWindow(target);
        }
    }
}
