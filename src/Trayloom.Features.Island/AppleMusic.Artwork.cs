// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;

namespace Trayloom.Features.Island;

/// <summary>
/// Where Apple Music cached each track's artwork: <c>artwork.sqlite</c> maps a track pid (as a signed 64-bit integer) to an artwork
/// id, and the picture is <c>artwork\&lt;id&gt;_sk1.jpeg</c> (or png) next to the library file. Everything is read-only: the
/// database is copied (with its -wal) to a temp folder with shared reads, and only that copy is opened.
/// </summary>
internal sealed class AmArtworkMap
{
    private readonly Dictionary<long, string> _files;

    private AmArtworkMap(Dictionary<long, string> files)
    {
        _files = files;
    }

    /// <summary>How many tracks have a picture on disk.</summary>
    public int Count => _files.Count;

    public static AmArtworkMap Empty { get; } = new([]);

    /// <summary>The picture file of a track, or null.</summary>
    public string? Resolve(ulong pid) => _files.GetValueOrDefault(unchecked((long)pid));

    /// <summary>
    /// Reads the artwork database that sits next to <paramref name="libraryFile"/> (the picture folder too; the environment variable
    /// <c>TRAYLOOM_AMART</c> names a different picture folder, for a copied library). Never throws: no artwork is an empty map.
    /// </summary>
    public static AmArtworkMap Create(string libraryFile)
    {
        string? copy = null;
        try
        {
            var folder = Path.GetDirectoryName(libraryFile) ?? "";
            var database = Path.Combine(folder, "artwork.sqlite");
            var pictures = Path.Combine(folder, "artwork");
            if (Environment.GetEnvironmentVariable("TRAYLOOM_AMART") is { Length: > 0 } custom)
            {
                pictures = custom;
            }

            if (!File.Exists(database) || !Directory.Exists(pictures))
            {
                return Empty;
            }

            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(pictures, "*_sk1.*"))
            {
                var name = Path.GetFileName(file);
                var id = name[..name.IndexOf("_sk1.", StringComparison.OrdinalIgnoreCase)];
                var extension = Path.GetExtension(name);
                if (extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || !files.ContainsKey(id))
                {
                    files[id] = file;
                }
            }

            copy = CopyDatabase(database);
            var map = new Dictionary<long, string>();
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = copy, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT item_id, artwork_id FROM item_to_artwork";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (files.TryGetValue(reader.GetString(1), out var path))
                    {
                        map[reader.GetInt64(0)] = path;
                    }
                }
            }

            return new AmArtworkMap(map);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException or InvalidOperationException)
        {
            return Empty;
        }
        finally
        {
            DeleteCopy(copy);
        }
    }

    private static string CopyDatabase(string database)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Trayloom", "am-art");
        Directory.CreateDirectory(directory);
        var copy = Path.Combine(directory, "artwork-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8] + ".sqlite");
        CopyShared(database, copy);
        if (File.Exists(database + "-wal"))
        {
            CopyShared(database + "-wal", copy + "-wal");
        }

        return copy;
    }

    private static void CopyShared(string from, string to)
    {
        using var source = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var target = new FileStream(to, FileMode.Create, FileAccess.Write, FileShare.None);
        source.CopyTo(target);
    }

    private static void DeleteCopy(string? copy)
    {
        if (copy is null)
        {
            return;
        }

        foreach (var file in new[] { copy, copy + "-wal", copy + "-shm" })
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // a leftover temp file; the folder is Trayloom's own
            }
        }
    }
}

/// <summary>
/// Small decoded pictures for the library rows: 64 px wide by default (a larger width for a header tile), an LRU of 200 keyed by file and
/// width, decoded on the thread pool.
/// </summary>
internal static class AmArtwork
{
    private const int Capacity = 200;

    /// <summary>The decode width of a row tile.</summary>
    public const int DecodeWidth = 64;

    /// <summary>How many pictures a mosaic holds.</summary>
    public const int MosaicCount = 4;

    private static readonly object Gate = new();
    private static readonly SemaphoreSlim Slots = new(2);
    private static readonly Dictionary<string, LinkedListNode<(string Key, ImageSource? Image)>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<(string Key, ImageSource? Image)> Order = new();

    /// <summary>The cache key of one picture at one width.</summary>
    private static string KeyOf(string path, int width) => width == DecodeWidth ? path : path + "|" + width.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The pictures a 2x2 mosaic shows for a list of artwork files (null = a track without one): the first four distinct files, in order.
    /// With one to three distinct pictures a mosaic would repeat or leave holes, so only the first is returned (shown whole); none gives none.
    /// </summary>
    public static IReadOnlyList<string> PickMosaic(IEnumerable<string?> paths)
    {
        var distinct = new List<string>(MosaicCount);
        foreach (var path in paths)
        {
            if (path is null || distinct.Exists(d => string.Equals(d, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            distinct.Add(path);
            if (distinct.Count == MosaicCount)
            {
                return distinct;
            }
        }

        return distinct.Count > 0 ? [distinct[0]] : [];
    }

    /// <summary>The picture if it was decoded before (cheap, UI-thread safe); a failed decode is cached as "no picture".</summary>
    public static bool TryGetCached(string path, out ImageSource? image, int width = DecodeWidth)
    {
        var key = KeyOf(path, width);
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var node))
            {
                Order.Remove(node);
                Order.AddFirst(node);
                image = node.Value.Image;
                return true;
            }
        }

        image = null;
        return false;
    }

    /// <summary>
    /// The track's picture, decoded off the calling thread (null = none or unreadable). The result is frozen, so any thread may use it.
    /// At most two decodes run at once: a screenful of rows asks for dozens of pictures together, and many parallel decodes on thread-pool
    /// threads were far slower than the same decodes one after another. Also used for playlists (their pid has a picture too).
    /// </summary>
    public static async Task<ImageSource?> GetAsync(AmDb db, ulong pid, int width = DecodeWidth)
    {
        return await Task.Run(async () =>
        {
            var path = db.ArtworkPath(pid);
            return path is null ? null : await GetForPathAsync(path, width).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    /// <summary>The picture of one artwork file; a file that cannot be decoded is remembered as "no picture" so it is not retried.</summary>
    internal static async Task<ImageSource?> GetForPathAsync(string path, int width = DecodeWidth)
    {
        if (TryGetCached(path, out var cached, width))
        {
            return cached;
        }

        await Slots.WaitAsync().ConfigureAwait(false);
        try
        {
            if (TryGetCached(path, out cached, width))
            {
                return cached;
            }

            var image = Decode(path, width);
            var key = KeyOf(path, width);
            lock (Gate)
            {
                if (!Cache.ContainsKey(key))
                {
                    Cache[key] = Order.AddFirst((key, image));
                    while (Order.Count > Capacity)
                    {
                        Cache.Remove(Order.Last!.Value.Key);
                        Order.RemoveLast();
                    }
                }
            }

            return image;
        }
        finally
        {
            Slots.Release();
        }
    }

    internal static ImageSource? Decode(string path, int width = DecodeWidth)
    {
        try
        {
            // read into memory first: the decoder must not keep Apple Music's file open
            // (shared like Apple Music's own readers: never block it from replacing the file)
            byte[] bytes;
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                bytes = new byte[file.Length];
                file.ReadExactly(bytes);
            }

            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = width;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            // a corrupt, truncated or vanished picture (FileFormatException, COMException, ...) is simply "no picture"
            return null;
        }
    }
}
