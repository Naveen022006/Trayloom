// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.Island;

/// <summary>Settings keys of the Spotify and YouTube Music browser. Tokens and API keys are never stored here (see <see cref="SecretStore"/>).</summary>
internal static partial class K
{
    /// <summary>Which source the island's Library shows: "" (automatic), "apple", "spotify" or "youtube".</summary>
    public const string MusicBrowserSource = "notchMusicBrowserSource";

    /// <summary>Show Spotify in the island's Library.</summary>
    public const string MusicSpotify = "notchMusicSpotify";

    /// <summary>Show YouTube Music in the island's Library.</summary>
    public const string MusicYouTube = "notchMusicYouTube";

    /// <summary>The user's Spotify app Client ID (public, not a secret).</summary>
    public const string SpotifyClientId = "notchSpotifyClientId";

    /// <summary>Where YouTube songs open: "music" or "youtube".</summary>
    public const string YouTubeOpenIn = "notchYouTubeOpenIn";

    /// <summary>The height the person dragged the Library in the music page to.</summary>
    public const string MusicLibraryHeight = "notchMusicLibraryHeight";

    /// <summary>Registers the defaults of the music services (off, nothing sent until the user sets one up).</summary>
    private static void RegisterMusicDefaults(IAppSettings app)
    {
        app.RegisterDefaults(new Dictionary<string, object?>
        {
            [MusicBrowserSource] = "",
            [MusicSpotify] = false,
            [MusicYouTube] = false,
            [SpotifyClientId] = "",
            [YouTubeOpenIn] = "music",
            [MusicLibraryHeight] = IslandSizes.DefaultLibraryHeight,
        });
    }
}
