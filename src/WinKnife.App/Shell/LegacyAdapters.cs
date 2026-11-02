// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.App;

/// <summary>
/// TEMPORARY ADAPTER. Until a feature project replaces its v1 feature with real <see cref="ISettingsSection"/> contributions, its
/// <c>IFeature.CreateSettingsView()</c> is shown at the feature's catalog destination (<c>page#anchor</c>). A feature that stops
/// returning a view from <c>CreateSettingsView()</c> (because it contributes its own sections) disappears from here automatically.
/// </summary>
internal sealed class LegacySettingsSection(IFeature feature, FeatureRegistry registry) : ISettingsSection
{
    private readonly FeatureCatalogEntry _entry = FeatureCatalog.Get(feature.CatalogId)!;

    public string PageId => _entry.Page;

    public string? Anchor => _entry.Anchor;

    public string? Title => _entry.Title;

    public string? FeatureId => _entry.Id;

    public int Order => 10_000;

    public FrameworkElement CreateView(ISettingsContext context)
    {
        FrameworkElement? view = null;
        if (registry.IsRunning(_entry.Id))
        {
            view = feature.CreateSettingsView();
        }

        var stack = new StackPanel();
        if (view is null)
        {
            // Nothing to show: collapsed so pages without a real section stay clean.
            stack.Visibility = Visibility.Collapsed;
            return stack;
        }

        var heading = UiKit.Text(_entry.Title, 15, FontWeights.SemiBold, ThemeKeys.TextPrimary);
        heading.Margin = new Thickness(2, 0, 0, 8);
        stack.Children.Add(heading);
        stack.Children.Add(view);
        return stack;
    }
}

/// <summary>
/// TEMPORARY ADAPTER for the flyout: the <see cref="FeatureAction"/>s of v1 features are listed as plain rows on the tab
/// their feature belongs to (Keep awake actions on the Keep awake tab, Volume mixer actions on the Volume mixer tab, ...). Real
/// <see cref="IPanelSection"/> / <see cref="IPanelUtility"/> contributions replace them.
/// </summary>
internal static class LegacyPanel
{
    private static readonly Dictionary<string, string> TabOf = new(StringComparer.Ordinal)
    {
        ["keep-awake"] = PanelTabs.KeepAwake,
        ["brightness"] = PanelTabs.Utilities,
        ["mixer"] = PanelTabs.Mixer,
        ["audio-priority"] = PanelTabs.Mixer,
        ["monitor-cpu"] = PanelTabs.System,
        ["monitor-gpu"] = PanelTabs.System,
        ["monitor-memory"] = PanelTabs.System,
        ["connected-devices"] = PanelTabs.System,
        ["monitor-network"] = PanelTabs.Network,
        ["monitor-disk"] = PanelTabs.Disk,
        ["monitor-power"] = PanelTabs.Power,
        ["quick-toggles"] = PanelTabs.Toggles,
        ["mic-mute"] = PanelTabs.Toggles,
        ["quit-apps"] = PanelTabs.Toggles,
        ["wallpaper"] = PanelTabs.Wallpaper,
    };

    public static string TabFor(string catalogId)
    {
        if (TabOf.TryGetValue(catalogId, out var tab))
        {
            return tab;
        }

        var controls = PanelTabs.Get(PanelTabs.Controls)!.GateFeatures;
        return controls.Contains(catalogId) ? PanelTabs.Controls : PanelTabs.Utilities;
    }

    /// <summary>Actions of running v1 features that belong on <paramref name="tabId"/>.</summary>
    public static IReadOnlyList<(IFeature Feature, FeatureCatalogEntry Entry, FeatureAction Action)> ActionsFor(FeatureRegistry registry, string tabId)
    {
        var result = new List<(IFeature, FeatureCatalogEntry, FeatureAction)>();
        foreach (var (feature, action) in registry.All)
        {
            var entry = FeatureCatalog.Get(feature.CatalogId);
            if (entry is not null && TabFor(entry.Id) == tabId)
            {
                result.Add((feature, entry, action));
            }
        }

        return result;
    }
}
