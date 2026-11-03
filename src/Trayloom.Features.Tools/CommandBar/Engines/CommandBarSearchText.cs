// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Text;

namespace Trayloom.Features.Tools;

/// <summary>Text folding used by every search of the bar (spec 1.6.1): case-, diacritic- and width-insensitive, locale independent.</summary>
internal static class CommandBarSearchText
{
    /// <summary>Removes invisible format characters, folds (FormKC, no marks, invariant lower case) and collapses whitespace.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value >= 0xAD && Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format)
            {
                continue;
            }

            sb.Append(rune.ToString());
        }

        string folded;
        try
        {
            folded = sb.ToString().Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            folded = sb.ToString();
        }

        var decomposed = folded.Normalize(NormalizationForm.FormD);
        sb.Clear();
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        var lower = sb.ToString().ToLowerInvariant();
        sb.Clear();
        var pendingSpace = false;
        foreach (var ch in lower)
        {
            if (char.IsWhiteSpace(ch) || ch == '　')
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }
}
