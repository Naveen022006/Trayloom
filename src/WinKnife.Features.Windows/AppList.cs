// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;

namespace WinKnife.Features.WindowTools;

/// <summary>One row of the app picker.</summary>
internal sealed record AppEntry(string Identity, string Name, string Subtitle, BitmapSource? Icon);

/// <summary>"Choose app" sheet (spec 02b 0.9): searchable list of running and installed apps plus a Browse button.</summary>
internal sealed class AppPickerWindow : Window
{
    private readonly TextBox _search = new();
    private readonly StackPanel _list = new();
    private readonly TextBlock _state = new();
    private readonly Func<IReadOnlyList<AppEntry>> _loader;
    private readonly HashSet<string> _exclude;
    private readonly string? _emptyNote;
    private List<AppEntry> _entries = [];

    public AppPickerWindow(Func<IReadOnlyList<AppEntry>>? loader, IEnumerable<string> exclude, bool allowBrowse, string? emptyNote = null)
    {
        _loader = loader ?? LoadDefault;
        _exclude = new HashSet<string>(exclude.Select(e => e.ToLowerInvariant()));
        _emptyNote = emptyNote;
        Title = Strings.ChooseApp;
        Width = 520;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        FontFamily = UiKit.TextFont;
        this.Themed(BackgroundProperty, ThemeKeys.SettingsWindow);
        SourceInitialized += (_, _) => ThemeManager.BindWindow(this);

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(UiKit.Text(Strings.ChooseApp, 16, FontWeights.SemiBold));
        if (allowBrowse)
        {
            var browse = UiKit.Button(Strings.ChooseAppEllipsis, ButtonKind.Bordered, 12, "", Browse);
            Grid.SetColumn(browse, 1);
            header.Children.Add(browse);
        }

        var cancel = UiKit.Button(Strings.Cancel, ButtonKind.Bordered, 12, null, () =>
        {
            DialogResult = false;
        });
        cancel.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(cancel, 2);
        header.Children.Add(cancel);
        root.Children.Add(header);

        _search.Margin = new Thickness(0, 12, 0, 12);
        _search.Padding = new Thickness(8, 6, 8, 6);
        _search.ToolTip = Strings.SearchApps;
        _search.TextChanged += (_, _) => Refill();
        var searchHost = new Grid { Margin = new Thickness(0, 12, 0, 12) };
        _search.Margin = new Thickness(0);
        var hint = UiKit.Text(Strings.SearchApps, 13, FontWeights.Normal, ThemeKeys.TextTertiary);
        hint.IsHitTestVisible = false;
        hint.Margin = new Thickness(10, 0, 0, 0);
        hint.VerticalAlignment = VerticalAlignment.Center;
        _search.TextChanged += (_, _) => hint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        searchHost.Children.Add(_search);
        searchHost.Children.Add(hint);
        Grid.SetRow(searchHost, 1);
        root.Children.Add(searchHost);

        _state.HorizontalAlignment = HorizontalAlignment.Center;
        _state.VerticalAlignment = VerticalAlignment.Center;
        _state.TextAlignment = TextAlignment.Center;
        _state.TextWrapping = TextWrapping.Wrap;
        _state.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextSecondary);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list };
        var body = new Grid();
        body.Children.Add(scroll);
        body.Children.Add(_state);
        Grid.SetRow(body, 2);
        root.Children.Add(body);
        Content = root;

        _state.Text = Strings.Scanning;
        Loaded += async (_, _) =>
        {
            _search.Focus();
            var entries = await Task.Run(() =>
            {
                try
                {
                    return _loader().Where(e => !_exclude.Contains(e.Identity.ToLowerInvariant())).ToList();
                }
                catch (Exception)
                {
                    return [];
                }
            });
            _entries = entries;
            Refill();
        };
    }

    /// <summary>The identity the user picked.</summary>
    public string? Result { get; private set; }

    private void Refill()
    {
        _list.Children.Clear();
        var query = _search.Text.Trim();
        var shown = _entries.Where(e => query.Length == 0
            || e.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || e.Identity.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
        if (shown.Count == 0)
        {
            _state.Text = _entries.Count == 0 && _emptyNote is not null ? _emptyNote : Strings.NoAppsFound;
            _state.Visibility = Visibility.Visible;
            return;
        }

        _state.Visibility = Visibility.Collapsed;
        foreach (var entry in shown)
        {
            _list.Children.Add(Row(entry));
        }
    }

    private FrameworkElement Row(AppEntry entry)
    {
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (entry.Icon is not null)
        {
            var image = new Image { Source = entry.Icon, Width = 28, Height = 28 };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            grid.Children.Add(image);
        }

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(UiKit.Text(entry.Name, 13, FontWeights.Medium, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis));
        text.Children.Add(UiKit.Text(entry.Subtitle, 11, FontWeights.Normal, ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis));
        Grid.SetColumn(text, 2);
        grid.Children.Add(text);
        var border = new Border { Padding = new Thickness(8, 7, 8, 7), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = grid, ToolTip = entry.Identity };
        border.MouseEnter += (_, _) => border.SetResourceReference(Border.BackgroundProperty, ThemeKeys.RowHover);
        border.MouseLeave += (_, _) => border.Background = Brushes.Transparent;
        border.MouseLeftButtonUp += (_, _) =>
        {
            Result = entry.Identity;
            DialogResult = true;
        };
        return border;
    }

    private void Browse()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Strings.ChooseApp,
            Filter = "Apps (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) == true)
        {
            Result = dialog.FileName.ToLowerInvariant();
            DialogResult = true;
        }
    }

    /// <summary>Running apps plus Start-menu apps.</summary>
    public static IReadOnlyList<AppEntry> LoadDefault()
    {
        var map = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in RunningPaths())
        {
            Add(map, path);
        }

        foreach (var path in StartMenuTargets())
        {
            Add(map, path);
        }

        return [.. map.Values.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>Executable paths of apps that currently own a window.</summary>
    public static IEnumerable<string> RunningPaths()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        WinApi.EnumWindows((hwnd, _) =>
        {
            if (WinApi.IsWindowVisible(hwnd) && WindowEnumerator.IsSwitchable(hwnd))
            {
                var path = AppIdentity.OwnerOf(hwnd).Path;
                if (path is not null)
                {
                    seen.Add(path);
                }
            }

            return true;
        }, 0);
        return seen;
    }

    private static IEnumerable<string> StartMenuTargets()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        };
        object? shell = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            shell = type is null ? null : Activator.CreateInstance(type);
        }
        catch (Exception)
        {
            yield break;
        }

        if (shell is null)
        {
            yield break;
        }

        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r) && Directory.Exists(r)))
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(Path.Combine(root, "Programs"), "*.lnk", SearchOption.AllDirectories).ToList();
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var file in files)
            {
                string? target = null;
                try
                {
                    dynamic link = ((dynamic)shell).CreateShortcut(file);
                    target = (string?)link.TargetPath;
                }
                catch (Exception)
                {
                    // unreadable link
                }

                if (!string.IsNullOrEmpty(target) && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(target) && results.Add(target))
                {
                    yield return target;
                }
            }
        }
    }

    private static void Add(Dictionary<string, AppEntry> map, string path)
    {
        var key = path.ToLowerInvariant();
        if (map.ContainsKey(key) || path.Contains("\\windows\\system32\\", StringComparison.OrdinalIgnoreCase) && !path.EndsWith("notepad.exe", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        map[key] = new AppEntry(key, AppIdentity.DisplayName(path), Path.GetDirectoryName(path) ?? path, AppIdentity.IconOf(path));
    }
}

/// <summary>
/// "Apps this feature treats differently" list (spec 02b 0.9): an expander with a count, one row per app with icon, name,
/// optional accessory and a remove button, an "Add an app…" button and a caption.
/// </summary>
internal sealed class AppBundleList : StackPanel
{
    private readonly Func<List<string>> _read;
    private readonly Action<List<string>> _write;
    private readonly string _title;
    private readonly string _addTitle;
    private readonly string _caption;
    private readonly Func<string, UIElement?>? _accessory;
    private readonly Func<IReadOnlyList<AppEntry>>? _loader;
    private readonly Func<string, bool>? _isLocked;
    private readonly string? _emptyNote;
    private readonly Action<string>? _added;
    private readonly Action<string>? _removed;
    private readonly bool _allowBrowse;
    private readonly StackPanel _body = new();
    private readonly TextBlock _count;
    private readonly TextBlock _chevron;
    private bool _expanded;

    public AppBundleList(
        string title,
        string caption,
        Func<List<string>> read,
        Action<List<string>> write,
        string addTitle = Strings.AddAnApp,
        Func<string, UIElement?>? accessory = null,
        Func<IReadOnlyList<AppEntry>>? loader = null,
        bool allowBrowse = true,
        string? emptyNote = null,
        Action<string>? added = null,
        Action<string>? removed = null,
        Func<string, bool>? isLocked = null)
    {
        _isLocked = isLocked;
        _title = title;
        _caption = caption;
        _read = read;
        _write = write;
        _addTitle = addTitle;
        _accessory = accessory;
        _loader = loader;
        _allowBrowse = allowBrowse;
        _emptyNote = emptyNote;
        _added = added;
        _removed = removed;
        _expanded = read().Count > 0;

        _chevron = UiKit.Icon(Glyphs.ChevronRightSmall, 9, ThemeKeys.TextSecondary, FontWeights.Bold);
        _chevron.Margin = new Thickness(0, 0, 8, 0);
        _count = UiKit.Text(string.Empty, 12, FontWeights.Normal, ThemeKeys.TextSecondary);
        _count.FontFamily = new FontFamily("Cascadia Mono, Consolas");

        var header = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(_chevron);
        var titleText = UiKit.Text(title, 12.5, FontWeights.Medium);
        Grid.SetColumn(titleText, 1);
        header.Children.Add(titleText);
        Grid.SetColumn(_count, 2);
        header.Children.Add(_count);
        header.MouseLeftButtonUp += (_, _) =>
        {
            _expanded = !_expanded;
            Apply();
        };
        Children.Add(header);
        Children.Add(_body);
        Rebuild();
    }

    /// <summary>Re-reads the stored list.</summary>
    public void Rebuild()
    {
        var items = _read();
        _count.Text = items.Count > 0 ? items.Count.ToString() : string.Empty;
        _body.Children.Clear();
        _body.Margin = new Thickness(17, 9, 0, 0);
        foreach (var identity in items.OrderBy(i => AppIdentity.DisplayName(i), StringComparer.CurrentCultureIgnoreCase))
        {
            _body.Children.Add(Row(identity));
        }

        var add = UiKit.Button(_addTitle, ButtonKind.Bordered, 11.5, Glyphs.Add, AddApp);
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Margin = new Thickness(0, 6, 0, 0);
        _body.Children.Add(add);
        var caption = UiKit.Text(_caption, 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        caption.Margin = new Thickness(0, 8, 0, 0);
        _body.Children.Add(caption);
        Apply();
    }

    private void Apply()
    {
        _body.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        _chevron.RenderTransform = new RotateTransform(_expanded ? 90 : 0, 4.5, 6);
    }

    private FrameworkElement Row(string identity)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(9) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var isPath = identity.Contains('\\') || identity.Contains('/');
        var icon = AppIdentity.IconOf(isPath ? identity : null);
        if (icon is not null)
        {
            var image = new Image { Source = icon, Width = 18, Height = 18 };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            grid.Children.Add(image);
        }

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, ToolTip = identity };
        text.Children.Add(UiKit.Text(AppIdentity.DisplayName(identity), 13, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis));
        if (isPath)
        {
            var folder = UiKit.Text(Path.GetDirectoryName(identity) ?? identity, 11, FontWeights.Normal, ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis);
            text.Children.Add(folder);
        }

        Grid.SetColumn(text, 2);
        grid.Children.Add(text);
        if (_accessory?.Invoke(identity) is { } accessory)
        {
            if (accessory is FrameworkElement fe)
            {
                fe.Margin = new Thickness(8, 0, 8, 0);
                fe.VerticalAlignment = VerticalAlignment.Center;
            }

            Grid.SetColumn(accessory, 3);
            grid.Children.Add(accessory);
        }

        if (_isLocked?.Invoke(identity) != true)
        {
            var remove = new Button
            {
                Content = UiKit.Icon(Glyphs.CloseCircle, 14, ThemeKeys.TextSecondary),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(2),
                Cursor = Cursors.Hand,
                ToolTip = Strings.Remove,
                VerticalAlignment = VerticalAlignment.Center,
            };
            remove.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, Strings.Remove);
            remove.Click += (_, _) =>
            {
                var list = _read();
                list.RemoveAll(x => string.Equals(x, identity, StringComparison.OrdinalIgnoreCase));
                _write(list);
                _removed?.Invoke(identity);
                Rebuild();
            };
            Grid.SetColumn(remove, 4);
            grid.Children.Add(remove);
        }

        return grid;
    }

    private void AddApp()
    {
        var current = _read();
        var picker = new AppPickerWindow(_loader, current, _allowBrowse, _emptyNote);
        var owner = Window.GetWindow(this);
        if (owner is not null)
        {
            picker.Owner = owner;
            picker.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        if (picker.ShowDialog() == true && picker.Result is { Length: > 0 } picked)
        {
            current = _read();
            if (!current.Any(x => string.Equals(x, picked, StringComparison.OrdinalIgnoreCase)))
            {
                current.Add(picked);
                _write(current);
                _added?.Invoke(picked);
            }

            _expanded = true;
            Rebuild();
        }
    }
}
