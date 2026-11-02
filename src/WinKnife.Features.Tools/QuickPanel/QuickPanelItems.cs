// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using WinKnife.Core;

namespace WinKnife.Features.Tools;

/// <summary>One tile of the Quick panel (spec 2.1.3).</summary>
/// <param name="Id">Raw id (persisted in the order / hidden lists).</param>
/// <param name="FeatureId">Catalog id that must be installed.</param>
/// <param name="Title">Tile title.</param>
/// <param name="Glyph">Segoe Fluent glyph (the catalog glyph is used when this is null).</param>
/// <param name="HasOptions">Edit mode shows a gear and an options card.</param>
internal sealed record QuickItem(string Id, string FeatureId, string Title, string? Glyph, bool HasOptions = false);

/// <summary>The 17 tiles in declaration order plus the persistence helpers of 2.0.3.</summary>
internal static class QuickItems
{
    public const string OrderKey = "quickLauncherItemOrder";
    public const string HiddenKey = "quickLauncherHiddenItems";

    public static readonly QuickItem[] All =
    [
        new("keepAwake", "keep-awake", "Keep awake", "", true),
        new("cleaner", "cleaner", "Cleaner", null),
        new("toggles", "quick-toggles", "Quick toggles", ""),
        new("micMute", "mic-mute", "Mute microphone", "", true),
        new("screenOCR", "screen-ocr", "Copy text from screen", null),
        new("colorPicker", "color-picker", "Color picker", "", true),
        new("clipboard", "clipboard-history", "Clipboard", null, true),
        new("windowLayout", "window-layout", "Window layout", null),
        new("cleaning", "cleaning-mode", "Cleaning Mode", ""),
        new("homebrew", "homebrew", "Packages", null),
        new("media", "media-tools", "Media", null),
        new("urlCleaner", "url-cleaner", "Clean URL", ""),
        new("uninstaller", "uninstaller", "Uninstaller", ""),
        new("screenshot", "screenshot", "Screenshot", ""),
        new("screenRecorder", "screen-recorder", "Screen recording", ""),
        new("cameraPreview", "camera-preview", "Camera preview", ""),
        new("scratchpad", "scratchpad", "Scratchpad", ""),
    ];

    public static QuickItem? Get(string id) => All.FirstOrDefault(i => i.Id == id);

    /// <summary>The saved order sanitised: known ids only, no duplicates, missing ids appended in default order.</summary>
    public static List<string> Order(IAppSettings app)
    {
        var result = new List<string>();
        foreach (var id in (app.Get<string>(OrderKey) ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Get(id) is not null && !result.Contains(id))
            {
                result.Add(id);
            }
        }

        foreach (var item in All)
        {
            if (!result.Contains(item.Id))
            {
                result.Add(item.Id);
            }
        }

        return result;
    }

    public static void SaveOrder(IAppSettings app, IEnumerable<string> order) => app.Set(OrderKey, string.Join(",", order));

    public static HashSet<string> Hidden(IAppSettings app) =>
        [.. (app.Get<string>(HiddenKey) ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    public static void SaveHidden(IAppSettings app, IEnumerable<string> hidden) => app.Set(HiddenKey, string.Join(",", hidden.OrderBy(h => h, StringComparer.Ordinal)));

    /// <summary>The Segoe glyph of an item (catalog glyph unless overridden).</summary>
    public static string GlyphOf(QuickItem item) => item.Glyph ?? FeatureCatalog.Get(item.FeatureId)?.Glyph ?? "";
}
