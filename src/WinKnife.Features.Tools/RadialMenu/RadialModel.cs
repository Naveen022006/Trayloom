// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Input;
using System.Windows.Media;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

/// <summary>One action of a wheel. Raw kind / payload values persist in user data and never change.</summary>
internal sealed class RadialItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString().ToUpperInvariant();

    public string Kind { get; set; } = RadialKinds.App;

    public string Name { get; set; } = "";

    public string SymbolName { get; set; } = "";

    public string Payload { get; set; } = "";

    public string? CustomIconData { get; set; }

    public List<RadialItem> Children { get; set; } = [];

    public RadialItem Clone(bool newId = false)
    {
        var copy = new RadialItem
        {
            Id = newId ? Guid.NewGuid().ToString().ToUpperInvariant() : Id,
            Kind = Kind,
            Name = Name,
            SymbolName = SymbolName,
            Payload = Payload,
            CustomIconData = CustomIconData,
            Children = Children.Select(c => c.Clone(newId)).ToList(),
        };
        return copy;
    }
}

/// <summary>A named wheel with its own colour, triggers and actions.</summary>
internal sealed class RadialProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString().ToUpperInvariant();

    public string Name { get; set; } = "";

    public string Color { get; set; } = "accent";

    public string Shortcut { get; set; } = "";

    public string MouseButton { get; set; } = "off";

    public string? Preset { get; set; }

    public bool TrackpadTap { get; set; }

    public List<RadialItem> Items { get; set; } = [];

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "General" : Name;

    public bool HasTrigger => !string.IsNullOrEmpty(Shortcut) || MouseButton != "off" || TrackpadTap;

    public RadialProfile Clone() => new()
    {
        Id = Id,
        Name = Name,
        Color = Color,
        Shortcut = Shortcut,
        MouseButton = MouseButton,
        Preset = Preset,
        TrackpadTap = TrackpadTap,
        Items = Items.Select(i => i.Clone()).ToList(),
    };
}

/// <summary>Raw kind values.</summary>
internal static class RadialKinds
{
    public const string App = "app";
    public const string File = "file";
    public const string Url = "url";
    public const string Shortcut = "shortcut";
    public const string Tool = "tool";
    public const string QuickToggle = "quickToggle";
    public const string WindowLayout = "windowLayout";
    public const string Media = "media";
    public const string Submenu = "submenu";

    public static readonly string[] All = [App, File, Url, Shortcut, Tool, QuickToggle, WindowLayout, Media, Submenu];

    public static string Label(string kind) => kind switch
    {
        App => "Open an app",
        File => "Open a file or folder",
        Url => "Open a link",
        Shortcut => "Press a shortcut",
        Tool => "WinKnife tool",
        QuickToggle => "Quick toggles",
        WindowLayout => "Window layout",
        Media => "Media control",
        Submenu => "Submenu",
        _ => kind,
    };
}

/// <summary>Strings of the feature (spec 04-tools 3.11).</summary>
internal static class RadialStrings
{
    public const string PageTitle = "Radial menu";
    public const string HubDescription = "Opens a wheel of your favorite actions around the pointer";
    public const string PanelCaption = "Your favorite actions on a wheel";
}

/// <summary>Quick toggle / media / layout tables.</summary>
internal static class RadialCatalog
{
    public static readonly string[] QuickToggles = ["darkMode", "emptyTrash", "quitAllApps", "ejectDisks", "hiddenFiles", "desktopIcons", "lockScreen", "displayOff", "screenSaver"];

    public static readonly string[] MediaKeys = ["playPause", "previousTrack", "nextTrack", "nowPlaying"];

    public static string MediaLabel(string key) => key switch
    {
        "playPause" => "Play or pause",
        "previousTrack" => "Previous track",
        "nextTrack" => "Next track",
        "nowPlaying" => "Now Playing",
        _ => "Media control",
    };

    public static string MediaGlyph(string key) => key switch
    {
        "playPause" => "",
        "previousTrack" => "",
        "nextTrack" => "",
        _ => "",
    };

    private static readonly Dictionary<string, (string Title, string Glyph)> ToggleFallback = new()
    {
        ["darkMode"] = ("Switch to dark mode", ""),
        ["emptyTrash"] = ("Empty the Recycle Bin", ""),
        ["quitAllApps"] = ("Quit all apps", ""),
        ["ejectDisks"] = ("Eject all disks", ""),
        ["hiddenFiles"] = ("Show hidden files", ""),
        ["desktopIcons"] = ("Hide desktop icons", ""),
        ["lockScreen"] = ("Lock the screen", ""),
        ["displayOff"] = ("Turn off the display", ""),
        ["screenSaver"] = ("Start the screen saver", ""),
    };

    public static string ToggleTitle(string id)
    {
        var live = QuickToggleOps.Title(id);
        return string.IsNullOrEmpty(live) || live == id ? ToggleFallback.GetValueOrDefault(id).Title ?? id : live;
    }

    public static string ToggleGlyph(string id)
    {
        var live = QuickToggleOps.Glyph(id);
        return string.IsNullOrEmpty(live) ? ToggleFallback.GetValueOrDefault(id).Glyph ?? "" : live;
    }

    /// <summary>Window layout actions in enum order: (raw id, title, role-style id of the Windows project).</summary>
    public static readonly (string Raw, string Title, string Role)[] Layouts =
    [
        ("leftHalf", "Left", "left"), ("rightHalf", "Right", "right"), ("topHalf", "Top", "top"), ("bottomHalf", "Bottom", "bottom"),
        ("centerHalf", "Center half", "centerHalf"), ("leftThird", "Left 1/3", "leftThird"), ("centerThird", "Center 1/3", "centerThird"),
        ("rightThird", "Right 1/3", "rightThird"), ("leftTwoThirds", "Left 2/3", "leftTwoThirds"), ("rightTwoThirds", "Right 2/3", "rightTwoThirds"),
        ("centerTwoThirds", "Center 2/3", "centerTwoThirds"), ("topThird", "Top 1/3", "topThird"), ("middleThird", "Middle 1/3", "middleThird"),
        ("bottomThird", "Bottom 1/3", "bottomThird"), ("topTwoThirds", "Top 2/3", "topTwoThirds"), ("bottomTwoThirds", "Bottom 2/3", "bottomTwoThirds"),
        ("topQuarter", "Top 1/4", "topQuarter"), ("upperMiddleQuarter", "Upper middle 1/4", "upperMiddleQuarter"),
        ("lowerMiddleQuarter", "Lower middle 1/4", "lowerMiddleQuarter"), ("bottomQuarter", "Bottom 1/4", "bottomQuarter"),
        ("leftQuarter", "Left 1/4", "leftQuarter"), ("leftMiddleQuarter", "Left middle 1/4", "leftMiddleQuarter"),
        ("rightMiddleQuarter", "Right middle 1/4", "rightMiddleQuarter"), ("rightQuarter", "Right 1/4", "rightQuarter"),
        ("topLeftSixth", "Top left 1/6", "topLeftSixth"), ("topCenterSixth", "Top center 1/6", "topCenterSixth"),
        ("topRightSixth", "Top right 1/6", "topRightSixth"), ("bottomLeftSixth", "Bottom left 1/6", "bottomLeftSixth"),
        ("bottomCenterSixth", "Bottom center 1/6", "bottomCenterSixth"), ("bottomRightSixth", "Bottom right 1/6", "bottomRightSixth"),
        ("topLeft", "Top left", "topLeft"), ("topRight", "Top right", "topRight"), ("bottomLeft", "Bottom left", "bottomLeft"),
        ("bottomRight", "Bottom right", "bottomRight"), ("maximize", "Maximize", "maximize"),
        ("marginMaximize", "Maximize with Margin", "maximizeWithMargin"), ("fullScreen", "Full Screen", "fullScreen"), ("center", "Center", "center"),
        ("previousDisplay", "Previous display", "previousDisplay"), ("nextDisplay", "Next display", "nextDisplay"), ("restore", "Restore", "restore"),
    ];

    public static string LayoutTitle(string raw) => Layouts.FirstOrDefault(l => l.Raw == raw).Title ?? "Window layout";

    /// <summary>A small glyph for a layout action (no font glyph exists; the rectangles family is approximated).</summary>
    public static string LayoutGlyph(string raw) => raw switch
    {
        "maximize" or "marginMaximize" or "fullScreen" => "",
        "restore" => "",
        "center" => "",
        "previousDisplay" => "",
        "nextDisplay" => "",
        _ => "",
    };
}

/// <summary>The 12 profile colours.</summary>
internal static class RadialColors
{
    public static readonly string[] Ids = ["accent", "blue", "purple", "pink", "red", "orange", "yellow", "green", "mint", "cyan", "indigo", "graphite"];

    public static string Name(string id) => id switch
    {
        "accent" => "Accent", "blue" => "Blue", "purple" => "Purple", "pink" => "Pink", "red" => "Red", "orange" => "Orange",
        "yellow" => "Yellow", "green" => "Green", "mint" => "Mint", "cyan" => "Cyan", "indigo" => "Indigo", "graphite" => "Graphite",
        _ => id,
    };

    public static Color Resolve(string id, bool light)
    {
        static Color F(double r, double g, double b) => Color.FromRgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
        return id switch
        {
            "blue" => light ? F(0, 0.48, 1) : Color.FromRgb(0x0A, 0x84, 0xFF),
            "purple" => light ? F(0.58, 0.20, 0.85) : Color.FromRgb(0xBF, 0x5A, 0xF2),
            "pink" => light ? F(0.88, 0.16, 0.45) : Color.FromRgb(0xFF, 0x37, 0x5F),
            "red" => light ? F(0.85, 0.18, 0.18) : Color.FromRgb(0xFF, 0x45, 0x3A),
            "orange" => light ? F(0.95, 0.45, 0) : Color.FromRgb(0xFF, 0x9F, 0x0A),
            "yellow" => light ? F(0.85, 0.65, 0) : Color.FromRgb(0xFF, 0xD6, 0x0A),
            "green" => light ? F(0.18, 0.65, 0.25) : Color.FromRgb(0x30, 0xD1, 0x58),
            "mint" => light ? F(0, 0.68, 0.60) : Color.FromRgb(0x63, 0xE6, 0xE2),
            "cyan" => light ? F(0.15, 0.65, 0.85) : Color.FromRgb(0x64, 0xD2, 0xFF),
            "indigo" => light ? F(0.35, 0.35, 0.85) : Color.FromRgb(0x5E, 0x5C, 0xE6),
            "graphite" => light ? Color.FromRgb(0x66, 0x66, 0x66) : Color.FromRgb(0xA6, 0xA6, 0xA6),
            _ => ThemeManager.AccentColor,
        };
    }
}

/// <summary>Curated icon list (SF names kept as stable ids, mapped to Segoe Fluent glyphs).</summary>
internal static class RadialSymbols
{
    public static readonly (string Id, string Glyph)[] All =
    [
        ("star.fill", ""), ("heart.fill", ""), ("bolt.fill", ""), ("flame.fill", ""), ("sparkles", "✦"),
        ("folder.fill", ""), ("doc.fill", ""), ("tray.full.fill", ""), ("terminal.fill", ""), ("globe", ""),
        ("envelope.fill", ""), ("message.fill", ""), ("music.note", ""), ("headphones", ""), ("camera.fill", ""),
        ("photo.fill", ""), ("video.fill", ""), ("gamecontroller.fill", ""), ("calendar", ""), ("clock.fill", ""),
        ("house.fill", ""), ("cart.fill", ""), ("hammer.fill", ""), ("paintbrush.fill", ""), ("book.fill", ""),
        ("keyboard", ""), ("magnifyingglass", ""), ("airplane", ""), ("checkmark.circle.fill", ""), ("xmark.circle.fill", ""),
        ("plus.circle.fill", ""), ("minus.circle.fill", ""), ("exclamationmark.triangle.fill", ""), ("questionmark.circle.fill", ""),
        ("info.circle.fill", ""), ("lock.fill", ""), ("lock.open.fill", ""), ("key.fill", ""), ("person.fill", ""),
        ("person.2.fill", ""), ("bell.fill", ""), ("flag.fill", ""), ("bookmark.fill", ""), ("tag.fill", ""),
        ("paperclip", ""), ("link", ""), ("scissors", ""), ("doc.on.clipboard", ""), ("square.and.arrow.up", ""),
        ("square.and.arrow.down", ""), ("trash.fill", ""), ("archivebox.fill", ""), ("externaldrive.fill", ""),
        ("internaldrive.fill", ""), ("display", ""), ("desktopcomputer", ""), ("laptopcomputer", ""), ("iphone", ""),
        ("ipad", ""), ("applewatch", ""), ("wifi", ""), ("network", ""), ("antenna.radiowaves.left.and.right", ""),
        ("speaker.wave.2.fill", ""), ("mic.fill", ""), ("waveform", ""), ("play.fill", ""), ("pause.fill", ""),
        ("stop.fill", ""), ("backward.fill", ""), ("forward.fill", ""), ("shuffle", ""), ("repeat", ""),
        ("sun.max.fill", ""), ("moon.fill", ""), ("lightbulb.fill", ""), ("battery.100", ""), ("power", ""),
        ("eye.fill", ""), ("eye.slash.fill", ""), ("location.fill", ""), ("map.fill", ""), ("paperplane.fill", ""),
        ("bubble.left.fill", ""), ("phone.fill", ""), ("gearshape.fill", ""), ("slider.horizontal.3", ""), ("switch.2", ""),
        ("command", "⌘"), ("printer.fill", ""), ("textformat", ""), ("number", "#"),
    ];

    private static readonly Dictionary<string, string> Lookup = All.ToDictionary(s => s.Id, s => s.Glyph);

    public static string? GlyphOf(string id) => Lookup.GetValueOrDefault(id);
}

/// <summary>Presets, sanitising, JSON and display names (spec 04-tools 3.2 - 3.5).</summary>
internal static class RadialModel
{
    public const int MaxItems = 12;
    public const int MaxDepth = 2;
    public const string StoreKey = "radialMenuProfiles";

    public static readonly string[] PresetIds = ["general", "media", "tools", "windowLayout", "quickToggles", "blank"];

    public static string PresetTitle(string id) => id switch
    {
        "general" => "General", "media" => "Media", "tools" => "Tools", "windowLayout" => "Window layout", "quickToggles" => "Quick toggles", _ => "Blank",
    };

    public static string PresetColor(string id) => id switch
    {
        "media" => "purple", "tools" => "cyan", "windowLayout" => "orange", "quickToggles" => "mint", "blank" => "graphite", _ => "accent",
    };

    private static RadialItem Item(string kind, string payload, string symbol = "") => new() { Kind = kind, Payload = payload, SymbolName = symbol };

    public static List<RadialItem> MakeItems(string preset) => preset switch
    {
        "general" =>
        [
            Item(RadialKinds.Media, "playPause"), Item(RadialKinds.Media, "nextTrack"), Item(RadialKinds.Tool, "screenshot"),
            Item(RadialKinds.File, "~/Downloads"), Item(RadialKinds.Tool, "colorPicker"), Item(RadialKinds.Media, "previousTrack"),
        ],
        "media" => [Item(RadialKinds.Media, "playPause"), Item(RadialKinds.Media, "nextTrack"), Item(RadialKinds.Media, "nowPlaying"), Item(RadialKinds.Media, "previousTrack")],
        "tools" =>
        [
            Item(RadialKinds.Tool, "screenshot"), Item(RadialKinds.Tool, "colorPicker"), Item(RadialKinds.Tool, "screenOCR"),
            Item(RadialKinds.Tool, "screenRecorder"), Item(RadialKinds.Tool, "micMute"), Item(RadialKinds.Tool, "scratchpad"),
        ],
        "windowLayout" =>
        [
            Item(RadialKinds.WindowLayout, "maximize"), Item(RadialKinds.WindowLayout, "rightHalf"), Item(RadialKinds.WindowLayout, "bottomHalf"),
            Item(RadialKinds.WindowLayout, "leftHalf"), Item(RadialKinds.WindowLayout, "topHalf"),
        ],
        "quickToggles" =>
        [
            Item(RadialKinds.QuickToggle, "darkMode"), Item(RadialKinds.QuickToggle, "desktopIcons"), Item(RadialKinds.QuickToggle, "hiddenFiles"),
            Item(RadialKinds.QuickToggle, "lockScreen"), Item(RadialKinds.QuickToggle, "emptyTrash"),
        ],
        _ => [],
    };

    private static RadialProfile? _seed;

    /// <summary>The shipped first profile (item ids are generated once per run).</summary>
    public static RadialProfile Seed(IContributionServices? ctx)
    {
        if (_seed is null)
        {
            var hotkey = ctx?.Shortcuts.GetHotkey("radialMenu");
            _seed = new RadialProfile { Name = "", Color = "accent", Shortcut = hotkey?.ToString() ?? "Ctrl+Alt+Shift+Space", Preset = "general", Items = MakeItems("general") };
        }

        return _seed.Clone();
    }

    /// <summary>Reads the stored profiles (seeded when missing / empty).</summary>
    public static List<RadialProfile> Load(IContributionServices ctx)
    {
        var json = ctx.App.Get<string>(StoreKey, "");
        var list = Decode(json);
        return list.Count == 0 ? [Seed(ctx)] : list;
    }

    /// <summary>Sanitises and stores the profiles. Returns the sanitised list.</summary>
    public static List<RadialProfile> Save(IContributionServices ctx, List<RadialProfile> profiles)
    {
        var clean = SanitizeProfiles(profiles);
        ctx.App.Set(StoreKey, Encode(clean));
        return clean;
    }

    // ---------------------------------------------------------------- sanitising

    public static List<RadialProfile> SanitizeProfiles(IEnumerable<RadialProfile> profiles)
    {
        var result = new List<RadialProfile>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var buttons = new HashSet<string>();
        var tapTaken = false;
        foreach (var p in profiles)
        {
            if (string.IsNullOrWhiteSpace(p.Id) || !ids.Add(p.Id))
            {
                p.Id = Guid.NewGuid().ToString().ToUpperInvariant();
                ids.Add(p.Id);
            }

            p.Name = Clip(p.Name.Trim(), 60);
            p.Color = RadialColors.Ids.Contains(p.Color) ? p.Color : "accent";
            p.Shortcut = Hotkey.TryParse(p.Shortcut, out var hk) && (hk.Modifiers != HotkeyModifiers.None || hk.Key is >= Key.F1 and <= Key.F24) ? hk.ToString() : "";
            p.MouseButton = p.MouseButton is "back" or "forward" ? p.MouseButton : (p.MouseButton.StartsWith("button:", StringComparison.Ordinal) && int.TryParse(p.MouseButton[7..], out var n) && n is >= 3 and <= 31 ? p.MouseButton : "off");
            if (p.MouseButton != "off" && !buttons.Add(p.MouseButton))
            {
                p.MouseButton = "off";
            }

            if (p.TrackpadTap)
            {
                if (tapTaken)
                {
                    p.TrackpadTap = false;
                }

                tapTaken = true;
            }

            p.Items = Sanitize(p.Items, 0);
            result.Add(p);
        }

        if (result.Count == 0)
        {
            result.Add(new RadialProfile { Shortcut = "Ctrl+Alt+Shift+Space", Preset = "general", Items = MakeItems("general") });
        }

        return result;
    }

    public static List<RadialItem> Sanitize(IEnumerable<RadialItem> items, int depth)
    {
        var result = new List<RadialItem>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (result.Count >= MaxItems)
            {
                break;
            }

            if (item.Kind == RadialKinds.Submenu && depth + 1 >= MaxDepth)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id))
            {
                if (string.IsNullOrWhiteSpace(item.Id))
                {
                    item.Id = Guid.NewGuid().ToString().ToUpperInvariant();
                    ids.Add(item.Id);
                }
                else
                {
                    continue;
                }
            }

            item.Name = Clip(item.Name.Trim(), 60);
            item.Payload = item.Payload.Trim();
            if (!IsValidPayload(item))
            {
                continue;
            }

            if (item.Kind == RadialKinds.Url)
            {
                item.Payload = NormalizedUrl(item.Payload) ?? item.Payload;
            }

            if (item.CustomIconData is { } data)
            {
                if (item.Kind != RadialKinds.Url || data.Length > 65536 * 4 / 3 + 8 || !IsImageData(data))
                {
                    item.CustomIconData = null;
                }
            }

            item.Children = item.Kind == RadialKinds.Submenu ? Sanitize(item.Children, depth + 1) : [];
            result.Add(item);
        }

        return result;
    }

    private static bool IsImageData(string base64)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64);
            return bytes.Length is > 0 and <= 65536;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsValidPayload(RadialItem item) => item.Kind switch
    {
        RadialKinds.App or RadialKinds.File => item.Payload.Length > 0,
        RadialKinds.Url => NormalizedUrl(item.Payload) is not null,
        RadialKinds.Shortcut => Hotkey.TryParse(item.Payload, out var hk) && (hk.Modifiers != HotkeyModifiers.None || hk.Key is >= Key.F1 and <= Key.F24),
        RadialKinds.Tool => ToolActions.RadialOrder.Contains(item.Payload),
        RadialKinds.QuickToggle => RadialCatalog.QuickToggles.Contains(item.Payload),
        RadialKinds.WindowLayout => RadialCatalog.Layouts.Any(l => l.Raw == item.Payload),
        RadialKinds.Media => RadialCatalog.MediaKeys.Contains(item.Payload),
        RadialKinds.Submenu => true,
        _ => false,
    };

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];

    /// <summary>The URL normaliser of spec 3.2.3 (null = invalid).</summary>
    public static string? NormalizedUrl(string raw)
    {
        var text = raw.Trim();
        if (text.Length == 0 || text.Contains(' '))
        {
            return null;
        }

        var hasScheme = false;
        if (text.Contains("://", StringComparison.Ordinal))
        {
            hasScheme = true;
        }
        else
        {
            var colon = text.IndexOf(':');
            if (colon > 0 && char.IsLetter(text[0]))
            {
                var before = text[..colon];
                var after = text[(colon + 1)..];
                var schemeLike = before.All(c => char.IsLetterOrDigit(c) || c is '+' or '-' or '.');
                var portLike = after.Length > 0 && after.TakeWhile(char.IsDigit).Count() > 0 && (after.All(char.IsDigit) || after.StartsWith('/') || after.TakeWhile(char.IsDigit).Count() == after.Length || after.SkipWhile(char.IsDigit).FirstOrDefault() == '/');
                var hostLike = before.Contains('.') || before.Equals("localhost", StringComparison.OrdinalIgnoreCase);
                hasScheme = schemeLike && !(portLike && hostLike);
            }
        }

        if (hasScheme)
        {
            if (Uri.TryCreate(text, UriKind.Absolute, out var uri))
            {
                if (uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.Host))
                {
                    return null;
                }

                return text;
            }

            return null;
        }

        var candidate = "https://" + text;
        return Uri.TryCreate(candidate, UriKind.Absolute, out var u) && !string.IsNullOrEmpty(u.Host) ? candidate : null;
    }

    // ---------------------------------------------------------------- JSON

    public static string Encode(IEnumerable<RadialProfile> profiles)
    {
        var array = new JsonArray();
        foreach (var p in profiles)
        {
            var o = new JsonObject
            {
                ["id"] = p.Id,
                ["name"] = p.Name,
                ["color"] = p.Color,
                ["shortcut"] = p.Shortcut,
                ["mouseButton"] = p.MouseButton,
                ["trackpadTap"] = p.TrackpadTap,
            };
            if (p.Preset is not null)
            {
                o["preset"] = p.Preset;
            }

            o["items"] = EncodeItems(p.Items);
            array.Add(o);
        }

        return array.ToJsonString();
    }

    private static JsonArray EncodeItems(IEnumerable<RadialItem> items)
    {
        var array = new JsonArray();
        foreach (var i in items)
        {
            var o = new JsonObject
            {
                ["id"] = i.Id,
                ["kind"] = i.Kind,
                ["name"] = i.Name,
                ["symbolName"] = i.SymbolName,
                ["payload"] = i.Payload,
            };
            if (i.CustomIconData is not null)
            {
                o["customIconData"] = i.CustomIconData;
            }

            o["children"] = EncodeItems(i.Children);
            array.Add(o);
        }

        return array;
    }

    public static List<RadialProfile> Decode(string? json)
    {
        var result = new List<RadialProfile>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }

        try
        {
            if (JsonNode.Parse(json) is not JsonArray array)
            {
                return result;
            }

            foreach (var node in array)
            {
                if (node is not JsonObject o)
                {
                    continue;
                }

                var p = new RadialProfile
                {
                    Id = Str(o, "id") is { Length: > 0 } id ? id : Guid.NewGuid().ToString().ToUpperInvariant(),
                    Name = Str(o, "name") ?? "",
                    Color = Str(o, "color") ?? "accent",
                    Shortcut = Str(o, "shortcut") ?? "",
                    MouseButton = Str(o, "mouseButton") ?? "off",
                    Preset = Str(o, "preset"),
                    TrackpadTap = o["trackpadTap"] is JsonValue v && v.TryGetValue<bool>(out var b) && b,
                    Items = DecodeItems(o["items"] as JsonArray),
                };
                result.Add(p);
            }
        }
        catch (JsonException)
        {
            return [];
        }

        return SanitizeProfiles(result);
    }

    private static List<RadialItem> DecodeItems(JsonArray? array)
    {
        var list = new List<RadialItem>();
        if (array is null)
        {
            return list;
        }

        foreach (var node in array)
        {
            if (node is not JsonObject o || Str(o, "kind") is not { } kind || !RadialKinds.All.Contains(kind))
            {
                continue;
            }

            list.Add(new RadialItem
            {
                Id = Str(o, "id") is { Length: > 0 } id ? id : Guid.NewGuid().ToString().ToUpperInvariant(),
                Kind = kind,
                Name = Str(o, "name") ?? "",
                SymbolName = Str(o, "symbolName") ?? "",
                Payload = Str(o, "payload") ?? "",
                CustomIconData = Str(o, "customIconData"),
                Children = DecodeItems(o["children"] as JsonArray),
            });
        }

        return list;
    }

    private static string? Str(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    // ---------------------------------------------------------------- display

    public static string Expand(string path)
    {
        var p = path.Trim();
        if (p == "~" || p.StartsWith("~/", StringComparison.Ordinal) || p.StartsWith("~\\", StringComparison.Ordinal))
        {
            p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), p.Length > 2 ? p[2..] : "");
        }

        return Environment.ExpandEnvironmentVariables(p);
    }

    public static string DisplayName(RadialItem item, string? nowPlayingLabel = null)
    {
        if (item.Name.Length > 0)
        {
            return item.Name;
        }

        switch (item.Kind)
        {
            case RadialKinds.App or RadialKinds.File:
                return FileDisplayName(item.Payload);
            case RadialKinds.Url:
                return Uri.TryCreate(NormalizedUrl(item.Payload) ?? item.Payload, UriKind.Absolute, out var u) && u.Host.Length > 0 ? u.Host : item.Payload;
            case RadialKinds.Shortcut:
                return Hotkey.TryParse(item.Payload, out var hk) ? hk.ToString() : "Press a shortcut";
            case RadialKinds.Tool:
                return ToolActions.RadialOrder.Contains(item.Payload) ? ToolActions.TitleOf(item.Payload) : "WinKnife tool";
            case RadialKinds.QuickToggle:
                return RadialCatalog.QuickToggles.Contains(item.Payload) ? RadialCatalog.ToggleTitle(item.Payload) : "Quick toggles";
            case RadialKinds.WindowLayout:
                return RadialCatalog.Layouts.Any(l => l.Raw == item.Payload) ? RadialCatalog.LayoutTitle(item.Payload) : "Window layout";
            case RadialKinds.Media:
                return item.Payload == "nowPlaying" && nowPlayingLabel is not null ? nowPlayingLabel : RadialCatalog.MediaLabel(item.Payload);
            default:
                return "Submenu";
        }
    }

    private static readonly Dictionary<string, string> NameCache = new(StringComparer.OrdinalIgnoreCase);

    public static string FileDisplayName(string payload)
    {
        if (NameCache.TryGetValue(payload, out var cached))
        {
            return cached;
        }

        var path = Expand(payload);
        string name;
        if (payload.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            name = payload[(payload.LastIndexOf('\\') + 1)..];
        }
        else
        {
            name = Path.GetFileNameWithoutExtension(path.TrimEnd('\\', '/'));
            if (string.IsNullOrEmpty(name))
            {
                name = path;
            }

            try
            {
                if (File.Exists(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    && System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 0 } desc)
                {
                    name = desc;
                }
            }
            catch
            {
                // keep the file name
            }
        }

        NameCache[payload] = name;
        return name;
    }

    public static void ForgetName(string payload) => NameCache.Remove(payload);

    /// <summary>Default glyph of an item (symbol override first).</summary>
    public static string GlyphOf(RadialItem item)
    {
        if (item.SymbolName.Length > 0 && RadialSymbols.GlyphOf(item.SymbolName) is { } custom)
        {
            return custom;
        }

        return item.Kind switch
        {
            RadialKinds.App => "",
            RadialKinds.File => "",
            RadialKinds.Url => "",
            RadialKinds.Shortcut => "⌘",
            RadialKinds.Tool => string.IsNullOrEmpty(FeatureCatalog.Get(ToolActions.FeatureOf(item.Payload))?.Glyph) ? "" : FeatureCatalog.Get(ToolActions.FeatureOf(item.Payload))!.Glyph,
            RadialKinds.QuickToggle => RadialCatalog.ToggleGlyph(item.Payload),
            RadialKinds.WindowLayout => RadialCatalog.LayoutGlyph(item.Payload),
            RadialKinds.Media => RadialCatalog.MediaGlyph(item.Payload),
            _ => "",
        };
    }
}

/// <summary>Wheel geometry (spec 3.6).</summary>
internal static class RadialGeometry
{
    public const double PanelSize = 400;
    public const double WheelDiameter = 300;
    public const double RingRadius = 112;
    public const double ChipSize = 52;
    public const double HubDiameter = 76;
    public const double DeadZone = 40;
    public const double MoveActivation = 8;

    /// <summary>Slice index for a pointer offset (dx right, dyUp up); null inside the dead zone.</summary>
    public static int? SliceAt(double dx, double dyUp, int count, double deadZone = DeadZone, bool ignoreDeadZone = false)
    {
        if (count == 0)
        {
            return null;
        }

        if (!ignoreDeadZone && Math.Sqrt(dx * dx + dyUp * dyUp) < deadZone)
        {
            return null;
        }

        var angle = Math.Atan2(dx, dyUp);
        if (angle < 0)
        {
            angle += 2 * Math.PI;
        }

        var step = 2 * Math.PI / count;
        var index = (int)(((angle + step / 2) % (2 * Math.PI)) / step);
        return Math.Clamp(index, 0, count - 1);
    }

    /// <summary>Chip centre offset (screen coordinates, y down).</summary>
    public static (double X, double Y) ChipOffset(int index, int count, double radius)
    {
        var theta = 2 * Math.PI * index / count;
        return (Math.Sin(theta) * radius, -Math.Cos(theta) * radius);
    }
}
