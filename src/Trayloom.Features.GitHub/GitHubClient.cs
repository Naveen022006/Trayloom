// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Trayloom.Features.GitHub;

/// <summary>One HTTP answer, whichever way it came (gh or HttpClient).</summary>
internal sealed record RawResponse(int Status, string Body, string? Etag, int? PollSeconds, DateTime? RetryAt, bool RateExhausted, string? Scopes = null);

/// <summary>A transport failure that already says which <see cref="GhError"/> it is.</summary>
internal sealed class GhTransportException(GhError error) : Exception(error.ToString())
{
    public GhError Error { get; } = error;
}

/// <summary>The two requests of a poll, sent through gh or with a token.</summary>
internal interface IGhTransport
{
    Task<RawResponse> NotificationsAsync(string? etag, CancellationToken ct);

    Task<RawResponse> GraphQlAsync(string query, bool withRepoCount, CancellationToken ct);
}

/// <summary>Reads the rate-limit and polling headers into a <see cref="RawResponse"/>.</summary>
internal static class GhHeaders
{
    public static RawResponse Build(int status, string body, Func<string, string?> header)
    {
        int? poll = int.TryParse(header("X-Poll-Interval"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : null;
        DateTime? retry = null;
        if (int.TryParse(header("Retry-After"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var after))
        {
            retry = DateTime.UtcNow.AddSeconds(after);
        }
        else if (long.TryParse(header("X-RateLimit-Reset"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var reset))
        {
            retry = DateTimeOffset.FromUnixTimeSeconds(reset).UtcDateTime;
        }

        var exhausted = header("X-RateLimit-Remaining") == "0" || header("Retry-After") is not null;
        return new RawResponse(status, body, header("ETag"), poll, retry, exhausted);
    }
}

/// <summary>Requests through the signed-in GitHub CLI.</summary>
internal sealed class GhCliTransport : IGhTransport
{
    private static async Task<RawResponse> SendAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        var result = await GhCli.RunAsync(args, ct).ConfigureAwait(false);
        if (result.StartFailed)
        {
            throw new GhTransportException(GhError.GhMissing);
        }

        if (result.TimedOut || result.TooLarge)
        {
            throw new GhTransportException(GhError.Unexpected);
        }

        var http = GhCli.ParseHttp(result.Stdout);
        if (http is null)
        {
            if (result.SignedOut)
            {
                throw new GhTransportException(GhError.SignedOut);
            }

            var err = result.Stderr;
            var offline = err.Contains("error connecting", StringComparison.OrdinalIgnoreCase) || err.Contains("dial tcp", StringComparison.OrdinalIgnoreCase)
                || err.Contains("no such host", StringComparison.OrdinalIgnoreCase) || err.Contains("timeout", StringComparison.OrdinalIgnoreCase);
            throw new GhTransportException(offline ? GhError.Offline : GhError.Unexpected);
        }

        return GhHeaders.Build(http.Status, http.Body, http.Header);
    }

    public Task<RawResponse> NotificationsAsync(string? etag, CancellationToken ct)
    {
        var args = new List<string> { "api", "--hostname", "github.com", "-i" };
        if (etag is not null)
        {
            args.Add("-H");
            args.Add("If-None-Match: " + etag);
        }

        args.Add("notifications?per_page=50");
        return SendAsync(args, ct);
    }

    public Task<RawResponse> GraphQlAsync(string query, bool withRepoCount, CancellationToken ct)
    {
        var args = new List<string> { "api", "--hostname", "github.com", "-i", "graphql", "-f", "query=" + query };
        if (withRepoCount)
        {
            args.Add("-F");
            args.Add("repoCount=5");
        }

        return SendAsync(args, ct);
    }
}

/// <summary>Requests with a personal access token; the token is decrypted per request and only ever sent to api.github.com.</summary>
internal sealed class TokenTransport(Func<string?> tokenProvider) : IGhTransport
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };

    static TokenTransport()
    {
        var version = typeof(TokenTransport).Assembly.GetName().Version?.ToString(3) ?? "0";
        Http.DefaultRequestHeaders.UserAgent.ParseAdd($"Trayloom/{version} (+https://github.com/Naveen022006/Trayloom)");
    }

    /// <summary>Sends one request with the bearer token set on the message itself (never on the shared defaults).</summary>
    public static async Task<RawResponse> SendAsync(string token, HttpMethod method, string path, string? json, string? etag, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, "https://api.github.com/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (etag is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }

        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        try
        {
            using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            string? Header(string name)
            {
                if (response.Headers.TryGetValues(name, out var v) || response.Content.Headers.TryGetValues(name, out v))
                {
                    return v.FirstOrDefault();
                }

                return null;
            }

            var raw = GhHeaders.Build((int)response.StatusCode, body, Header);
            if (raw.Status is >= 300 and < 400 && raw.Status != 304)
            {
                throw new GhTransportException(GhError.Unexpected);
            }

            return raw with { Scopes = Header("X-OAuth-Scopes") };
        }
        catch (HttpRequestException)
        {
            throw new GhTransportException(GhError.Offline);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new GhTransportException(GhError.Offline);
        }
    }

    public async Task<RawResponse> NotificationsAsync(string? etag, CancellationToken ct)
    {
        var token = tokenProvider() ?? throw new GhTransportException(GhError.TokenRejected);
        return await SendAsync(token, HttpMethod.Get, "notifications?per_page=50", null, etag, ct).ConfigureAwait(false);
    }

    public async Task<RawResponse> GraphQlAsync(string query, bool withRepoCount, CancellationToken ct)
    {
        var token = tokenProvider() ?? throw new GhTransportException(GhError.TokenRejected);
        var payload = withRepoCount ? new { query, variables = new { repoCount = 5 } } : (object)new { query };
        return await SendAsync(token, HttpMethod.Post, "graphql", JsonSerializer.Serialize(payload), null, ct).ConfigureAwait(false);
    }
}

/// <summary>What one poll produced.</summary>
internal sealed record PollOutcome(GitHubSnapshot? Snapshot, GhError Error, DateTime? RetryAt, string? Etag, int? PollSeconds, int NotificationsStatus, int GraphQlStatus, int Cached, int Cost, int Remaining, DateTime? ResetAt);

/// <summary>Runs the two requests of a poll and maps their answers (spec 06 section 2.4).</summary>
internal sealed class GitHubClient(IGhTransport transport, bool viaCli)
{
    private GhError AuthError => viaCli ? GhError.SignedOut : GhError.TokenRejected;

    public async Task<PollOutcome> PollAsync(string? etag, IReadOnlyList<string> repos, GitHubSnapshot? prior, CancellationToken ct)
    {
        var basis = prior ?? new GitHubSnapshot();
        string? newEtag = etag;
        int? pollSeconds = null;
        var notifications = basis.Notifications;
        var available = true;
        RawResponse n;
        try
        {
            n = await transport.NotificationsAsync(etag, ct).ConfigureAwait(false);
        }
        catch (GhTransportException ex)
        {
            return Failed(ex.Error, null, 0, 0);
        }

        pollSeconds = n.PollSeconds;
        switch (n.Status)
        {
            case 200:
                try
                {
                    notifications = GitHubParse.Notifications(n.Body);
                }
                catch (JsonException)
                {
                    return Failed(GhError.Unexpected, null, n.Status, 0);
                }

                newEtag = n.Etag;
                break;
            case 304:
                break;
            case 401:
                return Failed(AuthError, null, n.Status, 0);
            case 403 or 429:
                if (n.RateExhausted)
                {
                    return Failed(GhError.RateLimited, n.RetryAt, n.Status, 0);
                }

                // Fine-grained tokens cannot read notifications: the rest still works.
                available = false;
                notifications = [];
                newEtag = null;
                break;
            default:
                return Failed(GhError.Unexpected, null, n.Status, 0);
        }

        RawResponse g;
        try
        {
            g = await transport.GraphQlAsync(GitHubQuery.Build(repos), repos.Count == 0, ct).ConfigureAwait(false);
        }
        catch (GhTransportException ex)
        {
            return Failed(ex.Error, null, n.Status, 0);
        }

        if (g.Status == 401)
        {
            return Failed(AuthError, null, n.Status, g.Status);
        }

        if (g.Status is 403 or 429)
        {
            return Failed(g.RateExhausted ? GhError.RateLimited : GhError.Unexpected, g.RetryAt, n.Status, g.Status);
        }

        if (g.Status != 200)
        {
            return Failed(GhError.Unexpected, null, n.Status, g.Status);
        }

        try
        {
            using var doc = JsonDocument.Parse(g.Body);
            var root = doc.RootElement;
            var data = root.Obj("data");
            if (data.ValueKind != JsonValueKind.Object)
            {
                var rate = root.TryGetProperty("errors", out var errs) && errs.ValueKind == JsonValueKind.Array
                    && errs.EnumerateArray().Any(e => e.StrOrNull("type") == "RATE_LIMITED");
                return Failed(rate ? GhError.RateLimited : GhError.Unexpected, null, n.Status, g.Status);
            }

            var partial = root.TryGetProperty("errors", out var partialErrors) && partialErrors.ValueKind == JsonValueKind.Array ? partialErrors.GetArrayLength() : 0;
            var snapshot = GitHubParse.GraphQl(data, basis, repos, out var cost, out var remaining, out var resetAt) with
            {
                FetchedAt = DateTime.UtcNow,
                Notifications = notifications,
                NotificationsAvailable = available,
                Error = GhError.None,
                RetryAt = null,
            };
            if (partial > 0)
            {
                PartialErrors = partial;
            }

            return new PollOutcome(snapshot, GhError.None, null, newEtag, pollSeconds, n.Status, g.Status, n.Status == 304 ? notifications.Count : 0, cost, remaining, resetAt);
        }
        catch (JsonException)
        {
            return Failed(GhError.Unexpected, null, n.Status, g.Status);
        }
    }

    /// <summary>How many GraphQL errors came with data in the last poll (logged by the runtime).</summary>
    public int PartialErrors { get; private set; }

    private static PollOutcome Failed(GhError error, DateTime? retryAt, int nStatus, int gStatus) =>
        new(null, error, retryAt, null, null, nStatus, gStatus, 0, 0, int.MaxValue, null);
}
