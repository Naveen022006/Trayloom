// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using NAudio.Wave;
using Trayloom.Features.Island;

namespace Trayloom.Island.Harness;

/// <summary>
/// Checks of the pure parts of the meeting heads-up (<see cref="MeetingRules"/> and <see cref="MicCheck.PeakOf"/>).
/// Nothing is opened: no microphone, no camera and no calendar.
/// </summary>
internal static class MeetingChecks
{
    private static readonly DateTime Start = new(2026, 11, 6, 10, 30, 0);
    private static readonly IReadOnlySet<string> None = new HashSet<string>();
    private static Action<bool, string> _check = (_, _) => { };

    public static void Run(Action<bool, string> check)
    {
        _check = check;
        Window();
        Filters();
        Choice();
        Countdown();
        Peak();
    }

    private static void Check(bool ok, string what) => _check(ok, "meeting: " + what);

    private static CalEvent Event(string id, DateTime start, int minutes = 30, string? link = "https://meet.google.com/aaa-bbbb-ccc", bool allDay = false) => new()
    {
        Id = id,
        Title = "x",
        Start = start,
        End = start.AddMinutes(minutes),
        AllDay = allDay,
        JoinUrl = link,
    };

    private static bool Shown(CalEvent e, DateTime now, int lead = 5, bool videoOnly = true) => MeetingRules.Pick([e], now, lead, videoOnly, None) is not null;

    private static void Window()
    {
        var e = Event("a", Start);
        Check(!Shown(e, Start.AddMinutes(-5).AddSeconds(-1)), "nothing before the lead");
        Check(Shown(e, Start.AddMinutes(-5)), "shown at the lead");
        Check(Shown(e, Start.AddMinutes(-2), 2), "a 2 min lead shows at 2 min");
        Check(!Shown(e, Start.AddMinutes(-3), 2), "a 2 min lead hides at 3 min");
        Check(Shown(e, Start), "shown at the start");
        Check(Shown(e, Start.AddMinutes(9)), "shown at start + 9 min");
        Check(!Shown(e, Start.AddMinutes(11)), "gone at start + 11 min");
        Check(!Shown(e, Start.AddMinutes(10)), "gone at start + 10 min");
        var shortOne = Event("b", Start, 6);
        Check(Shown(shortOne, Start.AddMinutes(5)), "a short event is shown until its end");
        Check(!Shown(shortOne, Start.AddMinutes(6)), "a short event stops at its end, not at start + 10 min");
        Check(MeetingRules.Until(shortOne) == Start.AddMinutes(6) && MeetingRules.Until(e) == Start.AddMinutes(10), "Until is the earlier of the end and start + 10 min");
    }

    private static void Filters()
    {
        var now = Start.AddMinutes(-3);
        Check(!Shown(Event("c", Start, allDay: true), now), "all-day events are ignored");
        Check(Shown(Event("d", Start, link: null), now, videoOnly: false), "no link is fine when video-only is off");
        Check(!Shown(Event("e", Start, link: null), now, videoOnly: true), "no link is skipped when video-only is on");
        Check(!Shown(Event("f", Start, link: ""), now, videoOnly: true), "an empty link counts as no link");
        Check(Shown(Event("g", Start), now, videoOnly: true), "a link is shown when video-only is on");
        var skipped = Event("h", Start);
        Check(MeetingRules.Pick([skipped], now, 5, true, new HashSet<string> { MeetingRules.Key(skipped) }) is null, "a dismissed key is skipped");
        Check(MeetingRules.Pick([skipped], now, 5, true, new HashSet<string> { "other" }) is not null, "another dismissed key does not hide it");
    }

    private static void Choice()
    {
        var now = Start.AddMinutes(-1);
        var early = Event("early", Start.AddMinutes(-1));
        var late = Event("late", Start.AddMinutes(2));
        Check(MeetingRules.Pick([late, early], now, 5, true, None)?.Id == "early", "the earliest start wins");
        var noLink = Event("plain", Start, link: null);
        var withLink = Event("linked", Start);
        Check(MeetingRules.Pick([noLink, withLink], now, 5, false, None)?.Id == "linked", "on a tie the one with a link wins");
        Check(MeetingRules.Pick([withLink, noLink], now, 5, false, None)?.Id == "linked", "on a tie the link wins whatever the order");
        Check(MeetingRules.Pick([], now, 5, true, None) is null, "an empty list gives nothing");
    }

    private static void Countdown()
    {
        Check(MeetingRules.Countdown(Start, Start.AddMinutes(-4).AddSeconds(-32)) == "4:32", "4:32 before the start");
        Check(MeetingRules.Countdown(Start, Start.AddSeconds(-59)) == "0:59", "0:59 in the last minute");
        Check(MeetingRules.Countdown(Start, Start.AddSeconds(-1)) == "0:01", "0:01 a second before");
        Check(MeetingRules.Countdown(Start, Start.AddMilliseconds(-200)) == "0:01", "a part of a second rounds up");
        Check(MeetingRules.Countdown(Start, Start) == "", "empty at the start");
        Check(MeetingRules.Countdown(Start, Start.AddMinutes(3)) == "", "empty once started");
        Check(MeetingRules.Countdown(Start, Start.AddMinutes(-15)) == "15:00", "15:00 at the longest lead");
    }

    private static void Peak()
    {
        var pcm = new WaveFormat(48000, 16, 1);
        var full = BitConverter.GetBytes(short.MaxValue);
        Check(Math.Abs(MicCheck.PeakOf(full, full.Length, pcm) - 1.0) < 1e-9, "PCM16 full scale is 1.0");
        var negative = BitConverter.GetBytes(short.MinValue);
        Check(Math.Abs(MicCheck.PeakOf(negative, negative.Length, pcm) - 1.0) < 1e-9, "PCM16 negative full scale is 1.0");
        var silence = new byte[64];
        Check(MicCheck.PeakOf(silence, silence.Length, pcm) == 0, "PCM16 silence is 0");
        var mixed = new byte[6];
        BitConverter.GetBytes((short)1000).CopyTo(mixed, 0);
        BitConverter.GetBytes((short)-16384).CopyTo(mixed, 2);
        BitConverter.GetBytes((short)200).CopyTo(mixed, 4);
        Check(Math.Abs(MicCheck.PeakOf(mixed, mixed.Length, pcm) - 16384 / 32767.0) < 1e-9, "PCM16 takes the loudest sample, either sign");
        Check(MicCheck.PeakOf(mixed, 2, pcm) == 1000 / 32767.0, "only the recorded bytes count");

        var ieee = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);
        var half = BitConverter.GetBytes(0.5f);
        Check(Math.Abs(MicCheck.PeakOf(half, half.Length, ieee) - 0.5) < 1e-9, "float32 0.5 is 0.5");
        var minus = BitConverter.GetBytes(-0.25f);
        Check(Math.Abs(MicCheck.PeakOf(minus, minus.Length, ieee) - 0.25) < 1e-9, "float32 uses the absolute value");
        var loud = BitConverter.GetBytes(3.0f);
        Check(MicCheck.PeakOf(loud, loud.Length, ieee) == 1.0, "float32 above full scale is capped at 1.0");
        var nan = BitConverter.GetBytes(float.NaN);
        Check(MicCheck.PeakOf(nan, nan.Length, ieee) == 0, "float32 NaN reads as silence");
        Check(MicCheck.PeakOf(silence, silence.Length, ieee) == 0, "float32 silence is 0");
        Check(MicCheck.PeakOf(half, 3, ieee) == 0, "a partial float sample is ignored");
        Check(MicCheck.PeakOf(full, full.Length, new WaveFormat(48000, 24, 1)) == 0, "an unknown format reads as silence");
    }
}
