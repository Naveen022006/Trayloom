// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace Trayloom.Features.GitHub;

/// <summary>What a window title can tell about the repository: nothing, a full path, or only a folder name.</summary>
internal enum TitleHintKind
{
    None,
    Path,
    Folder,
}

/// <summary>The outcome of reading an editor or terminal title; <see cref="Candidates"/> are tried in order.</summary>
internal sealed record TitleHint(TitleHintKind Kind, IReadOnlyList<string> Candidates)
{
    public static readonly TitleHint Nothing = new(TitleHintKind.None, []);
}

/// <summary>A repository on this PC: the folder that holds <c>.git</c> and the folder's own name.</summary>
internal sealed record RepoRef(string Root, string Name);

/// <summary>
/// The result of one <c>git status</c>. The counters are per entry. <see cref="Both"/> counts the entries that are both staged and
/// unstaged, so <see cref="Changes"/> counts every changed file once.
/// </summary>
internal sealed record GitState(
    string? Branch,
    bool Detached,
    string? HeadSha,
    string? Upstream,
    int Ahead,
    int Behind,
    int Staged,
    int Unstaged,
    int Untracked,
    int Conflicts,
    bool UntrackedUnknown,
    int Both = 0)
{
    /// <summary>Changed files, each counted once.</summary>
    public int Changes => Staged + Unstaged - Both + Untracked + Conflicts;
}

/// <summary>A github.com repository, owner and name already validated.</summary>
internal sealed record GhSlug(string Owner, string Name)
{
    public override string ToString() => Owner + "/" + Name;
}

/// <summary>Why the tracker believes the person is working in a repository.</summary>
internal enum CodingReason
{
    Foreground,
    Recent,
    Agent,
    Git,
}

/// <summary>Why <c>git</c> gave no state.</summary>
internal enum GitError
{
    None,
    Missing,
    NotTrusted,
    TimedOut,
    Failed,
}

/// <summary>The checks of one commit, counted.</summary>
internal sealed record CheckSummary(CheckState State, int Total, int Passed, int Failed, int Running, int Skipped, IReadOnlyList<string> Failing, string? Url);

/// <summary>The open pull request of the branch.</summary>
internal sealed record BranchPull(int Number, string Title, string Url, bool IsDraft, string? ReviewDecision, string Repo);

/// <summary>Everything the strip and the card show. Immutable; the tracker swaps it as a whole.</summary>
internal sealed record CodingSnapshot(
    RepoRef? Repo,
    CodingReason? Reason,
    GitState? Git,
    GitError GitError,
    GhSlug? Slug,
    string? DefaultBranch,
    BranchPull? Pull,
    CheckSummary? Checks,
    DateTime? GitAt,
    DateTime? GitHubAt,
    GhError GitHubError,
    bool GitHubFound = true)
{
    public static readonly CodingSnapshot Empty = new(null, null, null, GitError.None, null, null, null, null, null, null, GhError.None);

    /// <summary>The branch to show: its name, or the short sha when HEAD is detached.</summary>
    public string? BranchLabel => Git is null ? null : Git.Detached ? (Git.HeadSha is { Length: >= 7 } sha ? sha[..7] : null) : Git.Branch;
}
