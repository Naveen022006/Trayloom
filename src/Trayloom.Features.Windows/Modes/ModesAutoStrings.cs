// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.WindowTools.Modes;

/// <summary>Strings of the automatic mode starts and the mode badge (spec 10 package P2), English verbatim.</summary>
internal static class ModesAutoStrings
{
    // ------------------------------------------------------------------------------------------ the card

    public static string Title => Loc.T("Start modes automatically");
    public static string MasterCaption => Loc.T("Start a mode when an app opens or at a time you choose. A mode you are already in is never replaced.");
    public static string ShowInIsland => Loc.T("Show the active mode in the island");
    public static string ShowInIslandCaption => Loc.T("Shows the mode’s icon and name in the closed island when nothing else is going on.");

    // ------------------------------------------------------------------------------------------ list and form

    public static string NoRules => Loc.T("No rules yet.");
    public static string AddRule => Loc.T("Add rule");
    public static string Mode => Loc.T("Mode");
    public static string StartWhen => Loc.T("Start when");
    public static string AppOpens => Loc.T("An app opens");
    public static string AtTime => Loc.T("At a time");
    public static string App => Loc.T("App");
    public static string Browse => Loc.T("Browse…");
    public static string Time => Loc.T("Time");
    public static string Days => Loc.T("Days");
    public static string LeaveAt => Loc.T("Leave at");
    public static string LeaveAtCaption => Loc.T("Optional. Leaves the mode at this time if this rule started it and you’re still in it.");
    public static string AskFirst => Loc.T("Ask before starting");
    public static string AskFirstCaption => Loc.T("Shows a question in the island instead of starting the mode right away.");
    public static string Save => Loc.T("Save");
    public static string Cancel => Loc.T("Cancel");
    public static string Edit => Loc.T("Edit");
    public static string Delete => Loc.T("Delete");
    public static string DeleteConfirm => Loc.T("Delete this rule?");
    public static string ChooseApp => Loc.T("Choose an app");
    public static string TimeHint => Loc.T("18:30");

    public static string ErrorTime => Loc.T("Enter a time like 18:30.");
    public static string ErrorLeaveAt => Loc.T("Leave at must be later than the start time.");
    public static string ErrorApp => Loc.T("Choose an app.");
    public static string ErrorDays => Loc.T("Choose at least one day.");
    public static string ErrorSave => Loc.T("Couldn’t save the rule.");

    // ------------------------------------------------------------------------------------------ rule captions

    public static string WhenOpens(string app) => Loc.F("When {0} opens", app);
    public static string DaysAt(string days, string time) => Loc.F("{0} at {1}", days, time);
    public static string AskSuffix => Loc.T(" · Ask first");
    public static string RightAwaySuffix => Loc.T(" · Starts right away");
    public static string LeavesAt(string time) => Loc.F(" · Leaves at {0}", time);
    public static string Disabled => Loc.T(" · Off");

    public static string EveryDay => Loc.T("Every day");
    public static string Weekdays => Loc.T("Weekdays");
    public static string Weekends => Loc.T("Weekends");
    public static string NoDays => Loc.T("No days");

    /// <summary>The short name of a day, Monday = 0.</summary>
    public static string DayShort(int index) => index switch
    {
        0 => Loc.T("Mon"),
        1 => Loc.T("Tue"),
        2 => Loc.T("Wed"),
        3 => Loc.T("Thu"),
        4 => Loc.T("Fri"),
        5 => Loc.T("Sat"),
        _ => Loc.T("Sun"),
    };

    /// <summary>A stored <c>H:mm</c> time in the person's own clock format.</summary>
    public static string TimeText(string at) => ModesAutoMath.TryParseTime(at, out var time) ? (DateTime.Today + time).ToString(Loc.ShortTime, Loc.Culture) : at;

    // ------------------------------------------------------------------------------------------ suggestion

    public static string IslandAppOpened(string app, string mode) => Loc.F("{0} opened · Start {1}?", app, mode);
    public static string IslandItsTime(string time, string mode) => Loc.F("It’s {0} · Start {1}?", time, mode);
    public static string ToastAppOpened(string app, string mode) => Loc.F("{0} opened. Start {1} from Modes in the tray or the island.", app, mode);
    public static string ToastItsTime(string time, string mode) => Loc.F("It’s {0}. Start {1} from Modes in the tray or the island.", time, mode);
    public static string StartMode(string mode) => Loc.F("Start {0}", mode);
    public static string NotNow => Loc.T("Not now");
}
