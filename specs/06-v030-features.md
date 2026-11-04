# 06 - v0.3.0 features: Screen time, GitHub, Wi-Fi, Away mode

Trayloom-only features (Vorssaint has none of them), so this spec is the source of truth for their screens, strings, defaults and
behaviour. `02-settings.md` still wins on structure (catalog, pages, sidebar); `CONTRACTS.md` is the API. Every rule in `CLAUDE.md`
applies; the ones that matter most here are repeated in section 0.3. Release: Trayloom 0.3.0 (`Directory.Build.props` goes from
0.2.0 to 0.3.0 in the release commit, section 5.6).

## Table of contents
0. Decisions that apply to all four (read first)
1. Screen time (`notch-screen-time`) - package A
2. GitHub (`notch-github`) - package B
3. Wi-Fi (`notch-wifi`) - package C
4. Away mode (`away-mode`) - package D
5. Implementation split (four parallel packages, shared edits, release commit, verification)
6. Open questions and risks for the owner

---------------------------------------------------------------------------------------------------------------------

## 0. Decisions that apply to all four

### 0.1 Summary

| | Screen time | GitHub | Wi-Fi | Away mode |
|---|---|---|---|---|
| Catalog id | `notch-screen-time` (alias `screen-time`) | `notch-github` (alias `github`) | `notch-wifi` (alias `wifi`) | `away-mode` |
| RawId (auto) | `notchScreenTime` | `notchGithub` | `notchWifi` | `awayMode` |
| Group | Dynamic Island | Dynamic Island | Dynamic Island | Tools |
| Lives in project | `Trayloom.Features.Windows` (folder `ScreenTime/`) | new `Trayloom.Features.GitHub` | `Trayloom.Features.System` (folder `Wifi/`) | `Trayloom.Features.Windows` (folder `AwayMode/`) |
| Namespace | `Trayloom.Features.WindowTools.ScreenTime` | `Trayloom.Features.GitHub` | `Trayloom.Features.SystemTools.Wifi` | `Trayloom.Features.WindowTools.AwayMode` |
| Permissions | `A` | none | none (location is explained in place, 3.6) | `A,Ca` |
| Energy | Periodic | Periodic | Idle | Periodic |
| Default installed | false | false | false | false |
| Destination | `island#screenTime` | `island#github` | `island#wifi` | `quick-tools#awayMode` |
| Enabled keys | `notchScreenTimeEnabled` | none (connect flow) | none (on demand) | `awayModeEnabled` |
| Island page (module id) | `screenTime` | `github` | `wifi` | none |
| Island strips | `screenTime.resting` (idle content) | `github.notice` | none | none |
| Island Controls tile | no | no | `wifi` (opens the page) | no |
| Tray flyout | no | no | Network tab card | Quick toggles tab card |
| Command Bar rows | "Screen time today" | "Refresh GitHub status", "Open GitHub notifications" | "Turn Wi-Fi on/off", "Disconnect from Wi-Fi", "Wi-Fi settings" | "Go away now", "Restore windows" |
| Files | `%APPDATA%\Trayloom\notch-screen-time\` | `%APPDATA%\Trayloom\notch-github\` | none | `%APPDATA%\Trayloom\away-mode\` |
| Online | never | only after the user connects | never | never |

Why these homes:
* **Screen time and Away mode go into the Windows project** because both need what it already has: `AppIdentity.OwnerOf` (UWP frames
  resolve to the hosted app), `AppIdentity.AppName/IconOf`, `WindowEnumerator.Enumerate` (switchable windows in Z order, cloaked
  windows of other virtual desktops excluded), `WinUtil.ForceForeground`, `WinUtil.PrepareOnMonitorAt/SetBounds`, `AppBundleList`
  (app list editor), `WinApi`. They live in their own sub-folders and sub-namespaces so the two packages never touch the same file.
* **Wi-Fi goes into the System project** next to the network monitor; it needs nothing from other projects.
* **GitHub gets its own project** because it is the only online, credential-holding feature: the token code is in one small assembly
  that is easy to review, and only package B edits the solution, `Trayloom.App.csproj` and `App.FeatureAssemblies`.
* **None of them goes into the Island project.** The Island project only receives small, generic plumbing edits (section 0.4); the
  pages come in as `IIslandSection` / `IIslandStrip` contributions, exactly like AI Agents (`AgentsIsland.cs`).
* **Away mode is in the Tools group** next to Cleaning Mode (same kind of "the person is not using the PC right now" tool) and its
  settings card lives on the Quick tools page, so no new Settings page or sidebar entry is needed.
* **All four are `DefaultInstalled: false`.** A registered `featureAvailable.*` default of true would install them on everyone who
  updates from 0.2.0 (`AppSettings` line ~270 registers `DefaultInstalled` as the default). None of them is added to
  `FeatureCatalog.IslandInitialExtensions`.
* The aliases (`legacy:` argument of `Def`) exist so the dev hooks read naturally: `--action screen-time dump`, `--action github
  refresh`, `--action wifi scan`. They are never shown.

### 0.2 Shared conventions for the four packages

* **Header and style**: every new `.cs` file starts with the two-line GPL header from `CLAUDE.md`, file-scoped namespace, Allman
  braces, `_camelCase`, nullable on, XML `<summary>` on types and most members, comments that say why. UTF-8 without BOM. Edit with
  editor tools, never PowerShell `Set-Content`/`Out-File`.
* **Strings**: every user-visible string through `Loc.T("…")` / `Loc.F("…{0}", x)`, English verbatim from this spec (typographic
  quotes and apostrophes exactly as written here: “ ” ’ …). Strings live in one `*Strings.cs` per package. Never the Vorssaint name.
  Developer-only action titles (shown only with `TRAYLOOM_DEV=1`) are plain English, like the island's "Island page: …" actions.
* **Contribution pattern** (copy AI Agents): the `IFeature` builds a runtime object in `Start`, publishes it with
  `context.Services.Provide(runtime)` and withdraws it in `Stop`. Stateless contribution classes (`IIslandSection`, `IIslandStrip`,
  `ISettingsSection`, `IPanelSection`) find it with `context.Services.Get<T>()` and show a fallback view when it is null.
* **Island look**: island pages are drawn on black. Each package writes its own tiny kit (white text at opacity 1 / 0.9 / 0.6 /
  0.32, `Segoe UI Variable Text`, icons in `UiKit.IconFont`, pills with radius 10, list rows 30-34 px high, hover fill white 0.06,
  rounded cards with fill white 0.08 and radius 14). Model it on `AgentsUiKit.cs` (`AgentColors`, `AU.Text/Icon/Capsule`) but do not
  reference it (other project). Kit class names are unique per package: `ScreenTimeLook`, `GitHubLook`, `WifiLook`.
* **Island page size**: `ExternalPage` measures the view at the content width (about 504 at the default 560 island) and caps it at
  the budget (about 264 at the default size, 180 compact). Design for 264 high; anything longer scrolls inside the page
  (`ExternalPage.ScrollsContent` is already true). A control that belongs in the island's top row is offered through
  `IIslandTopRowHost.ClaimTopRow()` (Wi-Fi only).
* **Async**: no `_ = FooAsync()` without a top-level try/catch. Each package adds its own logging fire-and-forget helper (copy
  `Guarded.FireAndLog` from `IslandUi.cs`; name it `ScreenTimeGuard`, `GitHubGuard`, `WifiGuard`, `AwayGuard`). No
  `Dispatcher.Invoke` from async code already on the UI thread. WinRT `IAsyncOperation`s are awaited with `.AsTask()`.
* **Logging**: one prefix per package: `[screen-time]`, `[github]`, `[wifi]`, `[away]`. The log is personal data that people paste
  into issues, so **never log**: tokens or any HTTP header, notification/PR titles, repository names other than the count, SSIDs or
  BSSIDs, window titles, executable paths of other apps (screen time and away mode log counts only). Exceptions are logged with
  `ex.GetType().Name` and `ex.HResult`, plus `ex.Message` only where the message cannot contain those (WinRT and IO messages are
  fine; HTTP exception messages are not logged for GitHub).
* **Stop() undoes everything**: unhook, stop timers, close own windows, stop the camera, cancel work, kill child processes, restore
  windows (Away mode), withdraw services. Files that hold the person's data (history, cache, token) stay on `Stop()` at app exit and
  are removed only when the feature is uninstalled: in `Stop()`, `!context.Runtime.IsAvailable(CatalogId)` means "uninstalled"
  (the runtime flips `featureAvailable.*` before it stops the feature; verify this once with a log line).
* **Files** are written atomically (temp file in the same folder + `File.Move(temp, path, overwrite: true)`), never through the
  debounced settings store when they must survive a crash.
* **Developer actions** (`FeatureAction`s whose id starts with `dev-`, plus `dump`/`scan`/`simulate-*`) exist only while
  `Environment.GetEnvironmentVariable("TRAYLOOM_DEV") == "1"`, because every `FeatureAction` is also offered by the Radial menu and
  the island Tools. User-facing actions are always present while the feature runs.
* **UI Automation**: every interactive control named in this spec gets `AutomationProperties.AutomationId` (listed per feature) and
  an `AutomationProperties.Name` equal to its visible label, so it can be verified and driven with UIA patterns instead of input.

### 0.3 Safety rules for implementing and testing (from `CLAUDE.md`, made concrete)

1. **Never synthesize input** (no `SendInput`, `keybd_event`, `mouse_event`, `SendKeys`, no new `InputSender` use). Away mode and
   screen time read input state with `GetLastInputInfo` only; windows are handled with window APIs only. Tests use dev actions, UIA
   patterns (`InvokePattern`, `TogglePattern`, `ValuePattern`) or `--dev-key`.
2. **Never cut the agent's own network.** `--action wifi toggle-radio`, `wifi disconnect`, connecting to another network and the
   radio switch must **not** be exercised by an automated agent: they disconnect the machine the agent works through. They are on
   the person's manual test list (5.6, TESTING.md). Agents may run `wifi scan`, `wifi state`, `wifi dump`.
3. **Never minimize the person's real windows in a test.** Away mode's `simulate-away` works only when `away-mode-only.txt` exists
   (4.13) and then touches only windows of the processes listed there (the `AwayDummy` test process). `away-now` is a real user
   action; agents do not run it.
4. **Never turn the person's camera on in a test.** Face detection is verified with `--action away-mode simulate-face`; a real camera
   test is on the person's manual list.
5. **GitHub**: agents may connect the dev copy through the signed-in GitHub CLI (read-only calls on the person's account) but never
   create, paste or print a token, never write to GitHub (no mark-as-read, no comments), and never paste fetched data into chat,
   commits or issues.
6. Use a scratch `TRAYLOOM_DATA` and the instance isolation of section 5.0 (S0); never touch the installed copy in
   `%LOCALAPPDATA%\Programs\Trayloom` or the real `%APPDATA%\Trayloom`.
7. Before installing the island in a dev copy, keep it away from Apple Music: launch with `--no-onboarding`, then
   `--dev-install notch` only after seeding `"notchAppleMusicKeepHidden": false` in the scratch `settings.json` (`{"_app":{…}}`);
   do not play, pause or close Apple Music.

### 0.4 Island plumbing (what exists, and the small generic edits)

How contributed island content works today (read `IslandCore.cs` 330-600, `ClosedViews.cs` 64-100, `AgentsIsland.cs`):
* A contributed `IIslandSection` becomes a page through `PageFactory` → `ExternalPage`. Without a `ModuleDef` in
  `IslandCatalog.Modules` it gets a grey tile, no Ctrl+Alt key, no summary, and **it does not appear on Settings > Dynamic Island >
  Content** (that list only shows `ModuleDef`s). So each package adds one `ModuleDef` (section 5.5, distinct anchors).
* Strips: ids ending in `.resting` are idle content (only `agents.resting` is read today, by `ClosedViews.Resting`), ids ending in
  `.notice` are shown once as a 5 s notice by `IslandCore.PollExternalNotices`, ids ending in `.attention` pop out, every other strip
  id is a live activity after keep awake. Live activities are not used by any of the four (they would occupy the closed island all
  day).
* Problem 1: `PollExternalNotices` posts every `.notice` element as `NoticeEvents.Agents`, so it is suppressed when AI Agents is off
  and a click opens the AI Agents page. **Package B** adds `NoticeEvents.External` and routes `<owner>.notice` to the `<owner>` page.
* Problem 2: only `"agents"` is treated as "a page that exists only while its contributing project provides it". **Package B**
  generalises that with `IslandCatalog.IsContributed(id)` covering `agents`, `screenTime`, `github`, `wifi`.
* Problem 3: idle content choices are hard-coded. **Package A** adds the `screenTime` choice to `ClosedViews.Resting` and
  `Settings.Section.cs`.
* Problem 4: the island Controls page has no Wi-Fi tile. **Package C** adds the `wifi` `ControlDef` and its two dispatch lines.

Exact snippets are in section 5.5. These are the only Island-project edits; no package edits `PageFactory`, `IslandWindow.State.cs`
or `Prefs.cs`. New settings keys are registered by the new features themselves, not in `Island/Prefs.cs`.

---------------------------------------------------------------------------------------------------------------------

## 1. Screen time (`notch-screen-time`) - package A

"How long did I use the PC today, and on what?" Counts the time each app is in front while the person is actually using the PC,
per day, locally. Shown as an island page, as optional idle content of the closed island, and as a "Today" card in Settings.

### 1.1 Catalog entry

```csharp
list.Add(Def("notch-screen-time", I, Loc.T("Screen time"), Loc.T("See how long you used the PC today, and in which apps, in the Dynamic Island. App names and times stay on this PC."), "\uE916", Per, "A", false, "island#screenTime", ["notchScreenTimeEnabled"], legacy: "screen-time"));
```
First install switches `notchScreenTimeEnabled` on (catalog default). Counting runs while the extension is installed and enabled,
also while the island itself is off or uninstalled (so the numbers are right when the person turns the island on); the hub row says
nothing extra.

### 1.2 Settings keys (registered in `ScreenTimeFeature.Start` and in the settings section ctor)

| Key | Type | Default | Values | Effect |
|---|---|---|---|---|
| `notchScreenTimeEnabled` | bool | true | | Counting on/off. Off: tracker unhooked, page shows the Off state, existing history stays. |
| `notchScreenTimeIdleMinutes` | int | 2 | 1, 2, 5 | Time without keyboard/mouse input after which counting pauses. |
| `notchScreenTimeCountMedia` | bool | true | | Keep counting without input while the front app is full screen or presenting, or while any media session plays. |
| `notchScreenTimeKeepDays` | int | 7 | 1, 7, 30 | Day files older than this many days (today counts as day 1) are deleted at start and at each midnight. |

### 1.3 What counts (exact rules)

* **Sources**: `SetWinEventHook(EVENT_SYSTEM_FOREGROUND, …, WINEVENT_OUTOFCONTEXT)` (use `WinApi.SetWinEventHook`; keep the delegate in
  a field) for the moment the front app changes, plus a `DispatcherTimer` tick every 10 s. Both run on the UI thread (the hook is
  out-of-context, so its callback arrives through the message loop); the callback only records the new window and calls `Credit()`.
* **Credit(now)**: the time since the last mark is added to the app that was in front, if the PC was *active* during it, then the mark
  moves to `now`. One credit adds at most 30 s (anything longer is a sleep, a hang or a clock change and is dropped). A credit whose
  `now` is earlier than the mark (clock moved back) adds nothing and resets the mark.
* **Active** when all of: the session is not locked (`SystemEvents.SessionSwitch` lock/unlock), the session is on the console (not
  `ConsoleDisconnect`/`RemoteDisconnect`), the system is not suspended (`SystemEvents.PowerModeChanged`), and either
  * input idle time (`GetLastInputInfo`, `Environment.TickCount` difference, unsigned) is below `notchScreenTimeIdleMinutes`, or
  * `notchScreenTimeCountMedia` is on and (`SHQueryUserNotificationState` returns `QUNS_BUSY`, `QUNS_RUNNING_D3D_FULL_SCREEN` or
    `QUNS_PRESENTATION_MODE`, or a `GlobalSystemMediaTransportControlsSessionManager` session reports `PlaybackStatus == Playing`).
    The media manager is requested once (lazily, the first time input is idle) and its sessions are read only while idle.
* **The app** of the front window: `AppIdentity.OwnerOf(hwnd).Path` (lower-cased exe path; UWP frames resolve to the hosted app).
  Shell windows (`Progman`, `WorkerW`, `Shell_TrayWnd`, `Shell_SecondaryTrayWnd`, the Start/Search hosts `Windows.UI.Core.CoreWindow`
  owned by `SearchHost.exe`/`StartMenuExperienceHost.exe`) count as the pseudo app id `#shell`, shown as "Desktop and taskbar".
  `LockApp.exe` never counts. A window whose path cannot be read (elevated process) counts as `#unknown`, "Other apps". Trayloom's
  own windows count as Trayloom (its path) like any other app.
* **Window titles, URLs and document names are never read.**
* **Day**: local calendar day. A credit that crosses local midnight is split; at the first credit after midnight the previous day is
  flushed and the retention sweep runs.
* **Hourly buckets**: each credit also adds to `hours[localHour]` (split at hour boundaries), 24 ints in seconds.
* **First use**: the local time of the first counted second of the day ("since 08:41").
* Counting only happens while Trayloom runs; the settings caption says so.

### 1.4 Storage

Folder `context.DataDirectory` = `%APPDATA%\Trayloom\notch-screen-time\` (or under `TRAYLOOM_DATA`). One file per day,
`days\yyyy-MM-dd.json`, written atomically:

```json
{ "v": 1, "date": "2026-11-04", "first": "08:41", "total": 11520,
  "hours": [0,0,0,0,0,0,0,0,1800,3600, 0,0,0,0,0,0,0,0,0,0,0,0,0,0],
  "apps": [ { "id": "c:\\users\\me\\appdata\\local\\programs\\microsoft vs code\\code.exe", "name": "Visual Studio Code", "seconds": 5234 },
            { "id": "#shell", "name": "Desktop and taskbar", "seconds": 312 } ] }
```
* `total` = sum of `apps[].seconds` (also equal to the sum of `hours`). `name` is the display name at the time it was first seen
  (`AppIdentity.AppName`, so renamed apps keep a stable label for the day).
* Written: every 60 s while dirty, at day rollover, on lock, on suspend, on `SessionEnding`, and in `Stop()`. A crash loses at most
  60 s. A file that does not parse is renamed to `yyyy-MM-dd.bad.json` (kept, never retried) and the day starts from zero; log
  `[screen-time] day file unreadable, kept as .bad`.
* Retention: delete `days\*.json` (and `.bad.json`) older than `notchScreenTimeKeepDays`.
* "Clear history…" deletes every day file and resets today in memory (permanent deletion after a confirmation; there is nothing to
  recycle).
* Uninstall (`Stop()` with the feature no longer available): delete the whole `notch-screen-time` folder.

### 1.5 Island page (module `screenTime`)

`ModuleDef` (section 5.5): title "Screen time", glyph `G.Clock`, key `u` (Ctrl+Alt+U inside the island), tint `#30B0C7`, summary
"How long you used the PC today, and in which apps.". `IIslandSection`: `Id = "screenTime"`, `Title = Loc.T("Screen time")`,
`Glyph = "\uE916"`, `FeatureId = "notch-screen-time"`, `Order = 120`.

Layout, top to bottom (content width W ≈ 504, black background, padding 0 - the island adds its margins):
1. **Header row** (height 40): left a big total `3 h 12 min` (22 semibold, white). Under it (11, white 0.6) "Today", followed by
   " · " and the comparison when yesterday's file exists: `Loc.F("{0} more than yesterday", d)` / `Loc.F("{0} less than yesterday",
   d)` / "Same as yesterday" (difference under 1 min). Right-aligned (11, white 0.6): `Loc.F("Since {0}", first)` (`first` in the
   culture's short time).
2. **Hour bars** (height 44 + 14 for labels, margin-top 10): 24 equal columns with 2 px gaps, each bar's height = seconds/3600 of the
   44 px; bars tinted `#30B0C7`, the current hour at opacity 1, earlier hours 0.55, hours with 0 s a 2 px stub at white 0.12.
   Labels under the columns 0, 6, 12, 18: `DateTime.Today.AddHours(h).ToString(pattern)` where pattern is `"H"` when the culture's
   short time uses 24 h (pattern contains `H`), else `"h tt"`. Tooltip per column: `Loc.F("{0}: {1}", hourRange, duration)`.
3. **App list** (margin-top 10): up to 5 rows, 30 px each: app icon 18 px (`AppIdentity.IconOf`; `#shell` uses glyph `\uE80F`,
   `#unknown` `\uE74C`), name (12.5, white, ellipsis), a bar (height 4, radius 2, width proportional to the top app, tint at 0.8) under
   the name, duration right-aligned (11.5 mono digits, white 0.9, compact form `1h 05m`). Tooltip on the row: full name and long
   duration.
4. When there are more than 5 apps, a last row (11.5, white 0.6, hand cursor): `Loc.F("{0} more apps · {1}", n, duration)`; click
   expands the list in place to every app (the page then scrolls); the row becomes "Show fewer".

Durations: long form `Loc.F("{0} h {1} min", h, m)` (plain numbers, `3 h 5 min`), `Loc.F("{0} min", m)` under an hour, "Less than a minute" under 60 s; compact form `Loc.F("{0}h {1:00}m", h, m)` / `Loc.F("{0}m", m)` / "<1m".

States:
* **Off** (`notchScreenTimeEnabled` false): centred glyph `\uE916` 22 px white 0.5, "Screen time is off." (13), pill "Turn on"
  (sets the key).
* **Empty** (enabled, today has 0 s): glyph, "No screen time yet today." and caption "Use the PC for a minute and it shows up here.".
* **Runtime missing** (feature stopped): the `ExternalPage` fallback text "Screen time is not running.".

The view subscribes to `ScreenTimeRuntime.Changed` (raised at most every 10 s, after a credit that changed the visible minute) while
loaded and unsubscribes on `Unloaded`. It reads only in-memory data (never the disk) when shown.

AutomationIds: `ScreenTime.Total`, `ScreenTime.Hours`, `ScreenTime.AppList`, `ScreenTime.More`, `ScreenTime.TurnOn`.

### 1.6 Closed island: idle content "Screen time"

Strip `IIslandStrip` `Id = "screenTime.resting"`, `Order = 905`, `FeatureId = "notch-screen-time"`. Returns null unless the feature
runs, is enabled and `notchIdleContent == "screenTime"`. Otherwise a horizontal `StackPanel`: glyph `\uE916` 11 px tinted
`#30B0C7`, then the compact total of today (9 medium, white, mono digits), margin 5 between. It updates itself every 60 s
(`DispatcherTimer` started on `Loaded`, stopped on `Unloaded`) and calls `IIslandContext.Invalidate()` only when the text changes
length (the island re-measures, like `AgentStripView`). Package A wires it into `ClosedViews.Resting` and offers "Screen time" on
Settings > Dynamic Island > Activity > "At rest" (snippets in 5.5). The choice is listed while the `screenTime` section is visible
(or currently selected); when selected but hidden, the existing hint pattern shows "Show “Screen time” on the Content tab.". Sample
in the choice picture: "3h 12m".

### 1.7 Settings section (`island#screenTime`)

`ISettingsSection`: `PageId = SettingsPages.Island`, `Anchor = "screenTime"`, `Title = Loc.T("Screen time")`, `Order = 170`,
`FeatureId = "notch-screen-time"`, `Keywords = ["usage", "time", "apps", "today", "history"]`. One `SettingsCard("Screen time")`:

| Row | Control | Strings |
|---|---|---|
| `SettingsRow("\uE916", …)` | `MacSwitch` Regular → `notchScreenTimeEnabled` | "Count screen time" / caption "Counts the time each app is in front while you use the PC. App names and times stay on this PC; window titles are never read. Nothing is counted while Trayloom is not running." |
| `PickerRow<int>` (indented, shown when enabled) | 1/2/5 → `notchScreenTimeIdleMinutes` | "Stop counting after" / items "1 minute without input", "2 minutes without input", "5 minutes without input" |
| `SettingsRow` + `MacSwitch` (indented) | → `notchScreenTimeCountMedia` | "Count videos and full-screen apps" / caption "Watching a video or giving a presentation counts even when you do not touch the mouse or keyboard." |
| `PickerRow<int>` (indented) | 1/7/30 → `notchScreenTimeKeepDays` | "Keep history for" / items "1 day", "7 days", "30 days" |
| divider | | |
| `SettingsRow("\uE823", …)` | accessory: bordered button "Clear history…" | title "Today", caption = `Loc.F("{0} · {1}", total, top3)` where top3 is "Visual Studio Code 1 h 27 min, Microsoft Edge 52 min, …" (names joined with ", "), or "No screen time yet today." |

"Clear history…" → `MessageBox.Show(Loc.T("This deletes the screen time of every day kept on this PC."), Loc.T("Clear screen time
history?"), OKCancel, Warning, Cancel)`; OK clears (1.4) and rebuilds the section.

AutomationIds: `ScreenTime.Enabled`, `ScreenTime.IdleMinutes`, `ScreenTime.CountMedia`, `ScreenTime.KeepDays`, `ScreenTime.Today`,
`ScreenTime.Clear`.

### 1.8 Actions and Command Bar

`FeatureAction`s of `ScreenTimeFeature` (Id `notch-screen-time`):
* `today` - title `Loc.F("Screen time today: {0}", longTotal)` (re-evaluated each time the list is read), glyph `\uE916`; opens
  Settings at `island#screenTime`.
* dev only: `dump` ("Screen time: dump"), `flush` ("Screen time: flush"), `dev-add-minute` ("Screen time: add a minute to the front
  app": credits 60 s to the current front app, bypassing the active rules, then raises `Changed`).

`dump` writes `notch-screen-time\dump.json` = the in-memory day (same shape as the day file plus `"tracking": true|false`,
`"active": true|false`, `"activeReason": "input|media|fullscreen|idle|locked|suspended|off"`) and logs
`[screen-time] dump: total=11520 s, apps=7, active=input`.

Command Bar row (package A's block in `BarCatalog`, 5.5): id `action.screenTimeToday`, title = the action title, subtitle
`Area("notch-screen-time", …)`, glyph 0xE916, runs the action. The catalog toggle row "Screen time" (from the single enabled key) and
the "settings.feature.notch-screen-time" row come for free.

### 1.9 Edge cases

* Fast front-app changes (Alt+Tab through 10 windows): every event credits; credits under 1 s are kept (they add up).
* The hook misses an event (rare): the 10 s tick re-reads `GetForegroundWindow` and corrects the current app.
* Sleep/hibernate: `Suspend` credits up to the suspend and stops the mark; `Resume` sets a fresh mark (the gap is not counted).
* Time zone or manual clock change: covered by the 30 s cap and the backwards check; the day key follows the new local date.
* Two monitors / virtual desktops: only the foreground window matters.
* Remote Desktop into the PC: not on console → not counted.
* Day file of today exists from an earlier run: loaded and continued at start.
* `notchScreenTimeEnabled` turned off mid-day: credit up to now, flush, unhook; on again: re-hook with a fresh mark.
* Hundreds of distinct apps in a day: no cap needed (one entry each); the page shows 5 until expanded.

### 1.10 Failure states

* `SetWinEventHook` returns 0: log `[screen-time] foreground hook failed`, fall back to the 10 s tick only (accuracy ±10 s).
* Folder not writable: log once per run (`[screen-time] cannot write day file: <HResult>`), keep counting in memory, retry at the
  next flush.
* Media manager request throws (no SMTC on Windows N without media pack): treat as "nothing plays", log once.

### 1.11 Verification checklist (package A)

1. Full build passes; `--dump-info <file>` lists `notch-screen-time` with actions `today` (and the dev actions with `TRAYLOOM_DEV=1`).
2. `--dev-install notch-screen-time` → log `Started feature notch-screen-time`; `notch-screen-time\days\<today>.json` appears within
   60 s of normal use (or after `--action screen-time flush`).
3. `--action screen-time dev-add-minute` ×3 then `--action screen-time dump` → `total` grows by 180, log line present.
4. `--open-settings island#screenTime` → UIA finds `ScreenTime.Today` with a caption that contains the total; toggle
   `ScreenTime.Enabled` via TogglePattern → `dump` shows `"tracking": false`; toggle back.
5. Island installed in the dev copy: `--action island dev-open-screenTime` → `--shot` of the island shows the page; UIA finds
   `ScreenTime.Total`.
6. Settings > Dynamic Island > Activity: "Screen time" choice present; select it (UIA) → closed island shows the glyph and total.
7. Leave the PC idle for longer than the idle minutes (or set 1 and wait) → `dump` shows `active=idle` and the total stops growing.
8. Uninstall (`--dev-uninstall notch-screen-time`) → log "Stopped feature …", folder deleted, no hook left (log line from Stop).

---------------------------------------------------------------------------------------------------------------------

## 2. GitHub (`notch-github`) - package B

The current state of the person's GitHub account in the Dynamic Island: unread notifications, their open pull requests, pull
requests waiting for their review, failing checks and the latest releases of their repositories. Read-only. Online only after the
person connects.

### 2.1 Catalog entry

```csharp
list.Add(Def("notch-github", I, Loc.T("GitHub"), Loc.T("Your unread notifications, pull requests, reviews and failing checks in the Dynamic Island. Uses the GitHub CLI you are signed in to, or a token you paste. Off until you connect."), "\uE943", Per, "", false, "island#github", [], legacy: "github"));
```
No enabled key: installing the extension does nothing online; the connect flow (2.7) is the opt-in.

### 2.2 Authentication and secrets (exact design)

Two sources, tried in this order when the person presses a connect button; the chosen one is stored in `notchGitHubSource`.

**A. The GitHub CLI (preferred, `cli`).** Trayloom never sees a token: it runs the signed-in `gh` and gh uses its own credential
store.
* Finding gh: the first existing of `gh.exe` on each `PATH` entry, `%ProgramFiles%\GitHub CLI\gh.exe`,
  `%LOCALAPPDATA%\Programs\GitHub CLI\gh.exe`, `%ProgramFiles(x86)%\GitHub CLI\gh.exe`. Version from `gh --version` (first line,
  `gh version X.Y.Z`); cached for the run, re-checked when a connect button is pressed.
* Running gh (`GhCli.RunAsync`): `ProcessStartInfo` with `UseShellExecute = false`, `CreateNoWindow = true`, redirected stdout and
  stderr (UTF-8), arguments only through `ArgumentList` (never a shell, never string concatenation), working directory
  `Environment.SystemDirectory`. Environment = the inherited one plus `GH_PROMPT_DISABLED=1`, `GH_NO_UPDATE_NOTIFIER=1`,
  `GH_NO_EXTENSION_UPDATE_NOTIFIER=1`, `NO_COLOR=1`, `GH_PAGER=` (empty), `GH_SPINNER_DISABLED=1`, `CLICOLOR=0`. Timeout 20 s; on
  timeout or `Stop()` the process tree is killed (`Process.Kill(entireProcessTree: true)`). Output capped at 4 MB (more = error).
  Exit code 4 or stderr containing `gh auth login` = **signed out**.
* Every call passes `--hostname github.com` so a person's GitHub Enterprise default host is never used by accident.
* Check signed in: `gh auth status --hostname github.com` exit 0, then `gh api --hostname github.com user --jq .login` → login.
* Conditional REST through gh (verified with gh 2.102.0 on this machine): `gh api --hostname github.com -i -H "If-None-Match:
  <etag>" "notifications?per_page=50"`. With `-i` stdout starts with the status line (`HTTP/2.0 200 OK`), then headers, a blank line
  and the body. A 304 prints `HTTP/2.0 304 Not Modified` and **exits with code 1** (stderr `gh: HTTP 304`): parse the status line
  first and treat 304 as "not modified", not as a failure.
* GraphQL through gh: `gh api --hostname github.com graphql -f query=<text>` (the query as one `ArgumentList` item).

**B. A personal access token (fallback, `token`).**
* Entered only in Trayloom's own dialog (2.8) in a `PasswordBox`; never in a `TextBox`, never on a command line, never in an
  environment variable, never in `settings.json`, never logged, never copied to the clipboard, never shown again.
* Stored at `notch-github\token.bin` = `ProtectedData.Protect(UTF8(token), entropy: UTF8("Trayloom.GitHub.v1"),
  DataProtectionScope.CurrentUser)`. `System.Security.Cryptography.ProtectedData` comes with the Windows Desktop shared framework
  that every Trayloom project already references through `UseWPF`/`UseWindowsForms` (verified: `System.Security.Cryptography.
  ProtectedData.dll` is in `Microsoft.WindowsDesktop.App.Ref 9.0.20` and compiles against `net9.0-windows10.0.19041.0` with no
  package). No NuGet package.
* Decrypted only for the length of one poll; the plaintext string is not kept in a field between polls.
* HTTP: one static `HttpClient` (`Timeout` 20 s, `UserAgent "Trayloom/<assembly version> (+https://github.com/Naveen022006/Trayloom)"`).
  Every request is an `HttpRequestMessage` with `Authorization: Bearer <token>` set on the message (never on
  `DefaultRequestHeaders`), `Accept: application/vnd.github+json`, `X-GitHub-Api-Version: 2022-11-28`. Only `https://api.github.com`
  is ever called with the token; redirects to another host are not followed with the header (`HttpClientHandler.AllowAutoRedirect =
  false`; a 3xx is an error).
* Scope check on connect: `GET /user` → 200 required; `X-OAuth-Scopes` must contain `notifications` or `repo`, otherwise the
  dialog says the notifications line in 2.8 (fine-grained tokens cannot read `/notifications`; the Unread tile then shows "Needs a
  classic token" and the rest works).

**Disconnect** (button, or uninstall): cancel the poll, delete `token.bin` and `cache.json`, set `notchGitHubConnected=false`,
`notchGitHubSource=""`, `notchGitHubLogin=""`. Nothing is sent to GitHub (tokens are not revoked; the caption tells the person they
can revoke a token on GitHub).

### 2.3 Settings keys

| Key | Type | Default | Values | Effect |
|---|---|---|---|---|
| `notchGitHubConnected` | bool | false | | The only switch that allows network access. |
| `notchGitHubSource` | string | "" | "", "cli", "token" | Which source polls. |
| `notchGitHubLogin` | string | "" | | Display only ("@login"); refreshed on each successful poll. |
| `notchGitHubInterval` | int | 5 | 2, 5, 15, 30 | Minutes between polls. |
| `notchGitHubRepos` | string | "" | "owner/name, owner/name" (max 10) | Repositories for checks and releases; empty = the 5 repositories the person owns and pushed to most recently (not archived). |
| `notchGitHubNotices` | bool | true | | Island notices for new notifications, review requests and failed checks. |

### 2.4 What is fetched (two requests per poll)

1. **Notifications (REST, conditional)**: `GET /notifications?per_page=50` (unread only, the API default) with `If-None-Match` =
   the stored ETag. 200 → replace the cached list and ETag; 304 → keep the cache. Honour `X-Poll-Interval` (never ask again sooner).
   Each item keeps: `id`, `unread`, `reason`, `updated_at`, `subject.title`, `subject.type`, `subject.url`, `repository.full_name`.
   Count shown: the number of items, "50+" when 50 came back.
2. **Everything else (GraphQL, one query, cost 1 point - verified)**:

```graphql
query($repoCount: Int!) {
  viewer {
    login
    repositories(first: $repoCount, orderBy: {field: PUSHED_AT, direction: DESC}, ownerAffiliations: OWNER, isArchived: false) {
      nodes { ...Repo }
    }
  }
  mine: search(query: "is:open is:pr author:@me archived:false", type: ISSUE, first: 5) {
    issueCount
    nodes { ...Pr }
  }
  review: search(query: "is:open is:pr review-requested:@me archived:false", type: ISSUE, first: 5) {
    issueCount
    nodes { ...Pr }
  }
  rateLimit { cost remaining resetAt }
}
fragment Repo on Repository {
  nameWithOwner url
  defaultBranchRef { name target { ... on Commit { statusCheckRollup { state } } } }
  latestRelease { tagName name publishedAt url isPrerelease }
}
fragment Pr on PullRequest {
  number title url isDraft reviewDecision updatedAt
  repository { nameWithOwner }
  commits(last: 1) { nodes { commit { statusCheckRollup { state } } } }
}
```
   With `notchGitHubRepos` set, the `viewer.repositories` part is replaced by one aliased field per listed repository
   (`r0: repository(owner: "o", name: "n") { ...Repo }`, …; owner/name validated against `^[A-Za-z0-9-]{1,39}/[A-Za-z0-9._-]{1,100}$`
   before they are put in the text, so nothing user-typed can change the query). `$repoCount` = 5. Via gh: `-F repoCount=5`.
   `statusCheckRollup.state`: `SUCCESS` → Passing (green `#30D158`), `FAILURE`/`ERROR` → Failing (red `#FF453A`),
   `PENDING`/`EXPECTED` → Running (amber `#FF9F0A`), null → No checks (white 0.32).
   "Latest release" = the newest non-null `latestRelease` across the listed repositories.

Snapshot model (`GitHubSnapshot`): `Login`, `FetchedAt`, `Notifications` (list), `NotificationsAvailable` (false for fine-grained
tokens), `MineCount`, `Mine` (≤5), `ReviewCount`, `Review` (≤5), `Repos` (name, url, branch, state, release), `Error` (enum
`None, Offline, SignedOut, TokenRejected, RateLimited, GhMissing, Unexpected`), `RetryAt`.

Links: only `https://github.com/...` URLs are ever opened (`Uri` scheme `https`, host exactly `github.com`; anything else is
dropped). Notification `subject.url` (an API URL) maps to the web: `…/repos/{o}/{r}/pulls/{n}` → `https://github.com/{o}/{r}/pull/{n}`,
`…/issues/{n}` → `https://github.com/{o}/{r}/issues/{n}`, `…/releases/{id}` → `https://github.com/{o}/{r}/releases`, anything else
(discussions, check suites, null) → `https://github.com/notifications`. URLs are opened with
`Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })`.

### 2.5 Polling schedule

* Only while `notchGitHubConnected` is true and the feature runs.
* First poll 20 s after `Start` (not in the logon rush); then every `notchGitHubInterval` minutes (`System.Threading.Timer`, the
  work on the thread pool, one poll at a time behind a `SemaphoreSlim`).
* Paused while the session is locked or the system is suspended; on unlock/resume a poll runs at once if the last one is older than
  the interval.
* Opening the island page polls at once when the data is older than 2 minutes. "Refresh" (page button, Command Bar, `--action github
  refresh`) polls at once but at most once per 15 s.
* Errors back off: Offline/Unexpected → 2×, 4×, 8× the interval (max 60 min), reset after a success. RateLimited → wait until
  `X-RateLimit-Reset` / `Retry-After` / GraphQL `rateLimit.resetAt`. When `rateLimit.remaining` < 100 the interval doubles.
  SignedOut / TokenRejected → polling stops until the person reconnects or presses Refresh/Try again.

### 2.6 Storage

`notch-github\cache.json` (atomic): `{ "v":1, "login", "fetchedAt", "notificationsEtag", "pollInterval", "snapshot": {…the model,
notification titles and URLs included…} }`. Loaded at `Start` so the page has data before the first poll (marked "Updated
<time>"). Never contains a token or any header. `token.bin` as in 2.2. Both deleted on Disconnect and on uninstall.

### 2.7 Island page (module `github`)

`ModuleDef`: title "GitHub", glyph `G.Code`, key `h`, tint `#8250DF`, summary "Notifications, pull requests and checks of your GitHub
account.". `IIslandSection`: `Id = "github"`, `Title = Loc.T("GitHub")`, `Glyph = "\uE943"`, `FeatureId = "notch-github"`, `Order =
160`.

**Not connected**: centred glyph `\uE943` 22 px white 0.5, "Connect your GitHub account to see notifications, pull requests and
checks here." (13, wraps, max width 360), caption "Nothing is sent to GitHub until you connect." (11, white 0.6), pill "Connect…"
→ `Shell.OpenSettings("island#github")` and collapse the island (the island window cannot take text input, so connecting happens in
Settings).

**Connected** layout:
1. **Tiles row** (height 58): four equal tiles (radius 12, fill white 0.08, selected tile fill white 0.16 with a 1 px white 0.3
   border), each a big number (20 semibold, mono digits) over a label (10.5, white 0.6): "Unread" (notification count, "50+"), "Your
   PRs" (`mine.issueCount`), "To review" (`review.issueCount`, number tinted amber when > 0), "Failing" (failing repo count, tinted red
   when > 0). Click selects the tile; the selection is remembered for the run (default "Unread", or "Failing" when that count > 0
   at open).
2. **List for the selected tile** (margin-top 8, up to 5 rows of 34 px, the page scrolls beyond):
   * Unread: type glyph (PullRequest `\uE8A4`-like branch glyph `\uE8AD`, Issue `\uE7BA`, Release `\uE7B8`, Discussion `\uE8F2`,
     other `\uE7ED`; verify glyphs), title (12.5, ellipsis), caption `Loc.F("{0} · {1}", repo, relativeTime)` (11, white 0.6).
     Click opens the mapped URL. Empty: "No unread notifications." Fine-grained token: "Notifications need a classic token."
   * Your PRs / To review: check-state dot (6 px), title, caption `repo #n` + " · Draft" / " · Approved" / " · Changes requested" /
     " · Review required" (from `isDraft` / `reviewDecision`). Click opens the PR. Empty: "No open pull requests." / "Nothing waits
     for your review." When the count is larger than 5 a last row "Show all on GitHub" opens
     `https://github.com/pulls` (mine) or `https://github.com/pulls/review-requested`.
   * Failing: every watched repository: state dot, `nameWithOwner`, caption state word ("Passing", "Failing", "Running", "No checks")
     + (release) `Loc.F(" · {0} released {1}", tag, relativeTime)`. Click opens `{url}/actions` when failing or running, else
     `{url}`. Empty list: "No repositories to check." Header line above the list when every repo passes: "All checks pass."
3. **Footer** (height 24, 11, white 0.6): `Loc.F("@{0} · Updated {1}", login, relativeTime)`; right: refresh icon button (`\uE72C`,
   tooltip "Refresh", spins while polling) and "Open GitHub" link (`https://github.com/notifications` when Unread is selected, else
   `https://github.com`).
4. **Error line** (replaces the footer text, amber `#FF9F0A`): Offline "Offline. Showing what was loaded at {0}."; SignedOut "The
   GitHub CLI is signed out. Run “gh auth login”, then press Try again." (+ pill "Try again"); TokenRejected "GitHub no longer accepts
   the token. Connect again in Settings." (+ pill "Settings…"); RateLimited "GitHub asked to wait until {0}."; GhMissing "The GitHub
   CLI is no longer installed."; Unexpected "Could not update. Trying again at {0}.".

Relative time strings: "just now" (< 1 min), `Loc.F("{0} min ago", m)`, `Loc.F("{0} h ago", h)`, `Loc.F("{0} d ago", d)`; in the
release caption the same numbers without "ago" (`"2 d"`).

AutomationIds: `GitHub.Connect`, `GitHub.TileUnread`, `GitHub.TileMine`, `GitHub.TileReview`, `GitHub.TileFailing`, `GitHub.List`,
`GitHub.Refresh`, `GitHub.Status`.

### 2.8 Settings section (`island#github`) and the token dialog

`ISettingsSection`: `PageId = SettingsPages.Island`, `Anchor = "github"`, `Title = Loc.T("GitHub")`, `Order = 180`, `FeatureId =
"notch-github"`, `Keywords = ["pull requests", "notifications", "checks", "CI", "releases", "token", "gh"]`. One
`SettingsCard("GitHub")`.

Not connected:
* `SettingsRow("\uE943", "Not connected", "Nothing is sent to GitHub until you connect. Trayloom then asks every few minutes for
  your notifications, pull requests, checks and releases. It never changes anything on GitHub.")`.
* Button row (inset `SettingsRow.TextInset`): accent button "Connect with GitHub CLI" (enabled when gh was found) and bordered
  button "Use a token instead…". Under it a caption: gh found → `Loc.F("GitHub CLI {0} found.", version)`; not found → "GitHub CLI
  not found. Install it from cli.github.com, run “gh auth login” in a terminal, then come back." with a link button "cli.github.com"
  (`https://cli.github.com`).
* Pressing "Connect with GitHub CLI": button shows "Connecting…" and is disabled; runs the 2.2 A check off the UI thread; success →
  set `notchGitHubSource="cli"`, login, `notchGitHubConnected=true`, poll now, rebuild. Failure captions (orange): "The GitHub CLI is
  not signed in. Run “gh auth login” in a terminal, then try again." / "The GitHub CLI did not answer in time." / "The GitHub CLI
  could not be started.".

Connected:
* `SettingsRow("\uE943", Loc.F("Connected as @{0}", login), source caption)`: "Through the GitHub CLI. Sign out there with “gh auth
  logout”." / "With a personal access token, encrypted for your Windows account. You can revoke it on GitHub at any time."; accessory
  bordered button "Disconnect".
* Status caption (inset): `Loc.F("Updated {0}", relative)` or the error text of 2.7, plus bordered mini button "Refresh now".
* `PickerRow<int>` "Check every": "2 minutes", "5 minutes", "15 minutes", "30 minutes" → `notchGitHubInterval`.
* `SettingsRow("\uE8B7", "Repositories", "Checks and releases of these repositories. Leave empty for the 5 you pushed to most
  recently.")` with a `TextBox` (width 260, placeholder "owner/name, owner/name") → `notchGitHubRepos`, saved on lost focus/Enter
  after validation; invalid → orange caption "Use owner/name, separated by commas (up to 10)." and the old value stays.
* `SettingsRow("\uE7ED", "Show alerts in the island", "New notifications, review requests and failed checks appear in the closed
  island for a moment.")` + `MacSwitch` → `notchGitHubNotices`.

Token dialog `GitHubTokenWindow` (Trayloom-owned WPF `Window`, `ThemeManager.BindWindow`, 440 wide, `SizeToContent=Height`, centred
on the Settings window's monitor, owner = Settings window, `ShowDialog`):
* Title "Connect with a token". Text "Paste a GitHub personal access token. Trayloom encrypts it for your Windows account and never
  shows it again."
* `PasswordBox` (AutomationId `GitHub.TokenBox`), caption "Use a classic token with the “notifications” and “repo” scopes. A
  fine-grained token works too, but GitHub does not let it read notifications." Link button "Create a classic token on GitHub" →
  `https://github.com/settings/tokens/new?scopes=notifications,repo&description=Trayloom`.
* Buttons "Cancel" and accent "Connect" (AutomationIds `GitHub.TokenCancel`, `GitHub.TokenConnect`). Connect: "Checking…", `GET
  /user` with the token (off the UI thread). 200 → save `token.bin`, set keys (`source="token"`), close, poll. 401 → "GitHub did not
  accept this token."; network error → "Could not reach GitHub. Check your connection and try again."; no `notifications`/`repo`
  scope → saves anyway and shows on the page "Notifications need a classic token.". The `PasswordBox` is cleared
  (`passwordBox.Clear()`) when the dialog closes in every path.

AutomationIds (settings): `GitHub.ConnectCli`, `GitHub.UseToken`, `GitHub.Disconnect`, `GitHub.RefreshNow`, `GitHub.Interval`,
`GitHub.Repos`, `GitHub.Notices`, `GitHub.Status`.

### 2.9 Island notices (`github.notice`)

`IIslandStrip` `Id = "github.notice"`, `Order = 42`, `FeatureId = "notch-github"`. After each successful poll (not the first one of
a run, and not when the cache was empty) the runtime compares with the previous snapshot and queues at most one notice, priority:
1. a watched repo turned Failing → title "Checks failed", detail `nameWithOwner`, tint red;
2. `review.issueCount` grew → "Review requested", detail the newest PR title (ellipsis at 120 px), tint amber;
3. unread notifications grew → "New on GitHub", detail `Loc.F("{0} new notifications", n)` / "1 new notification", tint white.
Only when `notchGitHubNotices` is on. `CreateStrip` returns the queued notice once (glyph + title + detail, same shape as
`AgentNoticeView`: icon 12, title 11 medium white max 170, detail 11 medium tint max 120) and clears it. The island shows it for 5 s
(`NoticeEvents.External`, package B's Island edit) and a click opens the `github` page.

### 2.10 Actions and Command Bar

`FeatureAction`s: `refresh` ("Refresh GitHub status", glyph `\uE72C`, only while connected), `open-notifications` ("Open GitHub
notifications", glyph `\uE7ED`, opens `https://github.com/notifications`; always). Dev only: `dump` (writes `notch-github\dump.json`:
state, source, gh path and version, connected, last poll time and duration, last HTTP status per request, counts, next poll time,
error - **no token, no titles**; logs `[github] dump: …`), `connect-cli` (runs the CLI connect flow without UI), `simulate-notice`
(queues "New on GitHub · 3 new notifications").

Command Bar (package B's block): `action.githubRefresh` (shown while connected) and `action.githubNotifications`, both via
`Exact(ctx, "notch-github", …)`, subtitle "GitHub", glyphs 0xE72C / 0xE7ED.

### 2.11 Logging (exact forms)

* `[github] source=cli gh=2.102.0` (version only, not the path) / `[github] source=token`.
* `[github] poll: notifications 304 (12 cached), graphql 200 cost=1 remaining=4987, 812 ms`.
* `[github] poll failed: Offline (HttpRequestException)`; `[github] signed out`; `[github] rate limited until 14:30Z`.
* Never: token, headers, URLs with query strings, login, repository names, titles.

### 2.12 Edge cases

* gh signed in to several accounts: gh uses its active account for `github.com`; the page shows that login.
* The person runs `gh auth logout` while connected: next poll → SignedOut state; Refresh retries.
* `GH_TOKEN`/`GITHUB_TOKEN` set in the person's environment: gh uses it (their configuration); Trayloom neither reads nor logs it.
* Fine-grained token: `/notifications` returns 403 → `NotificationsAvailable=false`, no error state.
* GraphQL partial errors (`errors` array with data): use the data, log `[github] graphql partial errors: n`.
* A listed repository does not exist or is not visible: its alias is null → the row shows "Not found" (white 0.32), no error state.
* Offline at start: cache shown with "Offline. Showing what was loaded at …" after the first failed poll.
* The machine is behind a proxy: `HttpClient` uses the system proxy (default handler); gh uses its own settings.
* Uninstall while a gh process runs: killed in `Stop()`.

### 2.13 Verification checklist (package B)

1. Full build; `--dump-info` lists `notch-github`; the solution and `App.FeatureAssemblies` contain `Trayloom.Features.GitHub`.
2. `--dev-install notch-github` → no network activity in the log (no `[github] poll` line) while not connected.
3. `--action github connect-cli` (TRAYLOOM_DEV) → `[github] source=cli gh=…`, then a `poll:` line; `--action github dump` shows
   counts and `connected: true`; `dump.json` contains no `gho_`/`ghp_`/`github_pat_` substring (check with `Select-String`).
4. Second `--action github refresh` within 5 min → `notifications 304` in the log (conditional request works).
5. `--open-settings island#github` → UIA finds `GitHub.Status` "Connected as @…"; Invoke `GitHub.Disconnect` → `cache.json` gone,
   `notchGitHubConnected` false in the scratch settings.json.
6. `--action github simulate-notice` with the island installed → the closed island shows the notice; its click target is the
   `github` page (log `[island]` open line).
7. `--action island dev-open-github` → `--shot` shows tiles and list; UIA finds `GitHub.TileUnread`.
8. Token path: the person tests by hand (5.6).

---------------------------------------------------------------------------------------------------------------------

## 3. Wi-Fi (`notch-wifi`) - package C

The current Wi-Fi network and signal, the Wi-Fi radio switch, nearby networks with connect (saved, open, password), disconnect and a
way to Windows' own Wi-Fi settings - in the island, as a tile on the island Controls page, and as a card on the tray's Network tab.

### 3.1 Catalog entry

```csharp
list.Add(Def("notch-wifi", I, Loc.T("Wi-Fi"), Loc.T("See your network and its signal, turn Wi-Fi on or off and join nearby networks from the Dynamic Island."), "\uE701", Idle, "", false, "island#wifi", [], legacy: "wifi"));
```

### 3.2 Settings keys

None are needed for behaviour. One remembered UI state: `notchWifiShowSaved` (bool, default true) - "Show saved networks first".
Radio state and saved networks belong to Windows and are never stored by Trayloom.

### 3.3 Windows mapping

* Adapter: `WiFiAdapter.RequestAccessAsync()` (once per run, only when a view first needs Wi-Fi data, never at `Start`), then
  `WiFiAdapter.FindAllAdaptersAsync()`; the first adapter is used (several adapters: the one whose `NetworkAdapter` carries the
  connected profile, else the first).
* Radio: `Radio.RequestAccessAsync()`, `Radio.GetRadiosAsync()` → `Kind == RadioKind.WiFi`; `StateChanged` event;
  `SetStateAsync(RadioState.On|Off)` → `RadioAccessStatus` (`Allowed` / `DeniedByUser` / `DeniedBySystem`).
* Current connection: `NetworkInformation.NetworkStatusChanged` (subscribed in `Start`, cheap) → `adapter.NetworkAdapter.
  GetConnectedProfileAsync()`; `IsWlanConnectionProfile` → `WlanConnectionProfileDetails.GetConnectedSsid()`, `GetSignalBars()`
  (0-5), `GetNetworkConnectivityLevel()` (`InternetAccess` → "Internet", `ConstrainedInternetAccess` → "Sign-in needed",
  `LocalAccess` → "No internet", `None` → "No internet").
* Scan: `adapter.ScanAsync()` then `adapter.NetworkReport.AvailableNetworks` (`Ssid`, `SignalBars` 0-4,
  `NetworkRssiInDecibelMilliwatts`, `SecuritySettings.NetworkAuthenticationType`, `IsWiFiDirect`). `AvailableNetworksChanged` updates
  the list between scans.
* Saved networks: `NetworkInformation.GetConnectionProfiles()` where `IsWlanConnectionProfile` → `ProfileName` set (profile names
  equal SSIDs for networks joined normally; a mismatch only means the "Saved" label is missing).
* Connect: `adapter.ConnectAsync(network, WiFiReconnectionKind.Automatic|Manual)` for saved/open networks;
  `adapter.ConnectAsync(network, kind, new PasswordCredential { Password = pw })` for a password. Result `WiFiConnectionStatus`.
* Disconnect: `adapter.Disconnect()`.
* Windows UI: `ms-settings:network-wifi` (Wi-Fi settings), `ms-availablenetworks:` (the Wi-Fi list in quick settings),
  `ms-settings:privacy-location` (location privacy).
* All WinRT calls run off the UI thread (`Task.Run` + `.AsTask()`), results marshalled with `context.Ui.BeginInvoke`. Every call has a
  10 s timeout (`WaitAsync`), a scan 15 s.

### 3.4 Network list rules

* Hide empty SSIDs (hidden networks) and Wi-Fi Direct entries; group by SSID keeping the strongest entry (several access points).
* Order: the connected network is not in the list (it is the header); then saved networks (when `notchWifiShowSaved`), then the rest,
  each by `SignalBars` descending, then RSSI, then SSID (culture compare). At most 30 rows.
* Security: `Open80211` / `None` → open (no lock glyph, caption "Open"); `Wpa*Psk`, `Rsna*Psk`, `Wpa3Sae`, `Wep` → password
  (lock glyph `\uE72E`); `Wpa`, `Rsna`, `Wpa3Enterprise*`, `Ihv` → enterprise (lock glyph, caption "Needs Windows sign-in").
* Captions: "Saved" (saved), "Open" (open, not saved), "Secured" (password, not saved), "Needs Windows sign-in" (enterprise).
* Signal glyphs (Segoe Fluent; verify on a `--shot`): bars 0-1 `\uE872`, 2 `\uE873`, 3 `\uE874`, 4-5 `\uE701`; radio off / no
  adapter `\uEB55` (verify; fallback `\uE701` at white 0.32).
* Scans run only while the island Wi-Fi page is shown: on `Shown`, then every 30 s while it stays open, and on the refresh button
  (at most every 5 s). Never in the background.

### 3.5 Island page (module `wifi`)

`ModuleDef`: title "Wi-Fi", glyph `"\uE701"`, key `l`, tint `#32ADE6`, summary "Your network, its signal and the networks nearby.".
`IIslandSection`: `Id = "wifi"`, `Title = Loc.T("Wi-Fi")`, `Glyph = "\uE701"`, `FeatureId = "notch-wifi"`, `Order = 170`. The view
implements `IIslandTopRowHost`: the top row holds an island-style switch (white knob, track `#32ADE6` when on, white 0.2 off, 34×19)
for the radio, AutomationId `Wifi.Radio`, name "Wi-Fi".

Layout:
1. **Current network card** (radius 14, fill white 0.08, padding 12, height 56): signal glyph 18 px, SSID (14 semibold, ellipsis),
   caption (11, white 0.6) `Loc.F("Connected · {0}", level)` with level "Internet" / "No internet" / "Sign-in needed"; trailing pill
   "Disconnect" (AutomationId `Wifi.Disconnect`). Not connected: glyph at 0.5, "Not connected", no pill. Connecting: "Connecting to
   {0}…" with a spinner.
2. **Section header** (margin-top 10, height 20): "Networks" (11 semibold, white 0.6); trailing refresh icon `\uE72C` (tooltip
   "Search again", spins while scanning, AutomationId `Wifi.Scan`).
3. **List** (AutomationId `Wifi.List`): rows 34 px: signal glyph 14, SSID (12.5, ellipsis), lock glyph 11 white 0.6 when secured,
   caption right (11, white 0.6). Hover fill white 0.06, hand cursor. Click → connect (3.7). While scanning with an empty list:
   "Searching for networks…". After a scan with nothing: "No networks found.".
4. **Footer** (margin-top 8): pills "Wi-Fi settings…" (`ms-settings:network-wifi`, AutomationId `Wifi.Settings`) and "More
   networks…" (`ms-availablenetworks:`).

States that replace items 1-3 (centred glyph 22 px white 0.5, text 13, caption 11 white 0.6, one pill):
* **Wi-Fi off**: "Wi-Fi is off." / pill "Turn on" (same as the switch).
* **No adapter**: "No Wi-Fi adapter found." / caption "Plug in or turn on a Wi-Fi adapter." / no pill.
* **Location needed** (3.6): "Wi-Fi names need location access." / caption (3.6) / pill "Open location settings".
* **Access denied for radios** (`SetStateAsync` denied): inline orange caption under the header "Windows did not let Trayloom switch
  Wi-Fi. Use Wi-Fi settings instead." for 6 s.

### 3.6 Location permission (Windows 11)

Since Windows 11 24H2 the Wi-Fi APIs that reveal network names (scan results, the connected SSID, BSSIDs) need location access,
because nearby networks can reveal where the PC is. On this PC (build 26300) the rule applies. Behaviour:
* `WiFiAdapter.RequestAccessAsync()` returns `DeniedByUser`/`DeniedBySystem`, or a scan/`GetConnectedSsid()` throws
  `UnauthorizedAccessException` or returns an empty SSID on a WLAN profile → state **Location needed**.
* The first call may show Windows' own prompt ("Let Trayloom access your location?"). That is why Trayloom asks only when the person
  opens the Wi-Fi page or the tray card, never at start.
* To word the caption, read (never write) `HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\
  location` value `Value` (global switch) and its `NonPackaged` subkey (desktop apps) and the same under HKLM (device switch):
  * device or global "Deny": "Location is off on this PC. Windows shows Wi-Fi names only to apps that may use location, because nearby
    networks can reveal where you are. Trayloom never reads or stores your location."
  * desktop apps "Deny": "Location is off for desktop apps. Turn on “Let desktop apps access your location” to see Wi-Fi names here.
    Trayloom never reads or stores your location."
  * otherwise: "Windows did not share Wi-Fi names with Trayloom. Allow location for desktop apps, then search again."
* Without location the radio switch, disconnect and the signal still work; the header shows "Connected" without the SSID.
* The same explanation (one line + "Open location settings" link) appears on the tray card and in the settings section.

### 3.7 Connecting

* Saved or open network: `ConnectAsync(network, Automatic)`; the row shows "Connecting…"; on `Success` the header updates through
  `NetworkStatusChanged`. Open networks get a one-line caption under the header for 6 s: "Open network. Others nearby can see what
  you send."
* Password network not saved, or a saved network that fails with `InvalidCredential`: open `WifiPasswordWindow`.
* Enterprise network: do not try; open `ms-availablenetworks:` and show "Join this network in Windows: it needs a sign-in Windows
  handles." for 6 s.
* Failure captions (orange, 6 s, on the row): `InvalidCredential` "Wrong password."; `NetworkNotAvailable` "The network is out of
  range."; `Timeout` "The network did not answer."; `UnsupportedAuthenticationProtocol` "Windows needs to join this network itself."
  (+ opens `ms-availablenetworks:`); `AccessRevoked` → Location needed state; other "Couldn’t connect.".
* Only one connect at a time; other rows are disabled meanwhile.

`WifiPasswordWindow` (Trayloom-owned WPF `Window`, `ThemeManager.BindWindow`, 380 wide, `SizeToContent=Height`, `Topmost` (the
island is topmost), centred on the monitor of the island / cursor, `ShowActivated`):
* Title `Loc.F("Join {0}", ssid)`; text `Loc.F("Enter the password for “{0}”.", ssid)`.
* `PasswordBox` (AutomationId `Wifi.Password`), `CheckBox` "Connect automatically" (default checked → `Automatic`, else `Manual`,
  AutomationId `Wifi.AutoConnect`), buttons "Cancel" / accent "Connect" (`Wifi.PasswordCancel`, `Wifi.PasswordConnect`), Enter =
  Connect, Esc = Cancel. Connect is disabled under 8 characters for WPA/WPA2/WPA3 (WEP: 5 or 13 characters, or 10 or 26 hex digits).
* While connecting: "Connecting…", controls disabled. `InvalidCredential` → stays open with "Wrong password. Try again." and the
  box selected. Success → closes.
* The password is read from `PasswordBox.Password` only at the moment of the call, handed to `PasswordCredential`, never logged or
  stored by Trayloom (Windows saves the profile itself when "Connect automatically" is checked). The box is cleared on close.

### 3.8 Island Controls tile

`ControlDef("wifi", "Wi-Fi", "\uE701", false, ["notch-wifi"])` (5.5). Visible when `ModuleVisible("wifi")`; a click opens the Wi-Fi
page (`ShowModule("wifi")`). It shows the static title (no live SSID: the Controls page cannot read the System project).

### 3.9 Tray flyout: Network tab card

`IPanelSection` `WifiPanelSection`: `TabId = PanelTabs.Network`, `Order = 900` (below the network monitor), `FeatureId =
"notch-wifi"`. A `PanelCard` with a `PanelRowGroup`:
* `PanelToggleRow(signalGlyph, "Wi-Fi", caption, radioOn, toggle)`: caption = SSID (or "Connected" without location, "Not
  connected", "Off"); AutomationId `Wifi.PanelRadio`.
* `PanelActionRow("\uE701", "Networks…", null, open ms-availablenetworks:)` via `context.InvokeAfterClose`.
* When location is needed: a third row `PanelActionRow("\uE81D", "Allow location to see Wi-Fi names", null, open
  ms-settings:privacy-location, needsAttention: true)`.
Data is read when `context.IsActive` turns true (no polling while the flyout is closed).

### 3.10 Settings section (`island#wifi`)

`ISettingsSection`: `PageId = SettingsPages.Island`, `Anchor = "wifi"`, `Title = Loc.T("Wi-Fi")`, `Order = 190`, `FeatureId =
"notch-wifi"`, `Keywords = ["wireless", "network", "SSID", "signal", "location"]`. `SettingsCard("Wi-Fi")`:
* `SettingsRow("\uE701", "Wi-Fi in the island", "Shows your network and the networks nearby on the island’s Wi-Fi page. Trayloom
  asks Windows for nearby networks only while that page is open, and never stores network names.")`.
* `SettingsRow` + `MacSwitch` → `notchWifiShowSaved`: "Show saved networks first".
* Location status row (only when needed): orange caption from 3.6 + bordered button "Open location settings"
  (AutomationId `Wifi.LocationSettings`).
* Bordered button "Open Wi-Fi settings" (`ms-settings:network-wifi`).

### 3.11 Actions and Command Bar

`FeatureAction`s (evaluated each time): `toggle-radio` ("Turn Wi-Fi off" / "Turn Wi-Fi on", glyph `\uE701`, only when a radio was
found), `disconnect` ("Disconnect from Wi-Fi", only while connected), `settings` ("Wi-Fi settings", opens `ms-settings:network-wifi`).
Dev only: `scan` (scan once, log `[wifi] scan: 14 networks (9 shown), saved=4, access=Allowed, radio=On, connected=yes`),
`state` (log adapter, radio, access, connectivity - no names), `dump` (writes `dump.json` with the same fields plus per-network
signal/security/saved flags with the SSID replaced by its index - no names).

Command Bar (package C's block): `action.wifiRadio`, `action.wifiDisconnect`, `action.wifiSettings` from the three actions, subtitle
"Wi-Fi", glyph 0xE701.

### 3.12 Edge cases

* Airplane mode on: the Wi-Fi radio reports Off; turning Wi-Fi on is allowed by Windows (airplane mode stays on for other radios).
* Radio switched in Windows' quick settings: `Radio.StateChanged` updates the page, the tile and the tray card.
* Hardware switch off: `RadioState.Disabled` → "Wi-Fi is turned off with a switch on the PC." and no "Turn on" pill.
* Disconnect on an "Automatic" profile: Windows may reconnect later on its own; the caption under the header for 6 s: "Disconnected.
  Windows may reconnect to it later."
* Captive portal (`ConstrainedInternetAccess`): caption "Sign-in needed"; Windows opens the browser itself; no action from Trayloom.
* The page closes while connecting: the connect task completes in the background; result logged without SSID.
* No Wi-Fi hardware (desktop on Ethernet): the module stays visible (installed) and shows "No Wi-Fi adapter found."; the tray card
  and the Controls tile hide.

### 3.13 Verification checklist (package C) - automated parts only (rule 0.3.2)

1. Full build; `--dump-info` lists `notch-wifi` and its actions.
2. `--dev-install notch-wifi`, then `--action wifi state` → `[wifi] state:` line; `--action wifi scan` → `[wifi] scan:` line with a
   count (no SSID in the log - check).
3. `--action island dev-open-wifi` → `--shot`; UIA finds `Wifi.Radio` (TogglePattern state readable - **do not toggle**),
   `Wifi.List` with rows.
4. `--open-panel network` → `--shot`; UIA finds `Wifi.PanelRadio` (read only).
5. `--open-settings island#wifi` → section present; location row visible exactly when 3.6 says so.
6. Island Controls page shows the "Wi-Fi" tile; Invoke it via UIA (or the dev action) → the Wi-Fi page opens.
7. Radio off/on, disconnect, join with password, enterprise and open networks: on the person's manual list (5.6).

---------------------------------------------------------------------------------------------------------------------

## 4. Away mode (`away-mode`) - package D

### 4.0 What the person asked for, and what is built

The request: "if I am away from my laptop, the screen should close all the tabs. If I am coming back to the laptop and my face is
detected, the windows and tabs from before should open again."

Closing apps and browser tabs and reopening them later is not built: closing loses unsaved work, many apps ask questions when they
close, browsers restore tabs only in their own way, and there is no reliable Windows API to reopen an app in its previous state.
Away mode gives the same privacy result without losing anything: when the person leaves, **every visible window is minimized**
(nothing is closed); when they come back, **exactly those windows are restored in their previous order, and the one they were
working in gets the focus again**. Coming back can be detected by the camera seeing a face (opt-in), by unlocking the PC, or by the
first touch of the mouse or keyboard.

Honest limits, stated in the UI: face *detection* is not face *recognition* (any face brings the windows back), and minimizing is a
privacy screen, not a lock (anyone can click the taskbar). For real protection the person turns on "Lock the PC too".

### 4.1 Catalog entry

```csharp
list.Add(Def("away-mode", T, Loc.T("Away mode"), Loc.T("Minimizes your windows when you leave the PC and brings them back in the same order when you return, when the camera sees you if you like. Nothing is closed."), "\uE77B", Per, "A,Ca", false, "quick-tools#awayMode", ["awayModeEnabled"]));
```
First install switches `awayModeEnabled` on (catalog rule) with the camera off and the idle trigger at 10 minutes.

### 4.2 Settings keys

| Key | Type | Default | Values | Effect |
|---|---|---|---|---|
| `awayModeEnabled` | bool | true | | The idle trigger is armed. Manual "Go away now" works even when off. |
| `awayModeIdleMinutes` | int | 10 | 2, 5, 10, 15, 30, 60 | Minutes without input before the person counts as away. |
| `awayModeLockPc` | bool | false | | After minimizing, lock the PC (`LockWorkStation`). Return = unlock. |
| `awayModeCover` | bool | false | | Show a dark cover on every display while away. |
| `awayModeFace` | bool | false | | Use the camera to notice the person's return (not with `awayModeLockPc`). |
| `awayModeCameraId` | string | "" | device id | Camera for face detection; "" = front camera, else the first. |
| `awayModeFaceStopMinutes` | int | 120 | 30, 60, 120, 240, 0 (= never) | Turn the camera off after this long away; then input brings the windows back. |
| `awayModeSkipMedia` | bool | true | | Not away while a media session plays. |
| `awayModeSkipCalls` | bool | true | | Not away while any app uses the microphone or a camera. |
| `awayModeSkipFullScreen` | bool | true | | Not away while an app is full screen or presenting. |
| `awayModeIgnoredApps` | List<string> | [] | lower-cased exe paths | Windows of these apps are left alone. |

### 4.3 States and triggers

```
Present --(idle >= N min and armed and not suppressed) | "Go away now" | simulate-away--> GoingAway --> Away
Away --(return trigger)--> Returning --> Present
```
* **Idle check**: a `DispatcherTimer` every 5 s while armed (`awayModeEnabled`), only on console, unlocked, not suspended. Idle =
  `GetLastInputInfo`. No input hooks.
* **Not an away trigger: the session lock.** When Windows locks (by the person, by "dynamic lock", by Windows presence sensing, by a
  timeout) the lock screen already hides every window; minimizing behind it adds nothing. A lock while *Present* does nothing. A lock
  while *Away* keeps Away and makes "unlock" the return trigger.
* **Human presence sensor**: not used in 0.3.0. `Windows.Devices.Sensors.HumanPresenceSensor` is not in the
  `net9.0-windows10.0.19041.0` projection (verified: CS0234), and raising the TFM for one feature is out of scope. PCs with a
  presence sensor can let Windows lock on leave (Settings > Privacy & security > Presence sensing); Away mode then follows the unlock
  rule above. The settings card does not mention it (kept short).
* **Suppression** (checked at each idle tick once idle ≥ N; any one keeps the person Present and resets nothing, so away starts
  at the first tick when none applies):
  * `awayModeSkipFullScreen`: `SHQueryUserNotificationState` is `QUNS_BUSY`, `QUNS_RUNNING_D3D_FULL_SCREEN` or
    `QUNS_PRESENTATION_MODE`.
  * `awayModeSkipMedia`: any `GlobalSystemMediaTransportControlsSession` has `PlaybackStatus == Playing`.
  * `awayModeSkipCalls`: any app uses the microphone or a camera: the consent store
    `HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone` and `…\webcam` (packaged apps as
    subkeys, desktop apps under `NonPackaged`, in use while `LastUsedTimeStop` is 0 or older than `LastUsedTimeStart`; same reading
    as `MicUseService` in the Island project, re-implemented here). Trayloom's own camera preview and island mirror count (the person
    is looking at themselves); Away mode's own face capture never counts (it is off while Present).
  * Trayloom is recording the screen: `Services.Get<IRecordingState>()?.IsRecording == true`.
  * Always: not on console (Remote Desktop), or the Settings > Away mode card is open and focused (the person is configuring it).
  The current reason is shown on the settings card and in `dump`.
* **Return triggers** (any):
  * **Face** (`awayModeFace` on, camera running): two positive samples in a row (4.7).
  * **Unlock**: `SessionSwitch` `SessionUnlock` while Away.
  * **Input**: new input after `awayStart + 2 s` (the grace keeps the click/key that started "Go away now" from ending it), when
    face detection is off, failed, or was stopped by `awayModeFaceStopMinutes`. With face detection running, input does not restore
    by itself (someone else may touch the mouse); it starts a **quick look**: samples every 0.5 s for 10 s; a face → return; none →
    stay away, and the cover (if on) shows "Not recognised? Press I’m back to bring your windows back." (Any face counts, see 4.0.)
  * **The person restores a recorded window themselves** (taskbar click): a recorded window is no longer iconic and there was input in
    the last 3 s → return (the rest come back too).
  * **Manual**: "I’m back" on the cover, "Restore windows" in the tray card / Command Bar, `simulate-return`.
* **Leaving Away for other reasons**: `awayModeEnabled` switched off while Away → return (restore). Feature `Stop()` → restore
  (bounded, 4.6).

### 4.4 Snapshot and minimize (GoingAway)

1. Enumerate with `WindowEnumerator.Enumerate(includeMinimized: false)` (switchable, visible, uncloaked, top-down Z order) and keep
   windows that are not iconic, not owned by Trayloom's process, not of an app in `awayModeIgnoredApps` (`AppIdentity.OwnerOf` path),
   and (dev, 4.13) of a process in `away-mode-only.txt` when that file is active. At most 200 windows.
2. Record per window: `hwnd`, `pid`, process name (no path), class name, Z index (0 = top), `GetWindowPlacement` (`showCmd`, `flags`
   incl. `WPF_RESTORETOMAXIMIZED`, `rcNormalPosition`), `wasMaximized` (`IsZoomed`). Record the active window: `GetForegroundWindow()`
   → `GetAncestor(GA_ROOTOWNER)`; if it is not in the list (a Trayloom window, the desktop) the window at Z 0 becomes the one to
   focus.
3. **Write `away-mode\session.json` synchronously before touching any window** (4.6).
4. Minimize each window top-down with `ShowWindowAsync(hwnd, SW_SHOWMINNOACTIVE)` (async: a hung app cannot block Trayloom).
5. After 1 s, any recorded window that is not `IsIconic` is dropped from the record (an elevated window refuses; UIPI) and counted
   as skipped; the session file is rewritten.
6. If `awayModeLockPc`: `LockWorkStation()`. Else if `awayModeCover`: show the covers (4.8). If `awayModeFace` (and not lock): start
   the face watcher (4.7).
7. Log `[away] away (idle 10 min|manual|simulated): 12 windows minimized, 1 skipped, lock=no, cover=no, face=yes`.
8. While Away, each 5 s tick also minimizes new switchable windows that appear (same filters) and adds them to the record above the
   existing ones (a chat app opening a window must not undo the privacy screen); capped at 200.

### 4.5 Restore (Returning)

Runs on a dedicated background thread (never the UI thread), all foreign-window calls bounded:
1. Stop the face watcher (camera off first), hide the covers.
2. Keep only records whose window still exists and matches (`IsWindow`, same pid via `GetWindowThreadProcessId`, same class name).
   Windows the person closed are skipped silently.
3. Restore bottom-up (highest Z index first): `ShowWindowAsync(hwnd, wasMaximized ? SW_SHOWMAXIMIZED : SW_SHOWNOACTIVATE)`, then
   wait until `!IsIconic(hwnd)` (poll 25 ms, at most 1 s per window, at most 10 s for all; a window that does not answer is left to
   finish on its own and counted as "late").
4. Re-stack in the recorded order without activating: `SetWindowPos(w[0], HWND_TOP, …)` then `SetWindowPos(w[i], w[i-1], 0, 0, 0, 0,
   SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS)` for i = 1…n-1 (async: no cross-process blocking).
5. Placement safety: for each restored, non-maximized window whose rectangle is not `WindowPlacementMath.IsComfortablyVisible` on
   the current monitors (a display was unplugged while away), move it with `WindowPlacementMath.Sanitize` (enter the window's DPI
   context first, `SetWindowPos` async). Never leave a window off-screen.
6. Focus: `WinUtil.ForceForeground(active)` (the existing `AttachThreadInput` path; no input synthesis). If it fails, Windows flashes
   the taskbar button - acceptable, logged.
7. After 1.5 s verify: count recorded windows that are no longer iconic and the foreground window. Then delete `session.json`.
8. Log `[away] back (face|input|unlock|manual|restore-own|disabled|stop|simulated) after 14 min: restored 12/12, late 0, closed 0,
   z-order ok, focus ok`.

Windows the person had minimized before leaving were never recorded and stay minimized. Windows on other virtual desktops are not
touched (cloaked windows are not enumerated).

### 4.6 Crash safety and the window guardian (decision)

**Away mode does not record its windows in `WindowGuardianState`.** Reasons: the guardian exists for windows Trayloom makes
*invisible* (hidden, transparent, parked off-screen); its rescue ends with a window "visible again, opaque, on screen, minimised in
the taskbar" (`WindowRescue` summary). A minimized window *is* that end state: it keeps its taskbar button and Alt+Tab entry, and
the person can always get it back. Recording it would make the guardian "rescue" a window that is already safe and keep a record
that can only be cleared after a visible check. Away mode therefore **never hides, makes transparent, moves off-screen or otherwise
makes a window invisible**; the only change to foreign windows is minimize/restore/Z-order (plus the on-screen sanitising of 4.5).
If a later version ever hides windows, it must use the guardian.

What survives a crash or kill instead is the convenience of getting the windows back in order:
* `away-mode\session.json`, written synchronously (atomic temp + move) after the snapshot, rewritten when records change, deleted
  after a verified restore:
  `{ "v":1, "ownerPid":1234, "ownerStart":<process start FILETIME>, "since":"2026-11-04T14:05:00Z", "trigger":"idle",
  "active": 1708, "windows":[ {"hwnd":1708, "pid":4412, "process":"Code", "cls":"Chrome_WidgetWin_1", "z":0, "max":false,
  "normal":[100,80,1500,980]} ] }` (no titles, no paths).
* At `Start`, if the file exists and its owner is not this process: restore the recorded windows that still exist, match pid and
  class, and are still iconic, bottom-up with `SW_SHOWNOACTIVATE` (no focus stealing at login), then delete the file. Log `[away]
  restored 9 windows left minimized by an earlier run`. A file older than 24 h, or from another logon (handles do not survive
  logoff; the pid/class check fails), is just deleted.
* The covers are Trayloom windows: they die with the process, so a crash can never leave the screen covered.

### 4.7 Face detection

* Availability: `FaceDetector.IsSupported`; camera access `DeviceAccessInformation.CreateFromDeviceClass(DeviceClass.VideoCapture).
  CurrentStatus` not `DeniedByUser`/`DeniedBySystem`. The switch is disabled with the reason when either fails.
* Camera choice: `DeviceInformation.FindAllAsync(DeviceClass.VideoCapture)`; `awayModeCameraId` if present, else the first with
  `EnclosureLocation.Panel == Front`, else the first whose name does not contain "IR", else the first.
* Capture: `MediaCapture.InitializeAsync` with `VideoDeviceId`, `SharingMode = MediaCaptureSharingMode.SharedReadOnly`,
  `StreamingCaptureMode = Video`, `MemoryPreference = Cpu` - the same mode the Camera preview and the island Camera mirror use, so
  they can share the camera instead of fighting over it. `MediaFrameReader` on the first color source, subtype `Nv12` when offered,
  else `Bgra8`; `AcquisitionMode = Realtime`.
* Sampling: frames that arrive before the next due time are dropped (`TryAcquireLatestFrame` + dispose). Due every 3 s (0.5 s during
  a quick look). The sample's `SoftwareBitmap` is converted to `Gray8` when the frame is not `Nv12` (`SoftwareBitmap.Convert`),
  passed to `FaceDetector.DetectFacesAsync` (one detector created once), and disposed right after. **Frames are never written to
  disk, never kept after the check, never logged, never leave the process.**
* Positive sample: at least one face whose box height ≥ 12 % of the frame height (a face near the screen, not a poster across the
  room). Return after 2 positive samples in a row.
* The camera stays on for the whole away time (switching it on and off every few seconds would blink the light, which is more
  alarming than a steady light). It is turned off: on return, after `awayModeFaceStopMinutes`, on `Suspend`, when the session is
  locked (Windows may block cameras on the lock screen anyway), on `Stop()`.
* Coexistence with Trayloom's Camera preview / island mirror: all use `SharedReadOnly`. Away mode does not start the camera while
  any camera use is active (it is a suppression reason with `awayModeSkipCalls`; with that switch off, the shared mode still lets
  both read). Starting the preview while Away is input → return (or a quick look) and the watcher stops the camera before the
  windows come back.
* Failures (`UnauthorizedAccessException`, `MediaCapture` init error, no frames for 10 s, no camera): log `[away] camera: <reason>`
  (no device name), stop trying for this away period, and input becomes the return trigger. The settings card shows the last
  failure under the switch until the next success.
* Logs: `[away] camera: started (Nv12 640x480)`, `[away] camera: face seen`, `[away] camera: stopped (return|timeout|suspend|stop)`.

### 4.8 The cover (optional)

One `AwayCoverWindow` per monitor (`WinUtil.PrepareOnMonitorAt` then `SetBounds` to the monitor bounds, no-activate topmost tool
window, `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`, excluded from the snapshot as a Trayloom window). Background `#0B0B0D`. Centred on
the primary monitor's cover only:
* "Away" (34 semibold, white), under it `Loc.F("Since {0}", shortTime)` (14, white 0.6).
* With face detection: a small line with a camera glyph `\uE714`: "Looking for you with the camera" (12, white 0.6).
* After an unrecognised quick look: "Not recognised? Press I’m back to bring your windows back." (12, amber).
* Button "I’m back" (pill, white 0.12 fill, AutomationId `AwayCover.Back`) → manual return.
Other monitors' covers are plain. The cover never takes the focus and does not capture input (input detection is
`GetLastInputInfo`). With `awayModeLockPc` the cover is not shown (the lock screen covers).

### 4.9 Tray flyout: Quick toggles card

`IPanelSection` `AwayPanelSection`: `TabId = PanelTabs.Toggles`, `Order = 40`, `FeatureId = "away-mode"`. `PanelRowGroup`:
* `PanelToggleRow("\uE77B", "Away mode", caption, awayModeEnabled, set)`, caption `Loc.F("After {0} min without input", n)` or
  "Off"; AutomationId `AwayMode.PanelToggle`.
* `PanelActionRow("\uE7E8", "Go away now", null, () => context.InvokeAfterClose(GoAwayNow, 200))`; while Away it is "Restore
  windows" instead. AutomationId `AwayMode.PanelNow`.

### 4.10 Settings section (`quick-tools#awayMode`)

`ISettingsSection`: `PageId = SettingsPages.QuickTools`, `Anchor = "awayMode"`, `Title = Loc.T("Away mode")`, `Order = 90` (after
Cleaning Mode 80), `FeatureId = "away-mode"`, `Keywords = ["privacy", "idle", "face", "camera", "minimize", "lock", "presence"]`.
`SettingsCard("Away mode")`, top to bottom:

| Row | Control | Strings |
|---|---|---|
| `SettingsRow("\uE77B", …)` | `MacSwitch` → `awayModeEnabled` | "Away mode" / "Minimizes your windows when you leave the PC and brings them back, in the same order, when you return. Nothing is closed, so nothing you were doing is lost." |
| `PickerRow<int>` (inset) | → `awayModeIdleMinutes` | "Away after" / "2 minutes without input", "5 minutes without input", "10 minutes without input", "15 minutes without input", "30 minutes without input", "1 hour without input" |
| status line (inset, 11.5 secondary) | | "Here" / `Loc.F("Away since {0} · {1} windows", time, n)` / `Loc.F("Waiting: {0}", reason)` with reasons "a video or music is playing", "an app uses the microphone or camera", "an app is full screen", "Trayloom is recording the screen" |
| divider | | |
| `SettingsRow("\uE72E", …)` | `MacSwitch` → `awayModeLockPc` | "Lock the PC too" / "Your windows come back when you unlock. This is the only way to keep others out." |
| `SettingsRow("\uE7F4", …)` | `MacSwitch` → `awayModeCover` (disabled while lock is on) | "Cover the screens" / "Shows a dark screen with the time while you are away." |
| divider | | |
| `SettingsRow("\uE714", …)` | `MacSwitch` → `awayModeFace` (disabled while lock is on, or unsupported) | "Come back when the camera sees a face" / "The camera light stays on while you are away and Trayloom looks for you. Pictures are checked on this PC and never saved. Any face brings your windows back: this is face detection, not recognition." When lock is on: "Not needed while “Lock the PC too” is on: unlocking brings your windows back." When unsupported: "Face detection is not available on this PC." |
| `PickerRow<string>` (inset, only with 2+ cameras and face on) | → `awayModeCameraId` | "Camera" / device names |
| `PickerRow<int>` (inset, face on) | → `awayModeFaceStopMinutes` | "Stop looking after" / "30 minutes", "1 hour", "2 hours", "4 hours", "Never" |
| permission line (inset, orange, when camera access is denied) | bordered button "Open camera settings" (`ms-settings:privacy-webcam`) | "Camera access is off for desktop apps." |
| last camera failure (inset, orange) | | "The camera could not start the last time. Your windows came back with the mouse or keyboard instead." |
| divider | | |
| caption "Stay here when" (12 semibold) | | |
| 3× `SettingsRow` + `MacSwitch` (inset) | → `awayModeSkipMedia`, `awayModeSkipCalls`, `awayModeSkipFullScreen` | "A video or music is playing", "An app uses the microphone or camera", "An app is full screen or presenting" |
| `AppBundleList` | → `awayModeIgnoredApps` | title "Leave these apps alone", add button "Add an app…", caption "Windows of these apps stay as they are while you are away." |
| divider | | |
| button row | accent button "Go away now" (while Away: "Restore windows") | caption "Your windows are minimized at once. Move the mouse to bring them back." (with face on: "…Look at the camera to bring them back.") |

AutomationIds: `AwayMode.Enabled`, `AwayMode.IdleMinutes`, `AwayMode.Status`, `AwayMode.Lock`, `AwayMode.Cover`, `AwayMode.Face`,
`AwayMode.Camera`, `AwayMode.FaceStop`, `AwayMode.SkipMedia`, `AwayMode.SkipCalls`, `AwayMode.SkipFullScreen`, `AwayMode.Ignored`,
`AwayMode.Now`.

### 4.11 Actions and Command Bar

`FeatureAction`s: `away-now` ("Go away now", glyph `\uE7E8`; only while Present), `restore` ("Restore windows", glyph `\uE777`;
only while Away). Dev only: `simulate-away` (as `away-now` but refuses unless `away-mode-only.txt` is active, skips suppression and
the 2 s grace; log `[away] simulate-away refused: no away-mode-only.txt` otherwise), `simulate-return` (return with trigger
`simulated`), `simulate-face` (feeds one positive face sample into the state machine; two calls return when face detection is on;
works without a camera), `dump` (writes `away-mode\dump.json`: state, trigger, since, suppression reason, idle seconds, armed,
records count and per record `z`, `max`, `iconic now`; camera state; never titles or paths; logs `[away] dump: …`).

Command Bar (package D's block): `action.awayNow` ("Go away now") and `action.awayRestore` ("Restore windows") via `Exact(ctx,
"away-mode", …)`, subtitle "Away mode", glyphs 0xE7E8 / 0xE777. The toggle row "Away mode" comes from the enabled key.

### 4.12 Edge cases

* Going away while a menu, a drag or a modal dialog is open in some app: minimize may be refused or delayed; the 1 s check drops
  it from the record (it stays visible; logged as skipped).
* A window was closed while away: skipped on restore.
* An app re-creates its window while away (new hwnd): the old record is skipped; the new window was minimized by the 5 s sweep and is
  restored on top.
* Display layout changed while away: 4.5 step 5.
* Return during GoingAway (input within the first second): the minimize pass finishes, then Returning runs at once.
* Return trigger while Returning: ignored. Away trigger while Returning: ignored until 10 s after Present.
* Sleep while away: camera off at `Suspend`; after `Resume` the state is still Away; input or unlock returns.
* The person locks manually while Away: unlock = return.
* `awayModeEnabled` turned off while Away: return now. Feature uninstalled or Trayloom quits while Away: `Stop()` stops the camera,
  closes covers and restores (bounded to 3 s on the calling thread; the remaining restores continue on the background thread until
  process exit), then deletes `session.json` only when every window was verified; otherwise the file stays for the next start (4.6).
* Full-screen game in exclusive mode: suppressed by the full-screen rule.
* The idle tick runs while a Trayloom window has focus (Settings open, no input): counts as idle like anything else; only the Away
  mode card itself suppresses (see 4.3).
* Elevated Trayloom (run as admin): everything works; not required.

### 4.13 Developer aid and the dummy test process

* `away-mode-only.txt` in `AppPaths.Root`, honoured only with `TRAYLOOM_DEV=1`: one process name per line (without `.exe`, e.g.
  `AwayDummy`). When present, the snapshot (4.4) and the 5 s sweep only take windows of those processes. `simulate-away` requires
  it.
* `tests/AwayDummy/` (console app, not in `Trayloom.sln`, like `tests/AmDummy`): `AwayDummy.exe --windows 3` creates three top-level
  windows titled "Away dummy 1..3" (window 2 maximized, window 1 active, distinct sizes), prints `hwnd=<hex> z=<n> state=<normal|max|
  min>` lines, and with `--report <file>` writes the same lines every 500 ms until killed. It reads nothing from Trayloom. Build:
  `dotnet build tests\AwayDummy\AwayDummy.csproj`.
* Test flow (agents): start the dev copy (S0) with `TRAYLOOM_DEV=1`, write `away-mode-only.txt` = `AwayDummy`, start `AwayDummy
  --windows 3 --report <scratch>\dummy.txt`, `--action away-mode simulate-away` → report shows all three `min`; `--action away-mode
  dump` shows 3 records; `--action away-mode simulate-return` → report shows the original states, the original Z order and window 1
  in front; log `[away] back (simulated) … restored 3/3, z-order ok, focus ok`. Face path: enable `awayModeFace` through UIA
  (`AwayMode.Face` TogglePattern) - this does not start the camera while Present - then `simulate-away`; immediately
  `simulate-face` twice → return with trigger `face`. **This starts the camera when face detection is on; agents set
  `"awayModeFace": false` before `simulate-away` unless the person has agreed to a camera test** (rule 0.3.4); in that case
  `simulate-face` is tested with the dev switch `TRAYLOOM_AWAY_NO_CAMERA=1`, which makes the watcher skip `MediaCapture` and wait for
  simulated samples only.
* Crash path: `simulate-away`, then kill the dev copy (`Stop-Process` on its pid), start it again → log "restored 3 windows left
  minimized by an earlier run"; report shows them restored.

### 4.14 Verification checklist (package D)

1. Full build; `--dump-info` lists `away-mode`, actions `away-now` (and dev actions).
2. Dummy flow of 4.13 (minimize, dump, restore order and focus, crash recovery) - all from logs and the dummy report.
3. Suppression: with `AwayDummy` playing nothing, `dump` shows `suppressed: none`; the person can check media/calls/full screen by
   hand (5.6).
4. `--open-settings quick-tools#awayMode` → UIA finds every AutomationId of 4.10; Lock on → `AwayMode.Face` disabled.
5. `--open-panel toggles` → `AwayMode.PanelToggle` present.
6. `away-mode` uninstall while Away (dummy) → windows restored, `session.json` deleted, log "Stopped feature away-mode".
7. Grep the new code: no `SendInput`, `keybd_event`, `mouse_event`, `SendKeys`, `InputSender`, `SW_HIDE`, `WS_EX_TRANSPARENT`,
   `WS_EX_LAYERED` on foreign windows.

---------------------------------------------------------------------------------------------------------------------

## 5. Implementation split

Four packages, one per feature, implemented in parallel by four engineers in four git worktrees cut from the same `main` commit.
Each package creates its own files and edits a few shared files only at the anchors given in 5.5, so the four branches merge without
conflicts in any order.

### 5.0 Before the worktrees (lead) - S0: dev instance isolation

Trayloom is single-instance (`Local\Trayloom.SingleInstance`), so four engineers (and the person's installed copy) cannot each run a
build at the same time. S0 is one small commit on `main` before the worktrees are cut, owned by the lead:

`src/Trayloom.App/App.xaml.cs` - replace the three constants:
```csharp
    private const string MutexName = @"Local\Trayloom.SingleInstance";
    private const string ActivateEventName = @"Local\Trayloom.Activate";
    private const string QuitEventName = @"Local\Trayloom.Quit";
```
with
```csharp
    /// <summary>
    /// Developer aid: with TRAYLOOM_DATA and TRAYLOOM_INSTANCE both set, the single-instance names get a suffix so a development copy can
    /// run beside the installed one (each with its own settings, logs and dev command file under its TRAYLOOM_DATA).
    /// </summary>
    private static readonly string InstanceSuffix =
        Environment.GetEnvironmentVariable("TRAYLOOM_DATA") is { Length: > 0 } && Environment.GetEnvironmentVariable("TRAYLOOM_INSTANCE") is { Length: > 0 } name
            ? "." + string.Concat(name.Where(char.IsLetterOrDigit))
            : "";

    private static readonly string MutexName = @"Local\Trayloom.SingleInstance" + InstanceSuffix;
    private static readonly string ActivateEventName = @"Local\Trayloom.Activate" + InstanceSuffix;
    private static readonly string QuitEventName = @"Local\Trayloom.Quit" + InstanceSuffix;
```
(and add one line to `docs/DEVELOPMENT.md` "Isolating experiments": "* `TRAYLOOM_INSTANCE=<name>` (with `TRAYLOOM_DATA`) lets a
development copy run beside the installed one; flags and `--quit` go to the copy with the same name."). Known side effects of two
copies: global hotkeys register only in the first copy (the second reports a conflict), two tray icons, two islands (move the dev
island with `--action island dev-place-bottom-center`). If the lead skips S0, live runs must be serialised and the person's copy quit
first (`Trayloom.exe --quit`), which the packages must not do on their own.

Per engineer environment (PowerShell):
```
$env:TRAYLOOM_DATA = "<scratchpad>\tl-<A|B|C|D>"; $env:TRAYLOOM_INSTANCE = "pkg<A|B|C|D>"; $env:TRAYLOOM_DEV = "1"
$env:TRAYLOOM_NO_INSTALL_PROMPT = "1"
dotnet build Trayloom.sln -c Debug
src\Trayloom.App\bin\Debug\net9.0-windows10.0.19041.0\Trayloom.exe --no-onboarding
```
(path: check the actual output folder once). Later flags go to the same exe with the same environment. Quit with `--quit`.

Rules for every package: edit only the files listed for it; no new NuGet packages; no edits to Core except the one catalog line; do
not reformat or reorder anything around an anchor; keep the anchor line itself unchanged; commit with a message without any AI
attribution; do not push, do not merge (the lead merges).

### 5.1 Package A - Screen time

Creates (namespace `Trayloom.Features.WindowTools.ScreenTime`):
| File | Content |
|---|---|
| `src/Trayloom.Features.Windows/ScreenTime/ScreenTimeFeature.cs` | `IFeature` (`Id = "notch-screen-time"`), key registration, actions (1.8), runtime publish/withdraw, uninstall cleanup |
| `src/Trayloom.Features.Windows/ScreenTime/ScreenTimeRuntime.cs` | owns tracker + store, today model, `Changed` event, yesterday lookup |
| `src/Trayloom.Features.Windows/ScreenTime/ScreenTimeTracker.cs` | foreground hook, 10 s tick, active rules (1.3), credits, rollover, power/session events |
| `src/Trayloom.Features.Windows/ScreenTime/ScreenTimeStore.cs` | day files, atomic writes, retention, clear, `.bad` handling |
| `src/Trayloom.Features.Windows/ScreenTime/ScreenTimeModel.cs` | `DayUsage`, `AppUsage`, duration formatting (long/compact) |
| `src/Trayloom.Features.Windows/ScreenTime/ScreenTimeNative.cs` | `GetLastInputInfo`, `SHQueryUserNotificationState`, `GetClassName` if not in `WinApi` |
| `src/Trayloom.Features.Windows/ScreenTime/ScreenTimeIsland.cs` | `ScreenTimeIslandSection`, `ScreenTimeRestingStrip`, page view, hour bars |
| `src/Trayloom.Features.Windows/ScreenTime/ScreenTimeLook.cs` | island kit + `ScreenTimeGuard` |
| `src/Trayloom.Features.Windows/ScreenTime/ScreenTimeSettings.cs` | `ISettingsSection` `island#screenTime` |
| `src/Trayloom.Features.Windows/ScreenTime/ScreenTimeStrings.cs` | all strings of section 1 |

Edits (exact snippets in 5.5): `src/Trayloom.Core/Catalog/FeatureCatalog.cs` (A1), `src/Trayloom.Features.Island/IslandModel.cs`
(A2), `src/Trayloom.Features.Island/ClosedViews.cs` (A3), `src/Trayloom.Features.Island/Settings.Section.cs` (A4, A5),
`src/Trayloom.Features.Tools/CommandBar/BarCatalog.cs` (A6). May *use* (never edit) `WinApi`, `AppIdentity`, `NativeMethods`.

### 5.2 Package B - GitHub

Creates (namespace `Trayloom.Features.GitHub`):
| File | Content |
|---|---|
| `src/Trayloom.Features.GitHub/Trayloom.Features.GitHub.csproj` | see B2 |
| `src/Trayloom.Features.GitHub/GitHubFeature.cs` | `IFeature` (`Id = "notch-github"`), keys, actions (2.10), runtime publish, uninstall cleanup |
| `src/Trayloom.Features.GitHub/GitHubRuntime.cs` | state, schedule (2.5), snapshot diff → notices, `Changed` |
| `src/Trayloom.Features.GitHub/GhCli.cs` | gh discovery, `RunAsync` (2.2 A), status-line parsing |
| `src/Trayloom.Features.GitHub/GitHubTokenVault.cs` | DPAPI save/load/delete of `token.bin` |
| `src/Trayloom.Features.GitHub/GitHubClient.cs` | the two requests through gh or `HttpClient`, ETag, rate limits, error mapping |
| `src/Trayloom.Features.GitHub/GitHubQuery.cs` | GraphQL text builder (2.4), repo-list validation |
| `src/Trayloom.Features.GitHub/GitHubModels.cs` | snapshot records, JSON parsing, URL mapping and validation |
| `src/Trayloom.Features.GitHub/GitHubStore.cs` | `cache.json` |
| `src/Trayloom.Features.GitHub/GitHubIsland.cs` | section, `github.notice` strip, page view |
| `src/Trayloom.Features.GitHub/GitHubLook.cs` | island kit + `GitHubGuard` |
| `src/Trayloom.Features.GitHub/GitHubSettings.cs` | `ISettingsSection` `island#github` |
| `src/Trayloom.Features.GitHub/GitHubTokenWindow.cs` | the token dialog |
| `src/Trayloom.Features.GitHub/GitHubStrings.cs` | strings of section 2 |

Edits: `FeatureCatalog.cs` (B1), `Trayloom.sln` (B3, via `dotnet sln`), `src/Trayloom.App/Trayloom.App.csproj` (B4),
`src/Trayloom.App/App.xaml.cs` (B5), `IslandModel.cs` (B6, B7, B8), `src/Trayloom.Features.Island/IslandCore.cs` (B9, B10),
`src/Trayloom.Features.Island/Settings.Content.cs` (B11), `BarCatalog.cs` (B12).

### 5.3 Package C - Wi-Fi

Creates (namespace `Trayloom.Features.SystemTools.Wifi`):
| File | Content |
|---|---|
| `src/Trayloom.Features.System/Wifi/WifiFeature.cs` | `IFeature` (`Id = "notch-wifi"`), keys, actions (3.11), runtime publish |
| `src/Trayloom.Features.System/Wifi/WifiService.cs` | adapter, radio, connection, scan, connect/disconnect (3.3, 3.7), `Changed` |
| `src/Trayloom.Features.System/Wifi/WifiModels.cs` | `WifiState`, `WifiNetwork`, list rules (3.4), glyph mapping |
| `src/Trayloom.Features.System/Wifi/WifiLocation.cs` | location consent reading and captions (3.6) |
| `src/Trayloom.Features.System/Wifi/WifiIsland.cs` | section (+ `IIslandTopRowHost`), page view |
| `src/Trayloom.Features.System/Wifi/WifiLook.cs` | island kit + `WifiGuard` |
| `src/Trayloom.Features.System/Wifi/WifiPasswordWindow.cs` | password dialog |
| `src/Trayloom.Features.System/Wifi/WifiPanelSection.cs` | Network tab card (3.9) |
| `src/Trayloom.Features.System/Wifi/WifiSettings.cs` | `ISettingsSection` `island#wifi` |
| `src/Trayloom.Features.System/Wifi/WifiStrings.cs` | strings of section 3 |

Edits: `FeatureCatalog.cs` (C1), `IslandModel.cs` (C2, C3), `src/Trayloom.Features.Island/Pages.Controls.cs` (C4),
`src/Trayloom.Features.Island/IslandWindow.Input.cs` (C5, C6), `BarCatalog.cs` (C7).

### 5.4 Package D - Away mode

Creates (namespace `Trayloom.Features.WindowTools.AwayMode`):
| File | Content |
|---|---|
| `src/Trayloom.Features.Windows/AwayMode/AwayModeFeature.cs` | `IFeature` (`Id = "away-mode"`), keys, actions (4.11), stale session recovery at start |
| `src/Trayloom.Features.Windows/AwayMode/AwayController.cs` | state machine (4.3), idle tick, triggers, quick look |
| `src/Trayloom.Features.Windows/AwayMode/AwayPresence.cs` | suppression checks (full screen, media, mic/camera consent store, recording) |
| `src/Trayloom.Features.Windows/AwayMode/AwayWindows.cs` | snapshot, minimize, sweep, restore, Z order, sanitise, focus (4.4, 4.5) |
| `src/Trayloom.Features.Windows/AwayMode/AwaySession.cs` | `session.json` (4.6) |
| `src/Trayloom.Features.Windows/AwayMode/AwayFaceWatcher.cs` | camera + `FaceDetector` (4.7) |
| `src/Trayloom.Features.Windows/AwayMode/AwayCoverWindow.cs` | covers (4.8) |
| `src/Trayloom.Features.Windows/AwayMode/AwayNative.cs` | `ShowWindowAsync`, `Get/SetWindowPlacement`, `GetAncestor`, `IsZoomed`, `LockWorkStation`, `GetLastInputInfo`, `SHQueryUserNotificationState` |
| `src/Trayloom.Features.Windows/AwayMode/AwayPanelSection.cs` | Quick toggles card (4.9) |
| `src/Trayloom.Features.Windows/AwayMode/AwaySettings.cs` | `ISettingsSection` `quick-tools#awayMode` (4.10) |
| `src/Trayloom.Features.Windows/AwayMode/AwayStrings.cs` | strings of section 4 (+ `AwayGuard`) |
| `tests/AwayDummy/AwayDummy.csproj`, `tests/AwayDummy/Program.cs` | dummy windows process (4.13), not in the solution |

Edits: `FeatureCatalog.cs` (D1), `BarCatalog.cs` (D2). May *use* (never edit) `WindowEnumerator`, `AppIdentity`, `WinUtil`,
`AppBundleList`, `WinApi`, `WinApiDpi`, `Core.Native.WindowPlacementMath`.

### 5.5 Shared edits - exact snippets and anchors

Every insertion goes **directly after** (or, where said, before) an anchor line that exists exactly once today and that no other
package touches; between any two packages' insertions there are at least two unchanged lines, so git merges them cleanly.

**`src/Trayloom.Core/Catalog/FeatureCatalog.cs`** (Island group lines are in this order today: calendar, notifications, gestures,
timer, accessories, lyrics, queue, live-equalizer, downloads, agents, watch, mascot)
* A1 - after the line starting `list.Add(Def("notch-timer", I,`: the Def line of 1.1.
* B1 - after the line starting `list.Add(Def("notch-agents", I,`: the Def line of 2.1.
* C1 - after the line starting `list.Add(Def("notch-mascot", I,`: the Def line of 3.1.
* D1 - after the line starting `list.Add(Def("cleaning-mode", T,`: the Def line of 4.1.

**`src/Trayloom.Features.Island/IslandModel.cs`**
* C2 - after `new("system", Loc.T("System"), G.System, 'i', …` (Modules list):
  ```csharp
        new("wifi", Loc.T("Wi-Fi"), "\uE701", 'l', C("#32ADE6"), false, Loc.T("Your network, its signal and the networks nearby."), ["notch-wifi"], null, true),
  ```
* A2 - after `new("timer", Loc.T("Timer"), G.Timer, 'r', …` (Modules list):
  ```csharp
        new("screenTime", Loc.T("Screen time"), G.Clock, 'u', C("#30B0C7"), false, Loc.T("How long you used the PC today, and in which apps."), ["notch-screen-time"], null, true),
  ```
* B6 - after `new("agents", Loc.T("AI Agents"), G.Claude, 'g', …` (Modules list):
  ```csharp
        new("github", Loc.T("GitHub"), G.Code, 'h', C("#8250DF"), false, Loc.T("Notifications, pull requests and checks of your GitHub account."), ["notch-github"], null, true),
  ```
* C3 - after `new("scratchpad", Loc.T("Scratchpad"), G.Note, false, ["scratchpad"]),` (Controls list):
  ```csharp
        new("wifi", Loc.T("Wi-Fi"), "\uE701", false, ["notch-wifi"]),
  ```
* B7 - after `public static ControlDef? Control(string id) => Controls.FirstOrDefault(c => c.Id == id);`:
  ```csharp

    /// <summary>
    /// Sections whose page is contributed by another project (an <see cref="IIslandSection"/>): they count as installed only while that
    /// project provides the page, not merely while their feature is installed.
    /// </summary>
    public static bool IsContributed(string id) => id is "agents" or "screenTime" or "github" or "wifi";
  ```
* B8 - after `public static readonly NoticeEvent Unlock = new("unlock", K.LockScreen, 2, 1.4, "controls");`:
  ```csharp

    /// <summary>A notice contributed by another project (a "&lt;owner&gt;.notice" strip other than AI Agents); its owner gates it with its own setting.</summary>
    public static readonly NoticeEvent External = new("external", K.Enabled, 1, 5, "controls");
  ```

**`src/Trayloom.Features.Island/IslandCore.cs`** (B only)
* B9 - replace the line `        return id != "agents" || External("agents") is not null;` with
  `        return !IslandCatalog.IsContributed(id) || External(id) is not null;`
* B10 - replace the line `            ShowNotice(new Notice { Event = NoticeEvents.Agents, Element = el, Glyph = G.Claude, Title = "" });`
  with
  ```csharp
            // AI Agents keeps its own event (its switch and page); any other "<owner>.notice" opens the owner's page when the island has one.
            var owner = s.Id[..^".notice".Length];
            if (owner == "agents")
            {
                ShowNotice(new Notice { Event = NoticeEvents.Agents, Element = el, Glyph = G.Claude, Title = "" });
            }
            else
            {
                ShowNotice(new Notice { Event = NoticeEvents.External, Element = el, Glyph = G.Bell, Title = "", Detail = s.Id + ":" + Environment.TickCount64, OpensModule = IslandCatalog.Get(owner) is not null || External(owner) is not null ? owner : null });
            }
  ```
  (The notice click already falls back to `Event.OpensModule` when `OpensModule` is null: `IslandWindow.State.cs` line ~1288.)

**`src/Trayloom.Features.Island/Settings.Content.cs`** (B only)
* B11 - in `private bool Available(ModuleDef m)` replace `(m.Id != "agents" || (_core?.External("agents") is not null))` with
  `(!IslandCatalog.IsContributed(m.Id) || (_core?.External(m.Id) is not null))`.

**`src/Trayloom.Features.Island/ClosedViews.cs`** (A only)
* A3 - directly **before** the line `            case "agents":` in `Resting`:
  ```csharp
            case "screenTime":
            {
                var strip = core.TakeStrip("screenTime.resting");
                if (strip is not null)
                {
                    middle = strip;
                }

                break;
            }

  ```

**`src/Trayloom.Features.Island/Settings.Section.cs`** (A only)
* A4 - directly **before** the line `        var grid = new UniformGrid { Columns = choices.Count };` (the "At rest" card):
  ```csharp
        if (core?.ModuleVisible("screenTime") == true || current == "screenTime")
        {
            choices.Add(RestChoice(Loc.T("Screen time"), G.Clock, "screenTime", marked == "screenTime", current == "screenTime", core is not null && !core.ModuleVisible("screenTime") ? Loc.T("Show “Screen time” on the Content tab.") : null, () => Pick("screenTime")));
        }

  ```
* A5 - after the line `                "agents" => D.T("62%", 9, FontWeights.Medium),`:
  ```csharp
                "screenTime" => D.T("3h 12m", 9, FontWeights.Medium),
  ```

**`src/Trayloom.Features.Island/Pages.Controls.cs`** (C only)
* C4 - after the line `            "calendar" => Core.ModuleVisible("calendar"),`:
  `            "wifi" => Core.ModuleVisible("wifi"),`

**`src/Trayloom.Features.Island/IslandWindow.Input.cs`** (C only)
* C5 - after the line `            "calendar" => _core.ModuleVisible("calendar"),`:
  `            "wifi" => _core.ModuleVisible("wifi"),`
* C6 - after the three lines `            case "calendar":` / `                ShowModule("calendar");` / `                break;` (the
  control dispatch switch):
  ```csharp
            case "wifi":
                ShowModule("wifi");
                break;
  ```

**`src/Trayloom.Features.Tools/CommandBar/BarCatalog.cs`** (blocks use the existing `Has`, `Exact`, `Simple`, `Area` helpers)
* A6 - after `        Tool("action.scratchpad", "scratchpad", Loc.T("Scratchpad"), 0xE70B, "scratchpad", "scratchpad");`:
  ```csharp
        if (Has("notch-screen-time") && Exact(ctx, "notch-screen-time", "today") is { } screenTime)
        {
            rows.Add(Simple("action.screenTimeToday", screenTime.Title, Area("notch-screen-time", Loc.T("Screen time")), 0xE916, screenTime.Invoke, 100, Loc.T("Screen time usage today apps")));
        }
  ```
* D2 - after `        Tool("action.cleaningMode", "cleaningMode", Loc.T("Cleaning Mode"), 0xE765, "cleaning-mode", null, 100);`:
  ```csharp
        if (Has("away-mode") && Exact(ctx, "away-mode", "away-now") is { } awayNow)
        {
            rows.Add(Simple("action.awayNow", Loc.T("Go away now"), Loc.T("Away mode"), 0xE7E8, awayNow.Invoke, 200, Loc.T("Away mode privacy minimize windows")));
        }

        if (Has("away-mode") && Exact(ctx, "away-mode", "restore") is { } awayRestore)
        {
            rows.Add(Simple("action.awayRestore", Loc.T("Restore windows"), Loc.T("Away mode"), 0xE777, awayRestore.Invoke, 100, Loc.T("Away mode back")));
        }
  ```
* C7 - directly **before** `        if (Has("url-cleaner") && Exact(ctx, "url-cleaner", "clean-clipboard") is { } clean)`:
  ```csharp
        if (Has("notch-wifi") && Exact(ctx, "notch-wifi", "toggle-radio") is { } wifiRadio)
        {
            rows.Add(Simple("action.wifiRadio", wifiRadio.Title, Loc.T("Wi-Fi"), 0xE701, wifiRadio.Invoke, 100, Loc.T("Wi-Fi wireless radio on off")));
        }

        if (Has("notch-wifi") && Exact(ctx, "notch-wifi", "disconnect") is { } wifiDisconnect)
        {
            rows.Add(Simple("action.wifiDisconnect", wifiDisconnect.Title, Loc.T("Wi-Fi"), 0xE701, wifiDisconnect.Invoke, 100, Loc.T("Wi-Fi network disconnect")));
        }

        if (Has("notch-wifi") && Exact(ctx, "notch-wifi", "settings") is { } wifiSettings)
        {
            rows.Add(Simple("action.wifiSettings", wifiSettings.Title, Loc.T("Wi-Fi"), 0xE701, wifiSettings.Invoke, 100, Loc.T("Wi-Fi network settings")));
        }

  ```
* B12 - after `        Tool("action.quickLauncher", "quickLauncher", Loc.T("Quick panel"), 0xE80A, "quick-launcher", "quickLauncher");`:
  ```csharp
        if (Has("notch-github") && Exact(ctx, "notch-github", "refresh") is { } githubRefresh)
        {
            rows.Add(Simple("action.githubRefresh", githubRefresh.Title, Loc.T("GitHub"), 0xE72C, githubRefresh.Invoke, 100, Loc.T("GitHub pull requests checks notifications")));
        }

        if (Has("notch-github") && Exact(ctx, "notch-github", "open-notifications") is { } githubNotifications)
        {
            rows.Add(Simple("action.githubNotifications", githubNotifications.Title, Loc.T("GitHub"), 0xE7ED, githubNotifications.Invoke, 100, Loc.T("GitHub notifications inbox")));
        }
  ```

**New project wiring (B only)**
* B2 - `src/Trayloom.Features.GitHub/Trayloom.Features.GitHub.csproj`:
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <OutputType>Library</OutputType>
      <RootNamespace>Trayloom.Features.GitHub</RootNamespace>
    </PropertyGroup>
    <ItemGroup>
      <ProjectReference Include="..\Trayloom.Core\Trayloom.Core.csproj" />
    </ItemGroup>
  </Project>
  ```
* B3 - `dotnet sln Trayloom.sln add src\Trayloom.Features.GitHub\Trayloom.Features.GitHub.csproj --solution-folder src` (adds the
  project, its configurations and the nesting under `src`; commit whatever it writes, check the file stays without a BOM change).
* B4 - `src/Trayloom.App/Trayloom.App.csproj`, after
  `    <ProjectReference Include="..\Trayloom.Features.Agents\Trayloom.Features.Agents.csproj" />`:
  `    <ProjectReference Include="..\Trayloom.Features.GitHub\Trayloom.Features.GitHub.csproj" />`
* B5 - `src/Trayloom.App/App.xaml.cs` `FeatureAssemblies`, after `        "Trayloom.Features.Agents",`:
  `        "Trayloom.Features.GitHub",`

**Not edited by any package** (the lead does them in the release commit, 5.6): `Directory.Build.props`, What's New
(`src/Trayloom.App/Pages/InfoPages.cs`), `README.md`, `docs/DEVELOPMENT.md`, `TESTING.md`, `specs/CONTRACTS.md` (catalog count).
Each package puts its dev-action lines and manual test steps in its final report so the lead can paste them.

### 5.6 Release commit R (lead, after merging A-D)

* `Directory.Build.props`: `<Version>0.2.0</Version>` → `<Version>0.3.0</Version>` (keep the file's existing BOM/encoding as is).
* `src/Trayloom.App/Pages/InfoPages.cs` - `WhatsNewPage._loc_Notes` becomes:
  ```csharp
    private static readonly LocValue<Section[]> _loc_Notes = new(() => [
        new("Added",
        [
            Loc.T("Screen time: how long you used the PC today, and in which apps, in the Dynamic Island. Everything stays on your PC."),
            Loc.T("GitHub: unread notifications, your pull requests, reviews waiting for you and failing checks in the Dynamic Island. Off until you connect, through the GitHub CLI or an encrypted token."),
            Loc.T("Wi-Fi: see your network and its signal, turn Wi-Fi on or off and join nearby networks from the Dynamic Island and the tray."),
            Loc.T("Away mode: your windows are minimized when you leave the PC and come back in the same order when you return, optionally when the camera sees you. Nothing is closed."),
        ]),
    ]);
  ```
  and the version line date `2026-11-02` → the release date.
* `README.md`: Dynamic Island table, after the "Timer" row:
  `| **Screen time** | How long you used the PC today and which apps, with an hour-by-hour chart. App names and times stay on your PC. |`;
  after the "AI Agents" row:
  `| **GitHub** | Unread notifications, your pull requests, reviews waiting for you, failing checks and latest releases (online only after you connect, through the GitHub CLI or an encrypted token). |`
  and `| **Wi-Fi** | Your network and signal, the Wi-Fi switch, nearby networks with join and disconnect. |`;
  Tools table, after "Cleaning Mode": `| **Away mode** | Minimizes your windows when you leave and brings them back in the same order when you return, by mouse, unlock or (optionally) when the camera sees you. Nothing is closed. |`;
  the "only online features" sentence (line ~292) adds "GitHub status (after you connect)".
* `docs/DEVELOPMENT.md` "Developer flags": one paragraph listing `--action screen-time dump|flush|dev-add-minute|today`,
  `--action github refresh|open-notifications|dump|connect-cli|simulate-notice`, `--action wifi scan|state|dump` (never
  `toggle-radio`/`disconnect` in automation), `--action away-mode simulate-away|simulate-return|simulate-face|dump` with
  `away-mode-only.txt`, `TRAYLOOM_AWAY_NO_CAMERA=1` and `tests\AwayDummy`.
* `TESTING.md` - new manual checks (only a person can do them):
  * Wi-Fi: switch off and on from the island, the tray card and the Command Bar; disconnect and reconnect; join a password network
    (wrong password first), an open network, an enterprise network (opens Windows); location off for desktop apps → the explanation
    and the settings link; airplane mode.
  * Away mode with real windows: idle 2 min → everything minimized, move the mouse → same order, same focus; "Lock the PC too" →
    unlock restores; cover on two displays; face detection: walk away 3 min, come back, look at the screen → windows return within
    about 6 s, the camera light is on only while away; Camera preview open while away (suppressed by "An app uses the microphone or
    camera"); a video playing (no away); a full-screen presentation (no away); undock a display while away.
  * GitHub with a classic token and with a fine-grained token; `gh auth logout` while connected; disconnect removes the token file.
  * Screen time across midnight and after sleep.
* `specs/CONTRACTS.md`: catalog "79 entries; 73 installable" → "83 entries; 77 installable".

### 5.7 Merge order and conflict check

Any order works; recommended A → D → C → B (B last because it touches the most shared files). After each merge: `dotnet build
Trayloom.sln -c Debug` and `--dump-info` (duplicate checks, four new ids). If git still reports a conflict in a shared file, both
sides are additions at different anchors: keep both.

### 5.8 Per-package verification (summary)

| | Build | Dev actions to run | Log lines to look for | UIA ids to find |
|---|---|---|---|---|
| A | `dotnet build Trayloom.sln -c Debug` (Core changed; Windows, Island, Tools rebuilt) | `screen-time flush`, `dev-add-minute`, `dump`; `island dev-open-screenTime` | `Started feature notch-screen-time`, `[screen-time] dump: total=…` | `ScreenTime.Total`, `ScreenTime.AppList`, `ScreenTime.Enabled`, `ScreenTime.Today` |
| B | same (new project) | `github connect-cli`, `refresh` ×2, `dump`, `simulate-notice`; `island dev-open-github` | `[github] source=cli gh=…`, `[github] poll: notifications 304 …` | `GitHub.Status`, `GitHub.TileUnread`, `GitHub.Disconnect` |
| C | same | `wifi state`, `wifi scan`, `wifi dump`; `island dev-open-wifi`; `--open-panel network` | `[wifi] state: …`, `[wifi] scan: N networks …` (no names) | `Wifi.Radio` (read only), `Wifi.List`, `Wifi.PanelRadio` |
| D | same + `dotnet build tests\AwayDummy\AwayDummy.csproj` | `away-mode simulate-away`, `dump`, `simulate-return`, `simulate-face`; kill/restart | `[away] away (simulated): 3 windows minimized`, `[away] back (simulated) … restored 3/3, z-order ok, focus ok`, `restored … left minimized by an earlier run` | `AwayMode.Enabled`, `AwayMode.Face`, `AwayMode.Now`, `AwayMode.PanelToggle` |

Every package additionally: grep its new files for the GPL header, `Loc.T` on every visible string, no `Vorssaint` in strings, no
input-synthesis API; `Stop()` leaves no hook, timer, window, process or camera (check the log and Task Manager handles if unsure);
take `--shot` pictures into `screenshots/` (git-ignored) only.

---------------------------------------------------------------------------------------------------------------------

## 6. Open questions and risks for the owner

1. **S0 (dev instance isolation)**: without it the four engineers cannot run builds in parallel and the person's copy has to be quit
   for every live test. Recommended: let the lead commit S0 first. Alternative: engineers only build in parallel; the lead verifies
   live after merging.
2. **Any face counts**: face detection cannot tell the owner from a colleague. The UI says so; "Lock the PC too" is the real
   protection. A "check it’s me with Windows Hello" step (`UserConsentVerifier` with an HWND interop) is possible later but not in
   0.3.0.
3. **Session lock is not an away trigger** (the lock screen already hides everything). If the owner wants "lock → minimize, unlock
   → restore" anyway, it is a two-line change in the controller.
4. **Human presence sensor** needs a newer Windows SDK projection (TFM above 19041); not in 0.3.0.
5. **Wi-Fi and location**: on Windows 11 24H2+ network names need location access for desktop apps; some people will refuse. The
   feature then still switches Wi-Fi and disconnects, but cannot list or name networks. Should the Permissions page get a "Location"
   row (new `AppPermission`, Core + App edits)? Not in this split; recommended as a follow-up.
6. **Wi-Fi radio switching for unpackaged apps** (`Radio.SetStateAsync`) is expected to be allowed, but a device policy can deny it;
   the page then points to Windows settings. Verify on the person's PC by hand.
7. **GitHub fine-grained tokens** cannot read notifications (GitHub limitation); the classic-token link pre-selects `notifications`
   and `repo`, which grants broad access - the CLI path is recommended in the UI.
8. **Screen time counts while the island is off** (it is an island extension, but counting is independent). Alternative: count only
   while the island is installed. Recommended as specified.
9. **Away mode's first-install default is "on" with 10 minutes** (catalog convention: installing a feature switches it on). If the
   owner prefers installed-but-off, pass `initial: []` in D1.
10. **Command Bar edits sit in the Tools project**, which none of the four otherwise touches; four small blocks at four anchors.
    A generic "contributed Command Bar rows" contract would be cleaner but is a Core change; not in 0.3.0.
11. **Glyph code points** (Segoe Fluent) are best-effort; each package checks them on a `--shot` and swaps any that render wrong.
