# 02b. Settings pages: Mouse and Trackpad, Switcher, Dock, Window layout, Quit on close, Quit protection, Debounce, Super key, Text snippets

Source of truth: `Sources/Vorssaint/UI/Settings/*.swift` (+ `UI/WindowGestureControls.swift`, `UI/ShortcutRecorderButton.swift`, `UI/SnippetLibraryView.swift`), strings in `Core/Localization.swift` (`Strings.enUS`), `Core/FeatureStrings.swift` and the other `Core/*Strings.swift` (`static let enUS`), defaults in `Core/Defaults.swift`.
All English strings below are verbatim from the `enUS` blocks. Typographic apostrophes (U+2019 `’`) are kept as in the source. The word "Vorssaint" is written "Trayloom". macOS-only wording is quoted verbatim and followed by a `[WIN]` replacement or a "macOS-only" flag with the closest Windows equivalent.

Conventions used in this file

* "Page" = one Settings sidebar page. "Card" = `SettingsCard`. "Row" = `SettingsRow`.
* "available" = the feature is installed in the Features hub (`AppFeature.x.isAvailable`). A card/row whose feature is not installed is not drawn at all.
* Key names are the exact `UserDefaults` keys (`DefaultsKey.*` raw strings). Where the Swift property name differs from the stored string, both are given. Defaults are the values in the `register(defaults:)` block in `Core/Defaults.swift` (these win over `@AppStorage` initial values; one place where they differ is called out).
* Modifier mapping used throughout (matches the existing Trayloom convention): macOS Control = Ctrl, Option = Alt, Shift = Shift, Command = Win (glyph `⌘` becomes "Win"). In three-modifier default chords (Ctrl+Opt+Cmd) Trayloom already uses Ctrl+Alt+Shift because the Win key is reserved by the shell; keep that.
* Segoe Fluent Icons code points are suggestions (name given so they can be looked up in the font map). Nothing here is binding if a glyph is missing in the font on Windows 10.
* Page text sizes in the Swift sources are SwiftUI semantic styles; macOS point sizes: `title2`=17 (bold), `headline`=13 semibold, `body`=13, `callout`=12, `subheadline`=11 (medium), `caption`=10 (≈11 rendered), `caption2`=10. Use the same numbers as WPF DIPs.

---

## 0. Shared building blocks used by every page below

### 0.1 Page chrome (card pages: Mouse & Trackpad, Switcher, Dock)

* `ScrollView` > `VStack(alignment: .leading, spacing: 20)`, content `maxWidth: 760` centred in the pane, outer `padding(22)`.
* Page header: `Text(title).font(.title2.bold())` then (spacing 5) `Text(description).font(.callout).foregroundStyle(.secondary)`, wrapping.
* Toggles are switch style (`.toggleStyle(.switch)`).

### 0.2 `SettingsCard`
`VStack(alignment: .leading, spacing: 13)`; optional heading `Text(title).font(.headline)`; card is `maxWidth: .infinity`, `padding(16)`, fill `.quaternary.opacity(0.35)`, corner radius 16 (continuous).

### 0.3 `SettingsRow`
`HStack(spacing: 12)`: icon tile 26x26 (SF Symbol 12pt semibold, accent colour, tile fill accent 12 %, radius 7) · `VStack(spacing 2)` { title (body) [+ optional badge capsule: 8pt bold white on accent, padding 5x1], caption (`.caption`, secondary, wraps) } · `Spacer(minLength 12)` · accessory control (right aligned).
`settingsRowTextInset = 26 + 12 = 38`: follow-up controls under a row are indented by 38 so they line up with the row text.

### 0.4 Chips (used for modifier keys, switcher choices, tap fingers)
Button, `height 28`, horizontal padding 10, font 11pt medium, corner radius 8 (7 for the modifier keycaps). Selected: text accent, fill accent 12 %, stroke accent 70 % 1 px. Unselected: text primary, fill `secondary 6 %` (keycaps: `primary 6 %` with `primary 14 %` stroke), stroke clear. Chip rows wrap (`FlowLayoutLite`, spacing 8).

### 0.5 Active badge (`activeBadge`)
`Label(text, "checkmark.circle.fill")`, caption medium, green (`PanelMetricColor.green`), padding 8x4, capsule background green 12 %. Glyph suggestion: `U+E73E` CheckMark (filled circle variant `U+EC61` CompletedSolid).

### 0.6 Slider row (`sliderRow` on Mouse page)
`HStack(spacing 12)`: label (min width 96, leading) · `Slider(value, in: range, step)` · readout `caption monospacedDigit secondary`, width 60, trailing.

### 0.7 Shortcut recorder row (`ShortcutPreferenceRow`)
Left: icon + label (default label "Shortcut"). Right: recorder button 108 wide (shows shortcut, or "Press keys" while recording) + `Reset` button (disabled when equal to default or row disabled). Under it: orange caption error, or while recording the caption "Press the new shortcut Escape cancels." (parts joined by space: `shortcutRecording` + `shortcutEscapeHint`, and `+ shortcutDeleteHint` when Delete may clear). Conflict message: "This shortcut is already used by %@." Invalid: "Use at least Control, Option or Command with a key." Not captured: "Nothing was captured. macOS or another app already uses that combination. Try another one." [WIN] "Nothing was captured. Windows or another app already uses that combination. Try another one." Reject: "macOS rejected this shortcut. Choose another one." [WIN] "Windows rejected this shortcut. Choose another one." System take-over offer (macOS): "macOS uses %@ for one of its own shortcuts." / buttons "Don’t take over", "Take over while Vorssaint runs" / caption "macOS gets the key back whenever Vorssaint is not running or this feature is off." [WIN] same text with Windows (applies to Win+Arrows, Alt+Tab, Win+Tab etc., see Switcher).
The recorder component is covered by the Shortcuts spec; this file only lists where it is placed.

### 0.8 Permission rows
Every page conditionally shows a `PermissionRow(kind: .accessibility)` (and Switcher/Dock `.screenRecording`) in a card titled "Permission required" (strings `permissionRequired`="Permission required", `permissionAccessibility`="Accessibility", `permissionScreenRecording`="Screen Recording", `permissionGranted`="Granted", `permissionMissing`="Not granted", `permissionOpenSettings`="Open System Settings…", `permissionRequest`="Grant access", `permissionRestartNote`="macOS may ask to reopen the app after granting.").
**macOS-only. Windows:** no permission exists for hooks, SendInput, SetWinEventHook or window capture, so these cards/rows never appear. The only Windows analogue is UIPI: a non-elevated Trayloom cannot hook, inject into or move windows of elevated (admin) processes. Optional replacement (not in the original): a single orange caption "Some windows run as administrator and ignore Trayloom. Restart Trayloom as administrator to include them." shown when the foreground window is elevated. Every `permissions.requestAccessibility()` call in the Swift onChange handlers becomes a no-op.

### 0.9 `AppBundleList` ("apps this feature treats differently") — used by 7 lists
Layout: `DisclosureGroup`; label row = `Text(title)` + `Spacer` + count (`"\(n)"`, secondary, monospaced digit, only when n>0). Initial state: expanded iff the list is non-empty at creation (no persistence of the disclosure state). Inside, top to bottom:
1. One row per app, sorted by display name (case-insensitive, localized compare; ties broken by identity string): `HStack(spacing 9)` { icon 18x18, name (1 line) [for path identities a `VStack(spacing 1)` { name, location caption `.caption` secondary 1 line, head-truncated }, tooltip = identity], `Spacer(minLength 8)`, optional per-row accessory (only Switcher rules use one), remove button = `minus.circle.fill` in secondary, accessibility label = removeLabel }.
2. Add button (`Label(addTitle, "plus")`, small control size, left aligned).
3. Caption (`.caption` secondary, wraps, left aligned).
`AppPickerView` sheet (opened by Add): 520x560 sheet, padding 18, `VStack(spacing 12)`: header `HStack` { "Choose app" (16 semibold), `Spacer`, [if `reachesEveryApp`] button `Label("Choose app…", "folder")` that opens a file chooser (apps; plus executables when `acceptsExecutables`), button "Cancel" }; search field `TextField("Search apps")` rounded border; list. States: loading = spinner + "Scanning files…" (area height 360); empty = `app.dashed` glyph 34pt light + "No apps found" (+ optional tertiary `emptyNote`) ; list = `LazyVStack(spacing 2)` rows { icon 28x28, name 13 medium, subtitle 11 secondary (location head-truncated, else bundle id, else path) }, row padding 8x7, radius 6. Filter: case-insensitive "contains" on name, bundle id, identity. Apps are loaded on a background queue on first appearance. Compact variant (used in menu panel, not on these pages): 10 padding, 150/235 heights.
Strings (all pages): `uninstallerPickerTitle`="Choose app", `uninstallerChoose`="Choose app…", `uninstallerCancel`="Cancel", `uninstallerPickerSearch`="Search apps", `uninstallerPickerEmpty`="No apps found", `uninstallerScanning`="Scanning files…", `uninstallerPickerHiddenNote`="Apps that are part of macOS or made by Apple aren’t listed." ([WIN] the note is not needed, or: "Windows system components aren’t listed.").
Identity: macOS stores bundle identifiers; "executables" (not packaged as an app) are stored as a path identity; `MouseAppExceptionSupport.pickedIdentity(for:)` decides which.
**Windows mapping:** identity = lower-cased full exe path for classic apps, and `PackageFamilyName!AppId` (AUMID) for packaged apps (Process → `GetApplicationUserModelId`); both are shown (name from `FileVersionInfo.FileDescription`/package display name; location caption = exe folder for path identities). Running apps: enumerate visible top-level windows → PID → exe (`QueryFullProcessImageName`). Installed apps: Start-menu `.lnk` + `shell:AppsFolder` (`IShellItem` enumeration) so UWP apps appear; "Choose app…" = `OpenFileDialog` filter `*.exe`. Icons: `SHGetFileInfo`/`IShellItemImageFactory` 18 px/28 px. Matching at runtime: compare the foreground/pointer-window owner's exe path (case-insensitive) or AUMID; also match by file name when only a name was stored. `acceptsExecutables` is always true on Windows (every app is an exe), so the flag only changes which lists show the file chooser; keep it `true` everywhere.
**Gap:** Trayloom has no equivalent component. `Trayloom.Core/Ui/SettingsCard.cs` has `AddText("excludedApps", …)` (a comma-separated text box in `CloseProtectionFeature.cs:111`). Build `AppBundleList` + `AppPicker` once in `Trayloom.Core/Ui`.

### 0.10 `MouseExceptionsList` (per-feature "Apps to leave alone")
`AppBundleList(title: "Apps to leave alone", addTitle: "Add an app…", removeLabel: "Remove", reachesEveryApp: true, acceptsExecutables: true)`, caption depends on scope. One independent list per scope, stored as `[String]` (app identities):

| scope | key | caption (verbatim) | shown by |
|---|---|---|---|
| smoothScroll | `smoothScrollExceptions` | "The wheel keeps its plain steps in these apps, for apps that read it their own way, like 3D and design tools." | Smooth scroll card, when on |
| linearScroll | `linearScrollExceptions` | "The wheel keeps the pace macOS gives it in these apps, for games and 3D tools that count the notches themselves." [WIN] "…keeps the pace Windows gives it…" | Linear scroll card, when on |
| scrollDirection | `scrollInverterExceptions` | "The wheel keeps the direction macOS gives it in these apps." [WIN] "…direction Windows gives it…" | Scrolling card (and Zoom card if the Scrolling card is not available) while invert / horizontal / zoom is on |
| navigation | `mouseNavigationExceptions` | "The side buttons keep doing whatever these apps already do with them." | Navigation card, when on |
| buttonShortcuts | `mouseButtonExceptions` | "Your extra mouse buttons stay quiet in these apps, and the press reaches them instead." | Mouse button shortcuts card, when either shortcuts or Spaces-drag switch is on |
| middleClick | `middleClickExceptions` | "A three finger click stays a normal click in these apps." | Middle click card, when on |
| focusFollowsMouse | `focusFollowsMouseExceptions` | "Hovering does not change focus or raise a window in these apps." | Focus follows mouse card, when on |
| superKey | `superKeyExceptions` | "While any of these apps is open, even in the background, Super Key pauses and the chosen key works normally." [WIN] "Super key" | Super key page (section), when on |

Default for all: empty array. The check only runs while the owning feature is on. For wheel/mouse features the app tested is the one **under the pointer** (window-server hit test with 0.5 s result reuse: `resolveLifetime = 0.5`; ordinary windows are layers 0…3; the app's own panels are skipped); for focus follows mouse the hovered window's app; for Super key any *running* app on the list pauses the feature ("Paused while a selected app is open").
**Windows mapping:** `WindowFromPoint(pt)` → `GetAncestor(GA_ROOT)` → owner PID (cache by HWND for 500 ms; skip Trayloom's own windows and `Shell_TrayWnd`, `Progman`, `WorkerW` unless the identity is explorer). Super key: `Process.GetProcesses()` poll (every 1 s while enabled) against the list.
**Gap:** Trayloom has no exception lists at all except `close-protection.excludedApps` (text). Needs the shared list component and per-scope checks in `WheelPipeline.cs`, `FocusFollowsMouseFeature.cs`, `MouseButtonFeatures.cs`, `SuperKeyFeature.cs`.

---

## 1. Page "Mouse & Trackpad" (`MouseSettings.swift`)

Sidebar entry: title `tabMouse` = "Mouse & Trackpad", SF Symbol `computermouse` (Fluent: Mouse `U+E962`, fallback MouseCursor `U+E8B0`). Description (`SettingsPageStrings.mouseDescription`): "Give the wheel, the side buttons and the trackpad new jobs." [WIN] "…and the touchpad…".
Features on this page and their hub names (`hubTitle`) / SF symbol (for the legend and hub tiles):

| feature | hub title | hub description | SF symbol |
|---|---|---|---|
| scrollInverter | Invert mouse scrolling | Invert the mouse wheel direction | arrow.up.arrow.down |
| scrollHorizontal | Scroll sideways while holding a key | (the caption) Hold only the selected modifier to scroll the vertical mouse wheel horizontally. Other key combinations are unchanged. | arrow.triangle.swap |
| scrollZoom | Zoom | Pinch Zoom | plus.magnifyingglass |
| focusFollowsMouse | Focus follows mouse | Focuses the window under the pointer. | cursorarrow.and.square.on.square.dashed |
| smoothScroll | Smooth scrolling | Smooth, animated mouse scrolling | cursorarrow.motionlines |
| linearScroll | Linear scrolling | Every notch of the mouse wheel scrolls the same distance, no matter how fast it spins. The trackpad is not affected. | arrow.up.and.down.text.horizontal |
| mouseAcceleration | Disable mouse acceleration | Removes pointer acceleration for connected mice. Your previous setting returns when this is turned off or Trayloom quits. | cursorarrow.rays |
| mouseNavigation | Side buttons | Side mouse buttons go back and forward | arrow.left.arrow.right |
| mouseButtonShortcuts | Mouse button shortcuts | Extra buttons and side-wheel directions press a key combination you choose. | button.programmable |
| mouseClickDebounce | Extra click filter | Ignores rapid extra clicks from worn mouse buttons without slowing normal clicks. | cursorarrow.click |
| middleClick | Trackpad middle click | Three finger click acts as a middle click | hand.tap |

### 1.1 Layout (top to bottom)
1. Header: "Mouse & Trackpad" / description.
2. **Legend card** (shown if ≥1 mouse feature available). `SettingsCard` without title containing `LazyVGrid(columns: [adaptive(minimum 220)], spacing 4 horizontally, 2 vertically, leading)`. One button per available feature, in this order: scrollInverter, scrollHorizontal, scrollZoom, focusFollowsMouse, smoothScroll, linearScroll, mouseAcceleration, mouseNavigation, mouseButtonShortcuts, mouseClickDebounce, middleClick. Each: `HStack(spacing 10)` { 8x8 circle (on = accent, off = secondary 35 %), title (callout; primary when on, secondary when off; 1 line), spacer, `chevron.forward` 9pt semibold tertiary }, padding 6x8, whole row clickable. Click = navigate to the feature's section (page Mouse + `sectionAnchor`) scrolling it to the centre of the page and lighting the card (same highlight as a search result: `settingsSectionAnchor(anchor, cornerRadius: 16)`). "On" definitions: scrollInverter = invertVertical || invertHorizontal; scrollHorizontal = horizontalScrollEnabled; scrollZoom = zoom toggle on AND modifier ≠ None; focusFollowsMouse/smoothScroll/linearScroll/mouseNavigation/mouseClickDebounce/middleClick = their enable key; mouseAcceleration = `mouseAccelerationDisabled`; mouseButtonShortcuts = shortcuts switch || Spaces switch.
3. **Scrolling card** (title "Scrolling"; shown if scrollInverter OR scrollHorizontal available). Anchor `scrollDirection`.
   * [if scrollInverter] Row `arrow.up.arrow.down` "Invert vertical scrolling" - switch (key `scrollInverterEnabled`).
   * [if scrollInverter] Row `arrow.left.arrow.right` "Invert horizontal scrolling" - switch (`scrollInverterHorizontalEnabled`).
   * [if scrollHorizontal] Row `arrow.left.and.right` "Scroll sideways while holding a key" / caption "Hold only the selected modifier to scroll the vertical mouse wheel horizontally. Other key combinations are unchanged." - switch (`scrollHorizontalEnabled`).
   * [if that switch is on] modifier keycaps, indented 38: caption-ish label "Modifier key" (subheadline medium), then 4 keycap chips in order ⇧ Shift · ⌥ Option · ⌃ Control · ⌘ Command (glyph 13pt semibold rounded + name 11pt medium; single-select; `ScrollHorizontalModifier` raw: shift/option/control/command; default shift). Names: `FeatureStrings.quitProtection.shiftKey` "Shift", `optionKey` "Option", `controlKey` "Control", and `scrollHorizontalCommandKey` "Command".
   * [if invertVertical||invertHorizontal AND the wheel tap is running] green badge "Inverting mouse scrolling right now".
   * Note (caption, secondary): "The trackpad is untouched: it keeps macOS natural scrolling."
   * [if scrollDirectionEnabled = inversion || horizontal-scroll || zoom active] `MouseExceptionsList(.scrollDirection)`.
   * onChange of any of the three switches: sync the shared wheel tap; if any direction feature is now on, request Accessibility.
4. **Zoom card** (title "Zoom"; shown if scrollZoom available). Anchor `scrollZoom`.
   * Control row (no icon tile): `HStack(spacing 16)` { text "Pinch Zoom" (width 180 leading), `Picker` menu (flex width; disabled while the switch is off) with items "None" / "⌥ Option" / "⌃ Control" / "⌘ Command" (values `none`/`option`/`control`/`command`, key `pinchZoomModifier`, default `control`), switch (width 52 trailing, key `pinchZoomEnabled`, default off) }.
   * Caption: "Control + scroll performs app zoom instead of macOS screen zoom."
   * If neither Scrolling-card feature is available but zoom is on: the Scrolling exception list is shown here.
5. **Focus follows mouse card** (no title; anchor `focusFollowsMouse`).
   * Row `macwindow.and.cursorarrow` "Focus follows mouse" / "Focuses the window under the pointer." - switch (`focusFollowsMouseEnabled`).
   * When on (indented 38, `VStack(spacing 10)`): slider row "Hover delay" (range 100…3000 ms, step 50, readout `"{n} ms"`, key `focusFollowsMouseDelayMilliseconds`, default 250); switch "Bring the window to the front" (`focusFollowsMouseRaise`, default on); only if that is OFF: switch "Wait for the pointer to stop" (`focusFollowsMouseWaitForStop`, default on); then `MouseExceptionsList(.focusFollowsMouse)`.
6. **Smooth scrolling card** (no title; anchor `smoothScroll`).
   * Row `scroll` "Smooth scrolling" / "Turns each mouse wheel step into a short, gentle glide. The trackpad is not affected." - switch (`smoothScrollEnabled`).
   * When on (indented): slider "Scrolling speed" (20…100, step 10, readout the integer, key `smoothScrollStep`, default 40; 0/garbage → 40); `DisclosureGroup` labelled "More options" (subheadline medium; collapsed on every page visit, state not persisted) containing slider "Response" (0…100 step 5, readout `"{n}%"`, key `smoothScrollResponse`, default 65) and slider "Coast" (0…100 step 5, `"{n}%"`, key `smoothScrollCoast`, default 0), each with 6 top padding; then `MouseExceptionsList(.smoothScroll)`.
7. **Linear scrolling card** (anchor `linearScroll`): row (symbol `arrow.up.and.down.text.horizontal`) "Linear scrolling" / "Every notch of the mouse wheel scrolls the same distance, no matter how fast it spins. The trackpad is not affected." - switch (`linearScrollEnabled`). When on: slider "Lines per step" (1…10 step 1, readout integer, key `linearScrollLines`, default 3; 0 → 3) + `MouseExceptionsList(.linearScroll)`.
8. **Acceleration card** (anchor `mouseAcceleration`): row `cursorarrow.motionlines` "Disable mouse acceleration" / "Removes pointer acceleration for connected mice. Your previous setting returns when this is turned off or Trayloom quits." - switch (`mouseAccelerationDisabled`, default off). No accessibility request.
9. **Navigation card** (title "Navigation"; anchor `mouseNavigation`): row `arrowshape.turn.up.left` "Use side buttons for Back and Forward" / "Turns the mouse Back and Forward buttons into navigation commands in Finder, browsers and compatible apps." [WIN] "…in File Explorer, browsers and compatible apps." - switch (`mouseNavigationEnabled`). If on and running: badge "Side buttons active right now". If on: `MouseExceptionsList(.navigation)`.
10. **Mouse button shortcuts card** (see 1.4; title "Mouse button shortcuts"; anchor `mouseButtonShortcuts`). Shown if mouseButtonShortcuts available.
11. **Click filter card** (anchor `mouseClickDebounce`): row `cursorarrow.click.badge.clock` "Extra click filter" / "Ignores rapid extra clicks from worn mouse buttons without slowing normal clicks." - switch (`mouseClickDebounceEnabled`). When on (indented, `VStack(spacing 6)`): `Stepper` row { "Filter window" ... spacer ... `"{n} ms"` secondary monospaced } range 5…100 step 1, key `mouseClickDebounceWindowMs`, default 25 (out-of-range stored value → 25), then caption "A repeated click inside this interval is treated as an accidental duplicate."
12. **Trackpad middle click card** (title "Trackpad middle click"; anchor `middleClick`): row `hand.tap` "Three-finger click acts as middle click" / "Pressing the trackpad with three fingers works like a mouse wheel click: open links in a new tab, close tabs and everything else the middle button does." - switch (`middleClickEnabled`). When on (indented, `VStack(spacing 8)`): label "A light tap also clicks" (subheadline medium); 3 chips (height 28, radius 8, selected stroke accent 70 %; unselected NO stroke, fill secondary 6 %; each `Label(title, symbol)` 11pt medium): [`hand.raised.slash` "Off" (0)] [`hand.tap` "3 fingers" (3)] [`hand.tap` "4 fingers" (4)] (key `middleClickTapFingers`, default 0); caption "A light tap with that many fingers, without pressing, also fires the middle click. Sliding never counts. If the macOS three-finger tap is assigned to Look Up, turn it off so both do not fire together."; if the system three-finger-drag gesture is on: caption "macOS three-finger drag is turned on and uses three fingers, so click with four fingers for the middle click. To use three, turn three-finger drag off in System Settings under Accessibility, Pointer Control, Trackpad Options."; if no touch device readable: orange caption "Can’t read trackpad touches. Connect a supported trackpad."; then (outside the indent) `MouseExceptionsList(.middleClick)`. Refreshes the drag-gesture conflict on page appear.
13. **"Permission required" card** (Accessibility) when any enabled & available mouse feature needs it and permission is missing (see 0.8; not on Windows).

### 1.2 Settings table (Mouse page)

| Key | Type | Default | Allowed | Effect |
|---|---|---|---|---|
| `scrollInverterEnabled` | Bool | false | | invert vertical wheel (mouse only) |
| `scrollInverterHorizontalEnabled` | Bool | false | | invert horizontal wheel |
| `scrollHorizontalEnabled` | Bool | false | | modifier + vertical wheel → horizontal scroll |
| `scrollHorizontalModifier` | String | `shift` | shift/option/control/command (bad value → shift) | which single modifier |
| `pinchZoomEnabled` | Bool | false | | modifier + wheel → pinch/magnify gesture |
| `pinchZoomModifier` | String | `control` | none/option/control/command (bad → none) | modifier for zoom; `none` = off |
| `focusFollowsMouseEnabled` | Bool | false | | |
| `focusFollowsMouseDelayMilliseconds` (Swift `focusFollowsMouseDelay`) | Int | 250 | 100…3000, step 50 (clamped) | time pointer must stay over a window |
| `focusFollowsMouseRaise` | Bool | true | | raise as well as focus |
| `focusFollowsMouseWaitForStop` | Bool | true | | when not raising, wait for pointer to rest |
| `focusFollowsMouseExceptions` | [String] | [] | | |
| `smoothScrollEnabled` | Bool | false | | |
| `smoothScrollStep` | Int | 40 | 20…100 (0→40) | pixels per wheel tick (40 = system distance) |
| `smoothScrollResponse` | Int | 65 | 0…100 | follows the wheel sooner when higher |
| `smoothScrollCoast` | Int | 0 | 0…100 | longer landing when higher |
| `smoothScrollExceptions` | [String] | [] | | |
| `linearScrollEnabled` | Bool | false | | |
| `linearScrollLines` | Int | 3 | 1…10 (0→3) | lines per notch |
| `linearScrollExceptions` | [String] | [] | | |
| `scrollInverterExceptions` | [String] | [] | | |
| `mouseAccelerationDisabled` | Bool | false | | pointer acceleration off for mice |
| `mouseNavigationEnabled` | Bool | false | | |
| `mouseNavigationExceptions` | [String] | [] | | |
| `mouseButtonShortcutsEnabled` | Bool | false | | |
| `mouseButtonShortcuts` | [String:String] | [:] | button number → shortcut storage `"mod+mod:keyCode"` | |
| `mouseSpacesGestureEnabled` | Bool | false | | |
| `mouseSpacesGestureButton` | Int | 0 | 0 = none, 2 or 3…31 | bound button |
| `mouseSpacesGestureFollowsDrag` | Bool | false | | invert left/right |
| `mouseButtonExceptions` | [String] | [] | | |
| `mouseClickDebounceEnabled` | Bool | false | | |
| `mouseClickDebounceWindowMs` | Int | 25 | 5…100 | |
| `middleClickEnabled` | Bool | false | | |
| `middleClickTapFingers` | Int | 0 | 0, 3, 4 | |
| `middleClickExceptions` | [String] | [] | | |

Hub "panel" mirror toggles exist (`panelControlMouseScroll`, `panelControlFocusFollowsMouse`, `panelControlMouseNavigation`, `panelControlMouseButtonShortcuts`, `panelControlMouseAcceleration`, `panelControlLinearScroll`, `panelControlMouseClickDebounce`); they are covered by the Quick-panel spec.

### 1.3 Behaviour
* All wheel features share one event tap (priority order in the Swift code: horizontal-scroll modifier redirect → invert → linear cap → smooth glide; the smooth glide re-applies invert/linear itself and honours *their* exception lists, so an app excepted from "Scrolling" never scrolls backwards after the glide).
* Wheel vs. touchpad: only mouse wheels are touched (discrete events are wheels; continuous events are wheels only when they carry no gesture phase). Remote-screen events (events from another process) are left alone.
* **Invert:** negates the selected axis' line/point/fixed-point deltas.
* **Horizontal scroll:** active only while exactly the selected modifier is down (other combinations unchanged); the vertical delta moves to the horizontal axis (sign preserved, so Shift behaves as the receiver would redirect it); a wheel already reporting a horizontal axis is left alone.
* **Zoom:** the chosen modifier (not None) + wheel on either axis is turned into a magnify (pinch) gesture at the pointer location; gesture begins when modifier/pointer location changes, and ends when preferences change or the tap stops.
* **Linear:** one discrete wheel event = exactly one notch (sign only), times "Lines per step"; high-resolution fractions stay fractions; pixel distance for continuous events is capped the same way. Default 3 lines = system pace.
* **Smooth scroll engine (exact constants):** per-axis budget `remaining`; each wheel tick adds its pixel distance (tick distance = pixels × step/40; fixed-point delta wins when present; reversal replaces the old tail). Frame timer 60 Hz (`1/60`), elapsed-time based, max frame interval `1/20`. Emitted per frame = `min(|rem|, max(|rem|·(1−e^(−dt/τ)), 60/stretch·dt))` where τ = (0.16 − r·(0.16−0.04))·stretch, r = response/100; `stretch = 1 + coast·(3−1)·travelled²` (coast in 0…1, `travelled = 1 − rem/glide`); leftovers below `1.0/stretch` px flush in one frame; integer pixels are posted with a fractional carry (carry dropped on reversal; the last frame rounds). A tick within 24 px of the previous in the same window extends the glide; otherwise a new target latches. Shift held with a vertical tick scrolls sideways.
* **Focus follows mouse:** movement is sampled; after the pointer has stayed over the same window for `delay` ms, that window is focused (and raised if "Bring the window to the front"; otherwise, if "Wait for the pointer to stop" is on, focus waits until the pointer rests). Never switches to Dock/menu bar/banners/desktop (only window layers 0…3), never when the focused window is a modal/sheet blocking the target, or the target hangs from the focused window's root (popovers, suggestion lists); apps with captured pointers (games) are left alone unless a known different window is focused; a cancelled handoff restores the previous window.
* **Mouse acceleration:** sets pointer acceleration off for mice (macOS `HIDMouseAcceleration = -1`) and restores the previous value when switched off, on quit, and after a crash (recovery file/guard).
* **Navigation:** Back = button 3, Forward = button 4 (extra buttons 3/4). The click is swallowed and turned into the app's Back/Forward menu command (looked up in the frontmost app's menu through Accessibility, matching `⌘[` / `⌘]` or the layout-moved shortcut); if the app has neither command it gets the raw click back; apps that handle the buttons themselves are passed through (virtualization/remote-desktop clients, `org.mozilla.*`, apps that are default web handlers). A button owned by "Mouse button shortcuts"/Spaces drag or the radial menu is not touched.
* **Click filter:** only buttons left, right, middle. After an accepted Up, a Down within the window is suppressed together with its Up and any drags; repeated Downs while held are noise; an Up of an accepted Down is never suppressed; state resets on time going backwards.
* **Middle click (trackpad):** a *physical* press with exactly 3 fingers (4 while system 3-finger-drag is on), fingers resting ≥ 0.04 s, contact frame fresh ≤ 0.25 s, becomes a middle click; a second synthesized click within 0.30 s is swallowed. Optional light tap (3 or 4 fingers, duration ≤ 0.35 s, travel ≤ 0.03 (normalised), spread change ≤ 0.04, no press, no keystroke in the last 0.5 s; 3-finger tap disabled when system drag is on).
* Settings change → immediate resync; features only hold hooks/timers while on and available.

### 1.4 Mouse button shortcuts card (`MouseButtonSettings.swift`)
Title "Mouse button shortcuts". Top to bottom:
1. Row `computermouse.fill` "Use extra buttons as shortcuts" / "Each extra button or side-wheel direction can press a key combination for you. While it has a shortcut, it stops doing what it did before." - switch (`mouseButtonShortcutsEnabled`). Turning off cancels any capture; turning on requests Accessibility.
2. When on (indented 38, `VStack(spacing 10)`):
   * empty caption (if no mappings and no pending row): "No shortcuts yet. Add a button or side-wheel direction."
   * one mapping row per button, sorted by button number ascending (side-wheel inputs are negative so they sort first): `VStack(spacing 5)`: `HStack(spacing 8)` { button name, spacer, recorder 108 wide (empty state label "Set shortcut" until a combination exists; recording label "Press keys"), remove button `xmark.circle.fill` (22x22, tooltip/label "Remove") }; under it: orange error caption (from the recorder) or, while recording, "Press the new shortcut Escape cancels."; and if the button is claimed by the radial menu and a shortcut exists: orange "This button opens the radial menu now, so the shortcut waits."
   * pending row (button captured but no combination yet; nothing persisted until the combination lands; Remove just drops it).
   * capture row: when idle a button `Label("Add a button or side wheel", "plus")`; when capturing: `HStack` { running ? (`circle.dashed` secondary + "Now press an extra button or move the side wheel.") : (`exclamationmark.circle.fill` orange + "Trayloom cannot watch the mouse right now.") , spacer, "Cancel" button }, optional orange feedback line, and caption "If nothing happens, your mouse’s own software may already be using that control."
   * Feedback strings: unsupported → "That input cannot take a shortcut. Use an extra button or a side-wheel direction."; claimed by radial menu → "That button already opens the radial menu. Pick another one, or free it there first."; already listed (or equals the Spaces button) → "That button or direction is already on the list below."
3. `Divider`.
4. Row `rectangle.3.group` "Switch Spaces by dragging a button" / "Hold the chosen button and drag: left or right moves one Space over, up opens Mission Control, down opens App Exposé. A short click still does what it always did." - switch (`mouseSpacesGestureEnabled`); turning off clears the bound button (`mouseSpacesGestureButton = 0`).
5. When Spaces switch on (indented, spacing 10): the bound-button row (idle: button `Label("Choose a button","plus")`; capturing: dashed circle + "Now press the middle button or an extra button." (or the blind message), Cancel, orange feedback; bound: name + `xmark.circle.fill` remove); toggle row "Spaces follow the drag" / "Dragging right brings the Space on the left, the way a trackpad swipe carries it along with your fingers." (`mouseSpacesGestureFollowsDrag`); orange note when none of the 4 system shortcuts is enabled: "The Mission Control keyboard shortcuts are switched off in System Settings, so this gesture has nothing to ask for." Spaces capture feedback: unsupported → "That input cannot be held for a drag. Use the middle button or an extra button."; exists → "That button already has a shortcut. Pick another one."; claimed by radial → the radial message above.
6. If (shortcuts || Spaces) on: `MouseExceptionsList(.buttonShortcuts)`.
On page disappear any capture stops.
Button names: 2 = "Middle button", 3 = "Back side button", 4 = "Forward side button", ≥5 = "Button %d" with number+1 (button 5 prints "Button 6"), -2 = "Side wheel left", -1 = "Side wheel right". Mappable inputs: buttons 3…31 and the two side-wheel directions (middle button is *not* mappable for shortcuts; it can only be bound for the Spaces drag; left/right never). Side wheel: horizontal wheel movement where |horizontal| > |vertical|, positive horizontal delta = left; a wheel burst fires once per direction (250 ms quiet gap starts a new burst). Spaces drag tracker: horizontal step 220 px per Space change, vertical step 150 px opens an overview, 0.35 s cooldown between Space changes, banked travel capped at one step, axis locks on first firing, a press that never fired stays a click; "Spaces follow the drag" swaps left/right only; up = Mission Control, down = App Exposé.
Extra strings (panel): `manageButton` "Set up…", `panelCaption` "Extra buttons and side-wheel directions press key combinations you choose.", hub description "Extra buttons and side-wheel directions press a key combination you choose."
Persistence: `mouseButtonShortcuts` = dictionary `{"3": "control+option:123", "-1": "command:12"}` (unparseable entries dropped on load).

### 1.5 Windows mapping (Mouse page)

| Feature | Windows implementation |
|---|---|
| Wheel pipeline (invert / sideways / linear / smooth) | One `WH_MOUSE_LL` handler for `WM_MOUSEWHEEL` (0x020A) / `WM_MOUSEHWHEEL` (0x020E), swallow and re-inject with `SendInput` (`MOUSEEVENTF_WHEEL`/`HWHEEL`, tag via `dwExtraInfo` = Trayloom magic so own events are skipped). Mouse vs touchpad: wheel events from Precision Touchpads arrive as small non-multiple-of-120 deltas via the same hook; distinguish with `GetCurrentInputMessageSource` / `LLMHF_INJECTED` (touchpad inertia is injected by the OS, `flags & LLMHF_INJECTED`) and treat deltas that are exact multiples of 120 as wheel notches. Windows "natural scrolling" for touchpad stays untouched. Smooth scroll: output 1-notch = 120 units split into sub-notch `WHEEL_DELTA/n` events at 60 Hz (`DispatcherTimer`/`timeBeginPeriod(1)`), same engine as above (pixels↔units: 40 px per notch ≙ 120). |
| Scroll sideways with a key | In the same hook: modifier state via `GetAsyncKeyState`; when exactly the chosen modifier (Shift/Alt/Ctrl/Win) is down, swallow the vertical wheel and inject `MOUSEEVENTF_HWHEEL` (positive = right). Many Windows apps already do Shift+wheel; the feature still helps Alt/Ctrl. Win key chords cause Start menu on release - inject a dummy key (VK 0xE8) before the release. |
| Zoom ("Pinch Zoom") | Windows already maps Precision Touchpad pinch to Ctrl+wheel. Implement the *modifier + wheel → Ctrl+wheel* part: swallow the wheel while the chosen modifier is down and inject Ctrl down, wheel, Ctrl up (`KEYEVENTF` with the magic tag). Options become None / Alt / Ctrl / Win [WIN] ("⌥ Option"→"Alt", "⌃ Control"→"Ctrl", "⌘ Command"→"Win"); the default `control` is then a no-op on Windows (Ctrl+wheel already zooms): either keep default `control` (harmless) or ship default `none`. Note text [WIN]: "Ctrl + scroll performs app zoom." and the "instead of macOS screen zoom" clause is dropped (Windows Magnifier is Win+wheel). |
| Focus follows mouse | Low-level mouse hook records `pt`; a 10-20 ms timer evaluates `WindowFromPoint`→`GA_ROOT`; focus with `SetForegroundWindow` (needs the foreground-lock workaround: `AllowSetForegroundWindow(ASFW_ANY)`, or inject a no-op Alt key press (`keybd_event(VK_MENU)` tap) / `AttachThreadInput` to the foreground thread). Raise-less focus: use `SetFocus`/`SwitchToThisWindow` is not possible without raising; Windows has a native equivalent `SPI_SETACTIVEWINDOWTRACKING` (hover to activate, `SPI_SETACTIVEWNDTRKTIMEOUT` = delay, `SPI_SETACTIVEWNDTRKZORDER` = raise) which already implements "Bring the window to the front" and the delay (recommended: drive these three SPI values and restore on stop, or implement own timer for exact parity incl. exceptions and the "wait for pointer to stop"). Skip desktop/taskbar (`Progman`, `WorkerW`, `Shell_TrayWnd`, `Shell_SecondaryTrayWnd`), the start menu/search/flyout (`Windows.UI.Core.CoreWindow`, `XamlExplorerHostIslandWindow`), cloaked/tool windows, and Trayloom's own overlays. Full-screen exclusive/games: skip when the foreground window rect equals its monitor rect and the target differs (Trayloom already has `skipFullscreen`). |
| Mouse acceleration | `SystemParametersInfo(SPI_GETMOUSE/SPI_SETMOUSE, [0,0,0])` (threshold1, threshold2, accel). Save the old triple first (and a recovery copy in `%APPDATA%\Trayloom`), restore on disable/exit/crash (`SPIF_SENDCHANGE`, do not persist `SPIF_UPDATEINIFILE`). Windows has no per-device split; applies to all pointing devices, say so in the caption if kept verbatim: "…for connected mice" is acceptable. |
| Side buttons Back/Forward | `WH_MOUSE_LL` `WM_XBUTTONDOWN/UP` (`HIWORD(mouseData)` = XBUTTON1 back / XBUTTON2 forward). Windows already sends `WM_APPCOMMAND` (APPCOMMAND_BROWSER_BACKWARD/FORWARD) to browsers/Explorer for these buttons; to "turn them into navigation commands" swallow the click and send `WM_APPCOMMAND` (lParam `MAKELPARAM(0, APPCOMMAND_BROWSER_BACKWARD)`) to the window under the pointer (`SendMessage` to the root; fall back to `Alt+Left`/`Alt+Right` via SendInput). Pass-through list: VM/remote clients (mstsc, VMware, Parallels, Hyper-V vmconnect, Moonlight), Firefox. |
| Mouse button shortcuts | Same hook; inputs: XBUTTON1 (3), XBUTTON2 (4), horizontal wheel left/right (negative ids). Windows exposes only buttons 1-5 to `WH_MOUSE_LL` (buttons ≥6 need Raw Input `RIDEV_INPUTSINK` on `RAWMOUSE.usButtonFlags` for 6+ - only `RI_MOUSE_BUTTON_4/5`; HID-level extra buttons are not visible), so "Button %d" naming applies only when raw-HID capture is added later. Middle button remains non-mappable (matches Swift). Shortcut emitted with `SendInput` chord. Radial menu ownership check: keep (the radial menu feature is Tools). |
| Spaces drag → virtual desktops | Closest Windows equivalent of the Spaces/Mission Control drag: hold the bound button and drag: left/right = switch virtual desktop (inject `Ctrl+Win+Left/Right`; there is no public API to switch desktops, `IVirtualDesktopManager` only tells `IsWindowOnCurrentVirtualDesktop` / `MoveWindowToDesktop`), up = Task View (`Win+Tab`), down = Show desktop (`Win+D`) or "App Exposé" has no Windows twin (propose Win+D, flag as Trayloom decision). Constants identical (220 / 150 px, 0.35 s cooldown). Strings [WIN]: "Switch desktops by dragging a button" / "Hold the chosen button and drag: left or right moves one desktop over, up opens Task View, down shows the desktop. A short click still does what it always did." Drop the "Mission Control keyboard shortcuts are switched off" note (the Win+Ctrl+Arrows always exist). Rename "Spaces follow the drag" → "Desktops follow the drag" [WIN]. |
| Click filter | `WH_MOUSE_LL` with `MSLLHOOKSTRUCT.time`; implement `MouseClickDebounceState` exactly (buttons left/right/middle only; ignore events with magic tag). Range 5…100, default 25. |
| Middle click (touchpad) | **Closest equivalent.** macOS reads raw multitouch frames (private MultitouchSupport). Windows Precision Touchpads expose HID digitizer reports: `RegisterRawInputDevices` for usage page 0x0D / usage 0x05 (Touch Pad) with `RIDEV_INPUTSINK`; in `WM_INPUT` parse the report with `HidP_GetCaps`/`HidP_GetUsageValue` ("Contact Count" 0x54, per-finger Tip Switch 0x42, X/Y 0x30/0x31) and the button usage (page 0x09) for the physical press. Algorithm = the Swift one: when a *physical click* arrives (left `WM_LBUTTONDOWN` from the hook within 0.25 s of a frame with exactly 3 (or 4) contacts and settle ≥ 40 ms), swallow it and inject a middle button down/up; swallow bounce within 0.30 s. Tap detection identical to the constants above. The OS also has native "three/four-finger tap → Middle mouse button" (Settings > Bluetooth & devices > Touchpad > Three- and four-finger gestures > Taps; registry `HKCU\Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad\ThreeFingerTapEnabled`/`FourFingerTapEnabled`, value 3 = middle mouse button - verify per build): the "light tap" option can simply drive those registry values (and restore), which is far cheaper than raw input. Conflict notes [WIN]: replace the three-finger-drag/Look Up wording with: "Windows three-finger gestures are assigned to something else. Turn them off in Settings > Bluetooth & devices > Touchpad." No touchpad found → "Can’t read touchpad touches. Connect a Precision Touchpad." (detect via `GetRawInputDeviceList` HID usage 0x0D/0x05). Mark as high effort; do last. |
| Exceptions | see 0.10 |
| Elevated windows | UIPI: hooks do not see wheel/button events destined to elevated windows and injected input is dropped; document in the page footer instead of the Accessibility card. |

### 1.6 Gap vs Trayloom (Mouse page)
Existing Trayloom features are separate hub entries, each with an auto-built settings view (`SettingsViewBuilder`), not one page of cards. Concrete differences:
1. **No page/legend/cards**: Vorssaint shows one scrollable "Mouse & Trackpad" page with a legend and 11 cards. Trayloom shows one small settings view per feature (`InvertScrollFeature`, `HorizontalScrollFeature`, `SmoothScrollFeature`, `LinearScrollFeature`, `FocusFollowsMouseFeature`, `MouseAccelerationFeature`, `MouseButtonFeatures` (`side-buttons`, `mouse-button-shortcuts`), `DebounceFeatures` (`click-debounce`)). Build the card page and the legend (needs section anchors/highlight).
2. **Zoom (Pinch Zoom): missing entirely** (`grep -i zoom` finds nothing in Input).
3. **Middle click: missing entirely.**
4. **Invert scroll**: Trayloom has vertical (default **true**, Vorssaint false), horizontal, and an extra "Leave touchpads and remote-control input alone" toggle (not in Vorssaint; keep as hidden always-on). No "active now" badge, no trackpad note, no exceptions.
5. **Horizontal scroll**: Trayloom choice list "Modifier key" + extra "Reverse direction" (not in Vorssaint). Needs the 4 keycap chips (Shift/Alt/Ctrl/Win) with default Shift and the caption text.
6. **Smooth scroll**: Trayloom has "Animation length (ms)" 60…900 (260) and "Distance per notch (%)" 25…400 (100). Vorssaint has Scrolling speed (20…100, 40), Response (0…100, 65), Coast (0…100, 0) in a "More options" disclosure, exceptions list and the specific engine (exp. decay). Replace the engine and controls.
7. **Linear scroll**: Trayloom has "Count every wheel event as one notch" / "Combine fine wheel steps into notches" / "Distance per notch (%)". Vorssaint: single slider "Lines per step" 1…10 (3). Replace; keep the notch normalisation as internal behaviour.
8. **Focus follows mouse**: Trayloom has delay 50…1500 (250) + skipFullscreen. Vorssaint: delay 100…3000 step 50 (250), "Bring the window to the front" (true), "Wait for the pointer to stop" (true, only when not raising), exceptions, hover-in-window timing semantics.
9. **Mouse acceleration**: matches in intent (`MouseAccelerationFeature.cs`); it is a standalone feature with a note instead of a titled switch row; add the Vorssaint row/caption text.
10. **Side buttons**: Trayloom offers per-button presets/custom shortcuts (Back/Forward/Custom). Vorssaint "Navigation": a single switch "Use side buttons for Back and Forward" + exceptions + active badge. Per-button presets are extra; hide or move into Mouse button shortcuts.
11. **Mouse button shortcuts**: Trayloom has three fixed `AddHotkey` fields (middle, XButton1, XButton2). Vorssaint: dynamic list, press-to-capture flow, side-wheel directions, Spaces/desktop drag, radial-menu ownership, remove/pending rows; middle is not mappable. Rebuild per 1.4.
12. **Click filter**: Trayloom window 5…250 default 40 and per-button toggles (left/right/middle/xbuttons). Vorssaint: 5…100, default 25, fixed buttons left/right/middle, Stepper row + caption.
13. **Exceptions lists**: none implemented (0.10).
14. **Hook order**: Trayloom's `HookPriorities` (click-debounce 500 → shortcuts 300 → side buttons 200) already mirrors "button owned by another feature is not touched"; keep.

---

## 2. Page "Switcher" (`SwitcherSettings.swift`, `SwitcherAppRulesList.swift`, `WindowPreviewsCard`, `WindowPreviewExclusionsList.swift`)

Sidebar entry: `tabSwitcher` = "Switcher", symbol `rectangle.on.rectangle` (Fluent: `U+E8A7` OpenInNewWindow or `U+E7C4` TaskView). Description (`switcherDescription`): "Switch between apps and windows your way."
Hub entry: title `switcherSection` = "Window switcher", description "Switch apps and windows with previews". Installed by default (`switcherEnabled` default true).

### 2.1 Layout (top to bottom)
1. Header "Switcher" / description.
2. **Switcher card**, title "Window switcher" (anchor `switcher`; shown if the switcher feature is available):
   1. Row `rectangle.on.rectangle` "Use the Trayloom switcher" / "Switch between apps and windows, including minimized windows and multiple windows from the same app." - switch (`switcherEnabled`, default **true**). Everything below in this card is `.disabled` while it is off.
   2. **Layout chooser**: label "Layout" (`NotchEditorStrings.layout`, subheadline medium) then `HStack(spacing 10)` of 3 equal choice cards (single select). Card = `VStack(alignment leading, spacing 8)`: sketch box (full width, height 64, fill black 82 %, radius 10) · title (12 semibold, accent when selected) · caption (`.caption` secondary, wraps); card padding 10; fill accent 10 % (selected) or secondary 5 %; radius 12; selected stroke accent 55 % 1 px. Cards:
      * **Window previews** — "One preview per window, minimized ones included." Sketch: 3 mini windows 34x26 spacing 6 (white 40 % fill r3, title strip 4 px black 35 %, first one white 90 % with 1.5 px accent stroke).
      * **Large icons** — "Shows one icon per app with that app’s window previews above it." Sketch: 4 columns spacing 10; each = a 26x18 mini window (only the first is visible, others opacity 0) above a 16x16 rounded icon (first accent, rest white 35 %).
      * **Simple list** — "Shows app icons and window titles, without previews or screen capture by the switcher." Sketch: white 90 % capsule 44x4 above a row of 4 icons 16x16 spacing 8 (first accent).
      Selection logic: `switcherSimpleMode` wins over `switcherIconRowMode`; windows → both false; icons → icon-row true, simple false; simple → simple true. Every change calls `AppSwitcher.syncWithPreferences()`.
   3. `Divider`.
   4. `ShortcutPreferenceRow(role: .switcher)` label "Apps", icon `app.dashed`. Default ⌘Tab (storage `command:48`). Key `switcherShortcut`.
   5. `ShortcutPreferenceRow(role: .switcherWindow)` label "Windows", icon `macwindow.on.rectangle`. Default ⌘` (keyCode 50). Key `switcherWindowShortcut`.
   6. Caption: "Opens a switcher for the frontmost app’s windows. While the switcher is open, jumps between the selected app’s windows."
   7. Row `macwindow.on.rectangle` "All windows" / "Use a separate shortcut to cycle through windows from all apps in any layout. Your window filters still apply." - switch (`switcherAllWindowsShortcutEnabled`, default false). (Strings `SwitcherShortcutStrings.allWindows`, `.allWindowsCaption`.)
   8. If that switch is on: `ShortcutPreferenceRow(role: .switcherAllWindows)` (no label/icon; default ⌥Tab, key `switcherAllWindowsShortcut`).
   9. Row `command` "Replace macOS ⌘Tab and ⌘`" / "Disables the matching macOS app and window shortcuts only while Trayloom’s switcher is active. All running apps stay reachable." - switch (`switcherTakeOverSystemShortcuts`, default false).
   10. If take-over is effective (switcher engaged AND take-over on AND Accessibility granted) and secure input is held: `SecureInputRow(blocking: .switcher)` indented 38 (orange `exclamationmark.circle.fill` + "Secure input is on" + caption + optional button "Show %@"): "%@ was the active app when it turned on and may be holding it. Until it turns off, macOS hides your keystrokes from the switcher and ⌘Tab opens the macOS switcher instead. Dismiss its password field, or quit it, to release it." / "The app holding it cannot be named. Until it turns off, macOS hides your keystrokes from the switcher and ⌘Tab opens the macOS switcher instead. Dismiss any open password field, or log out and back in, to clear it." / "The app holding it could not be identified. Until it turns off, macOS hides your keystrokes from the switcher and ⌘Tab opens the macOS switcher instead." (macOS-only; see mapping).
   11. Caption (usage hint): "Hold %@ to navigate; release to activate the window. Shift or ← goes back; W closes the window; Q quits the app; Esc cancels." with `%@` = the Apps shortcut display string (e.g. "⌘Tab") [WIN] "Alt+Tab".
3. **Options card**, title "Options" (`keepAwakeOptions`); whole card disabled while the switcher is off (except the app-rules list, which is forced enabled "as before the redesign"):
   * `timer` "Appearance delay" / "How long the shortcut must be held before the switcher appears." - slider 140 wide, 0…500 step 25, readout `"{n} ms"` (caption monospaced, secondary, width 56 trailing). Key `switcherAppearanceDelay`, default 100.
   * `square.grid.3x2` "Maximum columns" / "Wraps onto more rows after this many items. Useful on ultrawide displays." - slider 140 wide from (min−1)=1 to 10 step 1 (the leftmost step = Auto); readout "Auto" or the number. Key `switcherMaxColumns`, default 0 (=Auto), valid 2…10.
   * `cursorarrow.rays` "Instant selection" / "Moves the highlight and scroll position immediately as you browse apps and windows." - switch `switcherInstantSelection` false.
   * `magnifyingglass` "Pin search with S" / "S starts a search and pins the switcher open, so typing no longer produces special characters when your shortcut uses ⌥, and a search starting with Q or W no longer closes the window or quits the app by mistake." - switch `switcherSearchPinEnabled` false.
   * [only when layout is Simple list or Large icons] `keyboard` "Show shortcut hints" / "Shows the app and window shortcuts below the icons." - switch `switcherShowShortcutHints` true.
   * `square.stack` "Show one entry per app" / "Collapses all of an app’s windows into one entry in the switcher, instead of one entry per window." - switch `switcherMergeTabs` false.
   * `arrow.down.right.and.arrow.up.left` "Show fullscreen windows" (no caption) - switch `switcherShowFullscreenWindows` true.
   * `Divider`.
   * chip row (icon `arrow.down.to.line`, title "Minimized windows", no caption; chips wrap, indented 38): "Normal ordering" (`normal`) · "Place at end" (`end`) · "Hide" (`hidden`). Key `switcherMinimizedPlacement`, default `normal`.
   * if placement ≠ normal: `eye.slash` "Treat hidden apps like minimized windows" - switch `switcherTreatHiddenAppsLikeMinimized` true.
   * chip row (icon `display.2`, title "Show on", caption "Which display the switcher opens on when more than one is connected."): "Display with the pointer" (`pointer`, default) · "Display with the menu bar" (`menuBar`) · "Display with the active window" (`activeWindow`). Key `switcherScreenPlacement`.
   * `display` "Show only the current display" / "Lists only windows on the display under the pointer. If that display has no windows, the switcher does not open." - switch `switcherCurrentDisplayOnly` false.
   * `rectangle.3.group` "Show only the current desktop" / "Lists windows from the current desktop. Fullscreen windows from other desktops also appear while “Show fullscreen windows” is on." - switch `switcherCurrentSpaceOnly` false.
   * `Divider`.
   * chip row (icon `app.dashed`, title "Apps with no open window", caption "Chooses which running apps with no window at all show up in the switcher."): "Do not show" (`off`) · "Finder only" (`finder`, default) · "All apps" (`all`). Key `switcherWindowlessApps` (legacy `switcherShowWindowlessFinder` true migrates to finder/off). The row is disabled while take-over is on, the displayed selection is then forced to "All apps", and the caption "All running apps stay reachable while “Replace macOS ⌘Tab and ⌘`” is on. Turn it off to choose." appears below (format `switcherWindowlessAppsLockedFormat`, `%@` = the take-over title).
   * `SwitcherAppRulesList` (see 2.2).
4. **Window thumbnails card** (`WindowPreviewsCard`, title "Window thumbnails"; keys: `switcherPreviewSize` + `switcherPreviewExcludedApps` on this page; the Dock page uses `previewSize` + `windowPreviewExcludedApps`; `minimalWindowPreviews` is shared): see 2.3.
5. Permission cards (macOS only): "Permission required" Accessibility when the switcher is engaged and permission missing; Screen Recording row (card without title) when engaged, missing, and the layout captures previews (any layout except Simple list).

### 2.2 `SwitcherAppRulesList` ("Rules by app")
`AppBundleList(title: "Rules by app", addTitle: "Add an app…", removeLabel: "Remove", reachesEveryApp: true, acceptsExecutables: false)`; caption "Choose how each app appears. Apps without a rule use the choice above." Each row has a trailing accessory: menu `Picker` (label hidden, accessibility "Switcher behavior") with 3 items: "Show without windows" (`showWithoutWindows`, rule assigned to a newly added app) · "Windows only" (`windowsOnly`, display fallback when missing) · "Never show" (`hidden`). Persistence: `switcherAppRules` = `{identity: rawValue}` dictionary (default `{}`; empty/whitespace identities and unknown values are dropped on load). Rules override the "Apps with no open window" choice per app.

### 2.3 `WindowPreviewsCard` and `WindowPreviewExclusionsList`
Title "Window thumbnails" (`WindowPreviewExclusionStrings.sectionTitle`).
* Label "Preview size" (subheadline medium); 4 tiles `HStack(spacing 10)`, equal width: each tile = `VStack(spacing 8)`: a mini thumbnail (rounded rect r4, `60·scale x 42·scale`, vertically centred in a 42-high slot, with a 4-px title strip inset 3 at the top) + label (11 medium, 1 line), tile padding 8, radius 12, fill accent 10 % / secondary 5 %, selected stroke accent 55 %. Tiles: "Small" (scale 0.5, value `small`), "Normal" (0.66, `normal`, default), "Large" (0.83, `large`), "Extra large" (1.0, `xlarge`). Selecting calls `syncWithPreferences`.
* Row `rectangle.dashed` "Minimal previews" / "Hide titles, buttons and decorative details in Dock and switcher previews. The selection stays visible." - switch `minimalWindowPreviews` (false).
* `WindowPreviewExclusionsList(key)`: AppBundleList title "Pause in these apps", add "Add an app…", remove "Remove", caption "Window thumbnails stop while one of these apps is in front.", reachesEveryApp true, acceptsExecutables true. Stored as sanitized (trimmed, de-duplicated) `[String]`, default `[]`.

### 2.4 Settings table
| Key | Type | Default | Notes |
|---|---|---|---|
| `switcherEnabled` | Bool | true | |
| `switcherShortcut` | String | `command:48` (⌘Tab) | storage `mods:keyCode` |
| `switcherWindowShortcut` | String | ⌘` (`command:50`) | |
| `switcherAllWindowsShortcut` | String | ⌥Tab (`option:48`) | |
| `switcherAllWindowsShortcutEnabled` | Bool | false | |
| `switcherTakeOverSystemShortcuts` | Bool | false | suppresses macOS symbolic hotkeys 1 (⌘Tab), 2 (⇧⌘Tab), 27 (⌘`), 220 (⇧⌘`) while the tap is live |
| `switcherIconRowMode` | Bool | false | Large icons |
| `switcherSimpleMode` | Bool | false | Simple list |
| `switcherMergeTabs` | Bool | false | |
| `switcherWindowlessApps` | String | `finder` | off/finder/all; forced `all` while take-over is on |
| `switcherMinimizedPlacement` | String | `normal` | normal/end/hidden |
| `switcherTreatHiddenAppsLikeMinimized` | Bool | true | |
| `switcherShowFullscreenWindows` | Bool | true | |
| `switcherAppRules` | [String:String] | {} | |
| `switcherCurrentSpaceOnly` | Bool | false | |
| `switcherCurrentDisplayOnly` | Bool | false | |
| `switcherScreenPlacement` | String | `pointer` | pointer/menuBar/activeWindow |
| `switcherSearchPinEnabled` | Bool | false | |
| `switcherShowShortcutHints` | Bool | true | |
| `switcherAppearanceDelay` | Int | 100 | 0…500 |
| `switcherInstantSelection` | Bool | false | |
| `switcherMaxColumns` | Int | 0 | 0 = Auto, 2…10 |
| `minimalWindowPreviews` | Bool | false | shared with Dock |
| `switcherPreviewSize` | String | `normal` | small/normal/large/xlarge |
| `switcherPreviewExcludedApps` | [String] | [] | |

### 2.5 Behaviour
* The engine (not detailed on this page): shortcut held → after `appearanceDelay` the panel appears; releasing activates the selection; Shift/← backwards; W closes the window; Q quits the app; Esc cancels; S pins a search field. Window order = most-recently-used (WindowUseTracker); with Minimized placement `end` minimized windows go last, `hidden` removes them; with "Treat hidden apps like minimized windows" hidden-app windows follow the same rule.
* Permissions: the tap needs Accessibility; previews need Screen Recording. With take-over off and no Accessibility the macOS switcher still answers.
* "Secure input" (password field focus) blinds the key tap; the row explains why the macOS switcher took over. Polled only while this page is visible and take-over is effective.
* Layout switch persists immediately; options card greys when the switcher is off; per-app rules stay editable.
* Apps with no open window: Finder (always running) shows an entry that opens a Finder window.

### 2.6 Windows mapping
| macOS | Windows |
|---|---|
| ⌘Tab / ⌘` / ⇧ variants & take-over (symbolic hotkeys via CGS) | `WH_KEYBOARD_LL`: when "Replace Windows Alt+Tab and Alt+`" [WIN title: "Replace Windows Alt+Tab and Win+Tab"] is on, swallow Alt+Tab (and Shift+Alt+Tab, Win+Tab if configured) key-downs *and* the matching Alt key-up bookkeeping so the native Task Switching UI never opens (it cannot be disabled by `RegisterHotKey`; the hook must see the Tab key-down before the shell: set `LLKHF_ALTDOWN` check, return 1). With take-over off the user must pick a shortcut Windows does not own, e.g. Trayloom today uses Alt+` via `RegisterHotKey`. Proposed Windows defaults: Apps = Alt+Tab with take-over default ON (deliberate divergence - otherwise two switchers open); Windows (frontmost app's windows) = Alt+`; All windows = Ctrl+Alt+` [WIN]. |
| Window list | `EnumWindows` + visibility/cloak filters (`IsWindowVisible`, `DWMWA_CLOAKED`, `WS_EX_TOOLWINDOW`, owner==null, non-empty title) as in `WindowEnumerator.cs`; group by process/AUMID for per-app grouping; MRU via `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`. Minimized = `IsIconic`. |
| Previews | DWM thumbnails (`DwmRegisterThumbnail`) rendered into the switcher panel (live, no capture permission); minimized windows have no live DWM content, so use a cached `PrintWindow(PW_RENDERFULLCONTENT)` bitmap taken while the window was last visible (or `Windows.Graphics.Capture` for the last shown). The Simple list layout needs no thumbnails. "Screen Recording" permission row does not exist. |
| Current desktop only | `IVirtualDesktopManager.IsWindowOnCurrentVirtualDesktop(hwnd)` (COM `CLSID_VirtualDesktopManager {AA509086-5CA9-4C25-8F95-589D3C07B48A}`) [WIN caption: "Lists windows from the current virtual desktop."; the "Fullscreen windows from other desktops also appear…" sentence is macOS-only (fullscreen apps get their own Space) - drop it]. "Other desktop" tag string `switcherOtherDesktop` = "Other desktop". |
| Show fullscreen windows | Windows covering their whole monitor with no caption (borderless/fullscreen video/games): include/exclude. |
| Treat hidden apps like minimized | macOS "Hide app" (⌘H) has no Windows equivalent. Closest: windows that are cloaked/suspended UWP or tray-minimized. Recommend omitting the row on Windows (keep the key for settings parity, default true). |
| Apps with no open window | "Finder only" [WIN] → "File Explorer only" (explorer.exe with no folder window); "All apps" → processes that own a tray icon or have a main window handle that is hidden. Windows apps rarely sit windowless; low value but keep the control. |
| Screen placement | pointer = `MonitorFromPoint(GetCursorPos)`; "Display with the menu bar" [WIN] "Primary display" (taskbar display: `MonitorFromWindow(Shell_TrayWnd)`); active window = `MonitorFromWindow(GetForegroundWindow)`. |
| Secure input row | No equivalent: Windows has no secure input mode; password fields do not block hooks. The nearest blocker is the secure desktop (UAC prompt, lock screen), where hooks are not called at all. Omit the row; keep the usage hint caption. |
| Permission cards | none (0.8). |

### 2.7 Gap vs Trayloom (`Trayloom.Features.Windows/AppSwitcherFeature.cs`, `SwitcherWindow.cs`, `WindowEnumerator.cs`)
* Trayloom is one feature "App switcher" with: hotkey (default Alt+`), "Release to switch", "Include minimized windows", "Only windows on the current monitor", plus a note. Everything else on this page is missing: layout chooser (Window previews / Large icons / Simple list; Trayloom has one overlay), separate Apps / Windows / All windows shortcuts, take-over of Alt+Tab, appearance delay, max columns, instant selection, search pin, shortcut hints, one-entry-per-app, fullscreen filter, minimized placement (normal/end/hidden), hidden-app handling, screen placement choice, current-desktop-only (Trayloom says "Windows on other virtual desktops are not listed" - fixed behaviour, not a switch), windowless apps choice, per-app rules list, thumbnail size, minimal previews, thumbnail pause list.
* "Release to switch" (Trayloom) is always-on behaviour in Vorssaint (hold = navigate, release = activate); remove the toggle.
* "Include minimized windows" ≈ Minimized windows chip row (normal/end/hidden).
* "Only windows on the current monitor" ≈ "Show only the current display".
* Defaults differ: Vorssaint's switcher is installed/on by default; Trayloom requires installing.

---

## 3. Page "Dock" (`DockSettings.swift`)

Sidebar entry: `dockTitle` = "Dock", symbol `dock.rectangle` (Fluent: `U+E8A1`? use Taskbar glyph `U+E7C4`/`U+EDA2`; look up "Dock"/"Taskbar"). [WIN] page title "Taskbar" (Dock → taskbar; flagged) with description "See an app’s windows from its taskbar button, and choose what a click on it does." Original description: "See an app’s windows from its Dock icon, and choose what a click on it does."

### 3.1 Layout
1. Header (title/description above).
2. **Dock Preview card**, title "Dock Preview" (`dockPreviewName`; anchor `dock`; shown if dockPreview available):
   * Row `dock.rectangle` "Preview windows from the Dock" - caption (dynamic): off → "Hover over an open app in the Dock to see its windows, then click the one you want."; on and Accessibility missing → "Permission required: Accessibility"; on and Screen Recording missing → "Permission required: Screen Recording"; on and service blocked with reason dockUnavailable → "Could not read Dock items."; otherwise the "Hover over…" text. Switch `dockPreviewEnabled` (false).
   * When on, rows in this order:
     * `rectangle.3.group` "Show only the current desktop" / "When off, shows windows from all desktops. Choosing a window on another desktop takes you there." - switch `dockPreviewCurrentSpaceOnly` (false).
     * `timer` "Open delay" / "How long the pointer has to rest on an icon before its panel opens." - number field (width 64, rounded border, integer, formatter bounded 200…900, no grouping) + Stepper (step 50, same range, label hidden) + secondary "ms". Key `dockPreviewOpenDelay`, default **200**, range 200…900 (out of range clamps).
     * `circle.lefthalf.filled` "Panel background" - caption "Turn it down to see more of what sits behind the panel." (or, when Liquid Glass is drawn - `liquidGlassEnabled` true and Reduce Transparency off - "Liquid Glass is on, so the panel’s transparency is set in System Settings > Appearance.") - slider 140 wide 0.4…1.0 step 0.05 + readout `"{n}%"` (caption monospaced, width 44 trailing); slider disabled when glass is drawn. Key `dockPreviewBackgroundOpacity` default 1.0.
     * `xmark.circle` "Quit the app with the × button" / "In Dock Preview, × quits the whole app instead of closing only that window." - switch `dockPreviewQuitAppOnClose` (false).
     * `DisclosureGroup` labelled "More options" (subheadline medium; collapsed each visit) containing: `dock.rectangle` "Keep Dock visible (experimental)" / "Pauses auto-hide while the preview is open and restores it when you leave. May resize windows. If the app is interrupted, reopen it to restore the Dock." - switch `dockPreviewKeepDockVisible` (false; disabled and off if the system cannot support the hold, i.e. `!DockAutohideHold.isSupported`); and `clock.arrow.circlepath` "Order windows by creation time" / "Show older windows first instead of the ones you used most recently." - switch `dockPreviewOrderByCreation` (false). First row has 6 top padding.
3. **Dock clicks card**, title "Dock clicks" (`titleDockClick`; anchor `dockClick`; shown if dockClick available):
   * `dock.arrow.down.rectangle` "Click the Dock icon to minimize" / "The active app’s windows minimize when you click its Dock icon. Click again to bring them back." - switch `dockClickMinimize` (false). Turning on forces `dockClickHide` off.
   * `eye.slash` "Click the Dock icon to hide the app" / "The active app hides when you click its Dock icon. Click again to bring it back." - switch `dockClickHide` (false). Turning on forces `dockClickMinimize` off.
   * `arrow.triangle.2.circlepath` "Click the Dock icon to cycle windows" / "Click an active app’s Dock icon to rotate through its windows, like ⌘`." - switch `dockClickCycleWindows` (false).
   Each change → `DockClickService.syncWithPreferences()`.
4. **Spaces order card** (no title; anchor `spacesOrder`; shown if spacesOrder available): row (symbol `rectangle.split.3x1`) "Keep Spaces in a fixed order" / "Stops macOS from rearranging Spaces by most recent use, so they stay in the order you set. Your previous setting returns when this is turned off. The Dock may restart once to apply the change." - switch `spacesOrderEnabled` (false).
5. **Window thumbnails card** (shown if dockPreview available): `WindowPreviewsCard(sizeKey: previewSize, excludedAppsKey: windowPreviewExcludedApps)` — see 2.3 (default `previewSize` = `normal`, `windowPreviewExcludedApps` = []).
6. Permission cards (Accessibility if any dock feature active; Screen Recording card when Dock Preview is on) - macOS only.

Hub entries: Dock Preview "Window previews when hovering the Dock" (symbol `dock.rectangle`); Dock clicks "Click a Dock icon to minimize or cycle windows" (`dock.arrow.down.rectangle`); Keep Spaces in a fixed order (hub desc = the caption; symbol `rectangle.split.3x1`).

### 3.2 Dock Preview panel strings (shown outside the Settings page; for the panel implementer)
"Open window" · "Close window" · "Minimize window" · "Restore window" · "Pin preview" · "Unpin preview" · "Pinned" · "Close preview" · "Previous window" · "Next window" · "Active in the Dock" · "Beta. You may run into some bugs." · "No open window" (`switcherNoOpenWindow`).

### 3.3 Settings table
| Key | Type | Default | Notes |
|---|---|---|---|
| `dockPreviewEnabled` | Bool | false | |
| `dockPreviewCurrentSpaceOnly` | Bool | false | |
| `dockPreviewOpenDelay` | Int | 200 | 200…900 ms |
| `dockPreviewBackgroundOpacity` | Double | 1.0 | 0.4…1.0 |
| `dockPreviewQuitAppOnClose` | Bool | false | |
| `dockPreviewKeepDockVisible` | Bool | false | experimental |
| `dockPreviewRestoreAutohide` | (internal) | | crash-recovery marker, never backed up |
| `dockPreviewOrderByCreation` | Bool | false | ascending window id as creation proxy |
| `dockClickMinimize` / `dockClickHide` / `dockClickCycleWindows` | Bool | false | min and hide are mutually exclusive |
| `spacesOrderEnabled` | Bool | false | `com.apple.dock mru-spaces` |
| `previewSize` | String | `normal` | |
| `windowPreviewExcludedApps` | [String] | [] | |
| `minimalWindowPreviews` | Bool | false | |

### 3.4 Behaviour
* **Dock Preview:** a global mouse-moved tap tracks the pointer over the Dock; reading the Dock's Accessibility tree identifies the hovered app tile; after `openDelay` ms (list is pre-fetched `prefetchLead` earlier) an NSPanel of cards (one per window, ordered by last use or creation) opens along the Dock edge (orientation bottom/left/right) with a "corridor" so moving from icon to panel does not close it; once the cursor enters the panel only the panel keeps the session alive; icon switch has `switchDelay`; the panel can be pinned; clicking a card activates the window (switching Space when "Show only the current desktop" is off); × closes the window, or quits the app when "Quit the app with the × button" is on; dragging a card drags the window ghost; "Keep Dock visible" holds Dock auto-hide while open (restored on leave, on app quit, and after a crash via `dockPreviewRestoreAutohide`, "May resize windows"). Capture is paused while an app on the thumbnail pause list is frontmost.
* **Dock click actions:** a click on the *active* app's Dock tile: minimize all its windows (second click restores), or hide the app (second click unhides), or cycle windows. Clicks within 0.25 s are one intent; toggling follows our own record for 1.5 s (AX state is ambiguous during the animation); a press that moves > 6 px is a Dock drag and is passed through. Minimize uses the app's "Minimize All" menu item only when verified (⌥⌘M with identifier `miniaturizeAll:`).
* **Spaces order:** writes `mru-spaces = false` to `com.apple.dock`, remembers the previous state (`absent`/`on`/`off`), restarts the Dock once, and restores on off/quit; respects managed (forced) preferences; if the user turns it back on in System Settings the feature switches itself off.

### 3.5 Windows mapping
| Vorssaint | Windows |
|---|---|
| Dock | Taskbar (`Shell_TrayWnd`, secondary monitors `Shell_SecondaryTrayWnd`). Strings [WIN]: "Dock" → "taskbar", "Dock icon" → "taskbar button". Titles: "Dock Preview" → "Taskbar Preview" (keep hub key `dockPreview`). |
| Hover detection | `WH_MOUSE_LL` move events filtered to rects of the taskbar windows; hit-test with UI Automation (`IUIAutomation.ElementFromPoint`) - the Windows 11 taskbar buttons are `Taskbar.TaskListButtonAutomationPeer` with `AutomationId` `Appid: <AUMID>` (Win10: `MSTaskListWClass` buttons, name "App - N running windows"). Resolve to app (AUMID / exe) then to its top-level windows (`EnumWindows` + `GetApplicationUserModelId` / PID). Taskbar orientation (bottom/top/left/right) = `SHAppBarMessage(ABM_GETTASKBARPOS)`. |
| Windows' own hover cards | The native thumbnails will appear too. Delay them with `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\ExtendedUIHoverTime` (ms, DWORD; restore afterwards) or suppress by drawing the Trayloom panel on top (topmost, layered) - a DWM limitation: the native flyout cannot be disabled by API. |
| Panel thumbnails | `DwmRegisterThumbnail` with `DWM_TNP_RECTDESTINATION | VISIBLE | OPACITY | SOURCECLIENTAREAONLY=false` in a topmost WPF window (no Screen Recording permission); minimized windows: cached bitmap/Graphics.Capture. Card actions: click = `ShowWindow(SW_RESTORE)` if iconic + `SetForegroundWindow` (use `AttachThreadInput` foreground trick), × = `PostMessage(WM_CLOSE)`, "quit app" = `WM_CLOSE` to all of the process' top-level windows then `Process.CloseMainWindow`; minimize = `SW_MINIMIZE`. Order by creation: no creation time per window → use `GetWindowThreadProcessId`+enumeration order or `GetProcessTimes` creation time of the owner; or HWND ascending as proxy (same as Swift: "ascending window ID"). |
| Panel background opacity | Slider drives the panel background alpha; Mica/Acrylic backdrops ignore it - if the app theme draws Acrylic ("glass") show the Liquid Glass caption [WIN]: "Acrylic is on, so the panel’s transparency follows Windows Settings > Personalization > Colors > Transparency effects." |
| Keep Dock visible | Auto-hide taskbar: `SHAppBarMessage(ABM_GETSTATE)` to read `ABS_AUTOHIDE`; while the panel is open call `ABM_SETSTATE` with `ABS_ALWAYSONTOP` (clears autohide) then restore on close/exit/crash (persist the pending-restore marker). Desktop work-area changes may resize maximized windows - same "May resize windows" caveat. |
| Dock click minimize / hide / cycle | `WH_MOUSE_LL` `WM_LBUTTONDOWN/UP` on the taskbar button of the *foreground* app (UIA hit-test): minimize = `ShowWindow(SW_MINIMIZE)` on all app windows; second click restores them (track our record 1.5 s); hide the app has no Windows concept → treat as minimize-all and mark "Hide" row macOS-only (or omit); cycle = activate the app's next window in z-order (`GetWindow(GW_HWNDNEXT)` / MRU list) like Alt+`. Swallow the click only when acting; Windows itself minimizes the active window on a single-window taskbar click, so only multi-window apps need the feature. Drag threshold 6 px, repeat gap 0.25 s. |
| Spaces order | macOS-only: Windows virtual desktops never reorder themselves. Omit the card on Windows (hub tile hidden). If a placeholder is wanted: none. |
| Permission cards | none. |

### 3.6 Gap vs Trayloom (Dock page)
* Trayloom has **no** taskbar preview, no taskbar click actions and no Spaces-order equivalent (grep for taskbar/dock/virtual-desktop in `src` only finds `IslandWindow`/overlay placement code). Only `Trayloom.Features.Windows` (`SwitcherWindow.cs`) draws window cards; reuse its thumbnail code for the Taskbar Preview panel.
* Needs: `TaskbarPreviewFeature` (id `dock-preview`), `TaskbarClickFeature` (id `dock-click`), shared `WindowPreviewsCard` (size tiles + minimal previews + pause list) used by both Switcher and Taskbar Preview settings with the separate size/excluded keys listed above.

---

## 4. Page "Window layout" (`WindowLayoutSettings.swift`, `WindowGestureControls.swift`, `WindowLayoutIgnoredAppsList.swift`, `WindowMaximizerExclusionsList.swift`)

Sidebar entry: title `FeatureStrings.windowLayout.title` = "Window layout", symbol `rectangle.3.group` (Fluent `U+E8A9`? look up "GridView"/"Tiles" `U+F0E2`; suggested `U+E80A` ViewAll). Hub: "Window layout" - "Arrange windows with shortcuts or edge snapping, then adjust them by dragging". The page is a **grouped `Form`** (not cards): each `Section` is a rounded group (macOS `.formStyle(.grouped)`: section header text above the group, rows separated by hairlines, toggles label-left/switch-right, captions `.caption` secondary under the control). Replicate with `SettingsCard`-like groups: header (13 semibold secondary) + group radius 10, fill secondary 6 %, row padding 10x8.

### 4.1 Layout (section order, top to bottom)
1. **Section (no header)** - shown if windowLayout available:
   * Toggle "Show in panel" (key `panelUtilityWindowLayout`, default true).
   * caption "Arrange windows into screen sections or move and resize them with a trackpad or mouse."
   * `Label("Uses Accessibility only to move and resize windows.", "hand.raised")` caption secondary. [WIN] drop (no permission) or "Uses the Windows window APIs only to move and resize windows."
2. **Section "Permission required"** with the Accessibility row - when permission missing. (macOS-only.)
3. **Window maximizer section** (no header; anchor `windowMaximizer`; shown if windowMaximizer available) — **macOS-only** (see 4.5):
   * Toggle "Maximize windows" (`windowMaximizeEnabled`, false) + caption "The green button maximizes without creating another Space."
   * If on and running: `Label("Green button override active", "checkmark.circle.fill")` caption green.
   * `WindowMaximizerExclusionsList`: AppBundleList title "Keep full screen in these apps", add "Add an app…", remove "Remove", caption "The green button keeps its macOS behavior in these apps, so games, emulators and video players can still enter full screen.", `reachesEveryApp: true`, `acceptsExecutables: false`; key `windowMaximizeExcludedApps` ([String] sanitized, default []).
   * (Also string `windowMaximizeNeedsAccessibility` "Needs Accessibility to work.")
4. **Section (no header)** - shown if windowLayout OR windowMaximizer available: Toggle "Reserve space for Stage Manager" (`windowReserveStageManagerSpace`, false) + caption "Keep recent apps visible when maximizing or arranging windows, while Stage Manager and Show recent apps are on." — **macOS-only** (4.5).
5. Everything below only if windowLayout available:
6. **Section "Window dragging"** (`gestureSection`):
   * Toggle "Snap windows at screen edges" (`windowEdgeSnapEnabled`, false); onChange → service sync.
   * caption "Turn this on and drag a window title bar to a highlighted area below. A window moved by holding the modifier keys snaps too. Click an area to choose where the window goes."
   * `WindowEdgeSnapZonePicker` (4.2) — disabled and opacity 0.45 while the toggle is off; onChange of `windowEdgeSnapDisabledZones` → sync.
   * If the OS's own tiling is enabled (`isSystemTilingEnabled`): `Label("macOS is using the same edges. Turn off window tiling in Desktop & Dock so Vorssaint can take over.", "exclamationmark.triangle.fill")` orange caption; if edge snap is on additionally the caption "Enabled in Vorssaint. It starts working as soon as macOS tiling is off."; small button "Open Desktop & Dock" (opens System Settings > Desktop & Dock). The tiling state is re-read on appear and on app activation.
   * Toggle "Move and resize by dragging" (`windowGestureEnabled`, false) + caption "On a trackpad or mouse, hold the shown modifier keys and drag anywhere inside a window."
   * If on: `WindowGestureModifierPicker` with title "Keys to move" (key `windowGestureModifiers`, default `control+command`); `WindowGestureHints` (two chips: `[chord] ⤧ Drag to move` and `[chord+⇧] ⤡ Add Shift and drag to resize`); caption "The starting point chooses the nearest edge or corner. On a mouse, right-button drag also resizes."; Toggle "Bring the dragged window to front" (`windowGestureRaiseWindow`, false).
7. **Section "Ignore apps"** (`windowLayoutIgnoredApps` strings `sectionTitle`): `WindowLayoutIgnoredAppsList` - AppBundleList title "Pause in these apps", add "Add an app…", remove "Remove", caption "Window Layout does not use mouse or keyboard input while one of these apps is focused.", reachesEveryApp & acceptsExecutables true. Key `windowLayoutIgnoredApps` ([String], default []). Add/remove → `syncWithPreferences()`.
8. **Section "Gaps"**: two menu pickers "Window gap" and "Screen gap" (keys `windowLayoutWindowGap` and `windowLayoutScreenGap`, Int px, default 0). Options (value → label): 0 "None"; 8 "Tiny (8 px)"; 16 "Small (16 px)"; 32 "Medium (32 px)"; 64 "Large (64 px)"; 128 "Extra large (128 px)"; any other stored value "{n} px". Caption "Space between snapped windows, and between windows and the screen edge." (no service sync needed; read at each apply).
9. **Section "Shortcuts"**:
   * Toggle "Shortcuts" (`windowLayoutShortcutsEnabled`; **registered default false**, the `@AppStorage` fallback true is overridden) + caption "Use global shortcuts to arrange the active window without opening the panel." ; if any shortcut failed to register: orange caption "macOS rejected this shortcut. Choose another one."
   * Toggle "Shortcut + pointer layout" (`windowDirectionalEnabled`, false) + caption "Hold the shortcut, move the pointer toward an edge or corner, then release to place the active window."
   * If on: recorder button (width 108, enabled only with Accessibility; empty title = the current trigger text, waiting "Press keys"; also accepts **modifier-only** triggers, which need ≥2 of Control/Option/Command, stored as `modifiers:control+option`) key `windowDirectionalShortcut`, default ⌃⌥Space (`control+option:49`); orange error text (conflict: "This shortcut is already used by %@.", invalid, not captured); orange "macOS rejected this shortcut. Choose another one." when registration failed.
10. **Section (no header)**: Toggle "Move pointer to next display" (`pointerDisplayEnabled`, false) + caption "Puts the pointer in the center of the next display." + `ShortcutPreferenceRow(role: .pointerNextDisplay)` (key `pointerDisplayShortcut`, default ⌃⌥⌘Z, disabled while the toggle is off; extra conflict check against window-layout actions) + orange "macOS rejected this shortcut…" if registration failed. Works without Accessibility.
11. **Section (no header)**: Toggle "Click the Dock icon to cycle windows" (mirror of `dockClickCycleWindows`) + caption "Click an active app’s Dock icon to rotate through its windows, like ⌘`."
12. **Placement sections** (each a `Section(title)` with one *action row* per action; Halves also has a toggle):
    * "Halves": Left, Right, Top, Bottom, Center half; then Toggle "Repeat Left or Right to cycle sizes" (`windowLayoutSideRepeatCyclesThirds`, false) + caption "Half, then two thirds, then one third on the same display. Off, with more than one display, the repeat moves the window to the next display on that side."
    * "Thirds": Left 1/3, Center 1/3, Right 1/3, Left 2/3, Right 2/3, Center 2/3, Top 1/3, Middle 1/3, Bottom 1/3, Top 2/3, Bottom 2/3.
    * "Quarter rows": Top 1/4, Upper middle 1/4, Lower middle 1/4, Bottom 1/4.
    * "Quarter columns": Left 1/4, Left middle 1/4, Right middle 1/4, Right 1/4.
    * "Sixths": Top left 1/6, Top center 1/6, Top right 1/6, Bottom left 1/6, Bottom center 1/6, Bottom right 1/6.
    * "Corners": Top left, Top right, Bottom left, Bottom right.
13. **Section "Actions"** (`other`): rows Maximize, Maximize with Margin, then the margin slider row (`Slider` 0…25 step 1, readout `"{n}%"` right-aligned 44 wide, caption/label text "Margin per edge" used as the slider's accessibility label; key `windowLayoutMarginPercent`, Double, default 5.0), then Full Screen, Center, Previous display, Next display, Restore. Then, if the last action produced a result, a caption: success "Window arranged." / "Window restored." (green), failures orange: "Grant Accessibility to move windows." · "No active window found." · "No previous layout to restore." · "Could not move this window."

### 4.2 Action row (`WindowLayoutActionRow`) and the edge-snap map
**Action row:** `HStack(spacing 8)`: a button `Label(title, symbol)` that applies the action to the active window now ("try it"; disabled without permission) · `Spacer` · shortcut recorder 108 wide (enabled when shortcuts toggle on; empty label "None" when cleared; waiting "Press keys") · clear button `xmark.circle.fill` (hierarchical, secondary; tooltip/label "Remove shortcut"; disabled when no shortcut) · "Reset" button (restores the default; disabled when equal to default). Beneath: orange error / recording hint "Press the new shortcut Escape cancels. Delete clears." / system take-over offer. Saving checks conflicts against every global shortcut role, then other layout actions ("This shortcut is already used by %@."), then macOS system shortcuts (offer to take over, "Don’t take over" → "This shortcut is already used by macOS."). Storage: one key per action (below); value `"mods:keyCode"` or `"none"` when cleared; a missing or corrupt value falls back to the default.

Action → SF symbol (Fluent suggestions in brackets: use "ViewAll/Tiles/Rectangle" family; no direct halves glyphs exist - draw a 16x12 mini-screen with the filled region, which is also what the edge map uses):
leftHalf `rectangle.lefthalf.inset.filled`, rightHalf `rectangle.righthalf.inset.filled`, topHalf `rectangle.tophalf.filled`, bottomHalf `rectangle.bottomhalf.filled`, centerHalf/centerThird/centerTwoThirds `rectangle.center.inset.filled`, leftThird `rectangle.leftthird.inset.filled`, rightThird `rectangle.rightthird.inset.filled`, leftTwoThirds `rectangle.leadinghalf.filled`, rightTwoThirds `rectangle.trailinghalf.filled`, leftQuarter `rectangle.portrait.leftthird.inset.filled`, leftMiddleQuarter `rectangle.portrait.lefthalf.inset.filled`, rightMiddleQuarter `rectangle.portrait.righthalf.inset.filled`, rightQuarter `rectangle.portrait.rightthird.inset.filled`, topThird `rectangle.portrait.topthird.inset.filled`, middleThird `rectangle.portrait.center.inset.filled`, bottomThird `rectangle.portrait.bottomthird.inset.filled`, topTwoThirds `rectangle.portrait.tophalf.inset.filled`, bottomTwoThirds `rectangle.portrait.bottomhalf.inset.filled`, topQuarter `rectangle.topthird.inset.filled`, upperMiddleQuarter `rectangle.tophalf.inset.filled`, lowerMiddleQuarter `rectangle.bottomhalf.inset.filled`, bottomQuarter `rectangle.bottomthird.inset.filled`, topLeft/topLeftSixth `arrow.up.left`, topCenterSixth `arrow.up`, topRight/topRightSixth `arrow.up.right`, bottomLeft/bottomLeftSixth `arrow.down.left`, bottomCenterSixth `arrow.down`, bottomRight/bottomRightSixth `arrow.down.right`, maximize `arrow.up.left.and.arrow.down.right`, marginMaximize `rectangle.inset.filled`, fullScreen `rectangle.fill`, center `scope`, previousDisplay `arrow.left.to.line`, nextDisplay `arrow.right.to.line`, restore `arrow.uturn.backward`. (Arrows: Fluent `U+E74A` Up, `U+E74B` Down, `U+E72B` Back, `U+E72A` Forward, diagonals via rotated `U+E74A`; maximize `U+E740` FullScreen; restore `U+E777`; center `U+E81D`/target; margin: `U+E7FB`.)

**Per-action storage keys and default shortcuts** (macOS ⌃⌥ = Ctrl+Alt on Windows; ⌃⌥⌘ chords in Trayloom → Ctrl+Alt+Shift):

| Action | Key (`DefaultsKey`) | Default |
|---|---|---|
| leftHalf | `windowLayoutShortcutLeft` | ⌃⌥← |
| rightHalf | `windowLayoutShortcutRight` | ⌃⌥→ |
| topHalf | `windowLayoutShortcutTop` | ⌃⌥↑ |
| bottomHalf | `windowLayoutShortcutBottom` | ⌃⌥↓ |
| centerHalf | `windowLayoutShortcutCenterHalf` | none |
| topLeft / topRight / bottomLeft / bottomRight | `windowLayoutShortcutTopLeft/TopRight/BottomLeft/BottomRight` | ⌃⌥U / I / J / K |
| maximize | `windowLayoutShortcutMaximize` | ⌃⌥Return |
| marginMaximize | `windowLayoutShortcutMarginMaximize` | none |
| fullScreen | `windowLayoutShortcutFullScreen` | none |
| center | `windowLayoutShortcutCenter` | ⌃⌥C |
| restore | `windowLayoutShortcutRestore` | ⌃⌥R |
| leftThird / centerThird / rightThird | `…LeftThird/CenterThird/RightThird` | ⌃⌥D / F / G |
| leftTwoThirds / rightTwoThirds | `…LeftTwoThirds/RightTwoThirds` | ⌃⌥E / T |
| centerTwoThirds, topThird, middleThird, bottomThird, topTwoThirds, bottomTwoThirds | `…CenterTwoThirds/TopThird/MiddleThird/BottomThird/TopTwoThirds/BottomTwoThirds` | none |
| topQuarter, upperMiddleQuarter, lowerMiddleQuarter, bottomQuarter, leftQuarter, leftMiddleQuarter, rightMiddleQuarter, rightQuarter | `…TopQuarter/UpperMiddleQuarter/LowerMiddleQuarter/BottomQuarter/LeftQuarter/LeftMiddleQuarter/RightMiddleQuarter/RightQuarter` | none |
| the six sixths | `…TopLeftSixth/TopCenterSixth/TopRightSixth/BottomLeftSixth/BottomCenterSixth/BottomRightSixth` | none |
| previousDisplay | `windowLayoutShortcutPreviousDisplay` | none |
| nextDisplay | `windowLayoutShortcutNextDisplay` | ⌃⌥⌘→ |

(All keys are prefixed `windowLayoutShortcut`.) Registered shortcut ids 30…70 (per action) are internal. Hidden-action list key `windowLayoutHiddenActions` ("" default) hides actions from the panel's layout grid only; an assigned shortcut keeps working.

**Edge-snap zone map (`WindowEdgeSnapZonePicker`)** - centred row: `HStack(alignment top, spacing 9)` { map, reset button }.
* Map: width 340 (compact variant used elsewhere: 258), padding 7, radius 12, fill primary 2.5 %, stroke primary 14 %; 3 rows, spacing 5: top row [topLeft | top | topRight] height 50; middle row [left | blank centre | right] height `max(62, parts·22 + (parts−1)·3)` where parts = the largest area count of the left/right edge; bottom row [bottomLeft | bottom | bottomRight] height 50. Corner/side cells fixed width 78; centre cell flexible. The blank centre is a rounded rect (r7, primary 1.8 % fill) with a 14pt `cursorarrow.motionlines` glyph in quaternary.
* Each zone holds 1…4 **area buttons** (corners: always 1; top/bottom edges split side by side; left/right edges split stacked top→bottom; spacing 3). Area button: radius 7, `ZStack` { fill, 1px stroke, placement glyph }. On: fill accent 13 % (hover 20 %), stroke accent 42 % (hover 65 %), glyph accent. Off: fill primary 2.5 % (hover 6.5 %), stroke primary 9 % (hover 20 %), glyph secondary 45 %. Hover animation 0.1 s; map animates 0.14 s ease-in-out on changes. A zone that is off shows a small `xmark.circle.fill` (9pt) in its top-right corner. Tooltip = the placement title; accessibility "{place}: {title}" (+ " i/n" for split edges), place names Top left / Top center / Top right / Center left / Center right / Bottom left / Bottom center / Bottom right.
* Glyph = mini screen 24x16 (compact 18x12): outline r2.5 at 55 % opacity + a filled rect showing the placement (computed on a 1600x1000 screen, inset 2.5).
* Reset button: 24x24, `arrow.counterclockwise` 11pt semibold secondary, radius 6 fill primary 4.5 %, disabled (opacity 0.35) when nothing differs from the standard layout; tooltip "Reset".
* Click an area → popup menu anchored under it: first item "Use this corner" (corner zones) or "Use this edge" (edges), checkmark = zone enabled (toggles the whole zone); separator; submenus "Halves", "Thirds", "Quarter rows", "Quarter columns", "Sixths", "Corners" (each with the placements of that family, check on the current one; the submenu also shows a check if it contains the current placement) and the flat entries "Maximize", "Maximize with Margin" (group "Actions"); for edges also a separator and the submenu "Areas on this edge" with items "1", "2", "3", "4" (check = current count). Choosing a placement or a count also enables the zone.
* Defaults per zone: topLeft→Top left, top→Maximize, topRight→Top right, left→Left, right→Right, bottomLeft→Bottom left, bottom→Bottom, bottomRight→Bottom right. Changing the count of an edge resets to equal columns/rows: 2 → Left+Right (top/bottom) or Top+Bottom (sides); 3 → thirds; 4 → quarters; back to 1 restores the default placement; picking the current count keeps placements.
* Storage: `windowEdgeSnapDisabledZones` = comma list of zone ids (`topLeft,top,topRight,left,right,bottomLeft,bottom,bottomRight`), default ""; `windowEdgeSnapZoneActions` = `zone=action(+action…)` entries only for changed zones, default "" (unknown/invalid entries dropped).

**Modifier picker (`WindowGestureModifierPicker`):** row `HStack(spacing 7)`: title "Keys to move" (body, secondary) + spacer + three keycaps ⌃ (Control) ⌥ (Option) ⌘ (Command): 30x25, radius 6, 14pt semibold rounded; selected: accent text, fill accent 14 %, stroke accent 45 %; unselected secondary text, fill primary 4.5 %, stroke primary 10 %. At least one of Control/Option/Command must remain (the last selected one is disabled). Shift is reserved for resize and shown in the hint. Storage: tokens joined by `+` in the order `control+option+shift+command`; invalid → default `control+command`.
**Hints:** two capsules in one row (radius 7, fill primary 4.5 %, padding 8x5): chord keycaps (11pt semibold rounded secondary), accent glyph `arrow.up.and.down.and.arrow.left.and.right` (move) / `arrow.up.left.and.arrow.down.right` (resize), text "Drag to move" / "Add Shift and drag to resize" (caption, 1 line, min scale 0.8). Resize chord = move chord + ⇧.

### 4.3 Settings table (Window layout page)
| Key | Type | Default | Notes |
|---|---|---|---|
| `panelUtilityWindowLayout` | Bool | true | show in quick panel |
| `windowLayoutShortcutsEnabled` | Bool | false | registered default (view fallback true) |
| `windowDirectionalEnabled` | Bool | false | |
| `windowDirectionalShortcut` | String | `control+option:49` | or `modifiers:…` |
| `windowEdgeSnapEnabled` | Bool | false | |
| `windowEdgeSnapDisabledZones` | String | "" | |
| `windowEdgeSnapZoneActions` | String | "" | |
| `windowGestureEnabled` | Bool | false | |
| `windowGestureModifiers` | String | `control+command` | |
| `windowGestureRaiseWindow` | Bool | false | |
| `windowLayoutIgnoredApps` | [String] | [] | |
| `windowLayoutWindowGap` / `windowLayoutScreenGap` | Int | 0 / 0 | presets 0,8,16,32,64,128 |
| `windowLayoutMarginPercent` | Double | 5.0 | 0…25 |
| `windowLayoutSideRepeatCyclesThirds` | Bool | false | |
| `windowReserveStageManagerSpace` | Bool | false | macOS-only |
| `windowMaximizeEnabled` | Bool | false | macOS-only |
| `windowMaximizeExcludedApps` | [String] | [] | macOS-only |
| `pointerDisplayEnabled` | Bool | false | |
| `pointerDisplayShortcut` | String | ⌃⌥⌘Z | |
| `dockClickCycleWindows` | Bool | false | shared with Dock page |
| per-action keys | String | see table | |

### 4.4 Behaviour
* **Apply:** target = frontmost window of the active app (`Active window`); frame computed from the display's visible frame (menu bar/Dock excluded) minus screen gap, with window gap between neighbours; Stage Manager reserve shrinks the visible frame by the strip width when reserved; margin maximize uses the margin % on every edge; Restore returns to the frame saved before the first layout change of that window (error "No previous layout to restore."); Full Screen toggles the native full screen (macOS); Previous/Next display move the window keeping relative position; Center centres in the visible frame. Repeating Left/Right with "Repeat Left or Right to cycle sizes" cycles half → 2/3 → 1/3 on the same display; off + multiple displays, a repeat moves the window to the adjacent display on that side.
* **Gesture (`WindowGestureSupport`):** modifier chord held + left button down → the press is *held* (not delivered) until the pointer travels > 6 px (`dragSlop`); release before that replays the click untouched (so modifier-clicks keep working); past 6 px it becomes a window move; with Shift added (or right button on a mouse) it becomes a resize from the nearest edge/corner (corner zone 12 px, edge zone 5 px); optional raise of the window; custody rules guarantee a swallowed press is always given back exactly once (events that disable the tap, a second button, an unrelated event).
* **Edge snap:** while a window title bar (or a chord-move) is dragged, the pointer within 12 px (`activationDistance`) of a screen edge/corner and moving slower than 400 px/s (`crossingSpeed`: faster means passing, not aiming) arms that zone; a translucent preview of the target frame appears; releasing applies the zone's placement; zones that are off do nothing; resizing drags (not moves) are classified as `.resizing` and ignored; velocity from the last ~50 ms (span 0.05 s, min 0.02, still after 0.1 s, 512-sample buffer).
* **Pointer layout (directional):** while the trigger is held, the pointer's travel from where the trigger was pressed ≥ 28 px picks one of 8 sectors of 45° (right → Right half, up-right → Top right, up → Top half, up-left → Top left, left → Left half, down-left → Bottom left, down → Bottom half, down-right → Bottom right); releasing the trigger applies it; unmoved release = Maximize (key trigger repeats don't force it; the maximize/minimize "special" via repeats is suppressed); wheel/click/key cancels a modifier-only trigger and lets the input pass.
* **Ignore apps:** while the frontmost app is on the list, shortcuts, gestures and edge snap are inert (synced on every app switch).
* **Pointer to next display:** warps the pointer to the centre of the next display (display order = Displays settings arrangement, cycling).
* **Result message** (section Actions) reflects the last `apply` from the page.

### 4.5 macOS-only parts and Windows equivalents
| Item | Windows |
|---|---|
| "Maximize windows" (green button override) + exclusions | macOS-only: the traffic-light green button enters Spaces full screen; Windows' maximize button already maximizes in place. Drop the section on Windows (keep the keys and hub tile hidden), or repurpose as "Maximize button uses margin / work area" - not in the source, do not build unasked. |
| Stage Manager reservation | macOS-only. Closest Windows analogue: an auto-hidden taskbar (work area then covers it). Optional: hide the row; if a row is wanted use "Leave room for an auto-hidden taskbar" - a Trayloom invention, not in the original. |
| OS tiling conflict (Desktop & Dock) | Windows Snap: `SystemParametersInfo(SPI_GETWINARRANGING)` (0x0082) tells whether drag-to-edge snapping is on (`SPI_SETWINARRANGING` 0x0083 toggles; `HKCU\Control Panel\Desktop\WindowArrangementActive`, plus `HKCU\...\Explorer\Advanced\SnapAssist`/`EnableSnapBar` for Snap Assist and Snap Layouts). Strings [WIN]: "Windows Snap is using the same edges. Turn off “Snap windows” in Settings > System > Multitasking so Trayloom can take over." / "Enabled in Trayloom. It starts working as soon as Windows Snap is off." / button "Open Multitasking settings" (`Process.Start("ms-settings:multitasking")`). Existing Trayloom stance is to *not* reimplement edge snapping because Snap exists; the Vorssaint spec requires it. |
| Accessibility API (AX) move/resize | `SetWindowPos` (or `MoveWindow`) with `SWP_NOZORDER|SWP_NOACTIVATE`; compensate the invisible 7-px resize borders on Win10/11 by comparing `GetWindowRect` with `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)`; restore from maximized/minimized first (`ShowWindow(SW_RESTORE)`) because `SetWindowPos` is ignored on maximized windows; per-monitor DPI aware (manifest `PerMonitorV2`). Visible frame = `GetMonitorInfo().rcWork`. Full Screen action: F11 is app-specific; closest = borderless-style maximize covering the monitor `rcMonitor` (set `WS_POPUP` temporarily) or `SW_MAXIMIZE` + hide taskbar; propose "Full Screen = send F11" with fallback to rcMonitor. Elevated windows cannot be moved by a non-elevated process (UIPI). |
| Gesture (modifier + drag) | `WH_MOUSE_LL` `WM_LBUTTONDOWN/MOUSEMOVE/LBUTTONUP` (+ `WM_RBUTTONDOWN` for resize) with `GetAsyncKeyState` chord check; window under pointer = `GetAncestor(WindowFromPoint, GA_ROOT)` (exclude desktop/taskbar/shell); hold the press by returning 1 (swallow) and re-inject down+up with `SendInput` (magic tag) if released before 6 px; moving = `SetWindowPos` per move event. Modifiers: ⌃⌘ default → Ctrl+Win is awkward (Win key triggers Start on release; inject an unused key like VK 0xE8 before releasing to cancel Start). Recommended Windows default: **Ctrl+Alt** (document as a divergence) while the picker offers Ctrl / Alt / Win keycaps ("⌃" `Ctrl`, "⌥" `Alt`, "⌘" `Win` - label keycaps with text on Windows: "Ctrl", "Alt", "Win"; Shift reserved for resize). Precision Touchpad: tap-and-drag arrives as ordinary left button events, so it works on touchpads. |
| Edge-snap overlay | Topmost layered click-through WPF window (`WS_EX_TRANSPARENT|WS_EX_LAYERED|WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW`) showing the preview rect with 150 ms fade; drag detection with `SetWinEventHook(EVENT_SYSTEM_MOVESIZESTART/END)` for title-bar drags + `GetCursorPos` polling (or the LL hook). |
| Pointer layout (directional) | Hold a `RegisterHotKey` chord: `RegisterHotKey` gives only key-down; track release via the keyboard hook (`WH_KEYBOARD_LL` up event) or modifier polling; sampling of pointer via the LL mouse hook. |
| Pointer to next display | `EnumDisplayMonitors` ordered by `rcMonitor.left,top`; `MonitorFromPoint(GetCursorPos)` index+1; `SetCursorPos(center)`. Needs no special permission. |
| "Click the Dock icon to cycle windows" row | Mirrors the Taskbar page row (see 3.5). |
| Panel "Show in panel" | Quick panel spec. |

### 4.6 Gap vs Trayloom (`Trayloom.Features.Windows/WindowLayoutFeature.cs`)
* Present: 18 actions (left/right/top/bottom half, 4 quarters, 3 thirds, left/right two-thirds, maximize, center, restore, next/previous monitor) with hotkeys through `AddHotkey`, one `gap` slider (0…32 px, "half of it at the screen edges"), `IgnoredClasses` for shell windows, restore memory, `QuickActionIds` for the quick panel. Default shortcuts: Ctrl+Alt + arrows/U/I/J/K/D/F/G/E/T/C/Enter, **Restore = Ctrl+Alt+Backspace** (Vorssaint: ⌃⌥R → use R), next/previous monitor Ctrl+Alt+Shift+→/← (Vorssaint has Next = ⌃⌥⌘→ and Previous with no default).
* Missing: all other placements (center half, top/middle/bottom thirds, top/bottom two-thirds, center two-thirds, quarter rows/columns, sixths, margin maximize, full screen) and their sections; separate window gap/screen gap pickers with presets (Trayloom: one slider); margin slider; "Repeat Left or Right to cycle sizes"; per-action Remove/Reset buttons and "try it" buttons (Trayloom shows hotkey fields only); the page structure (Form sections); "Show in panel" toggle; "Shortcuts" master toggle (Trayloom: always on); edge snapping (explicitly *not* implemented - note in `BuildSettings`), drag gestures, zone map, ignore-apps list, directional "Shortcut + pointer layout", "Move pointer to next display", result messages, Stage Manager row (n/a), maximizer (n/a).
* Settings keys: Trayloom stores `hotkey.<layout-id>` (feature-scoped); adopt the Vorssaint key names (`windowLayoutShortcut…`) or add a mapping table so a future import works.

---

## 5. Page "Quit on close" (`AutoQuitSettings.swift`)

Sidebar entry: `autoQuitName` = "Quit on close", symbol `xmark.rectangle` (Fluent `U+E8BB` ChromeClose / `U+E711`). Hub description: "Quit apps when their last window closes". A grouped `Form` (same chrome as 4.1).

### 5.1 Layout
1. **Section (no header):** Toggle "Quit an app when its last window closes" (`autoQuitEnabled`, false; onChange → service sync) · caption "Closing an app’s last window also quits it." · if on and running: `Label("Active now", "checkmark.circle.fill")` caption green.
2. **Section "How it works":** bullet 1 (`rectangle.badge.xmark`, 12pt, accent, 18 wide) "Close an app’s last window (⌘W or the red button)." [WIN] "(Ctrl+W or the X button)"; bullet 2 (`bolt.fill`) "The app quits on its own. “Save changes?” dialogs still appear."; caption "Apps that have not opened a window stay open. Menu bar apps stay open unless you explicitly allow them to quit." [WIN] "…Tray apps stay open…"; warning label (`exclamationmark.triangle.fill`, caption secondary) "Some apps close their last window while they keep working, such as a screen recorder once capture starts. Those are quit too, unless they are on the Exceptions list."
3. **Section "Exceptions"** (`autoQuitExceptionsTitle`): if the (visible) list is empty: callout secondary "No exceptions"; else rows sorted by name: icon 20x20 + name + `Spacer` + (mandatory app: `lock.fill` tertiary; otherwise `minus.circle.fill` button). Row group disabled while the feature is off. Button `Label("Add app…", "plus")` (disabled when off) opens `AppPickerView` (excludes already-listed; apps resolved to bundle id). Caption "Apps on this list stay open even with no windows."
4. **Section "Menu bar apps to quit"** (`AutoQuitDockAppsStrings.listTitle`): rows (icon, name, minus button, label "Remove"), disabled when off; button `Label("Add an app…","plus")` opens a picker that lists only menu-bar-only apps (`LSUIElement` and not `LSBackgroundOnly`; excluded: already added and mandatory ones) with empty note "No menu bar apps available to add."; caption "Menu bar apps stay open by default. Apps you select here can quit after their last window closes while they are in the Dock. Exceptions still keep them open."
5. **Section "Permission required"** (Accessibility row) when on and permission missing (macOS-only).

### 5.2 Settings
| Key | Type | Default | Notes |
|---|---|---|---|
| `autoQuitEnabled` | Bool | false | |
| `autoQuitExceptions` | [String] | [`com.apple.finder`, `com.apple.mobilephone`] (mandatory ids) | mandatory rows locked; Phone is hidden when not installed but still protected |
| `autoQuitDockApps` | [String] | [] | menu-bar apps allowed to quit while in the Dock |

### 5.3 Behaviour
* Per regular app an Accessibility observer watches window created/destroyed/miniaturized; an app becomes *eligible* only after it has shown at least one window since watching began (window-less agents are never touched); when an eligible app drops to zero user-facing windows (not sheets/palettes/system dialogs; minimized or other-Space windows still count; windows the window server lists on screen keep the app) it is asked to quit with a normal terminate (so unsaved-changes prompts still appear).
* Re-checks: after a window is destroyed look again at ~0.25 s fades plus two further delayed looks; ⌘W / close requests schedule checks for hide-on-close apps; apps whose AX lists no windows retry a few times then stop (no endless polling).
* Menu-bar/background apps (`LSUIElement`/`LSBackgroundOnly`) are exempt unless listed in "Menu bar apps to quit" and shown in the Dock; guest-VM helper windows inherit their host's exemption.
* Edits in lists apply immediately; lists are inert while the feature is off.

### 5.4 Windows mapping
* Detection: `SetWinEventHook(EVENT_OBJECT_CREATE/DESTROY/SHOW/HIDE, EVENT_OBJECT_NAMECHANGE not needed)` with `WINEVENT_OUTOFCONTEXT`, filtered to `OBJID_WINDOW` top-level visible windows (`WS_OVERLAPPED`/captioned, not `WS_EX_TOOLWINDOW`, not owned popups). Maintain per-PID counts; "eligible" after ≥1 such window was seen. On destroy → after ~300 ms, re-enumerate (`EnumWindows` for that PID, include minimized, cloaked-on-other-desktop windows via `DWMWA_CLOAKED`); if 0 → quit.
* Quit = `PostMessage(mainHwnd, WM_CLOSE)` to remaining top-level windows (none) is not possible; use `Process.CloseMainWindow()` → if the process has no main window, send `WM_QUIT`-like via `PostThreadMessage(WM_QUIT)` is unsafe - instead send `WM_CLOSE` to its hidden message windows, wait 3 s, and never `Kill()` (unsaved-work safety). Many Windows apps live on in the tray when the last window closes (Teams, Discord, Steam): those have a notification icon - treat apps with a tray icon (`Shell_NotifyIcon` windows enumerated via the tray UIA tree, or class `TrayNotifyWnd` children) as the equivalent of macOS "menu bar apps" (exempt by default; opt-in via "Menu bar apps to quit" → [WIN] "Tray apps to quit"). [WIN] "Dock" → "taskbar" in captions.
* Mandatory exceptions [WIN]: `explorer.exe` (shell!), `ApplicationFrameHost.exe`, `ShellExperienceHost.exe`, `SearchHost.exe`, `StartMenuExperienceHost.exe`, `TextInputHost.exe`, `dwm.exe`, `winlogon.exe`, `csrss.exe`, `taskmgr.exe` and Trayloom itself. Locked rows show a lock glyph (`U+E72E`). Replace `com.apple.mobilephone` (Phone) - not applicable.
* No Accessibility permission. UIPI: elevated apps' windows are still enumerable via WinEvent hook but `WM_CLOSE` posts require elevation - skip silently.
* Strings [WIN]: step1 "Close an app’s last window (Ctrl+W or the X button)."; picker uses the shared AppPicker; "Menu bar apps stay open…" → "Apps that live in the notification area stay open…".

### 5.5 Gap vs Trayloom
No auto-quit feature exists (`grep -ri "last window"` empty). Build `AutoQuitFeature` (Id `auto-quit`) in `Trayloom.Features.Windows` using WinEvent hooks, with the page above. Keep key names `autoQuitEnabled`, `autoQuitExceptions`, `autoQuitDockApps`.

---

## 6. Page "Quit & close protection" (`QuitProtectionSettings.swift`)

Sidebar entry: `QuitProtectionStrings.name` = "Quit & close protection", symbol `shield.lefthalf.filled` (Fluent `U+EA18` Shield/`U+E83D`). Hub description: "Protects ⌘Q and ⌘W from accidental presses" [WIN] "Protects Alt+F4 and Ctrl+W from accidental presses". Grouped `Form`.

### 6.1 Layout
1. **Section (no header):** caption "Configure each shortcut independently. The original action passes only after the selected confirmation." · `Label("Protection uses Accessibility to observe only ⌘Q and ⌘W globally.", "hand.raised.fill")` caption secondary.
2. **Section "⌘Q"** (header = `QuitProtectionShortcut.symbol`: "⌘Q" for quit, "⌘W" for close): 
   * Toggle "Protect this shortcut" (quit: `quitProtectionQuitEnabled`, close: `quitProtectionCloseEnabled`, default false) + caption "Other Command shortcuts continue to work normally."
   * `Picker` (menu) "Confirmation mode": "Hold to confirm" (`hold`, default) / "Double press" (`doublePress`) / "Require extra modifier" (`extraModifier`). Keys `quitProtectionQuitMode` / `quitProtectionCloseMode`.
   * mode hold: slider "Hold duration" 250…2000 step 50 (min label "250 ms", max label "2 s", caption2) + below "{n} ms" caption secondary. Keys `quitProtectionQuitHoldDurationMs`/`quitProtectionCloseHoldDurationMs` (Double, default 800).
   * mode doublePress: slider "Double press interval" 200…1500 step 50 (min "200 ms", max "1.5 s") + "{n} ms". Keys `quitProtectionQuitDoubleIntervalMs`/`quitProtectionCloseDoubleIntervalMs` (Double, default 600).
   * mode extraModifier: `Picker` "Extra modifier": "Shift (⇧)" / "Option (⌥)" / "Control (⌃)" (default shift; keys `quitProtectionQuitExtraModifier`/`…CloseExtraModifier`), then caption showing the resulting chord e.g. "⇧⌘Q".
   * `Picker` "Applications": "All applications" (`all`, default) / "Selected applications only" (`selectedOnly`) / "All except selected applications" (`allExceptSelected`). Keys `quitProtectionQuitScope`/`…CloseScope`.
   * when scope ≠ all: block "Selected applications" (subheadline semibold) + "No applications selected" caption or rows (icon 20, name, minus button), button `Label("Add application…","plus")` (opens AppPicker excluding listed; apps stored as bundle ids). Block disabled while the shortcut toggle is off. Keys `quitProtectionQuitExceptions`/`quitProtectionCloseExceptions` ([String], []).
   * Toggle "Show visual feedback" (`quitProtectionQuitShowFeedback`/`…CloseShowFeedback`, default true).
3. **Section "⌘W"** - same controls for the close shortcut.
4. **Section "Protection uses Accessibility to observe only ⌘Q and ⌘W globally."** (header) with the Accessibility row - when either shortcut is on and permission missing.
Every change → `service.syncWithPreferences()` (also on page appear). Sheet: AppPicker per shortcut.

### 6.2 HUD strings (feedback overlay)
Hold: "Hold %@ to quit" / "Hold %@ to close". Double: "Press %@ again to quit" / "Press %@ again to close". Extra modifier: "Use %@ to quit" / "Use %@ to close" (`%@` = the chord e.g. "⌘Q" or "⇧⌘Q"). Secondary hints: "Esc cancels", "Release to confirm". Key names: `shiftKey` "Shift", `optionKey` "Option", `controlKey` "Control".

### 6.3 Behaviour
* Event tap sees ⌘Q/⌘W key-down (layout-resolved character, with the QWERTY key code as fallback); key repeats with Command are swallowed; the original is delivered only after confirmation.
* Hold: key down starts a pending timer + HUD; release before the duration cancels (and swallows the release); at the duration the original shortcut is posted to the frontmost app. Command released early cancels.
* Double press: first press → HUD "Press … again"; a second press of the same shortcut within the interval (event timestamps) confirms; else the first expires.
* Extra modifier: the base chord is swallowed with a HUD; the chord with the extra modifier (e.g. ⇧⌘Q) passes after removing the extra modifier from the posted event.
* Scope: all / only the listed apps / all but the listed apps, decided on the frontmost app (some key-forwarding apps are special-cased, `keyForwardingBundleIdentifiers`).
* The HUD shows the hint "Esc cancels" while pending (no dedicated Escape handler was found in the service; releasing Command or the key cancels). Other Command shortcuts are untouched.

### 6.4 Windows mapping
* Shortcut pair: ⌘Q (Quit app) → **Alt+F4** (closes window/app); ⌘W (close window/tab) → **Ctrl+W**. Existing Trayloom also protects Ctrl+Q (apps such as Office/Chromium quit with Ctrl+Q / Ctrl+Shift+Q). Section headers [WIN]: "Alt+F4" and "Ctrl+W"; chord captions for extra modifier become e.g. "Shift+Alt+F4"/"Shift+Ctrl+W" (modifier set Shift/Alt/Ctrl; **Option→Alt may collide with the base Alt in Alt+F4**, so offer "Shift (⇧)", "Win" and "Control (⌃)" for the quit shortcut - flag as a design decision).
* `WH_KEYBOARD_LL` handler (priority below key-debounce and super-key): detect keydown VK_F4 with Alt down, VK 'W' with Ctrl down; pending state with a `System.Threading.Timer`; swallow (return 1) the keydown; on confirm inject the chord with `SendInput` (tagged) — the injected sequence includes the modifier down/up so apps see a real Alt+F4. Hold detection needs the key-up event (also swallowed). Key repeat (LLKHF auto-repeat not flagged on LL hooks; compare to previous down with same vk) swallowed.
* HUD = topmost layered window near the screen centre (existing `OverlayWindow`).
* Exceptions list identity = process exe path; the foreground app = `GetForegroundWindow` → PID. `keyForwardingBundleIdentifiers` equivalents: remote desktop / VM clients (mstsc, vmconnect, VMware, VirtualBox) where Alt+F4 must pass through — pass-through list.
* No Accessibility permission; replace the caption with nothing.

### 6.5 Gap vs Trayloom (`CloseProtectionFeature.cs`)
* Has: one feature "Close protection" with mode choice ("To confirm" - hold/double), hold time 200…3000 (default 500), double window 200…2000 (default 600), per-shortcut toggles Alt+F4/Ctrl+W/Ctrl+Q, hint toggle, text box `excludedApps`.
* Differs: Vorssaint configures each of the two shortcuts independently (enabled, mode incl. "Require extra modifier", own durations, scope with three options, own app list picker, own feedback toggle); hold range 250…2000 step 50 default 800; double 200…1500 step 50 default 600; keys `quitProtection{Quit,Close}*`. Missing: extra-modifier mode, per-shortcut settings, scope all/selected/all-except, app picker, HUD strings ("Hold Alt+F4 to quit" etc.), Esc-cancel hint, page intro text.

---

## 7. Shared list: "Kept apps" for Quit all (`QuitAllExceptionsList.swift`)

Used by the Quick Toggles / Quit apps page (outside this file's pages) but implemented in this folder. `DisclosureGroup`; label `QuickToggleFeatureStrings.exceptionsTitle` = "Kept apps" + count of visible entries (secondary, monospaced digit). Initially expanded only if the stored list contains an entry beyond the mandatory ones. Rows (icon 18, name, spacer): mandatory entries show `lock.fill` (tertiary) instead of a remove button (`actionRemove`="Remove" accessibility label for the others); sorted by name case-insensitively; Phone is hidden when not installed. Button `Label("Add app…", "plus")` (`autoQuitAddApp`, small) → AppPicker (excluding listed apps; installed apps only; no browse/running). Caption `exceptionsCaption`: "Quit all leaves these apps running. Finder, Vorssaint and Phone are always kept. You can still quit a kept app from its own row." [WIN] "File Explorer and Trayloom are always kept."
Key `quitAppsKeptApps` ([String], default = mandatory ids `com.apple.finder`, `com.apple.mobilephone`). Related shortcut keys: `quitAppsShortcutEnabled`/`quitAppsShortcut` (⌃⌥⌘Q) and `quitAppsExceptFrontShortcutEnabled`/`quitAppsExceptFrontShortcut`.
Windows: mandatory = explorer.exe + Trayloom (+ the shell host list in 5.4); quit = `CloseMainWindow` + wait, never kill. **Gap:** not present in Trayloom.

---

## 8. Page "Debounce" (`KeyboardDebounceSettings.swift`)

Sidebar entry: `keyDebounceName` = "Debounce", symbol `keyboard` (Fluent `U+E765` Keyboard). Hub: title "Debounce", description "Ignore accidental double key presses". Grouped `Form`.

### 8.1 Layout
1. **Section "Debounce"**:
   * Toggle "Filter duplicate keys" (`keyboardDebounceEnabled`, false). onChange: sync; when turned on and Accessibility is missing → request + open the Accessibility settings.
   * caption "Filters very fast duplicate key presses."
   * if on and running: `Label("Filter active", "checkmark.circle.fill")` caption green.
   * `Stepper` row (disabled while off): left "Global window", right `"{n} ms"` (secondary, monospaced digit); range 0…500, step 1; key `keyboardDebounceWindowMs`, default **5**; out-of-range → 5; every change syncs the service.
2. **Section "Specific keys"** (disabled while the feature is off):
   * caption "Per-key values override the global window. Use 0 ms to stop filtering a key."
   * add row `HStack(spacing 8)`: `Picker` label "Key" (min width 160; first item "Key" = none selected, then the catalogue below), `Stepper` showing `"{n} ms"` (monospaced; range 0…500 step 1; starts at the sanitized default 5), button "Add key" (disabled when off or no key selected) → sets/overwrites that key's window.
   * if no overrides: tertiary caption "No specific keys configured."; else one row per override sorted by label (string order): key label (12 semibold, width 70), Stepper `"{n} ms"` (secondary monospaced, 0…500), trash button (`trash`, borderless, tooltip "Remove key").
   * Storage `keyboardDebounceKeyWindows` = `"keyCode:ms,keyCode:ms"` sorted by key code (default ""); entries that fail to parse are dropped; windows sanitized to 0…500.
3. **Section "Permission required"** (Accessibility) when on and missing (macOS-only).
Strings: `keyDebounceRemoveKey` "Remove key", `keyDebounceWindowLabel` "Window" (unused label), `keyDebounceKeyLabel` "Key".

### 8.2 Key catalogue (`KeyboardDebounceKeyCatalog.common`, display order)
Letters A…Z (mac key codes A 0, B 11, C 8, D 2, E 14, F 3, G 5, H 4, I 34, J 38, K 40, L 37, M 46, N 45, O 31, P 35, Q 12, R 15, S 1, T 17, U 32, V 9, W 13, X 7, Y 16, Z 6) · digits 0…9 (0 29, 1 18, 2 19, 3 20, 4 21, 5 23, 6 22, 7 26, 8 28, 9 25) · "Space" 49 · "Return" 36 · "Tab" 48 · "Delete" 51 · "Escape" 53 · "," 43 · "." 47 · "/" 44 · ";" 41 · "'" 39 · "-" 27 · "=" 24 · "[" 33 · "]" 30 · "\" 42 · "`" 50. An unknown stored code displays as "#<code>".
[WIN] store Windows virtual-key codes instead (never mac codes): A…Z = 0x41…0x5A, 0…9 = 0x30…0x39, Space 0x20, Return(Enter) 0x0D, Tab 0x09, "Delete" → label **"Backspace"** 0x08, Escape 0x1B, "," 0xBC, "." 0xBE, "/" 0xBF, ";" 0xBA, "'" 0xDE, "-" 0xBD, "=" 0xBB, "[" 0xDB, "]" 0xDD, "\" 0xDC, "`" 0xC0 (OEM codes are layout-dependent; prefer scan codes for stability: `MapVirtualKey(vk, MAPVK_VK_TO_VSC)`). Unknown code label "#<code>".

### 8.3 Behaviour (exact algorithm, `KeyboardDebounceState`)
Per key code: `isDown`, `lastAcceptedPress`, `lastAcceptedRelease`, `lastEventTimestamp`; global `lastAcceptedKeyCode`. Window for a key = per-key override else global; window 0 = never filter that key. When the feature is off all state is dropped.
* Key-up: if the key was down → mark up and remember the release time; never suppressed.
* Key-down with the auto-repeat flag → mark down, pass.
* Key-down otherwise: (a) if the key is currently down and the previous accepted press was < window ago → **suppress**; (b) else if its last accepted release was < window ago **and the last accepted key overall is this key** → suppress (so typing a different key in between resets the chatter filter); (c) otherwise accept (record press, set down, remember `lastAcceptedKeyCode`).
* State of a key resets when the timestamp goes backwards or after a 5 s gap (stale).
* Suppressed key-downs are swallowed; key-ups are never suppressed (a lone key-up passes harmlessly). Events the app posts itself (Quit protection confirmation, snippet retyping; tag `OwnKeyEvent`) are never filtered.

### 8.4 Windows mapping
`WH_KEYBOARD_LL`, highest priority (Trayloom `HookPriorities.KeyDebounce = 500`). `KBDLLHOOKSTRUCT.time` is a ms tick; use `QueryPerformanceCounter` at hook entry for sub-ms precision (the default window is 5 ms). The LL hook has no auto-repeat flag: treat a key-down for a key already `isDown` (no key-up received) as auto-repeat (pass). Ignore injected events (`LLKHF_INJECTED`) unless from another remapper? Swift ignores nothing special - Trayloom ignores its own tagged events only. Return 1 to swallow suppressed down events. No permission. Keep a 5 s stale reset.

### 8.5 Gap vs Trayloom (`DebounceFeatures.cs`, class `KeyDebounce`, id `key-debounce`, name "Keyboard debounce")
Has: single `windowMs` slider 5…250 (default 40) and a note "Fast typists can double-tap a letter in about 80 ms…". Missing: titles ("Debounce", "Filter duplicate keys", "Filter active"), stepper UI with step 1 and range 0…500, **default 5 ms**, "Global window" label, the whole "Specific keys" section (picker, add/remove, per-key override storage, 0 ms = off), the exact algorithm if it differs (verify the "last accepted key overall" rule and the 5 s stale reset).

---

## 9. Page "Super key" (`SuperKeySettings.swift`)

Sidebar entry: `SuperKeyStrings.pageTitle` = "Super key"; icon = the selected source's symbol (capslock / command / option / control / shift - `SuperKeySource.systemImage`: `capslock`, `command`, `option`, `control`, `shift`). Hub description "Turns one key into the modifier combination you choose." Grouped `Form`.

### 9.1 Layout
1. **Section "Super key"**:
   * Toggle "Use this key as the super key" (`superKeyEnabled`, false). On enabling without Accessibility: request + open Accessibility settings.
   * `Picker` "Key to hold" (menu), items from `SuperKeySource.allCases`: "Caps Lock" · "Right ⌘" · "Right ⌥" · "Right ⌃" · "Right ⇧" (format `rightKeyFormat` "Right %@" with the modifier symbol; `capsLockKey` = "Caps Lock"). Key `superKeySource`, values `capsLock`/`rightCommand`/`rightOption`/`rightControl`/`rightShift`, default `capsLock`.
   * caption "Hold it and press any key. Choose one or more modifiers below."
   * `Label("Keep this key set to its default action in System Settings › Keyboard › Modifier Keys.", "info.circle")` caption secondary (macOS-only).
   * **Diagram** (centred, vertical padding 4, opacity 0.5 while off, animated 0.18 s): `HStack(spacing 10)`: left a keycap (min 84x30, radius 7, fill primary 6 %, stroke primary 12 %, 12pt medium; for Caps Lock with the `capslock` glyph 11pt semibold + label) with the hint "Hold" below (10pt tertiary); `arrow.right` (11pt semibold tertiary, bottom padding 14); four modifier keycaps ⇧ Shift · ⌃ Control · ⌥ Option · ⌘ Command (min 30x30, r7; selected: accent text, fill accent 14 %, stroke accent 45 %; unselected: secondary text, fill primary 4 %, stroke primary 10 %; tooltip = name; spacing 5; bottom padding 14). Toggling is disabled while the feature is off or when deselecting would remove the last of Control/Option/Command (a primary modifier is mandatory; Shift alone is not allowed). Key `superKeyModifiers`, default all four = `control+option+shift+command`; storage tokens joined by `+`; invalid → default.
   * status line (one of): enabled & mapping failure → orange `exclamationmark.triangle.fill` + failure text; enabled & paused because a listed app is open → `pause.circle` "Paused while a selected app is open"; enabled & running → green `checkmark.circle.fill` "Working now". Failure texts: "Another app’s key mapping uses the selected key. Remove it in that app: quitting it is not enough." · "macOS refused the key mapping. Reconnect the keyboard or restart the Mac, then switch this on again." · "macOS would not let Vorssaint watch the keyboard. Turn Vorssaint off and on in System Settings › Privacy & Security › Accessibility, then switch this on again."
2. **Section "A tap on its own"** (disabled while off): radio group (labels hidden) with caption "What a quick tap does when no other key is pressed." Items: "Nothing" (`none`, default) · "Turn capitals on and off" (`capsLock`) · "Switch input source; hold for Caps Lock" (`inputSource`) · "Press Escape" (`escape`). Key `superKeySoloAction`.
3. `MouseExceptionsList(.superKey)` when on (key `superKeyExceptions`, caption in 0.10).
4. Permission section when on and missing.
Related strings: `panelCaptionFormat` "%1$@ holds %2$@." , `manageButton` "Set up…", `holdHint` "Hold".

### 9.2 Behaviour
* Mac: the source key is remapped by the keyboard driver (`hidutil` UserKeyMapping) to F18 (HID usage 0x70000006D, key code 79) because Caps Lock cannot be "held" otherwise; the event tap then swallows F18 and ORs the chosen modifiers into every key event (and mouse press/drag) while it is down. Mapping is owned/marked (persistent marker) and always removed on off/quit/crash; foreign mappings on the same source key refuse activation; a held-key watchdog (re-checks real key state) releases stuck modifiers; mouse tap failures retry once.
* Solo tap (no other key pressed during the hold): performs the selected action (toggle Caps Lock, switch input source - tap switches, hold toggles Caps Lock -, send Escape).
* Pause list: while any listed app is running (even in background) the feature stops and the key behaves normally.

### 9.3 Windows mapping
* No remap needed: `WH_KEYBOARD_LL` sees `VK_CAPITAL`, `VK_RMENU` (Right Alt), `VK_RCONTROL`, `VK_RWIN`, `VK_RSHIFT` down/up (extended-key flag `LLKHF_EXTENDED` distinguishes right-hand keys). Sources [WIN]: "Caps Lock", "Right Win", "Right Alt", "Right Ctrl", "Right Shift" (labels "Right ⌘"→"Right Win", "⌥"→"Alt", "⌃"→"Ctrl", "⇧"→"Shift"). Beware AltGr layouts: Right Alt arrives as Left-Ctrl+Right-Alt - detect via `VK_RMENU` + the fake `VK_LCONTROL` (scan code 0x21D) and swallow both; warn or disable that source when the layout has AltGr (`GetKeyboardLayout` check).
* While held: swallow the source key down/up (also stop Caps Lock toggling), and when another key goes down inject the chosen modifier key-downs (`SendInput`, tagged) before it and key-ups after it; the mouse hook adds modifiers to button presses (inject modifier down before WM_xBUTTONDOWN and up after). Win modifier injected alone opens Start on release: inject VK 0xE8 between down and up. Watchdog: `GetAsyncKeyState(source)` poll every 250-500 ms to release stuck modifiers.
* Solo actions: Caps → inject `VK_CAPITAL` (the hook's own tag so it is not swallowed); input source → `Win+Space` (inject) or `ActivateKeyboardLayout`/`PostMessage(WM_INPUTLANGCHANGEREQUEST)` to the foreground window; hold for Caps Lock; Escape → inject `VK_ESCAPE`.
* Failure messages [WIN]: "Another app’s key mapping uses the selected key. Remove it in that app: quitting it is not enough." (PowerToys Keyboard Manager / registry `Scancode Map` at `HKLM\SYSTEM\CurrentControlSet\Control\Keyboard Layout`; detect by reading the registry value and by low-level probing) · "Windows refused the key mapping…" (unused) · "Trayloom could not watch the keyboard. Turn Trayloom off and on, then switch this on again." (hook install failed). The "Modifier Keys" note is macOS-only: omit, or show on Windows only when a Scancode Map exists for the chosen key.
* Elevated windows ignore injected input from a non-elevated process (UIPI); the page cannot fix it.

### 9.4 Settings
| Key | Type | Default |
|---|---|---|
| `superKeyEnabled` | Bool | false |
| `superKeySource` | String | `capsLock` |
| `superKeyModifiers` | String | `control+option+shift+command` |
| `superKeySoloAction` | String | `none` |
| `superKeyExceptions` | [String] | [] |

### 9.5 Gap vs Trayloom (`SuperKeyFeature.cs`, id `super-key`)
Has: a choice list of keys (default Caps Lock) with note, four toggles Ctrl/Alt/Shift/Windows (all on), "A quick tap sends Escape" toggle. Missing: the page title/strings ("Use this key as the super key", "Key to hold", "Hold it and press any key…"), the diagram with keycaps, "A tap on its own" radio (Nothing / Turn capitals on and off / Switch input source / Press Escape - Trayloom has only Escape on/off), status line ("Working now"/failure/"Paused while a selected app is open"), exceptions list, mouse modifier injection (verify), icon changes with the source. Requirement "at least one primary modifier" (Shift alone invalid).

---

## 10. Page "Text snippets" (`TextSnippetsSettings.swift`, `DateVariableBuilder.swift`, `UI/SnippetLibraryView.swift`)

Sidebar entry: `SnippetFeatureStrings.pageTitle` = "Text snippets", symbol `text.append` (Fluent `U+E8D2`? use `U+E8E9`/"TextBox" - look up "Font"/"InsertText"). Hub description: "Short triggers expand into full text". Grouped `Form`.

### 10.1 Layout (page)
1. **Section (no header)**:
   * Toggle "Expand snippets while typing" (`textSnippetsEnabled`, false) + caption "Type a trigger anywhere and it becomes its text. Everything stays on this Mac." [WIN] "…this PC."
   * if on and no Accessibility: Accessibility `PermissionRow` (macOS-only).
   * if on: Toggle "Play a sound when a typed trigger expands" (`snippetSoundEnabled`, false) + caption "A short system sound plays each time a typed trigger expands."; if that is on: `Picker` "Sound" listing the system alert sounds (names from `/System/Library/Sounds/*.aiff`, localized display names, sorted per language; fallback list Basso, Blow, Bottle, Frog, Funk, Glass, Hero, Morse, Ping, Pop, Purr, Sosumi, Submarine, Tink) plus an extra row "Sound unavailable" when the stored name no longer resolves; key `snippetSoundName`, default "Tink"; picking previews the sound.
2. **Section, header "Quick snippet menu"**: Toggle "Open snippets from a menu" (`snippetLibraryEnabled`, false) + caption "The shortcut opens a searchable menu. Picking a snippet types it right where your cursor is."; if on: `ShortcutPreferenceRow(role: .snippetLibrary)` (key `snippetLibraryShortcut`, default ⌃⌥⌘L, label "Shortcut") + orange "macOS rejected this shortcut. Choose another one." on failure.
3. **Section** with `SecureInputRow` ("Secure input is on" ...; macOS-only) when (expansion || library) on and secure input is held. Typing variant strings: "%@ was the active app when it turned on and may be holding it, so Vorssaint cannot type for you. Dismiss its password field, or quit it, to release it." / "The app holding it cannot be named, so Vorssaint cannot type for you. Dismiss any open password field, or log out and back in, to clear it." / "The app holding it could not be identified, so Vorssaint cannot type for you." / button "Show %@".
4. **Section (list)**: empty caption "No snippets yet. Add the first one."; rows sorted for display (alphabetically by the shown title - name, or trigger if unnamed - stored order breaks ties); button `Label("Add snippet","plus")`; footer (caption secondary, three lines): "Variables: {{date}}, {{time}}, {{datetime}}, {{clipboard}}" / "They become the date, the time and the copied text at the moment of expansion." / "A format after a colon picks how they look, like {{date:yyyy-MM-dd}}. A -tz(...) part sets the timezone, like {{date-tz(America/New_York):yyyy-MM-dd}}."
   **Row**: button area { title (medium; name, or the trigger when name empty) + trigger pill (caption monospaced, padding 5x1, radius 4, fill primary 7 %) + optional folder label (`folder` icon, caption2, secondary, 1 line); preview (caption, secondary, 1 line, newlines → spaces) } · spacer · mode text caption2 tertiary ("Right away" / "After space, Tab or Return") · small switch (enables/disables that snippet). Click row = edit sheet.
5. Sheets: new / edit (below).

### 10.2 Snippet editor sheet (440 wide, padding 18, `VStack(spacing 14)`)
Title (headline) "New snippet" / "Edit snippet". `Form` (columns style, label left):
* TextField "Name" (prompt "Personal email")
* TextField "Trigger" (prompt ";email", monospaced)
* Picker "Expand": "After space, Tab or Return" (`afterDelimiter`, default for new) / "Right away" (`immediate`)
* Toggle "Ignore capitalization"
* "Folder": TextField (prompt "Work") + (if other folders exist) a folder-icon menu listing existing folder names (alphabetical, distinct); choosing one fills the field
* Toggle "Show in the quick menu" (`showsInLibrary`, default true)
* Block: header row "Text" + trailing button `Label("Insert date/time" | "Edit date/time", "calendar.badge.plus")` ("Edit" when the caret is inside an existing date token) that opens the Date popover (below); a plain-text editor (min height 76, border primary 12 %, radius 6, no rich text); caption "Variables: {{date}}, {{time}}, {{datetime}}, {{clipboard}}"; caption "A format after a colon picks how they look, like {{date:yyyy-MM-dd}}, or use the date/time button above. A -tz(...) part sets the timezone, like {{date-tz(America/New_York):yyyy-MM-dd}}."
Validation captions (orange): trigger non-empty but < 2 characters (after stripping whitespace): "The trigger needs at least 2 characters."; duplicate (case-insensitive when either snippet ignores case): "Another snippet already uses this trigger.".
Buttons: [Delete (destructive, edit only)] spacer [Cancel] [Save (prominent; disabled when trigger < 2 chars, duplicate, or replacement empty)]. Save sanitizes: trigger = whitespace removed and truncated to 40 characters; name trimmed; folder trimmed (whitespace-only → none).
Extra strings: `replacementPlaceholder` "myemail@example.com" (defined, unused in the editor), `deleteButton` "Delete", `saveButton` "Save", `manageButton` "Manage snippets".

### 10.3 Date/time popover (`DateVariableBuilder`, width 340, padding 16, `VStack(spacing 12)`)
1. Segmented picker "Type": "Date" · "Time" · "DateTime" (default: detected token kind, else DateTime).
2. Menu picker "Style": "Short" · "Medium" · "Long" · "Full" · "ISO 8601" (default for a new token) · "Custom".
3. For Short/Medium/Long/Full: caption "A named style saves the format your Mac’s region uses right now." [WIN] "…your PC’s region…".
4. "Timezone" block: label caption; either the chosen IANA id (monospaced) with a clear `xmark.circle.fill` button (tooltip "Clear timezone") or the text "Device default" (body secondary); a search TextField "Search timezones" (Return confirms a unique match) with a trailing check (green, "Valid timezone") / cross (red, "Unrecognized timezone") shown only once the query settled (single match or none); a suggestion list under it (lazy, monospaced ids, 2 px row padding, height `min(140, rows·22)`, not capped in count). Search: case- and separator-insensitive ("New_York" = "new york"), matches identifiers and common abbreviations (e.g. "PST"), ranked so a typed city leads; "resolved" = exact identifier/abbreviation or the only remaining candidate; never a raw UTC offset.
5. If Style = Custom: caption "Pattern" + monospaced TextField "Pattern".
6. Row: caption "Preview" … monospaced preview of *now* formatted with the exact expansion code path.
7. Buttons: [Cancel] ("Cancel" `uninstallerCancel`) and prominent "Insert" (new) / "Update" (editing an existing token); disabled when Custom with an empty pattern.
Token written: `{{kind:pattern}}` or `{{kind-tz(IANA):pattern}}`. ISO 8601 patterns: date `yyyy-MM-dd`; time `HH:mm:ssXXX`; datetime `yyyy-MM-dd'T'HH:mm:ssXXX`. Named styles resolve to the current locale's ICU pattern for that kind/style (date only: dateStyle; time only: timeStyle; datetime: both with the same style) at the time of building. Editing an existing token re-detects kind, tz, and the matching style (falls back to Custom); the insert replaces the token (edit) or the selection/caret (new), keeping undo and placing the caret after it.

### 10.4 Expansion engine (service side, needed to reproduce behaviour)
* Buffer of the last 64 typed characters; reset on whitespace/delimiters, clicks, focus changes, navigation keys. Trigger max length 40 (sanitized), min 2. Modes: `immediate` (fires when the last trigger character is typed; that key is swallowed) and `afterDelimiter` (fires when Space, Tab, Return or newline follows the trigger; the delimiter is typed after the replacement). Longest trigger wins; case-insensitive compare only for snippets with "Ignore capitalization". Only enabled snippets participate.
* Replacement: the typed trigger is erased with the exact number of backspaces; a text field with inline autocompletion (browser address bars) may need one more backspace (selection after the caret); then the replacement is typed (single-line) or pasted through a transient pasteboard (when it contains a newline) and the clipboard restored; `{{clipboard}}` content is read last so pasted text is never re-expanded; unknown `{{tags}}` stay visible; secure input / password fields: nothing is typed.
* Variables: `{{date}}` = locale medium date, `{{time}}` = locale short time, `{{datetime}}` = "date time", `{{clipboard}}`; plus `{{date:PATTERN}}`, `{{time:PATTERN}}`, `{{datetime:PATTERN}}` and `-tz(IANA)` variants (locale of the user for month/weekday names).
* Sound: if enabled, the chosen alert sound plays when a typed expansion happens, after the paste lands (the quick menu's insertion path reuses the same typing routine with 0 backspaces; the library service itself only beeps on failures).
* Quick snippet menu (`SnippetLibraryView`): borderless floating panel 460 wide, radius 22, high-contrast HUD backdrop, centred on the screen with the pointer a bit above the middle; search field (magnifier glyph, 15pt, placeholder "Search snippets", clear `xmark.circle.fill` when non-empty; padding 16x13) · divider · list (max height 330; folder headers `folder` 10pt + name caption semibold secondary, top padding 4/10; rows: name (13 medium) + trigger pill + preview, selected row accent 18 % with a `return` glyph, hover selects) · divider · footer ("↩ inserts · esc closes" caption tertiary, trailing "Manage snippets" caption button). Empty library: `text.badge.plus` 28pt + "Nothing to show yet. Add snippets, or turn on “Show in the quick menu” for the ones you use most." + button "Manage snippets"; no match: "No snippet matches the search." Search matches name, trigger, text and folder (case/diacritic-insensitive); sections = folders alphabetically first, then loose snippets; only enabled snippets marked "Show in the quick menu". The panel never activates the app: Enter (or click) types the snippet into the previously active app after waiting (15 ms steps, ≤ 1.5 s) for modifiers to be released; Esc closes; arrows move; ⌘1…9 quick picks.

### 10.5 Settings
| Key | Type | Default |
|---|---|---|
| `textSnippetsEnabled` | Bool | false |
| `textSnippets` | Data (JSON array of `{id: UUID, name, trigger, replacement, expansion: "immediate"|"afterDelimiter", enabled, ignoresCase, folder, showsInLibrary}`; missing optional fields decode as false / "" / true) | empty |
| `snippetLibraryEnabled` | Bool | false |
| `snippetLibraryShortcut` | String | ⌃⌥⌘L (`control+option+command:37`) |
| `snippetSoundEnabled` | Bool | false |
| `snippetSoundName` | String | `Tink` |

### 10.6 Windows mapping
* Keyboard hook (`WH_KEYBOARD_LL`, Trayloom priority 100 "observe only", but swallowing the final key for `immediate` mode requires it to return 1): translate keys with `ToUnicodeEx` (keyboard layout of the foreground thread via `GetKeyboardLayout(GetWindowThreadProcessId(fg))`, do not alter dead-key state: use the `wFlags` bit 2 trick or `ToUnicodeEx` with `(1<<2)`); clear on mouse clicks, window change, Ctrl/Alt/Win chords, navigation keys. Backspaces and text via `SendInput` (`KEYEVENTF_UNICODE` for text; newlines as Enter; use clipboard paste (`Ctrl+V`) with save/restore when the replacement contains a newline - same rule). Password fields: skip when the focused control is `ES_PASSWORD` (UIA `IsPassword`) or the secure desktop.
* Sound: Windows has no `/System/Library/Sounds`; list `%WINDIR%\Media\*.wav` (names minus extension, sorted), fallback "Windows Ding", "Windows Notify", "chimes"; default `snippetSoundName` → first of ("Windows Notify System Generic", "Windows Ding", first file); play with `System.Media.SoundPlayer`/`PlaySound`; label "Sound unavailable" row unchanged.
* Date patterns: Vorssaint uses ICU `DateFormatter` patterns (e.g. `yyyy-MM-dd`, `HH:mm:ssXXX`, `EEEE`, `a`). .NET custom formats differ (`dddd`, `tt`, `zzz`, `fff`); implement a small ICU-pattern formatter (letters y M L d E e a h H K k m s S Z X z, quoted literals `'…'`) on `DateTimeOffset` and `TimeZoneInfo` rather than passing patterns to `ToString`. Named styles (Short/Medium/Long/Full) on Windows map to `CultureInfo.CurrentCulture.DateTimeFormat` (`ShortDatePattern`, `ShortTimePattern`, `LongDatePattern`, …) converted to ICU syntax at build time and stored in the token (keeps "saved pattern" semantics). Time zones: `TimeZoneInfo.FindSystemTimeZoneById` accepts IANA ids on .NET 6+ with ICU; list via `TimeZoneInfo.GetSystemTimeZones()` + `TimeZoneInfo.TryConvertWindowsIdToIanaId`.
* Library panel: borderless topmost WPF window with `WS_EX_NOACTIVATE`, `Hotkeys` service, typed into `GetForegroundWindow` previously active window.
* Permission and secure-input rows: none on Windows (omit); UIPI note applies to elevated targets.

### 10.7 Gap vs Trayloom (`TextSnippetsFeature.cs`, id `text-snippets`)
* Has: hook-based expansion with a 64-char buffer, `Snippet {Trigger, Text}`, defaults `;sig` and `;shrug`, "Ignore capitalisation of triggers" (global toggle, not per snippet), "Only expand at the start of a word", triggers 2…32 chars, a `SnippetEditor` list control, typed-Unicode expansion (newlines as Enter).
* Missing: per-snippet fields (name, expansion mode immediate/after delimiter, ignore-case per snippet, folder, show in quick menu, enabled switch); trigger limit 40; variables `{{date}}`, `{{time}}`, `{{datetime}}`, `{{clipboard}}`, formatted dates and `-tz()`; date/time builder popover; sound toggle/picker; Quick snippet menu (shortcut ⌃⌥⌘L equivalent, panel); empty-list text and footers; display sorting; default content differs (Vorssaint ships no default snippets). Trayloom's "needBoundary" option is not in Vorssaint (remove or hide).

---

## 11. Cross-cutting summary

### 11.1 Installed by default (hub availability, `featureAvailable.<id>`)
Installed on first run: switcher, dockPreview, dockClick, windowMaximizer, windowLayout, autoQuit, scrollInverter, smoothScroll, mouseAcceleration, mouseNavigation, mouseButtonShortcuts, middleClick, mouseClickDebounce, keyboardDebounce, textSnippets, superKey, quitWindowProtection (Quit & close protection).
Opt-in (not installed by default): focusFollowsMouse, scrollHorizontal, scrollZoom, linearScroll, spacesOrder, quitApps (Quit all). (Each toggle inside the pages above still defaults off unless stated; installed ≠ enabled. Trayloom's `InstalledByDefault` flags should follow this list, noting that on Windows windowMaximizer and spacesOrder have no behaviour.)

### 11.2 Feature → hub identifiers to keep consistent (suggested Trayloom ids)
`invert-scroll`, `horizontal-scroll`, `scroll-zoom` (new), `focus-follows-mouse`, `smooth-scroll`, `linear-scroll`, `mouse-acceleration`, `side-buttons` (= mouseNavigation), `mouse-button-shortcuts`, `click-debounce`, `middle-click` (new), `app-switcher`, `dock-preview` (new, "Taskbar Preview"), `dock-click` (new, "Taskbar clicks"), `window-layout`, `window-maximizer` (macOS-only, hidden), `auto-quit` (new), `close-protection` (existing), `key-debounce`, `super-key`, `text-snippets`, `spaces-order` (macOS-only, hidden).

### 11.3 macOS-only items (with the proposed Windows outcome)
| Item | Outcome |
|---|---|
| Spaces order | Hidden (Windows virtual desktops do not self-reorder). |
| Green-button maximizer & exclusions | Hidden. |
| Stage Manager space reservation | Hidden (optionally an auto-hide-taskbar equivalent, not in the source). |
| Hide app on Dock click | Treat as minimize-all or hide the row. |
| Treat hidden apps like minimized (switcher) | Hide the row. |
| Secure-input rows | Hidden. |
| Permission rows (Accessibility/Screen Recording) | Hidden; UIPI/elevation note instead. |
| Three-finger-drag conflict (middle click) | Replaced by "Windows three-finger gestures are assigned…". |
| Mission Control / App Exposé drag | Task View (Win+Tab) and Show desktop (Win+D); desktops via Ctrl+Win+←/→. |
| Pinch Zoom (magnify gesture) | modifier + wheel → Ctrl+wheel. |
| Window "Full Screen" action | F11 / monitor-sized borderless fallback. |

### 11.4 Prioritised implementation checklist
1. Shared UI: `SettingsCard`/`SettingsRow` look (0.2/0.3), chips (0.4), `AppBundleList` + `AppPicker` (0.9), slider row, shortcut row; page scaffolds for the card pages (Mouse, Switcher, Taskbar) and grouped-form pages.
2. Mouse page: restructure into one page with legend + cards; add missing Zoom + Middle click (touchpad) later; exceptions lists on every wheel/mouse feature; align defaults/ranges (click filter 25 ms 5…100; key debounce 5 ms 0…500; focus follows delay 250 100…3000; smooth 40/65/0; linear 3 lines).
3. Mouse button shortcuts: dynamic list + capture flow + side wheel + desktop-drag.
4. Switcher: 3 layouts, Apps/Windows/All-windows shortcuts with Alt+Tab take-over, options card, per-app rules, thumbnail card.
5. Window layout: complete action set (37 placements) with Remove/Reset/"try" rows, gaps (two pickers), margin maximize, side-repeat cycle, ignore list, pointer-to-display, directional trigger, gestures + edge snap with the zone map.
6. Quit on close, Quit protection (per-shortcut), Debounce per-key, Super key page (diagram, solo actions), Snippets (model + editor + date builder + quick menu).
7. Taskbar Preview / Taskbar clicks (new features).
