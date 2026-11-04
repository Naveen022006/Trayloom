// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text;
using System.Text.RegularExpressions;

namespace Trayloom.Features.GitHub;

/// <summary>Builds the one GraphQL query of a poll (spec 06 section 2.4) and validates the repository list.</summary>
internal static class GitHubQuery
{
    public const int MaxRepos = 10;

    private static readonly Regex RepoName = new(@"^[A-Za-z0-9-]{1,39}/[A-Za-z0-9._-]{1,100}$", RegexOptions.Compiled);

    private const string Fragments = """
        fragment Repo on Repository {
          nameWithOwner url
          defaultBranchRef { name target { ... on Commit { statusCheckRollup { state } } } }
          latestRelease { tagName name publishedAt url isPrerelease }
        }
        fragment Pr on PullRequest {
          number title url isDraft reviewDecision updatedAt
          repository { nameWithOwner }
          commits(last: 1) { nodes { commit { statusCheckRollup { state } } } }
        }
        """;

    /// <summary>Splits and validates "owner/name, owner/name"; null when any entry is invalid or there are more than ten.</summary>
    public static List<string>? ParseRepos(string text)
    {
        var list = new List<string>();
        foreach (var part in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!RepoName.IsMatch(part))
            {
                return null;
            }

            if (!list.Contains(part, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(part);
            }
        }

        return list.Count > MaxRepos ? null : list;
    }

    /// <summary>
    /// The query text. With no explicit repositories it asks for the five most recently pushed ones through <c>$repoCount</c>; otherwise one aliased
    /// field per validated name, so nothing the person typed can change the query.
    /// </summary>
    public static string Build(IReadOnlyList<string> repos)
    {
        var sb = new StringBuilder();
        var explicitRepos = repos.Count > 0;
        sb.Append(explicitRepos ? "query {\n" : "query($repoCount: Int!) {\n");
        sb.Append("  viewer {\n    login\n");
        if (!explicitRepos)
        {
            sb.Append("    repositories(first: $repoCount, orderBy: {field: PUSHED_AT, direction: DESC}, ownerAffiliations: OWNER, isArchived: false) {\n      nodes { ...Repo }\n    }\n");
        }

        sb.Append("  }\n");
        for (var i = 0; i < repos.Count; i++)
        {
            var parts = repos[i].Split('/');
            sb.Append($"  r{i}: repository(owner: \"{parts[0]}\", name: \"{parts[1]}\") {{ ...Repo }}\n");
        }

        sb.Append("  mine: search(query: \"is:open is:pr author:@me archived:false\", type: ISSUE, first: 5) {\n    issueCount\n    nodes { ...Pr }\n  }\n");
        sb.Append("  review: search(query: \"is:open is:pr review-requested:@me archived:false\", type: ISSUE, first: 5) {\n    issueCount\n    nodes { ...Pr }\n  }\n");
        sb.Append("  rateLimit { cost remaining resetAt }\n}\n");
        sb.Append(Fragments);
        return sb.ToString();
    }
}
