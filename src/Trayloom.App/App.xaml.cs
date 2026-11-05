// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Core.Native;
using Trayloom.Core.Ui;

namespace Trayloom.App;

/// <summary>Application entry: single instance, service composition, startup guard and the shells.</summary>
public partial class App : Application
{
    /// <summary>
    /// Developer aid: with TRAYLOOM_DATA and TRAYLOOM_INSTANCE both set, the single-instance names get a suffix so a development copy can
    /// run beside the installed one (each with its own settings, logs and dev command file under its TRAYLOOM_DATA).
    /// </summary>
    private static readonly string InstanceSuffix =
        Environment.GetEnvironmentVariable("TRAYLOOM_DATA") is { Length: > 0 } && Environment.GetEnvironmentVariable("TRAYLOOM_INSTANCE") is { Length: > 0 } name
            ? "." + string.Concat(name.Where(char.IsLetterOrDigit))
            : "";

    private static readonly string MutexName = @"Local\Trayloom.SingleInstance" + InstanceSuffix;
    private static readonly string ActivateEventName = @"Local\Trayloom.Activate" + InstanceSuffix;
    private static readonly string QuitEventName = @"Local\Trayloom.Quit" + InstanceSuffix;

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
        "Trayloom.Features.GitHub",
        "Trayloom.Features.Files",
        "Trayloom.Features.Maintenance",
    ];

    /// <summary>
    /// Developer flags (<c>--action</c>, <c>--shot</c>, <c>--dump-*</c> ...) work only when <c>TRAYLOOM_DEV=1</c> is set in the environment
    /// of the running copy. They can write files and invoke feature actions, so a stranger-installed copy must not obey them.
    /// </summary>
    private static bool DevMode => Environment.GetEnvironmentVariable("TRAYLOOM_DEV") == "1";

    /// <summary>The flags that stay available without <c>TRAYLOOM_DEV</c>: they only open Trayloom's own windows.</summary>
    private static readonly string[] AlwaysAllowed = ["--open-settings", "--open-panel", "--open-menu"];

    /// <summary>True when Trayloom runs elevated from a user-writable folder and could not restart itself without administrator rights.</summary>
    internal static bool RestrictedElevated { get; private set; }

    private static int s_crashHandled;

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
        ElevationPolicy.RestartElevatedHandler = PermissionService.RestartElevated;
        base.OnStartup(e);
        try
        {
            Start(e);
        }
        catch (Exception ex)
        {
            try
            {
                _host?.Log.Error("Trayloom could not start", ex);
            }
            catch (Exception)
            {
                // the log itself may be what failed
            }

            StartupSplash.Disarm();
            try
            {
                Dialogs.Show(null, Loc.T("Trayloom couldn’t start"), ex.Message, [Loc.T("OK")]);
            }
            catch (Exception)
            {
                // nothing more can be shown
            }

            Shutdown();
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

        // An administrator copy started by hand from a folder an unprivileged user can write to is not restarted or "fixed": it keeps
        // running, but as a restricted copy (no developer flags, no command file, never an offer to elevate). Development builds are excepted.
        if (ElevationPolicy.IsElevated && !ElevationPolicy.IsAdminOwnedInstall && !InstallManager.IsDevelopmentBuild)
        {
            RestrictedElevated = true;
        }

        // Developer flag: pretend start-up is slow (to see the "Starting Trayloom…" toast): --slow-start <milliseconds>.
        var slowIndex = DevMode ? Array.FindIndex(e.Args, a => a == "--slow-start") : -1;
        if (slowIndex >= 0 && slowIndex + 1 < e.Args.Length && int.TryParse(e.Args[slowIndex + 1], out var slowMs))
        {
            Thread.Sleep(Math.Clamp(slowMs, 0, 30_000));
        }

        var quitRequested = e.Args.Any(a => string.Equals(a, "--quit", StringComparison.OrdinalIgnoreCase));
        var relaunchIndex = Array.FindIndex(e.Args, a => string.Equals(a, "--relaunch", StringComparison.OrdinalIgnoreCase));
        if (relaunchIndex >= 0 && relaunchIndex + 1 < e.Args.Length && int.TryParse(e.Args[relaunchIndex + 1], out var oldPid))
        {
            // Started by "Restart now": wait (<= 35 s, see QuitBudget) for the previous process to exit before taking the single-instance lock.
            try
            {
                Process.GetProcessById(oldPid).WaitForExit((int)QuitBudget.OtherProcessWait.TotalMilliseconds);
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
                if (!quitRequested && e.Args.Any(a => DevFlags.Contains(a)) && (DevMode || e.Args.Where(a => DevFlags.Contains(a)).All(a => AlwaysAllowed.Contains(a))))
                {
                    // Hand the developer flags to the running instance (only the always-allowed ones unless TRAYLOOM_DEV=1).
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

        // A command file left behind by an earlier run must not be run by this one.
        try
        {
            File.Delete(DevCommandFile);
        }
        catch (Exception)
        {
            // not there, or locked: the activation handler checks again
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
            (_, _) => Dispatcher.BeginInvoke(QuitFromSignal),
            null,
            Timeout.Infinite,
            executeOnlyOnce: true);

        // Installs from before the rename: stop a still-running old instance and move its settings / data over to the Trayloom names
        // before anything reads them (see LegacyMigration / LegacyNames).
        LegacyMigration.Run();

        _host = new CoreHost(Dispatcher);
        var log = _host.Log;
        log.Info($"Trayloom {AppInfo.Version} starting");
        if (RestrictedElevated)
        {
            log.Warn("Trayloom is running as administrator from a folder that can be changed without administrator rights; developer flags are ignored and no elevation is offered");
        }

        // A previous run that was killed or crashed may have left Apple Music's window hidden, transparent or off-screen: give it back
        // (before the features start, so nothing of this run can overwrite the record first). Off the UI thread: cross-process calls.
        if (WindowGuardianState.TakeStale() is { } stale)
        {
            log.Warn($"A previous run left {stale.Windows.Count} foreign window record(s); giving the windows back");
            Task.Run(() =>
            {
                try
                {
                    var left = WindowRescue.RescueAll(stale, new PrefixedLog(log, "guardian"), heuristics: true);
                    WindowGuardianState.ReleaseStale(stale, left);
                }
                catch (Exception ex)
                {
                    log.Error("Giving the windows back failed", ex);
                }
            });
        }

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
        {
            log.Error("Unhandled exception" + (args.IsTerminating ? " (terminating)" : string.Empty), args.ExceptionObject as Exception);
            if (args.IsTerminating && Interlocked.Exchange(ref s_crashHandled, 1) == 0)
            {
                // The process is going down: put back what Trayloom changed in Windows. No UI thread is needed for this.
                try
                {
                    StartupRecovery.RunAll(RecoveryStage.Crash, log, TimeSpan.FromSeconds(3));
                    var stopper = Task.Run(() => _host?.Features.StopAll());
                    stopper.Wait(TimeSpan.FromSeconds(2));
                }
                catch (Exception)
                {
                    // nothing more can be done while crashing
                }
            }
        };
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

        // Undo anything a previous run left behind (lid action, muted microphone, pending Bluetooth ...), each hook driven by its journal.
        StartupRecovery.RunAll(RecoveryStage.Startup, log, TimeSpan.FromSeconds(3));
        _shell = new AppShell(_host) { InstallOffer = manual => OfferInstall(manual, null) };
        _host.Contributions.Discover();
        _host.Runtime.Initialize();
        MigratePanelKeys();
        _shell.Start();
        if (_host.Store.WasReset)
        {
            _host.Notifier.Show(Loc.T("Settings were reset"), Loc.T("Trayloom couldn’t read its settings file and started with defaults. A copy was kept as settings.json.bad."));
        }

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

        Dispatcher.BeginInvoke(ShutdownAfterFinish);
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
            // Give back any window a killed run left hidden, then undo every Windows change Trayloom made (lid, microphone, Bluetooth,
            // the Claude Code hook) before the program files go. Feature load errors are only logged: the uninstall must still work.
            GiveBackStaleWindows(log);
            LoadFeatureAssemblies(log, null);
            StartupRecovery.RunAll(RecoveryStage.Uninstall, log, TimeSpan.FromSeconds(10));
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
                ? Loc.T("The program files, shortcuts and your data are being removed.")
                : Loc.T("The program files and shortcuts are being removed. Your settings were kept, so a later install picks up where you left off."), [Loc.T("OK")]);
        }

        InstallManager.ScheduleDeletion(later, log);
        Shutdown();
    }

    /// <summary>
    /// <c>--quit</c> and the uninstaller ask through an event, with nobody to answer a dialog: a recording in progress is stopped and
    /// saved first (up to 15 s), then Trayloom shuts down.
    /// </summary>
    private void QuitFromSignal() => ShutdownAfterFinish();

    /// <summary>
    /// Every shutdown path goes through here (or through the shell's confirmed quit): the recorder is asked to stop and finish, within
    /// 15 s, even when nothing is recording, because that also puts open recorder editors into keep mode so their unsaved takes are
    /// not deleted. It is awaited, never blocked on, so the UI thread keeps pumping.
    /// </summary>
    internal async void ShutdownAfterFinish()
    {
        try
        {
            if (_host?.Services.Get<IRecordingState>() is { } rec)
            {
                if (rec.IsRecording)
                {
                    _host.Log.Info("Shutting down while recording: finishing the recording first");
                }

                await rec.StopAndFinishAsync(QuitBudget.FinishWait);
            }
        }
        catch (Exception ex)
        {
            _host?.Log.Error("Finishing the recording before quitting failed", ex);
        }

        Shutdown();
    }

    /// <summary>Gives back foreign windows a killed run left hidden or parked (bounded at 5 s, on a worker).</summary>
    private static void GiveBackStaleWindows(ILog log)
    {
        try
        {
            var task = Task.Run(() =>
            {
                if (WindowGuardianState.TakeStale() is { } stale)
                {
                    var left = WindowRescue.RescueAll(stale, new PrefixedLog(log, "guardian"), heuristics: true);
                    WindowGuardianState.ReleaseStale(stale, left);
                }
            });
            if (!task.Wait(TimeSpan.FromSeconds(5)))
            {
                log.Warn("Giving the windows back took too long; the guardian record was kept");
            }
        }
        catch (Exception ex)
        {
            log.Warn("Giving the windows back failed: " + ex.Message);
        }
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
                got = mutex.WaitOne(QuitBudget.OtherProcessWait); // a slow quit (recording export) can take about 31 s
            }
            catch (AbandonedMutexException)
            {
                got = true;
            }

            if (got)
            {
                // the guardian (a Trayloom.exe of its own) would keep the executable locked
                WindowGuardianClient.StopAndWait(10_000);
                mutex.ReleaseMutex();
            }
            else
            {
                log.Warn("The running Trayloom did not quit in time; closing it");
                // Only a copy that runs from the install folder: a separate development or portable copy is not ours to kill.
                foreach (var process in Process.GetProcessesByName("Trayloom").Where(p => p.Id != Environment.ProcessId && !WindowGuardianClient.IsGuardian(p.Id)))
                {
                    try
                    {
                        var image = process.MainModule?.FileName;
                        if (image is not null && SafePaths.IsUnder(image, InstallManager.InstallRoot))
                        {
                            process.Kill();
                        }
                    }
                    catch (Exception)
                    {
                        // access denied: not ours, or already gone
                    }
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
        if (!DevMode || RestrictedElevated)
        {
            args = KeepAlwaysAllowed(args);
        }

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

    /// <summary>Drops every developer flag that needs TRAYLOOM_DEV (logging each once), keeping the always-allowed ones with their value.</summary>
    private string[] KeepAlwaysAllowed(string[] args)
    {
        var kept = new List<string>();
        var logged = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (AlwaysAllowed.Contains(arg))
            {
                kept.Add(arg);
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    kept.Add(args[++i]);
                }
            }
            else if ((DevFlags.Contains(arg) || arg == "--slow-start") && logged.Add(arg))
            {
                _host?.Log.Warn($"Developer flag ignored: {arg} (set TRAYLOOM_DEV=1)");
            }
        }

        return kept.ToArray();
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
            if ((ElevationPolicy.IsElevated || RestrictedElevated) && File.Exists(DevCommandFile))
            {
                // An elevated copy never runs commands from a file a normal process can write.
                File.Delete(DevCommandFile);
                _host?.Log.Warn("Ignored a developer command file because Trayloom is running as administrator");
            }
            else if (File.Exists(DevCommandFile))
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

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // StopAndFinishAsync is a UI-thread API (it closes WPF windows), so it is called right here and never blocked on: its synchronous
        // part stops the recording and starts finalising the video before its first await. The recorder's own SystemEvents handler then
        // makes the one bounded (5 s) wait for the file work, which never needs the UI thread. If the sign-out is cancelled, the
        // recorder clears its own "finishing" state when the file work ends, so recording works again.
        try
        {
            if (_host?.Services.Get<IRecordingState>() is { } rec)
            {
                var log = _host.Log;
                rec.StopAndFinishAsync(TimeSpan.FromSeconds(4)).ContinueWith(
                    t => log.Warn("Finishing the recording at session end failed: " + t.Exception?.GetBaseException().Message),
                    CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            }
        }
        catch (Exception ex)
        {
            _host?.Log.Warn("Finishing the recording at session end failed: " + ex.Message);
        }

        base.OnSessionEnding(e);
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
            if (_shell?.DeleteDataOnExit == true && _host is not null)
            {
                // "Uninstall completely": put back every Windows change Trayloom made and remove its Claude Code hook, then delete the data.
                StartupRecovery.RunAll(RecoveryStage.Uninstall, _host.Log, TimeSpan.FromSeconds(10));
                foreach (var (folder, kind) in new[] { (AppPaths.Root, InstallManager.FolderKind.Data), (AppPaths.LocalRoot, InstallManager.FolderKind.Local) })
                {
                    if (!InstallManager.LooksLikeOurs(folder, kind))
                    {
                        _host.Log.Warn("Not deleting " + folder + ": it does not look like a Trayloom data folder");
                        continue;
                    }

                    try
                    {
                        Directory.Delete(folder, recursive: true);
                    }
                    catch (Exception ex)
                    {
                        _host.Log.Warn($"Could not delete {folder}: {ex.Message}");
                    }
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

    private static void LoadFeatureAssemblies(ILog log, ProblemReporter? problems)
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
                problems?.Add(new StartupProblem(name + ".dll", ex.Message, ex, SmartAppControl.LooksBlocked(ex)));
            }
        }
    }
}
