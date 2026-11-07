// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Features.Island;

namespace Trayloom.Island.Harness;

/// <summary>
/// Console harness for the pure decisions of "Play with nothing loaded" (<see cref="AmIdle.Choose"/>, <see cref="AmIdle.FindTrack"/>,
/// <see cref="LastPlayed"/>). Synthetic songs only: Apple Music is never started. Exit code 0 = all passed.
/// </summary>
internal static class Program
{
    private static int s_failed;
    private static int s_checks;
    private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    private static int Main()
    {
        LastPlayedMatches();
        LastPlayedContext();
        AgeLimits();
        ResumePosition();
        UnknownLastPlayed();
        Favourites();
        RandomPick();
        EmptyLibrary();
        Ordering();
        Matching();
        Json();
        IslandSizesChecks.Run(Check);
        PlacementChecks.Run(Check);
        OpeningDirectionChecks.Run(Check);
        AmDbChecks.Run(Check);
        AmMatchChecks.Run(Check);
        AmServiceChecks.Run(Check);
        LibrarySearchChecks.Run(Check);
        IslandUiChecks.Run(Check);
        SeekChecks.Run(Check);
        MusicCoreChecks.Run(Check);
        SpotifyChecks.Run(Check);
        YouTubeChecks.Run(Check);
        StreamListChecks.Run(Check);
        GoogleCalendarChecks.Run(Check);
        SafetyChecks.Run(Check);
        TimerAlarmChecks.Run(Check);
        FocusChecks.Run(Check);
        MeetingChecks.Run(Check);
        Console.WriteLine($"{s_checks - s_failed}/{s_checks} checks passed");
        return s_failed == 0 ? 0 : 1;
    }

    private static void Check(bool ok, string what)
    {
        s_checks++;
        if (!ok)
        {
            s_failed++;
            Console.WriteLine("FAIL: " + what);
        }
    }

    private static AmTrack Song(ulong pid, string title, string artist = "Artist", string album = "Album", int ms = 200_000) =>
        new() { Pid = pid, Title = title, Artist = artist, Album = album, DurationMs = ms };

    private static AmPlaylist List(ulong pid, string name, params AmTrack[] tracks) => new() { Pid = pid, Name = name, Tracks = tracks };

    private static LastPlayed Last(AmTrack t, TimeSpan age, double position = 100, ulong playlist = 0, double? duration = null) =>
        new(t.Title, t.Artist, t.Album, position, duration ?? t.DurationMs / 1000.0, t.Pid, playlist, Now - age);

    private static IdlePlan Plan(IReadOnlyList<AmTrack> songs, IReadOnlyList<AmTrack>? recent = null, IReadOnlyList<AmPlaylist>? playlists = null, LastPlayed? last = null, int seed = 1) =>
        AmIdle.Choose(songs, recent ?? [], playlists ?? [], last, Now, new Random(seed));

    private static void LastPlayedMatches()
    {
        var a = Song(1, "Alpha");
        var b = Song(2, "Beta");
        var plan = Plan([a, b], last: Last(b, TimeSpan.FromHours(1)));
        Check(plan.First is { Kind: IdleStepKind.PlayTrack } s && s.Track?.Pid == 2, "last played: the remembered song is the first step");
        Check(plan.First?.Page == "songs", "last played: played from the Songs page");
        Check(plan.First?.Playlist is null, "last played: no playlist without a playlist pid");

        // found by title, artist and album when its pid is not known (0) or stale
        var byName = Plan([a, b], last: new LastPlayed("beta", "Artist", "Album", 100, 200, 0, 0, Now - TimeSpan.FromHours(1)));
        Check(byName.First?.Track?.Pid == 2, "last played: matched by title (case-insensitive) when the pid is unknown");
        var stale = Plan([a, b], last: new LastPlayed("Beta", "Artist", "Album", 100, 200, 999, 0, Now - TimeSpan.FromHours(1)));
        Check(stale.First?.Track?.Pid == 2, "last played: matched by name when the stored pid is no longer in the library");
    }

    private static void LastPlayedContext()
    {
        var a = Song(1, "Alpha");
        var b = Song(2, "Beta");
        var holding = List(10, "Mix", a, b);
        var without = List(11, "Other", a);
        var withIt = Plan([a, b], playlists: [holding, without], last: Last(b, TimeSpan.FromHours(1), playlist: 10));
        Check(withIt.First?.Playlist?.Pid == 10, "context: the playlist it was played from is kept when it still holds the song");
        var notHolding = Plan([a, b], playlists: [holding, without], last: Last(b, TimeSpan.FromHours(1), playlist: 11));
        Check(notHolding.First?.Playlist is null && notHolding.First?.Track?.Pid == 2, "context: dropped when the playlist no longer holds the song");
        var gone = Plan([a, b], playlists: [holding], last: Last(b, TimeSpan.FromHours(1), playlist: 77));
        Check(gone.First?.Playlist is null && gone.First?.Track?.Pid == 2, "context: dropped when the playlist is gone");
    }

    private static void AgeLimits()
    {
        var a = Song(1, "Alpha");
        var b = Song(2, "Beta");
        var fav = List(10, "Favourite Songs", a, b);
        Check(Plan([a, b], playlists: [fav], last: Last(b, TimeSpan.FromDays(29))).First?.Why == "last played", "age: 29 days old is still continued");
        var old = Plan([a, b], playlists: [fav], last: Last(b, TimeSpan.FromDays(31)));
        Check(old.First?.Kind == IdleStepKind.ShufflePlaylist, "age: 31 days old is not continued (Favourite Songs next)");
        var future = Plan([a, b], last: Last(b, TimeSpan.FromHours(-3)));
        Check(future.First?.Why == "last played", "age: a stamp in the future (clock change) counts as new");
    }

    private static void ResumePosition()
    {
        var b = Song(2, "Beta", ms: 200_000);
        double? At(TimeSpan age, double pos, double? duration = null) => Plan([b], last: Last(b, age, pos, duration: duration)).First?.ResumeAt;
        Check(At(TimeSpan.FromHours(1), 100) == 100, "resume: a recent position is continued");
        Check(At(TimeSpan.FromHours(11.9), 100) == 100, "resume: 11.9 h old is continued");
        Check(At(TimeSpan.FromHours(12.1), 100) is null, "resume: older than 12 h starts from the beginning");
        Check(At(TimeSpan.FromHours(1), 5) is null, "resume: exactly 5 s is not worth a seek");
        Check(At(TimeSpan.FromHours(1), 5.5) == 5.5, "resume: just over 5 s is continued");
        Check(At(TimeSpan.FromHours(1), 3) is null, "resume: the first seconds are not continued");
        Check(At(TimeSpan.FromHours(1), 190) is null, "resume: the last 10 s are not continued (the song would end at once)");
        Check(At(TimeSpan.FromHours(1), 189) == 189, "resume: 11 s before the end is continued");
        Check(At(TimeSpan.FromHours(1), 100, duration: 0) == 100, "resume: an unknown stored duration falls back to the library's");
        var nothing = Song(3, "Zero", ms: 0);
        Check(Plan([nothing], last: Last(nothing, TimeSpan.FromHours(1), 100, duration: 0)).First?.ResumeAt is null, "resume: no known duration at all, no seek");
    }

    private static void UnknownLastPlayed()
    {
        var a = Song(1, "Alpha");
        var fav = List(10, "Favourite Songs", a);
        var ghost = new LastPlayed("Not In Library", "Nobody", "Nothing", 50, 200, 0, 0, Now - TimeSpan.FromHours(1));
        var plan = Plan([a], playlists: [fav], last: ghost);
        Check(plan.First?.Kind == IdleStepKind.ShufflePlaylist, "unknown last played: falls through to Favourite Songs");
        Check(Plan([a], last: new LastPlayed("", "", "", 0, 0, 0, 0, Now)).First?.Why == "a library song", "unknown last played: an empty title is ignored");
        Check(Plan([a], last: null).First?.Why == "a library song", "unknown last played: none stored");
    }

    private static void Favourites()
    {
        var a = Song(1, "Alpha");
        var b = Song(2, "Beta");
        foreach (var name in new[] { "Favourite Songs", "Favorite Songs", "favourite songs", "  FAVORITE   songs ", "Favourité Songs" })
        {
            var plan = Plan([a, b], playlists: [List(1, "Road trip", a), List(2, name, a, b)]);
            Check(plan.First is { Kind: IdleStepKind.ShufflePlaylist } s && s.Playlist?.Pid == 2, $"favourites: '{name}' is recognised");
        }

        Check(Plan([a], playlists: [List(2, "Favourites", a)]).First?.Kind == IdleStepKind.PlayTrack, "favourites: 'Favourites' is another playlist");
        Check(Plan([a], playlists: [List(2, "Favourite Songs")]).First?.Kind == IdleStepKind.PlayTrack, "favourites: an empty Favourite Songs is not shuffled");
        Check(AmIdle.IsFavourites("Favorite Songs") && AmIdle.IsFavourites("Favourite Songs") && !AmIdle.IsFavourites("Favourite Song"), "favourites: both spellings, nothing else");
    }

    private static void RandomPick()
    {
        var songs = Enumerable.Range(1, 40).Select(i => Song((ulong)i, "Song " + i)).ToList();
        var recent = songs.Skip(30).ToList();
        var picked = new HashSet<ulong>();
        for (var seed = 0; seed < 200; seed++)
        {
            var step = Plan(songs, seed: seed).First;
            if (step?.Track is { } t)
            {
                picked.Add(t.Pid);
            }

            Check(step is { Kind: IdleStepKind.PlayTrack, Page: "songs", Playlist: null } && songs.Contains(step.Track!), $"random: a library song from the Songs page (seed {seed})");
        }

        Check(picked.Count > 10, $"random: the pick varies ({picked.Count} different songs in 200 draws)");

        var fromRecent = Plan(songs, recent, seed: 5).First;
        Check(fromRecent is { Page: "songs" } && recent.Contains(fromRecent.Track!), "random: recently added songs are preferred, played from the Songs page (the Recently added page has no song list)");
        Check(Plan(songs, seed: 9).First?.Track?.Pid == Plan(songs, seed: 9).First?.Track?.Pid, "random: the same random numbers give the same pick");
    }

    private static void EmptyLibrary()
    {
        var plan = Plan([]);
        Check(plan.Steps.Count == 0 && plan.First is null, "empty library: nothing to play");
        var onlyEmptyFavourites = Plan([], playlists: [List(1, "Favourite Songs")]);
        Check(onlyEmptyFavourites.First is null, "empty library: an empty Favourite Songs is nothing either");
        var ghost = new LastPlayed("Gone", "Nobody", "", 50, 200, 5, 0, Now);
        Check(Plan([], last: ghost).First is null, "empty library: a remembered song that is not in it is nothing either");
    }

    private static void Ordering()
    {
        var a = Song(1, "Alpha");
        var b = Song(2, "Beta");
        var c = Song(3, "Gamma");
        var plan = Plan([a, b, c], [c], [List(10, "Favorite Songs", a, b)], Last(b, TimeSpan.FromHours(2)));
        Check(plan.Steps.Count == 3, "order: every step that applies is in the plan");
        Check(plan.Steps[0].Why == "last played" && plan.Steps[1].Kind == IdleStepKind.ShufflePlaylist && plan.Steps[2].Kind == IdleStepKind.PlayTrack,
            "order: last played, then Favourite Songs shuffled, then a library song");
    }

    private static void Matching()
    {
        var plain = Song(1, "Intro", "Band", "First");
        var other = Song(2, "Intro", "Other", "Second");
        var snap = new ClosedSnapshot("intro", "Other", "Second", 10, 100, null, null, Now);
        Check(AmIdle.FindTrack([plain, other], snap)?.Pid == 2, "match: the same title is told apart by artist and album");
        var viaPid = new ClosedSnapshot("Intro", "", "", 10, 100, plain, null, Now);
        Check(AmIdle.FindTrack([plain, other], viaPid)?.Pid == 1, "match: a known pid decides between equals");
        Check(AmIdle.FindTrack([plain], new ClosedSnapshot("Missing", "", "", 0, 0, null, null, Now)) is null, "match: no such title");
        Check(AmIdle.FindTrack([plain], new ClosedSnapshot("  ", "", "", 0, 0, null, null, Now)) is null, "match: blank title");
    }

    private static void Json()
    {
        var value = new LastPlayed("Beta", "Artist", "Album", 12.5, 200, 2, 10, Now);
        Check(LastPlayed.Parse(value.ToJson()) == value, "json: round trip");
        Check(LastPlayed.Parse("") is null && LastPlayed.Parse(null) is null && LastPlayed.Parse("{not json") is null, "json: nothing and garbage read as none");
        Check(LastPlayed.Parse("[1,2]") is null, "json: the wrong shape reads as none");
    }
}
