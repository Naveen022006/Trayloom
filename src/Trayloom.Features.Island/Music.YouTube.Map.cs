// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Trayloom.Features.Island;

/// <summary>Parses the ISO 8601 durations the YouTube Data API returns ("PT1H2M3S").</summary>
internal static class Iso8601
{
    private static readonly Regex Pattern = new(
        @"^P(?:(?<d>\d+)D)?(?:T(?:(?<h>\d+)H)?(?:(?<m>\d+)M)?(?:(?<s>\d+(?:\.\d+)?)S)?)?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>The length in seconds; 0 for anything that is not a duration (and for a live stream's "P0D").</summary>
    public static double Seconds(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var match = Pattern.Match(text.Trim());
        if (!match.Success)
        {
            return 0;
        }

        double Part(string name, double unit) =>
            match.Groups[name].Success && double.TryParse(match.Groups[name].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v * unit : 0;

        return Part("d", 86400) + Part("h", 3600) + Part("m", 60) + Part("s", 1);
    }
}

/// <summary>Validation of YouTube ids and of the API key, so only well-formed values reach a URL, a header or the shell.</summary>
internal static class YouTubeIds
{
    private static readonly Regex Video = new(@"^[A-Za-z0-9_-]{11}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex PlaylistId = new(@"^[A-Za-z0-9_-]{2,64}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Key = new(@"^[A-Za-z0-9_-]{20,128}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>True for an 11-character video id.</summary>
    public static bool IsVideo(string? id) => id is not null && Video.IsMatch(id);

    /// <summary>True for a playlist id (2 to 64 URL-safe characters; "LM" is the liked-music list).</summary>
    public static bool IsPlaylist(string? id) => id is not null && PlaylistId.IsMatch(id);

    /// <summary>True when the text looks like an API key; this also keeps anything unusual out of the request header.</summary>
    public static bool IsKey(string? key) => key is not null && Key.IsMatch(key);
}

/// <summary>Builds YouTube and YouTube Music addresses from validated ids and escaped text.</summary>
internal static class YouTubeLinks
{
    /// <summary>The address of the home page of the service.</summary>
    public static string Home(bool music) => music ? "https://music.youtube.com" : "https://www.youtube.com";

    /// <summary>A watch link for a video, optionally inside a playlist; empty when an id is not valid.</summary>
    public static string Watch(string id, bool music, string? playlistId = null)
    {
        if (!YouTubeIds.IsVideo(id))
        {
            return "";
        }

        var url = Home(music) + "/watch?v=" + Uri.EscapeDataString(id);
        if (playlistId is not null && YouTubeIds.IsPlaylist(playlistId))
        {
            url += "&list=" + Uri.EscapeDataString(playlistId);
        }

        return url;
    }

    /// <summary>A link to a playlist page; empty when the id is not valid.</summary>
    public static string Playlist(string id, bool music) =>
        YouTubeIds.IsPlaylist(id) ? Home(music) + "/playlist?list=" + Uri.EscapeDataString(id) : "";

    /// <summary>A link to the service's own search for <paramref name="query"/>.</summary>
    public static string Search(string query, bool music) =>
        music
            ? "https://music.youtube.com/search?q=" + Uri.EscapeDataString(query ?? "")
            : "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(query ?? "");
}

/// <summary>When the daily YouTube Data API quota resets: midnight Pacific time.</summary>
internal static class YouTubeQuota
{
    /// <summary>The first midnight Pacific time after <paramref name="nowUtc"/>, in UTC.</summary>
    /// <param name="nowUtc">The current time in UTC.</param>
    /// <param name="zone">The zone to use; null finds "Pacific Standard Time" (which follows daylight saving), else a fixed UTC-8.</param>
    public static DateTime ResetAfter(DateTime nowUtc, TimeZoneInfo? zone = null)
    {
        zone ??= FindPacific();
        var utc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        var midnight = DateTime.SpecifyKind(local.Date.AddDays(1), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(midnight, zone);
    }

    private static TimeZoneInfo FindPacific()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("Pacific", TimeSpan.FromHours(-8), "Pacific", "Pacific");
        }
    }
}

/// <summary>Pure mappers from YouTube Data API JSON to <see cref="StreamItem"/>s.</summary>
internal static class YouTubeMap
{
    private const string TopicSuffix = " - Topic";

    /// <summary>Decodes HTML entities (the API escapes titles), trims, and drops a trailing " - Topic" (auto-generated artist channels).</summary>
    public static string CleanTitle(string? text)
    {
        var value = WebUtility.HtmlDecode(text ?? "").Trim();
        if (value.EndsWith(TopicSuffix, StringComparison.OrdinalIgnoreCase) && value.Length > TopicSuffix.Length)
        {
            value = value[..^TopicSuffix.Length].TrimEnd();
        }

        return value;
    }

    /// <summary>
    /// The thumbnail to show: medium first, then high, then default. Only addresses <see cref="StreamArt.Allowed"/> accepts are returned.
    /// </summary>
    public static string? Image(JsonElement snippet)
    {
        if (snippet.ValueKind != JsonValueKind.Object || !snippet.TryGetProperty("thumbnails", out var thumbs) || thumbs.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var size in new[] { "medium", "high", "default" })
        {
            var url = Str(Get(thumbs, size), "url");
            if (StreamArt.Allowed(url))
            {
                return url;
            }
        }

        return null;
    }

    /// <summary>Maps a <c>search</c> response (type video or playlist) to items; entries without a valid id are skipped.</summary>
    public static List<StreamItem> Search(JsonElement root, StreamItemKind kind)
    {
        var list = new List<StreamItem>();
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var entry in items.EnumerateArray())
        {
            var idObject = Get(entry, "id");
            var id = Str(idObject, kind == StreamItemKind.Playlist ? "playlistId" : "videoId");
            var valid = kind == StreamItemKind.Playlist ? YouTubeIds.IsPlaylist(id) : YouTubeIds.IsVideo(id);
            if (!valid)
            {
                continue;
            }

            var snippet = Get(entry, "snippet");
            var title = CleanTitle(Str(snippet, "title"));
            if (title.Length == 0)
            {
                continue;
            }

            list.Add(new StreamItem(kind, id!, title, CleanTitle(Str(snippet, "channelTitle")))
            {
                ImageUrl = Image(snippet),
            });
        }

        return list;
    }

    /// <summary>Maps a <c>playlistItems</c> response to video items; deleted and private videos are skipped.</summary>
    public static List<StreamItem> PlaylistItems(JsonElement root)
    {
        var list = new List<StreamItem>();
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var entry in items.EnumerateArray())
        {
            var snippet = Get(entry, "snippet");
            var id = Str(Get(snippet, "resourceId"), "videoId");
            var title = CleanTitle(Str(snippet, "title"));
            if (!YouTubeIds.IsVideo(id) || title.Length == 0 || IsPlaceholder(title))
            {
                continue;
            }

            var privacy = Str(Get(entry, "status"), "privacyStatus");
            if (string.Equals(privacy, "private", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var owner = Str(snippet, "videoOwnerChannelTitle");
            if (string.IsNullOrWhiteSpace(owner))
            {
                owner = Str(snippet, "channelTitle");
            }

            list.Add(new StreamItem(StreamItemKind.Video, id!, title, CleanTitle(owner))
            {
                ImageUrl = Image(snippet),
            });
        }

        return list;
    }

    /// <summary>Maps a <c>videos?part=contentDetails</c> response to seconds by video id.</summary>
    public static Dictionary<string, double> Durations(JsonElement root)
    {
        var map = new Dictionary<string, double>(StringComparer.Ordinal);
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return map;
        }

        foreach (var entry in items.EnumerateArray())
        {
            var id = Str(entry, "id");
            if (YouTubeIds.IsVideo(id))
            {
                map[id!] = Iso8601.Seconds(Str(Get(entry, "contentDetails"), "duration"));
            }
        }

        return map;
    }

    /// <summary>Copies the known durations onto the items.</summary>
    public static List<StreamItem> WithDurations(IEnumerable<StreamItem> items, IReadOnlyDictionary<string, double> durations) =>
        items.Select(i => i.Kind == StreamItemKind.Video && durations.TryGetValue(i.Id, out var d) && d > 0 ? i with { Duration = d } : i).ToList();

    private static bool IsPlaceholder(string title) =>
        title.Equals("Deleted video", StringComparison.OrdinalIgnoreCase) || title.Equals("Private video", StringComparison.OrdinalIgnoreCase);

    private static JsonElement Get(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    private static string? Str(JsonElement element, string name) =>
        Get(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
}
