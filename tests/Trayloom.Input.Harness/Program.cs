// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Features.Input;

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
