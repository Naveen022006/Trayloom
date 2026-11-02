# 02 - Settings window, Features hub, Permissions, onboarding, app-wide behaviour

Source of truth: `vorssaint-mac/Sources/Vorssaint` (`UI/Settings/*`, `Core/FeatureCatalog.swift`,
`Core/FeaturePresets.swift`, `Core/FeatureHubStrings.swift`, `App/FeatureRuntime.swift`,
`App/AppDelegate.swift`, `UI/Onboarding/*`). Reference images: `docs/assets/readme/features-hub.png`,
`permissions.png`. English strings are verbatim; "Vorssaint" is written **WinKnife**.

This file is the **index + core**. The per-page detail is split into sibling files, each following
SPEC_RULES (Layout / Strings / Settings / Behaviour / Windows mapping / Gap):

| File | Pages |
|---|---|
| `02a-settings-general-energy-monitor.md` | General (+ Menu bar, Volume mixer, Output switcher, Audio device priority, Music app blocker sub-pages), Energy (Keep awake, Displays, Extra brightness, Bluetooth on sleep), Monitor (+panel config, alerts, tokens), Keyboard shortcuts, Advanced, shared components, glyph table |
| `02b-settings-mouse-windows-keyboard.md` | Mouse & Trackpad, Mouse buttons, Switcher, Dock, Window layout, Quit on close, Quit & close protection, Debounce, Super key, Text snippets |
| `02c-settings-clipboard-tools-capture.md` | Clipboard, Cut & paste (+Rename), Shelf, Clean URL, Quick tools, Screen capture (Screenshot, Recorder, OCR, Color), Command Bar, Radial menu |
| `02d-settings-island.md` | Dynamic Island page (Layout/Content/Activity/Behavior/Companion tabs), layout/content editors, AI agents + accounts, calendar selection |
| `02e-settings-apps-cleaner-about.md` | App updates, Homebrew, Cleaner (+WhatsApp downloads), Uninstaller, Kill Process, Port Manager, Media, About, What's New, Support, update windows, Feedback |

This file covers: the window chrome, search, sidebar (exact order), Features hub, Permissions page, the
full feature catalog, install/uninstall runtime semantics, onboarding, app startup / tray menu / global
behaviour, and the macOS-only adaptation list.

---

## 1. Settings window chrome

### 1.1 Layout
- Native window titled **"WinKnife Settings"** (`settingsTitle`). Style: titled, closable, minimizable,
  resizable. Not restorable, does not hide on deactivate, `isReleasedWhenClosed=false` (the window object
  is kept; closing only hides it; the last page stays selected). The tray panel is **not** closed when Settings
  opens (it stays beside it as a live preview).
- Size: min content **772 x 528**. Default content size = saved size if both >= min, else **772 wide x
  min(838, screenVisibleHeight-40)** tall (never below 528). Size is persisted when resized
  (`settingsWindowWidth`, `settingsWindowHeight`, doubles, 0 = unset).
- Placement: on the screen that holds the tray panel if it is open, else the screen under the pointer; centered.
- Two columns (NavigationSplitView, "balanced"): **sidebar** width min 198 / ideal 210 / max 240 px; **detail**
  fills the rest. The detail column is wrapped so a page can never drive the window size (windows size is
  owned only by the rules above + user drag).
- Toolbar (leading/"navigation" placement), left to right: **Toggle sidebar** button (icon `sidebar.left`,
  tooltip "Hide Sidebar"/"Show Sidebar", accelerator Ctrl+Cmd+S -> on Windows **Ctrl+Alt+S**), **Back**
  (`chevron.backward`, tooltip "Back"), **Forward** (`chevron.forward`, "Forward"). Back/Forward disabled when
  no reachable history entry.
- Sidebar top: search field (pill, capsule, `.quaternary` 50% fill, padding 5/7, magnifier icon, placeholder
  **"Search settings"**, clear (x) button when non-empty; Esc clears). Below it a scrolling list, `.sidebar` list style.
- Detail pages are one of: a `ScrollView` with `SettingsCard`s (max content width 760, padding 22, cards spacing 20)
  or a grouped `Form`. See sec. 1.5 for shared card metrics.

### 1.2 Navigation model (router)
- Router keeps `page`, `destination (page + optional sectionAnchor)`, `sidebarFeature`, a one-shot
  `pendingFeatureTarget` (feature row to reveal inside the hub), plus one-shot hints `cleanerTool`, `notchModule`,
  `notchCompanion`.
- **History** (Back/Forward): a request to a *different page* appends an entry; a section-anchor request on the
  *same page* only refines the current entry, except on General and Energy where each anchor (tool) is its own
  visit. Back/Forward skip entries whose page is no longer visible (feature uninstalled). Request with
  `replacingVisit` swaps the current entry (used for fallbacks).
- Anything (hub row click, search result, tray, panel, Command Bar "open settings", permission nudge) navigates
  via `router.request(destination, targetFeature?, sidebarFeature?)`; every request publishes a fresh UUID so repeats re-fire.
- `ensureVisiblePage`: if the current page's gating features are all uninstalled, go to **Features**. If an
  anchored destination vanished: General -> plain General; Energy -> first available Energy tool.
- Companion ("Mascot") destination is redirected to the Dynamic Island page with the Companion tab flag.
- Mouse side buttons (Back/Forward) navigate when the Settings window is key, no sheet/modal is open and no
  mouse-button capture is active, and the button is not claimed by the radial menu / mouse-button shortcuts.
  The matching down/up/drag events are swallowed so the gesture stays together. A "Go" menu (Back/Forward)
  exists in the main menu (accelerators = platform Back/Forward keys, Alt+Left / Alt+Right on Windows).

### 1.3 Section anchor focus
Pages that host several tools (`SettingsSectionFocus`) scroll to the requested section and **tint it with an
accent highlight for ~1.2 s**; sections carry a `settingsSectionAnchor(id, cornerRadius: 16)`. Retries once after
the first layout turn. Reduce-motion: jump without animation.

### 1.4 Search ("Search settings")
- Index = (a) every sidebar **page** with its `keywords` (labels of options inside the page, localized), (b) every
  **feature** (title, destination = that feature's section), (c) the "Menu bar" panel-config entry (keywords: "Show
  menu bar icon" string, general menuBarIconTitle, "SF Symbols"), (d) "Keyboard light" shortcut entry if keyboard backlight exists.
  Pages that duplicate a feature 1:1 (e.g. Homebrew) are merged (page row carries the feature).
- Matching is case-, accent- and width-insensitive containment; ranking: exact title > title contains > keyword-only.
  Results are **grouped by page**: page row (bold) first (only if the page itself matches), then each matching
  option/utility indented 18 px; each result row is icon + title. Selection highlight = accent 18% fill, accent text.
- Keys while the field is focused: Up/Down move the selected result (wraps via `moveSelection`), Return opens it,
  no modifiers. First result is auto-selected whenever the query text changes. Selection index is reconciled when
  results change but the query didn't.
- Results hide features that are uninstalled **but** clicking one that is unavailable routes to **Features** and
  reveals/highlights that feature's row (`targetFeature`). Pages whose gating features are all off route to Features too.
- Clearing the search or routing to a destination (`router.requestID` changes) empties the query.
- The sidebar scrolls to the selected row (centered) after search ends or an external route arrives.

### 1.5 Shared card/row components (see 02a "shared components" for full metrics)
- `SettingsCard(title?)`: VStack spacing 13, padding 16, fill `.quaternary` 35%, corner radius 16 (continuous),
  optional `.headline` title.
- `SettingsRow(symbol, title, badge?, caption?, accessory)`: HStack spacing 12: **26x26 icon tile** (symbol 12pt
  semibold accent colour on accent 12% fill, radius 7), VStack(title, caption `.caption` secondary), Spacer(12), accessory
  (toggle `switch` style / picker / button). Optional badge: 8pt bold white on accent capsule (e.g. "BETA").
- `settingsRowTextInset = 38` (26 + 12): follow-up controls align to the text column.
- `SettingsFeatureSwitchRow`: a switch for an option that needs another feature; when that feature is uninstalled
  the row is greyed (45% opacity, desaturated, forced off) and a line "<enableReason>" + button **"Open Features"**
  (jumps to the hub row) is shown under it.
- `SettingsChoiceRow` (segmented beside title, falls back to under the title, then to a menu) and `SettingsMenuRow`.
- Windows: WPF/WinUI `Border` CornerRadius 16, theme brush `CardBackgroundFillColorDefaultBrush`-like at 35% alpha.

### 1.6 Windows mapping (chrome)
- Window: WinUI 3 / WPF window with Mica backdrop, caption title "WinKnife Settings", NavigationView-like left pane
  (use a custom list; a stock `NavigationView` can't do collapsible headers + search-result groups). Persist size/pos.
- Search: `AutoSuggestBox`-style inline search replacing the list (not a popup).
- History: own back/forward stack; XButton1/XButton2 via window message `WM_XBUTTONUP` / `WM_APPCOMMAND` (APPCOMMAND_BROWSER_BACKWARD/FORWARD).
- Highlight pulse: animated brush on the section container.

### 1.7 Gap vs WinKnife
`FeaturesWindow.xaml` is a 1000x660 two-pane window titled "WinKnife - Features": left 340 px grouped list + search ("Search features"),
right pane with feature name/description/Install button and the feature's own `CreateSettingsView()`. There is **no** sidebar of
settings pages, no category sections, no page-level search index/keywords, no router/history/back-forward, no section highlight, no
General/Features(hub with tabs)/Energy/Monitor/etc. pages, no persisted window size, no settings card design system beyond `SettingsCard.cs`.
The whole window must be rebuilt per this spec; per-feature `CreateSettingsView()` views can be reused inside the new pages.

---

## 2. Sidebar - exact structure

The sidebar is **dynamic**: rows are generated from `SettingsDirectory.sections()` + feature availability, then
re-organised (`sidebarSections`). A page with no available gating feature has no rows; empty sections are dropped.
Header rows are buttons (chevron.down / chevron.right 9pt semibold + title) that **collapse/expand** the section;
the section containing the selection auto-expands on external navigation. Row = HStack(spacing 9): icon (18 wide,
tint accent, `.primary` when selected) + title; tooltip = title. Sidebar selection resolves by: preferred
feature id -> anchored destination -> page row -> first row of the page.

Final order (English titles; icon = SF Symbol -> suggested Segoe Fluent glyph, `?` = verify in Character Map):

**Essentials**
1. General - `gearshape` -> Settings E713
2. Menu bar - `menubar.rectangle` (anchor `panelConfiguration` of General; on Windows rename **"Tray & panel"**) -> E7F4 ?
3. Features - `square.grid.2x2` -> GridView E80A
4. Dynamic Island - `macbook` (only if `notch` installed) -> E7F8 (Computer)? / custom
5. Monitor - `chart.line.uptrend.xyaxis` (if any monitor feature installed) -> E9D2 (Chart) ?
6. Fan Control - `fanblades.fill` (only if installed; N/A on Windows)

**Utilities**
- Command Bar `command` (page, if installed) -> E721 Search / custom
- Quick panel `wand.and.rays`, Quick toggles `togglepower`, Quit apps `xmark.app`, Camera preview `web.camera`,
  Wallpaper `photo.on.rectangle`, Scratchpad `note.text`, Cleaning Mode `bubbles.and.sparkles` (each = a feature row
  of the shared "Quick panel" page, only the installed ones; Quick tools has **no overview row**)
- Screenshot `camera.viewfinder`, Screen recording `record.circle`, Copy text from screen `text.viewfinder`,
  Color picker `eyedropper` (feature rows of the Screen capture page; no overview row)
- Radial menu `circle.grid.cross`

**Sound** (hub group title reused as section title)
- Music app blocker `music.note`, Volume mixer `speaker.wave.2`, Output switcher `hifispeaker`, Audio device priority `list.number`
  (feature rows of General, anchors `musicBlocking/mixer/soundOutputSwitcher/audioPriority`), Mute microphone `mic.slash`

**Energy and display**
- Keep awake `moon.zzz.fill`, Displays `display.2`, Extra brightness `sun.max.fill`, Bluetooth on sleep `wave.3.right.circle`
  (feature rows of the Energy page; no overview row; Energy page = one tool at a time)

**Windows and Dock** (rename **"Windows and taskbar"** on Windows - WinKnife already uses `WindowsTaskbar`)
- Window switcher `rectangle.on.rectangle`, Dock Preview `dock.rectangle`, Dock clicks `dock.arrow.down.rectangle`,
  Keep Spaces in a fixed order `rectangle.split.3x1`, Window layout `rectangle.3.group` (page row),
  Maximize windows `arrow.up.left.and.arrow.down.right`, Quit on close `xmark.rectangle`

**Mouse and keyboard**
- Invert mouse scrolling `arrow.up.arrow.down`, Scroll sideways while holding a key `arrow.triangle.swap`, Zoom
  `plus.magnifyingglass`, Trackpad middle click `hand.tap`, Focus follows mouse `cursorarrow.and.square.on.square.dashed`,
  Smooth scrolling `cursorarrow.motionlines`, Linear scrolling `arrow.up.and.down.text.horizontal`, Disable mouse acceleration
  `cursorarrow.rays`, Side buttons `arrow.left.arrow.right`, Mouse button shortcuts `button.programmable`, Extra click filter
  `cursorarrow.click` (all feature rows of the shared Mouse & Trackpad page; no overview row; installed ones only),
  Quit & close protection `shield.lefthalf.filled`, Debounce `keyboard`, Super key (icon depends on chosen source key),
  Text snippets `text.append`

**Files**
- Clipboard `doc.on.clipboard` (title taken from the Command-Bar "Clipboard" source string), Paste as plain text `doc.plaintext`,
  Cut & paste `scissors`, Rename shortcut `pencil`, Shelf `tray.full`, Media `photo.on.rectangle.angled`, Clean URL `link`

**App management**
- App updates `arrow.down.app`, Cleaner `sparkles`, Homebrew `shippingbox`, Uninstaller `trash`, Kill Process
  `xmark.octagon` (beta), Port Manager `network`

**App** (always shown)
- Keyboard shortcuts `command`, [Keyboard light `keyboard` - only if brightness installed and keyboard backlight supported],
  Advanced `wrench.and.screwdriver`, About `info.circle`, What's New `sparkles`, Support `heart.fill`

Section-order summary: Essentials -> Utilities -> Sound -> Energy and display -> Windows and Dock -> Mouse and keyboard
-> Files -> App management -> App. (`Categories`: essentials "Essentials", windowsControls "Window controls", files "Files",
utilities "Utilities", app "App", appManagement "App management". Sidebar section titles Sound / Energy and display /
Windows and Dock / Mouse and keyboard are the hub's group titles.)

> The README screenshots show an older sidebar (Essentials: General, Features, Energy, Monitor; "Window controls":
> Mouse & Trackpad, Switcher, Window layout, Quit on close; Files; Utilities). The **code above is the current
> truth**; build the dynamic structure. Page titles ("Mouse & Trackpad", "Switcher", "Energy", "Quick panel", "Screen capture")
> still exist as the *detail page titles*.

### Page -> gating features (page hidden when none installed)
Energy: keepAwake, brightness, extraBrightness, bluetoothSleep. Monitor: all 8 monitor/connected/fan features. Mouse: the 11
mouse features. Switcher: switcher. Dock: dockPreview, dockClick, spacesOrder. Window layout: windowLayout, windowMaximizer.
Quit on close: autoQuit. Quit protection: quitWindowProtection. Clipboard: clipboardHistory, pastePlain, finderCutPaste. Cut & paste:
finderCutPaste, finderRename. Shelf, Media(mediaTools), Clean URL, Cleaner, Homebrew, App updates, Uninstaller, Kill Process, Port Manager,
Debounce, Super key, Text snippets, Radial menu, Command Bar: their own feature. Quick tools: quickLauncher, quickToggles, quitApps,
micMute, cameraPreview, wallpaper, scratchpad, cleaningMode. Screen capture: screenshot, screenRecorder, screenOCR, colorPicker.
Dynamic Island: all 13 notch features. General, Features, Shortcuts, Advanced, About, What's New, Support: always.

### Feature -> settings destination
(feature rows open `page#anchor`.) switcher->Switcher#switcher; dockPreview/dockClick/spacesOrder->Dock#dock/dockClick/spacesOrder;
windowMaximizer->Window layout#windowMaximizer; windowLayout->Window layout; autoQuit->Quit on close; quitWindowProtection->Quit protection;
scrollInverter+scrollHorizontal->Mouse#scrollDirection; scrollZoom/focusFollowsMouse/smoothScroll/linearScroll/mouseAcceleration/
mouseNavigation/mouseButtonShortcuts/middleClick/mouseClickDebounce->Mouse#<same>; keyboardDebounce->Debounce; textSnippets->Text snippets;
superKey->Super key; clipboardHistory/pastePlain->Clipboard#; finderCutPaste/finderRename->Cut & paste#; shelf, urlCleaner, mediaTools,
cleaner, uninstaller, killProcess, portManager, homebrew, appUpdates->own page; diskImageInstaller->**Features** (no page; no chevron);
mixer/soundOutputSwitcher/audioPriority/musicBlock->General#mixer/soundOutputSwitcher/audioPriority/musicBlocking; micMute/quickLauncher/
quickToggles/quitApps/cameraPreview/wallpaper/scratchpad/cleaningMode->Quick tools#; colorPicker/screenOCR/screenshot/screenRecorder->Screen capture#;
keepAwake/brightness/extraBrightness/bluetoothSleep->Energy#; all notch* (except mascot)->Dynamic Island; notchMascot->Dynamic Island Companion tab;
radialMenu, commandBar->own page; monitorCPU/GPU/Memory/Network/Disk/Power/connectedDevices->Monitor; fanControl->Monitor#fanControl.
A feature whose destination is the Features page itself shows no chevron in the hub.

---

## 3. Features hub page (sidebar "Features", `FeatureHubSettings.swift`)

### 3.1 Layout (top to bottom; content max width 760, centered, padding 22, vertical spacing 20)
1. **Header block** (VStack spacing 5): `Features` (title2 bold). Under it, depending on tab, callout secondary:
   - Features tab: **"Install only what you use. Whatever you uninstall disappears from the whole app and stops loading."**
   - Permissions tab: **"What each permission does and which features use it. Other access is requested only when you use it."**
2. **Segmented control** (full width): `Features` | `Permissions` (label hidden; default Features).
3. **Restart card** (shown on either tab only while `needsRestartToUnload`): HStack spacing 12 padded 16, accent 12% fill, radius 16:
   icon `arrow.clockwise.circle.fill` 24pt accent; text callout **"Features uninstalled in this session stay loaded until the app restarts. Restart to unload them from memory now."**;
   prominent button **"Restart now"**.
4. (Features tab) **Summary card**: headline **"%1$d of %2$d features installed"** (e.g. "37 of 37 features installed" -
   N = installed, total = installable on this machine), spacer, buttons **"Install all"** (disabled when installed == installable)
   and **"Uninstall all"** (disabled when installed == 0); under it a full-width **linear progress bar** (value installed / total, accent tint).
5. (Features tab) **"Never turned on" card** (only if >= 3 candidates; see 3.4) or, right after an uninstall from it, a green-check card with
   the footer note text + **"Undo"** button.
6. **Dynamic Island card** (always at the top of the list): group header + the `notch` row + a disclosure **"What appears"**
   (`modules`, default expanded) containing the 12 extension rows separated by dividers (`Divider` inset 8).
7. **Bundles card** - disclosure titled **"Start with a bundle"** (default expanded) with 3 preset cards in an HStack (spacing 10,
   top aligned) and under them the caption **"One click sets the app up for how you use your Mac. Everything else stays one click away."**
   - Preset card: VStack spacing 8, padding 12, min height 138, fill primary 5%, radius 12: **34x34 icon tile** (symbol 16pt semibold accent
     on accent 12% fill, radius 9), name (13 semibold, 1 line), caption (`.caption` secondary), spacer, small **"Apply"** button.
   - Presets: **Essentials** (`star.fill`) "Volume mixer, system monitor and keep awake."; **Windows** (`macwindow.on.rectangle`) "Window switcher, window
     layout and the Dock features."; **Battery and quiet** (`battery.75percent`) "A lean monitor with battery, memory and processor. Nothing listens to input."
   - Apply opens an alert titled with the preset name: message **"Install the %1$@ bundle and uninstall the rest? Nothing is deleted, and everything comes back with one click."**
     buttons **"Apply bundle"** / **"Cancel"**.
8. **One card per group** (order: Windows and Dock, Mouse and keyboard, Clipboard and files, Sound, Energy and display, Tools, System monitor; the
   Dynamic Island group is the card in step 6). Each card is a disclosure (default **expanded**) whose label is the **group header**:
   26x26 icon tile (symbol 12pt semibold accent, accent 12% fill, radius 7) + group title (headline) + spacer + "`installed/total`" (caption monospaced digits) +
   64 px wide small progress bar. Group symbols: windowsDock `macwindow.on.rectangle`, mouseKeyboard `computermouse`, clipboardFiles `doc.on.clipboard`, sound `speaker.wave.2.fill`,
   energyDisplay `bolt.fill`, tools `wrench.and.screwdriver.fill`, dynamicIsland `macbook`, monitor `chart.line.uptrend.xyaxis`.
   The Monitor card shows caption **"With everything off, the Monitor leaves the panel and the menu bar."** when no monitor feature is installed.
   Collapsed groups remove their rows. Rows are separated by dividers inset 8.
9. **Footer** (caption, secondary): **"Uninstalling deletes nothing: the feature just leaves the app and stops loading. Install it again anytime and everything returns as it was."**
   then **"What the feature keeps alive while it is on. Uninstalled features load nothing at all."**

### 3.2 Feature row (`FeatureHubRow`)
HStack spacing 12, padding vertical 7 / horizontal 8, corner radius 10, background clear (accent 10% when highlighted by search; primary 4% on hover when clickable).
Contents left -> right:
- **Text block**: name (primary when installed, secondary when not) + badges inline; below, description (`.caption`, secondary; 70% when uninstalled). Whole block is a button
  (plain) when installed and the feature has a real settings page -> navigates to its destination (`router.request(dest, sidebarFeature: feature)`), trailing chevron (`chevron.forward` 10pt tertiary).
  Otherwise (not installed, or no page) it is static; unsupported hardware -> 40% opacity + desaturated.
- Badges: **EXPERIMENTAL** on Dynamic Island (9pt bold uppercase orange on orange 14% capsule, h-pad 7, v-pad 3); **BETA** on Kill Process and Fan Control (8pt bold white on accent capsule).
- **Metadata button** (plain, caption secondary): label is the energy label when the feature has no permissions, else **"Permissions"**; tooltip = permission names + energy label joined by newlines;
  click opens a popover (padding 14, grid spacing 8): one line per permission (icon + name) then one line with the energy icon(s) + energy label.
- **Install button**: installed -> bordered small **"Uninstall"**; not installed -> prominent small **"Install"**; not installable on this hardware -> disabled bordered "Install" with tooltip = reason.
- Uninstalling the Dynamic Island while extension rows are installed shows an alert **"Uninstall Dynamic Island"** - **"These extensions only work inside the Dynamic Island: %@. Uninstall them too? Nothing is deleted, and everything comes back with one click."** buttons **"Uninstall extensions too"**, **"Keep extensions"**, **"Cancel"**.
- Install/uninstall animates with easeOut 0.22 s.

> **Reference screenshot variant** (`features-hub.png`, older build): each row shows a **44x44 black rounded icon tile** with the white feature glyph at far left,
> then the name, a small **(i)** info button, a small **permission icon**, and a grey **pill badge** ("Listens to the keyboard" / "Listens to the mouse" / "Nothing at rest"),
> and the description under the name; "Uninstall" button at the right. The segmented control reads `Features | Permissions` inside the top card with the intro line and the
> "N of N features installed" line + **Install all / Uninstall all**. The current code collapses (i)+icon+pill into the single metadata button/popover above. **Recommendation:** implement the
> screenshot look (tile + inline pill) as it carries the same information and is what the product's site shows: icon tile 44x44 radius 10 (dark `#111` in dark theme), name 15pt, pill = energy label
> in a 12pt secondary text on `primary 8%` capsule with the energy icon, (i) opens the same permissions/energy popover. Both are acceptable; keep strings identical.

### 3.3 Energy profile -> label / icon
`idle` **"Nothing at rest"** (leaf); `mouse` **"Listens to the mouse"** (computermouse); `pointer` **"Listens to pointer input"** (hand.point.up.left);
`keyboard` **"Listens to the keyboard"** (keyboard); `inputs` **"Listens to mouse and keyboard"** (computermouse + keyboard); `periodic` **"Checks on an interval"** (clock.arrow.circlepath).
Windows: map to hooks - `mouse` = WH_MOUSE_LL, `keyboard` = WH_KEYBOARD_LL, `inputs` = both, `pointer` = a mouse hook for gestures/edge snap, `periodic` = a timer.
Per-feature profile is in the catalog table (sec. 4); `windowLayout`, `radialMenu`, `mixer`, `brightness`, `appUpdates` are **dynamic** (see code rules in sec. 4 notes).

### 3.4 "Never turned on" offer
Offered features list (`offeredWhenNeverSwitchedOn`): dockPreview, dockClick, windowMaximizer, autoQuit, scrollInverter, linearScroll, focusFollowsMouse, mouseAcceleration, mouseNavigation,
mouseButtonShortcuts, middleClick, keyboardDebounce, mouseClickDebounce, superKey, finderCutPaste. A feature qualifies when installed, its enable keys all false, none of its enable keys was **ever saved**
(a switch turned on and back off counts as used), it was installed when the app started (a later install in this session isn't offered), and it is not in `featureHubKeptFeatures`.
Card shows when **>= 3** qualify: title **"Never turned on"**; text **"These features are installed but have never been on: %@. Uninstalling them makes the panel and Settings shorter. Nothing is deleted, and each one comes back with one click."**
(%@ = comma-joined hub titles); buttons **"Keep them"** (adds all to kept list = persisted comma-joined raw ids in `featureHubKeptFeatures`) and **"Uninstall these"** (re-evaluates at click time, uninstalls,
then shows the green card with footerNote + **"Undo"**: Undo marks them kept and reinstalls with `enablingFirstInstalls=false`).

### 3.5 Search/command-bar reveal
`pendingFeatureTarget`: switch to Features tab, expand the feature's group (or the Island "What appears" disclosure), scroll group to top, then scroll feature to center (twice: +0.08 s, +0.16 s), tint row accent 10%, clear after 1.2 s (animations skipped with reduce motion).

### 3.6 Strings (complete `FeatureHubStrings`, English)
pageTitle "Features"; intro (above); tabFeatures "Features"; tabPermissions "Permissions"; activeCountFormat "%1$d of %2$d features installed"; monitorAllOffNote (above);
titleDockClick "Dock clicks"; titleMouseNavigation "Side buttons"; titleMusicBlock "Music app blocker"; titleAudioPriority "Audio device priority";
groups: "Windows and Dock", "Mouse and keyboard", "Clipboard and files", "Sound", "Energy and display", "Tools", "System monitor" (Dynamic Island group title = "Dynamic Island"); experimentalBadge "Experimental";
permissionsIntro (above); usedByFormat "Used by %@"; usedByNone "Nothing that is on uses this permission right now."; unusedBanner "You granted this permission, but nothing that is on needs it. If you like, revoke it in System Settings.";
statusGranted "Granted"; statusMissing "Not granted"; statusUnknown "The app can’t check this one"; requestButton "Request"; openSystemSettings "Open System Settings";
footerNote, restartNote, restartButton "Restart now", installButton "Install", uninstallButton "Uninstall", installAllButton "Install all", uninstallAllButton "Uninstall all";
presetsTitle "Start with a bundle", presetsCaption, presetApplyButton "Apply", presetConfirmFormat/Apply/Cancel (above);
energy*: see 3.3; energyHelp (above); notchUninstall* (above); neverUsed* (above); onboardingSelectedPermissionsTitle "Permissions for your choices"; onboardingNoSelectedPermissions "You do not need to grant any permission to finish setup.";
onboardingOtherPermissionsTitle "Other permissions"; onboardingOtherPermissionsCaption "Optional. Grant these now or later, when a feature needs them."; menuUndo "Undo"; betaBadge "BETA"; betaFeatureWarning "Beta. You may run into some bugs.".
Per-feature descriptions are in the catalog table (sec. 4). On Windows replace "Mac" with "PC", "Dock" with "taskbar", "menu bar" with "taskbar/tray", "System Settings" with "Windows Settings".

### 3.7 Behaviour
- Install/Uninstall -> `FeatureRuntime.setAvailable` (sec. 5). All pages re-read the catalog on `revision` change (sidebar rebuild, page fallback, search index).
- Install all: installs every installable feature **without** switching on enable keys (avoids intrusive things such as Focus follows mouse). Uninstall all: uninstalls everything.
- Preset apply: `replaceAvailable(with: preset.features, enabling: preset.enableKeys)`: installs preset set, switches enable keys on (Windows preset: switcherEnabled, dockPreviewEnabled, dockClickMinimize, windowMaximizeEnabled), uninstalls the rest.
- Hardware-gated feature (fan control): row greyed + tooltip reason; install refused everywhere (hub, install all, preset, onboarding). Existing installs are never revoked.
- Page keeps no scroll jump when the restart card appears (scroll anchor correction) - implement by compensating offset when content above the viewport changes height.

### 3.8 Gap vs WinKnife
`FeaturesWindow` lists features with a simple Install button; no tab control, no Permissions page, no tally/progress bar, no Install all / Uninstall all, no bundles, no restart card, no
energy/permission metadata, no never-used card, no per-group collapsing/counters, no Dynamic Island "What appears" sub-group, no search-reveal, no confirm dialogs. `FeatureGroup.WindowsTaskbar` etc. exist; the
registry's `InstalledByDefault` is **false** except 1 feature (should follow sec. 4). WinKnife registry has ids but is missing: install semantics (enable-on-first-install, saved choices kept, island companions),
restart-to-unload tracking, hardware gate.

---

## 4. Feature catalog (`FeatureCatalog.swift`)

Per feature: id (stable raw value, persist as `featureAvailable.<id>`), group, hub title, hub description, SF Symbol, energy profile, permissions it *can* use, installed-by-default on an updated Mac (`installedByDefault`), and enable keys.
**Fresh install** (first run) starts from the *Essentials* bundle only (sec. 5.4) - `installedByDefault` is what an existing/updated install keeps.

Legend - Energy: I idle, M mouse, K keyboard, B both inputs, P periodic, D dynamic. Perm: A accessibility, S screen recording, FDA full disk, FF files & folders, N notifications, AF Finder automation, AP playback automation,
AC app audio (audio capture), Mic, Cam, Cal calendar, AM app management. Def: Y = installed by default, N = opt-in.

### Windows and Dock (hub id `windowsDock`)
| id | Title | Description | SF | E | Perm | Def | Enable key(s) |
|---|---|---|---|---|---|---|---|
| switcher | Window switcher | Switch apps and windows with previews | rectangle.on.rectangle | B | A, S (S only when not "simple mode") | Y | switcherEnabled |
| dockPreview | Dock Preview | Window previews when hovering the Dock | dock.rectangle | M | A, S | Y | dockPreviewEnabled |
| dockClick | Dock clicks | Click a Dock icon to minimize or cycle windows | dock.arrow.down.rectangle | M | A | Y | dockClickMinimize / dockClickHide / dockClickCycleWindows |
| windowMaximizer | Maximize windows | The green button maximizes instead of full screen | arrow.up.left.and.arrow.down.right | M | A | Y | windowMaximizeEnabled |
| windowLayout | Window layout | Arrange windows with shortcuts or edge snapping, then adjust them by dragging | rectangle.3.group | D | A | Y | none (first install enables windowLayoutShortcutsEnabled; saved choices considered: shortcuts, directional, pointer display, edge snap, gesture) |
| autoQuit | Quit on close | Quit apps when their last window closes | xmark.rectangle | B | A | Y | autoQuitEnabled |
| spacesOrder | Keep Spaces in a fixed order | Stops macOS from rearranging Spaces by most recent use, so they stay in the order you set. Your previous setting returns when this is turned off. The Dock may restart once to apply the change. | rectangle.split.3x1 | I | - | **N** | spacesOrderEnabled |

### Mouse and keyboard (`mouseKeyboard`)
| id | Title | Description | SF | E | Perm | Def | Enable |
|---|---|---|---|---|---|---|---|
| scrollInverter | Invert mouse scrolling | Invert the mouse wheel direction | arrow.up.arrow.down | M | A | Y | scrollInverterEnabled / scrollInverterHorizontalEnabled |
| scrollHorizontal | Scroll sideways while holding a key | Hold only the selected modifier to scroll the vertical mouse wheel horizontally. Other key combinations are unchanged. | arrow.triangle.swap | M | A | N | scrollHorizontalEnabled |
| scrollZoom | Zoom | Pinch Zoom | plus.magnifyingglass | M | A | N | pinchZoomEnabled |
| focusFollowsMouse | Focus follows mouse | Focuses the window under the pointer. | cursorarrow.and.square.on.square.dashed | M | A | N | focusFollowsMouseEnabled |
| smoothScroll | Smooth scrolling | Smooth, animated mouse scrolling | cursorarrow.motionlines | M | A | Y | smoothScrollEnabled |
| linearScroll | Linear scrolling | Every notch of the mouse wheel scrolls the same distance, no matter how fast it spins. The trackpad is not affected. | arrow.up.and.down.text.horizontal | M | A | N | linearScrollEnabled |
| mouseAcceleration | Disable mouse acceleration | Removes pointer acceleration for connected mice. Your previous setting returns when this is turned off or WinKnife quits. | cursorarrow.rays | I | - | Y | mouseAccelerationDisabled |
| mouseNavigation | Side buttons | Side mouse buttons go back and forward | arrow.left.arrow.right | M | A | Y | mouseNavigationEnabled |
| mouseButtonShortcuts | Mouse button shortcuts | Extra buttons and side-wheel directions press a key combination you choose. | button.programmable | M | A | Y | mouseButtonShortcutsEnabled / mouseSpacesGestureEnabled |
| middleClick | Trackpad middle click | Three finger click acts as a middle click | hand.tap | M | A | Y | middleClickEnabled |
| mouseClickDebounce | Extra click filter | Ignores rapid extra clicks from worn mouse buttons without slowing normal clicks. | cursorarrow.click | M | A | Y | mouseClickDebounceEnabled |
| keyboardDebounce | Debounce | Ignore accidental double key presses | keyboard | K | A | Y | keyboardDebounceEnabled |
| textSnippets | Text snippets | Short triggers expand into full text | text.append | B | A | Y | textSnippetsEnabled / snippetLibraryEnabled |
| superKey | Super key | Turns one key into the modifier combination you choose. | (source key symbol, default Caps Lock) | B | A | Y | superKeyEnabled |
| quitWindowProtection | Quit & close protection | Protects ⌘Q and ⌘W from accidental presses (Windows: Alt+F4 / Ctrl+W) | shield.lefthalf.filled | K | A | Y | quitProtectionQuitEnabled / quitProtectionCloseEnabled |

### Clipboard and files (`clipboardFiles`)
| id | Title | Description | SF | E | Perm | Def | Enable |
|---|---|---|---|---|---|---|---|
| clipboardHistory | Clipboard | Keep a local history of what you copy | doc.on.clipboard | P | - | Y | clipboardHistoryEnabled |
| pastePlain | Paste as plain text | Paste text without formatting | doc.plaintext | I | A | Y | pastePlainEnabled |
| finderCutPaste | Cut & paste | Cut and paste files in Finder (Windows: Explorer) | scissors | K | A, AF | Y | finderCutPasteEnabled / finderPasteImageAsFile |
| finderRename | Rename shortcut | Rename the selected file or folder with a shortcut you choose. | pencil | K | A | Y | finderRenameEnabled |
| shelf | Shelf | Drop files on the menu bar to hold them (Windows: on the tray/edge) | tray.full | M | - | Y | shelfEnabled |
| urlCleaner | Clean URL | Copied links lose their tracking junk | link | P | - | Y | urlCleanerEnabled |
| diskImageInstaller | (title from DiskImageInstallerStrings) | (hubDescription of same) | externaldrive.badge.plus | I | AM | **N** | none |

### Sound (`sound`)
| id | Title | Description | SF | E | Perm | Def |
|---|---|---|---|---|---|---|
| mixer | Volume mixer | Per-app volume, pinning and custom order | speaker.wave.2 | D (K if precise volume roller on) | AC, A | Y |
| soundOutputSwitcher | Output switcher | Cycle sound outputs with a shortcut | hifispeaker | I | - | Y |
| audioPriority | Audio device priority | Automatically use your preferred audio devices | list.number | I | - | **N** (enable keys: audioPriorityOutputEnabled + audioPriorityInputEnabled both on at install) |
| micMute | Mute microphone | Mute the microphone from anywhere | mic.slash | I | - | Y |
| musicBlock | Music app blocker | Block music launches from detected media keys | music.note | K | A | Y (musicBlockEnabled) |

### Energy and display (`energyDisplay`)
| id | Title | Description | SF | E | Perm | Def |
|---|---|---|---|---|---|---|
| keepAwake | Keep awake | Keep the Mac awake on demand | moon.zzz.fill | I | A (only for mouse jiggle) | Y |
| brightness | Displays | Brightness and power controls for every display | display.2 | D (K when brightness keys/OSD/finer step used) | A (same conditions) | Y (brightnessControlEnabled) |
| extraBrightness | Extra brightness | Extra brightness on XDR displays | sun.max.fill | P | - | Y (extraBrightnessEnabled) |
| bluetoothSleep | Bluetooth on sleep | Switches Bluetooth off while the Mac sleeps, so headphones in a bag stop connecting to it. | wave.3.right.circle | I | - | Y (bluetoothSleepEnabled) |

### Tools (`tools`)
| id | Title | Description | SF | E | Perm | Def |
|---|---|---|---|---|---|---|
| quickLauncher | Quick panel | A floating panel with your favorite tools | wand.and.rays | I | - | Y |
| quickToggles | Quick toggles | One-click actions like dark mode and Trash | togglepower | I | AF (empty Trash only) | Y |
| quitApps | Quit apps | Lists your open apps to quit one by one, all at once, or all but the one in front. Always shows the list before anything quits. | xmark.app | I | - | **N** |
| colorPicker | Color picker | Pick any color on screen | eyedropper | I | - | Y |
| screenOCR | Copy text from screen | Copy text or QR codes from anything on screen | text.viewfinder | I | S | Y |
| cleaningMode | Cleaning Mode | Lock keyboard and screen for cleaning | bubbles.and.sparkles | I | A | Y |
| mediaTools | Media | Compress videos, images and GIFs | photo.on.rectangle.angled | I | - | Y |
| cleaner | Cleaner | Clear caches and junk files (+ " · " + WhatsApp downloads description when that sub-feature is on) | sparkles | I | FDA, FF, N | Y |
| uninstaller | Uninstaller | Remove apps and their leftovers | trash | I | FDA, AF | Y |
| homebrew | Homebrew | Keep Homebrew packages up to date | shippingbox | I | AM | Y |
| appUpdates | App updates | Find and install updates for the apps you have | arrow.down.app | D (P when schedule != off) | N, AM | Y |
| screenshot | Screenshot | Captures an area, window or screen and annotates it | camera.viewfinder | I | S | Y |
| screenRecorder | Screen recording | Records an area, window or screen and edits it afterwards | record.circle | I | S, A, AC, Mic (AC only when system audio on; Mic only when microphone on) | Y |
| cameraPreview | Camera preview | Opens a floating mirror with your camera | web.camera | I | Cam | Y |
| radialMenu | Radial menu | Opens a wheel of your favorite actions around the pointer | circle.grid.cross | D (M when a side button/trackpad trigger is used, else I) | A (only if an item needs it or a mouse trigger is set) | Y |
| scratchpad | Scratchpad | Floating pads for short-lived notes | note.text | I | - | Y |
| commandBar | Command Bar | One field that finds and runs everything the app does | command | I | A | Y |
| wallpaper | Wallpaper | Pick a still wallpaper without opening System Settings | photo.on.rectangle | I | - | **N** |
| killProcess | Kill Process | Search running processes and force quit, restart, or kill process trees | xmark.octagon | I | - | **N**, BETA |
| portManager | Port Manager | View active listening ports and, with Kill Process installed, terminate the processes using them | network | I | - | **N** |

### Dynamic Island (`dynamicIsland`; sidebar/hub rows order = enum order below; all extensions need `notch`)
| id | Title | Description | SF | E | Perm | Def |
|---|---|---|---|---|---|---|
| notch | Dynamic Island (EXPERIMENTAL badge) | Your music, controls and everyday tools, together at the top of your screen. Optional. Turn it off to keep using the separate panels. | macbook | P | A, AP (AP not if the "music" module is hidden; A only when volume / keyboard light / brightness / clipboard-window options are on) | Y |
| notchCalendar | Calendar | Browse the month and your upcoming appointments in the Dynamic Island. | calendar | P | Cal | Y |
| notchNotifications | Notifications | New system notifications in the Dynamic Island. | bell | I | A | Y |
| notchGestures | Dynamic Island Gestures | Open and close the Dynamic Island with scrolling, and swipe to change tracks. | hand.draw | I | - | Y |
| notchTimer | Timer | Timers, a stopwatch and focused work sessions in the Dynamic Island. | timer | I | - | Y |
| notchAccessories | Accessory alerts | Show connected accessories and warn once when their battery falls to 20%. | battery.25percent | P | - | Y |
| notchLyrics | Lyrics | Follow the lyrics of the current song in the Dynamic Island. | quote.bubble | P | - | Y |
| notchQueue | Up next | See the actual upcoming songs shared by your player. | list.bullet | I | - | Y |
| notchLiveEqualizer | Bars follow the music | Move the Dynamic Island bars with the sound your player makes. | waveform | P | AC | Y (stays **off** at install: asks for audio capture on first song) |
| notchDownloads | Downloads | See files arriving in a folder you choose, directly in the Dynamic Island. | arrow.down.circle | I | FF | Y |
| notchAgents | AI Agents | Follow plan limits, tokens, API value and the work in progress of Claude, Codex, OpenCode and GitHub Copilot in the Dynamic Island. | sparkles | P | - | Y |
| notchWatch | Watch | Turn any part of any window into a live activity in the Dynamic Island, with an alert when it changes, finishes or shows what you are waiting for. | eye | I | S, N | Y |
| notchMascot | Companion | A little friend in the Dynamic Island who says hello now and then and reacts to what happens on your Mac. | face.smiling | P | - | **N** |

First install of `notch` also installs the extensions except Companion and AI Agents (`dynamicIslandInitialExtensions`) once (`notchInitialExtensionsInstalled`); Agents (reads local session histories) and Companion are separate opt-ins.

### System monitor (`monitor`)
| id | Title | Description | SF | E | Perm | Def |
|---|---|---|---|---|---|---|
| monitorCPU | CPU | Processor usage and temperature | cpu | P | N (when CPU/CPU-temp alert on) | Y |
| monitorGPU | GPU | Graphics usage and temperature | rectangle.connected.to.line.below | P | - | Y |
| monitorMemory | Memory | Memory use and pressure | memorychip | P | N (memory alert) | Y |
| monitorNetwork | Network | Network speed and usage | network | P | - | Y |
| monitorDisk | Disks | Disk space and activity | internaldrive | P | N (disk alert) | Y |
| monitorPower | Power | Battery, power and charging | bolt.fill | P | N (battery/battery-temp alerts; only counted when an internal battery exists) | Y |
| connectedDevices | Connected Devices | Count connected external USB peripherals | cable.connector | P | - | Y |
| fanControl | Fan Control (BETA) | Control fans manually or with temperature curves while seeing live and target RPM | fanblades.fill | I | - | **N**; hardware-gated: unsupported text `fanControl.noFans` |

### Catalog helpers to replicate
- `isBeta`: fanControl, killProcess. `enabledKeys` empty => on-demand feature, counts as always engaged for permission "in use" logic.
- `activeFeatures(using: permission)` (drives "Used by …"): feature must be installed, engaged (any enable key true or none) and pass the dynamic rule
  (switcher/S not in simple mode; notch/AP unless music module hidden; notch features/A etc. only while the island is installed+enabled and the matching module isn't hidden; radial A only when needed; keepAwake/A only with mouse-jiggle;
  mixer/A only with precise volume roller; brightness/A with keys/OSD/finer step; monitor/N only with an alert on and its metric installed; appUpdates/N when schedule != off and notify on; cleaner/FF when WhatsApp downloads on; cleaner/N when schedule + notify or WhatsApp auto/organizer + notify; screenRecorder/AC when system audio on, /Mic when microphone on; notchLiveEqualizer/AC when island on and music module visible).
- `monitorsPermissionChanges`: false (no background polling) for screenOCR, cleaningMode, screenshot, commandBar, screenRecorder, wallpaper, notchWatch, notchMascot, and for windowLayout unless shortcuts/gesture/edge-snap with >=1 zone are on. Permission polling interval: 2.5 s while a permission UI is visible or something is needed and missing; 60 s when needed and granted; none otherwise.
- `onboardingPermissions`: only accessibility/screen-recording subset of `permissions`, excluding keepAwake, brightness, radialMenu, quickToggles, cleaner, uninstaller, homebrew, appUpdates, mixer, cameraPreview, micMute, musicBlock, notchWatch.
- `initialEnableKeys` on first install (only if none of the feature's enable keys were ever saved): first key of `enabledKeys` (windowLayout: shortcuts; audioPriority: both; notchLiveEqualizer: none).

### Windows mapping & gap
WinKnife `IFeature` ids already exist for ~55 of these (see `src/**`), with different ids in places: `app-switcher` (= switcher), `volume-mixer`, `output-switcher`, `displays` (= brightness), `clean-url` (= urlCleaner), `side-buttons` (= mouseNavigation), `island-*` (= notch*), `window-layout`,`keep-awake`, `monitor-*` ... Not present (no feature class): dock-preview/dock-click, window-maximizer, auto-quit, spaces-order, scroll-zoom, middle-click (trackpad), finder cut/paste/rename, disk-image-installer, music-block, extra-brightness, bluetooth-sleep, quit-apps, homebrew, notch-mascot, notch-watch, notch-agents, notch-notifications(listener), notch-lyrics/queue/live-equalizer, notch-accessories, fan-control. Metadata missing everywhere: hub title/desc exactness, SF/glyph, energy profile, permission list, installedByDefault per this table, enable keys, beta/experimental flags, hardware gate. Add these as fields on `IFeature` (`Symbol`, `Energy`, `Permissions`, `IsBeta`, `HardwareUnsupportedReason`, `EnabledKeys`, `InitialEnableKeys`).

---

## 5. Install / uninstall runtime (`FeatureRuntime.swift`)

### 5.1 Semantics
- Availability is a layer **above** each feature's own enable keys. Key `featureAvailable.<id>` (bool). Unavailable => disappears from Settings, panel, menu/tray, shortcuts and the Command Bar; service torn down immediately; never instantiated on the next launch. Settings are **never deleted**; reinstalling restores saved choices.
- `setAvailable([features], bool, enablingFirstInstalls=true)`: for each feature that may flip (`isAvailable != target` and, when installing, hardware supported): on first install call `enableOnFirstInstall` (turn on the primary control **only if none of its enable keys has ever been saved**), set the availability key, run the feature's **binding** (a re-sync closure) immediately - uninstall tears everything down on the spot - record `loadedThisSession`, remove from `offerableThisSession` when installed; one `revision` bump at the end (UI rebuild) + Command Bar `noteHubChange` + island re-sync.
- First island install also installs the default extensions (`notchInitialExtensionsInstalled`).
- `replaceAvailable(with:enabling:)` (presets / first-run picker): set enable keys, flip every feature to membership of `selected` (via the same gate), re-run bindings for features that stayed.
- `setAllAvailable(bool)`: bulk, no enable-key changes.
- `availableCount`, `installableCount` (= features this machine can run or already has).
- `needsRestartToUnload` = a feature that **loaded this process** (installed at launch or installed later this session) is now uninstalled -> show the restart card. `relaunchApp()`: spawn a detached helper that waits (<= 20 s, polling 0.2 s) for this PID to exit then reopens the app; only then terminate (never terminate before the helper runs).
- `syncAtLaunch()` runs bindings **only for available features**. `sync([features])` re-runs bindings when a permission is granted at runtime (accessibility granted -> input features; screen recording -> dockPreview, screenRecorder).
- Bindings map each feature to its service sync(s); a few features share a service (e.g. scrollInverter/scrollHorizontal/scrollZoom/linearScroll all sync the scroll tap; quickToggles+quitApps share a service; mixer syncs precise volume roller + mixer + input device manager; shelf also syncs the island file tools; mediaTools cancels in-flight work on uninstall; extension features stop their own service when the island is uninstalled).
- Unsupported hardware: `hardwareUnsupportedReason` (only fanControl -> "No fans found."-style string `fanControl.noFans`); `installBlockedReason = isAvailable ? nil : hardwareUnsupportedReason`.

### 5.2 Windows mapping
`FeatureRegistry.SetInstalled` already has start/stop; add: first-install enable-key logic (consult the *persisted* settings file, not defaults, to know if a key was ever saved), `installableCount`, `needsRestartToUnload` (+ restart helper: spawn `cmd /c` or a small `WinKnife.exe --relaunch <pid>` that waits for the PID then starts the exe; guard 20 s), `HardwareUnsupportedReason`, bulk install/uninstall, preset apply, revision event for UI.

### 5.3 Presets (`FeaturePreset`)
- Essentials: mixer, keepAwake, monitorCPU, monitorGPU, monitorMemory, monitorNetwork, monitorDisk, monitorPower; enable keys none.
- Windows: switcher, windowLayout, dockPreview, dockClick, windowMaximizer; enable keys switcherEnabled, dockPreviewEnabled, dockClickMinimize, windowMaximizeEnabled. (Windows port: `switcher`, `windowLayout`, taskbar preview/click equivalents, maximize.)
- Battery and quiet: monitorCPU, monitorMemory, monitorPower; none.
- Icon: star.fill / macwindow.on.rectangle / battery.75percent.

### 5.4 First-run availability
`prepareFirstRunAvailability()` runs before any binding on a clean install (`hasOnboarded == false` and `onboardingStep == 0`): sets availability **true only for Essentials**, false for the rest. Updates keep every saved availability; the registered default for an unsaved key is `installedByDefault` (table above). Interrupted onboarding keeps the selection applied at its "purpose" step.

---

## 6. Permissions page (Features hub, "Permissions" tab; also reused by onboarding)

### 6.1 Layout
Inside one `SettingsCard`: one row per `AppPermission`, in enum order **accessibility, screenRecording, fullDiskAccess, filesAndFolders, notifications, automationFinder, automationPlayback, audioCapture, microphone, camera, appManagement, calendar** (12 rows), separated by `Divider` inset 42.
Row (HStack top aligned, spacing 12):
- **30x30 status tile**: symbol 13pt semibold coloured by status on 14% fill, radius 8. Colours: granted = green, missing = orange, unknown = secondary.
- Column (spacing 4): **name** (medium) + **status chip** (6 px dot + caption secondary: "Granted" / "Not granted" / "The app can’t check this one"); **explainer** (caption secondary); **"Used by A, B, C"** (caption tertiary) or **"Nothing that is on uses this permission right now."**;
  if granted and nothing uses it: **unused banner** (caption on primary 5% rounded 7, padding 8): "You granted this permission, but nothing that is on needs it. If you like, revoke it in System Settings.";
  button row (small): **"Request"** (only if not granted and a request flow exists) and **"Open System Settings"**.
- Statuses: accessibility/screenRecording/fullDiskAccess = live `Permissions` checks (polled 2.5 s while the page is visible); filesAndFolders = WhatsApp-downloads access state if cleaner + WhatsApp downloads on else unknown; notifications from UN settings (undetermined = missing); automationFinder probed off-main (AE round trip); automationPlayback always unknown; audioCapture "missing" only when the mixer reported a failed tap else unknown; microphone/camera from AV status; calendar granted only for full access; appManagement always unknown (no public preflight).
- Request flows exist for: accessibility, screenRecording, fullDiskAccess (always), notifications/calendar/camera/microphone (only while status is undetermined).
- Page header intro on this tab: "What each permission does and which features use it. Other access is requested only when you use it."
- Page refreshes statuses on appear, registers a polling demand while visible, and does a background Finder-automation check.

### 6.2 Permission strings (English)
| Permission | Name | Explainer | Symbol |
|---|---|---|---|
| accessibility | Accessibility | Lets features react to clicks and keys, and move windows. | accessibility |
| screenRecording | Screen Recording | Lets features show window thumbnails and read text on screen. | rectangle.dashed.badge.record |
| fullDiskAccess | Full Disk Access | Lets the cleaner and the uninstaller find leftover files everywhere. | externaldrive.badge.person.crop |
| filesAndFolders | Files & Folders | Lets WhatsApp downloads cleanup and the experimental organizer inspect your Downloads folder. | folder.badge.person.crop |
| notifications | Notifications | Lets the app notify you about alerts you turned on. | bell.badge |
| automationFinder | Finder automation | Lets the app ask Finder to move files for you. | gearshape.2 |
| automationPlayback | Playback automation | Some music apps require permission for their playback controls. Authorize the selected player from Dynamic Island; access is granted separately for each app. | gearshape.2 |
| audioCapture | App audio | Lets the mixer adjust each app’s volume, the Dynamic Island bars follow the music and screen recordings include the Mac’s sound. | waveform |
| microphone | Microphone | Lets screen recordings include your voice when you turn it on. | mic |
| camera | Camera | Shows your camera only in the preview window, so you can check how you look before a call. Nothing is recorded or leaves your Mac. | camera |
| appManagement | App management | Lets updates replace or remove apps installed by the package manager. | app.badge |
| calendar | Calendar | Read your calendars to show upcoming appointments. Events stay on this Mac. | calendar |
Other related: `permissionGranted` "Granted", `permissionMissing` "Not granted", `permissionRequest` "Grant access", `permissionOpenSettings` "Open System Settings…", `permissionRestartNote` "macOS may ask to reopen the app after granting.".
Also see `Core/PermissionGuideStrings.swift` for the floating "permission guide" overlay (drives the user through the OS pane); not part of this page.

### 6.3 Windows mapping - what each "permission" becomes
Windows has no TCC prompts for most of these. Keep the **same page, same rows where meaningful**, but the status becomes a *capability check*, "Request" becomes "Fix this"/elevate, and "Open System Settings" opens a `ms-settings:` URI.

| macOS permission | Windows capability the features really use | Status check | Request / Open |
|---|---|---|---|
| Accessibility | Low-level hooks `WH_KEYBOARD_LL` / `WH_MOUSE_LL` (SetWindowsHookEx), `SetWinEventHook`, UI Automation (UIA) for other apps' windows/menus, `SendInput`, `RegisterHotKey`. No grant needed. **Limitation:** elevated (admin) windows ignore hooks/UIA/SendInput from a non-elevated process (UIPI). | Always "Granted"; add a second state **"Limited: running without administrator rights"** when `IsUserAnAdmin()`==false AND the foreground/target process is elevated (detect on use). | Request = "Restart as administrator" (`runas`, or sign + `uiAccess=true` in manifest for installed-in-ProgramFiles builds); Open = none (hide button) |
| Screen Recording | Windows.Graphics.Capture / DXGI Desktop Duplication / DWM thumbnails (`DwmRegisterThumbnail`). No grant. Win11 shows a capture border (can be disabled when `GraphicsCaptureSession.IsBorderRequired=false` on 22H2+ after `GraphicsCaptureAccess.RequestAccessAsync(Borderless)`). | "Granted" (or "Granted - capture border shown" on Win10) | Open = `ms-settings:privacy-graphicscaptureprogrammatic` where present |
| Full Disk Access | No equivalent; the cleaner/uninstaller read `%LOCALAPPDATA%`, `%APPDATA%`, `%ProgramData%`, `C:\Windows\Temp`, other users' profiles (need admin) | Row renamed **"Administrator access"**: granted when elevated | Request = restart elevated (UAC) ; Open = none |
| Files & Folders | **Controlled folder access** (Windows Security ransomware protection) may block writes to Documents/Downloads | Test write to the Downloads folder / read `Get-MpPreference` ControlledFolderAccess; "Unknown" if can't read | Open = `windowsdefender://RansomwareProtection` |
| Notifications | Toast notifications must be enabled for the app (AUMID registered, unpackaged apps need a Start-menu shortcut with AUMID) | `ToastNotificationManager.CreateToastNotifier().Setting == Enabled` | Open = `ms-settings:notifications` ; Request: send a test toast |
| Finder automation | Not a permission. Windows: Explorer is driven with COM (`Shell.Application`, `IShellFolder`, UIA on `CabinetWClass`) | Always Granted (hide row or show "Not needed on Windows") | - |
| Playback automation | Not a permission. System Media Transport Controls (`GlobalSystemMediaTransportControlsSessionManager`) give playback info + controls for any app | Always Granted | - |
| App audio (audio capture) | **WASAPI process loopback** (`ActivateAudioInterfaceAsync` with `AUDIOCLIENT_ACTIVATION_PARAMS`, Win10 2004+) for per-app capture; per-app volume via `ISimpleAudioVolume`/`IAudioSessionManager2`. No grant | Granted; "Missing" if loopback activation fails (old OS) | - |
| Microphone | Real: Settings > Privacy & security > Microphone, incl. "Let desktop apps access your microphone" | `DeviceAccessInformation.CreateFromDeviceClass(DeviceClass.AudioCapture).CurrentStatus` / `MediaCapture` init | Request = start `MediaCapture` init; Open = `ms-settings:privacy-microphone` |
| Camera | Real: Privacy & security > Camera | `DeviceAccessInformation` VideoCapture | Open = `ms-settings:privacy-webcam` |
| Calendar | Real: Privacy & security > Calendar (`AppointmentStore` via `AppointmentManager.RequestStoreAsync`) | AppointmentStore access result | Open = `ms-settings:privacy-calendar` |
| App management | UAC elevation for per-machine installs/uninstalls (winget/MSI/`msiexec`) | Granted when elevated; else "Not granted - will prompt (UAC) when needed" | Request = elevate |

Plus two Windows-only rows worth adding at the end: **"Run at startup"** (status of the autorun entry) and **"Background activity"** (Task Manager "Efficiency mode" / battery saver may throttle the app) - optional.
Used-by lines keep working unchanged (feature->permission mapping above, with Windows-specific subsets: e.g. switcher uses Accessibility + Screen Recording (thumbnails); mixer uses App audio).

### 6.4 Gap
WinKnife has no Permissions page or capability model. Add `Permissions` service (capability probes above), `PermissionsPortalSections` control, and per-feature `Permissions` metadata.

---

## 7. Onboarding (`UI/Onboarding/OnboardingView.swift`)

Window: fixed **540 x 600**, non-resizable; content in a scroll view above a footer (Divider + nav bar). Shown once at first launch (`hasOnboarded == false`; skipped for one launch if the previous start never finished) and from About > "Review introduction". Resume point `onboardingStep` (int); `hasOnboarded` is set when the user finishes. Menu-bar permission guide overlay is suppressed during onboarding.

**Steps (4):** welcome -> purpose -> permissions -> done.
Footer: leading **Back** (disabled on step 0); centre **page dots** (7 px capsules, current one 18 px wide accent, others primary 15%, spring animation); trailing large prominent default button **"Continue"** (last step: **"Open WinKnife"**, which resets step to 0 and finishes).

1. **Welcome** - 220 px hero with the brand gradient (`spaceGradient`), logo (130 wide), app name 26 bold white, subtitle 12 white 85%: **"A discreet menu bar utility that makes everyday macOS more practical."** (Windows: "...everyday Windows more practical."). Below (padding 24): language `Picker` (menu style, label "Language", all 15 languages in their own names), then three feature rows (30x30 gradient tile radius 8 with white 13pt glyph; title 13 semibold; text 11.5 secondary):
   - `bolt.fill` **"Energy under control"** - "Keep the Mac awake for as long as you want, even with the lid closed."
   - `gauge.with.dots.needle.50percent` **"A clear view of the system"** - "CPU, GPU and battery temperatures, hardware usage and memory pressure in real time."
   - `rectangle.on.rectangle` **"Mouse and windows, your way"** - "Reversed mouse scrolling and a window switcher with thumbnails."
2. **Purpose** - header icon `sparkles.rectangle.stack`, title **"What brought you here?"**, subtitle **"Choose a ready setup or select exactly what you want to use."**; three **bundle cards** (padding h14 v10, radius 10, selected: accent 12% fill + 45% accent stroke + `checkmark.circle.fill`; unselected primary 5%) for the presets; the default selection is **Essentials** with its feature set pre-selected on a fresh run; then a row **"FEATURES"** (11 bold secondary, tracking 1) + "**N of M features installed**" (caption tertiary) and, per group (group caption headings incl. "Dynamic Island"), one **feature card per feature** (HStack: 32x32 tile - selected: gradient+white glyph, unselected: secondary 16% + secondary glyph; title 12.5 semibold; description caption 2 lines; trailing `checkmark.circle.fill`/`circle` 16pt). Selecting a card clears the preset; picking `notch` also selects its initial extension group, deselecting it deselects all extensions; hardware-unsupported cards disabled 40% + tooltip. Footer note: **"You can add or remove features later in Settings."**. Pressing Continue **applies the selection** via `replaceAvailable(with:enabling: preset.enableKeys)`.
3. **Permissions** - header icon `key.horizontal.fill`, title **"Permissions"**, subtitle = permissionsIntro. Card **"Permissions for your choices"** (headline) listing only Accessibility and/or Screen Recording needed by the selected features (`onboardingPermissions`): each = a `PermissionRow` (icon `checkmark.circle.fill` green / `exclamationmark.circle.fill` orange, name, status text "Granted"/"Not granted", buttons **"Grant access"** and **"Open System Settings…"** when missing) + explainer + "Used by …" (sorted with localized compare). If none: green-check label **"You do not need to grant any permission to finish setup."**. Second card: disclosure **"Other permissions"** with caption **"Optional. Grant these now or later, when a feature needs them."** that expands to the full portal rows of the remaining permissions. Footer caption: **"macOS may ask to reopen the app after granting."**
4. **Done** - 300 px hero gradient, logo 150, **"All set!"** (26 bold), **"WinKnife is already looking after your Mac."**; below: icon `menubar.arrow.up.rectangle` 26pt + hint **"Look for the black hole in the menu bar, at the top right of the screen."** (Windows: "Look for the WinKnife icon in the system tray (click the ^ if it's hidden).").
Other onboarding-family windows (What's New, update highlights, support intro) are specified in `02e`.

**Windows mapping:** a 540x600 WPF/WinUI window with Mica; permission step becomes "Capabilities" with the Windows rows from 6.3 (only Administrator access / Microphone etc. relevant ones); remove the restart note (or: "Some features need WinKnife to run as administrator"). **Gap:** WinKnife has no onboarding; `App.xaml.cs` sets `firstRun = !File.Exists(SettingsFile)` only.

---

## 8. App-wide behaviour (`App/AppDelegate.swift`)

### 8.1 Startup order
1. Accessory app (no taskbar/dock entry), set appearance (light/dark/system) before any window exists; start observing keyboard-layout changes; notification delegate.
2. **Startup watch**: set `startupDidNotFinish=true`; cleared after 20 s of healthy run or on proper quit. If it was still set at launch -> the previous start died: skip all optional startup windows (onboarding / what's new / intros) this time so the app comes up bare.
3. Bundle/rename migration (may quit to relaunch); sweep temp recorder workspaces.
4. `FeaturePreset.prepareFirstRunAvailability()` (clean install -> Essentials only).
5. Repair "launch at login" registration off-main-thread (never turns it off at startup). Restore any display left dimmed/off by a prior run.
6. Install the main menu (App/Edit/Go/Window) so standard shortcuts work in Settings; install horizontal-wheel scrolling; reset collapsed panel sections once per version.
7. Create the status item (tray icon) + wire: left click -> toggle panel; right click -> if Keep awake installed and `keepAwakeRightClickToggle` on => toggle keep awake, else show context menu; metric click handlers.
8. Recovery helpers (fan control, spaces order), `FeatureRuntime.syncAtLaunch()`, power probe (if battery), auto update checks, app-activation observer (re-check permissions), keep-awake auto-start (`activateOnLaunchIfNeeded`).
9. Permission observers: accessibility granted -> sync all input features; screen recording granted -> sync dockPreview, screenRecorder. Language change -> rebuild main menu.
10. Next run-loop turn (so the tray icon is visible first): if `!hasOnboarded` -> onboarding; else queue Brightness-update prompt and What's New for `lastUpdateIntroVersion`, store `featuresOnboardingVersion` + `lastUpdateIntroVersion`, recover tray icon after update, present update intros.

### 8.2 Tray / status item context menu (right click)
In order, each only when applicable: **Enable keep awake / Disable keep awake** (Keep awake installed); **Activate for…** submenu (only when keep awake is off): **15 minutes, 30 minutes, 1 hour, 2 hours, 4 hours, 8 hours, Indefinitely**; **Cleaning Mode** (installed); separator (if any of the above); **Settings…** (Ctrl+, / ⌘,); **About WinKnife**; **Uninstall an app…** (Uninstaller installed); **Open shelf** (Shelf installed AND `shelfEnabled`); **Check for updates…**; separator; **Quit WinKnife** (⌘Q -> Alt+F4 / Ctrl+Q).
Main menu (App/Edit/Go/Window) has no Windows counterpart (skip) except enabling Ctrl+C/V/X/A/Z in text boxes and Ctrl+, to open Settings inside WinKnife windows, Esc to close dialogs.
About = standard about panel with the description credit (`aboutDescription`).

### 8.3 Reopen (second launch / double-click while running)
`applicationShouldHandleReopen`: ignore reopen requests not initiated by a person (Siri/Shortcuts); if a What's New/support intro window exists, focus it; if any window is visible do nothing; otherwise **recover**: if the tray icon is missing/off-screen and not hidden by choice, recreate it; next turn, pop the panel if the icon is on screen else open Settings - the user must **always** be able to get back in. Windows: single-instance mutex (`Local\WinKnife.SingleInstance`, already present) -> second instance signals the first (named event / `WM_COPYDATA`) to run this recovery; recreate the tray icon on `TaskbarCreated` message.

### 8.4 Termination
On quit (`applicationWillTerminate`): stop/suspend every service so nothing global stays changed - island, URL cleaner, focus-follows-mouse, maximizer, window layout, debounce services, text snippets, Super key mapping, switcher hotkeys, mouse-button shortcuts, middle click, scroll, smooth scroll, mouse acceleration (restore previous), navigation, Dock preview, sound output switcher, precise volume roller, app volume mixer, fan control, preferred microphone override, scratchpad flush, **restore any OS shortcut taken over**, clipboard history flush (async persistence must land before the process dies), keep awake deactivated, brightness displays restored, extra brightness stopped, network monitoring stopped; restore any borrowed input source (terminate-later while restoring). Windows: `Application.Exit` -> `registry.StopAll()` (already) + restore mouse acceleration (`SPI_SETMOUSE`), sticky/filter keys, execution state, display brightness/gamma, disabled keys remap (Scancode Map registry if Super key uses it - prefer hook-only).

### 8.5 Other global behaviours
- **Appearance** (`appAppearance`: system/light/dark; applied app-wide) and **language** (15 languages; English default; live switch re-renders all pages and the menu).
- **Launch at login** (General page): registered with the OS; startup repair; pending/approval state shown. Windows: `HKCU\...\Run` or `StartupTask`; WinKnife has `AutoStart.cs`.
- **Global keep-awake shortcut** (`hotkeyEnabled`, default true).
- **Settings window size** persisted (`settingsWindowWidth/Height`).
- Settings **backup/restore** (`Core/BackupStrings.swift`, `SettingsBackupSupport.swift`) lives in Advanced (see 02a).
- **Secure input** warning rows (typing features blocked while a password field holds secure input) are macOS-only: omit on Windows, but show an equivalent note when the foreground window is elevated (hooks ignored).

### 8.6 Gap
`App.xaml.cs`: single-instance mutex, tray, theme manager, auto start, registry start - present. Missing: startup-watch crash guard, second-instance recovery of the tray icon / settings, relaunch helper, first-run Essentials set, onboarding/what's-new flow, full tray context menu (WinKnife's tray only has "Start with Windows" + basics), right-click keep-awake toggle option, update checks, appearance/language, permission observers, orderly termination restores.

---

## 9. macOS-only features: how to adapt (hub rows keep their id; unsupported ones use the hardware-gate mechanism: row disabled, tooltip with reason, Install refused, never counted in "installable")

| Feature | macOS concept | Windows adaptation / verdict |
|---|---|---|
| switcher | Cmd+Tab replacement w/ thumbnails | Alt+Tab replacement: LL keyboard hook swallowing Alt+Tab, DWM thumbnails; works (Win key interplay). Keep. |
| dockPreview / dockClick | Dock hover previews / click behaviours | **Taskbar preview / click**: UIA on `Shell_TrayWnd` buttons + DWM thumbnails; rename titles on Windows: "Taskbar Preview", "Taskbar clicks" (WinKnife already uses `WindowsTaskbar` group). |
| windowMaximizer | Green button maximizes instead of full-screen | No full-screen green button. **Unsupported -> hide**, or reinterpret as "Maximize button maximizes to a borderless/work-area fit" - recommended: hide (hardware-gate "Not applicable on Windows"). |
| windowLayout | Edge snap, shortcuts, drag gestures | Fully supported (move/resize via `SetWindowPos`; edge snap via hook; coordinate with built-in Snap Layouts - optionally disable them). Stage Manager card: omit. |
| autoQuit | Quit apps when last window closes | Supported via `SetWinEventHook(EVENT_OBJECT_DESTROY)` + process window count; send `WM_CLOSE`; keep exceptions list; skip tray-resident apps by default. |
| spacesOrder | Fixed Spaces order | No Windows equivalent. **Hide.** |
| scrollInverter / horizontal / linear / smooth | Mouse wheel tap | `WH_MOUSE_LL` wheel events (WM_MOUSEWHEEL/HWHEEL) re-injected via `SendInput`; mark injected events. Supported. |
| scrollZoom | Pinch zoom on mouse? (Ctrl+scroll zoom) | Map to Ctrl+wheel zoom behaviour / `SendInput` Ctrl+wheel; touchpad pinch already works natively. |
| focusFollowsMouse | Focus under pointer | `SetForegroundWindow` with ForegroundLockTimeout workaround (`SystemParametersInfo SPI_SETACTIVEWINDOWTRACKING` is built-in: prefer it). |
| mouseAcceleration | Disable acceleration | `SPI_SETMOUSE` (enhance pointer precision off); restore on quit. |
| mouseNavigation / mouseButtonShortcuts | Side buttons | `WM_XBUTTON*` from LL hook; only Back/Forward on most mice; extra buttons via Raw Input / HID. |
| middleClick (three-finger click) | Trackpad | Precision Touchpad: configurable natively (Settings > Touchpad > three-finger tap = middle mouse); implement via registry/`SystemParametersInfo` or Raw Input HID parse; else hide. |
| mouseClickDebounce / keyboardDebounce | Event taps | LL hooks; supported. |
| textSnippets | Expand triggers | LL keyboard hook + `SendInput(KEYEVENTF_UNICODE)`. |
| superKey | Caps Lock etc. as hyper key | LL hook remap (avoid registry scancode maps); supported. |
| quitWindowProtection | Protects ⌘Q/⌘W | Protect **Alt+F4 / Ctrl+W (/ Ctrl+Q)** by hold-to-confirm; supported. |
| clipboardHistory / pastePlain / urlCleaner | NSPasteboard | `AddClipboardFormatListener`, CF_UNICODETEXT/HTML; paste plain = strip formats then Ctrl+V. Supported. |
| finderCutPaste / finderRename | Finder | Explorer **already has cut/paste**; "Rename shortcut" = F2. Adapt: **Explorer enhancements** (paste image as file, rename shortcut of choice) via UIA/`SendInput` on `CabinetWClass`/`WorkerW`. Rename feature id as needed; AF permission row N/A. |
| shelf | Drop zone at menu bar | Drop zone window at screen edge / on tray flyout; `IDropTarget` OLE. |
| diskImageInstaller | DMG installer | Adapt to **mount ISO/VHD and run installer, or MSI/MSIX launch** (`DiskImage` cmdlets / `IVdsService`) - low priority; hide until built. |
| mixer / soundOutputSwitcher / audioPriority / micMute | CoreAudio | WASAPI/MMDevice (`IMMDeviceEnumerator`, `IPolicyConfig` for default device switch, `IAudioEndpointVolume` for mic mute). Supported. |
| musicBlock | Stops Music app launching on media keys | Windows launches the default media app for media keys with no active session; intercept `VK_MEDIA_*`/`APPCOMMAND_MEDIA_*` in the LL hook when no SMTC session exists. Supported (rename "Media key blocker"). |
| keepAwake | caffeinate / clamshell | `SetThreadExecutionState` + `powercfg /change`/`PowerWriteACValueIndex` for lid action; no sudoers. |
| brightness / extraBrightness | DDC / XDR | WMI `WmiMonitorBrightnessMethods` (laptop panels), DDC/CI `dxva2` `SetMonitorBrightness` (externals); "Extra brightness" = **HDR SDR-white-level boost** (undocumented) - hide unless implemented. "Keyboard light": Dynamic Lighting only. |
| bluetoothSleep | Bluetooth radio off on sleep | `Windows.Devices.Radios` + `SystemEvents.PowerModeChanged`. Supported. |
| quickToggles | Dark mode, Trash | Registry `AppsUseLightTheme`/`SystemUsesLightTheme`; `SHEmptyRecycleBin`; plus Night light/DND toggles. |
| quitApps | quit list | Enumerate top-level windows by process -> `WM_CLOSE`. Supported. |
| colorPicker / screenOCR / screenshot / screenRecorder | ScreenCaptureKit, Vision | Graphics.Capture / GDI, `Windows.Media.Ocr`, Media Foundation (+ffmpeg). Supported. |
| cleaningMode | Lock keyboard/screen | LL hook swallowing keys + full-screen black overlay (BlockInput needs admin and is unreliable). Supported. |
| mediaTools / cleaner / uninstaller / appUpdates / homebrew | Mac apps | Cleaner = temp/cache/`%LOCALAPPDATA%`; Uninstaller = registry Uninstall keys + leftovers; App updates = winget/Store; Homebrew = **winget/Scoop/Chocolatey** page ("Packages"). See 02e. |
| cameraPreview | AVCapture | `MediaCapture`/`MediaPlayer` preview; Camera permission row real. |
| wallpaper | Desktop picture | `SystemParametersInfo(SPI_SETDESKWALLPAPER)` / `IDesktopWallpaper`. |
| radialMenu / commandBar / scratchpad | - | Fully portable. |
| notch* | Notch/Dynamic Island | Top-center overlay window (layered, topmost, tool window) with pill/capsule silhouettes; "camera housing fit" options dropped; media via SMTC, calendar via `AppointmentStore`, notifications via `UserNotificationListener` (needs package identity - otherwise omit), accessories via Bluetooth GATT battery. See 02d. |
| monitorCPU/GPU/Memory/Network/Disk/Power | IOKit sensors | PDH / `GlobalMemoryStatusEx` / `GetIfTable2` / `GetSystemPowerStatus`; **temperatures and GPU temp need vendor APIs (LibreHardwareMonitor / NVML / ADL) -> optional**; "memory pressure" -> commit % or standby list. connectedDevices via `SetupAPI`/WMI USB enum. |
| fanControl | SMC fans | **Impossible** on Windows without vendor drivers (EC access): keep as hardware-gated unsupported row (BETA badge) or omit. |
| Liquid Glass / SF Symbols menu-bar icon | macOS 26 UI | Mica/Acrylic backdrops; icon picker uses Segoe Fluent glyphs. |

---

## 10. Defaults & app-wide keys quick reference
(Complete per-page default tables are in 02a-02e; the keys below are app-wide.)

| Key | Type | Default | Meaning |
|---|---|---|---|
| `featureAvailable.<id>` | bool | `installedByDefault` (sec. 4); clean install = Essentials only | availability layer |
| `featureHubKeptFeatures` | string | "" | comma-joined raw ids the user chose to keep (never-used offer) |
| `notchInitialExtensionsInstalled` | bool | false | island companions auto-installed once |
| `hasOnboarded` | bool | false | onboarding finished |
| `onboardingStep` | int | 0 | resume point (>=2 => selection already applied) |
| `featuresOnboardingVersion`, `lastUpdateIntroVersion` | string | "" | last feature-tour / version seen |
| `startupDidNotFinish` | bool | false | startup crash guard |
| `settingsWindowWidth/Height` | double | 0 (unset) | persisted Settings size |
| `appLanguage` | enum | English | UI language |
| `appAppearance` | enum | `AppAppearance.fallback` (system) | light/dark/system |
| `hotkeyEnabled` | bool | true | global keep-awake shortcut |
| `autoCheckUpdates` | bool | true | background update check |
| `menuBarIconSymbol` | string | "" | custom tray icon symbol (empty = brand glyph) |
| `keepAwakeRightClickToggle` | bool | false | right click on tray toggles keep awake |

---

## 11. Prioritised build list for implementers
1. Settings shell: window, sidebar (dynamic, 9 sections), search, router/history, card/row components (sec. 1-2).
2. Catalog metadata on `IFeature` + `FeatureRuntime` semantics (sec. 4-5): enable-on-first-install, installedByDefault, hardware gate, restart-to-unload, relaunch helper.
3. Features hub page incl. presets, summary, never-used, groups, rows, popover (sec. 3).
4. Permissions page with Windows capability probes (sec. 6).
5. Onboarding (sec. 7), startup watch / second-instance recovery / tray context menu (sec. 8).
6. Then the individual pages per 02a-02e; hide unsupported features per sec. 9.
