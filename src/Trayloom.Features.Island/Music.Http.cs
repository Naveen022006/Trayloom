// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace Trayloom.Features.Island;

/// <summary>The HttpClient and per-request timeout shared by the music services.</summary>
internal static class ApiHttp
{
    /// <summary>The time one request may take before it is abandoned.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Creates a client: pooled connections recycled every 5 minutes, no redirects, no cookies, no overall timeout (use <see cref="Timeout"/>).</summary>
    public static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(10),
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
        };
        var http = new HttpClient(handler, disposeHandler: true) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Trayloom/0.2 (https://github.com/Naveen022006/Trayloom)");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return http;
    }

    /// <summary>A token source that cancels after <see cref="RequestTimeout"/> or when <paramref name="ct"/> does; dispose it after the request.</summary>
    public static CancellationTokenSource Timeout(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(RequestTimeout);
        return cts;
    }
}

/// <summary>Pure rules for turning an HTTP failure into a <see cref="StreamStatus"/>.</summary>
internal static class ApiError
{
    /// <summary>Classifies a non-success response. A 401 is <see cref="StreamStatus.Revoked"/>; the caller refreshes the token first.</summary>
    public static StreamStatus Classify(StreamKind kind, int status, string? body)
    {
        var text = body ?? "";
        if (status == 401)
        {
            return StreamStatus.Revoked;
        }

        if (kind == StreamKind.Spotify)
        {
            if (status == 403)
            {
                if (Has(text, "PREMIUM_REQUIRED"))
                {
                    return StreamStatus.PremiumRequired;
                }

                if (Has(text, "not registered") || Has(text, "Developer Dashboard"))
                {
                    return StreamStatus.NotAllowlisted;
                }
            }
            else if (status == 404 && Has(text, "NO_ACTIVE_DEVICE"))
            {
                return StreamStatus.NoDevice;
            }
            else if (status == 429)
            {
                return Has(text, "QUOTA_EXCEEDED") ? StreamStatus.QuotaExceeded : StreamStatus.RateLimited;
            }

            return StreamStatus.Failed;
        }

        if (status is 400 or 403 or 429)
        {
            if (Has(text, "API_KEY_INVALID") || Has(text, "keyInvalid") || Has(text, "keyExpired"))
            {
                return StreamStatus.InvalidKey;
            }

            if (Has(text, "quotaExceeded") || Has(text, "dailyLimitExceeded"))
            {
                return StreamStatus.QuotaExceeded;
            }

            if (Has(text, "accessNotConfigured") || Has(text, "SERVICE_DISABLED"))
            {
                return StreamStatus.ApiDisabled;
            }
        }

        return StreamStatus.Failed;
    }

    /// <summary>Reads a Retry-After header (seconds or an HTTP date); 5 s when missing or unreadable, clamped to 1..120 s.</summary>
    public static TimeSpan RetryAfter(string? header) => RetryAfter(header, DateTimeOffset.UtcNow);

    /// <summary>As <see cref="RetryAfter(string?)"/> with an explicit clock.</summary>
    public static TimeSpan RetryAfter(string? header, DateTimeOffset now)
    {
        var seconds = 5.0;
        var text = header?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
            {
                seconds = s;
            }
            else if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            {
                seconds = (date - now).TotalSeconds;
            }
        }

        if (double.IsNaN(seconds))
        {
            seconds = 5;
        }

        return TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 120));
    }

    /// <summary>True when the failure means there is no network (a socket error underneath, or no adapter is up).</summary>
    public static bool IsOffline(Exception ex)
    {
        if (ex is HttpRequestException && ex.InnerException is SocketException)
        {
            return true;
        }

        return ex is HttpRequestException && !NetworkInterface.GetIsNetworkAvailable();
    }

    private static bool Has(string body, string needle) => body.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
