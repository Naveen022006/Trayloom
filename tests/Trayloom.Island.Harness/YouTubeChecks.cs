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
        InnerTube();
        InnerTubeRequests();
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

    // ---------------------------------------------------------------- keyless YouTube Music (InnerTube); trimmed from real answers

    private const string InnerSongsFixture = """
        {"contents":{"tabbedSearchResultsRenderer":{"tabs":[{"tabRenderer":{"content":{"sectionListRenderer":{"contents":[{"musicShelfRenderer":{
          "contents":[
            {"musicResponsiveListItemRenderer":{
              "thumbnail":{"musicThumbnailRenderer":{"thumbnail":{"thumbnails":[{"url":"https://yt3.googleusercontent.com/aaa=w120-h120-l90-rj","width":120,"height":120},{"url":"https://yt3.googleusercontent.com/aaa=w60-h60-l90-rj","width":60,"height":60}]}}},
              "flexColumns":[
                {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"Believer","navigationEndpoint":{"watchEndpoint":{"videoId":"Kx7B-XvmFtE"}}}]}}},
                {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[
                  {"text":"Imagine Dragons","navigationEndpoint":{"browseEndpoint":{"browseId":"UC0aXrjVxG5pZr99v77wZdPQ","browseEndpointContextSupportedConfigs":{"browseEndpointContextMusicConfig":{"pageType":"MUSIC_PAGE_TYPE_ARTIST"}}}}},
                  {"text":" • "},
                  {"text":"Evolve","navigationEndpoint":{"browseEndpoint":{"browseId":"MPREb_q16Gzaa1WK8","browseEndpointContextSupportedConfigs":{"browseEndpointContextMusicConfig":{"pageType":"MUSIC_PAGE_TYPE_ALBUM"}}}}},
                  {"text":" • "},
                  {"text":"3:25"}]}}},
                {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"7B plays"}]}}}],
              "playlistItemData":{"videoId":"Kx7B-XvmFtE"}}},
            {"musicResponsiveListItemRenderer":{
              "thumbnail":{"musicThumbnailRenderer":{"thumbnail":{"thumbnails":[{"url":"https://lh3.googleusercontent.com/bbb=w226-h226","width":226,"height":226}]}}},
              "flexColumns":[
                {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"Believer","navigationEndpoint":{"watchEndpoint":{"videoId":"PFcAim-pGfU"}}}]}}},
                {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[
                  {"text":"Julien Cohen","navigationEndpoint":{"browseEndpoint":{"browseEndpointContextSupportedConfigs":{"browseEndpointContextMusicConfig":{"pageType":"MUSIC_PAGE_TYPE_ARTIST"}}}}},{"text":", "},
                  {"text":"Violamarie","navigationEndpoint":{"browseEndpoint":{"browseEndpointContextSupportedConfigs":{"browseEndpointContextMusicConfig":{"pageType":"MUSIC_PAGE_TYPE_ARTIST"}}}}},{"text":" & "},
                  {"text":"DDKN","navigationEndpoint":{"browseEndpoint":{"browseEndpointContextSupportedConfigs":{"browseEndpointContextMusicConfig":{"pageType":"MUSIC_PAGE_TYPE_ARTIST"}}}}},{"text":" • "},
                  {"text":"Believer","navigationEndpoint":{"browseEndpoint":{"browseEndpointContextSupportedConfigs":{"browseEndpointContextMusicConfig":{"pageType":"MUSIC_PAGE_TYPE_ALBUM"}}}}},{"text":" • "},
                  {"text":"1:02:03"}]}}}],
              "playlistItemData":{"videoId":"PFcAim-pGfU"}}},
            {"musicResponsiveListItemRenderer":{
              "flexColumns":[
                {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"No length, no art","navigationEndpoint":{"watchEndpoint":{"videoId":"aaaaaaaaaaa"}}}]}}},
                {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"Some Artist","navigationEndpoint":{"browseEndpoint":{"browseEndpointContextSupportedConfigs":{"browseEndpointContextMusicConfig":{"pageType":"MUSIC_PAGE_TYPE_ARTIST"}}}}}]}}}],
              "playlistItemData":{"videoId":"aaaaaaaaaaa"}}},
            {"musicResponsiveListItemRenderer":{
              "thumbnail":{"musicThumbnailRenderer":{"thumbnail":{"thumbnails":[{"url":"https://evil.example.com/a.jpg","width":120,"height":120},{"url":"http://i.ytimg.com/vi/x/hq.jpg","width":120,"height":120}]}}},
              "flexColumns":[
                {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"Art on other hosts","navigationEndpoint":{"watchEndpoint":{"videoId":"bbbbbbbbbbb"}}}]}}}],
              "playlistItemData":{"videoId":"bbbbbbbbbbb"}}},
            {"musicResponsiveListItemRenderer":{
              "flexColumns":[
                {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"An artist, not a song","navigationEndpoint":{"browseEndpoint":{"browseId":"UC0aXrjVxG5pZr99v77wZdPQ"}}}]}}},
                {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"Artist"},{"text":" • "},{"text":"80M subscribers"}]}}}]}},
            {"musicResponsiveListItemRenderer":{
              "flexColumns":[
                {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"Bad id","navigationEndpoint":{"watchEndpoint":{"videoId":"bad id!"}}}]}}}],
              "playlistItemData":{"videoId":"bad id!"}}},
            {"musicResponsiveListItemRenderer":{
              "flexColumns":[
                {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"A podcast episode","navigationEndpoint":{"watchEndpoint":{"videoId":"ccccccccccc","watchEndpointMusicSupportedConfigs":{"watchEndpointMusicConfig":{"musicVideoType":"MUSIC_VIDEO_TYPE_PODCAST_EPISODE"}}}}}]}}}],
              "playlistItemData":{"videoId":"ccccccccccc"}}}
          ],
          "continuations":[{"nextContinuationData":{"continuation":"ErEDEhhCZWxpZXZlciBJbWFnaW5l","clickTrackingParams":"CAwQ"}}]}}]}}}}]}}}
        """;

    private const string InnerMoreFixture = """
        {"continuationContents":{"musicShelfContinuation":{"contents":[
          {"musicResponsiveListItemRenderer":{
            "flexColumns":[{"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"Second page","navigationEndpoint":{"watchEndpoint":{"videoId":"ddddddddddd"}}}]}}}],
            "playlistItemData":{"videoId":"ddddddddddd"}}}]}}}
        """;

    private const string InnerPlaylistsFixture = """
        {"contents":{"tabbedSearchResultsRenderer":{"tabs":[{"tabRenderer":{"content":{"sectionListRenderer":{"contents":[{"musicShelfRenderer":{"contents":[
          {"musicResponsiveListItemRenderer":{
            "thumbnail":{"musicThumbnailRenderer":{"thumbnail":{"thumbnails":[{"url":"https://i.ytimg.com/vi/1A0dUOfwTDQ/hqdefault.jpg?sqp=a","width":400,"height":225},{"url":"https://i.ytimg.com/vi/1A0dUOfwTDQ/hqdefault.jpg?sqp=b","width":480,"height":270}]}}},
            "flexColumns":[
              {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"Malayalam LoFi | eternaL"}]}}},
              {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"eternaL","navigationEndpoint":{"browseEndpoint":{"browseId":"UChyMVotGoVtOwcrVWLW0z6A"}}},{"text":" • "},{"text":"460K views"}]}}}],
            "navigationEndpoint":{"browseEndpoint":{"browseId":"VLPLBYtTyeevqOx5KvWttiOPavvMPdxsyQAp","browseEndpointContextSupportedConfigs":{"browseEndpointContextMusicConfig":{"pageType":"MUSIC_PAGE_TYPE_PLAYLIST"}}}}}},
          {"musicResponsiveListItemRenderer":{
            "flexColumns":[
              {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"Lofi Flow State"}]}}},
              {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"YouTube Music"},{"text":" • "},{"text":"68 songs"}]}}}],
            "navigationEndpoint":{"browseEndpoint":{"browseId":"VLRDCLAK5uy_nZJzoZEBYRptA2XXskbxGTvKkevapT_F4"}}}},
          {"musicResponsiveListItemRenderer":{
            "flexColumns":[
              {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"An album, not a playlist"}]}}}],
            "navigationEndpoint":{"browseEndpoint":{"browseId":"MPREb_q16Gzaa1WK8"}}}}
        ]}}]}}}}]}}}
        """;

    private const string InnerBrowseFixture = """
        {"contents":{"twoColumnBrowseResultsRenderer":{"secondaryContents":{"sectionListRenderer":{"contents":[{"musicPlaylistShelfRenderer":{"contents":[
          {"musicResponsiveListItemRenderer":{
            "thumbnail":{"musicThumbnailRenderer":{"thumbnail":{"thumbnails":[{"url":"https://yt3.googleusercontent.com/ccc=w60-h60-l90-rj","width":60,"height":60}]}}},
            "flexColumns":[
              {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"Forest Lake Walk","navigationEndpoint":{"watchEndpoint":{"videoId":"qGVMBirxwWc"}}}]}}},
              {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"No Spirit","navigationEndpoint":{"browseEndpoint":{"browseEndpointContextSupportedConfigs":{"browseEndpointContextMusicConfig":{"pageType":"MUSIC_PAGE_TYPE_ARTIST"}}}}}]}}},
              {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"Reminiscing","navigationEndpoint":{"browseEndpoint":{"browseEndpointContextSupportedConfigs":{"browseEndpointContextMusicConfig":{"pageType":"MUSIC_PAGE_TYPE_ALBUM"}}}}}]}}}],
            "fixedColumns":[{"musicResponsiveListItemFixedColumnRenderer":{"text":{"runs":[{"text":"2:57"}]}}}],
            "playlistItemData":{"videoId":"qGVMBirxwWc"}}},
          {"musicResponsiveListItemRenderer":{
            "flexColumns":[
              {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"Upload without length","navigationEndpoint":{"watchEndpoint":{"videoId":"eeeeeeeeeee"}}}]}}},
              {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"the_relaxing & lo-fi_music","navigationEndpoint":{"browseEndpoint":{"browseEndpointContextSupportedConfigs":{"browseEndpointContextMusicConfig":{"pageType":"MUSIC_PAGE_TYPE_USER_CHANNEL"}}}}}]}}}],
            "playlistItemData":{"videoId":"eeeeeeeeeee"}}},
          {"musicResponsiveListItemRenderer":{
            "musicItemRendererDisplayPolicy":"MUSIC_ITEM_RENDERER_DISPLAY_POLICY_GREY_OUT",
            "flexColumns":[
              {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"Unavailable song"}]}}}]}}
        ]}}]}}}}}
        """;

    private static void InnerTube()
    {
        var page = InnerTubeMap.Search(Parse(InnerSongsFixture), StreamItemKind.Video);
        Check(page is not null && page.Items.Count == 4, $"innertube songs: four songs kept, the artist, the bad id and the podcast skipped ({page?.Items.Count})");
        var songs = page?.Items ?? [];
        if (songs.Count == 4)
        {
            Check(songs[0] is { Id: "Kx7B-XvmFtE", Title: "Believer", Subtitle: "Imagine Dragons · Evolve", Kind: StreamItemKind.Video, Duration: 205 }, "innertube songs: title, artist · album, duration and id");
            Check(songs[0].ImageUrl == "https://yt3.googleusercontent.com/aaa=w60-h60-l90-rj", "innertube songs: the smallest thumbnail of at least 60 px");
            Check(songs[1].Subtitle == "Julien Cohen, Violamarie, DDKN · Believer" && songs[1].Duration == 3723, "innertube songs: artists joined with a comma, hour clock parsed");
            Check(songs[1].ImageUrl == "https://lh3.googleusercontent.com/bbb=w226-h226", "innertube songs: an lh3 thumbnail is accepted");
            Check(songs[2].Duration == 0 && songs[2].Subtitle == "Some Artist" && songs[2].ImageUrl is null, "innertube songs: a missing duration and thumbnail are 0 and null");
            Check(songs[3].ImageUrl is null && songs[3].Subtitle == "", "innertube songs: thumbnails on other hosts or plain http are dropped");
        }

        Check(page?.Continuation == "ErEDEhhCZWxpZXZlciBJbWFnaW5l", "innertube songs: the continuation token is read");

        var more = InnerTubeMap.Search(Parse(InnerMoreFixture), StreamItemKind.Video);
        Check(more is { Continuation: null } && more.Items.Count == 1 && more.Items[0].Id == "ddddddddddd", "innertube paging: a continuation answer maps its songs and ends without a token");
        var none = InnerTubeMap.Search(Parse("""{"contents":{"tabbedSearchResultsRenderer":{"tabs":[{"tabRenderer":{"content":{"sectionListRenderer":{"contents":[{"itemSectionRenderer":{"contents":[]}}]}}}}]}}}"""), StreamItemKind.Video);
        Check(none is { Continuation: null } && none.Items.Count == 0, "innertube songs: no results is an empty page, not an error");
        Check(InnerTubeMap.Search(Parse("{}"), StreamItemKind.Video) is null && InnerTubeMap.Search(Parse("[]"), StreamItemKind.Video) is null && InnerTubeMap.Search(Parse("""{"contents":5}"""), StreamItemKind.Video) is null, "innertube songs: an unknown shape is null");

        var playlists = InnerTubeMap.Search(Parse(InnerPlaylistsFixture), StreamItemKind.Playlist);
        Check(playlists is not null && playlists.Items.Count == 2 && playlists.Continuation is null, $"innertube playlists: two playlists, the album skipped ({playlists?.Items.Count})");
        if (playlists is { Items.Count: 2 })
        {
            Check(playlists.Items[0] is { Kind: StreamItemKind.Playlist, Id: "PLBYtTyeevqOx5KvWttiOPavvMPdxsyQAp", Title: "Malayalam LoFi | eternaL", Subtitle: "eternaL · 460K views", Count: 0 }, "innertube playlists: the VL prefix is dropped, owner and views as subtitle");
            Check(playlists.Items[0].ImageUrl == "https://i.ytimg.com/vi/1A0dUOfwTDQ/hqdefault.jpg?sqp=a", "innertube playlists: the smaller of two big thumbnails");
            Check(playlists.Items[1] is { Id: "RDCLAK5uy_nZJzoZEBYRptA2XXskbxGTvKkevapT_F4", Subtitle: "YouTube Music · 68 songs", Count: 68, ImageUrl: null }, "innertube playlists: a featured playlist has its song count");
        }

        var tracks = InnerTubeMap.PlaylistSongs(Parse(InnerBrowseFixture));
        Check(tracks is { Count: 2 }, $"innertube browse: two songs, the unavailable one skipped ({tracks?.Count})");
        if (tracks is { Count: 2 })
        {
            Check(tracks[0] is { Id: "qGVMBirxwWc", Title: "Forest Lake Walk", Subtitle: "No Spirit · Reminiscing", Duration: 177 }, "innertube browse: columns for artist and album, length from the fixed column");
            Check(tracks[1] is { Id: "eeeeeeeeeee", Subtitle: "the_relaxing & lo-fi_music", Duration: 0, ImageUrl: null }, "innertube browse: an uploader counts as the artist; no length, no art");
        }

        Check(InnerTubeMap.PlaylistSongs(Parse("{}")) is null && InnerTubeMap.PlaylistSongs(Parse("""{"contents":{"twoColumnBrowseResultsRenderer":{"secondaryContents":{"sectionListRenderer":{"contents":[]}}}}}""")) is { Count: 0 }, "innertube browse: unknown shape is null, an empty list is empty");

        Check(InnerTubeMap.Thumbnail(Parse("""[{"url":"https://i.ytimg.com/a","width":226},{"url":"https://i.ytimg.com/b","width":60},{"url":"https://i.ytimg.com/c","width":120}]""")) == "https://i.ytimg.com/b", "innertube thumbnail: smallest of at least 60");
        Check(InnerTubeMap.Thumbnail(Parse("""[{"url":"https://i.ytimg.com/a","width":40},{"url":"https://i.ytimg.com/b","width":50}]""")) == "https://i.ytimg.com/b", "innertube thumbnail: all too small, the largest");
        Check(InnerTubeMap.Thumbnail(Parse("[]")) is null && InnerTubeMap.Thumbnail(default) is null && InnerTubeMap.Thumbnail(Parse("""[{"url":"https://evil.example.com/a","width":100}]""")) is null, "innertube thumbnail: none, or none allowed");

        Check(InnerTubeMap.ClockSeconds("3:25") == 205 && InnerTubeMap.ClockSeconds("0:09") == 9 && InnerTubeMap.ClockSeconds("1:02:03") == 3723, "innertube clock: m:ss and h:mm:ss");
        Check(InnerTubeMap.ClockSeconds("7B plays") == 0 && InnerTubeMap.ClockSeconds("") == 0 && InnerTubeMap.ClockSeconds(null) == 0 && InnerTubeMap.ClockSeconds("3:5") == 0, "innertube clock: anything else is 0");
    }

    private static void InnerTubeRequests()
    {
        const string html = """<script>ytcfg.set({"INNERTUBE_API_KEY":"x","INNERTUBE_CLIENT_NAME":"WEB_REMIX","INNERTUBE_CLIENT_VERSION":"1.20261031.09.00","INNERTUBE_CONTEXT":{"client":{"hl":"en-GB"}}});</script>""";
        Check(InnerTubeMap.ClientVersion(html) == "1.20261031.09.00", "innertube version: read from the page's JSON");
        Check(InnerTubeMap.ClientVersion("""ytcfg.set(\x7b\x22INNERTUBE_CLIENT_VERSION\x22:\x221.20261101.01.00\x22\x7d)""") == "1.20261101.01.00", "innertube version: read from the escaped form");
        Check(InnerTubeMap.ClientVersion("<html>nothing here</html>") is null && InnerTubeMap.ClientVersion("") is null && InnerTubeMap.ClientVersion(null) is null, "innertube version: missing is null");
        Check(InnerTubeMap.ClientVersion("\"INNERTUBE_CLIENT_VERSION\":\"<script>alert(1)</script>\"") is null && InnerTubeMap.ClientVersion("\"INNERTUBE_CLIENT_VERSION\":\"\"") is null, "innertube version: something that is not a version is ignored");
        Check(InnerTubeMap.ClientVersion("\"INNERTUBE_CLIENT_VERSION\":\"" + InnerTubeMap.FallbackVersion + "\"") == InnerTubeMap.FallbackVersion, "innertube version: the fallback is shaped like a version");

        Check(InnerTubeMap.Region("de") == "DE" && InnerTubeMap.Region("IN") == "IN" && InnerTubeMap.Region("") == "US" && InnerTubeMap.Region(null) == "US" && InnerTubeMap.Region("USA") == "US" && InnerTubeMap.Region("1a") == "US", "innertube region: two letters or US");

        using var search = JsonDocument.Parse(InnerTubeMap.SearchBody("1.2.3", "IN", "say \"hi\" \\ 日本", InnerTubeMap.SongsParams));
        var client = search.RootElement.GetProperty("context").GetProperty("client");
        Check(client.GetProperty("clientName").GetString() == "WEB_REMIX" && client.GetProperty("clientVersion").GetString() == "1.2.3" && client.GetProperty("hl").GetString() == "en" && client.GetProperty("gl").GetString() == "IN", "innertube body: the WEB_REMIX client context");
        Check(search.RootElement.GetProperty("query").GetString() == "say \"hi\" \\ 日本" && search.RootElement.GetProperty("params").GetString() == InnerTubeMap.SongsParams, "innertube body: the query is escaped by the JSON writer and survives intact");
        using var unfiltered = JsonDocument.Parse(InnerTubeMap.SearchBody("1.2.3", "US", "x", null));
        Check(!unfiltered.RootElement.TryGetProperty("params", out _), "innertube body: no params without a filter");
        using var next = JsonDocument.Parse(InnerTubeMap.ContinuationBody("1.2.3", "US", "tok%3D"));
        Check(next.RootElement.GetProperty("continuation").GetString() == "tok%3D" && !next.RootElement.TryGetProperty("query", out _), "innertube body: a continuation carries only the token");
        using var browse = JsonDocument.Parse(InnerTubeMap.BrowseBody("1.2.3", "US", "VLPLabc"));
        Check(browse.RootElement.GetProperty("browseId").GetString() == "VLPLabc", "innertube body: browse id");
        Check(InnerTubeMap.SongsParams.Length > 0 && InnerTubeMap.FeaturedPlaylistsParams != InnerTubeMap.CommunityPlaylistsParams && InnerTubeMap.FeaturedPlaylistsParams.StartsWith("Eg", StringComparison.Ordinal), "innertube params: songs, featured and community filters differ");
        Check(StreamStrings.Message(StreamStatus.Failed, StreamKind.YouTube) == "YouTube Music didn’t respond. Try again.", "innertube errors: the failure message names YouTube Music");
    }

    private static void Images()
    {
        Check(YouTubeMap.Image(Parse("""{"thumbnails":{"high":{"url":"https://i.ytimg.com/vi/x/hq.jpg"}}}""")) == "https://i.ytimg.com/vi/x/hq.jpg", "image: falls back to high");
        Check(YouTubeMap.Image(Parse("""{"thumbnails":{"default":{"url":"https://yt3.ggpht.com/a.jpg"}}}""")) == "https://yt3.ggpht.com/a.jpg", "image: ggpht accepted");
        Check(YouTubeMap.Image(Parse("{}")) is null && YouTubeMap.Image(default) is null, "image: none");
    }
}
