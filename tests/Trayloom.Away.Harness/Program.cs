// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace Trayloom.Away.Harness;

/// <summary>
/// Console harness for the camera-based leaving of Away mode: <c>AwayPresenceMath</c> (brightness grid, usable frame, motion) and
/// <c>AwayPresenceClock</c> (the pure presence state machine). No camera, window or setting is touched. Exit code 0 = all passed.
/// </summary>
internal static class Program
{
    private static int s_failed;
    private static int s_checks;

    private static int Main()
    {
        PresenceChecks.Math();
        PresenceChecks.Clock();

        Console.WriteLine($"{s_checks - s_failed}/{s_checks} checks passed");
        return s_failed == 0 ? 0 : 1;
    }

    internal static void Check(bool ok, string what)
    {
        s_checks++;
        if (!ok)
        {
            s_failed++;
        }

        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}");
    }
}
