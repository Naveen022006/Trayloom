// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Trayloom.Core;

namespace Trayloom.Features.Island;

/// <summary>The outcome of adding a Google calendar.</summary>
internal enum GoogleAddResult
{
    Added,
    InvalidAddress,
    AlreadyAdded,
    Rejected,
    Offline,
    NotCalendar,
    TooMany,
}

/// <summary>How the last check of one calendar ended.</summary>
internal enum FeedState
{
    Ok,
    Offline,
    Rejected,
    Failed,
}

/// <summary>The state of one calendar shown in Settings. <paramref name="LastOkUtc"/> is when its events were last confirmed current.</summary>
internal sealed record FeedStatus(FeedState State, DateTime? LastOkUtc);

/// <summary>
/// One added calendar. <paramref name="Url"/> is the secret address, so it is left out of <c>ToString</c> and can never reach a log by accident.
/// </summary>
internal sealed record GoogleFeed(string Id, string Url, string Name, string Color)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Id = ").Append(Id);
        return true;
    }
}

/// <summary>
/// Files under <c>%APPDATA%\Trayloom\calendar</c>, encrypted for the current Windows user (DPAPI) with their own entropy. Same shape as the
/// music <see cref="SecretStore"/>, kept separate so that file stays untouched and log lines name the calendar, not the music services.
/// </summary>
internal sealed class GoogleSecretFile
{
    private readonly string _directory;
    private readonly ILog? _log;

    public GoogleSecretFile(string directory, ILog? log = null)
    {
        _directory = directory;
        _log = log;
    }

    /// <summary>Reads and decrypts <paramref name="name"/>; an unreadable file is deleted and reported as missing.</summary>
    public bool TryRead(string name, out string text)
    {
        text = "";
        var path = PathOf(name);
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            text = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy(name), DataProtectionScope.CurrentUser));
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Never log the exception text: it must not carry anything about the file's content.
            _log?.Warn("[island] Calendar: stored data unreadable, cleared (" + name + ")");
            Delete(name);
            return false;
        }
    }

    /// <summary>Encrypts and writes <paramref name="text"/> immediately (temp file, then replace).</summary>
    public void Write(string name, string text)
    {
        Directory.CreateDirectory(_directory);
        var path = PathOf(name);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, ProtectedData.Protect(Encoding.UTF8.GetBytes(text), Entropy(name), DataProtectionScope.CurrentUser));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Deletes <paramref name="name"/> if present.</summary>
    public void Delete(string name)
    {
        try
        {
            File.Delete(PathOf(name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Warn("[island] Calendar: could not delete stored data (" + name + ")");
        }
    }

    private string PathOf(string name) => Path.Combine(_directory, name + ".bin");

    private static byte[] Entropy(string name) => Encoding.UTF8.GetBytes("Trayloom.Calendar.v1:" + name);
}

/// <summary>
/// Google calendars read through their secret iCal address: no sign-in, read-only. The addresses and the last downloaded copy of each
/// calendar are stored encrypted; only status codes, counts and timings are ever logged.
/// </summary>
internal sealed class GoogleCalendarSource : IDisposable
{
    /// <summary>The most calendars that can be added.</summary>
    public const int MaxFeeds = 10;

    private const int MaxBytes = 8 * 1024 * 1024;
    private const string FeedsFile = "google";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
    private static readonly Regex FeedIdRule = new(@"^g-[0-9a-f]{8}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ColorRule = new(@"^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly HttpClient SharedHttp = CreateHttp(null);

    private readonly IAppSettings _app;
    private readonly ILog _log;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly GoogleSecretFile _files;
    private readonly object _gate = new();
    private readonly List<Runtime> _feeds = [];
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public GoogleCalendarSource(IAppSettings app, ILog log, HttpMessageHandler? handler = null, string? directory = null)
    {
        _app = app;
        _log = log;
        _ownsHttp = handler is not null;
        _http = handler is null ? SharedHttp : CreateHttp(handler);
        _files = new GoogleSecretFile(directory ?? Path.Combine(AppPaths.Root, "calendar"), log);
        LoadFeeds();
        SystemEvents.SessionSwitch += OnSession;
        SystemEvents.PowerModeChanged += OnPower;
    }

    /// <summary>The calendars that were added, in the order they were added.</summary>
    public IReadOnlyList<GoogleFeed> Feeds
    {
        get
        {
            lock (_gate)
            {
                return [.. _feeds.Select(f => f.Feed)];
            }
        }
    }

    /// <summary>When the next calendar is due to be checked.</summary>
    public DateTime NextDueUtc
    {
        get
        {
            lock (_gate)
            {
                var ceiling = DateTime.UtcNow.AddDays(1);
                return _feeds.Count == 0 ? ceiling : new DateTime(Math.Min(_feeds.Min(f => f.DueUtc).Ticks, ceiling.Ticks), DateTimeKind.Utc);
            }
        }
    }

    /// <summary>Raised when the list of calendars or a status changed (any thread).</summary>
    public event Action? Changed;

    /// <summary>Raised when the events should be read again (any thread).</summary>
    public event Action? RefreshRequested;

    /// <summary>The state of one calendar; Failed with no time for an unknown id.</summary>
    public FeedStatus StatusOf(string id)
    {
        lock (_gate)
        {
            return _feeds.FirstOrDefault(f => f.Feed.Id == id)?.Status ?? new FeedStatus(FeedState.Failed, null);
        }
    }

    /// <summary>Checks the address, downloads the calendar once and stores it. Nothing is stored unless it worked.</summary>
    public async Task<GoogleAddResult> AddAsync(string url, CancellationToken ct = default)
    {
        if (!GoogleRules.TryNormalize(url, out var normalized, out _))
        {
            return GoogleAddResult.InvalidAddress;
        }

        lock (_gate)
        {
            if (_feeds.Any(f => string.Equals(f.Feed.Url, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                return GoogleAddResult.AlreadyAdded;
            }

            if (_feeds.Count >= MaxFeeds)
            {
                return GoogleAddResult.TooMany;
            }
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        var watch = Stopwatch.StartNew();
        var fetch = await FetchAsync(normalized, null, null, linked.Token).ConfigureAwait(false);
        _log.Info($"[island] Calendar: add check {fetch.Kind} {fetch.Status} in {watch.ElapsedMilliseconds} ms");
        switch (fetch.Kind)
        {
            case FetchKind.Rejected:
                return GoogleAddResult.Rejected;
            case FetchKind.Body:
                break;
            default:
                return GoogleAddResult.Offline;
        }

        var text = fetch.Text!;
        if (!text.Contains("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase))
        {
            return GoogleAddResult.NotCalendar;
        }

        var cal = await Task.Run(() => Ics.Parse(text)).ConfigureAwait(false);
        Runtime rt;
        lock (_gate)
        {
            if (_feeds.Count >= MaxFeeds)
            {
                return GoogleAddResult.TooMany;
            }

            if (_feeds.Any(f => string.Equals(f.Feed.Url, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                return GoogleAddResult.AlreadyAdded;
            }

            var id = "g-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
            while (_feeds.Any(f => f.Feed.Id == id))
            {
                id = "g-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
            }

            var feed = new GoogleFeed(id, normalized, cal.Name ?? Loc.T("Google Calendar"), GoogleRules.NextColor(_feeds.Select(f => f.Feed.Color)));
            rt = new Runtime(feed);
            _feeds.Add(rt);
            ApplyBody(rt, cal, fetch, DateTime.UtcNow);
        }

        try
        {
            WriteCache(rt, text, fetch.Etag, fetch.LastModified, DateTime.UtcNow);
            PersistFeeds();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            // Without the file the calendar would vanish at the next start, so do not pretend it was added.
            lock (_gate)
            {
                _feeds.Remove(rt);
            }

            _files.Delete(CacheName(rt.Feed.Id));
            _log.Warn("[island] Calendar: could not store the new calendar");
            return GoogleAddResult.Offline;
        }

        _log.Info($"[island] Calendar: added {rt.Feed.Id} ({cal.Events.Count} events)");
        Changed?.Invoke();
        RefreshRequested?.Invoke();
        return GoogleAddResult.Added;
    }

    /// <summary>Forgets a calendar and deletes its stored address and cached copy.</summary>
    public void Remove(string id)
    {
        lock (_gate)
        {
            if (_feeds.RemoveAll(f => f.Feed.Id == id) == 0)
            {
                return;
            }
        }

        _files.Delete(CacheName(id));
        PersistFeedsLogged();
        _log.Info($"[island] Calendar: removed {id}");
        Changed?.Invoke();
        RefreshRequested?.Invoke();
    }

    /// <summary>Gives a calendar a new display name (blank names are ignored).</summary>
    public void Rename(string id, string name)
    {
        name = name.Trim();
        if (name.Length == 0)
        {
            return;
        }

        Edit(id, f => f with { Name = name.Length > 80 ? name[..80] : name });
    }

    /// <summary>Gives a calendar a new colour (<c>#RRGGBB</c>).</summary>
    public void SetColor(string id, string hex)
    {
        if (ColorRule.IsMatch(hex))
        {
            Edit(id, f => f with { Color = hex.ToUpperInvariant() });
        }
    }

    /// <summary>Makes every calendar that was last checked longer ago than <paramref name="age"/> due now and asks for a refresh.</summary>
    public void MarkStale(TimeSpan age)
    {
        var any = false;
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            foreach (var rt in _feeds.Where(f => now - f.LastAttemptUtc > age))
            {
                rt.DueUtc = now;
                any = true;
            }
        }

        if (any)
        {
            RefreshRequested?.Invoke();
        }
    }

    /// <summary>Checks every calendar as soon as possible.</summary>
    public void RefreshNow()
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            foreach (var rt in _feeds)
            {
                rt.DueUtc = now;
            }
        }

        RefreshRequested?.Invoke();
    }

    /// <summary>
    /// Brings the due calendars up to date (at most three at a time) and appends the events of the others from memory.
    /// Calendars Google no longer accepts show no events; offline ones keep showing their last copy.
    /// </summary>
    internal async Task ReadAsync(List<CalEvent> events, List<CalendarInfo> calendars, IReadOnlyCollection<string> excluded, DateTime from, DateTime to)
    {
        List<Runtime> snapshot;
        lock (_gate)
        {
            snapshot = [.. _feeds];
        }

        if (snapshot.Count == 0)
        {
            return;
        }

        await Task.WhenAll(snapshot.Where(f => !f.Loaded).Select(f => Task.Run(() => LoadCache(f)))).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        var due = snapshot.Where(f => f.DueUtc <= now).ToList();
        if (due.Count > 0)
        {
            using var gate = new SemaphoreSlim(3);
            await Task.WhenAll(due.Select(async f =>
            {
                await gate.WaitAsync(_cts.Token).ConfigureAwait(false);
                try
                {
                    await RefreshFeedAsync(f).ConfigureAwait(false);
                }
                finally
                {
                    gate.Release();
                }
            })).ConfigureAwait(false);
        }

        foreach (var rt in snapshot)
        {
            GoogleFeed feed;
            IcsCalendar? parsed;
            bool rejected;
            lock (_gate)
            {
                if (!_feeds.Contains(rt))
                {
                    continue;
                }

                feed = rt.Feed;
                parsed = rt.Parsed;
                rejected = rt.Status.State == FeedState.Rejected;
            }

            var calId = "google:" + feed.Id;
            var color = GoogleRules.ParseColor(feed.Color);
            calendars.Add(new CalendarInfo(calId, feed.Name, Loc.T("Google Calendar"), color, "google"));
            if (parsed is null || rejected || excluded.Contains(calId))
            {
                continue;
            }

            var cal = parsed;
            events.AddRange(await Task.Run(() => IcsReader.ToEvents(cal, from, to, calId, feed.Name, color, "google", _log)).ConfigureAwait(false));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.SessionSwitch -= OnSession;
        SystemEvents.PowerModeChanged -= OnPower;
        _cts.Cancel();
        _cts.Dispose();
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }

    // ---------------------------------------------------------------- refresh

    private async Task RefreshFeedAsync(Runtime rt)
    {
        string url;
        string? etag;
        string? modified;
        lock (_gate)
        {
            url = rt.Feed.Url;
            etag = rt.Parsed is null ? null : rt.Etag;
            modified = rt.Parsed is null ? null : rt.LastModified;
        }

        var watch = Stopwatch.StartNew();
        var fetch = await FetchAsync(url, etag, modified, _cts.Token).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        IcsCalendar? parsed = null;
        var count = -1;
        if (fetch.Kind == FetchKind.Body)
        {
            var text = fetch.Text!;
            if (text.Contains("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase))
            {
                parsed = await Task.Run(() => Ics.Parse(text)).ConfigureAwait(false);
                count = parsed.Events.Count;
                try
                {
                    WriteCache(rt, text, fetch.Etag, fetch.LastModified, now);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
                {
                    _log.Warn("[island] Calendar: could not store the downloaded copy of " + rt.Feed.Id);
                }
            }
            else
            {
                fetch = fetch with { Kind = FetchKind.Failed };
            }
        }

        var changed = false;
        lock (_gate)
        {
            var before = rt.Status;
            rt.LastAttemptUtc = now;
            switch (fetch.Kind)
            {
                case FetchKind.Body:
                    ApplyBody(rt, parsed!, fetch, now);
                    break;
                case FetchKind.NotModified:
                    rt.Failures = 0;
                    rt.Status = new FeedStatus(FeedState.Ok, now);
                    rt.DueUtc = now + Interval;
                    break;
                case FetchKind.Rejected:
                    rt.Status = new FeedStatus(FeedState.Rejected, rt.Status.LastOkUtc);
                    rt.DueUtc = now + TimeSpan.FromMinutes(60);
                    break;
                case FetchKind.RateLimited:
                    rt.Status = new FeedStatus(FeedState.Failed, rt.Status.LastOkUtc);
                    rt.DueUtc = now + TimeSpan.FromMinutes(30);
                    break;
                default:
                    rt.Failures++;
                    rt.Status = new FeedStatus(fetch.Kind == FetchKind.Offline ? FeedState.Offline : FeedState.Failed, rt.Status.LastOkUtc);
                    rt.DueUtc = now + TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, Math.Min(rt.Failures, 6))));
                    break;
            }

            changed = before != rt.Status;
        }

        _log.Info($"[island] Calendar: {rt.Feed.Id} {fetch.Kind} {fetch.Status} in {watch.ElapsedMilliseconds} ms" + (count >= 0 ? $", {count} events" : ""));
        if (changed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Stores a freshly downloaded and parsed calendar in memory (call with the gate held).</summary>
    private void ApplyBody(Runtime rt, IcsCalendar cal, Fetched fetch, DateTime now)
    {
        rt.Parsed = cal;
        rt.Etag = fetch.Etag;
        rt.LastModified = fetch.LastModified;
        rt.FetchedUtc = now;
        rt.LastAttemptUtc = now;
        rt.Loaded = true;
        rt.Failures = 0;
        rt.Status = new FeedStatus(FeedState.Ok, now);
        rt.DueUtc = now + Interval;
    }

    private TimeSpan Interval => TimeSpan.FromMinutes(Math.Clamp(_app.Get<int>(GoogleCalendarKeys.Interval), 5, 60));

    // ---------------------------------------------------------------- http

    private enum FetchKind
    {
        Body,
        NotModified,
        Rejected,
        RateLimited,
        Offline,
        Failed,
    }

    private sealed record Fetched(FetchKind Kind, int Status, string? Text = null, string? Etag = null, string? LastModified = null);

    private async Task<Fetched> FetchAsync(string url, string? etag, string? lastModified, CancellationToken ct)
    {
        var current = new Uri(url);
        for (var hop = 0; hop <= 3; hop++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(RequestTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.Accept.Clear();
            request.Headers.Accept.ParseAdd("text/calendar");
            if (etag is not null)
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            }

            if (lastModified is not null)
            {
                request.Headers.TryAddWithoutValidation("If-Modified-Since", lastModified);
            }

            try
            {
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                var code = (int)response.StatusCode;
                if (code is 301 or 302 or 303 or 307 or 308)
                {
                    if (response.Headers.Location is not { } location)
                    {
                        return new Fetched(FetchKind.Failed, code);
                    }

                    var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                    if (!IsAllowedRedirect(next))
                    {
                        return new Fetched(FetchKind.Failed, code);
                    }

                    current = next;
                    continue;
                }

                switch (code)
                {
                    case 200:
                        return await ReadBodyAsync(response, timeout.Token).ConfigureAwait(false);
                    case 304:
                        return new Fetched(FetchKind.NotModified, code);
                    case 401 or 403 or 404:
                        return new Fetched(FetchKind.Rejected, code);
                    case 429:
                        return new Fetched(FetchKind.RateLimited, code);
                    default:
                        return new Fetched(FetchKind.Failed, code);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return new Fetched(FetchKind.Offline, 0);
            }
            catch (HttpRequestException)
            {
                return new Fetched(FetchKind.Offline, 0);
            }
        }

        return new Fetched(FetchKind.Failed, 0);
    }

    private static async Task<Fetched> ReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > MaxBytes)
        {
            return new Fetched(FetchKind.Failed, 200);
        }

        using var buffer = new MemoryStream();
        await using (var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        {
            var chunk = new byte[81920];
            int n;
            while ((n = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + n > MaxBytes)
                {
                    return new Fetched(FetchKind.Failed, 200);
                }

                buffer.Write(chunk, 0, n);
            }
        }

        var text = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length).TrimStart('﻿');
        return new Fetched(
            FetchKind.Body,
            200,
            text,
            response.Headers.ETag?.ToString(),
            response.Content.Headers.LastModified?.ToString("R", CultureInfo.InvariantCulture));
    }

    /// <summary>Only https hops inside Google's own domains may be followed, so a redirect cannot carry the request elsewhere.</summary>
    private static bool IsAllowedRedirect(Uri uri)
    {
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) || uri.UserInfo.Length > 0)
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        return host == "google.com" || host.EndsWith(".google.com", StringComparison.Ordinal)
            || host == "googleusercontent.com" || host.EndsWith(".googleusercontent.com", StringComparison.Ordinal);
    }

    private static HttpClient CreateHttp(HttpMessageHandler? handler)
    {
        HttpClient http;
        if (handler is null)
        {
            http = ApiHttp.Create();
        }
        else
        {
            http = new HttpClient(handler, disposeHandler: false) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        }

        var version = typeof(GoogleCalendarSource).Assembly.GetName().Version;
        var label = version is null ? "0.2" : $"{version.Major}.{version.Minor}";
        http.DefaultRequestHeaders.UserAgent.Clear();
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Trayloom/{label} (+https://github.com/Naveen022006/Trayloom)");
        return http;
    }

    // ---------------------------------------------------------------- storage

    private static string CacheName(string id) => "google-cache-" + id;

    private void LoadFeeds()
    {
        if (!_files.TryRead(FeedsFile, out var json))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("feeds", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var item in list.EnumerateArray())
            {
                var id = Str(item, "id");
                var url = Str(item, "url");

                // The id becomes part of a file name and the address is fetched, so both are checked again after reading.
                if (id is null || !FeedIdRule.IsMatch(id) || url is null || !GoogleRules.TryNormalize(url, out var normalized, out _) || _feeds.Count >= MaxFeeds || _feeds.Any(f => f.Feed.Id == id))
                {
                    continue;
                }

                var name = Str(item, "name")?.Trim();
                var color = Str(item, "color");
                var feed = new GoogleFeed(id, normalized, string.IsNullOrEmpty(name) ? Loc.T("Google Calendar") : name, color is not null && ColorRule.IsMatch(color) ? color.ToUpperInvariant() : GoogleRules.NextColor(_feeds.Select(f => f.Feed.Color)));
                _feeds.Add(new Runtime(feed));
            }
        }
        catch (JsonException)
        {
            _log.Warn("[island] Calendar: stored calendar list unreadable, cleared");
            _files.Delete(FeedsFile);
        }
    }

    private void LoadCache(Runtime rt)
    {
        string name;
        lock (_gate)
        {
            if (rt.Loaded)
            {
                return;
            }

            name = CacheName(rt.Feed.Id);
        }

        IcsCalendar? parsed = null;
        string? etag = null;
        string? modified = null;
        var fetched = DateTime.MinValue;
        if (_files.TryRead(name, out var json))
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (Str(root, "ics") is { } ics)
                {
                    parsed = Ics.Parse(ics);
                    etag = Str(root, "etag");
                    modified = Str(root, "lastModified");
                    if (Str(root, "fetchedUtc") is { } stamp && DateTime.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when))
                    {
                        fetched = when;
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                _log.Warn("[island] Calendar: stored copy unreadable, cleared (" + name + ")");
                _files.Delete(name);
            }
        }

        lock (_gate)
        {
            if (rt.Loaded)
            {
                return;
            }

            rt.Loaded = true;

            // Without a copy there is nothing to show, so check at once; with one, wait out the interval since it was fetched.
            // A refresh that was requested before the copy was read keeps its earlier time.
            var natural = parsed is null ? DateTime.UtcNow : fetched + Interval;
            if (rt.DueUtc > natural)
            {
                rt.DueUtc = natural;
            }

            if (parsed is not null)
            {
                rt.Parsed = parsed;
                rt.Etag = etag;
                rt.LastModified = modified;
                rt.FetchedUtc = fetched;
                rt.LastAttemptUtc = fetched;
                rt.Status = new FeedStatus(FeedState.Ok, fetched == DateTime.MinValue ? null : fetched);
            }
        }
    }

    private void WriteCache(Runtime rt, string text, string? etag, string? modified, DateTime stamp)
    {
        _files.Write(CacheName(rt.Feed.Id), JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["v"] = 1,
            ["etag"] = etag,
            ["lastModified"] = modified,
            ["fetchedUtc"] = stamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["ics"] = text,
        }));
    }

    private void PersistFeeds()
    {
        List<GoogleFeed> feeds;
        lock (_gate)
        {
            feeds = [.. _feeds.Select(f => f.Feed)];
        }

        _files.Write(FeedsFile, JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["v"] = 1,
            ["feeds"] = feeds.Select(f => new Dictionary<string, string> { ["id"] = f.Id, ["url"] = f.Url, ["name"] = f.Name, ["color"] = f.Color }).ToList(),
        }));
    }

    private void PersistFeedsLogged()
    {
        try
        {
            PersistFeeds();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            _log.Warn("[island] Calendar: could not update the stored calendar list");
        }
    }

    private void Edit(string id, Func<GoogleFeed, GoogleFeed> change)
    {
        lock (_gate)
        {
            var rt = _feeds.FirstOrDefault(f => f.Feed.Id == id);
            if (rt is null)
            {
                return;
            }

            rt.Feed = change(rt.Feed);
        }

        PersistFeedsLogged();
        Changed?.Invoke();
        RefreshRequested?.Invoke();
    }

    private static string? Str(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

    // ---------------------------------------------------------------- triggers

    private void OnSession(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionUnlock)
        {
            MarkStale(Interval);
        }
    }

    private void OnPower(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            MarkStale(Interval);
        }
    }

    /// <summary>The live state of one calendar. Fields are guarded by the source's gate.</summary>
    private sealed class Runtime
    {
        public Runtime(GoogleFeed feed)
        {
            Feed = feed;
        }

        public GoogleFeed Feed { get; set; }

        public FeedStatus Status { get; set; } = new(FeedState.Ok, null);

        public IcsCalendar? Parsed { get; set; }

        public string? Etag { get; set; }

        public string? LastModified { get; set; }

        public DateTime FetchedUtc { get; set; }

        public DateTime LastAttemptUtc { get; set; }

        /// <summary>When the next check is due; unknown (far future) until the stored copy has been read, unless a refresh was requested before.</summary>
        public DateTime DueUtc { get; set; } = DateTime.MaxValue;

        public int Failures { get; set; }

        public bool Loaded { get; set; }
    }
}
