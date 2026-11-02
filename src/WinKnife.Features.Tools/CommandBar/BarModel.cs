// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Text;
using System.Windows.Media;

namespace WinKnife.Features.Tools;

/// <summary>Verbatim English strings of the Command Bar (spec 1.3).</summary>
internal static class BarStrings
{
    public const string PageTitle = "Command Bar";
    public const string SearchPlaceholder = "Type what you want to do";
    public const string Suggestions = "Suggestions";
    public const string NoResultsTitle = "Nothing here by that name.";
    public const string NoResultsAction = "See suggestions";
    public const string ArgumentHint = "Return applies · Esc goes back";
    public const string ConfirmHint = "Return confirms · Esc cancels";
    public const string KindApp = "App";
    public const string KindClipboard = "Paste from history";
    public const string KindSnippet = "Insert snippet";
    public const string KindWindow = "Window";
    public const string KindMenu = "Menu command";
    public const string KindEmoji = "Emoji";
    public const string KindFolder = "Folder";
    public const string KindAnswer = "Answer";
    public const string KindSelection = "Selection";
    public const string KindLink = "Your shortcut";
    public const string SettingsSubtitle = "WinKnife Settings";
    public const string PanesSubtitle = "Windows Settings pages";
    public const string ActionsTitle = "Actions";
    public const string Pin = "Pin to the top";
    public const string Unpin = "Unpin";
    public const string Name = "Give it your own name";
    public const string Rename = "Change the name it answers to";
    public const string Hide = "Never show this";
    public const string Forget = "Forget how often I use this";
    public const string AliasPlaceholder = "The name you call it";
    public const string Shortcut = "Give it a shortcut";
    public const string ShortcutChange = "Change the shortcut";
    public const string ShortcutRemove = "Take the shortcut off";
    public const string ShortcutCaptureHint = "Press the keys you want · Delete clears it · Esc goes back";
    public const string DragHint = "Drag to move · Double-click to recenter";
    public const string TryLabel = "Try";
    public const string Everything = "Everything it can do";
    public const string PastFromHistory = "Paste from history";
    public const string ShowInExplorer = "Show in File Explorer";
    public const string OpenInBrowser = "Open in browser";
    public const string Cancel = "Cancel";
    public const string Confirm = "Confirm";
    public const string ReturnCopies = "Return copies";
    public const string Continue = "Continue";

    public static string NeedsSetup(string feature) => $"Turn on {feature} in Settings";

    public static string AliasTaken(string title) => $"{title} already answers to that";
}

/// <summary>A category / source of rows (spec 1.5.1).</summary>
/// <param name="Raw">Persisted raw value.</param>
/// <param name="Label">Settings toggle label.</param>
/// <param name="Glyph">Segoe glyph.</param>
/// <param name="Prefix">Row id prefix (empty: none).</param>
/// <param name="Bias">Rank bias.</param>
/// <param name="AcceptsPin">Rows can be pinned.</param>
/// <param name="AcceptsAlias">Rows can get an alias / shortcut.</param>
internal sealed record BarSource(string Raw, string Label, string Glyph, string Prefix, int Bias, bool AcceptsPin, bool AcceptsAlias)
{
    public static readonly BarSource[] All =
    [
        new("actions", "WinKnife actions", "\uE945", "", 0, true, true),
        new("apps", "Apps", "\uE80A", "app.", 80, true, true),
        new("menus", "Menu commands of the app in front", "\uE700", "menu.", -80, false, false),
        new("windows", "Open windows", "\uE737", "window.", 0, true, false),
        new("quitApps", "Quit an app", "\uE711", "quit.", 0, false, true),
        new("uninstallApps", "Uninstaller", "\uE74D", "uninstall.", 0, false, false),
        new("settingsPages", "Settings pages", "\uE713", "settings.", -40, true, true),
        new("macSettings", "Windows Settings pages", "\uE713", "macsettings.", 0, true, true),
        new("snippets", "Snippets", "\uE8D2", "snippet.", 0, true, true),
        new("clipboard", "Clipboard history", "\uE77F", "clipboard.", 0, false, false),
        new("emoji", "Emoji", "\uE76E", "emoji.", 0, false, true),
        new("folders", "Folders", "\uE8B7", "folder.", 0, true, true),
        new("answers", "Answers about this PC", "\uE946", "answer.", 0, true, true),
        new("calculator", "Sums and conversions", "\uE8EF", "", 0, true, true),
        new("selection", "What is selected", "\uE8E9", "selection.", 0, false, false),
        new("links", "Your shortcuts", "\uE8A4", "link.", 0, true, true),
        new("files", "Files", "\uE721", "file.", -40, false, false),
        new("killProcess", "Kill Process", "\uEA39", "kill.", 0, false, false),
    ];

    /// <summary>Category chip order.</summary>
    public static readonly string[] ChipOrder = ["actions", "apps", "clipboard", "windows", "menus", "settingsPages", "macSettings", "snippets", "emoji", "folders", "links"];

    /// <summary>Titles of the browse headings.</summary>
    public static string CategoryTitle(string raw) => raw switch
    {
        "actions" => "WinKnife actions",
        "apps" => "Apps",
        "menus" => KindMenuTitle,
        "windows" => "Open windows",
        "settingsPages" => "Settings pages",
        "macSettings" => "Windows Settings pages",
        "snippets" => "Snippets",
        "clipboard" => "Clipboard history",
        "emoji" => "Emoji",
        "folders" => "Folders",
        "links" => "Your shortcuts",
        "killProcess" => "Kill Process",
        _ => raw,
    };

    private const string KindMenuTitle = "Menu command";

    private static readonly HashSet<string> ActionBrowse = ["emoji.browse", "kill.browse", "uninstall.browse", "uninstall.finder"];

    private static readonly HashSet<string> CalculatorIds = ["math.result", "units.result", "color.result", "color.preview", "date.result"];

    public static bool IsActionBrowseRow(string id) => ActionBrowse.Contains(id);

    /// <summary>Source of a row id: first whose prefix matches, calculator ids, else actions.</summary>
    public static BarSource Of(string rowId)
    {
        if (CalculatorIds.Contains(rowId))
        {
            return Get("calculator");
        }

        foreach (var s in All)
        {
            if (s.Prefix.Length > 0 && rowId.StartsWith(s.Prefix, StringComparison.Ordinal))
            {
                return s;
            }
        }

        return All[0];
    }

    public static BarSource Get(string raw) => All.First(s => s.Raw == raw);
}

/// <summary>One action of the Actions panel.</summary>
internal sealed class BarAction
{
    public string Id { get; init; } = "";

    public string Title { get; init; } = "";

    public string Glyph { get; init; } = "";

    public bool Destructive { get; init; }

    public bool ClosesPanel { get; init; } = true;

    public Action Run { get; init; } = () => { };

    public System.Windows.Media.ImageSource? Swatch { get; init; }
}

/// <summary>A row of the bar (spec 1.5).</summary>
internal sealed class BarEntry
{
    public string Id { get; init; } = "";

    public string? StableKeyOverride { get; init; }

    public string StableKey => StableKeyOverride ?? Id;

    public string Title { get; set; } = "";

    public string EnglishTitle { get; init; } = "";

    public string Subtitle { get; set; } = "";

    public string Keywords { get; set; } = "";

    public string Glyph { get; init; } = "";

    public System.Windows.Media.ImageSource? Icon { get; set; }

    public Func<System.Windows.Media.ImageSource?>? IconLoader { get; init; }

    public Color? Swatch { get; init; }

    public string? Shortcut { get; set; }

    public string? MenuShortcut { get; init; }

    public bool IsActive { get; set; }

    public string? NeedsSetupFeature { get; init; }

    public string? NeedsSetupDestination { get; init; }

    public (int Min, int Max)? NumericRange { get; init; }

    public bool NumericIsOptional { get; init; }

    public string? ConfirmPrompt { get; init; }

    public string? AnswerValue { get; init; }

    public bool IsAnswer { get; init; }

    public bool BigTitle { get; init; }

    public bool CountsUsage { get; init; } = true;

    public string? MatchTitle { get; init; }

    public bool KeepsBarOpen { get; init; }

    public bool TakesArgument { get; init; }

    public string? RankingTitle { get; init; }

    public string? RevealPath { get; init; }

    public bool Destructive { get; init; }

    public int BeatMs { get; init; } = 150;

    public bool Mono { get; init; }

    /// <summary>The feature id the row belongs to (for feature ordering).</summary>
    public string? FeatureKey { get; init; }

    public int FeatureTurn { get; init; } = -1;

    public bool IsRunning { get; set; }

    public string? ReuseText { get; init; }

    private string? _normTitle;

    private string? _normKeywords;

    public string NormTitle => _normTitle ??= BarSearch.Normalize(MatchTitle ?? Title);

    public string NormKeywords => _normKeywords ??= BarSearch.Normalize(Keywords + " " + EnglishTitle);

    public Action<int?> Run { get; init; } = _ => { };

    public Func<IReadOnlyList<BarAction>>? ExtraActions { get; init; }

    public bool NeedsPrompt => ConfirmPrompt is not null || (NumericRange is not null && !NumericIsOptional) || NeedsSetupFeature is not null;

    public BarSource Source => BarSources_Of(Id);

    private static BarSource BarSources_Of(string id) => BarSource.Of(id);
}

/// <summary>Text folding, scoring and ranking (spec 1.6).</summary>
internal static class BarSearch
{
    /// <summary>Folds text: invisible format characters removed, case/diacritic/width-insensitive, single spaces.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var kc = text.Normalize(NormalizationForm.FormKC);
        var sb = new StringBuilder(kc.Length);
        foreach (var rune in kc.Normalize(NormalizationForm.FormD))
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(rune);
            if (cat == UnicodeCategory.Format || cat == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            sb.Append(rune);
        }

        var lowered = sb.ToString().ToLowerInvariant();
        var parts = lowered.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts);
    }

    /// <summary>Score of a row for a query; null = no match.</summary>
    public static int? Score(string normTitle, string normKeywords, string normQuery)
    {
        if (normQuery.Length == 0)
        {
            return 0;
        }

        var haystack = normKeywords.Length > 0 ? normTitle + " " + normKeywords : normTitle;
        var words = haystack.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var total = 0;
        foreach (var token in normQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var best = TokenScore(words, haystack, token);
            if (best is null)
            {
                return null;
            }

            total += best.Value;
        }

        if (normTitle == normQuery)
        {
            total += 1200;
        }
        else if (normTitle.StartsWith(normQuery, StringComparison.Ordinal))
        {
            total += 900;
        }
        else if (normTitle.Contains(normQuery, StringComparison.Ordinal))
        {
            total += 700;
        }
        else if (normKeywords.Length > 0 && normKeywords.Contains(normQuery, StringComparison.Ordinal))
        {
            total += 350;
        }

        return total;
    }

    private static int? TokenScore(string[] words, string haystack, string token)
    {
        var best = 0;
        foreach (var word in words)
        {
            if (word == token)
            {
                return 140;
            }

            if (word.StartsWith(token, StringComparison.Ordinal))
            {
                best = Math.Max(best, 80);
            }
        }

        if (best > 0)
        {
            return best;
        }

        if (haystack.Contains(token, StringComparison.Ordinal))
        {
            return 44;
        }

        var digits = token.All(char.IsDigit);
        if (digits || token.Length < 3)
        {
            return null;
        }

        foreach (var word in words)
        {
            if (word.Length >= token.Length && IsSubsequence(token, word))
            {
                return 24;
            }
        }

        foreach (var word in words)
        {
            if (word.Length == token.Length && OneSwap(token, word))
            {
                return 16;
            }
        }

        if (token.Length >= 4)
        {
            foreach (var word in words)
            {
                if (Math.Abs(word.Length - token.Length) <= 1 && WithinOneEdit(token, word))
                {
                    return 16;
                }
            }
        }

        return null;
    }

    private static bool IsSubsequence(string token, string word)
    {
        var i = 0;
        foreach (var c in word)
        {
            if (i < token.Length && token[i] == c)
            {
                i++;
            }
        }

        return i == token.Length;
    }

    private static bool OneSwap(string a, string b)
    {
        var diff = new List<int>();
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
            {
                diff.Add(i);
                if (diff.Count > 2)
                {
                    return false;
                }
            }
        }

        return diff.Count == 2 && diff[1] == diff[0] + 1 && a[diff[0]] == b[diff[1]] && a[diff[1]] == b[diff[0]];
    }

    private static bool WithinOneEdit(string a, string b)
    {
        if (a == b)
        {
            return true;
        }

        if (a.Length == b.Length)
        {
            var diff = 0;
            for (var i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    diff++;
                }
            }

            return diff == 1 || OneSwap(a, b);
        }

        var (s, l) = a.Length < b.Length ? (a, b) : (b, a);
        var si = 0;
        var li = 0;
        var skipped = false;
        while (si < s.Length && li < l.Length)
        {
            if (s[si] == l[li])
            {
                si++;
                li++;
            }
            else if (!skipped)
            {
                skipped = true;
                li++;
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>One candidate to rank.</summary>
    public sealed class Candidate
    {
        public int Index;
        public string NormTitle = "";
        public string NormKeywords = "";
        public int Priority;
        public int Boost;
    }

    /// <summary>Ranks candidates: priority desc, tier desc, score desc, original position asc.</summary>
    public static List<int> Rank(IReadOnlyList<Candidate> candidates, string normQuery)
    {
        var matches = new List<(Candidate C, int Tier, int Score)>();
        foreach (var c in candidates)
        {
            var s = Score(c.NormTitle, c.NormKeywords, normQuery);
            if (s is null)
            {
                continue;
            }

            var tier = c.NormTitle == normQuery ? 5
                : c.NormTitle.StartsWith(normQuery, StringComparison.Ordinal) ? 4
                : c.NormTitle.Contains(normQuery, StringComparison.Ordinal) ? 3
                : c.NormKeywords.Contains(normQuery, StringComparison.Ordinal) ? 2 : 1;
            matches.Add((c, tier, s.Value + c.Boost));
        }

        return matches
            .OrderByDescending(m => m.C.Priority)
            .ThenByDescending(m => m.Tier)
            .ThenByDescending(m => m.Score)
            .ThenBy(m => m.C.Index)
            .Select(m => m.C.Index)
            .ToList();
    }

    /// <summary>Indexes of title characters to highlight for the typed query.</summary>
    public static HashSet<int> Highlight(string title, string query)
    {
        var result = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(query) || title.Length == 0)
        {
            return result;
        }

        var lower = title.ToLowerInvariant();
        foreach (var token in query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var at = -1;
            for (var i = 0; i + token.Length <= lower.Length; i++)
            {
                if (string.CompareOrdinal(lower, i, token, 0, token.Length) == 0 && (i == 0 || !char.IsLetterOrDigit(lower[i - 1])))
                {
                    at = i;
                    break;
                }
            }

            if (at < 0)
            {
                at = lower.IndexOf(token, StringComparison.Ordinal);
            }

            if (at >= 0)
            {
                for (var k = 0; k < token.Length; k++)
                {
                    result.Add(at + k);
                }

                continue;
            }

            var pos = 0;
            var picked = new List<int>();
            foreach (var ch in token)
            {
                var found = lower.IndexOf(ch, pos);
                if (found < 0)
                {
                    picked.Clear();
                    break;
                }

                picked.Add(found);
                pos = found + 1;
            }

            foreach (var p in picked)
            {
                result.Add(p);
            }
        }

        return result;
    }

    /// <summary>Splits "volume 25%" into ("volume", 25) when the last token is a number of 1-4 digits.</summary>
    public static (string Text, int? Number) SplitTrailingNumber(string query)
    {
        var tokens = query.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
        {
            return (query.Trim(), null);
        }

        var last = tokens[^1].TrimEnd('%');
        if (last.Length is >= 1 and <= 4 && last.All(char.IsAsciiDigit) && int.TryParse(last, out var n))
        {
            return (string.Join(' ', tokens[..^1]), n);
        }

        return (query.Trim(), null);
    }
}
