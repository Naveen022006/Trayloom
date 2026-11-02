// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace WinKnife.Features.WindowTools;

/// <summary>English strings of the Windows and taskbar features (verbatim from the specs, Mac wording replaced). One place so localisation can be added later.</summary>
internal static class Strings
{
    // ---- shared
    public const string Remove = "Remove";
    public const string AddAnApp = "Add an app…";
    public const string ChooseApp = "Choose app";
    public const string ChooseAppEllipsis = "Choose app…";
    public const string Cancel = "Cancel";
    public const string SearchApps = "Search apps";
    public const string NoAppsFound = "No apps found";
    public const string Scanning = "Scanning files…";
    public const string Reset = "Reset";
    public const string None = "None";
    public const string RemoveShortcut = "Remove shortcut";
    public const string Options = "Options";
    public const string MoreOptions = "More options";
    public const string ActiveNow = "Active now";
    public const string PermissionRequired = "Permission required";
    public const string AdminCaption = "Some windows belong to apps running as administrator. Run WinKnife as administrator to control them.";
    public const string AdminButton = "Restart as administrator";
    public const string NoOpenWindow = "No open window";
    public const string NoWindows = "No open windows";
    public const string OtherDesktop = "Other desktop";

    // ---- switcher page
    public const string SwitcherTitle = "Switcher";
    public const string SwitcherDescription = "Switch between apps and windows your way.";
    public const string SwitcherSection = "Window switcher";
    public const string SwitcherUse = "Use the WinKnife switcher";
    public const string SwitcherUseCaption = "Switch between apps and windows, including minimized windows and multiple windows from the same app.";
    public const string Layout = "Layout";
    public const string LayoutWindows = "Window previews";
    public const string LayoutWindowsCaption = "One preview per window, minimized ones included.";
    public const string LayoutIcons = "Large icons";
    public const string LayoutIconsCaption = "Shows one icon per app with that app’s window previews above it.";
    public const string LayoutSimple = "Simple list";
    public const string LayoutSimpleCaption = "Shows app icons and window titles, without previews or screen capture by the switcher.";
    public const string ShortcutApps = "Apps";
    public const string ShortcutWindows = "Windows";
    public const string WindowShortcutCaption = "Opens a switcher for the frontmost app’s windows. While the switcher is open, jumps between the selected app’s windows.";
    public const string AllWindows = "All windows";
    public const string AllWindowsCaption = "Use a separate shortcut to cycle through windows from all apps in any layout. Your window filters still apply.";
    public const string TakeOver = "Replace Windows Alt+Tab and Alt+`";
    public const string TakeOverCaption = "Hides the matching Windows switcher shortcuts only while WinKnife’s switcher is active. All running apps stay reachable.";
    public const string TakeOverWarning = "Windows Alt+Tab will open too.";
    public const string UsageHintFormat = "Hold {0} to navigate; release to activate the window. Shift or ← goes back; W closes the window; Q quits the app; Esc cancels.";
    public const string AppearanceDelay = "Appearance delay";
    public const string AppearanceDelayCaption = "How long the shortcut must be held before the switcher appears.";
    public const string MaxColumns = "Maximum columns";
    public const string MaxColumnsCaption = "Wraps onto more rows after this many items. Useful on ultrawide displays.";
    public const string InstantSelection = "Instant selection";
    public const string InstantSelectionCaption = "Moves the highlight and scroll position immediately as you browse apps and windows.";
    public const string PinSearch = "Pin search with S";
    public const string PinSearchCaption = "S starts a search and pins the switcher open, so typing no longer fires Alt shortcuts, and a search starting with Q or W no longer closes the window or quits the app by mistake.";
    public const string ShortcutHints = "Show shortcut hints";
    public const string ShortcutHintsCaption = "Shows the app and window shortcuts below the icons.";
    public const string MergeTabs = "Show one entry per app";
    public const string MergeTabsCaption = "Collapses all of an app’s windows into one entry in the switcher, instead of one entry per window.";
    public const string FullscreenWindows = "Show full-screen windows";
    public const string MinimizedWindows = "Minimized windows";
    public const string MinNormal = "Normal ordering";
    public const string MinEnd = "Place at end";
    public const string MinHide = "Hide";
    public const string TreatHidden = "Treat hidden apps like minimized windows";
    public const string ShowOn = "Show on";
    public const string ShowOnCaption = "Which display the switcher opens on when more than one is connected.";
    public const string OnPointer = "Display with the pointer";
    public const string OnTaskbar = "Display with the taskbar";
    public const string OnActive = "Display with the active window";
    public const string CurrentDisplayOnly = "Show only the current display";
    public const string CurrentDisplayOnlyCaption = "Lists only windows on the display under the pointer. If that display has no windows, the switcher does not open.";
    public const string CurrentDesktopOnly = "Show only the current desktop";
    public const string CurrentDesktopOnlyCaption = "Lists windows from the current virtual desktop.";
    public const string WindowlessApps = "Apps with no open window";
    public const string WindowlessAppsCaption = "Chooses which running apps with no window at all show up in the switcher.";
    public const string WindowlessOff = "Do not show";
    public const string WindowlessExplorer = "Explorer only";
    public const string WindowlessAll = "All apps";
    public const string WindowlessLockedFormat = "All running apps stay reachable while “{0}” is on. Turn it off to choose.";
    public const string RulesByApp = "Rules by app";
    public const string RulesCaption = "Choose how each app appears. Apps without a rule use the choice above.";
    public const string RuleShow = "Show without windows";
    public const string RuleWindowsOnly = "Windows only";
    public const string RuleNever = "Never show";
    public const string SwitcherBehavior = "Switcher behavior";

    // ---- thumbnails card
    public const string WindowThumbnails = "Window thumbnails";
    public const string PreviewSize = "Preview size";
    public const string SizeSmall = "Small";
    public const string SizeNormal = "Normal";
    public const string SizeLarge = "Large";
    public const string SizeXLarge = "Extra large";
    public const string MinimalPreviews = "Minimal previews";
    public const string MinimalPreviewsCaption = "Hide titles, buttons and decorative details in taskbar and switcher previews. The selection stays visible.";
    public const string PauseInApps = "Pause in these apps";
    public const string PauseCaption = "Window thumbnails stop while one of these apps is in front.";

    // ---- taskbar page
    public const string DockTitle = "Taskbar";
    public const string DockDescription = "See an app’s windows from its taskbar button, and choose what a click on it does.";
    public const string DockPreviewName = "Taskbar Preview";
    public const string DockPreviewEnable = "Preview windows from the taskbar";
    public const string DockPreviewEnableCaption = "Hover over an open app in the taskbar to see its windows, then click the one you want.";
    public const string DockUnavailable = "Could not read taskbar items.";
    public const string DockCurrentDesktop = "Show only the current desktop";
    public const string DockCurrentDesktopCaption = "When off, shows windows from all desktops. Choosing a window on another desktop takes you there.";
    public const string OpenDelay = "Open delay";
    public const string OpenDelayCaption = "How long the pointer has to rest on an icon before its panel opens.";
    public const string PanelBackground = "Panel background";
    public const string PanelBackgroundCaption = "Turn it down to see more of what sits behind the panel.";
    public const string PanelBackgroundGlass = "Transparency effects are on, so the panel’s transparency is set in Settings > Personalization > Colors.";
    public const string QuitOnX = "Close the app with the × button";
    public const string QuitOnXCaption = "In taskbar preview, × closes every window of the app instead of only that window.";
    public const string KeepDockVisible = "Keep taskbar visible (experimental)";
    public const string KeepDockVisibleCaption = "Pauses auto-hide while the preview is open and restores it when you leave. May resize windows. If the app is interrupted, reopen it to restore the taskbar.";
    public const string OrderByCreation = "Order windows by creation time";
    public const string OrderByCreationCaption = "Show older windows first instead of the ones you used most recently.";
    public const string Beta = "Beta. You may run into some bugs.";
    public const string DockClickTitle = "Taskbar clicks";
    public const string ClickMinimize = "Click the taskbar icon to minimize";
    public const string ClickMinimizeCaption = "The active app’s windows minimize when you click its taskbar icon. Click again to bring them back.";
    public const string ClickHide = "Click the taskbar icon to hide the app";
    public const string ClickHideCaption = "The active app hides when you click its taskbar icon. Click again to bring it back.";
    public const string ClickCycle = "Click the taskbar icon to cycle windows";
    public const string ClickCycleCaption = "Click an active app’s taskbar icon to rotate through its windows, like Alt+`.";
    public const string PanelOpenWindow = "Open window";
    public const string PanelCloseWindow = "Close window";
    public const string PanelMinimize = "Minimize window";
    public const string PanelRestore = "Restore window";
    public const string PanelPin = "Pin preview";
    public const string PanelUnpin = "Unpin preview";
    public const string PanelPinned = "Pinned";
    public const string PanelClosePreview = "Close preview";
    public const string PanelPrevious = "Previous window";
    public const string PanelNext = "Next window";
    public const string PanelQuit = "Quit";

    // ---- window layout page
    public const string LayoutPageTitle = "Window layout";
    public const string ShowInPanel = "Show in panel";
    public const string LayoutIntro = "Arrange windows into screen sections or move and resize them with a trackpad or mouse.";
    public const string LayoutApis = "Uses window management APIs only to move and resize windows.";
    public const string WindowDragging = "Window dragging";
    public const string EdgeSnap = "Snap windows at screen edges";
    public const string EdgeSnapCaption = "Turn this on and drag a window title bar to a highlighted area below. A window moved by holding the modifier keys snaps too. Click an area to choose where the window goes.";
    public const string EdgeSnapConflict = "Windows Snap is using the same edges. Turn off “Snap windows” in Settings > System > Multitasking so WinKnife can take over.";
    public const string EdgeSnapWaiting = "Enabled in WinKnife. It starts working as soon as Windows Snap is off.";
    public const string OpenMultitasking = "Open Multitasking settings";
    public const string MoveResize = "Move and resize by dragging";
    public const string MoveResizeCaption = "On a trackpad or mouse, hold the shown modifier keys and drag anywhere inside a window.";
    public const string KeysToMove = "Keys to move";
    public const string DragToMove = "Drag to move";
    public const string DragToResize = "Add Shift and drag to resize";
    public const string ResizeHint = "The starting point chooses the nearest edge or corner. On a mouse, right-button drag also resizes.";
    public const string BringToFront = "Bring the dragged window to front";
    public const string IgnoreApps = "Ignore apps";
    public const string IgnoreAppsCaption = "Window Layout does not use mouse or keyboard input while one of these apps is focused.";
    public const string Gaps = "Gaps";
    public const string WindowGap = "Window gap";
    public const string ScreenGap = "Screen gap";
    public const string GapsCaption = "Space between snapped windows, and between windows and the screen edge.";
    public const string Shortcuts = "Shortcuts";
    public const string ShortcutsCaption = "Use global shortcuts to arrange the active window without opening the panel.";
    public const string Directional = "Shortcut + pointer layout";
    public const string DirectionalCaption = "Hold the shortcut, move the pointer toward an edge or corner, then release to place the active window.";
    public const string ShortcutRejected = "Windows rejected this shortcut. Choose another one.";
    public const string PointerDisplay = "Move pointer to next display";
    public const string PointerDisplayCaption = "Puts the pointer in the center of the next display.";
    public const string RepeatCycles = "Repeat Left or Right to cycle sizes";
    public const string RepeatCyclesCaption = "Half, then two thirds, then one third on the same display. Off, with more than one display, the repeat moves the window to the next display on that side.";
    public const string MarginPerEdge = "Margin per edge";
    public const string UseThisCorner = "Use this corner";
    public const string UseThisEdge = "Use this edge";
    public const string AreasOnEdge = "Areas on this edge";
    public const string ShortcutRecording = "Press the new shortcut";
    public const string ResultDone = "Window arranged.";
    public const string ResultRestored = "Window restored.";
    public const string ResultNoWindow = "No active window found.";
    public const string ResultNoRestore = "No previous layout to restore.";
    public const string ResultFailed = "Could not move this window.";

    // ---- quit on close page
    public const string QuitOnCloseTitle = "Quit on close";
    public const string QuitOnCloseDescription = "Closes an app when its last window closes";
    public const string AutoQuitEnable = "Quit an app when its last window closes";
    public const string AutoQuitEnableCaption = "Closing an app’s last window also quits it.";
    public const string HowItWorks = "How it works";
    public const string AutoQuitStep1 = "Close an app’s last window (Ctrl+W, Alt+F4 or the × button).";
    public const string AutoQuitStep2 = "The app quits on its own. “Save changes?” dialogs still appear.";
    public const string AutoQuitPredictable = "Apps that have not opened a window stay open. Notification-area apps stay open unless you explicitly allow them to quit.";
    public const string AutoQuitOngoing = "Some apps close their last window while they keep working, such as a screen recorder once capture starts. Those are quit too, unless they are on the Exceptions list.";
    public const string Exceptions = "Exceptions";
    public const string NoExceptions = "No exceptions";
    public const string AddApp = "Add app…";
    public const string ExceptionsCaption = "Apps on this list stay open even with no windows.";
    public const string TrayAppsToQuit = "Notification-area apps to quit";
    public const string NoTrayApps = "No notification-area apps available to add.";
    public const string TrayAppsCaption = "Notification-area apps stay open by default. Apps you select here can quit after their last window closes while they are in the taskbar. Exceptions still keep them open.";

    // ---- panel
    public const string ControlSwitcher = "Window switcher";
    public const string ControlDockPreview = "Taskbar Preview";
    public const string ControlDockPreviewCaption = "Hover over an open app in the taskbar to preview and peek at its windows.";
    public const string ControlAutoQuit = "Quit on close";
    public const string LargeIconsFormat = "Show {0} with large icons";
    public const string LargeIconsCaption = "Shows one icon per app with that app's window previews above it.";
}
