// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;

namespace Trayloom.Features.Island;

/// <summary>How well one Apple Music list row fits a library song. Only rows whose title text is exactly the song's title qualify.</summary>
internal readonly record struct AmRowScore(bool TitleExact, int Artist, int Album, bool Duration)
{
    public static AmRowScore None => new(false, 0, 0, false);

    /// <summary>Artist outweighs album outweighs duration; -1 for a row that is not the song.</summary>
    public int Rank => TitleExact ? Artist * 100 + Album * 10 + (Duration ? 1 : 0) : -1;

    /// <summary>True when nothing better can follow, so the search may stop at this row (the artist is right and so is the album, when there is one).</summary>
    public bool IsConclusive(AmTrack track) =>
        TitleExact && (Artist == 2 || (Artist >= 1 && (Album >= 1 || track.Album.Length == 0)) || (track.Artist.Length == 0 && (Album >= 1 || track.Album.Length == 0)));
}

/// <summary>One descendant of a list row: its accessible name and whether it is a Text control (as opposed to a button, image or link).</summary>
internal readonly record struct AmCell(string Name, bool IsText);

/// <summary>What GSMTC says is playing, compared with the song that was asked for.</summary>
internal readonly record struct AmPlayingCheck(bool TitleOk, bool ArtistOk, bool AlbumOk);

/// <summary>
/// The pure decisions of the Apple Music driver (no UI Automation types): which list row is the song, which menu item to invoke, which
/// button is "More", and whether the song that started is the one that was asked for. Exact where a wrong match is harmful: a song with
/// the same title plus a suffix ("Title (From "Film")", "Title - Extended") is a different song and must never be played in its place.
/// </summary>
internal static class AmMatch
{
    /// <summary>Case-, accent- and punctuation-insensitive text (quotes dropped, other punctuation a space); the lower-case text without quotes when nothing but punctuation is left.</summary>
    public static string Key(string text)
    {
        var folded = AmSearchIndex.Fold(text ?? "");
        return folded.Length > 0 ? folded : AppleMusicDriver.Squash(text ?? "");
    }

    /// <summary>The same text apart from case, accents, quotes and punctuation. Never true for two empty texts.</summary>
    public static bool SameText(string a, string b)
    {
        var ka = Key(a);
        return ka.Length > 0 && ka == Key(b);
    }

    /// <summary>Whether <paramref name="needle"/> occurs in <paramref name="hay"/> as whole words (both already folded).</summary>
    public static bool ContainsPhrase(string hay, string needle) =>
        needle.Length > 0 && (" " + hay + " ").Contains(" " + needle + " ", StringComparison.Ordinal);

    /// <summary>Cheap pre-filter before a row's text is read: does the row's accessible name hold the song's title as whole words?</summary>
    public static bool NameMayHoldTitle(string rowName, AmTrack track) => ContainsPhrase(Key(rowName), Key(track.Title));

    /// <summary>
    /// Scores a row from its cells' texts (all as Text cells). See <see cref="ScoreCells"/>.
    /// </summary>
    public static AmRowScore RowScore(string rowName, IReadOnlyList<string> texts, AmTrack track) =>
        ScoreCells(rowName, texts.Select(t => new AmCell(t, true)).ToList(), track);

    /// <summary>
    /// Scores a row. <paramref name="cells"/> are the descendants of the row in order. Only the first Text cell (the title cell) can make
    /// the row qualify: an album or artist cell that happens to read like the title (a song "Willow" on an album "Willow") must not make a
    /// longer title such as "Willow (Sped Up)" the song. Every other cell is only used for the artist, album and duration scores. With no
    /// Text cell at all the row's name is used instead, and then only when it is the title alone or the title followed by the artist.
    /// </summary>
    public static AmRowScore ScoreCells(string rowName, IReadOnlyList<AmCell> cells, AmTrack track)
    {
        var titleKey = Key(track.Title);
        if (titleKey.Length == 0)
        {
            return AmRowScore.None;
        }

        var others = new List<string>(cells.Count);
        var exact = false;
        var sawText = false;
        foreach (var cell in cells)
        {
            if (!sawText && cell.IsText && !string.IsNullOrWhiteSpace(cell.Name))
            {
                sawText = true;
                if (Key(cell.Name) == titleKey)
                {
                    exact = true;
                    continue;
                }
            }

            others.Add(cell.Name);
        }

        if (!sawText)
        {
            var nameKey = Key(rowName);
            var artistKey = Key(track.Artist);
            exact = nameKey == titleKey || (artistKey.Length > 0 && nameKey.StartsWith(titleKey + " " + artistKey, StringComparison.Ordinal));
            if (exact)
            {
                others.Add(rowName);
            }
        }

        if (!exact)
        {
            return AmRowScore.None;
        }

        var artist = 0;
        var album = 0;
        var duration = false;
        var wantedArtist = Key(track.Artist);
        var wantedAlbum = Key(track.Album);
        var credits = AmSearchIndex.SplitArtists(track.Artist).Select(Key).Where(k => k.Length > 0).ToArray();
        var seconds = track.DurationMs / 1000.0;
        foreach (var text in others)
        {
            var key = Key(text);
            if (wantedArtist.Length > 0)
            {
                if (key == wantedArtist)
                {
                    artist = 2;
                }
                else if (artist < 1 && key.Length > 0 && (ContainsPhrase(key, wantedArtist) || ContainsPhrase(wantedArtist, key) || credits.Any(c => ContainsPhrase(key, c))))
                {
                    artist = 1;
                }
            }

            if (wantedAlbum.Length > 0)
            {
                if (key == wantedAlbum)
                {
                    album = 2;
                }
                else if (album < 1 && key.Length > 0 && (ContainsPhrase(key, wantedAlbum) || ContainsPhrase(wantedAlbum, key)))
                {
                    album = 1;
                }
            }

            if (!duration && seconds > 0 && TryParseClock(text, out var shown) && Math.Abs(shown - seconds) <= 1.5)
            {
                duration = true;
            }
        }

        return new AmRowScore(true, artist, album, duration);
    }

    /// <summary>Reads "m:ss" or "h:mm:ss" (the duration cell) into seconds.</summary>
    public static bool TryParseClock(string text, out double seconds)
    {
        seconds = 0;
        var parts = (text ?? "").Trim().Split(':');
        if (parts.Length is < 2 or > 3)
        {
            return false;
        }

        double total = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            // the leading part is one or two digits (minutes or hours); minutes and seconds after a colon are always two ("12:3" is not a time)
            var part = parts[i];
            if (part.Length is 0 or > 2 || (i > 0 && part.Length != 2) || !part.All(char.IsAsciiDigit))
            {
                return false;
            }

            var value = int.Parse(part, CultureInfo.InvariantCulture);
            if (i > 0 && value > 59)
            {
                return false;
            }

            total = total * 60 + value;
        }

        seconds = total;
        return true;
    }

    // ---------------------------------------------------------------- menus

    /// <summary>The "More" button of a row: named exactly "More", or an automation id that contains "More". Nothing else is ever pressed.</summary>
    public static bool IsMoreButton(string name, string automationId) =>
        string.Equals((name ?? "").Trim(), "More", StringComparison.Ordinal)
        || (automationId ?? "").Contains("More", StringComparison.Ordinal);

    /// <summary>The first "More" button, a button named "More" before one found by its id only; -1 when there is none.</summary>
    public static int PickMore(IReadOnlyList<(string Name, string Id)> buttons)
    {
        for (var i = 0; i < buttons.Count; i++)
        {
            if (string.Equals(buttons[i].Name.Trim(), "More", StringComparison.Ordinal))
            {
                return i;
            }
        }

        for (var i = 0; i < buttons.Count; i++)
        {
            if (IsMoreButton(buttons[i].Name, buttons[i].Id))
            {
                return i;
            }
        }

        return -1;
    }

    private static readonly string[] PlayNextNames = ["play next"];

    private static readonly string[] QueueNames = ["play last", "add to queue", "play later"];

    /// <summary>The words that follow "Play " in the entries that are not "play this song".</summary>
    private static bool IsOtherPlayEntry(string remainder) =>
        remainder.Trim() is var r && (r.Equals("Next", StringComparison.OrdinalIgnoreCase) || r.Equals("Last", StringComparison.OrdinalIgnoreCase) || r.Equals("Later", StringComparison.OrdinalIgnoreCase));

    private static bool HasQuote(string text) => text.IndexOfAny(['"', '“', '”', '„', '‟', '«', '»']) >= 0;

    /// <summary>"Play “title”": the squashed name is "play " plus the squashed title.</summary>
    public static bool IsPlayItem(string name, string title)
    {
        name = (name ?? "").Trim();
        if (!name.StartsWith("Play ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remainder = name[5..];
        if (!HasQuote(remainder) && IsOtherPlayEntry(remainder))
        {
            return false;
        }

        return NormaliseSpaces(AppleMusicDriver.Squash(name)) == "play " + NormaliseSpaces(AppleMusicDriver.Squash(title))
            || SameText(remainder, title);
    }

    private static string NormaliseSpaces(string text) => string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// The index of the item to invoke for <paramref name="action"/> in a song's menu, or -1. Play: the item for exactly this title, else the
    /// one and only "Play …" item that holds the title. Play Next: "Play Next". Queue: "Play Last" / "Add to Queue" / "Play Later".
    /// There is no fallback by position and no other item is ever chosen ("Add to Library" and "Favourite" must stay untouched).
    /// </summary>
    public static int PickMenuItem(AmAction action, IReadOnlyList<string> names, string title)
    {
        switch (action)
        {
            case AmAction.PlayNext:
                return IndexOfName(names, PlayNextNames);
            case AmAction.AddToQueue:
                return IndexOfName(names, QueueNames);
        }

        for (var i = 0; i < names.Count; i++)
        {
            if (IsPlayItem(names[i], title))
            {
                return i;
            }
        }

        var titleKey = Key(title);
        var only = -1;
        for (var i = 0; i < names.Count; i++)
        {
            var name = (names[i] ?? "").Trim();
            if (!name.StartsWith("Play ", StringComparison.OrdinalIgnoreCase) || (!HasQuote(name[5..]) && IsOtherPlayEntry(name[5..])))
            {
                continue;
            }

            if (ContainsPhrase(Key(name[5..]), titleKey))
            {
                if (only >= 0)
                {
                    return -1;
                }

                only = i;
            }
        }

        return only;
    }

    private static int IndexOfName(IReadOnlyList<string> names, string[] wanted)
    {
        for (var i = 0; i < names.Count; i++)
        {
            var squashed = NormaliseSpaces(AppleMusicDriver.Squash(names[i] ?? ""));
            if (Array.IndexOf(wanted, squashed) >= 0)
            {
                return i;
            }
        }

        return -1;
    }

    // ---------------------------------------------------------------- confirmation

    /// <summary>GSMTC puts the album after an em dash in the artist: "Artist — Album".</summary>
    public static (string Artist, string Album) SplitMediaArtist(string artist)
    {
        artist ??= "";
        foreach (var dash in new[] { " — ", " – " })
        {
            var at = artist.IndexOf(dash, StringComparison.Ordinal);
            if (at >= 0)
            {
                return (artist[..at].Trim(), artist[(at + dash.Length)..].Trim());
            }
        }

        return (artist.Trim(), "");
    }

    /// <summary>The title must be the asked-for title (same text, not "contains"); artist and album only give a warning.</summary>
    public static AmPlayingCheck CheckPlaying(string mediaTitle, string mediaArtist, AmTrack track)
    {
        var titleOk = SameText(mediaTitle, track.Title);
        var (artist, album) = SplitMediaArtist(mediaArtist);
        var artistKey = Key(artist);
        var wantedArtist = Key(track.Artist);
        var artistOk = artistKey.Length > 0 && wantedArtist.Length > 0
            && (artistKey == wantedArtist || ContainsPhrase(artistKey, wantedArtist) || ContainsPhrase(wantedArtist, artistKey)
                || AmSearchIndex.SplitArtists(track.Artist).Any(c => ContainsPhrase(artistKey, Key(c))));
        var albumKey = Key(album);
        var wantedAlbum = Key(track.Album);
        var albumOk = albumKey.Length > 0 && wantedAlbum.Length > 0 && (albumKey == wantedAlbum || ContainsPhrase(albumKey, wantedAlbum) || ContainsPhrase(wantedAlbum, albumKey));
        return new AmPlayingCheck(titleOk, artistOk, albumOk);
    }
}
