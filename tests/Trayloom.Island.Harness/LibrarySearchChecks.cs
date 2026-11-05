// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Features.Island;

namespace Trayloom.Island.Harness;

/// <summary>
/// Checks of the Library search without any UI: the result rows (<see cref="LibrarySearchModel"/>: grouping, caps, scope chips, the highlight,
/// what Enter does and with which playlist), the Esc order and the focus hand-over across page rebuilds (<see cref="LibraryFocusCarry"/>).
/// Synthetic data only; when <c>TRAYLOOM_AMDB</c> names a copy of a library file a few invariants are checked on it too.
/// </summary>
internal static class LibrarySearchChecks
{
    private static Action<bool, string> _check = (_, _) => { };

    public static void Run(Action<bool, string> check)
    {
        _check = check;
        Grouping();
        Scopes();
        Highlight();
        Actions();
        Containers();
        ArtAndTabs();
        EscapeOrder();
        FocusCarry();
        Unplayable();
        PagePositions();
        VirtualListLayout();
        if (Environment.GetEnvironmentVariable("TRAYLOOM_AMDB") is { Length: > 0 } path && File.Exists(path))
        {
            RealLibrary(path);
        }
    }

    private static void Check(bool ok, string what) => _check(ok, "library search: " + what);

    private static AmTrack T(ulong pid, string title, string artist, string album) =>
        new() { Pid = pid, Title = title, Artist = artist, Album = album, AlbumArtist = artist, DurationMs = 200_000, Added = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(pid) };

    /// <summary>Six library songs and three playlist-only ones; "Mix A" (2 tracks) is smaller than "Big Mix" (3) and wins as the default context.</summary>
    private static AmDb Synthetic() => AmDb.FromTracks(
        [
            T(1, "Lumen", "Ari", "Lantern"),
            T(2, "Lumen Reloaded", "Ari", "Lantern"),
            T(3, "Lumen Remix", "Ari", "Remixes"),
            T(4, "Lumen Slowed", "Someone Else", "Slow"),
            T(5, "Lumen Acoustic", "Ari", "Unplugged"),
            T(6, "Other Song", "Ari", "Lantern"),
            T(11, "Lumen Extended", "Ari", "Lantern Deluxe"),
            T(12, "Moonlight", "Luna", "Night"),
            T(13, "Starfall", "Luna", "Night"),
        ],
        [1, 2, 3, 4, 5, 6],
        [
            (100, "Mix A", [11, 12]),
            (101, "Big Mix", [11, 12, 13]),
            (102, "Lumen Mix", [1, 11, 12]),
        ]);

    private static LibrarySearchModel Model(AmDb db, string text, LibScope scope = LibScope.All, int tab = 1, LibContainer? container = null)
    {
        var model = new LibrarySearchModel(db);
        model.Update(text, tab, scope, container);
        return model;
    }

    // ---------------------------------------------------------------- results

    private static void Grouping()
    {
        var db = Synthetic();
        var m = Model(db, "lumen");
        var kinds = m.Rows.Select(r => r.Kind).ToList();
        var headers = m.Rows.Where(r => r.Kind == LibRowKind.Header).ToList();
        Check(m.Searching && m.Query == "lumen" && m.Scope == LibScope.All, "grouping: a query searches everything, grouped");
        Check(headers.Count == 2 && headers[0].Target == LibScope.Songs && headers[1].Target == LibScope.Playlists, "grouping: a header for each group that has hits (Songs, Playlists)");
        Check(headers[0].Total == 6 && headers[0].Shown == 4, "grouping: the Songs group lists 4 of its 6 hits");
        Check(m.Rows.Count(r => r.Kind == LibRowKind.Song) == 4, "grouping: at most 4 songs in the All view");
        Check(kinds[0] == LibRowKind.Header && kinds[1] == LibRowKind.Song && kinds[5] == LibRowKind.Header && kinds[6] == LibRowKind.Playlist, "grouping: header, songs, header, playlist");
        Check(LibrarySearchModel.ShowAllOf(headers[0]) is { Kind: LibActionKind.ShowAll, Scope: LibScope.Songs }, "grouping: 'Show all' switches to the Songs chip");
        Check(headers[1].Total == headers[1].Shown, "grouping: a group that fits has nothing more to show");

        var heights = 2 * LibrarySearchModel.HeaderHeight + (5 * LibrarySearchModel.RowHeight);
        Check(m.Tops.Count == m.Rows.Count + 1 && Math.Abs(m.Tops[^1] - heights) < 0.001, "heights: header 24, row 44, prefix sums add up");
        Check(m.Rows.Select(r => r.Id).Distinct().Count() == m.Rows.Count, "grouping: row ids are unique");

        var none = Model(db, "zzzz");
        Check(none.Rows.Count == 0 && none.Highlight == -1 && none.SelectableCount == 0, "grouping: no hits = no rows, nothing highlighted");
        var punct = Model(db, "lumen, !");
        Check(punct.Rows.Count > 0, "grouping: punctuation in the query is folded away");
        Check(Model(db, " ").Searching == false, "grouping: a blank query is not a search");
    }

    private static void Scopes()
    {
        var db = Synthetic();
        var songs = Model(db, "lumen", LibScope.Songs);
        Check(songs.Rows.Count == 6 && songs.Rows.All(r => r.Kind == LibRowKind.Song), "scope: Songs lists all 6 matching songs, no headers");
        Check(songs.Rows.Where(r => r.Track!.InLibrary).Select(r => r.Track!.Pid).First() == 1, "scope: the exact title ranks first");

        var playlists = Model(db, "lumen", LibScope.Playlists);
        Check(playlists.Rows.Count == 1 && playlists.Rows[0].Playlist!.Name == "Lumen Mix", "scope: Playlists lists the matching playlist");

        var albums = Model(db, "lantern", LibScope.Albums);
        Check(albums.Rows.Count == 2 && albums.Rows.All(r => r.Kind == LibRowKind.Album), "scope: Albums finds Lantern and Lantern Deluxe");
        var artists = Model(db, "ari", LibScope.Artists);
        Check(artists.Rows.Count == 1 && artists.Rows[0].Artist!.Name == "Ari", "scope: Artists finds the artist");
        Check(artists.ScopeTotal(LibScope.Artists) == 1 && artists.ScopeTotal(LibScope.Albums) >= 2, "scope: the chips know each group's total");

        var all = Model(db, "luna");
        var group = all.Rows.Select(r => r.Kind).Where(k => k == LibRowKind.Header).Count();
        Check(group >= 2 && all.Rows.Any(r => r.Kind == LibRowKind.Artist) && all.Rows.Any(r => r.Kind == LibRowKind.Album), "scope: All shows songs, albums and artists together");

        var browse = Model(db, "", LibScope.Songs, tab: 1);
        Check(browse.Scope == LibScope.All && browse.Rows.Count == db.Songs.Count && !browse.Searching, "scope: without a query the scope is All and the browse list shows");
        Check(Model(db, "", tab: 0).Rows.Count == db.Playlists.Count && Model(db, "", tab: 0).Rows.All(r => r.Kind == LibRowKind.Playlist), "browse: the Playlists tab lists playlists");
        Check(Model(db, "", tab: 2).Rows.Count == db.Recent.Count, "browse: Recently added lists the recent songs");
        Check(Model(db, "", tab: 1).Highlight == -1, "browse: nothing is highlighted until an arrow key is pressed");
    }

    private static void Highlight()
    {
        var db = Synthetic();
        var m = Model(db, "lumen");
        Check(m.Highlight == 1 && m.Rows[m.Highlight].Kind == LibRowKind.Song, "highlight: a search highlights the top hit");
        Check(m.Move(1) && m.Highlight == 2, "highlight: Down moves one result");
        Check(m.Move(-1) && m.Highlight == 1, "highlight: Up moves back");
        Check(!m.Move(-1) && m.Highlight == 1, "highlight: Up at the top stays");
        m.Move(3);
        Check(m.Highlight == 4, "highlight: the 4th song");
        Check(m.Move(1) && m.Rows[m.Highlight].Kind == LibRowKind.Playlist && m.Highlight == 6, "highlight: Down skips the next section's header");
        Check(m.Move(-1) && m.Highlight == 4, "highlight: Up skips it back");
        Check(m.Move(LibrarySearchModel.PageStep * 5) && m.Highlight == 6 && !m.Move(1), "highlight: PageDown clamps at the last result");
        Check(m.Move(-LibrarySearchModel.PageStep * 5) && m.Highlight == 1, "highlight: PageUp clamps at the first result");
        Check(m.LeadRows(1) == 1 && m.LeadRows(2) == 0 && m.LeadRows(6) == 1, "highlight: the header above a section's first row scrolls into view with it");

        // a typed letter more keeps the highlight on the same song when it is still listed
        m.Move(1);
        var id = m.HighlightId;
        m.Update("lumen rel", 1, LibScope.All, null);
        Check(m.Rows.Count > 0 && m.HighlightId.Length > 0, "highlight: after more typing the top hit is highlighted");
        var big = Model(db, "lumen");
        big.Move(2);
        var kept = big.HighlightId;
        big.Update("lumen ", 1, LibScope.All, null);
        Check(big.HighlightId == kept, "highlight: it stays on the same song while that song is still listed");

        // a reloaded library: a new model seeded with the old highlight
        var reloaded = new LibrarySearchModel(Synthetic());
        reloaded.Keep(id);
        reloaded.Update("lumen", 1, LibScope.All, null);
        Check(reloaded.HighlightId == id, "highlight: kept across a library reload");

        var b = Model(db, "", tab: 1);
        Check(b.Move(1) && b.Highlight == 0 && b.Move(1) && b.Highlight == 1, "highlight: browsing starts at the top on the first Down");
        b.SetHighlight(3);
        Check(b.Highlight == 3, "highlight: a click highlights its row");
    }

    private static void Actions()
    {
        var db = Synthetic();
        var m = Model(db, "lumen");
        var library = m.Activate(false, false);
        Check(library is { Kind: LibActionKind.Play, Page: "songs" } && library.Track!.Pid == 1 && library.Context is null, "enter: a library song plays from the Songs page, no playlist");
        Check(m.Activate(true, false).Kind == LibActionKind.PlayNext && m.Activate(false, true).Kind == LibActionKind.AddToQueue, "enter: Shift = Play Next, Ctrl = Add to Queue");

        var only = Model(db, "extended").Activate(false, false);
        Check(only.Kind == LibActionKind.Play && only.Track!.Pid == 11 && only.Context?.Pid == 100, "enter: a playlist-only song plays in the smallest playlist that holds it");
        var via = Model(db, "extended").Rows.First(r => r.Kind == LibRowKind.Song);
        Check(via.Via == "Mix A", "enter: the row says 'in Mix A'");
        Check(Model(db, "lumen").Rows.Where(r => r.Kind == LibRowKind.Song && r.Track!.InLibrary).All(r => r.Via.Length == 0), "enter: library songs have no 'in' label");

        var album = Model(db, "lantern", LibScope.Albums).Activate(false, false);
        Check(album.Kind == LibActionKind.OpenAlbum && album.Album is not null, "enter: an album opens");
        var artist = Model(db, "ari", LibScope.Artists).Activate(true, false);
        Check(artist.Kind == LibActionKind.OpenArtist && artist.Artist!.Name == "Ari", "enter: an artist opens (Shift does not change it)");
        var playlist = Model(db, "lumen", LibScope.Playlists).Activate(false, false);
        Check(playlist.Kind == LibActionKind.OpenPlaylist && playlist.Playlist!.Pid == 102, "enter: a playlist opens");
        Check(Model(db, "zzzz").Activate(false, false).Kind == LibActionKind.None, "enter: nothing highlighted does nothing");

        var recent = Model(db, "", tab: 2);
        recent.Move(1);
        Check(recent.Activate(false, false) is { Kind: LibActionKind.Play, Page: "songs" }, "enter: a Recently added row plays from 'songs', not 'recent'");

        var track = db.ByPid(1)!;
        Check(m.AlbumOf(track)?.Name == "Lantern" && m.ArtistOf(track)?.Name == "Ari", "go to: the album and the artist of a song are found");
        Check(m.AlbumOf(T(99, "x", "y", "")) is null, "go to: a song without an album has none");
    }

    private static void ArtAndTabs()
    {
        // total lengths
        Check(LibStrings.Duration(0) == "" && LibStrings.Duration(-5) == "", "duration: none is empty");
        Check(LibStrings.Duration(52 * 60_000L) == "52 min", "duration: under an hour is minutes");
        Check(LibStrings.Duration((2 * 60 + 13) * 60_000L) == "2 hr 13 min", "duration: hours and minutes");
        Check(LibStrings.Duration(2 * 3_600_000L) == "2 hr", "duration: whole hours have no minutes");
        Check(LibStrings.Duration(20_000) == "1 min" && LibStrings.Duration(59 * 60_000L + 40_000) == "1 hr", "duration: rounded to the minute, at least one minute");
        Check(LibStrings.Summary(40, (2 * 60 + 13) * 60_000L) == "40 songs · 2 hr 13 min" && LibStrings.Summary(1, 0) == "1 song", "summary: count and length, count alone without a length");

        // mosaic pictures
        Check(AmArtwork.PickMosaic(["a", "b", "c", "d", "e"]).SequenceEqual(["a", "b", "c", "d"]), "mosaic: the first four pictures");
        Check(AmArtwork.PickMosaic(["a", null, "A", "b", "a", "c", "d"]).SequenceEqual(["a", "b", "c", "d"]), "mosaic: distinct (ignoring case), tracks without a picture skipped");
        Check(AmArtwork.PickMosaic(["a", "b", "a", "c"]).SequenceEqual(["a"]), "mosaic: three distinct pictures show the first one only");
        Check(AmArtwork.PickMosaic(["a", "a"]).SequenceEqual(["a"]) && AmArtwork.PickMosaic([null, null]).Count == 0 && AmArtwork.PickMosaic([]).Count == 0, "mosaic: one picture, or none");

        // repeat cycle
        Check(RepeatCycle.Next(RepeatKind.None) == RepeatKind.List && RepeatCycle.Next(RepeatKind.List) == RepeatKind.Track && RepeatCycle.Next(RepeatKind.Track) == RepeatKind.None, "repeat: none, list, track, none");
        Check(RepeatCycle.FromWinRt(Windows.Media.MediaPlaybackAutoRepeatMode.Track) == RepeatKind.Track && RepeatCycle.FromWinRt(null) is null, "repeat: session modes map both ways");

        // Albums and Artists browse tabs
        var db = Synthetic();
        var albums = Model(db, "", tab: 3);
        Check(albums.Rows.Count == db.Search.AllAlbums.Count && albums.Rows.Count > 0 && albums.Rows.All(r => r.Kind == LibRowKind.Album), "albums tab: every album, name order");
        Check(LibrarySearchModel.ActionFor(albums.Rows[0]).Kind == LibActionKind.OpenAlbum, "albums tab: a row opens the album");
        var artists = Model(db, "", tab: 4);
        Check(artists.Rows.Count == db.Search.AllArtists.Count && artists.Rows.Any(r => r.Artist!.Name == "Luna") && artists.Rows.All(r => r.Kind == LibRowKind.Artist), "artists tab: every artist");
        Check(LibrarySearchModel.ActionFor(artists.Rows[0]).Kind == LibActionKind.OpenArtist, "artists tab: a row opens the artist");
        Check(Model(db, "luna", tab: 3).Searching && Model(db, "luna", tab: 4).Rows.Any(r => r.Kind == LibRowKind.Song), "tabs: a search ignores the browse tab");

        // the song that plays -> its album and artist
        var playing = AmIdle.FindPlaying(db.AllTracks, "Moonlight", "Luna", "Night");
        Check(playing?.Pid == 12, "playing: matched by title, artist and album");
        Check(AmIdle.FindPlaying(db.AllTracks, "Moonlight", "Luna — Night", "")?.Pid == 12, "playing: the album folded into the artist is split off");
        Check(AmIdle.FindPlaying(db.AllTracks, "Nothing Like It", "Luna", "Night") is null && AmIdle.FindPlaying(db.AllTracks, " ", "Luna", "Night") is null, "playing: no match, blank title");
        Check(playing is not null && LibrarySearchModel.AlbumOf(db, playing)?.Name == "Night" && LibrarySearchModel.ArtistOf(db, playing)?.Name == "Luna", "playing: the album and artist to open");
    }

    private static void Containers()
    {
        var db = Synthetic();
        var playlist = db.Playlists.First(p => p.Pid == 102);
        var open = LibrarySearchModel.ForPlaylist(playlist);
        var all = Model(db, "", container: open);
        Check(all.Rows.Count == 3 && all.Rows.All(r => r.Context?.Pid == 102) && all.Highlight == -1, "playlist: lists its tracks in order, all play in it");
        var filtered = Model(db, "moon", container: open);
        Check(filtered.Rows.Count == 1 && filtered.Rows[0].Track!.Pid == 12 && filtered.Rows[0].Context?.Pid == 102 && filtered.Highlight == 0, "playlist: the box filters this playlist and the top hit is highlighted");
        Check(filtered.Rows[0].Via.Length == 0, "playlist: no 'in' label inside the playlist");
        Check(Model(db, "starfall", container: open).Rows.Count == 0, "playlist: a song of another playlist is not found inside this one");

        var search = Model(db, "lantern", LibScope.Albums);
        var album = LibrarySearchModel.ForAlbum(search.Rows.Select(r => r.Album!).First(a => a.Name == "Lantern"));
        var tracks = Model(db, "", container: album);
        Check(tracks.Rows.Select(r => r.Track!.Pid).OrderBy(p => p).SequenceEqual(new ulong[] { 1, 2, 6 }), "album: lists the album's tracks");
        Check(tracks.Rows.All(r => r.Context is null), "album: library songs play from the Songs page");
        var deluxe = LibrarySearchModel.ForAlbum(search.Rows.Select(r => r.Album!).First(a => a.Name == "Lantern Deluxe"));
        Check(Model(db, "", container: deluxe).Rows[0].Context?.Pid == 100 && Model(db, "", container: deluxe).Rows[0].Via == "Mix A", "album: a playlist-only track plays in its default playlist");
        Check(Model(db, "reloaded", container: album).Rows.Count == 1, "album: the box filters it");

        var rebound = album.Rebind(Synthetic());
        Check(rebound.Tracks.Count == 3 && !ReferenceEquals(rebound.Tracks[0], album.Tracks[0]), "album: reopened on a reloaded library, its tracks are found again");
        Check(album.Rebind(AmDb.FromTracks([T(1, "Lumen", "Ari", "Lantern")], null)).Tracks.Count == 1, "album: tracks that left the library are dropped");
    }

    // ---------------------------------------------------------------- Esc

    private static void EscapeOrder()
    {
        Check(LibrarySearchModel.Escape(true, true, true) == LibStep.Clear, "esc: text first (the box keeps the keyboard)");
        Check(LibrarySearchModel.Escape(true, false, false) == LibStep.Clear, "esc: text is cleared even when the box is not focused");
        Check(LibrarySearchModel.Escape(false, true, true) == LibStep.Blur, "esc: then the box lets go of the keyboard");
        Check(LibrarySearchModel.Escape(false, false, true) == LibStep.Back, "esc: then the open album / artist / playlist closes");
        Check(LibrarySearchModel.Escape(false, false, false) == LibStep.Close, "esc: then the Library closes");
    }

    // ---------------------------------------------------------------- focus hand-over

    private sealed class Token : IDisposable
    {
        public int Disposed { get; private set; }

        public void Dispose() => Disposed++;
    }

    private static void Unplayable()
    {
        var orphan = AmDb.FromTracks([T(1, "Lib", "A", "X"), T(2, "Catalogue", "A", "X")], [1], [(100, "Mix", [1])]);
        var inLibrary = orphan.ByPid(1)!;
        var loose = orphan.ByPid(2)!;
        Check(orphan.DefaultContext(loose) is null && !loose.InLibrary, "unplayable: a playlist-only song in no listed playlist has no context");
        Check(LibrarySearchModel.CanPlay(inLibrary, null) && !LibrarySearchModel.CanPlay(loose, null) && LibrarySearchModel.CanPlay(loose, orphan.Playlists[0]), "unplayable: only a library song or one with a playlist can be played");
        var m = Model(orphan, "catalogue");
        var row = m.Rows.First(r => r.Kind == LibRowKind.Song);
        Check(row.Track!.Pid == 2, "unplayable: the search still lists it");
        Check(m.Activate(false, false).Kind == LibActionKind.None && m.Activate(true, false).Kind == LibActionKind.None && m.Activate(false, true).Kind == LibActionKind.None, "unplayable: Enter, Shift+Enter and Ctrl+Enter do nothing for it");
        Check(Model(orphan, "lib").Activate(false, false).Kind == LibActionKind.Play, "unplayable: a library song next to it still plays");
    }

    private static void PagePositions()
    {
        var db = Synthetic();
        var (index, count) = db.PagePosition(db.ByPid(3)!, null);
        Check(count == db.Songs.Count && index >= 0 && db.Songs[index].Pid == 3, "page position: a library song's index on the Songs page");
        Check(db.PagePosition(db.ByPid(11)!, null) == (-1, 0), "page position: a playlist-only song is not on the Songs page");
        var mix = db.Playlists.First(p => p.Pid == 102);
        var inMix = db.PagePosition(db.ByPid(12)!, mix);
        Check(inMix == (2, 3), "page position: the index and count in a playlist");
        Check(db.PagePosition(db.ByPid(6)!, mix) == (-1, 0), "page position: a song the playlist does not hold");
    }

    private static void FocusCarry()
    {
        var carry = new LibraryFocusCarry();
        var hold = new Token();
        var serial = carry.Park(5, true, hold, 1000);
        Check(carry.Parked, "carry: parked");
        Check(carry.TryTake(1300, out var caret, out var took, out var taken) && caret == 5 && took && ReferenceEquals(taken, hold), "carry: a box that loads within a second takes the caret, the keyboard and the hold");
        Check(!carry.Parked && hold.Disposed == 0, "carry: taking it releases nothing");
        Check(!carry.TryExpire(serial, out _, out _) && hold.Disposed == 0, "carry: the half-second check finds it claimed and releases nothing");
        Check(!carry.TryTake(1400, out _, out _, out _), "carry: it can only be taken once");

        var late = new LibraryFocusCarry();
        var hold2 = new Token();
        var serial2 = late.Park(0, true, hold2, 5000);
        Check(late.TryExpire(serial2, out var tookKeyboard, out var expired) && tookKeyboard && ReferenceEquals(expired, hold2), "carry: unclaimed after the check it is handed back to be released");
        Check(!late.Parked && !late.TryTake(5100, out _, out _, out _), "carry: a released carry is gone");

        var stale = new LibraryFocusCarry();
        stale.Park(1, false, null, 100);
        Check(!stale.TryTake(100 + LibraryFocusCarry.TakeWithinMs, out _, out _, out _), "carry: after a second a new box does not take it");
        var fresh = new LibraryFocusCarry();
        fresh.Park(1, false, null, 100);
        Check(fresh.TryTake(100 + LibraryFocusCarry.TakeWithinMs - 1, out _, out _, out _), "carry: just inside the second it still does");

        var twice = new LibraryFocusCarry();
        var a = new Token();
        var b = new Token();
        var first = twice.Park(2, true, a, 0);
        var second = twice.Park(7, false, b, 100);
        Check(a.Disposed == 1 && !twice.TryExpire(first, out _, out _), "carry: a second park supersedes the first (its hold is released, its check does nothing)");
        Check(twice.TryTake(200, out var c, out var t, out var h) && c == 7 && t && ReferenceEquals(h, b), "carry: the keyboard ownership of the first park is kept");
        Check(second > first, "carry: every park has its own number");

        // the keyboard was lost while the carry was parked: nothing is resumed and the parked hold is released
        var lost = new LibraryFocusCarry();
        var held = new Token();
        lost.Park(4, true, held, 0);
        Check(!lost.TryTake(100, false, out _, out var tookLost, out var holdLost) && !tookLost && holdLost is null && held.Disposed == 1 && !lost.Parked, "carry: without the island's keyboard a new box does not continue it and the hold is released");
        var kept = new LibraryFocusCarry();
        var held2 = new Token();
        kept.Park(4, true, held2, 0);
        Check(kept.TryTake(100, true, out _, out _, out var holdKept) && ReferenceEquals(holdKept, held2) && held2.Disposed == 0, "carry: with the keyboard it continues as before");

        var request = new LibraryFocusCarry();
        request.RequestFocus(1000);
        Check(request.ConsumeFocusRequest(2500) && !request.ConsumeFocusRequest(2600), "request: the magnifier's request is used once");
        request.RequestFocus(1000);
        Check(!request.ConsumeFocusRequest(1000 + LibraryFocusCarry.RequestWithinMs), "request: a request from long ago is dropped");
        Check(!new LibraryFocusCarry().ConsumeFocusRequest(10), "request: none made, none used");
    }

    // ---------------------------------------------------------------- the list control (a real WPF layout pass, off screen)

    private static void VirtualListLayout()
    {
        string? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                // headers of 24 and rows of 44 in a 200 high viewport
                var tops = new List<double> { 0 };
                for (var i = 0; i < 400; i++)
                {
                    tops.Add(tops[^1] + (i % 5 == 0 ? 24 : 44));
                }

                var list = new VirtualList(tops, i => new System.Windows.Controls.Border { Height = i % 5 == 0 ? 24 : 44, Tag = i });
                var window = new System.Windows.Window
                {
                    Width = 300,
                    Height = 200,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    WindowStyle = System.Windows.WindowStyle.None,
                    Left = -20000,
                    Top = -20000,
                    Content = list,
                };
                window.Show();
                var dispatcher = window.Dispatcher;
                dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                if (list.LiveCount is < 3 or > 12 || !list.IsLive(0) || list.IsLive(100))
                {
                    failure = "top: " + list.LiveCount + " rows built";
                }

                list.Offset = tops[200];
                dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                if (list.LiveCount is < 3 or > 12 || !list.IsLive(200) || list.IsLive(0) || list.IsLive(100))
                {
                    failure = "middle: " + list.LiveCount + " rows built";
                }

                list.Offset = tops[^1];
                dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                if (!list.IsLive(399) || list.IsLive(200))
                {
                    failure = "end: " + list.LiveCount + " rows built";
                }

                window.Close();
            }
            catch (Exception ex)
            {
                failure = ex.GetType().Name + ": " + ex.Message;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Check(thread.Join(20_000) && failure is null, "list: only the rows in view are built, at their own heights (" + (failure ?? "ok") + ")");
    }

    // ---------------------------------------------------------------- the user's own library (a copy, read at run time only)

    private static void RealLibrary(string path)
    {
        var db = AmDb.Load(path);
        var songs = Model(db, "a", LibScope.Songs);
        Check(songs.Rows.Count == songs.Results.SongTotal && songs.Rows.Count > db.Songs.Count / 2, "real: the Songs scope lists every match, library songs and playlist-only ones");
        var only = songs.Rows.Where(r => !r.Track!.InLibrary).ToList();
        Check(only.Count > 0 && only.All(r => r.Context is not null && r.Via.Length > 0), "real: every playlist-only hit knows the playlist it plays in");
        Check(songs.Rows.Where(r => r.Track!.InLibrary).All(r => r.Context is null), "real: library songs play from the Songs page");
        var grouped = Model(db, "a");
        Check(grouped.Rows.Count(r => r.Kind == LibRowKind.Song) <= LibrarySearchModel.GroupCap, "real: the All view caps songs at 4");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 50; i++)
        {
            Model(db, i % 2 == 0 ? "lumen" : "an");
        }

        watch.Stop();
        Check(watch.ElapsedMilliseconds / 50.0 < 30, "real: building a result list takes well under 30 ms");
        Console.WriteLine($"info: library search model {watch.ElapsedMilliseconds / 50.0:0.0} ms per query on {db.TrackCount} tracks");
    }
}
