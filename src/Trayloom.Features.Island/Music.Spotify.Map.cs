// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;
using System.Text.Json.Nodes;
using Trayloom.Core;

namespace Trayloom.Features.Island;

/// <summary>Spotify URIs and open.spotify.com links, parsed and rebuilt from validated ids only.</summary>
internal static class SpotifyUri
{
    private static readonly string[] Types = ["track", "album", "artist", "playlist"];

    /// <summary>True for a 22-character base-62 Spotify id.</summary>
    public static bool IsId(string? id)
    {
        if (id is null || id.Length != 22)
        {
            return false;
        }

        foreach (var ch in id)
        {
            if (!(ch is >= '0' and <= '9' or >= 'a' and <= 'z' or >= 'A' and <= 'Z'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Reads <c>spotify:{type}:{id}</c> or <c>https://open.spotify.com/[intl-xx/]{type}/{id}</c> (type: track, album, artist or playlist).
    /// </summary>
    public static bool TryParse(string? text, out string type, out string id)
    {
        type = "";
        id = "";
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        string[] parts;
        if (value.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase))
        {
            parts = value[8..].Split(':');
        }
        else if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && string.Equals(uri.Host, "open.spotify.com", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(uri.UserInfo))
        {
            parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0 && parts[0].StartsWith("intl-", StringComparison.OrdinalIgnoreCase))
            {
                parts = parts[1..];
            }
        }
        else
        {
            return false;
        }

        if (parts.Length != 2)
        {
            return false;
        }

        var kind = parts[0].ToLowerInvariant();
        if (Array.IndexOf(Types, kind) < 0 || !IsId(parts[1]))
        {
            return false;
        }

        type = kind;
        id = parts[1];
        return true;
    }

    /// <summary>The URI type word of an item kind ("track", "album", ...); empty for kinds Spotify does not have.</summary>
    public static string TypeOf(StreamItemKind kind) => kind switch
    {
        StreamItemKind.Track => "track",
        StreamItemKind.Album => "album",
        StreamItemKind.Artist => "artist",
        StreamItemKind.Playlist => "playlist",
        _ => "",
    };

    /// <summary><c>spotify:{type}:{id}</c>, or empty when the id is not a Spotify id.</summary>
    public static string Build(string type, string id) => IsId(id) ? "spotify:" + type + ":" + id : "";

    /// <summary><c>https://open.spotify.com/{type}/{id}</c>, or empty when the id is not a Spotify id.</summary>
    public static string LinkFor(string type, string id) => IsId(id) ? "https://open.spotify.com/" + type + "/" + id : "";
}

/// <summary>A device from <c>GET me/player/devices</c>.</summary>
internal sealed record SpotifyDevice(string Id, string Name, bool IsActive, bool IsRestricted);

/// <summary>What to do about a play command that found no active device.</summary>
internal enum DeviceAction
{
    /// <summary>Play on <see cref="DeviceChoice.DeviceId"/>.</summary>
    UseDevice,

    /// <summary>Start the Spotify app and wait for its device to appear.</summary>
    LaunchApp,

    /// <summary>Nothing to play on.</summary>
    NoDevice,
}

/// <summary>The outcome of <see cref="SpotifyDevicePlan.Choose"/>.</summary>
internal readonly record struct DeviceChoice(DeviceAction Action, string? DeviceId = null, string? DeviceName = null);

/// <summary>Picks the device to play on when Spotify has no active one. Pure.</summary>
internal static class SpotifyDevicePlan
{
    /// <summary>
    /// Skips restricted devices, then prefers the one named like this PC (any case), then the only device, then the active one, then the
    /// first. With no usable device: launch the app when it is installed, otherwise nothing.
    /// </summary>
    public static DeviceChoice Choose(IReadOnlyList<SpotifyDevice> devices, string machineName, bool appInstalled)
    {
        var usable = devices.Where(d => !d.IsRestricted && !string.IsNullOrEmpty(d.Id)).ToList();
        if (usable.Count == 0)
        {
            return new DeviceChoice(appInstalled ? DeviceAction.LaunchApp : DeviceAction.NoDevice);
        }

        var pick = usable.FirstOrDefault(d => string.Equals(d.Name, machineName, StringComparison.OrdinalIgnoreCase))
            ?? (usable.Count == 1 ? usable[0] : null)
            ?? usable.FirstOrDefault(d => d.IsActive)
            ?? usable[0];
        return new DeviceChoice(DeviceAction.UseDevice, pick.Id, pick.Name);
    }
}

/// <summary>The one gate every Spotify write goes through. Trayloom controls playback only: it never edits a library or playlist.</summary>
internal static class SpotifyWrites
{
    private static readonly HashSet<string> Endpoints = new(StringComparer.Ordinal)
    {
        "me/player/play",
        "me/player",
        "me/player/queue",
        "me/player/seek",
        "me/player/shuffle",
    };

    /// <summary>True when <paramref name="endpoint"/> (path with an optional query, with or without a leading slash or "v1/") is a playback command.</summary>
    public static bool Allowed(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return false;
        }

        var path = endpoint.Trim();
        var query = path.IndexOf('?');
        if (query >= 0)
        {
            path = path[..query];
        }

        path = path.TrimStart('/');
        if (path.StartsWith("v1/", StringComparison.Ordinal))
        {
            path = path[3..];
        }

        return Endpoints.Contains(path);
    }
}

/// <summary>The JSON body of <c>PUT me/player/play</c>. Pure.</summary>
internal static class SpotifyPlayBody
{
    private const int MaxUris = 50;

    /// <summary>
    /// What to start for a click. A container (album, playlist, artist) plays as a whole. A song inside an album or playlist
    /// <paramref name="context"/> plays within it, starting at the song. Otherwise, with <paramref name="siblings"/> (the Liked Songs
    /// list), the song and the up to 49 playable songs after it are sent as a list. Otherwise it plays within its own album or recent
    /// context, or alone. Null when the item cannot be played.
    /// </summary>
    public static string? For(StreamItem item, StreamItem? context, IReadOnlyList<StreamItem>? siblings)
    {
        if (!item.Playable)
        {
            return null;
        }

        var node = new JsonObject();
        if (item.Kind is StreamItemKind.Album or StreamItemKind.Artist or StreamItemKind.Playlist)
        {
            if (!IsUri(item.Uri))
            {
                return null;
            }

            node["context_uri"] = item.Uri;
            return node.ToJsonString();
        }

        if (item.Kind != StreamItemKind.Track || !IsUri(item.Uri))
        {
            return null;
        }

        if (context is { Kind: StreamItemKind.Album or StreamItemKind.Playlist } && IsUri(context.Uri))
        {
            return Within(context.Uri, item.Uri);
        }

        if (siblings is { Count: > 0 })
        {
            var uris = new JsonArray { item.Uri };
            var at = -1;
            for (var i = 0; i < siblings.Count; i++)
            {
                if (siblings[i].Key == item.Key)
                {
                    at = i;
                    break;
                }
            }

            if (at >= 0)
            {
                for (var i = at + 1; i < siblings.Count && uris.Count < MaxUris; i++)
                {
                    var next = siblings[i];
                    if (next.Kind == StreamItemKind.Track && next.Playable && IsUri(next.Uri))
                    {
                        uris.Add(next.Uri);
                    }
                }
            }

            node["uris"] = uris;
            return node.ToJsonString();
        }

        if (item.ContextUri is { Length: > 0 } own && IsUri(own))
        {
            return Within(own, item.Uri);
        }

        node["uris"] = new JsonArray { item.Uri };
        return node.ToJsonString();
    }

    private static string Within(string context, string track)
    {
        var node = new JsonObject
        {
            ["context_uri"] = context,
            ["offset"] = new JsonObject { ["uri"] = track },
        };
        return node.ToJsonString();
    }

    private static bool IsUri(string? uri) => SpotifyUri.TryParse(uri, out _, out _);
}

/// <summary>Turns Spotify Web API JSON into <see cref="StreamItem"/>s. Pure; unknown shapes yield nothing rather than an exception.</summary>
internal static class SpotifyMap
{
    /// <summary>The cap Spotify puts on <c>offset</c> + <c>limit</c> of a search.</summary>
    public const int SearchDepth = 1000;

    /// <summary>The sections of a <c>/search</c> answer (songs, albums, artists, playlists, in that order, each only when present).</summary>
    public static IReadOnlyList<StreamSection> Search(JsonElement root, string? userId)
    {
        var sections = new List<StreamSection>();
        if (Child(root, "tracks") is { } tracks)
        {
            sections.Add(Section(StreamScope.Songs, LibStrings.Songs, tracks, e => Track(e, null)));
        }

        if (Child(root, "albums") is { } albums)
        {
            sections.Add(Section(StreamScope.Albums, LibStrings.Albums, albums, Album));
        }

        if (Child(root, "artists") is { } artists)
        {
            sections.Add(Section(StreamScope.Artists, LibStrings.Artists, artists, Artist));
        }

        if (Child(root, "playlists") is { } playlists)
        {
            sections.Add(Section(StreamScope.Playlists, LibStrings.Playlists, playlists, e => Playlist(e, userId)));
        }

        return sections;
    }

    /// <summary>The user's playlists (<c>me/playlists</c>).</summary>
    public static StreamSection Playlists(JsonElement root, string? userId) =>
        Section(StreamScope.Playlists, LibStrings.Playlists, root, e => Playlist(e, userId));

    /// <summary>Liked songs (<c>me/tracks</c>); each entry wraps the song in <c>track</c> (or <c>item</c>).</summary>
    public static StreamSection Liked(JsonElement root) =>
        Section(StreamScope.Songs, StreamStrings.LikedSongs, root, e => Track(Wrapped(e), null));

    /// <summary>Recently played songs, with immediate repeats of the same song removed.</summary>
    public static StreamSection Recent(JsonElement root)
    {
        var items = new List<StreamItem>();
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in array.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var track = Track(Wrapped(entry), null);
                if (track is null)
                {
                    continue;
                }

                // Where it was played from: a playlist or album the song can resume in. Artist and collection contexts cannot take an offset.
                var context = Child(entry, "context") is { } c && Str(c, "uri") is { } uri
                    && SpotifyUri.TryParse(uri, out var type, out var id) && type is "album" or "playlist"
                    ? SpotifyUri.Build(type, id)
                    : null;
                if (context is not null)
                {
                    track = track with { ContextUri = context };
                }

                if (items.Count > 0 && items[^1].Id == track.Id)
                {
                    continue;
                }

                items.Add(track);
            }
        }

        return new StreamSection(StreamScope.Songs, StreamStrings.RecentlyPlayed, items, items.Count);
    }

    /// <summary>The songs of an album (<c>albums/{id}/tracks</c>); they carry no album of their own, so they inherit <paramref name="album"/>.</summary>
    public static StreamSection AlbumTracks(JsonElement root, StreamItem album) =>
        Section(StreamScope.Songs, LibStrings.Songs, root, e => Track(e, album));

    /// <summary>The songs of a playlist (<c>playlists/{id}/items</c>); entries wrap the song in <c>item</c> (formerly <c>track</c>).</summary>
    public static StreamSection PlaylistItems(JsonElement root) =>
        Section(StreamScope.Songs, LibStrings.Songs, root, e => Track(Wrapped(e), null));

    /// <summary>An artist's albums (<c>artists/{id}/albums</c>).</summary>
    public static StreamSection ArtistAlbums(JsonElement root) =>
        Section(StreamScope.Albums, LibStrings.Albums, root, Album);

    /// <summary>The "Up next" list of <c>me/player/queue</c>.</summary>
    public static StreamSection Queue(JsonElement root)
    {
        var items = new List<StreamItem>();
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("queue", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in array.EnumerateArray())
            {
                if (Track(entry, null) is { } track)
                {
                    items.Add(track);
                }
            }
        }

        return new StreamSection(StreamScope.Songs, Loc.T("Up next"), items, items.Count);
    }

    /// <summary>The devices of <c>me/player/devices</c>.</summary>
    public static IReadOnlyList<SpotifyDevice> Devices(JsonElement root)
    {
        var list = new List<SpotifyDevice>();
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("devices", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var d in array.EnumerateArray())
            {
                if (d.ValueKind == JsonValueKind.Object && Str(d, "id") is { Length: > 0 } id)
                {
                    list.Add(new SpotifyDevice(id, Str(d, "name") ?? "", Bool(d, "is_active"), Bool(d, "is_restricted")));
                }
            }
        }

        return list;
    }

    /// <summary>One song from a track object; null for nulls, episodes and objects without a usable id.</summary>
    /// <param name="element">The track object.</param>
    /// <param name="album">The album it is listed under, for album listings whose songs carry none.</param>
    public static StreamItem? Track(JsonElement element, StreamItem? album)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var type = Str(element, "type");
        if (type is not null && type != "track")
        {
            return null;
        }

        var title = Str(element, "name") ?? "";
        var artists = ArtistNames(element);
        var seconds = Num(element, "duration_ms") / 1000.0;
        var uri = Str(element, "uri") ?? "";
        if (uri.StartsWith("spotify:local:", StringComparison.Ordinal))
        {
            // A file from the user's disk: listed so the queue reads right, but Spotify's API cannot start it.
            return new StreamItem(StreamItemKind.Track, "local:" + StableHash(uri), title, StreamStrings.LocalFile)
            {
                Duration = seconds,
                Playable = false,
            };
        }

        var id = Str(element, "id");
        if (!SpotifyUri.IsId(id))
        {
            return null;
        }

        string? albumId = null;
        string? albumName = null;
        string? contextUri = null;
        string? image = null;
        if (Child(element, "album") is { } a)
        {
            if (Str(a, "id") is { } ai && SpotifyUri.IsId(ai))
            {
                albumId = ai;
                contextUri = SpotifyUri.Build("album", ai);
            }

            albumName = Str(a, "name");
            image = BestImage(a);
        }
        else if (album is not null)
        {
            albumId = album.Id;
            albumName = album.Title;
            contextUri = album.Uri;
            image = album.ImageUrl;
        }

        string? artistId = null;
        if (element.TryGetProperty("artists", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var artist in list.EnumerateArray())
            {
                if (artist.ValueKind == JsonValueKind.Object && Str(artist, "id") is { } id0 && SpotifyUri.IsId(id0))
                {
                    artistId = id0;
                    break;
                }
            }
        }

        // is_playable is only sent when a market is given; absent means playable.
        var playable = !(element.TryGetProperty("is_playable", out var p) && p.ValueKind == JsonValueKind.False);
        return new StreamItem(StreamItemKind.Track, id!, title, artists)
        {
            Duration = seconds,
            ImageUrl = image,
            Uri = SpotifyUri.Build("track", id!),
            ContextUri = contextUri,
            AlbumId = albumId,
            AlbumName = albumName,
            ArtistId = artistId,
            Playable = playable,
        };
    }

    /// <summary>One album from an album object; null without a usable id.</summary>
    public static StreamItem? Album(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object || Str(element, "id") is not { } id || !SpotifyUri.IsId(id))
        {
            return null;
        }

        return new StreamItem(StreamItemKind.Album, id, Str(element, "name") ?? "", ArtistNames(element))
        {
            ImageUrl = BestImage(element),
            Uri = SpotifyUri.Build("album", id),
            Count = (int)Num(element, "total_tracks"),
            ArtistId = FirstArtistId(element),
        };
    }

    /// <summary>One artist from an artist object; null without a usable id.</summary>
    public static StreamItem? Artist(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object || Str(element, "id") is not { } id || !SpotifyUri.IsId(id))
        {
            return null;
        }

        return new StreamItem(StreamItemKind.Artist, id, Str(element, "name") ?? "", Loc.T("Artist"))
        {
            ImageUrl = BestImage(element),
            Uri = SpotifyUri.Build("artist", id),
        };
    }

    /// <summary>One playlist; <see cref="StreamItem.Owned"/> when the user owns it or it is collaborative (the only ones whose songs Spotify lists).</summary>
    public static StreamItem? Playlist(JsonElement element, string? userId)
    {
        if (element.ValueKind != JsonValueKind.Object || Str(element, "id") is not { } id || !SpotifyUri.IsId(id))
        {
            return null;
        }

        var owner = Child(element, "owner");
        var ownerId = owner is { } o ? Str(o, "id") : null;
        var owned = Bool(element, "collaborative") || (userId is { Length: > 0 } && ownerId == userId);
        var count = Child(element, "items") is { } items ? Num(items, "total") : Child(element, "tracks") is { } tracks ? Num(tracks, "total") : 0;
        return new StreamItem(StreamItemKind.Playlist, id, Str(element, "name") ?? "", owner is { } ow ? Str(ow, "display_name") ?? "" : "")
        {
            ImageUrl = BestImage(element),
            Uri = SpotifyUri.Build("playlist", id),
            Owned = owned,
            Count = (int)count,
        };
    }

    /// <summary>The smallest image that is at least 64 px wide (else the widest there is), if its host is one artwork may come from.</summary>
    public static string? BestImage(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? best = null;
        double bestWidth = 0;
        foreach (var image in images.EnumerateArray())
        {
            if (image.ValueKind != JsonValueKind.Object || Str(image, "url") is not { } url || !StreamArt.Allowed(url))
            {
                continue;
            }

            // A missing width counts as large: such an image is used only when nothing is known to be right-sized.
            var width = image.TryGetProperty("width", out var w) && w.ValueKind == JsonValueKind.Number ? w.GetDouble() : 10_000;
            var better = best is null
                || (bestWidth < 64 && width > bestWidth)
                || (width >= 64 && width < bestWidth);
            if (better)
            {
                best = url;
                bestWidth = width;
            }
        }

        return best;
    }

    /// <summary>True when a title and artist from the media session (Windows) are the song the API describes; case, accents and punctuation are ignored.</summary>
    public static bool SameSong(string? title, string? artist, string? apiTitle, string? apiArtists)
    {
        var t = AmSearchIndex.Fold(title ?? "");
        if (t.Length == 0 || t != AmSearchIndex.Fold(apiTitle ?? ""))
        {
            return false;
        }

        var a = AmSearchIndex.Fold(artist ?? "");
        var api = AmSearchIndex.Fold(apiArtists ?? "");
        if (a.Length == 0 || api.Length == 0 || a == api)
        {
            return true;
        }

        // The media session may name only the first artist, or join them differently.
        foreach (var part in (apiArtists ?? "").Split([',', ';', '&'], StringSplitOptions.RemoveEmptyEntries))
        {
            var folded = AmSearchIndex.Fold(part);
            if (folded.Length > 0 && (a == folded || a.Contains(folded, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return api.Contains(a, StringComparison.Ordinal);
    }

    private static StreamSection Section(StreamScope scope, string title, JsonElement page, Func<JsonElement, StreamItem?> map)
    {
        var items = new List<StreamItem>();
        if (page.ValueKind == JsonValueKind.Object && page.TryGetProperty("items", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in array.EnumerateArray())
            {
                if (map(entry) is { } item)
                {
                    items.Add(item);
                }
            }
        }

        var total = page.ValueKind == JsonValueKind.Object ? (int)Num(page, "total") : 0;
        return new StreamSection(scope, title, items, Math.Max(total, items.Count));
    }

    /// <summary>The song inside a list entry: <c>item</c> (current) or <c>track</c> (formerly); the entry itself when it has neither.</summary>
    private static JsonElement Wrapped(JsonElement entry)
    {
        if (entry.ValueKind == JsonValueKind.Object)
        {
            if (entry.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object)
            {
                return item;
            }

            if (entry.TryGetProperty("track", out var track) && track.ValueKind == JsonValueKind.Object)
            {
                return track;
            }
        }

        return default;
    }

    /// <summary>A hash that is the same in every run (unlike <see cref="string.GetHashCode()"/>), so a local file keeps its row identity.</summary>
    private static string StableHash(string text)
    {
        uint hash = 2166136261;
        foreach (var ch in text)
        {
            hash = (hash ^ ch) * 16777619;
        }

        return hash.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string ArtistNames(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("artists", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return "";
        }

        var names = new List<string>();
        foreach (var artist in list.EnumerateArray())
        {
            if (artist.ValueKind == JsonValueKind.Object && Str(artist, "name") is { Length: > 0 } name)
            {
                names.Add(name);
            }
        }

        return string.Join(", ", names);
    }

    private static string? FirstArtistId(JsonElement element)
    {
        if (element.TryGetProperty("artists", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var artist in list.EnumerateArray())
            {
                if (artist.ValueKind == JsonValueKind.Object && Str(artist, "id") is { } id && SpotifyUri.IsId(id))
                {
                    return id;
                }
            }
        }

        return null;
    }

    private static JsonElement? Child(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var child) && child.ValueKind == JsonValueKind.Object ? child : null;

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double Num(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    private static bool Bool(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
