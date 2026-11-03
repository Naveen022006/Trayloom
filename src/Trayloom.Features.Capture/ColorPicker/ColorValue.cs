// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Text.RegularExpressions;

namespace Trayloom.Features.Capture;

/// <summary>sRGB colour value with the copy formats of the Color picker (hex, rgb, hsl, swiftui) and a tolerant parser.</summary>
internal readonly record struct ColorValue(double R, double G, double B, double A = 1)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static ColorValue FromBytes(byte r, byte g, byte b) => new(r / 255.0, g / 255.0, b / 255.0);

    private static double Clamp(double v) => double.IsFinite(v) ? Math.Clamp(v, 0, 1) : 0;

    private static int Round(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);

    /// <summary>Formats the colour. <paramref name="alpha"/> null = opaque form.</summary>
    public static string Format(double red, double green, double blue, double? alpha, string format, bool bareHex)
    {
        var r = Clamp(red);
        var g = Clamp(green);
        var b = Clamp(blue);
        string Alpha(double a) => (Math.Round(Clamp(a) * 1000) / 1000).ToString("0.###", Inv);
        switch (format)
        {
            case "rgb":
            {
                var rgb = $"{Round(r * 255)}, {Round(g * 255)}, {Round(b * 255)}";
                return alpha is { } a ? $"rgba({rgb}, {Alpha(a)})" : $"rgb({rgb})";
            }

            case "hsl":
            {
                var (h, s, l) = ToHsl(r, g, b);
                var hsl = $"{Round(h)}, {Round(s * 100)}%, {Round(l * 100)}%";
                return alpha is { } a ? $"hsla({hsl}, {Alpha(a)})" : $"hsl({hsl})";
            }

            case "swiftui":
            {
                var body = $"red: {r.ToString("0.000", Inv)}, green: {g.ToString("0.000", Inv)}, blue: {b.ToString("0.000", Inv)}";
                return alpha is { } a ? $"Color({body}, opacity: {Clamp(a).ToString("0.000", Inv)})" : $"Color({body})";
            }

            default:
            {
                var hex = $"{Round(r * 255):X2}{Round(g * 255):X2}{Round(b * 255):X2}";
                if (alpha is { } a)
                {
                    hex += Round(Clamp(a) * 255).ToString("X2", Inv);
                }

                return bareHex ? hex : "#" + hex;
            }
        }
    }

    public string ToText(string format, bool bareHex, bool withAlpha = false) => Format(R, G, B, withAlpha ? A : null, format, bareHex);

    public static (double H, double S, double L) ToHsl(double r, double g, double b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        var l = (max + min) / 2;
        if (delta <= 1e-6)
        {
            return (0, 0, l);
        }

        var s = Math.Clamp(delta / (1 - Math.Abs(2 * l - 1)), 0, 1);
        double h;
        if (max == r)
        {
            h = ((g - b) / delta) % 6;
        }
        else if (max == g)
        {
            h = (b - r) / delta + 2;
        }
        else
        {
            h = (r - g) / delta + 4;
        }

        h *= 60;
        if (h < 0)
        {
            h += 360;
        }

        return (h, s, l);
    }

    /// <summary>Parses a colour only when the whole text is one (#hex, rgb(), hsl(), Color(red:...)).</summary>
    public static ColorValue? Parse(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var t = text.Trim().ToLowerInvariant();
        if (t.Length == 0 || System.Text.Encoding.UTF8.GetByteCount(t) > 96)
        {
            return null;
        }

        if (t[0] == '#')
        {
            var hex = t[1..];
            if (!Regex.IsMatch(hex, "^[0-9a-f]+$") || hex.Length is not (3 or 4 or 6 or 8))
            {
                return null;
            }

            if (hex.Length <= 4)
            {
                hex = string.Concat(hex.Select(c => new string(c, 2)));
            }

            double Pair(int i) => int.Parse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, Inv) / 255.0;
            return new ColorValue(Pair(0), Pair(1), Pair(2), hex.Length == 8 ? Pair(3) : 1);
        }

        var m = Regex.Match(t, @"^(rgba?|hsla?|color)\s*\((.*)\)$");
        if (!m.Success)
        {
            return null;
        }

        var kind = m.Groups[1].Value;
        var body = m.Groups[2].Value;
        if (kind == "color")
        {
            var pm = Regex.Matches(body, @"(red|green|blue|opacity)\s*:\s*([0-9.]+)");
            if (pm.Count is < 3 or > 4)
            {
                return null;
            }

            double Get(string label) => pm.Cast<Match>().Where(x => x.Groups[1].Value == label).Select(x => double.Parse(x.Groups[2].Value, Inv)).DefaultIfEmpty(label == "opacity" ? 1 : double.NaN).First();
            var r = Get("red");
            var g = Get("green");
            var b = Get("blue");
            return double.IsNaN(r) || double.IsNaN(g) || double.IsNaN(b) ? null : new ColorValue(Clamp(r), Clamp(g), Clamp(b), Clamp(Get("opacity")));
        }

        var args = Regex.Split(body.Trim(), @"\s*[,/]\s*|\s+").Where(x => x.Length > 0).ToArray();
        if (args.Length is not (3 or 4))
        {
            return null;
        }

        double Alpha(string? s)
        {
            if (s is null)
            {
                return 1;
            }

            return s.EndsWith('%') ? double.Parse(s[..^1], Inv) / 100 : double.Parse(s, Inv);
        }

        try
        {
            if (kind.StartsWith("rgb", StringComparison.Ordinal))
            {
                double Ch(string s) => s.EndsWith('%') ? double.Parse(s[..^1], Inv) / 100 : double.Parse(s, Inv) / 255;
                return new ColorValue(Clamp(Ch(args[0])), Clamp(Ch(args[1])), Clamp(Ch(args[2])), Clamp(Alpha(args.Length > 3 ? args[3] : null)));
            }

            var hue = double.Parse(args[0].Replace("deg", string.Empty), Inv);
            hue = ((hue % 360) + 360) % 360;
            if (!args[1].EndsWith('%') || !args[2].EndsWith('%'))
            {
                return null;
            }

            var sat = Math.Clamp(double.Parse(args[1][..^1], Inv) / 100, 0, 1);
            var light = Math.Clamp(double.Parse(args[2][..^1], Inv) / 100, 0, 1);
            var h = hue / 360;
            var chroma = (1 - Math.Abs(2 * light - 1)) * sat;
            double Chan(double k0)
            {
                var k = (k0 + h * 12) % 12;
                return light - chroma / 2 * Math.Max(-1, Math.Min(Math.Min(k - 3, 9 - k), 1));
            }

            return new ColorValue(Clamp(Chan(0)), Clamp(Chan(8)), Clamp(Chan(4)), Clamp(Alpha(args.Length > 3 ? args[3] : null)));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
