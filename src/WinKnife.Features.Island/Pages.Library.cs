// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinKnife.Core;

namespace WinKnife.Features.Island;

/// <summary>English strings of the Apple Music library browser.</summary>
internal static class LibStrings
{
    public static string Library => Loc.T("Apple Music library");
    public static string Playlists => Loc.T("Playlists");
    public static string Songs => Loc.T("Songs");
    public static string Recent => Loc.T("Recently added");
    public static string SearchLibrary => Loc.T("Search");
    public static string Play => Loc.T("Play");
    public static string PlayNext => Loc.T("Play Next");
    public static string AddToQueue => Loc.T("Add to Queue");
    public static string More => Loc.T("More");
    public static string AddedNext => Loc.T("Playing next");
    public static string AddedQueue => Loc.T("Added to Up next");
    public static string NotStarted => Loc.T("Apple Music didn't start");
    public static string NotFound => Loc.T("Couldn't find it in Apple Music");
    public static string NoResponse => Loc.T("Apple Music didn't respond");
    public static string SongNotStarted => Loc.T("Apple Music didn't start this song");
    public static string PlaylistNotStarted => Loc.T("Apple Music didn't start this playlist");
    public static string Missing => Loc.T("Apple Music library not found");
    public static string MissingHint => Loc.T("Open Apple Music once and let it set up your library, then try again.");
    public static string Unreadable => Loc.T("Couldn't read the Apple Music library");
    public static string UnreadableHint => Loc.T("WinKnife reads it from your Music folder. Try again in a moment.");
    public static string Loading => Loc.T("Loading your library…");
    public static string NoSongs => Loc.T("No songs");
    public static string NoPlaylists => Loc.T("No playlists");
    public static string NoRecent => Loc.T("No recently added songs");
    public static string UpNextHint => Loc.T("Nothing is queued in Apple Music.");
    public static string UpNextLoading => Loc.T("Reading the queue…");

    public static string Count(int n) => n == 1 ? Loc.T("1 song") : Loc.F("{0} songs", n);
}

/// <summary>A panel of fixed-height rows that only builds the rows in view (a library has a thousand of them).</summary>
internal sealed class RowsPanel : Panel
{
    public static readonly DependencyProperty IndexProperty = DependencyProperty.RegisterAttached("Index", typeof(int), typeof(RowsPanel), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    private readonly double _rowHeight;

    public RowsPanel(double rowHeight) => _rowHeight = rowHeight;

    public int Count { get; set; }

    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? 0 : available.Width;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(width, _rowHeight));
        }

        return new Size(width, Count * _rowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in InternalChildren)
        {
            child.Arrange(new Rect(0, (int)child.GetValue(IndexProperty) * _rowHeight, finalSize.Width, _rowHeight));
        }

        return finalSize;
    }
}

/// <summary>Scrolling list with row virtualisation for fixed-height rows.</summary>
internal sealed class VirtualList : Grid
{
    private readonly double _rowHeight;
    private readonly Func<int, FrameworkElement> _make;
    private readonly ScrollViewer _scroll;
    private readonly RowsPanel _panel;
    private readonly Dictionary<int, FrameworkElement> _live = [];

    public VirtualList(double rowHeight, int count, Func<int, FrameworkElement> make)
    {
        _rowHeight = rowHeight;
        _make = make;
        _panel = new RowsPanel(rowHeight) { Count = count };
        _scroll = PageUi.VScroll(_panel);
        _scroll.ScrollChanged += (_, _) => Realize();
        SizeChanged += (_, _) => Realize();
        Children.Add(_scroll);
        Loaded += (_, _) => Realize();
    }

    public event Action<double>? Scrolled;

    public double Offset
    {
        get => _scroll.VerticalOffset;
        set
        {
            _scroll.UpdateLayout();
            _scroll.ScrollToVerticalOffset(value);
            Realize();
        }
    }

    public void ScrollIntoView(int index)
    {
        var top = index * _rowHeight;
        if (top < _scroll.VerticalOffset)
        {
            WinKnife.Core.Ui.SmoothScroll.ScrollTo(_scroll, top);
        }
        else if (top + _rowHeight > _scroll.VerticalOffset + _scroll.ViewportHeight)
        {
            WinKnife.Core.Ui.SmoothScroll.ScrollTo(_scroll, top + _rowHeight - _scroll.ViewportHeight);
        }
    }

    /// <summary>Rebuilds the rows in view (a state shown in them changed).</summary>
    public void Refresh()
    {
        foreach (var row in _live.Values)
        {
            _panel.Children.Remove(row);
        }

        _live.Clear();
        Realize();
    }

    private void Realize()
    {
        Scrolled?.Invoke(_scroll.VerticalOffset);
        var height = _scroll.ViewportHeight > 0 ? _scroll.ViewportHeight : ActualHeight;
        if (height <= 0)
        {
            return;
        }

        var first = Math.Max(0, (int)Math.Floor(_scroll.VerticalOffset / _rowHeight) - 2);
        var last = Math.Min(_panel.Count - 1, (int)Math.Ceiling((_scroll.VerticalOffset + height) / _rowHeight) + 2);
        foreach (var i in _live.Keys.Where(i => i < first || i > last).ToList())
        {
            _panel.Children.Remove(_live[i]);
            _live.Remove(i);
        }

        for (var i = first; i <= last; i++)
        {
            if (_live.ContainsKey(i))
            {
                continue;
            }

            var row = _make(i);
            row.SetValue(RowsPanel.IndexProperty, i);
            _live[i] = row;
            _panel.Children.Add(row);
        }
    }
}

/// <summary>
/// The Apple Music library browser inside the music page: Playlists · Songs · Recently added, a search box, rows that play on click.
/// Playing goes through <see cref="AppleMusicService"/> (the Apple Music window is never shown).
/// </summary>
internal sealed class LibraryPanel : Grid
{
    private const double RowHeight = 44;

    private readonly IslandCore _core;
    private readonly AppleMusicService _svc;
    private readonly Func<IDisposable?> _hold;
    private readonly Action _ensureKeyboard;
    private readonly Action _releaseKeyboard;
    private readonly Func<bool> _keyboardActive;
    private readonly Border _headerHost = new() { Height = 28 };
    private readonly Border _searchBox;
    private readonly TextBox _search = PageUi.FlatTextBox(12);
    private readonly TextBlock _hint;
    private readonly Grid _body = new();
    private readonly Border _contentHost = new();
    private readonly Border _noteHost = new() { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false, Margin = new Thickness(0, 0, 0, 6) };
    private VirtualList? _list;
    private List<object> _items = [];
    private IDisposable? _focusHold;
    private bool _tookKeyboard;
    private bool _built;

    public LibraryPanel(IslandCore core, Func<IDisposable?> hold, Action ensureKeyboard, Action releaseKeyboard, Func<bool> keyboardActive)
    {
        _core = core;
        _svc = core.AppleMusic;
        _hold = hold;
        _ensureKeyboard = ensureKeyboard;
        _releaseKeyboard = releaseKeyboard;
        _keyboardActive = keyboardActive;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Children.Add(_headerHost);

        // search field
        _search.Text = _svc.Ui.Search;
        _hint = D.T(LibStrings.SearchLibrary, 12, null, D.White(0.35));
        _hint.IsHitTestVisible = false;
        _hint.VerticalAlignment = VerticalAlignment.Center;
        _hint.Margin = new Thickness(1, 0, 0, 0);
        var holder = new Grid { VerticalAlignment = VerticalAlignment.Center };
        holder.Children.Add(_hint);
        holder.Children.Add(_search);
        var clear = D.IconButton(G.Close, Strings.Clear, ClearSearch, size: 20, glyphSize: 8);
        clear.CornerRadius = new CornerRadius(10);
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var glass = D.I(G.Search, 11, D.White(0.55));
        glass.Margin = new Thickness(0, 0, 7, 0);
        row.Children.Add(glass);
        Grid.SetColumn(holder, 1);
        row.Children.Add(holder);
        Grid.SetColumn(clear, 2);
        row.Children.Add(clear);
        _searchBox = new Border { Height = 28, CornerRadius = new CornerRadius(9), Background = D.White(0.07), Padding = new Thickness(9, 0, 4, 0), Margin = new Thickness(0, 6, 0, 6), Child = row };
        Grid.SetRow(_searchBox, 1);
        Children.Add(_searchBox);
        clear.Visibility = _search.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _hint.Visibility = _search.Text.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
        _search.TextChanged += (_, _) =>
        {
            _svc.Ui.Search = _search.Text;
            clear.Visibility = _search.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _hint.Visibility = _search.Text.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
            RebuildBody(resetScroll: true);
        };
        _searchBox.PreviewMouseLeftButtonDown += (_, _) => FocusSearch();
        _search.GotKeyboardFocus += (_, _) => _focusHold ??= _hold();
        _search.LostKeyboardFocus += (_, _) => LeaveSearch();
        _search.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Return)
            {
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        };
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (_search.IsKeyboardFocused && !IsInside(e.OriginalSource as DependencyObject, _searchBox))
            {
                Keyboard.ClearFocus();
            }
        };

        Grid.SetRow(_body, 2);
        _body.Children.Add(_contentHost);
        Children.Add(_body);
        _body.Children.Add(_noteHost);

        Loaded += (_, _) =>
        {
            _svc.Changed += OnChanged;
            _svc.EnsureLoaded();
            if (!_built)
            {
                Rebuild();
            }
        };
        Unloaded += (_, _) =>
        {
            _svc.Changed -= OnChanged;
            LeaveSearch();
        };
    }

    private static bool IsInside(DependencyObject? node, DependencyObject ancestor)
    {
        while (node is not null)
        {
            if (ReferenceEquals(node, ancestor))
            {
                return true;
            }

            node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }

        return false;
    }

    // ---------------------------------------------------------------- keyboard

    private void FocusSearch()
    {
        if (_search.IsKeyboardFocused)
        {
            return;
        }

        var had = _keyboardActive();
        _ensureKeyboard();
        _tookKeyboard = !had;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            _search.Focus();
            Keyboard.Focus(_search);
        });
    }

    private void LeaveSearch()
    {
        _focusHold?.Dispose();
        _focusHold = null;
        if (_tookKeyboard)
        {
            _tookKeyboard = false;
            _releaseKeyboard();
        }
    }

    private void ClearSearch()
    {
        _search.Text = "";
        Keyboard.ClearFocus();
    }

    /// <summary>Esc: leave the search box, then the playlist; false when nothing was open.</summary>
    public bool StepBack()
    {
        if (_search.IsKeyboardFocused)
        {
            Keyboard.ClearFocus();
            return true;
        }

        if (_search.Text.Length > 0)
        {
            _search.Text = "";
            return true;
        }

        if (_svc.Ui.PlaylistPid is not null)
        {
            OpenPlaylist(null);
            return true;
        }

        return false;
    }

    // ---------------------------------------------------------------- state

    private void OnChanged() => Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
    {
        if (!IsLoaded)
        {
            return;
        }

        if (!Equals(_dataStamp, DataStamp()))
        {
            Rebuild();
        }
        else
        {
            BuildHeader();
            BuildNote();
            _list?.Refresh();
        }
    });

    private object? _dataStamp;

    private object DataStamp() => (_svc.State, _svc.Db);

    private AmPlaylist? Playlist => _svc.Ui.PlaylistPid is { } pid ? _svc.Db?.Playlists.FirstOrDefault(p => p.Pid == pid) : null;

    private void OpenPlaylist(AmPlaylist? playlist)
    {
        _svc.Ui.PlaylistPid = playlist?.Pid;
        if (_search.Text.Length > 0)
        {
            _search.Text = "";
        }

        Rebuild();
    }

    private void SetTab(int tab)
    {
        if (_svc.Ui.Tab == tab && _svc.Ui.PlaylistPid is null)
        {
            return;
        }

        _svc.Ui.Tab = tab;
        _svc.Ui.PlaylistPid = null;
        Rebuild();
    }

    private string ScrollKey => $"{_svc.Ui.Tab}:{_svc.Ui.PlaylistPid}:{WatchService.Normalise(_svc.Ui.Search)}";

    private void Rebuild()
    {
        _built = true;
        _dataStamp = DataStamp();
        BuildHeader();
        RebuildBody(resetScroll: false);
    }

    private void BuildHeader()
    {
        var ready = _svc.State == LibraryState.Ready;
        _searchBox.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        _headerHost.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        if (!ready)
        {
            return;
        }

        _headerHost.Child = Playlist is { } pl ? PlaylistHeader(pl) : Tabs();
    }

    private FrameworkElement Tabs()
    {
        var plate = new Border { CornerRadius = new CornerRadius(10), Background = D.White(0.065), Padding = new Thickness(2), Height = 28 };
        var grid = new Grid();
        string[] names = [LibStrings.Playlists, LibStrings.Songs, LibStrings.Recent];
        for (var i = 0; i < 3; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var tab = i;
            var selected = _svc.Ui.Tab == i;
            var b = new HoverButton(() => SetTab(tab)) { CornerRadius = new CornerRadius(8), Lifts = false, Selected = selected };
            var label = D.T(names[i], 12, selected ? FontWeights.SemiBold : FontWeights.Medium, selected ? D.Text : D.White(0.6), false, TextTrimming.CharacterEllipsis);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(4, 0, 4, 0);
            b.Child = label;
            Grid.SetColumn(b, i);
            grid.Children.Add(b);
        }

        plate.Child = grid;
        return plate;
    }

    private FrameworkElement PlaylistHeader(AmPlaylist pl)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var back = D.IconButton(G.ChevronLeft, Strings.Back, () => OpenPlaylist(null), size: 28, glyphSize: 12);
        grid.Children.Add(back);
        var title = D.T(pl.Name, 13, FontWeights.SemiBold, D.Text, false, TextTrimming.CharacterEllipsis);
        title.VerticalAlignment = VerticalAlignment.Center;
        title.Margin = new Thickness(4, 0, 8, 0);
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);
        var busy = _svc.BusyPid == pl.Pid;
        var play = ActionPill(G.Play, LibStrings.Play, () => _svc.PlayPlaylist(pl, false), busy && !_svc.BusyShuffle);
        Grid.SetColumn(play, 2);
        grid.Children.Add(play);
        var shuffle = ActionPill(G.Shuffle, Strings.Shuffle, () => _svc.PlayPlaylist(pl, true), busy && _svc.BusyShuffle);
        shuffle.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(shuffle, 3);
        grid.Children.Add(shuffle);
        return grid;
    }

    private static HoverButton ActionPill(string glyph, string text, Action click, bool busy)
    {
        var b = new HoverButton(click) { Height = 26, CornerRadius = new CornerRadius(13), Padding = new Thickness(10, 0, 11, 0), Fill = D.White(0.14), Lifts = false, VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        FrameworkElement icon = busy ? PageUi.Spinner(11) : D.I(glyph, 10, D.Text);
        icon.Margin = new Thickness(0, 0, 6, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(icon);
        row.Children.Add(D.T(text, 12, FontWeights.SemiBold, D.Text));
        b.Child = row;
        return b;
    }

    // ---------------------------------------------------------------- body

    private void RebuildBody(bool resetScroll)
    {
        _list = null;
        FrameworkElement content;
        switch (_svc.State)
        {
            case LibraryState.Missing:
                content = D.EmptyView(G.Music, LibStrings.Missing + "\n" + LibStrings.MissingHint, D.PillButton(Strings.Refresh, () => _svc.EnsureLoaded(true), true));
                break;
            case LibraryState.Unreadable:
                content = D.EmptyView(G.Warning, LibStrings.Unreadable + "\n" + LibStrings.UnreadableHint, D.PillButton(Strings.LyricsRetry, () => _svc.EnsureLoaded(true), true));
                break;
            case LibraryState.Ready:
                content = BuildList(resetScroll);
                break;
            default:
                var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var sp = PageUi.Spinner(16);
                sp.HorizontalAlignment = HorizontalAlignment.Center;
                stack.Children.Add(sp);
                var t = D.T(LibStrings.Loading, 12, null, D.Secondary);
                t.Margin = new Thickness(0, 8, 0, 0);
                stack.Children.Add(t);
                content = stack;
                break;
        }

        _contentHost.Child = content;
        BuildNote();
    }

    private FrameworkElement BuildList(bool resetScroll)
    {
        var db = _svc.Db!;
        var pl = Playlist;
        var terms = WatchService.Normalise(_svc.Ui.Search).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool Match(string key) => terms.All(key.Contains);
        if (pl is null && _svc.Ui.Tab == 0)
        {
            _items = [.. db.Playlists.Where(p => Match(p.SearchKey))];
        }
        else
        {
            IEnumerable<AmTrack> source = pl is not null ? pl.Tracks : (_svc.Ui.Tab == 2 ? db.Recent : db.Songs);
            _items = [.. source.Where(t => Match(t.SearchKey))];
        }

        if (_items.Count == 0)
        {
            var message = terms.Length > 0 ? Strings.NoResults : (pl is null && _svc.Ui.Tab == 0 ? LibStrings.NoPlaylists : (_svc.Ui.Tab == 2 && pl is null ? LibStrings.NoRecent : LibStrings.NoSongs));
            var empty = D.EmptyView(G.Music, message);
            empty.Margin = new Thickness(0, 0, 0, 24);
            return empty;
        }

        var key = ScrollKey;
        var list = new VirtualList(RowHeight, _items.Count, Row);
        _list = list;
        list.Scrolled += offset => _svc.Ui.Scroll[key] = offset;
        if (!resetScroll && _svc.Ui.Scroll.TryGetValue(key, out var saved) && saved > 0)
        {
            list.Loaded += (_, _) => list.Offset = saved;
        }

        return list;
    }

    private FrameworkElement Row(int index)
    {
        var item = _items[index];
        return item is AmTrack t ? TrackRow(t) : PlaylistRow((AmPlaylist)item);
    }

    private FrameworkElement TrackRow(AmTrack track)
    {
        var current = _svc.IsCurrent(track);
        var busy = _svc.BusyPid == track.Pid;
        var context = Playlist;
        var page = _svc.Ui.Tab == 2 ? "recent" : "songs";
        var row = new HoverButton(() => _svc.PlaySong(track, context, page)) { Margin = new Thickness(0, 1, 0, 1), CornerRadius = new CornerRadius(10), Lifts = false, Fill = current ? D.White(0.1) : null };
        var grid = new Grid { Margin = new Thickness(6, 0, 4, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        FrameworkElement lead;
        if (busy)
        {
            lead = PageUi.Spinner(13);
        }
        else if (current)
        {
            lead = new EqBars(3, 2.4, 12, D.Text) { Playing = _core.Playback.Current.IsPlaying };
        }
        else
        {
            lead = D.I(G.Music, 11, D.White(0.3));
        }

        lead.HorizontalAlignment = HorizontalAlignment.Left;
        lead.VerticalAlignment = VerticalAlignment.Center;
        lead.Margin = new Thickness(3, 0, 0, 0);
        grid.Children.Add(lead);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        texts.Children.Add(D.T(string.IsNullOrWhiteSpace(track.Title) ? "—" : track.Title, 13, FontWeights.SemiBold, D.Text, false, TextTrimming.CharacterEllipsis));
        texts.Children.Add(D.T(track.Subtitle, 11, null, D.Secondary, false, TextTrimming.CharacterEllipsis));
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        var time = D.Digits(track.Duration, 11, null, D.Tertiary);
        time.VerticalAlignment = VerticalAlignment.Center;
        time.Margin = new Thickness(0, 0, 4, 0);
        Grid.SetColumn(time, 2);
        grid.Children.Add(time);
        HoverButton? more = null;
        more = new HoverButton(() => IslandMenu.Show(more!, TrackMenu(track, context, page), _hold)) { Width = 26, Height = 26, CornerRadius = new CornerRadius(8), Lifts = false, VerticalAlignment = VerticalAlignment.Center };
        var dots = D.I(G.More, 12, D.White(0.6));
        more.Child = dots;
        more.Icon = dots;
        D.Tip(more, LibStrings.More);
        Grid.SetColumn(more, 3);
        grid.Children.Add(more);
        row.Child = grid;
        return row;
    }

    private IEnumerable<IslandMenu.Item> TrackMenu(AmTrack track, AmPlaylist? context, string page)
    {
        yield return new IslandMenu.Item(LibStrings.Play, () => _svc.PlaySong(track, context, page), Glyph: G.Play);
        yield return new IslandMenu.Item(LibStrings.PlayNext, () => _svc.PlaySong(track, context, page, AmAction.PlayNext), Glyph: G.Next);
        yield return new IslandMenu.Item(LibStrings.AddToQueue, () => _svc.PlaySong(track, context, page, AmAction.AddToQueue), Glyph: G.List);
    }

    private FrameworkElement PlaylistRow(AmPlaylist pl)
    {
        var row = new HoverButton(() => OpenPlaylist(pl)) { Margin = new Thickness(0, 1, 0, 1), CornerRadius = new CornerRadius(10), Lifts = false };
        var grid = new Grid { Margin = new Thickness(6, 0, 8, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tile = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(8), Background = D.White(0.08), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Child = D.I(G.List, 12, D.White(0.7)) };
        grid.Children.Add(tile);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        texts.Children.Add(D.T(pl.Name, 13, FontWeights.SemiBold, D.Text, false, TextTrimming.CharacterEllipsis));
        texts.Children.Add(D.T(LibStrings.Count(pl.Tracks.Count), 11, null, D.Secondary));
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        var chevron = D.I(G.ChevronRight, 10, D.White(0.4));
        chevron.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(chevron, 2);
        grid.Children.Add(chevron);
        row.Child = grid;
        return row;
    }

    private void BuildNote()
    {
        if (_svc.Note is not { } note)
        {
            _noteHost.Child = null;
            _noteHost.Visibility = Visibility.Collapsed;
            return;
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var glyph = D.I(note.Error ? G.Warning : G.Check, 11, note.Error ? D.Orange : D.Text);
        glyph.Margin = new Thickness(0, 0, 7, 0);
        row.Children.Add(glyph);
        row.Children.Add(D.T(note.Text, 12, FontWeights.Medium, note.Error ? D.Orange : D.Text, false, TextTrimming.CharacterEllipsis));
        _noteHost.Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x29));
        _noteHost.BorderBrush = D.White(0.14);
        _noteHost.BorderThickness = new Thickness(1);
        _noteHost.CornerRadius = new CornerRadius(10);
        _noteHost.Padding = new Thickness(11, 6, 12, 6);
        _noteHost.Child = row;
        _noteHost.Visibility = Visibility.Visible;
    }
}

/// <summary>"Up next" for Apple Music, read from its play queue pane (read-only).</summary>
internal sealed class AppleQueueView : Grid
{
    private readonly AppleMusicService _svc;

    public AppleQueueView(IslandCore core)
    {
        _svc = core.AppleMusic;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Margin = new Thickness(0, 6, 0, 0);
        var header = new Grid { Height = 24 };
        header.Children.Add(D.T(Strings.Queue, 13, FontWeights.SemiBold, D.Text));
        var refresh = D.IconButton(G.Refresh, Strings.Refresh, () => _svc.RefreshQueue(), size: 24, glyphSize: 11);
        refresh.HorizontalAlignment = HorizontalAlignment.Right;
        header.Children.Add(refresh);
        Children.Add(header);
        Loaded += (_, _) =>
        {
            _svc.Changed += OnChanged;
            _svc.RefreshQueue();
            Rebuild();
        };
        Unloaded += (_, _) => _svc.Changed -= OnChanged;
    }

    private void OnChanged() => Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
    {
        if (IsLoaded)
        {
            Rebuild();
        }
    });

    private void Rebuild()
    {
        for (var i = Children.Count - 1; i >= 1; i--)
        {
            Children.RemoveAt(i);
        }

        FrameworkElement body;
        if (_svc.Queue is not { } queue)
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var sp = PageUi.Spinner(16);
            sp.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(sp);
            var t = D.T(LibStrings.UpNextLoading, 12, null, D.Secondary);
            t.Margin = new Thickness(0, 8, 0, 0);
            stack.Children.Add(t);
            body = stack;
        }
        else if (queue.Count == 0)
        {
            body = D.EmptyView(G.List, Strings.QueueEmpty);
        }
        else
        {
            var list = new StackPanel();
            foreach (var q in queue)
            {
                var row = new Border { Height = 40, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 1, 0, 1), Padding = new Thickness(8, 0, 8, 0) };
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                texts.Children.Add(D.T(q.Title, 13, FontWeights.SemiBold, D.Text, false, TextTrimming.CharacterEllipsis));
                texts.Children.Add(D.T(q.Subtitle, 11, null, D.Secondary, false, TextTrimming.CharacterEllipsis));
                grid.Children.Add(texts);
                var time = D.Digits(q.Duration, 11, null, D.Tertiary);
                time.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(time, 1);
                grid.Children.Add(time);
                row.Child = grid;
                list.Children.Add(row);
            }

            body = PageUi.VScroll(list);
        }

        Grid.SetRow(body, 1);
        Children.Add(body);
    }
}
