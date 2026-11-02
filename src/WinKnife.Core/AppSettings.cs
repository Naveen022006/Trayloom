// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;
using System.Text.Json.Nodes;

namespace WinKnife.Core;

/// <summary>
/// The flat, app-wide key/value store (Vorssaint's <c>UserDefaults</c> key names), persisted in
/// <c>%APPDATA%\WinKnife\settings.json</c> (scope <c>_app</c>). Pages that span several features use these keys;
/// the old per-feature scoped settings stay readable through <see cref="Scope"/>.
/// </summary>
/// <remarks>
/// Values are JSON-serialised: bool, int, double, string, <see cref="Hotkey"/>, enums (as strings), lists, dictionaries.
/// <see cref="Changed"/> may be raised on any thread.
/// </remarks>
public interface IAppSettings
{
    /// <summary>
    /// The stored value; when nothing was saved, the registered default (see <see cref="Register{T}"/>);
    /// when there is none, <c>default(T)</c>.
    /// </summary>
    T Get<T>(string key);

    /// <summary>Like <see cref="Get{T}(string)"/> but uses <paramref name="fallback"/> when neither a value nor a registered default exists.</summary>
    T Get<T>(string key, T fallback);

    /// <summary>Stores a value (saved asynchronously, debounced) and raises <see cref="Changed"/> when it differs.</summary>
    void Set<T>(string key, T value);

    /// <summary>True when the key was ever saved (registered defaults do not count). Used by "never turned on" and first-install logic.</summary>
    bool IsSet(string key);

    /// <summary>Removes the saved value so the registered default applies again.</summary>
    void Reset(string key);

    /// <summary>Registers the default returned when <paramref name="key"/> was never saved. Safe to call repeatedly (last call wins).</summary>
    void Register<T>(string key, T defaultValue);

    /// <summary>Registers many defaults at once.</summary>
    void RegisterDefaults(IEnumerable<KeyValuePair<string, object?>> defaults);

    /// <summary>The old per-feature scoped settings of <paramref name="featureId"/> (a v1 feature id or any scope name).</summary>
    IFeatureSettings Scope(string featureId);

    /// <summary>Raised with the key after a value was saved or reset (any thread).</summary>
    event Action<string>? Changed;
}

/// <summary>Default <see cref="IAppSettings"/> on top of <see cref="SettingsStore"/>.</summary>
public sealed class AppSettings : IAppSettings
{
    /// <summary>Name of the store scope that holds the flat keys.</summary>
    public const string ScopeName = "_app";

    private readonly SettingsStore _store;
    private readonly ScopedSettings _scope;
    private readonly Dictionary<string, object?> _defaults = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>Creates the settings view over <paramref name="store"/>.</summary>
    public AppSettings(SettingsStore store)
    {
        _store = store;
        _scope = store.GetScope(ScopeName);
        _scope.Changed += key => Changed?.Invoke(key);
    }

    /// <inheritdoc />
    public event Action<string>? Changed;

    /// <inheritdoc />
    public T Get<T>(string key) => Get(key, default(T)!);

    /// <inheritdoc />
    public T Get<T>(string key, T fallback)
    {
        if (_scope.Contains(key))
        {
            return _scope.Get(key, TryDefault(key, out T? d) ? d! : fallback);
        }

        return TryDefault(key, out T? registered) ? registered! : fallback;
    }

    /// <inheritdoc />
    public void Set<T>(string key, T value) => _scope.Set(key, value);

    /// <inheritdoc />
    public bool IsSet(string key) => _scope.Contains(key);

    /// <inheritdoc />
    public void Reset(string key) => _scope.Remove(key);

    /// <inheritdoc />
    public void Register<T>(string key, T defaultValue)
    {
        lock (_gate)
        {
            _defaults[key] = defaultValue;
        }
    }

    /// <inheritdoc />
    public void RegisterDefaults(IEnumerable<KeyValuePair<string, object?>> defaults)
    {
        lock (_gate)
        {
            foreach (var (key, value) in defaults)
            {
                _defaults[key] = value;
            }
        }
    }

    /// <inheritdoc />
    public IFeatureSettings Scope(string featureId) => _store.GetScope(featureId);

    /// <summary>The underlying store (for backup / migration).</summary>
    internal SettingsStore Store => _store;

    private bool TryDefault<T>(string key, out T? value)
    {
        object? raw;
        lock (_gate)
        {
            if (!_defaults.TryGetValue(key, out raw))
            {
                value = default;
                return false;
            }
        }

        if (raw is T typed)
        {
            value = typed;
            return true;
        }

        try
        {
            var node = JsonSerializer.SerializeToNode(raw, SettingsStore.JsonOptions);
            value = node.Deserialize<T>(SettingsStore.JsonOptions);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
        {
            value = default;
            return false;
        }
    }
}

/// <summary>Setting keys and registered defaults that belong to the shell (spec 02-settings sec. 10, 01-menu-panel sec. 3.4, 02a).</summary>
public static class AppKeys
{
    public const string SettingsVersion = "settingsVersion";
    public const string HasOnboarded = "hasOnboarded";
    public const string OnboardingStep = "onboardingStep";
    public const string FeaturesOnboardingVersion = "featuresOnboardingVersion";
    public const string LastUpdateIntroVersion = "lastUpdateIntroVersion";
    public const string StartupDidNotFinish = "startupDidNotFinish";
    public const string SettingsWindowWidth = "settingsWindowWidth";
    public const string SettingsWindowHeight = "settingsWindowHeight";
    public const string AppLanguage = "appLanguage";
    public const string AppAppearance = "appAppearance";
    public const string HotkeyEnabled = "hotkeyEnabled";
    public const string AutoCheckUpdates = "autoCheckUpdates";
    public const string IncludeBetaUpdates = "includeBetaUpdates";
    public const string ShowReleaseNotesBeforeUpdate = "showReleaseNotesBeforeUpdate";
    public const string ReleaseNotesOnUpdate = "releaseNotesOnUpdate";
    public const string MenuBarIconSymbol = "menuBarIconSymbol";
    public const string KeepAwakeRightClickToggle = "keepAwakeRightClickToggle";
    public const string LaunchAtLoginWanted = "launchAtLoginWanted";
    public const string FeatureHubKeptFeatures = "featureHubKeptFeatures";
    public const string NotchInitialExtensionsInstalled = "notchInitialExtensionsInstalled";
    public const string LiquidGlassEnabled = "liquidGlassEnabled";
    public const string NotchLiquidGlassEnabled = "notchLiquidGlassEnabled";
    public const string PanelSectionOrder = "panelSectionOrder";
    public const string PanelCollapsedSections = "panelCollapsedSections";
    public const string BrightnessControlEnabled = "brightnessControlEnabled";
    public const string FeaturesTabSelected = "featuresPageTab";
    public const string TrayTipShown = "trayOverflowTipShown";

    /// <summary>Destination (<c>page#anchor</c>) of the Settings page that was open last; reopened by "Settings".</summary>
    public const string SettingsLastPage = "settingsLastPage";

    /// <summary>The one-time "WinKnife is running - click the icon by the clock" toast was shown.</summary>
    public const string FirstRunToastShown = "firstRunToastShown";

    /// <summary>The user chose "Don't ask again" on the install offer.</summary>
    public const string InstallPromptDismissed = "installPromptDismissed";

    /// <summary>The user hid the "Getting started" card on the General page.</summary>
    public const string GettingStartedDismissed = "gettingStartedDismissed";

    /// <summary>The key that stores install state for a catalog entry: <c>featureAvailable.&lt;RawId&gt;</c>.</summary>
    public static string Available(FeatureCatalogEntry entry) => "featureAvailable." + entry.RawId;

    /// <summary>Visibility key of a panel tab (monitoring tabs reuse <c>monitorShow*</c>).</summary>
    public static string PanelShow(string tabId) => tabId switch
    {
        PanelTabs.KeepAwake => "panelShowKeepAwake",
        PanelTabs.Brightness => "panelShowBrightness",
        PanelTabs.Mixer => "monitorShowMixer",
        PanelTabs.System => "monitorShowSystem",
        PanelTabs.Network => "monitorShowNetwork",
        PanelTabs.Disk => "monitorShowDisk",
        PanelTabs.Power => "monitorShowPower",
        PanelTabs.Utilities => "panelShowUtilities",
        PanelTabs.Controls => "panelShowControls",
        PanelTabs.Toggles => "panelShowToggles",
        PanelTabs.Wallpaper => "panelShowWallpaper",
        _ => "panelShow" + tabId,
    };

    /// <summary>Registers every default of the shell and the catalog's install defaults.</summary>
    public static void RegisterDefaults(IAppSettings settings)
    {
        var d = new Dictionary<string, object?>
        {
            [SettingsVersion] = 0,
            [HasOnboarded] = false,
            [OnboardingStep] = 0,
            [FeaturesOnboardingVersion] = string.Empty,
            [LastUpdateIntroVersion] = string.Empty,
            [StartupDidNotFinish] = false,
            [SettingsWindowWidth] = 0.0,
            [SettingsWindowHeight] = 0.0,
            [AppLanguage] = Loc.SystemDefault,
            [AppAppearance] = "system",
            [HotkeyEnabled] = true,
            [AutoCheckUpdates] = true,
            [IncludeBetaUpdates] = false,
            [ShowReleaseNotesBeforeUpdate] = true,
            [ReleaseNotesOnUpdate] = true,
            [MenuBarIconSymbol] = string.Empty,
            [KeepAwakeRightClickToggle] = false,
            [LaunchAtLoginWanted] = false,
            [FeatureHubKeptFeatures] = string.Empty,
            [NotchInitialExtensionsInstalled] = false,
            [LiquidGlassEnabled] = false,
            [NotchLiquidGlassEnabled] = false,
            [PanelSectionOrder] = string.Empty,
            [PanelCollapsedSections] = string.Empty,
            [PanelKeys.UtilityOrder] = string.Empty,
            [PanelKeys.ControlOrder] = string.Empty,
            [BrightnessControlEnabled] = false,
            [FeaturesTabSelected] = 0,
            [TrayTipShown] = false,
            [SettingsLastPage] = string.Empty,
            [FirstRunToastShown] = false,
            [InstallPromptDismissed] = false,
            [GettingStartedDismissed] = false,
            ["panelShowKeepAwake"] = true,
            ["panelShowBrightness"] = true,
            ["panelShowUtilities"] = true,
            ["panelShowControls"] = true,
            ["panelShowToggles"] = true,
            ["panelShowWallpaper"] = true,
            ["monitorShowMixer"] = true,
            ["monitorShowSystem"] = true,
            ["monitorShowNetwork"] = true,
            ["monitorShowDisk"] = true,
            ["monitorShowPower"] = true,
        };
        foreach (var entry in FeatureCatalog.All)
        {
            d[Available(entry)] = entry.DefaultInstalled && entry.CanInstall;
        }

        settings.RegisterDefaults(d);
    }
}
