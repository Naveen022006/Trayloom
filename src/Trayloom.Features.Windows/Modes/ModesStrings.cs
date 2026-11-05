// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.WindowTools.Modes;

/// <summary>Strings of the Modes runtime and its quick-access surfaces (spec 08 section 2.6), English verbatim.</summary>
internal static class ModesStrings
{
    public static string Title => Loc.T("Modes");
    public static string Active => Loc.T("Active");
    public static string Opening => Loc.T("Opening…");
    public static string Empty => Loc.T("Empty");
    public static string ItemCount(int n) => Loc.F("{0} items", n);
    public static string Leave(string name) => Loc.F("Leave {0}", name);
    public static string SaveCurrent => Loc.T("Save what’s open now…");
    public static string NoModes => Loc.T("No modes yet.");
    public static string CreateMode => Loc.T("Create a mode");
    public static string PanelCaption => Loc.T("Open the apps and tabs of a task in one click.");

    public static string Ready(string name) => Loc.F("{0} is ready", name);
    public static string DidntOpen(string name, int n) => Loc.F("{0}: {1} didn’t open", name, n);

    public static string ActionMode(string name) => Loc.F("Mode: {0}", name);
    public static string ActionLeave(string name) => Loc.F("Leave mode: {0}", name);
    public static string ActionSaveCurrent => Loc.T("Save what’s open now as a mode");

    public static string RefusedAway => Loc.T("Not while Away mode is on.");
    public static string RefusedElevated => Loc.T("Not available while Trayloom runs as administrator: the apps would open as administrator.");
    public static string RefusedOff => Loc.T("Modes is turned off.");
    public static string RefusedMissing => Loc.T("This mode doesn’t exist anymore.");
    public static string RefusedEmpty => Loc.T("This mode has nothing to open yet.");

    public static string ReasonAdmin => Loc.T("needs administrator");
    public static string ReasonMissing => Loc.T("isn’t installed there anymore");
    public static string ReasonNoWindow => Loc.T("didn’t open a window");
    public static string ReasonFolderMissing => Loc.T("isn’t there anymore");
    public static string ReasonBlockedFile => Loc.T("is a program or script and can’t be opened as a file");
    public static string ReasonFailed => Loc.T("couldn’t be started");
    public static string ReasonPrompt => Loc.T("is waiting for your answer");

    public static string DefaultBrowser => Loc.T("Default browser");
    public static string BrowserWindow(string browser, int n) => Loc.F("{0} window {1}", browser, n);
    public static string TabCount(int n) => Loc.F("{0} tabs", n);
    public static string WindowCount(int n) => Loc.F("{0} windows", n);
    public static string TabsChromeEdgeOnly => Loc.T("Trayloom can read tabs from Chrome and Edge only.");
    public static string TabsUnreadable(string browser) => Loc.F("Couldn’t read the tabs of {0}. Paste the addresses instead.", browser);
    public static string CaptureTabsFailed(string browser) => Loc.F("Couldn’t read the tabs of {0}, so it is saved as an app.", browser);
    public static string CaptureTabsUnsupported(string browser) => Loc.F("Trayloom can read tabs from Chrome and Edge only, so {0} is saved as an app.", browser);

    public static string ShortcutSection => Loc.T("Modes");
}
