// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Features.Agents;

namespace Trayloom.Agents.Harness;

/// <summary>
/// The Claude Code pixel mascot: its frame data, clips, timing rules and cell sizing. Pure logic from <see cref="ClaudeMascotArt"/>; no window
/// is created.
/// </summary>
internal static partial class Program
{
    /// <summary>True when the two rows of text are the very same frame, which is how the art hands out a pose.</summary>
    private static bool Same(string[] a, string[] b) => ReferenceEquals(a, b);

    private static bool HasLaptop(string[] frame) => frame.Any(row => row.Contains('G') || row.Contains('W'));

    private static void Mascot()
    {
        var frames = ClaudeMascotArt.AllFrames().ToList();
        var front = ClaudeMascotArt.Front;
        var intro = ClaudeMascotArt.Intro;
        var loop = ClaudeMascotArt.TypingLoop;
        var outro = ClaudeMascotArt.Outro;
        var wink = ClaudeMascotArt.Wink;

        // The box: one size for every frame, so the layout never jumps.
        Check(ClaudeMascotArt.Columns == 17 && ClaudeMascotArt.Rows == 11, "mascot: the box is 17 columns by 11 rows");
        Check(frames.Count == 19, "mascot: the clips hold 19 distinct frames");
        Check(frames.All(f => f.Length == ClaudeMascotArt.Rows && f.All(r => r.Length == ClaudeMascotArt.Columns)), "mascot: every frame has the same 11 by 17 box");
        Check(frames.All(f => f.All(r => r.All(ch => ch == '.' || ClaudeMascotArt.Inks.Contains(ch)))), "mascot: every character is empty or one of O, e, D, G and W");
        Check(frames.All(f => f[ClaudeMascotArt.Rows - 1].Any(ch => ch != '.')), "mascot: every pose stands on the shared last row");
        Check(ClaudeMascotArt.BodyTop + ClaudeMascotArt.BodyRows == ClaudeMascotArt.Rows, "mascot: the standing body runs from its top row to the floor row");
        Check(front.Take(ClaudeMascotArt.BodyTop).All(r => r.All(ch => ch == '.')) && front[ClaudeMascotArt.BodyTop].Any(ch => ch != '.'), "mascot: the standing pose starts at the body top row");
        Check(frames.Count(f => f.Any(r => r.Contains('e'))) == frames.Count, "mascot: every frame has an eye");

        // The standing pose: a 9-cell wide body from the recording, two-cell arms, four legs, two eyes.
        var painted = string.Concat(front).Count(ch => ch != '.');
        Check(painted == 64 && string.Concat(front).Count(ch => ch == 'e') == 2, "mascot: the front pose paints 64 cells, 2 of them eyes");
        Check(front[4][8] == 'e' && front[4][13] == 'e', "mascot: front eyes at (4,8) and (4,13)");
        // The body is 8 cells wide on columns 7 to 14 and the arms reach to columns 5 and 16, so the mirror axis lies between columns 10 and 11.
        Check(front.All(r => Enumerable.Range(5, 12).All(c => r[c] == r[21 - c]) && r.Take(5).All(ch => ch == '.') && r[17 - 1] == r[5]), "mascot: the front pose is mirror-symmetric about its centre");
        Check(string.Concat(front).Count(ch => ch == 'D' || ch == 'G' || ch == 'W') == 0, "mascot: the front pose has no side shading and no laptop");
        Check(ClaudeMascotArt.Wave.Length == ClaudeMascotArt.Rows && !Same(ClaudeMascotArt.Wave, front), "mascot: the wave is its own frame of the same box");
        Check(string.Concat(ClaudeMascotArt.Wave).Count(ch => ch != '.') == painted, "mascot: the wave moves the arms and paints as many cells as the front pose");
        Check(ClaudeMascotArt.Wave[4].Contains("OOOe") && front[5].StartsWith(".....OOOOOOOOOOOO", StringComparison.Ordinal), "mascot: the wave lifts the arms one row above the front pose");

        // The laptop and the side view appear only where the story puts them.
        Check(front.All(r => r.All(ch => ch != 'G' && ch != 'W')) && ClaudeMascotArt.Wave.All(r => r.All(ch => ch != 'G' && ch != 'W')), "mascot: neither standing pose has a laptop");
        Check(loop.All(f => HasLaptop(f.Rows)), "mascot: every typing frame shows the laptop");
        Check(loop.All(f => f.Rows.Any(r => r.Contains('D'))), "mascot: every typing frame has the shaded side of the body");
        Check(loop.Select(f => f.Rows).Distinct().Count() == 4, "mascot: the typing loop uses four distinct poses");
        Check(!HasLaptop(wink[0].Rows) && wink.All(f => !HasLaptop(f.Rows)), "mascot: the idle wink has no laptop");
        Check(ClaudeMascotArt.LaptopAppearsSeconds == 0.6 && !intro.Take(5).Any(f => HasLaptop(f.Rows)) && HasLaptop(intro[5].Rows), "mascot: the laptop first shows after the front pose and the wink, at 0.6 s");
        Check(HasLaptop(outro[0].Rows) && HasLaptop(outro[1].Rows) && !HasLaptop(outro[2].Rows) && !HasLaptop(outro[3].Rows), "mascot: the outro puts the laptop away before the last two frames");

        // Clip durations add up.
        Check(new[] { intro, loop, outro, wink }.All(c => c.All(f => f.Milliseconds > 0)), "mascot: every frame lasts a positive number of milliseconds");
        Check(ClaudeMascotArt.TotalMilliseconds(intro) == 1268, "mascot: the intro lasts 1268 ms");
        Check(ClaudeMascotArt.TotalMilliseconds(loop) == 1501, "mascot: the typing loop lasts 1501 ms");
        Check(ClaudeMascotArt.TotalMilliseconds(outro) == 533, "mascot: the outro lasts 533 ms");
        Check(ClaudeMascotArt.TotalMilliseconds(wink) == 401, "mascot: the idle wink lasts 401 ms");
        Check(intro.Sum(f => f.Milliseconds) is >= 1200 and <= 1300, "mascot: the intro is about 1.2 s");
        Check(intro.Count == 12 && loop.Count == 16 && outro.Count == 4 && wink.Count == 5, "mascot: the clips have 12, 16, 4 and 5 steps");

        // The frame chosen at a time inside the loop.
        var introSeconds = ClaudeMascotArt.TotalMilliseconds(intro) / 1000.0;
        var loopSeconds = ClaudeMascotArt.TotalMilliseconds(loop) / 1000.0;
        Check(Same(ClaudeMascotArt.FrameAt(loop, 0), loop[0].Rows) && Same(ClaudeMascotArt.FrameAt(loop, 0.099), loop[0].Rows), "mascot: the loop starts on its first frame and holds it for 100 ms");
        Check(Same(ClaudeMascotArt.FrameAt(loop, 0.101), loop[1].Rows) && Same(ClaudeMascotArt.FrameAt(loop, 0.166), loop[1].Rows), "mascot: the second loop frame runs from 100 to 167 ms");
        Check(Same(ClaudeMascotArt.FrameAt(loop, 0.168), loop[2].Rows), "mascot: the third loop frame starts at 167 ms");
        Check(Same(ClaudeMascotArt.FrameAt(loop, loopSeconds - 0.01), loop[^1].Rows) && Same(ClaudeMascotArt.FrameAt(loop, loopSeconds + 5), loop[^1].Rows), "mascot: a clip holds its last frame once it is over");
        Check(Same(ClaudeMascotArt.FrameAt(loop, -1), loop[0].Rows), "mascot: a negative time shows the first frame");
        Check(Same(ClaudeMascotArt.WorkingFrameAt(introSeconds + 0.05), loop[0].Rows) && Same(ClaudeMascotArt.WorkingFrameAt(introSeconds + 0.12), loop[1].Rows), "mascot: working shows the loop frames right after the intro");
        Check(Same(ClaudeMascotArt.WorkingFrameAt(introSeconds + loopSeconds + 0.05), loop[0].Rows) && Same(ClaudeMascotArt.WorkingFrameAt(introSeconds + 7 * loopSeconds + 0.12), loop[1].Rows), "mascot: the loop wraps and keeps its timing after many turns");
        Check(Enumerable.Range(0, 3000).All(i => loop.Any(f => Same(f.Rows, ClaudeMascotArt.WorkingFrameAt(introSeconds + i * 0.0137)))), "mascot: after the intro working shows only loop frames");

        // Intro, then loop, then outro, in that order.
        var introFrames = intro.Select(f => f.Rows).ToList();
        var phases = new List<string>();
        for (var i = 0; i < 400; i++)
        {
            var frame = ClaudeMascotArt.FrameAt(MascotMood.Working, i * 0.01, 0, false);
            var phase = introFrames.Any(f => Same(f, frame)) ? "intro" : loop.Any(f => Same(f.Rows, frame)) ? "loop" : "other";
            if (phases.Count == 0 || phases[^1] != phase)
            {
                phases.Add(phase);
            }
        }

        Check(phases.SequenceEqual(["intro", "loop"]), "mascot: working plays the intro once and then the loop, never back");
        Check(Same(ClaudeMascotArt.FrameAt(MascotMood.Working, 0, 3, false), front), "mascot: working starts on the front pose");
        Check(Same(ClaudeMascotArt.FrameAt(MascotMood.Working, 0.21, 3, false), intro[1].Rows) && Same(ClaudeMascotArt.FrameAt(MascotMood.Working, 0.61, 3, false), intro[5].Rows), "mascot: the wink follows the front pose and the laptop is grabbed at 0.6 s");
        Check(Same(ClaudeMascotArt.FrameAt(MascotMood.Working, introSeconds - 0.01, 3, false), intro[^1].Rows), "mascot: the intro ends on the side-view turn");
        Check(ClaudeMascotArt.NeedsOutro(0.5) == false && ClaudeMascotArt.NeedsOutro(0.6) && ClaudeMascotArt.NeedsOutro(30), "mascot: the outro is only played once the laptop is out");
        Check(Same(ClaudeMascotArt.OutroFrameAt(0)!, outro[0].Rows) && Same(ClaudeMascotArt.OutroFrameAt(0.099)!, outro[0].Rows) && Same(ClaudeMascotArt.OutroFrameAt(0.101)!, outro[1].Rows), "mascot: the outro starts by picking the laptop up");
        Check(Same(ClaudeMascotArt.OutroFrameAt(0.52)!, outro[3].Rows) && ClaudeMascotArt.OutroFrameAt(0.534) is null && ClaudeMascotArt.OutroFrameAt(3) is null, "mascot: the outro ends on the settled pose and then hands over");
        Check(Same(ClaudeMascotArt.FrameAt(MascotMood.Idle, 2.5, 0, false), front), "mascot: between winks an idle mascot stands in the front pose");

        // Waving and the idle wink.
        Check(Same(ClaudeMascotArt.FrameAt(MascotMood.Waiting, 0.5, 0, false), ClaudeMascotArt.Wave) && Same(ClaudeMascotArt.FrameAt(MascotMood.Waiting, 0.1, 0, false), front), "mascot: waiting waves every 0.4 s");
        Check(Enumerable.Range(0, 400).All(i => ClaudeMascotArt.FrameAt(MascotMood.Waiting, i * 0.05, 3, false) is var w && (Same(w, front) || Same(w, ClaudeMascotArt.Wave))), "mascot: waiting only ever shows the front pose and the wave");
        Check(Enumerable.Range(0, 400).All(i => ClaudeMascotArt.FrameAt(MascotMood.Idle, i * 0.05, 3, false) is var w && (Same(w, front) || wink.Any(f => Same(f.Rows, w)))), "mascot: idle shows the front pose and, now and then, the wink");
        // Seed 0 winks every 4.0 s for 401 ms, never in the first second.
        Check(wink.Any(f => Same(f.Rows, ClaudeMascotArt.FrameAt(MascotMood.Idle, 4.1, 0, false))) && Same(ClaudeMascotArt.FrameAt(MascotMood.Idle, 4.5, 0, false), front), "mascot: the idle wink plays at the period and ends before 4.5 s");
        Check(Same(ClaudeMascotArt.FrameAt(MascotMood.Idle, 0.1, 0, false), front) && Same(ClaudeMascotArt.FrameAt(MascotMood.Idle, 0.95, 0, false), front), "mascot: no wink in the first second");
        Check(wink.Any(f => Same(f.Rows, ClaudeMascotArt.FrameAt(MascotMood.Idle, 9.1, 500, false))) && Same(ClaudeMascotArt.FrameAt(MascotMood.Idle, 9.1, 0, false), front), "mascot: the seed sets the wink period (4.5 s for seed 500)");
        Check(wink.Any(f => f.Rows.Any(r => r.Contains("ee"))), "mascot: the wink has a dash for an eye");

        // Reduced motion: the static typing pose for working, the front pose otherwise (arms up while waiting so the state still shows).
        Check(Enumerable.Range(0, 400).All(i => Same(ClaudeMascotArt.FrameAt(MascotMood.Working, i * 0.05, 3, true), ClaudeMascotArt.TypingStill)), "mascot: reduced motion working is the static typing pose");
        Check(Enumerable.Range(0, 400).All(i => Same(ClaudeMascotArt.FrameAt(MascotMood.Idle, i * 0.05, 3, true), front)), "mascot: reduced motion idle is the front pose");
        Check(Enumerable.Range(0, 400).All(i => Same(ClaudeMascotArt.FrameAt(MascotMood.Waiting, i * 0.05, 3, true), ClaudeMascotArt.Wave)), "mascot: reduced motion waiting is the arms-up pose, still");
        Check(loop.Any(f => Same(f.Rows, ClaudeMascotArt.TypingStill)), "mascot: the static typing pose is one of the loop frames");

        // Centring: the art sits in the middle of the footprint it shares with the classic glyph, on whole device pixels.
        Check(ClaudeMascotArt.CenterOffsetPixels(20, 10) == 5 && ClaudeMascotArt.CenterOffsetPixels(21, 10) == 5 && ClaudeMascotArt.CenterOffsetPixels(10, 10) == 0, "mascot: centring offset is half the spare pixels, rounded down");
        Check(ClaudeMascotArt.CenterOffsetPixels(18, 20) == -1 && ClaudeMascotArt.CenterOffsetPixels(17, 20) == -2, "mascot: art wider than its box overhangs evenly, to the left first");
        Check(ClaudeMascotArt.CenterOffsetPixels(26, 10 * 2) == 3 && ClaudeMascotArt.CenterOffsetPixels(18, ClaudeMascotArt.Columns) == 0, "mascot: centring at two pixels per cell and a 17-pixel art");
        // At 125 % x 1.1 the strip's 12 dip target is a 25-pixel box and 2-pixel cells: the 8-row body (16 px) is centred, rows 0 to 2 hang above it.
        Check(ClaudeMascotArt.CenterOffsetPixels(25, ClaudeMascotArt.BodyRows * 2) - ClaudeMascotArt.BodyTop * 2 == -2, "mascot: the standing body is what centres vertically, not the whole box");

        Check(ClaudeMascotArt.CellPixels(12, 1.375) == 2 && ClaudeMascotArt.CellPixels(10, 1.375) == 1 && ClaudeMascotArt.CellPixels(8, 1.375) == 1, "mascot: cell pixels at 125% x 1.1");
        Check(ClaudeMascotArt.CellPixels(12, 1.1) == 1 && ClaudeMascotArt.CellPixels(12, 2.2) == 3 && ClaudeMascotArt.CellPixels(12, 1.65) == 2, "mascot: cell pixels at other scales");
        Check(ClaudeMascotArt.CellPixels(1, 1) == 1, "mascot: a cell is never smaller than one pixel");

        var row5 = ClaudeMascotArt.Runs(front).Where(r => r.Row == 5).ToList();
        Check(row5.Count == 1 && row5[0] == (5, 5, 12, 'O'), "mascot: front row 5 is one run of 12 for the body and both arms");
        var runs = ClaudeMascotArt.Runs(front).ToList();
        Check(runs.Sum(r => r.Len) == painted && runs.Where(r => r.Ink == 'e').Sum(r => r.Len) == 2, "mascot: runs cover every painted cell");
        Check(frames.All(f => ClaudeMascotArt.Runs(f).Sum(r => r.Len) == string.Concat(f).Count(ch => ch != '.')), "mascot: runs cover every painted cell of every frame");
    }
}
