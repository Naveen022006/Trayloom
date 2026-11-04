// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using Trayloom.Core;

namespace Trayloom.Features.Island;

/// <summary>The outcome of one InnerTube call: the status, the body of a 200, and for a 429 how long to wait.</summary>
internal sealed record InnerTubeResult(StreamStatus Status, string Body, TimeSpan? RetryIn);

/// <summary>
/// Talks to YouTube Music's own web service (<c>music.youtube.com/youtubei/v1</c>, "InnerTube"), the one the site itself calls, so search
/// works with no key and no account. It is unofficial and may break whenever YouTube changes it. It sends no cookies and no sign-in,
/// presents a desktop browser (the service answers browsers only) and reads the web client's version from the site's page once per
/// session. Logs name only the endpoint, the status and the time, never the query. Used from the UI thread or a task it started.
/// </summary>
internal sealed class InnerTubeClient : IDisposable
{
    private const string Origin = "https://music.youtube.com";
    private const string Browser = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";
    private const int MaxPageBytes = 4 * 1024 * 1024;

    private readonly ILog _log;
    private readonly string _region;
    private readonly SemaphoreSlim _versionGate = new(1, 1);
    private HttpClient? _http;
    private string? _version;
    private bool _versionRead;
    private bool _disposed;

    public InnerTubeClient(ILog log)
    {
        _log = log;
        _region = InnerTubeMap.Region(RegionName());
    }

    /// <summary>The client version in use: the one read from the site, else <see cref="InnerTubeMap.FallbackVersion"/>.</summary>
    public string Version => _version ?? InnerTubeMap.FallbackVersion;

    /// <summary>The country sent as <c>gl</c>.</summary>
    public string Region => _region;

    private HttpClient Http
    {
        get
        {
            if (_http is null)
            {
                // The shared client carries Trayloom's own user agent: this one presents a browser, for these calls only.
                var http = ApiHttp.Create();
                http.DefaultRequestHeaders.UserAgent.Clear();
                http.DefaultRequestHeaders.UserAgent.ParseAdd(Browser);
                http.DefaultRequestHeaders.TryAddWithoutValidation("Origin", Origin);
                http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", Origin + "/");
                _http = http;
            }

            return _http;
        }
    }

    /// <summary>
    /// One POST to <c>/youtubei/v1/{endpoint}</c>. <paramref name="body"/> makes the JSON for a client version. A 400 usually means the
    /// version is too old: it is read from the site again and the call repeated once. A 5xx or a timeout is repeated once after a second.
    /// </summary>
    public async Task<InnerTubeResult> PostAsync(string endpoint, Func<string, string> body, CancellationToken ct)
    {
        var refreshed = false;
        var retried = false;
        while (true)
        {
            var version = await VersionAsync(refresh: false, ct);
            var watch = Stopwatch.StartNew();
            try
            {
                using var timeout = ApiHttp.Timeout(ct);
                using var message = new HttpRequestMessage(HttpMethod.Post, $"{Origin}/youtubei/v1/{endpoint}?prettyPrint=false")
                {
                    Content = new StringContent(body(version), Encoding.UTF8, "application/json"),
                };
                using var response = await Http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                var text = await response.Content.ReadAsStringAsync(timeout.Token);
                var code = (int)response.StatusCode;
                _log.Info($"[island] YouTube Music {endpoint} -> {code} ({watch.ElapsedMilliseconds} ms)");
                if (response.IsSuccessStatusCode)
                {
                    return new InnerTubeResult(StreamStatus.Ok, text, null);
                }

                if (code == 400 && !refreshed)
                {
                    refreshed = true;
                    await VersionAsync(refresh: true, ct);
                    continue;
                }

                if (code >= 500 && !retried)
                {
                    retried = true;
                    await Task.Delay(TimeSpan.FromSeconds(1), ct);
                    continue;
                }

                if (code == 429)
                {
                    var header = response.Headers.TryGetValues("Retry-After", out var values) ? values.FirstOrDefault() : null;
                    return new InnerTubeResult(StreamStatus.RateLimited, "", ApiError.RetryAfter(header));
                }

                return new InnerTubeResult(StreamStatus.Failed, "", null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return new InnerTubeResult(StreamStatus.Cancelled, "", null);
            }
            catch (OperationCanceledException)
            {
                _log.Info($"[island] YouTube Music {endpoint} timed out ({watch.ElapsedMilliseconds} ms)");
                if (!retried)
                {
                    retried = true;
                    continue;
                }

                return new InnerTubeResult(StreamStatus.TimedOut, "", null);
            }
            catch (HttpRequestException ex)
            {
                var offline = ApiError.IsOffline(ex);
                _log.Info($"[island] YouTube Music {endpoint} failed ({(offline ? "offline" : "error")}, {watch.ElapsedMilliseconds} ms)");
                if (offline)
                {
                    return new InnerTubeResult(StreamStatus.Offline, "", null);
                }

                if (!retried)
                {
                    retried = true;
                    await Task.Delay(TimeSpan.FromSeconds(1), ct);
                    continue;
                }

                return new InnerTubeResult(StreamStatus.Failed, "", null);
            }
        }
    }

    /// <summary>
    /// The client version: read from the site's page the first time (and again when <paramref name="refresh"/> is set), with
    /// <see cref="InnerTubeMap.FallbackVersion"/> when the page cannot be read. Never throws for a network problem.
    /// </summary>
    private async Task<string> VersionAsync(bool refresh, CancellationToken ct)
    {
        await _versionGate.WaitAsync(ct);
        try
        {
            if (_versionRead && !refresh)
            {
                return Version;
            }

            _versionRead = true;
            var watch = Stopwatch.StartNew();
            try
            {
                using var timeout = ApiHttp.Timeout(ct);
                using var message = new HttpRequestMessage(HttpMethod.Get, Origin + "/");
                message.Headers.Accept.ParseAdd("text/html");
                message.Headers.TryAddWithoutValidation("Accept-Language", "en");
                using var response = await Http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                _log.Info($"[island] YouTube Music page -> {(int)response.StatusCode} ({watch.ElapsedMilliseconds} ms)");
                if (response.IsSuccessStatusCode && response.Content.Headers.ContentLength is null or <= MaxPageBytes)
                {
                    var found = InnerTubeMap.ClientVersion(await response.Content.ReadAsStringAsync(timeout.Token));
                    if (found is not null)
                    {
                        _version = found;
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException)
            {
                _log.Info($"[island] YouTube Music page failed ({watch.ElapsedMilliseconds} ms)");
            }

            return Version;
        }
        finally
        {
            _versionGate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _http?.Dispose();
        _versionGate.Dispose();
    }

    private static string? RegionName()
    {
        try
        {
            return RegionInfo.CurrentRegion.TwoLetterISORegionName;
        }
        catch (Exception ex) when (ex is ArgumentException or CultureNotFoundException)
        {
            return null;
        }
    }
}
