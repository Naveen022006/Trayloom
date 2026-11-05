// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows.Controls;
using Trayloom.Core;

namespace Trayloom.App;

/// <summary>The "Bluetooth" submenu of the tray's right-click menu. It reads only <see cref="IBluetoothState"/>, never the System project.</summary>
internal static class BluetoothMenu
{
    /// <summary>Builds the submenu from the state as it is now; the tray menu is rebuilt each time it opens.</summary>
    public static MenuItem Build(IBluetoothState bluetooth, IShell shell)
    {
        var menu = new MenuItem { Header = Loc.T("Bluetooth") };
        if (bluetooth.IsOn is { } on)
        {
            menu.Items.Add(Item(on ? Loc.T("Turn Bluetooth off") : Loc.T("Turn Bluetooth on"), () => bluetooth.SetRadio(!on)));
            menu.Items.Add(new Separator());
        }

        foreach (var device in bluetooth.Devices.Where(d => d.Connected))
        {
            var text = device.Battery is { } percent ? Loc.F("{0} — {1}%", device.Name, percent) : Loc.F("{0} — Connected", device.Name);
            menu.Items.Add(Item(text, () => shell.OpenPanel(PanelTabs.Bluetooth)));
        }

        menu.Items.Add(Item(Loc.T("Connect a device…"), bluetooth.OpenConnect));
        menu.Items.Add(Item(Loc.T("Bluetooth settings…"), bluetooth.OpenBluetoothSettings));
        menu.Items.Add(Item(Loc.T("Show in panel"), () => shell.OpenPanel(PanelTabs.Bluetooth)));
        return menu;
    }

    private static MenuItem Item(string header, Action onClick)
    {
        // A single underscore in a menu header is an access-key marker and would be swallowed, so device names double it.
        var item = new MenuItem { Header = header.Replace("_", "__", StringComparison.Ordinal) };
        item.Click += (_, _) => onClick();
        return item;
    }
}
