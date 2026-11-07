# 10c: Focus session (Island)

A Pomodoro that can enter a Mode, holds back Trayloom's optional island notices while a focus phase runs, and shows the
companion beside the time left. Design: `10-next-batch-0.8.md`, section P3. Needs P0 (`IModesService`) and P1 (timer alarm).

## Flow
1. **Start.** On the Timer page, Pomodoro's Start (and the `focus` island action, "Start a focus session") calls
   `Core.Focus.Start()`. It starts a Pomodoro if none is running (an existing Pomodoro is joined; a plain timer or stopwatch
   is left alone), sets `Active`, then enters the chosen mode with trigger `"focus"`.
2. **Mode.** Entered only if `notchFocusModeId` names an existing mode that is not already active. A refusal (Away, elevated
   window) shows Modes' own toast, the Pomodoro still runs, and nothing is left later. The previous mode is not restored.
3. **Quiet.** `ShowNotice` passes every notice through `Focus.Hold` after the existing gates. While `Quieting` (session on,
   `notchFocusQuiet`, focus phase running and not finished) the events `systemNotification`, `clipboard`, `track`, `download`,
   `external` and `appleMusic` are counted and dropped. Timers, battery, microphone, agents, volume and the rest still show.
   Held Windows notifications stay on the Notifications page. Breaks do not quiet. The mascot makes no visits while quieting.
4. **Mascot.** The closed Timer strip puts the companion before the reading while `Active`, `notchFocusMascot` and the
   mascot is enabled. The strip is rebuilt on `Focus.Changed`.
5. **Alarm and summary.** When a focus phase ends the alarm starts first (P1). If it rings, the summary waits for
   `AlarmChanged` with `!Ringing`; otherwise it is posted at once. "While you focused", "1 notice held back" or
   "{0} notices held back", only if `Held > 0`; then `Held` resets. The summary event is gated by `notchFocusQuiet`.
6. **End.** On `Timer.Changed`, when the session has gone (`!HasSession`) or the cycle finished, `End()` leaves the mode if
   `notchFocusLeaveMode` and the mode is still the one Focus entered, clears `Active`, and the mascot reacts "celebrate"
   (only when the cycle finished).

## Settings
| Key | Default |
|---|---|
| `notchFocusModeId` | `""` |
| `notchFocusQuiet` | true |
| `notchFocusLeaveMode` | true |
| `notchFocusMascot` | true |

Settings > Dynamic Island > Content > Timer: heading "Focus session"; "Mode during focus" (hidden when Modes is missing);
"Leave the mode when the session ends"; "Quiet the island while you focus"; "Show the companion with the time left"; the Do
not disturb caption and the button "Open Windows notification settings…" (`ms-settings:notifications` on build 22000+,
else `ms-settings:quiethours`). The Leave switch lives with the mode picker and is hidden with it.

Island Timer page: a "Mode" chip (menu of "None" and the modes) under the Pomodoro readouts, only when Modes exists; the
page height grows by 8 + 28 and the ruler gives that up first. While a session is on, the band subtitle reads
"Session 1 of 4 · {mode name}" when a mode was entered.

## Deviations from the design
- `Start()` does nothing while a non-Pomodoro session runs (the design set `Active` anyway).
- The "celebrate" reaction plays only when the cycle finishes, not when the session is cancelled. A cancelled session still
  posts the held summary right away (unless the alarm rings).
- Snoozing after the final focus does not bring the session back; it has already ended.
- The Leave switch is hidden together with the mode picker when Modes is missing.

## Harness
`tests/Trayloom.Island.Harness/FocusChecks.cs`: `Quietable` (six true, eight false), the `Quieting` truth table,
`ModeToEnter` and `ShouldLeave`.

## Manual tests
1. Mode = Coding, Pomodoro with 1-minute phases: Coding is entered.
2. Copy text during focus: no "Copied" notice. A Windows notification (banner reading on) is held.
3. Phase ends: alarm; after Stop, "While you focused · 2 notices held back".
4. During the break, notices show.
5. Done: Coding is left; if another mode was started in between it is not left.
6. Mascot beside the time, no visits during focus.
7. Uninstall Modes: the chip and the picker are hidden.
