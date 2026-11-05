// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Trayloom.Features.Island;

namespace Trayloom.Island.Harness;

/// <summary>Checks of the safety rules of the island: ICS source addresses, secret-file deletion, toast matching and ICS limits.</summary>
internal static class SafetyChecks
{
    public static void Run(Action<bool, string> check)
    {
        Urls(check);
        Secrets(check);
        Toasts(check);
        IcsLimits(check);
        check(BrightnessService.MinimumLevel is >= 0.05 and < 0.5, "brightness: the island never writes below 5 %");
    }

    private static void Urls(Action<bool, string> check)
    {
        check(CalendarService.NormalizeIcsUrl("webcal://example.com/a.ics") == "https://example.com/a.ics", "ics url: webcal becomes https");
        check(CalendarService.NormalizeIcsUrl("  WEBCAL://example.com/a.ics ") == "https://example.com/a.ics", "ics url: webcal is trimmed and case-insensitive");
        check(CalendarService.NormalizeIcsUrl("https://example.com/a.ics") == "https://example.com/a.ics", "ics url: https is kept");
        check(CalendarService.NormalizeIcsUrl("http://example.com/a.ics") is null, "ics url: plain http is refused");
        check(CalendarService.NormalizeIcsUrl("ftp://example.com/a.ics") is null, "ics url: ftp is refused");
        check(CalendarService.NormalizeIcsUrl("file:///C:/a.ics") is null, "ics url: file is refused");
        check(CalendarService.NormalizeIcsUrl(@"\server\share\a.ics") is null, "ics url: a UNC path is not an address");
        check(CalendarService.NormalizeIcsUrl("https://user:pw@example.com/a.ics") is null, "ics url: user-info is refused");
        check(CalendarService.NormalizeIcsUrl("https://example.com/a\n.ics") is null, "ics url: control characters are refused");
        check(CalendarService.NormalizeIcsUrl("") is null && CalendarService.NormalizeIcsUrl(null) is null, "ics url: empty is refused");
        check(CalendarService.IsGoogleCalendarAddress("https://calendar.google.com/calendar/ical/x/private-abc/basic.ics"), "ics url: a Google secret address is recognised");
        check(!CalendarService.IsGoogleCalendarAddress("https://example.com/a.ics"), "ics url: another host is not Google");
        check(CalendarService.LooksLikeUrl("http://a") && CalendarService.LooksLikeUrl("webcal://a") && !CalendarService.LooksLikeUrl(@"C:\a.ics"), "ics url: URL versus file detection");
    }

    private static void Secrets(Action<bool, string> check)
    {
        check(SecretStore.ShouldDelete(new CryptographicException()), "secrets: a decryption failure deletes the file");
        check(SecretStore.ShouldDelete(new JsonException()), "secrets: a JSON failure deletes the file");
        check(!SecretStore.ShouldDelete(new IOException()), "secrets: an I/O error keeps the file");
        check(!SecretStore.ShouldDelete(new UnauthorizedAccessException()), "secrets: a denied access keeps the file");
        check(!SecretStore.ShouldDelete(new ArgumentException()), "secrets: a bad argument keeps the file");

        var dir = Path.Combine(Path.GetTempPath(), "trayloom-secrets-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SecretStore(dir);
            store.Write("probe", "{\"v\":1}");
            check(store.TryRead("probe", out var json) && json == "{\"v\":1}", "secrets: a good file reads back");
            var path = Path.Combine(dir, "probe.bin");
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                check(!store.TryRead("probe", out _), "secrets: a locked file reads as missing");
            }

            check(File.Exists(path), "secrets: a locked file is kept");
            File.WriteAllBytes(path, [1, 2, 3, 4]);
            check(!store.TryRead("probe", out _) && !File.Exists(path), "secrets: a corrupt file is deleted");
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static void Toasts(Action<bool, string> check)
    {
        check(NotificationsService.IsToastElement("New notification", ""), "toast: matched by name");
        check(NotificationsService.IsToastElement("", "NormalToastView"), "toast: matched by automation id");
        check(!NotificationsService.IsToastElement("Notification Center", "NotificationCenterWindow"), "toast: the notification center is not a banner");
        check(!NotificationsService.IsToastElement(null, null), "toast: nothing is not a banner");
    }

    private static void IcsLimits(Action<bool, string> check)
    {
        // A huge file of folded lines must unfold in linear time and keep a bounded line.
        var sb = new StringBuilder("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:a\r\nDTSTART:20261110T040000Z\r\nSUMMARY:x\r\n");
        for (var i = 0; i < 100_000; i++)
        {
            sb.Append(" aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\r\n");
        }

        sb.Append("END:VEVENT\r\nEND:VCALENDAR\r\n");
        var watch = Stopwatch.StartNew();
        var cal = Ics.Parse(sb.ToString());
        watch.Stop();
        check(watch.ElapsedMilliseconds < 2000, $"ics: 100 000 folded lines unfold quickly ({watch.ElapsedMilliseconds} ms)");
        check(cal.Events.Count == 1 && cal.Events[0].Summary.Length <= Ics.MaxLineLength, "ics: an unfolded line is capped");

        var many = new StringBuilder("BEGIN:VCALENDAR\r\n");
        for (var i = 0; i < Ics.MaxEvents + 50; i++)
        {
            many.Append("BEGIN:VEVENT\r\nUID:e").Append(i).Append("\r\nDTSTART:20261110T040000Z\r\nEND:VEVENT\r\n");
        }

        many.Append("END:VCALENDAR\r\n");
        var parsed = Ics.Parse(many.ToString());
        check(parsed.Events.Count == Ics.MaxEvents, "ics: events per file are capped");

        var daily = Ics.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:d\r\nDTSTART:20261110T040000Z\r\nDTEND:20261110T050000Z\r\nRRULE:FREQ=DAILY;INTERVAL=99999999999\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");
        var list = Ics.Expand(daily, new DateTime(2026, 11, 1), new DateTime(2027, 12, 1), TimeZoneInfo.Utc).ToList();
        check(list.Count == 1, "ics: an absurd INTERVAL shows the first occurrence only");

        var zones = true;
        for (var i = 0; i < 500; i++)
        {
            zones &= Ics.FindZone("Nowhere/Zone" + i) is null;
        }

        check(zones, "ics: unknown zone ids are tolerated (and the cache stays bounded)");
    }
}
