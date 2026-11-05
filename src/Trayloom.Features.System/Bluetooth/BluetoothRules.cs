// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace Trayloom.Features.SystemTools.Bluetooth;

/// <summary>The pure decisions of the Bluetooth feature, kept free of Windows so a console harness can check them.</summary>
internal static class BtRules
{
    /// <summary>A battery value as Windows reports it (a small integer type holding 0 to 100); anything else is "no battery".</summary>
    public static int? ParseBattery(object? value)
    {
        long number;
        switch (value)
        {
            case byte b:
                number = b;
                break;
            case sbyte sb:
                number = sb;
                break;
            case ushort us:
                number = us;
                break;
            case short s:
                number = s;
                break;
            case uint ui:
                number = ui;
                break;
            case int i:
                number = i;
                break;
            default:
                return null;
        }

        return number is >= 0 and <= 100 ? (int)number : null;
    }

    /// <summary>
    /// A Bluetooth address as 12 lowercase hex digits. Endpoint ids end with the address (<c>…-5c:5e:0a:04:06:9c</c>), so a long text is
    /// read from its last 17 characters.
    /// </summary>
    public static string? NormalizeAddress(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var s = text.Trim();
        if (s.Length > 17)
        {
            s = s[^17..];
        }

        s = s.Replace(":", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        if (s.Length != 12)
        {
            return null;
        }

        foreach (var c in s)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return null;
            }
        }

        return s;
    }

    /// <summary>A container id given as a <see cref="Guid"/> or as text with or without braces; the empty id counts as none.</summary>
    public static Guid? ParseContainer(object? value)
    {
        var guid = value switch
        {
            Guid g => g,
            string s when Guid.TryParse(s, out var parsed) => parsed,
            _ => Guid.Empty,
        };
        return guid == Guid.Empty ? null : guid;
    }

    /// <summary>
    /// What a device is. The name wins, because a pair of earbuds often reports a generic class; the class of device fills in the rest.
    /// <paramref name="major"/> and <paramref name="minor"/> are the Windows <c>BluetoothMajorClass</c> and <c>BluetoothMinorClass</c> numbers.
    /// </summary>
    public static BtKind KindFrom(uint? major, uint? minor, string name)
    {
        var n = (name ?? string.Empty).ToLowerInvariant();
        if (HasAny(n, "airpods", "buds", "headphone", "headset", "earbuds", "wh-", "wf-"))
        {
            return BtKind.Headphones;
        }

        if (HasAny(n, "keyboard", "keys"))
        {
            return BtKind.Keyboard;
        }

        if (HasAny(n, "mouse", "mx master", "mx anywhere", "trackpad"))
        {
            return BtKind.Mouse;
        }

        if (HasAny(n, "controller", "gamepad", "xbox", "dualsense"))
        {
            return BtKind.Gamepad;
        }

        if (n.Contains("watch", StringComparison.Ordinal))
        {
            return BtKind.Watch;
        }

        if (HasAny(n, "speaker", "soundbar"))
        {
            return BtKind.Speaker;
        }

        if (major is not { } mj || minor is not { } mn)
        {
            return BtKind.Other;
        }

        // Windows numbers the peripheral minor classes in steps of 16 (keyboard 16, pointer 32); the raw class-of-device bits (64 and 128) are
        // accepted too, so a value that was not shifted still lands on the right kind.
        return (mj, mn) switch
        {
            (1, _) => BtKind.Computer,
            (2, _) => BtKind.Phone,
            (4, 5) or (4, 7) or (4, 10) => BtKind.Speaker,
            (4, 1) or (4, 2) or (4, 6) => BtKind.Headphones,
            (4, 18) => BtKind.Gamepad,
            (5, _) when (mn & 0x0F) is 1 or 2 => BtKind.Gamepad,
            (5, _) when (mn & 0xA0) != 0 => BtKind.Mouse,
            (5, _) when (mn & 0x50) != 0 => BtKind.Keyboard,
            (7, _) => BtKind.Watch,
            (8, _) => BtKind.Gamepad,
            _ => BtKind.Other,
        };
    }

    private static bool HasAny(string text, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (text.Contains(needle, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// One device per physical thing. Endpoints are grouped by container (a classic and a low-energy endpoint of one watch share it),
    /// else by address. A device is connected when any endpoint is; its battery is the highest one a node of its container reports, and
    /// only while it is connected, because Windows keeps the last value of a device that has gone.
    /// </summary>
    public static IReadOnlyList<BtDevice> Merge(IEnumerable<AepRow> aeps, IEnumerable<NodeRow> nodes)
    {
        var order = new List<string>();
        var groups = new Dictionary<string, List<AepRow>>(StringComparer.Ordinal);
        foreach (var aep in aeps)
        {
            var address = NormalizeAddress(aep.Address) ?? NormalizeAddress(aep.Id);
            var group = aep.Container is { } c ? "c:" + c.ToString("N") : address is not null ? "a:" + address : "i:" + aep.Id;
            if (!groups.TryGetValue(group, out var rows))
            {
                groups[group] = rows = [];
                order.Add(group);
            }

            rows.Add(aep);
        }

        var batteryOf = new Dictionary<Guid, int>();
        foreach (var node in nodes)
        {
            if (node.Container is { } container && node.Battery is { } level)
            {
                batteryOf[container] = batteryOf.TryGetValue(container, out var best) ? Math.Max(best, level) : level;
            }
        }

        var result = new List<BtDevice>(order.Count);
        foreach (var group in order)
        {
            var rows = groups[group];
            var classic = rows.FirstOrDefault(r => !r.Le && !string.IsNullOrWhiteSpace(r.Name));
            var named = classic ?? rows.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.Name));
            var container = rows.Select(r => r.Container).FirstOrDefault(c => c is not null);
            var key = KeyOf(rows, container);
            var connected = rows.Any(r => r.Connected);
            int? battery = connected && container is { } id && batteryOf.TryGetValue(id, out var level) ? level : null;
            var name = named?.Name ?? key;
            result.Add(new BtDevice(key, name, KindFrom(null, null, name), connected, battery, container, rows.Select(r => r.Id).ToList(), null));
        }

        return result;
    }

    private static string KeyOf(List<AepRow> rows, Guid? container)
    {
        foreach (var row in rows.OrderBy(r => r.Le))
        {
            var address = NormalizeAddress(row.Address) ?? NormalizeAddress(row.Id);
            if (address is not null)
            {
                return address;
            }
        }

        return container is { } c ? c.ToString("N") : rows[0].Id.ToLowerInvariant();
    }

    /// <summary>
    /// The devices a view lists: connected first, then those with a battery, then by name. <paramref name="show"/> is <c>all</c>,
    /// <c>connected</c> or <c>battery</c>; hidden keys never appear.
    /// </summary>
    public static IReadOnlyList<BtDevice> Visible(IReadOnlyList<BtDevice> all, string show, IReadOnlySet<string> hidden)
    {
        IEnumerable<BtDevice> query = all.Where(d => !hidden.Contains(d.Key));
        if (show == "connected")
        {
            query = query.Where(d => d.Connected);
        }
        else if (show == "battery")
        {
            query = query.Where(d => d.Battery is not null);
        }

        return query
            .OrderByDescending(d => d.Connected)
            .ThenByDescending(d => d.Battery is not null)
            .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// True once, when a reading falls to <paramref name="threshold"/> or below. The first reading only sets the baseline, so a device that
    /// connects already low does not alert; the alert re-arms once the level is 5 points above the threshold.
    /// </summary>
    public static bool StepLow(ref BtLowState state, int percent, int threshold)
    {
        if (!state.Seen)
        {
            state.Seen = true;
            state.Low = percent <= threshold;
            return false;
        }

        if (!state.Low && percent <= threshold)
        {
            state.Low = true;
            return true;
        }

        if (state.Low && percent >= threshold + 5)
        {
            state.Low = false;
        }

        return false;
    }

    /// <summary>The colour of a battery ring as a hex string: red at 10 % or less, orange at the warning level or less, else white.</summary>
    public static string RingTint(int percent, int threshold) => percent <= 10 ? "#FF453A" : percent <= threshold ? "#FF9F0A" : "#FFFFFF";
}
