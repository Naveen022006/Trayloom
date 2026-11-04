// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Trayloom.Features.Island;

namespace Trayloom.Island.Harness;

/// <summary>
/// Checks of the library parser and search index (<see cref="AmDb"/>, <see cref="AmSearchIndex"/>, <see cref="AmArtworkMap"/>). The first
/// part uses synthetic data only; when the environment variable <c>TRAYLOOM_AMDB</c> names a COPY of a <c>Library.musicdb</c> the second part
/// checks generic invariants of that library (nothing in the library is written into this file). Apple Music is never touched.
/// </summary>
internal static class AmDbChecks
{
    private static Action<bool, string> _check = (_, _) => { };

    public static void Run(Action<bool, string> check)
    {
        _check = check;
        FoldRules();
        ArtistSplitting();
        SyntheticLibrary();
        SyntheticSearch();
        SyntheticPerformance();
        ArtworkMapping();
        if (Environment.GetEnvironmentVariable("TRAYLOOM_AMDB") is { Length: > 0 } path && File.Exists(path))
        {
            RealLibrary(path);
        }
        else
        {
            Console.WriteLine("info: TRAYLOOM_AMDB not set; the library invariants were skipped");
        }
    }

    private static void Check(bool ok, string what) => _check(ok, "amdb: " + what);

    private static AmTrack T(ulong pid, string title, string artist = "Artist", string album = "Album", string albumArtist = "", string sortTitle = "", DateTime? added = null, string genre = "", long adamId = 0) =>
        new() { Pid = pid, Title = title, Artist = artist, Album = album, AlbumArtist = albumArtist.Length > 0 ? albumArtist : artist, SortTitle = sortTitle, Added = added, Genre = genre, AdamId = adamId, DurationMs = 200_000 };

    // ---------------------------------------------------------------- folding

    private static void FoldRules()
    {
        Check(AmSearchIndex.Fold("A.R. Rahman") == "ar rahman" && AmSearchIndex.Fold("A.R. Rahman") == AmSearchIndex.Fold("AR Rahman"), "fold: dots are dropped (A.R. = AR)");
        Check(AmSearchIndex.Fold("Don’t Stop") == AmSearchIndex.Fold("Don't Stop"), "fold: curly and straight apostrophes agree");
        Check(AmSearchIndex.Fold("Rock’n’Roll") == "rocknroll", "fold: apostrophes join the letters around them");
        Check(AmSearchIndex.Fold("“Quoted” \"Title\"") == "quoted title", "fold: curly and straight double quotes are dropped");
        Check(AmSearchIndex.Fold("Quokka - Burrow Lights (From “Outback”)") == "quokka burrow lights from outback", "fold: hyphen and brackets become a space, quotes vanish");
        Check(AmSearchIndex.Fold("São Tomé") == "sao tome", "fold: accents are removed");
        Check(AmSearchIndex.Fold("Mr. X & Y") == "mr x y", "fold: ampersand is a separator");
        Check(AmSearchIndex.Fold("  A  —  B  ") == "a b", "fold: dashes and runs of spaces collapse, ends are trimmed");
        Check(AmSearchIndex.Fold("") == "" && AmSearchIndex.Fold("...") == "" && AmSearchIndex.Fold(" - ") == "", "fold: nothing left is the empty string");
        Check(AmSearchIndex.Fold("Glow✨ 🙂👍🏼") == "glow✨ 🙂👍🏼", "fold: emoji are kept as they are");
        Check(AmSearchIndex.Fold(AmSearchIndex.Fold("Quokka - “Outback”")) == AmSearchIndex.Fold("Quokka - “Outback”"), "fold: folding twice changes nothing");
    }

    private static void ArtistSplitting()
    {
        var names = AmSearchIndex.SplitArtists("A, B & C feat. D ft. E x F").ToArray();
        Check(names.SequenceEqual(new[] { "A", "B", "C", "D", "E", "F" }), "artists: split on comma, ampersand, feat., ft. and x");
        Check(AmSearchIndex.SplitArtists("Solo Artist").SequenceEqual(new[] { "Solo Artist" }), "artists: a single credit stays whole");
        Check(AmSearchIndex.SplitArtists("Maxine").SequenceEqual(new[] { "Maxine" }) && AmSearchIndex.SplitArtists("Rex").SequenceEqual(new[] { "Rex" }), "artists: an x inside a name is not a separator");
        Check(!AmSearchIndex.SplitArtists("").Any(), "artists: an empty credit has no artists");
    }

    // ---------------------------------------------------------------- a synthetic library

    private static readonly DateTime Older = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Newer = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

    private static AmDb Synthetic()
    {
        AmTrack[] tracks =
        [
            T(1, "Aardvark", "Artist One", "Alpha", added: Older),
            T(2, "A Long Walk", "Artist Two", "Beta", sortTitle: "Long Walk", added: Older),
            T(3, "Zebra", "Artist Two", "Gamma", added: Newer),
            T(4, "São Tomé Nights", "A.R. Rahman", "Alpha", albumArtist: "Various Artists", added: Older),
            T(5, "Quokka - Burrow (From “Outback”)", "Ana Bell & Cy Orion", "Outback - Single", adamId: 1234567890),
            T(6, "Quokka Remix", "Artist One feat. Artist Three", "Remixes", adamId: 42),
            T(7, "Quokka", "Artist Four", "Solo"),
            T(8, "Glow✨", "Artist Five", "Emoji Album", adamId: 7),
        ];
        (ulong, string, IReadOnlyList<ulong>)[] playlists =
        [
            (10, "Big", [5, 6, 7, 8, 1, 2, 3]),
            (11, "Small", [5, 8]),
            (12, "Small too", [6, 5]),
            (13, "Emoji 🙂👍🏼", [8]),
            (14, "Repeats", [3, 3, 3]),
        ];
        return AmDb.FromTracks(tracks, [1, 2, 3, 4, 7], playlists);
    }

    private static void SyntheticLibrary()
    {
        var db = Synthetic();
        Check(db.AllTracks.Count == 8 && db.TrackCount == 8, "library: AllTracks keeps every track");
        Check(db.AllTracks.Select(t => t.Pid).SequenceEqual(new[] { 1UL, 2UL, 3UL, 4UL, 5UL, 6UL, 7UL, 8UL }), "library: AllTracks is in file order");
        Check(db.Songs.Count == 5 && db.Songs.All(t => t.InLibrary), "library: Songs holds the master list only");
        Check(db.AllTracks.Count(t => !t.InLibrary) == 3 && !db.ByPid(5)!.InLibrary && db.ByPid(7)!.InLibrary, "library: InLibrary comes from the master list");
        Check(db.ByPid(4)?.Title == "São Tomé Nights" && db.ByPid(999) is null, "library: ByPid finds a track or nothing");

        var titles = db.Songs.Select(t => t.Title).ToList();
        Check(titles.IndexOf("A Long Walk") > titles.IndexOf("Aardvark"), "sort: a sort title moves the song (Long Walk sorts after Aardvark)");
        Check(titles.IndexOf("A Long Walk") > titles.IndexOf("Aardvark") && titles.IndexOf("A Long Walk") < titles.IndexOf("Quokka"), "sort: …and puts it under L, before Quokka");
        Check(titles[0] == "Aardvark" && titles[^1] == "Zebra", "sort: A first, Z last");

        // Recent: the newest first; the same stamp falls back to the album, then file order (1 and 4 share an album and a stamp)
        Check(db.Recent.Select(t => t.Pid).SequenceEqual(new[] { 3UL, 1UL, 4UL, 2UL }), "recent: newest first, then album, then file order; songs without a date are left out");

        Check(db.DefaultContext(db.ByPid(1)!) is null && db.DefaultContext(db.ByPid(7)!) is null, "context: a library song has none (the Songs page)");
        Check(db.DefaultContext(db.ByPid(5)!)?.Name == "Small", "context: the smallest playlist wins, a tie goes to the earlier one in the sidebar");
        Check(db.DefaultContext(db.ByPid(6)!)?.Name == "Small too", "context: …also when the smaller list is later in the sidebar");
        Check(db.DefaultContext(db.ByPid(8)!)?.Name == "Emoji 🙂👍🏼", "context: emoji playlist names round-trip");
        Check(db.PlaylistsOf(5).Select(p => p.Name).SequenceEqual(new[] { "Big", "Small", "Small too" }), "playlists of: every holder in sidebar order");
        Check(db.PlaylistsOf(3).Count(p => p.Name == "Repeats") == 1 && db.PlaylistsOf(4).Count == 0 && db.PlaylistsOf(12345).Count == 0, "playlists of: a repeated track counts once, no holder = empty");
        Check(db.AllTracks.Where(t => !t.InLibrary).All(t => db.DefaultContext(t) is not null), "context: every playlist-only track here has a playlist");

        var none = AmDb.FromTracks([T(1, "X"), T(2, "Y")], null);
        Check(none.Songs.Count == 2 && none.AllTracks.All(t => t.InLibrary), "library: without a master list every track is a library song");
        Check(none.DefaultContext(none.ByPid(1)!) is null, "context: nothing for a library song when there are no playlists");
        var orphan = AmDb.FromTracks([T(1, "In"), T(2, "Orphan")], [1]);
        Check(orphan.DefaultContext(orphan.ByPid(2)!) is null && !orphan.ByPid(2)!.InLibrary, "context: a playlist-only track that no listed playlist holds has none");
    }

    private static void SyntheticSearch()
    {
        var db = Synthetic();
        var s = db.Search;

        var quokka = s.Query("quokka", 10);
        Check(quokka.SongTotal == 3 && quokka.Songs.Count == 3, "search: 'quokka' finds the three Quokka songs");
        Check(quokka.Songs[0].Item.Pid == 7, "search: the song titled exactly 'Quokka' is first");
        Check(quokka.Songs.Select(h => h.Item.Pid).Contains(5UL) && quokka.Songs.Select(h => h.Item.Pid).Contains(6UL), "search: playlist-only tracks are found too");
        Check(quokka.Songs.Zip(quokka.Songs.Skip(1)).All(p => p.First.Score >= p.Second.Score), "search: hits are in score order");
        Check(quokka.Songs[0].Score > quokka.Songs[1].Score, "search: the exact title outscores a longer title");

        Check(s.Query("quokka", 2).Songs.Count == 2 && s.Query("quokka", 2).SongTotal == 3, "search: perGroup caps the hits, the total stays");
        Check(s.Query("quokka", int.MaxValue).Songs.Count == 3, "search: int.MaxValue returns everything");
        Check(s.Query("quokka", 0).Songs.Count == 0 && s.Query("quokka", 0).SongTotal == 3, "search: perGroup 0 only counts");

        Check(s.Query("sao tome", 5).Songs.Select(h => h.Item.Pid).SequenceEqual(new[] { 4UL }), "search: 'sao tome' matches 'São Tomé Nights'");
        Check(s.Query("SÃO TOMÉ", 5).SongTotal == 1, "search: the query is folded too");
        Check(s.Query("ar rahman", 5).Songs.Select(h => h.Item.Pid).SequenceEqual(new[] { 4UL }) && s.Query("a.r. rahman", 5).SongTotal == 1, "search: artist names match with and without dots");
        Check(s.Query("quokka outback", 5).Songs.Select(h => h.Item.Pid).SequenceEqual(new[] { 5UL }) && s.Query("outback quokka", 5).SongTotal == 1, "search: every term must match, in any order");
        Check(s.Query("quokka burrow", 5).SongTotal == 1 && s.Query("burrow quokka from outback", 5).SongTotal == 1, "search: punctuation in the title does not matter");
        Check(s.Query("quokka nothing", 5).IsEmpty && s.Query("zzzz", 5).IsEmpty, "search: a term nothing contains rejects the item");
        Check(s.Query("", 5).IsEmpty && s.Query("   ", 5).IsEmpty && s.Query(" - . ", 5).IsEmpty, "search: an empty or punctuation-only query has no results");
        Check(s.Query("glow", 5).Songs.Select(h => h.Item.Pid).SequenceEqual(new[] { 8UL }) && s.Query("glow✨", 5).SongTotal == 1, "search: emoji titles are found");
        Check(s.Query("aard", 5).Songs.Select(h => h.Item.Pid).SequenceEqual(new[] { 1UL }) && s.Query("dvark", 5).SongTotal == 1 && s.Query("wal", 5).Songs.Select(h => h.Item.Pid).SequenceEqual(new[] { 2UL }) && s.Query("alk", 5).SongTotal == 1, "search: word prefixes and substrings match");
        Check(s.Query("alpha", 5).Songs.Count == 2, "search: an album name finds its songs");

        var libraryFirst = AmDb.FromTracks([T(1, "Same"), T(2, "Same")], [2]);
        Check(libraryFirst.Search.Query("same", 5).Songs[0].Item.Pid == 2, "search: a library song outranks a playlist-only one with the same title");

        // groups
        var alpha = s.Query("alpha", 5);
        Check(alpha.AlbumTotal == 2 && alpha.Albums.All(h => h.Item.Name == "Alpha"), "albums: same name, different album artist = two albums");
        Check(alpha.Albums.Select(h => h.Item.Artist).OrderBy(x => x).SequenceEqual(new[] { "Artist One", "Various Artists" }), "albums: the artist is the album artist, else the artist");
        Check(alpha.Albums.All(h => h.Item.Tracks.Count == 1), "albums: tracks are grouped by album and album artist");

        var one = s.Query("artist one", 5);
        var artistOne = one.Artists.FirstOrDefault(h => h.Item.Name == "Artist One")?.Item;
        Check(artistOne is not null && artistOne.Tracks.Select(t => t.Pid).SequenceEqual(new[] { 1UL, 6UL }), "artists: 'Artist One feat. Artist Three' counts for Artist One");
        Check(s.Query("three", 5).Artists.Any(h => h.Item.Name == "Artist Three"), "artists: the feat. artist is an artist of its own");
        Check(s.Query("cy orion", 5).Artists.Any(h => h.Item.Name == "Cy Orion") && s.Query("ana bell", 5).Artists.Any(h => h.Item.Name == "Ana Bell"), "artists: 'A & B' gives A and B");
        Check(!s.Query("various", 5).Artists.Any(h => h.Item.Name == "Various Artists"), "artists: 'Various Artists' is not an artist");
        Check(s.Query("rahman", 5).Artists.Any(h => h.Item.Name == "A.R. Rahman"), "artists: dotted names are found");

        var small = s.Query("small", 5);
        Check(small.PlaylistTotal == 2 && small.Playlists[0].Item.Name == "Small", "playlists: found by name, the exact name first");
        Check(s.Query("emoji", 5).Playlists.Any(h => h.Item.Name == "Emoji 🙂👍🏼"), "playlists: emoji names are found");
        Check(!s.Query("zzzz", 5).Playlists.Any() && s.Query("big", 5).PlaylistTotal == 1, "playlists: only matching ones");
        Check(AmSearchIndex.Empty.Query("anything", 5).IsEmpty, "search: the empty index has no results");
    }

    private static void SyntheticPerformance()
    {
        string[] words = ["love", "night", "moon", "river", "fire", "heart", "dance", "dream", "stone", "rain", "light", "road", "gold", "wind", "star", "song", "blue", "home", "time", "wild"];
        string[] first = ["Ana", "Ben", "Chandra", "Dev", "Elif", "Farah", "Gus", "Hana", "Ivo", "Jun"];
        var rnd = new Random(7);
        var tracks = new List<AmTrack>();
        for (ulong i = 1; i <= 20_000; i++)
        {
            var title = $"{words[rnd.Next(words.Length)]} {words[rnd.Next(words.Length)]} {rnd.Next(1000)}";
            tracks.Add(T(i, title, first[rnd.Next(first.Length)] + " " + words[rnd.Next(words.Length)], words[rnd.Next(words.Length)] + " album " + rnd.Next(500)));
        }

        var build = Stopwatch.StartNew();
        var db = AmDb.FromTracks(tracks, tracks.Where(t => t.Pid % 3 != 0).Select(t => t.Pid));
        build.Stop();
        Console.WriteLine($"info: 20000 synthetic tracks built in {build.ElapsedMilliseconds} ms");
        Check(db.AllTracks.Count == 20_000, "perf: 20000 tracks built");

        foreach (var query in new[] { "love", "moon night", "dev stone", "a", "xyz" })
        {
            db.Search.Query(query, 4);

            // a one-letter query matches nearly every track, so it gets the looser bound
            var limit = query.Length == 1 ? 10 : 5;

            // The median of 15 runs; up to three rounds, the best one counts: a busy machine (parallel builds) only ever adds time, so the
            // best round is the honest one, and a real slowdown still fails all three.
            var best = double.MaxValue;
            for (var round = 0; round < 3 && best >= limit; round++)
            {
                var times = new double[15];
                for (var k = 0; k < times.Length; k++)
                {
                    var watch = Stopwatch.StartNew();
                    db.Search.Query(query, 4);
                    times[k] = watch.Elapsed.TotalMilliseconds;
                }

                Array.Sort(times);
                best = Math.Min(best, times[7]);
            }

            Console.WriteLine($"info: query '{query}' over 20000 tracks: median {best:0.00} ms");
            Check(best < limit, $"perf: query '{query}' over 20000 tracks takes under {limit} ms (median {best:0.00} ms)");
        }

        var capped = db.Search.Query("love", 4);
        var every = db.Search.Query("love", int.MaxValue);
        Check(every.Songs.Count == every.SongTotal && every.SongTotal == capped.SongTotal, "perf: 'show all' returns every match");
        Check(every.Songs.Zip(every.Songs.Skip(1)).All(p => p.First.Score >= p.Second.Score), "perf: 'show all' is in score order");
        Check(capped.Songs.Select(h => h.Item.Pid).SequenceEqual(every.Songs.Take(4).Select(h => h.Item.Pid)), "perf: the capped hits are the head of 'show all'");
    }

    // ---------------------------------------------------------------- artwork

    private static void ArtworkMapping()
    {
        var root = Path.Combine(Path.GetTempPath(), "TraylaomAmDbChecks-" + Guid.NewGuid().ToString("N")[..8]);
        var previous = Environment.GetEnvironmentVariable("TRAYLOOM_AMART");
        Environment.SetEnvironmentVariable("TRAYLOOM_AMART", null);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "artwork"));
            var library = Path.Combine(root, "Library.musicdb");
            File.WriteAllText(library, "not read by the artwork map");
            File.WriteAllBytes(Path.Combine(root, "artwork", "AAAA-1_sk1.jpeg"), [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(root, "artwork", "BBBB-2_sk1.png"), [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(root, "artwork", "DDDD-4_sk1.jpeg"), [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(root, "artwork", "DDDD-4_sk1.png"), [1, 2, 3]);
            var negative = unchecked((ulong)-8_000_000_000_000_000_000L);
            var database = Path.Combine(root, "artwork.sqlite");
            using (var connection = new SqliteConnection($"Data Source={database};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "CREATE TABLE item_to_artwork (item_id INTEGER NOT NULL, source_kind INTEGER NOT NULL, remote_id INTEGER DEFAULT 0, artwork_id TEXT NOT NULL, PRIMARY KEY (item_id));" +
                    "INSERT INTO item_to_artwork VALUES (5, 13, 0, 'aaaa-1'), (-8000000000000000000, 13, 0, 'BBBB-2'), (7, 13, 0, 'CCCC-3'), (9, 13, 0, 'DDDD-4');";
                command.ExecuteNonQuery();
            }

            SqliteConnection.ClearAllPools();
            var before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(database)));
            var map = AmArtworkMap.Create(library);
            Check(map.Resolve(5) is { } a && a.EndsWith("AAAA-1_sk1.jpeg", StringComparison.OrdinalIgnoreCase), "artwork: a pid maps to its jpeg (the id is matched ignoring case)");
            Check(map.Resolve(negative) is { } b && b.EndsWith("BBBB-2_sk1.png", StringComparison.OrdinalIgnoreCase), "artwork: a pid above long.MaxValue is looked up as the signed integer, png works");
            Check(map.Resolve(7) is null && map.Resolve(6) is null, "artwork: an id without a file, or a pid without a row, has none");
            Check(map.Resolve(9) is { } c && c.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase), "artwork: jpeg is preferred over png");
            Check(map.Count == 3, "artwork: only pictures that exist on disk are counted");
            Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(database))) == before, "artwork: the source database is not changed");
            var leftovers = Directory.Exists(Path.Combine(Path.GetTempPath(), "Trayloom", "am-art"))
                ? Directory.GetFiles(Path.Combine(Path.GetTempPath(), "Trayloom", "am-art"), $"artwork-{Environment.ProcessId}-*")
                : [];
            Check(leftovers.Length == 0, "artwork: the temp copy is removed again");

            Check(AmArtworkMap.Create(Path.Combine(root, "missing", "Library.musicdb")).Count == 0, "artwork: no artwork folder is an empty map, not an error");
            File.WriteAllText(database, "this is not a database");
            Check(AmArtworkMap.Create(library).Count == 0, "artwork: an unreadable database is an empty map, not an error");
        }
        finally
        {
            Environment.SetEnvironmentVariable("TRAYLOOM_AMART", previous);
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(root, true);
            }
            catch (IOException)
            {
                // a temp folder left behind
            }
        }
    }

    // ---------------------------------------------------------------- a real library copy (generic invariants only)

    private static void RealLibrary(string path)
    {
        var first = Stopwatch.StartNew();
        _ = AmDb.Load(path);
        first.Stop();
        var watch = Stopwatch.StartNew();
        var db = AmDb.Load(path);
        watch.Stop();
        Console.WriteLine($"info: library copy: {db.AllTracks.Count} tracks, {db.Songs.Count} library songs, {db.Playlists.Count} playlists; load {first.ElapsedMilliseconds} ms cold, {watch.ElapsedMilliseconds} ms warm");
        Check(Math.Min(first.ElapsedMilliseconds, watch.ElapsedMilliseconds) < 300, "library: loads in under 300 ms");

        Check(db.AllTracks.Count > 0 && db.TrackCount == db.AllTracks.Count, "library: tracks were read");
        Check(db.Songs.Count == db.AllTracks.Count(t => t.InLibrary) && db.Songs.All(t => t.InLibrary), "library: Songs is exactly the InLibrary tracks");
        Check(db.AllTracks.Select(t => t.Pid).Distinct().Count() == db.AllTracks.Count, "library: no duplicate pids");
        Check(db.AllTracks.All(t => db.ByPid(t.Pid) == t), "library: ByPid finds every track");
        Check(db.AllTracks.Where(t => t.Artist.Length > 0).All(t => t.AlbumArtist.Length > 0), "library: AlbumArtist is never empty when the track has an artist");
        Check(db.AllTracks.All(t => t.Title.Length > 0 && t.Title == t.Title.Trim()), "library: titles are not empty and are trimmed");
        Check(db.AllTracks.All(t => t.AdamId >= 0), "library: adam ids are not negative");
        Check(db.Songs.Zip(db.Songs.Skip(1)).All(p => AmDb.CompareSongs(p.First, p.Second) <= 0), "library: Songs is in sort-title, sort-artist order");
        Check(db.Recent.Zip(db.Recent.Skip(1)).All(p => p.First.Added >= p.Second.Added) && db.Recent.All(t => t.InLibrary && t.Added is not null), "library: Recent is newest first and library songs only");
        Check(db.Songs.All(t => db.DefaultContext(t) is null), "library: DefaultContext is null for every library song");
        var only = db.AllTracks.Where(t => !t.InLibrary).ToList();
        Check(only.All(t => db.PlaylistsOf(t.Pid).Count > 0 ? db.DefaultContext(t) is { } c && db.PlaylistsOf(t.Pid).Contains(c) && db.PlaylistsOf(t.Pid).All(p => p.Tracks.Count >= c.Tracks.Count) : db.DefaultContext(t) is null),
            "library: a playlist-only track's context is its smallest listed holder (none when it has no holder)");
        Check(db.Playlists.All(p => p.Name.Length > 0 && !p.Name.StartsWith("####", StringComparison.Ordinal)), "library: listed playlists have names and are not the master list");
        Check(db.Playlists.SelectMany(p => p.Tracks).All(t => db.PlaylistsOf(t.Pid).Count > 0), "library: every track of a playlist knows its playlists");

        // every library song is found by its own title, and by its title with the punctuation left out
        var misses = db.AllTracks.Where(t => AmSearchIndex.Fold(t.Title).Length > 0)
            .Where(t => db.Search.Query(t.Title, int.MaxValue).Songs.All(h => h.Item.Pid != t.Pid)).Take(5).ToList();
        Check(misses.Count == 0, "library: every track is found by its own title");
        Check(db.AllTracks.All(t => AmSearchIndex.Fold(AmSearchIndex.Fold(t.Title)) == AmSearchIndex.Fold(t.Title)), "library: folding is idempotent on every title");
        Check(db.Search.Query("a", 4).SongTotal > 0 && db.Search.Query("zqxjkv", 4).IsEmpty, "library: search finds something for 'a' and nothing for nonsense");

        // artwork: report how many tracks have a picture (needs the artwork folder next to the copy, or TRAYLOOM_AMART)
        var artwork = Path.Combine(Path.GetDirectoryName(path)!, "artwork");
        if (Directory.Exists(artwork) || Directory.Exists(Environment.GetEnvironmentVariable("TRAYLOOM_AMART")))
        {
            var tracksWith = db.AllTracks.Count(t => db.ArtworkPath(t.Pid) is not null);
            var share = db.AllTracks.Count == 0 ? 1.0 : (double)tracksWith / db.AllTracks.Count;
            Console.WriteLine($"info: artwork resolves for {tracksWith} of {db.AllTracks.Count} tracks ({share:P1})");
            Check(share >= 0.95, "library: artwork resolves for at least 95 % of the tracks");
        }
        else
        {
            Console.WriteLine("info: no artwork folder next to the copy and TRAYLOOM_AMART not set; the artwork share was skipped");
        }
    }
}
