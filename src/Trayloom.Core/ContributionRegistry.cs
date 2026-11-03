// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Reflection;

namespace Trayloom.Core;

/// <summary>
/// Finds the contribution providers (<see cref="ISettingsSection"/>, <see cref="IPanelSection"/>, <see cref="IPanelUtility"/>,
/// <see cref="IIslandSection"/>, <see cref="IIslandStrip"/>) in the loaded <c>Trayloom.Features.*</c> assemblies and serves them
/// filtered by install state. A provider is shown while any of its gating features is installed (no gate = always).
/// Providers are created once and must be stateless.
/// </summary>
public sealed class ContributionRegistry : IIslandRegistry
{
    private const string FeatureAssemblyPrefix = "Trayloom.Features.";

    private readonly IFeatureRuntime _runtime;
    private readonly ILog _log;
    private readonly List<object> _providers = [];
    private readonly object _gate = new();

    /// <summary>Creates the registry; call <see cref="Discover"/> after the feature assemblies are loaded.</summary>
    public ContributionRegistry(IFeatureRuntime runtime, ILog log)
    {
        _runtime = runtime;
        _log = log;
        runtime.Changed += () => Changed?.Invoke();
    }

    /// <summary>Raised when install state changed or a provider was added/removed (UI thread).</summary>
    public event Action? Changed;

    /// <inheritdoc />
    public IReadOnlyList<IIslandSection> Sections => Visible<IIslandSection>();

    /// <inheritdoc />
    public IReadOnlyList<IIslandStrip> Strips => Visible<IIslandStrip>();

    /// <summary>Reflection scan of the loaded feature assemblies (parameterless-ctor, non-abstract classes).</summary>
    public void Discover()
    {
        var count = 0;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var name = assembly.GetName().Name;
            if (name is null || !name.StartsWith(FeatureAssemblyPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = [.. ex.Types.Where(t => t is not null)!];
            }

            foreach (var type in types)
            {
                if (type.IsAbstract || type.IsInterface || !typeof(IContributionProvider).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) is null)
                {
                    continue;
                }

                try
                {
                    AddProviderCore(Activator.CreateInstance(type)!);
                    count++;
                }
                catch (Exception ex)
                {
                    _log.Error($"Could not create contribution {type.FullName}", ex);
                }
            }
        }

        _log.Info($"Discovered {count} contribution provider(s)");
        Changed?.Invoke();
    }

    /// <summary>Adds a provider at run time (the App registers its temporary adapters this way).</summary>
    public void AddProvider(IContributionProvider provider)
    {
        AddProviderCore(provider);
        Changed?.Invoke();
    }

    /// <summary>Removes a provider added with <see cref="AddProvider"/>.</summary>
    public void RemoveProvider(IContributionProvider provider)
    {
        lock (_gate)
        {
            _providers.Remove(provider);
        }

        Changed?.Invoke();
    }

    /// <summary>Settings sections of installed features (all pages when <paramref name="pageId"/> is null), ordered.</summary>
    public IReadOnlyList<ISettingsSection> SettingsSections(string? pageId = null) =>
        [.. Visible<ISettingsSection>().Where(s => pageId is null || s.PageId == pageId)];

    /// <summary>Every settings section regardless of install state (the Settings window filters itself when it needs the dormant ones).</summary>
    public IReadOnlyList<ISettingsSection> AllSettingsSections() => All<ISettingsSection>();

    /// <summary>Panel sections of one tab, ordered.</summary>
    public IReadOnlyList<IPanelSection> PanelSections(string tabId) =>
        [.. Visible<IPanelSection>().Where(s => s.TabId == tabId)];

    /// <summary>Utilities-tab rows of installed features, ordered.</summary>
    public IReadOnlyList<IPanelUtility> PanelUtilities() => Visible<IPanelUtility>();

    private void AddProviderCore(object provider)
    {
        lock (_gate)
        {
            if (!_providers.Contains(provider))
            {
                _providers.Add(provider);
            }
        }
    }

    private List<T> All<T>() where T : class
    {
        lock (_gate)
        {
            return [.. _providers.OfType<T>().OrderBy(p => ((IContributionProvider)p).Order)];
        }
    }

    private List<T> Visible<T>() where T : class, IContributionProvider =>
        [.. All<T>().Where(p => _runtime.AnyAvailable(p.FeatureIds))];
}
