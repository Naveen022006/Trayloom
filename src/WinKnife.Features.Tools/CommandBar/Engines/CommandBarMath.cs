// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WinKnife.Features.Tools;

/// <summary>Result of a calculator answer.</summary>
/// <param name="Display">The big title (formatted, locale grouping).</param>
/// <param name="Reusable">The expression Tab puts back into the field (plain, ungrouped, locale decimal, negatives in parentheses).</param>
/// <param name="Subtitle">"Return copies" or "&lt;expr completed&gt; · Return copies" when brackets were auto-closed.</param>
internal sealed record MathAnswer(string Display, string Reusable, string Subtitle);

/// <summary>Strict calculator (spec 1.7.1): recursive descent, percent semantics, functions, virtual closing brackets.</summary>
internal static class CommandBarMath
{
    private sealed class Reject : Exception
    {
    }

    private enum Kind
    {
        Num,
        Const,
        Func,
        Op,
        LParen,
        RParen,
        Pct,
    }

    private readonly record struct Tok(Kind Kind, double Value = 0, char Op = '\0', string Name = "");

    private readonly record struct Val(double V, double? Pct);

    private static readonly HashSet<string> Functions = ["sqrt", "abs", "sin", "cos", "tan", "asin", "acos", "atan", "ln", "log", "log10", "exp", "floor", "ceil", "round"];

    private static readonly HashSet<string> OfWords = ["of", "de", "da", "do", "von", "di", "del", "dal"];

    private static readonly Regex DateLooking = new(@"^\d{1,4}(?:[-/:]\d{1,4}){1,2}$", RegexOptions.Compiled);

    /// <summary>Evaluates when the whole text is a calculation; null otherwise.</summary>
    public static MathAnswer? Evaluate(string text, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var typed = text.Trim();
        if (typed.EndsWith('='))
        {
            typed = typed[..^1].TrimEnd();
        }

        if (typed.Length == 0 || typed.Length > 120 || typed.Contains('=') || LooksLikeDateOrTime(typed))
        {
            return null;
        }

        try
        {
            var tokens = Tokenize(typed, culture, out var closers);
            var parser = new Parser(tokens);
            var result = parser.Run();
            if (!parser.HasIntent || !double.IsFinite(result.V))
            {
                return null;
            }

            var v = result.V;
            var shown = SignificantRounded(v);
            if (!double.IsFinite(shown))
            {
                return null;
            }

            var reusableNumber = v.ToString("R", culture);
            var reusable = v < 0 ? "(" + reusableNumber + ")" : reusableNumber;
            var subtitle = closers.Length > 0 ? typed + closers + " · Return copies" : "Return copies";
            return new MathAnswer(Format(shown, culture), reusable, subtitle);
        }
        catch (Reject)
        {
            return null;
        }
    }

    /// <summary>Parses one numeric token (digits with ., separators) following the locale rules of spec 1.7.1. Null when invalid.</summary>
    public static double? ParseNumber(string text, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (text.Length == 0)
        {
            return null;
        }

        var decSep = culture.NumberFormat.NumberDecimalSeparator;
        var dec = decSep.Length == 1 && decSep[0] is '.' or ',' ? decSep[0] : '.';
        var alt = dec == '.' ? ',' : '.';
        var cd = text.Count(c => c == '.');
        var cm = text.Count(c => c == ',');
        string normalized;
        if (cd == 0 && cm == 0)
        {
            normalized = text;
        }
        else if (cd > 0 && cm > 0)
        {
            var last = Math.Max(text.LastIndexOf('.'), text.LastIndexOf(','));
            var decimalChar = text[last];
            var groupChar = decimalChar == '.' ? ',' : '.';
            var sb = new StringBuilder();
            foreach (var c in text)
            {
                if (c == groupChar)
                {
                    continue;
                }

                sb.Append(c == decimalChar ? '.' : c);
            }

            normalized = sb.ToString();
        }
        else
        {
            var sepChar = cd > 0 ? '.' : ',';
            var count = Math.Max(cd, cm);
            if (sepChar == dec)
            {
                if (count > 1)
                {
                    return null;
                }

                normalized = text.Replace(sepChar, '.');
            }
            else
            {
                var pattern = new Regex("^[1-9][0-9]{0,2}(?:" + Regex.Escape(alt.ToString()) + "[0-9]{3})+$");
                if (pattern.IsMatch(text))
                {
                    normalized = text.Replace(alt.ToString(), string.Empty);
                }
                else if (count == 1)
                {
                    normalized = text.Replace(alt, '.');
                }
                else
                {
                    return null;
                }
            }
        }

        return double.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : null;
    }

    private static bool LooksLikeDateOrTime(string text)
    {
        if (!DateLooking.IsMatch(text))
        {
            return false;
        }

        var separators = text.Count(c => c is '-' or '/' or ':');
        return separators == 2 || text.Contains(':');
    }

    private static double SignificantRounded(double v)
    {
        if (v == 0 || !double.IsFinite(v))
        {
            return v;
        }

        var magnitude = (int)Math.Floor(Math.Log10(Math.Abs(v)));
        var digits = Math.Min(15, Math.Max(10, magnitude + 3));
        var scale = digits - 1 - magnitude;
        if (scale > 300 || scale < -300)
        {
            return v;
        }

        var p = Math.Pow(10, scale);
        var rounded = Math.Round(v * p, MidpointRounding.ToEven) / p;
        return double.IsFinite(rounded) ? rounded : v;
    }

    private static string Format(double v, CultureInfo culture)
    {
        if (v == 0)
        {
            return "0";
        }

        var a = Math.Abs(v);
        return a >= 1e12 || a < 1e-6 ? v.ToString("0.#######e0", culture) : v.ToString("#,##0.########", culture);
    }

    private static List<Tok> Tokenize(string s, CultureInfo culture, out string closers)
    {
        var tokens = new List<Tok>();
        var stack = new Stack<char>();
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < s.Length && char.IsAsciiDigit(s[i + 1])))
            {
                var j = i;
                while (j < s.Length && (char.IsAsciiDigit(s[j]) || ((s[j] is '.' or ',') && j + 1 < s.Length && char.IsAsciiDigit(s[j + 1]))))
                {
                    j++;
                }

                var mantissa = ParseNumber(s[i..j], culture) ?? throw new Reject();
                if (j < s.Length && (s[j] is 'e' or 'E'))
                {
                    var k = j + 1;
                    if (k < s.Length && (s[k] is '+' or '-'))
                    {
                        k++;
                    }

                    if (k < s.Length && char.IsAsciiDigit(s[k]))
                    {
                        while (k < s.Length && char.IsAsciiDigit(s[k]))
                        {
                            k++;
                        }

                        var exponent = int.Parse(s[(j + 1)..k], CultureInfo.InvariantCulture);
                        mantissa *= Math.Pow(10, exponent);
                        j = k;
                    }
                }

                if (!double.IsFinite(mantissa))
                {
                    throw new Reject();
                }

                tokens.Add(new Tok(Kind.Num, mantissa));
                i = j;
                continue;
            }

            switch (c)
            {
                case '+' or '-' or '*' or '/' or '^':
                    tokens.Add(new Tok(Kind.Op, Op: c));
                    i++;
                    continue;
                case '−':
                    tokens.Add(new Tok(Kind.Op, Op: '-'));
                    i++;
                    continue;
                case '×':
                    tokens.Add(new Tok(Kind.Op, Op: '*'));
                    i++;
                    continue;
                case '÷':
                    tokens.Add(new Tok(Kind.Op, Op: '/'));
                    i++;
                    continue;
                case '%':
                    tokens.Add(new Tok(Kind.Pct));
                    i++;
                    continue;
                case '(' or '[':
                    stack.Push(c == '(' ? ')' : ']');
                    tokens.Add(new Tok(Kind.LParen));
                    i++;
                    continue;
                case ')' or ']':
                    if (stack.Count == 0 || stack.Pop() != c)
                    {
                        throw new Reject();
                    }

                    tokens.Add(new Tok(Kind.RParen));
                    i++;
                    continue;
            }

            if (char.IsLetter(c))
            {
                var j = i;
                while (j < s.Length && char.IsLetter(s[j]))
                {
                    j++;
                }

                var word = s[i..j].ToLowerInvariant();
                if (word == "log" && j + 1 < s.Length && s[j] == '1' && s[j + 1] == '0')
                {
                    word = "log10";
                    j += 2;
                }

                i = j;
                if (word == "x")
                {
                    tokens.Add(new Tok(Kind.Op, Op: '*'));
                }
                else if (OfWords.Contains(word))
                {
                    tokens.Add(new Tok(Kind.Op, Op: 'o'));
                }
                else if (word is "pi" or "π")
                {
                    tokens.Add(new Tok(Kind.Const, Math.PI));
                }
                else if (word == "e")
                {
                    tokens.Add(new Tok(Kind.Const, Math.E));
                }
                else if (Functions.Contains(word))
                {
                    tokens.Add(new Tok(Kind.Func, Name: word));
                }
                else
                {
                    throw new Reject();
                }

                continue;
            }

            throw new Reject();
        }

        var sbClosers = new StringBuilder();
        foreach (var closer in stack)
        {
            sbClosers.Append(closer);
            tokens.Add(new Tok(Kind.RParen));
        }

        closers = sbClosers.ToString();
        return tokens;
    }

    private sealed class Parser
    {
        private readonly List<Tok> _t;
        private int _pos;
        private int _depth;

        public Parser(List<Tok> tokens) => _t = tokens;

        public bool HasIntent { get; private set; }

        public Val Run()
        {
            var v = Expr();
            if (_pos != _t.Count)
            {
                throw new Reject();
            }

            return v;
        }

        private Tok? Peek() => _pos < _t.Count ? _t[_pos] : null;

        private static double Check(double v) => double.IsFinite(v) ? v : throw new Reject();

        private Val Expr()
        {
            if (++_depth > 32)
            {
                throw new Reject();
            }

            var left = Term();
            while (Peek() is { Kind: Kind.Op, Op: '+' or '-' } op)
            {
                _pos++;
                var right = Term();
                HasIntent = true;
                var amount = right.Pct is { } p ? left.V * p / 100 : right.V;
                left = new Val(Check(op.Op == '+' ? left.V + amount : left.V - amount), null);
            }

            _depth--;
            return left;
        }

        private Val Term()
        {
            var left = Factor();
            while (true)
            {
                var next = Peek();
                if (next is null)
                {
                    return left;
                }

                var t = next.Value;
                if (t.Kind == Kind.Op && t.Op is '*' or '/' or 'o')
                {
                    _pos++;
                    var right = Factor();
                    HasIntent = true;
                    switch (t.Op)
                    {
                        case '*':
                            left = new Val(Check(left.V * right.V), null);
                            break;
                        case '/':
                            if (right.V == 0)
                            {
                                throw new Reject();
                            }

                            left = new Val(Check(left.V / right.V), null);
                            break;
                        default:
                            if (left.Pct is not { } pct)
                            {
                                throw new Reject();
                            }

                            left = new Val(Check(pct / 100 * right.V), null);
                            break;
                    }

                    continue;
                }

                var prev = _t[_pos - 1];
                var startsPrimary = t.Kind is Kind.Const or Kind.Func or Kind.LParen;
                var endsOperand = prev.Kind is Kind.Num or Kind.Const or Kind.RParen;
                if (startsPrimary && endsOperand)
                {
                    HasIntent = true;
                    var right = Factor();
                    left = new Val(Check(left.V * right.V), null);
                    continue;
                }

                return left;
            }
        }

        private Val Factor()
        {
            if (++_depth > 32)
            {
                throw new Reject();
            }

            Val result;
            if (Peek() is { Kind: Kind.Op, Op: '+' or '-' } sign)
            {
                _pos++;
                var inner = Factor();
                result = new Val(sign.Op == '-' ? -inner.V : inner.V, null);
            }
            else
            {
                result = Power();
            }

            _depth--;
            return result;
        }

        private Val Power()
        {
            var b = Primary();
            if (Peek() is { Kind: Kind.Op, Op: '^' })
            {
                _pos++;
                var e = Factor();
                HasIntent = true;
                return new Val(Check(Math.Pow(b.V, e.V)), null);
            }

            return b;
        }

        private Val Primary()
        {
            var next = Peek() ?? throw new Reject();
            _pos++;
            Val v;
            switch (next.Kind)
            {
                case Kind.Num:
                case Kind.Const:
                    v = new Val(next.Value, null);
                    break;
                case Kind.LParen:
                    v = Expr();
                    if (Peek() is not { Kind: Kind.RParen })
                    {
                        throw new Reject();
                    }

                    _pos++;
                    break;
                case Kind.Func:
                    if (Peek() is not { Kind: Kind.LParen })
                    {
                        throw new Reject();
                    }

                    _pos++;
                    var arg = Expr();
                    if (Peek() is not { Kind: Kind.RParen })
                    {
                        throw new Reject();
                    }

                    _pos++;
                    HasIntent = true;
                    v = new Val(Apply(next.Name, arg.V), null);
                    break;
                default:
                    throw new Reject();
            }

            if (Peek() is { Kind: Kind.Pct })
            {
                _pos++;
                v = new Val(v.V / 100, v.V);
            }

            return v;
        }

        private static double Apply(string name, double x) => Check(name switch
        {
            "sqrt" => Math.Sqrt(x),
            "abs" => Math.Abs(x),
            "sin" => Math.Sin(x),
            "cos" => Math.Cos(x),
            "tan" => Math.Tan(x),
            "asin" => Math.Asin(x),
            "acos" => Math.Acos(x),
            "atan" => Math.Atan(x),
            "ln" => Math.Log(x),
            "log" or "log10" => Math.Log10(x),
            "exp" => Math.Exp(x),
            "floor" => Math.Floor(x),
            "ceil" => Math.Ceiling(x),
            "round" => Math.Round(x, MidpointRounding.AwayFromZero),
            _ => throw new Reject(),
        });
    }
}
