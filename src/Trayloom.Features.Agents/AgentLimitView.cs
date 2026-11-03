// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Trayloom.Core;

namespace Trayloom.Features.Agents;

/// <summary>One line of the Limits card as the person reads it, decided before anything is drawn.</summary>
internal sealed record LimitRowModel(string Label, string Value, double? Fraction, Color Tint, string? Reset, string? Note, bool NoteIsAlert, string Tip, double? Pace)
{
    /// <summary>A note this long wraps onto a second line in a card of the island's usual width.</summary>
    private const int NoteWrapAt = 34;

    /// <summary>A paused window keeps its figure but its bar is dimmed: nothing can be used until the week renews.</summary>
    public bool Dimmed { get; init; }

    public int NoteLines => Note is null ? 0 : Note.Length > NoteWrapAt ? 2 : 1;

    /// <summary>The label and number, the bar when there is one, the note and the reset line.</summary>
    public double Height => 15 + (Fraction is null ? 0 : 11) + (Note is null ? 0 : 2 + 13 * NoteLines) + 2 + (Reset is not null ? 13 : 0);
}

/// <summary>
/// The rules of what a limit says. Every row has a number and, when there is one, a bar: "N% used" or "N% left" as the person chose,
/// and a full red bar once the limit is used up. A reading is shown with the source it came from and its age; a reset time only when
/// the source gave one. A window the plan has but the source did not report says "No reading yet". Nothing is inferred.
/// </summary>
internal static class LimitPresenter
{
    private static readonly Color Red = Color.FromRgb(0xFF, 0x45, 0x3A);

    public static List<LimitRowModel> Rows(AgentProvider provider, AgentLimits? limits, DateTime now, bool usedDisplay, AgentPlan? plan = null)
    {
        var rows = new List<LimitRowModel>();
        if (limits is null)
        {
            return rows;
        }

        limits = AgentLimitPlan.Shape(provider, limits, plan)!;
        var all = limits.Windows.Select(w => AgentLimitSupport.Current(w, now)).ToList();
        var weekly = all.FirstOrDefault(w => w.Kind == LimitKind.Weekly && w.Scope is null);
        var weekExhausted = weekly is { Renewed: false, UsedPercent: >= 100 };
        var inferred = limits.Origin == LimitOrigins.ClaudeDesktop;
        var ordered = all.OrderBy(w => w.Kind).ThenBy(w => w.Scope is not null).ThenBy(w => w.Minutes).ToList();
        foreach (var w in ordered)
        {
            rows.Add(Row(provider, w, now, usedDisplay, inferred, weekExhausted ? weekly : null));
        }

        // The plan says which windows exist; one the source did not report is still listed, saying so.
        if (AgentLimitPlan.ExpectsWeekly(provider, plan) && weekly is null)
        {
            rows.Insert(ordered.Count(w => w.Kind == LimitKind.Session), Missing(provider, AgentStrings.WeeklyLimit));
        }

        if (AgentLimitPlan.ExpectsSession(provider, plan) && !ordered.Any(w => w.Kind == LimitKind.Session))
        {
            rows.Insert(0, Missing(provider, AgentStrings.SessionLimit));
        }

        return rows;
    }

    private static LimitRowModel Missing(AgentProvider provider, string label) =>
        new(label, AgentStrings.NoReading, null, provider.Tint(), null, AgentStrings.NoReadingNote, false, label + ": " + AgentStrings.NoReadingNote + ". It appears as soon as Claude reports it.", null);

    private static LimitRowModel Row(AgentProvider provider, LimitWindow w, DateTime now, bool usedDisplay, bool inferred, LimitWindow? exhaustedWeek)
    {
        var label = Label(w);
        var reset = ResetText(w, now, inferred);
        if (w.Renewed)
        {
            return new LimitRowModel(label, Loc.T("Renewed"), null, provider.Tint(), null, Loc.T("Reset since the last reading; waiting for a new one"), false, label + ": its reset time passed after the last reading, so what it holds now is not known yet.", null);
        }

        var exhausted = w.UsedPercent >= 100;
        var tint = exhausted ? Red : Tint(provider, w);

        // The week being used up pauses every shorter window, whatever it holds itself.
        string? note = null;
        var alert = false;
        string? paused = null;
        if (w.Kind == LimitKind.Session && exhaustedWeek is not null)
        {
            paused = exhaustedWeek.ResetsAt is { } wr ? Loc.F("Paused until the weekly limit resets {0}", Clock(wr, now).Replace(' ', ' ')) : Loc.T("Paused until the weekly limit resets");
            note = paused;
            alert = true;
        }
        else if (exhausted)
        {
            note = AgentStrings.LimitReached;
            alert = true;
        }
        else if (w.NotStarted)
        {
            note = provider == AgentProvider.Claude ? AgentStrings.StartsWithMessage : AgentStrings.StartsWithTask;
        }

        // A full bar in both display modes once the limit is used up, so it never reads as empty.
        var fraction = exhausted ? 1 : usedDisplay ? w.UsedFraction : w.RemainingFraction;
        var value = AgentSettings.LimitShare(w, usedDisplay);
        if (exhausted)
        {
            value = AgentStrings.UsedShare(AgentFormat.Percent(1));
        }

        var pace = AgentLimitSupport.Pace(w, now) is { } p ? (usedDisplay ? p.Elapsed : 1 - p.Elapsed) : (double?)null;
        var tip = string.Join(" · ", new[]
        {
            label, AgentStrings.UsedShare(AgentFormat.Percent(w.UsedFraction)), AgentStrings.Left(AgentFormat.Percent(w.RemainingFraction)),
            exhausted ? AgentStrings.LimitReached : null,
            paused,
            w.NotStarted ? "not started: no usage has begun this window" : null,
            w.Severity is { Length: > 0 } s ? "severity " + s : null,
            reset is null ? "no reset time reported" : "resets " + (AgentLimitSupport.DisplayedReset(w, now) is { } d ? d.ToLocalTime().ToString("ddd MMM d, " + Loc.ShortTime, CultureInfo.CurrentCulture) : ""),
            inferred ? "reset time inferred from history" : null,
        }.Where(x => !string.IsNullOrEmpty(x)));
        return new LimitRowModel(label, value, fraction, tint, reset, note, alert, tip, pace) { Dimmed = paused is not null };
    }

    /// <summary>The colour the source's own severity gives; otherwise the usual rule by how much is spent.</summary>
    private static Color Tint(AgentProvider provider, LimitWindow w) => w.Severity switch
    {
        "critical" => Red,
        "warning" => AgentColors.Orange,
        "normal" => w.UsedFraction >= 0.95 ? Red : provider.Tint(),
        _ => AgentColors.LimitTint(provider, w.UsedFraction),
    };

    public static string Label(LimitWindow w) => AgentLimitPlan.Name(w);

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
        return local.Date == now.ToLocalTime().Date ? local.ToString(Loc.ShortTime, culture) : local.ToString("ddd " + Loc.ShortTime, culture);
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

    /// <summary>The height the Limits card needs for what it will show; the two footnotes wrap onto a second line when they are long.</summary>
    public static double CardHeight(List<LimitRowModel> rows, string? breakdown, string? disagreement)
    {
        double Lines(string? text, int wrapAt) => text is null ? 0 : text.Length > wrapAt ? 25 : 14;
        var height = 20 + 17 + 6 + rows.Sum(r => r.Height + 5) + 17 + 2 + Lines(breakdown is null ? null : BreakdownPrefix + breakdown, 40) + Lines(disagreement, 42);
        return rows.Count == 0 ? AgentSettings.CardHeight : Math.Max(AgentSettings.CardHeight, height);
    }

    public static string BreakdownPrefix => Loc.T("This week's use") + ": ";
}
