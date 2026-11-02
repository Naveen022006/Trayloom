// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows.Input;
using WinKnife.Core;

namespace WinKnife.Features.Capture;

internal enum ToolId
{
    Select, Arrow, Pixelate, Crop, Text, Sticker, Rect, Highlight, Freehand, Line, Ellipse, Counter, Redact, Spotlight, ExpandCanvas, Image,
}

internal sealed record ToolInfo(ToolId Id, string RawId, string Title, string Glyph);

/// <summary>The 15 rail tools, their order (digits 1-9 are positions) and per-tool custom keys.</summary>
internal static class ToolCatalog
{
    public static readonly IReadOnlyList<ToolInfo> All =
    [
        new(ToolId.Select, "select", "Select", G.Pointer),
        new(ToolId.Arrow, "arrow", "Arrow", ""),
        new(ToolId.Pixelate, "pixelate", "Blur", ""),
        new(ToolId.Crop, "crop", "Crop", G.Crop),
        new(ToolId.Text, "text", "Text", ""),
        new(ToolId.Sticker, "sticker", "Sticker", G.Emoji),
        new(ToolId.Rect, "rect", "Rectangle", ""),
        new(ToolId.Highlight, "highlight", "Highlighter", G.Highlight),
        new(ToolId.Freehand, "freehand", "Pen", G.Pen),
        new(ToolId.Line, "line", "Line", ""),
        new(ToolId.Ellipse, "ellipse", "Ellipse", ""),
        new(ToolId.Counter, "counter", "Number", ""),
        new(ToolId.Redact, "redact", "Solid block", ""),
        new(ToolId.Spotlight, "spotlight", "Spotlight", ""),
        new(ToolId.ExpandCanvas, "expandCanvas", "Expand canvas", G.Expand),
    ];

    public static ToolInfo Get(ToolId id) => All.FirstOrDefault(t => t.Id == id) ?? All[0];

    public static ToolInfo? ByRaw(string raw) => All.FirstOrDefault(t => t.RawId == raw);

    /// <summary>Order from the setting: unknown/duplicate ids dropped, missing tools appended in canonical order.</summary>
    public static List<ToolInfo> Order(IAppSettings s)
    {
        var raw = (s.Get<string>(K.ToolOrder) ?? K.DefaultToolOrder).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = new List<ToolInfo>();
        foreach (var r in raw)
        {
            if (ByRaw(r) is { } t && !list.Contains(t))
            {
                list.Add(t);
            }
        }

        foreach (var t in All)
        {
            if (!list.Contains(t))
            {
                list.Add(t);
            }
        }

        return list;
    }

    public static void SaveOrder(IAppSettings s, IEnumerable<ToolInfo> order) =>
        s.Set(K.ToolOrder, string.Join(",", order.Select(t => t.RawId)));

    public static bool IsDefaultOrder(IAppSettings s) =>
        string.Join(",", Order(s).Select(t => t.RawId)) == K.DefaultToolOrder && string.IsNullOrEmpty(s.Get<string>(K.ToolShortcuts));

    /// <summary>Custom keys (tool raw id to key name), duplicates dropped (first tool in default order keeps it).</summary>
    public static Dictionary<string, Key> CustomKeys(IAppSettings s)
    {
        var result = new Dictionary<string, Key>();
        var used = new HashSet<Key>();
        var map = new Dictionary<string, Key>();
        foreach (var part in (s.Get<string>(K.ToolShortcuts) ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && ByRaw(kv[0]) is not null && Enum.TryParse<Key>(kv[1], true, out var k))
            {
                map[kv[0]] = k;
            }
        }

        foreach (var t in All)
        {
            if (map.TryGetValue(t.RawId, out var key) && used.Add(key))
            {
                result[t.RawId] = key;
            }
        }

        return result;
    }

    public static void SaveCustomKeys(IAppSettings s, Dictionary<string, Key> keys) =>
        s.Set(K.ToolShortcuts, string.Join(",", keys.Select(kv => $"{kv.Key}={kv.Value}")));

    /// <summary>Digit (1-9) of a key, or 0.</summary>
    public static int DigitOf(Key key) => key >= Key.D1 && key <= Key.D9 ? key - Key.D0 : key >= Key.NumPad1 && key <= Key.NumPad9 ? key - Key.NumPad0 : 0;

    /// <summary>The tool selected by a key press in the editor (custom key first, else position digit).</summary>
    public static ToolInfo? ToolForKey(IAppSettings s, Key key)
    {
        if (!s.Get<bool>(K.ToolShortcutsEnabled))
        {
            return null;
        }

        var custom = CustomKeys(s);
        foreach (var kv in custom)
        {
            if (kv.Value == key)
            {
                return ByRaw(kv.Key);
            }
        }

        var digit = DigitOf(key);
        if (digit > 0)
        {
            var order = Order(s);
            if (digit <= order.Count && !custom.ContainsKey(order[digit - 1].RawId))
            {
                return order[digit - 1];
            }
        }

        return null;
    }

    public static string BadgeFor(IAppSettings s, ToolInfo tool, int position)
    {
        if (!s.Get<bool>(K.ToolShortcutsEnabled))
        {
            return string.Empty;
        }

        if (CustomKeys(s).TryGetValue(tool.RawId, out var key))
        {
            return KeyName(key);
        }

        return position < 9 ? (position + 1).ToString() : string.Empty;
    }

    public static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        Key.OemPlus => "=",
        Key.OemMinus => "-",
        Key.Space => "Space",
        _ => key.ToString(),
    };

    private static readonly HashSet<Key> ReservedWithCtrl = [Key.C, Key.V, Key.S, Key.Z, Key.P, Key.D0, Key.D1, Key.OemPlus, Key.OemMinus, Key.Back, Key.Delete, Key.W, Key.Q];

    public static bool IsReserved(Key key) => key is Key.Escape or Key.Return or Key.Enter or Key.Back or Key.Delete;

    public static bool IsCtrlReserved(Key key) => ReservedWithCtrl.Contains(key);
}
