// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace Trayloom.Features.SystemTools.Bluetooth;

/// <summary>What a device is, for its icon and for the decision whether a connect notice is worth showing.</summary>
internal enum BtKind
{
    Headphones,
    Speaker,
    Keyboard,
    Mouse,
    Gamepad,
    Phone,
    Computer,
    Watch,
    Other,
}

/// <summary>State of the Bluetooth radio. <c>Disabled</c> means a hardware switch or airplane mode; <c>None</c> means no adapter.</summary>
internal enum BtRadio
{
    Unknown,
    None,
    On,
    Off,
    Disabled,
}

/// <summary>One physical device, merged from its classic and low-energy endpoints.</summary>
/// <param name="Key">The Bluetooth address as 12 lowercase hex digits.</param>
/// <param name="Name">The name Windows shows.</param>
/// <param name="Kind">The kind, from the name and the class of device.</param>
/// <param name="Connected">True when any endpoint is connected.</param>
/// <param name="Battery">Percent; always null while the device is not connected, because Windows keeps the last value of a device that left.</param>
/// <param name="Container">The Windows container the endpoints share.</param>
/// <param name="AepIds">The ids of the endpoints.</param>
/// <param name="ConnectedSinceUtc">When this run first saw the device connected.</param>
internal sealed record BtDevice(string Key, string Name, BtKind Kind, bool Connected, int? Battery, Guid? Container, IReadOnlyList<string> AepIds, DateTime? ConnectedSinceUtc);

/// <summary>Everything the views draw from.</summary>
/// <param name="Loaded">False until Windows answered the first time.</param>
/// <param name="Radio">The radio state.</param>
/// <param name="AccessDenied">Windows refused Trayloom access to the radio, so the switch cannot work.</param>
/// <param name="Devices">Every paired device, unfiltered and unsorted.</param>
internal sealed record BtSnapshot(bool Loaded, BtRadio Radio, bool AccessDenied, IReadOnlyList<BtDevice> Devices);

/// <summary>A closed-island notice: a device connected or its battery ran low.</summary>
internal sealed record BtNotice(BtKind Kind, string Name, string Detail, int? Battery, bool Low);

/// <summary>A paired endpoint as Windows lists it (the input of the merge).</summary>
internal sealed record AepRow(string Id, string Name, bool Connected, string? Address, Guid? Container, bool Le);

/// <summary>A Bluetooth PnP node with the battery it reports (the input of the merge).</summary>
internal sealed record NodeRow(string Id, Guid? Container, int? Battery);

/// <summary>Memory of the low-battery alert for one device: the first reading only sets a baseline.</summary>
internal struct BtLowState
{
    public bool Seen;

    public bool Low;
}
