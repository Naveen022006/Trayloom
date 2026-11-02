// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinKnife.Core.Ui;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;
using WinKnife.Core;

namespace WinKnife.Features.Files;

/// <summary>One choice of the app picker.</summary>
internal sealed record PickableApp(string Name, string Identity, string? Path);

/// <summary>"Add an app…" dialog: searchable list of running and installed apps plus Browse for any executable.</summary>
internal sealed class AppPickerDialog : Window
{
    private readonly List<PickableApp> _all = [];
    private readonly HashSet<string> _exclude;
    private readonly StackPanel _list = new();
    private readonly TextBox _search;
    private readonly TextBlock _status = UiKit.Text("…", 11.5, FontWeights.Normal, ThemeKeys.TextSecondary);

    private AppPickerDialog(IEnumerable<string> exclude)
    {
        _exclude = exclude.Select(Lists.Key).ToHashSet();
        Title = Loc.T("Add an app");
        Width = 420;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        this.Themed(BackgroundProperty, ThemeKeys.SettingsWindow);
        ThemeManager.BindWindow(this);

        var root = new DockPanel { Margin = new Thickness(16) };
        var searchGrid = Ui.PlaceholderBox(out _search, Loc.T("Search apps"));
        DockPanel.SetDock(searchGrid, Dock.Top);
        _search.TextChanged += (_, _) => Fill();
        root.Children.Add(searchGrid);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var browse = UiKit.Button("Browse…", ButtonKind.Bordered, 12, null, Browse);
        var cancel = UiKit.Button(Strings.Clipboard.Cancel, ButtonKind.Bordered, 12, null, () => DialogResult = false);
        cancel.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(browse);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        _status.Margin = new Thickness(2, 8, 0, 0);
        DockPanel.SetDock(_status, Dock.Top);
        root.Children.Add(_status);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list, Margin = new Thickness(0, 8, 0, 0) };
        root.Children.Add(scroll);
        Content = root;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
            }
        };
        Loaded += (_, _) =>
        {
            _search.Focus();
            LoadApps();
        };
    }

    /// <summary>The picked identity (lower-case executable name), or null.</summary>
    public string? Result { get; private set; }

    /// <summary>Shows the dialog; returns the identity of the chosen app, or null when cancelled.</summary>
    public static string? Pick(Window? owner, IEnumerable<string> exclude)
    {
        var dialog = new AppPickerDialog(exclude) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private void Browse()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("Choose an app"),
            Filter = "Applications (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) == true)
        {
            var identity = System.IO.Path.GetFileName(dialog.FileName).ToLowerInvariant();
            AppIdentity.Remember(identity, System.IO.Path.GetFileNameWithoutExtension(identity), dialog.FileName);
            Result = identity;
            DialogResult = true;
        }
    }

    private void LoadApps()
    {
        var dispatcher = Dispatcher;
        Task.Run(() =>
        {
            var found = new Dictionary<string, PickableApp>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var process in Process.GetProcesses())
                {
                    try
                    {
                        if (process.MainWindowHandle == 0)
                        {
                            continue;
                        }

                        var path = WinApi.GetProcessPath((uint)process.Id);
                        if (string.IsNullOrEmpty(path))
                        {
                            continue;
                        }

                        var identity = System.IO.Path.GetFileName(path).ToLowerInvariant();
                        var name = identity;
                        try
                        {
                            var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
                            name = string.IsNullOrWhiteSpace(description) ? System.IO.Path.GetFileNameWithoutExtension(path) : description;
                        }
                        catch (Exception)
                        {
                            name = System.IO.Path.GetFileNameWithoutExtension(path);
                        }

                        found.TryAdd(identity, new PickableApp(name, identity, path));
                    }
                    catch (Exception)
                    {
                        // a process that exited or denies access
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch (Exception)
            {
                // best effort
            }

            dispatcher.BeginInvoke(() =>
            {
                _all.AddRange(found.Values);
                Fill();
            });

            var installed = ScanStartMenu();
            dispatcher.BeginInvoke(() =>
            {
                foreach (var app in installed)
                {
                    if (!found.ContainsKey(app.Identity))
                    {
                        found[app.Identity] = app;
                        _all.Add(app);
                    }
                }

                _status.Text = string.Empty;
                Fill();
            });
        });
    }

    private static List<PickableApp> ScanStartMenu()
    {
        var result = new List<PickableApp>();
        var thread = new Thread(() =>
        {
            try
            {
                var type = Type.GetTypeFromProgID("WScript.Shell");
                if (type is null)
                {
                    return;
                }

                dynamic shell = Activator.CreateInstance(type)!;
                foreach (var root in new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                })
                {
                    if (!Directory.Exists(root))
                    {
                        continue;
                    }

                    foreach (var link in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                    {
                        try
                        {
                            string target = shell.CreateShortcut(link).TargetPath;
                            if (string.IsNullOrEmpty(target) || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(target))
                            {
                                continue;
                            }

                            var identity = System.IO.Path.GetFileName(target).ToLowerInvariant();
                            if (identity.Contains("uninstall", StringComparison.Ordinal))
                            {
                                continue;
                            }

                            result.Add(new PickableApp(System.IO.Path.GetFileNameWithoutExtension(link), identity, target));
                        }
                        catch (Exception)
                        {
                            // unreadable shortcut
                        }
                    }
                }
            }
            catch (Exception)
            {
                // COM unavailable
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(20));
        return result;
    }

    private void Fill()
    {
        _list.Children.Clear();
        var query = _search.Text.Trim();
        var matches = _all
            .Where(a => !_exclude.Contains(a.Identity))
            .Where(a => query.Length == 0 || a.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || a.Identity.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Take(300);
        foreach (var app in matches)
        {
            _list.Children.Add(Row(app));
        }
    }

    private UIElement Row(PickableApp app)
    {
        var row = new Border { Padding = new Thickness(8, 6, 8, 6), CornerRadius = new CornerRadius(7), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = new Image { Width = 20, Height = 20, Source = app.Path is null ? null : ShellIcons.ForExecutable(app.Path), VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(icon);
        var text = new StackPanel();
        text.Children.Add(UiKit.Text(app.Name, 12.5, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis));
        text.Children.Add(UiKit.Text(app.Identity, 10.5, FontWeights.Normal, ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        row.Child = grid;
        row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, ThemeKeys.RowHover);
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, _) =>
        {
            AppIdentity.Remember(app.Identity, app.Name, app.Path);
            Result = app.Identity;
            DialogResult = true;
        };
        return row;
    }
}

/// <summary>
/// Vorssaint's <c>AppBundleList</c>: an expander whose header is <c>title ..... count</c>; one row per app (icon, name, path,
/// remove button), then "+ Add an app…" and a caption. Apps are identified by lower-case executable name.
/// </summary>
internal sealed class AppListControl : StackPanel
{
    private readonly Func<List<string>> _get;
    private readonly Action<List<string>> _set;
    private readonly string _title;
    private readonly string _addTitle;
    private readonly string _caption;
    private readonly string _removeLabel;
    private readonly bool _useExpander;
    private bool _expanded;

    public AppListControl(string title, string addTitle, string caption, Func<List<string>> get, Action<List<string>> set, string removeLabel = "Remove", bool useExpander = true)
    {
        _title = title;
        _addTitle = addTitle;
        _caption = caption;
        _get = get;
        _set = set;
        _removeLabel = removeLabel;
        _useExpander = useExpander;
        _expanded = !useExpander || get().Count > 0;
        Build();
    }

    private void Build()
    {
        Children.Clear();
        var list = _get().OrderBy(a => AppIdentity.Resolve(a).Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (_useExpander)
        {
            var header = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(UiKit.Text(_title, 13, FontWeights.Medium, ThemeKeys.TextPrimary));
            if (list.Count > 0)
            {
                var count = UiKit.Text(list.Count.ToString(), 12, FontWeights.Normal, ThemeKeys.TextSecondary);
                count.Margin = new Thickness(0, 0, 8, 0);
                Grid.SetColumn(count, 1);
                grid.Children.Add(count);
            }

            var chevron = UiKit.Icon(_expanded ? G.ChevronDown : G.ChevronRight, 9, ThemeKeys.TextSecondary, FontWeights.SemiBold);
            Grid.SetColumn(chevron, 2);
            grid.Children.Add(chevron);
            header.Child = grid;
            header.MouseLeftButtonUp += (_, _) =>
            {
                _expanded = !_expanded;
                Build();
            };
            Children.Add(header);
        }
        else
        {
            Children.Add(UiKit.Text(_title, 13, FontWeights.SemiBold, ThemeKeys.TextPrimary));
        }

        if (_expanded)
        {
            var body = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            if (list.Count == 0 && !_useExpander)
            {
                body.Children.Add(UiKit.Text(Strings.Shelf.ExclusionsEmpty, 13, FontWeights.Normal, ThemeKeys.TextSecondary));
            }

            foreach (var identity in list)
            {
                body.Children.Add(Row(identity));
            }

            var add = UiKit.Button(_addTitle, ButtonKind.Bordered, 11.5, G.Plus, AddApp);
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Margin = new Thickness(0, list.Count > 0 ? 8 : 4, 0, 0);
            body.Children.Add(add);
            Children.Add(body);
        }

        var caption = Ui.Caption(_caption);
        caption.Margin = new Thickness(0, 8, 0, 0);
        Children.Add(caption);
    }

    private UIElement Row(string identity)
    {
        var (name, path) = AppIdentity.Resolve(identity);
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new Image { Width = 18, Height = 18, Source = path is null ? null : ShellIcons.ForExecutable(path), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        grid.Children.Add(icon);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(UiKit.Text(name, 13, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis));
        var caption = path ?? identity;
        text.Children.Add(UiKit.Text(caption, 10.5, FontWeights.Normal, ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var remove = Ui.IconButton(G.CloseCircle, _removeLabel, () =>
        {
            var next = _get().Where(a => !string.Equals(Lists.Key(a), Lists.Key(identity), StringComparison.Ordinal)).ToList();
            _set(next);
            Build();
        }, 22, 12, 11, filled: false);
        Grid.SetColumn(remove, 2);
        grid.Children.Add(remove);
        return grid;
    }

    private void AddApp()
    {
        var current = _get();
        var picked = AppPickerDialog.Pick(Window.GetWindow(this), current);
        if (picked is null || current.Any(a => Lists.Key(a) == Lists.Key(picked)))
        {
            return;
        }

        _expanded = true;
        _set([.. current, picked]);
        Build();
    }
}
