// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Automation;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.WindowTools.Modes;

/// <summary>Quick toggles tab: one row per mode, a "Leave" row while a mode is active, and "Create a mode" when there are none (spec 08 section 2.6).</summary>
public sealed class ModesPanelSection : IPanelSection
{
    public string? FeatureId => ModesKeys.FeatureId;

    public string TabId => PanelTabs.Toggles;

    public int Order => 35;

    public FrameworkElement CreateView(IPanelContext context)
    {
        ModesKeys.Register(context.App);
        var group = new PanelRowGroup();
        var runtime = context.Services.Get<ModesRuntime>();
        var modes = runtime?.Modes ?? [];
        if (runtime is null || modes.Count == 0)
        {
            var create = new PanelActionRow("", ModesStrings.CreateMode, null, () => context.InvokeAfterClose(() => context.Shell.OpenSettings("quick-tools#modes"), 150));
            return group.Add(Landmark(create, "Modes.Panel.Create", ModesStrings.CreateMode));
        }

        var active = runtime.Active;
        foreach (var mode in modes)
        {
            var id = mode.Id;
            var caption = mode.Id == active?.Id ? ModesStrings.Active : mode.Items.Count == 0 ? ModesStrings.Empty : ModesStrings.ItemCount(mode.Items.Count);
            var row = new PanelActionRow(mode.Glyph, mode.Name, caption, () => context.InvokeAfterClose(() => runtime.Enter(id, "panel"), 200), showCaption: true);
            group.Add(Landmark(row, "Modes.Panel." + id, mode.Name));
        }

        if (active is not null)
        {
            var title = ModesStrings.Leave(active.Name);
            var leave = new PanelActionRow("", title, null, () => context.InvokeAfterClose(() => runtime.Leave("panel"), 200));
            group.Add(Landmark(leave, "Modes.Panel.Leave", title));
        }

        return group;
    }

    /// <summary>Row borders are not part of the UI Automation tree, so each row sits in a landmark that carries its id and name.</summary>
    private static FrameworkElement Landmark(FrameworkElement row, string id, string name)
    {
        var host = new LandmarkControl { Content = row, Focusable = false, IsTabStop = false };
        AutomationProperties.SetAutomationId(host, id);
        AutomationProperties.SetName(host, name);
        return host;
    }

    private sealed class LandmarkControl : System.Windows.Controls.ContentControl
    {
        protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() =>
            new System.Windows.Automation.Peers.FrameworkElementAutomationPeer(this);
    }
}

/// <summary>The hosted "Modes" utility of the Quick panel: the tile grid in the light style. A pick closes the panel, then enters the mode.</summary>
public sealed class ModesUtility : IPanelUtility
{
    public string? FeatureId => ModesKeys.FeatureId;

    public int Order => 35;

    public string Id => ModesKeys.FeatureId;

    public string Title => ModesStrings.Title;

    public string Glyph => "";

    public string? Caption => ModesStrings.PanelCaption;

    public string? SettingsDestination => "quick-tools#modes";

    public FrameworkElement? CreateHostedView(IPanelContext context)
    {
        if (context.Services.Get<ModesRuntime>() is not { } runtime)
        {
            return null;
        }

        return new ModeTilesView(
            runtime,
            island: false,
            pick: id => context.InvokeAfterClose(() => runtime.Enter(id, "quick-panel"), 200),
            create: () => context.InvokeAfterClose(() => context.Shell.OpenSettings("quick-tools#modes"), 150),
            resized: context.RequestRelayout);
    }
}
