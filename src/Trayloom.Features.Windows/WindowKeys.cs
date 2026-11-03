// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.WindowTools;

/// <summary>The settings store captured by the running features (panel rows need it before their first view is built).</summary>
internal static class Ambient
{
    public static IAppSettings? App { get; set; }
}

/// <summary>Flat <see cref="IAppSettings"/> keys (Vorssaint <c>DefaultsKey</c> names) and registered defaults of this project.</summary>
internal static class Keys
{
    // switcher
    public const string SwitcherEnabled = "switcherEnabled";
    public const string SwitcherAllWindowsEnabled = "switcherAllWindowsShortcutEnabled";
    public const string SwitcherTakeOver = "switcherTakeOverSystemShortcuts";
    public const string SwitcherIconRow = "switcherIconRowMode";
    public const string SwitcherSimple = "switcherSimpleMode";
    public const string SwitcherMergeTabs = "switcherMergeTabs";
    public const string SwitcherWindowless = "switcherWindowlessApps";
    public const string SwitcherMinimized = "switcherMinimizedPlacement";
    public const string SwitcherTreatHidden = "switcherTreatHiddenAppsLikeMinimized";
    public const string SwitcherFullscreen = "switcherShowFullscreenWindows";
    public const string SwitcherRules = "switcherAppRules";
    public const string SwitcherCurrentDesktop = "switcherCurrentSpaceOnly";
    public const string SwitcherCurrentDisplay = "switcherCurrentDisplayOnly";
    public const string SwitcherPlacement = "switcherScreenPlacement";
    public const string SwitcherSearchPin = "switcherSearchPinEnabled";
    public const string SwitcherHints = "switcherShowShortcutHints";
    public const string SwitcherDelay = "switcherAppearanceDelay";
    public const string SwitcherInstant = "switcherInstantSelection";
    public const string SwitcherMaxColumns = "switcherMaxColumns";
    public const string SwitcherPreviewSize = "switcherPreviewSize";
    public const string SwitcherExcluded = "switcherPreviewExcludedApps";
    public const string MinimalPreviews = "minimalWindowPreviews";

    // taskbar
    public const string DockPreviewEnabled = "dockPreviewEnabled";
    public const string DockCurrentDesktop = "dockPreviewCurrentSpaceOnly";
    public const string DockOpenDelay = "dockPreviewOpenDelay";
    public const string DockOpacity = "dockPreviewBackgroundOpacity";
    public const string DockQuitOnX = "dockPreviewQuitAppOnClose";
    public const string DockKeepVisible = "dockPreviewKeepDockVisible";
    public const string DockOrderByCreation = "dockPreviewOrderByCreation";
    public const string DockClickMinimize = "dockClickMinimize";
    public const string DockClickHide = "dockClickHide";
    public const string DockClickCycle = "dockClickCycleWindows";
    public const string PreviewSize = "previewSize";
    public const string PreviewExcluded = "windowPreviewExcludedApps";

    // window layout
    public const string PanelUtility = "panelUtilityWindowLayout";
    public const string LayoutShortcutsEnabled = "windowLayoutShortcutsEnabled";
    public const string DirectionalEnabled = "windowDirectionalEnabled";
    public const string EdgeSnapEnabled = "windowEdgeSnapEnabled";
    public const string EdgeSnapDisabledZones = "windowEdgeSnapDisabledZones";
    public const string EdgeSnapZoneActions = "windowEdgeSnapZoneActions";
    public const string GestureEnabled = "windowGestureEnabled";
    public const string GestureModifiers = "windowGestureModifiers";
    public const string GestureRaise = "windowGestureRaiseWindow";
    public const string LayoutIgnored = "windowLayoutIgnoredApps";
    public const string WindowGap = "windowLayoutWindowGap";
    public const string ScreenGap = "windowLayoutScreenGap";
    public const string MarginPercent = "windowLayoutMarginPercent";
    public const string SideRepeat = "windowLayoutSideRepeatCyclesThirds";
    public const string HiddenActions = "windowLayoutHiddenActions";
    public const string PointerDisplayEnabled = "pointerDisplayEnabled";

    // auto quit
    public const string AutoQuitEnabled = "autoQuitEnabled";
    public const string AutoQuitExceptions = "autoQuitExceptions";
    public const string AutoQuitTrayApps = "autoQuitDockApps";

    // crash recovery journals
    public const string HoverTimeJournal = "dockPreviewHoverTimeJournal";
    public const string AutohideJournal = "dockPreviewRestoreAutohide";

    /// <summary>Role id of the "Shortcut + pointer layout" trigger.</summary>
    public const string DirectionalRole = "windowDirectional";

    /// <summary>Panel visibility key of a Controls row.</summary>
    public static string PanelControl(string item) => "panelControl" + item;

    /// <summary>Registers every default of this project (safe to call repeatedly).</summary>
    public static void Register(IAppSettings app)
    {
        app.RegisterDefaults(new Dictionary<string, object?>
        {
            [SwitcherEnabled] = true,
            [SwitcherAllWindowsEnabled] = false,
            [SwitcherTakeOver] = true,
            [SwitcherIconRow] = false,
            [SwitcherSimple] = false,
            [SwitcherMergeTabs] = false,
            [SwitcherWindowless] = "finder",
            [SwitcherMinimized] = "normal",
            [SwitcherTreatHidden] = true,
            [SwitcherFullscreen] = true,
            [SwitcherRules] = new Dictionary<string, string>(),
            [SwitcherCurrentDesktop] = false,
            [SwitcherCurrentDisplay] = false,
            [SwitcherPlacement] = "pointer",
            [SwitcherSearchPin] = false,
            [SwitcherHints] = true,
            [SwitcherDelay] = 100,
            [SwitcherInstant] = false,
            [SwitcherMaxColumns] = 0,
            [SwitcherPreviewSize] = "normal",
            [SwitcherExcluded] = new List<string>(),
            [MinimalPreviews] = false,
            [DockPreviewEnabled] = false,
            [DockCurrentDesktop] = false,
            [DockOpenDelay] = 200,
            [DockOpacity] = 1.0,
            [DockQuitOnX] = false,
            [DockKeepVisible] = false,
            [DockOrderByCreation] = false,
            [DockClickMinimize] = false,
            [DockClickHide] = false,
            [DockClickCycle] = false,
            [PreviewSize] = "normal",
            [PreviewExcluded] = new List<string>(),
            [PanelUtility] = true,
            [LayoutShortcutsEnabled] = false,
            [DirectionalEnabled] = false,
            [EdgeSnapEnabled] = false,
            [EdgeSnapDisabledZones] = string.Empty,
            [EdgeSnapZoneActions] = string.Empty,
            [GestureEnabled] = false,
            [GestureModifiers] = "control+option",
            [GestureRaise] = false,
            [LayoutIgnored] = new List<string>(),
            [WindowGap] = 0,
            [ScreenGap] = 0,
            [MarginPercent] = 5.0,
            [SideRepeat] = false,
            [HiddenActions] = string.Empty,
            [PointerDisplayEnabled] = false,
            [AutoQuitEnabled] = false,
            [AutoQuitExceptions] = new List<string>(),
            [AutoQuitTrayApps] = new List<string>(),
        });
        // The pointer-layout trigger is a runtime role (Core has none): default Ctrl+Alt+Shift+Space because Ctrl+Alt+Space belongs to the Command Bar.
    }
}
