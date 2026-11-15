// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.Agents;

/// <summary>
/// The pure rules that turn the monitor's session list into what the person sees: how many sessions count, what the closed strip says,
/// which sessions the card lists and how a session's subagents read. No UI and no files, so a harness can check them.
/// </summary>
internal static class AgentSessionSummary
{
    /// <summary>Sessions that are doing something or need the person; idle ones do not count.</summary>
    public static int Active(IReadOnlyList<AgentSession> sessions) => sessions.Count(s => s.State != AgentSessionState.Idle);

    /// <summary>Only an agent that waits for the person keeps the closed island over a playing song.</summary>
    public static bool OutranksMusic(IReadOnlyList<AgentSession> sessions) => sessions.Any(s => s.IsWaiting);

    /// <summary>
    /// The words the closed strip leads with, or null when the clock of one session (or the usual reading) is enough: "3 working",
    /// "Needs you", "Needs you · 2 working", "2 need you · 1 working".
    /// </summary>
    public static string? StripLead(IReadOnlyList<AgentSession> sessions)
    {
        var active = Active(sessions);
        var waiting = sessions.Count(s => s.IsWaiting);
        if (waiting == 0)
        {
            return active > 1 ? Loc.F("{0} working", active) : null;
        }

        if (waiting == 1)
        {
            return active == 1 ? Loc.T("Needs you") : Loc.T("Needs you") + " · " + Loc.F("{0} working", active - 1);
        }

        return Loc.F("{0} need you", waiting) + (active > waiting ? " · " + Loc.F("{0} working", active - waiting) : "");
    }

    /// <summary>
    /// The count of sessions followed by the usual readout: a limit keeps its own colour and window ("3 working · 42%"), any other reading
    /// (time, tokens, cost) follows as text; without a reading the count stands alone.
    /// </summary>
    public static StripText Compose(string lead, StripText readout)
    {
        if (readout.Limit is not null)
        {
            return new StripText(lead, readout.Limit, readout.Window, readout.Provider);
        }

        return new StripText(readout.Lead.Length > 0 ? lead + " · " + readout.Lead : lead, null, null, null);
    }

    /// <summary>"1 agent" or "3 agents", then the newest agent's kind and task when it has one.</summary>
    public static string SubagentLine(AgentSession session)
    {
        var count = session.Subagents.Count;
        var line = count == 1 ? Loc.T("1 agent") : Loc.F("{0} agents", count);
        if (session.Subagents.FirstOrDefault() is { Description.Length: > 0 } first)
        {
            line += " · " + first.Type + ": " + first.Description;
        }

        return line;
    }

    /// <summary>Every working or waiting session, and idle ones only while they moved in the last <see cref="AgentSessionMonitor.IdleShown"/>.</summary>
    public static List<AgentSession> Visible(IReadOnlyList<AgentSession> sessions, DateTime now) =>
        sessions.Where(s => s.State != AgentSessionState.Idle || now - s.LastActivity <= AgentSessionMonitor.IdleShown).ToList();
}
