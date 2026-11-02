# WinKnife

WinKnife is a Windows 10/11 tray utility (.NET 9, WPF) that bundles many small system tweaks and tools into one
"install what you need" app. It is a Windows replica of the macOS utility [Vorssaint](https://vorssaint.com): the same
tray panel, Settings window, Features hub, Dynamic Island, Command Bar and feature catalog (73 installable entries),
rebuilt for Windows. Features are installed or uninstalled one by one from the **Features hub**; whatever is
uninstalled disappears from the whole app and loads nothing.

WinKnife is licensed under the same terms as Vorssaint, **GPL-3.0-or-later** (see `LICENSE`). It is an independent
project and is **not affiliated with or endorsed by Vorssaint**; the Vorssaint name, logo and icon are not used.

* Settings: `%APPDATA%\WinKnife\settings.json` (override the folder with the `WINKNIFE_DATA` environment variable).
* Log: `<settings folder>\logs\winknife.log`.
* First run: onboarding, then only the **Essentials** bundle (Volume mixer, System monitor, Keep awake) is installed.
  Everything else is one click away in the Features hub (Install all, bundles, or one by one).
* Every feature undoes its side effects when uninstalled or when WinKnife quits (mouse acceleration, hooks, hotkeys,
  wake locks, windows). Uninstalling *Mute microphone* while muted unmutes the microphone.
* `WinKnife.exe --quit` asks the running instance to shut down gracefully; a second launch (the Start menu shortcut) brings
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

## Install and first start

WinKnife is a tray app: after it starts you will find its icon **next to the clock** (Windows often hides new tray icons
behind the **^** arrow). There is no installer program; the installer is `WinKnife.exe` itself.

1. Unzip `WinKnife-win-x64.zip` anywhere (or use `dist\WinKnife`) and double-click `WinKnife.exe`
   (or `WinKnife-Setup.cmd`, which starts the same thing).
2. WinKnife offers **Install WinKnife** (or **Update WinKnife** when an older copy is installed):
   * **Install** copies the app to `%LOCALAPPDATA%\Programs\WinKnife` for your account only (no administrator password),
     adds a Start menu shortcut, an optional Desktop shortcut and an optional "start when I sign in" entry, and registers
     an entry in **Settings > Apps > Installed apps** whose Uninstall runs `WinKnife.exe --uninstall`. It then starts the
     installed copy and you can delete the download.
   * **Just run it** runs from where it is (tick "Don't ask again" to stop the offer). **Install WinKnife...** stays in the
     tray menu and in Settings > General > Getting started.
3. On a fresh profile a short introduction opens: what WinKnife is, a bundle (**Essentials** is preselected, or
   Windows / Battery and quiet, or pick features one by one), Launch at login, and a picture of where the tray icon is.
   A toast says "WinKnife is running" once.
4. Windows does not let apps pin their own icon. To keep it visible: Settings > Personalization > Taskbar > **Other system
   tray icons** > switch WinKnife on (the introduction and Settings > General have a button that opens that page,
   `ms-settings:taskbar`), or drag the icon out of the **^** overflow next to the clock.
5. Start it again from the Start menu while it runs: Settings opens on the page you used last, so you can always get back in.
6. Uninstall: Settings > Apps > Installed apps > WinKnife > Uninstall (or `WinKnife.exe --uninstall`). It quits WinKnife,
   removes the files, shortcuts, the Apps & features entry and the start-at-sign-in entry, and asks whether to delete your
   settings too.

If something does not start, right-click the tray icon > **Report a problem...** opens the folder with the log. A start
that takes longer than 2 s shows a "Starting WinKnife..." toast (the first start after copying new DLLs can take ~10 s).
If Windows **Smart App Control** blocks a file, WinKnife shows one dialog that explains it in plain words and lists the
options (below); it only reads that setting, it never changes it.

`scripts\package.ps1` publishes the self-contained build, adds `WinKnife-Setup.cmd` and writes `dist\WinKnife-win-x64.zip`.
(A self-extracting `Setup.exe` built with Windows IExpress was tried and is silently blocked on a Smart App Control PC, so
`WinKnife-Setup.cmd` is the Setup equivalent.)

Testing the install flow without touching your real Start menu or registry: set `WINKNIFE_INSTALL_ROOT`,
`WINKNIFE_STARTMENU_DIR`, `WINKNIFE_DESKTOP_DIR`, `WINKNIFE_UNINSTALL_KEY` (an HKCU sub key) and `WINKNIFE_RUN_KEY`
(an HKCU sub key) to scratch locations, plus `WINKNIFE_DATA` for the settings. `WINKNIFE_NO_INSTALL_PROMPT=1` hides the
install offer; builds run from `bin\Debug` / `bin\Release` never show it.

## Hotkeys

macOS ⌘ becomes **Win** in multi-modifier global shortcuts (⌃⌥⌘ = Ctrl+Alt+Win, ⌃⌘ = Ctrl+Win); ⌃ is Ctrl, ⌥ is Alt.
Shortcuts that act inside the front app use Windows conventions. All of them are editable in *Keyboard shortcuts*;
every default is unique (checked by `WinKnife.exe --dump-info <file>`). A shortcut only registers while its feature is
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

If Windows reserves a combination or another program owns it, WinKnife falls back (Win replaced by Shift; Ctrl+Win
replaced by Ctrl+Alt so it never becomes Ctrl+Shift+key), says so in the settings row, and the rest of the feature keeps
working. Ctrl+Alt+Arrow is also used by some Intel graphics drivers for screen rotation.

## Differences from Vorssaint on Windows

* "Menu bar" is the notification area: the tray icon opens the panel, right-click gives the menu, live metrics are drawn
  as a strip next to the tray. The icon can step aside while metrics are shown (`menuBarHideIconWithMetrics`), unless
  keep awake is active or the microphone is muted. Keep awake changes the icon (WinKnife mark, coffee, eye, moon, bulb),
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
* Per-app output routing and extra brightness are unavailable on the current Windows builds.

## Build, run, publish

Requires the .NET 9 SDK on Windows 10 (19041) or newer.

```
dotnet build WinKnife.sln -c Debug
dotnet run --project src/WinKnife.App

dotnet publish src/WinKnife.App/WinKnife.App.csproj -c Release -r win-x64 --self-contained true -o dist/WinKnife
```

The publish output (`dist\WinKnife\WinKnife.exe`, about 175 MB unpacked, 70 MB zipped) is a self-contained folder: feature
discovery loads the `WinKnife.Features.*.dll` assemblies next to the exe, and NAudio, ZXing.Net and the SQLite natives
ship beside them, so keep the folder together. The first start after a fresh copy can take ~10 s (Windows scans the new
DLLs); later starts take about a second.

Developer flags (a second launch hands them to the running copy): `--open-settings <page#anchor>`,
`--open-panel [tab]`, `--open-menu`, `--search <text>`, `--action <feature> <action>`, `--dev-install <id>`,
`--dev-uninstall <id>`, `--close-panel`, `--dump-info <file>` (features, actions, panel utilities, shortcut defaults,
duplicate checks), `--open-onboarding [step]`, `--open-install`, `--show-toast`, `--simulate-problem sac|crash`,
`--dev-key escape` (raises a key event inside the app), `--slow-start <ms>`, `--shot <png> [window title]` (saves a
picture of one of WinKnife's own windows), `--install`, `--uninstall [--quiet]`, `--no-onboarding`. With `WINKNIFE_DEV=1` the island also exposes one action per page.

`ARCHITECTURE_V2.md` and `specs/CONTRACTS.md` describe the solution layout and the contracts feature projects code
against; `specs/R3_NOTES.md` is the integration checklist this release worked through.

## Smart App Control and unsigned builds

WinKnife builds are **not code-signed**. On Windows 11 with **Smart App Control** (SAC) in *Enforce* mode, unsigned and
unknown binaries (the exe and the feature DLLs it loads) can be blocked, which shows up as a failed start, a "blocked"
message or a feature assembly that cannot be loaded. Your options, your choice:

1. Sign the executables and DLLs with a code-signing certificate that Windows trusts.
2. Turn Smart App Control off (Windows Security, App and browser control). Windows does not allow turning it back on
   without resetting the PC.
3. Run on a machine without SAC (or in Evaluation mode), or build locally on that machine.

WinKnife never tries to bypass SAC or SmartScreen.

## Known limitations

* Not yet done (tracked in `specs/R3_NOTES.md`): the shared app picker / app list lives in three projects (Input,
  Windows, Files) instead of Core; recorder extras (pointer track, click ring, auto-zoom, text lanes, audio time-stretch,
  per-window loopback, capture border); an elevated "run cleaner as administrator" relaunch helper.
* Features that hook input (smooth scrolling, focus follows mouse, super key, snippets, protection, the switcher) cannot
  see or change input in elevated windows unless WinKnife runs elevated.
* GPU monitor needs the Windows GPU engine counters; external brightness needs DDC/CI; Packages and App updates need
  `winget`.
* With a very large AI-agent history (several GB of Claude Code logs) the Agents extension alone keeps ~200 MB resident;
  turn it off in the Dynamic Island settings if that matters. Idle cost with everything installed is below 3 % of one
  core.

## License

GPL-3.0-or-later, see `LICENSE`. Vorssaint is a trademark of its owners; WinKnife only follows its public source code
under the GPL and has no relationship with the project.
