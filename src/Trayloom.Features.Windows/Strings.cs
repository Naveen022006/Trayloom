// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.WindowTools;

/// <summary>English strings of the Windows and taskbar features (verbatim from the specs, Mac wording replaced). One place so localisation can be added later.</summary>
internal static class Strings
{
    // ---- shared
    public static  string Remove => Loc.T("Remove");
    public static  string AddAnApp => Loc.T("Add an app…");
    public static  string ChooseApp => Loc.T("Choose app");
    public static  string ChooseAppEllipsis => Loc.T("Choose app…");
    public static  string Cancel => Loc.T("Cancel");
    public static  string SearchApps => Loc.T("Search apps");
    public static  string NoAppsFound => Loc.T("No apps found");
    public static  string Scanning => Loc.T("Scanning files…");
    public static  string Reset => Loc.T("Reset");
    public static  string None => Loc.T("None");
    public static  string RemoveShortcut => Loc.T("Remove shortcut");
    public static  string Options => Loc.T("Options");
    public static  string MoreOptions => Loc.T("More options");
    public static  string ActiveNow => Loc.T("Active now");
    public static  string PermissionRequired => Loc.T("Permission required");
    public static  string AdminCaption => Loc.T("Some windows belong to apps running as administrator. Run Trayloom as administrator to control them.");
    public static  string AdminButton => Loc.T("Restart as administrator");
    public static  string NoOpenWindow => Loc.T("No open window");
    public static  string NoWindows => Loc.T("No open windows");
    public static  string OtherDesktop => Loc.T("Other desktop");

    // ---- switcher page
    public static  string SwitcherTitle => Loc.T("Switcher");
    public static  string SwitcherDescription => Loc.T("Switch between apps and windows your way.");
    public static  string SwitcherSection => Loc.T("Window switcher");
    public static  string SwitcherUse => Loc.T("Use the Trayloom switcher");
    public static  string SwitcherUseCaption => Loc.T("Switch between apps and windows, including minimized windows and multiple windows from the same app.");
    public static  string Layout => Loc.T("Layout");
    public static  string LayoutWindows => Loc.T("Window previews");
    public static  string LayoutWindowsCaption => Loc.T("One preview per window, minimized ones included.");
    public static  string LayoutIcons => Loc.T("Large icons");
    public static  string LayoutIconsCaption => Loc.T("Shows one icon per app with that app’s window previews above it.");
    public static  string LayoutSimple => Loc.T("Simple list");
    public static  string LayoutSimpleCaption => Loc.T("Shows app icons and window titles, without previews or screen capture by the switcher.");
    public static  string ShortcutApps => Loc.T("Apps");
    public static  string ShortcutWindows => Loc.T("Windows");
    public static  string WindowShortcutCaption => Loc.T("Opens a switcher for the frontmost app’s windows. While the switcher is open, jumps between the selected app’s windows.");
    public static  string AllWindows => Loc.T("All windows");
    public static  string AllWindowsCaption => Loc.T("Use a separate shortcut to cycle through windows from all apps in any layout. Your window filters still apply.");
    public static  string TakeOver => Loc.T("Replace Windows Alt+Tab and Alt+`");
    public static  string TakeOverCaption => Loc.T("Hides the matching Windows switcher shortcuts only while Trayloom’s switcher is active. All running apps stay reachable.");
    public static  string TakeOverWarning => Loc.T("Windows Alt+Tab will open too.");
    public static  string UsageHintFormat => Loc.T("Hold {0} to navigate; release to activate the window. Shift or ← goes back; W closes the window; Q quits the app; Esc cancels.");
    public static  string AppearanceDelay => Loc.T("Appearance delay");
    public static  string AppearanceDelayCaption => Loc.T("How long the shortcut must be held before the switcher appears.");
    public static  string MaxColumns => Loc.T("Maximum columns");
    public static  string MaxColumnsCaption => Loc.T("Wraps onto more rows after this many items. Useful on ultrawide displays.");
    public static  string InstantSelection => Loc.T("Instant selection");
    public static  string InstantSelectionCaption => Loc.T("Moves the highlight and scroll position immediately as you browse apps and windows.");
    public static  string PinSearch => Loc.T("Pin search with S");
    public static  string PinSearchCaption => Loc.T("S starts a search and pins the switcher open, so typing no longer fires Alt shortcuts, and a search starting with Q or W no longer closes the window or quits the app by mistake.");
    public static  string ShortcutHints => Loc.T("Show shortcut hints");
    public static  string ShortcutHintsCaption => Loc.T("Shows the app and window shortcuts below the icons.");
    public static  string MergeTabs => Loc.T("Show one entry per app");
    public static  string MergeTabsCaption => Loc.T("Collapses all of an app’s windows into one entry in the switcher, instead of one entry per window.");
    public static  string FullscreenWindows => Loc.T("Show full-screen windows");
    public static  string MinimizedWindows => Loc.T("Minimized windows");
    public static  string MinNormal => Loc.T("Normal ordering");
    public static  string MinEnd => Loc.T("Place at end");
    public static  string MinHide => Loc.T("Hide");
    public static  string TreatHidden => Loc.T("Treat hidden apps like minimized windows");
    public static  string ShowOn => Loc.T("Show on");
    public static  string ShowOnCaption => Loc.T("Which display the switcher opens on when more than one is connected.");
    public static  string OnPointer => Loc.T("Display with the pointer");
    public static  string OnTaskbar => Loc.T("Display with the taskbar");
    public static  string OnActive => Loc.T("Display with the active window");
    public static  string CurrentDisplayOnly => Loc.T("Show only the current display");
    public static  string CurrentDisplayOnlyCaption => Loc.T("Lists only windows on the display under the pointer. If that display has no windows, the switcher does not open.");
    public static  string CurrentDesktopOnly => Loc.T("Show only the current desktop");
    public static  string CurrentDesktopOnlyCaption => Loc.T("Lists windows from the current virtual desktop.");
    public static  string WindowlessApps => Loc.T("Apps with no open window");
    public static  string WindowlessAppsCaption => Loc.T("Chooses which running apps with no window at all show up in the switcher.");
    public static  string WindowlessOff => Loc.T("Do not show");
    public static  string WindowlessExplorer => Loc.T("Explorer only");
    public static  string WindowlessAll => Loc.T("All apps");
    public static  string WindowlessLockedFormat => Loc.T("All running apps stay reachable while “{0}” is on. Turn it off to choose.");
    public static  string RulesByApp => Loc.T("Rules by app");
    public static  string RulesCaption => Loc.T("Choose how each app appears. Apps without a rule use the choice above.");
    public static  string RuleShow => Loc.T("Show without windows");
    public static  string RuleWindowsOnly => Loc.T("Windows only");
    public static  string RuleNever => Loc.T("Never show");
    public static  string SwitcherBehavior => Loc.T("Switcher behavior");

    // ---- thumbnails card
    public static  string WindowThumbnails => Loc.T("Window thumbnails");
    public static  string PreviewSize => Loc.T("Preview size");
    public static  string SizeSmall => Loc.T("Small");
    public static  string SizeNormal => Loc.T("Normal");
    public static  string SizeLarge => Loc.T("Large");
    public static  string SizeXLarge => Loc.T("Extra large");
    public static  string MinimalPreviews => Loc.T("Minimal previews");
    public static  string MinimalPreviewsCaption => Loc.T("Hide titles, buttons and decorative details in taskbar and switcher previews. The selection stays visible.");
    public static  string PauseInApps => Loc.T("Pause in these apps");
    public static  string PauseCaption => Loc.T("Window thumbnails stop while one of these apps is in front.");

    // ---- taskbar page
    public static  string DockTitle => Loc.T("Taskbar");
    public static  string DockDescription => Loc.T("See an app’s windows from its taskbar button, and choose what a click on it does.");
    public static  string DockPreviewName => Loc.T("Taskbar Preview");
    public static  string DockPreviewEnable => Loc.T("Preview windows from the taskbar");
    public static  string DockPreviewEnableCaption => Loc.T("Hover over an open app in the taskbar to see its windows, then click the one you want.");
    public static  string DockUnavailable => Loc.T("Could not read taskbar items.");
    public static  string DockCurrentDesktop => Loc.T("Show only the current desktop");
    public static  string DockCurrentDesktopCaption => Loc.T("When off, shows windows from all desktops. Choosing a window on another desktop takes you there.");
    public static  string OpenDelay => Loc.T("Open delay");
    public static  string OpenDelayCaption => Loc.T("How long the pointer has to rest on an icon before its panel opens.");
    public static  string PanelBackground => Loc.T("Panel background");
    public static  string PanelBackgroundCaption => Loc.T("Turn it down to see more of what sits behind the panel.");
    public static  string PanelBackgroundGlass => Loc.T("Transparency effects are on, so the panel’s transparency is set in Settings > Personalization > Colors.");
    public static  string QuitOnX => Loc.T("Close the app with the × button");
    public static  string QuitOnXCaption => Loc.T("In taskbar preview, × closes every window of the app instead of only that window.");
    public static  string KeepDockVisible => Loc.T("Keep taskbar visible (experimental)");
    public static  string KeepDockVisibleCaption => Loc.T("Pauses auto-hide while the preview is open and restores it when you leave. May resize windows. If the app is interrupted, reopen it to restore the taskbar.");
    public static  string OrderByCreation => Loc.T("Order windows by creation time");
    public static  string OrderByCreationCaption => Loc.T("Show older windows first instead of the ones you used most recently.");
    public static  string Beta => Loc.T("Beta. You may run into some bugs.");
    public static  string DockClickTitle => Loc.T("Taskbar clicks");
    public static  string ClickMinimize => Loc.T("Click the taskbar icon to minimize");
    public static  string ClickMinimizeCaption => Loc.T("The active app’s windows minimize when you click its taskbar icon. Click again to bring them back.");
    public static  string ClickHide => Loc.T("Click the taskbar icon to hide the app");
    public static  string ClickHideCaption => Loc.T("The active app hides when you click its taskbar icon. Click again to bring it back.");
    public static  string ClickCycle => Loc.T("Click the taskbar icon to cycle windows");
    public static  string ClickCycleCaption => Loc.T("Click an active app’s taskbar icon to rotate through its windows, like Alt+`.");
    public static  string PanelOpenWindow => Loc.T("Open window");
    public static  string PanelCloseWindow => Loc.T("Close window");
    public static  string PanelMinimize => Loc.T("Minimize window");
    public static  string PanelRestore => Loc.T("Restore window");
    public static  string PanelPin => Loc.T("Pin preview");
    public static  string PanelUnpin => Loc.T("Unpin preview");
    public static  string PanelPinned => Loc.T("Pinned");
    public static  string PanelClosePreview => Loc.T("Close preview");
    public static  string PanelPrevious => Loc.T("Previous window");
    public static  string PanelNext => Loc.T("Next window");
    public static  string PanelQuit => Loc.T("Quit");

    // ---- window layout page
    public static  string LayoutPageTitle => Loc.T("Window layout");
    public static  string ShowInPanel => Loc.T("Show in panel");
    public static  string LayoutIntro => Loc.T("Arrange windows into screen sections or move and resize them with a trackpad or mouse.");
    public static  string LayoutApis => Loc.T("Uses window management APIs only to move and resize windows.");
    public static  string WindowDragging => Loc.T("Window dragging");
    public static  string EdgeSnap => Loc.T("Snap windows at screen edges");
    public static  string EdgeSnapCaption => Loc.T("Turn this on and drag a window title bar to a highlighted area below. A window moved by holding the modifier keys snaps too. Click an area to choose where the window goes.");
    public static  string EdgeSnapConflict => Loc.T("Windows Snap is using the same edges. Turn off “Snap windows” in Settings > System > Multitasking so Trayloom can take over.");
    public static  string EdgeSnapWaiting => Loc.T("Enabled in Trayloom. It starts working as soon as Windows Snap is off.");
    public static  string OpenMultitasking => Loc.T("Open Multitasking settings");
    public static  string MoveResize => Loc.T("Move and resize by dragging");
    public static  string MoveResizeCaption => Loc.T("On a trackpad or mouse, hold the shown modifier keys and drag anywhere inside a window.");
    public static  string KeysToMove => Loc.T("Keys to move");
    public static  string DragToMove => Loc.T("Drag to move");
    public static  string DragToResize => Loc.T("Add Shift and drag to resize");
    public static  string ResizeHint => Loc.T("The starting point chooses the nearest edge or corner. On a mouse, right-button drag also resizes.");
    public static  string BringToFront => Loc.T("Bring the dragged window to front");
    public static  string IgnoreApps => Loc.T("Ignore apps");
    public static  string IgnoreAppsCaption => Loc.T("Window Layout does not use mouse or keyboard input while one of these apps is focused.");
    public static  string Gaps => Loc.T("Gaps");
    public static  string WindowGap => Loc.T("Window gap");
    public static  string ScreenGap => Loc.T("Screen gap");
    public static  string GapsCaption => Loc.T("Space between snapped windows, and between windows and the screen edge.");
    public static  string Shortcuts => Loc.T("Shortcuts");
    public static  string ShortcutsCaption => Loc.T("Use global shortcuts to arrange the active window without opening the panel.");
    public static  string Directional => Loc.T("Shortcut + pointer layout");
    public static  string DirectionalCaption => Loc.T("Hold the shortcut, move the pointer toward an edge or corner, then release to place the active window.");
    public static  string ShortcutRejected => Loc.T("Windows rejected this shortcut. Choose another one.");
    public static  string PointerDisplay => Loc.T("Move pointer to next display");
    public static  string PointerDisplayCaption => Loc.T("Puts the pointer in the center of the next display.");
    public static  string RepeatCycles => Loc.T("Repeat Left or Right to cycle sizes");
    public static  string RepeatCyclesCaption => Loc.T("Half, then two thirds, then one third on the same display. Off, with more than one display, the repeat moves the window to the next display on that side.");
    public static  string MarginPerEdge => Loc.T("Margin per edge");
    public static  string UseThisCorner => Loc.T("Use this corner");
    public static  string UseThisEdge => Loc.T("Use this edge");
    public static  string AreasOnEdge => Loc.T("Areas on this edge");
    public static  string ShortcutRecording => Loc.T("Press the new shortcut");
    public static  string ResultDone => Loc.T("Window arranged.");
    public static  string ResultRestored => Loc.T("Window restored.");
    public static  string ResultNoWindow => Loc.T("No active window found.");
    public static  string ResultNoRestore => Loc.T("No previous layout to restore.");
    public static  string ResultFailed => Loc.T("Could not move this window.");

    // ---- quit on close page
    public static  string QuitOnCloseTitle => Loc.T("Quit on close");
    public static  string QuitOnCloseDescription => Loc.T("Closes an app when its last window closes");
    public static  string AutoQuitEnable => Loc.T("Quit an app when its last window closes");
    public static  string AutoQuitEnableCaption => Loc.T("Closing an app’s last window also quits it.");
    public static  string HowItWorks => Loc.T("How it works");
    public static  string AutoQuitStep1 => Loc.T("Close an app’s last window (Ctrl+W, Alt+F4 or the × button).");
    public static  string AutoQuitStep2 => Loc.T("The app quits on its own. “Save changes?” dialogs still appear.");
    public static  string AutoQuitPredictable => Loc.T("Apps that have not opened a window stay open. Notification-area apps stay open unless you explicitly allow them to quit.");
    public static  string AutoQuitOngoing => Loc.T("Some apps close their last window while they keep working, such as a screen recorder once capture starts. Those are quit too, unless they are on the Exceptions list.");
    public static  string Exceptions => Loc.T("Exceptions");
    public static  string NoExceptions => Loc.T("No exceptions");
    public static  string AddApp => Loc.T("Add app…");
    public static  string ExceptionsCaption => Loc.T("Apps on this list stay open even with no windows.");
    public static  string TrayAppsToQuit => Loc.T("Notification-area apps to quit");
    public static  string NoTrayApps => Loc.T("No notification-area apps available to add.");
    public static  string TrayAppsCaption => Loc.T("Notification-area apps stay open by default. Apps you select here can quit after their last window closes while they are in the taskbar. Exceptions still keep them open.");

    // ---- panel
    public static  string ControlSwitcher => Loc.T("Window switcher");
    public static  string ControlDockPreview => Loc.T("Taskbar Preview");
    public static  string ControlDockPreviewCaption => Loc.T("Hover over an open app in the taskbar to preview and peek at its windows.");
    public static  string ControlAutoQuit => Loc.T("Quit on close");
    public static  string LargeIconsFormat => Loc.T("Show {0} with large icons");
    public static  string LargeIconsCaption => Loc.T("Shows one icon per app with that app's window previews above it.");
}
