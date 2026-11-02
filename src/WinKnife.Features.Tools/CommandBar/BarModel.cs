// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Text;
using System.Windows.Media;
using WinKnife.Core;

namespace WinKnife.Features.Tools;

/// <summary>Verbatim English strings of the Command Bar (spec 1.3).</summary>
internal static class BarStrings
{
    public static  string PageTitle => Loc.T("Command Bar");
    public static  string SearchPlaceholder => Loc.T("Type what you want to do");
    public static  string Suggestions => Loc.T("Suggestions");
    public static  string NoResultsTitle => Loc.T("Nothing here by that name.");
    public static  string NoResultsAction => Loc.T("See suggestions");
    public static  string ArgumentHint => Loc.T("Return applies · Esc goes back");
    public static  string ConfirmHint => Loc.T("Return confirms · Esc cancels");
    public static  string KindApp => Loc.T("App");
    public static  string KindClipboard => Loc.T("Paste from history");
    public static  string KindSnippet => Loc.T("Insert snippet");
    public static  string KindWindow => Loc.T("Window");
    public static  string KindMenu => Loc.T("Menu command");
    public static  string KindEmoji => Loc.T("Emoji");
    public static  string KindFolder => Loc.T("Folder");
    public static  string KindAnswer => Loc.T("Answer");
    public static  string KindSelection => Loc.T("Selection");
    public static  string KindLink => Loc.T("Your shortcut");
    public static  string SettingsSubtitle => Loc.T("WinKnife Settings");
    public static  string PanesSubtitle => Loc.T("Windows Settings pages");
    public static  string ActionsTitle => Loc.T("Actions");
    public static  string Pin => Loc.T("Pin to the top");
    public static  string Unpin => Loc.T("Unpin");
    public static  string Name => Loc.T("Give it your own name");
    public static  string Rename => Loc.T("Change the name it answers to");
    public static  string Hide => Loc.T("Never show this");
    public static  string Forget => Loc.T("Forget how often I use this");
    public static  string AliasPlaceholder => Loc.T("The name you call it");
    public static  string Shortcut => Loc.T("Give it a shortcut");
    public static  string ShortcutChange => Loc.T("Change the shortcut");
    public static  string ShortcutRemove => Loc.T("Take the shortcut off");
    public static  string ShortcutCaptureHint => Loc.T("Press the keys you want · Delete clears it · Esc goes back");
    public static  string DragHint => Loc.T("Drag to move · Double-click to recenter");
    public static  string TryLabel => Loc.T("Try");
    public static  string Everything => Loc.T("Everything it can do");
    public static  string PastFromHistory => Loc.T("Paste from history");
    public static  string ShowInExplorer => Loc.T("Show in File Explorer");
    public static  string OpenInBrowser => Loc.T("Open in browser");
    public static  string Cancel => Loc.T("Cancel");
    public static  string Confirm => Loc.T("Confirm");
    public static  string ReturnCopies => Loc.T("Return copies");
    public static  string Continue => Loc.T("Continue");

    public static string NeedsSetup(string feature) => Loc.F("Turn on {0} in Settings", feature);

    public static string AliasTaken(string title) => Loc.F("{0} already answers to that", title);
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
    public static BarSource[] All => _loc_All.Value;

    private static readonly LocValue<BarSource[]> _loc_All = new(() => [
        new("actions", Loc.T("WinKnife actions"), "\uE945", "", 0, true, true),
        new("apps", Loc.T("Apps"), "\uE80A", "app.", 80, true, true),
        new("menus", Loc.T("Menu commands of the app in front"), "\uE700", "menu.", -80, false, false),
        new("windows", Loc.T("Open windows"), "\uE737", "window.", 0, true, false),
        new("quitApps", Loc.T("Quit an app"), "\uE711", "quit.", 0, false, true),
        new("uninstallApps", Loc.T("Uninstaller"), "\uE74D", "uninstall.", 0, false, false),
        new("settingsPages", Loc.T("Settings pages"), "\uE713", "settings.", -40, true, true),
        new("macSettings", Loc.T("Windows Settings pages"), "\uE713", "macsettings.", 0, true, true),
        new("snippets", Loc.T("Snippets"), "\uE8D2", "snippet.", 0, true, true),
        new("clipboard", Loc.T("Clipboard history"), "\uE77F", "clipboard.", 0, false, false),
        new("emoji", Loc.T("Emoji"), "\uE76E", "emoji.", 0, false, true),
        new("folders", Loc.T("Folders"), "\uE8B7", "folder.", 0, true, true),
        new("answers", Loc.T("Answers about this PC"), "\uE946", "answer.", 0, true, true),
        new("calculator", Loc.T("Sums and conversions"), "\uE8EF", "", 0, true, true),
        new("selection", Loc.T("What is selected"), "\uE8E9", "selection.", 0, false, false),
        new("links", Loc.T("Your shortcuts"), "\uE8A4", "link.", 0, true, true),
        new("files", Loc.T("Files"), "\uE721", "file.", -40, false, false),
        new("killProcess", Loc.T("Kill Process"), "\uEA39", "kill.", 0, false, false),
    ]);

    /// <summary>Category chip order.</summary>
    public static readonly string[] ChipOrder = ["actions", "apps", "clipboard", "windows", "menus", "settingsPages", "macSettings", "snippets", "emoji", "folders", "links"];

    /// <summary>Titles of the browse headings.</summary>
    public static string CategoryTitle(string raw) => raw switch
    {
        "actions" => Loc.T("WinKnife actions"),
        "apps" => Loc.T("Apps"),
        "menus" => KindMenuTitle,
        "windows" => Loc.T("Open windows"),
        "settingsPages" => Loc.T("Settings pages"),
        "macSettings" => Loc.T("Windows Settings pages"),
        "snippets" => Loc.T("Snippets"),
        "clipboard" => Loc.T("Clipboard history"),
        "emoji" => Loc.T("Emoji"),
        "folders" => Loc.T("Folders"),
        "links" => Loc.T("Your shortcuts"),
        "killProcess" => Loc.T("Kill Process"),
        _ => raw,
    };

    private static  string KindMenuTitle => Loc.T("Menu command");

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
