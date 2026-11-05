// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Trayloom.Core.Updates;

/// <summary>How a request for the release list ended.</summary>
public enum FetchStatus
{
    /// <summary>200: <see cref="FetchResult.Json"/> holds the list.</summary>
    Ok,

    /// <summary>304: the cached list is still current.</summary>
    NotModified,

    /// <summary>403/429 with the rate limit used up: wait until <see cref="FetchResult.RetryAtUtc"/>.</summary>
    RateLimited,

    /// <summary>No connection, a timeout or a TLS failure.</summary>
    NoConnection,

    /// <summary>Any other answer (including a redirect, which is never followed) or a body that is too large.</summary>
    HttpError,
}

/// <summary>The outcome of <see cref="UpdateChecker.FetchAsync"/>.</summary>
public sealed record FetchResult(FetchStatus Status, string? Json = null, string? ETag = null, int HttpStatus = 0, DateTime? RetryAtUtc = null);

/// <summary>
/// The one HTTPS request of the update notifier: <c>GET /repos/Naveen022006/Trayloom/releases</c>. It reads the answer into memory
/// (at most 1 MB), never follows a redirect and never downloads or runs anything.
/// </summary>
public sealed class UpdateChecker : IDisposable
{
    /// <summary>The repository the notifier asks about.</summary>
    public const string RepoUrl = "https://github.com/Naveen022006/Trayloom";

    /// <summary>The API address of the release list.</summary>
    public const string ApiUrl = "https://api.github.com/repos/Naveen022006/Trayloom/releases?per_page=20";

    /// <summary>The largest answer accepted.</summary>
    public const int MaxBytes = 1024 * 1024;

    private readonly HttpClient _http;
    private readonly string _userAgent;
    private readonly TimeSpan _timeout;
    private readonly CancellationTokenSource _life = new();

    /// <summary>Creates a checker. <paramref name="handler"/> is injectable for tests; the default never follows redirects.</summary>
    public UpdateChecker(string appVersion, HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        _userAgent = "Trayloom/" + appVersion;
        _timeout = timeout ?? TimeSpan.FromSeconds(15);
        _http = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false }, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan, // the whole request, body included, is bounded below instead
        };
    }

    /// <summary>
    /// Asks GitHub for the release list. Never throws. The whole request, headers and body, ends within the timeout (15 s by default),
    /// even when the server stalls or the stream ignores cancellation; disposing the checker cancels it too.
    /// </summary>
    public async Task<FetchResult> FetchAsync(string? etag, CancellationToken cancel = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancel, _life.Token);
            cts.CancelAfter(_timeout);
            var token = cts.Token;
            using var request = new HttpRequestMessage(HttpMethod.Get, ApiUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.ParseAdd(_userAgent);
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
            if (!string.IsNullOrEmpty(etag))
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            }

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).WaitAsync(token).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return new FetchResult(FetchStatus.NotModified, ETag: etag, HttpStatus: status);
            }

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests && IsRateLimited(response, out var retryAt))
            {
                return new FetchResult(FetchStatus.RateLimited, HttpStatus: status, RetryAtUtc: retryAt);
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return new FetchResult(FetchStatus.HttpError, HttpStatus: status);
            }

            if (response.Content.Headers.ContentLength is > MaxBytes)
            {
                return new FetchResult(FetchStatus.HttpError, HttpStatus: status);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(token).WaitAsync(token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, token).AsTask().WaitAsync(token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > MaxBytes)
                {
                    return new FetchResult(FetchStatus.HttpError, HttpStatus: status);
                }

                buffer.Write(chunk, 0, read);
            }

            var newTag = response.Headers.ETag?.ToString();
            return new FetchResult(FetchStatus.Ok, System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length), newTag, status);
        }
        catch (OperationCanceledException)
        {
            return new FetchResult(FetchStatus.NoConnection);
        }
        catch (HttpRequestException)
        {
            return new FetchResult(FetchStatus.NoConnection);
        }
        catch (Exception)
        {
            return new FetchResult(FetchStatus.NoConnection);
        }
    }

    private static bool IsRateLimited(HttpResponseMessage response, out DateTime? retryAt)
    {
        retryAt = null;
        var now = DateTime.UtcNow;
        var limited = response.StatusCode == HttpStatusCode.TooManyRequests;
        if (response.Headers.RetryAfter is { } after)
        {
            limited = true;
            retryAt = after.Delta is { } delta ? now + delta : after.Date?.UtcDateTime;
        }

        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.FirstOrDefault() == "0")
        {
            limited = true;
            if (retryAt is null && response.Headers.TryGetValues("X-RateLimit-Reset", out var reset)
                && long.TryParse(reset.FirstOrDefault(), out var epoch) && epoch > 0 && epoch < 4_000_000_000)
            {
                retryAt = DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
            }
        }

        if (limited)
        {
            // never wait longer than a day, and at least a minute so a bad header cannot make a busy loop
            var latest = now.AddHours(24);
            var earliest = now.AddMinutes(1);
            var at = retryAt ?? now.AddHours(1);
            retryAt = at > latest ? latest : at < earliest ? earliest : at;
        }

        return limited;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            _life.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // already disposed
        }

        _http.Dispose();
        _life.Dispose();
    }
}
