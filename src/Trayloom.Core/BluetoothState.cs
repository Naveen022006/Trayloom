// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace Trayloom.Core;

/// <summary>One Bluetooth device as the tray menu shows it.</summary>
/// <param name="Key">The Bluetooth address as 12 lowercase hex digits (stable across reconnects).</param>
/// <param name="Name">The name Windows shows for the device.</param>
/// <param name="Connected">True while Windows reports the device as connected.</param>
/// <param name="Battery">Battery level in percent; null when the device is not connected or reports none.</param>
public sealed record BluetoothDeviceInfo(string Key, string Name, bool Connected, int? Battery);

/// <summary>
/// Bluetooth state for the tray menu and the panel, so the App never references the System project. Provided by the Bluetooth
/// feature (<c>notch-bluetooth</c>); the service is null while that feature is not installed.
/// </summary>
public interface IBluetoothState
{
    /// <summary>True when the radio is on, false when it is off, null when there is no radio or Windows has not answered yet.</summary>
    bool? IsOn { get; }

    /// <summary>False once Windows said this PC has no Bluetooth adapter; null while that is not known yet.</summary>
    bool? HasAdapter { get; }

    /// <summary>The devices the person chose to show, connected ones first.</summary>
    IReadOnlyList<BluetoothDeviceInfo> Devices { get; }

    /// <summary>Raised (any thread) when the radio, the device list or a battery level changed.</summary>
    event Action? Changed;

    /// <summary>Turns the radio on or off without waiting; a refusal by Windows is logged.</summary>
    void SetRadio(bool on);

    /// <summary>Opens the Windows Bluetooth settings page.</summary>
    void OpenBluetoothSettings();

    /// <summary>Opens the Windows "Add a device" dialog.</summary>
    void OpenAddDevice();

    /// <summary>Opens the Windows list a device can be connected from.</summary>
    void OpenConnect();
}
