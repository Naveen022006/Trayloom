// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics.CodeAnalysis;

namespace Trayloom.Core;

/// <summary>
/// The complete feature catalog (spec 02-settings section 4/9): ids, groups, titles, descriptions, glyphs, energy
/// profile, permissions, default install state, settings destination and Windows availability. Hub, sidebar,
/// panel gating and search all read this table; nothing derives order from reflection.
/// </summary>
public static class FeatureCatalog
{
    private const FeatureGroup W = FeatureGroup.WindowsTaskbar;
    private const FeatureGroup M = FeatureGroup.MouseKeyboard;
    private const FeatureGroup C = FeatureGroup.ClipboardFiles;
    private const FeatureGroup S = FeatureGroup.Sound;
    private const FeatureGroup E = FeatureGroup.EnergyDisplay;
    private const FeatureGroup T = FeatureGroup.Tools;
    private const FeatureGroup I = FeatureGroup.Island;
    private const FeatureGroup Mon = FeatureGroup.Monitor;

    private const EnergyProfile Idle = EnergyProfile.Idle;
    private const EnergyProfile Ms = EnergyProfile.Mouse;
    private const EnergyProfile Kb = EnergyProfile.Keyboard;
    private const EnergyProfile Both = EnergyProfile.Inputs;
    private const EnergyProfile Per = EnergyProfile.Periodic;

    private static readonly Dictionary<string, FeatureCatalogEntry> ById;
    private static readonly Dictionary<string, FeatureCatalogEntry> ByAlias;

    /// <summary>The preset bundles of the hub / onboarding, in order.</summary>
    public static IReadOnlyList<FeaturePreset> Presets => BuildPresets();

    private static List<FeaturePreset> BuildPresets() =>
    [
        new("essentials", Loc.T("Essentials"), Loc.T("Volume mixer, system monitor and keep awake."), "",
            ["mixer", "keep-awake", "monitor-cpu", "monitor-gpu", "monitor-memory", "monitor-network", "monitor-disk", "monitor-power"], []),
        new("windows", Loc.T("Windows"), Loc.T("Window switcher, window layout and the taskbar features."), "",
            ["switcher", "window-layout", "dock-preview", "dock-click", "window-maximizer"],
            ["switcherEnabled", "dockPreviewEnabled", "dockClickMinimize", "windowMaximizeEnabled"]),
        new("battery", Loc.T("Battery and quiet"), Loc.T("A lean monitor with battery, memory and processor. Nothing listens to input."), "",
            ["monitor-cpu", "monitor-memory", "monitor-power"], []),
    ];

    /// <summary>Every catalog entry in hub order (group order of the table below).</summary>
    public static IReadOnlyList<FeatureCatalogEntry> All { get; }

    /// <summary>The sidebar/hub title of the Island group extensions order = order of <see cref="All"/> filtered by <see cref="FeatureGroup.Island"/>.</summary>
    public static IEnumerable<FeatureCatalogEntry> IslandExtensions => All.Where(e => e.IsIslandExtension);

    /// <summary>Extensions installed together with the Dynamic Island on its first install (not Companion, not AI Agents).</summary>
    public static IReadOnlyList<string> IslandInitialExtensions { get; } =
    [
        "notch-calendar", "notch-notifications", "notch-gestures", "notch-timer", "notch-accessories", "notch-lyrics",
        "notch-queue", "notch-live-equalizer", "notch-downloads", "notch-watch",
    ];

    static FeatureCatalog()
    {
        var list = new List<FeatureCatalogEntry>();

        // ---- Windows and taskbar -------------------------------------------------------------------------------
        list.Add(Def("switcher", W, Loc.T("Window switcher"), Loc.T("Switch apps and windows with previews"), "", Both, "A,S", true, "switcher#switcher", ["switcherEnabled"], legacy: "app-switcher"));
        list.Add(Def("dock-preview", W, Loc.T("Taskbar Preview"), Loc.T("Window previews when hovering the taskbar"), "", Ms, "A,S", true, "dock#dockPreview", ["dockPreviewEnabled"], support: WindowsSupport.Adapted));
        list.Add(Def("dock-click", W, Loc.T("Taskbar clicks"), Loc.T("Click a taskbar icon to minimize or cycle windows"), "", Ms, "A", true, "dock#dockClick", ["dockClickMinimize", "dockClickHide", "dockClickCycleWindows"], support: WindowsSupport.Adapted));
        list.Add(Def("window-maximizer", W, Loc.T("Maximize windows"), Loc.T("The green button maximizes instead of full screen"), "", Ms, "A", true, "window-layout#windowMaximizer", ["windowMaximizeEnabled"], support: WindowsSupport.Unavailable, reason: "Every Windows window already has its own maximize button."));
        list.Add(Def("window-layout", W, Loc.T("Window layout"), Loc.T("Arrange windows with shortcuts or edge snapping, then adjust them by dragging"), "", EnergyProfile.Pointer, "A", true, "window-layout", ["windowLayoutShortcutsEnabled", "windowEdgeSnapEnabled", "windowGestureEnabled", "pointerDisplayEnabled"], ["windowLayoutShortcutsEnabled"], legacy: "window-layout"));
        list.Add(Def("auto-quit", W, Loc.T("Quit on close"), Loc.T("Quit apps when their last window closes"), "", Both, "A", true, "quit-on-close", ["autoQuitEnabled"]));
        list.Add(Def("spaces-order", W, Loc.T("Keep Spaces in a fixed order"), Loc.T("Stops macOS from rearranging Spaces by most recent use, so they stay in the order you set. Your previous setting returns when this is turned off. The Dock may restart once to apply the change."), "", Idle, "", false, "dock#spacesOrder", ["spacesOrderEnabled"], support: WindowsSupport.Unavailable, reason: "Windows has no Spaces."));

        // ---- Mouse and keyboard --------------------------------------------------------------------------------
        list.Add(Def("scroll-inverter", M, Loc.T("Invert mouse scrolling"), Loc.T("Invert the mouse wheel direction"), "", Ms, "A", true, "mouse#scrollDirection", ["scrollInverterEnabled", "scrollInverterHorizontalEnabled"], legacy: "invert-scroll"));
        list.Add(Def("scroll-horizontal", M, Loc.T("Scroll sideways while holding a key"), Loc.T("Hold only the selected modifier to scroll the vertical mouse wheel horizontally. Other key combinations are unchanged."), "", Ms, "A", false, "mouse#scrollDirection", ["scrollHorizontalEnabled"], legacy: "horizontal-scroll"));
        list.Add(Def("scroll-zoom", M, Loc.T("Zoom"), Loc.T("Pinch Zoom"), "", Ms, "A", false, "mouse#scrollZoom", ["pinchZoomEnabled"], support: WindowsSupport.Adapted));
        list.Add(Def("focus-follows-mouse", M, Loc.T("Focus follows mouse"), Loc.T("Focuses the window under the pointer."), "", Ms, "A", false, "mouse#focusFollowsMouse", ["focusFollowsMouseEnabled"], legacy: "focus-follows-mouse"));
        list.Add(Def("smooth-scroll", M, Loc.T("Smooth scrolling"), Loc.T("Smooth, animated mouse scrolling"), "", Ms, "A", true, "mouse#smoothScroll", ["smoothScrollEnabled"], legacy: "smooth-scroll"));
        list.Add(Def("linear-scroll", M, Loc.T("Linear scrolling"), Loc.T("Every notch of the mouse wheel scrolls the same distance, no matter how fast it spins. The touchpad is not affected."), "", Ms, "A", false, "mouse#linearScroll", ["linearScrollEnabled"], legacy: "linear-scroll"));
        list.Add(Def("mouse-acceleration", M, Loc.T("Disable mouse acceleration"), Loc.T("Removes pointer acceleration for connected mice. Your previous setting returns when this is turned off or Trayloom quits."), "", Idle, "", true, "mouse#mouseAcceleration", ["mouseAccelerationDisabled"], legacy: "mouse-acceleration"));
        list.Add(Def("mouse-navigation", M, Loc.T("Side buttons"), Loc.T("Side mouse buttons go back and forward"), "", Ms, "A", true, "mouse#mouseNavigation", ["mouseNavigationEnabled"], legacy: "side-buttons"));
        list.Add(Def("mouse-button-shortcuts", M, Loc.T("Mouse button shortcuts"), Loc.T("Extra buttons and side-wheel directions press a key combination you choose."), "", Ms, "A", true, "mouse#mouseButtonShortcuts", ["mouseButtonShortcutsEnabled", "mouseSpacesGestureEnabled"], legacy: "mouse-button-shortcuts"));
        list.Add(Def("middle-click", M, Loc.T("Touchpad middle click"), Loc.T("Three finger click acts as a middle click"), "", Ms, "A", true, "mouse#middleClick", ["middleClickEnabled"], support: WindowsSupport.Unavailable, reason: "Windows sets three-finger taps in Settings > Bluetooth & devices > Touchpad."));
        list.Add(Def("mouse-click-debounce", M, Loc.T("Extra click filter"), Loc.T("Ignores rapid extra clicks from worn mouse buttons without slowing normal clicks."), "", Ms, "A", true, "mouse#mouseClickDebounce", ["mouseClickDebounceEnabled"], legacy: "click-debounce"));
        list.Add(Def("keyboard-debounce", M, Loc.T("Debounce"), Loc.T("Ignore accidental double key presses"), "", Kb, "A", true, "debounce", ["keyboardDebounceEnabled"], legacy: "key-debounce"));
        list.Add(Def("text-snippets", M, Loc.T("Text snippets"), Loc.T("Short triggers expand into full text"), "", Both, "A", true, "text-snippets", ["textSnippetsEnabled", "snippetLibraryEnabled"], legacy: "text-snippets"));
        list.Add(Def("super-key", M, Loc.T("Super key"), Loc.T("Turns one key into the modifier combination you choose."), "", Both, "A", true, "super-key", ["superKeyEnabled"], legacy: "super-key"));
        list.Add(Def("quit-window-protection", M, Loc.T("Quit & close protection"), Loc.T("Protects Alt+F4 and Ctrl+W from accidental presses"), "", Kb, "A", true, "quit-protection", ["quitProtectionQuitEnabled", "quitProtectionCloseEnabled"], legacy: "close-protection"));

        // ---- Clipboard and files -------------------------------------------------------------------------------
        list.Add(Def("clipboard-history", C, Loc.T("Clipboard"), Loc.T("Keep a local history of what you copy"), "", Per, "", true, "clipboard", ["clipboardHistoryEnabled"], legacy: "clipboard-history"));
        list.Add(Def("paste-plain", C, Loc.T("Paste as plain text"), Loc.T("Paste text without formatting"), "", Idle, "A", true, "clipboard#pastePlain", ["pastePlainEnabled"], legacy: "paste-plain"));
        list.Add(Def("finder-cut-paste", C, Loc.T("Cut & paste"), Loc.T("Cut and paste files in File Explorer"), "", Kb, "A", true, "cut-paste", ["finderCutPasteEnabled", "finderPasteImageAsFile"], support: WindowsSupport.Adapted));
        list.Add(Def("finder-rename", C, Loc.T("Rename shortcut"), Loc.T("Rename the selected file or folder with a shortcut you choose."), "", Kb, "A", true, "cut-paste#rename", ["finderRenameEnabled"], support: WindowsSupport.Adapted));
        list.Add(Def("shelf", C, Loc.T("Shelf"), Loc.T("Drop files on the screen edge to hold them"), "", Ms, "", true, "shelf", ["shelfEnabled"], legacy: "shelf"));
        list.Add(Def("url-cleaner", C, Loc.T("Clean URL"), Loc.T("Copied links lose their tracking junk"), "", Per, "", true, "clean-url", ["urlCleanerEnabled"], legacy: "clean-url"));
        list.Add(Def("disk-image-installer", C, Loc.T("Disk image installer"), Loc.T("Install the single app inside a disk image and clean up the download"), "", Idle, "AM", false, "features", [], support: WindowsSupport.Unavailable, reason: "Not available on Windows yet."));

        // ---- Sound ---------------------------------------------------------------------------------------------
        list.Add(Def("mixer", S, Loc.T("Volume mixer"), Loc.T("Per-app volume, pinning and custom order"), "", Idle, "C,A", true, "general#mixer", [], legacy: "volume-mixer"));
        list.Add(Def("sound-output-switcher", S, Loc.T("Output switcher"), Loc.T("Cycle sound outputs with a shortcut"), "", Idle, "", true, "general#soundOutputSwitcher", [], legacy: "output-switcher"));
        list.Add(Def("audio-priority", S, Loc.T("Audio device priority"), Loc.T("Automatically use your preferred audio devices"), "", Idle, "", false, "general#audioPriority", ["audioPriorityOutputEnabled", "audioPriorityInputEnabled"], ["audioPriorityOutputEnabled", "audioPriorityInputEnabled"], legacy: "audio-priority"));
        list.Add(Def("mic-mute", S, Loc.T("Mute microphone"), Loc.T("Mute the microphone from anywhere"), "", Idle, "", true, "quick-tools#micMute", [], legacy: "mic-mute"));
        list.Add(Def("music-block", S, Loc.T("Music app blocker"), Loc.T("Block music launches from detected media keys"), "", Kb, "A", true, "general#musicBlocking", ["musicBlockEnabled"], support: WindowsSupport.Adapted));

        // ---- Energy and display --------------------------------------------------------------------------------
        list.Add(Def("keep-awake", E, Loc.T("Keep awake"), Loc.T("Keep the PC awake on demand"), "", Idle, "A", true, "energy#keepAwake", [], legacy: "keep-awake"));
        list.Add(Def("brightness", E, Loc.T("Displays"), Loc.T("Brightness and power controls for every display"), "", Idle, "A", true, "energy#brightness", ["brightnessControlEnabled"], legacy: "displays"));
        list.Add(Def("extra-brightness", E, Loc.T("Extra brightness"), Loc.T("Extra brightness on HDR displays"), "", Per, "", true, "energy#extraBrightness", ["extraBrightnessEnabled"], support: WindowsSupport.Unavailable, reason: "Windows does not expose an HDR brightness boost to apps."));
        list.Add(Def("bluetooth-sleep", E, Loc.T("Bluetooth on sleep"), Loc.T("Switches Bluetooth off while the PC sleeps, so headphones in a bag stop connecting to it."), "", Idle, "", true, "energy#bluetoothSleep", ["bluetoothSleepEnabled"]));

        // ---- Tools ---------------------------------------------------------------------------------------------
        list.Add(Def("quick-launcher", T, Loc.T("Quick panel"), Loc.T("A floating panel with your favorite tools"), "", Idle, "", true, "quick-tools#quickLauncher", ["quickLauncherShortcutEnabled"]));
        list.Add(Def("quick-toggles", T, Loc.T("Quick toggles"), Loc.T("One-click actions like dark mode and Recycle Bin"), "", Idle, "", true, "quick-tools#quickToggles", [], legacy: "quick-toggles"));
        list.Add(Def("quit-apps", T, Loc.T("Quit apps"), Loc.T("Lists your open apps to quit one by one, all at once, or all but the one in front. Always shows the list before anything quits."), "", Idle, "", false, "quick-tools#quitApps", []));
        list.Add(Def("color-picker", T, Loc.T("Color picker"), Loc.T("Pick any color on screen"), "", Idle, "", true, "screen-capture#colorPicker", [], legacy: "color-picker"));
        list.Add(Def("screen-ocr", T, Loc.T("Copy text from screen"), Loc.T("Copy text or QR codes from anything on screen"), "", Idle, "S", true, "screen-capture#screenOCR", [], legacy: "screen-ocr"));
        list.Add(Def("cleaning-mode", T, Loc.T("Cleaning Mode"), Loc.T("Lock keyboard and screen for cleaning"), "", Idle, "A", true, "quick-tools#cleaningMode", [], legacy: "cleaning-mode"));
        list.Add(Def("media-tools", T, Loc.T("Media"), Loc.T("Compress videos, images and GIFs"), "", Idle, "", true, "media", [], legacy: "media-tools"));
        list.Add(Def("cleaner", T, Loc.T("Cleaner"), Loc.T("Clear caches and junk files"), "", Idle, "D,F,N", true, "cleaner", [], legacy: "cleaner"));
        list.Add(Def("uninstaller", T, Loc.T("Uninstaller"), Loc.T("Remove apps and their leftovers"), "", Idle, "D,AF", true, "uninstaller", [], legacy: "uninstaller"));
        list.Add(Def("homebrew", T, "Packages", Loc.T("Keep winget packages up to date"), "", Idle, "AM", true, "packages", [], support: WindowsSupport.Adapted));
        list.Add(Def("app-updates", T, Loc.T("App updates"), Loc.T("Find and install updates for the apps you have"), "", Idle, "N,AM", true, "app-updates", [], legacy: "app-updates"));
        list.Add(Def("screenshot", T, Loc.T("Screenshot"), Loc.T("Captures an area, window or screen and annotates it"), "", Idle, "S", true, "screen-capture#screenshot", [], legacy: "screenshot"));
        list.Add(Def("screen-recorder", T, Loc.T("Screen recording"), Loc.T("Records an area, window or screen and edits it afterwards"), "", Idle, "S,A,C,Mi", true, "screen-capture#screenRecorder", []));
        list.Add(Def("camera-preview", T, Loc.T("Camera preview"), Loc.T("Opens a floating mirror with your camera"), "", Idle, "Ca", true, "quick-tools#cameraPreview", [], legacy: "camera-preview"));
        list.Add(Def("radial-menu", T, Loc.T("Radial menu"), Loc.T("Opens a wheel of your favorite actions around the pointer"), "", Idle, "A", true, "radial-menu", [], legacy: "radial-menu"));
        list.Add(Def("scratchpad", T, Loc.T("Scratchpad"), Loc.T("Floating pads for short-lived notes"), "", Idle, "", true, "quick-tools#scratchpad", [], legacy: "scratchpad"));
        list.Add(Def("command-bar", T, Loc.T("Command Bar"), Loc.T("One field that finds and runs everything the app does"), "", Idle, "A", true, "command-bar", [], legacy: "command-bar"));
        list.Add(Def("wallpaper", T, Loc.T("Wallpaper"), Loc.T("Pick a still wallpaper without opening Settings"), "", Idle, "", false, "quick-tools#wallpaper", [], legacy: "wallpaper"));
        list.Add(Def("kill-process", T, Loc.T("Kill Process"), Loc.T("Search running processes and force quit, restart, or kill process trees"), "", Idle, "", false, "kill-process", [], beta: true, legacy: "kill-process"));
        list.Add(Def("port-manager", T, Loc.T("Port Manager"), Loc.T("View active listening ports and, with Kill Process installed, terminate the processes using them"), "", Idle, "", false, "port-manager", [], legacy: "port-manager"));

        // ---- Dynamic Island ------------------------------------------------------------------------------------
        list.Add(Def("notch", I, Loc.T("Dynamic Island"), Loc.T("Your music, controls and everyday tools, together at the top of your screen. Optional. Turn it off to keep using the separate panels."), "", Per, "A,AP", true, "island", ["notchEnabled", "notchShortcutEnabled"], ["notchEnabled"], experimental: true, legacy: "island"));
        list.Add(Def("notch-calendar", I, Loc.T("Calendar"), Loc.T("Browse the month and your upcoming appointments in the Dynamic Island."), "", Per, "Cal", true, "island", [], legacy: "island-calendar"));
        list.Add(Def("notch-notifications", I, Loc.T("Notifications"), Loc.T("New system notifications in the Dynamic Island."), "", Idle, "A", true, "island", [], support: WindowsSupport.Adapted));
        list.Add(Def("notch-gestures", I, Loc.T("Dynamic Island Gestures"), Loc.T("Open and close the Dynamic Island with scrolling, and swipe to change tracks."), "", Idle, "", true, "island", [], legacy: "island-gestures"));
        list.Add(Def("notch-timer", I, Loc.T("Timer"), Loc.T("Timers, a stopwatch and focused work sessions in the Dynamic Island."), "", Idle, "", true, "island", [], legacy: "island-timer"));
        list.Add(Def("notch-screen-time", I, Loc.T("Screen time"), Loc.T("See how long you used the PC today, and in which apps, in the Dynamic Island. App names and times stay on this PC."), "\uE916", Per, "A", false, "island#screenTime", ["notchScreenTimeEnabled"], legacy: "screen-time"));
        list.Add(Def("notch-accessories", I, Loc.T("Accessory alerts"), Loc.T("Show connected accessories and warn once when their battery falls to 20%."), "", Per, "", true, "island", []));
        list.Add(Def("notch-lyrics", I, Loc.T("Lyrics"), Loc.T("Follow the lyrics of the current song in the Dynamic Island."), "", Per, "", true, "island", []));
        list.Add(Def("notch-queue", I, Loc.T("Up next"), Loc.T("See the actual upcoming songs shared by your player."), "", Idle, "", true, "island", []));
        list.Add(Def("notch-live-equalizer", I, Loc.T("Bars follow the music"), Loc.T("Move the Dynamic Island bars with the sound your player makes."), "", Per, "C", true, "island", []));
        list.Add(Def("notch-downloads", I, Loc.T("Downloads"), Loc.T("See files arriving in a folder you choose, directly in the Dynamic Island."), "", Idle, "F", true, "island", [], legacy: "island-downloads"));
        list.Add(Def("notch-agents", I, Loc.T("AI Agents"), Loc.T("Follow plan limits, tokens, API value and the work in progress of Claude, Codex, OpenCode and GitHub Copilot in the Dynamic Island."), "", Per, "", true, "island", ["notchAgentsEnabled"]));
        list.Add(Def("notch-github", I, Loc.T("GitHub"), Loc.T("Your unread notifications, pull requests, reviews and failing checks in the Dynamic Island. Uses the GitHub CLI you are signed in to, or a token you paste. Off until you connect."), "\uE943", Per, "", false, "island#github", [], legacy: "github"));
        list.Add(Def("notch-watch", I, Loc.T("Watch"), Loc.T("Turn any part of any window into a live activity in the Dynamic Island, with an alert when it changes, finishes or shows what you are waiting for."), "", Idle, "S,N", true, "island", []));
        list.Add(Def("notch-mascot", I, Loc.T("Companion"), Loc.T("A little friend in the Dynamic Island who says hello now and then and reacts to what happens on your PC."), "", Per, "", false, "island#companion", ["notchMascotEnabled"]));

        // ---- System monitor ------------------------------------------------------------------------------------
        list.Add(Def("monitor-cpu", Mon, Loc.T("CPU"), Loc.T("Processor usage and temperature"), "", Per, "N", true, "monitor", [], legacy: "monitor-cpu"));
        list.Add(Def("monitor-gpu", Mon, Loc.T("GPU"), Loc.T("Graphics usage and temperature"), "", Per, "", true, "monitor", [], legacy: "monitor-gpu"));
        list.Add(Def("monitor-memory", Mon, Loc.T("Memory"), Loc.T("Memory use and pressure"), "", Per, "N", true, "monitor", [], legacy: "monitor-memory"));
        list.Add(Def("monitor-network", Mon, Loc.T("Network"), Loc.T("Network speed and usage"), "", Per, "", true, "monitor", [], legacy: "monitor-network"));
        list.Add(Def("monitor-disk", Mon, Loc.T("Disks"), Loc.T("Disk space and activity"), "", Per, "N", true, "monitor", [], legacy: "monitor-disk"));
        list.Add(Def("monitor-power", Mon, Loc.T("Power"), Loc.T("Battery, power and charging"), "", Per, "N", true, "monitor", [], legacy: "monitor-power"));
        list.Add(Def("connected-devices", Mon, Loc.T("Connected Devices"), Loc.T("Count connected external USB peripherals"), "", Per, "", true, "monitor", [], legacy: "connected-devices"));
        list.Add(Def("fan-control", Mon, Loc.T("Fan Control"), Loc.T("Control fans manually or with temperature curves while seeing live and target RPM"), "", Idle, "", false, "monitor#fanControl", [], beta: true, support: WindowsSupport.Unavailable, reason: "No fans found. Windows has no supported way to control fans."));

        All = list;
        ById = list.ToDictionary(e => e.Id, StringComparer.Ordinal);
        ByAlias = new Dictionary<string, FeatureCatalogEntry>(StringComparer.Ordinal);
        foreach (var entry in list)
        {
            ByAlias[entry.Id] = entry;
            ByAlias[entry.RawId] = entry;
            if (entry.LegacyId is not null)
            {
                ByAlias[entry.LegacyId] = entry;
            }
        }
    }

    /// <summary>The entry with this catalog id (null when unknown).</summary>
    public static FeatureCatalogEntry? Get(string id) => ById.GetValueOrDefault(id);

    /// <summary>Entry lookup that also accepts Vorssaint raw ids and the ids v1 feature classes used.</summary>
    public static bool TryResolve(string idOrAlias, [NotNullWhen(true)] out FeatureCatalogEntry? entry) =>
        ByAlias.TryGetValue(idOrAlias, out entry);

    /// <summary>Resolves a catalog id, a raw id or a v1 id to the catalog id (the input is returned unchanged when unknown).</summary>
    public static string Resolve(string idOrAlias) => ByAlias.TryGetValue(idOrAlias, out var e) ? e.Id : idOrAlias;

    /// <summary>All entries of a group in catalog order.</summary>
    public static IEnumerable<FeatureCatalogEntry> InGroup(FeatureGroup group) => All.Where(e => e.Group == group);

    /// <summary>Features that count as engaged for the Permissions page: entries listing <paramref name="permission"/>.</summary>
    public static IEnumerable<FeatureCatalogEntry> UsingPermission(AppPermission permission) => All.Where(e => e.Permissions.Contains(permission));

    private static readonly Dictionary<string, AppPermission> PermissionCodes = new(StringComparer.Ordinal)
    {
        ["A"] = AppPermission.Accessibility,
        ["S"] = AppPermission.ScreenRecording,
        ["D"] = AppPermission.FullDiskAccess,
        ["F"] = AppPermission.FilesAndFolders,
        ["N"] = AppPermission.Notifications,
        ["AF"] = AppPermission.AutomationFinder,
        ["AP"] = AppPermission.AutomationPlayback,
        ["C"] = AppPermission.AudioCapture,
        ["Mi"] = AppPermission.Microphone,
        ["Ca"] = AppPermission.Camera,
        ["AM"] = AppPermission.AppManagement,
        ["Cal"] = AppPermission.Calendar,
    };

    private static FeatureCatalogEntry Def(
        string id,
        FeatureGroup group,
        string title,
        string description,
        string glyph,
        EnergyProfile energy,
        string permissions,
        bool defaultInstalled,
        string destination,
        string[] enabledKeys,
        string[]? initial = null,
        bool beta = false,
        bool experimental = false,
        WindowsSupport support = WindowsSupport.Supported,
        string? reason = null,
        string? legacy = null)
    {
        var perms = permissions.Length == 0
            ? []
            : permissions.Split(',', StringSplitOptions.TrimEntries).Select(p => PermissionCodes[p]).ToArray();
        // First enable key is what a first install switches on (unless the entry lists its own initial keys).
        initial ??= enabledKeys.Length > 0 ? [enabledKeys[0]] : [];
        return new FeatureCatalogEntry(id, ToCamel(id), group, title, description, glyph, energy, perms, defaultInstalled, destination, enabledKeys, initial, beta, experimental, support, reason, legacy);
    }

    private static string ToCamel(string kebab)
    {
        switch (kebab)
        {
            case "monitor-cpu": return "monitorCPU";
            case "monitor-gpu": return "monitorGPU";
            case "screen-ocr": return "screenOCR";
        }

        var parts = kebab.Split('-');
        return parts[0] + string.Concat(parts.Skip(1).Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }
}
