// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Core.Native;

namespace Trayloom.Features.Tools;

/// <summary>Modes of the field (spec 1.4.4).</summary>
internal enum BarMode
{
    Search,
    Argument,
    Confirm,
    Actions,
    Naming,
    Capturing,
}

/// <summary>An item of the displayed list: a heading or a row.</summary>
internal sealed class BarListItem
{
    public string? Heading { get; init; }

    public BarEntry? Entry { get; init; }
}

/// <summary>The brain of the Command Bar: mode machine, search pipeline, run algorithm, learning (spec 1.4 - 1.7).</summary>
internal sealed class BarService
{
    private static readonly string[] Curated =
    [
        "action.screenshot", "action.scrollingScreenshot", "action.keepAwake", "action.screenOCR", "action.clipboardWindow", "action.colorPicker",
        "action.darkMode", "action.snippetLibrary",
    ];

    private readonly DispatcherTimer _scriptTimer;
    private readonly DispatcherTimer _fileTimer;
    private List<BarEntry> _catalog = [];
    private List<BarEntry> _apps = [];
    private List<BarEntry> _panes = [];
    private List<BarEntry> _windows = [];
    private List<BarEntry> _quit = [];
    private List<BarEntry> _menus = [];
    private List<BarEntry> _emoji = [];
    private List<BarEntry> _kill = [];
    private List<BarEntry> _selection = [];
    private List<BarEntry> _files = [];
    private string _fileQuery = "";
    private CancellationTokenSource? _fileCts;
    private readonly Dictionary<string, string> _scriptResults = [];
    private readonly HashSet<string> _scriptPending = [];
    private CancellationTokenSource? _scriptCts;
    private int _generation;
    private string _lastTrimmed = "";
    private string _savedQuery = "";
    private string _typedBeforeTab = "";
    private Dictionary<string, BarEntry> _byStableKey = [];

    public BarService(IContributionServices ctx)
    {
        Ctx = ctx;
        Ui = ctx.Ui;
        Prefs = new BarPreferences(ctx.App);
        Learning = new BarLearning(ctx.App);
        _scriptTimer = new DispatcherTimer(DispatcherPriority.Normal, Ui) { Interval = TimeSpan.FromMilliseconds(250) };
        _scriptTimer.Tick += (_, _) =>
        {
            _scriptTimer.Stop();
            StartScript(false);
        };
        BarProviders.AppsChanged += () => Ui.BeginInvoke(() =>
        {
            _apps = BarCatalog.AppEntries(this);
            Index();
            if (Visible)
            {
                Rebuild();
            }
        });
        _fileTimer = new DispatcherTimer(DispatcherPriority.Normal, Ui) { Interval = TimeSpan.FromMilliseconds(120) };
        _fileTimer.Tick += (_, _) =>
        {
            _fileTimer.Stop();
            StartFileSearch();
        };
    }

    public IContributionServices Ctx { get; }

    public Dispatcher Ui { get; }

    public BarPreferences Prefs { get; }

    public BarLearning Learning { get; }

    public BarMode Mode { get; private set; }

    public string Query { get; private set; } = "";

    public string? ActiveCategory { get; private set; }

    public bool Peek { get; private set; }

    public bool Compact => Prefs.CompactMode;

    public List<BarListItem> Items { get; private set; } = [];

    public List<BarEntry> Rows { get; private set; } = [];

    public int Selected { get; private set; }

    public BarEntry? ModeEntry { get; private set; }

    public List<BarAction> Actions { get; private set; } = [];

    public int ActionSelected { get; private set; }

    public string? Warning { get; private set; }

    public bool ShowedHome { get; private set; }

    public nint Target { get; set; }

    public string TargetAppName { get; set; } = "";

    public string? SelectionText { get; set; }

    public bool SelectionTypesOver { get; set; }

    public bool? WifiOn { get; private set; }

    public string QueryWhenRun { get; private set; } = "";

    public string? SelectionWhenRun { get; private set; }

    public bool Visible { get; set; }

    public string Placeholder => Mode switch
    {
        BarMode.Argument when ModeEntry?.NumericRange is { } r => Loc.F("{0} to {1}", r.Min, r.Max),
        BarMode.Naming => BarStrings.AliasPlaceholder,
        _ => BarStrings.SearchPlaceholder,
    };

    /// <summary>True when the panel is just the field (compact home).</summary>
    public bool IsCollapsed => Mode == BarMode.Search && Compact && Query.Length == 0 && ActiveCategory is null && !Peek;

    public event Action? Changed;

    /// <summary>The service replaced the text of the field (Tab, Esc, prefill).</summary>
    public event Action<string>? QueryReplaced;

    public event Action? HideRequested;

    public event Action<string>? FocusFieldRequested;

    public string[] ExampleChips
    {
        get
        {
            var list = new List<string> { "100 km to mi", "2+2*3", "brightness 40", "fire" };
            if (BarSystem.Battery() is not null)
            {
                list.Add("battery");
            }

            return [.. list];
        }
    }

    // ------------------------------------------------------------------ lifecycle

    /// <summary>Starts a presentation: resets state and loads everything in the background.</summary>
    public void Begin(nint target, string rememberedQuery = "")
    {
        _generation++;
        Target = target;
        TargetAppName = "";
        if (target != 0)
        {
            var exe = N.ProcessPath(Plat.ProcessOf(target));
            TargetAppName = exe is null ? "" : TryName(exe);
        }

        Mode = BarMode.Search;
        Query = "";
        ActiveCategory = null;
        Peek = false;
        ModeEntry = null;
        Warning = null;
        _files = [];
        _scriptResults.Clear();
        _scriptPending.Clear();
        _lastTrimmed = "";
        Selected = 0;
        Hydrate();
        Rebuild();
        var gen = _generation;
        Ui.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (gen != _generation)
            {
                return;
            }

            BarProviders.RescanApps(() => Ui.BeginInvoke(() =>
            {
                if (gen == _generation)
                {
                    _apps = BarCatalog.AppEntries(this);
                    Index();
                    Rebuild();
                }
            }));
            Task.Run(async () => await BarSystem.WifiOnAsync()).ContinueWith(t => Ui.BeginInvoke(() =>
            {
                if (gen == _generation && t.Result != WifiOn)
                {
                    WifiOn = t.Result;
                    _catalog = BarCatalog.Build(this);
                    Index();
                    Rebuild();
                }
            }));
        });
    }

    private static string TryName(string exe)
    {
        try
        {
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe);
            return string.IsNullOrWhiteSpace(info.FileDescription) ? Path.GetFileNameWithoutExtension(exe) : info.FileDescription.Trim();
        }
        catch
        {
            return Path.GetFileNameWithoutExtension(exe);
        }
    }

    private void Hydrate()
    {
        _catalog = BarCatalog.Build(this);
        _apps = BarCatalog.AppEntries(this);
        _panes = BarCatalog.PaneEntries(this);
        _windows = BarCatalog.WindowEntries(this);
        _quit = BarCatalog.QuitEntries(this);
        _selection = BarCatalog.SelectionEntries(this);
        _kill = [];
        _emoji = _emoji.Count == 0 ? BarCatalog.EmojiEntries(this) : _emoji;
        _menus = [];
        if (Target != 0 && Plat.ProcessOf(Target) != Environment.ProcessId && Prefs.IsSourceEnabled("menus"))
        {
            var target = Target;
            var gen = _generation;
            Task.Run(() => BarProviders.ReadMenus(target)).ContinueWith(t => Ui.BeginInvoke(() =>
            {
                if (gen == _generation && t.Result.Count > 0)
                {
                    _menus = BarCatalog.MenuEntries(this, t.Result);
                    Rebuild();
                }
            }));
        }

        Index();
    }

    private void Index()
    {
        _byStableKey = [];
        foreach (var e in _catalog.Concat(_apps).Concat(_panes))
        {
            _byStableKey.TryAdd(e.StableKey, e);
        }
    }

    public void End()
    {
        _generation++;
        _scriptTimer.Stop();
        _fileTimer.Stop();
        _fileCts?.Cancel();
        _scriptCts?.Cancel();
        Mode = BarMode.Search;
        ModeEntry = null;
        _lastQuery = Query;
        Query = "";
        Visible = false;
    }

    private bool _fromShortcut;

    private string _lastQuery = "";

    /// <summary>The query of the previous opening (memory only).</summary>
    public string LastQuery => _lastQuery;

    // ------------------------------------------------------------------ field input

    /// <summary>The user typed in the field.</summary>
    public void SetQuery(string text)
    {
        switch (Mode)
        {
            case BarMode.Search:
                if (text == Query)
                {
                    return;
                }

                Query = text;
                Peek = Peek || text.Length > 0;
                Warning = null;
                Rebuild();
                ScheduleBackgroundLookups();
                break;
            case BarMode.Argument:
            case BarMode.Naming:
                Warning = null;
                FieldText = text;
                break;
            default:
                break;
        }
    }

    public string FieldText { get; private set; } = "";

    /// <summary>Replaces the query from code (and tells the field).</summary>
    public void SetQueryDirect(string text)
    {
        Query = text;
        Selected = 0;
        QueryReplaced?.Invoke(text);
        Rebuild();
        ScheduleBackgroundLookups();
    }

    public void PrefillQuery(string text)
    {
        SetQueryDirect(text);
        FocusFieldRequested?.Invoke(text);
    }

    public void SetCategory(string? raw)
    {
        ActiveCategory = raw;
        Peek = true;
        Selected = 0;
        if (raw == "killProcess")
        {
            _kill = BarCatalog.KillEntries();
        }

        Rebuild();
    }

    // ------------------------------------------------------------------ selection movement

    public void Move(int delta)
    {
        if (Mode == BarMode.Actions)
        {
            if (Actions.Count > 0)
            {
                ActionSelected = (ActionSelected + delta + Actions.Count) % Actions.Count;
            }

            Changed?.Invoke();
            return;
        }

        if (Mode != BarMode.Search)
        {
            return;
        }

        if (IsCollapsed)
        {
            Peek = true;
            Rebuild();
            return;
        }

        if (Rows.Count == 0)
        {
            return;
        }

        Selected = (Selected + delta + Rows.Count) % Rows.Count;
        Changed?.Invoke();
    }

    public void SelectRow(int index)
    {
        if (index >= 0 && index < Rows.Count && Selected != index)
        {
            Selected = index;
            Changed?.Invoke();
        }
    }

    public BarEntry? SelectedEntry => Selected >= 0 && Selected < Rows.Count ? Rows[Selected] : null;

    public bool CanOpenActions => Mode == BarMode.Search && SelectedEntry is { } e && ActionsFor(e).Count > 0;

    public bool CanTabComplete => SelectedEntry?.Id == "math.result";

    /// <summary>Walks the category chips (only while the field is empty).</summary>
    public void WalkChips(int dir)
    {
        if (Mode != BarMode.Search || Query.Length > 0)
        {
            return;
        }

        var chips = Chips();
        var stops = new List<string?> { null };
        stops.AddRange(chips);
        var i = Math.Max(0, stops.IndexOf(ActiveCategory));
        i = Math.Clamp(i + dir, 0, stops.Count - 1);
        ActiveCategory = stops[i];
        Selected = 0;
        if (ActiveCategory is not null)
        {
            Peek = true;
        }

        Rebuild();
    }

    // ------------------------------------------------------------------ rebuild

    private bool Allowed(BarEntry e)
    {
        if (Prefs.Hidden().Contains(e.StableKey))
        {
            return false;
        }

        return Prefs.IsSourceEnabled(BarSource.Of(e.Id).Raw);
    }

    private void Rebuild()
    {
        if (Mode != BarMode.Search)
        {
            Changed?.Invoke();
            return;
        }

        var trimmed = Query.Trim();
        var keepId = trimmed == _lastTrimmed ? SelectedEntry?.Id : null;
        Items = [];
        Rows = [];
        if (IsCollapsed)
        {
            Selected = 0;
            _lastTrimmed = trimmed;
            Changed?.Invoke();
            return;
        }

        if (trimmed.Length == 0 && ActiveCategory is null)
        {
            BuildHome();
        }
        else if (ActiveCategory is not null)
        {
            BuildCategory(trimmed);
        }
        else
        {
            BuildSearch(trimmed);
        }

        Rows = [.. Items.Where(i => i.Entry is not null).Select(i => i.Entry!)];
        if (keepId is not null && Rows.FindIndex(r => r.Id == keepId) is var at and >= 0)
        {
            Selected = at;
        }
        else if (trimmed != _lastTrimmed || Selected >= Rows.Count)
        {
            Selected = 0;
        }

        _lastTrimmed = trimmed;
        ShowedHome = trimmed.Length == 0 && ActiveCategory is null;
        Changed?.Invoke();
    }

    private void Add(string heading, IEnumerable<BarEntry> rows)
    {
        var list = rows.ToList();
        if (list.Count == 0)
        {
            return;
        }

        Items.Add(new BarListItem { Heading = heading });
        foreach (var e in list)
        {
            Items.Add(new BarListItem { Entry = e });
        }
    }

    private IEnumerable<BarEntry> AllOfferable() => _catalog.Concat(_apps).Concat(_panes).Concat(_windows).Where(Allowed);

    private void BuildHome()
    {
        var shown = new HashSet<string>();
        var offerable = AllOfferable().ToList();
        if (_selection.Count > 0 && SelectionText is { } sel)
        {
            var flat = string.Join(' ', sel.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            Add("Selected · " + (flat.Length > 44 ? flat[..44] + "…" : flat), _selection);
            foreach (var e in _selection)
            {
                shown.Add(e.Id);
            }
        }

        var pins = Prefs.Pins()
            .Select(k => offerable.FirstOrDefault(e => e.StableKey == k))
            .Where(e => e is not null)
            .Cast<BarEntry>()
            .ToList();
        Add(Loc.T("Pinned"), pins);
        foreach (var e in pins)
        {
            shown.Add(e.Id);
        }

        var limit = Math.Max(0, 7 - pins.Count);
        var suggestions = new List<BarEntry>();
        foreach (var id in Learning.MostUsed())
        {
            if (suggestions.Count >= limit)
            {
                break;
            }

            var e = offerable.FirstOrDefault(x => x.Id == id);
            if (e is not null && !shown.Contains(e.Id) && e.CountsUsage)
            {
                suggestions.Add(e);
                shown.Add(e.Id);
            }
        }

        foreach (var id in Curated)
        {
            if (suggestions.Count >= limit)
            {
                break;
            }

            var e = offerable.FirstOrDefault(x => x.Id == id);
            if (e is not null && !shown.Contains(e.Id))
            {
                suggestions.Add(e);
                shown.Add(e.Id);
            }
        }

        Add(BarStrings.Suggestions, suggestions);
        var groups = new List<(string Heading, List<BarEntry> Rows)>();
        foreach (var e in _catalog.Where(Allowed))
        {
            if (shown.Contains(e.Id) || e.Id.StartsWith("app.", StringComparison.Ordinal))
            {
                continue;
            }

            var heading = e.Id.StartsWith("answer.", StringComparison.Ordinal) ? BarStrings.KindAnswer
                : e.Id.StartsWith("link.", StringComparison.Ordinal) ? BarStrings.KindLink
                : string.IsNullOrEmpty(e.Subtitle) ? BarStrings.Everything : e.Subtitle;
            var group = groups.FirstOrDefault(g => g.Heading == heading);
            if (group.Rows is null)
            {
                group = (heading, []);
                groups.Add(group);
            }

            if (group.Rows.Count < 12)
            {
                group.Rows.Add(e);
            }
        }

        foreach (var (heading, rows) in groups)
        {
            Add(heading, rows);
        }

        if (Prefs.IsSourceEnabled("clipboard"))
        {
            Add(BarStrings.PastFromHistory, FilterRows(BarCatalog.ClipboardEntries(this, 6)));
        }
    }

    private IEnumerable<BarEntry> FilterRows(IEnumerable<BarEntry> rows) => rows.Where(Allowed);

    private List<string> Chips()
    {
        var chips = new List<string>();
        foreach (var raw in BarSource.ChipOrder)
        {
            if (!Prefs.IsSourceEnabled(raw))
            {
                continue;
            }

            var ok = raw switch
            {
                "apps" or "macSettings" => true,
                "windows" => Ctx.Runtime.AnyAvailable(["switcher", "window-layout"]) && _windows.Count > 0,
                "menus" => _menus.Count > 0,
                "clipboard" => BarProviders.Clips().Count > 0,
                "emoji" => true,
                "actions" => _catalog.Any(e => Allowed(e) && InCategory(e, "actions")),
                _ => _catalog.Any(e => Allowed(e) && InCategory(e, raw)),
            };
            if (ok)
            {
                chips.Add(raw);
            }
        }

        return chips;
    }

    public IReadOnlyList<string> CategoryChips() => Mode == BarMode.Search && (ShowedHome || ActiveCategory is not null) ? Chips() : [];

    private static bool InCategory(BarEntry e, string raw)
    {
        var src = BarSource.Of(e.Id).Raw;
        return raw == "actions" ? src == "actions" || BarSource.IsActionBrowseRow(e.Id) : src == raw;
    }

    private IEnumerable<BarEntry> CategoryPool(string raw) => raw switch
    {
        "apps" => _apps,
        "macSettings" => _panes,
        "windows" => _windows,
        "menus" => _menus,
        "quitApps" => _quit,
        "emoji" => _emoji,
        "killProcess" => _kill,
        "clipboard" => BarCatalog.ClipboardEntries(this, 40),
        _ => _catalog.Where(e => InCategory(e, raw)),
    };

    private void BuildCategory(string trimmed)
    {
        var rows = CategoryPool(ActiveCategory!).Where(Allowed).ToList();
        List<BarEntry> result;
        if (trimmed.Length == 0)
        {
            result = [.. rows
                .Select((e, i) => (e, i))
                .OrderByDescending(p => Learning.UseCount(p.e.Id))
                .ThenByDescending(p => Learning.LastUsed(p.e.Id))
                .ThenBy(p => p.i)
                .Select(p => p.e)
                .Take(40)];
        }
        else
        {
            result = [.. Rank(rows, trimmed).Take(40)];
        }

        Add($"{BarSource.CategoryTitle(ActiveCategory!)} · {result.Count}", result);
    }

    private void BuildSearch(string trimmed)
    {
        // leading ':' -> emoji only
        if (trimmed.StartsWith(':') && Prefs.IsSourceEnabled("emoji"))
        {
            var rest = trimmed[1..].Trim();
            var pool = _emoji.Where(Allowed).ToList();
            var emojiRanked = rest.Length == 0 ? pool.Take(40).ToList() : Rank(pool, rest, emoji: true).Take(40).ToList();
            Add($"{BarSource.CategoryTitle("emoji")} · {emojiRanked.Count}", emojiRanked);
            return;
        }

        var result = new List<BarEntry>();
        var answer = AnswerRow(trimmed);
        if (answer is not null)
        {
            result.Add(answer);
        }

        if (LooksLikeUrl(trimmed))
        {
            result.Add(new BarEntry
            {
                Id = "action.openURL",
                Title = trimmed,
                EnglishTitle = trimmed,
                Subtitle = BarStrings.OpenInBrowser,
                Glyph = "",
                CountsUsage = false,
                BeatMs = 100,
                Run = _ => Plat.Open(trimmed.Contains("://") ? trimmed : "https://" + trimmed),
            });
        }

        var scriptRow = ScriptAnswerRow(trimmed);
        if (scriptRow is not null)
        {
            result.Add(scriptRow);
        }

        result.AddRange(_files.Where(f => Allowed(f)).Take(4));

        var (text, number) = BarSearch.SplitTrailingNumber(trimmed);
        var effective = number is not null && _catalog.Any(e => e.NumericRange is not null && Allowed(e) && BarSearch.Score(e.NormTitle, e.NormKeywords, BarSearch.Normalize(text)) is not null) ? text : trimmed;
        var nq = BarSearch.Normalize(effective);

        var pool2 = new List<BarEntry>();
        pool2.AddRange(_selection);
        pool2.AddRange(_catalog);
        pool2.AddRange(_apps);
        pool2.AddRange(_panes);
        pool2.AddRange(_windows);
        var first = nq.Split(' ')[0];
        if (first.Length >= 2 && "quit".StartsWith(first, StringComparison.Ordinal))
        {
            pool2.AddRange(_quit);
        }

        if (nq.Length >= 2)
        {
            pool2.AddRange(_menus);
        }

        pool2.AddRange(BarCatalog.ClipboardEntries(this, 400).Take(200));
        var pooled = pool2.Where(Allowed).ToList();
        if (scriptRow is not null)
        {
            pooled.RemoveAll(e => e.Id.StartsWith("link.", StringComparison.Ordinal) && e.TakesArgument);
        }
        else
        {
            // keep only the longest-named matching script/query link
            var matchingLinks = pooled.Where(e => e.TakesArgument && LinkMatches(e, nq)).ToList();
            if (matchingLinks.Count > 1)
            {
                var longest = matchingLinks.OrderByDescending(e => e.Title.Length).First();
                foreach (var e in matchingLinks.Where(e => e != longest))
                {
                    pooled.Remove(e);
                }
            }
        }

        var ranked = Rank(pooled, effective);
        var filtered = new List<BarEntry>();
        var counts = new Dictionary<string, int>();
        var firstToken = nq.Split(' ')[0];
        var seen = new HashSet<string>(result.Select(r => r.Id));
        foreach (var e in ranked)
        {
            if (!seen.Add(e.Id))
            {
                continue;
            }

            if (e.Id.StartsWith("answer.", StringComparison.Ordinal) && !(firstToken.Length >= 3 && (e.NormTitle.StartsWith(firstToken, StringComparison.Ordinal) || BarSearch.Normalize(e.EnglishTitle).StartsWith(firstToken, StringComparison.Ordinal))))
            {
                continue;
            }

            var kind = KindPrefix(e.Id);
            if (kind is not null)
            {
                counts.TryGetValue(kind.Value.Prefix, out var n);
                if (n >= kind.Value.Cap)
                {
                    continue;
                }

                counts[kind.Value.Prefix] = n + 1;
            }

            filtered.Add(e);
        }

        result.AddRange(filtered);
        if (result.Count > 12)
        {
            result = result.Take(12).ToList();
        }

        // lone colour preview placement
        if (answer is null && CommandBarColors.Parse(trimmed) is { } color && Prefs.IsSourceEnabled("calculator"))
        {
            var preview = new BarEntry
            {
                Id = "color.preview",
                Title = trimmed,
                EnglishTitle = trimmed,
                Subtitle = BarStrings.ReturnCopies,
                Swatch = CommandBarColors.ToColor(color),
                IsAnswer = true,
                CountsUsage = false,
                Run = _ => Copy(trimmed),
            };
            var at = result.Any(r => BarSearch.Normalize(r.Title).Contains(BarSearch.Normalize(trimmed), StringComparison.Ordinal)) ? Math.Min(1, result.Count) : 0;
            result.Insert(at, preview);
        }

        foreach (var e in result)
        {
            Items.Add(new BarListItem { Entry = e });
        }
    }

    private static (string Prefix, int Cap)? KindPrefix(string id)
    {
        foreach (var (prefix, cap) in new[] { ("app.", 5), ("window.", 4), ("quit.", 3), ("menu.", 5), ("emoji.", 6), ("settings.", 4), ("macsettings.", 4), ("clipboard.", 4), ("snippet.", 4), ("file.", 4), ("toggle.", 5) })
        {
            if (id.StartsWith(prefix, StringComparison.Ordinal))
            {
                return (prefix, cap);
            }
        }

        return null;
    }

    private bool LinkMatches(BarEntry e, string nq)
    {
        var name = BarSearch.Normalize(e.Title);
        return nq == name || nq.StartsWith(name + " ", StringComparison.Ordinal);
    }

    private static bool LooksLikeUrl(string text)
    {
        if (text.Length < 4 || text.Contains(' '))
        {
            return false;
        }

        var t = text.Contains("://") ? text : "https://" + text;
        if (!Uri.TryCreate(t, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        var host = uri.Host;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var dot = host.LastIndexOf('.');
        return dot > 0 && host.Length - dot - 1 >= 2 && host[(dot + 1)..].All(char.IsLetter);
    }

    private List<BarEntry> Rank(IEnumerable<BarEntry> entries, string query, bool emoji = false)
    {
        var list = entries.ToList();
        var nq = BarSearch.Normalize(query);
        var aliases = Prefs.Aliases();
        var habits = Learning.HabitPriorities(emoji ? nq : nq);
        var pins = Prefs.Pins().ToHashSet();
        var candidates = new List<BarSearch.Candidate>(list.Count);
        for (var i = 0; i < list.Count; i++)
        {
            var e = list[i];
            var source = BarSource.Of(e.Id);
            var alias = aliases.GetValueOrDefault(e.StableKey);
            var aliasNorm = alias is null ? "" : BarSearch.Normalize(alias);
            var aliasBoost = 0;
            if (aliasNorm.Length > 0 && nq.Length > 0)
            {
                var words = aliasNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                aliasBoost = words.Any(w => w == nq) ? 2400 : words.Any(w => w.StartsWith(nq, StringComparison.Ordinal)) ? 1100 : 0;
            }

            var normTitle = e.NormTitle;
            if (e.TakesArgument && e.RankingTitle is not null)
            {
                var name = BarSearch.Normalize(e.RankingTitle);
                normTitle = nq == name || nq.StartsWith(name + " ", StringComparison.Ordinal) ? nq : name;
            }

            var habit = e.CountsUsage && habits.TryGetValue(e.Id, out var h) ? h : 0;
            var boost = (e.CountsUsage ? Learning.UsageBoostOf(e.Id) + Learning.MemoryBoost(nq, e.Id) : 0)
                + (e.IsActive ? 20 : 0) + aliasBoost + source.Bias + (pins.Contains(e.StableKey) ? 60 : 0);
            candidates.Add(new BarSearch.Candidate
            {
                Index = i,
                NormTitle = normTitle,
                NormKeywords = aliasNorm.Length > 0 ? e.NormKeywords + " " + aliasNorm : e.NormKeywords,
                Priority = Math.Max(aliasBoost, habit),
                Boost = boost,
            });
        }

        var order = BarSearch.Rank(candidates, nq);
        var ordered = order.Select(i => list[i]).ToList();
        return FeatureOrdered(ordered, candidates, order);
    }

    private static List<BarEntry> FeatureOrdered(List<BarEntry> ordered, List<BarSearch.Candidate> candidates, List<int> order)
    {
        // Rows of one feature trade places so the order is: main command, presets, Settings page.
        var groups = new Dictionary<string, List<int>>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var e = ordered[i];
            if (e.FeatureKey is not null && e.FeatureTurn >= 0 && candidates[order[i]].Priority == 0)
            {
                if (!groups.TryGetValue(e.FeatureKey, out var slots))
                {
                    groups[e.FeatureKey] = slots = [];
                }

                slots.Add(i);
            }
        }

        var result = new List<BarEntry>(ordered);
        foreach (var slots in groups.Values.Where(g => g.Count > 1))
        {
            var sorted = slots.Select(i => ordered[i]).OrderBy(e => e.FeatureTurn).ToList();
            for (var k = 0; k < slots.Count; k++)
            {
                result[slots[k]] = sorted[k];
            }
        }

        return result;
    }

    // ------------------------------------------------------------------ answers

    private BarEntry? AnswerRow(string q)
    {
        if (!Prefs.IsSourceEnabled("calculator") || q.Length == 0)
        {
            return null;
        }

        var math = CommandBarMath.Evaluate(q);
        if (math is not null)
        {
            return Answer("math.result", math.Display, math.Subtitle, 0xE8EF, math.Display, math.Reusable);
        }

        var units = CommandBarUnits.Convert(q);
        if (units is not null)
        {
            return Answer("units.result", units, BarStrings.ReturnCopies, 0xE8AB, units);
        }

        var colour = CommandBarColors.Convert(q);
        if (colour is not null)
        {
            return Answer("color.result", colour, BarStrings.ReturnCopies, 0xE790, colour);
        }

        var date = CommandBarDates.Answer(q, DateTime.Now);
        if (date is not null)
        {
            return Answer("date.result", date.Title, date.Detail, 0xE787, date.Title);
        }

        return null;
    }

    private BarEntry Answer(string id, string title, string subtitle, int glyph, string copy, string? reuse = null) => new()
    {
        Id = id,
        Title = title,
        EnglishTitle = title,
        Subtitle = subtitle,
        Glyph = char.ConvertFromUtf32(glyph),
        IsAnswer = true,
        BigTitle = true,
        CountsUsage = false,
        ReuseText = reuse,
        Run = _ => Copy(copy),
    };

    private static void Copy(string value)
    {
        try
        {
            Clipboard.SetText(value);
            ToolHud.Show("", value);
        }
        catch
        {
            ToolHud.Show("", Loc.T("Couldn’t copy"));
        }
    }

    // ------------------------------------------------------------------ scripts and files

    private BarEntry? ScriptAnswerRow(string q)
    {
        var links = Prefs.Links().Where(l => l.Kind == "script").ToList();
        if (links.Count == 0 || !Prefs.IsSourceEnabled("links"))
        {
            return null;
        }

        var nq = BarSearch.Normalize(q);
        foreach (var link in links.OrderByDescending(l => l.Name.Length))
        {
            var arg = CommandBarLinks.TrailingArgument(q, link.Name);
            var bare = link.RunsWithoutArgument && nq == BarSearch.Normalize(link.Name);
            if (arg is null && !bare)
            {
                continue;
            }

            var key = link.Id + "|" + (bare ? "" : arg);
            if (_scriptResults.TryGetValue(key, out var output))
            {
                return new BarEntry
                {
                    Id = "link." + link.Id,
                    Title = output,
                    EnglishTitle = link.Name,
                    Subtitle = BarStrings.ReturnCopies,
                    Glyph = "",
                    IsAnswer = true,
                    BigTitle = false,
                    CountsUsage = false,
                    KeepsBarOpen = true,
                    Run = _ => Copy(output),
                };
            }

            return null;
        }

        return null;
    }

    private void ScheduleBackgroundLookups()
    {
        _scriptTimer.Stop();
        _scriptTimer.Start();
        _fileTimer.Stop();
        if (Prefs.IsSourceEnabled("files") && Prefs.FileScopes().Count > 0 && Query.Trim().Length >= 2)
        {
            _fileTimer.Start();
        }
        else if (_files.Count > 0)
        {
            _files = [];
        }
    }

    private void StartScript(bool now)
    {
        var q = Query.Trim();
        if (q.Length == 0 || !Prefs.IsSourceEnabled("links"))
        {
            return;
        }

        var nq = BarSearch.Normalize(q);
        foreach (var link in Prefs.Links().Where(l => l.Kind == "script").OrderByDescending(l => l.Name.Length))
        {
            var arg = CommandBarLinks.TrailingArgument(q, link.Name);
            var bare = link.RunsWithoutArgument && nq == BarSearch.Normalize(link.Name);
            if (arg is null && !bare)
            {
                continue;
            }

            var argument = bare ? null : arg;
            var key = link.Id + "|" + (argument ?? "");
            if (_scriptResults.ContainsKey(key) || !_scriptPending.Add(key))
            {
                return;
            }

            var gen = _generation;
            _scriptCts?.Cancel();
            _scriptCts = new CancellationTokenSource();
            var token = _scriptCts.Token;
            CommandBarLinks.RunScriptAsync(link, argument, token).ContinueWith(t => Ui.BeginInvoke(() =>
            {
                _scriptPending.Remove(key);
                if (gen != _generation || t.IsFaulted || t.IsCanceled)
                {
                    return;
                }

                var (output, failed) = t.Result;
                if (!string.IsNullOrEmpty(output))
                {
                    _scriptResults[key] = output;
                }
                else if (failed)
                {
                    _scriptResults[key] = Loc.T("Couldn’t run this file");
                }

                Rebuild();
            }));
            return;
        }
    }

    private void StartFileSearch()
    {
        var q = Query.Trim();
        if (q.Length < 2)
        {
            return;
        }

        _fileCts?.Cancel();
        _fileCts = new CancellationTokenSource();
        var token = _fileCts.Token;
        var gen = _generation;
        var scopes = CommandBarFileSearch.ResolveScopes(Prefs.FileScopes());
        var ignores = Prefs.FileIgnores();
        CommandBarFileSearch.SearchAsync(q, scopes, ignores, token).ContinueWith(t => Ui.BeginInvoke(() =>
        {
            if (gen != _generation || token.IsCancellationRequested || t.IsFaulted || t.IsCanceled || q != Query.Trim())
            {
                return;
            }

            _files = BarCatalog.FileEntries(this, t.Result);
            _fileQuery = q;
            Rebuild();
        }));
    }

    public bool Awaiting => _scriptPending.Count > 0 || _fileTimer.IsEnabled;

    // ------------------------------------------------------------------ running rows

    public void RunSelected()
    {
        if (Mode == BarMode.Actions)
        {
            if (Actions.Count > 0)
            {
                RunAction(Actions[ActionSelected]);
            }

            return;
        }

        if (Mode == BarMode.Argument && ModeEntry is not null)
        {
            SubmitArgument(FieldText);
            return;
        }

        if (Mode == BarMode.Confirm && ModeEntry is not null)
        {
            Finish(ModeEntry, null);
            return;
        }

        if (Mode != BarMode.Search)
        {
            return;
        }

        var entry = SelectedEntry;
        if (entry is null)
        {
            if (Query.Length > 0 || ActiveCategory is not null)
            {
                ActiveCategory = null;
                SetQueryDirect("");
            }

            return;
        }

        Run(entry);
    }

    public void Run(BarEntry entry)
    {
        if (entry.NeedsSetupFeature is { } feature)
        {
            Hide();
            Ctx.Shell.OpenSettings(entry.NeedsSetupDestination ?? SettingsPages.Features);
            _ = feature;
            return;
        }

        _savedQuery = Query;
        if (entry.ConfirmPrompt is not null)
        {
            Mode = BarMode.Confirm;
            ModeEntry = entry;
            Changed?.Invoke();
            return;
        }

        if (entry.NumericRange is { } range)
        {
            var (text, number) = BarSearch.SplitTrailingNumber(Query);
            if (number is { } n && BarSearch.Score(entry.NormTitle, entry.NormKeywords, BarSearch.Normalize(text)) is not null)
            {
                Finish(entry, Math.Clamp(n, range.Min, range.Max));
                return;
            }

            if (entry.NumericIsOptional)
            {
                Finish(entry, null);
                return;
            }

            Mode = BarMode.Argument;
            ModeEntry = entry;
            FieldText = "";
            Warning = null;
            QueryReplaced?.Invoke("");
            Changed?.Invoke();
            return;
        }

        Finish(entry, null);
    }

    public void SubmitArgument(string text)
    {
        if (ModeEntry?.NumericRange is not { } range)
        {
            return;
        }

        var t = text.Trim().TrimEnd('%');
        if (t.Length is >= 1 and <= 4 && t.All(char.IsAsciiDigit) && int.TryParse(t, out var n))
        {
            Finish(ModeEntry, Math.Clamp(n, range.Min, range.Max));
        }
        else
        {
            System.Media.SystemSounds.Beep.Play();
        }
    }

    public void ConfirmYes()
    {
        if (ModeEntry is { } e)
        {
            Finish(e, null);
        }
    }

    private void Finish(BarEntry entry, int? value)
    {
        QueryWhenRun = _savedQuery.Length > 0 && Mode != BarMode.Search ? _savedQuery : Query;
        SelectionWhenRun = SelectionText;
        var typed = BarSearch.Normalize(Mode == BarMode.Search ? Query : _savedQuery);
        if (typed.StartsWith(':'))
        {
            typed = typed[1..].Trim();
        }

        Learning.Record(entry.Id, entry.CountsUsage && Visible, _typedBeforeTab.Length > 0 && Mode == BarMode.Search ? BarSearch.Normalize(_typedBeforeTab) : typed);
        _typedBeforeTab = "";
        if (entry.KeepsBarOpen)
        {
            Mode = BarMode.Search;
            ModeEntry = null;
            entry.Run(value);
            return;
        }

        Mode = BarMode.Search;
        ModeEntry = null;
        var target = Target;
        var isWindow = entry.Id.StartsWith("window.", StringComparison.Ordinal);
        Hide();
        if (!isWindow && target != 0)
        {
            Plat.Activate(target);
        }

        Plat.After(Ui, entry.BeatMs, () =>
        {
            try
            {
                entry.Run(value);
            }
            catch (Exception ex)
            {
                Ctx.Log.Error("Command Bar row failed: " + entry.Id, ex);
            }
        });
    }

    public void Hide() => HideRequested?.Invoke();

    // ------------------------------------------------------------------ Esc / Tab

    /// <summary>Esc ladder. Returns true when the bar should close.</summary>
    public bool StepBack()
    {
        switch (Mode)
        {
            case BarMode.Actions:
                Mode = BarMode.Search;
                ModeEntry = null;
                Warning = null;
                Changed?.Invoke();
                return false;
            case BarMode.Naming:
            case BarMode.Capturing:
                Mode = BarMode.Actions;
                Warning = null;
                QueryReplaced?.Invoke("");
                Changed?.Invoke();
                return false;
            case BarMode.Argument:
            case BarMode.Confirm:
                Mode = BarMode.Search;
                ModeEntry = null;
                Query = _savedQuery;
                QueryReplaced?.Invoke(_savedQuery);
                Rebuild();
                return false;
        }

        if (Query.Length > 0)
        {
            SetQueryDirect("");
            return false;
        }

        if (ActiveCategory is not null)
        {
            ActiveCategory = null;
            Selected = 0;
            Rebuild();
            return false;
        }

        if (Peek && Compact)
        {
            Peek = false;
            Rebuild();
            return false;
        }

        return true;
    }

    public void Tab()
    {
        if (Mode != BarMode.Search || SelectedEntry is not { } e)
        {
            return;
        }

        _typedBeforeTab = Query.Trim();
        if (e.Id == "math.result" && e.ReuseText is { } reuse)
        {
            SetQueryDirect(reuse);
            return;
        }

        if (e.IsAnswer)
        {
            return;
        }

        SetQueryDirect(e.Id.StartsWith("emoji.", StringComparison.Ordinal) && e.Id != "emoji.browse" ? ":" + e.EnglishTitle : e.MatchTitle ?? e.Title);
    }

    public void HandleDigit(int n)
    {
        if (Mode == BarMode.Search && n >= 1 && n <= Rows.Count)
        {
            Run(Rows[n - 1]);
        }
    }

    public void RevealSelected()
    {
        if (SelectedEntry?.RevealPath is { } path)
        {
            Hide();
            Plat.After(Ui, 100, () => { RevealInExplorer(path); });
        }
        else
        {
            RunSelected();
        }
    }

    private static bool RevealInExplorer(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), File.Exists(path) ? $"/select,\"{path}\"" : $"\"{path}\"");
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ------------------------------------------------------------------ actions panel

    public IReadOnlyList<BarAction> ActionsFor(BarEntry e)
    {
        var list = new List<BarAction>();
        if (e.ExtraActions?.Invoke() is { } extra)
        {
            list.AddRange(extra);
        }

        if (e.Id.StartsWith("quit.", StringComparison.Ordinal) || e.Source.Raw == "apps")
        {
            // running app extras
        }

        if (e.RevealPath is { } path)
        {
            list.Add(new BarAction { Id = "reveal", Title = BarStrings.ShowInExplorer, Glyph = "", Run = () => { Hide(); Plat.After(Ui, 100, () => RevealInExplorer(path)); } });
        }

        var src = BarSource.Of(e.Id);
        if (src.AcceptsPin)
        {
            var pinned = Prefs.IsPinned(e.StableKey);
            list.Add(new BarAction { Id = "pin", Title = pinned ? BarStrings.Unpin : BarStrings.Pin, Glyph = pinned ? "" : "", Run = () => { Prefs.SetPinned(e.StableKey, !pinned); } });
        }

        if (src.AcceptsAlias)
        {
            var has = Prefs.AliasOf(e.StableKey) is not null;
            list.Add(new BarAction { Id = "name", Title = has ? BarStrings.Rename : BarStrings.Name, Glyph = "", ClosesPanel = false, Run = () => BeginNaming(e) });
            var hk = Prefs.RowShortcuts().ContainsKey(e.StableKey);
            list.Add(new BarAction { Id = "shortcut", Title = hk ? BarStrings.ShortcutChange : BarStrings.Shortcut, Glyph = "", ClosesPanel = false, Run = () => BeginCapture(e) });
            if (hk)
            {
                list.Add(new BarAction { Id = "unshortcut", Title = BarStrings.ShortcutRemove, Glyph = "", Run = () => ShortcutsChanged(e.StableKey, null) });
            }
        }

        if (src.Raw is not ("menus" or "windows" or "clipboard" or "selection" or "files" or "killProcess" or "uninstallApps"))
        {
            list.Add(new BarAction { Id = "hide", Title = BarStrings.Hide, Glyph = "", Run = () => Prefs.SetHidden(e.StableKey, true) });
        }

        if (e.CountsUsage && !e.IsAnswer)
        {
            list.Add(new BarAction { Id = "forget", Title = BarStrings.Forget, Glyph = "", Run = () => Learning.Forget(e.Id) });
        }

        return list;
    }

    public void OpenActions()
    {
        if (!CanOpenActions || SelectedEntry is not { } entry)
        {
            return;
        }

        ModeEntry = entry;
        Actions = [.. ActionsFor(entry)];
        ActionSelected = 0;
        Mode = BarMode.Actions;
        Changed?.Invoke();
    }

    public void RunAction(BarAction action)
    {
        action.Run();
        if (action.ClosesPanel)
        {
            Mode = BarMode.Search;
            ModeEntry = null;
            _catalog = BarCatalog.Build(this);
            Index();
            Rebuild();
        }
    }

    public void TogglePinSelected()
    {
        if (Mode == BarMode.Search && SelectedEntry is { } e && BarSource.Of(e.Id).AcceptsPin)
        {
            Prefs.SetPinned(e.StableKey, !Prefs.IsPinned(e.StableKey));
            Rebuild();
        }
    }

    public void BeginNaming(BarEntry e)
    {
        ModeEntry = e;
        Mode = BarMode.Naming;
        FieldText = Prefs.AliasOf(e.StableKey) ?? "";
        Warning = null;
        QueryReplaced?.Invoke(FieldText);
        Changed?.Invoke();
    }

    public void SubmitName(string text)
    {
        if (ModeEntry is not { } e)
        {
            return;
        }

        var clean = text.Trim();
        if (clean.Length > 0 && Prefs.AliasOwner(e.StableKey, clean) is { } owner)
        {
            var title = _byStableKey.TryGetValue(owner, out var other) ? other.Title : owner;
            Warning = BarStrings.AliasTaken(title);
            Changed?.Invoke();
            return;
        }

        Prefs.SetAlias(e.StableKey, clean);
        Mode = BarMode.Search;
        ModeEntry = null;
        QueryReplaced?.Invoke(Query);
        Rebuild();
    }

    public void BeginCapture(BarEntry e)
    {
        ModeEntry = e;
        Mode = BarMode.Capturing;
        Warning = null;
        CaptureStarted?.Invoke(true);
        Changed?.Invoke();
    }

    public event Action<bool>? CaptureStarted;

    public event Action<string, Hotkey?>? RowShortcutAssigned;

    /// <summary>The capture card got a key combination (null = Delete: clear).</summary>
    public void CaptureCombination(Hotkey? hotkey)
    {
        if (ModeEntry is not { } e)
        {
            return;
        }

        if (hotkey is null)
        {
            ShortcutsChanged(e.StableKey, null);
            EndCapture();
            return;
        }

        var error = ValidateRowShortcut(e.StableKey, hotkey);
        if (error is not null)
        {
            Warning = error;
            Changed?.Invoke();
            return;
        }

        ShortcutsChanged(e.StableKey, hotkey);
        EndCapture();
    }

    private void EndCapture()
    {
        CaptureStarted?.Invoke(false);
        Mode = BarMode.Search;
        ModeEntry = null;
        Rebuild();
    }

    public void CancelCapture() => CaptureStarted?.Invoke(false);

    private void ShortcutsChanged(string key, Hotkey? hotkey)
    {
        Prefs.SetRowShortcut(key, hotkey);
        RowShortcutAssigned?.Invoke(key, hotkey);
    }

    /// <summary>Validation of spec 1.7.7 (null = fine).</summary>
    public string? ValidateRowShortcut(string key, Hotkey hotkey)
    {
        if (hotkey.Modifiers == HotkeyModifiers.None)
        {
            return "Use at least Ctrl, Alt or Win with a key.";
        }

        var shortcuts = Prefs.RowShortcuts();
        var owner = shortcuts.FirstOrDefault(p => p.Value.Equals(hotkey) && p.Key != key).Key;
        if (owner is not null)
        {
            return Loc.F("This shortcut is already used by {0}.", TitleOfKey(owner));
        }

        if (!shortcuts.ContainsKey(key) && shortcuts.Count >= BarPreferences.MaxRowShortcuts)
        {
            return Loc.F("You can assign up to {0} shortcuts. Remove one to add another.", BarPreferences.MaxRowShortcuts);
        }

        if (Ctx.Shortcuts.FindConflict(hotkey, "") is { } role)
        {
            return Loc.F("This shortcut is already used by {0}.", role);
        }

        if (HotkeyMap.IsReserved(hotkey))
        {
            return "This shortcut is already used by Windows.";
        }

        return null;
    }

    public string TitleOfKey(string key)
    {
        if (_byStableKey.TryGetValue(key, out var e))
        {
            return e.Title;
        }

        var link = Prefs.Links().FirstOrDefault(l => "link." + l.Id == key);
        return link?.Name ?? key;
    }

    public IReadOnlyList<BarEntry> AppEntriesSnapshot()
    {
        BarProviders.RescanApps();
        return [.. _apps];
    }

    public bool AppsLoaded => _apps.Count > 0;

    public void RefreshCatalog()
    {
        _catalog = BarCatalog.Build(this);
        _apps = BarCatalog.AppEntries(this);
        Index();
    }

    public BarEntry? Lookup(string stableKey) => _byStableKey.GetValueOrDefault(stableKey);

    // ------------------------------------------------------------------ links / apps / row shortcuts

    public void RunLink(CommandBarLink link)
    {
        var arg = CommandBarLinks.TrailingArgument(Query, link.Name);
        if (link.Kind == "script")
        {
            var nq = BarSearch.Normalize(Query);
            if (arg is null && !(link.RunsWithoutArgument && nq == BarSearch.Normalize(link.Name)))
            {
                PrefillQuery(link.Name + " ");
                return;
            }

            var key = link.Id + "|" + (arg ?? "");
            if (_scriptResults.TryGetValue(key, out var output))
            {
                Copy(output);
                Hide();
                return;
            }

            _scriptTimer.Stop();
            StartScript(true);
            return;
        }

        if (link.TakesQuery && string.IsNullOrWhiteSpace(arg))
        {
            PrefillQuery(link.Name + " ");
            return;
        }

        var clipboard = link.Destination.Contains("{clipboard}", StringComparison.Ordinal) ? SafeClipboard() : null;
        var target = CommandBarLinks.Expand(link, arg, clipboard, SelectionWhenRun ?? SelectionText, DateTime.Now);
        Hide();
        if (link.Kind == "place")
        {
            var path = BarCatalog.Expand(target);
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                ToolHud.Show("", link.Name);
                return;
            }

            Plat.Open(path);
            return;
        }

        var url = target.Contains("://", StringComparison.Ordinal) || target.Contains(':') && !target.Contains('\\') ? target : "https://" + target;
        if (!Plat.Open(url))
        {
            System.Media.SystemSounds.Beep.Play();
        }
    }

    private static string? SafeClipboard()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Hides the app when it is already in front; returns true when it did.</summary>
    public bool HideAppInFront(BarApp app)
    {
        try
        {
            var fg = Target != 0 ? Target : Plat.Foreground();
            var exe = N.ProcessPath(Plat.ProcessOf(fg));
            if (exe is null || !_fromShortcut)
            {
                return false;
            }

            if (BarSearch.Normalize(Path.GetFileNameWithoutExtension(exe)) == BarSearch.Normalize(app.Name) || BarSearch.Normalize(TryName(exe)) == BarSearch.Normalize(app.Name))
            {
                N.ShowWindow(fg, 6);
                return true;
            }
        }
        catch
        {
            // launch instead
        }

        return false;
    }

    /// <summary>Runs a row from its own global shortcut (bar closed). Returns true when it needs the bar.</summary>
    public bool RunRowByKey(string key)
    {
        _catalog = BarCatalog.Build(this);
        Index();
        if (!_byStableKey.TryGetValue(key, out var entry) && key.StartsWith("app.", StringComparison.Ordinal))
        {
            _apps = BarCatalog.AppEntries(this);
            Index();
            _byStableKey.TryGetValue(key, out entry);
        }

        if (entry is null || Prefs.Hidden().Contains(key))
        {
            return false;
        }

        if (key.StartsWith("link.", StringComparison.Ordinal))
        {
            var link = Prefs.Links().FirstOrDefault(l => "link." + l.Id == key);
            if (link is { Kind: "script", RunsDirectly: true } && Prefs.IsSourceEnabled("links"))
            {
                _ = CommandBarLinks.RunScriptAsync(link, null, CancellationToken.None).ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully && t.Result.Failed)
                    {
                        Ui.BeginInvoke(() => System.Media.SystemSounds.Beep.Play());
                    }
                });
                return false;
            }
        }

        if (entry.NeedsPrompt || entry.KeepsBarOpen)
        {
            return true;
        }

        QueryWhenRun = "";
        _fromShortcut = true;
        try
        {
            entry.Run(null);
        }
        finally
        {
            _fromShortcut = false;
        }

        return false;
    }

    /// <summary>The bar was opened by a row shortcut that needs a prompt: run it through the normal path.</summary>
    public void RunPrompted(string key)
    {
        if (_byStableKey.TryGetValue(key, out var entry))
        {
            Run(entry);
        }
    }
}
