// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using Trayloom.Core;
using Trayloom.Core.Ui;
using Trayloom.Features.SystemTools.Wifi;

namespace Trayloom.Features.SystemTools.Bluetooth;

/// <summary>Settings &gt; Dynamic Island &gt; "Bluetooth" (anchor <c>bluetooth</c>).</summary>
public sealed class BluetoothSettingsSection : ISettingsSection
{
    /// <summary>How far the options that depend on a switch are indented.</summary>
    private const double Indent = 20;

    public string? FeatureId => BtKeys.FeatureId;

    public string PageId => SettingsPages.Island;

    public string? Anchor => "bluetooth";

    public string? Title => BtStrings.Title;

    public int Order => 195;

    public IReadOnlyList<string> Keywords => ["battery", "headphones", "AirPods", "earbuds", "mouse", "keyboard", "pair", "connect"];

    public FrameworkElement CreateView(ISettingsContext context)
    {
        var app = context.App;
        BtKeys.Register(app);
        var card = new SettingsCard(BtStrings.Title);

        card.Add(new SettingsRow(null, BtStrings.ShowOnConnect, BtStrings.ShowOnConnectCaption, new MacSwitch(app.Get(BtKeys.ConnectAlerts, true), SwitchSize.Regular, on => app.Set(BtKeys.ConnectAlerts, on))));

        // The options under "Warn when a battery runs low" follow its switch without rebuilding the page.
        var dependents = new List<FrameworkElement>();
        var lowOn = app.Get(BtKeys.LowAlerts, true);
        card.Add(new SettingsRow(null, BtStrings.WarnLow, BtStrings.WarnLowCaption, new MacSwitch(lowOn, SwitchSize.Regular, on =>
        {
            app.Set(BtKeys.LowAlerts, on);
            foreach (var element in dependents)
            {
                element.IsEnabled = on;
                element.Opacity = on ? 1 : 0.45;
            }
        })));
        var thresholds = BtKeys.Thresholds.Select(n => (n, BtStrings.Percent(n))).ToList();
        var picker = new PickerRow<int>(null, BtStrings.WarnAt, null, thresholds, BtKeys.Threshold(app), value => app.Set(BtKeys.LowThreshold, value), 90) { Margin = new Thickness(Indent, 0, 0, 0) };
        var strip = new SettingsRow(null, BtStrings.KeepLowStrip, BtStrings.KeepLowStripCaption, new MacSwitch(app.Get(BtKeys.LowStrip, true), SwitchSize.Regular, on => app.Set(BtKeys.LowStrip, on))) { Margin = new Thickness(Indent, 0, 0, 0) };
        dependents.Add(picker);
        dependents.Add(strip);
        foreach (var element in dependents)
        {
            element.IsEnabled = lowOn;
            element.Opacity = lowOn ? 1 : 0.45;
            card.Add(element);
        }

        card.Add(new PickerRow<string>(null, BtStrings.DevicesToShow, null,
        [
            ("all", BtStrings.ShowAll),
            ("connected", BtStrings.ShowConnected),
            ("battery", BtStrings.ShowBattery),
        ], app.Get(BtKeys.Show, "all"), value => app.Set(BtKeys.Show, value), 230));

        card.Add(UiKit.Text(BtStrings.ShownDevices, 13, FontWeights.SemiBold));
        var list = new StackPanel();
        card.Add(list);
        if (context.Services.Get<BluetoothService>() is { } service)
        {
            Fill(list, app, service);

            // The list follows the service for as long as the card is on screen, and the battery is read while it is.
            card.Loaded += (_, _) =>
            {
                service.Changed += Redraw;
                service.Attach();
            };
            card.Unloaded += (_, _) =>
            {
                service.Changed -= Redraw;
                service.Detach();
            };

            void Redraw() => Fill(list, app, service);
        }

        card.AddCaption(BtStrings.SettingsNote);
        var open = UiKit.Button(BtStrings.OpenBluetoothSettings, ButtonKind.Bordered, 12, null, BtKeys.OpenSettings);
        open.HorizontalAlignment = HorizontalAlignment.Left;
        card.Add(open);
        return card;
    }

    /// <summary>One tick box per paired device; ticking edits the hidden list. A device the person unticked stays listed so it can be ticked again.</summary>
    private static void Fill(StackPanel list, IAppSettings app, BluetoothService service)
    {
        list.Children.Clear();
        var hidden = BtKeys.HiddenSet(app);
        var all = BtRules.Visible(service.Snapshot.Devices, "all", new HashSet<string>());
        if (all.Count == 0)
        {
            list.Children.Add(UiKit.Text(BtStrings.NoDevices, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary));
            return;
        }

        foreach (var device in all)
        {
            var key = device.Key;
            var line = device.Connected ? (device.Battery is { } percent ? BtStrings.ConnectedLevel(percent) : BtStrings.Connected) : BtStrings.NotConnected;
            var text = new StackPanel { Margin = new Thickness(2, 0, 0, 0) };
            text.Children.Add(UiKit.Text(device.Name, 12.5, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis));
            text.Children.Add(UiKit.Text(line, 11, FontWeights.Normal, ThemeKeys.TextSecondary));
            var box = new CheckBox { IsChecked = !hidden.Contains(key), Content = text, Margin = new Thickness(0, 4, 0, 4), VerticalContentAlignment = VerticalAlignment.Center };
            box.Checked += (_, _) => SetShown(app, key, true);
            box.Unchecked += (_, _) => SetShown(app, key, false);
            WifiLook.Automate(box, "Bluetooth.Shown", device.Name);
            list.Children.Add(box);
        }
    }

    private static void SetShown(IAppSettings app, string key, bool shown)
    {
        var hidden = BtKeys.HiddenSet(app);
        if (shown)
        {
            hidden.Remove(key);
        }
        else
        {
            hidden.Add(key);
        }

        BtKeys.SetHidden(app, hidden);
    }
}
