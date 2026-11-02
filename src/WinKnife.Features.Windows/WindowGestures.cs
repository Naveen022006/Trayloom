// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows.Input;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Native;
using static WinKnife.Core.Native.NativeConstants;

namespace WinKnife.Features.WindowTools;

/// <summary>Modifier set of the move gesture (<c>windowGestureModifiers</c>, tokens joined by '+': control, option, shift, command).</summary>
internal static class GestureModifiers
{
    public const string Default = "control+option";

    public static HashSet<string> Parse(string? value)
    {
        var tokens = (value ?? string.Empty).Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t is "control" or "option" or "command").ToHashSet();
        return tokens.Count == 0 ? [.. Default.Split('+')] : tokens;
    }

    public static string Serialize(IEnumerable<string> tokens)
    {
        var set = tokens.ToHashSet();
        return string.Join('+', new[] { "control", "option", "shift", "command" }.Where(set.Contains));
    }

    public static ModifierKeys ToKeys(IEnumerable<string> tokens)
    {
        var keys = ModifierKeys.None;
        foreach (var t in tokens)
        {
            keys |= t switch
            {
                "control" => ModifierKeys.Control,
                "option" => ModifierKeys.Alt,
                "command" => ModifierKeys.Windows,
                _ => ModifierKeys.None,
            };
        }

        return keys;
    }

    /// <summary>Windows keycap label.</summary>
    public static string Label(string token) => token switch
    {
        "control" => "Ctrl",
        "option" => "Alt",
        "command" => "Win",
        _ => Loc.T("Shift"),
    };
}

/// <summary>
/// Move and resize by dragging with a modifier chord (spec A6.2). The press is held (swallowed) until the pointer travels more than
/// 6 px; releasing earlier replays the click. Frame writes go through a worker so a slow app never stalls the hook.
/// </summary>
internal sealed class WindowGestures : IDisposable
{
    private const int DragSlop = 6;
    private const int MinWidth = 120;
    private const int MinHeight = 80;

    private enum Phase
    {
        Idle,
        Pending,
        Moving,
        Resizing,
    }

    private readonly IAppSettings _app;
    private readonly WindowLayoutEngine _engine;
    private readonly EdgeSnapController _edge;
    private readonly Dispatcher _ui;
    private readonly object _gate = new();
    private readonly AutoResetEvent _wake = new(false);
    private IDisposable? _subscription;
    private Thread? _worker;
    private volatile bool _stop;
    private (nint Hwnd, int X, int Y, int W, int H)? _pending;
    private Phase _phase;
    private nint _target;
    private bool _rightButton;
    private bool _shiftResize;
    private POINT _origin;
    private RECT _startRect;
    private int _regionX;
    private int _regionY;
    private ModifierKeys _required;

    public WindowGestures(IAppSettings app, WindowLayoutEngine engine, EdgeSnapController edge, Dispatcher ui)
    {
        _app = app;
        _engine = engine;
        _edge = edge;
        _ui = ui;
    }

    public bool IsRunning => _subscription is not null;

    public void Start(IInputHooks hooks)
    {
        if (_subscription is not null)
        {
            return;
        }

        _stop = false;
        _required = GestureModifiers.ToKeys(GestureModifiers.Parse(_app.Get<string>(Keys.GestureModifiers)));
        _worker = new Thread(WriterLoop) { IsBackground = true, Name = "WinKnife window gesture writer" };
        _worker.Start();
        _subscription = hooks.SubscribeMouse(OnMouse, priority: 80);
    }

    public void Refresh() => _required = GestureModifiers.ToKeys(GestureModifiers.Parse(_app.Get<string>(Keys.GestureModifiers)));

    public void Stop() => Dispose();

    public void Dispose()
    {
        _subscription?.Dispose();
        _subscription = null;
        _stop = true;
        _wake.Set();
        _phase = Phase.Idle;
    }

    private bool ChordHeld(ModifierKeys held, out bool shift)
    {
        shift = held.HasFlag(ModifierKeys.Shift);
        var rest = held & ~ModifierKeys.Shift;
        return _required != ModifierKeys.None && rest == _required;
    }

    private bool OnMouse(MouseHookEvent e)
    {
        if (e.IsFromWinKnife)
        {
            return false;
        }

        switch (e.Kind)
        {
            case MouseEventKind.LeftDown or MouseEventKind.RightDown when _phase == Phase.Idle:
                return OnDown(e);
            case MouseEventKind.Move when _phase != Phase.Idle:
                OnMove(e);
                return false;
            case MouseEventKind.LeftUp when _phase != Phase.Idle && !_rightButton:
            case MouseEventKind.RightUp when _phase != Phase.Idle && _rightButton:
                return OnUp(e);
            case MouseEventKind.LeftDown or MouseEventKind.RightDown or MouseEventKind.MiddleDown when _phase != Phase.Idle:
                return true; // a second button during the gesture is swallowed (custody)
            default:
                return false;
        }
    }

    private bool OnDown(MouseHookEvent e)
    {
        if (!ChordHeld(e.Modifiers, out var shift))
        {
            return false;
        }

        var right = e.Kind == MouseEventKind.RightDown;
        var hwnd = WinApi.WindowFromPoint(new POINT { X = e.X, Y = e.Y });
        if (hwnd == 0)
        {
            return false;
        }

        hwnd = WinApi.GetAncestor(hwnd, WinApi.GA_ROOT) is var root and not 0 ? root : hwnd;
        if (!WindowLayoutEngine.IsMovable(hwnd) || _engine.IsIgnored(hwnd) || IsOwnWindow(hwnd))
        {
            return false;
        }

        _target = hwnd;
        _rightButton = right;
        _shiftResize = shift || right;
        _origin = new POINT { X = e.X, Y = e.Y };
        NativeMethods.GetWindowRect(hwnd, out _startRect);
        _phase = Phase.Pending;
        if (_required.HasFlag(ModifierKeys.Windows))
        {
            // A Win key that sees another key press does not open the Start menu on release.
            ThreadPool.QueueUserWorkItem(_ =>
            {
                InputSender.SendKey(0xE8);
                InputSender.SendKey(0xE8, keyUp: true);
            });
        }

        return true;
    }

    private static bool IsOwnWindow(nint hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    private void OnMove(MouseHookEvent e)
    {
        var dx = e.X - _origin.X;
        var dy = e.Y - _origin.Y;
        if (_phase == Phase.Pending)
        {
            if (dx * dx + dy * dy <= DragSlop * DragSlop)
            {
                return;
            }

            Promote();
        }

        if (_phase == Phase.Moving)
        {
            Post(_target, _startRect.Left + dx, _startRect.Top + dy, _startRect.Width, _startRect.Height);
        }
        else if (_phase == Phase.Resizing)
        {
            var left = _startRect.Left;
            var top = _startRect.Top;
            var right = _startRect.Right;
            var bottom = _startRect.Bottom;
            if (_regionX < 0)
            {
                left = Math.Min(left + dx, right - MinWidth);
            }
            else if (_regionX > 0)
            {
                right = Math.Max(right + dx, left + MinWidth);
            }

            if (_regionY < 0)
            {
                top = Math.Min(top + dy, bottom - MinHeight);
            }
            else if (_regionY > 0)
            {
                bottom = Math.Max(bottom + dy, top + MinHeight);
            }

            Post(_target, left, top, right - left, bottom - top);
        }
    }

    private void Promote()
    {
        var target = _target;
        if (WinApi.IsZoomed(target))
        {
            // Dragging a maximized window: restore it first, keeping the pointer at the same relative spot.
            var maxRect = _startRect;
            WinApi.ShowWindow(target, WinApi.SW_RESTORE);
            NativeMethods.GetWindowRect(target, out var restored);
            var fx = (_origin.X - maxRect.Left) / (double)Math.Max(1, maxRect.Width);
            _startRect = new RECT
            {
                Left = (int)(_origin.X - restored.Width * fx),
                Top = Math.Max(_origin.Y - 16, maxRect.Top),
                Right = (int)(_origin.X - restored.Width * fx) + restored.Width,
                Bottom = Math.Max(_origin.Y - 16, maxRect.Top) + restored.Height,
            };
        }

        if (_app.Get<bool>(Keys.GestureRaise))
        {
            NativeMethods.SetWindowPos(target, (nint)0, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        if (_shiftResize)
        {
            var fx = (_origin.X - _startRect.Left) / (double)Math.Max(1, _startRect.Width);
            var fy = (_origin.Y - _startRect.Top) / (double)Math.Max(1, _startRect.Height);
            _regionX = fx < 0.34 ? -1 : fx > 0.66 ? 1 : 0;
            _regionY = fy < 0.34 ? -1 : fy > 0.66 ? 1 : 0;
            if (_regionX == 0 && _regionY == 0)
            {
                // The centre resizes from the nearest edge.
                var distances = new[]
                {
                    (_origin.X - _startRect.Left, -1, 0), (_startRect.Right - _origin.X, 1, 0),
                    (_origin.Y - _startRect.Top, 0, -1), (_startRect.Bottom - _origin.Y, 0, 1),
                };
                var nearest = distances.OrderBy(d => d.Item1).First();
                _regionX = nearest.Item2;
                _regionY = nearest.Item3;
            }

            _phase = Phase.Resizing;
        }
        else
        {
            _phase = Phase.Moving;
            var hwnd = _target;
            _ui.BeginInvoke(() => _edge.BeginMove(hwnd));
        }
    }

    private bool OnUp(MouseHookEvent e)
    {
        var phase = _phase;
        _phase = Phase.Idle;
        if (phase == Phase.Pending)
        {
            // Never moved: replay the press and release so modifier-clicks keep working.
            var button = _rightButton ? MouseButton.Right : MouseButton.Left;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                InputSender.SendMouseButton(button, true);
                InputSender.SendMouseButton(button, false);
            });
            return true;
        }

        if (phase == Phase.Moving)
        {
            _ui.BeginInvoke(() => _edge.EndMove(apply: true));
        }

        return true;
    }

    private void Post(nint hwnd, int x, int y, int w, int h)
    {
        lock (_gate)
        {
            _pending = (hwnd, x, y, w, h);
        }

        _wake.Set();
    }

    private void WriterLoop()
    {
        while (!_stop)
        {
            _wake.WaitOne(250);
            (nint Hwnd, int X, int Y, int W, int H)? frame;
            lock (_gate)
            {
                frame = _pending;
                _pending = null;
            }

            if (frame is { } f && WinApi.IsWindow(f.Hwnd))
            {
                NativeMethods.SetWindowPos(f.Hwnd, 0, f.X, f.Y, f.W, f.H, SWP_NOZORDER | SWP_NOACTIVATE | WinApi.SWP_ASYNCWINDOWPOS);
            }
        }
    }
}
