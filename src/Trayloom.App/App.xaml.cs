// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.App;

/// <summary>Application entry: single instance, service composition, startup guard and the shells.</summary>
public partial class App : Application
{
    private const string MutexName = @"Local\Trayloom.SingleInstance";
    private const string ActivateEventName = @"Local\Trayloom.Activate";
    private const string QuitEventName = @"Local\Trayloom.Quit";

    /// <summary>Feature assemblies referenced by the app; loaded explicitly so reflection discovery finds them.</summary>
    private static readonly string[] FeatureAssemblies =
    [
        "Trayloom.Features.Input",
        "Trayloom.Features.Windows",
        "Trayloom.Features.System",
        "Trayloom.Features.Tools",
        "Trayloom.Features.Island",
        "Trayloom.Features.Capture",
        "Trayloom.Features.Agents",
        "Trayloom.Features.Files",
        "Trayloom.Features.Maintenance",
    ];

    private Mutex? _mutex;
    private EventWaitHandle? _activateEvent;
    private RegisteredWaitHandle? _activateWait;
    private EventWaitHandle? _quitEvent;
    private RegisteredWaitHandle? _quitWait;
    private CoreHost? _host;
    private AppShell? _shell;
    private DispatcherTimer? _healthyTimer;
    private ProblemReporter? _problems;
    private InstallWindow? _installWindow;

    public App()
    {
        // The first start after copying new files can take ~10 s (Windows scans every DLL): show "Starting Trayloom…" after 2 s.
        StartupSplash.Arm(TimeSpan.FromSeconds(2));
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            Start(e);
        }
        finally
        {
            StartupSplash.Disarm();
        }
    }

    private void Start(StartupEventArgs e)
    {
        if (e.Args.Any(a => string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            RunUninstaller(e.Args);
            return;
        }

        // Developer flag: pretend start-up is slow (to see the "Starting Trayloom…" toast): --slow-start <milliseconds>.
        var slowIndex = Array.FindIndex(e.Args, a => a == "--slow-start");
        if (slowIndex >= 0 && slowIndex + 1 < e.Args.Length && int.TryParse(e.Args[slowIndex + 1], out var slowMs))
        {
            Thread.Sleep(Math.Clamp(slowMs, 0, 30_000));
        }

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
                if (!quitRequested && e.Args.Any(a => DevFlags.Contains(a)))
                {
                    // Hand the developer flags to the running instance.
                    File.WriteAllLines(DevCommandFile, e.Args);
                }

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
            (_, _) => Dispatcher.BeginInvoke(OnActivateRequested),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        // "Trayloom.exe --quit" from a second process asks this instance to shut down gracefully (Stop() on every feature).
        _quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
        _quitWait = ThreadPool.RegisterWaitForSingleObject(
            _quitEvent,
            (_, _) => Dispatcher.BeginInvoke(() => Shutdown()),
            null,
            Timeout.Infinite,
            executeOnlyOnce: true);

        // Installs from before the rename: stop a still-running old instance and move its settings / data over to the Trayloom names
        // before anything reads them (see LegacyMigration / LegacyNames).
        LegacyMigration.Run();

        _host = new CoreHost(Dispatcher);
        var log = _host.Log;
        log.Info($"Trayloom {AppInfo.Version} starting");
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

        _problems = new ProblemReporter(log);
        _host.Features.ProblemReported += _problems.Add;
        log.Info(SmartAppControl.Describe(SmartAppControl.GetState()));
        LoadFeatureAssemblies(log, _problems);
        _shell = new AppShell(_host) { InstallOffer = manual => OfferInstall(manual, null) };
        _host.Contributions.Discover();
        _host.Runtime.Initialize();
        MigratePanelKeys();
        _shell.Start();

        var justInstalled = e.Args.Any(a => string.Equals(a, "--just-installed", StringComparison.OrdinalIgnoreCase));
        var forceInstall = e.Args.Any(a => string.Equals(a, "--install", StringComparison.OrdinalIgnoreCase));
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => AfterStartup(previousStartFailed, justInstalled, forceInstall, e.Args));

        RunDevFlags(e.Args, startup: true);
        ScheduleMemoryTrim();
    }

    /// <summary>
    /// First thing the user sees once the tray icon exists: the install offer (when running from outside the install folder),
    /// then onboarding on a fresh profile, and the one-time "Trayloom is running" toast pointing at the icon.
    /// </summary>
    private void AfterStartup(bool previousStartFailed, bool justInstalled, bool forceInstall, string[] args)
    {
        var shell = _shell!;
        var host = _host!;
        void Continue()
        {
            if (!host.App.Get<bool>(AppKeys.HasOnboarded) && !previousStartFailed && !args.Contains("--no-onboarding"))
            {
                shell.ShowOnboarding();
            }

            if (justInstalled || !host.App.Get<bool>(AppKeys.FirstRunToastShown))
            {
                // A moment later so the tray icon is registered with the shell first.
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    shell.ShowRunningToast(justInstalled);
                };
                timer.Start();
            }
        }

        var offer = (forceInstall || (!previousStartFailed && ShouldOfferInstall(host))) && !InstallManager.IsInstalledCopy;
        if (offer)
        {
            OfferInstall(manual: forceInstall, Continue);
        }
        else
        {
            Continue();
        }
    }

    private static bool ShouldOfferInstall(CoreHost host) =>
        !InstallManager.IsInstalledCopy
        && !InstallManager.IsDevelopmentBuild
        && !host.App.Get<bool>(AppKeys.InstallPromptDismissed)
        && Environment.GetEnvironmentVariable("TRAYLOOM_NO_INSTALL_PROMPT") != "1";

    /// <summary>Shows the "Install Trayloom" window; <paramref name="done"/> runs when the user chose "Just run it" or closed it.</summary>
    private void OfferInstall(bool manual, Action? done)
    {
        if (_installWindow is not null)
        {
            _installWindow.Activate();
            return;
        }

        if (InstallManager.IsInstalledCopy)
        {
            if (manual)
            {
                Dialogs.Show(null, Loc.T("Trayloom is installed"), Loc.F("This copy already runs from {0}.", InstallManager.InstallRoot), [Loc.T("OK")]);
            }

            done?.Invoke();
            return;
        }

        var host = _host!;
        var window = new InstallWindow(host.Log);
        _installWindow = window;
        window.Closed += (_, _) =>
        {
            _installWindow = null;
            switch (window.Choice)
            {
                case InstallChoice.Installed:
                    LaunchInstalledCopy(window);
                    return;
                case InstallChoice.JustRun:
                    if (window.DontAskAgain)
                    {
                        host.App.Set(AppKeys.InstallPromptDismissed, true);
                    }

                    break;
            }

            done?.Invoke();
        };
        window.Show();
        window.Activate();
    }

    /// <summary>Starts the installed copy (it waits for this process to exit) and quits this one.</summary>
    private void LaunchInstalledCopy(InstallWindow window)
    {
        var host = _host!;
        try
        {
            host.App.Set(AppKeys.LaunchAtLoginWanted, AutoStart.IsEnabled);
            host.Store.Flush();
            var exe = window.InstalledExe!;
            host.Log.Info("Starting the installed copy " + exe);
            Process.Start(new ProcessStartInfo(exe, $"--relaunch {Environment.ProcessId} --just-installed") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! });
        }
        catch (Exception ex)
        {
            host.Log.Error("Could not start the installed copy", ex);
            Dialogs.Show(null, Loc.T("Trayloom was installed"), Loc.F("It could not be started automatically. Open it from the Start menu, or run {0}.", window.InstalledExe), [Loc.T("OK")]);
            return;
        }

        Dispatcher.BeginInvoke(() => Shutdown());
    }

    // ------------------------------------------------------------------------------------------------ uninstall

    /// <summary><c>Trayloom.exe --uninstall</c> (Apps &amp; features): confirm, stop the running instance, remove everything.</summary>
    private void RunUninstaller(string[] args)
    {
        var quiet = args.Any(a => string.Equals(a, "--quiet", StringComparison.OrdinalIgnoreCase));
        ThemeManager.Initialize(this, AppAppearance.System);
        var log = new FileLog();
        var options = new UninstallOptions(false);
        if (!quiet)
        {
            var window = new UninstallWindow();
            window.ShowDialog();
            if (!window.Confirmed)
            {
                Shutdown();
                return;
            }

            options = window.Options;
        }

        StopRunningInstance(log);
        List<string> later;
        try
        {
            later = InstallManager.Uninstall(options, log);
        }
        catch (Exception ex)
        {
            log.Error("Uninstall failed", ex);
            Dialogs.Show(null, Loc.T("Uninstall stopped"), ex.Message, [Loc.T("OK")]);
            Shutdown();
            return;
        }

        if (!quiet)
        {
            Dialogs.Show(null, Loc.T("Trayloom was uninstalled"), options.RemoveSettings
                ? Loc.T("The program files, shortcuts and your settings are being removed.")
                : Loc.T("The program files and shortcuts are being removed. Your settings were kept, so a later install picks up where you left off."), [Loc.T("OK")]);
        }

        InstallManager.ScheduleDeletion(later, log);
        Shutdown();
    }

    /// <summary>Asks a running Trayloom to quit and waits for it (the single-instance mutex is free again).</summary>
    private static void StopRunningInstance(ILog log)
    {
        try
        {
            using var quit = EventWaitHandle.OpenExisting(QuitEventName);
            quit.Set();
        }
        catch (Exception)
        {
            return; // nothing is running
        }

        try
        {
            using var mutex = new Mutex(false, MutexName);
            var got = false;
            try
            {
                got = mutex.WaitOne(15_000);
            }
            catch (AbandonedMutexException)
            {
                got = true;
            }

            if (got)
            {
                mutex.ReleaseMutex();
            }
            else
            {
                log.Warn("The running Trayloom did not quit in time; closing it");
                foreach (var process in Process.GetProcessesByName("Trayloom").Where(p => p.Id != Environment.ProcessId))
                {
                    process.Kill();
                }
            }
        }
        catch (Exception ex)
        {
            log.Warn("Stopping the running instance failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Start-up work (feature scans, the AI Agents history, thumbnails) leaves a lot of garbage behind; compact the heap once things
    /// have settled and once more a few minutes later so an idle tray app does not sit on hundreds of megabytes.
    /// </summary>
    private void ScheduleMemoryTrim()
    {
        foreach (var delay in new[] { TimeSpan.FromSeconds(40), TimeSpan.FromMinutes(4) })
        {
            var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = delay };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try
                {
                    System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                }
                catch (Exception)
                {
                    // best effort
                }
            };
            timer.Start();
        }
    }

    private static string DevCommandFile => Path.Combine(AppPaths.Root, "devcmd.txt");

    /// <summary>
    /// Developer flags used by the screenshot scripts: <c>--open-settings &lt;page#anchor&gt;</c>, <c>--open-panel [tab]</c>, <c>--open-menu</c>,
    /// <c>--search &lt;text&gt;</c>, <c>--dev-uninstall &lt;id&gt;</c>, <c>--dev-install &lt;id&gt;</c>, <c>--action &lt;feature&gt; &lt;action&gt;</c>, <c>--dump-info &lt;file&gt;</c>,
    /// <c>--close-panel</c>, <c>--set-language &lt;code&gt;</c>. A second launch with any of them hands them to the running instance (through <c>devcmd.txt</c>).
    /// </summary>
    private void RunDevFlags(string[] args, bool startup)
    {
        var shell = _shell!;
        var priority = startup ? DispatcherPriority.ApplicationIdle : DispatcherPriority.Normal;
        string? Arg(string flag, int offset = 1)
        {
            var i = Array.FindIndex(args, a => a == flag);
            return i >= 0 && i + offset < args.Length ? args[i + offset] : null;
        }

        if (Arg("--open-settings") is { } destination)
        {
            Dispatcher.BeginInvoke(priority, () => shell.OpenSettings(destination));
        }

        if (Arg("--search") is { } query)
        {
            Dispatcher.BeginInvoke(priority, () => shell.SearchForDevelopment(query));
        }

        if (Arg("--dev-uninstall") is { } uninstall)
        {
            Dispatcher.BeginInvoke(priority, () => _host!.Runtime.SetAvailable([uninstall], false));
        }

        if (Arg("--dev-install") is { } install)
        {
            Dispatcher.BeginInvoke(priority, () =>
            {
                if (install == "all")
                {
                    // --dev-install all: Install all (no enable key is switched on, so no hook starts doing anything).
                    _host!.Runtime.SetAllAvailable(true);
                }
                else
                {
                    _host!.Runtime.SetAvailable([install], true);
                }
            });
        }

        if (Arg("--dump-info") is { } dump)
        {
            Dispatcher.BeginInvoke(priority, () => DumpInfo(dump));
        }

        if (Arg("--action") is { } feature && Arg("--action", 2) is { } actionId)
        {
            Dispatcher.BeginInvoke(priority, () =>
            {
                var hit = _host!.Features.All.FirstOrDefault(x => x.Feature.CatalogId == FeatureCatalog.Resolve(feature) && x.Action.Id == actionId);
                if (hit.Action is null)
                {
                    _host.Log.Warn($"--action: {feature}/{actionId} not found");
                    return;
                }

                hit.Action.Invoke();
            });
        }

        if (args.Contains("--close-panel"))
        {
            Dispatcher.BeginInvoke(priority, () => shell.ClosePanel());
        }

        if (args.Contains("--open-menu"))
        {
            Dispatcher.BeginInvoke(priority, () => shell.ShowMenuForDevelopment());
        }

        if (Arg("--open-onboarding") is { } stepText || args.Contains("--open-onboarding"))
        {
            // --open-onboarding [step]: jump to a step of the introduction (screenshots).
            Dispatcher.BeginInvoke(priority, () =>
            {
                if (int.TryParse(Arg("--open-onboarding"), out var step))
                {
                    _host!.App.Set(AppKeys.OnboardingStep, Math.Clamp(step, 0, 4));
                }

                shell.ShowOnboarding();
            });
        }

        if (args.Contains("--open-install"))
        {
            Dispatcher.BeginInvoke(priority, () => OfferInstall(manual: true, null));
        }

        if (args.Contains("--show-toast"))
        {
            Dispatcher.BeginInvoke(priority, () => shell.ShowRunningToast(false));
        }

        if (Arg("--simulate-problem") is { } kind)
        {
            Dispatcher.BeginInvoke(priority, () =>
            {
                var blocked = new FileLoadException("Could not load file or assembly 'Trayloom.Features.Input, Version=0.1.0.0'. An Application Control policy has blocked this file. (0x800711C7)", "Trayloom.Features.Input.dll") { HResult = unchecked((int)0x800711C7) };
                var crash = new InvalidOperationException("The audio service did not answer.");
                _problems?.Add(kind == "sac"
                    ? new StartupProblem("Trayloom.Features.Input.dll", blocked.Message, blocked, true)
                    : new StartupProblem(Loc.T("Volume mixer"), crash.Message, crash, false));
            });
        }

        if (Arg("--shot") is { } shotFile)
        {
            var titlePart = Arg("--shot", 2) is { } t && !t.StartsWith("--", StringComparison.Ordinal) ? t : null;
            var delay = int.TryParse(Arg("--shot-delay"), out var shotDelay) ? Math.Clamp(shotDelay, 100, 20_000) : 900;
            Dispatcher.BeginInvoke(priority, () => DevShot.Take(shotFile, titlePart, delay));
        }

        if (Arg("--host-utility") is { } hostUtility)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () => shell.HostUtilityForDevelopment(hostUtility));
        }

        if (Arg("--scroll-to") is { } scrollNeedle)
        {
            // --scroll-to <text>: scrolls the first text block containing it into view (underscores = spaces).
            Dispatcher.BeginInvoke(priority, () => DevLayout.ScrollTo(scrollNeedle.Replace('_', ' ')));
        }

        if (Arg("--dump-text") is { } needle && Arg("--dump-text", 2) is { } dumpFile)
        {
            // --dump-text <needle> <file>: layout facts (sizes, parents) of every TextBlock whose text contains the needle (underscores = spaces).
            Dispatcher.BeginInvoke(priority, () => DevLayout.Dump(needle.Replace('_', ' '), dumpFile));
        }

        if (Arg("--shot-full") is { } fullFile)
        {
            var delay = int.TryParse(Arg("--shot-delay"), out var fullDelay) ? Math.Clamp(fullDelay, 100, 20_000) : 1200;
            Dispatcher.BeginInvoke(priority, () => DevShot.TakeFull(fullFile, delay));
        }

        if (Arg("--set-language") is { } languageCode)
        {
            // --set-language <code|system>: the same change as Settings > General > Language (checks the live language switch).
            Dispatcher.BeginInvoke(priority, () => _host!.App.Set(AppKeys.AppLanguage, languageCode));
        }

        if (Arg("--set-appearance") is { } appearance)
        {
            // --set-appearance <system|light|dark>: the same change as Settings > General > Appearance.
            Dispatcher.BeginInvoke(priority, () =>
            {
                _host!.App.Set(AppKeys.AppAppearance, appearance);
                ThemeManager.SetAppearance(appearance switch { "light" => AppAppearance.Light, "dark" => AppAppearance.Dark, _ => AppAppearance.System });
            });
        }

        if (Arg("--dev-key") is { } keyName)
        {
            // Raises a key event inside this process (no synthesized system keystroke): used to check Esc handling.
            Dispatcher.BeginInvoke(priority, () => RaiseKey(keyName));
        }

        var panelIndex = Array.FindIndex(args, a => a == "--open-panel");
        if (panelIndex >= 0)
        {
            var tab = panelIndex + 1 < args.Length && !args[panelIndex + 1].StartsWith("--", StringComparison.Ordinal) ? args[panelIndex + 1] : null;
            Dispatcher.BeginInvoke(priority, () => shell.OpenPanelForDevelopment(tab));
        }
    }

    private static readonly string[] DevFlags = ["--open-settings", "--open-panel", "--open-menu", "--search", "--dev-uninstall", "--dev-install", "--action", "--dump-info", "--close-panel", "--open-onboarding", "--open-install", "--show-toast", "--simulate-problem", "--dev-key", "--shot", "--set-language", "--set-appearance", "--shot-delay", "--shot-full", "--dump-text", "--scroll-to", "--host-utility"];

    /// <summary>Raises a PreviewKeyDown for <paramref name="name"/> (e.g. "escape") on the active window, as if it had been typed there.</summary>
    private void RaiseKey(string name)
    {
        if (!Enum.TryParse<System.Windows.Input.Key>(name, ignoreCase: true, out var key))
        {
            return;
        }

        var window = Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Windows.OfType<Window>().LastOrDefault(w => w.IsVisible);
        if (window is null)
        {
            return;
        }

        var source = PresentationSource.FromVisual(window);
        var target = System.Windows.Input.Keyboard.FocusedElement as UIElement ?? window;
        var args = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent,
        };
        target.RaiseEvent(args);
    }

    /// <summary>Activation request: a pending developer command wins over "recover" (show the panel / Settings).</summary>
    private void OnActivateRequested()
    {
        try
        {
            if (File.Exists(DevCommandFile))
            {
                var lines = File.ReadAllLines(DevCommandFile);
                File.Delete(DevCommandFile);
                if (lines.Length > 0 && _shell is not null)
                {
                    RunDevFlags(lines, startup: false);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _host?.Log.Warn("Developer command failed: " + ex.Message);
        }

        _shell?.Recover();
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
            _host?.Log.Info(Loc.T("Trayloom shutting down"));
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

    /// <summary>One-time move of the old <c>panelUtilityShow.&lt;id&gt;</c> keys to Vorssaint's <c>panelUtility&lt;Name&gt;</c> keys (single source of truth).</summary>
    private void MigratePanelKeys()
    {
        try
        {
            var app = _host!.App;
            foreach (var utility in _host.Contributions.PanelUtilities())
            {
                var legacy = "panelUtilityShow." + utility.Id;
                if (!app.IsSet(legacy))
                {
                    continue;
                }

                var key = PanelKeys.Utility(utility.Id);
                if (!app.IsSet(key))
                {
                    app.Set(key, app.Get<bool>(legacy));
                }

                app.Reset(legacy);
            }
        }
        catch (Exception ex)
        {
            _host?.Log.Warn("Panel key migration failed: " + ex.Message);
        }
    }

    /// <summary>Developer flag <c>--dump-info &lt;file&gt;</c>: writes features, actions, contributions and shortcut defaults, finds duplicates.</summary>
    private void DumpInfo(string path)
    {
        var sb = new System.Text.StringBuilder();
        var host = _host!;
        sb.AppendLine("# features (IFeature.Id -> catalog id) and actions");
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in host.Features.Features)
        {
            var assembly = f.GetType().Assembly.GetName().Name;
            sb.AppendLine(Loc.F("{0} -> {1} [{2}] running={3}", f.Id, f.CatalogId, assembly, host.Runtime.IsRunning(f.CatalogId)));
            if (seen.TryGetValue(f.CatalogId, out var other))
            {
                sb.AppendLine($"  !! DUPLICATE catalog id {f.CatalogId}: also {other}");
            }
            else
            {
                seen[f.CatalogId] = f.Id + " [" + assembly + "]";
            }

            if (FeatureCatalog.Get(f.CatalogId) is null)
            {
                sb.AppendLine("  !! no catalog entry");
            }

            try
            {
                foreach (var a in f.Actions)
                {
                    sb.AppendLine($"  action {a.Id} | {a.Title}");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("  actions failed: " + ex.Message);
            }
        }

        sb.AppendLine();
        sb.AppendLine("# catalog entries without an IFeature class");
        foreach (var e in FeatureCatalog.All.Where(c => host.Features.Features.All(f => f.CatalogId != c.Id)))
        {
            sb.AppendLine(Loc.F("{0} support={1}", e.Id, e.Support));
        }

        sb.AppendLine();
        sb.AppendLine("# panel utilities");
        foreach (var u in host.Contributions.PanelUtilities())
        {
            sb.AppendLine(Loc.F("{0} key={1} hostedOnly={2} gate={3}", u.Id, PanelKeys.Utility(u.Id), u.HostedOnly, string.Join(",", u.FeatureIds)));
        }

        sb.AppendLine();
        sb.AppendLine("# shortcut defaults (role: effective, problem) and duplicates");
        var byKey = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in ShortcutRoles.CreateStatic())
        {
            var hk = role.Default;
            sb.AppendLine($"{role.Id,-32} {(hk is null ? "-" : hk.ToString()),-22} group={role.Group} keys={string.Join("+", role.EnableKeys)}");
            if (hk is not null)
            {
                var k = hk.ToString()!;
                if (!byKey.TryGetValue(k, out var list))
                {
                    byKey[k] = list = [];
                }

                list.Add(role.Id);
            }
        }

        foreach (var (k, list) in byKey.Where(p => p.Value.Count > 1))
        {
            sb.AppendLine($"!! DUPLICATE default {k}: {string.Join(", ", list)}");
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static void LoadFeatureAssemblies(ILog log, ProblemReporter problems)
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
                problems.Add(new StartupProblem(name + ".dll", ex.Message, ex, SmartAppControl.LooksBlocked(ex)));
            }
        }
    }
}
