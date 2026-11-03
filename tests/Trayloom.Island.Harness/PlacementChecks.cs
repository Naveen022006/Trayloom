// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Trayloom.Core;
using Trayloom.Core.Native;
using Trayloom.Features.Island;

namespace Trayloom.Island.Harness;

/// <summary>
/// Pure checks of the island's "place anywhere" math (<see cref="IslandPlacement"/>, <see cref="IslandPosition"/>, <see cref="IslandPlacementStore"/>) on
/// synthetic display layouts, plus the regression guard that the default position reproduces the window and pill rectangles the old
/// centred-on-the-monitor code produced. No window, no screen, no input. <c>-- --displays</c> additionally prints the real displays (read only).
/// </summary>
internal static class PlacementChecks
{
    private const double Zoom = 1.10;
    private static Action<bool, string> _check = (_, _) => { };

    public static void Run(Action<bool, string> check)
    {
        _check = check;
        if (Environment.GetCommandLineArgs().Contains("--displays"))
        {
            PrintDisplays();
        }

        ParseFormat();
        DefaultReproducesToday();
        foreach (var (name, layout) in Layouts())
        {
            Invariants(name, layout);
        }

        MaxOffsetShortDisplay();
        Drops();
        ResolveAndFind();
        AfterDrop();
        Store();
    }

    // ---------------------------------------------------------------- layouts

    private static IslandMonitor Mon(string id, bool primary, bool internalPanel, (int L, int T, int R, int B) bounds, (int L, int T, int R, int B) work, double dpi) =>
        new(
            $@"\\?\DISPLAY#{id}#4&1234&0&UID{id.Length}#{{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}}",
            id,
            $@"\\.\DISPLAY{(primary ? 1 : 5)}",
            id + " panel",
            primary,
            internalPanel,
            new RECT { Left = bounds.L, Top = bounds.T, Right = bounds.R, Bottom = bounds.B },
            new RECT { Left = work.L, Top = work.T, Right = work.R, Bottom = work.B },
            dpi,
            0);

    private static readonly (int, int, int, int) B1 = (0, 0, 1920, 1200);

    private static IEnumerable<(string, IslandMonitor[])> Layouts()
    {
        var main = Mon("AUOD7A7", true, true, B1, (0, 0, 1920, 1140), 1.25);
        var side = Mon("STD0110", false, false, (1920, 0, 3520, 900), (1920, 0, 3520, 852), 1.0);
        yield return ("L1 this machine", [main, side]);
        yield return ("L2 second left 2560x1440@1.5", [main, Mon("DEL4001", false, false, (-2560, 0, 0, 1440), (-2560, 0, 0, 1400), 1.5)]);
        yield return ("L3 second above", [main, Mon("DEL4002", false, false, (0, -1080, 1920, 0), (0, -1080, 1920, -40), 1.0)]);
        yield return ("L4a taskbar left", [Mon("AUOD7A7", true, true, B1, (48, 0, 1920, 1200), 1.25), side]);
        yield return ("L4b taskbar top", [Mon("AUOD7A7", true, true, B1, (0, 48, 1920, 1200), 1.25), side]);
        yield return ("L5 portrait right", [main, Mon("PRT1920", false, false, (1920, 0, 3000, 1920), (1920, 0, 3000, 1880), 1.0)]);
        yield return ("L6 short 1366x768@1.25", [Mon("LAP1366", true, true, (0, 0, 1366, 768), (0, 0, 1366, 720), 1.25)]);
    }

    private static IslandMonitor[] L1() => Layouts().First().Item2;

    // ---------------------------------------------------------------- parse / format

    private static void ParseFormat()
    {
        _check(IslandPosition.Default.Format() == "0.5,top,0", "position: the default formats as the registered default text");
        _check(IslandPosition.Parse("0.5,top,0") == IslandPosition.Default, "position: parses the default");
        _check(IslandPosition.Parse(null) == IslandPosition.Default && IslandPosition.Parse("") == IslandPosition.Default, "position: nothing is the default");

        foreach (var bad in new[] { "abc", "0.5,left,0", "NaN,top,0", "0.5,top,Infinity", "0.5,top", "1,2", "0.5,top,0,1", "x,y,z", "{}" })
        {
            _check(IslandPosition.Parse(bad) == IslandPosition.Default, $"position: garbage '{bad}' is the default");
        }

        _check(IslandPosition.Parse("2,top,5000") == new IslandPosition(1, IslandEdge.Top, 2000), "position: X and offset are clamped high");
        _check(IslandPosition.Parse("-1,bottom,-5") == new IslandPosition(0, IslandEdge.Bottom, 0), "position: X and offset are clamped low");
        _check(IslandPosition.Parse(" 0.25 , BOTTOM , 12 ") == new IslandPosition(0.25, IslandEdge.Bottom, 12), "position: spaces and case are tolerated");

        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var p = new IslandPosition(0.3125, IslandEdge.Bottom, 160.5);
            _check(p.Format() == "0.3125,bottom,160.5", "position: formatting ignores the current culture");
            _check(IslandPosition.Parse(p.Format()) == p, "position: round trip under a comma-decimal culture");
        }
        finally
        {
            CultureInfo.CurrentCulture = old;
        }

        foreach (var p in new[] { IslandPosition.Default, new(0, IslandEdge.Top, 0), new(1, IslandEdge.Bottom, 0), new(0.3, IslandEdge.Top, 160), new(0.9876, IslandEdge.Bottom, 33.25) })
        {
            _check(IslandPosition.Parse(p.Format()) == p, $"position: round trip {p.Format()}");
        }

        _check(new IslandPosition(0.5, IslandEdge.Top, 0.4).Attached && !new IslandPosition(0.5, IslandEdge.Top, 0.5).Attached, "position: attached below half a unit");
    }

    // ---------------------------------------------------------------- regression: default == today

    /// <summary>The window rectangle the code before this feature computed (ApplyWindow: centred on the monitor bounds, top at the bounds' top).</summary>
    private static (int X, int Y, int W, int H) TodayWindow(RECT bounds, double scale, double envW, double envH)
    {
        var pw = (int)Math.Ceiling(envW * scale);
        var ph = (int)Math.Ceiling(envH * scale);
        var x = (int)Math.Round(bounds.Left + ((double)(bounds.Width - pw) / 2));
        var y = (int)Math.Round((double)bounds.Top);
        return (x, y, pw, ph);
    }

    /// <summary>The pill rectangle PublishPill published: centred on the bounds, SurfaceTop below the top.</summary>
    private static (int L, int T, int R, int B) TodayPill(RECT bounds, double scale, double w, double h, double surfaceTop)
    {
        var wPx = w * scale;
        var hPx = h * scale;
        var left = bounds.Left + ((bounds.Width - wPx) / 2);
        var top = bounds.Top + (surfaceTop * scale);
        return ((int)left, (int)top, (int)Math.Ceiling(left + wPx), (int)Math.Ceiling(top + hPx));
    }

    private static void DefaultReproducesToday()
    {
        var ms = L1();
        foreach (var m in ms)
        {
            // a bottom taskbar: the work area differs from the bounds only at the bottom, so x/y/w must match to the pixel
            var windowOk = true;
            var pillOk = true;
            string? firstBad = null;
            foreach (var zoom in new[] { 0.9, 1.0, 1.10, 1.2, 1.3 })
            {
                var f = IslandPlacement.Frame(m, IslandPosition.Default, zoom);
                var scale = m.Dpi * zoom;
                for (var envW = 300.0; envW <= 900; envW += 0.75)
                {
                    foreach (var envH in new[] { 460.0, 477.3, 600 })
                    {
                        if (f.WindowRect(envW, envH) != TodayWindow(m.Bounds, scale, envW, envH))
                        {
                            windowOk = false;
                            firstBad ??= $"{m.Name} zoom {zoom} env {envW}x{envH}: {f.WindowRect(envW, envH)} vs {TodayWindow(m.Bounds, scale, envW, envH)}";
                        }
                    }
                }

                foreach (var gap in new[] { 0.0, 2, 12 })
                {
                    for (var w = 40.0; w <= 640; w += 1.25)
                    {
                        var r = IslandPlacement.PillRectPx(f, w, 24, gap);
                        if ((r.Left, r.Top, r.Right, r.Bottom) != TodayPill(m.Bounds, scale, w, 24, gap))
                        {
                            pillOk = false;
                            firstBad ??= $"{m.Name} zoom {zoom} pill w {w} gap {gap}: {(r.Left, r.Top, r.Right, r.Bottom)} vs {TodayPill(m.Bounds, scale, w, 24, gap)}";
                        }
                    }
                }
            }

            _check(windowOk, $"default == today: window x/y/w/h on {m.Name} ({firstBad})");
            _check(pillOk, $"default == today: pill rect on {m.Name} ({firstBad})");
        }

        // the concrete numbers on this machine (envelope 600 + 6 + 144 + 28 = 778 units, 460 high, zoom 1.10)
        var main = IslandPlacement.Frame(ms[0], IslandPosition.Default, Zoom).WindowRect(778, 460);
        _check(main == (425, 0, 1070, 633), $"default == today: internal panel window is 425,0,1070,633 (was {main})");
        var side = IslandPlacement.Frame(ms[1], IslandPosition.Default, Zoom).WindowRect(778, 460);
        _check(side == (2292, 0, 856, 507), $"default == today: external window is 2292,0,856,507 (was {side})");
    }

    // ---------------------------------------------------------------- invariants on every layout

    private static bool Inside(RECT r, RECT work, int slack = 1) =>
        r.Left >= work.Left - slack && r.Top >= work.Top - slack && r.Right <= work.Right + slack && r.Bottom <= work.Bottom + slack;

    private static void Invariants(string layoutName, IslandMonitor[] layout)
    {
        foreach (var m in layout)
        {
            var failures = new List<string>();
            void Expect(bool ok, string what)
            {
                if (!ok && failures.Count < 4)
                {
                    failures.Add(what);
                }
            }

            var count = 0;
            foreach (var edge in new[] { IslandEdge.Top, IslandEdge.Bottom })
            {
                foreach (var x in new[] { 0.0, 0.5, 1.0, 0.13, 0.87 })
                {
                    foreach (var offsetFraction in new[] { 0.0, 0.3, 1.0 })
                    {
                        var probe = IslandPlacement.Frame(m, new IslandPosition(x, edge, 0), Zoom);
                        var offset = probe.MaxOffset * offsetFraction;
                        var f = IslandPlacement.Frame(m, new IslandPosition(x, edge, offset), Zoom);
                        var work = m.Work;
                        var tag = $"{m.Name} {edge} x={x} off={offset:0.#}";
                        const double nearGap = 2;
                        count++;

                        Expect(f.Inset <= f.MaxOffset + 1e-9, tag + ": inset within MaxOffset");
                        Expect(f.OpensUp == (edge == IslandEdge.Bottom), tag + ": OpensUp follows the edge");

                        // closed bodies: bare capsule up to a wide strip
                        var envW = Math.Min(f.UsableWidth, 778);
                        var (wx, wy, ww, wh) = f.WindowRect(envW, edge == IslandEdge.Bottom ? f.UsableHeight : Math.Min(f.UsableHeight, nearGap + f.Inset + 460));
                        var window = new RECT { Left = wx, Top = wy, Right = wx + ww, Bottom = wy + wh };
                        Expect(Inside(window, work, 0), tag + $": window {wx},{wy},{ww},{wh} inside the work area");
                        Expect(edge == IslandEdge.Top ? wy == work.Top : wy + wh == work.Bottom, tag + ": window rests on the edge");

                        foreach (var w in new[] { 60.0, 108, 180, 340, Math.Min(f.UsableWidth - 24, 600) })
                        {
                            var pill = IslandPlacement.PillRectPx(f, w, 24, nearGap);
                            Expect(Inside(pill, work), tag + $": pill w={w:0.#} {pill.Left},{pill.Top},{pill.Right},{pill.Bottom} inside work");
                            Expect(Inside(pill, window), tag + $": window contains pill w={w:0.#}");
                            if (f.Inset == 0 && x == 0.5 && Math.Abs(((pill.Left + pill.Right) / 2.0) - (work.Left + (work.Width / 2.0))) > 1.5)
                            {
                                Expect(false, tag + ": centred pill is centred");
                            }

                            if (edge == IslandEdge.Top)
                            {
                                Expect(pill.Top == work.Top + (int)Math.Floor((nearGap + f.Inset) * f.Scale), tag + ": top pill sits nearGap + inset below the work top");
                            }
                            else
                            {
                                Expect(Math.Abs(work.Bottom - pill.Bottom - ((nearGap + f.Inset) * f.Scale)) <= 1.01, tag + ": bottom pill sits nearGap + inset above the work bottom");
                            }
                        }

                        // open bodies with their quick-button gutters
                        foreach (var openW in new[] { 440.0, 520, 600 })
                        {
                            var gutter = openW + (2 * SideGutter) + (2 * IslandPlacement.EdgeMargin) <= f.UsableWidth ? SideGutter : 0;
                            if (openW > f.UsableWidth - 24)
                            {
                                continue;
                            }

                            var c = IslandPlacement.ClampCentre(f.CentreX, openW, gutter, gutter, f.UsableWidth);
                            var openH = Math.Min(400, f.FarRoom(nearGap, SideGutter));
                            if (openH < 60)
                            {
                                continue;
                            }

                            var body = IslandPlacement.BodyRectPx(f, c, openW, openH, nearGap);
                            var withGutters = new RECT
                            {
                                Left = body.Left - (int)Math.Ceiling(gutter * f.Scale),
                                Right = body.Right + (int)Math.Ceiling(gutter * f.Scale),
                                Top = body.Top,
                                Bottom = body.Bottom,
                            };
                            Expect(Inside(withGutters, work, 1), tag + $": open {openW} (+gutters {gutter}) inside work: {withGutters.Left},{withGutters.Top},{withGutters.Right},{withGutters.Bottom}");

                            // top island: the window grows down to hold the page and the bottom quick row; bottom island: full height
                            var needH = edge == IslandEdge.Top ? Math.Min(f.UsableHeight, nearGap + f.Inset + openH + SideGutter + 10) : f.UsableHeight;
                            var (ox, oy, ow, oh) = f.WindowRect(envW, needH);
                            var openWindow = new RECT { Left = ox, Top = oy, Right = ox + ow, Bottom = oy + oh };
                            Expect(Inside(openWindow, work, 0), tag + ": open window inside work");
                            Expect(Inside(withGutters, openWindow, 1), tag + $": open window contains the page {openW} with gutters");
                        }
                    }
                }
            }

            _check(failures.Count == 0, $"{layoutName}: {m.Name} pill/open/window invariants over {count} placements" + (failures.Count == 0 ? "" : " -> " + string.Join(" | ", failures)));
        }
    }

    private const double SideGutter = IslandPlacement.SideGutter;

    private static void MaxOffsetShortDisplay()
    {
        var m = Layouts().First(l => l.Item1.StartsWith("L6", StringComparison.Ordinal)).Item2[0];
        var f = IslandPlacement.Frame(m, new IslandPosition(0.5, IslandEdge.Top, 1000), Zoom);
        var usableH = 720 / (1.25 * Zoom);
        var expected = Math.Min((usableH / 2) - 12, usableH - 360 - 48 - 72);
        _check(Math.Abs(f.UsableHeight - usableH) < 1e-9, "L6: usable height in units");
        _check(Math.Abs(f.MaxOffset - expected) < 1e-9 && Math.Abs(f.Inset - expected) < 1e-9, $"L6: offset 1000 clamps to MaxOffset {expected:0.##} (was {f.Inset:0.##})");
        _check(f.FarRoom(0, SideGutter) >= 360 - 1e-9, $"L6: a page still has 360 units at the largest offset (was {f.FarRoom(0, SideGutter):0.##})");

        var tiny = Mon("TINY", true, false, (0, 0, 800, 480), (0, 0, 800, 440), 1.0);
        var t = IslandPlacement.Frame(tiny, new IslandPosition(0.5, IslandEdge.Bottom, 500), Zoom);
        _check(t.MaxOffset >= 0 && t.Inset == t.MaxOffset, "tiny display: MaxOffset never negative");
        _check(Math.Abs(IslandPlacement.MaxOffsetFor(1000) - 488) < 1e-9, "MaxOffsetFor: tall display is limited to half the height minus the margin");
    }

    // ---------------------------------------------------------------- snapping

    private static void Drops()
    {
        var ms = L1();
        var (main, side) = (ms[0], ms[1]);
        var s = main.Dpi * Zoom;

        // click jitter near the top centre snaps back to the default
        var (m1, p1) = IslandPlacement.FromDrop(ms, 960 + 9, ((2 + 12) * s) + 9, 108, 24, Zoom);
        _check(m1 == main && p1 == IslandPosition.Default, $"drop: jitter at the top centre snaps to the default (was {p1.Format()})");

        // left / right
        var (_, left) = IslandPlacement.FromDrop(ms, 90, 14 * s, 108, 24, Zoom);
        _check(left.X == 0 && left.Edge == IslandEdge.Top && left.Offset == 0, $"drop: near the left edge snaps to X=0, attached (was {left.Format()})");
        var (_, right) = IslandPlacement.FromDrop(ms, 1920 - 90, 14 * s, 108, 24, Zoom);
        _check(right.X == 1 && right.Edge == IslandEdge.Top && right.Offset == 0, $"drop: near the right edge snaps to X=1, attached (was {right.Format()})");

        // attach to the bottom of the work area (above the taskbar)
        var (_, bottom) = IslandPlacement.FromDrop(ms, 960, 1140 - (30 * s), 108, 24, Zoom);
        _check(bottom == new IslandPosition(0.5, IslandEdge.Bottom, 0), $"drop: just above the work bottom attaches at the bottom centre (was {bottom.Format()})");

        // edge choice by half
        var (_, upper) = IslandPlacement.FromDrop(ms, 500, 560, 108, 24, Zoom);
        var (_, lower) = IslandPlacement.FromDrop(ms, 500, 580, 108, 24, Zoom);
        _check(upper.Edge == IslandEdge.Top && lower.Edge == IslandEdge.Bottom, "drop: the centre in the upper half chooses Top, the lower half Bottom");

        // floating: offset is the distance from the edge to the body's near side, X a fraction of the work width
        var (_, floating) = IslandPlacement.FromDrop(ms, 500, 300, 108, 24, Zoom);
        var expectedOffset = Math.Round((300 / s) - 12);
        var expectedX = Math.Round((500 / s) / (1920 / s), 4);
        _check(floating.Edge == IslandEdge.Top && floating.Offset == expectedOffset && Math.Abs(floating.X - expectedX) < 1e-9, $"drop: floating placement (was {floating.Format()}, expected {expectedX},top,{expectedOffset})");
        _check(!floating.Attached, "drop: a floating placement is not attached");

        // on the secondary display: its own work area and scale (100 % here, the main one is 125 %)
        var s2 = side.Dpi * Zoom;
        var (m2, onSide) = IslandPlacement.FromDrop(ms, 2300, 300, 108, 24, Zoom);
        var sideOffset = Math.Round((300 / s2) - 12);
        var sideX = Math.Round(((2300 - 1920) / s2) / (1600 / s2), 4);
        _check(m2 == side && onSide.Edge == IslandEdge.Top && onSide.Offset == sideOffset && Math.Abs(onSide.X - sideX) < 1e-9, $"drop: secondary display uses its own scale (was {onSide.Format()}, expected {sideX},top,{sideOffset})");
        _check(sideOffset != floating.Offset, "drop: the same pixel distance is a different number of units on the two displays");

        // outside every display: the nearest work area
        var (m3, _) = IslandPlacement.FromDrop(ms, -400, 300, 108, 24, Zoom);
        _check(m3 == main, "drop: left of everything chooses the nearest display");
        var (m4, _) = IslandPlacement.FromDrop(ms, 5000, 100, 108, 24, Zoom);
        _check(m4 == side, "drop: right of everything chooses the nearest display");
        var (m5, p5) = IslandPlacement.FromDrop(ms, 960, 1190, 108, 24, Zoom);
        _check(m5 == main && p5.Edge == IslandEdge.Bottom && p5.Offset == 0, "drop: on the taskbar strip attaches to the work bottom of that display");

        // a short display limits the offset
        var l6 = Layouts().First(l => l.Item1.StartsWith("L6", StringComparison.Ordinal)).Item2;
        var max = IslandPlacement.MaxOffsetFor(720 / (1.25 * Zoom));
        var (_, clamped) = IslandPlacement.FromDrop(l6, 600, 300, 108, 24, Zoom);
        _check(clamped.Edge == IslandEdge.Top && Math.Abs(clamped.Offset - Math.Round(max)) <= 1 && clamped.Offset <= max, $"drop: L6 offset clamps to MaxOffset (was {clamped.Format()}, max {max:0.##})");
    }

    // ---------------------------------------------------------------- display choice

    private static void ResolveAndFind()
    {
        var ms = L1();
        var (main, side) = (ms[0], ms[1]);
        _check(IslandPlacement.Resolve("automatic", "", ms, 3000, 100) == main, "resolve: automatic is the main display");
        _check(IslandPlacement.Resolve("main", "", ms, 3000, 100) == main, "resolve: main is the main display");
        _check(IslandPlacement.Resolve("unknown-value", "", ms, 3000, 100) == main, "resolve: an unknown value is the main display");
        _check(IslandPlacement.Resolve("pointer", "", ms, 3000, 100) == side && IslandPlacement.Resolve("pointer", "", ms, 100, 100) == main, "resolve: pointer is the display under the cursor");
        _check(IslandPlacement.Resolve("all", "", ms, 3000, 100) == side, "resolve: all follows the cursor like pointer");
        _check(IslandPlacement.Resolve("pointer", "", ms, 3700, 100) == side && IslandPlacement.Resolve("pointer", "", ms, -50, 100) == main, "resolve: a cursor outside every display takes the nearest");
        _check(IslandPlacement.Resolve("specific", side.Key, ms, 0, 0) == side, "resolve: specific by key");
        _check(IslandPlacement.Resolve("specific", @"\\?\DISPLAY#GONE000#1&2&3#{guid}", ms, 0, 0) == main, "resolve: a specific display that is not connected falls back to the main display");
        _check(IslandPlacement.Resolve("specific", "", ms, 0, 0) == main, "resolve: specific with no id falls back to the main display");
        _check(IslandPlacement.Resolve("builtIn", "", ms, 0, 0) == main, "resolve: builtIn is the internal panel (here also the main display)");

        // internal panel on the secondary slot, an external display as the main one
        var ext = Mon("EXT0001", true, false, (0, 0, 2560, 1440), (0, 0, 2560, 1400), 1.0);
        var panel = Mon("PNL0002", false, true, (2560, 0, 4480, 1200), (2560, 0, 4480, 1160), 1.25);
        var docked = new[] { ext, panel };
        _check(IslandPlacement.Resolve("builtIn", "", docked, 0, 0) == panel, "resolve: builtIn picks the internal panel when it is not the main display");
        _check(IslandPlacement.Resolve("builtIn", "", [ext], 0, 0) == ext, "resolve: builtIn without an internal panel is the main display");
        _check(IslandPlacement.Resolve("automatic", "", docked, 0, 0) == ext, "resolve: automatic stays the main display (even when the internal panel is elsewhere)");

        // find: exact key, hardware id, GDI name
        _check(IslandPlacement.FindByKey(ms, side.Key) == side, "find: exact key");
        _check(IslandPlacement.FindByKey(ms, side.Key.ToLowerInvariant()) == side, "find: the key is compared without case");
        _check(IslandPlacement.FindByKey(ms, @"\\?\DISPLAY#STD0110#4&OLDPORT&0&UID999#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}") == side, "find: the hardware id still matches after the interface path changed");
        _check(IslandPlacement.FindByKey(ms, "STD0110") == side, "find: a bare hardware id");
        _check(IslandPlacement.FindByKey(ms, side.GdiName) == side, "find: the GDI name as the last resort");
        _check(IslandPlacement.FindByKey(ms, @"\\?\DISPLAY#ZZZ9999#1&2&3#{g}") is null && IslandPlacement.FindByKey(ms, "") is null, "find: nothing for an unknown display or an empty id");
    }

    private static void AfterDrop()
    {
        var ms = L1();
        var (main, side) = (ms[0], ms[1]);
        (string, string) After(string display, string id, IslandMonitor from, IslandMonitor to) => IslandPlacement.DisplayAfterDrop(display, id, from, to);

        _check(After("automatic", "", main, side) == ("specific", side.Key), "after drop: automatic moved to another display becomes specific");
        _check(After("main", "", main, side) == ("specific", side.Key), "after drop: main moved to another display becomes specific");
        _check(After("builtIn", "", main, side) == ("specific", side.Key), "after drop: builtIn moved to another display becomes specific");
        _check(After("automatic", "", main, main) == ("automatic", ""), "after drop: dropped on the same display changes nothing");
        _check(After("pointer", "", main, side) == ("pointer", ""), "after drop: pointer never changes");
        _check(After("all", "x", side, main) == ("all", "x"), "after drop: all never changes");
        _check(After("specific", main.Key, main, side) == ("specific", side.Key), "after drop: specific moves to the new display's key");
        _check(After("specific", side.Key, side, side) == ("specific", side.Key), "after drop: specific dropped where it was changes nothing");
        _check(After("specific", "GONE", main, main) == ("specific", "GONE"), "after drop: a missing specific display keeps its id while the island stays on the fallback display");
        _check(After("specific", "GONE", main, side) == ("specific", side.Key), "after drop: a missing specific display is replaced by the one it was dropped on");
    }

    // ---------------------------------------------------------------- store

    private sealed class FakeSettings : IAppSettings
    {
        private readonly Dictionary<string, object?> _values = [];
        private readonly Dictionary<string, object?> _defaults = [];

        public event Action<string>? Changed;

        public T Get<T>(string key) => Get(key, default(T)!);

        public T Get<T>(string key, T fallback) =>
            _values.TryGetValue(key, out var v) && v is T t ? t : _defaults.TryGetValue(key, out var d) && d is T td ? td : fallback;

        public void Set<T>(string key, T value)
        {
            _values[key] = value;
            Changed?.Invoke(key);
        }

        public bool IsSet(string key) => _values.ContainsKey(key);

        public void Reset(string key) => _values.Remove(key);

        public void Register<T>(string key, T defaultValue) => _defaults[key] = defaultValue;

        public void RegisterDefaults(IEnumerable<KeyValuePair<string, object?>> defaults)
        {
            foreach (var (k, v) in defaults)
            {
                _defaults[k] = v;
            }
        }

        public IFeatureSettings Scope(string featureId) => throw new NotSupportedException();
    }

    private static void Store()
    {
        var ms = L1();
        var (main, side) = (ms[0], ms[1]);
        var app = new FakeSettings();
        K.RegisterDefaults(app);

        _check(app.Get<string>(K.Position) == "0.5,top,0" && app.Get<string>(K.PositionsByDisplay) == "" && app.Get<string>(K.DisplayId) == "" && !app.Get<bool>(K.PositionLocked),
            "store: the new keys default to today's behaviour");
        _check(app.Get<string>(K.Display) == "automatic", "store: the display default stays automatic");
        _check(IslandPlacementStore.Load(app, main) == IslandPosition.Default && IslandPlacementStore.Load(app, side) == IslandPosition.Default, "store: nothing saved is the default");

        var pa = new IslandPosition(0.3, IslandEdge.Top, 160);
        IslandPlacementStore.Save(app, main, pa, null);
        _check(IslandPlacementStore.Load(app, main) == pa, "store: round trip for the display it was saved for");
        _check(IslandPlacementStore.Load(app, side) == pa, "store: another display without an entry falls back to notchPosition");
        _check(app.Get<string>(K.Position) == pa.Format() && app.Get<string>(K.Display) == "automatic", "store: a save without resolvedBefore leaves the display rule alone");

        var pb = new IslandPosition(1, IslandEdge.Bottom, 0);
        IslandPlacementStore.Save(app, side, pb, main);
        _check(IslandPlacementStore.Load(app, side) == pb && IslandPlacementStore.Load(app, main) == pa, "store: each display keeps its own position");
        _check(app.Get<string>(K.Display) == "specific" && app.Get<string>(K.DisplayId) == side.Key, "store: dropping on another display switches to specific");

        // identical interface path changed, same model: found by hardware id
        var moved = side with { Key = @"\\?\DISPLAY#STD0110#4&OTHER&0&UID1#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}" };
        _check(IslandPlacementStore.Load(app, moved) == pb, "store: the entry is found by hardware id when the interface path changed");

        // a re-save replaces, it does not add a duplicate
        IslandPlacementStore.Save(app, main, new IslandPosition(0.5, IslandEdge.Top, 0), null);
        _check(MapCount(app) == 2, "store: saving an existing display replaces its entry");

        // the cap: 16 entries, the oldest forgotten
        var app2 = new FakeSettings();
        K.RegisterDefaults(app2);
        for (var i = 0; i < 20; i++)
        {
            var m = Mon($"M{i:00}", false, false, B1, B1, 1.0) with { Key = $@"\\?\DISPLAY#MON{i:00}#KEY{i}#{{g}}" };
            IslandPlacementStore.Save(app2, m, new IslandPosition(i / 20.0, IslandEdge.Top, i), null);
        }

        _check(MapCount(app2) == 16, $"store: the map is capped at 16 entries (was {MapCount(app2)})");
        var newest = Mon("M19", false, false, B1, B1, 1.0) with { Key = @"\\?\DISPLAY#MON19#KEY19#{g}" };
        var oldest = Mon("M00", false, false, B1, B1, 1.0) with { Key = @"\\?\DISPLAY#MON00#KEY0#{g}" };
        _check(IslandPlacementStore.Load(app2, newest) == new IslandPosition(0.95, IslandEdge.Top, 19), "store: the newest entry survives the cap");
        _check(IslandPlacementStore.Load(app2, oldest) == IslandPosition.Parse(app2.Get<string>(K.Position)), "store: the oldest entry was dropped (falls back to notchPosition)");

        // garbage in the map is survivable
        app2.Set(K.PositionsByDisplay, "{not json");
        _check(IslandPlacementStore.Load(app2, newest) == IslandPosition.Parse(app2.Get<string>(K.Position)), "store: a broken map reads as empty");
        IslandPlacementStore.Save(app2, newest, pa, null);
        _check(MapCount(app2) == 1 && IslandPlacementStore.Load(app2, newest) == pa, "store: a save repairs a broken map");

        // reset
        IslandPlacementStore.Reset(app);
        _check(app.Get<string>(K.PositionsByDisplay) == "" && app.Get<string>(K.Position) == "0.5,top,0", "store: reset clears the map and the fallback");
        _check(app.Get<string>(K.Display) == "automatic", "store: reset turns specific back into automatic");
        _check(IslandPlacementStore.Load(app, main) == IslandPosition.Default, "store: after reset every display is at the default");
        app.Set(K.Display, "pointer");
        IslandPlacementStore.Reset(app);
        _check(app.Get<string>(K.Display) == "pointer", "store: reset leaves pointer alone");
    }

    private static int MapCount(IAppSettings app)
    {
        try
        {
            using var doc = JsonDocument.Parse(app.Get<string>(K.PositionsByDisplay));
            return doc.RootElement.EnumerateObject().Count();
        }
        catch (JsonException)
        {
            return -1;
        }
    }

    // ---------------------------------------------------------------- live displays (read only)

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(nint value);

    private static void PrintDisplays()
    {
        // the harness has no manifest: ask for per-monitor awareness (the app's own setting) so the numbers are the real ones
        var aware = SetProcessDpiAwarenessContext(-4);
        Console.WriteLine($"-- live displays (per-monitor v2 awareness set: {aware})");
        foreach (var m in IslandDisplays.Enumerate())
        {
            Console.WriteLine($"{(m.IsPrimary ? "*" : " ")} \"{m.Name}\" gdi={m.GdiName} internal={m.IsInternal} dpi={m.Dpi.ToString("0.##", CultureInfo.InvariantCulture)} ({(int)Math.Round(m.Dpi * 96)} dpi)");
            Console.WriteLine($"    key={m.Key}");
            Console.WriteLine($"    hardware={m.HardwareId} bounds={m.Bounds.Left},{m.Bounds.Top},{m.Bounds.Right},{m.Bounds.Bottom} ({m.Bounds.Width}x{m.Bounds.Height}) work={m.Work.Left},{m.Work.Top},{m.Work.Right},{m.Work.Bottom} ({m.Work.Width}x{m.Work.Height})");
            var f = IslandPlacement.Frame(m, IslandPosition.Default, 1.10);
            Console.WriteLine($"    frame@1.10: scale={f.Scale:0.####} usable={f.UsableWidth:0.#}x{f.UsableHeight:0.#} units maxOffset={f.MaxOffset:0.#} window(778x460)={f.WindowRect(778, 460)}");
        }
    }
}
