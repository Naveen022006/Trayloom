// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Text.RegularExpressions;
using Trayloom.Core;

namespace Trayloom.Features.Tools;

/// <summary>A date / time answer.</summary>
/// <param name="Title">Big value.</param>
/// <param name="Detail">Detail line (weekday, long date, "City · date").</param>
internal sealed record DateAnswer(string Title, string Detail);

/// <summary>Relative dates, days-until and time somewhere else (spec 1.7.3). Answered without network.</summary>
internal static class CommandBarDates
{
    private enum Unit
    {
        Day,
        Week,
        Month,
        Year,
    }

    private static readonly HashSet<string> TodayWords = ["today", "hoje", "heute", "hoy", "oggi", "aujourdhui"];

    private static readonly HashSet<string> FutureWords = ["in", "em", "daqui", "dans", "en", "tra", "fra", "from", "dentro"];

    private static readonly HashSet<string> PastWords = ["ago", "atras", "ha", "hace", "vor", "fa"];

    private static readonly HashSet<string> UntilWords = ["until", "ate", "hasta", "bis", "jusqua", "fino"];

    private static readonly HashSet<string> TimeWords = ["time", "hora", "uhrzeit", "heure", "ora", "hour"];

    private static readonly HashSet<string> TimeStopWords = ["in", "a", "at", "de", "em", "en", "the", "now", "current", "right", "dans", "tra"];

    private static readonly HashSet<string> Fillers = ["now", "the"];

    private static readonly Dictionary<string, Unit> UnitWords = BuildUnits();

    private static readonly HashSet<string> DayWords = ["day", "days", "dia", "dias", "tag", "tage", "tagen", "jour", "jours", "giorno", "giorni"];

    private static readonly HashSet<string> UntilPrefixWords = ["how", "many", "much", "long", "left", "count", "countdown", "to", "go", "days", "day", "dia", "dias", "tage", "tag", "jours", "jour", "giorni", "giorno"];

    private static readonly Regex DatePattern = new(@"^(\d{1,4})[./-](\d{1,4})(?:[./-](\d{1,4}))?$", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> CityIds = BuildCities();

    /// <summary>Answers "in 3 weeks", "3 days ago", "days until 25/12", "time in tokyo"; null otherwise.</summary>
    public static DateAnswer? Answer(string text, DateTime now, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 80)
        {
            return null;
        }

        var folded = CommandBarSearchText.Normalize(text).Replace("'", string.Empty);
        var tokens = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
        {
            return null;
        }

        return Relative(tokens, now, culture) ?? Until(tokens, now, culture) ?? TimeIn(tokens, now, culture);
    }

    private static DateAnswer? Relative(string[] tokens, DateTime now, CultureInfo culture)
    {
        int? count = null;
        Unit? unit = null;
        var past = 0;
        var future = 0;

        foreach (var t in tokens)
        {
            if (PastWords.Contains(t) || t == "-")
            {
                past++;
            }
            else if (TodayWords.Contains(t) || FutureWords.Contains(t) || t == "+")
            {
                future++;
            }
            else if (Fillers.Contains(t))
            {
            }
            else if (UnitWords.TryGetValue(t, out var u))
            {
                if (unit is not null)
                {
                    return null;
                }

                unit = u;
            }
            else if (TryInt(t, out var n))
            {
                if (count is not null)
                {
                    return null;
                }

                count = Math.Abs(n);
                if (t.StartsWith('-'))
                {
                    past++;
                }
                else if (t.StartsWith('+'))
                {
                    future++;
                }
            }
            else
            {
                return null;
            }
        }

        if (unit is null || count is null || (past > 0) == (future > 0))
        {
            return null;
        }

        var signed = past > 0 ? -count.Value : count.Value;
        try
        {
            var date = now.Date;
            date = unit switch
            {
                Unit.Day => date.AddDays(signed),
                Unit.Week => date.AddDays(7.0 * signed),
                Unit.Month => date.AddMonths(signed),
                _ => date.AddYears(signed),
            };
            return new DateAnswer(LongDate(date, culture), date.ToString("dddd", culture));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static bool TryInt(string token, out int value)
    {
        value = 0;
        var s = token.TrimStart('+', '-');
        if (s.Length == 0 || s.Length > 6 || !s.All(char.IsAsciiDigit))
        {
            return false;
        }

        value = int.Parse(s, CultureInfo.InvariantCulture);
        if (token.StartsWith('-'))
        {
            value = -value;
        }

        return Math.Abs(value) <= 10_000;
    }

    private static DateAnswer? Until(string[] tokens, DateTime now, CultureInfo culture)
    {
        var index = Array.FindIndex(tokens, t => UntilWords.Contains(t));
        if (index < 0 || index == tokens.Length - 1)
        {
            return null;
        }

        if (tokens.Take(index).Any(t => !UntilPrefixWords.Contains(t)))
        {
            return null;
        }

        var dateText = string.Concat(tokens.Skip(index + 1));
        var match = DatePattern.Match(dateText);
        if (!match.Success)
        {
            return null;
        }

        var numbers = new List<int> { int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) };
        var hasYear = match.Groups[3].Success;
        if (hasYear)
        {
            numbers.Add(int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
        }

        var order = DateOrder(culture, hasYear);
        var target = ResolveDate(numbers, order, hasYear, match.Groups[3].Value.Length, now)
            ?? ResolveDate(numbers, SwapDayMonth(order), hasYear, match.Groups[3].Value.Length, now);
        if (target is not { } date)
        {
            return null;
        }

        var days = Math.Abs((date - now.Date).Days);
        var title = days == 1 ? Loc.T("1 day") : days.ToString("#,##0", culture) + " days";
        return new DateAnswer(title, LongDate(date, culture));
    }

    private static char[] DateOrder(CultureInfo culture, bool withYear)
    {
        var pattern = culture.DateTimeFormat.ShortDatePattern;
        var order = new List<char>();
        foreach (var c in pattern)
        {
            var key = c switch
            {
                'd' => 'd',
                'M' => 'M',
                'y' => 'y',
                _ => '\0',
            };
            if (key != '\0' && !order.Contains(key))
            {
                order.Add(key);
            }
        }

        if (order.Count < 3)
        {
            order = ['M', 'd', 'y'];
        }

        if (!withYear)
        {
            order.Remove('y');
        }

        return [.. order];
    }

    private static char[] SwapDayMonth(char[] order) => [.. order.Select(c => c == 'd' ? 'M' : c == 'M' ? 'd' : c)];

    private static DateTime? ResolveDate(List<int> numbers, char[] order, bool hasYear, int yearDigits, DateTime now)
    {
        int day = 0;
        int month = 0;
        int year = 0;
        for (var i = 0; i < order.Length && i < numbers.Count; i++)
        {
            switch (order[i])
            {
                case 'd':
                    day = numbers[i];
                    break;
                case 'M':
                    month = numbers[i];
                    break;
                default:
                    year = numbers[i];
                    break;
            }
        }

        if (month is < 1 or > 12 || day < 1)
        {
            return null;
        }

        if (hasYear)
        {
            if (yearDigits <= 2)
            {
                year += 2000;
            }

            if (year is < 1 or > 9999 || day > DateTime.DaysInMonth(year, month))
            {
                return null;
            }

            return new DateTime(year, month, day);
        }

        for (var y = now.Year; y <= now.Year + 8; y++)
        {
            if (day > DateTime.DaysInMonth(y, month))
            {
                continue;
            }

            var candidate = new DateTime(y, month, day);
            if (candidate >= now.Date)
            {
                return candidate;
            }
        }

        return null;
    }

    private static DateAnswer? TimeIn(string[] tokens, DateTime now, CultureInfo culture)
    {
        if (tokens.Count(t => TimeWords.Contains(t)) != 1)
        {
            return null;
        }

        var place = tokens.Where(t => !TimeWords.Contains(t) && !TimeStopWords.Contains(t) && !FutureWords.Contains(t)).ToArray();
        if (place.Length == 0)
        {
            return null;
        }

        var key = string.Join(' ', place);
        if (!CityIds.TryGetValue(key, out var iana))
        {
            return null;
        }

        var zone = FindZone(iana);
        if (zone is null)
        {
            return null;
        }

        try
        {
            var local = now.Kind == DateTimeKind.Utc ? now : TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(now, DateTimeKind.Local));
            var there = TimeZoneInfo.ConvertTimeFromUtc(local, zone);
            var title = there.ToString("t", culture);
            var city = culture.TextInfo.ToTitleCase(key);
            return new DateAnswer(title, city + " · " + there.ToString("D", culture));
        }
        catch (Exception ex) when (ex is ArgumentException or TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }

    private static TimeZoneInfo? FindZone(string iana)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(iana);
        }
        catch (Exception)
        {
            try
            {
                return TimeZoneInfo.TryConvertIanaIdToWindowsId(iana, out var windows) && windows is not null ? TimeZoneInfo.FindSystemTimeZoneById(windows) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>The long date without the weekday ("November 2, 2026").</summary>
    private static string LongDate(DateTime date, CultureInfo culture)
    {
        var pattern = culture.DateTimeFormat.LongDatePattern;
        var stripped = Regex.Replace(pattern, @"d{4}\s*[,.]?\s*", string.Empty).Trim(' ', ',', '.');
        if (string.IsNullOrEmpty(stripped))
        {
            stripped = pattern;
        }

        try
        {
            return date.ToString(stripped, culture);
        }
        catch (FormatException)
        {
            return date.ToString("D", culture);
        }
    }

    private static Dictionary<string, Unit> BuildUnits()
    {
        var map = new Dictionary<string, Unit>(StringComparer.Ordinal);
        void Add(Unit unit, params string[] words)
        {
            foreach (var w in words)
            {
                map[w] = unit;
            }
        }

        Add(Unit.Day, "day", "days", "dia", "dias", "tag", "tage", "tagen", "jour", "jours", "giorno", "giorni", "dag", "dagen", "dzien", "dni");
        Add(Unit.Week, "week", "weeks", "semana", "semanas", "woche", "wochen", "semaine", "semaines", "settimana", "settimane", "weken", "tydzien", "tygodnie");
        Add(Unit.Month, "month", "months", "mes", "meses", "monat", "monate", "monaten", "mois", "mese", "mesi", "maand", "maanden", "miesiac", "miesiace");
        Add(Unit.Year, "year", "years", "ano", "anos", "jahr", "jahre", "jahren", "an", "ans", "anno", "anni", "jaar", "jaren", "rok", "lata");
        return map;
    }

    private static Dictionary<string, string> BuildCities()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        const string ids = "Africa/Abidjan Africa/Algiers Africa/Cairo Africa/Casablanca Africa/Johannesburg Africa/Lagos Africa/Nairobi Africa/Tunis Africa/Accra Africa/Addis_Ababa "
            + "America/Anchorage America/Argentina/Buenos_Aires America/Bogota America/Caracas America/Chicago America/Denver America/Halifax America/Havana America/Lima America/Los_Angeles "
            + "America/Mexico_City America/New_York America/Panama America/Phoenix America/Santiago America/Sao_Paulo America/St_Johns America/Toronto America/Vancouver America/Montevideo "
            + "America/La_Paz America/Guatemala America/Costa_Rica America/Jamaica America/Puerto_Rico America/Edmonton America/Winnipeg America/Detroit America/Regina America/Bogota "
            + "Asia/Almaty Asia/Baghdad Asia/Bangkok Asia/Beirut Asia/Colombo Asia/Dhaka Asia/Dubai Asia/Ho_Chi_Minh Asia/Hong_Kong Asia/Jakarta Asia/Jerusalem Asia/Kabul Asia/Karachi "
            + "Asia/Kathmandu Asia/Kolkata Asia/Kuala_Lumpur Asia/Manila Asia/Riyadh Asia/Seoul Asia/Shanghai Asia/Singapore Asia/Taipei Asia/Tashkent Asia/Tehran Asia/Tokyo Asia/Ulaanbaatar "
            + "Asia/Yangon Asia/Doha Asia/Kuwait Asia/Baku Asia/Tbilisi Asia/Yerevan Asia/Amman Asia/Damascus Asia/Muscat "
            + "Atlantic/Reykjavik Atlantic/Azores Atlantic/Canary Australia/Adelaide Australia/Brisbane Australia/Darwin Australia/Hobart Australia/Melbourne Australia/Perth Australia/Sydney "
            + "Europe/Amsterdam Europe/Athens Europe/Belgrade Europe/Berlin Europe/Brussels Europe/Bucharest Europe/Budapest Europe/Copenhagen Europe/Dublin Europe/Helsinki Europe/Istanbul "
            + "Europe/Kyiv Europe/Lisbon Europe/London Europe/Madrid Europe/Minsk Europe/Moscow Europe/Oslo Europe/Paris Europe/Prague Europe/Riga Europe/Rome Europe/Sofia Europe/Stockholm "
            + "Europe/Tallinn Europe/Vienna Europe/Vilnius Europe/Warsaw Europe/Zurich Europe/Luxembourg Europe/Monaco Europe/Zagreb Europe/Bratislava Europe/Ljubljana "
            + "Pacific/Auckland Pacific/Fiji Pacific/Honolulu Pacific/Guam Pacific/Port_Moresby Pacific/Tahiti";
        foreach (var id in ids.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var city = id[(id.LastIndexOf('/') + 1)..].Replace('_', ' ');
            map.TryAdd(CommandBarSearchText.Normalize(city), id);
        }

        void Alias(string iana, params string[] names)
        {
            foreach (var n in names)
            {
                map[CommandBarSearchText.Normalize(n)] = iana;
            }
        }

        Alias("Europe/London", "londres", "uk", "england");
        Alias("Europe/Lisbon", "lisboa", "portugal");
        Alias("Europe/Rome", "roma", "italy");
        Alias("Europe/Moscow", "moscou", "moscovo", "москва");
        Alias("America/New_York", "nova york", "nueva york", "nova iorque", "new york city", "nyc", "miami", "boston", "atlanta", "washington", "washington dc");
        Alias("America/Mexico_City", "cidade do mexico", "ciudad de mexico", "mexico");
        Alias("Asia/Shanghai", "pequim", "beijing", "china");
        Alias("Asia/Tokyo", "toquio", "tokio", "japan", "japao");
        Alias("Europe/Zurich", "genebra", "geneva", "zurique", "zurich", "genf");
        Alias("Europe/Vienna", "viena", "wien");
        Alias("Europe/Copenhagen", "copenhague");
        Alias("Europe/Prague", "praga");
        Alias("Europe/Athens", "atenas");
        Alias("Europe/Warsaw", "varsovia", "warschau");
        Alias("Europe/Brussels", "bruxelas", "bruselas");
        Alias("Europe/Berlin", "munique", "munich", "munchen", "colonia", "cologne", "koln", "hamburgo", "hamburg", "germany");
        Alias("Europe/Stockholm", "estocolmo");
        Alias("Europe/Kyiv", "kiev");
        Alias("Europe/Istanbul", "istambul", "turkey");
        Alias("America/Los_Angeles", "san francisco", "seattle", "san diego", "las vegas", "vegas", "la");
        Alias("America/Chicago", "dallas", "houston", "austin", "chicago", "new orleans");
        Alias("America/Sao_Paulo", "rio", "rio de janeiro", "brasilia", "brazil", "brasil");
        Alias("Asia/Kolkata", "mumbai", "bombay", "delhi", "new delhi", "calcutta", "india");
        Alias("Asia/Dubai", "abu dhabi");
        Alias("Asia/Jerusalem", "tel aviv");
        Alias("Africa/Johannesburg", "cape town", "pretoria", "south africa");
        Alias("Asia/Ho_Chi_Minh", "saigon");
        Alias("Pacific/Honolulu", "hawaii");
        return map;
    }
}
