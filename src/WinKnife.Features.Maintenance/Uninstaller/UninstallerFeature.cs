// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

internal sealed class InstalledApp : INotifyPropertyChanged
{
    private ImageSource? _icon;

    public required string Name { get; init; }

    public string Publisher { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public long SizeBytes { get; init; }

    public string InstallLocation { get; init; } = string.Empty;

    public string UninstallString { get; init; } = string.Empty;

    public string QuietUninstallString { get; init; } = string.Empty;

    public string DisplayIcon { get; init; } = string.Empty;

    public string RegistryKeyName { get; init; } = string.Empty;

    public string SizeText => SizeBytes > 0 ? UiKit.FormatBytes(SizeBytes) : string.Empty;

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

internal static class InstalledApps
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static List<InstalledApp> Read()
    {
        var apps = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hive, view) in new[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Registry64),
            (RegistryHive.CurrentUser, RegistryView.Registry32),
        })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var root = baseKey.OpenSubKey(UninstallPath);
                if (root is null)
                {
                    continue;
                }

                foreach (var name in root.GetSubKeyNames())
                {
                    using var key = root.OpenSubKey(name);
                    if (key is null || ToApp(name, key) is not { } app)
                    {
                        continue;
                    }

                    apps.TryAdd($"{app.Name}|{app.Version}|{app.Publisher}", app);
                }
            }
            catch
            {
                // unreadable hive: continue with the rest
            }
        }

        return apps.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static bool Exists(InstalledApp app)
    {
        foreach (var (hive, view) in new[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Registry64),
        })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(UninstallPath + "\\" + app.RegistryKeyName);
                if (key is not null)
                {
                    return true;
                }
            }
            catch
            {
                // ignore
            }
        }

        return false;
    }

    private static InstalledApp? ToApp(string keyName, RegistryKey key)
    {
        var name = key.GetValue("DisplayName") as string;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (key.GetValue("SystemComponent") is int sc && sc == 1)
        {
            return null;
        }

        if (key.GetValue("ParentKeyName") is not null || key.GetValue("ParentDisplayName") is not null)
        {
            return null;
        }

        var release = key.GetValue("ReleaseType") as string;
        if (release is "Update" or "Hotfix" or "Security Update" or "Service Pack")
        {
            return null;
        }

        var uninstall = key.GetValue("UninstallString") as string ?? string.Empty;
        var quiet = key.GetValue("QuietUninstallString") as string ?? string.Empty;
        if (uninstall.Length == 0 && quiet.Length == 0)
        {
            return null;
        }

        long sizeKb = 0;
        try
        {
            sizeKb = Convert.ToInt64(key.GetValue("EstimatedSize") ?? 0);
        }
        catch
        {
            // non numeric
        }

        return new InstalledApp
        {
            Name = name.Trim(),
            Publisher = (key.GetValue("Publisher") as string)?.Trim() ?? string.Empty,
            Version = (key.GetValue("DisplayVersion") as string)?.Trim() ?? string.Empty,
            SizeBytes = sizeKb * 1024,
            InstallLocation = (key.GetValue("InstallLocation") as string)?.Trim().Trim('"') ?? string.Empty,
            UninstallString = uninstall,
            QuietUninstallString = quiet,
            DisplayIcon = key.GetValue("DisplayIcon") as string ?? string.Empty,
            RegistryKeyName = keyName,
        };
    }

    /// <summary>Extracts an icon for an app (call from any thread; the result is frozen).</summary>
    public static ImageSource? LoadIcon(InstalledApp app)
    {
        var candidates = new List<string>();
        var raw = app.DisplayIcon.Trim();
        if (raw.Length > 0)
        {
            var m = Regex.Match(raw, "^\"?(?<path>[^\",]+)\"?(,\\s*-?\\d+)?$");
            candidates.Add(m.Success ? m.Groups["path"].Value : raw.Trim('"'));
        }

        if (app.InstallLocation.Length > 0 && Directory.Exists(app.InstallLocation))
        {
            try
            {
                var exe = Directory.EnumerateFiles(app.InstallLocation, "*.exe", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(f => !Path.GetFileName(f).Contains("unins", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(f).Contains("uninst", StringComparison.OrdinalIgnoreCase));
                if (exe is not null)
                {
                    candidates.Add(exe);
                }
            }
            catch
            {
                // ignore
            }
        }

        foreach (var path in candidates)
        {
            try
            {
                var expanded = Environment.ExpandEnvironmentVariables(path);
                if (!File.Exists(expanded))
                {
                    continue;
                }

                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(expanded);
                if (icon is null)
                {
                    continue;
                }

                var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            catch
            {
                // try the next candidate
            }
        }

        return null;
    }
}

internal sealed record LeftoverCandidate(string Path, long Size, string Reason);

internal static class LeftoverFinder
{
    private static readonly HashSet<string> Shared = new(StringComparer.OrdinalIgnoreCase)
    {
        "microsoft", "windows", "common files", "programs", "packages", "temp", "google", "adobe", "intel", "nvidia", "amd", "apple", "oracle", "java", "windowsapps", "systemapps",
    };

    public static IEnumerable<string> AllowedRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    }

    /// <summary>True when the path is a direct child of one of the allowed roots (what the finder may offer or delete).</summary>
    public static bool IsAllowed(string path)
    {
        try
        {
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            var parent = Path.GetDirectoryName(full);
            if (parent is null)
            {
                return false;
            }

            foreach (var root in AllowedRoots())
            {
                var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
                if (string.Equals(parent, r, StringComparison.OrdinalIgnoreCase))
                {
                    return !Shared.Contains(Path.GetFileName(full));
                }

                // one level deeper: <root>\<Publisher>\<App>
                var grand = Path.GetDirectoryName(parent);
                if (grand is not null && string.Equals(grand, r, StringComparison.OrdinalIgnoreCase) && !Shared.Contains(Path.GetFileName(full)))
                {
                    return true;
                }
            }
        }
        catch
        {
            // fall through
        }

        return false;
    }

    private static string Normalize(string text)
    {
        var t = Regex.Replace(text, @"\((x64|x86|64-bit|32-bit)[^)]*\)", " ", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\s+v?\d+([._]\d+)+.*$", string.Empty);
        t = Regex.Replace(t, @"\b(x64|x86|64-bit|32-bit)\b", " ", RegexOptions.IgnoreCase);
        return new string(t.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    public static List<LeftoverCandidate> Find(InstalledApp app)
    {
        var name = Normalize(app.Name);
        var publisher = Normalize(app.Publisher);
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (name.Length < 3)
        {
            return new();
        }

        bool Matches(string dirName)
        {
            var n = Normalize(dirName);
            if (n.Length < 3)
            {
                return false;
            }

            return n == name || (name.Length >= 5 && (n.StartsWith(name, StringComparison.Ordinal) || n.Contains(name, StringComparison.Ordinal)));
        }

        foreach (var root in AllowedRoots())
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            try
            {
                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    var leaf = Path.GetFileName(dir);
                    if (Shared.Contains(leaf) && !(publisher.Length > 0 && Normalize(leaf) == publisher))
                    {
                        continue;
                    }

                    if (Matches(leaf))
                    {
                        found.TryAdd(dir, "Folder name matches the app");
                    }
                    else if (publisher.Length >= 3 && Normalize(leaf) == publisher)
                    {
                        // <root>\<Publisher>\<App>: offer only the app folder, never the shared publisher folder.
                        foreach (var sub in Directory.EnumerateDirectories(dir))
                        {
                            if (Matches(Path.GetFileName(sub)))
                            {
                                found.TryAdd(sub, "Inside the publisher folder " + leaf);
                            }
                        }
                    }
                }
            }
            catch
            {
                // unreadable root
            }
        }

        if (app.InstallLocation.Length > 0 && Directory.Exists(app.InstallLocation) && IsAllowed(app.InstallLocation))
        {
            found.TryAdd(app.InstallLocation, "Install location recorded by the app");
        }

        return found.Select(kv => new LeftoverCandidate(kv.Key, FolderSize(kv.Key), kv.Value)).OrderBy(c => c.Path).ToList();
    }

    private static long FolderSize(string path)
    {
        long total = 0;
        try
        {
            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                total += file.Length;
            }
        }
        catch
        {
            // partial size
        }

        return total;
    }
}

/// <summary>Lists installed programs, runs their own uninstaller and then offers leftover folders to review.</summary>
public sealed class UninstallerFeature : ToolFeatureBase
{
    private UninstallerWindow? _window;

    public override string Id => "uninstaller";

    public override string Name => "Uninstaller";

    public override string Description => "Remove programs with their own uninstaller, then review leftover folders before deleting anything.";

    public override IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("open", "Open uninstaller", () => Safe(Open), ""),
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

        var open = UiKit.Btn("Open uninstaller", Open, accent: true);
        open.HorizontalAlignment = HorizontalAlignment.Left;
        var builder = new SettingsViewBuilder(Ctx.Settings)
            .AddHotkey("hotkey", "Open uninstaller", null, "Optional global shortcut.")
            .AddNote("Uninstalling runs each program's own uninstaller (it may ask for administrator approval). Leftover folders are only suggested; they go to the Recycle Bin and only after you tick and confirm them.");
        return UiKit.WithExtras(builder.Build(), open);
    }

    private void Open()
    {
        if (_window is null)
        {
            _window = new UninstallerWindow(Ctx);
            _window.Closed += (_, _) => _window = null;
        }

        UiKit.Raise(_window);
    }
}

internal sealed class UninstallerWindow : Window
{
    private readonly IFeatureContext _ctx;
    private readonly DataGrid _grid = new();
    private readonly TextBox _search = new() { MinWidth = 260 };
    private readonly TextBlock _status = UiKit.Muted("Loading installed programs...");
    private readonly StackPanel _banner = new() { Visibility = Visibility.Collapsed };
    private readonly Button _quiet;
    private System.Collections.ObjectModel.ObservableCollection<InstalledApp> _items = new();
    private ListCollectionView? _view;
    private bool _busy;

    public UninstallerWindow(IFeatureContext ctx)
    {
        _ctx = ctx;
        Title = "Uninstaller";
        Width = 900;
        Height = 640;
        MinWidth = 640;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(StyleProperty, ThemeKeys.WindowStyle);
        SourceInitialized += (_, _) => UiKit.ApplyTitleBarTheme(this);

        _grid.AutoGenerateColumns = false;
        _grid.IsReadOnly = true;
        _grid.SelectionMode = DataGridSelectionMode.Single;
        _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        _grid.GridLinesVisibility = DataGridGridLinesVisibility.None;
        var iconFactory = new FrameworkElementFactory(typeof(Image));
        iconFactory.SetBinding(Image.SourceProperty, new Binding(nameof(InstalledApp.Icon)));
        iconFactory.SetValue(FrameworkElement.WidthProperty, 20.0);
        iconFactory.SetValue(FrameworkElement.HeightProperty, 20.0);
        iconFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 2, 4, 2));
        _grid.Columns.Add(new DataGridTemplateColumn { Header = string.Empty, Width = new DataGridLength(34), CellTemplate = new DataTemplate { VisualTree = iconFactory } });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Binding(nameof(InstalledApp.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Publisher", Binding = new Binding(nameof(InstalledApp.Publisher)), Width = new DataGridLength(180) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Version", Binding = new Binding(nameof(InstalledApp.Version)), Width = new DataGridLength(110) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Size", Binding = new Binding(nameof(InstalledApp.SizeText)), SortMemberPath = nameof(InstalledApp.SizeBytes), Width = new DataGridLength(90) });

        _search.TextChanged += (_, _) => _view?.Refresh();
        var searchRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        searchRow.Children.Add(UiKit.Glyph("", 14, ThemeKeys.TextMuted));
        _search.Margin = new Thickness(8, 0, 0, 0);
        searchRow.Children.Add(_search);
        searchRow.Children.Add(new Border { Width = 16 });
        searchRow.Children.Add(_status);

        _quiet = UiKit.Btn("Quiet uninstall", () => _ = UninstallAsync(quiet: true));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(UiKit.Btn("Uninstall", () => _ = UninstallAsync(false), true, ""));
        buttons.Children.Add(_quiet);
        buttons.Children.Add(UiKit.Btn("Find leftovers", () => FindLeftovers(Selected()), false, ""));
        buttons.Children.Add(UiKit.Btn("Open install folder", OpenFolder));
        buttons.Children.Add(UiKit.Btn("Refresh", () => _ = LoadAsync(), false, ""));
        _grid.SelectionChanged += (_, _) => _quiet.IsEnabled = Selected()?.QuietUninstallString.Length > 0;
        _quiet.IsEnabled = false;

        var layout = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(searchRow, Dock.Top);
        DockPanel.SetDock(_banner, Dock.Bottom);
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(searchRow);
        layout.Children.Add(buttons);
        layout.Children.Add(_banner);
        layout.Children.Add(_grid);
        Content = layout;

        Loaded += async (_, _) => await LoadAsync();
    }

    private InstalledApp? Selected() => _grid.SelectedItem as InstalledApp;

    private async Task LoadAsync()
    {
        _status.Text = "Loading installed programs...";
        var apps = await Task.Run(InstalledApps.Read);
        _items = new System.Collections.ObjectModel.ObservableCollection<InstalledApp>(apps);
        _view = new ListCollectionView(_items)
        {
            Filter = o =>
            {
                var q = _search.Text.Trim();
                var a = (InstalledApp)o;
                return q.Length == 0 || a.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || a.Publisher.Contains(q, StringComparison.OrdinalIgnoreCase);
            },
        };
        _grid.ItemsSource = _view;
        _status.Text = $"{apps.Count} programs";

        // Icons are loaded in the background so the list appears immediately.
        _ = Task.Run(() =>
        {
            foreach (var app in apps)
            {
                var icon = InstalledApps.LoadIcon(app);
                if (icon is not null)
                {
                    Dispatcher.BeginInvoke(() => app.Icon = icon);
                }
            }
        });
    }

    private async Task UninstallAsync(bool quiet)
    {
        if (_busy || Selected() is not { } app)
        {
            return;
        }

        var command = quiet && app.QuietUninstallString.Length > 0 ? app.QuietUninstallString : app.UninstallString;
        if (command.Length == 0)
        {
            command = app.QuietUninstallString;
        }

        if (MessageBox.Show(this, $"Uninstall \"{app.Name}\"?\r\n\r\nThe program's own uninstaller will run{(quiet ? " silently" : string.Empty)}. It may ask for administrator permission.", "Uninstall", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _busy = true;
        _status.Text = $"Running the uninstaller for {app.Name}...";
        try
        {
            var info = new ProcessStartInfo("cmd.exe", $"/s /c \"{command}\"") { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden };
            using var process = Process.Start(info);
            if (process is not null)
            {
                await process.WaitForExitAsync();
            }

            await Task.Delay(1500);
            var removed = !await Task.Run(() => InstalledApps.Exists(app));
            _status.Text = removed ? $"{app.Name} was uninstalled." : $"The uninstaller for {app.Name} finished, but the program still appears in the list (it may have been cancelled).";
            await LoadAsync();
            if (removed)
            {
                ShowBanner(app);
            }
        }
        catch (Exception ex)
        {
            _ctx.Log.Warn("Uninstall failed: " + ex.Message);
            MessageBox.Show(this, "The uninstaller could not be started: " + ex.Message, "Uninstall", MessageBoxButton.OK, MessageBoxImage.Warning);
            _status.Text = string.Empty;
        }
        finally
        {
            _busy = false;
        }
    }

    private void ShowBanner(InstalledApp app)
    {
        _banner.Children.Clear();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        row.Children.Add(UiKit.Label($"{app.Name} was removed. Check for leftover files?", null, 13));
        var review = UiKit.Btn("Review leftovers", () =>
        {
            _banner.Visibility = Visibility.Collapsed;
            FindLeftovers(app);
        }, true);
        review.Margin = new Thickness(12, 0, 8, 0);
        row.Children.Add(review);
        row.Children.Add(UiKit.Btn("Dismiss", () => _banner.Visibility = Visibility.Collapsed));
        _banner.Children.Add(UiKit.Card(row));
        _banner.Visibility = Visibility.Visible;
    }

    private void OpenFolder()
    {
        if (Selected() is { InstallLocation.Length: > 0 } app && Directory.Exists(app.InstallLocation))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{app.InstallLocation}\"") { UseShellExecute = true });
        }
        else
        {
            MessageBox.Show(this, "This program did not record an install folder.", "Uninstaller", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void FindLeftovers(InstalledApp? app)
    {
        if (app is null)
        {
            MessageBox.Show(this, "Select a program first.", "Leftovers", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _status.Text = "Looking for leftovers...";
        var candidates = await Task.Run(() => LeftoverFinder.Find(app));
        _status.Text = string.Empty;
        if (candidates.Count == 0)
        {
            MessageBox.Show(this, $"No leftover folders matching \"{app.Name}\" were found.", "Leftovers", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        new LeftoverWindow(app, candidates, _ctx) { Owner = this }.Show();
    }
}

internal sealed class LeftoverWindow : Window
{
    private readonly List<(CheckBox Box, LeftoverCandidate Item)> _rows = new();
    private readonly IFeatureContext _ctx;

    public LeftoverWindow(InstalledApp app, List<LeftoverCandidate> candidates, IFeatureContext ctx)
    {
        _ctx = ctx;
        Title = "Leftover folders";
        Width = 700;
        Height = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(StyleProperty, ThemeKeys.WindowStyle);
        SourceInitialized += (_, _) => UiKit.ApplyTitleBarTheme(this);

        var list = new StackPanel();
        foreach (var candidate in candidates)
        {
            var box = new CheckBox { VerticalAlignment = VerticalAlignment.Center, IsEnabled = LeftoverFinder.IsAllowed(candidate.Path) };
            var text = new StackPanel { Margin = new Thickness(10, 0, 10, 0) };
            text.Children.Add(UiKit.Label(candidate.Path, null, 13));
            text.Children.Add(UiKit.Label($"{candidate.Reason} - {UiKit.FormatBytes(candidate.Size)}", ThemeKeys.TextMuted, 11));
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(box);
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            list.Children.Add(UiKit.Card(grid, new Thickness(0, 0, 0, 8)));
            _rows.Add((box, candidate));
        }

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(UiKit.Label($"Possible leftovers of {app.Name}", null, 18, FontWeights.SemiBold));
        header.Children.Add(UiKit.Muted("Nothing is selected by default. Check each folder yourself; selected folders are moved to the Recycle Bin."));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(UiKit.Btn("Move selected to Recycle Bin", MoveSelected, true, ""));
        buttons.Children.Add(UiKit.Btn("Close", Close));

        var layout = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(header);
        layout.Children.Add(buttons);
        layout.Children.Add(new ScrollViewer { Content = list });
        Content = layout;
    }

    private void MoveSelected()
    {
        var chosen = _rows.Where(r => r.Box.IsChecked == true).Select(r => r.Item).ToList();
        if (chosen.Count == 0)
        {
            return;
        }

        var text = "Move these folders to the Recycle Bin?\r\n\r\n" + string.Join("\r\n", chosen.Select(c => "  - " + c.Path));
        if (MessageBox.Show(this, text, "Remove leftovers", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        var failed = new List<string>();
        foreach (var item in chosen)
        {
            if (!LeftoverFinder.IsAllowed(item.Path))
            {
                failed.Add(item.Path + " (not an allowed location)");
                continue;
            }

            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                    item.Path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }
            catch (Exception ex)
            {
                failed.Add($"{item.Path} ({ex.Message})");
            }
        }

        if (failed.Count > 0)
        {
            MessageBox.Show(this, "Some folders could not be removed:\r\n\r\n" + string.Join("\r\n", failed), "Remove leftovers", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            _ctx.Notifier.Show("Uninstaller", $"{chosen.Count} folder(s) moved to the Recycle Bin.");
            Close();
        }
    }
}
