// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;

namespace Trayloom.Features.Island;

/// <summary>What a line of the streaming list is.</summary>
internal enum StreamRowKind
{
    Header,
    Item,
    ShowMore,
}

/// <summary>One line of the list: a section header, a song / album / artist / playlist / video, or the "Show more" line at the end.</summary>
internal sealed record StreamRow(StreamRowKind Kind, string Id)
{
    /// <summary>The item of an <see cref="StreamRowKind.Item"/> row.</summary>
    public StreamItem? Item { get; init; }

    /// <summary>A header's title.</summary>
    public string Title { get; init; } = "";

    /// <summary>A header: how many results the section has in all.</summary>
    public int Total { get; init; }

    /// <summary>A header: how many of them are listed.</summary>
    public int Shown { get; init; }

    /// <summary>A header: the scope "Show all" switches to.</summary>
    public StreamScope Target { get; init; }

    /// <summary>The row's height in the list.</summary>
    public double Height => Kind == StreamRowKind.Header ? StreamListModel.HeaderHeight : StreamListModel.RowHeight;

    /// <summary>Headers cannot be highlighted.</summary>
    public bool Selectable => Kind != StreamRowKind.Header;
}

/// <summary>What pressing Enter on (or clicking) a row does.</summary>
internal enum StreamActionKind
{
    None,
    Play,
    Queue,
    Open,
    OpenContainer,
    ShowAll,
    ShowMore,
}

/// <summary>An action with what it acts on.</summary>
internal sealed record StreamAction(StreamActionKind Kind)
{
    /// <summary>The song, video or list the action is about.</summary>
    public StreamItem? Item { get; init; }

    /// <summary>For <see cref="StreamActionKind.ShowAll"/>: the scope to switch to.</summary>
    public StreamScope Scope { get; init; }
}

/// <summary>
/// The rows of the streaming list (grouped results with "Show all", a plain list, a list inside an album or playlist), the highlight and the
/// action of each row. No UI here: the harness tests it.
/// </summary>
internal sealed class StreamListModel
{
    public const double HeaderHeight = 24;
    public const double RowHeight = 44;
    public const int GroupCap = 4;
    public const int PageStep = 6;

    private List<StreamRow> _rows = [];
    private double[] _tops = [0];
    private int[] _selectable = [];
    private string _highlightId = "";

    /// <summary>The rows.</summary>
    public IReadOnlyList<StreamRow> Rows => _rows;

    /// <summary>Row heights as a prefix sum: row i spans Tops[i] to Tops[i + 1].</summary>
    public IReadOnlyList<double> Tops => _tops;

    /// <summary>The highlighted row (Enter acts on it); -1 for none.</summary>
    public int Highlight { get; private set; } = -1;

    /// <summary>The id of the highlighted row (kept across rebuilds).</summary>
    public string HighlightId => _highlightId;

    /// <summary>The number of rows that can be highlighted.</summary>
    public int SelectableCount => _selectable.Length;

    /// <summary>Whether Ctrl / Shift + Enter on a song adds it to the queue.</summary>
    public bool CanQueue { get; private set; }

    /// <summary>The next page starts here; null when the list is complete.</summary>
    public int? NextOffset { get; private set; }

    /// <summary>Seeds the highlight of a model that replaces an older one.</summary>
    public void Keep(string id) => _highlightId = id;

    /// <summary>Joins consecutive pages of one list: sections with the same scope and title are continued, the last page says whether more follows.</summary>
    public static StreamPage Merge(IReadOnlyList<StreamPage> pages)
    {
        if (pages.Count == 1)
        {
            return pages[0];
        }

        var sections = new List<StreamSection>();
        foreach (var page in pages)
        {
            foreach (var section in page.Sections)
            {
                var at = sections.FindIndex(s => s.Scope == section.Scope && s.Title == section.Title);
                if (at < 0)
                {
                    sections.Add(section);
                }
                else
                {
                    var items = new List<StreamItem>(sections[at].Items);
                    var seen = items.Select(i => i.Key).ToHashSet();
                    items.AddRange(section.Items.Where(i => seen.Add(i.Key)));
                    sections[at] = sections[at] with { Items = items, Total = Math.Max(sections[at].Total, section.Total) };
                }
            }
        }

        return new StreamPage(sections, pages.Count > 0 ? pages[^1].NextOffset : null);
    }

    /// <summary>
    /// Builds the rows. <paramref name="grouped"/> (an "All" search) gives each section a header and at most <see cref="GroupCap"/> rows
    /// with "Show all (N)" when there are more. <paramref name="filter"/> narrows a list that is open (an album, a playlist) by its text.
    /// The highlight stays on the same item when it is still listed; else a search highlights the top hit and browsing highlights nothing
    /// until an arrow key is pressed.
    /// </summary>
    public void Update(StreamPage? page, bool grouped, string filter, bool autoHighlight, bool canQueue)
    {
        var keep = _highlightId;
        CanQueue = canQueue;
        var rows = new List<StreamRow>();
        NextOffset = page?.NextOffset;
        filter = (filter ?? "").Trim();
        if (page is not null)
        {
            var headers = grouped || page.Sections.Count(s => s.Items.Count > 0) > 1;
            foreach (var section in page.Sections)
            {
                var items = filter.Length == 0 ? section.Items : section.Items.Where(i => Matches(i, filter)).ToList();
                if (items.Count == 0)
                {
                    continue;
                }

                var shown = grouped ? items.Take(GroupCap).ToList() : items;
                if (headers && section.Title.Length > 0)
                {
                    rows.Add(new StreamRow(StreamRowKind.Header, "h:" + section.Scope)
                    {
                        Title = section.Title,
                        Total = filter.Length > 0 ? items.Count : Math.Max(section.Total, items.Count),
                        Shown = shown.Count,
                        Target = section.Scope,
                    });
                }

                rows.AddRange(shown.Select(i => new StreamRow(StreamRowKind.Item, "i:" + i.Key) { Item = i }));
            }

            if (NextOffset is not null && !grouped)
            {
                rows.Add(new StreamRow(StreamRowKind.ShowMore, "more"));
            }
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
        if (at < 0 && autoHighlight && _selectable.Length > 0)
        {
            at = _selectable[0];
        }

        SetHighlightIndex(at);
    }

    /// <summary>Whether the text is in the item's title or subtitle (ignoring case and accents).</summary>
    public static bool Matches(StreamItem item, string text)
    {
        var compare = CultureInfo.CurrentCulture.CompareInfo;
        const CompareOptions Options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        return compare.IndexOf(item.Title, text, Options) >= 0 || compare.IndexOf(item.Subtitle, text, Options) >= 0;
    }

    private void SetHighlightIndex(int index)
    {
        Highlight = index >= 0 && index < _rows.Count ? index : -1;
        _highlightId = Highlight >= 0 ? _rows[Highlight].Id : "";
    }

    /// <summary>Highlights a row (a click on it).</summary>
    public void SetHighlight(int index) => SetHighlightIndex(index >= 0 && index < _rows.Count && _rows[index].Selectable ? index : -1);

    /// <summary>Moves the highlight by <paramref name="delta"/> rows (headers are skipped); false when it did not move.</summary>
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
    public int LeadRows(int index) => index > 0 && index < _rows.Count && _rows[index - 1].Kind == StreamRowKind.Header ? 1 : 0;

    /// <summary>The items listed, in order (the Liked Songs list passes them as the songs that follow the one played).</summary>
    public IReadOnlyList<StreamItem> Items() => [.. _rows.Where(r => r.Item is not null).Select(r => r.Item!)];

    // ---------------------------------------------------------------- actions

    /// <summary>What Enter does: play a song (Shift or Ctrl: add it to the queue), open a video or a list, show more.</summary>
    public StreamAction Activate(bool shift, bool ctrl) =>
        Highlight >= 0 && Highlight < _rows.Count ? ActionFor(_rows[Highlight], shift, ctrl, CanQueue) : new StreamAction(StreamActionKind.None);

    /// <summary>The action of a row.</summary>
    public static StreamAction ActionFor(StreamRow row, bool shift = false, bool ctrl = false, bool canQueue = false)
    {
        if (row.Kind == StreamRowKind.ShowMore)
        {
            return new StreamAction(StreamActionKind.ShowMore);
        }

        if (row.Item is not { } item)
        {
            return new StreamAction(StreamActionKind.None);
        }

        return item.Kind switch
        {
            StreamItemKind.Track when !item.Playable => new StreamAction(StreamActionKind.None),
            StreamItemKind.Track => new StreamAction((shift || ctrl) && canQueue ? StreamActionKind.Queue : StreamActionKind.Play) { Item = item },
            StreamItemKind.Video => new StreamAction(StreamActionKind.Open) { Item = item },
            _ => new StreamAction(StreamActionKind.OpenContainer) { Item = item },
        };
    }

    /// <summary>The "Show all (N)" of a section header.</summary>
    public static StreamAction ShowAllOf(StreamRow header) => new(StreamActionKind.ShowAll) { Scope = header.Target };
}
