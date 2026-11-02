// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Reflection;
using System.Windows;
using WinKnife.Core;

namespace WinKnife.Features.Tools;

/// <summary>Finds the <see cref="IPanelUtility"/> providers of the other Features projects so the Quick panel can host their views.</summary>
internal static class UtilityProviders
{
    private static List<IPanelUtility>? _cache;

    /// <summary>Every provider found in the loaded <c>WinKnife.Features.*</c> assemblies (instances are cached).</summary>
    public static IReadOnlyList<IPanelUtility> All()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        var list = new List<IPanelUtility>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var name = assembly.GetName().Name ?? string.Empty;
            if (!name.StartsWith("WinKnife.Features.", StringComparison.Ordinal) && assembly != typeof(UtilityProviders).Assembly)
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
                types = ex.Types.Where(t => t is not null).ToArray()!;
            }

            foreach (var type in types)
            {
                if (type.IsAbstract || !type.IsPublic || !typeof(IPanelUtility).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) is null)
                {
                    continue;
                }

                try
                {
                    if (Activator.CreateInstance(type) is IPanelUtility utility)
                    {
                        list.Add(utility);
                    }
                }
                catch
                {
                    // skip providers that cannot be created
                }
            }
        }

        _cache = list;
        return list;
    }

    /// <summary>Providers gated by a catalog feature id.</summary>
    public static IEnumerable<IPanelUtility> For(string featureId) => All().Where(u => u.FeatureIds.Contains(featureId));
}

/// <summary>The <see cref="IPanelContext"/> handed to hosted views inside the Quick panel.</summary>
internal sealed class QuickPanelContext : ContributionServicesBase, IPanelContext
{
    private readonly Action _close;
    private readonly Action _relayout;
    private readonly Action<string> _host;

    public QuickPanelContext(IContributionServices services, Action close, Action relayout, Action<string> host)
        : base(services)
    {
        _close = close;
        _relayout = relayout;
        _host = host;
    }

    public string TabId => PanelTabs.Utilities;

    public bool IsEditing => false;

    public bool IsActive => true;

#pragma warning disable CS0067
    public event Action? EditingChanged;

    public event Action? ResetRequested;

    public event Action? ActiveChanged;
#pragma warning restore CS0067

    public void RequestRelayout() => _relayout();

    public void ClosePanel() => _close();

    public void InvokeAfterClose(Action action, int delayMs = 150)
    {
        _close();
        Plat.After(Ui, delayMs, action);
    }

    public void SetKeepOpen(bool keepOpen)
    {
    }

    public void ShowDetail(string title, string glyph, FrameworkElement view)
    {
    }

    public void CloseDetail()
    {
    }

    public void HostUtility(string utilityId) => _host(utilityId);
}
