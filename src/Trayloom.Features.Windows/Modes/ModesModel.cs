// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json.Serialization;

namespace Trayloom.Features.WindowTools.Modes;

/// <summary>
/// What a mode item opens. <see cref="Unknown"/> is never written: it is what a kind this version does not know reads back as, so
/// <see cref="ModesMath.Sanitize"/> can drop that one item instead of the whole file failing to load.
/// </summary>
internal enum ModeItemKind
{
    Unknown = -1,
    App,
    Web,
    Path,
}

/// <summary>One thing a mode opens: an app, a group of browser tabs (one window) or a folder or file (spec 08 section 2.2).</summary>
internal sealed class ModeItem
{
    public string Id { get; set; } = string.Empty;

    public ModeItemKind Kind { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>App: lower-cased full path of the executable. Null for a packaged app.</summary>
    public string? Exe { get; set; }

    /// <summary>App: the command line after the executable, as it is passed to the process.</summary>
    public string Args { get; set; } = string.Empty;

    /// <summary>App: working directory; empty means the executable's own folder.</summary>
    public string Dir { get; set; } = string.Empty;

    /// <summary>App: AppUserModelId of a packaged app, started through the shell.</summary>
    public string? Aumid { get; set; }

    /// <summary>Web: <c>"default"</c> or the lower-cased path of a browser executable.</summary>
    public string Browser { get; set; } = "default";

    /// <summary>Web: Chromium profile folder name (<c>Default</c>, <c>Profile 1</c>); empty for the browser's own choice.</summary>
    public string Profile { get; set; } = string.Empty;

    /// <summary>Web: the addresses of one window, http and https only.</summary>
    public List<string> Urls { get; set; } = [];

    /// <summary>Path: a folder or a file.</summary>
    public string? Path { get; set; }

    /// <summary>A <see cref="LayoutAction"/> raw value (halves, thirds, corners or <c>maximize</c>); null leaves the window where it opens.</summary>
    public string? Zone { get; set; }

    /// <summary>The item that is brought to the front when the mode starts (exactly one per mode).</summary>
    public bool Front { get; set; }

    public ModeItem Clone()
    {
        var copy = (ModeItem)MemberwiseClone();
        copy.Urls = [.. Urls];
        return copy;
    }
}

/// <summary>What a mode does when it starts, besides opening its items.</summary>
internal sealed class ModeEnterActions
{
    /// <summary><c>off</c>, <c>60</c> (minutes), <c>120</c> or <c>on</c> (until the mode is left).</summary>
    public string KeepAwake { get; set; } = "off";

    /// <summary>-1 leaves the volume as it is, 0 to 100 sets the master volume (0 also mutes).</summary>
    public int Volume { get; set; } = -1;

    /// <summary>Opens the island timer.</summary>
    public bool Timer { get; set; }
}

/// <summary>A workspace: a named, coloured list of items plus what happens when it starts and when it is left.</summary>
internal sealed class Mode
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>A Segoe Fluent Icons glyph from <see cref="ModesMath.Glyphs"/>.</summary>
    public string Glyph { get; set; } = "";

    /// <summary>A name from <see cref="ModesMath.Colors"/>.</summary>
    public string Color { get; set; } = "blue";

    public ModeEnterActions OnEnter { get; set; } = new();

    /// <summary><c>keep</c>, <c>minimize</c> or <c>close</c> (only the windows the mode opened itself).</summary>
    public string OnLeave { get; set; } = "minimize";

    public List<ModeItem> Items { get; set; } = [];

    public Mode Clone()
    {
        var copy = (Mode)MemberwiseClone();
        copy.OnEnter = new ModeEnterActions { KeepAwake = OnEnter.KeepAwake, Volume = OnEnter.Volume, Timer = OnEnter.Timer };
        copy.Items = [.. Items.Select(i => i.Clone())];
        return copy;
    }
}

/// <summary>The content of <c>modes.json</c>.</summary>
internal sealed class ModesFile
{
    [JsonPropertyName("v")]
    public int Version { get; set; } = 1;

    public List<Mode> Modes { get; set; } = [];
}

/// <summary>What one switch did, for the notice and the log. Names are for display only and are never logged.</summary>
/// <param name="ModeId">The mode that was entered (or left).</param>
/// <param name="Opened">Items that were started and produced a window.</param>
/// <param name="Focused">Items that were already open.</param>
/// <param name="Failed">Items that could not be opened.</param>
/// <param name="Problems">Item name and the reason it failed.</param>
/// <param name="Seconds">How long the switch took.</param>
/// <param name="Prompts">Windows of the mode that was put away which asked a question and were left for the person to answer.</param>
internal sealed record ModeSwitchSummary(string ModeId, int Opened, int Focused, int Failed, IReadOnlyList<(string Name, string Reason)> Problems, double Seconds, int Prompts = 0);
