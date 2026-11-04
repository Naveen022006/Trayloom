# 07 - Away mode: close apps while away, open them again on return (0.4.0)

An addition to Away mode (`away-mode`, spec 06 section 4, shipped in 0.3.0). Spec 06 section 4 stays the source of truth for
everything not changed here; where the two differ, this spec wins. `02-settings.md` still wins on structure, `CONTRACTS.md` is the
API. Every rule in `CLAUDE.md` applies; the ones that matter most are repeated in 0.4. Release: Trayloom 0.4.0
(`Directory.Build.props` goes from 0.3.1 to 0.4.0 in the release commit, section 13).

## Table of contents
0. The request, the decisions, what was verified
1. Settings keys
2. Which apps can be closed
3. Browsers: closing, private windows, downloads, reopening
4. Other apps: closing, prompts, timeouts, reopening
5. State machine changes
6. Reopening: launching, matching windows, placement, order, focus
7. Persistence, crash, sleep, sign-out and shutdown
8. Edge cases
9. UI: settings card, tray card, cover, recovery card, notifications, Command Bar, catalog
10. Privacy and logging
11. Developer aid and tests that cannot harm the person
12. Files to create and edit (exact anchors)
13. Release 0.4.0: version, What's New, README, TESTING, DEVELOPMENT
14. Verification checklist
15. Open questions for the person

---------------------------------------------------------------------------------------------------------------------

## 0. The request, the decisions, what was verified

### 0.1 The request

"Use case: closing and opening of the tabs while I'm not there in the PC. Add that also, and do it properly." In 0.3.0 Away mode
only minimizes. The person now wants, as an option, chosen apps (above all browsers, with their tabs) really **closed** when they
leave and **opened again as they were** (same tabs, same window positions) when they come back, by any existing return trigger
(face, unlock, input, restoring a window, manual).

### 0.2 Decisions (summary)

| Topic | Decision |
|---|---|
| Opt-in | New switch "Close apps while I’m away", **off** by default. Minimizing everything else is unchanged. |
| Which apps | Only apps on a new list "Apps to close". The first time the switch is turned on, the installed browsers that Trayloom can reopen with their tabs (Chrome and Edge on this PC) are added to the list once. Nothing else is ever preselected. |
| When | At the moment the person counts as away (default), or after 5 / 15 / 30 / 60 minutes away. Everything is still minimized at once, as in 0.3.0. |
| Browsers | Closed through the **Windows Restart Manager** (`RmShutdown`, graceful, never forced): the same `WM_QUERYENDSESSION` / `WM_ENDSESSION` path Windows uses when it restarts for an update. The whole browser closes at once and saves its full session. Reopened with the command line the browser itself registered with Windows (`GetApplicationRestartSettings`), which for Chromium browsers contains `--restore-last-session --restart` and the original profile switches. Works whatever the person's "On startup" setting is. |
| Not per-window `WM_CLOSE` for browsers | Verified (0.3): closing Chrome's windows one by one keeps only the **last** window in the session; the others are lost. |
| Private windows | A browser with an incognito, InPrivate or guest window open is **never closed** (closing is per browser process, and private windows cannot come back); it is only minimized, and the card says why. Detection is fail-safe: any doubt counts as private. |
| Downloads | A browser with a download in progress is only minimized. |
| Firefox, Brave, Vivaldi, Opera | **Only minimized in 0.4.0**, with the reason shown. Not installed on this PC, so not verified; private windows cannot be told apart for them yet (3.7). |
| Other apps | Closed with `WM_CLOSE` posted to their windows (window messages only; no input, never terminate or kill). An app that asks a question (save prompt) is answered "Cancel" through its standard dialog (`WM_CLOSE` to a `#32770` dialog = Cancel) and stays open and minimized; one that does not exit within 10 s is left open and minimized. |
| Reopen | Relaunch with the recorded executable and arguments, never elevated, never anything Trayloom did not close, never while the app is already running. Window positions are put back onto the new windows by matching (6.3). Apps that restore their own state (VS Code, Office, browsers) are simply started and left to do it. |
| Persistence | New sibling file `away-mode\closed-apps.json` (atomic, written before anything is closed). After a crash, kill, reboot, sign-out or shutdown while away, the next start **asks** (tray card, Settings card and a notification) instead of reopening by itself. |
| Sign-out / shutdown while away | Nothing is reopened during the session end; the record is kept and offered at the next start. |
| Elevated Trayloom | The close option is unavailable while Trayloom runs as administrator (it could otherwise relaunch apps elevated). Elevated apps are never closed. |

### 0.3 Reliable vs best effort

* **Reliable (verified on this PC, 0.5)**: Chrome 157 and Edge 156 close through `RmShutdown` in 0.1-0.2 s without any prompt (Edge's
  "Close all tabs?" does not appear), every browser process exits, and launching `<exe> <registered restart command>` brings back
  **all windows and all tabs**, with a fresh profile whose startup setting is "Open the New Tab page". Incognito and InPrivate windows
  are recognised (3.3). Generic `WM_CLOSE`, save-prompt detection and "Cancel" are standard Windows behaviour and are verified with
  AwayDummy (11.3).
* **Best effort**: window *positions* after reopening (the browser restores its own windows, but not always with the same show state:
  verified that Chrome brought one window back minimized and one un-maximized; Trayloom therefore re-applies the recorded placement,
  6.3, which depends on matching); other apps' internal state (open documents, tabs, unsaved text) is whatever the app restores
  itself; download detection only sees the browser's own download folders; packaged (Store) apps reopen to their start page unless
  they restore themselves; minimize-to-tray apps; a browser started in the background at sign-in (Edge startup boost, Chrome
  background apps) when the person reopens from the recovery card.
* **Not done**: Firefox / Brave / Vivaldi / Opera closing (minimized instead), apps with windows on another virtual desktop (Windows
  gives no way to move a foreign window back to its desktop), console and terminal apps, File Explorer, elevated apps, apps of
  another user.

### 0.4 Safety rules (from `CLAUDE.md`, made concrete for this feature)

1. **Never synthesize input.** Closing is window messages only (`WM_CLOSE`, and Restart Manager's own `WM_QUERYENDSESSION` /
   `WM_ENDSESSION`). Prompts are cancelled with `WM_CLOSE` to the dialog window, never with keys or clicks.
2. **Never terminate.** No `TerminateProcess`, `Process.Kill`, `taskkill`, `NtTerminateProcess`, no `RmForceShutdown` (0x1). The
   Restart Manager is called with `lActionFlags = 0` (0.5 shows why not `RmShutdownOnlyRegistered`).
3. **Never close the person's real apps or browser in a test.** Every automated test runs with `away-mode-only.txt` (11.1), which
   now also restricts browsers to instances whose `--user-data-dir` lies under `TRAYLOOM_DATA` or the temp folder. The person's
   Chrome (profile in `%LOCALAPPDATA%\Google\Chrome\User Data`) can never match.
4. **Never relaunch elevated, never relaunch what Trayloom did not close**, never relaunch while the app is running.
5. Never open or log the browser's history, cookies, sessions or tab titles; the only browser files Trayloom reads are `Local State`
   (profile display names) and each profile's `Preferences` (two download-folder keys), in memory, never stored or logged.

### 0.5 What was verified, and how

Documentation (Microsoft Learn, Chromium source, Mozilla Bugzilla):
* `RmShutdown`: "Initiates the shutdown of applications"; `RmForceShutdown` (0x1) forces unresponsive apps after 30 s - **never used**;
  `RmShutdownOnlyRegistered` (0x10) shuts down only if all are registered for restart; returns `ERROR_FAIL_SHUTDOWN` (351) when some
  could not be shut down, `ERROR_CANCELLED` (1223) after `RmCancelCurrentTask`.
* Restart Manager "Guidelines for Applications": GUI apps get `WM_QUERYENDSESSION` with `lParam = ENDSESSION_CLOSEAPP`; if none
  returns FALSE, `WM_ENDSESSION` (TRUE, `ENDSESSION_CLOSEAPP`), then `WM_CLOSE` for apps that did not exit; an app returning FALSE
  cancels the shutdown; without force nothing is terminated. "Restart Manager can only restart applications that have been
  registered for restart", using the command line registered with `RegisterApplicationRestart`.
* `GetApplicationRestartSettings(hProcess, …)`: returns the restart command line another process registered (needs
  `PROCESS_VM_READ`); `HRESULT_FROM_WIN32(ERROR_NOT_FOUND)` (0x80070490) when it did not register.
* `RmCancelCurrentTask`: cancels the current `RmShutdown`; must be called by the process that started the session.
* Chromium `chrome_browser_main_win.cc` (`RegisterApplicationRestart` / `GetRestartCommandLine`): the registered command keeps the
  original switches (not URLs), drops `about_flags` switches and `--from-installer`/`--isolated`, and adds `--restore-last-session`
  and `--restart`; flags `RESTART_NO_CRASH | RESTART_NO_HANG | RESTART_NO_PATCH`.
* Chromium `chrome_switches.cc`: `--restore-last-session` "overrides the preferences value" and deliberately does not force restore
  after a crash. `startup_browser_creator.cc`: with that switch and no open windows, Chrome opens the **last opened profiles** (each
  with its session); guest and off-the-record profiles are not restored.
* Firefox (Bug 603903, Firefox 62): Firefox registers for restart at startup (`toolkit.winRegisterApplicationRestart`, default true)
  and treats an OS-requested end of session as a restart, so its session is resumed. Not installed here, so not verified; see 3.7.

Experiments on this PC (scratchpad only, isolated `--user-data-dir` instances, the person's Chrome never touched; it kept running
throughout and its own registration reads `--source-shortcut-location=taskbar --restore-last-session --restart`):
1. Chrome, 2 windows / 4 local `file:///` tabs, `RmShutdown(flags 0)`: returned 0 in 109 ms, main process gone in 8 ms, all processes
   of that profile gone in 238 ms. Relaunch `chrome.exe <registered command>`: **2 windows, all 4 tabs** (DevTools `/json/list`).
2. Same, but `WM_CLOSE` posted to each window 300 ms apart: relaunch brought back **only the last window** (2 of 4 tabs).
3. Edge, 2 windows / 4 tabs plus an InPrivate window (closed first by the test): `RmShutdown` 199 ms, no "Close all tabs?" prompt,
   relaunch restored both windows and 4 tabs.
4. `RmShutdown` with `RmShutdownOnlyRegistered` (0x10) on Chrome: `ERROR_FAIL_NOACTION_REBOOT` (350), nothing closed (Chrome
   registers with `RESTART_NO_PATCH`). Hence flags 0.
5. Launching the restart command while that browser instance was still running opened an extra "New Tab" window instead of a
   restore. Hence "never relaunch while running" (6.1).
6. Private-window signals (UI Automation, class `BrowserRootView`, its Name vs the window title):
   Chrome normal "… - Google Chrome" (= title); Chrome second profile "… - Google Chrome - Person 1"; Chrome incognito
   "… - Google Chrome (Incognito)"; Edge normal "… - Microsoft Edge"; Edge InPrivate "… - Microsoft Edge (InPrivate)". The window
   title of a Chrome incognito window has **no** marker, its AppUserModelID is the same as the normal profile's, and the Win32 window
   properties are identical, so UI Automation is the only usable signal. FindFirst took 16 ms.
7. Chrome brought restored windows back with different show states (one minimized, one un-maximized) - hence 6.3.
Observed oddity for test scripts: a second `chrome.exe` launch that repeats `--remote-debugging-port` made the first test instance
disappear; open further windows with only `--user-data-dir` and `--new-window` (11.4).

---------------------------------------------------------------------------------------------------------------------

## 1. Settings keys

Registered in `AwayKeys.Register` (next to the existing keys). All in `settings.json` through `IAppSettings`.

| Key | Type | Default | Values | Effect |
|---|---|---|---|---|
| `awayModeCloseApps` | bool | false | | Close the apps on the list while away and open them again on return. |
| `awayModeCloseList` | List<string> | [] | lower-cased exe paths (same format as `awayModeIgnoredApps`) | Apps to close. |
| `awayModeCloseListSeeded` | bool | false | | Set when the browsers were added once (2.3); never reset. |
| `awayModeCloseAfterMinutes` | int | 0 | 0 (= when I leave), 5, 15, 30, 60 | Minutes away before closing starts. |

Constants (not settings): close timeout per app `CloseTimeoutMs = 10000`; Restart Manager watchdog `RmWatchdogMs = 20000`; reopen
budget `ReopenBudgetMs = 30000` (browsers 20000 each); at most `MaxCloseApps = 20` apps per away period; recovery record kept at most
7 days.

`awayModeIgnoredApps` wins over `awayModeCloseList` (an app on both is left alone, like 0.3.0).

---------------------------------------------------------------------------------------------------------------------

## 2. Which apps can be closed

### 2.1 Kinds

Every listed app that is running with at least one top-level window when closing starts (visible, minimized or not; switchable
rules of `WindowEnumerator.IsSwitchable` except the iconic check) is classified by its executable (`AppIdentity.OwnerOf`, lower-cased
path; UWP frames resolve to the hosted process):

| Kind | How it is recognised | Close | Reopen |
|---|---|---|---|
| `browser` (Chrome, Edge) | file name `chrome.exe` / `msedge.exe` and the main process registered a restart command containing `--restore-last-session` | Restart Manager (3.2) | exe + registered command (3.5) |
| `browser-min` (Firefox, Brave, Vivaldi, Opera, Chrome Beta/Dev/Canary when the title check fails) | file names `firefox.exe`, `brave.exe`, `vivaldi.exe`, `opera.exe`, `launcher.exe` under an `Opera` folder | not closed | (minimized, as 0.3.0) |
| `packaged` | the process has package identity (`GetApplicationUserModelId` succeeds) | `WM_CLOSE` (4.2) | `explorer.exe shell:AppsFolder\<AUMID>` |
| `app` | anything else that is allowed | `WM_CLOSE` (4.2) | exe + recorded arguments (4.5) |

### 2.2 Never closed (and shown as such if added)

* Trayloom's own process; `explorer.exe` (shell and File Explorer windows); shell hosts (`ApplicationFrameHost.exe` itself,
  `ShellExperienceHost.exe`, `StartMenuExperienceHost.exe`, `SearchHost.exe`, `TextInputHost.exe`, `LockApp.exe`, `dwm.exe`).
* Consoles and terminals (`conhost.exe`, `OpenConsole.exe`, `WindowsTerminal.exe`, `cmd.exe`, `powershell.exe`, `pwsh.exe`,
  `wsl.exe`, `mintty.exe`, and any window of class `ConsoleWindowClass` or `CASCADIA_HOSTING_WINDOW_CLASS`): closing ends the programs
  running inside.
* Processes started as COM servers or helpers: command line contains `-Embedding`, `/Embedding`, `/automation` or `--type=`.
* Elevated processes (token `TokenElevation` = 1, or `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` / `OpenProcessToken` refused),
  processes of another user (token user SID differs from Trayloom's), processes in another session.
* Apps with any window on another virtual desktop (`DWMWA_CLOAKED` = `DWM_CLOAKED_SHELL` on a window whose
  `IVirtualDesktopManager.IsWindowOnCurrentVirtualDesktop` is false): they would come back on the current desktop.
* Apps not running when closing starts (nothing to close, nothing recorded).

These rules are applied again at close time, so a list entry never overrides them.

### 2.3 Defaults and how the person picks apps

* When `awayModeCloseApps` turns **on** and `awayModeCloseListSeeded` is false: read the registered browsers
  (`HKCU` then `HKLM\SOFTWARE\Clients\StartMenuInternet\*\shell\open\command`, first token, unquoted, must exist), keep those of kind
  `browser` (`chrome.exe`, `msedge.exe`), add their lower-cased paths to `awayModeCloseList` if missing, set `awayModeCloseListSeeded`
  = true. On this PC that adds `c:\program files (x86)\google\chrome\application\chrome.exe` and
  `c:\program files (x86)\microsoft\edge\application\msedge.exe`. Removing them later is respected (the seeding never runs again).
* The person adds apps with the existing `AppBundleList` (running and installed apps, Browse…). Each row gets an accessory caption
  (9.1) saying what will happen to that app (tabs come back / only minimized / opens again / can't be closed).
* Firefox, Brave, Vivaldi and Opera can be added; their row says they are only minimized.

---------------------------------------------------------------------------------------------------------------------

## 3. Browsers

### 3.1 The browser instance

* A browser instance = one **main process**: a process of that exe without `--type=` in its command line that owns top-level
  windows. Several instances can run (different `--user-data-dir`); each is handled on its own.
* `userDataDir` of an instance: the `--user-data-dir=` value of its command line (quotes removed), else the default
  (`%LOCALAPPDATA%\Google\Chrome\User Data`, `%LOCALAPPDATA%\Microsoft\Edge\User Data`).
* Command lines are read with `NtQueryInformationProcess(ProcessCommandLineInformation = 60)` (Windows 8.1+, needs only
  `PROCESS_QUERY_LIMITED_INFORMATION`, no foreign memory reads); failure = "unknown" (then the browser is only minimized).
* Restart command: `GetApplicationRestartSettings` on the main process (`OpenProcess(PROCESS_VM_READ |
  PROCESS_QUERY_LIMITED_INFORMATION)`), buffer `RESTART_MAX_CMD_LINE` (1024) + 1 characters. Required: success and the text contains
  `--restore-last-session`. Otherwise the instance is `browser-min` for this period (reason `Unsupported`).

### 3.2 Closing a browser (Restart Manager)

On the close worker thread (never the UI thread), per instance, in this order:
1. Checks that keep it open (only minimized): private window (3.3) → `Private`; download (3.4) → `Download`; window on another desktop
   → `OtherDesktop`; elevated/other user → `Elevated` / `OtherUser`; restart command missing → `Unsupported`.
2. Record the instance (7.2) with state `closing`, the restart command, the exe path, every top-level window of the main process
   (incl. minimized ones) with Z index, show state and normal rectangle; write `closed-apps.json`.
3. `RmStartSession(out session, 0, key)`; `RmRegisterResources(session, 0, null, 1, [RM_UNIQUE_PROCESS{pid, start FILETIME from
   GetProcessTimes}], 0, null)` (the start time makes a reused pid impossible); `RmShutdown(session, 0, null)`. A watchdog thread
   calls `RmCancelCurrentTask(session)` after `RmWatchdogMs` or when the person comes back (5.3). `RmEndSession` always.
4. Result: `ERROR_SUCCESS` and the main process exits within 10 s (wait on a `SYNCHRONIZE` handle) → `closed`. Process still alive
   (the browser vetoed, or `ERROR_FAIL_SHUTDOWN` / `ERROR_CANCELLED`) → `kept` with `Refused` / `Cancelled`; its windows are still there
   and were already minimized. Main process alive but **no top-level windows left** after 10 s (a background-mode browser) → `closed`
   with `"background": true` (reopen 3.5).
5. Write `closed-apps.json` after each instance.

`WM_CLOSE` to browser windows is never used (0.5, experiment 2).

### 3.3 Private windows (incognito, InPrivate, guest) - fail-safe detection

Per top-level window of the instance, on the close worker (an MTA thread), each bounded to 1500 ms (run the UI Automation call on a
short-lived worker and abandon it on timeout):
1. `AutomationElement.FromHandle(hwnd)` then `FindFirst(TreeScope.Descendants, ClassName == "BrowserRootView")`; take its `Name` = R.
2. Remove zero-width characters (U+200B, U+200C, U+200D, U+FEFF) from R.
3. The window is **normal** only when:
   * Chrome: R ends with `" - Google Chrome"`, or ends with `" - Google Chrome - " + P` where P is a profile display name from the
     instance's `Local State` (`profile.info_cache.<dir>.name`; read once per close pass, in memory only).
   * Edge: R ends with `" - Microsoft Edge"`.
4. Anything else is **private**: a different suffix (verified "(Incognito)", "(InPrivate)"; "(Guest)" and channel names such as
   "Beta" fall here too), no `BrowserRootView`, an exception, a timeout. The browser main command line containing `--incognito`,
   `--inprivate` or `--guest` is private as well.
5. One private window → the whole instance is kept (minimized), reason `Private`.

The brand names are not translated by Google or Microsoft, so the rule does not depend on the display language; the private suffixes
are never matched as words. The cost of a false "private" is only that the browser is minimized instead of closed. Querying UI
Automation makes Chromium build the accessibility tree of its own window frame only (not of web pages) for that session; it is done
once per away period, just before closing.

### 3.4 Downloads in progress

An instance is kept (reason `Download`) when any `*.crdownload` file modified in the last 30 s exists (top level only, at most 500
entries looked at) in any of: each profile's `Preferences` → `download.default_directory` and `savefile.default_directory` (profiles
from `Local State` `profile.info_cache` keys), the user's Downloads known folder (`SHGetKnownFolderPath(FOLDERID_Downloads)`).
Downloads saved elsewhere ("Ask where to save each file") are not seen: best effort, said in TESTING.md. Paused downloads (file not
written for 30 s) do not count; Chromium marks them interrupted and resumable.

### 3.5 Reopening a browser

1. Skip (state `reopened`, note `AlreadyOpen`) when a main process of that exe with the same `userDataDir` exists **and owns a
   visible top-level window** (the person or Windows opened it again; launching would add a window, 0.5 experiment 5).
2. If such a process exists **without windows** (`"background": true`, startup boost): launch anyway (the command is forwarded to it;
   best effort, not verified), and accept whatever comes back.
3. Launch: `CreateProcess` of the recorded exe path with the recorded restart command (exactly as registered: it already carries
   `--restore-last-session --restart` and the profile switches), working directory = the exe's folder, `UseShellExecute = false`,
   environment = Trayloom's minus every `TRAYLOOM_*` variable. Never `--profile-directory` added by Trayloom: Chrome opens every
   profile that was open (0.5).
4. Match windows and re-apply placement (6.3).

### 3.6 Prompts, "On startup", profiles

* "Close all tabs?" (Edge), "Leave site?" and download warnings are window-close prompts; the Restart Manager path does not raise
  them (verified for Edge). Pages close the way they do when Windows restarts for an update; whether a page's own "Leave site?"
  handler runs is not observable from outside (open question 15.3).
* "On startup" (new tab page / continue / specific pages) does not matter: `--restore-last-session` overrides it (0.5).
* Several profiles in one instance: one main process serves all of them and they all come back (Chrome reopens its last opened
  profiles). A managed browser whose policy forbids session restore is a known limit: Trayloom then sees fewer windows than recorded
  and reports "didn't open again as before" (9.5).

### 3.7 Firefox, Brave, Vivaldi, Opera

Only minimized in 0.4.0 (kind `browser-min`, reason `Unsupported`), row caption and "Last time" line explain it. Reasons: none is
installed here, so the restart command and session behaviour cannot be verified; Firefox private windows and Vivaldi/Opera private
windows have no verified signal. Firefox is the most promising (it registers for restart and resumes its session after an
OS-requested end, Bug 603903); enabling it later = add `firefox.exe` to the `browser` kind with a verified private-window rule and the
isolated test of 11.4 with `-profile <temp> -no-remote`. Brave is Chromium and would use the Chrome rule with brand `" - Brave"`;
enable only after the isolated test passes on a PC with Brave (open question 15.1).

---------------------------------------------------------------------------------------------------------------------

## 4. Other apps

### 4.1 Grouping

All processes of the same listed exe (excluding `--type=` helpers) that own top-level windows form one app. Each process is
recorded separately (own command line), so two copies of a classic app with two different files come back as two.

### 4.2 Closing (`WM_CLOSE`)

On the close worker thread, all apps in parallel inside one polling loop (200 ms step):
1. Checks that keep it open: elevated / other user / other desktop / hung (`IsHungAppWindow` on any of its windows → `NotResponding`).
2. Read the command line (3.1 method). If it cannot be read the app is still closed and later started without arguments (logged
   `args=unknown`).
3. Record (7.2) with state `closing` and write `closed-apps.json`.
4. Remember the set of the process's top-level windows and whether each is enabled; then `PostMessageW(hwnd, WM_CLOSE, 0, 0)` to each
   visible, unowned top-level window, bottom of the Z order first (so the window used last closes last; apps that remember "the last
   window" keep the right one). `PostMessage` never blocks on a hung app.
5. Watch until one of:
   * **Exited** (wait handle signalled) → `closed`.
   * **Prompt**: within 10 s a new visible top-level window of that process appears that is owned (`GetWindow(GW_OWNER)`) by one of its
     windows, or has class `#32770`, or one of the recorded windows becomes disabled (`!IsWindowEnabled`: a modal loop) and stays so for
     600 ms. → stop waiting, answer the prompt with "Cancel" (4.3), reason `Prompt`, state `kept`.
   * **Windows gone, process alive** for 3 s (a minimize-to-tray app) → `closed` with `"tray": true` (reopen 4.5).
   * **Timeout** (`CloseTimeoutMs`) with windows still present → `kept`, reason `Timeout`. No second message is sent.
   * **Hung** during the wait → `kept`, reason `NotResponding`.
6. Kept apps are minimized again (`ShowWindowAsync(SW_SHOWMINNOACTIVE)` on their recorded windows, since a prompt may have restored
   one) and stay in the 0.3.0 window record, so they come back like any minimized window.
7. **Late exit**: a kept app (reason `Timeout`) whose process exits later while still away closed because Trayloom asked; it becomes
   `closed` (watched until return).

### 4.3 Answering a prompt with "Cancel"

Only for a prompt window of class `#32770` (standard dialog, `MessageBox`, `TaskDialog`): `PostMessageW(dialog, WM_CLOSE, 0, 0)`.
The dialog manager turns `WM_CLOSE` into `IDCANCEL` when the dialog has a Cancel action, and ignores it when it has none (e.g.
Yes/No only). Cancel in a "Save changes?" prompt aborts the close: the app and its unsaved work stay exactly as they were. After 1 s:
dialog gone → done; still there → leave it alone (logged `prompt left open`; with the cover on it is hidden under the cover). Any
other prompt class (custom Office/Electron dialogs) is left alone. Nothing else is ever sent to a prompt.

### 4.4 Packaged (Store) apps

* AUMID from `GetApplicationUserModelId(hProcess)` (`OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)`), stored with the record.
* UWP apps: `WM_CLOSE` to their `ApplicationFrameWindow`; "closed" = the frame window is gone (the hosted process may linger
  suspended; it is not waited for).
* Packaged desktop apps (MSIX Win32, e.g. Notepad on Windows 11, WhatsApp): like `app`, reopened through the AUMID.
* Reopen: `Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + aumid) { UseShellExecute = true })` (same as
  `NotificationsService`); the app restores what it restores.

### 4.5 Reopening other apps

1. Never when Trayloom is elevated (the feature is off then), never for a record not in state `closed`.
2. Already running: if a process of that exe path exists and owns a visible top-level window → skip (`AlreadyOpen`), except
   `"tray": true` records, where the exe is started without arguments so the app shows its window (single-instance apps hand the
   launch to the running copy).
3. Launch: `CreateProcess` (`UseShellExecute = false`, so an exe that requires administrator fails with `ERROR_ELEVATION_REQUIRED`
   instead of elevating; that record becomes `failed`), exe path + recorded arguments (everything after argv[0] as read, unchanged),
   working directory = the exe's folder (the original directory is not read: that would need foreign memory reads), environment
   without `TRAYLOOM_*`.
4. Apps that restore their own windows and documents (VS Code, Office, Notepad, browsers) are only started; Trayloom never opens
   documents itself.
5. Match windows and re-apply placement (6.3).

---------------------------------------------------------------------------------------------------------------------

## 5. State machine changes

### 5.1 Diagram

`AwayState` is unchanged (`Present`, `GoingAway`, `Away`, `Returning`). Two jobs are added beside it:

```
Present --(trigger)--> GoingAway --(1 s verify)--> Away
Away: CloseJob  None --(awayModeCloseApps && list not empty && delay reached)--> Closing --> Done
Away --(return trigger)--> Returning:
    1. cancel/finish CloseJob (5.3)
    2. ReopenJob starts launches                     (background, at once)
    3. restore minimized windows (spec 06 4.5)        (existing worker, focusActive: false)
    4. ReopenJob: wait, match, place (6.3)            (budget 30 s)
    5. final re-stack + focus over both sets (6.4)
    --> Present
```

* `CloseJob` starts in `AwayTick` when `State == Away`, not simulated-without-dev-filter (11.1), `awayModeCloseApps`, Trayloom not
  elevated, and `now - awayStart >= awayModeCloseAfterMinutes * 60000` (0 = right after the 1 s verify of `GoingAway`). Only once per
  away period.
* Closing never runs while suppressed conditions would have prevented away (they already did) and never in a remote session
  (`SystemInformation.TerminalServerSession` true → no close; logged).
* With "Lock the PC too", the lock happens first (privacy at once); closing continues behind the lock screen.
* The sweep (spec 06 4.4 step 8) keeps minimizing new windows; windows of apps being closed are not re-recorded.

### 5.2 Timing

`closing` → `closed` for browsers is about 0.2 s; other apps at most 10 s each, in parallel; the whole job is bounded by
`MaxCloseApps` and ends at most ~25 s after it started.

### 5.3 The person comes back during closing

`Return(trigger)` while `CloseJob` runs: set its cancel flag; the RM watchdog calls `RmCancelCurrentTask`; no further app is
started. The restore worker waits for the job to finish, at most 3 s more (apps already sent `WM_CLOSE` get that long to exit or show
their prompt). Then: exited → `closed` (reopened at once); prompt → cancelled as 4.3, `kept`; not yet decided → `kept` with
`Cancelled`, and a late exit of such an app during the next 10 s is reopened by the reopen job (it watches `kept` processes until its
budget ends). The person sees their minimized windows come back immediately; reopened apps follow.

### 5.4 Other transitions

* `awayModeCloseApps` switched off while Away: nothing new is closed; apps already closed are reopened on return as usual.
* `awayModeEnabled` off while Away, `Stop()` (quit, uninstall): return as today **and** reopen (bounded: `Stop()` starts the launches
  synchronously, which takes milliseconds, and leaves matching to the background worker until process exit). Exception: the session is
  ending (7.4).
* Away trigger while Returning or Reopening: ignored (existing rule) until 10 s after Present.

---------------------------------------------------------------------------------------------------------------------

## 6. Reopening: launching, matching windows, placement, order, focus

### 6.1 Launch order and budget

All launches start at once (browsers first). Budget 30 s overall, 20 s per browser. A record whose launch throws or whose app shows no
window within its budget becomes `failed`.

### 6.2 Which new windows belong to the reopened app

Before launching, remember every existing top-level window handle. New candidates are visible or iconic, unowned, titled top-level
windows not in that set, whose process has the recorded exe path (packaged: the AUMID's package) and a start time at or after the
launch minus 2 s (single-instance hand-offs reuse an older process: then any *new* window of that exe counts). Collection ends when
the count reaches the recorded count, or 2 s pass without a new window after the first one, or the budget ends.

### 6.3 Matching and placement

* Pair new windows with recorded ones greedily by the smallest distance between the new window's normal rectangle
  (`GetWindowPlacement.rcNormalPosition`, valid also while iconic) and the recorded one (sum of edge differences); ties by order of
  appearance. Browsers restore their own bounds, so this pairs them correctly (verified: Chrome came back within 4 px of the recorded
  rectangles). Extra new windows are left as they are; recorded windows without a match are counted "not back".
* Apply, per pair, without blocking (all async, on the reopen worker):
  1. Convert the recorded normal rectangle (workspace coordinates, as `GetWindowPlacement` gives) to screen coordinates with the
     work-area offset of the monitor it is on (`MonitorFromRect`, `rcWork - rcMonitor`), sanitise it with
     `WindowPlacementMath.Sanitize` when it is not `IsComfortablyVisible` (a display may be gone), in the window's DPI context
     (`WinApiDpi.EnterWindowDpiContext`).
  2. `ShowWindowAsync(new, SW_SHOWNOACTIVATE)` if iconic; `SetWindowPos(new, 0, rect…, SWP_NOZORDER | SWP_NOACTIVATE |
     SWP_ASYNCWINDOWPOS)`.
  3. Recorded maximized → `ShowWindowAsync(SW_SHOWMAXIMIZED)` (the normal rectangle set in 2 stays the restore size); recorded
     minimized before the person left → `ShowWindowAsync(SW_SHOWMINNOACTIVE)`.
* The old → new handle map is kept in memory for 6.4.

### 6.4 Order and focus

After both the window restore (spec 06 4.5 steps 1-3 and 5, called with `focusActive: false`) and the reopen placement finish, one
final pass re-stacks **all** records top-down with the existing `SetWindowPos(…, SWP_ASYNCWINDOWPOS)` chain (spec 06 4.5 step 4),
substituting new handles for reopened windows, then focuses the recorded active window (its new handle when it was in a reopened
app) with `WinUtil.ForceForeground`. Launched apps usually take the foreground themselves; the final focus call puts it back where
the person was. Log line 10.2.

### 6.5 Results

Per record: `reopened` (window count matched or more), `partial` (fewer windows than recorded), `failed` (no window / launch error),
`AlreadyOpen`. `reopened` and `AlreadyOpen` records are removed from `closed-apps.json`; `partial` is removed too (the app is open; the
person can see it); `failed` records stay for the recovery card (9.4) and a notification is shown (9.5).

---------------------------------------------------------------------------------------------------------------------

## 7. Persistence, crash, sleep, sign-out and shutdown

### 7.1 Files

* `away-mode\session.json` (spec 06 4.6) is unchanged in format and lifetime; windows of closed apps simply fail the `StillMatches`
  check there.
* New `away-mode\closed-apps.json`, its own lifetime: written atomically (temp + `File.Move(…, overwrite: true)`), synchronously,
  before the first close message and after each state change; deleted when it has no record left. Never through the debounced
  settings store.

### 7.2 Format (v1)

```json
{ "v": 1, "ownerPid": 1234, "ownerStart": 133456789012345678, "since": "2026-11-05T14:05:00Z", "closedAt": "2026-11-05T14:05:02Z",
  "apps": [
    { "id": "a1", "kind": "browser", "exe": "c:\\program files (x86)\\google\\chrome\\application\\chrome.exe",
      "args": "--source-shortcut-location=taskbar --restore-last-session --restart", "aumid": null,
      "udd": "", "pid": 4412, "pidStart": 133456780000000000, "how": "rm", "state": "closed", "reason": "",
      "tray": false, "background": false, "active": true,
      "windows": [ { "z": 0, "show": "normal", "normal": [100, 80, 1500, 980] }, { "z": 3, "show": "max", "normal": [0, 0, 1280, 900] } ] }
  ] }
```

* `z` is in the same numbering as `session.json` (0 = top of all recorded windows; pre-minimized windows get z after all visible
  ones), so the final stack interleaves correctly. `show` is `normal|max|min`. `udd` is "" for the default profile folder.
* `active` marks the app that owned the window the person was working in.
* `kept` records are written too (with `reason`) so `dump` and the "Last time" list can show them; they are removed at return.
* **No window titles, no URLs, no tab counts.** Paths and arguments are personal data: only in this local file, removed as soon as the
  app is reopened or the person presses "Forget them".

### 7.3 Start of Trayloom with an earlier run's file

`RecoverEarlierRun` (in `AwayModeFeature.Start`, after the 0.3.0 window recovery) reads `closed-apps.json`:
* Owner alive (same pid and start time) → it is this run's file; nothing to do.
* Older than 7 days, unreadable, or `v` unknown → delete, log.
* `closing` records: process (pid + start time) still alive → it did not close → drop the record; gone → treat as `closed`.
* Records `closed` / `failed` / `reopening` whose app is already running with a window → drop (`AlreadyOpen`).
* Remaining records → **ask, do not reopen by itself**: the recovery card (9.4) in the tray Quick toggles card and the Settings card,
  plus one notification (9.5). Reasons: at sign-in the person may not be at the PC; Windows or the browser may already have started it;
  a surprise launch of several apps at login is worse than one click.
* The file stays until "Open them again" (then reopened with placement but no re-stacking and no focus change) or "Forget them".

### 7.4 Sign-out and shutdown while away

* `SystemEvents.SessionEnding` sets `_sessionEnding` (subscribed in `AwayController.Start`, removed in `Stop`).
* `Stop()` with `_sessionEnding`: stop the close job (cancel RM), restore minimized windows as today (bounded 3 s), but **do not
  reopen** closed apps (they would start during the shutdown and be closed again by Windows) and keep `closed-apps.json` with every
  closed record. The next sign-in offers them (7.3). Log `[away] session ending: 2 closed apps kept for the next start`.
* Windows' own "Restart apps after signing in" only restarts apps that were running at sign-out; apps Trayloom closed are not among
  them, so there are no duplicates; browsers Trayloom kept (minimized) may be restarted by Windows itself, which is their normal
  behaviour.

### 7.5 Sleep, crash, kill, reboot

* Sleep while away: closed apps stay closed; after resume the state is still Away; return reopens (existing rules).
* Crash or kill of Trayloom while away: the 0.3.0 window recovery runs at the next start; closed apps are offered (7.3).
* Reboot while away: same as sign-out (7.4) when Trayloom saw `SessionEnding`, otherwise as a crash. Either way the card offers them.

---------------------------------------------------------------------------------------------------------------------

## 8. Edge cases

* **Came back during closing**: 5.3.
* **An app was already closing** (the person closed it just before leaving, or it is exiting): when its process has exited or has no
  top-level windows by the time Trayloom would send the first message, it is not recorded, not closed and never reopened ("never
  reopen what Trayloom did not close").
* **The person closes or starts the app while away** (remotely, another user on the PC): a closed app that is running with a window at
  return → `AlreadyOpen`, not launched again.
* **Same app, several windows**: one app record per process, all windows recorded, closed bottom-up, matched on reopen (6.3).
* **Same exe, several processes**: one record each, each relaunched with its own arguments.
* **Browser with incognito open**: kept, minimized, reason `Private`; the private window survives untouched.
* **Browser downloading**: kept, `Download`.
* **Browser in full screen or playing video**: Away mode was suppressed anyway (existing rules).
* **PWA / app windows of a browser** (`--app-id=` windows of the same main process): they close with the browser; Chromium restores
  app windows with the session (best effort; not verified).
* **UWP / packaged apps**: 4.4. Apps whose package is gone at reopen → `failed`.
* **Trayloom's own windows** (Settings, covers, island): never candidates (pid check), as in 0.3.0.
* **Elevated windows**: never closed (UIPI also blocks the messages); they are not minimized either (0.3.0 drops them).
* **Elevated Trayloom**: the switch is disabled with its reason; `CloseJob` never starts (checked again at start, since Trayloom may
  have been restarted elevated with the switch on).
* **RDP**: Away mode does not start while not on the console (existing); if the person returns over Remote Desktop, the reopened apps
  start in the remote session, which is the same session - fine.
* **Multiple monitors**: placement uses each window's own monitor and is sanitised when that monitor is gone (6.3); DPI context per
  window.
* **Display layout changed while away**: same.
* **Recorded app uninstalled or updated while away**: exe missing → `failed`; a browser updated in place keeps its path and its
  restart command still works (Chrome's own update relaunch uses the same mechanism).
* **The restart command contains `--remote-debugging-port`** (test instances only): relaunched as is (needed by the test of 11.4).
* **Apps that hide to the tray on close**: 4.2 step 5 / 4.5 step 2.
* **Apps that restart themselves after close** (some updaters): a new process appearing while away is ignored; at return the app is
  `AlreadyOpen`.
* **A prompt that is not a `#32770`** (custom dialog): left open, app kept and minimized; the dialog is hidden under the cover if the
  cover is on, otherwise visible: said in the "Last time" list.
* **Dev restriction** `away-mode-only.txt`: limits the snapshot, sweep, close and reopen to the listed test processes (11.1).
* **The list contains Trayloom, Explorer or a terminal**: shown "Can’t be closed" and never closed (2.2).
* **More than 20 listed apps running**: the first 20 by Z order are closed, the rest stay minimized (`Limit`).

---------------------------------------------------------------------------------------------------------------------

## 9. UI

All strings through `Loc.T` / `Loc.F` in `AwayStrings.cs`, English verbatim (typographic ’ and “ ” as written). Never the Vorssaint
name. Every new interactive control has `AutomationProperties.AutomationId` and a Name equal to its label.

### 9.1 Settings card (`quick-tools#awayMode`)

Changed strings:
* Main switch caption (`AwayStrings.Description`): "Minimizes your windows when you leave the PC and brings them back, in the same
  order, when you return. Nothing is closed unless you choose apps to close below."
* Keywords: add `"close"`, `"browser"`, `"tabs"`, `"reopen"`.
* "Go away now" caption when closing is on and the delay is 0: "Your windows are minimized and the chosen apps are closed at once.
  Move the mouse to bring everything back." (with face on: "…Look at the camera to bring everything back.")

New block, inserted after the face section and before the "Stay here when" divider:

| Row | Control | Strings |
|---|---|---|
| divider | | |
| `SettingsRow("\uE8BB", …)` | `MacSwitch` → `awayModeCloseApps` (rebuilds the card) | "Close apps while I’m away" / "Chosen apps are closed when you leave and opened again when you’re back. Chrome and Edge come back with the same windows and tabs. An app that asks to save something is left open and minimized, so nothing is lost." |
| (same row, disabled, when Trayloom is elevated) | | caption "Not available while Trayloom runs as administrator: the apps would open again as administrator." |
| `PickerRow<int>` (inset, switch on) | → `awayModeCloseAfterMinutes` | "Close them" / "When I leave", "After 5 minutes away", "After 15 minutes away", "After 30 minutes away", "After 1 hour away" |
| `AppBundleList` (inset, switch on) | → `awayModeCloseList`, accessory per row | title "Apps to close", add button "Add an app…", caption "Only these apps are closed. The rest are minimized as usual." |
| row accessory (11.5 secondary) | | `browser`: "Windows and tabs come back"; `browser-min`: "Only minimized: Trayloom can’t reopen its tabs yet"; `packaged` / `app`: "Opens again when you’re back"; never closable: "Can’t be closed by Trayloom" (orange) |
| note (inset, 11.5 secondary, switch on) | | "Private and incognito windows can’t be reopened, so a browser with one open, or with a download running, is only minimized. Browsers close the way they do when Windows restarts for an update." |
| "Last time you were away" (inset, only after a return with kept or failed apps; 12 semibold) | list of lines `"{app name}: {reason}"` (11.5) | reasons below |

Reasons (shown in "Last time you were away" and, for kept apps, in the panel tooltip):
* `Private`: "a private window was open, so it was only minimized"
* `Download`: "a download was running, so it was only minimized"
* `OtherDesktop`: "it has windows on another desktop, so it was only minimized"
* `Unsupported`: "Trayloom can’t reopen its tabs yet, so it was only minimized"
* `Prompt`: "it asked to save something, so it was left open"
* `Timeout`: "it didn’t close in time, so it was left open"
* `NotResponding`: "it wasn’t responding, so it was left open"
* `Refused`: "it refused to close, so it was left open"
* `Elevated`: "it runs as administrator, so Trayloom can’t close it"
* `OtherUser`: "it runs as another user, so Trayloom can’t close it"
* `Cancelled`: "you came back before it closed"
* `Limit`: "too many apps to close at once, so it was only minimized"
* `failed`: "it didn’t open again"
* `partial`: "it opened again with fewer windows"

Status line (`AwayMode.Status`) additions:
* While Away with closed apps: `Loc.F("Away since {0} · {1} windows · {2} apps closed", time, windows, apps)`
* While waiting for the delay: `Loc.F("Away since {0} · {1} windows · apps close at {2}", time, windows, closeTime)`
* While closing: "Closing apps…"
* While reopening: `Loc.F("Opening {0} apps…", n)`

AutomationIds: `AwayMode.CloseApps`, `AwayMode.CloseAfter`, `AwayMode.CloseList`, `AwayMode.LastTime`, plus the recovery card's
`AwayMode.Reopen`, `AwayMode.Forget`.

### 9.2 Tray flyout (Quick toggles card, `AwayPanelSection`)

* While Away with closed apps: the "Restore windows" row gets the caption `Loc.F("And opens {0} closed apps again", n)`.
* While reopening: that row reads `Loc.F("Opening {0} apps…", n)` and does nothing on click.
* Recovery pending (7.3): an extra `PanelActionRow("\uE8A7", Loc.F("Open {0} closed apps again", n), "Closed while you were away", …)`
  → reopen; AutomationId `AwayMode.PanelReopen`. Shown also when Away mode's switch is off.

### 9.3 The cover (primary display only, under "Since …", 12, white 0.6)

* While closing: "Closing your apps…"
* After closing: `Loc.F("{0} apps closed. They open again when you’re back.", n)`; when some were kept, a second line
  `Loc.F("{0} apps were only minimized.", k)`.
* No cover text for reopening: the cover closes at return as today; progress is in the tray card and Settings.
`AwayCoverWindow` gets `SetCloseStatus(string? line1, string? line2)` called on the UI thread.

### 9.4 Recovery card ("reopen what was closed")

At the top of the Away mode Settings card (above the main switch), when 7.3 left records, a bordered card (orange glyph `\uE7BA`):
* Title (12.5 semibold): "Apps closed while you were away"
* Text (11.5): `Loc.F("Trayloom closed {0} apps when you left at {1}, and they were not opened again because Trayloom or Windows stopped first.", n, since.ToString("g"))`;
  for records that failed at return instead: `Loc.F("{0} apps did not open again when you came back.", n)`.
* The app names, one per line (display names from the exe; never arguments).
* Buttons: accent "Open them again" (`AwayMode.Reopen`), bordered "Forget them" (`AwayMode.Forget`).

### 9.5 Notifications (`INotifier`, title "Away mode"; messages contain counts only, because the notifier logs them)

* At start with records (7.3): `Loc.F("{0} apps were closed while you were away. Open them again from the tray icon or from Settings.", n)`
* After a return with `failed` records: `Loc.F("{0} apps did not open again. Settings > Away mode can try again.", n)`

### 9.6 Actions and Command Bar

`FeatureAction`s: new `reopen` ("Open closed apps again", glyph `\uE8A7`; only while recovery records exist and not Away).
Dev only (11.2): `dev-close-now`, `simulate-session-end`, extended `dump`.

Command Bar (`BarCatalog.cs`, block E1 in 12.2): `action.awayReopen` via `Exact(ctx, "away-mode", "reopen")`, subtitle
"Away mode", glyph 0xE8A7, keywords "Away mode reopen closed apps browser tabs".

### 9.7 Catalog description

`FeatureCatalog.cs` away-mode line, description becomes: "Minimizes your windows when you leave the PC and brings them back in the
same order when you return, when the camera sees you if you like. Can also close the apps you choose and open them again, browsers
with their tabs."

---------------------------------------------------------------------------------------------------------------------

## 10. Privacy and logging

### 10.1 Rules

* Stored: exe paths, arguments, AUMIDs, user-data-dir, window rectangles and show states, in `closed-apps.json` only, deleted after
  reopening or "Forget them". Not stored anywhere: window titles, tab titles, URLs, profile names, download file names.
* In memory only during a close pass: `BrowserRootView` names, profile display names, download folder paths.
* The log keeps the 0.3.0 rule (counts only, never titles or paths). App kinds are logged, never names.

### 10.2 Log lines (exact forms)

* `[away] close: record written (3 apps)` (after `closed-apps.json` is on disk, before any message is sent)
* `[away] close: 3 apps (2 browsers, 1 other): closed 2, kept 1 (prompt 1), only minimized 0, in 1.4 s`
* `[away] close: browser kept (private|download|other-desktop|unsupported|refused|cancelled|elevated)`
* `[away] close: app kept (prompt|timeout|not-responding|elevated|other-user|cancelled|limit)`; `[away] close: prompt cancelled` /
  `[away] close: prompt left open`
* `[away] close: rm shutdown 0 in 109 ms` (the RM result code and time; one line per browser)
* `[away] close: skipped (remote session|elevated Trayloom|dev filter)`
* `[away] reopen: 2/2 apps, windows 5/5 placed, 0 failed, 0 already open, in 3.4 s`
* `[away] back (input) after 14 min: restored 12/12, late 0, closed 3, z-order ok, focus ok` (existing line; `closed` counts windows of
  closed apps that are reopened separately)
* `[away] closed apps from an earlier run: 2 offered` / `[away] closed apps: forgotten 2` / `[away] session ending: 2 closed apps kept
  for the next start`
* `dump` adds `apps: [{kind, how, state, reason, windows}]` (no paths) and `closeJob: none|waiting|closing|done`.

---------------------------------------------------------------------------------------------------------------------

## 11. Developer aid and tests that cannot harm the person

### 11.1 The dev filter (`away-mode-only.txt`) becomes `AwayDevFilter`

Still honoured only with `TRAYLOOM_DEV=1`, still one process name per line. New rules:
* A process passes when its name is listed **and**, when that name is a browser exe (`chrome`, `msedge`, `brave`, `vivaldi`, `opera`,
  `firefox`), its main process command line has `--user-data-dir=` (or `-profile`) pointing under `TRAYLOOM_DATA` or under
  `Path.GetTempPath()`. A browser process whose command line cannot be read never passes.
* The filter applies to the snapshot, the sweep, the close job and the reopen (reopen only of records created under the filter).
* `simulate-away` with closing on works only with the filter active (as today).
* Implementation: `AwayWindows.DevOnlyProcesses()` is replaced by `AwayDevFilter.Load()` returning null or a filter with
  `bool Allows(uint pid, string processName)`; `Candidates()` calls `only.Allows(owner.Pid, owner.Name)`.

This is what keeps the person's Chrome safe during the browser test: its processes are named `chrome` but their profile is in
`%LOCALAPPDATA%`.

### 11.2 Dev actions (only with `TRAYLOOM_DEV=1`)

* `dev-close-now`: while Away, start the close job now regardless of the delay (needs the filter).
* `simulate-session-end`: sets `_sessionEnding` and runs the 7.4 path of `Stop()` without stopping the feature (keeps
  `closed-apps.json`, reopens nothing), for testing the recovery card.
* `reopen` is a user action (9.6); agents run it only for records made under the filter.
* `dump` as 10.2.

### 11.3 AwayDummy extensions (`tests/AwayDummy/Program.cs`)

New arguments (existing behaviour unchanged when none is given):
* `--mode closable` (default when any new flag is used): the process exits when its last window closes (`Application.Exit` from
  `FormClosed` of the last form).
* `--mode asks`: `FormClosing` shows `MessageBox.Show("Save changes to Away dummy?", "Away dummy", MessageBoxButtons.YesNoCancel)`;
  Cancel → `e.Cancel = true` (the app stays); Yes/No → close. Tests the prompt + Cancel path (`#32770`).
* `--mode refuses`: `FormClosing` sets `e.Cancel = true` silently (timeout path).
* `--mode slow --slow-ms 15000`: `FormClosing` hides nothing and exits after the delay (late exit path, 4.2 step 7).
* `--mode tray`: `FormClosing` cancels and hides the form, the process keeps running (tray path).
* `--tag <text>`: any text, echoed in the report, so a relaunch with the same arguments is visible.
* The report gains a header line `pid=<n> start=<filetime> args=<all args> mode=<mode>` and keeps the per-window lines. Because the
  relaunch reuses the arguments, the relaunched dummy writes to the same `--report` file.

### 11.4 Test flows (agents)

Environment as spec 06 5.0 (scratch `TRAYLOOM_DATA`, `TRAYLOOM_INSTANCE`, `TRAYLOOM_DEV=1`, `TRAYLOOM_NO_INSTALL_PROMPT=1`,
`TRAYLOOM_AWAY_NO_CAMERA=1`), `away-mode-only.txt` written first, `"awayModeFace": false`, `"awayModeLockPc": false` seeded.

A. **Dummy close and reopen**: `away-mode-only.txt` = `AwayDummy`; seed `awayModeCloseApps: true`, `awayModeCloseList: ["<full path of
   AwayDummy.exe, lower-case>"]`, `awayModeCloseListSeeded: true`. Start `AwayDummy --windows 3 --mode closable --tag T1 --report
   <scratch>\d1.txt`. `simulate-away` → log `[away] close: 1 apps … closed 1`; dummy pid gone; `dump` shows state `closed`.
   `simulate-return` → report has a new `pid=` with `args=` containing `--tag T1`, three windows at the recorded rectangles (±8 px),
   window 2 `max`, window 1 `fg=1`; log `[away] reopen: 1/1 apps, windows 3/3 placed`; `closed-apps.json` gone.
B. **Asks to save**: dummy `--windows 1 --mode asks`. `simulate-away` → log `prompt cancelled`, `kept 1 (prompt 1)`; dummy still
   running, its window minimized; `simulate-return` → window restored, nothing relaunched, "Last time you were away" lists it (UIA).
C. **Refuses** (`--mode refuses`): after 10 s `kept 1 (timeout 1)`. **Slow** (`--mode slow --slow-ms 15000`): kept at 10 s, becomes
   closed at ~15 s (late exit), reopened on return. **Tray** (`--mode tray`): `closed` with tray; return starts the exe without
   arguments (report shows a new `pid=`; the old one still running - expected for the dummy).
D. **Return during closing**: dummy `--mode slow --slow-ms 2500`, `simulate-away` then `simulate-return` at once → the restore waits,
   the dummy exits at 2.5 s and is reopened.
E. **Crash path**: A up to "closed", then `Stop-Process` on the **dev Trayloom pid only**, start it again → log `closed apps from an
   earlier run: 1 offered`; UIA finds `AwayMode.Reopen`; invoke it → dummy reopened, file gone. Repeat with `simulate-session-end`
   instead of the kill and `--quit` / start.
F. **Isolated browser test (Chrome or Edge)** - never the person's profile:
   1. `$udd = "$env:TRAYLOOM_DATA\away-browser\udd"`; pages `tab1.html … tab4.html` in `$env:TRAYLOOM_DATA\away-browser\pages`
      (`<title>Away test tab N</title>`).
   2. `chrome.exe --user-data-dir="$udd" --no-first-run --no-default-browser-check --remote-debugging-port=9333 file:///…tab1.html
      file:///…tab2.html`; wait 7 s; `chrome.exe --user-data-dir="$udd" --new-window file:///…tab3.html file:///…tab4.html` (do not
      repeat the debugging switch, 0.5).
   3. Record `Invoke-RestMethod http://127.0.0.1:9333/json/list` (type `page`) URLs; record the pids of every chrome process **not**
      containing `$udd` (the person's), to compare at the end.
   4. `away-mode-only.txt` = `chrome`; seed `awayModeCloseApps: true`, `awayModeCloseList: ["c:\\program files (x86)\\google\\chrome\\
      application\\chrome.exe"]`, `awayModeCloseListSeeded: true`.
   5. `simulate-away` → log `close: 1 apps (1 browsers…) closed 1`, `rm shutdown 0`; no process with `$udd` left; the person's chrome
      pids all still exist and **their windows were not minimized** (filter).
   6. `simulate-return` → the same 4 URLs in `/json/list` (order may differ), 2 windows of the new main process, not iconic, at the
      recorded rectangles (±8 px); log `reopen: 1/1 apps, windows 2/2 placed`.
   7. Private: launch `chrome.exe --user-data-dir="$udd" --incognito file:///…tab1.html`, `simulate-away` → `browser kept (private)`,
      all 3 windows minimized, none closed; `simulate-return` → restored; the incognito window still exists.
   8. Download: before step 2 seed `$udd\Default\Preferences` with `{"download":{"default_directory":"<scratch>\\dl"}}`, create
      `<scratch>\dl\x.crdownload` and touch it within 30 s of closing → `browser kept (download)`.
   9. Edge: the same with `msedge.exe` and `--inprivate` (`browser kept (private)`).
   10. Cleanup: let Trayloom close the test browser (`simulate-away` with closing, then quit the dev copy and use "Forget them"), or a
       scratch helper calling `RmShutdown` on the test main pid; delete `$udd`. The test never uses `Stop-Process` on a browser.

Not for agents (person only, TESTING.md rows in section 13): the person's real Chrome/Edge, Office save prompts, sign-out and reboot while away,
real downloads.

---------------------------------------------------------------------------------------------------------------------

## 12. Files to create and edit (exact anchors)

Namespace `Trayloom.Features.WindowTools.AwayMode`, header, style and UTF-8 without BOM as spec 06 0.2. No new NuGet package
(UI Automation comes with WPF's framework reference; Restart Manager and the rest are P/Invoke).

### 12.1 New files

| File | Content |
|---|---|
| `src/Trayloom.Features.Windows/AwayMode/AwayApps.cs` | `AwayAppKind`, `AwayKeepReason`, `AwayAppState`; classification (2.1, 2.2), grouping (4.1), the close job (`AwayCloseJob`: thread, polling loop, cancel, 4.2, 4.3, late exits) |
| `src/Trayloom.Features.Windows/AwayMode/AwayBrowsers.cs` | browser table, main-process discovery, user-data-dir, `Local State` / `Preferences` reading, private-window check (3.3), download check (3.4), RM close (3.2) |
| `src/Trayloom.Features.Windows/AwayMode/AwayRestartManager.cs` | P/Invoke `RmStartSession`, `RmRegisterResources`, `RmShutdown`, `RmCancelCurrentTask`, `RmEndSession`, `GetApplicationRestartSettings`, `GetProcessTimes`; `Shutdown(pid, start, cancel)` returning code + ms; asserts flags == 0 |
| `src/Trayloom.Features.Windows/AwayMode/AwayProcess.cs` | `OpenProcess` helpers, command line (`NtQueryInformationProcess` 60), elevation and user SID check, `GetApplicationUserModelId`, wait handles, `IsHungAppWindow`, `IsWindowEnabled`, `GetWindow(GW_OWNER)`, virtual desktop check (`IVirtualDesktopManager`) |
| `src/Trayloom.Features.Windows/AwayMode/AwayReopen.cs` | `AwayReopenJob`: launches (3.5, 4.5), new-window collection and matching (6.2, 6.3), placement, handle map, results |
| `src/Trayloom.Features.Windows/AwayMode/AwayClosedApps.cs` | `closed-apps.json` model (7.2), atomic write/read/delete, start-up recovery decision (7.3) |
| `src/Trayloom.Features.Windows/AwayMode/AwayDevFilter.cs` | 11.1 |

### 12.2 Edits

* `src/Trayloom.Features.Windows/AwayMode/AwayModeFeature.cs`
  * `AwayKeys`: after `public const string CameraFailed = "awayModeCameraFailed";` add the four keys of section 1; in `Register`
    after `app.Register(CameraFailed, false);` register them.
  * `Start`: after `RecoverEarlierRun(context, session);` add the closed-apps recovery (7.3) that hands its records to the controller
    (`controller.OfferRecovery(records)` after `controller.Start();`) and shows the notification.
  * `Actions`: after the `else { list.Add(new FeatureAction("away-now", …)); }` block add `reopen` (9.6); inside the dev block after the
    `dump` line add `dev-close-now`, `simulate-session-end`.
  * `SimulateAway`: use `AwayDevFilter.Load()` instead of `AwayWindows.DevOnlyProcesses()`.
  * `Stop`: on uninstall also delete `closed-apps.json` only when it holds no `closed` record (never drop apps the person still wants
    back silently; otherwise keep it).
* `src/Trayloom.Features.Windows/AwayMode/AwayController.cs`
  * Fields: `_closeJob`, `_reopenJob`, `_sessionEnding`, `_recovery`.
  * `Start`: after `SystemEvents.PowerModeChanged += OnPower;` add `SystemEvents.SessionEnding += OnSessionEnding;`; `Stop` removes it
    and follows 7.4 when `_sessionEnding`.
  * `AwayTick`: directly before `if (now - _lastSweep >= SweepMs)` add the close-job start check (5.1).
  * `Return`: before `var worker = new Thread(() => RestoreWorker(records, active, trigger, since))` signal `_closeJob?.Cancel()`.
  * `RestoreWorker`: replace `var result = AwayWindows.Restore(records, active, focusActive: true);` with: wait for the close job (5.3),
    start the reopen job, `AwayWindows.Restore(records, active, focusActive: reopenRecords.Count == 0)`, then when reopening
    `reopen.Finish(records, active)` (6.4). Keep the existing log line and add the 10.2 reopen line.
  * `Sweep`, `GoAway`: use `AwayDevFilter.Load()`.
  * New public members for the UI: `ClosedAppCount`, `CloseJobState`, `LastResult` (kept/failed list with display names, in memory),
    `RecoveryCount`, `ReopenRecovery()`, `ForgetRecovery()`; `Dump` extended (10.2).
* `src/Trayloom.Features.Windows/AwayMode/AwayWindows.cs`: replace `DevOnlyProcesses()` by a call-through to `AwayDevFilter`
  (its callers in `AwayController` and `AwayModeFeature` switch to `AwayDevFilter.Load()`, see above); `Candidates(ISet<string> ignored,
  AwayDevFilter? only)`; add `Restack(IReadOnlyList<AwayRecord> records, IReadOnlyDictionary<long, nint> replaced, nint active,
  bool focus)` extracted from steps 2 and 4 of `Restore` (Restore calls it), so 6.4 can reuse it.
* `src/Trayloom.Features.Windows/AwayMode/AwaySettings.cs`: recovery card at the top (after `var card = new SettingsCard(AwayStrings.Title);`);
  the close block directly **before** the `card.AddDivider();` that precedes `card.Add(UiKit.Text(AwayStrings.StayHereWhen, 12,
  FontWeights.SemiBold));`; `Keywords` extended; `StatusText` extended; "Go away now" caption choice extended.
* `src/Trayloom.Features.Windows/AwayMode/AwayPanelSection.cs`: caption and reopening title on the action row; the recovery row
  added to the `PanelRowGroup` after the action row.
* `src/Trayloom.Features.Windows/AwayMode/AwayCoverWindow.cs`: a collapsed `TextBlock` pair under the "Since" label on the primary
  cover and `SetCloseStatus`.
* `src/Trayloom.Features.Windows/AwayMode/AwayStrings.cs`: every string of section 9; `Description` changed.
* `src/Trayloom.Features.Windows/AwayMode/AwayNative.cs`: nothing (new P/Invokes live in the new files).
* `src/Trayloom.Core/Catalog/FeatureCatalog.cs`: the line starting `list.Add(Def("away-mode", T,` - only the description text
  changes (9.7).
* `src/Trayloom.Features.Tools/CommandBar/BarCatalog.cs` - **E1**, directly after the block
  `if (Has("away-mode") && Exact(ctx, "away-mode", "restore") is { } awayRestore) { … }` (before the blank line and `// keep awake`):
  ```csharp

        if (Has("away-mode") && Exact(ctx, "away-mode", "reopen") is { } awayReopen)
        {
            rows.Add(Simple("action.awayReopen", awayReopen.Title, Loc.T("Away mode"), 0xE8A7, awayReopen.Invoke, 150, Loc.T("Away mode reopen closed apps browser tabs")));
        }
  ```
* `tests/AwayDummy/Program.cs`: 11.3 (argument parsing after the `--report` branch; the forms' `FormClosing`/`FormClosed` handlers;
  the report header). `AwayDummy.csproj` unchanged.
* Release commit (13): `Directory.Build.props`, `src/Trayloom.App/Pages/InfoPages.cs`, `README.md`, `TESTING.md`,
  `docs/DEVELOPMENT.md`.

---------------------------------------------------------------------------------------------------------------------

## 13. Release 0.4.0

* `Directory.Build.props`: `<Version>0.3.1</Version>` → `<Version>0.4.0</Version>` (keep the file's encoding).
* `src/Trayloom.App/Pages/InfoPages.cs`, `_loc_Notes`, "Added": replace the line
  `Loc.T("Away mode: your windows are minimized when you leave the PC and come back in the same order when you return, optionally when the camera sees you. Nothing is closed."),`
  with
  ```csharp
            Loc.T("Away mode: your windows are minimized when you leave the PC and come back in the same order when you return, optionally when the camera sees you."),
            Loc.T("Away mode can also close the apps you choose when you leave and open them again when you’re back. Chrome and Edge come back with the same windows and tabs; an app that asks to save something is left open, so nothing is lost."),
  ```
  and in `Build()` the date in `$"v{AppInfo.Version} · 2026-11-04"` → the release date.
* `README.md`, Tools table, the **Away mode** row becomes:
  `| **Away mode** | Minimizes your windows when you leave and brings them back in the same order when you return, by mouse, unlock or (optionally) when the camera sees you. Optionally closes the apps you choose and opens them again, browsers with their tabs. |`
  and in "Away mode and GitHub, in detail" the first bullet becomes:
  `* **Away mode** minimizes your windows and restores them in the same order. Closing apps is off until you turn on **Close apps
  while I’m away** and choose apps; Chrome and Edge then close the way they do for a Windows update restart and reopen with the
  same windows and tabs. Private windows can’t be reopened, so a browser with one open is only minimized, and so is an app that asks
  to save something. What was closed is remembered on your PC only until it is open again. Face detection is detection, not
  recognition: any face counts, so it cannot tell you from a colleague. For real protection, switch on **Lock the PC too**.`
* `docs/DEVELOPMENT.md`, after the sentence ending `` `TRAYLOOM_AWAY_NO_CAMERA=1` keeps the camera off. ``: "Closing apps
  (0.4.0): `--action away-mode dev-close-now|simulate-session-end|reopen`; with `away-mode-only.txt` a browser name (`chrome`,
  `msedge`, …) only matches a browser started with `--user-data-dir` under `TRAYLOOM_DATA` or the temp folder, so the isolated
  browser test of spec 07 section 11.4 can never touch your own browser. `tests\AwayDummy --mode closable|asks|refuses|slow|tray`
  stands in for other apps."
* `TESTING.md`, after the "Away mode face detection" row (person only):
  * `| Away mode: close and reopen your browser | Turn on "Close apps while I’m away" (Chrome and Edge are added), open Chrome with two windows and a few tabs, one window maximized, press Go away now, wait, move the mouse. Try again with "Close them" after 5 minutes. | Chrome closes within a second, then comes back with the same windows, tabs and positions, and the window you used is in front. Your "On startup" setting does not matter. |`
  * `| Away mode: private windows and downloads | Open an incognito window (or InPrivate in Edge) and go away; then start a large download and go away. | The browser is only minimized; Settings > Away mode lists why under "Last time you were away". The incognito window is still there. |`
  * `| Away mode: an app with unsaved work | Add Notepad (or Word) to "Apps to close", type something without saving, go away and come back. | The app asks nothing on screen, is still open with your text, minimized while away; the card says it asked to save something. |`
  * `| Away mode: sign out, restart or crash while away | Go away with Chrome on the list, then sign out (or restart Windows, or end Trayloom in Task Manager), sign in and start Trayloom. | A notification and the tray card offer "Open N closed apps again"; it brings Chrome back with its tabs. "Forget them" removes the offer. |`
  * `| Away mode: two profiles and two displays | Open two Chrome profiles, one window on each display, go away and return; unplug a display while away. | Both profiles come back; windows return to their displays, or onto the remaining one, never off-screen. |`

---------------------------------------------------------------------------------------------------------------------

## 14. Verification checklist

1. `dotnet build Trayloom.sln -c Debug` and `dotnet build tests\AwayDummy\AwayDummy.csproj` without warnings in the new files;
   `--dump-info` lists `away-mode` with actions `away-now` (and `reopen` when records exist) and the dev actions.
2. Flows A-E of 11.4 pass from the log, `dump` and the dummy report.
3. Flow F passes for Chrome and Edge (all URLs back, windows placed, private and download kept), **and the person's Chrome pids and
   window states are unchanged** before/after.
4. `--open-settings quick-tools#awayMode` → UIA finds `AwayMode.CloseApps`; toggling it on (TogglePattern) adds exactly Chrome and
   Edge to `awayModeCloseList` once (`settings.json`), off/on again adds nothing; `AwayMode.CloseAfter` and `AwayMode.CloseList`
   appear; with the dev copy started elevated (person only) the switch is disabled with its caption.
5. `--open-panel toggles` → `AwayMode.PanelReopen` present only with recovery records.
6. Kill test (E) leaves `closed-apps.json`; "Forget them" deletes it; nothing reopens by itself at start.
7. Logs contain no path, title, URL, profile name or app name (`Select-String` the log for `\\`, `file:///`, `Away test`, `Person 1`).
8. Grep the AwayMode folder: no `SendInput`, `keybd_event`, `mouse_event`, `SendKeys`, `InputSender`, `TerminateProcess`, `.Kill(`,
   `taskkill`, `NtTerminate`, `RmForceShutdown`, `RmShutdownOnlyRegistered`, `WriteProcessMemory`, `ReadProcessMemory`, `runas`,
   `Verb =`; `RmShutdown(` is called with the literal flags `0`.
9. `closed-apps.json` written before the first `WM_CLOSE` / `RmShutdown` (log order: `close: record written`, then the `rm shutdown` / close lines).
10. Every new `.cs` file has the GPL header, file-scoped namespace, `Loc.T` on every visible string, no "Vorssaint" in strings.

---------------------------------------------------------------------------------------------------------------------

## 15. Open questions for the person

1. **Brave / Vivaldi / Opera / Firefox**: only minimized in 0.4.0 because they could not be verified here. Install one (or say which
   you use) and the isolated test of 11.4 can enable it; Firefox needs its own private-window rule.
2. **Default timing**: closing starts when you count as away ("When I leave"). Closing and reopening a browser for a 3-minute break
   reloads every page; "After 15 minutes away" may suit you better. Keep "When I leave" as the default?
3. **Pages that warn before leaving** ("Leave site? Changes may not be saved"): the browser closes the way it does for a Windows update
   restart, and whether such a page gets to warn cannot be checked from outside. Web apps that keep unsaved text only in the page
   (rare; Gmail, Docs and most editors save drafts) could lose it. Acceptable, or should browsers with such pages be left alone (not
   detectable reliably today)?
4. **Recovery after a restart or crash**: the next start *asks* (card + notification). Prefer that it reopens by itself when Trayloom
   starts within, say, 1 hour of leaving?
5. **Apps registered with Windows' restart** (Office, some editors) could use the browser path (all windows at once, Windows-update
   style) instead of one `WM_CLOSE` per window. 0.4.0 uses plain close as you asked; say if you want the other.
