// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Features.WindowTools.AwayMode;

namespace Trayloom.Away.Harness;

/// <summary>The checks of spec 08 section 1.11, in two groups: the picture maths and the state machine.</summary>
internal static class PresenceChecks
{
    private const long LeaveMs = 60_000;
    private const long Start = 1_000_000;

    private static readonly AwayObservation Absent = new(false, false, true, true);
    private static readonly AwayObservation Face = new(true, false, true, true);
    private static readonly AwayObservation Moving = new(false, true, true, true);
    private static readonly AwayObservation Dark = new(false, false, false, true);

    // ------------------------------------------------------------------------------------------ maths

    public static void Math()
    {
        var constant = Enumerable.Repeat((byte)100, 640 * 480).ToArray();
        var grid = AwayPresenceMath.Grid(constant, 0, 640, 480, 640);
        Program.Check(grid.Length == 3072 && grid.All(v => v == 100), "grid of a constant 640x480 frame is all equal");

        // A stride wider than the width: the padding is 255 and must never be read.
        var padded = new byte[700 * 480];
        Array.Fill(padded, (byte)255);
        for (var y = 0; y < 480; y++)
        {
            Array.Fill(padded, (byte)100, y * 700, 640);
        }

        var paddedGrid = AwayPresenceMath.Grid(padded, 0, 640, 480, 700);
        Program.Check(paddedGrid.All(v => v == 100), "grid respects a stride larger than the width");

        // A start offset (plane description) is honoured too.
        var shifted = new byte[64 + (640 * 480)];
        Array.Fill(shifted, (byte)0, 0, 64);
        Array.Fill(shifted, (byte)100, 64, 640 * 480);
        Program.Check(AwayPresenceMath.Grid(shifted, 64, 640, 480, 640).All(v => v == 100), "grid honours the start offset");

        // 641 x 481: the last column and the last row belong to the edge blocks.
        var odd = new byte[641 * 481];
        for (var y = 0; y < 481; y++)
        {
            odd[(y * 641) + 640] = 200;
        }

        var oddGrid = AwayPresenceMath.Grid(odd, 0, 641, 481, 641);
        Program.Check(oddGrid[63] > 0 && oddGrid[62] == 0, "641x481 frame keeps its edge column in the edge blocks");
        var rows = new byte[641 * 481];
        Array.Fill(rows, (byte)200, 480 * 641, 641);
        var rowGrid = AwayPresenceMath.Grid(rows, 0, 641, 481, 641);
        Program.Check(rowGrid[(47 * 64) + 10] > 0 && rowGrid[(46 * 64) + 10] == 0, "641x481 frame keeps its edge row in the edge blocks");

        var random = new Random(7);
        var scene = new byte[3072];
        for (var i = 0; i < scene.Length; i++)
        {
            scene[i] = (byte)random.Next(60, 140);
        }

        Program.Check(!AwayPresenceMath.Motion(scene, scene.ToArray()), "identical grids give no motion");

        var brighter = scene.Select(v => (byte)(v + 30)).ToArray();
        Program.Check(!AwayPresenceMath.Motion(scene, brighter), "a global +30 brightness shift gives no motion");

        var block = scene.ToArray();
        for (var y = 10; y < 20; y++)
        {
            for (var x = 20; x < 30; x++)
            {
                block[(y * 64) + x] = (byte)(block[(y * 64) + x] + 60);
            }
        }

        Program.Check(AwayPresenceMath.Motion(scene, block), "a 10x10 block changed by +60 gives motion");

        var noise = new Random(11);
        var noisyA = scene.Select(v => (byte)(v + noise.Next(-5, 6))).ToArray();
        var noisyB = scene.Select(v => (byte)(v + noise.Next(-5, 6))).ToArray();
        Program.Check(!AwayPresenceMath.Motion(noisyA, noisyB), "+-5 pseudo-random noise gives no motion");

        var dim = Enumerable.Repeat((byte)10, 3072).ToArray();
        Program.Check(!AwayPresenceMath.Usable(AwayPresenceMath.Stats(dim)), "a frame with mean 10 is not usable");
        var flat = Enumerable.Range(0, 3072).Select(i => (byte)(i % 2 == 0 ? 119 : 121)).ToArray();
        var flatStats = AwayPresenceMath.Stats(flat);
        Program.Check(flatStats.Mean is > 119 and < 121 && flatStats.Spread < 2 && !AwayPresenceMath.Usable(flatStats), "a uniform frame (mean 120, spread 1) is not usable");
        Program.Check(AwayPresenceMath.Usable(AwayPresenceMath.Stats(scene)), "a normal scene is usable");
    }

    // ------------------------------------------------------------------------------------------ state machine

    /// <summary>A clock that has just been told to start the camera, and the time the camera became ready.</summary>
    private static (AwayPresenceClock Clock, long Ready) Watching()
    {
        AwayPresenceClock.ArmIdleMs = 30_000;
        var clock = new AwayPresenceClock();
        var command = clock.Tick(Start, 30_000, true, false, LeaveMs);
        if (command != AwayPresenceCommand.StartCamera)
        {
            throw new InvalidOperationException("setup: camera did not start");
        }

        clock.CameraStarted(Start + 500);
        return (clock, Start + 500);
    }

    /// <summary>Drives a clock into the countdown: absent samples one second apart from the end of the warm-up.</summary>
    private static (AwayPresenceClock Clock, long Now) Counting()
    {
        var (clock, ready) = Watching();
        var now = ready + AwayPresenceClock.WarmupMs;
        clock.Sample(now, Absent, LeaveMs);
        var first = now;
        AwayPresenceCommand command;
        do
        {
            now += 1000;
            command = clock.Sample(now, Absent, LeaveMs);
        }
        while (command == AwayPresenceCommand.None && now - first < 120_000);

        if (command != AwayPresenceCommand.BeginCountdown)
        {
            throw new InvalidOperationException("setup: no countdown");
        }

        return (clock, now);
    }

    public static void Clock()
    {
        AwayPresenceClock.ArmIdleMs = 30_000;

        var armed = new AwayPresenceClock();
        Program.Check(armed.Tick(Start, 29_000, true, false, LeaveMs) == AwayPresenceCommand.None && armed.Phase == AwayPresencePhase.Armed, "idle 29 s: no camera");
        Program.Check(armed.Tick(Start + 1000, 30_000, true, false, LeaveMs) == AwayPresenceCommand.StartCamera && armed.Phase == AwayPresencePhase.Watching, "idle 30 s: StartCamera");

        {
            var (clock, ready) = Watching();
            var t = ready + 1000;
            Program.Check(clock.Sample(t, Absent, LeaveMs) == AwayPresenceCommand.None, "sample inside the warm-up is ignored");
            var a = ready + AwayPresenceClock.WarmupMs;
            clock.Sample(a, Absent, LeaveMs);
            Program.Check(clock.Sample(ready + 61_000, Absent, LeaveMs) == AwayPresenceCommand.None, "the warm-up sample did not start the absence");
            Program.Check(clock.Sample(ready + 62_000, Absent, LeaveMs) == AwayPresenceCommand.BeginCountdown, "60 s of absent samples after the warm-up: BeginCountdown");
        }

        {
            var (clock, ready) = Watching();
            var a = ready + AwayPresenceClock.WarmupMs;
            clock.Sample(a, Absent, LeaveMs);
            Program.Check(clock.Sample(a + 59_000, Absent, LeaveMs) == AwayPresenceCommand.None, "59 s of absence: no countdown");
            Program.Check(clock.Sample(a + 60_000, Absent, LeaveMs) == AwayPresenceCommand.BeginCountdown && clock.Phase == AwayPresencePhase.Countdown, "60 s of absence: BeginCountdown");
        }

        {
            var (clock, ready) = Watching();
            var a = ready + AwayPresenceClock.WarmupMs;
            clock.Sample(a, Absent, LeaveMs);
            clock.Sample(a + 30_000, Moving, LeaveMs);
            Program.Check(clock.Sample(a + 80_000, Absent, LeaveMs) == AwayPresenceCommand.None, "movement restarts the absence count");
        }

        {
            var (clock, ready) = Watching();
            var first = clock.Sample(ready + AwayPresenceClock.WarmupMs, new AwayObservation(false, false, true, false), LeaveMs);
            var late = clock.Sample(ready + 120_000, new AwayObservation(false, false, true, false), LeaveMs);
            Program.Check(first == AwayPresenceCommand.None && late == AwayPresenceCommand.None && clock.Phase == AwayPresencePhase.Watching, "samples without a comparison never count as absence");
        }

        {
            var (clock, now) = Counting();
            var command = clock.Sample(now + 1000, Face, LeaveMs);
            Program.Check(command == AwayPresenceCommand.CancelCountdown && clock.Phase == AwayPresencePhase.Watching, "a face during the countdown: CancelCountdown, back to Watching");
        }

        {
            var (clock, now) = Counting();
            var command = clock.Sample(now + 1000, Moving, LeaveMs);
            Program.Check(command == AwayPresenceCommand.CancelCountdown && clock.Phase == AwayPresencePhase.Watching, "motion during the countdown: CancelCountdown, back to Watching");
        }

        {
            var (clock, now) = Counting();
            Program.Check(clock.Sample(now + 1000, Absent, LeaveMs) == AwayPresenceCommand.None && clock.Phase == AwayPresencePhase.Countdown, "an empty frame during the countdown changes nothing");
        }

        {
            var (clock, now) = Counting();
            var command = clock.Tick(now + 1000, 2000, true, false, LeaveMs);
            Program.Check(command == (AwayPresenceCommand.StopCamera | AwayPresenceCommand.CancelCountdown) && clock.Phase == AwayPresencePhase.Armed, "input during the countdown: StopCamera | CancelCountdown");
        }

        {
            var (clock, now) = Counting();
            Program.Check(clock.CountdownEndsAt == now + AwayPresenceClock.CountdownMs, "the countdown lasts 10 s");
            Program.Check(clock.Tick(clock.CountdownEndsAt - 1, 90_000, true, false, LeaveMs) == AwayPresenceCommand.None, "one millisecond before the end: still counting");
            Program.Check(clock.Tick(clock.CountdownEndsAt, 90_000, true, false, LeaveMs) == AwayPresenceCommand.Leave && clock.Phase == AwayPresencePhase.Off, "countdown end: Leave");
        }

        {
            var (clock, ready) = Watching();
            var t = ready + AwayPresenceClock.WarmupMs;
            clock.Sample(t, Dark, LeaveMs);
            Program.Check(clock.Sample(t + 59_000, Dark, LeaveMs) == AwayPresenceCommand.None, "59 s of dark frames: still watching, never absence");
            Program.Check(clock.Phase == AwayPresencePhase.Watching, "dark frames never start a countdown");
            Program.Check(clock.Sample(t + 60_000, Dark, LeaveMs) == AwayPresenceCommand.StopCamera && clock.Phase == AwayPresencePhase.GaveUp, "unusable for 60 s: StopCamera, GaveUp");
            Program.Check(clock.Tick(t + 61_000, 120_000, true, false, LeaveMs) == AwayPresenceCommand.None && clock.Phase == AwayPresencePhase.GaveUp, "a tick without input stays in GaveUp");
            Program.Check(clock.Tick(t + 62_000, 1000, true, false, LeaveMs) == AwayPresenceCommand.None && clock.Phase == AwayPresencePhase.Armed, "input clears GaveUp");
        }

        {
            var (clock, ready) = Watching();
            var t = ready + AwayPresenceClock.WarmupMs;
            clock.Sample(t, Dark, LeaveMs);
            clock.Sample(t + 30_000, Absent, LeaveMs);
            Program.Check(clock.Sample(t + 80_000, Dark, LeaveMs) == AwayPresenceCommand.None, "a usable frame restarts the dark count");
        }

        {
            var (clock, ready) = Watching();
            var command = clock.Tick(ready + 5000, 40_000, true, true, LeaveMs);
            Program.Check(command == AwayPresenceCommand.StopCamera && clock.Phase == AwayPresencePhase.Armed, "suppression: StopCamera");
            Program.Check(clock.Tick(ready + 5000 + 29_000, 70_000, true, false, LeaveMs) == AwayPresenceCommand.None, "no restart 29 s after the suppression");
            Program.Check(clock.Tick(ready + 5000 + 30_000, 71_000, true, false, LeaveMs) == AwayPresenceCommand.StartCamera, "restart after the 30 s cooldown");
        }

        {
            var (clock, now) = Counting();
            var command = clock.Tick(now + 1000, 50_000, true, true, LeaveMs);
            Program.Check(command == (AwayPresenceCommand.StopCamera | AwayPresenceCommand.CancelCountdown), "suppression during the countdown: stop and cancel");
        }

        {
            var (clock, _) = Watching();
            var command = clock.Tick(Start + 10_000, 40_000, false, false, LeaveMs);
            Program.Check(command == AwayPresenceCommand.StopCamera && clock.Phase == AwayPresencePhase.Off, "not allowed: StopCamera and Off");
            Program.Check(clock.Tick(Start + 11_000, 41_000, false, false, LeaveMs) == AwayPresenceCommand.None, "not allowed while off: nothing");
        }

        {
            var (clock, ready) = Watching();
            var t = ready + AwayPresenceClock.WarmupMs;
            clock.Sample(t, Face, LeaveMs);
            Program.Check(clock.IntervalMs == 3000, "a face sample sets the interval to 3000");
            clock.Sample(t + 3000, Absent, LeaveMs);
            Program.Check(clock.IntervalMs == 1000, "an absent sample sets the interval to 1000");
        }

        {
            var clock = new AwayPresenceClock();
            var command = clock.CameraFailed();
            Program.Check(command == AwayPresenceCommand.StopCamera && clock.Phase == AwayPresencePhase.GaveUp, "CameraFailed: GaveUp and StopCamera");
        }

        {
            var (clock, now) = Counting();
            clock.Reset();
            Program.Check(clock.Phase == AwayPresencePhase.Off && clock.Sample(now + 1000, Absent, LeaveMs) == AwayPresenceCommand.None, "Reset: Off, samples ignored");
        }

        {
            // The developer override shortens the arm time.
            AwayPresenceClock.ArmIdleMs = 5000;
            var clock = new AwayPresenceClock();
            Program.Check(clock.Tick(Start, 5000, true, false, LeaveMs) == AwayPresenceCommand.StartCamera, "ArmIdleMs override: the camera starts after 5 s");
            AwayPresenceClock.ArmIdleMs = 30_000;
        }
    }
}
