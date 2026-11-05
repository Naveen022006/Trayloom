// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Features.Island;

namespace Trayloom.Island.Harness;

/// <summary>
/// Checks of the streaming list without any UI: the rows of <see cref="StreamListModel"/> (grouping, "Show all", "Show more", the filter
/// inside a list, the highlight and what Enter does) and which source the browser shows (<see cref="MusicSources"/>). Synthetic data only;
/// no network, nothing plays.
/// </summary>
internal static class StreamListChecks
{
    private static Action<bool, string> _check = (_, _) => { };

    public static void Run(Action<bool, string> check)
    {
        _check = check;
        Grouping();
        PlainLists();
        Highlight();
        Actions();
        Paging();
        Filter();
        Sources();
    }

    private static void Check(bool ok, string what) => _check(ok, "stream list: " + what);

    private static StreamItem Song(int n, bool playable = true) =>
        new(StreamItemKind.Track, "t" + n, "Song " + n, "Artist " + n) { Duration = 200, Playable = playable };

    private static StreamItem Album(int n) => new(StreamItemKind.Album, "a" + n, "Album " + n, "Artist " + n);

    private static StreamItem Video(int n) => new(StreamItemKind.Video, "v" + n, "Video " + n, "Channel " + n);

    private static StreamSection Section(StreamScope scope, string title, int total, IEnumerable<StreamItem> items) => new(scope, title, [.. items], total);

    private static StreamPage SearchPage() => new(
        [
            Section(StreamScope.Songs, "Songs", 120, Enumerable.Range(1, 5).Select(i => Song(i))),
            Section(StreamScope.Albums, "Albums", 2, Enumerable.Range(1, 2).Select(Album)),
            Section(StreamScope.Artists, "Artists", 0, []),
        ],
        null);

    private static string Shape(StreamListModel model) => string.Concat(model.Rows.Select(r => r.Kind switch
    {
        StreamRowKind.Header => 'H',
        StreamRowKind.ShowMore => 'M',
        _ => 'i',
    }));

    // ---------------------------------------------------------------- grouping and Show all

    private static void Grouping()
    {
        var model = new StreamListModel();
        model.Update(SearchPage(), grouped: true, filter: "", autoHighlight: true, canQueue: true);
        Check(Shape(model) == "HiiiiHii", "an All search has a header per section with results and caps a section at four rows (" + Shape(model) + ")");
        var songs = model.Rows[0];
        Check(songs.Title == "Songs" && songs.Total == 120 && songs.Shown == StreamListModel.GroupCap && songs.Target == StreamScope.Songs, "the Songs header counts all results and what is listed");
        Check(songs.Total > songs.Shown, "Songs offers Show all");
        var albums = model.Rows[5];
        Check(albums.Title == "Albums" && albums.Total == albums.Shown, "Albums lists everything: no Show all");
        var all = StreamListModel.ShowAllOf(songs);
        Check(all.Kind == StreamActionKind.ShowAll && all.Scope == StreamScope.Songs, "Show all switches to the section's scope");
        Check(model.Rows.All(r => r.Kind != StreamRowKind.ShowMore), "a grouped search has no Show more row");
        Check(model.Rows.Where(r => r.Kind == StreamRowKind.Header).All(r => !r.Selectable), "headers cannot be highlighted");
        Check(model.SelectableCount == 6, "six rows can be highlighted");

        // row heights: a header is shorter than a song, and Tops is their prefix sum
        Check(model.Tops.Count == model.Rows.Count + 1 && model.Tops[0] == 0, "Tops has one entry more than the rows");
        var expected = 2 * StreamListModel.HeaderHeight + 6 * StreamListModel.RowHeight;
        Check(Math.Abs(model.Tops[^1] - expected) < 0.001, "Tops adds up the header and song heights");
    }

    private static void PlainLists()
    {
        var page = new StreamPage([Section(StreamScope.Songs, "", 3, Enumerable.Range(1, 3).Select(i => Song(i)))], null);
        var model = new StreamListModel();
        model.Update(page, grouped: false, filter: "", autoHighlight: false, canQueue: false);
        Check(Shape(model) == "iii", "a plain list has no header and no Show more row when complete");
        Check(model.Highlight == -1, "browsing highlights nothing until an arrow key is pressed");

        // a single section with a title still gets no header (one section: nothing to tell apart)
        var titled = new StreamPage([Section(StreamScope.Songs, "Songs", 3, Enumerable.Range(1, 3).Select(i => Song(i)))], null);
        model.Update(titled, grouped: false, filter: "", autoHighlight: false, canQueue: false);
        Check(Shape(model) == "iii", "one section in a scope search shows no header");

        // two sections (an artist: albums and singles) do get headers
        var two = new StreamPage([Section(StreamScope.Albums, "Albums", 1, [Album(1)]), Section(StreamScope.Songs, "Singles", 1, [Song(1)])], null);
        model.Update(two, grouped: false, filter: "", autoHighlight: false, canQueue: false);
        Check(Shape(model) == "HiHi", "two sections get headers even when not grouped (" + Shape(model) + ")");

        model.Update(null, grouped: false, filter: "", autoHighlight: true, canQueue: false);
        Check(model.Rows.Count == 0 && model.Highlight == -1 && model.Tops.Count == 1, "no page: no rows");
    }

    // ---------------------------------------------------------------- highlight

    private static void Highlight()
    {
        var model = new StreamListModel();
        model.Update(SearchPage(), grouped: true, filter: "", autoHighlight: true, canQueue: true);
        Check(model.Highlight == 1, "a search highlights the top hit, not the header above it");

        Check(model.Move(-1) == false && model.Highlight == 1, "up from the first result does not move");
        for (var i = 0; i < 3; i++)
        {
            model.Move(1);
        }

        Check(model.Highlight == 4, "down moves one result at a time within a section");
        Check(model.Move(1) && model.Highlight == 6, "down skips the next header (row 5)");
        Check(model.Move(-1) && model.Highlight == 4, "up skips the header too");
        Check(model.LeadRows(6) == 1 && model.LeadRows(4) == 0, "the first row of a section comes with its header into view");

        Check(model.Move(StreamListModel.PageStep) && model.Highlight == 7, "a page down stops at the last result");
        Check(!model.Move(1), "down from the last result does not move");
        Check(model.Move(-StreamListModel.PageStep) && model.Highlight == 1, "a page up stops at the first result");

        // the highlight stays on the same item when the list is rebuilt around it
        model.SetHighlight(3);
        var id = model.HighlightId;
        model.Update(SearchPage(), grouped: true, filter: "", autoHighlight: true, canQueue: true);
        Check(model.Highlight == 3 && model.HighlightId == id, "a rebuilt list keeps the highlight on the same item");
        model.SetHighlight(0);
        Check(model.Highlight == -1, "a header cannot be highlighted by a click");

        // browsing: the first arrow key highlights from the ends
        var browse = new StreamListModel();
        browse.Update(new StreamPage([Section(StreamScope.Songs, "", 3, Enumerable.Range(1, 3).Select(i => Song(i)))], null), false, "", false, false);
        Check(browse.Move(1) && browse.Highlight == 0, "down with nothing highlighted takes the first row");
        var up = new StreamListModel();
        up.Update(new StreamPage([Section(StreamScope.Songs, "", 3, Enumerable.Range(1, 3).Select(i => Song(i)))], null), false, "", false, false);
        Check(up.Move(-1) && up.Highlight == 2, "up with nothing highlighted takes the last row");

        // a seeded id (a panel rebuilt on a track change) is found again
        var seeded = new StreamListModel();
        seeded.Keep("i:" + Song(2).Key);
        seeded.Update(new StreamPage([Section(StreamScope.Songs, "", 3, Enumerable.Range(1, 3).Select(i => Song(i)))], null), false, "", false, false);
        Check(seeded.Highlight == 1, "Keep seeds the highlight of a new model");
    }

    // ---------------------------------------------------------------- what Enter does

    private static void Actions()
    {
        var page = new StreamPage(
            [
                Section(StreamScope.Songs, "Songs", 3, [Song(1), Song(2, playable: false)]),
                Section(StreamScope.Albums, "Albums", 1, [Album(1)]),
                Section(StreamScope.Songs, "Videos", 1, [Video(1)]),
            ],
            null);
        var model = new StreamListModel();
        model.Update(page, grouped: true, filter: "", autoHighlight: true, canQueue: true);
        Check(model.Highlight == 1, "the first song is highlighted");
        Check(model.Activate(false, false).Kind == StreamActionKind.Play && model.Activate(false, false).Item?.Id == "t1", "Enter plays the song");
        Check(model.Activate(true, false).Kind == StreamActionKind.Queue, "Shift+Enter adds the song to the queue");
        Check(model.Activate(false, true).Kind == StreamActionKind.Queue, "Ctrl+Enter adds the song to the queue");
        Check(model.Activate(true, true).Kind == StreamActionKind.Queue, "Ctrl+Shift+Enter adds it too");

        model.Move(1);
        Check(model.Activate(false, false).Kind == StreamActionKind.None, "a song that cannot be played does nothing");
        model.Move(1);
        var album = model.Activate(false, false);
        Check(album.Kind == StreamActionKind.OpenContainer && album.Item?.Id == "a1", "Enter on an album opens it");
        Check(model.Activate(true, true).Kind == StreamActionKind.OpenContainer, "Shift / Ctrl do not change what an album does");
        model.Move(1);
        var video = model.Activate(false, true);
        Check(video.Kind == StreamActionKind.Open && video.Item?.Id == "v1", "Enter on a video opens it, with or without Ctrl");

        // a service that cannot queue (YouTube): Shift / Ctrl play
        model.Update(page, grouped: true, filter: "", autoHighlight: true, canQueue: false);
        model.SetHighlight(1);
        Check(model.Activate(true, false).Kind == StreamActionKind.Play && model.Activate(false, true).Kind == StreamActionKind.Play, "without a queue, Shift / Ctrl+Enter play");

        // nothing highlighted
        model.SetHighlight(-1);
        Check(model.Activate(false, false).Kind == StreamActionKind.None, "nothing highlighted: nothing happens");

        // the row actions that do not need the highlight
        var artist = new StreamItem(StreamItemKind.Artist, "r1", "Artist", "Artist");
        var playlist = new StreamItem(StreamItemKind.Playlist, "p1", "Playlist", "Me");
        Check(StreamListModel.ActionFor(new StreamRow(StreamRowKind.Item, "i:Artist:r1") { Item = artist }).Kind == StreamActionKind.OpenContainer, "an artist opens its list");
        Check(StreamListModel.ActionFor(new StreamRow(StreamRowKind.Item, "i:Playlist:p1") { Item = playlist }).Kind == StreamActionKind.OpenContainer, "a playlist opens its list");
        Check(StreamListModel.ActionFor(new StreamRow(StreamRowKind.Header, "h")).Kind == StreamActionKind.None, "a header does nothing");
        Check(StreamListModel.ActionFor(new StreamRow(StreamRowKind.ShowMore, "more")).Kind == StreamActionKind.ShowMore, "the Show more row shows more");
    }

    // ---------------------------------------------------------------- paging

    private static void Paging()
    {
        var first = new StreamPage([Section(StreamScope.Songs, "Songs", 25, Enumerable.Range(1, 10).Select(i => Song(i)))], 10);
        var second = new StreamPage([Section(StreamScope.Songs, "Songs", 25, Enumerable.Range(11, 10).Select(i => Song(i)))], 20);
        var third = new StreamPage([Section(StreamScope.Songs, "Songs", 25, Enumerable.Range(21, 5).Select(i => Song(i)))], null);
        var model = new StreamListModel();
        model.Update(first, grouped: false, filter: "", autoHighlight: true, canQueue: true);
        Check(Shape(model) == "iiiiiiiiiiM" && model.NextOffset == 10, "a page with more behind it ends in a Show more row");
        Check(model.Highlight == 0, "the top hit of a scope search is highlighted");
        model.Move(StreamListModel.PageStep * 2);
        Check(model.Highlight == 10 && model.Activate(false, false).Kind == StreamActionKind.ShowMore, "Enter on Show more asks for the next page");

        var merged = StreamListModel.Merge([first, second, third]);
        Check(merged.Sections.Count == 1 && merged.Sections[0].Items.Count == 25, "the pages join into one section");
        Check(merged.NextOffset is null, "the last page says the list is complete");
        Check(StreamListModel.Merge([first, second]).NextOffset == 20, "the last page decides where the next one starts");
        var again = StreamListModel.Merge([first, first]);
        Check(again.Sections[0].Items.Count == 10, "a page listed twice adds nothing");
        model.Update(merged, grouped: false, filter: "", autoHighlight: false, canQueue: true);
        Check(model.Rows.Count == 25 && model.NextOffset is null && model.Rows.All(r => r.Kind == StreamRowKind.Item), "the joined list has all rows and no Show more");

        // a grouped (All) search never shows Show more: Show all comes first
        model.Update(first, grouped: true, filter: "", autoHighlight: true, canQueue: true);
        Check(model.Rows.All(r => r.Kind != StreamRowKind.ShowMore), "a grouped search has no Show more row");
    }

    // ---------------------------------------------------------------- filter inside a list

    private static void Filter()
    {
        var songs = new[]
        {
            new StreamItem(StreamItemKind.Track, "t1", "Café del Mar", "Energy 52"),
            new StreamItem(StreamItemKind.Track, "t2", "Silence", "Delerium"),
            new StreamItem(StreamItemKind.Track, "t3", "Sunrise", "Norah"),
        };
        var page = new StreamPage([Section(StreamScope.Songs, "", 3, songs)], null);
        var model = new StreamListModel();
        model.Update(page, grouped: false, filter: "cafe", autoHighlight: true, canQueue: true);
        Check(model.Rows.Count == 1 && model.Rows[0].Item?.Id == "t1", "the filter ignores accents and case");
        model.Update(page, grouped: false, filter: "norah", autoHighlight: true, canQueue: true);
        Check(model.Rows.Count == 1 && model.Rows[0].Item?.Id == "t3", "the filter also looks at the subtitle");
        model.Update(page, grouped: false, filter: "zzz", autoHighlight: true, canQueue: true);
        Check(model.Rows.Count == 0 && model.Highlight == -1, "a filter without a match lists nothing");
        model.Update(page, grouped: false, filter: "  ", autoHighlight: false, canQueue: true);
        Check(model.Rows.Count == 3, "a blank filter lists everything");
    }

    // ---------------------------------------------------------------- sources

    private static void Sources()
    {
        Check(MusicSources.Available(false, false, false).Count == 0, "sources: nothing is on by default without Apple Music");
        Check(MusicSources.Available(true, false, false) is [MusicSource.Apple], "sources: Apple Music alone (no switcher, as before)");
        Check(MusicSources.Available(true, true, true) is [MusicSource.Apple, MusicSource.Spotify, MusicSource.YouTube], "sources: all three in switcher order");
        Check(MusicSources.Available(false, true, true) is [MusicSource.Spotify, MusicSource.YouTube], "sources: Spotify and YouTube Music without Apple Music");
        Check(MusicSources.Available(false, false, true) is [MusicSource.YouTube], "sources: YouTube Music alone");

        var all = MusicSources.Available(true, true, true);
        Check(MusicSources.Resolve("", [], null) is null, "sources: no source, nothing to show");
        Check(MusicSources.Resolve("spotify", all, "AppleInc.AppleMusicWin") == MusicSource.Spotify, "sources: the saved choice wins over the playing app");
        Check(MusicSources.Resolve("", all, "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify") == MusicSource.Spotify, "sources: automatic follows the playing Spotify");
        Check(MusicSources.Resolve("", all, "AppleInc.AppleMusicWin_nzyj5cx40ttqa!App") == MusicSource.Apple, "sources: automatic follows the playing Apple Music");
        Check(MusicSources.Resolve("", all, "chrome.exe") == MusicSource.Apple, "sources: an unknown player falls back to the first source");
        Check(MusicSources.Resolve("", [MusicSource.Spotify, MusicSource.YouTube], "AppleInc.AppleMusicWin") == MusicSource.Spotify, "sources: the playing app is ignored when its source is off");
        Check(MusicSources.Resolve("apple", [MusicSource.Spotify], null) == MusicSource.Spotify, "sources: a saved source that is not available is skipped");
        Check(MusicSources.Resolve("nonsense", all, null) == MusicSource.Apple, "sources: an unknown saved value means automatic");
        Check(MusicSources.Resolve("YouTube", all, null) == MusicSource.YouTube, "sources: the saved value is not case sensitive");

        Check(MusicSources.Name(MusicSource.Spotify) == "spotify" && MusicSources.Parse(MusicSources.Name(MusicSource.YouTube)) == MusicSource.YouTube, "sources: the saved names round-trip");
        Check(MusicSources.FromPlayer("Microsoft.YouTubeMusic_x") == MusicSource.YouTube && MusicSources.FromPlayer(null) is null, "sources: the YouTube Music app is recognised, no player is none");
        Check(MusicSources.Brand(MusicSource.Apple) == "Apple Music" && MusicSources.Brand(MusicSource.YouTube) == "YouTube Music", "sources: the switcher shows brand names");

        Check(MusicSources.Selected(MusicSource.Spotify, "AppleInc.AppleMusicWin") == MusicSource.Spotify, "sources: the switch selects the resolved source first");
        Check(MusicSources.Selected(null, "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify") == MusicSource.Spotify, "sources: with none resolved the switch follows the playing player");
        Check(MusicSources.Selected(null, "chrome.exe") == MusicSource.Apple && MusicSources.Selected(null, null) == MusicSource.Apple, "sources: with nothing to follow the switch selects Apple Music");
        string[] sessions = ["chrome.exe", "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify"];
        Check(MusicSources.SessionFor(sessions, MusicSource.Spotify) == sessions[1], "sources: a live Spotify session is found for the switch");
        Check(MusicSources.SessionFor(sessions, MusicSource.Apple) is null && MusicSources.SessionFor([], MusicSource.YouTube) is null, "sources: no session of that source means no Playback.Choose");
    }
}
