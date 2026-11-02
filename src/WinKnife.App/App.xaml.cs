// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.App;

/// <summary>Application entry: single instance, service composition, startup guard and the shells.</summary>
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
        "WinKnife.Features.Capture",
        "WinKnife.Features.Agents",
        "WinKnife.Features.Files",
        "WinKnife.Features.Maintenance",
    ];

    private Mutex? _mutex;
    private EventWaitHandle? _activateEvent;
    private RegisteredWaitHandle? _activateWait;
    private EventWaitHandle? _quitEvent;
    private RegisteredWaitHandle? _quitWait;
    private CoreHost? _host;
    private AppShell? _shell;
    private DispatcherTimer? _healthyTimer;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var quitRequested = e.Args.Any(a => string.Equals(a, "--quit", StringComparison.OrdinalIgnoreCase));
        var relaunchIndex = Array.FindIndex(e.Args, a => string.Equals(a, "--relaunch", StringComparison.OrdinalIgnoreCase));
        if (relaunchIndex >= 0 && relaunchIndex + 1 < e.Args.Length && int.TryParse(e.Args[relaunchIndex + 1], out var oldPid))
        {
            // Started by "Restart now": wait (<= 20 s) for the previous process to exit before taking the single-instance lock.
            try
            {
                Process.GetProcessById(oldPid).WaitForExit(20_000);
            }
            catch (Exception)
            {
                // already gone
            }
        }

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
            // Ask the running instance to quit (--quit) or to recover (show the panel / Settings), then leave.
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

        // Created before the (possibly slow, cold-disk) feature loading so --quit / activation requests from a second
        // process are never lost; the handlers are queued on the dispatcher until startup has finished.
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        _activateWait = ThreadPool.RegisterWaitForSingleObject(
            _activateEvent,
            (_, _) => Dispatcher.BeginInvoke(() => _shell?.Recover()),
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

        _host = new CoreHost(Dispatcher);
        var log = _host.Log;
        log.Info($"WinKnife {AppInfo.Version} starting");
        var appearance = _host.App.Get<string>(AppKeys.AppAppearance) switch
        {
            "light" => AppAppearance.Light,
            "dark" => AppAppearance.Dark,
            _ => AppAppearance.System,
        };
        ThemeManager.Initialize(this, appearance);

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

        // Startup watch: if the previous start never finished, skip every optional startup window this time.
        var previousStartFailed = _host.App.Get<bool>(AppKeys.StartupDidNotFinish);
        _host.App.Set(AppKeys.StartupDidNotFinish, true);
        _healthyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _healthyTimer.Tick += (_, _) =>
        {
            _healthyTimer.Stop();
            _host?.App.Set(AppKeys.StartupDidNotFinish, false);
        };
        _healthyTimer.Start();

        LoadFeatureAssemblies(log);
        _shell = new AppShell(_host);
        _host.Contributions.Discover();
        _host.Runtime.Initialize();
        foreach (var feature in _host.Features.Features)
        {
            if (FeatureCatalog.Get(feature.CatalogId) is not null)
            {
                // TEMPORARY: show v1 settings views until the feature project contributes real sections.
                _host.Contributions.AddProvider(new LegacySettingsSection(feature, _host.Features));
            }
        }

        _shell.Start();
        if (!_host.App.Get<bool>(AppKeys.HasOnboarded) && !previousStartFailed)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => _shell.ShowOnboarding());
        }

        // Developer flags used by the screenshot scripts: --open-settings <page#anchor>, --open-panel [tab].
        var settingsIndex = Array.FindIndex(e.Args, a => a == "--open-settings");
        if (settingsIndex >= 0 && settingsIndex + 1 < e.Args.Length)
        {
            var destination = e.Args[settingsIndex + 1];
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => _shell.OpenSettings(destination));
        }

        var searchIndex = Array.FindIndex(e.Args, a => a == "--search");
        if (searchIndex >= 0 && searchIndex + 1 < e.Args.Length)
        {
            var query = e.Args[searchIndex + 1];
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => _shell.SearchForDevelopment(query));
        }

        var uninstallIndex = Array.FindIndex(e.Args, a => a == "--dev-uninstall");
        if (uninstallIndex >= 0 && uninstallIndex + 1 < e.Args.Length)
        {
            var id = e.Args[uninstallIndex + 1];
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => _host!.Runtime.SetAvailable([id], false));
        }

        if (e.Args.Contains("--open-menu"))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => _shell.ShowMenuForDevelopment());
        }

        var panelIndex = Array.FindIndex(e.Args, a => a == "--open-panel");
        if (panelIndex >= 0)
        {
            var tab = panelIndex + 1 < e.Args.Length && !e.Args[panelIndex + 1].StartsWith("--", StringComparison.Ordinal) ? e.Args[panelIndex + 1] : null;
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => _shell.OpenPanelForDevelopment(tab));
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activateWait?.Unregister(null);
        _activateEvent?.Dispose();
        _quitWait?.Unregister(null);
        _quitEvent?.Dispose();
        _healthyTimer?.Stop();

        foreach (var window in Windows.OfType<Window>().ToList())
        {
            window.Close();
        }

        // Stops every feature (restoring system state), then releases hooks, hotkeys and flushes settings.
        try
        {
            _host?.Log.Info("WinKnife shutting down");
            _host?.App.Set(AppKeys.StartupDidNotFinish, false);
            _shell?.Dispose();
            _host?.Dispose();
            if (_shell?.DeleteDataOnExit == true)
            {
                try
                {
                    Directory.Delete(AppPaths.Root, recursive: true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }
        }
        catch (Exception ex)
        {
            _host?.Log.Error("Shutdown failed", ex);
        }

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
}
