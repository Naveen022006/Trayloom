// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.App;

/// <summary>A row of the Settings sidebar.</summary>
/// <param name="Id">Stable id.</param>
/// <param name="Title">Row title.</param>
/// <param name="Glyph">Icon.</param>
/// <param name="Destination">Router destination (<c>page#anchor</c>).</param>
/// <param name="Gates">Visible while any of these catalog features is installed (empty = always).</param>
/// <param name="FeatureId">Catalog id when the row is a feature row (hub gating, search, tooltip).</param>
/// <param name="Badge">BETA etc.</param>
internal sealed record SidebarRow(string Id, string Title, string Glyph, string Destination, IReadOnlyList<string> Gates, string? FeatureId = null, string? Badge = null)
{
    public bool IsVisible(IFeatureRuntime runtime) => runtime.AnyAvailable(Gates);
}

/// <summary>A collapsible sidebar section.</summary>
internal sealed record SidebarSection(string Title, IReadOnlyList<SidebarRow> Rows);

/// <summary>
/// The exact sidebar order of spec 02-settings section 2, with Windows wording. Rows are visible only while their gating
/// features are installed; empty sections are dropped.
/// </summary>
internal static class SidebarModel
{
    private static readonly string[] MonitorIds = ["monitor-cpu", "monitor-gpu", "monitor-memory", "monitor-network", "monitor-disk", "monitor-power", "connected-devices", "fan-control"];

    public static IReadOnlyList<SidebarSection> All => _loc_All.Value;

    private static readonly LocValue<IReadOnlyList<SidebarSection>> _loc_All = new(() => Build());

    private static SidebarRow Page(string id, string title, string glyph, string dest, params string[] gates) =>
        new(id, title, glyph, dest, gates);

    private static SidebarRow Feature(string catalogId)
    {
        var e = FeatureCatalog.Get(catalogId)!;
        return new SidebarRow(e.Id, e.Title, e.Glyph, e.Destination == SettingsPages.Features ? SettingsPages.Features : e.Destination, [e.Id], e.Id, e.IsBeta ? Loc.T("Beta") : null);
    }

    private static SidebarSection Features(string title, params string[] ids) => new(title, [.. ids.Select(Feature)]);

    private static IReadOnlyList<SidebarSection> Build() =>
    [
        new(Loc.T("Essentials"),
        [
            Page("general", Loc.T("General"), "", "general"),
            Page("tray", Loc.T("Tray & panel"), "", "general#" + SettingsPages.PanelConfigurationAnchor),
            Page("features", Loc.T("Features"), "", "features"),
            Page("island", Loc.T("Dynamic Island"), "", "island", "notch"),
            Page("monitor", Loc.T("Monitor"), "", "monitor", MonitorIds),
        ]),
        Features(Loc.T("Utilities"), "command-bar", "quick-launcher", "quick-toggles", "quit-apps", "camera-preview", "wallpaper", "scratchpad", "cleaning-mode", "screenshot", "screen-recorder", "screen-ocr", "color-picker", "radial-menu"),
        Features(Loc.T("Sound"), "music-block", "mixer", "sound-output-switcher", "audio-priority", "mic-mute"),
        Features(Loc.T("Energy and display"), "keep-awake", "brightness", "extra-brightness", "bluetooth-sleep"),
        Features(Loc.T("Windows and taskbar"), "switcher", "dock-preview", "dock-click", "window-layout", "window-maximizer", "auto-quit", "spaces-order"),
        Features(Loc.T("Mouse and keyboard"), "scroll-inverter", "scroll-horizontal", "scroll-zoom", "middle-click", "focus-follows-mouse", "smooth-scroll", "linear-scroll", "mouse-acceleration", "mouse-navigation", "mouse-button-shortcuts", "mouse-click-debounce", "quit-window-protection", "keyboard-debounce", "super-key", "text-snippets"),
        Features(Loc.T("Files"), "clipboard-history", "paste-plain", "finder-cut-paste", "finder-rename", "shelf", "media-tools", "url-cleaner"),
        Features(Loc.T("App management"), "app-updates", "cleaner", "homebrew", "uninstaller", "kill-process", "port-manager"),
        new("App",
        [
            Page("shortcuts", Loc.T("Keyboard shortcuts"), "", "shortcuts"),
            Page("advanced", Loc.T("Advanced"), "", "advanced"),
            Page("about", Loc.T("About"), "", "about"),
            Page("whats-new", Loc.T("What’s New"), "", "whats-new"),
            Page("support", Loc.T("Support"), "", "support"),
        ]),
    ];

    /// <summary>Sections with their visible rows.</summary>
    public static IReadOnlyList<(SidebarSection Section, IReadOnlyList<SidebarRow> Rows)> Visible(IFeatureRuntime runtime) =>
        [.. All.Select(s => (s, (IReadOnlyList<SidebarRow>)[.. s.Rows.Where(r => r.IsVisible(runtime))])).Where(t => t.Item2.Count > 0)];

    /// <summary>True when the visit's page is reachable (gating features installed).</summary>
    public static bool IsPageVisible(IFeatureRuntime runtime, Visit visit)
    {
        var page = visit.Page;
        if (page is SettingsPages.General or SettingsPages.Features or SettingsPages.Shortcuts or SettingsPages.Advanced or SettingsPages.About or SettingsPages.WhatsNew or SettingsPages.Support)
        {
            return true;
        }

        var gates = PageGates(page);
        return runtime.AnyAvailable(gates);
    }

    /// <summary>Gating features of a page (page hidden when none is installed).</summary>
    public static IReadOnlyList<string> PageGates(string page)
    {
        if (page == SettingsPages.Monitor)
        {
            return MonitorIds;
        }

        var ids = FeatureCatalog.All.Where(e => e.Page == page).Select(e => e.Id).ToList();
        return ids.Count == 0 ? [] : ids;
    }
}
