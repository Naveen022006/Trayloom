// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Threading;

namespace Trayloom.Core;

/// <summary>Ids of the tray flyout tabs (spec 01-menu-panel 3.4) in canonical order.</summary>
public static class PanelTabs
{
    public const string KeepAwake = "keepAwake";
    public const string Brightness = "brightness";
    public const string Mixer = "mixer";
    public const string System = "system";
    public const string Network = "network";
    public const string Disk = "disk";
    public const string Power = "power";
    public const string FanControl = "fanControl";
    public const string Utilities = "utilities";
    public const string Controls = "controls";
    public const string Toggles = "toggles";
    public const string Wallpaper = "wallpaper";

    /// <summary>Category ids of the Controls tab (<see cref="IPanelSection.Category"/>).</summary>
    public const string CategoryWindows = "windows";
    public const string CategoryInput = "input";
    public const string CategoryFiles = "files";

    /// <summary>All tabs in canonical order. <c>FanControl</c> is listed but never available on Windows.</summary>
    public static IReadOnlyList<PanelTabInfo> All => _loc_All.Value;

    private static readonly LocValue<IReadOnlyList<PanelTabInfo>> _loc_All = new(() => [
        new(KeepAwake, Loc.T("Keep awake"), "", Loc.T("Keeps your PC awake for as long as you want."), ["keep-awake"], false),
        new(Brightness, Loc.T("Displays"), "", Loc.T("Brightness of your displays."), ["brightness"], false),
        new(Mixer, Loc.T("Volume mixer"), "", Loc.T("Volume of each app, one slider each."), ["mixer", "audio-priority"], false),
        new(System, Loc.T("System"), "", Loc.T("Processor, graphics and memory at a glance."), ["monitor-cpu", "monitor-gpu", "monitor-memory", "connected-devices"], true),
        new(Network, Loc.T("Network"), "", Loc.T("Internet speed and which apps are using it."), ["monitor-network"], true),
        new(Disk, Loc.T("Disks"), "", Loc.T("Free space and disk activity."), ["monitor-disk"], true),
        new(Power, Loc.T("Power"), "", Loc.T("Battery, charging and power use."), ["monitor-power"], true),
        new(FanControl, Loc.T("Fan control"), "", Loc.T("Fan speeds and your own fan curve."), ["fan-control"], false, AvailableOnWindows: false),
        new(Utilities, Loc.T("Utilities"), "", Loc.T("Screenshots, cleaner, updates and other tools."),
        [
            "quick-launcher", "cleaner", "homebrew", "app-updates", "media-tools", "clipboard-history", "window-layout", "uninstaller",
            "url-cleaner", "cleaning-mode", "screen-ocr", "color-picker", "screenshot", "screen-recorder", "camera-preview", "scratchpad",
            "command-bar", "port-manager",
        ], true),
        new(Controls, Loc.T("Controls"), "", Loc.T("Switches for mouse, keyboard and window features."),
        [
            "scroll-inverter", "linear-scroll", "focus-follows-mouse", "mouse-acceleration", "mouse-navigation", "mouse-button-shortcuts", "switcher",
            "finder-cut-paste", "auto-quit", "shelf", "window-maximizer", "dock-preview", "keyboard-debounce", "dock-click", "spaces-order",
            "middle-click", "text-snippets", "super-key", "radial-menu", "mouse-click-debounce", "notch",
        ], true),
        new(Toggles, Loc.T("Quick toggles"), "", Loc.T("One-click actions like dark mode and muting the mic."), ["quick-toggles", "mic-mute", "quit-apps"], true),
        new(Wallpaper, Loc.T("Wallpaper"), "", Loc.T("Pick a still wallpaper without opening Settings."), ["wallpaper"], false),
    ]);

    /// <summary>Looks up a tab.</summary>
    public static PanelTabInfo? Get(string id) => All.FirstOrDefault(t => t.Id == id);
}

/// <summary>Static description of a flyout tab.</summary>
/// <param name="Id">One of the <see cref="PanelTabs"/> constants.</param>
/// <param name="Title">Tooltip and (uppercased) body heading.</param>
/// <param name="Glyph">Tab icon.</param>
/// <param name="Description">Caption in the Tray &amp; panel editor.</param>
/// <param name="GateFeatures">The tab is shown only when any of these catalog features is installed.</param>
/// <param name="SupportsEditing">The section header shows the edit (slider) button.</param>
/// <param name="AvailableOnWindows">False for tabs that cannot exist on Windows.</param>
public sealed record PanelTabInfo(string Id, string Title, string Glyph, string Description, IReadOnlyList<string> GateFeatures, bool SupportsEditing, bool AvailableOnWindows = true);

/// <summary>Settings keys of the panel layout (Vorssaint names).</summary>
public static class PanelKeys
{
    /// <summary>Order of the Utilities rows (comma separated utility ids).</summary>
    public const string UtilityOrder = "panelUtilityOrder";

    /// <summary>Order of the Controls items.</summary>
    public const string ControlOrder = "panelControlOrder";

    /// <summary>Visibility key of a utility row: <c>panelUtility&lt;Name&gt;</c> (id with the first letter upper-cased).</summary>
    public static string Utility(string utilityId) => utilityId switch
    {
        "cleanURL" => "panelUtilityURLCleaner",
        _ => "panelUtility" + char.ToUpperInvariant(utilityId[0]) + utilityId[1..],
    };

    /// <summary>Visibility key of a Controls item: <c>panelControl&lt;Item&gt;</c>.</summary>
    public static string Control(string item) => "panelControl" + item;
}

/// <summary>Opens parts of the shell. Implemented by <c>Trayloom.App</c>; reachable from every context.</summary>
public interface IShell
{
    /// <summary>Opens (or focuses) the Settings window at <paramref name="destination"/> (<c>page#anchor</c>, see <see cref="SettingsPages"/>).</summary>
    /// <param name="destination">Page id and optional anchor.</param>
    /// <param name="targetFeature">Catalog id of the feature to reveal in the Features hub when the destination is the hub.</param>
    void OpenSettings(string destination, string? targetFeature = null);

    /// <summary>Opens the Features hub scrolled to <paramref name="featureId"/> (installs nothing).</summary>
    void OpenFeatures(string? featureId = null);

    /// <summary>Opens the tray flyout on a tab (<see cref="PanelTabs"/>).</summary>
    void OpenPanel(string tabId);

    /// <summary>Closes the tray flyout.</summary>
    void ClosePanel();

    /// <summary>True while the tray flyout is visible.</summary>
    bool IsPanelOpen { get; }

    /// <summary>Quits Trayloom (stops every feature first).</summary>
    void Quit();

    /// <summary>Restarts Trayloom through the relaunch helper.</summary>
    void Restart();
}

/// <summary>Read-mostly view of the install state. The mutating API lives on <see cref="FeatureRuntime"/>.</summary>
public interface IFeatureRuntime
{
    /// <summary>True when the feature (catalog id, raw id or v1 id) is installed.</summary>
    bool IsAvailable(string featureId);

    /// <summary>True when the feature is installed and its <see cref="IFeature"/> is started (features without a class are never running).</summary>
    bool IsRunning(string featureId);

    /// <summary>True when any of the features is installed (an empty list is true).</summary>
    bool AnyAvailable(IEnumerable<string> featureIds);

    /// <summary>Increments after every install / uninstall; pages rebuild when it changes.</summary>
    int Revision { get; }

    /// <summary>Raised on the UI thread after the install state changed (also after a bulk change, once).</summary>
    event Action? Changed;

    /// <summary>Why a feature cannot be installed on this machine (null when it can).</summary>
    string? InstallBlockedReason(string featureId);
}

/// <summary>Services every contribution context offers (settings sections, panel sections, island sections).</summary>
public interface IContributionServices
{
    /// <summary>The flat, app-wide settings (Vorssaint key names).</summary>
    IAppSettings App { get; }

    /// <summary>Install state of the catalog.</summary>
    IFeatureRuntime Runtime { get; }

    /// <summary>Open Settings / panel / quit.</summary>
    IShell Shell { get; }

    /// <summary>Central keyboard-shortcut registry (roles, conflicts, status).</summary>
    IShortcutService Shortcuts { get; }

    /// <summary>Global hotkeys (prefer <see cref="Shortcuts"/> for user-configurable shortcuts).</summary>
    IHotkeyService Hotkeys { get; }

    /// <summary>Shared low-level input hooks.</summary>
    IInputHooks Hooks { get; }

    /// <summary>Notifications.</summary>
    INotifier Notifier { get; }

    /// <summary>Application log.</summary>
    ILog Log { get; }

    /// <summary>The UI dispatcher.</summary>
    Dispatcher Ui { get; }

    /// <summary>Actions of the running features.</summary>
    IActionCatalog Actions { get; }

    /// <summary>The Dynamic Island registry (sections and strips contributed by feature projects).</summary>
    IIslandRegistry Island { get; }

    /// <summary>Service registry: features publish well-known services for the shell (keep awake state, mic mute state...).</summary>
    IServiceHub Services { get; }
}

/// <summary>Forwarding base class so hosts can implement the specific contexts with a few lines.</summary>
public abstract class ContributionServicesBase : IContributionServices
{
    private readonly IContributionServices _inner;

    /// <summary>Wraps the shared services.</summary>
    protected ContributionServicesBase(IContributionServices inner) => _inner = inner;

    public IAppSettings App => _inner.App;

    public IFeatureRuntime Runtime => _inner.Runtime;

    public IShell Shell => _inner.Shell;

    public IShortcutService Shortcuts => _inner.Shortcuts;

    public IHotkeyService Hotkeys => _inner.Hotkeys;

    public IInputHooks Hooks => _inner.Hooks;

    public INotifier Notifier => _inner.Notifier;

    public ILog Log => _inner.Log;

    public Dispatcher Ui => _inner.Ui;

    public IActionCatalog Actions => _inner.Actions;

    public IIslandRegistry Island => _inner.Island;

    public IServiceHub Services => _inner.Services;
}

/// <summary>Common shape of every contribution provider. Providers are created once (parameterless ctor) and must be stateless.</summary>
public interface IContributionProvider
{
    /// <summary>
    /// Catalog id of the feature this contribution belongs to. The contribution is shown only while that feature is
    /// installed. <c>null</c> = always shown.
    /// </summary>
    string? FeatureId => null;

    /// <summary>
    /// Several gating features: shown while any of them is installed. Defaults to <see cref="FeatureId"/>
    /// (empty = always shown). Override when a section serves several features.
    /// </summary>
    IReadOnlyList<string> FeatureIds => FeatureId is null ? [] : [FeatureId];

    /// <summary>Sort key inside the page / tab / list (ascending; ties keep discovery order).</summary>
    int Order => 0;
}

// ------------------------------------------------------------------------------------------------ settings

/// <summary>Context handed to <see cref="ISettingsSection.CreateView"/>.</summary>
public interface ISettingsContext : IContributionServices
{
    /// <summary>The page the section is created for.</summary>
    string PageId { get; }

    /// <summary>The anchor currently requested by the router (null when none).</summary>
    string? RequestedAnchor { get; }

    /// <summary>Rebuilds this page (e.g. after a toggle changed which rows exist).</summary>
    void RequestRebuild();
}

/// <summary>
/// A card or group of cards on a Settings page. A page = header + the sections of that page in <see cref="IContributionProvider.Order"/>.
/// The view is created every time the page is shown or rebuilt; it must read its values from <see cref="IAppSettings"/> and write back immediately.
/// </summary>
public interface ISettingsSection : IContributionProvider
{
    /// <summary>Page id (<see cref="SettingsPages"/>).</summary>
    string PageId { get; }

    /// <summary>Anchor within the page (the router scrolls to it and tints the section for ~1.2 s). Optional.</summary>
    string? Anchor => null;

    /// <summary>Section title used by "Search settings" (page rows + indented section rows). Optional.</summary>
    string? Title => null;

    /// <summary>Extra search keywords (labels of the options inside).</summary>
    IReadOnlyList<string> Keywords => [];

    /// <summary>Builds the section view (usually one or more <c>SettingsCard</c>s in a <c>StackPanel</c>).</summary>
    FrameworkElement CreateView(ISettingsContext context);
}

// ------------------------------------------------------------------------------------------------ panel

/// <summary>Context handed to panel sections and hosted utility views.</summary>
public interface IPanelContext : IContributionServices
{
    /// <summary>The tab the view lives in.</summary>
    string TabId { get; }

    /// <summary>True while the section header is in edit mode (reorder / eye toggles).</summary>
    bool IsEditing { get; }

    /// <summary>Raised when <see cref="IsEditing"/> changes.</summary>
    event Action? EditingChanged;

    /// <summary>Raised when the user presses the "Default" (reset) button of the edit bar: restore default order and visibility.</summary>
    event Action? ResetRequested;

    /// <summary>True while the tab is the visible one and the flyout is open (start/stop sampling with it).</summary>
    bool IsActive { get; }

    /// <summary>Raised when <see cref="IsActive"/> changes.</summary>
    event Action? ActiveChanged;

    /// <summary>Call after the content height changed so the host re-measures the flyout.</summary>
    void RequestRelayout();

    /// <summary>Closes the flyout.</summary>
    void ClosePanel();

    /// <summary>Closes the flyout, then runs <paramref name="action"/> on the UI thread after <paramref name="delayMs"/> (focus has returned to the previous window).</summary>
    void InvokeAfterClose(Action action, int delayMs = 150);

    /// <summary>While true, clicking outside does not dismiss the flyout (Vorssaint's <c>viewKeepsPopoverOpen</c>). Reset when the view goes away.</summary>
    void SetKeepOpen(bool keepOpen);

    /// <summary>Replaces tab bar and body with a metric-detail view (back button + title). <see cref="CloseDetail"/> returns.</summary>
    void ShowDetail(string title, string glyph, FrameworkElement view);

    /// <summary>Leaves the detail view.</summary>
    void CloseDetail();

    /// <summary>Opens the hosted view of an <see cref="IPanelUtility"/> (Utilities tab).</summary>
    void HostUtility(string utilityId);
}

/// <summary>
/// The body of a flyout tab (or part of it). A tab body = the sections of that tab in order; the host adds the
/// uppercase title row (with the edit button when <see cref="SupportsEditing"/>) and the scroll area.
/// Tabs without any visible section are hidden.
/// </summary>
public interface IPanelSection : IContributionProvider
{
    /// <summary>One of the <see cref="PanelTabs"/> constants.</summary>
    string TabId { get; }

    /// <summary>True when the view reacts to <see cref="IPanelContext.IsEditing"/> (shows the edit button).</summary>
    bool SupportsEditing => false;

    /// <summary>Controls tab only: <see cref="PanelTabs.CategoryWindows"/>, <see cref="PanelTabs.CategoryInput"/> or <see cref="PanelTabs.CategoryFiles"/>.</summary>
    string? Category => null;

    /// <summary>Controls tab only: <c>(enabled, total)</c> shown on the category header (e.g. 6/6). Prefer <see cref="GetCategoryCounts"/>, which has a context.</summary>
    (int Enabled, int Total) CategoryCounts => (0, 0);

    /// <summary>Controls tab only: the counts of this section, evaluated with a live context when the tab is built.</summary>
    (int Enabled, int Total) GetCategoryCounts(IPanelContext context) => CategoryCounts;

    /// <summary>Builds the section content (usually a <c>PanelCard</c> or <c>PanelRowGroup</c>).</summary>
    FrameworkElement CreateView(IPanelContext context);
}

/// <summary>
/// A row of the Utilities tab. Either a plain action (<see cref="Invoke"/>: the flyout closes, then the action runs) or a
/// hosted utility (<see cref="CreateHostedView"/>: the view replaces the list inside the flyout, one at a time).
/// </summary>
public interface IPanelUtility : IContributionProvider
{
    /// <summary>
    /// Stable id (persisted in the panel order). Visibility is stored under Vorssaint's key <c>panelUtility&lt;Name&gt;</c>
    /// (<see cref="PanelKeys.Utility"/>), the single source of truth shared with the feature's own "Show in panel" switch.
    /// </summary>
    string Id { get; }

    /// <summary>True for utilities that only exist as a hosted view (reached through <see cref="IPanelContext.HostUtility"/>, e.g. Recent captures); they never get a row.</summary>
    bool HostedOnly => false;

    /// <summary>Row title.</summary>
    string Title { get; }

    /// <summary>Row icon (Segoe Fluent Icons glyph).</summary>
    string Glyph { get; }

    /// <summary>Tooltip text (also printed under the title when <see cref="CaptionStaysVisible"/> or <see cref="NeedsAttention"/>).</summary>
    string? Caption => null;

    /// <summary>Print the caption under the title even without a problem.</summary>
    bool CaptionStaysVisible => false;

    /// <summary>Orange icon + caption (missing permission, disabled option...).</summary>
    bool NeedsAttention => false;

    /// <summary>Text of the shortcut chip (the currently registered shortcut only, e.g. <c>Ctrl+Alt+V</c>); null = no chip.</summary>
    string? ShortcutText => null;

    /// <summary>Delay between closing the flyout and <see cref="Invoke"/> (150 or 200 ms).</summary>
    int InvokeDelayMs => 150;

    /// <summary>Plain action. Called after the flyout closed. Ignored when <see cref="CreateHostedView"/> returns a view.</summary>
    void Invoke(IPanelContext context) { }

    /// <summary>Hosted utility view (return null for plain actions). Shown under the standard header (title, gear, close).</summary>
    FrameworkElement? CreateHostedView(IPanelContext context) => null;

    /// <summary>Settings destination opened by the gear in the hosted header and by the footer Settings button while hosted.</summary>
    string? SettingsDestination => null;

    /// <summary>Show the gear button in the hosted header (App updates and Port manager only in Vorssaint).</summary>
    bool ShowsSettingsGear => false;

    /// <summary>Keep the flyout open on outside clicks while hosted (true for all except the package manager).</summary>
    bool HostedKeepsPanelOpen => true;

    /// <summary>Accessory mini button under the row (e.g. "Recent captures"); null = none.</summary>
    string? AccessoryTitle => null;

    /// <summary>Glyph of the accessory button.</summary>
    string? AccessoryGlyph => null;

    /// <summary>Accessory click handler.</summary>
    void OnAccessory(IPanelContext context) { }

    /// <summary>Raised when title / caption / shortcut changed so the host re-renders the row. Default: never.</summary>
    event Action? Changed
    {
        add { }
        remove { }
    }
}

// ------------------------------------------------------------------------------------------------ island

/// <summary>
/// Install-state-filtered view of the island contributions. The Island project hosts them: render <see cref="Sections"/> as
/// pages and <see cref="Strips"/> as compact items, and rebuild when <see cref="Changed"/> fires.
/// </summary>
public interface IIslandRegistry
{
    /// <summary>Sections of installed features, ordered by <see cref="IContributionProvider.Order"/>.</summary>
    IReadOnlyList<IIslandSection> Sections { get; }

    /// <summary>Strip items of installed features, ordered by <see cref="IContributionProvider.Order"/>.</summary>
    IReadOnlyList<IIslandStrip> Strips { get; }

    /// <summary>Raised (UI thread) when a feature was installed/uninstalled or a provider was added.</summary>
    event Action? Changed;
}

/// <summary>Context handed to island contributions.</summary>
public interface IIslandContext : IContributionServices
{
    /// <summary>Asks the island to re-render the contribution.</summary>
    void Invalidate();
}

/// <summary>A page / module of the Dynamic Island (e.g. Calendar, Timer, AI Agents). Hosted by the Island project.</summary>
public interface IIslandSection : IContributionProvider
{
    /// <summary>Stable id (persisted in the island layout).</summary>
    string Id { get; }

    /// <summary>Title in the island page switcher and in the layout editor.</summary>
    string Title { get; }

    /// <summary>Glyph in the island page switcher.</summary>
    string Glyph { get; }

    /// <summary>Builds the expanded page content.</summary>
    FrameworkElement CreateView(IIslandContext context);
}

/// <summary>
/// Optional: implemented by the view of an <see cref="IIslandSection"/> that keeps a control (for example a period menu) in the
/// island's top row, beside the back button, instead of at the top of the page.
/// </summary>
public interface IIslandTopRowHost
{
    /// <summary>
    /// The island calls this once before it measures the page: from now on the control lives in the returned element, which the
    /// island places in the top row (it is empty, and takes no room, while the page has nothing for the row).
    /// </summary>
    FrameworkElement ClaimTopRow();
}

/// <summary>A compact item of the island strip (collapsed / live-activity state), e.g. the timer countdown or an agent badge.</summary>
public interface IIslandStrip : IContributionProvider
{
    /// <summary>Stable id (persisted in the island layout).</summary>
    string Id { get; }

    /// <summary>Builds the strip item; return null when there is nothing to show right now.</summary>
    FrameworkElement? CreateStrip(IIslandContext context);
}
