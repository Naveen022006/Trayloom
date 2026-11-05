// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Trayloom.Features.GitHub;

/// <summary>What the GraphQL answer said about the branch.</summary>
internal sealed record CodingRemote(bool Found, string? DefaultBranch, BranchPull? Pull, CheckSummary? Checks, string? ChecksSha, int Remaining);

/// <summary>The one GraphQL question about the branch you are on, and the reading of its answer. Pure.</summary>
internal static class CodingChecks
{
    private static readonly Regex Sha40 = new(@"^[0-9a-f]{40}$", RegexOptions.Compiled);

    private const string Fragments = """
        fragment P on PullRequest { number title url isDraft reviewDecision headRepositoryOwner { login }
          commits(last: 1) { nodes { commit { oid statusCheckRollup { ...R } } } } }
        fragment R on StatusCheckRollup { state contexts(first: 30) { totalCount nodes { __typename
          ... on CheckRun { name status conclusion detailsUrl } ... on StatusContext { context state targetUrl } } } }
        """;

    /// <summary>
    /// The query text. Every value is an inlined literal, like <see cref="GitHubQuery"/>: the owner and name must pass the repository rule
    /// (otherwise this throws), the branch becomes a JSON string (also a valid GraphQL string) and the sha is used only when it is 40 hex digits.
    /// </summary>
    public static string BuildQuery(GhSlug slug, string branch, string? pushedSha, bool withParent)
    {
        if (GitCli.Slug(slug.Owner, slug.Name) is null)
        {
            throw new ArgumentException("invalid repository name", nameof(slug));
        }

        if (string.IsNullOrEmpty(branch) || branch.Length > 255)
        {
            throw new ArgumentException("invalid branch", nameof(branch));
        }

        var literal = JsonSerializer.Serialize(branch);
        var sb = new StringBuilder();
        sb.Append("query {\n");
        sb.Append($"  repository(owner: \"{slug.Owner}\", name: \"{slug.Name}\") {{\n");
        sb.Append("    nameWithOwner url defaultBranchRef { name }\n");
        sb.Append($"    pullRequests(headRefName: {literal}, states: OPEN, first: 5, orderBy: {{field: UPDATED_AT, direction: DESC}}) {{ nodes {{ ...P }} }}\n");
        if (withParent)
        {
            sb.Append($"    parent {{ nameWithOwner pullRequests(headRefName: {literal}, states: OPEN, first: 5) {{ nodes {{ ...P }} }} }}\n");
        }

        if (pushedSha is not null && Sha40.IsMatch(pushedSha))
        {
            sb.Append($"    pushed: object(oid: \"{pushedSha}\") {{ ... on Commit {{ oid statusCheckRollup {{ ...R }} }} }}\n");
        }

        sb.Append("  }\n  rateLimit { cost remaining resetAt }\n}\n");
        sb.Append(Fragments);
        return sb.ToString();
    }

    /// <summary>
    /// Reads the <c>data</c> object. The pull request is the first one opened from the person's own fork or branch (in the repository, then
    /// in its parent); its checks are those of its last commit when that is the pushed commit, otherwise those of the pushed commit.
    /// </summary>
    public static CodingRemote Parse(JsonElement data, GhSlug slug, string? pushedSha)
    {
        var remaining = data.Obj("rateLimit") is { ValueKind: JsonValueKind.Object } rate ? rate.Int("remaining") : int.MaxValue;
        var repository = data.Obj("repository");
        if (repository.ValueKind != JsonValueKind.Object)
        {
            return new CodingRemote(false, null, null, null, null, remaining);
        }

        var defaultBranch = repository.Obj("defaultBranchRef").StrOrNull("name");
        var found = new List<(string Repo, JsonElement Node)>();
        AddPulls(found, repository.Str("nameWithOwner"), repository.Obj("pullRequests"), slug);
        var parent = repository.Obj("parent");
        if (parent.ValueKind == JsonValueKind.Object)
        {
            AddPulls(found, parent.Str("nameWithOwner"), parent.Obj("pullRequests"), slug);
        }

        BranchPull? pull = null;
        CheckSummary? checks = null;
        string? checksSha = null;
        if (found.Count > 0)
        {
            var (repo, node) = found[0];
            pull = new BranchPull(node.Int("number"), node.Str("title"), node.Str("url"), node.Bool("isDraft"), node.StrOrNull("reviewDecision"), repo);
        }

        // With no upstream there is no pushed commit to compare with, so the pull request's own last commit is the best answer.
        var last = found.Count > 0 ? found[0].Node.Obj("commits").Obj("nodes") : default;
        var lastCommit = last.ValueKind == JsonValueKind.Array && last.GetArrayLength() > 0 ? last[0].Obj("commit") : default;
        var lastOid = lastCommit.StrOrNull("oid");
        var pushed = repository.Obj("pushed");
        if (lastOid is not null && (pushedSha is null || string.Equals(lastOid, pushedSha, StringComparison.OrdinalIgnoreCase)))
        {
            checksSha = lastOid;
            checks = Summarize(lastCommit.Obj("statusCheckRollup"), slug, checksSha);
        }
        else if (pushed.ValueKind == JsonValueKind.Object)
        {
            checksSha = pushed.StrOrNull("oid") ?? pushedSha;
            checks = Summarize(pushed.Obj("statusCheckRollup"), slug, checksSha);
        }

        return new CodingRemote(true, defaultBranch, pull, checks, checksSha, remaining);
    }

    private static void AddPulls(List<(string, JsonElement)> list, string repo, JsonElement pulls, GhSlug slug)
    {
        var nodes = pulls.Obj("nodes");
        if (nodes.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var node in nodes.EnumerateArray())
        {
            if (node.ValueKind == JsonValueKind.Object && node.Int("number") > 0
                && string.Equals(node.Obj("headRepositoryOwner").StrOrNull("login"), slug.Owner, StringComparison.OrdinalIgnoreCase))
            {
                list.Add((repo, node));
            }
        }
    }

    /// <summary>Counts the checks of a <c>StatusCheckRollup</c>. A missing rollup is "no checks".</summary>
    public static CheckSummary Summarize(JsonElement rollup, GhSlug? slug = null, string? sha = null)
    {
        int passed = 0, failed = 0, running = 0, skipped = 0;
        var failing = new List<string>();
        string? failUrl = null;
        var contexts = rollup.Obj("contexts");
        var nodes = contexts.Obj("nodes");
        var seen = 0;
        if (nodes.ValueKind == JsonValueKind.Array)
        {
            foreach (var node in nodes.EnumerateArray())
            {
                seen++;
                string name;
                string? url;
                CheckOutcome outcome;
                if (node.Str("__typename") == "StatusContext")
                {
                    name = node.Str("context");
                    url = node.StrOrNull("targetUrl");
                    outcome = node.Str("state") switch
                    {
                        "SUCCESS" => CheckOutcome.Passed,
                        "PENDING" or "EXPECTED" => CheckOutcome.Running,
                        "FAILURE" or "ERROR" => CheckOutcome.Failed,
                        _ => CheckOutcome.Skipped,
                    };
                }
                else
                {
                    name = node.Str("name");
                    url = node.StrOrNull("detailsUrl");
                    outcome = node.Str("status") != "COMPLETED"
                        ? CheckOutcome.Running
                        : node.Str("conclusion") switch
                        {
                            "SUCCESS" => CheckOutcome.Passed,
                            "NEUTRAL" or "SKIPPED" or "STALE" => CheckOutcome.Skipped,
                            "FAILURE" or "TIMED_OUT" or "CANCELLED" or "ACTION_REQUIRED" or "STARTUP_FAILURE" => CheckOutcome.Failed,
                            _ => CheckOutcome.Skipped,
                        };
                }

                switch (outcome)
                {
                    case CheckOutcome.Passed:
                        passed++;
                        break;
                    case CheckOutcome.Running:
                        running++;
                        break;
                    case CheckOutcome.Failed:
                        failed++;
                        if (failing.Count < 3 && name.Length > 0)
                        {
                            failing.Add(name);
                        }

                        failUrl ??= url;
                        break;
                    default:
                        skipped++;
                        break;
                }
            }
        }

        var total = Math.Max(contexts.Int("totalCount"), seen);
        CheckState state;
        if (failed > 0)
        {
            state = CheckState.Failing;
        }
        else if (running > 0)
        {
            state = CheckState.Running;
        }
        else if (passed + skipped > 0)
        {
            state = CheckState.Passing;
        }
        else
        {
            state = RollupState(rollup);
        }

        // Only 30 contexts come back; a rollup that is worse than what those showed still counts.
        if (seen > 0 && total > seen)
        {
            var overall = RollupState(rollup);
            if (overall == CheckState.Failing || (overall == CheckState.Running && state == CheckState.Passing))
            {
                state = overall;
            }
        }

        var link = failUrl is not null && GitHubLinks.IsSafe(failUrl)
            ? failUrl
            : slug is not null && sha is not null && Sha40.IsMatch(sha) ? $"https://github.com/{slug.Owner}/{slug.Name}/commit/{sha}/checks" : null;
        return new CheckSummary(state, total, passed, failed, running, skipped, failing, link);
    }

    private static CheckState RollupState(JsonElement rollup) => rollup.StrOrNull("state") switch
    {
        "SUCCESS" => CheckState.Passing,
        "FAILURE" or "ERROR" => CheckState.Failing,
        "PENDING" or "EXPECTED" => CheckState.Running,
        _ => CheckState.None,
    };

    private enum CheckOutcome
    {
        Passed,
        Running,
        Failed,
        Skipped,
    }

    /// <summary>True when checks that were running on this commit have all finished (and there were some).</summary>
    public static bool Finished(CheckSummary? before, CheckSummary now, bool sameSha) =>
        sameSha && before is { Running: > 0 } && now.Running == 0 && now.Total > 0;
}
