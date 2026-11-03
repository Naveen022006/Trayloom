// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows.Threading;

namespace WinKnife.Core;

/// <summary>
/// Install / uninstall semantics (spec 02-settings section 5): availability is a layer above each feature's own enable keys,
/// stored as <c>featureAvailable.&lt;rawId&gt;</c>. Uninstalled features disappear from Settings, panel, tray and Command Bar
/// and are torn down on the spot; their settings are never deleted. First installs switch on the primary control
/// when none of its enable keys was ever saved.
/// </summary>
public sealed class FeatureRuntime : IFeatureRuntime
{
    private readonly FeatureRegistry _registry;
    private readonly IAppSettings _settings;
    private readonly ILog _log;
    private readonly Dispatcher _ui;
    private readonly HashSet<string> _loadedThisSession = new(StringComparer.Ordinal);
    private HashSet<string> _installedAtLaunch = new(StringComparer.Ordinal);
    private bool _initialized;

    /// <summary>Ids that count for the "Never turned on" offer (spec 3.4).</summary>
    private static readonly string[] OfferedWhenNeverSwitchedOn =
    [
        "dock-preview", "dock-click", "window-maximizer", "auto-quit", "scroll-inverter", "linear-scroll", "focus-follows-mouse",
        "mouse-acceleration", "mouse-navigation", "mouse-button-shortcuts", "middle-click", "keyboard-debounce", "mouse-click-debounce",
        "super-key", "finder-cut-paste",
    ];

    /// <summary>Creates the runtime over a registry and the settings.</summary>
    public FeatureRuntime(FeatureRegistry registry, IAppSettings settings, ILog log, Dispatcher ui)
    {
        _registry = registry;
        _settings = settings;
        _log = log;
        _ui = ui;
        registry.AttachRuntime(this);
    }

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public int Revision { get; private set; }

    /// <summary>The feature registry (feature classes, actions).</summary>
    public FeatureRegistry Registry => _registry;

    /// <summary>
    /// True when a feature that loaded in this process was uninstalled afterwards: the hub shows the restart card
    /// ("Features uninstalled in this session stay loaded until the app restarts").
    /// </summary>
    public bool NeedsRestartToUnload => _loadedThisSession.Any(id => !IsAvailable(id));

    /// <summary>Number of installed features.</summary>
    public int AvailableCount => FeatureCatalog.All.Count(e => IsAvailable(e.Id));

    /// <summary>Number of features this machine can run or already has (the denominator of "N of M features installed").</summary>
    public int InstallableCount => FeatureCatalog.All.Count(e => e.CanInstall || IsAvailable(e.Id));

    /// <summary>True when the start-up of a clean install still needs to choose its features (<c>hasOnboarded</c> false and no step saved).</summary>
    public bool IsCleanInstall => !_settings.Get<bool>(AppKeys.HasOnboarded) && _settings.Get<int>(AppKeys.OnboardingStep) == 0;

    /// <inheritdoc />
    public bool IsAvailable(string featureId) =>
        FeatureCatalog.TryResolve(featureId, out var entry) && _settings.Get<bool>(AppKeys.Available(entry));

    /// <inheritdoc />
    public bool IsRunning(string featureId) => _registry.IsRunning(featureId);

    /// <inheritdoc />
    public bool AnyAvailable(IEnumerable<string> featureIds)
    {
        var any = false;
        foreach (var id in featureIds)
        {
            any = true;
            if (IsAvailable(id))
            {
                return true;
            }
        }

        return !any;
    }

    /// <inheritdoc />
    public string? InstallBlockedReason(string featureId) =>
        FeatureCatalog.TryResolve(featureId, out var entry) && !IsAvailable(entry.Id) && !entry.CanInstall
            ? Loc.T(entry.UnavailableReason ?? "Not available on Windows.")
            : null;

    /// <summary>
    /// Runs the one-time migration of v1 settings, prepares the first-run selection (Essentials only), discovers feature
    /// classes and starts the installed ones. Call once after the shell is attached.
    /// </summary>
    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        MigrateV1();
        PrepareFirstRunAvailability();
        _registry.Discover();
        _registry.StartInstalled();
        foreach (var entry in FeatureCatalog.All.Where(e => IsAvailable(e.Id)))
        {
            _loadedThisSession.Add(entry.Id);
        }

        _installedAtLaunch = FeatureCatalog.All.Where(e => IsAvailable(e.Id)).Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Installs or uninstalls features (spec 5.1 <c>setAvailable</c>). One <see cref="Changed"/> at the end.</summary>
    /// <param name="featureIds">Catalog ids.</param>
    /// <param name="available">Target state.</param>
    /// <param name="enablingFirstInstalls">On first install, switch the primary control on (never when none of its keys was ever saved is false).</param>
    public void SetAvailable(IEnumerable<string> featureIds, bool available, bool enablingFirstInstalls = true)
    {
        OnUi(() =>
        {
            var changed = ApplyAvailability(featureIds, available, enablingFirstInstalls);
            if (changed)
            {
                Bump();
            }
        });
    }

    /// <summary>Presets / first-run picker: switch the enable keys on, flip every feature to membership of <paramref name="selected"/>.</summary>
    public void ReplaceAvailable(IEnumerable<string> selected, IEnumerable<string> enableKeys)
    {
        OnUi(() =>
        {
            foreach (var key in enableKeys)
            {
                _settings.Set(key, true);
            }

            var set = selected.Select(FeatureCatalog.Resolve).ToHashSet(StringComparer.Ordinal);
            var changed = ApplyAvailability(FeatureCatalog.All.Where(e => set.Contains(e.Id)).Select(e => e.Id), true, false);
            changed |= ApplyAvailability(FeatureCatalog.All.Where(e => !set.Contains(e.Id)).Select(e => e.Id), false, false);
            Bump();
            _ = changed;
        });
    }

    /// <summary>Applies one of the <see cref="FeatureCatalog.Presets"/>.</summary>
    public void ApplyPreset(FeaturePreset preset) => ReplaceAvailable(preset.Features, preset.EnableKeys);

    /// <summary>Installs (or uninstalls) every installable feature without touching enable keys.</summary>
    public void SetAllAvailable(bool available) =>
        SetAvailable(FeatureCatalog.All.Where(e => available ? e.CanInstall : IsAvailable(e.Id)).Select(e => e.Id), available, false);

    /// <summary>Installed Dynamic Island extension ids (for the "Uninstall Dynamic Island" confirmation).</summary>
    public IReadOnlyList<FeatureCatalogEntry> InstalledIslandExtensions() => [.. FeatureCatalog.IslandExtensions.Where(e => IsAvailable(e.Id))];

    // ------------------------------------------------------------------------------------------ never turned on

    /// <summary>Features that qualify for the "Never turned on" card right now (>= 3 are shown).</summary>
    public IReadOnlyList<FeatureCatalogEntry> NeverTurnedOnCandidates()
    {
        var kept = KeptFeatures();
        var list = new List<FeatureCatalogEntry>();
        foreach (var id in OfferedWhenNeverSwitchedOn)
        {
            if (!FeatureCatalog.TryResolve(id, out var entry) || !_installedAtLaunch.Contains(entry.Id) || !IsAvailable(entry.Id) || kept.Contains(entry.RawId))
            {
                continue;
            }

            if (entry.EnabledKeys.Count == 0 || entry.EnabledKeys.Any(k => _settings.IsSet(k) || _settings.Get<bool>(k)))
            {
                continue;
            }

            list.Add(entry);
        }

        return list;
    }

    /// <summary>Marks features as kept (never offered again).</summary>
    public void KeepFeatures(IEnumerable<FeatureCatalogEntry> entries)
    {
        var kept = KeptFeatures();
        foreach (var entry in entries)
        {
            kept.Add(entry.RawId);
        }

        _settings.Set(AppKeys.FeatureHubKeptFeatures, string.Join(',', kept.Order(StringComparer.Ordinal)));
    }

    /// <summary>Removes features from the kept list.</summary>
    public void UnkeepFeatures(IEnumerable<FeatureCatalogEntry> entries)
    {
        var kept = KeptFeatures();
        foreach (var entry in entries)
        {
            kept.Remove(entry.RawId);
        }

        _settings.Set(AppKeys.FeatureHubKeptFeatures, string.Join(',', kept.Order(StringComparer.Ordinal)));
    }

    private HashSet<string> KeptFeatures() =>
        _settings.Get<string>(AppKeys.FeatureHubKeptFeatures).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);

    // ------------------------------------------------------------------------------------------ internals

    private bool ApplyAvailability(IEnumerable<string> featureIds, bool target, bool enablingFirstInstalls)
    {
        var changed = false;
        var islandInstalledNow = false;
        foreach (var id in featureIds.Select(FeatureCatalog.Resolve).Distinct())
        {
            var entry = FeatureCatalog.Get(id);
            if (entry is null || IsAvailable(id) == target || (target && !entry.CanInstall))
            {
                continue;
            }

            // The Dynamic Island is always switched on by its first install (also through "Install all", bundles and onboarding):
            // otherwise a user who just installed it would see nothing.
            if (target && (enablingFirstInstalls || entry.Id == "notch"))
            {
                EnableOnFirstInstall(entry);
            }

            _settings.Set(AppKeys.Available(entry), target);
            _registry.Sync(id);
            if (target)
            {
                _loadedThisSession.Add(id);
                if (id == "notch")
                {
                    islandInstalledNow = true;
                }
            }

            changed = true;
        }

        if (islandInstalledNow && !_settings.Get<bool>(AppKeys.NotchInitialExtensionsInstalled))
        {
            _settings.Set(AppKeys.NotchInitialExtensionsInstalled, true);
            ApplyAvailability(FeatureCatalog.IslandInitialExtensions, true, enablingFirstInstalls);
        }

        return changed;
    }

    private void EnableOnFirstInstall(FeatureCatalogEntry entry)
    {
        if (entry.InitialEnableKeys.Count == 0 || entry.EnabledKeys.Any(_settings.IsSet))
        {
            return;
        }

        foreach (var key in entry.InitialEnableKeys)
        {
            _settings.Set(key, true);
        }
    }

    private void Bump()
    {
        Revision++;
        _log.Info($"Feature availability changed (revision {Revision}, {AvailableCount} installed)");
        Changed?.Invoke();
    }

    /// <summary>v1 stored the installed list under <c>_core/installed</c> (v1 feature ids). Convert once to <c>featureAvailable.*</c>.</summary>
    private void MigrateV1()
    {
        if (_settings.Get<int>(AppKeys.SettingsVersion) >= 2)
        {
            return;
        }

        var core = _settings.Scope("_core");
        var legacy = core.Get<List<string>>("installed", null!);
        if (legacy is not null)
        {
            var installed = legacy.Select(FeatureCatalog.Resolve).ToHashSet(StringComparer.Ordinal);
            foreach (var entry in FeatureCatalog.All)
            {
                var on = installed.Contains(entry.Id) && entry.CanInstall;
                _settings.Set(AppKeys.Available(entry), on);
                if (on && !entry.EnabledKeys.Any(_settings.IsSet))
                {
                    foreach (var key in entry.InitialEnableKeys)
                    {
                        _settings.Set(key, true);
                    }
                }
            }

            _settings.Set(AppKeys.HasOnboarded, true);
            _settings.Set(AppKeys.NotchInitialExtensionsInstalled, true);
            _log.Info($"Migrated {legacy.Count} installed v1 feature(s) to the catalog");
        }

        _settings.Set(AppKeys.SettingsVersion, 2);
    }

    /// <summary>A clean install starts with the Essentials bundle only (spec 5.4).</summary>
    private void PrepareFirstRunAvailability()
    {
        if (!IsCleanInstall || FeatureCatalog.All.Any(e => _settings.IsSet(AppKeys.Available(e))))
        {
            return;
        }

        var essentials = FeatureCatalog.Presets[0].Features.ToHashSet(StringComparer.Ordinal);
        foreach (var entry in FeatureCatalog.All)
        {
            _settings.Set(AppKeys.Available(entry), essentials.Contains(entry.Id));
        }

        _log.Info("First run: Essentials bundle installed");
    }

    private void OnUi(Action action)
    {
        if (_ui.CheckAccess())
        {
            action();
        }
        else
        {
            _ui.Invoke(action);
        }
    }
}
