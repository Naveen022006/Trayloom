// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.Island;

/// <summary>English strings of the Dynamic Island (spec 03 Parts B-D and 02d), Windows wording.</summary>
internal static class Strings
{
    // ---- chrome
    public static  string Title => Loc.T("Dynamic Island");
    public static  string Description => Loc.T("Your music, controls and everyday tools, together at the top of your screen. Optional. Turn it off to keep using the separate panels.");
    public static  string Enable => Loc.T("Enable Dynamic Island");
    public static  string Open => Loc.T("Open Dynamic Island");
    public static  string Collapse => Loc.T("Collapse");
    public static  string Pin => Loc.T("Keep open");
    public static  string Unpin => Loc.T("Allow automatic closing");
    public static  string Combine => Loc.T("Combine");
    public static  string DropHint => Loc.T("Drop files here");
    public static  string Empty => Loc.T("Choose what appears in Dynamic Island settings.");
    public static  string Explore => Loc.T("Explore");
    public static  string Back => Loc.T("Back");
    public static  string FindSection => Loc.T("Find a section");
    public static  string SectionKeyboardHint => Loc.T("Use the arrow keys, then press Return");
    public static  string SwitchSection => Loc.T("Switch section");
    public static  string Disabled => Loc.T("Enable the corresponding feature to use it here.");
    public static  string SettingsEllipsis => Loc.T("Settings…");
    public static  string NoResults => Loc.T("No results");
    public static  string Search => Loc.T("Search");
    public static  string Clear => Loc.T("Clear");
    public static  string Settings => Loc.T("Settings");
    public const string AppName = "Trayloom";

    // ---- music
    public static  string MediaNowPlaying => Loc.T("Now Playing");
    public static  string MediaNothingPlaying => Loc.T("Nothing playing");
    public static  string MusicHint => Loc.T("Your music and playback controls appear here.");
    public static  string PlaybackPosition => Loc.T("Playback position");
    public static  string PlayPause => Loc.T("Play or pause");
    public static  string Previous => Loc.T("Previous track");
    public static  string Next => Loc.T("Next track");
    public static  string Lyrics => Loc.T("Lyrics");
    public static  string Queue => Loc.T("Up next");
    public static  string PlaybackSource => Loc.T("Playback source");
    public static  string AutomaticSource => Loc.T("Automatic");
    public static  string Shuffle => Loc.T("Shuffle");
    public static  string Repeat => Loc.T("Repeat");
    public static  string RepeatOne => Loc.T("Repeat one");
    public static  string PlaybackFailed => Loc.T("Could not change playback.");
    public static  string NewTrack => Loc.T("New track");
    public static  string LyricsLoading => Loc.T("Loading lyrics…");
    public static  string LyricsUnavailable => Loc.T("No matching lyrics for this recording.");
    public static  string LyricsFailed => Loc.T("Could not load the lyrics.");
    public static  string LyricsRetry => Loc.T("Try again");
    public static  string LyricsInstrumental => Loc.T("Instrumental");
    public static  string LyricsWaiting => Loc.T("Waiting for the first verse");
    public static  string LyricsNoPosition => Loc.T("The player does not share its position.");
    public static  string LyricsTiming => Loc.T("Timing");
    public static  string LyricsEarlier => Loc.T("Earlier");
    public static  string LyricsLater => Loc.T("Later");
    public static  string LyricsReset => Loc.T("Reset");
    public static  string ImportLyrics => Loc.T("Import lyrics…");
    public static  string LyricsOnlineHint => Loc.T("While lyrics are open, send the song title, artist, album and duration to lrclib.net. Imported lyrics stay on this PC. Song titles, artist names and albums may also be checked with musicbrainz.org.");
    public static  string FindLyricsOnline => Loc.T("Find lyrics online");
    public static  string QueueUnavailable => Loc.T("This player does not share its upcoming songs.");
    public static  string QueueEmpty => Loc.T("No upcoming songs");
    public static  string OpenPlayer => Loc.T("Open player");
    public static  string Refresh => Loc.T("Refresh");
    public static  string VolumeMixer => Loc.T("Volume mixer");

    // ---- controls
    public static  string ControlsTitle => Loc.T("Controls");
    public static  string OutputUnavailable => Loc.T("Output unavailable");
    public static  string Mute => Loc.T("Mute");
    public static  string Unmute => Loc.T("Unmute");
    public static  string KeyboardBacklightUnavailable => Loc.T("Keyboard backlight control is unavailable on this PC.");
    public static  string NoDisplay => Loc.T("No display found.");
    public static  string AppsUseAudio => Loc.T("Apps that use audio show up here");
    public static  string SensorsUnavailable => Loc.T("Sensors unavailable on this PC");

    // ---- settings page
    public static  string Layout => Loc.T("Layout");
    public static  string Content => Loc.T("Content");
    public static  string Activity => Loc.T("Activity");
    public static  string Behavior => Loc.T("Behavior");
    public static  string Companion => Loc.T("Companion");
}
