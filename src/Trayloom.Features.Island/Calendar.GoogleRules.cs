// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Text.RegularExpressions;
using Trayloom.Core;

namespace Trayloom.Features.Island;

/// <summary>Why a pasted address was refused by <see cref="GoogleRules.TryNormalize"/>.</summary>
internal enum GoogleUrlProblem
{
    None,
    Empty,
    NotHttps,
    WrongHost,
    WrongPath,
}

/// <summary>Which calendar sources feed the island, as resolved from the "Show events from" setting.</summary>
internal enum CalendarSourceMode
{
    Google,
    Windows,
    Both,
}

/// <summary>The two Google Calendar settings (registered here so <c>Prefs.cs</c> stays untouched).</summary>
internal static class GoogleCalendarKeys
{
    /// <summary>"auto", "google", "windows" or "both".</summary>
    public const string Source = "notchCalendarSource";

    /// <summary>Minutes between checks for changes: 5, 15, 30 or 60.</summary>
    public const string Interval = "notchCalendarGoogleInterval";

    /// <summary>Registers the defaults; safe to call more than once.</summary>
    public static void Register(IAppSettings app)
    {
        app.Register(Source, "auto");
        app.Register(Interval, 15);
    }
}

/// <summary>Pure rules for Google Calendar secret addresses, meeting links and the colour palette. Nothing here touches the network.</summary>
internal static class GoogleRules
{
    /// <summary>The calendar colours offered to the user, named after Google's own.</summary>
    public static readonly IReadOnlyList<(string Name, string Hex)> Palette =
    [
        ("Peacock", "#039BE5"),
        ("Tomato", "#D50000"),
        ("Sage", "#33B679"),
        ("Banana", "#F6BF26"),
        ("Grape", "#8E24AA"),
        ("Tangerine", "#F4511E"),
        ("Lavender", "#7986CB"),
        ("Basil", "#0B8043"),
        ("Flamingo", "#E67C73"),
        ("Blueberry", "#3F51B5"),
        ("Graphite", "#616161"),
        ("Cocoa", "#795548"),
    ];

    private static readonly Regex PathRule = new(@"^/calendar/ical/[^/]+/(private-[A-Za-z0-9]+|public)/basic\.ics$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex UrlInText = new(@"https://[^\s<>""'\\]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Checks a pasted address. Accepts https (or webcal) on exactly calendar.google.com with the iCal path; the query and fragment are
    /// dropped so only the canonical address is ever stored.
    /// </summary>
    public static bool TryNormalize(string? input, out string url, out GoogleUrlProblem problem)
    {
        url = "";
        var text = input?.Trim() ?? "";
        if (text.Length == 0)
        {
            problem = GoogleUrlProblem.Empty;
            return false;
        }

        if (text.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase))
        {
            text = "https://" + text["webcal://".Length..];
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            problem = GoogleUrlProblem.NotHttps;
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            problem = GoogleUrlProblem.NotHttps;
            return false;
        }

        // A user-info part or another port would let the address point somewhere other than the host it appears to name.
        if (!string.Equals(uri.Host, "calendar.google.com", StringComparison.OrdinalIgnoreCase) || uri.Port != 443 || uri.UserInfo.Length > 0)
        {
            problem = GoogleUrlProblem.WrongHost;
            return false;
        }

        if (!PathRule.IsMatch(uri.AbsolutePath))
        {
            problem = GoogleUrlProblem.WrongPath;
            return false;
        }

        url = "https://calendar.google.com" + uri.AbsolutePath;
        problem = GoogleUrlProblem.None;
        return true;
    }

    /// <summary>
    /// A form of the address that is safe to show or log. The secret part is never included, so this returns the same text for every
    /// address; logs name a feed by its id instead.
    /// </summary>
    public static string Redact(string url) => "calendar.google.com/…/basic.ics";

    /// <summary>
    /// The first video-call link found in the conference field, the URL field, the location and the description, in that order.
    /// Only https links to well-known meeting hosts count, so a calendar cannot make the island open an arbitrary address.
    /// </summary>
    public static string? MeetingLink(string? conference, string? url, string? location, string? description)
    {
        var desc = description?.Replace("\\n", "\n", StringComparison.Ordinal);
        foreach (var text in new[] { conference, url, location, desc })
        {
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            foreach (Match m in UrlInText.Matches(text))
            {
                var link = m.Value.TrimEnd(')', '.', ',', '>');
                if (IsMeetingUrl(link))
                {
                    return link;
                }
            }
        }

        return null;
    }

    /// <summary>The source mode for a setting value and the number of Google calendars added; "auto" prefers Google once there is one.</summary>
    public static CalendarSourceMode Resolve(string? setting, int feeds) => (setting ?? "").Trim().ToLowerInvariant() switch
    {
        "google" => CalendarSourceMode.Google,
        "windows" => CalendarSourceMode.Windows,
        "both" => CalendarSourceMode.Both,
        _ => feeds > 0 ? CalendarSourceMode.Google : CalendarSourceMode.Windows,
    };

    /// <summary>The first palette colour that is not in <paramref name="used"/>; once all are used, the palette is cycled.</summary>
    public static string NextColor(IEnumerable<string> used)
    {
        var taken = new HashSet<string>(used, StringComparer.OrdinalIgnoreCase);
        foreach (var (_, hex) in Palette)
        {
            if (!taken.Contains(hex))
            {
                return hex;
            }
        }

        return Palette[taken.Count % Palette.Count].Hex;
    }

    /// <summary>Parses <c>#RRGGBB</c>; falls back to the first palette colour.</summary>
    public static System.Windows.Media.Color ParseColor(string? hex)
    {
        var text = (hex ?? "").TrimStart('#');
        if (text.Length == 6 && int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
        {
            return System.Windows.Media.Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        }

        return ParseColor(Palette[0].Hex);
    }

    /// <summary>The Google Calendar day view for <paramref name="d"/>.</summary>
    public static string DayUrl(DateTime d) =>
        string.Create(CultureInfo.InvariantCulture, $"https://calendar.google.com/calendar/r/day/{d.Year}/{d.Month}/{d.Day}");

    /// <summary>True for an https address on one of the supported meeting hosts.</summary>
    private static bool IsMeetingUrl(string link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        return host is "meet.google.com" or "zoom.us" or "teams.microsoft.com" or "teams.live.com" or "whereby.com" or "meet.goto.com" or "chime.aws" or "webex.com"
            || host.EndsWith(".zoom.us", StringComparison.Ordinal)
            || host.EndsWith(".webex.com", StringComparison.Ordinal);
    }
}
