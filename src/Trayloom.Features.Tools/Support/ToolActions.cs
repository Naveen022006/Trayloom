// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.Tools;

/// <summary>
/// Maps Vorssaint's tool raw ids (radial menu <c>RadialMenuTool</c>, quick launcher items) to the actions of the Trayloom
/// features. Every feature of the Tools project exposes the action ids <c>show</c> / <c>activate</c> / <c>open</c> used below.
/// </summary>
internal static class ToolActions
{
    /// <summary>Tool raw ids in radial-menu picker order.</summary>
    public static readonly string[] RadialOrder =
    [
        "screenshot", "screenRecorder", "colorPicker", "screenOCR", "micMute", "clipboardHistory", "quickLauncher", "cameraPreview",
        "scratchpad", "shelf", "cleaner", "uninstaller", "appUpdates", "cleaningMode", "keepAwake",
    ];

    private static readonly Dictionary<string, (string Feature, string[] Actions)> Map = new(StringComparer.Ordinal)
    {
        ["screenshot"] = ("screenshot", ["region", "capture"]),
        ["screenRecorder"] = ("screen-recorder", ["toggle", "record"]),
        ["colorPicker"] = ("color-picker", ["pick"]),
        ["screenOCR"] = ("screen-ocr", ["capture"]),
        ["micMute"] = ("mic-mute", ["toggle"]),
        ["clipboardHistory"] = ("clipboard-history", ["open"]),
        ["quickLauncher"] = ("quick-launcher", ["show"]),
        ["cameraPreview"] = ("camera-preview", ["show"]),
        ["scratchpad"] = ("scratchpad", ["show"]),
        ["shelf"] = ("shelf", ["toggle"]),
        ["cleaningMode"] = ("cleaning-mode", ["activate"]),
        ["keepAwake"] = ("keep-awake", ["toggle"]),
        ["appUpdates"] = ("app-updates", ["check"]),
        ["commandBar"] = ("command-bar", ["open"]),
        ["recentCaptures"] = ("screenshot", ["recent", "history"]),
    };

    /// <summary>Catalog id of the feature behind a tool.</summary>
    public static string FeatureOf(string tool) => tool switch
    {
        "cleaner" => "cleaner",
        "uninstaller" => "uninstaller",
        _ => Map.TryGetValue(tool, out var m) ? m.Feature : tool,
    };

    /// <summary>Hub title of the tool (the label on the wheel / the tile).</summary>
    public static string TitleOf(string tool) => FeatureCatalog.Get(FeatureOf(tool))?.Title ?? tool;

    /// <summary>True when the feature behind the tool is installed (and, for the shelf, running).</summary>
    public static bool IsAvailable(IContributionServices ctx, string tool) => ctx.Runtime.IsAvailable(FeatureOf(tool));

    /// <summary>Runs the tool. Returns false when it could not be started.</summary>
    public static bool Run(IContributionServices ctx, string tool)
    {
        switch (tool)
        {
            case "cleaner":
                ctx.Shell.OpenSettings(SettingsPages.Cleaner);
                return true;
            case "uninstaller":
                ctx.Shell.OpenSettings(SettingsPages.Uninstaller);
                return true;
            case "appUpdates":
                ActionBridge.Invoke(ctx, "app-updates", "check");
                ctx.Shell.OpenSettings(SettingsPages.AppUpdates);
                return true;
        }

        return Map.TryGetValue(tool, out var m) && ActionBridge.Invoke(ctx, m.Feature, m.Actions);
    }
}
