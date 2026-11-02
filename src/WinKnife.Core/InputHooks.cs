// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows.Input;
using WinKnife.Core.Native;
using static WinKnife.Core.Native.NativeConstants;

namespace WinKnife.Core;

/// <summary>Classification of a low-level mouse message.</summary>
public enum MouseEventKind
{
    Move,
    LeftDown,
    LeftUp,
    RightDown,
    RightUp,
    MiddleDown,
    MiddleUp,
    XButtonDown,
    XButtonUp,
    Wheel,
    HorizontalWheel,
    Other,
}

/// <summary>A low-level keyboard event (WH_KEYBOARD_LL). Delivered on the hook thread.</summary>
/// <param name="Message">WM_KEYDOWN, WM_KEYUP, WM_SYSKEYDOWN or WM_SYSKEYUP.</param>
/// <param name="VkCode">Virtual key code.</param>
/// <param name="ScanCode">Hardware scan code.</param>
/// <param name="Flags">Raw LLKHF_* flags.</param>
/// <param name="Timestamp">Event time in milliseconds (GetTickCount base).</param>
/// <param name="ExtraInfo">dwExtraInfo; equals <see cref="InputSender.WinKnifeExtraInfo"/> for input injected by WinKnife.</param>
/// <param name="Modifiers">Modifier keys held at the time the hook ran (state before this event is applied).</param>
public readonly record struct KeyboardHookEvent(
    int Message,
    int VkCode,
    int ScanCode,
    uint Flags,
    uint Timestamp,
    nuint ExtraInfo,
    ModifierKeys Modifiers)
{
    /// <summary>True for key-down messages (including system key-down).</summary>
    public bool IsKeyDown => Message is (int)WM_KEYDOWN or (int)WM_SYSKEYDOWN;

    /// <summary>True for key-up messages.</summary>
    public bool IsKeyUp => !IsKeyDown;

    /// <summary>True when the event was injected by any program (SendInput etc.).</summary>
    public bool IsInjected => (Flags & LLKHF_INJECTED) != 0;

    /// <summary>True when the event was injected by <see cref="InputSender"/>.</summary>
    public bool IsFromWinKnife => ExtraInfo == InputSender.WinKnifeExtraInfo;

    /// <summary>True for extended keys (right Alt/Ctrl, arrows, numpad Enter...).</summary>
    public bool IsExtended => (Flags & LLKHF_EXTENDED) != 0;

    /// <summary>The WPF key corresponding to <see cref="VkCode"/>.</summary>
    public Key Key => KeyInterop.KeyFromVirtualKey(VkCode);
}

/// <summary>A low-level mouse event (WH_MOUSE_LL). Delivered on the hook thread.</summary>
/// <param name="Message">The WM_* mouse message.</param>
/// <param name="X">Cursor X in physical screen pixels.</param>
/// <param name="Y">Cursor Y in physical screen pixels.</param>
/// <param name="WheelDelta">Signed wheel delta (multiples of 120) for wheel messages, otherwise 0.</param>
/// <param name="XButton">1 for XBUTTON1 (back), 2 for XBUTTON2 (forward) on X button messages, otherwise 0.</param>
/// <param name="Flags">Raw LLMHF_* flags.</param>
/// <param name="Timestamp">Event time in milliseconds.</param>
/// <param name="ExtraInfo">dwExtraInfo; equals <see cref="InputSender.WinKnifeExtraInfo"/> for input injected by WinKnife.</param>
/// <param name="Modifiers">Modifier keys held at the time the hook ran.</param>
public readonly record struct MouseHookEvent(
    int Message,
    int X,
    int Y,
    int WheelDelta,
    int XButton,
    uint Flags,
    uint Timestamp,
    nuint ExtraInfo,
    ModifierKeys Modifiers)
{
    /// <summary>True when the event was injected by any program.</summary>
    public bool IsInjected => (Flags & LLMHF_INJECTED) != 0;

    /// <summary>True when the event was injected by <see cref="InputSender"/>.</summary>
    public bool IsFromWinKnife => ExtraInfo == InputSender.WinKnifeExtraInfo;

    /// <summary>The kind of mouse event.</summary>
    public MouseEventKind Kind => (uint)Message switch
    {
        WM_MOUSEMOVE => MouseEventKind.Move,
        WM_LBUTTONDOWN => MouseEventKind.LeftDown,
        WM_LBUTTONUP => MouseEventKind.LeftUp,
        WM_RBUTTONDOWN => MouseEventKind.RightDown,
        WM_RBUTTONUP => MouseEventKind.RightUp,
        WM_MBUTTONDOWN => MouseEventKind.MiddleDown,
        WM_MBUTTONUP => MouseEventKind.MiddleUp,
        WM_XBUTTONDOWN => MouseEventKind.XButtonDown,
        WM_XBUTTONUP => MouseEventKind.XButtonUp,
        WM_MOUSEWHEEL => MouseEventKind.Wheel,
        WM_MOUSEHWHEEL => MouseEventKind.HorizontalWheel,
        _ => MouseEventKind.Other,
    };

    /// <summary>True for vertical or horizontal wheel events.</summary>
    public bool IsWheel => Kind is MouseEventKind.Wheel or MouseEventKind.HorizontalWheel;

    /// <summary>True for any button-press message.</summary>
    public bool IsButtonDown => Kind is MouseEventKind.LeftDown or MouseEventKind.RightDown or MouseEventKind.MiddleDown or MouseEventKind.XButtonDown;

    /// <summary>True for any button-release message.</summary>
    public bool IsButtonUp => Kind is MouseEventKind.LeftUp or MouseEventKind.RightUp or MouseEventKind.MiddleUp or MouseEventKind.XButtonUp;
}

/// <summary>
/// Shared low-level keyboard and mouse hooks. Handlers run ON THE HOOK THREAD and must return in well
/// under 1 ms (Windows silently removes hooks that time out): marshal real work to the dispatcher.
/// Higher <c>priority</c> handlers run first; the first handler that returns <c>true</c> swallows the event
/// and lower-priority handlers do not see it. Hooks are installed lazily while at least one subscriber exists.
/// </summary>
public interface IInputHooks
{
    /// <summary>Subscribes to keyboard events. Return <c>true</c> from the handler to swallow the event.</summary>
    IDisposable SubscribeKeyboard(Func<KeyboardHookEvent, bool> handler, int priority = 0);

    /// <summary>Subscribes to mouse events. Return <c>true</c> from the handler to swallow the event.</summary>
    IDisposable SubscribeMouse(Func<MouseHookEvent, bool> handler, int priority = 0);
}

/// <summary>Default <see cref="IInputHooks"/> implementation: one dedicated thread with a message loop.</summary>
public sealed class InputHooks : IInputHooks, IDisposable
{
    private const uint WmSync = WM_APP + 1;

    private sealed class Subscription<T> : IDisposable
    {
        private readonly Action<Subscription<T>> _remove;

        public Subscription(Action<Subscription<T>> remove, Func<T, bool> handler, int priority, long sequence)
        {
            _remove = remove;
            Handler = handler;
            Priority = priority;
            Sequence = sequence;
        }

        public Func<T, bool> Handler { get; }

        public int Priority { get; }

        public long Sequence { get; }

        public void Dispose() => _remove(this);
    }

    private readonly ILog _log;
    private readonly object _gate = new();
    private readonly HookProc _keyboardProc;
    private readonly HookProc _mouseProc;
    private Subscription<KeyboardHookEvent>[] _keyboard = [];
    private Subscription<MouseHookEvent>[] _mouse = [];
    private Thread? _thread;
    private uint _threadId;
    private ManualResetEventSlim? _ready;
    private nint _keyboardHook;
    private nint _mouseHook;
    private long _sequence;
    private bool _disposed;
    private long _lastErrorTick;

    /// <summary>Creates the hook service; no thread or hook exists until the first subscription.</summary>
    public InputHooks(ILog log)
    {
        _log = log;
        _keyboardProc = KeyboardCallback;
        _mouseProc = MouseCallback;
    }

    /// <inheritdoc />
    public IDisposable SubscribeKeyboard(Func<KeyboardHookEvent, bool> handler, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Subscription<KeyboardHookEvent> sub;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            sub = new Subscription<KeyboardHookEvent>(RemoveKeyboard, handler, priority, ++_sequence);
            _keyboard = Sorted([.. _keyboard, sub]);
        }

        RequestSync();
        return sub;
    }

    /// <inheritdoc />
    public IDisposable SubscribeMouse(Func<MouseHookEvent, bool> handler, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Subscription<MouseHookEvent> sub;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            sub = new Subscription<MouseHookEvent>(RemoveMouse, handler, priority, ++_sequence);
            _mouse = Sorted([.. _mouse, sub]);
        }

        RequestSync();
        return sub;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Thread? thread;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _keyboard = [];
            _mouse = [];
            thread = _thread;
        }

        if (thread is not null)
        {
            NativeMethods.PostThreadMessageW(_threadId, WM_QUIT, 0, 0);
            thread.Join(TimeSpan.FromSeconds(2));
        }
    }

    private static Subscription<T>[] Sorted<T>(Subscription<T>[] subs) =>
        [.. subs.OrderByDescending(s => s.Priority).ThenBy(s => s.Sequence)];

    private void RemoveKeyboard(Subscription<KeyboardHookEvent> sub)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _keyboard = Array.FindAll(_keyboard, s => !ReferenceEquals(s, sub));
        }

        RequestSync();
    }

    private void RemoveMouse(Subscription<MouseHookEvent> sub)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _mouse = Array.FindAll(_mouse, s => !ReferenceEquals(s, sub));
        }

        RequestSync();
    }

    private void RequestSync()
    {
        ManualResetEventSlim ready;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_thread is null)
            {
                ready = _ready = new ManualResetEventSlim(false);
                _thread = new Thread(HookThread)
                {
                    IsBackground = true,
                    Name = "WinKnife.InputHooks",
                    Priority = ThreadPriority.Highest,
                };
                _thread.Start();
            }
            else
            {
                ready = _ready!;
            }
        }

        ready.Wait();
        NativeMethods.PostThreadMessageW(_threadId, WmSync, 0, 0);
    }

    private void HookThread()
    {
        // Touch the queue so PostThreadMessage works, then publish the thread id.
        NativeMethods.PeekMessageW(out _, 0, WM_USER, WM_USER, PM_NOREMOVE);
        _threadId = NativeMethods.GetCurrentThreadId();
        _ready!.Set();

        try
        {
            Reconcile();
            while (NativeMethods.GetMessageW(out var msg, 0, 0, 0) > 0)
            {
                if (msg.message == WmSync)
                {
                    Reconcile();
                }
                else
                {
                    NativeMethods.TranslateMessage(ref msg);
                    NativeMethods.DispatchMessageW(ref msg);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error("Input hook thread crashed", ex);
        }
        finally
        {
            Uninstall(ref _keyboardHook);
            Uninstall(ref _mouseHook);
        }
    }

    private void Reconcile()
    {
        var wantKeyboard = _keyboard.Length > 0;
        var wantMouse = _mouse.Length > 0;
        Sync(ref _keyboardHook, wantKeyboard, WH_KEYBOARD_LL, _keyboardProc, "keyboard");
        Sync(ref _mouseHook, wantMouse, WH_MOUSE_LL, _mouseProc, "mouse");
    }

    private void Sync(ref nint hook, bool wanted, int hookId, HookProc proc, string name)
    {
        if (wanted && hook == 0)
        {
            hook = NativeMethods.SetWindowsHookExW(hookId, proc, NativeMethods.GetModuleHandleW(null), 0);
            if (hook == 0)
            {
                _log.Error($"Installing the {name} hook failed (Win32 error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()})");
            }
            else
            {
                _log.Info($"Installed {name} hook");
            }
        }
        else if (!wanted && hook != 0)
        {
            Uninstall(ref hook);
            _log.Info($"Removed {name} hook");
        }
    }

    private static void Uninstall(ref nint hook)
    {
        if (hook != 0)
        {
            NativeMethods.UnhookWindowsHookEx(hook);
            hook = 0;
        }
    }

    private unsafe nint KeyboardCallback(int nCode, nuint wParam, nint lParam)
    {
        if (nCode == HC_ACTION)
        {
            var subs = _keyboard;
            if (subs.Length > 0)
            {
                var data = *(KBDLLHOOKSTRUCT*)lParam;
                var evt = new KeyboardHookEvent((int)wParam, (int)data.vkCode, (int)data.scanCode, data.flags, data.time, data.dwExtraInfo, ReadModifiers());
                foreach (var sub in subs)
                {
                    try
                    {
                        if (sub.Handler(evt))
                        {
                            return 1;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogHandlerError("keyboard", ex);
                    }
                }
            }
        }

        return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private unsafe nint MouseCallback(int nCode, nuint wParam, nint lParam)
    {
        if (nCode == HC_ACTION)
        {
            var subs = _mouse;
            if (subs.Length > 0)
            {
                var data = *(MSLLHOOKSTRUCT*)lParam;
                var message = (uint)wParam;
                var high = (short)(data.mouseData >> 16);
                var wheel = message is WM_MOUSEWHEEL or WM_MOUSEHWHEEL ? high : 0;
                var xButton = message is WM_XBUTTONDOWN or WM_XBUTTONUP ? high : 0;
                var evt = new MouseHookEvent((int)message, data.pt.X, data.pt.Y, wheel, xButton, data.flags, data.time, data.dwExtraInfo, ReadModifiers());
                foreach (var sub in subs)
                {
                    try
                    {
                        if (sub.Handler(evt))
                        {
                            return 1;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogHandlerError("mouse", ex);
                    }
                }
            }
        }

        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private static ModifierKeys ReadModifiers()
    {
        var mods = ModifierKeys.None;
        if (NativeMethods.GetAsyncKeyState(VK_CONTROL) < 0)
        {
            mods |= ModifierKeys.Control;
        }

        if (NativeMethods.GetAsyncKeyState(VK_MENU) < 0)
        {
            mods |= ModifierKeys.Alt;
        }

        if (NativeMethods.GetAsyncKeyState(VK_SHIFT) < 0)
        {
            mods |= ModifierKeys.Shift;
        }

        if (NativeMethods.GetAsyncKeyState(VK_LWIN) < 0 || NativeMethods.GetAsyncKeyState(VK_RWIN) < 0)
        {
            mods |= ModifierKeys.Windows;
        }

        return mods;
    }

    private void LogHandlerError(string kind, Exception ex)
    {
        // Throttle: a faulty handler would otherwise flood the log on every event.
        var now = Environment.TickCount64;
        if (now - Interlocked.Exchange(ref _lastErrorTick, now) > 1000)
        {
            _log.Error($"Unhandled exception in a {kind} hook handler", ex);
        }
    }
}
