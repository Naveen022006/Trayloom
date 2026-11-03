# 01 - Menu bar status item and the menu bar PANEL (Windows: tray icon(s) + tray flyout)

Source of truth: `E:\REA TEST 1\vorssaint-mac\Sources\Vorssaint\` (`App/StatusItemController.swift`, `App/MenuBarRenderer.swift`,
`App/MenuBarSpacingSupport.swift`, `UI/MenuPanel/*`, `UI/Theme.swift`, `UI/SharedUI.swift`, `Core/MonitorLayoutStrings.swift`,
`Core/MenuBarAppearanceStrings.swift`, `Core/Localization.swift` enUS block) and the readme images
(`docs/assets/readme/panel-*.png`, `network-section.png`, `power-section.png`, `utilities-section.png`, `volume-mixer.png`,
`system-monitor-graph.png`, `Resources/Images/menu-bar-temperature-metrics.png`).

Conventions used below:
- "pt" = DIP on Windows (1 pt -> 1 DIP at 96 dpi). All sizes are the Swift numbers verbatim.
- Strings are the English strings verbatim. Platform/brand adjustments (apply everywhere): `Vorssaint` -> `Trayloom`, `Mac` -> `PC`,
  `macOS` -> `Windows`, `Finder` -> `File Explorer`, `menu bar` -> `taskbar` / `notification area` where it names the surface,
  `System Settings` -> `Windows Settings`, `Trash` -> `Recycle Bin`. Everything else stays as written.
- Several readme PNGs are OLDER than the code (see section 0.1). Where image and Swift disagree, the Swift wins.
- Strings that are stated as "(tooltip)" are NOT printed on the row; they are the hover tooltip + screen-reader help only.

---------------------------------------------------------------------------------------------------------------------

## 0. Orientation

### 0.1 Image vs code differences (the code is current; images are partly 2.x-era)
| Item | Old PNGs (network-section, power-section, utilities-section, volume-mixer, system-monitor-graph) | Current code |
|---|---|---|
| Header | Big app icon tile + "Vorssaint" name + yellow "Mac awake" pill | Only the brand mark, centred, 48 pt wide x 28 pt high (`MenuPanelHeader`); optional BETA pill + feedback icon on beta builds only |
| Footer | Text links `Settings`, `List`, `Quit` | Two equal bordered buttons `Settings` (gearshape) and `Quit` (power); no `List` mode (the panel always navigates by tabs since 3.1.8) |
| Row captions in Utilities / Controls | Printed under every title | Caption is a TOOLTIP only, except when it reports a problem/permission/state (`needsAttention`, `captionStaysVisible`) or while editing |
| System card | "Separate metrics into their own items" checkbox card on top; Battery row + "Apps using significant energy" inside System | Not in the panel any more (it lives on the Monitor settings page); Battery bar + energy apps moved to the Power card |
| Tab bar | 6 tabs (no controls tab) | up to 12 tabs (see 3.4) |
`panel-*.png` (716 px wide = 2x of 358 pt incl. shadow; panel itself = 332 pt) are the CURRENT look (dark mode, no liquid glass) and are what the colour tokens
below were sampled from.

### 0.2 What the user sees (one sentence each)
1. A tray icon (the mark; changes when Keep Awake runs / update ready / mic muted). Hovering it shows a tooltip with the keep-awake state.
2. Optionally, live metrics (CPU %, GPU %, RAM, temperatures, network, disk, battery...) rendered as small two-line blocks next to the icon, either all in one
   item or as one clickable item per metric ("separate items").
3. Left-click opens a 332 pt wide dark rounded panel anchored to the icon: brand mark, an icon-only tab bar, a scrollable section body with an uppercase title, a
   Settings / Quit footer.
4. Right-click opens a context menu (Keep awake toggle, Activate for..., Cleaning Mode, Settings..., About, Uninstall an app..., Shelf, Check for updates..., Quit).

---------------------------------------------------------------------------------------------------------------------

## 1. Visual tokens

### 1.1 Colours
Dark theme is the reference (sampled from the PNGs, flattened to opaque sRGB). Light theme is derived from the Swift opacities (`PanelSurface`); light values
marked (est.) are estimates because no light screenshot exists.

| Token | Swift definition | Dark (sampled) | Light |
|---|---|---|---|
| `panel.base` | `regularMaterial` + `black 42%` (dark) / `white 68%` (light) | **#181818** (opaque fallback; with Acrylic use #000 @ 42% over the blur) | #F1F1F1 (est.; white 68% over blur) |
| `card.fill` | `white 7.5%` / `white 38%` | **#292929** | #F7F7F7 (est.) |
| `card.border` (0.7 pt) | `white 11%` / `black 9%` (raised-contrast: 28% / 24%) | **#414141** | rgba(0,0,0,.09) |
| `control.fill` | `white 8.5%` / `black 5.5%` | #313131 | rgba(0,0,0,.055) |
| `tile.fill` (temperature cells, hover tiles) | `primary 4.5%` | #323232 | rgba(0,0,0,.045) |
| `track` (usage bar track, slider track) | `primary 8%` (usage bar), `primary 16%/11%` (slider track) | #383838 (bar) / #484848 (slider) | rgba(0,0,0,.08) |
| `divider` (in-card `Divider`) | system separator | #4C4C4C | rgba(0,0,0,.10) |
| `row.hover` | `primary 6%`, radius 7, inset 3 | rgba(255,255,255,.06) | rgba(0,0,0,.06) |
| `text.primary` | `.primary` | #FFFFFF (renders ~#EAEAEA anti-aliased) | #000000 @ 85% |
| `text.secondary` | `.secondary` | **#A5A5A5** (rgba 235,235,245,.60 over card) | rgba(60,60,67,.60) |
| `text.tertiary` | `.tertiary` | #636364 (30%) | rgba(60,60,67,.30) |
| `tab.icon.idle` | `.secondary` @ 86% | #8E8E8E | |
| `accent` | `Color.accentColor` | **Windows system accent** (UISettings `Accent`); fallback **#007AFF** | same (fallback #007AFF) |
| `tab.selected.fill` | accent @ 20% (dark) / 13% (light) | #273A54 (with #007AFF) | accent @ 13% |
| `tab.selected.icon` | accent | #007AFF | accent |
| `green` | `.green` / light `rgb(0,.44,.18)` | **#3BDB63** | #007030 |
| `cyan` (GPU graph) | `.cyan` / light `rgb(0,.43,.54)` | **#48D8FC** | #006E8A |
| `mint` (memory graph) | `.mint` / light `rgb(0,.44,.40)` | **#2DDFCD** | #007066 |
| `yellow` | `.yellow` / light `rgb(.56,.36,0)` | #FFD60A | #8F5C00 |
| `red` | `.red` / light `rgb(.68,.08,.10)` | #FF453A | #AD141A |
| `orange` (boost, attention, warnings) | `.orange` / light `rgb(.68,.30,0)` | **#FF9E33** (boost slider sampled) / #FF9F0A | #AD4D00 |
| `pink` (disk write, health) | `.pink` / light `rgb(.68,.06,.34)` | #FF375F | #AD0F57 |
| `pressure pill` | colour @ 13% fill, text in colour | green #2E4031 bg / #3BDB63 text | |
| `shadow` (flyout) | NSPopover shadow | 0 8 24 rgba(0,0,0,.45) | 0 8 24 rgba(0,0,0,.25) |

Use the Windows accent for every `accent` use. `Color.accentColor` is also used for: selected tab, selected chips, slider fill (normal), switch-on track,
`UsageBar` < 60 %, disk bar < 75 %, row icons, links, selected list rows (accent @ 12-18 %).

### 1.2 Geometry
| Item | Value |
|---|---|
| Panel width | **332** (content width 308 = 332 - 2 x 12 padding) |
| Panel padding | 12 on all sides |
| Vertical rhythm of the main stack | `spacing: 12` between banner / header / tab bar / body / footer |
| Panel corner radius | **18** (measured: 36 px at 2x). Window is clipped to this radius |
| Panel arrow (popover tip) | ~26 wide x 11 tall, centred on the anchor icon, same fill as panel; the panel itself starts 11-13 below it |
| Panel height | `min(maxHeight, max(220, scrollHeight + chrome))`; `chrome = 180` (+ `banner + 12` when update banner shows); `maxHeight = max(360, workAreaHeight - 28)`; body scroll height = `min(contentHeight, max(80, maxHeight - chrome))`. Content height before first measurement uses per-section estimates: keepAwake 250, brightness 140, mixer 250, system 460, network 190, disk 360, power 170, fanControl 220, utilities 500, controls 360, toggles 420, wallpaper 480; metric detail: cpu/gpu/memory 430, network 330, disk 360, battery/power 360, fan/connectedDevices 240 |
| Card (`panelCard`) | radius **10**, padding **10** (0 for row-group cards), fill `card.fill`, 0.7 pt `card.border` |
| Row-group card | one card, rows separated by 1 px dividers; row inset H **10** / V **8**; divider starts at `10 + iconWidth(22) + iconSpacing(9) = 41` from the left (+25 in edit mode for the 16 px drag handle + 9 gap) and ends 10 from the right |
| Row icon column | 22 wide, 9 gap to text, icon font 15 semibold |
| Tab bar | HStack spacing 2, padding 4, radius 12, fill `card.fill`, 0.7 pt `card.border`; each tab: full-width share, **height 30**, radius 8, icon font 13.5 semibold; selected: `tab.selected.fill` + accent icon |
| Section title row | uppercase, 10 pt semibold, tracking 0.5, `text.secondary`; right side edit button 22 x 18 (icon 11 semibold) |
| Footer | two equal buttons, spacing 8, row height 30, top padding 4; each button min height 28, radius 7, fill `card.fill`, 0.8 pt `card.border`, text 11 medium `text.secondary`, `Label(icon + title)` centred, h-padding 7, `minimumScaleFactor .78` |
| Chip (keep awake) | capsule, min height 22, h-padding 8, text 11 medium, monospaced digits; idle fill `primary 7%` (pressed 16%); selected fill accent (pressed 80%), text white |
| Toggle | macOS `.switch`: regular ~38 x 22 in the Keep awake card header; `.small` ~ 32 x 18 in rows; `.mini` ~ 26 x 15 for option toggles. Windows: `ToggleSwitch` scaled to 40x20 / 34x18 / 28x15, track-on = accent, thumb white |
| Usage bar | height 5 (disk bar 6), capsule, track `track`, fill min width 3 |
| Sparkline | height 30 in System/Network/Disk/Power cards, 38 in metric detail; line 1.5 round caps/joins; area gradient colour@16% -> 0 (second series 8%); zero baseline 1 px `secondary@28%`; ceiling rule dashed [2,3] 1 px `secondary@40%` with 9 pt label at top-left over a `regularMaterial` 3 px-radius chip (label hidden when `monitorGraphScale` = false) |
| Process row | icon 14 (15 default), name 10.5, value 10.5 medium monospaced `text.secondary`, spacing 7 |
| Hairline radius for small list rows | 7 (`primary 3.5%` fill) |
| Pill/badge | capsule; `PanelHiddenBadge` 9.5 bold `secondary` on `primary 8%`, h 7 / v 3 |

### 1.3 Typography
SF Pro -> **Segoe UI Variable Text** (body) / **Segoe UI Variable Display** where Swift uses `design: .rounded` (numbers, chips, badges). Numeric text is
`monospacedDigit()` -> use tabular figures (`Typography.NumeralAlignment = Tabular` / `FontFeatures`). Sizes (pt/DIP) used in the panel:
section title 10 semibold uppercase kern .5 | sub-section label 10.5 semibold secondary | row title 12 semibold | row caption 10 | tooltips system |
keep-awake status 11 | option toggle title 11 medium (compact) / 11.5 medium (checkbox lists) | caption small 9.5 | hint 10 tertiary | big number
(metric detail) 28 semibold rounded | temperature cell value 15 semibold rounded, label 10 | rate value 13.5 semibold rounded, label 10 | power row value 13 semibold rounded |
usage row label 11, % 11 medium | process row 10.5 | footer 11 medium | tab icons 13.5 semibold.
Menu-bar/tray text: see 2.5 (monospaced `Cascadia Mono`/`Consolas`, 11.6 medium single line; value 12 semibold, label 6.6 medium in two-line blocks).

### 1.4 SF Symbol -> Segoe Fluent Icons (glyph = `FontFamily="Segoe Fluent Icons, Segoe MDL2 Assets"`)
Code points marked (v) are best recollection and must be verified in Character Map; where there is no Segoe glyph, use the named open-source
**Fluent UI System Icons** glyph (bundle as vector/PathGeometry, Regular style) - `fluent:` prefix below.

| SF Symbol | Use | Segoe Fluent / fallback |
|---|---|---|
| `moon.zzz.fill` | Keep awake tab | E708 (QuietHours) |
| `display.2` | Displays tab | E7F4 (TVMonitor) (v) / `fluent: desktop` |
| `speaker.wave.2` / `.fill` | Mixer tab, output picker | E767 (Volume); `speaker.slash.fill` E74F (Mute) |
| `cpu` | System tab, CPU | `fluent: developer_board` (no Segoe glyph) |
| `network` | Network tab | E968 (NetworkTower) (v) / E774 (Globe) |
| `internaldrive` / `externaldrive` | Disk tab / rows | EDA2 (HardDrive); external: E88E (USB) (v) |
| `bolt.fill` | Power tab, System power row | E945 (LightningBolt) |
| `wrench.and.screwdriver.fill` | Utilities tab | E90F (Repair) (v) |
| `switch.2` | Controls tab | EC11 (ToggleLeft) (v) |
| `togglepower` | Quick toggles tab | E7E8 (PowerButton) |
| `photo.on.rectangle` | Wallpaper tab | E91B (Picture) |
| `fanblades.fill` | Fan tab | (not on Windows) |
| `slider.horizontal.3` | section edit button | E9E9 (Equalizer) (v) / `fluent: options` |
| `chevron.right/.left/.down` | disclosure, back | E76C / E76B / E70D (small: E974 / E973 / E972) (v) |
| `gearshape` | footer Settings | E713 (Settings) |
| `power` | footer Quit | E7E8 (PowerButton) |
| `xmark.circle.fill` | close hosted panel | EB90 (StatusErrorFull) (v) |
| `checkmark`, `checkmark.circle.fill` | | E73E (CheckMark), E930 (Completed) |
| `eye` / `eye.slash.fill` | show/hide item | E7B3 (View) / ED1A (Hide) |
| `line.3.horizontal` | drag handle | E700 (GlobalNavButton) |
| `arrow.counterclockwise` | reset | E777 (UpdateRestore) / E72C (Refresh) |
| `arrow.up.forward.app` | open Task Manager / open window | E8A7 (OpenInNewWindow) |
| `trash` | delete | E74D |
| `clock` / `clock.arrow.circlepath` | Until chip, uptime / recent | E121 (Clock) / E81C (History) |
| `keyboard` | | E765 |
| `thermometer.medium` | | `fluent: temperature` |
| `memorychip`, `memorychip.fill` | memory | `fluent: ram` |
| `battery.100`, `.bolt` | battery | E83F..E850 series (battery levels) / E85E (BatteryCharging) (v) |
| `powerplug.fill` | adapter / power condition | `fluent: plug_connected` (Segoe has no plug glyph) |
| `heart.fill` | battery health | EB51 (HeartFill) |
| `display` | display condition, Dim | E7F4 |
| `laptopcomputer` | built-in display | E7F8 (DeviceLaptopNoPic) (v) |
| `play.circle` | auto start | E768-in-circle `fluent: play_circle` |
| `cursorarrow.motionlines` | mouse jiggle | `fluent: cursor` |
| `lock.fill` | pause when locked | E72E (Lock) |
| `app.fill` | applications condition | E71D (AllApps) |
| `pin` / `pin.fill` / `pin.slash` | | E718 / E718 (filled EE e.g. `fluent: pin`) / E77A |
| `mic`, `mic.slash.fill` | mic mute | E720 (Microphone) / `fluent: mic_off` (no Segoe slash variant) |
| `bell.fill` | system sounds | `fluent: alert` (Segoe: EA8F is not reliable - verify) |
| `doc.on.clipboard` | Clipboard | F0E3 (Paste) (v) / E77F |
| `square.grid.2x2` | Quick panel | F0E2 (GridView) (v) |
| `sparkle` | Cleaner | `fluent: sparkle` |
| `shippingbox` | Homebrew -> winget | E7B8 (Package?) (v) / `fluent: box` |
| `photo.on.rectangle.angled` | Media | E8B9 (Photo2)(v) |
| `rectangle.3.group` | Window layout | `fluent: layout_cell_four` / E8A9 (v) |
| `link` | Clean URL | E71B |
| `keyboard` (Cleaning) | | E765 |
| `text.viewfinder` | Copy text from screen | `fluent: scan_text` |
| `camera.viewfinder` | Screenshot | E722 / `fluent: screenshot` |
| `record.circle` / `stop.circle` | Recorder | E7C8 (Record) (v) / E71A (Stop) |
| `eyedropper` | Color picker | EF3C (Eyedropper)(v) |
| `web.camera` | Camera preview | E722 |
| `note.text` | Scratchpad | E70B (QuickNote) |
| `command` | Command bar | E721 (Search) |
| `arrow.down.app` | App updates | E896 (Download)/ E895 |
| `network` (port) | Port manager | E968 |
| `gauge.with.dots.needle.67percent` | speed test | EC4A (Speedometer)(v) / `fluent: top_speed` |
| `eject.fill` | eject | `fluent: eject` |
| `cable.connector` | connected devices | E88E (USB) (v) |
| `mic.slash.fill` badge | mic muted tray badge | red slashed mic (draw) |
| `sun.max.fill`/`moon.fill` | dark-mode toggle | E706 (Brightness) / E708 |
| `xmark.app` | Quit all apps | E8BB (ChromeClose)(v) / `fluent: dismiss_square` |
| `desktopcomputer` | desktop icons | E7F4 |
| `sparkles.tv` | screen saver | `fluent: tv` |
| `lock.fill` | lock screen | E72E |
| `display` | turn off display | E7F4 |
| `eye`/`eye.slash` | hidden files | E7B3 / ED1A |

---------------------------------------------------------------------------------------------------------------------

## 2. Status item -> Windows tray

### 2.1 Layout (macOS)
`StatusItemController` owns up to three kinds of items:
1. **Main item** (autosave name `VorssaintMenuBarItem`): variable length button = `[glyph 26x20 pt template image] [" "] [title]`. Title (attributed) =
   optional keep-awake countdown + 2 spaces + (when metrics are NOT separate) the metric blocks (see 2.5). Font: monospaced system, 11.6 pt medium
   (9.4 semibold if the title were stacked - never in current code: `lines()` always returns one dense line). Image position `.imageLeading`; `.imageOnly` if title empty.
2. **Metric items** (one per metric group, id `VorssaintMetric.<id>`), only when `menuBarSeparateMetrics` = true: no image, title = that group's blocks, tooltip = group title,
   click -> opens the panel focused on that metric's detail view (2.8).
3. **Clipboard preview item** (`VorssaintClipboardPreview`): text-only item showing the latest copy, truncated; hidden (not removed) when nothing to show; click opens the
   clipboard history window. Enabled by clipboard history + `clipboardHistoryMenuBarPreview`; length `clipboardHistoryMenuBarPreviewLength` (5...50, default 20).

### 2.2 Icon states (main item glyph)
Priority: hidden (see 2.6) > update available > keep-awake active > idle. Then a mic-muted badge is composited on top of whichever applies.
| State | Image |
|---|---|
| Idle | the brand mark, **template** (follows light/dark bar), or the symbol named by `menuBarIconSymbol` (gallery in 2.7) |
| Update available (`UpdateService.state == .available`) | the mark filled solid **blue** (`systemBlue`) |
| Keep awake active | `KeepAwakeActiveIcon` (`vorssaint` mark / `coffee` `cup.and.saucer.fill` / `eye` `eye.fill` / `moon` `moon.fill` / `light` `lightbulb.fill`) tinted with `KeepAwakeIconTint` (orange / green / blue / purple / pink / none = stays template, no colour) |
| Mic muted + `micMuteMenuBarIndicator` | red slashed mic (`mic.slash.fill`, 12 pt) appended 2 pt right of the glyph |
Icon canvas is fixed (26 x 20 pt) so state changes never shift neighbours. Memoised by a state key; not re-rendered every tick.

### 2.3 Tooltip (main item)
- Idle: `Trayloom: normal sleep` (`statusIdleTooltip`).
- Active with end: `Trayloom: awake until <short time>` (`statusActiveUntil` + locale short time, e.g. `3:45 PM`).
- Active no end: `Trayloom: awake indefinitely`.
- Active by automation: the automation sentence (`Active while an external display is connected` / `Active while connected to power` / `Active while a selected app is running` /
  `Active because an automatic condition is met`).
- Metric item tooltip = metric group title (CPU, GPU, Memory, ..., `Battery`, `Network`...). Clipboard item tooltip = longer preview (tooltipCharacters).

### 2.4 Countdown next to the icon (`showCountdownInMenuBar`, default false)
Only when keep awake is active. Text: `H:MM` when >= 1 h (`%d:%02d`), else `N min` (minimum `1 min`), `∞` when indefinite. A 30 s timer (tolerance 5 s) runs only while
active + countdown on + finite end. Separated from metrics by two spaces. With a countdown present metrics never stack.

### 2.5 Metric blocks (content of the title / of each separate item)
Order = `menuBarMetricOrder` (default `cpu, cpuTemperature, gpu, gpuTemperature, memory, battery, batteryTime, batteryTemperature, peripheralBattery, network, diskUsage, diskActivity, connectedDevices, power, fanSpeed`).
A metric renders only if: its `menuBar*` key is true (ALL default **false**; "metrics are opt-in"), its hub feature is installed, and hardware supports it (battery metrics need a battery; fan needs fans).
Only the **dense** preset exists (`menuBarPreset` = `dense`; `readable` only used by an old code path). Blocks are bitmaps, text `labelColor` (adapts), 2-line:
| Metric (`MenuBarMetric`) | Label (top, 6.6 pt medium) | Value (bottom, 12 pt semibold monospaced-digit) | Reserved min width string |
|---|---|---|---|
| cpu | `CPU` | `NN%` | `100%` |
| gpu | `GPU` | `NN%` | `100%` |
| memory | `RAM` | `NN%` of used/total (`--%` if unknown) per `menuBarMemoryStyle`: `dot` (coloured pressure dot only), `percent` (default), `both` (dot 4.8 pt + gap 4 + %) | `100%` |
| cpuTemperature / gpuTemperature | `CPU°C` / `GPU°C` (or `°F`) | `47°` | `999°` |
| batteryTemperature | `BAT°C` | `31°` | `999°` |
| combined usage+temp (`menuBarCombineTemperatures`, default **true**, values mode only) | `CPU` / `GPU` / `BAT` | `12% 47°` | `100% 999°` |
| network | two lines, right-aligned: `↓<rate>` over `↑<rate>` (or up over down if `menuBarNetworkUploadFirst`), mono 9.2 semibold, line height 10 | compact rate: `0B`, `320K`, `1.2M`, `10M`; bits: `0b`,`320Kb`,`9.6Mb` | `↓999Kb` |
| diskUsage | `DSK` | per `menuBarDiskStyle`: `percent` `NN%` (default) / `free` `123 GB` / `used` `456 GB` (`MetricFormat.diskBytes`, decimal 1000) | `100%` or `1000 GB` |
| diskActivity | `R<rate>` over `W<rate>` stacked, mono 9.2 | `R1.0M` / `W2K` | `R8888M` |
| battery | **battery block**: battery symbol (18 wide, level-aware; charging bolt; plug state) + `NN%` (12 semibold), 4 gap. Value turns **orange** (`early` warning) or **red** (`low`) via the island's low-battery thresholds | | `100%` |
| batteryTime | `BAT` | `1h 25m` (`H`h `M`m); `...` while estimating (on battery, not charging) | `99h 59m` |
| peripheralBattery | `KBD`/`MOU`/`TRK`/`AUD`/`PER` | `87%` or `87%+2` (+extra device count, max +9) | `100%+9` |
| connectedDevices | `USB` | device count | `99` |
| power | `PWR` | `9W` | `99W` |
| fanSpeed | `RPM` | `1200/1300` | (not on Windows) |
Block image height 21 (dense), width = max(label width, value group width incl. reserved width) + 0.5. Number formats: percent = rounded `NN%`; temperature compact `%.0f°`;
`temperatureUnit` celsius|fahrenheit.
**Usage = Bars** (`menuBarMetricAppearance` = `bars`): CPU/GPU/RAM/DSK(percent style) become a **vertical gauge** instead of text: 19 x 20 pt image (dense; +7 w when a RAM pressure dot is shown):
3 stacked letters of the label at left (6.1 pt semibold, width 6), optional dot, then a 9 x 18 rounded-rect outline (radius 2.2, 1.15 pt, `labelColor`) filled bottom-up
to `fraction` in `normal`/`elevated`/`critical` colour (defaults `#64D2FF` / `#FFD60A` / `#FF453A`; thresholds default 70 / 90 %, min 1/99; stored `menuBarUsageBarMediumThreshold`, `...HighThreshold`).
Unknown value = dashed dash. Bars disable "combine temperatures". Other metrics stay numeric.
**Block spacing** (`menuBarMetricSpacing`: `standard` / `compact`, default `compact`): `compact` reserves only the current digit shape (min 2 digits, with a session high-water mark so a block widens once
and never shrinks again) and joins blocks with a hair space; `standard` reserves the metric's absolute maximum and joins with one space.
**Separate items** (`menuBarSeparateMetrics`, default false): one item per group. Groups: with `combineTemperatures` (values mode) cpu+cpuTemp -> one `CPU` group, gpu+gpuTemp -> `GPU`, battery+batteryTemp -> `Battery`;
every other metric is its own group. An item that renders nothing for 5 consecutive refreshes is removed (kept blank before that).
`menuBarNetworkUploadFirst` default false. `networkSpeedUnit` bytes|bits default bytes affects the network block and the panel. `monitorInterval` 1|2|5 s (default 2): sampler runs
continuously only while at least one metric is pinned (`setMenuBarActive`).

### 2.6 Hiding the glyph / main item
- `menuBarHideIconWithMetrics` (default false, "Hide the app icon while metrics are shown"): while metrics are rendered inside the main item, the glyph (and its leading space) is removed. It
  comes back automatically when metrics leave, when an update is available, when keep awake is active with a visible signal (tint != none OR non-default icon), or when the mic-muted
  indicator shows. Not applicable in separate mode (glyph stays unless the whole main item steps aside).
- Separate mode + hide option: the whole main item is hidden when >=1 metric item is really showing a reading, its own title is empty and no signal needs it.
- Dynamic Island standing in (`notchHidesMenuBarIcon` + island on + island not hidden in full screen) hides the glyph unless a signal needs it; an empty main item is hidden entirely.
- Invariant: the main item must never be empty (no image and no text); with a pinned metric item present the panel stays reachable through it.

### 2.7 Settings that shape the status item (Monitor / General pages; keys exactly as stored)
| Key | Type | Default | Values | Effect |
|---|---|---|---|---|
| `showCountdownInMenuBar` | bool | false | | countdown text (2.4); label "Show remaining time next to the icon" |
| `menuBarIconSymbol` | string | "" | gallery or any valid symbol | replaces the mark; `menuBarIconGallery` = 40 solid symbols (bolt, star, heart, flame, sparkles, leaf, drop, snowflake, sun, moon.stars, cloud, mountain, circle, square, triangle, diamond, hexagon, seal, half-circle, hexagongrid, infinity, command, cpu, memorychip, gauge, fanblades, gear, terminal, waveform, wand, key, crown, gamecontroller, headphones, music.note, paperplane, pawprint, cat, hare, tortoise); strings: `Menu bar icon`, `Choose the icon Trayloom shows in the menu bar.`, `Other symbol`, `Type the name of any SF Symbol. Leave it empty to use the Trayloom icon.`, `This Mac has no symbol with that name.`, `Use the Trayloom icon`; `Can't find the icon?` / `A crowded menu bar can hide it, especially on Macs with a notch.` |
| `menuBarCPU/GPU/Memory/CPUTemperature/GPUTemperature/BatteryTemperature/Network/DiskUsage/DiskActivity/Battery/BatteryTime/PeripheralBattery/ConnectedDevices/Power/FanSpeed` | bool | false | | which metrics are pinned |
| `menuBarMetricOrder` | csv | default order above | drag order in Settings tiles | order |
| `menuBarMetricAppearance` | enum | `values` | `values`/`bars` | `Usage display`: `Values` / `Bars`; caption `Bars apply to CPU, GPU, memory and disk usage. Other readings stay numeric.` |
| `menuBarUsageBarNormalColor/ElevatedColor/CriticalColor` | #RRGGBB | #64D2FF / #FFD60A / #FF453A | | `Bar colors and limits`: `Normal color`, `Medium color`, `High color` |
| `menuBarUsageBarMediumThreshold/HighThreshold` | int % | 70 / 90 | 1...99 / (medium+1)...100 | `Medium from`, `High from` |
| `menuBarCombineTemperatures` | bool | true | | `Combine usage and temperature` / `When usage and temperature for the same item are enabled, show them in one block.` |
| `menuBarSeparateMetrics` | bool | false | | `Separate metrics into their own items` / `Separates active blocks in the menu bar and keeps usage and temperature together when combine is on.` |
| `menuBarMetricSpacing` | enum | `compact` | `standard`/`compact` | `Menu bar spacing`: `Standard` / `Compact` |
| `menuBarHideIconWithMetrics` | bool | false | | `Hide the app icon while metrics are shown` / `The icon returns by itself when metrics leave the bar and when there is something to signal (an update ready, "Keep awake" running or the microphone muted).` |
| `menuBarNetworkUploadFirst` | bool | false | | `Upload above download` |
| `menuBarMemoryStyle` | enum | `percent` | `dot`/`percent`/`both` | `Show memory as` (`Pressure dot`...) |
| `menuBarDiskStyle` | enum | `percent` | `percent`/`free`/`used` | `Disk display`: `Used percentage`, `Available space`, `Used space` |
| `menuBarLabelStyle` | enum | `compact` | `compact`/`classic` | legacy, unused by the dense renderer |
| `menuBarPreset` | enum | `dense` | only `dense` | |
| `monitorInterval` (`monitorIntervalSeconds`) | int | 2 | 1/2/5 | `Update every`: `1 second`, `2 seconds`, `5 seconds` |
| `temperatureUnit` | enum | `celsius` | `celsius`/`fahrenheit` | |
| `networkSpeedUnit` | enum | `bytes` | `bytes`/`bits` | `Network speed unit` (`MonitorLayoutStrings.networkSpeedUnit`); `Readings and alerts` (`.shared`) is the section heading |
| `monitorMemoryMetric` | enum | `used` | `used`/`app` | `Measure memory as` `Memory Used` / `App Memory` |
| `keepAwakeRightClickToggle` | bool | false | | right-click toggles keep awake instead of opening the menu |
| `keepAwakeIconTint`, `keepAwakeActiveIcon` | enum | orange, vorssaint | see 2.2 | active glyph |
| `micMuteMenuBarIndicator` | bool | | | mic-muted badge |
| `statusItemPlacementGeneration` | int | 0 | | macOS-only recovery |
`MenuBarAppearanceStrings` (enUS): label `Usage display`, values `Values`, bars `Bars`, caption `Bars apply to CPU, GPU, memory and disk usage. Other readings stay numeric.`,
customize `Bar colors and limits`, normalColor `Normal color`, mediumColor `Medium color`, highColor `High color`, mediumFrom `Medium from`, highFrom `High from`.
`MonitorLayoutStrings` (enUS): shared `Readings and alerts`, networkSpeedUnit `Network speed unit`.

### 2.8 Clicks (macOS behaviour to reproduce)
- Left click main item / glyph: toggle the panel (`toggleMainPopover`): opens on the normal tab view; if Dynamic Island routes the app panel the island opens instead.
- Left click a metric item: open the panel on that metric's detail (`MetricDetailKind`): cpu/cpuTemperature -> cpu; gpu(+temp) -> gpu; memory -> memory; network -> network; diskUsage/diskActivity -> disk;
  battery/batteryTemperature/peripheralBattery -> battery; batteryTime/power -> power; fanSpeed -> fan; connectedDevices -> connected devices. If the panel is already open on another metric it
  re-anchors (after 160 ms) under the clicked item; clicking the item of the metric currently shown closes it.
- Left click the clipboard preview item: toggle the clipboard history window.
- Right click any item: if keep awake available and `keepAwakeRightClickToggle` -> toggle keep awake; else context menu (2.9). An open panel is closed first.

### 2.9 Context menu (right click) - exact items and order
1. (if Keep awake installed) `Enable keep awake` / `Disable keep awake`
2. (if keep awake installed and NOT active) `Activate for…` submenu: `15 minutes`, `30 minutes`, `1 hour`, `2 hours`, `4 hours`, `8 hours`, `Indefinitely`
3. (if Cleaning Mode installed) `Cleaning Mode`
4. separator (only if any of 1-3 present)
5. `Settings…` (Ctrl+, equivalent)
6. `About Trayloom`
7. (if Uninstaller installed) `Uninstall an app…` (opens Settings on the Uninstaller page)
8. (if Shelf installed and enabled) `Shelf`
9. `Check for updates…`
10. separator
11. `Quit Trayloom` (Ctrl+Q equivalent)

### 2.10 Windows mapping
- **Tray icon**: `Shell_NotifyIcon` (or `H.NotifyIcon`/`System.Windows.Forms.NotifyIcon` as Trayloom already does) with a stable `guidItem` (NIF_GUID) so Windows remembers its position/visibility
  across restarts. Icon size = `SM_CXSMICON` at the current DPI (16/20/24/32); render glyph with WPF `RenderTargetBitmap` -> `HICON`. Light/dark adaptation: read
  `HKCU\...\Themes\Personalize\SystemUsesLightTheme`, listen to `WM_SETTINGCHANGE "ImmersiveColorSet"` and re-render (white mark on dark taskbar, near-black on light).
  Tinted active states and the update state are coloured (non-template) bitmaps. Mic-muted badge: 40 % size red slashed mic drawn bottom-right of the icon bitmap (a side-by-side badge does not fit 16 px).
- **Tooltip**: `NOTIFYICONDATA.szTip` (max 127 chars) = 2.3 text; update on every keep-awake change and every 30 s while a countdown runs. Win11 shows it as the modern tooltip.
- **Metrics**: Windows tray cannot show text next to an icon. Provide both (user option "Show metrics as": `Tray icons` / `Taskbar strip`, default `Taskbar strip`... see below):
  1. **Per-metric tray icons** (= "separate items" mode, the only way to get text into the tray): each metric group = its own `NotifyIcon` (own GUID `...Metric.<id>`) whose bitmap is the 2-line block
     (label 6.6 over value 12 semibold, drawn to the icon size; at 100 % scale 16 x 16 only ~3 digits fit: use value-only `12` + tiny `%` and show label in the tooltip; at >=150 % use label+value). Click -> panel on the metric detail.
  2. **Taskbar metrics strip** (= non-separate "all in one item" mode): a topmost, non-activating, layered `WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE|WS_EX_LAYERED` window docked immediately left of the notification
     area (rect from `Shell_NotifyIconGetRect` of the main icon / `TrayNotifyWnd` via `FindWindowEx(Shell_TrayWnd,"TrayNotifyWnd")`), height = taskbar height, vertically centring the 21 pt blocks, background transparent
     (so it reads as part of the taskbar) with ClearType-free grayscale text; text colour = taskbar foreground. Hover shows nothing; click opens the panel (per-block hit regions = per-metric click behaviour of separate items).
     Re-dock on `TaskbarCreated`, DPI changes, taskbar auto-hide (hide with the taskbar: poll `ABM_GETSTATE`/`GetWindowRect`), full-screen apps (hide on `SHQueryUserNotificationState != QUNS_ACCEPTS_NOTIFICATIONS`).
  3. Countdown text (2.4) goes into the strip (as the first segment) or, in tray-only mode, only into the tooltip.
  4. `menuBarHideIconWithMetrics`: hide the main tray icon only when the strip is on (Windows will not allow a zero-icon app; keep the icon when a signal is active).
- **Windows 11 tray overflow**: new icons start in the overflow flyout. Provide a one-time first-run tip ("Drag the Trayloom icon from the ^ overflow onto the taskbar to keep it visible") =
  mac `iconMissingTitle`/`iconMissingCaption` (`Can't find the icon?` / `The icon may be hidden in the taskbar overflow (^). Drag it out to keep it visible.`) and a Settings button that opens `ms-settings:taskbar`. Optionally set
  `HKCU\Control Panel\NotifyIconSettings\<id>\IsPromoted = 1` (undocumented, per-icon) to pin it.
- **Right-click menu**: `ContextMenuStrip`/WPF context menu with the 11 items in 2.9 (keep the existing Trayloom items `Quick panel`, `Features...`, `Start with Windows` out of this menu or move them into Settings - the mac menu has none of them).
- **Panel anchor**: `Shell_NotifyIconGetRect(NOTIFYICONIDENTIFIER{guid})` gives the icon rect (works for per-GUID icons); for the strip use its own HWND rect.

### 2.11 Gap vs Trayloom (status item)
Existing `TrayController.cs`: WinForms `NotifyIcon`, static icon, tooltip always `Trayloom`, no GUID, no theme-adaptive glyph, context menu = `Quick panel`, `Features...`, separator, `Start with Windows`,
separator, `Quit Trayloom`; left-click opens `QuickPanelWindow` using the CURSOR position (not the icon rect). Missing: icon states (update/active/tint/mic badge), keep-awake tooltip, countdown, every
metric block / bars / separate items / strip, hide-glyph logic, right-click toggle option, the 11-item context menu, `Check for updates...`, `About`, per-metric click routing, clipboard preview item, first-run overflow tip.
Existing `MonitorHub` "pinned readout" (`Osd` pill in a screen corner, text joined by 3 spaces) is NOT the block renderer - replace by the strip.

---------------------------------------------------------------------------------------------------------------------

## 3. The panel (flyout) - host and chrome

### 3.1 Window behaviour (macOS = `NSPopover`, `applicationDefined`)
- Opens anchored under the clicked status item; has an arrow; width 332; height per 1.2. Shown with AppKit's animation; closes with a ~0.15 s fade.
- Stays open when the user works in our own Settings window (so reordering is live); outside clicks (global mouse-down not on a status item) close it unless `preventsPopoverDismissal`:
  `viewKeepsPopoverOpen` (a hosted utility is showing) | a modal alert from the panel | AirPlay picker | a Homebrew operation running | the Cleaner scanning/cleaning | the Uninstaller scanning/removing.
- Esc closes (only when the panel is the key window and no IME composition is active). Space/Return are forwarded to the focused control when a hosted utility keeps it open.
- Clicking the status item toggles. Opening the panel refreshes: keep-awake passwordless status, monitor sampling needs.
- Sampling needs follow the visible content: System tab -> system; Network -> network; Disk -> disk; Power -> power + power card; metric detail -> its own needs; others none. The monitor stops when the panel disappears (unless a metric anchor switch is in progress).
- Selected tab: starts at the first visible tab; remembered only while the panel object lives. A request to "focus section X" (from Settings tabs, island, etc.) selects that tab.
- Height is re-measured after every layout pass; content taller than the cap scrolls with an **overlay** scroller (no gutter, auto-hides).

### 3.2 Windows mapping (host)
WPF `Window`: `WindowStyle=None`, `AllowsTransparency=True` (or per-pixel `WS_EX_NOREDIRECTIONBITMAP` + `DwmExtendFrameIntoClientArea`), `ShowInTaskbar=False`, `Topmost=True`, `ResizeMode=NoResize`,
`ShowActivated=True`, `WS_EX_TOOLWINDOW`. Backdrop: `DWMWA_SYSTEMBACKDROP_TYPE = DWMSBT_TRANSIENTWINDOW` (Acrylic) + a `panel.base` overlay at the Swift opacity; fallback opaque `panel.base` when transparency is
off (`SPI` "transparency effects" / `AccessibilityReduceTransparency` -> `UISettings.AdvancedEffectsEnabled`). Corner radius 18: clip the root `Border` (CornerRadius 18) and use `DWMWCP_DONOTROUND`; draw 1 px outline `card.border`
and the shadow in a 20 px transparent margin. Position: `iconRect` from `Shell_NotifyIconGetRect`; taskbar bottom (default): `x = clamp(iconCenterX - 166, workArea.Left+8, workArea.Right-332-8)`, `bottom = min(iconTop - 11 - 8, workArea.Bottom - 12)`;
taskbar top/left/right: mirror (arrow on the side facing the icon). Arrow = small rounded triangle ~26 x 11 centred on the icon X (clamped inside the radius), same fill. `maxHeight = max(360, workArea.Height - 28)` of the monitor holding the icon.
Close rules: `Deactivated` (unless `preventsDismissal` flags) + global low-level mouse hook not needed; Esc; second tray click toggles (use `LastClosedTick` guard as Trayloom does: ignore a toggle within 300 ms of a close). Dark/light follows
the app's `appearance` setting (system/light/dark, default per `AppAppearance.fallback`) and Windows apps theme.
Animation: fade+8 px slide-up 120-150 ms (Windows flyout convention), or none if `SystemParameters.ClientAreaAnimation` is off.

### 3.3 Header
`MenuPanelHeader`: ZStack, height 28 + vertical padding 4 (=36 total), centred **brand mark 48 pt wide** (template tinted `white` dark / `rgb(.03)` light). Trayloom mark here (never the saturn).
Beta builds only: left a `BETA` pill (9 bold, orange on orange@18 %, capsule) and right an icon button (`bubble.left.and.text.bubble.right`, 11 medium secondary, tooltip = feedback "open" string).
Above the header (conditional): **UpdateBanner** (11.1).

### 3.4 Tab bar (`sectionNavigation`)
Icon-only segmented bar, tabs = visible sections in saved order (`PanelLayout.order`, filtered by `isVisibleInPanel`). Each button: icon centred, full-width share of the bar, 30 high, radius 8;
selected = accent icon + `tab.selected.fill`; idle icon `secondary@86%`. Tooltip + accessible name = section title. Bar: fill `card.fill`, 0.7 pt border, radius 12, padding 4, spacing 2.
Click selects; keyboard focus ring follows `focusedSection`. If the active section disappears the first visible tab is used.
**Tabs (canonical order = `PanelSectionID.allCases`)**: 
| # | id | icon | Title (tab tooltip + body heading) | Visible when | Hub gate (any of) |
|---|---|---|---|---|---|
| 1 | `keepAwake` | moon.zzz.fill | `Keep awake` | `panelShowKeepAwake` (true) && feature | keepAwake |
| 2 | `brightness` | display.2 | `Displays` | feature && `panelShowBrightness` && **`brightnessControlEnabled`** (false by default: enabled from Settings, the tab never shows an empty "enable" screen) | brightness |
| 3 | `mixer` | speaker.wave.2 | `Volume mixer` (or `Audio priority` when only that feature is installed) | `monitorShowMixer` | mixer, audioPriority |
| 4 | `system` | cpu | `System` | `monitorShowSystem` | monitorCPU, monitorGPU, monitorMemory, connectedDevices |
| 5 | `network` | network | `Network` | `monitorShowNetwork` | monitorNetwork |
| 6 | `disk` | internaldrive | `Disks` | `monitorShowDisk` | monitorDisk |
| 7 | `power` | bolt.fill | `Power` | `monitorShowPower` | monitorPower |
| 8 | `fanControl` | fanblades.fill | `Fan control` | **not on Windows** (omit) | |
| 9 | `utilities` | wrench.and.screwdriver.fill | `Utilities` | `panelShowUtilities` | any of quickLauncher, cleaner, homebrew, appUpdates, mediaTools, clipboardHistory, windowLayout, uninstaller, urlCleaner, cleaningMode, screenOCR, colorPicker, screenshot, screenRecorder, cameraPreview, scratchpad, commandBar, portManager |
| 10 | `controls` | switch.2 | `Controls` | `panelShowControls` | any of scrollInverter, linearScroll, focusFollowsMouse, mouseAcceleration, mouseNavigation, mouseButtonShortcuts, switcher, finderCutPaste, autoQuit, shelf, windowMaximizer, dockPreview, keyboardDebounce, dockClick, spacesOrder, middleClick, textSnippets, superKey, radialMenu, mouseClickDebounce, notch |
| 11 | `toggles` | togglepower | `Quick toggles` | `panelShowToggles` | quickToggles, micMute, quitApps |
| 12 | `wallpaper` | photo.on.rectangle | `Wallpaper` | `panelShowWallpaper` | wallpaper |
Persisted: `panelSectionOrder` (csv of ids; unknown/missing ids appended at canonical positions: `brightness` right after `keepAwake`, `disk` right after `network`, `controls` right after `utilities`), each section's visibility key (above; monitoring ones reuse `monitorShow*`), `panelCollapsedSections` (csv; only used by the legacy list mode; reset once for version 2.15.1).
Default install (Essential preset) = mixer, keepAwake, monitorCPU/GPU/Memory/Network/Disk/Power => tabs: Keep awake, Volume mixer, System, Network, Disks, Power. Tabs appear/disappear immediately when a feature is installed/uninstalled in the Features hub or a visibility toggle flips in Settings.
Windows additions: none of the dock/Spaces/macOS-only controls appear (see 5.10).

### 3.5 Section header row (`PanelSection` in navigation mode: `collapsible = false`)
- Left: the title (`sectionTitle`: uppercase, 10 semibold, kern .5, secondary). No chevron in navigation mode (the chevron + collapse persist only in the legacy list mode).
- Right (only for sections with `supportsEditing`: utilities, controls, toggles, system, network, disk, power; hidden while a hosted utility is shown in Utilities): edit button = icon `slider.horizontal.3` 11 semibold in a 22 x 18 hit box, radius 6, `secondary`, tooltip `Edit`.
- Edit mode: button turns into `OK` pill (`checkmark` + `OK`, 10.5 bold white on accent, radius 8, height 24, h-padding 8; tooltip = `uninstallerDoneTitle`) and a reset button (`arrow.counterclockwise`, 10.5 semibold, 22 x 22, radius 7, `primary 7%` fill, tooltip `Default`) appears to its left. Entering edit mode expands a collapsed section.
- While editing: every row shows a drag handle (`line.3.horizontal`, 16 wide, tooltip `Drag to reorder the panel sections and use the eye to show or hide each one.`), hidden items are listed dimmed with a `Hidden` badge and an eye button (`PanelInlineHideButton`: 11 bold, 24 x 22, radius 7, `primary 10%` fill; `eye.slash.fill` secondary when visible with tooltip `Hide from panel`; `eye.fill` accent when hidden with tooltip `Show in panel`). In rows where the caption was hidden the caption re-appears.
- Dragging a row shows a card preview (min width 220) and reorders live (drop-enter move); order persisted per list.

### 3.6 Footer
`Settings` (gearshape) opens the Settings window on the page of the hosted utility if one is showing (`hostedSettingsPage`) else on General, and leaves the panel open. `Quit` (power) terminates the app.
Footer present in every state (tabs, metric detail).

### 3.7 Metric detail mode (opened from a metric item or the connected-devices row)
Replaces tab bar + body: header (brand mark) stays; below it `metricNavigationHeader`: back button (`chevron.left` 11 semibold, 24 x 24, radius 7, `card.fill` + 0.7 border) + `Label(title, symbol)` 12.5 semibold. Back -> returns to that section's tab. Body = `MetricDetailView(kind)` (width 308, scroll):
- **Summary card** (`panelCard`): row `[symbol 15 semibold, colour per kind, 18 wide][primary value 28 semibold rounded mono][secondary value 10.5 medium secondary, 2 lines]` + history graph 38 high with ceiling label.
  Colours: cpu/network = accent, gpu = cyan, memory = mint, disk = yellow, battery = green, power = orange, fan/devices = cyan.
- **Detail card**: rows `[icon 14?][title 10.5 secondary][spacer][value 11 medium primary, mono, 1 line middle-trunc]`, spacing 7; values: CPU: `Hardware usage` % / `Temperatures` / `Up for`; GPU: usage/temperature; Memory: `Memory Used / App Memory` `x / y`, `Pressure` (pill), `Compressed`, `Cached files`, `Swap used`; Network: `Download`,`Upload`, `This session` `↓a  ↑b`; Disk: name `NN% used`, `available`, `purgeable` (>=500 MB), `Read`,`Write`; Battery: `Charge`, `Battery` `x W · Charging|On battery`, `Temperatures`, `Battery health`, `Cycles`, peripheral list (max 5) or `No devices found`; Power: `System`, `Adapter`, `Battery`, `Battery time remaining`; Fan: per fan `Fan N` / `rpm`; Connected devices: list `name` + vendor (`USB Device` for unnamed; empty `No external devices connected`).
- **Speed test card** (network only), **process card** (cpu/gpu/memory/network/power(energy)): `Hardware usage`-style 10 medium tertiary heading (`Apps using network` / `Apps using significant energy` / `Hardware usage`) + Activity Monitor button + up to 15 process rows (`Measuring…` while loading; empty strings `No apps using network now`, `No significant energy use`).
Graph scales: percent kinds fixed 0-100 %; network/disk/power auto-scale to a "round" ceiling (1,2,5,10,20,50,100,200,500 x unit^n; unit 1024 bytes / 1000 for bits and watts).

### 3.6b Update banner (`UpdateBanner`, above header)
- `.available(version)`: full-width rounded (10) button filled accent (orange if pre-release): left `arrow.down.circle.fill` 16 white; two lines `Update available` (12 semibold white) / `Version <v>` (10.5 white@85 %); right white capsule `Update` (11 semibold, accent text). Click -> shows the update preview window.
- `.downloading(p)`: card (`primary 6%`, radius 10) `Downloading update…` 11.5 medium + `ProgressView(value)` + `NN%` 10.5; `.installing`: spinner + `Installing and restarting…`.
- Height reserved in `chrome` while visible.

---------------------------------------------------------------------------------------------------------------------

## 4. Shared row components (used by Utilities, Controls, Quick toggles)

### 4.1 `UtilityActionButton` (action rows)
Layout (HStack, spacing 9): `[drag handle (edit)] [icon 15 semibold, 22 wide, accent (orange if needsAttention; secondary if hidden in editor)] [VStack(spacing 1): Title 12 semibold (<=2 lines) (+ optional BETA pill 8 bold orange, caps, tracking .4); caption 10 (only when shown)] [spacer] [shortcut chip] [chevron.right 9 semibold tertiary]`.
Insets H10/V8; hover = `row.hover`; whole row clickable (`contentShape`). Caption shown only when `needsAttention || captionStaysVisible || isEditing`; otherwise it is the tooltip. Orange caption = problem.
Shortcut chip: text of the CURRENTLY REGISTERED shortcut only (feature available AND its enable keys on) e.g. `^⌘V`; 9 semibold rounded, tertiary, h5/v2, radius 5, `primary 6%`.
Permission variant: row content (no chevron) + below, indented 31, a mini bordered button `Label(permissionButtonTitle, "hand.raised.fill")` 10 semibold; plus optional accessory mini button (`Label(accessoryTitle, accessorySystemImage)` 9.5 semibold; e.g. `Recent captures` with `clock.arrow.circlepath`).
Edit mode: no chevron/chip; visibility eye button (see 3.5).
### 4.2 `PanelToggleRow` (switch rows)
Same icon/title/caption block (VStack spacing 2); icon colour: orange if needsAttention, accent when on, secondary when off. Trailing: `Toggle .switch .small` (or eye button in edit mode).
Optional: green `Label(activeText, checkmark.circle.fill)` 9.5 medium; permission mini button; accessory mini button `Label(accessoryTitle, "arrow.up.forward.square")` 9.5 semibold (shown only when not editing), e.g. `Settings`, `Manage`, `Open the shelf (3)`.
Caption (only when needsAttention or editing): `Permission required: Accessibility` style (see strings). Description otherwise tooltip + accessibility hint.
### 4.3 Sub-row (option under a row): left inset 41, right 10, bottom 8 (`panelSubRowInsets`), 10.5 medium secondary; e.g. `Show ⌘Tab with large icons` + mini switch; `Global debounce window` stepper `NN ms`.
### 4.4 Permission wording (Windows)
mac `Permission required: Accessibility / Screen Recording / Camera` -> Windows rarely needs permissions (hooks need none; elevated windows need admin). Keep the mechanism for: UIAccess/elevation ("Run as administrator to control elevated windows"), Camera/Microphone privacy toggles (`ms-settings:privacy-webcam`), Location etc. Strings: `Permission required`, `Grant access`, `Open System Settings…` -> `Open Windows Settings…`.

---------------------------------------------------------------------------------------------------------------------

## 5. Sections

### 5.1 KEEP AWAKE (`KeepAwakeCard`) - tab 1
**Layout** (title `KEEP AWAKE`, body = one card, VStack spacing 10):
1. **Status row**: left status text (11 secondary), right switch (regular size). When active with an end date the row becomes `[status (hidden if too narrow via ViewThatFits)] [spacer] [+15m +30m +1h chips (capsule, same style)] [switch]`; extend chips call `extend(minutes)`.
   Status text: inactive `The Mac follows its normal energy rules` (-> `The PC ...`); active+end `Ends in 1 h 05 min` (re-evaluated every 1 s; `%d h %02d min` / `%d min %02d s` / `%d s`); active no end `Active until you turn it off`; active by automation: the automation string (2.3).
2. **Chips**: duration chips `15m 30m 1h 2h 4h 8h ∞` (`DurationPicker.choices = [15,30,60,120,240,480,0]`, short labels localised `15m`,`1h`; `0` = `∞`; accessibility names `15 minutes`... `Indefinitely`) + **Until chip** (`clock` + `Until…`). If a single row of 8 does not fit the card width -> two rows: first 4 chips, then remaining 3 + Until chip (at 308 pt this is the normal case).
   Clicking a chip starts (or switches) a manual session; clicking the highlighted chip stops it. The switch starts `startLastPick()` (reuses the last pick; default duration `defaultDuration` = 0 indefinite) or stops. A selected timed chip shows a live countdown `58:12` / `1:05:12` (`%d:%02d` / `%d:%02d:%02d`) in place of its label (width reserved for `0:00:00`).
   The Until chip while an "until" session runs shows the end time (`h:mm a`) highlighted; click stops.
3. **Hint line** (10, refreshed every 30 s): `Click a chip to start. Click it again to stop` (tertiary), or - when battery protection blocks starting - orange `Label("Battery at N%. Plug in or lower the battery limit to start", battery.25percent)`.
4. **Options** disclosure (see below) - 11.5 semibold secondary, `chevron.right` rotates 90.
5. Divider.
6. **Closed-lid row**: title `Keep going with the lid closed` (12) + 10 caption + mini switch. Caption by state: configuring -> `Configuring…`; failed -> red `Couldn't turn on closed-lid mode. Try again.`; active -> `Sleep fully disabled. Mind the power`; preferred but no session -> `Applied whenever "Keep awake" is active`; else passwordless-ready `Ready. Toggles without a password` or `Will ask for the administrator password once`.
**Until picker popover** (`KeepAwakeEndTimePicker`, 224 wide, padding 10): header `Until` (11 semibold) + native time field (hour:minute); two **digit wheels** (`Hour (0–23)` 0-23, `Minute` 0-59): each = value 26 medium mono accent on accent@13 % (hover 20 %) rounded-7 box 52 x 36, neighbours above/below 12 secondary 52 x 20; scroll wheel (trackpad delta threshold 8; mouse notch 1), click neighbour, up/down arrows step with wrap; ":" separator 24 tertiary; divider; footer summary (10 secondary, refreshed every 30 s) `<relative day, e.g. Today/Tomorrow> · 2h 15min` (resolved next occurrence of that time, DST-safe) + default button `Start`. Picker value is only saved when Start is pressed (`keepAwakeUntilTime`).
**Options (expanded, indented 19)**:
- Icon picker card (radius 9, `secondary 5.5 %`, padding 8): header `Active icon` + current name; 5 icon buttons (Vorssaint->Trayloom, `Coffee`, `Eye`, `Moon`, `Lightbulb`), each 29 high radius 7, selected accent@14 % + 1.2 accent border, unselected `secondary 6 %` + 1 px `secondary 13 %`; header `Active icon color` + name; 6 tint buttons (`Orange`,`Green`,`Blue`,`Purple`,`Pink`,`No color`) 24 high, 12 px dot (none = ring + diagonal slash).
- Toggle `Allow the display to sleep` (icon display) -> `keepAwakeAllowDisplaySleep` (caption `Keeps the Mac awake while the display follows its normal sleep timer.` tooltip).
- Toggle `Keep Awake when Trayloom opens` (icon play.circle) -> `keepAwakeAutoStart` (`Starts a session with the default duration.`).
- **Automation** disclosure (icon bolt.fill, title `Automation`, trailing summary badges: `Off` (9.5 tertiary) or 17 px accent@12 % circles with icons display / powerplug / app / lock for each active condition; chevron 8.5): expanded (indent 22) = 3 condition tiles (compact: min height 65, padding 7, radius 9, icon 14; selected = accent@11 % fill + 32 % accent border + `checkmark.circle.fill`; unselected `primary 4.5 %` + `primary 8 %` border): `External display`, `Power`, `Applications` (toggle `keepAwakeExternalDisplay`, `keepAwakeConnectedToPower`, `keepAwakeRunningApps`); when >=2 selected a segmented `Any` | `All` (`keepAwakeAutomationRequireAll`); when Applications selected an app-bundle list (`Selected apps`, `Add an app…`, `Remove`, caption `Keep Awake starts while any of these apps is open, even in the background.`) stored as `keepAwakeRunningAppBundleIDs` (Windows: executable names / AppUserModelIDs); toggle `Pause while the Mac is locked` (`lock.fill`; `keepAwakePauseWhenLocked`; caption `Follows normal sleep rules while locked and resumes the remaining session after you unlock.`).
- Toggle `Move pointer slightly` (icon cursorarrow.motionlines) -> `keepAwakeMouseJiggleEnabled`; when on: row `Interval` + menu (`1 min`,`2 min`,`5 min`,`10 min`,`15 min`; `keepAwakeMouseJiggleInterval` default 5; small); caption/permission error in red when Accessibility missing (Windows: no permission needed).
**Settings**: `defaultDuration` (int minutes, 0, one of 0/15/30/60/120/240/480), `keepAwakeAutoStart` false, `keepAwakeAllowDisplaySleep` false, `keepAwakeExternalDisplay`/`ConnectedToPower`/`RunningApps` false, `keepAwakeRunningAppBundleIDs` [], `keepAwakeAutomationRequireAll` false, `keepAwakePauseWhenLocked` false, `keepAwakeUntilTime` (seconds since 2001 ref date; 0 -> next hour default), `keepAwakeMouseJiggleEnabled` false, `keepAwakeMouseJiggleInterval` 5, `keepAwakeIconTint` orange, `keepAwakeActiveIcon` vorssaint, `keepAwakeRightClickToggle` false, `keepAwakeShortcut` (default Ctrl+Opt+Cmd+K -> Windows Ctrl+Alt+Win+K... choose a free combo), `clamshellPreferred` false, battery protection (disable below N %, default never; `batteryDisableBelow`), notifications: `Session ended` / `Time is up. The Mac will sleep normally again.`; `Trayloom disabled` / `Low battery. Normal sleep was restored to protect the charge.`
**Behaviour**: session = manual (chips/switch) or automation; ends at end date -> notification; battery protection ends a session immediately if battery below limit; automation sessions replace the chips' highlight; lid-close toggle needs elevated rule (mac sudoers) -> see mapping.
**Windows mapping**: `SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED [| ES_DISPLAY_REQUIRED unless "Allow the display to sleep"])` (or `PowerCreateRequest/PowerSetRequest`, which shows in `powercfg /requests`). Lid closed: `powercfg /setacvalueindex SCHEME_CURRENT SUB_BUTTONS LIDACTION 0` (+ DC) and restore the original values on stop/crash/quit (store originals); no UAC needed for the current user's active plan in most cases, else prompt once. External display: `EnumDisplayMonitors` / `WM_DISPLAYCHANGE`; power: `SystemEvents.PowerModeChanged` / `GetSystemPowerStatus`; running apps: `Process` watch (WMI `Win32_ProcessStartTrace`); locked: `SystemEvents.SessionSwitch`; jiggle: `SendInput` 1 px move relative then back every N min (no Accessibility needed). "Mac awake" wording -> `PC awake`.
**Gap**: `KeepAwakeFeature.cs` exposes only Actions (toggle/15m/1h/2h/forever) + a settings view; no chips row, no Until picker, no countdown, no automation, no jiggle, no lid, no icon/tint, no tray state.

### 5.2 DISPLAYS (`BrightnessSection`) - tab 2 (shown only when `brightnessControlEnabled`)
Title `DISPLAYS`. Card (spacing 10): per display row: `[icon laptopcomputer|display 10.5 semibold, 16 wide][name 11.5 medium, middle-trunc][spacer][NN% 10.5 semibold mono secondary | `Off`][power button]`, below `Slider 0...1 (small)` (disabled while a change is pending), below optional `Dim the picture` / `Extra dimming` button (`circle.lefthalf.filled`/`checkmark.circle.fill`, 10 medium; accent when chosen). Empty: `No display found.` (10.5 secondary). Failure line red 10.5: `Display switching is unavailable on this Mac.` / `At least one display must stay on.` / `Could not change this display.` / `Open the lid to turn on the built-in display.`
Divider + keyboard light row (`Keyboard light` + % + slider) only when a keyboard backlight exists. Divider + `Show brightness when adjusting` mini switch (tooltip `Shows the brightness percentage when you use the brightness keys or sliders.`) when OSD is supported. Divider + `Extra brightness` switch (XDR only). Divider + **Options** disclosure: checkbox `Brightness keys follow the pointer` (caption `The keyboard brightness keys change the display under the pointer.`; permission link), `Use display brightness shortcuts` (+ `Decrease display brightness` / `Increase display brightness` shortcut rows).
Power button: `power` (secondary) / `power.circle.fill` (green) 10.5 semibold, 16 x 18; tooltips `Turn off display`/`Turn on display`; spinner while pending.
**Settings**: `brightnessControlEnabled` false (tab visibility), `brightnessKeysEnabled` false, `brightnessOSDEnabled` false, `brightnessKeyStep` standard (`Standard`, `Half steps`, `Quarter steps`), `displayBrightnessShortcutsEnabled`, `extraBrightnessEnabled`.
**Windows mapping**: built-in: WMI `root\wmi` `WmiMonitorBrightness` / `WmiMonitorBrightnessMethods.WmiSetBrightness`; external: DDC/CI via `dxva2.dll` `GetMonitorBrightness`/`SetMonitorBrightness` (physical monitor handles); "Dim the picture" = software dimming (gamma ramp `SetDeviceGammaRamp` or a black layered overlay at N % alpha) for displays without DDC; turn display off per monitor: `SendMessage(HWND_BROADCAST, WM_SYSCOMMAND, SC_MONITORPOWER, 2)` (all) - per-monitor off only via a black overlay/`SetDisplayConfig`; keyboard light/extra brightness/XDR: omit (OEM-specific). Brightness keys: low-level keyboard hook on the OEM keys is unreliable (handled by firmware) - omit keys option.
**Gap**: `DisplaysFeature.cs` + `DisplayDevices.cs` exist with its own flyout/settings; no panel card.

### 5.3 VOLUME MIXER (`MixerSection`) - tab 3
Title `VOLUME MIXER`. Card (non-interactive, spacing 8):
1. **Audio devices** (spacing 6): 
   - Row `[speaker.wave.2.fill 10.5, 16][`Output` 11.5 medium secondary][spacer][Picker menu 164 wide, small]`; picker items = output devices, current suffixed ` (current)`, an `Output unavailable` item when the stored one vanished; disabled when no devices. Under it (when the system volume is readable) a row `[speaker icon (speaker.slash.fill when muted or <=0.1 %)][slider 0..100 %][editable `NN%` 36 wide 10.5 medium mono secondary]`. Empty: `No outputs found` (9.5 secondary, speaker.slash icon). Switch error: `Could not switch: %@` (triangle).
   - `System sounds` row (bell.fill; picker 164; tooltip `Choose where alerts and sound effects play`).
   - `Microphone` row (mic.fill; picker `Default` + inputs; `Microphone unavailable` / fallback message `Using default until this microphone returns.`; input volume slider + editable percent, disabled while mic muted; empty `No microphones found`).
2. Divider (when apps or permission hint).
3. **Rows** (one per audio-session app): see below. Empty: centred 11 secondary `Apps that use audio show up here`; unsupported: `Available on macOS 14.4 and later`; (mac) permission: `To adjust per-app volume, allow "Screen & System Audio Recording" in System Settings. Audio is never recorded.` + `Open System Settings…`.
4. Divider + **Options** disclosure (see below).
**App row** (`MixerRow`; HStack spacing 10, vertical padding 2): icon 32 x 32 (app icon; 8 px green dot with 1.2 border bottom-right when playing; tooltip `Hold Command and drag to reorder` -> `Hold Ctrl and drag to reorder`) | VStack spacing 5: line 1 = name 11.5 medium (middle-trunc) [+ `pin.fill` 9 secondary if pinned] [spacer] [ellipsis menu `Actions`: `Pin to Top`/`Unpin`, call-app `Allow volume control`] [output picker 112 wide: `Default`, devices, `Output unavailable`, `Choose AirPlay speaker…`]; line 2 = slider (0...200 %, native; fill accent, **amber `#FF9E33` when >100 %**) + editable percent (42 wide, 10.5 medium mono; amber with `bolt.fill` 8 bold when boosting) + reset button (`arrow.counterclockwise` 9.5 semibold, 14 wide; hidden (opacity 0) at exactly 100 %; amber when boosting; tooltip `Reset to 100%`) + mute speaker button (`speaker.wave.2.fill`/`speaker.slash.fill` 10; red when <=0.1 %). Bypassed apps (`This app manages its own audio.`) show text instead of controls. Output fallback warning `Using default until this device returns.` Context menu: arrangement actions + `Hide from the list`.
**Percent field**: click the number -> becomes a text field (accent 0.7 border, radius 4, 18 high, right-aligned, `%` suffix), selects all, Return commits, Esc cancels, blur commits; accepts optional `%` and locale decimal comma; value clamped to max (200 app rows, 100 system/mic); invalid -> beep + cancel.
**Boost**: >100 % amplifies (up to 200 %); 100 % = passthrough; amber state tied to rounded displayed percent.
**Arrangement**: `mixerAppArrangement` JSON `{order:[id], pinned:[id]}`; pinned apps first; drag reorder only within pinned/unpinned group (Command-drag on mac); accessibility `Move Up`/`Move Down`.
**Options** (indent 19): `Hide inactive apps` mini switch (`mixerHideInactiveApps`, shows only playing / non-100 % / routed apps); checkbox `Lower volume when headphones disconnect` + caption `Adjusts output when wired or Bluetooth headphones disconnect.` + stepper `Volume after disconnect` `NN%` step 5; checkbox `Use finer volume steps` + caption `Turns volume wheels and keys into smaller system volume steps.` (+ tap-failure `Could not listen for volume keys.`); **Sound output switcher** (`Switch outputs with shortcut`, caption `Choose outputs and use the shortcut to move to the next available one.`, shortcut row, list `Outputs in cycle` with checkboxes); **Audio priority** disclosure (`Audio priority`, icon `list.number`): caption `Devices are selected in priority order. When a higher-priority device connects, it becomes active. When the active one disconnects, the next available takes over.`; two sub-lists `Output priority` / `Microphone priority` each with a mini switch (tooltips `Automatically switch to the highest-priority output` / `...microphone`), rows `[N 10.5 medium rounded][name 10.5][Current (accent 9)/Unavailable (orange 9)][drag handle]` radius 7 (`primary 3.5 %`; current = accent@8 % + accent border); `Apps in the list` row `All` / `Hidden: N` + chevron -> checkbox per app (10.5), disabled when it cannot be hidden.
**Audio priority only** (mixer not installed): `AudioPrioritySection` = section titled `AUDIO PRIORITY` with that disclosure expanded and header hidden.
**Settings keys**: `mixerShowFinder` true, `mixerHideInactiveApps` false, `mixerAppArrangement` "", `mixerLowerVolumeOnHeadphonesDisconnect` false, `mixerHeadphonesDisconnectVolumePercent` (default constant; min constant), `mixerHeadphonesDisconnectSilenceAllowed`, `preciseVolumeRollerEnabled`, `soundOutputSwitcherEnabled`, `soundOutputSwitcherShortcut`, audio-priority lists/enables.
**Windows mapping**: sessions = `IAudioSessionManager2.GetSessionEnumerator` per active render endpoint (`ISimpleAudioVolume` per session, `IAudioSessionControl2` for pid/icon/name, `IAudioMeterInformation` peak > 0 = playing, `IAudioSessionNotification` for new sessions). System volume/mute = `IAudioEndpointVolume`. Output/mic device lists = `IMMDeviceEnumerator` (`DEVICE_STATE_ACTIVE`), default device change = undocumented `IPolicyConfig::SetDefaultEndpoint` (roles eConsole+eMultimedia[+eCommunications]) (same approach as SoundSwitch/EarTrumpet). Per-app output routing = undocumented `IAudioPolicyConfig` (`SetPersistedDefaultAudioEndpoint`) - works Win10 1803+/Win11. **Boost beyond 100 %: impossible with WASAPI session volume (capped at 1.0)**; closest: (a) cap the slider at 100 % and drop the amber/bolt states, or (b) implement per-process loopback capture (`ActivateAudioInterfaceAsync` + `AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK`, Win10 2004+/Win11) -> gain -> re-render to the device while muting the original (adds latency, breaks exclusive/DRM streams) - treat as an optional phase. Mute button = `ISimpleAudioVolume.SetMute`. "System sounds" device: Windows has no separate alert device; implement as `eCommunications` default device labelled `Calls` or omit. Headphone disconnect guard: `IMMNotificationClient.OnDeviceStateChanged`/`OnDefaultDeviceChanged`. Finer steps: low-level hook on `VK_VOLUME_UP/DOWN` + `IAudioEndpointVolume.VolumeStepUp` replaced by 1 % steps. Drag modifier: **Ctrl** instead of Command.
**Gap**: `MixerWindow.cs` is a separate flyout window (582 lines) with device picker + per-app slider rows + `%` text; missing: panel integration, system-sounds/mic pickers, input volume, editable percent field, boost, pin/ordering, hide list, per-app output, options (headphone guard, finer steps, output switcher, priority lists).

### 5.4 SYSTEM (`SystemSection`) - tab 4
Title `SYSTEM`; edit button (blocks reorderable/hideable). One card (`panelCard`), blocks separated by `Divider`, VStack spacing 10. Default block order (`panelSystemOrder`): `temps`, `usage`, `memory`, (`alerts` is filtered out of the panel), `uptime`, `connectedDevices`. A block is hidden when its key is false or its hub feature is off.
**temps** (`monitorSysTemps` true): subsection label `Temperatures` (10.5 semibold secondary); HStack spacing 8 of **temperature cells** (one per available family: CPU `cpu` icon 9 + `CPU`; GPU `memorychip` icon + `GPU`): VStack spacing 3 [icon 9 + label 10 secondary][value 15 semibold rounded mono `42 °C` (`%.0f °C`/`%.0f °F`, `-` if nil)], padding v6, radius 7, `tile.fill`. (The PNG's 3rd `Battery` cell is no longer in the code.) If both nil: `Sensors unavailable on this Mac` (10 tertiary).
**usage** (visible if CPU or GPU row on): label `Hardware usage` + right Activity Monitor button (`arrow.up.forward.app` 10 medium secondary in an 18 px circle, hover `primary 10 %`, tooltip `Open Activity Monitor` -> `Open Task Manager`). Rows:
- `usageRow`: `[chevron.right 8 semibold secondary (rotates 90 when expanded)][label 11 secondary 52 wide][UsageBar (5 high, capsule)][NN% 11 medium mono, 38 wide, right]`. Bar colour: accent < 60 %, yellow < 85 %, red >= 85 %. Click toggles that row's per-app breakdown.
- CPU history sparkline (`monitorGraphCPU`, accent), GPU (`monitorGraphGPU`, cyan), height 30, ceiling label `100%` (when `monitorGraphScale`), shown when >= 2 samples; history = 120 samples.
- Expanded CPU (`monitorSysCPUCores` true): **CPUCoreMatrix** then a disclosure row `Apps using CPU` (chevron + 10.5 semibold label) that folds the apps list; without cores CPU expands straight to the apps list.
- Expanded breakdown (CPU/GPU/Memory): up to **15** `ProcessUsageRow`s, indent 38, icon 14; value `NN.N%` (memory: byte string); `Measuring…` while loading; refresh every `monitorInterval` s (memory/network 4 s). Row click activates the app; context menu `Force Quit` (when Kill Process installed; protected processes disabled).
**CPUCoreMatrix**: container padding 8, radius 5, fill `primary 2.5 %`, 1 px border `primary 12 %` (0.75/1.5 in increased contrast); groups by core class (names `Super`, `Performance`, `Efficiency`, else raw; caption `Performance ×6` `.subheadline` secondary 22 high, 2 lines); each core = vertical bar 60 high, width 14 x group weight (super 1.5 / performance 1.25 / other 1) scaled to fit, bars 4 apart, groups 12 apart; fill `primary 78 %` height = utilisation, bg `primary 3.5 %`, radius 2, 0.75 border `primary 25 %` (dashed + `–` when unknown); rows 88 high incl. caption, 12 apart; total height = rows x 88 + gaps + 16; animation ease-out 0.2 s (off if reduce motion). Accessibility `Core N` + `%`. Strings: `Logical CPU cores`, `Core %d`, `Each bar is one logical core. Its fill height shows utilization, not remaining performance.`, `Apps using CPU`, `Per core`.
**memory** (`monitorSysMemory`): label `Memory`; row `[chevron][Pressure 11 secondary][PressureIndicator pill][spacer][`used / total` 11 medium mono secondary]` (e.g. `11.43 GB / 16 GB`, binary `ByteCountFormatter.memory`), click expands top-15 memory apps; secondary rows indent 16 (10.5 secondary label / 11 medium value) `Compressed`, `Cached files`, `Swap used`; sparkline (`monitorGraphMemory`, mint, history of used or app per `monitorMemoryMetric`) 30 high.
**PressureIndicator**: capsule h8/v3 fill colour@13 %; 7 px dot (colour, shadow colour@60 % r2) + label 11 medium in colour: `Normal` green / `Caution` yellow / `Critical` red / `-` secondary.
**uptime** (`monitorSysUptime`): `clock` 9 + `Up for 1h 11min` (10.5 medium secondary); format `Nd Nh` / `Nh Nmin` / `Nmin` (wall-clock uptime).
**connectedDevices** (`monitorSysConnectedDevices`, hub feature): row `[cable.connector 11? + `Connected Devices` 11 medium][spacer][count 11 medium mono secondary][chevron 8]` -> opens metric detail (3.7).
**Settings**: `monitorShowSystem`, `monitorSysTemps/CPU/CPUCores/GPU/Memory/Alerts/Uptime/ConnectedDevices` (all true), `monitorGraphCPU/GPU/Memory` true, `monitorGraphScale` true, `panelSystemOrder`, `monitorInterval`, `temperatureUnit`, `monitorMemoryMetric`.
**Windows mapping**: CPU % = PDH `\Processor Information(_Total)\% Processor Utility` (matches Task Manager; fallback `GetSystemTimes`); per-core `\Processor Information(0,N)\% Processor Utility`; core classes from `GetSystemCpuSetInformation` `EfficiencyClass` (higher = `Performance`, lower = `Efficiency`; single class -> `CPU`); GPU % = PDH `\GPU Engine(*)\Utilization Percentage` summed per adapter (max over engines, usually `engtype_3D`); memory used = `GlobalMemoryStatusEx`/`GetPerformanceInfo`; `Pressure`: derived (Normal >= 20 % available, Caution 10-20 %, Critical < 10 %, tuned); `Compressed` = "Memory Compression" process working set; `Cached files` = standby list (`SystemMemoryListInformation` via `NtQuerySystemInformation`); `Swap used` = `\Paging File(_Total)\% Usage` x pagefile size; temperatures: **no supported Windows API** (ACPI thermal zone `MSAcpi_ThermalZoneTemperature` is coarse/absent) -> show `-` + `Sensors unavailable on this PC`, optionally LibreHardwareMonitorLib (needs admin driver) for CPU package/GPU temperature, NVML/ADL for GPU; per-app breakdown: CPU via `GetProcessTimes` deltas / `\Process(*)\% Processor Time`, GPU via `\GPU Engine(pid_*)`, memory via private working set; `Force Quit` = `TerminateProcess` after confirmation; uptime = `NtQuerySystemInformation(SystemTimeOfDayInformation).BootTime` (not `GetTickCount64`, which includes Fast Startup/hibernate drift); `Open Task Manager` = `taskmgr.exe`.
**Gap**: `MonitorFlyout.cs` is one flyout of cards (CPU %, per-core bars, GPU, memory, network, disk, power) with 60-sample sparklines and its own formats (`MonitorFormat.Bytes` `0.#`), no tab, no edit/reorder, no temperature grid, no pressure pill, no breakdown rows, no uptime/devices, history 60 vs 120, no ceiling label, no core classes.

### 5.5 NETWORK (`NetworkSection`) - tab 5
Title `NETWORK`; edit. One card, blocks (default order = `speed, apps, totals, addresses, test`; `panelNetworkOrder`), dividers between.
- **speed** (`monitorNetSpeed`): HStack spacing 10: rate column (`arrow.down` 12 semibold accent, value 13.5 semibold rounded mono `43 KB/s` / bits `12 Mbps` / `Measuring…`, label `Download` 10 secondary) | `Divider` 28 high | rate column (`arrow.up`, green, `Upload`) | **unit toggle** pill (`B/s` or `bit/s`, 10.5 semibold rounded secondary, h7, height 22, radius 7, `primary 10 %`; tooltip `Network speed unit · B/s → bit/s`). Below: graph (`monitorGraphNetwork`) = ZStack of two sparklines on a shared auto scale: download accent (fill 16 %), upload green (fill 8 %), height 30, ceiling label = rounded ceiling in the chosen unit.
- **apps** (`monitorNetApps`): heading `Apps using network` (10 medium tertiary); up to **6** rows (`↓12K ↑3.4K`); `Measuring…` / `No apps using network now`. Starts per-app network monitoring only while visible.
- **totals** (`monitorNetTotals`): `This session` (10 tertiary) ... `↓454 MB  ↑117 MB` (10.5 medium mono secondary), session totals since app start/boot.
- **addresses** (`monitorNetAddresses`): `Local IPv4` (10 tertiary) + right-aligned selectable 10.5 medium mono secondary lines (one per interface; nothing leaves the machine).
- **test** (`monitorNetTest`): button `Speed test` (first) / `Test again` (`gauge.with.dots.needle.67percent` + 11; bordered small) or spinner + `Testing…`; right `↓691 ↑472 Mbps` 11 semibold mono (>=100 -> 0 decimals else 1); below `Latency: 48 ms` 10 tertiary or orange `Test failed`. Test = latency x5 (best) then 5 s download then 5 s upload against Cloudflare `https://speed.cloudflare.com/__down?bytes=0|90000000` / `__up` (zeros body).
**Windows mapping**: bytes/s from `GetIfTable2`/`MIB_IF_ROW2` (sum of non-loopback/non-tunnel physical interfaces; exclude `Loopback`, `Teredo`, `ISATAP`, vEthernet/Hyper-V and VPN adapters per `includeNetworkInterface` rules) or PDH `\Network Interface(*)\Bytes Received/sec`; 64-bit counters for totals; per-app traffic: ETW `Microsoft-Windows-Kernel-Network` (needs admin) - without elevation show `Run as administrator to see apps using the network` instead of the list (or hide the block); local IPv4: `NetworkInterface.GetAllNetworkInterfaces` (Up, non-loopback). Speed test: `HttpClient` (same endpoints).
**Gap**: Network is a card in `MonitorFlyout` (rate, graph); no unit toggle, no per-app list, totals, addresses or speed test.

### 5.6 DISKS (`DiskSection`) - tab 6
Title `DISKS`; edit. One card (spacing 10): empty `No mounted disks found.` (10.5 tertiary). Otherwise **disk selector** (label `Select disk` 10 medium tertiary + adaptive grid min 96/max 138, spacing 6): a button per volume (`internaldrive`/`externaldrive` 10 + name 10.5 semibold + `NN% used` 9 secondary; selected accent@18 % else `primary 6 %`; radius 8; external drives get an eject button 18 px circle: spinner while ejecting, green check when ready, red eject on failure; tooltips `Eject`, `Ejecting…`, `Ready to remove`, `Could not eject`). Then dividered blocks for the selected disk (default order `usage, activity, smart, protection, tools`; `panelDiskOrder`; each has `monitorDisk*` key):
- **usage**: header `Disk usage` (10 medium tertiary); title row `[icon 10][name 11.5 semibold][spacer][tag: file system (APFS/NTFS...)][tag: Internal|External]` (tags 9 medium secondary capsules `primary 7 %`); `DiskUsageBar` (6 high; accent < 75 %, yellow < 90 %, red; animates 0.4 s); `NN% used` ... `123 GB available` (10.5 medium mono secondary); `used / total` and optional `NN GB purgeable` (>= 500 MB) (10 mono tertiary). Sizes use decimal 1000 units (`MetricFormat.diskBytes`).
- **activity**: header `Live activity`; two rate columns `Read` (arrow.down accent) / `Write` (arrow.up pink) 13.5 semibold; graph (30 high; read accent, write pink 8 % fill; ceiling in B/s) when `monitorGraphDisk`; `This session ↓ read  ↑ written`.
- **smart**: header `SMART`; title row (tags if usage hidden); rows (label 10.5 secondary / value 10.5 semibold mono primary): `Status`, `Total written`, `Total read`, `Temperature`, `Health`, `Power cycles`, `Power on hours`; unavailable: `SMART unavailable for this disk`.
- **protection**: header `External protection`; two bordered small buttons `Eject` / `Eject all` (`eject.fill` 10.5); caption (9.5, 2 lines) `Eject before unplugging.` / `No external disk ready to eject.` / state strings (green `Ready to remove`, red `Could not eject`).
- **tools**: header `Tools`; name + `Open` (folder) button (opens the volume in Finder/File Explorer); `Storage` button (settings gear) -> storage settings (Windows: `ms-settings:storagesense`).
**Disk exclusions** (Settings > Quick tools, `DiskExclusionsList`, applies to `Eject all`): disclosure `Excluded drives` + count; rows `externaldrive` + name + `minus.circle.fill` remove; `Add drive…` menu of mounted candidates (+ `Other drive name…` -> text field placeholder `Drive or volume name` + `Add drive…` + Cancel); caption `Drives in this list are never unmounted when using Eject all disks.`; stored `diskEjectExcludedVolumes` (names, UUID or mount path; matched case-insensitively).
**Windows mapping**: volumes = `DriveInfo`/`GetLogicalDriveStrings` + `IOCTL_STORAGE_QUERY_PROPERTY` (internal vs removable via `BusType`/`MediaType`); usage = `GetDiskFreeSpaceEx`; R/W throughput = PDH `\PhysicalDisk(*)\Disk Read Bytes/sec` `Disk Write Bytes/sec` (map instance -> drive letter); totals since boot/session from `IOCTL_DISK_PERFORMANCE`; SMART: `MSStorageDriver_FailureStatus` (WMI `root\wmi`) / `Get-StorageReliabilityCounter` (Temperature, Wear, PowerOnHours, ReadErrors, StartStopCycleCount) / NVMe health log via `IOCTL_STORAGE_QUERY_PROPERTY` (needs admin for some); eject = `CM_Request_Device_Eject` / `DeviceIoControl(IOCTL_STORAGE_EJECT_MEDIA)` after lock+dismount (+ `SHChangeNotify`); "Purgeable" not applicable (omit); "Open" = `explorer.exe <drive>`.
**Gap**: Disk is a card in `MonitorFlyout`; no selector, SMART, eject, exclusions.

### 5.7 POWER (`PowerSection`) - tab 7
Title `POWER`; edit. One card, blocks default order `charge, temperature, system, adapter, battery, remaining, health, peripherals` (`panelPowerOrder`); empty card text `Power metrics unavailable on this Mac` when nothing available. Battery-only blocks (`charge, temperature, battery, remaining, health`) need an internal battery.
- **charge** (`monitorSysBattery`): row `[battery/bolt symbol 9, 10 wide][Battery 11 secondary, 52 wide][UsageBar tinted: red < 20 %, yellow < 40 %, green][NN% 11 medium mono, 38]`; bolt symbol when charging OR external power; sparkline (`monitorGraphBattery`, green, 0-100 %, 30 high, ceiling `100%`); then **EnergyAppsBreakdown** disclosure `Apps using significant energy` (chevron 8 + 10.5 secondary; expands to up to 15 process rows with `NN.N%`, indent 16; `Measuring…` / `No significant energy use`).
- **temperature** (`monitorPwrTemperature`): row `Battery temperature` `31 °C`.
- **system** (`monitorPwrSystem`): row `[bolt.fill orange 11][System 11 (primary 74 %)][value 13 semibold rounded `6.0 W` (`%.1f W` < 10 else `%.0f W`)]` + sparkline (`monitorGraphPower`, orange, auto ceiling in 1 W steps, 30 high, ceiling label `10 W`).
- **adapter** (`monitorPwrAdapter`; only on AC with known watts): `[powerplug.fill accent][Adapter][`30 W max` caption 9.5 secondary][value `7.7 W`]` (`%@ max` rated, else `Plugged in`).
- **battery** (`monitorPwrBattery`): `[battery.100.bolt green | battery.50 secondary][Battery][caption Charging|On battery][value |watts|]`.
- **remaining** (`monitorPwrTimeRemaining`; on battery & not charging): `[clock green][Battery time remaining][caption `System estimate` | `Calculating…`][value `1h 25m` | `...`]`.
- **health** (`monitorPwrHealth`): `[heart.fill pink][Battery health][caption `54 cycles` (`%d Cycles`)... e.g. `54 Cycles`][value `100%`]`.
- **peripherals** (`monitorPwrPeripherals`, default **off**): label `Peripheral battery`; up to 5 rows `[icon (keyboard/computermouse/trackpad/headphones/battery)][name 10.5 medium][spacer][NN% 10.5 semibold]` + `+N` overflow (10 tertiary).
All row values update with `numericText` transition. 
**Windows mapping**: `GetSystemPowerStatus` (percent, AC, charging, `BatteryLifeTime`) + WMI `root\wmi` `BatteryStatus`/`BatteryFullChargedCapacity`/`BatteryStaticData` (health = full/design capacity, cycles via `BatteryCycleCount` when exposed), watts via `BatteryStatus.ChargeRate/DischargeRate` (mW) -> system power = discharge rate when on battery (no direct system-power sensor; show only when derivable, else omit), adapter watts: not exposed -> show `Plugged in`; peripheral battery: `Windows.Devices.Bluetooth` GATT Battery service / `DEVPKEY_Device_BatteryLevel` (HID/BT); `Apps using significant energy`: CPU-weighted estimate (`\Process(*)\% Processor Time` + GPU) or SRUM energy (`C:\Windows\System32\sru\SRUDB.dat` is locked; skip) -> label unchanged. Desktop PCs without a battery: only `system`/`adapter` omitted -> card shows the unavailable note.
**Gap**: Power card exists inside `MonitorFlyout` partially (battery % + status); no energy apps, health, time-remaining, adapter, peripherals, graph.

### 5.8 UTILITIES (`UtilitiesSection`) - tab 9
Title `UTILITIES`; edit button (hidden while a hosted tool shows). Body = **row-group card** (no card padding) of `UtilityActionButton`s, one per installed utility feature not hidden, default order = enum order (persisted `panelUtilityOrder`; reset removes the key): 
`screenshot, quickLauncher, appUpdates, cleaner, homebrew, media, clipboard, windowLayout, uninstaller, cleanURL, cleaning, screenOCR, colorPicker, cameraPreview, scratchpad, commandBar, screenRecorder, portManager` (each shown only if its hub feature is installed AND its `panelUtility*` key is true, default all true).
Row = icon (accent) + title (tooltip = description) + [shortcut chip] + chevron. Exact content:
| # | Title (row) | Tooltip/caption (verbatim) | SF symbol -> Segoe | Chip | Action / what opens |
|---|---|---|---|---|---|
| 1 | `Screenshot` | `Capture an area, window or the whole screen` (permission: `Permission required: Screen Recording` orange + mini button `Grant access`) | camera.viewfinder -> E722 | `screenshotShortcut` (^⇧⌘4 style) | close panel, 0.2 s later start capture; accessory mini button `Recent captures` (clock.arrow.circlepath) opens hosted **Recent captures** |
| 2 | `Quick panel` | `A floating panel with your favorite tools, summoned by a shortcut from anywhere.` | square.grid.2x2 | `^⌘V` default | close panel, 0.15 s later show Quick Launcher |
| 3 | `App updates` | `See which apps have a newer version` | arrow.down.app -> E896 | | hosted **App updates** panel |
| 4 | `Cleaner` | `App leftovers, caches and logs` | sparkle | | hosted **Cleaner** (`CleanerView(compact)`) |
| 5 | `Homebrew` -> (Windows) `Package manager`/`winget` | `Search, install and remove formulae and casks.` -> `Search, install and remove apps with winget.` | shippingbox | | hosted **Homebrew** panel (see 6.4) |
| 6 | `Media` | `Compress videos, convert and process images, make GIFs and extract text locally.` | photo.on.rectangle.angled | | hosted `MediaWorkspaceView(compact)` |
| 7 | `Clipboard` | enabled: `Stores copied text so you can reuse it later. Everything stays local and can be cleared anytime.`; disabled: `Enable history to start saving copied text.` (caption stays visible when disabled) | doc.on.clipboard | `^⌥⌘V` default (only if history enabled + shortcut toggle) | hosted **Clipboard** panel |
| 8 | `Window layout` | `Arrange windows into screen sections or move and resize them with a trackpad or mouse.` (panel shows short caption) | rectangle.3.group | | hosted **Window layout** panel |
| 9 | `Uninstaller` | `Removes an app together with the caches, preferences, logs and leftovers it leaves behind.` | trash | | hosted **Uninstaller** panel |
| 10 | `Clean URL` | `Removes tracking parameters from a link the moment it reaches the clipboard.` | link | | hosted **URL cleaner** panel |
| 11 | `Cleaning Mode` | `Locks the keyboard so you can clean safely.` (permission variant `Permission required: Accessibility`) | keyboard | | close panel, start cleaning mode |
| 12 | `Copy text from screen` | `Select an area of the screen and the recognized text is copied, ready to paste.` (permission Screen Recording) | text.viewfinder | OCR shortcut | close panel, 0.2 s later OCR capture |
| 13 | `Color picker` | `Grab the color of any pixel on screen and copy it in your favorite format.` | eyedropper | shortcut | close panel, 0.15 s later pick |
| 14 | `Camera preview` | `Check how you look before a call` (denied: `Permission required: <permName>` + `Open System Settings…`) | web.camera | shortcut | close, 0.15 s, open mirror |
| 15 | `Scratchpad` | `Quick notes in separate tabs` | note.text | shortcut | close, 0.15 s, open scratchpad |
| 16 | `Command Bar` | `Type to find and run anything` | command | shortcut | close, 0.15 s, open command bar |
| 17 | `Screen recording` (while recording: `Stop recording`, icon stop.circle, caption = elapsed `0:42` stays visible) | `Record an area, window or the whole screen` | record.circle | shortcut | close, 0.2 s, toggle recording; accessory `Recent captures` (hidden while recording) |
| 18 | `Port Manager` | `Your listening ports` | network | | hosted **Port manager** |
Row height ~ 40-58 (title only = ~38; with caption/permission more). In the older PNGs rows are 2-3 lines because captions were printed; current panel prints title only.
**Hosted-tool mode** (one at a time, replaces the list; the section header edit button disappears): `PanelUninstallerView`, `PanelCleanerView`, `PanelURLCleanerView`, `PanelHomebrewView`, `PanelMediaView`, `PanelClipboardView`, `PanelRecentCapturesView`, `PanelWindowLayoutView`, `PanelAppUpdatesView`, `PanelPortManagerView` - see section 6. While hosted: `viewKeepsPopoverOpen` is true (except Homebrew, which behaves like a normal popover), the footer Settings button opens the tool's own Settings page (uninstaller, cleaner, urlCleaner, homebrew, media, clipboard, screenshot (for recent captures), windowLayout, appUpdates, portManager), and clicks outside the panel do not dismiss it. Leaving the tab/closing resets `hostedSettingsPage`.
**Edit mode**: drag rows, eye toggles (`panelUtility*` keys), reset (`Default`) restores order + all visible.
**Windows mapping**: Windows-specific tools only: Screenshot, Quick panel, App updates (winget/MS Store), Cleaner, Package manager (winget), Media, Clipboard, Window layout, Uninstaller, Clean URL, Cleaning mode, OCR, Color picker, Camera preview, Scratchpad, Command bar, Recorder, Port manager - all exist as features in `Trayloom.Features.*`; only their panel rows are new. Shortcut chip = formatted Windows shortcut (`Ctrl+Alt+V`, `Win+Shift+S`...) via `HotkeyBinding` text. `closePanel; delay` = hide flyout then `Dispatcher` delay 150/200 ms before invoking (existing `InvokeAfterClose`).
**Gap**: `QuickPanelWindow` shows ALL feature actions as 88 px tiles (icon + title + feature name) in a 448 wide wrap panel; no rows, no chips, no hosted tools, no edit/order/hide, no permission rows.

### 5.9 CONTROLS (`QuickControlsSection`) - tab 10
Title `CONTROLS`; edit. Three collapsible **categories**, each a header button (chevron.right 8.5 bold secondary rotating 90 + uppercase `WINDOWS` / `MOUSE AND KEYBOARD` / `FILES` 10 bold secondary tracking .5 + spacer + `enabled/total` count 9.5 semibold rounded mono, accent when > 0 else secondary) over a **row-group card** of `PanelToggleRow`s. Categories start **collapsed** and remember state (`panelControlWindowsExpanded`, `panelControlInputExpanded`, `panelControlFilesExpanded`, default false); edit mode forces them open and disables the header toggle. Titles: `Windows`, `Mouse and keyboard`, `Files` (`panelCategoryWindows/Input/Files`). Item order key `panelControlOrder` (default = enum order below); visibility keys `panelControl*` (default true). Category assignment:
- **Windows**: `switcher`, `dockPreview`, `dockClick`, `dockClickHide`, `dockClickCycle`, `windowMaximize`, `autoQuit`, `notch`, `spacesOrder`
- **Mouse and keyboard**: `mouseScroll`, `linearScroll`, `focusFollowsMouse`, `mouseAcceleration`, `mouseNavigation`, `mouseButtonShortcuts`, `middleClick`, `keyDebounce`, `textSnippets`, `radialMenu`, `superKey`, `mouseClickDebounce`
- **Files**: `cutPaste`, `shelf`
(Enum order is: mouseScroll, linearScroll, focusFollowsMouse, mouseAcceleration, mouseNavigation, switcher, cutPaste, autoQuit, shelf, windowMaximize, dockPreview, keyDebounce, dockClick, dockClickHide, dockClickCycle, middleClick, textSnippets, radialMenu, mouseButtonShortcuts, superKey, mouseClickDebounce, notch, spacesOrder; rows are filtered per category preserving this order.)
Row catalogue (title / tooltip caption verbatim / icon / extra):
| item | title | caption (tooltip) | icon | notes |
|---|---|---|---|---|
| mouseScroll | `Invert mouse scrolling` | `Reverses the mouse wheel direction.` | computermouse | one switch drives vertical+horizontal inversion |
| linearScroll | `Linear scrolling` | `Every notch of the mouse wheel scrolls the same distance, no matter how fast it spins. The trackpad is not affected.` | arrow.up.and.down.text.horizontal | |
| focusFollowsMouse | `Focus follows mouse` | `Focuses the window under the pointer.` | cursorarrow.and.square.on.square.dashed | |
| mouseAcceleration | `Disable mouse acceleration` | `Removes pointer acceleration for connected mice. Your previous setting returns when this is turned off or Trayloom quits.` | cursorarrow.rays | no permission |
| mouseNavigation | `Use side buttons for Back and Forward` | `Turns the mouse Back and Forward buttons into navigation commands in Finder, browsers and compatible apps.` | arrow.left.arrow.right | |
| switcher | `Window switcher` | `Switch between apps and windows, including minimized windows and multiple windows from the same app.` | rectangle.on.rectangle | when on & not editing: sub-row `Show ⌘Tab with large icons` (format with the current shortcut) + mini switch (`switcherIconRowMode`; disabled in simple mode; tooltip `Shows one icon per app with that app's window previews above it.`) |
| dockPreview | `Dock Preview` | `Hover over an open app in the Dock to see its windows, then click the one you want.` | dock.rectangle | (taskbar thumbnails on Windows) |
| dockClick | `Click the Dock icon to minimize` | `The active app's windows minimize when you click its Dock icon. Click again to bring them back.` | dock.arrow.down.rectangle | exclusive with dockClickHide |
| dockClickHide | `Click the Dock icon to hide the app` | | eye.slash | exclusive with dockClick |
| dockClickCycle | `Click the Dock icon to cycle windows` | `Click an active app's Dock icon to rotate through its windows, like ⌘\`.` | dock.rectangle | |
| windowMaximize | `Maximize windows` | `The green button maximizes without creating another Space.` | arrow.up.left.and.arrow.down.right | (mac-only wording) |
| autoQuit | `Quit on close` | `Closing an app's last window also quits it.` | xmark.rectangle | |
| notch | `Dynamic Island` (FeatureStrings.notch.title) | notch description | macbook | accessory `Settings` -> opens island settings page |
| spacesOrder | `Keep Spaces in a fixed order` | `Stops macOS from rearranging Spaces by most recent use, so they stay in the order you set. Your previous setting returns when this is turned off. The Dock may restart once to apply the change.` | rectangle.split.3x1 | |
| middleClick | `Three-finger click acts as middle click` | `Pressing the trackpad with three fingers works like a mouse wheel click...` (dynamic warnings: `Can't read trackpad touches. Connect a supported trackpad.` / drag-conflict text) | cursorarrow.click.2 | |
| keyDebounce | `Debounce` | `Filters very fast duplicate key presses.`; when on: caption becomes `Global window: NN ms` and a stepper sub-row `Global window` `NN ms` (range `allowedKeyboardDebounceWindowRange`, step 1, default `defaultKeyboardDebounceWindowMs`) | keyboard | |
| textSnippets | `Text snippets` | snippets enable caption | text.append | accessory `Manage` when on -> Settings page |
| radialMenu | `Radial menu` | panelCaption | circle.grid.cross | accessory `Manage`; Accessibility only needed for shortcut/media slices |
| mouseButtonShortcuts | `Mouse buttons` | panelCaption | button.programmable | accessory `Manage` |
| superKey | `Super key` | `<source label> <keycaps>` e.g. `Caps Lock ⌃⌥⇧⌘` | caps/globe symbol per source | accessory = mapping label when permitted else `Set up…`/manage |
| mouseClickDebounce | `Mouse click debounce` | caption | cursorarrow.click | |
| cutPaste | `Cut & paste` | `Use ⌘X to cut and ⌘V to move files and folders in Finder.` | scissors | |
| shelf | `Shelf` | `A floating spot to gather files, images and text, then drag them anywhere later.` | tray.full | accessory `Open the shelf (N)` when on and N > 0 |
Attention: any enabled row whose permission is missing flips to an orange caption `Permission required: Accessibility` (and Screen Recording for dock preview / switcher when not in simple mode), orange icon, and a mini `Grant access` button; enabling such a row requests the permission immediately. Toggling a row calls the feature's `syncWithPreferences()`.
**Windows mapping**: keep and implement (features exist in `Trayloom.Features.Input/Windows`): Invert mouse scrolling, Linear scrolling, Focus follows mouse, Mouse acceleration, Side buttons (Back/Forward = `XBUTTON` native; keep row for navigation commands in non-supporting apps), Window switcher, Quit on close (auto-quit), Cut & paste (File Explorer), Shelf, Keyboard debounce, Mouse click debounce, Text snippets, Radial menu, Mouse buttons, Super key (Caps Lock/other remap), Dynamic Island (Trayloom Island). **Omit on Windows** (mac-only concepts): Dock Preview, all three Dock-click rows, Maximize windows (green button), Keep Spaces in order, Three-finger middle click (Precision Touchpad gestures are system-owned; could map to a 3-finger tap gesture via `Windows.Devices.Input` - optional). Dock Preview/dock-click have a rough Windows analogue ("taskbar thumbnails / click taskbar icon to minimize") but are OS-native; do not build. Category counts reflect only the rows that exist.
**Gap**: no Controls tab; per-feature enable toggles live on the Features hub/`FeaturesWindow`.

### 5.10 QUICK TOGGLES (`QuickTogglesSection`) - tab 11
Title `QUICK TOGGLES`; edit. **Row-group card** of `UtilityActionButton`s (one `PanelToggleRow` for keyboard light); default order (`panelToggleOrder`) = `darkMode, keyboardLight, micMute, emptyTrash, quitAllApps, ejectDisks, hiddenFiles, desktopIcons, lockScreen, displayOff, screenSaver`; visibility keys `panelToggle*` (`panelToggleMicMute` = `panelUtilityMicMute`), default true. Hub gate: `quickToggles` (all but micMute/quitAllApps) / `micMute` / `quitApps`; `keyboardLight` only when a keyboard backlight exists. Rows are disabled while their action runs.
| item | title (state dependent) | caption (tooltip; stays visible when state/permission note) | icon |
|---|---|---|---|
| darkMode | `Switch to dark mode` / `Switch to light mode` | `Changes the appearance of the whole system.` | moon.fill / sun.max.fill |
| keyboardLight | `Keyboard light` | `Turns the keyboard backlight on or off.` | keyboard (switch row) |
| micMute | `Mute microphone` / `Unmute microphone` | `Uses each microphone's mute or input-volume controls. Siri and some apps may still hear you. This is not a privacy lock.` | mic / mic.slash.fill; chip = mic shortcut |
| emptyTrash | `Empty the Trash` | `Removes everything from the Trash.` (permission note `Permission required: <Automation Finder>` + `Open System Settings…`) | trash; confirmation `Empty the Trash?` / `All items in the Trash will be removed. This cannot be undone.` / button `Empty the Trash` |
| quitAllApps | `Quit all apps` | `Lists your open apps to quit one by one, all at once, or all but the one in front. Always shows the list before anything quits.` | xmark.app; chip = quitAll shortcut; opens the quit-apps list (`Open apps`, `Quit all`, `Quit all except %@`, `Quit %@`, badges `Front` / `Kept`, empty `No other apps are open.`) |
| ejectDisks | `Eject all disks` | `Safely ejects every external disk.` (states `Ejecting…`, `Could not eject`, `No external disk ready to eject.`) | eject.fill |
| hiddenFiles | `Show hidden files` / `Hide hidden files` | `The Finder restarts to apply it.` (always visible) | eye / eye.slash |
| desktopIcons | `Hide desktop icons` / `Show desktop icons` | `The Finder restarts to apply it.` (always visible) | desktopcomputer |
| lockScreen | `Lock the screen` | `Asks for the password to come back.` | lock.fill |
| displayOff | `Turn off the display` | `The Mac keeps running with the display off.` | display |
| screenSaver | `Start the screen saver` | `Starts right away, on every display.` | sparkles.tv |
Failure caption `Could not complete.`. Actions that take over the screen close the panel first (150 ms delay). While this tab shows, `viewKeepsPopoverOpen = true` (panel stays open for toggle-back). Same list is reused in the Quick panel and in Edit mode.
**Windows mapping**: dark mode = `HKCU\...\Themes\Personalize` `AppsUseLightTheme` + `SystemUsesLightTheme` then `WM_SETTINGCHANGE`; mic mute = `IAudioEndpointVolume.SetMute` on capture endpoints; empty = `SHEmptyRecycleBin` (confirm dialog, no permission); quit all = enumerate top-level windows -> `WM_CLOSE` with kept list (File Explorer, Trayloom always kept); eject = see Disks; hidden files = `HKCU\...\Explorer\Advanced\Hidden` (1/2) + `SHChangeNotify`/`SendMessage(WM_SETTINGCHANGE)` (no Explorer restart needed - drop the `The Finder restarts to apply it.` caption or keep `Explorer may refresh.`); desktop icons = `ToggleDesktop`-style: `HKCU\...\Explorer\Advanced\HideIcons` + refresh (or `SendMessage(progman, WM_COMMAND, 0x7402)`); lock = `LockWorkStation`; display off = `SC_MONITORPOWER`; screen saver = `SendMessage(HWND_BROADCAST, WM_SYSCOMMAND, SC_SCREENSAVE, 0)`; keyboard light: omit (no API). Existing `Trayloom.Features.Tools/QuickToggles` supplies the actions.
**Gap**: actions appear only as tiles in the Quick panel; no tab, no states in titles, no order/visibility edit.

### 5.11 WALLPAPER (`WallpaperSection`) - tab 12
Title `WALLPAPER`. No edit button. VStack spacing 10:
1. Picker (menu style) filter `All` / `Your pictures` / `Apple` (-> `Windows` stills or `Bing`/Spotlight? keep `Apple` label replaced by the system wallpaper set: `Windows`) bound to `service.filter` (persisted).
2. Switch (small) `Show on all Spaces` (`applyAllDisplays`) (-> `Show on all displays`).
3. HStack buttons (bordered small) `Add image`, `Add folder`, and (when own sources exist) `Remove` <-> `Done` toggling remove mode (disables the add buttons).
4. (remove mode) horizontal strip of chips (radius 8, `secondary 15 %`, icon folder|photo|triangle (unreachable), name 10 mid-trunc, red `xmark.circle.fill` 14) for each removable source.
5. **Gallery**: 3 columns x 8 rows = **24 per page** (`pageSize` 24), thumbnails 54 high, spacing 8, rounded 6, applied one has a 2 px accent border; height fixed to the full 3x8 grid when >1 page (so paging never resizes the panel), else only the rows needed (2 rows for empty). Empty strings: `No wallpapers found` / `No pictures added yet` / `No Apple stills found`; spinner while loading; in remove mode own entries show a red `xmark.circle.fill` badge (offset 4,-4).
6. **Pager** (22 high): `chevron.left` `n / m` `chevron.right` (shown when pages > 1) + spacer + link-style `Open Wallpaper settings` (10 secondary).
Status: `Downloading…` (caption) / error (orange) (`Could not download the wallpaper`, `Could not set the wallpaper`). Caption description: `Pick a still wallpaper without opening System Settings.`
**Windows mapping**: apply via `IDesktopWallpaper::SetWallpaper(monitorId|null)` (per-monitor or all) / `SystemParametersInfo(SPI_SETDESKWALLPAPER)`; built-in set = `C:\Windows\Web\Wallpaper\**` and `C:\Windows\Web\4K\Wallpaper`; thumbnails via `IShellItemImageFactory`; `Open Wallpaper settings` -> `ms-settings:personalization-background`. "Show on all Spaces" -> `Show on all displays` (virtual desktops share one wallpaper on Windows).

### 5.12 FAN CONTROL - tab 8: **skipped on Windows** (no supported fan API). Omit the tab, the `fanSpeed` menu-bar metric and `monitorShowFanControlBeta`.

---------------------------------------------------------------------------------------------------------------------

## 6. Hosted utility panels (replace the Utilities list inside the panel)
Shared header pattern: `HStack{ Label(title, icon) 12 semibold ; Spacer ; [gearshape 12 secondary 22x22 (opens the tool's Settings page; tooltip `Settings…` (menuSettings)) - only App updates & Port manager ; [trash 11 semibold 22x22 `Clear history` - Recent captures] ; xmark.circle.fill 14 secondary 22x22 (tooltip = Cancel) }`. Body = `VStack(spacing 10)` of cards. All set `viewKeepsPopoverOpen` while visible (Homebrew excepted) and reset it on disappear.

### 6.1 Clipboard (`PanelClipboardView`)
Header `Clipboard` (doc.on.clipboard) + X. Card "controls" (spacing 7): checkbox `Save clipboard history` (11.5 medium) [`clipboardHistoryEnabled`, default false -> `ClipboardHistoryService.syncWithPreferences()`]; caption 10 secondary: enabled `Stores copied text so you can reuse it later. Everything stays local and can be cleared anytime.` / disabled `Enable history to start saving copied text.`; if enabled and `clipboardHistoryShortcutEnabled` (true): `History shortcut: ^⌥⌘V` (9.5 medium rounded tertiary); row: search field (roundedBorder, 11) placeholder `Search copied text` (disabled when empty history) + trash button (bordered mini 24x22, tooltip `Clear unpinned`, disabled with no recent items; confirmation `Clear unpinned (%d)?` / `Pinned items stay, and so does anything copied after this. This can't be undone.` buttons `Clear unpinned`/`Cancel`) + open-window button (`arrow.up.forward.app`, tooltip `History shortcut`).
List (LazyVStack spacing 7, max height **260**, scroll): each entry a card (spacing 7): optional `Pinned` label (pin.fill 9.5 bold accent); preview by kind: text (3 lines max 10.5, optional 12 px colour swatch if the text is a colour), image (thumb <=110x40 radius 4 + `Image · 1920 × 1080`), files (single image file thumb + name / folder icon + name or `%d files`); search-term highlighting; action row (bordered mini buttons): `arrow.up` (`Move up`), `arrow.down` (`Move down`) (disabled unless unfiltered and movable), pin (`Pin`/`Unpin`), pencil `Edit` (image + screenshot feature), prominent `Copy` -> `Copied` with checkmark (ignores the 2nd click of a double-click; list scrolls to the moved entry), trash `Delete item`, spacer, time (9.5 tertiary). Empty: card 72 high `No saved text`; no match `No results`.
**Windows mapping**: `AddClipboardFormatListener` + `Clipboard`/WinRT `Windows.ApplicationModel.DataTransfer.Clipboard`; existing `ClipboardHistoryFeature/Store/PopupWindow`. Cloud/Win+V history is separate.
### 6.2 Clean URL (`PanelURLCleanerView`)
Header `Clean URL` (link) + X. Card 1: checkbox `Clean URLs as you copy them` (`urlCleanerEnabled` false) + caption `Removes tracking parameters from a link the moment it reaches the clipboard.` + green `Active now` (checkmark.circle.fill 10) when running. Card 2: heading `Clean now` (10 semibold tertiary); `TextField` placeholder `Paste a URL` (roundedBorder 11) + clear button (`xmark.circle.fill` 13, tooltip `Clear field`, dimmed when empty); buttons row (small): `Paste` | spacer | prominent `Copy` (disabled without result); result: monospaced 10.5 up to 3 lines middle-truncated selectable + status 10 secondary (`Copied.`, `URL cleaned.`, `Nothing to clean.`, `Paste a valid URL.`, `Removed utm_source, fbclid`) or placeholder `The clean URL appears here`. Result recomputed on every render from the field + current rules (global / site / disabled parameter lists).
### 6.3 Uninstaller (`PanelUninstallerView`) / Windows installed-apps remover
Header `Uninstaller` (trash) + X (cancels a running scan; a removal in progress is left alone). Phases: **empty** card: dashed drop zone (radius 12, 1.5 dashed [6,5], height 108; hover accent border + accent@8 % fill; icon `arrow.down.app` 28 light) `Drag an app here` / `or choose one to scan`; refusal note (orange triangle + reason); bordered small `Choose…` (`Choose app…`, plus.app) -> inline app picker (`Choose app`, `Search apps`, `No apps found`); note `Nothing is removed without your confirmation.`; Full-disk-access note card when missing (mac only). **scanning**: card 118 high spinner `Scanning files…` + app icon+name + Cancel. **results** card: target header (icon 28, name 12.5 semibold, bundle id/path 9.5; right: size 12 semibold rounded + `found` 9 + X); divider; leftover list grouped by category headings (9 semibold tertiary uppercase: `Application`, `Support`, `Caches`, `Preferences`, `Containers`, `Logs`, `Saved state`, `Other`); row `[checkbox][file icon 17][name 11 + path 9.5 tertiary (head-truncated, orange `questionmark.circle` for "related" optional items)][folder (reveal) button][size 10 mono secondary]` min height 24; divider; Homebrew line (`Uninstall with Homebrew` info/ status) ; footer `N of M selected` + size; buttons `Cancel` | prominent red `Move to Trash` (trash icon; `Uninstall` when the app is a Homebrew cask). **removing**: spinner `Moving to the Trash…`. **done**: check/warning 32, `Done!` (14 bold), `%@ removed`, failed-items note, button `Uninstall another` (prominent small).
**Windows mapping**: list = HKLM/HKCU `Uninstall` keys + MSIX (`PackageManager`); remove = run `UninstallString` / winget uninstall; leftovers scan = `%AppData%`, `%LocalAppData%`, `%ProgramData%`, registry keys under Software\<Vendor>; "Moving to the Trash" -> Recycle Bin via `SHFileOperation`; drag a `.exe/.lnk` onto the panel.
### 6.4 Homebrew -> winget panel (`PanelHomebrewView`)
Header `Homebrew` (shippingbox; Windows `Package manager`) + X (no Settings icon). **Missing** state card: icon 30 light, `Homebrew not found` (13 semibold), body `Vorssaint can open the official Homebrew installer in your terminal. It shows the steps and asks for your password if needed.`, prominent `Install Homebrew` (terminal icon), caption `When the installer finishes, come back here and click Refresh.` / `Installer opened in your terminal.`, error label, `Refresh` bordered. For Windows: if `winget` missing (`App Installer` from Microsoft Store) show the equivalent.
**Normal**: segmented `Search` | `Installed N`; **shell setup** card (mac only: `Finish terminal setup`...) -> omit; search card: search field (`Search packages`) + prominent magnifier button (disabled when empty/busy), segmented `Casks` | `Formulae` (-> Windows `Apps` | `Packages`/`msstore`), hint 9 tertiary `Space or Return closes the macOS panel. Use the search button.` (omit/replace: Enter searches); installed card: segmented `All N | Casks N | Formulae N`; buttons `Check packages` (refresh) and `Update Homebrew` (`arrow.triangle.2.circlepath`), outdated summary: spinner `Updates`, or orange `Updates N` + `Update all` (arrow.up.circle) or `Updates 0` with check. Package list card: header `Results` / `Installed` + count badge (8.5 semibold rounded capsule) + spinner; list height **162**, rows radius 7 (`primary 3.5 %`, selected accent@14 %): `[terminal|app icon 11][name 11.5 (semibold if selected) + version/name 9.5 secondary][popularity badge `12K` accent 9 semibold][status: spinner | `arrow.up.circle.fill` orange (tooltip `Update: 1.2 → 1.3`) | green check | `arrow.down.circle.fill` (Install)]`; context menu: `Package details`, `Update`, `Uninstall` (destructive)/`Install`. Empty/loading strings: `Select an installed package or search for a new one.`, `No results`, `No packages found`, `Loading…`. **Detail card**: name 13 semibold + status badge (`Installed` green / `Not installed` / `Update available` orange / action in progress accent) ; description 10.5 (2 lines); `package name` mono 9.5 + version; popularity (`%@ installs in %@ days`); update line; `Open website` link; buttons Install (prominent) / Update (prominent) + Uninstall (red). **Operation card** (`HomebrewOperationStatusView`, compact): progress (`Preparing…`, `Downloading files…`, `Installing files…`, `Removing files…`, `Updating files…`, `Finishing…`, `Refreshing list…`, `Continue in your terminal.`, `%@ elapsed`, `Show details`/`Hide details`, `Technical details`, `Homebrew has not reported a percentage yet.`) + `Cancel`, `Clear log`, `Open terminal`; result lines `%@ installed.` `%@ uninstalled.` `%@ updated.` `Packages updated.` `Homebrew updated.` `Could not finish %@.` `Operation cancelled.`. Confirmations (native dialog): `Install with Homebrew?` / `Homebrew will download and install %@. Dependencies may also be installed.`; `Uninstall with Homebrew?` / `Homebrew will uninstall %@. Configuration files may remain on the system.`; `Update with Homebrew?` / `Homebrew will download and apply the latest version of %@. Dependencies may also be updated.`; `Update all with Homebrew?` / `Homebrew will download and apply the latest versions for packages with updates available. Dependencies may also be updated.`; `Update Homebrew?` / `Homebrew will fetch the latest information and then reload your packages.` Untrusted-tap card `Tap not trusted yet` / `Homebrew now asks for your confirmation before using third party taps. Trust %@ to continue.` / `Trust and continue` (mac-only; omit).
**Windows mapping**: replace brew with `winget` (CLI: `winget search/list/upgrade --accept-source-agreements`, parse `--output` via JSON through the `Microsoft.WinGet.Client` PowerShell module or `winget export`); `Update Homebrew` -> `winget source update`; elevation prompts happen in their own UAC window (omit "terminal fallback"); the panel does not auto-dismiss during an operation (preventsDismissal).
### 6.5 App updates (`PanelAppUpdatesView` + `AppUpdatesListView(compact: true)`)
Header `App updates` (arrow.down.app) + gear (Settings page) + X; on appear it runs `checkIfNeeded()`. Card: summary row (`Last checked 5 minutes ago` / `Not checked yet`; 10.5 secondary) + bordered small `Check now` (arrow.clockwise) / spinner `Checking`; empty state: `No updates found` (green check) / `No updates found in this partial check` / `No updates outside your rules` + note `Checks the original sources of installed apps and a public catalog. Updates install through their original source.` (+ `Homebrew is not installed, so apps cannot be updated from here yet.`); with items: selection bar link buttons `Select all` / `Clear` (10); list (ScrollView, shows 4 rows: row height 40, spacing 5; whole rows): `[checkbox | spacer 14][icon 22][name 11.5 medium + source badge (`Homebrew`/`App Store`/`Online`, 8.5 semibold accent capsule) / version summary 9.5][bordered small `Update`|`Open`|`Open the App Store`]` radius 8 `primary 3.5 %`, context menu `Skip version %@` / `Don't check this app`; prominent `Update N` button when selection; rules disclosure `Update exceptions` (hint + `+` add button; rule rows 48 high: name + `Skipped version %@`/`Not checked until this rule is removed` + `Remove`); incomplete-check warning (`Check incomplete`, unchecked names, `The online check could not be completed. Other results are still shown.`, `Open the App Store` link); uncomparable-versions note; operation status; error label.
**Windows mapping**: sources = winget (`winget upgrade`) + Microsoft Store (`Windows.Services.Store.StoreContext.GetAppAndOptionalStorePackageUpdatesAsync`) + registry-installed apps checked against winget catalog; badges `winget`, `Microsoft Store`, `Online`.
### 6.6 Port manager (`PanelPortManagerView`)
Header `Port Manager` (network) + gear + X; card: row `Your listening ports` (10 secondary) + `12 open` (9.5 semibold rounded secondary); filter field `Filter by port, process, or PID` + refresh button (`arrow.clockwise`, tooltip `Refresh`); `Could not read listening ports. Try refreshing.` warning card; list (max height **260**): row `[network icon accent 11, 18][ `3000` mono 11.5 semibold + protocol capsule `TCP` 8.5 bold rounded + all-interfaces badge ; process name 10.5 ; `PID 1234  •  127.0.0.1` mono 8.5 tertiary][spacer][`Kill` bordered mini + `bolt.fill` (Force Kill) bordered mini]` (Kill hidden unless Kill Process feature installed; disabled for protected processes or unknown start time), row bg `primary 3 %` radius 6; context menu = copy port / copy `address:port`; empty card 72 high (`network.slash`, `No listening ports found`, `Try refreshing or changing your search.`); loading spinner. Alert `Terminate %@?` / `This closes port %d by terminating PID %d.` + `Kill`/`Force Kill` (destructive) / Cancel.
**Windows mapping**: `GetExtendedTcpTable`/`GetExtendedUdpTable` (`TCP_TABLE_OWNER_PID_LISTENER`) + `Process`; terminate = `Process.Kill` (force) / `CloseMainWindow` (graceful); protected = system processes (PID 0/4, csrss, lsass...).
### 6.7 Recent captures (`PanelRecentCapturesView`)
Header `Recent captures` (clock.arrow.circlepath) + trash (`Clear history`, confirmation dialog `Clear history` destructive/Cancel) + X. List (max height **300**, spacing 7): card rows: preview 104 x 68 (radius 7, `black 13 %`, 1 px border `primary 10 %`, thumbnail fit or photo/film icon) | `Screenshot`/`Recording` 11.5 semibold, recording file name 9.5 secondary, relative time 9.5 tertiary, buttons prominent mini `Restore` (screenshot -> reopen in editor) / `Open` (recording) + bordered mini trash (`Remove from history`). Empty: card 92 high `Take a screenshot or save a recording to find it here.`
### 6.8 Window layout (`PanelWindowLayoutView`)
Header `Window layout` (rectangle.3.group) + tune toggle (slider.horizontal.3 -> checkmark.circle.fill accent in edit; hides actions from the grid) + X. Intro card: checkbox `Snap windows at screen edges` (`windowEdgeSnapEnabled` false) + caption (9.5 tertiary) `Turn this on and drag a window title bar to a highlighted area below...`; when on: zone picker (compact); system-conflict warning `macOS is using the same edges. Turn off window tiling in Desktop & Dock so Vorssaint can take over.` + `Open Desktop & Dock` + `Enabled in Vorssaint...` (Windows: Snap Layouts conflict note + button `Open Multitasking settings` `ms-settings:multitasking`); divider; caption `Arrange windows into screen sections or move and resize them with a trackpad or mouse.`; checkbox `Shortcuts` (`windowLayoutShortcutsEnabled` true) + caption; divider; checkbox `Move and resize by dragging` (`windowGestureEnabled` false) + modifier picker `Keys to move` + hints `Drag to move`, `Add Shift and drag to resize`, `The starting point chooses the nearest edge or corner. On a mouse, right-button drag also resizes.`; Accessibility grant button when missing. Action groups (each a card: heading UPPERCASE 9.5 bold tertiary + 2-column grid, spacing 7, bordered small buttons min height 28, `Label(title, symbol)` 10.5 semibold + shortcut text 9 medium rounded secondary when shortcuts on): `Halves` (Left, Right, Top, Bottom, Center half), `Thirds` (Left 1/3, Center 1/3, Right 1/3, Left 2/3, Right 2/3, Center 2/3, Top 1/3, Middle 1/3, Bottom 1/3, Top 2/3, Bottom 2/3), `Quarter rows` (Top 1/4, Upper middle 1/4, Lower middle 1/4, Bottom 1/4), `Quarter columns` (Left 1/4, Left middle 1/4, Right middle 1/4, Right 1/4), `Sixths` (Top left/center/right 1/6, Bottom left/center/right 1/6), `Corners` (Top left, Top right, Bottom left, Bottom right), `Actions` (Maximize, Maximize with Margin, Full Screen, Center, Previous display, Next display, Restore). Result line card: green `Window arranged.` / `Window restored.` or orange `No active window found.` / `Grant Accessibility to move windows.` / `No previous layout to restore.` / `Could not move this window.` Hidden actions per `windowLayoutHiddenActions`. Buttons disabled without Accessibility (Windows: n/a).
### 6.9 Cleaner / Media
`PanelCleanerView`: header (sparkle 13 + `Cleaner` 13 semibold + X, cancels scan) then `CleanerView(compact:true)`. `PanelMediaView` = `MediaWorkspaceView(compact:true, onClose:)` (own header with tool tabs: video compressor, GIF, image tools, text extraction). Their bodies are specified in the Cleaner / Media feature specs.

---------------------------------------------------------------------------------------------------------------------

## 7. Behaviour summary (cross-cutting)
- **Persistence**: every visibility/order key listed above lives in the app settings store; reorder via drag writes immediately; the panel observes changes live (Settings window can edit while the panel is open).
- **Tab refresh**: tab set recomputed on any of: feature availability change, `panelShow*`/`monitorShow*` change, `brightnessControlEnabled`.
- **Sampling lifecycle**: monitor sampler interval = `monitorInterval`; runs continuously only while a menu-bar metric is pinned; otherwise only while the visible tab needs it; per-process breakdown sampling only while a breakdown is expanded/visible; network per-app monitoring only while visible.
- **Hosted tool lifecycle**: switching tab or closing the flyout resets the tool and clears `viewKeepsPopoverOpen` and `hostedSettingsPage` (except it stays when the flyout is merely closed - the tool state resets on next open).
- **Permissions polling** (mac): rows re-read permission state every 2.5 s while a surface needing it is visible (60 s otherwise). Windows: re-read elevation/privacy states on flyout open and every 2.5 s while visible.
- **Accessibility**: tabs are icon-only so each carries accessible name + `selected` state; rows expose captions as help; sliders expose value `NN%`; adjustable actions `Move Up`/`Move Down` for reorderable rows.
- **Localisation**: all strings via the shared string table (15 languages on mac); only English needs to be implemented first, keep keys.

---------------------------------------------------------------------------------------------------------------------

## 8. Impossible / adapted on Windows (summary)
| mac feature | Windows decision |
|---|---|
| Text next to the tray icon | impossible -> per-metric tray icons (bitmap blocks) and/or a taskbar metrics strip window (2.10) |
| Status item arrow popover | Windows flyouts have no arrow; draw one for fidelity (3.2) or omit by option |
| Fan control tab + `fanSpeed` metric | skipped |
| CPU/GPU/battery temperature | no supported API -> show `-` / hide; optional LibreHardwareMonitor |
| App volume boost > 100 % | not possible with WASAPI session volume; cap at 100 % or implement process-loopback re-render (optional) |
| Per-app output device | possible via undocumented `IAudioPolicyConfig` |
| `System sounds` device | no separate Windows device; omit or map to communications device |
| Memory pressure, compressed, purgeable, swap | derived/approximate (3.4 mapping) |
| Keyboard backlight, XDR extra brightness, brightness keys | omit |
| Dock preview / dock click / Spaces order / green-button maximize / trackpad 3-finger click | omit (OS-native or non-existent) |
| Accessibility / Screen Recording / Full Disk Access permission rows | mostly N/A; keep the row mechanism for UAC elevation, camera/microphone privacy |
| Homebrew | winget |
| Cut & paste in Finder | File Explorer (existing feature) |
| Finder-restart captions | Explorer refresh (no restart needed) |
| Liquid Glass option | map to Mica/Acrylic (`liquidGlassEnabled` -> `Use acrylic`) or drop |

## 9. Gap vs Trayloom - consolidated
Trayloom today (`src\Trayloom.App`): `TrayController` (static icon, 3-4 item context menu), `QuickPanelWindow` (448 wide WPF flyout, `Quick panel` title, wrap-panel of 88 px action tiles from `IActionCatalog`, `Features...` button, closes on deactivate/Esc, positions above the work area using the cursor position, DWM rounded corners, dark mode attribute, 300 ms toggle guard), `FeaturesWindow` (hub), per-feature flyouts (`FlyoutWindow`) for Mixer, Monitor, Displays, plus `Osd` pill for the pinned readout.
Not present and required by this spec:
1. A single tabbed panel with brand header, icon tab bar, per-section title+edit header, scroll body and Settings/Quit footer (332 wide, 18 radius, tokens in section 1) and persistence of order/visibility.
2. All section bodies in section 5 (Keep awake card with chips/Until picker/automation/jiggle/closed-lid; Displays; Mixer incl. editable %, pin/order, per-app output, priority; System with temps grid/usage rows/cores/memory pressure/uptime/devices; Network incl. unit toggle, apps, totals, addresses, speed test; Disks incl. selector/SMART/eject/exclusions; Power incl. charge/health/remaining/adapter/peripherals; Utilities rows with chips + hosted tools; Controls with 3 collapsible categories; Quick toggles; Wallpaper gallery).
3. Hosted tool panels (section 6) inside the flyout, `viewKeepsPopoverOpen` dismissal rules, `hostedSettingsPage` footer routing.
4. Tray: state icons, tooltips, countdown, metric blocks/strip/separate icons, right-click menu of 11 items, per-metric detail view, overflow first-run tip, anchor via `Shell_NotifyIconGetRect` + arrow.
5. Panel header update banner and metric-detail navigation.
6. Settings keys of sections 2.7 and 5 (none exist in `SettingsStore` today; feature settings there are per-feature `Settings.Get("pinned")` style).
Existing services to reuse: `Trayloom.Features.System/Monitor/SystemSampler.cs` + `Pdh.cs` (CPU/GPU/mem/net/disk), `Audio/*` (sessions/devices), `Display/*`, `KeepAwakeFeature`, `Features.Tools/*` (QuickToggles, Cleaner, Uninstaller, Wallpaper, PortManager, AppUpdates, MediaTools...), `Features.Windows/ClipboardHistory*`, `WindowLayoutFeature`.
