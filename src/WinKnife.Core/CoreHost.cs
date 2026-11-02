// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows.Threading;

namespace WinKnife.Core;

/// <summary>
/// Forwards <see cref="IShell"/> calls to the shell implemented by the host application (assigned once the UI exists).
/// Calls made before that are ignored.
/// </summary>
public sealed class ShellProxy : IShell
{
    /// <summary>The real shell (set by <c>WinKnife.App</c>).</summary>
    public IShell? Inner { get; set; }

    /// <inheritdoc />
    public bool IsPanelOpen => Inner?.IsPanelOpen ?? false;

    /// <inheritdoc />
    public void OpenSettings(string destination, string? targetFeature = null) => Inner?.OpenSettings(destination, targetFeature);

    /// <inheritdoc />
    public void OpenFeatures(string? featureId = null) => Inner?.OpenFeatures(featureId);

    /// <inheritdoc />
    public void OpenPanel(string tabId) => Inner?.OpenPanel(tabId);

    /// <inheritdoc />
    public void ClosePanel() => Inner?.ClosePanel();

    /// <inheritdoc />
    public void Quit() => Inner?.Quit();

    /// <inheritdoc />
    public void Restart() => Inner?.Restart();
}

/// <summary>
/// Composition root of the shared services. The host application creates one instance on the UI thread, attaches its
/// shell, calls <see cref="FeatureRuntime.Initialize"/> and disposes it on exit (which stops all features first).
/// </summary>
public sealed class CoreHost : IContributionServices, IDisposable
{
    /// <summary>Creates all services; call from the UI thread.</summary>
    public CoreHost(Dispatcher ui)
    {
        Ui = ui;
        Log = new FileLog();
        Store = new SettingsStore(AppPaths.SettingsFile, Log);
        var app = new AppSettings(Store);
        AppKeys.RegisterDefaults(app);
        App = app;
        Hooks = new InputHooks(Log);
        Hotkeys = new HotkeyService(ui, Log);
        Notifier = new Notifier(ui, Log);
        Shortcuts = new ShortcutService(app, Hotkeys, Log);
        ShellProxy = new ShellProxy();
        Services = new ServiceHub();
        Features = new FeatureRegistry(app, Hooks, Hotkeys, Shortcuts, Notifier, ShellProxy, null!, Services, ui, Log);
        Runtime = new FeatureRuntime(Features, app, Log, ui);
        Contributions = new ContributionRegistry(Runtime, Log);
        Features.AttachIsland(Contributions);
    }

    /// <summary>The UI dispatcher.</summary>
    public Dispatcher Ui { get; }

    /// <inheritdoc />
    public ILog Log { get; }

    /// <summary>The JSON settings database (use <see cref="App"/> in application code).</summary>
    public SettingsStore Store { get; }

    /// <inheritdoc />
    public IAppSettings App { get; }

    /// <summary>Shared keyboard/mouse hooks.</summary>
    public InputHooks Hooks { get; }

    /// <summary>Global hotkeys.</summary>
    public HotkeyService Hotkeys { get; }

    /// <summary>Notification hub; assign <see cref="Notifier.Sink"/> to show them.</summary>
    public Notifier Notifier { get; }

    /// <summary>Configurable shortcut roles.</summary>
    public ShortcutService Shortcuts { get; }

    /// <summary>Service registry shared with features.</summary>
    public ServiceHub Services { get; }

    /// <summary>The shell forwarder; set <see cref="ShellProxy.Inner"/> when the shell exists.</summary>
    public ShellProxy ShellProxy { get; }

    /// <summary>Feature classes (start/stop, actions).</summary>
    public FeatureRegistry Features { get; }

    /// <summary>Install / uninstall semantics, presets, first run.</summary>
    public FeatureRuntime Runtime { get; }

    /// <summary>Settings sections, panel sections/utilities and island contributions.</summary>
    public ContributionRegistry Contributions { get; }

    IFeatureRuntime IContributionServices.Runtime => Runtime;

    IShell IContributionServices.Shell => ShellProxy;

    IShortcutService IContributionServices.Shortcuts => Shortcuts;

    IHotkeyService IContributionServices.Hotkeys => Hotkeys;

    IInputHooks IContributionServices.Hooks => Hooks;

    INotifier IContributionServices.Notifier => Notifier;

    IActionCatalog IContributionServices.Actions => Features;

    IIslandRegistry IContributionServices.Island => Contributions;

    IServiceHub IContributionServices.Services => Services;

    /// <summary>Stops all features, then releases hooks, hotkeys and flushes settings.</summary>
    public void Dispose()
    {
        Features.StopAll();
        Hotkeys.Dispose();
        Hooks.Dispose();
        Store.Dispose();
    }
}
