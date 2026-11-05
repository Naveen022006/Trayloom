// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.SystemTools.Bluetooth;

/// <summary>English strings of the Bluetooth feature (spec 09 part 1), verbatim.</summary>
internal static class BtStrings
{
    public static string Title => Loc.T("Bluetooth");

    // ---- island page
    public static string Connected => Loc.T("Connected");

    public static string PairedDevices => Loc.T("Paired devices");

    public static string NotConnected => Loc.T("Not connected");

    public static string ConnectEllipsis => Loc.T("Connect…");

    public static string AddDevice => Loc.T("Add a device");

    public static string BluetoothSettings => Loc.T("Bluetooth settings");

    public static string NoDevices => Loc.T("No paired devices.");

    public static string NoAdapter => Loc.T("No Bluetooth on this PC.");

    public static string NoAdapterHint => Loc.T("Plug in a Bluetooth adapter to use this page.");

    public static string HardwareOff => Loc.T("Bluetooth is turned off by a hardware switch or airplane mode.");

    public static string BluetoothOff => Loc.T("Bluetooth is off.");

    public static string TurnOn => Loc.T("Turn on");

    public static string RadioDenied => Loc.T("Windows did not let Trayloom switch Bluetooth. Use Bluetooth settings instead.");

    public static string AccessDenied => Loc.T("Windows did not let Trayloom switch Bluetooth");

    public static string Footnote => Loc.T("Windows connects and disconnects devices itself. Connect… opens its Bluetooth list.");

    public static string ConnectedLevel(int percent) => Loc.F("Connected · {0}%", percent);

    public static string Percent(int percent) => Loc.F("{0}%", percent);

    public static string LowBattery => Loc.T("Low battery");

    public static string LowTooltip(string name, int percent) => Loc.F("{0} battery is low: {1}%", name, percent);

    // ---- tray tab
    public static string On => Loc.T("On");

    public static string Off => Loc.T("Off");

    public static string TurnedOffByHardware => Loc.T("Turned off by hardware");

    public static string AddDeviceEllipsis => Loc.T("Add a device…");

    public static string BluetoothSettingsEllipsis => Loc.T("Bluetooth settings…");

    // ---- settings
    public static string ShowOnConnect => Loc.T("Show when a device connects");

    public static string ShowOnConnectCaption => Loc.T("Its name and battery appear in the closed island for a moment.");

    public static string WarnLow => Loc.T("Warn when a battery runs low");

    public static string WarnLowCaption => Loc.T("Once per charge, when a connected device falls to the level you choose.");

    public static string WarnAt => Loc.T("Warn at");

    public static string KeepLowStrip => Loc.T("Keep a low battery in the closed island");

    public static string KeepLowStripCaption => Loc.T("Until you charge it or it disconnects.");

    public static string DevicesToShow => Loc.T("Devices to show");

    public static string ShowAll => Loc.T("All paired devices");

    public static string ShowConnected => Loc.T("Connected devices");

    public static string ShowBattery => Loc.T("Devices that report a battery");

    public static string ShownDevices => Loc.T("Shown devices");

    public static string SettingsNote => Loc.T("Battery levels come from Windows. Many earbuds report one level for both buds, in steps of 10 or 20%. AirPods do not report each bud or the case to Windows. Some devices report no battery at all.");

    public static string OpenBluetoothSettings => Loc.T("Open Bluetooth settings");

    // ---- actions
    public static string TurnBluetoothOff => Loc.T("Turn Bluetooth off");

    public static string TurnBluetoothOn => Loc.T("Turn Bluetooth on");

    public static string ActionSettings => Loc.T("Open Bluetooth settings");

    public static string ActionAdd => Loc.T("Add a Bluetooth device");
}
