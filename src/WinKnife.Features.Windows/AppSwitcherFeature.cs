// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Input;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;

namespace WinKnife.Features.WindowTools;

/// <summary>An Alt+Tab style window switcher with live previews on its own shortcut.</summary>
public sealed class AppSwitcherFeature : IFeature
{
    private static readonly Hotkey DefaultHotkey = new(HotkeyModifiers.Alt, Key.OemTilde);

    private IFeatureContext? _context;
    private HotkeyBindings? _bindings;
    private SwitcherWindow? _window;

    public string Id => "app-switcher";

    public string Name => "App switcher";

    public string Description => "Switch between open windows with live previews, search by typing, on a shortcut of your choice.";

    public FeatureGroup Group => FeatureGroup.WindowsTaskbar;

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("show", "Show app switcher", () => Invoke(), ""),
    ];

    public void Start(IFeatureContext context)
    {
        _context = context;
        _bindings = new HotkeyBindings(context,
        [
            new HotkeyBinding("hotkey", "App switcher", DefaultHotkey, () => Invoke()),
        ]);
    }

    public void Stop()
    {
        _bindings?.Dispose();
        _bindings = null;
        CloseWindow();
        _context = null;
    }

    public FrameworkElement? CreateSettingsView()
    {
        if (_context is null)
        {
            return null;
        }

        return new SettingsViewBuilder(_context.Settings)
            .AddHotkey("hotkey", "Shortcut", DefaultHotkey, "Press it again (or use the arrow keys / Tab) to move through the windows. Holding the shortcut's modifier keys and releasing them switches.")
            .AddToggle("releaseToActivate", "Release to switch", true, "Switch when you let go of the modifier keys, like Alt+Tab. When off, the overlay stays open until you press Enter or click.")
            .AddToggle("includeMinimized", "Include minimized windows", true)
            .AddToggle("currentMonitorOnly", "Only windows on the current monitor", false, "Show only windows that are on the monitor where the overlay appears.")
            .AddNote("Type to filter by title or app name. Esc cancels. Windows on other virtual desktops are not listed.")
            .Build();
    }

    private void Invoke()
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        if (_window is { IsFinished: false } existing)
        {
            existing.Move(1);
            return;
        }

        var hotkey = context.Settings.Get<Hotkey?>("hotkey", DefaultHotkey);
        var modifiers = hotkey?.Modifiers ?? HotkeyModifiers.None;
        var releaseToActivate = context.Settings.Get("releaseToActivate", true);
        var includeMinimized = context.Settings.Get("includeMinimized", true);

        var windows = WindowEnumerator.Enumerate(includeMinimized);
        var area = Screens.UnderCursor();
        var foreground = NativeMethods.GetForegroundWindow();
        var cursor = Screens.CursorPosition();
        var monitor = NativeMethods.MonitorFromPoint(new POINT { X = (int)cursor.X, Y = (int)cursor.Y }, NativeConstants.MONITOR_DEFAULTTONEAREST);
        if (context.Settings.Get("currentMonitorOnly", false))
        {
            windows = [.. windows.Where(w => w.Monitor == monitor)];
        }

        if (windows.Count == 0)
        {
            return;
        }

        // Start on the second window (the previously used one) unless the first is not the active window.
        var start = windows.Count > 1 && windows[0].Handle == foreground ? 1 : 0;

        var window = new SwitcherWindow(windows, modifiers, releaseToActivate, start);
        _window = window;
        window.Completed += target =>
        {
            if (ReferenceEquals(_window, window))
            {
                _window = null;
            }

            if (target is not null)
            {
                context.Ui.BeginInvoke(() => WinUtil.Activate(target.Handle));
            }
        };

        try
        {
            window.ShowOn(new RECT
            {
                Left = (int)area.WorkArea.Left,
                Top = (int)area.WorkArea.Top,
                Right = (int)area.WorkArea.Right,
                Bottom = (int)area.WorkArea.Bottom,
            });
        }
        catch (Exception ex)
        {
            context.Log.Error("Could not show the app switcher", ex);
            _window = null;
            try
            {
                window.Close();
            }
            catch (Exception)
            {
                // ignore
            }
        }
    }

    private void CloseWindow()
    {
        var window = _window;
        _window = null;
        if (window is { IsFinished: false })
        {
            window.Cancel();
        }
    }
}
