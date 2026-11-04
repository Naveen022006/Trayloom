// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Trayloom.Core;

namespace Trayloom.Features.Island;

/// <summary>English strings of the Apple Music library browser.</summary>
internal static class LibStrings
{
    public static string Library => Loc.T("Apple Music library");
    public static string NotPlayable => Loc.T("Not in your library or a playlist");
    public static string Playlists => Loc.T("Playlists");
    public static string Songs => Loc.T("Songs");
    public static string Recent => Loc.T("Recently added");
    public static string SearchLibrary => Loc.T("Search");
    public static string SearchHint => Loc.T("Search songs, albums, artists, playlists");
    public static string SearchTip => Loc.T("Search your Apple Music library");
    public static string SearchInPlaylist => Loc.T("Search in this playlist");
    public static string SearchInAlbum => Loc.T("Search in this album");
    public static string SearchInArtist => Loc.T("Search in this artist");
    public static string ScopeAll => Loc.T("All");
    public static string Albums => Loc.T("Albums");
    public static string Artists => Loc.T("Artists");
    public static string GoToAlbum => Loc.T("Go to album");
    public static string GoToArtist => Loc.T("Go to artist");
    public static string ClickToType => Loc.T("Click the search box again to type");
    public static string Play => Loc.T("Play");
    public static string PlayNext => Loc.T("Play Next");
    public static string AddToQueue => Loc.T("Add to Queue");
    public static string More => Loc.T("More");
    public static string AddedNext => Loc.T("Playing next");
    public static string AddedQueue => Loc.T("Added to Up next");
    public static string NotStarted => Loc.T("Apple Music didn't start");
    public static string NotFound => Loc.T("Couldn't find it in Apple Music");
    public static string NoResponse => Loc.T("Apple Music didn't respond");
    public static string NothingToResume => Loc.T("Apple Music has nothing to resume - pick a song in Library");
    public static string SongNotStarted => Loc.T("Apple Music didn't start this song");
    public static string PlaylistNotStarted => Loc.T("Apple Music didn't start this playlist");
    public static string Missing => Loc.T("Apple Music library not found");
    public static string MissingHint => Loc.T("Open Apple Music once and let it set up your library, then try again.");
    public static string Unreadable => Loc.T("Couldn't read the Apple Music library");
    public static string UnreadableHint => Loc.T("Trayloom reads it from your Music folder. Try again in a moment.");
    public static string Loading => Loc.T("Loading your library…");
    public static string NoSongs => Loc.T("No songs");
    public static string NoPlaylists => Loc.T("No playlists");
    public static string NoRecent => Loc.T("No recently added songs");
    public static string UpNextHint => Loc.T("Nothing is queued in Apple Music.");
    public static string UpNextLoading => Loc.T("Reading the queue…");
    public static string ShowApple => Loc.T("Show Apple Music");
    public static string HideApple => Loc.T("Hide Apple Music");
    public static string KeptTitle => Loc.T("Apple Music keeps playing");
    public static string KeptDetail => Loc.T("Show it from the island");
    public static string ClosedTitle => Loc.T("Apple Music was closed");
    public static string Resuming => Loc.T("Keeping your music playing…");
    public static string StartingApple => Loc.T("Starting Apple Music…");
    public static string StillStarting => Loc.T("Still starting Apple Music…");

    public static string Count(int n) => n == 1 ? Loc.T("1 song") : Loc.F("{0} songs", n);

    public static string ShowAll(int n) => Loc.F("Show all ({0})", n);

    public static string NoResultsFor(string query) => Loc.F("No results for “{0}”", query);

    public static string InPlaylist(string name) => Loc.F("in {0}", name);

    public static string ResultCount(int n) => Loc.F("{0} results", n);
}

// ==================================================================== search model (no UI: tested by the harness)

/// <summary>What a search covers: everything grouped, or one kind of result.</summary>
internal enum LibScope
{
    All,
    Songs,
    Albums,
    Artists,
    Playlists,
}

internal enum LibRowKind
{
    Header,
    Song,
    Album,
    Artist,
    Playlist,
}

internal enum LibContainerKind
{
    Playlist,
    Album,
    Artist,
}

/// <summary>A list the Library opened into: a playlist, an album or an artist (a track list with a back arrow).</summary>
internal sealed record LibContainer(LibContainerKind Kind, string Key, string Name, IReadOnlyList<AmTrack> Tracks, AmPlaylist? Playlist = null)
{
    /// <summary>The same list on a newer snapshot of the library (tracks found again by id; the ones that vanished are dropped).</summary>
    public LibContainer Rebind(AmDb db) =>
        this with { Tracks = [.. Tracks.Select(t => db.ByPid(t.Pid)).OfType<AmTrack>()] };
}

/// <summary>One line of the result list: a section header, a song, an album, an artist or a playlist.</summary>
internal sealed record LibRow(LibRowKind Kind, string Id)
{
    public AmTrack? Track { get; init; }

    public AmAlbum? Album { get; init; }

    public AmArtist? Artist { get; init; }

    public AmPlaylist? Playlist { get; init; }

    /// <summary>The header's title.</summary>
    public string Title { get; init; } = "";

    /// <summary>For a playlist-only song found in a search: the playlist it plays from (shown as "in &lt;playlist&gt;").</summary>
    public string Via { get; init; } = "";

    /// <summary>The playlist a song plays in (null = the Songs page).</summary>
    public AmPlaylist? Context { get; init; }

    /// <summary>A header: how many results the section has in all, how many are listed, and the scope "Show all" switches to.</summary>
    public int Total { get; init; }

    public int Shown { get; init; }

    public LibScope Target { get; init; }

    public double Height => Kind == LibRowKind.Header ? LibrarySearchModel.HeaderHeight : LibrarySearchModel.RowHeight;

    public bool Selectable => Kind != LibRowKind.Header;
}

internal enum LibActionKind
{
    None,
    Play,
    PlayNext,
    AddToQueue,
    OpenAlbum,
    OpenArtist,
    OpenPlaylist,
    ShowAll,
}

/// <summary>What pressing Enter on (or clicking) a row does.</summary>
internal sealed record LibAction(LibActionKind Kind)
{
    public AmTrack? Track { get; init; }

    public AmPlaylist? Context { get; init; }

    /// <summary>The Apple Music page the song is looked up on when it has no playlist to play from.</summary>
    public string Page { get; init; } = "songs";

    public AmAlbum? Album { get; init; }

    public AmArtist? Artist { get; init; }

    public AmPlaylist? Playlist { get; init; }

    public LibScope Scope { get; init; }
}

/// <summary>What Esc does next in the Library.</summary>
internal enum LibStep
{
    Clear,
    Blur,
    Back,
    Close,
}

/// <summary>
/// The rows of the Library list: the browse tabs, a global search (grouped, or one kind with a scope chip) and the inside of a playlist,
/// album or artist (filtered by the same box). Also the highlight, and the action each row has. No UI here.
/// </summary>
internal sealed class LibrarySearchModel
{
    public const double HeaderHeight = 24;
    public const double RowHeight = 44;
    public const int GroupCap = 4;
    public const int PageStep = 6;

    private List<LibRow> _rows = [];
    private double[] _tops = [0];
    private int[] _selectable = [];
    private string _highlightId = "";

    public LibrarySearchModel(AmDb db) => Db = db;

    public AmDb Db { get; }

    /// <summary>The text as typed.</summary>
    public string Text { get; private set; } = "";

    /// <summary>The folded text; a search is on when it is not empty.</summary>
    public string Query { get; private set; } = "";

    public bool Searching => Query.Length > 0;

    public int Tab { get; private set; } = 1;

    /// <summary>The scope in effect (All while there is no query).</summary>
    public LibScope Scope { get; private set; }

    public LibContainer? Container { get; private set; }

    public IReadOnlyList<LibRow> Rows => _rows;

    /// <summary>Row heights as a prefix sum: row i spans Tops[i] to Tops[i + 1].</summary>
    public IReadOnlyList<double> Tops => _tops;

    /// <summary>The highlighted row (Enter acts on it); -1 for none.</summary>
    public int Highlight { get; private set; } = -1;

    public string HighlightId => _highlightId;

    /// <summary>Seeds the highlight of a model that replaces an older one (a reloaded library).</summary>
    public void Keep(string id) => _highlightId = id;

    /// <summary>The raw results of the last global search (their totals feed the scope chips).</summary>
    public AmSearchResults Results { get; private set; } = AmSearchResults.None;

    public int SelectableCount => _selectable.Length;

    /// <summary>How many results a scope has (the chip is dimmed at 0).</summary>
    public int ScopeTotal(LibScope scope) => scope switch
    {
        LibScope.Songs => Results.SongTotal,
        LibScope.Albums => Results.AlbumTotal,
        LibScope.Artists => Results.ArtistTotal,
        LibScope.Playlists => Results.PlaylistTotal,
        _ => Results.SongTotal + Results.AlbumTotal + Results.ArtistTotal + Results.PlaylistTotal,
    };

    /// <summary>
    /// Builds the rows for the given state. The highlighted row stays on the same item when it is still listed; else a search (or a
    /// filter inside a list) highlights the top hit, and browsing highlights nothing until an arrow key is pressed.
    /// </summary>
    public void Update(string text, int tab, LibScope scope, LibContainer? container)
    {
        var keep = _highlightId;
        Text = text ?? "";
        Query = AmSearchIndex.Fold(Text);
        Tab = tab;
        Container = container;
        Scope = Query.Length == 0 || container is not null ? LibScope.All : scope;
        Results = AmSearchResults.None;
        var rows = new List<LibRow>();
        if (container is not null)
        {
            BuildContainer(rows, container);
        }
        else if (Query.Length > 0)
        {
            BuildSearch(rows);
        }
        else
        {
            BuildBrowse(rows);
        }

        _rows = rows;
        _tops = new double[rows.Count + 1];
        var selectable = new List<int>();
        for (var i = 0; i < rows.Count; i++)
        {
            _tops[i + 1] = _tops[i] + rows[i].Height;
            if (rows[i].Selectable)
            {
                selectable.Add(i);
            }
        }

        _selectable = [.. selectable];
        var at = keep.Length > 0 ? rows.FindIndex(r => r.Id == keep) : -1;
        if (at < 0 && (Query.Length > 0) && _selectable.Length > 0)
        {
            at = _selectable[0];
        }

        SetHighlightIndex(at);
    }

    private void SetHighlightIndex(int index)
    {
        Highlight = index >= 0 && index < _rows.Count ? index : -1;
        _highlightId = Highlight >= 0 ? _rows[Highlight].Id : "";
    }

    /// <summary>Highlights a row (a click on it).</summary>
    public void SetHighlight(int index) => SetHighlightIndex(index >= 0 && index < _rows.Count && _rows[index].Selectable ? index : -1);

    /// <summary>Moves the highlight by <paramref name="delta"/> result rows (headers are skipped); false when it did not move.</summary>
    public bool Move(int delta)
    {
        if (_selectable.Length == 0 || delta == 0)
        {
            return false;
        }

        var position = Array.IndexOf(_selectable, Highlight);
        int target;
        if (position < 0)
        {
            target = delta > 0 ? Math.Min(delta - 1, _selectable.Length - 1) : _selectable.Length - 1;
        }
        else
        {
            target = Math.Clamp(position + delta, 0, _selectable.Length - 1);
        }

        var next = _selectable[target];
        if (next == Highlight)
        {
            return false;
        }

        SetHighlightIndex(next);
        return true;
    }

    /// <summary>The row to keep in view with the highlight: a header right above the first row of a section comes with it.</summary>
    public int LeadRows(int index) => index > 0 && index < _rows.Count && _rows[index - 1].Kind == LibRowKind.Header ? 1 : 0;

    // ---------------------------------------------------------------- actions

    /// <summary>What Enter does: play (Shift: play next, Ctrl: add to the queue) or open the album / artist / playlist.</summary>
    public LibAction Activate(bool shift, bool ctrl) =>
        Highlight >= 0 && Highlight < _rows.Count ? ActionFor(_rows[Highlight], shift, ctrl) : new LibAction(LibActionKind.None);

    public static LibAction ActionFor(LibRow row, bool shift = false, bool ctrl = false) => row.Kind switch
    {
        LibRowKind.Song when !CanPlay(row.Track, row.Context) => new LibAction(LibActionKind.None),
        LibRowKind.Song => new LibAction(shift ? LibActionKind.PlayNext : ctrl ? LibActionKind.AddToQueue : LibActionKind.Play) { Track = row.Track, Context = row.Context },
        LibRowKind.Album => new LibAction(LibActionKind.OpenAlbum) { Album = row.Album },
        LibRowKind.Artist => new LibAction(LibActionKind.OpenArtist) { Artist = row.Artist },
        LibRowKind.Playlist => new LibAction(LibActionKind.OpenPlaylist) { Playlist = row.Playlist },
        _ => new LibAction(LibActionKind.None),
    };

    /// <summary>
    /// Whether a song can be played from the list it is shown in: a library song is on the Songs page, a playlist-only song needs a listed
    /// playlist that holds it. A catalogue track in no listed playlist is nowhere Apple Music's lists show it.
    /// </summary>
    public static bool CanPlay(AmTrack? track, AmPlaylist? context) => track is { InLibrary: true } || context is not null;

    /// <summary>The "Show all (N)" of a section header.</summary>
    public static LibAction ShowAllOf(LibRow header) => new(LibActionKind.ShowAll) { Scope = header.Target };

    /// <summary>Esc: clear the text first (the box keeps the keyboard), then leave the box, then leave the open list, then close the Library.</summary>
    public static LibStep Escape(bool hasText, bool focused, bool containerOpen) =>
        hasText ? LibStep.Clear : focused ? LibStep.Blur : containerOpen ? LibStep.Back : LibStep.Close;

    // ---------------------------------------------------------------- album / artist of a song

    public AmAlbum? AlbumOf(AmTrack track)
    {
        if (string.IsNullOrWhiteSpace(track.Album))
        {
            return null;
        }

        return Db.Search.Query(track.Album, int.MaxValue).Albums.Select(h => h.Item).FirstOrDefault(a => a.Tracks.Any(t => t.Pid == track.Pid));
    }

    public AmArtist? ArtistOf(AmTrack track)
    {
        foreach (var name in AmSearchIndex.SplitArtists(track.Artist))
        {
            if (Db.Search.Query(name, int.MaxValue).Artists.Select(h => h.Item).FirstOrDefault(a => a.Tracks.Any(t => t.Pid == track.Pid)) is { } artist)
            {
                return artist;
            }
        }

        return null;
    }

    public static LibContainer ForAlbum(AmAlbum album) => new(LibContainerKind.Album, album.Key, album.Name, album.Tracks);

    public static LibContainer ForArtist(AmArtist artist) => new(LibContainerKind.Artist, artist.Key, artist.Name, artist.Tracks);

    public static LibContainer ForPlaylist(AmPlaylist playlist) => new(LibContainerKind.Playlist, playlist.Pid.ToString(), playlist.Name, playlist.Tracks, playlist);

    // ---------------------------------------------------------------- building

    private LibRow SongRow(AmTrack track, AmPlaylist? context, bool via) => new(LibRowKind.Song, "s:" + track.Pid)
    {
        Track = track,
        Context = context,
        Via = via && !track.InLibrary ? context?.Name ?? "" : "",
    };

    private void BuildBrowse(List<LibRow> rows)
    {
        if (Tab == 0)
        {
            rows.AddRange(Db.Playlists.Select(p => new LibRow(LibRowKind.Playlist, "p:" + p.Pid) { Playlist = p }));
            return;
        }

        foreach (var t in Tab == 2 ? Db.Recent : Db.Songs)
        {
            rows.Add(SongRow(t, null, false));
        }
    }

    private void BuildContainer(List<LibRow> rows, LibContainer container)
    {
        IEnumerable<AmTrack> tracks = container.Tracks;
        if (Query.Length > 0)
        {
            // the same ranking as the global search, kept to the tracks of this list
            var inside = container.Tracks.Select(t => t.Pid).ToHashSet();
            tracks = Db.Search.Query(Text, int.MaxValue).Songs.Where(h => inside.Contains(h.Item.Pid)).Select(h => h.Item);
        }

        foreach (var t in tracks)
        {
            var context = container.Kind == LibContainerKind.Playlist ? container.Playlist : Db.DefaultContext(t);
            rows.Add(SongRow(t, context, container.Kind != LibContainerKind.Playlist));
        }
    }

    private void BuildSearch(List<LibRow> rows)
    {
        var all = Scope == LibScope.All;
        var r = Db.Search.Query(Text, all ? GroupCap : int.MaxValue);
        Results = r;
        if (Scope is LibScope.All or LibScope.Songs)
        {
            Group(rows, all, LibStrings.Songs, LibScope.Songs, r.SongTotal, r.Songs.Select(h => SongRow(h.Item, Db.DefaultContext(h.Item), true)));
        }

        if (Scope is LibScope.All or LibScope.Albums)
        {
            Group(rows, all, LibStrings.Albums, LibScope.Albums, r.AlbumTotal, r.Albums.Select(h => new LibRow(LibRowKind.Album, "al:" + h.Item.Key) { Album = h.Item }));
        }

        if (Scope is LibScope.All or LibScope.Artists)
        {
            Group(rows, all, LibStrings.Artists, LibScope.Artists, r.ArtistTotal, r.Artists.Select(h => new LibRow(LibRowKind.Artist, "ar:" + h.Item.Key) { Artist = h.Item }));
        }

        if (Scope is LibScope.All or LibScope.Playlists)
        {
            Group(rows, all, LibStrings.Playlists, LibScope.Playlists, r.PlaylistTotal, r.Playlists.Select(h => new LibRow(LibRowKind.Playlist, "p:" + h.Item.Pid) { Playlist = h.Item }));
        }
    }

    private static void Group(List<LibRow> rows, bool withHeader, string title, LibScope target, int total, IEnumerable<LibRow> items)
    {
        var list = items.ToList();
        if (list.Count == 0)
        {
            return;
        }

        if (withHeader)
        {
            rows.Add(new LibRow(LibRowKind.Header, "h:" + target) { Title = title, Total = total, Shown = list.Count, Target = target });
        }

        rows.AddRange(list);
    }
}

// ==================================================================== keyboard focus across page rebuilds (no UI: tested by the harness)

/// <summary>
/// Keeps the search box's keyboard when the Music page is rebuilt under it (a song change, a timeline change, a note): the box that goes
/// away parks what it holds (caret, the island's keyboard, the keep-open hold) and the box that comes back within a second takes it over.
/// If nothing takes it within half a second it is released, so the keyboard never stays with an island that has no box.
/// </summary>
internal sealed class LibraryFocusCarry
{
    /// <summary>A new box may take a parked carry up to this old.</summary>
    public const long TakeWithinMs = 1000;

    /// <summary>The check that releases an unclaimed carry runs this long after it was parked.</summary>
    public const long ReleaseAfterMs = 500;

    /// <summary>A "focus the box" gesture (the magnifier) waits for the rebuilt page this long.</summary>
    public const long RequestWithinMs = 3000;

    private long _serial;
    private long _requestAt = long.MinValue;

    public bool Parked { get; private set; }

    public int Caret { get; private set; }

    public bool TookKeyboard { get; private set; }

    public IDisposable? Hold { get; private set; }

    public long Tick { get; private set; }

    /// <summary>Set when the page is being left for good (another module, Explore): a box that goes away then releases at once.</summary>
    public bool LeaveRequested { get; set; }

    /// <summary>Parks what the box holds; returns the number the expiry check must present.</summary>
    public long Park(int caret, bool tookKeyboard, IDisposable? hold, long now)
    {
        if (Parked)
        {
            tookKeyboard |= TookKeyboard;
            if (!ReferenceEquals(Hold, hold))
            {
                Hold?.Dispose();
            }
        }

        Parked = true;
        Caret = caret;
        TookKeyboard = tookKeyboard;
        Hold = hold;
        Tick = now;
        return ++_serial;
    }

    /// <summary>A new box takes the carry over (only a fresh one); false when there is none or it is too old.</summary>
    public bool TryTake(long now, out int caret, out bool tookKeyboard, out IDisposable? hold) => TryTake(now, true, out caret, out tookKeyboard, out hold);

    /// <summary>
    /// As above, but a carry is only continued while the island still has the keyboard: when it was lost while the carry was parked
    /// (another app took the foreground) nobody was told, so the parked hold is released here and the new box starts without the keyboard.
    /// </summary>
    public bool TryTake(long now, bool keyboardActive, out int caret, out bool tookKeyboard, out IDisposable? hold)
    {
        caret = Caret;
        tookKeyboard = TookKeyboard;
        hold = Hold;
        if (!Parked || now - Tick >= TakeWithinMs)
        {
            return false;
        }

        Clear();
        if (!keyboardActive)
        {
            Hold?.Dispose();
            hold?.Dispose();
            hold = null;
            tookKeyboard = false;
            return false;
        }

        return true;
    }

    /// <summary>The expiry check: when the carry parked as <paramref name="serial"/> is still unclaimed it is handed back to be released.</summary>
    public bool TryExpire(long serial, out bool tookKeyboard, out IDisposable? hold)
    {
        tookKeyboard = TookKeyboard;
        hold = Hold;
        if (!Parked || serial != _serial)
        {
            return false;
        }

        Clear();
        return true;
    }

    private void Clear()
    {
        Parked = false;
        Hold = null;
        TookKeyboard = false;
        Caret = 0;
    }

    /// <summary>A gesture asks the next box to take the keyboard (the page is about to be rebuilt around it).</summary>
    public void RequestFocus(long now) => _requestAt = now;

    public bool ConsumeFocusRequest(long now)
    {
        var fresh = _requestAt != long.MinValue && now - _requestAt < RequestWithinMs;
        _requestAt = long.MinValue;
        return fresh;
    }
}

/// <summary>What the Library remembers while the Music page is rebuilt: the scope chip, an open album / artist, where Esc goes back to.</summary>
internal static class LibraryNav
{
    public static LibScope Scope { get; set; }

    /// <summary>An open album or artist (a playlist is kept in <see cref="LibraryUi.PlaylistPid"/>).</summary>
    public static LibContainer? Container { get; set; }

    public static string ReturnText { get; set; } = "";

    public static LibScope ReturnScope { get; set; }

    public static readonly LibraryFocusCarry Focus = new();

    public static long Now => Environment.TickCount64;
}

// ==================================================================== virtual list

/// <summary>A panel of rows of known heights that only holds the rows in view (a library has a thousand of them).</summary>
internal sealed class RowsPanel : Panel
{
    public static readonly DependencyProperty IndexProperty = DependencyProperty.RegisterAttached("Index", typeof(int), typeof(RowsPanel), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    private readonly double[] _tops;

    /// <summary><paramref name="tops"/> is the prefix sum of the row heights: row i spans tops[i] to tops[i + 1].</summary>
    public RowsPanel(double[] tops) => _tops = tops;

    public int Count => _tops.Length - 1;

    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? 0 : available.Width;
        foreach (UIElement child in InternalChildren)
        {
            var i = (int)child.GetValue(IndexProperty);
            child.Measure(new Size(width, i >= 0 && i < Count ? _tops[i + 1] - _tops[i] : 0));
        }

        return new Size(width, _tops[^1]);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in InternalChildren)
        {
            var i = (int)child.GetValue(IndexProperty);
            if (i >= 0 && i < Count)
            {
                child.Arrange(new Rect(0, _tops[i], finalSize.Width, _tops[i + 1] - _tops[i]));
            }
        }

        return finalSize;
    }
}

/// <summary>Scrolling list with row virtualisation for rows of known heights (a uniform height, or a header taller or shorter than a song).</summary>
internal sealed class VirtualList : Grid
{
    private readonly Func<int, FrameworkElement> _make;
    private readonly ScrollViewer _scroll;
    private readonly RowsPanel _panel;
    private readonly double[] _tops;
    private readonly Dictionary<int, FrameworkElement> _live = [];

    public VirtualList(double rowHeight, int count, Func<int, FrameworkElement> make)
        : this(UniformTops(rowHeight, count), make)
    {
    }

    public VirtualList(IReadOnlyList<double> tops, Func<int, FrameworkElement> make)
    {
        _make = make;
        _tops = [.. tops];
        _panel = new RowsPanel(_tops);
        _scroll = PageUi.VScroll(_panel);
        _scroll.ScrollChanged += (_, _) => Realize();
        SizeChanged += (_, _) => Realize();
        Children.Add(_scroll);
        Loaded += (_, _) => Realize();
    }

    private static double[] UniformTops(double rowHeight, int count)
    {
        var tops = new double[count + 1];
        for (var i = 1; i <= count; i++)
        {
            tops[i] = i * rowHeight;
        }

        return tops;
    }

    public event Action<double>? Scrolled;

    /// <summary>Whether the row is built right now (the harness checks the virtualisation).</summary>
    internal bool IsLive(int index) => _live.ContainsKey(index);

    internal int LiveCount => _live.Count;

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

    /// <summary>Scrolls just enough to show row <paramref name="index"/> and the <paramref name="lead"/> rows right above it.</summary>
    public void ScrollIntoView(int index, int lead = 0)
    {
        if (index < 0 || index >= _panel.Count)
        {
            return;
        }

        var top = _tops[Math.Max(0, index - lead)];
        var bottom = _tops[index + 1];
        if (top < _scroll.VerticalOffset)
        {
            Trayloom.Core.Ui.SmoothScroll.ScrollTo(_scroll, top);
        }
        else if (bottom > _scroll.VerticalOffset + _scroll.ViewportHeight)
        {
            Trayloom.Core.Ui.SmoothScroll.ScrollTo(_scroll, bottom - _scroll.ViewportHeight);
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

    /// <summary>The first row whose bottom is below <paramref name="y"/> (binary search over the prefix sums).</summary>
    private int IndexAt(double y)
    {
        var lo = 0;
        var hi = _panel.Count - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (_tops[mid] <= y)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return lo;
    }

    private void Realize()
    {
        Scrolled?.Invoke(_scroll.VerticalOffset);
        var height = _scroll.ViewportHeight > 0 ? _scroll.ViewportHeight : ActualHeight;
        if (height <= 0 || _panel.Count == 0)
        {
            return;
        }

        var first = Math.Max(0, IndexAt(_scroll.VerticalOffset) - 2);
        var last = Math.Min(_panel.Count - 1, IndexAt(_scroll.VerticalOffset + height) + 2);
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

// ==================================================================== the panel

/// <summary>
/// The Apple Music library browser inside the music page: Playlists · Songs · Recently added, and a search box that searches everything
/// (songs, albums, artists, playlists; scope chips; Enter plays the highlighted result). Playing goes through <see cref="AppleMusicService"/>
/// (the Apple Music window is never shown).
/// </summary>
internal sealed class LibraryPanel : Grid
{
    private const double TileSize = 32;
    private static bool s_loggedKeyboard;

    private readonly IslandCore _core;
    private readonly AppleMusicService _svc;
    private readonly IslandWindow? _window;
    private readonly Border _headerHost = new() { Height = 28 };
    private readonly Border _searchBox;
    private readonly TextBox _search = PageUi.FlatTextBox(12);
    private readonly TextBlock _hint;
    private readonly HoverButton _clear;
    private readonly Grid _body = new();
    private readonly Border _contentHost = new();
    private readonly Border _noteHost = new() { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false, Margin = new Thickness(0, 0, 0, 6) };
    private readonly DispatcherTimer _debounce = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly Dictionary<ulong, ImageSource?> _art = [];
    private readonly HashSet<ulong> _artPending = [];
    private VirtualList? _list;
    private LibrarySearchModel? _model;
    private LibContainer? _container;
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

    public LibraryPanel(IslandCore core, IslandWindow? window)
    {
        _core = core;
        _svc = core.AppleMusic;
        _window = window;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Children.Add(_headerHost);

        // search field
        _search.Text = _svc.Ui.Search;
        AutomationProperties.SetAutomationId(_search, "Island.Music.Search");
        AutomationProperties.SetName(_search, LibStrings.SearchTip);
        _hint = D.T(LibStrings.SearchHint, 12, null, D.White(0.35), false, TextTrimming.CharacterEllipsis);
        _hint.IsHitTestVisible = false;
        _hint.VerticalAlignment = VerticalAlignment.Center;
        _hint.Margin = new Thickness(1, 0, 0, 0);
        var holder = new Grid { VerticalAlignment = VerticalAlignment.Center };
        holder.Children.Add(_hint);
        holder.Children.Add(_search);
        _clear = D.IconButton(G.Close, Strings.Clear, ClearSearch, size: 20, glyphSize: 8);
        _clear.CornerRadius = new CornerRadius(10);
        AutomationProperties.SetAutomationId(_clear, "Island.Music.SearchClear");
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
        _search.TextChanged += (_, _) =>
        {
            _svc.Ui.Search = _search.Text;
            SyncBox();
            if (_quiet)
            {
                return;
            }

            if (_search.Text.Length == 0)
            {
                ApplyQuery();
            }
            else
            {
                _debounce.Stop();
                _debounce.Start();
            }
        };
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
            _svc.EnsureLoaded();
            if (!_built)
            {
                Rebuild();
            }

            if (_search.Text != _svc.Ui.Search)
            {
                // keys typed into the box of the page this one replaces
                _search.Text = _svc.Ui.Search;
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

    private AmPlaylist? Playlist => _svc.Ui.PlaylistPid is { } pid ? _svc.Db?.Playlists.FirstOrDefault(p => p.Pid == pid) : null;

    private void SyncBox()
    {
        _clear.Visibility = _search.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _hint.Visibility = _search.Text.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
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

    // ---------------------------------------------------------------- keyboard

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

        if (_svc.State != LibraryState.Ready)
        {
            _pendingFocus = _svc.State is LibraryState.Loading or LibraryState.Idle;
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

    /// <summary>Types into the box (letters typed while the Library is open, or Space with text in it).</summary>
    public bool TypeInto(string text)
    {
        if (_parked || _window is null || _svc.State != LibraryState.Ready || _searchBox.Visibility != Visibility.Visible)
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

        if (_svc.State is LibraryState.Missing or LibraryState.Unreadable)
        {
            _pendingFocus = false;
            return;
        }

        if (_svc.State == LibraryState.Ready)
        {
            _pendingFocus = false;
            FocusSearch();
        }
    }

    private void ResumeFocus()
    {
        if (_svc.State != LibraryState.Ready)
        {
            return;
        }

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

    /// <summary>Gives the keyboard back now (the Library is closed, or the page is left).</summary>
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
            // the island lost the foreground (Apple Music at work, another app): the window decides, it tells us KeyboardLost / Regained
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
                _core.Ctx.Log.Info("[island] Library search could not take the keyboard (the island is not the foreground window)");
            }

            ShowLocalNote(LibStrings.ClickToType);
        };
        timer.Start();
    }

    private void ShowLocalNote(string text)
    {
        _localNote = text;
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

    private void ClearSearch() => _search.Text = "";

    private void OnSearchKey(object sender, KeyEventArgs e)
    {
        if (_svc.State != LibraryState.Ready)
        {
            return;
        }

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
                MoveHighlight(LibrarySearchModel.PageStep);
                e.Handled = true;
                break;
            case Key.PageUp:
                MoveHighlight(-LibrarySearchModel.PageStep);
                e.Handled = true;
                break;
            case Key.Return:
                Flush();
                if (_model is not null)
                {
                    Execute(_model.Activate(shift, ctrl));
                }

                e.Handled = true;
                break;
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

        _list?.Refresh();
        _list?.ScrollIntoView(_model.Highlight, _model.LeadRows(_model.Highlight));
        AutomationProperties.SetItemStatus(_search, StatusText());
    }

    private string StatusText() => _model is { } m ? LibStrings.ResultCount(m.SelectableCount) : "";

    /// <summary>Esc: clear the text (the box keeps the keyboard), then leave the box, then the open list; false when nothing was open.</summary>
    public bool StepBack()
    {
        switch (LibrarySearchModel.Escape(_search.Text.Length > 0, _wantFocus || _search.IsKeyboardFocused, _container is not null))
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
            ApplyPendingFocus();
            if (_wantFocus)
            {
                ResumeFocus();
            }
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

    private void OpenPlaylist(AmPlaylist? playlist)
    {
        if (playlist is null)
        {
            CloseContainer();
            return;
        }

        Remember();
        LibraryNav.Container = null;
        _svc.Ui.PlaylistPid = playlist.Pid;
        SetText("");
        Rebuild();
    }

    private void OpenContainer(LibContainer? container)
    {
        if (container is null)
        {
            return;
        }

        if (container.Playlist is { } playlist)
        {
            OpenPlaylist(playlist);
            return;
        }

        Remember();
        _svc.Ui.PlaylistPid = null;
        LibraryNav.Container = container;
        SetText("");
        Rebuild();
    }

    /// <summary>Opening a list from search results remembers the search, so the back arrow returns to it.</summary>
    private void Remember()
    {
        if (_container is null && _search.Text.Length > 0)
        {
            LibraryNav.ReturnText = _search.Text;
            LibraryNav.ReturnScope = LibraryNav.Scope;
        }
    }

    private void CloseContainer()
    {
        _svc.Ui.PlaylistPid = null;
        LibraryNav.Container = null;
        var text = LibraryNav.ReturnText;
        LibraryNav.ReturnText = "";
        LibraryNav.Scope = text.Length > 0 ? LibraryNav.ReturnScope : LibScope.All;
        SetText(text);
        Rebuild();
    }

    private void SetTab(int tab)
    {
        if (_svc.Ui.Tab == tab && _svc.Ui.PlaylistPid is null && LibraryNav.Container is null)
        {
            return;
        }

        _svc.Ui.Tab = tab;
        _svc.Ui.PlaylistPid = null;
        LibraryNav.Container = null;
        Rebuild();
    }

    private void SetScope(LibScope scope)
    {
        if (LibraryNav.Scope == scope)
        {
            return;
        }

        LibraryNav.Scope = scope;
        Flush();
        RebuildBody(resetScroll: true);
        BuildHeader();
    }

    /// <summary>One saved scroll slot per place, and one for "searching" there (not one per distinct query, which would grow without bound).</summary>
    private string ScrollKey => $"{_svc.Ui.Tab}:{_svc.Ui.PlaylistPid}:{LibraryNav.Container?.Key}:{LibraryNav.Scope}:{(AmSearchIndex.Fold(_svc.Ui.Search).Length > 0 ? "q" : "")}";

    private void Rebuild()
    {
        _built = true;
        _dataStamp = DataStamp();
        RebuildBody(resetScroll: false);
        BuildHeader();
    }

    /// <summary>The (debounced) text changed: the results are rebuilt from the top.</summary>
    private void ApplyQuery()
    {
        _debounce.Stop();
        if (!IsLoaded || _svc.State != LibraryState.Ready)
        {
            return;
        }

        if (_search.Text.Length == 0 && _container is null)
        {
            LibraryNav.Scope = LibScope.All;
        }

        RebuildBody(resetScroll: true);
        BuildHeader();
    }

    private LibContainer? CurrentContainer()
    {
        var db = _svc.Db;
        if (db is null)
        {
            return null;
        }

        if (Playlist is { } pl)
        {
            return LibrarySearchModel.ForPlaylist(pl);
        }

        if (LibraryNav.Container is { } open)
        {
            return open.Rebind(db);
        }

        return null;
    }

    private string HintText() => _container?.Kind switch
    {
        LibContainerKind.Playlist => LibStrings.SearchInPlaylist,
        LibContainerKind.Album => LibStrings.SearchInAlbum,
        LibContainerKind.Artist => LibStrings.SearchInArtist,
        _ => LibStrings.SearchHint,
    };

    private void BuildHeader()
    {
        var ready = _svc.State == LibraryState.Ready;
        _searchBox.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        _headerHost.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        if (!ready)
        {
            return;
        }

        _hint.Text = HintText();
        _headerHost.Child = _container is { } c ? ContainerHeader(c) : (_model is { Searching: true } ? Chips() : Tabs());
    }

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

    private FrameworkElement Plate(IReadOnlyList<HoverButton> segments)
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

    private FrameworkElement Tabs()
    {
        string[] names = [LibStrings.Playlists, LibStrings.Songs, LibStrings.Recent];
        string[] ids = ["Playlists", "Songs", "Recent"];
        var segments = new List<HoverButton>();
        for (var i = 0; i < 3; i++)
        {
            var tab = i;
            segments.Add(Segment(names[i], _svc.Ui.Tab == i, false, () => SetTab(tab), "Island.Music.Tab." + ids[i]));
        }

        return Plate(segments);
    }

    /// <summary>While searching the tab plate becomes the scope chips.</summary>
    private FrameworkElement Chips()
    {
        LibScope[] scopes = [LibScope.All, LibScope.Songs, LibScope.Albums, LibScope.Artists, LibScope.Playlists];
        string[] names = [LibStrings.ScopeAll, LibStrings.Songs, LibStrings.Albums, LibStrings.Artists, LibStrings.Playlists];
        var segments = new List<HoverButton>();
        for (var i = 0; i < scopes.Length; i++)
        {
            var scope = scopes[i];
            var dim = scope != LibScope.All && (_model?.ScopeTotal(scope) ?? 0) == 0;
            segments.Add(Segment(names[i], _model?.Scope == scope, dim, () => SetScope(scope), "Island.Music.Scope." + scope));
        }

        return Plate(segments);
    }

    private FrameworkElement ContainerHeader(LibContainer container)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var back = D.IconButton(G.ChevronLeft, Strings.Back, CloseContainer, size: 28, glyphSize: 12);
        AutomationProperties.SetAutomationId(back, "Island.Music.SearchBack");
        grid.Children.Add(back);
        var title = D.T(container.Name, 13, FontWeights.SemiBold, D.Text, false, TextTrimming.CharacterEllipsis);
        title.VerticalAlignment = VerticalAlignment.Center;
        title.Margin = new Thickness(4, 0, 8, 0);
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);
        if (container.Playlist is { } pl)
        {
            var busy = _svc.BusyPid == pl.Pid;
            var play = ActionPill(G.Play, LibStrings.Play, () => _svc.PlayPlaylist(pl, false), busy && !_svc.BusyShuffle);
            Grid.SetColumn(play, 2);
            grid.Children.Add(play);
            var shuffle = ActionPill(G.Shuffle, Strings.Shuffle, () => _svc.PlayPlaylist(pl, true), busy && _svc.BusyShuffle);
            shuffle.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(shuffle, 3);
            grid.Children.Add(shuffle);
        }

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
        if (_model is null || !ReferenceEquals(_model.Db, db))
        {
            // a reloaded library: the highlight stays on the same item when it is still listed
            var kept = _model?.HighlightId ?? "";
            _model = new LibrarySearchModel(db);
            _model.Keep(kept);
        }

        _container = CurrentContainer();
        if (_container is null && (LibraryNav.Container is not null || _svc.Ui.PlaylistPid is not null))
        {
            // the album / artist / playlist is gone from the library
            LibraryNav.Container = null;
            _svc.Ui.PlaylistPid = null;
        }

        _model.Update(_search.Text, _svc.Ui.Tab, LibraryNav.Scope, _container);
        AutomationProperties.SetItemStatus(_search, StatusText());
        if (_model.Rows.Count == 0)
        {
            string message;
            string glyph = G.Music;
            if (_model.Searching)
            {
                message = LibStrings.NoResultsFor(_search.Text.Trim());
                glyph = G.Search;
            }
            else if (_container is not null)
            {
                message = LibStrings.NoSongs;
            }
            else
            {
                message = _svc.Ui.Tab == 0 ? LibStrings.NoPlaylists : (_svc.Ui.Tab == 2 ? LibStrings.NoRecent : LibStrings.NoSongs);
            }

            var empty = D.EmptyView(glyph, message);
            empty.Margin = new Thickness(0, 0, 0, 24);
            return empty;
        }

        var key = ScrollKey;
        var list = new VirtualList(_model.Tops, Row);
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
        var model = _model!;
        var row = model.Rows[index];
        var highlighted = model.Highlight == index;
        FrameworkElement element = row.Kind switch
        {
            LibRowKind.Header => HeaderRow(row),
            LibRowKind.Song => TrackRow(row, index, highlighted),
            LibRowKind.Album => AlbumRow(row, index, highlighted),
            LibRowKind.Artist => ArtistRow(row, index, highlighted),
            _ => PlaylistRow(row, index, highlighted),
        };
        if (row.Selectable)
        {
            AutomationProperties.SetAutomationId(element, "Island.Music.Result." + index);
        }

        return element;
    }

    private FrameworkElement HeaderRow(LibRow header)
    {
        var grid = new Grid { Margin = new Thickness(6, 4, 4, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = D.T(header.Title, 11, FontWeights.SemiBold, D.Secondary, false, TextTrimming.CharacterEllipsis);
        title.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(title);
        if (header.Total > header.Shown)
        {
            var more = new HoverButton(() => Execute(LibrarySearchModel.ShowAllOf(header))) { Height = 20, Padding = new Thickness(8, 0, 8, 0), CornerRadius = new CornerRadius(10), Lifts = false, VerticalAlignment = VerticalAlignment.Center };
            more.Child = D.T(LibStrings.ShowAll(header.Total), 11, FontWeights.Medium, D.White(0.6));
            ((TextBlock)more.Child).VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetAutomationId(more, "Island.Music.ShowAll." + header.Target);
            AutomationProperties.SetName(more, LibStrings.ShowAll(header.Total));
            Grid.SetColumn(more, 1);
            grid.Children.Add(more);
        }

        return grid;
    }

    private HoverButton RowButton(int index, bool highlighted, bool current, string name)
    {
        var action = _model!.Rows[index];
        var button = new HoverButton(() =>
        {
            _model?.SetHighlight(index);
            Execute(LibrarySearchModel.ActionFor(action));
        })
        {
            Margin = new Thickness(0, 1, 0, 1),
            CornerRadius = new CornerRadius(10),
            Lifts = false,
            Fill = highlighted ? D.White(0.13) : (current ? D.White(0.08) : null),
        };
        AutomationProperties.SetName(button, name);
        return button;
    }

    /// <summary>The picture tile of a row: the track's artwork when Apple Music cached one (decoded off the UI thread), else a glyph.</summary>
    private FrameworkElement Tile(AmTrack? art, string glyph, bool round, bool current, bool busy)
    {
        var radius = round ? TileSize / 2 : 7;
        var tile = new Grid { Width = TileSize, Height = TileSize, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var plate = new Border { CornerRadius = new CornerRadius(radius), Background = D.White(0.08), Child = D.I(glyph, 12, D.White(0.45)) };
        tile.Children.Add(plate);
        if (art is not null)
        {
            LoadArt(art.Pid, plate, radius);
        }

        if (busy || current)
        {
            FrameworkElement inner = busy ? PageUi.Spinner(13) : new EqBars(3, 2.4, 12, D.Text) { Playing = _core.Playback.Current.IsPlaying };
            inner.HorizontalAlignment = HorizontalAlignment.Center;
            inner.VerticalAlignment = VerticalAlignment.Center;
            tile.Children.Add(new Border { CornerRadius = new CornerRadius(radius), Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)), Child = inner });
        }

        return tile;
    }

    private void LoadArt(ulong pid, Border plate, double radius)
    {
        if (_art.TryGetValue(pid, out var known))
        {
            if (known is not null)
            {
                SetArt(plate, known, radius);
            }

            return;
        }

        if (_svc.Db is not { } db || !_artPending.Add(pid))
        {
            return;
        }

        Guarded.FireAndLog(async () =>
        {
            ImageSource? image = null;
            try
            {
                image = await AmArtwork.GetAsync(db, pid);
            }
            finally
            {
                // whatever happened, the picture is not pending any more (a failure is remembered as "no picture", not retried per rebuild)
                _artPending.Remove(pid);
                _art[pid] = image;
            }

            if (image is not null)
            {
                SetArt(plate, image, radius);
            }
        }, _core.Ctx.Log, "Library artwork");
    }

    private static void SetArt(Border plate, ImageSource image, double radius)
    {
        var picture = new Image { Source = image, Stretch = Stretch.UniformToFill, Width = TileSize, Height = TileSize, Clip = new RectangleGeometry(new Rect(0, 0, TileSize, TileSize), radius, radius) };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
        plate.Child = picture;
    }

    private FrameworkElement TrackRow(LibRow data, int index, bool highlighted)
    {
        var track = data.Track!;
        var current = _svc.IsCurrent(track);
        var busy = _svc.BusyPid == track.Pid;
        var context = data.Context;
        var subtitle = data.Via.Length > 0 ? track.Subtitle + " · " + LibStrings.InPlaylist(data.Via) : track.Subtitle;
        var title = string.IsNullOrWhiteSpace(track.Title) ? "—" : track.Title;
        var row = RowButton(index, highlighted, current, title + ", " + subtitle);
        var playable = LibrarySearchModel.CanPlay(track, context);
        var grid = new Grid { Margin = new Thickness(6, 0, 4, 0) };
        if (!playable)
        {
            // listed so the result is not silently missing, but dimmed and not playable (it is in no list Apple Music shows)
            grid.Opacity = 0.45;
            D.Tip(row, LibStrings.NotPlayable);
        }

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TileSize + 8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(Tile(track, G.Music, false, current, busy));
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        texts.Children.Add(D.T(title, 13, FontWeights.SemiBold, D.Text, false, TextTrimming.CharacterEllipsis));
        texts.Children.Add(D.T(subtitle, 11, null, D.Secondary, false, TextTrimming.CharacterEllipsis));
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        var time = D.Digits(track.Duration, 11, null, D.Tertiary);
        time.VerticalAlignment = VerticalAlignment.Center;
        time.Margin = new Thickness(0, 0, 4, 0);
        Grid.SetColumn(time, 2);
        grid.Children.Add(time);
        HoverButton? more = null;
        more = new HoverButton(() => IslandMenu.Show(more!, TrackMenu(track, context), () => _window?.HoldOpen())) { Width = 26, Height = 26, CornerRadius = new CornerRadius(8), Lifts = false, VerticalAlignment = VerticalAlignment.Center };
        var dots = D.I(G.More, 12, D.White(0.6));
        more.Child = dots;
        more.Icon = dots;
        D.Tip(more, LibStrings.More);
        Grid.SetColumn(more, 3);
        grid.Children.Add(more);
        row.Child = grid;
        return row;
    }

    private IEnumerable<IslandMenu.Item> TrackMenu(AmTrack track, AmPlaylist? context)
    {
        var playable = LibrarySearchModel.CanPlay(track, context);
        yield return new IslandMenu.Item(LibStrings.Play, () => _svc.PlaySong(track, context, "songs"), Enabled: playable, Glyph: G.Play);
        yield return new IslandMenu.Item(LibStrings.PlayNext, () => _svc.PlaySong(track, context, "songs", AmAction.PlayNext), Enabled: playable, Glyph: G.Next);
        yield return new IslandMenu.Item(LibStrings.AddToQueue, () => _svc.PlaySong(track, context, "songs", AmAction.AddToQueue), Enabled: playable, Glyph: G.List);
        yield return IslandMenu.Sep();
        yield return new IslandMenu.Item(LibStrings.GoToAlbum, () => OpenContainer(_model?.AlbumOf(track) is { } album ? LibrarySearchModel.ForAlbum(album) : null), Enabled: !string.IsNullOrWhiteSpace(track.Album));
        yield return new IslandMenu.Item(LibStrings.GoToArtist, () => OpenContainer(_model?.ArtistOf(track) is { } artist ? LibrarySearchModel.ForArtist(artist) : null), Enabled: !string.IsNullOrWhiteSpace(track.Artist));
    }

    private FrameworkElement AlbumRow(LibRow data, int index, bool highlighted)
    {
        var album = data.Album!;
        var subtitle = (album.Artist.Length > 0 ? album.Artist + " · " : "") + LibStrings.Count(album.Tracks.Count);
        return NamedRow(data, index, highlighted, album.Name, subtitle, album.Tracks.Count > 0 ? album.Tracks[0] : null, G.MusicLibrary, false);
    }

    private FrameworkElement ArtistRow(LibRow data, int index, bool highlighted)
    {
        var artist = data.Artist!;
        return NamedRow(data, index, highlighted, artist.Name, LibStrings.Count(artist.Tracks.Count), artist.Tracks.Count > 0 ? artist.Tracks[0] : null, "", true);
    }

    private FrameworkElement PlaylistRow(LibRow data, int index, bool highlighted)
    {
        var pl = data.Playlist!;
        return NamedRow(data, index, highlighted, pl.Name, LibStrings.Count(pl.Tracks.Count), null, G.List, false);
    }

    /// <summary>An album, artist or playlist line: tile, name, count, chevron.</summary>
    private FrameworkElement NamedRow(LibRow data, int index, bool highlighted, string name, string subtitle, AmTrack? art, string glyph, bool round)
    {
        var row = RowButton(index, highlighted, false, name + ", " + subtitle);
        var grid = new Grid { Margin = new Thickness(6, 0, 8, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TileSize + 8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(Tile(art, glyph, round, false, false));
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        texts.Children.Add(D.T(name, 13, FontWeights.SemiBold, D.Text, false, TextTrimming.CharacterEllipsis));
        texts.Children.Add(D.T(subtitle, 11, null, D.Secondary, false, TextTrimming.CharacterEllipsis));
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        var chevron = D.I(G.ChevronRight, 10, D.White(0.4));
        chevron.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(chevron, 2);
        grid.Children.Add(chevron);
        row.Child = grid;
        return row;
    }

    private void Execute(LibAction action)
    {
        switch (action.Kind)
        {
            case LibActionKind.Play:
                _svc.PlaySong(action.Track!, action.Context, action.Page);
                break;
            case LibActionKind.PlayNext:
                _svc.PlaySong(action.Track!, action.Context, action.Page, AmAction.PlayNext);
                break;
            case LibActionKind.AddToQueue:
                _svc.PlaySong(action.Track!, action.Context, action.Page, AmAction.AddToQueue);
                break;
            case LibActionKind.OpenAlbum:
                OpenContainer(LibrarySearchModel.ForAlbum(action.Album!));
                break;
            case LibActionKind.OpenArtist:
                OpenContainer(LibrarySearchModel.ForArtist(action.Artist!));
                break;
            case LibActionKind.OpenPlaylist:
                OpenPlaylist(action.Playlist);
                break;
            case LibActionKind.ShowAll:
                SetScope(action.Scope);
                break;
        }
    }

    private void BuildNote()
    {
        var note = _localNote is not null ? new LibraryNote(_localNote, true) : _svc.Note;
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

            // Reading the queue drives Apple Music through UI Automation: only when there is nothing to show yet or the song has changed
            // (and the last read is not fresh), never on every rebuild of the page. The refresh button reads it whenever asked.
            _svc.RefreshQueueIfStale();
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
