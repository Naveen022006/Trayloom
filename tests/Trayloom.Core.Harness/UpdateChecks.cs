// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Net;
using System.Net.Http;
using System.Text;
using Trayloom.Core.Updates;

namespace Trayloom.Core.Harness;

/// <summary>
/// Checks of the update notifier's pure parts: version parsing and precedence, picking the newest release from canned JSON, and the
/// HTTP request with a fake handler (200, 304, rate limit, 500, redirect, timeout, oversized body). No network is used.
/// </summary>
internal static class UpdateChecks
{
    private const string Repo = "https://github.com/Naveen022006/Trayloom";

    private static Action<bool, string> _check = (_, _) => { };

    public static void Run(Action<bool, string> check)
    {
        _check = check;
        Versions();
        Picking();
        Requests().GetAwaiter().GetResult();
    }

    private static void Check(bool ok, string what) => _check(ok, "updates: " + what);

    private static ReleaseVersion V(string text)
    {
        Check(ReleaseVersion.TryParse(text, out var v), "parses " + text);
        return v;
    }

    private static void Versions()
    {
        Check(V("0.5.0") < V("0.5.1"), "0.5.0 < 0.5.1");
        Check(V("0.5.1-beta.1") < V("0.5.1"), "0.5.1-beta.1 < 0.5.1");
        Check(V("1.0.0-beta.2") < V("1.0.0-beta.11"), "beta.2 < beta.11 (numeric)");
        Check(V("1.0.0-alpha") < V("1.0.0-beta"), "alpha < beta");
        Check(V("1.0.0-rc.1") < V("1.0.0"), "1.0.0-rc.1 < 1.0.0");
        Check(V("1.0.0-alpha") < V("1.0.0-alpha.1"), "a shorter prefix list ranks lower");
        Check(V("1.0.0-1") < V("1.0.0-alpha"), "numbers rank below text");
        Check(V("v1.2.3+build") == V("1.2.3"), "v1.2.3+build equals 1.2.3");
        Check(V("0.10.0") > V("0.9.9"), "0.10.0 > 0.9.9");
        Check(V("2.0.0-beta.1").IsPrerelease && !V("2.0.0").IsPrerelease, "IsPrerelease");
        foreach (var bad in new[] { "01.2.3", "1.2", "", "1.2.3.4", "v", "1.2.3-", "1.2.3-01", "x.y.z", "1.2.3 ", new string('1', 70), "99999999999.0.0" })
        {
            Check(!ReleaseVersion.TryParse(bad, out _), "rejects '" + (bad.Length > 20 ? bad[..20] + "..." : bad) + "'");
        }

        Check(!ReleaseVersion.TryParse(null, out _), "rejects null");
    }

    private const string Canned = """
        [
          {"tag_name":"v0.7.0-beta.1","draft":false,"prerelease":true,"html_url":"https://evil.example/x","body":"ignored"},
          {"tag_name":"v0.9.0","draft":true,"prerelease":false},
          {"tag_name":"v0.6.0","draft":false,"prerelease":false,"html_url":"https://evil.example/y"},
          {"tag_name":"nightly","draft":false,"prerelease":false},
          {"tag_name":"v0.5.0","draft":false,"prerelease":false},
          {"tag_name":"v0.8.0-rc.1","draft":false,"prerelease":false},
          {"tag_name":"v0.4.0/../../x","draft":false,"prerelease":false},
          {"name":"no tag"},
          42
        ]
        """;

    private static void Picking()
    {
        var all = ReleasePicker.Parse(Canned, Repo);
        Check(all.Count == 4, "parse skips drafts, unparseable tags and junk (" + all.Count + " kept)");
        Check(all.All(r => r.Url.StartsWith(Repo + "/releases/tag/", StringComparison.Ordinal)), "every URL is built from the tag on the repository");
        Check(!all.Any(r => r.Url.Contains("evil.example")), "no URL is taken from the answer");
        Check(all.Single(r => r.Tag == "v0.8.0-rc.1").Prerelease, "a -suffix tag counts as a prerelease even when the flag is false");
        Check(ReleasePicker.Parse("{}", Repo).Count == 0 && ReleasePicker.Parse("not json", Repo).Count == 0 && ReleasePicker.Parse("", Repo).Count == 0, "non-array answers give nothing");

        var current = V("0.5.0");
        Check(ReleasePicker.Newest(all, current, false)?.Tag == "v0.6.0", "stable users get the newest stable release");
        Check(ReleasePicker.Newest(all, current, true)?.Tag == "v0.8.0-rc.1", "beta users get the newest release including prereleases");
        Check(ReleasePicker.Newest(all, V("0.6.0"), false) is null, "nothing newer: no update");
        Check(ReleasePicker.Newest(all, V("0.6.0-beta.1"), false)?.Tag == "v0.6.0", "a beta of 0.6.0 is offered the stable 0.6.0");
        Check(ReleasePicker.Newest(all, V("0.9.0-beta.1"), true) is null, "a stable older release is not offered to someone on a newer beta");
        Check(ReleasePicker.FromTag("v1.0.0 /x", false, Repo) is null, "a tag with spaces is not a version");
        Check(ReleasePicker.FromTag("v1.0.0-beta.1", false, Repo)!.Url == Repo + "/releases/tag/v1.0.0-beta.1", "the URL carries the escaped tag");
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }

        public static CancellationToken Token { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            Token = cancellationToken;
            return await respond(request).ConfigureAwait(false);
        }
    }

    private static HttpResponseMessage Json(string body, string? etag = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (etag is not null)
        {
            response.Headers.TryAddWithoutValidation("ETag", etag);
        }

        return response;
    }

    private static async Task<FetchResult> Fetch(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond, string? etag = null, TimeSpan? timeout = null)
    {
        var handler = new FakeHandler(respond);
        using var checker = new UpdateChecker("0.5.0", handler, timeout);
        var result = await checker.FetchAsync(etag);
        lastRequest = handler.Last;
        return result;
    }

    private static HttpRequestMessage? lastRequest;

    private static async Task Requests()
    {
        var ok = await Fetch(_ => Task.FromResult(Json("[]", "\"abc\"")));
        Check(ok.Status == FetchStatus.Ok && ok.ETag == "\"abc\"" && ok.Json == "[]", "200 gives the body and the ETag");
        Check(lastRequest!.RequestUri!.ToString() == "https://api.github.com/repos/Naveen022006/Trayloom/releases?per_page=20", "the request goes to the GitHub API over https for this repository");
        Check(lastRequest.Headers.UserAgent.ToString().StartsWith("Trayloom/0.5.0", StringComparison.Ordinal) && lastRequest.Headers.Accept.ToString().Contains("vnd.github+json"), "User-Agent and Accept headers are set");
        Check(lastRequest.Headers.Contains("X-GitHub-Api-Version") && lastRequest.Method == HttpMethod.Get, "GET with the API version header");

        var notModified = await Fetch(r => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified)), "\"abc\"");
        Check(notModified.Status == FetchStatus.NotModified, "304 means the cache is current");
        Check(lastRequest!.Headers.TryGetValues("If-None-Match", out var inm) && inm.Single() == "\"abc\"", "the ETag is sent as If-None-Match");

        var limited = await Fetch(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
            response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
            response.Headers.TryAddWithoutValidation("X-RateLimit-Reset", DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds().ToString());
            return Task.FromResult(response);
        });
        Check(limited.Status == FetchStatus.RateLimited && limited.RetryAtUtc is { } at && at > DateTime.UtcNow.AddMinutes(25) && at < DateTime.UtcNow.AddMinutes(35), "403 with no calls left is a rate limit until the reset time");

        var far = await Fetch(_ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)429);
            response.Headers.TryAddWithoutValidation("Retry-After", "999999999");
            return Task.FromResult(response);
        });
        Check(far.Status == FetchStatus.RateLimited && far.RetryAtUtc is { } far2 && far2 <= DateTime.UtcNow.AddHours(24).AddSeconds(5), "the back-off is capped at 24 hours");

        var plain403 = await Fetch(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)));
        Check(plain403.Status == FetchStatus.HttpError && plain403.HttpStatus == 403, "a plain 403 is an HTTP error, not a rate limit");

        var error = await Fetch(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        Check(error.Status == FetchStatus.HttpError && error.HttpStatus == 500, "500 is an HTTP error");

        var redirect = await Fetch(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
            response.Headers.Location = new Uri("https://evil.example/");
            return Task.FromResult(response);
        });
        Check(redirect.Status == FetchStatus.HttpError && redirect.HttpStatus == 301, "a 301 fails and is not followed");

        var huge = await Fetch(_ => Task.FromResult(Json(new string('x', 2 * 1024 * 1024))));
        Check(huge.Status == FetchStatus.HttpError, "a 2 MB body is rejected");

        var offline = await Fetch(_ => throw new HttpRequestException("no route"));
        Check(offline.Status == FetchStatus.NoConnection, "a network failure is NoConnection");

        var slow = await Fetch(async _ =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), FakeHandler.Token);
            return Json("[]");
        }, timeout: TimeSpan.FromMilliseconds(200));
        Check(slow.Status == FetchStatus.NoConnection, "a timeout is NoConnection");

        // Headers arrive at once, then the body never does (a stream that even ignores cancellation): the whole request still ends.
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var stalled = await Fetch(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) };
            return Task.FromResult(response);
        }, timeout: TimeSpan.FromMilliseconds(400));
        watch.Stop();
        Check(stalled.Status == FetchStatus.NoConnection && watch.Elapsed < TimeSpan.FromSeconds(3), "a stalled body ends as NoConnection within the bound (" + watch.ElapsedMilliseconds + " ms)");
    }

    /// <summary>A body that never delivers a byte and ignores cancellation.</summary>
    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => new(new TaskCompletionSource<int>().Task);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
