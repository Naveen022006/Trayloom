// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;
using System.Windows.Threading;
using Microsoft.Win32;
using Trayloom.Core;
using Trayloom.Core.Native;

namespace Trayloom.Features.WindowTools.AwayMode;

/// <summary>The states of spec 06 section 4.3.</summary>
internal enum AwayState
{
    Present,
    GoingAway,
    Away,
    Returning,
}

/// <summary>
/// The Away mode state machine: the idle check, the away pass (snapshot, minimize), the return triggers (face, unlock, input, the
/// person restoring a window, manual) and the restore. Everything here runs on the UI thread except the restore itself, which runs on
/// its own background thread so a hung app can never freeze Trayloom.
/// </summary>
internal sealed partial class AwayController
{
    private const int PresentTickMs = 5000;
    private const int AwayTickMs = 250;
    private const int SweepMs = 5000;
    private const int GraceMs = 2000;
    private const int QuickLookMs = 10000;

    private readonly IFeatureContext _ctx;
    private readonly IAppSettings _app;
    private readonly ILog _log;
    private readonly AwaySession _session;
    private readonly AwayClosedStore _store;
    private readonly AwayPresence _presence;
    private readonly AwayFaceWatcher _watcher;
    private readonly string _directory;
    private readonly List<AwayCoverWindow> _covers = [];
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _verify;
    private readonly long _startedAt;
    private List<AwayRecord> _records = [];
    private Thread? _restoreThread;
    private nint _active;
    private long _awayTick;
    private uint _awayTick32;
    private DateTime _awaySince;
    private string _trigger = string.Empty;
    private string _awayLabel = string.Empty;
    private bool _simulated;
    private bool _returnPending;
    private string _pendingTrigger = "input";
    private bool _locked;
    private bool _suspended;
    private bool _stopping;
    private bool _faceWanted;
    private int _hits;
    private long _quickUntil;
    private long _lastSweep;
    private long _presentSince;
    private long _faceStarted;
    private uint _consumedInput;
    private AwaySuppression _suppression;
    private AwayCloseJob? _closeJob;
    private AwayReopenJob? _reopenJob;
    private ClosedAppsFile? _recovery;
    private bool _recoveryFailedAtReturn;
    private bool _closeStarted;
    private bool _closeCoverShown;
    private bool _sessionEnding;
    private List<(string Name, AwayKeepReason Reason)> _lastResult = [];
    private readonly bool _elevated = AppIdentity.IsAdmin();

    public AwayController(IFeatureContext ctx, string directory)
    {
        _ctx = ctx;
        _app = ctx.App;
        _log = ctx.Log;
        _directory = directory;
        _session = new AwaySession(directory, ctx.Log);
        _store = new AwayClosedStore(directory, ctx.Log);
        _presence = new AwayPresence(ctx);
        _watcher = new AwayFaceWatcher(ctx.Log);
        _timer = new DispatcherTimer(DispatcherPriority.Background, ctx.Ui) { Interval = TimeSpan.FromMilliseconds(PresentTickMs) };
        _timer.Tick += (_, _) => Safe(Tick);
        _verify = new DispatcherTimer(DispatcherPriority.Background, ctx.Ui) { Interval = TimeSpan.FromMilliseconds(1000) };
        _verify.Tick += (_, _) => Safe(Verify);

        // The person counts as present from the start: a PC that was already idle before a restart needs a fresh idle period first.
        _startedAt = Environment.TickCount64;
        _presentSince = _startedAt;
    }

    /// <summary>Raised on the UI thread when the state, the window count or the waiting reason changed.</summary>
    public event Action? Changed;

    public AwayState State { get; private set; } = AwayState.Present;

    /// <summary>Local time the person left (meaningful while away).</summary>
    public DateTime Since => _awaySince;

    /// <summary>Windows currently recorded.</summary>
    public int WindowCount => _records.Count;

    public AwaySuppression Suppression => _suppression;

    /// <summary>True when Trayloom runs as administrator: closing is unavailable then (the apps would open again elevated).</summary>
    public bool Elevated => _elevated;

    /// <summary>Apps closed in the current away period (and not yet opened again).</summary>
    public int ClosedAppCount => _closeJob?.ClosedCount ?? 0;

    public AwayCloseJobState CloseJobState
    {
        get
        {
            if (_closeJob is { } job)
            {
                return job.State;
            }

            return State == AwayState.Away && !_closeStarted && CloseWanted ? AwayCloseJobState.Waiting : AwayCloseJobState.None;
        }
    }

    /// <summary>Local time closing starts when it waits for a delay.</summary>
    public DateTime CloseAt => _awaySince.AddMinutes(_app.Get<int>(AwayKeys.CloseAfterMinutes));

    /// <summary>Apps being opened again right now (0 when none).</summary>
    public int ReopeningCount => _reopenJob is { Running: true } job ? job.Count : 0;

    /// <summary>Apps waiting to be opened again after a restart, a crash or a failed return.</summary>
    public int RecoveryCount => _recovery?.Apps.Count ?? 0;

    /// <summary>True when the apps in <see cref="RecoveryCount"/> did not open when the person came back (not a restart).</summary>
    public bool RecoveryFailedAtReturn => _recoveryFailedAtReturn;

    public DateTime RecoverySince => _recovery?.Since.ToLocalTime() ?? default;

    public IReadOnlyList<string> RecoveryNames => _recovery?.Apps.Select(a => a.Name).Distinct().OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList() ?? [];

    /// <summary>Apps that stayed open or did not open again after the last return, with the reason (names only, in memory).</summary>
    public IReadOnlyList<(string Name, AwayKeepReason Reason)> LastResult => _lastResult;

    private bool CloseWanted => !_elevated && _app.Get<bool>(AwayKeys.CloseApps)
        && (_app.Get<string>(AwayKeys.CloseScope) == "all" || CloseListFor().Count > 0);

    /// <summary>True from the first minimize until the restore finished.</summary>
    public bool IsAway => State is AwayState.GoingAway or AwayState.Away;

    /// <summary>The camera is on and looking.</summary>
    public bool Looking => _watcher.IsRunning;

    public void Start()
    {
        _app.Changed += OnSettingChanged;
        SystemEvents.SessionSwitch += OnSession;
        SystemEvents.PowerModeChanged += OnPower;
        SystemEvents.SessionEnding += OnSessionEnding;
        _watcher.Sample += positive => _ctx.Ui.BeginInvoke(() => Safe(() => OnSample(positive)));
        _watcher.Failed += reason => _ctx.Ui.BeginInvoke(() => Safe(() => OnCameraFailed(reason)));
        _watcher.Observed += o => _ctx.Ui.BeginInvoke(() => Safe(() => OnObserved(o)));
        AwayGuard.FireAndLog(_presence.InitializeAsync, _log, "media sessions");
        _timer.Start();
    }

    /// <summary>Undoes everything: timers, events, camera, covers, and (bounded to 3 s here) the windows.</summary>
    public void Stop()
    {
        _stopping = true;
        ForgetPresence();
        _timer.Stop();
        _verify.Stop();
        _app.Changed -= OnSettingChanged;
        SystemEvents.SessionSwitch -= OnSession;
        SystemEvents.PowerModeChanged -= OnPower;
        SystemEvents.SessionEnding -= OnSessionEnding;

        try
        {
            if (State == AwayState.GoingAway)
            {
                State = AwayState.Away;
            }

            if (State == AwayState.Away)
            {
                Return("stop");
            }
            else
            {
                _closeJob?.Cancel();
            }

            _restoreThread?.Join(3000);
        }
        finally
        {
            _watcher.Dispose();
            CloseCovers();
        }
    }

    // ------------------------------------------------------------------------------------------ going away

    /// <summary>"Go away now" (and the idle trigger). Returns false when it does not apply right now.</summary>
    public bool GoAway(string trigger, bool simulated = false)
    {
        if (State != AwayState.Present || _stopping)
        {
            return false;
        }

        if (trigger is "idle" or "camera" && !AwayLeaveGuard.MayLeave(Environment.TickCount64, _startedAt, _presentSince))
        {
            return false;
        }

        var only = AwayDevFilter.Load();
        var ignored = _app.Get<List<string>>("awayModeIgnoredApps").Select(p => p.ToLowerInvariant()).ToHashSet();
        _records = AwayWindows.Snapshot(ignored, only, out _active);
        _trigger = trigger;
        _simulated = simulated;
        _awayLabel = trigger == "idle" ? $"idle {_app.Get<int>("awayModeIdleMinutes")} min"
            : trigger == "camera" ? $"camera {_app.Get<int>(AwayKeys.LeaveCameraSeconds)} s"
            : simulated ? "simulated" : "manual";
        _awaySince = DateTime.Now;
        _awayTick = Environment.TickCount64;
        _awayTick32 = (uint)Environment.TickCount;
        _consumedInput = AwayNative.LastInputTick();
        _returnPending = false;
        _hits = 0;
        _closeJob = null;
        _closeStarted = false;
        _closeCoverShown = false;
        State = AwayState.GoingAway;
        _faceWanted = false;

        // The record is on disk before any window is touched, so a crash from here on still leaves a way back.
        WriteSession();
        AwayWindows.MinimizeAll(_records);
        _verify.Interval = TimeSpan.FromMilliseconds(1000);
        _verify.Start();
        _timer.Interval = TimeSpan.FromMilliseconds(AwayTickMs);
        _lastSweep = Environment.TickCount64;
        Changed?.Invoke();
        return true;
    }

    /// <summary>One second after the minimize pass: drops windows that refused, then lock / cover / camera.</summary>
    private void Verify()
    {
        _verify.Stop();
        if (State == AwayState.Away)
        {
            // a sweep verification
            if (AwayWindows.DropNotMinimized(_records, 900) > 0)
            {
                WriteSession();
                Changed?.Invoke();
            }

            return;
        }

        if (State != AwayState.GoingAway)
        {
            return;
        }

        var before = _records.Count;
        AwayWindows.DropNotMinimized(_records, 900);
        var skipped = before - _records.Count;
        WriteSession();

        var devRestricted = AwayDevFilter.Load() is not null;
        var lockPc = _app.Get<bool>("awayModeLockPc") && !devRestricted;
        var cover = !lockPc && _app.Get<bool>("awayModeCover");
        var face = !lockPc && _app.Get<bool>("awayModeFace");
        State = AwayState.Away;
        _log.Info($"[away] away ({_awayLabel}): {_records.Count} windows minimized, {skipped} skipped, lock={(lockPc ? "yes" : "no")}, cover={(cover ? "yes" : "no")}, face={(face ? "yes" : "no")}");

        if (_returnPending)
        {
            Return(_pendingTrigger);
            return;
        }

        if (lockPc)
        {
            // With the PC locked the person comes back by unlocking: the presence watch must not keep the camera light on.
            if (_presenceCamera)
            {
                HandOverCamera(false);
            }

            AwayNative.Lock();
        }
        else
        {
            if (_presenceCamera)
            {
                // The presence watch has the camera open, or is still opening it: hand it over for the return, or switch it off. A second
                // start next to an opening one would open the camera twice and leave one of them on.
                HandOverCamera(face);
            }
            else if (face)
            {
                StartFace();
            }

            if (cover)
            {
                ShowCovers(face);
            }
        }

        Changed?.Invoke();
    }

    private void WriteSession()
    {
        _session.Write(new AwaySessionFile
        {
            OwnerPid = Environment.ProcessId,
            OwnerStart = AwaySession.CurrentProcessStart(),
            Since = _awaySince.ToUniversalTime(),
            Trigger = _trigger,
            Active = _active,
            Windows = _records,
        });
    }

    // ------------------------------------------------------------------------------------------ ticks

    private void Tick()
    {
        if (_stopping)
        {
            return;
        }

        switch (State)
        {
            case AwayState.Present:
                PresenceTick();
                PresentTick();
                break;
            case AwayState.Away:
                AwayTick();
                break;
        }
    }

    private void PresentTick()
    {
        var before = _suppression;
        _suppression = AwaySuppression.None;
        if (!_app.Get<bool>("awayModeEnabled") || _locked || _suspended)
        {
            if (before != _suppression)
            {
                Changed?.Invoke();
            }

            return;
        }

        var threshold = _app.Get<int>("awayModeIdleMinutes") * 60_000L;
        if (EffectiveIdleMs() >= threshold)
        {
            // Trayloom's own presence watch is listed in the consent store too; it must not look like a call, or the idle timer could
            // never fire while the camera is on.
            _suppression = _presence.Current(ownCameraOpen: _presenceCamera || _watcher.IsRunning);
            if (_suppression == AwaySuppression.None)
            {
                GoAway("idle");
            }
        }

        if (before != _suppression)
        {
            Changed?.Invoke();
        }
    }

    private void AwayTick()
    {
        var now = Environment.TickCount64;
        if (!_app.Get<bool>("awayModeEnabled"))
        {
            Return("disabled");
            return;
        }

        // Camera: off after the configured time away; input then brings the windows back.
        var stopMinutes = _app.Get<int>("awayModeFaceStopMinutes");
        if (_watcher.IsRunning && stopMinutes > 0 && now - _faceStarted > stopMinutes * 60_000L)
        {
            Task.Run(() => _watcher.Stop("timeout"));
        }

        if (_quickUntil != 0 && now > _quickUntil)
        {
            EndQuickLook();
        }

        MaybeStartClose(now, force: false);
        ShowCloseOnCover();

        if (now - _lastSweep >= SweepMs)
        {
            _lastSweep = now;
            Sweep();
        }

        if (_locked || _suspended || _simulated || now - _awayTick < GraceMs)
        {
            return;
        }

        // The person restored a recorded window themselves (taskbar click).
        if (AwayNative.IdleMilliseconds() < 3000 && _records.Any(r => now - r.MinimizedAt > 1500 && WinApi.IsWindow(r.Handle) && !WinApi.IsIconic(r.Handle)))
        {
            Return("restore-own");
            return;
        }

        var last = AwayNative.LastInputTick();
        var fresh = last != 0 && last != _consumedInput && unchecked((int)(last - (_awayTick32 + GraceMs))) > 0;
        if (!fresh || _quickUntil != 0)
        {
            return;
        }

        if (_watcher.IsRunning)
        {
            // With the camera looking, input alone does not bring the windows back (someone else may touch the mouse): look for a face.
            _consumedInput = last;
            _hits = 0;
            _quickUntil = now + QuickLookMs;
            _watcher.SetQuickLook(true);
        }
        else
        {
            Return("input");
        }
    }

    private void EndQuickLook()
    {
        _quickUntil = 0;
        _hits = 0;
        _watcher.SetQuickLook(false);
        _consumedInput = AwayNative.LastInputTick();
        foreach (var cover in _covers)
        {
            cover.ShowNotRecognised();
        }
    }

    private void Sweep()
    {
        var only = AwayDevFilter.Load();
        var ignored = _app.Get<List<string>>("awayModeIgnoredApps").Select(p => p.ToLowerInvariant()).ToHashSet();
        var added = AwayWindows.Sweep(_records, ignored, only);
        if (added > 0)
        {
            WriteSession();
            _verify.Interval = TimeSpan.FromMilliseconds(1000);
            _verify.Start();
            Changed?.Invoke();
        }
    }

    // ------------------------------------------------------------------------------------------ face

    private void StartFace()
    {
        _faceWanted = true;
        AwayGuard.FireAndLog(async () =>
        {
            if (_app.Get<bool>("awayModeSkipCalls") && _presence.OtherAppUsesCamera())
            {
                _log.Info("[away] camera: in use by another app, input brings the windows back");
                return;
            }

            var failure = await _watcher.StartAsync(_app.Get<string>("awayModeCameraId") ?? string.Empty);
            if (failure == AwayFaceWatcher.Cancelled)
            {
                // The person came back (or the PC locked) while the camera was opening; the camera is already off again.
                return;
            }

            if (failure is not null)
            {
                _log.Info("[away] camera: " + failure);
                _app.Set("awayModeCameraFailed", true);
                Changed?.Invoke();
                return;
            }

            _app.Set("awayModeCameraFailed", false);
            _faceStarted = Environment.TickCount64;
            if (State != AwayState.Away && State != AwayState.GoingAway)
            {
                _watcher.Stop("return");
            }
        }, _log, "camera start");
    }

    private void OnSample(bool positive)
    {
        if (State != AwayState.Away || !_faceWanted)
        {
            return;
        }

        _hits = positive ? _hits + 1 : 0;
        if (_hits >= 2)
        {
            Return("face");
        }
    }

    private void OnCameraFailed(string reason)
    {
        _log.Info("[away] camera: " + reason);
        _app.Set("awayModeCameraFailed", true);
        _quickUntil = 0;
        if (_presenceCamera)
        {
            Apply(_clock.CameraFailed());
        }

        Changed?.Invoke();
    }

    /// <summary>Developer action: one positive sample, as if the camera had seen a face (works without a camera).</summary>
    public void SimulateFace()
    {
        if (State != AwayState.Away || !_app.Get<bool>("awayModeFace"))
        {
            _log.Info("[away] simulate-face ignored: not away or face detection is off");
            return;
        }

        _faceWanted = true;
        _watcher.Feed(true);
    }

    // ------------------------------------------------------------------------------------------ covers

    private void ShowCovers(bool looking)
    {
        CloseCovers();
        var monitors = WindowPlacementMath.GetMonitors();
        for (var i = 0; i < monitors.Count; i++)
        {
            var cover = new AwayCoverWindow(monitors[i].Monitor, i == 0, _awaySince, looking, () => Return("manual"));
            _covers.Add(cover);
            cover.ShowCover();
        }
    }

    private void CloseCovers()
    {
        foreach (var cover in _covers)
        {
            try
            {
                cover.Close();
            }
            catch (Exception)
            {
                // already closed
            }
        }

        _covers.Clear();
    }

    // ------------------------------------------------------------------------------------------ returning

    /// <summary>The person is back (any trigger). During the minimize pass the return waits for the pass to finish.</summary>
    public void Return(string trigger)
    {
        if (State == AwayState.GoingAway)
        {
            _returnPending = true;
            _pendingTrigger = trigger;
            return;
        }

        if (State != AwayState.Away)
        {
            return;
        }

        State = AwayState.Returning;
        _clock.Reset();
        _quickUntil = 0;
        _faceWanted = false;
        CloseCovers();
        _verify.Stop();
        var records = _records.ToList();
        var active = _active;
        var since = _awayTick;
        _closeJob?.Cancel();
        var worker = new Thread(() => RestoreWorker(records, active, trigger, since)) { IsBackground = true, Name = "Away restore" };
        _restoreThread = worker;
        Changed?.Invoke();
        worker.Start();
    }

    private void RestoreWorker(List<AwayRecord> records, nint active, string trigger, long awayStart)
    {
        IReadOnlyDictionary<long, nint> replaced = new Dictionary<long, nint>();
        IReadOnlySet<string> reopened = new HashSet<string>();
        try
        {
            _watcher.Stop(trigger is "stop" ? "stop" : "return");

            // Apps that were asked to close get a few seconds to answer before the return goes on (spec 07 section 5.3).
            var job = _closeJob;
            job?.WaitFinished(3500);
            var closed = job?.ClosedApps() ?? [];
            var late = job?.LateCandidates() ?? [];
            var all = job?.AllApps() ?? [];
            var keepForNextStart = _sessionEnding && closed.Count > 0;
            AwayReopenJob? reopen = null;
            if (keepForNextStart)
            {
                _log.Info($"[away] session ending: {closed.Count} closed apps kept for the next start");
            }
            else if (job is not null && closed.Count + late.Count > 0)
            {
                var file = new ClosedAppsFile
                {
                    OwnerPid = Environment.ProcessId,
                    OwnerStart = AwaySession.CurrentProcessStart(),
                    Since = _awaySince.ToUniversalTime(),
                    ClosedAt = DateTime.UtcNow,
                    Apps = all,
                };
                reopen = new AwayReopenJob(_log, _store, file, closed, late);
                _reopenJob = reopen;
                _ctx.Ui.BeginInvoke(() => Safe(() => Changed?.Invoke()));
                reopen.Start();
            }
            else if (job is not null && all.Count > 0)
            {
                // Only apps that stayed open: nothing to open again, so nothing to remember.
                _store.Delete();
            }

            var immediate = reopen is { HasImmediate: true };
            var result = AwayWindows.Restore(records, active, focusActive: !immediate);
            if (result.Verified || !_stopping)
            {
                _session.Delete();
            }

            // With apps to open again the order and the focus are decided after they are back, so the verdict is read then.
            AwayReopenSummary? summary = null;
            var zOk = result.ZOrderOk;
            var focusOk = result.FocusOk;
            if (reopen is not null && !immediate)
            {
                // Only apps that may still be on their way out: they are opened if they exit within a few seconds, without holding up the return.
                AwayGuard.FireAndLog(() => Task.Run(() => reopen.Finish(null, 0)), _log, "late reopen");
            }
            else if (reopen is not null)
            {
                summary = reopen.Finish(records, active, out var stack);
                replaced = reopen.Replaced;
                reopened = closed.Select(a => a.Exe.ToLowerInvariant()).ToHashSet();
                var focusTarget = reopen.Replaced.TryGetValue(active, out var moved) ? moved : active;
                (zOk, focusOk) = AwayWindows.Check(stack, focusTarget);
            }

            var seconds = (Environment.TickCount64 - awayStart) / 1000;
            var after = seconds >= 60 ? $"{seconds / 60} min" : $"{seconds} s";
            _log.Info($"[away] back ({trigger}) after {after}: restored {result.Restored}/{result.Total}, late {result.Late}, closed {result.Closed}, z-order {(zOk ? "ok" : "differs")}, focus {(focusOk ? "ok" : "not given")}");

            RememberResult(all, reopen, summary);
        }
        catch (Exception ex)
        {
            _log.Error("[away] restore failed", ex);
        }
        finally
        {
            _ctx.Ui.BeginInvoke(() => Safe(() =>
            {
                State = AwayState.Present;
                _records = [];
                _presentSince = Environment.TickCount64;
                _timer.Interval = TimeSpan.FromMilliseconds(PresentTickMs);
                _reopenJob = null;
                _clock.Reset();
                if (!_stopping)
                {
                    _ctx.Services.Get<IAwayWorkspace>()?.OnAwayReturned(new AwayReturnInfo(replaced, reopened, trigger));
                }

                Changed?.Invoke();
            }));
        }
    }

    /// <summary>Keeps the "Last time you were away" lines and, for apps that did not open again, the recovery card and a notification.</summary>
    private void RememberResult(List<ClosedApp> all, AwayReopenJob? reopen, AwayReopenSummary? summary)
    {
        var lines = new List<(string, AwayKeepReason)>();
        foreach (var app in all)
        {
            if (app.State == AwayAppState.Kept)
            {
                lines.Add((app.Name, app.Reason));
            }
            else if (app.State == AwayAppState.Failed)
            {
                lines.Add((app.Name, AwayKeepReason.Failed));
            }
            else if (app.Reason == AwayKeepReason.Partial)
            {
                lines.Add((app.Name, AwayKeepReason.Partial));
            }
        }

        _lastResult = lines;
        if (summary is { Failed: > 0 } && reopen is not null)
        {
            var failed = all.Where(a => a.State == AwayAppState.Failed).ToList();
            _recovery = new ClosedAppsFile
            {
                OwnerPid = Environment.ProcessId,
                OwnerStart = AwaySession.CurrentProcessStart(),
                Since = _awaySince.ToUniversalTime(),
                ClosedAt = DateTime.UtcNow,
                Apps = failed,
            };
            _recoveryFailedAtReturn = true;
            _ctx.Notifier.Show(AwayStrings.Title, AwayStrings.NotificationFailed(failed.Count), NotificationLevel.Warning);
        }
    }

    // ------------------------------------------------------------------------------------------ closing apps

    /// <summary>Starts the close job once per away period, when the apps on the list are due (spec 07 section 5.1).</summary>
    private void MaybeStartClose(long now, bool force)
    {
        if (_closeStarted || !_app.Get<bool>(AwayKeys.CloseApps))
        {
            return;
        }

        var delay = _app.Get<int>(AwayKeys.CloseAfterMinutes) * 60_000L;
        if (!force && now - _awayTick < delay)
        {
            return;
        }

        var closeList = CloseListFor();
        if (closeList.Count == 0)
        {
            return;
        }

        _closeStarted = true;
        if (_elevated)
        {
            _log.Info("[away] close: skipped (elevated Trayloom)");
            return;
        }

        if (System.Windows.Forms.SystemInformation.TerminalServerSession)
        {
            _log.Info("[away] close: skipped (remote session)");
            return;
        }

        var filter = AwayDevFilter.Load();
        if (_simulated && filter is null)
        {
            _log.Info("[away] close: skipped (dev filter)");
            return;
        }

        var ignored = _app.Get<List<string>>(AwayKeys.IgnoredApps);
        var job = new AwayCloseJob(_log, _store, closeList, ignored, filter, _records, _active, _awaySince);
        job.Changed += () => _ctx.Ui.BeginInvoke(() => Safe(() =>
        {
            ShowCloseOnCover();
            Changed?.Invoke();
        }));
        _closeJob = job;
        job.Start();
        foreach (var cover in _covers)
        {
            cover.SetCloseStatus(AwayStrings.CoverClosing, null);
        }

        Changed?.Invoke();
    }

    /// <summary>Developer action <c>dev-close-now</c>: starts the close job now, whatever the delay (needs the dev filter).</summary>
    public void DevCloseNow()
    {
        if (State != AwayState.Away || AwayDevFilter.Load() is null)
        {
            _log.Info("[away] dev-close-now ignored: not away, or no away-mode-only.txt");
            return;
        }

        MaybeStartClose(Environment.TickCount64, force: true);
    }

    /// <summary>Shows how many apps were closed on the cover once the pass is over.</summary>
    private void ShowCloseOnCover()
    {
        if (_closeJob is not { State: AwayCloseJobState.Done } job || _closeCoverShown)
        {
            return;
        }

        _closeCoverShown = true;
        var closed = job.ClosedCount;
        var kept = job.KeptCount;
        foreach (var cover in _covers)
        {
            cover.SetCloseStatus(closed > 0 ? AwayStrings.CoverClosed(closed) : null, kept > 0 ? AwayStrings.CoverKept(kept) : null);
        }
    }

    private void OnSessionEnding(object sender, SessionEndingEventArgs e) => _sessionEnding = true;

    /// <summary>Developer action <c>simulate-session-end</c>: behaves as if the session were ending, without stopping the feature.</summary>
    public void SimulateSessionEnd()
    {
        if (State != AwayState.Away)
        {
            _log.Info("[away] simulate-session-end ignored: not away");
            return;
        }

        _sessionEnding = true;
        Return("simulated");
    }

    // ------------------------------------------------------------------------------------------ recovery

    /// <summary>Hands the apps an earlier run left closed to the cards (never opens them by itself).</summary>
    public void OfferRecovery(ClosedAppsFile file)
    {
        _recovery = file;
        _recoveryFailedAtReturn = false;
        Changed?.Invoke();
    }

    /// <summary>"Open them again": starts the apps of the recovery card, puts their windows back, changes neither the stacking nor the focus.</summary>
    public void ReopenRecovery()
    {
        var file = _recovery;
        if (file is null || _reopenJob is { Running: true } || IsAway)
        {
            return;
        }

        var job = new AwayReopenJob(_log, _store, file, file.Apps, []);
        _reopenJob = job;
        Changed?.Invoke();
        Task.Run(() =>
        {
            try
            {
                job.Start();
                job.Finish(null, 0);
            }
            catch (Exception ex)
            {
                _log.Error("[away] reopen failed", ex);
            }
            finally
            {
                _ctx.Ui.BeginInvoke(() => Safe(() =>
                {
                    _reopenJob = null;
                    if (file.Apps.Count == 0)
                    {
                        _recovery = null;
                    }
                    else
                    {
                        _recoveryFailedAtReturn = true;
                    }

                    Changed?.Invoke();
                }));
            }
        });
    }

    /// <summary>"Forget them": the record is deleted and the apps stay closed.</summary>
    public void ForgetRecovery()
    {
        var count = RecoveryCount;
        _store.Delete();
        _recovery = null;
        _log.Info($"[away] closed apps: forgotten {count}");
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------------------------------ events

    private void OnSettingChanged(string key)
    {
        if (key == "awayModeEnabled")
        {
            _ctx.Ui.BeginInvoke(() => Safe(() =>
            {
                if (!_app.Get<bool>("awayModeEnabled") && State == AwayState.Away)
                {
                    Return("disabled");
                }

                Changed?.Invoke();
            }));
        }
    }

    private void OnSession(object sender, SessionSwitchEventArgs e)
    {
        _ctx.Ui.BeginInvoke(() => Safe(() =>
        {
            switch (e.Reason)
            {
                case SessionSwitchReason.SessionLock:
                    _locked = true;
                    ForgetPresence();
                    if (_watcher.IsRunning)
                    {
                        Task.Run(() => _watcher.Stop("suspend"));
                    }

                    break;
                case SessionSwitchReason.SessionUnlock:
                    _locked = false;
                    if (State is AwayState.Away or AwayState.GoingAway)
                    {
                        Return("unlock");
                    }

                    break;
            }
        }));
    }

    private void OnPower(object sender, PowerModeChangedEventArgs e)
    {
        _ctx.Ui.BeginInvoke(() => Safe(() =>
        {
            if (e.Mode == PowerModes.Suspend)
            {
                _suspended = true;
                ForgetPresence();
                if (_watcher.IsRunning)
                {
                    Task.Run(() => _watcher.Stop("suspend"));
                }
            }
            else if (e.Mode == PowerModes.Resume)
            {
                _suspended = false;
            }
        }));
    }

    // ------------------------------------------------------------------------------------------ developer aid

    /// <summary>Developer action <c>dump</c>: state and records (no titles, no paths) to <c>away-mode\dump.json</c>.</summary>
    public void Dump()
    {
        var records = _records.Select(r => new
        {
            z = r.Z,
            max = r.Max,
            iconicNow = WinApi.IsWindow(r.Handle) && WinApi.IsIconic(r.Handle),
        }).ToList();
        var armed = _app.Get<bool>("awayModeEnabled");
        var reason = _presence.Current(ownCameraOpen: _presenceCamera || _watcher.IsRunning).ToString().ToLowerInvariant();
        var dump = new
        {
            state = State.ToString(),
            trigger = _trigger,
            since = State == AwayState.Present ? null : (DateTime?)_awaySince,
            suppressed = reason,
            idleSeconds = AwayNative.IdleMilliseconds() / 1000,
            idleCountedSeconds = EffectiveIdleMs() / 1000,
            armed,
            records,
            closeJob = CloseJobState.ToString().ToLowerInvariant(),
            apps = (_closeJob?.AllApps() ?? []).Select(a => new { kind = a.Kind.ToString().ToLowerInvariant(), how = a.How, state = a.State.ToString().ToLowerInvariant(), reason = a.Reason.ToString().ToLowerInvariant(), windows = a.Windows.Count }).ToList(),
            recovery = RecoveryCount,
            camera = new { running = _watcher.IsRunning, supported = AwayFaceWatcher.IsSupported, access = AwayFaceWatcher.Access.ToString(), failed = _app.Get<bool>("awayModeCameraFailed") },
        };
        try
        {
            File.WriteAllText(Path.Combine(_directory, "dump.json"), JsonSerializer.Serialize(dump, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            _log.Warn($"[away] dump not written: {ex.GetType().Name}");
        }

        _log.Info($"[away] dump: state={State} records={records.Count} suppressed={dump.suppressed} armed={armed}");
    }

    private void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _log.Error("[away] error", ex);
        }
    }
}
