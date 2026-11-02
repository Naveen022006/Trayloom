# WinKnife

WinKnife is a Windows 10/11 tray utility (.NET 9, WPF) that bundles many small system tweaks and tools into one
"install what you need" app. Features are grouped, installed or uninstalled individually from the **Features hub**,
and the actions of every installed feature show up in the tray **Quick panel** and in the **Command bar**.

WinKnife is a Windows port of the macOS utility [Vorssaint](https://vorssaint.com) and is licensed under the same
terms, **GPL-3.0-or-later** (see `LICENSE`). WinKnife is an independent project and is **not affiliated with or
endorsed by Vorssaint**; the Vorssaint name, logo and icon are not used.

* Settings: `%APPDATA%\WinKnife\settings.json` (override the folder with the `WINKNIFE_DATA` environment variable).
* Log: `%APPDATA%\WinKnife\logs\winknife.log`.
* On first run only the Command bar is installed; everything else is switched on in the Features hub.
* Every feature undoes its side effects when uninstalled or when WinKnife quits (mouse acceleration, hooks,
  hotkeys, wake locks, windows...).
* `WinKnife.exe --quit` asks the running instance to shut down gracefully.

## Features

**Windows and taskbar**
App switcher (Alt+Tab style with live previews and search), Window layout (halves, thirds, quarters, maximize,
center, move between monitors).

**Clipboard and files**
Clipboard history (text and images, searchable popup), Paste as plain text, Clean URL (strips `utm_*`, `fbclid`, ...),
Shelf (floating drop target for files).

**Mouse and keyboard**
Invert mouse scrolling, Scroll sideways with a key, Smooth scrolling, Linear scrolling, Disable mouse acceleration,
Focus follows mouse, Side buttons, Mouse button shortcuts, Extra click filter, Keyboard debounce, Text snippets,
Super key, Close protection (Alt+F4 / Ctrl+W / Ctrl+Q need a hold or double press).

**Sound**
Volume mixer (per-app volume), Sound output switcher, Audio device priority, Mute microphone.

**Energy and display**
Keep awake, Displays (brightness for built-in and DDC/CI external monitors).

**Tools**
Quick toggles (dark mode, desktop icons, hidden files and extensions, Recycle Bin, lock), Color picker, Copy text from
screen (OCR), Screenshot (with annotation), Camera preview, Cleaning mode, Media compress, Cleaner, Uninstaller,
App updates (winget), Radial menu, Scratchpad, Command bar, Kill process, Port manager, Wallpaper.

**Dynamic Island**
Dynamic Island (now playing pill) plus its extensions Island timer, Island calendar, Island downloads and Island
gestures. The four extensions only show up when the **Dynamic Island** feature itself is installed; the Features hub
says so on their pages.

**System monitor**
CPU, GPU, Memory, Network, Disk, Battery and power, Connected devices, all shown in one shared flyout.

## Default hotkeys

Every default is unique. Most features have no hotkey until you set one in their settings page (cleaner, uninstaller,
kill process, port manager, wallpaper, media compress, app updates, cleaning mode, display brightness, extra
screenshot shortcuts and so on). All hotkeys can be changed or cleared in the Features hub.

| Feature | Default | Action |
| --- | --- | --- |
| Paste as plain text | Ctrl+Alt+V | Paste clipboard without formatting |
| Clipboard history | Ctrl+Alt+H | Open the history popup |
| Shelf | Ctrl+Alt+Shift+F | Show or hide the shelf |
| App switcher | Alt+` | Open the switcher (press again to move on) |
| Mute microphone | Ctrl+Alt+M | Toggle microphone mute |
| Volume mixer | Ctrl+Alt+A | Open the mixer |
| Sound output switcher | Ctrl+Alt+O | Cycle the output device |
| CPU monitor (opens the shared monitor flyout) | Ctrl+Alt+P | Show the system monitor |
| Screenshot, region or window | Ctrl+Alt+S | Capture a region or click a window |
| Screenshot, all screens | Ctrl+Alt+Shift+S | Capture every screen |
| Copy text from screen | Ctrl+Alt+Shift+T | Draw a box, copy the recognised text |
| Color picker | Ctrl+Alt+Shift+C | Pick a colour from the screen |
| Camera preview | Ctrl+Alt+Shift+K | Show or hide the mirror |
| Command bar | Ctrl+Alt+R | Open the command bar |
| Radial menu | Ctrl+Alt+Q (hold) | Hold to show the ring, release to run |
| Scratchpad | Ctrl+Alt+N | Show or hide all notes |
| Window layout: left / right / top / bottom half | Ctrl+Alt+Left / Right / Up / Down | Snap the active window |
| Window layout: quarters (top left, top right, bottom left, bottom right) | Ctrl+Alt+U / I / J / K | Snap to a corner |
| Window layout: left third / center third / right third | Ctrl+Alt+D / F / G | Snap to a third |
| Window layout: left two thirds / right two thirds | Ctrl+Alt+E / T | Snap to two thirds |
| Window layout: maximize / center / restore | Ctrl+Alt+Enter / C / Backspace | Maximize, center, undo |
| Window layout: next / previous monitor | Ctrl+Alt+Shift+Right / Left | Move to another display |

Notes on the choices:

* Ctrl+Alt+Letter is shared by many features and by Window layout (U I J K D F G E T C). Where a tool wanted one of those
  letters, it uses the same letter with Shift added (Shelf F, Camera K, Color picker C, Copy text T) so the window
  layout keeps its mnemonic block.
* Ctrl+Alt+Space (the usual launcher key) is already taken by other apps on many PCs, so the Command bar uses Ctrl+Alt+R.
* Clipboard history avoids Ctrl+Shift+V, which browsers and terminals use for "paste as plain text".
* Ctrl+Alt+Arrow combinations are also used by some Intel graphics drivers for screen rotation; turn that off in the
  graphics control panel or rebind Window layout if the shortcuts do not react.
* Hotkeys that are already owned by another program are reported in the log and as a tray notification; the rest of
  the feature still works.

## Build, run, publish

Requires the .NET 9 SDK on Windows 10 (19041) or newer.

```
dotnet build WinKnife.sln -c Debug
dotnet run --project src/WinKnife.App

dotnet publish src/WinKnife.App/WinKnife.App.csproj -c Release -r win-x64 --self-contained true -o dist/WinKnife
```

The publish output (`dist\WinKnife\WinKnife.exe`) is a self-contained folder; feature discovery loads the
`WinKnife.Features.*.dll` assemblies from the folder of the exe, so keep the folder together (NAudio ships next to
them). `-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true` also works and still discovers every
feature, but the result is not smaller and it unpacks native libraries to a temporary folder at start-up, which makes
Smart App Control more likely to object, so the folder layout is the recommended distribution.

`ARCHITECTURE.md` describes the solution layout and the contracts the feature projects code against.

## Smart App Control and unsigned builds

WinKnife builds are **not code-signed**. On Windows 11 with **Smart App Control** (SAC) in *Enforce* mode, unsigned
and unknown binaries (the exe and the feature DLLs it loads) can be blocked, which shows up as a failed start, a
"blocked" message, or a feature assembly that cannot be loaded. Your options, your choice:

1. Sign the executables and DLLs with a code-signing certificate that Windows trusts (SAC allows signed code from
   trusted publishers).
2. Turn Smart App Control off (Windows Security, App and browser control, Smart App Control settings). Note that
   Windows does not allow turning it back on without resetting the PC.
3. Run on a machine without SAC (or in Evaluation mode), or build locally on that machine, which has worked in the
   tests done so far.

WinKnife never tries to bypass SAC or SmartScreen.

## Known limitations and skipped Mac-only features

* Skipped because Windows already covers them or they do not exist on Windows: trackpad middle click, rename shortcut,
  cut and paste of files, quit-on-close for apps, taskbar preview tweaks, disk image installer, fan control,
  extra brightness, music blocker, Bluetooth on sleep, Island notifications.
* Window layout has no drag-to-edge snapping; Windows Snap (drag to an edge, Win+Arrow, Snap layouts) already does it.
* Side buttons already work as back/forward in most apps; the feature exists to remap them.
* Not implemented in this release: screen recording, lyrics, AI agents, watch and companion integrations.
* Features that hook input (smooth scrolling, focus follows mouse, super key, text snippets, close protection) work
  through global low-level hooks; they cannot see or change input in elevated (administrator) windows unless WinKnife
  itself runs elevated.
* Smooth scrolling is limited by what the target app accepts (apps ignoring small wheel deltas scroll in jumps).
* GPU monitor depends on the Windows GPU engine performance counters; some drivers do not provide them.
* External monitor brightness needs DDC/CI support and enabled in the monitor menu.
* App updates need `winget`; Media compress handles video and GIF only when `ffmpeg` is on the PATH.
* The self-contained publish is large (about 177 MB unpacked, 70 MB zipped) because it carries the .NET and WPF runtime.
* Unsigned builds, see the Smart App Control section above.

## License

GPL-3.0-or-later, see `LICENSE`. Vorssaint is a trademark of its owners; WinKnife only follows its public source code
under the GPL and has no relationship with the project.
