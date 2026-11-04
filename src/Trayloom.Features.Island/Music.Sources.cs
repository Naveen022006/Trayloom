// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace Trayloom.Features.Island;

/// <summary>A place the island's music browser can show.</summary>
internal enum MusicSource
{
    Apple,
    Spotify,
    YouTube,
}

/// <summary>
/// Which sources the music browser offers and which one it shows. The decisions are pure (<see cref="Available(bool, bool, bool)"/> and
/// <see cref="Resolve"/>, tested by the harness); the helpers taking an <see cref="IslandCore"/> read the live state.
/// </summary>
internal static class MusicSources
{
    /// <summary>The sources in switcher order: Apple Music when the island can drive it, Spotify and YouTube Music when the user turned them on.</summary>
    public static IReadOnlyList<MusicSource> Available(bool apple, bool spotifyOn, bool youTubeOn)
    {
        var list = new List<MusicSource>(3);
        if (apple)
        {
            list.Add(MusicSource.Apple);
        }

        if (spotifyOn)
        {
            list.Add(MusicSource.Spotify);
        }

        if (youTubeOn)
        {
            list.Add(MusicSource.YouTube);
        }

        return list;
    }

    /// <summary>
    /// The source to show: the saved choice ("apple", "spotify", "youtube") when it is available, else the one of the player that is
    /// playing now, else the first available. Null when there is no source at all.
    /// </summary>
    public static MusicSource? Resolve(string? saved, IReadOnlyList<MusicSource> available, string? currentAumid)
    {
        if (available.Count == 0)
        {
            return null;
        }

        if (Parse(saved) is { } chosen && available.Contains(chosen))
        {
            return chosen;
        }

        if (FromPlayer(currentAumid) is { } playing && available.Contains(playing))
        {
            return playing;
        }

        return available[0];
    }

    /// <summary>The saved name of a source; null for "" (automatic) and anything unknown.</summary>
    public static MusicSource? Parse(string? saved) => (saved ?? "").Trim().ToLowerInvariant() switch
    {
        "apple" => MusicSource.Apple,
        "spotify" => MusicSource.Spotify,
        "youtube" => MusicSource.YouTube,
        _ => null,
    };

    /// <summary>The name stored in the settings.</summary>
    public static string Name(MusicSource source) => source switch
    {
        MusicSource.Apple => "apple",
        MusicSource.Spotify => "spotify",
        _ => "youtube",
    };

    /// <summary>The brand name (not localized).</summary>
    public static string Brand(MusicSource source) => source switch
    {
        MusicSource.Apple => "Apple Music",
        MusicSource.Spotify => "Spotify",
        _ => "YouTube Music",
    };

    /// <summary>The source a player belongs to (by its app id); null for any other player.</summary>
    public static MusicSource? FromPlayer(string? aumid)
    {
        if (string.IsNullOrEmpty(aumid))
        {
            return null;
        }

        if (aumid.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase))
        {
            return MusicSource.Apple;
        }

        if (aumid.Contains("spotify", StringComparison.OrdinalIgnoreCase))
        {
            return MusicSource.Spotify;
        }

        return aumid.Contains("youtubemusic", StringComparison.OrdinalIgnoreCase) ? MusicSource.YouTube : null;
    }

    // ---------------------------------------------------------------- live state

    /// <summary>The sources on offer right now.</summary>
    public static IReadOnlyList<MusicSource> Available(IslandCore core) =>
        Available(core.AppleMusic.Available, core.Music.Spotify.ShowInIsland, core.Music.YouTube.ShowInIsland);

    /// <summary>The browser (Library toggle, magnifier, search keys) is offered: at least one source is available.</summary>
    public static bool Any(IslandCore core) => Available(core).Count > 0;

    /// <summary>The source shown now; null when there is none.</summary>
    public static MusicSource? Current(IslandCore core) =>
        Resolve(core.App.Get<string>(K.MusicBrowserSource), Available(core), core.Playback.Current.Aumid);

    /// <summary>Remembers the user's choice.</summary>
    public static void Choose(IslandCore core, MusicSource source) => core.App.Set(K.MusicBrowserSource, Name(source));

    /// <summary>The tooltip of the Library toggle: the Apple Music one while it is the only source, else plain "Library".</summary>
    public static string ToggleTitle(IslandCore core) =>
        Available(core) is [MusicSource.Apple] ? LibStrings.Library : StreamStrings.Library;

    /// <summary>The tooltip of the magnifier for the source shown.</summary>
    public static string SearchTip(IslandCore core) => Current(core) switch
    {
        MusicSource.Spotify => StreamStrings.SearchSpotify,
        MusicSource.YouTube => StreamStrings.SearchYouTubeMusic,
        _ => LibStrings.SearchTip,
    };
}
