// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.App;

/// <summary>
/// Owns the app shells: tray icon + context menu, the tray flyout, the Settings window and onboarding. Implements
/// <see cref="IShell"/> for every feature and contribution.
/// </summary>
internal sealed class AppShell : IShell
{
    private readonly CoreHost _host;
    private readonly ShellServices _s;
    private TrayController? _tray;
    private TrayPanel? _panel;
    private SettingsWindow? _settings;
    private OnboardingWindow? _onboarding;
    private DispatcherTimer? _trayTimer;
    private bool _deleteDataOnExit;

    public AppShell(CoreHost host)
    {
        _host = host;
        _s = new ShellServices(host, this);
        host.ShellProxy.Inner = this;
    }

    public ShellServices Services => _s;

    public bool IsPanelOpen => _panel is { IsVisible: true };

    public bool DeleteDataOnExit => _deleteDataOnExit;

    public Func<string?> RelaunchArguments { get; set; } = () => null;

    public void Start()
    {
        _tray = new TrayController(_host.Log) { GlyphCode = _s.App.Get<string>(AppKeys.MenuBarIconSymbol) };
        _tray.LeftClick += TogglePanel;
        _tray.RightClick += ShowMenu;
        _host.Notifier.Sink = _tray.ShowBalloon;
        _s.App.Changed += key =>
        {
            if (key == AppKeys.MenuBarIconSymbol)
            {
                Application.Current.Dispatcher.BeginInvoke(() => _tray!.GlyphCode = _s.App.Get<string>(AppKeys.MenuBarIconSymbol));
            }
        };
        _host.Services.Changed += () => Application.Current.Dispatcher.BeginInvoke(UpdateTrayState);
        _trayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _trayTimer.Tick += (_, _) => UpdateTrayState();
        _trayTimer.Start();
        UpdateTrayState();
    }

    public void Dispose()
    {
        _trayTimer?.Stop();
        _tray?.Dispose();
    }

    // ------------------------------------------------------------------------------------------------ IShell

    public void OpenSettings(string destination, string? targetFeature = null)
    {
        Application.Current.Dispatcher.BeginInvoke(() => ShowSettings(destination, targetFeature));
    }

    public void OpenFeatures(string? featureId = null) => OpenSettings(SettingsPages.Features, featureId);

    public void OpenPanel(string tabId)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (_panel is null)
            {
                var anchor = Screens.CursorPosition();
                OpenPanelAt(anchor);
            }

            _panel?.SelectTabExternally(tabId);
        });
    }

    public void ClosePanel() => _panel?.ClosePanel();

    /// <summary>Developer flag: shows the tray context menu at the cursor.</summary>
    public void ShowMenuForDevelopment() => ShowMenu(Screens.CursorPosition());

    /// <summary>Developer flag: opens the panel near the bottom-right corner (where the tray lives) on a tab.</summary>
    public void OpenPanelForDevelopment(string? tab)
    {
        var area = Screens.Primary().WorkArea;
        OpenPanelAt(new Point(area.Right - 120, area.Bottom + 10));
        if (tab is not null)
        {
            _panel?.SelectTabExternally(tab);
        }
    }

    public void Quit() => Application.Current.Dispatcher.BeginInvoke(() => Application.Current.Shutdown());

    public void Restart() => Restart(false);

    public void Restart(bool skipSave)
    {
        var exe = Environment.ProcessPath;
        if (exe is null)
        {
            return;
        }

        if (skipSave)
        {
            _host.Store.SuspendSaving();
        }

        try
        {
            // The new process waits (<= 20 s) for this one to exit, then starts normally.
            Process.Start(new ProcessStartInfo(exe, $"--relaunch {Environment.ProcessId}") { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            _host.Log.Error("Relaunch failed", ex);
            return;
        }

        Application.Current.Dispatcher.BeginInvoke(() => Application.Current.Shutdown());
    }

    public void QuitAndDeleteData()
    {
        _host.Store.SuspendSaving();
        _deleteDataOnExit = true;
        Application.Current.Dispatcher.BeginInvoke(() => Application.Current.Shutdown());
    }

    public void ReshowTray()
    {
        _tray?.Recreate();
        UpdateTrayState();
        UiKit.OpenUrl("ms-settings:taskbar");
    }

    public void ShowOnboarding()
    {
        if (_onboarding is not null)
        {
            _onboarding.Activate();
            return;
        }

        _onboarding = new OnboardingWindow(_s);
        _onboarding.Closed += (_, _) => _onboarding = null;
        _onboarding.Show();
        _onboarding.Activate();
    }

    /// <summary>Second launch / reopen: the user must always be able to get back in (spec 8.3).</summary>
    public void Recover()
    {
        if (_onboarding is not null)
        {
            _onboarding.Activate();
            return;
        }

        if (_settings is { IsVisible: true })
        {
            _settings.WindowState = _settings.WindowState == WindowState.Minimized ? WindowState.Normal : _settings.WindowState;
            _settings.Activate();
            return;
        }

        _tray?.Recreate();
        ShowSettings(SettingsPages.General, null);
    }

    // ------------------------------------------------------------------------------------------------ panel / settings

    private void TogglePanel(Point anchor)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (_panel is not null)
            {
                _panel.ClosePanel();
                return;
            }

            // Clicking the tray icon deactivates (and closes) an open panel before this runs; treat that as a toggle.
            if (Environment.TickCount64 - TrayPanel.LastClosedTick < 300)
            {
                return;
            }

            OpenPanelAt(anchor);
        });
    }

    private void OpenPanelAt(Point anchor)
    {
        if (_panel is not null)
        {
            return;
        }

        _panel = new TrayPanel(_s, anchor);
        _panel.Closed += (_, _) => _panel = null;
        _panel.Show();
    }

    private void ShowSettings(string destination, string? targetFeature)
    {
        if (_settings is null)
        {
            _settings = new SettingsWindow(_s);
            _settings.Closed += (_, _) => _settings = null;
            _settings.Show();
        }

        _settings.Navigate(destination, targetFeature);
        if (_settings.WindowState == WindowState.Minimized)
        {
            _settings.WindowState = WindowState.Normal;
        }

        _settings.Activate();
        // Keep Settings above the panel's topmost layer long enough to take focus.
        _settings.Topmost = true;
        _settings.Topmost = false;
    }

    // ------------------------------------------------------------------------------------------------ tray state / menu

    private void UpdateTrayState()
    {
        if (_tray is null)
        {
            return;
        }

        var keepAwake = _host.Services.Get<IKeepAwakeService>();
        var mic = _host.Services.Get<IMicMuteState>();
        if (keepAwake is { IsActive: true })
        {
            _tray.Tooltip = keepAwake.EndsAt is { } end
                ? $"WinKnife: awake until {end:t}"
                : "WinKnife: awake indefinitely";
            _tray.Badge = Color.FromRgb(0xFF, 0x9F, 0x0A);
        }
        else
        {
            _tray.Tooltip = "WinKnife: normal sleep";
            _tray.Badge = mic is { IsMuted: true } ? Color.FromRgb(0xFF, 0x45, 0x3A) : null;
        }
    }

    private FeatureAction? FindAction(string featureId, string actionId) =>
        _host.Features.All.Where(a => a.Feature.CatalogId == featureId && a.Action.Id == actionId).Select(a => a.Action).FirstOrDefault();

    private void ShowMenu(Point anchor)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            _panel?.ClosePanel();
            var menu = BuildMenu();
            // Hidden activated window so the menu closes when the user clicks elsewhere.
            var owner = new Window
            {
                Width = 1,
                Height = 1,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                Topmost = true,
                ShowActivated = true,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -2000,
                Top = -2000,
            };
            owner.Show();
            owner.Activate();
            menu.PlacementTarget = null;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.Closed += (_, _) => owner.Close();
            menu.IsOpen = true;
        });
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu { FontFamily = UiKit.TextFont, StaysOpen = false };
        var rt = _s.Runtime;
        var any = false;

        if (rt.IsAvailable("keep-awake"))
        {
            any = true;
            var svc = _host.Services.Get<IKeepAwakeService>();
            var toggle = FindAction("keep-awake", "toggle");
            var active = svc?.IsActive ?? toggle?.Title.Contains(": on", StringComparison.OrdinalIgnoreCase) ?? false;
            menu.Items.Add(Item(active ? "Disable keep awake" : "Enable keep awake", () =>
            {
                if (svc is not null)
                {
                    if (active)
                    {
                        svc.Deactivate();
                    }
                    else
                    {
                        svc.Activate(null);
                    }
                }
                else
                {
                    toggle?.Invoke();
                }
            }));
            if (!active)
            {
                var sub = new MenuItem { Header = "Activate for…" };
                foreach (var (label, minutes, actionId) in new[]
                {
                    ("15 minutes", 15, "15m"), ("30 minutes", 30, ""), ("1 hour", 60, "1h"), ("2 hours", 120, "2h"), ("4 hours", 240, ""), ("8 hours", 480, ""), ("Indefinitely", 0, "forever"),
                })
                {
                    var captured = (minutes, actionId);
                    var legacy = captured.actionId.Length > 0 ? FindAction("keep-awake", captured.actionId) : null;
                    if (svc is null && legacy is null)
                    {
                        continue;
                    }

                    sub.Items.Add(Item(label, () =>
                    {
                        if (svc is not null)
                        {
                            svc.Activate(captured.minutes == 0 ? null : TimeSpan.FromMinutes(captured.minutes));
                        }
                        else
                        {
                            legacy?.Invoke();
                        }
                    }));
                }

                if (sub.Items.Count > 0)
                {
                    menu.Items.Add(sub);
                }
            }
        }

        if (rt.IsAvailable("cleaning-mode"))
        {
            any = true;
            menu.Items.Add(Item("Cleaning Mode", () =>
            {
                if (_host.Services.Get<ICleaningModeService>() is { } cleaning)
                {
                    cleaning.Start();
                }
                else
                {
                    FindAction("cleaning-mode", "start")?.Invoke();
                }
            }));
        }

        if (any)
        {
            menu.Items.Add(new Separator());
        }

        menu.Items.Add(Item("Settings…", () => OpenSettings(SettingsPages.General), "Ctrl+,"));
        menu.Items.Add(Item("About WinKnife", () => OpenSettings(SettingsPages.About)));
        if (rt.IsAvailable("uninstaller"))
        {
            menu.Items.Add(Item("Uninstall an app…", () => OpenSettings(SettingsPages.Uninstaller)));
        }

        if (rt.IsAvailable("shelf") && _s.App.Get<bool>("shelfEnabled", true))
        {
            menu.Items.Add(Item("Open shelf", () =>
            {
                if (_host.Services.Get<IShelfService>() is { } shelf)
                {
                    shelf.Toggle();
                }
                else
                {
                    FindAction("shelf", "toggle")?.Invoke();
                }
            }));
        }

        menu.Items.Add(Item("Check for updates…", () => OpenSettings(SettingsPages.About)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Quit WinKnife", Quit, "Ctrl+Q"));
        return menu;
    }

    private static MenuItem Item(string header, Action onClick, string? gesture = null)
    {
        var item = new MenuItem { Header = header, InputGestureText = gesture };
        item.Click += (_, _) => onClick();
        return item;
    }
}
