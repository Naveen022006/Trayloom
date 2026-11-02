// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

namespace WinKnife.Features.Capture;

internal sealed class RecentEntry
{
    public Guid Id { get; set; }

    public string Kind { get; set; } = "screenshot";

    public DateTime CreatedAt { get; set; }

    public string? ScreenshotName { get; set; }

    public string? RecordingPath { get; set; }

    public string? ThumbnailName { get; set; }

    public double Scale { get; set; } = 1;

    public int AnchorX { get; set; }

    public int AnchorY { get; set; }

    public int AnchorW { get; set; }

    public int AnchorH { get; set; }
}

/// <summary>The last 12 screenshots and recordings (full PNG + 360 px thumbnail), newest first, capped at 256 MB of screenshots.</summary>
internal sealed class RecentCaptureService
{
    public const int Limit = 12;
    private const long ByteLimit = 256L * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false, PropertyNameCaseInsensitive = true };
    private readonly object _gate = new();
    private List<RecentEntry> _entries = new();
    private bool _loaded;

    public static RecentCaptureService Instance { get; } = new();

    public event Action? Changed;

    private static string Dir => Path.Combine(Output.DataRoot(), "RecentCaptures");

    private static string Index => Path.Combine(Dir, "history.json");

    private void Load()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            if (File.Exists(Index))
            {
                _entries = JsonSerializer.Deserialize<List<RecentEntry>>(File.ReadAllText(Index), Json) ?? new();
                _entries.RemoveAll(e => e.Kind == "screenshot" ? e.ScreenshotName is null || !File.Exists(Path.Combine(Dir, e.ScreenshotName)) : e.RecordingPath is null);
            }
        }
        catch
        {
            _entries = new();
        }
    }

    public IReadOnlyList<RecentEntry> Entries(Func<string, bool>? kindAvailable = null)
    {
        lock (_gate)
        {
            Load();
            return _entries.Where(e => kindAvailable?.Invoke(e.Kind) ?? true).OrderByDescending(e => e.CreatedAt).ToList();
        }
    }

    public void RecordScreenshot(Capture capture)
    {
        try
        {
            lock (_gate)
            {
                Load();
                Directory.CreateDirectory(Dir);
                var id = Guid.NewGuid();
                var name = id + ".png";
                var thumb = id + "-thumbnail.png";
                File.WriteAllBytes(Path.Combine(Dir, name), Output.ToPng(capture.Image, capture.Scale));
                File.WriteAllBytes(Path.Combine(Dir, thumb), Output.ToPng(Output.Thumbnail(capture.Image, 360)));
                _entries.Insert(0, new RecentEntry
                {
                    Id = id, Kind = "screenshot", CreatedAt = DateTime.Now, ScreenshotName = name, ThumbnailName = thumb, Scale = capture.Scale,
                    AnchorX = capture.Anchor.X, AnchorY = capture.Anchor.Y, AnchorW = capture.Anchor.Width, AnchorH = capture.Anchor.Height,
                });
                Trim();
                Commit();
            }
        }
        catch (Exception ex)
        {
            CaptureHost.Log("recent captures write failed", ex);
        }

        Changed?.Invoke();
    }

    public void RecordRecording(string path, BitmapSource? firstFrame)
    {
        try
        {
            lock (_gate)
            {
                Load();
                Directory.CreateDirectory(Dir);
                _entries.RemoveAll(e => e.Kind == "recording" && string.Equals(e.RecordingPath, path, StringComparison.OrdinalIgnoreCase));
                var id = Guid.NewGuid();
                string? thumb = null;
                if (firstFrame is not null)
                {
                    thumb = id + "-thumbnail.png";
                    File.WriteAllBytes(Path.Combine(Dir, thumb), Output.ToPng(Output.Thumbnail(firstFrame, 360)));
                }

                _entries.Insert(0, new RecentEntry { Id = id, Kind = "recording", CreatedAt = DateTime.Now, RecordingPath = path, ThumbnailName = thumb });
                Trim();
                Commit();
            }
        }
        catch (Exception ex)
        {
            CaptureHost.Log("recent captures write failed", ex);
        }

        Changed?.Invoke();
    }

    private void Trim()
    {
        _entries = _entries.OrderByDescending(e => e.CreatedAt).ToList();
        while (_entries.Count > Limit)
        {
            _entries.RemoveAt(_entries.Count - 1);
        }

        long total = 0;
        var keepFirst = true;
        foreach (var e in _entries.ToList())
        {
            if (e.ScreenshotName is null)
            {
                continue;
            }

            long size = 0;
            try
            {
                size = new FileInfo(Path.Combine(Dir, e.ScreenshotName)).Length;
            }
            catch
            {
                // gone
            }

            total += size;
            if (!keepFirst && total > ByteLimit)
            {
                _entries.Remove(e);
            }

            keepFirst = false;
        }
    }

    private void Commit()
    {
        File.WriteAllText(Index, JsonSerializer.Serialize(_entries, Json));
        // remove orphaned files
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "history.json" };
        foreach (var e in _entries)
        {
            if (e.ScreenshotName is not null)
            {
                referenced.Add(e.ScreenshotName);
            }

            if (e.ThumbnailName is not null)
            {
                referenced.Add(e.ThumbnailName);
            }
        }

        foreach (var f in Directory.EnumerateFiles(Dir))
        {
            if (!referenced.Contains(Path.GetFileName(f)))
            {
                try
                {
                    File.Delete(f);
                }
                catch
                {
                    // in use
                }
            }
        }
    }

    public void Remove(Guid id)
    {
        lock (_gate)
        {
            Load();
            _entries.RemoveAll(e => e.Id == id);
            try
            {
                Commit();
            }
            catch
            {
                // ignore
            }
        }

        Changed?.Invoke();
    }

    public void Clear()
    {
        lock (_gate)
        {
            Load();
            _entries.Clear();
            try
            {
                Commit();
            }
            catch
            {
                // ignore
            }
        }

        Changed?.Invoke();
    }

    public BitmapSource? Thumbnail(RecentEntry e) =>
        e.ThumbnailName is null ? null : Output.LoadImage(Path.Combine(Dir, e.ThumbnailName));

    public Capture? LoadCapture(RecentEntry e)
    {
        if (e.ScreenshotName is null)
        {
            return null;
        }

        var image = Output.LoadImage(Path.Combine(Dir, e.ScreenshotName));
        return image is null ? null : new Capture(image, e.Scale, new Int32Rect(e.AnchorX, e.AnchorY, e.AnchorW, e.AnchorH));
    }
}
