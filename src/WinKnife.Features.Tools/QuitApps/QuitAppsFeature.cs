// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

/// <summary>Quit apps (catalog id <c>quit-apps</c>): the list window plus the two global shortcuts.</summary>
public sealed class QuitAppsFeature : ToolsFeatureBase
{
    /// <inheritdoc />
    public override string Id => "quit-apps";

    /// <inheritdoc />
    public override IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("show", "Quit all apps", () => Safe(() => QuitAppsOps.Show(Ctx, false)), ""),
        new FeatureAction("showExceptFront", "Quit all apps except the front one", () => Safe(() => QuitAppsOps.Show(Ctx, true)), ""),
    ];

    /// <inheritdoc />
    protected override void RegisterDefaults(IAppSettings app)
    {
        ToolsDefaults.Register(app);
        app.Register(QuitAppsModel.KeptKey, new List<string>(QuitAppsModel.Mandatory));
    }

    /// <inheritdoc />
    protected override void OnStart()
    {
        BindRole("quitAll", () => QuitAppsOps.Show(Ctx, false));
        BindRole("quitAllExceptFront", () => QuitAppsOps.Show(Ctx, true));
    }

    /// <inheritdoc />
    protected override void OnStop() => QuitAppsWindow.CloseCurrent();
}

/// <summary>Quick tools page card "Quit apps" (02c 5.3).</summary>
public sealed class QuitAppsSettingsSection : ISettingsSection
{
    /// <inheritdoc />
    public string? FeatureId => "quit-apps";

    /// <inheritdoc />
    public string PageId => SettingsPages.QuickTools;

    /// <inheritdoc />
    public string? Anchor => "quitApps";

    /// <inheritdoc />
    public string? Title => "Quit apps";

    /// <inheritdoc />
    public IReadOnlyList<string> Keywords => ["Quit all apps", "Kept apps", "Quit all apps except the front one"];

    /// <inheritdoc />
    public int Order => 30;

    /// <inheritdoc />
    public FrameworkElement CreateView(ISettingsContext c)
    {
        ToolsDefaults.Register(c.App);
        var card = new SettingsCard("Quit apps");
        card.Add(UiKit.Button("Quit all apps", ButtonKind.Bordered, 13, "", () => QuitAppsOps.Show(c, false)));
        card.Add(new KeptAppsList(c));
        ShortcutSettingsRows.AddGlobalShortcut(card, c, "quitAll", "quitAppsShortcutEnabled", "Global shortcut: Quit all apps");
        ShortcutSettingsRows.AddGlobalShortcut(card, c, "quitAllExceptFront", "quitAppsExceptFrontShortcutEnabled", "Global shortcut: Quit all apps except the front one");
        card.AddCaption("Lists your open apps to quit one by one, all at once, or all but the one in front. Always shows the list before anything quits.");
        return card;
    }
}

/// <summary>The "Kept apps" disclosure list (<c>QuitAllExceptionsList</c>).</summary>
internal sealed class KeptAppsList : StackPanel
{
    private readonly ISettingsContext _c;
    private readonly StackPanel _body = new();
    private bool _open;

    public KeptAppsList(ISettingsContext c)
    {
        _c = c;
        var user = QuitAppsModel.Kept(c.App).Count(k => !QuitAppsModel.Mandatory.Contains(k));
        _open = user > 0;
        Rebuild();
    }

    private void Rebuild()
    {
        Children.Clear();
        _body.Children.Clear();
        var kept = QuitAppsModel.Kept(_c.App);
        var header = new Grid { Cursor = Cursors.Hand, Background = System.Windows.Media.Brushes.Transparent };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(UiKit.Text("Kept apps", 13));
        var count = UiKit.Text(kept.Count.ToString(), 12, FontWeights.Normal, ThemeKeys.TextSecondary);
        count.FontFamily = UiKit.DisplayFont;
        count.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(count, 1);
        header.Children.Add(count);
        var chevron = UiKit.Icon(Glyphs.ChevronRight, 9, ThemeKeys.TextSecondary, FontWeights.SemiBold);
        chevron.RenderTransformOrigin = new Point(0.5, 0.5);
        chevron.RenderTransform = new System.Windows.Media.RotateTransform(_open ? 90 : 0);
        Grid.SetColumn(chevron, 2);
        header.Children.Add(chevron);
        header.MouseLeftButtonUp += (_, _) =>
        {
            _open = !_open;
            Rebuild();
        };
        Children.Add(header);
        if (!_open)
        {
            return;
        }

        _body.Margin = new Thickness(0, 10, 0, 0);
        foreach (var key in kept.OrderBy(DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            if (!QuitAppsModel.Mandatory.Contains(key) && key.Contains('\\') && !File.Exists(key))
            {
                continue;
            }

            _body.Children.Add(Row(key));
        }

        var add = UiKit.Button("Add app…", ButtonKind.Bordered, 11.5, "", AddApp);
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Margin = new Thickness(0, 8, 0, 0);
        _body.Children.Add(add);
        var caption = UiKit.Text("Quit all leaves these apps running. File Explorer, WinKnife and Phone Link are always kept. You can still quit a kept app from its own row.", 11.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        caption.Margin = new Thickness(0, 8, 0, 0);
        _body.Children.Add(caption);
        Children.Add(_body);
    }

    private static string DisplayName(string key) =>
        key == QuitAppsModel.ExplorerKey ? "File Explorer"
        : key == "phoneexperiencehost.exe" ? "Phone Link"
        : key.Contains('\\') && File.Exists(key) ? QuitAppsModel.NameOf(key)
        : key;

    private FrameworkElement Row(string key)
    {
        var mandatory = QuitAppsModel.Mandatory.Contains(key);
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (key.Contains('\\') && File.Exists(key))
        {
            grid.Children.Add(new Image { Width = 18, Height = 18, Source = Plat.ShellImage(key, 18) });
        }
        else
        {
            grid.Children.Add(UiKit.Icon("", 13, ThemeKeys.TextSecondary));
        }

        var name = UiKit.Text(DisplayName(key), 12.5, FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
        name.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        FrameworkElement trailing;
        if (mandatory)
        {
            trailing = UiKit.Icon(Glyphs.Lock, 11, ThemeKeys.TextTertiary);
        }
        else
        {
            var remove = UiKit.Icon("", 14, ThemeKeys.TextSecondary);
            remove.Cursor = Cursors.Hand;
            remove.ToolTip = "Remove";
            remove.MouseLeftButtonUp += (_, _) => Remove(key);
            trailing = remove;
        }

        Grid.SetColumn(trailing, 2);
        grid.Children.Add(trailing);
        return grid;
    }

    private void Remove(string key)
    {
        var list = _c.App.Get<List<string>>(QuitAppsModel.KeptKey, []).Where(k => !k.Equals(key, StringComparison.OrdinalIgnoreCase)).ToList();
        _c.App.Set(QuitAppsModel.KeptKey, list);
        Rebuild();
    }

    private void AddApp()
    {
        var kept = QuitAppsModel.Kept(_c.App);
        var path = AppPickerDialog.Pick(kept);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var list = _c.App.Get<List<string>>(QuitAppsModel.KeptKey, []).ToList();
        list.Add(path.ToLowerInvariant());
        _c.App.Set(QuitAppsModel.KeptKey, list);
        _open = true;
        Rebuild();
    }
}

/// <summary>A small picker: running apps with a window, plus "Browse…" for any .exe. Returns the exe path or null.</summary>
internal static class AppPickerDialog
{
    public static string? Pick(IReadOnlyCollection<string> exclude)
    {
        string? result = null;
        var window = new Window
        {
            Title = "Add app",
            Width = 380,
            Height = 440,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
        };
        window.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.WindowStyle);
        window.SourceInitialized += (_, _) => ThemeManager.BindWindow(window);
        var search = new TextBox { Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(6, 4, 6, 4) };
        search.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.TextBoxStyle);
        var list = new ListBox { BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent };
        var apps = QuitAppsModel.Load(0, []).Where(a => !exclude.Any(k => QuitAppsModel.KeyMatches(k, a.Path))).ToList();

        void Fill()
        {
            list.Items.Clear();
            foreach (var app in apps.Where(a => a.Name.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase)))
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3), Tag = app.Path };
                row.Children.Add(new Image { Width = 20, Height = 20, Source = Plat.ShellImage(app.Path, 20), Margin = new Thickness(0, 0, 8, 0) });
                row.Children.Add(UiKit.Text(app.Name, 13));
                list.Items.Add(row);
            }
        }

        search.TextChanged += (_, _) => Fill();
        Fill();
        void Accept()
        {
            if (list.SelectedItem is StackPanel { Tag: string path })
            {
                result = path;
                window.Close();
            }
        }

        list.MouseDoubleClick += (_, _) => Accept();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var browse = UiKit.Button("Browse…", ButtonKind.Bordered, 12, null, () =>
        {
            var dialog = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) };
            if (dialog.ShowDialog(window) == true)
            {
                result = dialog.FileName;
                window.Close();
            }
        });
        var cancel = UiKit.Button("Cancel", ButtonKind.Bordered, 12, null, window.Close);
        var add = UiKit.Button("Add app…", ButtonKind.Prominent, 12, null, Accept);
        foreach (var b in new[] { browse, cancel, add })
        {
            b.Margin = new Thickness(8, 0, 0, 0);
            buttons.Children.Add(b);
        }

        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(search, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(search);
        root.Children.Add(buttons);
        root.Children.Add(list);
        window.Content = root;
        window.ShowDialog();
        return result;
    }
}
