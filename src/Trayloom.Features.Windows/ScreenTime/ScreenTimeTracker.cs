// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows.Threading;
using Microsoft.Win32;
using Trayloom.Core;
using Trayloom.Core.Native;
using Windows.Media.Control;

namespace Trayloom.Features.WindowTools.ScreenTime;

/// <summary>
/// Counts the time each app is in front while the person uses the PC (spec 06 section 1.3). Everything runs on the UI thread: the
/// foreground hook is out-of-context, so its callback arrives through the message loop. Window titles are never read.
/// </summary>
internal sealed class ScreenTimeTracker
{
    /// <summary>One credit adds at most this many seconds; a longer gap is a sleep, a hang or a clock change.</summary>
    private const double MaxCreditSeconds = 30;

    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
    };

    private readonly IAppSettings _app;
    private readonly ILog _log;
    private readonly Dispatcher _ui;
    private readonly ScreenTimeStore _store;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(10) };
    private WinApi.WinEventProc? _proc;
    private nint _hook;
    private bool _tracking;
    private bool _locked;
    private bool _disconnected;
    private bool _suspended;
    private bool _hasMark;
    private DateTime _mark;
    private string? _currentId;
    private string _currentName = "";
    private bool _dirty;
    private DateTime _lastFlush = DateTime.Now;
    private DateTime _lastRaise = DateTime.MinValue;
    private int _lastMinute = -1;
    private GlobalSystemMediaTransportControlsSessionManager? _media;
    private bool _mediaRequested;
    private bool _mediaLogged;

    public ScreenTimeTracker(IAppSettings app, ILog log, Dispatcher ui, ScreenTimeStore store)
    {
        _app = app;
        _log = log;
        _ui = ui;
        _store = store;
        Today = store.Load(DateTime.Now.Date) ?? new DayUsage(DateTime.Now);
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>The day being counted (in memory; the page reads this, never the disk).</summary>
    public DayUsage Today { get; private set; }

    /// <summary>True while the foreground hook and the tick run.</summary>
    public bool Tracking => _tracking;

    /// <summary>Raised (UI thread) when the visible minute changed, at most every 10 s.</summary>
    public event Action? Changed;

    /// <summary>Raised (UI thread) when counting moved to a new local day.</summary>
    public event Action? DayChanged;

    /// <summary>Starts listening to power and session events (they stay on while the feature runs; they only matter while tracking).</summary>
    public void Attach()
    {
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionEnding += OnSessionEnding;
    }

    /// <summary>Stops everything: credits up to now, flushes, unhooks and detaches.</summary>
    public void Detach()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionEnding -= OnSessionEnding;
        StopTracking();
        _media = null;
    }

    /// <summary>Hooks the foreground event and starts the 10 s tick with a fresh mark.</summary>
    public void StartTracking()
    {
        if (_tracking)
        {
            return;
        }

        _tracking = true;
        _mark = DateTime.Now;
        _hasMark = true;
        _proc = OnForegroundEvent;
        _hook = WinApi.SetWinEventHook(WinApi.EVENT_SYSTEM_FOREGROUND, WinApi.EVENT_SYSTEM_FOREGROUND, 0, _proc, 0, 0, WinApi.WINEVENT_OUTOFCONTEXT);
        if (_hook == 0)
        {
            _log.Warn("[screen-time] foreground hook failed");
        }

        SetCurrent(NativeMethods.GetForegroundWindow());
        _timer.Start();
        _log.Info("[screen-time] tracking started");
    }

    /// <summary>Credits up to now, flushes and unhooks.</summary>
    public void StopTracking()
    {
        if (!_tracking)
        {
            return;
        }

        Credit(DateTime.Now);
        _tracking = false;
        _timer.Stop();
        if (_hook != 0)
        {
            WinApi.UnhookWinEvent(_hook);
            _hook = 0;
        }

        _proc = null;
        _hasMark = false;
        _currentId = null;
        Flush();
        _log.Info("[screen-time] tracking stopped");
    }

    /// <summary>Writes today to disk when it changed (or always with <paramref name="force"/>).</summary>
    public void Flush(bool force = false)
    {
        if (!_dirty && !force)
        {
            return;
        }

        if (_store.Save(Today))
        {
            _dirty = false;
        }

        _lastFlush = DateTime.Now;
    }

    /// <summary>Forgets today (after "Clear history…").</summary>
    public void ResetToday()
    {
        Today = new DayUsage(DateTime.Now);
        _dirty = false;
        _lastMinute = -1;
    }

    /// <summary>Developer aid: credits 60 s to the front app, bypassing the active rules.</summary>
    public void AddMinute()
    {
        var now = DateTime.Now;
        SetCurrent(NativeMethods.GetForegroundWindow());
        var id = _currentId ?? ScreenTimeKeys.Unknown;
        var name = _currentId is null ? ScreenTimeStrings.OtherApps : _currentName;
        EnsureDay(now);
        Today.Add(id, name, 60, now.Hour);
        Today.First ??= now.ToString("HH:mm");
        _dirty = true;
        RaiseChanged(force: true);
    }

    /// <summary>Why counting is (not) happening right now: input, media, fullscreen, idle, locked, suspended or off.</summary>
    public string ActiveReason()
    {
        if (!_tracking)
        {
            return "off";
        }

        if (_locked || _disconnected)
        {
            return "locked";
        }

        if (_suspended)
        {
            return "suspended";
        }

        var limit = Math.Max(1, _app.Get(ScreenTimeKeys.IdleMinutes, 2)) * 60_000L;
        if (ScreenTimeNative.IdleMilliseconds() < limit)
        {
            return "input";
        }

        if (_app.Get(ScreenTimeKeys.CountMedia, true))
        {
            if (ScreenTimeNative.IsFullScreenOrPresenting())
            {
                return "fullscreen";
            }

            if (MediaPlaying())
            {
                return "media";
            }
        }

        return "idle";
    }

    private static bool Counts(string reason) => reason is "input" or "media" or "fullscreen";

    private bool MediaPlaying()
    {
        if (_media is null)
        {
            if (!_mediaRequested)
            {
                _mediaRequested = true;
                ScreenTimeGuard.FireAndLog(RequestMediaAsync, _log, "[screen-time] media request");
            }

            return false;
        }

        try
        {
            foreach (var session in _media.GetSessions())
            {
                if (session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            LogMediaOnce(ex);
        }

        return false;
    }

    private async Task RequestMediaAsync()
    {
        try
        {
            _media = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask();
        }
        catch (Exception ex)
        {
            LogMediaOnce(ex);
        }
    }

    private void LogMediaOnce(Exception ex)
    {
        if (!_mediaLogged)
        {
            _mediaLogged = true;
            _log.Warn($"[screen-time] media sessions unavailable: {ex.GetType().Name} {ex.HResult}");
        }
    }

    // ------------------------------------------------------------------------------------------------ credits

    private void OnForegroundEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint threadId, uint time)
    {
        if (!_tracking || hwnd == 0)
        {
            return;
        }

        Credit(DateTime.Now);
        SetCurrent(hwnd);
    }

    private void Tick()
    {
        var now = DateTime.Now;
        Credit(now);
        // The hook can miss an event (rare): re-read the front window so the app is corrected within one tick.
        SetCurrent(NativeMethods.GetForegroundWindow());
        if (_dirty && (now - _lastFlush).TotalSeconds >= 60)
        {
            Flush();
        }
    }

    /// <summary>Adds the time since the mark to the app in front, when the PC was active, then moves the mark to <paramref name="now"/>.</summary>
    private void Credit(DateTime now)
    {
        if (!_hasMark)
        {
            _mark = now;
            _hasMark = true;
            return;
        }

        if (now < _mark)
        {
            _mark = now;
            return;
        }

        var from = _mark;
        _mark = now;
        var seconds = (now - from).TotalSeconds;
        if (seconds <= 0 || seconds > MaxCreditSeconds || _currentId is null || !Counts(ActiveReason()))
        {
            EnsureDay(now);
            return;
        }

        // Split the span at every hour and midnight so each bucket gets its own part.
        var cursor = from;
        while (cursor < now)
        {
            var next = new DateTime(cursor.Year, cursor.Month, cursor.Day, cursor.Hour, 0, 0).AddHours(1);
            var end = next < now ? next : now;
            EnsureDay(cursor);
            Today.Add(_currentId, _currentName, (end - cursor).TotalSeconds, cursor.Hour);
            Today.First ??= cursor.ToString("HH:mm");
            cursor = end;
        }

        EnsureDay(now);
        _dirty = true;
        RaiseChanged(force: false);
    }

    /// <summary>Makes <see cref="Today"/> the day of <paramref name="moment"/>: flushes the old day, loads the new one and sweeps old files.</summary>
    private void EnsureDay(DateTime moment)
    {
        if (moment.Date == Today.Date)
        {
            return;
        }

        Flush(force: true);
        Today = _store.Load(moment.Date) ?? new DayUsage(moment);
        _dirty = false;
        _lastMinute = -1;
        _store.Sweep(Math.Max(1, _app.Get(ScreenTimeKeys.KeepDays, 7)), moment.Date);
        _log.Info("[screen-time] new day");
        DayChanged?.Invoke();
        Changed?.Invoke();
    }

    private void RaiseChanged(bool force)
    {
        var minute = (int)(Today.Total / 60);
        var now = DateTime.Now;
        if (!force && (minute == _lastMinute || (now - _lastRaise).TotalSeconds < 10))
        {
            return;
        }

        _lastMinute = minute;
        _lastRaise = now;
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------------------------------------ the front app

    private void SetCurrent(nint hwnd)
    {
        if (hwnd == 0)
        {
            return;
        }

        try
        {
            var (id, name) = Resolve(hwnd);
            if (id != _currentId)
            {
                // The time up to now was credited by the caller; a change from the tick follows the same order.
                _currentId = id;
                _currentName = name;
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"[screen-time] front window unreadable: {ex.GetType().Name} {ex.HResult}");
        }
    }

    /// <summary>The app of a window: its executable path, the shell pseudo app, or "unknown"; null id for the lock screen.</summary>
    private static (string? Id, string Name) Resolve(nint hwnd)
    {
        var cls = WinApi.GetClass(hwnd);
        if (ShellClasses.Contains(cls))
        {
            return (ScreenTimeKeys.Shell, ScreenTimeStrings.DesktopAndTaskbar);
        }

        var owner = AppIdentity.OwnerOf(hwnd);
        var file = owner.FileName;
        if (file == "lockapp.exe")
        {
            return (null, "");
        }

        // ShellHost.exe and ShellExperienceHost.exe are the shell's own hosts on current Windows 11 builds (spec deviation: not listed there).
        if (file is "shellhost.exe" or "shellexperiencehost.exe"
            || (cls == "Windows.UI.Core.CoreWindow" && file is "searchhost.exe" or "startmenuexperiencehost.exe"))
        {
            return (ScreenTimeKeys.Shell, ScreenTimeStrings.DesktopAndTaskbar);
        }

        return owner.Path is null
            ? (ScreenTimeKeys.Unknown, ScreenTimeStrings.OtherApps)
            : (owner.Path, AppIdentity.AppName(owner.Path));
    }

    // ------------------------------------------------------------------------------------------------ session and power

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e) => _ui.BeginInvoke(() =>
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
                Credit(DateTime.Now);
                _locked = true;
                Flush();
                break;
            case SessionSwitchReason.SessionUnlock:
                _locked = false;
                _mark = DateTime.Now;
                break;
            case SessionSwitchReason.ConsoleDisconnect:
            case SessionSwitchReason.RemoteDisconnect:
                Credit(DateTime.Now);
                _disconnected = true;
                break;
            case SessionSwitchReason.ConsoleConnect:
            case SessionSwitchReason.RemoteConnect:
                _disconnected = false;
                _mark = DateTime.Now;
                break;
        }
    });

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e) => _ui.BeginInvoke(() =>
    {
        if (e.Mode == PowerModes.Suspend)
        {
            Credit(DateTime.Now);
            _suspended = true;
            _hasMark = false;
            Flush();
        }
        else if (e.Mode == PowerModes.Resume)
        {
            // A fresh mark: the time asleep is not counted.
            _suspended = false;
            _mark = DateTime.Now;
            _hasMark = true;
        }
    });

    private void OnSessionEnding(object sender, SessionEndingEventArgs e) => _ui.BeginInvoke(DispatcherPriority.Send, () =>
    {
        Credit(DateTime.Now);
        Flush();
    });
}
