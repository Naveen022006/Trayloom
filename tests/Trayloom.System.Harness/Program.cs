// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core.Ui;
using Trayloom.Features.SystemTools;

namespace Trayloom.System.Harness;

/// <summary>
/// Console harness for <see cref="DisplayMath"/>: the brightness key step, the hardware id match and the internal-panel
/// fallback. Pure functions only; no display, WMI or setting is touched. Exit code 0 = all passed.
/// </summary>
internal static class Program
{
    private static int s_failed;
    private static int s_checks;

    private static int Main()
    {
        Check(DisplayMath.KeyStep("standard") == 10, "standard step is 10");
        Check(DisplayMath.KeyStep("half") == 5, "half step is 5");
        Check(DisplayMath.KeyStep("quarter") == 3, "quarter step rounds 2.5 up to 3, not to even 2");
        Check(DisplayMath.KeyStep(null) == 10, "unknown setting falls back to 10");

        Check(DisplayMath.IsDeviceOf(@"\\.\DISPLAY1", @"\\.\DISPLAY1"), "exact id matches");
        Check(DisplayMath.IsDeviceOf(@"\\.\DISPLAY1#0", @"\\.\DISPLAY1"), "DDC id with #index matches");
        Check(!DisplayMath.IsDeviceOf(@"\\.\DISPLAY10", @"\\.\DISPLAY1"), "DISPLAY10 is not DISPLAY1");
        Check(!DisplayMath.IsDeviceOf(@"\\.\DISPLAY12#1", @"\\.\DISPLAY1"), "DISPLAY12#1 is not DISPLAY1");
        Check(DisplayMath.IsDeviceOf(@"\\.\display2#0", @"\\.\DISPLAY2"), "match ignores case");
        Check(!DisplayMath.IsDeviceOf(@"\\.\DISPLAY1", string.Empty), "empty GDI name never matches");

        Check(DisplayMath.AssumeInternalWithoutTopology(0, 1, 1, false), "lone monitor without topology is the panel");
        Check(!DisplayMath.AssumeInternalWithoutTopology(0, 2, 1, false), "several monitors without topology: no guess");
        Check(!DisplayMath.AssumeInternalWithoutTopology(2, 1, 1, false), "topology available: no guess");
        Check(!DisplayMath.AssumeInternalWithoutTopology(0, 1, 0, false), "no WMI panel: no guess");
        Check(!DisplayMath.AssumeInternalWithoutTopology(0, 1, 1, true), "panel already assigned: no guess");

        // Settings row stacking and tile grid arithmetic (Core, pure).
        Check(SettingsLayoutMath.ShouldStack(300, 38, 214, false), "narrow row stacks its accessory");
        Check(!SettingsLayoutMath.ShouldStack(684, 38, 214, false), "wide row keeps the accessory beside the text");
        Check(!SettingsLayoutMath.ShouldStack(400, 38, 0, false), "no accessory never stacks");
        Check(SettingsLayoutMath.ShouldStack(434 + 10, 38, 214, true), "stacked row stays stacked inside the hysteresis band");
        Check(!SettingsLayoutMath.ShouldStack(434 + SettingsLayoutMath.StackHysteresis, 38, 214, true), "stacked row unstacks past the band");
        Check(!SettingsLayoutMath.ShouldStack(434, 38, 214, false), "unstacked row at the threshold stays unstacked");
        Check(SettingsLayoutMath.TileColumns(684, 116, 10, 6) == 5, "684 wide fits five 116 tiles");
        Check(SettingsLayoutMath.TileColumns(50, 116, 10, 6) == 1, "never fewer than one column");
        Check(SettingsLayoutMath.TileColumns(2000, 92, 8, 4) == 4, "capped at MaxColumns");
        Check(Math.Abs(SettingsLayoutMath.TileCellWidth(684, 5, 10) - 128.8) < 1e-9, "cell width shares the width minus gaps");

        BluetoothChecks.Run(Check);
        SafetyChecks.Run(Check);

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
