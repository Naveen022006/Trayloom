// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.SystemTools;

/// <summary>English strings of the Energy and display features (spec 02a section 4, 01 section 5.1/5.2, 05 part E).</summary>
internal static partial class Strings
{
    public static class KeepAwake
    {
        public static  string Title => Loc.T("Keep awake");
        public static  string Idle => Loc.T("The PC follows its normal energy rules");
        public static  string ActiveIndefinite => Loc.T("Active until you turn it off");
        public static  string AutomationActive => Loc.T("Active because an automatic condition is met");
        public static  string ExternalDisplayActive => Loc.T("Active while an external display is connected");
        public static  string PowerActive => Loc.T("Active while connected to power");
        public static  string RunningAppsActive => Loc.T("Active while a selected app is running");
        public static  string DefaultDuration => Loc.T("Default duration");
        public static  string Automation => Loc.T("Automation");
        public static  string AutomationCaptionAny => Loc.T("Starts when any selected condition is active.");
        public static  string AutomationCaptionAll => Loc.T("Starts only when every selected condition is active.");
        public static  string AutomationOff => Loc.T("Off");
        public static  string ExternalDisplay => Loc.T("External display");
        public static  string Power => Loc.T("Power");
        public static  string Applications => Loc.T("Applications");
        public static  string Any => Loc.T("Any");
        public static  string All => Loc.T("All");
        public static  string SelectedApps => Loc.T("Selected apps");
        public static  string AddApp => Loc.T("Add an app…");
        public static  string RemoveApp => Loc.T("Remove");
        public static  string AppsCaption => Loc.T("Keep Awake starts while any of these apps is open, even in the background.");
        public static  string PauseWhenLocked => Loc.T("Pause while the PC is locked");
        public static  string PauseWhenLockedCaption => Loc.T("Follows normal sleep rules while locked and resumes the remaining session after you unlock.");
        public static  string Options => Loc.T("Options");
        public static  string AutoStart => Loc.T("Keep Awake when Trayloom opens");
        public static  string AutoStartCaption => Loc.T("Starts a session with the default duration.");
        public static  string RightClick => Loc.T("Right-click the tray icon to toggle Keep Awake");
        public static  string RightClickCaption => Loc.T("Replaces the right-click context menu.");
        public static  string ShowCountdown => Loc.T("Show remaining time next to the icon");
        public static  string AllowDisplaySleep => Loc.T("Allow the display to sleep");
        public static  string AllowDisplaySleepCaption => Loc.T("Keeps the PC awake while the display follows its normal sleep timer.");
        public static  string MovePointer => Loc.T("Move pointer slightly");
        public static  string MovePointerCaption => Loc.T("During a session, moves the pointer a little at the chosen interval.");
        public static  string Interval => Loc.T("Interval");
        public static  string ActiveIcon => Loc.T("Active icon");
        public static  string ActiveIconColor => Loc.T("Active icon color");
        public static  string MenuBarIcon => Loc.T("Tray icon");
        public static  string BatteryLimit => Loc.T("Disable when battery drops below");
        public static  string BatteryLimitCaption => Loc.T("Keeps a forgotten session from draining the laptop battery.");
        public static  string Never => Loc.T("Never");
        public static  string LidClosed => Loc.T("Keep going with the lid closed");
        public static  string LidExplanation => Loc.T("“Keep going with the lid closed” fully disables sleep while “Keep awake” is active and is reverted automatically when the session ends or the app quits. Prefer using it plugged in.");
        public static  string LidConfiguring => Loc.T("Configuring…");
        public static  string LidFailed => Loc.T("Couldn’t turn on closed-lid mode. Try again.");
        public static  string LidOn => Loc.T("Sleep fully disabled. Mind the power");
        public static  string LidNeedsSession => Loc.T("Applied whenever “Keep awake” is active");
        public static  string LidReady => Loc.T("Ready. Toggles without asking for permission");
        public static  string DimDisplay => Loc.T("Dim the display to zero");
        public static  string DimDisplayCaption => Loc.T("Dims the display when the lid closes and brings the brightness back when it opens.");
        public static  string ChipHint => Loc.T("Click a chip to start. Click it again to stop");
        public static  string Until => Loc.T("Until…");
        public static  string UntilHeader => Loc.T("Until");
        public static  string Start => Loc.T("Start");
        public static  string Hour => Loc.T("Hour (0–23)");
        public static  string Minute => Loc.T("Minute");
        public static  string SessionEndedTitle => Loc.T("Session ended");
        public static  string SessionEndedBody => Loc.T("Time is up. The PC will sleep normally again.");
        public static  string DisabledTitle => Loc.T("Trayloom disabled");
        public static  string LowBatteryBody => Loc.T("Low battery. Normal sleep was restored to protect the charge.");

        public static (int Minutes, string Label, string Short, string Name)[] Durations => _loc_Durations.Value;

    private static readonly LocValue<(int Minutes, string Label, string Short, string Name)[]> _loc_Durations = new(() => [
            (15, Loc.T("15 minutes"), "15m", Loc.T("15 minutes")),
            (30, Loc.T("30 minutes"), "30m", Loc.T("30 minutes")),
            (60, Loc.T("1 hour"), "1h", Loc.T("1 hour")),
            (120, Loc.T("2 hours"), "2h", Loc.T("2 hours")),
            (240, Loc.T("4 hours"), "4h", Loc.T("4 hours")),
            (480, Loc.T("8 hours"), "8h", Loc.T("8 hours")),
            (0, Loc.T("Indefinite"), "∞", Loc.T("Indefinitely")),
        ]);

        public static string BatteryBlocked(int percent) => $"Battery at {percent}%. Plug in or lower the battery limit to start";

        public static string EndsIn(TimeSpan remaining)
        {
            var total = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
            var h = total / 3600;
            var m = total % 3600 / 60;
            var s = total % 60;
            if (h > 0)
            {
                return $"Ends in {h} h {m:00} min";
            }

            return m > 0 ? $"Ends in {m} min {s:00} s" : $"Ends in {s} s";
        }

        public static string UntilSummary(string day, TimeSpan span)
        {
            var total = (int)Math.Round(span.TotalMinutes);
            var text = total >= 60 ? $"{total / 60}h {total % 60:00}min" : $"{total}min";
            return $"{day} · {text}";
        }
    }

    public static class Displays
    {
        public static  string Title => Loc.T("Displays");
        public static  string ControlDisplays => Loc.T("Control displays");
        public static  string ControlDisplaysCaption => Loc.T("Brightness and on or off controls for the built-in and external displays, here and in the tray panel.");
        public static  string NoDisplay => Loc.T("No display found.");
        public static  string Off => Loc.T("Off");
        public static  string ExtraDimming => Loc.T("Extra dimming");
        public static  string DimPicture => Loc.T("Dim the picture");
        public static  string TurnOff => Loc.T("Turn off display");
        public static  string TurnOn => Loc.T("Turn on display");
        public static  string LastDisplay => Loc.T("At least one display must stay on.");
        public static  string SwitchingUnavailable => Loc.T("Display switching is unavailable on this PC.");
        public static  string ChangeFailed => Loc.T("Could not change this display.");
        public static  string OpenLid => Loc.T("Open the lid to turn on the built-in display.");
        public static  string MoreOptions => Loc.T("More options");
        public static  string KeysFollowPointer => Loc.T("Brightness keys follow the pointer");
        public static  string KeysFollowPointerCaption => Loc.T("The keyboard brightness keys change the display under the pointer.");
        public static  string KeySteps => Loc.T("Brightness key steps");
        public static  string KeyStepsCaption => Loc.T("Smaller steps for each press of the brightness keys and display brightness shortcuts.");
        public static  string StepStandard => Loc.T("Standard");
        public static  string StepHalf => Loc.T("Half steps");
        public static  string StepQuarter => Loc.T("Quarter steps");
        public static  string UseShortcuts => Loc.T("Use display brightness shortcuts");
        public static  string UseShortcutsCaption => Loc.T("Shortcuts adjust the primary display, or the display under the pointer when pointer following is on.");
        public static  string Decrease => Loc.T("Decrease display brightness");
        public static  string Increase => Loc.T("Increase display brightness");
        public static  string ShowOsd => Loc.T("Show brightness when adjusting");
        public static  string ShowOsdCaption => Loc.T("Shows the brightness percentage when you use the brightness keys or sliders.");
        public static  string ExternalCaption => Loc.T("External displays are adjusted through the same protocol as their own buttons. When the connection cannot carry it, as with HDMI adapters, the slider dims the picture instead, so brightness control works either way.");
        public static  string Detecting => Loc.T("Detecting displays…");
    }

    public static class Bluetooth
    {
        public static  string Title => Loc.T("Bluetooth on sleep");
        public static  string SleepRow => Loc.T("Turn Bluetooth off when the PC sleeps");
        public static  string SleepCaption => Loc.T("Bluetooth already off before sleep is left alone and stays off on wake.");
        public static  string WakeRow => Loc.T("Turn Bluetooth back on when the PC wakes");
        public static  string WakeCaption => Loc.T("Only when Trayloom was the one that switched it off.");
        public static  string NoController => Loc.T("This PC has no Bluetooth controller.");
    }

    public static class ExtraBrightness
    {
        public static  string Title => Loc.T("Extra brightness");
        public const string Caption = "Available only on displays with HDR turned on in Windows (Settings > System > Display > HDR).";
    }
}
