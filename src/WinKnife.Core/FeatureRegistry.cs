// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Windows.Threading;

namespace WinKnife.Core;

/// <summary>
/// Discovers features, tracks which are installed (persisted in settings) and starts/stops them with
/// exception isolation. Also serves as the <see cref="IActionCatalog"/>. All members must be used on the UI thread
/// (calls from other threads are marshalled).
/// </summary>
public sealed class FeatureRegistry : IActionCatalog, IDisposable
{
    private const string CoreScope = "_core";
    private const string InstalledKey = "installed";
    private const string FeatureAssemblyPrefix = "WinKnife.Features.";

    private sealed class Entry(IFeature feature)
    {
        public IFeature Feature { get; } = feature;

        public bool Running { get; set; }

        public string? Error { get; set; }

        public ScopedHooks? Hooks { get; set; }

        public ScopedHotkeys? Hotkeys { get; set; }
    }

    private sealed class FeatureContext(
        IFeatureSettings settings,
        IInputHooks hooks,
        IHotkeyService hotkeys,
        INotifier notifier,
        Dispatcher ui,
        ILog log,
        string featureId,
        IActionCatalog actions) : IFeatureContext
    {
        public IFeatureSettings Settings { get; } = settings;

        public IInputHooks Hooks { get; } = hooks;

        public IHotkeyService Hotkeys { get; } = hotkeys;

        public INotifier Notifier { get; } = notifier;

        public Dispatcher Ui { get; } = ui;

        public ILog Log { get; } = log;

        public string DataDirectory => AppPaths.FeatureData(featureId);

        public IActionCatalog Actions { get; } = actions;
    }

    private readonly SettingsStore _store;
    private readonly IInputHooks _hooks;
    private readonly IHotkeyService _hotkeys;
    private readonly INotifier _notifier;
    private readonly Dispatcher _ui;
    private readonly ILog _log;
    private readonly List<Entry> _entries = [];
    private readonly Dictionary<string, Entry> _byId = new(StringComparer.Ordinal);
    private readonly ScopedSettings _core;
    private HashSet<string> _installed = new(StringComparer.Ordinal);

    /// <summary>Creates a registry; call <see cref="Discover"/> then <see cref="StartInstalled"/>.</summary>
    public FeatureRegistry(SettingsStore store, IInputHooks hooks, IHotkeyService hotkeys, INotifier notifier, Dispatcher ui, ILog log)
    {
        _store = store;
        _hooks = hooks;
        _hotkeys = hotkeys;
        _notifier = notifier;
        _ui = ui;
        _log = log;
        _core = store.GetScope(CoreScope);
    }

    /// <summary>Raised on the UI thread after a feature was installed, uninstalled, started, stopped or failed.</summary>
    public event Action<IFeature>? FeatureStateChanged;

    /// <inheritdoc />
    public event Action? Changed;

    /// <summary>All discovered features ordered by group and name.</summary>
    public IReadOnlyList<IFeature> Features => [.. _entries.Select(e => e.Feature)];

    /// <inheritdoc />
    public IReadOnlyList<(IFeature Feature, FeatureAction Action)> All
    {
        get
        {
            var result = new List<(IFeature, FeatureAction)>();
            foreach (var entry in _entries.Where(e => e.Running))
            {
                IReadOnlyList<FeatureAction> actions;
                try
                {
                    actions = entry.Feature.Actions;
                }
                catch (Exception ex)
                {
                    _log.Error($"Reading actions of {entry.Feature.Id} failed", ex);
                    continue;
                }

                foreach (var action in actions)
                {
                    result.Add((entry.Feature, action with { Invoke = Guard(entry.Feature, action) }));
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Finds all non-abstract <see cref="IFeature"/> types with a public parameterless constructor in loaded
    /// <c>WinKnife.Features.*</c> assemblies. Assemblies of that name found next to the executable are loaded first.
    /// </summary>
    public void Discover()
    {
        OnUi(() =>
        {
            LoadFeatureAssembliesFromDisk();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var name = assembly.GetName().Name;
                if (name is null || !name.StartsWith(FeatureAssemblyPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var type in SafeGetTypes(assembly))
                {
                    if (type.IsAbstract || type.IsInterface || !typeof(IFeature).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) is null)
                    {
                        continue;
                    }

                    try
                    {
                        var feature = (IFeature)Activator.CreateInstance(type)!;
                        if (_byId.ContainsKey(feature.Id))
                        {
                            _log.Warn($"Duplicate feature id '{feature.Id}' in {type.FullName}; ignored");
                            continue;
                        }

                        var entry = new Entry(feature);
                        _entries.Add(entry);
                        _byId[feature.Id] = entry;
                    }
                    catch (Exception ex)
                    {
                        _log.Error($"Could not create feature {type.FullName}", ex);
                    }
                }
            }

            _entries.Sort((a, b) =>
            {
                var byGroup = a.Feature.Group.CompareTo(b.Feature.Group);
                return byGroup != 0 ? byGroup : string.Compare(a.Feature.Name, b.Feature.Name, StringComparison.CurrentCultureIgnoreCase);
            });

            _log.Info($"Discovered {_entries.Count} feature(s)");
            LoadInstalledSet();
        });
    }

    /// <summary>Starts every installed feature (call once after <see cref="Discover"/>).</summary>
    public void StartInstalled() => OnUi(() =>
    {
        foreach (var entry in _entries.Where(e => _installed.Contains(e.Feature.Id)).ToList())
        {
            StartEntry(entry);
        }
    });

    /// <summary>Stops every running feature without changing the installed set (used on app exit).</summary>
    public void StopAll() => OnUi(() =>
    {
        foreach (var entry in _entries.Where(e => e.Running).Reverse().ToList())
        {
            StopEntry(entry);
        }
    });

    /// <summary>True when the feature is in the installed set.</summary>
    public bool IsInstalled(string featureId) => _installed.Contains(featureId);

    /// <summary>True when the feature is currently started.</summary>
    public bool IsRunning(string featureId) => _byId.TryGetValue(featureId, out var e) && e.Running;

    /// <summary>The last start error of the feature, if it failed to start.</summary>
    public string? GetError(string featureId) => _byId.TryGetValue(featureId, out var e) ? e.Error : null;

    /// <summary>Looks up a feature by id.</summary>
    public bool TryGet(string featureId, [NotNullWhen(true)] out IFeature? feature)
    {
        feature = _byId.TryGetValue(featureId, out var e) ? e.Feature : null;
        return feature is not null;
    }

    /// <summary>Installs (starts + persists) or uninstalls (stops + persists) a feature. Returns false if it failed to start.</summary>
    public bool SetInstalled(string featureId, bool installed)
    {
        var ok = true;
        OnUi(() =>
        {
            if (!_byId.TryGetValue(featureId, out var entry))
            {
                ok = false;
                return;
            }

            if (installed)
            {
                _installed.Add(featureId);
                PersistInstalled();
                ok = entry.Running || StartEntry(entry);
            }
            else
            {
                _installed.Remove(featureId);
                PersistInstalled();
                StopEntry(entry);
                entry.Error = null;
                FeatureStateChanged?.Invoke(entry.Feature);
            }
        });
        return ok;
    }

    /// <inheritdoc />
    public void Dispose() => StopAll();

    private bool StartEntry(Entry entry)
    {
        var feature = entry.Feature;
        entry.Error = null;
        entry.Hooks = new ScopedHooks(_hooks);
        entry.Hotkeys = new ScopedHotkeys(_hotkeys);
        var context = new FeatureContext(
            _store.GetScope(feature.Id),
            entry.Hooks,
            entry.Hotkeys,
            _notifier,
            _ui,
            new PrefixedLog(_log, feature.Id),
            feature.Id,
            this);
        try
        {
            feature.Start(context);
            entry.Running = true;
            _log.Info($"Started feature {feature.Id}");
        }
        catch (Exception ex)
        {
            _log.Error($"Feature {feature.Id} failed to start", ex);
            entry.Error = ex.Message;
            TryStop(feature);
            ReleaseScopes(entry);
            _notifier.Show("WinKnife", $"{feature.Name} could not be started: {ex.Message}", NotificationLevel.Error);
        }

        FeatureStateChanged?.Invoke(feature);
        Changed?.Invoke();
        return entry.Running;
    }

    private void StopEntry(Entry entry)
    {
        if (!entry.Running)
        {
            return;
        }

        entry.Running = false;
        TryStop(entry.Feature);
        ReleaseScopes(entry);
        _log.Info($"Stopped feature {entry.Feature.Id}");
        FeatureStateChanged?.Invoke(entry.Feature);
        Changed?.Invoke();
    }

    private void TryStop(IFeature feature)
    {
        try
        {
            feature.Stop();
        }
        catch (Exception ex)
        {
            _log.Error($"Feature {feature.Id} threw in Stop()", ex);
        }
    }

    private static void ReleaseScopes(Entry entry)
    {
        entry.Hooks?.Dispose();
        entry.Hotkeys?.Dispose();
        entry.Hooks = null;
        entry.Hotkeys = null;
    }

    private Action Guard(IFeature feature, FeatureAction action) => () =>
    {
        try
        {
            action.Invoke();
        }
        catch (Exception ex)
        {
            _log.Error($"Action {feature.Id}/{action.Id} failed", ex);
            _notifier.Show("WinKnife", $"{action.Title} failed: {ex.Message}", NotificationLevel.Error);
        }
    };

    private void LoadInstalledSet()
    {
        if (_core.Contains(InstalledKey))
        {
            _installed = new HashSet<string>(_core.Get(InstalledKey, new List<string>()), StringComparer.Ordinal);
        }
        else
        {
            _installed = new HashSet<string>(_entries.Where(e => e.Feature.InstalledByDefault).Select(e => e.Feature.Id), StringComparer.Ordinal);
            PersistInstalled();
        }
    }

    private void PersistInstalled() => _core.Set(InstalledKey, _installed.Order(StringComparer.Ordinal).ToList());

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

    private void LoadFeatureAssembliesFromDisk()
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetName().Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, FeatureAssemblyPrefix + "*.dll"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (loaded.Contains(name))
            {
                continue;
            }

            try
            {
                Assembly.LoadFrom(file);
            }
            catch (Exception ex)
            {
                _log.Error($"Could not load {file}", ex);
            }
        }
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }

    /// <summary>Hook subscriptions made by one feature; disposed when it stops.</summary>
    private sealed class ScopedHooks(IInputHooks inner) : IInputHooks, IDisposable
    {
        private readonly List<IDisposable> _subs = [];

        public IDisposable SubscribeKeyboard(Func<KeyboardHookEvent, bool> handler, int priority = 0) =>
            Track(inner.SubscribeKeyboard(handler, priority));

        public IDisposable SubscribeMouse(Func<MouseHookEvent, bool> handler, int priority = 0) =>
            Track(inner.SubscribeMouse(handler, priority));

        public void Dispose()
        {
            IDisposable[] subs;
            lock (_subs)
            {
                subs = [.. _subs];
                _subs.Clear();
            }

            foreach (var sub in subs)
            {
                sub.Dispose();
            }
        }

        private IDisposable Track(IDisposable sub)
        {
            var tracked = new Tracked(sub, this);
            lock (_subs)
            {
                _subs.Add(sub);
            }

            return tracked;
        }

        private void Forget(IDisposable sub)
        {
            lock (_subs)
            {
                _subs.Remove(sub);
            }
        }

        private sealed class Tracked(IDisposable inner, ScopedHooks owner) : IDisposable
        {
            public void Dispose()
            {
                owner.Forget(inner);
                inner.Dispose();
            }
        }
    }

    /// <summary>Hotkey registrations made by one feature; disposed when it stops.</summary>
    private sealed class ScopedHotkeys(IHotkeyService inner) : IHotkeyService, IDisposable
    {
        private readonly List<IDisposable> _regs = [];

        public IDisposable Register(Hotkey hotkey, Action callback) => Track(inner.Register(hotkey, callback));

        public bool TryRegister(Hotkey hotkey, Action callback, [NotNullWhen(true)] out IDisposable? registration)
        {
            if (inner.TryRegister(hotkey, callback, out var reg))
            {
                registration = Track(reg);
                return true;
            }

            registration = null;
            return false;
        }

        public void Dispose()
        {
            IDisposable[] regs;
            lock (_regs)
            {
                regs = [.. _regs];
                _regs.Clear();
            }

            foreach (var reg in regs)
            {
                reg.Dispose();
            }
        }

        private IDisposable Track(IDisposable reg)
        {
            lock (_regs)
            {
                _regs.Add(reg);
            }

            return new Tracked(reg, this);
        }

        private void Forget(IDisposable reg)
        {
            lock (_regs)
            {
                _regs.Remove(reg);
            }
        }

        private sealed class Tracked(IDisposable inner, ScopedHotkeys owner) : IDisposable
        {
            public void Dispose()
            {
                owner.Forget(inner);
                inner.Dispose();
            }
        }
    }
}
