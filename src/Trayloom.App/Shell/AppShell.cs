// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.App;

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
    private UpdateService? _updates;
    private ITrayMetricsState? _hookedMetrics;
    private IKeepAwakeService? _hookedKeepAwake;
    private IMicMuteState? _hookedMic;

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

    /// <summary>Set by the application: shows the "Install Trayloom" offer (the argument is true when the user asked for it).</summary>
    public Action<bool>? InstallOffer { get; set; }

    public void Start()
    {
        _updates = new UpdateService(_s.App, _host.Log, _host.Notifier, Application.Current.Dispatcher);
        _s.Updates = _updates;
        _updates.Changed += UpdateTrayState;
        _updates.Start();
        _tray = new TrayController(_host.Log) { GlyphCode = _s.App.Get<string>(AppKeys.MenuBarIconSymbol) };
        _tray.LeftClick += TogglePanel;
        _tray.RightClick += ShowMenu;
        _tray.BalloonClicked += () => Application.Current.Dispatcher.BeginInvoke(ShowPanelAtTray);
        _host.Notifier.Sink = _tray.ShowBalloon;
        _s.App.Changed += key =>
        {
            if (key == AppKeys.MenuBarIconSymbol)
            {
                Application.Current.Dispatcher.BeginInvoke(() => _tray!.GlyphCode = _s.App.Get<string>(AppKeys.MenuBarIconSymbol));
            }
            else if (key is "micMuteMenuBarIndicator" or "keepAwakeActiveIcon" or "keepAwakeIconTint" or "showCountdownInMenuBar" or "menuBarHideIconWithMetrics")
            {
                Application.Current.Dispatcher.BeginInvoke(UpdateTrayState);
            }
        };
        _host.Services.Changed += () => Application.Current.Dispatcher.BeginInvoke(UpdateTrayState);
        Loc.Changed += () => Application.Current.Dispatcher.BeginInvoke(OnLanguageChanged);
        _trayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _trayTimer.Tick += (_, _) => UpdateTrayState();
        _trayTimer.Start();
        UpdateTrayState();
    }

    /// <summary>
    /// The language changed (Settings > General > Language): like Vorssaint the open windows take the new language at once.
    /// The panel closes (it is rebuilt on its next opening) and Settings is recreated on the page that was open.
    /// </summary>
    private void OnLanguageChanged()
    {
        try
        {
            _panel?.ClosePanel();
            if (_settings is { } old)
            {
                var destination = _s.App.Get<string>(AppKeys.SettingsLastPage);
                var bounds = (old.Left, old.Top, old.Width, old.Height, old.WindowState);
                _settings = null;
                old.Close();
                ShowSettings(string.IsNullOrEmpty(destination) ? SettingsPages.General : destination, null);
                if (_settings is { } fresh && bounds.WindowState == WindowState.Normal)
                {
                    fresh.WindowStartupLocation = WindowStartupLocation.Manual;
                    fresh.Left = bounds.Left;
                    fresh.Top = bounds.Top;
                    fresh.Width = bounds.Width;
                    fresh.Height = bounds.Height;
                }
            }

            UpdateTrayState();
        }
        catch (Exception ex)
        {
            _host.Log.Warn("Could not refresh the windows for the new language: " + ex.Message);
        }
    }

    public void Dispose()
    {
        _trayTimer?.Stop();
        _updates?.Dispose();
        _tray?.Dispose();
    }

    // ------------------------------------------------------------------------------------------------ IShell

    public void OpenSettings(string destination, string? targetFeature = null)
    {
        Application.Current.Dispatcher.BeginInvoke(() => ShowSettings(destination, targetFeature));
    }

    /// <summary>"Settings": the page that was open last (General the first time, or when that page is gone).</summary>
    private string ResolveDestination(string destination)
    {
        if (destination != SettingsPages.Last)
        {
            return destination;
        }

        var saved = _s.App.Get<string>(AppKeys.SettingsLastPage);
        if (string.IsNullOrEmpty(saved))
        {
            return SettingsPages.General;
        }

        var (page, anchor) = SettingsPages.Split(saved);
        return SidebarModel.IsPageVisible(_s.Runtime, new Visit(page, anchor, null)) ? saved : SettingsPages.General;
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

    /// <summary>Developer flag: types a query into the Settings search box.</summary>
    public void SearchForDevelopment(string query)
    {
        ShowSettings(SettingsPages.General, null);
        _settings?.SetSearchForDevelopment(query);
    }

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

    /// <summary>Developer flag <c>--host-utility &lt;id&gt;</c>: shows a hosted utility panel inside the open flyout (screenshots).</summary>
    public void HostUtilityForDevelopment(string id) => _panel?.HostUtility(id);

    public void Quit() => Application.Current.Dispatcher.BeginInvoke(() => ConfirmThen(() => Application.Current.Shutdown()));

    public void Restart() => Restart(false);

    public void Restart(bool skipSave) => Restart(skipSave, null);

    /// <summary>
    /// Restarts after the recording check. <paramref name="beforeRelaunch"/> runs only on the confirmed path, after saving was suspended,
    /// so a cancelled restart changes nothing and a pending debounced save cannot overwrite what it wrote. If it throws, nothing restarts.
    /// </summary>
    public void Restart(bool skipSave, Action? beforeRelaunch)
    {
        var exe = Environment.ProcessPath;
        if (exe is null)
        {
            return;
        }

        ConfirmThen(() =>
        {
            if (skipSave)
            {
                _host.Store.SuspendSaving();
            }

            try
            {
                beforeRelaunch?.Invoke();
            }
            catch (Exception ex)
            {
                _host.Log.Error("Preparing the restart failed", ex);
                Dialogs.Show(null, Loc.T("Restart stopped"), ex.Message, [Loc.T("OK")]);
                return;
            }

            try
            {
                // The new process waits (<= 35 s) for this one to exit, then starts normally.
                Process.Start(new ProcessStartInfo(exe, $"--relaunch {Environment.ProcessId}") { UseShellExecute = false });
            }
            catch (Exception ex)
            {
                _host.Log.Error("Relaunch failed", ex);
                return;
            }

            Application.Current.Dispatcher.BeginInvoke(() => Application.Current.Shutdown());
        });
    }

    public void QuitAndDeleteData()
    {
        ConfirmThen(() =>
        {
            _host.Store.SuspendSaving();
            _deleteDataOnExit = true;
            Application.Current.Dispatcher.BeginInvoke(() => Application.Current.Shutdown());
        });
    }

    /// <summary>
    /// Runs <paramref name="proceed"/> (on the UI thread) unless a recording is in progress and the user cancels. "Stop and quit" stops
    /// the recording and waits (up to 15 s) until the take is saved, so quitting never loses it.
    /// </summary>
    private void ConfirmThen(Action proceed)
    {
        IRecordingState? rec = null;
        try
        {
            rec = _host.Services.Get<IRecordingState>();
        }
        catch (Exception ex)
        {
            _host.Log.Warn("Could not ask the recorder: " + ex.Message);
        }

        if (rec is null)
        {
            proceed();
            return;
        }

        if (!rec.IsRecording)
        {
            // nothing to ask, but still let the recorder finish: it puts open editors into keep mode so no unsaved take is deleted
            _ = StopThenAsync(rec, proceed);
            return;
        }

        var choice = Dialogs.Show(null, Loc.T("A recording is in progress"), Loc.T("Quitting stops the recording and saves it first."),
            [Loc.T("Stop and quit"), Loc.T("Cancel")]);
        if (choice != 0)
        {
            return;
        }

        _ = StopThenAsync(rec, proceed);
    }

    private async Task StopThenAsync(IRecordingState rec, Action proceed)
    {
        try
        {
            if (!await rec.StopAndFinishAsync(QuitBudget.FinishWait))
            {
                _host.Log.Warn("The recording did not finish within 15 s; quitting anyway");
            }
        }
        catch (Exception ex)
        {
            _host.Log.Error("Stopping the recording before quitting failed", ex);
        }

        proceed();
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

    /// <summary>
    /// Second launch / reopen (the Start menu shortcut while Trayloom runs): the user must always be able to get back in (spec 8.3).
    /// Brings forward whatever is open; otherwise opens Settings on the last page and points at the tray icon.
    /// </summary>
    public void Recover()
    {
        if (_onboarding is not null)
        {
            _onboarding.WindowState = WindowState.Normal;
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
        UpdateTrayState();
        ShowSettings(SettingsPages.Last, null);
        _tray?.ShowBalloon(Loc.T("Trayloom is already running"), Loc.T("Settings is open. The Trayloom icon is next to the clock (click the ^ arrow if it is hidden)."), NotificationLevel.Info);
    }

    /// <summary>Opens the flyout next to the tray corner (where the icon lives), as if the icon had been clicked.</summary>
    public void ShowPanelAtTray()
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var area = Screens.Primary().WorkArea;
            OpenPanelAt(new Point(area.Right - 120, area.Bottom + 10));
        });
    }

    /// <summary>The one-time "Trayloom is running" toast (balloon) that points at the tray icon.</summary>
    public void ShowRunningToast(bool justInstalled)
    {
        var title = justInstalled ? Loc.T("Trayloom is installed and running") : Loc.T("Trayloom is running");
        _tray?.ShowBalloon(title, Loc.T("Click the icon by the clock to open it. If you cannot see it, click the ^ arrow next to the clock."), NotificationLevel.Info);
        _s.App.Set(AppKeys.FirstRunToastShown, true);
    }

    /// <summary>Install Trayloom… (tray menu, General): shows the install offer even if it was dismissed before.</summary>
    public void ShowInstallOffer() => Application.Current.Dispatcher.BeginInvoke(() => InstallOffer?.Invoke(true));

    /// <summary>"Report a problem": opens the folder with the log file.</summary>
    public void ReportProblem() => UiParts.OpenLogFolder();

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
        _panel.Closed += (_, _) =>
        {
            _panel = null;
            Trayloom.Core.Native.MemoryTrimmer.Schedule();
        };
        _panel.Show();
    }

    private void ShowSettings(string destination, string? targetFeature)
    {
        destination = ResolveDestination(destination);
        if (_settings is null)
        {
            _settings = new SettingsWindow(_s);
            _settings.Closed += (_, _) =>
            {
                _settings = null;
                Trayloom.Core.Native.MemoryTrimmer.Schedule();
            };
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
        var metrics = _host.Services.Get<ITrayMetricsState>();
        var app = _s.App;
        void Refresh() => Application.Current?.Dispatcher.BeginInvoke(UpdateTrayState);
        if (!ReferenceEquals(metrics, _hookedMetrics))
        {
            if (_hookedMetrics is not null)
            {
                _hookedMetrics.Changed -= Refresh;
            }

            _hookedMetrics = metrics;
            if (metrics is not null)
            {
                metrics.Changed += Refresh;
            }
        }

        if (!ReferenceEquals(keepAwake, _hookedKeepAwake))
        {
            if (_hookedKeepAwake is not null)
            {
                _hookedKeepAwake.Changed -= Refresh;
            }

            _hookedKeepAwake = keepAwake;
            if (keepAwake is not null)
            {
                keepAwake.Changed += Refresh;
            }
        }

        if (!ReferenceEquals(mic, _hookedMic))
        {
            if (_hookedMic is not null)
            {
                _hookedMic.Changed -= Refresh;
            }

            _hookedMic = mic;
            if (mic is not null)
            {
                mic.Changed += Refresh;
            }
        }

        var micShown = mic is { IsMuted: true } && app.Get<bool>("micMuteMenuBarIndicator", true);
        if (keepAwake is { IsActive: true })
        {
            var left = keepAwake.EndsAt is { } e ? e - DateTime.Now : (TimeSpan?)null;
            _tray.Tooltip = keepAwake.EndsAt is { } end
                ? Loc.F("Trayloom: awake until {0} ({1} left)", Loc.Fmt(end, "t"), FormatLeft(left!.Value))
                : Loc.T("Trayloom: awake indefinitely");
            _tray.Badge = micShown ? Color.FromRgb(0xFF, 0x45, 0x3A) : null;
            var (glyph, font) = app.Get<string>("keepAwakeActiveIcon") switch
            {
                "coffee" => ("☕", Loc.T("Segoe UI Symbol")),
                "eye" => ("", (string?)null),
                "moon" => ("", null),
                "light" => ("", null),
                _ => ((string?)null, (string?)null),
            };
            var tint = app.Get<string>("keepAwakeIconTint") switch
            {
                "green" => Color.FromRgb(0x34, 0xC7, 0x59),
                "blue" => Color.FromRgb(0x0A, 0x84, 0xFF),
                "purple" => Color.FromRgb(0xBF, 0x5A, 0xF2),
                "pink" => Color.FromRgb(0xFF, 0x37, 0x5F),
                "none" => (Color?)null,
                _ => Color.FromRgb(0xFF, 0x95, 0x00),
            };
            string? overlay = null;
            if (app.Get<bool>("showCountdownInMenuBar"))
            {
                overlay = left is { } l ? CompactLeft(l) : "∞";
            }

            _tray.SetLook(tint, glyph, font, overlay);
        }
        else
        {
            _tray.Tooltip = Loc.T("Trayloom: normal sleep");
            _tray.Badge = micShown ? Color.FromRgb(0xFF, 0x45, 0x3A) : null;
            _tray.SetLook(null, null, null, null);
        }

        // With live metrics in the tray the icon steps aside (menuBarHideIconWithMetrics), unless a signal needs it.
        var signal = keepAwake is { IsActive: true } || micShown;
        _tray.Visible = !(app.Get<bool>("menuBarHideIconWithMetrics") && metrics is { HasMetrics: true } && !signal);
    }

    private static string FormatLeft(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{(int)span.TotalHours} h {span.Minutes} min" : $"{Math.Max(0, (int)Math.Ceiling(span.TotalMinutes))} min";

    private static string CompactLeft(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{(int)Math.Ceiling(span.TotalHours)}h" : $"{Math.Max(0, (int)Math.Ceiling(span.TotalMinutes))}";

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
            menu.Items.Add(Item(active ? Loc.T("Disable keep awake") : Loc.T("Enable keep awake"), () =>
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
                var sub = new MenuItem { Header = Loc.T("Activate for…") };
                foreach (var (label, minutes, actionId) in new[]
                {
                    (Loc.T("15 minutes"), 15, "15m"), (Loc.T("30 minutes"), 30, ""), (Loc.T("1 hour"), 60, "1h"), (Loc.T("2 hours"), 120, "2h"), (Loc.T("4 hours"), 240, ""), (Loc.T("8 hours"), 480, ""), (Loc.T("Indefinitely"), 0, "forever"),
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
            menu.Items.Add(Item(Loc.T("Cleaning Mode"), () =>
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

        if (rt.IsAvailable("notch-bluetooth") && _host.Services.Get<IBluetoothState>() is { HasAdapter: not false } bt)
        {
            any = true;
            menu.Items.Add(BluetoothMenu.Build(bt, this));
        }

        if (any)
        {
            menu.Items.Add(new Separator());
        }

        menu.Items.Add(Item(Loc.T("Settings…"), () => OpenSettings(SettingsPages.Last), "Ctrl+,"));
        menu.Items.Add(Item(Loc.T("Add features…"), () => OpenFeatures()));
        menu.Items.Add(Item(Loc.T("About Trayloom"), () => OpenSettings(SettingsPages.About)));
        if (rt.IsAvailable("uninstaller"))
        {
            menu.Items.Add(Item(Loc.T("Uninstall an app…"), () => OpenSettings(SettingsPages.Uninstaller)));
        }

        if (rt.IsAvailable("shelf") && _s.App.Get<bool>("shelfEnabled", true))
        {
            menu.Items.Add(Item(Loc.T("Open shelf"), () =>
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

        menu.Items.Add(Item(_updates is { State: UpdateState.Available, LatestVersion: { } latest }
            ? Loc.F("Update available: {0}…", latest)
            : Loc.T("Check for updates…"), () => OpenSettings(SettingsPages.About)));
        menu.Items.Add(Item(Loc.T("Report a problem…"), ReportProblem));
        if (!InstallManager.IsInstalledCopy && !InstallManager.IsDevelopmentBuild)
        {
            menu.Items.Add(Item(Loc.T("Install Trayloom…"), () => InstallOffer?.Invoke(true)));
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Loc.T("Quit Trayloom"), Quit, "Ctrl+Q"));
        return menu;
    }

    private static MenuItem Item(string header, Action onClick, string? gesture = null)
    {
        var item = new MenuItem { Header = header, InputGestureText = gesture };
        item.Click += (_, _) => onClick();
        return item;
    }
}
