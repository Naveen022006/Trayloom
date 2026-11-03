// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Core.Native;
using static Trayloom.Core.Native.NativeConstants;

namespace Trayloom.Features.WindowTools;

/// <summary>Which edge of the screen the taskbar sits on.</summary>
internal enum TaskbarEdge
{
    Bottom,
    Top,
    Left,
    Right,
}

/// <summary>A taskbar button under the pointer (UI Automation) and the windows of its app.</summary>
internal sealed class TaskbarButton
{
    public RECT Bounds { get; init; }

    public string Name { get; init; } = string.Empty;

    public string AutomationId { get; init; } = string.Empty;

    public TaskbarEdge Edge { get; init; }

    public RECT TaskbarRect { get; init; }

    /// <summary>Top-level windows of the app (filled on the UI thread).</summary>
    public List<SwitchWindow> Windows { get; set; } = [];

    public DateTime MappedAt { get; set; }

    public string? AppPath { get; set; }

    public string Key => AutomationId.Length > 0 ? AutomationId : Name;
}

/// <summary>
/// Finds the taskbar button under the pointer without elevation (spec 05 A2.3): mouse moves inside the taskbar rectangles wake a
/// worker that asks UI Automation what is there (Windows 11 <c>Taskbar.TaskListButtonAutomationPeer</c>, Windows 10 <c>MSTaskListWClass</c>
/// buttons). Shared by Taskbar Preview and Taskbar clicks; nothing runs while neither is on.
/// </summary>
internal sealed class TaskbarTracker
{
    private static readonly Regex RunningSuffix = new(@"\s+-\s+\d+\s+running\s+windows?\s*$|\s+-\s+\d+\s+windows?\s*$|\s+pinned\s*$|\s+-\s+\d+\s+running\s+window\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly Dispatcher _ui;
    private readonly AutoResetEvent _wake = new(false);
    private readonly object _gate = new();
    private IDisposable? _subscription;
    private Thread? _worker;
    private volatile bool _stop;
    private (int X, int Y)? _point;
    private volatile bool _inside;
    private volatile bool _hadButton;
    private RECT[] _rects = [];
    private DateTime _rectsAt = DateTime.MinValue;
    private TaskbarButton? _current;
    private int _users;

    public TaskbarTracker(Dispatcher ui) => _ui = ui;

    /// <summary>The button under the pointer (UI thread value, updated by the worker).</summary>
    public TaskbarButton? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>Raised on the UI thread when the hovered button changed (null = pointer left the buttons).</summary>
    public event Action<TaskbarButton?>? Changed;

    /// <summary>False when UI Automation could not read the taskbar.</summary>
    public bool Readable { get; private set; } = true;

    public void Acquire(IInputHooks hooks)
    {
        if (_users++ > 0)
        {
            return;
        }

        _stop = false;
        RefreshRects();
        _worker = new Thread(Loop) { IsBackground = true, Name = "Trayloom taskbar hit test" };
        _worker.SetApartmentState(ApartmentState.MTA);
        _worker.Start();
        _subscription = hooks.SubscribeMouse(OnMouse, priority: 20);
    }

    public void Release()
    {
        if (--_users > 0)
        {
            return;
        }

        _users = 0;
        _subscription?.Dispose();
        _subscription = null;
        _stop = true;
        _wake.Set();
        lock (_gate)
        {
            _current = null;
        }
    }

    /// <summary>Pointer position inside one of the taskbar rectangles (hook thread, cheap).</summary>
    public bool IsOverTaskbar(int x, int y) => Contains(_rects, x, y, 2);

    private static bool Contains(RECT[] rects, int x, int y, int margin)
    {
        foreach (var r in rects)
        {
            if (x >= r.Left - margin && x < r.Right + margin && y >= r.Top - margin && y < r.Bottom + margin)
            {
                return true;
            }
        }

        return false;
    }

    private bool OnMouse(MouseHookEvent e)
    {
        if (e.Kind != MouseEventKind.Move)
        {
            return false;
        }

        if (DateTime.UtcNow - _rectsAt > TimeSpan.FromSeconds(3))
        {
            _rectsAt = DateTime.UtcNow;
            _ui.BeginInvoke(RefreshRects);
        }

        var inside = Contains(_rects, e.X, e.Y, 2);
        if (inside)
        {
            lock (_gate)
            {
                _point = (e.X, e.Y);
            }

            _inside = true;
            _wake.Set();
        }
        else if (_inside || _hadButton)
        {
            _inside = false;
            lock (_gate)
            {
                _point = null;
            }

            _wake.Set();
        }

        return false;
    }

    private void RefreshRects()
    {
        var list = new List<RECT>();
        WinApi.EnumWindows((hwnd, _) =>
        {
            var cls = WinApi.GetClass(hwnd);
            if (cls is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" && WinApi.IsWindowVisible(hwnd))
            {
                NativeMethods.GetWindowRect(hwnd, out var rect);
                list.Add(rect);
            }

            return true;
        }, 0);
        _rects = [.. list];
    }

    private void Loop()
    {
        while (!_stop)
        {
            _wake.WaitOne(500);
            if (_stop)
            {
                break;
            }

            (int X, int Y)? point;
            lock (_gate)
            {
                point = _point;
            }

            TaskbarButton? button = null;
            if (point is { } p)
            {
                try
                {
                    button = HitTest(p.X, p.Y);
                    Readable = true;
                }
                catch (Exception)
                {
                    Readable = false;
                }

                Thread.Sleep(16); // 60 Hz cap
            }

            bool changed;
            lock (_gate)
            {
                changed = !Same(_current, button);
                _current = button;
            }

            _hadButton = button is not null;
            if (changed)
            {
                var snapshot = button;
                _ui.BeginInvoke(() =>
                {
                    if (snapshot is not null)
                    {
                        TaskbarMapper.Fill(snapshot);
                    }

                    Changed?.Invoke(snapshot);
                });
            }
        }
    }

    private static bool Same(TaskbarButton? a, TaskbarButton? b) =>
        ReferenceEquals(a, b) || (a is not null && b is not null && a.Key == b.Key && a.Bounds.Left == b.Bounds.Left && a.Bounds.Top == b.Bounds.Top);

    private TaskbarButton? HitTest(int x, int y)
    {
        var element = AutomationElement.FromPoint(new System.Windows.Point(x, y));
        // Walk up to the button (Windows 11: TaskListButtonAutomationPeer, Windows 10: a Button under MSTaskListWClass).
        var walker = TreeWalker.ControlViewWalker;
        var node = element;
        for (var depth = 0; node is not null && depth < 6; depth++)
        {
            var info = node.Current;
            var cls = info.ClassName ?? string.Empty;
            var id = info.AutomationId ?? string.Empty;
            if (cls.Contains("TaskListButton", StringComparison.OrdinalIgnoreCase) || id.StartsWith("Appid:", StringComparison.OrdinalIgnoreCase)
                || (info.ControlType == ControlType.Button && (node.Current.Name ?? string.Empty).Length > 0 && IsTaskList(walker, node)))
            {
                var r = info.BoundingRectangle;
                if (r.IsEmpty)
                {
                    return null;
                }

                var rect = new RECT { Left = (int)r.Left, Top = (int)r.Top, Right = (int)r.Right, Bottom = (int)r.Bottom };
                var bar = EnclosingTaskbar(rect);
                return new TaskbarButton
                {
                    Bounds = rect,
                    Name = info.Name ?? string.Empty,
                    AutomationId = id,
                    Edge = EdgeOf(bar),
                    TaskbarRect = bar,
                };
            }

            node = walker.GetParent(node);
        }

        return null;
    }

    private static bool IsTaskList(TreeWalker walker, AutomationElement node)
    {
        var parent = walker.GetParent(node);
        for (var i = 0; parent is not null && i < 3; i++)
        {
            var cls = parent.Current.ClassName ?? string.Empty;
            if (cls is "MSTaskListWClass" or "MSTaskSwWClass" or "ReBarWindow32" or "Taskbar.TaskListControl")
            {
                return true;
            }

            parent = walker.GetParent(parent);
        }

        return false;
    }

    private RECT EnclosingTaskbar(RECT button)
    {
        foreach (var r in _rects)
        {
            if (button.Left >= r.Left - 2 && button.Right <= r.Right + 2 && button.Top >= r.Top - 2 && button.Bottom <= r.Bottom + 2)
            {
                return r;
            }
        }

        return _rects.Length > 0 ? _rects[0] : button;
    }

    private static TaskbarEdge EdgeOf(RECT bar)
    {
        var monitor = NativeMethods.MonitorFromPoint(new POINT { X = (bar.Left + bar.Right) / 2, Y = (bar.Top + bar.Bottom) / 2 }, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        NativeMethods.GetMonitorInfoW(monitor, ref info);
        if (bar.Width > bar.Height)
        {
            return bar.Top <= info.rcMonitor.Top + 4 ? TaskbarEdge.Top : TaskbarEdge.Bottom;
        }

        return bar.Left <= info.rcMonitor.Left + 4 ? TaskbarEdge.Left : TaskbarEdge.Right;
    }

    /// <summary>The label of a button without the " - 3 running windows" suffix.</summary>
    public static string CleanName(string name) => RunningSuffix.Replace(name, string.Empty).Trim();
}

/// <summary>Maps a taskbar button to the windows of its app (AUMID, executable, display name).</summary>
internal static class TaskbarMapper
{
    public static void Fill(TaskbarButton button)
    {
        button.Windows = Windows(button, out var path);
        button.AppPath = path;
        button.MappedAt = DateTime.UtcNow;
    }

    public static List<SwitchWindow> Windows(TaskbarButton button, out string? path)
    {
        path = null;
        var label = TaskbarTracker.CleanName(button.Name);
        var id = button.AutomationId.StartsWith("Appid:", StringComparison.OrdinalIgnoreCase) ? button.AutomationId[6..].Trim() : button.AutomationId;
        var all = SwitcherModel.Windows(includeOtherDesktops: true);
        var scored = new Dictionary<string, (int Score, List<SwitchWindow> Windows, string? Path)>(StringComparer.OrdinalIgnoreCase);
        foreach (var window in all)
        {
            var owner = AppIdentity.OwnerOf(window.Hwnd);
            var key = owner.Path ?? ("pid" + owner.Pid);
            var aumid = WinApi.GetWindowAumid(window.Hwnd) ?? string.Empty;
            var name = AppIdentity.AppName(owner.Path);
            var score = 0;
            if (id.Length > 0 && aumid.Length > 0 && string.Equals(id, aumid, StringComparison.OrdinalIgnoreCase))
            {
                score = 100;
            }
            else if (id.Length > 0 && owner.Path is not null && id.Contains(owner.FileName, StringComparison.OrdinalIgnoreCase))
            {
                score = 90;
            }
            else if (label.Length > 0 && string.Equals(label, name, StringComparison.OrdinalIgnoreCase))
            {
                score = 80;
            }
            else if (label.Length > 0 && label.Equals("File Explorer", StringComparison.OrdinalIgnoreCase) && owner.Name == "explorer")
            {
                score = 80;
            }
            else if (label.Length > 0 && (string.Equals(label, window.Title, StringComparison.OrdinalIgnoreCase) || (owner.Name.Length > 2 && label.Contains(owner.Name, StringComparison.OrdinalIgnoreCase))))
            {
                score = 50;
            }

            if (score == 0)
            {
                continue;
            }

            if (!scored.TryGetValue(key, out var entry))
            {
                entry = (score, [], owner.Path);
            }

            entry.Windows.Add(window);
            scored[key] = (Math.Max(entry.Score, score), entry.Windows, entry.Path);
        }

        if (scored.Count == 0)
        {
            return [];
        }

        var best = scored.Values.OrderByDescending(v => v.Score).ThenByDescending(v => v.Windows.Count).First();
        path = best.Path;
        return best.Windows;
    }
}
