// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;
using static WinKnife.Core.Native.NativeConstants;

namespace WinKnife.Features.WindowTools;

/// <summary>The glass ring of "Shortcut + pointer layout": eight sectors around a centre toggle (reference: highlights-windowlayout).</summary>
internal sealed class RingWindow : OverlayWindow
{
    public const double Size = 330;
    private const double Outer = 160;
    private const double Inner = 80;
    private const double Core = 66;

    // Sector 0 = right, counter-clockwise (screen up = 2).
    public static readonly string[] SectorActions =
        ["rightHalf", "topRight", "topHalf", "topLeft", "leftHalf", "bottomLeft", "bottomHalf", "bottomRight"];

    private readonly Canvas _canvas = new() { Width = Size, Height = Size };
    private readonly Path[] _sectors = new Path[8];
    private readonly Ellipse _centre;
    private readonly Border _centreIcon;
    private int _selected = -1;

    public RingWindow()
    {
        ClickThrough = true;
        Width = Size;
        Height = Size;
        var c = Size / 2;

        var shell = new Ellipse { Width = Outer * 2 + 4, Height = Outer * 2 + 4, Stroke = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), StrokeThickness = 1.2 };
        shell.Fill = new SolidColorBrush(Color.FromArgb(232, 27, 29, 36));
        Canvas.SetLeft(shell, c - Outer - 2);
        Canvas.SetTop(shell, c - Outer - 2);
        _canvas.Children.Add(shell);

        var track = new Ellipse { Width = (Outer - 10) * 2, Height = (Outer - 10) * 2, Fill = new SolidColorBrush(Color.FromArgb(255, 38, 40, 48)), Stroke = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), StrokeThickness = 1 };
        Canvas.SetLeft(track, c - Outer + 10);
        Canvas.SetTop(track, c - Outer + 10);
        _canvas.Children.Add(track);

        for (var i = 0; i < 8; i++)
        {
            var centerAngle = i * 45.0;
            var path = new Path { Data = Sector(c, c, Inner, Outer - 14, centerAngle - 22.5, centerAngle + 22.5), Fill = Brushes.Transparent, Stroke = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)), StrokeThickness = 1 };
            _sectors[i] = path;
            _canvas.Children.Add(path);
            var rad = centerAngle * Math.PI / 180;
            var r = (Inner + Outer - 14) / 2;
            var dot = new Ellipse { Width = 5, Height = 5, Fill = new SolidColorBrush(Color.FromArgb(150, 200, 205, 215)), IsHitTestVisible = false };
            Canvas.SetLeft(dot, c + r * Math.Cos(rad) - 2.5);
            Canvas.SetTop(dot, c - r * Math.Sin(rad) - 2.5);
            _canvas.Children.Add(dot);
        }

        var ring = new Ellipse { Width = (Inner - 6) * 2 + 12, Height = (Inner - 6) * 2 + 12, Fill = new SolidColorBrush(Color.FromArgb(255, 29, 31, 38)), Stroke = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), StrokeThickness = 1 };
        Canvas.SetLeft(ring, c - Inner + 0);
        Canvas.SetTop(ring, c - Inner + 0);
        ring.Width = Inner * 2;
        ring.Height = Inner * 2;
        _canvas.Children.Add(ring);

        _centre = new Ellipse { Width = Core * 2, Height = Core * 2, Fill = new SolidColorBrush(Color.FromArgb(255, 36, 38, 46)), Stroke = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), StrokeThickness = 1 };
        Canvas.SetLeft(_centre, c - Core);
        Canvas.SetTop(_centre, c - Core);
        _canvas.Children.Add(_centre);

        // The toggle-like icon of the centre: a small rounded screen with its left half filled.
        _centreIcon = new Border
        {
            Width = 54,
            Height = 36,
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(1.5),
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 190, 195, 205)),
            Background = new SolidColorBrush(Color.FromArgb(255, 52, 54, 62)),
            Child = new Border { Width = 24, Margin = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromRgb(10, 132, 255)) },
        };
        Canvas.SetLeft(_centreIcon, c - 27);
        Canvas.SetTop(_centreIcon, c - 18);
        _canvas.Children.Add(_centreIcon);

        Content = _canvas;
    }

    /// <summary>Highlights a sector (-1 = none, the centre is selected).</summary>
    public void Select(int sector)
    {
        if (sector == _selected)
        {
            return;
        }

        _selected = sector;
        var accent = (Application.Current?.TryFindResource(ThemeKeys.Accent) as Brush) ?? new SolidColorBrush(Color.FromRgb(10, 132, 255));
        for (var i = 0; i < 8; i++)
        {
            _sectors[i].Fill = i == sector ? accent : Brushes.Transparent;
        }

        _centre.Fill = new SolidColorBrush(sector < 0 ? Color.FromArgb(255, 44, 52, 70) : Color.FromArgb(255, 36, 38, 46));
    }

    /// <summary>Shows the ring centred on a physical pixel position.</summary>
    public void ShowCentred(int x, int y)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
        NativeMethods.SetWindowPos(hwnd, 0, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        var scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        var px = (int)Math.Round(Size * scale);
        NativeMethods.SetWindowPos(hwnd, HWND_TOPMOST, x - px / 2, y - px / 2, px, px, SWP_NOACTIVATE);
        Select(-1);
        ShowNoActivate();
    }

    private static Geometry Sector(double cx, double cy, double inner, double outer, double fromDeg, double toDeg)
    {
        Point P(double r, double deg) => new(cx + r * Math.Cos(deg * Math.PI / 180), cy - r * Math.Sin(deg * Math.PI / 180));
        var figure = new PathFigure { StartPoint = P(outer, fromDeg), IsClosed = true };
        figure.Segments.Add(new ArcSegment(P(outer, toDeg), new Size(outer, outer), 0, false, SweepDirection.Counterclockwise, true));
        figure.Segments.Add(new LineSegment(P(inner, toDeg), true));
        figure.Segments.Add(new ArcSegment(P(inner, fromDeg), new Size(inner, inner), 0, false, SweepDirection.Clockwise, true));
        return new PathGeometry([figure]);
    }
}

/// <summary>
/// "Shortcut + pointer layout" (spec A6.2): holding the trigger shows the ring at the pointer; a pointer travel of at least 28 px selects
/// one of eight 45° sectors, releasing the trigger places the window that was active when the ring opened. No travel = Maximize.
/// </summary>
internal sealed class DirectionalRing
{
    private const int Threshold = 28;

    private readonly IAppSettings _app;
    private readonly IShortcutService _shortcuts;
    private readonly WindowLayoutEngine _engine;
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _timer;
    private IDisposable? _subscription;
    private RingWindow? _window;
    private volatile bool _down;
    private nint _target;
    private POINT _origin;
    private int _selected = -1;

    public DirectionalRing(IAppSettings app, IShortcutService shortcuts, WindowLayoutEngine engine, Dispatcher ui)
    {
        _app = app;
        _shortcuts = shortcuts;
        _engine = engine;
        _ui = ui;
        _timer = new DispatcherTimer(DispatcherPriority.Input, ui) { Interval = TimeSpan.FromMilliseconds(8) };
        _timer.Tick += (_, _) => Update();
    }

    public bool IsRunning => _subscription is not null;

    public void Start(IInputHooks hooks)
    {
        if (_subscription is null)
        {
            _subscription = hooks.SubscribeKeyboard(OnKey, priority: 70);
        }
    }

    public void Stop()
    {
        _subscription?.Dispose();
        _subscription = null;
        _down = false;
        _timer.Stop();
        _window?.Close();
        _window = null;
    }

    private bool OnKey(KeyboardHookEvent e)
    {
        if (e.IsFromWinKnife)
        {
            return false;
        }

        var hotkey = _shortcuts.GetHotkey(Keys.DirectionalRole);
        if (hotkey is null)
        {
            return false;
        }

        var key = e.Key;
        if (!_down)
        {
            if (e.IsKeyDown && key == hotkey.Key && ToHotkeyModifiers(e.Modifiers) == hotkey.Modifiers)
            {
                _down = true;
                _ui.BeginInvoke(Open);
                return true;
            }

            return false;
        }

        if (e.IsKeyDown && key == Key.Escape)
        {
            _down = false;
            _ui.BeginInvoke(() => Close(apply: false));
            return true;
        }

        if (key == hotkey.Key)
        {
            if (e.IsKeyUp)
            {
                _down = false;
                _ui.BeginInvoke(() => Close(apply: true));
            }

            return true; // auto-repeat must not re-trigger
        }

        // Releasing a modifier of the chord also ends the gesture.
        if (e.IsKeyUp && IsModifierOf(e.VkCode, hotkey.Modifiers))
        {
            _down = false;
            _ui.BeginInvoke(() => Close(apply: true));
        }

        return false;
    }

    private static HotkeyModifiers ToHotkeyModifiers(ModifierKeys keys)
    {
        var mods = HotkeyModifiers.None;
        if (keys.HasFlag(ModifierKeys.Control)) mods |= HotkeyModifiers.Control;
        if (keys.HasFlag(ModifierKeys.Alt)) mods |= HotkeyModifiers.Alt;
        if (keys.HasFlag(ModifierKeys.Shift)) mods |= HotkeyModifiers.Shift;
        if (keys.HasFlag(ModifierKeys.Windows)) mods |= HotkeyModifiers.Win;
        return mods;
    }

    private static bool IsModifierOf(int vk, HotkeyModifiers mods) =>
        (mods.HasFlag(HotkeyModifiers.Control) && vk is 0x11 or 0xA2 or 0xA3)
        || (mods.HasFlag(HotkeyModifiers.Alt) && vk is 0x12 or 0xA4 or 0xA5)
        || (mods.HasFlag(HotkeyModifiers.Shift) && vk is 0x10 or 0xA0 or 0xA1)
        || (mods.HasFlag(HotkeyModifiers.Win) && vk is 0x5B or 0x5C);

    private void Open()
    {
        if (_engine.ForegroundIgnored())
        {
            _down = false;
            return;
        }

        _target = _engine.ResolveTarget();
        NativeMethods.GetCursorPos(out _origin);
        _window ??= new RingWindow();
        _window.ShowCentred(_origin.X, _origin.Y);
        _selected = -1;
        _timer.Start();
    }

    private void Update()
    {
        if (_window is null)
        {
            return;
        }

        NativeMethods.GetCursorPos(out var p);
        var dx = p.X - _origin.X;
        var dy = p.Y - _origin.Y;
        if (Math.Sqrt(dx * dx + dy * dy) < Threshold)
        {
            _selected = -1;
        }
        else
        {
            var angle = Math.Atan2(-dy, dx) * 180 / Math.PI;
            _selected = (int)Math.Round((angle + 360) % 360 / 45.0) % 8;
        }

        _window.Select(_selected);
    }

    private void Close(bool apply)
    {
        _timer.Stop();
        _window?.Hide();
        if (!apply || _target == 0)
        {
            return;
        }

        var raw = _selected < 0 ? "maximize" : RingWindow.SectorActions[_selected];
        if (LayoutActions.Get(raw) is { } action)
        {
            _engine.Apply(action, _target);
        }
    }
}
