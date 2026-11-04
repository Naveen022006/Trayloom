// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Trayloom.Features.GitHub;

/// <summary>Why the last poll did not work (spec 06 section 2.4).</summary>
public enum GhError
{
    None,
    Offline,
    SignedOut,
    TokenRejected,
    RateLimited,
    GhMissing,
    Unexpected,
}

/// <summary>The combined state of the checks of a commit.</summary>
public enum CheckState
{
    None,
    Passing,
    Failing,
    Running,
}

/// <summary>One unread notification (only what the page shows).</summary>
public sealed record GhNotification(string Id, string Reason, DateTime? UpdatedAt, string Title, string Type, string? SubjectUrl, string Repo);

/// <summary>An open pull request of the person or waiting for their review.</summary>
public sealed record GhPull(int Number, string Title, string Url, bool IsDraft, string? ReviewDecision, DateTime? UpdatedAt, string Repo, CheckState State);

/// <summary>The latest release of a repository.</summary>
public sealed record GhRelease(string Tag, DateTime? PublishedAt, string? Url);

/// <summary>A watched repository: the checks of its default branch and its latest release.</summary>
public sealed record GhRepo(string Name, string? Url, CheckState State, GhRelease? Release, bool Found = true);

/// <summary>Everything the page shows, as of <see cref="FetchedAt"/>.</summary>
public sealed record GitHubSnapshot
{
    public string Login { get; init; } = "";

    public DateTime FetchedAt { get; init; }

    public List<GhNotification> Notifications { get; init; } = [];

    public bool NotificationsAvailable { get; init; } = true;

    public int MineCount { get; init; }

    public List<GhPull> Mine { get; init; } = [];

    public int ReviewCount { get; init; }

    public List<GhPull> Review { get; init; } = [];

    public List<GhRepo> Repos { get; init; } = [];

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GhError Error { get; init; }

    public DateTime? RetryAt { get; init; }

    /// <summary>Repositories whose default branch fails its checks.</summary>
    [JsonIgnore]
    public int FailingCount => Repos.Count(r => r.State == CheckState.Failing);

    /// <summary>The newest release across the watched repositories.</summary>
    [JsonIgnore]
    public (GhRepo Repo, GhRelease Release)? LatestRelease =>
        Repos.Where(r => r.Release is not null).OrderByDescending(r => r.Release!.PublishedAt).Select(r => ((GhRepo, GhRelease)?)(r, r.Release!)).FirstOrDefault();
}

/// <summary>Shared JSON settings for the cache file.</summary>
internal static class GhJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Str(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    public static string? StrOrNull(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static int Int(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;

    public static bool Bool(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    public static JsonElement Obj(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;

    public static DateTime? Date(this JsonElement e, string name) =>
        e.StrOrNull(name) is { } s && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : null;
}

/// <summary>Turns GitHub's JSON into the snapshot model.</summary>
internal static class GitHubParse
{
    public static List<GhNotification> Notifications(string body)
    {
        var list = new List<GhNotification>();
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var n in doc.RootElement.EnumerateArray())
        {
            var subject = n.Obj("subject");
            list.Add(new GhNotification(n.Str("id"), n.Str("reason"), n.Date("updated_at"), subject.Str("title"), subject.Str("type"), subject.StrOrNull("url"), n.Obj("repository").Str("full_name")));
        }

        return list;
    }

    private static CheckState State(JsonElement rollupOwner)
    {
        var state = rollupOwner.Obj("statusCheckRollup").StrOrNull("state");
        return state switch
        {
            "SUCCESS" => CheckState.Passing,
            "FAILURE" or "ERROR" => CheckState.Failing,
            "PENDING" or "EXPECTED" => CheckState.Running,
            _ => CheckState.None,
        };
    }

    private static GhRepo? Repo(JsonElement r, string? fallbackName)
    {
        if (r.ValueKind != JsonValueKind.Object)
        {
            return fallbackName is null ? null : new GhRepo(fallbackName, null, CheckState.None, null, false);
        }

        var target = r.Obj("defaultBranchRef").Obj("target");
        var rel = r.Obj("latestRelease");
        GhRelease? release = rel.ValueKind == JsonValueKind.Object ? new GhRelease(rel.Str("tagName"), rel.Date("publishedAt"), rel.StrOrNull("url")) : null;
        return new GhRepo(r.Str("nameWithOwner"), r.StrOrNull("url"), State(target), release);
    }

    private static List<GhPull> Pulls(JsonElement search)
    {
        var list = new List<GhPull>();
        var nodes = search.Obj("nodes");
        if (nodes.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var p in nodes.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Object || p.Int("number") == 0)
            {
                continue;
            }

            var commits = p.Obj("commits").Obj("nodes");
            var state = CheckState.None;
            if (commits.ValueKind == JsonValueKind.Array && commits.GetArrayLength() > 0)
            {
                state = State(commits[0].Obj("commit"));
            }

            list.Add(new GhPull(p.Int("number"), p.Str("title"), p.Str("url"), p.Bool("isDraft"), p.StrOrNull("reviewDecision"), p.Date("updatedAt"), p.Obj("repository").Str("nameWithOwner"), state));
        }

        return list;
    }

    /// <summary>Reads the GraphQL <c>data</c> object. <paramref name="remaining"/> and <paramref name="resetAt"/> come from <c>rateLimit</c>.</summary>
    public static GitHubSnapshot GraphQl(JsonElement data, GitHubSnapshot basis, IReadOnlyList<string>? explicitNames, out int cost, out int remaining, out DateTime? resetAt)
    {
        var rate = data.Obj("rateLimit");
        cost = rate.Int("cost");
        remaining = rate.ValueKind == JsonValueKind.Object ? rate.Int("remaining") : int.MaxValue;
        resetAt = rate.Date("resetAt");
        var repos = new List<GhRepo>();
        var viewer = data.Obj("viewer");
        var nodes = viewer.Obj("repositories").Obj("nodes");
        if (nodes.ValueKind == JsonValueKind.Array)
        {
            repos.AddRange(nodes.EnumerateArray().Select(n => Repo(n, null)).Where(r => r is not null)!);
        }

        if (data.ValueKind == JsonValueKind.Object)
        {
            // Explicit repositories come back as r0, r1, ... in the order they were asked for.
            foreach (var prop in data.EnumerateObject().Where(p => p.Name.Length > 1 && p.Name[0] == 'r' && char.IsDigit(p.Name[1])).OrderBy(p => int.Parse(p.Name[1..], CultureInfo.InvariantCulture)))
            {
                var index = int.Parse(prop.Name[1..], CultureInfo.InvariantCulture);
                var repo = Repo(prop.Value, explicitNames is not null && index < explicitNames.Count ? explicitNames[index] : prop.Name);
                if (repo is not null)
                {
                    repos.Add(repo);
                }
            }
        }

        return basis with
        {
            Login = viewer.Str("login") is { Length: > 0 } login ? login : basis.Login,
            MineCount = data.Obj("mine").Int("issueCount"),
            Mine = Pulls(data.Obj("mine")),
            ReviewCount = data.Obj("review").Int("issueCount"),
            Review = Pulls(data.Obj("review")),
            Repos = repos,
        };
    }
}

/// <summary>Web links: only <c>https://github.com/...</c> is ever opened (spec 06 section 2.4).</summary>
internal static class GitHubLinks
{
    public const string Notifications = "https://github.com/notifications";

    private static readonly Regex PullApi = new(@"/repos/([^/]+)/([^/]+)/pulls/(\d+)$", RegexOptions.Compiled);
    private static readonly Regex IssueApi = new(@"/repos/([^/]+)/([^/]+)/issues/(\d+)$", RegexOptions.Compiled);
    private static readonly Regex ReleaseApi = new(@"/repos/([^/]+)/([^/]+)/releases/\d+$", RegexOptions.Compiled);

    public static bool IsSafe(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "github.com";

    /// <summary>Maps a notification's API subject url to the web page, or the inbox.</summary>
    public static string FromApi(string? apiUrl)
    {
        if (apiUrl is null)
        {
            return Notifications;
        }

        if (PullApi.Match(apiUrl) is { Success: true } p)
        {
            return $"https://github.com/{p.Groups[1].Value}/{p.Groups[2].Value}/pull/{p.Groups[3].Value}";
        }

        if (IssueApi.Match(apiUrl) is { Success: true } i)
        {
            return $"https://github.com/{i.Groups[1].Value}/{i.Groups[2].Value}/issues/{i.Groups[3].Value}";
        }

        if (ReleaseApi.Match(apiUrl) is { Success: true } r)
        {
            return $"https://github.com/{r.Groups[1].Value}/{r.Groups[2].Value}/releases";
        }

        return Notifications;
    }

    /// <summary>Opens a safe link in the default browser; anything else is dropped.</summary>
    public static void Open(string? url)
    {
        if (!IsSafe(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url!) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // no default browser: nothing to report to
        }
    }
}

/// <summary>Relative time strings of the page.</summary>
internal static class GitHubTime
{
    public static string Relative(DateTime utc, DateTime? now = null)
    {
        var span = (now ?? DateTime.UtcNow) - utc;
        if (span.TotalMinutes < 1)
        {
            return GhText.JustNow;
        }

        if (span.TotalHours < 1)
        {
            return GhText.MinutesAgo((int)span.TotalMinutes);
        }

        return span.TotalDays < 1 ? GhText.HoursAgo((int)span.TotalHours) : GhText.DaysAgo((int)span.TotalDays);
    }

    /// <summary>The same numbers without "ago" (release captions).</summary>
    public static string Short(DateTime utc)
    {
        var span = DateTime.UtcNow - utc;
        if (span.TotalMinutes < 1)
        {
            return GhText.JustNow;
        }

        if (span.TotalHours < 1)
        {
            return (int)span.TotalMinutes + " min";
        }

        return span.TotalDays < 1 ? (int)span.TotalHours + " h" : (int)span.TotalDays + " d";
    }

    public static string Clock(DateTime utc) => utc.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);
}
