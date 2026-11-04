// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Features.Island;

namespace Trayloom.Island.Harness;

/// <summary>
/// Checks of the pure rules of moving within a song (<see cref="SeekRules"/>): when the timeline is draggable, what Apple Music's scrubber is
/// set to, how long the island's position outranks the media session and when the timeline lets go of the released position.
/// Synthetic numbers only; Apple Music is never touched.
/// </summary>
internal static class SeekChecks
{
    private static Action<bool, string> _check = (_, _) => { };

    public static void Run(Action<bool, string> check)
    {
        _check = check;
        Draggable();
        ScrubberUnits();
        ScrubberClamping();
        PositionOverride();
        Hold();
    }

    private static void Check(bool ok, string what) => _check(ok, "seek: " + what);

    private static void Draggable()
    {
        Check(SeekRules.CanSeek(true, false, 217, true), "Apple Music is draggable although its session says the position cannot be set");
        Check(!SeekRules.CanSeek(true, false, 217, false), "Apple Music is not draggable without a way to reach its scrubber");
        Check(!SeekRules.CanSeek(true, false, 0, true), "Apple Music is not draggable without a known length");
        Check(!SeekRules.CanSeek(false, false, 217, true), "another player follows its session: position not settable, not draggable");
        Check(SeekRules.CanSeek(false, true, 217, false), "another player whose session allows it is draggable");
        Check(!SeekRules.CanSeek(false, true, 0, false), "another player without a length is not draggable");
        Check(PlaybackService.IsAppleMusicId("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App") && !PlaybackService.IsAppleMusicId("Spotify.exe"), "the Apple Music application id is recognised");
    }

    private static void ScrubberUnits()
    {
        Check(SeekRules.RangeIsSeconds(217, 0, 217), "a range as long as the song is in seconds");
        Check(SeekRules.RangeIsSeconds(217, 0, 219.5), "a range within 3 s of the song's length is in seconds");
        Check(!SeekRules.RangeIsSeconds(217, 0, 100), "a range of 0..100 is not in seconds");
        Check(!SeekRules.RangeIsSeconds(217, 0, 1), "a range of 0..1 is not in seconds");
        Check(SeekRules.RangeIsSeconds(0, 0, 100), "without a known length seconds are assumed");
        Check(Math.Abs(SeekRules.ScrubberTarget(60, 217, 0, 217) - 60) < 1e-9, "seconds range: 60 s is 60");
        Check(Math.Abs(SeekRules.ScrubberTarget(60, 217, 10, 227) - 70) < 1e-9, "seconds range with an offset minimum is shifted");
        Check(Math.Abs(SeekRules.ScrubberTarget(108.5, 217, 0, 100) - 50) < 1e-9, "0..100: half of the song is 50");
        Check(Math.Abs(SeekRules.ScrubberTarget(54.25, 217, 0, 1) - 0.25) < 1e-9, "0..1: a quarter of the song is 0.25");
        Check(Math.Abs(SeekRules.ScrubberTarget(60, 0, 0, 100) - 60) < 1e-9, "unknown length: seconds");
    }

    private static void ScrubberClamping()
    {
        Check(Math.Abs(SeekRules.ScrubberTarget(-5, 217, 0, 217)) < 1e-9, "before the start is the start");
        Check(Math.Abs(SeekRules.ScrubberTarget(400, 217, 0, 217) - 216) < 1e-9, "past the end stops one second before it");
        Check(Math.Abs(SeekRules.ScrubberTarget(217, 217, 0, 217) - 216) < 1e-9, "the very end stops one second before it");
        Check(Math.Abs(SeekRules.ScrubberTarget(217, 200, 0, 100) - (100 - (100 / 200.0))) < 1e-9, "fraction range: the end stops one second's worth before it");
        Check(SeekRules.ScrubberTarget(5, 217, 0, 0.5) >= 0, "a tiny range never goes below its minimum");
        Check(SeekRules.ScrubberReached(61.5, 60, 217, 0, 217), "1.5 s away is reached");
        Check(!SeekRules.ScrubberReached(63, 60, 217, 0, 217), "3 s away is not reached");
        Check(SeekRules.ScrubberReached(0.51, 0.5, 200, 0, 100), "fraction range: 0.01 away is within 2 s");
        Check(!SeekRules.ScrubberReached(10, 50, 200, 0, 100), "fraction range: 40 units away is not reached");
    }

    private static void PositionOverride()
    {
        var at = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
        Check(SeekRules.OverrideHolds(at, at.AddSeconds(-3)), "a timeline from before the seek does not replace the island's position");
        Check(SeekRules.OverrideHolds(at, at), "a timeline stamped at the seek itself does not replace it");
        Check(!SeekRules.OverrideHolds(at, at.AddMilliseconds(1)), "a timeline updated after the seek wins");
        Check(SeekRules.OverrideHolds(at, null), "a session without a timeline stamp keeps the island's position");
    }

    private static void Hold()
    {
        Check(!SeekRules.HoldEnds(0.5, false, 10, 120), "normal player: held for the first second");
        Check(SeekRules.HoldEnds(1.2, false, 10, 120), "normal player: the hold runs out after 1 s");
        Check(!SeekRules.HoldEnds(3, true, 10, 120), "Apple Music: still held after 3 s");
        Check(!SeekRules.HoldEnds(6.9, true, 10, 120), "Apple Music: still held at 6.9 s");
        Check(SeekRules.HoldEnds(7.1, true, 10, 120), "Apple Music: the hold runs out after 7 s");
        Check(SeekRules.HoldEnds(0.2, true, 119, 120), "the hold ends as soon as the player is within 2 s");
        Check(!SeekRules.HoldEnds(0.2, true, 117.5, 120), "2.5 s away is not yet there");

        // newest seek wins: the hold is (position, time) of the last release; an old release's time does not shorten the newer one
        var older = 6.0;
        var newer = 0.5;
        Check(SeekRules.HoldEnds(older, false, 10, 120) && !SeekRules.HoldEnds(newer, true, 10, 200), "a newer release is judged by its own time and target");
    }
}
