// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows.Threading;

namespace Trayloom.Features.Island;

internal sealed class DownloadItem
{
    public required string Path { get; init; }

    public string Name { get; init; } = "";

    public DateTime Date { get; init; }

    public long Bytes { get; init; }

    public double? Fraction { get; init; }

    public bool Active { get; init; }

    public bool Completed { get; init; }

    public bool Partial { get; init; }
}

/// <summary>Watches one folder for browser downloads (partial files, completions, failures) — spec Part D 1.5/1.6.</summary>
internal sealed class DownloadsService : IDisposable
{
    private static readonly string[] PartialExt = [".crdownload", ".part", ".opdownload", ".download", ".partial", ".!ut", ".aria2", ".filepart"];

    private readonly IslandCore _core;
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _debounce;
    private DispatcherTimer? _refresh;
    private string _folder = "";
    private List<DownloadItem> _items = [];
    private readonly Dictionary<string, (string Expected, DateTime Seen, string? Id)> _partials = [];
    private readonly List<DownloadItem> _finished = [];
    private DateTime _finishedAt;
    private readonly HashSet<string> _arrived = [];

    public DownloadsService(IslandCore core) => _core = core;

    public IReadOnlyList<DownloadItem> Items => _items;

    public string FolderName => string.IsNullOrEmpty(_folder) ? "" : System.IO.Path.GetFileName(_folder.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : _folder;

    public string Folder => _folder;

    public bool FolderUnavailable { get; private set; }

    public bool Enabled => _core.App.Get<bool>(K.DownloadsEnabled) && _core.Ctx.Runtime.IsAvailable("notch-downloads");

    public bool HasActive => _items.Any(i => i.Active && !i.Completed);

    public DownloadItem? ActiveItem => _items.FirstOrDefault(i => i.Active && !i.Completed);

    public event Action? Changed;

    public event Action<DownloadItem>? Arrived;

    public event Action? Failed;

    public void Start() => Sync();

    public void Sync()
    {
        var folder = _core.App.Get<string>(K.DownloadsFolder) ?? "";
        var wants = Enabled && _core.IslandOn && _core.ModuleVisible("downloads") || (Enabled && _core.IslandOn);
        Stop();
        _folder = folder;
        FolderUnavailable = false;
        if (string.IsNullOrWhiteSpace(folder))
        {
            Changed?.Invoke();
            return;
        }

        if (!Directory.Exists(folder))
        {
            FolderUnavailable = true;
            Changed?.Invoke();
            return;
        }

        if (!wants)
        {
            Changed?.Invoke();
            return;
        }

        try
        {
            _watcher = new FileSystemWatcher(folder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime | NotifyFilters.DirectoryName,
                InternalBufferSize = 64 * 1024,
                EnableRaisingEvents = true,
            };
            _watcher.Created += (_, _) => Schedule();
            _watcher.Changed += (_, _) => Schedule();
            _watcher.Renamed += (_, _) => Schedule();
            _watcher.Deleted += (_, _) => Schedule();
            _watcher.Error += (_, _) => Schedule();
        }
        catch
        {
            FolderUnavailable = true;
        }

        Scan();
    }

    private void Stop()
    {
        _watcher?.Dispose();
        _watcher = null;
        _refresh?.Stop();
    }

    private void Schedule()
    {
        _core.Ui.BeginInvoke(() =>
        {
            _debounce ??= new DispatcherTimer(DispatcherPriority.Background, _core.Ui) { Interval = TimeSpan.FromMilliseconds(300) };
            _debounce.Tick -= OnDebounce;
            _debounce.Tick += OnDebounce;
            _debounce.Stop();
            _debounce.Start();
        });
    }

    private void OnDebounce(object? sender, EventArgs e)
    {
        _debounce?.Stop();
        Scan();
    }

    private static bool IsPartialName(string name, out string expected)
    {
        foreach (var ext in PartialExt)
        {
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                expected = name[..^ext.Length];
                return true;
            }
        }

        expected = name;
        return false;
    }

    private void Scan()
    {
        if (string.IsNullOrEmpty(_folder))
        {
            return;
        }

        if (!Directory.Exists(_folder))
        {
            FolderUnavailable = true;
            Stop();
            Changed?.Invoke();
            return;
        }

        var items = new List<DownloadItem>();
        var seenPartials = new Dictionary<string, (string Expected, DateTime Seen, string? Id)>();
        var now = DateTime.Now;
        DateTime? soonest = null;
        try
        {
            foreach (var entry in new DirectoryInfo(_folder).EnumerateFileSystemInfos())
            {
                try
                {
                    if ((entry.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) != 0)
                    {
                        continue;
                    }

                    if (IsPartialName(entry.Name, out var expected))
                    {
                        if (seenPartials.Count >= 32)
                        {
                            continue;
                        }

                        var bytes = entry is FileInfo fi ? fi.Length : 0;
                        var active = (now - entry.LastWriteTime).TotalSeconds < 120;
                        if (active)
                        {
                            var until = entry.LastWriteTime.AddSeconds(120);
                            if (soonest is null || until < soonest)
                            {
                                soonest = until;
                            }
                        }

                        seenPartials[entry.FullName] = (System.IO.Path.Combine(_folder, expected), now, null);
                        items.Add(new DownloadItem { Path = entry.FullName, Name = expected, Date = entry.LastWriteTime, Bytes = bytes, Active = active, Partial = true });
                    }
                    else if (entry is FileInfo f)
                    {
                        items.Add(new DownloadItem { Path = f.FullName, Name = f.Name, Date = new[] { f.CreationTime, f.LastWriteTime }.Max(), Bytes = f.Length, Completed = true });
                    }
                    else
                    {
                        items.Add(new DownloadItem { Path = entry.FullName, Name = entry.Name, Date = entry.LastWriteTime, Completed = true });
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
            FolderUnavailable = true;
        }

        // completion: a previously seen partial vanished and its expected file now exists
        foreach (var (path, info) in _partials)
        {
            if (seenPartials.ContainsKey(path) || File.Exists(path))
            {
                continue;
            }

            if (File.Exists(info.Expected))
            {
                var f = new FileInfo(info.Expected);
                var item = new DownloadItem { Path = f.FullName, Name = f.Name, Date = DateTime.Now, Bytes = f.Length, Completed = true };
                if (_arrived.Add(f.FullName))
                {
                    _finished.Insert(0, item);
                    if (_finished.Count > 5)
                    {
                        _finished.RemoveAt(5);
                    }

                    _finishedAt = DateTime.Now;
                    Arrived?.Invoke(item);
                }
            }
            else
            {
                var expected = info.Expected;
                _core.Ui.BeginInvoke(async () =>
                {
                    await Task.Delay(2000);
                    if (!File.Exists(expected) && !_partials.Keys.Any(k => IsPartialName(System.IO.Path.GetFileName(k), out var e2) && e2 == System.IO.Path.GetFileName(expected)) && !Directory.EnumerateFiles(_folder).Any(f => IsPartialName(System.IO.Path.GetFileName(f), out var e3) && e3 == System.IO.Path.GetFileName(expected)))
                    {
                        Failed?.Invoke();
                    }
                });
            }
        }

        _partials.Clear();
        foreach (var (k, v) in seenPartials)
        {
            _partials[k] = v;
        }

        if ((DateTime.Now - _finishedAt).TotalSeconds > 15)
        {
            _finished.Clear();
        }

        var active2 = items.Where(i => i.Partial).Select(i => i.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _items = [.. items
            .Where(i => i.Partial || !active2.Contains(i.Name))
            .OrderByDescending(i => i.Date).ThenBy(i => i.Path, StringComparer.Ordinal)
            .Take(200)];
        Changed?.Invoke();

        if (soonest is { } at)
        {
            _refresh ??= new DispatcherTimer(DispatcherPriority.Background, _core.Ui);
            _refresh.Stop();
            _refresh.Interval = TimeSpan.FromSeconds(Math.Max(1, (at - DateTime.Now).TotalSeconds + 1));
            _refresh.Tick -= OnRefresh;
            _refresh.Tick += OnRefresh;
            _refresh.Start();
        }
    }

    private void OnRefresh(object? sender, EventArgs e)
    {
        _refresh?.Stop();
        Scan();
    }

    public void Forget()
    {
        _core.App.Set(K.DownloadsFolder, "");
        _core.App.Set(K.DownloadsEnabled, false);
        _items = [];
        Sync();
    }

    public void Choose(string folder)
    {
        _core.App.Set(K.DownloadsFolder, folder);
        _core.App.Set(K.DownloadsEnabled, true);
        Sync();
    }

    public void Dispose()
    {
        Stop();
        _debounce?.Stop();
    }
}
