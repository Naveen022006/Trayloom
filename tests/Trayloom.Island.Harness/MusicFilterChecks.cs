// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Features.Island;

namespace Trayloom.Island.Harness;

/// <summary>
/// Checks of the pure rules behind "only Apple Music, Spotify and YouTube Music": which sessions count (<see cref="MusicSources.FromPlayer(string?, IReadOnlySet{string})"/>),
/// which app an idle press goes to (<see cref="IdleTargets.Resolve"/>), the optimistic play / pause state, how long the closed strip
/// stays after a pause and the Spotify search address. Synthetic ids only; no player is touched.
/// </summary>
internal static class MusicFilterChecks
{
    private static Action<bool, string> _check = (_, _) => { };

    public static void Run(Action<bool, string> check)
    {
        _check = check;
        Players();
        Idle();
        Optimistic();
        Strip();
        Search();
        Bounds();
    }

    private static void Check(bool ok, string what) => _check(ok, "music filter: " + what);

    private static void Players()
    {
        var none = new HashSet<string>();
        Check(MusicSources.FromPlayer("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App", none) == MusicSource.Apple, "Apple Music for Windows is Apple Music");
        Check(MusicSources.FromPlayer("Spotify.exe", none) == MusicSource.Spotify, "the Spotify desktop app is Spotify");
        Check(MusicSources.FromPlayer("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", none) == MusicSource.Spotify, "the Spotify store app is Spotify");
        Check(MusicSources.FromPlayer("com.github.th-ch.youtube-music", none) == MusicSource.YouTube, "an id with dashes still names YouTube Music");
        Check(MusicSources.FromPlayer("Chrome._crx_x", none) is null, "a browser web app is not YouTube Music on its own");
        Check(MusicSources.FromPlayer("Chrome._crx_x", new HashSet<string> { "Chrome._crx_x" }) == MusicSource.YouTube, "a web app listed as 'YouTube Music' in the Start menu is YouTube Music");
        Check(MusicSources.FromPlayer("chrome._crx_x", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Chrome._crx_x" }) == MusicSource.YouTube, "the web app id is compared without case");
        Check(MusicSources.FromPlayer("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", none) is null, "Windows Media Player is ignored");
        Check(MusicSources.FromPlayer("Chrome", none) is null && MusicSources.FromPlayer("MSEdge", none) is null, "browsers are ignored");
        Check(MusicSources.FromPlayer(null, none) is null && MusicSources.FromPlayer("", none) is null, "no id is no source");
        Check(MusicSources.FromPlayer(PlaybackService.YouTubePlayerAumid, none) == MusicSource.YouTube, "Trayloom's own YouTube Music player is YouTube Music");
    }

    private static void Idle()
    {
        Check(IdleTargets.Resolve("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", null, false, null, null) == IdleTarget.Choose, "a saved Media Player id with nothing else installed asks which app");
        Check(IdleTargets.Resolve("", "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", true, "Spotify.exe", null) == IdleTarget.Spotify, "the app that played last wins in Automatic");
        Check(IdleTargets.Resolve("", null, true, null, null) == IdleTarget.Apple, "Apple Music alone is the target");
        Check(IdleTargets.Resolve("", null, true, "Spotify.exe", null) == IdleTarget.Choose, "two apps and no history ask which one");
        Check(IdleTargets.Resolve("apple", "Spotify.exe", true, "Spotify.exe", null) == IdleTarget.Apple, "a saved app beats the one that played last");
        Check(IdleTargets.Resolve("spotify", null, true, null, null) == IdleTarget.Apple, "a saved app that is gone falls back to Automatic");
        Check(IdleTargets.Resolve("youtube", null, false, null, null) == IdleTarget.YouTube, "YouTube Music is always available (the island's own player)");
        Check(IdleTargets.Resolve("", null, false, null, null) == IdleTarget.Choose, "nothing installed asks which app (YouTube Music is on the menu)");
        Check(IdleTargets.Name(IdleTarget.Choose) == "" && IdleTargets.Name(IdleTarget.YouTube) == "youtube", "the saved names are the source names");
    }

    private static void Optimistic()
    {
        var at = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);
        Check(OptimisticPlay.Shown(true, false, at, at.AddMilliseconds(100)) == false, "a pause shows at once while the session still plays");
        Check(OptimisticPlay.Shown(false, true, at, at.AddMilliseconds(1499)) == true, "the wanted state is held until 1.5 s");
        Check(OptimisticPlay.Shown(true, false, at, at.AddMilliseconds(1500)) == true, "after 1.5 s the session's state is shown again");
        Check(OptimisticPlay.Shown(false, false, at, at.AddMilliseconds(100)) == false && OptimisticPlay.Shown(true, null, at, at) == true, "an agreeing or absent wish changes nothing");
        Check(OptimisticPlay.TimedOut(true, false, at, at.AddMilliseconds(1500)), "a press the session ignored for 1.5 s has failed");
        Check(!OptimisticPlay.TimedOut(true, false, at, at.AddMilliseconds(1000)) && !OptimisticPlay.TimedOut(false, false, at, at.AddMilliseconds(2000)), "not failed while waiting, or once the session agrees");
    }

    private static void Strip()
    {
        var at = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);
        Check(MusicActivity.Shows(false, at, at.AddSeconds(4.9), false), "the strip stays 4.9 s after a pause from it");
        Check(!MusicActivity.Shows(false, at, at.AddSeconds(5.1), false), "the strip goes after 5.1 s");
        Check(MusicActivity.Shows(false, at, at.AddMinutes(10), true), "the strip stays while the pointer is on it");
        Check(MusicActivity.Shows(true, null, at, false), "a playing song always shows");
        Check(!MusicActivity.Shows(false, null, at, false), "a pause the island did not make hides the strip");
    }

    private static void Search()
    {
        Check(SpotifySearchUri.Build("daft punk") == "spotify:search:daft%20punk", "spaces are percent-encoded");
        Check(SpotifySearchUri.Build("Björk – Jóga") == "spotify:search:Bj%C3%B6rk%20%E2%80%93%20J%C3%B3ga", "unicode is encoded as UTF-8");
        Check(SpotifySearchUri.Build("a&b=c") == "spotify:search:a%26b%3Dc", "reserved characters cannot change the address");
        Check(SpotifySearchUri.Build("   ") == "" && SpotifySearchUri.Build(null) == "", "a blank query builds nothing");
        Check(SpotifySearchUri.Build(new string('x', 500)).Length == "spotify:search:".Length + 200, "a long query is cut");
    }

    private static void Bounds()
    {
        Check(YouTubePlayerBounds.TryParse("10,20,960,640", out var l, out var t, out var w, out var h) && l == 10 && t == 20 && w == 960 && h == 640, "saved bounds are read");
        Check(!YouTubePlayerBounds.TryParse("", out _, out _, out _, out _) && !YouTubePlayerBounds.TryParse("1,2,3", out _, out _, out _, out _) && !YouTubePlayerBounds.TryParse("1,2,0,5", out _, out _, out _, out _) && !YouTubePlayerBounds.TryParse("a,b,c,d", out _, out _, out _, out _), "unusable bounds are refused");
        Check(YouTubePlayerBounds.Format(10.4, 20.6, 960, 640) == "10,21,960,640", "bounds are saved as whole pixels");
        Check(YouTubePlayerHost.IsAllowed("https://music.youtube.com/watch?v=dQw4w9WgXcQ") && !YouTubePlayerHost.IsAllowed("http://music.youtube.com/") && !YouTubePlayerHost.IsAllowed("https://evil.example/music.youtube.com") && !YouTubePlayerHost.IsAllowed("file:///c:/x"), "the player loads YouTube Music addresses only");
    }
}
