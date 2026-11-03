// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using Trayloom.Core.Native;

namespace Trayloom.Features.Tools;

/// <summary>Natives used by the radial menu only.</summary>
internal static class RadialNative
{
    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    /// <summary>Scale factor (1.0 = 96 dpi) of the monitor containing the physical-pixel point.</summary>
    public static double ScaleAt(double x, double y)
    {
        try
        {
            var monitor = NativeMethods.MonitorFromPoint(new POINT { X = (int)x, Y = (int)y }, NativeConstants.MONITOR_DEFAULTTONEAREST);
            return GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1.0;
        }
        catch
        {
            return 1.0;
        }
    }
}
