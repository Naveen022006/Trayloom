// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace Trayloom.Features.WindowTools.AwayMode;

/// <summary>
/// When the automatic triggers (the idle timer and the camera) may minimize the windows. Pure, so the harness can drive it with
/// made-up times; all times are in milliseconds on one monotonic clock.
/// </summary>
internal static class AwayLeaveGuard
{
    /// <summary>No automatic leave in the first minute after Away mode starts (a Trayloom restart, an update, a test run).</summary>
    public const long StartGraceMs = 60_000;

    /// <summary>No automatic leave right after the person came back.</summary>
    public const long AfterPresentMs = 10_000;

    /// <summary>
    /// The idle time Away mode acts on: the time since the last input, but never more than the time since the person last counted as
    /// present (Away mode started, or the windows came back). Windows keeps counting idle time through a Trayloom restart and through a
    /// return by face, which brings no hand to the mouse, so the raw value would make Away mode leave again at once.
    /// </summary>
    public static long EffectiveIdleMs(long rawIdleMs, long now, long presentSince) => Math.Max(0, Math.Min(rawIdleMs, now - presentSince));

    /// <summary>True when an automatic trigger may minimize the windows now; "Go away now" is never held back.</summary>
    public static bool MayLeave(long now, long startedAt, long presentSince) => now - startedAt >= StartGraceMs && now - presentSince >= AfterPresentMs;
}
