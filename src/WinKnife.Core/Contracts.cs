// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Threading;

namespace WinKnife.Core;

/// <summary>Hub grouping of features. The order of the members is the display order.</summary>
public enum FeatureGroup
{
    WindowsTaskbar,
    MouseKeyboard,
    ClipboardFiles,
    Sound,
    EnergyDisplay,
    Tools,
    Island,
    Monitor,
}

/// <summary>Display helpers for <see cref="FeatureGroup"/>.</summary>
public static class FeatureGroupExtensions
{
    /// <summary>Human readable group title used by the Features hub.</summary>
    public static string DisplayName(this FeatureGroup group) => group switch
    {
        FeatureGroup.WindowsTaskbar => "Windows and taskbar",
        FeatureGroup.MouseKeyboard => "Mouse and keyboard",
        FeatureGroup.ClipboardFiles => "Clipboard and files",
        FeatureGroup.Sound => "Sound",
        FeatureGroup.EnergyDisplay => "Energy and display",
        FeatureGroup.Tools => "Tools",
        FeatureGroup.Island => "Dynamic Island",
        FeatureGroup.Monitor => "System monitor",
        _ => group.ToString(),
    };
}

/// <summary>
/// A user-installable unit of functionality. Implementations must have a public parameterless
/// constructor and are discovered by <see cref="FeatureRegistry"/> in assemblies named
/// <c>WinKnife.Features.*</c>. All members are called on the UI thread.
/// </summary>
public interface IFeature
{
    /// <summary>Stable kebab-case identifier, persisted in settings. Never rename.</summary>
    string Id { get; }

    /// <summary>Display name.</summary>
    string Name { get; }

    /// <summary>One sentence describing what the feature does.</summary>
    string Description { get; }

    /// <summary>Group in the Features hub.</summary>
    FeatureGroup Group { get; }

    /// <summary>True when the feature is installed on first run.</summary>
    bool InstalledByDefault => false;

    /// <summary>Called when the feature is installed and the app is running. Hook up everything here.</summary>
    void Start(IFeatureContext context);

    /// <summary>
    /// Called when the feature is uninstalled or the app exits. Must fully undo side effects
    /// (restore system settings, close windows, stop timers). Hook and hotkey subscriptions made through
    /// the context are also disposed automatically after this returns.
    /// </summary>
    void Stop();

    /// <summary>Creates the settings view shown in the Features hub (only while the feature is running).</summary>
    FrameworkElement? CreateSettingsView() => null;

    /// <summary>
    /// Actions shown in the quick panel and searchable from the command bar. Evaluated every time the
    /// list is shown, so titles may change with state. Only consulted while the feature is running.
    /// </summary>
    IReadOnlyList<FeatureAction> Actions => Array.Empty<FeatureAction>();
}

/// <summary>An invokable action contributed by a feature.</summary>
/// <param name="Id">Stable identifier, unique inside the feature.</param>
/// <param name="Title">Label shown on the tile / in the command bar.</param>
/// <param name="Invoke">Executed on the UI thread. Exceptions are caught and logged by the registry.</param>
/// <param name="Glyph">Optional Segoe Fluent Icons / Segoe MDL2 Assets glyph (a one-character string such as "").</param>
public sealed record FeatureAction(string Id, string Title, Action Invoke, string? Glyph = null);

/// <summary>Services handed to a feature in <see cref="IFeature.Start"/>. Scoped to that feature.</summary>
public interface IFeatureContext
{
    /// <summary>Settings scoped to this feature id.</summary>
    IFeatureSettings Settings { get; }

    /// <summary>Shared low-level keyboard/mouse hooks. Subscriptions are disposed when the feature stops.</summary>
    IInputHooks Hooks { get; }

    /// <summary>Global hotkeys. Registrations are disposed when the feature stops.</summary>
    IHotkeyService Hotkeys { get; }

    /// <summary>Tray balloon / toast notifications.</summary>
    INotifier Notifier { get; }

    /// <summary>The UI dispatcher.</summary>
    Dispatcher Ui { get; }

    /// <summary>Log whose lines are prefixed with the feature id.</summary>
    ILog Log { get; }

    /// <summary><c>%APPDATA%\WinKnife\&lt;feature-id&gt;\</c>, created on first access.</summary>
    string DataDirectory { get; }

    /// <summary>All actions of the currently running features.</summary>
    IActionCatalog Actions { get; }
}

/// <summary>Read-only view over the actions of all running features.</summary>
public interface IActionCatalog
{
    /// <summary>Snapshot of every action of every running feature (invocations are exception-safe).</summary>
    IReadOnlyList<(IFeature Feature, FeatureAction Action)> All { get; }

    /// <summary>Raised (on the UI thread) when a feature starts or stops.</summary>
    event Action? Changed;
}

/// <summary>Key/value settings persisted in <c>%APPDATA%\WinKnife\settings.json</c>.</summary>
public interface IFeatureSettings
{
    /// <summary>Returns the stored value, or <paramref name="fallback"/> when missing or not convertible.</summary>
    T Get<T>(string key, T fallback);

    /// <summary>Stores a JSON-serialisable value (saved asynchronously, debounced).</summary>
    void Set<T>(string key, T value);

    /// <summary>Raised with the key after a value changed. May be raised on any thread.</summary>
    event Action<string>? Changed;
}

/// <summary>Severity of a notification.</summary>
public enum NotificationLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>Shows tray balloons / toasts.</summary>
public interface INotifier
{
    /// <summary>Shows a notification. Safe to call from any thread.</summary>
    void Show(string title, string message, NotificationLevel level = NotificationLevel.Info);
}

/// <summary>Application log (<c>%APPDATA%\WinKnife\logs\winknife.log</c>).</summary>
public interface ILog
{
    /// <summary>Logs an informational message.</summary>
    void Info(string message);

    /// <summary>Logs a warning.</summary>
    void Warn(string message);

    /// <summary>Logs an error with an optional exception.</summary>
    void Error(string message, Exception? exception = null);
}
