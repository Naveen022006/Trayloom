// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Tools;

/// <summary>Block kinds of the reading view (spec 6.1.7).</summary>
internal enum MdKind
{
    Paragraph,
    Heading,
    Bullet,
    Task,
    Ordered,
    Quote,
    Code,
    Rule,
}

/// <summary>One parsed block. <see cref="BoxOffset"/> is the UTF-16 offset of the "[" of a task box in the source.</summary>
internal sealed record MdBlock(MdKind Kind, string Text, int Level = 0, int Depth = 0, int Ordinal = 0, int QuoteDepth = 0, bool Checked = false, int BoxOffset = -1);

/// <summary>Inline text span with its style.</summary>
internal readonly record struct MdSpan(string Text, bool Bold, bool Italic, bool Strike, bool Code, string? Url);

/// <summary>Small Markdown parser (CommonMark subset + strikethrough + task lists) used by the reading view.</summary>
internal static class ScratchpadMarkdown
{
    private static readonly Regex Heading = new(@"^(#{1,6})[ \t]+(.*?)[ \t]*#*[ \t]*$", RegexOptions.Compiled);
    private static readonly Regex Rule = new(@"^ {0,3}([-*_])([ \t]*\1){2,}[ \t]*$", RegexOptions.Compiled);
    private static readonly Regex ListItem = new(@"^([ \t]*)([-*+]|\d+[.)])[ \t]+(.*)$", RegexOptions.Compiled);
    private static readonly Regex TaskBox = new(@"^\[( |x|X)?\]([ \t]+|$)", RegexOptions.Compiled);
    private static readonly Regex Fence = new(@"^[ \t]*(```|~~~)", RegexOptions.Compiled);

    /// <summary>Parses the source into blocks.</summary>
    public static List<MdBlock> Parse(string text)
    {
        var blocks = new List<MdBlock>();
        var lines = ScratchpadText.Lines(text);
        var paragraph = new StringBuilder();
        var paragraphQuote = 0;
        var inCode = false;
        var code = new StringBuilder();

        void FlushParagraph()
        {
            if (paragraph.Length > 0)
            {
                blocks.Add(new MdBlock(paragraphQuote > 0 ? MdKind.Quote : MdKind.Paragraph, paragraph.ToString(), QuoteDepth: paragraphQuote));
                paragraph.Clear();
            }
        }

        foreach (var line in lines)
        {
            var raw = text.Substring(line.Start, line.Content);
            if (inCode)
            {
                if (Fence.IsMatch(raw))
                {
                    inCode = false;
                    blocks.Add(new MdBlock(MdKind.Code, code.ToString().TrimEnd('\n')));
                    code.Clear();
                }
                else
                {
                    code.Append(raw).Append('\n');
                }

                continue;
            }

            if (Fence.IsMatch(raw))
            {
                FlushParagraph();
                inCode = true;
                continue;
            }

            var offset = line.Start;
            var quote = 0;
            var content = raw;
            while (true)
            {
                var trimmed = content.TrimStart(' ', '\t');
                if (trimmed.StartsWith('>'))
                {
                    var skip = content.Length - trimmed.Length + 1;
                    if (skip < content.Length && content[skip] == ' ')
                    {
                        skip++;
                    }

                    offset += skip;
                    content = content[skip..];
                    quote++;
                }
                else
                {
                    break;
                }
            }

            if (content.Trim().Length == 0)
            {
                FlushParagraph();
                continue;
            }

            if (Heading.Match(content) is { Success: true } h)
            {
                FlushParagraph();
                blocks.Add(new MdBlock(MdKind.Heading, h.Groups[2].Value, Level: h.Groups[1].Length, QuoteDepth: quote));
                continue;
            }

            if (Rule.IsMatch(content))
            {
                FlushParagraph();
                blocks.Add(new MdBlock(MdKind.Rule, string.Empty, QuoteDepth: quote));
                continue;
            }

            if (ListItem.Match(content) is { Success: true } li)
            {
                FlushParagraph();
                var depth = li.Groups[1].Value.Replace("\t", "  ").Length / 2;
                var marker = li.Groups[2].Value;
                var rest = li.Groups[3].Value;
                var restOffset = offset + li.Groups[3].Index;
                if (!char.IsDigit(marker[0]) && TaskBox.Match(rest) is { Success: true } tb)
                {
                    var isChecked = tb.Groups[1].Value is "x" or "X";
                    blocks.Add(new MdBlock(MdKind.Task, rest[tb.Length..], Depth: depth, QuoteDepth: quote, Checked: isChecked, BoxOffset: restOffset));
                }
                else if (char.IsDigit(marker[0]))
                {
                    _ = int.TryParse(marker[..^1], out var ordinal);
                    blocks.Add(new MdBlock(MdKind.Ordered, rest, Depth: depth, Ordinal: ordinal, QuoteDepth: quote));
                }
                else
                {
                    blocks.Add(new MdBlock(MdKind.Bullet, rest, Depth: depth, QuoteDepth: quote));
                }

                continue;
            }

            if (paragraph.Length > 0 && paragraphQuote != quote)
            {
                FlushParagraph();
            }

            if (paragraph.Length > 0)
            {
                paragraph.Append(' ');
            }

            paragraph.Append(content.Trim());
            paragraphQuote = quote;
        }

        if (inCode)
        {
            blocks.Add(new MdBlock(MdKind.Code, code.ToString().TrimEnd('\n')));
        }

        FlushParagraph();
        return blocks;
    }

    /// <summary>Parses inline emphasis, code, strikethrough and links.</summary>
    public static List<MdSpan> Inline(string s)
    {
        var spans = new List<MdSpan>();
        Scan(s, false, false, false, null, spans);
        return spans;
    }

    private static void Scan(string s, bool bold, bool italic, bool strike, string? url, List<MdSpan> output)
    {
        var sb = new StringBuilder();
        var i = 0;

        void Flush()
        {
            if (sb.Length > 0)
            {
                output.Add(new MdSpan(sb.ToString(), bold, italic, strike, false, url));
                sb.Clear();
            }
        }

        while (i < s.Length)
        {
            var c = s[i];
            if (c == '\\' && i + 1 < s.Length && char.IsPunctuation(s[i + 1]))
            {
                sb.Append(s[i + 1]);
                i += 2;
                continue;
            }

            if (c == '`')
            {
                var j = s.IndexOf('`', i + 1);
                if (j > i + 1)
                {
                    Flush();
                    output.Add(new MdSpan(s[(i + 1)..j], bold, italic, strike, true, url));
                    i = j + 1;
                    continue;
                }
            }

            if ((c == '*' || c == '_') && i + 1 < s.Length && s[i + 1] == c)
            {
                var token = new string(c, 2);
                var j = s.IndexOf(token, i + 2, StringComparison.Ordinal);
                if (j > i + 2)
                {
                    Flush();
                    Scan(s[(i + 2)..j], true, italic, strike, url, output);
                    i = j + 2;
                    continue;
                }
            }

            if (c == '~' && i + 1 < s.Length && s[i + 1] == '~')
            {
                var j = s.IndexOf("~~", i + 2, StringComparison.Ordinal);
                if (j > i + 2)
                {
                    Flush();
                    Scan(s[(i + 2)..j], bold, italic, true, url, output);
                    i = j + 2;
                    continue;
                }
            }

            if ((c == '*' || c == '_') && i + 1 < s.Length && s[i + 1] != ' ')
            {
                var j = i + 1;
                while ((j = s.IndexOf(c, j)) > 0 && s[j - 1] == ' ')
                {
                    j++;
                }

                if (j > i + 1 && (c == '*' || i == 0 || !char.IsLetterOrDigit(s[i - 1])))
                {
                    Flush();
                    Scan(s[(i + 1)..j], bold, true, strike, url, output);
                    i = j + 1;
                    continue;
                }
            }

            if (c == '[')
            {
                var close = s.IndexOf(']', i + 1);
                if (close > i && close + 1 < s.Length && s[close + 1] == '(')
                {
                    var end = s.IndexOf(')', close + 2);
                    if (end > close)
                    {
                        Flush();
                        Scan(s[(i + 1)..close], bold, italic, strike, s[(close + 2)..end].Trim(), output);
                        i = end + 1;
                        continue;
                    }
                }
            }

            sb.Append(c);
            i++;
        }

        Flush();
    }
}

/// <summary>Builds the "formatted reading" <see cref="FlowDocument"/> from blocks.</summary>
internal static class ScratchpadReading
{
    public static FlowDocument Build(IReadOnlyList<MdBlock> blocks, double size, Action<int> toggleTask, Action<string> openLink)
    {
        var doc = new FlowDocument { FontSize = size, FontFamily = UiKit.TextFont, PagePadding = new Thickness(7, 4, 7, 4), LineHeight = size * 1.35 };
        doc.SetResourceReference(FlowDocument.ForegroundProperty, ThemeKeys.TextPrimary);
        for (var b = 0; b < blocks.Count; b++)
        {
            var block = blocks[b];
            var next = b + 1 < blocks.Count ? blocks[b + 1] : null;
            var sameContainer = next is not null && IsList(block) && IsList(next) || next is not null && block.Kind == MdKind.Quote && next.Kind == MdKind.Quote;
            var p = new Paragraph { Margin = new Thickness(0, 0, 0, sameContainer ? 0 : size * 0.8), LineHeight = size * 1.35 };
            if (block.QuoteDepth > 0 && block.Kind != MdKind.Quote)
            {
                AddRun(p, string.Concat(Enumerable.Repeat("▏ ", block.QuoteDepth)), ThemeKeys.TextSecondary);
            }

            switch (block.Kind)
            {
                case MdKind.Heading:
                    p.FontSize = size + (block.Level == 1 ? 5 : block.Level == 2 ? 3 : 1);
                    p.FontWeight = FontWeights.SemiBold;
                    AddInline(p, block.Text, size, openLink, null);
                    break;
                case MdKind.Bullet:
                    AddRun(p, new string(' ', block.Depth * 2) + "• ", null);
                    AddInline(p, block.Text, size, openLink, null);
                    break;
                case MdKind.Ordered:
                    AddRun(p, new string(' ', block.Depth * 2) + block.Ordinal + ". ", null);
                    AddInline(p, block.Text, size, openLink, null);
                    break;
                case MdKind.Task:
                    AddRun(p, new string(' ', block.Depth * 2), null);
                    p.Inlines.Add(new InlineUIContainer(CheckBox(block, size, toggleTask)) { BaselineAlignment = BaselineAlignment.Center });
                    AddRun(p, " ", null);
                    AddInline(p, block.Text, size, openLink, block.Checked ? ThemeKeys.TextSecondary : null, block.Checked);
                    break;
                case MdKind.Quote:
                    AddRun(p, string.Concat(Enumerable.Repeat("▏ ", Math.Max(1, block.QuoteDepth))), ThemeKeys.TextSecondary);
                    AddInline(p, block.Text, size, openLink, ThemeKeys.TextSecondary);
                    break;
                case MdKind.Code:
                    p.FontFamily = new FontFamily("Cascadia Mono, Consolas");
                    p.FontSize = Math.Max(9, size - 1);
                    p.Background = SubtleBackground();
                    p.Padding = new Thickness(6, 4, 6, 4);
                    p.Inlines.Add(new Run(block.Text));
                    break;
                case MdKind.Rule:
                    AddRun(p, new string('─', 24), ThemeKeys.TextSecondary);
                    break;
                default:
                    AddInline(p, block.Text, size, openLink, null);
                    break;
            }

            doc.Blocks.Add(p);
        }

        return doc;
    }

    private static bool IsList(MdBlock b) => b.Kind is MdKind.Bullet or MdKind.Task or MdKind.Ordered;

    private static Brush SubtleBackground()
    {
        var color = (Application.Current?.TryFindResource(ThemeKeys.TextPrimary) as SolidColorBrush)?.Color ?? Colors.Gray;
        return new SolidColorBrush(Color.FromArgb(18, color.R, color.G, color.B));
    }

    private static void AddRun(Paragraph p, string text, string? brushKey)
    {
        var run = new Run(text);
        if (brushKey is not null)
        {
            run.SetResourceReference(TextElement.ForegroundProperty, brushKey);
        }

        p.Inlines.Add(run);
    }

    private static void AddInline(Paragraph p, string text, double size, Action<string> openLink, string? brushKey, bool struck = false)
    {
        foreach (var span in ScratchpadMarkdown.Inline(text))
        {
            Inline run = new Run(span.Text);
            if (span.Bold)
            {
                run.FontWeight = FontWeights.Bold;
            }

            if (span.Italic)
            {
                run.FontStyle = FontStyles.Italic;
            }

            if (span.Strike || struck)
            {
                run.TextDecorations = TextDecorations.Strikethrough;
            }

            if (span.Code)
            {
                run.FontFamily = new FontFamily("Cascadia Mono, Consolas");
                run.FontSize = Math.Max(9, size - 1);
                run.Background = SubtleBackground();
            }

            if (span.Url is { } url)
            {
                if (SafeLinks.IsWebUrl(url, out _))
                {
                    var link = new Hyperlink(run) { Cursor = Cursors.Hand, ToolTip = url };
                    link.SetResourceReference(TextElement.ForegroundProperty, ThemeKeys.Accent);
                    link.TextDecorations = null;
                    link.Click += (_, _) => openLink(url);
                    run = link;
                }
                else
                {
                    // Only http, https and mailto are links; anything else (file:, ms-settings:, a path) is shown as plain text.
                    run.ToolTip = url;
                    if (brushKey is not null)
                    {
                        run.SetResourceReference(TextElement.ForegroundProperty, brushKey);
                    }
                }
            }
            else if (brushKey is not null)
            {
                run.SetResourceReference(TextElement.ForegroundProperty, brushKey);
            }

            p.Inlines.Add(run);
        }
    }

    private static FrameworkElement CheckBox(MdBlock block, double size, Action<int> toggle)
    {
        var diameter = size + 3;
        var grid = new Grid { Width = diameter, Height = diameter, Cursor = Cursors.Hand, Background = Brushes.Transparent };
        if (block.Checked)
        {
            var disc = new Ellipse();
            disc.SetResourceReference(Shape.FillProperty, ThemeKeys.Accent);
            grid.Children.Add(disc);
            grid.Children.Add(new TextBlock { Text = "", FontFamily = UiKit.IconFont, FontSize = diameter * 0.6, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        }
        else
        {
            var ring = new Ellipse { StrokeThickness = 1.4, Margin = new Thickness(1) };
            ring.SetResourceReference(Shape.StrokeProperty, ThemeKeys.TextTertiary);
            grid.Children.Add(ring);
        }

        grid.ToolTip = block.Checked ? Loc.T("Done") : Loc.T("Not done");
        var offset = block.BoxOffset;
        grid.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            toggle(offset);
        };
        return grid;
    }
}
