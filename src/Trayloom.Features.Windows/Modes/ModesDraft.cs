// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace Trayloom.Features.WindowTools.Modes;

/// <summary>Why a draft cannot be saved yet.</summary>
internal enum DraftProblem
{
    None,
    NameMissing,
    NameTaken,
    LimitReached,
}

/// <summary>What adding items to a draft did, so the form can say what was left out.</summary>
/// <param name="Added">Items that are in the draft now and were not before.</param>
/// <param name="HitLimit">True when at least one item was left out because the mode already has twenty.</param>
internal readonly record struct DraftAdd(int Added, bool HitLimit);

/// <summary>
/// The mode the island form is editing: a plain value that outlives the form's views. The island page is rebuilt whenever a song
/// changes, so the name, colour and items are kept here (and not in the text boxes) and the new page shows the same form again. It holds
/// no window, brush or handle, so the console harness can run all of its rules. Nothing reaches <see cref="ModesRuntime"/> until Save.
/// </summary>
internal sealed class ModesDraft
{
    private ModesDraft(string? editId, Mode mode)
    {
        EditId = editId;
        Mode = mode;
    }

    /// <summary>The id of the stored mode being edited, or null for a new mode.</summary>
    public string? EditId { get; }

    public bool IsNew => EditId is null;

    /// <summary>The working copy. Its name is kept as typed and trimmed by <see cref="ToMode"/>.</summary>
    public Mode Mode { get; }

    /// <summary>Browser (<c>default</c> or an executable path) and Chromium profile used for the next web items.</summary>
    public string BrowserId { get; set; } = "default";

    public string Profile { get; set; } = string.Empty;

    /// <summary>The inline panel that is open under the add buttons: <c>app</c>, <c>web</c> or null.</summary>
    public string? Panel { get; set; }

    /// <summary>Whether "More options" is expanded.</summary>
    public bool MoreOptions { get; set; }

    /// <summary>The addresses typed or pasted into the web box and not added yet, so a rebuilt page does not lose them.</summary>
    public string WebText { get; set; } = string.Empty;

    /// <summary>True while "Use what’s open now" is reading; the line the form shows about it (reading, nothing found, failed) or null.</summary>
    public bool Capturing { get; set; }

    public string? CaptureNote { get; set; }

    /// <summary>Raised (UI thread) when a read finished, so the form that is on screen now shows the result even if the page was rebuilt meanwhile.</summary>
    public event Action? CaptureChanged;

    public void RaiseCaptureChanged() => CaptureChanged?.Invoke();

    /// <summary>What "Use what’s open now" found and the entries the person unticked; null until it was asked for.</summary>
    public ModeCapture? Captured { get; private set; }

    public HashSet<int> Unticked { get; } = [];

    /// <summary>A new mode with the first colour and icon.</summary>
    public static ModesDraft ForNew() =>
        new(null, new Mode { Color = ModesMath.Colors[0], Glyph = ModesMath.Glyphs[0], OnLeave = "minimize" });

    /// <summary>A draft that edits a copy of <paramref name="existing"/>.</summary>
    public static ModesDraft ForEdit(Mode existing) => new(existing.Id, existing.Clone());

    // ------------------------------------------------------------------------------------------ validation

    /// <summary>
    /// The first thing that stops Save, matching the Settings editor: a name is required and no other mode may use it (ignoring case),
    /// and a new mode needs room among the twelve.
    /// </summary>
    public DraftProblem Problem(IEnumerable<Mode> stored, bool canAdd)
    {
        var name = Mode.Name.Trim();
        if (name.Length == 0)
        {
            return DraftProblem.NameMissing;
        }

        if (stored.Any(m => m.Id != EditId && string.Equals(m.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)))
        {
            return DraftProblem.NameTaken;
        }

        return IsNew && !canAdd ? DraftProblem.LimitReached : DraftProblem.None;
    }

    // ------------------------------------------------------------------------------------------ items

    /// <summary>Adds items at the end (at most twenty in all), gives them ids and drops repeats of what the mode already opens.</summary>
    public DraftAdd AddItems(IEnumerable<ModeItem> items)
    {
        var before = Mode.Items.Count;
        var hitLimit = false;
        foreach (var item in items)
        {
            if (Mode.Items.Count >= ModesMath.MaxItems)
            {
                hitLimit = true;
                break;
            }

            item.Id = ModesMath.NewItemId(Mode);
            Mode.Items.Add(item);
        }

        // A repeat is not an error: the second copy simply never joins the list.
        Mode.Items = ModesMath.Dedupe(Mode.Items);
        return new DraftAdd(Math.Max(0, Mode.Items.Count - before), hitLimit);
    }

    /// <summary>Removes the item with this id; true when there was one.</summary>
    public bool Remove(string itemId) => Mode.Items.RemoveAll(i => i.Id == itemId) > 0;

    /// <summary>True while one more item fits.</summary>
    public bool CanAddItem => Mode.Items.Count < ModesMath.MaxItems;

    /// <summary>Exactly one item is brought to the front: the one marked, else the first.</summary>
    public void EnsureFront()
    {
        if (Mode.Items.Count == 0)
        {
            return;
        }

        var marked = Mode.Items.FirstOrDefault(i => i.Front) ?? Mode.Items[0];
        foreach (var item in Mode.Items)
        {
            item.Front = ReferenceEquals(item, marked);
        }
    }

    // ------------------------------------------------------------------------------------------ web pages

    /// <summary>One web item (one browser window) in the browser and profile the draft has chosen.</summary>
    public ModeItem WebItem(string name, IReadOnlyList<string> urls) => new()
    {
        Kind = ModeItemKind.Web,
        Name = name,
        Browser = string.IsNullOrWhiteSpace(BrowserId) ? "default" : BrowserId,
        Profile = Profile,
        Urls = [.. urls],
    };

    /// <summary>
    /// Turns pasted text (addresses separated by spaces or line breaks) into one web item. Null when nothing in it is a web page;
    /// <paramref name="rejected"/> counts the entries that were not.
    /// </summary>
    public ModeItem? PastedWebItem(string? text, out int rejected)
    {
        var urls = ModesMath.ParseUrlList(text, out rejected);
        if (urls.Count == 0)
        {
            return null;
        }

        var host = HostOf(urls[0]);
        return WebItem(urls.Count > 1 ? ModesUiStrings.AndMore(host, urls.Count - 1) : host, urls);
    }

    /// <summary>Web items for browser windows read from a session file: one item per window, split when a window has more than fifty tabs.</summary>
    public List<ModeItem> TabWindowItems(string browserName, IReadOnlyList<(int Number, IReadOnlyList<string> Urls)> windows)
    {
        var items = new List<ModeItem>();
        foreach (var (number, urls) in windows)
        {
            var clean = urls.Select(ModesMath.NormalizeUrl).OfType<string>().ToList();
            foreach (var group in ModesMath.GroupUrls(clean))
            {
                items.Add(WebItem(ModesStrings.BrowserWindow(browserName, number), group));
            }
        }

        return items;
    }

    /// <summary>The host of an address without a leading <c>www.</c>, or the text itself when it is not an address.</summary>
    public static string HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? (uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host) : url;

    // ------------------------------------------------------------------------------------------ what is open now

    /// <summary>Keeps what "Use what’s open now" found; everything starts ticked as the capture says.</summary>
    public void SetCapture(ModeCapture capture)
    {
        Captured = capture;
        Unticked.Clear();
        for (var i = 0; i < capture.Items.Count; i++)
        {
            if (!capture.Items[i].Selected)
            {
                Unticked.Add(i);
            }
        }
    }

    /// <summary>Forgets the captured list (after its ticked items were added, or when the person closes it).</summary>
    public void ClearCapture()
    {
        Captured = null;
        Unticked.Clear();
    }

    /// <summary>How many captured entries are ticked.</summary>
    public int TickedCount => Captured is null ? 0 : Captured.Items.Count - Unticked.Count(i => i >= 0 && i < Captured.Items.Count);

    /// <summary>Ticks or unticks the captured entry at <paramref name="index"/>.</summary>
    public void Tick(int index, bool ticked)
    {
        if (ticked)
        {
            Unticked.Remove(index);
        }
        else
        {
            Unticked.Add(index);
        }
    }

    /// <summary>
    /// Adds the ticked captured entries (copies, so the capture stays untouched) and clears the capture. Nothing is added when none is
    /// ticked: the form says "Tick at least one thing" instead.
    /// </summary>
    public DraftAdd AddTicked()
    {
        if (Captured is null || TickedCount == 0)
        {
            return new DraftAdd(0, false);
        }

        var ticked = Captured.Items.Where((_, i) => !Unticked.Contains(i)).Select(c => c.Item.Clone());
        var result = AddItems(ticked);
        ClearCapture();
        return result;
    }

    // ------------------------------------------------------------------------------------------ result

    /// <summary>The mode to hand to <see cref="ModesRuntime.Add"/> or <see cref="ModesRuntime.Update"/>: a copy with the name trimmed and one front item.</summary>
    public Mode ToMode()
    {
        EnsureFront();
        var copy = Mode.Clone();
        copy.Name = Mode.Name.Trim();
        copy.Id = EditId ?? string.Empty;
        return copy;
    }
}
