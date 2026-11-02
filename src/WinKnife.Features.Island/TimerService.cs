// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Media;
using System.Windows.Threading;
using WinKnife.Core;

namespace WinKnife.Features.Island;

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
    private readonly DispatcherTimer _alarm;
    private double _anchor;
    private double _anchorValue;
    private int _lastWhole = -1;
    private DateTime _alarmStart;

    public TimerService(IAppSettings app, Dispatcher ui)
    {
        _app = app;
        _ui = ui;
        _tick = new DispatcherTimer(DispatcherPriority.Normal, ui) { Interval = TimeSpan.FromMilliseconds(200) };
        _tick.Tick += (_, _) => OnTick();
        _alarm = new DispatcherTimer(DispatcherPriority.Normal, ui) { Interval = TimeSpan.FromSeconds(2) };
        _alarm.Tick += (_, _) => PlayAlarm();
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPower;
    }

    public bool HasSession { get; private set; }

    public TimerMode Mode { get; private set; }

    public TimerPhase Phase { get; private set; }

    public bool Running { get; private set; }

    public bool Paused => HasSession && !Running && !Completed;

    public bool Completed { get; private set; }

    public bool CycleFinished { get; private set; }

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
        TimerPhase.Stopwatch => "Stopwatch",
        TimerPhase.Focus => "Focus",
        TimerPhase.ShortBreak => "Short break",
        TimerPhase.LongBreak => "Long break",
        _ => "Timer",
    };

    public string HeaderName => CycleFinished ? "Pomodoro complete" : (Completed && Mode != TimerMode.Pomodoro ? "Time is up" : (Completed ? "Time is up" : PhaseName));

    public TimerPhase NextPhase => Phase == TimerPhase.Focus
        ? (CompletedFocuses % Math.Max(1, LongInterval) == 0 ? TimerPhase.LongBreak : TimerPhase.ShortBreak)
        : TimerPhase.Focus;

    public static string PhaseTitle(TimerPhase p) => p switch
    {
        TimerPhase.Focus => "Focus",
        TimerPhase.ShortBreak => "Short break",
        TimerPhase.LongBreak => "Long break",
        TimerPhase.Stopwatch => "Stopwatch",
        _ => "Timer",
    };

    public void Start(TimerMode mode)
    {
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
        Changed?.Invoke();
        Finished?.Invoke(Phase, CycleFinished);
        if (_app.Get<bool>(K.TimerSound))
        {
            _alarmStart = DateTime.UtcNow;
            PlayAlarm();
            _alarm.Start();
        }
    }

    private void PlayAlarm()
    {
        if ((DateTime.UtcNow - _alarmStart).TotalMinutes >= 5 || !_app.Get<bool>(K.TimerSound))
        {
            _alarm.Stop();
            return;
        }

        try
        {
            SystemSounds.Asterisk.Play();
        }
        catch
        {
        }
    }

    public void StopAlarm() => _alarm.Stop();

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
        _alarm.Stop();
    }
}
