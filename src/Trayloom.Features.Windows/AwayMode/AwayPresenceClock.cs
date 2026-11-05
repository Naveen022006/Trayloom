// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace Trayloom.Features.WindowTools.AwayMode;

/// <summary>Where the camera-based leaving check is.</summary>
internal enum AwayPresencePhase
{
    /// <summary>Nothing runs (the feature is off or the PC is busy with something else).</summary>
    Off,

    /// <summary>On, waiting for the person to stop using the mouse and keyboard.</summary>
    Armed,

    /// <summary>The camera is on and looking for someone.</summary>
    Watching,

    /// <summary>Nobody was seen for long enough; the notice is showing and movement cancels it.</summary>
    Countdown,

    /// <summary>The camera could not help (dark lens, failure): it stays off until the next input.</summary>
    GaveUp,
}

/// <summary>What the controller has to do after a tick or a sample. More than one flag can be set.</summary>
[Flags]
internal enum AwayPresenceCommand
{
    None = 0,
    StartCamera = 1,
    StopCamera = 2,
    BeginCountdown = 4,
    CancelCountdown = 8,
    Leave = 16,
}

/// <summary>
/// The pure state machine of "leave when the camera doesn't see you": it decides when the camera may run, when the person counts as
/// gone, when the notice starts and when Away mode begins. It never touches a camera, a timer or a window, so the harness can drive
/// it with made-up times. All times are in milliseconds on one monotonic clock.
/// </summary>
internal sealed class AwayPresenceClock
{
    /// <summary>The camera starts only after this long without input (a developer can shorten it, see <c>TRAYLOOM_AWAY_ARM_SECONDS</c>).</summary>
    public static long ArmIdleMs = 30_000;

    public const long WarmupMs = 2_000;
    public const long CountdownMs = 10_000;
    public const long UnusableGiveUpMs = 60_000;
    public const long SuppressedCooldownMs = 30_000;
    public const int FastIntervalMs = 1000;
    public const int RelaxedIntervalMs = 3000;

    private long? _absentSince;
    private long? _unusableSince;
    private long _cooldownUntil;
    private long _cameraStartedAt;
    private bool _cameraReady;

    public AwayPresencePhase Phase { get; private set; }

    public long CountdownEndsAt { get; private set; }

    public int IntervalMs { get; private set; } = FastIntervalMs;

    /// <summary>Why the last command happened: input, face, motion, suppressed, dark, failed or disabled. For the log only.</summary>
    public string LastReason { get; private set; } = string.Empty;

    /// <summary>
    /// The slow tick (once a second while looking, every few seconds otherwise). <paramref name="allowed"/> is the feature switch plus
    /// the hard conditions, <paramref name="suppressed"/> is a call, media, full screen or battery saver.
    /// </summary>
    public AwayPresenceCommand Tick(long now, long idleMs, bool allowed, bool suppressed, long leaveAfterMs)
    {
        if (!allowed)
        {
            var off = Phase switch
            {
                AwayPresencePhase.Watching => AwayPresenceCommand.StopCamera,
                AwayPresencePhase.Countdown => AwayPresenceCommand.StopCamera | AwayPresenceCommand.CancelCountdown,
                _ => AwayPresenceCommand.None,
            };
            if (off != AwayPresenceCommand.None)
            {
                LastReason = "disabled";
            }

            ClearRun();
            Phase = AwayPresencePhase.Off;
            return off;
        }

        if (Phase == AwayPresencePhase.Off)
        {
            Phase = AwayPresencePhase.Armed;
        }

        // Input is the only thing that clears a give-up and the first thing that ends a look or a countdown.
        if (idleMs < ArmIdleMs)
        {
            var input = Phase switch
            {
                AwayPresencePhase.Watching => AwayPresenceCommand.StopCamera,
                AwayPresencePhase.Countdown => AwayPresenceCommand.StopCamera | AwayPresenceCommand.CancelCountdown,
                _ => AwayPresenceCommand.None,
            };
            if (input != AwayPresenceCommand.None)
            {
                LastReason = "input";
            }

            ClearRun();
            Phase = AwayPresencePhase.Armed;
            return input;
        }

        if (suppressed)
        {
            var stop = Phase switch
            {
                AwayPresencePhase.Watching => AwayPresenceCommand.StopCamera,
                AwayPresencePhase.Countdown => AwayPresenceCommand.StopCamera | AwayPresenceCommand.CancelCountdown,
                _ => AwayPresenceCommand.None,
            };
            if (stop != AwayPresenceCommand.None)
            {
                LastReason = "suppressed";
            }

            _cooldownUntil = now + SuppressedCooldownMs;
            if (Phase != AwayPresencePhase.GaveUp)
            {
                ClearRun();
                Phase = AwayPresencePhase.Armed;
            }

            return stop;
        }

        if (Phase == AwayPresencePhase.Armed && now >= _cooldownUntil)
        {
            ClearRun();
            Phase = AwayPresencePhase.Watching;
            IntervalMs = FastIntervalMs;
            return AwayPresenceCommand.StartCamera;
        }

        if (Phase == AwayPresencePhase.Countdown && now >= CountdownEndsAt)
        {
            Phase = AwayPresencePhase.Off;
            return AwayPresenceCommand.Leave;
        }

        return AwayPresenceCommand.None;
    }

    /// <summary>One camera sample. Ignored unless the camera is looking, and during the first moments after it started.</summary>
    public AwayPresenceCommand Sample(long now, AwayObservation o, long leaveAfterMs)
    {
        if (Phase is not (AwayPresencePhase.Watching or AwayPresencePhase.Countdown))
        {
            return AwayPresenceCommand.None;
        }

        // Frames from an opening camera are often black or half exposed.
        if (!_cameraReady || now - _cameraStartedAt < WarmupMs)
        {
            return AwayPresenceCommand.None;
        }

        var present = o.Face || (o.MotionKnown && o.Motion);
        if (Phase == AwayPresencePhase.Countdown)
        {
            if (!present)
            {
                return AwayPresenceCommand.None;
            }

            Phase = AwayPresencePhase.Watching;
            _absentSince = null;
            IntervalMs = FastIntervalMs;
            LastReason = o.Face ? "face" : "motion";
            return AwayPresenceCommand.CancelCountdown;
        }

        // A face is proof enough even in a dim picture; without one, a dark or flat frame says nothing about the chair.
        if (!present && !o.Usable)
        {
            _absentSince = null;
            _unusableSince ??= now;
            if (now - _unusableSince.Value >= UnusableGiveUpMs)
            {
                Phase = AwayPresencePhase.GaveUp;
                LastReason = "dark";
                return AwayPresenceCommand.StopCamera;
            }

            return AwayPresenceCommand.None;
        }

        // The first frame has nothing to compare with: it can show a face, but it cannot show that nobody is there.
        if (!present && !o.MotionKnown)
        {
            return AwayPresenceCommand.None;
        }

        _unusableSince = null;
        if (present)
        {
            _absentSince = null;
            IntervalMs = o.Face ? RelaxedIntervalMs : FastIntervalMs;
            return AwayPresenceCommand.None;
        }

        IntervalMs = FastIntervalMs;
        _absentSince ??= now;
        if (now - _absentSince.Value >= leaveAfterMs)
        {
            Phase = AwayPresencePhase.Countdown;
            CountdownEndsAt = now + CountdownMs;
            return AwayPresenceCommand.BeginCountdown;
        }

        return AwayPresenceCommand.None;
    }

    /// <summary>The camera delivered its first usable state; the warm-up counts from here.</summary>
    public void CameraStarted(long now)
    {
        _cameraStartedAt = now;
        _cameraReady = true;
    }

    /// <summary>The camera could not start or died: stay off until the next input.</summary>
    public AwayPresenceCommand CameraFailed()
    {
        ClearRun();
        Phase = AwayPresencePhase.GaveUp;
        LastReason = "failed";
        return AwayPresenceCommand.StopCamera;
    }

    /// <summary>Back to Off (the person returned, the PC locked or slept, the feature stopped).</summary>
    public void Reset()
    {
        ClearRun();
        _cooldownUntil = 0;
        Phase = AwayPresencePhase.Off;
        IntervalMs = FastIntervalMs;
    }

    private void ClearRun()
    {
        _absentSince = null;
        _unusableSince = null;
        _cameraReady = false;
    }
}
