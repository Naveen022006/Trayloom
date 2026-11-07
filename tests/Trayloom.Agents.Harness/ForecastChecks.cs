// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Features.Agents;

namespace Trayloom.Agents.Harness;

internal static partial class Program
{
    /// <summary>The pure maths of the limit forecast: the pace from a reading, the projected run-out and its clamping to the reset.</summary>
    private static void Forecast()
    {
        var start = new DateTime(2026, 11, 6, 8, 0, 0, DateTimeKind.Utc);
        var reset = start.AddHours(5);

        LimitWindow Session(double used, DateTime? resets = null) => new("five_hour", LimitKind.Session, 300, null, used, resets ?? reset) { Active = true };

        // Rate: 50 % after 2 hours is 25 % per hour.
        var at2h = start.AddHours(2);
        var rate = AgentLimitForecast.RatePerMinute(Session(50), at2h, at2h);
        Check(rate is { } r && Math.Abs(r - 50.0 / 120) < 1e-9, "rate: 50% after 2 h is 0.4167 % per minute");
        Check(AgentLimitForecast.RatePerMinute(Session(50), start.AddMinutes(9), start.AddMinutes(9)) is null, "rate: nothing under 10 minutes into the window");
        // 5 % of a 7-day window is 8.4 hours, more than the 10-minute floor.
        var weekReset = start.AddDays(7);
        var week = new LimitWindow("seven_day", LimitKind.Weekly, 10080, null, 30, weekReset) { Active = true };
        Check(AgentLimitForecast.RatePerMinute(week, start.AddHours(8), start.AddHours(8)) is null, "rate: 5% of a weekly window is the floor (8 h is too early)");
        Check(AgentLimitForecast.RatePerMinute(week, start.AddHours(9), start.AddHours(9)) is not null, "rate: a weekly window is measurable after 5% of its length");

        // Run-out: 50 % after 2 h reaches 100 % after 4 h, before the 5 h reset.
        var eta = AgentLimitForecast.RunsOutAt(Session(50), at2h, at2h);
        Check(eta == start.AddHours(4), "run-out: 50% after 2 h => 4 h after the start");
        Check(eta is { } e && e < reset, "run-out: before the reset");

        // The estimate is judged at 'now' (a little after the reading): still the same moment.
        Check(AgentLimitForecast.RunsOutAt(Session(50), at2h, at2h.AddMinutes(10)) == start.AddHours(4), "run-out: 10 minutes after the reading, same moment");

        // Clamping to the reset: 20 % after 2 h would reach 100 % at 10 h, long after the reset.
        Check(AgentLimitForecast.RunsOutAt(Session(20), at2h, at2h) is null, "run-out: after the reset => none");
        // 40 % after 2 h would land on the reset itself, which is too fragile for floating point to assert; 41 % is clearly before it.
        Check(AgentLimitForecast.RunsOutAt(Session(41), at2h, at2h) is not null, "run-out: just before the reset => value");

        Check(AgentLimitForecast.RunsOutAt(Session(5), at2h, at2h) is null, "5% used => none");
        Check(AgentLimitForecast.RunsOutAt(Session(9.9), at2h, at2h) is null, "just under 10% => none");
        Check(AgentLimitForecast.RunsOutAt(Session(100), at2h, at2h) is null, "100% used => none");
        var early = start.AddMinutes(3);
        Check(AgentLimitForecast.RunsOutAt(Session(50), early, early) is null, "3 minutes elapsed => none");
        Check(AgentLimitForecast.RunsOutAt(Session(50), at2h, at2h.AddMinutes(40)) is null, "reading 40 minutes old => none");
        Check(AgentLimitForecast.RunsOutAt(Session(50) with { Renewed = true }, at2h, at2h) is null, "renewed window => none");
        Check(AgentLimitForecast.RunsOutAt(Session(50, resets: DateTime.MinValue) with { ResetsAt = null }, at2h, at2h) is null, "no reset time => none");
        Check(AgentLimitForecast.RunsOutAt(new LimitWindow("five_hour", LimitKind.Session, null, null, 50, reset), at2h, at2h) is null, "no window length => none");
        Check(AgentLimitForecast.RunsOutAt(Session(50), at2h, reset.AddMinutes(1)) is null, "reset already passed => none");

        // The warning lead.
        var now = new DateTime(2026, 11, 6, 11, 0, 0, DateTimeKind.Utc);
        Check(AgentLimitForecast.ShouldWarn(now.AddMinutes(29), now, 30), "warn: 29 minutes ahead, lead 30");
        Check(AgentLimitForecast.ShouldWarn(now.AddMinutes(30), now, 30), "warn: 30 minutes ahead, lead 30");
        Check(!AgentLimitForecast.ShouldWarn(now.AddMinutes(31), now, 30), "no warn: 31 minutes ahead, lead 30");
        Check(AgentLimitForecast.ShouldWarn(now.AddMinutes(31), now, 60), "warn: 31 minutes ahead, lead 60");

        // One warning per window and renewal.
        var w = Session(50);
        Check(AgentLimitForecast.WarnKey(w, reset) == AgentLimitForecast.WarnKey(w, reset), "warn key is stable for the same reset");
        Check(AgentLimitForecast.WarnKey(w, reset) != AgentLimitForecast.WarnKey(w, reset.AddHours(5)), "warn key changes with the reset");
        Check(AgentLimitForecast.WarnKey(w, reset) != AgentLimitForecast.WarnKey(week, reset), "warn key differs between windows");

        // The limits card shows the forecast only as a note on a row that has none.
        var limits = new AgentLimits(AgentProvider.Claude, [Session(50)], at2h, LimitSource.ClaudeApp);
        var rows = LimitPresenter.Rows(AgentProvider.Claude, limits, at2h, false);
        Check(rows.Any(row => row.Note is { } n && n.StartsWith("Runs out around ", StringComparison.Ordinal) && n.EndsWith(" at this pace", StringComparison.Ordinal)), "limits card: row note says when it runs out");
        var slow = limits with { Windows = [Session(20)] };
        Check(LimitPresenter.Rows(AgentProvider.Claude, slow, at2h, false).All(row => row.Note is null), "limits card: no note when the window lasts");
    }
}
