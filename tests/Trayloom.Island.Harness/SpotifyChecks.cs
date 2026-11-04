// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;
using Trayloom.Features.Island;

namespace Trayloom.Island.Harness;

/// <summary>
/// Checks of the pure parts of the Spotify service: URI parsing, JSON mapping on fixtures, the play request body, device choice, the
/// write allow-list, song matching, the sign-in URL and the timeline reader. No network, nothing is played.
/// </summary>
internal static class SpotifyChecks
{
    private static Action<bool, string> _check = (_, _) => { };

    public static void Run(Action<bool, string> check)
    {
        _check = check;
        Uris();
        Images();
        SearchMapping();
        ListMapping();
        LocalAndUnplayable();
        RecentMapping();
        AlbumAndQueue();
        PlayBodies();
        DevicePlan();
        Writes();
        SameSongs();
        AuthorizeUrl();
        Timeline();
        ClientIds();
        TimelineMerges();
        Extrapolation();
    }

    private static void Check(bool ok, string what) => _check(ok, "spotify: " + what);

    private static string Id(char c) => new(c, 22);

    private static readonly string T1 = Id('t');
    private static readonly string T2 = Id('u');
    private static readonly string T3 = Id('v');
    private static readonly string A1 = Id('a');
    private static readonly string A2 = Id('b');
    private static readonly string Al1 = Id('l');
    private static readonly string P1 = Id('p');
    private static readonly string P2 = Id('q');
    private static readonly string P3 = Id('r');

    private static JsonElement J(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static void Uris()
    {
        Check(SpotifyUri.TryParse("spotify:track:" + T1, out var type, out var id) && type == "track" && id == T1, "uri: spotify:track");
        Check(SpotifyUri.TryParse("spotify:album:" + Al1, out type, out _) && type == "album", "uri: spotify:album");
        Check(SpotifyUri.TryParse("spotify:artist:" + A1, out type, out _) && type == "artist", "uri: spotify:artist");
        Check(SpotifyUri.TryParse("spotify:playlist:" + P1, out type, out _) && type == "playlist", "uri: spotify:playlist");
        Check(SpotifyUri.TryParse("https://open.spotify.com/track/" + T1, out type, out id) && type == "track" && id == T1, "uri: open.spotify.com link");
        Check(SpotifyUri.TryParse("https://open.spotify.com/intl-de/album/" + Al1 + "?si=abc123", out type, out id) && type == "album" && id == Al1, "uri: intl prefix and query are accepted");
        Check(!SpotifyUri.TryParse("http://open.spotify.com/track/" + T1, out _, out _), "uri: plain http is refused");
        Check(!SpotifyUri.TryParse("https://open.spotify.com.evil.com/track/" + T1, out _, out _) && !SpotifyUri.TryParse("https://evil.com/open.spotify.com/track/" + T1, out _, out _), "uri: look-alike hosts are refused");
        Check(!SpotifyUri.TryParse("spotify:track:short", out _, out _) && !SpotifyUri.TryParse("spotify:track:" + new string('a', 23), out _, out _), "uri: an id must be 22 characters");
        Check(!SpotifyUri.TryParse("spotify:track:" + new string('-', 22), out _, out _), "uri: an id must be base-62");
        Check(!SpotifyUri.TryParse("spotify:show:" + T1, out _, out _) && !SpotifyUri.TryParse("spotify:episode:" + T1, out _, out _) && !SpotifyUri.TryParse("spotify:local:a:b:c:1", out _, out _), "uri: only the four playable kinds");
        Check(!SpotifyUri.TryParse("", out _, out _) && !SpotifyUri.TryParse(null, out _, out _) && !SpotifyUri.TryParse("javascript:alert(1)", out _, out _) && !SpotifyUri.TryParse("spotify:track:" + T1 + ":x", out _, out _), "uri: empty and malformed text is refused");
        Check(SpotifyUri.Build("track", T1) == "spotify:track:" + T1 && SpotifyUri.Build("track", "nope") == "", "uri: built only from a valid id");
        Check(SpotifyUri.LinkFor("playlist", P1) == "https://open.spotify.com/playlist/" + P1 && SpotifyUri.LinkFor("track", "../x") == "", "uri: share links are rebuilt from the id");
    }

    private static void Images()
    {
        string Pick(string images) => SpotifyMap.Album(J($$"""{"id":"{{Al1}}","name":"x","images":{{images}}}"""))?.ImageUrl ?? "none";
        Check(Pick("""[{"url":"https://i.scdn.co/image/big","width":640},{"url":"https://i.scdn.co/image/mid","width":300},{"url":"https://i.scdn.co/image/small","width":64}]""") == "https://i.scdn.co/image/small", "image: the smallest one at least 64 px wide");
        Check(Pick("""[{"url":"https://i.scdn.co/image/big","width":640},{"url":"https://i.scdn.co/image/mid","width":300}]""") == "https://i.scdn.co/image/mid", "image: 300 beats 640");
        Check(Pick("""[{"url":"https://i.scdn.co/image/tiny","width":40},{"url":"https://i.scdn.co/image/tiny2","width":20}]""") == "https://i.scdn.co/image/tiny", "image: only small ones: the widest");
        Check(Pick("""[{"url":"https://evil.example/x.png","width":64},{"url":"https://i.scdn.co/image/ok","width":300}]""") == "https://i.scdn.co/image/ok", "image: a disallowed host is skipped");
        Check(Pick("""[{"url":"https://evil.example/x.png","width":64}]""") == "none" && Pick("[]") == "none", "image: nothing usable gives none");
        Check(Pick("""[{"url":"https://i.scdn.co/image/nowidth"}]""") == "https://i.scdn.co/image/nowidth", "image: an image without a width is still used when alone");
    }

    private static string TrackJson(string id, string name, string artists = "Ann", string? albumId = null, string extra = "")
    {
        var names = string.Join(",", artists.Split(',').Select((a, i) => $$"""{"id":"{{(i == 0 ? A1 : A2)}}","name":"{{a.Trim()}}"}"""));
        var album = albumId is null ? "" : $$""","album":{"id":"{{albumId}}","name":"Album","images":[{"url":"https://i.scdn.co/image/big","width":640},{"url":"https://i.scdn.co/image/small","width":64}]}""";
        return $$"""{"type":"track","id":"{{id}}","name":"{{name}}","uri":"spotify:track:{{id}}","duration_ms":200000,"artists":[{{names}}]{{album}}{{extra}}}""";
    }

    private static void SearchMapping()
    {
        var json = $$$"""
        {
          "tracks": {"total": 120, "items": [
            null,
            {{{TrackJson(T1, "Song One", "Ann, Bob", Al1)}}},
            {"type":"episode","id":"{{{T2}}}","name":"Podcast"},
            {{{TrackJson(T3, "Gone", "Ann", Al1, ",\"is_playable\":false")}}}
          ]},
          "albums": {"total": 2, "items": [{"id":"{{{Al1}}}","name":"Album","total_tracks":12,"artists":[{"id":"{{{A1}}}","name":"Ann"}],"images":[]}]},
          "artists": {"total": 1, "items": [{"id":"{{{A1}}}","name":"Ann","images":[{"url":"https://evil.example/x.png","width":64}]}]},
          "playlists": {"total": 3, "items": [
            null,
            {"id":"{{{P1}}}","name":"Mine","owner":{"id":"me","display_name":"Me"},"items":{"total":5},"images":[]},
            {"id":"{{{P2}}}","name":"Theirs","owner":{"id":"other","display_name":"Other"},"tracks":{"total":9}},
            {"id":"{{{P3}}}","name":"Collab","collaborative":true,"owner":{"id":"other"}}
          ]}
        }
        """;
        var sections = SpotifyMap.Search(J(json), "me");
        Check(sections.Count == 4 && sections[0].Scope == StreamScope.Songs && sections[1].Scope == StreamScope.Albums && sections[2].Scope == StreamScope.Artists && sections[3].Scope == StreamScope.Playlists, "search: four sections in order");

        var songs = sections[0];
        Check(songs.Total == 120 && songs.Items.Count == 2, "search: nulls and episodes are skipped, the total is kept");
        var one = songs.Items[0];
        Check(one.Kind == StreamItemKind.Track && one.Id == T1 && one.Title == "Song One" && one.Subtitle == "Ann, Bob", "search: title and artists joined with a comma");
        Check(one.Uri == "spotify:track:" + T1 && one.Duration == 200 && one.AlbumId == Al1 && one.ContextUri == "spotify:album:" + Al1 && one.ArtistId == A1, "search: uri, duration, album and first artist");
        Check(one.ImageUrl == "https://i.scdn.co/image/small" && one.Playable, "search: the small album image, playable");
        Check(!songs.Items[1].Playable, "search: is_playable=false is not playable");

        var album = sections[1].Items[0];
        Check(album.Kind == StreamItemKind.Album && album.Subtitle == "Ann" && album.Count == 12 && album.Uri == "spotify:album:" + Al1 && album.ImageUrl is null, "search: an album");
        var artist = sections[2].Items[0];
        Check(artist.Kind == StreamItemKind.Artist && artist.Uri == "spotify:artist:" + A1 && artist.ImageUrl is null, "search: an artist whose image host is not allowed has no image");

        var lists = sections[3].Items;
        Check(lists.Count == 3 && sections[3].Total == 3, "search: a null playlist entry is skipped");
        Check(lists[0].Owned && lists[0].Count == 5 && lists[0].Subtitle == "Me", "search: an owned playlist (items.total)");
        Check(!lists[1].Owned && lists[1].Count == 9 && lists[1].Subtitle == "Other", "search: someone else's playlist (tracks.total)");
        Check(lists[2].Owned, "search: a collaborative playlist counts as owned");
        Check(!SpotifyMap.Search(J(json), null)[3].Items[0].Owned, "search: without a user id only collaborative playlists are owned");

        var onlyTracks = SpotifyMap.Search(J("""{"tracks":{"total":0,"items":[]}}"""), null);
        Check(onlyTracks.Count == 1 && onlyTracks[0].Items.Count == 0, "search: a single-type answer has a single section");
        Check(SpotifyMap.Search(J("{}"), null).Count == 0 && SpotifyMap.Search(J("[]"), null).Count == 0, "search: nothing recognisable gives no sections");
    }

    private static void ListMapping()
    {
        var json = $$$"""
        {"items":[
          {"added_at":"2026-01-01T00:00:00Z","track":{{{TrackJson(T1, "Old Shape", "Ann", Al1)}}}},
          {"added_at":"2026-01-02T00:00:00Z","item":{{{TrackJson(T3, "New Shape", "Ann", Al1)}}}},
          {"track":null},
          {"item":{"type":"episode","id":"{{{T2}}}","name":"Podcast"}}
        ],"total":4}
        """;
        var liked = SpotifyMap.Liked(J(json));
        Check(liked.Items.Count == 2 && liked.Items[0].Title == "Old Shape" && liked.Items[1].Title == "New Shape", "liked: both the track and the item shape are read");
        Check(liked.Scope == StreamScope.Songs && liked.Total == 4, "liked: the total is the API's");

        var playlistSongs = SpotifyMap.PlaylistItems(J(json));
        Check(playlistSongs.Items.Count == 2, "playlist items: the same two shapes");
        Check(SpotifyMap.Liked(J("""{"items":[]}""")).Items.Count == 0 && SpotifyMap.Liked(J("{}")).Items.Count == 0, "liked: empty answers give no rows");

        var mine = SpotifyMap.Playlists(J($$$"""{"items":[{"id":"{{{P1}}}","name":"Mine","owner":{"id":"me"}},{"id":"short","name":"Bad id"}]}"""), "me");
        Check(mine.Items.Count == 1 && mine.Items[0].Owned, "playlists: owned by the user id; an invalid id is skipped");
    }

    private static void LocalAndUnplayable()
    {
        var local = SpotifyMap.Track(J("""{"type":"track","id":null,"uri":"spotify:local:Me:Album:Home+Song:60","name":"Home Song","artists":[{"name":"Me"}],"duration_ms":60000}"""), null);
        Check(local is { Playable: false } && local.Subtitle == StreamStrings.LocalFile && local.Id.StartsWith("local:", StringComparison.Ordinal) && local.Uri == "", "local: a local file is listed, unplayable, with the Local file subtitle");
        var again = SpotifyMap.Track(J("""{"type":"track","id":null,"uri":"spotify:local:Me:Album:Home+Song:60","name":"Home Song"}"""), null);
        Check(again?.Id == local?.Id, "local: its row identity is stable");
        Check(SpotifyMap.Track(J("""{"type":"track","id":null,"uri":"spotify:track:x","name":"No id"}"""), null) is null, "track: a null id (unavailable) is skipped");
        Check(SpotifyMap.Track(J("null"), null) is null && SpotifyMap.Track(J("\"x\""), null) is null, "track: non-objects are skipped");
        Check(SpotifyMap.Track(J(TrackJson(T1, "X", "Ann", Al1, ",\"is_playable\":true")), null)?.Playable == true, "track: is_playable=true");
        Check(SpotifyMap.Track(J(TrackJson(T1, "X", "Ann", Al1)), null)?.Playable == true, "track: no is_playable means playable");
        Check(SpotifyMap.Track(J("""{"id":"%ID%","name":"No type"}""".Replace("%ID%", T1)), null) is { Title: "No type" }, "track: a missing type is accepted");
    }

    private static void RecentMapping()
    {
        var json = $$$"""
        {"items":[
          {"played_at":"a","track":{{{TrackJson(T1, "One", "Ann", Al1)}}},"context":{"type":"playlist","uri":"spotify:playlist:{{{P1}}}"}},
          {"played_at":"b","track":{{{TrackJson(T1, "One", "Ann", Al1)}}},"context":{"type":"playlist","uri":"spotify:playlist:{{{P1}}}"}},
          {"played_at":"c","track":{{{TrackJson(T3, "Three", "Ann", Al1)}}},"context":{"type":"artist","uri":"spotify:artist:{{{A1}}}"}},
          {"played_at":"d","track":{{{TrackJson(T1, "One", "Ann", Al1)}}},"context":null},
          {"played_at":"e","track":null}
        ]}
        """;
        var recent = SpotifyMap.Recent(J(json));
        Check(recent.Items.Count == 3, "recent: consecutive repeats are removed, later repeats stay");
        Check(recent.Items[0].ContextUri == "spotify:playlist:" + P1, "recent: the playlist it was played from");
        Check(recent.Items[1].ContextUri == "spotify:album:" + Al1, "recent: an artist context falls back to the album");
        Check(recent.Items[2].ContextUri == "spotify:album:" + Al1, "recent: no context falls back to the album");
        Check(recent.Title == StreamStrings.RecentlyPlayed && recent.Total == 3, "recent: titled");
    }

    private static void AlbumAndQueue()
    {
        var album = new StreamItem(StreamItemKind.Album, Al1, "Album", "Ann") { Uri = "spotify:album:" + Al1, ImageUrl = "https://i.scdn.co/image/small" };
        var simple = $$"""{"items":[{"type":"track","id":"{{T1}}","name":"First","uri":"spotify:track:{{T1}}","duration_ms":1000,"artists":[{"id":"{{A1}}","name":"Ann"}]},{"type":"track","id":"{{T3}}","name":"Second","duration_ms":2000,"artists":[]}],"total":2}""";
        var tracks = SpotifyMap.AlbumTracks(J(simple), album);
        Check(tracks.Items.Count == 2 && tracks.Items.All(t => t.ImageUrl == album.ImageUrl && t.ContextUri == album.Uri && t.AlbumId == Al1), "album tracks: they inherit the album's image and context");

        var queue = SpotifyMap.Queue(J($$"""{"currently_playing":{{TrackJson(T2, "Now", "Ann", Al1)}},"queue":[{{TrackJson(T1, "Next", "Ann", Al1)}},{"type":"episode","id":"{{T2}}","name":"Pod"},{{TrackJson(T3, "After", "Bob", Al1)}}]}"""));
        Check(queue.Items.Count == 2 && queue.Items[0].Title == "Next" && queue.Items[1].Title == "After", "queue: the queue only, episodes skipped");
        Check(SpotifyMap.Queue(J("""{"currently_playing":null,"queue":[]}""")).Items.Count == 0, "queue: empty");

        var devices = SpotifyMap.Devices(J("""{"devices":[{"id":"d1","name":"MY-PC","is_active":false,"is_restricted":false},{"id":"d2","name":"Phone","is_active":true,"is_restricted":true},{"id":null,"name":"Ghost"}]}"""));
        Check(devices.Count == 2 && devices[0].Name == "MY-PC" && !devices[0].IsActive && devices[1].IsActive && devices[1].IsRestricted, "devices: id, name and flags; one without an id is skipped");
    }

    private static StreamItem Song(string id, bool playable = true, string? contextUri = null) =>
        new(StreamItemKind.Track, id, "Song " + id[..1], "Ann") { Uri = "spotify:track:" + id, ContextUri = contextUri, Playable = playable };

    private static void PlayBodies()
    {
        var album = new StreamItem(StreamItemKind.Album, Al1, "Album", "Ann") { Uri = "spotify:album:" + Al1 };
        var playlist = new StreamItem(StreamItemKind.Playlist, P1, "Mine", "Me") { Uri = "spotify:playlist:" + P1, Owned = true };
        var artist = new StreamItem(StreamItemKind.Artist, A1, "Ann", "Artist") { Uri = "spotify:artist:" + A1 };

        Check(SpotifyPlayBody.For(album, null, null) == "{\"context_uri\":\"spotify:album:" + Al1 + "\"}", "body: an album plays as a context");
        Check(SpotifyPlayBody.For(playlist, null, null) == "{\"context_uri\":\"spotify:playlist:" + P1 + "\"}", "body: a playlist plays as a context");
        Check(SpotifyPlayBody.For(artist, null, null) == "{\"context_uri\":\"spotify:artist:" + A1 + "\"}", "body: an artist plays as a context, no offset");

        var one = Song(T1);
        Check(SpotifyPlayBody.For(one, playlist, null) == "{\"context_uri\":\"spotify:playlist:" + P1 + "\",\"offset\":{\"uri\":\"spotify:track:" + T1 + "\"}}", "body: a song in a playlist starts at that song");
        Check(SpotifyPlayBody.For(one, album, null) == "{\"context_uri\":\"spotify:album:" + Al1 + "\",\"offset\":{\"uri\":\"spotify:track:" + T1 + "\"}}", "body: a song in an album starts at that song");
        Check(SpotifyPlayBody.For(Song(T1, contextUri: "spotify:album:" + Al1), null, null) == "{\"context_uri\":\"spotify:album:" + Al1 + "\",\"offset\":{\"uri\":\"spotify:track:" + T1 + "\"}}", "body: a search result plays within its album");
        Check(SpotifyPlayBody.For(Song(T1, contextUri: "spotify:playlist:" + P1), null, null) == "{\"context_uri\":\"spotify:playlist:" + P1 + "\",\"offset\":{\"uri\":\"spotify:track:" + T1 + "\"}}", "body: a recent song plays within its playlist");
        Check(SpotifyPlayBody.For(one, null, null) == "{\"uris\":[\"spotify:track:" + T1 + "\"]}", "body: a song with no context plays alone");

        var liked = new[] { Song(T1, contextUri: "spotify:album:" + Al1), Song(T2), Song(Id('w'), playable: false), Song(T3) };
        Check(SpotifyPlayBody.For(liked[0], null, liked) == "{\"uris\":[\"spotify:track:" + T1 + "\",\"spotify:track:" + T2 + "\",\"spotify:track:" + T3 + "\"]}", "body: liked songs continue with the playable ones after it");
        Check(SpotifyPlayBody.For(liked[1], null, liked) == "{\"uris\":[\"spotify:track:" + T2 + "\",\"spotify:track:" + T3 + "\"]}", "body: liked songs start at the clicked one");
        Check(SpotifyPlayBody.For(liked[3], null, liked) == "{\"uris\":[\"spotify:track:" + T3 + "\"]}", "body: the last liked song plays alone");
        Check(SpotifyPlayBody.For(Song(Id('z')), null, liked) == "{\"uris\":[\"spotify:track:" + Id('z') + "\"]}", "body: a song missing from the siblings plays alone");

        var many = Enumerable.Range(0, 80).Select(i => Song("m" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture) + new string('x', 17))).ToList();
        var body = SpotifyPlayBody.For(many[0], null, many)!;
        using var parsed = JsonDocument.Parse(body);
        Check(parsed.RootElement.GetProperty("uris").GetArrayLength() == 50 && parsed.RootElement.GetProperty("uris")[0].GetString() == many[0].Uri, "body: at most 50 uris, the clicked one first");

        Check(SpotifyPlayBody.For(Song(T1, playable: false), null, null) is null, "body: an unplayable song has none");
        Check(SpotifyPlayBody.For(new StreamItem(StreamItemKind.Track, "local:1", "Local", "") { Playable = false }, null, null) is null, "body: a local file has none");
        Check(SpotifyPlayBody.For(new StreamItem(StreamItemKind.Album, Al1, "A", "") { Uri = "spotify:evil:x" }, null, null) is null, "body: a container with a bad uri has none");
        Check(SpotifyPlayBody.For(new StreamItem(StreamItemKind.Video, "x", "V", ""), null, null) is null, "body: a video is not a Spotify item");
    }

    private static void DevicePlan()
    {
        SpotifyDevice D(string id, string name, bool active = false, bool restricted = false) => new(id, name, active, restricted);

        var pc = SpotifyDevicePlan.Choose([D("1", "Phone", active: true), D("2", "my-pc")], "MY-PC", appInstalled: true);
        Check(pc is { Action: DeviceAction.UseDevice, DeviceId: "2" }, "devices: this PC wins over the active phone, name compared without case");
        var only = SpotifyDevicePlan.Choose([D("1", "Speaker")], "MY-PC", appInstalled: false);
        Check(only is { Action: DeviceAction.UseDevice, DeviceId: "1", DeviceName: "Speaker" }, "devices: the only device is used");
        var active = SpotifyDevicePlan.Choose([D("1", "Phone"), D("2", "Speaker", active: true), D("3", "TV")], "MY-PC", appInstalled: true);
        Check(active is { Action: DeviceAction.UseDevice, DeviceId: "2" }, "devices: otherwise the active one");
        var first = SpotifyDevicePlan.Choose([D("1", "Phone"), D("2", "Speaker")], "MY-PC", appInstalled: true);
        Check(first is { Action: DeviceAction.UseDevice, DeviceId: "1" }, "devices: otherwise the first");
        var restricted = SpotifyDevicePlan.Choose([D("1", "MY-PC", restricted: true), D("2", "Phone")], "MY-PC", appInstalled: true);
        Check(restricted is { Action: DeviceAction.UseDevice, DeviceId: "2" }, "devices: a restricted device is skipped even when it is this PC");
        Check(SpotifyDevicePlan.Choose([], "MY-PC", appInstalled: true).Action == DeviceAction.LaunchApp, "devices: none and the app installed: launch it");
        Check(SpotifyDevicePlan.Choose([D("1", "PC", restricted: true)], "MY-PC", appInstalled: true).Action == DeviceAction.LaunchApp, "devices: only restricted ones count as none");
        Check(SpotifyDevicePlan.Choose([], "MY-PC", appInstalled: false).Action == DeviceAction.NoDevice, "devices: none and no app: nothing to do");
    }

    private static void Writes()
    {
        foreach (var ok in new[] { "me/player/play", "me/player", "me/player/queue?uri=spotify%3Atrack%3Aabc", "me/player/seek?position_ms=1000", "me/player/shuffle?state=true", "/v1/me/player/play?device_id=x", "v1/me/player" })
        {
            Check(SpotifyWrites.Allowed(ok), "writes: " + ok.Split('?')[0] + " is allowed");
        }

        foreach (var bad in new[] { "me/tracks", "me/tracks?ids=abc", "playlists/x/items", "playlists/x/tracks", "me/playlists", "me/albums", "me/following", "me/player/play/../../me/tracks", "me/player/pause", "me/player/next", "me/player/volume", "users/x/playlists", "ME/PLAYER", "", "  " })
        {
            Check(!SpotifyWrites.Allowed(bad), "writes: '" + bad + "' is refused");
        }

        Check(!SpotifyWrites.Allowed(null), "writes: null is refused");
    }

    private static void SameSongs()
    {
        Check(SpotifyMap.SameSong("Café del Mar", "Énergie", "Cafe Del Mar", "Energie, Other"), "same song: case and accents are ignored");
        Check(SpotifyMap.SameSong("Don't Stop", "Queen", "Don’t Stop", "Queen"), "same song: punctuation is ignored");
        Check(SpotifyMap.SameSong("Song", "Ann", "Song", "Ann, Bob"), "same song: the session names the first artist only");
        Check(SpotifyMap.SameSong("Song", "Bob", "Song", "Ann, Bob"), "same song: or another of them");
        Check(SpotifyMap.SameSong("Song", "Ann & Bob", "Song", "Ann, Bob"), "same song: joined differently");
        Check(!SpotifyMap.SameSong("Song", "Carl", "Song", "Ann, Bob"), "same song: another artist is another song");
        Check(!SpotifyMap.SameSong("Other", "Ann", "Song", "Ann"), "same song: another title is another song");
        Check(SpotifyMap.SameSong("Song", "", "Song", "Ann"), "same song: an unknown artist matches on the title");
        Check(!SpotifyMap.SameSong("", "Ann", "", "Ann") && !SpotifyMap.SameSong(null, null, "Song", "Ann"), "same song: no title never matches");
    }

    private static void AuthorizeUrl()
    {
        var challenge = Pkce.Challenge(Pkce.CreateVerifier());
        var state = Pkce.CreateState();
        var url = SpotifyAuth.AuthorizeUrl("client-xyz", challenge, state);
        Check(url.StartsWith("https://accounts.spotify.com/authorize?", StringComparison.Ordinal), "authorize: Spotify's authorize page over https");
        Check(url.Contains("redirect_uri=http%3A%2F%2F127.0.0.1%3A43118%2Fcallback", StringComparison.Ordinal), "authorize: the redirect URI is escaped");
        Check(SpotifyService.RedirectUri == $"http://127.0.0.1:{SpotifyService.Port}/callback" && SpotifyService.Port == 43118, "authorize: the redirect URI is the loopback IP literal on the fixed port");
        var query = System.Web.HttpUtility.ParseQueryString(url[(url.IndexOf('?') + 1)..]);
        Check(query["response_type"] == "code" && query["client_id"] == "client-xyz", "authorize: code flow with the client id");
        Check(query["code_challenge_method"] == "S256" && query["code_challenge"] == challenge && query["state"] == state, "authorize: S256 challenge and state");
        Check(query["redirect_uri"] == "http://127.0.0.1:43118/callback", "authorize: the redirect URI decodes to the registered one");
        string[] expected =
        [
            "user-read-playback-state", "user-modify-playback-state", "user-read-currently-playing", "user-library-read",
            "playlist-read-private", "playlist-read-collaborative", "user-read-recently-played",
        ];
        Check(query["scope"] == string.Join(' ', expected), "authorize: exactly the seven scopes, in order");
        Check(!url.Contains("modify-public", StringComparison.Ordinal) && !url.Contains("library-modify", StringComparison.Ordinal) && !url.Contains("playlist-modify", StringComparison.Ordinal), "authorize: no scope that edits a library or playlist");
        Check(!url.Contains("secret", StringComparison.OrdinalIgnoreCase), "authorize: no client secret");
        Check(SpotifyAuth.BuiltInClientId == "", "authorize: the public source ships no built-in client id");
    }

    private static void Timeline()
    {
        var stamp = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
        var playing = SpotifyService.ReadTimeline("""{"is_playing":true,"progress_ms":61500,"item":{"type":"track","name":"Song One","duration_ms":200000,"artists":[{"name":"Ann"},{"name":"Bob"}]}}""", stamp);
        Check(playing is { Title: "Song One", Artist: "Ann, Bob", Position: 61.5, Duration: 200, Playing: true } && playing.StampUtc == stamp, "timeline: a playing song");
        var paused = SpotifyService.ReadTimeline("""{"is_playing":false,"progress_ms":1000,"item":{"type":"track","name":"X","duration_ms":5000,"artists":[]}}""", stamp);
        Check(paused is { Playing: false, Artist: "" }, "timeline: paused");
        Check(SpotifyService.ReadTimeline("""{"is_playing":true,"item":{"type":"episode","name":"Pod"}}""", stamp) is null, "timeline: an episode is not a song");
        Check(SpotifyService.ReadTimeline("", stamp) is null && SpotifyService.ReadTimeline(null, stamp) is null && SpotifyService.ReadTimeline("{}", stamp) is null && SpotifyService.ReadTimeline("{nope", stamp) is null && SpotifyService.ReadTimeline("""{"item":null}""", stamp) is null, "timeline: nothing playing and bad answers give none");
    }

    private static void ClientIds()
    {
        const string Good = "0123456789abcdef0123456789ABCDEF";
        Check(SpotifyAuth.IsClientIdFormat(Good), "client id: 32 hexadecimal characters, either case");
        Check(!SpotifyAuth.IsClientIdFormat(Good[..31]) && !SpotifyAuth.IsClientIdFormat(Good + "0"), "client id: 31 or 33 characters are refused");
        Check(!SpotifyAuth.IsClientIdFormat("0123456789abcdef0123456789abcdeg"), "client id: a non-hexadecimal letter is refused");
        Check(!SpotifyAuth.IsClientIdFormat("0123456789abcdef 123456789abcdef") && !SpotifyAuth.IsClientIdFormat(" " + Good[..31]), "client id: spaces are refused");
        Check(!SpotifyAuth.IsClientIdFormat("0123456789abcdef0123456789abcdé0"), "client id: a non-ASCII character is refused");
        Check(!SpotifyAuth.IsClientIdFormat("") && !SpotifyAuth.IsClientIdFormat(null), "client id: empty or missing is refused");
    }

    private static void TimelineMerges()
    {
        var stamp = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
        var web = new ExternalTimeline("Song One", "Ann, Bob", 61.5, 200, true, stamp);

        // the session has no length and the Web API plays the same song: the Web API fills in
        var fill = TimelineMerge.Decide(0, "Song One", "Ann", web, canSeek: true);
        Check(fill is { Duration: 200, BasePosition: 61.5, CanSeek: true } f && f.BaseStamp == stamp, "merge: no session length and the same song uses the Web API reading");
        Check(TimelineMerge.Decide(0, "Song One", "Ann", web, canSeek: false) is { CanSeek: false }, "merge: seeking follows what is on offer");
        Check(TimelineMerge.Decide(-1, "song one", "Bob", web, canSeek: true) is not null, "merge: a negative length counts as none, case does not matter");

        // a real session timeline is never overridden
        Check(TimelineMerge.Decide(215, "Song One", "Ann", web, canSeek: true) is null, "merge: a session length stands");
        Check(TimelineMerge.Decide(0.5, "Song One", "Ann", web, canSeek: true) is null, "merge: even a short session length stands");

        // another song, or nothing to use
        Check(TimelineMerge.Decide(0, "Other Song", "Ann", web, canSeek: true) is null, "merge: another title is not filled");
        Check(TimelineMerge.Decide(0, "Song One", "Carl", web, canSeek: true) is null, "merge: another artist is not filled");
        Check(TimelineMerge.Decide(0, "Song One", "Ann", null, canSeek: true) is null, "merge: no Web API reading, nothing to fill");
        Check(TimelineMerge.Decide(0, "Song One", "Ann", web with { Duration = 0 }, canSeek: true) is null, "merge: a Web API reading without a length is not used");
        Check(TimelineMerge.Decide(0, "", "Ann", web with { Title = "" }, canSeek: true) is null, "merge: no title never matches");

        // the position stays inside the song
        Check(TimelineMerge.Decide(0, "Song One", "Ann", web with { Position = 999 }, canSeek: true) is { BasePosition: 200 }, "merge: a position past the end is held at the end");
        Check(TimelineMerge.Decide(0, "Song One", "Ann", web with { Position = -5 }, canSeek: true) is { BasePosition: 0 }, "merge: a negative position is held at the start");
    }

    private static void Extrapolation()
    {
        var stamp = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
        var playing = new ExternalTimeline("Song", "Ann", 60, 200, true, stamp);
        Check(Math.Abs(TimelineMerge.PositionAt(playing, stamp) - 60) < 1e-9, "extrapolation: at the stamp it is the reading");
        Check(Math.Abs(TimelineMerge.PositionAt(playing, stamp.AddSeconds(2.5)) - 62.5) < 1e-9, "extrapolation: while playing it moves with the clock");
        Check(Math.Abs(TimelineMerge.PositionAt(playing, stamp.AddSeconds(500)) - 200) < 1e-9, "extrapolation: it stops at the end of the song");
        Check(Math.Abs(TimelineMerge.PositionAt(playing, stamp.AddSeconds(-3)) - 60) < 1e-9, "extrapolation: a clock before the stamp does not move it back");
        Check(Math.Abs(TimelineMerge.PositionAt(playing with { Playing = false }, stamp.AddSeconds(30)) - 60) < 1e-9, "extrapolation: paused it stands still");
        Check(Math.Abs(TimelineMerge.PositionAt(playing with { Duration = 0 }, stamp.AddSeconds(30)) - 90) < 1e-9, "extrapolation: with no length it is not capped");
    }
}
