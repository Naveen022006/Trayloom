// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.WindowTools.AwayMode;

/// <summary>Strings of Away mode (spec 06 section 4), English verbatim.</summary>
internal static class AwayStrings
{
    public static string Title => Loc.T("Away mode");
    public static string Description => Loc.T("Minimizes your windows when you leave the PC and brings them back, in the same order, when you return. Nothing is closed, so nothing you were doing is lost.");
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

    // The cover.
    public static string CoverAway => Loc.T("Away");
    public static string CoverSince(string time) => Loc.F("Since {0}", time);
    public static string CoverLooking => Loc.T("Looking for you with the camera");
    public static string CoverNotRecognised => Loc.T("Not recognised? Press I’m back to bring your windows back.");
    public static string ImBack => Loc.T("I’m back");
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
