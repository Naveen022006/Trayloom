// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace Trayloom.Features.Agents;

/// <summary>
/// The pure arithmetic behind "runs out around 3:40 PM at this pace". It reads only numbers Trayloom already has (a window's
/// used percent, its length and its reset time), assumes the pace so far this window continues, and says nothing when that guess
/// would be unreliable: too early in the window, a stale reading, or a window that would not run out before it resets anyway.
/// </summary>
internal static class AgentLimitForecast
{
    /// <summary>A reading older than this says nothing about the pace right now, so no forecast is made from it.</summary>
    public static readonly TimeSpan MaxReadingAge = TimeSpan.FromMinutes(30);

    /// <summary>Below this share used the pace is mostly noise (one short reply moves it), so no forecast is made.</summary>
    public const double MinUsedPercent = 10;

    /// <summary>The window must have run at least this long (or 5 % of its length, whichever is more) before its pace means anything.</summary>
    public static readonly TimeSpan MinElapsed = TimeSpan.FromMinutes(10);

    private const double MinElapsedShare = 0.05;

    /// <summary>
    /// Percent of the window used per minute since it began, measured at <paramref name="observedAt"/>; null when the window cannot
    /// be measured (no length, no reset time, or it has not run long enough).
    /// </summary>
    public static double? RatePerMinute(LimitWindow w, DateTime observedAt, DateTime now)
    {
        if (w.Minutes is not > 0 || AgentLimitSupport.DisplayedReset(w, now) is not { } reset)
        {
            return null;
        }

        var length = TimeSpan.FromMinutes(w.Minutes.Value);
        var elapsed = observedAt - (reset - length);
        var minimum = length * MinElapsedShare > MinElapsed ? length * MinElapsedShare : MinElapsed;
        if (elapsed < minimum)
        {
            return null;
        }

        return w.UsedPercent / elapsed.TotalMinutes;
    }

    /// <summary>Linear "at your pace so far this window": when used% reaches 100, or null if not before the reset.</summary>
    public static DateTime? RunsOutAt(LimitWindow w, DateTime observedAt, DateTime now)
    {
        if (w.Renewed || w.NotStarted || w.UsedPercent >= 100 || w.UsedPercent < MinUsedPercent || w.Minutes is null)
        {
            return null;
        }

        if (AgentLimitSupport.DisplayedReset(w, now) is not { } reset || now - observedAt > MaxReadingAge)
        {
            return null;
        }

        if (RatePerMinute(w, observedAt, now) is not { } rate || rate <= 0)
        {
            return null;
        }

        var eta = observedAt + TimeSpan.FromMinutes((100 - w.UsedPercent) / rate);
        return eta < reset && eta > now ? eta : null;
    }

    /// <summary>Whether the projected run-out is close enough to be worth a notice.</summary>
    public static bool ShouldWarn(DateTime eta, DateTime now, int leadMinutes) => eta - now <= TimeSpan.FromMinutes(leadMinutes);

    /// <summary>One warning per window per renewal: the key changes when the window's reset moves on.</summary>
    public static string WarnKey(LimitWindow w, DateTime reset) => w.Id + "|" + reset.ToString("o");

    /// <summary>The forecast for a row, as local clock text with the weekday when it is not today; null when there is none.</summary>
    public static string? Text(LimitWindow w, DateTime observedAt, DateTime now) =>
        RunsOutAt(w, observedAt, now) is { } eta ? LimitPresenter.Clock(eta, now) : null;
}
