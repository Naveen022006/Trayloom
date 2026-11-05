// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;

namespace Trayloom.Features.Island;

/// <summary>The song Apple Music played last (kept in <see cref="K.AppleMusicLastPlayed"/> as JSON), so an idle Play can continue with it.</summary>
internal sealed record LastPlayed(string Title, string Artist, string Album, double Position, double Duration, ulong TrackPid, ulong PlaylistPid, DateTime At)
{
    /// <summary>Reads the stored value; null for none or anything unreadable.</summary>
    public static LastPlayed? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<LastPlayed>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this);
}

internal enum IdleStepKind
{
    /// <summary>Play one song (from a playlist page when <see cref="IdleStep.Playlist"/> is set).</summary>
    PlayTrack,

    /// <summary>Shuffle a playlist.</summary>
    ShufflePlaylist,
}

/// <summary>One thing the island may start when Apple Music has nothing loaded; <see cref="Why"/> is what it is for the log.</summary>
internal sealed record IdleStep(IdleStepKind Kind, string Why, AmTrack? Track = null, AmPlaylist? Playlist = null, string Page = "songs", double? ResumeAt = null);

/// <summary>What to try, best first: the last played song, the Favourite Songs playlist shuffled, a library song. Empty = nothing to play.</summary>
internal sealed record IdlePlan(IReadOnlyList<IdleStep> Steps)
{
    public IdleStep? First => Steps.Count > 0 ? Steps[0] : null;
}

/// <summary>Pure decisions of "Play with nothing loaded" (no UI, no Apple Music): what to play and which library track a remembered one is.</summary>
internal static class AmIdle
{
    /// <summary>A last played song older than this is not continued.</summary>
    public static readonly TimeSpan LastPlayedMaxAge = TimeSpan.FromDays(30);

    /// <summary>The position of a last played song is only continued when it was left this recently.</summary>
    public static readonly TimeSpan ResumeMaxAge = TimeSpan.FromHours(12);

    /// <summary>
    /// The plan for an idle Play, Next or Previous. <paramref name="now"/> and <see cref="LastPlayed.At"/> are UTC. Every step that applies is in
    /// the plan (the caller falls through to the next one when Apple Music cannot find the song or does not start it). Hand-made data: every
    /// track is a library song, so no song has a playlist as its default context.
    /// </summary>
    public static IdlePlan Choose(IReadOnlyList<AmTrack> songs, IReadOnlyList<AmTrack> recent, IReadOnlyList<AmPlaylist> playlists, LastPlayed? last, DateTime now, Random rng) =>
        Choose(songs, songs, recent, playlists, last, now, rng, _ => null);

    /// <summary>The plan for an idle Play over a loaded library: the last played song may be a playlist-only track (found in <see cref="AmDb.AllTracks"/>).</summary>
    public static IdlePlan Choose(AmDb db, LastPlayed? last, DateTime now, Random rng) =>
        Choose(db.AllTracks, db.Songs, db.Recent, db.Playlists, last, now, rng, db.DefaultContext);

    /// <param name="all">Every track the last played song may be (library songs and playlist-only tracks).</param>
    /// <param name="songs">The library songs (the pool of a random pick).</param>
    /// <param name="recent">The recently added library songs (preferred by the random pick).</param>
    /// <param name="playlists">The listed playlists.</param>
    /// <param name="last">The last played song, if any.</param>
    /// <param name="now">The current time (UTC).</param>
    /// <param name="rng">The random source of the random pick.</param>
    /// <param name="defaultContext">Where to play a track from when the stored playlist does not hold it (null = the Songs page).</param>
    public static IdlePlan Choose(IReadOnlyList<AmTrack> all, IReadOnlyList<AmTrack> songs, IReadOnlyList<AmTrack> recent, IReadOnlyList<AmPlaylist> playlists, LastPlayed? last, DateTime now, Random rng, Func<AmTrack, AmPlaylist?> defaultContext)
    {
        var steps = new List<IdleStep>();
        if (last is not null && !string.IsNullOrWhiteSpace(last.Title))
        {
            var age = now - last.At;
            if (age < TimeSpan.Zero)
            {
                age = TimeSpan.Zero;
            }

            var hint = last.TrackPid == 0 ? null : all.FirstOrDefault(t => t.Pid == last.TrackPid);
            if (age < LastPlayedMaxAge && FindTrack(all, new ClosedSnapshot(last.Title, last.Artist, last.Album, last.Position, last.Duration, hint, null, last.At)) is { } track)
            {
                var context = ContextFor(track, last.PlaylistPid, playlists, defaultContext);
                var duration = last.Duration > 0 ? last.Duration : track.DurationMs / 1000.0;
                double? resumeAt = age < ResumeMaxAge && duration > 0 && last.Position > 5 && last.Position < duration - 10 ? last.Position : null;
                steps.Add(new IdleStep(IdleStepKind.PlayTrack, "last played", track, context, "songs", resumeAt));
            }
        }

        if (playlists.FirstOrDefault(p => p.Tracks.Count > 0 && IsFavourites(p.Name)) is { } favourites)
        {
            steps.Add(new IdleStep(IdleStepKind.ShufflePlaylist, "Favourite Songs", Playlist: favourites));
        }

        // always the Songs page: the Recently added page is an album grid without a song list
        var pool = recent.Count > 0 ? recent : songs;
        if (pool.Count > 0)
        {
            steps.Add(new IdleStep(IdleStepKind.PlayTrack, recent.Count > 0 ? "a recently added song" : "a library song", pool[rng.Next(pool.Count)], Page: "songs"));
        }

        return new IdlePlan(steps);
    }

    /// <summary>
    /// Where to play a track from: the playlist it was last played from when that still holds it, else the default context of the library
    /// (a playlist for a playlist-only track, null = the Songs page).
    /// </summary>
    public static AmPlaylist? ContextFor(AmTrack track, ulong storedPlaylistPid, IReadOnlyList<AmPlaylist> playlists, Func<AmTrack, AmPlaylist?> defaultContext)
    {
        if (storedPlaylistPid != 0 && playlists.FirstOrDefault(p => p.Pid == storedPlaylistPid && p.Tracks.Any(t => t.Pid == track.Pid)) is { } stored)
        {
            return stored;
        }

        return defaultContext(track);
    }

    /// <summary>The Favourite Songs playlist under either spelling of its name.</summary>
    public static bool IsFavourites(string name)
    {
        var key = WatchService.Normalise(name);
        return key is "favourite songs" or "favorite songs";
    }

    /// <summary>
    /// Windows' media session for Apple Music puts the album behind the artist ("Artist — Album"); this is the artist alone and the album
    /// (empty when there is none).
    /// </summary>
    public static (string Artist, string Album) SplitAppleArtist(string? artist)
    {
        if (string.IsNullOrEmpty(artist))
        {
            return ("", "");
        }

        const string separator = " — ";
        var i = artist.IndexOf(separator, StringComparison.Ordinal);
        return i < 0 ? (artist.Trim(), "") : (artist[..i].Trim(), artist[(i + separator.Length)..].Trim());
    }

    /// <summary>The artist and album of a media session sample: the album of the session when it has one, else the one folded into the artist.</summary>
    public static (string Artist, string Album) SplitNowPlaying(string? artist, string? album)
    {
        var (a, b) = SplitAppleArtist(artist);
        return (a, string.IsNullOrWhiteSpace(album) ? b : album.Trim());
    }

    /// <summary>The comparable form of a title or name: folded (case, accents, punctuation), or just lower case when nothing else is left.</summary>
    public static string Key(string? text)
    {
        text ??= "";
        var folded = AmSearchIndex.Fold(text);
        return folded.Length > 0 ? folded : text.Trim().ToLowerInvariant();
    }

    /// <summary>The two titles are the same song title.</summary>
    public static bool SameTitle(string? a, string? b) => Key(a) == Key(b);

    /// <summary>The same song as far as a media session sample can tell: same title and same artist (the album folded into the artist is ignored).</summary>
    public static bool SameSong(string? titleA, string? artistA, string? titleB, string? artistB) =>
        SameTitle(titleA, titleB) && Key(SplitAppleArtist(artistA).Artist) == Key(SplitAppleArtist(artistB).Artist);

    /// <summary>The folded "artist album" text of a media session sample (the album split off the artist when the session folded it in).</summary>
    public static string DetailKey(string? artist, string? album)
    {
        var (a, b) = SplitNowPlaying(artist, album);
        return AmSearchIndex.Fold(a + " " + b);
    }

    /// <summary>The track's artist occurs in the folded media session text.</summary>
    private static bool ArtistMatches(AmTrack t, string detail)
    {
        var key = AmSearchIndex.Fold(t.Artist);
        return key.Length > 0 && detail.Contains(key, StringComparison.Ordinal);
    }

    /// <summary>The track has no album, or its album occurs in the folded media session text.</summary>
    private static bool AlbumMatches(AmTrack t, string detail)
    {
        var key = AmSearchIndex.Fold(t.Album);
        return key.Length == 0 || detail.Contains(key, StringComparison.Ordinal);
    }

    /// <summary>The track is what the media session plays: same title, and the album or the artist agrees (the island's "now playing" highlight).</summary>
    /// <param name="t">A track of the library.</param>
    /// <param name="title">The session's title.</param>
    /// <param name="detailKey">The session's <see cref="DetailKey"/>.</param>
    public static bool IsPlaying(AmTrack t, string title, string detailKey) =>
        SameTitle(t.Title, title) && (AlbumMatches(t, detailKey) || ArtistMatches(t, detailKey));

    /// <summary>
    /// The track of the library the media session plays now (title, artist and album as the session reports them), or null. Several tracks may
    /// pass <see cref="IsPlaying"/>; the one whose album and artist both agree wins, then one in the library proper, then the first.
    /// </summary>
    public static AmTrack? FindPlaying(IReadOnlyList<AmTrack> tracks, string title, string artist, string album)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var detail = DetailKey(artist, album);
        AmTrack? best = null;
        var bestScore = -1;
        foreach (var t in tracks)
        {
            if (!IsPlaying(t, title, detail))
            {
                continue;
            }

            var score = (t.Album.Length > 0 && AlbumMatches(t, detail) ? 2 : 0) + (ArtistMatches(t, detail) ? 2 : 0) + (t.InLibrary ? 1 : 0);
            if (score > bestScore)
            {
                best = t;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>
    /// The track (library song or playlist-only) for what was playing: title, then artist and album, like <c>AppleMusicService.IsCurrent</c>.
    /// Several tracks may share a title (versions, other artists): one needs the artist, its real album or the remembered Pid to agree unless it
    /// is the only one with the title. Null when none matches.
    /// </summary>
    public static AmTrack? FindTrack(IReadOnlyList<AmTrack> tracks, ClosedSnapshot s)
    {
        if (s.Title.Trim().Length == 0)
        {
            return null;
        }

        var title = Key(s.Title);
        var detail = DetailKey(s.Artist, s.Album);
        AmTrack? best = null;
        var bestScore = -1;
        var bestEvidence = false;
        var candidates = 0;
        foreach (var t in tracks)
        {
            if (Key(t.Title) != title)
            {
                continue;
            }

            candidates++;
            var albumOk = AlbumMatches(t, detail);
            var artistOk = ArtistMatches(t, detail);
            var pid = s.Track is { } last && last.Pid == t.Pid;
            var score = (1 + (albumOk ? 1 : 0) + (artistOk ? 1 : 0) + (pid ? 2 : 0)) * 2 + (t.InLibrary ? 1 : 0);
            if (score > bestScore)
            {
                best = t;
                bestScore = score;
                bestEvidence = pid || artistOk || (albumOk && t.Album.Length > 0);
            }
        }

        return candidates == 1 || bestEvidence ? best : null;
    }

    /// <summary>The one track with this title and artist, or null for none or several (used to learn the Pid of what plays).</summary>
    public static AmTrack? UniqueTrack(IReadOnlyList<AmTrack> tracks, string title, string? artist, string? album = "")
    {
        var key = Key(title);
        var detail = AmSearchIndex.Fold(SplitNowPlaying(artist, album).Artist);
        AmTrack? found = null;
        foreach (var t in tracks)
        {
            if (Key(t.Title) != key || !ArtistMatches(t, detail))
            {
                continue;
            }

            if (found is not null)
            {
                return null;
            }

            found = t;
        }

        return found;
    }
}

/// <summary>What to play again after Apple Music was closed in the middle of a song.</summary>
internal sealed record AmResumePick(AmTrack Track, AmPlaylist? Context);

/// <summary>The pure choice of "resume after quit": which track of the library, and which playlist to play it from.</summary>
internal static class AmResume
{
    /// <summary>
    /// The track for the snapshot among every track of the library (playlist-only ones too) and the context it was playing from: the playlist
    /// of the snapshot when it still holds the track, else the library's default context for it. Null when the library has no such track.
    /// </summary>
    public static AmResumePick? Pick(AmDb db, ClosedSnapshot snapshot)
    {
        if (AmIdle.FindTrack(db.AllTracks, snapshot) is not { } track)
        {
            return null;
        }

        return new AmResumePick(track, AmIdle.ContextFor(track, snapshot.Playlist?.Pid ?? 0, db.Playlists, db.DefaultContext));
    }
}

/// <summary>When the island reloads the library file that Apple Music rewrites while the music UI is on screen.</summary>
internal static class AmReload
{
    /// <summary>
    /// One poll. The file is loaded when its stamp differs from the loaded one and from the one that failed, and was already the same at the
    /// previous poll (Apple Music writes the file in bursts: the debounce waits for it to rest). <paramref name="seen"/> is the stamp of the last poll.
    /// </summary>
    public static bool ShouldLoad(DateTime? loaded, DateTime? failed, DateTime current, ref DateTime? seen)
    {
        var stable = seen == current;
        seen = current;
        return stable && current != loaded && current != failed;
    }
}
