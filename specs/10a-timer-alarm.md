# 10a: Timer alarm (Dynamic Island, v0.8.0)

A timer, or a Pomodoro focus or break, that ends now rings an alarm you can choose, loop, snooze and stop from the closed island. The stopwatch never rings.

## Behaviour
- **When it rings.** `TimerService.Complete()` starts the alarm when `notchTimerSoundEnabled` is on and the phase is not the stopwatch (`TimerAlarmRules.ShouldRing`). Pomodoro phases still wait for a click to start the next one.
- **Order of events.** State is updated, the alarm starts (`Ringing` becomes true, `AlarmChanged` is raised), then `Changed`, then `Finished`. Every listener therefore already sees `Ringing`.
- **What stops it.** Stop, +1 min, Start, Pause, Resume, Cancel, opening an island page, or clicking the closed island (`StopAlarm()`). A repeating alarm stops by itself after 60 s; with repeat off it plays one pass.
- **Snooze.** `Snooze(int seconds = 60)` works only on a finished countdown (`CanSnooze`). It runs the same phase again for `seconds`. A snoozed Focus is uncounted (`FocusesAfterSnooze`) so it counts once, when it really ends.
- **Closed island.** While ringing, the timer strip becomes a wiggling bell (0.6 s, not with reduce motion), "Time is up" / "Focus done" / "Break over", and the pills **Stop** and **+1 min** (automation ids `Timer.Stop`, `Timer.Snooze`). The timer is pinned as the current activity and overrides the person's pick. A hide-until-hover island appears while ringing. The "Time is up" notice is skipped while ringing; the mascot still reacts.
- **Timer page.** The band of a finished timer has a **+1 min** button left of the close button.
- **No audio device.** A warning is logged and the old system sound plays every 2 s instead.
- **Volume** is scaled by the Windows volume. If Windows is muted the alarm is silent; Trayloom never changes the system volume. The alarm ignores Do not disturb because Windows does not report it.

## Sounds (`TimerAlarm.cs`)
- `classic`: four 880 Hz beeps of 120 ms with 5 ms fades and 80 ms gaps, then 900 ms of silence.
- `gentle`: 660 Hz then 880 Hz notes of 350 ms (fundamental plus 0.4 times the 2nd harmonic, decay e^(-t/0.25)), a 150 ms gap, then 1.2 s of silence.
- `alarm01` to `alarm10`: `%WINDIR%\Media\Alarm01.wav` and so on, offered only when the file exists, refused over 5 MB, read whole into a cached buffer. An unknown id or a missing file falls back to `classic`.
- Playback chain: `LoopingSamples` to `VolumeSampleProvider` to `SampleToWaveProvider16` to `WaveOutEvent` on the default device.

## Settings keys
| Key | Default | Meaning |
|---|---|---|
| `notchTimerSoundEnabled` (`K.TimerSound`) | true | Master switch, kept from before. |
| `notchTimerAlarmSound` | `"classic"` | Sound id. |
| `notchTimerAlarmRepeat` | true | Loop until stopped (cap 60 s). |
| `notchTimerAlarmVolume` | 70 | 0 to 100. |

## Strings
Settings: "Alarm when a timer ends" with its caption "Rings when a timer or a Pomodoro focus or break ends. The stopwatch never rings."; "Sound" with "Preview" (tooltip "Play this sound"); "Repeat until I stop it", "Stops by itself after 1 minute."; "Volume" ("{0}%"); "Windows doesn’t tell apps whether Do not disturb is on, so the alarm always rings. Its volume follows the Windows volume." Names: "Classic beep", "Gentle chime", "Windows alarm {0}". Strip: "Time is up", "Focus done", "Break over", "Stop", "+1 min", tooltip "Snooze for 1 minute".

## Contract for the Focus session (P3)
```csharp
// TimerService
public bool Ringing { get; }
public event Action? AlarmChanged;       // UI thread; raised when the alarm starts and when it stops (Ringing already updated)
public void StopAlarm();                 // no-op when not ringing
public bool Snooze(int seconds = 60);    // false when CanSnooze is false
// ClosedViews.TimerStripView
public void BuildRow();                  // rebuilds the strip content; P3 adds the mascot here
```
`StopAlarm()` raises `AlarmChanged` then `Changed`.

## Files
New: `TimerAlarm.cs`, `TimerAlarmSettings.cs`, `Prefs.Timer.cs`, `tests/Trayloom.Island.Harness/TimerAlarmChecks.cs`. Changed: `TimerService.cs`, `ClosedViews.cs` (timer strip), `IslandCore.cs`, `IslandWindow.Input.cs`, `NoticeSources.cs`, `Pages.Timer.cs`, `Settings.Content.cs`, `IslandFeature.cs` (dev action `dev-timer-5s`), `Prefs.cs`, harness `Program.cs`.

## Privacy and cost
Nothing is stored beyond the three settings. Idle cost is nil; a WaveOut thread runs for at most 60 s and the buffers (about 1 to 2 MB) are cached.

## Manual tests
1. `TRAYLOOM_DEV=1`, `--action notch dev-timer-5s`: the strip shows the bell, "Time is up", Stop and +1 min; Stop silences it.
2. +1 min resumes a 1:00 countdown.
3. It stops by itself after 60 s; with Repeat off it plays once.
4. Volume 10% against 100%.
5. Opening the island silences it.
6. A stopwatch never rings.
7. A Pomodoro rings at each phase end; +1 min during Focus extends it without counting a session.
8. Hide-until-hover on: the island appears while ringing.
9. Preview each sound in Settings.
