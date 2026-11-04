// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Trayloom.Core;

namespace Trayloom.Features.Island;

/// <summary>Strings only the streaming panel uses (the shared ones are in <see cref="StreamStrings"/>).</summary>
internal static class StreamUiStrings
{
    public static string Open => Loc.T("Open");
    public static string Copied => Loc.T("Copied");
    public static string Album => Loc.T("Album");
    public static string Artist => Loc.T("Artist");
    public static string SomethingWentWrong => Loc.T("Something went wrong");
}

// ==================================================================== navigation kept across page rebuilds

/// <summary>
/// Where one streaming panel was left: the music page is rebuilt on every track change, so the box text, the tab, the scope chip, the open
/// lists, the scroll offsets and the highlight live here, not in the panel.
/// </summary>
internal sealed class StreamNavState
{
    /// <summary>The text in the search box.</summary>
    public string Text { get; set; } = "";

    /// <summary>The text that was searched (Spotify: the debounced text of two or more characters; YouTube: what Enter submitted).</summary>
    public string Query { get; set; } = "";

    /// <summary>The scope chip of the search.</summary>
    public StreamScope Scope { get; set; }

    /// <summary>The list tab shown while not searching.</summary>
    public StreamView Tab { get; set; } = StreamView.Playlists;

    /// <summary>The open lists (album, playlist, artist), innermost last.</summary>
    public List<StreamItem> Stack { get; } = [];

    /// <summary>The box text to restore when the first list opened from a search is left.</summary>
    public string ReturnText { get; set; } = "";

    /// <summary>The highlighted row.</summary>
    public string HighlightId { get; set; } = "";

    /// <summary>One saved scroll offset per place.</summary>
    public Dictionary<string, double> Scroll { get; } = [];

    /// <summary>Per list, the pages the user asked for with "Show more" (each page is identified by its start).</summary>
    public Dictionary<string, HashSet<int>> Expanded { get; } = [];
}

/// <summary>The navigation state of each service, kept for the life of the process.</summary>
internal static class StreamNav
{
    private static readonly StreamNavState s_spotify = new();
    private static readonly StreamNavState s_youTube = new() { Scope = StreamScope.Songs };

    /// <summary>The state of a service.</summary>
    public static StreamNavState For(StreamKind kind) => kind == StreamKind.Spotify ? s_spotify : s_youTube;
}

// ==================================================================== the panel

/// <summary>
/// The Spotify / YouTube Music browser inside the music page: the same rows as the Apple Music <see cref="LibraryPanel"/> (tabs or link pills,
/// search box with scope chips, a virtual list) over an <see cref="IMusicService"/>. A thin view: the service owns the results, this asks
/// it with <see cref="IMusicService.Get"/> and draws again on <see cref="IMusicService.Changed"/>.
/// </summary>
internal sealed class StreamPanel : Grid, IMusicBrowserPanel
{
    private static bool s_loggedKeyboard;

    private readonly IslandCore _core;
    private readonly IMusicService _svc;
    private readonly IslandWindow? _window;
    private readonly StreamNavState _nav;
    private readonly bool _wide;
    private readonly double _tileWidth;
    private readonly Border _headerHost = new() { MinHeight = 28 };
    private readonly Border _searchBox;
    private readonly TextBox _search = PageUi.FlatTextBox(12);
    private readonly TextBlock _hint;
    private readonly HoverButton _clear;
    private readonly Grid _body = new();
    private readonly Border _contentHost = new();
    private readonly Border _noteHost = new() { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false, Margin = new Thickness(0, 0, 0, 6) };
    private readonly DispatcherTimer _debounce = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(450) };
    private StreamListModel? _model;
    private VirtualList? _list;
    private List<object?> _stamp = [];
    private HoverButton? _externalSearch;
    private string? _moreMessage;
    private bool _moreLoading;
    private IDisposable? _focusHold;
    private string? _localNote;
    private DispatcherTimer? _noteTimer;
    private bool _tookKeyboard;
    private bool _wantFocus;
    private bool _parked;
    private bool _quiet;
    private bool _built;
    private bool _pendingFocus;
    private int _resumeCaret = -1;

    /// <summary>Creates the panel for one service.</summary>
    public StreamPanel(IslandCore core, IslandWindow? window, IMusicService service)
    {
        _core = core;
        _svc = service;
        _window = window;
        _nav = StreamNav.For(service.Kind);
        _wide = service.Kind == StreamKind.YouTube;
        _tileWidth = _wide ? 56 : 32;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Children.Add(_headerHost);

        // search field
        _search.Text = _nav.Text;
        AutomationProperties.SetAutomationId(_search, "Island.Stream.Search");
        AutomationProperties.SetName(_search, SearchName);
        _hint = D.T(SearchName, 12, null, D.White(0.35), false, TextTrimming.CharacterEllipsis);
        _hint.IsHitTestVisible = false;
        _hint.VerticalAlignment = VerticalAlignment.Center;
        _hint.Margin = new Thickness(1, 0, 0, 0);
        var holder = new Grid { VerticalAlignment = VerticalAlignment.Center };
        holder.Children.Add(_hint);
        holder.Children.Add(_search);
        _clear = D.IconButton(G.Close, Strings.Clear, ClearSearch, size: 20, glyphSize: 8);
        _clear.CornerRadius = new CornerRadius(10);
        AutomationProperties.SetAutomationId(_clear, "Island.Stream.SearchClear");
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var glass = D.I(G.Search, 11, D.White(0.55));
        glass.Margin = new Thickness(0, 0, 7, 0);
        row.Children.Add(glass);
        Grid.SetColumn(holder, 1);
        row.Children.Add(holder);
        Grid.SetColumn(_clear, 2);
        row.Children.Add(_clear);
        _searchBox = new Border { Height = 28, CornerRadius = new CornerRadius(9), Background = D.White(0.07), Padding = new Thickness(9, 0, 4, 0), Margin = new Thickness(0, 6, 0, 6), Child = row };
        Grid.SetRow(_searchBox, 1);
        Children.Add(_searchBox);
        SyncBox();
        _debounce.Tick += (_, _) => ApplyQuery();
        _search.TextChanged += (_, _) => OnTextChanged();
        _searchBox.PreviewMouseLeftButtonDown += (_, _) => FocusSearch();
        _search.GotKeyboardFocus += (_, _) =>
        {
            if (_parked || !IsLoaded)
            {
                return;
            }

            _wantFocus = true;
            _focusHold ??= _window?.HoldOpen();
        };
        _search.LostKeyboardFocus += OnLostKeyboardFocus;
        _search.PreviewKeyDown += OnSearchKey;

        Grid.SetRow(_body, 2);
        _body.Children.Add(_contentHost);
        Children.Add(_body);
        _body.Children.Add(_noteHost);

        Loaded += (_, _) =>
        {
            _parked = false;
            _svc.Changed += OnChanged;
            if (_window is not null)
            {
                _window.KeyboardLost += OnKeyboardLost;
                _window.KeyboardRegained += OnKeyboardRegained;
            }

            LibraryNav.Focus.LeaveRequested = false;
            if (!_built)
            {
                Rebuild();
            }

            if (_search.Text != _nav.Text)
            {
                // keys typed into the box of the page this one replaces
                _search.Text = _nav.Text;
            }

            if (LibraryNav.Focus.TryTake(LibraryNav.Now, _window?.KeyboardActive ?? true, out var caret, out var took, out var hold))
            {
                // the page was rebuilt under a focused box: this one carries on with its keyboard
                _tookKeyboard |= took;
                _focusHold ??= hold;
                if (!ReferenceEquals(_focusHold, hold))
                {
                    hold?.Dispose();
                }

                _wantFocus = true;
                _resumeCaret = caret;
                ResumeFocus();
            }
            else if (LibraryNav.Focus.ConsumeFocusRequest(LibraryNav.Now))
            {
                _pendingFocus = true;
                ApplyPendingFocus();
            }
        };
        Unloaded += (_, _) =>
        {
            _svc.Changed -= OnChanged;
            if (_window is not null)
            {
                _window.KeyboardLost -= OnKeyboardLost;
                _window.KeyboardRegained -= OnKeyboardRegained;
            }

            _debounce.Stop();
            _noteTimer?.Stop();
            if (_parked)
            {
                return;
            }

            if (LibraryNav.Focus.LeaveRequested)
            {
                LeaveSearch(release: true);
            }
            else if (_wantFocus || _focusHold is not null)
            {
                Park();
            }
        };
    }

    /// <summary>The box has text (Space then belongs to it, not to play / pause).</summary>
    public bool HasText => _search.Text.Length > 0;

    private bool Spotify => _svc.Kind == StreamKind.Spotify;

    private string SearchName => Spotify ? StreamStrings.SearchSpotify : StreamStrings.SearchYouTubeMusic;

    private StreamItem? Container => _nav.Stack.Count > 0 ? _nav.Stack[^1] : null;

    private bool Searching => _nav.Query.Length > 0 && _svc.CanBrowse;

    private void SyncBox()
    {
        _clear.Visibility = _search.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _hint.Visibility = _search.Text.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
        if (_externalSearch is not null)
        {
            _externalSearch.IsEnabled = _search.Text.Trim().Length > 0;
        }
    }

    private void SetText(string text)
    {
        _quiet = true;
        try
        {
            _search.Text = text;
        }
        finally
        {
            _quiet = false;
        }
    }

    // ---------------------------------------------------------------- keyboard (the same hand-over as the Apple Music library)

    /// <summary>
    /// An explicit gesture (a click on the box, the magnifier, Ctrl+F, "/", a typed letter): takes the island's keyboard (it is a window that
    /// does not activate) and puts the caret in the box. Nothing else ever takes it.
    /// </summary>
    public void FocusSearch(bool selectAll = false)
    {
        if (_window is null || _parked)
        {
            return;
        }

        if (_search.IsKeyboardFocused && _window.IsForeground)
        {
            if (selectAll)
            {
                _search.SelectAll();
            }

            return;
        }

        var had = _window.KeyboardActive;
        _window.EnsureKeyboard(dropsWhenAppLeft: true);
        _tookKeyboard |= !had;
        _wantFocus = true;
        if (!_window.IsForeground)
        {
            _window.ReclaimKeyboard();
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            _search.Focus();
            Keyboard.Focus(_search);
            if (selectAll)
            {
                _search.SelectAll();
            }
        });
        VerifyKeyboardSoon();
    }

    /// <summary>Types into the box (letters typed while the browser is open, or Space with text in it).</summary>
    public bool TypeInto(string text)
    {
        if (_parked || _window is null || _searchBox.Visibility != Visibility.Visible)
        {
            return false;
        }

        _search.Text += text;
        _search.CaretIndex = _search.Text.Length;
        FocusSearch();
        return true;
    }

    private void ApplyPendingFocus()
    {
        if (!_pendingFocus)
        {
            return;
        }

        _pendingFocus = false;
        FocusSearch();
    }

    private void ResumeFocus()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!IsLoaded || _parked || _window is { KeyboardActive: false })
            {
                return;
            }

            _search.Focus();
            Keyboard.Focus(_search);
            if (_resumeCaret >= 0)
            {
                _search.CaretIndex = Math.Min(_resumeCaret, _search.Text.Length);
                _resumeCaret = -1;
            }
        });
    }

    /// <summary>
    /// The page is about to be rebuilt (or this box is going away while it has the keyboard): what it holds is parked for the box that
    /// comes next. When none does within half a second the keyboard is given back.
    /// </summary>
    public void Park()
    {
        if (_parked || (!_wantFocus && _focusHold is null))
        {
            return;
        }

        var serial = LibraryNav.Focus.Park(_search.CaretIndex, _tookKeyboard, _focusHold, LibraryNav.Now);
        _parked = true;
        _focusHold = null;
        _tookKeyboard = false;
        _wantFocus = false;
        var window = _window;
        var timer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TimeSpan.FromMilliseconds(LibraryFocusCarry.ReleaseAfterMs) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (LibraryNav.Focus.TryExpire(serial, out var took, out var hold))
            {
                hold?.Dispose();
                if (took)
                {
                    window?.ReleaseKeyboard();
                }
            }
        };
        timer.Start();
    }

    /// <summary>Gives the keyboard back now (the browser is closed or switched, or the page is left).</summary>
    public void ReleaseNow() => LeaveSearch(release: true);

    private void LeaveSearch(bool release)
    {
        _wantFocus = false;
        _focusHold?.Dispose();
        _focusHold = null;
        if (_tookKeyboard)
        {
            _tookKeyboard = false;
            if (release)
            {
                _window?.ReleaseKeyboard();
            }
        }
    }

    private void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_parked || !IsLoaded)
        {
            return;
        }

        if (e.NewFocus is null && _window is { IsForeground: false })
        {
            // the island lost the foreground (another app): the window decides, it tells us KeyboardLost / Regained
            return;
        }

        if (e.NewFocus is FrameworkElement now && (now is MenuItem || D.FindAncestor<ContextMenu>(now) is not null))
        {
            // the "…" menu of a row: the box gets the focus back when it closes
            return;
        }

        LeaveSearch(release: true);
    }

    private void OnKeyboardLost()
    {
        // another app has the foreground and the window already took the keyboard mode off: nothing to release, the hold goes
        if (_parked)
        {
            return;
        }

        _tookKeyboard = false;
        LeaveSearch(release: false);
    }

    private void OnKeyboardRegained()
    {
        if (_parked || !_wantFocus || !IsLoaded || _window is { KeyboardActive: false })
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_wantFocus && !_parked && IsLoaded && _window is not { KeyboardActive: false })
            {
                Keyboard.Focus(_search);
            }
        });
    }

    /// <summary>A short check after the gesture: the island must now be the foreground window, or typing would go to another app.</summary>
    private void VerifyKeyboardSoon()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!IsLoaded || _window is null || (_search.IsKeyboardFocused && _window.IsForeground))
            {
                return;
            }

            if (!s_loggedKeyboard)
            {
                s_loggedKeyboard = true;
                _core.Ctx.Log.Info("[island] Music search could not take the keyboard (the island is not the foreground window)");
            }

            ShowLocalNote(LibStrings.ClickToType, error: true);
        };
        timer.Start();
    }

    private void ShowLocalNote(string text, bool error = false)
    {
        _localNote = text;
        _localNoteError = error;
        BuildNote();
        _noteTimer?.Stop();
        _noteTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TimeSpan.FromSeconds(3) };
        _noteTimer.Tick += (_, _) =>
        {
            _noteTimer?.Stop();
            _localNote = null;
            BuildNote();
        };
        _noteTimer.Start();
    }

    private bool _localNoteError;

    private void ClearSearch() => _search.Text = "";

    private void OnSearchKey(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        if (alt)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                MoveHighlight(1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveHighlight(-1);
                e.Handled = true;
                break;
            case Key.PageDown:
                MoveHighlight(StreamListModel.PageStep);
                e.Handled = true;
                break;
            case Key.PageUp:
                MoveHighlight(-StreamListModel.PageStep);
                e.Handled = true;
                break;
            case Key.Return:
                OnEnter(shift, ctrl);
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Enter: inside a list or on Spotify it acts on the highlighted row; on YouTube the first Enter on new text searches (in the island,
    /// with no key needed), and an Enter on the results acts on the highlighted row.
    /// </summary>
    private void OnEnter(bool shift, bool ctrl)
    {
        Flush();
        var text = _search.Text.Trim();
        if (Container is null)
        {
            if (!_svc.CanBrowse)
            {
                if (text.Length > 0)
                {
                    _svc.OpenSearchExternally(text);
                }

                return;
            }

            if (!_svc.SearchAsYouType && text.Length > 0 && text != _nav.Query)
            {
                Submit(text);
                return;
            }
        }

        if (_model is not null)
        {
            Execute(_model.Activate(shift, ctrl));
        }
    }

    private void Flush()
    {
        if (_debounce.IsEnabled)
        {
            ApplyQuery();
        }
    }

    private void MoveHighlight(int delta)
    {
        Flush();
        if (_model is null || !_model.Move(delta))
        {
            return;
        }

        _nav.HighlightId = _model.HighlightId;
        _list?.Refresh();
        _list?.ScrollIntoView(_model.Highlight, _model.LeadRows(_model.Highlight));
        AutomationProperties.SetItemStatus(_search, StatusText());
    }

    private string StatusText() => _model is { } m ? LibStrings.ResultCount(m.SelectableCount) : "";

    /// <summary>Esc: clear the text (the box keeps the keyboard), then leave the box, then the open list; false when nothing was open.</summary>
    public bool StepBack()
    {
        switch (LibrarySearchModel.Escape(_search.Text.Length > 0, _wantFocus || _search.IsKeyboardFocused, Container is not null))
        {
            case LibStep.Clear:
                _search.Text = "";
                return true;
            case LibStep.Blur:
                LeaveSearch(release: true);
                Keyboard.ClearFocus();
                return true;
            case LibStep.Back:
                CloseContainer();
                return true;
            default:
                return false;
        }
    }

    // ---------------------------------------------------------------- text and navigation state

    private void OnTextChanged()
    {
        _nav.Text = _search.Text;
        SyncBox();
        if (_quiet)
        {
            return;
        }

        var text = _search.Text.Trim();
        if (text.Length == 0)
        {
            ApplyQuery();
        }
        else if (Container is not null)
        {
            // a list is open: the box filters it
            _debounce.Interval = TimeSpan.FromMilliseconds(120);
            _debounce.Stop();
            _debounce.Start();
        }
        else if (_svc.SearchAsYouType && _svc.CanBrowse)
        {
            _debounce.Interval = TimeSpan.FromMilliseconds(450);
            _debounce.Stop();
            _debounce.Start();
        }
        else
        {
            // YouTube searches on Enter: the header says so while the box differs from the results
            BuildHeader();
        }
    }

    /// <summary>The (debounced) text changed: the results are asked for again from the top.</summary>
    private void ApplyQuery()
    {
        _debounce.Stop();
        if (!IsLoaded)
        {
            return;
        }

        var text = _search.Text.Trim();
        if (Container is not null)
        {
            RebuildBody(resetScroll: true);
            return;
        }

        if (text.Length == 0)
        {
            if (_nav.Query.Length == 0)
            {
                BuildHeader();
                return;
            }

            _nav.Query = "";
            _nav.Scope = DefaultScope;
        }
        else if (_svc.SearchAsYouType)
        {
            // Spotify needs two characters; a shorter text keeps what is listed
            text = text.Length > 200 ? text[..200] : text;
            if (text.Length < 2 || text == _nav.Query)
            {
                BuildHeader();
                return;
            }

            _nav.Query = text;
        }
        else
        {
            BuildHeader();
            return;
        }

        RebuildBody(resetScroll: true);
        BuildHeader();
    }

    /// <summary>YouTube: Enter on new text searches it.</summary>
    private void Submit(string text)
    {
        _nav.Query = text.Length > 200 ? text[..200] : text;
        if (!_svc.Scopes.Contains(_nav.Scope))
        {
            _nav.Scope = DefaultScope;
        }

        RebuildBody(resetScroll: true);
        BuildHeader();
    }

    private StreamScope DefaultScope => _svc.Scopes.Count > 0 ? _svc.Scopes[0] : StreamScope.All;

    private StreamScope CurrentScope => _svc.Scopes.Contains(_nav.Scope) ? _nav.Scope : DefaultScope;

    private void OpenContainer(StreamItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (_nav.Stack.Count == 0)
        {
            _nav.ReturnText = _search.Text;
        }

        _nav.Stack.Add(item);
        SetText("");
        _nav.Text = "";
        Rebuild(resetScroll: true);
    }

    private void CloseContainer()
    {
        if (_nav.Stack.Count == 0)
        {
            return;
        }

        _nav.Stack.RemoveAt(_nav.Stack.Count - 1);
        if (_nav.Stack.Count == 0)
        {
            var text = _nav.ReturnText;
            _nav.ReturnText = "";
            SetText(text);
            _nav.Text = text;
        }
        else
        {
            SetText("");
            _nav.Text = "";
        }

        Rebuild();
    }

    private void SetTab(StreamView tab)
    {
        if (_nav.Tab == tab && _nav.Stack.Count == 0)
        {
            return;
        }

        _nav.Tab = tab;
        _nav.Stack.Clear();
        Rebuild();
    }

    private void SetScope(StreamScope scope)
    {
        if (_nav.Scope == scope)
        {
            return;
        }

        _nav.Scope = scope;
        Flush();
        RebuildBody(resetScroll: true);
        BuildHeader();
    }

    // ---------------------------------------------------------------- the request

    /// <summary>The list on screen, from its first page; null when nothing is asked for (YouTube before the first Enter).</summary>
    private StreamRequest? CurrentRequest()
    {
        if (Container is { } open)
        {
            return new StreamRequest(StreamView.Container, "", StreamScope.All, open);
        }

        if (_nav.Query.Length > 0)
        {
            return new StreamRequest(StreamView.Search, _nav.Query, CurrentScope);
        }

        return Spotify ? new StreamRequest(_svc.Tabs.Contains(_nav.Tab) ? _nav.Tab : StreamView.Playlists) : null;
    }

    private static string RequestKey(StreamRequest request) => $"{request.View}|{request.Query}|{request.Scope}|{request.Container?.Key}";

    private string ScrollKey => CurrentRequest() is { } r ? RequestKey(r) : "";

    private sealed record Gathered(StreamRequest? Request, List<StreamPage> Pages, StreamLoad? Tail, StreamRequest? TailRequest, List<object?> Stamp);

    /// <summary>Asks the service for the list and every "Show more" page the user opened, and notes what a redraw would depend on.</summary>
    private Gathered Gather()
    {
        var stamp = new List<object?> { _svc.Connection, _svc.CanBrowse };
        var pages = new List<StreamPage>();
        if (!_svc.CanBrowse || CurrentRequest() is not { } request)
        {
            return new Gathered(null, pages, null, null, stamp);
        }

        stamp.Add(request);
        _nav.Expanded.TryGetValue(RequestKey(request), out var expanded);
        var offset = 0;
        StreamLoad? tail = null;
        StreamRequest? tailRequest = null;
        while (true)
        {
            var step = offset == 0 ? request : request with { Offset = offset };
            var load = _svc.Get(step);
            stamp.Add(load);
            if (load is { State: StreamLoadState.Ready, Page: { } page })
            {
                pages.Add(page);
                if (page.NextOffset is { } next && expanded is not null && expanded.Contains(next) && next > offset)
                {
                    offset = next;
                    continue;
                }
            }
            else
            {
                tail = load;
                tailRequest = step;
            }

            break;
        }

        return new Gathered(request, pages, tail, tailRequest, stamp);
    }

    // ---------------------------------------------------------------- state changes

    private void OnChanged() => Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
    {
        if (!IsLoaded)
        {
            return;
        }

        if (!_stamp.SequenceEqual(Gather().Stamp))
        {
            Rebuild();
        }
        else
        {
            BuildNote();
            _list?.Refresh();
        }
    });

    private void Rebuild(bool resetScroll = false)
    {
        _built = true;
        RebuildBody(resetScroll);
        BuildHeader();
    }

    // ---------------------------------------------------------------- header

    private HoverButton Segment(string text, bool selected, bool dim, Action click, string id)
    {
        var b = new HoverButton(click) { CornerRadius = new CornerRadius(8), Lifts = false, Selected = selected };
        var label = D.T(text, 12, selected ? FontWeights.SemiBold : FontWeights.Medium, selected ? D.Text : D.White(dim ? 0.3 : 0.6), false, TextTrimming.CharacterEllipsis);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Margin = new Thickness(4, 0, 4, 0);
        b.Child = label;
        AutomationProperties.SetAutomationId(b, id);
        AutomationProperties.SetName(b, text);
        return b;
    }

    private static FrameworkElement Plate(IReadOnlyList<HoverButton> segments)
    {
        var plate = new Border { CornerRadius = new CornerRadius(10), Background = D.White(0.065), Padding = new Thickness(2), Height = 28 };
        var grid = new Grid();
        for (var i = 0; i < segments.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(segments[i], i);
            grid.Children.Add(segments[i]);
        }

        plate.Child = grid;
        return plate;
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

    private string HintText() => Container?.Kind switch
    {
        StreamItemKind.Playlist => LibStrings.SearchInPlaylist,
        StreamItemKind.Album => LibStrings.SearchInAlbum,
        StreamItemKind.Artist => LibStrings.SearchInArtist,
        _ => SearchName,
    };

    private void BuildHeader()
    {
        // Spotify is not connected: there is nothing to browse, the body explains it
        if (Spotify && !_svc.CanBrowse)
        {
            _headerHost.Visibility = Visibility.Collapsed;
            _hint.Text = SearchName;
            return;
        }

        _headerHost.Visibility = Visibility.Visible;
        _hint.Text = HintText();
        if (Container is { } open)
        {
            _headerHost.Child = ContainerHeader(open);
        }
        else if (Spotify)
        {
            _headerHost.Child = Searching ? Chips() : Tabs();
        }
        else if (_search.Text.Trim().Length > 0 && _search.Text.Trim() != _nav.Query)
        {
            _headerHost.Child = HintLine();
        }
        else
        {
            _headerHost.Child = Searching ? Chips() : LinkPills();
        }
    }

    private FrameworkElement Tabs()
    {
        var segments = new List<HoverButton>();
        foreach (var view in _svc.Tabs)
        {
            var tab = view;
            var name = tab switch
            {
                StreamView.Liked => StreamStrings.LikedSongs,
                StreamView.Recent => StreamStrings.RecentlyPlayed,
                _ => LibStrings.Playlists,
            };
            segments.Add(Segment(name, _nav.Tab == tab, false, () => SetTab(tab), "Island.Stream.Tab." + tab));
        }

        return Plate(segments);
    }

    /// <summary>While searching the tab plate becomes the scope chips.</summary>
    private FrameworkElement Chips()
    {
        var segments = new List<HoverButton>();
        foreach (var scope in _svc.Scopes)
        {
            var s = scope;
            var name = s switch
            {
                StreamScope.Songs => LibStrings.Songs,
                StreamScope.Albums => LibStrings.Albums,
                StreamScope.Artists => LibStrings.Artists,
                StreamScope.Playlists => LibStrings.Playlists,
                _ => LibStrings.ScopeAll,
            };
            segments.Add(Segment(name, CurrentScope == s, false, () => SetScope(s), "Island.Stream.Scope." + s));
        }

        return Plate(segments);
    }

    /// <summary>YouTube: the pills that open YouTube Music, the liked music and the library in the browser (or the installed app).</summary>
    private FrameworkElement LinkPills()
    {
        var panel = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        if (_svc is YouTubeService youTube)
        {
            foreach (var link in youTube.Links)
            {
                var l = link;
                var pill = D.PillButton(l.Title, () => youTube.OpenLink(l), false, null, D.White(0.1), 26);
                pill.Margin = new Thickness(0, 1, 6, 1);
                panel.Children.Add(pill);
            }
        }

        return panel;
    }

    private static FrameworkElement HintLine()
    {
        var text = D.T(StreamStrings.PressEnterToSearch, 12, null, D.Secondary, false, TextTrimming.CharacterEllipsis);
        text.VerticalAlignment = VerticalAlignment.Center;
        text.Margin = new Thickness(6, 0, 0, 0);
        return text;
    }

    private FrameworkElement ContainerHeader(StreamItem container)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var back = D.IconButton(G.ChevronLeft, Strings.Back, CloseContainer, size: 28, glyphSize: 12);
        AutomationProperties.SetAutomationId(back, "Island.Stream.Back");
        grid.Children.Add(back);
        var title = D.T(container.Title.Length > 0 ? container.Title : ContainerFallbackTitle(container), 13, FontWeights.SemiBold, D.Text, false, TextTrimming.CharacterEllipsis);
        title.VerticalAlignment = VerticalAlignment.Center;
        title.Margin = new Thickness(4, 0, 8, 0);
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);
        if (Spotify)
        {
            var busy = _svc.BusyKey == container.Key;
            var play = ActionPill(G.Play, LibStrings.Play, () => _svc.Play(container, null), busy);
            AutomationProperties.SetAutomationId(play, "Island.Stream.Play");
            Grid.SetColumn(play, 2);
            grid.Children.Add(play);
            if (_svc is SpotifyService spotify)
            {
                var shuffle = ActionPill(G.Shuffle, Strings.Shuffle, () => spotify.Shuffle(container), false);
                shuffle.Margin = new Thickness(6, 0, 0, 0);
                AutomationProperties.SetAutomationId(shuffle, "Island.Stream.Shuffle");
                Grid.SetColumn(shuffle, 3);
                grid.Children.Add(shuffle);
            }
        }
        else
        {
            var open = ActionPill(G.OpenApp, StreamUiStrings.Open, () => _svc.OpenExternally(container), false);
            Grid.SetColumn(open, 2);
            grid.Children.Add(open);
        }

        return grid;
    }

    private static string ContainerFallbackTitle(StreamItem container) => container.Kind switch
    {
        StreamItemKind.Album => StreamUiStrings.Album,
        StreamItemKind.Artist => StreamUiStrings.Artist,
        _ => LibStrings.Playlists,
    };

    // ---------------------------------------------------------------- body

    private void RebuildBody(bool resetScroll)
    {
        _list = null;
        _externalSearch = null;
        _contentHost.Child = BuildContent(resetScroll);
        BuildNote();
    }

    private FrameworkElement BuildContent(bool resetScroll)
    {
        var gathered = Gather();
        _stamp = gathered.Stamp;
        _moreLoading = false;
        _moreMessage = null;
        if (!_svc.CanBrowse)
        {
            _model = null;
            AutomationProperties.SetItemStatus(_search, "");
            return ConnectView();
        }

        if (gathered.Request is null)
        {
            _model = null;
            return D.EmptyView(G.Search, StreamStrings.PressEnterToSearch);
        }

        if (gathered.Pages.Count == 0)
        {
            _model = null;
            AutomationProperties.SetItemStatus(_search, "");
            var load = gathered.Tail!;
            return load.State == StreamLoadState.Loading
                ? LoadingView(gathered.Request.View == StreamView.Search ? StreamStrings.Searching : StreamStrings.Loading)
                : ErrorView(load, gathered.TailRequest ?? gathered.Request);
        }

        if (gathered.Tail is { } more)
        {
            _moreLoading = more.State == StreamLoadState.Loading;
            _moreMessage = more.State == StreamLoadState.Error ? (more.Message.Length > 0 ? more.Message : StreamUiStrings.SomethingWentWrong) : null;
        }

        var request = gathered.Request;
        var searching = request.View == StreamView.Search;
        var grouped = searching && request.Scope == StreamScope.All && Container is null;
        _model ??= CreateModel();
        _model.Update(StreamListModel.Merge(gathered.Pages), grouped, Container is not null ? _search.Text : "", searching, _svc.CanQueue);
        _nav.HighlightId = _model.HighlightId;
        AutomationProperties.SetItemStatus(_search, StatusText());
        if (_model.Rows.Count == 0)
        {
            return EmptyList(request);
        }

        var key = ScrollKey;
        var list = new VirtualList(_model.Tops, Row);
        _list = list;
        list.Scrolled += offset => _nav.Scroll[key] = offset;
        if (!resetScroll && _nav.Scroll.TryGetValue(key, out var saved) && saved > 0)
        {
            list.Loaded += (_, _) => list.Offset = saved;
        }

        return list;
    }

    private StreamListModel CreateModel()
    {
        var model = new StreamListModel();
        model.Keep(_nav.HighlightId);
        return model;
    }

    private FrameworkElement EmptyList(StreamRequest request)
    {
        string message;
        var glyph = G.Music;
        if (_search.Text.Trim().Length > 0 && Container is not null)
        {
            message = LibStrings.NoResultsFor(_search.Text.Trim());
            glyph = G.Search;
        }
        else if (request.View == StreamView.Search)
        {
            message = LibStrings.NoResultsFor(request.Query);
            glyph = G.Search;
        }
        else if (request.View == StreamView.Liked)
        {
            message = StreamStrings.NoLikedSongs;
            glyph = G.Heart;
        }
        else if (request.View == StreamView.Recent)
        {
            message = StreamStrings.NothingPlayedRecently;
            glyph = G.History;
        }
        else if (request.View == StreamView.Playlists)
        {
            message = LibStrings.NoPlaylists;
            glyph = G.List;
        }
        else
        {
            message = LibStrings.NoSongs;
        }

        var actions = new List<UIElement>();
        if (Spotify && Container is { } open)
        {
            // a list Spotify gave nothing for (an artist without albums): the Spotify app has it
            actions.Add(D.PillButton(StreamStrings.OpenInSpotify, () => _svc.OpenExternally(open), true));
        }

        var empty = D.EmptyView(glyph, message, [.. actions]);
        empty.Margin = new Thickness(0, 0, 0, 24);
        return empty;
    }

    private static FrameworkElement LoadingView(string text)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var sp = PageUi.Spinner(16);
        sp.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(sp);
        var t = D.T(text, 12, null, D.Secondary);
        t.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(t);
        return stack;
    }

    /// <summary>A failed request: what happened and the one thing that helps (try again, open Settings, or open the item in Spotify).</summary>
    private FrameworkElement ErrorView(StreamLoad load, StreamRequest failed)
    {
        var actions = new List<UIElement>();
        switch (load.Status)
        {
            case StreamStatus.NotListed when Container is { } open:
                actions.Add(D.PillButton(StreamStrings.OpenInSpotify, () => _svc.OpenExternally(open), true));
                break;
            case StreamStatus.Revoked or StreamStatus.NotConfigured or StreamStatus.NotAllowlisted or StreamStatus.InvalidKey or StreamStatus.ApiDisabled:
                actions.Add(D.PillButton(Spotify ? StreamStrings.ConnectInSettings : Strings.SettingsEllipsis, OpenSettings, true));
                break;
            case StreamStatus.RateLimited:
                // the service asks again by itself when the wait is over
                break;
            default:
                actions.Add(D.PillButton(Strings.LyricsRetry, () => _svc.Retry(failed), true));
                break;
        }

        var message = load.Message.Length > 0 ? load.Message : StreamUiStrings.SomethingWentWrong;
        return D.EmptyView(G.Warning, message, [.. actions]);
    }

    /// <summary>Spotify is not set up or not signed in: say so, and offer Settings and the zero-setup search in Spotify itself.</summary>
    private FrameworkElement ConnectView()
    {
        var message = _svc.Connection == StreamConnection.NeedsReconnect ? StreamStrings.Message(StreamStatus.Revoked, StreamKind.Spotify) : StreamStrings.ConnectSpotifyToSearch;
        var connect = D.PillButton(StreamStrings.ConnectInSettings, OpenSettings, true);
        AutomationProperties.SetAutomationId(connect, "Island.Stream.Connect");
        var external = D.PillButton(StreamStrings.SearchInSpotify, () =>
        {
            var text = _search.Text.Trim();
            if (text.Length > 0)
            {
                _svc.OpenSearchExternally(text);
            }
        });
        external.Margin = new Thickness(6, 0, 0, 0);
        external.IsEnabled = _search.Text.Trim().Length > 0;
        AutomationProperties.SetAutomationId(external, "Island.Stream.SearchExternal");
        _externalSearch = external;
        return D.EmptyView(G.Music, message, connect, external);
    }

    private void OpenSettings()
    {
        _core.Ctx.Shell.OpenSettings(SettingsPages.Join(SettingsPages.Island, "content-music"));
        _window?.Collapse();
    }

    // ---------------------------------------------------------------- rows

    private FrameworkElement Row(int index)
    {
        var row = _model!.Rows[index];
        var highlighted = _model.Highlight == index;
        FrameworkElement element = row.Kind switch
        {
            StreamRowKind.Header => HeaderRow(row),
            StreamRowKind.ShowMore => MoreRow(index, highlighted),
            _ => ItemRow(row, index, highlighted),
        };
        if (row.Selectable)
        {
            AutomationProperties.SetAutomationId(element, "Island.Stream.Result." + index);
        }

        return element;
    }

    private FrameworkElement HeaderRow(StreamRow header)
    {
        var grid = new Grid { Margin = new Thickness(6, 4, 4, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = D.T(header.Title, 11, FontWeights.SemiBold, D.Secondary, false, TextTrimming.CharacterEllipsis);
        title.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(title);
        if (header.Total > header.Shown)
        {
            var more = new HoverButton(() => Execute(StreamListModel.ShowAllOf(header))) { Height = 20, Padding = new Thickness(8, 0, 8, 0), CornerRadius = new CornerRadius(10), Lifts = false, VerticalAlignment = VerticalAlignment.Center };
            more.Child = D.T(LibStrings.ShowAll(header.Total), 11, FontWeights.Medium, D.White(0.6));
            ((TextBlock)more.Child).VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetAutomationId(more, "Island.Stream.ShowAll." + header.Target);
            AutomationProperties.SetName(more, LibStrings.ShowAll(header.Total));
            Grid.SetColumn(more, 1);
            grid.Children.Add(more);
        }

        return grid;
    }

    private HoverButton RowButton(StreamRow data, int index, bool highlighted, string name)
    {
        var button = new HoverButton(() =>
        {
            var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            _model?.SetHighlight(index);
            if (_model is not null)
            {
                _nav.HighlightId = _model.HighlightId;
            }

            Execute(StreamListModel.ActionFor(data, shift, ctrl, _svc.CanQueue));
        })
        {
            Margin = new Thickness(0, 1, 0, 1),
            CornerRadius = new CornerRadius(10),
            Lifts = false,
            Fill = highlighted ? D.White(0.13) : null,
        };
        AutomationProperties.SetName(button, name);
        return button;
    }

    private FrameworkElement MoreRow(int index, bool highlighted)
    {
        var row = RowButton(_model!.Rows[index], index, highlighted, StreamStrings.ShowMore);
        var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (_moreLoading)
        {
            var spinner = PageUi.Spinner(12);
            spinner.Margin = new Thickness(0, 0, 8, 0);
            spinner.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(spinner);
        }

        var text = _moreMessage ?? (_moreLoading ? StreamStrings.Loading : StreamStrings.ShowMore);
        line.Children.Add(D.T(text, 12, FontWeights.Medium, _moreMessage is null ? D.White(0.7) : D.Orange, false, TextTrimming.CharacterEllipsis));
        row.Child = line;
        AutomationProperties.SetAutomationId(row, "Island.Stream.ShowMore");
        return row;
    }

    private static string GlyphFor(StreamItem item) => item.Kind switch
    {
        StreamItemKind.Album => G.MusicLibrary,
        StreamItemKind.Artist => G.Face,
        StreamItemKind.Playlist => G.List,
        StreamItemKind.Video => G.Video,
        _ => G.Music,
    };

    /// <summary>The picture tile of a row: Spotify's artwork whole (square, small radius), YouTube's thumbnail 16:9 and centre-cropped, else a glyph.</summary>
    private FrameworkElement Tile(StreamItem item, bool busy)
    {
        const double Height = 32;
        const double Radius = 4;
        var tile = new Grid { Width = _tileWidth, Height = Height, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var plate = new Border { CornerRadius = new CornerRadius(Radius), Background = D.White(0.08), Child = D.I(GlyphFor(item), 12, D.White(0.45)) };
        tile.Children.Add(plate);
        if (item.ImageUrl is { Length: > 0 } url)
        {
            var width = _tileWidth;
            var stretch = _wide ? Stretch.UniformToFill : Stretch.Uniform;
            StreamArt.Load(url, image =>
            {
                var picture = new Image { Source = image, Stretch = stretch, Width = width, Height = Height, Clip = new RectangleGeometry(new Rect(0, 0, width, Height), Radius, Radius) };
                RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
                plate.Child = picture;
            });
        }

        if (busy)
        {
            var spinner = PageUi.Spinner(13);
            spinner.HorizontalAlignment = HorizontalAlignment.Center;
            spinner.VerticalAlignment = VerticalAlignment.Center;
            tile.Children.Add(new Border { CornerRadius = new CornerRadius(Radius), Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)), Child = spinner });
        }

        return tile;
    }

    private FrameworkElement ItemRow(StreamRow data, int index, bool highlighted)
    {
        var item = data.Item!;
        var isSong = item.Kind is StreamItemKind.Track or StreamItemKind.Video;
        var busy = _svc.BusyKey == item.Key;
        var subtitle = ItemSubtitle(item);
        var title = string.IsNullOrWhiteSpace(item.Title) ? "—" : item.Title;
        var row = RowButton(data, index, highlighted, title + ", " + subtitle);
        var grid = new Grid { Margin = new Thickness(6, 0, 4, 0) };
        if (!item.Playable)
        {
            // listed so the result is not silently missing, but dimmed and not playable
            grid.Opacity = 0.45;
            D.Tip(row, item.Id.StartsWith("local:", StringComparison.Ordinal) ? StreamStrings.LocalFile : StreamStrings.NotAvailableInCountry);
        }

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_tileWidth + 8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(Tile(item, busy));
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        texts.Children.Add(D.T(title, 13, FontWeights.SemiBold, D.Text, false, TextTrimming.CharacterEllipsis));
        texts.Children.Add(D.T(subtitle, 11, null, D.Secondary, false, TextTrimming.CharacterEllipsis));
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        if (isSong && item.Duration > 0)
        {
            var time = D.Digits(D.TrackTime(item.Duration), 11, null, D.Tertiary);
            time.VerticalAlignment = VerticalAlignment.Center;
            time.Margin = new Thickness(0, 0, 4, 0);
            Grid.SetColumn(time, 2);
            grid.Children.Add(time);
        }
        else if (!isSong)
        {
            var chevron = D.I(G.ChevronRight, 10, D.White(0.4));
            chevron.VerticalAlignment = VerticalAlignment.Center;
            chevron.Margin = new Thickness(0, 0, 4, 0);
            Grid.SetColumn(chevron, 2);
            grid.Children.Add(chevron);
        }

        HoverButton? more = null;
        more = new HoverButton(() => IslandMenu.Show(more!, ItemMenu(item), () => _window?.HoldOpen())) { Width = 26, Height = 26, CornerRadius = new CornerRadius(8), Lifts = false, VerticalAlignment = VerticalAlignment.Center };
        var dots = D.I(G.More, 12, D.White(0.6));
        more.Child = dots;
        more.Icon = dots;
        D.Tip(more, LibStrings.More);
        Grid.SetColumn(more, 3);
        grid.Children.Add(more);
        row.Child = grid;
        return row;
    }

    private static string ItemSubtitle(StreamItem item)
    {
        if (item.Kind == StreamItemKind.Playlist && item.Count > 0)
        {
            return item.Subtitle.Length > 0 ? item.Subtitle + " · " + LibStrings.Count(item.Count) : LibStrings.Count(item.Count);
        }

        if (item.Kind == StreamItemKind.Album && item.Count > 0)
        {
            return item.Subtitle.Length > 0 ? item.Subtitle + " · " + LibStrings.Count(item.Count) : LibStrings.Count(item.Count);
        }

        return item.Subtitle;
    }

    // ---------------------------------------------------------------- menus and actions

    private IEnumerable<IslandMenu.Item> ItemMenu(StreamItem item)
    {
        if (Spotify)
        {
            if (item.Kind == StreamItemKind.Track)
            {
                var playTitle = _svc.CanPlayHere == false ? StreamStrings.OpenInSpotify : LibStrings.Play;
                yield return new IslandMenu.Item(playTitle, () => Play(item), Enabled: item.Playable, Glyph: G.Play);
                yield return new IslandMenu.Item(LibStrings.AddToQueue, () => _svc.Queue(item), Enabled: item.Playable && _svc.CanQueue, Glyph: G.List);
                yield return IslandMenu.Sep();
                yield return new IslandMenu.Item(LibStrings.GoToAlbum, () => OpenContainer(AlbumOf(item)), Enabled: item.AlbumId is not null);
                yield return new IslandMenu.Item(LibStrings.GoToArtist, () => OpenContainer(ArtistOf(item)), Enabled: item.ArtistId is not null);
                yield return IslandMenu.Sep();
            }

            var local = item.Id.StartsWith("local:", StringComparison.Ordinal);
            yield return new IslandMenu.Item(StreamStrings.OpenInSpotify, () => _svc.OpenExternally(item), Enabled: !local, Glyph: G.OpenApp);
            yield return new IslandMenu.Item(StreamStrings.CopyLink, () => CopyLink(item), Enabled: !local, Glyph: G.Copy);
            yield break;
        }

        var playlistId = item.Kind == StreamItemKind.Video && Container is { Kind: StreamItemKind.Playlist } list ? list.Id : null;
        yield return new IslandMenu.Item(StreamStrings.OpenInYouTubeMusic, () => OpenYouTube(item, true, playlistId), Glyph: G.OpenApp);
        yield return new IslandMenu.Item(StreamStrings.OpenInYouTube, () => OpenYouTube(item, false, playlistId), Glyph: G.Video);
        yield return IslandMenu.Sep();
        yield return new IslandMenu.Item(StreamStrings.CopyLink, () => CopyLink(item), Glyph: G.Copy);
    }

    private static StreamItem AlbumOf(StreamItem track) =>
        new(StreamItemKind.Album, track.AlbumId!, track.AlbumName ?? "", "") { Uri = SpotifyUri.Build("album", track.AlbumId!) };

    private static StreamItem ArtistOf(StreamItem track)
    {
        var first = track.Subtitle.Split(',')[0].Trim();
        return new StreamItem(StreamItemKind.Artist, track.ArtistId!, first, StreamUiStrings.Artist) { Uri = SpotifyUri.Build("artist", track.ArtistId!) };
    }

    /// <summary>Opens one video or playlist in YouTube Music or in YouTube, whatever "Open songs in" says.</summary>
    private void OpenYouTube(StreamItem item, bool music, string? playlistId)
    {
        var url = item.Kind == StreamItemKind.Playlist ? YouTubeLinks.Playlist(item.Id, music) : YouTubeLinks.Watch(item.Id, music, playlistId);
        MusicLinks.Open(url, _core.Ctx.Log);
    }

    private void CopyLink(StreamItem item)
    {
        var url = _svc.ShareUrl(item);
        if (url.Length == 0)
        {
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(url);
            ShowLocalNote(StreamUiStrings.Copied);
        }
        catch (COMException)
        {
            // another program has the clipboard open: nothing was copied
            ShowLocalNote(StreamUiStrings.SomethingWentWrong, error: true);
        }
    }

    /// <summary>Plays a song: within the open album or playlist, or, in Liked Songs only, with the songs that follow it.</summary>
    private void Play(StreamItem item)
    {
        var request = CurrentRequest();
        var context = Container is { Kind: StreamItemKind.Album or StreamItemKind.Playlist } open ? open : null;
        var siblings = request is { View: StreamView.Liked } && _model is not null ? _model.Items() : null;
        _svc.Play(item, context, siblings);
    }

    private void Execute(StreamAction action)
    {
        switch (action.Kind)
        {
            case StreamActionKind.Play:
                Play(action.Item!);
                break;
            case StreamActionKind.Queue:
                _svc.Queue(action.Item!);
                break;
            case StreamActionKind.Open:
                _svc.Play(action.Item!, Container);
                break;
            case StreamActionKind.OpenContainer:
                OpenContainer(action.Item);
                break;
            case StreamActionKind.ShowAll:
                SetScope(action.Scope);
                break;
            case StreamActionKind.ShowMore:
                ShowMore();
                break;
        }
    }

    private void ShowMore()
    {
        if (_model?.NextOffset is not { } next || CurrentRequest() is not { } request || _moreLoading)
        {
            return;
        }

        var key = RequestKey(request);
        if (!_nav.Expanded.TryGetValue(key, out var expanded))
        {
            if (_nav.Expanded.Count > 32)
            {
                _nav.Expanded.Clear();
            }

            expanded = [];
            _nav.Expanded[key] = expanded;
        }

        if (!expanded.Add(next))
        {
            // already asked for and failed: ask again
            _svc.Retry(request with { Offset = next });
        }

        RebuildBody(resetScroll: false);
    }

    private void BuildNote()
    {
        var note = _localNote is not null ? new StreamNote(_localNote, _localNoteError) : _svc.Note;
        if (note is null)
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

// ==================================================================== Up next for Spotify

/// <summary>"Up next" for Spotify, read from the Web API (read-only): the queue of the account's active player, with a refresh button.</summary>
internal sealed class SpotifyQueueView : Grid
{
    private readonly SpotifyService _svc;

    /// <summary>Creates the view.</summary>
    public SpotifyQueueView(IslandCore core)
    {
        _svc = core.Music.Spotify;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Margin = new Thickness(0, 6, 0, 0);
        var header = new Grid { Height = 24 };
        header.Children.Add(D.T(Strings.Queue, 13, FontWeights.SemiBold, D.Text));
        var refresh = D.IconButton(G.Refresh, Strings.Refresh, () => _svc.Retry(SpotifyService.QueueRequest), size: 24, glyphSize: 11);
        refresh.HorizontalAlignment = HorizontalAlignment.Right;
        AutomationProperties.SetAutomationId(refresh, "Island.Stream.QueueRefresh");
        header.Children.Add(refresh);
        Children.Add(header);
        Loaded += (_, _) =>
        {
            _svc.Changed += OnChanged;
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

        var load = _svc.GetQueue();
        FrameworkElement body;
        if (load.State == StreamLoadState.Loading)
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var sp = PageUi.Spinner(16);
            sp.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(sp);
            var t = D.T(StreamStrings.Loading, 12, null, D.Secondary);
            t.Margin = new Thickness(0, 8, 0, 0);
            stack.Children.Add(t);
            body = stack;
        }
        else if (load.State == StreamLoadState.Error)
        {
            // Spotify was disconnected while this was open: that has no message of its own, and is not a fault
            body = load.Status == StreamStatus.NotConnected
                ? D.EmptyView(G.List, Strings.QueueUnavailable)
                : D.EmptyView(G.Warning, load.Message.Length > 0 ? load.Message : StreamUiStrings.SomethingWentWrong);
        }
        else
        {
            var items = (load.Page?.Sections ?? []).SelectMany(s => s.Items).ToList();
            if (items.Count == 0)
            {
                body = D.EmptyView(G.List, Strings.QueueEmpty);
            }
            else
            {
                var list = new StackPanel();
                foreach (var q in items)
                {
                    var row = new Border { Height = 40, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 1, 0, 1), Padding = new Thickness(8, 0, 8, 0) };
                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                    texts.Children.Add(D.T(q.Title, 13, FontWeights.SemiBold, D.Text, false, TextTrimming.CharacterEllipsis));
                    texts.Children.Add(D.T(q.Subtitle, 11, null, D.Secondary, false, TextTrimming.CharacterEllipsis));
                    grid.Children.Add(texts);
                    if (q.Duration > 0)
                    {
                        var time = D.Digits(D.TrackTime(q.Duration), 11, null, D.Tertiary);
                        time.VerticalAlignment = VerticalAlignment.Center;
                        Grid.SetColumn(time, 1);
                        grid.Children.Add(time);
                    }

                    row.Child = grid;
                    list.Children.Add(row);
                }

                body = PageUi.VScroll(list);
            }
        }

        SetRow(body, 1);
        Children.Add(body);
    }
}
