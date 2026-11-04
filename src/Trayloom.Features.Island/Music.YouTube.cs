// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using Trayloom.Core;

namespace Trayloom.Features.Island;

/// <summary>
/// YouTube Music in the island's Library. With no key it searches through YouTube Music's own web service (<see cref="InnerTubeClient"/>:
/// unofficial, no account, no cookies); with a saved YouTube Data API v3 key of the user's own it uses Google's official API instead.
/// It cannot play (<see cref="CanPlayHere"/> is false): songs open in the browser, and the island's controls then follow the browser's
/// media session. Used from the UI thread only; <see cref="Changed"/> is raised on it.
/// </summary>
internal sealed class YouTubeService : IMusicService
{
    private const string SecretName = "youtube";
    private const string BaseUrl = "https://www.googleapis.com/youtube/v3";
    private const int MaxQueryLength = 200;
    private const int MaxSearchResults = 200;
    private static readonly TimeSpan ErrorLife = TimeSpan.FromSeconds(15);
    private static readonly Regex Aumid = new(@"^[A-Za-z0-9._!+\-]{1,200}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IslandCore _core;
    private readonly ILog _log;
    private readonly SecretStore _secrets;
    private readonly StreamCache _cache = new(() => DateTime.UtcNow, TimeSpan.FromMinutes(30));
    private readonly Dictionary<StreamRequest, (StreamLoad Load, DateTime Until)> _errors = [];
    private readonly Dictionary<StreamRequest, string> _tokens = [];
    private readonly InnerTubeClient _inner;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _unblockTimer;
    private readonly DispatcherTimer _noteTimer;
    private HttpClient? _http;
    private CancellationTokenSource? _saving;
    private string? _key;
    private DateTime _quotaUntilUtc = DateTime.MinValue;
    private DateTime _rateUntilUtc = DateTime.MinValue;
    private bool _pwaLookupStarted;
    private string? _pwaAumid;
    private IReadOnlyList<StreamLink>? _links;
    private StreamNote? _note;
    private bool _disposed;

    public YouTubeService(IslandCore core)
    {
        _core = core;
        _log = core.Ctx.Log;
        _secrets = SecretStore.Default(_log);
        _inner = new InnerTubeClient(_log);
        _unblockTimer = new DispatcherTimer(DispatcherPriority.Normal, core.Ui);
        _unblockTimer.Tick += (_, _) =>
        {
            _unblockTimer.Stop();
            Raise();
            if (Blocked() is not null)
            {
                ScheduleUnblock(DateTime.UtcNow);
            }
        };
        _noteTimer = new DispatcherTimer(DispatcherPriority.Normal, core.Ui) { Interval = TimeSpan.FromSeconds(4) };
        _noteTimer.Tick += (_, _) =>
        {
            _noteTimer.Stop();
            _note = null;
            Raise();
        };
        _key = ReadKey();
        KeyStatus = _key is null ? StreamStatus.NotConfigured : StreamStatus.Ok;
    }

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public StreamKind Kind => StreamKind.YouTube;

    /// <inheritdoc />
    public string Name => "YouTube Music";

    /// <inheritdoc />
    public StreamConnection Connection => StreamConnection.Connected;

    /// <inheritdoc />
    public bool CanBrowse => true;

    /// <inheritdoc />
    public bool ShowInIsland => _core.App.Get<bool>(K.MusicYouTube);

    /// <inheritdoc />
    public bool SearchAsYouType => false;

    /// <inheritdoc />
    public IReadOnlyList<StreamView> Tabs { get; } = [];

    /// <inheritdoc />
    public IReadOnlyList<StreamScope> Scopes { get; } = [StreamScope.Songs, StreamScope.Playlists];

    /// <inheritdoc />
    public bool CanQueue => false;

    /// <inheritdoc />
    public bool? CanPlayHere => false;

    /// <inheritdoc />
    public StreamNote? Note => _note;

    /// <inheritdoc />
    public string? BusyKey => null;

    /// <summary>
    /// Open YouTube Music, Liked music and Library as https links, plus "Open YouTube Music app" when the installed app is found. That
    /// entry's <see cref="StreamLink.Url"/> is <c>shell:AppsFolder\AUMID</c>: open every entry with <see cref="OpenLink"/>, which starts
    /// the app through <see cref="PlaybackService.OpenPlayer"/>. The app is looked up once, in the background, on the first read.
    /// </summary>
    public IReadOnlyList<StreamLink> Links
    {
        get
        {
            EnsureAppLookup();
            return _links ??= BuildLinks();
        }
    }

    /// <summary>The last four characters of the saved key, or null without one.</summary>
    public string? KeyHint => _key is { Length: >= 4 } key ? key[^4..] : _key;

    /// <summary>How the saved key last fared: <see cref="StreamStatus.Ok"/>, <see cref="StreamStatus.InvalidKey"/>, <see cref="StreamStatus.ApiDisabled"/>, <see cref="StreamStatus.QuotaExceeded"/> or <see cref="StreamStatus.NotConfigured"/> without a key.</summary>
    public StreamStatus KeyStatus { get; private set; }

    /// <summary>Opens one of <see cref="Links"/>; anything else is ignored.</summary>
    public void OpenLink(StreamLink link)
    {
        if (!Links.Contains(link))
        {
            return;
        }

        if (link.Url.StartsWith("shell:AppsFolder\\", StringComparison.Ordinal))
        {
            PlaybackService.OpenPlayer(link.Url["shell:AppsFolder\\".Length..]);
        }
        else
        {
            Open(link.Url);
        }
    }

    /// <inheritdoc />
    public StreamLoad Get(StreamRequest request)
    {
        request = Normalize(request);
        if (_cache.TryGet(request, out var page))
        {
            return new StreamLoad(StreamLoadState.Ready, page, StreamStatus.Ok);
        }

        if (request.View is not (StreamView.Search or StreamView.Container) || (request.View == StreamView.Search && request.Query.Length == 0) || (request.View == StreamView.Container && request.Container is null))
        {
            return new StreamLoad(StreamLoadState.Ready, new StreamPage([], null), StreamStatus.Ok);
        }

        if (Blocked() is { } blocked)
        {
            return blocked;
        }

        if (_errors.TryGetValue(request, out var error))
        {
            if (DateTime.UtcNow < error.Until)
            {
                return error.Load;
            }

            _errors.Remove(request);
        }

        if (_cache.BeginFetch(request))
        {
            Guarded.FireAndLog(() => FetchAsync(request), _log, "YouTube fetch");
        }

        return new StreamLoad(StreamLoadState.Loading, null, StreamStatus.Ok);
    }

    /// <inheritdoc />
    public void Retry(StreamRequest request)
    {
        request = Normalize(request);
        _errors.Remove(request);
        _cache.Remove(request);
        Raise();
    }

    /// <inheritdoc />
    public void Play(StreamItem item, StreamItem? context, IReadOnlyList<StreamItem>? siblings = null)
    {
        var playlist = context is { Kind: StreamItemKind.Playlist } ? context.Id : null;
        OpenItem(item, playlist);
    }

    /// <inheritdoc />
    public void Queue(StreamItem item)
    {
        // YouTube has no queue we can reach; the view never offers this (CanQueue is false).
    }

    /// <inheritdoc />
    public void OpenExternally(StreamItem item) => OpenItem(item, null);

    /// <inheritdoc />
    public void OpenSearchExternally(string query)
    {
        var text = (query ?? "").Trim();
        if (text.Length > 0)
        {
            Open(YouTubeLinks.Search(text.Length > MaxQueryLength ? text[..MaxQueryLength] : text, music: true));
        }
    }

    /// <inheritdoc />
    public string ShareUrl(StreamItem item) => item.Kind == StreamItemKind.Playlist
        ? YouTubeLinks.Playlist(item.Id, OpenInMusic)
        : YouTubeLinks.Watch(item.Id, OpenInMusic);

    /// <summary>
    /// Checks <paramref name="key"/> with a one-unit call and saves it (encrypted) only when Google accepts it. Returns the outcome
    /// ( <see cref="StreamStatus.Ok"/> when saved), updates <see cref="KeyStatus"/> and raises <see cref="Changed"/>.
    /// </summary>
    public async Task<StreamStatus> SaveKeyAsync(string key, CancellationToken ct)
    {
        key = (key ?? "").Trim();
        if (!YouTubeIds.IsKey(key))
        {
            return StreamStatus.InvalidKey;
        }

        _saving?.Cancel();
        using var saving = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        _saving = saving;
        try
        {
            var result = await SendAsync("videos", "/videos?part=id&chart=mostPopular&maxResults=1", key, saving.Token, applyEffects: false);
            if (_disposed || saving.IsCancellationRequested)
            {
                return StreamStatus.Cancelled;
            }

            if (result.Status != StreamStatus.Ok)
            {
                KeyStatus = result.Status is StreamStatus.InvalidKey or StreamStatus.ApiDisabled or StreamStatus.QuotaExceeded ? result.Status : KeyStatus;
                Raise();
                return result.Status;
            }

            try
            {
                _secrets.Write(SecretName, JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["v"] = 1,
                    ["apiKey"] = key,
                    ["checkedAtUtc"] = DateTime.UtcNow.ToString("O"),
                }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
            {
                _log.Warn("[island] YouTube: could not save the key");
                return StreamStatus.Failed;
            }

            _key = key;
            KeyStatus = StreamStatus.Ok;
            ClearState();
            _log.Info("[island] YouTube: key saved");
            Raise();
            return StreamStatus.Ok;
        }
        finally
        {
            if (ReferenceEquals(_saving, saving))
            {
                _saving = null;
            }
        }
    }

    /// <summary>Deletes the saved key, forgets every result and block, and raises <see cref="Changed"/>.</summary>
    public void RemoveKey()
    {
        _saving?.Cancel();
        _secrets.Delete(SecretName);
        _key = null;
        KeyStatus = StreamStatus.NotConfigured;
        ClearState();
        _log.Info("[island] YouTube: key removed");
        Raise();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _unblockTimer.Stop();
        _noteTimer.Stop();
        _lifetime.Cancel();
        _http?.Dispose();
        _inner.Dispose();
        _lifetime.Dispose();
    }

    private bool OpenInMusic => !string.Equals(_core.App.Get<string>(K.YouTubeOpenIn), "youtube", StringComparison.OrdinalIgnoreCase);

    private HttpClient Http => _http ??= ApiHttp.Create();

    private StreamRequest Normalize(StreamRequest request)
    {
        var query = (request.Query ?? "").Trim();
        if (query.Length > MaxQueryLength)
        {
            query = query[..MaxQueryLength];
        }

        // Albums, artists and "all" do not exist here: they are the song search.
        var scope = request.Scope == StreamScope.Playlists ? StreamScope.Playlists : StreamScope.Songs;

        // Only a song search through the web service has more pages (one token per page); the official path and playlists have one.
        var paged = _key is null && request.View == StreamView.Search && scope == StreamScope.Songs;
        return request with { Query = query, Scope = scope, Offset = paged ? Math.Clamp(request.Offset, 0, MaxSearchResults) : 0 };
    }

    private string? ReadKey()
    {
        if (!_secrets.TryRead(SecretName, out var json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("v", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var version) && version == 1
                && root.TryGetProperty("apiKey", out var k) && k.ValueKind == JsonValueKind.String && YouTubeIds.IsKey(k.GetString()))
            {
                return k.GetString();
            }
        }
        catch (JsonException)
        {
        }

        _log.Warn("[island] Music: stored sign-in unreadable, cleared (youtube)");
        _secrets.Delete(SecretName);
        return null;
    }

    private void ClearState()
    {
        _cache.Clear();
        _errors.Clear();
        _tokens.Clear();
        _quotaUntilUtc = DateTime.MinValue;
        _rateUntilUtc = DateTime.MinValue;
    }

    private void OpenItem(StreamItem item, string? playlistId)
    {
        var url = item.Kind == StreamItemKind.Playlist
            ? YouTubeLinks.Playlist(item.Id, OpenInMusic)
            : YouTubeLinks.Watch(item.Id, OpenInMusic, playlistId);
        Open(url);
    }

    private void Open(string url)
    {
        if (MusicLinks.Open(url, _log))
        {
            return;
        }

        _note = new StreamNote(Loc.T("Couldn’t open your browser."), true);
        _noteTimer.Stop();
        _noteTimer.Start();
        Raise();
    }

    private IReadOnlyList<StreamLink> BuildLinks()
    {
        var list = new List<StreamLink>
        {
            new(StreamStrings.OpenYouTubeMusic, "https://music.youtube.com"),
            new(StreamStrings.LikedMusic, "https://music.youtube.com/playlist?list=LM"),
            new(StreamStrings.Library, "https://music.youtube.com/library"),
        };
        if (_pwaAumid is not null)
        {
            list.Add(new StreamLink(StreamStrings.OpenYouTubeMusicApp, "shell:AppsFolder\\" + _pwaAumid));
        }

        return list;
    }

    /// <summary>Looks for the installed YouTube Music app once, off the UI thread (it lists Start menu apps with PowerShell).</summary>
    private void EnsureAppLookup()
    {
        if (_pwaLookupStarted || _disposed)
        {
            return;
        }

        _pwaLookupStarted = true;
        Guarded.FireAndLog(async () =>
        {
            var players = await PlaybackService.InstalledPlayersAsync();
            if (_disposed)
            {
                return;
            }

            var found = players.FirstOrDefault(p => p.Name.Replace(" ", "").Contains("youtubemusic", StringComparison.OrdinalIgnoreCase) && Aumid.IsMatch(p.Aumid ?? ""));
            if (found.Aumid is not null)
            {
                _pwaAumid = found.Aumid;
                _links = null;
                Raise();
            }
        }, _log, "YouTube Music app lookup");
    }

    private StreamLoad? Blocked()
    {
        var now = DateTime.UtcNow;
        if (now < _quotaUntilUtc)
        {
            return Error(StreamStatus.QuotaExceeded, null);
        }

        if (now < _rateUntilUtc)
        {
            return Error(StreamStatus.RateLimited, _rateUntilUtc - now);
        }

        return null;
    }

    private static StreamLoad Error(StreamStatus status, TimeSpan? retryIn) =>
        new(StreamLoadState.Error, null, status, StreamStrings.Message(status, StreamKind.YouTube, retryIn), retryIn);

    private async Task FetchAsync(StreamRequest request)
    {
        var key = _key;
        StreamLoad load;
        try
        {
            load = key is null ? await LoadInnerAsync(request, _lifetime.Token) : await LoadAsync(request, key, _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            load = Error(StreamStatus.Cancelled, null);
        }
        finally
        {
            _cache.EndFetch(request);
        }

        if (_disposed)
        {
            return;
        }

        // A key change while the call was running makes the answer stale: drop it and let the view ask again.
        if (key == _key)
        {
            if (load.State == StreamLoadState.Ready && load.Page is not null)
            {
                _cache.Set(request, load.Page);
            }
            else if (load.Status != StreamStatus.Cancelled)
            {
                _errors[request] = (load, DateTime.UtcNow + ErrorLife);
            }
        }

        Raise();
    }

    /// <summary>
    /// The keyless path. A song search asks the web service for a page and keeps the token of the next one under the request that will
    /// ask for it; a playlist search joins YouTube Music's featured playlists with the community's (one page); a container is a browse of
    /// the playlist. An answer the mappers do not recognise is "didn't respond".
    /// </summary>
    private async Task<StreamLoad> LoadInnerAsync(StreamRequest request, CancellationToken ct)
    {
        var container = request.View == StreamView.Container;
        var region = _inner.Region;
        if (container)
        {
            if (!YouTubeIds.IsPlaylist(request.Container!.Id))
            {
                return Error(StreamStatus.Failed, null);
            }

            var browseId = "VL" + request.Container.Id;
            var browse = await _inner.PostAsync("browse", v => InnerTubeMap.BrowseBody(v, region, browseId), ct);
            if (browse.Status != StreamStatus.Ok)
            {
                return FailedInner(browse);
            }

            var songs = Parse(browse.Body, InnerTubeMap.PlaylistSongs);
            return songs is null ? Error(StreamStatus.Failed, null) : Page(StreamScope.Songs, songs, 0, null);
        }

        if (request.Scope == StreamScope.Playlists)
        {
            var featured = _inner.PostAsync("search", v => InnerTubeMap.SearchBody(v, region, request.Query, InnerTubeMap.FeaturedPlaylistsParams), ct);
            var community = _inner.PostAsync("search", v => InnerTubeMap.SearchBody(v, region, request.Query, InnerTubeMap.CommunityPlaylistsParams), ct);
            var both = await Task.WhenAll(featured, community);
            var pages = both.Select(r => r.Status == StreamStatus.Ok ? Parse(r.Body, root => InnerTubeMap.Search(root, StreamItemKind.Playlist)) : null).ToList();
            if (pages.All(p => p is null))
            {
                return FailedInner(both.FirstOrDefault(r => r.Status != StreamStatus.Ok) ?? both[0]);
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var merged = pages.Where(p => p is not null).SelectMany(p => p!.Items).Where(i => seen.Add(i.Id)).ToList();
            return Page(StreamScope.Playlists, merged, 0, null);
        }

        InnerTubeResult result;
        if (request.Offset > 0)
        {
            if (!_tokens.TryGetValue(request, out var token))
            {
                return Error(StreamStatus.Failed, null);
            }

            result = await _inner.PostAsync("search", v => InnerTubeMap.ContinuationBody(v, region, token), ct);
        }
        else
        {
            result = await _inner.PostAsync("search", v => InnerTubeMap.SearchBody(v, region, request.Query, InnerTubeMap.SongsParams), ct);
        }

        if (result.Status != StreamStatus.Ok)
        {
            return FailedInner(result);
        }

        var page = Parse(result.Body, root => InnerTubeMap.Search(root, StreamItemKind.Video));
        if (page is null)
        {
            return Error(StreamStatus.Failed, null);
        }

        var next = request.Offset + page.Items.Count;
        if (page.Continuation is not null && page.Items.Count > 0 && next < MaxSearchResults)
        {
            _tokens[request with { Offset = next }] = page.Continuation;
            return Page(StreamScope.Songs, page.Items, request.Offset, next);
        }

        return Page(StreamScope.Songs, page.Items, request.Offset, null);
    }

    private StreamLoad FailedInner(InnerTubeResult result)
    {
        if (result.Status == StreamStatus.RateLimited)
        {
            ApplyFailure(StreamStatus.RateLimited, result.RetryIn);
        }

        return Error(result.Status, result.RetryIn);
    }

    /// <summary>Parses a body and maps it; null when it is not JSON or the mapper does not know its shape.</summary>
    private static T? Parse<T>(string body, Func<JsonElement, T?> map)
        where T : class
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return map(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static StreamLoad Page(StreamScope scope, List<StreamItem> items, int offset, int? nextOffset)
    {
        var title = scope == StreamScope.Playlists ? Loc.T("Playlists") : Loc.T("Songs");
        return new StreamLoad(StreamLoadState.Ready, new StreamPage([new StreamSection(scope, title, items, offset + items.Count)], nextOffset), StreamStatus.Ok);
    }

    private async Task<StreamLoad> LoadAsync(StreamRequest request, string key, CancellationToken ct)
    {
        var container = request.View == StreamView.Container;
        var playlists = !container && request.Scope == StreamScope.Playlists;
        var endpoint = container ? "playlistItems" : "search";
        var path = container
            ? "/playlistItems?part=snippet,status&maxResults=50&playlistId=" + Uri.EscapeDataString(request.Container!.Id)
            : "/search?part=snippet&maxResults=15&type=" + (playlists ? "playlist" : "video&videoCategoryId=10") + "&q=" + Uri.EscapeDataString(request.Query);
        if (container && !YouTubeIds.IsPlaylist(request.Container!.Id))
        {
            return Error(StreamStatus.Failed, null);
        }

        var result = await SendAsync(endpoint, path, key, ct, applyEffects: true);
        if (result.Status != StreamStatus.Ok)
        {
            return Error(result.Status, result.RetryIn);
        }

        List<StreamItem> items;
        try
        {
            using var doc = JsonDocument.Parse(result.Body);
            items = container
                ? YouTubeMap.PlaylistItems(doc.RootElement)
                : YouTubeMap.Search(doc.RootElement, playlists ? StreamItemKind.Playlist : StreamItemKind.Video);
        }
        catch (JsonException)
        {
            return Error(StreamStatus.Failed, null);
        }

        if (!playlists && items.Count > 0)
        {
            items = await AddDurationsAsync(items, key, ct);
        }

        var scope = playlists ? StreamScope.Playlists : StreamScope.Songs;
        var title = playlists ? Loc.T("Playlists") : Loc.T("Songs");
        return new StreamLoad(StreamLoadState.Ready, new StreamPage([new StreamSection(scope, title, items, items.Count)], null), StreamStatus.Ok);
    }

    /// <summary>One more call (1 unit) for the lengths; the list is shown without them when it fails.</summary>
    private async Task<List<StreamItem>> AddDurationsAsync(List<StreamItem> items, string key, CancellationToken ct)
    {
        var ids = string.Join(',', items.Where(i => i.Kind == StreamItemKind.Video).Select(i => i.Id).Distinct().Take(50));
        var result = await SendAsync("videos", "/videos?part=contentDetails&maxResults=50&id=" + Uri.EscapeDataString(ids), key, ct, applyEffects: true);
        if (result.Status != StreamStatus.Ok)
        {
            return items;
        }

        try
        {
            using var doc = JsonDocument.Parse(result.Body);
            return YouTubeMap.WithDurations(items, YouTubeMap.Durations(doc.RootElement));
        }
        catch (JsonException)
        {
            return items;
        }
    }

    private sealed record ApiResult(StreamStatus Status, string Body, TimeSpan? RetryIn);

    /// <summary>
    /// One GET. The key travels in a header, never in the address. Retries once after a second on a 5xx or a timeout. The log line names
    /// only the endpoint, status and time. <paramref name="applyEffects"/> false (checking a new key) leaves blocks and status alone.
    /// </summary>
    private async Task<ApiResult> SendAsync(string endpoint, string pathAndQuery, string key, CancellationToken ct, bool applyEffects)
    {
        for (var attempt = 0; ; attempt++)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                using var timeout = ApiHttp.Timeout(ct);
                using var message = new HttpRequestMessage(HttpMethod.Get, BaseUrl + pathAndQuery);
                message.Headers.TryAddWithoutValidation("X-Goog-Api-Key", key);
                using var response = await Http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                var body = await response.Content.ReadAsStringAsync(timeout.Token);
                var code = (int)response.StatusCode;
                _log.Info($"[island] YouTube GET {endpoint} -> {code} ({watch.ElapsedMilliseconds} ms)");
                if (response.IsSuccessStatusCode)
                {
                    return new ApiResult(StreamStatus.Ok, body, null);
                }

                if (code >= 500 && attempt == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), ct);
                    continue;
                }

                var status = ApiError.Classify(StreamKind.YouTube, code, body);
                if (code == 429 && status == StreamStatus.Failed)
                {
                    // Google's per-second "rateLimitExceeded" is a plain slow-down, unlike the daily quota.
                    status = StreamStatus.RateLimited;
                }

                TimeSpan? retryIn = null;
                if (status == StreamStatus.RateLimited)
                {
                    var header = response.Headers.TryGetValues("Retry-After", out var values) ? values.FirstOrDefault() : null;
                    retryIn = ApiError.RetryAfter(header);
                }

                if (applyEffects)
                {
                    ApplyFailure(status, retryIn);
                }

                return new ApiResult(status, "", retryIn);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return new ApiResult(StreamStatus.Cancelled, "", null);
            }
            catch (OperationCanceledException)
            {
                _log.Info($"[island] YouTube GET {endpoint} timed out ({watch.ElapsedMilliseconds} ms)");
                if (attempt == 0)
                {
                    continue;
                }

                return new ApiResult(StreamStatus.TimedOut, "", null);
            }
            catch (HttpRequestException ex)
            {
                var offline = ApiError.IsOffline(ex);
                _log.Info($"[island] YouTube GET {endpoint} failed ({(offline ? "offline" : "error")}, {watch.ElapsedMilliseconds} ms)");
                if (offline)
                {
                    return new ApiResult(StreamStatus.Offline, "", null);
                }

                if (attempt == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), ct);
                    continue;
                }

                return new ApiResult(StreamStatus.Failed, "", null);
            }
        }
    }

    private void ApplyFailure(StreamStatus status, TimeSpan? retryIn)
    {
        switch (status)
        {
            case StreamStatus.QuotaExceeded:
                _quotaUntilUtc = YouTubeQuota.ResetAfter(DateTime.UtcNow);
                KeyStatus = StreamStatus.QuotaExceeded;
                ScheduleUnblock(_quotaUntilUtc);
                break;
            case StreamStatus.RateLimited:
                _rateUntilUtc = DateTime.UtcNow + (retryIn ?? TimeSpan.FromSeconds(5));
                ScheduleUnblock(_rateUntilUtc);
                break;
            case StreamStatus.InvalidKey:
            case StreamStatus.ApiDisabled:
                KeyStatus = status;
                break;
        }
    }

    /// <summary>Raises <see cref="Changed"/> when a block ends, so a view that showed the error asks again.</summary>
    private void ScheduleUnblock(DateTime untilUtc)
    {
        var next = new[] { _quotaUntilUtc, _rateUntilUtc }.Where(t => t > DateTime.UtcNow).DefaultIfEmpty(untilUtc).Min();
        var wait = next - DateTime.UtcNow + TimeSpan.FromMilliseconds(250);
        // A DispatcherTimer tops out around 24 days; midnight Pacific is always well inside that.
        _unblockTimer.Stop();
        _unblockTimer.Interval = wait > TimeSpan.Zero ? wait : TimeSpan.FromMilliseconds(250);
        _unblockTimer.Start();
    }

    private void Raise()
    {
        if (_disposed)
        {
            return;
        }

        if (_core.Ui.CheckAccess())
        {
            Changed?.Invoke();
        }
        else
        {
            _core.Ui.BeginInvoke(() =>
            {
                if (!_disposed)
                {
                    Changed?.Invoke();
                }
            });
        }
    }
}
