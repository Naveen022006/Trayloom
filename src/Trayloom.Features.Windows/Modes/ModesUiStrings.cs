// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.WindowTools.Modes;

/// <summary>
/// Strings of the Modes settings card and its two dialogs (spec 08 section 2.6), English verbatim. The runtime and quick-access strings
/// live in <see cref="ModesStrings"/>; the ones both need are used from there.
/// </summary>
internal static class ModesUiStrings
{
    // ------------------------------------------------------------------------------------------ settings card

    public static string Caption => Loc.T("One click opens the apps, folders and browser tabs of a task, like coding or studying, and puts the others away.");
    public static string Open => Loc.T("Open");
    public static string Edit => Loc.T("Edit");
    public static string MoveUp => Loc.T("Move up");
    public static string MoveDown => Loc.T("Move down");
    public static string Delete => Loc.T("Delete");
    public static string Cancel => Loc.T("Cancel");
    public static string Save => Loc.T("Save");
    public static string NewMode => Loc.T("New mode");
    public static string NoModesYet => Loc.T("No modes yet. Start with what’s open now, or build one item by item.");
    public static string NeverCloses => Loc.T("Trayloom never closes a window that asks to save something, and never closes File Explorer or terminals.");
    public static string DeleteConfirm(string name) => Loc.F("Delete the mode “{0}”? Its apps and tabs stay as they are.", name);
    public static string LimitReached => Loc.F("You can have up to {0} modes.", ModesMath.MaxModes);
    public static string ShortcutsTitle => Loc.T("Shortcuts");
    public static string Shortcut => Loc.T("Shortcut");
    public static string ShortcutSaveFirst => Loc.T("Save the mode first to give it a shortcut.");
    public static string Dot => " · ";

    // ------------------------------------------------------------------------------------------ editor

    public static string EditorNew => Loc.T("New mode");
    public static string EditorEdit => Loc.T("Edit mode");
    public static string Name => Loc.T("Name");
    public static string NamePlaceholder => Loc.T("Coding");
    public static string NameMissing => Loc.T("Give the mode a name.");
    public static string NameTaken => Loc.T("Another mode already has this name.");
    public static string Colour => Loc.T("Colour");
    public static string Icon => Loc.T("Icon");
    public static string WhatOpens => Loc.T("What opens");
    public static string NothingYet => Loc.T("Nothing yet. Add an app, a folder, a file or some web pages.");
    public static string AddApp => Loc.T("Add an app…");
    public static string AddFolder => Loc.T("Add a folder…");
    public static string AddFile => Loc.T("Add a file…");
    public static string AddWeb => Loc.T("Add web pages…");
    public static string BlockedFile => Loc.T("Add programs and scripts with “Add an app…”.");
    public static string ItemLimit => Loc.F("A mode can have up to {0} items.", ModesMath.MaxItems);
    public static string Place => Loc.T("Place");
    public static string WhereItOpens => Loc.T("Where it opens");
    public static string BringToFront => Loc.T("Bring to front");
    public static string Remove => Loc.T("Remove");
    public static string ArgsPlaceholder => Loc.T("Arguments (optional)");
    public static string OpenWithFolder => Loc.T("Open with folder…");
    public static string Address(int n) => n == 1 ? Loc.T("1 address") : Loc.F("{0} addresses", n);
    public static string AndMore(string host, int more) => Loc.F("{0} and {1} more", host, more);
    public static string ChooseFolder => Loc.T("Choose a folder");
    public static string ChooseFile => Loc.T("Choose a file");
    public static string KindApp => Loc.T("App");
    public static string KindFolder => Loc.T("Folder");
    public static string KindFile => Loc.T("File");

    // The inline "Add web pages" panel
    public static string Browser => Loc.T("Browser");
    public static string Profile => Loc.T("Profile");
    public static string BrowserOwnChoice => Loc.T("Browser’s choice");
    public static string PasteAddresses => Loc.T("Paste addresses, one per line");
    public static string AddAddresses => Loc.T("Add addresses");
    public static string AddCurrentTabs => Loc.T("Add current browser tabs");
    public static string ReadingTabs => Loc.T("Reading tabs…");
    public static string NoTabsFound => Loc.T("No browser windows with tabs were found.");
    public static string NoValidAddresses => Loc.T("No web page addresses were found.");
    public static string Rejected(int n) => Loc.F("{0} addresses were not web pages and were left out.", n);
    public static string WebWindow(int n) => Loc.F("Window {0}", n);
    public static string AddSelected => Loc.T("Add the ticked windows");
    public static string Done => Loc.T("Done");
    public static string TabsCaption => Loc.T("Tabs are read from the browser’s own session file on this PC, only when you press this button. Nothing is sent anywhere.");

    // When I leave / when this mode starts
    public static string LeaveTitle => Loc.T("When I leave this mode");
    public static string LeaveKeep => Loc.T("Leave its windows as they are");
    public static string LeaveMinimize => Loc.T("Minimize its windows");
    public static string LeaveClose => Loc.T("Close the windows it opened");
    public static string LeaveCloseCaption => Loc.T("A window that asks to save something stays open for you to answer. Everything else of this mode is minimized.");
    public static string StartTitle => Loc.T("When this mode starts");
    public static string KeepAwake => Loc.T("Keep awake");
    public static string KeepAwakeOff => Loc.T("Off");
    public static string KeepAwake1h => Loc.T("1 hour");
    public static string KeepAwake2h => Loc.T("2 hours");
    public static string KeepAwakeUntil => Loc.T("Until I leave the mode");
    public static string KeepAwakeMissing => Loc.T("Install Keep awake in Features to use this.");
    public static string Volume => Loc.T("Volume");
    public static string VolumeAsIs => Loc.T("Leave as it is");
    public static string VolumeMute => Loc.T("Mute");
    public static string Percent(int n) => $"{n}%";
    public static string Timer => Loc.T("Open the island timer");
    public static string DoNotDisturb => Loc.T("Windows has no setting apps can use to switch on Do not disturb, so Modes can’t do it for you.");

    // ------------------------------------------------------------------------------------------ capture dialog

    public static string CaptureTitle => Loc.T("Save what’s open now");
    public static string CaptureCaption => Loc.T("Choose what this mode should open. Private windows are never saved.");
    public static string CaptureDefaultName => Loc.T("My mode");
    public static string CaptureReading => Loc.T("Reading what’s open…");
    public static string CaptureNothing => Loc.T("Nothing that can be saved was found.");
    public static string CaptureFailed => Loc.T("Couldn’t read what’s open.");
    public static string CaptureNothingTicked => Loc.T("Tick at least one thing to save.");
    public static string CaptureApps => Loc.T("Apps");
    public static string CaptureBrowserWindows => Loc.T("Browser windows");
    public static string CaptureFolders => Loc.T("Folders");
    public static string CaptureSave => Loc.T("Save mode");
    public static string ShowAddresses => Loc.T("Show addresses");
    public static string HideAddresses => Loc.T("Hide addresses");
}
