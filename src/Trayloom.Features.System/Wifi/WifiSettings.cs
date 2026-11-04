// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.SystemTools.Wifi;

/// <summary>Settings &gt; Dynamic Island &gt; "Wi-Fi" (anchor <c>wifi</c>, spec 06 section 3.10).</summary>
public sealed class WifiSettingsSection : ISettingsSection
{
    public string? FeatureId => WifiKeys.FeatureId;

    public string PageId => SettingsPages.Island;

    public string? Anchor => "wifi";

    public string? Title => WifiStrings.Title;

    public int Order => 190;

    public IReadOnlyList<string> Keywords => ["wireless", "network", "SSID", "signal", "location"];

    public FrameworkElement CreateView(ISettingsContext context)
    {
        WifiKeys.Register(context.App);
        var service = context.Services.Get<WifiService>();
        var card = new SettingsCard(WifiStrings.Title);
        card.Add(new SettingsRow("", WifiStrings.SettingsRowTitle, WifiStrings.SettingsRowCaption));
        card.Add(new SettingsRow(null, WifiStrings.ShowSavedFirst, null, new MacSwitch(context.App.Get(WifiKeys.ShowSaved, true), SwitchSize.Regular, on => context.App.Set(WifiKeys.ShowSaved, on))));

        if (service is not null)
        {
            if (!service.Snapshot.Loaded)
            {
                // The status row exists only when Windows hides the names, so the answer has to be read once; the page is rebuilt when it arrives.
                WifiGuard.FireAndLog(async () =>
                {
                    await service.RefreshAsync();
                    if (service.Snapshot.LocationNeeded)
                    {
                        context.RequestRebuild();
                    }
                }, context.Log, "settings load");
            }
            else if (service.Snapshot.LocationNeeded)
            {
                var button = UiKit.Button(WifiStrings.OpenLocationSettings, ButtonKind.Bordered, 12, null, () => WifiKeys.OpenUri("ms-settings:privacy-location"));
                WifiLook.Automate(button, "Wifi.LocationSettings", WifiStrings.OpenLocationSettings);
                card.Add(new SettingsRow("", WifiStrings.LocationNeeded, WifiLocation.Caption(), button));
            }
        }

        var open = UiKit.Button(WifiStrings.OpenWifiSettings, ButtonKind.Bordered, 12, null, () => WifiKeys.OpenUri("ms-settings:network-wifi"));
        open.HorizontalAlignment = HorizontalAlignment.Left;
        card.Add(open);
        return card;
    }
}
