# Trayloom: camera-based leaving (Part 1) and Modes (Part 2), design

Everything below comes from reading `main` at `8d003c8`. Branch `away-close-apps` has no commits beyond `main` (`git log main..away-close-apps` is empty). The four `.claude/worktrees/agent-*` checkouts are older snapshots (`3d5947f`, `e272bd2`, `4d20b55`, `15ad14a`). So the Away mode files on `main` are the latest version. I still keep the Away edits small and put most new code in new files.

Two decisions shape the whole design:

- **Modes lives in the Windows project**, in a new folder `src/Trayloom.Features.Windows/Modes/` with namespace `Trayloom.Features.WindowTools.Modes`. Feature projects cannot reference each other, and Modes has to reuse Away mode's internal helpers (`AwayProcess`, `AwayBrowsers`, `AwayApps.NeverClosable`, `AppIdentity`, `WinUtil.Activate`, `WindowLayoutEngine`). A separate project could not see them. The other surfaces reach Modes through `FeatureAction`s, which needs small edits in Tools (Command Bar and Quick panel).
- **Away mode does not capture tabs or URLs.** It closes Chrome and Edge through the Restart Manager (`AwayRestartManager.Shutdown`, flags 0) and starts them again with their own registered restart command plus `--restore-last-session` (`AwayApps.cs:556-562`, `AwayReopen.cs:197-245`). The browser restores its whole session itself. The class comment at `AwayBrowsers.cs:24-25` says it outright: tabs, sessions and titles are never read. So "save what's open now as a mode" cannot reuse an Away tab capture, because none exists. Part 2 needs a new, explicit, on-demand tab reader (section 2.5).

---

## Part 1: the camera closes and reopens the workspace

### 1.1 What Away mode does today

| Question | Today | Where |
|---|---|---|
| How leaving is detected | Only input idle (`GetLastInputInfo` ≥ `awayModeIdleMinutes`, 2–60 min, default 10) or manual "Go away now". The PC locking is deliberately not a trigger. | `AwayController.PresentTick` `AwayController.cs:338-366`; `GoAway` `:205-244`; spec `specs/06-v030-features.md` §4.3 |
| Can the camera trigger leaving? | **No.** The camera starts only after the windows are minimized (`Verify` → `StartFace` `:291-294`, `:459-486`) and is used only to notice the person coming back. While present it is always off. | `AwayFaceWatcher.cs` (face detection only, 3 s per sample, 0.5 s during a quick look, `:22-23`) |
| What "close" covers | Only with "Close apps while I'm away" on (`awayModeCloseApps`). Only apps on `awayModeCloseList`, after `awayModeCloseAfterMinutes` (0 = at once). At most 20 apps. Chrome and Edge are closed through the Restart Manager, all other apps by posting `WM_CLOSE`. An app that shows a prompt is cancelled and kept. Hung, elevated, private-window and download cases are kept. Shell, consoles and terminals are never closed. Nothing is ever forced. | `MaybeStartClose` `:712-760`; `AwayCloseJob` `AwayApps.cs:142-971`; `NeverFiles` `AwayApps.cs:24-29`; prompt handling `:859-912` |
| How reopening works | When the person returns, `RestoreWorker` (`:586-670`) runs `AwayReopenJob`. Apps start through CreateProcess, never elevated. Browsers start with their restart command, so the same windows and tabs come back. Windows are paired with their old ones and placed. Failures go to a recovery card. Firefox, Brave, Vivaldi and Opera are only minimized (`BrowserMin`, `AwayBrowsers.cs:40-55`). | `AwayReopen.cs` |
| Return triggers | Two face samples in a row, unlocking, input (or a quick look when the camera is on), the person restoring a window, or manual. | `AwayTick` `:368-429`, `OnSample` `:488-500` |

**Conclusion.** "Close the tabs when I leave, reopen them when I'm back" already works with the idle trigger. What is missing:
- (a) the camera as a **leaving** trigger;
- (b) a countdown notice in the island;
- (c) a choice of which apps close beyond the list: "all my apps" and "the active mode's apps";
- (d) the link to Modes.

### 1.2 Settings the user turns on today (without the new code)

1. Features hub › Tools › install **Away mode** (it is not installed by default).
2. Settings › Quick tools › Away mode:
   - **Away mode**: on.
   - **Away after**: 2 minutes without input.
   - **Come back when the camera sees a face**: on, with **Lock the PC too** off (the face switch is disabled while lock is on).
   - **Close apps while I'm away**: on. Chrome and Edge are added automatically. Add VS Code and any others with "Add an app…".
   - **Close them**: When I leave.

After the new code, also turn on **Leave when the camera doesn't see you**, with "Nobody there for" set to 1 minute.

### 1.3 Detection method (Windows camera APIs only, no new NuGet)

Reuse `AwayFaceWatcher`: the same `MediaCapture` in `SharedReadOnly` (`:151-157`), the same `MediaFrameReader`, and the same `FaceDetector`. Add **motion** as a second signal: frame differencing on a tiny brightness grid.

Per sample, in `CheckAsync` (`:305-339`), when `MeasureMotion` is true:

1. **Gray frame.** `gray = bitmap.BitmapPixelFormat == Gray8 ? bitmap : SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Gray8)`. Pass `gray` to `FaceDetector`; Gray8 is a supported format.
2. **Raw bytes.**
   - `using (var bb = gray.LockBuffer(BitmapBufferAccessMode.Read)) { var p = bb.GetPlaneDescription(0); start = p.StartIndex; stride = p.Stride; }`
   - `var buf = new Windows.Storage.Streams.Buffer((uint)(start + stride * height)); gray.CopyToBuffer(buf); var bytes = new byte[buf.Length]; using (var r = DataReader.FromBuffer(buf)) r.ReadBytes(bytes);`
   - This needs no `unsafe` code and no extension package.
3. **Grid and signals.** `grid = AwayPresenceMath.Grid(bytes, start, width, height, stride)` gives 64×48 block means (3 072 bytes). Then compute `stats`, `usable`, and `motion` against the previous grid if it is at most 3 s old.
4. **Raise** `Observed(new AwayObservation(Face, Motion, Usable, MotionKnown))`.
5. **Keep only the newest grid** in `_previousGrid`, a 3 KB brightness thumbnail. Set it to null in `Teardown`. It is never written, logged or sent.
6. **No per-sample logging.** In presence mode do not log `"[away] camera: face seen"` (`:328`); keep that line for return mode only.

New pure file **`AwayMode/AwayPresenceMath.cs`**:

```csharp
internal readonly record struct AwayObservation(bool Face, bool Motion, bool Usable, bool MotionKnown);
internal static class AwayPresenceMath
{
    public const int GridWidth = 64, GridHeight = 48;
    public const double MinMeanLuma = 20, MinSpread = 5;      // darker or flatter = dark room / covered lens = not usable
    public const int BaseThreshold = 12, MaxThreshold = 40;
    public const double MotionShare = 0.012;                   // ≥ 1.2 % of cells changed (~37 cells: a head turn, a hand)
    public static byte[] Grid(ReadOnlySpan<byte> gray, int start, int width, int height, int stride); // block mean, edge blocks partial
    public static (double Mean, double Spread) Stats(ReadOnlySpan<byte> grid);                       // Spread = std deviation
    public static bool Usable((double Mean, double Spread) s) => s.Mean >= MinMeanLuma && s.Spread >= MinSpread;
    public static double ChangedShare(ReadOnlySpan<byte> previous, ReadOnlySpan<byte> current);
    public static bool Motion(ReadOnlySpan<byte> previous, ReadOnlySpan<byte> current) => ChangedShare(previous, current) >= MotionShare;
}
```

`ChangedShare` works as follows:
- d_i = |(c_i − mean(c)) − (p_i − mean(p))|. Subtracting the means cancels auto-exposure shifts.
- T = clamp(BaseThreshold + 2·median(d), BaseThreshold, MaxThreshold). The median comes from a counting sort over 0..255 and adapts the threshold to sensor noise.
- Result: count(d_i > T) / 3072.

**Someone is present** if `Face || (Usable && MotionKnown && Motion)`. A frame counts as **nobody** only when it is `Usable`, shows no face, has no motion, and motion is known. **Unusable** frames are never counted as absence, and their movement (often sensor noise in the dark) does not count as presence either, so a dark room still reaches the 60 s give-up. During the countdown any movement cancels.

The previous grid is compared when it is at most `MaxGridAgeMs` = 4.5 s old (not 3 s): at the relaxed 3 s rate the next frame always arrives a little after 3 s, and a 3 s limit would leave every relaxed sample without a comparison, so absence would never be counted once a face had been seen.

### 1.4 The presence state machine (pure, harness-tested)

New file **`AwayMode/AwayPresenceClock.cs`**:

```csharp
internal enum AwayPresencePhase { Off, Armed, Watching, Countdown, GaveUp }
[Flags] internal enum AwayPresenceCommand { None = 0, StartCamera = 1, StopCamera = 2, BeginCountdown = 4, CancelCountdown = 8, Leave = 16 }
internal sealed class AwayPresenceClock
{
    public static long ArmIdleMs = 30_000;          // camera only after 30 s without input (dev override, see 1.9)
    public const long WarmupMs = 2_000, CountdownMs = 10_000, UnusableGiveUpMs = 60_000, SuppressedCooldownMs = 30_000;
    public const int FastIntervalMs = 1000, RelaxedIntervalMs = 3000;
    public AwayPresencePhase Phase { get; private set; }
    public long CountdownEndsAt { get; private set; }
    public int IntervalMs { get; private set; } = FastIntervalMs;
    public string LastReason { get; private set; } = "";   // input|face|motion|suppressed|dark|failed|disabled (log only)
    public AwayPresenceCommand Tick(long now, long idleMs, bool allowed, bool suppressed, long leaveAfterMs);
    public AwayPresenceCommand Sample(long now, AwayObservation o, long leaveAfterMs);
    public void CameraStarted(long now);
    public AwayPresenceCommand CameraFailed();     // → GaveUp, StopCamera
    public void Reset();                           // → Off
}
```

**`Tick`, in this order:**

1. **Not allowed.** If `!allowed`:
   - from Watching: return `StopCamera`;
   - from Countdown: return `StopCamera | CancelCountdown`;
   - then Phase = Off.
2. **Off.** If Phase is Off, set Phase = Armed.
3. **Recent input.** If `idleMs < ArmIdleMs` (there was input):
   - Watching: return `StopCamera`;
   - Countdown: return `StopCamera | CancelCountdown`;
   - GaveUp: clear it (only input clears it);
   - then Phase = Armed.
4. **Suppressed.** If `suppressed`:
   - Watching or Countdown: stop, and cancel the countdown if one is running;
   - set cooldown = now + 30 s and Phase = Armed.
5. **Armed and cooldown passed:** Phase = Watching, reset `absentSince` and `unusableSince`, `IntervalMs = Fast`, return `StartCamera`.
6. **Countdown and `now ≥ CountdownEndsAt`:** Phase = Off, return `Leave`.

**`Sample`** does nothing unless Phase is Watching or Countdown. It ignores samples taken within `WarmupMs` of `CameraStarted`.

- **Countdown:** if someone is present, Phase = Watching, `absentSince` = null, return `CancelCountdown`.
- **Watching, frame not usable:**
  - set `absentSince` = null and `unusableSince ??= now`;
  - if unusable for 60 s or more: Phase = GaveUp, return `StopCamera`. The camera stays off until the next input.
- **Watching, no face and motion unknown:** nothing is decided; `IntervalMs = Fast` so the next sample has a recent frame to compare with.
- **Watching, someone present:** `absentSince` = null; `IntervalMs = Face ? Relaxed : Fast`.
- **`Tick`, Watching and no sample for `SilentGiveUpMs` (60 s) since the camera was asked to start or since the last sample:** Phase = GaveUp, return `StopCamera` (a hung camera never keeps the light on).
- **Idle and start:** the controller feeds `Tick` the effective idle (`AwayLeaveGuard.EffectiveIdleMs`: never counted from before the feature started or the windows came back), and `GoAway("camera")` is refused in the first 60 s after the feature starts.
- **Watching, nobody:**
  - `IntervalMs = Fast`, `absentSince ??= now`;
  - once absent for `leaveAfterMs` or more: Phase = Countdown, `CountdownEndsAt = now + 10 s`, return `BeginCountdown`.

### 1.5 Controller wiring

Make `AwayController` a `partial` class (`AwayController.cs:26` becomes `internal sealed partial class AwayController`). Add **`AwayMode/AwayController.Leave.cs`** with these members:
- `_clock`, `_presenceCamera`, `IslandContext`, `TakeNotice()`;
- `PresencePhase`, `CountdownSecondsLeft`, `PresenceLooking`, `CountdownCloses`;
- `PresenceTick()`, `OnObserved(AwayObservation)`, `Apply(AwayPresenceCommand)`;
- `StartPresenceCamera()`, `StopPresenceCamera(string reason)`, `LeaveByCamera()`.

Hooks in the existing file. Each is one or two lines.

| Where | Change |
|---|---|
| `Start` `:160-161` | Also subscribe `_watcher.Observed += o => _ctx.Ui.BeginInvoke(() => Safe(() => OnObserved(o)))`. |
| `Tick` `:329-331` | In `case Present:` call `PresenceTick();` before `PresentTick();`. |
| `PresenceTick` | Computes `allowed`, `suppressed` and `leaveAfterMs`, then calls `_clock.Tick` and applies the result. Sets `_timer.Interval` to 1000 ms while the clock is Watching or Countdown, otherwise 5000 ms. |
| `GoAway` `:212` | Treat `"camera"` like `"idle"` for the `AfterPresentMs` guard. Label: `$"camera {s} s"`. |
| `Verify` `:291-294` | If `_presenceCamera && _watcher.IsRunning`: when face return is on, hand the camera over (`_faceWanted = true; _faceStarted = now; _watcher.MeasureMotion = false; _watcher.SetInterval(3000); _presenceCamera = false;`) and **do not** call `StartFace`. Otherwise stop it (`Task.Run(() => _watcher.Stop("away"))`). |
| `StartFace` `:464` | Use `_presence.OtherAppUsesCamera()` instead of `CameraInUse()`. |
| `Return` `:571` and `RestoreWorker` `finally` `:660-668` | `_clock.Reset()`. In the finally, after `State = Present`, call `_ctx.Services.Get<IAwayWorkspace>()?.OnAwayReturned(info)` (see 1.8). |
| `OnSession` lock `:893-899`, `OnPower` suspend `:917-923`, `Stop` `:167-200` | `_clock.Reset(); _presenceCamera = false;`. The existing code already stops the watcher in these places. |
| `CloseWanted` `:146` and `MaybeStartClose` `:714-746` | Use `CloseListFor()` (see 1.7) instead of `AwayKeys.CloseList`. `CloseWanted` returns true for scope `"all"` without enumerating windows. |

**`allowed`** requires all of the following:
- `awayModeEnabled` and `awayModeLeaveCamera` are on;
- `State == Present`, not `_locked`, not `_suspended`, not a Terminal Server session;
- `AwayFaceWatcher.IsSupported` and not `AccessDenied`;
- the dev filter is absent, or `TRAYLOOM_AWAY_NO_CAMERA=1`.

**`suppressed`** is true when any of these holds:
- `_presence.Current(ownCameraOpen: _presenceCamera) != None`;
- `_presence.OtherAppUsesCamera()` is true (even with `awayModeSkipCalls` off, the watch never competes with a call);
- `AwayPresence.EnergySaverOn()` is true.

**`StartPresenceCamera`**:
- `_presenceCamera = true; _watcher.MeasureMotion = true; _watcher.SetInterval(_clock.IntervalMs);`
- fire-and-log `await _watcher.StartAsync(cameraId)`. On a failure string: `Apply(_clock.CameraFailed())`, set `awayModeCameraFailed`, and log `[away] camera: <reason>`. On success: `_clock.CameraStarted(now)`.

**After each sample**, if the interval changed, call `_watcher.SetInterval(_clock.IntervalMs)`.

**`BeginCountdown`**:
- set `_pendingNotice`;
- `IslandContext?.Invalidate()`;
- if the island is not presenting (`!(_ctx.Runtime.IsRunning("notch") && _app.Get<bool>("notchEnabled")) || IslandContext is null`), show a toast instead: `_ctx.Notifier.Show(AwayStrings.Title, …Toast…)`;
- `Changed?.Invoke()`; log `[away] leave: countdown`.

**`CancelCountdown`**: `Invalidate()`, `Changed`, log `[away] leave: cancelled (<reason>)`.

**`Leave`**: `if (!GoAway("camera")) _clock.Reset();`.

`CountdownCloses` is true when `CloseWanted && CloseAfterMinutes == 0`. It picks between the "Closing…" and "Minimizing…" wording.

**Log lines** (no frame data, no face counts, no paths):
- `[away] camera: watching (idle 30 s)`
- `[away] camera: stopped (input|suppressed|dark|away|return)`
- `[away] leave: countdown`, `[away] leave: cancelled (motion|face|input|suppressed)`
- `[away] away (camera 60 s): …`

**`AwayFaceWatcher.cs` edits:**
- add `private volatile int _normalMs = NormalIntervalMs;` and `public void SetInterval(int ms)` (sets it and resets `_due`), and use `_normalMs` at `:292`;
- add `public bool MeasureMotion { get; set; }`, `private byte[]? _previousGrid; private long _previousGridAt;` (cleared in `Teardown`), and `public event Action<AwayObservation>? Observed;`;
- add `public void FeedObservation(AwayObservation o) => Observed?.Invoke(o);` for dev testing.

**`AwayPresence.cs` edits:**
- `InUse(string capability, bool skipSelf = false)`: when `skipSelf`, `AnyInUse` skips the `NonPackaged` subkey whose name equals `Environment.ProcessPath.Replace('\\', '#')` (case-insensitive). Without this, the presence watch's own camera use would mark the person as "in a call", because the consent store records Trayloom itself.
- `public bool OtherAppUsesCamera() => InUse("webcam", skipSelf: true);`
- `Current(bool ownCameraOpen = false)`: in the calls check (`:90`), use `InUse("webcam", ownCameraOpen)`. The microphone check is unchanged.
- `public static bool EnergySaverOn()`: `Windows.System.Power.PowerManager.EnergySaverStatus == EnergySaverStatus.On`, wrapped in try/catch, returning false on error.

### 1.6 Cost, false positives, privacy

- **CPU and battery.**
  - The camera is never on while the person types or uses the mouse. It opens only after 30 s without input, and only while the feature is on.
  - Processing is one frame per second (one every 3 s while a face is seen): about 5–15 ms for face detection plus about 0.3 ms for the grid. Other frames are dropped unread (`TryAcquireLatestFrame`).
  - The sensor itself runs at the device's current format because `SharedReadOnly` cannot change it. That costs roughly 0.5–1.5 W on laptops while the camera is on. This is honest and unavoidable with a webcam.
  - The camera is off while locked, asleep, on battery saver, during calls or media, and in full screen.
- **Person sitting still or reading.** Face, or any motion above about 1.2 % of the frame, keeps them present. Leaving needs 30 s without input, then N seconds of continuous absence, then a 10 s countdown that any movement in front of the camera or any input cancels.
- **Low light or a covered lens.** Such frames are unusable and never count as absence. After 60 s of unusable frames the camera turns off until the next input, and the normal idle timer applies.
- **Video call holding the camera.** The watch never starts while another app uses the camera, or the microphone when "skip calls" is on. A shared-read-only open could still interfere with a call that starts while the watch runs; starting a call normally involves input, which closes the camera first. This must be tested manually (see 1.11).
- **Full-screen video or media playing.** Covered by the existing suppression checks, which now also stop the watch.
- **Lock screen.** The PC locking resets the clock and stops the camera; Windows also blocks cameras on the lock screen. Locking is still not a leave trigger, as before.
- **Grace periods.** 30 s input arm time, 2 s warm-up, N s continuous absence, 10 s countdown, a 30 s cooldown after a suppression ends, and the existing `AfterPresentMs` of 10 s after a return.
- **Unsaved work.** Unchanged: `WM_CLOSE` and the Restart Manager with flags 0 only. A prompt means the app is kept; nothing is ever killed.
- **Apps without restore support.** Unchanged:
  - ordinary apps reopen with their recorded command line, and their inner state is whatever the app restores itself;
  - Firefox, Brave, Vivaldi and Opera are only minimized;
  - terminals, consoles and Explorer are never closed;
  - elevated and other-user apps are kept.
- **Privacy indicators.**
  - The hardware LED and Windows' own "camera in use" indicator.
  - The island live strip "Checking you're here" (1.10).
  - The settings and tray status line "Checking that you're here with the camera".
  - Frames are processed in memory only; one 3 KB brightness grid is kept for one second.

### 1.7 Which apps close

Add a new key `awayModeCloseScope` (string, default `"list"`, values `list | mode | all`), and a helper in the partial file:

```csharp
private List<string> CloseListFor()
// "list": AwayKeys.CloseList
// "mode": CloseList ∪ Services.Get<IAwayWorkspace>()?.ActiveAppsToClose() (behaves as "list" when null/empty)
// "all":  _records.Select(r => AppIdentity.PathOf(r.Pid)).Where(p => p is not null && !AwayApps.NeverClosable(p))
//         .Select(p => p!.ToLowerInvariant()).Distinct()  (ignored apps are filtered by AwayCloseJob already)
```

The existing limit of 20 apps (`MaxCloseApps`, `AwayApps.cs:146`) still applies; apps over the limit are minimized with reason `Limit`.

### 1.8 Contract with Modes

New file **`AwayMode/AwayWorkspace.cs`**, created by package A and implemented by package B:

```csharp
namespace Trayloom.Features.WindowTools.AwayMode;
/// <summary>The workspace the person is in (provided by Modes through the service hub).</summary>
internal interface IAwayWorkspace
{
    /// <summary>Display only, never logged; null when no mode is active.</summary>
    string? ActiveModeName { get; }
    /// <summary>Lower-cased full exe paths of the active mode's app items plus the browsers of its web items; UI thread; no window enumeration.</summary>
    IReadOnlyList<string> ActiveAppsToClose();
    /// <summary>UI thread, after the return finished.</summary>
    void OnAwayReturned(AwayReturnInfo info);
}
internal sealed record AwayReturnInfo(IReadOnlyDictionary<long, nint> Replaced, IReadOnlySet<string> ReopenedExes, string Trigger);
```

In `RestoreWorker`, declare `replaced` (empty) and `reopened` (empty set) before the `try`. After `reopen.Finish(...)` (`:643`), set `replaced = reopen.Replaced; reopened = closed.Select(a => a.Exe.ToLowerInvariant()).ToHashSet();`. Pass them to `OnAwayReturned` in the finally block.

Away's own reopen remains the only thing that brings apps back. Modes only updates its window tracking from `Replaced` and does not launch anything, so nothing is opened twice.

### 1.9 Keys, defaults, UI and strings for Part 1

Add to `AwayKeys` (`AwayModeFeature.cs:10-51`) and register in `Register`:
- `LeaveCamera = "awayModeLeaveCamera"`, default false;
- `LeaveCameraSeconds = "awayModeLeaveCameraSeconds"`, default 60, values 30, 60, 120, 300;
- `CloseScope = "awayModeCloseScope"`, default "list".

**Settings card** (`AwaySettings.cs`):
- After the face block (`:105`) and before `AddCloseBlock`, add a divider and a switch "Leave when the camera doesn't see you":
  - AutomationId `AwayMode.LeaveCamera`, glyph `"\uE714"`;
  - caption `LeaveCameraCaption`, or `FaceUnsupported` when face detection is not supported (then also disabled);
  - `onToggled` → `RequestRebuild`.
- When it is on, show beneath it:
  - a `PickerRow<int>` "Nobody there for" (`AwayMode.LeaveAfter`);
  - the note `LeaveNote`;
  - the camera picker and the access-denied and camera-failed blocks. Show these when `(faceOn && !lockOn) || leaveOn`.
- The leave switch works independently of "Lock the PC too". With lock on, the person returns by unlocking.
- In `AddCloseBlock`, after "Close them" (`:199`), add a `PickerRow<string>` "Which apps" (`AwayMode.CloseScope`):
  - the `"mode"` option appears only when `c.Runtime.IsAvailable("modes")`;
  - for `"all"`, hide the "Apps to close" list and show `ScopeAllCaption`;
  - for `"mode"`, show `ScopeModeCaption` under the list.
- `StatusText` (`:378-409`): while Present, if `PresencePhase == Countdown` show `StatusClosingIn(n)` or `StatusMinimizingIn(n)`; else if `PresenceLooking` show `StatusLooking`.
- `Wire` already refreshes on `Changed`. Raise `Changed` once per second during the countdown from `PresenceTick`.

**Tray card** (`AwayPanelSection.cs:25-27`): while present, the caption is the countdown text or `StatusLooking`, falling back to the existing caption.

**Dev actions** (`AwayModeFeature.Actions` `:91-99`, only with `TRAYLOOM_DEV=1`):
- `simulate-absence`: `_watcher.FeedObservation(new(false, false, true, true))`;
- `simulate-motion`: `new(false, true, true, true)`;
- `simulate-dark`: `new(false, false, false, true)`;
- when `TRAYLOOM_DEV=1` and `TRAYLOOM_AWAY_ARM_SECONDS` is set, `AwayPresenceClock.ArmIdleMs` = that value × 1000.

**New strings** in `AwayStrings.cs`, English verbatim, all through `Loc.T` / `Loc.F`:
- `LeaveCamera` = "Leave when the camera doesn’t see you"
- `LeaveCameraCaption` = "After 30 seconds without the mouse or keyboard, the camera turns on and checks whether someone is at the PC. If nobody is there for the time below, Away mode starts after a 10-second notice. Pictures are checked on this PC and never saved; the camera light is on while it checks."
- `LeaveAfter` = "Nobody there for"; `Leave30` = "30 seconds"; `Leave60` = "1 minute"; `Leave120` = "2 minutes"; `Leave300` = "5 minutes"
- `LeaveNote` = "In a dark room, with the camera covered, while another app uses the camera, or with battery saver on, Away mode waits for the usual time without input instead."
- `StatusLooking` = "Checking that you’re here with the camera"
- `StatusClosingIn(s)` = `Loc.F("Closing your workspace in {0} s", s)`; `StatusMinimizingIn(s)` = `Loc.F("Minimizing your windows in {0} s", s)`
- `NoticeClosing` = `Loc.F("Closing your workspace in {0} s. Move to cancel.", 10)`; `NoticeMinimizing` = `Loc.F("Minimizing your windows in {0} s. Move to cancel.", 10)`
- `StripClosing(s)` = `Loc.F("Closing in {0} s", s)`; `StripMinimizing(s)` = `Loc.F("Minimizing in {0} s", s)`; `StripLooking` = "Checking you’re here"
- `ToastClosing` = `Loc.F("The camera doesn’t see you. Your workspace closes in {0} seconds; move the mouse to stay.", 10)`; `ToastMinimizing` = `Loc.F("The camera doesn’t see you. Your windows are minimized in {0} seconds; move the mouse to stay.", 10)`
- `CloseScope` = "Which apps"; `ScopeList` = "The apps on the list"; `ScopeMode` = "The active mode’s apps and the list"; `ScopeAll` = "All my apps"
- `ScopeAllCaption` = "Every app with an open window is closed, up to 20, except File Explorer, terminals and the apps under “Leave these apps alone”. Closing an app also ends what runs inside it, such as a build in an editor’s terminal."
- `ScopeModeCaption` = "The apps of the mode you’re in (Modes) are closed too, and open again when you’re back."

**Catalog description** (`FeatureCatalog.cs:119`). New text: "Minimizes your windows when you leave the PC, or when the camera stops seeing you, and brings them back in the same order when you return. Can also close the apps you choose and open them again, browsers with their tabs."

### 1.10 The island notice

New file **`AwayMode/AwayIsland.cs`**:
- **`AwayNoticeStrip : IIslandStrip`**: Id `"away.notice"`, Order 44, FeatureId `"away-mode"`.
  - `CreateStrip` sets `controller.IslandContext = context`, returns null unless `TakeNotice()` gives text, and otherwise returns a horizontal panel: glyph `\uE714` (Segoe Fluent Icons, 12, amber `#FFB340`) plus text (11, Medium, white 0.95, max 260).
  - The island shows it as a 5 s notice. `IslandCore.PollExternalNotices` handles `*.notice` strips at `IslandCore.cs:625-654`. It runs on every `Invalidate()` (`IslandWindow.State.cs:202-213`).
- **`AwayLiveStrip : IIslandStrip`**: Id `"away.live"`, Order 906.
  - Also sets `IslandContext`. The live strip is queried on every closed-island refresh (`IslandCore.cs:510-521`), so the context is captured early.
  - Returns an `AwayCountdownView` while `PresencePhase == Countdown`: a camera glyph plus `StripClosing(n)` or `StripMinimizing(n)`, updated by a 250 ms `DispatcherTimer` from `CountdownSecondsLeft`. The timer stops on `Unloaded`.
  - Else, while `PresenceLooking`: a grey camera glyph plus `StripLooking`. Else null.
- The 10 s countdown is longer than the 5 s notice. The notice draws attention; the live strip carries the countdown. Live strips rank after the built-in activities, so music would win, but media playing suppresses the watch anyway.
- No Island project changes are needed.

### 1.11 Harness checks and manual tests for Part 1

New console project **`tests/Trayloom.Away.Harness`**, not in the solution, same style as `tests/Trayloom.System.Harness/Program.cs`. Add `<InternalsVisibleTo Include="Trayloom.Away.Harness" />` and `<InternalsVisibleTo Include="Trayloom.Modes.Harness" />` to the Windows `.csproj`.

**`AwayPresenceMath` checks:**
- `Grid` of a constant 640×480 frame is all equal; a stride larger than the width is respected; a 641×481 frame keeps its edge blocks.
- Identical grids give no motion.
- A global +30 brightness shift gives no motion.
- A 10×10 block changed by +60 gives motion.
- ±5 pseudo-random noise (fixed seed) gives no motion.
- A frame with mean 10 is not usable; a uniform frame (mean 120, spread 1) is not usable; a normal scene is usable.

**`AwayPresenceClock` checks:**
- Idle 29 s: no camera. Idle 30 s: `StartCamera`.
- Samples inside the warm-up are ignored.
- 59 s of absent samples: no countdown; 60 s: `BeginCountdown`.
- A face or motion during the countdown: `CancelCountdown` and back to Watching.
- Input during the countdown: `StopCamera | CancelCountdown`.
- Countdown end: `Leave`.
- Unusable for 60 s: `StopCamera` and GaveUp. A tick without input stays in GaveUp; input returns to Armed.
- Suppression: stop, then no restart for 30 s.
- Not allowed: `StopCamera` and Off.
- A face sample sets `IntervalMs` to 3000; an absent sample sets it to 1000.
- `CameraFailed`: GaveUp.

**Manual tests**, for `TESTING.md`. The person runs these; no synthetic input.
1. Leave the desk with "Nobody there for" = 30 s:
   - the LED comes on about 30 s after the last input;
   - the island shows "Closing your workspace in 10 s. Move to cancel." and then the countdown;
   - windows minimize and Chrome closes;
   - coming back, the face brings everything back with the same tabs.
2. Sit and read without touching anything: the camera stays on and nothing happens. Look down at a book for 2 minutes: check that small movements keep you present. Note the result.
3. Wave during the countdown: it cancels.
4. Cover the lens: no countdown; after about 60 s the LED goes off until you touch the mouse.
5. Start a Teams, Zoom or Windows Camera call while the LED is on (the watch is running): the call must get the camera. Report any failure, because this is the main unknown.
6. Play a YouTube video in full screen and walk away: no countdown.
7. Lock with Win+L: the LED goes off.
8. With "Which apps" = All my apps and an unsaved Notepad: Notepad stays open, and Settings shows the reason.
9. With the island off: a toast appears instead of the island notice.
10. Battery saver on: the camera never starts.

---

## Part 2: Modes (workspaces)

### 2.1 Files

All new files are in `src/Trayloom.Features.Windows/Modes/`. Every `.cs` file starts with the two-line SPDX/Trayloom header.

**Package B:**

| File | Content |
|---|---|
| `ModesModel.cs` | Data classes and enums (2.2) |
| `ModesMath.cs` | Pure logic: ids, sanitising, URLs, dedupe, keys, argument rules, putting windows away, file-extension guard |
| `ModesStore.cs` | `modes.json` load and save: atomic, immediate, backs up a damaged file |
| `ChromiumSession.cs` | Pure SNSS session parser (2.5) |
| `ModesBrowsers.cs` | Default browser, installed browsers, Chromium profiles, launch arguments, the tab reader |
| `ModesWindows.cs` | Finding, claiming, placing, minimizing and gently closing windows; Explorer folder windows |
| `ModesSwitcher.cs` | The background switch engine |
| `ModesCapture.cs` | "Save what's open now" capture |
| `ModeVolume.cs` | Master volume through COM (copy the `IAudioEndpointVolume` pattern from `Trayloom.Features.Tools/CommandBar/BarSystem.cs:134-200`) |
| `ModesRuntime.cs` | Service: modes, active mode, switching, events; implements `IAwayWorkspace` |
| `ModesFeature.cs` | `IFeature` `"modes"`, `ModesKeys`, actions, shortcut roles |
| `ModesIsland.cs` | `ModesIslandSection`, `ModesNoticeStrip`, `ModeTilesView` (shared tile grid) |
| `ModesPanel.cs` | `ModesPanelSection` (Quick toggles tab) and `ModesUtility` (`IPanelUtility` used by the Quick panel) |
| `ModesStrings.cs` | Strings for the runtime and quick-access surfaces |

**Package C:**

| File | Content |
|---|---|
| `ModesSettings.cs` | `ISettingsSection` on Quick tools, anchor `modes` |
| `ModeEditorWindow.cs` | New or edit mode |
| `ModeCaptureWindow.cs` | Review "Save what's open now" |
| `ModesUiStrings.cs` | Strings for the settings and dialogs |

**Edits outside the folder:**
- **Package B:**
  - `src/Trayloom.Core/Catalog/FeatureCatalog.cs` (one `Def` line after `:119`);
  - `src/Trayloom.Core/Contributions.cs` (add `"modes"` to the Toggles gate list `:54` and the Utilities gate list `:42-46`);
  - `src/Trayloom.Core/Shortcuts.cs` (add `RemoveRole`).
- **Package C:**
  - `src/Trayloom.Features.Tools/CommandBar/BarCatalog.cs` (after `:167`);
  - `src/Trayloom.Features.Tools/QuickPanel/QuickPanelItems.cs` (`:32-39`);
  - `src/Trayloom.Features.Tools/QuickPanel/QuickPanelWindow.cs` (`:863`);
  - `README.md`, `TESTING.md`, `docs/DEVELOPMENT.md`.

The Island project needs **no changes**. A contributed section whose id is not in `IslandCatalog` is listed and counted as installed automatically (`IslandCore.cs:394-413` and `:416-423`). `App.FeatureAssemblies` is unchanged.

### 2.2 Data model and JSON

Stored in `%APPDATA%\Trayloom\modes\modes.json` (`AppPaths.FeatureData("modes")`, which is also `context.DataDirectory`). It is a separate file because URL lists are large and personal and must not go through the debounced flat store. Writes are atomic (`.tmp` then `File.Move(overwrite)`, as in `AwayClosedStore.Write`, `AwayClosedApps.cs:181-196`), immediate, and run on the UI thread after every edit.

If the file cannot be read, rename it to `modes.json.bad-yyyyMMddHHmmss`, start empty, and log `[modes] modes.json unreadable: kept aside`. The user's modes are never silently dropped. Use `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`.

```json
{ "v": 1,
  "modes": [ {
    "id": "m3f9a2c1d", "name": "Coding", "glyph": "\uE943", "color": "blue",
    "onEnter": { "keepAwake": "off", "volume": -1, "timer": false },
    "onLeave": "minimize",
    "items": [
      { "id": "i1", "kind": "app",  "name": "Visual Studio Code", "exe": "c:\\users\\me\\appdata\\local\\programs\\microsoft vs code\\code.exe",
        "args": "\"D:\\src\\trayloom\"", "dir": "", "aumid": null, "zone": "leftHalf", "front": true },
      { "id": "i2", "kind": "app",  "name": "Terminal", "exe": "c:\\users\\me\\appdata\\local\\microsoft\\windowsapps\\wt.exe",
        "args": "-d \"D:\\src\\trayloom\"", "dir": "D:\\src\\trayloom", "zone": "rightHalf" },
      { "id": "i3", "kind": "web",  "name": "GitHub and docs", "browser": "default", "profile": "",
        "urls": ["https://github.com/Naveen022006/Trayloom", "https://learn.microsoft.com/dotnet/"], "zone": null },
      { "id": "i4", "kind": "path", "name": "Notes", "path": "D:\\notes" }
] } ] }
```

```csharp
internal enum ModeItemKind { App, Web, Path }
internal sealed class ModeItem { Id, Kind, Name, Exe?, Args = "", Dir = "", Aumid?, Browser = "default", Profile = "", Urls = [], Path?, Zone?, Front }
internal sealed class ModeEnterActions { string KeepAwake = "off" /* off|60|120|on */; int Volume = -1 /* -1 = as is, 0..100 */; bool Timer }
internal sealed class Mode { Id, Name, Glyph, Color, OnEnter, string OnLeave = "minimize" /* keep|minimize|close */, List<ModeItem> Items }
internal sealed class ModesFile { int Version = 1; List<Mode> Modes }
internal sealed record ModeSwitchSummary(string ModeId, int Opened, int Focused, int Failed, IReadOnlyList<(string Name, string Reason)> Problems, double Seconds);
```

**Limits:**
- 12 modes; 20 items per mode; 50 URLs per web item; 2048 characters per URL; 40 characters per name.
- Colours: `blue purple pink red orange yellow green mint teal cyan indigo graphite`.
- 12 glyphs, Segoe Fluent Icons; check that each renders: `\uE943` code, `\uE8F1` library, `\uE70F` edit, `\uE774` globe, `\uE7FC` game, `\uE8D6` music, `\uE821` work, `\uE80F` home, `\uEA80` lightbulb, `\uE8BD` chat, `\uE787` calendar, `\uEB51` heart.
- `Zone` is a `LayoutActions` raw value from Halves, Thirds, Corners or `maximize`, or null (`LayoutActions.cs:60-115`).

**Flat-store keys** (`ModesKeys.Register`):
- `modesEnabled` (true), the catalog enable key;
- `modesActiveId` ("");
- `panelUtilityModes` (false), so Modes shows on the Quick toggles tab, not twice. The registered default wins (`AppSettings.cs:77-85`);
- shortcut storage `modesShortcut.<modeId>`.

### 2.3 Pure logic (`ModesMath`, for the harness)

- `NewModeId()`: `"m" + Guid.NewGuid().ToString("N")[..8]`. `NewItemId(Mode)`: `"i" + n`, unique inside the mode.
- `ModesFile Sanitize(ModesFile)`:
  - drop unknown kinds, app items without `Exe` and `Aumid`, web items with no valid URL, and path items without a path;
  - apply the limits; trim names;
  - make ids unique (suffix `-2`);
  - fix colours and glyphs that are not on the lists;
  - allow exactly one `Front` per mode (first wins; if none, the first item);
  - drop unknown zones.
- `string? NormalizeUrl(string raw)`:
  - trim;
  - with no `://`, and if the text contains `.` and no whitespace, prepend `https://`;
  - `Uri.TryCreate(Absolute)` must succeed, the scheme must be `http` or `https`, `UserInfo` must be empty, and the length at most 2048;
  - return `uri.AbsoluteUri`.
  - Everything else returns null: `javascript:`, `file:`, `chrome://`, `data:` and so on.
- `List<string> ParseUrlList(string text)`: split on whitespace and newlines (not commas), normalize each, drop nulls, dedupe by ordinal equality of the normalized URL keeping the first, cap at 50. Also report how many were rejected: `out int rejected`.
- `List<List<string>> GroupUrls(IReadOnlyList<string> urls, int perWindow = 50)` for captured windows.
- `string ItemKey(ModeItem i)`:
  - App: `"pkg|"+aumid`, or `"app|"+exe.ToLowerInvariant()+"|"+args.Trim().ToLowerInvariant()`;
  - Web: `"web|"+browser.ToLowerInvariant()+"|"+profile+"|"+string.Join("\n", urls)`;
  - Path: `"path|"+Path.GetFullPath(path).TrimEnd('\\').ToLowerInvariant()`.
- `List<ModeItem> Dedupe(IEnumerable<ModeItem>)`: by key, first wins.
- `IReadOnlyList<ModeItem> ToPutAway(Mode leaving, Mode? entering)`: the items of `leaving` whose key is not among `entering`'s keys.
- `string ArgsForFolder(string exe, string folder, out string dir)`, by file name (case-insensitive):
  - `code.exe`, `code - insiders.exe`, `cursor.exe`, `windsurf.exe`: `"\"folder\""`, dir = "";
  - `wt.exe`, `windowsterminal.exe`: `-d "folder"`, dir = folder;
  - `explorer.exe`: `"\"folder\""`;
  - anything else: args unchanged, dir = folder.
- `bool IsForwarder(string exe)`: the four editor names above. A launch of one of these is idempotent; the app focuses or opens the folder itself.
- `bool IsBlockedFile(string path)`: extension in `.exe .com .bat .cmd .ps1 .psm1 .vbs .vbe .js .jse .wsf .wsh .msi .msp .scr .hta .cpl .reg .lnk .url .appref-ms`. These must be added as apps, not opened as files.
- `IReadOnlyList<string> ChromiumArgs(string profile, IReadOnlyList<string> urls)`: `["--profile-directory=" + profile]` only when the profile is non-empty, then `"--new-window"`, then the URLs. Always pass them through `ProcessStartInfo.ArgumentList`.

### 2.4 The switch engine (`ModesSwitcher`, run by `ModesRuntime`)

**Runtime API**, used by package C and the quick-access surfaces. All on the UI thread; `Changed` is raised on the UI thread.

```csharp
internal sealed class ModesRuntime : IAwayWorkspace
{
    public IReadOnlyList<Mode> Modes { get; }             // deep copies
    public Mode? Active { get; }
    public bool IsSwitching { get; }  public string? SwitchingToId { get; }
    public ModeSwitchSummary? LastSummary { get; }
    public bool Elevated { get; }                          // AppIdentity.IsAdmin()
    public event Action? Changed;
    public bool Enter(string modeId, string trigger);      // trigger: tile|panel|quick-panel|command-bar|shortcut|settings
    public bool Leave(string trigger);
    public Mode Add(Mode mode);  public void Update(Mode mode);  public void Delete(string modeId);  public void Move(string modeId, int delta);
    public Task<ModeCapture> CaptureAsync();
    public Task<BrowserTabsResult> ReadBrowserTabsAsync(string browserId, string profile);
    public IReadOnlyList<BrowserChoice> Browsers();
    public IReadOnlyList<(string Dir, string Name)> ChromiumProfiles(string browserId);
    public static string RoleId(string modeId) => "modes." + modeId;
}
internal sealed record CapturedItem(ModeItem Item, bool Selected, string Detail, int Windows);
internal sealed record ModeCapture(IReadOnlyList<CapturedItem> Items, IReadOnlyList<string> Warnings);
internal sealed record BrowserTabsResult(bool Ok, IReadOnlyList<IReadOnlyList<string>> Windows, string? Problem);
internal sealed record BrowserChoice(string Id /* "default" or lower-cased exe */, string Name, bool Chromium);
```

**Window tracking**, in memory only: `Dictionary<(string ModeId, string ItemId), List<Tracked>>` with `Tracked(nint Hwnd, uint Pid, long PidStart, bool OpenedByMode)`. A record is valid while `IsWindow` holds and the process start time still matches.

**`Enter(modeId, trigger)` on the UI thread.** It refuses, with a notifier message, when:
- `modesEnabled` is off or the mode is missing;
- `Services.Get<AwayController>()?.IsAway == true`, giving "Not while Away mode is on.";
- `Elevated`, giving "Not available while Trayloom runs as administrator: the apps would open as administrator.";
- the mode is empty.

If a switch is already running, it stores the request as pending (last request wins) and returns true. Otherwise it starts an **STA** thread named `"Modes switch"`, because shell COM is used there.

**Steps on that thread:**

1. **Put away the previous mode**, if it is not the mode being entered, using the previous mode's `OnLeave`. Use `ToPutAway(previous, target)` and its tracked windows. If there is no tracking (after a restart), fall back to switchable windows owned by an item's exe, and only ever minimize those.
   - `keep`: nothing.
   - `minimize`: `WinApi.ShowWindowAsync(h, SW_SHOWMINNOACTIVE)`.
   - `close`:
     - only for `OpenedByMode` windows, and only when `!AwayApps.NeverClosable(exe)`; minimize everything else;
     - `WinApi.PostMessageW(h, WM_CLOSE, 0, 0)` per window, bottom of the z-order first;
     - poll every 200 ms for up to 10 s:
       - the window is gone: closed;
       - a new visible window owned by `h` appears, a `#32770` dialog of that process appears, or `h` stays disabled for 600 ms or more: a **prompt**. Leave it for the person to answer; never answer it, never force anything;
       - timeout: minimize.
     - Count and report the windows that asked something.
   - **Keep awake:** if the previous mode turned keep awake on (`_keepAwakeByMode`) and the target's `KeepAwake == "off"`, deactivate it on the UI thread.
2. **On-enter actions.**
   - **Keep awake:** `Services.Get<IKeepAwakeService>()?.Activate(60 min | 120 min | null)` on the UI thread; set `_keepAwakeByMode = true`.
   - **Volume:** `ModeVolume.Set(volume / 100f)` when the volume is 0 or more.
   - **Timer:** run the island action `notch` / `timer`, via `Actions.All`, on the UI thread.
3. **Items, in order.** Take `before = AwayProcess.TopLevelWindows()`. For each item, first look for what is already open:
   - **Tracked:** live tracked windows.
   - **App, not a forwarder:**
     - a switchable window whose `AppIdentity.OwnerOf(h).Path` equals the exe (case-insensitive) and whose process arguments equal the item's arguments (`AwayProcess.ArgumentsOf(AwayProcess.CommandLine(pid))`, trimmed, case-insensitive). An unreadable command line counts as a match, the same rule as `AwayReopenJob.IsOpen`, `AwayReopen.cs:84-126`;
     - packaged apps match by `WinApi.GetWindowAumid(h) == aumid`.
   - **Path (folder):** an Explorer window from `Shell.Application.Windows()` whose `LocationURL` maps to the folder. Use dynamic COM as in `Trayloom.Features.Files/ExplorerShell.cs:171-210`.
   - **Web:** tracked windows only. Trayloom cannot know which tabs are open in windows it did not open itself.

   **If something is open:** restore it without activation if it is minimized (`SW_SHOWNOACTIVATE`), then apply the zone.

   **Otherwise, launch:**
   - **App:** `ProcessStartInfo(exe) { UseShellExecute = false, Arguments = args, WorkingDirectory = dir or the exe's folder }`, with the `TRAYLOOM_*` environment variables removed, as `AwayReopen.cs:226-238` does.
     - On `Win32Exception` 740: fail with "needs administrator".
     - Exe missing: fail with "isn't installed there anymore".
     - Packaged: `explorer.exe "shell:AppsFolder\<aumid>"`.
   - **Web:**
     - resolve the browser: for `"default"`, read `HKCU\Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice` `ProgId`, then `HKCR\<ProgId>\shell\open\command`, first token;
     - **Chromium family** (chrome, msedge, brave, vivaldi): `UseShellExecute = false`, `ArgumentList = ChromiumArgs(profile, urls)`. One launch gives one new window with all the tabs;
     - **Firefox:** best effort. `-new-window url1`, then `-new-tab urlN` per remaining URL with 400 ms between launches;
     - **Other browsers:** `UseShellExecute = true, FileName = url` per URL, 300 ms apart. Every URL goes through `NormalizeUrl` again first.
   - **Path:**
     - a folder opens with `explorer.exe "<folder>"` (`UseShellExecute = false`);
     - a file opens with `UseShellExecute = true, Verb = "open"`, and is refused if `IsBlockedFile`.

   **Claiming the new window(s).** Poll every 100 ms. A window counts when it is in `TopLevelWindows`, was not in `before`, is not already claimed, is visible, unowned, titled and not a tool window, and belongs to the item:
   - same owner exe, or the same AUMID;
   - for Chromium, also the main process (no `--type=`) with the same `--user-data-dir`, using the rule of `AwayReopenJob.Belongs`, `AwayReopen.cs:332-361`;
   - for a path item, owner `explorer.exe`.

   Stop when the expected count is reached, after 1.5 s with no new window after the first, or at the budget: apps 15 s, web 10 s, path 8 s.

   A forwarder or any single-instance app may open no new window. If the foreground window becomes one of that exe's windows within the budget, claim it with `OpenedByMode = false`. If nothing appears, the item fails with "didn't open a window".

   **Zone.** Use a Modes-owned `WindowLayoutEngine(app, log, ui)`, created once, used only on this thread, after `WindowTools.Keys.Register(app)`:
   - `var a = LayoutActions.Get(zone)`;
   - `maximize` → `ShowWindowAsync(h, SW_MAXIMIZE)`;
   - otherwise `frame = engine.FrameFor(a, WindowLayoutEngine.WorkAreaOf(h))`; if `!WindowLayoutEngine.Near(WinApi.VisibleFrame(h), frame)`, call `engine.PlaceVisible(h, frame, a.Raw)`.

   Do not use `Apply`: it cycles sizes on a repeat. The frame always lies inside the window's monitor work area, so no window can end up off-screen. Nothing is ever hidden, made transparent or parked off-screen, so the window guardian has nothing to track (CLAUDE.md "Foreign windows"; `WindowGuardian.cs:12-16` covers "a state the person cannot see"; minimized windows stay on the taskbar).
4. **Focus.** Call `WinUtil.Activate(frontHwnd)` (`Common.cs:16-55`) on the item with `Front`. Other windows are never activated.
5. **Finish**, on the UI thread:
   - `Active = target`, save `modesActiveId`, store `LastSummary`;
   - queue a notice when the trigger was not `tile`, or when anything failed;
   - log `[modes] enter <modeId> (<trigger>): <n> items, opened <o>, focused <f>, failed <x>, in <s> s`. Mode ids only: no names, paths or URLs;
   - raise `Changed`, then run the pending request if there is one.

**`Leave(trigger)`:** run step 1 with `entering = null`, deactivate keep awake if the mode turned it on, clear `Active` and `modesActiveId`, notify.

**`IAwayWorkspace` in `ModesAwayBridge.cs`:**
- `ActiveAppsToClose()` returns, normalized and lower-cased:
  - the `Exe` of app items, except packaged ones and `NeverClosable` ones;
  - the resolved browser exe of web items, only if `AwayBrowsers.KindOf` returns `Browser`.
- `OnAwayReturned(info)` replaces tracked handles using `info.Replaced` and drops tracked records that are no longer valid. It launches nothing.

**`ModesFeature`** (Id `"modes"`):
- **Start:**
  - `ModesKeys.Register`; `ModesStore.Load`;
  - `Services.Provide(runtime)` and `Services.Provide<IAwayWorkspace>(runtime)`;
  - for each mode, `Shortcuts.DefineRole(new ShortcutRole(RoleId(id), "modesShortcut." + id, Loc.F("Mode: {0}", name), "modes", FeatureGroup.Tools, null, ["modesEnabled"], Section: Loc.T("Modes")))` and `Bind(..., () => runtime.Enter(id, "shortcut"))`;
  - rebuild these after any add, rename or delete.
- **Stop:**
  - withdraw both services and dispose the bindings;
  - wait for the switch thread (`Join(3000)`);
  - deactivate keep awake if a mode turned it on;
  - never minimize or close anything.
- **Delete a mode:** `Shortcuts.RemoveRole(roleId)` and `App.Reset("modesShortcut." + id)`.

**New Core API** (`Shortcuts.cs`): add `void RemoveRole(string roleId) { }` to `IShortcutService` as a default interface member. Implement it in `ShortcutService`:
- under `_gate`, remove the role from `_roles` and `_byId`;
- dispose the registrations of its bindings and remove them from `_bindings` and `_runtime`;
- raise `Changed(roleId)`.

There is no remove API today (`:119-157`, `AddRole` `:346-360`).

**`Actions`**, evaluated live:
- `mode.<id>`: `Loc.F("Mode: {0}", name)`, with the mode's glyph, calling `Enter(id, "command-bar")`;
- when a mode is active, `leave`: `Loc.F("Leave mode: {0}", name)`;
- `save-current`: `Loc.T("Save what’s open now as a mode")`. It opens Settings at `quick-tools#modes` and the capture window, through `Shell.OpenSettings("quick-tools#modes")` plus a static request flag that the settings section reads on load;
- with `TRAYLOOM_DEV=1`: `dump` (counts only, written to the log) and `dev-capture` (logs capture counts by kind).

**Catalog line**, after `FeatureCatalog.cs:119`:

```csharp
list.Add(Def("modes", T, Loc.T("Modes"), Loc.T("One-click workspaces: open the apps, folders and browser tabs of a task, like coding or studying, and put the others away."), "\uE7C4", Idle, "A", false, "quick-tools#modes", ["modesEnabled"]));
```

### 2.5 Capturing what is open now (`ModesCapture`, `ChromiumSession`)

`CaptureAsync` runs on an STA worker:

1. **Group windows by app.** Take every `WindowEnumerator.IsSwitchable` window (minimized ones included), excluding Trayloom's own process. Group by `AppIdentity.OwnerOf(h)`.
   - Skip helper processes (`AwayApps.IsHelperCommand`) and the shell. `explorer.exe` windows become **folder** items instead, from `Shell.Application` `LocationURL` values that are `file:` URLs.
   - Packaged apps give app items with `Aumid` (`AwayProcess.Aumid(pid)`).
   - Other apps give app items with `Exe` and `Args = AwayProcess.ArgumentsOf(CommandLine(pid))`. Note: all VS Code windows share one main process, so VS Code becomes one item, and its folder must be chosen in the editor.
   - Zone: the first window's visible frame is compared (with `Near`) to `FrameFor` of each Halves, Thirds or Corners action on its work area; `IsZoomed` gives `maximize`.
2. **Chrome and Edge** (`AwayBrowsers.KindOf == Browser`): per running main process, `udd = AwayBrowsers.ResolveUserDataDir(exe, UserDataDirOf(cmd))`.
   - Profiles: the `--profile-directory` switch if present, else `Local State` → `profile.last_active_profiles`, else `"Default"`.
   - For each profile, parse the newest `Sessions\Session_*` file (by `LastWriteTimeUtc`), or `Current Session` on old versions. Each parsed window gives one web item: `name` = `Loc.F("{0} window {1}", browserName, n)`, URLs normalized, with `Detail` = `Loc.F("{0} tabs", count)`.
   - On a parse failure, the item becomes an app item plus the warning `CaptureTabsFailed`.
3. **Other browsers** become app items plus the warning `CaptureTabsUnsupported`.
4. **Result.** Dedupe all items, cap at 20, and return a `ModeCapture`. Nothing is saved until the person confirms in the dialog.

**`ChromiumSession.Parse(ReadOnlySpan<byte> data) → List<SessionWindow>?`** (`record SessionWindow(int Id, List<string> Urls)`). It is pure and **fails closed**: any inconsistency returns null. The format is reverse-engineered: Chromium's `components/sessions` SNSS format, stable for years but not a public API.

**Header.** Bytes `S N S S`, then an int32 little-endian version. Accept only 1 and 3; 2 and 4 are encrypted.

**Records** follow from offset 8:
- a uint16 LE `size`, then a byte `id`, then `size − 1` bytes of payload;
- stop at a truncated tail, which is normal while Chrome writes;
- skip unknown ids, including the marker 255.

| id | Meaning | Payload (int32 LE unless noted) |
|---|---|---|
| 0 | SetTabWindow | window, tab |
| 2 | SetTabIndexInWindow | tab, index |
| 5 | NavigationPathPrunedFromBack | tab, index: drop navigations with index ≥ index |
| 6 | UpdateTabNavigation | Pickle: uint32 payload size, int32 tab, int32 nav index, string URL (int32 byte length, UTF-8 bytes, padded to 4) |
| 7 | SetSelectedNavigationIndex | tab, index |
| 8 | SetSelectedTabInIndex | window, index (unused) |
| 9 | SetWindowType | window, type (keep only type 0, normal) |
| 11 | NavigationPathPrunedFromFront | tab, count: drop indices below count, shift the rest down by count |
| 16 | TabClosed | tab (first int32) |
| 17 | WindowClosed | window (first int32) |
| 24 | NavigationPathPruned | tab, index, count: drop [index, index + count), shift the higher indices down by count |

**Output.** Windows that are not closed and have type 0 (or no type). Tabs that are not closed, with their latest window, sorted by index. The URL is the navigation at the selected index, falling back to the highest index. Keep http and https only, normalized. Windows left with no URLs are dropped.

Titles are never read: parsing stops after the URL of command 6. Data is read with `FileShare.ReadWrite | Delete`, capped at 64 MB, held in memory only, and processed only on an explicit click.

**Honest limits of the tab reader:**
- Chrome writes the session file about every 2.5 s, so a tab opened just before the click may be missing.
- Private windows are never on disk, so they are never captured, which is correct.
- A future Chrome version could change the format; the parser then returns null and the UI falls back to "paste the addresses".

### 2.6 Surfaces and exact strings

`ModesStrings.cs` belongs to package B; `ModesUiStrings.cs` to package C.

**Island:** `ModesIslandSection` — Id `"modes"`, Title "Modes", Glyph `\uE7C4`, Order 125, FeatureId `"modes"`.
- Page: `ModeTilesView` in island style (white on dark, as `ScreenTimeIsland.cs`). A grid of 3 columns of 96×72 tiles: a coloured circle with the glyph, and the name (12, white) underneath.
- The active tile has a ring and the caption "Active". The tile being switched to shows "Opening…". A click calls `Enter(id, "tile")`.
- Footer pills: `Loc.F("Leave {0}", name)` when a mode is active, and "Save what’s open now…".
- Empty state: "No modes yet." with the button "Create a mode", which opens `quick-tools#modes`.
- Scrolls after 2 rows.
- `ModesNoticeStrip` — Id `"modes.notice"`, Order 46: `Loc.F("{0} is ready", name)` or `Loc.F("{0}: {1} didn’t open", name, n)`. Clicking it opens the `modes` page (`IslandCore.cs:641-650` handles that).

**Tray:** `ModesPanelSection` — `TabId = PanelTabs.Toggles`, Order 35, a `PanelRowGroup`.
- One `PanelActionRow(glyph, name, caption)` per mode. The caption is "Active", or `Loc.F("{0} items", n)`, or "Empty". The click is `context.InvokeAfterClose(() => runtime.Enter(id, "panel"), 200)`.
- When a mode is active, an extra row `Loc.F("Leave {0}", name)`.
- With no modes, a single row "Create a mode" → `Shell.OpenSettings("quick-tools#modes")`.
- Use AutomationIds `Modes.Panel.<id>` through a landmark, as `AwayPanelSection.cs:73-86` does.

**Panel utility:** `ModesUtility : IPanelUtility` — Id `"modes"`, Title "Modes", Caption "Open the apps and tabs of a task in one click.", `SettingsDestination` `"quick-tools#modes"`. `CreateHostedView` returns `ModeTilesView` in light style, with each pick doing `context.InvokeAfterClose(() => Enter(id, "quick-panel"), 200)`.

**Quick panel** (C):
- add `new("modes", "modes", Loc.T("Modes"), "\uE7C4")` to `QuickItems.All` (`QuickPanelItems.cs:24-41`);
- in `QuickPanelWindow.RunItem` `:863`, add `or "modes"` to the hosted case, which uses `UtilityProviders.For("modes")`.

**Command Bar** (C), in `BarCatalog.Build` after `:167`:

```csharp
if (Has("modes"))
{
    foreach (var (_, a) in ctx.Actions.All.Where(x => FeatureCatalog.Resolve(x.Feature.Id) == "modes"))
    {
        if (a.Id.StartsWith("mode.", StringComparison.Ordinal))
            rows.Add(Simple("action.mode." + a.Id[5..], a.Title, Loc.T("Modes"), 0xE7C4, a.Invoke, 200, Loc.T("Mode workspace switch open apps tabs coding study")));
        else if (a.Id == "leave")
            rows.Add(Simple("action.modeLeave", a.Title, Loc.T("Modes"), 0xE89F, a.Invoke, 200, Loc.T("Mode leave workspace")));
        else if (a.Id == "save-current")
            rows.Add(Simple("action.modeSave", a.Title, Loc.T("Modes"), 0xE74E, a.Invoke, 150, Loc.T("Mode save workspace capture tabs")));
    }
}
```

**Settings** (C): `ModesSettingsSection` — `PageId = QuickTools`, Anchor `"modes"`, Title "Modes", Order 85, which places it before Away mode at 90. Keywords: `workspace, coding, study, tabs, browser, apps, switch, focus, mode, shortcut`.

The card contains:
- a switch "Modes" with the caption "One click opens the apps, folders and browser tabs of a task, like coding or studying, and puts the others away." (`Modes.Enabled`);
- the mode list. Each row shows the colour and glyph, the name, and a caption: "Active", or `Loc.F("{0} items", n)` plus " · " plus the shortcut text if there is one. Buttons on each row:
  - "Open";
  - "Edit";
  - "Move up" / "Move down" (`ShortcutUi.SmallIconButton` `\uE70E` / `\uE70D`);
  - "Delete", which turns into inline confirmation text `Loc.F("Delete the mode “{0}”? Its apps and tabs stay as they are.", name)` with the buttons "Delete" and "Cancel";
- the buttons "New mode" and "Save what’s open now…";
- the empty state "No modes yet. Start with what’s open now, or build one item by item.";
- the caption "Trayloom never closes a window that asks to save something, and never closes File Explorer or terminals.";
- when elevated, an orange line: "Not available while Trayloom runs as administrator: the apps would open as administrator."

**Editor** (C): `ModeEditorWindow`, 560 wide, height sized to content with a maximum of 720 and a scroll area, styled like `Trayloom.Features.Input/SnippetEditorWindow.cs`. Contents:
- Title "New mode" or "Edit mode".
- "Name", with placeholder "Coding". Validation: "Give the mode a name." and "Another mode already has this name."
- "Colour" (12 swatches) and "Icon" (12 glyphs).
- "What opens":
  - item rows: icon, name, detail, a "Place" combo, a "Bring to front" radio, "Move up", "Move down", "Remove";
  - "Place" options: "Where it opens" (null), "Maximize", "Left", "Right", "Left 1/3", "Center 1/3", "Right 1/3", "Left 2/3", "Right 2/3", "Top left", "Top right", "Bottom left", "Bottom right". Use the `LayoutAction.Title`s.
- Add buttons:
  - "Add an app…": `new AppPickerWindow(null, [], allowBrowse: true)` (`AppList.cs:19`), whose `Result` is a lower-cased exe;
  - "Add a folder…": `Microsoft.Win32.OpenFolderDialog`;
  - "Add a file…": `OpenFileDialog`. A blocked extension gives "Add programs and scripts with “Add an app…”.";
  - "Add web pages…".
- On an app row, the button "Open with folder…" uses `ArgsForFolder`.
- "Add web pages…" opens an inline panel:
  - "Browser": "Default browser", then the installed browsers;
  - "Profile": Chromium only, from `Local State` names;
  - a multi-line box "Paste addresses, one per line";
  - the button "Add current browser tabs", which reads that browser and profile and shows a checklist per window;
  - messages: `Loc.F("{0} addresses were not web pages and were left out.", n)`, "Trayloom can read tabs from Chrome and Edge only.", and `Loc.F("Couldn’t read the tabs of {0}. Paste the addresses instead.", name)`;
  - the caption "Tabs are read from the browser’s own session file on this PC, only when you press this button. Nothing is sent anywhere."
- "When I leave this mode": "Leave its windows as they are" / "Minimize its windows" / "Close the windows it opened". The close option's caption: "A window that asks to save something stays open for you to answer. Everything else of this mode is minimized."
- "When this mode starts":
  - "Keep awake": "Off" / "1 hour" / "2 hours" / "Until I leave the mode". Disabled when Keep awake is not installed, with the caption "Install Keep awake in Features to use this.";
  - "Volume": "Leave as it is" / "Mute" / "25%" / "50%" / "75%" / "100%";
  - "Open the island timer", shown only when `notch` and `notch-timer` are installed;
  - the caption "Windows has no setting apps can use to switch on Do not disturb, so Modes can’t do it for you."
- "Shortcut": `ShortcutUi.LabelledRow(shortcuts, RoleId(id), "\uE765", Loc.T("Shortcut"), clearable: true)` (`ShortcutUi.cs:98`). For an unsaved mode, show "Save the mode first to give it a shortcut."
- Buttons "Save" (prominent) and "Cancel".

**Capture dialog** (C): `ModeCaptureWindow` "Save what’s open now".
- Caption: "Choose what this mode should open. Private windows are never saved."
- Name field, default "My mode".
- Checklists grouped under "Apps", "Browser windows" (each expands to show the URLs' host and path) and "Folders". Each line shows a detail such as `Loc.F("{0} windows", n)` or the zone title.
- Warnings appear as orange lines.
- Buttons "Save mode" and "Cancel". "Save mode" calls `runtime.Add(...)`.

### 2.7 Edge cases

- **Two modes share an item** (same key): switching leaves it alone.
- **Same app, different folders** (VS Code): different keys and separate windows; each mode tracks its own.
- **A request during a switch:** the last request wins once the current switch is done. Processes already launched are not cancelled.
- **The mode is deleted while active:** `Active` is cleared, the shortcut role removed, tracking dropped.
- **After a Trayloom restart:** `modesActiveId` is kept but tracking is lost. Leaving minimizes the item exes' windows only and never closes them. Web items open a new window again; this is stated in the UI.
- **Chrome starts with a web item while not running:** Chrome may also restore its own session (if "Continue where you left off" is set) next to the mode's new window.
- **Edge's "Close all tabs?" or a page's "Leave site?" when closing:** treated as a prompt and left for the person to answer.
- **Away mode while a mode is active:** with scope "mode", Away closes the mode's apps and reopens them. Modes refuses to switch while away.
- **Elevated Trayloom:** Modes refuses, the same rule as Away (`AwaySettings.cs:161`).
- **Multiple monitors:** a zone applies on the monitor where the window opens. A choice of monitor is out of scope.
- **Modes cannot:**
  - close individual tabs (that would need input injection or a browser extension);
  - set Do not disturb;
  - change the per-app volume (master volume only);
  - recognise tabs it did not open.
- **Logs:** never mode names, paths, URLs, profile names or titles. Only ids, kinds and counts.

### 2.8 Harness: `tests/Trayloom.Modes.Harness`

**Round trip and sanitising:**
- Build a `ModesFile` covering all kinds, serialize, parse and sanitize: the result is equal.
- Unknown kind dropped; 13 modes capped to 12; duplicate ids get `-2`; two `Front` items reduced to the first.
- A bad colour becomes `blue`; a bad zone becomes null.
- An unreadable file: `ModesStore` renames it to `.bad-*` and returns empty. Run this in a temporary `TRAYLOOM_DATA`.

**URLs:**
- `NormalizeUrl`: `github.com/x` → `https://github.com/x`; `javascript:alert(1)` → null; `https://u:p@h/` → null; `chrome://settings` → null; `HTTP://Example.com/A` → `http://example.com/A`; 2 049 characters → null; a URL with a space is percent-encoded.
- `ParseUrlList`: mixed newlines and spaces, duplicates removed with the first kept, `rejected` counted, 60 entries capped to 50.
- `GroupUrls`: 120 URLs give windows of 50, 50 and 20.
- `ChromiumArgs`: with and without a profile; the order is profile, `--new-window`, then the URLs.

**Keys and dedupe:**
- `ItemKey` ignores case for the exe and arguments and keeps the URL order.
- `Dedupe` keeps the first.
- `ToPutAway`: shared items are excluded; with `entering` null, all items are returned.

**Other rules:**
- `ArgsForFolder` for `Code.exe`, `wt.exe`, `explorer.exe` and `notepad.exe`.
- `IsBlockedFile` for `.ps1`, `.LNK` and `.pdf`.

**`ChromiumSession`** (the harness writes synthetic SNSS bytes with a small writer):
- 2 windows and 3 tabs: the right URLs in index order.
- A closed tab or window is excluded; a popup window (type 1) is excluded.
- The selected navigation index is honoured; each prune command shifts indices correctly.
- A truncated tail still parses.
- Versions 2 and 4 → null; a bad magic → null; a 0-length record stops parsing.
- `chrome://newtab` is filtered out.

### 2.9 Manual tests for Modes (`TESTING.md`)

1. **Capture.** With VS Code, Windows Terminal and Chrome (2 windows, about 8 tabs) open, use "Save what's open now": all three appear, Chrome as 2 browser windows with the right tab counts, no private window, zones detected.
2. **Enter Coding from the island tile:**
   - VS Code opens or focuses the folder;
   - Terminal opens in the folder;
   - one new Chrome window holds all the tabs, placed in its zone;
   - VS Code is in front;
   - entering again opens no duplicates.
3. **Switch to Study with Coding's "When I leave" = Minimize:** Coding's windows minimize, shared apps stay. With "Close": an unsaved VS Code file shows VS Code's own save prompt and Trayloom leaves it alone; Edge's "Close all tabs?" stays for you to answer.
4. **Other surfaces:** Command Bar "Mode: Coding", the Quick panel tile, the Quick toggles row, and a per-mode shortcut (also check a shortcut conflict shows its message). Delete the mode: its shortcut disappears from Keyboard shortcuts.
5. **Away mode with "Which apps" = the active mode's apps:** walk away, then come back. The mode's apps and Chrome tabs come back. Then press the mode tile: no duplicate windows.
6. **Pasted addresses:** a `javascript:` line is rejected with the count message.
7. **Firefox as default browser:** tabs open (best effort).
8. **Elevated Trayloom:** Modes refuses with the message.
9. **Logs:** `Select-String` the log for `\\`, `http`, the mode names: no hits.

---

## 3. Work packages for Sonnet

Before dispatching, save this design as `specs/08-modes-and-camera-leave.md`. Specs are the source of truth per CLAUDE.md.

All packages follow CLAUDE.md:
- the file header, Allman braces, `Loc.T` for every string, UTF-8 without BOM;
- no `SendInput` or other synthetic input; no new NuGet packages;
- commit only when asked, and per CLAUDE.md **no Claude/AI attribution lines in commits or PRs**.

Verify each package with `dotnet build Trayloom.sln -c Debug` and its harness (`dotnet run --project tests/...`).

**Package A: Away mode camera leaving.** Independent; start first or in parallel with B.
- **New files:** `AwayMode/AwayPresenceMath.cs`, `AwayMode/AwayPresenceClock.cs`, `AwayMode/AwayController.Leave.cs`, `AwayMode/AwayIsland.cs`, `AwayMode/AwayWorkspace.cs` (the contract in 1.8, verbatim), `tests/Trayloom.Away.Harness/{Trayloom.Away.Harness.csproj, Program.cs, PresenceChecks.cs}`.
- **Edits:** `AwayController.cs` (partial plus the hooks in 1.5 and 1.7), `AwayFaceWatcher.cs`, `AwayPresence.cs`, `AwayModeFeature.cs` (keys, dev actions), `AwaySettings.cs`, `AwayPanelSection.cs`, `AwayStrings.cs`, `src/Trayloom.Core/Catalog/FeatureCatalog.cs` (the away-mode description only), `src/Trayloom.Features.Windows/Trayloom.Features.Windows.csproj` (both `InternalsVisibleTo` entries).
- **Done when:**
  - the harness passes;
  - with `TRAYLOOM_DEV=1 TRAYLOOM_AWAY_NO_CAMERA=1 TRAYLOOM_AWAY_ARM_SECONDS=5`, `--action away-mode simulate-absence` repeated over 30 s gives the countdown log and then `[away] away (camera 30 s)`;
  - `simulate-motion` during the countdown cancels it.

**Package B: the Modes engine and quick access.** Parallel with A, except for the last step.
- **New files:** all the B files in 2.1 (`ModesModel`, `ModesMath`, `ModesStore`, `ChromiumSession`, `ModesBrowsers`, `ModesWindows`, `ModesSwitcher`, `ModesCapture`, `ModeVolume`, `ModesRuntime`, `ModesFeature`, `ModesIsland`, `ModesPanel`, `ModesStrings`), plus `Modes/ModesAwayBridge.cs` and `tests/Trayloom.Modes.Harness/*`.
- **Edits:** `Core/Catalog/FeatureCatalog.cs` (the `modes` line only), `Core/Contributions.cs` (gate lists), `Core/Shortcuts.cs` (`RemoveRole`).
- **Last step,** after A has merged: `ModesAwayBridge.cs` implements `IAwayWorkspace`, and `Services.Provide<IAwayWorkspace>(runtime)` is added. B must **not** create `AwayWorkspace.cs`.
- **Done when:**
  - the harness passes;
  - `--action modes dev-capture` logs counts only;
  - a hand-written `modes.json` mode can be entered from the island and the Quick toggles tab.

**Package C: Modes settings, dialogs, Tools surfaces, docs.** Starts after B merges.
- **New files:** `Modes/ModesSettings.cs`, `Modes/ModeEditorWindow.cs`, `Modes/ModeCaptureWindow.cs`, `Modes/ModesUiStrings.cs`.
- **Edits:**
  - `Trayloom.Features.Tools/CommandBar/BarCatalog.cs`, `QuickPanel/QuickPanelItems.cs`, `QuickPanel/QuickPanelWindow.cs`;
  - `README.md`: the Away mode table row and paragraph, a Modes row, and a privacy line about the tab reader;
  - `TESTING.md`: the manual tests in 1.11 and 2.9;
  - `docs/DEVELOPMENT.md`: the new dev actions and the `TRAYLOOM_AWAY_ARM_SECONDS` variable.
- **Uses only:** the `ModesRuntime` API (2.4), `ModesMath`, and the `Mode` / `ModeItem` types.
- **Done when:** `--open-settings quick-tools#modes` shows the card; creating, editing, reordering, deleting and capturing work; and UI Automation finds `Modes.Enabled`, `Modes.Row.<id>` and `Modes.New`.

**Order:** A and B run in parallel. B's bridge step comes after A. C comes after B. The packages touch disjoint files, except `FeatureCatalog.cs`: A edits line 119 and B adds a new line after it, which merges cleanly.

### Critical Files for Implementation
- E:\REA TEST 1\trayloom\src\Trayloom.Features.Windows\AwayMode\AwayController.cs
- E:\REA TEST 1\trayloom\src\Trayloom.Features.Windows\AwayMode\AwayFaceWatcher.cs
- E:\REA TEST 1\trayloom\src\Trayloom.Features.Windows\AwayMode\AwayPresence.cs
- E:\REA TEST 1\trayloom\src\Trayloom.Features.Windows\AwayMode\AwayReopen.cs (rules for opening and window matching that Modes reuses)
- E:\REA TEST 1\trayloom\src\Trayloom.Features.Windows\WindowLayoutEngine.cs (`FrameFor`, `PlaceVisible`, `Near`, `WorkAreaOf` for zones)
- E:\REA TEST 1\trayloom\src\Trayloom.Features.Tools\CommandBar\BarCatalog.cs
- E:\REA TEST 1\trayloom\src\Trayloom.Core\Shortcuts.cs