// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;
using static WinKnife.Core.Native.NativeConstants;

namespace WinKnife.Features.WindowTools;

/// <summary>A small floating shelf that holds files while you move between windows.</summary>
public sealed class ShelfFeature : IFeature
{
    private static readonly Hotkey DefaultHotkey = new(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, Key.F);

    private IFeatureContext? _context;
    private HotkeyBindings? _bindings;
    private IDisposable? _mouseSubscription;
    private DispatcherTimer? _timer;
    private ShelfWindow? _window;
    private bool _autoShown;
    private int _dwell;
    private int _idle;

    // Written on the hook thread, read on the UI thread.
    private volatile bool _leftDown;
    private int _downX;
    private int _downY;

    public string Id => "shelf";

    public string Name => "Shelf";

    public string Description => "Park files on a small floating shelf while you switch windows, then drag them out where you need them.";

    public FeatureGroup Group => FeatureGroup.ClipboardFiles;

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("toggle", _window is { IsVisible: true } ? "Hide shelf" : "Show shelf", Toggle, ""),
        new FeatureAction("clear", "Clear shelf", () => _window?.Clear(), ""),
    ];

    public void Start(IFeatureContext context)
    {
        _context = context;
        _bindings = new HotkeyBindings(context,
        [
            new HotkeyBinding("hotkey", "Shelf", DefaultHotkey, Toggle),
        ]);

        _timer = new DispatcherTimer(DispatcherPriority.Background, context.Ui) { Interval = TimeSpan.FromMilliseconds(60) };
        _timer.Tick += OnTick;
        ApplyAutoShowSetting();
        context.Settings.Changed += OnSettingChanged;
    }

    public void Stop()
    {
        if (_context is not null)
        {
            _context.Settings.Changed -= OnSettingChanged;
        }

        _bindings?.Dispose();
        _bindings = null;
        _mouseSubscription?.Dispose();
        _mouseSubscription = null;
        _timer?.Stop();
        _timer = null;
        var window = _window;
        _window = null;
        if (window is not null)
        {
            SavePosition(window);
            window.Close();
        }

        _context = null;
    }

    public FrameworkElement? CreateSettingsView()
    {
        if (_context is null)
        {
            return null;
        }

        return new SettingsViewBuilder(_context.Settings)
            .AddHotkey("hotkey", "Show or hide the shelf", DefaultHotkey)
            .AddToggle("autoShow", "Show when dragging near the top edge", true, "While you drag something to the very top of the screen the shelf appears so you can drop files on it. It hides again when you are done.")
            .AddToggle("remember", "Remember shelf contents between sessions", true)
            .AddNote("Drag files onto the shelf. Drag a tile out to copy or move the file. Double-click opens it, right-click for more.")
            .Build();
    }

    private void OnSettingChanged(string key)
    {
        if (key == "autoShow")
        {
            _context?.Ui.BeginInvoke(ApplyAutoShowSetting);
        }
    }

    private void ApplyAutoShowSetting()
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        var wanted = context.Settings.Get("autoShow", true);
        if (wanted && _mouseSubscription is null)
        {
            _mouseSubscription = context.Hooks.SubscribeMouse(OnMouse);
        }
        else if (!wanted && _mouseSubscription is not null)
        {
            _mouseSubscription.Dispose();
            _mouseSubscription = null;
            _leftDown = false;
        }
    }

    /// <summary>Hook thread: only records the button state, never swallows.</summary>
    private bool OnMouse(MouseHookEvent e)
    {
        if (e.IsFromWinKnife)
        {
            return false;
        }

        switch (e.Kind)
        {
            case MouseEventKind.LeftDown:
                _downX = e.X;
                _downY = e.Y;
                _leftDown = true;
                _context?.Ui.BeginInvoke(() =>
                {
                    _dwell = 0;
                    _timer?.Start();
                });
                break;
            case MouseEventKind.LeftUp:
                _leftDown = false;
                break;
        }

        return false;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        if (_leftDown)
        {
            _idle = 0;
            var cursor = Screens.CursorPosition();
            var monitor = Screens.FromPoint(cursor);
            var inBand = cursor.Y - monitor.Bounds.Top <= 3;
            var travelled = Math.Abs(cursor.X - _downX) + Math.Abs(cursor.Y - _downY);

            var info = new GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<GUITHREADINFO>() };
            var movingWindow = WinApi.GetGUIThreadInfo(0, ref info) && (info.flags & WinApi.GUI_INMOVESIZE) != 0;

            if (inBand && travelled > 60 && !movingWindow && !(_window is { IsVisible: true }))
            {
                _dwell++;
                if (_dwell >= 4)
                {
                    Show(autoShown: true);
                }
            }
            else if (!inBand)
            {
                _dwell = 0;
            }

            return;
        }

        // Button is up: keep ticking only while an automatically shown shelf waits to be dismissed.
        if (_autoShown && _window is { IsVisible: true } window)
        {
            if (CursorOver(window))
            {
                _idle = 0;
            }
            else if (++_idle >= 35)
            {
                Hide();
            }
        }
        else
        {
            _timer?.Stop();
        }
    }

    private static bool CursorOver(ShelfWindow window)
    {
        var handle = window.Handle;
        if (handle == 0 || !NativeMethods.GetWindowRect(handle, out var rect))
        {
            return false;
        }

        NativeMethods.GetCursorPos(out var pt);
        return pt.X >= rect.Left && pt.X <= rect.Right && pt.Y >= rect.Top - 8 && pt.Y <= rect.Bottom;
    }

    private void Toggle()
    {
        if (_window is { IsVisible: true })
        {
            Hide();
        }
        else
        {
            Show(autoShown: false);
        }
    }

    private ShelfWindow EnsureWindow()
    {
        if (_window is not null)
        {
            return _window;
        }

        var context = _context!;
        var window = new ShelfWindow();
        if (context.Settings.Get("remember", true))
        {
            window.SetPaths(context.Settings.Get("items", new List<string>()));
        }

        window.ItemsChanged += () =>
        {
            // The user is using the shelf: do not hide it automatically any more.
            _autoShown = false;
            if (context.Settings.Get("remember", true))
            {
                context.Settings.Set("items", window.Items.ToList());
            }
        };
        _window = window;
        return window;
    }

    private void Show(bool autoShown)
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        try
        {
            var window = EnsureWindow();
            window.PruneMissing();
            _autoShown = autoShown;
            _idle = 0;

            var cursor = Screens.CursorPosition();
            var area = Screens.FromPoint(cursor);
            var scale = WinUtil.PrepareOnMonitorAt(window, (int)cursor.X, (int)cursor.Y);
            var x = (int)(area.WorkArea.Left + (area.WorkArea.Width - ShelfWidthDips * scale) / 2);
            var y = (int)(area.WorkArea.Top + 12 * scale);

            var savedX = context.Settings.Get("x", int.MinValue);
            var savedY = context.Settings.Get("y", int.MinValue);
            if (!autoShown && savedX != int.MinValue && savedY != int.MinValue && IsOnScreen(savedX, savedY))
            {
                x = savedX;
                y = savedY;
            }

            NativeMethods.SetWindowPos(window.Handle, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
            window.ShowNoActivate();
            if (autoShown)
            {
                _timer?.Start();
            }
        }
        catch (Exception ex)
        {
            context.Log.Error("Could not show the shelf", ex);
        }
    }

    private const double ShelfWidthDips = 352;

    private void Hide()
    {
        var window = _window;
        _autoShown = false;
        if (window is null)
        {
            return;
        }

        SavePosition(window);
        window.Hide();
    }

    private void SavePosition(ShelfWindow window)
    {
        var context = _context;
        if (context is null || !window.IsVisible || window.Handle == 0)
        {
            return;
        }

        if (NativeMethods.GetWindowRect(window.Handle, out var rect))
        {
            context.Settings.Set("x", rect.Left);
            context.Settings.Set("y", rect.Top);
        }
    }

    private static bool IsOnScreen(int x, int y)
    {
        foreach (var monitor in WinUtil.Monitors())
        {
            var b = monitor.Bounds;
            if (x >= b.Left - 20 && x < b.Right - 40 && y >= b.Top - 20 && y < b.Bottom - 40)
            {
                return true;
            }
        }

        return false;
    }
}
