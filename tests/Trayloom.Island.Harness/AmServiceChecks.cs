// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Features.Island;

namespace Trayloom.Island.Harness;

/// <summary>
/// Checks of the pure decisions behind <c>AppleMusicService</c>: finding a remembered song among every track of the library (playlist-only
/// ones too) with the right playlist context, splitting the media session's "Artist — Album", the resume-after-quit choice
/// (<see cref="AmResume.Pick"/>), the idle plan and the library reload debounce. Synthetic data only; Apple Music is never touched.
/// </summary>
internal static class AmServiceChecks
{
    private static Action<bool, string> _check = (_, _) => { };

    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    public static void Run(Action<bool, string> check)
    {
        _check = check;
        SplitCases();
        PlaylistOnlyLastPlayed();
        StoredContext();
        VersionsAndRestarts();
        IdlePlanNeverRecent();
        ResumePick();
        PidResolution();
        NowPlayingHighlight();
        ReloadDebounce();
    }

    private static void Check(bool ok, string what) => _check(ok, "service: " + what);

    private static AmTrack T(ulong pid, string title, string artist, string album, bool inLibrary = true, DateTime? added = null) =>
        new() { Pid = pid, Title = title, Artist = artist, Album = album, AlbumArtist = artist, InLibrary = inLibrary, Added = added, DurationMs = 200_000 };

    private const string Lumen = "Lumen - Northern Lights (From “Lantern”)";

    /// <summary>
    /// Library songs 1-4 (Songs page); playlist-only 10 (Lumen), 11 and 12 (two versions of 'Willow'). Playlists in sidebar order: 100 "Big"
    /// holds 10, 11 and 12 and the songs; 101 "Small" holds only 10.
    /// </summary>
    private static AmDb Library()
    {
        AmTrack[] tracks =
        [
            T(1, "Tide", "Hari", "Harbor", added: Now.AddDays(-1)),
            T(2, "Tide (From “Harbor”)", "Hari", "Harbor - Single", added: Now.AddDays(-2)),
            T(3, "Cascade", "Dev", "Singles", added: Now.AddDays(-3)),
            T(4, "Zebra", "Zed", "Stripes"),
            T(10, Lumen, "Ari Vance & Sol Rivers", "Lantern - Single", inLibrary: false),
            T(11, "Willow", "bôa", "Twilight", inLibrary: false),
            T(12, "Willow", "Cover Band", "Covers", inLibrary: false),
        ];
        (ulong, string, IReadOnlyList<ulong>)[] playlists =
        [
            (100, "Big", [10, 11, 12, 1, 2, 3]),
            (101, "Small", [10]),
        ];
        return AmDb.FromTracks(tracks, [1, 2, 3, 4], playlists);
    }

    private static LastPlayed Last(string title, string artist, string album, ulong pid = 0, ulong playlist = 0, double position = 80, TimeSpan? age = null) =>
        new(title, artist, album, position, 200, pid, playlist, Now - (age ?? TimeSpan.FromHours(1)));

    private static ClosedSnapshot Snap(string title, string artist, string album = "", AmTrack? track = null, AmPlaylist? playlist = null) =>
        new(title, artist, album, 80, 200, track, playlist, Now);

    // ---------------------------------------------------------------- splitting

    private static void SplitCases()
    {
        Check(AmIdle.SplitAppleArtist("Artist — Album") is ("Artist", "Album"), "split: 'Artist — Album'");
        Check(AmIdle.SplitAppleArtist("Ari Vance & Sol Rivers — Lumen - Northern Lights (From \"Lantern\") - Single") is ("Ari Vance & Sol Rivers", "Lumen - Northern Lights (From \"Lantern\") - Single"),
            "split: the real session text keeps the album with its own hyphens");
        Check(AmIdle.SplitAppleArtist("Just An Artist") is ("Just An Artist", ""), "split: no separator, no album");
        Check(AmIdle.SplitAppleArtist("") is ("", "") && AmIdle.SplitAppleArtist(null) is ("", ""), "split: empty and null");
        Check(AmIdle.SplitAppleArtist("A — B — C") is ("A", "B — C"), "split: only the first separator splits");
        Check(AmIdle.SplitAppleArtist("A - B") is ("A - B", ""), "split: a plain hyphen is no separator");
        Check(AmIdle.SplitNowPlaying("A — Folded", "") is ("A", "Folded"), "split: the folded album is used when the session has none");
        Check(AmIdle.SplitNowPlaying("A — Folded", "Real") is ("A", "Real"), "split: the session's own album wins");
        Check(AmIdle.SameSong("Song", "A — Album", "song", "A"), "same song: the album folded into the artist is ignored");
        Check(!AmIdle.SameSong("Song", "A", "Song", "B") && !AmIdle.SameSong("Song", "A", "Other", "A"), "same song: another artist or title is another song");
    }

    // ---------------------------------------------------------------- idle plan / last played

    private static void PlaylistOnlyLastPlayed()
    {
        var db = Library();
        // as the session reports it: pids lost, album folded into the artist
        var last = Last(Lumen, "Ari Vance & Sol Rivers — Lantern - Single", "");
        var first = AmIdle.Choose(db, last, Now, new Random(1)).First;
        Check(first is { Kind: IdleStepKind.PlayTrack, Why: "last played" } && first.Track?.Pid == 10, "last played: a playlist-only song is found among every track");
        Check(first?.Playlist?.Pid == 101, "last played: its context is the smallest playlist that holds it");
        Check(first?.Page == "songs" && first.ResumeAt == 80, "last played: Songs page, position continued");

        var viaPid = AmIdle.Choose(db, Last(Lumen, "x", "", pid: 10), Now, new Random(1)).First;
        Check(viaPid?.Track?.Pid == 10, "last played: the stored Pid is honoured as evidence");

        var librarySong = AmIdle.Choose(db, Last("Cascade", "Dev", "Singles"), Now, new Random(1)).First;
        Check(librarySong?.Track?.Pid == 3 && librarySong.Playlist is null, "last played: a library song plays from the Songs page");
    }

    private static void StoredContext()
    {
        var db = Library();
        var big = Last(Lumen, "Ari Vance & Sol Rivers", "Lantern - Single", pid: 10, playlist: 100);
        Check(AmIdle.Choose(db, big, Now, new Random(1)).First?.Playlist?.Pid == 100, "context: the stored playlist wins when it holds the song");
        var notHolding = Last(Lumen, "Ari Vance & Sol Rivers", "Lantern - Single", pid: 10, playlist: 999);
        Check(AmIdle.Choose(db, notHolding, Now, new Random(1)).First?.Playlist?.Pid == 101, "context: an unknown stored playlist falls back to the default context");
        var librarySongInPlaylist = Last("Tide", "Hari", "Harbor", pid: 1, playlist: 100);
        Check(AmIdle.Choose(db, librarySongInPlaylist, Now, new Random(1)).First?.Playlist?.Pid == 100, "context: a library song played from a playlist keeps that playlist");
        var tooOld = Last(Lumen, "Ari Vance & Sol Rivers", "", age: TimeSpan.FromDays(40));
        Check(AmIdle.Choose(db, tooOld, Now, new Random(1)).First?.Why != "last played", "context: a song older than 30 days is not continued");
    }

    private static void VersionsAndRestarts()
    {
        var db = Library();
        // two 'Willow' versions: the artist decides
        var cover = AmIdle.FindTrack(db.AllTracks, Snap("Willow", "Cover Band — Covers"));
        Check(cover?.Pid == 12, "versions: the artist picks the cover");
        var original = AmIdle.FindTrack(db.AllTracks, Snap("Willow", "bôa — Twilight"));
        Check(original?.Pid == 11, "versions: accents are folded (boa = bôa)");
        Check(AmIdle.FindTrack(db.AllTracks, Snap("Willow", "Nobody")) is null, "versions: an unrelated artist matches neither version of a shared title");
        Check(AmIdle.FindTrack(db.AllTracks, Snap("Willow", "Nobody", track: db.ByPid(12))) is { Pid: 12 }, "versions: the remembered Pid is evidence");
        Check(AmIdle.FindTrack(db.AllTracks, Snap("Zebra", "Somebody else")) is { Pid: 4 }, "versions: the only track with the title matches without further evidence");
        Check(AmIdle.FindTrack(db.AllTracks, Snap("Tide", "Hari — Harbor")) is { Pid: 1 }, "versions: 'Tide' is not 'Tide (From “Harbor”)'");
        Check(AmIdle.FindTrack(db.AllTracks, Snap("tide (from \"harbor\")", "Hari")) is { Pid: 2 }, "versions: curly and straight quotes agree");
        Check(AmIdle.FindTrack(db.AllTracks, Snap("", "Hari")) is null, "versions: an empty title finds nothing");

        // a sample with pids 0 of the same song must not lose what is stored: the pure part is SameSong + UniqueTrack
        Check(AmIdle.SameSong(Lumen, "Ari Vance & Sol Rivers", Lumen, "Ari Vance & Sol Rivers — Lantern - Single"), "restart: the same song is recognised across the split");
    }

    private static void IdlePlanNeverRecent()
    {
        var db = Library();
        var rng = new Random(3);
        for (var i = 0; i < 40; i++)
        {
            var plan = AmIdle.Choose(db, Last(Lumen, "Ari Vance & Sol Rivers", ""), Now, rng);
            Check(plan.Steps.All(s => s.Page != "recent"), "idle plan: no step uses the Recently added page");
        }

        var noLast = AmIdle.Choose(db, null, Now, new Random(5));
        Check(noLast.First is { Kind: IdleStepKind.PlayTrack, Page: "songs" } && noLast.First.Track!.InLibrary, "idle plan: the random pick is a library song from the Songs page");
        var noRecent = AmIdle.Choose(db.AllTracks.Where(t => t.InLibrary).ToList(), db.Songs, [], [], null, Now, new Random(5), _ => null);
        Check(noRecent.First is { Page: "songs" }, "idle plan: without recent songs the Songs page too");
    }

    // ---------------------------------------------------------------- resume after quit

    private static void ResumePick()
    {
        var db = Library();
        var pick = AmResume.Pick(db, Snap(Lumen, "Ari Vance & Sol Rivers — Lantern - Single"));
        Check(pick is { Track.Pid: 10 } && pick.Context?.Pid == 101, "resume: a playlist-only song is found with its default context");

        var oldPlaylist = new AmPlaylist { Pid = 100, Name = "Big", Tracks = [] };
        var fromBig = AmResume.Pick(db, Snap(Lumen, "Ari Vance & Sol Rivers", track: db.ByPid(10), playlist: oldPlaylist));
        Check(fromBig?.Context?.Pid == 100 && ReferenceEquals(fromBig.Context, db.Playlists[0]), "resume: the playlist it played from is taken from the new snapshot of the library");

        var gone = new AmPlaylist { Pid = 555, Name = "Gone", Tracks = [] };
        Check(AmResume.Pick(db, Snap(Lumen, "Ari Vance & Sol Rivers", playlist: gone))?.Context?.Pid == 101, "resume: a playlist that no longer holds the song falls back to the default context");
        Check(AmResume.Pick(db, Snap("Cascade", "Dev", playlist: gone)) is { Track.Pid: 3, Context: null }, "resume: a library song resumes from the Songs page");
        Check(AmResume.Pick(db, Snap("Not In The Library", "Nobody")) is null, "resume: a song the library does not have is no pick");
        Check(AmResume.Pick(AmDb.FromTracks([], null), Snap("Cascade", "Dev")) is null, "resume: an empty library is no pick");
    }

    private static void PidResolution()
    {
        var db = Library();
        Check(AmIdle.UniqueTrack(db.AllTracks, Lumen, "Ari Vance & Sol Rivers — Lantern - Single")?.Pid == 10, "pid: a unique title and artist resolves");
        Check(AmIdle.UniqueTrack(db.AllTracks, "Willow", "Somebody") is null, "pid: an artist that matches no version resolves nothing");
        Check(AmIdle.UniqueTrack(db.AllTracks, "Willow", "bôa")?.Pid == 11, "pid: the artist tells the versions apart");
        var twins = AmDb.FromTracks([T(1, "Same", "Artist", "One"), T(2, "Same", "Artist", "Two")], null);
        Check(AmIdle.UniqueTrack(twins.AllTracks, "Same", "Artist") is null, "pid: two equal hits are ambiguous, no guess");
    }

    // ---------------------------------------------------------------- now playing highlight

    private static void NowPlayingHighlight()
    {
        var db = Library();
        var detail = AmIdle.DetailKey("Ari Vance & Sol Rivers — Lantern - Single", "");
        Check(AmIdle.IsPlaying(db.ByPid(10)!, Lumen, detail), "highlight: the split album and artist agree with the playlist-only song");
        Check(!AmIdle.IsPlaying(db.ByPid(11)!, "Willow", detail) && !AmIdle.IsPlaying(db.ByPid(10)!, "Other", detail), "highlight: another title is not highlighted");
        var willow = AmIdle.DetailKey("Cover Band — Covers", "");
        Check(AmIdle.IsPlaying(db.ByPid(12)!, "Willow", willow) && !AmIdle.IsPlaying(db.ByPid(11)!, "Willow", willow), "highlight: of two versions only the one whose artist or album agrees");
    }

    // ---------------------------------------------------------------- reload debounce

    private static void ReloadDebounce()
    {
        var loaded = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var changed = loaded.AddMinutes(5);
        DateTime? seen = null;
        Check(!AmReload.ShouldLoad(loaded, null, loaded, ref seen), "reload: an unchanged file is not loaded");
        Check(!AmReload.ShouldLoad(loaded, null, changed, ref seen), "reload: the first poll that sees a change waits (the file may still be written)");
        Check(AmReload.ShouldLoad(loaded, null, changed, ref seen), "reload: the same new stamp at the next poll is loaded");

        DateTime? seen2 = null;
        AmReload.ShouldLoad(loaded, null, changed, ref seen2);
        Check(!AmReload.ShouldLoad(loaded, null, changed.AddSeconds(2), ref seen2), "reload: a file that keeps changing is not loaded yet");
        Check(AmReload.ShouldLoad(loaded, null, changed.AddSeconds(2), ref seen2), "reload: it is loaded once it rests");

        DateTime? seen3 = changed;
        Check(!AmReload.ShouldLoad(loaded, changed, changed, ref seen3), "reload: a stamp that failed to load is not retried until it changes");
        DateTime? seen4 = changed;
        Check(!AmReload.ShouldLoad(changed, null, changed, ref seen4), "reload: the loaded stamp is not loaded again");
    }
}
