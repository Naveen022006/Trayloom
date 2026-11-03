// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;

namespace Trayloom.Core.Native;

/// <summary>One monitor: its full rectangle and the part not covered by the taskbar (both in virtual-screen coordinates).</summary>
public readonly record struct MonitorBounds(RECT Monitor, RECT Work);

/// <summary>
/// Where a foreign window may live. Pure arithmetic over a list of monitors (so it can be tested without a screen), plus the one
/// place that enumerates the real monitors. The primary monitor is always first in the list.
/// </summary>
public static class WindowPlacementMath
{
    /// <summary>A window narrower or lower than this is not one a person can use.</summary>
    private const int MinVisibleWidth = 200;
    private const int MinVisibleHeight = 150;

    /// <summary>The caption strip: this much of the window's top has to be reachable (it is what the person grabs).</summary>
    private const int CaptionHeight = 32;
    private const int MinCaptionWidth = 160;
    private const int MinCaptionVisible = 24;

    /// <summary>How far above a monitor a window may start and still count as starting at its top edge (the invisible frame of a maximised window).</summary>
    private const int CaptionTolerance = 16;

    private const int DefaultWidth = 1200;
    private const int DefaultHeight = 800;
    private const int MinWidth = 480;
    private const int MinHeight = 360;
    private const int Absurd = 20000;

    /// <summary>The monitors, primary first. Never empty: without a screen the primary metrics stand in for one.</summary>
    public static IReadOnlyList<MonitorBounds> GetMonitors()
    {
        var list = new List<MonitorBounds>();
        try
        {
            GuardianNative.EnumDisplayMonitors(0, 0, (nint monitor, nint hdc, ref RECT rect, nint data) =>
            {
                var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (NativeMethods.GetMonitorInfoW(monitor, ref info))
                {
                    var area = new MonitorBounds(info.rcMonitor, info.rcWork);
                    if ((info.dwFlags & 1) != 0)
                    {
                        list.Insert(0, area);
                    }
                    else
                    {
                        list.Add(area);
                    }
                }

                return true;
            }, 0);
        }
        catch
        {
            // fall through to the primary metrics
        }

        if (list.Count == 0)
        {
            var w = Math.Max(800, GuardianNative.GetSystemMetrics(0));
            var h = Math.Max(600, GuardianNative.GetSystemMetrics(1));
            var all = new RECT { Left = 0, Top = 0, Right = w, Bottom = h };
            list.Add(new MonitorBounds(all, all));
        }

        return list;
    }

    /// <summary>The window can be seen and grabbed: big enough, and its caption strip is clearly inside some monitor's work area.</summary>
    public static bool IsComfortablyVisible(RECT r, IReadOnlyList<MonitorBounds> monitors)
    {
        var w = r.Right - r.Left;
        var h = r.Bottom - r.Top;
        if (w < MinVisibleWidth || h < MinVisibleHeight)
        {
            return false;
        }

        var needWidth = Math.Min(MinCaptionWidth, w / 2);
        foreach (var m in monitors)
        {
            // A maximised window's rectangle starts at minus its frame (8 px at 100 %, 10 at 125 %, 12 at 150 %, more above): up to
            // CaptionTolerance above the monitor the caption strip starts at the monitor's top edge instead of where the rectangle does.
            var top = r.Top >= m.Monitor.Top - CaptionTolerance ? Math.Max(r.Top, m.Monitor.Top) : r.Top;
            var width = Math.Min(r.Right, m.Work.Right) - Math.Max(r.Left, m.Work.Left);
            var height = Math.Min(top + CaptionHeight, m.Work.Bottom) - Math.Max(top, m.Work.Top);
            if (width >= needWidth && height >= MinCaptionVisible)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The rectangle itself when it is comfortably visible; otherwise a usable one on the nearest monitor: sized sensibly (480x360 up to the
    /// work area; 1200x800 when the size is unknown), kept inside the work area, centred when the place is invalid or parked far away.
    /// </summary>
    public static RECT Sanitize(RECT r, IReadOnlyList<MonitorBounds> monitors)
    {
        if (monitors.Count == 0)
        {
            return r;
        }

        if (IsComfortablyVisible(r, monitors))
        {
            return r;
        }

        var rw = r.Right - r.Left;
        var rh = r.Bottom - r.Top;
        var invalid = rw <= 0 || rh <= 0 || Math.Abs(r.Left) > Absurd || Math.Abs(r.Top) > Absurd || Math.Abs(r.Right) > Absurd || Math.Abs(r.Bottom) > Absurd;
        var monitor = invalid ? monitors[0] : Nearest(r, monitors);
        var work = monitor.Work;
        var workW = Math.Max(1, work.Right - work.Left);
        var workH = Math.Max(1, work.Bottom - work.Top);

        // a window with an unusable size is given a normal one; a big one is shrunk to what the screen can show
        var w = Math.Min(Math.Max(rw > 0 ? rw : DefaultWidth, MinWidth), workW);
        var h = Math.Min(Math.Max(rh > 0 ? rh : DefaultHeight, MinHeight), workH);

        // parked windows (and everything that is not on any monitor at all) are centred, not dragged to an edge
        var parked = invalid || r.Left <= -10000 || r.Top <= -10000 || !Intersects(r, monitor.Monitor);
        int left;
        int top;
        if (parked)
        {
            left = work.Left + ((workW - w) / 2);
            top = work.Top + ((workH - h) / 2);
        }
        else
        {
            left = Math.Min(Math.Max(r.Left, work.Left), work.Right - w);
            top = Math.Min(Math.Max(r.Top, work.Top), work.Bottom - h);
        }

        return new RECT { Left = left, Top = top, Right = left + w, Bottom = top + h };
    }

    /// <summary>Where a window with no place of its own goes: the middle of the primary monitor's work area.</summary>
    public static RECT Centered(IReadOnlyList<MonitorBounds> monitors) =>
        Sanitize(new RECT { Left = -30000, Top = -30000, Right = -30000, Bottom = -30000 }, monitors);

    /// <summary>
    /// WINDOWPLACEMENT.rcNormalPosition of a window without WS_EX_TOOLWINDOW is in workspace coordinates (relative to the work area of the
    /// primary monitor), not in screen coordinates. This is the offset between the two.
    /// </summary>
    public static POINT WorkspaceOffset(IReadOnlyList<MonitorBounds> monitors)
    {
        if (monitors.Count == 0)
        {
            return default;
        }

        var primary = monitors[0];
        return new POINT { X = primary.Work.Left - primary.Monitor.Left, Y = primary.Work.Top - primary.Monitor.Top };
    }

    /// <summary>A workspace-coordinate rectangle (WINDOWPLACEMENT) in screen coordinates.</summary>
    public static RECT WorkspaceToScreen(RECT r, IReadOnlyList<MonitorBounds> monitors) => Offset(r, WorkspaceOffset(monitors), 1);

    /// <summary>A screen-coordinate rectangle as WINDOWPLACEMENT wants it.</summary>
    public static RECT ScreenToWorkspace(RECT r, IReadOnlyList<MonitorBounds> monitors) => Offset(r, WorkspaceOffset(monitors), -1);

    private static RECT Offset(RECT r, POINT by, int sign) => new()
    {
        Left = r.Left + (sign * by.X),
        Top = r.Top + (sign * by.Y),
        Right = r.Right + (sign * by.X),
        Bottom = r.Bottom + (sign * by.Y),
    };

    private static bool Intersects(RECT a, RECT b) => a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;

    /// <summary>The monitor closest to the rectangle (by the gap between them; the one holding most of it wins by being at distance 0).</summary>
    private static MonitorBounds Nearest(RECT r, IReadOnlyList<MonitorBounds> monitors)
    {
        var best = monitors[0];
        long bestDistance = long.MaxValue;
        foreach (var m in monitors)
        {
            var dx = Math.Max(Math.Max(m.Monitor.Left - r.Right, r.Left - m.Monitor.Right), 0);
            var dy = Math.Max(Math.Max(m.Monitor.Top - r.Bottom, r.Top - m.Monitor.Bottom), 0);
            var distance = ((long)dx * dx) + ((long)dy * dy);
            if (distance < bestDistance)
            {
                best = m;
                bestDistance = distance;
            }
        }

        return best;
    }
}
