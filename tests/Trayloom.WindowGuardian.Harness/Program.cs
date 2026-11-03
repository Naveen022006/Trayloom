// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core.Native;

namespace Trayloom.WindowGuardian.Harness;

/// <summary>H1: placement arithmetic of the window guardian (pure, no screen needed). Exit code 0 = all passed.</summary>
public static class Program
{
    private static int _checks;
    private static int _failures;

    public static int Main()
    {
        var single = new[] { Mon(0, 0, 1536, 912, 0, 0, 1536, 864) };
        var dual = new[]
        {
            Mon(0, 0, 1920, 1080, 0, 0, 1920, 1040),
            Mon(1920, 0, 3840, 1080, 1920, 0, 3840, 1040),
        };
        var topTaskbar = new[] { Mon(0, 0, 1920, 1080, 0, 48, 1920, 1080) };

        // the reported defect: caption at 885 of a 912 high screen whose taskbar takes the bottom 48 px
        var r = R(100, 885, 1300, 1685);
        Check("885/912: not comfortable", !WindowPlacementMath.IsComfortablyVisible(r, single));
        var s = WindowPlacementMath.Sanitize(r, single);
        Check("885/912: caption inside the work area", WindowPlacementMath.IsComfortablyVisible(s, single) && s.Top >= 0 && s.Top + 32 <= 864, Text(s));
        Check("885/912: keeps its size and x", s.Width == 1200 && s.Height == 800 && s.Left == 100, Text(s));

        // parked far away -> centred on the primary work area, shrunk to fit
        s = WindowPlacementMath.Sanitize(R(-30000, -30000, -27600, -28500), single);
        Check("-30000: centred and shrunk to the work area", s.Left == 0 && s.Top == 0 && s.Width == 1536 && s.Height == 864, Text(s));
        s = WindowPlacementMath.Sanitize(R(-30000, -30000, -28800, -29200), single);
        Check("-30000 (1200x800): centred", s.Left == 168 && s.Top == 32 && s.Width == 1200 && s.Height == 800, Text(s));

        // larger than the work area and not reachable -> shrunk
        s = WindowPlacementMath.Sanitize(R(-500, -300, 3000, 2000), single);
        Check("larger than work: shrunk and inside", s.Left == 0 && s.Top == 0 && s.Width == 1536 && s.Height == 864, Text(s));

        // two monitors
        r = R(3700, 1050, 4900, 1850);
        s = WindowPlacementMath.Sanitize(r, dual);
        Check("two monitors: moved onto the right one", s.Left >= 1920 && s.Right <= 3840 && s.Top >= 0 && s.Bottom <= 1040 && WindowPlacementMath.IsComfortablyVisible(s, dual), Text(s));
        s = WindowPlacementMath.Sanitize(R(5200, 100, 6400, 900), dual);
        Check("two monitors: a monitor that is gone -> nearest one", s.Left >= 1920 && s.Right <= 3840 && WindowPlacementMath.IsComfortablyVisible(s, dual), Text(s));
        r = R(2000, 100, 3200, 900);
        Check("two monitors: a fine window is left alone", WindowPlacementMath.Sanitize(r, dual).Equals(r));
        r = R(1500, 100, 2700, 900);
        Check("two monitors: spanning both is fine", WindowPlacementMath.Sanitize(r, dual).Equals(r));

        // invalid rectangles
        foreach (var bad in new[] { R(0, 0, 0, 0), R(100, 100, 50, 50), R(100, 100, 100, 900), R(30000, 30000, 31200, 30800) })
        {
            s = WindowPlacementMath.Sanitize(bad, single);
            Check("invalid " + Text(bad), s.Width == 1200 && s.Height == 800 && WindowPlacementMath.IsComfortablyVisible(s, single) && s.Left == 168, Text(s));
        }

        // thresholds: 24 px of the caption strip, 160 px wide (or half the window), at least 200x150
        Check("24 px of caption visible is enough", WindowPlacementMath.IsComfortablyVisible(R(100, 864 - 24, 1300, 1700), single));
        Check("23 px of caption visible is not", !WindowPlacementMath.IsComfortablyVisible(R(100, 864 - 23, 1300, 1700), single));
        Check("160 px wide is enough", WindowPlacementMath.IsComfortablyVisible(R(-1040, 100, 160, 900), single));
        Check("159 px wide is not", !WindowPlacementMath.IsComfortablyVisible(R(-1041, 100, 159, 900), single));
        Check("a small window needs only half its width", WindowPlacementMath.IsComfortablyVisible(R(-100, 100, 120, 400), single));
        Check("too small to use", !WindowPlacementMath.IsComfortablyVisible(R(100, 100, 290, 240), single));
        s = WindowPlacementMath.Sanitize(R(-20, 100, 180, 240), single);
        Check("tiny window gets a usable size", s.Width >= 480 && s.Height >= 360 && WindowPlacementMath.IsComfortablyVisible(s, single), Text(s));

        // C: a maximised window's rectangle starts at minus its frame (8 / 10 / 12 / 16 px at 100 / 125 / 150 / 200 %) and is as wide and high as the monitor
        foreach (var frame in new[] { 8, 10, 12, 16 })
        {
            var zoomed = R(-frame, -frame, 1536 + frame, 864 + frame);
            Check($"maximised at frame {frame}: comfortable", WindowPlacementMath.IsComfortablyVisible(zoomed, single), Text(zoomed));
            Check($"maximised at frame {frame}: left alone by Sanitize", WindowPlacementMath.Sanitize(zoomed, single).Equals(zoomed), Text(zoomed));
            var zoomedDual = R(1920 - frame, -frame, 3840 + frame, 1040 + frame);
            Check($"maximised on the second monitor at frame {frame}: comfortable", WindowPlacementMath.IsComfortablyVisible(zoomedDual, dual), Text(zoomedDual));
        }

        // ... but a window whose caption is really above the screen is not (the tolerance is the frame, not 20 px)
        Check("caption 17 px above the monitor is not comfortable", !WindowPlacementMath.IsComfortablyVisible(R(100, -17, 1300, 783), single));
        Check("caption 16 px above the monitor is comfortable", WindowPlacementMath.IsComfortablyVisible(R(100, -16, 1300, 784), single));
        Check("caption 60 px above the monitor is not comfortable", !WindowPlacementMath.IsComfortablyVisible(R(100, -60, 1300, 740), single));

        // with the taskbar on top the work area starts at 48: the monitor's top edge does not count as the caption then
        Check("top taskbar: a window at the top of the monitor under the bar is not comfortable", !WindowPlacementMath.IsComfortablyVisible(R(100, 0, 1300, 800), topTaskbar));
        Check("top taskbar: a window right below the bar is comfortable", WindowPlacementMath.IsComfortablyVisible(R(100, 48, 1300, 848), topTaskbar));

        // results are always comfortable and stable
        var samples = new[] { R(100, 885, 1300, 1685), R(-30000, -30000, -27600, -28500), R(3700, 1050, 4900, 1850), R(-500, -300, 3000, 2000), R(0, 0, 0, 0), R(1900, 1030, 3100, 1830) };
        foreach (var sample in samples)
        {
            foreach (var set in new[] { single, dual, topTaskbar })
            {
                var once = WindowPlacementMath.Sanitize(sample, set);
                Check("comfortable result for " + Text(sample), WindowPlacementMath.IsComfortablyVisible(once, set), Text(once));
                Check("sanitising twice changes nothing for " + Text(sample), WindowPlacementMath.Sanitize(once, set).Equals(once), Text(once));
            }
        }

        // WINDOWPLACEMENT coordinates: workspace <-> screen (the taskbar on top shifts the work area)
        var ws = R(10, 10, 1210, 810);
        var screen = WindowPlacementMath.WorkspaceToScreen(ws, topTaskbar);
        Check("workspace -> screen with a top taskbar", screen.Left == 10 && screen.Top == 58 && screen.Bottom == 858, Text(screen));
        Check("screen -> workspace is the inverse", WindowPlacementMath.ScreenToWorkspace(screen, topTaskbar).Equals(ws));
        Check("no offset with a bottom taskbar", WindowPlacementMath.WorkspaceToScreen(ws, single).Equals(ws));

        // the real monitors (whatever this machine has): primary first, work area inside the monitor
        var real = WindowPlacementMath.GetMonitors();
        Check("real monitors: at least one", real.Count >= 1);
        Check("real monitors: work inside monitor", real.All(m => m.Work.Left >= m.Monitor.Left && m.Work.Right <= m.Monitor.Right && m.Work.Top >= m.Monitor.Top && m.Work.Bottom <= m.Monitor.Bottom));
        Check("real monitors: centred default is comfortable", WindowPlacementMath.IsComfortablyVisible(WindowPlacementMath.Centered(real), real));

        Console.WriteLine($"{_checks - _failures}/{_checks} checks passed");
        return _failures == 0 ? 0 : 1;
    }

    private static MonitorBounds Mon(int l, int t, int r, int b, int wl, int wt, int wr, int wb) =>
        new(R(l, t, r, b), R(wl, wt, wr, wb));

    private static RECT R(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };

    private static string Text(RECT r) => $"[{r.Left},{r.Top},{r.Right},{r.Bottom}] {r.Width}x{r.Height}";

    private static void Check(string name, bool ok, string? detail = null)
    {
        _checks++;
        if (!ok)
        {
            _failures++;
            Console.WriteLine("FAIL " + name + (detail is null ? "" : " -> " + detail));
        }
    }
}
