# Trayloom

Formerly WinKnife.

Trayloom is a Windows 10/11 tray utility (.NET 9, WPF) that bundles many small system tweaks and tools into one
"install what you need" app. It is a Windows replica of the macOS utility [Vorssaint](https://vorssaint.com): the same
tray panel, Settings window, Features hub, Dynamic Island, Command Bar and feature catalog (79 entries, of which
73 are installable on Windows and 6 are marked unavailable because Windows has no equivalent), rebuilt for Windows. Features are installed or uninstalled one by one from the **Features hub**; whatever is
uninstalled disappears from the whole app and loads nothing.

Trayloom is licensed under the same terms as Vorssaint, **GPL-3.0-or-later** (see `LICENSE`). It is an independent
project and is **not affiliated with or endorsed by Vorssaint**; the Vorssaint name, logo and icon are not used.

* Languages: 15 (System default, English and the 13 other Vorssaint languages), switched live in Settings > General > Language;
  see `specs/L10N_TODO.md` for coverage. The first-run guide, the install / uninstall windows and the Settings pages General, About and Advanced are translated into all 15 languages; some captions of the feature pages are still English.
* Settings: `%APPDATA%\Trayloom\settings.json` (override the folder with the `TRAYLOOM_DATA` environment variable).
* Log: `<settings folder>\logs\trayloom.log`.
* Upgrading from the previous name: the first start closes a still-running old copy, moves the old settings folder and local data
  to the Trayloom names and re-registers Start with Windows (on/off state kept); installing Trayloom also removes the old Start menu
  shortcut and Apps & features entry. The old program folder is left in place for you to delete.
* First run: onboarding, then only the **Essentials** bundle (Volume mixer, System monitor, Keep awake) is installed.
  Everything else is one click away in the Features hub (Install all, bundles, or one by one).
* Every feature undoes its side effects when uninstalled or when Trayloom quits (mouse acceleration, hooks, hotkeys,
  wake locks, windows). Uninstalling *Mute microphone* while muted unmutes the microphone.
* `Trayloom.exe --quit` asks the running instance to shut down gracefully; a second launch (the Start menu shortcut) brings
  Settings forward on the page you used last.

## What is in it

Catalog groups, as in the Features hub (Windows availability noted where it differs from the Mac).

| Group | Features |
| --- | --- |
| Windows and taskbar | Window switcher (Alt+Tab replacement with previews), Taskbar Preview, Taskbar clicks (minimize / hide / cycle), Window layout (41 placements, edge snap, move/resize by dragging), Quit on close. *Unavailable on Windows:* Maximize windows, Keep Spaces in a fixed order. |
| Mouse and keyboard | Invert mouse scrolling, Scroll sideways while holding a key, Zoom, Focus follows mouse, Smooth scrolling, Linear scrolling, Disable mouse acceleration, Side buttons, Mouse button shortcuts, Extra click filter, Debounce, Text snippets, Super key, Quit & close protection. *Unavailable:* Touchpad middle click (the precision touchpad owns its gestures). |
| Clipboard and files | Clipboard history, Paste as plain text, Cut & paste and the F2 rename shortcut for File Explorer, Shelf, Clean URL. *Unavailable:* Disk image installer. |
| Sound | Volume mixer (per-app volume and output), Output switcher, Audio device priority, Mute microphone, Music app blocker. |
| Energy and display | Keep awake (timers, rules, lid closed), Displays (brightness, DDC/CI), Bluetooth sleep. *Unavailable:* Extra brightness. |
| Tools | Quick panel, Quick toggles, Quit apps, Color picker, Copy text from screen, Cleaning Mode, Media, Cleaner, Uninstaller, Packages (winget), App updates, Screenshot (editor, scrolling capture, pins), Screen recording (Windows.Graphics.Capture + Media Foundation), Camera preview, Radial menu, Scratchpad, Command Bar, Wallpaper, Kill Process, Port Manager. |
| Dynamic Island | The island itself plus Calendar, Notifications, Gestures, Timer, Accessory alerts, Lyrics (online lookup is **off** by default), Up next, Bars follow the music, Downloads, **AI Agents** (Claude Code, Codex, OpenCode, Copilot usage and limits, read locally), Watch, Companion. Installing the island switches it on. |
| Monitor | CPU, GPU, Memory, Network, Disks, Power, Connected devices, shown in the tray panel and as a strip next to the tray icon. *Unavailable:* Fan control. |

## AI Agents (Dynamic Island)

Install **AI Agents** (it needs the Dynamic Island) to follow Claude Code, Codex, OpenCode and GitHub Copilot from the island.
Everything is read **locally**; nothing is sent anywhere and no account password is read.

* **Limits** show the session and weekly windows with where the number came from and how old it is ("From Claude Code, 2 minutes
  ago"). Claude's usage comes from what Claude Code reports; Codex asks the local Codex app-server and falls back to its session log
  (older readings are labelled as possibly out of date). If a source is missing the card says so instead of showing a guess.
* **Now** lists live sessions (the one you are typing in, background agents) and what each is doing.
* **Spending** is the *API-equivalent value* of your tokens from the local logs (models, projects, trend). It is what the usage
  would cost at API prices, **not what you pay** on a subscription.
* **Permission alerts**: when a Claude Code session asks for a permission, the island pops out to say which session needs you (you still answer in Claude Code). This uses
  a small hook in Claude's `settings.json` that only reports the event and never changes what Claude decides; Trayloom adds it
  only when you install it from the AI Agents settings (after a backup) and removes it when you remove it there.
* With a very large history (several GB of logs) the first scan can take a moment and the feature keeps a few hundred MB.

## Install and first start

Trayloom is a tray app: after it starts you will find its icon **next to the clock** (Windows often hides new tray icons
behind the **^** arrow). There is no installer program; the installer is `Trayloom.exe` itself.

1. Unzip `Trayloom-win-x64.zip` anywhere (or use `dist\Trayloom`) and double-click `Trayloom.exe`
   (or `Trayloom-Setup.cmd`, which starts the same thing).
2. Trayloom offers **Install Trayloom** (or **Update Trayloom** when an older copy is installed):
   * **Install** copies the app to `%LOCALAPPDATA%\Programs\Trayloom` for your account only (no administrator password),
     adds a Start menu shortcut, an optional Desktop shortcut and an optional "start when I sign in" entry, and registers
     an entry in **Settings > Apps > Installed apps** whose Uninstall runs `Trayloom.exe --uninstall`. It then starts the
     installed copy and you can delete the download.
   * **Just run it** runs from where it is (tick "Don't ask again" to stop the offer). **Install Trayloom...** stays in the
     tray menu and in Settings > General > Getting started.
3. On a fresh profile a short introduction opens: what Trayloom is, a bundle (**Essentials** is preselected, or
   Windows / Battery and quiet, or pick features one by one), Launch at login, and a picture of where the tray icon is.
   A toast says "Trayloom is running" once.
4. Windows does not let apps pin their own icon. To keep it visible: Settings > Personalization > Taskbar > **Other system
   tray icons** > switch Trayloom on (the introduction and Settings > General have a button that opens that page,
   `ms-settings:taskbar`), or drag the icon out of the **^** overflow next to the clock.
5. Start it again from the Start menu while it runs: Settings opens on the page you used last, so you can always get back in.
6. Uninstall: Settings > Apps > Installed apps > Trayloom > Uninstall (or `Trayloom.exe --uninstall`). It quits Trayloom,
   removes the files, shortcuts, the Apps & features entry and the start-at-sign-in entry, and asks whether to delete your
   settings too.

If something does not start, right-click the tray icon > **Report a problem...** opens the folder with the log. A start
that takes longer than 2 s shows a "Starting Trayloom..." toast (the first start after copying new DLLs can take ~10 s).
If Windows **Smart App Control** blocks a file, Trayloom shows one dialog that explains it in plain words and lists the
options (below); it only reads that setting, it never changes it.

`scripts\package.ps1` publishes the self-contained build, adds `Trayloom-Setup.cmd` and writes `dist\Trayloom-win-x64.zip`.
(A self-extracting `Setup.exe` built with Windows IExpress was tried and is silently blocked on a Smart App Control PC, so
`Trayloom-Setup.cmd` is the Setup equivalent.)

Testing the install flow without touching your real Start menu or registry: set `TRAYLOOM_INSTALL_ROOT`,
`TRAYLOOM_STARTMENU_DIR`, `TRAYLOOM_DESKTOP_DIR`, `TRAYLOOM_UNINSTALL_KEY` (an HKCU sub key) and `TRAYLOOM_RUN_KEY`
(an HKCU sub key) to scratch locations, plus `TRAYLOOM_DATA` for the settings. `TRAYLOOM_NO_INSTALL_PROMPT=1` hides the
install offer; builds run from `bin\Debug` / `bin\Release` never show it.

## Hotkeys

macOS ⌘ becomes **Win** in multi-modifier global shortcuts (⌃⌥⌘ = Ctrl+Alt+Win, ⌃⌘ = Ctrl+Win); ⌃ is Ctrl, ⌥ is Alt.
Shortcuts that act inside the front app use Windows conventions. All of them are editable in *Keyboard shortcuts*;
every default is unique (checked by `Trayloom.exe --dump-info <file>`). A shortcut only registers while its feature is
installed **and** its own switch is on, so a fresh install grabs nothing except the ones listed as "always".

| Role | Default | Notes |
| --- | --- | --- |
| Command Bar | Ctrl+Alt+Space | Alt+Space stays with the Windows window menu |
| Window switcher: apps / windows of the app / all windows | Alt+Tab / Alt+` / Ctrl+Alt+Tab | hook based; Alt+Tab replacement is a setting |
| Window layout (per action) | Ctrl+Alt+Left / Right / Up / Down (halves), D F G (thirds), E T (two thirds), U I J K (corners), Enter (maximize), C (center), R (restore) | Ctrl+Alt+Win+Right = next display |
| Move pointer to next display | Ctrl+Alt+Win+Z | |
| Directional ring (window layout) | Ctrl+Alt+Shift+Space | hold + direction |
| Move / resize by dragging | hold Ctrl+Alt (add Shift to resize) | was Ctrl+Win in the first port: that chord collides with the Windows desktop switcher |
| Quick panel | Ctrl+Win+V | falls back to Ctrl+Alt+V when Windows or another app owns it (shown under the recorder) |
| Clipboard history / Paste as plain text | Ctrl+Alt+Win+V / Alt+Shift+Win+V | |
| Shelf, Quick snippet menu | Ctrl+Alt+Win+D, Ctrl+Alt+Win+L | |
| Rename shortcut (File Explorer) | F2 | bare key, hook based |
| Output switcher, Mute microphone, Keep awake | Ctrl+Alt+Win+S, +M, +K | |
| Display brightness down / up | Shift+Win+- / Shift+Win+= | only with the brightness control on |
| Scratchpad, Camera preview | Ctrl+Alt+Win+N, +W | |
| Quit all apps / all except the front one | Ctrl+Alt+Win+Q / +O | |
| Radial menu | Ctrl+Alt+Win+Space | |
| Screenshot, whole screen, recent, edit latest, edit clipboard, upload | Ctrl+Alt+Win+4, +3, +H, +E, +P, +U | |
| Screen recording, Copy text from screen, Color picker | Ctrl+Alt+Win+5, +T, +C | |
| Dynamic Island | Ctrl+Alt+Win+I | |

If Windows reserves a combination or another program owns it, Trayloom falls back (Win replaced by Shift; Ctrl+Win
replaced by Ctrl+Alt so it never becomes Ctrl+Shift+key), says so in the settings row, and the rest of the feature keeps
working. Ctrl+Alt+Arrow is also used by some Intel graphics drivers for screen rotation.

## Differences from Vorssaint on Windows

* "Menu bar" is the notification area: the tray icon opens the panel, right-click gives the menu, live metrics are drawn
  as a strip next to the tray. The icon can step aside while metrics are shown (`menuBarHideIconWithMetrics`), unless
  keep awake is active or the microphone is muted. Keep awake changes the icon (Trayloom mark, coffee, eye, moon, bulb),
  tint and can print the remaining minutes in the icon.
* The Dock features become taskbar features (thumbnails, minimize / hide / cycle on click); Spaces and the green
  maximize button do not exist. Windows Snap stays available next to Window layout.
* Permissions are Windows ones: *Accessibility* (global hooks, window management; limited when not elevated),
  *Screen recording*, microphone, camera, notifications. Administrator rights are only needed for the system-wide
  Cleaner paths and for controlling elevated windows.
* Trash becomes the Recycle Bin, Finder becomes File Explorer (no restart needed), Homebrew becomes winget, Spotlight
  settings become Windows Settings pages.
* Nothing talks to Vorssaint servers: share links are hidden, feedback opens a GitHub issue, the agent price list is
  bundled, lyrics lookup is opt-in.
* Per-app output routing uses an undocumented Windows interface. The first use on each Windows build runs a self-test (Trayloom
  routes its own silent stream to the default output and reads it back); if that does not round-trip, the route picker is hidden for
  that build and the reason is logged. Extra brightness is unavailable.

## Build, run, publish

Requires the .NET 9 SDK on Windows 10 (19041) or newer.

```
dotnet build Trayloom.sln -c Debug
dotnet run --project src/Trayloom.App

dotnet publish src/Trayloom.App/Trayloom.App.csproj -c Release -r win-x64 --self-contained true -o dist/Trayloom
```

The publish output (`dist\Trayloom\Trayloom.exe`, about 175 MB unpacked, 70 MB zipped) is a self-contained folder: feature
discovery loads the `Trayloom.Features.*.dll` assemblies next to the exe, and NAudio, ZXing.Net and the SQLite natives
ship beside them, so keep the folder together. The first start after a fresh copy can take ~10 s (Windows scans the new
DLLs); later starts take about a second.

Developer flags (a second launch hands them to the running copy): `--open-settings <page#anchor>`,
`--open-panel [tab]`, `--open-menu`, `--search <text>`, `--action <feature> <action>`, `--dev-install <id>`,
`--dev-uninstall <id>`, `--close-panel`, `--dump-info <file>` (features, actions, panel utilities, shortcut defaults,
duplicate checks), `--open-onboarding [step]`, `--open-install`, `--show-toast`, `--simulate-problem sac|crash`,
`--dev-key escape` (raises a key event inside the app), `--slow-start <ms>`, `--shot <png> [window title]` (saves a
picture of one of Trayloom's own windows), `--install`, `--uninstall [--quiet]`, `--no-onboarding`. With `TRAYLOOM_DEV=1` the island also exposes one action per page. Island placement: `--action island move` enters "Move island" mode and `--action island reset-position` puts the island back at the top center; with `TRAYLOOM_DEV=1` also `--action island dev-place-top-left`, `dev-place-top-right`, `dev-place-bottom-left`, `dev-place-bottom-center`, `dev-place-bottom-right`, `dev-place-float`, `dev-place-next-display` (cycles "Choose a display" through the connected displays) and `dev-place-reset`.

`ARCHITECTURE_V2.md` and `specs/CONTRACTS.md` describe the solution layout and the contracts feature projects code
against; `specs/R3_NOTES.md` is the integration checklist this release worked through.

## Smart App Control and unsigned builds

Trayloom builds are **not code-signed**. On Windows 11 with **Smart App Control** (SAC) in *Enforce* mode, unsigned and
unknown binaries (the exe and the feature DLLs it loads) can be blocked, which shows up as a failed start, a "blocked"
message or a feature assembly that cannot be loaded. Your options, your choice:

1. Sign the executables and DLLs with a code-signing certificate that Windows trusts.
2. Turn Smart App Control off (Windows Security, App and browser control). Windows does not allow turning it back on
   without resetting the PC.
3. Run on a machine without SAC (or in Evaluation mode), or build locally on that machine.

Trayloom never tries to bypass SAC or SmartScreen.

## Known limitations

* Not yet done (tracked in `specs/R3_NOTES.md`): the shared app picker / app list lives in three projects (Input,
  Windows, Files) instead of Core.
* The screenshot editor and recorder editor windows are hidden from screen capture on purpose, so screenshot tools show them blank.
* Exporting a recording renders every frame on the CPU: a 15 second clip takes about 15 s and briefly uses 1 to 2 GB.
* `TESTING.md` lists what needs a real person to verify (Alt+Tab takeover, taskbar previews, edge snapping, real clicks in
  recordings, the permission alert, and so on).
* Features that hook input (smooth scrolling, focus follows mouse, super key, snippets, protection, the switcher) cannot
  see or change input in elevated windows unless Trayloom runs elevated.
* GPU monitor needs the Windows GPU engine counters; external brightness needs DDC/CI; Packages and App updates need
  `winget`.
* With a very large AI-agent history (several GB of Claude Code logs) the Agents extension alone keeps ~200 MB resident;
  turn it off in the Dynamic Island settings if that matters. Idle cost with everything installed is about 3 to 4 % of one
  core (0.1 % of a 28-thread CPU) and 0.6 to 0.9 GB of memory, most of it the AI-agent history.

## License

GPL-3.0-or-later, see `LICENSE`. Vorssaint is a trademark of its owners; Trayloom only follows its public source code
under the GPL and has no relationship with the project.
