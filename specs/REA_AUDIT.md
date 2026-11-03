# REA parity audit: Trayloom vs Vorssaint 3.4.3

Auditor: REA (`rea-agents` 6.7.0) plus static scripts. Date of run: 2026-11-03. No Trayloom source was edited, Trayloom and Vorssaint binaries were never executed.

Subject A: the official Vorssaint release `Vorssaint-3.4.3.dmg` (sha256 `d1d59435f949539968adcbc9aa2545a6b90610b753bf843d8113c85615a20396`, 27.8 MB, from https://github.com/vorssaint/vorssaint-utils/releases/latest = v3.4.3).
Subject B: `E:\REA TEST 1\trayloom\dist\Trayloom\` (11 first-party assemblies: `Trayloom.dll`, `Trayloom.Core.dll`, 9 x `Trayloom.Features.*.dll`).
Reference source: `E:\REA TEST 1\vorssaint-mac` (Swift, 702 files).

All raw evidence lives under `C:\Users\ADMIN\AppData\Local\Temp\claude\E--REA-TEST-1\7b91bf88-d61e-42ce-a3bd-2a6a3cdaf08c\scratchpad\rea\` (call it `$REA`): see section 9.

---

## 0. Headline numbers

| Measure | Result |
|---|---|
| Feature catalog (Vorssaint `AppFeature`, read from the Mach-O) | **79 / 79** present as Trayloom catalog ids; 65 supported, 8 adapted, **6 marked unavailable** (all with a Windows reason) |
| Settings keys (Vorssaint `DefaultsKey`, 868 in binary and source) found in Trayloom | **748 / 868 = 86.2%** (644 as IL string literals, 58 composed at runtime, 41 `windowLayoutShortcut.<id>` renames, 5 shortcut-role renames) |
| ... of the 120 remaining | 64 not possible on Windows or dev/migration markers, 9 stored under another key or file, 27 real gap (WhatsApp organizer), 20 partial or needing live verification |
| Coverage of *applicable* keys (excluding the 64 N/A) | **94.2%** |
| Registered defaults (775 in Vorssaint) | 657 have a Trayloom key; **494** could be compared mechanically: **490 identical (99.2%)**, 4 differ (all intentional, section 3) |
| Global shortcut roles | **32 / 34** (missing: the two keyboard-backlight roles, no Windows equivalent). Window-layout actions **41 / 41** |
| Settings pages | **35 / 35** reachable (3 renamed, 1 folded into an anchor) |
| English UI strings (2,656 localized English literals extracted from the Swift `enUS` blocks; 1,476 of the 1,478 literals over 15 bytes were independently confirmed in the Mach-O `__cstring`) | **67.6%** exact match in Trayloom literals, **72.4%** after Mac->Windows vocabulary mapping, **77.6%** with fuzzy match. This is a *lower bound*: printf-style strings (`Quit %@?`) are built by interpolation in C# and cannot match as literals; 197 of the 596 misses name Mac-only things |
| Translations | **1 / 15 languages.** Trayloom has a language picker with 14 extra names but zero translated UI text (only 8 non-Latin literals exist: the picker labels). Vorssaint embeds 13,217 CJK/Cyrillic strings |
| Network | Trayloom has **no Vorssaint endpoint** (no `vorssaint.com`, `screenshots.vorssaint.com`, no vorssaint GitHub URL in any compiled string) |
| Release vs cloned source | Source == 3.4.3 release for features, keys and strings. Only drift: `agent-prices.json` (source `updated 2026-10-30`, release `2026-10-09`; Trayloom bundles 2026-10-30). No newer-than-source feature found |

---

## 1. REA setup and what REA could / could not analyze

Install: `npm install --global rea-agents --prefix <scratch>\npm\prefix` (official npm package, no global change; Node 24.19, npm 11.17). `rea --version` = **6.7.0**. `rea setup` was **not** run (it edits agent configs). npm warned that `frida`'s install script is not allowed; not needed.

`rea doctor` (saved `$REA\out\doctor.json`): `healthy:false`, only because there is no analysis engine and no agent registration. Node and host checks pass. Missing: Hopper, Ghidra (`GHIDRA_INSTALL_DIR` unset; Ghidra 12.1 also needs JDK 21 and this machine has JDK 17), IDA, JADX, Kuna. No Ghidra/Hopper was installed (large toolchains, not installed without approval).

| REA command | Works without a native provider on this Windows host? | Evidence |
|---|---|---|
| `inspect-managed-artifact`, `inspect-managed-members`, `inspect-managed-native-boundaries`, `inspect-pe-resources` | **Yes, fully.** Types, methods, fields, call edges, member refs, `ldstr` literals (state `complete`; the "partial" flag is only the benign UTF-16 terminal-flag note) and P/Invoke tables for all 11 Trayloom assemblies | `out\w\*.artifact.json`, `*.members.json`, `*.nativeboundaries.json`, `Trayloom.exe.peresources.json` |
| `inspect-artifact` (dmg, .app directory, Mach-O, dylib, helper) | Yes, but shallow: identity, sha256, format (`dmg`, `directory`, `mach-o`), container graph. No symbols/strings; the only next probe it suggests is `open_binary` (needs a provider) | `out\inspect-artifact-*.json` |
| `extract-artifact` on a `.dmg` | **No**: "Artifact format has no extraction reader: dmg". I extracted the HFS+ volume with my own read-only UDIF/HFS+ reader (`tools\udif.py`, `tools\hfsx.py`), 44 files, nothing executed |
| `inspect-binary-layout` | **No**: ELF-only (pwntools profile, Linux x64) | `out\layout.json` |
| `inspect-macho`, `inspect-plist`, `demangle-swift`, `inspect-signature`, `list-architectures` | **No**: provider `native-macos` "requires macOS" | `out\infoplist.json` |
| `analyze`, `search`, `decompile`, `function`, `trace`, `inspect-native-*` | **No**: need Ghidra/Hopper/IDA/Kuna | error in transcript (`provider_unavailable`, candidates ghidra/hopper/ida/kuna) |
| `doctor` | Yes | `out\doctor.json` |

Consequence: for the Mach-O the REA-native path stops at identity. The Vorssaint binary inventory below therefore uses REA for identity/hash/container evidence and a read-only parser (`tools\macho_dump.py`, Python `-I`) for load commands, the **unstripped symbol table (209,383 symbols)**, `__cstring`, `__swift5_reflstr` (enum case names) and the embedded signature blob. Swift mangled names were decoded structurally (module/type/identifier, including Swift word-substitution). The only things REA could not give for the Mach-O were demangled signatures and decompilation. For the .NET side REA did everything needed.

---

## 2. Vorssaint 3.4.3 inventory (from the binary)

Bundle (`$REA\extracted\manifest.txt`): `Contents/MacOS/Vorssaint` (80.85 MB, **arm64 only**, min macOS 14.0, 89 load commands, 59 linked dylibs/frameworks), `Frameworks/libVorssaintNowPlaying.dylib` (MediaRemote adapter, loaded through perl by `Resources/now-playing.pl`), `Library/LaunchServices/com.vorssaint.utils.fan-control` (privileged SMC helper, plus `LaunchDaemons/com.vorssaint.utils.fan-control.plist`: Mach service, KeepAlive on crash, 5 s throttle), `Resources`: `CHANGELOG.md`, `agent-prices.json`, gifs and images, 14 `*.lproj/InfoPlist.strings`. **No Localizable.strings**: all UI text is compiled into the Swift binary (15 languages).

Info.plist: bundle id `com.vorssaint.utils`, v3.4.3 (build 105), `LSUIElement` (menu-bar agent), 14 locales declared, usage descriptions for Apple Events, audio capture, Bluetooth, Calendars, Camera, Desktop/Documents/Downloads/Network/Removable volumes, Microphone. Embedded entitlements: Apple Events, camera, audio-input, calendars (hardened runtime, not sandboxed).

Binary-derived inventories:

* `AppFeature` cases: **79** (identical order and names in `FeatureCatalog.swift`), 8 hub groups, 12 `AppPermission`s.
* `DefaultsKey` constants: **868** (accessors in the symbol table), identical to the source set (849 by direct name decode, 19 more once Swift word-substitution is decoded, e.g. `DefaultsKeyO05superC7Enabled` = `superKeyEnabled`).
* Registered defaults: 775 (source `Defaults.registeredDefaults`); 702 resolved to literals, 73 symbolic (enum raw values, computed shortcuts).
* Swift types in symbols: 1,538 (source declares 2,371 incl. nested, generic and protocol-only types; only 2 binary-only names: `MenuBarUsage`, `MiddleClick`, both nested classes, not features).
* Global shortcut roles: **34** (`GlobalShortcutRole`); window layout actions: 41; Settings pages: 35.
* Localized UI: 15 languages; ~32k `__cstring` entries.
* Hotkeys, helpers: fan-control privileged helper + now-playing adapter (above).

### Endpoints contacted by Vorssaint (string evidence in `__cstring`)

| Endpoint | Purpose | Trayloom |
|---|---|---|
| `github.com/vorssaint/vorssaint-utils/releases/download/v...`, `api.github.com/repos/`, Sparkle appcast namespace | self-update | not present (no self-updater, see gap G7) |
| `raw.githubusercontent.com/vorssaint/vorssaint-utils/main/Resources/agent-prices.json` | agent price list refresh | **absent on purpose**: `AgentPriceList.Remote = ""`, bundled list only |
| `screenshots.vorssaint.com`, `/v1/feedback` | screenshot/recording share links and in-app feedback | **absent on purpose** (sharing hidden, feedback opens a GitHub issue) |
| `lrclib.net/api/get`, `/api/search` | synced lyrics | present, opt-in, off by default |
| `musicbrainz.org/ws/2/{artist,recording,release-group}`, `itunes.apple.com/{search,lookup}`, `uclient-api.itunes.apple.com/...`, `i.ytimg.com/vi/` | lyric/cover/album matching and Firefox YouTube thumbnails | **not present** (see gap G9) |
| `speed.cloudflare.com` | network speed test | present (same host) |
| `formulae.brew.sh/api/cask.json`, `/api/analytics/`, `raw.githubusercontent.com/Homebrew/install` | Homebrew catalog | N/A (winget used) |
| `claude.ai/download`, `code.claude.com/docs/...` | links in AI Agents | present |
| `discord.gg/...`, `x.com/vorssaint`, `buymeacoffee.com/vorssaint`, `vorssaint.com`, `github.com/vorssaint/vorssaint-utils` | about/support links | replaced by `github.com/Naveen022006/Trayloom` |

---

## 3. Trayloom inventory (REA .NET analysis)

Totals across the 11 assemblies (`$REA\out\w\summary.json`): **3,596 types, 23,091 methods, 11,955 `ldstr` literals (8,956 unique)**, 484 P/Invoke imports (user32 272, kernel32 86, gdi32 23, shell32 21, dwmapi 19, combase 13, dxva2 9, powrprof 7, pdh 6, shcore 5, setupapi 5, iphlpapi 3, msi 3, d3d11 2). Per assembly: Core 202 types / 1,053 literals; Agents 329 / 937; Capture 354 / 861; Files 275 / 668; Input 200 / 656; Island 406 / 1,285; Maintenance 388 / 1,542; System 419 / 1,240; Tools 611 / 2,392; Windows 255 / 599; App 157 / 722.

* Feature ids: all 79 kebab ids present as literals in `Trayloom.Core` (`FeatureCatalog` static constructor). 60 ids have an explicit `IFeature.Id`; the other 19 are the 6 unavailable features and 11 Island extensions and 2 scroll features that are hosted by a parent feature class. (The README says "73 installable entries"; the shipped catalog has **79**. README is stale.)
* Network in Trayloom (REA member refs + literal URLs): `HttpClient` is used only by `LyricsService` (lrclib.net), `CalendarService` (user-supplied ICS URL), `SpeedTest` (Cloudflare), `AppUpdatesService`/`FeedLoader` (GitHub `releases/latest/download/latest.yml` of apps the *user* has installed), `RadialFavicon` (host of a user-added web item), `AgentPriceList.Download` (dead code: `Remote` is empty). Literal URLs found: `claude.ai/download`, `code.claude.com/docs/en/authentication`, `github.com/Naveen022006/Trayloom`, `lrclib.net/api/get`, `speed.cloudflare.com`. **Nothing contacts Vorssaint servers.**
* Settings defaults: 699 registered defaults extracted from Trayloom source (REA IL confirms the key literals).
* Shortcut roles: see section 5. Settings pages: section 6.

### Default-value differences (4 of 494 comparable keys; all intentional)

| Key | Vorssaint | Trayloom | Verdict |
|---|---|---|---|
| `recorderSharingEnabled` | true | false | intentional (no share server) |
| `screenshotSharingEnabled` | true | false | intentional |
| `snippetSoundName` | `Tink` | empty | intentional (no Tink on Windows) |
| `switcherTakeOverSystemShortcuts` | false | true | intentional (Alt+Tab replacement is the point on Windows) |

---

## 4. Feature parity matrix (79 features)

"Present" means the catalog entry, the feature class and its settings keys exist. REA is static, so runtime behaviour of hook-based features is **not** verified (the project's own notes list live verification of Alt+Tab takeover, taskbar thumbnails, radial ring, selection overlays, recorder, camera and cleaning mode as still pending).

| # | Vorssaint AppFeature (binary + source) | Trayloom catalog id | Status in Trayloom | Notes |
|---|---|---|---|---|
| 1 | `switcher` | `switcher` | Present |  |
| 2 | `dockPreview` | `dock-preview` | Present (adapted) | Dock thumbnails -> taskbar hover previews (UI Automation); needs live check |
| 3 | `dockClick` | `dock-click` | Present (adapted) | Dock click -> taskbar click minimize/hide/cycle |
| 4 | `windowMaximizer` | `window-maximizer` | Not possible / not available | Not possible: Windows caption buttons already maximize |
| 5 | `windowLayout` | `window-layout` | Present |  |
| 6 | `autoQuit` | `auto-quit` | Present |  |
| 7 | `spacesOrder` | `spaces-order` | Not possible / not available | Not possible: Windows has no MRU Space reordering |
| 8 | `scrollInverter` | `scroll-inverter` | Present |  |
| 9 | `scrollHorizontal` | `scroll-horizontal` | Present |  |
| 10 | `scrollZoom` | `scroll-zoom` | Present (adapted) | Mac pinch gesture synthesis -> Ctrl+wheel zoom |
| 11 | `focusFollowsMouse` | `focus-follows-mouse` | Present |  |
| 12 | `smoothScroll` | `smooth-scroll` | Present |  |
| 13 | `linearScroll` | `linear-scroll` | Present |  |
| 14 | `mouseAcceleration` | `mouse-acceleration` | Present |  |
| 15 | `mouseNavigation` | `mouse-navigation` | Present |  |
| 16 | `mouseButtonShortcuts` | `mouse-button-shortcuts` | Present |  |
| 17 | `middleClick` | `middle-click` | Not possible / not available | Not possible: precision touchpad gestures are OS-owned |
| 18 | `mouseClickDebounce` | `mouse-click-debounce` | Present |  |
| 19 | `keyboardDebounce` | `keyboard-debounce` | Present |  |
| 20 | `textSnippets` | `text-snippets` | Present |  |
| 21 | `superKey` | `super-key` | Present |  |
| 22 | `quitWindowProtection` | `quit-window-protection` | Present |  |
| 23 | `clipboardHistory` | `clipboard-history` | Present |  |
| 24 | `pastePlain` | `paste-plain` | Present |  |
| 25 | `finderCutPaste` | `finder-cut-paste` | Present (adapted) | Finder -> File Explorer |
| 26 | `finderRename` | `finder-rename` | Present (adapted) | Finder Return-to-rename -> F2 |
| 27 | `shelf` | `shelf` | Present |  |
| 28 | `urlCleaner` | `url-cleaner` | Present |  |
| 29 | `diskImageInstaller` | `disk-image-installer` | Not possible / not available | Could be adapted (installer download cleanup); W says "Not available on Windows yet" |
| 30 | `mixer` | `mixer` | Present |  |
| 31 | `soundOutputSwitcher` | `sound-output-switcher` | Present |  |
| 32 | `audioPriority` | `audio-priority` | Present |  |
| 33 | `micMute` | `mic-mute` | Present |  |
| 34 | `musicBlock` | `music-block` | Present (adapted) | Media-key launch blocking adapted to SMTC/media keys |
| 35 | `keepAwake` | `keep-awake` | Present |  |
| 36 | `brightness` | `brightness` | Present |  |
| 37 | `extraBrightness` | `extra-brightness` | Not possible / not available | Not possible: no HDR brightness-boost API for apps |
| 38 | `bluetoothSleep` | `bluetooth-sleep` | Present |  |
| 39 | `quickLauncher` | `quick-launcher` | Present |  |
| 40 | `quickToggles` | `quick-toggles` | Present |  |
| 41 | `quitApps` | `quit-apps` | Present |  |
| 42 | `colorPicker` | `color-picker` | Present |  |
| 43 | `screenOCR` | `screen-ocr` | Present |  |
| 44 | `cleaningMode` | `cleaning-mode` | Present |  |
| 45 | `mediaTools` | `media-tools` | Present |  |
| 46 | `cleaner` | `cleaner` | Present |  |
| 47 | `uninstaller` | `uninstaller` | Present |  |
| 48 | `homebrew` | `homebrew` | Present (adapted) | Homebrew -> winget (Packages) |
| 49 | `appUpdates` | `app-updates` | Present |  |
| 50 | `screenshot` | `screenshot` | Present |  |
| 51 | `cameraPreview` | `camera-preview` | Present |  |
| 52 | `radialMenu` | `radial-menu` | Present |  |
| 53 | `scratchpad` | `scratchpad` | Present |  |
| 54 | `commandBar` | `command-bar` | Present |  |
| 55 | `screenRecorder` | `screen-recorder` | Present |  |
| 56 | `wallpaper` | `wallpaper` | Present |  |
| 57 | `killProcess` | `kill-process` | Present |  |
| 58 | `portManager` | `port-manager` | Present |  |
| 59 | `notch` | `notch` | Present |  |
| 60 | `notchCalendar` | `notch-calendar` | Present |  |
| 61 | `notchNotifications` | `notch-notifications` | Present (adapted) | macOS notification DB reader -> Windows notification listener |
| 62 | `notchGestures` | `notch-gestures` | Present |  |
| 63 | `notchTimer` | `notch-timer` | Present |  |
| 64 | `notchAccessories` | `notch-accessories` | Present |  |
| 65 | `notchLyrics` | `notch-lyrics` | Present |  |
| 66 | `notchQueue` | `notch-queue` | Present |  |
| 67 | `notchLiveEqualizer` | `notch-live-equalizer` | Present |  |
| 68 | `notchDownloads` | `notch-downloads` | Present |  |
| 69 | `notchAgents` | `notch-agents` | Present |  |
| 70 | `notchWatch` | `notch-watch` | Present |  |
| 71 | `notchMascot` | `notch-mascot` | Present |  |
| 72 | `monitorCPU` | `monitor-cpu` | Present |  |
| 73 | `monitorGPU` | `monitor-gpu` | Present |  |
| 74 | `monitorMemory` | `monitor-memory` | Present |  |
| 75 | `monitorNetwork` | `monitor-network` | Present |  |
| 76 | `monitorDisk` | `monitor-disk` | Present |  |
| 77 | `monitorPower` | `monitor-power` | Present |  |
| 78 | `connectedDevices` | `connected-devices` | Present |  |
| 79 | `fanControl` | `fan-control` | Not possible / not available | Not possible: no supported fan control; needs privileged SMC helper on Mac |


Group breakdown: Windows and taskbar 7 (2 adapted, 2 unavailable), Mouse and keyboard 15 (1 adapted, 1 unavailable), Clipboard and files 7 (2 adapted, 1 unavailable), Sound 5 (1 adapted), Energy and display 4 (1 unavailable), Tools 21 (1 adapted: Packages), Dynamic Island 13 (1 adapted), Monitor 8 (1 unavailable). Totals 65 supported, 8 adapted, 6 unavailable.

Unavailable features worth a second look: `disk-image-installer` (a Windows analogue, "delete an installer after it ran from Downloads", is feasible; W says "Not available on Windows yet"). The other five are genuinely impossible or meaningless on Windows.

---

## 5. Hotkeys

Vorssaint `GlobalShortcutRole` (34) against Trayloom `ShortcutRoles.CreateStatic` (+41 window layout roles):

| Result | Roles |
|---|---|
| Present (32) | keepAwake, shelf, switcher, switcherWindow, switcherAllWindows, clipboard, soundOutputSwitcher, pastePlain, finderRename (F2), colorPicker, screenOCR, micMute, quitAll, quitAllExceptFront, quickLauncher, screenshot, screenshotFullScreen, screenshotLastCapture, screenshotUpload, recentCaptures, screenshotClipboard, cameraPreview, radialMenu, scratchpad, snippetLibrary, commandBar, screenRecorder, displayBrightnessDecrease/Increase, pointerNextDisplay, notch (stored as `<roleId>Shortcut`; two renames: `recorderShortcut` -> `screenRecorderShortcut`, `pointerDisplayShortcut` -> `pointerNextDisplayShortcut`, `quitAppsShortcut` -> `quitAllShortcut`) |
| Not possible (2) | keyboardBrightnessDecrease, keyboardBrightnessIncrease (Mac keyboard backlight) |

Window layout: all 41 `WindowLayoutAction` cases exist (`windowLayoutShortcut.<id>`, ids `layout.<raw>` for the Command Bar). Modifier mapping Cmd -> Win with automatic fallback when Windows owns the chord.

## 6. Settings screens

Vorssaint `SettingsPage` (35) -> Trayloom `SettingsPages`: general, features, energy, monitor, mouse, switcher, dock(Taskbar), keyDebounce(debounce), superKey, cutPaste, autoQuit(quit-on-close), quitProtection, cleaner, uninstaller, urlCleaner(clean-url), homebrew(packages), appUpdates, media, clipboard, windowLayout, shelf, quickTools, textSnippets, screenshot(screen-capture), radialMenu, commandBar, killProcess, portManager, notch(island), shortcuts, advanced, about, releaseNotes(whats-new), support = all present; `notchMascot` is the `island#companion` anchor. Trayloom has no extra page.

Vorssaint surfaces with a Trayloom counterpart (by type/source inspection): menu panel (tray panel with Monitor/Mixer/Brightness/Utilities/Controls/Toggles/Wallpaper tabs), Settings window + search directory, onboarding, Features hub + presets (Essentials/Windows/Battery), Dynamic Island (Music, Calendar, Timer, Clipboard, Files/shelf, Downloads, Mixer, System, Scratchpad, Controls, Quick access, Camera mirror, Lyrics, Queue, Notifications, Agents, Watch, Companion, lock-screen alert), Command Bar, Quick launcher, Radial menu, Switcher overlay, Dock preview, Screenshot overlay/editor/pins, Recorder + editor, Color picker, OCR, Camera preview, Scratchpad, Cleaner, Uninstaller, Media tools, Homebrew -> winget, App updates, Kill Process, Port Manager, Keep Awake automation, Displays, Fan control section (not possible), Permission guide.

Vorssaint services (`Services/*`, 100+ files) with no Windows meaning: FanControl, SpacesOrder, DiskImageInstaller, MiddleClick, WindowMaximizer, SpotlightNames, SudoersSupport, SystemShortcutTakeover, WindowServer*, EnhancedUserInterfaceSuspension, DockTileResolver. All other service groups map to a Trayloom project (Input, Windows, Files, System, Capture, Maintenance, Tools, Island, Agents).

---

## 7. Settings-key coverage detail (`$REA\out\key_coverage.csv`, 868 rows)

| Bucket | Count | Meaning |
|---|---|---|
| Present as IL literal | 644 | same key, found in a `ldstr` of a Trayloom assembly |
| Present, composed at runtime | 58 | e.g. `"quitProtectionQuit" + "Mode"`; defaults confirmed in `InputSettings.cs` (Mode hold, 800 ms, 600 ms, shift, scope all) |
| Present, renamed | 46 | 41 `windowLayoutShortcut.<id>` + 5 role shortcuts |
| Stored under another key or file | 9 | clipboardHistoryEntries (own store), shelfItems (shelf.json), displaysSwitchedOff(+Fingerprints), micMute* saved state, notchDownloadsFolderBookmark (path), recorderSystemAudioTapVerified, restoredScreenCaptureShortcutsMigrated |
| Not possible on Windows | 41 | fan control 6, keyboard backlight 4, disk-image installer 3, Spaces/maximize/Stage Manager/middle-click/windowless Finder 9, display/brightness internals 8, shortcut take-over and key-remap internals 5, per-app audio routing 6 |
| Dev or migration markers | 23 | simulateBetaUI, simulateUpdate, screenshotSharingDeveloperEndpoint, *Migrated, whatsNew*, updateShowcase*, panelNavigationEnabled (legacy), screenshotOpenEditorDirectly (legacy migration), notchMusicActivity and notchHideInCaptures (unused in source) |
| **Gap: WhatsApp downloads** | 27 | whole feature missing (section 8, G2) |
| Partial / verify | 20 | menuBar per-metric toggles (8: Trayloom keeps visibility in `menuBarMetricOrder`), Island options `notchCameraFitWidth/Height`, `notchCoversMenus`, `notchHideMenuBarGap`, `notchQuickAccessSide/Second/Third` (7), panel toggles `panelUtility{ColorPicker,QuickLauncher,ScreenOCR,ScreenRecorder}`, `panelControlCutPaste` (5) |

Dynamic-key caveat: Trayloom builds many keys at runtime, so literal matching is a lower bound; the 58 composed keys were found by prefix+suffix matching and spot-checked against `InputSettings.cs`.

## 8. Prioritized gap list

### P1: user-visible features missing

* **G1. UI localization.** Vorssaint ships 15 languages (en-US, pt-BR, tr, ru, es, sk, de, fr, it, ja, ko, uk, zh-Hans, zh-TW, zh-HK; 13,217 non-Latin strings in the binary plus the Latin ones). Trayloom's General page lists all 15 names but the whole UI is English; the picker only stores `appLanguage`. Either translate (the Swift `Strings+*.swift` files are GPL and structured per feature) or hide the picker.
* **G2. WhatsApp downloads cleanup + organizer** (Cleaner page, Vorssaint 3.4.x): scheduled cleanup of confirmed WhatsApp downloads (categories image/video/audio, retention days, notify, exclusions, include existing), and an organizer that moves downloads to a chosen folder (delay, file-type categories, flat/by-type layout, duplicate action with exact-duplicate hashing, undo transaction). 27 settings keys, ~57 UI strings. Absent from Trayloom (only `PermissionService` mentions WhatsApp). Feasible on Windows (WhatsApp Desktop download folders).
* **G3. Screen recorder editor extras**: pointer track, click ring, auto-zoom and zoom lane, backdrop/blur, text lanes, presets, audio time-stretch, per-window loopback, capture border, cursor capture. Already listed in README "known limitations"; REA confirms 45 `RecorderStrings` literals unmatched. Core recording and trimming exist.

### P2: partial behaviour

* **G4. "Check for updates automatically" / "Receive beta updates"** toggles on About are dead: `autoCheckUpdates` / `includeBetaUpdates` are only read by the About page UI and the defaults table, and there is no self-update code or endpoint (Vorssaint uses a Sparkle appcast on GitHub releases). Implement a GitHub-releases check or remove the toggles.
* **G5. Per-app output routing and per-app volume persistence** (`appOutputDevices`, `appVolumes`, `mixerUniversalOutputDevice`, `mixerCallAppVolumeEnabled`): documented as unavailable on this Windows build; the mixer exists, per-app routing does not.
* **G6. Island options** missing: camera fit width/height, quick-access side/second/third, "show over menus", menu-bar gap. Layout-only; `notchQuickAccessLayout` exists.
* **G7. Disk image installer** has no Windows analogue yet (installer cleanup after running a downloaded setup).
* **G8. Menu-bar metric toggles**: Battery time/temperature, CPU/GPU temperature, connected devices, disk usage/activity, fan speed are separate toggles in Vorssaint; Trayloom keeps them inside `menuBarMetricOrder`. Verify in the UI that each metric can be shown/hidden individually.
* **G9. Music metadata sources**: Vorssaint also verifies title/artist/album and finds covers through MusicBrainz and iTunes, and reads Firefox sessions for YouTube thumbnails; Trayloom only queries lrclib.
* **G10. Eight "Adapted" features** (taskbar preview/clicks, Pinch Zoom, cut and paste, F2 rename, music-block, winget Packages, island notifications) are statically present; behaviour needs the pending live verification listed in `R3_NOTES.md`.
* **G11. Format-string UI text**: 399 non-Mac strings did not match as literals (many are interpolated). Spot checks show Command Bar rows ("Turn on %@", "Restart %@", "Force quit %@?", "Uninstall %@...", "Words: %d, characters: %d"), island hover/haptics options ("Expand fully on hover", "Haptic feedback"), permission-guide wording, and App Store rows. Re-check each in the running app.

### P3: cosmetic or minor

* **G12. CLI flags**: Vorssaint `--export-settings FILE` / `--import-settings FILE` (UI export/import exists in Trayloom Advanced; the flags do not).
* **G13. Agent price list is bundled only** (updated 2026-10-30, same as the source); it will go stale without the remote refresh (intentional privacy choice, `Remote = ""`).
* **G14. README stale**: says 73 catalog entries (actual 79).
* **G15. Not possible on Windows (informational)**: fan control, keyboard backlight, Spaces order, maximize button, three-finger middle click, extra HDR brightness, HDR/DDC internals, macOS shortcut take-over, Stage Manager, App Store updates.

### Verified OK (no action)

79/79 feature ids, 32/32 applicable shortcut roles, 41/41 layout actions, 35/35 settings pages, 99.2% identical defaults, no Vorssaint network endpoint, uninstall-time side-effect undo, catalog presets, Claude account selection, All-windows switcher shortcut, Quit apps, Pinch zoom, Island volume row (all 3.4.3 changelog items map to present keys).

---

## 9. REA commands run and evidence paths

`$REA` = `C:\Users\ADMIN\AppData\Local\Temp\claude\E--REA-TEST-1\7b91bf88-d61e-42ce-a3bd-2a6a3cdaf08c\scratchpad\rea`

| Command | Output |
|---|---|
| `npm install --global rea-agents --prefix $REA\npm\prefix`; `rea --version`; `rea --help` | `$REA\npm\prefix` (REA 6.7.0) |
| `rea doctor --json` | `$REA\out\doctor.json` |
| `rea inspect-artifact` on dmg, `.app`, `MacOS/Vorssaint`, helper, dylib | `$REA\out\inspect-artifact-{app,macho,helper,nowplaying}.json` |
| `rea extract-artifact` (dmg) | refused: no extraction reader |
| `rea inspect-binary-layout`, `inspect-plist`, `inspect-macho`, `demangle-swift`, `analyze` | refused (ELF only / macOS only / no provider): `out\layout.json`, `out\infoplist.json` |
| `rea inspect-managed-artifact` x 11 | `$REA\out\w\<assembly>.artifact.json` |
| `rea inspect-managed-members` x 11 (types, methods, call edges, `ldstr`) | `$REA\out\w\<assembly>.members.json`; literals: `out\w\literals_all.json`; summary `out\w\summary.json` |
| `rea inspect-managed-native-boundaries` x 11, `inspect-pe-resources Trayloom.exe` | `$REA\out\w\*.nativeboundaries.json`, `Trayloom.exe.peresources.json` |
| Mach-O dump (own parser, REA could not): sections, 209,383 symbols, cstrings, reflection strings, entitlements | `$REA\out\v\*` (`symbols.txt`, `cstring.txt`, `swift_reflstr.txt`, `inv_binary.json`, `appfeature_binary.txt`) |
| Comparisons | `$REA\out\key_coverage.csv`, `keys_buckets.json`, `keys_diff.txt`, `strcov.json`, `strcov_missing.txt`, `feature_map.json`; scripts in `$REA\tools\` |
| Extracted app bundle (untrusted, never executed) | `$REA\extracted\volume\Vorssaint.app`, `$REA\extracted\manifest.txt`; download in `$REA\vorssaint\Vorssaint-3.4.3.dmg` |

Not done: `Trayloom.exe --dump-info` (a second launch hands dev flags to the user's running copy through the single-instance mutex, so it was skipped in favour of static analysis); running any Vorssaint code; installing Ghidra/Hopper.

## 10. Method limits (read before trusting a percentage)

* Swift string literals up to 15 bytes are immediates in code, not in `__cstring`; English-string extraction therefore comes from the Swift source `enUS` blocks (validated against the binary: 1,476/1,478 long literals present), not from the binary alone. About 145 additional English strings in the binary (catalog descriptions) are not in the 2,656-string set.
* Key presence is by string literal / runtime composition, not by proof that each key is read; a present key can still be wired to a no-op, and an absent key can be served by a different storage name (bucketed above).
* Defaults comparison covers only keys whose default resolves to a literal on both sides (494 of 775).
* Trayloom settings that live in per-feature scoped stores are not visible as flat keys.
