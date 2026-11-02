// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Text;
using System.Windows.Input;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Native;
using static WinKnife.Core.Native.NativeConstants;

namespace WinKnife.Features.WindowTools;

/// <summary>
/// The window switcher (spec 05 A1): hooks Alt+Tab, Alt+` and Ctrl+Alt+Tab (swallowed when "Replace Windows Alt+Tab and Alt+`" is on),
/// shows the panel after the appearance delay while the modifier is held and activates the selection on release.
/// </summary>
public sealed class SwitcherFeature : IFeature
{
    private const int InjectedShiftWindowMs = 160;

    private IFeatureContext? _context;
    private IDisposable? _keyboard;
    private IDisposable? _mouse;
    private Action<string>? _settingsChanged;
    private Action<string>? _shortcutsChanged;

    // hook-thread state
    private volatile Hotkey? _apps;
    private volatile Hotkey? _windows;
    private volatile Hotkey? _all;
    private volatile bool _takeOver = true;
    private volatile bool _active;
    private HotkeyModifiers _mods;
    private Key _triggerKey;
    private long _shiftTick;
    private volatile bool _panelKnown;
    private RECT _panelRect;
    private readonly object _rectGate = new();

    // UI-thread state
    private SwitcherOptions _options = new();
    private List<SwitchEntry> _all2 = [];
    private List<SwitchEntry> _view = [];
    private int _selected;
    private string _query = string.Empty;
    private bool _pinned;
    private SwitcherPanel? _panel;
    private DispatcherTimer? _showTimer;
    private SwitchMode _mode;
    private bool _ending;

    public string Id => "switcher";

    public void Start(IFeatureContext context)
    {
        _context = context;
        Keys.Register(context.App);
        Ambient.App = context.App;
        WindowUseTracker.Acquire();
        _settingsChanged = key =>
        {
            if (key is Keys.SwitcherEnabled or Keys.SwitcherTakeOver or Keys.SwitcherAllWindowsEnabled)
            {
                context.Ui.BeginInvoke(Sync);
            }
        };
        _shortcutsChanged = _ => context.Ui.BeginInvoke(RefreshHotkeys);
        context.App.Changed += _settingsChanged;
        context.Shortcuts.Changed += _shortcutsChanged;
        Sync();
    }

    public void Stop()
    {
        if (_context is not null)
        {
            if (_settingsChanged is not null)
            {
                _context.App.Changed -= _settingsChanged;
            }

            if (_shortcutsChanged is not null)
            {
                _context.Shortcuts.Changed -= _shortcutsChanged;
            }
        }

        EndSession(commit: false);
        _keyboard?.Dispose();
        _keyboard = null;
        WindowUseTracker.Release();
        _context = null;
    }

    private void Sync()
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        RefreshHotkeys();
        if (context.App.Get<bool>(Keys.SwitcherEnabled))
        {
            _keyboard ??= context.Hooks.SubscribeKeyboard(OnKey, priority: 100);
        }
        else
        {
            EndSession(commit: false);
            _keyboard?.Dispose();
            _keyboard = null;
        }
    }

    private void RefreshHotkeys()
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        _apps = context.Shortcuts.GetHotkey("switcher");
        _windows = context.Shortcuts.GetHotkey("switcherWindow");
        _all = context.App.Get<bool>(Keys.SwitcherAllWindowsEnabled) ? context.Shortcuts.GetHotkey("switcherAllWindows") : null;
        _takeOver = context.App.Get<bool>(Keys.SwitcherTakeOver);
    }

    // ---------------------------------------------------------------------------------------------- hook thread

    private static HotkeyModifiers Mods(ModifierKeys keys)
    {
        var m = HotkeyModifiers.None;
        if (keys.HasFlag(ModifierKeys.Control)) m |= HotkeyModifiers.Control;
        if (keys.HasFlag(ModifierKeys.Alt)) m |= HotkeyModifiers.Alt;
        if (keys.HasFlag(ModifierKeys.Shift)) m |= HotkeyModifiers.Shift;
        if (keys.HasFlag(ModifierKeys.Windows)) m |= HotkeyModifiers.Win;
        return m;
    }

    private static bool IsModifierVk(int vk) => vk is 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C;

    private static bool Belongs(int vk, HotkeyModifiers mods) =>
        (mods.HasFlag(HotkeyModifiers.Alt) && vk is 0x12 or 0xA4 or 0xA5)
        || (mods.HasFlag(HotkeyModifiers.Control) && vk is 0x11 or 0xA2 or 0xA3)
        || (mods.HasFlag(HotkeyModifiers.Win) && vk is 0x5B or 0x5C)
        || (mods.HasFlag(HotkeyModifiers.Shift) && vk is 0x10 or 0xA0 or 0xA1);

    private bool OnKey(KeyboardHookEvent e)
    {
        if (e.IsFromWinKnife || e.IsInjected)
        {
            return false;
        }

        var context = _context;
        if (context is null)
        {
            return false;
        }

        var mods = Mods(e.Modifiers);
        if (!_active)
        {
            if (!e.IsKeyDown)
            {
                return false;
            }

            var key = e.Key;
            foreach (var (hotkey, mode) in new[] { (_apps, SwitchMode.Apps), (_windows, SwitchMode.FrontAppWindows), (_all, SwitchMode.AllWindows) })
            {
                if (hotkey is null || key != hotkey.Key || hotkey.Modifiers == HotkeyModifiers.None)
                {
                    continue;
                }

                var shift = mods.HasFlag(HotkeyModifiers.Shift);
                var matches = mods == hotkey.Modifiers || (shift && !hotkey.Modifiers.HasFlag(HotkeyModifiers.Shift) && (mods & ~HotkeyModifiers.Shift) == hotkey.Modifiers);
                if (!matches)
                {
                    continue;
                }

                Begin(mode, hotkey, shift);
                return _takeOver || !HotkeyMap.IsReserved(hotkey);
            }

            if (_takeOver && key == Key.Tab && mods is HotkeyModifiers.Alt or (HotkeyModifiers.Alt | HotkeyModifiers.Shift) or (HotkeyModifiers.Control | HotkeyModifiers.Alt))
            {
                return true; // the Windows task switcher never opens
            }

            return _takeOver && key == Key.Escape && mods == HotkeyModifiers.Alt;
        }

        // --- a session is running ---
        var vk = e.VkCode;
        if (IsModifierVk(vk))
        {
            if (vk is 0x10 or 0xA0 or 0xA1)
            {
                if (e.IsKeyDown && !_shiftIsDown)
                {
                    _shiftIsDown = true;
                    _shiftTick = Environment.TickCount64;
                    Post(SwitchKey.Previous);
                }
                else if (e.IsKeyUp)
                {
                    _shiftIsDown = false;
                }

                return false;
            }

            if (e.IsKeyDown)
            {
                return false;
            }

            if (Belongs(vk, _mods))
            {
                Post(SwitchKey.ModifierReleased);
            }

            return false;
        }

        if (e.IsKeyUp)
        {
            return true;
        }

        var shiftHeld = e.Modifiers.HasFlag(ModifierKeys.Shift);
        var k = e.Key;
        if (k == _triggerKey || k == Key.Tab)
        {
            // The Tab that belongs to the same chord as a Shift press must not step twice.
            if (shiftHeld && Environment.TickCount64 - _shiftTick < InjectedShiftWindowMs)
            {
                return true;
            }

            Post(shiftHeld ? SwitchKey.Previous : SwitchKey.Next);
            return true;
        }

        if (k == Key.OemTilde && (_windows?.Key == Key.OemTilde))
        {
            Post(shiftHeld ? SwitchKey.WalkBack : SwitchKey.Walk);
            return true;
        }

        switch (k)
        {
            case Key.Left:
                Post(SwitchKey.Previous);
                break;
            case Key.Right:
                Post(SwitchKey.Next);
                break;
            case Key.Up:
                Post(SwitchKey.Up);
                break;
            case Key.Down:
                Post(SwitchKey.Down);
                break;
            case Key.Enter:
                Post(SwitchKey.Commit);
                break;
            case Key.Escape:
                Post(SwitchKey.Cancel);
                break;
            case Key.Back:
                Post(SwitchKey.Backspace);
                break;
            default:
                var ch = CharOf(vk, shiftHeld);
                if (ch is not null)
                {
                    PostChar(ch.Value);
                }

                break;
        }

        return true;
    }

    private bool _shiftIsDown;

    private static char? CharOf(int vk, bool shift)
    {
        var c = WinApi.MapVirtualKeyW((uint)vk, 2);
        if (c == 0 || char.IsControl((char)c))
        {
            return null;
        }

        var ch = (char)(c & 0xFFFF);
        if (char.IsLetter(ch))
        {
            return shift ? char.ToUpperInvariant(ch) : char.ToLowerInvariant(ch);
        }

        return char.IsDigit(ch) || ch == ' ' || ch == '.' || ch == '-' ? ch : null;
    }

    private void Begin(SwitchMode mode, Hotkey hotkey, bool shift)
    {
        var e0 = shift;
        _active = true;
        _mods = hotkey.Modifiers;
        _triggerKey = hotkey.Key;
        _shiftTick = 0;
        _shiftIsDown = e0;
        var ui = _context?.Ui;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Alt) || hotkey.Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            // Another key press while Alt is down keeps the target app's menu bar from activating on release.
            ThreadPool.QueueUserWorkItem(_ =>
            {
                InputSender.SendKey(0xFC);
                InputSender.SendKey(0xFC, keyUp: true);
            });
        }

        ui?.BeginInvoke(() => StartSession(mode, shift));
    }

    private enum SwitchKey
    {
        Next,
        Previous,
        Up,
        Down,
        Walk,
        WalkBack,
        Commit,
        Cancel,
        Backspace,
        ModifierReleased,
    }

    private void Post(SwitchKey key) => _context?.Ui.BeginInvoke(() => OnSwitchKey(key));

    private void PostChar(char ch) => _context?.Ui.BeginInvoke(() => OnChar(ch));

    // ---------------------------------------------------------------------------------------------- UI thread

    private void StartSession(SwitchMode mode, bool backwards)
    {
        var context = _context;
        if (context is null)
        {
            _active = false;
            return;
        }

        try
        {
            _ending = false;
            _mode = mode;
            _options = SwitcherOptions.Read(context.App);
            _query = string.Empty;
            _pinned = false;
            _all2 = SwitcherModel.Build(_options, mode, out var initial);
            _view = _all2;
            if (_view.Count == 0)
            {
                EndSession(commit: false);
                return;
            }

            _selected = backwards ? _view.Count - 1 : Math.Clamp(initial, 0, _view.Count - 1);
            _showTimer?.Stop();
            if (_options.Delay <= 0)
            {
                ShowPanel();
            }
            else
            {
                _showTimer = new DispatcherTimer(DispatcherPriority.Input, context.Ui) { Interval = TimeSpan.FromMilliseconds(_options.Delay) };
                _showTimer.Tick += (_, _) =>
                {
                    _showTimer?.Stop();
                    ShowPanel();
                };
                _showTimer.Start();
            }
        }
        catch (Exception ex)
        {
            context.Log.Error("Could not start the switcher", ex);
            EndSession(commit: false);
        }
    }

    private void ShowPanel()
    {
        var context = _context;
        if (context is null || !_active || _ending || _panel is not null)
        {
            return;
        }

        var hint = (_mode == SwitchMode.Apps ? _apps : _windows)?.ToString() ?? "Alt+Tab";
        var panel = new SwitcherPanel(_options, hint);
        panel.Clicked += (entry, window) => CommitAt(entry, window);
        panel.CloseRequested += (entry, window) => CloseWindowAt(entry, window, quitApp: false);
        panel.Hovered += index =>
        {
            if (index != _selected && index >= 0 && index < _view.Count)
            {
                _selected = index;
                _panel?.Select(index);
            }
        };
        _panel = panel;
        try
        {
            panel.ShowOn(PlacementArea(), _view, _selected, _query, _pinned);
        }
        catch (Exception ex)
        {
            context.Log.Error("Could not show the switcher", ex);
            _panel = null;
            panel.Close();
            EndSession(commit: false);
            return;
        }

        lock (_rectGate)
        {
            _panelRect = panel.PhysicalBounds;
        }

        _panelKnown = true;
        _mouse ??= context.Hooks.SubscribeMouse(OnMouse, priority: 90);
        StartCaptures();
    }

    private RECT PlacementArea()
    {
        nint monitor;
        switch (_options.Placement)
        {
            case "menuBar":
                monitor = NativeMethods.MonitorFromPoint(new POINT { X = 0, Y = 0 }, MONITOR_DEFAULTTOPRIMARY);
                break;
            case "activeWindow":
                var fg = NativeMethods.GetForegroundWindow();
                monitor = fg != 0 ? NativeMethods.MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST) : NativeMethods.MonitorFromPoint(new POINT { X = 0, Y = 0 }, MONITOR_DEFAULTTOPRIMARY);
                break;
            default:
                NativeMethods.GetCursorPos(out var p);
                monitor = NativeMethods.MonitorFromPoint(p, MONITOR_DEFAULTTONEAREST);
                break;
        }

        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        NativeMethods.GetMonitorInfoW(monitor, ref info);
        return info.rcWork;
    }

    private void StartCaptures()
    {
        if (_options.Layout == SwitchLayout.Simple)
        {
            return;
        }

        var foreground = SwitcherModel.ForegroundPath();
        if (AppIdentity.InList(_options.Excluded, foreground))
        {
            return;
        }

        var ui = _context?.Ui;
        foreach (var window in _all2.SelectMany(e => e.Windows).Where(w => !w.Minimized && !w.OtherDesktop).Take(40))
        {
            var hwnd = window.Hwnd;
            _ = WindowCapture.CaptureAsync(hwnd).ContinueWith(t =>
            {
                if (t.Result is not null)
                {
                    ui?.BeginInvoke(() => _panel?.ThumbReady(hwnd));
                }
            });
        }
    }

    private bool OnMouse(MouseHookEvent e)
    {
        if (!_active || !_panelKnown || e.Kind is not (MouseEventKind.LeftDown or MouseEventKind.RightDown or MouseEventKind.MiddleDown))
        {
            return false;
        }

        RECT r;
        lock (_rectGate)
        {
            r = _panelRect;
        }

        if (e.X < r.Left || e.X > r.Right || e.Y < r.Top || e.Y > r.Bottom)
        {
            // A click outside cancels before the click passes through.
            _context?.Ui.BeginInvoke(() => EndSession(commit: false));
        }

        return false;
    }

    private void OnSwitchKey(SwitchKey key)
    {
        if (!_active || _ending)
        {
            return;
        }

        switch (key)
        {
            case SwitchKey.Next:
                Move(1);
                break;
            case SwitchKey.Previous:
                Move(-1);
                break;
            case SwitchKey.Up:
            case SwitchKey.Down:
                MoveRow(key == SwitchKey.Down ? 1 : -1);
                break;
            case SwitchKey.Walk:
                Walk(1);
                break;
            case SwitchKey.WalkBack:
                Walk(-1);
                break;
            case SwitchKey.Commit:
                EndSession(commit: true);
                break;
            case SwitchKey.Cancel:
                EndSession(commit: false);
                break;
            case SwitchKey.Backspace:
                if (_query.Length > 0)
                {
                    _query = _query[..^1];
                    ApplyFilter();
                }

                break;
            case SwitchKey.ModifierReleased:
                if (!_pinned)
                {
                    EndSession(commit: true);
                }

                break;
        }
    }

    private void OnChar(char ch)
    {
        if (!_active || _ending)
        {
            return;
        }

        var searching = _query.Length > 0 || _pinned;
        if (!searching)
        {
            switch (char.ToLowerInvariant(ch))
            {
                case 'w':
                    CloseWindowAt(_selected, _view.ElementAtOrDefault(_selected)?.WindowIndex ?? 0, quitApp: false);
                    return;
                case 'q':
                    CloseWindowAt(_selected, 0, quitApp: true);
                    return;
                case 's' when _options.SearchPin:
                    _pinned = true;
                    ApplyFilter();
                    return;
            }
        }

        if (char.IsDigit(ch) && !searching && _options.Layout != SwitchLayout.Windows && ch != '0')
        {
            var index = ch - '1';
            if (index >= 0 && index < _view.Count)
            {
                _selected = index;
                _panel?.Select(index);
                return;
            }
        }

        _query += ch;
        ApplyFilter();
    }

    private void Move(int delta)
    {
        if (_view.Count == 0)
        {
            return;
        }

        _selected = ((_selected + delta) % _view.Count + _view.Count) % _view.Count;
        _panel?.Select(_selected);
    }

    private void MoveRow(int direction)
    {
        if (_view.Count == 0 || _options.Layout != SwitchLayout.Windows)
        {
            // In the icon layouts the vertical keys walk the selected app's windows.
            Walk(direction);
            return;
        }

        var columns = Math.Max(1, _options.MaxColumns >= 2 ? _options.MaxColumns : (int)Math.Round(Math.Sqrt(_view.Count)));
        var target = _selected + direction * columns;
        if (target >= 0 && target < _view.Count)
        {
            _selected = target;
            _panel?.Select(_selected);
        }
    }

    private void Walk(int delta)
    {
        if (_view.Count == 0)
        {
            return;
        }

        var entry = _view[_selected];
        if (_options.Layout == SwitchLayout.Windows || entry.Windows.Count <= 1)
        {
            Move(delta);
            return;
        }

        entry.WindowIndex = ((entry.WindowIndex + delta) % entry.Windows.Count + entry.Windows.Count) % entry.Windows.Count;
        _panel?.Select(_selected);
    }

    private void ApplyFilter()
    {
        var query = _query.Trim();
        _view = query.Length == 0
            ? _all2
            : [.. _all2.Where(e => e.AppName.Contains(query, StringComparison.CurrentCultureIgnoreCase) || e.Windows.Any(w => w.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)))];
        _selected = Math.Clamp(0, 0, Math.Max(0, _view.Count - 1));
        _panel?.Update(_view, _selected, _query, _pinned);
    }

    private void CloseWindowAt(int entryIndex, int windowIndex, bool quitApp)
    {
        if (entryIndex < 0 || entryIndex >= _view.Count)
        {
            return;
        }

        var entry = _view[entryIndex];
        if (entry.NoWindows)
        {
            return;
        }

        if (quitApp)
        {
            foreach (var w in entry.Windows)
            {
                WinApi.PostMessageW(w.Hwnd, WinApi.WM_CLOSE, 0, 0);
            }

            RemoveEntry(entry);
        }
        else
        {
            var w = entry.Windows[Math.Clamp(windowIndex, 0, entry.Windows.Count - 1)];
            WinApi.PostMessageW(w.Hwnd, WinApi.WM_CLOSE, 0, 0);
            entry.Windows.Remove(w);
            entry.WindowIndex = 0;
            if (entry.NoWindows)
            {
                RemoveEntry(entry);
            }
            else
            {
                _panel?.Update(_view, _selected, _query, _pinned);
            }
        }
    }

    private void RemoveEntry(SwitchEntry entry)
    {
        _all2 = [.. _all2.Where(e => e != entry)];
        _view = [.. _view.Where(e => e != entry)];
        _selected = Math.Clamp(_selected, 0, Math.Max(0, _view.Count - 1));
        if (_view.Count == 0)
        {
            EndSession(commit: false);
            return;
        }

        _panel?.Update(_view, _selected, _query, _pinned);
    }

    private void CommitAt(int entryIndex, int windowIndex)
    {
        if (entryIndex >= 0 && entryIndex < _view.Count)
        {
            _selected = entryIndex;
            var entry = _view[entryIndex];
            if (!entry.NoWindows)
            {
                entry.WindowIndex = Math.Clamp(windowIndex, 0, entry.Windows.Count - 1);
            }
        }

        EndSession(commit: true);
    }

    private void EndSession(bool commit)
    {
        if (_ending)
        {
            return;
        }

        _ending = true;
        _showTimer?.Stop();
        _showTimer = null;
        var entry = commit && _selected >= 0 && _selected < _view.Count ? _view[_selected] : null;
        _panel?.Close();
        _panel = null;
        _panelKnown = false;
        _mouse?.Dispose();
        _mouse = null;
        _active = false;
        _view = [];
        _all2 = [];
        if (entry is not null)
        {
            Activate(entry);
        }
    }

    private void Activate(SwitchEntry entry)
    {
        try
        {
            if (entry.Current is { } window)
            {
                WindowUseTracker.Touch(window.Hwnd);
                WinUtil.Activate(window.Hwnd);
            }
            else if (entry.ExePath is { } path)
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            _context?.Log.Warn($"Switcher could not activate '{entry.AppName}': {ex.Message}");
        }
    }
}
