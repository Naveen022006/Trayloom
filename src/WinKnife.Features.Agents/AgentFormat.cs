// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;

namespace WinKnife.Features.Agents;

/// <summary>Numbers in the reader's region; costs in US dollars (spec 03 A6.5 <c>AgentFormat</c>).</summary>
public static class AgentFormat
{
    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    public static string Cost(double value)
    {
        if (!double.IsFinite(value))
        {
            return "$0";
        }

        var magnitude = Math.Abs(value);
        if (magnitude >= 10_000)
        {
            return "$" + Scaled(magnitude / 1000) + "K";
        }

        var digits = magnitude >= 100 ? 0 : 2;
        return "$" + value.ToString("N" + digits, Culture);
    }

    public static string Tokens(long value)
    {
        var m = (double)Math.Max(0, value);
        if (m < 1000)
        {
            return ((long)m).ToString("N0", Culture);
        }

        if (m < 1_000_000)
        {
            return Scaled(m / 1000) + "K";
        }

        return m < 1_000_000_000 ? Scaled(m / 1_000_000) + "M" : Scaled(m / 1_000_000_000) + "B";
    }

    /// <summary>One decimal below ten, none above, truncated: "4.2", "48", "120".</summary>
    private static string Scaled(double value)
    {
        if (value < 10)
        {
            var floored = Math.Floor(value * 10) / 10;
            return floored.ToString("0.#", Culture);
        }

        return Math.Floor(value).ToString("0", Culture);
    }

    public static string Percent(double fraction)
    {
        var v = double.IsFinite(fraction) ? Math.Min(1, Math.Max(0, fraction)) : 0;
        return Math.Round(v * 100, MidpointRounding.AwayFromZero).ToString("0", Culture) + "%";
    }

    /// <summary>"4m 12s", "1h 5m", "2d 3h" (at most <paramref name="units"/> units).</summary>
    public static string Duration(double seconds, int units = 2)
    {
        seconds = double.IsFinite(seconds) ? Math.Max(0, seconds) : 0;
        var total = (long)seconds;
        long d = total / 86400, h = total % 86400 / 3600, m = total % 3600 / 60, s = total % 60;
        var parts = new List<string>();
        if (seconds >= 86400)
        {
            parts.Add(d + "d");
            parts.Add(h + "h");
        }
        else if (seconds >= 3600)
        {
            parts.Add(h + "h");
            parts.Add(m + "m");
        }
        else
        {
            parts.Add(m + "m");
            parts.Add(s + "s");
        }

        // Leading zero units are dropped (DateComponentsFormatter behaviour), then the unit count is cut.
        while (parts.Count > 1 && parts[0].StartsWith('0'))
        {
            parts.RemoveAt(0);
        }

        if (parts.Count > units)
        {
            parts = parts.Take(units).ToList();
        }

        // Trailing zero units are dropped too ("2h" rather than "2h 0m").
        while (parts.Count > 1 && parts[^1].StartsWith('0'))
        {
            parts.RemoveAt(parts.Count - 1);
        }

        return string.Join(' ', parts);
    }

    /// <summary>"30 sec", "1 min", "2 min" - the short style of the finish-minimum menu.</summary>
    public static string DurationShort(double seconds)
    {
        var total = (long)Math.Max(0, seconds);
        if (total < 60)
        {
            return total + " sec";
        }

        if (total < 3600)
        {
            return total / 60 + " min" + (total % 60 != 0 ? " " + total % 60 + " sec" : "");
        }

        return total / 3600 + " hr" + (total % 3600 / 60 != 0 ? " " + total % 3600 / 60 + " min" : "");
    }

    /// <summary>"13 weeks" or "30 days".</summary>
    public static string Span(int days)
    {
        days = Math.Max(0, days);
        if (days % 7 == 0)
        {
            var w = days / 7;
            return w + (w == 1 ? " week" : " weeks");
        }

        return days + (days == 1 ? " day" : " days");
    }

    /// <summary>A running stopwatch: "0:42", "12:05", "1:02:05".</summary>
    public static string Clock(double seconds)
    {
        var total = (long)Math.Max(0, double.IsFinite(seconds) ? seconds : 0);
        return total >= 3600
            ? $"{total / 3600}:{total / 60 % 60:00}:{total % 60:00}"
            : $"{total / 60}:{total % 60:00}";
    }

    /// <summary>A calendar day kept in UTC, like the price list's: "Oct 30, 2026".</summary>
    public static string Day(DateTime utc) => utc.ToString("MMM d, yyyy", Culture);

    /// <summary>"5 min. ago" style (abbreviated named presentation).</summary>
    public static string Relative(DateTime utc, DateTime now, bool abbreviated = true)
    {
        var delta = (now - utc).TotalSeconds;
        var future = delta < 0;
        var s = Math.Abs(delta);
        if (s < 45)
        {
            return "now";
        }

        string text;
        if (s < 3600)
        {
            var m = (int)Math.Round(s / 60);
            text = abbreviated ? m + " min." : m + (m == 1 ? " minute" : " minutes");
        }
        else if (s < 86400)
        {
            var h = (int)Math.Round(s / 3600);
            text = abbreviated ? h + " hr." : h + (h == 1 ? " hour" : " hours");
        }
        else
        {
            var days = (int)Math.Round(s / 86400);
            if (days == 1)
            {
                return future ? "tomorrow" : "yesterday";
            }

            text = days + (days == 1 ? " day" : " days");
        }

        return future ? "in " + text : text + " ago";
    }
}

/// <summary>ISO 8601 times as the agents write them, parsed without a formatter on the fast path.</summary>
public static class AgentTime
{
    public static readonly DateTime Epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Unix seconds, or milliseconds from agents that write those (&gt; 1e11).</summary>
    public static DateTime? FromSeconds(double raw)
    {
        if (!double.IsFinite(raw) || raw <= 0)
        {
            return null;
        }

        var seconds = raw > 100_000_000_000 ? raw / 1000 : raw;
        return seconds > 253_402_300_000 ? null : Epoch.AddSeconds(seconds);
    }

    public static DateTime? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (Fast(text) is { } fast)
        {
            return fast;
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d.UtcDateTime : null;
    }

    private static DateTime? Fast(string b)
    {
        if (b.Length < 20 || b[4] != '-' || b[7] != '-' || b[10] != 'T' || b[13] != ':' || b[16] != ':')
        {
            return null;
        }

        static int Num(string s, int start, int end)
        {
            var v = 0;
            for (var i = start; i < end; i++)
            {
                var d = s[i] - '0';
                if (d is < 0 or > 9)
                {
                    return -1;
                }

                v = v * 10 + d;
            }

            return v;
        }

        int year = Num(b, 0, 4), month = Num(b, 5, 7), day = Num(b, 8, 10), hour = Num(b, 11, 13), minute = Num(b, 14, 16), second = Num(b, 17, 19);
        if (year < 0 || month is < 1 or > 12 || day is < 1 or > 31 || hour is < 0 or > 23 || minute is < 0 or > 59 || second is < 0 or > 60)
        {
            return null;
        }

        var index = 19;
        var fraction = 0.0;
        if (b[index] == '.')
        {
            var scale = 0.1;
            index++;
            while (index < b.Length && b[index] is >= '0' and <= '9')
            {
                fraction += (b[index] - '0') * scale;
                scale /= 10;
                index++;
            }
        }

        if (index >= b.Length)
        {
            return null;
        }

        var offset = 0;
        if (b[index] == 'Z')
        {
            if (index != b.Length - 1)
            {
                return null;
            }
        }
        else if ((b[index] == '+' || b[index] == '-') && b.Length == index + 6 && b[index + 3] == ':')
        {
            int hh = Num(b, index + 1, index + 3), mm = Num(b, index + 4, index + 6);
            if (hh < 0 || mm < 0)
            {
                return null;
            }

            offset = (hh * 3600 + mm * 60) * (b[index] == '+' ? 1 : -1);
        }
        else
        {
            return null;
        }

        try
        {
            var dt = new DateTime(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)), hour, minute, Math.Min(second, 59), DateTimeKind.Utc);
            return dt.AddSeconds(-offset).AddSeconds(fraction);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
