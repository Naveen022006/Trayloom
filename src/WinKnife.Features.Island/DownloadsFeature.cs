// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Island;

/// <summary>Watches a folder (Downloads by default) and lists newly arrived files in the island.</summary>
public sealed class IslandDownloadsFeature : IFeature
{
    private IFeatureContext? _context;
    private DownloadsModel? _model;

    public string Id => "island-downloads";

    public string Name => "Island downloads";

    public string Description => "Shows files as they finish downloading so you can open, reveal or drag them straight out of the island.";

    public FeatureGroup Group => FeatureGroup.Island;

    public IReadOnlyList<FeatureAction> Actions =>
        IslandHub.IsRunning && _model is not null
            ?
            [
                new FeatureAction("open", "Open the island downloads", () => IslandHub.Controller?.Expand("downloads"), ""),
                new FeatureAction("folder", "Open the downloads folder", () => _model?.OpenFolder(), ""),
            ]
            : [];

    public void Start(IFeatureContext context)
    {
        _context = context;
        _model = new DownloadsModel(context);
        var view = new DownloadsView(_model);
        IslandHub.RegisterSection(new IslandSection
        {
            Id = "downloads",
            Title = "Downloads",
            Glyph = "",
            Order = 30,
            Create = view.Build,
            VisibilityChanged = view.SetVisible,
            ScrollsContent = true,
        });
        _model.Start();
    }

    public void Stop()
    {
        IslandHub.UnregisterSection("downloads");
        _model?.Dispose();
        _model = null;
        _context = null;
    }

    public FrameworkElement? CreateSettingsView()
    {
        if (_context is null)
        {
            return null;
        }

        var settings = _context.Settings;
        var box = new TextBox { MinWidth = 260, Text = settings.Get("folder", string.Empty) };
        box.TextChanged += (_, _) => settings.Set("folder", box.Text.Trim());
        var browse = new Button { Content = "Browse...", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3) };
        browse.Click += (_, _) =>
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Folder to watch for downloads",
                UseDescriptionForTitle = true,
                SelectedPath = DownloadsModel.ResolveFolder(box.Text),
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                box.Text = dialog.SelectedPath;
            }
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(box);
        row.Children.Add(browse);

        return new SettingsViewBuilder(settings)
            .Add(new LegacySettingsCard("Folder to watch", "Leave empty to use your Downloads folder.", row))
            .AddToggle("announce", "Show new downloads in the collapsed island", true)
            .Build();
    }
}

internal sealed record DownloadItem(string Path, string Name, long Size, DateTime Arrived);

/// <summary>Folder watching and the list of finished files.</summary>
internal sealed class DownloadsModel : IDisposable
{
    private static readonly HashSet<string> TemporaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".crdownload", ".part", ".tmp", ".temp", ".download", ".opdownload", ".partial", ".!ut", ".aria2", ".crswap", ".filepart",
    };

    private const int MaxItems = 15;

    private readonly IFeatureContext _context;
    private readonly Dictionary<string, (long Size, int Stable, DateTime Since)> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DownloadItem> _items = [];
    private readonly DispatcherTimer _settle;
    private FileSystemWatcher? _watcher;
    private string _folder = string.Empty;

    public DownloadsModel(IFeatureContext context)
    {
        _context = context;
        _settle = new DispatcherTimer(DispatcherPriority.Background, context.Ui) { Interval = TimeSpan.FromMilliseconds(1000) };
        _settle.Tick += (_, _) => CheckPending();
    }

    public IReadOnlyList<DownloadItem> Items => _items;

    public string Folder => _folder;

    public event Action? Changed;

    public static string ResolveFolder(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
        {
            return configured;
        }

        try
        {
            var downloads = new Guid("374DE290-123F-4565-9164-39C4925E467B");
            if (SHGetKnownFolderPath(downloads, 0, 0, out var pointer) == 0)
            {
                var path = Marshal.PtrToStringUni(pointer);
                Marshal.FreeCoTaskMem(pointer);
                if (!string.IsNullOrEmpty(path))
                {
                    return path;
                }
            }
        }
        catch
        {
            // Fall back below.
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, nint token, out nint path);

    public static bool IsTemporary(string name)
    {
        var extension = Path.GetExtension(name);
        return TemporaryExtensions.Contains(extension) || name.StartsWith("~$", StringComparison.Ordinal) || name.StartsWith('.');
    }

    public void Start()
    {
        _context.Settings.Changed += OnSettingChanged;
        Watch();
    }

    public void Dispose()
    {
        _context.Settings.Changed -= OnSettingChanged;
        StopWatching();
        _settle.Stop();
        _pending.Clear();
        _items.Clear();
    }

    private void OnSettingChanged(string key)
    {
        if (key == "folder")
        {
            _context.Ui.BeginInvoke(Watch);
        }
    }

    private void Watch()
    {
        StopWatching();
        _items.Clear();
        _pending.Clear();
        _folder = ResolveFolder(_context.Settings.Get("folder", string.Empty));

        try
        {
            if (!Directory.Exists(_folder))
            {
                Changed?.Invoke();
                return;
            }

            // Seed with what arrived in the last day so the list is not empty after a restart.
            var cutoff = DateTime.Now.AddHours(-24);
            foreach (var file in new DirectoryInfo(_folder).EnumerateFiles()
                         .Where(f => !IsTemporary(f.Name) && (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0 && f.LastWriteTime > cutoff)
                         .OrderByDescending(f => f.LastWriteTime)
                         .Take(MaxItems))
            {
                _items.Add(new DownloadItem(file.FullName, file.Name, file.Length, file.LastWriteTime));
            }

            var watcher = new FileSystemWatcher(_folder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false,
                InternalBufferSize = 16 * 1024,
            };
            watcher.Created += (_, e) => Post(e.FullPath);
            watcher.Renamed += (_, e) =>
            {
                Forget(e.OldFullPath);
                Post(e.FullPath);
            };
            watcher.Changed += (_, e) => Post(e.FullPath);
            watcher.Deleted += (_, e) => Forget(e.FullPath);
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception ex)
        {
            _context.Log.Warn($"Cannot watch '{_folder}': {ex.Message}");
        }

        Changed?.Invoke();
    }

    private void StopWatching()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    private void Post(string path) => _context.Ui.BeginInvoke(() => Observe(path));

    private void Forget(string path) => _context.Ui.BeginInvoke(() =>
    {
        _pending.Remove(path);
        if (_items.RemoveAll(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            Changed?.Invoke();
        }
    });

    private void Observe(string path)
    {
        var name = Path.GetFileName(path);
        if (IsTemporary(name) || Directory.Exists(path))
        {
            return;
        }

        if (_items.Any(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase)) && !_pending.ContainsKey(path))
        {
            // Already listed: only a rewrite of the same file (size change) is worth re-checking.
            return;
        }

        _pending[path] = (-1, 0, DateTime.Now);
        if (!_settle.IsEnabled)
        {
            _settle.Start();
        }
    }

    /// <summary>A file counts as finished when it can be opened exclusively and its size stopped changing.</summary>
    private void CheckPending()
    {
        foreach (var path in _pending.Keys.ToList())
        {
            var (lastSize, stable, since) = _pending[path];
            if (!File.Exists(path) || DateTime.Now - since > TimeSpan.FromMinutes(30))
            {
                _pending.Remove(path);
                continue;
            }

            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                var size = stream.Length;
                stable = size == lastSize ? stable + 1 : 0;
                _pending[path] = (size, stable, since);
                if (stable >= 1)
                {
                    _pending.Remove(path);
                    Complete(path, size);
                }
            }
            catch (IOException)
            {
                _pending[path] = (-1, 0, since);
            }
            catch (UnauthorizedAccessException)
            {
                _pending.Remove(path);
            }
        }

        if (_pending.Count == 0)
        {
            _settle.Stop();
        }
    }

    private void Complete(string path, long size)
    {
        _items.RemoveAll(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
        var name = Path.GetFileName(path);
        _items.Insert(0, new DownloadItem(path, name, size, DateTime.Now));
        if (_items.Count > MaxItems)
        {
            _items.RemoveRange(MaxItems, _items.Count - MaxItems);
        }

        Changed?.Invoke();
        if (IslandHub.IsRunning && _context.Settings.Get("announce", true))
        {
            IslandHub.ShowAlert(new IslandAlert("", name, IslandTheme.Green, "downloads", TimeSpan.FromSeconds(4)));
        }
    }

    public void Remove(DownloadItem item)
    {
        if (_items.Remove(item))
        {
            Changed?.Invoke();
        }
    }

    public void OpenFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _context.Log.Warn($"Cannot open folder: {ex.Message}");
        }
    }

    public void Open(DownloadItem item)
    {
        try
        {
            Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _context.Log.Warn($"Cannot open '{item.Path}': {ex.Message}");
        }
    }

    public void Reveal(DownloadItem item)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.Path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _context.Log.Warn($"Cannot reveal '{item.Path}': {ex.Message}");
        }
    }
}

internal sealed class DownloadsView
{
    private readonly DownloadsModel _model;
    private readonly StackPanel _list = new();
    private readonly TextBlock _title = IslandTheme.Label("Recent downloads", 13, IslandTheme.Muted, FontWeights.SemiBold);
    private bool _visible;

    public DownloadsView(DownloadsModel model)
    {
        _model = model;
    }

    public void SetVisible(bool visible)
    {
        if (_visible == visible)
        {
            return;
        }

        _visible = visible;
        if (visible)
        {
            _model.Changed += Render;
            Render();
        }
        else
        {
            _model.Changed -= Render;
        }
    }

    public FrameworkElement Build()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Margin = new Thickness(2, 0, 0, 6) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var openFolder = new IslandButton("", "Open folder", 28, foreground: IslandTheme.Muted);
        openFolder.Clicked += _model.OpenFolder;
        Grid.SetColumn(openFolder, 1);
        header.Children.Add(_title);
        header.Children.Add(openFolder);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _list,
            Focusable = false,
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(header);
        root.Children.Add(scroll);
        Render();
        return root;
    }

    private void Render()
    {
        _list.Children.Clear();
        var folderName = string.IsNullOrEmpty(_model.Folder) ? "Downloads" : Path.GetFileName(_model.Folder.TrimEnd('\\', '/'));
        _title.Text = "Recent in " + (string.IsNullOrEmpty(folderName) ? "Downloads" : folderName);
        if (_model.Items.Count == 0)
        {
            var empty = IslandTheme.Label("Nothing downloaded yet", 14, IslandTheme.Faint);
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.Margin = new Thickness(0, 40, 0, 0);
            _list.Children.Add(empty);
            return;
        }

        foreach (var item in _model.Items)
        {
            _list.Children.Add(BuildRow(item));
        }
    }

    private FrameworkElement BuildRow(DownloadItem item)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(8),
            Background = IslandTheme.SurfaceHover,
            Child = IslandTheme.Glyph(GlyphFor(item.Name), 14, IslandTheme.Muted),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0) };
        text.Children.Add(IslandTheme.Label(item.Name, 13, IslandTheme.Text, FontWeights.SemiBold));
        text.Children.Add(IslandTheme.Label($"{FormatSize(item.Size)} · {Ago(item.Arrived)}", 11.5, IslandTheme.Muted));
        Grid.SetColumn(text, 1);

        var reveal = new IslandButton("", size: 28, foreground: IslandTheme.Muted);
        reveal.ToolTip = "Show in folder";
        reveal.Clicked += () => _model.Reveal(item);
        var remove = new IslandButton("", size: 28, foreground: IslandTheme.Muted);
        remove.ToolTip = "Remove from list";
        remove.Clicked += () => _model.Remove(item);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Hidden };
        buttons.Children.Add(reveal);
        buttons.Children.Add(remove);
        Grid.SetColumn(buttons, 2);

        grid.Children.Add(icon);
        grid.Children.Add(text);
        grid.Children.Add(buttons);

        var row = new Border
        {
            Child = grid,
            Padding = new Thickness(8, 5, 6, 5),
            CornerRadius = new CornerRadius(10),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 0, 2),
        };
        row.MouseEnter += (_, _) =>
        {
            row.Background = IslandTheme.Surface;
            buttons.Visibility = Visibility.Visible;
        };
        row.MouseLeave += (_, _) =>
        {
            row.Background = Brushes.Transparent;
            buttons.Visibility = Visibility.Hidden;
        };

        Point? start = null;
        row.PreviewMouseLeftButtonDown += (_, e) => start = e.GetPosition(row);
        row.MouseLeftButtonUp += (_, e) =>
        {
            if (start is not null)
            {
                start = null;
                _model.Open(item);
            }

            e.Handled = true;
        };
        row.MouseMove += (_, e) =>
        {
            if (start is null || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var now = e.GetPosition(row);
            if (Math.Abs(now.X - start.Value.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(now.Y - start.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            start = null;
            if (!File.Exists(item.Path))
            {
                return;
            }

            var data = new DataObject(DataFormats.FileDrop, new[] { item.Path });
            using var hold = IslandHub.Controller?.HoldOpen();
            DragDrop.DoDragDrop(row, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        };

        var menu = new ContextMenu();
        var open = new MenuItem { Header = "Open" };
        open.Click += (_, _) => _model.Open(item);
        var show = new MenuItem { Header = "Show in folder" };
        show.Click += (_, _) => _model.Reveal(item);
        var copy = new MenuItem { Header = "Copy path" };
        copy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(item.Path);
            }
            catch
            {
                // Clipboard busy.
            }
        };
        var drop = new MenuItem { Header = "Remove from list" };
        drop.Click += (_, _) => _model.Remove(item);
        menu.Items.Add(open);
        menu.Items.Add(show);
        menu.Items.Add(copy);
        menu.Items.Add(new Separator());
        menu.Items.Add(drop);
        IDisposable? menuHold = null;
        menu.Opened += (_, _) => menuHold = IslandHub.Controller?.HoldOpen();
        menu.Closed += (_, _) =>
        {
            menuHold?.Dispose();
            menuHold = null;
        };
        row.ContextMenu = menu;
        return row;
    }

    private static string GlyphFor(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".svg" or ".heic" => "",
        ".mp3" or ".wav" or ".flac" or ".m4a" or ".ogg" or ".aac" => "",
        ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm" => "",
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "",
        ".exe" or ".msi" or ".msix" => "",
        _ => "",
    };

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : string.Create(CultureInfo.CurrentCulture, $"{value:0.#} {units[unit]}");
    }

    private static string Ago(DateTime time)
    {
        var span = DateTime.Now - time;
        if (span.TotalSeconds < 60)
        {
            return "just now";
        }

        if (span.TotalMinutes < 60)
        {
            return $"{(int)span.TotalMinutes} min ago";
        }

        return span.TotalHours < 24 ? $"{(int)span.TotalHours} h ago" : $"{(int)span.TotalDays} d ago";
    }
}
