# 02a. Settings pages: General (+ sound sub-pages), Energy, Monitor, Shortcuts, Advanced

Source of truth: `vorssaint-mac/Sources/Vorssaint/UI/Settings/{GeneralSettings, GeneralToolSettings, EnergySettings,
MonitorSettings, MonitorPanelConfig, MonitorAlertsControls, MonitorToken, PanelLayoutEditor, DiskExclusionsList,
AdvancedSettings, DisplayBrightnessShortcutControls, ShortcutsSettings}.swift` plus the helpers they pull in
(`UI/KeepAwakeIconPicker.swift`, `UI/KeepAwakeAutomationView.swift`, `UI/MenuPanel/{MixerSection,AudioPrioritySection,PanelLayout}.swift`,
`UI/ShortcutRecorderButton.swift`, `Core/Defaults.swift`, strings in `Core/*Strings.swift` `static let enUS`).
All English strings are verbatim; "Vorssaint" is written **WinKnife**. `[WIN]` marks a platform-noun substitution
(original text kept next to it). Defaults are the *registered* defaults in `Defaults.registeredDefaults` (they win
over the `@AppStorage(... = x)` fallback written in the views; the two differ in a few places and this is noted).

Conventions used below
* "Switch" = `Toggle(...).toggleStyle(.switch)` with hidden label (WinKnife: `ThemeKeys.SwitchStyle`).
* "Menu picker" = `Picker(...).pickerStyle(.menu)` with hidden label (WPF `ComboBox`).
* "Segmented" = `Picker(...).pickerStyle(.segmented)` sized to content (WPF segmented `ListBox`/`RadioButton` strip).
* Swift point sizes are given as-is; use the same DIP values with Segoe UI Variable (macOS `body` 13, `callout` 12,
  `subheadline` 11, `caption` 10, `headline` 13 semibold, `title2` 17, `title3` 15).
* Apostrophes and quotes inside the source strings are typographic (’ U+2019, “ ”); keep them when copying strings.
* Settings keys are the raw `UserDefaults` strings (Vorssaint). On Windows they are stored in the existing
  `%APPDATA%\WinKnife\settings.json` through `ScopedSettings` (scope `core` for app-wide keys); keep the same key names.

---------------------------------------------------------------------------------------------------------------

## 0. Shared building blocks used by every page below

### 0.1 Page frame (all pages in this file)
`ScrollView > VStack(alignment .leading, spacing 20)`, content column `maxWidth 760` centred, `padding 22`.
Page title = `Text(...).font(.title2.bold())`. Pages that have a description put a `VStack(spacing 5)` with title + `callout`
secondary multi-line text. Energy/Monitor use this header; General/Advanced/Shortcuts only title (General) or none
(Advanced and Shortcuts are `Form{...}.formStyle(.grouped)`, i.e. grouped sections, see their pages).

### 0.2 `SettingsCard` (`SettingsCard.swift`)
`VStack(alignment .leading, spacing 13)`; optional title `Text(title).font(.headline)`; full width; `padding 16`;
background `quaternary` at 35% opacity (≈ 3.5% of the text colour: light `#000 @ 3.5%`, dark `#FFF @ 3.5%`) in
`RoundedRectangle(cornerRadius 16, continuous)`. Cards that are a scroll anchor (`.settingsSectionAnchor(x, cornerRadius 16)`)
get a brief highlight ring (corner radius 16) when a sidebar row / search result jumps to them.

### 0.3 `SettingsRow` (`SettingsCard.swift`)
`HStack(spacing 12)`: **icon tile** 26x26, corner 7, fill accent @12%, SF symbol `system(size 12, weight semibold)` accent;
**text column** (`VStack(spacing 2)`): title `Text(title)` (body) + optional **badge** (`system(8, bold)` white on accent `Capsule`,
padding h5 v1, e.g. "BETA"), then optional caption (`.caption`, secondary, wraps); `Spacer(minLength 12)`; **accessory** on the right.
When `symbol == nil` the tile shows the app's own menu-bar glyph (`MenuBarGlyph`, 17x13, white) on a `black @82%` tile (26x26, radius 7).
`settingsRowTextInset = 26 + 12 = 38`: follow-up controls that belong under a row are indented `padding(.leading, 38)`.
Row separators inside a card are `Divider()`.

### 0.4 Small controls
* **Chip** (interval/duration/etc.): label `system(11, medium)`, 1 line; unselected: fill `secondary @6%`; selected: text accent,
  fill accent @12% and 1px accent @70% stroke; corner 8 continuous; height 28 (Energy duration chips) / 26 (Monitor interval chips,
  `padding h10`).
* **Choice icon cell** (menu bar icon gallery, keep-awake icon picker): corner 7; selected = fill accent @14% + 1.2px stroke accent @75%;
  unselected = fill `secondary @6%` + 1px stroke `secondary @13%`.
* **MonitorToken** (section 5.3) and **MonitorTokenOption** are used on the Monitor page.
* Disclosure rows: `DisclosureHeaderRow` = whole row clickable, label then trailing chevron (`chevron.right`, caption semibold,
  rotates 90 deg when open, 0.18 s ease-in-out); children indent `padding(.leading, 25)`. Accessibility value strings
  "Expanded" / "Collapsed".

### 0.5 SF Symbol → Segoe Fluent Icons map (all symbols used in this file)
`?` = glyph name/code point from memory, verify in Character Map ("Segoe Fluent Icons") before shipping; `-` = no glyph exists,
use the stated fallback. Windows tray/UI glyph font: `Segoe Fluent Icons` (Win11) with `Segoe MDL2 Assets` fallback.

| SF Symbol | Use | Segoe glyph |
|---|---|---|
| laptopcomputer | Launch at login row, built-in display, closed-lid row | E7F8 ? (Laptop) |
| globe | Language row | E774 (Globe) |
| sparkles | Liquid Glass row | EE57 ? (Sparkle) else U+2728 in Segoe UI Symbol |
| keyboard | Global shortcut row, display-brightness shortcut row | E765 (KeyboardClassic) |
| bubble.left.and.text.bubble.right | Feedback row | E8F2 (ChatBubbles) |
| menubar.rectangle | "In the menu bar" heading, Menu bar sidebar row | E8A0 ? (tray/taskbar area; alt E7F4) |
| macwindow | "In the panel" heading | E737 (Window) |
| slider.horizontal.3 | "Readings and alerts" heading | E9E9 ? (Equalizer) else E713 |
| line.3.horizontal | drag handle | E700 (GlobalNavigationButton) |
| playpause.fill | Media keys row | E768 (Play) |
| moon.zzz.fill | Keep awake | E708 (QuietHours) |
| play.circle | auto start row | E768 |
| cursorarrow.click.2 | right-click row | E7C9 ? (TouchPointer) |
| timer | show countdown / update every | E916 (Stopwatch) |
| display / display.2 | allow display sleep, Displays | E7F4 (TVMonitor) |
| cursorarrow.motionlines | move pointer slightly | E7C9 ? |
| battery.25percent | battery limit | E852 ? (Battery2) |
| sun.min / sun.max / sun.max.fill | brightness rows, Extra brightness | E706 (Brightness) |
| dot.radiowaves.left.and.right / wave.3.right.circle | Bluetooth on sleep | E702 (Bluetooth) |
| arrow.counterclockwise | restore Bluetooth | E7A7 (Undo) |
| lock.fill | pause while locked | E72E (Lock) |
| cursorarrow.rays | brightness keys follow pointer | E7C9 ? |
| power / power.circle.fill | display power button | E7E8 (PowerButton) |
| circle.lefthalf.filled / checkmark.circle.fill | software dimming button | E91F ? (CircleHalfFill) / E73A (CheckboxComposite) |
| externaldrive / internaldrive | disk exclusions, disk | EDA2 (HardDrive) |
| minus.circle.fill / xmark.circle.fill | remove buttons | E738 (Remove) / E711 (Cancel) in a circle |
| plus | add buttons | E710 (Add) |
| chart.line.uptrend.xyaxis | Monitor page icon | E9D9 (Diagnostic) |
| cpu | CPU | E950 ? (no exact) |
| rectangle.connected.to.line.below | GPU | E7F4 ? |
| memorychip | Memory | E950 ? |
| network | Network / IP addresses | E968 (Network) |
| speedometer / gauge.with.needle | Live speed / Speed test | EC4A ? (Speed) |
| app.badge | Apps using network | E71D (AllApps) |
| sum | Session totals | U+2211 text glyph |
| arrow.up.arrow.down | Live activity / upload-first | E174 (Sort?) ? / E8CB (Sort) |
| heart.text.square / heart | SMART / Battery health | EB51 (Heart) |
| shield | External protection | EA18 ? (Shield) |
| wrench.and.screwdriver(.fill) | Tools, Advanced, Utilities | E90F (Repair) |
| bolt / bolt.fill | Power | E945 (LightningBolt) |
| powerplug / powerplug.fill | Adapter / Power | E83E ? (charging) |
| battery.75percent / battery.100percent.bolt | Charge / Battery | E853 / E83E ? (Battery3 / BatteryCharging) |
| clock | Uptime / Battery time | E121 (Clock) |
| thermometer.medium / thermometer.high | temperatures | - use U+1F321 in Segoe UI Symbol or a bundled glyph |
| arrow.left.and.right | menu bar spacing | E8AB (Switch) |
| eye / eye.slash | show/hide | E7B3 (View) / ED1A (Hide) |
| rectangle.split.3x1 | Separate metrics | E8A9 ? (ViewAll) |
| fanblades / fanblades.fill | Fan control | - no glyph (bundle a fan SVG) |
| bell.badge | repeat alert after | EA8F (Ringer) |
| cable.connector | Connected devices | E88E (USB) |
| square.and.arrow.up / .down | Export / Import | E898 (Upload) / E896 (Download) |
| lock.slash | Clear all permissions | E785 (Unlock) |
| trash | Uninstall | E74D (Delete) |
| app.badge (App shortcuts button) | App shortcuts | E71D |
| speaker.wave.2(.fill) / speaker.slash.fill / mic.fill / mic.slash.fill / bell.fill | mixer | E767 (Volume) / E74F (Mute) / E720 (Microphone) / E720 / EA8F |
| list.number | Audio priority | E9D5 ? (NumberedList) else E8FD |
| hifispeaker | Output switcher | E7F5 (Speakers) |
| music.note | Music blocker | EC4F (MusicNote) ? |
| switch.2 / togglepower / photo.on.rectangle | panel tabs Controls / Toggles / Wallpaper | EC11 ? (Toggle) / E7E8 / E91B (Picture) |
| rectangle.on.rectangle | Switcher feature | E8A9 ? |
| gearshape / power (footer pills in miniature) | gear/power | E713 / E7E8 |
| wifi / battery.75 (menu bar mock) | tray mock | E701 / E851 |
| line.diagonal, ellipsis, chevron.down/right | misc | E7C5? (use a drawn 45-deg line) / E712 / E70D / E76C |
| hand.raised, exclamationmark.triangle, exclamationmark.circle.fill | permission/warning | E7BA (Warning) |
| checkmark.circle.fill, circle | token check | E73E (CheckMark) in circle / E91F |
| cup.and.saucer.fill, eye.fill, moon.fill, lightbulb.fill | keep-awake active icons | - U+2615 (Segoe UI Symbol), E7B3, E708, EA80 ? (Lightbulb) |

---------------------------------------------------------------------------------------------------------------

## 1. Where these pages sit in the Settings window (context only)

The Settings window has a flat sidebar (another spec covers the shell). The destinations that render the views of this file:

| Sidebar row (title, SF icon) | Page / view | Anchor |
|---|---|---|
| General (`gearshape`) | `GeneralSettings` | - |
| Menu bar (`menubar.rectangle`), directly below General | `GeneralToolSettings(.panelConfiguration)` | panelConfiguration |
| Volume mixer (`speaker.wave.2`), group "Sound" | `GeneralToolSettings(.mixer)` | mixer |
| Output switcher (`hifispeaker`) | `GeneralToolSettings(.soundOutputSwitcher)` | soundOutputSwitcher |
| Audio device priority (`list.number`) | `GeneralToolSettings(.audioPriority)` | audioPriority |
| Music app blocker (`music.note`) | `GeneralToolSettings(.musicBlocking)` | musicBlocking |
| Keep awake (`moon.zzz.fill`), group "Energy and display" | `EnergySettings(focus: .keepAwake)` | keepAwake |
| Displays (`display.2`) | `EnergySettings(focus: .brightness)` | brightness |
| Extra brightness (`sun.max.fill`) | `EnergySettings(focus: .extraBrightness)` | extraBrightness |
| Bluetooth on sleep (`wave.3.right.circle`) | `EnergySettings(focus: .bluetoothSleep)` | bluetoothSleep |
| Monitor (`chart.line.uptrend.xyaxis`) | `MonitorSettings` | - |
| Fan Control (`fanblades.fill`, row under Monitor) | scrolls to the fan card in `MonitorSettings` | fanControl |
| Keyboard shortcuts (`command`), category "App" | `ShortcutsSettings` | - |
| (extra row `Keyboard light`, `keyboard`) | scrolls to keyboard-brightness group | keyboardBrightnessShortcuts |
| Advanced (`wrench.and.screwdriver`), category "App" | `AdvancedSettings` | - |

Page titles (strings): "General", "Energy", "Monitor", "Keyboard shortcuts" (`shortcutsPageTitle`), "Advanced" (`tabAdvanced`).
Section captions of the features hub for these features (reuse on hub tiles/search): Keep awake "Keep the Mac awake on demand",
Displays "Brightness and power controls for every display", Extra brightness "Extra brightness on XDR displays",
Bluetooth on sleep "Switches Bluetooth off while the Mac sleeps, so headphones in a bag stop connecting to it.",
Volume mixer "Per-app volume, pinning and custom order", Output switcher "Cycle sound outputs with a shortcut",
Audio device priority "Automatically use your preferred audio devices", Music app blocker "Block music launches from detected media keys",
Monitor CPU/GPU/Memory/Network/Disk/Power: "Processor usage and temperature" / "Graphics usage and temperature" /
"Memory use and pressure" / "Network speed and usage" / "Disk space and activity" / "Battery, power and charging",
Connected Devices "Count connected external USB peripherals", Fan Control "Control fans manually or with temperature curves while seeing live and target RPM".
`[WIN]` replace "Mac" with "PC" in these captions.

---------------------------------------------------------------------------------------------------------------

## 2. General page (`GeneralSettings.swift`)

### 2.1 Layout (top to bottom)
1. Title `Text("General").font(.title2.bold())`.
2. **Card A (no title)**
   1. `SettingsRow` symbol `laptopcomputer`, title **"Launch at login"**, caption **"Opens by itself every time you turn on your Mac."** [WIN: "...turn on your PC."]; accessory = optional small `ProgressView` (while a change is pending) + Switch. The switch reads ON whenever registration != off (also while "needs approval"); disabled while pending.
   2. Conditional under it: if registration == needsApproval: caption text **"The login item is registered, but macOS still needs your approval. In System Settings, allow WinKnife to open at login and to run in the background."** [WIN: "...but Windows still needs your approval. In Settings > Apps > Startup, allow WinKnife to start at sign-in."] (`.caption`, secondary, wraps) + `Button` **"Open System Settings…"** [WIN: "Open Windows Settings…"] (opens login-items settings). Else if `loginError` non-nil: red `.caption` text with the error.
   3. `Divider()`
   4. `SettingsRow` symbol `globe`, title **"Language"** (no caption), accessory = menu picker listing `AppLanguage.allCases`, each shown by its own-script name; the selection is `L10n.language`.
3. **Card B, title "Appearance"**
   1. `HStack(spacing 10)` of 3 `AppearanceChoice` buttons in order **System, Light, Dark** (enum `AppAppearance.allCases` = system, light, dark; titles "System", "Light", "Dark"). Each: `VStack(spacing 8)`: thumbnail 66 high (corner 9 continuous, clipped) with a stroke (selected: accent 2px; unselected: primary @12% 1px), then the label `system(11, medium)` (accent when selected, primary otherwise); cell `padding 8`, `maxWidth: .infinity`, background accent @8% (selected) in `RoundedRectangle(12)`.
      * Thumbnail art ("three little desktops with a window"): a desktop gradient top-left→bottom-right (light: `rgb(.80,.88,1.00)`→`(.58,.72,.95)`; dark: `(.20,.22,.34)`→`(.07,.07,.13)`), with a mini app window inset (top 12, left 14, corner 5): title bar 12 high (`white .93` light / `.22` dark) with three traffic-light dots 5pt (`#FF5F57`, `#FFBD2E`, `#29C740`, spacing 3, h-padding 6), body background (`white .99` / `.19`), left sidebar 34 wide (`.90` / `.16`) with 3 capsule bars (w 18/22/14, h 3, fill `.76` / `.40`, spacing 4, padding 6) and content area with 3 capsule bars (w 46/34/40, spacing 5, padding 8). "System" = light thumbnail with the dark one masked to its lower-right triangle (diagonal split, `DiagonalHalf`).
   2. caption **"Applies to WinKnife’s own windows and panels, not to the whole Mac."** [WIN: "...not to the whole PC."] (`.caption` secondary).
   3. (macOS 26+ only, hidden otherwise) `Divider()`, `SettingsRow` symbol `sparkles` title **"Liquid Glass"** caption **"See-through, glass-like panels."**, then a `VStack(spacing 8)` indented 38 with two trailing-switch toggles (label left, switch right): **"Other windows and panels"** (`liquidGlassEnabled`) and **"Dynamic Island"** (`notchLiquidGlassEnabled`; title = `FeatureStrings.notch.title`).
4. **Card C, title "Global shortcut"** — only if the Keep-awake feature is installed (`AppFeature.keepAwake.isAvailable`):
   1. `SettingsRow` symbol `keyboard`, title **"Enable shortcut for “Keep awake”"**, caption **"Works in any app, no extra permissions."**, accessory Switch bound to `hotkeyEnabled` (on change calls `HotkeyManager.setEnabled`).
   2. Indented 38: `ShortcutPreferenceRow(role .keepAwake, isEnabled: hotkeyEnabled)`: label column **"Shortcut"**, recorder button (width 108) + **"Reset"** button, see 7.4 for the row's full behaviour.
   3. If `hotkeyEnabled` and registration failed: orange caption (indent 38) **"macOS rejected this shortcut. Choose another one."** [WIN: "Windows rejected this shortcut. Choose another one."]
5. **Card D (no title)**: `SettingsRow` symbol `bubble.left.and.text.bubble.right`, title **"Feedback"**, caption **"Send a bug report or feature idea directly to the person who maintains WinKnife."**, accessory `Button` **"Send feedback"** (opens the Feedback window).

### 2.2 Settings
| Key | Type | Default | Allowed | Effect |
|---|---|---|---|---|
| `launchAtLoginWanted` | Bool | false | - | The user's wish; the real registration is repaired at startup to match (see Behaviour) |
| `language` (raw key `appLanguage`) | String | system language on first launch, else `en-US` | `en-US, pt-BR, tr, ru, es, sk, de, fr, it, ja, ko, uk, zh-Hans, zh-TW, zh-HK` | UI language (live) |
| `appearance` (raw key `appAppearance`) | String | `system` | `system, light, dark`; unknown → system | Forces the app's own windows/panel light or dark |
| `liquidGlassEnabled` | Bool | false | - | Glass material on windows/panels (macOS 26+) |
| `notchLiquidGlassEnabled` | Bool | false | - | Glass material on the Dynamic Island |
| `hotkeyEnabled` | Bool | **true** | - | Master switch of the Keep-awake global shortcut |
| `keepAwakeShortcut` | String | `control+option+command:40` (⌃⌥⌘K) | `mods+...:keyCode` storage string | The shortcut; must contain Control, Option or Command |
Language display names (verbatim), in `AppLanguage.allCases` order: English (US) `en-US`, Português (Brasil) `pt-BR`, Türkçe `tr`, Русский `ru`, Español `es`, Slovenčina `sk`, Deutsch `de`, Français `fr`, Italiano `it`, 日本語 `ja`, 한국어 `ko`, Українська `uk`, 简体中文 `zh-Hans`, 繁體中文（台灣） `zh-TW`, 繁體中文（香港） `zh-HK`.

### 2.3 Behaviour
* **Launch at login** is "remembered and self-repairing": the switch is seeded from `launchAtLoginWanted` so it never flashes off; the real state is read on `onAppear` and whenever `LaunchAtLoginSupport.settingsRefreshRequested` fires. Toggling: immediately shows the requested state, shows the spinner, disables the switch, runs the register/unregister off the main thread, then replaces the state with the real registration and clears/sets the error. A read that arrives after a change releases the switch. A stale answer (older request id) is dropped. Error text is cleared when the result is `needsApproval` (the approval note replaces it). Unregistering something already gone is not an error. If registering fails the stored wish is reset to the real state. At app start `repairAtStartup` re-registers when the wish is true but the system lost it, and adopts an enable done in System Settings (sets the wish true).
* **Appearance** applies instantly (`NSApp.appearance` + the panel popover).
* **Language** applies instantly to every window (all `L10n` observers re-render).
* **Keep-awake shortcut** row: see 7.4. Toggling the master switch registers/unregisters the hotkey; if registration fails the orange note appears.
* "Send feedback" opens the Feedback window (`appDelegate.openFeedbackWindow`).

### 2.4 Windows mapping
* Launch at login: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` value `WinKnife` = quoted exe path (already in `WinKnife.App/AutoStart.cs`). "Needs approval" = user disabled it in Task Manager > Startup apps: read `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run\WinKnife` (binary; first byte `0x02`/`0x06` = enabled, `0x03`/`0x07` = disabled by user). If disabled show the approval note + button opening `ms-settings:startupapps`. "Unstable location" has no Windows equivalent (drop `launchAtLoginNeedsApplications`). Keep `launchAtLoginWanted` + repair at startup (re-write Run key when wanted but missing).
* Language: all 15 languages exist upstream; minimum viable = `en-US` + the picker structure; strings in a `Strings` resource keyed by the Swift property names.
* Appearance: `ThemeMode.Light/Dark/System` on `Application` (WPF .NET 9) plus `ThemeManager.Apply(isLight)`; set `DWMWA_USE_IMMERSIVE_DARK_MODE` on each window; the tray flyout must follow too. System = follow `AppsUseLightTheme`.
* Liquid Glass (macOS 26 only): **Windows equivalent = Mica/Acrylic** via `DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE)` (`DWMSBT_MAINWINDOW` Mica, `DWMSBT_TRANSIENTWINDOW` Acrylic) with `ExtendFrameIntoClientArea`. Show the card only on Windows 11 build >= 22621; recommended label `[WIN]` "Acrylic" instead of "Liquid Glass" (keep captions). Two switches: windows+flyout, and the Island (Island is a custom overlay: use acrylic via `SetWindowCompositionAttribute` ACCENT_ENABLE_ACRYLICBLURBEHIND or a blurred-wallpaper brush).
* Global shortcut: `RegisterHotKey` (existing `IHotkeyService`/`Hotkeys.cs`); Windows needs no permission. Default: map ⌃⌥⌘ layer to **Ctrl+Alt+Shift** (Win key combos are largely reserved) → `Ctrl+Alt+Shift+K`; failed `RegisterHotKey` (ERROR_HOTKEY_ALREADY_REGISTERED) = "registration failed" note.
* Feedback: `Send feedback` opens the Feedback window (separate spec).

### 2.5 Gap vs WinKnife
* There is **no General page at all**: WinKnife has a Features hub (`WinKnife.App/FeaturesWindow.xaml.cs`) where each feature's `CreateSettingsView()` is shown; the only "launch at login" is a tray context-menu item "Start with Windows" (`TrayController.cs`, `AutoStart.cs`, no StartupApproved check, no wish/repair, no spinner).
* No language setting (strings are hard-coded English in code), no Appearance override (`ThemeManager` always follows system: `ThemeMode.System`), no glass/Acrylic option, no feedback row.
* `KeepAwakeFeature` has no global shortcut and no `hotkeyEnabled` (only quick-panel actions).
* Missing the whole `Card`/`Row` visual language (`SettingsCard` in `WinKnife.Core/Ui/SettingsCard.cs` is a title+description+control card without icon tile, badge, inset, divider rhythm).

---------------------------------------------------------------------------------------------------------------

## 3. General sub-pages (`GeneralToolSettings.swift`)

Each of these is its own sidebar destination (the same `General` page enum with a section anchor). Same page frame (0.1) but **no page title**: just the card(s).

### 3.1 "Menu bar" page (anchor `panelConfiguration`)

#### Layout
`SettingsCard(title: "Menu bar")` [WIN: title stays "Menu bar" verbatim in the strings table; display "Taskbar"/"Tray" is a product decision; recommended: keep "Menu bar" key, show `[WIN]` **"Tray"**]:
1. Intro `Text` (`callout`, secondary, wraps): **"Click WinKnife’s icon in the menu bar to open the panel. Its tabs appear in this order."** [WIN: "...in the system tray..."]
2. `PanelLayoutEditor` (see 3.1.1).
3. `Divider()`
4. `MenuBarIconSymbolRow` (see 3.1.2).
5. `Divider()`
6. `SettingsRow(symbol nil → app glyph tile)` title **"Can’t find the icon?"**, caption **"A crowded menu bar can hide it, especially on Macs with a notch."** [WIN: "A crowded system tray can hide it; drag it out of the overflow."], accessory `Button` **"Show menu bar icon"** [WIN: "Show tray icon"] → `reshowStatusItem()` (recreates the status item).

#### 3.1.1 `PanelLayoutEditor`
Adaptive: `ViewThatFits(horizontal)` → first choice `HStack(alignment .top, spacing 20) { miniature ; rows(minWidth 300, ideal 320, max ∞) }`, otherwise `VStack(spacing 16) { miniature ; rows }`.

**Miniature column** (width 232, `VStack(spacing 10)`): `MenuBarPanelMiniature` then caption **"Drag to reorder. Switch off anything you don’t need."** (`.caption` secondary). Miniature (`VStack(spacing 2)`, width 232):
* *Menu bar mock*: height 22, `black @82%`, corner 6, h-padding 12, right-aligned items spacing 10: `wifi` (9.5pt, white @50%), `battery.75` (10pt, white @50%, only if the machine has an internal battery), then the app glyph 20x15 padded (h3, v2) on a `white @16%` rounded(4) highlight (the item stays highlighted while its panel is open).
* *Panel mock* (below, 2pt gap): bubble shape (corner 12, arrow 14 wide x 7 high pointing up at the glyph, arrow tip 22pt from the trailing edge), fill = opaque `windowBackground` plate with shadow (`black @16%` light / `@50%` dark, radius 10, y 5) + regular material + panel base fill + 0.7px border. Inside, `VStack(spacing 8)`, padding 8 (+7 top for the arrow): brand mark (32 wide, 18 high; tint near-black light / white dark); **tab strip** (`HStack(spacing 1)`, padding 3, card fill, corner 8, 0.7px border) with one button per visible section: icon `system(9, semibold)`, height 20, corner 5; active = accent text + accent @13% (light) / @20% (dark) fill, inactive = secondary @86%; each has tooltip + accessibility label = section title; **open tab card** (card fill, corner 8, padding 8): active section title uppercased (`system(8, semibold)`, kerning 0.4, secondary, 1 line) then 3 placeholder capsules (height 5; widths = inner, 0.62·inner, 0.8·inner; inner = 232 − 2·8 − 2·8 = 200); **footer** two pills (`gearshape`, `power`; height 18, corner 5, card fill, 0.7px border).
* Animates tab changes with `.smooth(0.2)` (disabled when Reduce Motion).

**Rows column**: `VStack(spacing 0)`, one `PanelSectionRow` per editable section, `Divider().padding(.leading, 68)` between rows. Row (`HStack spacing 10`, padding v6 h8, corner 10 continuous, background: active = accent @10%, hovered = primary @4%, else clear; whole row opacity 0.45 while it is being dragged): drag handle `line.3.horizontal` (11 semibold, tertiary, width 14) • icon tile 26x26 radius 7 (symbol 12 semibold; accent when shown, secondary when hidden; fill same colour @12%) • text (`VStack spacing 1`): title `callout.weight(.medium)` (primary if shown, secondary if hidden) + description `.caption` secondary • trailing Switch (`controlSize .small`), disabled when this is the only visible section (`shown && !canHide`). The Fan Control row has **no switch** (it follows its own toggle on the Monitor page). Hover over a row previews that section in the miniature; click selects it; dragging a row reorders (immediate persist while dragging, 0.12 s ease-in-out).
Section list (canonical order; `id`, SF symbol, title, description):

| id | SF symbol | Title | Description |
|---|---|---|---|
| keepAwake | moon.zzz.fill | Keep awake | Keeps your Mac awake for as long as you want. |
| brightness | display.2 | Displays | Brightness of your displays. |
| mixer | speaker.wave.2 | Volume mixer | Volume of each app, one slider each. |
| system | cpu | System | Processor, graphics and memory at a glance. |
| network | network | Network | Internet speed and which apps are using it. |
| disk | internaldrive | Disks | Free space and disk activity. |
| power | bolt.fill | Power | Battery, charging and power use. |
| fanControl | fanblades.fill | Fan Control | Fan speeds and your own fan curve. |
| utilities | wrench.and.screwdriver.fill | Utilities | Screenshots, cleaner, updates and other tools. |
| controls | switch.2 | Controls | Switches for mouse, keyboard and window features. |
| toggles | togglepower | Quick toggles | One-click actions like dark mode and muting the mic. |
| wallpaper | photo.on.rectangle | Wallpaper | Pick a still wallpaper without opening System Settings. [WIN: "...without opening Settings."] |

Which rows are listed (`editableOrder`): section must be available (any feature in its gate installed: keepAwake←keepAwake; brightness←brightness; mixer←mixer|audioPriority; system←monitorCPU|GPU|Memory|connectedDevices; network←monitorNetwork; disk←monitorDisk; power←monitorPower; fanControl←fanControl; utilities←quickLauncher, cleaner, homebrew, appUpdates, mediaTools, clipboardHistory, windowLayout, uninstaller, urlCleaner, cleaningMode, screenOCR, colorPicker, screenshot, screenRecorder, cameraPreview, scratchpad, commandBar, portManager; controls←scroll/mouse/keyboard/window/dock features + notch; toggles←quickToggles|micMute|quitApps; wallpaper←wallpaper) **and** (`fanControl` only if `panelShowFanControl`) **and** (`brightness` only if `brightnessControlEnabled`).
The miniature's tabs = `editableOrder` filtered by `isVisibleInPanel` (available + shown + for brightness the control enabled).

#### 3.1.2 `MenuBarIconSymbolRow`
`VStack(alignment .leading, spacing 10)`: `SettingsRow(symbol nil)` title **"Menu bar icon"**, caption **"Choose the icon WinKnife shows in the menu bar."** [WIN: "...in the system tray."]; then a wrapping flow layout (spacing 6, `padding .leading 38`) of cells (34 x 30, corner 7, glyph `system(14, semibold)` primary; styles per 0.4):
1. the app's own mark (always first; selected when `menuBarIconSymbol` is empty); tooltip/label **"Use the WinKnife icon"**;
2. one cell per symbol of the gallery (tooltip = symbol name), filtered to symbols the OS can draw:
   `bolt.fill, star.fill, heart.fill, flame.fill, sparkles, leaf.fill, drop.fill, snowflake, sun.max.fill, moon.stars.fill, cloud.fill, mountain.2.fill, circle.fill, square.fill, triangle.fill, diamond.fill, hexagon.fill, seal.fill, circle.lefthalf.filled, circle.hexagongrid.fill, infinity, command, cpu.fill, memorychip.fill, gauge.with.dots.needle.67percent, fanblades.fill, gearshape.fill, terminal.fill, waveform, wand.and.stars, key.fill, crown.fill, gamecontroller.fill, headphones, music.note, paperplane.fill, pawprint.fill, cat.fill, hare.fill, tortoise.fill` (40; keep-awake's own symbols are intentionally excluded);
3. a last cell: shows `ellipsis` unless the saved name is custom (not in the gallery), then it shows that symbol and is selected; tooltip **"Other symbol"** (or the custom name); click opens the *name popover* (arrow below).
**Name popover** (`MenuBarIconNameField`, width 280, padding 16, `VStack spacing 8`): headline **"Other symbol"**; a rounded `TextField` (placeholder text `bolt.fill`, autocorrect off, focused on open, Return closes) + a trailing `xmark.circle.fill` clear button (tooltip/label "Use the WinKnife icon"; kept in layout but invisible/disabled while the field is empty so the width never jumps); caption **"Type the name of any SF Symbol. Leave it empty to use the WinKnife icon."** [WIN: "Type the name or hex code of any Segoe Fluent Icons glyph (for example E945). Leave it empty to use the WinKnife icon."]; orange caption **"This Mac has no symbol with that name."** [WIN: "This PC has no glyph with that name."] shown when the typed name is non-empty and not drawable.
Behaviour: every typing step saves immediately: a drawable name (or empty) is saved at once, any other name restores the name the field opened with ("a typo never leaves a valid half behind"). `Defaults.sanitizedMenuBarIconSymbol` = trim whitespace/newlines.

#### Settings (Menu bar page)
| Key | Type | Default | Notes |
|---|---|---|---|
| `menuBarIconSymbol` | String | `""` (app glyph) | trimmed; any SF symbol name |
| `panelSectionOrder` | String | `""` → canonical order | comma-joined section ids; missing ids are appended in canonical position (disk after network, controls after utilities, brightness after keepAwake) |
| `panelShowKeepAwake` / `panelShowBrightness` / `panelShowUtilities` / `panelShowControls` / `panelShowToggles` / `panelShowWallpaper` / `panelShowFanControl` | Bool | true | per-section visibility (mixer/system/network/disk/power reuse `monitorShowMixer/System/Network/Disk/Power`, all true) |
| `brightnessControlEnabled` | Bool | false | gate for the Displays row |
| `panelCollapsedSections` | String | `""` | set of collapsed section ids (panel itself) |

#### Windows mapping
* The "menu bar panel" = the **tray flyout** (`QuickPanelWindow`). The miniature must be mirrored for Windows: the tray lives bottom-right, so draw a *taskbar strip* (bottom, `black @82%` → use the real taskbar colour) with wifi/battery/glyph and the flyout *above* it with the arrow pointing down; tab strip + open tab + footer pills (settings gear, power → quit) stay.
* Tray icon art: `NotifyIcon` needs a bitmap: render the chosen glyph (Segoe Fluent Icons or the bundled mark) into 16/20/24/32 px icons at the DPI of the tray (`GetSystemMetrics(SM_CXSMICON)`), white/black according to the *taskbar* theme (`SystemUsesLightTheme`), regenerate on theme/DPI change.
* SF-symbol gallery → glyph gallery: use the glyph map 0.5 (flame/leaf/snowflake/mountain/seal/crown/cat/hare/tortoise/pawprint/wand have no Segoe glyph: ship them as embedded vector paths or use monochrome Segoe UI Symbol/Emoji code points U+1F525 🔥, U+1F343, U+2744, U+26F0, U+1F451, U+1F431, U+1F407, U+1F422, U+1F43E, U+1FA84). "Other symbol" accepts a Segoe glyph name or a hex code point.
* "Show tray icon": dispose and recreate the `NotifyIcon`; Windows 11 may keep it in the overflow flyout: also offer to open `ms-settings:taskbar` ("Other system tray icons"). (Promoting programmatically via `HKCU\Control Panel\NotifyIconSettings\<id>\IsPromoted=1` is undocumented; do not ship without asking.)
* Panel sections keys/order/visibility must be honoured by `QuickPanelWindow` (today it just lists feature actions).

#### Gap vs WinKnife
No editor, no section model, no order/visibility keys, no icon gallery, no "Show tray icon". `QuickPanelWindow.xaml.cs` renders one flat grid of `FeatureAction` tiles (88 px wide) with no tabs, no sections (Keep awake / Displays / Mixer / System … / Utilities / Controls / Quick toggles / Wallpaper), no collapse state, no persisted order. `TrayController` has a fixed icon `Assets/winknife.ico`, no custom glyph, no keep-awake active-icon swap.

---

### 3.2 "Volume mixer" page (anchor `mixer`) — `MixerSection(settingsMode: true)`
Single `SettingsCard(title: "Volume mixer")` containing `mixerControls` (`VStack spacing 8`). (The panel version is the same content inside a `PanelSection`; the panel spec owns the full widget, this is the settings-mode summary.)
1. **Audio devices block** (`VStack spacing 6`):
   * *Output* row: label `Label("Output", speaker.wave.2.fill)` (`system(11.5, medium)`, secondary, icon 16 wide) • spacer • menu picker (width 164, small; tooltip "Choose system output"): items = all output devices that can be default (current one suffixed " (current)"), plus "Output unavailable" when the current device is missing; disabled when the list is empty. Under it, when the volume is readable: `speaker(.slash).fill` icon • slider (0…1, normal tint) • editable percent (width 36, click to type, e.g. "62%"). If no outputs: icon+text **"No outputs found"**.
   * *System sounds* row: `Label("System sounds", bell.fill)`, picker tooltip **"Choose where alerts and sound effects play"**, same device list (those that can be default *system* output); empty → **"No outputs found"** with `bell.slash`.
   * *Microphone* row: `Label("Microphone", mic.fill)`, picker tooltip **"Choose microphone"**: "Default" + input devices + "Microphone unavailable" when the preferred mic is missing (only if priority is off); disabled when empty; a slider row with `mic(.slash).fill`; empty → **"No microphones found"**.
   * Error line when the last switch failed: warning icon + **"Could not switch: %@"**.
2. `Divider` (only when apps/permission block follows) then one of: **"Available on macOS 14.4 and later"** [WIN: not shown; Windows always supported], the permission hint **"To adjust per-app volume, allow “Screen & System Audio Recording” in System Settings. Audio is never recorded."** + button "Open System Settings…" [WIN: never shown], **"Apps that use audio show up here"** (empty), or the app rows.
3. App row (`HStack spacing 10`): 32x32 app icon (green 8pt "playing" dot bottom-right with 1.2px window-colour ring; tooltip **"Hold Command and drag to reorder"**); right column: name (`system(11.5, medium)`, 1 line, middle-truncated) • pin icon (`pin.fill` 9pt when pinned) • spacer • "…" menu (`ellipsis`, tooltip **"Actions"**; items **"Pin to Top"/"Unpin"**, plus "Allow volume control" toggle for screen-share/call apps) • output picker (width 112; "Default", devices, "Output unavailable", "Choose AirPlay speaker…" when AirPlay present; tooltip "Choose output"); below: slider (max **200%**, boost range tinted orange with a `bolt.fill` before the %) • editable percent (width 42) • `arrow.counterclockwise` reset (tooltip **"Reset to 100%"**, hidden at exactly 100%) • mute `speaker(.slash).fill` (red when 0). Bypassed apps show **"This app manages its own audio."**; unavailable output shows **"Using default until this device returns."**. Context menu: arrange actions + **"Hide from the list"**.
4. `Divider`, then **Options** disclosure (collapsed by default; title "Options", `system(11.5, semibold)` secondary, chevron 9 bold, children indented 19) containing `MixerOptionsControls(includeSharedAudioFeatures: false)` (output switcher and audio priority have their own pages here):
   * (macOS 14.4+) row **"Hide inactive apps"** + mini Switch (`hideInactiveApps`)
   * Checkbox **"Lower volume when headphones disconnect"** (`system(11.5, medium)`) + caption **"Adjusts output when wired or Bluetooth headphones disconnect."** (9.5pt); when on: small Stepper **"Volume after disconnect"** (0…100 step 5, default 25) with a right-aligned "%d%%" (38 wide)
   * Checkbox **"Use finer volume steps"** + caption **"Turns volume wheels and keys into smaller system volume steps."**; when on without Accessibility: link button "Open System Settings…" [WIN: omit]; if the tap failed: **"Could not listen for volume keys."**
   * Footer "Apps in the list" row (10pt secondary) with value **"All"** or **"Hidden: N"** + chevron; expanded: one checkbox per known app (checked = listed; disabled when the app has no stable id) — unchecking hides it.
Settings: `mixerHideInactiveApps` false, `mixerLowerVolumeOnHeadphonesDisconnect` false, `mixerHeadphonesDisconnectVolumePercent` 25 (0…100), `preciseVolumeRollerEnabled` false, `mixerShowFinder` true, `mixerAppArrangement` "" (pinned/order store), hidden-apps list.
Windows mapping: NAudio `MMDeviceEnumerator` / `AudioSessionManager2` (already in `Features.System/Audio/MixerWindow.cs`). **Impossible as-is**: >100% app boost (Windows `ISimpleAudioVolume` is 0…1 → cap slider at 100% and drop the bolt/orange boost styling, or later add a process-loopback re-render — ask first); per-app output routing exists only through undocumented `IAudioPolicyConfigFactory` (EarTrumpet technique; flag as risky); AirPlay picker: omit; screen-share "allow volume control" toggle: omit; "finer volume steps": Windows volume keys are 2% already; implement via LL keyboard hook swallowing `VK_VOLUME_UP/DOWN` and stepping `IAudioEndpointVolume` by 1% (no permission needed). Headphone-disconnect lowering: `MMDeviceEnumerator` notification client (`OnDeviceStateChanged`/`OnDefaultDeviceChanged`) on a headphone/Bluetooth endpoint.
Gap: `MixerWindow.cs` is a floating flyout with master slider, per-app rows, pin, `showInactive` toggle and a hotkey; there is no settings page in this form, no system-sounds picker, no microphone picker/slider, no hide list, no headphone-disconnect lowering, no finer steps, no percent editing.

### 3.3 "Output switcher" page (anchor `soundOutputSwitcher`)
`SettingsCard(title: "Output switcher")` → `SoundOutputSwitcherControls` (`VStack spacing 5`):
* Checkbox **"Switch outputs with shortcut"** (`soundOutputSwitcherEnabled`, default false; font `system(11.5, medium)`). Turning it on with nothing selected pre-selects the current default output.
* Caption **"Choose outputs and use the shortcut to move to the next available one."** (9.5pt)
* When enabled: `ShortcutPreferenceRow(role .soundOutputSwitcher)` (label "Shortcut"; default ⌃⌥⌘S; registration-failed note **"macOS rejected this shortcut. Choose another one."** with `keyboard.badge.ellipsis`; last-switch-failed note **"Select at least one available output."** with `speaker.badge.exclamationmark`); then label **"Outputs in cycle"** (10.5 medium secondary) and one checkbox per output device that can be default (title = name, " (current)" for the default; small control, 10.5pt); empty → **"No outputs found"** (`speaker.slash`). Selection order follows the system device order; unavailable-but-selected UIDs are kept at the end. Stored in `soundOutputSwitcherDeviceUIDs` (array of device UIDs, de-duplicated).
* Windows: existing `OutputSwitcherFeature` (IPolicyConfig `SetDefaultEndpoint` — undocumented but standard) + `DeviceEditors`. Default hotkey Ctrl+Alt+Shift+S (today Ctrl+Alt+O). Gap: has hotkey + device checkbox list inside the feature view, but no master checkbox/caption structure, no "Outputs in cycle" label, no failure notes, hotkey default differs.

### 3.4 "Audio device priority" page (anchor `audioPriority`)
`SettingsCard(title: "Audio priority")` → `AudioPriorityDisclosure(initiallyExpanded: true, showsHeader: false)`:
* Caption (9.5pt) **"Devices are selected in priority order. When a higher-priority device connects, it becomes active. When the active one disconnects, the next available takes over."**
* **Output** subsection: header `Label("Output priority", speaker.wave.2.fill)` (`system(11, semibold)`, secondary) + mini Switch (tooltip/label **"Automatically switch to the highest-priority output"**, key `audioPriorityOutputEnabled`, default **true**) then the priority list.
* `Divider`
* **Microphone** subsection: `Label("Microphone priority", mic.fill)` + mini Switch (**"Automatically switch to the highest-priority microphone"**, `audioPriorityInputEnabled`, default **true**) + list.
* List: empty text **"No outputs found"** / **"No microphones found"**; else rows (`VStack spacing 2`): rank number (10.5 medium rounded, 16 wide) • name (10.5, middle-truncated, primary or secondary when unavailable) with **"Current"** (9pt accent) when it is the active device • under the name orange 9pt **"Unavailable"** for disconnected devices (kept at their stored position with the last-known name) • drag handle `line.3.horizontal` (11 semibold tertiary, 16x22). Row: padding h6 v4, corner 7, fill `primary @3.5%` (current: accent @8% + 1px accent @65% stroke), opacity .45 while dragged. Drag reorders live (drop target insertion), VoiceOver actions **"Move up"/"Move down"**.
* Stores: `audioPriorityOutputUIDs`, `audioPriorityInputUIDs` (arrays), `audioPriorityDeviceNames` (uid→name dictionary).
* Windows: existing `AudioPriorityFeature` + `DeviceEditors` (MMDevice enumeration, `IPolicyConfig`). Keep rank/drag/"Current"/"Unavailable" visuals, add the two enable switches. Gap: priority editing exists inside the feature view but without the on/off switches per list, without caption/layout of this page and without drag handles (verify `DeviceEditors.cs`).

### 3.5 "Media keys" page — Music app blocker (anchor `musicBlocking`, feature `musicBlock`)

#### Layout
`SettingsCard(title: "Media keys")`:
1. `SettingsRow` symbol `playpause.fill`, title **"Stop Music from opening on its own"**, caption **"Blocks the music app opening after a detected media key. Requires Accessibility access. Headphone commands without a detected key are left alone."** [WIN caption: drop "Requires Accessibility access."], Switch `musicBlockEnabled`.
2. If enabled (all indented 38 unless noted):
   * If Accessibility is missing → `PermissionRow(.accessibility)` (see below) [WIN: never shown]; else if the blocker is not monitoring → orange caption **"This protection is unavailable right now. Turn it off and on to try again."**
   * `HStack(spacing 8)`: label **"Open instead"** • spacer • current replacement name (secondary; **"None"** when empty, else display name without extension) • button **"Choose app…"** • when a path is set, a plain `xmark.circle.fill` button (secondary) that clears it and clears the rejection note.
   * If a replacement is set: Toggle (checkbox style in a form) **"Play after opening replacement"** (`musicBlockPlayReplacement`, default true).
   * If the picked app is one of the blocked apps: orange caption **"Music and iTunes are the apps being blocked, so they can’t open instead. Choose another app."**
`PermissionRow` (shared): `HStack`: `checkmark.circle.fill` green / `exclamationmark.circle.fill` orange • name ("Accessibility") • spacer • status caption **"Granted"** green / **"Not granted"** orange; when missing: two small buttons **"Grant access"** and **"Open System Settings…"**.

#### Settings
| Key | Type | Default | Effect |
|---|---|---|---|
| `musicBlockEnabled` | Bool | false | enables the blocker |
| `musicBlockReplacementPath` | String | `""` | .app opened in place of Music |
| `musicBlockPlayReplacement` | Bool | true | send Play after opening the replacement |

#### Behaviour (from `MusicLaunchBlocker`/`MusicLaunchSupport`)
Observes media keys (Play/Pause, Next, Previous, Fast-forward, Rewind key-down, non-repeat) via an event tap; arms for **2.0 s** after a key; when `com.apple.Music` or `com.apple.iTunes` starts inside that window and the time since the last user gesture (pointer button / ordinary key; modifier keys ignored) is larger than the elapsed time since the key, the new process is terminated and the replacement (if any) opened (open timeout 15 s, settle 1 s, 3 playback attempts; only Play/Pause asks for playback). One key explains one launch only; a launch already judged is not judged again at did-launch. The chosen replacement may not be a blocked app (prevents a launch-kill loop). Nothing runs while the option/feature/permission is off.

#### Windows mapping
Feasible with a Windows-specific target set: block *Windows Media Player / Media Player (Microsoft.ZuneMusic) / Groove* auto-launch. Detect media keys with `WH_KEYBOARD_LL` (`VK_MEDIA_PLAY_PAUSE 0xB3`, `VK_MEDIA_NEXT_TRACK 0xB0`, `VK_MEDIA_PREV_TRACK 0xB1`; fast-forward/rewind don't exist), detect new process starts via WMI `Win32_ProcessStartTrace` (needs admin) or by polling `Process.GetProcesses()` at 100 ms for `wmplayer.exe` / `Music.UI.exe` / `Microsoft.Media.Player` AppX package within the 2 s window, then `Process.Kill`. Replacement chooser = file dialog for `.exe` (or installed-app picker) and `Process.Start`; "play" = send `VK_MEDIA_PLAY_PAUSE` after the window appears. No permission needed (drop the accessibility sentence and row). Note: Windows launches no music app on a bare media key by default (the system routes the key to the SMTC session), so this feature has low value; keep it for parity but mark as low priority. **Gap:** not present in WinKnife (no file mentions it).

---------------------------------------------------------------------------------------------------------------

## 4. Energy page (`EnergySettings.swift`)

`EnergySettings(focus:)` shows the header and **only the card group of the focused feature** (each sidebar row = one feature). With `focus == nil` all installed groups are stacked (the sidebar never routes there: "Energy has no overview row"). Each group is gated by its feature being installed.

Header (`VStack spacing 5`): title `title2.bold` + description `callout` secondary:

| focus | Title | Description |
|---|---|---|
| keepAwake | Keep awake | Keep the Mac awake on demand |
| brightness | Displays | Brightness and power controls for every display |
| extraBrightness | Extra brightness | Extra brightness on XDR displays |
| bluetoothSleep | Bluetooth on sleep | Switches Bluetooth off while the Mac sleeps, so headphones in a bag stop connecting to it. |
| nil | Energy | Keep the Mac awake, control your displays and save battery. |
[WIN] "Mac" → "PC".

`.onAppear`: sanitise `defaultDuration`, `batteryLimit`, `keepAwakeIconTint`, `keepAwakeActiveIcon`, `keepAwakeMouseJiggleInterval`; refresh the visible services (passwordless-sudo status, extra-brightness sync, brightness display rescan, since "displays may have changed since launch (docked, clamshell)"). The whole page asks for switch toggle style.

### 4.1 Keep awake (focus keepAwake) — two cards

#### Card 1 (no title) `keepAwakeCard`
1. Session row `SettingsRow(symbol moon.zzz.fill, title "Keep awake", caption status)` + Switch (on = a session is running). Caption (`status`):
   * running, started manually, with an end time: `"Ends in " + remaining` (refreshed every 1 s by a `TimelineView(.periodic 1 s)`); remaining format `h min` style: `"%d h %02d min"` / `"%d min %02d s"` / `"%d s"`;
   * running without end (manual) or other: **"Active until you turn it off"**;
   * running by automation: the automation status: **"Active while an external display is connected"** / **"Active while connected to power"** / **"Active while a selected app is running"** (single condition) or **"Active because an automatic condition is met"**;
   * not running: **"The Mac follows its normal energy rules"**.
   Switch on → `startLastPick()` (restarts the last pick: the saved "until" time if still ahead, else the saved default duration); off → `toggle()` (ends the session; if the session was automatic or conditions currently hold, automation is suppressed until conditions clear).
2. `VStack(spacing 8)`: label **"Default duration"** (`subheadline.weight(.medium)`) and an adaptive grid (`columns: adaptive(min 84), spacing 6`) of 7 chips in this order: **"15 minutes", "30 minutes", "1 hour", "2 hours", "4 hours", "8 hours", "Indefinite"** (minutes 15, 30, 60, 120, 240, 480, 0). Selected = `defaultDuration == minutes`. Click: sets `defaultDuration` and `keepAwakeSwitchUsesUntil = false` ("a chosen default is the newest pick").
3. `Divider`
4. `VStack(spacing 8)`: **"Automation"** (`subheadline.weight(.medium)`), caption (`.caption` secondary) **"Starts when any selected condition is active."** (or **"Starts only when every selected condition is active."** when `keepAwakeAutomationRequireAll`), then `KeepAwakeAutomationEditor`:
   * three condition tiles in an `HStack(alignment .top, spacing 8)`: **"External display"** (`display`), **"Power"** (`powerplug.fill`), **"Applications"** (`app.fill`). Tile: `VStack(alignment .leading, spacing 7)` padding 9, min height 78, corner 11; top row: symbol `system(17, semibold)` (accent when selected, secondary otherwise) + checkmark.circle.fill (13) when selected; title `system(12, medium)` max 2 lines; fill selected accent @11% / unselected primary @4.5%; stroke selected accent @32% / unselected primary @8%. Toggling a tile flips `keepAwakeExternalDisplay` / `keepAwakeConnectedToPower` / `keepAwakeRunningApps` and calls `automationPreferencesDidChange()`.
   * if more than one tile is selected: segmented picker **"Any" / "All"** (`keepAwakeAutomationRequireAll`, false = Any).
   * if "Applications" is selected: an `AppBundleList` titled **"Selected apps"**, caption **"Keep Awake starts while any of these apps is open, even in the background."**, add button **"Add an app…"**, remove label **"Remove"**; stored in `keepAwakeRunningAppBundleIDs` (sanitised, de-duplicated list).
5. `SettingsRow(symbol lock.fill, title "Pause while the Mac is locked", caption "Follows normal sleep rules while locked and resumes the remaining session after you unlock.")` + Switch `keepAwakePauseWhenLocked`.
[WIN] "Mac" → "PC" in "Pause while the PC is locked" etc.

#### Card 2 `keepAwakeOptionsCard` (title **"Options"**), rows in order
1. `play.circle` **"Keep Awake when WinKnife opens"** — **"Starts a session with the default duration."** — Switch `keepAwakeAutoStart`.
2. `cursorarrow.click.2` **"Right-click the menu bar icon to toggle Keep Awake"** [WIN: "tray icon"] — **"Replaces the right-click context menu."** — Switch `keepAwakeRightClickToggle`.
3. `timer` **"Show remaining time next to the icon"** (no caption) — Switch `showCountdownInMenuBar`.
4. `display` **"Allow the display to sleep"** — **"Keeps the Mac awake while the display follows its normal sleep timer."** — Switch `keepAwakeAllowDisplaySleep`.
5. `cursorarrow.motionlines` **"Move pointer slightly"** — **"During a session, moves the pointer a little at the chosen interval."** — Switch `keepAwakeMouseJiggleEnabled`.
   If on, indented 38: `HStack`: **"Interval"** • spacer • small menu picker with **"1 min", "2 min", "5 min", "10 min", "15 min"** (`keepAwakeMouseJiggleInterval`, default 5); and a `PermissionRow(.accessibility)` if Accessibility is missing [WIN: none].
6. `Divider`; `KeepAwakeIconPicker` (non-compact): header row **"Active icon"** (`system(12, medium)` secondary) with the current choice name at right (`system(11)` tertiary); row of 5 cells (spacing 7, each full-width, height 35, corner 7): **Vorssaint/WinKnife mark** (title = "WinKnife"; if a custom menu-bar symbol is set, the first cell's tooltip is **"Menu bar icon"**), **"Coffee"** (`cup.and.saucer.fill`), **"Eye"** (`eye.fill`), **"Moon"** (`moon.fill`), **"Lightbulb"** (`lightbulb.fill`); glyph max 21x16, rendered in the chosen tint (original colours) or as template when tint = none. Second header **"Active icon color"** with current colour name; row of 6 swatch buttons (spacing 8, height 28, corner 7; 14pt dot): **"Orange"** (default), **"Green"**, **"Blue"**, **"Purple"**, **"Pink"**, **"No color"** (outlined ring with a 45° slash). Selected = accent @12% fill + 1.1px accent @70% stroke.
7. If the machine has a battery: `Divider`; `battery.25percent` **"Disable when battery drops below"** — **"Keeps a forgotten session from draining the MacBook battery."** [WIN: "...draining the laptop battery."] — menu picker: **"Never"** (0), **"5%"**, **"10%"** (default), **"15%"**, **"20%"** (`batteryLimitPercent`).
8. `Divider`; `laptopcomputer` **"Keep going with the lid closed"** — caption: **"Configuring…"** while setting up, else **"“Keep going with the lid closed” fully disables sleep while “Keep awake” is active and is reverted automatically when the session ends or the app quits. Prefer using it plugged in."** — Switch bound to `clamshellPreferred` (disabled while setup is in progress). Failure: red caption (indent 38) **"Couldn’t turn on closed-lid mode. Try again."** When on: indented 38 `SettingsRow(sun.min, "Dim the display to zero", "Dims the display when the lid closes and brings the brightness back when it opens.")` + Switch `dimScreenOnLidClose`.

#### Settings (Keep awake)
| Key | Type | Default | Allowed | Effect |
|---|---|---|---|---|
| `defaultDurationMinutes` (`defaultDuration`) | Int | 0 | 0, 15, 30, 60, 120, 240, 480 | Default session length; 0 = until switched off |
| `keepAwakeSwitchUsesUntil` | Bool | false | - | Switch restarts the saved end time instead of the duration |
| `keepAwakeUntilTime` | Double (ref-date seconds) | 0 | - | Saved "until" end time |
| `keepAwakeAutoStart` | Bool | false | - | Start a session at launch |
| `keepAwakeRightClickToggle` | Bool | false | - | Right click on tray icon toggles |
| `showCountdownInMenuBar` | Bool | false | - | Remaining time text beside the icon |
| `keepAwakeAllowDisplaySleep` | Bool | false | - | Release the display assertion |
| `keepAwakeMouseJiggleEnabled` | Bool | false | - | Pointer nudges during a session |
| `keepAwakeMouseJiggleIntervalMinutes` | Int | 5 | 1, 2, 5, 10, 15 | Nudge interval |
| `keepAwakeActiveIcon` | String | `vorssaint` (→ WinKnife mark) | `vorssaint, coffee, eye, moon, light` | Tray icon while active |
| `keepAwakeIconTint` | String | `orange` | `orange, green, blue, purple, pink, none` | Colour of the active icon |
| `batteryLimitPercent` | Int | 10 | 0, 5, 10, 15, 20 | End a session on battery at/below this % |
| `clamshellPreferred` | Bool | false | - | Closed-lid mode while a session runs |
| `dimScreenOnLidClose` | Bool | false | - | Dim built-in display to 0 on lid close |
| `keepAwakeExternalDisplay` / `keepAwakeConnectedToPower` / `keepAwakeRunningApps` | Bool | false | - | Automation conditions |
| `keepAwakeRunningAppBundleIDs` | [String] | [] | - | Apps for the "Applications" condition |
| `keepAwakeAutomationRequireAll` | Bool | false | - | false = Any, true = All |
| `keepAwakePauseWhenLocked` | Bool | false | - | Release assertions while locked, resume after unlock |

#### Behaviour (from `KeepAwakeManager`)
* A session holds two power assertions: system idle-sleep prevention always, display idle-sleep prevention unless `keepAwakeAllowDisplaySleep` (toggling the option updates the assertions live).
* End: a timer at the end date; when it fires, if the session is manual and automation conditions currently hold (all conditions satisfied per Any/All, battery protection allows) it silently converts to an automatic session instead of ending. `extend(minutes)` adds time.
* Battery watch: every 30 s (tolerance 5 s); if on battery and percent <= limit, the session ends (also checked at start). Automatic sessions don't start when blocked by battery protection.
* Mouse jiggle: timer = interval (tolerance min(10 s, 10%)); each tick moves the pointer by +1 px in x on the display containing it (fallbacks -1 x, +1 y, -1 y, within a 2-px inset of the display bounds), then 80 ms later moves it back only if the pointer is still within 2 px of the jiggled spot; skipped when paused for lock.
* Pause when locked: while the screen is locked the assertions/battery watch/jiggle/clamshell are released and restored on unlock; the end time keeps running.
* Automation: monitors display count, power source and running apps; "external display" = any online display that isn't built-in; Any = at least one enabled condition matches, All = all enabled conditions match; stopping an automatic session by hand suppresses it until conditions clear or the automation preferences change.
* Clamshell: runs `pmset disablesleep` through a sudoers rule (password asked once); reverted at session end/quit/relaunch recovery; dim-to-zero saves the built-in display brightness (`dimmedDisplaySavedBrightness`) on lid close and restores it (up to retries every 0.5 s) on lid open.
* Leaving the Features hub ends any running session; settings stay saved.

#### Windows mapping
* Assertions: `SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED [| ES_DISPLAY_REQUIRED])` — already in `KeepAwakeFeature.cs`. It must be called from one dedicated long-lived thread (the call is per-thread); alternative `PowerCreateRequest/PowerSetRequest(PowerRequestSystemRequired / PowerRequestDisplayRequired)` which is process-wide and avoids the thread pitfall (preferred).
* Closed lid ("clamshell"): **no sudoers needed**: change the active power scheme's lid action with `powercfg /setacvalueindex SCHEME_CURRENT SUB_BUTTONS LIDACTION 0` and `/setdcvalueindex ... 0` then `powercfg /setactive SCHEME_CURRENT` (or `PowerWriteACValueIndex/PowerWriteDCValueIndex` with GUID_LIDCLOSE_ACTION = `5ca83367-6e45-459f-a27b-476b1d01c936`, value 0 = do nothing), remembering the original values in settings (`clamshellSavedAc/Dc`) and restoring on session end/exit/next start (crash recovery). Normally no UAC for the current user's scheme; fall back to an elevated helper only if `PowerWrite*` returns ERROR_ACCESS_DENIED. Drop the "administrator password" strings (`clamshellNeedsPassword`, `clamshellReady`); keep "Configuring…" and the failure text.
* Dim display to zero on lid close: `RegisterPowerSettingNotification(GUID_LIDSWITCH_STATE_CHANGE)`; on close save brightness (WMI `WmiMonitorBrightnessMethods.WmiSetBrightness` for the built-in panel) and set 0, restore on open (reuse `WinKnife.Features.System/Display`).
* Mouse jiggle: `SendInput` with `MOUSEEVENTF_MOVE` (+1 px) and back after 80 ms (check `GetCursorPos` within 2 px); no permission.
* Pause when locked: `WTSRegisterSessionNotification` → `WM_WTSSESSION_CHANGE` (`WTS_SESSION_LOCK`/`UNLOCK`).
* Automation: external display = `EnumDisplayMonitors`/`GetSystemMetrics(SM_CMONITORS) > 1` or `QueryDisplayConfig` outputs that are not internal (`DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL/DISPLAYPORT_EMBEDDED`); power = `GetSystemPowerStatus.ACLineStatus` with `SystemEvents.PowerModeChanged`; running apps = process snapshot by exe path (replace bundle IDs with exe full paths or file names; "Add an app…" = file dialog for `.exe` or a running/installed-app list from `HKLM/HKCU\...\Uninstall` + Start-menu shortcuts).
* Battery limit row only when `GetSystemPowerStatus.BatteryFlag != 128`.
* Active icon: the tray icon swaps to a tinted glyph/bitmap while active (see 3.1 tray art); coffee glyph has no Segoe glyph (use Segoe UI Symbol U+2615 or bundled art); tint colours: orange `#FF9500`, green `#34C759`, blue `#0A84FF`, purple `#BF5AF2`, pink `#FF375F` (macOS system colours; adapt to Windows accent-friendly equivalents).
* Right-click toggle: `NotifyIcon.MouseClick` right button.
* Countdown next to icon: a Windows tray icon cannot show text beside it; render the remaining time (e.g. "12m") *into* the icon bitmap, or show it in the tooltip only.

#### Gap vs WinKnife
`KeepAwakeFeature.cs` has: a toggle card, a duration choice (0/15/30/60/120/240/480 as a combo "Turn off automatically after"), "Also keep the display on" (inverse of `keepAwakeAllowDisplaySleep`, key `keepDisplayOn`), a 1 s status text and 4 quick actions. **Missing:** chip grid, default-duration semantic (`defaultDuration`/`keepAwakeSwitchUsesUntil`/until-time), automation (external display / power / apps, Any/All), pause when locked, auto start, right-click toggle, countdown beside icon, mouse jiggle, active-icon + tint picker, battery limit, closed-lid mode and dim-to-zero, status caption wording, global shortcut, "Options" card, `SettingsRow` look. Key names differ (`minutes`, `keepDisplayOn`) → migrate to the Vorssaint key names above.

### 4.2 Displays (focus brightness) — one card `displaysCard` (no title)

1. `SettingsRow(display.2, "Control displays", "Brightness and on or off controls for the built-in and external displays, here and in the menu bar panel.")` + Switch `brightnessControlEnabled` (default false); change calls `BrightnessService.syncWithPreferences()`. [WIN] "menu bar panel" → "tray panel".
2. If enabled: `Divider`, then
   * no displays: caption **"No display found."**; else one **display row** per display (`HStack spacing 12`): 26x26 tile (symbol `laptopcomputer` for built-in / `display` for external; accent when the display is active else secondary; tile fill same @12%) • name (1 line, middle-truncation, min width 80) • if active and controllable: slider 0…1 (disabled while a change is pending, label = display name) + percent `"%d%%"` in monospaced body font (width 52, trailing); else spacer, and for a switched-off display the text **"Off"** (secondary, width 52) • `SoftwareDimmingButton` • `DisplayPowerButton`.
   * `SoftwareDimmingButton`: shown for active, non-built-in displays that can choose dimming: label **"Extra dimming"** (when DDC is readable and software dimming isn't chosen) or **"Dim the picture"** (write-only DDC / software chosen); symbol `circle.lefthalf.filled`, becomes `checkmark.circle.fill` + accent when chosen; `system(12, medium)` + icon 11; offered in Settings for readable-DDC displays (extra dimming goes below the hardware minimum), and for write-only DDC displays (fallback to software). Disabled while pending.
   * `DisplayPowerButton`: icon-only, `power` (secondary) for an active display / `power.circle.fill` (green) for an off one; frame 20x18; tooltip: **"Turn off display"** / **"Turn on display"**; disabled tooltips: **"At least one display must stay on."** (last active) or **"Display switching is unavailable on this Mac."**; shows a spinner while pending.
   * failure text (red `.caption`, wraps), when present: **"Display switching is unavailable on this Mac."** / **"At least one display must stay on."** / **"Could not change this display."** / **"Open the lid to turn on the built-in display."**
   * `DisclosureGroup` "**More options**" (label `subheadline.weight(.medium)`; collapsed by default; content `VStack(spacing 13)`, padding-top 10):
     1. `cursorarrow.rays` **"Brightness keys follow the pointer"** / **"The keyboard brightness keys change the display under the pointer."** + Switch `brightnessKeysEnabled` (default false; turning on requests Accessibility and re-syncs).
     2. `sun.min` **"Brightness key steps"** / **"Smaller steps for each press of the brightness keys and display brightness shortcuts."** + menu picker: **"Standard"** (`standard`, default), **"Half steps"** (`half`), **"Quarter steps"** (`quarter`) (`brightnessKeyStep`; non-standard requests Accessibility).
     3. `DisplayBrightnessShortcutControls(showsSettingsRow: true)`: `keyboard` **"Use display brightness shortcuts"** / **"Shortcuts adjust the primary display, or the display under the pointer when pointer following is on."** + Switch `displayBrightnessShortcutsEnabled` (default false). When on, two `ShortcutPreferenceRow`s indented 38: **"Decrease display brightness"** (default `shift+command:27` = ⇧⌘−) and **"Increase display brightness"** (`shift+command:24` = ⇧⌘=) (with "Reset" buttons and conflict checks incl. window-layout shortcuts); red caption **"macOS rejected this shortcut. Choose another one."** when registration failed.
     4. (only if the Mac supports the brightness OSD) `sun.max` **"Show brightness when adjusting"** / **"Shows the brightness percentage when you use the brightness keys or sliders."** + Switch `brightnessOSDEnabled` (default false; turning on requests Accessibility).
     5. `PermissionRow(.accessibility)` when keys, OSD or non-standard step is on and Accessibility is missing [WIN: none].
     6. Caption: **"External displays are adjusted through the same protocol as their own buttons. When the connection cannot carry it, as with HDMI adapters, the slider dims the picture instead, so brightness control works either way."**

Settings: `brightnessControlEnabled` false; `brightnessKeysEnabled` false; `brightnessKeyStep` `standard` (`standard, half, quarter`; fractions of the base step); `brightnessOSDEnabled` false; `displayBrightnessShortcutsEnabled` false; `displayBrightnessDecreaseShortcut` `shift+command:27`; `displayBrightnessIncreaseShortcut` `shift+command:24`; `keyboardBrightnessShortcutsEnabled` false (+ `keyboardBrightnessDecreaseShortcut` `option+command:27`, `keyboardBrightnessIncreaseShortcut` `option+command:24`, shown only on Shortcuts page); per-display `extendedDimmingPreferred`/`softwareDimmingPreferred` sets.

Behaviour: sliders write brightness live (OSD optional); DDC/CI pacing (write pause 10 ms, read 50 ms, retry 20 ms, 4 retries, VCP 0x10) with software dimming as fallback; displays rescan on appear and on topology change/wake; power toggle uses the display-switching private API; mirrored displays handled.

Windows mapping: built-in: WMI `root\wmi WmiMonitorBrightness`/`WmiMonitorBrightnessMethods.WmiSetBrightness`; external: DDC/CI via `dxva2` (`GetPhysicalMonitorsFromHMONITOR`, `GetMonitorBrightness/SetMonitorBrightness`, VCP 0x10 via `GetVCPFeatureAndVCPFeatureReply`) — **already implemented** (`Features.System/Display/DisplayDevices.cs`: `WmiDisplay`, `DdcDisplay`). Software dimming ("Dim the picture"/"Extra dimming"): click-through black `WS_EX_LAYERED|WS_EX_TRANSPARENT` overlay per monitor with alpha, or `SetDeviceGammaRamp` (limited range). Per-display power on/off: `SetDisplayConfig` removing/adding the path (extend ↔ disabled) or, for "all displays", `SendMessage(HWND_BROADCAST, WM_SYSCOMMAND, SC_MONITORPOWER, 2)`; the "last display must stay on" rule stays. "Brightness keys follow the pointer": Fn brightness keys on Windows never reach `WH_KEYBOARD_LL` on most laptops (handled in firmware/driver) → **not implementable generally**; show the row only when a key event is observed or hide it; the display-brightness *shortcuts* (RegisterHotKey) are the working equivalent; "Brightness key steps" applies to those shortcuts only. OSD: existing `Shared/Osd.cs`. Accessibility permission rows never shown. Page also shows "Brightness" panel tab only when `brightnessControlEnabled`.
Gap: `DisplaysFeature.cs` provides a flyout (`TogglePanel`), two hotkeys (`upHotkey`/`downHotkey`, no default), `step` slider 2…25 (%) default 10, and a note; **missing**: the page structure, enable switch (feature = installed), display rows with sliders in settings, power buttons, software/extended dimming, key-step options, OSD toggle, shortcut defaults (⇧⌘− / ⇧⌘= → e.g. Ctrl+Shift+−/=), keyboard-light shortcuts, "More options" disclosure, failure messages.

### 4.3 Extra brightness (focus extraBrightness) — one card

* If the main display is an XDR panel: `SettingsRow(sun.max.fill, "Extra brightness", "Uses the display’s HDR headroom to go past the maximum brightness. Uses more battery and the Mac can run warm.")` + Switch `extraBrightnessEnabled` (default false). When on: indented 38 `HStack(spacing 12)`: label **"Intensity"** • slider 10…100 step 5 (`extraBrightnessLevel`, default 100) • `"%d%%"` monospaced (width 52, trailing).
* Else: only the row with caption **"Available only on XDR displays, such as the ones on the 14 and 16 inch MacBook Pro."**, no control.
* Behaviour: click-through multiply-filter Metal overlay windows covering the screen make macOS engage the panel's HDR headroom; it starts small and ramps up (+0.03 factor per tick, ramp-down share 0.25, min step 0.01, 3-tick dropout grace), engagement factor 1.10, headroom threshold 1.05, capability floor 2.05; the overlay dies with the app; thermal pressure can shrink headroom. Nothing runs while off.
* **macOS-only.** Windows closest equivalent: on HDR-enabled displays raise the **SDR content brightness** (white level) above the SDR maximum: `DisplayConfigSetDeviceInfo` with the (undocumented) set type `0xFFFFFFEE` (`DISPLAYCONFIG_DEVICE_INFO_SET_SDR_WHITE_LEVEL`, payload `SDRWhiteLevel = nits/80*1000`, 80…480 nits) / read via the documented `DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL` (type 11); map Intensity 10–100% to 80–480 nits. Show the unsupported caption (reworded `[WIN]` "Available only on displays with HDR turned on in Windows Settings.") when no HDR display is active. Flag the SET call as undocumented: ask before shipping.
* Gap: not present in WinKnife.

### 4.4 Bluetooth on sleep (focus bluetoothSleep) — one card
* If the Mac has a Bluetooth controller: `SettingsRow(dot.radiowaves.left.and.right, "Turn Bluetooth off when the Mac sleeps", "Bluetooth already off before sleep is left alone and stays off on wake.")` + Switch `bluetoothSleepEnabled` (default false). When on: `SettingsRow(arrow.counterclockwise, "Turn Bluetooth back on when the Mac wakes", "Only when WinKnife was the one that switched it off.")` + Switch `bluetoothSleepRestoreOnWake` (default true).
* Else: `SettingsRow(dot.radiowaves.left.and.right, "Bluetooth on sleep", "This Mac has no Bluetooth controller.")` (no control). [WIN] "Mac" → "PC".
* Behaviour: on will-sleep, if powered on: power off and record `bluetoothSleepRestorePending = restoreOnWake`; on wake (and at every launch, even if the feature has since been turned off) pay the debt: if pending and Bluetooth is currently off, power it on; the pending flag is always cleared (Bluetooth the user switched on meanwhile cancels the debt).
* Windows mapping: `Windows.Devices.Radios.Radio.GetRadiosAsync()` → `RadioKind.Bluetooth` → `SetStateAsync(RadioState.Off/On)` (needs `radios` capability / user consent; works for unpackaged desktop apps with `RequestAccessAsync`). Sleep/wake: `SystemEvents.PowerModeChanged` (Suspend/Resume) or `WM_POWERBROADCAST` (`PBT_APMSUSPEND/RESUMEAUTOMATIC`); caveat: the suspend notification gives ~2 s, so issue `SetStateAsync` immediately and don't await long; on Modern Standby PCs there is no hard suspend — use display-off/`GUID_CONSOLE_DISPLAY_STATE` or `GUID_LOW_POWER_EPOCH` as the trigger. Persist the pending flag like Vorssaint. "No controller" = no `RadioKind.Bluetooth` radio.
* Gap: not present in WinKnife.

---------------------------------------------------------------------------------------------------------------

## 5. Monitor page (`MonitorSettings.swift`, `MonitorPanelConfig.swift`, `MonitorAlertsControls.swift`, `MonitorToken.swift`)

### 5.1 Layout (top to bottom)
The page is `VStack(spacing 0)` (non-lazy on purpose): a **pinned preview strip above the scroll view**, a `Divider`, then the `ScrollView`.
0. **Pinned strip** (max width 760 centred, padding h22 v10): caption `Text("Menu bar preview")` (`.caption` secondary) over `MenuBarMetricsPreview`: a 32-high bar, corner 8, fill `black @82%`, content right-aligned, h-padding 14, spacing 12, font 12: `wifi` (white @50%), `battery.75` (white @50%, only if a battery exists), then (a) when *Separate metrics* is on: one block of lines per status group (grouped by the same grouping the real items use), then the app glyph; (b) otherwise the app glyph (hidden when *Hide the app icon…* is on and metrics are shown) + the stacked metric lines (`MenuBarRenderer.lines`), updated live from the sampler and from every key below.
1. Page header: title **"Monitor"**, description **"What the menu bar and the panel show about your Mac, and when to warn you."** [WIN: "...your PC..."; keep "menu bar" → "tray/taskbar" decision as in 3.1].
2. **Group heading** `Divider` + `Label("In the menu bar", menubar.rectangle)` (`title3.weight(.semibold)`, top padding 8, header trait).
3. **Card "menuBarCard"** (no title): callout secondary **"Choose what appears next to the icon in the menu bar."**; `MenuBarMetricTiles` (below); caption **"Drag to reorder. Click the checkmark to show or hide."**.
4. **Card "menuBarStyleCard"** title **"Usage display"**:
   * two `MenuBarStyleChoice` cells side by side (spacing 10): **"Values"** and **"Bars"**. Cell: `VStack spacing 8`, padding 8, corner 12, fill (selected accent @10% / `secondary @5%`) + 1px accent @55% stroke when selected; top mock 36 high on `black @82%` corner 8 showing two mini readouts: *Values*: stacked label (`system(6.6, medium)`, white) over value (`system(12, semibold, monospaced)`) for "CPU 42%" and "RAM 61%"; *Bars*: vertical letter stack of the label (`system(6.1, semibold)`, line spacing −1.8) beside a 9x18 rounded outline (corner 2.2, 1.15px white) with a fill of `#64D2FF`-like (`rgb(.39,.82,1.0)`) 4.8 wide, height 13.8·fraction (0.42 / 0.61), bottom padding 2.1; label under the mock `system(11, medium)`, accent when selected.
   * caption **"Bars apply to CPU, GPU, memory and disk usage. Other readings stay numeric."**
   * if Bars: `MenuBarUsageBarSettings`: subheadline-semibold **"Bar colors and limits"**; three color pickers (no opacity): **"Normal color"** (`#64D2FF`), **"Medium color"** (`#FFD60A`), **"High color"** (`#FF453A`); `Divider`; two steppers **"Medium from"** (1…99, default 70, shows "70%", must stay < High) and **"High from"** (2…100, default 90, must stay > Medium) — setting one clamps against the other (`medium = min(max(1,v), max(1, high−1))`, `high = min(100, max(medium+1, v))`); values sanitised on appear.
   * else: `SettingsRow(thermometer.medium, "Combine usage and temperature", "When usage and temperature for the same item are enabled, show them in one block.")` + Switch `menuBarCombineTemperatures` (default true).
   * `Divider`
   * `SettingsRow(arrow.left.and.right, "Menu bar spacing")` + segmented **"Standard"** / **"Compact"** (`menuBarMetricSpacing`; registered default **compact**).
   * `SettingsRow(eye.slash, "Hide the app icon while metrics are shown", "The icon returns by itself when metrics leave the bar and when there is something to signal (an update ready, “Keep awake” running or the microphone muted).")` + Switch `menuBarHideIconWithMetrics` (false).
   * `SettingsRow(rectangle.split.3x1, "Separate metrics into their own items", caption (only in Values mode) "Separates active blocks in the menu bar and keeps usage and temperature together when combine is on.")` + Switch `menuBarSeparateMetrics` (false).
5. **Group heading** `Label("In the panel", macwindow)`.
6. **Card "panelCard"** (no title): `MonitorPanelConfig(tiles: true)` (5.4); caption **"Open a block to choose what it shows."**; `SettingsRow(arrow.up.to.line, "Show graph scale")` + Switch `monitorGraphScale` (default true).
7. **Fan card** (only if Fan Control is installed; scroll anchor `fanControl`): `SettingsRow(fanblades.fill, "Fan Control", badge "BETA", caption "Adds manual fan speeds and temperature curves to the menu bar panel.")` + Switch (label "Show Fan Control in the panel") bound `panelShowFanControl` (default true); under it indented 38 caption **"Beta. You may run into some bugs."**
8. **Group heading** `Label("Readings and alerts", slider.horizontal.3)`.
9. **Card "readingsCard"** (no title):
   * `SettingsRow(timer, "Update every")` + 3 chips **"1 second"**, **"2 seconds"**, **"5 seconds"** (`monitorIntervalSeconds`, default 2; allowed 1, 2, 5).
   * `SettingsRow(thermometer.medium, "Temperatures")` + segmented **"°C"** / **"°F"** (`temperatureUnit` `celsius`/`fahrenheit`, default celsius).
   * if Network monitor installed: `SettingsRow(network, "Network speed unit")` + segmented **"MB/s"** / **"Mbps"** (`networkSpeedUnit` `bytes`/`bits`, default bytes).
   * if Memory monitor installed: `SettingsRow(memorychip, "Measure memory as")` + segmented **"Memory Used"** / **"App Memory"** (`monitorMemoryMetric` `used`/`app`, default `used`).
10. **Card "alertsCard"** title **"Alerts"**: `MonitorAlertsControls(compact: false)` (5.5).

### 5.2 Menu-bar tiles (`MenuBarMetricTiles`)
`LazyVGrid(columns: adaptive(min 196), spacing 8)` inside an inset group box (`padding 10`, `secondary @5%`, corner 10). One `MonitorToken` (small) per metric in the **saved order**, filtered to metrics whose hub feature is installed and available on this hardware (battery metrics need a battery; fan needs fan telemetry). Drag any tile to reorder (live reordering; writes `menuBarMetricOrder` only when the user drags). Click the checkmark to show/hide (writes that metric's key immediately so the pinned preview updates). Default order and tiles:

| Metric id | Key (all default false) | Tile title | Symbol | Hub feature |
|---|---|---|---|---|
| cpu | `menuBarCPU` | CPU | cpu | monitorCPU |
| cpuTemperature | `menuBarCPUTemperature` | CPU temperature | cpu | monitorCPU |
| gpu | `menuBarGPU` | GPU | rectangle.connected.to.line.below | monitorGPU |
| gpuTemperature | `menuBarGPUTemperature` | GPU temperature | rectangle.connected.to.line.below | monitorGPU |
| memory | `menuBarMemory` | Memory | memorychip | monitorMemory |
| battery | `menuBarBattery` | Battery | battery.100 | monitorPower |
| batteryTime | `menuBarBatteryTime` | Battery time remaining | clock | monitorPower |
| batteryTemperature | `menuBarBatteryTemperature` | Battery temperature | battery.100 | monitorPower |
| peripheralBattery | `menuBarPeripheralBattery` | Peripheral battery | keyboard | monitorPower |
| network | `menuBarNetwork` | Network | network | monitorNetwork |
| diskUsage | `menuBarDiskUsage` | Disk usage | internaldrive | monitorDisk |
| diskActivity | `menuBarDiskActivity` | Live activity | internaldrive.fill | monitorDisk |
| connectedDevices | `menuBarConnectedDevices` | Connected Devices | cable.connector | connectedDevices |
| power | `menuBarPower` | Power | powerplug.fill | monitorPower |
| fanSpeed | `menuBarFanSpeed` | Fan speed | fanblades | fanControl |
`menuBarMetricOrder` = comma-joined ids, default the order above; sanitiser keeps known ids, removes duplicates, appends missing ones, and expands a legacy `temperature` id into cpuTemperature,gpuTemperature,batteryTemperature.
Per-tile options (second half of the token, opens a popover):
* **Memory**: option row `circle.fill` (green tint) **"Pressure dot"** (switch; `menuBarMemoryStyle` `percent` (default) ↔ `both`; allowed `dot, percent, both`); summary text shown on the tile = "Pressure dot" when style != percent, else "More options".
* **Network**: option row `arrow.up.arrow.down` **"Upload above download"** (`menuBarNetworkUploadFirst`, false); summary "↑ ↓" or "↓ ↑".
* **Disk usage**: a menu picker **"Disk display"** with **"Used percentage"** (`percent`, default), **"Available space"** (`free`), **"Used space"** (`used`) (`menuBarDiskStyle`); summary = the chosen label.
* other metrics: no options half.

### 5.3 `MonitorToken` component
Compact label for one item, checked and tinted when shown. `ViewThatFits(horizontal)`: first variant shows the options summary beside the name on one line, second hides the summary (name may wrap to 2 lines, scale ≥ 0.8) keeping only a chevron. Font `system(12, regular)` (large: `system(13, medium)`, icon scale large), min height 28 (large 40), `maxWidth ∞`, corner 8 (large 10); background: on = accent @12%, off = `secondary @7%`; `selected` adds 1px accent @70% stroke. Layout: `HStack(spacing 0)` { left part `HStack spacing 4`, padding h9 v4: **check button** (`checkmark.circle.fill` accent when on / `circle` secondary; toggles `included`), **name button** (`Label(title, symbol)`, primary when on / secondary when off; click = `select` action if provided else toggles `included`), tapping the padding area also activates; if `options` exist: `Divider` (14 high) + **options button**: [summary text 1 line] + `chevron.down` (7 bold), accent when on else secondary, padding h8, disabled when the item is off; opens a popover (arrow below, width 250, padding 14): headline `Label(title, symbol)` then the options views with switch style}. Unavailable items: opacity 0.5 and disabled. Tooltip = title. Accessibility: check button label = title, trait selected when included; options button label "title: summary".
`MonitorTokenOption`: `HStack(spacing 8)`: 16-wide icon (11pt, tint or secondary) • title (wraps) • spacer • small Switch.
Grid column definitions: small tokens `adaptive(min 196, spacing 8)`; large tokens `adaptive(min 210, spacing 10)`; block tiles `adaptive(min 175, spacing 10)`.

### 5.4 `MonitorPanelConfig(tiles: true)` — what the panel shows
`VStack(spacing 10)`:
1. A grid (`adaptive(min 175)`, spacing 10) of **block tiles** (large `MonitorToken`; check = master "show in panel", click name = select the block; selected block has the accent stroke), one per available block in this order: **System** (`cpu`), **Network** (`network`), **Disks** (`internaldrive`), **Power** (`bolt.fill`), **Volume mixer** (`speaker.wave.2`). Master keys (all default true): `monitorShowSystem`, `monitorShowNetwork`, `monitorShowDisk`, `monitorShowPower`, `monitorShowMixer`. The first available block is selected initially.
2. For the selected block (not the mixer, which has no sub-items): heading `Text(block title)` (`subheadline.weight(.medium)`) and a grid of **item tiles** (large tokens, `adaptive(min 210, spacing 10)`) in an inset group box; tiles grey out (opacity .5, disabled) while the block master is off. Items (title • symbol • key default • options):
   * **System**: "Temperatures" • thermometer.medium • `monitorSysTemps` true (only if CPU or GPU monitor installed); "CPU" • cpu • `monitorSysCPU` true • options: **"Graphs"** (`chart.xyaxis.line`, `monitorGraphCPU` true) and **"Per core"** (`square.grid.3x2`, `monitorSysCPUCores` true) — summary lists the names of the ones on joined by " · ", else "More options"; "GPU" • rectangle.connected.to.line.below • `monitorSysGPU` true • option Graphs (`monitorGraphGPU`); "Memory" • memorychip • `monitorSysMemory` true • option Graphs (`monitorGraphMemory`); "Uptime" • clock • `monitorSysUptime` true; "Connected Devices" • cable.connector • `monitorSysConnectedDevices` true.
   * **Network**: "Live speed" • speedometer • `monitorNetSpeed` true • option Graphs (`monitorGraphNetwork`); "Apps using network" • app.badge • `monitorNetApps` true; "Session totals" • sum • `monitorNetTotals` true; "IP addresses" • network • `monitorNetAddresses` true; "Speed test" • gauge.with.needle • `monitorNetTest` true.
   * **Disks**: "Disk usage" • internaldrive • `monitorDiskUsage` true; "Live activity" • arrow.up.arrow.down • `monitorDiskActivity` true • option Graphs (`monitorGraphDisk`); "SMART" • heart.text.square • `monitorDiskSMART` true; "External protection" • shield • `monitorDiskProtection` true; "Tools" • wrench.and.screwdriver • `monitorDiskTools` true.
   * **Power**: "System" • bolt • `monitorPwrSystem` true • option Graphs (`monitorGraphPower`); "Adapter" • powerplug • `monitorPwrAdapter` true; (battery machines only) "Charge" • battery.75percent • `monitorSysBattery` true • option Graphs (`monitorGraphBattery`); "Battery" • battery.100percent.bolt • `monitorPwrBattery` true; "Battery time remaining" • clock • `monitorPwrTimeRemaining` true; "Battery temperature" • thermometer.medium • `monitorPwrTemperature` true; "Battery health" • heart • `monitorPwrHealth` true; "Peripheral battery" • keyboard • `monitorPwrPeripherals` **false** (always listed).
   * Graph toggles (all default true): `monitorGraphCPU, monitorGraphGPU, monitorGraphMemory, monitorGraphNetwork, monitorGraphDisk, monitorGraphPower, monitorGraphBattery`.
3. The same choices are also drawn as expandable rows in onboarding (`rowLayout`: per block a `DisclosureHeaderRow`, a "Show in panel" master toggle (**"Show in panel"**) and per-item toggles; the Power block has a "More options" disclosure containing "Battery temperature" and "Battery health"; the Mixer is a single toggle "Volume mixer"). Same keys.

### 5.5 `MonitorAlertsControls(compact: false)` (Alerts card)
`VStack(spacing 12)`:
* inset group box with `LazyVGrid(adaptive(min 196), spacing 8)` of alert tokens (`MonitorToken`, check + name + optional limit half; limit popover contains a `Stepper` whose text is "<label> <value>" monospaced digits; summary on the tile = the value):

| Alert (title) | Symbol | Key (default off) | Limit label | Key | Default | Range / step | Shown when |
|---|---|---|---|---|---|---|---|
| High CPU | cpu | `monitorAlertCPU` | "CPU above" | `monitorAlertCPUThreshold` | 90 | 50…100 / 5 (%) | CPU monitor installed |
| High CPU temperature | thermometer.medium | `monitorAlertCPUTemperature` | "Temperature above" | `monitorAlertCPUTemperatureThreshold` | 90 | 70…105 / 5 (shown in °C or °F) | CPU monitor installed |
| Critical memory pressure | memorychip | `monitorAlertMemory` | (no limit) | - | - | - | Memory monitor installed |
| Low disk space | internaldrive | `monitorAlertDisk` | "Free space below" | `monitorAlertDiskFreePercent` | 10 | 5…30 / 5 (%) | Disk monitor installed |
| High battery temperature | thermometer.high | `monitorAlertBatteryTemperature` | "Temperature above" | `monitorAlertBatteryTemperatureThreshold` | 40 | 30…50 / 5 (°) | Power monitor + battery |
| Low battery | battery.25percent | `monitorAlertBattery` | "Battery below" | `monitorAlertBatteryPercent` | 15 | 5…50 / 5 (%) | Power monitor + battery |

* `SettingsRow(bell.badge, "Repeat the same alert after")` + menu picker **"2 minutes", "5 minutes", "15 minutes" (default), "30 minutes", "1 hour"** (`monitorAlertCooldownMinutes`; values 2, 5, 15, 30, 60); the whole row is disabled while no alert is on.
* If notifications are denied and any alert is on: orange caption **"Notifications for WinKnife are off in System Settings, so alerts cannot appear."** [WIN: "...off in Windows Settings..."].
* Caption: **"Alerts fire when their selected limits are reached. CPU use and temperature alerts ignore spikes shorter than about 12 seconds. The repeat setting only limits repeats of the same alert."**
* The compact variant (used in the panel, 9.5–10.5pt checkbox list) shows the caption first, then a checkbox per alert with an inline `Stepper` appearing under it when on, then the cooldown picker when any is on, then the denied-note.
* Notification texts: CPU "High CPU" / "CPU stayed above %d%% for a few seconds."; temperature "Hot CPU" / "CPU reached %@."; memory "Critical memory" / "Memory pressure reached the critical level."; disk "Low disk space" / "%@ has less than %d%% free."; battery "Low battery" / "Battery is at %d%%."; battery temp "Hot battery" / "Battery reached %@."
* Behaviour: every toggle calls `MonitorAlertService.syncWithPreferences()` (the service and its sample sink only exist while an alert for an available metric is on) and re-checks notification authorisation 1 s later (so a just-fired permission prompt can be answered first); values are sanitised to their ranges (invalid → default) on every change.

### 5.6 Settings summary (Monitor)
| Key | Type | Default (registered) | Allowed |
|---|---|---|---|
| `menuBarMetricAppearance` | String | `values` | `values, bars` |
| `menuBarCombineTemperatures` | Bool | true | - |
| `menuBarSeparateMetrics` | Bool | false | - |
| `menuBarMetricSpacing` | String | **`compact`** (view fallback says standard) | `standard, compact` |
| `menuBarHideIconWithMetrics` | Bool | false | - |
| `menuBarUsageBarNormalColor/ElevatedColor/CriticalColor` | String hex | `#64D2FF` / `#FFD60A` / `#FF453A` | `#RRGGBB` |
| `menuBarUsageBarMediumThreshold` / `HighThreshold` | Int | 70 / 90 | 1…99 / 2…100, medium < high |
| `menuBarLabelStyle` | String | `compact` | `compact, classic` (no UI on this page) |
| `menuBarPreset` | String | `dense` | `readable, dense` (no UI on this page) |
| `monitorIntervalSeconds` | Int | 2 | 1, 2, 5 |
| `monitorGraphScale` | Bool | true | - |
| `temperatureUnit` | String | `celsius` | `celsius, fahrenheit` |
| `networkSpeedUnit` | String | `bytes` | `bytes, bits` |
| `monitorMemoryMetric` | String | `used` | `used, app` |
| `panelShowFanControl` | Bool | true | - |
| all keys listed in 5.2, 5.4, 5.5 | | | |

### 5.7 Behaviour
* Interval, unit and style keys are sanitised on appear (`interval` ∈ {1,2,5} else 2; appearance; temperature unit; memory metric).
* The preview strip redraws from the live sampler snapshot and re-renders on every one of the keys above (order, combine, appearance, bar colors/thresholds, label style, upload-first, memory style, disk style, temperature unit, speed unit, spacing, hide-icon, separate).
* A metric whose hub feature is uninstalled keeps its saved key/order slot but stops rendering and sampling.
* Fan control card only when the Fan Control feature is installed; its tile in the menu bar only when fan telemetry exists.

### 5.8 Windows mapping
* **Menu-bar readouts in the tray (the big adaptation).** Windows has no text menu extras. Closest options (implement #1 + #2): (1) **one `NotifyIcon` per "item"** rendered as a 16–32 px bitmap (two-line "CPU/42%" or bar drawn with GDI+) — this is exactly the "Separate metrics into their own items" mode; "Hide the app icon while metrics are shown" removes the main icon; (2) a **floating always-on-top readout strip** docked next to the tray (existing `MonitorHub` "pinned readout" in `Features.System/Monitor`) for the non-separate mode with full text and bars; (3) Win11 Widgets/DeskBand are not available to third parties. Preview strip = same renderer drawn on a taskbar-coloured bar (Windows tray glyphs: wifi `E701`, battery `E851`).
* "Menu bar spacing" (Standard/Compact) → padding of the floating strip / item gap.
* Counters: CPU/GPU/memory/network/disk via PDH (`Monitor/Pdh.cs`, `SystemSampler.cs`; GPU from "GPU Engine" counters); battery/power via `GetSystemPowerStatus` + WMI `BatteryStatus`/`Win32_Battery` (`Power` watts: `root\wmi BatteryStatus.ChargeRate/DischargeRate`); CPU/GPU temperatures: **not reliably available** on Windows without a kernel driver (WMI `MSAcpi_ThermalZoneTemperature` is often missing; `Win32_PerfFormattedData_Counters_ThermalZoneInformation` gives a rough ACPI zone temp; LibreHardwareMonitor needs a driver → ask first). Hide the temperature tiles/alerts when no temperature source exists; battery temperature: WMI battery `Temperature` usually absent → hide. Fan speed/Fan Control: **not possible** in-box (no standard API; vendor EC SDKs only) → hide the Fan card and tiles. SMART tile: WMI `MSStorageDriver_FailurePredictStatus` (root\wmi) = basic; "External protection": the DiskProtection/eject feature maps to removable drives. Connected devices: count USB devices via `Win32_PnPEntity`/CfgMgr32. Peripheral battery: Bluetooth GATT battery service/`Windows.Devices.Enumeration` `DevicePropertyKind` battery (`{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2`).
* Alerts: toast notifications via the shell (`Notifier`, WinRT toasts need an AUMID shortcut for unpackaged apps; today the code base uses tray balloon tips). "Notifications denied" = `ToastNotifier.Setting != Enabled` (DisabledForUser/Application) or Focus Assist; sample-spike filter 12 s must be kept (rolling window).
* Colors: `ColorPicker` → WPF colour dialog/flyout (hex).
* Tokens/drag: WPF drag-drop (`DoDragDrop`) or pointer-capture reorder with live move animation.

### 5.9 Gap vs WinKnife
`Monitor/MonitorFeatures.cs` + `MonitorHub.cs` + `MonitorFlyout.cs` + `SystemSampler.cs`: seven monitor features (cpu, gpu, memory, network, disk, power, connected-devices) each with only `pinned` (show in always-on-top readout) and `hotkey`, a shared flyout with sparklines. **Missing:** the whole Monitor settings page, preview strip, metric tiles + order + per-metric options, Values/Bars styles and colors/thresholds, combine temperatures, spacing, hide icon, separate items (tray icons), interval/unit/memory-metric options, panel-block configuration (all `monitorShow*`/`monitorSys*`/`monitorNet*`/`monitorDisk*`/`monitorPwr*`/`monitorGraph*` keys), graph-scale toggle, alerts + cooldown + notifications, fan card. `SystemSampler` already reads CPU temp (`SampleTemperature`) and power snapshot (`PowerSnap`).

### 5.10 `DiskExclusionsList` (used on the Quick toggles page, documented here because it is in this file set)
* Collapsed `DisclosureGroup` (open initially if the list is non-empty). Label row: **"Excluded drives"** • spacer • count (secondary, monospaced digits) when non-empty.
* Content: one row per excluded volume: `externaldrive` icon (11pt, secondary, 16 wide) • name (12pt, 1 line) • spacer • `minus.circle.fill` remove button (secondary; accessibility label **"Remove"**).
* Add control: when the "custom" field is hidden: if there are mounted offerable drives (not already excluded, case-insensitive) → a `Menu` button `Label("Add drive…", plus)` (small) listing those drive names (sorted) then a divider and **"Other drive name…"**; if none → a plain `Label("Add drive…", plus)` button that goes straight to the field. When shown: `HStack`: rounded text field (placeholder **"Drive or volume name"**, Return adds) • bordered small button titled **"Add drive…"** (`addButton`; disabled while the trimmed text is empty) • plain small button **"Cancel"**.
* Caption: **"Drives in this list are never unmounted when using Eject all disks."**
* Store: `diskEjectExcludedVolumes` ([String], default []): entries are volume names; an entry typed as a volume UUID or mount path also excludes that drive.
* Offerable drives = local, non-root volumes that are removable/ejectable (or external): `isLocal && !isRootFileSystem && (!isInternal || isRemovable || isEjectable)`.
* Windows mapping: `DriveInfo.GetDrives()` (DriveType.Removable, plus fixed drives whose bus is USB via `Win32_DiskDrive.InterfaceType='USB'`); entries = volume label, drive letter (`E:`) or volume GUID/serial; text "mount path" → drive letter. Ejecting: `CM_Request_Device_Eject`/`DeviceIoControl(IOCTL_STORAGE_EJECT_MEDIA)`.
* Gap: QuickToggles feature exists (`Features.Tools/QuickToggles/QuickTogglesFeature.cs`); no exclusion list found (verify by grep "Eject").

---------------------------------------------------------------------------------------------------------------

## 6. Keyboard shortcuts page (`ShortcutsSettings.swift`)

### 6.1 Layout
`Form{...}.formStyle(.grouped)` (inset grouped sections, i.e. WPF cards with section headers; rows are plain, not SettingsRow tiles).
1. First section (no header): caption **"Edit every global shortcut from the features installed on this Mac. Inactive shortcuts stay saved but do not run."** [WIN "PC"].
2. One `Section(groupTitle)` per `FeatureGroup` that has at least one available role (or Window layout when installed). Group order and titles: **Windows and Dock**, **Mouse and keyboard**, **Clipboard and files**, **Sound**, **Energy and display**, **Tools**, **Dynamic Island** (title = Island feature name), **System monitor** (never has roles). [WIN] "Windows and Dock" → "Windows and Taskbar".
3. Last (if Command Bar installed): a section with `Button(Label("App shortcuts", app.badge))` opening the `CommandBarAppShortcutsView` sheet, and caption **"Open apps with their own shortcuts, even when the bar is closed. Set search names and pin favorites here."**
Within a group, features appear in hub order (`AppFeature.allCases` order filtered by group); a feature with **one** role shows that role's row directly; with **more than one** role it shows a collapsible *feature header* (`DisclosureHeaderRow`): `ShortcutRowLabel` (accent symbol 18 wide; title; second line: status dot 5pt green/secondary + **"Active"**/**"Inactive"**), spacer, a count capsule (`caption monospacedDigit`, padding h7 v2, `primary @6%`), chevron. Children indented 25. State: all collapsed, except the Screen-capture group starts expanded (`[.tools: [.screenshot]]`).
Special rows:
* **Screen capture group** (tools): header title **"Screen capture"**, symbol of the Screenshot feature, containing the roles in this display order: Screenshot, Screen recording, Copy text from screen, Color picker, Recent captures, Capture the whole screen, Edit latest screenshot, Edit clipboard image, Upload latest screenshot (only those whose feature is installed).
* **Window layout** (windows group): header with count = 41 placement actions + role rows; children = one `CentralWindowLayoutShortcutRow` per action (recorder width 108 + clear `xmark.circle.fill` button (tooltip **"Remove shortcut"**, disabled when none) + **"Reset"**) in this order with default shortcuts (defaults are ⌃⌥+key unless noted; "none" = cleared by default):
  Left (⌃⌥←), Right (⌃⌥→), Top (⌃⌥↑), Bottom (⌃⌥↓), Center half (none), Left 1/3 (⌃⌥D), Center 1/3 (⌃⌥F), Right 1/3 (⌃⌥G), Left 2/3 (⌃⌥E), Right 2/3 (⌃⌥T), Center 2/3 (none), Top 1/3, Middle 1/3, Bottom 1/3, Top 2/3, Bottom 2/3 (all none), Top 1/4, Upper middle 1/4, Lower middle 1/4, Bottom 1/4, Left 1/4, Left middle 1/4, Right middle 1/4, Right 1/4 (all none), Top left 1/6, Top center 1/6, Top right 1/6, Bottom left 1/6, Bottom center 1/6, Bottom right 1/6 (all none), Top left (⌃⌥U), Top right (⌃⌥I), Bottom left (⌃⌥J), Bottom right (⌃⌥K), Maximize (⌃⌥↩), Maximize with Margin (none), Full Screen (none), Center (⌃⌥C), Restore (⌃⌥R), Previous display (none), Next display (⌃⌥⌘→). Plus the role row **"Move pointer to next display"** (⌃⌥⌘Z). These rows are "active" when `windowLayoutShortcutsEnabled` (false by default) and a shortcut is set.
* **Radial menu**: a read-only row: label (symbol, title "Radial menu", status), the shortcut strings of every wheel profile (newline separated; **"None"** when empty), and a button **"Manage the menu"** that jumps to the Radial menu page.
* **Brightness** (energy group): header **"Displays"** (display shortcuts) containing, first, `DisplayBrightnessShortcutControls(showsShortcutRows: false)` (the enable toggle with caption, no recorders), then the two role rows; the **keyboard-light** roles are in the *Mouse and keyboard* group under a header titled **"Keyboard light"** (symbol `keyboard`, scroll anchor `keyboardBrightnessShortcuts`) containing a toggle **"Use keyboard brightness shortcuts"** (`keyboardBrightnessShortcutsEnabled`, orange note **"macOS rejected this shortcut. Choose another one."** if registration failed) and rows **"Decrease keyboard brightness"** (⌥⌘−) / **"Increase keyboard brightness"** (⌥⌘=); only if the Mac has a controllable keyboard backlight; navigating to that anchor auto-expands the header.

### 6.2 Role table (Title · feature group · default shortcut · enable keys that make it "Active")
| Role | Title | Group | Default (mac) | Active when |
|---|---|---|---|---|
| keepAwake | Keep awake | Energy and display | ⌃⌥⌘K | `hotkeyEnabled` |
| shelf | Shelf | Clipboard and files | ⌃⌥⌘D | `shelfEnabled`, `shelfShortcutEnabled` |
| switcher | Apps | Windows and Dock | ⌘⇥ | `switcherEnabled` |
| switcherWindow | Windows | Windows and Dock | ⌘` | `switcherEnabled` |
| switcherAllWindows | All windows | Windows and Dock | ⌥⇥ | `switcherEnabled`, `switcherAllWindowsShortcutEnabled` |
| clipboard | Clipboard | Clipboard and files | ⌃⌥⌘V | `clipboardHistoryEnabled`, `clipboardHistoryShortcutEnabled` |
| soundOutputSwitcher | Output switcher | Sound | ⌃⌥⌘S | `soundOutputSwitcherEnabled` |
| pastePlain | Paste as plain text | Clipboard and files | ⇧⌥⌘V | `pastePlainEnabled` |
| finderRename | Rename shortcut | Clipboard and files | F2 | `finderRenameEnabled` |
| colorPicker | Color picker | Tools (capture) | ⌃⌥⌘C | `colorPickerShortcutEnabled` |
| screenOCR | Copy text from screen | Tools (capture) | ⌃⌥⌘T | `screenOCRShortcutEnabled` |
| micMute | Mute microphone | Sound | ⌃⌥⌘M | `micMuteShortcutEnabled` |
| quitAll | Quit all apps | Tools | ⌃⌥⌘Q | `quitAppsShortcutEnabled` |
| quitAllExceptFront | Quit all apps except the front one | Tools | ⌃⌥⌘O | `quitAppsExceptFrontShortcutEnabled` |
| quickLauncher | Quick panel | Tools | ⌃⌘V | `quickLauncherShortcutEnabled` (default true) |
| screenshot | Screenshot | Tools (capture) | ⌃⌥⌘4 | `screenshotShortcutEnabled` |
| screenshotFullScreen | Capture the whole screen | Tools (capture) | ⌃⌥⌘3 | `screenshotFullScreenShortcutEnabled` |
| screenshotLastCapture | Edit latest screenshot | Tools (capture) | ⌃⌥⌘E | `screenshotLastCaptureShortcutEnabled` |
| screenshotUpload | Upload latest screenshot | Tools (capture) | ⌃⌥⌘U | `screenshotUploadShortcutEnabled`, `screenshotSharingEnabled` |
| recentCaptures | Recent captures | Tools (capture) | ⌃⌥⌘H | `recentCapturesShortcutEnabled` |
| screenshotClipboard | Edit clipboard image | Tools (capture) | ⌃⌥⌘P | `screenshotClipboardShortcutEnabled` |
| cameraPreview | Camera preview | Tools | ⌃⌥⌘W | `cameraPreviewShortcutEnabled` |
| radialMenu | Radial menu | Tools | ⌃⌥⌘Space | `radialMenuEnabled` (read-only row) |
| scratchpad | Scratchpad | Tools | ⌃⌥⌘N | `scratchpadShortcutEnabled` |
| snippetLibrary | Quick snippet menu | Mouse and keyboard | ⌃⌥⌘L | `snippetLibraryEnabled` |
| commandBar | Command Bar | Tools | ⌥Space | `commandBarShortcutEnabled` |
| screenRecorder | Screen recording | Tools (capture) | ⌃⌥⌘5 | `recorderShortcutEnabled` |
| displayBrightnessDecrease/Increase | Decrease/Increase display brightness | Energy and display | ⇧⌘− / ⇧⌘= | `brightnessControlEnabled`, `displayBrightnessShortcutsEnabled` |
| keyboardBrightnessDecrease/Increase | Decrease/Increase keyboard brightness | Mouse and keyboard | ⌥⌘− / ⌥⌘= | `keyboardBrightnessShortcutsEnabled` |
| pointerNextDisplay | Move pointer to next display | Windows and Dock | ⌃⌥⌘Z | `pointerDisplayEnabled` |
| notch | Dynamic Island | Dynamic Island | ⌃⌥⌘I | `notchEnabled`, `notchShortcutEnabled` |
Role keys are the `storageKey`s: `keepAwakeShortcut`, `shelfShortcut`, `switcherShortcut`, `switcherWindowShortcut`, `switcherAllWindowsShortcut`, `clipboardHistoryShortcut`, `soundOutputSwitcherShortcut`, `pastePlainShortcut`, `finderRenameShortcut`, `colorPickerShortcut`, `screenOCRShortcut`, `micMuteShortcut`, `quitAppsShortcut`, `quitAppsExceptFrontShortcut`, `quickLauncherShortcut`, `screenshotShortcut`, `screenshotFullScreenShortcut`, `screenshotLastCaptureShortcut`, `screenshotUploadShortcut`, `recentCapturesShortcut`, `screenshotClipboardShortcut`, `cameraPreviewShortcut`, `radialMenuShortcut`, `scratchpadShortcut`, `snippetLibraryShortcut`, `commandBarShortcut`, `recorderShortcut`, `displayBrightnessDecreaseShortcut`, `displayBrightnessIncreaseShortcut`, `keyboardBrightnessDecreaseShortcut`, `keyboardBrightnessIncreaseShortcut`, `pointerDisplayShortcut`, `notchShortcut`. Storage string = `"<mod>+<mod>:<virtual keycode>"` with modifiers in the order control, option, shift, command (e.g. `control+option+command:40`).

### 6.3 Behaviour
* A role is listed only when its feature is installed; `recentCaptures` appears if Screenshot or Screen recording is installed.
* Status text **"Active"/"Inactive"** (green/grey dot): all `requiredEnableKeys` true. Inactive shortcuts stay saved.
* Editing uses `ShortcutPreferenceRow(... isEnabled: !keyboard-brightness || keyboard toggle, includeInactiveConflicts: true, additionalConflict: window-layout conflict)`: after saving, `FeatureRuntime.sync(role.availabilityFeatures)` re-registers.
* Conflict messages: **"This shortcut is already used by %@."**; invalid: **"Use at least Control, Option or Command with a key."**; nothing captured: **"Nothing was captured. macOS or another app already uses that combination. Try another one."**; recording hint: **"Press the new shortcut Escape cancels."** (+ **"Delete clears."** where clearing is allowed); recorder idle label "None" when cleared; placeholder while recording **"Press keys"**. System-shortcut takeover offer (macOS only): **"macOS uses %@ for one of its own shortcuts."** / **"Take over while WinKnife runs"** / **"macOS gets the key back whenever WinKnife is not running or this feature is off."** / **"Don’t take over"**; refusal text uses "macOS" as the conflicting owner. When the Super key is running, a caption **"or %@"** shows the Super-key alternative under the recorder.
* All shortcut keys can be cleared/reset to default; "Reset" disabled when already default.
* `[WIN]` `expandedFeatures` start collapsed; `router.requestID` change re-expands the keyboard-light header.

### 6.4 Windows mapping
* Modifier mapping Mac → Windows: ⌘ → **Ctrl**, ⌥ → **Alt**, ⌃ → Ctrl (when ⌘ absent) / **Win-free**, ⇧ → Shift; the ⌃⌥⌘ layer → **Ctrl+Alt+Shift**; ⌃⌥ → **Ctrl+Alt**; ⌘⇥ / ⌘` (app switcher) → Alt+Tab-style handled by the switcher feature's own hook; ⇧⌘−/= → Ctrl+Shift+−/=; ⌥⌘−/= → Ctrl+Alt+−/=; F2 stays F2; ⌥Space → Alt+Space conflicts with the system menu → use Ctrl+Alt+Space; ⌃⌘V → Ctrl+Alt+V. `RegisterHotKey` accepts any combination except those reserved by the system (Win+L, etc.); conflict check across roles + `RegisterHotKey` failure.
* Recorder control: existing `WinKnife.Core/Ui/HotkeyRecorder.cs`; the "take over a system shortcut" offer has no Windows analogue (Windows hotkeys registered with `RegisterHotKey` pre-empt shell shortcuts except a few; only offer for Win-key combos via a keyboard hook) — drop `SystemShortcutTakeOverOffer` and the "macOS" wording ([WIN] conflict owner "Windows").
* Show modifier names as text ("Ctrl+Alt+Shift+K") instead of glyph symbols.
* The roles table is the contract for IDs: keep the same enable keys so toggling a feature's own "enable shortcut" switch is reflected as Active.

### 6.5 Gap vs WinKnife
There is no central shortcut page. Hotkeys are per-feature (`SettingsViewBuilder.AddHotkey` → stored under each feature's scope, e.g. `hotkey`, `upHotkey`) with Ctrl+Alt+<letter> defaults (`MicMute` Ctrl+Alt+M, OutputSwitcher Ctrl+Alt+O, Monitor CPU Ctrl+Alt+P) that don't match the table above, no Active/Inactive status, no cross-feature conflict detection (`Hotkeys.cs` has registration only), no window-layout shortcut list, no radial/keyboard-light special rows.

### 6.6 Shared shortcut row (`ShortcutPreferenceRow`) — used by General, Output switcher, Displays, Shortcuts
`VStack(alignment .leading, spacing 5)`: `HStack(alignment .top, spacing 8)`: `ShortcutRowLabel` (optional accent symbol 18 wide; title (default label **"Shortcut"**); optional 2nd line: context label (feature name when different from the role title) • "•" • status dot + Active/Inactive; `.caption` secondary) • spacer • right column: `HStack(spacing 8)`: recorder button (width 108; shows the shortcut or "None"/"Press keys") + [optional hidden clear-button placeholder so columns align] + **"Reset"** button (disabled when disabled or already default); optional `Text("or %@")` (`caption2`). Below: orange caption error, else secondary recording hint (while recording), else the take-over offer. Errors clear when the language changes. The row writes `rawValue` (role storage key) and calls `onChange` after each save/reset/accept.

---------------------------------------------------------------------------------------------------------------

## 7. Advanced page (`AdvancedSettings.swift`)

### 7.1 Layout
`Form{...}.formStyle(.grouped)` with three sections:
1. **Section "Backup"**: caption (`.caption`, secondary, wraps) **"Take your setup to another Mac: export every preference to a file and import it there. Your Scratchpad notes, clipboard history, Shelf items and system permissions never leave this Mac."** [WIN: "...to another PC... never leave this PC."]; `HStack(spacing 10)` of two buttons: `Label("Export settings…", square.and.arrow.up)` and `Label("Import settings…", square.and.arrow.down)`. Result lines: after a successful export: green `checkmark.circle.fill` + green caption **"Backup saved"**; failed export: orange caption **"Could not save the backup."**; invalid import file: orange caption **"This file is not a valid WinKnife backup."**
2. **Section "Permissions"**: caption **"Removes every permission you granted WinKnife (Accessibility, Screen Recording, Full Disk Access and others), the login item and the closed-lid rule. Useful to start fresh or before uninstalling. The app stays installed."**; destructive button `Label("Clear all permissions", lock.slash)` (disabled while `working`); result: green check + **"Permissions cleared."** or orange **"Some permissions or the closed-lid rule could not be removed. Try again and allow the password request if it appears."**
3. **Section "Uninstall"**: caption **"Does all of the above, then removes the preferences and moves WinKnife to the Trash, leaving nothing behind. The app quits when done. You can reinstall anytime."**; destructive button `Label("Uninstall WinKnife completely", trash)` (disabled while working).

Alerts (modal confirmations):
* Clear: title **"Clear all permissions?"**, message **"Features that need permissions will stop working until you grant them again. Your settings are kept."**, buttons **"Cancel"** (cancel) / **"Clear all permissions"** (destructive).
* Uninstall: title **"Uninstall WinKnife?"**, message **"WinKnife will clear its permissions, remove its preferences and move to the Trash, then quit. This can’t be undone from the app, but it stays in the Trash until you empty it."**, buttons **"Cancel"** / **"Uninstall WinKnife completely"** (destructive).
* Uninstall failed: title **"Uninstall stopped"**, message = the failure body returned by the service, button **"OK"**.
* Import confirm: title **"Import these settings?"**, message **"Your current settings are replaced by the file’s and the app restarts. Nothing else on this Mac is touched."**; if the file has no Dynamic Island keys: **"This backup has no Dynamic Island settings. This Mac’s island settings will be kept. Re-export with WinKnife 3.4 or newer on the other Mac to copy them. Other settings will be imported and the app will restart."**; buttons **"Cancel"** / **"Import and restart"**.

### 7.2 Behaviour
* Export: save panel, default name **"Vorssaint Settings.plist"** [WIN: "WinKnife Settings.json"], creates folders allowed; payload = snapshot of every exportable key **including registered defaults** (`object(forKey:)` sees defaults) wrapped as `{ vorssaintBackupVersion: 1, vorssaintBackupAppVersion: <version>, settings: {...} }`; Scratchpad notes are flushed first. Not exported: clipboard history, Shelf, permissions, machine-specific paths of non-app programs in exception lists.
* Import: open panel, validate/sanitise → show confirm alert; on accept, clear the exportable keys (keys missing in the file fall back to registered defaults; Dynamic Island keys are kept when the file has none; local-only presets/watermark carried across; exception-list paths merged), write the file's values, then **relaunch the app**.
* Clear permissions: stops every input interceptor first, then `tccutil reset All <bundle id>` + removes login item and closed-lid sudoers rule; calls back with success/failure.
* Uninstall: clear permissions, remove preferences/saved state, restore normal sleep (last chance to revert closed-lid), move the app bundle to the Trash via a detached helper, quit. On failure: "Uninstall stopped" alert.
* Controls disabled while `working`.

### 7.3 Windows mapping
* Backup: JSON file (`WinKnife Settings.json`) of the `settings.json` store keys (exclude machine-specific paths and secrets); import = validate → replace → relaunch (`Process.Start(self)` + exit). Keep the format version fields renamed `winknifeBackupVersion`, `winknifeBackupAppVersion`, `settings`. File dialogs: `Microsoft.Win32.SaveFileDialog/OpenFileDialog`.
* "Permissions": Windows has no TCC. Replace the section by **"Reset"**: [WIN] title "Reset" or keep "Permissions" with text **"Removes the startup entry, the closed-lid rule and any Windows privacy consent WinKnife asked for (camera, microphone). ..."**; implementation: delete the `Run` value, restore the saved lid-close power-scheme values, clear radio/webcam consent keys under `HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\<capability>\NonPackaged\<path>`. No password request.
* Uninstall: no Trash; run the uninstaller / delete `%APPDATA%\WinKnife`, `%LOCALAPPDATA%\WinKnife`, Run key, scheduled tasks, then self-delete the exe via a detached helper (`cmd /c ping -n 3 127.0.0.1 >nul & del`) or call the installer's uninstall entry; "moves to the Trash" → `[WIN]` "removes WinKnife from this PC" (or sends to the Recycle Bin with `SHFileOperation FOF_ALLOWUNDO`).
* Gap: none of this exists (no export/import, no clear, no uninstall); tray menu only has Quick panel / Features / Start with Windows / Quit.

---------------------------------------------------------------------------------------------------------------

## 8. Consolidated defaults reference (registered values; keys used by this file set)

General/appearance: `appAppearance=system`, `liquidGlassEnabled=false`, `notchLiquidGlassEnabled=false`, `hotkeyEnabled=true`, `launchAtLoginWanted=false`, `keepAwakeShortcut="control+option+command:40"`, `menuBarIconSymbol=""`, `appLanguage` = system language.
Panel layout: `panelSectionOrder` (empty), `panelShowKeepAwake/Brightness/Utilities/Controls/Toggles/Wallpaper/FanControl=true`, `monitorShowSystem/Network/Disk/Power/Mixer=true`, `panelCollapsedSections` (empty).
Sound: `mixerShowFinder=true`, `mixerHideInactiveApps=false`, `mixerAppArrangement=""`, `mixerLowerVolumeOnHeadphonesDisconnect=false`, `mixerHeadphonesDisconnectVolumePercent=25`, `preciseVolumeRollerEnabled=false`, `soundOutputSwitcherEnabled=false`, `soundOutputSwitcherShortcut="control+option+command:1"` (⌃⌥⌘S), `soundOutputSwitcherDeviceUIDs` (empty), `audioPriorityOutputEnabled=true`, `audioPriorityInputEnabled=true`, `audioPriorityOutputUIDs=[]`, `audioPriorityInputUIDs=[]`, `audioPriorityDeviceNames={}`, `musicBlockEnabled=false`, `musicBlockReplacementPath=""`, `musicBlockPlayReplacement=true`.
Energy: see 4.1 table; plus `extraBrightnessEnabled=false`, `extraBrightnessLevel=100`, `brightnessControlEnabled=false`, `brightnessKeysEnabled=false`, `brightnessOSDEnabled=false`, `brightnessKeyStep=standard`, `displayBrightnessShortcutsEnabled=false`, `keyboardBrightnessShortcutsEnabled=false`, `bluetoothSleepEnabled=false`, `bluetoothSleepRestoreOnWake=true`, `bluetoothSleepRestorePending=false`.
Monitor: see 5.6; alerts all off, thresholds 90 / 90 / 40 / 10 / 15, cooldown 15.
Backup: no keys.

---------------------------------------------------------------------------------------------------------------

## 9. Prioritised gap checklist for implementers

1. **Settings shell + shared components** (`SettingsCard`/`SettingsRow` with icon tile, badge, inset 38, chips, tokens, popovers) in `WinKnife.Core/Ui` — every page above depends on them.
2. **General page**: launch-at-login (+ StartupApproved), language picker (structure; en-US first), appearance override (Light/Dark/System thumbnails), Acrylic option, Keep-awake global shortcut row, feedback row.
3. **Menu bar (tray) page**: section model + order/visibility keys honoured by `QuickPanelWindow`, `PanelLayoutEditor` with taskbar-mirrored miniature, icon gallery with Segoe/bundled glyphs, "Show tray icon".
4. **Energy**: finish Keep awake (default duration chips, automation, pause on lock, jiggle, active icon/tint, battery limit, lid-close via powercfg, auto-start, right-click toggle), Displays page (rows, dimming, power, key steps, shortcuts), Bluetooth on sleep (Radios API), Extra brightness via SDR white level (ask first).
5. **Monitor page**: tiles + order + per-metric options, Values/Bars + colours/thresholds, preview strip, tray items/floating readout, panel block config keys, intervals/units, alerts + notifications.
6. **Sound pages**: re-skin mixer/output switcher/priority as settings pages with the listed controls; add music blocker (low priority).
7. **Shortcuts page** with the role table and conflict detection; **Advanced page** (backup/reset/uninstall).
8. Hide/disable the truly mac-only items on Windows: Liquid Glass (→ Acrylic), XDR Extra brightness (→ SDR white level), fan control, brightness-key interception, Accessibility/System-Audio permission rows, app boost >100%, system-shortcut takeover offer, "needs Applications folder".
