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

/// <summary>Pure decisions of "Play with nothing loaded" (no UI, no Apple Music): what to play and which library song a remembered one is.</summary>
internal static class AmIdle
{
    /// <summary>A last played song older than this is not continued.</summary>
    public static readonly TimeSpan LastPlayedMaxAge = TimeSpan.FromDays(30);

    /// <summary>The position of a last played song is only continued when it was left this recently.</summary>
    public static readonly TimeSpan ResumeMaxAge = TimeSpan.FromHours(12);

    /// <summary>
    /// The plan for an idle Play, Next or Previous. <paramref name="now"/> and <see cref="LastPlayed.At"/> are UTC. Every step that applies is in
    /// the plan (the caller falls through to the next one when Apple Music cannot find the song or does not start it).
    /// </summary>
    public static IdlePlan Choose(IReadOnlyList<AmTrack> songs, IReadOnlyList<AmTrack> recent, IReadOnlyList<AmPlaylist> playlists, LastPlayed? last, DateTime now, Random rng)
    {
        var steps = new List<IdleStep>();
        if (last is not null && !string.IsNullOrWhiteSpace(last.Title))
        {
            var age = now - last.At;
            if (age < TimeSpan.Zero)
            {
                age = TimeSpan.Zero;
            }

            if (age < LastPlayedMaxAge && FindTrack(songs, new ClosedSnapshot(last.Title, last.Artist, last.Album, last.Position, last.Duration, songs.FirstOrDefault(t => t.Pid == last.TrackPid && last.TrackPid != 0), null, last.At)) is { } track)
            {
                // the playlist it was played from only counts when it still holds the song
                var context = last.PlaylistPid == 0 ? null : playlists.FirstOrDefault(p => p.Pid == last.PlaylistPid && p.Tracks.Any(t => t.Pid == track.Pid));
                var duration = last.Duration > 0 ? last.Duration : track.DurationMs / 1000.0;
                double? resumeAt = age < ResumeMaxAge && duration > 0 && last.Position > 5 && last.Position < duration - 10 ? last.Position : null;
                steps.Add(new IdleStep(IdleStepKind.PlayTrack, "last played", track, context, "songs", resumeAt));
            }
        }

        if (playlists.FirstOrDefault(p => p.Tracks.Count > 0 && IsFavourites(p.Name)) is { } favourites)
        {
            steps.Add(new IdleStep(IdleStepKind.ShufflePlaylist, "Favourite Songs", Playlist: favourites));
        }

        var pool = recent.Count > 0 ? recent : songs;
        if (pool.Count > 0)
        {
            steps.Add(new IdleStep(IdleStepKind.PlayTrack, recent.Count > 0 ? "a recently added song" : "a library song", pool[rng.Next(pool.Count)], Page: recent.Count > 0 ? "recent" : "songs"));
        }

        return new IdlePlan(steps);
    }

    /// <summary>The Favourite Songs playlist under either spelling of its name.</summary>
    public static bool IsFavourites(string name)
    {
        var key = WatchService.Normalise(name);
        return key is "favourite songs" or "favorite songs";
    }

    /// <summary>The library song for what was playing (title, then artist and album, like <c>AppleMusicService.IsCurrent</c>); null when none matches.</summary>
    public static AmTrack? FindTrack(IReadOnlyList<AmTrack> songs, ClosedSnapshot s)
    {
        var title = s.Title.Trim();
        if (title.Length == 0)
        {
            return null;
        }

        var detail = s.Artist + " " + s.Album;
        int Score(AmTrack t)
        {
            if (!string.Equals(t.Title.Trim(), title, StringComparison.CurrentCultureIgnoreCase))
            {
                return 0;
            }

            var album = t.Album.Length == 0 || detail.Contains(t.Album, StringComparison.OrdinalIgnoreCase);
            var artist = t.Artist.Length > 0 && detail.Contains(t.Artist, StringComparison.OrdinalIgnoreCase);
            return 1 + (album ? 1 : 0) + (artist ? 1 : 0) + (s.Track is { } last && last.Pid == t.Pid ? 2 : 0);
        }

        AmTrack? best = null;
        var bestScore = 0;
        foreach (var t in songs)
        {
            var score = Score(t);
            if (score > bestScore)
            {
                best = t;
                bestScore = score;
            }
        }

        return best;
    }
}
