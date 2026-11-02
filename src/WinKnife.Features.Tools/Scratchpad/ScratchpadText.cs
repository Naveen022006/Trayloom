// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.RegularExpressions;

namespace WinKnife.Features.Tools;

/// <summary>A text edit result: the new text and selection (UTF-16 offsets).</summary>
internal readonly record struct PadEdit(string Text, int SelStart, int SelLength);

/// <summary>The nine Markdown marks of the format bar, in button order.</summary>
internal enum PadMark
{
    Bold,
    Italic,
    Strikethrough,
    Heading,
    Bullet,
    Numbered,
    Quote,
    Code,
    Link,
}

/// <summary>Pure text functions of the scratchpad editor (Vorssaint <c>PlainTextEditor</c> / <c>PlainTextLineMover</c> / marks).</summary>
internal static class ScratchpadText
{
    private static readonly Regex ListMarker = new(@"^([ \t]*(?:>[ \t]*)*)([-*+]|\d+[.)])([ \t]+)(\[[ xX]?\](?:[ \t]+|$))?", RegexOptions.Compiled);
    private static readonly Regex TaskContext = new(@"^[\s>]*[-*+][ \t]+\[( |x|X)?\]([ \t]|$)", RegexOptions.Compiled);

    // ------------------------------------------------------------------------------------------------ lines

    /// <summary>A line: start offset, content length and terminator length (0, 1 or 2).</summary>
    internal readonly record struct Line(int Start, int Content, int Terminator)
    {
        public int End => Start + Content;

        public int FullEnd => Start + Content + Terminator;
    }

    /// <summary>Splits into lines handling \n, \r\n and \r; the text always yields at least one line.</summary>
    internal static List<Line> Lines(string text)
    {
        var lines = new List<Line>();
        var start = 0;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\n' || c == '\r')
            {
                var term = c == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
                lines.Add(new Line(start, i - start, term));
                i += term;
                start = i;
            }
            else
            {
                i++;
            }
        }

        lines.Add(new Line(start, text.Length - start, 0));
        return lines;
    }

    /// <summary>The terminator the document uses (CRLF when present, else LF, else the platform default).</summary>
    internal static string Terminator(string text) => text.Contains("\r\n") ? "\r\n" : text.Contains('\n') ? "\n" : text.Contains('\r') ? "\r" : "\r\n";

    private static int LineIndexAt(List<Line> lines, int offset)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (offset <= lines[i].End || (i + 1 < lines.Count && offset < lines[i + 1].Start))
            {
                return i;
            }
        }

        return lines.Count - 1;
    }

    // ------------------------------------------------------------------------------------------------ marks

    /// <summary>Applies a Markdown mark to the selection ("types what a user would type").</summary>
    internal static PadEdit ApplyMark(PadMark mark, string text, int selStart, int selLength)
    {
        selStart = Math.Clamp(selStart, 0, text.Length);
        selLength = Math.Clamp(selLength, 0, text.Length - selStart);
        return mark switch
        {
            PadMark.Bold => Wrap(text, selStart, selLength, "**"),
            PadMark.Italic => Wrap(text, selStart, selLength, "*"),
            PadMark.Strikethrough => Wrap(text, selStart, selLength, "~~"),
            PadMark.Code => Wrap(text, selStart, selLength, "`"),
            PadMark.Heading => Heading(text, selStart, selLength),
            PadMark.Bullet => Prefix(text, selStart, selLength, numbered: false, "- "),
            PadMark.Numbered => Prefix(text, selStart, selLength, numbered: true, null),
            PadMark.Quote => Prefix(text, selStart, selLength, numbered: false, "> "),
            _ => Link(text, selStart, selLength),
        };
    }

    private static PadEdit Wrap(string text, int start, int length, string mark)
    {
        var before = start >= mark.Length && string.CompareOrdinal(text, start - mark.Length, mark, 0, mark.Length) == 0;
        var after = start + length + mark.Length <= text.Length && string.CompareOrdinal(text, start + length, mark, 0, mark.Length) == 0;
        // "*" must not unwrap the inside of "**".
        if (mark == "*" && before && after && start >= 2 && text[start - 2] == '*' && start + length + 1 < text.Length && text[start + length + 1] == '*')
        {
            before = false;
        }

        if (before && after)
        {
            var unwrapped = text[..(start - mark.Length)] + text.Substring(start, length) + text[(start + length + mark.Length)..];
            return new PadEdit(unwrapped, start - mark.Length, length);
        }

        var inner = text.Substring(start, length);
        var result = text[..start] + mark + inner + mark + text[(start + length)..];
        return new PadEdit(result, start + mark.Length, length);
    }

    private static PadEdit Heading(string text, int start, int length)
    {
        var lines = Lines(text);
        var line = lines[LineIndexAt(lines, start)];
        var content = text.Substring(line.Start, line.Content);
        var hashes = 0;
        while (hashes < content.Length && content[hashes] == '#')
        {
            hashes++;
        }

        var hasMark = hashes is >= 1 and <= 6 && hashes < content.Length && content[hashes] == ' ';
        var body = hasMark ? content[(hashes + 1)..] : content;
        var next = !hasMark ? "# " : hashes == 1 ? "## " : hashes == 2 ? "### " : string.Empty;
        var replaced = next + body;
        var result = text[..line.Start] + replaced + text[line.End..];
        var delta = replaced.Length - content.Length;
        return new PadEdit(result, Math.Max(line.Start, start + delta), length);
    }

    private static PadEdit Prefix(string text, int start, int length, bool numbered, string? prefix)
    {
        var lines = Lines(text);
        var first = LineIndexAt(lines, start);
        var last = LineIndexAt(lines, length > 0 ? start + length - 1 : start);
        var contents = new List<string>();
        for (var i = first; i <= last; i++)
        {
            contents.Add(text.Substring(lines[i].Start, lines[i].Content));
        }

        var numberedRe = new Regex(@"^\d+\.\s");
        var all = contents.All(c => numbered ? numberedRe.IsMatch(c) : c.StartsWith(prefix!, StringComparison.Ordinal));
        var changed = new List<string>();
        for (var i = 0; i < contents.Count; i++)
        {
            var c = contents[i];
            if (all)
            {
                changed.Add(numbered ? numberedRe.Replace(c, string.Empty) : c[prefix!.Length..]);
            }
            else if (numbered)
            {
                changed.Add($"{i + 1}. " + numberedRe.Replace(c, string.Empty));
            }
            else
            {
                changed.Add(c.StartsWith(prefix!, StringComparison.Ordinal) ? c : prefix + c);
            }
        }

        var term = Terminator(text);
        var sb = new System.Text.StringBuilder(text[..lines[first].Start]);
        for (var i = 0; i < changed.Count; i++)
        {
            sb.Append(changed[i]);
            if (i < changed.Count - 1)
            {
                sb.Append(text, lines[first + i].End, lines[first + i].Terminator);
            }
        }

        var oldBlockEnd = lines[last].End;
        var blockStart = lines[first].Start;
        sb.Append(text, oldBlockEnd, text.Length - oldBlockEnd);
        var newBlockLength = changed.Sum(c => c.Length) + Enumerable.Range(0, changed.Count - 1).Sum(i => lines[first + i].Terminator);
        var oldBlockLength = oldBlockEnd - blockStart;
        _ = term;
        var newStart = length == 0 ? Math.Max(blockStart, start + (changed[0].Length - contents[0].Length)) : blockStart;
        var newLength = length == 0 ? 0 : newBlockLength;
        _ = oldBlockLength;
        return new PadEdit(sb.ToString(), newStart, newLength);
    }

    private static PadEdit Link(string text, int start, int length)
    {
        var inner = text.Substring(start, length);
        var result = text[..start] + "[" + inner + "](url)" + text[(start + length)..];
        return new PadEdit(result, start + inner.Length + 3, 3);
    }

    // ------------------------------------------------------------------------------------------------ list continuation

    /// <summary>Shift+Enter: continues a list item (or ends the list on an empty item); otherwise inserts a plain line break.</summary>
    internal static PadEdit LineBreak(string text, int selStart, int selLength)
    {
        selStart = Math.Clamp(selStart, 0, text.Length);
        var term = Terminator(text);
        PadEdit Plain() => new(text[..selStart] + term + text[(selStart + Math.Clamp(selLength, 0, text.Length - selStart))..], selStart + term.Length, 0);
        if (selLength != 0)
        {
            return Plain();
        }

        var lines = Lines(text);
        var line = lines[LineIndexAt(lines, selStart)];
        var content = text.Substring(line.Start, line.Content);
        var match = ListMarker.Match(content);
        var column = selStart - line.Start;
        if (!match.Success || column < match.Length)
        {
            return Plain();
        }

        var hasBox = match.Groups[4].Success && match.Groups[4].Length > 0;
        if (column == content.Length && content[match.Length..].Trim().Length == 0)
        {
            var cleared = text[..line.Start] + text[line.End..];
            return new PadEdit(cleared, line.Start, 0);
        }

        var prefix = match.Groups[1].Value;
        var marker = match.Groups[2].Value;
        var spacing = match.Groups[3].Value;
        var next = marker;
        if (char.IsDigit(marker[0]))
        {
            var delimiter = marker[^1];
            var digits = marker[..^1];
            next = long.TryParse(digits, out var number) && number < long.MaxValue ? (number + 1) + delimiter.ToString() : marker;
        }

        var insertion = term + prefix + next + spacing + (hasBox ? "[ ] " : string.Empty);
        return new PadEdit(text[..selStart] + insertion + text[selStart..], selStart + insertion.Length, 0);
    }

    // ------------------------------------------------------------------------------------------------ task boxes

    /// <summary>True when typing "]" at <paramref name="caret"/> should insert " ]" (completing a task box "- [ ]").</summary>
    internal static bool CompletesTaskBox(string text, int caret)
    {
        var lines = Lines(text);
        var line = lines[LineIndexAt(lines, caret)];
        var before = text.Substring(line.Start, Math.Max(0, caret - line.Start));
        return TaskContext.IsMatch(before + " ]");
    }

    // ------------------------------------------------------------------------------------------------ line mover

    /// <summary>Alt+Up / Alt+Down: moves the selected lines past their neighbour. Null when nothing moves.</summary>
    internal static PadEdit? MoveLines(bool up, string text, int selStart, int selLength)
    {
        var lines = Lines(text);
        var first = LineIndexAt(lines, selStart);
        var last = LineIndexAt(lines, selLength > 0 ? selStart + selLength - 1 : selStart);
        if (up ? first == 0 : last == lines.Count - 1)
        {
            return null;
        }

        var block = (Start: lines[first].Start, End: lines[last].End, Term: lines[last].Terminator);
        var blockContent = text[block.Start..block.End];
        var blockTerm = text.Substring(block.End, block.Term);
        if (up)
        {
            var above = lines[first - 1];
            var aboveContent = text.Substring(above.Start, above.Content);
            var aboveTerm = text.Substring(above.End, above.Terminator);
            var replacement = blockContent + aboveTerm + aboveContent + blockTerm;
            var result = text[..above.Start] + replacement + text[(block.End + block.Term)..];
            var shift = aboveContent.Length + aboveTerm.Length;
            return new PadEdit(result, selStart - shift, selLength);
        }

        var below = lines[last + 1];
        var belowContent = text.Substring(below.Start, below.Content);
        var belowTerm = text.Substring(below.End, below.Terminator);
        var repl = belowContent + blockTerm + blockContent + belowTerm;
        var res = text[..block.Start] + repl + text[below.FullEnd..];
        var down = belowContent.Length + blockTerm.Length;
        return new PadEdit(res, selStart + down, selLength);
    }

    // ------------------------------------------------------------------------------------------------ misc

    /// <summary>Toggles the task box whose "[" is at <paramref name="boxOffset"/>: "[ ]" and "[x]" swap, a bare "[]" becomes "[x]".</summary>
    internal static string ToggleTask(string text, int boxOffset)
    {
        if (boxOffset < 0 || boxOffset >= text.Length || text[boxOffset] != '[')
        {
            return text;
        }

        var close = text.IndexOf(']', boxOffset);
        if (close < 0 || close - boxOffset > 2)
        {
            return text;
        }

        var inner = text.Substring(boxOffset + 1, close - boxOffset - 1);
        var replacement = inner is "" or " " ? "[x]" : "[ ]";
        return text[..boxOffset] + replacement + text[(close + 1)..];
    }

    /// <summary>Suggested file name stem: runs of whitespace collapsed, max 40 chars, invalid path characters replaced.</summary>
    internal static string SafeFileStem(string name)
    {
        var collapsed = Regex.Replace(name, @"\s+", " ").Trim();
        if (collapsed.Length > 40)
        {
            collapsed = collapsed[..40];
        }

        foreach (var c in new[] { '/', ':', '\\', '*', '?', '"', '<', '>', '|' })
        {
            collapsed = collapsed.Replace(c, '-');
        }

        return collapsed.Length == 0 ? "Scratchpad" : collapsed;
    }
}
