// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.Island;

/// <summary>Which streaming service a request, status or message belongs to.</summary>
internal enum StreamKind
{
    Spotify,
    YouTube,
}

/// <summary>What a row of a streaming list is.</summary>
internal enum StreamItemKind
{
    Track,
    Album,
    Artist,
    Playlist,
    Video,
}

/// <summary>Which list a <see cref="StreamRequest"/> asks for.</summary>
internal enum StreamView
{
    Search,
    Playlists,
    Liked,
    Recent,
    Container,
}

/// <summary>The kind of result a search is narrowed to.</summary>
internal enum StreamScope
{
    All,
    Songs,
    Albums,
    Artists,
    Playlists,
}

/// <summary>The outcome of a call to a streaming service; <see cref="StreamStrings.Message"/> turns it into text.</summary>
internal enum StreamStatus
{
    Ok,
    NotConfigured,
    NotConnected,
    Offline,
    RateLimited,
    QuotaExceeded,
    PremiumRequired,
    NoDevice,
    AppMissing,
    NotAllowlisted,
    Revoked,
    InvalidKey,
    ApiDisabled,
    NotListed,
    Failed,
    Cancelled,
    PortBusy,
    TimedOut,
    Denied,
    StateMismatch,
    InvalidClient,
}

/// <summary>Whether a service has what it needs to talk to its provider.</summary>
internal enum StreamConnection
{
    NotConfigured,
    Disconnected,
    Connecting,
    Connected,
    NeedsReconnect,
}

/// <summary>Where a requested list stands.</summary>
internal enum StreamLoadState
{
    Loading,
    Ready,
    Error,
}

/// <summary>One row: a song, album, artist, playlist or video.</summary>
internal sealed record StreamItem(StreamItemKind Kind, string Id, string Title, string Subtitle)
{
    /// <summary>Length in seconds; 0 when unknown.</summary>
    public double Duration { get; init; }

    /// <summary>Artwork address, already checked against <see cref="StreamArt.Allowed"/>.</summary>
    public string? ImageUrl { get; init; }

    /// <summary>The provider's URI (<c>spotify:track:ID</c>); empty for YouTube.</summary>
    public string Uri { get; init; } = "";

    /// <summary>Spotify album or recently-played context the item can be played within.</summary>
    public string? ContextUri { get; init; }

    /// <summary>Spotify album id of a track.</summary>
    public string? AlbumId { get; init; }

    /// <summary>Spotify album name of a track (the title of the list "Go to album" opens).</summary>
    public string? AlbumName { get; init; }

    /// <summary>Spotify id of a track's first artist.</summary>
    public string? ArtistId { get; init; }

    /// <summary>False for local files and tracks the provider marks unavailable.</summary>
    public bool Playable { get; init; } = true;

    /// <summary>Spotify playlist the user owns or collaborates on (the only ones whose songs can be listed).</summary>
    public bool Owned { get; init; }

    /// <summary>Number of entries in a playlist or album; 0 when unknown.</summary>
    public int Count { get; init; }

    /// <summary>Identity within a list.</summary>
    public string Key => $"{Kind}:{Id}";
}

/// <summary>A question to a service; record equality makes it the cache key.</summary>
internal sealed record StreamRequest(StreamView View, string Query = "", StreamScope Scope = StreamScope.All, StreamItem? Container = null, int Offset = 0);

/// <summary>A titled group of rows (one per kind in an "All" search).</summary>
internal sealed record StreamSection(StreamScope Scope, string Title, IReadOnlyList<StreamItem> Items, int Total);

/// <summary>An answer: sections plus where the next page starts, if any.</summary>
internal sealed record StreamPage(IReadOnlyList<StreamSection> Sections, int? NextOffset);

/// <summary>The current state of a request.</summary>
internal sealed record StreamLoad(StreamLoadState State, StreamPage? Page, StreamStatus Status, string Message = "", TimeSpan? RetryIn = null);

/// <summary>A short line shown under the list (an error when <see cref="Error"/>).</summary>
internal sealed record StreamNote(string Text, bool Error);

/// <summary>A link pill offered while a service cannot list anything.</summary>
internal sealed record StreamLink(string Title, string Url);

/// <summary>What the island's music browser needs from Spotify or YouTube Music; Apple Music has its own service.</summary>
internal interface IMusicService : IDisposable
{
    /// <summary>Which service this is.</summary>
    StreamKind Kind { get; }

    /// <summary>The brand name (not localized).</summary>
    string Name { get; }

    /// <summary>Whether the service is set up and signed in.</summary>
    StreamConnection Connection { get; }

    /// <summary>Whether lists can be fetched (Spotify: connected; YouTube: an API key is saved).</summary>
    bool CanBrowse { get; }

    /// <summary>Whether the user turned the service on for the island.</summary>
    bool ShowInIsland { get; }

    /// <summary>Whether searching starts while typing (otherwise on Enter).</summary>
    bool SearchAsYouType { get; }

    /// <summary>The list tabs shown while not searching.</summary>
    IReadOnlyList<StreamView> Tabs { get; }

    /// <summary>The search scope chips.</summary>
    IReadOnlyList<StreamScope> Scopes { get; }

    /// <summary>Whether songs can be added to a queue.</summary>
    bool CanQueue { get; }

    /// <summary>Whether playback can be started from here (Spotify: whether Premium is known; null = unknown).</summary>
    bool? CanPlayHere { get; }

    /// <summary>Link pills shown while not searching and not signed in.</summary>
    IReadOnlyList<StreamLink> Links { get; }

    /// <summary>The line to show under the list, if any.</summary>
    StreamNote? Note { get; }

    /// <summary>Key of the row an action is running for, if any.</summary>
    string? BusyKey { get; }

    /// <summary>Raised on the UI thread whenever anything above changed.</summary>
    event Action? Changed;

    /// <summary>On the UI thread: the cached or in-flight result, starting a fetch when it is missing or stale.</summary>
    StreamLoad Get(StreamRequest request);

    /// <summary>Forgets a failed result and asks again.</summary>
    void Retry(StreamRequest request);

    /// <summary>Plays <paramref name="item"/> within <paramref name="context"/> (and, for liked songs, the following <paramref name="siblings"/>).</summary>
    void Play(StreamItem item, StreamItem? context, IReadOnlyList<StreamItem>? siblings = null);

    /// <summary>Adds a song to the queue.</summary>
    void Queue(StreamItem item);

    /// <summary>Opens the item in the provider's own app or web page.</summary>
    void OpenExternally(StreamItem item);

    /// <summary>Opens the provider's own search for the text.</summary>
    void OpenSearchExternally(string query);

    /// <summary>An https link rebuilt from the item's validated id.</summary>
    string ShareUrl(StreamItem item);
}

/// <summary>User-visible strings of the music browser and the messages for <see cref="StreamStatus"/> values.</summary>
internal static class StreamStrings
{
    public static string LikedSongs => Loc.T("Liked Songs");
    public static string RecentlyPlayed => Loc.T("Recently played");
    public static string SearchSpotify => Loc.T("Search Spotify");
    public static string SearchYouTubeMusic => Loc.T("Search YouTube Music");
    public static string PressEnterToSearch => Loc.T("Press Enter to search");
    public static string ShowMore => Loc.T("Show more");
    public static string Searching => Loc.T("Searching…");
    public static string Loading => Loc.T("Loading…");
    public static string OpenInSpotify => Loc.T("Open in Spotify");
    public static string OpenInYouTubeMusic => Loc.T("Open in YouTube Music");
    public static string OpenInYouTube => Loc.T("Open in YouTube");
    public static string OpenYouTubeMusic => Loc.T("Open YouTube Music");
    public static string OpenYouTubeMusicApp => Loc.T("Open YouTube Music app");
    public static string LikedMusic => Loc.T("Liked music");
    public static string Library => Loc.T("Library");
    public static string CopyLink => Loc.T("Copy link");
    public static string SearchInSpotify => Loc.T("Search in Spotify");
    public static string ConnectInSettings => Loc.T("Connect in Settings");
    public static string NoLikedSongs => Loc.T("No liked songs");
    public static string NothingPlayedRecently => Loc.T("Nothing played recently");
    public static string NotAvailableInCountry => Loc.T("Not available in your country");
    public static string LocalFile => Loc.T("Local file");
    public static string ConnectSpotifyToSearch => Loc.T("Connect Spotify to search and play from here.");
    public static string PressEnterToSearchYouTube => Loc.T("Press Enter to search YouTube Music in your browser.");
    public static string AddApiKey => Loc.T("Add an API key in Settings to see results here.");
    public static string AddedToQueue => Loc.T("Added to the queue");
    public static string StartingSpotify => Loc.T("Starting Spotify…");
    public static string OwnedPlaylistsOnly => Loc.T("Spotify lists songs only for playlists you own or collaborate on.");
    public static string StartSomethingFirst => Loc.T("Start something in Spotify first, then add to the queue.");

    /// <summary>"Playing on {device}".</summary>
    public static string PlayingOn(string device) => Loc.F("Playing on {0}", device);

    /// <summary>The text for a failed or refused call; empty for statuses that need no message (Ok, NotConnected, Cancelled).</summary>
    /// <param name="status">What happened.</param>
    /// <param name="kind">Which service it happened to.</param>
    /// <param name="retryIn">For <see cref="StreamStatus.RateLimited"/>: how long until the next try.</param>
    public static string Message(StreamStatus status, StreamKind kind, TimeSpan? retryIn = null)
    {
        var name = kind == StreamKind.Spotify ? "Spotify" : "YouTube";
        switch (status)
        {
            case StreamStatus.NotConfigured:
                return kind == StreamKind.Spotify
                    ? Loc.T("Add a Spotify Client ID in Settings to connect.")
                    : Loc.T("Add an API key in Settings to see results here.");
            case StreamStatus.Offline:
                return Loc.T("You’re offline. Check your connection and try again.");
            case StreamStatus.RateLimited:
                var seconds = (int)Math.Ceiling((retryIn ?? TimeSpan.FromSeconds(5)).TotalSeconds);
                return kind == StreamKind.Spotify
                    ? Loc.F("Spotify asked Trayloom to slow down. Try again in {0} s.", Math.Max(1, seconds))
                    : Loc.F("YouTube asked Trayloom to slow down. Try again in {0} s.", Math.Max(1, seconds));
            case StreamStatus.QuotaExceeded:
                return kind == StreamKind.Spotify
                    ? Loc.T("Spotify’s usage limit for this app is reached. Try again later.")
                    : Loc.T("Today’s YouTube search limit is used up. It resets at midnight Pacific time.");
            case StreamStatus.PremiumRequired:
                return Loc.T("Spotify Premium is needed to play from the island. Opened it in Spotify instead.");
            case StreamStatus.NoDevice:
                return Loc.T("Open Spotify on this PC, your phone or a speaker, then try again.");
            case StreamStatus.AppMissing:
                return Loc.T("Spotify isn’t installed. Opened the web player.");
            case StreamStatus.NotAllowlisted:
                return Loc.T("This Spotify account isn’t added to the app. In the Spotify Dashboard open User Management and add it, or use your own Client ID.");
            case StreamStatus.Revoked:
                return Loc.T("Spotify access ended. Connect again in Settings.");
            case StreamStatus.InvalidKey:
                return Loc.T("The YouTube API key isn’t valid. Check it in Settings.");
            case StreamStatus.ApiDisabled:
                return Loc.T("Turn on YouTube Data API v3 for this key’s project.");
            case StreamStatus.NotListed:
                return OwnedPlaylistsOnly;
            case StreamStatus.Failed:
                return Loc.F("{0} didn’t respond. Try again.", name);
            case StreamStatus.PortBusy:
                return Loc.T("Another app is using port 43118. Close it and try again.");
            case StreamStatus.TimedOut:
                return Loc.T("Signing in took too long. Try again.");
            case StreamStatus.Denied:
                return Loc.T("Spotify access wasn’t allowed.");
            case StreamStatus.StateMismatch:
                return Loc.T("The answer from the browser didn’t match. Try again.");
            case StreamStatus.InvalidClient:
                return Loc.T("Spotify didn’t accept this Client ID. Check it and the redirect URI in your Spotify app.");
            default:
                return "";
        }
    }
}
