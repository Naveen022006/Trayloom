// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Windows.Threading;
using Trayloom.Core;

namespace Trayloom.Features.Island;

internal enum TimerMode
{
    Timer,
    Pomodoro,
    Stopwatch,
}

internal enum TimerPhase
{
    Timer,
    Stopwatch,
    Focus,
    ShortBreak,
    LongBreak,
}

/// <summary>Timer, stopwatch and Pomodoro session (spec Part C 7.4); memory only, monotonic clock.</summary>
internal sealed class TimerService : IDisposable
{
    private readonly IAppSettings _app;
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _tick;
    private readonly TimerAlarm _sound;
    private double _anchor;
    private double _anchorValue;
    private int _lastWhole = -1;

    public TimerService(IAppSettings app, Dispatcher ui, ILog log)
    {
        _app = app;
        _ui = ui;
        _tick = new DispatcherTimer(DispatcherPriority.Normal, ui) { Interval = TimeSpan.FromMilliseconds(200) };
        _tick.Tick += (_, _) => OnTick();
        _sound = new TimerAlarm(ui, log);
        _sound.Changed += OnSoundEnded;
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPower;
    }

    public bool HasSession { get; private set; }

    public TimerMode Mode { get; private set; }

    public TimerPhase Phase { get; private set; }

    public bool Running { get; private set; }

    public bool Paused => HasSession && !Running && !Completed;

    public bool Completed { get; private set; }

    public bool CycleFinished { get; private set; }

    /// <summary>True while the finished alarm is sounding (any interaction stops it).</summary>
    public bool Ringing => _sound.IsPlaying;

    /// <summary>Raised on the UI thread when the alarm starts or stops sounding; <see cref="Ringing"/> is already updated.</summary>
    public event Action? AlarmChanged;

    public int CompletedFocuses { get; private set; }

    public int TotalSessions { get; private set; }

    public int LongInterval { get; private set; }

    private int _focus, _short, _long;

    public bool IsStopwatch => Phase == TimerPhase.Stopwatch;

    public int SessionNumber => Math.Min(TotalSessions, CompletedFocuses + (Phase == TimerPhase.Focus && !Completed ? 1 : 0));

    /// <summary>Raised on any state change (start, pause, complete, cancel).</summary>
    public event Action? Changed;

    /// <summary>Raised when the whole second of the reading changes.</summary>
    public event Action? Ticked;

    /// <summary>Raised when a phase/timer completed (after state is updated).</summary>
    public event Action<TimerPhase, bool>? Finished;

    public event Action<TimerPhase>? Started;

    private static double NowSec => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    /// <summary>Remaining (countdown) or elapsed (stopwatch) seconds, exact.</summary>
    public double ReadingSeconds
    {
        get
        {
            if (!HasSession)
            {
                return 0;
            }

            if (Completed)
            {
                return IsStopwatch ? _anchorValue : 0;
            }

            if (!Running)
            {
                return _anchorValue;
            }

            var delta = NowSec - _anchor;
            return IsStopwatch ? _anchorValue + delta : Math.Max(0, _anchorValue - delta);
        }
    }

    /// <summary>Countdowns round up (limit 180 min); the stopwatch rounds down saturating at 99:59:59.</summary>
    public int ReadingWhole => IsStopwatch ? (int)Math.Min(Math.Floor(ReadingSeconds), 359999) : (int)Math.Ceiling(ReadingSeconds - 1e-6);

    public string ClockText
    {
        get
        {
            var s = ReadingWhole;
            return s >= 3600 ? $"{s / 3600}:{s % 3600 / 60:00}:{s % 60:00}" : $"{s / 60:00}:{s % 60:00}";
        }
    }

    /// <summary>Compact text for the closed island ("1h35", "45m", "45s", stopwatch mm:ss).</summary>
    public string CompactText
    {
        get
        {
            var s = ReadingWhole;
            if (IsStopwatch)
            {
                return s >= 3600 ? $"{s / 3600}h{s % 3600 / 60:00}" : $"{s / 60:00}:{s % 60:00}";
            }

            if (s >= 3600)
            {
                return $"{s / 3600}h{s % 3600 / 60:00}";
            }

            return s >= 60 ? $"{(s + 59) / 60}m" : $"{s}s";
        }
    }

    public string PhaseName => Phase switch
    {
        TimerPhase.Stopwatch => Loc.T("Stopwatch"),
        TimerPhase.Focus => Loc.T("Focus"),
        TimerPhase.ShortBreak => Loc.T("Short break"),
        TimerPhase.LongBreak => Loc.T("Long break"),
        _ => Loc.T("Timer"),
    };

    public string HeaderName => CycleFinished ? Loc.T("Pomodoro complete") : (Completed && Mode != TimerMode.Pomodoro ? Loc.T("Time is up") : (Completed ? Loc.T("Time is up") : PhaseName));

    public TimerPhase NextPhase => Phase == TimerPhase.Focus
        ? (CompletedFocuses % Math.Max(1, LongInterval) == 0 ? TimerPhase.LongBreak : TimerPhase.ShortBreak)
        : TimerPhase.Focus;

    public static string PhaseTitle(TimerPhase p) => p switch
    {
        TimerPhase.Focus => Loc.T("Focus"),
        TimerPhase.ShortBreak => Loc.T("Short break"),
        TimerPhase.LongBreak => Loc.T("Long break"),
        TimerPhase.Stopwatch => Loc.T("Stopwatch"),
        _ => Loc.T("Timer"),
    };

    public void Start(TimerMode mode)
    {
        StopAlarm();
        if (HasSession)
        {
            return;
        }

        Mode = mode;
        Completed = false;
        CycleFinished = false;
        CompletedFocuses = 0;
        _focus = Math.Clamp(_app.Get<int>(K.PomodoroFocus), 1, 180);
        _short = Math.Clamp(_app.Get<int>(K.PomodoroShort), 1, 60);
        _long = Math.Clamp(_app.Get<int>(K.PomodoroLong), 1, 60);
        LongInterval = Math.Clamp(_app.Get<int>(K.PomodoroInterval), 1, 24);
        TotalSessions = Math.Clamp(_app.Get<int>(K.PomodoroTotal), 1, 24);
        switch (mode)
        {
            case TimerMode.Stopwatch:
                Phase = TimerPhase.Stopwatch;
                BeginAnchor(0);
                break;
            case TimerMode.Pomodoro:
                Phase = TimerPhase.Focus;
                BeginAnchor(_focus * 60.0);
                break;
            default:
                Phase = TimerPhase.Timer;
                BeginAnchor(Math.Clamp(_app.Get<int>(K.TimerMinutes), 1, 180) * 60.0);
                break;
        }

        HasSession = true;
        Running = true;
        _lastWhole = -1;
        _tick.Start();
        Started?.Invoke(Phase);
        Changed?.Invoke();
    }

    private void BeginAnchor(double value)
    {
        _anchor = NowSec;
        _anchorValue = value;
    }

    public void Pause()
    {
        StopAlarm();
        if (!Running)
        {
            return;
        }

        _anchorValue = ReadingSeconds;
        Running = false;
        Changed?.Invoke();
    }

    public void Resume()
    {
        StopAlarm();
        if (!HasSession || Running || Completed)
        {
            return;
        }

        _anchor = NowSec;
        Running = true;
        Changed?.Invoke();
    }

    public void TogglePause()
    {
        if (Running)
        {
            Pause();
        }
        else
        {
            Resume();
        }
    }

    public void StartNextPhase()
    {
        if (!HasSession || !Completed || CycleFinished || Mode != TimerMode.Pomodoro)
        {
            return;
        }

        StopAlarm();
        Phase = NextPhase;
        Completed = false;
        BeginAnchor((Phase == TimerPhase.Focus ? _focus : (Phase == TimerPhase.LongBreak ? _long : _short)) * 60.0);
        Running = true;
        _lastWhole = -1;
        Started?.Invoke(Phase);
        Changed?.Invoke();
    }

    public void Cancel()
    {
        StopAlarm();
        HasSession = false;
        Running = false;
        Completed = false;
        CycleFinished = false;
        _tick.Stop();
        Changed?.Invoke();
    }

    private void OnTick()
    {
        if (!HasSession)
        {
            _tick.Stop();
            return;
        }

        if (Running && !IsStopwatch && ReadingSeconds <= 0)
        {
            Complete();
            return;
        }

        var whole = ReadingWhole;
        if (whole != _lastWhole)
        {
            _lastWhole = whole;
            Ticked?.Invoke();
        }
    }

    private void Complete()
    {
        Running = false;
        Completed = true;
        _anchorValue = 0;
        if (Phase == TimerPhase.Focus)
        {
            CompletedFocuses++;
            if (CompletedFocuses >= TotalSessions)
            {
                CycleFinished = true;
            }
        }

        if (Mode == TimerMode.Pomodoro && Phase != TimerPhase.Focus && CompletedFocuses >= TotalSessions)
        {
            CycleFinished = true;
        }

        _lastWhole = -1;

        // The alarm starts before the events so every listener already sees Ringing (the strip and the Focus session rely on it).
        if (TimerAlarmRules.ShouldRing(Phase, _app.Get<bool>(K.TimerSound)))
        {
            StartAlarm();
        }

        Changed?.Invoke();
        Finished?.Invoke(Phase, CycleFinished);
    }

    private void StartAlarm()
    {
        var repeat = _app.Get<bool>(K.TimerAlarmRepeat);
        var sound = _app.Get<string>(K.TimerAlarmSound);
        var volume = TimerAlarmRules.Volume(_app.Get<int>(K.TimerAlarmVolume));
        _sound.Play(sound, volume, repeat, TimeSpan.FromSeconds(TimerAlarmRules.MaxRingSeconds));
        AlarmChanged?.Invoke();
    }

    /// <summary>Silences the alarm. Does nothing when it is not sounding.</summary>
    public void StopAlarm() => _sound.Stop();

    /// <summary>The sound ended or was stopped: tell listeners, then let the displays refresh.</summary>
    private void OnSoundEnded()
    {
        AlarmChanged?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>
    /// Extends a finished timer or Pomodoro phase by <paramref name="seconds"/> and runs it again. A snoozed focus does not count
    /// as a completed session until it ends for good. Returns false when there is nothing to snooze.
    /// </summary>
    public bool Snooze(int seconds = 60)
    {
        if (!HasSession || !TimerAlarmRules.CanSnooze(Mode, Completed))
        {
            return false;
        }

        CompletedFocuses = TimerAlarmRules.FocusesAfterSnooze(CompletedFocuses, Phase);
        CycleFinished = false;
        Completed = false;
        BeginAnchor(Math.Max(1, seconds));
        Running = true;
        _lastWhole = -1;
        _tick.Start();

        // Stopping after the state is set means the Changed it raises already shows the running countdown.
        _sound.Stop();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Developer aid (TRAYLOOM_DEV=1): a plain timer that ends in a few seconds, without touching the saved length.</summary>
    internal void StartForTest(int seconds)
    {
        if (HasSession)
        {
            Cancel();
        }

        Mode = TimerMode.Timer;
        Phase = TimerPhase.Timer;
        Completed = false;
        CycleFinished = false;
        CompletedFocuses = 0;
        TotalSessions = 1;
        LongInterval = 1;
        BeginAnchor(Math.Max(1, seconds));
        HasSession = true;
        Running = true;
        _lastWhole = -1;
        _tick.Start();
        Started?.Invoke(Phase);
        Changed?.Invoke();
    }

    private void OnPower(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Resume)
        {
            _ui.BeginInvoke(OnTick);
        }
    }

    public void Dispose()
    {
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPower;
        _tick.Stop();
        _sound.Changed -= OnSoundEnded;
        _sound.Dispose();
    }
}
