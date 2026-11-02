// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;
using WinKnife.Core;

namespace WinKnife.Features.WindowTools;

/// <summary>One remembered clipboard item.</summary>
internal sealed class ClipEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>"text" or "image".</summary>
    public string Kind { get; set; } = "text";

    public string? Text { get; set; }

    /// <summary>PNG file name inside the images folder.</summary>
    public string? ImageFile { get; set; }

    public string? ThumbFile { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public long ImageBytes { get; set; }

    public string Hash { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public bool Pinned { get; set; }

    public string? Source { get; set; }

    [JsonIgnore]
    public bool IsImage => Kind == "image";
}

/// <summary>Persistent clipboard history: JSON index plus PNG files in the feature's data directory.</summary>
internal sealed class ClipboardHistoryStore
{
    private const long MaxImageBytesTotal = 256L * 1024 * 1024;
    private const int MaxTextChars = 1_000_000;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly string _indexPath;
    private readonly string _imageDirectory;
    private readonly ILog _log;
    private readonly List<ClipEntry> _entries = [];
    private System.Windows.Threading.DispatcherTimer? _saveTimer;
    private bool _dirty;

    public ClipboardHistoryStore(string dataDirectory, ILog log, System.Windows.Threading.Dispatcher ui)
    {
        _log = log;
        _indexPath = Path.Combine(dataDirectory, "history.json");
        _imageDirectory = Path.Combine(dataDirectory, "images");
        Directory.CreateDirectory(_imageDirectory);
        Load();
        _saveTimer = new System.Windows.Threading.DispatcherTimer(
            TimeSpan.FromSeconds(1), System.Windows.Threading.DispatcherPriority.Background, (_, _) => Flush(), ui);
        _saveTimer.Stop();
    }

    /// <summary>Pinned items first, then newest first.</summary>
    public IReadOnlyList<ClipEntry> Entries =>
        [.. _entries.OrderByDescending(e => e.Pinned).ThenByDescending(e => e.CreatedUtc)];

    public event Action? Changed;

    public string ImagePath(ClipEntry entry) => Path.Combine(_imageDirectory, entry.ImageFile ?? string.Empty);

    public string ThumbPath(ClipEntry entry) => Path.Combine(_imageDirectory, entry.ThumbFile ?? entry.ImageFile ?? string.Empty);

    public static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    public static string Hash(string text) => Hash(Encoding.UTF8.GetBytes(text));

    /// <summary>Adds (or bumps to the top) a text item. Returns false when the text is too large.</summary>
    public bool AddText(string text, string? source, int limit, int retentionDays)
    {
        if (text.Length == 0 || text.Length > MaxTextChars || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var hash = Hash(text);
        var existing = _entries.FirstOrDefault(e => e.Hash == hash);
        if (existing is not null)
        {
            existing.CreatedUtc = DateTime.UtcNow;
            existing.Source = source ?? existing.Source;
        }
        else
        {
            _entries.Add(new ClipEntry { Kind = "text", Text = text, Hash = hash, Source = source });
        }

        Prune(limit, retentionDays);
        Touch();
        return true;
    }

    /// <summary>Adds (or bumps) an image given as PNG bytes. Returns false for undecodable or oversized images.</summary>
    public bool AddImage(byte[] png, string? source, int limit, int retentionDays)
    {
        if (png.Length > 40 * 1024 * 1024)
        {
            return false;
        }

        var hash = Hash(png);
        var existing = _entries.FirstOrDefault(e => e.Hash == hash);
        if (existing is not null)
        {
            existing.CreatedUtc = DateTime.UtcNow;
            existing.Source = source ?? existing.Source;
            Touch();
            return true;
        }

        try
        {
            var id = Guid.NewGuid().ToString("N");
            var imageFile = id + ".png";
            var thumbFile = id + ".thumb.png";

            int width;
            int height;
            using (var stream = new MemoryStream(png))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                width = decoder.Frames[0].PixelWidth;
                height = decoder.Frames[0].PixelHeight;
            }

            File.WriteAllBytes(Path.Combine(_imageDirectory, imageFile), png);
            WriteThumbnail(png, Path.Combine(_imageDirectory, thumbFile));
            _entries.Add(new ClipEntry
            {
                Id = id,
                Kind = "image",
                ImageFile = imageFile,
                ThumbFile = thumbFile,
                Width = width,
                Height = height,
                ImageBytes = png.Length,
                Hash = hash,
                Source = source,
            });
        }
        catch (Exception ex)
        {
            _log.Warn($"Could not store clipboard image: {ex.Message}");
            return false;
        }

        Prune(limit, retentionDays);
        Touch();
        return true;
    }

    /// <summary>Moves an item to the top (used after pasting it).</summary>
    public void Bump(ClipEntry entry)
    {
        entry.CreatedUtc = DateTime.UtcNow;
        Touch();
    }

    public void TogglePin(ClipEntry entry)
    {
        entry.Pinned = !entry.Pinned;
        Touch();
    }

    public void Delete(ClipEntry entry)
    {
        if (_entries.Remove(entry))
        {
            DeleteFiles(entry);
            Touch();
        }
    }

    /// <summary>Removes every unpinned item.</summary>
    public void ClearUnpinned()
    {
        foreach (var entry in _entries.Where(e => !e.Pinned).ToList())
        {
            _entries.Remove(entry);
            DeleteFiles(entry);
        }

        Touch();
    }

    /// <summary>Enforces the count limit and retention. Pinned items are exempt.</summary>
    public void Prune(int limit, int retentionDays)
    {
        var cutoff = retentionDays > 0 ? DateTime.UtcNow.AddDays(-retentionDays) : DateTime.MinValue;
        foreach (var entry in _entries.Where(e => !e.Pinned && e.CreatedUtc < cutoff).ToList())
        {
            _entries.Remove(entry);
            DeleteFiles(entry);
        }

        if (limit > 0)
        {
            var unpinned = _entries.Where(e => !e.Pinned).OrderByDescending(e => e.CreatedUtc).ToList();
            foreach (var entry in unpinned.Skip(limit))
            {
                _entries.Remove(entry);
                DeleteFiles(entry);
            }
        }

        // Keep the images folder bounded regardless of the item limit.
        var imageTotal = _entries.Where(e => e.IsImage).Sum(e => e.ImageBytes);
        if (imageTotal > MaxImageBytesTotal)
        {
            foreach (var entry in _entries.Where(e => e.IsImage && !e.Pinned).OrderBy(e => e.CreatedUtc).ToList())
            {
                if (imageTotal <= MaxImageBytesTotal)
                {
                    break;
                }

                imageTotal -= entry.ImageBytes;
                _entries.Remove(entry);
                DeleteFiles(entry);
            }
        }

        Touch(raise: false);
    }

    /// <summary>Writes pending changes now.</summary>
    public void Flush()
    {
        _saveTimer?.Stop();
        if (!_dirty)
        {
            return;
        }

        _dirty = false;
        try
        {
            var temp = _indexPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_entries, JsonOptions));
            File.Move(temp, _indexPath, overwrite: true);
        }
        catch (Exception ex)
        {
            _log.Error("Could not save clipboard history", ex);
        }
    }

    public void Dispose()
    {
        Flush();
        _saveTimer = null;
    }

    private void Touch(bool raise = true)
    {
        _dirty = true;
        _saveTimer?.Stop();
        _saveTimer?.Start();
        if (raise)
        {
            Changed?.Invoke();
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_indexPath))
            {
                return;
            }

            var loaded = JsonSerializer.Deserialize<List<ClipEntry>>(File.ReadAllText(_indexPath), JsonOptions);
            if (loaded is null)
            {
                return;
            }

            foreach (var entry in loaded)
            {
                if (entry.IsImage && (entry.ImageFile is null || !File.Exists(ImagePath(entry))))
                {
                    continue;
                }

                if (!entry.IsImage && string.IsNullOrEmpty(entry.Text))
                {
                    continue;
                }

                _entries.Add(entry);
            }

            // Drop image files nothing refers to any more (crash leftovers).
            var known = _entries.Where(e => e.IsImage).SelectMany(e => new[] { e.ImageFile, e.ThumbFile }).ToHashSet();
            foreach (var file in Directory.EnumerateFiles(_imageDirectory))
            {
                if (!known.Contains(Path.GetFileName(file)))
                {
                    TryDelete(file);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error("Could not read clipboard history; starting empty", ex);
            _entries.Clear();
        }
    }

    private void DeleteFiles(ClipEntry entry)
    {
        if (!entry.IsImage)
        {
            return;
        }

        TryDelete(ImagePath(entry));
        TryDelete(ThumbPath(entry));
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // best effort
        }
        catch (UnauthorizedAccessException)
        {
            // best effort
        }
    }

    private static void WriteThumbnail(byte[] png, string path)
    {
        var image = new BitmapImage();
        using (var stream = new MemoryStream(png))
        {
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.DecodePixelHeight = 96;
            image.EndInit();
        }

        image.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = File.Create(path);
        encoder.Save(output);
    }
}
