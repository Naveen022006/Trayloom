// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows.Threading;

namespace WinKnife.Core;

/// <summary>
/// Composition root of the shared services. The host application creates one instance on the UI thread
/// and disposes it on exit (which stops all features first).
/// </summary>
public sealed class CoreHost : IDisposable
{
    /// <summary>Creates all services; call from the UI thread.</summary>
    public CoreHost(Dispatcher ui)
    {
        Ui = ui;
        Log = new FileLog();
        Settings = new SettingsStore(AppPaths.SettingsFile, Log);
        Hooks = new InputHooks(Log);
        Hotkeys = new HotkeyService(ui, Log);
        Notifier = new Notifier(ui, Log);
        Features = new FeatureRegistry(Settings, Hooks, Hotkeys, Notifier, ui, Log);
    }

    /// <summary>The UI dispatcher.</summary>
    public Dispatcher Ui { get; }

    /// <summary>The application log.</summary>
    public ILog Log { get; }

    /// <summary>The settings database.</summary>
    public SettingsStore Settings { get; }

    /// <summary>Shared keyboard/mouse hooks.</summary>
    public InputHooks Hooks { get; }

    /// <summary>Global hotkeys.</summary>
    public HotkeyService Hotkeys { get; }

    /// <summary>Notification hub; assign <see cref="Notifier.Sink"/> to show them.</summary>
    public Notifier Notifier { get; }

    /// <summary>Feature discovery, install state and lifecycle.</summary>
    public FeatureRegistry Features { get; }

    /// <summary>Stops all features, then releases hooks, hotkeys and flushes settings.</summary>
    public void Dispose()
    {
        Features.StopAll();
        Hotkeys.Dispose();
        Hooks.Dispose();
        Settings.Dispose();
    }
}
