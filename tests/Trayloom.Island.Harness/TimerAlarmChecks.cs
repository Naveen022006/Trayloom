// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using NAudio.Wave;
using Trayloom.Features.Island;

namespace Trayloom.Island.Harness;

/// <summary>
/// Checks of the pure parts of the timer alarm (<see cref="TimerAlarmRules"/>, <see cref="AlarmSounds"/>, <see cref="LoopingSamples"/>).
/// Nothing is played and no audio device is opened.
/// </summary>
internal static class TimerAlarmChecks
{
    private static Action<bool, string> _check = (_, _) => { };

    public static void Run(Action<bool, string> check)
    {
        _check = check;
        ShouldRing();
        CanSnooze();
        Focuses();
        Volume();
        Resolve();
        Generate();
        Looping();
    }

    private static void Check(bool ok, string what) => _check(ok, "timer alarm: " + what);

    private static void ShouldRing()
    {
        foreach (var phase in new[] { TimerPhase.Timer, TimerPhase.Focus, TimerPhase.ShortBreak, TimerPhase.LongBreak })
        {
            Check(TimerAlarmRules.ShouldRing(phase, true), $"{phase} rings when enabled");
            Check(!TimerAlarmRules.ShouldRing(phase, false), $"{phase} is silent when disabled");
        }

        Check(!TimerAlarmRules.ShouldRing(TimerPhase.Stopwatch, true), "the stopwatch never rings");
        Check(!TimerAlarmRules.ShouldRing(TimerPhase.Stopwatch, false), "the stopwatch is silent when disabled");
    }

    private static void CanSnooze()
    {
        Check(!TimerAlarmRules.CanSnooze(TimerMode.Stopwatch, true), "a stopwatch cannot snooze");
        Check(!TimerAlarmRules.CanSnooze(TimerMode.Timer, false), "a running timer cannot snooze");
        Check(!TimerAlarmRules.CanSnooze(TimerMode.Pomodoro, false), "an unfinished phase cannot snooze");
        Check(TimerAlarmRules.CanSnooze(TimerMode.Timer, true), "a finished timer can snooze");
        Check(TimerAlarmRules.CanSnooze(TimerMode.Pomodoro, true), "a finished phase can snooze");
    }

    private static void Focuses()
    {
        Check(TimerAlarmRules.FocusesAfterSnooze(3, TimerPhase.Focus) == 2, "snoozing a focus uncounts it");
        Check(TimerAlarmRules.FocusesAfterSnooze(3, TimerPhase.ShortBreak) == 3, "snoozing a short break keeps the count");
        Check(TimerAlarmRules.FocusesAfterSnooze(3, TimerPhase.LongBreak) == 3, "snoozing a long break keeps the count");
        Check(TimerAlarmRules.FocusesAfterSnooze(0, TimerPhase.Focus) == 0, "the count never goes below zero");
    }

    private static void Volume()
    {
        Check(TimerAlarmRules.Volume(-5) == 0f, "volume clamps below");
        Check(TimerAlarmRules.Volume(250) == 1f, "volume clamps above");
        Check(Math.Abs(TimerAlarmRules.Volume(70) - 0.7f) < 1e-6f, "volume 70 is 0.7");
    }

    private static void Resolve()
    {
        Check(AlarmSounds.Resolve("nonsense", _ => true) == "classic", "an unknown id gives classic");
        Check(AlarmSounds.Resolve(null, _ => true) == "classic", "no id gives classic");
        Check(AlarmSounds.Resolve("alarm03", _ => false) == "classic", "a missing file gives classic");
        Check(AlarmSounds.Resolve("alarm03", _ => true) == "alarm03", "an existing file keeps its id");
        Check(AlarmSounds.Resolve("alarm11", _ => true) == "classic", "alarm11 is not an offered sound");
        Check(AlarmSounds.Resolve("gentle", _ => false) == "gentle", "gentle needs no file");
        Check(AlarmSounds.FileFor("alarm03") is { } f && f.EndsWith(@"Media\Alarm03.wav", StringComparison.OrdinalIgnoreCase), "alarm03 maps to Media\\Alarm03.wav");
        Check(AlarmSounds.FileFor("classic") is null, "a generated sound has no file");
    }

    private static void Generate()
    {
        foreach (var id in new[] { "classic", "gentle" })
        {
            var a = AlarmSounds.Generate(id, 44100);
            var b = AlarmSounds.Generate(id, 44100);
            Check(a.Length > 0, $"{id} is not empty");
            Check(a.Max(Math.Abs) <= 0.8f, $"{id} peak is at most 0.8");
            Check(a.Max(Math.Abs) > 0.3f, $"{id} is audible");
            Check(a.SequenceEqual(b), $"{id} is deterministic");
        }
    }

    private static void Looping()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);
        var data = new[] { 1f, 2f, 3f };
        var loop = new LoopingSamples(data, format) { Loop = true };
        var buffer = new float[7];
        Check(loop.Read(buffer, 0, 7) == 7 && buffer.SequenceEqual(new[] { 1f, 2f, 3f, 1f, 2f, 3f, 1f }), "a looping buffer wraps");
        Check(!loop.Ended, "a looping buffer never ends");

        var once = new LoopingSamples(data, format);
        var got = once.Read(buffer, 0, 7);
        Check(got == 3, "a single pass returns the whole buffer");
        Check(once.Ended, "a single pass ends");
        Check(once.Read(buffer, 0, 7) == 0, "a single pass returns 0 after the end");
        Check(new LoopingSamples([], format) { Loop = true }.Read(buffer, 0, 7) == 0, "an empty looping buffer does not spin");
    }
}
