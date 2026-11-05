// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace Trayloom.Features.GitHub;

/// <summary>Everything the tracker knows about where the person is working, as of <see cref="Now"/>.</summary>
internal sealed record CodingInput(
    RepoRef? ForegroundRepo,
    bool ForegroundIsCoding,
    (RepoRef Repo, DateTime At)? LastCoding,
    IReadOnlyList<(RepoRef Repo, DateTime UpdatedAt)> AgentSessions,
    IReadOnlyList<(RepoRef Repo, DateTime At)> GitActivity,
    DateTime Now);

/// <summary>Picks the active repository from the signals. Pure, so the order of the rules can be tested.</summary>
internal static class CodingFocus
{
    public static readonly TimeSpan RecentWindow = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan AgentWindow = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan GitWindow = TimeSpan.FromMinutes(10);

    /// <summary>The first rule that matches wins: foreground editor, last editor, newest Claude session, newest Git activity.</summary>
    public static (RepoRef? Repo, CodingReason? Reason) Decide(CodingInput i)
    {
        if (i.ForegroundIsCoding && i.ForegroundRepo is { } front)
        {
            return (front, CodingReason.Foreground);
        }

        if (i.LastCoding is { } last && i.Now - last.At <= RecentWindow)
        {
            return (last.Repo, CodingReason.Recent);
        }

        var agent = i.AgentSessions.Where(s => i.Now - s.UpdatedAt <= AgentWindow).OrderByDescending(s => s.UpdatedAt).Cast<(RepoRef Repo, DateTime UpdatedAt)?>().FirstOrDefault();
        if (agent is { } a)
        {
            return (a.Repo, CodingReason.Agent);
        }

        var git = i.GitActivity.Where(g => i.Now - g.At <= GitWindow).OrderByDescending(g => g.At).Cast<(RepoRef Repo, DateTime At)?>().FirstOrDefault();
        return git is { } g2 ? (g2.Repo, CodingReason.Git) : (null, null);
    }
}
