// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;
using Trayloom.Features.Island;

namespace Trayloom.Island.Harness;

/// <summary>Checks of the pure YouTube Music parts: durations, titles, ids, links, error rules, the quota reset and the JSON mappers. No network.</summary>
internal static class YouTubeChecks
{
    private static Action<bool, string> _check = (_, _) => { };

    public static void Run(Action<bool, string> check)
    {
        _check = check;
        Durations();
        Titles();
        Ids();
        Links();
        Errors();
        Quota();
        SearchJson();
        PlaylistJson();
        VideosJson();
        Images();
    }

    private static void Check(bool ok, string what) => _check(ok, "youtube: " + what);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static void Durations()
    {
        Check(Iso8601.Seconds("PT3M45S") == 225, "duration: PT3M45S");
        Check(Iso8601.Seconds("PT1H2M3S") == 3723, "duration: PT1H2M3S");
        Check(Iso8601.Seconds("PT45S") == 45, "duration: seconds only");
        Check(Iso8601.Seconds("PT2M") == 120, "duration: minutes only");
        Check(Iso8601.Seconds("P1DT1H") == 90000, "duration: days");
        Check(Iso8601.Seconds("P0D") == 0, "duration: P0D (live) is 0");
        Check(Iso8601.Seconds("") == 0 && Iso8601.Seconds(null) == 0 && Iso8601.Seconds("3:45") == 0 && Iso8601.Seconds("PTxS") == 0 && Iso8601.Seconds("PT-5S") == 0, "duration: invalid is 0");
    }

    private static void Titles()
    {
        Check(YouTubeMap.CleanTitle("Rock &amp; Roll &#39;n&#39; &quot;Blues&quot;") == "Rock & Roll 'n' \"Blues\"", "title: HTML entities decoded");
        Check(YouTubeMap.CleanTitle("Daft Punk - Topic") == "Daft Punk", "title: ' - Topic' stripped");
        Check(YouTubeMap.CleanTitle("  Daft Punk - topic ") == "Daft Punk", "title: stripped case-insensitively and trimmed");
        Check(YouTubeMap.CleanTitle("Topic - Live") == "Topic - Live", "title: only a trailing suffix goes");
        Check(YouTubeMap.CleanTitle("- Topic") == "- Topic", "title: a bare suffix is kept, never emptied out");
        Check(YouTubeMap.CleanTitle(null) == "", "title: null is empty");
    }

    private static void Ids()
    {
        Check(YouTubeIds.IsVideo("dQw4w9WgXcQ") && YouTubeIds.IsVideo("a_b-c_d-e_f"), "ids: valid video ids");
        Check(!YouTubeIds.IsVideo("short") && !YouTubeIds.IsVideo("dQw4w9WgXcQ1") && !YouTubeIds.IsVideo("dQw4w9WgXc&") && !YouTubeIds.IsVideo("dQw4w9 gXcQ") && !YouTubeIds.IsVideo(null) && !YouTubeIds.IsVideo("dQw4w9WgXcQ\n"), "ids: invalid video ids");
        Check(YouTubeIds.IsPlaylist("LM") && YouTubeIds.IsPlaylist("PLrAXtmErZgOeiKm4sgNOknGvNjby9efdf"), "ids: valid playlist ids");
        Check(!YouTubeIds.IsPlaylist("L") && !YouTubeIds.IsPlaylist("PL&x=1") && !YouTubeIds.IsPlaylist("") && !YouTubeIds.IsPlaylist(new string('a', 65)), "ids: invalid playlist ids");
        Check(YouTubeIds.IsKey("AIzaSyA-1234567890abcdefghijklmnopqrstu") && !YouTubeIds.IsKey("short") && !YouTubeIds.IsKey("AIza SyA-1234567890abcdefghijklmnopqrstu") && !YouTubeIds.IsKey("AIzaSyA-1234567890abcdefghij\r\nX-Evil: 1"), "ids: key shape (no spaces or line breaks)");
    }

    private static void Links()
    {
        Check(YouTubeLinks.Watch("dQw4w9WgXcQ", music: true) == "https://music.youtube.com/watch?v=dQw4w9WgXcQ", "links: watch in YouTube Music");
        Check(YouTubeLinks.Watch("dQw4w9WgXcQ", music: false) == "https://www.youtube.com/watch?v=dQw4w9WgXcQ", "links: watch in YouTube");
        Check(YouTubeLinks.Watch("dQw4w9WgXcQ", true, "PL123") == "https://music.youtube.com/watch?v=dQw4w9WgXcQ&list=PL123", "links: watch inside a playlist");
        Check(YouTubeLinks.Watch("dQw4w9WgXcQ", true, "PL1&evil=1") == "https://music.youtube.com/watch?v=dQw4w9WgXcQ", "links: a bad playlist id is dropped");
        Check(YouTubeLinks.Watch("x&y=1", true) == "", "links: a bad video id gives no link");
        Check(YouTubeLinks.Playlist("LM", true) == "https://music.youtube.com/playlist?list=LM", "links: playlist");
        Check(YouTubeLinks.Playlist("a b", false) == "", "links: a bad playlist id gives no link");
        Check(YouTubeLinks.Search("a b&c=d#e", true) == "https://music.youtube.com/search?q=a%20b%26c%3Dd%23e", "links: search text escaped");
        Check(YouTubeLinks.Search("é ü", false) == "https://www.youtube.com/results?search_query=%C3%A9%20%C3%BC", "links: YouTube search, non-ASCII escaped");
    }

    private static void Errors()
    {
        const string invalid = "{\"error\":{\"code\":400,\"errors\":[{\"reason\":\"keyInvalid\"}],\"status\":\"INVALID_ARGUMENT\",\"details\":[{\"reason\":\"API_KEY_INVALID\"}]}}";
        const string quota = "{\"error\":{\"code\":403,\"errors\":[{\"reason\":\"quotaExceeded\"}]}}";
        const string disabled = "{\"error\":{\"code\":403,\"errors\":[{\"reason\":\"accessNotConfigured\"}]}}";
        Check(ApiError.Classify(StreamKind.YouTube, 400, invalid) == StreamStatus.InvalidKey, "errors: 400 keyInvalid");
        Check(ApiError.Classify(StreamKind.YouTube, 403, quota) == StreamStatus.QuotaExceeded, "errors: 403 quotaExceeded");
        Check(ApiError.Classify(StreamKind.YouTube, 403, disabled) == StreamStatus.ApiDisabled, "errors: 403 accessNotConfigured");
        Check(ApiError.Classify(StreamKind.YouTube, 500, "") == StreamStatus.Failed, "errors: 5xx is Failed");
        Check(StreamStrings.Message(StreamStatus.QuotaExceeded, StreamKind.YouTube).Contains("midnight Pacific"), "errors: quota message names the reset");
        Check(StreamStrings.Message(StreamStatus.InvalidKey, StreamKind.YouTube).Contains("API key"), "errors: invalid key message");
    }

    private static void Quota()
    {
        var pst = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        var winter = YouTubeQuota.ResetAfter(new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc), pst);
        Check(winter == new DateTime(2026, 11, 5, 8, 0, 0, DateTimeKind.Utc), "quota: winter reset is 08:00 UTC the next day");
        var summer = YouTubeQuota.ResetAfter(new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc), pst);
        Check(summer == new DateTime(2026, 7, 2, 7, 0, 0, DateTimeKind.Utc), "quota: summer (daylight saving) reset is 07:00 UTC");
        var late = YouTubeQuota.ResetAfter(new DateTime(2026, 11, 4, 7, 59, 0, DateTimeKind.Utc), pst);
        Check(late == new DateTime(2026, 11, 4, 8, 0, 0, DateTimeKind.Utc), "quota: just before midnight Pacific resets a minute later");
        Check(YouTubeQuota.ResetAfter(DateTime.UtcNow) > DateTime.UtcNow, "quota: the default zone resets in the future");
    }

    private const string SearchFixture = """
        {"items":[
          {"id":{"kind":"youtube#video","videoId":"dQw4w9WgXcQ"},"snippet":{"title":"Never Gonna Give You Up &amp; More","channelTitle":"Rick Astley - Topic","thumbnails":{"default":{"url":"https://i.ytimg.com/vi/dQw4w9WgXcQ/default.jpg"},"medium":{"url":"https://i.ytimg.com/vi/dQw4w9WgXcQ/mqdefault.jpg"},"high":{"url":"https://i.ytimg.com/vi/dQw4w9WgXcQ/hqdefault.jpg"}}}},
          {"id":{"kind":"youtube#channel","channelId":"UC123"},"snippet":{"title":"A channel"}},
          {"id":{"kind":"youtube#video","videoId":"bad id!"},"snippet":{"title":"Bad id"}},
          {"id":{"kind":"youtube#video","videoId":"aaaaaaaaaaa"},"snippet":{"title":"","channelTitle":"x"}},
          {"id":{"kind":"youtube#video","videoId":"bbbbbbbbbbb"},"snippet":{"title":"Evil art","channelTitle":"Chan","thumbnails":{"medium":{"url":"https://evil.example.com/a.jpg"},"high":{"url":"http://i.ytimg.com/vi/x/hq.jpg"}}}}
        ]}
        """;

    private static void SearchJson()
    {
        var songs = YouTubeMap.Search(Parse(SearchFixture), StreamItemKind.Video);
        Check(songs.Count == 2, $"search: two valid videos kept ({songs.Count})");
        Check(songs[0] is { Id: "dQw4w9WgXcQ", Title: "Never Gonna Give You Up & More", Subtitle: "Rick Astley", Kind: StreamItemKind.Video }, "search: title decoded, channel cleaned");
        Check(songs[0].ImageUrl == "https://i.ytimg.com/vi/dQw4w9WgXcQ/mqdefault.jpg", "search: the medium thumbnail is preferred");
        Check(songs[1].ImageUrl is null, "search: thumbnails on other hosts or plain http are dropped");
        Check(songs[0].Uri == "" && songs[0].Playable, "search: no URI, playable");

        var playlists = YouTubeMap.Search(Parse("""{"items":[{"id":{"kind":"youtube#playlist","playlistId":"PLabc123"},"snippet":{"title":"Mix","channelTitle":"Me"}},{"id":{"videoId":"dQw4w9WgXcQ"},"snippet":{"title":"v"}}]}"""), StreamItemKind.Playlist);
        Check(playlists.Count == 1 && playlists[0] is { Kind: StreamItemKind.Playlist, Id: "PLabc123", Title: "Mix" }, "search: playlists map by playlistId");
        Check(YouTubeMap.Search(Parse("{}"), StreamItemKind.Video).Count == 0 && YouTubeMap.Search(Parse("{\"items\":5}"), StreamItemKind.Video).Count == 0, "search: missing items is empty");
    }

    private static void PlaylistJson()
    {
        var json = """
            {"items":[
              {"snippet":{"title":"Song One","videoOwnerChannelTitle":"Artist - Topic","channelTitle":"Lister","resourceId":{"kind":"youtube#video","videoId":"dQw4w9WgXcQ"},"thumbnails":{"medium":{"url":"https://i.ytimg.com/vi/a/mq.jpg"}}},"status":{"privacyStatus":"public"}},
              {"snippet":{"title":"Deleted video","resourceId":{"videoId":"aaaaaaaaaaa"}}},
              {"snippet":{"title":"Private video","resourceId":{"videoId":"bbbbbbbbbbb"}}},
              {"snippet":{"title":"Hidden gem","resourceId":{"videoId":"ccccccccccc"}},"status":{"privacyStatus":"private"}},
              {"snippet":{"title":"No id","resourceId":{}}},
              {"snippet":{"title":"Falls back","channelTitle":"Lister","resourceId":{"videoId":"ddddddddddd"}}}
            ]}
            """;
        var items = YouTubeMap.PlaylistItems(Parse(json));
        Check(items.Count == 2, $"playlist: deleted, private and id-less items skipped ({items.Count})");
        Check(items[0] is { Id: "dQw4w9WgXcQ", Title: "Song One", Subtitle: "Artist" }, "playlist: owner channel used and cleaned");
        Check(items[1].Subtitle == "Lister", "playlist: falls back to the list's channel");
    }

    private static void VideosJson()
    {
        var json = """{"items":[{"id":"dQw4w9WgXcQ","contentDetails":{"duration":"PT3M33S"}},{"id":"aaaaaaaaaaa","contentDetails":{"duration":"P0D"}},{"id":"bad","contentDetails":{"duration":"PT1S"}}]}""";
        var map = YouTubeMap.Durations(Parse(json));
        Check(map.Count == 2 && map["dQw4w9WgXcQ"] == 213 && map["aaaaaaaaaaa"] == 0, "videos: durations by valid id");
        var items = new List<StreamItem> { new(StreamItemKind.Video, "dQw4w9WgXcQ", "A", "B"), new(StreamItemKind.Video, "aaaaaaaaaaa", "C", "D"), new(StreamItemKind.Playlist, "PLx", "E", "F") };
        var merged = YouTubeMap.WithDurations(items, map);
        Check(merged[0].Duration == 213 && merged[1].Duration == 0 && merged[2].Duration == 0, "videos: durations merged, unknown stays 0");
    }

    private static void Images()
    {
        Check(YouTubeMap.Image(Parse("""{"thumbnails":{"high":{"url":"https://i.ytimg.com/vi/x/hq.jpg"}}}""")) == "https://i.ytimg.com/vi/x/hq.jpg", "image: falls back to high");
        Check(YouTubeMap.Image(Parse("""{"thumbnails":{"default":{"url":"https://yt3.ggpht.com/a.jpg"}}}""")) == "https://yt3.ggpht.com/a.jpg", "image: ggpht accepted");
        Check(YouTubeMap.Image(Parse("{}")) is null && YouTubeMap.Image(default) is null, "image: none");
    }
}
