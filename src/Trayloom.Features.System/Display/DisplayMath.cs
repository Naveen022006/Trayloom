// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace Trayloom.Features.SystemTools;

/// <summary>Small pure decisions of the display feature, kept apart from the native calls so a harness can check them.</summary>
internal static class DisplayMath
{
    /// <summary>
    /// The percentage one brightness key press moves: 10 (standard), 5 (half) or 2.5 (quarter) rounded half away from zero, so
    /// the quarter step is 3 and not the 2 that the default banker's rounding would give.
    /// </summary>
    public static int KeyStep(string? setting)
    {
        var baseStep = setting switch { "half" => 5.0, "quarter" => 2.5, _ => 10.0 };
        return Math.Max(1, (int)Math.Round(baseStep, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// The level a hardware write may use: never below 5 %, so no key press or slider drag can turn the panel black with no way
    /// to see the controls again. The software overlay keeps its own 5..100 range.
    /// </summary>
    public static int ClampHardware(int percent) => Math.Clamp(percent, 5, 100);

    /// <summary>
    /// True when a hardware control id belongs to the monitor with this GDI name. Ids are either the bare GDI name (WMI panel)
    /// or the name followed by "#index" (DDC/CI), so a plain prefix test would let \\.\DISPLAY1 claim \\.\DISPLAY10.
    /// </summary>
    public static bool IsDeviceOf(string hardwareId, string gdiName)
    {
        if (string.IsNullOrEmpty(gdiName) || !hardwareId.StartsWith(gdiName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return hardwareId.Length == gdiName.Length || hardwareId[gdiName.Length] == '#';
    }

    /// <summary>
    /// True when the monitor may be taken for the laptop panel although the display topology could not be read: only with a
    /// single monitor, because with several the first one could just as well be an external screen.
    /// </summary>
    public static bool AssumeInternalWithoutTopology(int topologyCount, int monitorCount, int internalBrightnessCount, bool alreadyAssigned)
    {
        return topologyCount == 0 && monitorCount == 1 && internalBrightnessCount > 0 && !alreadyAssigned;
    }
}
