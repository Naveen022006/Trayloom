// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinKnife.Core;

namespace WinKnife.Features.Island;

internal static class PageFactory
{
    public static IslandPage? Create(IslandCore core, IslandWindow? win, string id)
    {
        IslandPage? page = id switch
        {
            "controls" => new ControlsPage(core, win),
            "mixer" => new MixerPage(core, win),
            "music" => new MusicPage(core, win),
            "clipboard" => new ClipboardPage(core, win),
            "captures" => new CapturesPage(core, win),
            "files" => new FilesPage(core, win),
            "system" => new SystemPage(core, win),
            "tools" => new ToolsPage(core, win),
            "calendar" => new CalendarPage(core, win),
            "notifications" => new NotificationsPage(core, win),
            "timer" => new TimerPage(core, win),
            "camera" => new CameraPage(core, win),
            "downloads" => new DownloadsPage(core, win),
            "scratchpad" => new ScratchpadPage(core, win),
            "watch" => new WatchPage(core, win),
            _ => null,
        };
        if (page is not null)
        {
            return page;
        }

        return core.External(id) is { } ext ? new ExternalPage(core, win, ext) : null;
    }
}

/// <summary>No section available: the empty island (spec 7.12).</summary>
internal sealed class EmptyPage : IslandPage
{
    private readonly FrameworkElement _view;

    public EmptyPage(IslandCore core, IslandWindow? window)
        : base(core, window)
    {
        var settings = D.PillButton(Strings.SettingsEllipsis, () => { Core.Ctx.Shell.OpenSettings(Core_Pages.Island); Window.Collapse(); }, true);
        _view = D.EmptyView(G.Controls, Strings.Empty, settings);
    }

    public override string ModuleId => "empty";

    public override FrameworkElement View => _view;

    public override double Height(double width, double budget) => Math.Min(budget, 140);
}

internal static class Core_Pages
{
    public const string Island = WinKnife.Core.SettingsPages.Island;
}

/// <summary>Explore: the gallery of sections (spec 7.11).</summary>
internal sealed class ExplorePage : IslandPage
{
    private const double TileW = 92;
    private const double TileH = 86;
    private const double Gap = 8;

    private readonly Grid _view = new();
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly WrapPanel _wrap = new() { Orientation = Orientation.Horizontal };
    private readonly TextBox _search = PageUi.FlatTextBox(12);
    private readonly StackPanel _leading = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _dots = new() { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, -16, 0) };
    private List<ModuleInfo> _all = [];
    private int _highlight;
    private int _row;
    private int _columns = 4;
    private int _visibleRows = 3;
    private double _contentWidth;
    private readonly List<HoverButton> _tiles = [];
    private Border? _searchBox;

    public ExplorePage(IslandCore core, IslandWindow window)
        : base(core, window)
    {
        _all = core.Modules();
        var cur = _all.FindIndex(m => m.Id == window.CurrentModule);
        _highlight = Math.Max(0, cur);
        _view.Children.Add(_scroll);
        _scroll.Content = _wrap;
        _scroll.PreviewMouseWheel += OnWheel;
        BuildLeading();
    }

    public override string ModuleId => "explore";

    public override FrameworkElement View => _view;

    public override FrameworkElement? Leading => _leading;

    public override bool WantsKeyboard => true;

    private void BuildLeading()
    {
        var back = D.IconButton(G.ChevronLeft, Strings.Back, () => Window.ToggleExplore(), size: 28, glyphSize: 11);
        _leading.Children.Add(back);
        _search.Width = 120;
        _search.TextChanged += (_, _) => Rebuild();
        _search.GotKeyboardFocus += (_, _) => _searchBox!.BorderBrush = D.White(0.34);
        _search.LostKeyboardFocus += (_, _) => _searchBox!.BorderBrush = Brushes.Transparent;
        var holder = new Grid();
        holder.Children.Add(_search);
        var hint = D.T(Strings.FindSection, 12, null, D.White(0.35));
        hint.IsHitTestVisible = false;
        hint.Margin = new Thickness(4, 0, 0, 0);
        hint.VerticalAlignment = VerticalAlignment.Center;
        _search.TextChanged += (_, _) => hint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        holder.Children.Add(hint);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(D.I(G.Search, 11, D.White(0.55)));
        row.Children[0].SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 4, 0));
        row.Children.Add(holder);
        _searchBox = new Border { Height = 28, CornerRadius = new CornerRadius(9), Background = D.White(0.07), BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent, Child = row, Margin = new Thickness(8, 0, 0, 0) };
        _leading.Children.Add(_searchBox);
        Window.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            Window.EnsureKeyboard();
            _search.Focus();
            Keyboard.Focus(_search);
        });
    }

    private IEnumerable<ModuleInfo> Filtered()
    {
        var terms = WatchService.Normalise(_search.Text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var m in _all)
        {
            var hay = WatchService.Normalise(m.Title + " " + m.Id + (m.Id == "music" ? " music" : ""));
            if (terms.All(hay.Contains))
            {
                yield return m;
            }
        }
    }

    private void Rebuild()
    {
        _wrap.Children.Clear();
        _tiles.Clear();
        var items = Filtered().ToList();
        if (items.Count == 0)
        {
            _wrap.Children.Add(new Grid { Width = _contentWidth, Height = 120, Children = { D.EmptyView(G.Search, Strings.NoResults) } });
            return;
        }

        _highlight = Math.Clamp(_highlight, 0, items.Count - 1);
        for (var i = 0; i < items.Count; i++)
        {
            var m = items[i];
            var index = i;
            var tile = new HoverButton(() => { Window.ShowModule(m.Id); })
            {
                Width = TileW,
                Height = TileH,
                CornerRadius = new CornerRadius(16),
                Margin = new Thickness(0, 0, Gap, Gap),
                Fill = D.White(index == _highlight ? 0.12 : 0.045),
                BorderBrush = D.White(index == _highlight ? 0.55 : 0.04),
                BorderThickness = new Thickness(1),
            };
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var tint = m.Id switch { "music" => (Brush)D.Frozen(Color.FromRgb(0xFF, 0x37, 0x5F)), "calendar" => D.Red, "timer" => D.Orange, _ => D.White(0.85) };
            var icon = D.I(m.Glyph, 22, tint);
            icon.FontWeight = FontWeights.Medium;
            icon.Margin = new Thickness(0, 0, 0, 6);
            stack.Children.Add(icon);
            var title = D.T(m.Title, 11, FontWeights.Medium, D.Text, true);
            title.TextAlignment = TextAlignment.Center;
            title.Height = 27;
            title.MaxHeight = 27;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            title.Margin = new Thickness(4, 0, 4, 0);
            stack.Children.Add(title);
            if (m.Key != '\0')
            {
                var cap = D.T("Ctrl+Alt+" + char.ToUpperInvariant(m.Key), 10, FontWeights.Medium, D.White(0.60));
                cap.HorizontalAlignment = HorizontalAlignment.Center;
                stack.Children.Add(cap);
            }

            var grid = new Grid();
            grid.Children.Add(stack);
            if (m.Id == Window.CurrentModule)
            {
                var check = D.I("", 10, D.White(0.7));
                check.HorizontalAlignment = HorizontalAlignment.Right;
                check.VerticalAlignment = VerticalAlignment.Top;
                check.Margin = new Thickness(0, 7, 7, 0);
                grid.Children.Add(check);
            }

            tile.Child = grid;
            D.Tip(tile, m.Key == '\0' ? m.Title : Loc.F("{0}  Ctrl+Alt+{1}", m.Title, char.ToUpperInvariant(m.Key)));
            _wrap.Children.Add(tile);
            _tiles.Add(tile);
        }
    }

    public override double Height(double width, double budget)
    {
        _contentWidth = width;
        _columns = Math.Max(1, (int)Math.Floor((width - 12 + Gap) / (TileW + Gap)));
        var rows = (int)Math.Ceiling(Math.Max(1, _all.Count) / (double)_columns);
        _visibleRows = Math.Max(1, (int)Math.Floor((budget + Gap) / (TileH + Gap)));
        var shown = Math.Min(rows, _visibleRows);
        _wrap.Width = _columns * (TileW + Gap);
        _wrap.HorizontalAlignment = HorizontalAlignment.Left;
        Rebuild();
        _row = Math.Clamp(_highlight / _columns, 0, Math.Max(0, rows - 1));
        return shown * (TileH + Gap) - Gap;
    }

    public override void Shown() => ScrollToHighlight();

    private void ScrollToHighlight()
    {
        var row = _highlight / Math.Max(1, _columns);
        var top = row * (TileH + Gap);
        _scroll.ScrollToVerticalOffset(Math.Max(0, Math.Min(top, (Math.Ceiling(_tiles.Count / (double)_columns) - _visibleRows) * (TileH + Gap))));
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var rows = (int)Math.Ceiling(_tiles.Count / (double)_columns);
        var maxTop = Math.Max(0, rows - _visibleRows);
        var cur = (int)Math.Round(_scroll.VerticalOffset / (TileH + Gap));
        cur = Math.Clamp(cur + (e.Delta > 0 ? -1 : 1), 0, maxTop);
        _scroll.ScrollToVerticalOffset(cur * (TileH + Gap));
        e.Handled = true;
    }

    public override bool HandleKey(KeyEventArgs e)
    {
        var items = Filtered().ToList();
        if (items.Count == 0)
        {
            return false;
        }

        var move = e.Key switch
        {
            Key.Left when _search.Text.Length == 0 => -1,
            Key.Right when _search.Text.Length == 0 => 1,
            Key.Up => -_columns,
            Key.Down => _columns,
            Key.Tab => (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1,
            _ => 0,
        };
        if (move != 0)
        {
            _highlight = Math.Clamp(_highlight + move, 0, items.Count - 1);
            Rebuild();
            ScrollToHighlight();
            return true;
        }

        if (e.Key == Key.Return)
        {
            Window.ShowModule(items[Math.Clamp(_highlight, 0, items.Count - 1)].Id);
            return true;
        }

        return false;
    }
}
