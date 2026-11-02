// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
using WinKnife.Core;

namespace WinKnife.Features.Island;

/// <summary>Static description of an island section ("module", spec 03 Part B section 2).</summary>
internal sealed record ModuleDef(
    string Id,
    string Title,
    string Glyph,
    char Key,
    Color Tint,
    bool DarkGlyph,
    string Summary,
    string[] Features,
    string? EnableKey,
    bool TopRow);

/// <summary>A shortcut / level item of the Controls page (<c>NotchControlItem</c>).</summary>
internal sealed record ControlDef(string Id, string Title, string Glyph, bool IsLevel, string[] Features);

/// <summary>Module and control tables plus availability rules.</summary>
internal static class IslandCatalog
{
    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static readonly IReadOnlyList<ModuleDef> Modules =
    [
        new("controls", "Controls", G.Controls, 'c', C("#0A84FF"), false, "Playback, volume, brightness and your shortcuts.", [], null, true),
        new("mixer", "Volume mixer", G.Mixer, 'v', C("#BF5AF2"), false, "The volume of each app, and where sound plays.", ["mixer"], null, true),
        new("music", "Now Playing", G.Music, 'm', C("#FF375F"), false, "The song playing now, with its controls.", [], null, true),
        new("clipboard", "Clipboard", G.Clipboard, 'b', C("#AC8E68"), false, "What you copied recently, ready to paste again.", ["clipboard-history"], null, true),
        new("captures", "Recent captures", G.Camera, 's', C("#5E5CE6"), false, "Your latest screenshots and recordings.", ["screenshot", "screen-recorder", "screen-ocr", "color-picker"], null, true),
        new("files", "Files", G.Files, 'f', C("#64D2FF"), false, "A shelf for the files you drop on the island.", ["shelf"], null, true),
        new("system", "System", G.System, 'i', C("#30D158"), false, "CPU, memory, disk, network and battery at a glance.", ["monitor-cpu", "monitor-gpu", "monitor-memory", "monitor-network", "monitor-disk", "monitor-power", "connected-devices"], null, false),
        new("tools", "Tools", G.Tools, 't', C("#98989D"), false, "Tools and utilities, one click away.", ["quick-launcher"], null, true),
        new("calendar", "Calendar", G.Calendar, 'a', C("#FF453A"), false, "Today's events and the month ahead.", ["notch-calendar"], K.CalendarEnabled, true),
        new("notifications", "Notifications", G.Bell, 'n', C("#FF9F0A"), false, "Your PC's notifications, kept in the island.", ["notch-notifications"], K.NotificationsEnabled, true),
        new("timer", "Timer", G.Timer, 'r', C("#63E6E2"), false, "Timer, Pomodoro and stopwatch.", ["notch-timer"], K.TimerEnabled, true),
        new("camera", "Camera mirror", G.Video, 'w', C("#40C8E0"), false, "A mirror to check yourself before a call.", ["camera-preview"], K.CameraEnabled, true),
        new("downloads", "Downloads", G.Download, 'd', C("#0A84FF"), false, "Downloads in progress and just finished.", ["notch-downloads"], null, true),
        new("scratchpad", "Scratchpad", G.Note, 'p', C("#FFD60A"), true, "Quick notes that save by themselves.", ["scratchpad"], null, true),
        new("agents", "AI Agents", G.Sparkle, 'g', C("#D97857"), false, "Claude Code, Codex, OpenCode and GitHub Copilot usage, limits and costs.", ["notch-agents"], K.AgentsEnabled, true),
        new("watch", "Watch", G.Eye, 'o', C("#BF5AF2"), false, "Any part of a window, read live, with an alert when it changes.", ["notch-watch"], K.WatchEnabled, true),
    ];

    public static readonly IReadOnlyList<ControlDef> Controls =
    [
        new("volume", "Volume", G.Speaker, true, ["mixer"]),
        new("brightness", "Brightness", G.Brightness, true, ["brightness"]),
        new("keyboardLight", "Keyboard light", G.Brightness, true, ["brightness"]),
        new("music", "Now Playing", G.Music, false, []),
        new("mixer", "Volume mixer", G.Mixer, false, ["mixer"]),
        new("keepAwake", "Keep awake", G.Cup, false, ["keep-awake"]),
        new("timer", "Timer", G.Timer, false, ["notch-timer"]),
        new("calendar", "Calendar", G.Calendar, false, ["notch-calendar"]),
        new("microphone", "Mute microphone", G.Mic, false, ["mic-mute"]),
        new("screenshot", "Screenshot", G.Camera, false, ["screenshot"]),
        new("recording", "Screen recording", G.Record, false, ["screen-recorder"]),
        new("speedTest", "Speed test", G.Gauge, false, ["monitor-network"]),
        new("panel", "Open app panel", G.Panel, false, []),
        new("commandBar", "Command Bar", G.Command, false, ["command-bar"]),
        new("scratchpad", "Scratchpad", G.Note, false, ["scratchpad"]),
    ];

    public static ModuleDef? Get(string id) => Modules.FirstOrDefault(m => m.Id == id);

    public static ControlDef? Control(string id) => Controls.FirstOrDefault(c => c.Id == id);

    /// <summary>Stored order followed by the remaining modules in default order (spec 02d 3.4).</summary>
    public static List<string> OrderedIds(IAppSettings app, IEnumerable<string>? extra = null)
    {
        var result = new List<string>();
        foreach (var id in K.Csv(app.Get<string>(K.ModuleOrder)))
        {
            if (Get(id) is not null && !result.Contains(id))
            {
                result.Add(id);
            }
        }

        foreach (var m in Modules)
        {
            if (!result.Contains(m.Id))
            {
                result.Add(m.Id);
            }
        }

        if (extra is not null)
        {
            foreach (var id in extra)
            {
                if (!result.Contains(id))
                {
                    result.Add(id);
                }
            }
        }

        return result;
    }

    /// <summary>The effective order of the Controls shortcut items.</summary>
    public static List<string> ControlOrder(IAppSettings app)
    {
        var result = new List<string>();
        foreach (var id in K.Csv(app.Get<string>(K.ControlOrder)))
        {
            if (Control(id) is not null && !result.Contains(id))
            {
                result.Add(id);
            }
        }

        foreach (var c in Controls)
        {
            if (!result.Contains(c.Id))
            {
                result.Add(c.Id);
            }
        }

        return result;
    }

    /// <summary>Why a module cannot be shown (null = available): installed features only; per-module enable keys are separate.</summary>
    public static bool FeatureAvailable(IFeatureRuntime runtime, ModuleDef module)
    {
        if (module.Features.Length == 0)
        {
            return true;
        }

        return module.Id is "captures" or "system"
            ? runtime.AnyAvailable(module.Features)
            : runtime.IsAvailable(module.Features[0]);
    }

    /// <summary>Title of the first missing feature of a module (for the "Enable ... in Features." reason).</summary>
    public static string? MissingFeatureTitle(IFeatureRuntime runtime, string[] features)
    {
        foreach (var f in features)
        {
            if (!runtime.IsAvailable(f))
            {
                return FeatureCatalog.Get(f)?.Title ?? f;
            }
        }

        return null;
    }
}

/// <summary>Closed-island activities in priority order (spec 3.2).</summary>
internal enum Activity
{
    Timer,
    Watch,
    Downloads,
    Agents,
    Calendar,
    Music,
    KeepAwake,
}

/// <summary>A transient notice event (spec 7.7).</summary>
internal sealed record NoticeEvent(string Id, string PrefKey, int Priority, double Seconds, string OpensModule);

internal static class NoticeEvents
{
    public static readonly NoticeEvent Volume = new("volume", K.Volume, 3, 1.6, "controls");
    public static readonly NoticeEvent Brightness = new("brightness", K.Brightness, 3, 1.6, "controls");
    public static readonly NoticeEvent KeyboardLight = new("keyboardLight", K.KeyboardLight, 3, 1.6, "controls");
    public static readonly NoticeEvent Microphone = new("microphone", K.Microphone, 3, 1.6, "mixer");
    public static readonly NoticeEvent InputSource = new("inputSource", K.InputSource, 3, 1.6, "controls");
    public static readonly NoticeEvent Capture = new("capture", K.Capture, 2, 12, "controls");
    public static readonly NoticeEvent Timer = new("timer", K.TimerEnabled, 2, 6, "timer");
    public static readonly NoticeEvent Watch = new("watch", K.WatchEnabled, 2, 6, "watch");
    public static readonly NoticeEvent Battery = new("battery", K.Battery, 1, 4, "controls");
    public static readonly NoticeEvent SystemNotification = new("systemNotification", K.NotificationsEnabled, 1, 3, "notifications");
    public static readonly NoticeEvent Accessory = new("accessory", K.Accessories, 1, 4, "system");
    public static readonly NoticeEvent Agents = new("agents", K.AgentsEnabled, 1, 5, "agents");
    public static readonly NoticeEvent ClipboardCopy = new("clipboard", K.Clipboard, 0, 2.5, "clipboard");
    public static readonly NoticeEvent Download = new("download", K.DownloadsEnabled, 0, 6, "downloads");
    public static readonly NoticeEvent Track = new("track", K.TrackChange, 0, 3, "music");
    public static readonly NoticeEvent Unlock = new("unlock", K.LockScreen, 2, 1.4, "controls");
}

/// <summary>A notice shown on the closed island (wings + optional level meter, banner or companion reaction).</summary>
internal sealed class Notice
{
    public required NoticeEvent Event { get; init; }

    public string Glyph { get; set; } = "";

    public string Title { get; set; } = "";

    public string Detail { get; set; } = "";

    /// <summary>0..1 level meter (volume, brightness, battery...), null = text notice.</summary>
    public double? Level { get; set; }

    public System.Windows.Media.ImageSource? Image { get; set; }

    public Color? Tint { get; set; }

    /// <summary>A complete element supplied by another project (the AI Agents notice).</summary>
    public System.Windows.FrameworkElement? Element { get; set; }

    public string? Reaction { get; set; }

    /// <summary>Opens this module when clicked (default: the event's module).</summary>
    public string? OpensModule { get; set; }

    public Action? OnClick { get; set; }

    public bool Banner { get; set; }

    public string? FullBody { get; set; }

    public string Key => $"{Event.Id}|{Title}|{Detail}|{Reaction}";
}

// ------------------------------------------------------------------------------------------------ quick access layout

internal enum QuickSide
{
    Left,
    Right,
    Bottom,
}

internal sealed class QuickButton
{
    [JsonPropertyName("actionID")]
    public string ActionId { get; set; } = "";

    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("side")]
    public string Side { get; set; } = "left";

    [JsonIgnore]
    public QuickSide SideValue
    {
        get => Side switch { "right" => QuickSide.Right, "bottom" => QuickSide.Bottom, _ => QuickSide.Left };
        set => Side = value switch { QuickSide.Right => "right", QuickSide.Bottom => "bottom", _ => "left" };
    }
}

internal sealed class QuickAccessLayout
{
    public const int MaxPerSide = 3;

    [JsonPropertyName("buttons")]
    public List<QuickButton> Buttons { get; set; } = [];

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    public static QuickAccessLayout Initial() => new()
    {
        Buttons =
        [
            new() { Id = "00000000-0000-4000-8000-000000000001", ActionId = "explore", SideValue = QuickSide.Left },
            new() { Id = "00000000-0000-4000-8000-000000000002", ActionId = "timer", SideValue = QuickSide.Left },
            new() { Id = "00000000-0000-4000-8000-000000000003", ActionId = "settings", SideValue = QuickSide.Right },
            new() { Id = "00000000-0000-4000-8000-000000000004", ActionId = "mixer", SideValue = QuickSide.Right },
            new() { Id = "00000000-0000-4000-8000-000000000005", ActionId = "music", SideValue = QuickSide.Bottom },
        ],
    };

    public static QuickAccessLayout Load(IAppSettings app)
    {
        var text = app.Get<string>(K.QuickAccessLayout);
        if (!string.IsNullOrWhiteSpace(text) && text.Length <= 32768)
        {
            try
            {
                var layout = JsonSerializer.Deserialize<QuickAccessLayout>(text);
                if (layout is { Version: 1 })
                {
                    return layout.Sanitized();
                }
            }
            catch
            {
                // fall through to the default layout
            }
        }

        return Initial();
    }

    public void Save(IAppSettings app) =>
        app.Set(K.QuickAccessLayout, JsonSerializer.Serialize(Sanitized()));

    public static bool IsKnownAction(string id) =>
        id is "explore" or "settings" or "pin"
        || IslandCatalog.Get(id) is not null
        || (id.StartsWith("control.", StringComparison.Ordinal) && IslandCatalog.Control(id[8..]) is { IsLevel: false });

    public QuickAccessLayout Sanitized()
    {
        var seen = new HashSet<string>();
        var perSide = new Dictionary<string, int>();
        var result = new List<QuickButton>();
        foreach (var b in Buttons.Take(64))
        {
            if (!seen.Add(b.Id) || !IsKnownAction(b.ActionId))
            {
                continue;
            }

            perSide.TryGetValue(b.Side, out var n);
            if (n >= MaxPerSide)
            {
                continue;
            }

            perSide[b.Side] = n + 1;
            b.Label = b.Label.Replace('\n', ' ').Replace('\r', ' ').Trim();
            if (b.Label.Length > 40)
            {
                b.Label = b.Label[..40];
            }

            result.Add(b);
        }

        return new QuickAccessLayout { Buttons = result };
    }

    public IEnumerable<QuickButton> On(QuickSide side) => Buttons.Where(b => b.SideValue == side);

    public bool HasRoom(QuickSide side) => On(side).Count() < MaxPerSide;

    public bool Move(string id, QuickSide side, string? before = null)
    {
        var b = Buttons.FirstOrDefault(x => x.Id == id);
        if (b is null || (b.SideValue != side && !HasRoom(side)))
        {
            return false;
        }

        Buttons.Remove(b);
        b.SideValue = side;
        var index = before is null ? -1 : Buttons.FindIndex(x => x.Id == before);
        if (index < 0)
        {
            Buttons.Add(b);
        }
        else
        {
            Buttons.Insert(index, b);
        }

        return true;
    }

    public bool Reorder(string id, int by)
    {
        var b = Buttons.FirstOrDefault(x => x.Id == id);
        if (b is null)
        {
            return false;
        }

        var same = On(b.SideValue).ToList();
        var i = same.IndexOf(b);
        var j = i + by;
        if (j < 0 || j >= same.Count)
        {
            return false;
        }

        var a = Buttons.IndexOf(same[i]);
        var c = Buttons.IndexOf(same[j]);
        (Buttons[a], Buttons[c]) = (Buttons[c], Buttons[a]);
        return true;
    }
}
