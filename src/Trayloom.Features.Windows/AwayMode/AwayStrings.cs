// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.WindowTools.AwayMode;

/// <summary>Strings of Away mode (spec 06 section 4), English verbatim.</summary>
internal static class AwayStrings
{
    public static string Title => Loc.T("Away mode");
    public static string Description => Loc.T("Minimizes your windows when you leave the PC and brings them back, in the same order, when you return. Nothing is closed unless you choose apps to close below.");
    public static string GoAwayNow => Loc.T("Go away now");
    public static string RestoreWindows => Loc.T("Restore windows");
    public static string Off => Loc.T("Off");
    public static string PanelCaption(int minutes) => Loc.F("After {0} min without input", minutes);

    public static string AwayAfter => Loc.T("Away after");
    public static string Minutes2 => Loc.T("2 minutes without input");
    public static string Minutes5 => Loc.T("5 minutes without input");
    public static string Minutes10 => Loc.T("10 minutes without input");
    public static string Minutes15 => Loc.T("15 minutes without input");
    public static string Minutes30 => Loc.T("30 minutes without input");
    public static string Hour1 => Loc.T("1 hour without input");

    public static string Here => Loc.T("Here");
    public static string AwaySince(string time, int windows) => Loc.F("Away since {0} · {1} windows", time, windows);
    public static string Waiting(string reason) => Loc.F("Waiting: {0}", reason);
    public static string WaitMedia => Loc.T("a video or music is playing");
    public static string WaitCalls => Loc.T("an app uses the microphone or camera");
    public static string WaitFullScreen => Loc.T("an app is full screen");
    public static string WaitRecording => Loc.T("Trayloom is recording the screen");

    public static string LockPc => Loc.T("Lock the PC too");
    public static string LockPcCaption => Loc.T("Your windows come back when you unlock. This is the only way to keep others out.");
    public static string Cover => Loc.T("Cover the screens");
    public static string CoverCaption => Loc.T("Shows a dark screen with the time while you are away.");

    public static string Face => Loc.T("Come back when the camera sees a face");
    public static string FaceCaption => Loc.T("The camera light stays on while you are away and Trayloom looks for you. Pictures are checked on this PC and never saved. Any face brings your windows back: this is face detection, not recognition.");
    public static string FaceNotNeeded => Loc.T("Not needed while “Lock the PC too” is on: unlocking brings your windows back.");
    public static string FaceUnsupported => Loc.T("Face detection is not available on this PC.");
    public static string Camera => Loc.T("Camera");
    public static string StopLooking => Loc.T("Stop looking after");
    public static string Stop30 => Loc.T("30 minutes");
    public static string Stop60 => Loc.T("1 hour");
    public static string Stop120 => Loc.T("2 hours");
    public static string Stop240 => Loc.T("4 hours");
    public static string StopNever => Loc.T("Never");
    public static string CameraDenied => Loc.T("Camera access is off for desktop apps.");
    public static string OpenCameraSettings => Loc.T("Open camera settings");
    public static string CameraFailed => Loc.T("The camera could not start the last time. Your windows came back with the mouse or keyboard instead.");

    public static string StayHereWhen => Loc.T("Stay here when");
    public static string SkipMedia => Loc.T("A video or music is playing");
    public static string SkipCalls => Loc.T("An app uses the microphone or camera");
    public static string SkipFullScreen => Loc.T("An app is full screen or presenting");
    public static string IgnoredTitle => Loc.T("Leave these apps alone");
    public static string IgnoredAdd => Loc.T("Add an app…");
    public static string IgnoredCaption => Loc.T("Windows of these apps stay as they are while you are away.");

    public static string NowCaption => Loc.T("Your windows are minimized at once. Move the mouse to bring them back.");
    public static string NowCaptionFace => Loc.T("Your windows are minimized at once. Look at the camera to bring them back.");

    public static string NowCaptionClose => Loc.T("Your windows are minimized and the chosen apps are closed at once. Move the mouse to bring everything back.");
    public static string NowCaptionCloseFace => Loc.T("Your windows are minimized and the chosen apps are closed at once. Look at the camera to bring everything back.");

    // Closing apps (spec 07 section 9).
    public static string CloseApps => Loc.T("Close apps while I’m away");
    public static string CloseAppsCaption => Loc.T("Chosen apps are closed when you leave and opened again when you’re back. Chrome and Edge come back with the same windows and tabs. An app that asks to save something is left open and minimized, so nothing is lost.");
    public static string CloseAppsElevated => Loc.T("Not available while Trayloom runs as administrator: the apps would open again as administrator.");
    public static string CloseThem => Loc.T("Close them");
    public static string CloseNow => Loc.T("When I leave");
    public static string CloseAfter5 => Loc.T("After 5 minutes away");
    public static string CloseAfter15 => Loc.T("After 15 minutes away");
    public static string CloseAfter30 => Loc.T("After 30 minutes away");
    public static string CloseAfter60 => Loc.T("After 1 hour away");
    public static string CloseListTitle => Loc.T("Apps to close");
    public static string CloseListAdd => Loc.T("Add an app…");
    public static string CloseListCaption => Loc.T("Only these apps are closed. The rest are minimized as usual.");
    public static string AccessoryBrowser => Loc.T("Windows and tabs come back");
    public static string AccessoryBrowserMin => Loc.T("Only minimized: Trayloom can’t reopen its tabs yet");
    public static string AccessoryApp => Loc.T("Opens again when you’re back");
    public static string AccessoryNever => Loc.T("Can’t be closed by Trayloom");
    public static string CloseNote => Loc.T("Private and incognito windows can’t be reopened, so a browser with one open, or with a download running, is only minimized. Browsers close the way they do when Windows restarts for an update. A page that asks “Leave site?” may not get to ask.");
    public static string LastTime => Loc.T("Last time you were away");
    public static string LastLine(string app, string reason) => Loc.F("{0}: {1}", app, reason);

    public static string Reason(AwayKeepReason reason) => reason switch
    {
        AwayKeepReason.Private => Loc.T("a private window was open, so it was only minimized"),
        AwayKeepReason.Download => Loc.T("a download was running, so it was only minimized"),
        AwayKeepReason.OtherDesktop => Loc.T("it has windows on another desktop, so it was only minimized"),
        AwayKeepReason.Unsupported => Loc.T("Trayloom can’t reopen its tabs yet, so it was only minimized"),
        AwayKeepReason.Prompt => Loc.T("it asked to save something, so it was left open"),
        AwayKeepReason.Timeout => Loc.T("it didn’t close in time, so it was left open"),
        AwayKeepReason.NotResponding => Loc.T("it wasn’t responding, so it was left open"),
        AwayKeepReason.Refused => Loc.T("it refused to close, so it was left open"),
        AwayKeepReason.Elevated => Loc.T("it runs as administrator, so Trayloom can’t close it"),
        AwayKeepReason.OtherUser => Loc.T("it runs as another user, so Trayloom can’t close it"),
        AwayKeepReason.Cancelled => Loc.T("you came back before it closed"),
        AwayKeepReason.Limit => Loc.T("too many apps to close at once, so it was only minimized"),
        AwayKeepReason.Failed => Loc.T("it didn’t open again"),
        AwayKeepReason.Partial => Loc.T("it opened again with fewer windows"),
        _ => string.Empty,
    };

    public static string AwaySinceClosed(string time, int windows, int apps) => Loc.F("Away since {0} · {1} windows · {2} apps closed", time, windows, apps);
    public static string AwaySinceWaiting(string time, int windows, string closeTime) => Loc.F("Away since {0} · {1} windows · apps close at {2}", time, windows, closeTime);
    public static string Closing => Loc.T("Closing apps…");
    public static string Opening(int apps) => Loc.F("Opening {0} apps…", apps);

    // The tray card.
    public static string PanelAndOpens(int apps) => Loc.F("And opens {0} closed apps again", apps);
    public static string PanelOpenClosed(int apps) => Loc.F("Open {0} closed apps again", apps);
    public static string PanelClosedWhileAway => Loc.T("Closed while you were away");
    public static string ReopenAction => Loc.T("Open closed apps again");

    // The recovery card (spec 07 section 9.4).
    public static string RecoveryTitle => Loc.T("Apps closed while you were away");
    public static string RecoveryText(int apps, string time) => Loc.F("Trayloom closed {0} apps when you left at {1}, and they were not opened again because Trayloom or Windows stopped first.", apps, time);
    public static string RecoveryFailed(int apps) => Loc.F("{0} apps did not open again when you came back.", apps);
    public static string OpenThemAgain => Loc.T("Open them again");
    public static string ForgetThem => Loc.T("Forget them");

    // Notifications (counts only: the notifier logs them).
    public static string NotificationRecovery(int apps) => Loc.F("{0} apps were closed while you were away. Open them again from the tray icon or from Settings.", apps);
    public static string NotificationFailed(int apps) => Loc.F("{0} apps did not open again. Settings > Away mode can try again.", apps);

    // Leaving when the camera does not see the person.
    public static string LeaveCamera => Loc.T("Leave when the camera doesn’t see you");
    public static string LeaveCameraCaption => Loc.T("After 30 seconds without the mouse or keyboard, the camera turns on and checks whether someone is at the PC. If nobody is there for the time below, Away mode starts after a 10-second notice. Pictures are checked on this PC and never saved; the camera light is on while it checks.");
    public static string LeaveAfter => Loc.T("Nobody there for");
    public static string Leave30 => Loc.T("30 seconds");
    public static string Leave60 => Loc.T("1 minute");
    public static string Leave120 => Loc.T("2 minutes");
    public static string Leave300 => Loc.T("5 minutes");
    public static string LeaveNote => Loc.T("If the camera sees only darkness, it turns off and Away mode gives a 10-second notice after about 2½ minutes without input. While another app uses the camera or battery saver is on, the camera check waits.");
    public static string StatusLooking => Loc.T("Checking that you’re here with the camera");
    public static string StatusClosingIn(int seconds) => Loc.F("Closing your workspace in {0} s", seconds);
    public static string StatusMinimizingIn(int seconds) => Loc.F("Minimizing your windows in {0} s", seconds);
    public static string NoticeClosing => Loc.F("Closing your workspace in {0} s. Move to cancel.", 10);
    public static string NoticeMinimizing => Loc.F("Minimizing your windows in {0} s. Move to cancel.", 10);
    public static string StripClosing(int seconds) => Loc.F("Closing in {0} s", seconds);
    public static string StripMinimizing(int seconds) => Loc.F("Minimizing in {0} s", seconds);
    public static string StripLooking => Loc.T("Checking you’re here");
    public static string ToastClosing => Loc.F("The camera doesn’t see you. Your workspace closes in {0} seconds; move the mouse to stay.", 10);
    public static string ToastMinimizing => Loc.F("The camera doesn’t see you. Your windows are minimized in {0} seconds; move the mouse to stay.", 10);

    // Which apps are closed.
    public static string CloseScope => Loc.T("Which apps");
    public static string ScopeList => Loc.T("The apps on the list");
    public static string ScopeMode => Loc.T("The active mode’s apps and the list");
    public static string ScopeAll => Loc.T("All my apps");
    public static string ScopeAllCaption => Loc.T("Every app with an open window is closed, up to 20, except File Explorer, terminals and the apps under “Leave these apps alone”. Closing an app also ends what runs inside it, such as a build in an editor’s terminal.");
    public static string ScopeModeCaption => Loc.T("The apps of the mode you’re in (Modes) are closed too, and open again when you’re back.");

    // The cover.
    public static string CoverAway => Loc.T("Away");
    public static string CoverSince(string time) => Loc.F("Since {0}", time);
    public static string CoverLooking => Loc.T("Looking for you with the camera");
    public static string CoverNotRecognised => Loc.T("Not recognised? Press I’m back to bring your windows back.");
    public static string ImBack => Loc.T("I’m back");
    public static string CoverClosing => Loc.T("Closing your apps…");
    public static string CoverClosed(int apps) => Loc.F("{0} apps closed. They open again when you’re back.", apps);
    public static string CoverKept(int apps) => Loc.F("{0} apps were only minimized.", apps);
}

/// <summary>Fire-and-forget that cannot end in an unobserved exception: whatever the work throws is logged.</summary>
internal static class AwayGuard
{
    /// <summary>Starts <paramref name="work"/>; <paramref name="what"/> names it in the log. Exceptions are logged, never rethrown.</summary>
    internal static async void FireAndLog(Func<Task> work, ILog log, string what)
    {
        try
        {
            await work();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            try
            {
                log.Error("[away] " + what + " failed", ex);
            }
            catch
            {
                // a logger that fails has nothing left to report to
            }
        }
    }
}
