// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Trayloom.Core;

namespace Trayloom.Features.Island;

/// <summary>What the Spotify sign-in asks for. The app has no client secret: PKCE proves the request.</summary>
internal static class SpotifyAuth
{
    /// <summary>The maintainer's own Client ID, shared by friends added to its Spotify app; empty in the public source.</summary>
    public const string BuiltInClientId = "";

    /// <summary>The permissions asked for: read what is playing, the user's playlists, liked and recent songs, and control playback. Nothing that edits a library.</summary>
    public const string Scopes = "user-read-playback-state user-modify-playback-state user-read-currently-playing user-library-read playlist-read-private playlist-read-collaborative user-read-recently-played";

    /// <summary>The length of a Client ID from the Spotify Dashboard.</summary>
    public const int ClientIdLength = 32;

    /// <summary>
    /// A Spotify Client ID is 32 hexadecimal characters. A wrong one only shows an error page on Spotify's side and the sign-in would wait
    /// for the browser until it times out, so it is refused before anything starts.
    /// </summary>
    public static bool IsClientIdFormat(string? value) =>
        value is { Length: ClientIdLength } && value.All(Uri.IsHexDigit);

    /// <summary>The page the user approves Trayloom on, with the redirect, scopes, state and S256 challenge escaped into it.</summary>
    public static string AuthorizeUrl(string clientId, string challenge, string state) =>
        "https://accounts.spotify.com/authorize?response_type=code"
        + "&client_id=" + Uri.EscapeDataString(clientId)
        + "&redirect_uri=" + Uri.EscapeDataString(SpotifyService.RedirectUri)
        + "&scope=" + Uri.EscapeDataString(Scopes)
        + "&state=" + Uri.EscapeDataString(state)
        + "&code_challenge_method=S256"
        + "&code_challenge=" + Uri.EscapeDataString(challenge)
        + "&show_dialog=true";
}

/// <summary>
/// Spotify for the island's Library: sign-in with PKCE through a loopback redirect, token refresh, and cached lists from the Web
/// API. A UI-thread object: methods are called there, continuations resume there and <see cref="Changed"/> fires there.
/// Playback commands live in <c>Music.Spotify.Player.cs</c>. Nothing here ever edits the user's library or playlists.
/// </summary>
internal sealed partial class SpotifyService : IMusicService
{
    /// <summary>The fixed redirect port; the user adds the matching URI to their Spotify app.</summary>
    public const int Port = 43118;

    /// <summary>The redirect URI to register (Spotify demands an IP literal, not <c>localhost</c>).</summary>
    public const string RedirectUri = "http://127.0.0.1:43118/callback";

    private const string StoreName = "spotify";
    private const string ApiBase = "https://api.spotify.com/v1/";
    private const string TokenUrl = "https://accounts.spotify.com/api/token";

    /// <summary>The request key of the "Up next" list; <see cref="Retry"/> with it refreshes the queue.</summary>
    public static readonly StreamRequest QueueRequest = new(StreamView.Container, "queue");

    private readonly IslandCore _core;
    private readonly ILog _log;
    private readonly HttpClient _http = ApiHttp.Create();
    private readonly SecretStore _store;
    private readonly StreamCache _cache = new(() => DateTime.UtcNow, TimeSpan.FromMinutes(5));
    private readonly StreamCache _queueCache = new(() => DateTime.UtcNow, TimeSpan.FromSeconds(30), 2);
    private readonly Dictionary<StreamRequest, StreamLoad> _failed = [];
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    private TokenSet? _tokens;
    private bool _needsReconnect;
    private bool _connecting;
    private bool _disposed;
    private int _epoch;
    private string? _userId;
    private DateTime _blockedUntil = DateTime.MinValue;
    private CancellationTokenSource? _connectCts;
    private Task<StreamStatus>? _flow;
    private CancellationTokenSource? _searchCts;
    private StreamNote? _note;
    private int _noteGeneration;
    private string? _busyKey;

    public SpotifyService(IslandCore core)
    {
        _core = core;
        _log = core.Ctx.Log;
        _store = SecretStore.Default(_log);
        ReloadTokens();
        _core.App.Changed += OnSettingChanged;
    }

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public StreamKind Kind => StreamKind.Spotify;

    /// <inheritdoc />
    public string Name => "Spotify";

    /// <summary>The Client ID in use: the user's own, else the built-in one.</summary>
    public string ClientId
    {
        get
        {
            var own = _core.App.Get<string>(K.SpotifyClientId)?.Trim();
            return string.IsNullOrEmpty(own) ? SpotifyAuth.BuiltInClientId : own;
        }
    }

    /// <summary>The signed-in account's display name; empty when unknown or signed out.</summary>
    public string DisplayName => _tokens?.DisplayName ?? "";

    /// <summary>While a sign-in waits and the browser could not be opened: the address to copy into one by hand.</summary>
    public string? ManualSignInUrl { get; private set; }

    /// <inheritdoc />
    public StreamConnection Connection =>
        string.IsNullOrEmpty(ClientId) ? StreamConnection.NotConfigured
        : _connecting ? StreamConnection.Connecting
        : _needsReconnect ? StreamConnection.NeedsReconnect
        : _tokens is not null ? StreamConnection.Connected
        : StreamConnection.Disconnected;

    /// <inheritdoc />
    public bool CanBrowse => Connection == StreamConnection.Connected;

    /// <inheritdoc />
    public bool ShowInIsland => _core.App.Get<bool>(K.MusicSpotify);

    /// <inheritdoc />
    public bool SearchAsYouType => true;

    /// <inheritdoc />
    public IReadOnlyList<StreamView> Tabs { get; } = [StreamView.Playlists, StreamView.Liked, StreamView.Recent];

    /// <inheritdoc />
    public IReadOnlyList<StreamScope> Scopes { get; } = [StreamScope.All, StreamScope.Songs, StreamScope.Albums, StreamScope.Artists, StreamScope.Playlists];

    /// <inheritdoc />
    public bool CanQueue => true;

    /// <inheritdoc />
    public bool? CanPlayHere => _tokens?.Premium;

    /// <inheritdoc />
    public IReadOnlyList<StreamLink> Links { get; } = [];

    /// <inheritdoc />
    public StreamNote? Note => _note;

    /// <inheritdoc />
    public string? BusyKey => _busyKey;

    // ------------------------------------------------------------------ sign-in

    /// <summary>
    /// Signs in through the browser. One flow runs at a time: a new call cancels the running one. Returns Ok once the tokens are
    /// saved, otherwise why not (the status maps to text with <see cref="StreamStrings.Message"/>).
    /// </summary>
    public async Task<StreamStatus> ConnectAsync(CancellationToken ct)
    {
        var clientId = ClientId;
        if (string.IsNullOrEmpty(clientId))
        {
            return StreamStatus.NotConfigured;
        }

        if (!SpotifyAuth.IsClientIdFormat(clientId))
        {
            // Before the listener starts and the browser opens: nothing to cancel, nothing sent.
            _log.Info("[island] Spotify: sign-in -> " + StreamStatus.InvalidClient + " (Client ID format)");
            return StreamStatus.InvalidClient;
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
        var previous = _flow;
        _connectCts?.Cancel();
        _connectCts = cts;
        _connecting = true;
        ManualSignInUrl = null;
        RaiseChanged();

        var flow = RunFlowAsync(clientId, cts, previous);
        _flow = flow;
        return await flow;
    }

    /// <summary>Cancels a sign-in that is waiting for the browser.</summary>
    public void CancelConnect() => _connectCts?.Cancel();

    /// <summary>Signs out: deletes the stored sign-in and every cached list. The Spotify account keeps the app authorised until the user removes it there.</summary>
    public void Disconnect()
    {
        CancelConnect();
        _store.Delete(StoreName);
        ResetSession(needsReconnect: false);
        _log.Info("[island] Spotify: disconnected");
        RaiseChanged();
    }

    private async Task<StreamStatus> RunFlowAsync(string clientId, CancellationTokenSource cts, Task<StreamStatus>? previous)
    {
        StreamStatus status;
        try
        {
            if (previous is not null)
            {
                // The earlier flow must have released the port before this one binds it.
                await previous;
            }

            cts.Token.ThrowIfCancellationRequested();
            status = await ConnectCoreAsync(clientId, cts.Token);
        }
        catch (OperationCanceledException)
        {
            status = StreamStatus.Cancelled;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Only the type: the message of an HTTP or socket failure could carry an address.
            _log.Warn("[island] Spotify: sign-in failed (" + ex.GetType().Name + ")");
            status = StreamStatus.Failed;
        }
        finally
        {
            if (ReferenceEquals(_connectCts, cts))
            {
                _connectCts = null;
                _connecting = false;
                if (ManualSignInUrl is not null)
                {
                    // The "copy this link" line belongs to the wait that just ended.
                    ClearNote();
                }

                ManualSignInUrl = null;
            }

            cts.Dispose();
        }

        _log.Info("[island] Spotify: sign-in -> " + status);
        RaiseChanged();
        return status;
    }

    private async Task<StreamStatus> ConnectCoreAsync(string clientId, CancellationToken ct)
    {
        LoopbackRedirect? started;
        try
        {
            started = LoopbackRedirect.Start(Port);
        }
        catch (PortBusyException)
        {
            return StreamStatus.PortBusy;
        }

        await using var redirect = started;
        var verifier = Pkce.CreateVerifier();
        var state = Pkce.CreateState();
        var url = SpotifyAuth.AuthorizeUrl(clientId, Pkce.Challenge(verifier), state);
        if (!MusicLinks.Open(url, _log))
        {
            ManualSignInUrl = url;
            SetNote(Loc.T("Couldn’t open your browser. Copy this link into it:"), error: true, milliseconds: 0);
        }

        OAuthCallback callback;
        Func<string, Task> respond;
        try
        {
            (callback, respond) = await redirect.WaitAsync(TimeSpan.FromMinutes(5), ct);
        }
        catch (TimeoutException)
        {
            return StreamStatus.TimedOut;
        }

        if (callback.Kind == OAuthKind.Error)
        {
            return await FailAsync(callback.Error == "access_denied" ? StreamStatus.Denied : StreamStatus.Failed, respond);
        }

        if (!Pkce.StateMatches(state, callback.State))
        {
            return await FailAsync(StreamStatus.StateMismatch, respond);
        }

        // The browser connection stays open (a spinner) while the code is exchanged, so the page can report the real outcome.
        var grant = await TokenCallAsync(
            [
                new("grant_type", "authorization_code"),
                new("code", callback.Code),
                new("redirect_uri", RedirectUri),
                new("client_id", clientId),
                new("code_verifier", verifier),
            ],
            ct);
        if (!grant.Reply.Ok || grant.Grant is not { Refresh: { Length: > 0 } } granted)
        {
            var failure = grant.Reply.Ok ? StreamStatus.Failed
                : grant.Reply.Code == 400 && grant.Reply.Body.Contains("invalid_client", StringComparison.Ordinal) ? StreamStatus.InvalidClient
                : grant.Reply.Status;
            return await FailAsync(failure, respond);
        }

        var me = await TransportAsync(
            () => Authorized(HttpMethod.Get, "me", null, granted.Access),
            "GET", "me", retryOnServerError: true, quiet: false, ct);
        if (!me.Ok)
        {
            if (me.Status == StreamStatus.NotAllowlisted)
            {
                _store.Delete(StoreName);
            }

            return await FailAsync(me.Status, respond);
        }

        string display = "";
        try
        {
            using var doc = JsonDocument.Parse(me.Body);
            display = Str(doc.RootElement, "display_name") ?? Str(doc.RootElement, "id") ?? "";
            _userId = Str(doc.RootElement, "id");
        }
        catch (JsonException)
        {
            // An answer we cannot read still proves the sign-in works; the name just stays empty.
        }

        var tokens = new TokenSet(clientId, granted.Access, granted.Refresh, DateTime.UtcNow.AddSeconds(granted.ExpiresIn), granted.Scope, null, display);
        try
        {
            _store.Write(StoreName, tokens.ToJson());
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or IOException or UnauthorizedAccessException)
        {
            return await FailAsync(StreamStatus.Failed, respond);
        }

        ResetSession(needsReconnect: false);
        _tokens = tokens;
        _core.App.Set(K.MusicSpotify, true);
        _log.Info("[island] Spotify: connected yes");
        await respond(LoopbackRedirect.Page("Trayloom", Loc.T("Spotify is connected. You can close this tab and go back to Trayloom.")));
        return StreamStatus.Ok;
    }

    private static async Task<StreamStatus> FailAsync(StreamStatus status, Func<string, Task> respond)
    {
        var reason = StreamStrings.Message(status, StreamKind.Spotify).TrimEnd('.');
        if (reason.Length == 0)
        {
            reason = Loc.T("Something went wrong");
        }

        await respond(LoopbackRedirect.Page("Trayloom", Loc.F("Spotify didn’t connect: {0}. You can close this tab.", reason)));
        return status;
    }

    // ------------------------------------------------------------------ tokens

    private sealed record Grant(string Access, string? Refresh, int ExpiresIn, string Scope);

    private async Task<(Reply Reply, Grant? Grant)> TokenCallAsync(KeyValuePair<string, string>[] form, CancellationToken ct)
    {
        var reply = await TransportAsync(
            () => new HttpRequestMessage(HttpMethod.Post, TokenUrl) { Content = new FormUrlEncodedContent(form) },
            "POST", "token", retryOnServerError: false, quiet: false, ct);
        if (!reply.Ok)
        {
            return (reply, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(reply.Body);
            var root = doc.RootElement;
            if (Str(root, "access_token") is { Length: > 0 } access)
            {
                var seconds = root.TryGetProperty("expires_in", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : 3600;
                return (reply, new Grant(access, Str(root, "refresh_token"), seconds, Str(root, "scope") ?? ""));
            }
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            // handled below as an unusable answer
        }

        return (reply with { Status = StreamStatus.Failed }, null);
    }

    /// <summary>A usable access token, refreshing it first when it is about to expire.</summary>
    private async Task<(StreamStatus Status, string Access)> EnsureAccessAsync(CancellationToken ct)
    {
        var tokens = _tokens;
        if (tokens is null)
        {
            return (_needsReconnect ? StreamStatus.Revoked : StreamStatus.NotConnected, "");
        }

        if (tokens.NeedsRefresh(DateTime.UtcNow))
        {
            var status = await RefreshAsync(force: false, failedAccess: null, ct);
            if (status != StreamStatus.Ok)
            {
                return (status, "");
            }
        }

        return _tokens is { } current ? (StreamStatus.Ok, current.Access) : (StreamStatus.NotConnected, "");
    }

    /// <summary>
    /// Exchanges the refresh token for a new access token under one lock (so parallel requests refresh once). With
    /// <paramref name="force"/> it refreshes even if the token looks valid, unless another caller already replaced
    /// <paramref name="failedAccess"/>. The rotated refresh token is saved at once.
    /// </summary>
    private async Task<StreamStatus> RefreshAsync(bool force, string? failedAccess, CancellationToken ct)
    {
        await _refreshGate.WaitAsync(ct);
        try
        {
            var current = _tokens;
            if (current is null)
            {
                return _needsReconnect ? StreamStatus.Revoked : StreamStatus.NotConnected;
            }

            if (force ? failedAccess is not null && current.Access != failedAccess : !current.NeedsRefresh(DateTime.UtcNow))
            {
                return StreamStatus.Ok;
            }

            if (IsBlocked(out _))
            {
                return StreamStatus.RateLimited;
            }

            var result = await TokenCallAsync(
                [
                    new("grant_type", "refresh_token"),
                    new("refresh_token", current.Refresh),
                    new("client_id", current.ClientId),
                ],
                ct);
            if (!result.Reply.Ok || result.Grant is not { } grant)
            {
                if (result.Reply.Code == 400 && result.Reply.Body.Contains("invalid_grant", StringComparison.Ordinal))
                {
                    _log.Warn("[island] Spotify: access ended, reconnect needed");
                    _store.Delete(StoreName);
                    ResetSession(needsReconnect: true);
                    RaiseChanged();
                    return StreamStatus.Revoked;
                }

                // Offline or a server fault: keep the sign-in, the next request tries again.
                return result.Reply.Ok ? StreamStatus.Failed : result.Reply.Status;
            }

            var latest = _tokens;
            if (latest is null)
            {
                return StreamStatus.NotConnected;
            }

            var updated = latest with
            {
                Access = grant.Access,
                Refresh = string.IsNullOrEmpty(grant.Refresh) ? latest.Refresh : grant.Refresh,
                ExpiresAtUtc = DateTime.UtcNow.AddSeconds(grant.ExpiresIn),
                Scope = string.IsNullOrEmpty(grant.Scope) ? latest.Scope : grant.Scope,
            };
            _tokens = updated;
            Persist(updated);
            return StreamStatus.Ok;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private void Persist(TokenSet tokens)
    {
        try
        {
            _store.Write(StoreName, tokens.ToJson());
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or IOException or UnauthorizedAccessException)
        {
            // The session still works from memory; the user would have to sign in again after a restart.
            _log.Warn("[island] Spotify: could not save the sign-in");
        }
    }

    /// <summary>Loads the stored sign-in; one made with another Client ID than the configured one counts as no sign-in.</summary>
    private void ReloadTokens()
    {
        TokenSet? found = null;
        if (_store.TryRead(StoreName, out var json))
        {
            found = TokenSet.FromJson(json);
            if (found is null)
            {
                _store.Delete(StoreName);
            }
        }

        var clientId = ClientId;
        _tokens = found is not null && clientId.Length > 0 && found.ClientId == clientId ? found : null;
    }

    private void ResetSession(bool needsReconnect)
    {
        _tokens = null;
        _needsReconnect = needsReconnect;
        _userId = null;
        _epoch++;
        _searchCts?.Cancel();
        _cache.Clear();
        _queueCache.Clear();
        _failed.Clear();
        _note = null;
        _busyKey = null;
        SetTimeline(null);
    }

    private void OnSettingChanged(string key)
    {
        if (key is K.SpotifyClientId or K.MusicSpotify && !_disposed)
        {
            _core.Ui.BeginInvoke(() =>
            {
                if (_disposed)
                {
                    return;
                }

                if (key == K.SpotifyClientId && _tokens is { } tokens && tokens.ClientId != ClientId)
                {
                    ResetSession(needsReconnect: false);
                }

                RaiseChanged();
            });
        }
    }

    private void SetPremium(bool premium)
    {
        if (_tokens is { } tokens && tokens.Premium != premium)
        {
            _tokens = tokens with { Premium = premium };
            Persist(_tokens);
            RaiseChanged();
        }
    }

    // ------------------------------------------------------------------ lists

    /// <inheritdoc />
    public StreamLoad Get(StreamRequest request)
    {
        if (string.IsNullOrEmpty(ClientId))
        {
            return Failure(StreamStatus.NotConfigured);
        }

        if (_tokens is null)
        {
            return Failure(_needsReconnect ? StreamStatus.Revoked : StreamStatus.NotConnected);
        }

        if (request.View == StreamView.Search && request.Query.Trim().Length < 2)
        {
            return new StreamLoad(StreamLoadState.Ready, new StreamPage([], null), StreamStatus.Ok);
        }

        var cache = CacheFor(request);
        if (cache.TryGet(request, out var page))
        {
            return new StreamLoad(StreamLoadState.Ready, page, StreamStatus.Ok);
        }

        if (_failed.TryGetValue(request, out var failed))
        {
            return failed;
        }

        if (IsBlocked(out var remaining))
        {
            return Failure(StreamStatus.RateLimited, remaining);
        }

        if (cache.BeginFetch(request))
        {
            Guarded.FireAndLog(() => FetchAsync(request), _log, "Spotify list");
        }

        return new StreamLoad(StreamLoadState.Loading, null, StreamStatus.Ok);
    }

    /// <inheritdoc />
    public void Retry(StreamRequest request)
    {
        CacheFor(request).Remove(request);
        _failed.Remove(request);
        RaiseChanged();
    }

    private StreamCache CacheFor(StreamRequest request) => request == QueueRequest ? _queueCache : _cache;

    private static StreamLoad Failure(StreamStatus status, TimeSpan? retryIn = null) =>
        new(StreamLoadState.Error, null, status, StreamStrings.Message(status, StreamKind.Spotify, retryIn), retryIn);

    private async Task FetchAsync(StreamRequest request)
    {
        var epoch = _epoch;
        var cache = CacheFor(request);
        CancellationTokenSource? own = null;
        var token = _disposeCts.Token;
        if (request.View == StreamView.Search)
        {
            // Typing replaces the search in flight; browsing loads are left to finish so the lists are warm when the user returns.
            _searchCts?.Cancel();
            own = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
            _searchCts = own;
            token = own.Token;
        }

        try
        {
            var outcome = await LoadAsync(request, token);
            if (epoch != _epoch || _disposed)
            {
                return;
            }

            if (outcome.Status == StreamStatus.Ok && outcome.Page is { } page)
            {
                cache.Set(request, page);
                _failed.Remove(request);
            }
            else if (outcome.Status != StreamStatus.Cancelled)
            {
                _failed[request] = Failure(outcome.Status, outcome.RetryIn);
            }
        }
        finally
        {
            cache.EndFetch(request);
            if (ReferenceEquals(_searchCts, own))
            {
                _searchCts = null;
            }

            own?.Dispose();
            RaiseChanged();
        }
    }

    private sealed record Outcome(StreamStatus Status, StreamPage? Page, TimeSpan? RetryIn = null);

    private static Outcome Failed(Reply reply) => new(reply.Status, null, reply.RetryIn);

    private async Task<Outcome> LoadAsync(StreamRequest request, CancellationToken ct)
    {
        var offset = Math.Max(0, request.Offset);
        switch (request.View)
        {
            case StreamView.Search:
                return await SearchAsync(request, ct);

            case StreamView.Playlists:
                await EnsureUserIdAsync(ct);
                return await PagedAsync("me/playlists", $"me/playlists?limit=50&offset={offset}", offset, 50, ct, root => SpotifyMap.Playlists(root, _userId));

            case StreamView.Liked:
                return await PagedAsync("me/tracks", $"me/tracks?limit=50&offset={offset}&market=from_token", offset, 50, ct, SpotifyMap.Liked);

            case StreamView.Recent:
                return await PagedAsync("me/player/recently-played", "me/player/recently-played?limit=50", 0, 0, ct, SpotifyMap.Recent);

            case StreamView.Container:
                return request == QueueRequest
                    ? await PagedAsync("me/player/queue", "me/player/queue", 0, 0, ct, SpotifyMap.Queue)
                    : await ContainerAsync(request, offset, ct);

            default:
                return new Outcome(StreamStatus.Failed, null);
        }
    }

    private async Task<Outcome> SearchAsync(StreamRequest request, CancellationToken ct)
    {
        var query = request.Query.Trim();
        if (query.Length > 200)
        {
            query = query[..200];
        }

        var text = Uri.EscapeDataString(query);
        if (request.Scope == StreamScope.All)
        {
            await EnsureUserIdAsync(ct);
            return await ReadAsync("search", $"search?q={text}&type=track,album,artist,playlist&limit=5&market=from_token", ct,
                root => new StreamPage(SpotifyMap.Search(root, _userId), null));
        }

        var type = request.Scope switch
        {
            StreamScope.Songs => "track",
            StreamScope.Albums => "album",
            StreamScope.Artists => "artist",
            _ => "playlist",
        };
        var offset = Math.Min(request.Offset, SpotifyMap.SearchDepth - 10);
        if (request.Scope == StreamScope.Playlists)
        {
            await EnsureUserIdAsync(ct);
        }

        return await ReadAsync("search", $"search?q={text}&type={type}&limit=10&offset={Math.Max(0, offset)}&market=from_token", ct, root =>
        {
            var sections = SpotifyMap.Search(root, _userId);
            var total = sections.Count > 0 ? sections[0].Total : 0;
            var next = Math.Max(0, offset) + 10;
            return new StreamPage(sections, next < Math.Min(total, SpotifyMap.SearchDepth) ? next : null);
        });
    }

    private async Task<Outcome> ContainerAsync(StreamRequest request, int offset, CancellationToken ct)
    {
        var container = request.Container;
        if (container is null || !SpotifyUri.IsId(container.Id))
        {
            return new Outcome(StreamStatus.Failed, null);
        }

        switch (container.Kind)
        {
            case StreamItemKind.Album:
                return await PagedAsync("albums/tracks", $"albums/{container.Id}/tracks?limit=50&offset={offset}&market=from_token", offset, 50, ct, root => SpotifyMap.AlbumTracks(root, container));

            case StreamItemKind.Playlist:
            {
                if (!container.Owned)
                {
                    return new Outcome(StreamStatus.NotListed, null);
                }

                var result = await PagedAsync("playlists/items", $"playlists/{container.Id}/items?limit=50&offset={offset}&additional_types=track", offset, 50, ct, SpotifyMap.PlaylistItems);
                return result.Status is StreamStatus.Failed or StreamStatus.NotAllowlisted ? new Outcome(StreamStatus.NotListed, null) : result;
            }

            case StreamItemKind.Artist:
            {
                var result = await PagedAsync("artists/albums", $"artists/{container.Id}/albums?include_groups=album,single&limit=20&offset={offset}", offset, 20, ct, SpotifyMap.ArtistAlbums);
                // The albums are a bonus (Spotify may not serve them to this app): without them the pills still work.
                return result.Status is StreamStatus.Failed or StreamStatus.NotAllowlisted or StreamStatus.PremiumRequired
                    ? new Outcome(StreamStatus.Ok, new StreamPage([], null))
                    : result;
            }

            default:
                return new Outcome(StreamStatus.Failed, null);
        }
    }

    private async Task<Outcome> PagedAsync(string name, string path, int offset, int step, CancellationToken ct, Func<JsonElement, StreamSection> map) =>
        await ReadAsync(name, path, ct, root =>
        {
            var hasNext = step > 0 && root.ValueKind == JsonValueKind.Object && root.TryGetProperty("next", out var n) && n.ValueKind == JsonValueKind.String;
            return new StreamPage([map(root)], hasNext ? offset + step : null);
        });

    private async Task<Outcome> ReadAsync(string name, string path, CancellationToken ct, Func<JsonElement, StreamPage> map)
    {
        var reply = await GetAsync(name, path, ct);
        if (!reply.Ok)
        {
            return Failed(reply);
        }

        try
        {
            using var doc = JsonDocument.Parse(reply.Body);
            return new Outcome(StreamStatus.Ok, map(doc.RootElement));
        }
        catch (JsonException)
        {
            return new Outcome(StreamStatus.Failed, null);
        }
    }

    /// <summary>Learns the user's id once per session (playlists are "owned" by comparing it); a failure only leaves playlists marked not owned.</summary>
    private async Task EnsureUserIdAsync(CancellationToken ct)
    {
        if (_userId is not null)
        {
            return;
        }

        var reply = await GetAsync("me", "me", ct);
        if (reply.Ok)
        {
            try
            {
                using var doc = JsonDocument.Parse(reply.Body);
                _userId = Str(doc.RootElement, "id");
            }
            catch (JsonException)
            {
                // left unknown
            }
        }
    }

    // ------------------------------------------------------------------ HTTP

    /// <summary>One answer: a status (Ok for 2xx), the HTTP code (0 when nothing arrived) and the body. Holds no secrets of its own, but the body can: never log it.</summary>
    private sealed record Reply(StreamStatus Status, int Code, string Body, TimeSpan? RetryIn = null)
    {
        public bool Ok => Status == StreamStatus.Ok;
    }

    /// <summary>A GET with the access token. Repeats once after a server error or timeout, and once after refreshing on a 401.</summary>
    private Task<Reply> GetAsync(string name, string path, CancellationToken ct, bool quiet = false) =>
        AuthorizedAsync(HttpMethod.Get, name, path, null, quiet, ct);

    /// <summary>
    /// A playback command. Throws for anything not in <see cref="SpotifyWrites.Allowed"/>: this is the guard that keeps Trayloom from
    /// ever editing the user's library. Never repeated after an error (a second queue request would add the song twice), except
    /// once after a 401 refresh.
    /// </summary>
    private Task<Reply> SendWriteAsync(HttpMethod method, string name, string path, string? body, CancellationToken ct)
    {
        if (!SpotifyWrites.Allowed(path) || (method != HttpMethod.Put && method != HttpMethod.Post))
        {
            throw new InvalidOperationException("Spotify write not allowed: " + name);
        }

        return AuthorizedAsync(method, name, path, body, quiet: false, ct);
    }

    private async Task<Reply> AuthorizedAsync(HttpMethod method, string name, string path, string? body, bool quiet, CancellationToken ct)
    {
        if (IsBlocked(out var remaining))
        {
            return new Reply(StreamStatus.RateLimited, 429, "", remaining);
        }

        var (status, access) = await EnsureAccessAsync(ct);
        if (status != StreamStatus.Ok)
        {
            return new Reply(status, 0, "");
        }

        var get = method == HttpMethod.Get;
        var reply = await TransportAsync(() => Authorized(method, path, body, access), method.Method, name, get, quiet, ct);
        if (reply.Status == StreamStatus.Revoked)
        {
            // 401: the token was revoked or expired early. Refresh once and replay; a second 401 is reported as is.
            var refreshed = await RefreshAsync(force: true, failedAccess: access, ct);
            if (refreshed != StreamStatus.Ok)
            {
                return new Reply(refreshed, 0, "");
            }

            if (_tokens is { } current)
            {
                reply = await TransportAsync(() => Authorized(method, path, body, current.Access), method.Method, name, get, quiet, ct);
            }
        }

        return reply;
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string? body, string access)
    {
        var request = new HttpRequestMessage(method, ApiBase + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }
        else if (method != HttpMethod.Get)
        {
            // Spotify rejects a PUT or POST without a length.
            request.Content = new StringContent("");
        }

        return request;
    }

    /// <summary>Sends one request (twice at most, see <paramref name="retryOnServerError"/>), classifies the answer and logs endpoint, status and time only.</summary>
    private async Task<Reply> TransportAsync(Func<HttpRequestMessage> build, string method, string name, bool retryOnServerError, bool quiet, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var watch = Stopwatch.StartNew();
            Reply reply;
            using (var timeout = ApiHttp.Timeout(ct))
            {
                try
                {
                    using var request = build();
                    using var response = await _http.SendAsync(request, timeout.Token);
                    var body = await response.Content.ReadAsStringAsync(timeout.Token);
                    var code = (int)response.StatusCode;
                    if (response.IsSuccessStatusCode)
                    {
                        reply = new Reply(StreamStatus.Ok, code, body);
                    }
                    else
                    {
                        string? header = null;
                        if (code == 429 && response.Headers.TryGetValues("Retry-After", out var values))
                        {
                            header = values.FirstOrDefault();
                        }

                        reply = new Reply(ApiError.Classify(StreamKind.Spotify, code, body), code, body, code == 429 ? ApiError.RetryAfter(header) : null);
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // The 15 s limit, not the caller giving up.
                    reply = new Reply(StreamStatus.Failed, 0, "");
                }
                catch (HttpRequestException ex)
                {
                    reply = new Reply(ApiError.IsOffline(ex) ? StreamStatus.Offline : StreamStatus.Failed, 0, "");
                }
            }

            if (!(quiet && reply.Ok))
            {
                var outcome = reply.Ok ? reply.Code.ToString() : (reply.Code == 0 ? "" : reply.Code + " ") + reply.Status;
                var line = $"[island] Spotify {method} {name} -> {outcome} ({watch.ElapsedMilliseconds} ms)";
                if (reply.Ok)
                {
                    _log.Info(line);
                }
                else
                {
                    _log.Warn(line);
                }
            }

            var serverTrouble = reply.Code >= 500 || (reply.Code == 0 && reply.Status == StreamStatus.Failed);
            if (attempt == 0 && retryOnServerError && serverTrouble)
            {
                await Task.Delay(1000, ct);
                continue;
            }

            if (reply.Code == 429)
            {
                ApplyBlock(reply.RetryIn ?? TimeSpan.FromSeconds(5));
            }

            return reply;
        }
    }

    private bool IsBlocked(out TimeSpan remaining)
    {
        remaining = _blockedUntil - DateTime.UtcNow;
        return remaining > TimeSpan.Zero;
    }

    /// <summary>After a 429: no requests until Spotify's time is up, then tell the panel so it asks again.</summary>
    private void ApplyBlock(TimeSpan retry)
    {
        var until = DateTime.UtcNow + retry;
        if (until <= _blockedUntil)
        {
            return;
        }

        _blockedUntil = until;
        Guarded.FireAndLog(
            async () =>
            {
                await Task.Delay(retry + TimeSpan.FromMilliseconds(100), _disposeCts.Token);
                if (DateTime.UtcNow >= _blockedUntil)
                {
                    foreach (var key in _failed.Where(f => f.Value.Status == StreamStatus.RateLimited).Select(f => f.Key).ToList())
                    {
                        _failed.Remove(key);
                    }

                    RaiseChanged();
                }
            },
            _log,
            "Spotify rate-limit timer");
    }

    // ------------------------------------------------------------------ notes and events

    /// <summary>Shows a line under the list; it disappears after <paramref name="milliseconds"/> (0 = until replaced).</summary>
    private void SetNote(string text, bool error, int milliseconds = 3000)
    {
        _note = new StreamNote(text, error);
        var generation = ++_noteGeneration;
        RaiseChanged();
        if (milliseconds > 0)
        {
            Guarded.FireAndLog(
                async () =>
                {
                    await Task.Delay(milliseconds, _disposeCts.Token);
                    if (generation == _noteGeneration && !_disposed)
                    {
                        _note = null;
                        RaiseChanged();
                    }
                },
                _log,
                "Spotify note timer");
        }
    }

    private void ClearNote()
    {
        _noteGeneration++;
        _note = null;
    }

    private void RaiseChanged()
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

    /// <inheritdoc />
    public string ShareUrl(StreamItem item) => SpotifyUri.LinkFor(SpotifyUri.TypeOf(item.Kind), item.Id);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _core.App.Changed -= OnSettingChanged;
        _connectCts?.Cancel();
        _searchCts?.Cancel();
        StopTimeline();
        _disposeCts.Cancel();
        _http.Dispose();
    }

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
