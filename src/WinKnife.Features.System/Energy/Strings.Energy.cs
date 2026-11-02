// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace WinKnife.Features.SystemTools;

/// <summary>English strings of the Energy and display features (spec 02a section 4, 01 section 5.1/5.2, 05 part E).</summary>
internal static partial class Strings
{
    public static class KeepAwake
    {
        public const string Title = "Keep awake";
        public const string Idle = "The PC follows its normal energy rules";
        public const string ActiveIndefinite = "Active until you turn it off";
        public const string AutomationActive = "Active because an automatic condition is met";
        public const string ExternalDisplayActive = "Active while an external display is connected";
        public const string PowerActive = "Active while connected to power";
        public const string RunningAppsActive = "Active while a selected app is running";
        public const string DefaultDuration = "Default duration";
        public const string Automation = "Automation";
        public const string AutomationCaptionAny = "Starts when any selected condition is active.";
        public const string AutomationCaptionAll = "Starts only when every selected condition is active.";
        public const string AutomationOff = "Off";
        public const string ExternalDisplay = "External display";
        public const string Power = "Power";
        public const string Applications = "Applications";
        public const string Any = "Any";
        public const string All = "All";
        public const string SelectedApps = "Selected apps";
        public const string AddApp = "Add an app…";
        public const string RemoveApp = "Remove";
        public const string AppsCaption = "Keep Awake starts while any of these apps is open, even in the background.";
        public const string PauseWhenLocked = "Pause while the PC is locked";
        public const string PauseWhenLockedCaption = "Follows normal sleep rules while locked and resumes the remaining session after you unlock.";
        public const string Options = "Options";
        public const string AutoStart = "Keep Awake when WinKnife opens";
        public const string AutoStartCaption = "Starts a session with the default duration.";
        public const string RightClick = "Right-click the tray icon to toggle Keep Awake";
        public const string RightClickCaption = "Replaces the right-click context menu.";
        public const string ShowCountdown = "Show remaining time next to the icon";
        public const string AllowDisplaySleep = "Allow the display to sleep";
        public const string AllowDisplaySleepCaption = "Keeps the PC awake while the display follows its normal sleep timer.";
        public const string MovePointer = "Move pointer slightly";
        public const string MovePointerCaption = "During a session, moves the pointer a little at the chosen interval.";
        public const string Interval = "Interval";
        public const string ActiveIcon = "Active icon";
        public const string ActiveIconColor = "Active icon color";
        public const string MenuBarIcon = "Tray icon";
        public const string BatteryLimit = "Disable when battery drops below";
        public const string BatteryLimitCaption = "Keeps a forgotten session from draining the laptop battery.";
        public const string Never = "Never";
        public const string LidClosed = "Keep going with the lid closed";
        public const string LidExplanation = "“Keep going with the lid closed” fully disables sleep while “Keep awake” is active and is reverted automatically when the session ends or the app quits. Prefer using it plugged in.";
        public const string LidConfiguring = "Configuring…";
        public const string LidFailed = "Couldn’t turn on closed-lid mode. Try again.";
        public const string LidOn = "Sleep fully disabled. Mind the power";
        public const string LidNeedsSession = "Applied whenever “Keep awake” is active";
        public const string LidReady = "Ready. Toggles without asking for permission";
        public const string DimDisplay = "Dim the display to zero";
        public const string DimDisplayCaption = "Dims the display when the lid closes and brings the brightness back when it opens.";
        public const string ChipHint = "Click a chip to start. Click it again to stop";
        public const string Until = "Until…";
        public const string UntilHeader = "Until";
        public const string Start = "Start";
        public const string Hour = "Hour (0–23)";
        public const string Minute = "Minute";
        public const string SessionEndedTitle = "Session ended";
        public const string SessionEndedBody = "Time is up. The PC will sleep normally again.";
        public const string DisabledTitle = "WinKnife disabled";
        public const string LowBatteryBody = "Low battery. Normal sleep was restored to protect the charge.";

        public static readonly (int Minutes, string Label, string Short, string Name)[] Durations =
        [
            (15, "15 minutes", "15m", "15 minutes"),
            (30, "30 minutes", "30m", "30 minutes"),
            (60, "1 hour", "1h", "1 hour"),
            (120, "2 hours", "2h", "2 hours"),
            (240, "4 hours", "4h", "4 hours"),
            (480, "8 hours", "8h", "8 hours"),
            (0, "Indefinite", "∞", "Indefinitely"),
        ];

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
        public const string Title = "Displays";
        public const string ControlDisplays = "Control displays";
        public const string ControlDisplaysCaption = "Brightness and on or off controls for the built-in and external displays, here and in the tray panel.";
        public const string NoDisplay = "No display found.";
        public const string Off = "Off";
        public const string ExtraDimming = "Extra dimming";
        public const string DimPicture = "Dim the picture";
        public const string TurnOff = "Turn off display";
        public const string TurnOn = "Turn on display";
        public const string LastDisplay = "At least one display must stay on.";
        public const string SwitchingUnavailable = "Display switching is unavailable on this PC.";
        public const string ChangeFailed = "Could not change this display.";
        public const string OpenLid = "Open the lid to turn on the built-in display.";
        public const string MoreOptions = "More options";
        public const string KeysFollowPointer = "Brightness keys follow the pointer";
        public const string KeysFollowPointerCaption = "The keyboard brightness keys change the display under the pointer.";
        public const string KeySteps = "Brightness key steps";
        public const string KeyStepsCaption = "Smaller steps for each press of the brightness keys and display brightness shortcuts.";
        public const string StepStandard = "Standard";
        public const string StepHalf = "Half steps";
        public const string StepQuarter = "Quarter steps";
        public const string UseShortcuts = "Use display brightness shortcuts";
        public const string UseShortcutsCaption = "Shortcuts adjust the primary display, or the display under the pointer when pointer following is on.";
        public const string Decrease = "Decrease display brightness";
        public const string Increase = "Increase display brightness";
        public const string ShowOsd = "Show brightness when adjusting";
        public const string ShowOsdCaption = "Shows the brightness percentage when you use the brightness keys or sliders.";
        public const string ExternalCaption = "External displays are adjusted through the same protocol as their own buttons. When the connection cannot carry it, as with HDMI adapters, the slider dims the picture instead, so brightness control works either way.";
        public const string Detecting = "Detecting displays…";
    }

    public static class Bluetooth
    {
        public const string Title = "Bluetooth on sleep";
        public const string SleepRow = "Turn Bluetooth off when the PC sleeps";
        public const string SleepCaption = "Bluetooth already off before sleep is left alone and stays off on wake.";
        public const string WakeRow = "Turn Bluetooth back on when the PC wakes";
        public const string WakeCaption = "Only when WinKnife was the one that switched it off.";
        public const string NoController = "This PC has no Bluetooth controller.";
    }

    public static class ExtraBrightness
    {
        public const string Title = "Extra brightness";
        public const string Caption = "Available only on displays with HDR turned on in Windows (Settings > System > Display > HDR).";
    }
}
