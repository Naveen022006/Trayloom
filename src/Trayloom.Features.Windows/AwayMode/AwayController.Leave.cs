// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.WindowTools.AwayMode;

/// <summary>
/// The camera as a leaving trigger (spec 08 section 1): while the person is present and has not touched the mouse or keyboard for a
/// while, the camera looks for a face or movement; when nobody is there for long enough, a short notice runs and then Away mode starts.
/// The decisions are made by <see cref="AwayPresenceClock"/>; this part only feeds it and carries out what it says.
/// </summary>
internal sealed partial class AwayController
{
    /// <summary>While the camera is looking, the controller ticks once a second so the clock sees input and the countdown end in time.</summary>
    private const int WatchTickMs = 1000;

    /// <summary>The rate of the face check for the return, which the camera is handed over to when the windows are minimized.</summary>
    private const int ReturnFaceIntervalMs = 3000;

    private readonly AwayPresenceClock _clock = new();
    private bool _presenceCamera;
    private int _appliedInterval = AwayPresenceClock.FastIntervalMs;
    private string? _pendingNotice;

    /// <summary>The island context of the notice and live strips, captured when the island first asks for them.</summary>
    public IIslandContext? IslandContext { get; set; }

    /// <summary>The notice text waiting for the island, once; null when there is none.</summary>
    public string? TakeNotice()
    {
        var text = _pendingNotice;
        _pendingNotice = null;
        return text;
    }

    public AwayPresencePhase PresencePhase => _clock.Phase;

    /// <summary>Whole seconds until Away mode starts, rounded up, while the countdown runs.</summary>
    public int CountdownSecondsLeft => (int)Math.Clamp((_clock.CountdownEndsAt - Environment.TickCount64 + 999) / 1000, 0, AwayPresenceClock.CountdownMs / 1000);

    /// <summary>The camera is on only to check that the person is at the PC.</summary>
    public bool PresenceLooking => State == AwayState.Present && _presenceCamera && _clock.Phase == AwayPresencePhase.Watching;

    /// <summary>True when the apps are closed as soon as the person is away, so the notice says "Closing" instead of "Minimizing".</summary>
    public bool CountdownCloses => CloseWanted && _app.Get<int>(AwayKeys.CloseAfterMinutes) == 0;

    private long LeaveAfterMs => _app.Get<int>(AwayKeys.LeaveCameraSeconds) * 1000L;

    // ------------------------------------------------------------------------------------------ ticks

    /// <summary>Runs before <c>PresentTick</c> while the person is present: asks the clock what to do and does it.</summary>
    private void PresenceTick()
    {
        var allowed = PresenceAllowed();
        var idle = AwayNative.IdleMilliseconds();
        var suppressed = allowed && idle >= AwayPresenceClock.ArmIdleMs
            && (_presence.Current(ownCameraOpen: _presenceCamera) != AwaySuppression.None
                || _presence.OtherAppUsesCamera()
                || AwayPresence.EnergySaverOn());

        Apply(_clock.Tick(Environment.TickCount64, idle, allowed, suppressed, LeaveAfterMs));

        if (State != AwayState.Present)
        {
            return;
        }

        if (_clock.Phase == AwayPresencePhase.Countdown)
        {
            // The settings card and the tray card show the seconds left.
            Changed?.Invoke();
        }

        var wanted = _clock.Phase is AwayPresencePhase.Watching or AwayPresencePhase.Countdown ? WatchTickMs : PresentTickMs;
        if (_timer.Interval.TotalMilliseconds != wanted)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(wanted);
        }
    }

    /// <summary>Whether the camera may be used at all right now: the switches, a local session, a camera that works.</summary>
    private bool PresenceAllowed()
    {
        if (!_app.Get<bool>(AwayKeys.Enabled) || !_app.Get<bool>(AwayKeys.LeaveCamera))
        {
            return false;
        }

        if (State != AwayState.Present || _locked || _suspended || _stopping || System.Windows.Forms.SystemInformation.TerminalServerSession)
        {
            return false;
        }

        // The developer filter keeps tests away from the person's windows; only the camera-less test mode may run under it.
        if (AwayDevFilter.Load() is not null && !AwayFaceWatcher.NoCamera)
        {
            return false;
        }

        return AwayFaceWatcher.NoCamera || (AwayFaceWatcher.IsSupported && !AwayFaceWatcher.AccessDenied);
    }

    /// <summary>One camera sample (already marshalled to the UI thread).</summary>
    private void OnObserved(AwayObservation observation)
    {
        if (State != AwayState.Present || !_presenceCamera || _stopping)
        {
            return;
        }

        Apply(_clock.Sample(Environment.TickCount64, observation, LeaveAfterMs));
        if (_clock.IntervalMs != _appliedInterval)
        {
            _appliedInterval = _clock.IntervalMs;
            _watcher.SetInterval(_appliedInterval);
        }
    }

    /// <summary>Developer action: feeds one observation as if the camera had produced it (works with <c>TRAYLOOM_AWAY_NO_CAMERA=1</c>).</summary>
    public void SimulateObservation(AwayObservation observation)
    {
        if (State != AwayState.Present || !_presenceCamera)
        {
            _log.Info("[away] simulated sample ignored: the camera is not watching");
            return;
        }

        _watcher.FeedObservation(observation);
    }

    private void Apply(AwayPresenceCommand command)
    {
        if (command == AwayPresenceCommand.None)
        {
            return;
        }

        if (command.HasFlag(AwayPresenceCommand.StopCamera))
        {
            StopPresenceCamera(_clock.LastReason);
        }

        if (command.HasFlag(AwayPresenceCommand.CancelCountdown))
        {
            CancelCountdown();
        }

        if (command.HasFlag(AwayPresenceCommand.StartCamera))
        {
            StartPresenceCamera();
        }

        if (command.HasFlag(AwayPresenceCommand.BeginCountdown))
        {
            BeginCountdown();
        }

        if (command.HasFlag(AwayPresenceCommand.Leave))
        {
            LeaveByCamera();
        }
    }

    // ------------------------------------------------------------------------------------------ camera

    private void StartPresenceCamera()
    {
        _presenceCamera = true;
        _watcher.MeasureMotion = true;
        _appliedInterval = _clock.IntervalMs;
        _watcher.SetInterval(_appliedInterval);
        AwayGuard.FireAndLog(async () =>
        {
            var failure = await _watcher.StartAsync(_app.Get<string>(AwayKeys.CameraId) ?? string.Empty);
            if (failure is not null)
            {
                _log.Info("[away] camera: " + failure);
                _app.Set(AwayKeys.CameraFailed, true);
                if (_presenceCamera)
                {
                    Apply(_clock.CameraFailed());
                }

                Changed?.Invoke();
                return;
            }

            _app.Set(AwayKeys.CameraFailed, false);
            if (!_presenceCamera)
            {
                // The watch was cancelled while the camera was opening (input, a lock, a call): do not leave the camera on.
                if (!_faceWanted)
                {
                    await Task.Run(() => _watcher.Stop("input"));
                }

                return;
            }

            _clock.CameraStarted(Environment.TickCount64);
            _log.Info($"[away] camera: watching (idle {AwayPresenceClock.ArmIdleMs / 1000} s)");
        }, _log, "presence camera start");
    }

    private void StopPresenceCamera(string reason)
    {
        if (!_presenceCamera)
        {
            return;
        }

        _presenceCamera = false;
        _watcher.MeasureMotion = false;
        Task.Run(() => _watcher.Stop(reason.Length == 0 ? "input" : reason));
    }

    /// <summary>
    /// The windows were minimized while the presence watch had the camera: either it keeps running for the return (a face brings the
    /// windows back) or it goes off, because with nothing to look for the light should not stay on.
    /// </summary>
    private void HandOverCamera(bool faceReturn)
    {
        _presenceCamera = false;
        _watcher.MeasureMotion = false;
        if (faceReturn)
        {
            _faceWanted = true;
            _faceStarted = Environment.TickCount64;
            _watcher.SetInterval(ReturnFaceIntervalMs);
            return;
        }

        Task.Run(() => _watcher.Stop("away"));
    }

    // ------------------------------------------------------------------------------------------ countdown and leaving

    private void BeginCountdown()
    {
        var islandShowing = _ctx.Runtime.IsRunning("notch") && _app.Get<bool>("notchEnabled") && IslandContext is not null;
        if (islandShowing)
        {
            _pendingNotice = CountdownCloses ? AwayStrings.NoticeClosing : AwayStrings.NoticeMinimizing;
            IslandContext!.Invalidate();
        }
        else
        {
            // Without the island there is nowhere else to say it, so a toast does.
            _ctx.Notifier.Show(AwayStrings.Title, CountdownCloses ? AwayStrings.ToastClosing : AwayStrings.ToastMinimizing);
        }

        Changed?.Invoke();
        _log.Info("[away] leave: countdown");
    }

    private void CancelCountdown()
    {
        _pendingNotice = null;
        IslandContext?.Invalidate();
        Changed?.Invoke();
        _log.Info($"[away] leave: cancelled ({_clock.LastReason})");
    }

    private void LeaveByCamera()
    {
        if (!GoAway("camera"))
        {
            _clock.Reset();
            StopPresenceCamera("away");
        }
    }

    // ------------------------------------------------------------------------------------------ which apps close

    /// <summary>The apps the close job works on, as lower-cased exe paths: the chosen list, plus the active mode's apps, or every app.</summary>
    private List<string> CloseListFor()
    {
        var list = _app.Get<List<string>>(AwayKeys.CloseList);
        switch (_app.Get<string>(AwayKeys.CloseScope))
        {
            case "all":
                return [.. _records
                    .Select(r => AppIdentity.PathOf(r.Pid))
                    .Where(p => p is not null && !AwayApps.NeverClosable(p))
                    .Select(p => p!.ToLowerInvariant())
                    .Distinct()];
            case "mode":
                var extra = _ctx.Services.Get<IAwayWorkspace>()?.ActiveAppsToClose();
                if (extra is null || extra.Count == 0)
                {
                    return [.. list];
                }

                return [.. list.Concat(extra).Distinct(StringComparer.OrdinalIgnoreCase)];
            default:
                return [.. list];
        }
    }
}
