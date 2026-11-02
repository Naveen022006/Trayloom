// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using WinKnife.Core;

namespace WinKnife.Features.Input;

/// <summary>One text snippet (stored as a JSON array in the flat <c>textSnippets</c> key).</summary>
public sealed class SnippetItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString().ToUpperInvariant();

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("trigger")]
    public string Trigger { get; set; } = string.Empty;

    [JsonPropertyName("replacement")]
    public string Replacement { get; set; } = string.Empty;

    /// <summary><c>afterDelimiter</c> (after space, Tab or Return) or <c>immediate</c>.</summary>
    [JsonPropertyName("expansion")]
    public string Expansion { get; set; } = "afterDelimiter";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("ignoresCase")]
    public bool IgnoresCase { get; set; }

    [JsonPropertyName("folder")]
    public string Folder { get; set; } = string.Empty;

    [JsonPropertyName("showsInLibrary")]
    public bool ShowsInLibrary { get; set; } = true;

    [JsonIgnore]
    public bool IsImmediate => Expansion == "immediate";

    /// <summary>The name, or the trigger when the snippet has no name.</summary>
    [JsonIgnore]
    public string DisplayTitle => string.IsNullOrWhiteSpace(Name) ? Trigger : Name;

    public SnippetItem Clone() => (SnippetItem)MemberwiseClone();
}

/// <summary>Loading, saving and validating the snippet library.</summary>
public static class SnippetStore
{
    public const int MinTrigger = 2;
    public const int MaxTrigger = 40;

    public static List<SnippetItem> Load(IAppSettings app)
    {
        List<SnippetItem>? stored;
        try
        {
            stored = app.Get<List<SnippetItem>?>(K.Snippets);
        }
        catch
        {
            stored = null;
        }

        return [.. (stored ?? []).Where(x => x is not null)];
    }

    public static void Save(IAppSettings app, List<SnippetItem> items) => app.Set(K.Snippets, items);

    /// <summary>Whitespace removed, truncated to 40 characters.</summary>
    public static string SanitizeTrigger(string? trigger)
    {
        var text = new string((trigger ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray());
        return text.Length > MaxTrigger ? text[..MaxTrigger] : text;
    }

    /// <summary>True when another snippet already uses the trigger (case-insensitive when either snippet ignores case).</summary>
    public static bool IsDuplicate(IEnumerable<SnippetItem> all, string trigger, bool ignoresCase, string? exceptId)
    {
        foreach (var other in all)
        {
            if (other.Id == exceptId)
            {
                continue;
            }

            var comparison = ignoresCase || other.IgnoresCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (string.Equals(other.Trigger, trigger, comparison))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Distinct folder names, alphabetical.</summary>
    public static List<string> Folders(IEnumerable<SnippetItem> all) =>
        [.. all.Select(x => x.Folder.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)];
}

/// <summary>Snippet variables: <c>{{date}}</c>, <c>{{time}}</c>, <c>{{datetime}}</c>, <c>{{clipboard}}</c> and formatted / timezone variants.</summary>
public static class SnippetVariables
{
    private static readonly Regex DateToken = new(@"\{\{(datetime|date|time)(?:-tz\(([^)}]*)\))?(?::([^}]*))?\}\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Expands the date/time variables and then the clipboard (read last, so pasted text is never re-expanded).</summary>
    public static string Expand(string template, Func<string> clipboard, DateTimeOffset? now = null)
    {
        var moment = now ?? DateTimeOffset.Now;
        var text = DateToken.Replace(template, match => Format(match.Groups[1].Value.ToLowerInvariant(), match.Groups[2].Success ? match.Groups[2].Value : null, match.Groups[3].Success ? match.Groups[3].Value : null, moment) ?? match.Value);
        if (text.Contains("{{clipboard}}", StringComparison.OrdinalIgnoreCase))
        {
            var clip = clipboard();
            text = Regex.Replace(text, @"\{\{clipboard\}\}", _ => clip, RegexOptions.IgnoreCase);
        }

        return text;
    }

    /// <summary>Formats a date variable; null when the timezone id is unknown (the tag stays visible).</summary>
    public static string? Format(string kind, string? zone, string? pattern, DateTimeOffset now)
    {
        var moment = now;
        if (!string.IsNullOrWhiteSpace(zone))
        {
            var tz = FindZone(zone.Trim());
            if (tz is null)
            {
                return null;
            }

            moment = TimeZoneInfo.ConvertTime(now, tz);
        }

        var culture = CultureInfo.CurrentCulture;
        var icu = string.IsNullOrEmpty(pattern) ? DefaultPattern(kind, culture) : pattern;
        return IcuDate.Format(moment, icu, culture, zone is null ? TimeZoneInfo.Local : FindZone(zone.Trim()));
    }

    public static TimeZoneInfo? FindZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch
        {
            foreach (var zone in TimeZoneInfo.GetSystemTimeZones())
            {
                if (TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) && string.Equals(iana, id, StringComparison.OrdinalIgnoreCase))
                {
                    return zone;
                }
            }

            return null;
        }
    }

    /// <summary>Default pattern of a bare variable: medium date, short time, "date time".</summary>
    public static string DefaultPattern(string kind, CultureInfo culture) => kind switch
    {
        "date" => NamedPattern("date", "medium", culture),
        "time" => NamedPattern("time", "short", culture),
        _ => NamedPattern("date", "medium", culture) + " " + NamedPattern("time", "short", culture),
    };

    /// <summary>The ICU pattern of a named style (short, medium, long, full, iso) for the current region.</summary>
    public static string NamedPattern(string kind, string style, CultureInfo culture)
    {
        var f = culture.DateTimeFormat;
        var datePart = style switch
        {
            "short" => ToIcu(f.ShortDatePattern, f),
            "medium" => ToIcu(f.ShortDatePattern, f).StartsWith('M') ? "MMM d, yyyy" : "d MMM yyyy",
            "long" => ToIcu(f.LongDatePattern, f).Replace("EEEE, ", string.Empty).Replace("EEEE ", string.Empty).Replace(", EEEE", string.Empty),
            "full" => ToIcu(f.LongDatePattern, f),
            _ => "yyyy-MM-dd",
        };
        var timePart = style switch
        {
            "short" => ToIcu(f.ShortTimePattern, f),
            "medium" => ToIcu(f.LongTimePattern, f),
            "long" => ToIcu(f.LongTimePattern, f) + " z",
            "full" => ToIcu(f.LongTimePattern, f) + " zzzz",
            _ => "HH:mm:ssXXX",
        };
        return kind switch
        {
            "date" => datePart,
            "time" => timePart,
            _ => style == "iso" ? "yyyy-MM-dd'T'HH:mm:ssXXX" : datePart + " " + timePart,
        };
    }

    // .NET custom format -> ICU pattern (dddd -> EEEE, tt -> a, separators quoted).
    private static string ToIcu(string net, DateTimeFormatInfo f)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < net.Length;)
        {
            var c = net[i];
            var run = 1;
            while (i + run < net.Length && net[i + run] == c)
            {
                run++;
            }

            switch (c)
            {
                case 'd' when run >= 3:
                    sb.Append(run == 3 ? "EEE" : "EEEE");
                    break;
                case 't':
                    sb.Append('a');
                    break;
                case '/':
                    sb.Append('\'').Append(f.DateSeparator).Append('\'');
                    break;
                case ':':
                    sb.Append('\'').Append(f.TimeSeparator).Append('\'');
                    break;
                case 'f' or 'F':
                    sb.Append('S', run);
                    break;
                case 'z':
                    sb.Append(run == 1 ? "X" : run == 2 ? "XX" : "XXX");
                    break;
                case '\'' or '"':
                    var end = net.IndexOf(c, i + 1);
                    var literal = end < 0 ? net[(i + 1)..] : net.Substring(i + 1, end - i - 1);
                    sb.Append('\'').Append(literal.Replace("'", "''")).Append('\'');
                    i = end < 0 ? net.Length : end + 1;
                    continue;
                case '%' or '\\':
                    if (i + 1 < net.Length)
                    {
                        sb.Append('\'').Append(net[i + 1]).Append('\'');
                    }

                    i += 2;
                    continue;
                default:
                    if (char.IsLetter(c))
                    {
                        sb.Append(c, run);
                    }
                    else
                    {
                        sb.Append(c, run);
                    }

                    break;
            }

            i += run;
        }

        return sb.ToString().Replace("''", string.Empty);
    }
}

/// <summary>A small ICU-pattern date formatter (letters y M L d E e a h H K k m s S Z X z, quoted literals).</summary>
public static class IcuDate
{
    public static string Format(DateTimeOffset moment, string pattern, CultureInfo culture, TimeZoneInfo? zone = null)
    {
        var sb = new StringBuilder();
        var dt = moment.DateTime;
        var offset = moment.Offset;
        for (var i = 0; i < pattern.Length;)
        {
            var c = pattern[i];
            if (c == '\'')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '\'')
                {
                    sb.Append('\'');
                    i += 2;
                    continue;
                }

                var end = pattern.IndexOf('\'', i + 1);
                if (end < 0)
                {
                    sb.Append(pattern, i + 1, pattern.Length - i - 1);
                    break;
                }

                sb.Append(pattern, i + 1, end - i - 1);
                i = end + 1;
                continue;
            }

            if (!char.IsAsciiLetter(c))
            {
                sb.Append(c);
                i++;
                continue;
            }

            var n = 1;
            while (i + n < pattern.Length && pattern[i + n] == c)
            {
                n++;
            }

            var f = culture.DateTimeFormat;
            switch (c)
            {
                case 'y':
                    sb.Append(n == 2 ? (dt.Year % 100).ToString("00") : dt.Year.ToString(new string('0', Math.Max(1, n))));
                    break;
                case 'M' or 'L':
                    sb.Append(n switch
                    {
                        1 => dt.Month.ToString(),
                        2 => dt.Month.ToString("00"),
                        3 => f.GetAbbreviatedMonthName(dt.Month),
                        4 => f.GetMonthName(dt.Month),
                        _ => f.GetMonthName(dt.Month)[..1],
                    });
                    break;
                case 'd':
                    sb.Append(dt.Day.ToString(new string('0', n)));
                    break;
                case 'E' or 'e' or 'c':
                    sb.Append(n switch
                    {
                        <= 3 => f.GetAbbreviatedDayName(dt.DayOfWeek),
                        4 => f.GetDayName(dt.DayOfWeek),
                        5 => f.GetDayName(dt.DayOfWeek)[..1],
                        _ => f.GetShortestDayName(dt.DayOfWeek),
                    });
                    break;
                case 'a':
                    sb.Append(dt.Hour < 12 ? f.AMDesignator : f.PMDesignator);
                    break;
                case 'h':
                    sb.Append((dt.Hour % 12 == 0 ? 12 : dt.Hour % 12).ToString(new string('0', n)));
                    break;
                case 'H':
                    sb.Append(dt.Hour.ToString(new string('0', n)));
                    break;
                case 'K':
                    sb.Append((dt.Hour % 12).ToString(new string('0', n)));
                    break;
                case 'k':
                    sb.Append((dt.Hour == 0 ? 24 : dt.Hour).ToString(new string('0', n)));
                    break;
                case 'm':
                    sb.Append(dt.Minute.ToString(new string('0', n)));
                    break;
                case 's':
                    sb.Append(dt.Second.ToString(new string('0', n)));
                    break;
                case 'S':
                    sb.Append((dt.Millisecond * 1000 + (int)(dt.Ticks % 10000) / 10).ToString("000000")[..Math.Min(n, 6)].PadRight(n, '0'));
                    break;
                case 'Z':
                    sb.Append(n switch
                    {
                        4 => "GMT" + OffsetText(offset, true),
                        5 => OffsetText(offset, true, zulu: true),
                        _ => OffsetText(offset, false),
                    });
                    break;
                case 'X':
                    sb.Append(offset == TimeSpan.Zero ? "Z" : n switch
                    {
                        1 => OffsetText(offset, false)[..3],
                        2 => OffsetText(offset, false),
                        _ => OffsetText(offset, true),
                    });
                    break;
                case 'z' or 'v' or 'V':
                    sb.Append(n >= 4 && zone is not null ? zone.IsDaylightSavingTime(moment) ? zone.DaylightName : zone.StandardName : "GMT" + (offset == TimeSpan.Zero ? string.Empty : OffsetText(offset, true)));
                    break;
                default:
                    sb.Append(c, n);
                    break;
            }

            i += n;
        }

        return sb.ToString();
    }

    private static string OffsetText(TimeSpan offset, bool colon, bool zulu = false)
    {
        if (zulu && offset == TimeSpan.Zero)
        {
            return "Z";
        }

        var sign = offset < TimeSpan.Zero ? '-' : '+';
        var abs = offset.Duration();
        return $"{sign}{abs.Hours:00}{(colon ? ":" : string.Empty)}{abs.Minutes:00}";
    }
}
