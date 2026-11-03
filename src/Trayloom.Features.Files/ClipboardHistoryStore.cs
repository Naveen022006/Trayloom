// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Trayloom.Core;

namespace Trayloom.Features.Files;

internal static class ClipKinds
{
    public const string Text = "text";
    public const string Image = "image";
    public const string Files = "files";
}

/// <summary>One remembered clipboard item (text, an image kept as a PNG file, or a list of file paths).</summary>
internal sealed class ClipEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Kind { get; set; } = ClipKinds.Text;

    /// <summary>The content of a text entry (empty for images and files).</summary>
    public string Text { get; set; } = string.Empty;

    public List<string> FilePaths { get; set; } = [];

    /// <summary>PNG file name inside the image folder.</summary>
    public string? ImageFile { get; set; }

    public string? ImageHash { get; set; }

    public int ImageWidth { get; set; }

    public int ImageHeight { get; set; }

    public DateTime CopiedAt { get; set; } = DateTime.UtcNow;

    public DateTime? PinnedAt { get; set; }

    /// <summary>Executable image name (<c>chrome.exe</c>) of the app the copy came from.</summary>
    public string? SourceExe { get; set; }

    public string? SourcePath { get; set; }

    [JsonIgnore]
    public bool IsPinned => PinnedAt is not null;

    [JsonIgnore]
    public bool IsImage => Kind == ClipKinds.Image;

    [JsonIgnore]
    public bool IsFiles => Kind == ClipKinds.Files;

    [JsonIgnore]
    public bool IsText => Kind == ClipKinds.Text;

    [JsonIgnore]
    public IEnumerable<string> FileNames => FilePaths.Select(Path.GetFileName).Where(n => !string.IsNullOrEmpty(n))!;

    [JsonIgnore]
    public string ImageDimensionsLabel => ImageWidth > 0 && ImageHeight > 0 ? $"{ImageWidth}×{ImageHeight}" : string.Empty;

    [JsonIgnore]
    public double ImageAspectRatio => ImageWidth > 0 && ImageHeight > 0 ? (double)ImageWidth / ImageHeight : 1.0;

    /// <summary>One-line preview (line breaks and tabs folded).</summary>
    public string Preview()
    {
        switch (Kind)
        {
            case ClipKinds.Image:
                return ImageDimensionsLabel;
            case ClipKinds.Files:
                return string.Join(", ", FileNames);
            default:
                var prefix = Text.Length > ClipLimits.PreviewCharacters ? Text[..ClipLimits.PreviewCharacters] : Text;
                var collapsed = prefix.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ').Trim();
                var visible = collapsed.Length == 0 ? prefix : collapsed;
                return prefix.Length == Text.Length ? visible : visible + "…";
        }
    }

    /// <summary>Preview that keeps line breaks (cards).</summary>
    public string CardPreview()
    {
        if (!IsText)
        {
            return Preview();
        }

        var prefix = Text.Length > ClipLimits.PreviewCharacters ? Text[..ClipLimits.PreviewCharacters] : Text;
        var spaced = prefix.Replace('\t', ' ').Trim();
        var visible = spaced.Length == 0 ? prefix : spaced;
        return prefix.Length == Text.Length ? visible : visible + "…";
    }

    /// <summary>Shortened one-line text for the tray pill.</summary>
    public string TrayText(int maxCharacters)
    {
        var baseText = IsImage
            ? $"{Strings.Clipboard.ImageLabel} · {ImageDimensionsLabel}"
            : Preview().Replace("\r", " ").Replace("\n", " ");
        return baseText.Length > maxCharacters ? baseText[..maxCharacters] + "…" : baseText;
    }

    public bool MatchesContent(ClipEntry other)
    {
        if (Kind != other.Kind)
        {
            return false;
        }

        return Kind switch
        {
            ClipKinds.Text => Text == other.Text,
            ClipKinds.Image => ImageHash is not null && ImageHash == other.ImageHash,
            _ => FilePaths.SequenceEqual(other.FilePaths, StringComparer.OrdinalIgnoreCase),
        };
    }

    public string SearchableText()
    {
        switch (Kind)
        {
            case ClipKinds.Image:
                return $"{Strings.Clipboard.ImageLabel} png {ImageDimensionsLabel}";
            case ClipKinds.Files:
                var names = string.Join(' ', FileNames);
                return FilePaths.Any(ClipImages.IsImageFileName) ? $"{Strings.Clipboard.ImageLabel} {names}" : names;
            default:
                return Text;
        }
    }

    /// <summary>The colour a text entry spells out, when that is all it holds.</summary>
    public Color? ColorValue() => IsText ? ClipColors.Parse(Text) : null;

    public ClipEntry Clone() => (ClipEntry)MemberwiseClone();
}

internal static class ClipLimits
{
    public const int PreviewCharacters = 2000;
    public const int MaxCharacters = 1_000_000;
    public const int MaxStoredTextBytes = 64 * 1024 * 1024;
    public const long MaxEncodedBytes = 96L * 1024 * 1024;
    public const int MaxImageEncodedBytes = 16 * 1024 * 1024;
    public const long MaxImageRawBytes = 64L * 1024 * 1024;
    public const long MaxImagePixels = 24_000_000;
    public const int MaxFiles = 100;
}

internal static class ClipImages
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".heic", ".ico",
    };

    public static bool IsImageFileName(string name) => Extensions.Contains(Path.GetExtension(name));

    public static bool IsImageFile(string path) => IsImageFileName(path) && File.Exists(path);

    /// <summary>Pixel size of an image file without decoding it fully.</summary>
    public static (int Width, int Height)? Dimensions(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            return (frame.PixelWidth, frame.PixelHeight);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string FileSizeString(string path)
    {
        try
        {
            var bytes = new FileInfo(path).Length;
            string[] units = ["B", "KB", "MB", "GB"];
            var value = (double)bytes;
            var unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return unit == 0 ? $"{bytes} B" : $"{value.ToString("0.#", CultureInfo.InvariantCulture)} {units[unit]}";
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>Loads a frozen bitmap limited to <paramref name="maxPixelWidth"/> (null when the file is unreadable).</summary>
    public static BitmapSource? Load(string path, int maxPixelWidth)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.UriSource = new Uri(path);
            if (maxPixelWidth > 0)
            {
                image.DecodePixelWidth = maxPixelWidth;
            }

            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

internal static class ClipColors
{
    private static readonly Regex Hex = new(@"^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.Compiled);
    private static readonly Regex Rgb = new(@"^rgba?\(\s*(\d{1,3})\s*[, ]\s*(\d{1,3})\s*[, ]\s*(\d{1,3})\s*(?:[,/]\s*([\d.]+%?)\s*)?\)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static Color? Parse(string text)
    {
        var t = text.Trim();
        if (t.Length is < 4 or > 40)
        {
            return null;
        }

        var hex = Hex.Match(t);
        if (hex.Success)
        {
            var h = hex.Groups[1].Value;
            if (h.Length == 3)
            {
                h = string.Concat(h.Select(c => new string(c, 2)));
            }

            var r = Convert.ToByte(h[..2], 16);
            var g = Convert.ToByte(h[2..4], 16);
            var b = Convert.ToByte(h[4..6], 16);
            var a = h.Length == 8 ? Convert.ToByte(h[6..8], 16) : (byte)255;
            return Color.FromArgb(a, r, g, b);
        }

        var rgb = Rgb.Match(t);
        if (rgb.Success)
        {
            var r = int.Parse(rgb.Groups[1].Value);
            var g = int.Parse(rgb.Groups[2].Value);
            var b = int.Parse(rgb.Groups[3].Value);
            if (r <= 255 && g <= 255 && b <= 255)
            {
                return Color.FromRgb((byte)r, (byte)g, (byte)b);
            }
        }

        return null;
    }
}

/// <summary>Search over history entries: diacritic/case folding, token matching and relevance ranking (Vorssaint's ranking).</summary>
internal static class ClipSearch
{
    public static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c is '\n' or '\r' or '\t' ? ' ' : char.ToLowerInvariant(c));
            }
        }

        return sb.ToString().Trim();
    }

    public static List<string> Tokens(string query) =>
        [.. query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)];

    public static List<ClipEntry> Rank(IReadOnlyList<ClipEntry> entries, string query, Dictionary<string, string>? cache = null)
    {
        var normalizedQuery = Normalize(query);
        var tokens = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return [.. entries];
        }

        var scored = new List<(ClipEntry Entry, int Score, int Order)>();
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            string text;
            var cacheKey = entry.Id + "|" + entry.Text.GetHashCode();
            if (cache is not null && cache.TryGetValue(cacheKey, out var hit))
            {
                text = hit;
            }
            else
            {
                text = Normalize(entry.SearchableText());
                if (cache is not null)
                {
                    cache[cacheKey] = text;
                }
            }

            if (!tokens.All(t => text.Contains(t, StringComparison.Ordinal)))
            {
                continue;
            }

            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            var score = entry.IsPinned ? 30 : 0;
            if (text == normalizedQuery)
            {
                score += 1200;
            }

            if (text.StartsWith(normalizedQuery, StringComparison.Ordinal))
            {
                score += 900;
            }

            if (text.Contains(normalizedQuery, StringComparison.Ordinal))
            {
                score += 700;
            }

            foreach (var token in tokens)
            {
                if (words.Contains(token))
                {
                    score += 140;
                }
                else if (words.Any(w => w.StartsWith(token, StringComparison.Ordinal)))
                {
                    score += 80;
                }
                else
                {
                    score += 40;
                }
            }

            scored.Add((entry, score, i));
        }

        return [.. scored.OrderByDescending(s => s.Score).ThenBy(s => s.Order).Select(s => s.Entry)];
    }

    /// <summary>Character ranges of <paramref name="text"/> matching a token (case and diacritic insensitive).</summary>
    public static List<(int Start, int Length)> HighlightRanges(string text, IReadOnlyList<string> tokens)
    {
        var ranges = new List<(int, int)>();
        if (text.Length == 0 || tokens.Count == 0)
        {
            return ranges;
        }

        var compare = CultureInfo.InvariantCulture.CompareInfo;
        foreach (var token in tokens)
        {
            if (token.Length == 0)
            {
                continue;
            }

            var start = 0;
            while (start < text.Length)
            {
                var index = compare.IndexOf(text, token, start, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreWidth);
                if (index < 0)
                {
                    break;
                }

                ranges.Add((index, token.Length));
                start = index + Math.Max(1, token.Length);
            }
        }

        return ranges;
    }
}

/// <summary>"Looks sensitive" heuristic (opt-in): named credential assignments and long unbroken mixed strings.</summary>
internal static class SensitiveText
{
    private const string Name = @"(?:password|passwd|secret|token|api[_-]?key|private[_-]?key|authorization)";
    private const string Ending = @"(?:[_-]?(?:access[_-]?)?key(?:[_-]?base)?|[_-]?confirmation|[_-]?\d{1,3})?";

    private static readonly Regex Assignment = new(
        @"(?<![\w-])(?:[\w-]*" + Name + Ending + @"[""']?[ \t]*[:=][ \t]*|--[\w-]*" + Name + Ending + @"[ \t]+)[""']?[^\s""'`,;{}\[\]]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool Looks(string text)
    {
        if (Assignment.IsMatch(text))
        {
            return true;
        }

        if (IsWebUrl(text) || IsIdentifierCode(text))
        {
            return false;
        }

        if (text.Length is < 20 or > 160 || text.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var hasLetter = text.Any(char.IsLetter);
        var hasDigit = text.Any(char.IsDigit);
        var hasSymbol = text.Any(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c));
        return hasLetter && hasDigit && hasSymbol;
    }

    private static bool IsIdentifierCode(string text)
    {
        var value = text.Trim();
        if (value.StartsWith('{') && value.EndsWith('}'))
        {
            value = value[1..^1];
        }

        var groups = value.Split('-');
        int[] expected = [8, 4, 4, 4, 12];
        return groups.Length == 5 && groups.Select(g => g.Length).SequenceEqual(expected) && groups.All(g => g.All(Uri.IsHexDigit));
    }

    private static bool IsWebUrl(string text) =>
        Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && !string.IsNullOrEmpty(uri.Host);
}

/// <summary>Persistent clipboard history: an ordered list (pinned first, then recent, newest first) saved as JSON plus PNG files.</summary>
internal sealed class ClipboardHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly string _indexPath;
    private readonly ILog _log;
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _saveTimer;
    private readonly Dictionary<string, string> _searchCache = [];
    private readonly string _legacyDirectory;
    private List<ClipEntry> _entries = [];
    private bool _dirty;
    private int _saving;

    public ClipboardHistoryStore(string legacyDirectory, ILog log, Dispatcher ui)
    {
        _log = log;
        _ui = ui;
        _legacyDirectory = legacyDirectory;
        var data = Environment.GetEnvironmentVariable("TRAYLOOM_DATA");
        var root = string.IsNullOrWhiteSpace(data)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Trayloom")
            : Path.Combine(data, "local");
        Directory.CreateDirectory(root);
        ImageDirectory = Path.Combine(root, "Clipboard");
        Directory.CreateDirectory(ImageDirectory);
        _indexPath = Path.Combine(root, "clipboard.json");
        Load(legacyDirectory);
        _saveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(700), DispatcherPriority.Background, (_, _) => Flush(), ui);
        _saveTimer.Stop();
    }

    public string ImageDirectory { get; }

    public IReadOnlyList<ClipEntry> Entries => _entries;

    public IEnumerable<ClipEntry> Pinned => _entries.Where(e => e.IsPinned);

    public IEnumerable<ClipEntry> Recent => _entries.Where(e => !e.IsPinned);

    public int Revision { get; private set; }

    /// <summary>Raised (UI thread) after any change to the list.</summary>
    public event Action? Changed;

    /// <summary>Id of the entry most recently put on the clipboard by a copy or a paste (the tray pill shows it).</summary>
    public string? LatestPasteboardEntryId { get; set; }

    public string ImagePath(ClipEntry entry) => Path.Combine(ImageDirectory, entry.ImageFile ?? string.Empty);

    public ClipEntry? Find(string id) => _entries.FirstOrDefault(e => e.Id == id);

    public static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    // ---------------------------------------------------------------------------------------------- capture

    /// <summary>Adds (or promotes) a copied text. Returns the entry or null when the text cannot be stored.</summary>
    public ClipEntry? PromoteText(string text, string? sourceExe, string? sourcePath, int limit, int retentionDays)
    {
        if (text.Length == 0 || text.Length > ClipLimits.MaxCharacters || string.IsNullOrWhiteSpace(text) || Encoding.UTF8.GetByteCount(text) > ClipLimits.MaxStoredTextBytes)
        {
            return null;
        }

        var entry = new ClipEntry { Kind = ClipKinds.Text, Text = text };
        return Insert(entry, sourceExe, sourcePath, limit, retentionDays);
    }

    /// <summary>Adds (or promotes) a copied image given as PNG bytes.</summary>
    public ClipEntry? PromoteImage(byte[] png, string? sourceExe, string? sourcePath, int limit, int retentionDays)
    {
        if (png.Length == 0 || png.Length > ClipLimits.MaxImageEncodedBytes)
        {
            return null;
        }

        var hash = Sha256(png);
        var existing = _entries.FirstOrDefault(e => e.IsImage && e.ImageHash == hash);
        if (existing is not null)
        {
            return Insert(existing, sourceExe, sourcePath, limit, retentionDays);
        }

        try
        {
            int width;
            int height;
            using (var stream = new MemoryStream(png))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                width = decoder.Frames[0].PixelWidth;
                height = decoder.Frames[0].PixelHeight;
            }

            if ((long)width * height > ClipLimits.MaxImagePixels || (long)width * height * 4 > ClipLimits.MaxImageRawBytes)
            {
                return null;
            }

            var file = Guid.NewGuid().ToString("N") + ".png";
            File.WriteAllBytes(Path.Combine(ImageDirectory, file), png);
            var entry = new ClipEntry { Kind = ClipKinds.Image, ImageFile = file, ImageHash = hash, ImageWidth = width, ImageHeight = height };
            return Insert(entry, sourceExe, sourcePath, limit, retentionDays);
        }
        catch (Exception ex)
        {
            _log.Warn($"Could not store clipboard image: {ex.Message}");
            return null;
        }
    }

    /// <summary>Adds (or promotes) a copied list of files (remembered as links).</summary>
    public ClipEntry? PromoteFiles(IReadOnlyList<string> paths, string? sourceExe, string? sourcePath, int limit, int retentionDays)
    {
        if (paths.Count == 0 || paths.Count > ClipLimits.MaxFiles)
        {
            return null;
        }

        var entry = new ClipEntry { Kind = ClipKinds.Files, FilePaths = [.. paths] };
        return Insert(entry, sourceExe, sourcePath, limit, retentionDays);
    }

    private ClipEntry Insert(ClipEntry candidate, string? sourceExe, string? sourcePath, int limit, int retentionDays)
    {
        var existing = _entries.FirstOrDefault(e => e.MatchesContent(candidate));
        var entry = existing ?? candidate;
        if (existing is not null)
        {
            _entries.Remove(existing);
        }

        entry.CopiedAt = DateTime.UtcNow;
        if (!string.IsNullOrEmpty(sourceExe))
        {
            entry.SourceExe = sourceExe;
            entry.SourcePath = sourcePath;
        }

        if (entry.IsPinned)
        {
            _entries.Insert(0, entry);
        }
        else
        {
            _entries.Insert(FirstRecentIndex, entry);
        }

        LatestPasteboardEntryId = entry.Id;
        Trim(limit, retentionDays, null);
        Touch();
        return entry;
    }

    // ---------------------------------------------------------------------------------------------- editing

    /// <summary>Moves reused entries to the top of the recent group (pinned ones keep their place) and refreshes their date.</summary>
    public void Touch(IReadOnlyList<string> ids)
    {
        var order = new Dictionary<string, int>();
        foreach (var id in ids)
        {
            order.TryAdd(id, order.Count);
        }

        var now = DateTime.UtcNow;
        var pinned = new List<ClipEntry>();
        var reused = new List<ClipEntry>();
        var others = new List<ClipEntry>();
        var updated = false;
        foreach (var entry in _entries)
        {
            var isReused = order.ContainsKey(entry.Id);
            if (isReused)
            {
                entry.CopiedAt = now;
                updated = true;
            }

            if (entry.IsPinned)
            {
                pinned.Add(entry);
            }
            else if (isReused)
            {
                reused.Add(entry);
            }
            else
            {
                others.Add(entry);
            }
        }

        if (!updated)
        {
            return;
        }

        reused.Sort((a, b) => order[a.Id].CompareTo(order[b.Id]));
        _entries = [.. pinned, .. reused, .. others];
        Touch();
    }

    public bool TogglePin(ClipEntry entry, int limit, int retentionDays)
    {
        var index = _entries.IndexOf(entry);
        if (index < 0)
        {
            return false;
        }

        if (!entry.IsPinned && EstimatedBytes() > ClipLimits.MaxEncodedBytes)
        {
            return false; // pins are refused rather than dropped when the file is full
        }

        _entries.RemoveAt(index);
        if (entry.IsPinned)
        {
            entry.PinnedAt = null;
            _entries.Insert(FirstRecentIndex, entry);
        }
        else
        {
            entry.PinnedAt = DateTime.UtcNow;
            _entries.Insert(0, entry);
        }

        NormalizeOrder();
        Trim(limit, retentionDays, null);
        Touch();
        return true;
    }

    public void Remove(ClipEntry entry)
    {
        if (_entries.Remove(entry))
        {
            DeleteFiles(entry);
            Touch();
        }
    }

    public void Remove(IEnumerable<string> ids)
    {
        var set = ids.ToHashSet();
        var removed = _entries.Where(e => set.Contains(e.Id)).ToList();
        if (removed.Count == 0)
        {
            return;
        }

        _entries.RemoveAll(e => set.Contains(e.Id));
        foreach (var entry in removed)
        {
            DeleteFiles(entry);
        }

        Touch();
    }

    /// <summary>Snapshot of the unpinned entries (id and copy time) a "Clear unpinned" confirmation counts.</summary>
    public Dictionary<string, DateTime> RecentSnapshot() => Recent.ToDictionary(e => e.Id, e => e.CopiedAt);

    public void ClearRecent(IReadOnlyDictionary<string, DateTime> confirmed)
    {
        var removed = _entries.Where(e => !e.IsPinned && confirmed.TryGetValue(e.Id, out var at) && at == e.CopiedAt).ToList();
        if (removed.Count == 0)
        {
            return;
        }

        foreach (var entry in removed)
        {
            _entries.Remove(entry);
            DeleteFiles(entry);
        }

        Touch();
    }

    /// <summary>Removes every unpinned entry (command-bar action).</summary>
    public void ClearUnpinned() => ClearRecent(RecentSnapshot());

    public bool CanMove(ClipEntry entry, int direction) => MoveDestination(entry, direction) is not null;

    public void Move(ClipEntry entry, int direction)
    {
        var from = _entries.IndexOf(entry);
        var to = MoveDestination(entry, direction);
        if (from < 0 || to is null)
        {
            return;
        }

        (_entries[from], _entries[to.Value]) = (_entries[to.Value], _entries[from]);
        Touch();
    }

    /// <summary>Drag reordering: the entry takes the place of <paramref name="target"/> inside its own group (pinned or recent).</summary>
    public bool MoveTo(ClipEntry moving, ClipEntry target)
    {
        if (moving == target || moving.IsPinned != target.IsPinned)
        {
            return false;
        }

        var from = _entries.IndexOf(moving);
        var to = _entries.IndexOf(target);
        if (from < 0 || to < 0)
        {
            return false;
        }

        _entries.RemoveAt(from);
        _entries.Insert(to, moving);
        Touch();
        return true;
    }

    public bool UpdateText(ClipEntry entry, string draft)
    {
        if (!entry.IsText || draft.Length == 0 || string.IsNullOrWhiteSpace(draft) || draft.Length > ClipLimits.MaxCharacters)
        {
            return false;
        }

        if (draft == entry.Text)
        {
            return true;
        }

        // An edit that becomes identical to another entry merges into it.
        var twin = _entries.FirstOrDefault(e => e != entry && e.IsText && e.Text == draft);
        if (twin is not null)
        {
            _entries.Remove(twin);
        }

        entry.Text = draft;
        Touch();
        return true;
    }

    /// <summary>Enforces the limit and the retention. Pinned entries are exempt; <paramref name="ageExempt"/> ids skip the age rule.</summary>
    public void Trim(int limit, int retentionDays, ISet<string>? ageExempt)
    {
        var cutoff = retentionDays > 0 ? DateTime.UtcNow.AddDays(-retentionDays) : (DateTime?)null;
        var remaining = (long)ClipLimits.MaxStoredTextBytes;
        var keep = new List<ClipEntry>();
        foreach (var entry in _entries.Where(e => e.IsPinned))
        {
            var bytes = entry.IsText ? Encoding.UTF8.GetByteCount(entry.Text) : 0;
            if (bytes <= remaining)
            {
                remaining -= bytes;
                keep.Add(entry);
            }
        }

        var recentCount = 0;
        foreach (var entry in _entries.Where(e => !e.IsPinned))
        {
            if (limit > 0 && recentCount >= limit)
            {
                break;
            }

            if (cutoff is not null && !(ageExempt?.Contains(entry.Id) ?? false) && entry.CopiedAt < cutoff)
            {
                continue;
            }

            var bytes = entry.IsText ? Encoding.UTF8.GetByteCount(entry.Text) : 0;
            if (bytes > remaining)
            {
                continue;
            }

            remaining -= bytes;
            recentCount++;
            keep.Add(entry);
        }

        if (keep.Count == _entries.Count)
        {
            return;
        }

        var dropped = _entries.Except(keep).ToList();
        _entries = keep;
        foreach (var entry in dropped)
        {
            DeleteFiles(entry);
        }

        Touch();
    }

    public List<ClipEntry> Filter(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [.. _entries];
        }

        if (_searchCache.Count > 5000)
        {
            _searchCache.Clear();
        }

        return ClipSearch.Rank(_entries, query, _searchCache);
    }

    // ---------------------------------------------------------------------------------------------- persistence

    private int FirstRecentIndex
    {
        get
        {
            var index = _entries.FindIndex(e => !e.IsPinned);
            return index < 0 ? _entries.Count : index;
        }
    }

    private int? MoveDestination(ClipEntry entry, int direction)
    {
        var group = new List<int>();
        for (var i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].IsPinned == entry.IsPinned)
            {
                group.Add(i);
            }
        }

        var position = group.FindIndex(i => _entries[i] == entry);
        if (position < 0)
        {
            return null;
        }

        var target = position + direction;
        return target < 0 || target >= group.Count ? null : group[target];
    }

    private void NormalizeOrder() => _entries = [.. _entries.Where(e => e.IsPinned), .. _entries.Where(e => !e.IsPinned)];

    private long EstimatedBytes() => _entries.Sum(e => 200L + (e.IsText ? e.Text.Length : 0) + e.FilePaths.Sum(p => p.Length));

    private void Touch()
    {
        Revision++;
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
        Changed?.Invoke();
    }

    /// <summary>Writes pending changes now (off the UI thread unless <paramref name="synchronous"/>).</summary>
    public void Flush(bool synchronous = false)
    {
        _saveTimer.Stop();
        if (!_dirty)
        {
            return;
        }

        _dirty = false;
        var snapshot = _entries.Select(e => e.Clone()).ToList();
        if (synchronous)
        {
            Write(snapshot);
            return;
        }

        Task.Run(() =>
        {
            if (Interlocked.Exchange(ref _saving, 1) == 1)
            {
                _ui.BeginInvoke(() =>
                {
                    _dirty = true;
                    _saveTimer.Start();
                });
                return;
            }

            try
            {
                Write(snapshot);
            }
            finally
            {
                Interlocked.Exchange(ref _saving, 0);
            }
        });
    }

    private void Write(List<ClipEntry> snapshot)
    {
        try
        {
            var temp = _indexPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(snapshot, JsonOptions));
            File.Move(temp, _indexPath, overwrite: true);
            WriteReadOnlyMirror(snapshot);
        }
        catch (Exception ex)
        {
            _log.Error("Could not save clipboard history", ex);
        }
    }

    /// <summary>
    /// A read-only mirror in the feature data folder in the format the first Trayloom versions used (<c>history.json</c>), so other
    /// features that only display the history (the Dynamic Island page) keep working. Image paths are absolute.
    /// </summary>
    private void WriteReadOnlyMirror(List<ClipEntry> snapshot)
    {
        try
        {
            Directory.CreateDirectory(_legacyDirectory);
            var mirror = snapshot.Select(e => new Dictionary<string, object?>
            {
                ["Id"] = e.Id,
                ["Kind"] = e.IsImage ? "image" : "text",
                ["Text"] = e.IsFiles ? string.Join(", ", e.FileNames) : e.Text,
                ["ImageFile"] = e.IsImage && e.ImageFile is not null ? Path.Combine(ImageDirectory, e.ImageFile) : null,
                ["ThumbFile"] = null,
                ["Width"] = e.ImageWidth,
                ["Height"] = e.ImageHeight,
                ["CreatedUtc"] = e.CopiedAt,
                ["Pinned"] = e.IsPinned,
                ["Source"] = e.SourceExe is { Length: > 0 } exe ? Path.GetFileNameWithoutExtension(exe) : null,
            }).ToList();
            var target = Path.Combine(_legacyDirectory, "history.json");
            var temp = target + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(mirror, JsonOptions));
            File.Move(temp, target, overwrite: true);
        }
        catch (Exception)
        {
            // the mirror is a courtesy
        }
    }

    public void Dispose()
    {
        Flush(synchronous: true);
        _saveTimer.Stop();
    }

    private void Load(string legacyDirectory)
    {
        try
        {
            if (File.Exists(_indexPath) && new FileInfo(_indexPath).Length <= ClipLimits.MaxEncodedBytes)
            {
                var loaded = JsonSerializer.Deserialize<List<ClipEntry>>(File.ReadAllText(_indexPath), JsonOptions) ?? [];
                foreach (var entry in loaded)
                {
                    if (entry.IsImage && (entry.ImageFile is null || !File.Exists(ImagePath(entry))))
                    {
                        continue;
                    }

                    if (entry.IsText && string.IsNullOrEmpty(entry.Text))
                    {
                        continue;
                    }

                    if (entry.IsFiles && entry.FilePaths.Count == 0)
                    {
                        continue;
                    }

                    _entries.Add(entry);
                }

                NormalizeOrder();
                CleanupOrphans();
                return;
            }

            MigrateLegacy(legacyDirectory);
        }
        catch (Exception ex)
        {
            _log.Error("Could not read clipboard history; starting empty", ex);
            _entries = [];
        }
    }

    /// <summary>Imports the history of the first Trayloom versions (text and PNG items) once.</summary>
    private void MigrateLegacy(string legacyDirectory)
    {
        try
        {
            var old = Path.Combine(legacyDirectory, "history.json");
            if (!File.Exists(old))
            {
                return;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(old));
            var imported = new List<ClipEntry>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var kind = item.TryGetProperty("Kind", out var k) ? k.GetString() : "text";
                var created = item.TryGetProperty("CreatedUtc", out var c) && c.TryGetDateTime(out var dt) ? dt : DateTime.UtcNow;
                var pinned = item.TryGetProperty("Pinned", out var p) && p.ValueKind == JsonValueKind.True;
                var entry = new ClipEntry { CopiedAt = created, PinnedAt = pinned ? created : null };
                if (kind == "image")
                {
                    var file = item.TryGetProperty("ImageFile", out var f) ? f.GetString() : null;
                    var source = file is null ? null : Path.Combine(legacyDirectory, "images", file);
                    if (source is null || !File.Exists(source))
                    {
                        continue;
                    }

                    var bytes = File.ReadAllBytes(source);
                    var name = Guid.NewGuid().ToString("N") + ".png";
                    File.WriteAllBytes(Path.Combine(ImageDirectory, name), bytes);
                    entry.Kind = ClipKinds.Image;
                    entry.ImageFile = name;
                    entry.ImageHash = Sha256(bytes);
                    entry.ImageWidth = item.TryGetProperty("Width", out var w) ? w.GetInt32() : 0;
                    entry.ImageHeight = item.TryGetProperty("Height", out var h) ? h.GetInt32() : 0;
                }
                else
                {
                    entry.Text = item.TryGetProperty("Text", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                    if (string.IsNullOrWhiteSpace(entry.Text))
                    {
                        continue;
                    }
                }

                entry.SourceExe = item.TryGetProperty("Source", out var s) && s.GetString() is { Length: > 0 } name1 ? name1.ToLowerInvariant() + ".exe" : null;
                imported.Add(entry);
            }

            _entries = [.. imported.OrderByDescending(e => e.IsPinned).ThenByDescending(e => e.CopiedAt)];
            _dirty = true;
        }
        catch (Exception ex)
        {
            _log.Warn($"Could not import the old clipboard history: {ex.Message}");
        }
    }

    private void CleanupOrphans()
    {
        var known = _entries.Where(e => e.IsImage).Select(e => e.ImageFile).ToHashSet();
        foreach (var file in Directory.EnumerateFiles(ImageDirectory))
        {
            if (!known.Contains(Path.GetFileName(file)))
            {
                TryDelete(file);
            }
        }
    }

    private void DeleteFiles(ClipEntry entry)
    {
        if (entry.IsImage && entry.ImageFile is not null && !_entries.Any(e => e != entry && e.ImageFile == entry.ImageFile))
        {
            TryDelete(ImagePath(entry));
        }
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
}
