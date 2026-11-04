// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Automation;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.WindowTools.AwayMode;

/// <summary>Quick toggles tab card: the Away mode switch and "Go away now" / "Restore windows" (spec 06 section 4.9).</summary>
public sealed class AwayPanelSection : IPanelSection
{
    public string? FeatureId => "away-mode";

    public string TabId => PanelTabs.Toggles;

    public int Order => 40;

    public FrameworkElement CreateView(IPanelContext context)
    {
        AwayKeys.Register(context.App);
        var app = context.App;
        var controller = context.Services.Get<AwayController>();
        var enabled = app.Get<bool>(AwayKeys.Enabled);
        var caption = enabled ? AwayStrings.PanelCaption(app.Get<int>(AwayKeys.IdleMinutes)) : AwayStrings.Off;

        var toggle = new PanelToggleRow("", AwayStrings.Title, caption, enabled, on => app.Set(AwayKeys.Enabled, on), showCaption: true);

        var away = controller?.IsAway == true;
        var title = away ? AwayStrings.RestoreWindows : AwayStrings.GoAwayNow;
        var action = new PanelActionRow(away ? "" : "", title, null, () => context.InvokeAfterClose(() =>
        {
            if (controller is null)
            {
                return;
            }

            if (controller.IsAway)
            {
                controller.Return("manual");
            }
            else
            {
                controller.GoAway("manual");
            }
        }, 200));

        return new PanelRowGroup().Add(Landmark(toggle, "AwayMode.PanelToggle", AwayStrings.Title)).Add(Landmark(action, "AwayMode.PanelNow", title));
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
