// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.App;

/// <summary>Context for settings sections.</summary>
internal sealed class SettingsContext(IContributionServices services, string pageId, string? anchor, Action rebuild) : ContributionServicesBase(services), ISettingsContext
{
    public string PageId { get; } = pageId;

    public string? RequestedAnchor { get; } = anchor;

    public void RequestRebuild() => rebuild();
}

/// <summary>A section host that can scroll into view and tint itself (anchor focus, spec 1.3).</summary>
internal sealed class AnchorHost : Border
{
    public AnchorHost(string? anchor, UIElement child)
    {
        Anchor = anchor;
        CornerRadius = new CornerRadius(16);
        Child = child;
    }

    public string? Anchor { get; }

    public void Pulse()
    {
        var accent = ThemeManager.AccentColor;
        var brush = new SolidColorBrush(Color.FromArgb(0x38, accent.R, accent.G, accent.B));
        Background = brush;
        var animation = new ColorAnimation(Color.FromArgb(0x38, accent.R, accent.G, accent.B), Color.FromArgb(0, accent.R, accent.G, accent.B), new Duration(TimeSpan.FromMilliseconds(1200)))
        {
            BeginTime = TimeSpan.FromMilliseconds(300),
        };
        animation.Completed += (_, _) => Background = null;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }
}

/// <summary>The "WinKnife Settings" window: sidebar with search, back/forward history, anchor routing and the page composer.</summary>
internal sealed class SettingsWindow : Window
{
    private const double MinWidthDip = 772;
    private const double MinHeightDip = 528;

    private readonly ShellServices _s;
    private readonly SettingsRouter _router;
    private readonly FeaturesPageState _hubState = new();
    private readonly Dictionary<string, bool> _collapsed = new(StringComparer.Ordinal);
    private readonly StackPanel _sidebarList = new();
    private readonly StackPanel _resultsList = new();
    private readonly TextBox _search = new();
    private readonly Border _clearButton = new();
    private readonly ScrollViewer _sidebarScroll = new();
    private readonly ScrollViewer _detailScroll = new();
    private readonly Border _detailHost = new();
    private readonly Button _backButton;
    private readonly Button _forwardButton;
    private readonly ColumnDefinition _sidebarColumn = new() { Width = new GridLength(210) };
    private readonly Dictionary<string, Border> _rowViews = new(StringComparer.Ordinal);
    private int _selectedResult;
    private List<SearchEntry> _results = [];
    private bool _sidebarVisible = true;
    private int _builtRevision = -1;
    private Guid _builtRequest;
    private DispatcherTimer? _sizeSaveTimer;

    public SettingsWindow(ShellServices services)
    {
        _s = services;
        Title = Loc.T("WinKnife Settings");
        MinWidth = MinWidthDip;
        MinHeight = MinHeightDip;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextOptions.GetTextFormattingMode(this));
        this.Themed(StyleProperty, ThemeKeys.WindowStyle);
        FontFamily = UiKit.TextFont;
        ThemeManager.BindWindow(this);

        var saved = (_s.App.Get<double>(AppKeys.SettingsWindowWidth), _s.App.Get<double>(AppKeys.SettingsWindowHeight));
        var workHeight = SystemParameters.WorkArea.Height;
        Width = saved.Item1 >= MinWidthDip && saved.Item2 >= MinHeightDip ? saved.Item1 : MinWidthDip;
        Height = saved.Item1 >= MinWidthDip && saved.Item2 >= MinHeightDip ? saved.Item2 : Math.Max(MinHeightDip, Math.Min(838, workHeight - 40));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _hubState.Tab = Math.Clamp(_s.App.Get<int>(AppKeys.FeaturesTabSelected), 0, 1);
        _router = new SettingsRouter(v => SidebarModel.IsPageVisible(_s.Runtime, v));
        _router.Navigated += OnNavigated;
        _backButton = ToolbarButton(Glyphs.Back, Loc.T("Back"), () => _router.Back());
        _forwardButton = ToolbarButton(Glyphs.Forward, Loc.T("Forward"), () => _router.Forward());
        BuildChrome();
        _s.Runtime.Changed += OnRuntimeChanged;
        _s.Contributions.Changed += OnContributionsChanged;
        Closed += (_, _) =>
        {
            _s.Runtime.Changed -= OnRuntimeChanged;
            _s.Contributions.Changed -= OnContributionsChanged;
        };
        SizeChanged += (_, _) => ScheduleSizeSave();
        Loaded += (_, _) => FitToWorkArea();
        PreviewKeyDown += OnPreviewKeyDown;
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
        ThemeManager.ThemeChanged += OnThemeChanged;
        Closed += (_, _) => ThemeManager.ThemeChanged -= OnThemeChanged;
    }

    /// <summary>
    /// The saved / default size is in DIPs of the primary monitor; on a smaller or more scaled monitor the window could be taller
    /// than the work area. Clamp the size to the monitor the window opened on and keep it fully on screen.
    /// </summary>
    private void FitToWorkArea()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            var area = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
            var dpi = VisualTreeHelper.GetDpi(this);
            var maxWidth = area.Width / dpi.DpiScaleX - 16;
            var maxHeight = area.Height / dpi.DpiScaleY - 16;
            if (Height > maxHeight)
            {
                Height = Math.Max(MinHeight, maxHeight);
            }

            if (Width > maxWidth)
            {
                Width = Math.Max(MinWidth, maxWidth);
            }

            var left = area.Left / dpi.DpiScaleX;
            var top = area.Top / dpi.DpiScaleY;
            var right = area.Right / dpi.DpiScaleX;
            var bottom = area.Bottom / dpi.DpiScaleY;
            if (Left < left || Left + Width > right)
            {
                Left = Math.Max(left, left + ((right - left) - Width) / 2);
            }

            if (Top < top || Top + Height > bottom)
            {
                Top = Math.Max(top, top + ((bottom - top) - Height) / 2);
            }
        }
        catch (Exception ex)
        {
            _s.Log.Warn("Could not fit Settings to the work area: " + ex.Message);
        }
    }

    public void SetSearchForDevelopment(string query) => _search.Text = query;

    public void Navigate(string destination, string? targetFeature = null, string? sidebarFeature = null)
    {
        _router.Request(destination, targetFeature, sidebarFeature);
    }

    // ------------------------------------------------------------------------------------------------ chrome

    private Button ToolbarButton(string glyph, string tooltip, Action onClick)
    {
        var b = new Button
        {
            Content = UiKit.Icon(glyph, 13, ThemeKeys.TextPrimary),
            Width = 30,
            Height = 26,
            ToolTip = tooltip,
            Margin = new Thickness(0, 0, 2, 0),
        };
        b.SetResourceReference(StyleProperty, ThemeKeys.ButtonPlain);
        b.Click += (_, _) => onClick();
        return b;
    }

    private void BuildChrome()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(_sidebarColumn);
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.SetResourceReference(Panel.BackgroundProperty, ThemeKeys.SettingsWindow);
        Content = root;

        // Toolbar: sidebar toggle, back, forward.
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 6, 0, 0) };
        toolbar.Children.Add(ToolbarButton(Glyphs.SidebarToggle, Loc.T("Hide Sidebar"), ToggleSidebar));
        toolbar.Children.Add(_backButton);
        toolbar.Children.Add(_forwardButton);
        Grid.SetColumnSpan(toolbar, 2);
        root.Children.Add(toolbar);

        // Sidebar: search + list.
        var sidebar = new Grid { Margin = new Thickness(0) };
        sidebar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        sidebar.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        sidebar.SetResourceReference(Panel.BackgroundProperty, ThemeKeys.SettingsWindow);
        Grid.SetRow(sidebar, 1);
        root.Children.Add(sidebar);
        sidebar.Children.Add(BuildSearchBox());

        _sidebarScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _sidebarScroll.Content = _sidebarList;
        _sidebarScroll.Padding = new Thickness(10, 0, 10, 10);
        _resultsList.Margin = new Thickness(0);
        Grid.SetRow(_sidebarScroll, 1);
        sidebar.Children.Add(_sidebarScroll);

        // Detail.
        _detailScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _detailScroll.Content = _detailHost;
        _detailScroll.SetResourceReference(BackgroundProperty, ThemeKeys.SettingsWindow);
        Grid.SetColumn(_detailScroll, 1);
        Grid.SetRow(_detailScroll, 1);
        root.Children.Add(_detailScroll);
    }

    private UIElement BuildSearchBox()
    {
        var box = new Border { CornerRadius = new CornerRadius(14), Margin = new Thickness(12, 4, 12, 10), Padding = new Thickness(7, 5, 7, 5) };
        box.SetResourceReference(Border.BackgroundProperty, ThemeKeys.SettingsControl);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var glyph = UiKit.Icon(Glyphs.Search, 12, ThemeKeys.TextSecondary);
        glyph.Margin = new Thickness(2, 0, 7, 0);
        grid.Children.Add(glyph);

        _search.BorderThickness = new Thickness(0);
        _search.Background = Brushes.Transparent;
        _search.FontSize = 13;
        _search.Padding = new Thickness(0);
        _search.VerticalContentAlignment = VerticalAlignment.Center;
        _search.SetResourceReference(TextBox.ForegroundProperty, ThemeKeys.TextPrimary);
        _search.SetResourceReference(TextBox.CaretBrushProperty, ThemeKeys.TextPrimary);
        var placeholder = UiKit.Text(Loc.T("Search settings"), 13, FontWeights.Normal, ThemeKeys.TextSecondary);
        placeholder.IsHitTestVisible = false;
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        var host = new Grid();
        host.Children.Add(placeholder);
        host.Children.Add(_search);
        Grid.SetColumn(host, 1);
        grid.Children.Add(host);

        _clearButton.Child = UiKit.Icon(Glyphs.CloseCircle, 12, ThemeKeys.TextSecondary);
        _clearButton.Cursor = Cursors.Hand;
        _clearButton.Visibility = Visibility.Collapsed;
        _clearButton.Background = Brushes.Transparent;
        _clearButton.MouseLeftButtonUp += (_, _) => _search.Text = string.Empty;
        Grid.SetColumn(_clearButton, 2);
        grid.Children.Add(_clearButton);
        box.Child = grid;

        _search.TextChanged += (_, _) =>
        {
            placeholder.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _clearButton.Visibility = _search.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            RunSearch();
        };
        _search.PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Escape:
                    _search.Text = string.Empty;
                    e.Handled = true;
                    break;
                case Key.Down when _results.Count > 0:
                    SelectResult((_selectedResult + 1) % _results.Count);
                    e.Handled = true;
                    break;
                case Key.Up when _results.Count > 0:
                    SelectResult((_selectedResult - 1 + _results.Count) % _results.Count);
                    e.Handled = true;
                    break;
                case Key.Return when _results.Count > 0:
                    OpenResult(_results[_selectedResult]);
                    e.Handled = true;
                    break;
            }
        };
        return box;
    }

    private void ToggleSidebar()
    {
        _sidebarVisible = !_sidebarVisible;
        _sidebarColumn.Width = _sidebarVisible ? new GridLength(210) : new GridLength(0);
        _sidebarColumn.MinWidth = 0;
    }

    // ------------------------------------------------------------------------------------------------ navigation

    private void OnNavigated()
    {
        // Remember the page so "Settings" (tray menu, panel footer, Start menu shortcut) reopens where the user left off.
        if (_router.Current is { } visited)
        {
            _s.App.Set(AppKeys.SettingsLastPage, visited.Destination);
        }

        if (_search.Text.Length > 0)
        {
            _search.Text = string.Empty;
        }

        RebuildAll(scrollAnchor: true);
    }

    private void OnRuntimeChanged()
    {
        Dispatcher.BeginInvoke(() =>
        {
            _router.EnsureVisible();
            RebuildAll(scrollAnchor: false, keepScroll: true);
        });
    }

    private void OnContributionsChanged() => Dispatcher.BeginInvoke(() => RebuildAll(false, keepScroll: true));

    private void OnThemeChanged(bool light) => Dispatcher.BeginInvoke(() => RebuildAll(false, keepScroll: true));

    private void RebuildAll(bool scrollAnchor, bool keepScroll = false)
    {
        var visit = _router.Current;
        if (visit is null)
        {
            return;
        }

        RebuildSidebar(visit);
        var offset = keepScroll ? _detailScroll.VerticalOffset : 0;
        var content = PageComposer.Build(_s, visit, _hubState, _router, () => RebuildAll(false, keepScroll: true), out var anchors);
        var wrapper = new Border { Padding = new Thickness(22), Child = new Border { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Stretch, Child = content } };
        _detailHost.Child = wrapper;
        _builtRevision = _s.Runtime.Revision;
        _builtRequest = _router.RequestId;
        _backButton.IsEnabled = _router.CanGoBack;
        _forwardButton.IsEnabled = _router.CanGoForward;
        _detailScroll.ScrollToVerticalOffset(offset);

        if (scrollAnchor)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                _detailScroll.UpdateLayout();
                var target = visit.Anchor is { } a ? anchors.FirstOrDefault(h => h.Anchor == a) : null;
                if (target is not null)
                {
                    var y = target.TransformToAncestor((Visual)_detailScroll.Content).Transform(new Point(0, 0)).Y;
                    _detailScroll.ScrollToVerticalOffset(Math.Max(0, y - 6));
                    target.Pulse();
                }
                else if (_hubState.PendingReveal is not null)
                {
                    _hubState.RevealRow?.Invoke();
                }
            });
        }
    }

    // ------------------------------------------------------------------------------------------------ sidebar

    private void RebuildSidebar(Visit current)
    {
        _sidebarList.Children.Clear();
        _rowViews.Clear();
        var sections = SidebarModel.Visible(_s.Runtime);
        var selectedId = ResolveSelectedRow(sections.SelectMany(s => s.Rows), current)?.Id;
        foreach (var (section, rows) in sections)
        {
            var containsSelected = rows.Any(r => r.Id == selectedId);
            if (containsSelected)
            {
                _collapsed[section.Title] = false;
            }

            var collapsed = _collapsed.GetValueOrDefault(section.Title);
            var header = BuildSectionHeader(section.Title, collapsed);
            _sidebarList.Children.Add(header);
            if (collapsed)
            {
                continue;
            }

            foreach (var row in rows)
            {
                var view = BuildRow(row, row.Id == selectedId);
                _rowViews[row.Id] = view;
                _sidebarList.Children.Add(view);
            }
        }

        if (_search.Text.Length == 0)
        {
            _sidebarScroll.Content = _sidebarList;
        }
    }

    private static SidebarRow? ResolveSelectedRow(IEnumerable<SidebarRow> rows, Visit visit)
    {
        var list = rows.ToList();
        if (visit.SidebarFeature is { } feature && list.FirstOrDefault(r => r.FeatureId == feature && PageOf(r.Destination) == visit.Page) is { } byFeature)
        {
            return byFeature;
        }

        return list.FirstOrDefault(r => r.Destination == visit.Destination)
            ?? list.FirstOrDefault(r => PageOf(r.Destination) == visit.Page && AnchorOf(r.Destination) == visit.Anchor)
            ?? list.FirstOrDefault(r => PageOf(r.Destination) == visit.Page && r.FeatureId is null && visit.Anchor is null)
            ?? list.FirstOrDefault(r => PageOf(r.Destination) == visit.Page);
    }

    private static string PageOf(string destination) => SettingsPages.Split(destination).Page;

    private static string? AnchorOf(string destination) => SettingsPages.Split(destination).Anchor;

    private UIElement BuildSectionHeader(string title, bool collapsed)
    {
        var grid = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(6, 12, 0, 3) };
        grid.Children.Add(UiKit.Icon(collapsed ? Glyphs.ChevronRightSmall : Glyphs.ChevronDownSmall, 8, ThemeKeys.TextSecondary, FontWeights.SemiBold));
        var text = UiKit.Text(title, 12, FontWeights.SemiBold, ThemeKeys.TextSecondary);
        text.Margin = new Thickness(5, 0, 0, 0);
        grid.Children.Add(text);
        grid.MouseLeftButtonUp += (_, _) =>
        {
            _collapsed[title] = !collapsed;
            RebuildSidebar(_router.Current!);
        };
        return grid;
    }

    private Border BuildRow(SidebarRow row, bool selected)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = UiKit.Icon(row.Glyph, 14, selected ? ThemeKeys.OnAccent : ThemeKeys.Accent);
        grid.Children.Add(icon);
        var title = UiKit.Text(row.Title, 13, selected ? FontWeights.SemiBold : FontWeights.Normal, selected ? ThemeKeys.OnAccent : ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
        title.Margin = new Thickness(8, 0, 0, 0);
        title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);
        if (row.Badge is not null)
        {
            var pill = new BadgePill(row.Badge) { Margin = new Thickness(4, 0, 0, 0) };
            Grid.SetColumn(pill, 2);
            grid.Children.Add(pill);
        }

        var border = new Border
        {
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(8, 7, 8, 7),
            Margin = new Thickness(0, 1, 0, 1),
            Child = grid,
            Cursor = Cursors.Hand,
            ToolTip = row.Title,
            Background = Brushes.Transparent,
        };
        if (selected)
        {
            border.SetResourceReference(Border.BackgroundProperty, ThemeKeys.Accent);
        }
        else
        {
            border.MouseEnter += (_, _) => border.SetResourceReference(Border.BackgroundProperty, ThemeKeys.RowHover);
            border.MouseLeave += (_, _) => border.Background = Brushes.Transparent;
        }

        border.MouseLeftButtonUp += (_, _) => Navigate(row.Destination, sidebarFeature: row.FeatureId);
        return border;
    }

    // ------------------------------------------------------------------------------------------------ search

    private void RunSearch()
    {
        var query = _search.Text.Trim();
        if (query.Length == 0)
        {
            _results = [];
            _sidebarScroll.Content = _sidebarList;
            return;
        }

        _results = SettingsSearch.Search(_s, query);
        _selectedResult = 0;
        RenderResults();
        _sidebarScroll.Content = _resultsList;
    }

    private void RenderResults()
    {
        _resultsList.Children.Clear();
        if (_results.Count == 0)
        {
            var none = UiKit.Text(Loc.T("No results"), 12, FontWeights.Normal, ThemeKeys.TextSecondary);
            none.Margin = new Thickness(8, 10, 8, 0);
            _resultsList.Children.Add(none);
            var browse = UiKit.Button(Loc.T("Browse all features"), ButtonKind.Bordered, 11.5, onClick: () =>
            {
                _search.Text = string.Empty;
                Navigate(SettingsPages.Features);
            });
            browse.HorizontalAlignment = HorizontalAlignment.Left;
            browse.Margin = new Thickness(8, 8, 8, 0);
            _resultsList.Children.Add(browse);
            return;
        }

        for (var i = 0; i < _results.Count; i++)
        {
            var entry = _results[i];
            var index = i;
            var selected = i == _selectedResult;
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(21) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = UiKit.Icon(entry.Glyph, 13, ThemeKeys.Accent);
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Margin = new Thickness(0, 2, 0, 0);
            row.Children.Add(icon);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(UiKit.Text(entry.Title, 13, entry.IsPage ? FontWeights.SemiBold : FontWeights.Normal, selected ? ThemeKeys.Accent : (entry.Dimmed ? ThemeKeys.TextSecondary : ThemeKeys.TextPrimary), trimming: TextTrimming.CharacterEllipsis));
            if (!entry.IsPage && entry.Description is { Length: > 0 } description)
            {
                var sub = UiKit.Text(description, 10.5, FontWeights.Normal, ThemeKeys.TextTertiary, trimming: TextTrimming.CharacterEllipsis);
                sub.ToolTip = description;
                text.Children.Add(sub);
            }

            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            if (entry.CanInstall && entry.FeatureId is { } installId)
            {
                // Not installed yet: install straight from the result.
                var install = UiKit.Button(Loc.T("Install"), ButtonKind.Bordered, 11, onClick: () => InstallFromResult(installId));
                install.Padding = new Thickness(8, 2, 8, 2);
                install.MinHeight = 0;
                install.Margin = new Thickness(0, 5, 0, 1);
                install.HorizontalAlignment = HorizontalAlignment.Left;
                install.ToolTip = "Install " + entry.Title;
                text.Children.Add(install);
            }

            var border = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(entry.IsPage ? 0 : 18, 1, 0, 1),
                Child = row,
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                ToolTip = entry.Description,
            };
            if (selected)
            {
                border.SetResourceReference(Border.BackgroundProperty, ThemeKeys.AccentFill18);
            }

            border.MouseLeftButtonUp += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) is not null)
                {
                    return;
                }

                OpenResult(_results[index]);
            };
            _resultsList.Children.Add(border);
        }
    }

    private static T? FindAncestor<T>(DependencyObject node) where T : DependencyObject
    {
        for (var current = node; current is not null; current = current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>"Install" button of a search result: installs the feature, then opens its settings.</summary>
    private void InstallFromResult(string featureId)
    {
        _s.Runtime.SetAvailable([featureId], true);
        var entry = FeatureCatalog.Get(featureId);
        _search.Text = string.Empty;
        if (entry is not null)
        {
            Navigate(entry.Destination, sidebarFeature: entry.Id);
        }
    }

    private void SelectResult(int index)
    {
        _selectedResult = index;
        RenderResults();
    }

    private void OpenResult(SearchEntry entry)
    {
        _search.Text = string.Empty;
        if (entry.FeatureId is not null && !_s.Runtime.IsAvailable(entry.FeatureId))
        {
            Navigate(SettingsPages.Features, targetFeature: entry.FeatureId);
            return;
        }

        Navigate(entry.Destination, sidebarFeature: entry.FeatureId);
    }

    // ------------------------------------------------------------------------------------------------ input

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.S && mods == (ModifierKeys.Control | ModifierKeys.Alt))
        {
            ToggleSidebar();
            e.Handled = true;
        }
        else if (key == Key.Left && mods == ModifierKeys.Alt)
        {
            _router.Back();
            e.Handled = true;
        }
        else if (key == Key.Right && mods == ModifierKeys.Alt)
        {
            _router.Forward();
            e.Handled = true;
        }
        else if (key == Key.Escape && _search.IsKeyboardFocused)
        {
            _search.Text = string.Empty;
        }
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        const int WM_XBUTTONUP = 0x020C;
        const int WM_XBUTTONDOWN = 0x020B;
        if (msg is WM_XBUTTONUP or WM_XBUTTONDOWN)
        {
            var button = (int)((wParam >> 16) & 0xFFFF);
            if (msg == WM_XBUTTONUP)
            {
                if (button == 1)
                {
                    _router.Back();
                }
                else if (button == 2)
                {
                    _router.Forward();
                }
            }

            handled = true;
        }

        return 0;
    }

    private void ScheduleSizeSave()
    {
        if (WindowState != WindowState.Normal)
        {
            return;
        }

        _sizeSaveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _sizeSaveTimer.Tick -= SaveSize;
        _sizeSaveTimer.Tick += SaveSize;
        _sizeSaveTimer.Stop();
        _sizeSaveTimer.Start();
    }

    private void SaveSize(object? sender, EventArgs e)
    {
        _sizeSaveTimer?.Stop();
        _s.App.Set(AppKeys.SettingsWindowWidth, Math.Round(ActualWidth));
        _s.App.Set(AppKeys.SettingsWindowHeight, Math.Round(ActualHeight));
    }
}
