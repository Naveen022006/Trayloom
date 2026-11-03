// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using WinKnife.Core;

namespace WinKnife.Features.Agents;

/// <summary>
/// Which allowances a plan has, and what each one is called. Claude Ultra has a weekly limit only; Claude Max, Pro, Team and the rest
/// have a 5-hour limit and a weekly one. A plan that is not known shows exactly what the source reports. Nothing is invented: a window
/// the plan should have but the source has not reported yet is shown as "No reading yet", never with a made-up figure.
/// </summary>
internal static class AgentLimitPlan
{
    public static bool IsUltra(AgentPlan? plan) => plan is not null && plan.Name.Contains("ultra", StringComparison.OrdinalIgnoreCase);

    /// <summary>False when the plan has no 5-hour window at all, so none is shown, counted or warned about (Claude Ultra).</summary>
    public static bool HasSessionWindow(AgentProvider provider, AgentPlan? plan) => !(provider == AgentProvider.Claude && IsUltra(plan));

    /// <summary>A 5-hour row is always shown for these plans, with "No reading yet" when the source gave nothing.</summary>
    public static bool ExpectsSession(AgentProvider provider, AgentPlan? plan) => provider == AgentProvider.Claude && plan is not null && !IsUltra(plan);

    /// <summary>A weekly row is always shown for these plans.</summary>
    public static bool ExpectsWeekly(AgentProvider provider, AgentPlan? plan) => provider == AgentProvider.Claude && plan is not null;

    /// <summary>One sentence on which allowances the plan has (the plan chip's tooltip); null when there is nothing to say.</summary>
    public static string? Describe(AgentProvider provider, AgentPlan? plan)
    {
        if (plan is null || provider != AgentProvider.Claude)
        {
            return null;
        }

        return IsUltra(plan)
            ? Loc.T("Claude Ultra has a weekly limit only, so no 5-hour limit is shown.")
            : Loc.F("Claude {0} has a 5-hour limit and a weekly limit.", plan.Name);
    }

    /// <summary>The reading without the windows the plan does not have.</summary>
    public static AgentLimits? Shape(AgentProvider provider, AgentLimits? limits, AgentPlan? plan)
    {
        if (limits is null || HasSessionWindow(provider, plan))
        {
            return limits;
        }

        var kept = limits.Windows.Where(w => w.Kind != LimitKind.Session).ToList();
        return kept.Count == limits.Windows.Count ? limits : limits with { Windows = kept };
    }

    /// <summary>Whether any agent in use has a 5-hour window, for the settings menu that picks the limit to show.</summary>
    public static bool AnySessionWindow(AgentUsageSnapshot snapshot, IEnumerable<AgentProvider> providers)
    {
        foreach (var provider in providers.Where(p => p.ReportsLimits()))
        {
            snapshot.Plans.TryGetValue(provider, out var plan);
            if (!HasSessionWindow(provider, plan))
            {
                continue;
            }

            if (provider == AgentProvider.Claude || snapshot.Limits.GetValueOrDefault(provider)?.Windows.Any(w => w.Kind == LimitKind.Session) == true)
            {
                return true;
            }
        }

        return false;
    }

    // ------------------------------------------------------------------------------------------ names

    /// <summary>What a window is called to the person: "5-hour limit", "Weekly limit", "Weekly · Opus", "30-day limit".</summary>
    public static string Name(LimitWindow w)
    {
        if (w.Label is { Length: > 0 } label)
        {
            return label;
        }

        if (w.Kind == LimitKind.Weekly && w.Scope is { Length: > 0 } scope)
        {
            return Loc.T("Weekly") + " · " + scope;
        }

        if (w.Minutes is > 0 and var minutes)
        {
            return ByLength(minutes);
        }

        return w.Kind switch
        {
            LimitKind.Session => AgentStrings.SessionLimit,
            LimitKind.Weekly => AgentStrings.WeeklyLimit,
            _ => w.Scope ?? AgentStrings.ReadoutLimit,
        };
    }

    /// <summary>A window named by how long it lasts.</summary>
    public static string ByLength(int minutes)
    {
        if (minutes == 10_080)
        {
            return AgentStrings.WeeklyLimit;
        }

        if (minutes % 1440 == 0)
        {
            return Loc.F("{0}-day limit", minutes / 1440);
        }

        if (minutes % 60 == 0)
        {
            return Loc.F("{0}-hour limit", minutes / 60);
        }

        return Loc.F("{0}-minute limit", minutes);
    }

    /// <summary>The short tag in front of a number on the closed island: "5h", "Wk", "30d", "Wk Opus".</summary>
    public static string Tag(LimitWindow w)
    {
        if (w.Kind == LimitKind.Weekly && w.Scope is { Length: > 0 } scope)
        {
            return Loc.T("Wk") + " " + scope;
        }

        if (w.Minutes is > 0 and var minutes)
        {
            if (minutes == 10_080)
            {
                return Loc.T("Wk");
            }

            if (minutes % 1440 == 0)
            {
                return (minutes / 1440).ToString(CultureInfo.CurrentCulture) + Loc.T("d");
            }

            if (minutes % 60 == 0)
            {
                return (minutes / 60).ToString(CultureInfo.CurrentCulture) + Loc.T("h");
            }

            return minutes.ToString(CultureInfo.CurrentCulture) + Loc.T("m");
        }

        return w.Kind switch
        {
            LimitKind.Session => "5" + Loc.T("h"),
            LimitKind.Weekly => Loc.T("Wk"),
            _ => w.Label ?? AgentStrings.ReadoutLimit,
        };
    }
}
