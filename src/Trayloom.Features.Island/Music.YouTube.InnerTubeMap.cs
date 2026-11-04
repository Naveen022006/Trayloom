// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Trayloom.Features.Island;

/// <summary>One page of InnerTube results: the items, and the token that asks for the page after it (null on the last page).</summary>
internal sealed record InnerTubePage(List<StreamItem> Items, string? Continuation);

/// <summary>
/// The pure parts of the keyless YouTube Music client: the request bodies, the client version found in the site's HTML, and the mappers
/// from the web service's JSON (the one music.youtube.com itself calls) to <see cref="StreamItem"/>s. It is not an official API: every
/// mapper returns null (or skips the entry) for a shape it does not know, so a change on YouTube's side shows up as "didn't respond"
/// instead of a crash.
/// </summary>
internal static class InnerTubeMap
{
    /// <summary>The client version used until the real one has been read from the site (the one the site served on 2026-11-05).</summary>
    public const string FallbackVersion = "1.20261031.09.00";

    /// <summary>The <c>params</c> the "Songs" chip of the site's search sends.</summary>
    public const string SongsParams = "EgWKAQIIAWoSEAQQBRADEAoQCRAVEBAQDhAR";

    /// <summary>The <c>params</c> of the "Featured playlists" chip (playlists made by YouTube Music).</summary>
    public const string FeaturedPlaylistsParams = "EgeKAQQoADgBahIQBBAFEAMQChAJEBUQEBAOEBE=";

    /// <summary>The <c>params</c> of the "Community playlists" chip (playlists made by listeners).</summary>
    public const string CommunityPlaylistsParams = "EgeKAQQoAEABahIQBBAFEAMQChAJEBUQEBAOEBE=";

    private const int MinThumbnail = 60;

    private static readonly Regex VersionPattern = new(
        @"INNERTUBE_CLIENT_VERSION(?:\\x22|\\""|"")\s*:\s*(?:\\x22|\\""|"")(?<v>\d+(?:\.\d+){1,4})",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex Clock = new(@"^(?:(?<h>\d{1,3}):)?(?<m>\d{1,2}):(?<s>\d{2})$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Region2 = new(@"^[A-Z]{2}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SongCount = new(@"^(?<n>\d{1,6}) songs?$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // ---------------------------------------------------------------- requests

    /// <summary>Reads <c>INNERTUBE_CLIENT_VERSION</c> from the HTML of music.youtube.com; null when it is not there or not shaped like a version.</summary>
    public static string? ClientVersion(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return null;
        }

        var match = VersionPattern.Match(html);
        return match.Success ? match.Groups["v"].Value : null;
    }

    /// <summary>The two-letter country for the <c>gl</c> field: <paramref name="code"/> in capitals when it is two letters, else "US".</summary>
    public static string Region(string? code)
    {
        var upper = (code ?? "").Trim().ToUpperInvariant();
        return Region2.IsMatch(upper) ? upper : "US";
    }

    /// <summary>The body of a search: the client context, the query and, for a filtered search, the chip's <c>params</c>.</summary>
    public static string SearchBody(string version, string region, string query, string? filter) =>
        Body(version, region, w =>
        {
            w.WriteString("query", query);
            if (!string.IsNullOrEmpty(filter))
            {
                w.WriteString("params", filter);
            }
        });

    /// <summary>The body of "the next page" of a search, from a token an earlier answer gave.</summary>
    public static string ContinuationBody(string version, string region, string token) =>
        Body(version, region, w => w.WriteString("continuation", token));

    /// <summary>The body of a browse call for <paramref name="browseId"/> (<c>VL</c> + playlist id for a playlist).</summary>
    public static string BrowseBody(string version, string region, string browseId) =>
        Body(version, region, w => w.WriteString("browseId", browseId));

    private static string Body(string version, string region, Action<Utf8JsonWriter> rest)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteStartObject("context");
            w.WriteStartObject("client");
            w.WriteString("clientName", "WEB_REMIX");
            w.WriteString("clientVersion", version);
            w.WriteString("hl", "en");
            w.WriteString("gl", region);
            w.WriteEndObject();
            w.WriteEndObject();
            rest(w);
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    // ---------------------------------------------------------------- answers

    /// <summary>
    /// Maps a search answer (the first page, or a continuation) to items of <paramref name="kind"/>: songs become
    /// <see cref="StreamItemKind.Video"/>, playlists <see cref="StreamItemKind.Playlist"/>. Entries that are not what was asked for, or
    /// whose id is not valid, are skipped. Null when the answer has none of the shapes known (no results is an empty page, not null).
    /// </summary>
    public static InnerTubePage? Search(JsonElement root, StreamItemKind kind)
    {
        var items = new List<StreamItem>();
        string? token = null;
        var sections = At(First(At(root, "contents", "tabbedSearchResultsRenderer", "tabs")), "tabRenderer", "content", "sectionListRenderer", "contents");
        if (sections.ValueKind == JsonValueKind.Array)
        {
            foreach (var section in sections.EnumerateArray())
            {
                if (Get(section, "musicShelfRenderer") is { ValueKind: JsonValueKind.Object } shelf)
                {
                    Collect(shelf, kind, items, ref token);
                }
            }
        }
        else if (At(root, "continuationContents", "musicShelfContinuation") is { ValueKind: JsonValueKind.Object } more)
        {
            Collect(more, kind, items, ref token);
        }
        else
        {
            return null;
        }

        return new InnerTubePage(items, token);
    }

    /// <summary>Maps a browse answer for a playlist to its songs; null when the answer has no playlist in it.</summary>
    public static List<StreamItem>? PlaylistSongs(JsonElement root)
    {
        var sections = At(root, "contents", "twoColumnBrowseResultsRenderer", "secondaryContents", "sectionListRenderer", "contents");
        if (sections.ValueKind != JsonValueKind.Array)
        {
            sections = At(First(At(root, "contents", "singleColumnBrowseResultsRenderer", "tabs")), "tabRenderer", "content", "sectionListRenderer", "contents");
        }

        if (sections.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var items = new List<StreamItem>();
        foreach (var section in sections.EnumerateArray())
        {
            if (At(section, "musicPlaylistShelfRenderer", "contents") is { ValueKind: JsonValueKind.Array } rows)
            {
                foreach (var row in rows.EnumerateArray())
                {
                    if (Song(Get(row, "musicResponsiveListItemRenderer")) is { } song)
                    {
                        items.Add(song);
                    }
                }
            }
        }

        return items;
    }

    /// <summary>The length "3:25" or "1:02:03" in seconds; 0 for anything else.</summary>
    public static double ClockSeconds(string? text)
    {
        var match = Clock.Match((text ?? "").Trim());
        if (!match.Success)
        {
            return 0;
        }

        var hours = match.Groups["h"].Success ? int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture) : 0;
        return (hours * 3600) + (int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture) * 60) + int.Parse(match.Groups["s"].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The thumbnail to show from a <c>thumbnails</c> array: the smallest one at least 60 px wide whose host <see cref="StreamArt.Allowed"/>
    /// accepts, else the largest allowed one; null when there is none.
    /// </summary>
    public static string? Thumbnail(JsonElement thumbnails)
    {
        if (thumbnails.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var allowed = new List<(string Url, int Width)>();
        foreach (var thumb in thumbnails.EnumerateArray())
        {
            var url = Str(thumb, "url");
            if (StreamArt.Allowed(url))
            {
                var width = Get(thumb, "width") is { ValueKind: JsonValueKind.Number } w && w.TryGetInt32(out var n) ? n : 0;
                allowed.Add((url!, width));
            }
        }

        var fitting = allowed.Where(t => t.Width >= MinThumbnail).OrderBy(t => t.Width).Select(t => t.Url).FirstOrDefault();
        return fitting ?? allowed.OrderByDescending(t => t.Width).Select(t => t.Url).FirstOrDefault();
    }

    // ---------------------------------------------------------------- items

    private static void Collect(JsonElement shelf, StreamItemKind kind, List<StreamItem> items, ref string? token)
    {
        if (Get(shelf, "contents") is { ValueKind: JsonValueKind.Array } rows)
        {
            foreach (var row in rows.EnumerateArray())
            {
                var renderer = Get(row, "musicResponsiveListItemRenderer");
                var item = kind == StreamItemKind.Playlist ? PlaylistRow(renderer) : Song(renderer);
                if (item is not null)
                {
                    items.Add(item);
                }
            }
        }

        var next = Str(At(First(Get(shelf, "continuations")), "nextContinuationData"), "continuation");
        if (!string.IsNullOrEmpty(next) && next.Length <= 4096)
        {
            token = next;
        }
    }

    /// <summary>A song row of a search or of a playlist; null for anything that cannot be opened as a song.</summary>
    private static StreamItem? Song(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object || Str(row, "musicItemRendererDisplayPolicy") == "MUSIC_ITEM_RENDERER_DISPLAY_POLICY_GREY_OUT")
        {
            return null;
        }

        var columns = Get(row, "flexColumns");
        var first = Runs(columns, 0);
        var watch = At(First(first), "navigationEndpoint", "watchEndpoint");
        var id = Str(Get(row, "playlistItemData"), "videoId");
        if (!YouTubeIds.IsVideo(id))
        {
            id = Str(watch, "videoId");
        }

        if (!YouTubeIds.IsVideo(id) || Str(At(watch, "watchEndpointMusicSupportedConfigs", "watchEndpointMusicConfig"), "musicVideoType") == "MUSIC_VIDEO_TYPE_PODCAST_EPISODE")
        {
            return null;
        }

        var title = Join(first);
        if (title.Length == 0)
        {
            return null;
        }

        var artists = new List<string>();
        string? album = null;
        double duration = 0;
        var plain = new List<string>();
        for (var column = 1; column < Count(columns); column++)
        {
            foreach (var run in Runs(columns, column).EnumerateArraySafe())
            {
                var text = (Str(run, "text") ?? "").Trim();
                var page = Str(At(run, "navigationEndpoint", "browseEndpoint", "browseEndpointContextSupportedConfigs", "browseEndpointContextMusicConfig"), "pageType");
                if (text.Length == 0 || text is "•" or "," or "&")
                {
                    continue;
                }

                if (page is "MUSIC_PAGE_TYPE_ARTIST" or "MUSIC_PAGE_TYPE_USER_CHANNEL" or "MUSIC_PAGE_TYPE_LIBRARY_ARTIST")
                {
                    if (!artists.Contains(text))
                    {
                        artists.Add(text);
                    }
                }
                else if (page == "MUSIC_PAGE_TYPE_ALBUM")
                {
                    album ??= text;
                }
                else if (ClockSeconds(text) is var seconds and > 0)
                {
                    duration = seconds;
                }
                else if (page is null)
                {
                    plain.Add(text);
                }
            }
        }

        // The playlist view keeps the length in a column of its own.
        if (duration == 0 && Get(row, "fixedColumns") is { ValueKind: JsonValueKind.Array } fixedColumns)
        {
            duration = ClockSeconds(Join(At(First(fixedColumns), "musicResponsiveListItemFixedColumnRenderer", "text", "runs")));
        }

        // Without a linked artist (an upload, say) the first plain text is the best there is, unless it is a count like "7B plays".
        if (artists.Count == 0 && plain.Count > 0 && !plain[0].EndsWith(" plays", StringComparison.Ordinal) && !plain[0].EndsWith(" views", StringComparison.Ordinal))
        {
            artists.Add(plain[0]);
        }

        var by = string.Join(", ", artists);
        var subtitle = by.Length > 0 && album is not null ? by + " · " + album : by.Length > 0 ? by : album ?? "";
        return new StreamItem(StreamItemKind.Video, id!, title, subtitle)
        {
            Duration = duration,
            ImageUrl = Thumbnail(At(row, "thumbnail", "musicThumbnailRenderer", "thumbnail", "thumbnails")),
        };
    }

    /// <summary>A playlist row of a search; null unless it opens a playlist page (an album, an artist or a podcast is not one).</summary>
    private static StreamItem? PlaylistRow(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var browseId = Str(At(row, "navigationEndpoint", "browseEndpoint"), "browseId");
        if (browseId is null || !browseId.StartsWith("VL", StringComparison.Ordinal))
        {
            return null;
        }

        var id = browseId[2..];
        var title = Join(Runs(Get(row, "flexColumns"), 0));
        if (!YouTubeIds.IsPlaylist(id) || title.Length == 0)
        {
            return null;
        }

        // "Owner • 433K views" or "YouTube Music • 68 songs": the first two texts.
        var parts = new List<string>();
        foreach (var run in Runs(Get(row, "flexColumns"), 1).EnumerateArraySafe())
        {
            var text = (Str(run, "text") ?? "").Trim();
            if (text.Length > 0 && text != "•")
            {
                parts.Add(text);
            }
        }

        var count = parts.Count > 1 && SongCount.Match(parts[^1]) is { Success: true } m ? int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture) : 0;
        return new StreamItem(StreamItemKind.Playlist, id, title, string.Join(" · ", parts.Take(2)))
        {
            Count = count,
            ImageUrl = Thumbnail(At(row, "thumbnail", "musicThumbnailRenderer", "thumbnail", "thumbnails")),
        };
    }

    // ---------------------------------------------------------------- JSON helpers

    private static int Count(JsonElement columns) => columns.ValueKind == JsonValueKind.Array ? columns.GetArrayLength() : 0;

    /// <summary>The <c>runs</c> of flex column <paramref name="index"/>; a default element when it is not there.</summary>
    private static JsonElement Runs(JsonElement columns, int index)
    {
        if (columns.ValueKind != JsonValueKind.Array || index >= columns.GetArrayLength())
        {
            return default;
        }

        return At(columns[index], "musicResponsiveListItemFlexColumnRenderer", "text", "runs");
    }

    private static string Join(JsonElement runs)
    {
        var text = new StringBuilder();
        foreach (var run in runs.EnumerateArraySafe())
        {
            text.Append(Str(run, "text"));
        }

        return text.ToString().Trim();
    }

    private static IEnumerable<JsonElement> EnumerateArraySafe(this JsonElement element) =>
        element.ValueKind == JsonValueKind.Array ? element.EnumerateArray() : [];

    private static JsonElement First(JsonElement array) =>
        array.ValueKind == JsonValueKind.Array && array.GetArrayLength() > 0 ? array[0] : default;

    private static JsonElement At(JsonElement element, params string[] path)
    {
        foreach (var name in path)
        {
            element = Get(element, name);
        }

        return element;
    }

    private static JsonElement Get(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    private static string? Str(JsonElement element, string name) =>
        Get(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
}
