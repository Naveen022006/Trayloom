// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace Trayloom.Features.Island;

/// <summary>
/// The pure decisions of moving within a song (kept apart from UI Automation and WPF so the harness can check them): what Apple Music's scrubber
/// is set to, whether the island may offer a draggable timeline, how long a position set by the island outranks the media session, and when the
/// timeline stops showing the released position.
/// </summary>
internal static class SeekRules
{
    /// <summary>The scrubber's range may differ from the song's length by this many seconds before its unit is not taken to be seconds.</summary>
    public const double UnitToleranceSeconds = 3;

    /// <summary>The scrubber counts as having reached the target when it is within this many seconds (in its own unit) of it.</summary>
    public const double ReachedToleranceSeconds = 2;

    /// <summary>How long the timeline keeps the released position while it waits for the player: Apple Music seeks through UI Automation and is slow.</summary>
    public static double HoldSeconds(bool appleMusic) => appleMusic ? 7 : 1;

    /// <summary>The player has reached the released position (to within 2 s).</summary>
    public const double HoldReachedSeconds = 2;

    // ---------------------------------------------------------------- the timeline is draggable

    /// <summary>
    /// Whether the timeline can be dragged. Apple Music's media session reports that it cannot set the position (and ignores the request), so its
    /// own scrubber is used instead, whenever the island has a way to reach it; every other player follows what its session reports.
    /// </summary>
    public static bool CanSeek(bool appleMusic, bool positionEnabled, double duration, bool appleSeekAvailable) =>
        duration > 0 && (appleMusic ? appleSeekAvailable : positionEnabled);

    // ---------------------------------------------------------------- Apple Music's scrubber

    /// <summary>The scrubber's range is in seconds (or the song's length is not known, then seconds are assumed).</summary>
    public static bool RangeIsSeconds(double duration, double min, double max) =>
        duration <= 0 || Math.Abs(max - min - duration) <= UnitToleranceSeconds;

    /// <summary>The size of one second in the scrubber's own unit.</summary>
    public static double SecondInUnits(double duration, double min, double max) =>
        RangeIsSeconds(duration, min, max) ? 1 : (max - min) / duration;

    /// <summary>
    /// The value to set the scrubber to for <paramref name="seconds"/> into the song: seconds when the range is the song's length in seconds,
    /// otherwise the same fraction of the range. Never closer to the end than one second, so the song is not skipped.
    /// </summary>
    public static double ScrubberTarget(double seconds, double duration, double min, double max)
    {
        var second = SecondInUnits(duration, min, max);
        var raw = RangeIsSeconds(duration, min, max) ? min + seconds : min + (seconds / duration * (max - min));
        return Math.Clamp(raw, min, Math.Max(min, max - second));
    }

    /// <summary>The scrubber stands where it was set to (within 2 s in its own unit).</summary>
    public static bool ScrubberReached(double after, double target, double duration, double min, double max) =>
        Math.Abs(after - target) <= ReachedToleranceSeconds * SecondInUnits(duration, min, max);

    // ---------------------------------------------------------------- the position set by the island

    /// <summary>
    /// A position the island set after a seek outranks the media session until the session delivers a timeline updated after the seek
    /// (<paramref name="sessionStamp"/>: its LastUpdatedTime, null when it reports none).
    /// </summary>
    public static bool OverrideHolds(DateTime seekAt, DateTime? sessionStamp) => sessionStamp is not { } stamp || stamp <= seekAt;

    // ---------------------------------------------------------------- the timeline's hold

    /// <summary>
    /// The timeline stops showing the released position: the player reports a position within 2 s of it, or the hold has run out
    /// (1 s for a normal player, 7 s for Apple Music).
    /// </summary>
    public static bool HoldEnds(double sinceReleaseSeconds, bool appleMusic, double playerPosition, double released) =>
        sinceReleaseSeconds > HoldSeconds(appleMusic) || Math.Abs(playerPosition - released) < HoldReachedSeconds;
}
