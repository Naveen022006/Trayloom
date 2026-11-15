// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace Trayloom.Features.Island;

/// <summary>What the island's idle cover and playback buttons act on while nothing plays.</summary>
internal enum IdleTarget
{
    /// <summary>Apple Music, driven in the background.</summary>
    Apple,

    /// <summary>The Spotify app.</summary>
    Spotify,

    /// <summary>Trayloom's own YouTube Music player (search first).</summary>
    YouTube,

    /// <summary>Nothing is known to be wanted: the person is asked which app to use.</summary>
    Choose,
}

/// <summary>Which of the three supported music apps an idle press goes to (pure: tested by the harness).</summary>
internal static class IdleTargets
{
    /// <summary>
    /// The saved choice ("apple", "spotify", "youtube") when that app can be used; anything else (including the app ids older versions
    /// saved, such as Media Player) means Automatic: the app that played last, else the only one there is, else <see cref="IdleTarget.Choose"/>.
    /// </summary>
    /// <param name="saved">The saved setting.</param>
    /// <param name="lastMusic">The app id of the music session that was shown last, if any.</param>
    /// <param name="apple">The island can drive Apple Music.</param>
    /// <param name="spotifyAumid">What opens the installed Spotify app; null when it is not installed.</param>
    /// <param name="ytmAumid">What opens an installed YouTube Music web app; null when there is none.</param>
    public static IdleTarget Resolve(string saved, string? lastMusic, bool apple, string? spotifyAumid, string? ytmAumid)
    {
        switch (MusicSources.Parse(saved))
        {
            case MusicSource.Apple when apple:
                return IdleTarget.Apple;
            case MusicSource.Spotify when spotifyAumid is not null:
                return IdleTarget.Spotify;
            case MusicSource.YouTube:
                return IdleTarget.YouTube;
        }

        switch (MusicSources.FromPlayer(lastMusic))
        {
            case MusicSource.Apple when apple:
                return IdleTarget.Apple;
            case MusicSource.Spotify when spotifyAumid is not null:
                return IdleTarget.Spotify;
            case MusicSource.YouTube:
                return IdleTarget.YouTube;
        }

        var found = new List<IdleTarget>(3);
        if (apple)
        {
            found.Add(IdleTarget.Apple);
        }

        if (spotifyAumid is not null)
        {
            found.Add(IdleTarget.Spotify);
        }

        if (ytmAumid is not null)
        {
            found.Add(IdleTarget.YouTube);
        }

        return found.Count == 1 ? found[0] : IdleTarget.Choose;
    }

    /// <summary>The name saved for a target ("" for <see cref="IdleTarget.Choose"/>).</summary>
    public static string Name(IdleTarget target) => target switch
    {
        IdleTarget.Apple => "apple",
        IdleTarget.Spotify => "spotify",
        IdleTarget.YouTube => "youtube",
        _ => "",
    };
}

/// <summary>What a play / pause press shows before the player has answered (pure: tested by the harness).</summary>
internal static class OptimisticPlay
{
    /// <summary>How long the wanted state stands while the session still reports the old one.</summary>
    public const double TimeoutMs = 1500;

    /// <summary>The state to show: the wanted one until the session agrees or <see cref="TimeoutMs"/> ran out, else the session's.</summary>
    public static bool Shown(bool session, bool? wanted, DateTime wantAt, DateTime now) =>
        wanted is { } w && session != w && (now - wantAt).TotalMilliseconds < TimeoutMs ? w : session;

    /// <summary>The session never took the wanted state in time: the press failed.</summary>
    public static bool TimedOut(bool session, bool? wanted, DateTime wantAt, DateTime now) =>
        wanted is { } w && session != w && (now - wantAt).TotalMilliseconds >= TimeoutMs;
}

/// <summary>Whether the closed island still shows the music strip (pure: tested by the harness).</summary>
internal static class MusicActivity
{
    /// <summary>How long the strip stays after the person paused from it, so the button under their pointer does not vanish.</summary>
    public const double LingerSeconds = 5;

    /// <summary>Playing, or paused from the strip less than <see cref="LingerSeconds"/> ago, or paused from it and still under the pointer.</summary>
    public static bool Shows(bool playing, DateTime? pausedAt, DateTime now, bool hovered)
    {
        if (playing)
        {
            return true;
        }

        return pausedAt is { } at && (hovered || (now - at).TotalSeconds < LingerSeconds);
    }
}

/// <summary>The address that makes the Spotify app search (it needs no sign-in).</summary>
internal static class SpotifySearchUri
{
    private const int MaxQueryLength = 200;

    /// <summary><c>spotify:search:</c> and the query as UTF-8 percent-encoding; empty for a blank query.</summary>
    public static string Build(string? query)
    {
        var text = (query ?? "").Trim();
        if (text.Length == 0)
        {
            return "";
        }

        if (text.Length > MaxQueryLength)
        {
            text = text[..MaxQueryLength];
        }

        return "spotify:search:" + Uri.EscapeDataString(text);
    }
}
