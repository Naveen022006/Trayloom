// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinKnife.Features.Agents;

/// <summary>One line of the Limits card as the person reads it, decided before anything is drawn.</summary>
internal sealed record LimitRowModel(string Label, string Value, double? Fraction, Color Tint, string? Reset, string? Note, bool NoteIsAlert, string Tip, double? Pace)
{
    /// <summary>Rows with a note instead of a meter are a little taller.</summary>
    public double Height => (Fraction is null ? 32 : 28) + (Reset is not null ? 13 : 0);
}

/// <summary>
/// The rules of what a limit says. A reading is shown with the source it came from and its age; a reset time only when the source
/// gave one; a window that is not running says "Not started"; a session blocked by an exhausted week says so. Nothing is inferred.
/// </summary>
internal static class LimitPresenter
{
    private static readonly Color Red = Color.FromRgb(0xFF, 0x45, 0x3A);

    public static List<LimitRowModel> Rows(AgentProvider provider, AgentLimits? limits, DateTime now, bool usedDisplay)
    {
        var rows = new List<LimitRowModel>();
        if (limits is null)
        {
            return rows;
        }

        var all = limits.Windows.Select(w => AgentLimitSupport.Current(w, now)).ToList();
        var weekly = all.FirstOrDefault(w => w.Kind == LimitKind.Weekly && w.Scope is null);
        var weekExhausted = weekly is { Renewed: false, UsedPercent: >= 100 };
        var inferred = limits.Origin == LimitOrigins.ClaudeDesktop;
        foreach (var w in all.OrderBy(w => w.Kind).ThenBy(w => w.Scope is not null).ThenBy(w => w.Minutes))
        {
            rows.Add(Row(provider, w, now, usedDisplay, inferred, weekExhausted ? weekly : null));
        }

        return rows;
    }

    private static LimitRowModel Row(AgentProvider provider, LimitWindow w, DateTime now, bool usedDisplay, bool inferred, LimitWindow? exhaustedWeek)
    {
        var label = Label(w);
        var reset = ResetText(w, now, inferred);
        var tint = Tint(provider, w);
        if (w.Renewed)
        {
            return new LimitRowModel(label, "Renewed", null, provider.Tint(), null, "Reset since the last reading; waiting for a new one", false, label + ": its reset time passed after the last reading, so what it holds now is not known yet.", null);
        }

        if (w.Kind == LimitKind.Session && exhaustedWeek is not null && (w.NotStarted || w.UsedPercent <= 0))
        {
            var until = exhaustedWeek.ResetsAt is { } r ? " - resets " + Clock(r, now) : "";
            return new LimitRowModel(label, "Blocked", null, Red, null, "Weekly limit reached" + until, true,
                "The weekly limit is used up, so no session can start until it resets." + (exhaustedWeek.ResetsAt is { } r2 ? " Resets " + r2.ToLocalTime().ToString("dddd, MMM d, h:mm tt", CultureInfo.CurrentCulture) + "." : ""), null);
        }

        if (w.NotStarted)
        {
            var start = provider == AgentProvider.Claude ? "Starts with your next message" : "Starts with your next task";
            return new LimitRowModel(label, "Not started", null, provider.Tint(), null, start, false, label + ": no usage has begun this window, so the source reports no reset time.", null);
        }

        var used = w.UsedPercent >= 100;
        var fraction = usedDisplay ? w.UsedFraction : w.RemainingFraction;
        var value = used ? "Limit reached" : AgentSettings.LimitShare(w, usedDisplay);
        var pace = AgentLimitSupport.Pace(w, now) is { } p ? (usedDisplay ? p.Elapsed : 1 - p.Elapsed) : (double?)null;
        var tip = string.Join(" · ", new[]
        {
            label, AgentStrings.UsedShare(AgentFormat.Percent(w.UsedFraction)), AgentStrings.Left(AgentFormat.Percent(w.RemainingFraction)),
            w.Severity is { Length: > 0 } s ? "severity " + s : null,
            reset is null ? "no reset time reported" : "resets " + (AgentLimitSupport.DisplayedReset(w, now) is { } d ? d.ToLocalTime().ToString("ddd MMM d, h:mm tt", CultureInfo.CurrentCulture) : ""),
            inferred ? "reset time inferred from history" : null,
        }.Where(x => !string.IsNullOrEmpty(x)));
        return new LimitRowModel(label, value, fraction, tint, reset, null, false, tip, pace);
    }

    /// <summary>The colour the source's own severity gives; otherwise the usual rule by how much is spent.</summary>
    private static Color Tint(AgentProvider provider, LimitWindow w) => w.Severity switch
    {
        "critical" => Red,
        "warning" => AgentColors.Orange,
        "normal" => w.UsedFraction >= 0.95 ? Red : provider.Tint(),
        _ => AgentColors.LimitTint(provider, w.UsedFraction),
    };

    public static string Label(LimitWindow w) => w.Label ?? w.Kind switch
    {
        LimitKind.Session => AgentStrings.Session,
        LimitKind.Weekly => w.Scope is { } s ? AgentStrings.Weekly + " · " + s : AgentStrings.Weekly,
        _ => w.Minutes is { } m ? AgentFormat.Duration(m * 60.0, 1) : w.Scope ?? AgentStrings.ReadoutLimit,
    };

    /// <summary>"in 1d 4h · Wed 7:29 AM" for what the source reported; "≈ ..." when only inferred.</summary>
    public static string? ResetText(LimitWindow w, DateTime now, bool inferred)
    {
        if (AgentLimitSupport.DisplayedReset(w, now) is not { } r)
        {
            return null;
        }

        var seconds = (r - now).TotalSeconds;
        var span = AgentFormat.Duration(Math.Max(60, seconds), seconds >= 3600 ? 2 : 1);
        return (inferred || w.EstimatedResetsAt is not null ? "≈ " : "") + "in " + span + " · " + Clock(r, now);
    }

    /// <summary>Local clock time, with the weekday when it is not today.</summary>
    public static string Clock(DateTime utc, DateTime now)
    {
        var local = utc.ToLocalTime();
        var culture = CultureInfo.CurrentCulture;
        return local.Date == now.ToLocalTime().Date ? local.ToString("h:mm tt", culture) : local.ToString("ddd h:mm tt", culture);
    }

    /// <summary>"From Claude Code · 2 minutes ago", and whether the reading is old enough to be questioned.</summary>
    public static (string Text, bool Old) SourceLine(AgentLimits limits, DateTime now)
    {
        var age = now - limits.ObservedAt;
        var text = "From " + LimitOrigins.Name(limits.Origin ?? (limits.Source == LimitSource.SessionLog ? LimitOrigins.CodexLog : null)) + " · " + AgentFormat.Relative(limits.ObservedAt > now ? now : limits.ObservedAt, now, false);
        return (text, age > TimeSpan.FromMinutes(30));
    }

    /// <summary>Claude's weekly use by product: "Cowork 91% · Chats 9% · Claude Code 0%".</summary>
    public static string? Breakdown(AgentLimits? limits)
    {
        if (limits?.Breakdown is not { Count: > 0 } rows)
        {
            return null;
        }

        var shown = rows.OrderByDescending(r => r.Percent).Where(r => r.Percent > 0 || r.Key == "claude_code").ToList();
        return shown.Count == 0 ? null : string.Join(" · ", shown.Select(r => r.Name + " " + AgentFormat.Percent(r.Percent / 100)));
    }

    /// <summary>When another source disagrees with the one in use, what it said (so the person can see why the number changed).</summary>
    public static string? Disagreement(AgentUsageSnapshot snapshot, AgentProvider provider, DateTime now)
    {
        if (!snapshot.Limits.TryGetValue(provider, out var current))
        {
            return null;
        }

        var other = snapshot.LimitSources.FirstOrDefault(s => s.Provider == provider && !s.InUse && s.ObservedAt is not null && s.Raw.Count > 0 && s.Origin == LimitOrigins.CodexLog);
        if (other is null || provider != AgentProvider.Codex)
        {
            return null;
        }

        return "An older log reading (" + AgentFormat.Relative(other.ObservedAt!.Value, now, false) + ") said something else; see Settings to compare";
    }

    /// <summary>The height the Limits card needs for what it will show.</summary>
    public static double CardHeight(List<LimitRowModel> rows, bool breakdown, bool disagreement)
    {
        var height = 20 + 17 + 6 + rows.Sum(r => r.Height + 5) + 17 + 2 + (breakdown ? 15 : 0) + (disagreement ? 14 : 0);
        return rows.Count == 0 ? AgentSettings.CardHeight : Math.Max(AgentSettings.CardHeight, height);
    }
}
