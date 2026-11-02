// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;

namespace WinKnife.Features.Tools;

/// <summary>Unit conversion (spec 1.7.2). Returns the formatted result ("62.14 mi") or null.</summary>
internal static class CommandBarUnits
{
    private sealed record Unit(string Family, double Factor, string Symbol);

    private static readonly string[] ToWords = ["to", "in", "into", "as", "em", "para", "pra", "en", "a", "nach", "zu", "su", "->", ">", "→"];

    private static readonly Dictionary<string, Unit> Lexicon = Build();

    /// <summary>Converts "100 km to mi" style text.</summary>
    public static string? Convert(string text, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var raw = CommandBarSearchText.Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (raw.Length is < 3 or > 8)
        {
            return null;
        }

        var tokens = JoinTwoWordUnits(raw);
        if (tokens.Count < 3)
        {
            return null;
        }

        for (var i = tokens.Count - 2; i >= 1; i--)
        {
            if (Array.IndexOf(ToWords, tokens[i]) < 0 || tokens.Count - i - 1 != 1)
            {
                continue;
            }

            if (!Lexicon.TryGetValue(tokens[i + 1], out var target))
            {
                continue;
            }

            var left = tokens.Take(i).ToList();
            if (!ParseLeft(left, culture, out var value, out var source) || source.Family != target.Family)
            {
                continue;
            }

            return Render(value, source, target, culture);
        }

        return null;
    }

    private static List<string> JoinTwoWordUnits(string[] raw)
    {
        var list = new List<string>();
        for (var i = 0; i < raw.Length; i++)
        {
            if (i + 1 < raw.Length && raw[i] == "fl" && raw[i + 1] == "oz")
            {
                list.Add("floz");
                i++;
            }
            else if (i + 1 < raw.Length && raw[i] == "fluid" && raw[i + 1] is "ounce" or "ounces")
            {
                list.Add("fluidounce");
                i++;
            }
            else
            {
                list.Add(raw[i]);
            }
        }

        return list;
    }

    private static bool ParseLeft(List<string> left, CultureInfo culture, out double value, out Unit unit)
    {
        value = 0;
        unit = null!;
        string numberText;
        string unitText;
        if (left.Count == 2)
        {
            numberText = left[0];
            unitText = left[1];
        }
        else if (left.Count == 1)
        {
            var t = left[0];
            var j = 0;
            if (j < t.Length && t[j] is '-' or '+')
            {
                j++;
            }

            while (j < t.Length && (char.IsAsciiDigit(t[j]) || ((t[j] is '.' or ',') && j + 1 < t.Length && char.IsAsciiDigit(t[j + 1]))))
            {
                j++;
            }

            numberText = t[..j];
            unitText = t[j..];
        }
        else
        {
            return false;
        }

        var negative = false;
        if (numberText.StartsWith('-') || numberText.StartsWith('+'))
        {
            negative = numberText[0] == '-';
            numberText = numberText[1..];
        }

        if (numberText.Length == 0 || unitText.Length == 0 || !numberText.All(c => char.IsAsciiDigit(c) || c is '.' or ','))
        {
            return false;
        }

        if (CommandBarMath.ParseNumber(numberText, culture) is not { } number || !Lexicon.TryGetValue(unitText, out var found))
        {
            return false;
        }

        value = negative ? -number : number;
        unit = found;
        return true;
    }

    private static string? Render(double value, Unit source, Unit target, CultureInfo culture)
    {
        double result;
        if (source.Family == "temp")
        {
            var celsius = source.Symbol switch
            {
                "°F" => (value - 32) * 5 / 9,
                "K" => value - 273.15,
                _ => value,
            };
            result = target.Symbol switch
            {
                "°F" => celsius * 9 / 5 + 32,
                "K" => celsius + 273.15,
                _ => celsius,
            };
        }
        else
        {
            result = value * source.Factor / target.Factor;
        }

        if (!double.IsFinite(result))
        {
            return null;
        }

        if (target.Symbol == "ft" && result >= 1)
        {
            return RenderFeet(result, culture);
        }

        var number = FormatNumber(result, target.Family == "temp", culture);
        return target.Family == "temp" && target.Symbol != "K" ? number + target.Symbol : number + " " + target.Symbol;
    }

    private static string RenderFeet(double feet, CultureInfo culture)
    {
        var whole = Math.Floor(feet);
        var inches = (feet - whole) * 12;
        var digits = Math.Abs(inches) < 1 ? 4 : 2;
        var rounded = Math.Round(inches, digits);
        if (rounded >= 12)
        {
            whole += 1;
            rounded -= 12;
        }

        var wholeText = whole.ToString("#,##0", culture);
        return rounded == 0 ? wholeText + " ft" : wholeText + " ft " + FormatNumber(rounded, false, culture) + " in";
    }

    private static string FormatNumber(double v, bool temperature, CultureInfo culture)
    {
        if (v == 0)
        {
            return "0";
        }

        var a = Math.Abs(v);
        if (!temperature && a < 0.001)
        {
            var magnitude = (int)Math.Floor(Math.Log10(a));
            var decimals = Math.Clamp(4 - 1 - magnitude, 0, 15);
            return v.ToString("0." + new string('#', decimals), culture);
        }

        var fraction = a < 1 ? 4 : a < 100 ? 2 : 1;
        var rounded = Math.Round(v, fraction);
        return rounded == 0 ? "0" : rounded.ToString("#,##0." + new string('#', fraction), culture);
    }

    private static Dictionary<string, Unit> Build()
    {
        var map = new Dictionary<string, Unit>(StringComparer.Ordinal);

        void Add(string family, double factor, string symbol, params string[] aliases)
        {
            var unit = new Unit(family, factor, symbol);
            foreach (var alias in aliases)
            {
                map[alias] = unit;
            }
        }

        // temperature: factor unused
        Add("temp", 1, "°C", "c", "celsius", "°c", "centigrade");
        Add("temp", 1, "°F", "f", "fahrenheit", "°f");
        Add("temp", 1, "K", "k", "kelvin");

        // length (metres)
        Add("length", 0.001, "mm", "mm", "millimeter", "millimeters", "millimetre", "millimetres", "milimetro", "milimetros");
        Add("length", 0.01, "cm", "cm", "centimeter", "centimeters", "centimetre", "centimetres", "centimetro", "centimetros");
        Add("length", 1, "m", "m", "meter", "meters", "metre", "metres", "metro", "metros");
        Add("length", 1000, "km", "km", "kilometer", "kilometers", "kilometre", "kilometres", "quilometro", "quilometros", "kilometro", "kilometros");
        Add("length", 0.0254, "in", "in", "inch", "inches", "polegada", "polegadas", "\"");
        Add("length", 0.3048, "ft", "ft", "foot", "feet", "pe", "pes");
        Add("length", 0.9144, "yd", "yd", "yard", "yards", "jarda", "jardas");
        Add("length", 1609.344, "mi", "mi", "mile", "miles", "milha", "milhas");

        // mass (grams)
        Add("mass", 0.001, "mg", "mg", "milligram", "milligrams", "miligrama", "miligramas");
        Add("mass", 1, "g", "g", "gram", "grams", "grama", "gramas");
        Add("mass", 1000, "kg", "kg", "kilogram", "kilograms", "quilo", "quilos", "quilograma", "quilogramas");
        Add("mass", 1_000_000, "t", "t", "ton", "tons", "tonne", "tonnes", "tonelada", "toneladas");
        Add("mass", 28.349523125, "oz", "oz", "ounce", "ounces", "onca", "oncas");
        Add("mass", 453.59237, "lb", "lb", "lbs", "pound", "pounds", "libra", "libras");

        // data (bytes)
        Add("data", 1, "B", "b", "byte", "bytes");
        Add("data", 1e3, "KB", "kb", "kilobyte", "kilobytes");
        Add("data", 1e6, "MB", "mb", "megabyte", "megabytes");
        Add("data", 1e9, "GB", "gb", "gigabyte", "gigabytes");
        Add("data", 1e12, "TB", "tb", "terabyte", "terabytes");
        Add("data", 1024, "KiB", "kib", "kibibyte", "kibibytes");
        Add("data", 1024.0 * 1024, "MiB", "mib", "mebibyte", "mebibytes");
        Add("data", 1024.0 * 1024 * 1024, "GiB", "gib", "gibibyte", "gibibytes");
        Add("data", 1024.0 * 1024 * 1024 * 1024, "TiB", "tib", "tebibyte", "tebibytes");
        Add("data", 0.125, "bit", "bit", "bits");

        // duration (seconds)
        Add("duration", 0.001, "ms", "ms", "millisecond", "milliseconds", "milissegundo", "milissegundos");
        Add("duration", 1, "sec", "s", "sec", "secs", "second", "seconds", "segundo", "segundos");
        Add("duration", 60, "min", "min", "mins", "minute", "minutes", "minuto", "minutos");
        Add("duration", 3600, "hr", "h", "hr", "hrs", "hour", "hours", "hora", "horas");
        Add("duration", 86400, "days", "d", "day", "days", "dia", "dias");
        Add("duration", 604800, "wk", "wk", "week", "weeks", "semana", "semanas");
        Add("duration", 31_536_000, "yr", "yr", "yrs", "year", "years", "ano", "anos");

        // volume (litres)
        Add("volume", 0.001, "mL", "ml", "milliliter", "milliliters", "millilitre", "millilitres", "mililitro", "mililitros");
        Add("volume", 1, "L", "l", "liter", "liters", "litre", "litres", "litro", "litros");
        Add("volume", 3.785411784, "gal", "gal", "gallon", "gallons", "galao", "galoes");
        Add("volume", 0.946352946, "qt", "qt", "quart", "quarts");
        Add("volume", 0.0295735295625, "fl oz", "floz", "fluidounce", "fluidounces");
        Add("volume", 0.2365882365, "cup", "cup", "cups", "xicara", "xicaras");

        return map;
    }
}
