// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text;
using Trayloom.Features.Island;

namespace Trayloom.Island.Harness;

/// <summary>
/// Checks of the shared infrastructure of the Spotify and YouTube Music browser: sign-in storage, HTTP error rules, the answer cache and the
/// artwork host list. No network. Later packages add their own groups (PKCE, OAuth callback) to <see cref="Run"/>.
/// </summary>
internal static class MusicCoreChecks
{
    private static Action<bool, string> _check = (_, _) => { };

    public static void Run(Action<bool, string> check)
    {
        _check = check;
        Tokens();
        Secrets();
        Classify();
        RetryAfter();
        Cache();
        ArtHosts();
        PkceChecks();
        CallbackParsing();
        CallbackPage();
        Loopback().GetAwaiter().GetResult();
    }

    /// <summary>The redirect listener on an ephemeral loopback port: only this machine is involved, nothing leaves it.</summary>
    private static async Task Loopback()
    {
        await using var redirect = LoopbackRedirect.Start(0);
        var port = redirect.LocalPort;
        var busy = false;
        try
        {
            await using var second = LoopbackRedirect.Start(port);
        }
        catch (PortBusyException)
        {
            busy = true;
        }

        Check(busy, "loopback: a second listener on the same port reports PortBusy");

        var wait = redirect.WaitAsync(TimeSpan.FromSeconds(20), CancellationToken.None);
        var stray = await Exchange(port, "GET /favicon.ico HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n");
        Check(stray.StartsWith("HTTP/1.1 404", StringComparison.Ordinal) && !wait.IsCompleted, "loopback: another path gets 404 and the wait goes on");
        var post = await Exchange(port, "POST /callback?code=c&state=s HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n");
        Check(post.StartsWith("HTTP/1.1 404", StringComparison.Ordinal) && !wait.IsCompleted, "loopback: a POST to the callback gets 404");

        using (var big = new System.Net.Sockets.TcpClient())
        {
            await big.ConnectAsync(System.Net.IPAddress.Loopback, port);
            var stream = big.GetStream();
            try
            {
                await stream.WriteAsync(Encoding.ASCII.GetBytes("GET /callback?code=c&state=s HTTP/1.1\r\nX-Pad: " + new string('a', 9000)));
            }
            catch (IOException)
            {
            }

            var closed = await ReadAll(stream, TimeSpan.FromSeconds(3));
            Check(!closed.StartsWith("HTTP/1.1 200", StringComparison.Ordinal) && !wait.IsCompleted, "loopback: more than 8 KB of headers is refused and never reaches the sign-in");
        }

        using var browser = new System.Net.Sockets.TcpClient();
        await browser.ConnectAsync(System.Net.IPAddress.Loopback, port);
        var browserStream = browser.GetStream();
        await browserStream.WriteAsync(Encoding.ASCII.GetBytes("GET /callback?code=THECODE&state=THESTATE HTTP/1.1\r\nHost: 127.0.0.1:" + port + "\r\n\r\n"));
        var done = await Task.WhenAny(wait, Task.Delay(5000));
        Check(done == wait, "loopback: the callback completes the wait");
        if (done != wait)
        {
            return;
        }

        var (result, respond) = await wait;
        Check(result.Kind == OAuthKind.Code && result.Code == "THECODE" && result.State == "THESTATE", "loopback: the callback is handed over parsed");
        var early = await ReadAll(browserStream, TimeSpan.FromMilliseconds(400));
        Check(early.Length == 0, "loopback: the browser's connection is held open until the answer is written");
        await respond(LoopbackRedirect.Page("Trayloom", "All done <ok>"));
        var answer = await ReadAll(browserStream, TimeSpan.FromSeconds(3));
        Check(answer.StartsWith("HTTP/1.1 200", StringComparison.Ordinal), "loopback: the answer is a 200");
        Check(answer.Contains("Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'", StringComparison.Ordinal), "loopback: strict CSP header");
        Check(answer.Contains("Cache-Control: no-store", StringComparison.Ordinal) && answer.Contains("Connection: close", StringComparison.Ordinal) && answer.Contains("text/html; charset=utf-8", StringComparison.Ordinal), "loopback: no-store, close and the content type");
        Check(answer.Contains("All done &lt;ok&gt;", StringComparison.Ordinal), "loopback: the page text arrives encoded");
        Check(!answer.Contains("Access-Control", StringComparison.OrdinalIgnoreCase), "loopback: no CORS headers");

        var late = await Exchange(port, "GET /callback?code=again&state=s HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n");
        Check(late.Length == 0 || late.StartsWith("HTTP/1.1 404", StringComparison.Ordinal), "loopback: a second callback is not handed out");

        await using var quiet = LoopbackRedirect.Start(0);
        var timedOut = false;
        try
        {
            await quiet.WaitAsync(TimeSpan.FromMilliseconds(150), CancellationToken.None);
        }
        catch (TimeoutException)
        {
            timedOut = true;
        }

        Check(timedOut, "loopback: the wait times out");

        using var cts = new CancellationTokenSource(150);
        var cancelled = false;
        try
        {
            await quiet.WaitAsync(TimeSpan.FromSeconds(20), cts.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        Check(cancelled, "loopback: the wait is cancelled with its token");
    }

    private static async Task<string> Exchange(int port, string request)
    {
        using var client = new System.Net.Sockets.TcpClient();
        await client.ConnectAsync(System.Net.IPAddress.Loopback, port);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request));
        return await ReadAll(stream, TimeSpan.FromSeconds(3));
    }

    private static async Task<string> ReadAll(Stream stream, TimeSpan limit)
    {
        using var cts = new CancellationTokenSource(limit);
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        try
        {
            int read;
            while ((read = await stream.ReadAsync(chunk, cts.Token)) > 0)
            {
                buffer.Write(chunk, 0, read);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
            // stopped waiting, or the peer reset the connection
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void PkceChecks()
    {
        // RFC 7636 appendix B.
        Check(Pkce.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk") == "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", "pkce: the RFC 7636 test vector");

        var verifier = Pkce.CreateVerifier();
        Check(verifier.Length == 86, "pkce: the verifier has 86 characters");
        Check(verifier.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'), "pkce: the verifier uses only the unreserved URL-safe characters");
        Check(Pkce.CreateVerifier() != verifier, "pkce: verifiers differ");
        var challenge = Pkce.Challenge(verifier);
        Check(challenge.Length == 43 && !challenge.Contains('=') && !challenge.Contains('+') && !challenge.Contains('/'), "pkce: the challenge is 43 characters of unpadded base64url");

        var states = Enumerable.Range(0, 50).Select(_ => Pkce.CreateState()).ToList();
        Check(states.Distinct().Count() == 50, "pkce: states are unique");
        Check(states.All(s => s.Length == 43 && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')), "pkce: a state is 32 random bytes as base64url");
        Check(Pkce.StateMatches(states[0], states[0]) && !Pkce.StateMatches(states[0], states[1]), "pkce: state comparison");
        Check(!Pkce.StateMatches("", "") && !Pkce.StateMatches(null, null) && !Pkce.StateMatches(states[0], "") && !Pkce.StateMatches(states[0], states[0] + "x"), "pkce: empty and different-length states never match");
    }

    private static void CallbackParsing()
    {
        var ok = OAuthCallback.Parse("GET /callback?code=AQD123&state=abc HTTP/1.1");
        Check(ok.Kind == OAuthKind.Code && ok.Code == "AQD123" && ok.State == "abc" && ok.Error == "", "callback: a code with its state");
        var decoded = OAuthCallback.Parse("GET /callback?code=a%2Fb%2Bc%3D&state=x%20y+z HTTP/1.1");
        Check(decoded.Code == "a/b+c=" && decoded.State == "x y z", "callback: values are percent-decoded");
        var denied = OAuthCallback.Parse("GET /callback?error=access_denied&state=abc HTTP/1.1");
        Check(denied.Kind == OAuthKind.Error && denied.Error == "access_denied" && denied.State == "abc" && denied.Code == "", "callback: the user said no");
        Check(OAuthCallback.Parse("GET /callback?error=access_denied&code=zzz&state=s HTTP/1.1").Kind == OAuthKind.Error, "callback: an error wins over a code");
        var twice = OAuthCallback.Parse("GET /callback?code=first&code=second&state=one&state=two HTTP/1.1");
        Check(twice.Code == "first" && twice.State == "one", "callback: the first of a repeated key counts");
        Check(OAuthCallback.Parse("GET /callback?code=c HTTP/1.1") is { Kind: OAuthKind.Code, State: "" }, "callback: a missing state parses as empty (and fails the state check later)");
        Check(OAuthCallback.Parse("GET /callback?code=c&state=s#frag HTTP/1.1").State == "s", "callback: a fragment is ignored");

        Check(OAuthCallback.Parse("GET /favicon.ico HTTP/1.1").Kind == OAuthKind.Ignore, "callback: another path is ignored");
        Check(OAuthCallback.Parse("GET /callback/extra?code=c&state=s HTTP/1.1").Kind == OAuthKind.Ignore, "callback: a longer path is ignored");
        Check(OAuthCallback.Parse("GET /Callback?code=c&state=s HTTP/1.1").Kind == OAuthKind.Ignore, "callback: the path is case-sensitive");
        Check(OAuthCallback.Parse("GET / HTTP/1.1").Kind == OAuthKind.Ignore && OAuthCallback.Parse("GET /callback HTTP/1.1").Kind == OAuthKind.Ignore, "callback: no query is ignored");
        Check(OAuthCallback.Parse("POST /callback?code=c&state=s HTTP/1.1").Kind == OAuthKind.Ignore, "callback: only GET counts");
        Check(OAuthCallback.Parse("GET /callback?state=s HTTP/1.1").Kind == OAuthKind.Ignore, "callback: no code and no error is ignored");
        Check(OAuthCallback.Parse("GET /callback?code=&state=s HTTP/1.1").Kind == OAuthKind.Ignore, "callback: an empty code is ignored");
        Check(OAuthCallback.Parse("").Kind == OAuthKind.Ignore && OAuthCallback.Parse(null).Kind == OAuthKind.Ignore && OAuthCallback.Parse("GET").Kind == OAuthKind.Ignore && OAuthCallback.Parse("garbage").Kind == OAuthKind.Ignore, "callback: empty and malformed lines are ignored");
        Check(OAuthCallback.Parse("GET http://evil.example/callback?code=c&state=s HTTP/1.1").Kind == OAuthKind.Ignore, "callback: an absolute-form target is ignored");
        Check(OAuthCallback.Parse("GET /callback?code=%ZZ&state=s HTTP/1.1").Kind == OAuthKind.Code, "callback: bad percent escapes do not throw");
    }

    private static void CallbackPage()
    {
        var page = LoopbackRedirect.Page("<b>T</b>", "a & <script>alert(1)</script>");
        Check(!page.Contains("<script", StringComparison.OrdinalIgnoreCase) && !page.Contains("<b>", StringComparison.Ordinal), "callback page: texts are HTML-encoded");
        Check(page.Contains("&lt;script&gt;", StringComparison.Ordinal) && page.Contains("&amp;", StringComparison.Ordinal), "callback page: the encoded text is there");
    }

    private static void Check(bool ok, string what) => _check(ok, "music core: " + what);

    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    private static TokenSet Sample(DateTime? expires = null) =>
        new("client-123", "ACCESS-TOKEN-xyz", "REFRESH-TOKEN-abc", expires ?? Now.AddHours(1), "user-read-playback-state", true, "Ada");

    private static void Tokens()
    {
        var set = Sample();
        var json = set.ToJson();
        Check(json.StartsWith("{\"v\":1,", StringComparison.Ordinal), "tokens: JSON starts with the version");
        Check(TokenSet.FromJson(json) == set, "tokens: round trip");
        var unknownPremium = set with { Premium = null };
        Check(TokenSet.FromJson(unknownPremium.ToJson()) is { Premium: null }, "tokens: unknown Premium stays unknown");
        Check(TokenSet.FromJson(json.Replace("\"v\":1", "\"v\":2")) is null, "tokens: another version reads as none");
        Check(TokenSet.FromJson("{\"clientId\":\"x\"}") is null, "tokens: a missing version reads as none");
        Check(TokenSet.FromJson("{\"v\":1,\"clientId\":\"x\"}") is null, "tokens: missing fields read as none");
        Check(TokenSet.FromJson("") is null && TokenSet.FromJson(null) is null && TokenSet.FromJson("{nope") is null && TokenSet.FromJson("[1]") is null, "tokens: nothing and garbage read as none");

        Check(!Sample(Now.AddMinutes(10)).NeedsRefresh(Now), "refresh: 10 minutes left needs none");
        Check(!Sample(Now.AddSeconds(61)).NeedsRefresh(Now), "refresh: 61 s left needs none");
        Check(Sample(Now.AddSeconds(60)).NeedsRefresh(Now), "refresh: exactly 60 s left refreshes");
        Check(Sample(Now.AddSeconds(5)).NeedsRefresh(Now), "refresh: 5 s left refreshes");
        Check(Sample(Now.AddMinutes(-1)).NeedsRefresh(Now), "refresh: expired refreshes");
    }

    private static void Secrets()
    {
        var dir = Path.Combine(Path.GetTempPath(), "trayloom-music-checks-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SecretStore(dir);
            Check(!store.TryRead("spotify", out _), "secrets: nothing stored reads as missing");

            var plain = "{\"v\":1,\"apiKey\":\"AIzaSyPLAINTEXT-marker-123\"}";
            store.Write("youtube", plain);
            Check(store.TryRead("youtube", out var back) && back == plain, "secrets: round trip");
            var bytes = File.ReadAllBytes(Path.Combine(dir, "youtube.bin"));
            Check(IndexOf(bytes, Encoding.UTF8.GetBytes("PLAINTEXT-marker")) < 0 && IndexOf(bytes, Encoding.Unicode.GetBytes("PLAINTEXT-marker")) < 0, "secrets: the file does not contain the plaintext");
            Check(!File.Exists(Path.Combine(dir, "youtube.bin.tmp")), "secrets: no temp file is left behind");

            store.Write("youtube", "{\"v\":1,\"apiKey\":\"second\"}");
            Check(store.TryRead("youtube", out back) && back.Contains("second", StringComparison.Ordinal), "secrets: a write replaces the old value");

            // Entropy is per name: the same bytes under another name must not decrypt.
            File.Copy(Path.Combine(dir, "youtube.bin"), Path.Combine(dir, "spotify.bin"));
            Check(!store.TryRead("spotify", out _) && !File.Exists(Path.Combine(dir, "spotify.bin")), "secrets: another name's file is unreadable and cleared");

            File.WriteAllBytes(Path.Combine(dir, "youtube.bin"), [1, 2, 3, 4, 5]);
            Check(!store.TryRead("youtube", out var junk) && junk == "" && !File.Exists(Path.Combine(dir, "youtube.bin")), "secrets: a corrupt file reads as missing and is deleted");

            store.Write("spotify", "{\"v\":1}");
            store.Delete("spotify");
            Check(!File.Exists(Path.Combine(dir, "spotify.bin")), "secrets: delete removes the file");
            store.Delete("spotify");
            Check(true, "secrets: deleting a missing file is harmless");
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                return i;
            }
        }

        return -1;
    }

    private static void Classify()
    {
        var s = StreamKind.Spotify;
        var y = StreamKind.YouTube;
        Check(ApiError.Classify(s, 401, "") == StreamStatus.Revoked && ApiError.Classify(y, 401, null) == StreamStatus.Revoked, "classify: 401 is Revoked");
        Check(ApiError.Classify(s, 403, "{\"error\":{\"status\":403,\"message\":\"Player command failed: Premium required\",\"reason\":\"PREMIUM_REQUIRED\"}}") == StreamStatus.PremiumRequired, "classify: Spotify PREMIUM_REQUIRED");
        Check(ApiError.Classify(s, 403, "{\"error\":{\"status\":403,\"message\":\"User not registered in the Developer Dashboard\"}}") == StreamStatus.NotAllowlisted, "classify: Spotify not registered");
        Check(ApiError.Classify(s, 403, "{\"error\":{\"message\":\"Forbidden\"}}") == StreamStatus.Failed, "classify: other Spotify 403 is Failed");
        Check(ApiError.Classify(s, 404, "{\"error\":{\"status\":404,\"message\":\"Player command failed: No active device found\",\"reason\":\"NO_ACTIVE_DEVICE\"}}") == StreamStatus.NoDevice, "classify: Spotify NO_ACTIVE_DEVICE");
        Check(ApiError.Classify(s, 404, "{}") == StreamStatus.Failed, "classify: other 404 is Failed");
        Check(ApiError.Classify(s, 429, "{\"error\":{\"status\":429,\"message\":\"Too many requests\",\"reason\":\"QUOTA_EXCEEDED\"}}") == StreamStatus.QuotaExceeded, "classify: Spotify QUOTA_EXCEEDED");
        Check(ApiError.Classify(s, 429, "") == StreamStatus.RateLimited && ApiError.Classify(s, 429, null) == StreamStatus.RateLimited, "classify: other 429 is RateLimited");
        Check(ApiError.Classify(y, 400, "{\"error\":{\"errors\":[{\"reason\":\"keyInvalid\"}],\"message\":\"API key not valid\"}}") == StreamStatus.InvalidKey, "classify: Google keyInvalid");
        Check(ApiError.Classify(y, 400, "{\"error\":{\"details\":[{\"reason\":\"API_KEY_INVALID\"}]}}") == StreamStatus.InvalidKey, "classify: Google API_KEY_INVALID");
        Check(ApiError.Classify(y, 400, "{\"error\":{\"errors\":[{\"reason\":\"keyExpired\"}]}}") == StreamStatus.InvalidKey, "classify: Google keyExpired");
        Check(ApiError.Classify(y, 403, "{\"error\":{\"errors\":[{\"reason\":\"quotaExceeded\"}]}}") == StreamStatus.QuotaExceeded, "classify: Google quotaExceeded");
        Check(ApiError.Classify(y, 403, "{\"error\":{\"errors\":[{\"reason\":\"dailyLimitExceeded\"}]}}") == StreamStatus.QuotaExceeded, "classify: Google dailyLimitExceeded");
        Check(ApiError.Classify(y, 403, "{\"error\":{\"errors\":[{\"reason\":\"accessNotConfigured\"}]}}") == StreamStatus.ApiDisabled, "classify: Google accessNotConfigured");
        Check(ApiError.Classify(y, 403, "{\"error\":{\"details\":[{\"reason\":\"SERVICE_DISABLED\"}]}}") == StreamStatus.ApiDisabled, "classify: Google SERVICE_DISABLED");
        Check(ApiError.Classify(y, 500, "") == StreamStatus.Failed && ApiError.Classify(s, 503, "x") == StreamStatus.Failed && ApiError.Classify(y, 418, null) == StreamStatus.Failed, "classify: 5xx and anything else is Failed");
        Check(ApiError.Classify(y, 403, "PREMIUM_REQUIRED") == StreamStatus.Failed, "classify: Spotify reasons do not apply to YouTube");
        Check(ApiError.Classify(s, 400, "keyInvalid") == StreamStatus.Failed, "classify: Google reasons do not apply to Spotify");
    }

    private static void RetryAfter()
    {
        var now = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
        Check(ApiError.RetryAfter(null, now) == TimeSpan.FromSeconds(5), "retry-after: missing is 5 s");
        Check(ApiError.RetryAfter("", now) == TimeSpan.FromSeconds(5) && ApiError.RetryAfter("soon", now) == TimeSpan.FromSeconds(5), "retry-after: unreadable is 5 s");
        Check(ApiError.RetryAfter("30", now) == TimeSpan.FromSeconds(30), "retry-after: seconds");
        Check(ApiError.RetryAfter("0", now) == TimeSpan.FromSeconds(1) && ApiError.RetryAfter("-4", now) == TimeSpan.FromSeconds(1), "retry-after: clamped up to 1 s");
        Check(ApiError.RetryAfter("3600", now) == TimeSpan.FromSeconds(120), "retry-after: clamped down to 120 s");
        Check(ApiError.RetryAfter(now.AddSeconds(45).ToString("R"), now) == TimeSpan.FromSeconds(45), "retry-after: an HTTP date");
        Check(ApiError.RetryAfter(now.AddSeconds(-45).ToString("R"), now) == TimeSpan.FromSeconds(1), "retry-after: a date in the past is 1 s");
        Check(ApiError.RetryAfter(now.AddDays(1).ToString("R"), now) == TimeSpan.FromSeconds(120), "retry-after: a date far ahead is 120 s");
    }

    private static void Cache()
    {
        var now = Now;
        var cache = new StreamCache(() => now, TimeSpan.FromMinutes(5), max: 3);
        var a = new StreamRequest(StreamView.Search, "abba");
        var aAgain = new StreamRequest(StreamView.Search, "abba");
        var page = new StreamPage([], null);

        Check(!cache.TryGet(a, out _), "cache: empty");
        cache.Set(a, page);
        Check(cache.TryGet(aAgain, out var got) && ReferenceEquals(got, page), "cache: an equal request hits");
        Check(!cache.TryGet(new StreamRequest(StreamView.Search, "abba", StreamScope.Songs), out _), "cache: another scope misses");
        Check(!cache.TryGet(new StreamRequest(StreamView.Search, "abba", Offset: 10), out _), "cache: another offset misses");

        now = Now.AddMinutes(4.9);
        Check(cache.TryGet(a, out _), "cache: still fresh just before the TTL");
        now = Now.AddMinutes(5);
        Check(!cache.TryGet(a, out _) && cache.Count == 0, "cache: expired at the TTL and dropped");

        now = Now;
        var q1 = new StreamRequest(StreamView.Search, "1");
        var q2 = new StreamRequest(StreamView.Search, "2");
        var q3 = new StreamRequest(StreamView.Search, "3");
        var q4 = new StreamRequest(StreamView.Search, "4");
        cache.Set(q1, page);
        cache.Set(q2, page);
        cache.Set(q3, page);
        cache.Set(q4, page);
        Check(cache.Count == 3 && !cache.TryGet(q1, out _) && cache.TryGet(q2, out _) && cache.TryGet(q4, out _), "cache: the oldest entry goes when the limit is passed");
        cache.Set(q2, page);
        cache.Set(new StreamRequest(StreamView.Search, "5"), page);
        Check(!cache.TryGet(q3, out _) && cache.TryGet(q2, out _), "cache: a rewritten entry counts as new");

        Check(cache.BeginFetch(a) && !cache.BeginFetch(aAgain) && cache.IsInFlight(a), "cache: a second identical fetch is refused while one runs");
        cache.EndFetch(a);
        Check(!cache.IsInFlight(a) && cache.BeginFetch(a), "cache: a fetch can start again once it ended");
        cache.Clear();
        Check(cache.Count == 0, "cache: clear empties it");
    }

    private static void ArtHosts()
    {
        Check(StreamArt.Allowed("https://i.scdn.co/image/ab67616d00004851abc"), "art: Spotify i.scdn.co");
        Check(StreamArt.Allowed("https://mosaic.scdn.co/64/abc"), "art: another *.scdn.co");
        Check(StreamArt.Allowed("https://image-cdn-ak.spotifycdn.com/image/abc"), "art: *.spotifycdn.com");
        Check(StreamArt.Allowed("https://i.ytimg.com/vi/dQw4w9WgXcQ/mqdefault.jpg"), "art: i.ytimg.com");
        Check(StreamArt.Allowed("https://yt3.ggpht.com/abc=s88"), "art: *.ggpht.com");
        Check(StreamArt.Allowed("https://lh3.googleusercontent.com/abc=w60-h60-l90-rj"), "art: lh3.googleusercontent.com");
        Check(StreamArt.Allowed("https://yt3.googleusercontent.com/abc=w60-h60-l90-rj"), "art: yt3.googleusercontent.com");
        Check(!StreamArt.Allowed("https://evil.googleusercontent.com/abc") && !StreamArt.Allowed("https://googleusercontent.com/abc") && !StreamArt.Allowed("https://lh3.googleusercontent.com.evil.com/x") && !StreamArt.Allowed("https://u:p@lh3.googleusercontent.com/x") && !StreamArt.Allowed("http://lh3.googleusercontent.com/x"), "art: only the two googleusercontent hosts, https, no userinfo");
        Check(!StreamArt.Allowed("http://i.scdn.co/image/abc"), "art: plain http is refused");
        Check(!StreamArt.Allowed("https://scdn.co/image/abc"), "art: the bare domain is not a subdomain");
        Check(!StreamArt.Allowed("https://evil-scdn.co/image/abc") && !StreamArt.Allowed("https://i.scdn.co.evil.com/x"), "art: look-alike hosts are refused");
        Check(!StreamArt.Allowed("https://evil.com/i.scdn.co") && !StreamArt.Allowed("https://i.scdn.co@evil.com/x"), "art: the host is parsed, not searched");
        Check(!StreamArt.Allowed("https://img.ytimg.com/x") && !StreamArt.Allowed("https://i.ytimg.com.evil.com/x"), "art: only i.ytimg.com for ytimg");
        Check(!StreamArt.Allowed("") && !StreamArt.Allowed(null) && !StreamArt.Allowed("not a url") && !StreamArt.Allowed("file:///c:/x.png"), "art: empty, relative and file addresses are refused");
    }
}
