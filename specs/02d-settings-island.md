# 02d - Settings: Dynamic Island page (all tabs, editors, Companion, AI agents, accounts, calendars)

Source of truth: `vorssaint-mac/Sources/Vorssaint/UI/Settings/{NotchSettings, NotchSettingsTabRow, NotchLayoutEditor, NotchContentEditor, NotchEditorItem, NotchAgentsSettings, ClaudeAccountSettings, CodexAccountsSettings, NotchMascotSettings, NotchCalendarSelection}.swift`, model types in `Services/Notch/*` and `Services/AgentUsage/*`, strings in `Core/Notch*Strings.swift`, `Core/ClaudeAccountStrings.swift`, `Core/CodexAccountStrings.swift`, defaults in `Core/Defaults.swift`.
The runtime island UI (closed pill, pages, banners) is specified elsewhere; this file specifies only the SETTINGS pages and their editors, plus every setting the pages write.
"Vorssaint" is written "Trayloom" everywhere. macOS-only wording is given verbatim and flagged `[mac-only]` with the Windows wording to use.

---------------------------------------------------------------------------------------------------

## 0. Conventions used by every section

### 0.1 Shared Settings building blocks (from `SettingsCard.swift`)
| Block | Spec |
|---|---|
| `SettingsCard(title?)` | VStack, spacing 13, left aligned, full width, padding 16, background = secondary-fill at 35% opacity ("quaternary 0.35"), corner radius 16 (continuous). Optional heading = Text `.headline` (semibold 13pt-ish). |
| `SettingsRow(symbol,title,caption?,badge?) {accessory}` | HStack spacing 12: icon tile (26x26, radius 7, fill accent 12%, glyph 12pt semibold accent) - title (body) with optional caption below (`.caption`, secondary, wraps) - Spacer(min 12) - accessory (switch / picker / button) on the right. A nil symbol shows the app's tray glyph on a 82% black tile. |
| `settingsRowTextInset` | 26 + 12 = 38 px. Follow-up/sub-rows are indented by this so they line up with the parent's text column. |
| `SettingsFeatureSwitchRow(symbol,title,caption?,isOn,feature,enabled=true)` | A switch row for an option that needs another feature. While the feature is NOT installed (or `enabled == false`) the row is greyed (saturation 0, opacity 0.45), the switch reads OFF and is disabled, caption hidden. If the feature is not installed, below it (inset 38): caption text "`Enable “<Feature title>” in Features.`" (secondary) + small button **Open Features** (go to the Features hub focused on that feature). If installed but `enabled == false` the caption is shown plain (secondary, inset 38). |
| `SettingsChoiceRow(symbol,title,selection){options}` | Title row with a segmented picker at the right if it fits on one line; else the segmented picker under the title (inset 38); else a menu picker. |
| `SettingsMenuRow(symbol,title,selection){options}` | Title row with a pop-up menu picker on the right; wraps under the title (inset 38, or 0 if too wide) when it doesn't fit. |
| `TrailingSwitchToggleStyle` | Label at leading edge, switch pushed to trailing edge (used for shared controls that carry their own label). |
| `choice(title,symbol,selected)` tile | Button, VStack spacing 8: glyph 20pt medium + title 11pt medium (max 2 lines, centered); min height 68, padding 8, corner radius 12. Selected: foreground accent, fill accent 12%, 1px stroke accent 70%. Unselected: fill secondary 6%, no stroke. |
| `PermissionRow(kind)` | `[mac-only]` Row: status icon (checkmark.circle.fill green "Granted" / exclamationmark.circle.fill orange "Not granted") + name ("Accessibility" / "Screen Recording"); when not granted two small buttons "Grant access" and "Open System Settings…". Windows: not needed (see 13). |

### 0.2 Suggested Segoe Fluent Icons mapping (suggestions; (v) = code point to verify in the Segoe Fluent Icons font viewer, otherwise draw a small vector icon)
| SF Symbol(s) | Glyph | SF Symbol(s) | Glyph |
|---|---|---|---|
| slider.horizontal.3 (Controls section) | E9E9 (v) | speaker.wave.2 / speaker.wave.2.fill (Volume, mixer) | E767 |
| music.note | E8D6 | timer | E916 |
| web.camera / person.crop.rectangle | E722 | arrow.down.circle (Downloads) | E896 |
| bell | E7ED | bell.slash | E7ED with a slash line drawn over it (v) |
| calendar / calendar.badge.clock | E787 | doc.on.clipboard | E77F |
| camera.viewfinder (Captures, Screenshot) | E722 | tray.full (Files) | E8B7 |
| gauge.with.dots.needle.* (System, limits) | E9D9 (v) | wand.and.rays (Tools / quick panel) | E945 (v) |
| note.text (Scratchpad) | E70B | sparkles / sparkle (AI Agents) | draw (no stock glyph) or EA80 (v) |
| eye / eye.slash | E7B3 / ED1A | square.grid.2x2 (Explore) | E80A |
| gearshape (Settings) | E713 | pin | E718 |
| plus / plus.circle | E710 | checkmark.circle.fill (included) | E73E inside a filled circle (EC61 (v)) |
| minus.circle | E738 | arrow.up.right.circle | E8A7 |
| xmark | E711 | chevron.down | E70D |
| line.3.horizontal (drag handle) | E700 | arrow.up.forward.app (open island) | E8A7 |
| sun.max / sun.max.fill | E706 | light.max (keyboard light) | E706 (v) |
| mic / mic.fill | E720 | mic.slash | E1D6 (v) |
| battery.75percent / .50 / .25 | E857 / E854 / E852 (v) | headphones | E7F6 |
| keyboard | E765 | globe | E774 |
| lock.display / lock | E72E | lock.open | E785 |
| cup.and.saucer (Keep awake) | EC32 (v) | record.circle | E7C8 (v) |
| speedometer | EC4A (v) | bubble.middle.top (App panel) | E8BD (v) |
| command (Command Bar) | E8E0 (v) | hand.draw / hand.wave | E7C9 (v) |
| waveform / waveform.path / waveform.path.ecg | E9D9 (v) | photo | E91B |
| list.bullet | E8FD | text.quote (Lyrics) | E8F2 (v) |
| arrow.uturn.backward | E7A7 | livephoto | E9CE (v) |
| menubar.rectangle | E7F4 (v) | display / display.2 / macbook | E7F4 / E7F4 / E7F8 |
| laptopcomputer | E7F8 | cursorarrow / cursorarrow.motionlines | E962 (v) |
| rectangle.on.rectangle | E8A1 (v) | dollarsign.circle | EAFD (v) |
| folder | E8B7 | person.crop.circle | E77B |
| hourglass | E916 (v) | number | E8EF (v) |
| paintpalette / swatchpalette | E790 | figure.walk | E805 (v) |
| theatermasks | E8B2 (v) | drop / drop.halffull | EA3A (v) |
| minus | E108 | cpu | E950 (v) |
| chart.bar.xaxis | E9D2 (v) | square.grid.3x3.fill | E80A |
| arrow.counterclockwise.circle | E777 (v) | exclamationmark.triangle / .circle | E7BA / E783 (v) |
| checkmark.circle | E930 | arrow.triangle.2.circlepath | E895 |
| face.smiling (Companion) | E76E (v) | link / terminal / infinity | E71B / E756 / draw |
| bolt (charging) | E945 (v) | alarm | E7E8 (v) |
| info.circle.fill | E946 | rectangle.compress.vertical / rectangle.expand.vertical | E73F / E740 |
| arrow.up.left.and.arrow.down.right (Custom size, expand) | E740 | rectangle.topthird.inset.filled | draw (top-third filled rectangle) |
| capsule | draw (rounded rectangle outline) | circle.square / lock-less shapes | draw |

### 0.3 Layout metrics common to the page
* Page body: VStack(spacing 20), padding horizontal 22, top 22, scroll bottom 22.
* Cards use `SettingsCard`. Page text uses system font (Segoe UI Variable on Windows), `.title2.bold()` = 22pt bold for page title, `.callout` = 12-13pt secondary for descriptions, `.caption` = 11pt, `.headline` = 13pt semibold, `.subheadline.weight(.medium)` = 12pt medium.
* Everything is live: each change calls `sync()` (NotchService.syncWithPreferences, stop lyrics service when lyrics are off, sync queue pref, sync brightness service). There is no Apply/OK button.

---------------------------------------------------------------------------------------------------

## 1. Page shell: "Dynamic Island" (NotchSettings.swift / NotchSettingsTabRow.swift)

### 1.1 Layout (top to bottom, VStack spacing 20, padding H22/T22)
1. **Header row** (HStack, vertically centered)
   * Left VStack(spacing 5): `Text(title)` = **Dynamic Island** (22pt bold); `Text(description)` = "Your music, controls and everyday tools, together at the top of your screen. Optional. Turn it off to keep using the separate panels." (callout, secondary).
   * Right: master switch, no label, accessibility label "Enable Dynamic Island". Key `notchEnabled`, default **off**. Disabled when the feature `notch` is not installed.
2. **Accessibility banner** `[mac-only]` - shown only when: island enabled AND feature installed AND NOT (hover && hideUntilHover && !hiddenShowsActivity) AND the current display has no camera housing (`!geometry.isNotched`) AND Accessibility not granted. VStack(spacing 8): callout secondary "Allow Accessibility so Dynamic Island can appear on this display without covering the menus." + PermissionRow(accessibility). **Windows: never show** (no equivalent permission; the island is a normal topmost window).
3. **Tab row** (`NotchSettingsTabRow`): HStack - `ViewThatFits`: segmented picker (labels below) else the same picker as a pop-up menu - then a bordered icon button (SF `arrow.up.forward.app`, tooltip/AX "Open Dynamic Island") that opens the island now (`NotchService.open()`); **disabled when the island is off** (`canOpen = enabled`).
   * Tabs in order: **Layout**, **Content**, **Activity**, **Behavior**, and (only when the Companion feature `notchMascot` is installed) **Companion** (title from `NotchMascotStrings.title`). Default selected tab = Layout.
   * If the Companion feature is uninstalled while its tab shows, the page jumps back to Layout.
4. **Tab body**
   * Layout / Activity / Behavior: `ScrollView` of the corresponding page (section 2, 4, 5), re-created (`.id(tab)`) on tab change.
   * Content: not scrolled as a whole; the Content editor fills the remaining height (section 3).
   * Companion: embedded `NotchMascotSettings(embedded:true)` (section 6) which keeps its own ScrollView and has no outer padding except bottom 22.

### 1.2 Router hints (one-shot)
* `router.notchModule = X` (set when a section is chosen from inside the island, or "Customize" actions) -> select module X in the Content tab and switch to Content, then clear the hint.
* `router.notchCompanion = true` -> switch to Companion tab if installed, then clear.
* Changing tab clears any in-flight drag state (`draggingModule`, `draggingControl`).

### 1.3 Settings written by the shell
| Key | Type | Default | Effect |
|---|---|---|---|
| `notchEnabled` | Bool | false | Shows/hides the island. All other island settings are inert while off (but still editable; some previews still render). |

### 1.4 Windows mapping
Settings host = WPF page inside the existing Settings window under the Dynamic Island group. Master switch = `ToggleSwitch` style; tab row = a custom segmented `ListBox`/`TabControl` with an overflow `ComboBox` fallback when the width (measure) is smaller than the segmented width + button; the open button calls `IslandWindow.Expand()`.

### 1.5 Gap vs Trayloom
Trayloom has a single feature `island` whose generic settings view (SettingsViewBuilder) shows 6 flat options, no master switch inside the page (install/uninstall toggles the whole feature), no tabs, no open button, and separate features `island-timer`, `island-calendar`, `island-downloads`, `island-gestures` with their own flat settings pages. Everything in this file is missing and requires new custom controls (tile choice, toggle card, checkbox list, popover, preview canvas, segmented tab row).

---------------------------------------------------------------------------------------------------

## 2. Tab "Layout" (layoutPage + NotchLayoutEditor)

### 2.1 Layout top to bottom (VStack spacing 20)
1. **NotchLayoutEditor** (no card chrome; section 7 specifies it in full): a 400px-high canvas with the live preview of the Controls page, draggable round quick-access buttons around it, a resize grip, and a hint line below.
2. **Card "Size"**
   * Row of 3 `choice` tiles, HStack spacing 10: **Compact** (`rectangle.compress.vertical`), **Spacious** (`rectangle.expand.vertical`), **Custom** (`arrow.up.left.and.arrow.down.right`). Binding `notchSize` ("compact"/"spacious"/"custom"; unknown -> spacious in the editor).
   * If Custom: a 3-column Grid (spacing H12 V10) with two rows: `Width` slider 360...600 step 10 (key `notchCustomWidth`, default 440) and `Maximum height` slider 260...640 step 10 (key `notchCustomHeight`, default 480); each row = name (fixed width, hidden from AX) + slider + value text (monospaced digits, secondary, width 38, integer). Under the grid caption: "Controls stay compact. Longer lists use up to this height." Values pass through `clamped(to:range, fallback:default)` (non-finite -> default).
   * Switch row (symbol `drop.halffull`) **Translucent background** (`notchTranslucentBackground`, default off). Caption = "When open, Dynamic Island blurs what is behind it with the system’s own material." If Liquid Glass is in effect (macOS 26 + `notchLiquidGlassEnabled` + not Reduce Transparency): the switch is forced ON, disabled, caption = "Liquid Glass is on, so Dynamic Island uses it instead."
3. **Card "Displays without a notch"** - shown only if at least one connected display has no camera housing (`NotchSupport.hasDisplayWithoutNotch`).
   * Two silhouette tiles (HStack spacing 10): **Capsule** and **Notch**, each = 400x48 sample of a menu bar (see 2.3) scaled 0.5 (shown 200x24) above the title (11pt medium). Selected styling as `choice`. Key `notchSilhouette` ("capsule"/"notch", default capsule).
   * If island enabled and the chosen Display (Behavior tab) cannot reach a notchless display (`reachesDisplayWithoutNotch(display) == false`): caption "With the Display chosen on the Behavior tab, Dynamic Island doesn’t appear on these displays." + button **Show on all displays** (sets `notchDisplay = "all"`).
4. **Card "Capsule fit"** - shown only if card 3 is shown AND silhouette == capsule.
   * Grid of 3 signed sliders (label | slider | value text width 38): **Width** -40...40 step 2 (`notchCapsuleFitWidth`, default 0); **Height** -4...4 step 1 (`notchCapsuleFitHeight`, 0); **Distance from top** 0...20 step 1 (`notchCapsuleFitDrop`, 0; Windows: labelled **Distance from bottom** while the island rests on the bottom edge). Value text is signed ("+4", "-2", "0"; 0..1 fraction digits). Slider AX label "Capsule fit, Width" etc. (card name + control name, because other cards have same-named sliders).
   * Caption: "Resizes the capsule and lowers it from the top of the screen on displays without a notch."
   * Windows (island placement): at the bottom edge the caption reads "Resizes the capsule and raises it from the bottom of the screen on displays without a notch." The edge is read from the stored position of the display the island is on (`IslandPlacementStore.Load`), so it holds while the island is off.
5. **Card (no title)**: switch row symbol `capsule` **Show outline** (`notchOutlineEnabled`, default off).
6. **Card "Notch fit"** - shown only if a connected display HAS a camera housing (`NotchSupport.hasNotchedDisplay`). `[mac-only]`
   * Switch row (`rectangle.topthird.inset.filled`) **Hide gap below notch**, caption "Extends Dynamic Island to the bottom of the menu bar to hide the thin strip below the notch." (`notchHideMenuBarGap`, default off).
   * Grid: **Width** -10...10 step 1 (`notchCameraFitWidth`, 0), **Height** -6...6 step 0.5 (`notchCameraFitHeight`, 0).
   * Caption: "If an edge of the notch shows around Dynamic Island, adjust these values until it disappears."

### 2.2 Settings table (this tab)
| Key | Type | Default | Allowed | Effect |
|---|---|---|---|---|
| `notchSize` | String | "spacious" | compact, spacious, custom | Open-island size preset. Compact page width 480pt / content height 180; Spacious width 560 / content 264; Custom uses the two keys below. |
| `notchCustomWidth` | Double | 440 | 360...600 (step 10) | Open island width in Custom. |
| `notchCustomHeight` | Double | 480 | 260...640 (step 10) | Maximum open height in Custom. Dragging the grip in the Layout editor writes both and sets size=custom. |
| `notchTranslucentBackground` | Bool | false | | Open island blurs the content behind (system material) instead of pure black. |
| `notchLiquidGlassEnabled` | Bool | false | | Set in General > appearance, read here only (forces the translucent switch on). |
| `notchSilhouette` | String | "capsule" | capsule, notch | Shape on notchless displays: floating rounded capsule vs a cut-out hanging from the top edge. A real camera housing always keeps the notch shape. |
| `notchCapsuleFitWidth/Height/Drop` | Double | 0/0/0 | -40...40 step 2 / -4...4 step 1 / 0...20 step 1 | Capsule size offsets (points) and gap below the top edge. Persisted values are re-snapped (even width, whole points). |
| `notchOutlineEnabled` | Bool | false | | 2px white 65% outline around the island (a capsule's outline goes all the way round). |
| `notchHideMenuBarGap` | Bool | false | | `[mac-only]` extends the notch shape down to the menu bar bottom. |
| `notchCameraFitWidth/Height` | Double | 0/0 | -10...10 step 1 / -6...6 step 0.5 | `[mac-only]` corrections to the reported camera housing. |

### 2.3 Silhouette sample drawing
400x48 box, `primary 4%` background, corner radius 14: top strip = menu bar height 24 filled `primary 8%`; left 3 capsules (widths 26/36/30, height 8, `primary 22%`) spaced 9; right 3 circles 12px `primary 22%`; horizontal padding 14; centered black island shape at its resting size (capsule: fully rounded pill floating with a gap; notch: top-flush with bottom shoulders). Whole sample scaled 0.5.

### 2.4 Windows mapping
* Windows has NO camera housing, so `hasNotchedDisplay` is always false: **card 6 "Notch fit" and its 3 keys are never shown** (keep keys registered at 0 for backup compat). `hasDisplayWithoutNotch` is always true: cards 3 and 4 are always shown (keep the verbatim title "Displays without a notch"; optional deviation: title it "Shape" - not recommended for parity). The unreached-message/"Show on all displays" block never appears (every display is notchless) except when `display=builtIn` with the lid closed - keep the logic with Windows' own `reachesDisplay` (see 4.5 of display options).
* Silhouette "Capsule" = window with all corners rounded (radius = height/2), floating `topGap + capsuleFitDrop` px below the top edge of the monitor work/bounds area; "Notch" = flush with the top edge, flat top, rounded bottom shoulders (the current Trayloom `BottomRadius` shape).
* "Distance from top" replaces/merges the existing Trayloom `topGap` (0..24) -> `notchCapsuleFitDrop` 0..20.
* Translucent background: apply Acrylic via `SetWindowCompositionAttribute(ACCENT_ENABLE_ACRYLICBLURBEHIND)` or Windows 11 `DWMWA_SYSTEMBACKDROP_TYPE = DWMSBT_TRANSIENTWINDOW` on the layered island window; with "Transparency effects" off in Windows (`HKCU\...\Themes\Personalize\EnableTransparency=0`) behave like Reduce Transparency = solid black. Liquid Glass has no Windows equivalent: `liquidGlassIsOn` is always false.
* Size values are DIPs (WPF) - same numbers; the window is laid out at real size and scaled in previews exactly like the layout editor (see 7).
* Island outline = `Border` stroke 2 DIP white 65%.

### 2.5 Gap vs Trayloom
Has: `topGap` slider (0-24, key `topGap`), fixed 480x292 expanded size (`ExpandedWidth/Height`), fixed 116x34 idle pill. Lacks: Size presets/custom/dimension sliders, translucent background, silhouette choice + capsule fit, outline, the whole layout editor with quick-access buttons.

---------------------------------------------------------------------------------------------------

## 3. Tab "Content" (contentEditor + NotchContentEditor + NotchEditorItem)

### 3.1 Layout
The tab body is a `GeometryReader`; container width W, height H:
* `wide = W >= 940`. `listWidth = wide ? 216 : 196`. `previewWidth = wide ? min(560, round((W - listWidth - 32) * 0.5)) : 0`. `detailWidth = max(0, W - listWidth - 16 - (wide ? previewWidth + 16 : 0))`.
* HStack(alignment top, spacing 16): [Section list (width listWidth, height H)] [Detail ScrollView (width detailWidth)] [Preview column (width previewWidth, only if wide)].
* When NOT wide the preview is placed at the top of the detail column (below the section header) with height limit 280.

**A. Section list** (VStack spacing 8, padding 10, background secondary 35%, radius 16)
* Heading block (padding H6): `Sections` (.headline) + `Drag to reorder. Uncheck a section to hide it from the island.` (.caption secondary, wraps).
* Scrollable column (spacing 2) of `NotchSectionListRow` for every module in the island's order (`notchModuleOrder` then the remaining modules in default order). Auto-scrolls to the selected row on appear and (animated 0.2 s unless Reduce Motion) on selection change.
* **Row** (min height 32, padding H8, corner radius 8; fill accent 16% when selected, primary 5% on hover, clear otherwise; opacity 0.45 while being dragged): checkbox (shows the module) - 22px tile (rounded 27%, module tint, white glyph at 46% of the tile, grey tile `secondary 14%` + grey glyph when hidden/unavailable) - module title (12.5pt, semibold when selected, regular otherwise, secondary colour if unavailable, up to 2 lines). Tooltip + AX value = the module's one-line summary (strings 10.1). AX actions: "Move up"/"Move down".
  * The checkbox is `.constant(false)` and disabled when the module is not available (its feature is off/uninstalled).
  * Clicking the title/tile selects the module; dragging anywhere on the row reorders (see 8).

**B. Detail column** (ScrollView, VStack spacing 16, bottom padding 22)
1. **Section header** (`NotchSectionHeader`, HStack top-aligned spacing 12): 38px tile; VStack(spacing 3): module title (.title3 semibold), summary (callout secondary); if the module is unavailable also a row (top padding 4): reason in orange callout + small button **Open Features** (go to Features hub focused on `moduleFeature(module)`; for modules with no single feature - Controls, Music, Captures, System - the reason is "Enable the corresponding feature to use it here." and the button still opens Features).
2. If narrow: the preview (section D).
3. If the module is available and has options: a `SettingsCard` (no title) holding the module's options (3.2). If available and has none (mixer, system, tools): text "This section has no options." (callout secondary).

**C/D. Preview column**: `NotchIslandPreview(module, hidden)` - the module's real island page rendered at real size inside a slice of menu bar and scaled to fit; frame height = `largestIslandHeight * scale + 28`. Scale = max(0.1, min(1, (stageW - 32)/largest.width, (stageH - 28)/largest.height)); ONE scale for all sections (taken from the tallest island) so choosing a section never moves the list. Stage: top strip = menu-bar height (primary 6%), island black shape (black fill, white text, dark scheme) hung from it, scaled from the top anchor, dimmed to 40% opacity if the section is hidden. Surface: secondary 35% background, radius 16. Caption bottom-left (.caption medium secondary, padding H12/B8): `eye` + "Preview", or `eye.slash` + "Hidden from the island". The preview is non-interactive. AX label "Preview", value = title (+ ", Hidden from the island"). Pages in preview never turn on the camera (still image + text "The camera turns on only in the island." with `person.crop.rectangle` 34pt light) and never give the scratchpad the keyboard (still of the note, pads and note actions); Agents page shows the page when the agents feature is on else an empty view with message "Claude Code, Codex, OpenCode and GitHub Copilot usage, limits and costs."; Watch shows its setup view; System page only samples metrics while the preview is visible (SystemMonitor panelDidAppear/Disappear); Notifications/Clear buttons are placed in the band row.

### 3.2 Module tile colours and per-module options
Tile tint (macOS system colours; Windows suggestions in parentheses): controls blue (#0A84FF), mixer purple (#BF5AF2), music pink (#FF375F), clipboard brown (#AC8E68), captures indigo (#5E5CE6), files cyan (#64D2FF), system green (#30D158), tools gray (#8E8E93), calendar red (#FF453A), notifications orange (#FF9F0A), timer mint (#63E6E2), camera teal (#40C8E0), downloads blue (#0A84FF), scratchpad yellow (#FFD60A, glyph black 75%), agents (0.85,0.47,0.34 = #D97857), watch purple (#BF5AF2). All other glyphs white.
Default module order (= `NotchModule.allCases`): controls, mixer, music, clipboard, captures, files, system, tools, calendar, notifications, timer, camera, downloads, scratchpad, agents, watch.
Module titles: controls "Controls", mixer "Volume mixer", music "Now Playing", clipboard "Clipboard", captures "Recent captures", files "Files", system "System", tools "Tools", calendar "Calendar", notifications "Notifications", timer "Timer", camera "Camera mirror", downloads "Downloads", scratchpad "Scratchpad", agents "AI Agents", watch "Watch".
Module -> feature that must be installed (`moduleFeature`): mixer->mixer, clipboard->clipboardHistory, files->shelf, tools->quickLauncher, calendar->notchCalendar, notifications->notchNotifications, timer->notchTimer, camera->cameraPreview, downloads->notchDownloads, scratchpad->scratchpad, agents->notchAgents, watch->notchWatch; controls/music/captures/system: none (captures = any of screenshot|screenRecorder|screenOCR|colorPicker; system = any of monitorCPU|GPU|Memory|Network|Disk|Power|fanControl|connectedDevices).
Module visibility rule (`moduleBinding`, also the island's `NotchSupport.modules()`): shown iff NOT in `notchHiddenModules` AND feature available AND for timer `notchTimerEnabled`, camera `notchCameraEnabled`, calendar `notchCalendarEnabled`, notifications `notchNotificationsEnabled`, agents `notchAgentsEnabled`, watch `notchWatchEnabled`. Toggling the checkbox for those six writes BOTH the module's own boolean and the hidden list.

Options per module (all inside one SettingsCard; `switchRow(symbol,title,caption)` = SettingsRow + switch):

**Controls** (`controls`)
* HStack spacing 10 of 4 *toggle cards* (section 3.3): Now Playing (`music.note`) - Volume (`speaker.wave.2.fill`) - Brightness (`sun.max.fill`) - Keyboard light (`light.max`) writing `notchHiddenControls` (see 3.4). Titles: "Now Playing", "Volume", "Brightness", "Keyboard light".
* Subtitle "Controls and shortcuts" (.subheadline medium).
* Adaptive grid (min item width 116, spacing 8) of the 11 shortcut tiles in `notchControlOrder` order, each a draggable toggle card: Volume mixer (`speaker.wave.2`), Keep awake (`cup.and.saucer`), Timer, Calendar, Mute microphone (`mic.fill`), Screenshot (`camera.viewfinder`), Screen recording (`record.circle`), Speed test (`speedometer`), Open app panel (`bubble.middle.top`), Command Bar (`command`), Scratchpad (`note.text`). (Default order = enum order: mixer, keepAwake, timer, calendar, microphone, screenshot, recording, speedTest, panel, commandBar, scratchpad.) Drag reorders (`notchControlOrder`), the corner button/click toggles visibility.
* Divider, then `SettingsFeatureSwitchRow(cup.and.saucer, "Show “Keep awake” in the closed island", caption "A running session appears beside the camera with the time it has left, or ∞ when it has no end.", key notchKeepAwakeActivity, feature keepAwake)`.

**Music** (`music`)
1. Switch `music.note` "Show music while playing" -> `notchShowPlayingMusic` (true).
2. Switch `play.rectangle` "Automatically include videos and other apps" -> `notchIncludeOtherPlayers` (true).
3. FeatureSwitch `speaker.wave.2` "Show volume in the player" -> `notchMusicShowVolume` (false), feature mixer.
4. Menu row `arrow.up.forward.app` "Open when nothing is playing" caption "The cover and the playback buttons open this app. Automatic opens the music app that played last, then Spotify or Apple Music." Options: "Automatic" (value "") + installed music apps (loaded async; a saved choice that is not installed stays listed). -> `notchPreferredPlayer` (String, "" = automatic, max 256 bytes).
5. FeatureSwitch `text.quote` "Show lyrics" -> `notchLyricsEnabled` (true), feature notchLyrics. If on and feature installed: indented (38) switch `globe` "Find lyrics online" caption "While lyrics are open, send the song title, artist, album and duration to lrclib.net. Imported lyrics stay on this Mac. Song titles, artist names and albums may also be checked with musicbrainz.org." -> `notchLyricsOnline` (false).
6. Switch `photo` "Find covers for Firefox and Zen" caption "When Firefox, Zen or another Firefox-based browser plays without a sharp cover, read its saved session on this Mac to find the song’s YouTube tab and load its thumbnail from i.ytimg.com. If no tab shows the song but a YouTube Music tab is open, send the song title, artist and region to itunes.apple.com and load the album art from mzstatic.com." -> `notchBrowserCovers` (false). Changing it calls `NotchMusicService.syncBrowserCovers()`.
7. FeatureSwitch `list.bullet` "Show upcoming songs" caption "See the actual upcoming songs shared by your player." -> `notchQueueEnabled` (true), feature notchQueue.
8. FeatureSwitch `waveform` "Bars follow the music" -> `notchLiveEqualizer` (false), feature notchLiveEqualizer; caption "Listens to the player’s own audio output to move the bars. macOS asks once to allow system audio recording. Nothing is stored or sent. Without it, the bars keep their usual motion." (when `NotchAudioLevelSupport.isSupported`), else "Needs macOS 14.4 or later." and the row is disabled. `[mac-only]` Windows wording: "Listens to the player’s own audio output to move the bars. Nothing is stored or sent. Without it, the bars keep their usual motion." (always supported - WASAPI).

**Music services** `[windows-only]` (card under the Music options, `MusicServicesCard`; Spotify and YouTube Music in the island's Library, see the Dynamic Island section of the README)
* Keys (flat settings, all off or empty by default, so nothing is sent until a service is set up): `notchMusicSpotify` (false, "Show Spotify in the island"), `notchMusicYouTube` (false, "Show YouTube Music in the island"), `notchSpotifyClientId` ("", the user's own Spotify app Client ID; public, not a secret), `notchYouTubeOpenIn` ("music"; "music" | "youtube", "Open songs in"), `notchMusicBrowserSource` ("" = automatic; "apple" | "spotify" | "youtube", the source the island's Library shows, set by its switcher). Sign-in tokens and the YouTube API key are never settings: they are encrypted for the Windows account (DPAPI) in `%APPDATA%\Trayloom\music\*.bin`, outside `settings.json` and so outside settings exports.
* Title **Music services**; caption "Find and play music from Spotify and YouTube Music in the island’s Library. Nothing is sent until you set a service up here." Without the island running: "Turn on the Dynamic Island to set up music services."
* Spotify: row **Spotify** with caption by state ("Add a Client ID below to connect." / "Not connected" / "Finish signing in in your browser…" / "Connected as {0}" or "Connected" / "Spotify asks you to connect again.") and one button that is **Connect** (prominent), **Cancel** while signing in, **Disconnect** when connected, **Reconnect** when Spotify asks again. While signing in: "If Spotify shows “Invalid redirect URI”, add the redirect URI below to your Spotify app, then try again."; when the browser could not be opened: "Couldn’t open your browser. Copy this link into it:" with the address and a Copy button. A failed sign-in shows the message of its status in orange. Switch **Show Spotify in the island** (a successful Connect turns it on). Text box **Client ID** (width 280, committed on Enter or when it loses focus, trimmed; changing it while signed in disconnects, caption "Changing the Client ID disconnects Spotify."). Captions: "Spotify only lets apps like Trayloom in through a Client ID. Create one at developer.spotify.com: Dashboard › Create app, pick Web API, and add the redirect URI below. Then paste the app’s Client ID here. Creating an app needs Spotify Premium." and, only if the build carries a shared Client ID, "Leave it empty to use Trayloom’s shared Client ID. It works only for Spotify accounts its owner has added." Row **Redirect URI** `http://127.0.0.1:43118/callback` (read-only, Copy button saying "Copied" for 2 s). Button **Open Spotify Dashboard** (https://developer.spotify.com/dashboard). Captions "Playing, queueing and seeking from the island need Spotify Premium. Without Premium, songs open in the Spotify app.", "Trayloom can read your playlists, liked songs, recently played songs and what’s playing, and control playback. It can’t change your library or playlists. Your sign-in is stored encrypted on this PC." and "Disconnect deletes the sign-in from this PC. To also remove Trayloom from your Spotify account, open Manage apps." with button **Manage apps** (https://www.spotify.com/account/apps/).
* YouTube Music: row **YouTube Music**; switch **Show YouTube Music in the island**; caption "Search YouTube Music right in the island. No account or key needed: Trayloom uses the same web service as music.youtube.com. It isn’t an official API, so it may stop working if YouTube changes it."; picker **Open songs in** (YouTube Music | YouTube); sub-heading row **Official API (optional)** with caption "If the built-in search stops working, add your own YouTube Data API key to use Google’s official API instead (about 100 searches a day)."; password box **API key** (the saved key is never put back in the box) with **Save** (checks the key with one 1-unit call and saves it only when Google accepts it) and **Remove**; status line "Saved key ending in {0}" / "Checking…" / "The key works." / "This key isn’t valid." / "Turn on YouTube Data API v3 for this key’s project." / "Today’s limit for this key is used up. It resets at midnight Pacific time." / "Couldn’t check the key. You’re offline."; caption "In Google Cloud Console create a project, enable YouTube Data API v3, then open Credentials › Create credentials › API key. The free daily limit allows about 100 searches." with button **Open Google Cloud Console** (https://console.cloud.google.com/apis/library/youtube.googleapis.com); caption "Search results come from YouTube Music, or from YouTube API Services when you add your own key. YouTube’s Terms of Service and Google’s Privacy Policy apply." with buttons **YouTube Terms** (https://www.youtube.com/t/terms) and **Google Privacy Policy** (https://policies.google.com/privacy).
* Automation ids: `Island.Settings.Spotify.Connect`, `Island.Settings.Spotify.ClientId`, `Island.Settings.Spotify.RedirectUri`, `Island.Settings.Spotify.ManualUrl`, `Island.Settings.YouTube.Key`, `Island.Settings.YouTube.Save`, `Island.Settings.YouTube.Remove`. Destination `island#content-music`.
* Source switch on the Music page `[windows-only]`: a 24 px plate directly under the head (above the timeline and the playback buttons, also when nothing is playing) always offers **Apple Music**, **Spotify** and **YouTube Music** (`Island.Music.Source.Apple|Spotify|YouTube`). The selected segment is the source the Library shows (the saved `notchMusicBrowserSource`, else the source of the player that is playing, else Apple Music). Choosing another segment turns Spotify or YouTube Music on when it is off (nothing is sent until the person connects or searches), remembers the choice, makes the now-playing controls follow a live media session of that source when there is one, and opens the Library on it (Spotify not yet connected shows its “Connect in Settings” prompt). Apple Music only ever shows the island's own Library: its player is never opened and nothing is played. Without Apple Music (or its library) its segment is dimmed and disabled with the tooltip “Apple Music library not found”.
* In the island: with Apple Music alone the Library looks as before. With two or more sources (Apple Music installed, Spotify on, YouTube Music on) the switch above selects the source the Library shows; the Library toggle's tooltip is then "Library" and the magnifier's "Search Spotify" or "Search YouTube Music". The Spotify list searches while typing (450 ms, two characters or more); YouTube searches on Enter, with no key needed (before the first search the link pills show and the body says "Press Enter to search"; results come from YouTube Music's own web service, "InnerTube" — `POST music.youtube.com/youtubei/v1/search` with client `WEB_REMIX`, the client version read once per session from the site's page with a built-in fallback, no cookies; Songs use the site's Songs filter and are paged with continuation tokens through "Show more", Playlists join the featured and community playlist filters, a playlist opens through `browse` with `VL` + its id; a failure says "YouTube Music didn’t respond. Try again."). A saved API key switches the search to Google's official YouTube Data API instead (one page, no "Show more"). Up next shows Spotify's queue while Spotify plays and is connected. Search box `Island.Stream.Search`, rows `Island.Stream.Result.N`.
* Apple Music Library, additions `[windows-only]`: (1) **Cover art for playlists.** A playlist row and the header of an open playlist show the playlist's own picture from Apple Music's artwork cache (`artwork.sqlite`, `item_to_artwork`, keyed by the playlist id like a song id); with none, a 2×2 mosaic of the first four distinct covers of its songs (one to three distinct covers show the first one whole); the list glyph stays until the picture is ready. Row tiles are 32 px (decoded at 64 px), the header tile 44 px (radius 9, decoded at 128 px). An open album or artist shows its first song's cover the same way. Pictures are re-read when the library file is reloaded. (2) **Header line.** An open playlist, album or artist shows a second line under its name, “40 songs · 2 hr 13 min” (“· 52 min” under an hour). (3) **Browse tabs.** Playlists · Songs · Albums · Artists · Recently added (`Island.Music.Tab.Playlists|Songs|Albums|Artists|Recent`; “Recently added” gets the widest segment); Albums and Artists list everything in name order with the first song's cover (artists round), “Artist · N songs” / “N songs”, and a tap opens the album or artist like “Go to album / artist” in a song's “…” menu. The selected tab is remembered while the page is rebuilt. Search is unchanged. (4) **Go to album / artist from the Music page.** While Apple Music plays and its library is read, a “…” button next to the playback-source button in the head offers **Go to album** and **Go to artist**; the playing song is matched to the library by title, artist and album (an item is left out when there is no match, the button when neither is left) and the Library opens on Apple Music with that album or artist open.
* Shuffle and repeat on the Music page `[windows-only]`: the transport row shows **Shuffle** (left) when the media session offers it, and **Repeat** (right, beside the output button, which shrinks to 32 px next to it) when the session offers `IsRepeatEnabled` and reports an `AutoRepeatMode`. A press cycles repeat Off → All (E8EE, “Repeat”) → One (E8ED, “Repeat one”) → Off with `TryChangeAutoRepeatModeAsync`. Active toggles use the accent colour on a round white 12 % plate. A change of either state updates only the two buttons (no page rebuild); the buttons appear or go when the session starts or stops offering them.

**Notifications** (`notifications`)
* Switch `bell.slash` "Hide the system banner" caption "Hide the original while the Dynamic Island shows it." -> `notchDismissNativeNotifications` (false).
* If `notchNotificationsEnabled` and Accessibility missing: PermissionRow(accessibility) `[mac-only]` (Windows: replace with the notification-listener access row, 13.6).

**Downloads** (`downloads`) - `NotchDownloadsSettingsControls`, VStack spacing 10, toggles drawn with TrailingSwitchToggleStyle:
* Switch "Downloads" (`notchDownloadsEnabled`, true; disabled if feature not installed).
* Caption "Choose the folder where your browser saves downloads. Only that folder is watched."
* If folder unavailable: orange caption "This folder is unavailable. Choose it again to restore access."; else if a folder is chosen: label (`folder` icon + folder name, middle-truncated, caption).
* Small buttons: **Choose Folder…**; and **Forget Folder** when a folder is chosen or unavailable. State persisted as a security-scoped bookmark (`notchDownloadsFolderBookmark`, not registered, never backed up).

**Calendar** (`calendar`) - see section 9 for the per-calendar list.
1. Callout secondary "Read your calendars to show upcoming appointments. Events stay on this Mac."
2. If calendar access == full: green label `checkmark.circle.fill` "Granted"; else buttons **Allow Calendar Access** (disabled while a request is in flight) and **Open System Settings** `[mac-only]` (Windows: "Open Windows Settings"; see 13.5).
3. Divider.
4. Switch `calendar.badge.clock` "Event countdown" caption "Show every timed event in the closed island before it starts. To count down to only some, turn this off and right-click them on the island’s Calendar page. Titles may appear in screen captures." -> `notchCalendarCountdown` (false).
5. Menu row `timer` "Time before event" -> `notchCalendarCountdownLead` (Int minutes, default 60; clamped 5...240). Always shown (events chosen from their menu use the same lead). Options = sorted union of presets {15, 30, 60, 120} and the stored value, labelled with a short duration: "15 min", "30 min", "1 hr", "2 hr" (a stored 45 shows "45 min").
6. Switch `hourglass` "Time left in current event" caption "Show a timed event in progress in the closed island during the hour before it ends. Its title may appear in screen captures." -> `notchCalendarTimeLeft` (false).
7. Switch `number` "Week numbers" -> `notchCalendarWeekNumbers` (false).
8. If full access: `NotchCalendarSelection` (section 9).

**Timer** (`timer`)
* Switch `eye.slash` "Hide timer countdown" -> `notchHideTimerCountdown` (false), disabled if feature notchTimer is not installed.
* Switch `speaker.wave.2` "Play sound when time is up" -> `notchTimerSoundEnabled` (true), same disabling.

**Camera** (`camera`)
* Callout secondary "Open a live mirror here. The camera stops when you leave this view."
* If camera permission granted: green label "Granted"; else buttons **Grant access** and **Open System Settings…** `[mac-only wording]` (Windows: see 13.5).

**Files** (`files`)
* Choice row (`tray.full`) "Files" - segmented **Dynamic Island** | **Separate window**, key `notchShelf` (true = island), disabled if feature shelf missing.
* If `notchShelf`: switch `hand.draw` "Show a drop target while dragging" -> `notchDragReveal` (true).
* If feature mediaTools installed: caption "Drag images or one video onto the island, then drop on Optimize media to choose how to save a copy."

**Clipboard** (`clipboard`)
* Choice row (`doc.on.clipboard`) "Clipboard" **Dynamic Island | Separate window** -> `notchClipboardWindow` (true), disabled if clipboardHistory missing.
* Switch `doc.on.clipboard` "Notify when something is copied" caption "Copied content stays private until you open the clipboard. These options follow the original feature’s settings." -> `notchClipboard` (true).
* If `notchClipboardWindow` and feature installed: choice row `rectangle.compress.vertical` "Entry size" segmented **Compact | Comfortable** -> `notchClipboardCardSize` (default "comfortable"; any unknown/unset value reads comfortable) with caption (inset 38) "Compact entries show one or two lines and open up when the pointer rests on them."

**Captures** (`captures`)
* FeatureSwitch `camera.viewfinder` "Show screenshot previews here" -> `notchCapture` (true), feature screenshot.
* If on and screenshot installed: menu picker (inset 38), label "Default action", options **Ask each time** ("" ), **Save** (save), **Save & Copy** (saveAndCopy), **Copy** (copy), **Edit** (edit) -> `screenshotDefaultAction` (String, "").

**Scratchpad** (`scratchpad`)
* Choice row (`note.text`) "Scratchpad" **Dynamic Island | Separate window** -> `notchScratchpad` (true).

**AI Agents** (`agents`) - section 10. **Watch** (`watch`): `NotchWatchSettingsControls` (TrailingSwitch style): switch "Watch" (`notchWatchEnabled`, true; disabled if feature missing), caption "Turn any part of any window into a live activity in the Dynamic Island, with an alert when it changes, finishes or shows what you are waiting for."; if on: switch "Play a sound" (`notchWatchSound`, true) + PermissionRow(screenRecording) `[mac-only]`.
**Mixer / System / Tools**: no options ("arrange themselves in the island").

### 3.3 `NotchEditorItem` (toggle card) - used by Controls cards, shortcut grid, Indicators grid, Agents cards
* ZStack(top-trailing): main button + corner status button.
* Main: VStack spacing 8: glyph 20pt medium (accent when `included && available`, else secondary); title 11pt medium, centered, max 2 lines, fixed height 28; optional reason line (`.caption2`, 3 lines reserved, accent colour if it has an action else secondary) shown when unavailable or when `reservesReason` (keeps all cards in a row equally tall). Min height 76, padding 10, radius 12, fill secondary 5% (accent 10% + 1px accent 55% stroke when `selected`, unused in Settings).
* Click on main: if available -> `select()` (in Settings = toggle `included`); if unavailable -> `unavailableAction` (open the feature in Features / jump to the page); button disabled if unavailable and no action.
* Corner button (28x28 hit area, glyph 15pt): `checkmark.circle.fill` (included), `plus.circle` (not included), `minus.circle` (unavailable, no action), `arrow.up.right.circle` (unavailable with action). Accent when included+available else secondary. AX label = title, or the reason when unavailable.
* Tooltip = reason or title.

### 3.4 Persistence formats of the Content tab
| Key | Format |
|---|---|
| `notchModuleOrder` | comma-separated module raw values in display order (unknown ignored; missing modules appended in default order on read). Default "". |
| `notchHiddenModules` | comma-separated, **sorted alphabetically**, raw values of hidden modules. Default "". |
| `notchHiddenControls` | comma-separated sorted raw values of hidden control items. Default `keyboardLight,microphone,screenshot,recording,speedTest,panel,commandBar,scratchpad`. A saved list is read whole (absent items are SHOWN). |
| `notchControlOrder` | comma-separated order of control items (levels and music are excluded from the shortcut grid but kept valid). Default "". |

### 3.5 Windows mapping (Content tab)
Two- or three-column Grid with a `ListBox` for sections (drag/drop via `DragDrop.DoDragDrop` or a pointer-capture reorder, see 8), a `ScrollViewer` for the detail and a preview host that instantiates the REAL section controls (`IslandSection.Create`) inside a scaled `Viewbox` with `IsHitTestVisible=false`. Tile glyphs = Segoe Fluent Icons. Module availability maps to Trayloom features (mixer=`volume-mixer`, clipboard=`clipboard-history`, files=`shelf`, tools=quick panel, calendar=`island-calendar`, timer=`island-timer`, downloads=`island-downloads`, ...). Options that depend on macOS-only subsystems: Accessibility rows (drop), screen-recording permission (drop), "macOS 14.4" caption (reword). See section 13 for each API.

### 3.6 Gap vs Trayloom
Trayloom registers music (always), timer, calendar, downloads as sections of the expanded panel via `IslandHub.RegisterSection` with fixed `Order`; there is no user ordering, no hide/show, no preview, no per-section options page (only flat feature settings). Missing sections: Controls, Mixer, Clipboard, Captures, Files, System, Tools, Notifications, Camera, Scratchpad, AI Agents, Watch.

---------------------------------------------------------------------------------------------------

## 4. Tab "Activity" (activityPage)

### 4.1 Layout (VStack spacing 20, three cards)
**Card "At rest"** (`editor.resting`)
1. HStack(top, spacing 10) of up to 4 *resting choices* (`idleChoice`), each a VStack(spacing 6) containing a button and, below it, an optional fix link:
   * Button content (VStack spacing 14, full width, vertical padding 14, radius 12, fill secondary 5% / accent 12% when "marked"): a mini black capsule (padding 10) containing HStack spacing 14: [glyph 11pt white] [black rounded rect 20x12 radius 4 = the camera] [trailing: "76%" (battery) / "62%" (agents) 9pt medium, or waveform (music) / minus (none) 9pt], then the title (11pt medium). For "Nothing": an empty 50x12 spacer inside the capsule, or - when the Companion rests there (`enabled && notchMascotEnabled && !notchMascotHidesWhenIdle && Companion installed`) - the title becomes **Companion** and the capsule shows the mascot (12px, current look, on the side from `notchMascotSide`, mirrored with an empty 12px spacer) beside the camera.
   * Choices and titles: **Nothing** (`none`, `minus`), **Battery** (`battery`, `battery.75percent`; only if the machine has an internal battery; unavailable reason = `Enable “Power” in Features.`, fix opens Features), **Music** (`music`, `music.note`), **AI limits** (`agents`, `sparkles`; only offered once the AI section is on (`NotchSupport.canRest(with: .agents)`) or while it is the saved choice).
   * *Marked* = the choice the island actually rests with (`restingChoice`, which falls back to none for a saved battery choice on a machine without a battery) OR (it is waiting and is the saved choice -> fainter mark). *Waiting* (`reason != nil && !canRest(with:)`): whole button opacity 0.45 but still clickable; under it a plain link (caption2, accent, centered, wraps) with the reason and action: for Music/Agents reason = `Show “<Page title>” on the Content tab.` (or `Enable “<Feature>” in Features.` when that feature is missing) and the action jumps to the Content tab with that section selected (or to Features). AX value "Hidden from the island" when waiting+saved.
   * Selecting writes `notchIdleContent` ("none"|"battery"|"music"|"agents", default **"music"**).
2. If the resting choice is Battery: low-battery controls (4.2).
3. Switch row `menubar.rectangle` **Show over the menus** with caption "Keeps the timer, music and other compact activity on screen when the menu bar has no room, covering the menus beside the camera." -> `notchCoversMenus` (true). `[mac-only]` see 4.4.

**4.2 Low battery controls** (appear under the choices only while Battery is the resting choice; nested under the switch they follow)
* Switch `battery.25percent` "Turn red when low", caption "Shows the charge in red at or below this level while on battery." -> `notchLowBatteryTint` (false).
* If on:
  * Level slider "Red level" 1...100 step 1 (inset 38; title left, "NN%" monospaced secondary right) -> `notchLowBatteryThreshold` (Int, default 10, sanitized to 1...99). Setting it raises the amber level to at least red+1.
  * Switch `battery.50percent` "Turn amber first", caption "Shows the charge in amber at or below a higher level, as an early warning." -> `notchLowBatteryEarly` (false).
  * If on: slider "Amber level" 1...100 -> `notchLowBatteryEarlyThreshold` (Int, default 20, kept >= red+1, clamp 2...100).
  * Switch `menubar.rectangle` "Also in the menu bar" -> `notchLowBatteryMenuBar` (true) (tints the menu-bar/tray battery reading too; neither colour shows while plugged in).

**Card "Indicators"** (`editor.feedback`) - short transient notices the island shows
* Adaptive grid (min 116, spacing 10) of toggle cards (3.3). Every card is `reservesReason = !(all cards available)` so heights match. Cards, in order:

| # | Title | SF symbol | Key (default) | Available when | Unavailable reason / action |
|---|---|---|---|---|---|
| 1 | Volume | speaker.wave.2 | `notchVolume` (true) | feature mixer | `Enable “Volume mixer” in Features.` -> Features |
| 2 | Brightness | sun.max | `notchBrightness` (true) | feature brightness AND `brightnessControlEnabled` | brightness missing: `Enable “Displays” in Features.`; present but control off: `Enable “Control displays” in its settings.` -> opens Displays settings |
| 3 | Keyboard light | light.max | `notchKeyboardLight` (true) | brightness feature AND hardware has a keyboard light | no feature: Features reason; else "Keyboard backlight control is unavailable on this Mac." (no action) |
| 4 | Microphone | mic | `notchMicrophone` (true) | feature micMute | `Enable “Mute microphone” in Features.` |
| 5 | Battery | battery.75percent | `notchBattery` (true) | feature monitorPower (card omitted entirely without an internal battery) | `Enable “Power” in Features.` |
| 6 | Accessory alerts | headphones | `notchAccessoriesEnabled` (true) | features notchAccessories AND monitorPower | reason for whichever is missing |
| 7 | Clipboard | doc.on.clipboard | `notchClipboard` (true) | clipboardHistory installed AND `clipboardHistoryEnabled` AND clipboard section shown | not installed: Features reason; not enabled: `Enable “Save clipboard history” in its settings.`; else `Show “Clipboard” on the Content tab.` -> opens page |
| 8 | Captures | camera.viewfinder | `notchCapture` (true) | feature screenshot AND Captures section shown | `Enable “Screenshot” in Features.` / `Show “Recent captures” on the Content tab.` |
| 9 | New track | music.note | `notchTrackChange` (true) | Music section shown | `Show “Now Playing” on the Content tab.` |
| 10 | Input source | keyboard | `notchInputSource` (true) | always | - |

* Below the grid: if accessories on -> caption "Show connected accessories and warn once when their battery falls to 20%."; if island on and (volume||brightness||keyboardLight) and Accessibility missing -> PermissionRow(accessibility) `[mac-only]`.

**Card "Lock Screen"** `[mac-only]` (title = the OS's pane name)
* Switch `lock.display` "Show on the Lock Screen" caption "Music and the Dynamic Island’s activities, like a timer, AI agents, downloads and your next event, appear above the password field. Anyone who can see your screen can read them." -> `notchLockScreen` (false).
* Switch `speaker.wave.2` "Lock and unlock sounds" caption "Play the macOS padlock sound when your Mac locks and unlocks." -> `notchLockSounds` (false).

### 4.3 Settings table (this tab)
| Key | Type | Default | Allowed | Effect |
|---|---|---|---|---|
| `notchIdleContent` | String | "music" | none, battery, music, agents | What the closed island shows when nothing else is active. A choice that cannot be shown waits (stays saved) until its section/feature returns. Real playback takes over when "Companion" rests and music plays. |
| `notchCoversMenus` | Bool | true | | `[mac-only]` closed activity may cover the menus beside the camera when the menu bar has no room. |
| `notchLowBatteryTint` / `Threshold` / `Early` / `EarlyThreshold` / `MenuBar` | Bool/Int/Bool/Int/Bool | false/10/false/20/true | thresholds 1...99 and 2...100 (amber > red) | Red/amber battery reading while on battery. |
| `notchVolume`, `notchBrightness`, `notchKeyboardLight`, `notchMicrophone`, `notchBattery`, `notchAccessoriesEnabled`, `notchClipboard`, `notchCapture`, `notchTrackChange`, `notchInputSource` | Bool | all true | | Whether that event shows a notice in the closed island (`NotchEvent` router; priority and durations: volume/brightness/keyboard/mic/input 1.6 s prio 3; capture 12 s, timer/watch 6 s prio 2; battery/accessory 4 s, notification/track 3 s, agents 5 s prio 1; clipboard 2.5 s, download 6 s prio 0). |
| `notchLockScreen`, `notchLockSounds` | Bool | false, false | | `[mac-only]` |

### 4.4 Behaviour / states
* Choice marking and waiting as described; selecting a waiting choice still saves it.
* The Battery choice and battery card disappear on desktops (no internal battery).
* Notice routing is gated by `NotchSupport.routes(event)`: island on AND event toggle on AND the related feature on (brightness also needs `brightnessControlEnabled`; clipboard needs `clipboardHistoryEnabled` + section; capture needs screenshot + section; track needs music section; inputSource only its toggle).

### 4.5 Windows mapping
* Resting choices: identical UI. Battery presence = `GetSystemPowerStatus(...).BatteryFlag != 128 (no battery)` or `Windows.Devices.Power.Battery.AggregateBattery.GetReport()`; battery % via the same; "plugged in" = `PowerLineStatus`. Amber/red tint on the pill reading, and on the tray/battery readout for the "Also in the menu bar" switch (`[mac-only]` wording; documented Windows deviation: label it **"Also in the tray icon"**).
* **Show over the menus**: Windows has no menu bar and the island already floats above all windows -> hide the row (keep key `notchCoversMenus`=true for backup compat).
* Indicator events: volume = `IAudioEndpointVolume` callbacks (`OnNotify`) for default render endpoint; microphone mute = capture endpoint mute notify; brightness = WMI `WmiMonitorBrightnessEvent` / DDC; keyboard light = not exposed by Windows for most devices -> card hidden unless a keyboard-backlight API is available (`Windows.Devices.Lights.Lamp` with `LampArray` / Dynamic Lighting); battery = `SystemEvents.PowerModeChanged` + `PowerSettingRegisterNotification(GUID_BATTERY_PERCENTAGE_REMAINING)`; accessories = Bluetooth/HID battery (`DeviceInformation` Bluetooth LE GATT Battery service 0x180F, or PnP property `{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2` for classic BT); clipboard = `AddClipboardFormatListener`; captures = the future screenshot feature; new track = SMTC `MediaPropertiesChanged`; input source = `WM_INPUTLANGCHANGE`/`GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow()))` poll (250 ms) or `InputLanguageChanged`.
* Lock Screen card: Windows cannot draw above the lock screen (secure desktop owned by LogonUI; no supported overlay). **Hide "Show on the Lock Screen"**; "Lock and unlock sounds" is feasible (`WTSRegisterSessionNotification` -> `WM_WTSSESSION_CHANGE` WTS_SESSION_LOCK/UNLOCK, play `%SystemRoot%\Media\Windows Unlock.wav`/a bundled lock tick). If kept, show a one-row card titled **Lock Screen** with only the sounds row, caption "Play a sound when your PC locks and unlocks." (deviation: "macOS padlock sound" wording replaced).

### 4.6 Gap vs Trayloom
Nothing of this tab exists. Trayloom's collapsed pill shows now-playing (or a timer/other `CompactInfo`) with a single `compactTitle` toggle; no resting choice, no indicators/notices system beyond `IslandHub.ShowAlert`, no battery tinting.

---------------------------------------------------------------------------------------------------

## 5. Tab "Behavior" (behaviorPage)

### 5.1 Layout (VStack spacing 20, four cards; Windows adds "Position" as a fifth)
**Card "Opening"** (`editor.opening`) rows in order:
1. 2-column flexible grid (spacing 8) of 4 `choice` tiles:
   | Tile | SF symbol | selected when | sets |
   |---|---|---|---|
   | **Click to open** | cursorarrow | `!notchOpenOnHover` | hideUntilHover=false, hover=false |
   | **Preview on hover** | rectangle.topthird.inset.filled | hover && !hoverExpands && !hideUntilHover | hideUntilHover=false, hover=true, hoverExpands=false |
   | **Expand on hover** | arrow.up.left.and.arrow.down.right | hover && hoverExpands && !hideUntilHover | hideUntilHover=false, hover=true, hoverExpands=true |
   | **Hidden until hover** | eye.slash | hover && hideUntilHover | hover=true, hoverExpands=true, hideUntilHover=true |
2. If hover && hideUntilHover: switch `sparkles` "Show activity and indicators" -> `notchHiddenShowsActivity` (false); caption (only while ON) "Comes into view while an activity runs or an indicator shows, then hides again."
3. If hover || (hideInFullscreen && hoverInFullscreen): **Activation time** slider 0.10...1.00 s step 0.05, displayed "%.2f s" (e.g. "0.25 s"), key `notchHoverDelay` (default 0.25), caption "Keep the pointer over the island for this long to open it."; and, only if `notchHoverExpands`: **Closing time** slider 0.10...2.00 s step 0.05, key `notchCloseDelay` (default 0.18), caption "Wait this long after the pointer leaves the island before closing it." (VStack spacing 6: title left + value right monospaced secondary, slider, caption.)
4. Switch `keyboard` **Global shortcut** -> `notchShortcutEnabled` (false). If on: `ShortcutPreferenceRow(role .notch)` (inset 38; label "Shortcut"; recorder; default Control+Option+Command+I stored in `notchShortcut`) and, if registration failed, orange caption "macOS rejected this shortcut. Choose another one." `[mac-only]` (Windows: "Windows rejected this shortcut. Choose another one.").
5. FeatureSwitch `hand.draw` **Dynamic Island Gestures** -> `notchGesturesEnabled` (true), feature notchGestures; caption (only while on) "Scroll down to open. Scroll up over the top row to close. Swipe left or right over the music to change tracks. Lists keep their own scrolling." (feature description: "Open and close the Dynamic Island with scrolling, and swipe to change tracks.")
   * Windows (island at the bottom edge): the caption reads "Scroll up to open. Scroll down over the top row to close. Swipe left or right over the music to change tracks. Lists keep their own scrolling." The wheel directions follow the opening direction; the edge is read from the stored position, like the Capsule fit label.
6. Switch `waveform.path` **Haptic feedback** -> `notchHapticFeedback` (true). (Hint string exists, unused here: "Gentle taps when opening, switching sections or adjusting timer minutes, on a compatible trackpad.")
7. Menu row `arrow.uturn.backward` **When reopening** -> keys `notchReturnHome` (false) + `notchHomeModule` ("controls"). Options (tag): **Last page** ("" -> returnHome=false), **Open app panel** ("appPanel"), **Explore** ("explore"), then one item per currently shown section by title (tag = module raw value); a saved module that is no longer shown is appended disabled. Saved but invalid value displays as Controls.
8. Switch `livephoto` **Open the visible activity** -> `notchOpensActivity` (true), caption "When the closed island shows music, a timer or another activity, it opens on that page instead."

**Card "Display"** (`text.display`)
1. Switch `arrow.up.left.and.arrow.down.right` **Hide content in full screen** -> `notchHideInFullscreen` (false).
2. If on: indented switch `cursorarrow` **Hidden until hover in full screen** -> `notchHoverInFullscreen` (false).
3. 2-column grid of 5 `choice` tiles (Windows: 6, see below) -> `notchDisplay`: **Automatic** (`display.2`, "automatic"; also selected for any unknown stored value), **Built-in display** (`laptopcomputer`, "builtIn"), **Main display** (`display`, "main"), **Follow pointer** (`cursorarrow.motionlines`, "pointer"), **All displays** (`rectangle.on.rectangle`, "all").
   Semantics: automatic = built-in notched display, else any notched display, else main; builtIn = built-in panel only (island hidden while the lid is closed, main display if the machine has no built-in panel); main = main display; pointer = closed island follows the pointer's display; all = closed island on every display (pointer display is the responsive one).
   Windows (island placement): a sixth tile **Choose a display** (`display`, "specific") stores `notchDisplay = "specific"` and the chosen display in `notchDisplayId` (the display the island is on when no id is stored yet). While it is selected, a menu row **Display** lists the connected displays as `Name — W × H` (physical pixels; the main display gets the suffix " · Main display"). If the stored id is not connected the row shows "Choose a display" and a caption "This display isn’t connected. Dynamic Island shows on the main display until it’s back." The id is kept, so the island returns when the display does. On Windows automatic and main = the main display, builtIn = the internal panel (the main display when there is none), pointer and all = the display under the pointer.

**Card "Position"** (Windows addition, right after "Display"; the Behavior tab then has five cards: Opening, Display, Position, Where things open, Privacy)
1. 3 x 2 grid of tiles, each a small screen with the island drawn where the preset puts it: **Top left**, **Top center**, **Top right**, **Bottom left**, **Bottom center**, **Bottom right**. A tile writes the side (X 0 / 0.5 / 1) and edge for the display the island is on and keeps the current distance from the edge; it is selected when the stored side and edge match. While none matches (a dragged island) the caption **Custom position** shows.
2. Slider **Distance from edge**, 0...MaxOffset step 2 (`IslandPlacement.MaxOffsetFor` of the display; the row is hidden when that is under 2): the gap in island units between the edge of the work area and the island. 0 = attached (the notch shape is allowed), more = floating, always the capsule.
3. Buttons **Move island…** (enters Move mode, `IslandWindow.EnterMoveMode`; disabled while the island is off) and **Reset position** (top centre on every display; a "specific" display choice goes back to automatic).
4. Switch **Lock position** -> `notchPositionLocked` (false); caption "Dragging the closed island doesn’t move it. “Move island…” still works."
5. Caption "Drag the closed island to move it. Pages open away from the edge it rests on."

The tab follows the island: a change of `notchPosition`, `notchPositionsByDisplay`, `notchDisplay`, `notchDisplayId` or `notchEnabled` from elsewhere (a drag, the island's menu, a developer action) rebuilds it; changes made by the tab itself do not.

**Card "Where things open"** (`editor.destinations`) - every row is a `SettingsChoiceRow` with segmented **Dynamic Island** | **Separate window** (true = Dynamic Island):
* `bubble.middle.top` **Open app panel** -> `notchAppPanel` (default **false** = separate window); caption "Choose where clicking the Trayloom menu bar icon opens the app panel, on any display." `[mac-only wording]` Windows: "...clicking the Trayloom tray icon...".
* Switch `menubar.rectangle` **Hide the menu bar icon** -> `notchHidesMenuBarIcon` (false); caption "Settings and the app panel open from Dynamic Island. The icon returns when the island is off or hidden in full screen, or when an update is ready or the microphone is muted." Windows: **Hide the tray icon** (documented deviation) with the same caption wording using "tray icon".
* **Tools** (symbol of the Quick panel, `wand.and.rays`) -> `notchQuickPanel` (true), disabled if quickLauncher missing.
* **Clipboard** (`doc.on.clipboard`) -> `notchClipboardWindow` (true), disabled if clipboardHistory missing.
* **Files** (`tray.full`) -> `notchShelf` (true), disabled if shelf missing.
* **Captures** (`camera.viewfinder`) -> `notchCaptureControls` (true).
* **Scratchpad** (`note.text`) -> `notchScratchpad` (true), disabled if scratchpad missing.

**Card "Privacy"** (`editor.privacy`)
* Switch `camera.viewfinder` **Show in screenshots and videos** -> `notchShowInCaptures` (true). Off hides the island window from screen captures/recordings.

### 5.2 Settings table (this tab)
| Key | Type | Default | Allowed | Effect |
|---|---|---|---|---|
| `notchOpenOnHover` | Bool | false | | Hover opens the island (otherwise click). |
| `notchHoverExpands` | Bool | true | | With hover on: true = expand fully, false = "peek" preview that closes when the pointer leaves. |
| `notchHideUntilHover` | Bool | false | requires hover | Closed island invisible until pointer reaches it. |
| `notchHiddenShowsActivity` | Bool | false | requires hideUntilHover | Appears while an activity/notice shows. |
| `notchHoverDelay` | Double s | 0.25 | 0.10...1.0 (0.05) | Dwell before hover opens. |
| `notchCloseDelay` | Double s | 0.18 | 0.10...2.0 (0.05) | Wait before an island opened by hover closes (also hover-corridor exit 0.18). |
| `notchShortcutEnabled` / `notchShortcut` | Bool / String | false / Ctrl+Opt+Cmd+I | | Global shortcut toggling the island. |
| `notchGesturesEnabled` | Bool | true | | Scroll/swipe gestures. |
| `notchHapticFeedback` | Bool | true | | Trackpad haptics on open/section switch/timer minutes. |
| `notchReturnHome` / `notchHomeModule` | Bool / String | false / "controls" | module raw value, "appPanel", "explore" | Page shown when reopening. |
| `notchOpensActivity` | Bool | true | | Opening on the visible activity's page. |
| `notchHideInFullscreen` / `notchHoverInFullscreen` | Bool | false / false | | Hide in full-screen apps / still reachable by hover. |
| `notchDisplay` | String | "automatic" | automatic, builtIn, main, pointer, all, specific (Windows) | Which display(s). |
| `notchDisplayId` | String | "" | display key | Windows. The display used when `notchDisplay` = "specific": the monitor's device interface path (e.g. `\?DISPLAY#STD0110#4&…#{…}`); matched exactly, then by hardware id (`STD0110`), then by GDI name. Kept while that display is unplugged. |
| `notchPosition` | String | "0.5,top,0" | `x,top|bottom,offset` | Windows. Where the island rests: x = its centre across the display's work area (0 left ... 1 right), the edge, offset = distance from that edge in island units (DIP before `notchZoom`). The fallback for displays without an entry in `notchPositionsByDisplay`; rewritten on every save. |
| `notchPositionsByDisplay` | String (JSON) | "" | `{"<display key>": "x,edge,offset"}`, 16 entries | Windows. The position per display; the oldest entry is dropped past 16. |
| `notchPositionLocked` | Bool | false | | Windows. Dragging the closed island is off; "Move island…", the presets and the slider still work. |
| `notchAppPanel`, `notchQuickPanel`, `notchClipboardWindow`, `notchShelf`, `notchCaptureControls`, `notchScratchpad` | Bool | false, true, true, true, true, true | | Island vs separate window for each destination. |
| `notchHidesMenuBarIcon` | Bool | false | | Remove the app's menu-bar item while the island is on (icon returns on update/mic muted/island off or hidden). |
| `notchShowInCaptures` | Bool | true | | Island visible in screenshots/recordings. |
| `panelControlNotch` | Bool | true | | App panel control that opens the island (registered default). |

### 5.3 Windows mapping
* Hover = pointer-in tests against the island rect from a low-level mouse hook already used by gestures (`IslandHub.Pill`) plus `MouseEnter/Leave` on the window; dwell timer with `notchHoverDelay`; "peek" = open to the first page while the pointer stays, close after `notchCloseDelay`.
* Hidden until hover: set window opacity 0 / `WS_EX_TRANSPARENT` click-through and hover strip of 1-2 px at the top edge (a 0-alpha layered hit-test band) so the pointer can still wake it.
* Global shortcut: `RegisterHotKey`; Windows default **Ctrl+Alt+Shift+I** is suggested (Win-key combos are reserved) - check the port's hotkey convention; if registration fails show orange caption (Windows wording above).
* Haptics: `PointerDevice`/`Windows.Devices.Haptics.SimpleHapticsController` is only available on devices with a haptic pen/touchpad; **show the row only when a haptics controller exists** (keep key default true).
* Full screen detection: reuse `IslandNative.IsFullscreenForeground` (already used for Trayloom `hideInFullscreen`).
* Display options: Automatic = internal panel if present and active (`DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL`/`DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED`, `EnumDisplayMonitors` + `GetDisplayConfigBufferSizes/QueryDisplayConfig`), else primary monitor; Built-in = internal panel only (hidden when lid closed = internal path inactive); Main = primary (`MONITORINFOF_PRIMARY`); Follow pointer = `MonitorFromPoint(GetCursorPos)` re-evaluated on a 150 ms poll while closed; All = one closed pill window per monitor, the pointer's monitor responsive (extra windows are visual mirrors).
* Placement (Windows addition; where it differs, it supersedes the Display options bullet above): Automatic and Main = primary monitor, Built-in = internal panel else primary, Follow pointer and All displays = the monitor under the pointer, Choose a display = `notchDisplayId` (primary while it is unplugged). The island is placed on the monitor's **work area** (never over a top, left or right taskbar; a bottom island rests on the taskbar), shrunk by 2 px on an edge with an auto-hide taskbar. The position is an `IslandPosition(X, Edge, Offset)` per display (see the keys), snapped on drop. Dropping the island on another display while the choice is Automatic, Built-in, Main or Choose a display switches the choice to "specific" for that display; Follow pointer and All displays never change.
* Windows deviations from Vorssaint (placement): (1) a closed island now has a context menu (Move island, Reset position when not at the default, Settings…) and the open island's menu gains "Move island"; (2) the reference is the work area, not the monitor bounds, so the island no longer covers a top taskbar and pages are capped to the work area; (3) the notch silhouette only shows while attached (distance 0), a floating island is always the capsule; (4) a bottom island opens its pages upward and the gesture wheel directions swap; (5) the island stays on every virtual desktop (tool window), as before.
* Where things open: "Open app panel" = the existing `QuickPanelWindow` flyout anchored under the tray icon vs the island page; "Hide the tray icon" = `NotifyIcon.Visible=false` with the same re-show rules (update ready, mic muted, island off/hidden in full screen).
* Privacy: `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)` (Windows 10 2004+) when `notchShowInCaptures` is off.

### 5.4 Gap vs Trayloom
Has: `trigger` (hover|click, default hover), `monitor` (primary|cursor, default primary), `collapseDelay` (200/450/900/2000 ms choices, default 450), `hideInFullscreen` (default **true**), global actions (toggle, play/pause, next, previous) without default hotkeys, a separate gestures feature (`invert`, `tracks`). Differences: Vorssaint defaults are click-to-open, hideInFullscreen false, close delay 0.18 s slider, 4 opening modes, 5 display modes, per-destination routing, reopening choice, hidden-until-hover, privacy.

---------------------------------------------------------------------------------------------------

## 6. Tab "Companion" (NotchMascotSettings; also a standalone page when not embedded)

Visible only if feature `notchMascot` is installed. Standalone (sidebar item "Companion") adds padding 22 on all sides; embedded adds only bottom 22. ScrollView > VStack(spacing 20).

### 6.1 Layout
1. **Header**: left VStack: **Companion** (22pt bold) + hint "A little friend who lives in the Dynamic Island. It rests there when nothing else is showing, and comes out over music and activities to say hello and react to what happens." (callout secondary); right: master switch (AX "Companion") -> `notchMascotEnabled` (false).
2. **Island note** - only when the island is off or uninstalled: HStack (padding 12, secondary 35% background, radius 14): 26px accent tile with the island symbol (`macbook`), text "The companion lives in the Dynamic Island." (+ caption `Enable “Dynamic Island” in Features.` when not installed), then button **Enable Dynamic Island** (sets `notchEnabled=true`, syncs) or, if not installed, **Open Features**.
3. **Stage card** (padding 16, secondary 35% bg, radius 16, VStack spacing 14):
   * *Stage* 104pt high, radius 12 with 1px primary 7% stroke: wallpaper gradient (accent 30%->8% dark / 20%->5% light, top-leading to bottom-trailing), translucent menu bar (black 28% dark / white 55% light) with placeholder capsules/circles (menus 0.55/0.85/0.7/0.75 x bar height, 4 status dots) that only draw where they fit clear of the island, the black island strip drawn at 1x-2x real size (scale = clamp(1,2, stageWidth*0.66/strip.width, (104-36)/menuBarHeight)) with the companion resting on its side of the camera (wing 44; camera lens 6pt drawn in black for a notched display; capsule: centred, no camera). Asleep (feature switch off): "z" glyphs drift up every 2.7 s (30 fps; 2 static z's with Reduce Motion), lids closed. Clicking the stage plays the next reaction in order (cycles through all moments except "A visit"). AX: label "Companion preview", hint "Click to see it react.".
   * Moment title: **See how it reacts** (.headline) and, right, small button `hand.wave` **Say Hi** (tooltip "Plays a visit in the Dynamic Island now."), disabled unless `enabled && islandOn`; if the island has no room it plays the visit in the stage instead.
   * Chips (flow layout, spacing 6; capsule, caption medium, padding H9/V5; playing = accent 16% + 1px accent 50% stroke; hover primary 11%; others primary 6%; dimmed to 45% while a visit plays; disabled while the companion is off). 14 moments in order: **A visit** (figure.walk), **Timer started** (timer), **Time is up** (alarm), **Now Playing** (music.note), **AI Agents** (sparkles), **Happening now** (calendar), **Download complete** (arrow.down.circle), **Download failed** (exclamationmark.triangle), **Screenshot** (camera.viewfinder), **Mic Mute on** (mic.slash), **Keep awake** (cup.and.saucer), **Charging** (bolt), **Low battery** (battery.25percent), **Mac unlocked** (lock.open) `[mac-only wording]` -> Windows: **PC unlocked**. Playing a moment shows its name as a caption pill (regular material, capsule, caption semibold) 10pt above the stage bottom; "AI Agents" is a two-beat moment (ready then celebrate).
   * Footer label (`hand.point.up.left`, caption secondary, dimmed 45% when asleep): "Click to see it react. Rest the pointer on it to pet it. Left alone, it dozes off until you come back."
   * Stage reactions: switching on -> wakeUp; off -> yawn; changing side -> crosses behind the camera with a wink; visits switched on -> plays a visit; reactions switched on -> perk; hides-when-idle switched on -> demonstrates hop into the island and back 0.8 s later; any look change -> celebrate after 0.35 s settle.
4. **Card "Appearance"**
   * Row `paintpalette` **Style** (no accessory) + HStack of 2 `NotchMascotChoiceTile`s (inset 38): **Minimal**, **Robot** (preview = 30px mascot on a 64x42 black capsule). -> `notchMascotStyle` ("minimal"|"robot", default minimal).
   * If Minimal: row `circle.square` **Shape** (accessory: current shape name, secondary) + HStack (inset 38, spacing 8) of 4 shape swatches (58x40 black rounded rect radius 10 with a 24px mascot; selected = 2px accent ring radius 12, padding 2; tooltip/AX = name): **Ball**, **Egg**, **Rounded square**, **Pill** -> `notchMascotShape` (ball|egg|squircle|pill, default ball).
   * Row `swatchpalette` **Color** (accessory: current colour name) + HStack (inset 38, spacing 8) of 7 round swatches (22px circle with vertical gradient light->shade, 0.5px primary 15% stroke, padding 3, selected = 2px accent ring): **Pearl** (#FCFAF5 -> #D6D9E6), **Lemon** (#FFF5A3 -> #F7D44D), **Peach** (#FFDBBD -> #FAA380), **Rose** (#FFCCDE -> #F28AAD), **Lilac** (#E0D4FF -> #B096F5), **Sky** (#C4E6FF -> #6EABF7), **Mint** (#B8F7DB -> #66D4A8) -> `notchMascotPalette` (pearl default; swatch order pearl, lemon, peach, rose, lilac, sky, mint).
     (RGB: pearl 0.99/0.98/0.96 -> 0.84/0.85/0.90; lemon 1.00/0.96/0.64 -> 0.97/0.83/0.30; peach 1.00/0.86/0.74 -> 0.98/0.64/0.50; rose 1.00/0.80/0.87 -> 0.95/0.54/0.68; lilac 0.88/0.83/1.00 -> 0.69/0.59/0.96; sky 0.77/0.90/1.00 -> 0.43/0.67/0.97; mint 0.72/0.97/0.86 -> 0.40/0.83/0.66.)
5. **Card "Behavior"**
   * Only if the companion rests beside a camera (real notched display OR silhouette == notch): row `arrow.left.and.right` **Side of the camera** + 2 tiles (inset 38): **Left**, **Right** (preview = 92x20 island strip with 13px mascot in a 22px wing) -> `notchMascotSide` ("left"|"right", default left). On Windows with a capsule silhouette this row is hidden; with the "Notch" silhouette it is shown (the drawn cut-out has a centre gap).
   * Switch `eye.slash` **Hide when idle** caption "After half a minute with nothing going on, it hops into the island and the island goes back to its usual size. Visits and reactions still bring it out." -> `notchMascotHidesWhenIdle` (true).
   * Switch `figure.walk` **Appear now and then** caption "Every few minutes it passes through the island with a short animation." -> `notchMascotVisits` (true).
   * If visits on: choice row (inset 38) `clock` **How often** segmented **Rarely** (600-1200 s between visits) | **Sometimes** (240-540 s, default) | **Often** (90-180 s) -> `notchMascotVisitFrequency` (rare|normal|frequent).
   * Switch `theatermasks` **React to what happens** caption "It comes out to react to music, timers, downloads, screenshots, the microphone, Keep awake and more." -> `notchMascotReactions` (true).
6. **Card "Command Bar"** (title = Command Bar page title "Command Bar")
   * FeatureSwitch `command` **Command Bar in the island** caption "With the shortcut, the Command Bar comes out of the island with the companion as its face. It reacts while you search." -> `notchCommandBar` (true), feature commandBar.
   * If the feature is installed and the switch is on: row `drop` **Opens as** + 2 tiles (inset 38): **Drop** (a 46x9 island strip, a 5px drop, an 84x18 bar with 10px mascot + 38x3 field line) and **Open island** (92x34 island with the field inside) -> `notchCommandBarStyle` ("droplet"|"island", default droplet).
   * Animations 0.25 s smooth (none under Reduce Motion).

Reaction gate (runtime): at least 2.5 s between reactions, the same reaction not again within 30 s, an unshowable reaction waits at most 8 s. Reaction lengths (s): celebrate 1.0, love 1.4, surprised 0.9, flash 0.5, confused 1.2, wakeUp 1.6, yawn 1.2, perk 1.0, bounce 1.0, hush 1.0, ready 1.0, groove 1.6. Moments -> reaction: timerStarted ready, timeIsUp surprised, music groove, agents ready then celebrate, eventStarts bounce, downloadFinished celebrate, downloadFailed confused, screenshot flash, micMuted hush, keepAwake perk, charging love, lowBattery yawn, unlocked wakeUp.

### 6.2 Settings table
| Key | Type | Default | Allowed |
|---|---|---|---|
| `notchMascotEnabled` | Bool | false | |
| `notchMascotVisits` | Bool | true | |
| `notchMascotReactions` | Bool | true | |
| `notchMascotHidesWhenIdle` | Bool | true | |
| `notchMascotStyle` | String | "minimal" | minimal, robot |
| `notchMascotShape` | String | "ball" | ball, egg, squircle, pill (minimal only) |
| `notchMascotPalette` | String | "pearl" | pearl, lemon, peach, rose, lilac, sky, mint |
| `notchMascotSide` | String | "left" | left, right |
| `notchMascotVisitFrequency` | String | "normal" | rare, normal, frequent |
| `notchCommandBar` | Bool | true | |
| `notchCommandBarStyle` | String | "droplet" | droplet, island |
| `notchMascotBetaInstalled` | Bool | unregistered | local marker (auto-install once for beta users) |

Moods (runtime): idle, happy, wink, love, sleepy, determined, surprised, searching, thinking, confused, alert (Keep awake). Eyes: open, closed, happy, heart, sleepy, wide, squint, determined.

### 6.3 Windows mapping
Draw the mascot with WPF vector `Path`/`Ellipse` + `Storyboard`s (no bitmap needed). Pointer-follow eyes use `Mouse.GetPosition`. "Mac unlocked" -> `WTS_SESSION_UNLOCK`. Command Bar in the island needs a Command Bar feature (not in Trayloom yet): until then show the card with the feature switch disabled (SettingsFeatureSwitchRow behaviour). The stage mock menu bar -> draw a Windows 11 taskbar-top slice (just rectangles) or keep the menu bar placeholder for parity.

### 6.4 Gap vs Trayloom
Entire Companion feature absent (no feature, no tab, no settings).

---------------------------------------------------------------------------------------------------

## 7. Layout editor (NotchLayoutEditor) - quick-access buttons around the island

### 7.1 Model
* A **quick button** = `{ id: UUID, actionID: String, side: left|right|bottom, label: String }`. The island draws these as 44px round black buttons floating beside/below the open island (gutter 72, gap 12, row spacing 54, circle diameter 44; hover corridor margin 16; hover exit delay 0.18 s; withdrawal animation 0.16 s).
* **Max 3 buttons per side** (9 total; sanitizer also caps total at 64 raw entries and drops duplicates ids, unknown actions and excess per-side buttons). Label: newlines -> spaces, trimmed, max 40 chars; empty label = use the action's title.
* **Actions** (`actionID` strings): `explore` (title **Explore**, `square.grid.2x2`), `settings` (**Settings…**, `gearshape`), `pin` (**Keep open**, `pin`), a section raw value (`controls`, `mixer`, `music`, `clipboard`, `captures`, `files`, `system`, `tools`, `calendar`, `notifications`, `timer`, `camera`, `downloads`, `scratchpad`, `agents`, `watch`; title = section title, symbol = section symbol), or `control.<item>` for the non-level control items (`control.music`, `control.mixer`, `control.keepAwake`, `control.timer`, `control.calendar`, `control.microphone`, `control.screenshot`, `control.recording`, `control.speedTest`, `control.panel`, `control.commandBar`, `control.scratchpad`; titles as in 3.2: "Now Playing", "Volume mixer", "Keep awake", "Timer", "Calendar", "Mute microphone", "Screenshot", "Screen recording", "Speed test", "Open app panel", "Command Bar", "Scratchpad").
* An action is **available** when its section is shown (`NotchSupport.modules()`), or for controls when its feature is available (and for `speedTest`/`mixer` also the Mixer/System section is shown; `music`/`timer`/`calendar` need their section shown). `explore`, `settings`, `pin`, `control.panel` are always available. The island draws only available buttons but they keep their slot (and are shown dimmed at 40% in the editor) so they return when the section is switched back on.
* **Default layout** (`NotchQuickAccessConfiguration.initial`, used when nothing is stored): left = Explore, Timer; right = Settings…, Volume mixer; bottom = Now Playing. IDs `00000000-0000-4000-8000-00000000000{1..5}` respectively.

### 7.2 Persistence
`notchQuickAccessLayout` (Data/blob, <= 32 768 bytes): JSON with sorted keys: `{"buttons":[{"actionID":"explore","id":"00000000-0000-4000-8000-000000000001","label":"","side":"left"}, ...],"version":1}`. Decoded only if `version == 1`; always passed through `sanitized()`. The array order is the layout order within each side (buttons are filtered by side keeping array order). Empty/absent/invalid -> legacy keys (`notchQuickAccessSide`, `notchQuickAccessSecond`, `notchQuickAccessThird`; if any exists: side = stored side (default left), actions = [explore, second (default settings), third (if valid and not duplicate)] with IDs `...000000000001..3`), else the default layout. The first deliberate edit writes the new format. Writes happen on every change (no Apply).
Windows storage: store the same JSON as a string in the feature settings store; use `Guid` ids; keep key `notchQuickAccessLayout`.

### 7.3 Layout of the editor (VStack spacing 12)
* **Canvas**: height = 640 x 0.5 + 20 + 58 + 2 = **400** px, full width, background rounded rect radius 22 filled secondary 35%, named coordinate space "island.editor".
  * Island preview: the real Controls page laid out at its real size (`actualWidth` = expanded width of the current size; `actualHeight` = custom height, or the controls page's own height for presets) in a black island shape (notch or capsule per the display; `outlineEnabled` draws the 2px white 65% outline; a capsule's outline goes all the way round), then scaled by `scale = min(0.5, max(0.2, (canvasWidth - 112) / actualWidth))` and centred horizontally, top at y = 20. Its content = the user's current Controls items (levels in the top band row around the camera/pill, the music card, the shortcut rail) drawn non-interactively; empty -> empty view with message "Choose what appears in Dynamic Island settings.". The whole island preview is a button (tooltip/AX "Content") that jumps to the Content tab with Controls selected.
  * Buttons (bubbles): 30px black circle, glyph 12pt medium white, 1.5px stroke (white 20%, accent while its popover is open), shadow (black 12%, radius 3, y 2), 40% opacity if the action is not available (tooltip adds ", Hidden from the island"). Slot positions (canvas coordinates, `island` = scaled island rect): left x = island.minX - 18, y = island.minY + 30 + index*40; right x = island.maxX + 18 (same y); bottom x = island.midX + (index - (n-1)/2)*40, y = island.maxY + 24.
  * "+" add buttons: one per side that `hasRoom` (< 3 buttons), at the next free slot: 30px circle, `plus` 15pt semibold accent, fill = window background, 1px dashed (3,3) accent 65% stroke; tooltip "Add button", AX "Add button, <Left|Right|Bottom>". Opens the add popover (arrow edge: left side -> trailing, right -> leading, bottom -> top).
  * **Resize grip**: 24px circle (window background fill, 1px accent 65% stroke, shadow black 12%/3/y2) with `arrow.up.left.and.arrow.down.right` 10pt semibold accent, positioned at (island.maxX - 3, max(island.maxY - 3, island.minY + 139)) (a short island slides it below the third right slot). Dragging it (min distance 3): on first move capture start size, set `notchSize = custom`, then `width = clamp(startW + dx/scale, 360...600)`, `height = clamp(startH + dy/scale, 260...640)` (non-finite -> defaults 440/480). AX label "Size".
  * While dragging a bubble, three **drop zones** appear: left = rect(x: island.minX-48, y: island.minY, w 62, h island.height), right = rect(x: island.maxX-14, same y, w 62, h island.height), bottom = rect(x: island.minX, y: island.maxY, w island.width, h 58); each rounded rect radius 16 filled accent 4% (15% while the drag point is inside), 1px dashed (4,4) accent 30% stroke; not hit-testable. Changes animate 0.2 s smooth (none with Reduce Motion).
* **Hint** under the canvas (callout secondary): "Click + to add a button. Drag buttons around the island. Click a button to edit it."

### 7.4 Interactions
* **Drag a bubble** (minimum distance 5; high-priority so it wins over the island button): follows the pointer (offset by translation, z-order on top); on release, find the first drop zone containing the pointer (left, right, bottom order); if none -> nothing changes (snaps back). Otherwise: siblings = buttons already on that side (excluding itself, in order); the insertion target = the first sibling whose slot position is *beyond* the pointer (side zones: slot.y > pointer.y; bottom: slot.x > pointer.x); `configuration.move(id, to: side, before: target)`: rejected unless it is already on that side or the side has room; the item is removed and inserted before the target in the master array (or appended if no target). Starting a drag closes any open edit popover.
* **Click a bubble**: opens the edit popover (width 340, padding 16, VStack spacing 14):
  1. Heading **Edit button** (.headline).
  2. Text field, label **Name**, prompt = the action's default title; live-writes `label`.
  3. Segmented picker **Position**: **Left | Right | Bottom** (`quickAccessLeft` "Left", `quickAccessRight` "Right", `bottom` "Bottom"); a side with no room is disabled (unless it's the button's own side). Choosing a different side calls `move(id, to: side)` (appends to that side); same side = no-op.
  4. Small buttons **Move up** / **Move down** (`reorder(id, by: -1/+1)`: swaps with the neighbour on the same side; saved only if it actually moved).
  5. The action chooser (below) with the current action highlighted (accent 14% fill); picking one replaces `actionID`.
  6. **Remove button** (destructive) deletes the button and closes the popover.
* **Click "+"**: popover (width 340, padding 16): heading **Add button**, the action chooser; picking an action appends `NotchQuickButton(action, side)` (if the side still has room) and closes.
* **Action chooser** (`NotchActionChooser`): search field (rounded border, placeholder **Find an action**, autofocus in the add popover) filtering on normalised (case/diacritic-insensitive) `title + " " + actionID` containing the normalised query; a 200px-high scroll area (padding 2) with two groups, each with a caption heading and a 2-column grid (spacing 6) of buttons (label = SF symbol + title, 11pt, max 2 lines, min height 38, padding H8, radius 9, fill secondary 7%, selected accent 14%; unavailable actions are filtered out of the list entirely): **Open a section** = [Explore, Settings…] + every section; **Quick actions** = [Keep open] + the 12 non-level controls. If nothing matches: centred secondary text "No results" (min height 120).

### 7.5 Windows mapping
* Preview canvas: WPF `Canvas` 400 DIP high with the real `IslandWindow` Controls section inside a `Viewbox`-like `ScaleTransform` (scale as above). Bubbles = `Button` with `Border` `CornerRadius=15`; drag = `MouseLeftButtonDown` + `CaptureMouse` + 5 DIP threshold, translate via `TranslateTransform`, hit-test against the three rects on `MouseLeftButtonUp`.
* Popovers = `Popup` (`PlacementMode.Right/Left/Bottom`, `StaysOpen=false`) with `AllowsTransparency` and a themed card.
* Search normalisation = `string.Normalize(FormD)` strip combining marks + `ToLowerInvariant`.
* Runtime quick-access bubbles in the real island = separate layered click-through windows or part of the island window's transparent margin (the other agent specifies the runtime).

### 7.6 Gap vs Trayloom
No quick-access button system at all. Trayloom's expanded panel has a header with section tabs (`IslandSection` list) and no side/bottom bubbles; no editor.

---------------------------------------------------------------------------------------------------

## 8. Drag-and-drop reordering model (Content tab lists, shortcut grid, AI cards)

Shared by `PanelReorderableItem`/`PanelItemDropDelegate`:
* Items are `String` raw values in a `[Item]` array bound to a comma-separated setting. Dragging a row/card sets `dragging = item` (and an `NSItemProvider` with the raw value as plain text); the dragged item renders at 45% opacity (Content rows) while dragging.
* While a dragged item enters another item's frame (`dropEntered`), the array is **live-reordered**: `order.move(fromOffsets: [from], toOffset: to > from ? to+1 : to)`. No insertion marker; neighbours slide. Drop (`performDrop`) just clears `dragging` and accepts; drag operation = move.
* Reorder writes immediately: `notchModuleOrder` (sections list), `notchControlOrder` (Controls shortcut grid; only the non-level, non-music items are shown/reordered, the stored string may also hold the rest), `notchAgentsCardOrder` (AI cards). Stored order is read as `stored.compactMap(known) + allCases`, de-duplicated, so unknown ids are dropped and new items appended.
* Keyboard/AX alternative: "Move up"/"Move down" actions (swap with neighbour).
* Show/hide never moves an item: it toggles membership in the hidden CSV (`notchHiddenModules`, `notchHiddenControls`, `notchAgentsHiddenCards`; written sorted).
* Dragging cancelled by switching tab: `draggingModule`/`draggingControl` reset.
Windows: implement with a small attached behavior (`ReorderBehavior`) using `DragDrop.DoDragDrop(DataObject(string))` + `DragEnter` handler that swaps indices in an `ObservableCollection`, or pointer-capture dragging with a ghost; use `ItemsControl` + `WrapPanel` for the adaptive grids (min item width 116/104 -> compute columns = floor((width+gap)/(min+gap))).

---------------------------------------------------------------------------------------------------

## 9. Calendar selection (NotchCalendarSelection) and calendar permission

* Shown inside the Calendar module's options card only when calendar access == full. VStack(spacing 6): Divider; **Calendars shown** (.subheadline medium); for each account group (accounts sorted by localized-standard compare of name, tie by source id; calendars within sorted likewise) a caption-secondary account name (padding-top 4; skipped if empty) followed by one checkbox row per calendar: checkbox + 8px colour dot (calendar colour; fallback RGB 0.35/0.65/1.0) + title (1 line). AX label = title.
* Checked = shown. Unchecking adds the calendar's identifier to `notchCalendarExcluded` (String array, sorted, default `[]`); checking removes it; then `NotchCalendarService.syncWithPreferences()`. "Excluded calendars stay out of the Calendar section and the event countdown." The EventKit query passes only the non-excluded calendars (nil = read all, including calendars added later; an empty allowed set means read nothing and must not be passed as []).
* Reloads on appear and whenever the calendar store changes (`EKEventStoreChanged`).
* Related keys: `notchCalendarEnabled` (true), `notchCalendarCountdown` (false), `notchCalendarCountdownLead` (60, 5...240), `notchCalendarTimeLeft` (false), `notchCalendarWeekNumbers` (false), `notchCalendarChosenCountdowns` ([countdownKey: end date]; unregistered, local only; keys are `identifier` or `identifier@<occurrenceReferenceSeconds>`; each forgotten when its event ends).
* Countdown rules: start countdown shows within `lead` minutes before a timed event begins; time-left shows during the final hour (60 min) of an event in progress and only once begun; the island week view reads today..+7 days (month grid reads 42 days, extended to cover the 7-day window for the current month).
* Windows mapping: `AppointmentManager.RequestStoreAsync(AppointmentStoreAccessType.AllCalendarsReadOnly)` -> `store.FindAppointmentCalendarsAsync(FindAppointmentCalendarsOptions.IncludeHidden)` gives `AppointmentCalendar { LocalId, DisplayName, DisplayColor, UserDataAccountId }`; group by account: `UserDataAccountManager.RequestStoreAsync(UserDataAccountStoreAccessType.AllAccountsReadOnly)` -> `GetAccountAsync(UserDataAccountId).UserDisplayName` (fallback "Other"/"Local"); exclusions stored by `LocalId`; events via `FindAppointmentsAsync(rangeStart, span, new FindAppointmentsOptions{ CalendarIds=[allowed...] })` (do not pass an empty list); countdown key = `LocalId` (+ `@<ticks of OriginalStartTime>` for recurring). Refresh: no EKEventStoreChanged equivalent -> subscribe to `AppointmentStore.StoreChanged` (Windows 10 1703+ on `AppointmentStoreChangeTracker`) or poll every 60 s while the settings page/island is visible.
* Permission block on Windows: the Windows Calendar privacy gate is per-user "Let desktop apps access your calendar" under Settings > Privacy & security > Calendar (`ms-settings:privacy-calendar`). Button **Allow Calendar Access** triggers `RequestStoreAsync` (disabled while the request is running; on failure show "Could not request calendar access. Please try again."); **Open System Settings** -> `Launcher.LaunchUriAsync("ms-settings:privacy-calendar")` with the label "Open Windows Settings" (documented deviation); when denied show "Allow calendar access in System Settings to see your appointments." -> "Allow calendar access in Windows Settings to see your appointments.".
* Existing Trayloom: `island-calendar` feature already reads appointments with `AppointmentStoreAccessType.AllCalendarsReadOnly` (next 60 days) and offers only `weekStart` (auto/monday/sunday) and `showEvents`; it has no calendar selection, countdown, time-left, week-number setting (it computes week numbers internally), or permission UI.

---------------------------------------------------------------------------------------------------

## 10. AI Agents module options (NotchAgentsSettingsControls + ClaudeAccountSettings + CodexAccountsSettings)

Shown as the options of the **AI Agents** section on the Content tab (card body, VStack spacing 14, all switches in `.switch` style). The section itself is toggled by `notchAgentsEnabled` (default **false**; opt-in) via the section checkbox.

### 10.1 Layout top to bottom
1. Callout secondary: "Reads the usage Claude Code, Codex, OpenCode and GitHub Copilot record on this Mac and the plan limits the Claude app saves. Prompts, replies and files are never kept, and your usage never leaves your Mac." (Windows: "...on this PC...").
2. **Provider rows** x4 (HStack spacing 12): 26x26 tile (radius 7, provider tint 14%) with the provider mark (13pt) - VStack(spacing 2): name (**Claude**, **Codex**, **OpenCode**, **GitHub Copilot**) + caption "Found on this Mac" / "Not found on this Mac" (found = its log folder exists; computed when the page opens, not per draw) - switch. Keys `notchAgentsClaude`, `notchAgentsCodex`, `notchAgentsOpenCode`, `notchAgentsCopilot` (all default true). The last enabled provider's switch is disabled (one agent stays on; turning the whole section off stops all).
   Provider tints: Claude (0.85,0.47,0.34) #D97857, Codex (0.49,0.60,1.0) #7D99FF, OpenCode (0.06,0.73,0.51) #10BA82, GitHub Copilot (0.30,0.78,0.68) #4DC7AD. Symbols: Claude `sparkle`, Codex `chevron.left.forwardslash.chevron.right`, OpenCode `terminal`, Copilot `infinity` (each also has a custom mark).
3. If Claude on: **Claude account** block (10.3). If Codex on: **Codex accounts** block (10.4).
4. Divider; **Cards** (.subheadline medium); adaptive grid (min 104, spacing 8) of 8 draggable toggle cards (3.3 style, no reasons): **Limits** (gauge.with.dots.needle.33percent), **Spending** (dollarsign.circle), **Now** (waveform.path.ecg), **Trend** (chart.bar.xaxis), **Models** (cpu), **Projects** (folder), **Activity** (square.grid.3x3.fill), **Resets** (arrow.counterclockwise.circle) in `notchAgentsCardOrder` (default order = that list); hidden CSV `notchAgentsHiddenCards` (default "", i.e. all shown). Charts (Trend, Activity) take the full island width; Spending/Trend/Models/Projects follow the island's period chooser (`notchAgentsPeriod`: today|week|month; labels "Today", "7 days", "30 days"; default today). Caption: "Drag to reorder. Charts take the full width of the island." If Codex on and the Resets card shown: caption "A reset renews the Codex session and weekly limits at once. Codex checks your resets with its own sign-in, which Trayloom never reads."
5. Choice row (`gauge...33percent`) **Show limits as** segmented **Left** | **Used** -> `notchAgentsLimitDisplay` ("remaining" default | "used"). Menu row (`rectangle.topthird.inset.filled`) **Limit to show** options **Most used** (default), **Session**, **Week** -> `notchAgentsLimitFocus` (mostUsed|session|weekly); "the resting island shows this allowance whatever the live reading, and the live reading uses it when it shows a limit".
6. Divider; **While an agent works** (.subheadline medium); switch `waveform.path.ecg` **Show it in the closed Dynamic Island** -> `notchAgentsLiveActivity` (true). If on: menu row (inset 38, `camera.metering.center.weighted`) **Beside the camera** options **Time** (elapsed, default), **Tokens written** (tokens), **API value** (cost; capitalised first letter of "API value"), **Limit** (limit) -> `notchAgentsReadout`; then a **strip sample** (inset 38): 180x24 black attached shape (radius 9): left provider glyph 11pt (provider tint), 64px camera gap, right the reading (12pt medium monospaced digits, provider tint): elapsed "12:34", tokens "20K", cost "$4.56", limit "68%" (left) / "32%" (used) for the sample session (example when idle: started 754 s ago, 20 000 output tokens, $4.56, session 32% used; live data replaces it while a turn runs). Provider shown = the working one, else the first enabled in order Claude, Codex, OpenCode, Copilot.
7. Divider; **Alerts** (.subheadline medium):
   * Switch `checkmark.circle` **When a task finishes** -> `notchAgentsFinishAlert` (true); if on, menu row (inset 38, `timer`) **For tasks longer than** options **Any length** (0), 30 s, 1 min, 2 min, 5 min (short duration style: "30 sec", "1 min", "2 min", "5 min") -> `notchAgentsFinishMinimum` (Double seconds, default 60, stored clamp 0...3600).
   * Switch `exclamationmark.triangle` **Near a plan limit** -> `notchAgentsLimitAlert` (true); if on, menu row (inset 38, `gauge.with.dots.needle.67percent`) **Warn at** options "50% used", "75% used", "80% used", "90% used", "95% used" -> `notchAgentsLimitThreshold` (Double, default 80; clamp 1...100).
   * Menu row (`dollarsign.circle`) **Daily API value budget** options **Off** (0), $5.00, $10.00, $25.00, $50.00, $100, $250 -> `notchAgentsDailyBudget` (Double, default 0 = off).
   * Caption: "API value is what the same work would cost at API list prices. Plans charge a fixed price instead."
   * Switch row (`arrow.triangle.2.circlepath`) **Keep prices up to date** caption "Downloads the public price list from GitHub once a day, so new models get an API value without an app update. Nothing about your usage is sent." (+ " Price list of <date>" when a list has been downloaded) -> `notchAgentsPriceUpdates` (true).
8. If Claude on: Divider; **Claude plan limits** (.subheadline medium); a status row refreshed every 30 s (`TimelineView periodic 30`) - HStack(top, spacing 10), callout:
   * *Account reading present* (source = account): icon `checkmark.circle.fill` green if observed < 30 min ago, else `exclamationmark.circle.fill` orange; text "Claude Code · Updated <relative time>" (relative = named relative, e.g. "5 minutes ago"); under it, if stale and Claude Code's last check failed, the failure text in secondary.
   * else if a non-default Claude profile is selected: `info.circle.fill` + failure text or "Claude Code has not returned limits for this account yet."
   * else if the Claude app wrote a reading < 30 min ago: green check + "Read from the Claude app, updated <when>" (+ failure line).
   * else if the Claude app is not installed and nothing checked: info icon + "Plan limits come from Claude Code or from the Claude app, which is not on this Mac. Until then, the 5-hour session is estimated from Claude Code activity." + button **Get the Claude app** (opens https://claude.ai/download).
   * else (installed but stale): orange icon + (if known) "The Claude app last checked them <when>." + "Claude checks your limits only while its icon is in the menu bar. Turn the icon on in the Claude app settings, and your limits appear here within minutes." + button **Open Claude**.
   * A small spinner while a Claude account check is running. Under it, if the default profile is selected: caption "Claude Code checks your account’s limits, including use on other machines, with its own sign-in, which Trayloom never reads. No prompt is sent. When it cannot check, the percentages the Claude app saves on this Mac are used."
   * Failure texts: missing "Claude Code was not found on this Mac."; signed out "Sign in to Claude Code so it can check your account’s limits."; outdated "Update Claude Code so it can check your account’s limits."; unreachable "Claude Code could not check your account’s limits. It tries again later."
9. Any change of the keys in the page's `onChange` list (card order/hidden, provider switches, live activity, readout, limit display/focus) calls `NotchService.syncWithPreferences()` (page height and closed-island width follow).

### 10.2 Settings table (agents)
| Key | Type | Default | Allowed | Effect |
|---|---|---|---|---|
| `notchAgentsEnabled` | Bool | **false** | | Section shown (migration: existing configured profiles got true once via `notchAgentsOptInMigrated`). |
| `notchAgentsClaude` / `Codex` / `OpenCode` / `Copilot` | Bool | true | at least one stays on | Which logs are read. |
| `notchAgentsIncludeClaudeCode` | Bool | **false** | | "Include Claude Code in terminals and editors" (shown when Claude is on). Off: "Claude" means the Claude desktop app only (its plan usage and weekly limit, and the work done in its Code tab and Cowork); Claude Code transcripts, sessions, the live limits check and the Claude account group (10.3) are left out. On: today's behaviour. Changing it re-reads the logs. |
| `notchAgentsClaudeProfile` | String path | "" | "" = default account, else an absolute Claude config dir | Selected Claude profile (used only while `notchAgentsIncludeClaudeCode` is on). |
| `notchAgentsCodexProfiles` | String (JSON) | "" | up to 8 profiles | Added Codex homes (10.4). |
| `notchAgentsCardOrder` / `HiddenCards` | String CSV | "" / "" | card ids limits,spend,live,trend,models,projects,activity,resets | Cards order/visibility. |
| `notchAgentsPeriod` | String | "today" | today, week, month | Period for period cards (set from the island). |
| `notchAgentsLimitDisplay` | String | "remaining" | remaining, used | |
| `notchAgentsLimitFocus` | String | "mostUsed" | mostUsed, session, weekly | |
| `notchAgentsLiveActivity` | Bool | true | | Live reading in the closed island. |
| `notchAgentsReadout` | String | "elapsed" | elapsed, tokens, cost, limit | |
| `notchAgentsFinishAlert` / `FinishMinimum` | Bool / Double | true / 60 | 0,30,60,120,300 | Task-finished notice threshold (idle turn: silent 10 min = not working). |
| `notchAgentsLimitAlert` / `LimitThreshold` | Bool / Double | true / 80 | 50,75,80,90,95 | |
| `notchAgentsDailyBudget` | Double | 0 | 0,5,10,25,50,100,250 | |
| `notchAgentsPriceUpdates` | Bool | true | | Daily public price list download from GitHub. |

### 10.3 Claude account (ClaudeAccountSettings) - shown only when Claude is on
VStack spacing 8:
* Menu row (`person.crop.circle`) **Claude account**: options = default **Default account** (value "") + discovered profiles + the currently selected profile (kept while the disk read is pending). Option label = `<name>` or `<name> · <email>` where name = "Default account" for default, else the folder name with a `.claude-` prefix removed (max 80 chars). Discovery (off the main thread): home folders named `.claude` or `.claude-*` (at most 32, sorted by path) plus `~/.config/claude`, filtered to *available* ones; re-run on appear, when the selection changes, when `claudeAccountIdentity` changes, and whenever the app becomes active.
* For the selected non-default entry: orange caption **invalidFolder** when unavailable; else, if no identity found, caption "No account identified in this profile."; and the path (caption2 secondary, middle-truncated, tooltip full path).
* Button **Choose account folder…** (folder picker, can show hidden, starts in home). If the chosen folder is `~/.claude` and has no `.claude.json` of its own -> selects default (""); else if available -> selects it; else shows an alert titled "Claude account" with message = invalidFolder and an **OK** button.
* Caption: "Choose a profile already signed in to Claude Code. Usage comes from that profile’s sessions, and Claude Code checks its account’s limits. Changing this selection does not change the account in your other apps."
* Link (caption) **How to sign in to another Claude account** -> https://code.claude.com/docs/en/authentication#log-in-with-multiple-accounts
* invalidFolder text: "Choose an existing Claude Code profile folder that belongs to you and contains its own .claude.json file. The folder must not be writable by others, and the file must not be a link."
* Availability rules (profile directory): default is always available; else must be an absolute path (<= 4096 bytes), not `/` and not the home folder, a directory owned by the current user and not group/other-writable, containing its own `.claude.json` that is a regular file owned by the user with link count 1. Only metadata is read: `oauthAccount` {accountUuid, emailAddress, organizationUuid, organizationType, rateLimitTier...} and `cachedUsageUtilization.fetchedAtMs`; at most 4 MiB; nothing but email/ids kept in memory, never written to the usage archive. Log folders for the default profile: `~/.claude/projects` and `~/.config/claude/projects`; for a custom profile `<dir>/projects`.

### 10.4 Codex accounts (CodexAccountsSettings) - shown only when Codex is on
VStack spacing 10:
* **Codex accounts** (.subheadline medium); caption "Each CODEX_HOME folder you add gets its own limits card. While the AI Agents page is open, Codex checks each one about every five minutes with that folder’s own sign-in. No prompt is sent."
* One row per saved profile (max 8): text field (rounded border, placeholder **Account name**, AX "Codex · Account name: <name>") that writes the name on each valid edit (empty/whitespace-only edits are ignored, max 40 chars) + caption2 path (middle-truncated, tooltip) ; trailing `minus.circle` borderless button (tooltip/AX **Remove this account’s card**).
* Button row: **Find local accounts** (adds every valid `~/.codex` and `~/.codex-<name>` home not already present, canonicalised; the usual `~/.codex` is named **Default**, `.codex-work` -> **Work**), **Add folder…** (picker starting in home, hidden files visible; the chosen folder gets a name from its last component; invalid -> alert titled "Choose a Codex folder" with message invalidFolder and **OK**), and, when at least one profile exists, **Check limits** (`service.refresh(force: true)`; disabled when the Limits card is hidden (checks stop) or all checks are running). Find/Add disabled at 8 profiles.
* Footnote: with no profiles "With no accounts added, Codex limits come from its local sessions."; else "The Resets card still belongs to the default Codex account."
* invalidFolder: "Choose a separate CODEX_HOME folder with config.toml or auth.json in it. The folder must be yours and not writable by others, and auth.json must be private and not a link. Codex may refresh its own sign-in and caches there."
* Card states elsewhere: folderProblem "Choose this account’s folder again in Settings."; defaultName "Default"; defaultAccount "Default account"; refreshHint "Refresh to check this account’s limits."; signIn "Sign in with ChatGPT in this Codex folder."; update "Update Codex to check limits."; failed "Could not check limits. Try again."
* Persistence: `notchAgentsCodexProfiles` = JSON array `[{"id":"<UUID>","name":"Work","directory":"/abs/path"}]` (<= 64 KiB; decode keeps at most 8 entries, valid UUID ids, non-empty trimmed names <= 40 chars, absolute directories, unique ids and unique symlink-resolved paths).
* Validity of a Codex home (`canonicalDirectory`): resolve symlinks; not `/` and not the home dir; directory owned by the user and not group/other-writable; if `auth.json` exists it must be a regular file owned by the user, mode without group/other bits (private) and link count 1; if it doesn't exist `config.toml` must be a regular file. Refresh cadence: normal 5 min, 1 min after a window renews, retry 5 min after unavailable/cancelled, 30 min after invalidProfile/missingCLI/needsSignIn/outdated. The check launches Codex's own server/CLI with `CODEX_HOME=<dir>` and a minimal allow-listed environment (HOME, USER, LOGNAME, TMPDIR, proxy variables, SSL_CERT_*, NODE_EXTRA_CA_CERTS, DO_NOT_TRACK, DISABLE_TELEMETRY); errors: missing CLI ("Needs the Codex app or CLI"), needs sign-in, outdated, unavailable, cancelled.

### 10.5 Log locations (what "Found on this Mac" tests; Windows equivalents in 13.7)
macOS: Claude `~/.claude/projects` and `~/.config/claude/projects` (or `<profile>/projects`); Codex `~/.codex/sessions` and `~/.codex/archived_sessions`; OpenCode `~/.local/share/opencode` (SQLite database file inside); GitHub Copilot `~/.copilot/session-state/<id>/events.jsonl`. Claude app plan history `~/Library/Application Support/Claude/plan-usage-history.json` (version 1 or 2, <= 4 MiB; keys `fh` 5-hour session, `sd` weekly, `so` Opus, `sn` Sonnet; fresh within 30 min); bundle id `com.anthropic.claudefordesktop`; download https://claude.ai/download.

### 10.6 Windows mapping
See 13.7. UI: identical layout. The folder pickers use `FolderBrowserDialog`/`IFileOpenDialog (FOS_PICKFOLDERS)` starting in `%USERPROFILE%`. "Open Claude" = launch the Claude desktop app if found (App Paths / Start menu AUMID / `%LOCALAPPDATA%\AnthropicClaude\claude.exe`), "Get the Claude app" -> https://claude.ai/download. Rename "this Mac" -> "this PC" in the strings.

### 10.7 Gap vs Trayloom
No agents feature at all.

---------------------------------------------------------------------------------------------------

## 11. Strings

### 11.1 Strings borrowed from other string tables (used verbatim on these pages)
| Where | English |
|---|---|
| Global shortcut toggle (`quickToolShortcutToggle`) | "Global shortcut" |
| Shortcut row label (`shelfHotkeyLabel`) | "Shortcut" |
| Shortcut rejected (`shortcutUnavailable`) `[mac-only]` | "macOS rejected this shortcut. Choose another one." (Windows: "Windows rejected this shortcut. Choose another one.") |
| Permission states | "Granted" / "Not granted" / "Grant access" / "Open System Settings…" `[mac-only]` (Windows: "Open Windows Settings") ; names "Accessibility", "Screen Recording" |
| Microphone card title (`mixerInputTitle`) | "Microphone" |
| Mic mute feature (`micMuteName`) / HUD (`micMutedHUD`) | "Mute microphone" / "Mic Mute on" |
| Keep awake (`keepAwakeTitle`) | "Keep awake" |
| Volume mixer (`mixerSection`) | "Volume mixer" |
| Power (`powerSection`) | "Power" |
| Quick panel (`launcherName`) / Shelf (`shelfName`) / Network | "Quick panel" / "Shelf" / "Network" |
| Speed test (`speedTestRun`) | "Speed test" |
| Settings menu item (`menuSettings`) | "Settings…" |
| Clipboard feature | title "Clipboard"; enable "Save clipboard history"; moveUp "Move up"; moveDown "Move down"; noResults "No results" |
| Brightness feature | pageTitle "Displays"; enable "Control displays"; keyboardLight "Keyboard light" |
| Scratchpad | pageTitle "Scratchpad"; placeholder "Type anything. It saves by itself." |
| Screenshot | pageTitle "Screenshot"; defaultActionLabel "Default action"; none "Ask each time"; "Save"; "Save & Copy"; "Copy"; "Edit" |
| Recent captures | title "Recent captures"; screenshot "Screenshot"; recording "Recording" |
| Screen recording | pageTitle "Screen recording" |
| Camera preview | pageTitle "Camera preview" |
| Command Bar | pageTitle "Command Bar" |
| Media (radial menu) | mediaNowPlaying "Now Playing"; mediaNothingPlaying "Nothing playing" |
| `enableFeature` format | `Enable “%@” in Features.` ; `enableSetting`: `Enable “%@” in its settings.` ; `showPage`: `Show “%@” on the Content tab.` |
| Misc | "OK" (alert button), "Claude account" (alert title), "Choose a Codex folder" (alert title), "Claude Code · " prefix on the status line |

### 11.2 Complete English tables (verbatim, "Vorssaint" -> "Trayloom")
Format `field: "value"`; field names are the Swift struct members (keep them as resource keys). `%@`, `%d`, `%.2f`, `%1$@ %2$@` are format specifiers.

### NotchStrings.swift (`static let enUS`)

```
title: "Dynamic Island",
combineActivities: "Combine",
enable: "Enable Dynamic Island",
description: "Your music, controls and everyday tools, together at the top of your screen. Optional. Turn it off to keep using the separate panels.",
menuBarAccessHint: "Allow Accessibility so Dynamic Island can appear on this display without covering the menus.",
display: "Display",
automatic: "Automatic",
builtIn: "Built-in display",
mainDisplay: "Main display",
followPointer: "Follow pointer",
allDisplays: "All displays",
hover: "Open when the pointer rests on the Dynamic Island",
modules: "What appears",
events: "More options",
clipboardActivity: "Notify when something is copied",
captureActivity: "Show screenshot previews here",
privacy: "Copied content stays private until you open the clipboard. These options follow the original feature’s settings.",
open: "Open Dynamic Island",
collapse: "Collapse",
pin: "Keep open",
unpin: "Allow automatic closing",
panel: "Open app panel",
dropHint: "Drop files here",
empty: "Choose what appears in Dynamic Island settings.",
volume: "Volume",
inputSource: "Input source",
brightness: "Brightness",
battery: "Battery",
onBattery: "On battery",
charging: "Charging",
charged: "Fully charged",
lowBattery: "Low battery",
controls: "Controls",
system: "System",
disabled: "Enable the corresponding feature to use it here.",
files: "Files",
hideInFullscreen: "Hide content in full screen",
hoverInFullscreen: "Hidden until hover in full screen",
showInCaptures: "Show in screenshots and videos",
clipboardWindow: "Open clipboard history in the Dynamic Island",
tools: "Tools",
size: "Size",
showOutline: "Show outline",
compact: "Compact",
spacious: "Spacious",
hoverExpand: "Expand fully on hover",
shelfWindow: "Open the file shelf in the Dynamic Island",
dragReveal: "Show a drop target while dragging",
captureControls: "Show screen capture controls in the Dynamic Island",
quickPanel: "Open the quick panel in the Dynamic Island",
appPanel: "Open the app panel in the Dynamic Island",
idleContent: "While inactive",
idleNone: "Nothing",
coverMenus: "Show over the menus",
coverMenusHint: "Keeps the timer, music and other compact activity on screen when the menu bar has no room, covering the menus beside the camera.",
controlShortcuts: "Controls and shortcuts",
activity: "Activity",
playingMusic: "Show music while playing",
custom: "Custom",
width: "Width",
maximumHeight: "Maximum height",
sizeHint: "Controls stay compact. Longer lists use up to this height.",
cameraFit: "Notch fit",
height: "Height",
cameraFitHint: "If an edge of the notch shows around Dynamic Island, adjust these values until it disappears.",
hideMenuBarGap: "Hide gap below notch",
hideMenuBarGapHint: "Extends Dynamic Island to the bottom of the menu bar to hide the thin strip below the notch.",
withoutNotch: "Displays without a notch",
capsuleShape: "Capsule",
notchShape: "Notch",
withoutNotchUnreached: "With the Display chosen on the Behavior tab, Dynamic Island doesn’t appear on these displays.",
showOnAllDisplays: "Show on all displays",
capsuleFit: "Capsule fit",
fromTop: "Distance from top",
capsuleFitHint: "Resizes the capsule and lowers it from the top of the screen on displays without a notch.",
hapticFeedback: "Haptic feedback",
hapticHint: "Gentle taps when opening, switching sections or adjusting timer minutes, on a compatible trackpad.",
playbackPosition: "Playback position",
musicHint: "Your music and playback controls appear here.",
music: "Music",
captures: "Captures",
newTrack: "New track",
customizeTools: "Customize tools",
switchSection: "Switch section",
sectionsTitle: "Explore",
searchSections: "Find a section",
sectionKeyboardHint: "Use the arrow keys, then press Return",
quickAccessLeft: "Left",
quickAccessRight: "Right",
translucentBackground: "Translucent background",
translucentBackgroundHint: "When open, Dynamic Island blurs what is behind it with the system’s own material.",
translucentBackgroundGlassHint: "Liquid Glass is on, so Dynamic Island uses it instead."
```

### NotchEditorStrings.swift (`static let enUS`)

```
layout: "Layout",
content: "Content",
activity: "Activity",
behavior: "Behavior",
layoutHint: "Click + to add a button. Drag buttons around the island. Click a button to edit it.",
addButton: "Add button",
editButton: "Edit button",
removeButton: "Remove button",
buttonName: "Name",
position: "Position",
bottom: "Bottom",
sectionActions: "Open a section",
quickActions: "Quick actions",
findAction: "Find an action",
reorderHint: "Drag to reorder. Click the checkmark to show or hide.",
opening: "Opening",
clickOpen: "Click to open",
hoverPreview: "Preview on hover",
hoverExpand: "Expand on hover",
hiddenUntilHover: "Hidden until hover",
hiddenShowsActivity: "Show activity and indicators",
hiddenShowsActivityHint: "Comes into view while an activity runs or an indicator shows, then hides again.",
resting: "At rest",
destinations: "Where things open",
separate: "Separate window",
feedback: "Indicators",
privacy: "Privacy",
reopening: "When reopening",
lastPage: "Last page",
openActivity: "Open the visible activity",
openActivityHint: "When the closed island shows music, a timer or another activity, it opens on that page instead.",
activationTime: "Activation time",
activationTimeHint: "Keep the pointer over the island for this long to open it.",
activationTimeFormat: "%.2f s",
closeTime: "Closing time",
closeTimeHint: "Wait this long after the pointer leaves the island before closing it.",
enableFeatureFormat: "Enable “%@” in Features.",
enableSettingFormat: "Enable “%@” in its settings.",
showPageFormat: "Show “%@” on the Content tab.",
keyboardLightUnavailable: "Keyboard backlight control is unavailable on this Mac.",
appPanelHint: "Choose where clicking the Trayloom menu bar icon opens the app panel, on any display.",
hideMenuBarIcon: "Hide the menu bar icon",
hideMenuBarIconHint: "Settings and the app panel open from Dynamic Island. The icon returns when the island is off or hidden in full screen, or when an update is ready or the microphone is muted.",
sections: "Sections",
sectionsHint: "Drag to reorder. Uncheck a section to hide it from the island.",
preview: "Preview",
hiddenInIsland: "Hidden from the island",
openFeatures: "Open Features",
cameraPreview: "The camera turns on only in the island.",
noOptions: "This section has no options.",
controlsSummary: "Playback, volume, brightness and your shortcuts.",
mixerSummary: "The volume of each app, and where sound plays.",
musicSummary: "The song playing now, with its controls.",
clipboardSummary: "What you copied recently, ready to paste again.",
capturesSummary: "Your latest screenshots and recordings.",
filesSummary: "A shelf for the files you drop on the island.",
systemSummary: "CPU, memory, disk, network and battery at a glance.",
toolsSummary: "Tools and utilities, one click away.",
calendarSummary: "Today’s events and the month ahead.",
notificationsSummary: "Your Mac’s notifications, kept in the island.",
timerSummary: "Timer, Pomodoro and stopwatch.",
cameraSummary: "A mirror to check yourself before a call.",
downloadsSummary: "Downloads in progress and just finished.",
scratchpadSummary: "Quick notes that save by themselves.",
agentsSummary: "Claude Code, Codex, OpenCode and GitHub Copilot usage, limits and costs.",
watchSummary: "Any part of a window, read live, with an alert when it changes."
```

### NotchActivityStrings.swift (`static let enUS`)

```
timer: "Timer",
timerDescription: "Timers, a stopwatch and focused work sessions in the Dynamic Island.",
pomodoro: "Pomodoro",
stopwatch: "Stopwatch",
focus: "Focus",
shortBreak: "Short break",
longBreak: "Long break",
pomodoroHint: "Start each phase when you are ready. The cycle ends after the last focus session.",
totalSessions: "Total sessions",
longBreakInterval: "Sessions between long breaks",
sessionProgress: "Session %d of %d",
pomodoroFinished: "Pomodoro complete",
minutes: "Minutes",
start: "Start",
resume: "Resume",
finished: "Time is up",
soundEnabled: "Play sound when time is up",
hideTimerCountdown: "Hide timer countdown",
camera: "Camera mirror",
cameraUnavailable: "The camera could not start. Try opening it again.",
cameraHint: "Open a live mirror here. The camera stops when you leave this view.",
startCamera: "Open camera",
stopCamera: "Stop camera",
accessories: "Accessory alerts",
accessoryDescription: "Show connected accessories and warn once when their battery falls to 20%.",
connected: "Connected",
lowBattery: "Low battery",
keepAwakeActivity: "Show “Keep awake” in the closed island",
keepAwakeActivityHint: "A running session appears beside the camera with the time it has left, or ∞ when it has no end."
```

### NotchAgentStrings.swift (`static let enUS`)

```
title: "AI Agents",
hubDescription: "Follow plan limits, tokens, API value and the work in progress of Claude, Codex, OpenCode and GitHub Copilot in the Dynamic Island.",
settingsDescription: "Reads the usage Claude Code, Codex, OpenCode and GitHub Copilot record on this Mac and the plan limits the Claude app saves. Prompts, replies and files are never kept, and your usage never leaves your Mac.",
restingTitle: "AI limits",
limitsCard: "Limits",
spendCard: "Spending",
liveCard: "Now",
trendCard: "Trend",
modelsCard: "Models",
projectsCard: "Projects",
activityCard: "Activity",
today: "Today",
week: "7 days",
month: "30 days",
session: "Session",
weekly: "Week",
estimated: "Estimated from this Mac",
claudeLimitsHint: "Set up plan limits…",
waitingForLimits: "Limits appear after the next reply",
noSession: "No session running",
apiValue: "API value",
tokensFormat: "%@ tokens",
cachedFormat: "%@ from cache",
writtenFormat: "%@ written",
savedFormat: "%@ saved by the cache",
planMultipleFormat: "%1$@ the price of %2$@",
idle: "Idle",
noActivity: "Nothing in this period",
empty: "No usage from Claude Code, Codex, OpenCode or GitHub Copilot yet. It appears here as soon as any of them works on this Mac.",
loading: "Reading usage…",
noCards: "Choose what this page shows in Dynamic Island settings.",
unpriced: "Some models have no known price, so this is a minimum.",
streak: "Days in a row",
activeDays: "Active days",
busiestDay: "Busiest day",
finishedFormat: "%@ finished",
limitRenewed: "Limit renewed",
budgetTitle: "Daily budget",
leftFormat: "%@ left",
usedFormat: "%@ used",
agents: "Agents",
found: "Found on this Mac",
notFound: "Not found on this Mac",
cardsTitle: "Cards",
cardsHint: "Drag to reorder. Charts take the full width of the island.",
limitsAs: "Show limits as",
remaining: "Left",
used: "Used",
limitFocus: "Limit to show",
mostUsed: "Most used",
liveTitle: "While an agent works",
liveActivity: "Show it in the closed Dynamic Island",
readout: "Beside the camera",
readoutElapsed: "Time",
readoutTokens: "Tokens written",
readoutLimit: "Limit",
alerts: "Alerts",
finishAlert: "When a task finishes",
finishAfter: "For tasks longer than",
anyLength: "Any length",
limitAlert: "Near a plan limit",
limitAt: "Warn at",
budget: "Daily API value budget",
off: "Off",
updatedFormat: "Updated %@",
claudeLimitsTitle: "Claude plan limits",
claudeLimitsCurrentFormat: "Read from the Claude app, updated %@",
claudeLimitsStaleFormat: "The Claude app last checked them %@.",
claudeLimitsMenuBar: "Claude checks your limits only while its icon is in the menu bar. Turn the icon on in the Claude app settings, and your limits appear here within minutes.",
claudeLimitsNoApp: "Plan limits come from Claude Code or from the Claude app, which is not on this Mac. Until then, the 5-hour session is estimated from Claude Code activity.",
claudeLimitsPrivacy: "Claude Code checks your account’s limits, including use on other machines, with its own sign-in, which Trayloom never reads. No prompt is sent. When it cannot check, the percentages the Claude app saves on this Mac are used.",
claudeCodeMissing: "Claude Code was not found on this Mac.",
claudeCodeSignedOut: "Sign in to Claude Code so it can check your account’s limits.",
claudeCodeOutdated: "Update Claude Code so it can check your account’s limits.",
claudeCodeUnreachable: "Claude Code could not check your account’s limits. It tries again later.",
openClaude: "Open Claude",
getClaude: "Get the Claude app",
priceUpdates: "Keep prices up to date",
priceUpdatesHint: "Downloads the public price list from GitHub once a day, so new models get an API value without an app update. Nothing about your usage is sent.",
pricesFromFormat: "Price list of %@",
valueNote: "API value is what the same work would cost at API list prices. Plans charge a fixed price instead.",
resetsCard: "Resets",
useReset: "Use a reset",
resetConfirm: "Reset the session and weekly limits now?",
confirmReset: "Reset",
resetting: "Resetting…",
resetDone: "Limits reset",
resetNotNeeded: "Your usage doesn’t need a reset yet",
resetTaken: "That reset was already used",
resetFailed: "Couldn’t use the reset",
resetsNone: "No resets available",
resetsExpiryFormat: "Next expires %@",
resetsNeedCodex: "Needs the Codex app or CLI",
resetsSignIn: "Sign in to Codex with a plan to see resets",
resetsUpdate: "Update Codex to use resets here",
resetsCheckFailed: "Couldn’t check resets",
resetsHelp: "A reset renews the Codex session and weekly limits at once. Codex checks your resets with its own sign-in, which Trayloom never reads."
```

### NotchCalendarStrings.swift (`static let enUS`)

```
title: "Calendar",
description: "Browse the month and your upcoming appointments in the Dynamic Island.",
permission: "Read your calendars to show upcoming appointments. Events stay on this Mac.",
allow: "Allow Calendar Access",
denied: "Allow calendar access in System Settings to see your appointments.",
settings: "Open System Settings",
empty: "No upcoming appointments",
next: "Up next",
ongoing: "Happening now",
allDay: "All day",
untitled: "Untitled event",
openCalendar: "Open Calendar",
week: "Next 7 days",
today: "Today",
requestFailed: "Could not request calendar access. Please try again.",
previousMonth: "Previous month",
nextMonth: "Next month",
previousWeek: "Previous week",
nextWeek: "Next week",
month: "Month",
emptyDay: "No appointments on this day",
hasEvents: "Has appointments",
countdown: "Event countdown",
countdownHint: "Show every timed event in the closed island before it starts. To count down to only some, turn this off and right-click them on the island’s Calendar page. Titles may appear in screen captures.",
countdownLead: "Time before event",
addCountdown: "Add Countdown",
removeCountdown: "Remove Countdown",
timeLeft: "Time left in current event",
timeLeftHint: "Show a timed event in progress in the closed island during the hour before it ends. Its title may appear in screen captures.",
weekNumbers: "Week numbers",
weekNumber: "Week %d",
calendars: "Calendars shown"
```

### NotchClipboardSizeStrings.swift (`static let enUS`)

```
entrySize: "Entry size",
compact: "Compact",
comfortable: "Comfortable",
hint: "Compact entries show one or two lines and open up when the pointer rests on them."
```

### NotchFilesStrings.swift (`static let enUS`)

```
archive: "Create ZIP",
archiveHint: "Each selected item is saved as a separate ZIP. Originals stay unchanged.",
saved: "Saved",
downloadsTitle: "Downloads",
downloadsDescription: "See files arriving in a folder you choose, directly in the Dynamic Island.",
downloadsHint: "Choose the folder where your browser saves downloads. Only that folder is watched.",
chooseFolder: "Choose Folder…",
folderUnavailable: "This folder is unavailable. Choose it again to restore access.",
waiting: "No files in this folder",
inProgress: "Downloading",
totalUnknown: "Total size unavailable",
completed: "Download complete",
clearFolder: "Forget Folder",
optimizeMedia: "Optimize media",
optimizeDropHint: "Drag images or one video onto the island, then drop on Optimize media to choose how to save a copy.",
resumeMedia: "Return to media"
```

### NotchGestureStrings.swift (`static let enUS`)

```
title: "Dynamic Island Gestures",
description: "Open and close the Dynamic Island with scrolling, and swipe to change tracks.",
hint: "Scroll down to open. Scroll up over the top row to close. Swipe left or right over the music to change tracks. Lists keep their own scrolling."
```

### NotchLockScreenStrings.swift (`static let enUS`)

```
title: "Lock Screen",
show: "Show on the Lock Screen",
showHint: "Music and the Dynamic Island’s activities, like a timer, AI agents, downloads and your next event, appear above the password field. Anyone who can see your screen can read them.",
sounds: "Lock and unlock sounds",
soundsHint: "Play the macOS padlock sound when your Mac locks and unlocks.",
working: "Working"
```

### NotchLowBatteryStrings.swift (`static let enUS`)

```
title: "Turn red when low",
caption: "Shows the charge in red at or below this level while on battery.",
threshold: "Red level",
early: "Turn amber first",
earlyCaption: "Shows the charge in amber at or below a higher level, as an early warning.",
earlyThreshold: "Amber level",
menuBar: "Also in the menu bar",
```

### NotchMascotStrings.swift (`static let enUS`)

```
title: "Companion",
hint: "A little friend who lives in the Dynamic Island. It rests there when nothing else is showing, and comes out over music and activities to say hello and react to what happens.",
visits: "Appear now and then",
visitsHint: "Every few minutes it passes through the island with a short animation.",
style: "Style",
minimal: "Minimal",
robot: "Robot",
shape: "Shape",
ball: "Ball",
egg: "Egg",
squircle: "Rounded square",
pill: "Pill",
color: "Color",
pearl: "Pearl",
mint: "Mint",
peach: "Peach",
lilac: "Lilac",
lemon: "Lemon",
rose: "Rose",
commandBar: "Command Bar in the island",
commandBarHint: "With the shortcut, the Command Bar comes out of the island with the companion as its face. It reacts while you search.",
opensAs: "Opens as",
droplet: "Drop",
openIsland: "Open island",
preview: "Companion preview",
previewHint: "Click to see it react.",
side: "Side of the camera",
left: "Left",
right: "Right",
frequency: "How often",
rare: "Rarely",
normal: "Sometimes",
frequent: "Often",
sky: "Sky",
hubDescription: "A little friend in the Dynamic Island who says hello now and then and reacts to what happens on your Mac.",
appearance: "Appearance",
behavior: "Behavior",
reactions: "React to what happens",
reactionsHint: "It comes out to react to music, timers, downloads, screenshots, the microphone, Keep awake and more.",
hidesWhenIdle: "Hide when idle",
hidesWhenIdleHint: "After half a minute with nothing going on, it hops into the island and the island goes back to its usual size. Visits and reactions still bring it out.",
sayHi: "Say Hi",
sayHiHint: "Plays a visit in the Dynamic Island now.",
momentsTitle: "See how it reacts",
visitMoment: "A visit",
timerStarted: "Timer started",
downloadFailed: "Download failed",
unlocked: "Mac unlocked",
petTip: "Rest the pointer on it to pet it. Left alone, it dozes off until you come back.",
livesInIsland: "The companion lives in the Dynamic Island.",
keywords: "mascot pet buddy character"
```

### NotchMusicExtrasStrings.swift (`static let enUS`)

```
lyrics: "Lyrics",
lyricsDescription: "Follow the lyrics of the current song in the Dynamic Island.",
enableLyrics: "Show lyrics",
online: "Find lyrics online",
onlineHint: "While lyrics are open, send the song title, artist, album and duration to lrclib.net. Imported lyrics stay on this Mac. Song titles, artist names and albums may also be checked with musicbrainz.org.",
importLyrics: "Import lyrics…",
importHint: "Choose timed lyrics for the current recording. They are kept only while this song is open.",
loading: "Loading lyrics…",
unavailable: "No matching lyrics for this recording.",
failed: "Could not load the lyrics.",
retry: "Try again",
instrumental: "Instrumental",
waiting: "Waiting for the first verse",
noPosition: "The player does not share its position.",
offset: "Timing",
earlier: "Earlier",
later: "Later",
reset: "Reset",
actionFailed: "The player did not switch to this song.",
playbackFailed: "Could not change playback.",
allowPlayback: "Allow playback control",
automationPermission: "Playback automation",
automationExplanation: "Some music apps require permission for their playback controls. Authorize the selected player from Dynamic Island; access is granted separately for each app.",
queue: "Up next",
queueDescription: "See the actual upcoming songs shared by your player.",
enableQueue: "Show upcoming songs",
queueUnavailable: "This player does not share its upcoming songs.",
queueEmpty: "No upcoming songs",
playNow: "Play now",
refresh: "Refresh",
openPlayer: "Open player",
playbackSource: "Playback source",
automaticSource: "Automatic",
includeOtherPlayers: "Automatically include videos and other apps",
liveEqualizer: "Bars follow the music",
liveEqualizerDescription: "Move the Dynamic Island bars with the sound your player makes.",
liveEqualizerHint: "Listens to the player’s own audio output to move the bars. macOS asks once to allow system audio recording. Nothing is stored or sent. Without it, the bars keep their usual motion.",
liveEqualizerUnavailable: "Needs macOS 14.4 or later.",
shuffle: "Shuffle",
showVolume: "Show volume in the player",
preferredPlayer: "Open when nothing is playing",
preferredPlayerHint: "The cover and the playback buttons open this app. Automatic opens the music app that played last, then Spotify or Apple Music.",
openNamedPlayer: "Open %@",
browserCovers: "Find covers for Firefox and Zen",
browserCoversHint: "When Firefox, Zen or another Firefox-based browser plays without a sharp cover, read its saved session on this Mac to find the song’s YouTube tab and load its thumbnail from i.ytimg.com. If no tab shows the song but a YouTube Music tab is open, send the song title, artist and region to itunes.apple.com and load the album art from mzstatic.com."
```

### NotchNotificationStrings.swift (`static let enUS`)

```
title: "Notifications",
description: "New system notifications in the Dynamic Island.",
privacy: "Show new visible banners only. Messages stay in memory and are cleared when you lock this Mac or turn this off.",
empty: "New notifications will appear here",
waiting: "Waiting for the system notification service",
open: "Open",
dismiss: "Dismiss",
clearAll: "Clear All",
unavailable: "This notification can no longer accept this action.",
hideSystemBanner: "Hide the system banner",
hideSystemBannerHint: "Hide the original while the Dynamic Island shows it."
```

### NotchTourStrings.swift (`static let enUS`)

```
preview: "Preview for 3.4",
caption: "Control music, timers, volume and brightness. Choose the floating shortcuts you use most."
```

### NotchWatchStrings.swift (`static let enUS`)

```
title: "Watch",
description: "Turn any part of any window into a live activity in the Dynamic Island, with an alert when it changes, finishes or shows what you are waiting for.",
setupHint: "Choose any part of a window, like a progress bar, a build status or a score. The island reads it for you and tells you when it changes.",
choose: "Choose Area",
chooseAgain: "Choose Another Area",
purpose: "Choose what to watch",
stop: "Stop Watching",
watchAgain: "Watch Again",
permissionHint: "Watch reads the area you choose on your Mac, so it needs Screen Recording. Nothing leaves your Mac.",
allowAccess: "Allow Screen Recording",
tellMe: "Tell me",
changes: "When it changes",
settles: "When it stops changing",
contains: "When it shows",
reaches: "When the number reaches",
settlesHint: "After it changes, it must stay still for 30 seconds.",
textPlaceholder: "Text, like Done",
numberPlaceholder: "Number, like 100",
noText: "No text",
inIsland: "Show in the island",
automatic: "Automatic",
hidden: "The window is hidden. Watching continues when it returns.",
changedNotice: "It changed",
settledNotice: "It stopped changing",
showsFormat: "It shows %@",
reachedFormat: "It reached %@",
closedNotice: "The window closed",
sinceFormat: "Watching since %@",
sound: "Play a sound"
```

### ClaudeAccountStrings.swift (`static let enUS`)

```
title: "Claude account",
defaultAccount: "Default account",
chooseFolder: "Choose account folder…",
explanation: "Choose a profile already signed in to Claude Code. Usage comes from that profile’s sessions, and Claude Code checks its account’s limits. Changing this selection does not change the account in your other apps.",
invalidFolder: "Choose an existing Claude Code profile folder that belongs to you and contains its own .claude.json file. The folder must not be writable by others, and the file must not be a link.",
unknownAccount: "No account identified in this profile.",
noLimits: "Claude Code has not returned limits for this account yet.",
signInHelp: "How to sign in to another Claude account"
```

### CodexAccountStrings.swift (`static let enUS`)

```
title: "Codex accounts",
explanation: "Each CODEX_HOME folder you add gets its own limits card. While the AI Agents page is open, Codex checks each one about every five minutes with that folder’s own sign-in. No prompt is sent.",
accountName: "Account name",
remove: "Remove this account’s card",
find: "Find local accounts",
add: "Add folder…",
check: "Check limits",
fallback: "With no accounts added, Codex limits come from its local sessions.",
resetsNote: "The Resets card still belongs to the default Codex account.",
chooseFolder: "Choose a Codex folder",
invalidFolder: "Choose a separate CODEX_HOME folder with config.toml or auth.json in it. The folder must be yours and not writable by others, and auth.json must be private and not a link. Codex may refresh its own sign-in and caches there.",
folderProblem: "Choose this account’s folder again in Settings.",
defaultName: "Default",
defaultAccount: "Default account",
refreshHint: "Refresh to check this account’s limits.",
signIn: "Sign in with ChatGPT in this Codex folder.",
update: "Update Codex to check limits.",
failed: "Could not check limits. Try again."
```

---------------------------------------------------------------------------------------------------

## 12. Defaults catalog (`Core/Defaults.swift`, registered defaults; every key is also part of Backup/Restore unless noted)

Legend: S=String, B=Bool, D=Double, I=Int, A=String array, X=blob. "reg" = registered default; "unreg" = no registered default (absent until written).

### 12.1 Island core
| Key | Type | Default |
|---|---|---|
| notchEnabled | B | false |
| notchDisplay | S | "automatic" (automatic, builtIn, main, pointer, all) |
| notchSilhouette | S | "capsule" (capsule, notch) |
| notchSize | S | "spacious" (compact, spacious, custom) - fresh profiles pin spacious; a pre-existing unconfigured profile keeps the old implicit "compact" |
| notchCustomWidth / notchCustomHeight | D | 440.0 / 480.0 |
| notchOutlineEnabled | B | false |
| notchHideMenuBarGap | B | false |
| notchCameraFitWidth / notchCameraFitHeight | D | 0.0 / 0.0 |
| notchCapsuleFitWidth / Height / Drop | D | 0.0 / 0.0 / 0.0 |
| notchTranslucentBackground | B | false |
| notchLiquidGlassEnabled | B | false (migrated once from `liquidGlassEnabled`) |
| notchOpenOnHover | B | false |
| notchHoverExpands | B | true |
| notchHideUntilHover | B | false |
| notchHiddenShowsActivity | B | false |
| notchHoverDelay | D | 0.25 |
| notchCloseDelay | D | 0.18 |
| notchShortcutEnabled | B | false |
| notchShortcut | S | Control+Option+Command+I (`GlobalShortcut.notchDefault.storageValue`) |
| notchGesturesEnabled | B | true |
| notchHapticFeedback | B | true |
| notchHideInFullscreen / notchHoverInFullscreen | B | false / false |
| notchCoversMenus | B | true |
| notchReturnHome | B | false |
| notchHomeModule | S | "controls" (module raw value, "appPanel", "explore") |
| notchOpensActivity | B | true |
| notchShowInCaptures | B | true |
| notchHideInCaptures | B | false (legacy inverse; superseded by notchShowInCaptures) |
| notchLockScreen / notchLockSounds | B | false / false |
| panelControlNotch | B | true |
| notchHidesMenuBarIcon | B | false |

### 12.2 Content / layout
| Key | Type | Default |
|---|---|---|
| notchModuleOrder | S CSV | "" |
| notchHiddenModules | S CSV | "" |
| notchHiddenControls | S CSV | "keyboardLight,microphone,screenshot,recording,speedTest,panel,commandBar,scratchpad" |
| notchControlOrder | S CSV | "" |
| notchScratchpadControlHidden / notchKeyboardLightControlHidden | B | false / false (one-shot migration markers that append "scratchpad"/"keyboardLight" to a saved hidden list) |
| notchQuickAccessLayout | X (JSON) | empty Data -> built-in default layout (7.1) |
| notchQuickAccessSide / Second / Third | S | unreg (legacy, read only if present) |
| notchShelf | B | true |
| notchDragReveal | B | true |
| notchCaptureControls | B | true |
| notchQuickPanel | B | true |
| notchAppPanel | B | false |
| notchScratchpad | B | true |
| notchClipboardWindow | B | true |
| notchClipboardCardSize | S | "comfortable" (compact, comfortable) |
| notchKeepAwakeActivity | B | false |

### 12.3 Music
| Key | Type | Default |
|---|---|---|
| notchShowPlayingMusic | B | true |
| notchIncludeOtherPlayers | B | true |
| notchMusicShowVolume | B | false |
| notchPreferredPlayer | S | "" |
| notchLyricsEnabled / notchLyricsOnline | B | true / false |
| notchBrowserCovers | B | false |
| notchLiveEqualizer | B | false |
| notchQueueEnabled | B | true |
| notchIdleContent | S | "music" (none, battery, music, agents) |
| notchMusicActivity | B | false (legacy backup key, superseded by notchIdleContent) |

### 12.4 Indicators / notices
| Key | Type | Default |
|---|---|---|
| notchVolume, notchBrightness, notchKeyboardLight, notchMicrophone, notchBattery, notchInputSource, notchClipboard, notchCapture, notchTrackChange | B | true each |
| notchAccessoriesEnabled | B | true |
| notchLowBatteryTint | B | false |
| notchLowBatteryThreshold | I | 10 |
| notchLowBatteryEarly | B | false |
| notchLowBatteryEarlyThreshold | I | 20 |
| notchLowBatteryMenuBar | B | true |
| notchNotificationsEnabled | B | true |
| notchDismissNativeNotifications | B | false |
| notchDownloadsEnabled | B | true |
| notchDownloadsFolderBookmark | X | unreg, never backed up |

### 12.5 Timer / camera / calendar / watch
| Key | Type | Default |
|---|---|---|
| notchTimerEnabled | B | true |
| notchTimerMode | S | "timer" |
| notchTimerMinutes | I | 15 |
| notchTimerSoundEnabled | B | true |
| notchHideTimerCountdown | B | false |
| notchPomodoroFocusMinutes / ShortBreakMinutes / LongBreakMinutes / LongBreakInterval / TotalSessions | I | 25 / 5 / 15 / 4 / 4 |
| notchCameraEnabled | B | true |
| notchCalendarEnabled | B | true |
| notchCalendarCountdown | B | false |
| notchCalendarCountdownLead | I | 60 (clamped 5...240) |
| notchCalendarTimeLeft | B | false |
| notchCalendarWeekNumbers | B | false |
| notchCalendarExcluded | A | [] |
| notchCalendarChosenCountdowns | dict | unreg (local only, out of backups) |
| notchWatchEnabled | B | true |
| notchWatchSound | B | true |
| notchWatchCondition | S | "changes" (changes, settles, contains, reaches) |

### 12.6 AI agents
| Key | Type | Default |
|---|---|---|
| notchAgentsEnabled | B | false |
| notchAgentsClaude / Codex / OpenCode / Copilot | B | true each |
| notchAgentsClaudeProfile | S | "" |
| notchAgentsCodexProfiles | S (JSON) | "" |
| notchAgentsCardOrder / notchAgentsHiddenCards | S CSV | "" / "" |
| notchAgentsPeriod | S | "today" |
| notchAgentsLimitDisplay | S | "remaining" |
| notchAgentsLimitFocus | S | "mostUsed" |
| notchAgentsLiveActivity | B | true |
| notchAgentsReadout | S | "elapsed" |
| notchAgentsFinishAlert | B | true |
| notchAgentsFinishMinimum | D | 60.0 |
| notchAgentsLimitAlert | B | true |
| notchAgentsLimitThreshold | D | 80.0 |
| notchAgentsDailyBudget | D | 0.0 |
| notchAgentsPriceUpdates | B | true |
| notchAgentsOptInMigrated | B | unreg marker |

### 12.7 Companion
| Key | Type | Default |
|---|---|---|
| notchMascotEnabled | B | false |
| notchMascotVisits / notchMascotReactions | B | true / true |
| notchMascotHidesWhenIdle | B | true |
| notchMascotStyle / Shape / Palette / Side / VisitFrequency | S | "minimal" / "ball" / "pearl" / "left" / "normal" |
| notchCommandBar | B | true |
| notchCommandBarStyle | S | "droplet" |
| notchMascotBetaInstalled | B | unreg marker |

### 12.8 Related keys outside the island namespace read by these pages
`brightnessControlEnabled` B false; `clipboardHistoryEnabled` B false; `screenshotDefaultAction` S "" (none/save/saveAndCopy/copy/edit); `liquidGlassEnabled` B false.

### 12.9 First-run / migration markers (all unregistered, never backed up)
`notchDefaultProfileInitialized`, `notchInitialExtensionsInstalled`, `notchAgentsOptInMigrated`, `notchMascotBetaInstalled`. A profile already configured under the previous island keeps its old implicit values (size spacious, appPanel true, keyboardLight/notifications/camera/accessories/agents/lyrics/equalizer/queue/downloads off, openOnHover true, clipboard/capture/trackChange off) - **irrelevant for a fresh Trayloom install**, which gets the registered defaults above.

---------------------------------------------------------------------------------------------------

## 13. Windows mapping (consolidated)

### 13.1 Host window and rendering
Island = one top-most WPF/Win32 layered window per target monitor: `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST`, `AllowsTransparency=true` (or `WS_EX_LAYERED` + per-pixel alpha), shape from `Geometry` (capsule or notch silhouette), rounded corners by clipping geometry (DWM `DWMWA_WINDOW_CORNER_PREFERENCE` only applies to rectangular non-layered windows; for a custom silhouette use clip + the shadow-less look of the original). Black fill; translucent mode = Acrylic backdrop. Settings previews instantiate the same controls scaled (no second implementation).
Placement: `monitor.Bounds.Top + notchCapsuleFitDrop (+ Trayloom topGap legacy)`; centred on `Bounds`; DPI-aware (per-monitor v2) - all sizes in DIPs.

### 13.2 What cannot exist on Windows (and the replacement)
| macOS thing | Why impossible | Windows replacement |
|---|---|---|
| Camera housing ("notch"), "Notch fit", "Hide gap below notch" | No Windows laptop reports a camera cut-out; display area is a full rectangle | Remove those controls; keep `notchSilhouette` (Notch shape = flat top flush tab). Optionally let advanced users nudge with Capsule fit |
| "Show over the menus" | No menu bar | Hide row; island always above windows |
| Accessibility permission rows | Hooks/overlays need no consent | Hide |
| Screen Recording permission row (Watch) | Not a TCC permission | Hide (Windows.Graphics.Capture shows a yellow border by default; `IsBorderRequired=false` needs Win11 and capability) |
| Lock Screen display | LogonUI secure desktop cannot be drawn over | Hide "Show on the Lock Screen"; keep sounds |
| Liquid Glass (macOS 26) | macOS API | Acrylic/Mica via DWM/`SetWindowCompositionAttribute` |
| Trackpad haptics | Not generally exposed | Show only if `SimpleHapticsController` present |
| System audio recording consent for the equalizer | Not needed | WASAPI loopback / `IAudioMeterInformation` always allowed |
| "Mac unlocked" | wording | "PC unlocked" |

### 13.3 Display options
See 5.3. API: `EnumDisplayMonitors`, `GetMonitorInfo`, `QueryDisplayConfig` (internal-panel detection), `WM_DISPLAYCHANGE`/`SystemEvents.DisplaySettingsChanged` (already hooked in Trayloom), lid state via `GUID_LIDSWITCH_STATE_CHANGE` + `RegisterPowerSettingNotification`.

### 13.4 Music (SMTC)
Playback source = `GlobalSystemMediaTransportControlsSessionManager` (already in `MediaService`). "Automatically include videos and other apps" = whether sessions from browsers/video apps (`SourceAppUserModelId` not in the music-apps list) may become the current session; off -> only music apps. "Open when nothing is playing" = list of installed music apps: known list (Spotify, Apple Music for Windows (`AppleInc.AppleMusicWin_*!App`), iTunes, Amazon Music, YouTube Music desktop, Windows Media Player (`Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic`), foobar2000, MusicBee, AIMP, VLC) detected through Start-menu apps (`shell:AppsFolder` / registry `App Paths`) plus every `SourceAppUserModelId` ever seen on SMTC (persisted); value stored = AUMID or exe path; "" = Automatic (last played, then Spotify, then Apple Music). Lyrics online (lrclib.net / MusicBrainz) and queue (SMTC exposes no queue: show "This player does not share its upcoming songs." except for players with an API) are runtime. Live bars: `AudioMeterInformation.MasterPeakValue` of the player's audio session (`AudioSessionManager2` -> process id of the SMTC source) or WASAPI loopback FFT. Browser covers: Firefox profile `%APPDATA%\Mozilla\Firefox\Profiles\<profile>\sessionstore-backups\recovery.jsonlz4` (mozlz4) - SMTC usually already supplies the thumbnail, so this is low priority (keep switch, default off).

### 13.5 Permissions and settings deep links
Calendar: `ms-settings:privacy-calendar`. Camera (mirror): `ms-settings:privacy-webcam`; status from `DeviceAccessInformation.CreateFromDeviceClass(DeviceClass.VideoCapture).CurrentStatus` (Allowed => show green "Granted"; else buttons "Grant access" (try `MediaCapture.InitializeAsync` to trigger the prompt) and "Open Windows Settings"). Notifications listener: `ms-settings:privacy-notifications`.

### 13.6 Notifications section
`Windows.UI.Notifications.Management.UserNotificationListener.Current.RequestAccessAsync()`; `GetNotificationsAsync(NotificationKinds.Toast)`; `NotificationChanged` event. **Requires package identity** (MSIX or a sparse package with the `userNotificationListener` capability) - an unpackaged WPF exe gets `UnauthorizedAccess`/not supported. Plan: register a sparse MSIX identity for Trayloom, or show "Waiting for the system notification service" state with a note. "Hide the system banner": after capturing a toast, `RemoveNotification(id)` clears it from Action Center; suppressing the banner itself is best-effort (cloak the `ShellExperienceHost` "New notification" toast window via UI Automation/`DwmSetWindowAttribute(DWMWA_CLOAK)`); mark the option beta and keep the caption verbatim. Replace the Accessibility PermissionRow with an access row ("Granted" / "Grant access" / "Open Windows Settings"). Notifications privacy caption stays: "Show new visible banners only. Messages stay in memory and are cleared when you lock this Mac or turn this off." (-> "this PC").

### 13.7 AI agents on Windows
Log roots (per user profile `%USERPROFILE%`): Claude Code `%USERPROFILE%\.claude\projects` and `%USERPROFILE%\.config\claude\projects`, or `<profile>\projects` (also honor `CLAUDE_CONFIG_DIR`); account metadata `%USERPROFILE%\.claude.json` (default) or `<profile>\.claude.json`; Codex `%USERPROFILE%\.codex\sessions`, `\.codex\archived_sessions` (honor `CODEX_HOME`), auth in `auth.json`; OpenCode `%USERPROFILE%\.local\share\opencode` (database file inside; also check `%APPDATA%\opencode`/`%LOCALAPPDATA%\opencode`); GitHub Copilot CLI `%USERPROFILE%\.copilot\session-state\<id>\events.jsonl`. Claude desktop plan history `%APPDATA%\Claude\plan-usage-history.json`; app detection via `%LOCALAPPDATA%\AnthropicClaude\claude.exe`, `%LOCALAPPDATA%\Programs\Claude\Claude.exe` or the MSIX package (`Get-AppxPackage *Claude*`); "Open Claude" launches it; limits come primarily from Claude Code's own server (spawn `claude` with the profile as `CLAUDE_CONFIG_DIR`). Watch logs with `FileSystemWatcher` (`NotifyFilters.LastWrite | Size`, recursive) plus a 5 s coalescing timer; read with `FileShare.ReadWrite`.
Profile/home validity on NTFS (replaces POSIX owner/permission checks): path is absolute, not a drive root nor `%USERPROFILE%`; directory is not a reparse point (`FILE_ATTRIBUTE_REPARSE_POINT` unset); owner SID (`GetNamedSecurityInfo OWNER_SECURITY_INFORMATION`) == current user; DACL grants no write/modify to Everyone (S-1-1-0), Authenticated Users (S-1-5-11), Users (S-1-5-32-545); `.claude.json`/`auth.json` regular file, owner == user, `GetFileInformationByHandle(...).nNumberOfLinks == 1`, not a reparse point, and for `auth.json` the DACL grants access only to the user, SYSTEM and Administrators ("private").
Folder pickers: `IFileOpenDialog` with `FOS_PICKFOLDERS | FOS_FORCESHOWHIDDEN`, initial folder `%USERPROFILE%`.
Codex check: run the Codex CLI (`codex.exe` or the npm shim `codex.cmd` found on PATH / `%APPDATA%\npm`, or the Codex app's bundled CLI) with environment `CODEX_HOME=<dir>` + the allow-listed variables (USERPROFILE, HOME, USERNAME, TEMP/TMP, proxy and cert variables) via `ProcessStartInfo.EnvironmentVariables` with `Environment.Clear()` first.
Tray/AUMID: none needed.

### 13.8 Calendar
See section 9 (WinRT Appointments). Week numbers use `CultureInfo.Calendar.GetWeekOfYear(date, rule, firstDayOfWeek)` where firstDayOfWeek = the locale's (`DateTimeFormatInfo.FirstDayOfWeek`) or Trayloom's existing `weekStart` setting (auto/monday/sunday), rows starting on that day so every day of a row shares one number.

### 13.9 Storage
Use the existing `IFeatureSettings` store with the **same key names** as the Mac (backup/restore compatible). Island features should share ONE settings namespace "island" (today Trayloom has per-feature namespaces `island`, `island-timer`, `island-calendar`, `island-downloads`, `island-gestures`): migrate old keys: `trigger` -> `notchOpenOnHover` (hover=true), `monitor` -> `notchDisplay` (primary->main, cursor->pointer), `collapseDelay` ms -> `notchCloseDelay` seconds, `topGap` -> `notchCapsuleFitDrop` (clamp 0...20), `hideInFullscreen` -> `notchHideInFullscreen`, timer `focusMinutes/shortBreakMinutes/longBreakMinutes/sessionsBeforeLong/playSound` -> `notchPomodoroFocusMinutes/...ShortBreakMinutes/...LongBreakMinutes/...LongBreakInterval/notchTimerSoundEnabled`, gestures `tracks`, calendar `weekStart`/`showEvents`.

---------------------------------------------------------------------------------------------------

## 14. Gap vs Trayloom (summary)

Existing code: `Trayloom.Features.Island/{IslandFeature, IslandWindow, IslandHub, IslandTheme, IslandNative, MediaService, MusicSection, TimerFeature, CalendarFeature, DownloadsFeature, GesturesFeature}.cs`, generic settings builder `Trayloom.Core/Ui/SettingsCard.cs` (Toggle, Choice, Slider, IntSlider, Hotkey, Text, Note).
| Area | Trayloom today | Needed |
|---|---|---|
| Page shell | Per-feature flat settings; island is installed/uninstalled in Features hub | One "Dynamic Island" page with master switch, open button, 5-tab segmented row, Companion tab gating |
| Controls | Only switches/choices/sliders | Tile `choice`, toggle card with corner status button, checkbox section list with tinted tiles, adaptive reorderable grids, popover chooser, preview canvas, segmented/menu fallback picker, signed fit sliders, strip sample |
| Layout tab | `topGap` only; fixed idle 116x34, expanded 480x292 | Size presets (compact 480 / spacious 560 / custom 360-600 x 260-640), translucent bg, silhouette, capsule fit, outline, quick-access button editor |
| Content tab | Fixed order sections: Music, Timer, Calendar, Downloads | 16 sections with order/hide, per-section options + live preview; Controls, Mixer, Clipboard, Captures, Files, System, Tools, Notifications, Camera, Scratchpad, AI Agents, Watch missing |
| Activity tab | none | Resting content, low-battery tint, 10 indicator toggles, lock sounds |
| Behavior tab | hover/click, collapse delay choices, monitor, full-screen hide | 4 opening modes, delays 0.10-1.0 / 0.10-2.0 s, shortcut, gestures, haptics, reopening, display 5 modes, destinations routing, tray icon hide, privacy |
| Companion | none | whole feature |
| AI Agents | none | whole feature incl. Claude/Codex accounts, usage store, limits, alerts, price list |
| Calendar | week start, show events; computes week numbers always | countdown, lead, time-left, week numbers toggle, calendars shown, permission UI |
| Defaults | hideInFullscreen true; hover opens; close delay 450 ms | Align to Vorssaint defaults (click opens; hideInFullscreen false; close 0.18 s) unless product decides Windows-specific defaults |
| Strings | Hard-coded English ("Open the island", "Show on", ...) | Replace with the verbatim tables in section 11 |
