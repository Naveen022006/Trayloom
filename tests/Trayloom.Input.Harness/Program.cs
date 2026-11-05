// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Features.Input;
using Trayloom.Features.WindowTools;

namespace Trayloom.Input.Harness;

/// <summary>Pure-logic checks only: nothing here injects input or touches the clipboard.</summary>
internal static class Program
{
    private static int s_failed;
    private static int s_checks;

    private static int Main()
    {
        // ShouldRelease(active, idleMs, foregroundMissing, foregroundElevated, inputDesktopNotDefault, sessionLocked)
        Check(!SuperKeyMath.ShouldRelease(false, 99_999, true, true, true, true), "inactive never releases");
        Check(!SuperKeyMath.ShouldRelease(true, 0, false, false, false, false), "active, all clear: keep");
        Check(!SuperKeyMath.ShouldRelease(true, 20_000, false, false, false, false), "exactly 20 s is not over");
        Check(SuperKeyMath.ShouldRelease(true, 20_001, false, false, false, false), "idle over 20 s releases");
        Check(SuperKeyMath.ShouldRelease(true, 0, true, false, false, false), "missing foreground releases");
        Check(SuperKeyMath.ShouldRelease(true, 0, false, true, false, false), "elevated foreground releases");
        Check(SuperKeyMath.ShouldRelease(true, 0, false, false, true, false), "non-default desktop releases");
        Check(SuperKeyMath.ShouldRelease(true, 0, false, false, false, true), "locked session releases");

        Check(ClipboardRestorePolicy.ShouldRestore(5, 5, true), "unchanged and complete: restore");
        Check(!ClipboardRestorePolicy.ShouldRestore(5, 6, true), "clipboard changed: do not restore");
        Check(!ClipboardRestorePolicy.ShouldRestore(5, 5, false), "incomplete snapshot: do not restore");

        Check(SuperKeyFeature.ParseModifiers("control+option+shift+command").SequenceEqual(new ushort[] { 0xA2, 0xA4, 0xA0, 0x5B }), "parse all four");
        Check(SuperKeyFeature.ParseModifiers("shift").SequenceEqual(new ushort[] { 0xA2, 0xA4, 0xA0, 0x5B }), "shift only falls back to all four");
        Check(SuperKeyFeature.ParseModifiers(null).Length == 4, "null falls back to all four");
        Check(SuperKeyFeature.ParseModifiers("control+shift").SequenceEqual(new ushort[] { 0xA2, 0xA0 }), "control+shift kept");

        Check(SuperKeyMath.MissingConfirmed(2) && !SuperKeyMath.MissingConfirmed(1) && !SuperKeyMath.MissingConfirmed(0), "missing foreground needs two samples");
        Check(SuperKeyMath.ShouldFlushPending(true, false, false, false, false), "pending flushes on a normal foreground");
        Check(!SuperKeyMath.ShouldFlushPending(false, false, false, false, false), "nothing pending: nothing to flush");
        Check(!SuperKeyMath.ShouldFlushPending(true, true, false, false, false), "no flush while a chord is active");
        Check(!SuperKeyMath.ShouldFlushPending(true, false, false, true, false), "no flush while the foreground is elevated");
        Check(!SuperKeyMath.ShouldFlushPending(true, false, true, false, false), "no flush without a foreground");
        Check(!SuperKeyMath.ShouldFlushPending(true, false, false, false, true), "no flush on the secure desktop");

        Check(SnippetPastePolicy.Decide("hi there", true, true) == SnippetInsertMode.Type, "short single line is typed");
        Check(SnippetPastePolicy.Decide("hi there", false, false) == SnippetInsertMode.Type, "short single line is typed even with a bad clipboard");
        Check(SnippetPastePolicy.Decide("a\nb", true, true) == SnippetInsertMode.PasteAndRestore, "multi-line pastes and restores");
        Check(SnippetPastePolicy.Decide("a\nb", false, true) == SnippetInsertMode.PasteKeepSnippet, "multi-line with incomplete snapshot still pastes, no restore");
        Check(SnippetPastePolicy.Decide("a\nb", true, false) == SnippetInsertMode.Skip, "multi-line is never typed");
        Check(SnippetPastePolicy.Decide(new string('x', 201), true, false) == SnippetInsertMode.Skip, "long single line is not typed either");
        Check(SnippetPastePolicy.Decide(new string('x', 200), true, true) == SnippetInsertMode.Type, "200 characters is still typed");

        string[] temp = [@"C:\Users\me\AppData\Local\Temp\"];
        Check(AutoQuitPolicy.IsInstallerPath(@"C:\Users\me\Downloads\ChromeSetup.exe", temp), "ChromeSetup is an installer");
        Check(AutoQuitPolicy.IsInstallerPath(@"C:\x\Firefox Setup 130.exe", temp), "Firefox Setup is an installer");
        Check(AutoQuitPolicy.IsInstallerPath(@"C:\Windows\System32\msiexec.exe", temp), "msiexec");
        Check(AutoQuitPolicy.IsInstallerPath(@"C:\x\unins000.exe", temp), "unins000");
        Check(AutoQuitPolicy.IsInstallerPath(@"C:\x\AppUpdater.exe", temp), "updater");
        Check(AutoQuitPolicy.IsInstallerPath(@"C:\x\Foo.tmp", temp), ".tmp image");
        Check(AutoQuitPolicy.IsInstallerPath(@"C:\Users\me\AppData\Local\Temp\is-AB12\app.exe", temp), "exe under temp");
        Check(!AutoQuitPolicy.IsInstallerPath(@"C:\Program Files\Notepad++\notepad++.exe", temp), "a normal app is not an installer");
        Check(AutoQuitPolicy.IsNeverQuit(@"C:\Windows\System32\csrss.exe", temp), "protected name is never quit");

        Check(AutoQuitPolicy.Decide(true, false, 99_999, 99_999) == QuitStep.Abort, "blocked aborts");
        Check(AutoQuitPolicy.Decide(false, true, 99_999, -1) == QuitStep.Reset, "a visible window resets the clock");
        Check(AutoQuitPolicy.Decide(false, false, 14_999, -1) == QuitStep.Wait, "under 15 s waits");
        Check(AutoQuitPolicy.Decide(false, false, 15_000, -1) == QuitStep.AskClose, "15 s asks WM_CLOSE first");
        Check(AutoQuitPolicy.Decide(false, false, 20_000, 1_000) == QuitStep.Wait, "gives WM_CLOSE time");
        Check(AutoQuitPolicy.Decide(false, false, 30_000, 5_000) == QuitStep.PostQuit, "then WM_QUIT");
        Check(AutoQuitPolicy.Decide(false, true, 30_000, 5_000) == QuitStep.Reset, "a window that appears before WM_QUIT stops it");

        Console.WriteLine($"{s_checks - s_failed}/{s_checks} checks passed");
        return s_failed == 0 ? 0 : 1;
    }

    private static void Check(bool ok, string what)
    {
        s_checks++;
        if (!ok)
        {
            s_failed++;
        }

        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}");
    }
}
