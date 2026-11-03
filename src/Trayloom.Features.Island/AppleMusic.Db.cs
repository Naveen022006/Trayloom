// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Trayloom.Features.Island;

/// <summary>One song of the Apple Music library (read from <c>Library.musicdb</c>).</summary>
internal sealed class AmTrack
{
    public required ulong Pid { get; init; }

    public string Title { get; init; } = "";

    public string Artist { get; init; } = "";

    public string Album { get; init; } = "";

    public int DurationMs { get; init; }

    public DateTime? Added { get; init; }

    /// <summary>Lower-case, accent-free "title artist album" used by the search box.</summary>
    public string SearchKey { get; init; } = "";

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

    private AmDb()
    {
    }

    public ulong DbId { get; private set; }

    public DateTime Stamp { get; private set; }

    /// <summary>The library's Songs page (the master playlist), A–Z.</summary>
    public IReadOnlyList<AmTrack> Songs { get; private set; } = [];

    /// <summary>Library songs, newest first (only those that carry a date-added stamp).</summary>
    public IReadOnlyList<AmTrack> Recent { get; private set; } = [];

    /// <summary>User and subscribed playlists in the file's (sidebar) order.</summary>
    public IReadOnlyList<AmPlaylist> Playlists => _playlists;

    public int TrackCount => _all.Count;

    public static string LibraryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Apple Music", "Apple Music Library.musiclibrary", "Library.musicdb");

    public static string AliasPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "AppleMusic.exe");

    /// <summary>The AutomationId Apple Music gives this playlist's sidebar entry (the ids embed the database and playlist ids).</summary>
    public string PlaylistAutomationId(ulong pid) => $"DBID:0x{DbId:x16}-PID:0x{pid:x16}-PPID:0x0-PIPID:0x0-IKIND:ePlaylist";

    public static AmDb Load(string path)
    {
        byte[] raw;
        DateTime stamp;
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
        var db = new AmDb { DbId = BinaryPrimitives.ReadUInt64LittleEndian(raw.AsSpan(48)), Stamp = stamp };
        var payload = raw.AsSpan(headerLength).ToArray();
        var n = Math.Min(payload.Length, Math.Max(0, maxEncrypted));
        n -= n % 16;
        if (n > 0)
        {
            using var aes = Aes.Create();
            aes.Key = Encoding.ASCII.GetBytes(Key);
            aes.DecryptEcb(payload.AsSpan(0, n), PaddingMode.None).CopyTo(payload, 0);
        }

        byte[] data;
        using (var z = new ZLibStream(new MemoryStream(payload), CompressionMode.Decompress))
        using (var ms = new MemoryStream())
        {
            z.CopyTo(ms);
            data = ms.ToArray();
        }

        db.Parse(data);
        db.Build();
        return db;
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

        return encoding == 1 ? Encoding.Unicode.GetString(d, p + 36, length) : Encoding.UTF8.GetString(d, p + 36, length);
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
        string title = "", artist = "", album = "";
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
            DurationMs = duration,
            Added = added,
            SearchKey = WatchService.Normalise($"{title} {artist} {album}"),
        };
        _all.Add(track);
        _byId[pid] = track;
    }

    private readonly List<(ulong Pid, string Name, List<ulong> Items, bool Cloud)> _rawPlaylists = [];

    private void ParsePlaylist(byte[] d, int o)
    {
        var pid = U64(d, o + 30);
        var name = "";
        var items = new List<ulong>();
        var cloud = false;
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

        _rawPlaylists.Add((pid, name, items, cloud));
    }

    private void Build()
    {
        var master = _rawPlaylists.FirstOrDefault(p => p.Name.StartsWith("####", StringComparison.Ordinal));
        var songs = (master.Items ?? []).Where(_byId.ContainsKey).Select(id => _byId[id]).DistinctBy(t => t.Pid).ToList();
        if (songs.Count == 0)
        {
            songs = [.. _all];
        }

        songs.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase));
        Songs = songs;
        Recent = [.. songs.Where(t => t.Added is not null).OrderByDescending(t => t.Added).Take(150)];
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
    }
}
