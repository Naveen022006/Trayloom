// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.App;

/// <summary>Application entry: single instance, service composition, tray icon and window management.</summary>
public partial class App : Application
{
    private const string MutexName = @"Local\WinKnife.SingleInstance";
    private const string ActivateEventName = @"Local\WinKnife.Activate";
    private const string QuitEventName = @"Local\WinKnife.Quit";

    /// <summary>Feature assemblies referenced by the app; loaded explicitly so reflection discovery finds them.</summary>
    private static readonly string[] FeatureAssemblies =
    [
        "WinKnife.Features.Input",
        "WinKnife.Features.Windows",
        "WinKnife.Features.System",
        "WinKnife.Features.Tools",
        "WinKnife.Features.Island",
    ];

    private Mutex? _mutex;
    private EventWaitHandle? _activateEvent;
    private RegisteredWaitHandle? _activateWait;
    private EventWaitHandle? _quitEvent;
    private RegisteredWaitHandle? _quitWait;
    private CoreHost? _host;
    private TrayController? _tray;
    private FeaturesWindow? _featuresWindow;
    private QuickPanelWindow? _quickPanel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var quitRequested = e.Args.Any(a => string.Equals(a, "--quit", StringComparison.OrdinalIgnoreCase));
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
        if (quitRequested && isFirstInstance)
        {
            // Nothing is running, so there is nothing to quit.
            _mutex.ReleaseMutex();
            _mutex.Dispose();
            _mutex = null;
            Shutdown();
            return;
        }

        if (!isFirstInstance)
        {
            // Ask the running instance to quit (--quit) or to show its Features window, then leave.
            try
            {
                using var existing = EventWaitHandle.OpenExisting(quitRequested ? QuitEventName : ActivateEventName);
                existing.Set();
            }
            catch (Exception)
            {
                // The first instance may still be starting; nothing else to do.
            }

            _mutex.Dispose();
            _mutex = null;
            Shutdown();
            return;
        }

        var firstRun = !File.Exists(AppPaths.SettingsFile);

        // Created before the (possibly slow, cold-disk) feature loading so --quit / activation requests from a second
        // process are never lost; the handlers are queued on the dispatcher until startup has finished.
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        _activateWait = ThreadPool.RegisterWaitForSingleObject(
            _activateEvent,
            (_, _) => Dispatcher.BeginInvoke(ShowFeatures),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        // "WinKnife.exe --quit" from a second process asks this instance to shut down gracefully (Stop() on every feature).
        _quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
        _quitWait = ThreadPool.RegisterWaitForSingleObject(
            _quitEvent,
            (_, _) => Dispatcher.BeginInvoke(() => Shutdown()),
            null,
            Timeout.Infinite,
            executeOnlyOnce: true);

        ThemeManager.Initialize(this);
        _host = new CoreHost(Dispatcher);
        var log = _host.Log;
        log.Info($"WinKnife {Assembly.GetExecutingAssembly().GetName().Version} starting");

        DispatcherUnhandledException += (_, args) =>
        {
            log.Error("Unhandled UI exception", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            log.Error("Unhandled exception" + (args.IsTerminating ? " (terminating)" : string.Empty), args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        LoadFeatureAssemblies(log);
        _host.Features.Discover();
        _host.Features.StartInstalled();

        _tray = new TrayController(log);
        _tray.QuickPanelRequested += ToggleQuickPanel;
        _tray.FeaturesRequested += ShowFeatures;
        _tray.QuitRequested += () => Shutdown();
        _host.Notifier.Sink = _tray.ShowBalloon;

        if (firstRun)
        {
            ShowFeatures();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activateWait?.Unregister(null);
        _activateEvent?.Dispose();
        _quitWait?.Unregister(null);
        _quitEvent?.Dispose();

        _quickPanel?.Close();
        _featuresWindow?.Close();

        // Stops every feature (restoring system state), then releases hooks, hotkeys and flushes settings.
        try
        {
            _host?.Log.Info("WinKnife shutting down");
            _host?.Dispose();
        }
        catch (Exception ex)
        {
            _host?.Log.Error("Shutdown failed", ex);
        }

        _tray?.Dispose();
        if (_mutex is not null)
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }

        base.OnExit(e);
    }

    private static void LoadFeatureAssemblies(ILog log)
    {
        foreach (var name in FeatureAssemblies)
        {
            try
            {
                Assembly.Load(name);
            }
            catch (Exception ex)
            {
                log.Error($"Could not load feature assembly {name}", ex);
            }
        }
    }

    private void ShowFeatures()
    {
        if (_host is null)
        {
            return;
        }

        if (_featuresWindow is null)
        {
            _featuresWindow = new FeaturesWindow(_host.Features, _host.Log);
            _featuresWindow.Closed += (_, _) => _featuresWindow = null;
            _featuresWindow.Show();
        }

        if (_featuresWindow.WindowState == WindowState.Minimized)
        {
            _featuresWindow.WindowState = WindowState.Normal;
        }

        _featuresWindow.Activate();
    }

    private void ToggleQuickPanel()
    {
        if (_host is null)
        {
            return;
        }

        if (_quickPanel is not null)
        {
            _quickPanel.Close();
            return;
        }

        // Clicking the tray icon deactivates (and closes) an open panel before this runs; treat that as a toggle.
        if (Environment.TickCount64 - QuickPanelWindow.LastClosedTick < 300)
        {
            return;
        }

        _quickPanel = new QuickPanelWindow(_host.Features, ShowFeatures, Screens.CursorPosition());
        _quickPanel.Closed += (_, _) => _quickPanel = null;
        _quickPanel.Show();
    }
}
