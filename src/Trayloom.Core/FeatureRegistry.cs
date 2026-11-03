// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Windows.Threading;

namespace Trayloom.Core;

/// <summary>
/// Discovers <see cref="IFeature"/> classes, binds each to its catalog entry (by id, raw id or v1 id), starts/stops them with
/// exception isolation and serves as the <see cref="IActionCatalog"/>. The install state itself lives in
/// <see cref="IAppSettings"/> (<c>featureAvailable.&lt;rawId&gt;</c>); <see cref="FeatureRuntime"/> owns install/uninstall
/// semantics. All members must be used on the UI thread (calls from other threads are marshalled).
/// </summary>
public sealed class FeatureRegistry : IActionCatalog, IDisposable
{
    private const string FeatureAssemblyPrefix = "Trayloom.Features.";

    private sealed class Entry(IFeature feature, string catalogId)
    {
        public IFeature Feature { get; } = feature;

        public string CatalogId { get; } = catalogId;

        public bool Running { get; set; }

        public string? Error { get; set; }

        public ScopedHooks? Hooks { get; set; }

        public ScopedHotkeys? Hotkeys { get; set; }

        public ScopedShortcuts? Shortcuts { get; set; }
    }

    private sealed class FeatureContext(
        IFeatureSettings settings,
        IAppSettings app,
        IFeatureRuntime runtime,
        IShell shell,
        IShortcutService shortcuts,
        IInputHooks hooks,
        IHotkeyService hotkeys,
        INotifier notifier,
        Dispatcher ui,
        ILog log,
        string featureId,
        IActionCatalog actions,
        IIslandRegistry island,
        IServiceHub services) : IFeatureContext
    {
        public IFeatureSettings Settings { get; } = settings;

        public IAppSettings App { get; } = app;

        public IFeatureRuntime Runtime { get; } = runtime;

        public IShell Shell { get; } = shell;

        public IShortcutService Shortcuts { get; } = shortcuts;

        public IInputHooks Hooks { get; } = hooks;

        public IHotkeyService Hotkeys { get; } = hotkeys;

        public INotifier Notifier { get; } = notifier;

        public Dispatcher Ui { get; } = ui;

        public ILog Log { get; } = log;

        public string DataDirectory => AppPaths.FeatureData(featureId);

        public IActionCatalog Actions { get; } = actions;

        public IIslandRegistry Island { get; } = island;

        public IServiceHub Services { get; } = services;
    }

    private readonly IAppSettings _app;
    private readonly IInputHooks _hooks;
    private readonly IHotkeyService _hotkeys;
    private readonly IShortcutService _shortcuts;
    private readonly INotifier _notifier;
    private readonly IShell _shell;
    private IIslandRegistry _island;
    private readonly IServiceHub _services;
    private readonly Dispatcher _ui;
    private readonly ILog _log;
    private readonly List<Entry> _entries = [];
    private readonly Dictionary<string, Entry> _byId = new(StringComparer.Ordinal);
    private IFeatureRuntime _runtime = null!;

    /// <summary>Creates a registry; <see cref="FeatureRuntime"/> calls <see cref="Discover"/> and <see cref="StartInstalled"/>.</summary>
    public FeatureRegistry(IAppSettings app, IInputHooks hooks, IHotkeyService hotkeys, IShortcutService shortcuts, INotifier notifier, IShell shell, IIslandRegistry island, IServiceHub services, Dispatcher ui, ILog log)
    {
        _services = services;
        _island = island;
        _app = app;
        _hooks = hooks;
        _hotkeys = hotkeys;
        _shortcuts = shortcuts;
        _notifier = notifier;
        _shell = shell;
        _ui = ui;
        _log = log;
    }

    /// <summary>Raised on the UI thread after a feature was started, stopped or failed.</summary>
    public event Action<IFeature>? FeatureStateChanged;

    /// <inheritdoc />
    public event Action? Changed;

    /// <summary>
    /// Raised on the UI thread when a feature or a feature assembly could not be loaded or started (the host collects them and
    /// shows one clear dialog, including the Smart App Control explanation when that is the cause).
    /// </summary>
    public event Action<StartupProblem>? ProblemReported;

    /// <summary>All discovered feature classes in catalog order.</summary>
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

    internal void AttachRuntime(IFeatureRuntime runtime) => _runtime = runtime;

    internal void AttachIsland(IIslandRegistry island) => _island = island;

    /// <summary>
    /// Finds all non-abstract <see cref="IFeature"/> types with a public parameterless constructor in loaded
    /// <c>Trayloom.Features.*</c> assemblies. Assemblies of that name found next to the executable are loaded first.
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
                        var catalogId = feature.CatalogId;
                        if (FeatureCatalog.Get(catalogId) is null)
                        {
                            _log.Warn($"Feature {feature.Id} ({type.FullName}) is not in the catalog; ignored");
                            continue;
                        }

                        if (_byId.ContainsKey(catalogId))
                        {
                            _log.Warn($"Duplicate feature for catalog id {catalogId} in {type.FullName}; ignored");
                            continue;
                        }

                        var entry = new Entry(feature, catalogId);
                        _entries.Add(entry);
                        _byId[catalogId] = entry;
                    }
                    catch (Exception ex)
                    {
                        _log.Error($"Could not create feature {type.FullName}", ex);
                        Report(type.Name, ex);
                    }
                }
            }

            _entries.Sort((a, b) => IndexOf(a.CatalogId).CompareTo(IndexOf(b.CatalogId)));
            _log.Info($"Discovered {_entries.Count} feature(s)");
        });
    }

    /// <summary>Starts every installed feature (call once after <see cref="Discover"/>).</summary>
    public void StartInstalled() => OnUi(() =>
    {
        foreach (var entry in _entries.Where(e => IsInstalled(e.CatalogId)).ToList())
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

    /// <summary>True when the catalog feature (id, raw id or v1 id) is installed.</summary>
    public bool IsInstalled(string featureId) =>
        FeatureCatalog.TryResolve(featureId, out var entry) && _app.Get<bool>(AppKeys.Available(entry));

    /// <summary>True when the feature is currently started.</summary>
    public bool IsRunning(string featureId) => _byId.TryGetValue(FeatureCatalog.Resolve(featureId), out var e) && e.Running;

    /// <summary>The last start error of the feature, if it failed to start.</summary>
    public string? GetError(string featureId) => _byId.TryGetValue(FeatureCatalog.Resolve(featureId), out var e) ? e.Error : null;

    /// <summary>Looks up the feature class bound to a catalog entry.</summary>
    public bool TryGet(string featureId, [NotNullWhen(true)] out IFeature? feature)
    {
        feature = _byId.TryGetValue(FeatureCatalog.Resolve(featureId), out var e) ? e.Feature : null;
        return feature is not null;
    }

    /// <summary>
    /// Brings the running state of one feature in line with its install flag: starts it when installed, stops it when not.
    /// Catalog entries without a feature class are ignored. Returns false when the start failed.
    /// </summary>
    public bool Sync(string featureId)
    {
        var ok = true;
        OnUi(() =>
        {
            if (!_byId.TryGetValue(FeatureCatalog.Resolve(featureId), out var entry))
            {
                return;
            }

            if (IsInstalled(entry.CatalogId))
            {
                ok = entry.Running || StartEntry(entry);
            }
            else
            {
                StopEntry(entry);
                entry.Error = null;
                FeatureStateChanged?.Invoke(entry.Feature);
            }
        });
        return ok;
    }

    /// <inheritdoc />
    public void Dispose() => StopAll();

    private static int IndexOf(string catalogId)
    {
        for (var i = 0; i < FeatureCatalog.All.Count; i++)
        {
            if (FeatureCatalog.All[i].Id == catalogId)
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private bool StartEntry(Entry entry)
    {
        var feature = entry.Feature;
        entry.Error = null;
        entry.Hooks = new ScopedHooks(_hooks);
        entry.Hotkeys = new ScopedHotkeys(_hotkeys);
        entry.Shortcuts = new ScopedShortcuts(_shortcuts);
        var context = new FeatureContext(
            _app.Scope(feature.Id),
            _app,
            _runtime,
            _shell,
            entry.Shortcuts,
            entry.Hooks,
            entry.Hotkeys,
            _notifier,
            _ui,
            new PrefixedLog(_log, feature.Id),
            feature.Id,
            this,
            _island,
            _services);
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
            var title = FeatureCatalog.Get(entry.CatalogId)?.Title ?? feature.Id;
            if (ProblemReported is not null)
            {
                Report(title, ex);
            }
            else
            {
                _notifier.Show("Trayloom", $"{title} could not be started: {ex.Message}", NotificationLevel.Error);
            }
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
        entry.Shortcuts?.Dispose();
        entry.Hooks = null;
        entry.Hotkeys = null;
        entry.Shortcuts = null;
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
            _notifier.Show("Trayloom", $"{action.Title} failed: {ex.Message}", NotificationLevel.Error);
        }
    };

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
                Report(Path.GetFileName(file), ex);
            }
        }
    }

    private IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            _log.Error($"Some types of {assembly.GetName().Name} could not be loaded: {string.Join("; ", ex.LoaderExceptions.Where(e => e is not null).Select(e => e!.Message).Distinct())}", ex);
            Report(assembly.GetName().Name + ".dll", ex);
            return ex.Types.Where(t => t is not null)!;
        }
    }

    private void Report(string subject, Exception exception)
    {
        var blocked = SmartAppControl.LooksBlocked(exception);
        var message = exception is ReflectionTypeLoadException rtl
            ? rtl.LoaderExceptions.FirstOrDefault(e => e is not null)?.Message ?? exception.Message
            : exception.Message;
        try
        {
            ProblemReported?.Invoke(new StartupProblem(subject, message, exception, blocked));
        }
        catch (Exception ex)
        {
            _log.Warn("Problem handler failed: " + ex.Message);
        }
    }

    /// <summary>Shortcut bindings made by one feature; disposed when it stops.</summary>
    private sealed class ScopedShortcuts(IShortcutService inner) : IShortcutService, IDisposable
    {
        private readonly List<IDisposable> _bindings = [];

        public IReadOnlyList<ShortcutRole> Roles => inner.Roles;

        public event Action<string>? Changed
        {
            add => inner.Changed += value;
            remove => inner.Changed -= value;
        }

        public ShortcutRole? Role(string roleId) => inner.Role(roleId);

        public void DefineRole(ShortcutRole role) => inner.DefineRole(role);

        public Hotkey? GetHotkey(string roleId) => inner.GetHotkey(roleId);

        public ShortcutState GetState(string roleId) => inner.GetState(roleId);

        public IDisposable Bind(string roleId, Action callback)
        {
            var binding = inner.Bind(roleId, callback);
            lock (_bindings)
            {
                _bindings.Add(binding);
            }

            return binding;
        }

        public ShortcutSetResult TrySet(string roleId, Hotkey? hotkey) => inner.TrySet(roleId, hotkey);

        public void Reset(string roleId) => inner.Reset(roleId);

        public bool IsDefault(string roleId) => inner.IsDefault(roleId);

        public string? FindConflict(Hotkey hotkey, string exceptRoleId) => inner.FindConflict(hotkey, exceptRoleId);

        public void Dispose()
        {
            IDisposable[] all;
            lock (_bindings)
            {
                all = [.. _bindings];
                _bindings.Clear();
            }

            foreach (var binding in all)
            {
                binding.Dispose();
            }
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

        public IDisposable PauseOthers() => Track(inner.PauseOthers());

        public bool KeyboardHookInstalled => inner.KeyboardHookInstalled;

        public bool MouseHookInstalled => inner.MouseHookInstalled;

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
