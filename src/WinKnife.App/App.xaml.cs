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
        MigratePanelKeys();
        _shell.Start();
        if (!_host.App.Get<bool>(AppKeys.HasOnboarded) && !previousStartFailed)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => _shell.ShowOnboarding());
        }

        RunDevFlags(e.Args, startup: true);
        ScheduleMemoryTrim();
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
    /// <c>--close-panel</c>. A second launch with any of them hands them to the running instance (through <c>devcmd.txt</c>).
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
            Dispatcher.BeginInvoke(priority, () => _host!.Runtime.SetAvailable([install], true));
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

        var panelIndex = Array.FindIndex(args, a => a == "--open-panel");
        if (panelIndex >= 0)
        {
            var tab = panelIndex + 1 < args.Length && !args[panelIndex + 1].StartsWith("--", StringComparison.Ordinal) ? args[panelIndex + 1] : null;
            Dispatcher.BeginInvoke(priority, () => shell.OpenPanelForDevelopment(tab));
        }
    }

    private static readonly string[] DevFlags = ["--open-settings", "--open-panel", "--open-menu", "--search", "--dev-uninstall", "--dev-install", "--action", "--dump-info", "--close-panel"];

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
            sb.AppendLine($"{f.Id} -> {f.CatalogId} [{assembly}] running={host.Runtime.IsRunning(f.CatalogId)}");
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
            sb.AppendLine($"{e.Id} support={e.Support}");
        }

        sb.AppendLine();
        sb.AppendLine("# panel utilities");
        foreach (var u in host.Contributions.PanelUtilities())
        {
            sb.AppendLine($"{u.Id} key={PanelKeys.Utility(u.Id)} hostedOnly={u.HostedOnly} gate={string.Join(",", u.FeatureIds)}");
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
