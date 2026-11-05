// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Trayloom.Features.Island;

/// <summary>An album of the library: tracks sharing a folded album name and folded album artist (file order).</summary>
internal sealed record AmAlbum(string Key, string Name, string Artist, IReadOnlyList<AmTrack> Tracks);

/// <summary>An artist: the names found by splitting track artists (and album artists other than "Various Artists"); tracks in file order.</summary>
internal sealed record AmArtist(string Key, string Name, IReadOnlyList<AmTrack> Tracks);

/// <summary>One search result with its relevance score (higher = better).</summary>
internal sealed record AmHit<T>(T Item, int Score);

/// <summary>
/// The answer to one query: the best <c>perGroup</c> hits of each group plus how many matched in all (for a "Show all (N)" link).
/// </summary>
internal sealed class AmSearchResults
{
    public static readonly AmSearchResults None = new() { Query = "" };

    /// <summary>The folded query (what was actually searched).</summary>
    public string Query { get; init; } = "";

    public IReadOnlyList<AmHit<AmTrack>> Songs { get; init; } = [];

    public IReadOnlyList<AmHit<AmAlbum>> Albums { get; init; } = [];

    public IReadOnlyList<AmHit<AmArtist>> Artists { get; init; } = [];

    public IReadOnlyList<AmHit<AmPlaylist>> Playlists { get; init; } = [];

    public int SongTotal { get; init; }

    public int AlbumTotal { get; init; }

    public int ArtistTotal { get; init; }

    public int PlaylistTotal { get; init; }

    public bool IsEmpty => SongTotal + AlbumTotal + ArtistTotal + PlaylistTotal == 0;
}

/// <summary>
/// Search over the whole library file (library songs, playlist-only catalogue tracks, albums, artists, playlists), built once per
/// <see cref="AmDb"/>. Text is folded (case, accents and punctuation ignored); every term of the query must match something.
/// </summary>
internal sealed partial class AmSearchIndex
{
    /// <summary>An index with nothing in it (before a library is loaded).</summary>
    public static readonly AmSearchIndex Empty = new([], []);

    private const int MaxTop = 128;
    private const int PerTermTitleWord = 3;
    private const int PerTermTitleText = 2;
    private const int PerTermOtherWord = 2;
    private const int PerTermOtherText = 1;
    private const char Separator = '\u0001';

    private static readonly char[] Space = [' '];

    private readonly Entry[] _entries;

    /// <summary>Every album in name order (the Albums browse tab).</summary>
    public IReadOnlyList<AmAlbum> AllAlbums { get; }

    /// <summary>Every artist in name order (the Artists browse tab).</summary>
    public IReadOnlyList<AmArtist> AllArtists { get; }

    private readonly List<Named<AmAlbum>> _albums = [];
    private readonly List<Named<AmArtist>> _artists = [];
    private readonly List<Named<AmPlaylist>> _playlists = [];

    private sealed class Entry
    {
        public required AmTrack Track { get; init; }

        public required string Title { get; init; }

        public required string Artist { get; init; }

        public required string Album { get; init; }

        public required string AlbumArtist { get; init; }

        public required string Genre { get; init; }

        public required string[] TitleWords { get; init; }

        public required string[] OtherWords { get; init; }

        /// <summary>Everything in one string (fields joined by a separator no term contains): a cheap "can this match at all" test.</summary>
        public required string All { get; init; }

        public int Rank { get; set; }
    }

    private sealed record Named<T>(T Item, string Name, string[] NameWords, string Other, string[] OtherWords, bool InLibrary)
    {
        public string All { get; } = Name + Separator + Other;
    }

    public AmSearchIndex(IReadOnlyList<AmTrack> tracks, IReadOnlyList<AmPlaylist> playlists)
    {
        _entries = new Entry[tracks.Count];
        for (var i = 0; i < tracks.Count; i++)
        {
            var t = tracks[i];
            var title = Fold(t.Title);
            var artist = Fold(t.Artist);
            var album = Fold(t.Album);
            var albumArtist = Fold(t.AlbumArtist);
            var genre = Fold(t.Genre);
            _entries[i] = new Entry
            {
                Track = t,
                Title = title,
                Artist = artist,
                Album = album,
                AlbumArtist = albumArtist,
                Genre = genre,
                TitleWords = Words(title),
                OtherWords = Words(artist + " " + album + " " + albumArtist),
                All = string.Join(Separator, title, artist, album, albumArtist, genre),
            };
        }

        // Rank = place in the Songs-page order: the tie-break of equal scores.
        var order = new int[_entries.Length];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (a, b) =>
        {
            var c = AmDb.CompareSongs(_entries[a].Track, _entries[b].Track);
            return c != 0 ? c : a.CompareTo(b);
        });
        for (var r = 0; r < order.Length; r++)
        {
            _entries[order[r]].Rank = r;
        }

        BuildAlbums(tracks);
        BuildArtists(tracks);
        AllAlbums = [.. _albums.Select(a => a.Item)];
        AllArtists = [.. _artists.Select(a => a.Item)];
        foreach (var p in playlists)
        {
            var name = Fold(p.Name);
            _playlists.Add(new Named<AmPlaylist>(p, name, Words(name), "", [], true));
        }
    }

    // ---------------------------------------------------------------- folding

    /// <summary>
    /// The form text is compared in: <see cref="WatchService.Normalise"/> (compatibility-decomposed, accents removed, lower case) with
    /// the marks . ' ’ ‘ " “ ” dropped (so "A.R. Rahman" is "ar rahman") and every other punctuation character turned into a space,
    /// whitespace collapsed and trimmed.
    /// </summary>
    public static string Fold(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return "";
        }

        var d = s.IsNormalized(NormalizationForm.FormKD) ? s : s.Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder(d.Length);
        var pendingSpace = false;
        foreach (var ch in d)
        {
            if (ch is '.' or '\'' or '’' or '‘' or '"' or '“' or '”')
            {
                continue;
            }

            if (ch >= 0x300 && CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(char.ToLowerInvariant(ch));
        }

        return sb.ToString();
    }

    private static string[] Words(string folded) => folded.Length == 0 ? [] : folded.Split(Space, StringSplitOptions.RemoveEmptyEntries);

    // ---------------------------------------------------------------- albums and artists

    [GeneratedRegex(@", | & | feat\.? | ft\.? | x ", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ArtistSeparators();

    /// <summary>The artists a credit names: "A & B", "A, B", "A feat. B", "A ft. B", "A x B".</summary>
    public static IEnumerable<string> SplitArtists(string credit) =>
        ArtistSeparators().Split(credit).Select(p => p.Trim()).Where(p => p.Length > 0);

    private void BuildAlbums(IReadOnlyList<AmTrack> tracks)
    {
        var groups = new Dictionary<string, (string Name, string Artist, List<AmTrack> Tracks)>();
        foreach (var t in tracks)
        {
            if (string.IsNullOrWhiteSpace(t.Album))
            {
                continue;
            }

            var artist = t.AlbumArtist.Length > 0 ? t.AlbumArtist : t.Artist;
            var key = Fold(t.Album) + Separator + Fold(artist);
            if (!groups.TryGetValue(key, out var group))
            {
                groups[key] = group = (t.Album, artist, []);
            }

            group.Tracks.Add(t);
        }

        foreach (var (key, group) in groups)
        {
            var name = Fold(group.Name);
            var artist = Fold(group.Artist);
            var album = new AmAlbum(key, group.Name, group.Artist, group.Tracks);
            _albums.Add(new Named<AmAlbum>(album, name, Words(name), artist, Words(artist), group.Tracks.Exists(t => t.InLibrary)));
        }

        _albums.Sort((a, b) => AmDb.CompareNames(a.Item.Name, b.Item.Name));
    }

    private void BuildArtists(IReadOnlyList<AmTrack> tracks)
    {
        var groups = new Dictionary<string, (string Name, List<AmTrack> Tracks)>();
        var seen = new HashSet<string>();
        foreach (var t in tracks)
        {
            seen.Clear();
            var names = SplitArtists(t.Artist);
            if (t.AlbumArtist.Length > 0 && Fold(t.AlbumArtist) != "various artists")
            {
                names = names.Concat(SplitArtists(t.AlbumArtist));
            }

            foreach (var name in names)
            {
                var key = Fold(name);
                if (key.Length == 0 || !seen.Add(key))
                {
                    continue;
                }

                if (!groups.TryGetValue(key, out var group))
                {
                    groups[key] = group = (name, []);
                }

                group.Tracks.Add(t);
            }
        }

        foreach (var (key, group) in groups)
        {
            var artist = new AmArtist(key, group.Name, group.Tracks);
            _artists.Add(new Named<AmArtist>(artist, key, Words(key), "", [], group.Tracks.Exists(t => t.InLibrary)));
        }

        _artists.Sort((a, b) => AmDb.CompareNames(a.Item.Name, b.Item.Name));
    }

    // ---------------------------------------------------------------- queries

    /// <summary>
    /// Searches everything. Per term the best field decides: a title word that starts with it 3, a title containing it 2, an artist /
    /// album word starting with it 2, any other field containing it 1 (a term nothing contains rejects the item). Bonuses: the whole
    /// query is the title +10, the title starts with the query +5, a library song +1. Ties go in Songs-page order.
    /// <paramref name="perGroup"/> caps each group (int.MaxValue = all); the totals count every match.
    /// </summary>
    public AmSearchResults Query(string text, int perGroup)
    {
        var query = Fold(text ?? "");
        if (query.Length == 0)
        {
            return AmSearchResults.None;
        }

        var terms = query.Split(Space, StringSplitOptions.RemoveEmptyEntries);
        var cap = perGroup <= 0 ? 0 : perGroup;
        var (songs, songTotal) = QuerySongs(query, terms, cap);
        var (albums, albumTotal) = QueryNamed(_albums, query, terms, cap);
        var (artists, artistTotal) = QueryNamed(_artists, query, terms, cap);
        var (playlists, playlistTotal) = QueryNamed(_playlists, query, terms, cap);
        return new AmSearchResults
        {
            Query = query,
            Songs = songs,
            Albums = albums,
            Artists = artists,
            Playlists = playlists,
            SongTotal = songTotal,
            AlbumTotal = albumTotal,
            ArtistTotal = artistTotal,
            PlaylistTotal = playlistTotal,
        };
    }

    private (List<AmHit<AmTrack>> Hits, int Total) QuerySongs(string query, string[] terms, int cap)
    {
        const int idBits = 21;
        const long idMask = (1L << idBits) - 1;

        // A small cap keeps the best matches in a short ascending array (no sort of everything that matched); cap 0 only counts;
        // a big cap (a "show all") collects every match and sorts once.
        var everything = cap > MaxTop;
        var top = new long[everything ? 0 : Math.Min(cap, _entries.Length)];
        var collected = everything ? new List<long>() : null;
        var filled = 0;
        var total = 0;
        for (var i = 0; i < _entries.Length; i++)
        {
            var e = _entries[i];
            var score = 0;
            var ok = true;
            foreach (var term in terms)
            {
                // the cheap test first: most tracks contain none of the terms anywhere
                var s = e.All.Contains(term, StringComparison.Ordinal) ? TermScore(e, term) : 0;
                if (s == 0)
                {
                    ok = false;
                    break;
                }

                score += s;
            }

            if (!ok)
            {
                continue;
            }

            total++;
            if (!everything && top.Length == 0)
            {
                continue;
            }

            if (e.Title == query)
            {
                score += 10;
            }

            if (e.Title.StartsWith(query, StringComparison.Ordinal))
            {
                score += 5;
            }

            if (e.Track.InLibrary)
            {
                score++;
            }

            // best score first, then Songs-page rank
            var key = ((long)score << (2 * idBits)) | ((idMask - Math.Min(e.Rank, idMask)) << idBits) | (long)i;
            if (collected is not null)
            {
                collected.Add(key);
            }
            else if (filled < top.Length)
            {
                Insert(top, filled++, key);
            }
            else if (top.Length > 0 && key > top[0])
            {
                // full: drop the smallest (slot 0) and put the new one in place
                Array.Copy(top, 1, top, 0, top.Length - 1);
                Insert(top, top.Length - 1, key);
            }
        }

        if (collected is not null)
        {
            top = [.. collected.Order()];
            filled = Math.Min(top.Length, cap);
            top = top[^filled..];
        }

        var hits = new List<AmHit<AmTrack>>(filled);
        for (var k = filled - 1; k >= 0; k--)
        {
            hits.Add(new AmHit<AmTrack>(_entries[(int)(top[k] & idMask)].Track, (int)(top[k] >> (2 * idBits))));
        }

        return (hits, total);
    }

    /// <summary>Puts <paramref name="key"/> into the ascending array whose first <paramref name="count"/> slots are used (slot <paramref name="count"/> is free).</summary>
    private static void Insert(long[] a, int count, long key)
    {
        var at = count;
        while (at > 0 && a[at - 1] > key)
        {
            a[at] = a[at - 1];
            at--;
        }

        a[at] = key;
    }

    private static int TermScore(Entry e, string term)
    {
        foreach (var w in e.TitleWords)
        {
            if (w.StartsWith(term, StringComparison.Ordinal))
            {
                return PerTermTitleWord;
            }
        }

        if (e.Title.Contains(term, StringComparison.Ordinal))
        {
            return PerTermTitleText;
        }

        foreach (var w in e.OtherWords)
        {
            if (w.StartsWith(term, StringComparison.Ordinal))
            {
                return PerTermOtherWord;
            }
        }

        if (e.Artist.Contains(term, StringComparison.Ordinal) || e.Album.Contains(term, StringComparison.Ordinal)
            || e.AlbumArtist.Contains(term, StringComparison.Ordinal) || e.Genre.Contains(term, StringComparison.Ordinal))
        {
            return PerTermOtherText;
        }

        return 0;
    }

    private static (List<AmHit<T>> Hits, int Total) QueryNamed<T>(List<Named<T>> items, string query, string[] terms, int cap)
    {
        // Same scheme as the songs: a key of score and list position, the best `cap` kept in a short ascending array (no allocation per
        // match, no sort of everything); the lists are in name order, so equal scores stay A-Z. A big cap collects everything and sorts once.
        const int idBits = 31;
        const long idMask = (1L << idBits) - 1;
        var everything = cap > MaxTop;
        var top = new long[everything ? 0 : Math.Min(cap, items.Count)];
        var collected = everything ? new List<long>() : null;
        var filled = 0;
        var total = 0;
        for (var i = 0; i < items.Count; i++)
        {
            var n = items[i];
            var score = 0;
            var ok = true;
            foreach (var term in terms)
            {
                var s = n.All.Contains(term, StringComparison.Ordinal) ? NamedTermScore(n, term) : 0;
                if (s == 0)
                {
                    ok = false;
                    break;
                }

                score += s;
            }

            if (!ok)
            {
                continue;
            }

            total++;
            if (!everything && top.Length == 0)
            {
                continue;
            }

            if (n.Name == query)
            {
                score += 10;
            }

            if (n.Name.StartsWith(query, StringComparison.Ordinal))
            {
                score += 5;
            }

            if (n.InLibrary)
            {
                score++;
            }

            // best score first, then the earlier list position
            var key = ((long)score << idBits) | (idMask - i);
            if (collected is not null)
            {
                collected.Add(key);
            }
            else if (filled < top.Length)
            {
                Insert(top, filled++, key);
            }
            else if (top.Length > 0 && key > top[0])
            {
                Array.Copy(top, 1, top, 0, top.Length - 1);
                Insert(top, top.Length - 1, key);
            }
        }

        if (collected is not null)
        {
            top = [.. collected.Order()];
            filled = Math.Min(top.Length, cap);
            top = top[^filled..];
        }

        var hits = new List<AmHit<T>>(filled);
        for (var k = filled - 1; k >= 0; k--)
        {
            hits.Add(new AmHit<T>(items[(int)(idMask - (top[k] & idMask))].Item, (int)(top[k] >> idBits)));
        }

        return (hits, total);
    }

    private static int NamedTermScore<T>(Named<T> n, string term)
    {
        foreach (var w in n.NameWords)
        {
            if (w.StartsWith(term, StringComparison.Ordinal))
            {
                return PerTermTitleWord;
            }
        }

        if (n.Name.Contains(term, StringComparison.Ordinal))
        {
            return PerTermTitleText;
        }

        foreach (var w in n.OtherWords)
        {
            if (w.StartsWith(term, StringComparison.Ordinal))
            {
                return PerTermOtherWord;
            }
        }

        return n.Other.Contains(term, StringComparison.Ordinal) ? PerTermOtherText : 0;
    }
}
