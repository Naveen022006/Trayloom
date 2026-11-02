// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics.CodeAnalysis;

namespace WinKnife.Core;

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
    public static IReadOnlyList<FeaturePreset> Presets { get; } =
    [
        new("essentials", "Essentials", "Volume mixer, system monitor and keep awake.", "",
            ["mixer", "keep-awake", "monitor-cpu", "monitor-gpu", "monitor-memory", "monitor-network", "monitor-disk", "monitor-power"], []),
        new("windows", "Windows", "Window switcher, window layout and the taskbar features.", "",
            ["switcher", "window-layout", "dock-preview", "dock-click", "window-maximizer"],
            ["switcherEnabled", "dockPreviewEnabled", "dockClickMinimize", "windowMaximizeEnabled"]),
        new("battery", "Battery and quiet", "A lean monitor with battery, memory and processor. Nothing listens to input.", "",
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
        list.Add(Def("switcher", W, "Window switcher", "Switch apps and windows with previews", "", Both, "A,S", true, "switcher#switcher", ["switcherEnabled"], legacy: "app-switcher"));
        list.Add(Def("dock-preview", W, "Taskbar Preview", "Window previews when hovering the taskbar", "", Ms, "A,S", true, "dock#dockPreview", ["dockPreviewEnabled"], support: WindowsSupport.Adapted));
        list.Add(Def("dock-click", W, "Taskbar clicks", "Click a taskbar icon to minimize or cycle windows", "", Ms, "A", true, "dock#dockClick", ["dockClickMinimize", "dockClickHide", "dockClickCycleWindows"], support: WindowsSupport.Adapted));
        list.Add(Def("window-maximizer", W, "Maximize windows", "The green button maximizes instead of full screen", "", Ms, "A", true, "window-layout#windowMaximizer", ["windowMaximizeEnabled"], support: WindowsSupport.Unavailable, reason: "Windows windows already maximize with their own button."));
        list.Add(Def("window-layout", W, "Window layout", "Arrange windows with shortcuts or edge snapping, then adjust them by dragging", "", EnergyProfile.Pointer, "A", true, "window-layout", ["windowLayoutShortcutsEnabled", "windowEdgeSnapEnabled", "windowGestureEnabled", "pointerDisplayEnabled"], ["windowLayoutShortcutsEnabled"], legacy: "window-layout"));
        list.Add(Def("auto-quit", W, "Quit on close", "Quit apps when their last window closes", "", Both, "A", true, "quit-on-close", ["autoQuitEnabled"]));
        list.Add(Def("spaces-order", W, "Keep Spaces in a fixed order", "Stops macOS from rearranging Spaces by most recent use, so they stay in the order you set. Your previous setting returns when this is turned off. The Dock may restart once to apply the change.", "", Idle, "", false, "dock#spacesOrder", ["spacesOrderEnabled"], support: WindowsSupport.Unavailable, reason: "Windows has no Spaces."));

        // ---- Mouse and keyboard --------------------------------------------------------------------------------
        list.Add(Def("scroll-inverter", M, "Invert mouse scrolling", "Invert the mouse wheel direction", "", Ms, "A", true, "mouse#scrollDirection", ["scrollInverterEnabled", "scrollInverterHorizontalEnabled"], legacy: "invert-scroll"));
        list.Add(Def("scroll-horizontal", M, "Scroll sideways while holding a key", "Hold only the selected modifier to scroll the vertical mouse wheel horizontally. Other key combinations are unchanged.", "", Ms, "A", false, "mouse#scrollDirection", ["scrollHorizontalEnabled"], legacy: "horizontal-scroll"));
        list.Add(Def("scroll-zoom", M, "Zoom", "Pinch Zoom", "", Ms, "A", false, "mouse#scrollZoom", ["pinchZoomEnabled"], support: WindowsSupport.Adapted));
        list.Add(Def("focus-follows-mouse", M, "Focus follows mouse", "Focuses the window under the pointer.", "", Ms, "A", false, "mouse#focusFollowsMouse", ["focusFollowsMouseEnabled"], legacy: "focus-follows-mouse"));
        list.Add(Def("smooth-scroll", M, "Smooth scrolling", "Smooth, animated mouse scrolling", "", Ms, "A", true, "mouse#smoothScroll", ["smoothScrollEnabled"], legacy: "smooth-scroll"));
        list.Add(Def("linear-scroll", M, "Linear scrolling", "Every notch of the mouse wheel scrolls the same distance, no matter how fast it spins. The touchpad is not affected.", "", Ms, "A", false, "mouse#linearScroll", ["linearScrollEnabled"], legacy: "linear-scroll"));
        list.Add(Def("mouse-acceleration", M, "Disable mouse acceleration", "Removes pointer acceleration for connected mice. Your previous setting returns when this is turned off or WinKnife quits.", "", Idle, "", true, "mouse#mouseAcceleration", ["mouseAccelerationDisabled"], legacy: "mouse-acceleration"));
        list.Add(Def("mouse-navigation", M, "Side buttons", "Side mouse buttons go back and forward", "", Ms, "A", true, "mouse#mouseNavigation", ["mouseNavigationEnabled"], legacy: "side-buttons"));
        list.Add(Def("mouse-button-shortcuts", M, "Mouse button shortcuts", "Extra buttons and side-wheel directions press a key combination you choose.", "", Ms, "A", true, "mouse#mouseButtonShortcuts", ["mouseButtonShortcutsEnabled", "mouseSpacesGestureEnabled"], legacy: "mouse-button-shortcuts"));
        list.Add(Def("middle-click", M, "Touchpad middle click", "Three finger click acts as a middle click", "", Ms, "A", true, "mouse#middleClick", ["middleClickEnabled"], support: WindowsSupport.Unavailable, reason: "Windows sets three-finger taps in Settings > Bluetooth & devices > Touchpad."));
        list.Add(Def("mouse-click-debounce", M, "Extra click filter", "Ignores rapid extra clicks from worn mouse buttons without slowing normal clicks.", "", Ms, "A", true, "mouse#mouseClickDebounce", ["mouseClickDebounceEnabled"], legacy: "click-debounce"));
        list.Add(Def("keyboard-debounce", M, "Debounce", "Ignore accidental double key presses", "", Kb, "A", true, "debounce", ["keyboardDebounceEnabled"], legacy: "key-debounce"));
        list.Add(Def("text-snippets", M, "Text snippets", "Short triggers expand into full text", "", Both, "A", true, "text-snippets", ["textSnippetsEnabled", "snippetLibraryEnabled"], legacy: "text-snippets"));
        list.Add(Def("super-key", M, "Super key", "Turns one key into the modifier combination you choose.", "", Both, "A", true, "super-key", ["superKeyEnabled"], legacy: "super-key"));
        list.Add(Def("quit-window-protection", M, "Quit & close protection", "Protects Alt+F4 and Ctrl+W from accidental presses", "", Kb, "A", true, "quit-protection", ["quitProtectionQuitEnabled", "quitProtectionCloseEnabled"], legacy: "close-protection"));

        // ---- Clipboard and files -------------------------------------------------------------------------------
        list.Add(Def("clipboard-history", C, "Clipboard", "Keep a local history of what you copy", "", Per, "", true, "clipboard", ["clipboardHistoryEnabled"], legacy: "clipboard-history"));
        list.Add(Def("paste-plain", C, "Paste as plain text", "Paste text without formatting", "", Idle, "A", true, "clipboard#pastePlain", ["pastePlainEnabled"], legacy: "paste-plain"));
        list.Add(Def("finder-cut-paste", C, "Cut & paste", "Cut and paste files in File Explorer", "", Kb, "A", true, "cut-paste", ["finderCutPasteEnabled", "finderPasteImageAsFile"], support: WindowsSupport.Adapted));
        list.Add(Def("finder-rename", C, "Rename shortcut", "Rename the selected file or folder with a shortcut you choose.", "", Kb, "A", true, "cut-paste#rename", ["finderRenameEnabled"], support: WindowsSupport.Adapted));
        list.Add(Def("shelf", C, "Shelf", "Drop files on the screen edge to hold them", "", Ms, "", true, "shelf", ["shelfEnabled"], legacy: "shelf"));
        list.Add(Def("url-cleaner", C, "Clean URL", "Copied links lose their tracking junk", "", Per, "", true, "clean-url", ["urlCleanerEnabled"], legacy: "clean-url"));
        list.Add(Def("disk-image-installer", C, "Disk image installer", "Install the single app inside a disk image and clean up the download", "", Idle, "AM", false, "features", [], support: WindowsSupport.Unavailable, reason: "Not available on Windows yet."));

        // ---- Sound ---------------------------------------------------------------------------------------------
        list.Add(Def("mixer", S, "Volume mixer", "Per-app volume, pinning and custom order", "", Idle, "C,A", true, "general#mixer", [], legacy: "volume-mixer"));
        list.Add(Def("sound-output-switcher", S, "Output switcher", "Cycle sound outputs with a shortcut", "", Idle, "", true, "general#soundOutputSwitcher", [], legacy: "output-switcher"));
        list.Add(Def("audio-priority", S, "Audio device priority", "Automatically use your preferred audio devices", "", Idle, "", false, "general#audioPriority", ["audioPriorityOutputEnabled", "audioPriorityInputEnabled"], ["audioPriorityOutputEnabled", "audioPriorityInputEnabled"], legacy: "audio-priority"));
        list.Add(Def("mic-mute", S, "Mute microphone", "Mute the microphone from anywhere", "", Idle, "", true, "quick-tools#micMute", [], legacy: "mic-mute"));
        list.Add(Def("music-block", S, "Music app blocker", "Block music launches from detected media keys", "", Kb, "A", true, "general#musicBlocking", ["musicBlockEnabled"], support: WindowsSupport.Adapted));

        // ---- Energy and display --------------------------------------------------------------------------------
        list.Add(Def("keep-awake", E, "Keep awake", "Keep the PC awake on demand", "", Idle, "A", true, "energy#keepAwake", [], legacy: "keep-awake"));
        list.Add(Def("brightness", E, "Displays", "Brightness and power controls for every display", "", Idle, "A", true, "energy#brightness", ["brightnessControlEnabled"], legacy: "displays"));
        list.Add(Def("extra-brightness", E, "Extra brightness", "Extra brightness on HDR displays", "", Per, "", true, "energy#extraBrightness", ["extraBrightnessEnabled"], support: WindowsSupport.Unavailable, reason: "Windows does not expose an HDR brightness boost to apps."));
        list.Add(Def("bluetooth-sleep", E, "Bluetooth on sleep", "Switches Bluetooth off while the PC sleeps, so headphones in a bag stop connecting to it.", "", Idle, "", true, "energy#bluetoothSleep", ["bluetoothSleepEnabled"]));

        // ---- Tools ---------------------------------------------------------------------------------------------
        list.Add(Def("quick-launcher", T, "Quick panel", "A floating panel with your favorite tools", "", Idle, "", true, "quick-tools#quickLauncher", ["quickLauncherShortcutEnabled"]));
        list.Add(Def("quick-toggles", T, "Quick toggles", "One-click actions like dark mode and Recycle Bin", "", Idle, "", true, "quick-tools#quickToggles", [], legacy: "quick-toggles"));
        list.Add(Def("quit-apps", T, "Quit apps", "Lists your open apps to quit one by one, all at once, or all but the one in front. Always shows the list before anything quits.", "", Idle, "", false, "quick-tools#quitApps", []));
        list.Add(Def("color-picker", T, "Color picker", "Pick any color on screen", "", Idle, "", true, "screen-capture#colorPicker", [], legacy: "color-picker"));
        list.Add(Def("screen-ocr", T, "Copy text from screen", "Copy text or QR codes from anything on screen", "", Idle, "S", true, "screen-capture#screenOCR", [], legacy: "screen-ocr"));
        list.Add(Def("cleaning-mode", T, "Cleaning Mode", "Lock keyboard and screen for cleaning", "", Idle, "A", true, "quick-tools#cleaningMode", [], legacy: "cleaning-mode"));
        list.Add(Def("media-tools", T, "Media", "Compress videos, images and GIFs", "", Idle, "", true, "media", [], legacy: "media-tools"));
        list.Add(Def("cleaner", T, "Cleaner", "Clear caches and junk files", "", Idle, "D,F,N", true, "cleaner", [], legacy: "cleaner"));
        list.Add(Def("uninstaller", T, "Uninstaller", "Remove apps and their leftovers", "", Idle, "D,AF", true, "uninstaller", [], legacy: "uninstaller"));
        list.Add(Def("homebrew", T, "Packages", "Keep winget packages up to date", "", Idle, "AM", true, "packages", [], support: WindowsSupport.Adapted));
        list.Add(Def("app-updates", T, "App updates", "Find and install updates for the apps you have", "", Idle, "N,AM", true, "app-updates", [], legacy: "app-updates"));
        list.Add(Def("screenshot", T, "Screenshot", "Captures an area, window or screen and annotates it", "", Idle, "S", true, "screen-capture#screenshot", [], legacy: "screenshot"));
        list.Add(Def("screen-recorder", T, "Screen recording", "Records an area, window or screen and edits it afterwards", "", Idle, "S,A,C,Mi", true, "screen-capture#screenRecorder", []));
        list.Add(Def("camera-preview", T, "Camera preview", "Opens a floating mirror with your camera", "", Idle, "Ca", true, "quick-tools#cameraPreview", [], legacy: "camera-preview"));
        list.Add(Def("radial-menu", T, "Radial menu", "Opens a wheel of your favorite actions around the pointer", "", Idle, "A", true, "radial-menu", [], legacy: "radial-menu"));
        list.Add(Def("scratchpad", T, "Scratchpad", "Floating pads for short-lived notes", "", Idle, "", true, "quick-tools#scratchpad", [], legacy: "scratchpad"));
        list.Add(Def("command-bar", T, "Command Bar", "One field that finds and runs everything the app does", "", Idle, "A", true, "command-bar", [], legacy: "command-bar"));
        list.Add(Def("wallpaper", T, "Wallpaper", "Pick a still wallpaper without opening Settings", "", Idle, "", false, "quick-tools#wallpaper", [], legacy: "wallpaper"));
        list.Add(Def("kill-process", T, "Kill Process", "Search running processes and force quit, restart, or kill process trees", "", Idle, "", false, "kill-process", [], beta: true, legacy: "kill-process"));
        list.Add(Def("port-manager", T, "Port Manager", "View active listening ports and, with Kill Process installed, terminate the processes using them", "", Idle, "", false, "port-manager", [], legacy: "port-manager"));

        // ---- Dynamic Island ------------------------------------------------------------------------------------
        list.Add(Def("notch", I, "Dynamic Island", "Your music, controls and everyday tools, together at the top of your screen. Optional. Turn it off to keep using the separate panels.", "", Per, "A,AP", true, "island", [], experimental: true, legacy: "island"));
        list.Add(Def("notch-calendar", I, "Calendar", "Browse the month and your upcoming appointments in the Dynamic Island.", "", Per, "Cal", true, "island", [], legacy: "island-calendar"));
        list.Add(Def("notch-notifications", I, "Notifications", "New system notifications in the Dynamic Island.", "", Idle, "A", true, "island", [], support: WindowsSupport.Adapted));
        list.Add(Def("notch-gestures", I, "Dynamic Island Gestures", "Open and close the Dynamic Island with scrolling, and swipe to change tracks.", "", Idle, "", true, "island", [], legacy: "island-gestures"));
        list.Add(Def("notch-timer", I, "Timer", "Timers, a stopwatch and focused work sessions in the Dynamic Island.", "", Idle, "", true, "island", [], legacy: "island-timer"));
        list.Add(Def("notch-accessories", I, "Accessory alerts", "Show connected accessories and warn once when their battery falls to 20%.", "", Per, "", true, "island", []));
        list.Add(Def("notch-lyrics", I, "Lyrics", "Follow the lyrics of the current song in the Dynamic Island.", "", Per, "", true, "island", []));
        list.Add(Def("notch-queue", I, "Up next", "See the actual upcoming songs shared by your player.", "", Idle, "", true, "island", []));
        list.Add(Def("notch-live-equalizer", I, "Bars follow the music", "Move the Dynamic Island bars with the sound your player makes.", "", Per, "C", true, "island", []));
        list.Add(Def("notch-downloads", I, "Downloads", "See files arriving in a folder you choose, directly in the Dynamic Island.", "", Idle, "F", true, "island", [], legacy: "island-downloads"));
        list.Add(Def("notch-agents", I, "AI Agents", "Follow plan limits, tokens, API value and the work in progress of Claude, Codex, OpenCode and GitHub Copilot in the Dynamic Island.", "", Per, "", true, "island", []));
        list.Add(Def("notch-watch", I, "Watch", "Turn any part of any window into a live activity in the Dynamic Island, with an alert when it changes, finishes or shows what you are waiting for.", "", Idle, "S,N", true, "island", []));
        list.Add(Def("notch-mascot", I, "Companion", "A little friend in the Dynamic Island who says hello now and then and reacts to what happens on your PC.", "", Per, "", false, "island#companion", []));

        // ---- System monitor ------------------------------------------------------------------------------------
        list.Add(Def("monitor-cpu", Mon, "CPU", "Processor usage and temperature", "", Per, "N", true, "monitor", [], legacy: "monitor-cpu"));
        list.Add(Def("monitor-gpu", Mon, "GPU", "Graphics usage and temperature", "", Per, "", true, "monitor", [], legacy: "monitor-gpu"));
        list.Add(Def("monitor-memory", Mon, "Memory", "Memory use and pressure", "", Per, "N", true, "monitor", [], legacy: "monitor-memory"));
        list.Add(Def("monitor-network", Mon, "Network", "Network speed and usage", "", Per, "", true, "monitor", [], legacy: "monitor-network"));
        list.Add(Def("monitor-disk", Mon, "Disks", "Disk space and activity", "", Per, "N", true, "monitor", [], legacy: "monitor-disk"));
        list.Add(Def("monitor-power", Mon, "Power", "Battery, power and charging", "", Per, "N", true, "monitor", [], legacy: "monitor-power"));
        list.Add(Def("connected-devices", Mon, "Connected Devices", "Count connected external USB peripherals", "", Per, "", true, "monitor", [], legacy: "connected-devices"));
        list.Add(Def("fan-control", Mon, "Fan Control", "Control fans manually or with temperature curves while seeing live and target RPM", "", Idle, "", false, "monitor#fanControl", [], beta: true, support: WindowsSupport.Unavailable, reason: "No fans found. Windows has no supported way to control fans."));

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
