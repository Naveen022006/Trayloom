// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace WinKnife.Core;

/// <summary>What a feature keeps alive while it is on (drives the energy label in the Features hub).</summary>
public enum EnergyProfile
{
    /// <summary>"Nothing at rest".</summary>
    Idle,

    /// <summary>"Listens to the mouse" (WH_MOUSE_LL).</summary>
    Mouse,

    /// <summary>"Listens to pointer input".</summary>
    Pointer,

    /// <summary>"Listens to the keyboard" (WH_KEYBOARD_LL).</summary>
    Keyboard,

    /// <summary>"Listens to mouse and keyboard".</summary>
    Inputs,

    /// <summary>"Checks on an interval" (a timer).</summary>
    Periodic,
}

/// <summary>
/// Vorssaint's permission rows (enum order = order of the Permissions tab). On Windows each one is a capability
/// check, see the Permissions page in <c>WinKnife.App</c>.
/// </summary>
public enum AppPermission
{
    Accessibility,
    ScreenRecording,
    FullDiskAccess,
    FilesAndFolders,
    Notifications,
    AutomationFinder,
    AutomationPlayback,
    AudioCapture,
    Microphone,
    Camera,
    AppManagement,
    Calendar,
}

/// <summary>How well a catalog feature maps to Windows.</summary>
public enum WindowsSupport
{
    /// <summary>Works as on the Mac.</summary>
    Supported,

    /// <summary>Works with a Windows equivalent (title/description already adapted).</summary>
    Adapted,

    /// <summary>Cannot work on Windows: the row is disabled, never counted and cannot be installed.</summary>
    Unavailable,
}

/// <summary>One of the entries of the feature catalog (spec 02-settings section 4).</summary>
/// <param name="Id">Stable kebab-case id used everywhere in code (contributions, <see cref="IFeature.Id"/>).</param>
/// <param name="RawId">Vorssaint's raw id (camelCase). <c>featureAvailable.&lt;RawId&gt;</c> stores the install state.</param>
/// <param name="Group">Hub group.</param>
/// <param name="TitleEnglish">Hub title (English source text).</param>
/// <param name="DescriptionEnglish">Hub description (English source text).</param>
/// <param name="Glyph">Segoe Fluent Icons glyph.</param>
/// <param name="Energy">Energy profile shown in the hub.</param>
/// <param name="Permissions">Permissions the feature can use.</param>
/// <param name="DefaultInstalled">Installed by default on an updated install (a clean install only gets the Essentials bundle).</param>
/// <param name="Destination">Settings destination <c>page#anchor</c> (see <see cref="SettingsPages"/>).</param>
/// <param name="EnabledKeys">Flat IAppSettings keys that switch the feature on (empty = on-demand feature).</param>
/// <param name="InitialEnableKeys">Keys switched on at first install (only if none of <paramref name="EnabledKeys"/> was ever saved).</param>
/// <param name="IsBeta">Shows the BETA badge.</param>
/// <param name="IsExperimental">Shows the EXPERIMENTAL badge.</param>
/// <param name="Support">Windows availability.</param>
/// <param name="UnavailableReason">Tooltip when <paramref name="Support"/> is <see cref="WindowsSupport.Unavailable"/>.</param>
/// <param name="LegacyId">The id the v1 feature class used (still accepted by <see cref="FeatureCatalog.Resolve"/>).</param>
public sealed record FeatureCatalogEntry(
    string Id,
    string RawId,
    FeatureGroup Group,
    string TitleEnglish,
    string DescriptionEnglish,
    string Glyph,
    EnergyProfile Energy,
    IReadOnlyList<AppPermission> Permissions,
    bool DefaultInstalled,
    string Destination,
    IReadOnlyList<string> EnabledKeys,
    IReadOnlyList<string> InitialEnableKeys,
    bool IsBeta = false,
    bool IsExperimental = false,
    WindowsSupport Support = WindowsSupport.Supported,
    string? UnavailableReason = null,
    string? LegacyId = null)
{
    /// <summary>Hub title in the current language.</summary>
    public string Title => Loc.T(TitleEnglish);

    /// <summary>Hub description in the current language.</summary>
    public string Description => Loc.T(DescriptionEnglish);

    /// <summary>True when the Dynamic Island must be installed for this feature to show anything.</summary>
    public bool IsIslandExtension => Group == FeatureGroup.Island && Id != "notch";

    /// <summary>True when the feature can be installed on this machine.</summary>
    public bool CanInstall => Support != WindowsSupport.Unavailable;

    /// <summary>The settings page part of <see cref="Destination"/>.</summary>
    public string Page => SettingsPages.Split(Destination).Page;

    /// <summary>The anchor part of <see cref="Destination"/> (null when there is none).</summary>
    public string? Anchor => SettingsPages.Split(Destination).Anchor;

    /// <summary>False when the destination is the Features hub itself (no chevron in the hub).</summary>
    public bool HasSettingsPage => Page != SettingsPages.Features;
}

/// <summary>A one-click set of features of the Features hub / onboarding (spec 02-settings 5.3).</summary>
/// <param name="Id">essentials | windows | battery.</param>
/// <param name="Name">Display name.</param>
/// <param name="Description">Card caption.</param>
/// <param name="Glyph">Segoe Fluent Icons glyph.</param>
/// <param name="Features">Catalog ids installed by the bundle.</param>
/// <param name="EnableKeys">Keys switched on when the bundle is applied.</param>
public sealed record FeaturePreset(string Id, string Name, string Description, string Glyph, IReadOnlyList<string> Features, IReadOnlyList<string> EnableKeys);

/// <summary>Page ids of the Settings window and their titles. Contributions use these as <c>PageId</c>.</summary>
public static class SettingsPages
{
    public const string General = "general";

    /// <summary>Pseudo destination: "the page that was open last" (the host resolves it before routing; falls back to General).</summary>
    public const string Last = "last";

    public const string Features = "features";
    public const string Island = "island";
    public const string Monitor = "monitor";
    public const string Energy = "energy";
    public const string Switcher = "switcher";
    public const string Dock = "dock";
    public const string WindowLayout = "window-layout";
    public const string QuitOnClose = "quit-on-close";
    public const string QuitProtection = "quit-protection";
    public const string Mouse = "mouse";
    public const string Debounce = "debounce";
    public const string SuperKey = "super-key";
    public const string TextSnippets = "text-snippets";
    public const string Clipboard = "clipboard";
    public const string CutPaste = "cut-paste";
    public const string Shelf = "shelf";
    public const string Media = "media";
    public const string CleanUrl = "clean-url";
    public const string AppUpdates = "app-updates";
    public const string Packages = "packages";
    public const string Cleaner = "cleaner";
    public const string Uninstaller = "uninstaller";
    public const string KillProcess = "kill-process";
    public const string PortManager = "port-manager";
    public const string QuickTools = "quick-tools";
    public const string ScreenCapture = "screen-capture";
    public const string CommandBar = "command-bar";
    public const string RadialMenu = "radial-menu";
    public const string Shortcuts = "shortcuts";
    public const string Advanced = "advanced";
    public const string About = "about";
    public const string WhatsNew = "whats-new";
    public const string Support = "support";

    /// <summary>Anchor of the General page that hosts the tray / panel configuration ("Tray &amp; panel").</summary>
    public const string PanelConfigurationAnchor = "panelConfiguration";

    private static Dictionary<string, string> Titles => _loc_Titles.Value;

    private static readonly LocValue<Dictionary<string, string>> _loc_Titles = new(() => new(StringComparer.Ordinal)
    {
        [General] = Loc.T("General"),
        [Features] = Loc.T("Features"),
        [Island] = Loc.T("Dynamic Island"),
        [Monitor] = Loc.T("Monitor"),
        [Energy] = Loc.T("Energy"),
        [Switcher] = Loc.T("Switcher"),
        [Dock] = Loc.T("Taskbar"),
        [WindowLayout] = Loc.T("Window layout"),
        [QuitOnClose] = Loc.T("Quit on close"),
        [QuitProtection] = Loc.T("Quit & close protection"),
        [Mouse] = Loc.T("Mouse & Touchpad"),
        [Debounce] = Loc.T("Debounce"),
        [SuperKey] = Loc.T("Super key"),
        [TextSnippets] = Loc.T("Text snippets"),
        [Clipboard] = Loc.T("Clipboard"),
        [CutPaste] = Loc.T("Cut & paste"),
        [Shelf] = Loc.T("Shelf"),
        [Media] = Loc.T("Media"),
        [CleanUrl] = Loc.T("Clean URL"),
        [AppUpdates] = Loc.T("App updates"),
        [Packages] = "Packages",
        [Cleaner] = Loc.T("Cleaner"),
        [Uninstaller] = Loc.T("Uninstaller"),
        [KillProcess] = Loc.T("Kill Process"),
        [PortManager] = Loc.T("Port Manager"),
        [QuickTools] = Loc.T("Quick panel"),
        [ScreenCapture] = Loc.T("Screen capture"),
        [CommandBar] = Loc.T("Command Bar"),
        [RadialMenu] = Loc.T("Radial menu"),
        [Shortcuts] = Loc.T("Keyboard shortcuts"),
        [Advanced] = Loc.T("Advanced"),
        [About] = Loc.T("About"),
        [WhatsNew] = Loc.T("What’s New"),
        [Support] = Loc.T("Support"),
    });

    /// <summary>The detail-page title of a page id (the page id itself when unknown).</summary>
    public static string TitleOf(string pageId) => Titles.TryGetValue(pageId, out var title) ? Loc.T(title) : pageId;

    /// <summary>All known page ids.</summary>
    public static IReadOnlyCollection<string> All => Titles.Keys;

    /// <summary>Splits <c>page#anchor</c>.</summary>
    public static (string Page, string? Anchor) Split(string destination)
    {
        var i = destination.IndexOf('#');
        return i < 0 ? (destination, null) : (destination[..i], destination[(i + 1)..]);
    }

    /// <summary>Joins a page and an optional anchor.</summary>
    public static string Join(string page, string? anchor) => string.IsNullOrEmpty(anchor) ? page : page + "#" + anchor;
}
