// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Features.SystemTools.Bluetooth;

namespace Trayloom.System.Harness;

/// <summary>
/// Checks of <see cref="BtRules"/>: battery parsing, address and container ids, the merge of classic and low-energy endpoints with their
/// PnP nodes, device kinds, the list order and filters, the low-battery step and the ring colour. Pure functions only; no radio or device is touched.
/// </summary>
internal static class BluetoothChecks
{
    private static Action<bool, string> _check = (_, _) => { };

    public static void Run(Action<bool, string> check)
    {
        _check = check;
        Battery();
        Address();
        Container();
        Merging();
        Kinds();
        Low();
        Listing();
        Tint();
    }

    private static void Check(bool ok, string what) => _check(ok, "bluetooth: " + what);

    private static void Battery()
    {
        Check(BtRules.ParseBattery((byte)75) == 75, "byte 75 is 75");
        Check(BtRules.ParseBattery((byte)0) == 0, "byte 0 is 0");
        Check(BtRules.ParseBattery((byte)101) is null, "byte 101 is no battery");
        Check(BtRules.ParseBattery(null) is null, "null is no battery");
        Check(BtRules.ParseBattery((uint)55) == 55, "uint 55 is 55");
        Check(BtRules.ParseBattery("75") is null, "text is no battery");
        Check(BtRules.ParseBattery(-1) is null, "a negative number is no battery");
        Check(BtRules.ParseBattery((ushort)100) == 100, "ushort 100 is 100");
    }

    private static void Address()
    {
        Check(BtRules.NormalizeAddress("5c:5e:0a:04:06:9c") == "5c5e0a04069c", "colon address");
        Check(BtRules.NormalizeAddress("5C5E0A04069C") == "5c5e0a04069c", "bare upper-case address");
        Check(BtRules.NormalizeAddress("Bluetooth#Bluetoothaa:bb:cc:dd:ee:ff-5c:5e:0a:04:06:9c") == "5c5e0a04069c", "endpoint id ends with the address");
        Check(BtRules.NormalizeAddress("xyz") is null, "text is no address");
        Check(BtRules.NormalizeAddress(null) is null, "null is no address");
    }

    private static void Container()
    {
        var guid = Guid.Parse("a1b2c3d4-0000-4000-8000-0123456789ab");
        Check(BtRules.ParseContainer("{a1b2c3d4-0000-4000-8000-0123456789ab}") == guid, "container with braces");
        Check(BtRules.ParseContainer("a1b2c3d4-0000-4000-8000-0123456789ab") == guid, "container without braces");
        Check(BtRules.ParseContainer(guid) == guid, "container as a Guid");
        Check(BtRules.ParseContainer(Guid.Empty) is null, "the empty container is none");
        Check(BtRules.ParseContainer(12) is null, "a number is no container");
    }

    private static void Merging()
    {
        var container = Guid.NewGuid();
        var classic = new AepRow("classic", "CMF Watch", true, "5c:5e:0a:04:06:9c", container, false);
        var le = new AepRow("le", "CMF Watch LE", false, "5c:5e:0a:04:06:9d", container, true);
        var nodes = new[] { new NodeRow("n1", container, null), new NodeRow("n2", container, 75), new NodeRow("n3", container, null) };
        var merged = BtRules.Merge([classic, le], nodes);
        Check(merged.Count == 1, "classic and low-energy endpoints of one container are one device");
        Check(merged[0].Connected && merged[0].Battery == 75, "the device is connected and takes the node's 75 %");
        Check(merged[0].Name == "CMF Watch" && merged[0].Key == "5c5e0a04069c", "the classic endpoint gives the name and the key");

        var gone = BtRules.Merge([classic with { Connected = false }, le], [new NodeRow("n", container, 10)]);
        Check(gone.Count == 1 && !gone[0].Connected && gone[0].Battery is null, "a device that is not connected shows no stale battery");

        var best = BtRules.Merge([classic], [new NodeRow("a", container, 40), new NodeRow("b", container, 90), new NodeRow("c", Guid.NewGuid(), 99)]);
        Check(best[0].Battery == 90, "the highest battery of the container wins, other containers are ignored");

        var a = new AepRow("x1", "Mouse", true, "11:22:33:44:55:66", null, false);
        var b = new AepRow("x2", "Mouse", false, "11-22-33-44-55-66", null, true);
        var byAddress = BtRules.Merge([a, b], []);
        Check(byAddress.Count == 1 && byAddress[0].Connected, "without a container the endpoints merge by address");
        Check(BtRules.Merge([], []).Count == 0, "nothing paired is an empty list");
    }

    private static void Kinds()
    {
        Check(BtRules.KindFrom(null, null, "AirPods Pro") == BtKind.Headphones, "AirPods are headphones");
        Check(BtRules.KindFrom(null, null, "Galaxy Buds Core") == BtKind.Headphones, "Buds are headphones");
        Check(BtRules.KindFrom(null, null, "MX Master 3S") == BtKind.Mouse, "MX Master is a mouse");
        Check(BtRules.KindFrom(null, null, "Xbox Wireless Controller") == BtKind.Gamepad, "an Xbox controller is a gamepad");
        Check(BtRules.KindFrom(4, 6, "Unknown") == BtKind.Headphones, "class 4/6 is headphones");
        Check(BtRules.KindFrom(5, 0x40, "Unknown") == BtKind.Keyboard, "class 5 with the keyboard bit is a keyboard");
        Check(BtRules.KindFrom(5, 0x10, "Unknown") == BtKind.Keyboard, "Windows' keyboard minor class 16 is a keyboard");
        Check(BtRules.KindFrom(5, 0x20, "Unknown") == BtKind.Mouse, "Windows' pointer minor class 32 is a mouse");
        Check(BtRules.KindFrom(2, 3, "Unknown") == BtKind.Phone, "class 2 is a phone");
        Check(BtRules.KindFrom(1, 3, "Unknown") == BtKind.Computer, "class 1 is a computer");
        Check(BtRules.KindFrom(2, 3, "Pixel Buds") == BtKind.Headphones, "the name wins over the class");
        Check(BtRules.KindFrom(null, null, "Thing") == BtKind.Other, "no clue is other");
    }

    private static void Low()
    {
        var state = default(BtLowState);
        Check(!BtRules.StepLow(ref state, 25, 20), "25 sets the baseline");
        Check(!BtRules.StepLow(ref state, 21, 20), "21 is still above");
        Check(BtRules.StepLow(ref state, 20, 20), "20 alerts once");
        Check(!BtRules.StepLow(ref state, 18, 20), "18 does not alert again");
        Check(!BtRules.StepLow(ref state, 24, 20), "24 does not re-arm");
        Check(!BtRules.StepLow(ref state, 25, 20), "25 re-arms without alerting");
        Check(BtRules.StepLow(ref state, 19, 20), "19 alerts again after the re-arm");

        var fresh = default(BtLowState);
        Check(!BtRules.StepLow(ref fresh, 15, 20), "a first reading of 15 is only the baseline");
        Check(!BtRules.StepLow(ref fresh, 12, 20), "and does not alert afterwards while it stays low");
    }

    private static void Listing()
    {
        BtDevice Device(string key, string name, bool connected, int? battery) => new(key, name, BtKind.Other, connected, battery, null, [], null);
        var all = new[]
        {
            Device("k1", "zeta", false, null),
            Device("k2", "Beta", true, null),
            Device("k3", "alpha", true, 50),
            Device("k4", "gamma", false, null),
            Device("k5", "delta", true, 80),
        };
        var none = new HashSet<string>();
        var order = string.Join(',', BtRules.Visible(all, "all", none).Select(d => d.Key));
        Check(order == "k3,k5,k2,k4,k1", "connected first, then with battery, then by name");
        Check(BtRules.Visible(all, "connected", none).All(d => d.Connected) && BtRules.Visible(all, "connected", none).Count == 3, "the connected filter keeps three");
        Check(BtRules.Visible(all, "battery", none).Count == 2, "the battery filter keeps the two with a battery");
        Check(BtRules.Visible(all, "all", new HashSet<string> { "k3", "k1" }).Count == 3, "hidden keys never appear");
        Check(BtRules.Visible(all, "battery", new HashSet<string> { "k3" }).Single().Key == "k5", "filters and hidden keys combine");
    }

    private static void Tint()
    {
        Check(BtRules.RingTint(9, 20) == "#FF453A", "9 % is red");
        Check(BtRules.RingTint(10, 20) == "#FF453A", "10 % is red");
        Check(BtRules.RingTint(20, 20) == "#FF9F0A", "20 % is orange");
        Check(BtRules.RingTint(21, 20) == "#FFFFFF", "21 % is white");
        Check(BtRules.RingTint(15, 10) == "#FFFFFF", "above a low threshold is white");
    }
}
