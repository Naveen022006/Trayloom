// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Trayloom.Features.Island;

/// <summary>One track of the Apple Music library file (<c>Library.musicdb</c>): a library song or a playlist-only catalogue track.</summary>
internal sealed class AmTrack
{
    public required ulong Pid { get; init; }

    public string Title { get; init; } = "";

    public string Artist { get; init; } = "";

    public string Album { get; init; } = "";

    public int DurationMs { get; init; }

    public DateTime? Added { get; init; }

    /// <summary>The album artist (boma 0x1b); falls back to <see cref="Artist"/> when the file has none.</summary>
    public string AlbumArtist { get; init; } = "";

    /// <summary>The sort names (boma 0x1e title, 0x20 artist, 0x1f album); empty when the file has none (then the plain name sorts).</summary>
    public string SortTitle { get; init; } = "";

    public string SortArtist { get; init; } = "";

    public string SortAlbum { get; init; } = "";

    public string Genre { get; init; } = "";

    /// <summary>The Apple Music catalogue id (from boma 0x38 <c>adamId=…</c>); 0 when the track has none.</summary>
    public long AdamId { get; init; }

    /// <summary>
    /// True when the track is in the library proper (the master list that is Apple Music's Songs page); false for catalogue tracks that
    /// are only held by a playlist. Set while the database is built; a hand-made track is a library song.
    /// </summary>
    public bool InLibrary { get; internal set; } = true;

    /// <summary>Lower-case, accent-free "title artist album" used by the old per-tab search box.</summary>
    public string SearchKey { get; init; } = "";

    /// <summary>The name Apple Music sorts the track by: the sort title when there is one, else the title.</summary>
    public string SortTitleOrTitle => SortTitle.Length > 0 ? SortTitle : Title;

    public string SortArtistOrArtist => SortArtist.Length > 0 ? SortArtist : Artist;

    public string Duration => DurationMs <= 0 ? "" : D.TrackTime(DurationMs / 1000.0);

    public string Subtitle => string.IsNullOrWhiteSpace(Album) ? Artist : (string.IsNullOrWhiteSpace(Artist) ? Album : Artist + " · " + Album);
}

/// <summary>A playlist (user or subscribed) of the Apple Music library.</summary>
internal sealed class AmPlaylist
{
    public required ulong Pid { get; init; }

    public string Name { get; init; } = "";

    public required IReadOnlyList<AmTrack> Tracks { get; init; }

    public string SearchKey { get; init; } = "";
}

/// <summary>
/// Read-only snapshot of Apple Music for Windows' <c>Library.musicdb</c>: an <c>hfma</c> header, then a payload whose first
/// <c>min(length, maxEncrypted)</c> bytes (16-aligned) are AES-128-ECB encrypted with the format's well-known key, then zlib.
/// The file is copied to memory with shared access and never written.
/// </summary>
internal sealed class AmDb
{
    private const string Key = "BHUILuilfghuila3";

    private readonly Dictionary<ulong, AmTrack> _byId = [];
    private readonly List<AmTrack> _all = [];
    private readonly List<AmPlaylist> _playlists = [];
    private readonly object _artworkGate = new();
    private Dictionary<ulong, List<AmPlaylist>> _playlistsOf = [];
    private Dictionary<ulong, AmPlaylist> _context = [];
    private AmArtworkMap? _artwork;

    private AmDb()
    {
    }

    public ulong DbId { get; private set; }

    public DateTime Stamp { get; private set; }

    /// <summary>The file this snapshot was read from (empty for a hand-made one); the artwork folder is found next to it.</summary>
    public string SourcePath { get; private set; } = "";

    /// <summary>The library's Songs page (the master playlist) in Apple Music's order: sort title, then sort artist.</summary>
    public IReadOnlyList<AmTrack> Songs { get; private set; } = [];

    /// <summary>Library songs, newest first (only those that carry a date-added stamp); the same stamp falls back to album, then file order.</summary>
    public IReadOnlyList<AmTrack> Recent { get; private set; } = [];

    /// <summary>User and subscribed playlists in the file's (sidebar) order.</summary>
    public IReadOnlyList<AmPlaylist> Playlists => _playlists;

    public int TrackCount => _all.Count;

    /// <summary>Every track of the file (library songs and playlist-only catalogue tracks), in file order.</summary>
    public IReadOnlyList<AmTrack> AllTracks => _all;

    /// <summary>The search index over every track, album, artist and playlist; built once inside <see cref="Load"/> (so off the UI thread).</summary>
    public AmSearchIndex Search { get; private set; } = AmSearchIndex.Empty;

    public static string LibraryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Apple Music", "Apple Music Library.musiclibrary", "Library.musicdb");

    public static string AliasPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "AppleMusic.exe");

    /// <summary>The AutomationId Apple Music gives this playlist's sidebar entry (the ids embed the database and playlist ids).</summary>
    public string PlaylistAutomationId(ulong pid) => $"DBID:0x{DbId:x16}-PID:0x{pid:x16}-PPID:0x0-PIPID:0x0-IKIND:ePlaylist";

    public AmTrack? ByPid(ulong pid) => _byId.GetValueOrDefault(pid);

    /// <summary>The listed playlists that hold the track, in sidebar order (empty when none does).</summary>
    public IReadOnlyList<AmPlaylist> PlaylistsOf(ulong trackPid) => _playlistsOf.TryGetValue(trackPid, out var list) ? list : [];

    /// <summary>
    /// Where to play a track from: null for a library song (the Songs page); for a playlist-only track the smallest listed playlist that
    /// holds it (ties: sidebar order), or null when no listed playlist does.
    /// </summary>
    public AmPlaylist? DefaultContext(AmTrack track) => track.InLibrary ? null : _context.GetValueOrDefault(track.Pid);

    /// <summary>
    /// The artwork file (jpeg or png) Apple Music cached for a track, or null. The first call reads a copy of the artwork database and
    /// lists the artwork folder, so call it from a worker thread (<see cref="AmArtwork"/> does).
    /// </summary>
    public string? ArtworkPath(ulong pid) => ArtworkMap()?.Resolve(pid);

    /// <summary>How many tracks have artwork on disk (loads the artwork map when needed).</summary>
    public int ArtworkCount => ArtworkMap()?.Count ?? 0;

    private Dictionary<ulong, int>? _songIndex;

    /// <summary>
    /// Where a track sits on the page it is played from (the Songs page, or <paramref name="playlist"/> in its stored order) and how many
    /// songs that page has, so the driver can jump straight to the row. (-1, 0) when the track is not on that page.
    /// </summary>
    public (int Index, int Count) PagePosition(AmTrack track, AmPlaylist? playlist)
    {
        if (playlist is not null)
        {
            for (var i = 0; i < playlist.Tracks.Count; i++)
            {
                if (playlist.Tracks[i].Pid == track.Pid)
                {
                    return (i, playlist.Tracks.Count);
                }
            }

            return (-1, 0);
        }

        var map = _songIndex;
        if (map is null)
        {
            map = new Dictionary<ulong, int>(Songs.Count);
            for (var i = 0; i < Songs.Count; i++)
            {
                map.TryAdd(Songs[i].Pid, i);
            }

            _songIndex = map;
        }

        return map.TryGetValue(track.Pid, out var at) ? (at, Songs.Count) : (-1, 0);
    }

    private AmArtworkMap? ArtworkMap()
    {
        if (_artwork is { } ready)
        {
            return ready;
        }

        if (SourcePath.Length == 0)
        {
            return null;
        }

        lock (_artworkGate)
        {
            return _artwork ??= AmArtworkMap.Create(SourcePath);
        }
    }

    /// <summary>The sort rule of the Songs page: names compared ignoring case and accents.</summary>
    internal static int CompareNames(string a, string b) =>
        CultureInfo.InvariantCulture.CompareInfo.Compare(a, b, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);

    /// <summary>Songs-page order: sort title, then sort artist.</summary>
    internal static int CompareSongs(AmTrack a, AmTrack b)
    {
        var c = CompareNames(a.SortTitleOrTitle, b.SortTitleOrTitle);
        return c != 0 ? c : CompareNames(a.SortArtistOrArtist, b.SortArtistOrArtist);
    }

    public static AmDb Load(string path)
    {
        var data = ReadPayload(path, out var stamp, out var dbId);
        var db = new AmDb { DbId = dbId, Stamp = stamp, SourcePath = path };
        db.Parse(data);
        db.Build();
        return db;
    }

    /// <summary>
    /// A snapshot made from hand-built data (harness checks): <paramref name="libraryPids"/> is the master list (null = every track is a
    /// library song), <paramref name="playlists"/> the listed playlists in sidebar order.
    /// </summary>
    internal static AmDb FromTracks(IEnumerable<AmTrack> tracks, IEnumerable<ulong>? libraryPids, IEnumerable<(ulong Pid, string Name, IReadOnlyList<ulong> Items)>? playlists = null)
    {
        var db = new AmDb();
        foreach (var t in tracks)
        {
            db._all.Add(t);
            db._byId[t.Pid] = t;
        }

        if (libraryPids is not null)
        {
            db._rawPlaylists.Add((1, "####!####", [.. libraryPids], true));
        }

        foreach (var (pid, name, items) in playlists ?? [])
        {
            db._rawPlaylists.Add((pid, name, [.. items], true));
        }

        db.Build();
        return db;
    }

    /// <summary>Reads (shared, never written) and decodes the file; returns the decrypted, inflated payload.</summary>
    internal static byte[] ReadPayload(string path, out DateTime stamp, out ulong dbId)
    {
        byte[] raw;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            stamp = File.GetLastWriteTimeUtc(path);
            raw = new byte[fs.Length];
            fs.ReadExactly(raw);
        }

        if (raw.Length < 100 || Encoding.ASCII.GetString(raw, 0, 4) != "hfma")
        {
            throw new InvalidDataException("not a music database");
        }

        var headerLength = I32(raw, 4);
        var maxEncrypted = I32(raw, 84);
        dbId = BinaryPrimitives.ReadUInt64LittleEndian(raw.AsSpan(48));
        var payload = raw.AsSpan(headerLength).ToArray();
        var n = Math.Min(payload.Length, Math.Max(0, maxEncrypted));
        n -= n % 16;
        if (n > 0)
        {
            using var aes = Aes.Create();
            aes.Key = Encoding.ASCII.GetBytes(Key);
            aes.DecryptEcb(payload.AsSpan(0, n), PaddingMode.None).CopyTo(payload, 0);
        }

        using var z = new ZLibStream(new MemoryStream(payload), CompressionMode.Decompress);
        using var ms = new MemoryStream();
        z.CopyTo(ms);
        return ms.ToArray();
    }

    private static int I32(byte[] b, int o) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(o));

    private static ulong U64(byte[] b, int o) => BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(o));

    private static bool Tag(byte[] b, int o, string t) => o >= 0 && o + 4 <= b.Length && b[o] == t[0] && b[o + 1] == t[1] && b[o + 2] == t[2] && b[o + 3] == t[3];

    private static IEnumerable<(int Subtype, int Offset, int Length)> Bomas(byte[] d, int o)
    {
        var headerLength = I32(d, o + 4);
        var count = I32(d, o + 12);
        var p = o + headerLength;
        for (var k = 0; k < count; k++)
        {
            if (!Tag(d, p, "boma"))
            {
                yield break;
            }

            var length = I32(d, p + 8);
            yield return (I32(d, p + 12), p, length);
            p += length;
        }
    }

    private static string Str(byte[] d, int p)
    {
        var encoding = I32(d, p + 20);
        var length = I32(d, p + 24);
        if (length < 0 || p + 36 + length > d.Length)
        {
            return "";
        }

        var text = encoding == 1 ? Encoding.Unicode.GetString(d, p + 36, length) : Encoding.UTF8.GetString(d, p + 36, length);
        return text.Trim().Trim('\0').Trim();
    }

    private void Parse(byte[] d)
    {
        for (var i = 0; i + 4 <= d.Length; i++)
        {
            if (!Tag(d, i, "hsma"))
            {
                continue;
            }

            var list = i + I32(d, i + 4);
            if (Tag(d, list, "ltma"))
            {
                ParseList(d, list, "itma", ParseTrack);
            }
            else if (Tag(d, list, "lPma"))
            {
                ParseList(d, list, "lpma", ParsePlaylist);
            }
        }
    }

    private static void ParseList(byte[] d, int list, string itemTag, Action<byte[], int> each)
    {
        var count = I32(d, list + 8);
        var p = list + I32(d, list + 4);
        for (var k = 0; k < count && Tag(d, p, itemTag); k++)
        {
            each(d, p);
            var length = I32(d, p + 8);
            if (length <= 0)
            {
                break;
            }

            p += length;
        }
    }

    private void ParseTrack(byte[] d, int o)
    {
        string title = "", artist = "", album = "", albumArtist = "", sortTitle = "", sortArtist = "", sortAlbum = "", genre = "";
        long adamId = 0;
        var duration = 0;
        DateTime? added = null;
        foreach (var (subtype, p, length) in Bomas(d, o))
        {
            switch (subtype)
            {
                case 0x2:
                    title = Str(d, p);
                    break;
                case 0x3:
                    album = Str(d, p);
                    break;
                case 0x4:
                    artist = Str(d, p);
                    break;
                case 0x5:
                    genre = Str(d, p);
                    break;
                case 0x1b:
                    albumArtist = Str(d, p);
                    break;
                case 0x1e:
                    sortTitle = Str(d, p);
                    break;
                case 0x1f:
                    sortAlbum = Str(d, p);
                    break;
                case 0x20:
                    sortArtist = Str(d, p);
                    break;
                case 0x38:
                    adamId = AdamIdOf(d, p, length);
                    break;
                case 0x1:
                    if (length >= 180)
                    {
                        duration = I32(d, p + 176);
                    }

                    if (length >= 116)
                    {
                        var secs = (uint)I32(d, p + 112);
                        if (secs > 3_000_000_000u)
                        {
                            added = new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(secs);
                        }
                    }

                    break;
            }
        }

        var pid = U64(d, o + 16);
        var track = new AmTrack
        {
            Pid = pid,
            Title = title,
            Artist = artist,
            Album = album,
            AlbumArtist = albumArtist.Length > 0 ? albumArtist : artist,
            SortTitle = sortTitle,
            SortArtist = sortArtist,
            SortAlbum = sortAlbum,
            Genre = genre,
            AdamId = adamId,
            DurationMs = duration,
            Added = added,
            SearchKey = WatchService.Normalise($"{title} {artist} {album}"),
        };
        _all.Add(track);
        _byId[pid] = track;
    }

    /// <summary>The number after the ASCII text <c>adamId=</c> somewhere in the boma (the catalogue id of an Apple Music track); 0 if absent.</summary>
    private static long AdamIdOf(byte[] d, int p, int length)
    {
        ReadOnlySpan<byte> marker = "adamId="u8;
        var end = Math.Min(d.Length, p + length);
        if (end <= p)
        {
            return 0;
        }

        var span = d.AsSpan(p, end - p);
        var at = span.IndexOf(marker);
        if (at < 0)
        {
            return 0;
        }

        long value = 0;
        for (var i = at + marker.Length; i < span.Length && span[i] is >= (byte)'0' and <= (byte)'9' && value < 100_000_000_000_000L; i++)
        {
            value = (value * 10) + (span[i] - (byte)'0');
        }

        return value;
    }

    private readonly List<(ulong Pid, string Name, List<ulong> Items, bool Cloud)> _rawPlaylists = [];

    private void ParsePlaylist(byte[] d, int o)
    {
        var pid = U64(d, o + 30);
        var name = "";
        var items = new List<ulong>();
        var cloud = false;

        // Apple Music's own lists (Downloaded, Music, Genius, …) carry a non-zero "kind" byte in the header; user playlists have 0.
        // Favourite Songs is one of the special kinds but has the cloud box (0xcd) like the playlists the sidebar lists.
        var userKind = o + 79 < d.Length && d[o + 79] == 0;
        foreach (var (subtype, p, _) in Bomas(d, o))
        {
            if (subtype == 0xc8)
            {
                name = Str(d, p);
            }
            else if (subtype == 0xcd)
            {
                cloud = true;
            }
            else if (subtype == 0xda && Tag(d, p + 20, "IPfa"))
            {
                items.Add(U64(d, p + 36));
            }
        }

        _rawPlaylists.Add((pid, name, items, cloud || userKind));
    }

    private void Build()
    {
        var master = _rawPlaylists.FirstOrDefault(p => p.Name.StartsWith("####", StringComparison.Ordinal));
        var masterIds = (master.Items ?? []).Where(_byId.ContainsKey).ToHashSet();
        var everything = masterIds.Count == 0;
        foreach (var t in _all)
        {
            t.InLibrary = everything || masterIds.Contains(t.Pid);
        }

        // The library proper in file order: the base of the Songs and Recently added orders (LINQ ordering is stable).
        var library = _all.Where(t => t.InLibrary).DistinctBy(t => t.Pid).ToList();
        Songs = [.. library.OrderBy(t => t, Comparer<AmTrack>.Create(CompareSongs))];
        var byAlbum = Comparer<AmTrack>.Create((a, b) => CompareNames(a.Album, b.Album));
        Recent = [.. library.Where(t => t.Added is not null).OrderByDescending(t => t.Added).ThenBy(t => t, byAlbum).Take(150)];

        foreach (var (pid, name, items, cloud) in _rawPlaylists)
        {
            if (!cloud || name.StartsWith("####", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var tracks = items.Where(_byId.ContainsKey).Select(id => _byId[id]).ToList();
            _playlists.Add(new AmPlaylist { Pid = pid, Name = name.Trim(), Tracks = tracks, SearchKey = WatchService.Normalise(name) });
        }

        _rawPlaylists.Clear();

        // Which playlists hold a track, and where a playlist-only track is played from: the smallest list that holds it.
        var holders = new Dictionary<ulong, List<AmPlaylist>>();
        var context = new Dictionary<ulong, AmPlaylist>();
        foreach (var playlist in _playlists)
        {
            foreach (var pid in playlist.Tracks.Select(t => t.Pid).Distinct())
            {
                if (!holders.TryGetValue(pid, out var list))
                {
                    holders[pid] = list = [];
                }

                list.Add(playlist);
                if (!context.TryGetValue(pid, out var best) || playlist.Tracks.Count < best.Tracks.Count)
                {
                    context[pid] = playlist;
                }
            }
        }

        _playlistsOf = holders;
        _context = context;
        Search = new AmSearchIndex(_all, _playlists);
    }
}
