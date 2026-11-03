// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Core.Native;
using Trayloom.Core.Ui;
using static Trayloom.Core.Native.NativeConstants;

namespace Trayloom.Features.WindowTools;

/// <summary>The eight screen-edge zones of the snap map.</summary>
internal enum SnapZone
{
    TopLeft,
    Top,
    TopRight,
    Left,
    Right,
    BottomLeft,
    Bottom,
    BottomRight,
}

/// <summary>Zone configuration (<c>windowEdgeSnapDisabledZones</c> + <c>windowEdgeSnapZoneActions</c>) with Vorssaint's defaults.</summary>
internal static class EdgeSnapModel
{
    public const int MaximumParts = 4;

    public static readonly IReadOnlyList<SnapZone> Zones = Enum.GetValues<SnapZone>();

    public static string Id(SnapZone zone) => zone switch
    {
        SnapZone.TopLeft => "topLeft",
        SnapZone.Top => "top",
        SnapZone.TopRight => "topRight",
        SnapZone.Left => "left",
        SnapZone.Right => "right",
        SnapZone.BottomLeft => "bottomLeft",
        SnapZone.Bottom => "bottom",
        _ => "bottomRight",
    };

    public static string PlaceName(SnapZone zone) => zone switch
    {
        SnapZone.TopLeft => Loc.T("Top left"),
        SnapZone.Top => Loc.T("Top center"),
        SnapZone.TopRight => Loc.T("Top right"),
        SnapZone.Left => Loc.T("Center left"),
        SnapZone.Right => Loc.T("Center right"),
        SnapZone.BottomLeft => Loc.T("Bottom left"),
        SnapZone.Bottom => Loc.T("Bottom center"),
        _ => Loc.T("Bottom right"),
    };

    public static bool IsCorner(SnapZone zone) => zone is SnapZone.TopLeft or SnapZone.TopRight or SnapZone.BottomLeft or SnapZone.BottomRight;

    /// <summary>Top and bottom edges split side by side; left and right edges split stacked.</summary>
    public static bool SplitsHorizontally(SnapZone zone) => zone is SnapZone.Top or SnapZone.Bottom;

    public static IReadOnlyList<string> DefaultActions(SnapZone zone) => zone switch
    {
        SnapZone.TopLeft => ["topLeft"],
        SnapZone.Top => ["maximize"],
        SnapZone.TopRight => ["topRight"],
        SnapZone.Left => ["leftHalf"],
        SnapZone.Right => ["rightHalf"],
        SnapZone.BottomLeft => ["bottomLeft"],
        SnapZone.Bottom => ["bottomHalf"],
        _ => ["bottomRight"],
    };

    /// <summary>Equal columns/rows of an edge for 2..4 areas.</summary>
    public static IReadOnlyList<string> ForCount(SnapZone zone, int count)
    {
        if (IsCorner(zone) || count <= 1)
        {
            return DefaultActions(zone);
        }

        if (SplitsHorizontally(zone))
        {
            return count switch
            {
                2 => ["leftHalf", "rightHalf"],
                3 => ["leftThird", "centerThird", "rightThird"],
                _ => ["leftQuarter", "leftMiddleQuarter", "rightMiddleQuarter", "rightQuarter"],
            };
        }

        return count switch
        {
            2 => ["topHalf", "bottomHalf"],
            3 => ["topThird", "middleThird", "bottomThird"],
            _ => ["topQuarter", "upperMiddleQuarter", "lowerMiddleQuarter", "bottomQuarter"],
        };
    }

    public static HashSet<string> DisabledZones(IAppSettings app) =>
        [.. (app.Get<string>(Keys.EdgeSnapDisabledZones) ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];

    public static bool IsEnabled(IAppSettings app, SnapZone zone) => !DisabledZones(app).Contains(Id(zone));

    public static void SetEnabled(IAppSettings app, SnapZone zone, bool enabled)
    {
        var set = DisabledZones(app);
        if (enabled)
        {
            set.Remove(Id(zone));
        }
        else
        {
            set.Add(Id(zone));
        }

        app.Set(Keys.EdgeSnapDisabledZones, string.Join(',', Zones.Select(Id).Where(set.Contains)));
    }

    private static Dictionary<string, List<string>> ReadActions(IAppSettings app)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var entry in (app.Get<string>(Keys.EdgeSnapZoneActions) ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = entry.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var zone = entry[..eq];
            var actions = entry[(eq + 1)..].Split('+', StringSplitOptions.RemoveEmptyEntries).Where(a => LayoutActions.Get(a) is not null).Take(MaximumParts).ToList();
            if (Zones.Any(z => Id(z) == zone) && actions.Count > 0)
            {
                map[zone] = actions;
            }
        }

        return map;
    }

    public static IReadOnlyList<string> Actions(IAppSettings app, SnapZone zone) =>
        ReadActions(app).TryGetValue(Id(zone), out var list) && (IsCorner(zone) ? list.Count == 1 : list.Count >= 1) ? list : DefaultActions(zone);

    public static void SetActions(IAppSettings app, SnapZone zone, IReadOnlyList<string> actions)
    {
        var map = ReadActions(app);
        if (actions.SequenceEqual(DefaultActions(zone)))
        {
            map.Remove(Id(zone));
        }
        else
        {
            map[Id(zone)] = [.. actions];
        }

        app.Set(Keys.EdgeSnapZoneActions, string.Join(',', Zones.Where(z => map.ContainsKey(Id(z))).Select(z => Id(z) + "=" + string.Join('+', map[Id(z)]))));
    }

    public static bool IsStandard(IAppSettings app) =>
        string.IsNullOrEmpty(app.Get<string>(Keys.EdgeSnapDisabledZones)) && string.IsNullOrEmpty(app.Get<string>(Keys.EdgeSnapZoneActions));

    public static void ResetAll(IAppSettings app)
    {
        app.Set(Keys.EdgeSnapDisabledZones, string.Empty);
        app.Set(Keys.EdgeSnapZoneActions, string.Empty);
    }
}

/// <summary>Translucent target rectangle shown while a dragged window is armed on a zone (spec A6.2: rounded 8, accent 18% fill, 1.5 accent border).</summary>
internal sealed class SnapPreviewWindow : OverlayWindow
{
    private readonly Border _frame = new();

    public SnapPreviewWindow()
    {
        ClickThrough = true;
        Focusable = false;
        _frame.CornerRadius = new CornerRadius(8);
        _frame.BorderThickness = new Thickness(1.5);
        _frame.SetResourceReference(Border.BackgroundProperty, ThemeKeys.AccentFill18);
        _frame.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Accent);
        Content = _frame;
        Opacity = 0;
    }

    /// <summary>Shows the preview over a physical-pixel rectangle.</summary>
    public void ShowAt(RECT target)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
        NativeMethods.SetWindowPos(hwnd, 0, target.Left, target.Top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        var scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        Width = target.Width / scale;
        Height = target.Height / scale;
        NativeMethods.SetWindowPos(hwnd, HWND_TOPMOST, target.Left, target.Top, target.Width, target.Height, SWP_NOACTIVATE);
        if (!IsVisible)
        {
            ShowNoActivate();
        }

        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
    }

    public void HidePreview()
    {
        if (IsVisible)
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 0;
            Hide();
        }
    }
}

/// <summary>
/// Drag-to-edge snapping (spec A6.2): while a dragged window's pointer rests within 12 px of a screen edge or corner, moving
/// slower than 400 px/s, a preview of the zone's target frame follows; releasing places the window. Drags are detected through
/// the MOVESIZE WinEvents (title-bar drags) or by the modifier-drag gesture calling <see cref="BeginMove"/>.
/// </summary>
internal sealed class EdgeSnapController
{
    private const int ActivationDistance = 12;
    private const int CornerSpan = 56;
    private const double CrossingSpeed = 400;

    private readonly IAppSettings _app;
    private readonly WindowLayoutEngine _engine;
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _timer;
    private readonly Queue<(long Ticks, int X, int Y)> _samples = new();
    private SnapPreviewWindow? _preview;
    private WinApi.WinEventProc? _proc;
    private nint _hook;
    private nint _hwnd;
    private RECT _startFrame;
    private bool _resizing;
    private (SnapZone Zone, int Index)? _armed;
    private string? _armedAction;

    public EdgeSnapController(IAppSettings app, WindowLayoutEngine engine, Dispatcher ui)
    {
        _app = app;
        _engine = engine;
        _ui = ui;
        _timer = new DispatcherTimer(DispatcherPriority.Input, ui) { Interval = TimeSpan.FromMilliseconds(15) };
        _timer.Tick += (_, _) => Tick();
    }

    public bool IsActive => _hook != 0;

    public void Start()
    {
        if (_hook != 0)
        {
            return;
        }

        _proc = OnEvent;
        _hook = WinApi.SetWinEventHook(WinApi.EVENT_SYSTEM_MOVESIZESTART, WinApi.EVENT_SYSTEM_MOVESIZEEND, 0, _proc, 0, 0, WinApi.WINEVENT_OUTOFCONTEXT | WinApi.WINEVENT_SKIPOWNPROCESS);
    }

    public void Stop()
    {
        if (_hook != 0)
        {
            WinApi.UnhookWinEvent(_hook);
            _hook = 0;
        }

        _proc = null;
        EndMove(apply: false);
        _preview?.Close();
        _preview = null;
    }

    private void OnEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint threadId, uint time)
    {
        if (idObject != WinApi.OBJID_WINDOW || hwnd == 0)
        {
            return;
        }

        if (eventType == WinApi.EVENT_SYSTEM_MOVESIZESTART)
        {
            BeginMove(hwnd);
        }
        else if (eventType == WinApi.EVENT_SYSTEM_MOVESIZEEND)
        {
            // The drag ended: place after the system finished its own bookkeeping.
            EndMove(apply: true);
        }
    }

    /// <summary>Starts tracking a moving window (WinEvent or modifier gesture).</summary>
    public void BeginMove(nint hwnd)
    {
        if (!_app.Get<bool>(Keys.EdgeSnapEnabled) || !WindowLayoutEngine.IsMovable(hwnd) || _engine.IsIgnored(hwnd))
        {
            return;
        }

        _hwnd = hwnd;
        _resizing = false;
        _armed = null;
        _armedAction = null;
        _samples.Clear();
        NativeMethods.GetWindowRect(hwnd, out _startFrame);
        _timer.Start();
    }

    /// <summary>Ends tracking; with <paramref name="apply"/> the armed zone's placement is applied.</summary>
    public void EndMove(bool apply)
    {
        if (_hwnd == 0)
        {
            return;
        }

        _timer.Stop();
        var hwnd = _hwnd;
        var action = _armedAction;
        _hwnd = 0;
        _armed = null;
        _armedAction = null;
        _preview?.HidePreview();
        if (apply && action is not null && LayoutActions.Get(action) is { } layout && !_resizing)
        {
            _ui.BeginInvoke(DispatcherPriority.Background, () => _engine.Apply(layout, hwnd));
        }
    }

    private void Tick()
    {
        if (_hwnd == 0 || !WinApi.IsWindow(_hwnd))
        {
            EndMove(apply: false);
            return;
        }

        NativeMethods.GetCursorPos(out var cursor);
        var now = Environment.TickCount64;
        _samples.Enqueue((now, cursor.X, cursor.Y));
        while (_samples.Count > 1 && now - _samples.Peek().Ticks > 60)
        {
            _samples.Dequeue();
        }

        // Resizing drags are not snaps: the window size changed while the pointer moved.
        NativeMethods.GetWindowRect(_hwnd, out var rect);
        if (Math.Abs(rect.Width - _startFrame.Width) > 3 || Math.Abs(rect.Height - _startFrame.Height) > 3)
        {
            _resizing = true;
            Disarm();
            return;
        }

        var speed = Speed(now);
        var monitor = NativeMethods.MonitorFromPoint(new POINT { X = cursor.X, Y = cursor.Y }, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        NativeMethods.GetMonitorInfoW(monitor, ref info);
        var zone = ZoneAt(cursor.X, cursor.Y, info.rcMonitor);
        if (zone is null || speed > CrossingSpeed || !EdgeSnapModel.IsEnabled(_app, zone.Value))
        {
            Disarm();
            return;
        }

        var actions = EdgeSnapModel.Actions(_app, zone.Value);
        var index = AreaIndex(zone.Value, actions.Count, cursor.X, cursor.Y, info.rcMonitor);
        if (_armed == (zone.Value, index))
        {
            return;
        }

        _armed = (zone.Value, index);
        _armedAction = actions[index];
        var action = LayoutActions.Get(_armedAction);
        if (action is null)
        {
            Disarm();
            return;
        }

        var frame = _engine.FrameFor(action, info.rcWork);
        _preview ??= new SnapPreviewWindow();
        _preview.ShowAt(frame);
    }

    private void Disarm()
    {
        if (_armed is null)
        {
            return;
        }

        _armed = null;
        _armedAction = null;
        _preview?.HidePreview();
    }

    private double Speed(long now)
    {
        if (_samples.Count < 2)
        {
            return 0;
        }

        var first = _samples.Peek();
        var last = _samples.Last();
        var span = last.Ticks - first.Ticks;
        if (span < 20)
        {
            return 0;
        }

        var dx = last.X - first.X;
        var dy = last.Y - first.Y;
        return Math.Sqrt(dx * dx + dy * dy) * 1000.0 / span;
    }

    /// <summary>The zone under a pointer position (within the activation distance of an edge or corner), or null.</summary>
    public static SnapZone? ZoneAt(int x, int y, RECT monitor)
    {
        var left = x - monitor.Left <= ActivationDistance;
        var right = monitor.Right - 1 - x <= ActivationDistance;
        var top = y - monitor.Top <= ActivationDistance;
        var bottom = monitor.Bottom - 1 - y <= ActivationDistance;
        var nearLeft = x - monitor.Left <= CornerSpan;
        var nearRight = monitor.Right - 1 - x <= CornerSpan;
        var nearTop = y - monitor.Top <= CornerSpan;
        var nearBottom = monitor.Bottom - 1 - y <= CornerSpan;
        if ((top && nearLeft) || (left && nearTop))
        {
            return SnapZone.TopLeft;
        }

        if ((top && nearRight) || (right && nearTop))
        {
            return SnapZone.TopRight;
        }

        if ((bottom && nearLeft) || (left && nearBottom))
        {
            return SnapZone.BottomLeft;
        }

        if ((bottom && nearRight) || (right && nearBottom))
        {
            return SnapZone.BottomRight;
        }

        if (top)
        {
            return SnapZone.Top;
        }

        if (bottom)
        {
            return SnapZone.Bottom;
        }

        if (left)
        {
            return SnapZone.Left;
        }

        return right ? SnapZone.Right : null;
    }

    /// <summary>Which area of a split edge the pointer is over.</summary>
    public static int AreaIndex(SnapZone zone, int count, int x, int y, RECT monitor)
    {
        if (count <= 1)
        {
            return 0;
        }

        var fraction = EdgeSnapModel.SplitsHorizontally(zone)
            ? (x - monitor.Left) / (double)Math.Max(1, monitor.Width)
            : (y - monitor.Top) / (double)Math.Max(1, monitor.Height);
        return Math.Clamp((int)(fraction * count), 0, count - 1);
    }
}
