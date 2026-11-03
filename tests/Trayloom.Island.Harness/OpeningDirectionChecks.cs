// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Media;
using Trayloom.Core.Native;
using Trayloom.Features.Island;

namespace Trayloom.Island.Harness;

/// <summary>
/// Pure checks of how the island opens when it rests on the bottom edge: the mirrored notch silhouette, the open page's zoom origin, and that
/// a page at the largest height the island lets it have (with the quick-button row on its far side) stays inside the work area for both edges.
/// No window, no screen, no input.
/// </summary>
internal static class OpeningDirectionChecks
{
    private static Action<bool, string> _check = (_, _) => { };

    public static void Run(Action<bool, string> check)
    {
        _check = check;
        NotchFlip();
        LiveSurfaceFlip();
        ZoomOrigin();
        PagesFit();
    }

    // ---------------------------------------------------------------- notch silhouette

    private static IEnumerable<Point> Samples(double x, double y, double w, double h)
    {
        // a grid across the body, its shoulders (14 units each side) and a margin around
        for (var px = x - 24; px <= x + w + 24; px += 3.5)
        {
            for (var py = y - 6; py <= y + h + 6; py += 1.5)
            {
                yield return new Point(px, py);
            }
        }
    }

    private static void NotchFlip()
    {
        foreach (var (x, y, w, h, open) in new[] { (100.0, 50.0, 560.0, 300.0, true), (100.0, 50.0, 170.0, 32.0, false), (40.0, 0.0, 109.0, 24.0, false) })
        {
            var top = IslandSurface.Build(x, y, w, h, capsule: false, open);
            var def = IslandSurface.Build(x, y, w, h, capsule: false, open, flip: false);
            var flipped = IslandSurface.Build(x, y, w, h, capsule: false, open, flip: true);
            var mirror = (2 * y) + h;
            var tag = $"notch {w}x{h}";

            // the default overload and an explicit "no flip" are the same drawing
            var same = true;
            var inside = 0;
            var mirrored = true;
            foreach (var p in Samples(x, y, w, h))
            {
                var a = top.FillContains(p);
                same &= a == def.FillContains(p);

                // every point of the flipped shape is the mirror image (around the body's horizontal axis) of the normal one
                mirrored &= a == flipped.FillContains(new Point(p.X, mirror - p.Y));
                inside += a ? 1 : 0;
            }

            _check(same, tag + ": flip defaults to false (top look unchanged)");
            _check(inside > 100, tag + ": the sample covers the shape");
            _check(mirrored, tag + ": flipped notch is the exact mirror of the top notch");

            // flat edge with the shoulders on the screen edge side, rounded corners on the other side
            const double shoulder = 1;
            _check(top.FillContains(new Point(x - shoulder, y + 0.5)) && !top.FillContains(new Point(x - shoulder, y + h - 0.5)), tag + ": top notch: shoulders on the top edge");
            _check(flipped.FillContains(new Point(x - shoulder, y + h - 0.5)) && !flipped.FillContains(new Point(x - shoulder, y + 0.5)), tag + ": bottom notch: shoulders on the bottom edge");
            _check(!flipped.FillContains(new Point(x + 0.5, y + 0.5)) && flipped.FillContains(new Point(x + 0.5, y + h - 0.5)), tag + ": bottom notch: rounded corners on the top, flat edge at the bottom");
            _check(Math.Abs(flipped.Bounds.Top - top.Bounds.Top) < 0.01 && Math.Abs(flipped.Bounds.Bottom - top.Bounds.Bottom) < 0.01, tag + ": same vertical extent");
        }
    }

    private static void LiveSurfaceFlip()
    {
        // a FrameworkElement needs an STA thread
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                LiveSurfaceFlipSta();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        _check(error is null, "live surface checks ran" + (error is null ? "" : ": " + error.Message));
    }

    private static void LiveSurfaceFlipSta()
    {
        // the live geometry the animation moves must match the static one for both directions
        foreach (var flip in new[] { false, true })
        {
            var surface = new IslandSurface { Capsule = false, Open = true, FlipVertical = flip };
            surface.Set(100, 50, 560, 300);
            var expected = IslandSurface.Build(100, 50, 560, 300, capsule: false, open: true, flip);
            var same = true;
            foreach (var p in Samples(100, 50, 560, 300))
            {
                same &= surface.CurrentGeometry.FillContains(p) == expected.FillContains(p);
            }

            _check(same, $"live notch geometry matches Build(flip: {flip})");

            // the same surface moves to another size and keeps the direction
            surface.Set(100, 80, 170, 32);
            var small = IslandSurface.Build(100, 80, 170, 32, capsule: false, open: true, flip);
            var sameSmall = true;
            foreach (var p in Samples(100, 80, 170, 32))
            {
                sameSmall &= surface.CurrentGeometry.FillContains(p) == small.FillContains(p);
            }

            _check(sameSmall, $"live notch geometry follows Set() with flip {flip}");
        }

        // a capsule is symmetric: the flag changes nothing about its rectangle
        var capsule = new IslandSurface { Capsule = true, FlipVertical = true };
        capsule.Set(10, 20, 108, 24);
        _check(capsule.CurrentGeometry is RectangleGeometry { Rect: { X: 10, Y: 20, Width: 108, Height: 24 } }, "capsule rectangle ignores the flip");
    }

    // ---------------------------------------------------------------- zoom origin

    private static void ZoomOrigin()
    {
        var top = IslandWindow.OpenZoomOrigin(500, 500, 560, opensUp: false);
        _check(top is { X: 0.5, Y: 0 }, "zoom origin: centred top island is (0.5, 0) as before");
        var bottom = IslandWindow.OpenZoomOrigin(500, 500, 560, opensUp: true);
        _check(bottom is { X: 0.5, Y: 1 }, "zoom origin: centred bottom island is (0.5, 1)");

        // the page was shifted right by the work-area clamp: the pill is left of its centre
        var shifted = IslandWindow.OpenZoomOrigin(100, 380, 560, opensUp: true);
        _check(Math.Abs(shifted.X - (0.5 + ((100.0 - 380) / 560))) < 1e-12 && shifted.Y == 1, "zoom origin: shifted page scales from the pill's centre");
        var clamped = IslandWindow.OpenZoomOrigin(0, 600, 560, opensUp: false);
        _check(clamped is { X: 0, Y: 0 }, "zoom origin: clamped to the page's left edge");
        var clampedRight = IslandWindow.OpenZoomOrigin(900, 100, 560, opensUp: false);
        _check(clampedRight is { X: 1, Y: 0 }, "zoom origin: clamped to the page's right edge");
        var degenerate = IslandWindow.OpenZoomOrigin(0, 0, 0, opensUp: true);
        _check(double.IsFinite(degenerate.X) && degenerate.Y == 1, "zoom origin: a zero-width page stays finite");
    }

    // ---------------------------------------------------------------- pages inside the work area

    private static IslandMonitor Mon(string id, (int L, int T, int R, int B) bounds, (int L, int T, int R, int B) work, double dpi) =>
        new(
            $@"\\?\DISPLAY#{id}#4&1234&0&UID1#{{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}}",
            id,
            @"\\.\DISPLAY1",
            id,
            true,
            false,
            new RECT { Left = bounds.L, Top = bounds.T, Right = bounds.R, Bottom = bounds.B },
            new RECT { Left = work.L, Top = work.T, Right = work.R, Bottom = work.B },
            dpi,
            0);

    private static void PagesFit()
    {
        var monitors = new[]
        {
            Mon("THISMACH", (0, 0, 1920, 1200), (0, 0, 1920, 1140), 1.25),
            Mon("EXTERN", (1920, 0, 3520, 900), (1920, 0, 3520, 852), 1.0),
            Mon("SHORT", (0, 0, 1366, 768), (0, 0, 1366, 720), 1.25),
            Mon("BIG4K", (0, 0, 3840, 2160), (0, 0, 3840, 2112), 1.5),
            Mon("PORTRAIT", (0, 0, 1080, 1920), (0, 0, 1080, 1880), 1.0),
            Mon("TASKTOP", (0, 0, 1920, 1080), (0, 48, 1920, 1080), 1.0),
        };
        var failures = new List<string>();
        var count = 0;
        foreach (var m in monitors)
        {
            foreach (var zoom in new[] { 0.9, 1.1, 1.3 })
            {
                foreach (var edge in new[] { IslandEdge.Top, IslandEdge.Bottom })
                {
                    foreach (var x in new[] { 0.0, 0.5, 1.0, 0.2 })
                    {
                        foreach (var offsetFraction in new[] { 0.0, 0.5, 1.0 })
                        {
                            foreach (var nearGap in new[] { 0.0, 2.0, 22.0 })
                            {
                                foreach (var farRow in new[] { 0.0, IslandPlacement.SideGutter })
                                {
                                    var probe = IslandPlacement.Frame(m, new IslandPosition(x, edge, 0), zoom);
                                    var f = IslandPlacement.Frame(m, new IslandPosition(x, edge, probe.MaxOffset * offsetFraction), zoom);
                                    count++;
                                    var tag = $"{m.Name} zoom {zoom} {edge} x={x} offset={f.Inset:0.#} nearGap={nearGap} farRow={farRow}";

                                    // the page height OpenMetrics hands out, and a body of exactly that height at the widest page
                                    var cap = Math.Max(120, f.FarRoom(nearGap, farRow));
                                    var openW = Math.Min(600, f.UsableWidth - 24 - (2 * IslandPlacement.SideGutter));
                                    if (openW < 200)
                                    {
                                        continue;
                                    }

                                    var gutter = IslandPlacement.SideGutter;
                                    var c = IslandPlacement.ClampCentre(f.CentreX, openW, gutter, gutter, f.UsableWidth);
                                    var body = IslandPlacement.BodyRectPx(f, c, openW, cap, nearGap);
                                    var rowPx = (int)Math.Ceiling(farRow * f.Scale);
                                    var work = m.Work;

                                    // the far-side quick row is above a bottom island's page and below a top island's
                                    var withRow = new RECT
                                    {
                                        Left = body.Left - (int)Math.Ceiling(gutter * f.Scale),
                                        Right = body.Right + (int)Math.Ceiling(gutter * f.Scale),
                                        Top = edge == IslandEdge.Bottom ? body.Top - rowPx : body.Top,
                                        Bottom = edge == IslandEdge.Bottom ? body.Bottom : body.Bottom + rowPx,
                                    };
                                    if (withRow.Left < work.Left - 1 || withRow.Right > work.Right + 1 || withRow.Top < work.Top - 1 || withRow.Bottom > work.Bottom + 1)
                                    {
                                        failures.Add($"{tag}: page + gutters {withRow.Left},{withRow.Top},{withRow.Right},{withRow.Bottom} leaves work {work.Left},{work.Top},{work.Right},{work.Bottom}");
                                    }

                                    // the free margin at the far end really is free (at least FarMargin units less the rounding of a pixel)
                                    var farEdge = edge == IslandEdge.Bottom ? withRow.Top - work.Top : work.Bottom - withRow.Bottom;
                                    if (farEdge < (IslandPlacement.FarMargin * f.Scale) - 2 && cap > 120)
                                    {
                                        failures.Add($"{tag}: only {farEdge} px left at the far end");
                                    }

                                    // the window the island uses (top: grows to hold the page, bottom: the whole work height) holds it
                                    var needH = edge == IslandEdge.Top ? Math.Min(f.UsableHeight, nearGap + f.Inset + cap + farRow + 14) : f.UsableHeight;
                                    var (wx, wy, ww, wh) = f.WindowRect(Math.Min(f.UsableWidth, 778), needH);
                                    if (withRow.Top < wy - 1 || withRow.Bottom > wy + wh + 1 || withRow.Left < wx - 1 || withRow.Right > wx + ww + 1)
                                    {
                                        failures.Add($"{tag}: window {wx},{wy},{ww},{wh} does not hold page + gutters");
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        _check(failures.Count == 0, $"largest page + far quick row inside the work area and the window over {count} placements" + (failures.Count == 0 ? "" : " -> " + string.Join(" | ", failures.Take(4))));
    }
}
