// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace Trayloom.Modes.Harness;

/// <summary>
/// Console harness for Modes: the pure rules (<c>ModesMath</c>), the <c>modes.json</c> store and the Chromium session parser. It
/// touches no window, process, browser file or setting; the session bytes are built in code. Exit code 0 = all passed.
/// </summary>
internal static class Program
{
    private static int s_failed;
    private static int s_checks;

    private static int Main()
    {
        MathChecks.Run();
        StoreChecks.Run();
        SessionChecks.Run();
        DraftChecks.Run();

        Console.WriteLine($"{s_checks - s_failed}/{s_checks} checks passed");
        return s_failed == 0 ? 0 : 1;
    }

    internal static void Check(bool ok, string what)
    {
        s_checks++;
        if (!ok)
        {
            s_failed++;
            Console.WriteLine("FAILED: " + what);
        }
    }
}
