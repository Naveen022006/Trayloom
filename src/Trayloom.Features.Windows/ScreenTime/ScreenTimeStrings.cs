// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.WindowTools.ScreenTime;

/// <summary>English strings of Screen time (spec 06 section 1), verbatim. One place so localisation can be added later.</summary>
internal static class ScreenTimeStrings
{
    public static string Title => Loc.T("Screen time");

    public static string Summary => Loc.T("How long you used the PC today, and in which apps.");

    public static string Today => Loc.T("Today");

    public static string SameAsYesterday => Loc.T("Same as yesterday");

    public static string MoreThanYesterday(string difference) => Loc.F("{0} more than yesterday", difference);

    public static string LessThanYesterday(string difference) => Loc.F("{0} less than yesterday", difference);

    public static string Since(string time) => Loc.F("Since {0}", time);

    public static string HourTip(string range, string duration) => Loc.F("{0}: {1}", range, duration);

    public static string MoreApps(int count, string duration) => Loc.F("{0} more apps · {1}", count, duration);

    public static string ShowFewer => Loc.T("Show fewer");

    public static string Off => Loc.T("Screen time is off.");

    public static string TurnOn => Loc.T("Turn on");

    public static string Empty => Loc.T("No screen time yet today.");

    public static string EmptyCaption => Loc.T("Use the PC for a minute and it shows up here.");

    public static string NotRunning => Loc.T("Screen time is not running.");

    public static string DesktopAndTaskbar => Loc.T("Desktop and taskbar");

    public static string OtherApps => Loc.T("Other apps");

    public static string LessThanAMinute => Loc.T("Less than a minute");

    public static string ShowOnContentTab => Loc.T("Show “Screen time” on the Content tab.");

    // ---- settings
    public static string CountTitle => Loc.T("Count screen time");

    public static string CountCaption => Loc.T("Counts the time each app is in front while you use the PC. App names and times stay on this PC; window titles are never read. Nothing is counted while Trayloom is not running.");

    public static string StopAfter => Loc.T("Stop counting after");

    public static string OneMinute => Loc.T("1 minute without input");

    public static string TwoMinutes => Loc.T("2 minutes without input");

    public static string FiveMinutes => Loc.T("5 minutes without input");

    public static string CountMediaTitle => Loc.T("Count videos and full-screen apps");

    public static string CountMediaCaption => Loc.T("Watching a video or giving a presentation counts even when you do not touch the mouse or keyboard.");

    public static string KeepFor => Loc.T("Keep history for");

    public static string OneDay => Loc.T("1 day");

    public static string SevenDays => Loc.T("7 days");

    public static string ThirtyDays => Loc.T("30 days");

    public static string ClearButton => Loc.T("Clear history…");

    public static string ClearBody => Loc.T("This deletes the screen time of every day kept on this PC.");

    public static string ClearTitle => Loc.T("Clear screen time history?");

    public static string TodayCaption(string total, string top) => Loc.F("{0} · {1}", total, top);

    public static string ActionToday(string total) => Loc.F("Screen time today: {0}", total);

    public static string CommandKeywords => Loc.T("Screen time usage today apps");
}
