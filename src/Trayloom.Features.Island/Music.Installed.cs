// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using Microsoft.Win32;

namespace Trayloom.Features.Island;

/// <summary>
/// Which of the three music apps are on this PC, as far as the island knows (read from the cached Start menu list and the registry; never waits).
/// Spotify is "connected automatically": its being installed is all the island needs to control it through its media session.
/// </summary>
internal static class MusicInstalled
{
    /// <summary>Opens the Spotify app by its protocol when only that is known (registered by the desktop app).</summary>
    public const string SpotifyProtocolToken = "spotify:";

    /// <summary>What opens the Spotify app (a Start menu id or path, or <see cref="SpotifyProtocolToken"/>); null when it is not installed.</summary>
    public static string? SpotifyAumid()
    {
        var found = PlaybackService.InstalledPlayers().FirstOrDefault(p => MusicSources.FromPlayer(p.Aumid) == MusicSource.Spotify || MusicSources.Normalize(p.Name).Contains("spotify", StringComparison.Ordinal));
        if (found.Aumid is not null)
        {
            return found.Aumid;
        }

        return ProtocolRegistered() ? SpotifyProtocolToken : null;
    }

    /// <summary>What opens an installed YouTube Music web app; null when there is none (the island's own player needs no install).</summary>
    public static string? YouTubeMusicAumid() =>
        PlaybackService.InstalledPlayers().FirstOrDefault(p => PlaybackService.YtmAppIds.Contains(p.Aumid) && MusicSources.FromPlayer(p.Aumid, PlaybackService.YtmAppIds) == MusicSource.YouTube).Aumid;

    /// <summary>Opens the Spotify app (never a browser and never a media key); false when it is not installed.</summary>
    public static bool OpenSpotify()
    {
        var target = SpotifyAumid();
        if (target is null)
        {
            return false;
        }

        if (target == SpotifyProtocolToken)
        {
            return Launch(SpotifyProtocolToken);
        }

        PlaybackService.OpenPlayer(target);
        return true;
    }

    /// <summary>Hands a search to the Spotify app (<c>spotify:search:…</c>); false when it is not installed or the text is blank.</summary>
    public static bool SearchInSpotify(string? query)
    {
        var uri = SpotifySearchUri.Build(query);
        return uri.Length > 0 && SpotifyAumid() is not null && Launch(uri);
    }

    private static bool Launch(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool ProtocolRegistered()
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey("spotify");
            return key?.GetValue("URL Protocol") is not null;
        }
        catch
        {
            return false;
        }
    }
}
