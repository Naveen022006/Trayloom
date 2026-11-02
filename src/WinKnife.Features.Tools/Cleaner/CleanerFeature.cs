// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

/// <summary>One group of junk that the cleaner can measure and delete.</summary>
internal sealed class CleanCategory
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required string Description { get; init; }

    /// <summary>Directories whose CONTENTS may be deleted. Nothing outside these is ever touched.</summary>
    public Func<IReadOnlyList<string>> Roots { get; init; } = () => Array.Empty<string>();

    public Func<FileInfo, bool>? Filter { get; init; }

    /// <summary>Files modified more recently than this are left alone (they may be in use by a running app).</summary>
    public TimeSpan MinAge { get; init; } = TimeSpan.Zero;

    public bool IsRecycleBin { get; init; }

    public long Size { get; set; }

    public long Files { get; set; }

    public bool Scanned { get; set; }
}

internal sealed record CleanResult(long Freed, int Deleted, int Skipped);

/// <summary>Scans and deletes only inside a fixed list of well-known cache and temp directories.</summary>
internal static class CleanerEngine
{
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false,
    };

    public static List<CleanCategory> BuildCategories()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var list = new List<CleanCategory>
        {
            new()
            {
                Id = "user-temp",
                Title = "Temporary files",
                Description = "Your temp folder. Files changed in the last hour are kept.",
                Roots = () => Existing(Path.GetTempPath()),
                MinAge = TimeSpan.FromHours(1),
            },
            new()
            {
                Id = "windows-temp",
                Title = "Windows temporary files",
                Description = "Windows\\Temp. Needs administrator rights; locked or protected files are skipped.",
                Roots = () => Existing(Path.Combine(windows, "Temp")),
                MinAge = TimeSpan.FromHours(1),
            },
            new()
            {
                Id = "thumbnails",
                Title = "Thumbnail and icon cache",
                Description = "Explorer rebuilds these automatically; files in use are skipped.",
                Roots = () => Existing(Path.Combine(local, "Microsoft", "Windows", "Explorer")),
                Filter = f => f.Name.StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase) || f.Name.StartsWith("iconcache_", StringComparison.OrdinalIgnoreCase),
            },
            new()
            {
                Id = "crash-dumps",
                Title = "Crash dumps",
                Description = "Memory dumps left behind by crashed apps.",
                Roots = () => Existing(Path.Combine(local, "CrashDumps")),
            },
        };

        AddChromium(list, "chrome", "Google Chrome cache", Path.Combine(local, "Google", "Chrome", "User Data"));
        AddChromium(list, "edge", "Microsoft Edge cache", Path.Combine(local, "Microsoft", "Edge", "User Data"));
        AddChromium(list, "brave", "Brave cache", Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"));
        var firefox = Path.Combine(local, "Mozilla", "Firefox", "Profiles");
        if (Directory.Exists(firefox))
        {
            list.Add(new CleanCategory
            {
                Id = "firefox",
                Title = "Firefox cache",
                Description = "Cached web content. Close the browser first for best results.",
                Roots = () => Directory.EnumerateDirectories(firefox).Select(d => Path.Combine(d, "cache2")).Where(Directory.Exists).ToList(),
            });
        }

        list.Add(new CleanCategory
        {
            Id = "recycle-bin",
            Title = "Recycle Bin",
            Description = "Everything you deleted that is still recoverable. This cannot be undone.",
            IsRecycleBin = true,
        });
        return list;
    }

    private static void AddChromium(List<CleanCategory> list, string id, string title, string userData)
    {
        if (!Directory.Exists(userData))
        {
            return;
        }

        list.Add(new CleanCategory
        {
            Id = id,
            Title = title,
            Description = "Cached pages, code and GPU shaders. Sign-ins, history and passwords are not touched.",
            Roots = () =>
            {
                var roots = new List<string>();
                foreach (var profile in Directory.EnumerateDirectories(userData))
                {
                    var name = Path.GetFileName(profile);
                    if (name != "Default" && !name.StartsWith("Profile ", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    foreach (var sub in new[] { "Cache", "Code Cache", "GPUCache" })
                    {
                        var path = Path.Combine(profile, sub);
                        if (Directory.Exists(path))
                        {
                            roots.Add(path);
                        }
                    }
                }

                return roots;
            },
        });
    }

    private static IReadOnlyList<string> Existing(string path) => Directory.Exists(path) ? new[] { path } : Array.Empty<string>();

    private static IEnumerable<FileInfo> EligibleFiles(CleanCategory category, string root)
    {
        var cutoff = DateTime.UtcNow - category.MinAge;
        foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", Options))
        {
            if (category.Filter is not null && !category.Filter(file))
            {
                continue;
            }

            if (category.MinAge > TimeSpan.Zero && file.LastWriteTimeUtc > cutoff)
            {
                continue;
            }

            yield return file;
        }
    }

    private static bool IsSafeRoot(string root)
    {
        try
        {
            var full = Path.GetFullPath(root);
            return Path.GetPathRoot(full) != full && full.Count(c => c == Path.DirectorySeparatorChar) >= 2;
        }
        catch
        {
            return false;
        }
    }

    public static void Scan(CleanCategory category)
    {
        long size = 0;
        long files = 0;
        if (category.IsRecycleBin)
        {
            var info = new N.SHQUERYRBINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<N.SHQUERYRBINFO>() };
            if (N.SHQueryRecycleBinW(null, ref info) == 0)
            {
                size = info.i64Size;
                files = info.i64NumItems;
            }
        }
        else
        {
            foreach (var root in category.Roots().Where(IsSafeRoot))
            {
                try
                {
                    foreach (var file in EligibleFiles(category, root))
                    {
                        try
                        {
                            size += file.Length;
                            files++;
                        }
                        catch
                        {
                            // vanished
                        }
                    }
                }
                catch
                {
                    // inaccessible root
                }
            }
        }

        category.Size = size;
        category.Files = files;
        category.Scanned = true;
    }

    public static CleanResult Clean(CleanCategory category)
    {
        if (category.IsRecycleBin)
        {
            var before = category.Size;
            var hr = N.SHEmptyRecycleBinW(0, null, N.SHERB_NOCONFIRMATION | N.SHERB_NOPROGRESSUI | N.SHERB_NOSOUND);
            return hr == 0 ? new CleanResult(before, (int)category.Files, 0) : new CleanResult(0, 0, (int)category.Files);
        }

        long freed = 0;
        var deleted = 0;
        var skipped = 0;
        foreach (var root in category.Roots().Where(IsSafeRoot))
        {
            var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            try
            {
                foreach (var file in EligibleFiles(category, root).ToList())
                {
                    try
                    {
                        // Defence in depth: never delete anything that is not inside the root.
                        if (!file.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            skipped++;
                            continue;
                        }

                        var length = file.Length;
                        if (file.IsReadOnly)
                        {
                            file.IsReadOnly = false;
                        }

                        file.Delete();
                        freed += length;
                        deleted++;
                    }
                    catch
                    {
                        skipped++; // in use or access denied
                    }
                }

                RemoveEmptyDirectories(new DirectoryInfo(root), prefix);
            }
            catch
            {
                // root vanished or inaccessible
            }
        }

        return new CleanResult(freed, deleted, skipped);
    }

    private static void RemoveEmptyDirectories(DirectoryInfo directory, string prefix)
    {
        foreach (var sub in directory.EnumerateDirectories("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
        {
            if (!sub.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            RemoveEmptyDirectories(sub, prefix);
            try
            {
                if (!sub.EnumerateFileSystemInfos().Any())
                {
                    sub.Delete();
                }
            }
            catch
            {
                // not empty or in use
            }
        }
    }
}

/// <summary>Frees disk space by clearing temp folders, thumbnail and browser caches and the Recycle Bin.</summary>
public sealed class CleanerFeature : ToolFeatureBase
{
    private CleanerWindow? _window;

    public override string Id => "cleaner";

    public override string Name => "Cleaner";

    public override string Description => "Free disk space by clearing temp files, thumbnail and browser caches, and the Recycle Bin, with your confirmation.";

    public override IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("open", "Open cleaner", () => Safe(Open), ""),
    ];

    protected override void OnStart() => BindHotkey("hotkey", null, Open);

    protected override void OnStop()
    {
        _window?.Close();
        _window = null;
    }

    public override FrameworkElement? CreateSettingsView()
    {
        if (Ctx is null)
        {
            return null;
        }

        var open = UiKit.Btn("Open cleaner", Open, accent: true);
        open.HorizontalAlignment = HorizontalAlignment.Left;
        var builder = new SettingsViewBuilder(Ctx.Settings)
            .AddHotkey("hotkey", "Open cleaner", null, "Optional global shortcut.")
            .AddNote("The cleaner only ever deletes inside the temp folders, thumbnail cache, crash dumps, browser cache folders and the Recycle Bin. Nothing is removed until you confirm.");
        return UiKit.WithExtras(builder.Build(), open);
    }

    private void Open()
    {
        if (_window is null)
        {
            _window = new CleanerWindow(Ctx);
            _window.Closed += (_, _) => _window = null;
        }

        UiKit.Raise(_window);
    }
}

internal sealed class CleanerWindow : Window
{
    private readonly IFeatureContext _ctx;
    private readonly List<(CleanCategory Category, CheckBox Box, TextBlock Size)> _rows = new();
    private readonly TextBlock _status = UiKit.Muted("Press Scan to measure what can be cleaned.");
    private readonly TextBlock _total = UiKit.Label(string.Empty, null, 13, FontWeights.SemiBold);
    private readonly Button _scan;
    private readonly Button _clean;
    private bool _busy;

    public CleanerWindow(IFeatureContext ctx)
    {
        _ctx = ctx;
        Title = "Cleaner";
        Width = 640;
        Height = 640;
        MinWidth = 520;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(StyleProperty, ThemeKeys.WindowStyle);
        SourceInitialized += (_, _) => UiKit.ApplyTitleBarTheme(this);

        var list = new StackPanel();
        foreach (var category in CleanerEngine.BuildCategories())
        {
            var box = new CheckBox { IsChecked = category.Id != "recycle-bin", VerticalAlignment = VerticalAlignment.Center };
            var size = UiKit.Label("-", ThemeKeys.TextMuted, 13);
            size.MinWidth = 80;
            size.TextAlignment = TextAlignment.Right;
            var text = new StackPanel { Margin = new Thickness(10, 0, 10, 0) };
            text.Children.Add(UiKit.Label(category.Title, null, 14, FontWeights.SemiBold));
            var desc = UiKit.Muted(category.Description);
            desc.FontSize = 12;
            text.Children.Add(desc);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(box);
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            Grid.SetColumn(size, 2);
            grid.Children.Add(size);
            list.Children.Add(UiKit.Card(grid, new Thickness(0, 0, 0, 8)));
            _rows.Add((category, box, size));
            box.Click += (_, _) => UpdateTotal();
        }

        _scan = UiKit.Btn("Scan", () => _ = ScanAsync(), false, "");
        _clean = UiKit.Btn("Clean selected", () => _ = CleanAsync(), true, "");
        _clean.IsEnabled = false;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(_scan);
        buttons.Children.Add(_clean);
        buttons.Children.Add(_total);

        var layout = new DockPanel { Margin = new Thickness(16) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(UiKit.Label("Free up disk space", null, 20, FontWeights.SemiBold));
        header.Children.Add(_status);
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(header);
        layout.Children.Add(buttons);
        layout.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = layout;

        Loaded += async (_, _) => await ScanAsync();
    }

    private void UpdateTotal()
    {
        var bytes = _rows.Where(r => r.Box.IsChecked == true && r.Category.Scanned).Sum(r => r.Category.Size);
        _total.Text = _rows.Any(r => r.Category.Scanned) ? $"Selected: {UiKit.FormatBytes(bytes)}" : string.Empty;
        _total.Margin = new Thickness(8, 0, 0, 0);
    }

    private async Task ScanAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _scan.IsEnabled = false;
        _clean.IsEnabled = false;
        _status.Text = "Scanning...";
        foreach (var row in _rows)
        {
            row.Size.Text = "...";
        }

        try
        {
            foreach (var row in _rows)
            {
                var category = row.Category;
                await Task.Run(() => CleanerEngine.Scan(category));
                row.Size.Text = UiKit.FormatBytes(category.Size);
                UpdateTotal();
            }

            _status.Text = "Review the list, tick what to clean, then press Clean selected.";
            _clean.IsEnabled = true;
        }
        catch (Exception ex)
        {
            _ctx.Log.Error("Cleaner scan failed", ex);
            _status.Text = "Scan failed: " + ex.Message;
        }
        finally
        {
            _busy = false;
            _scan.IsEnabled = true;
        }
    }

    private async Task CleanAsync()
    {
        if (_busy)
        {
            return;
        }

        var chosen = _rows.Where(r => r.Box.IsChecked == true && r.Category.Size > 0).ToList();
        if (chosen.Count == 0)
        {
            MessageBox.Show(this, "Nothing selected to clean.", "Cleaner", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var summary = string.Join("\r\n", chosen.Select(r => $"  - {r.Category.Title}: {UiKit.FormatBytes(r.Category.Size)}"));
        var total = chosen.Sum(r => r.Category.Size);
        var message = $"Delete these files?\r\n\r\n{summary}\r\n\r\nAbout {UiKit.FormatBytes(total)} will be removed. Files in use are skipped. This cannot be undone.";
        if (MessageBox.Show(this, message, "Confirm cleaning", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        _busy = true;
        _scan.IsEnabled = false;
        _clean.IsEnabled = false;
        long freed = 0;
        var deleted = 0;
        var skipped = 0;
        try
        {
            foreach (var row in chosen)
            {
                _status.Text = "Cleaning " + row.Category.Title + "...";
                var category = row.Category;
                var result = await Task.Run(() => CleanerEngine.Clean(category));
                freed += result.Freed;
                deleted += result.Deleted;
                skipped += result.Skipped;
            }

            _status.Text = $"Freed {UiKit.FormatBytes(freed)} ({deleted} files removed, {skipped} skipped because they are in use or protected).";
            _ctx.Notifier.Show("Cleaner", $"Freed {UiKit.FormatBytes(freed)}.");
        }
        catch (Exception ex)
        {
            _ctx.Log.Error("Cleaning failed", ex);
            _status.Text = "Cleaning failed: " + ex.Message;
        }
        finally
        {
            _busy = false;
            await ScanAsync();
            _status.Text = $"Freed {UiKit.FormatBytes(freed)} ({deleted} files removed, {skipped} skipped because they are in use or protected).";
        }
    }
}
