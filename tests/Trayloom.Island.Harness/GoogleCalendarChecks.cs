// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Net;
using System.Net.Http;
using System.Text;
using Trayloom.Core;
using Trayloom.Features.Island;

namespace Trayloom.Island.Harness;

/// <summary>
/// Checks of the Google Calendar rules (<see cref="GoogleRules"/>), the iCalendar reader (<see cref="Ics"/>) and the encrypted storage and
/// HTTP handling of <see cref="GoogleCalendarSource"/>. Calendar text is inline and HTTP is a fake handler: nothing touches the network.
/// The local zone is fixed to India Standard Time so the expected times do not depend on the machine.
/// </summary>
internal static class GoogleCalendarChecks
{
    private const string Secret = "https://calendar.google.com/calendar/ical/me%40example.com/private-0123abcd4567ef89/basic.ics";

    private static Action<bool, string> _check = (_, _) => { };
    private static TimeZoneInfo _ist = TimeZoneInfo.Utc;

    public static void Run(Action<bool, string> check)
    {
        _check = check;
        _ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        Urls();
        Meeting();
        Modes();
        Palette();
        Times();
        Recurrence();
        Exceptions();
        DaylightSaving();
        Text();
        Unsupported();
        Mapping();
        Storage();
        Source();
    }

    private static void Check(bool ok, string what) => _check(ok, "gcal: " + what);

    // ---------------------------------------------------------------- helpers

    private static string Cal(params string[] lines) => "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" + string.Join("\r\n", lines) + "\r\nEND:VCALENDAR\r\n";

    private static string Ev(params string[] lines) => "BEGIN:VEVENT\r\n" + string.Join("\r\n", lines) + "\r\nEND:VEVENT";

    private static List<IcsOccurrence> Occ(string text, DateTime? from = null, DateTime? to = null) =>
        [.. Ics.Expand(Ics.Parse(text), from ?? new DateTime(2026, 11, 1), to ?? new DateTime(2026, 12, 1), _ist).OrderBy(o => o.StartLocal)];

    private static bool Is(IcsOccurrence o, DateTime start) => o.StartLocal == start;

    // ---------------------------------------------------------------- rules

    private static void Urls()
    {
        Check(GoogleRules.TryNormalize(Secret, out var url, out var problem) && url == Secret && problem == GoogleUrlProblem.None, "the private https form is accepted");
        Check(GoogleRules.TryNormalize("  " + Secret + "  ", out url, out _) && url == Secret, "surrounding spaces are trimmed");
        Check(GoogleRules.TryNormalize(Secret.Replace("https://", "webcal://"), out url, out _) && url == Secret, "webcal becomes https");
        Check(GoogleRules.TryNormalize(Secret + "?foo=1#x", out url, out _) && url == Secret, "the query and fragment are stripped");
        Check(GoogleRules.TryNormalize("https://calendar.google.com/calendar/ical/en.indian%23holiday%40group.v.calendar.google.com/public/basic.ics", out _, out _), "the public form is accepted");
        Check(!GoogleRules.TryNormalize(Secret.Replace("https://", "http://"), out _, out problem) && problem == GoogleUrlProblem.NotHttps, "http is rejected");
        Check(!GoogleRules.TryNormalize("https://example.com/calendar/ical/x/private-abc/basic.ics", out _, out problem) && problem == GoogleUrlProblem.WrongHost, "another host is rejected");
        Check(!GoogleRules.TryNormalize("https://calendar.google.com.evil.example/calendar/ical/x/private-abc/basic.ics", out _, out _), "a look-alike host is rejected");
        Check(!GoogleRules.TryNormalize("https://calendar.google.com:8443/calendar/ical/x/private-abc/basic.ics", out _, out _), "another port is rejected");
        Check(!GoogleRules.TryNormalize("https://user:pw@calendar.google.com/calendar/ical/x/private-abc/basic.ics", out _, out _), "user info is rejected");
        Check(!GoogleRules.TryNormalize("https://calendar.google.com/calendar/ical/x/private-abc/other.ics", out _, out problem) && problem == GoogleUrlProblem.WrongPath, "a path without basic.ics is rejected");
        Check(!GoogleRules.TryNormalize("https://calendar.google.com/calendar/ical/x/y/z/basic.ics", out _, out _), "an extra path segment is rejected");
        Check(!GoogleRules.TryNormalize("", out _, out problem) && problem == GoogleUrlProblem.Empty, "an empty address is rejected");
        Check(!GoogleRules.TryNormalize("not a url", out _, out _), "text that is not an address is rejected");
        Check(!GoogleRules.Redact(Secret).Contains("private-", StringComparison.Ordinal) && !GoogleRules.Redact(Secret).Contains("example.com", StringComparison.Ordinal), "Redact never contains the secret");
        Check(GoogleRules.Redact(Secret) == "calendar.google.com/…/basic.ics", "Redact has the documented form");
        Check(!new GoogleFeed("g-00000001", Secret, "Mine", "#039BE5").ToString().Contains("private-", StringComparison.Ordinal), "a feed prints without its address");
    }

    private static void Meeting()
    {
        const string meet = "https://meet.google.com/abc-defg-hij";
        Check(GoogleRules.MeetingLink(meet, null, null, "Join: https://zoom.us/j/1") == meet, "the conference field beats the description");
        Check(GoogleRules.MeetingLink(null, null, null, "Join: https://zoom.us/j/1).") == "https://zoom.us/j/1", "a trailing )., is trimmed");
        Check(GoogleRules.MeetingLink(null, null, null, "see <https://teams.microsoft.com/l/meetup-join/x>") == "https://teams.microsoft.com/l/meetup-join/x", "a trailing > is trimmed");
        Check(GoogleRules.MeetingLink("http://meet.google.com/abc", null, null, null) is null, "http is rejected");
        Check(GoogleRules.MeetingLink(null, null, null, "https://example.com/meet") is null, "an unknown host is rejected");
        Check(GoogleRules.MeetingLink(null, null, null, "https://evilzoom.us/j/1") is null, "a look-alike host is rejected");
        Check(GoogleRules.MeetingLink(null, null, null, "https://acme.zoom.us/j/1") == "https://acme.zoom.us/j/1", "a zoom subdomain is accepted");
        Check(GoogleRules.MeetingLink(null, null, null, "https://acme.webex.com/meet/x") is not null, "a webex subdomain is accepted");
        Check(GoogleRules.MeetingLink(null, null, null, "Notes\\nhttps://whereby.com/room") == "https://whereby.com/room", "an escaped newline is unescaped");
        Check(GoogleRules.MeetingLink(null, null, "https://chime.aws/123", "https://zoom.us/j/1") == "https://chime.aws/123", "the location beats the description");
        Check(GoogleRules.MeetingLink(null, null, null, "https://example.com/x then https://meet.goto.com/y") == "https://meet.goto.com/y", "the first link on a known host is used");
        Check(GoogleRules.MeetingLink(null, null, null, null) is null, "no text, no link");
    }

    private static void Modes()
    {
        Check(GoogleRules.Resolve("auto", 0) == CalendarSourceMode.Windows, "auto with no calendars is Windows");
        Check(GoogleRules.Resolve("auto", 1) == CalendarSourceMode.Google, "auto with a calendar is Google");
        Check(GoogleRules.Resolve("google", 0) == CalendarSourceMode.Google && GoogleRules.Resolve("windows", 3) == CalendarSourceMode.Windows && GoogleRules.Resolve("both", 0) == CalendarSourceMode.Both, "explicit values are kept");
        Check(GoogleRules.Resolve(null, 2) == CalendarSourceMode.Google && GoogleRules.Resolve("nonsense", 0) == CalendarSourceMode.Windows, "unknown values behave as auto");
    }

    private static void Palette()
    {
        Check(GoogleRules.Palette.Count == 12 && GoogleRules.Palette[0].Hex == "#039BE5" && GoogleRules.Palette[11].Hex == "#795548", "the palette has 12 colours, Peacock first and Cocoa last");
        Check(GoogleRules.NextColor([]) == "#039BE5", "the first feed gets Peacock");
        Check(GoogleRules.NextColor(["#039BE5", "#d50000"]) == "#33B679", "the next feed gets the first unused colour");
        Check(GoogleRules.NextColor(GoogleRules.Palette.Select(p => p.Hex)) is { Length: 7 }, "a full palette still gives a colour");
        var c = GoogleRules.ParseColor("#D50000");
        Check(c.R == 0xD5 && c.G == 0 && c.B == 0, "ParseColor reads #RRGGBB");
        Check(GoogleRules.DayUrl(new DateTime(2026, 11, 5)) == "https://calendar.google.com/calendar/r/day/2026/11/5", "DayUrl has no zero padding");
    }

    // ---------------------------------------------------------------- reader

    private static void Times()
    {
        var zoned = Occ(Cal(Ev("UID:a", "DTSTART;TZID=America/New_York:20261110T090000", "DTEND;TZID=America/New_York:20261110T100000", "SUMMARY:NY")));
        Check(zoned.Count == 1 && Is(zoned[0], new DateTime(2026, 11, 10, 19, 30, 0)) && zoned[0].EndLocal == new DateTime(2026, 11, 10, 20, 30, 0) && !zoned[0].AllDay, "TZID New York 09:00 is 19:30 India time");

        var utc = Occ(Cal(Ev("UID:b", "DTSTART:20261110T040000Z", "DTEND:20261110T053000Z")));
        Check(utc.Count == 1 && Is(utc[0], new DateTime(2026, 11, 10, 9, 30, 0)) && utc[0].EndLocal == new DateTime(2026, 11, 10, 11, 0, 0), "UTC times convert to local");

        var day = Occ(Cal(Ev("UID:c", "DTSTART;VALUE=DATE:20261110", "DTEND;VALUE=DATE:20261112")));
        Check(day.Count == 1 && day[0].AllDay && Is(day[0], new DateTime(2026, 11, 10)) && day[0].EndLocal == new DateTime(2026, 11, 12), "an all-day event spans two days through its exclusive end");

        var oneDay = Occ(Cal(Ev("UID:c2", "DTSTART;VALUE=DATE:20261110")));
        Check(oneDay.Count == 1 && oneDay[0].EndLocal == new DateTime(2026, 11, 11), "an all-day event without an end is one day");

        var duration = Occ(Cal(Ev("UID:d", "DTSTART:20261110T040000Z", "DURATION:PT45M")));
        Check(duration.Count == 1 && duration[0].EndLocal - duration[0].StartLocal == TimeSpan.FromMinutes(45), "DURATION:PT45M sets the length");

        var mozilla = Occ(Cal(Ev("UID:m", "DTSTART;TZID=\"/mozilla.org/20050126_1/America/New_York\":20261110T090000", "DTEND;TZID=\"/mozilla.org/20050126_1/America/New_York\":20261110T100000")));
        Check(mozilla.Count == 1 && Is(mozilla[0], new DateTime(2026, 11, 10, 19, 30, 0)), "a quoted /mozilla.org/ TZID is understood");

        var unknown = Occ(Cal("X-WR-TIMEZONE:Asia/Kolkata", Ev("UID:u", "DTSTART;TZID=Not/AZone:20261110T090000", "DTEND;TZID=Not/AZone:20261110T100000")));
        Check(unknown.Count == 1 && Is(unknown[0], new DateTime(2026, 11, 10, 9, 0, 0)), "an unknown TZID falls back to X-WR-TIMEZONE");

        Check(Ics.NormalizeTzid("/mozilla.org/20050126_1/America/Argentina/Buenos_Aires") == "America/Argentina/Buenos_Aires", "the mozilla prefix is stripped");
        Check(Ics.ParseDuration("P1DT2H30M") == new TimeSpan(1, 2, 30, 0) && Ics.ParseDuration("-PT15M") == TimeSpan.FromMinutes(-15) && Ics.ParseDuration("P1W") == TimeSpan.FromDays(7) && Ics.ParseDuration("junk") is null, "ISO durations are parsed");

        var outside = Occ(Cal(Ev("UID:o", "DTSTART:20270110T040000Z", "DTEND:20270110T050000Z")));
        Check(outside.Count == 0, "an event outside the window is left out");
    }

    private static void Recurrence()
    {
        var weekly = Occ(Cal(Ev("UID:w", "DTSTART;TZID=Asia/Kolkata:20261102T100000", "DTEND;TZID=Asia/Kolkata:20261102T110000", "RRULE:FREQ=WEEKLY;BYDAY=MO,WE;COUNT=4")));
        Check(weekly.Select(o => o.StartLocal).SequenceEqual([new DateTime(2026, 11, 2, 10, 0, 0), new DateTime(2026, 11, 4, 10, 0, 0), new DateTime(2026, 11, 9, 10, 0, 0), new DateTime(2026, 11, 11, 10, 0, 0)]), "weekly Mon and Wed, count 4");

        var wide = new DateTime(2027, 2, 1);
        var second = Occ(Cal(Ev("UID:m1", "DTSTART;TZID=Asia/Kolkata:20261110T100000", "DTEND;TZID=Asia/Kolkata:20261110T110000", "RRULE:FREQ=MONTHLY;BYDAY=2TU")), to: wide);
        Check(second.Select(o => o.StartLocal.Date).SequenceEqual([new DateTime(2026, 11, 10), new DateTime(2026, 12, 8), new DateTime(2027, 1, 12)]), "monthly on the second Tuesday");

        var lastFriday = Occ(Cal(Ev("UID:m2", "DTSTART;TZID=Asia/Kolkata:20261127T100000", "DTEND;TZID=Asia/Kolkata:20261127T110000", "RRULE:FREQ=MONTHLY;BYDAY=-1FR")), to: wide);
        Check(lastFriday.Select(o => o.StartLocal.Date).SequenceEqual([new DateTime(2026, 11, 27), new DateTime(2026, 12, 25), new DateTime(2027, 1, 29)]), "monthly on the last Friday");

        var lastDay = Occ(Cal(Ev("UID:m3", "DTSTART;TZID=Asia/Kolkata:20261130T100000", "DTEND;TZID=Asia/Kolkata:20261130T110000", "RRULE:FREQ=MONTHLY;BYMONTHDAY=-1")), to: wide);
        Check(lastDay.Select(o => o.StartLocal.Date).SequenceEqual([new DateTime(2026, 11, 30), new DateTime(2026, 12, 31), new DateTime(2027, 1, 31)]), "monthly on the last day");

        var setPos = Occ(Cal(Ev("UID:m4", "DTSTART;TZID=Asia/Kolkata:20261130T100000", "DTEND;TZID=Asia/Kolkata:20261130T110000", "RRULE:FREQ=MONTHLY;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=-1")), to: wide);
        Check(setPos.Select(o => o.StartLocal.Date).SequenceEqual([new DateTime(2026, 11, 30), new DateTime(2026, 12, 31), new DateTime(2027, 1, 29)]), "monthly on the last weekday (BYSETPOS)");

        var until = Occ(Cal(Ev("UID:un", "DTSTART;TZID=Asia/Kolkata:20261110T100000", "DTEND;TZID=Asia/Kolkata:20261110T110000", "RRULE:FREQ=DAILY;UNTIL=20261112")));
        Check(until.Count == 3, "UNTIL as a date includes that day");

        var untilUtc = Occ(Cal(Ev("UID:un2", "DTSTART;TZID=Asia/Kolkata:20261110T100000", "DTEND;TZID=Asia/Kolkata:20261110T110000", "RRULE:FREQ=DAILY;UNTIL=20261111T043000Z")));
        Check(untilUtc.Count == 2, "UNTIL as a UTC instant is compared in the event's zone (10:00 India on the 11th is 04:30Z, still included)");

        var interval = Occ(Cal(Ev("UID:iv", "DTSTART;TZID=Asia/Kolkata:20261102T100000", "DTEND;TZID=Asia/Kolkata:20261102T110000", "RRULE:FREQ=WEEKLY;INTERVAL=2")));
        Check(interval.Select(o => o.StartLocal.Date).SequenceEqual([new DateTime(2026, 11, 2), new DateTime(2026, 11, 16), new DateTime(2026, 11, 30)]), "every second week");

        var yearly = Occ(Cal(Ev("UID:y", "DTSTART;VALUE=DATE:20200315", "RRULE:FREQ=YEARLY")), new DateTime(2026, 3, 1), new DateTime(2026, 4, 1));
        Check(yearly.Count == 1 && yearly[0].AllDay && yearly[0].StartLocal == new DateTime(2026, 3, 15), "a yearly all-day event from years ago lands in this year");

        var old = Occ(Cal(Ev("UID:old", "DTSTART;TZID=Asia/Kolkata:20190102T100000", "DTEND;TZID=Asia/Kolkata:20190102T110000", "RRULE:FREQ=DAILY")), new DateTime(2026, 11, 9), new DateTime(2026, 11, 12));
        Check(old.Count == 3 && Is(old[0], new DateTime(2026, 11, 9, 10, 0, 0)), "a daily series that began in 2019 is still expanded (skipping ahead)");
    }

    private static void Exceptions()
    {
        const string master = "DTSTART;TZID=Asia/Kolkata:20261110T100000";
        const string end = "DTEND;TZID=Asia/Kolkata:20261110T110000";

        var exdate = Occ(Cal(Ev("UID:e", master, end, "RRULE:FREQ=DAILY;COUNT=5", "EXDATE;TZID=Asia/Kolkata:20261112T100000")));
        Check(exdate.Count == 4 && exdate.All(o => o.StartLocal.Date != new DateTime(2026, 11, 12)), "EXDATE with a TZID removes one occurrence and COUNT still counts it");

        var exUtc = Occ(Cal(Ev("UID:e2", master, end, "RRULE:FREQ=DAILY;COUNT=3", "EXDATE:20261111T043000Z")));
        Check(exUtc.Count == 2 && exUtc.All(o => o.StartLocal.Date != new DateTime(2026, 11, 11)), "EXDATE in UTC matches by instant");

        var moved = Occ(Cal(
            Ev("UID:r", master, end, "RRULE:FREQ=DAILY;COUNT=3"),
            Ev("UID:r", "RECURRENCE-ID;TZID=Asia/Kolkata:20261111T100000", "DTSTART;TZID=Asia/Kolkata:20261111T150000", "DTEND;TZID=Asia/Kolkata:20261111T160000", "SUMMARY:Moved")));
        Check(moved.Count == 3 && moved.Any(o => Is(o, new DateTime(2026, 11, 11, 15, 0, 0)) && o.Event.Summary == "Moved") && moved.All(o => !Is(o, new DateTime(2026, 11, 11, 10, 0, 0))), "a RECURRENCE-ID override moves one instance");

        var cancelled = Occ(Cal(
            Ev("UID:r", master, end, "RRULE:FREQ=DAILY;COUNT=3"),
            Ev("UID:r", "RECURRENCE-ID;TZID=Asia/Kolkata:20261111T100000", "DTSTART;TZID=Asia/Kolkata:20261111T100000", "DTEND;TZID=Asia/Kolkata:20261111T110000", "STATUS:CANCELLED")));
        Check(cancelled.Count == 2 && cancelled.All(o => o.StartLocal.Date != new DateTime(2026, 11, 11)), "a cancelled override removes the instance");

        var master2 = Occ(Cal(Ev("UID:x", master, end, "STATUS:CANCELLED", "RRULE:FREQ=DAILY;COUNT=3")));
        Check(master2.Count == 0, "a cancelled master is skipped");

        var other = Occ(Cal(
            Ev("UID:p", master, end, "RRULE:FREQ=DAILY;COUNT=2"),
            Ev("UID:q", "RECURRENCE-ID;TZID=Asia/Kolkata:20261110T100000", "DTSTART;TZID=Asia/Kolkata:20261110T130000", "DTEND;TZID=Asia/Kolkata:20261110T140000")));
        Check(other.Count == 3, "an override of another UID does not touch this series");
    }

    private static void DaylightSaving()
    {
        var series = Occ(Cal(Ev("UID:dst", "DTSTART;TZID=America/New_York:20261030T090000", "DTEND;TZID=America/New_York:20261030T100000", "RRULE:FREQ=DAILY;COUNT=5")), new DateTime(2026, 10, 25), new DateTime(2026, 11, 10));
        var ny = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        Check(series.Count == 5, "five daily occurrences across the change");
        Check(series.All(o => TimeZoneInfo.ConvertTime(TimeZoneInfo.ConvertTimeToUtc(o.StartLocal, _ist), ny).TimeOfDay == TimeSpan.FromHours(9)), "the wall clock stays at 09:00 New York time across 1 November");
        Check(Is(series[0], new DateTime(2026, 10, 30, 18, 30, 0)) && Is(series[4], new DateTime(2026, 11, 3, 19, 30, 0)), "which is 18:30 India time before the change and 19:30 after");
    }

    private static void Text()
    {
        var folded = Ics.Parse(Cal("X-WR-CALNAME:Work\\, mostly", Ev("UID:t", "DTSTART:20261110T040000Z", "DTEND:20261110T050000Z", "SUMMARY:Hello\\, wor", " ld", "LOCATION:Room 1\\; floor 2", "DESCRIPTION:Real", "BEGIN:VALARM", "TRIGGER:-PT10M", "DESCRIPTION:Reminder", "END:VALARM")));
        Check(folded.Events.Count == 1 && folded.Events[0].Summary == "Hello, world", "a folded line and \\, are read");
        Check(folded.Events[0].Location == "Room 1; floor 2", "\\; is unescaped");
        Check(folded.Events[0].Description == "Real", "the VALARM body does not replace the description");
        Check(folded.Name == "Work, mostly", "X-WR-CALNAME is read");

        var zone = Ics.Parse(Cal("X-WR-TIMEZONE:Asia/Kolkata", Ev("UID:z", "DTSTART:20261110T040000Z")));
        Check(zone.DefaultTz == "Asia/Kolkata", "X-WR-TIMEZONE is read");

        var meet = Ics.Parse(Cal(Ev("UID:g", "DTSTART:20261110T040000Z", "X-GOOGLE-CONFERENCE:https://meet.google.com/abc-defg-hij", "URL:https://example.com/x", "STATUS:CONFIRMED")));
        Check(meet.Events[0].Conference == "https://meet.google.com/abc-defg-hij" && meet.Events[0].Url == "https://example.com/x" && meet.Events[0].Status == "CONFIRMED", "X-GOOGLE-CONFERENCE, URL and STATUS are read");

        var lf = Ics.Parse("BEGIN:VCALENDAR\nBEGIN:VEVENT\nUID:lf\nDTSTART:20261110T040000Z\nSUMMARY:Plain\nEND:VEVENT\nEND:VCALENDAR\n");
        Check(lf.Events.Count == 1 && lf.Events[0].Summary == "Plain", "bare line feeds are accepted");

        Check(Ics.Parse("garbage").Events.Count == 0 && Ics.Parse("").Events.Count == 0, "garbage reads as no events");
        Check(Ics.Parse(Cal(Ev("UID:nostart", "SUMMARY:No start"))).Events.Count == 0, "an event without a start is dropped");
    }

    private static void Unsupported()
    {
        var hour = Occ(Cal(Ev("UID:h", "DTSTART;TZID=Asia/Kolkata:20261110T100000", "DTEND;TZID=Asia/Kolkata:20261110T110000", "RRULE:FREQ=DAILY;BYHOUR=9,10;COUNT=5")));
        Check(hour.Count == 1 && Is(hour[0], new DateTime(2026, 11, 10, 10, 0, 0)), "an unsupported BYHOUR gives the first occurrence only");

        var hourly = Occ(Cal(Ev("UID:h2", "DTSTART;TZID=Asia/Kolkata:20261110T100000", "DTEND;TZID=Asia/Kolkata:20261110T110000", "RRULE:FREQ=HOURLY;COUNT=5")));
        Check(hourly.Count == 1, "HOURLY gives the first occurrence only");

        var lines = new List<string>();
        _ = Ics.Expand(Ics.Parse(Cal(Ev("UID:secret-title", "SUMMARY:Private appointment", "DTSTART;TZID=Asia/Kolkata:20261110T100000", "RRULE:FREQ=DAILY;BYWEEKNO=5"))), new DateTime(2026, 11, 1), new DateTime(2026, 12, 1), _ist, new ListLog(lines)).ToList();
        Check(lines.Count == 1 && lines[0].Contains("unsupported RRULE part", StringComparison.Ordinal) && !lines[0].Contains("Private", StringComparison.Ordinal) && !lines[0].Contains("secret-title", StringComparison.Ordinal), "the unsupported part is logged once, without the title or id");
    }

    private static void Mapping()
    {
        var cal = Ics.Parse(Cal(Ev("UID:evt", "SUMMARY:Standup", "DTSTART;TZID=Asia/Kolkata:20261110T100000", "DTEND;TZID=Asia/Kolkata:20261110T103000", "X-GOOGLE-CONFERENCE:https://meet.google.com/abc-defg-hij", "RRULE:FREQ=DAILY;COUNT=2")));
        var events = IcsReader.ToEvents(cal, new DateTime(2020, 1, 1), new DateTime(2030, 1, 1), "google:g-00000001", "Work", System.Windows.Media.Colors.Red, "google");
        Check(events.Count == 2 && events.All(e => e.Id == "google:g-00000001|evt" && e.Source == "google" && e.CalendarId == "google:g-00000001" && e.CalendarName == "Work"), "events carry the google id, source and calendar");
        Check(events.All(e => e.JoinUrl == "https://meet.google.com/abc-defg-hij" && e.WebUrl is { } w && w.StartsWith("https://calendar.google.com/calendar/r/day/", StringComparison.Ordinal)), "events carry JoinUrl and WebUrl");
        Check(events[0].CountdownKey != events[1].CountdownKey, "the occurrences of a series have different countdown keys");
        var plain = IcsReader.ToEvents(cal, new DateTime(2020, 1, 1), new DateTime(2030, 1, 1), "ics:file", "File", System.Windows.Media.Colors.Green, "ics");
        Check(plain.All(e => e.WebUrl is null), "a plain ICS source has no web link");
    }

    // ---------------------------------------------------------------- storage and HTTP

    private static void Storage()
    {
        var dir = Path.Combine(Path.GetTempPath(), "trayloom-gcal-" + Guid.NewGuid().ToString("N"));
        try
        {
            var files = new GoogleSecretFile(dir);
            files.Write("sample", "{\"url\":\"" + Secret + "\"}");
            var raw = File.ReadAllBytes(Path.Combine(dir, "sample.bin"));
            Check(!Encoding.UTF8.GetString(raw).Contains("private-", StringComparison.Ordinal), "the stored file does not contain the address in clear");
            Check(files.TryRead("sample", out var text) && text.Contains(Secret, StringComparison.Ordinal), "the stored file reads back");
            File.Copy(Path.Combine(dir, "sample.bin"), Path.Combine(dir, "other.bin"));
            Check(!files.TryRead("other", out _) && !File.Exists(Path.Combine(dir, "other.bin")), "a file moved under another name is unreadable (entropy) and is cleared");
            files.Delete("sample");
            Check(!File.Exists(Path.Combine(dir, "sample.bin")) && !files.TryRead("sample", out _), "Delete removes the file");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    private static void Source()
    {
        var dir = Path.Combine(Path.GetTempPath(), "trayloom-gcal-" + Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(Path.GetTempPath(), "trayloom-gcal-settings-" + Guid.NewGuid().ToString("N") + ".json");
        var logLines = new List<string>();
        var log = new ListLog(logLines);
        try
        {
            var app = new AppSettings(new SettingsStore(settingsPath, log));
            GoogleCalendarKeys.Register(app);
            var status = 200;
            var calls = 0;
            string? conditional = null;
            var body = Cal("X-WR-CALNAME:Family", Ev("UID:s1", "SUMMARY:Dinner", "DTSTART:20261110T130000Z", "DTEND:20261110T140000Z", "X-GOOGLE-CONFERENCE:https://meet.google.com/abc-defg-hij"));
            var handler = new FakeHandler(request =>
            {
                calls++;
                conditional = request.Headers.IfNoneMatch.FirstOrDefault()?.ToString();
                var response = new HttpResponseMessage((HttpStatusCode)status);
                if (status == 200)
                {
                    response.Content = new StringContent(body, Encoding.UTF8, "text/calendar");
                    response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"v1\"");
                }

                return response;
            });

            using (var source = new GoogleCalendarSource(app, log, handler, dir))
            {
                Check(source.AddAsync("http://example.com/x").GetAwaiter().GetResult() == GoogleAddResult.InvalidAddress && calls == 0, "a bad address is refused before any request");
                status = 404;
                Check(source.AddAsync(Secret).GetAwaiter().GetResult() == GoogleAddResult.Rejected && source.Feeds.Count == 0, "a 404 is Rejected and stores nothing");
                status = 500;
                Check(source.AddAsync(Secret).GetAwaiter().GetResult() == GoogleAddResult.Offline && source.Feeds.Count == 0, "a 500 is reported as unreachable and stores nothing");
                body = "<html>not a calendar</html>";
                status = 200;
                Check(source.AddAsync(Secret).GetAwaiter().GetResult() == GoogleAddResult.NotCalendar, "a page that is not a calendar is refused");
                body = Cal("X-WR-CALNAME:Family", Ev("UID:s1", "SUMMARY:Dinner", "DTSTART:20261110T130000Z", "DTEND:20261110T140000Z", "X-GOOGLE-CONFERENCE:https://meet.google.com/abc-defg-hij"));
                Check(source.AddAsync(Secret).GetAwaiter().GetResult() == GoogleAddResult.Added && source.Feeds.Count == 1, "a calendar is added");
                var feed = source.Feeds[0];
                Check(feed.Name == "Family" && feed.Color == "#039BE5" && System.Text.RegularExpressions.Regex.IsMatch(feed.Id, "^g-[0-9a-f]{8}$"), "the name comes from X-WR-CALNAME, the colour from the palette and the id has its form");
                Check(source.AddAsync(Secret + "?x=1").GetAwaiter().GetResult() == GoogleAddResult.AlreadyAdded, "the same address again is AlreadyAdded");
                Check(source.StatusOf(feed.Id).State == FeedState.Ok && source.StatusOf(feed.Id).LastOkUtc is not null, "the status is Ok with a time");

                var cache = Path.Combine(dir, "google-cache-" + feed.Id + ".bin");
                Check(File.Exists(Path.Combine(dir, "google.bin")) && File.Exists(cache), "both encrypted files exist");
                Check(!File.ReadAllText(Path.Combine(dir, "google.bin"), Encoding.Latin1).Contains("private-", StringComparison.Ordinal) && !File.ReadAllText(cache, Encoding.Latin1).Contains("Dinner", StringComparison.Ordinal), "neither file holds the address or a title in clear");

                var events = new List<CalEvent>();
                var calendars = new List<CalendarInfo>();
                source.RefreshNow();
                status = 304;
                source.ReadAsync(events, calendars, [], new DateTime(2026, 11, 1), new DateTime(2026, 12, 1)).GetAwaiter().GetResult();
                Check(conditional == "\"v1\"", "a refresh sends If-None-Match");
                Check(events.Count == 1 && events[0].Title == "Dinner" && events[0].JoinUrl == "https://meet.google.com/abc-defg-hij" && events[0].Source == "google", "a 304 keeps the events");
                Check(calendars.Count == 1 && calendars[0].Id == "google:" + feed.Id && calendars[0].Source == "google" && calendars[0].Account == "Google Calendar", "the calendar is listed");
                Check(source.StatusOf(feed.Id).State == FeedState.Ok, "a 304 leaves the status Ok");

                var hidden = new List<CalEvent>();
                source.ReadAsync(hidden, [], ["google:" + feed.Id], new DateTime(2026, 11, 1), new DateTime(2026, 12, 1)).GetAwaiter().GetResult();
                Check(hidden.Count == 0, "an excluded calendar shows no events");

                source.SetColor(feed.Id, "#d50000");
                source.Rename(feed.Id, "  Home  ");
                Check(source.Feeds[0].Color == "#D50000" && source.Feeds[0].Name == "Home", "colour and name can be changed");
                source.SetColor(feed.Id, "red");
                Check(source.Feeds[0].Color == "#D50000", "an invalid colour is ignored");

                status = 500;
                source.RefreshNow();
                var offline = new List<CalEvent>();
                source.ReadAsync(offline, [], [], new DateTime(2026, 11, 1), new DateTime(2026, 12, 1)).GetAwaiter().GetResult();
                Check(source.StatusOf(feed.Id).State == FeedState.Failed && offline.Count == 1, "a 500 keeps showing the last copy");
                Check(source.NextDueUtc > DateTime.UtcNow, "and the next try is later (backoff)");

                status = 404;
                source.RefreshNow();
                var rejected = new List<CalEvent>();
                source.ReadAsync(rejected, [], [], new DateTime(2026, 11, 1), new DateTime(2026, 12, 1)).GetAwaiter().GetResult();
                Check(source.StatusOf(feed.Id).State == FeedState.Rejected && rejected.Count == 0, "a 404 later marks the address as no longer accepted and hides its events");
            }

            // A second start reads the feed and its copy back; the network is not needed to show the events.
            using (var again = new GoogleCalendarSource(app, log, handler, dir))
            {
                Check(again.Feeds.Count == 1 && again.Feeds[0].Name == "Home" && again.Feeds[0].Color == "#D50000", "the calendar list survives a restart");
                status = 500;
                again.RefreshNow();
                var events = new List<CalEvent>();
                again.ReadAsync(events, [], [], new DateTime(2026, 11, 1), new DateTime(2026, 12, 1)).GetAwaiter().GetResult();
                Check(events.Count == 1 && events[0].Title == "Dinner", "offline after a restart the cached events still show");
                Check(again.StatusOf(again.Feeds[0].Id).State == FeedState.Failed, "and the status says the check failed");

                var id = again.Feeds[0].Id;
                again.Remove(id);
                Check(again.Feeds.Count == 0 && !File.Exists(Path.Combine(dir, "google-cache-" + id + ".bin")), "removing deletes the cached copy");
                using var third = new GoogleCalendarSource(app, log, handler, dir);
                Check(third.Feeds.Count == 0, "a removed calendar stays removed after a restart");
            }

            Check(logLines.Count > 0 && logLines.All(l => !l.Contains("private-", StringComparison.Ordinal) && !l.Contains("example.com", StringComparison.Ordinal) && !l.Contains("Dinner", StringComparison.Ordinal) && !l.Contains("Family", StringComparison.Ordinal)), "the log has status lines and never the address, a title or a name");
        }
        finally
        {
            TryDelete(dir);
            try
            {
                File.Delete(settingsPath);
            }
            catch (IOException)
            {
            }
        }
    }

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
        catch (IOException)
        {
        }
    }

    private sealed class ListLog(List<string> lines) : ILog
    {
        public void Info(string message) => lines.Add(message);

        public void Warn(string message) => lines.Add(message);

        public void Error(string message, Exception? exception = null) => lines.Add(message);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
