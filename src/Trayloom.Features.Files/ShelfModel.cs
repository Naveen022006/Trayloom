// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Trayloom.Core;
using IDataObject = System.Windows.IDataObject;

namespace Trayloom.Features.Files;

internal static class ShelfKinds
{
    public const string File = "file";
    public const string Image = "image";
    public const string Text = "text";
    public const string Link = "link";
    public const string Pile = "pile";
}

/// <summary>One thing on the shelf: a file or folder (kept as a link), a pasted image, a note, a link, or a pile of those.</summary>
internal sealed class ShelfItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Kind { get; set; } = ShelfKinds.File;

    public string? Path { get; set; }

    public string? Text { get; set; }

    public string? Url { get; set; }

    /// <summary>PNG (or GIF) file name inside the shelf's image folder.</summary>
    public string? ImageFile { get; set; }

    public bool Pinned { get; set; }

    public List<ShelfItem> Children { get; set; } = [];

    [JsonIgnore]
    public bool IsPile => Kind == ShelfKinds.Pile;

    /// <summary>Leaves of this item (the item itself unless it is a pile).</summary>
    public IEnumerable<ShelfItem> Leaves()
    {
        if (!IsPile)
        {
            yield return this;
            yield break;
        }

        foreach (var child in Children)
        {
            foreach (var leaf in child.Leaves())
            {
                yield return leaf;
            }
        }
    }

    public int LeafCount => Leaves().Count();

    public int Depth => IsPile ? 1 + (Children.Count == 0 ? 0 : Children.Max(c => c.Depth)) : 0;
}

/// <summary>The shelf contents with selection, piles and persistence (JSON in the feature data folder; images next to it).</summary>
internal sealed class ShelfStore
{
    public const int MaxLeaves = 200;
    public const int MaxDepth = 4;
    public const int MaxTextChars = 200_000;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly string _indexPath;
    private readonly ILog _log;
    private readonly DispatcherTimer _saveTimer;
    private List<ShelfItem> _items = [];
    private bool _dirty;

    public ShelfStore(string dataDirectory, ILog log, Dispatcher ui)
    {
        _log = log;
        Directory.CreateDirectory(dataDirectory);
        ImageDirectory = System.IO.Path.Combine(dataDirectory, "ShelfFiles");
        Directory.CreateDirectory(ImageDirectory);
        _indexPath = System.IO.Path.Combine(dataDirectory, "items.json");
        Load();
        _saveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(600), DispatcherPriority.Background, (_, _) => Flush(), ui);
        _saveTimer.Stop();
    }

    public string ImageDirectory { get; }

    public IReadOnlyList<ShelfItem> Items => _items;

    public HashSet<string> Selection { get; } = [];

    public HashSet<string> Expanded { get; } = [];

    public string? AnchorId { get; set; }

    public string? RevealId { get; private set; }

    public int AddSerial { get; private set; }

    public int LeafCount => _items.Sum(i => i.LeafCount);

    /// <summary>Raised (UI thread) after any change of the contents.</summary>
    public event Action? Changed;

    /// <summary>Raised when only the selection changed.</summary>
    public event Action? SelectionChanged;

    public string ImagePath(ShelfItem item) => System.IO.Path.Combine(ImageDirectory, item.ImageFile ?? string.Empty);

    // ---------------------------------------------------------------------------------------------- adding

    public int AddFiles(IEnumerable<string> paths)
    {
        var added = 0;
        foreach (var path in paths)
        {
            if (!(File.Exists(path) || Directory.Exists(path)) || ContainsPath(path))
            {
                continue;
            }

            Append(new ShelfItem { Kind = ShelfKinds.File, Path = path });
            added++;
        }

        if (added > 0)
        {
            Touch();
        }

        return added;
    }

    public bool AddImage(byte[] bytes, string extension = ".png")
    {
        try
        {
            var name = Guid.NewGuid().ToString("N") + extension;
            File.WriteAllBytes(System.IO.Path.Combine(ImageDirectory, name), bytes);
            Append(new ShelfItem { Kind = ShelfKinds.Image, ImageFile = name });
            Touch();
            return true;
        }
        catch (Exception ex)
        {
            _log.Warn($"Could not keep an image on the shelf: {ex.Message}");
            return false;
        }
    }

    public bool AddText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (text.Length > MaxTextChars)
        {
            text = text[..MaxTextChars];
        }

        Append(new ShelfItem { Kind = ShelfKinds.Text, Text = text });
        Touch();
        return true;
    }

    public bool AddLink(string url)
    {
        Append(new ShelfItem { Kind = ShelfKinds.Link, Url = url.Trim() });
        Touch();
        return true;
    }

    /// <summary>Takes what a drop or a paste carries: files first, then an image, a link, a note.</summary>
    public int AddFromData(IDataObject data)
    {
        try
        {
            if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                var added = AddFiles(files);
                if (added > 0)
                {
                    return added;
                }
            }

            if (TryVirtualFile(data))
            {
                return 1;
            }

            var image = ReadImage(data);
            if (image is not null && AddImage(image))
            {
                return 1;
            }

            var link = ReadLink(data);
            if (link is not null)
            {
                AddLink(link);
                return 1;
            }

            if ((data.GetDataPresent(DataFormats.UnicodeText) || data.GetDataPresent(DataFormats.Text)) && data.GetData(DataFormats.UnicodeText) is string text && AddText(text))
            {
                return 1;
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"Shelf drop failed: {ex.Message}");
        }

        return 0;
    }

    private bool TryVirtualFile(IDataObject data)
    {
        // A dragged attachment or browser file arrives as FileGroupDescriptorW + FileContents (a "file promise").
        try
        {
            if (!data.GetDataPresent("FileGroupDescriptorW") || !data.GetDataPresent("FileContents"))
            {
                return false;
            }

            if (data.GetData("FileGroupDescriptorW") is not MemoryStream descriptor || data.GetData("FileContents") is not MemoryStream contents)
            {
                return false;
            }

            var bytes = descriptor.ToArray();
            // FILEGROUPDESCRIPTORW: cItems (4) then FILEDESCRIPTORW (cFileName at offset 72 within each 592 byte record)
            if (bytes.Length < 4 + 592 || BitConverter.ToInt32(bytes, 0) < 1)
            {
                return false;
            }

            var name = System.Text.Encoding.Unicode.GetString(bytes, 4 + 72, 520).Split('\0')[0];
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            name = string.Concat(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            contents.Position = 0;
            var target = ShelfDrops.WriteDropped(ImageDirectory, Guid.NewGuid().ToString("N").Substring(0, 8) + "_" + name, contents);
            if (target is null)
            {
                return false;
            }

            Append(new ShelfItem { Kind = ShelfKinds.File, Path = target });
            Touch();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static byte[]? ReadImage(IDataObject data)
    {
        try
        {
            if (data.GetDataPresent("PNG") && data.GetData("PNG") is MemoryStream png && png.Length > 8)
            {
                return png.ToArray();
            }

            if (data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is BitmapSource bitmap)
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = new MemoryStream();
                encoder.Save(stream);
                return stream.ToArray();
            }

            if (data.GetDataPresent(DataFormats.Dib) && data.GetData(DataFormats.Dib) is MemoryStream dib)
            {
                return ClipboardIO.DibToPng(dib.ToArray());
            }
        }
        catch (Exception)
        {
            // not an image after all
        }

        return null;
    }

    internal static string? ReadLink(IDataObject data)
    {
        try
        {
            if (data.GetDataPresent("UniformResourceLocatorW") && data.GetData("UniformResourceLocatorW") is MemoryStream w)
            {
                var url = System.Text.Encoding.Unicode.GetString(w.ToArray()).TrimEnd('\0').Trim();
                if (SafeLinks.IsWebUrl(url, out _, allowMailto: true))
                {
                    return url;
                }
            }

            if (data.GetDataPresent("UniformResourceLocator") && data.GetData("UniformResourceLocator") is MemoryStream a)
            {
                var url = System.Text.Encoding.Default.GetString(a.ToArray()).TrimEnd('\0').Trim();
                if (SafeLinks.IsWebUrl(url, out _, allowMailto: true))
                {
                    return url;
                }
            }

            if (data.GetDataPresent(DataFormats.UnicodeText) && data.GetData(DataFormats.UnicodeText) is string text)
            {
                var t = text.Trim();
                if (SafeLinks.IsWebUrl(t, out _, allowMailto: false))
                {
                    return t;
                }
            }
        }
        catch (Exception)
        {
            // fall through
        }

        return null;
    }

    private bool ContainsPath(string path) =>
        _items.SelectMany(i => i.Leaves()).Any(l => l.Kind == ShelfKinds.File && string.Equals(l.Path, path, StringComparison.OrdinalIgnoreCase));

    private void Append(ShelfItem item)
    {
        _items.Add(item);
        RevealId = item.Id;
        AddSerial++;
    }

    // ---------------------------------------------------------------------------------------------- selection

    public void Select(string id)
    {
        Selection.Clear();
        Selection.Add(id);
        AnchorId = id;
        SelectionChanged?.Invoke();
    }

    public void ToggleSelect(string id)
    {
        if (!Selection.Add(id))
        {
            Selection.Remove(id);
        }

        AnchorId = id;
        SelectionChanged?.Invoke();
    }

    public void ExtendSelect(string id, IReadOnlyList<string> visibleOrder)
    {
        var anchor = AnchorId is null ? -1 : visibleOrder.ToList().IndexOf(AnchorId);
        var target = visibleOrder.ToList().IndexOf(id);
        if (anchor < 0 || target < 0)
        {
            Select(id);
            return;
        }

        var (from, to) = anchor <= target ? (anchor, target) : (target, anchor);
        Selection.Clear();
        for (var i = from; i <= to; i++)
        {
            Selection.Add(visibleOrder[i]);
        }

        SelectionChanged?.Invoke();
    }

    public void ClearSelection()
    {
        if (Selection.Count == 0)
        {
            return;
        }

        Selection.Clear();
        SelectionChanged?.Invoke();
    }

    // ---------------------------------------------------------------------------------------------- editing

    public ShelfItem? Find(string id) => _items.SelectMany(Flatten).FirstOrDefault(i => i.Id == id);

    private static IEnumerable<ShelfItem> Flatten(ShelfItem item)
    {
        yield return item;
        foreach (var child in item.Children)
        {
            foreach (var inner in Flatten(child))
            {
                yield return inner;
            }
        }
    }

    public void TogglePin(string id)
    {
        if (Find(id) is { } item)
        {
            item.Pinned = !item.Pinned;
            Touch();
        }
    }

    /// <summary>Removes items (a pinned one only when <paramref name="includePinned"/>).</summary>
    public void Remove(IEnumerable<string> ids, bool includePinned = true)
    {
        var set = ids.ToHashSet();
        var changed = false;
        foreach (var item in _items.ToList())
        {
            changed |= RemoveFrom(_items, item, set, includePinned);
        }

        foreach (var id in set)
        {
            Selection.Remove(id);
        }

        if (changed)
        {
            Touch();
        }
    }

    private bool RemoveFrom(List<ShelfItem> list, ShelfItem item, HashSet<string> ids, bool includePinned)
    {
        if (ids.Contains(item.Id))
        {
            if (item.Pinned && !includePinned)
            {
                return false;
            }

            list.Remove(item);
            foreach (var leaf in item.Leaves().Where(l => l.Kind == ShelfKinds.Image))
            {
                DeleteImage(leaf);
            }

            // dropped copies are not deleted here: the app that took the drop may still be reading the file (see SweepUnreferenced)
            return true;
        }

        var changed = false;
        foreach (var child in item.Children.ToList())
        {
            changed |= RemoveFrom(item.Children, child, ids, includePinned);
        }

        if (item.IsPile && item.Children.Count <= 1 && changed)
        {
            // A pile of one dissolves.
            var index = list.IndexOf(item);
            list.RemoveAt(index);
            list.InsertRange(index, item.Children);
        }

        return changed;
    }

    /// <summary>"Clear all": everything but pinned items (a pinned pile protects its contents).</summary>
    public void ClearAll()
    {
        var keep = _items.Where(i => i.Pinned).ToList();
        foreach (var item in _items.Except(keep))
        {
            foreach (var leaf in item.Leaves().Where(l => l.Kind == ShelfKinds.Image))
            {
                DeleteImage(leaf);
            }
        }

        _items = keep;
        Selection.Clear();
        Touch();
    }

    /// <summary>Drops the dragged items onto a tile: both become one pile (counted leaves, depth and size limits apply).</summary>
    public bool MakePile(IEnumerable<string> draggedIds, string targetId)
    {
        var dragged = draggedIds.Where(id => id != targetId).Select(id => _items.FirstOrDefault(i => i.Id == id)).OfType<ShelfItem>().ToList();
        var target = _items.FirstOrDefault(i => i.Id == targetId);
        if (target is null || dragged.Count == 0)
        {
            return false;
        }

        var leaves = dragged.Sum(d => d.LeafCount) + target.LeafCount;
        var depth = Math.Max(target.Depth, dragged.Max(d => d.Depth)) + 1;
        if (leaves > MaxLeaves || depth > MaxDepth)
        {
            return false;
        }

        foreach (var item in dragged)
        {
            _items.Remove(item);
        }

        if (target.IsPile)
        {
            target.Children.AddRange(dragged);
        }
        else
        {
            var pile = new ShelfItem { Kind = ShelfKinds.Pile, Children = [target, .. dragged], Pinned = target.Pinned };
            _items[_items.IndexOf(target)] = pile;
            Selection.Remove(target.Id);
        }

        foreach (var item in dragged)
        {
            Selection.Remove(item.Id);
        }

        Touch();
        return true;
    }

    public void ToggleExpanded(string id)
    {
        if (!Expanded.Add(id))
        {
            Expanded.Remove(id);
        }

        Touch(save: false);
    }

    /// <summary>Items as shown: expanded piles are followed by their children.</summary>
    public IEnumerable<ShelfItem> Visible()
    {
        foreach (var item in _items)
        {
            yield return item;
            if (item.IsPile && Expanded.Contains(item.Id))
            {
                foreach (var child in item.Children)
                {
                    yield return child;
                }
            }
        }
    }

    // ---------------------------------------------------------------------------------------------- drag out

    /// <summary>Leaves behind the given top-level / expanded ids (a pile stands for all its leaves).</summary>
    public List<ShelfItem> LeavesOf(IEnumerable<string> ids) =>
        [.. ids.Select(Find).OfType<ShelfItem>().SelectMany(i => i.Leaves()).DistinctBy(i => i.Id)];

    /// <summary>Builds the data a drag out carries: files (real files and stored images), plus links/notes as text.</summary>
    public DataObject BuildDragData(IEnumerable<string> ids, out bool allowsMove)
    {
        var leaves = LeavesOf(ids);
        var data = new DataObject();
        var files = new List<string>();
        var texts = new List<string>();
        allowsMove = false;
        foreach (var leaf in leaves)
        {
            switch (leaf.Kind)
            {
                case ShelfKinds.File when leaf.Path is not null && (File.Exists(leaf.Path) || Directory.Exists(leaf.Path)):
                    files.Add(leaf.Path);
                    allowsMove = true;
                    break;
                case ShelfKinds.Image when File.Exists(ImagePath(leaf)):
                    files.Add(ImagePath(leaf));
                    break;
                case ShelfKinds.Link when leaf.Url is not null:
                    texts.Add(leaf.Url);
                    break;
                case ShelfKinds.Text when leaf.Text is not null:
                    texts.Add(leaf.Text);
                    break;
            }
        }

        if (files.Count > 0)
        {
            data.SetData(DataFormats.FileDrop, files.ToArray());
        }

        if (texts.Count > 0 && files.Count == 0)
        {
            data.SetText(string.Join(Environment.NewLine, texts));
        }

        data.SetData("Trayloom.ShelfItems", string.Join(",", ids));
        return data;
    }

    public List<string> FilePathsForActions(IEnumerable<string>? ids = null)
    {
        var source = ids is null ? _items.SelectMany(i => i.Leaves()) : LeavesOf(ids);
        var list = new List<string>();
        foreach (var leaf in source)
        {
            if (leaf.Kind == ShelfKinds.File && leaf.Path is not null && (File.Exists(leaf.Path) || Directory.Exists(leaf.Path)))
            {
                list.Add(leaf.Path);
            }
            else if (leaf.Kind == ShelfKinds.Image && File.Exists(ImagePath(leaf)))
            {
                list.Add(ImagePath(leaf));
            }
        }

        return list;
    }

    // ---------------------------------------------------------------------------------------------- persistence

    private void Touch(bool save = true)
    {
        if (save)
        {
            _dirty = true;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        // Selection never keeps ids that disappeared.
        var ids = _items.SelectMany(Flatten).Select(i => i.Id).ToHashSet();
        Selection.RemoveWhere(id => !ids.Contains(id));
        Changed?.Invoke();
    }

    public void Flush()
    {
        _saveTimer.Stop();
        if (!_dirty)
        {
            return;
        }

        _dirty = false;
        try
        {
            var temp = _indexPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_items, JsonOptions));
            File.Move(temp, _indexPath, overwrite: true);
        }
        catch (Exception ex)
        {
            _log.Error("Could not save the shelf", ex);
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

            var loaded = JsonSerializer.Deserialize<List<ShelfItem>>(File.ReadAllText(_indexPath), JsonOptions) ?? [];
            _items = loaded.Where(i => i.Kind == ShelfKinds.Pile || i.Kind != ShelfKinds.Image || File.Exists(ImagePath(i))).ToList();
        }
        catch (Exception ex)
        {
            _log.Warn($"Could not read the shelf, starting empty: {ex.Message}");
            _items = [];
        }
    }

    /// <summary>
    /// Deletes files in the shelf's own folder that no item refers to any more and that are older than <paramref name="minAge"/>.
    /// A dropped copy outlives its item for a while because the receiving app (a browser upload, an Electron app) may read it later.
    /// </summary>
    public void SweepUnreferenced(TimeSpan minAge)
    {
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var leaf in _items.SelectMany(Flatten))
        {
            if (leaf.Kind == ShelfKinds.File && leaf.Path is not null)
            {
                referenced.Add(SafeFull(leaf.Path));
            }
            else if (leaf.Kind == ShelfKinds.Image && leaf.ImageFile is not null)
            {
                referenced.Add(SafeFull(ImagePath(leaf)));
            }
        }

        var dir = ImageDirectory;
        _ = Task.Run(() => ShelfDrops.SweepUnreferenced(dir, referenced, minAge));
    }

    private static string SafeFull(string path)
    {
        try
        {
            return System.IO.Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return path;
        }
    }

    private void DeleteImage(ShelfItem leaf)
    {
        try
        {
            var path = ImagePath(leaf);
            if (File.Exists(path) && !_items.SelectMany(Flatten).Any(i => i != leaf && i.ImageFile == leaf.ImageFile))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // best effort
        }
    }
}

/// <summary>Real file thumbnails through IShellItemImageFactory (images, videos, documents), loaded on one STA worker.</summary>
internal static class ShellThumbs
{
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out nint hbitmap);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public nint bmBits;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, nint bindingContext, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(nint hObject, int count, out BITMAP bitmap);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint hObject);

    private static readonly System.Collections.Concurrent.BlockingCollection<Action> Queue = [];
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static Thread? _worker;

    public static void Load(string path, int size, Action<ImageSource?> done)
    {
        var key = path + "|" + size;
        if (Cache.TryGetValue(key, out var hit))
        {
            done(hit);
            return;
        }

        var dispatcher = System.Windows.Application.Current.Dispatcher;
        EnsureWorker();
        Queue.Add(() =>
        {
            var image = Render(path, size);
            dispatcher.BeginInvoke(() =>
            {
                Cache[key] = image;
                done(image);
            });
        });
    }

    private static void EnsureWorker()
    {
        if (_worker is not null)
        {
            return;
        }

        _worker = new Thread(() =>
        {
            foreach (var job in Queue.GetConsumingEnumerable())
            {
                try
                {
                    job();
                }
                catch (Exception)
                {
                    // thumbnail providers can throw
                }
            }
        })
        { IsBackground = true, Name = "Trayloom.ShelfThumbs" };
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
    }

    private static ImageSource? Render(string path, int size)
    {
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(path, 0, ref iid, out var factory);
            // SIIGBF_BIGGERSIZEOK (1) | SIIGBF_THUMBNAILONLY is not set: icons are fine as a fallback.
            if (factory.GetImage(new SIZE { cx = size, cy = size }, 0x1, out var hbitmap) != 0 || hbitmap == 0)
            {
                return null;
            }

            try
            {
                GetObject(hbitmap, Marshal.SizeOf<BITMAP>(), out var info);
                if (info.bmBits == 0 || info.bmBitsPixel != 32)
                {
                    var plain = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(hbitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    plain.Freeze();
                    return plain;
                }

                var stride = info.bmWidthBytes;
                var bytes = new byte[stride * info.bmHeight];
                Marshal.Copy(info.bmBits, bytes, 0, bytes.Length);
                var bitmap = BitmapSource.Create(info.bmWidth, info.bmHeight, 96, 96, PixelFormats.Pbgra32, null, bytes, stride);
                // Bottom-up DIBs come back flipped when the height is positive: the factory returns top-down for 32 bit.
                bitmap.Freeze();
                return bitmap;
            }
            finally
            {
                DeleteObject(hbitmap);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>Files that arrive on the shelf as a promise (a browser download, a mail attachment) and the checks around keeping them.</summary>
internal static class ShelfDrops
{
    /// <summary>The largest dropped file Trayloom keeps (it is copied into the shelf folder).</summary>
    public const long MaxBytes = 512L * 1024 * 1024;

    private static readonly HashSet<string> s_risky = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".bat", ".cmd", ".ps1", ".psm1", ".js", ".jse", ".vbs", ".vbe", ".wsf", ".wsh", ".hta", ".lnk", ".url", ".scr", ".pif",
        ".msi", ".msix", ".msixbundle", ".appx", ".appinstaller", ".application", ".reg", ".cpl", ".jar", ".chm", ".iso", ".vhd", ".vhdx",
        ".library-ms", ".settingcontent-ms",
    };

    /// <summary>True for a file type that runs code or mounts something when opened.</summary>
    public static bool IsRisky(string? path)
    {
        // Windows drops a trailing dot or space from a name, so "x.exe." is x.exe
        var ext = string.IsNullOrEmpty(path) ? string.Empty : System.IO.Path.GetExtension(path.TrimEnd('.', ' '));
        return ext.Length > 0 && s_risky.Contains(ext);
    }

    /// <summary>Deletes unreferenced files older than <paramref name="minAge"/> directly inside <paramref name="dir"/>; returns how many went.</summary>
    internal static int SweepUnreferenced(string dir, ISet<string> referencedFullPaths, TimeSpan minAge)
    {
        var removed = 0;
        try
        {
            if (!Directory.Exists(dir))
            {
                return 0;
            }

            var cutoff = DateTime.UtcNow - minAge;
            foreach (var file in new DirectoryInfo(dir).EnumerateFiles())
            {
                try
                {
                    var newest = file.LastWriteTimeUtc > file.CreationTimeUtc ? file.LastWriteTimeUtc : file.CreationTimeUtc;
                    if (newest < cutoff && !referencedFullPaths.Contains(file.FullName))
                    {
                        file.Delete();
                        removed++;
                    }
                }
                catch (Exception)
                {
                    // in use or gone
                }
            }
        }
        catch (Exception)
        {
            // housekeeping only
        }

        return removed;
    }

    /// <summary>True when <paramref name="path"/> lies inside <paramref name="dir"/>.</summary>
    public static bool IsUnder(string dir, string path)
    {
        try
        {
            var root = System.IO.Path.GetFullPath(dir).TrimEnd('\\') + "\\";
            return System.IO.Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Writes a dropped file into <paramref name="dir"/> without replacing anything, then marks it as coming from the internet
    /// (<c>Zone.Identifier</c>, zone 3) so Windows warns before running it. Returns the path, or null when the file is too large,
    /// the name is taken, or the mark could not be written for a risky file type.
    /// </summary>
    internal static string? WriteDropped(string dir, string name, Stream contents)
    {
        var safeName = System.IO.Path.GetFileName(name).TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(safeName) || safeName.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
        {
            return null;
        }

        // the same normalised path is used to write, to mark and to classify, so no name spelling skips the Zone.Identifier
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, safeName));
        var created = false;
        try
        {
            if (contents.CanSeek && contents.Length - contents.Position > MaxBytes)
            {
                return null;
            }

            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = contents.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += read;
                    if (total > MaxBytes)
                    {
                        throw new InvalidDataException("dropped file is too large");
                    }

                    output.Write(buffer, 0, read);
                }
            }

            try
            {
                File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
            }
            catch (Exception)
            {
                // FAT and exFAT have no alternate data streams; a risky type is not kept without the mark.
                if (IsRisky(path))
                {
                    TryDelete(path);
                    return null;
                }
            }

            return path;
        }
        catch (Exception)
        {
            // Only remove a file this call created: an existing name is left alone.
            if (created)
            {
                TryDelete(path);
            }

            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // best effort
        }
    }
}
