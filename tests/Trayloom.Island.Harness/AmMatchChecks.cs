// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Features.Island;

namespace Trayloom.Island.Harness;

/// <summary>
/// Checks of the Apple Music driver's pure matching rules (<see cref="AmMatch"/>): which list row is the song, which button is "More",
/// which menu item is invoked and whether the song that started is the one that was asked for. Synthetic songs only (generic common
/// titles); Apple Music is never touched.
/// </summary>
internal static class AmMatchChecks
{
    private static Action<bool, string> _check = (_, _) => { };

    public static void Run(Action<bool, string> check)
    {
        _check = check;
        RowChoice();
        RowRanking();
        TitleCell();
        RealRowNames();
        NamesAndCharacters();
        MoreButton();
        MenuChoice();
        MenuSafety();
        Confirmation();
        MediaArtist();
    }

    private static void Check(bool ok, string what) => _check(ok, "match: " + what);

    private static AmTrack Song(string title, string artist = "Artist", string album = "Album", int ms = 200_000) =>
        new() { Pid = 1, Title = title, Artist = artist, Album = album, DurationMs = ms };

    /// <summary>A row as Apple Music shows it: the cells' texts, and an accessible name made of them ("Title m:ss Artist Album").</summary>
    private static (string Name, string[] Texts) Row(string title, string artist = "Artist", string album = "Album", string time = "3:20") =>
        ($"{title} {time} {artist} {album}", [title, time, artist, album]);

    private static bool Exact(AmTrack track, (string Name, string[] Texts) row) => AmMatch.RowScore(row.Name, row.Texts, track).TitleExact;

    /// <summary>The titles (of the given rows) that qualify for <paramref name="track"/>.</summary>
    private static string[] Qualifying(AmTrack track, params (string Name, string[] Texts)[] rows) =>
        rows.Where(r => Exact(track, r)).Select(r => r.Texts[0]).ToArray();

    private static void RowChoice()
    {
        var plain = Row("Paper Moon");
        var film = Row("Paper Moon (From \"Ember\")");
        Check(Qualifying(Song("Paper Moon"), film, plain).SequenceEqual(["Paper Moon"]), "a title that is contained in a longer one picks only the exact row");
        Check(Qualifying(Song("Paper Moon (From \"Ember\")"), plain, film).SequenceEqual(["Paper Moon (From \"Ember\")"]), "the longer title picks only its own row");
        Check(Qualifying(Song("Paper Moon"), film).Length == 0, "only a longer version on the page: no row qualifies (never played in its place)");
        Check(AmMatch.NameMayHoldTitle(film.Name, Song("Paper Moon")), "the longer row is read (its name holds the title) but then rejected");

        var versions = new[] { Row("Willow"), Row("Willow (Sped Up)", "Other"), Row("Willow - Slowed Down", "Other"), Row("Willow (Acoustic)") };
        foreach (var title in new[] { "Willow", "Willow (Sped Up)", "Willow - Slowed Down", "Willow (Acoustic)" })
        {
            var hits = Qualifying(Song(title, "Artist"), versions);
            Check(hits.Length == 1 && hits[0] == title, $"four versions: '{title}' picks exactly itself");
        }

        Check(Qualifying(Song("Only You"), Row("Only You II"), Row("Only You")).SequenceEqual(["Only You"]), "'Only You' is not 'Only You II'");
        Check(Qualifying(Song("Only You II"), Row("Only You"), Row("Only You II")).SequenceEqual(["Only You II"]), "'Only You II' is not 'Only You'");
        Check(Qualifying(Song("Cascade"), Row("Cascade - Extended")).Length == 0, "'Cascade' is not 'Cascade - Extended'");
        Check(Qualifying(Song("Tide"), Row("Tide (From \"Harbor\")")).Length == 0, "'Tide' is not 'Tide (From \"Harbor\")'");
        Check(Qualifying(Song("Intro"), Row("Introduction")).Length == 0, "a longer word is not the title");
    }

    private static void TitleCell()
    {
        // a song "Willow" on an album "Willow": the longer version's row has the title in its album cell, which must not count
        var plain = Song("Willow", "Artist", "Other Album");
        AmCell[] sped = [new("Willow (Sped Up)", true), new("Artist", true), new("Willow", true), new("3:20", true)];
        Check(!AmMatch.ScoreCells("Willow (Sped Up) 3:20 Artist Willow", sped, plain).TitleExact, "title cell: the album cell reading like the title does not make a longer title the song");
        AmCell[] artistCell = [new("Willow (Acoustic)", true), new("Willow", true), new("Album", true)];
        Check(!AmMatch.ScoreCells("Willow (Acoustic), Willow, Album", artistCell, plain).TitleExact, "title cell: nor does an artist cell");
        AmCell[] own = [new("Willow", true), new("Artist", true), new("Willow", true), new("3:20", true)];
        var selfTitled = Song("Willow", "Artist", "Willow", 200_000);
        var score = AmMatch.ScoreCells("Willow 3:20 Artist Willow", own, selfTitled);
        Check(score.TitleExact && score.Album == 2 && score.Artist == 2 && score.Duration, "title cell: a song on a self-titled album still matches and scores artist, album and time");
        AmCell[] withButton = [new("More", false), new("Play", false), new("Willow", true), new("Artist", true)];
        Check(AmMatch.ScoreCells("x", withButton, plain).TitleExact, "title cell: buttons and images before the first text are skipped");
        AmCell[] onlyButtons = [new("Willow", false), new("Artist", false)];
        Check(!AmMatch.ScoreCells("Willow (Sped Up), Artist", onlyButtons, plain).TitleExact, "title cell: no Text cell at all falls back to the row name (a longer title does not qualify)");
        Check(AmMatch.ScoreCells("Willow, Artist, Album", onlyButtons, plain).TitleExact, "title cell: no Text cell at all falls back to the row name (title then artist qualifies)");
        AmCell[] blank = [new(" ", true)];
        Check(AmMatch.ScoreCells("Willow", blank, plain).TitleExact, "title cell: blank texts count as no text");

        // a recycled row (the list scrolled and the element now shows another song) is not the song any more
        var moved = Row("Another Tune", "Someone", "Elsewhere");
        Check(!Exact(plain, moved), "row moved: a row that now shows another song does not qualify");
    }

    /// <summary>Rows named the way Apple Music names them: "Title m:ss Artist Album Genre Plays"; the text before the duration decides.</summary>
    private static void RealRowNames()
    {
        var song = Song("Sun And Moon", "Ari Vance, Sol Rivers & Kay", "Ember (Original Soundtrack)", 217_000);
        const string name = "Sun And Moon 3:37 Ari Vance, Sol Rivers & Kay Ember (Original Soundtrack) Tamil 30";
        AmCell[] otherFirst = [new("Ari Vance, Sol Rivers & Kay", true), new("Sun And Moon", false), new("3:37", true)];
        var real = AmMatch.ScoreCells(name, otherFirst, song);
        Check(real.TitleExact && real.Artist >= 1 && real.Duration, "real row: the title before the duration matches even when the first text cell is another column");
        Check(AmMatch.ScoreCells(name, [], song).TitleExact, "real row: no readable cells, the name alone decides");
        Check(real.IsConclusive(song), "real row: artist and album from the name end the search");

        var plain = Song("Willow", "Artist", "Other Album");
        AmCell[] albumFirst = [new("Willow", true), new("Willow (Sped Up)", true)];
        Check(!AmMatch.ScoreCells("Willow (Sped Up) 3:20 Artist Willow", albumFirst, plain).TitleExact, "real row: a longer title before the duration never qualifies, whatever the cells say");
        Check(AmMatch.ScoreCells("Willow Explicit 3:20 Artist Other Album", [], plain).TitleExact, "real row: an Explicit mark after the title is allowed");
        Check(AmMatch.ScoreCells("3 Willow 3:20 Artist Other Album", [new("3", true), new("Willow", true)], plain).TitleExact, "playlist row: a leading track number is taken off when a cell is the title");
        Check(!AmMatch.ScoreCells("7 Willow 3:20 Artist Other Album", [new("7 Willow", true)], plain).TitleExact, "playlist row: a number that is part of the title is not taken off ('7 Rings' is not 'Rings')");
        Check(AmMatch.ScoreCells("9:30 Train 4:00 Artist Album", [], Song("9:30 Train")).TitleExact, "real row: a title that holds a time still matches");
        Check(!AmMatch.ScoreCells("9:30 Train 4:00 Artist Album", [], Song("Train")).TitleExact, "real row: nor is its tail another song");
    }

    private static void RowRanking()
    {
        var first = Row("Oh Marina", "Artist One", "Album One");
        var second = Row("Oh Marina", "Artist Two", "Album Two");
        var asOne = Song("Oh Marina", "Artist One", "Album One");
        var asTwo = Song("Oh Marina", "Artist Two", "Album Two");
        var a1 = AmMatch.RowScore(first.Name, first.Texts, asOne);
        var a2 = AmMatch.RowScore(second.Name, second.Texts, asOne);
        Check(a1.Rank > a2.Rank && a1.IsConclusive(asOne) && !a2.IsConclusive(asOne), "two artists: the matching artist ranks higher and ends the search");
        var b1 = AmMatch.RowScore(first.Name, first.Texts, asTwo);
        var b2 = AmMatch.RowScore(second.Name, second.Texts, asTwo);
        Check(b2.Rank > b1.Rank && b2.IsConclusive(asTwo), "two artists: the other song picks the other row");
        Check(a2.TitleExact && a2.Rank >= 0, "two artists: the row with the wrong artist still qualifies by title (best effort after a full pass)");

        var sameArtist = Song("Oh Marina", "Artist One", "Album Two");
        Check(AmMatch.RowScore(first.Name, first.Texts, sameArtist).Rank < AmMatch.RowScore(second.Name, [.. second.Texts.Take(1), "Artist One", "Album Two"], sameArtist).Rank, "same title and artist: the album decides");

        var long1 = Row("Oh Marina", "Artist One", "Album One", "3:20");
        var long2 = Row("Oh Marina", "Artist One", "Album One", "4:41");
        var timed = Song("Oh Marina", "Artist One", "Album One", 201_000);
        Check(AmMatch.RowScore(long1.Name, long1.Texts, timed).Rank > AmMatch.RowScore(long2.Name, long2.Texts, timed).Rank, "identical rows: the duration decides");
        Check(AmMatch.TryParseClock("3:20", out var s) && Math.Abs(s - 200) < 0.01 && AmMatch.TryParseClock("1:02:03", out var h) && Math.Abs(h - 3723) < 0.01 && !AmMatch.TryParseClock("Album", out _) && !AmMatch.TryParseClock("12:3", out _), "durations read as m:ss and h:mm:ss only");

        var multi = Song("Together", "Alpha & Beta", "Album");
        var row = Row("Together", "Alpha, Beta");
        Check(AmMatch.RowScore(row.Name, row.Texts, multi).Artist >= 1, "a credit written differently still counts as the artist");
    }

    private static void NamesAndCharacters()
    {
        Check(Qualifying(Song("Don't Stop"), Row("Don’t Stop")).Length == 1, "curly apostrophe in the row, straight in the library");
        Check(Qualifying(Song("Don’t Stop"), Row("Don't Stop")).Length == 1, "straight apostrophe in the row, curly in the library");
        Check(Qualifying(Song("Say \"Yes\""), Row("Say “Yes”")).Length == 1, "curly double quotes against straight ones");
        Check(Qualifying(Song("Cafe"), Row("Café")).Length == 1, "accents do not matter");
        Check(Qualifying(Song("\U0001F525 Fire"), Row("\U0001F525 Fire"), Row("Fire")).SequenceEqual(["\U0001F525 Fire"]), "emoji title picks the emoji row only");
        Check(Qualifying(Song("Fire"), Row("\U0001F525 Fire")).Length == 0, "a plain title is not the emoji title");
        Check(Qualifying(Song("A.R. Song"), Row("AR Song")).Length == 1, "dots are ignored");
        Check(Qualifying(Song("???"), Row("???")).Length == 1 && Qualifying(Song("???"), Row("!!!")).Length == 0, "a title of punctuation only is compared as it is");

        // a row whose texts could not be read is judged by its name: the title alone or title then artist, nothing longer
        var plain = Song("Hello", "Singer");
        Check(AmMatch.RowScore("Hello Singer Album", [], plain).TitleExact, "no readable text: title then artist in the name qualifies");
        Check(!AmMatch.RowScore("Hello Again Singer Album", [], plain).TitleExact, "no readable text: a longer title does not");
        Check(AmMatch.RowScore("Hello", [], plain).TitleExact, "no readable text: the title alone qualifies");
    }

    private static void MoreButton()
    {
        Check(AmMatch.IsMoreButton("More", ""), "a button named More");
        Check(AmMatch.IsMoreButton("", "RowMoreButton"), "an id that contains More");
        Check(!AmMatch.IsMoreButton("Add to Library", "AddButton") && !AmMatch.IsMoreButton("Favourite", "FavoriteButton") && !AmMatch.IsMoreButton("Play", "PlayButton"), "other buttons are not More");
        Check(!AmMatch.IsMoreButton("more options", "") && !AmMatch.IsMoreButton("Moreover", ""), "only the exact name counts");
        (string, string)[] buttons = [("Add to Library", "AddButton"), ("Love", "LoveButton")];
        Check(AmMatch.PickMore(buttons) == -1, "no More among the buttons: nothing is chosen (never the last button)");
        (string, string)[] withMore = [("Favourite", "Fav"), ("", "ItemMoreButton"), ("More", "x")];
        Check(AmMatch.PickMore(withMore) == 2, "a button named More comes before one found by id");
        Check(AmMatch.PickMore([("Favourite", "Fav"), ("", "ItemMoreButton")]) == 1, "an id match is accepted when no button is named More");
        Check(AmMatch.PickMore([]) == -1, "no buttons: nothing is chosen");
    }

    private static void MenuChoice()
    {
        const string title = "Lumen - Northern Lights (From \"Lantern\")";
        string[] menu = ["Play “Lumen - Northern Lights (From \"Lantern\")”", "Play Next", "Play Last", "Add to Library", "Share"];
        Check(AmMatch.PickMenuItem(AmAction.Play, menu, title) == 0, "Play: the item with the full quoted title");
        Check(AmMatch.PickMenuItem(AmAction.PlayNext, menu, title) == 1, "Play Next: the item named Play Next");
        Check(AmMatch.PickMenuItem(AmAction.AddToQueue, menu, title) == 2, "Add to Queue: 'Play Last' is accepted");
        Check(AmMatch.PickMenuItem(AmAction.AddToQueue, ["Play Next", "Add to Queue", "Play Last"], title) == 1, "Add to Queue: the first of the accepted names");
        Check(AmMatch.PickMenuItem(AmAction.AddToQueue, ["Play Next", "Play Later"], title) == 1, "Add to Queue: 'Play Later' is accepted");

        // the several versions of one title: the menu of a row holds its own title only
        string[] willow = ["Play “Willow (Sped Up)”", "Play Next", "Play Last"];
        Check(AmMatch.PickMenuItem(AmAction.Play, willow, "Willow") == 0 || AmMatch.IsPlayItem(willow[0], "Willow (Sped Up)"), "a quoted longer title is only accepted as the one 'Play …' item");
        Check(!AmMatch.IsPlayItem(willow[0], "Willow"), "IsPlayItem: a longer title is not the item");
        Check(AmMatch.IsPlayItem(willow[0], "Willow (Sped Up)"), "IsPlayItem: the full title is");
        Check(AmMatch.IsPlayItem("Play “Don’t Stop”", "Don't Stop"), "IsPlayItem: curly against straight apostrophes");
        Check(AmMatch.PickMenuItem(AmAction.Play, ["Play “Willow”", "Play “Willow (Sped Up)”"], "Willow (Sped Up)") == 1, "two Play items: the exact one");
        Check(AmMatch.PickMenuItem(AmAction.Play, ["Play “Willow (A)”", "Play “Willow (B)”"], "Willow") == -1, "two Play items that only contain the title: none (ambiguous)");
        Check(AmMatch.PickMenuItem(AmAction.Play, ["Play Next", "Play Last"], "Next") == -1, "a song titled 'Next' does not make 'Play Next' its Play item");
        Check(AmMatch.PickMenuItem(AmAction.Play, ["Play “Next”", "Play Next"], "Next") == 0, "…but its quoted item is chosen");
        Check(AmMatch.PickMenuItem(AmAction.Play, [], title) == -1 && AmMatch.PickMenuItem(AmAction.PlayNext, [], title) == -1, "an empty menu chooses nothing");
    }

    private static void MenuSafety()
    {
        // No positional fallback and nothing that edits the library, whatever the language or layout of the menu
        string[] odd = ["Add to Library", "Add to Playlist", "Favourite", "Share", "Go to Album"];
        foreach (var action in new[] { AmAction.Play, AmAction.PlayNext, AmAction.AddToQueue })
        {
            Check(AmMatch.PickMenuItem(action, odd, "Some Song") == -1, $"{action}: a menu without the exact items chooses nothing");
        }

        string[] localised = ["Jouer « Some Song »", "Lire ensuite", "Ajouter à la bibliothèque"];
        foreach (var action in new[] { AmAction.Play, AmAction.PlayNext, AmAction.AddToQueue })
        {
            Check(AmMatch.PickMenuItem(action, localised, "Some Song") == -1, $"{action}: another language is not guessed by position");
        }

        string[] close = ["Play Next Song", "Playlist", "Add to Queue Later", "Play Last One"];
        Check(AmMatch.PickMenuItem(AmAction.PlayNext, close, "X") == -1 && AmMatch.PickMenuItem(AmAction.AddToQueue, close, "X") == -1, "names that only start like the wanted ones are not accepted");
        Check(AmMatch.PickMenuItem(AmAction.Play, ["Playlist", "Play Queue"], "Queue") == -1 || AmMatch.PickMenuItem(AmAction.Play, ["Playlist"], "Queue") == -1, "'Playlist' is not a play item");
    }

    private static void Confirmation()
    {
        var wanted = Song("Paper Moon", "Ari", "Album");
        Check(AmMatch.CheckPlaying("Paper Moon", "Ari — Album", wanted) is { TitleOk: true, ArtistOk: true, AlbumOk: true }, "the same song, artist and album confirm");
        Check(!AmMatch.CheckPlaying("Paper Moon (From \"Ember\")", "Ari — Album", wanted).TitleOk, "a longer title is the wrong song");
        Check(!AmMatch.CheckPlaying("Paper", "Ari", wanted).TitleOk, "a shorter title is the wrong song");
        Check(AmMatch.CheckPlaying("Don’t Stop", "x", Song("Don't Stop")).TitleOk, "curly against straight apostrophes confirm");
        Check(AmMatch.CheckPlaying("PAPER MOON", "", wanted).TitleOk, "case does not matter");
        var other = AmMatch.CheckPlaying("Paper Moon", "Somebody Else — Unrelated Record", wanted);
        Check(other.TitleOk && !other.ArtistOk && !other.AlbumOk, "title right, artist and album wrong: still confirmed (the driver warns)");
        Check(!AmMatch.CheckPlaying("", "Ari", wanted).TitleOk, "no title never confirms");
        Check(AmMatch.CheckPlaying("Together", "Alpha", Song("Together", "Alpha & Beta")).ArtistOk, "one of several credits is enough");
        Check(AmMatch.CheckPlaying("\U0001F525 Fire", "x", Song("\U0001F525 Fire")).TitleOk && !AmMatch.CheckPlaying("Fire", "x", Song("\U0001F525 Fire")).TitleOk, "emoji titles are compared whole");
    }

    private static void MediaArtist()
    {
        Check(AmMatch.SplitMediaArtist("Ari Vance & Sol Rivers — Lumen - Northern Lights (From \"Lantern\") - Single") is ("Ari Vance & Sol Rivers", "Lumen - Northern Lights (From \"Lantern\") - Single"), "GSMTC artist with the album after an em dash");
        Check(AmMatch.SplitMediaArtist("Plain Artist") is ("Plain Artist", ""), "an artist alone");
        Check(AmMatch.SplitMediaArtist("") is ("", ""), "empty");
        Check(AmMatch.SplitMediaArtist("A – B") is ("A", "B"), "an en dash also splits");
        Check(AmMatch.SplitMediaArtist("Artist - Hyphen") is ("Artist - Hyphen", ""), "a hyphen does not split");
    }
}
