// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Collections.Concurrent;
using System.Windows.Media.Imaging;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

/// <summary>One picture of the gallery.</summary>
/// <param name="Path">Full path (also the id).</param>
/// <param name="Title">File name without extension / theme name.</param>
/// <param name="IsOwn">Added by the user (shown under "Your pictures").</param>
internal sealed record WallpaperEntry(string Path, string Title, bool IsOwn);

/// <summary>An added source (a file or a folder) for the remove strip.</summary>
internal sealed record WallpaperSource(string Path, bool IsFolder, bool Unreachable);

/// <summary>The still-image catalogue, source management, thumbnails and apply logic (spec 6.6.6).</summary>
internal sealed class WallpaperService
{
    public const int PageSize = 24;
    private const int ThumbCacheLimit = 400;
    private static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic"];
    private static readonly ConcurrentDictionary<string, BitmapSource> Thumbs = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentQueue<string> ThumbOrder = new();

    private readonly IContributionServices _ctx;
    private List<WallpaperEntry> _apple = [];
    private bool _appleLoaded;
    private int _scanToken;
    private int _applyToken;
    private int _viewers;

    public WallpaperService(IContributionServices ctx) => _ctx = ctx;

    /// <summary>The service of the running feature (null while uninstalled).</summary>
    public static WallpaperService? Instance { get; set; }

    public IReadOnlyList<WallpaperEntry> Entries { get; private set; } = [];

    public IReadOnlyList<WallpaperSource> Sources { get; private set; } = [];

    public bool Loading { get; private set; }

    public bool Downloading { get; private set; }

    public string? Error { get; private set; }

    public string? AppliedPath { get; private set; }

    public int Generation { get; private set; }

    /// <summary>Raised on the UI thread when the catalogue or the status changed.</summary>
    public event Action? Changed;

    public string Filter
    {
        get => _ctx.App.Get<string>("wallpaperFilter") is "own" or "apple" ? _ctx.App.Get<string>("wallpaperFilter")! : "all";
        set => _ctx.App.Set("wallpaperFilter", value is "own" or "apple" ? value : "all");
    }

    public IEnumerable<WallpaperEntry> Filtered => Filter switch
    {
        "own" => Entries.Where(e => e.IsOwn),
        "apple" => Entries.Where(e => !e.IsOwn),
        _ => Entries,
    };

    // ---------------------------------------------------------------- viewers

    public void ViewerAdded()
    {
        _viewers++;
        _ = RefreshAsync();
    }

    public void ViewerRemoved()
    {
        _viewers = Math.Max(0, _viewers - 1);
        if (_viewers == 0)
        {
            _scanToken++;
            Generation++;
        }
    }

    /// <summary>Drops the in-memory catalogue and thumbnails (uninstall).</summary>
    public void Clear()
    {
        _scanToken++;
        Generation++;
        Entries = [];
        Thumbs.Clear();
        while (ThumbOrder.TryDequeue(out _))
        {
        }
    }

    // ---------------------------------------------------------------- catalogue

    public async Task RefreshAsync()
    {
        var token = ++_scanToken;
        Loading = Entries.Count == 0;
        Changed?.Invoke();
        var own = OwnPaths();
        var excluded = Excluded();
        var needApple = !_appleLoaded;
        var result = await Task.Run(() =>
        {
            var apple = needApple ? ScanWindows() : _apple;
            var sources = own.Select(p => new WallpaperSource(p, Directory.Exists(p), !Directory.Exists(p) && !File.Exists(p))).ToList();
            var ownEntries = new List<WallpaperEntry>();
            foreach (var source in sources)
            {
                if (source.Unreachable)
                {
                    continue;
                }

                if (source.IsFolder)
                {
                    foreach (var file in ScanFolder(source.Path).Where(f => !excluded.Contains(f)))
                    {
                        ownEntries.Add(new WallpaperEntry(file, System.IO.Path.GetFileNameWithoutExtension(file), true));
                    }
                }
                else if (IsImage(source.Path) && !excluded.Contains(source.Path))
                {
                    ownEntries.Add(new WallpaperEntry(source.Path, System.IO.Path.GetFileNameWithoutExtension(source.Path), true));
                }
            }

            var merged = apple.Concat(ownEntries)
                .GroupBy(e => e.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                .OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
            return (apple, sources, merged);
        });
        if (token != _scanToken)
        {
            return;
        }

        _apple = result.apple;
        _appleLoaded = true;
        Sources = result.sources;
        Entries = result.merged;
        Loading = false;
        if (AppliedPath is null)
        {
            var current = WallpaperApi.Current(null);
            AppliedPath = Entries.FirstOrDefault(e => e.Path.Equals(current, StringComparison.OrdinalIgnoreCase))?.Path;
        }

        Changed?.Invoke();
    }

    private static bool IsImage(string path) => Extensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant());

    private static List<WallpaperEntry> ScanWindows()
    {
        var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var result = new Dictionary<string, WallpaperEntry>(StringComparer.OrdinalIgnoreCase);
        void Scan(string root, bool preferLargest)
        {
            if (!Directory.Exists(root))
            {
                return;
            }

            try
            {
                foreach (var group in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                    .GroupBy(f => System.IO.Path.GetDirectoryName(f)!))
                {
                    var theme = new DirectoryInfo(group.Key).Name;
                    var files = preferLargest ? group.OrderByDescending(f => new FileInfo(f).Length).Take(1) : group.OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
                    var n = 0;
                    foreach (var file in files)
                    {
                        n++;
                        var title = n == 1 ? theme : $"{theme} {n}";
                        // 4K variants replace the standard picture of the same theme.
                        result[title] = new WallpaperEntry(file, title, false);
                    }
                }
            }
            catch
            {
                // unreadable folder: skip
            }
        }

        Scan(System.IO.Path.Combine(windir, "Web", "Wallpaper"), false);
        Scan(System.IO.Path.Combine(windir, "Web", "Screen"), false);
        Scan(System.IO.Path.Combine(windir, "Web", "4K", "Wallpaper"), true);
        return [.. result.Values];
    }

    private static IEnumerable<string> ScanFolder(string folder)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint };
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder, "*.*", options).Where(IsImage).OrderBy(f => System.IO.Path.GetFileName(f), StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        catch
        {
            files = [];
        }

        return files;
    }

    // ---------------------------------------------------------------- own sources

    public List<string> OwnPaths() => _ctx.App.Get<List<string>>("wallpaperOwnBookmarks", []) ?? [];

    private HashSet<string> Excluded() => new(_ctx.App.Get<List<string>>("wallpaperExcludedOwnPaths", []) ?? [], StringComparer.OrdinalIgnoreCase);

    public void AddSources(IEnumerable<string> paths)
    {
        var own = OwnPaths();
        var excluded = Excluded();
        foreach (var path in paths)
        {
            if (!Directory.Exists(path) && !(File.Exists(path) && IsImage(path)))
            {
                continue;
            }

            own.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
            own.Add(path);
            excluded.RemoveWhere(e => e.Equals(path, StringComparison.OrdinalIgnoreCase) || e.StartsWith(path.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
        }

        _ctx.App.Set("wallpaperOwnBookmarks", own);
        _ctx.App.Set("wallpaperExcludedOwnPaths", excluded.OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToList());
        _ = RefreshAsync();
    }

    public void RemoveSource(string path)
    {
        var own = OwnPaths();
        own.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
        _ctx.App.Set("wallpaperOwnBookmarks", own);
        var excluded = Excluded();
        excluded.RemoveWhere(e => e.StartsWith(path.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
        _ctx.App.Set("wallpaperExcludedOwnPaths", excluded.OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToList());
        _ = RefreshAsync();
    }

    /// <summary>The "x" badge on an own picture: removes a file source, or hides a picture found through a folder.</summary>
    public void RemoveImage(string path)
    {
        var own = OwnPaths();
        if (own.Any(p => p.Equals(path, StringComparison.OrdinalIgnoreCase)))
        {
            RemoveSource(path);
            return;
        }

        var excluded = Excluded();
        excluded.Add(path);
        _ctx.App.Set("wallpaperExcludedOwnPaths", excluded.OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToList());
        _ = RefreshAsync();
    }

    // ---------------------------------------------------------------- thumbnails

    /// <summary>Decodes a 160 px thumbnail off the UI thread (cached, 400 items).</summary>
    public async Task<BitmapSource?> ThumbnailAsync(string path, int generation)
    {
        if (Thumbs.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var image = await Task.Run<BitmapSource?>(() =>
        {
            if (generation != Generation)
            {
                return null;
            }

            try
            {
                var attrs = File.GetAttributes(path);
                if ((attrs & (FileAttributes)0x400000) != 0 || (attrs & FileAttributes.Offline) != 0)
                {
                    return null; // cloud placeholder: do not read it
                }

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path);
                bitmap.DecodePixelWidth = 160;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        });
        if (image is not null)
        {
            Thumbs[path] = image;
            ThumbOrder.Enqueue(path);
            while (Thumbs.Count > ThumbCacheLimit && ThumbOrder.TryDequeue(out var old))
            {
                Thumbs.TryRemove(old, out _);
            }
        }

        return image;
    }

    // ---------------------------------------------------------------- apply

    public async Task ApplyAsync(WallpaperEntry entry)
    {
        Error = null;
        var token = ++_applyToken;
        var allDisplays = _ctx.App.Get("wallpaperApplyAllDisplays", true);
        string? monitor = null;
        if (!allDisplays)
        {
            var cursor = Screens.CursorPosition();
            monitor = WallpaperApi.MonitorAt((int)cursor.X, (int)cursor.Y);
            if (monitor is null)
            {
                Error = Loc.T("Could not set the wallpaper");
                Changed?.Invoke();
                return;
            }
        }

        if (IsPlaceholder(entry.Path))
        {
            Downloading = true;
            Changed?.Invoke();
            var ok = await Hydrate(entry.Path);
            Downloading = false;
            if (token != _applyToken)
            {
                return;
            }

            if (!ok)
            {
                Error = Loc.T("Could not download the wallpaper");
                Changed?.Invoke();
                return;
            }
        }

        try
        {
            await Task.Run(() => WallpaperApi.Apply(entry.Path, monitor));
            if (token != _applyToken)
            {
                return;
            }

            AppliedPath = entry.Path;
        }
        catch (Exception ex)
        {
            _ctx.Log.Warn("Wallpaper failed: " + ex.Message);
            Error = Loc.T("Could not set the wallpaper");
        }

        Changed?.Invoke();
    }

    private static bool IsPlaceholder(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            return (attrs & (FileAttributes)0x400000) != 0 || (attrs & FileAttributes.Offline) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> Hydrate(string path)
    {
        var read = Task.Run(() =>
        {
            try
            {
                using var stream = File.OpenRead(path);
                stream.CopyTo(Stream.Null);
            }
            catch
            {
                // polled below
            }
        });
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            if (!IsPlaceholder(path))
            {
                return true;
            }

            if (read.IsCompleted && IsPlaceholder(path))
            {
                return false;
            }

            await Task.Delay(250);
        }

        return false;
    }
}
