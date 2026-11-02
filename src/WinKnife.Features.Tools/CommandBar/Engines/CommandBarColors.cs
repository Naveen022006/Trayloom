// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace WinKnife.Features.Tools;

/// <summary>A parsed colour (0..1 channels).</summary>
internal readonly record struct ColorValue(double R, double G, double B, double A);

/// <summary>Colour parsing and conversion (spec 1.7.4).</summary>
internal static class CommandBarColors
{
    private static readonly string[] ToWords = ["to", "in", "into", "as", "em", "para", "pra", "en", "a", "nach", "zu", "su", "->", ">", "→"];

    private static readonly string[] Targets = ["hex", "rgb", "rgba", "hsl", "hsla", "swift", "swiftui"];

    private static readonly Regex Hex = new("^#(?:[0-9a-f]{3}|[0-9a-f]{4}|[0-9a-f]{6}|[0-9a-f]{8})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Functional = new(@"^(rgba?|hsla?)\(\s*(.*?)\s*\)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SwiftColor = new(@"^Color\(\s*red:\s*([0-9.]+)\s*,\s*green:\s*([0-9.]+)\s*,\s*blue:\s*([0-9.]+)\s*(?:,\s*opacity:\s*([0-9.]+)\s*)?\)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Number = new(@"^[+-]?(?:\d+\.?\d*|\.\d+)(%|deg)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Parses #RGB, #RGBA, #RRGGBB, #RRGGBBAA, rgb(), rgba(), hsl(), hsla(), Color(red:green:blue:); null otherwise.</summary>
    public static ColorValue? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || Encoding.UTF8.GetByteCount(text) > 96)
        {
            return null;
        }

        var s = text.Trim();
        if (Hex.IsMatch(s))
        {
            return ParseHex(s[1..]);
        }

        var swift = SwiftColor.Match(s);
        if (swift.Success)
        {
            var r = Unit(swift.Groups[1].Value);
            var g = Unit(swift.Groups[2].Value);
            var b = Unit(swift.Groups[3].Value);
            var a = swift.Groups[4].Success ? Unit(swift.Groups[4].Value) : 1;
            return r is null || g is null || b is null || a is null ? null : Clamp(r.Value, g.Value, b.Value, a.Value);
        }

        var match = Functional.Match(s);
        if (!match.Success)
        {
            return null;
        }

        var name = match.Groups[1].Value.ToLowerInvariant();
        var args = Regex.Split(match.Groups[2].Value.Trim(), @"\s*[,/\s]\s*").Where(a => a.Length > 0).ToArray();
        if (args.Length is not (3 or 4) || args.Any(a => !Number.IsMatch(a)))
        {
            return null;
        }

        double? alpha = 1;
        if (args.Length == 4)
        {
            alpha = Fraction(args[3]);
            if (alpha is null)
            {
                return null;
            }
        }

        if (name.StartsWith("rgb", StringComparison.Ordinal))
        {
            var r = Channel(args[0]);
            var g = Channel(args[1]);
            var b = Channel(args[2]);
            return r is null || g is null || b is null ? null : Clamp(r.Value, g.Value, b.Value, alpha.Value);
        }

        var hue = Hue(args[0]);
        var sat = Percent(args[1]);
        var light = Percent(args[2]);
        if (hue is null || sat is null || light is null)
        {
            return null;
        }

        var (rr, gg, bb) = HslToRgb(hue.Value, Math.Clamp(sat.Value, 0, 1), Math.Clamp(light.Value, 0, 1));
        return Clamp(rr, gg, bb, alpha.Value);
    }

    /// <summary>"&lt;colour&gt; to &lt;hex|rgb|rgba|hsl|hsla|swift|swiftui&gt;" → the formatted value, or null.</summary>
    public static string? Convert(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 3)
        {
            return null;
        }

        var target = tokens[^1].ToLowerInvariant();
        var toWord = CommandBarSearchText.Normalize(tokens[^2]);
        if (Array.IndexOf(Targets, target) < 0 || Array.IndexOf(ToWords, toWord) < 0)
        {
            return null;
        }

        if (Parse(string.Join(' ', tokens.Take(tokens.Length - 2))) is not { } color)
        {
            return null;
        }

        return Format(color, target);
    }

    /// <summary>The WPF colour of a value.</summary>
    public static Color ToColor(ColorValue value) => Color.FromArgb((byte)Byte(value.A), (byte)Byte(value.R), (byte)Byte(value.G), (byte)Byte(value.B));

    private static string Format(ColorValue c, string target)
    {
        var inv = CultureInfo.InvariantCulture;
        var withAlpha = c.A < 1;
        switch (target)
        {
            case "hex":
                return "#" + Byte(c.R).ToString("X2", inv) + Byte(c.G).ToString("X2", inv) + Byte(c.B).ToString("X2", inv) + (withAlpha ? Byte(c.A).ToString("X2", inv) : string.Empty);
            case "rgb" or "rgba":
                var rgb = Byte(c.R).ToString(inv) + ", " + Byte(c.G).ToString(inv) + ", " + Byte(c.B).ToString(inv);
                return target == "rgba" || withAlpha ? "rgba(" + rgb + ", " + Alpha(c.A) + ")" : "rgb(" + rgb + ")";
            case "hsl" or "hsla":
                var (h, s, l) = RgbToHsl(c.R, c.G, c.B);
                var hsl = Math.Round(h, MidpointRounding.AwayFromZero).ToString("0", inv) + ", " + Math.Round(s * 100, MidpointRounding.AwayFromZero).ToString("0", inv) + "%, " + Math.Round(l * 100, MidpointRounding.AwayFromZero).ToString("0", inv) + "%";
                return target == "hsla" || withAlpha ? "hsla(" + hsl + ", " + Alpha(c.A) + ")" : "hsl(" + hsl + ")";
            default:
                var swift = "Color(red: " + c.R.ToString("0.000", inv) + ", green: " + c.G.ToString("0.000", inv) + ", blue: " + c.B.ToString("0.000", inv);
                return swift + (withAlpha ? ", opacity: " + c.A.ToString("0.000", inv) : string.Empty) + ")";
        }
    }

    private static string Alpha(double a) => Math.Round(a, 3).ToString("0.###", CultureInfo.InvariantCulture);

    private static int Byte(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255, MidpointRounding.AwayFromZero);

    private static ColorValue Clamp(double r, double g, double b, double a) => new(Math.Clamp(r, 0, 1), Math.Clamp(g, 0, 1), Math.Clamp(b, 0, 1), Math.Clamp(a, 0, 1));

    private static ColorValue ParseHex(string h)
    {
        if (h.Length is 3 or 4)
        {
            h = string.Concat(h.Select(c => new string(c, 2)));
        }

        double At(int i) => System.Convert.ToInt32(h.Substring(i, 2), 16) / 255.0;
        return new ColorValue(At(0), At(2), At(4), h.Length == 8 ? At(6) : 1);
    }

    private static double? Unit(string text) =>
        double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) && v is >= 0 and <= 1 ? v : null;

    private static double? Value(string text, out bool percent)
    {
        percent = text.EndsWith('%');
        var t = percent ? text[..^1] : text.EndsWith("deg", StringComparison.OrdinalIgnoreCase) ? text[..^3] : text;
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static double? Channel(string text)
    {
        if (Value(text, out var percent) is not { } v)
        {
            return null;
        }

        return percent ? Math.Clamp(v, 0, 100) / 100 : Math.Clamp(v, 0, 255) / 255;
    }

    private static double? Fraction(string text)
    {
        if (Value(text, out var percent) is not { } v)
        {
            return null;
        }

        return percent ? Math.Clamp(v, 0, 100) / 100 : Math.Clamp(v, 0, 1);
    }

    private static double? Percent(string text)
    {
        if (!text.EndsWith('%') || Value(text, out _) is not { } v)
        {
            return null;
        }

        return Math.Clamp(v, 0, 100) / 100;
    }

    private static double? Hue(string text)
    {
        if (text.EndsWith('%') || Value(text, out _) is not { } v)
        {
            return null;
        }

        var h = v % 360;
        return h < 0 ? h + 360 : h;
    }

    private static (double R, double G, double B) HslToRgb(double h, double s, double l)
    {
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var hp = h / 60;
        var x = c * (1 - Math.Abs(hp % 2 - 1));
        var (r, g, b) = hp switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        var m = l - c / 2;
        return (r + m, g + m, b + m);
    }

    private static (double H, double S, double L) RgbToHsl(double r, double g, double b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        var d = max - min;
        if (d == 0)
        {
            return (0, 0, l);
        }

        var s = d / (1 - Math.Abs(2 * l - 1));
        double h;
        if (max == r)
        {
            h = 60 * (((g - b) / d) % 6);
        }
        else if (max == g)
        {
            h = 60 * ((b - r) / d + 2);
        }
        else
        {
            h = 60 * ((r - g) / d + 4);
        }

        return (h < 0 ? h + 360 : h, s, l);
    }
}
