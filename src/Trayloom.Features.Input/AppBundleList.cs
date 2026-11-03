// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Trayloom.Core;
using Trayloom.Core.Native;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Input;

/// <summary>An app offered by the picker.</summary>
internal sealed record PickableApp(string Identity, string Name, string Location, bool Running);

/// <summary>Icon extraction for app identities (cached, frozen bitmaps).</summary>
internal static class AppIcons
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Get(string exePath)
    {
        return Cache.GetOrAdd(exePath, static path =>
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon is null)
                {
                    return null;
                }

                var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            catch
            {
                return null;
            }
        });
    }
}

/// <summary>
/// "Apps to leave alone" list (the original <c>AppBundleList</c>): a disclosure whose header shows the title and a count,
/// one row per app (icon, name, path, remove), an "Add an app..." button and a caption. Stores lower-case exe paths.
/// </summary>
internal sealed class AppBundleList : StackPanel
{
    private readonly IAppSettings _app;
    private readonly string _key;
    private readonly StackPanel _rows = new();
    private readonly TextBlock _count;
    private readonly bool _compact;
    private readonly string _emptyText;

    public AppBundleList(IAppSettings app, string key, string caption, string? title = null, string? addTitle = null, bool compact = false, string emptyText = "")
    {
        title ??= Strings.AppsToLeaveAlone;
        addTitle ??= Strings.AddAnApp;
        _app = app;
        _key = key;
        _compact = compact;
        _emptyText = emptyText;
        var current = Load();

        _count = UiKit.Text(string.Empty, 12, FontWeights.Normal, ThemeKeys.TextSecondary);
        _count.FontFamily = new FontFamily("Segoe UI Variable Text");
        var header = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(UiKit.Text(title, 12.5, FontWeights.Medium, ThemeKeys.TextPrimary));
        Grid.SetColumn(_count, 1);
        header.Children.Add(_count);

        var body = new StackPanel();
        body.Children.Add(_rows);
        var add = UiKit.Button(addTitle, ButtonKind.Bordered, 12, "", OnAdd);
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Margin = new Thickness(0, _rows.Children.Count > 0 ? 8 : 2, 0, 0);
        body.Children.Add(add);
        if (!string.IsNullOrEmpty(caption))
        {
            var text = InputUi.Caption(caption);
            text.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(text);
        }

        if (compact)
        {
            Children.Add(body);
        }
        else
        {
            Children.Add(new InputUi.Disclosure(header, body, current.Count > 0));
        }

        Rebuild();
    }

    private List<string> Load() => [.. (_app.Get<List<string>?>(_key) ?? []).Where(x => !string.IsNullOrWhiteSpace(x))];

    private void Save(List<string> list) => _app.Set(_key, list);

    private void Rebuild()
    {
        var list = Load();
        _count.Text = list.Count > 0 ? list.Count.ToString() : string.Empty;
        _rows.Children.Clear();
        if (list.Count == 0 && _compact && _emptyText.Length > 0)
        {
            _rows.Children.Add(InputUi.Caption(_emptyText));
        }

        foreach (var identity in list.OrderBy(AppIdentity.DisplayName, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            _rows.Children.Add(Row(identity));
        }
    }

    private FrameworkElement Row(string identity)
    {
        var size = _compact ? 20 : 18;
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(size + 9) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        FrameworkElement icon;
        var source = identity.Contains('\\') ? AppIcons.Get(identity) : null;
        if (source is not null)
        {
            icon = new Image { Source = source, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        }
        else
        {
            icon = UiKit.Icon("", size - 4, ThemeKeys.TextSecondary);
            icon.HorizontalAlignment = HorizontalAlignment.Left;
        }

        grid.Children.Add(icon);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(UiKit.Text(AppIdentity.DisplayName(identity), 13, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis));
        if (identity.Contains('\\'))
        {
            var path = UiKit.Text(System.IO.Path.GetDirectoryName(identity) ?? string.Empty, 11, FontWeights.Normal, ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis);
            path.FlowDirection = FlowDirection.LeftToRight;
            text.Children.Add(path);
        }

        text.ToolTip = identity;
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var remove = new Button
        {
            Content = UiKit.Icon("", 14, ThemeKeys.TextSecondary),
            ToolTip = Strings.Remove,
            Padding = new Thickness(4),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        remove.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.ButtonPlain);
        System.Windows.Automation.AutomationProperties.SetName(remove, Strings.Remove);
        remove.Click += (_, _) =>
        {
            var list = Load();
            list.RemoveAll(x => string.Equals(x, identity, StringComparison.OrdinalIgnoreCase));
            Save(list);
            Rebuild();
        };
        Grid.SetColumn(remove, 2);
        grid.Children.Add(remove);
        return grid;
    }

    private void OnAdd()
    {
        var picker = new AppPickerWindow(Load());
        var owner = InputUi.ActiveWindow();
        if (owner is not null)
        {
            picker.Owner = owner;
        }

        if (picker.ShowDialog() == true && picker.Picked is { Length: > 0 } picked)
        {
            var list = Load();
            if (!list.Any(x => string.Equals(x, picked, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(picked);
                Save(list);
                Rebuild();
            }
        }
    }
}

/// <summary>The "Choose app" dialog: searchable running and installed apps plus a Browse button.</summary>
internal sealed class AppPickerWindow : Window
{
    private readonly HashSet<string> _exclude;
    private readonly TextBox _search = new();
    private readonly StackPanel _list = new();
    private readonly TextBlock _status;
    private List<PickableApp> _apps = [];

    public AppPickerWindow(IEnumerable<string> excluded)
    {
        _exclude = new HashSet<string>(excluded, StringComparer.OrdinalIgnoreCase);
        Title = Strings.ChooseApp;
        Width = 520;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        this.SetResourceReference(BackgroundProperty, ThemeKeys.SettingsWindow);
        SourceInitialized += (_, _) => ThemeManager.BindWindow(this);

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(UiKit.Text(Strings.ChooseApp, 16, FontWeights.SemiBold, ThemeKeys.TextPrimary));
        var choose = UiKit.Button(Strings.ChooseAppEllipsis, ButtonKind.Bordered, 12, "", Browse);
        Grid.SetColumn(choose, 1);
        header.Children.Add(choose);
        var cancel = UiKit.Button(Strings.Cancel, ButtonKind.Bordered, 12, null, () => DialogResult = false);
        cancel.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(cancel, 2);
        header.Children.Add(cancel);
        root.Children.Add(header);

        _search.SetResourceReference(StyleProperty, ThemeKeys.TextBoxStyle);
        _search.Margin = new Thickness(0, 12, 0, 12);
        _search.ToolTip = Strings.SearchApps;
        var hint = UiKit.Text(Strings.SearchApps, 13, FontWeights.Normal, ThemeKeys.TextTertiary);
        hint.IsHitTestVisible = false;
        hint.Margin = new Thickness(10, 0, 0, 0);
        hint.VerticalAlignment = VerticalAlignment.Center;
        var searchHost = new Grid { Margin = new Thickness(0, 12, 0, 12) };
        _search.Margin = new Thickness(0);
        searchHost.Children.Add(_search);
        searchHost.Children.Add(hint);
        _search.TextChanged += (_, _) =>
        {
            hint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Render();
        };
        Grid.SetRow(searchHost, 1);
        root.Children.Add(searchHost);

        _status = UiKit.Text(Strings.Scanning, 13, FontWeights.Normal, ThemeKeys.TextSecondary);
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.Margin = new Thickness(0, 120, 0, 0);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list };
        var area = new Grid();
        area.Children.Add(scroll);
        area.Children.Add(_status);
        Grid.SetRow(area, 2);
        root.Children.Add(area);
        Content = root;

        Loaded += (_, _) =>
        {
            _search.Focus();
            Task.Run(Scan).ContinueWith(t => Dispatcher.BeginInvoke(() =>
            {
                _apps = t.IsCompletedSuccessfully ? t.Result : [];
                Render();
            }));
        };
    }

    /// <summary>Identity of the chosen app.</summary>
    public string? Picked { get; private set; }

    private void Browse()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Applications (*.exe)|*.exe", Title = Strings.ChooseApp, CheckFileExists = true };
        if (dialog.ShowDialog(this) == true)
        {
            Pick(dialog.FileName.ToLowerInvariant());
        }
    }

    private void Pick(string identity)
    {
        Picked = identity;
        DialogResult = true;
    }

    private void Render()
    {
        _list.Children.Clear();
        _status.Visibility = Visibility.Collapsed;
        var query = _search.Text.Trim();
        var shown = _apps.Where(a => !_exclude.Contains(a.Identity)
            && (query.Length == 0 || a.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) || a.Identity.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (shown.Count == 0)
        {
            _status.Text = _apps.Count == 0 && query.Length == 0 ? Strings.Scanning : Strings.NoAppsFound;
            _status.Visibility = Visibility.Visible;
            return;
        }

        foreach (var app in shown.Take(300))
        {
            _list.Children.Add(Row(app));
        }
    }

    private FrameworkElement Row(PickableApp app)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var source = AppIcons.Get(app.Identity);
        FrameworkElement icon = source is not null
            ? new Image { Source = source, Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Left }
            : UiKit.Icon("", 22, ThemeKeys.TextSecondary);
        grid.Children.Add(icon);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(UiKit.Text(app.Name, 13, FontWeights.Medium, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis));
        text.Children.Add(UiKit.Text(app.Location, 11, FontWeights.Normal, ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var border = new Border { Padding = new Thickness(8, 7, 8, 7), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = grid };
        border.MouseEnter += (_, _) => border.SetResourceReference(Border.BackgroundProperty, ThemeKeys.RowHover);
        border.MouseLeave += (_, _) => border.Background = Brushes.Transparent;
        border.MouseLeftButtonUp += (_, _) => Pick(app.Identity);
        return border;
    }

    private static List<PickableApp> Scan()
    {
        var result = new Dictionary<string, PickableApp>(StringComparer.OrdinalIgnoreCase);

        // Running apps: visible top-level windows with a title.
        var own = Environment.ProcessId;
        Win32.EnumWindows((hwnd, _) =>
        {
            try
            {
                if (!Win32.IsWindowVisible(hwnd) || Win32.IsCloaked(hwnd) || Win32.WindowText(hwnd).Length == 0)
                {
                    return true;
                }

                NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
                if (pid == 0 || pid == own)
                {
                    return true;
                }

                var path = AppIdentity.OfProcess(pid);
                if (path is not null && !result.ContainsKey(path))
                {
                    result[path] = new PickableApp(path, AppIdentity.DisplayName(path), path, true);
                }
            }
            catch
            {
                // skip
            }

            return true;
        }, 0);

        // Installed apps: Start menu shortcuts.
        foreach (var root in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        })
        {
            try
            {
                var programs = System.IO.Path.Combine(root, "Programs");
                if (!Directory.Exists(programs))
                {
                    continue;
                }

                var shell = Type.GetTypeFromProgID("WScript.Shell");
                dynamic? wsh = shell is null ? null : Activator.CreateInstance(shell);
                foreach (var lnk in Directory.EnumerateFiles(programs, "*.lnk", SearchOption.AllDirectories))
                {
                    try
                    {
                        string target = wsh?.CreateShortcut(lnk).TargetPath ?? string.Empty;
                        if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(target))
                        {
                            var path = target.ToLowerInvariant();
                            if (!result.ContainsKey(path))
                            {
                                result[path] = new PickableApp(path, System.IO.Path.GetFileNameWithoutExtension(lnk), path, false);
                            }
                        }
                    }
                    catch
                    {
                        // unreadable shortcut
                    }
                }
            }
            catch
            {
                // folder not accessible
            }
        }

        return [.. result.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)];
    }
}
