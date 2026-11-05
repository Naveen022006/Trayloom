# 04 - TOOLS (Services/* + UI/* for the Tools group)

Part of the Trayloom replica spec (see SPEC_RULES.md). Source of truth: Vorssaint Swift sources
(`E:\REA TEST 1\vorssaint-mac\Sources\Vorssaint`). Tools group = AppFeature cases: quickLauncher, quickToggles,
quitApps, colorPicker, screenOCR, cleaningMode, mediaTools, cleaner, uninstaller, homebrew, appUpdates, screenshot,
cameraPreview, radialMenu, scratchpad, commandBar, screenRecorder, wallpaper, killProcess, portManager (+ Feedback).

## Table of contents
1. Command Bar
2. Quick panel (Quick Launcher), Quick toggles, Quit apps, Color picker, Copy text from screen, QR/barcode, shared plumbing
3. Radial menu (+ visual canvas)
4. Screenshot (capture modes, annotation editor, pins, recent captures, share)
5. Screen Recorder (capture, editor, blur, export, GIF)
6. Small tools: 6.1 Scratchpad, 6.2 Camera Preview, 6.3 Cleaning Mode, 6.4 Kill Process, 6.5 Port Manager, 6.6 Wallpaper, 6.7 Feedback
7. Maintenance: 7.1 Cleaner, 7.2 Uninstaller (+leftovers), 7.3 App updates, 7.4 Homebrew -> winget, 7.5 Media tools

## 0. Cross-cutting findings and decisions needed (read first)

Findings that corrected assumptions in the task brief (do NOT invent features Vorssaint lacks):
- Quick toggles: Vorssaint has exactly 11 (dark mode, keyboard light, mic mute, empty trash, quit all apps, eject disks,
  hidden files, desktop icons, lock, display off, screen saver). No Night Shift/Caffeinate/True Tone/DND/Stage Manager.
  Trayloom's current "file extensions" toggle is not in Vorssaint (remove for parity). Keyboard light has no Windows
  equivalent (hide unless a LampArray keyboard exists).
- Color picker and Copy-text-from-screen (OCR) and QR live in Services/QuickTools (spec section 2.4-2.6).
- Wallpaper is a still-image gallery picker in the menu panel (not live/video wallpaper).
- Cleaning Mode locks keyboard + scroll only (mouse stays usable); unlock = 5x Esc within 6 s or Unlock button.
- Kill Process is a Settings page (Beta, opt-in); Port Manager shows TCP listeners only, Kill buttons only when Kill Process installed.
- Camera Preview: fixed 320x240 rounded, mirrored, closes on outside click. Trayloom's circle/size options are extras.
- Screenshot saves PNG only; scrolling capture is user-driven (90 ms pixel polling), no injected wheel events.
- Recorder: no webcam source, no keystroke display, no per-segment speed (global export speed 0.25-4x), no MOV/ProRes/WebM.
  highlights-recorderblur.png is a marketing mock-up; real editor has a "Blur" lane block, m:ss timecodes.
- Homebrew has no taps/services/doctor/cleanup/pin/export in Vorssaint; the winget mapping lists them only as optional extras.
- Vorssaint's hosted services (screenshots.vorssaint.com share links, feedback endpoint) must NOT be used by Trayloom.

Decisions / open questions (implementer should pick the recommended option unless the owner says otherwise):
1. Hotkey modifier mapping: macOS Ctrl+Opt+Cmd -> Ctrl+Alt+Shift; Ctrl+Cmd -> Ctrl+Alt (avoid Win key). Scratchpad/Camera defaults
   (Ctrl+Opt+Cmd+N / W) -> Ctrl+Alt+Shift+N / W (one spec suggested Ctrl+Alt+Win; prefer Ctrl+Alt+Shift for consistency).
2. Command Bar default hotkey: Alt+Space (macOS default Option+Space) via RegisterHotKey; fallback Ctrl+Alt+Space if Alt+Space is taken (it opens the window system menu).
3. Share/feedback backends: need a Trayloom-operated endpoint; until one exists hide "temporary link" share UI and make Feedback fall back to opening a GitHub issue / mailto.
4. NuGet packages not on the allowed list - ASK FIRST: ZXing.Net (QR), SkiaSharp (screenshot renderer; WPF fallback exists), Markdig (Scratchpad preview; or hand-written parser),
   Vortice/SharpDX (recorder GPU compositor), WinGet COM interop, ImageSharp/Magick.NET (GIF quantiser), pinyin keywords, bundled GPL ffmpeg.exe.
5. Recorder export codec: H.264 default on Windows (HEVC only if encoder probe passes and extension present); direct-save container .mp4.
6. Cleaner deletion policy: caches/temp/logs/dumps permanently deleted (Recycle Bin frees no space); leftovers, startup items, backups, screenshots -> Recycle Bin.
7. Segoe Fluent Icons code points in all parts are best-effort; verify each against the font map ("verify" marks).
8. Platform wording substitutions are marked `[WIN]` with the original string beside them; keep both.

Suggested implementation priority across the group: (P0) Command Bar parity + Quick panel/toggles + Screenshot capture/editor
+ Cleaning Mode/Wallpaper/Scratchpad fixes; (P1) Radial menu parity, App updates, Uninstaller, Cleaner, Media;
(P2) Recorder (new, largest), Homebrew->winget page, Kill Process/Port Manager polish; (P3) optional extras.

---

## 1. Command Bar

Source of truth: `Services/CommandBar/*` (CommandBarCatalog, Service, Support, Math, Units, Dates, Emoji, Colors, Links, Menus, Extras, FileSearch(+Support), SystemSettings(+Support), ScriptRunner, QueryMemory, RowShortcuts, Preferences, Selection, DropletMotion), `UI/CommandBar/{CommandBarView,CommandBarDroplet}.swift`, `UI/Settings/{CommandBarSettings,CommandBarAppShortcutsView}.swift`, `Core/CommandBarStrings.swift`. Reference animation: `Resources/Gifs/commandBar.gif` (820x472, 16 frames).

Conventions used below: `[WIN]` marks a wording/behaviour substitution (the original Mac text is always given next to it). `⌘`=Command, `⌥`=Option, `⌃`=Control on macOS. "Mac keys -> Windows keys" are listed once in section 1.4.3. SF Symbol names are given with a suggested Segoe Fluent Icons code point (the code points are suggestions - verify each against the Segoe Fluent Icons catalogue; glyph names are given in brackets; where two are listed the first is preferred).

---

### 1.1 Identity, hub tile, entry points

| Item | Value |
|---|---|
| Feature id (Windows) | `command-bar` (keep) |
| Hub title (`pageTitle`) | **Command Bar** (current Trayloom value "Command bar" is wrong; capital B) |
| Hub description (`hubDescription`) | "One field that finds and runs everything the app does" |
| Hub group | Tools (`AppFeature.commandBar.group == .tools`) |
| Hub / panel symbol | SF `command` -> `U+E721` [Search] (⌘ glyph has no Windows twin; keep the same glyph everywhere the feature is shown: hub tile, quick panel tile, island tile, tray panel) |
| Quick panel (menu panel) tile | title "Command Bar", caption (`panelCaption`) "Type to find and run anything", shows the global shortcut as hint, tile visibility key `panelUtilityCommandBar` (bool, default `true`) |
| Island tile | `NotchModule` item `commandBar`, title "Command Bar", symbol `command`; action = open the bar |
| Needs permission | Accessibility on macOS (menus of other apps, selection, typing at the caret). Windows: **none** (UI Automation + `SendInput` need no consent; UIPI blocks injecting into elevated windows - see 1.9) |
| Opens via | (1) global shortcut, (2) button "Open the bar now" in its Settings page, (3) quick-panel/island tile, (4) the row's own global shortcut (per-row shortcuts, 1.7.5) which may skip the bar altogether |
| Hub availability | `AppFeature.commandBar.isAvailable` (feature installed). Uninstalling the feature clears every cache (catalog, app scan, windows, menus, emoji, panes) and hides the bar |

Default global shortcut: **⌥Space** (`GlobalShortcut.commandBarDefault`, key Space + Option) and the registration is **off** until the user enables "Global shortcut to open the bar" (`commandBarShortcutEnabled = false`). The footer of the bar always prints the currently saved shortcut (the GIF shows `⌃⌥⌘B` because the demo user customised it).
`[WIN]` Default = **Alt+Space** (the same combo PowerToys Run uses; register with `RegisterHotKey(MOD_ALT, VK_SPACE)`; if registration fails show the orange "macOS rejected this shortcut. Choose another one." line as `[WIN]` "Windows rejected this shortcut. Choose another one."). The existing port's comment that Alt+Space is "reserved" is true only while nothing registers it; registering it suppresses the window system menu - acceptable and identical to PowerToys. Fallback offered in UI: Ctrl+Alt+Space.

---

### 1.2 Visual layout (from `commandBar.gif` frames 0 and 30/last + `CommandBarView.swift`)

#### 1.2.1 What the two reference frames show
* **Frame 0** (query `bat`): dark grey, slightly translucent rounded panel (about 750x190 px in the 820x472 gif, i.e. 560 pt wide scaled 1.34x; corner radius 22 pt, "continuous" squircle corners) floating on a near-black desktop with a soft drop shadow. Top strip: the **planet mark** (brand mark, 22x22 pt, 85 % opacity, dark in light theme, white in dark theme) at the left, then the typed text `bat` in 16 pt regular; at the far right a **round clear (X) button** (`xmark.circle.fill`, 13 pt, tertiary grey). A hairline divider. Below it one **selected row**: a rounded highlight (10 pt radius, accent blue at 14 % opacity) containing a 30x30 rounded-rectangle "plate" with a battery glyph, the title **Battery** where the matched letters **Bat** are accent-blue bold and `tery` is normal black/white medium 13 pt, a second line `on power` (10.5 pt secondary), and on the right the answer chip **80%** (accent blue, 12 pt semibold rounded, monospaced digits) followed by the return glyph `↩` (10 pt, tertiary, drawn on the selected row only). A hairline divider, then the **footer**: left keyboard glyph + the global shortcut (`⌃⌥⌘B`, 9 pt semibold rounded tertiary), right `⌘K` `Actions` `↑↓` `↩` `Esc` (9 pt, tertiary). (The GIF predates the `⌃P ⌃N` hint; the current code prints `⌃P ⌃N ↑↓`.)
* **Frame 30 / last** (query `screen brightness`, Actions panel open, ⌘K): same field strip (mark, query text, clear X). The divider is followed by the header **ACTIONS** (uppercase, 9 pt bold, +0.5 tracking, tertiary, left inset 16) and four action rows at 7 pt vertical padding: highlighted first row (accent 14 %) with icon `pin` + "**Pin to the top**" + return glyph at right; then `A|` (text cursor, `character.cursor.ibeam`) "Give it your own name"; `eye.slash` "Never show this"; `arrow.counterclockwise` "Forget how often I use this". Footer: shortcut at left; `↑↓ ↩ Esc` at right (no "⌘K Actions" while the Actions panel itself is open, because `canOpenActions` is only true in search mode).

#### 1.2.2 Exact metrics (SwiftUI source)
| Element | Value |
|---|---|
| Panel width | 560 pt fixed (`CommandBarView.width`); height = content (WPF: `SizeToContent=Height`) |
| Panel corners | 22 pt continuous, clip everything to it |
| Backdrop (`.window` presentation) | `HUDBackdrop(cornerRadius: 22, contrast: .high)` = behind-window blur (HUD material) + plate of black 55 % (dark) / white 50 % (light). Reduce-transparency => opaque. `.droplet` presentation: solid black. `.island`: transparent (island draws it) |
| Colour scheme | follows system in window mode; forced dark in droplet/island modes |
| Accent | system accent (`Color.accentColor`) for highlight, selection, chips, answer values. `[WIN]` = `SystemParameters`/UISettings accent (`UISettings.GetColorValue(UIColorType.Accent)`) |
| Field strip | HStack spacing 10, padding horizontal 16 / vertical 14; min height 50 (`fieldHeight`) |
| Mark | brand mark 22 wide, 85 % opacity, frame 22x22, hit-testing off; it is the **drag handle** (open-hand cursor; tooltip `dragHint` "Drag to move · Double-click to recenter"); `[WIN]` use the Trayloom mark, never the saturn |
| Naming/argument chip (left of field) | entry title, 12 pt semibold accent, capsule fill accent 14 %, padding 8x3 (only in modes `naming` and `argument`) |
| Text field | plain, 16 pt, no autocorrect, accessibility label "Command Bar"; placeholder "Type what you want to do" (`searchPlaceholder`); in argument mode placeholder is "%d to %d" with the range (e.g. "0 to 100"); in naming mode "The name you call it" |
| Clear button | `xmark.circle.fill`, 13 pt, tertiary; visible only when the query is non-empty; click sets query "" |
| Compact inline hints (compact home only) | `↓` (9 pt semibold rounded) + "Suggestions" (9 pt) + `Esc` (9 pt semibold rounded, 4 pt leading gap), tertiary, one line, between the field and the clear button |
| Divider | 1 px system separator between sections (field / chips / list / footer) |
| Category chip row | horizontal scroll, no indicators; padding 16 horizontal, 9 vertical; chip spacing 5 |
| Category chip | capsule, padding 7x3; text 10 pt (semibold when active); source icon 8.5 pt semibold (not on "All"); inactive: secondary text on primary 6 % fill; active: accent text, accent 18 % fill, 1 px accent 45 % stroke |
| "Try" row | shown only while suggestions are shown with no category: "TRY" (9 pt bold, +0.5 tracking, tertiary) + one capsule per example (10 pt rounded secondary, padding 6x2, fill primary 6 %); padding left/right 16, top 10; clicking an example sets the query |
| Examples (`CommandBarView.examples`) | `100 km to mi`, `2+2*3`, the lowercased localized word for Battery (`battery`) only if the machine has an internal battery, `fire` |
| List container | padding 8 horizontal/8 vertical, row spacing 1; lazy list |
| List height | content height up to **452 pt** (`listCeiling`); > 14 rows => fixed 452 pt tall + scroll indicator, else exactly content height. Rows <=14 are measured, >14 are lazy |
| Section header (browse/category) | uppercase title, 9 pt bold, tracking 0.5, tertiary; padding 8 horizontal, 6 top, 3 bottom |
| Row | HStack spacing 10; padding 9 horizontal, 7 vertical (9 vertical for answer rows); selection background RoundedRect radius 10, accent 14 %; hover selects (only if the pointer actually moved since opening) |
| Row icon | 30x30 box. Glyph rows (`symbol`): rounded plate radius 8, fill primary 6 % (accent 16 % when the row `isActive`), glyph 13.5 pt semibold at primary 85 % (accent when active). App/file icons: drawn bare at 28x28 (no plate). Clipboard image: 28x28 thumbnail, radius 7. Colour: swatch 22 pt. Active rows add a **7 pt green dot** at the top-trailing corner (offset 2.5,-2.5, ring 1.2 pt in the window background) |
| Title | 13 pt medium primary, one line, truncated at the tail; matched letters = accent colour + bold (offsets from `highlightOffsets`); answer rows: 17 pt semibold rounded, monospaced digits (not for colour values), truncate in the middle |
| Subtitle | 10.5 pt secondary, one line, middle truncation; orange 10.5 pt when `trouble` (see 1.2.4); omitted when empty |
| Right accessory (priority order) | 1) while ⌘ is held and index < 9: chip "⌘1".."⌘9" (10 pt semibold rounded accent, padding 6x2.5, radius 5, accent 12 % fill); 2) `answerValue` (12 pt semibold rounded accent, monospaced digits); 3) shortcut chip (user-bound row shortcut, else the feature's own shortcut, else the menu item's own shortcut) - 10 pt semibold rounded secondary, padding 6x2.5, radius 5, primary 6 % fill; then 4) return glyph `return` 10 pt semibold tertiary, always laid out (reserved width), visible only on the selected row |
| Footer | padding 16/8; keyboard glyph (8.5 pt) + current global shortcut text (9 pt semibold rounded), all tertiary; spacer; [if the selected row has an Actions panel] "⌘K" (9 pt semibold rounded) + "Actions" (9 pt, 4 trailing); hints "⌃P ⌃N ↑↓ ←→" while suggestions+chips are visible else "⌃P ⌃N ↑↓"; [if selected row id == `math.result`] "⇥" + "Continue"; return glyph (8 pt) + "Esc". Hidden in compact home |
| Empty state | padding 24 vertical; mark 26 wide at 30 % opacity; "Nothing here by that name." (12 pt secondary); capsule button "See suggestions" (11 pt semibold accent on accent 12 %, padding 10x4); Return (or button) clears the query and leaves the category |
| Accessibility | each row is one element labelled `title, subtitle, answerValue, shortcut, "on" (if active), "Pinned" (if pinned), trouble text` joined by ", " |

#### 1.2.3 Cards (non-search modes)
* **Argument card** (`mode == .argument`): divider; HStack icon (30) + entry title (13 medium) padding 17/top 12; then hint line "Return applies · Esc goes back" (10.5 pt tertiary, padding 17, bottom 12). The field strip shows the title chip and the range placeholder.
* **Confirm card**: divider; rounded rectangle (radius 12, red 9 % fill, padding 12, outer padding 12) holding: icon, VStack(prompt 13 pt semibold e.g. "Restart the Mac?"; hint "Return confirms · Esc cancels" 10.5 secondary), Spacer, **Cancel** (bordered small; text "Cancel") and **Confirm** (prominent small). Mouse path must work: Cancel = `stepBack`, Confirm = Return.
* **Naming card**: icon + row title (13 medium) at top; line below = alias warning (orange) or "Return applies · Esc goes back" (tertiary), 10.5 pt, padding 17. Field strip shows the title chip + placeholder "The name you call it".
* **Shortcut capture card**: icon + title + (if one exists) current shortcut capsule (11 pt semibold rounded accent, accent 14 % capsule, padding 7x3); line below = warning (orange) or "Press the keys you want · Delete clears it · Esc goes back".
* **Actions list**: header "ACTIONS" (above), rows padding 9x7, icon 30 box with 13.5 pt semibold glyph, title 13 medium, selected row fill accent 14 % (red 12 % for destructive rows, text+icon red), return glyph on the selected row. Container padding 8.
* **Uninstall review / Homebrew confirm cards**: cross-feature, rendered inline by the Uninstaller; see 1.10 (only the Uninstaller feature spec defines their content).
* **Uninstall warning** (`uninstallWarning`): orange callout label (callout font, triangle glyph) padded 17/12 directly under the field.

#### 1.2.4 Row "trouble" states
`needsSetup(feature, page)` -> subtitle replaced by orange "Turn on %@ in Settings" (%@ = feature title); Return closes the bar and opens Settings on that page. `needsPermission` -> orange "Needs permission · Return asks" (Return runs the action anyway, which makes macOS prompt). `[WIN]` `needsPermission` is never produced (no equivalent) except the "Finder automation denied" Empty-Trash case which has no Windows analogue; keep the string but do not emit it.

#### 1.2.5 Presentation modes
| Mode | When | Notes |
|---|---|---|
| `.window` | default | floating non-activating panel (level floating, joins all Spaces, full-screen auxiliary), no shadow tricks needed; fades with the system's default panel animation |
| `.droplet` | Dynamic Island enabled + `notchCommandBar` (default `true`) + `notchCommandBarStyle == droplet` (default) and the island knows its anchor | panel hangs 16 pt below the island, centred on it; a liquid "drop" animation (1.8) carries the companion mascot into the field; black backdrop; panel `animationBehavior = none` |
| `.island` | `notchCommandBarStyle == island` | the same SwiftUI view is drawn *inside* the opened island (transparent backdrop); island owns the keyboard routing |
`[WIN]` Only `.window` is part of the base replica. `.droplet`/`.island` belong to the Island + Mascot features (they are toggled in the Island "Companion" settings page: "Command Bar in the island" - hint "With the shortcut, the Command Bar comes out of the island with the companion as its face. It reacts while you search."; style picker values `droplet` / `island`). Implement after the Island feature exposes `CommandBarDropSource()`.

---

### 1.3 Strings (enUS, verbatim)

`CommandBarFeatureStrings.enUS` (from `Core/CommandBarStrings.swift`). `·` is U+00B7. Strings with `%@`/`%d` are format strings.

| Key | English |
|---|---|
| pageTitle | Command Bar |
| hubDescription | One field that finds and runs everything the app does |
| panelCaption | Type to find and run anything |
| settingsCaption | One shortcut opens a field over whatever you are doing. Type a few letters, press Return and it happens. Nothing you type is saved. |
| openButton | Open the bar now |
| searchPlaceholder | Type what you want to do |
| suggestionsLabel | Suggestions |
| noResultsTitle | Nothing here by that name. |
| noResultsAction | See suggestions |
| argumentRangeFormat | %d to %d |
| argumentHint | Return applies · Esc goes back |
| confirmHint | Return confirms · Esc cancels |
| kindApp | App |
| kindClipboard | Paste from history |
| kindSnippet | Insert snippet |
| soundOutputSubtitle | Switch sound output |
| soundOutputCurrent | Current output |
| volumeTitle | Volume |
| brightnessTitle | Display brightness |
| brightnessKeywords | screen |
| keepAwakeForFormat | Keep awake for %@ |
| needsSetupFormat | Turn on %@ in Settings |
| needsPermissionHint | Needs permission · Return asks |
| actionCheckAppUpdates | Check for app updates |
| actionCleanURL | Clean the copied link |
| actionOpenSettings | Open Settings |
| answerBatteryLabel | Battery |
| answerBatteryCharging | charging |
| answerBatteryPlugged | on power |
| answerMemoryLabel | Memory |
| answerMemoryFormat | %@ of %@ in use |
| answerStorageLabel | Storage |
| answerStorageFormat | %@ available of %@ |
| copyHint | Return copies |
| copyFailed | Couldn’t copy |
| kindWindow | Window |
| quitFormat | Quit %@ |
| quitConfirmFormat | Quit %@? |
| kindMenu | Menu command |
| actionsTitle | Actions |
| actionsHint | Actions |
| reuseHint | Continue |
| actionPin | Pin to the top |
| actionUnpin | Unpin |
| actionName | Give it your own name |
| actionRename | Change the name it answers to |
| actionHide | Never show this |
| actionForget | Forget how often I use this |
| aliasPlaceholder | The name you call it |
| aliasTakenFormat | %@ already answers to that |
| sourcesTitle | What the bar searches |
| sourcesCaption | Turn off what you never want to see. Your own actions always stay. |
| sourceActions | Vorssaint actions `[WIN]` Trayloom actions |
| sourceApps | Apps |
| sourceMenus | Menu commands of the app in front |
| sourceWindows | Open windows |
| sourceQuitApps | Quit an app |
| sourceSettingsPages | Settings pages |
| sourceSnippets | Snippets |
| sourceClipboard | Clipboard history |
| sourceEmoji | Emoji |
| sourceFolders | Folders |
| sourceAnswers | Answers about this Mac `[WIN]` Answers about this PC |
| sourceCalculator | Sums and conversions |
| namedTitle | Names you gave |
| namedEmpty | Nothing named yet. Open the bar, pick a row and press the actions key. |
| pinnedTitle | Pinned |
| pinnedEmpty | Nothing pinned yet. |
| hiddenTitle | Never shown |
| hiddenEmpty | Nothing hidden. |
| forgetAllButton | Forget what I use most |
| tryTheseLabel | Try |
| stillLooking | Still looking |
| removeButton | Remove |
| confirmButton | Confirm |
| soundMute | Mute the sound |
| soundUnmute | Turn the sound back on |
| stateOn | on |
| powerSleep | Sleep |
| powerRestart | Restart |
| powerRestartConfirm | Restart the Mac? `[WIN]` Restart the PC? |
| powerShutDown | Shut down |
| powerShutDownConfirm | Shut down the Mac? `[WIN]` Shut down the PC? |
| powerLogOut | Log out `[WIN]` Sign out (keep "Log out" if exact parity is preferred) |
| powerLogOutConfirm | Log out? |
| wifiOn | Turn Wi-Fi on |
| wifiOff | Turn Wi-Fi off |
| kindEmoji | Emoji |
| kindFolder | Folder |
| answerDateLabel | Today |
| answerTimeLabel | Time now |
| kindAnswer | Answer |
| kindSelection | Selection |
| selectedTitle | Selected |
| selectionCopy | Copy it |
| selectionSearch | Use it in the search |
| selectionUpper | UPPERCASE |
| selectionLower | lowercase |
| selectionTitleCase | Title Case |
| selectionShelf | Keep it on the shelf |
| selectionCount | Count it |
| selectionCountFormat | Words: %d, characters: %d |
| sourceSelection | What is selected |
| kindLink | Your shortcut |
| linkSearchHint | Type what to look for after the name |
| linksTitle | Your shortcuts |
| linksEmpty | Nothing saved yet. Add a site, folder, search or script you use every day. |
| linkDestinationLabel | Where it goes |
| linkKindLink | Site or link |
| linkKindPlace | Folder or file |
| linkPlaceholdersHint | Drop one of these in and it gets filled in when it opens |
| placeholderQuery | what you type |
| placeholderClipboard | what you copied |
| placeholderSelection | what is selected |
| placeholderDate | today |
| privacyNote | Search runs on this Mac with no account or cloud, and what you type leaves it only through sites you open or scripts you add. `[WIN]` …on this PC… |
| actionShortcut | Give it a shortcut |
| actionShortcutChange | Change the shortcut |
| actionShortcutRemove | Take the shortcut off |
| shortcutCaptureHint | Press the keys you want · Delete clears it · Esc goes back |
| rowShortcutsTitle | Rows with their own shortcut |
| rowShortcutsEmpty | No row has its own shortcut yet. Open the actions on any row to give it one. |
| appCenterTitle | App shortcuts |
| appCenterCaption | Open apps with their own shortcuts, even when the bar is closed. Set search names and pin favorites here. |
| appAliasLabel | Alias |
| appShortcutLabel | Shortcut |
| appShortcutsFilter | With shortcuts |
| appShortcutRecord | Record… |
| rowShortcutsLimitFormat | You can assign up to %d shortcuts. Remove one to add another. |
| turnOnFormat | Turn on %@ |
| turnOffFormat | Turn off %@ |
| everythingTitle | Everything it can do |
| categoryAll | All |
| restartAppFormat | Restart %@ |
| forceQuitAppFormat | Force Quit %@… |
| forceQuitAppConfirmFormat | Force quit %@? Unsaved changes will be lost. |
| uninstallAppFormat | Uninstall %@… |
| openInBrowser | Open in browser |
| linkKindScript | Script |
| scriptHint | Choose an executable file. Type its name followed by what you want to send. It runs when you pause and shows the result here. |
| scriptRunsWithoutArgument | Also run when its name is typed on its own |
| scriptRunsDirectly | Run from its global shortcut without opening the bar |
| scriptRunFailed | Couldn’t run this file |
| scriptSearchHint | Type what to send after the name |
| scriptBareSearchHint | Runs on its own, or type what to send |
| linkAddButton | Add shortcut |
| positionCaption | Drag the mark at the left of the field to put the bar anywhere on the screen; double-click the mark to recenter it. |
| resetPositionButton | Recenter the bar |
| shortcutToggle | Global shortcut to open the bar |
| dragHint | Drag to move · Double-click to recenter |
| actionRevealInFinder | Show in Finder `[WIN]` Show in File Explorer |
| sourceMacSettings | System Settings panes `[WIN]` Windows Settings pages |
| sourceFiles | Files |
| filesTitle | Search your files |
| filesCaption | Name the folders to look in and the bar finds files by name as you type. Nothing is indexed and no permission is asked for: it uses the Mac’s own search and offers only what you can already see in Finder. `[WIN]` "…it uses Windows Search and offers only what you can already see in File Explorer." |
| filesEmpty | No folders yet, so the bar looks for no files. |
| filesAddFolder | Add a folder |
| filesIgnoreCaption | Names never worth showing: a whole folder or file name, or an extension written as *.log. |
| filesIgnorePlaceholder | A folder or file name |
| filesIgnoreAdd | Add |
| compactModeToggle | Compact mode |
| compactModeCaption | Bar opens without suggestions. Results appear as you type. |
| emojiSkinToneLabel | Skin tone |
| emojiSkinToneCaption | By default, emoji that can take a tone use this one. Open the actions on an emoji row to choose a different one. |
| asciiLayoutToggle | Switch to an ABC layout while the bar is open `[WIN]` Switch to an English (US) layout while the bar is open |
| asciiLayoutCaption | The field types Latin characters whatever layout was active, and the previous layout comes back when the bar closes. |

Strings the bar borrows from other string tables (all enUS):
* Shared `Strings`: Cancel (`uninstallerCancel`), "Search apps" (`uninstallerPickerSearch`), "Done" (`supportIntroDoneButton`), "Use at least Control, Option or Command with a key." (`shortcutInvalid`, `[WIN]` "Use at least Ctrl, Alt or Win with a key."), "Press keys" (`shortcutPressKeys`), "Nothing was captured. macOS or another app already uses that combination. Try another one." (`shortcutNotCaptured`, `[WIN]` Windows), "This shortcut is already used by %@." (`shortcutConflictFormat`; %@ = owning row title, another global-shortcut role title, a window-layout action title, or "macOS" `[WIN]` "Windows"), "macOS rejected this shortcut. Choose another one." (`shortcutUnavailable`).
* Other features: "Edit" (`ScreenshotStrings.editButton`, the link row Edit button), "Name"/"Save" (`SnippetStrings.nameLabel/saveButton`, link editor), "Choose…" (`RadialMenuStrings.chooseButton`, choose file/folder), "More options" (`RecorderStrings.moreOptions`, the two disclosure rows), "Uninstall Application" / "Uninstall app selected in Finder" (`uninstallerCommandBarBrowseTitle/FinderTitle`), "Show in Command Bar"/"Adds running processes to the Command Bar, so you can find and kill them without opening Settings." (`KillProcessStrings.commandBarToggle/Caption`, belongs to the Kill Process page).
* HUD messages: "Paste a valid URL." / "Nothing to clean." / "URL cleaned." / "Removed %@" (URL cleaner), copy HUD shows the copied value or "Couldn’t copy".

---

### 1.4 Behaviour of the field and keyboard

#### 1.4.1 Open / close lifecycle (service)
1. Shortcut pressed -> `toggle()` (visible ? hide : show). `show()`: the panel takes keyboard focus *before* anything is prepared so characters typed right after the shortcut wait for the field; a new `presentationID` is created; every cache of the previous opening (script results, file results, folded index, selection, windows, menus) is dropped; `mode = .search`; query "" ; `activeCategory = nil`; "peek home" reset; pending keys cleared; the ASCII layout switch (1.7.4) runs; panel ordered front. Home is hydrated on the **next main-loop turn** (preferences caches, catalog, running apps, then background loads) and a hidden/replaced presentation ignores its results.
2. Background loads (all guarded by the presentation id; a result for a closed/older presentation is dropped): Automation status (Finder, `[WIN]` n/a), boot-volume free space, Wi-Fi power, battery+memory, installed apps (re-scanned on **every open**; if unchanged nothing is rebuilt), Settings panes (once per launch/language), open windows (re-walked at most every **4 s**), menu bar of the front app (cached per app for **8 s**), selection (read once per opening), Kill-Process list, uninstall selection. Answers (storage/battery/memory/Wi-Fi) coalesce into **one** catalog rebuild 30 ms after the first lands.
3. `hide()`: removes all monitors; restores borrowed layout (next main-loop turn); clears script/file runners; ends shortcut capture; drops selection/kill/uninstall-selection rows; remembers the last typed query **in memory only**; clears query. Nothing typed is ever written to disk (only keyed digests, 1.6.4).
4. Auto-close: any click outside (local + global mouse-down monitors; the Accessibility Keyboard is exempt `[WIN]` On-Screen Keyboard `osk`/TabTip window exempt), activation of any other application, Esc from home, the global shortcut again, Return on a row that does not `keepsBarOpen`.
5. Opening while already visible ("reopening") only refocuses the field.

#### 1.4.2 Keys (search mode)
| Key (macOS) | Action |
|---|---|
| Esc | `stepBack()`: in actions -> leave; naming -> back to actions; capture -> back to actions; argument/confirm -> restore saved query and search; search: clear a non-empty query -> else leave category -> else un-peek the list -> else close |
| Return / keypad Enter | run selected (see 1.5.3). With no rows and non-empty query: clears the query |
| ↑ / ↓ | move selection with wrap-around; ↓ on a *collapsed compact bar* first "peeks" (shows the list) |
| ⌃P / ⌃N | same as ↑ / ↓ |
| ← / → | walk category chips **only while the field is empty** (otherwise the keys belong to the caret); stops: All, then chips in order |
| Tab | complete: on a calculator answer (`math.result`) replaces the query with the reusable result expression (plain, ungrouped, locale decimal, negative values in parentheses) and keeps undo; on any other non-answer row replaces the query with the row's title (or emoji `:`+name; `matchTitle` wins); the typed original is remembered for learning |
| ⌘K | open Actions panel for the selected row |
| ⌘P | toggle pin of the selected row (only if `acceptsPin`) |
| ⌘, | close the bar and open Settings -> Command Bar page |
| ⌘Return | show the selected row in Finder when it has a path (`revealPath`), else normal Return |
| ⌘1…⌘9 (incl. keypad) | run the visible row at that position |
| ⌘A/C/X/V | standard text edit on the field |
| ⌘Q/W/M/H | swallowed (they would quit/close/hide the host app) |
| Any other key while a confirm is open | cancels the confirm and the key goes to the field |
| Held ⌘ | shows ⌘1–⌘9 chips on the first nine rows |
| Input methods | while an IME is composing (marked text) Return/arrows/Tab belong to the IME - the bar never intercepts them |
| Mouse | click runs the clicked row by **identity** (not index); hover selects only when the pointer has really moved; click on the mark = drag, double-click on the mark = recentre |

#### 1.4.3 Mac -> Windows key map `[WIN]`
⌘ -> Ctrl, ⌥ -> Alt, ⌃ -> Ctrl. Because ⌘P (pin) and ⌃P (up) would both become Ctrl+P: **Pin = Ctrl+Shift+P**, **Ctrl+P / Ctrl+N** keep Up/Down (matches the printed hint). Mapping: ⌘K->Ctrl+K, ⌘,->Ctrl+, , ⌘Return->Ctrl+Enter, ⌘1-9->Ctrl+1-9 (or Alt+1-9; hold Ctrl shows the chips), footer glyphs: "Ctrl+K Actions", "Ctrl+P Ctrl+N ↑↓ ←→", "⇥ Continue" (Tab), "↵ Esc". Shortcut display strings use the Trayloom convention "Ctrl+Alt+B" instead of glyphs. Swallow Alt+F4? (no - leave) ; Esc/Enter/arrows same.

#### 1.4.4 Modes of the field (`CommandBarService.Mode`)
`search`, `argument(entryID)` (asks a number), `confirm(entryID)`, `uninstallReview`, `uninstallHomebrewConfirm`, `actions(entryID)`, `naming(entryID)`, `capturingShortcut(entryID)`. Entering a mode other than search sets `isCompactHome=false` and re-fits the panel. Only `search` is run by clicks and ⌘1-9.

Run algorithm for a chosen row (`run(entry)`):
1. `trouble == needsSetup(page)` -> hide, open Settings on page, stop.
2. `uninstallAppURL != nil` -> begin the inline uninstall review (1.10).
3. `confirmationPrompt != nil` -> `savedQuery = query`, `mode = confirm`.
4. `numericRange != nil`: if the query ends in a number (`splitTrailingNumber`) -> clamp to range and finish; else if `numericIsOptional` -> finish with nil; else enter `argument` mode (query cleared, `savedQuery` kept).
5. else `finish(entry, nil)`.
`finish`: `recordUsage`; copy `queryWhenRun`/`selectionWhenRun` (closing the bar wipes the field); if `keepsBarOpen` run in place; else mascot farewell `.happy`, `hide()`, then `entry.run(value)`. Most `run` closures execute after a **beat** (`afterBeat`, default **0.15 s**, 0.1 s for windows that want focus) so the non-activating panel is gone before capture/paste/activate.
Argument mode Return: `argumentValue(text, range)` - trim, accept optional trailing `%`, 1-4 digits only, clamp into range; anything else beeps and stays.
Typed number shortcut: "brightness 40" / "volume 25%" - `splitTrailingNumber`: >=2 whitespace tokens, last is digits with optional `%` -> (text, number). The number is only detached when some catalog row with a `numericRange` matches the remaining text (`numericCommandMatches`), otherwise it stays search text ("code 1234").

---

### 1.5 Result kinds (row ids, prefixes and sources)

Each row carries: `id`, `stableKey` (what pins/aliases/shortcuts/hidden are filed under; apps use bundle id so a moved app keeps them; `app.bundle.<id>` else `app.<path>`; uninstall rows `uninstall.bundle.<id>`), `title`, `englishTitle`, `subtitle`, `keywords`, icon, `shortcut`, `menuShortcut`, `isActive`, `trouble`, `numericRange`, `numericIsOptional`, `confirmationPrompt`, `answerValue`, `isAnswer`, `countsUsage`, `matchTitle`, `keepsBarOpen`, `takesArgument`, `revealPath`, `uninstallAppURL`, `run(Int?)`. `needsPrompt` = confirmation OR (numeric and not optional) OR needsSetup OR uninstall - such rows never run silently from a row shortcut.

Non-English UI: every built-in row is built twice (UI language + English); the English title is appended to the keywords and stored as `englishTitle` so "screenshot" works in any language. `[WIN]` Only if Trayloom ships more than English; otherwise skip.

#### 1.5.1 Source (category) table - `CommandBarSource`
Raw values are persisted in `commandBarDisabledSources` (comma list of *disabled* sources; `actions` can never be disabled). Order below = Settings toggle order.

| rawValue | Toggle label (enUS) | Settings icon (SF -> Segoe) | Row id prefix | `rankBias` | accepts pin | accepts alias/shortcut |
|---|---|---|---|---|---|---|
| actions | Vorssaint actions (always on, toggle disabled) | wand.and.rays -> `U+E945` [Lightning/Magic] | none (everything not matched below, incl. `action.*`, `toggle.*`, `uninstall.browse/finder`, `emoji.browse`, `kill.browse`) | 0 | yes | yes |
| apps | Apps | square.grid.2x2 -> `U+E80A` [GridView] | `app.` | **+80** | yes | yes |
| menus | Menu commands of the app in front | filemenu.and.selection -> `U+E700` [GlobalNavButton] | `menu.` | **-80** | no | no |
| windows | Open windows | macwindow -> `U+E737` [Window] | `window.` | 0 | yes | no |
| quitApps | Quit an app | xmark.circle -> `U+E711` [Cancel] | `quit.` | 0 | no | yes |
| uninstallApps | Uninstaller (name from Strings) | trash -> `U+E74D` [Delete] | `uninstall.` | 0 | no | no |
| settingsPages | Settings pages | gearshape -> `U+E713` | `settings.` | **-40** | yes | yes |
| macSettings | System Settings panes `[WIN]` Windows Settings pages | gearshape.2 -> `U+E713` [Settings] | `macsettings.` | 0 | yes | yes |
| snippets | Snippets | text.append -> `U+E8D2` [Font/Text] | `snippet.` | 0 | yes | yes |
| clipboard | Clipboard history | doc.on.clipboard -> `U+E77F` [Paste] | `clipboard.` | 0 | no | no |
| emoji | Emoji | face.smiling -> `U+E76E` [Emoji2] | `emoji.` | 0 | no | yes |
| folders | Folders | folder -> `U+E8B7` [Folder] | `folder.` | 0 | yes | yes |
| answers | Answers about this Mac | info.circle -> `U+E946` [Info] | `answer.` | 0 | yes | yes |
| calculator | Sums and conversions | equal.square -> `U+E8EF` [Calculator] | none (rows `math.result`, `units.result`, `color.result`, `color.preview`, `date.result`) | 0 | yes | yes |
| selection | What is selected | text.cursor -> `U+E8E9` [IBeam?; use `U+E8D2` if missing] | `selection.` | 0 | no | no |
| links | Your shortcuts | bookmark -> `U+E8A4` [FavoriteStar] | `link.` | 0 | yes | yes |
| files | Files | doc.text.magnifyingglass -> `U+E721` [Search] | `file.` | **-40** | no | no |
| killProcess | Kill Process (name from KillProcessStrings.pageTitle) | xmark.octagon -> `U+EA39` [ErrorBadge] | `kill.` | 0 | no | no |

Source resolution: `source(ofRowID:)` returns the first source whose prefix the id starts with, else `actions`. A row of the Actions list is shown in the Actions browse category if `source == actions` **or** its id is in `actionBrowseRowIDs = {emoji.browse, kill.browse, uninstall.browse, uninstall.finder}` and that row's own source is enabled. Row-shortcut/alias identifiers must never drift (`emoji.browse`, `kill.browse` persisted).

Category chips (fixed order `chipOrder`): actions, apps, clipboard, windows, menus, settingsPages, macSettings, snippets, emoji, folders, links. A chip appears only if the source is enabled **and** can have content: apps/macSettings always; windows = (Switcher or Window layout feature installed) AND Accessibility; menus = Accessibility AND front app is not the bar's app; clipboard = has any rows; emoji = any non-hidden; actions = any action row not hidden; settingsPages/snippets/folders/links = any non-hidden row of that prefix. `[WIN]` Accessibility conditions are always true; windows/menus use their own availability checks.

#### 1.5.2 Row kinds - what each shows and runs

(The complete built-in action catalog with ids/titles/keywords/Windows API is in section 1.11.)

| Kind | Id / source | Title / subtitle / right side | Return does |
|---|---|---|---|
| **App** | `app.<id>`; apps | name; subtitle "App"; real app icon; green dot if running; keywords = alternate names + disk name (if different) + pinyin (+initials) for Han titles | launches (or activates) the app. Mac: `NSWorkspace.openApplication`. The bar's own app is never offered. Actions panel adds Quit/Restart/Force Quit (if running) and Uninstall… |
| **Window** | `window.<windowId>`; windows | window title; subtitle app name (or "Window"); app icon; keywords "Window <app>"; skipped when title empty or equals app name; `countsUsage=false` | activates that exact window after 0.1 s (`WindowActivator.activate`) |
| **Menu command** | `menu.<index>.<title>`; menus | title; subtitle breadcrumb "App › Menu › Submenu" (a name is never repeated right after itself); right chip = the item's own shortcut text; keywords "Menu command <app> <path>"; deduped by path+title; `countsUsage=false` | presses the menu item (AXPress); beep on failure |
| **Clipboard paste** | `clipboard.<uuid>`; clipboard | preview text (or image label "Image"); subtitle "Paste from history"; icon per kind (text/image thumb/first file icon); `countsUsage=false`; only on non-empty typed query (cap 4 in global search, 40 in category) | closes, writes the item to the clipboard, waits for modifiers released, presses ⌘V |
| **Snippet** | `snippet.<uuid>`; snippets | name; subtitle "Insert snippet"; keywords = trigger + folder; only enabled snippets with names | types the expansion at the caret |
| **Emoji** | `emoji.<identity>`; emoji (search needs leading `:`; or Emoji category) | title `"<glyph>  <name>"` (two spaces); subtitle "Emoji"; ranking text = `name`; keywords name + aliases + "Emoji"; skin tone from setting | types the glyph at the caret (not paste) |
| **Folder** | `folder.<path>`; folders | name (localized display name); subtitle "Folder"; keywords "Folder <last path component>"; real folder icon; reveal path | opens the folder: Downloads, Documents, Desktop, Movies, Pictures, Music, Home, Applications |
| **File** | `file.<path>`; files | file name; subtitle path abbreviated with `~`; file icon; `countsUsage=false`; keywords "Files" | opens the file (HUD "doc.questionmark"+name if it vanished) |
| **Answer** (about the machine) | `answer.battery/memory/storage/date/time` | title label; subtitle detail; right chip value (accent); `countsUsage=false`; only offered when the typed first token (>=3 chars) is a **prefix of the row title or its English title** | copies the value (HUD with the value or "Couldn’t copy") |
| **Calculator answer** | `math.result`/`units.result`/`color.result`/`color.preview`/`date.result` (and script answer) | big 17 pt title = result; subtitle "Return copies" (or `<expr completed> · Return copies` when brackets were auto-closed; date answers: detail line) | copies the result; Tab on `math.result` reuses it |
| **Selection** | `selection.*` | see 1.5.4 | |
| **Your shortcut / link / script** | `link.<uuid>`; links | name; subtitle: script => "Runs on its own, or type what to send" (runsWithoutArgument) else "Type what to send after the name"; link/place => "Type what to look for after the name" when it contains `{query}` else "Your shortcut"; icon `link`/`folder`/`terminal` | opens the URL / path or runs the script (1.7.6) |
| **Quit app** | `quit.<bundle id or pid>`; quitApps (joins the pool only when the query matches the verb "Quit %@") | "Quit <Name>"; subtitle "App"; keywords English "Quit <Name>"; confirm prompt "Quit <Name>?" | app activated, 0.12 s later graceful terminate (so a "Save changes?" dialog is visible) |
| **System Settings pane** | `macsettings.<bundleID>`; macSettings | pane name (as macOS shows it); subtitle "System Settings panes" `[WIN]`; icon gearshape.2; keywords = translated search terms of the pane | opens `x-apple.systempreferences:<bundleID>` `[WIN]` `ms-settings:` URI (1.11.4) |
| **Settings page/feature/setting** | `settings.<page>`, `settings.feature.<feature>`, `settings.setting.<anchor>` | title; subtitle "Vorssaint Settings" `[WIN]` "Trayloom Settings"; keywords from `SettingsDirectory.searchItems`; pages already covered by actions (`general, cleaner, uninstaller, appUpdates`) are skipped | opens the Settings window on that destination (scrolls to the setting) |
| **Feature toggle** | `toggle.<feature>` | "Turn on <Feature>" / "Turn off <Feature>"; subtitle = hub group title; keywords = feature name; active dot when on; `needsPermission` if Accessibility required and missing | flips the feature's single enable key, re-syncs the feature, HUD with the feature name/symbol (see 1.11.2) |
| **Power / wifi / volume / brightness / keep awake…** | `action.*` | see 1.11 | |

Every `answer.*` row additionally requires the **row to be named** - e.g. "st" never answers "Storage". Rows are never offered when the user hid them (`commandBarHidden`) or their source is off.

#### 1.5.3 Return semantics summary
* `keepsBarOpen` rows (Emoji browser, Kill Process browser, Uninstall browse, Uninstall-finder, "Use it in the search", a link that takes a query) act in place and leave the field standing.
* `countsUsage=false` rows (answers, files, windows, menus, clipboard, selection helpers, math/units/colour/date answers, openURL, kill rows) never feed usage ranking.
* `confirmationPrompt` rows: Clipboard "Clear unpinned (N)?", Empty Trash "Empty the Trash?", power Restart/Shut down/Log out, Quit "<App>?", Kill "<Name>?".
* Optional numeric rows: none currently use `numericIsOptional` (reserved).

#### 1.5.4 Selection rows (`CommandBarCatalog.selectionEntries`)
Offered when the bar opened with text selected in the front app (Accessibility read, <= **20 000** characters, trimmed; our own app never; empty if the app does not expose a selection). The empty bar then leads with heading `"Selected · <preview>"` (preview = whitespace-flattened selection cut at **44** chars + "…") and these rows (kind label "Selection"):
| id | title | subtitle | notes |
|---|---|---|---|
| selection.copy | Copy it | Selection | copy full trimmed text |
| selection.search | Use it in the search | Selection | `keepsBarOpen`, `countsUsage=false`: puts the selection into the field |
| selection.cleanLink | Clean the copied link | Selection | only if URL Cleaner feature available and the cleaned URL differs; copies it + HUD ("URL cleaned." or "Removed a, b") |
| selection.upper | UPPERCASE | Selection | only if the transform changes the text, `typesOver` true (not a web-text-marker selection) and `exact` length <= 5 000; types the transformed *exact* selection over it |
| selection.lower | lowercase | same | |
| selection.titleCase | Title Case | same | localizedCapitalized |
| selection.shelf | Keep it on the shelf | Selection | only if Shelf feature available **and** its switch on; creates a private pasteboard `com.vorssaint.commandbar.selection` (`[WIN]` use an in-memory DataObject) and calls the Shelf's accept; HUD "Shelf" |
| selection.count | Count it | `Words: %d, characters: %d` | right chip = character count; copies the subtitle |
Words = whitespace-separated runs; characters = Swift grapheme `count`. `[WIN]` use `StringInfo.LengthInTextElements`.

---

### 1.6 Search, ranking and learning (exact algorithm)

#### 1.6.1 Text folding (`CommandBarSearch.normalized`)
1. Remove invisible format characters (Unicode category Cf with scalar >= U+00AD: LRM/RLM, ZWJ/ZWNJ, soft hyphen, ...). 2. Fold: case-, diacritic- and width-insensitive **without locale** (Turkish-safe), then lowercase. 3. Split on any whitespace (incl. U+3000) and rejoin with single spaces. `[WIN]`: `NormalizationForm.FormKC`, drop `UnicodeCategory.Format`, strip `NonSpacingMark` after `FormD`, `ToLowerInvariant`, regex `\s+` -> " ", trim. Never use culture-aware lower.
Pinyin keywords for Han titles (`pinyinKeywords`): syllables joined without spaces + " " + initials (only when the title has Han characters). `[WIN]` optional; needs a data source (ask first: NuGet `Microsoft.International.Converters.PinYinConverter`, or an embedded table).

#### 1.6.2 Score of one row for a query (`CommandBarSearch.score`) - returns null = no match
`haystack = normTitle + (" " + normKeywords)`; `words = haystack.split(' ')`. `tokens = normQuery.split(' ')`. For **every** token compute `bestTokenScore`; if any token is null the row is rejected; the token scores are summed.
Token score: for each word: `word == token` -> **140** (stop); `word.startsWith(token)` -> best = **80**; else if `haystack.contains(token)` -> **44**. If still none and the token is "fuzzy" (not all digits) and length >= 3: a token that is an in-order **subsequence of one word** (word.length >= token.length) -> **24**; else same-length word differing by **one adjacent transposition** -> **16**; else if token length >= 4: **within one edit** of a word (substitution, insertion, deletion or adjacent swap; length difference <= 1) -> **16**. Digit-only tokens are values, never fuzzy. Otherwise null.
Whole-query bonuses added once: `normTitle == normQuery` **+1200**; else `startsWith` **+900**; else `contains` **+700**; else (keywords non-empty and `normKeywords.contains(normQuery)`) **+350**.

#### 1.6.3 Ranking (`rankedIndexes`) and assembly
Candidate = (index, normTitle, normKeywords, `priority`, `boost`). For each match: `tier` = 5 equal title, 4 title prefix, 3 title contains, 2 keywords contain, else 1; `score = base + boost`. Sort by: **priority desc, tier desc, score desc, original position asc** (stable). Then `featureOrdered`: for rows (priority 0) that belong to one feature (`toggle.<f>`=turn 0, `action.<f>`=turn 0 when no dot else 1, `action.<f>.<sub>`=1, `settings.<f>`/`settings.feature.<f>`=2) the rows trade places among slots they already hold so the order is *main command, presets, Settings page*.
Typed-search pipeline (`searchRows`), in this order:
1. **Inside a category**: no answers, no caps; rank only that category's rows (clipboard via `clipboardEntries(limit 40)`); top **40**; one heading "<Category> · N".
2. **Leading `:`** (`emojiQuery`): emoji only (if source enabled); empty after colon -> first 40 of the pool; else rank by emoji name/keywords; top 40. Habit priority via query-habit digests of the text after the colon.
3. **Answer row** (if calculator source on): tries in this order and returns the first that parses: Math -> `math.result` (icon `equal.square`), Units -> `units.result` (`arrow.left.arrow.right`), Colour conversion -> `color.result` (swatch), plain colour -> `color.preview`, Dates -> `date.result` (`calendar`). Leads the list (except `color.preview`, inserted at index 0, or 1 if any ranked title already contains the typed text normalised).
4. **Open URL row** `action.openURL` (title = typed text, subtitle "Open in browser", icon `globe`) when the whole text is a web address (http/https, host present; system link detector semantics). `[WIN]` use a strict check: scheme optional, host with a dot or `localhost`, TLD of 2+ letters, no spaces; `Uri.TryCreate` after prefixing `https://`.
5. **Script answer row** (below, 1.7.6) leads after open-URL.
6. **File rows** from the file-search cache (1.7.7).
7. `effectiveQuery`: the trailing number is cut off only if a numeric command matches the rest.
8. Pool = selection rows + uninstall-selection + catalog + apps + macSettings + windows + (quit rows only when `matchesVerb(query, ["Quit %@", english])`) + (menu rows only when `effectiveQuery.count >= 2`) + clipboard rows (typed text, cap 4) + file rows. Hidden rows and rows of disabled sources are dropped. Script links that match the query are filtered so only the longest-named one stays, and when an answer row exists none do.
9. Candidate fields: `normTitle` = folded `matchTitle ?? title` — except for rows that `takesArgument` (saved searches/scripts) which are scored against the **whole typed query** (`rankingTitle`); `normKeywords` = folded keywords + " " + folded alias; `priority = max(aliasBoost, habitPriority)`; `boost = usageBoost + (isActive ? 20 : 0) + aliasBoost + queryMemoryBoost + rankBias(source) + (pinned ? 60 : 0)` where usage/memory/habit only count for rows with `countsUsage`.
10. Output filtering: `answer.*` rows dropped unless the first query token (>= 3 chars) prefixes their title/English title; **per-kind caps**: `app.`5, `window.`4, `quit.`3, `menu.`5, `emoji.`6, `settings.`4, `macsettings.`4, `clipboard.`4, `snippet.`4, `file.`4, `toggle.`5 (actions uncapped); **total cap 12 rows**. Selection index reset to 0 whenever the trimmed query changed; a background rebuild keeps the selection on the same row id.
Dedup: rows with a repeated id are dropped (first wins) on the typed list.

#### 1.6.4 Usage, habits, memory
* **Usage** (`commandBarUsage`, JSON `{rowId: {count, lastUsed(epoch secs, Double)}}`): recorded when a row runs and `countsUsage`; `count = min(count+1, 999)`; at most **200** ids (oldest `lastUsed` evicted). Boost: `min(count,40) * weight` with weight by age since last use: `< 2 h` -> 12, `< 48 h` -> 8, `< 14 d` -> 5, else 2.
* **Query habits** (`commandBarQueryHabits`, JSON `{digest: {rowId: use}}`, plus `commandBarQueryHabitKey` = base64 of a random 32-byte key created on first use; a missing/damaged key discards the habits): the typed query never leaves memory. On a run the normalised query (first **24** chars) is turned into one digest **per prefix length 1..24**: `HMAC-SHA256(key, utf8(prefix))`, first **12 bytes** hex (24 hex chars). Each digest keeps at most **4** rows (least used/oldest evicted), whole store at most **320** digests (stalest evicted). Boost for a row = max over matching prefix digests of `min(usageBoost(use) * 5 + prefixLength * 6, 720)` and it is the row's **priority** (leads tier/score). Learning uses the query *before* Tab-completion. Emoji learning uses the text after `:`. Only learns while the bar is visible (a row run from its own hotkey teaches nothing). `[WIN]` `HMACSHA256`, `RandomNumberGenerator.GetBytes(32)`.
* **Session query memory** (`CommandBarQueryMemory`, memory only, never stored): per prefix (1..12 chars of the folded query) up to **4** row ids with count (cap 99) and a step counter; at most **60** prefixes; boost = `min(count,3)` (a tie-breaker, max 3). `forget(id)`/`clear()` wipe it.
* "Forget how often I use this" (`resetRanking`): removes the row from usage, memory and habits. "Forget what I use most" in Settings clears usage + habits + memory.
* **Empty-bar suggestions**: `limit = 7 - pins`. Pins first (order made, only rows currently available), then the most-used rows (count desc, lastUsed desc; offerable = catalog + apps + panes + windows, not hidden, source on), then curated ids until the limit: `action.screenshot, action.scrollingScreenshot, action.keepAwake, action.screenOCR, action.clipboardWindow, action.colorPicker, action.darkMode, action.snippetLibrary` (those that exist). Heading order on the empty bar: `Selected · …` (if any), `Pinned`, `Suggestions`, then every other catalog row grouped under a heading = the row's subtitle (answers -> "Answer", links -> "Your shortcut", snippets -> "Insert snippet", folders -> "Folder", others subtitle or "Everything it can do"), max **12** rows per group, subtitle blanked if equal to the heading; finally `Paste from history` (6 most recent clipboard items) if the Clipboard source is on. Apps/windows never appear in the browse list.
* **Category browse** ordering: used rows by count desc then lastUsed desc, unseen rows keep catalog order. Heading "<Category title> · N". Category titles: actions "Vorssaint actions", apps "Apps", menus "Menu command", windows "Open windows", settingsPages "Settings pages", macSettings "System Settings panes", snippets "Snippets", clipboard "Clipboard history", emoji "Emoji", folders "Folders", links "Your shortcuts", killProcess "Kill Process".
* Compact mode (`isCollapsed`): compact && query empty && no category && not peeking -> the panel is the field only (no divider/chips/list/footer, shows compact hints). Category or peek always show the list.
* `Esc` from a typed query only clears it (so a long mistake is recoverable); a second Esc leaves the category; a third un-peeks; a fourth closes.

---

### 1.7 Feature behaviours in detail

#### 1.7.1 Calculator (`CommandBarMath.evaluate`) - strict
Input must be **entirely** mathematical; commands/searches are never answered as sums.
* Pre-checks: trim; one optional **trailing `=`** (`1+1=` ok; any other `=` -> not a calculation); non-empty; **<= 120 characters**; not date/time-looking (`looksLikeDateOrTime`: `-`, `/` or `:` separated groups of 1-4 digits with 2-3 groups: `2026-07-27`, `27/07/2026`, `10:30`; two groups with `-` or `/` are still allowed as subtraction/division; numbers *spaced* around `-` or `/` are a sum (`100 - 20 - 30`)).
* Tokens: numbers; operators `+ - − * × x / ÷ ^ %`; brackets `( ) [ ]` (mismatches -> not a calculation; **missing closing brackets are supplied virtually** and shown in the subtitle as `<typed expr><closers> · Return copies`); constants `pi`, `π`, `e`; functions `sqrt abs sin cos tan asin acos atan ln log log10 exp floor ceil round` (radians; `log`/`log10` = base 10; domain errors, non-finite results and overflow reject the whole expression); percent-of words `of de da do von di del dal` (parser vocabulary, language-independent); the letter `x` between operands = multiply.
* Must show *mathematical intent*: contains an explicit operation, or a function, or an implicit product (number/constant/`)` directly followed by constant/function/`(`). A lone number is never an answer.
* Grammar (precedence low->high): `expr = term (('+'|'-') term)*`; `term = factor (('*'|'/'|'of'|implicit) factor)*`; `factor = ('+'|'-') factor | power`; `power = primary ('^' factor)?` (right associative: `2^3^2 = 2^(3^2)`; sign applies to whole power: `-2^2 = -4`); `primary = number | constant | '(' expr ')' | function '(' expr ')'` optionally followed by `%`. Depth limit 32. Division by zero rejects.
* **Percent semantics**: `50%` is 0.5 and remembers 50. `a + b%` = `a + a*b/100`, `a - b%` = `a - a*b/100` (calculator-style relative percentages). `b% of a` is only valid when the left operand carries a percent (`20% of 150` = 30).
* Numbers: locale decimal separator plus the *other* of `.`/`,` as an alternate; a grouping-only space/apostrophe locale keeps that char as grouping. When both occur the last one is the decimal point. A single alternate separator is *grouping* only if the pattern is 1-3 digits (not starting with 0) followed by groups of exactly 3 (`1,234` => 1234, `0,125` => 0.125, `1,5` => 1.5). Scientific exponent `1e5`, `2E-3` accepted. `[WIN]` use `CultureInfo.CurrentCulture.NumberFormat`.
* Result rounding `significantRounded`: round to `digits = min(15, max(10, floor(log10|v|) + 3))` significant digits (so `0.1+0.2` reads `0.3`; `12345678901+1` keeps its digits).
* Formatting `format`: nonzero with `|v| >= 1e12` or `< 1e-6` -> **scientific**, up to **8 significant digits**, exponent symbol `e` (e.g. `1.2345678e15`); otherwise **decimal with grouping separators**, 0-**8** fraction digits; `-0` -> `0`. Locale-formatted.
* Tab on the answer (`reusableExpression`): the unrounded shortest round-trip number, no grouping, locale decimal separator, negative values wrapped in parentheses so `(-5)^2` keeps meaning.
* Existing Trayloom `Calculator` uses `DataTable.Compute` - replace with a real recursive-descent parser implementing the above (DataTable accepts SQL-ish text, no functions, wrong percent semantics).

#### 1.7.2 Unit conversion (`CommandBarUnits.convert`) - strict
Shape: `<number> <unit> <to-word> <unit>` (also `100km to mi`). Tokens after folding: 3..8; the number may be glued to the unit (`20c`, `5gb`). The to-words (`conversionWords`): `to in into as em para pra en a nach zu à su -> > →`. Try each candidate to-word position from the **right** (so in `5 in to cm` the first `in` is inches, the second is the verb). The right side must be exactly one known unit; the left is `number unit`; both must be of the **same family**. `fl oz`, `fluid ounce(s)` two-word forms are joined first. Matching is exact on folded text (no fuzziness).
Families / lexicon (folded; first entry shown in the result):
| Family | Units (aliases) |
|---|---|
| temperature (absolute, not increments) | c/celsius/°c/centigrade; f/fahrenheit/°f; k/kelvin |
| length | mm; cm; m/meter(s)/metro(s)/metre(s); km/kilometer(s)/quilometro(s); in/inch/inches/polegada(s)/`"`; ft/foot/feet/pe/pes; yd/yard(s)/jarda(s); mi/mile(s)/milha(s) (+ plural/Portuguese variants of mm/cm/km) |
| mass | mg; g/gram(s)/grama(s); kg/kilogram(s)/quilo(s); t/ton(s)/tonne/tonelada(s) (metric); oz/ounce(s)/onca(s); lb/lbs/pound(s)/libra(s) |
| data | b/byte(s); kb, mb, gb, tb (decimal, 1000-based) with names; kib, mib, gib, tib (binary, 1024-based); bit/bits |
| duration | ms; s/sec/second(s)/segundo(s); min/mins/minute(s)/minuto(s); h/hr/hrs/hour(s)/hora(s); d/day(s)/dia(s) (86 400 s); wk/week(s)/semana(s) (604 800 s); yr/year(s)/ano(s) (365 d = 31 536 000 s) |
| volume | ml; l/liter(s)/litre(s)/litro(s); gal/gallon(s)/galao; qt/quart(s); floz/fluidounce(s); cup(s)/xicara(s) (US customary units as Foundation defines them) |
Number parsing = same separator rules as the calculator (tokens never keep spaces/apostrophes inside a number).
Output: unit symbol as the *requested* unit (Foundation "medium" style, `providedUnit`), e.g. `62.14 mi`, `68°F`, `1,000 MB`, `3 hr`, `5 min`, `2 wk`; number locale-formatted with fraction digits `0` if value 0, `4` if |v|<1, `2` if |v|<100, else `1`; non-temperature values with `0 < |v| < 0.001` use **4 significant digits**. **Feet special case**: when the target is feet and the value >= 1 print `N ft M in` (inches rounded with the same digit rule, 12 in carries to a foot, `M=0` omitted). `[WIN]` implement the factors by hand (no `Measurement` type); temperature via offsets; symbols as listed.

#### 1.7.3 Dates (`CommandBarDates`) - answered without network
Max 80 chars, >= 2 tokens. Vocabulary is multilingual (today/hoje/heute/hoy/oggi/…, in/em/daqui/…, ago/atras/ha/hace/…, until/ate/hasta/bis/…, time/hora/uhrzeit/…, units day|week|month|year in 15 languages) - ship at least the English + the other app languages.
1. **Relative date**: an adjacent `<int (|n| <= 10 000)> <unit>` pair plus a direction word (today-word, future-word, or past-word); `-` token or a past word counts backwards. "in 3 weeks", "3 days ago", "today + 10 days". Result = long date (`DateStyle.long`), detail = weekday name.
2. **Days until**: until-word followed by a date written in the user's short date style (locale order; a missing year = next occurrence, including leap-day waiting up to 8 years) -> `N days` (full-style localized, absolute value) with detail = long date.
3. **Time somewhere else**: a time-word + place (stop words `in/a/at/de` and future-words removed) -> short time in that zone, detail `"<City> · <full date>"`. Zones resolved by city name of the IANA identifier (`America/Sao_Paulo` -> "sao paulo"); aliases for Londres, Lisboa, Roma, Moscou/Moscovo/Москва, Nova York/Nueva York, Nova Iorque, Cidade do Mexico, Pequim, Toquio/Tokio, Genebra, Viena, Copenhague, Praga, Atenas, Varsovia, Bruxelas, Zurique, Munique, Colonia, Estocolmo, Hamburgo.
Order tried: relative, until, time. Dates are tried *last*, after math/units/colours. `[WIN]` embed an IANA city list; convert with `TimeZoneInfo.FindSystemTimeZoneById(ianaId)` (.NET 6+ on Windows resolves IANA via ICU); `CultureInfo.CurrentCulture.DateTimeFormat` for long/short patterns.
Rows `answer.date` ("Today", subtitle full date, value short date) and `answer.time` ("Time now", subtitle medium time, value medium time) are separate catalog answer rows.

#### 1.7.4 Colours (`ColorValue`, `CommandBarColors`)
Accepted whole-entry forms (<= 96 bytes, trimmed, case-insensitive): `#RGB`, `#RGBA`, `#RRGGBB`, `#RRGGBBAA`; `rgb(...)`/`rgba(...)` (3-4 args separated by comma/space/`/`; each channel 0-255 number or 0-100 %; alpha 0-1 number or %); `hsl(...)`/`hsla(...)` (hue number with optional `deg`, wraps mod 360; s,l percent 0-100; alpha); `Color(red: 0.2, green: 0.4, blue: 0.6[, opacity: 0.5])` (labels required, 0-1). A bare `RRGGBB` is **not** a colour (it matches numbers/hashes).
* Typed alone -> `color.preview` row (title = the typed text, swatch 22 pt, Return copies the text).
* `<colour> <to-word> <target>` (>= 3 tokens; to-words = the unit list; targets `hex`, `rgb`, `rgba`, `hsl`, `hsla`, `swift`, `swiftui`) -> `color.result` with formatted value; alpha is written for `rgba`/`hsla` always, otherwise only if alpha < 1. Formats: HEX `#RRGGBB` uppercase (+`AA` if alpha), RGB `rgb(r, g, b)` / `rgba(r, g, b, a)` (a up to 3 decimals, `%g`), HSL `hsl(h, s%, l%)` / `hsla(...)` (rounded ints), SwiftUI `Color(red: 0.200, green: 0.400, blue: 0.600[, opacity: 0.500])` with `.` decimals. Channels clamp to 0-1.
* Placement of a lone colour preview: index 0, or 1 when an existing ranked row title already contains the typed text (so `#1234`/`#cafe` still reach their own rows).
* Windows: pure maths; the swatch is a rounded rectangle. SwiftUI output is kept for parity.

#### 1.7.5 Emoji (`CommandBarEmoji`)
* Pool = ~216 "popular" emoji (fixed order, rows 1..12 smileys, hands, hearts, weather/food, travel, tech, objects, symbols, animals, plants, time/chart/trophies) followed by **every single-scalar Unicode emoji sorted by Unicode name** (excluding regional-indicator flags, hair-style components U+1F9B0-3, keycap bases `# * 0-9`, skin-tone modifiers). Text-default scalars get U+FE0F. Names = lowercase Unicode character names ("grinning face", "heavy black heart"; ZERO WIDTH parts dropped). Duplicates folded by canonical (FE0F/FE0E removed) form.
* Aliases (extra keywords) for ~35 popular emoji, e.g. 😂 "haha lol roflmao laugh laughing tears funny", 🤣 "haha lol rofl roflmao laugh laughing funny", 😊 "happy smile blush", 🥰 "love affection hearts", 😘 "kiss love", 😎 "cool sunglasses", 🤔 "think thinking hmm", 🙄 "eyeroll whatever", 😭 "cry crying sad sob", 🥺 "please pleading puppy eyes", 😡 "angry mad rage", 🤬 "swear cursing angry", 🤷 "idk shrug whatever", 💀 "dead death dying skeleton halloween", ☠️ "dead death danger poison pirate", 🙏 "appreciate please thanks thank thx you pray prayer high five", 👍 "yes good approve like okay", 👎 "no bad disapprove dislike", 👌 "okay perfect good", 👏 "clap applause congrats congratulations", 🙌 "hooray celebrate praise", 🫶 "love heart hands", 👀 "look looking eyes see", ❤️ "love heart red", 💔 "heartbreak broken heart sad", 🔥 "fire hot lit trending", ✨ "sparkle sparkles magic clean", 🎉 "party celebrate celebration congrats congratulations", ✅ "check done yes complete success", ❌ "cross no wrong error fail", ⚠️ "warning caution alert", 💡 "idea lightbulb tip", 🚀 "launch ship rocket fast".
* Skin tone (`commandBarEmojiSkinTone`: `""` default yellow, `light`, `mediumLight`, `medium`, `mediumDark`, `dark` = modifiers U+1F3FB..1F3FF) applies to single-scalar `Emoji_Modifier_Base` emoji except 👪 (U+1F46A): canonical char + modifier (VS16 dropped). Actions panel on an emoji row offers the *other* five tones as rows (title = the toned glyph, symbol `hand.raised`) - choosing one types it for this insertion only and still records usage for the base emoji. Settings picker swatch = ✋ in each tone.
* Search: only with a leading `:` (`:fire` finds the same as `fire` inside the Emoji category) or inside the Emoji category (the `emoji.browse` row "Emoji", subtitle "Command Bar", keywords "Emoji", icon `face.smiling`, `keepsBarOpen`). Typing = **type at the caret**, not clipboard.
* `[WIN]` needs embedded data: the Unicode `emoji-test.txt`/`emoji-data.txt` (Emoji, Emoji_Presentation, Emoji_Modifier_Base properties + character names `UnicodeData.txt`) as a compressed resource. Typing with `SendInput` `KEYEVENTF_UNICODE` surrogate pairs. Render with Segoe UI Emoji. Optional: the Windows emoji panel (`Win+.`) is not scriptable.

#### 1.7.6 Typing at the caret and pasting (shared by emoji, snippets, selection case rows, clipboard)
* Refuse (beep) when the front app **is the bar's own process**. Wait for the summoning chord to leave the keyboard: poll every **15 ms**, up to **100** attempts (~1.5 s), until none of Command/Option/Shift/Control is held; then a **60 ms** settle; refuse on Secure Event Input (password field); then post the text (snippet expander routine, no trigger deleted).
* Clipboard paste: hide, put the item on the clipboard (restoring is *not* done - it simply becomes the clipboard), 0.15 s later the same modifier wait, then synthesize ⌘V. Without Accessibility: ask once per session (prompt) then beep.
* `[WIN]` Replace with: remember the foreground window handle *before* showing the bar (`GetForegroundWindow`), after hide `SetForegroundWindow` it back (use the `AttachThreadInput` trick if refused), wait for `GetAsyncKeyState` of Ctrl/Alt/Shift/Win released (same 15 ms/100 polls), 60 ms settle, then `SendInput` (Unicode text for typing; Ctrl+V for paste). Password fields: check the focused UIA element's `IsPassword`; refuse if true. Elevated target windows cannot receive input from a non-elevated bar (UIPI): beep.

#### 1.7.7 Per-row global shortcuts, aliases, pins, hidden
* **Aliases** (`commandBarAliases`, JSON `{stableKey: "text"}`, value trimmed, max **60** chars, several words allowed, each word finds the row). Alias hit: any alias word `== query` -> **exact (2400)**; any word `startsWith(query)` -> **prefix (1100)`; alias boost is added to both priority and boost, and the alias words are appended to the row's keywords. Allowed rows: actions, apps, quitApps, settingsPages, macSettings, snippets, emoji, folders, answers, calculator, links; not menus, windows, clipboard, selection, files, killProcess, uninstallApps. Another row already using one of the words (normalised word set intersection) => refusal text "%@ already answers to that" (%@ = that row's title) shown in orange under the field; the name is *not* saved.
* **Pins** (`commandBarPins`, newline separated stable keys, ordered, max **30** - oldest dropped, no duplicates): toggle via Actions "Pin to the top"/"Unpin" or ⌘P. Allowed on actions, apps, windows, settingsPages, macSettings, snippets, folders, links, answers, calculator. On an empty bar pins lead in the order made; on a typed query a pin is only a **+60 tie-break**. Settings lists pins; pins for hub-owned rows (actions, settings pages, snippets) whose row no longer exists are dropped from the list, an app/folder not on this machine still shows (so it can be removed).
* **Hidden** (`commandBarHidden`, newline separated stable keys): "Never show this" toggles; hidden rows disappear from search, browse, categories and global shortcuts' direct-run.
* **Per-row global shortcut** (`commandBarRowShortcuts`, JSON `{stableKey: "<GlobalShortcut storage string>"}`; **limit 64**): Actions -> "Give it a shortcut"/"Change the shortcut"/"Take the shortcut off" opens the **capture card** (all global hotkeys of the app are suspended while capturing: `ShortcutCapture.begin` + an event tap ahead of the app menu so ⌘Q etc. reach the card; `[WIN]` low-level keyboard hook `WH_KEYBOARD_LL` + swallow, and unregister every app hotkey during capture). Keys: Esc = back, Delete/ForwardDelete = remove, any combo with >= 1 modifier = bind (bare letters beep: `isUsable = !modifiers.isEmpty`). Assignment checks in order: invalid (no modifier) -> `shortcutInvalid`; owned by another row -> "This shortcut is already used by <that row title>."; table full -> "You can assign up to 64 shortcuts. Remove one to add another."; clash with another global role (`GlobalShortcutRole.conflict`) -> "... used by <role title>"; clash with a window-layout shortcut -> "... used by <layout action title>"; clash with macOS (takeover offer in Settings only) -> "... used by macOS" `[WIN]` "Windows" (Win+key combos). Binding a combination already tied to another row **moves** it (the other row loses it) when set from the store layer. Registered with ids starting at **200** (quick tools use lower ids); sorted by key; a refused registration (another app owns it) lands in `refusedRowShortcutKeys` and Settings shows orange "macOS rejected this shortcut. Choose another one." beside the row.
  Pressing a row shortcut (`runRow`): rebuild the catalog fresh; if the row is an app that is not yet in the scan, scan on demand then retry once; a Settings-pane row opens the bar prompting; a script marked `runsDirectly` runs *immediately with no argument and nothing on screen* (ignored if the row is hidden or Links source off); rows that `needsPrompt` or `keepsBarOpen` present the bar and run via the normal path; an **app already frontmost with its window in front and not hidden is hidden** (toggle; `hidesAppInFront(isFrontmost, isHidden, ownsFrontWindow)`) - when the bar is open the key always opens the app; everything else `finish(entry)`.
  `[WIN]` "hide the app" = `ShowWindow(SW_MINIMIZE)` on the app's top-level windows (or `SW_HIDE` via ShowWindowAsync) when its main window is foreground; otherwise activate/launch.
* **App shortcuts center** (sheet opened by the "App shortcuts" button): described in 1.12.2.

#### 1.7.8 "Your shortcuts" (links), placeholders and scripts
Model `CommandBarLink` (JSON array stored as Data in `commandBarLinks`, max **60**, rows with empty name/destination dropped on load; decoding is field-tolerant so older saves load):
```json
{ "id": "<uuid>", "name": "gh", "kind": "link|place|script", "destination": "https://github.com/search?q={query}",
  "runsWithoutArgument": false, "runsDirectly": false }
```
* kinds: `link` ("Site or link": anything with a scheme; no scheme -> `https://` prefixed), `place` ("Folder or file": path, `~` expanded), `script` ("Script": a local executable, one argument).
* **Placeholders** (language-independent tokens, case-sensitive): `{query}` (what you type after the name), `{clipboard}` (what you copied), `{selection}` (what is selected when the bar opened), `{date}` (today in the *short* localized date style). Expansion: for a `link` each value is **percent-encoded** with unreserved set `A-Z a-z 0-9 - . _ ~` (so `+` and `&` cannot change the query); for a `place` values are inserted raw. Clipboard is read only if the destination contains `{clipboard}` (via the shared pasteboard lane). Scripts do not use placeholders.
* `takesQuery` = destination contains `{query}`; `takesArgument` = script || takesQuery. A row that takes an argument is scored against the **whole typed query** (name matched whole word, case/width-insensitive, so `gh vorssaint` finds link `gh`, `ghost` does not) and `keepsBarOpen`.
* `trailingArgument(query, name)`: normalised query must start with `normalizedName + " "`; the argument is the remaining original words (keeps capitals/accents) joined by single spaces; nil otherwise.
* Running a link: if `takesQuery` and the argument is empty -> **prefill the field with `"<name> "`** and stay open; else expand, hide the bar (before the clipboard answers), and open: place that does not exist -> HUD `folder.badge.questionmark` + name; invalid URL -> beep; else `NSWorkspace.open`. `[WIN]` `Process.Start(UseShellExecute=true)`; HUD message = name.
* `revealPath` for a place without placeholders = the expanded path -> "Show in Finder" works.
* **Scripts**: path `~` expanded; run as `exec(path, [argument])`, **5 s default timeout**, output capped at **64 KiB**; stdout trimmed; empty output = nothing yet; non-zero exit with no output shows "Couldn’t run this file". Runs **debounced 0.25 s** after typing stops when the query names the script (`scriptArgument`: trailing text after the name; or the whole bare name when `runsWithoutArgument`, never on a prefix of the name). Results cached by `"<linkId>|<argument>"` for the opening; a run already in flight is not duplicated; stale results from a closed/other presentation are dropped (generation counter). The answer row (`link.<uuid>`, `isAnswer`, big title = output, subtitle "Return copies") leads the list; the script's own row is removed from the list while an answer shows; with several matches the longest name wins. Return before the debounce fired: `runNow` (cancel timer, run immediately); if the field will show an answer row the bar stays open and shows it, otherwise (reached by partial name or inside a category) the result is copied and the bar closes once it lands (unless the user typed on/moved on meanwhile). A script that needs input and got none (`!runsWithoutArgument`) just prefills `"<name> "`. `runsDirectly` (global shortcut path): run with no argument, ignore output, beep on non-zero exit.
* Windows scripts: any executable (`.exe`, `.cmd`, `.bat`, `.ps1` via `powershell.exe -NoProfile -File`, `.py` via file association?) - use `ProcessStartInfo` with `UseShellExecute=false` for `.exe/.cmd/.bat`, and `powershell.exe -NoProfile -ExecutionPolicy Bypass -File <path> <arg>` for `.ps1`; hide the console window (`CreateNoWindow`), read stdout as UTF-8, kill on 5 s timeout; the argument passed as one `ArgumentList` item (no shell quoting). `chooseDestination` picker filter: all files (a script is a file, never a folder; a place may be file or folder; name prefilled from the file name if empty).

#### 1.7.9 File search (`CommandBarFileSearch*`)
* Source `files`; **off by default** because `commandBarFileScopes` is empty (an empty list searches nothing; it must never fall back to the home folder). Scopes stored one path per line with `~` for home so an exported list stays portable; ignores one name/pattern per line.
* Query -> Spotlight expression: every whitespace-separated word must appear in the file name in any order (`kMDItemFSName == "*word*"cd` joined by `&&`; `\ " * ?` escaped so a typed `*` means the character). Minimum **2 characters** of trimmed query. Debounce **120 ms**; only the newest query matters (an old search is cancelled and a late completion stays invisible); results cached per exact query for the opening; at most **1000** candidates fetched, **200** rows kept, sorted by file name (case-insensitive, locale) then path. Rows only appear in the typed search pool (not on the empty bar, not in categories). `awaitsAnswers` (companion "thinking") is true while a file search or script is pending.
* Scope resolution: tilde expanded; a scope equal to the home folder expands to its **visible children excluding `Library`** (never the home itself - `~/Library` is noise); only existing directories that are not packages; duplicates dropped.
* Always excluded (cannot be switched off): any path with a component starting with `.` (hidden) and anything **inside a package** (`.app`, document bundles - by filesystem `isPackage`, not by extension list). Shipped ignores (not editable, merged with the user's): `node_modules, .git, DerivedData, Pods, .build, vendor`. User ignores: case-insensitive **whole component** match (`build` does not hide `rebuild-notes.md`), or an extension form `*.log` / `.log` (matches by suffix of the last component).
* Rank: `rankBias(files) = -40` (a file must be a clearly better match than a command to lead), no usage learning (`countsUsage=false`), cap 4 rows in the global list.
* Windows mapping: Windows Search index. Preferred (no NuGet): `Windows.Storage.Search` - `StorageFolder.CreateFileQueryWithOptions(new QueryOptions(CommonFileQuery.DefaultQuery, null){ FolderDepth = Deep, ApplicationSearchFilter = "System.FileName:~<\"word\" ..." , IndexerOption = IndexerOption.OnlyUseIndexer })`; or `System.Search` OLE DB (`Provider=Search.CollatorDSO;Extended Properties='Application=Windows'` with `SELECT TOP 1000 System.ItemPathDisplay FROM SystemIndex WHERE SCOPE='file:C:/Users/x/Documents' AND System.FileName LIKE '%word%'`; needs `System.Data.OleDb` - **ask first**). Hidden/System attribute filter (`FileAttributes.Hidden|System`) replaces the dot rule. "Package" has no Windows twin - skip. Row subtitle: path abbreviated with `%USERPROFILE%` -> `~`. Reveal = `explorer.exe /select,"<path>"`. Settings copy `[WIN]` "...it uses Windows Search...". Note the Windows index only covers indexed locations (default Start menu/Documents/Pictures/Music/Desktop) - say so in the caption or fall back to a bounded `Directory.EnumerateFiles` (depth-limited, cancellable) for folders outside the index.

#### 1.7.10 Position (drag + recenter)
* Default spot: horizontally centred on the **screen the pointer is on** (`pointerVisibleFrame`), vertically: top edge at `visibleFrame.minY + visibleFrame.height * 0.72` i.e. 28 % down from the top; origin clamped with a **16 pt** margin to the visible frame. Panel is anchored by its **top edge** so the list grows/shrinks below a field that never moves; re-fit on every layout change, keep horizontal centre, stop at 16 pt above the screen bottom.
* The mark is a drag handle (window dragging, not a gesture). While dragging the open-hand -> closed-hand cursor. Saving is deferred **0.15 s** after the last move (`didMove`) so only a finished drag writes: offset = `(panel.midX - screen.midX, panel.maxY - (screen.minY + 0.72*screen.height))`, clamped, stored as the integer string `"dx,dy"` in `commandBarPositionOffset` (empty string / key removed when zero). Stored as an **offset** so it means the same on every display and an unplugged monitor cannot strand the bar. Non-finite or malformed strings read as zero.
* Double-click the mark, or Settings "Recenter the bar" (enabled only while an offset exists, `hasCustomPosition`): remove the key and, if the window bar is visible, slide it back with an animated frame change.
* `[WIN]` macOS "y up" -> Windows "y down": use `WorkArea` of the monitor under the cursor (`MonitorFromPoint`/`Screens.UnderCursor()`); `top = work.Top + work.Height * 0.28`. Drag handle: `DragMove()` from the mark's `MouseLeftButtonDown`; double-click resets. Use `LocationChanged` + 150 ms debounce to persist (device-independent pixels).

#### 1.7.11 ASCII layout switch (`commandBarASCIILayoutEnabled`, default false)
On each open (read fresh), if enabled: pick the first enabled keyboard layout that is ASCII-capable and a plain layout (not an IME), unless the current one already is; select it and remember the previous source id (only record on acceptance); restore on close on the **next main-loop turn** (selecting inside a key event is ignored), keeping the record until restoration succeeds; the termination path restores synchronously. Also tell the island so its layout notice stays quiet.
`[WIN]` use `GetKeyboardLayout(threadId of the foreground window)` to remember the HKL; choose the first of `GetKeyboardLayoutList` whose primary LANGID is Latin-script/`0x0409` (en-US) preferred; switch the bar window with `ActivateKeyboardLayout(hkl, KLF_SETFORPROCESS)` (or post `WM_INPUTLANGCHANGEREQUEST`); restore on hide by `PostMessage(foregroundHwnd, WM_INPUTLANGCHANGEREQUEST, 0, previousHkl)`. IME layouts (Chinese/Japanese/Korean) are "not ASCII layout": TSF `ITfInputProcessorProfileMgr` can tell. Caption `[WIN]` unchanged.

#### 1.7.12 Compact mode (`commandBarCompactMode`, default false)
Read fresh each open. With the bar empty and no category the panel is **just the field** (no divider, chips, list or footer; inline hints "↓ Suggestions Esc"); the walk of the whole catalog (`categoryChips`, suggestions) is skipped entirely. `↓` (or the up/down of ⌃N) peeks the full list for the rest of that opening; typing shows results as usual; a category chip/arrow also shows the list. Setting caption: "Bar opens without suggestions. Results appear as you type."

---

### 1.8 Droplet animation (`CommandBarDropletMotion` + `CommandBarDroplet`)
Dynamic-Island-only presentation. A separate click-through, shadow-less panel (at the island's level, ordered *below* the island so the drop grows out from behind its edge) spans from `island.minY + rootDepth` down past the bar with **28 pt** margins left/right/bottom. Motion is pre-sampled at **120 fps** (`count = ceil(duration*120)`, frames linear keyframes in Core Animation; `fillMode backwards`); layers: `neck` (black shape path), `bead` (black rounded rect = becomes the bar's field), and the mascot (companion) riding inside.
Constants: `landingGap 16`, `beadSide 26`, `mascotSize 22` (riding scale **0.7**), `rootDepth 6` (neck roots 6 pt inside the island), `fieldRadius 22`, `hurriedReveal 0.12 s`, `rewindLength 0.2 s`. Apple spring (`NotchMotion.Spring(duration, bounce)`): `ω = 2π/duration`, `ζ = 1 - bounce`; progress(t) = `1 - e^{-ζωt}(cos(ω_d t) + ζω/ω_d · sin(ω_d t))`, `ω_d = ω·√(1-ζ²)` (critically damped when `ζ >= 1`: `1 - e^{-ωt}(1+ωt)`); `limited(travel, limit)` reduces bounce so overshoot*travel <= limit pt.
**Drop (open)** from island lower edge `edge` at `centerX` into `field` (the bar's top 50 pt strip, `fieldHeight`), `icon` = mascot slot (field.minX + 16 + 11, field.midY):
1. **Stretch 0–0.12 s**: liquid bulges and stretches: bead size `(26·(0.8+0.12s), 26·(0.7+0.42s))`, centre y `edge - 6.5 + 18.2·s`, neck root `22-6s`, tip `width·(0.5-0.08s)`, `s = smoothstep(t/0.12)`.
2. **Pinch 0.12–0.20 s**: size `(26(0.92+0.08p), 26(1.12-0.06p))`, centre y `edge+11.7+13p²`, neck thins (`root 16-6p`, `tip width·0.42(1-p)`, end `centre.y - 0.3·h·p`).
3. **Fall** `pinchEnd(0.20) → fallEnd`, `fall = clamp(0.06, 0.14, (land.y - pinched)/380)`; accelerating `y = pinched + (land.y - pinched)·share²`, size 0.96×1.06 of 26; neck springs back over **0.12 s** (`neckEnd` eased to `edge-1`, `root 10(1-back)`, rounded tip <= 3).
4. **Land/squash** `fallEnd → fallEnd+0.04`: size eases to `(side·1.3, side·0.72)`. Mascot face settles from "surprised" at `landing = fallEnd`.
5. **Open** (spring): width spring `(duration 0.3, bounce 0.18, limited(travel = field.w - squashed.w, limit 6))`, height spring `(0.24, 0.2, limited(.., 3))`; corner radius lerps from `min(w,h)/2` to `min(22, ...)`; centre slides from `centerX` to `field.midX` by the spread; mascot flies from the bead centre to `icon` and scales 0.7→1. Total = `openStart + max(settle(width), settle(height))` (settle = last time the spring is beyond 0.5 pt of target).
6. `reveal` = time the shape has all but arrived (within 2 pt width / 1 pt height); the **real bar** (panel) becomes visible then and the drop fades over it in **0.14 s** (`fadeOut`). Last frame is forced exactly to `field`, radius `min(22, h/2)`, mascot at `icon`, scale 1.
**Hurry** (a key typed while falling, anything but Esc): increment generation, show the bar **at once** fading in over **0.12 s**, play the *remainder* of the fall within 0.12 s and fade it; hide the drop's mascot in 0.05 s (the bar has its own face).
**Rewind** (closed before the bar showed): the drop retraces its frames in reverse within `min(0.2 s, original span)`, ends inside the island; companion goes home.
**Retract** (normal close, panel already gone): fold (0.1 s, only if the bar is taller than the field) the bar to its field, then a no-bounce spring (0.26 s) shrinks field→bead `side`; at `fold + 0.55·shrink` the drop rises (0.16 s, accelerating `share²`, stretching `×0.85 wide ×1.2 tall`) while the island bulges toward it (`near = smoothstep(1 - gap/(side·1.6))`, reach `0.3·side·near`, bridge when within ~2 pt) and absorbs it (0.06 s: shrink 40 %, slide up 0.8·side, neck 14→0). Mascot returns home with a hop (`farewell` mood: `.happy` if something ran, `.wink` on cancel). The neck outline is one cubic path with identical topology every frame (root flare, tip, rounded end).
**Reduce Motion** (system accessibility): skip the drop entirely (`revealed(0)` immediately).
Companion/mascot behaviour in the bar (`CommandBarMascot`): eyes follow typed text (`readingGaze`), `thinking` while a file search or script is pending, `confused` with a typed query and no rows, `idle` otherwise; celebrates when results arrive after thinking/confused.
`[WIN]` Pure data -> WPF: a `Canvas` overlay window (`WS_EX_TRANSPARENT|WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW`, topmost) with a `Path` (neck) and a `Border`/`Path` (bead); drive with `CompositionTarget.Rendering` sampling the precomputed `frames` by `elapsed/duration` (or a `DoubleAnimationUsingKeyFrames` per property at 120 keys). Needs the Island feature's anchor rect and mascot control. Everything else in the bar works without it; mark as phase 2.

---

### 1.9 Windows mapping - platform services used by the bar (summary)
| macOS facility | Windows implementation |
|---|---|
| Non-activating key panel (`.nonactivatingPanel`, `canBecomeKey`) | WPF window with `ShowActivated=true` is simplest (the bar activates; record/restore foreground window as in 1.7.6). True non-activating: `WS_EX_NOACTIVATE` + route text via a hidden activating `TextBox` hosted in a layered window and `SetWindowSubclass` - not recommended (IME). Keep `WindowStyle=None`, `AllowsTransparency=true`, `Topmost`, `ShowInTaskbar=false`, `WS_EX_TOOLWINDOW` so Alt+Tab does not list it |
| Blur backdrop + plate | `DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE = 3 /*acrylic*/)` on a non-transparent HWND with `SetWindowRgn(CreateRoundRectRgn(0,0,w,h,44,44))` (22 pt radius -> 44 px diameter), or `SetWindowCompositionAttribute(ACCENT_ENABLE_ACRYLICBLURBEHIND)`; overlay plate = black 55 % (dark) / white 50 % (light) and 1 px border `#FFFFFF1F` (dark) / `#00000014`. Fall back to a solid `ThemeKeys.WindowBackground` when transparency effects are off (`SystemParameters` / registry `EnableTransparency`) |
| Global hotkey | `RegisterHotKey` (existing `BindHotkey` helper); row hotkeys ids >= 200 equivalents via the same hotkey service; capturing = low-level hook |
| Pointer screen | `MonitorFromPoint(GetCursorPos)` |
| Accessibility API (menus, selection) | UI Automation (`System.Windows.Automation`/`UIAutomationClient` COM) |
| Workspace app list / running apps | Start Menu `.lnk` scan (existing `AppIndex`) + UWP apps via `Windows.Management.Deployment.PackageManager` / `shell:AppsFolder` (`IShellItem`/`AppUserModelID`) - the existing port misses Store/UWP apps and system tools; use `Get-StartApps`-equivalent: enumerate `shell:AppsFolder` (`SHGetKnownFolderItem(FOLDERID_AppsFolder)`), launch `shell:AppsFolder\<AUMID>` |
| Running apps (quit rows) | `Process.GetProcesses()` with `MainWindowHandle != 0`, visible, not cloaked; graceful `CloseMainWindow()`; force `Kill()` |
| Icons | `SHGetFileInfo`/`IShellItemImageFactory` 64x64 cache (cost ceiling ~8 MB, 300 items) |
| Settings storage | `Ctx.Settings` JSON (key names below keep the macOS names) |
| HUD message | existing `Ctx.Notifier` / a small toast window (icon + message, 10 pt radius, 14x9 padding, 12 pt semibold, max 360 px wide, auto-dismiss) |
| Clipboard I/O | WPF `Clipboard` on STA with retry (`OLE CLIPBRD_E_CANT_OPEN` loop) |

---

#### 1.9.1 Windows mapping detail: open windows, menu commands, selection, apps
**Open windows** (`CommandBarCatalog.windowEntries`; macOS reads them through Accessibility with ids `window.<id>`, titles from AX when the window server withholds them; refreshed at most every 4 s; the row set is wiped when the permission/feature goes away):
* Windows: reuse `Trayloom.Features.Windows/WindowEnumerator.cs` (Switcher). Filter: `IsWindowVisible`, not `WS_EX_TOOLWINDOW` (unless `WS_EX_APPWINDOW`), no owner (`GetWindow(GW_OWNER)==0`), not cloaked (`DwmGetWindowAttribute(DWMWA_CLOAKED)`, hides suspended UWP/other-desktop windows), non-empty trimmed title different (case-insensitive) from the process/app name, not the bar/Trayloom process, not `Progman`/`WorkerW`/`Shell_TrayWnd`. Id `window.<hwnd as hex>`; subtitle = app name (file description of the exe or UWP display name; "Window" if empty); icon = the app icon (`WM_GETICON`/`GCL_HICON` -> exe icon). Never learned (`countsUsage=false`).
* Activate: after 0.1 s, if minimized `ShowWindow(SW_RESTORE)`, then `SetForegroundWindow` using the `AttachThreadInput(GetCurrentThreadId, GetWindowThreadProcessId(fg))` or `ALT` key tap trick so Windows grants foreground; capture the handoff source (foreground window at the time the row ran) before the beat like the macOS code does.
* Source/category: "Open windows" (`windows`); chip shown if the Switcher or Window layout feature is installed.

**Menu commands of the front app** (`CommandBarMenus`; macOS: AX menu bar walk of the app that was in front when the bar opened):
* Limits to copy: top-level menus named **Apple, Window, Help** skipped (`[WIN]` skip "Window", "Help", "?"), depth <= **4**, <= **400** items, per-call timeout **0.35 s** (a hung app must not hold the walk), disabled items skipped, an item that only opens a submenu is not a command (its children are listed with the extended path), result cached per app pid for **8 s** and dropped the instant another app is in front. Runs off the UI thread; never read while the bar is closed; never for the bar's own app; only for regular (windowed) apps.
* Shortcut text = the item's key equivalent rendered in macOS order ⌃⌥⇧⌘ + uppercase character; `[WIN]` text after the `\t` in the menu string ("Ctrl+Shift+S") or UIA `AcceleratorKey`.
* Windows implementation, two providers merged into the same row model (title, path[], shortcut, invoke):
  1. **Win32 menu bar**: `GetMenu(hwnd)` of the captured foreground top-level window (`GetAncestor(GA_ROOT)`); recurse with `GetMenuItemCount`/`GetMenuItemInfo(MIIM_STRING|MIIM_ID|MIIM_SUBMENU|MIIM_STATE)`; strip `&`; split off `\t` for the shortcut; skip separators and `MFS_DISABLED|MFS_GRAYED`; invoke with `PostMessage(hwnd, WM_COMMAND, MAKEWPARAM(id,0), 0)`. Works for Notepad, classic Win32/MFC/WinForms apps.
  2. **UI Automation menu bars** (WPF, Qt, Java, Office classic): `AutomationElement.FromHandle(hwnd).FindFirst(Descendants, ControlType.MenuBar)` -> `MenuItem` children (read **without expanding**; do not call `ExpandCollapsePattern.Expand`, that would open menus on screen); invoke with `InvokePattern`. Use `CacheRequest` (Name, ControlType, IsEnabled, AcceleratorKey) and a 350 ms budget per element (`TreeScope.Children` walks).
  Modern apps without a menu bar (UWP, Electron without a native menu, ribbon-only) return nothing - accepted. Breadcrumb "App › Menu › Submenu" with the same duplicate-collapsing rule; id `menu.<index>.<title>`; keywords "Menu command <app> <path>"; rank bias -80; only when the effective query has >= 2 characters; cap 5.
* Row Return = press; failure = beep.

**Selection** (`CommandBarSelectionReader`): read through UIA while the bar is *opening*, before it takes focus: `AutomationElement.FocusedElement` -> `TextPattern.GetSelection()` -> `GetText(20001)` (take up to 20 001 chars; if > 20 000 treat as none). `typesOver=false` when the element is read-only (UIA `ValuePattern.IsReadOnly`, or a browser `Document`) because typing letters would not replace it; then the three case rows are not offered. Fallback (opt-in, off by default in the first build): `Ctrl+C` simulation with clipboard backup/restore - if used, mark the temporary data with `ExcludeClipboardContentFromMonitorProcessing` and `CanIncludeInClipboardHistory = 0` so the Clipboard history feature does not record it. Nothing is read while the bar is closed; the text never reaches disk.

**Apps** (`appEntries`, `InstalledApps`): macOS scans `/Applications`, `/System/Applications`, `~/Applications`, bundled apps and Spotlight apps in the home folder (every open; unchanged scans are discarded). Windows: Start-Menu `.lnk` scan (existing `AppIndex`) **plus** `shell:AppsFolder` (UWP/Store apps, system tools) via `SHGetKnownFolderItem(FOLDERID_AppsFolder)` and `IShellItem2.GetString(PKEY_AppUserModel_ID)`; skip names containing "uninstall" (existing rule), keep one row per `stableKey`. Keywords = alternate names (shortcut name vs exe `FileDescription` vs product name vs folder name) + (pinyin if Han). Running = a process whose main module path equals the target (or same AUMID) with a visible window -> green dot, +20 rank boost. Launch = `Process.Start(UseShellExecute)` for `.lnk`; `explorer.exe shell:AppsFolder\<AUMID>` for UWP. Rescan on every open in the background (existing code rescans only every 10 min).

---

### 1.10 Cross-feature flows hosted in the bar (owned by other specs, listed so the bar exposes the hooks)
* **Uninstall inline review** (`mode uninstallReview` / `uninstallHomebrewConfirm`): rows `uninstall.<id>` (offered only inside the "Uninstall Application" category = row `uninstall.browse`, title "Uninstall Application", subtitle "Uninstaller", icon `trash`, `countsUsage=false`, `keepsBarOpen`) and `selection.uninstall` / `uninstall.finder` ("Uninstall app selected in Finder", icon `app.badge`) exist only when the Uninstaller feature is available **and** its setting `uninstallerCommandBarEnabled` is on. The inline checklist = the Uninstaller's own scan/results/removing/done views (categories App/Support/Caches/Preferences/Containers/Logs/State/Other, per-item checkboxes, sizes, Cancel/Remove). Return in the field = primary action (a held-down Return repeat is ignored); other keys belong to the native controls; typing leaves the review. Windows: Uninstaller feature exposes `select(path)`, `Phase`, `Items`; Homebrew confirm = winget/Store variants (see Uninstaller spec).
* **Kill Process rows**: `kill.browse` (title "Kill Process", subtitle "Browse & Kill", icon `xmark.octagon`, `keepsBarOpen`) offered only when Kill Process is available **and** `killProcessCommandBarEnabled`; rows `kill.<pid>` title process name, subtitle "PID %d", keywords = path, confirm "Kill %@?"; Actions panel for such a row: "Force Kill" (confirm alert "Force Kill %@?" + path), "Kill All “%@”" (alert "Kill all “%@” processes?"), "Kill Process Tree" (alert "Kill %@ and all its child processes?" + path), "Restart" (when restartable). Alerts are critical-style modals with [action] / "Cancel". Rows appear only inside the category (never in the flat search).
* **App actions in the Actions panel** (rows built by `actions(for:)`, in this order; each only when it applies): for a *running app row*: "Quit <App>" (`xmark.circle`), "Restart <App>" (`arrow.clockwise`; app activated, 0.12 s later terminate; on the termination notification relaunch after 0.12 s; give up after 60 s), "Force Quit <App>…" (`exclamationmark.octagon`, destructive; critical alert "Force quit <App>? Unsaved changes will be lost." with buttons "Force Quit <App>" (ellipsis stripped) / "Cancel"); then "Uninstall <App>…" (`trash`) when Uninstaller available and the app is acceptable; "Show in Finder" (`folder`) when `revealPath` exists (hide bar, `activateFileViewerSelecting`; HUD `folder.badge.questionmark` + title if the path vanished) `[WIN]` "Show in File Explorer"; kill-process rows (above); emoji skin-tone rows (1.7.5); "Pin to the top"/"Unpin" (`pin`/`pin.slash`, closes panel); "Give it your own name"/"Change the name it answers to" (`character.cursor.ibeam`); "Give it a shortcut"/"Change the shortcut" (`keyboard`) and, if bound, "Take the shortcut off" (`keyboard.badge.ellipsis`); "Never show this" (`eye.slash`); "Forget how often I use this" (`arrow.counterclockwise`, only `countsUsage` rows). A row with no actions (or an answer / clipboard / calculator row that is not in the index) gets **no ⌘K** (footer hint hidden).
  Windows icon mapping: xmark.circle `E711`; arrow.clockwise `E72C`; exclamationmark.octagon `E7BA`(warning)/`EA39`; trash `E74D`; folder `E8B7`; pin `E718`; pin.slash `E77A`; character.cursor.ibeam `E8AC`; keyboard `E765`; keyboard.badge.ellipsis `E765`+`E10C`; eye.slash `ED1A`; arrow.counterclockwise `E7A7`.


---

### 1.11 Full action catalog (`CommandBarCatalog`) with Windows equivalents

Build order = ranking tie order (earlier wins a tie): **action rows (in the order below) -> feature toggles -> machine answers (battery, memory, storage) -> Settings pages -> snippets -> your shortcuts**; then (separately held lists) selection rows, apps, panes, windows, quit rows, menu rows, clipboard rows, file rows, emoji rows. The catalog is **rebuilt on every opening** and again 30 ms after any live-answer lands (battery/memory/storage/Wi-Fi), so availability, live state ("Enable"/"Disable" wording), shortcuts and confirmations are always current. A row appears only if its feature is installed (`AppFeature.x.isAvailable`) - `[WIN]` = running/installed in `FeatureRegistry`.

"Subtitle" below is `area(feature, under: title)`: the owning feature's hub title, except when it equals the row title, then the feature's hub **group** title (Windows and Dock / Mouse and keyboard / Clipboard and files / Sound / Energy and display / Tools / Dynamic Island / System monitor; `[WIN]` first group = "Windows and taskbar"). "Shortcut chip" = the feature's global shortcut shown only while it actually fires (its enables are on).

#### 1.11.1 Action rows
Legend: **Run** = macOS action; **Win** = Windows implementation. All GUI-capturing actions run after a 0.15 s beat so the bar is gone first.

| id | Title (enUS) | Subtitle | Keywords | Icon (SF -> Segoe) | Shortcut chip / state / trouble | Run (macOS) | Win |
|---|---|---|---|---|---|---|---|
| action.screenshot | Screenshot | Tools | - | camera.viewfinder -> E722 | screenshot role; needsPermission if no Screen Recording | `ScreenshotService.capture()` | existing Screenshot feature action `capture`; no permission |
| action.scrollingScreenshot | Scrolling screenshot | Tools | - | rectangle.stack -> E8A9 (ScrollMode `E7C9`) | needs ScreenRecording+Accessibility | `captureScrolling()` | Screenshot feature scrolling mode (UIA ScrollPattern / SendInput wheel + stitch) |
| action.screenRecorder | Screen recording / **Stop recording** while recording | Tools | - | record.circle / stop.circle -> E7C8 / E71A | recorder role | `ScreenRecorderService.toggle()` | Recorder feature toggle (Windows.Graphics.Capture) |
| action.recentCaptures | Recent captures | Tools | "Screenshot Recording Recent captures" | clock.arrow.circlepath -> E81C (History) | - | open history window after 0.1 s | Recent captures window |
| action.screenOCR | Copy text from screen | Tools | - | text.viewfinder -> E8EC (or E7C6) | screenOCR role | `ScreenTextService.capture()` | Screen OCR feature (Windows.Media.Ocr) |
| action.colorPicker | Color picker | Tools | - | eyedropper -> EF3C (Eyedropper) | colorPicker role | `ColorSamplerService.pick()` | Color picker feature |
| action.clipboardWindow | Clipboard | Clipboard and files | - | doc.on.clipboard -> F0E3 | clipboard role (only if history enabled); trouble needsSetup("Clipboard", Clipboard page) when capture off and no saved items | open history window after 0.1 s | Clipboard popup (existing) |
| action.clipboardClearRecent | Clear unpinned | Clipboard | "Clipboard Clipboard Clear unpinned Recent Recent Clear recent Clear recent" (+ UI language words) | trash -> E74D | needsSetup as above; confirm "Clear unpinned (N)?" (N = unpinned count at listing time) | clear unpinned items captured at listing time | Clipboard store `ClearRecent(ids)` |
| action.snippetLibrary | Quick snippet menu | Text snippets | - | text.append -> E8C4 | snippetLibrary role | open the snippet menu | Snippets feature popup |
| action.scratchpad | Scratchpad | Tools | - | note.text -> E70B (QuickNote) | scratchpad role | show scratchpad | Scratchpad feature |
| action.cameraPreview | Camera preview | Tools | - | web.camera -> E722 | cameraPreview role; needsPermission if camera not granted | show preview | Camera preview (MediaCapture); Windows camera privacy toggle (`ms-settings:privacy-webcam`) |
| action.shelf | Shelf | Clipboard and files | - | tray.full -> E7B8 | shelf role if enabled; needsSetup("Shelf", Shelf page) if Shelf switch off | `ShelfService.summon()` | Shelf window toggle |
| action.pastePlain | Paste as plain text | Clipboard and files | - | doc.plaintext -> E8E4 | pastePlain role; needsPermission without Accessibility | perform paste-plain | PastePlain feature |
| action.cleaningMode | Cleaning Mode | Tools | - | keyboard -> E765 | needsPermission w/o AX | activate cleaning mode (0.1 s beat) | Cleaning mode (blocks input via `BlockInput`/hooks) |
| action.keepAwake | **Enable keep awake** / **Disable keep awake** | Keep awake | "Keep awake" | bolt / bolt.fill -> E945 | keepAwake role; active dot when on | toggle (indefinite) | `SetThreadExecutionState(ES_CONTINUOUS|ES_SYSTEM_REQUIRED[|ES_DISPLAY_REQUIRED])` on a dedicated thread |
| action.keepAwake.15 / .30 / .60 / .120 / .240 / .480 | Keep awake for 15 minutes / 30 minutes / 1 hour / 2 hours / 4 hours / 8 hours | Keep awake | "Keep awake" | bolt.badge.clock -> E916 (Stopwatch) | - | `activate(minutes:)` (only these presets; any other number would mean indefinite, so the plain row takes **no** number) | same feature with a timer |
| action.micMute | **Mute microphone** / **Unmute microphone** | Mute microphone | - | mic / mic.slash.fill -> E720 / F781 | micMute role; active when muted | toggle | CoreAudio capture endpoints `IAudioEndpointVolume.Mute` on all active capture devices (NAudio) |
| action.brightness | Display brightness | "0 to 100" when enabled, else "Displays" | "Displays screen" | sun.max -> E706 | trouble needsSetup("Displays", Energy page) when `brightnessControlEnabled` off; **numeric 0…100** only when enabled | `applyBrightness(percent)`: **display under the pointer** only, OSD shown; if display not in list: refresh and retry once after 0.7 s; else beep | internal panel: WMI `WmiMonitorBrightnessMethods.WmiSetBrightness(timeout, level)` in `root\wmi`; external: DDC/CI `SetMonitorBrightness` (dxva2, `GetPhysicalMonitorsFromHMONITOR`); pick the monitor under the cursor (`MonitorFromPoint`) |
| action.volume | Volume | "0 to 100" | - | speaker.wave.2 -> E767 | numeric 0…100 | `setSystemOutputVolume(v/100)`; beep if fails; if the island routes volume, show it there, else HUD `speaker.wave.2` "Volume 40%" | `MMDeviceEnumerator.GetDefaultAudioEndpoint(Render, Multimedia).AudioEndpointVolume.MasterVolumeLevelScalar = v/100` (NAudio) |
| action.soundMute | **Mute the sound** / **Turn the sound back on** | Volume | - | speaker.wave.2 / speaker.slash.fill -> E767 / E74F | row only if mute state readable; active when muted | set mute | `AudioEndpointVolume.Mute` |
| action.soundOutput.<deviceUID> (one per output device that can be default) | device name | "Current output" (default device) / "Switch sound output" | "Switch sound output" | headphones / hifispeaker -> E7F6 / E767 | active dot on current | set default output | undocumented `IPolicyConfig::SetDefaultEndpoint` COM (ask first; or open `ms-settings:sound`) |
| action.darkMode | **Switch to light mode** / **Switch to dark mode** | Quick toggles | - | sun.max.fill / moon.fill -> E706 / E708 | - | toggle system appearance | `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize` `AppsUseLightTheme` + `SystemUsesLightTheme` (0/1) then broadcast `WM_SETTINGCHANGE` with lParam `"ImmersiveColorSet"` |
| action.lockScreen | Lock the screen | Quick toggles | - | lock.fill -> E72E | - | after beat | `LockWorkStation()` |
| action.displayOff | Turn off the display | Quick toggles | - | display -> E7F4 | - | after beat | `SendMessage(HWND_BROADCAST, WM_SYSCOMMAND, SC_MONITORPOWER, 2)` (`PostMessage` to the bar's own HWND) |
| action.screenSaver | Start the screen saver | Quick toggles | - | sparkles.tv -> E7F8 | - | after beat | `SendMessage(GetForegroundWindow(), WM_SYSCOMMAND, SC_SCREENSAVE, 0)` or run the configured `.scr /s` |
| action.ejectDisks | Eject all disks | Quick toggles | - | eject.fill -> F847 (verify; fallback E7E7?) | - | eject every ejectable volume | enumerate `DriveInfo` removable/CD: `DeviceIoControl(IOCTL_STORAGE_EJECT_MEDIA)` after lock/dismount, or Shell `Shell.Application.NameSpace(17).ParseName(drive).InvokeVerb("Eject")` |
| action.hiddenFiles | **Hide hidden files** / **Show hidden files** | Quick toggles | - | eye.slash / eye -> ED1A / E7B3 | - | toggle | `HKCU\...\Explorer\Advanced` `Hidden` = 1 (show) / 2 (hide), then refresh Explorer windows (`SHChangeNotify(SHCNE_ASSOCCHANGED)` + `WM_COMMAND 0xA220`/F5 to `Shell_TrayWnd`/`CabinetWClass`) |
| action.desktopIcons | **Hide desktop icons** / **Show desktop icons** | Quick toggles | - | desktopcomputer -> E7F4 | - | toggle | `HKCU\...\Explorer\Advanced` `HideIcons` + refresh, or toggle via `SHELLDLL_DefView` `WM_COMMAND 0x7402` |
| action.emptyTrash | Empty the Trash | Quick toggles | - | trash -> E74D | confirm "Empty the Trash?"; needsPermission when Finder automation was denied `[WIN]` never | `emptyTrashConfirmed()` | **Recycle Bin**: title `[WIN]` "Empty the Recycle Bin", confirm "Empty the Recycle Bin?"; `SHEmptyRecycleBin(NULL, NULL, SHERB_NOCONFIRMATION|SHERB_NOPROGRESSUI|SHERB_NOSOUND)` |
| action.quitAllApps | Quit all apps | Quit apps | - | xmark.app -> E711 | quitAll role | opens the "Quit apps" window (**that window is the confirmation**) | Quit-apps window (existing QuickToggles?) closing user apps via `CloseMainWindow` |
| action.quitAllExceptFront | Quit all apps except the front one | Quit apps | - | xmark.app -> E711 | quitAllExceptFront role | same, scope allExceptFront | same |
| action.cleanURL | Clean the copied link | Clean URL | "Clean URL" | link -> E71B | - | reads clipboard (shared lane); outcomes via HUD: blank/not URL "Paste a valid URL."; unchanged "Nothing to clean."; rewritten "URL cleaned." (copies); removed params "Removed a, b" (copies) | URL cleaner feature (`UrlCleaner`) |
| action.layout.<action> (43 rows, see 1.11.3) | per layout action | Window layout | "Window layout" (+ " Full Screen" for Maximize, + " Maximize" for Full Screen) | per action | the *layout's own* saved shortcut; needsPermission w/o AX | `WindowLayoutService.apply(action)` after beat; beep on failure | WindowLayoutFeature (SetWindowPos with `MonitorFromWindow` work areas) |
| action.appUpdates | Check for app updates | App updates | "App updates" | arrow.down.app -> E896 | - | `AppUpdatesService.check()` + open Settings -> App updates | AppUpdates feature check + Settings |
| action.cleaner | Cleaner | Tools | - | sparkle -> E9CE (or EA80) | - | opens Settings -> Cleaner | Settings page |
| action.uninstaller | Uninstaller | Tools | - | trash -> E74D | - | opens Settings -> Uninstaller | Settings page |
| uninstall.browse | Uninstall Application | Uninstaller | - | trash | only if `uninstallerCommandBarEnabled` (default **false**); `countsUsage=false`, `keepsBarOpen` | sets category `uninstallApps` | n/a until Uninstaller exposes per-app removal |
| uninstall.finder | Uninstall app selected in Finder | Uninstaller | - | app.badge -> E8A5 | same | reads Finder selection | `[WIN]` "Uninstall app selected in File Explorer" - shell selection via `IShellWindows`; low priority |
| action.quickLauncher | Quick panel | Tools | - | square.grid.3x3 -> E80A (GridView) | quickLauncher role | show quick panel | Quick panel window |
| emoji.browse | Emoji | Command Bar | "Emoji" | face.smiling -> E76E | `keepsBarOpen` | `setCategory(.emoji)` | in-bar |
| kill.browse | Kill Process | Browse & Kill | - | xmark.octagon -> EA39 | only if Kill Process available and `killProcessCommandBarEnabled` (default **true**); `keepsBarOpen` | category killProcess, clears query | Kill Process feature process list |
| action.feedback.bug | Report a bug | Send feedback | - | ladybug -> E7CC? (Bug `EBE8`) | - | opens feedback window kind bug (beat) | Feedback window / GitHub issue |
| action.feedback.feature | Suggest a feature | Send feedback | - | lightbulb -> EA80 | - | feedback kind feature | same |
| action.restartApp | Restart Vorssaint `[WIN]` Restart Trayloom | "Vorssaint actions" `[WIN]` "Trayloom actions" | - | arrow.clockwise -> E72C | - | `FeatureRuntime.relaunchApp()` | `Process.Start(Environment.ProcessPath)` then `Application.Shutdown()` (single-instance mutex released first) |
| action.power.sleep | Sleep | General | - | moon.zzz -> E708 | no confirmation | `pmset sleepnow` | `Application.SetSuspendState(PowerState.Suspend, false, false)` / `SetSuspendState(FALSE,FALSE,FALSE)` (powrprof). Note: returns hibernate if hibernate is enabled-by-policy; set `bForce=false` |
| action.power.restart | Restart | General | - | arrow.clockwise.circle -> E777 (Restart) | confirm "Restart the Mac?" `[WIN]` "Restart the PC?" | System Events AppleScript `restart` (also asks on its own) | `ExitWindowsEx(EWX_REBOOT, SHTDN_REASON_FLAG_PLANNED)` after `AdjustTokenPrivileges(SE_SHUTDOWN_NAME)`; or `shutdown.exe /r /t 0` |
| action.power.shutDown | Shut down | General | - | power -> E7E8 | confirm "Shut down the Mac?" `[WIN]` PC | AppleScript `shut down` | `ExitWindowsEx(EWX_SHUTDOWN)` / `shutdown /s /t 0` |
| action.power.logOut | Log out | General | - | rectangle.portrait.and.arrow.right -> F3B1/E7E8 (SignOut `F3B1`? verify) | confirm "Log out?" | AppleScript `log out` | `ExitWindowsEx(EWX_LOGOFF)` / `shutdown /l` |
| action.wifi | **Turn Wi-Fi off** / **Turn Wi-Fi on** | General | "wifi wi-fi" | wifi / wifi.slash -> E701 / F384 (verify; `EB5E` WifiWarning?) | only offered when a Wi-Fi interface exists (state read in background; `nil` = no row); active dot when on | `CWInterface.setPower` on a background queue; failure = beep | `Windows.Devices.Radios.Radio.GetRadiosAsync()` -> kind `RadioKind.WiFi` -> `Radio.RequestAccessAsync()` then `SetStateAsync(RadioState.On/Off)` (needs package identity or works in desktop apps with the `DeviceCapability radios`; else `netsh interface set interface "Wi-Fi" disable/enable` requires elevation - use WinRT first) |
| folder.<path> (8 rows) | Downloads, Documents, Desktop, Movies, Pictures, Music, <home folder name>, Applications | Folder | "Folder <last component>" | folder icon of the path | `countsUsage` true, reveal path | `NSWorkspace.open(url)` | `explorer.exe <path>`: Downloads, Documents, Desktop, **Videos**, Pictures, Music, `%USERPROFILE%`, **Applications -> `shell:AppsFolder`** (display name via `SHGetFileInfo(SHGFI_DISPLAYNAME)`) |
| answer.date | Today | long date (e.g. "Monday, November 2, 2026") | - | calendar -> E787 | value chip = short date; `countsUsage=false`; shown only for names with first token >= 3 chars prefixing "today" | copies the short date | `DateTime.Now.ToString("d")`/`"D"` |
| answer.time | Time now | medium time ("3:41:05 PM") | - | clock -> E121 | value chip = medium time | copies | `"T"` pattern |
| action.openSettings | Open Settings | "Vorssaint Settings" `[WIN]` "Trayloom Settings" | - | gearshape -> E713 | - | open Settings -> General | open Settings window |
| action.openURL | (typed address) | Open in browser | - | globe -> E774 | transient row, only for a typed web address; `countsUsage=false` | open | `Process.Start` |

Notes: (a) `action.layout.*`, `action.keepAwake.*`, power etc. use the `matchTitle`-less title for ranking. (b) Vorssaint never offers a "Search the web" row (current Trayloom does - remove, see 1.14). (c) `action.volume`/`action.brightness` also run from a global shortcut only through the argument UI (they `needsPrompt`).

#### 1.11.2 Feature toggles (`toggle.<feature>`) - generated from the feature catalog
Every installed feature with **exactly one** enable key becomes "Turn on <Hub title>" / "Turn off <Hub title>" (id `toggle.<AppFeature raw value>`, subtitle = hub group title, keywords = feature name, icon = the feature's hub symbol, active dot when on, `needsPermission` when the feature needs Accessibility and it is missing). Run: flip the bool, `FeatureRuntime.sync([feature])`, HUD (feature symbol + feature name). Features with two or more keys are skipped (a guess would change the machine's behaviour): dockClick, quitWindowProtection, textSnippets, finderCutPaste, audioPriority; features with none (windowLayout, diskImageInstaller, mixer, micMute, keepAwake, launcher, quick toggles, quitApps, colour picker, OCR, cleaning mode, media tools, cleaner, uninstaller, homebrew, updates, screenshot, camera, scratchpad, command bar, recorder, wallpaper, kill process, port manager, monitors, connected devices, fan control) have action rows instead.
Special cases: `scrollInverter` -> two rows `toggle.scrollInverter.vertical` "Invert vertical scrolling" and `.horizontal` "Invert horizontal scrolling"; `mouseButtonShortcuts` -> `toggle.mouseButtonShortcuts` (hub title) and `toggle.mouseButtonShortcuts.spacesGesture` "Switch Spaces by dragging a button".
Full single-key list (raw ids): switcher, dockPreview, windowMaximizer, autoQuit, spacesOrder, scrollHorizontal, scrollZoom, focusFollowsMouse, smoothScroll, linearScroll, mouseAcceleration (`mouseAccelerationDisabled`), mouseNavigation, middleClick, keyboardDebounce, superKey, mouseClickDebounce, notch, notchGestures, notchTimer, notchAccessories, notchLyrics, notchQueue, notchLiveEqualizer, notchDownloads, notchNotifications, notchCalendar, notchAgents, notchWatch, notchMascot, radialMenu, clipboardHistory, pastePlain, finderRename, shelf, urlCleaner, soundOutputSwitcher, musicBlock, brightness, extraBrightness, bluetoothSleep. The kind cap in search is **5** toggle rows because they all share the verb ("turn on").
`[WIN]` Trayloom has no per-feature "enabled" master key separate from "installed". Define the equivalent: a per-feature persisted bool `enabled` where the Vorssaint feature has an on/off switch (default per Vorssaint defaults), consumed by the feature's `Start/Stop`; the toggle row flips it and calls a `Registry.Resync(featureId)` (stop+start). Do **not** map the row to uninstall.

#### 1.11.3 Window-layout rows (`action.layout.<raw>`), titles and symbols
Subtitle "Window layout"; icon `WindowLayoutAction.symbolName`; ids/titles (enUS): leftHalf "Left", rightHalf "Right", topHalf "Top", bottomHalf "Bottom", centerHalf "Center half", leftThird "Left 1/3", centerThird "Center 1/3", rightThird "Right 1/3", leftTwoThirds "Left 2/3", rightTwoThirds "Right 2/3", centerTwoThirds "Center 2/3", topThird "Top 1/3", middleThird "Middle 1/3", bottomThird "Bottom 1/3", topTwoThirds "Top 2/3", bottomTwoThirds "Bottom 2/3", topQuarter "Top 1/4", upperMiddleQuarter "Upper middle 1/4", lowerMiddleQuarter "Lower middle 1/4", bottomQuarter "Bottom 1/4", leftQuarter "Left 1/4", leftMiddleQuarter "Left middle 1/4", rightMiddleQuarter "Right middle 1/4", rightQuarter "Right 1/4", topLeftSixth "Top left 1/6", topCenterSixth "Top center 1/6", topRightSixth "Top right 1/6", bottomLeftSixth "Bottom left 1/6", bottomCenterSixth "Bottom center 1/6", bottomRightSixth "Bottom right 1/6", topLeft "Top left", topRight "Top right", bottomLeft "Bottom left", bottomRight "Bottom right", maximize "Maximize", marginMaximize "Maximize with Margin", fullScreen "Full Screen", center "Center", previousDisplay "Previous display", nextDisplay "Next display", restore "Restore" (= 43 actions incl. all above; the exact set/order is `WindowLayoutAction.allCases` = halves, thirds, quarters, sixths, corners, then maximize/marginMaximize/fullScreen/center, previousDisplay/nextDisplay/restore). SF symbols such as `rectangle.lefthalf.inset.filled`, `rectangle.tophalf.filled`, `arrow.up.left`, `arrow.up.left.and.arrow.down.right`, `scope`, `arrow.uturn.backward` -> Segoe: use the Window-layout spec's glyph set (same icons as its tiles) or generic: halves E8A0/ `E73F` (BackToWindow), maximize E740 (FullScreen)/ E739, restore E923/ E73F, center E7C8, prev/next display E7F4 with arrows (E76B/E76C).

#### 1.11.4 System Settings panes -> `ms-settings:` (source `macSettings`)
macOS enumerates the panes dynamically: every `*.appex` under `/System/Library/ExtensionKit/Extensions` whose Info.plist has `EXAppExtensionAttributes.SettingsExtensionAttributes.allowsXAppleSystemPreferencesURLScheme == true`; name = resolved `CFBundleDisplayName` (localized) -> `CFBundleDisplayName` -> `CFBundleName` -> file name; keywords = the pane's own translated `.searchTerms` plist words (in language folder `en`, `pt_BR`, `tr`, `ru`, `es`, `sk`, `de`, `fr`, `it`, `ja`, `ko`, `zh_CN`, `zh_TW`, `zh_HK`, `uk`, falling back to `en`; `title` then comma-separated `index` words, dedup case-insensitively, each <= 40 chars, max **160** words, groups read in key-sorted order; legacy `legacyPrefPaneBundleName` bundle as fallback); sorted by name; opened with `x-apple.systempreferences:<bundle id>`; scanned once per launch (re-read on language change) off the main thread; the source can be turned off (no scan at all).
`[WIN]` Ship a **static table** (name, URI, keywords) - Windows has no enumerable equivalent. Ids `macsettings.<uri>` (e.g. `macsettings.ms-settings:display`); open with `Process.Start(new ProcessStartInfo("ms-settings:display"){UseShellExecute=true})`. Subtitle "System Settings panes" `[WIN]` "Windows Settings pages"; icon `gearshape.2`. Table (name -> URI; keywords in lower case are additional search words):

| Pane | URI | Extra keywords |
|---|---|---|
| Display | ms-settings:display | screen monitor resolution scale orientation refresh rate brightness night light hdr |
| Night light | ms-settings:nightlight | blue light warm |
| Advanced display | ms-settings:display-advanced | refresh rate hz |
| Graphics | ms-settings:display-advancedgraphics | gpu |
| Sound | ms-settings:sound | volume speakers microphone input output audio |
| Volume mixer | ms-settings:apps-volume | app volume |
| Notifications | ms-settings:notifications | alerts focus do not disturb |
| Focus | ms-settings:quiethours | focus assist |
| Power & battery | ms-settings:powersleep | sleep screen timeout power mode |
| Battery saver | ms-settings:batterysaver | energy |
| Storage | ms-settings:storagesense | disk space cleanup storage sense |
| Multitasking | ms-settings:multitasking | snap windows desktops |
| Clipboard | ms-settings:clipboard | history |
| Nearby sharing | ms-settings:crossdevice | share |
| Remote Desktop | ms-settings:remotedesktop | rdp |
| System / About | ms-settings:about | device name specs version |
| Troubleshoot | ms-settings:troubleshoot | |
| Recovery | ms-settings:recovery | reset pc |
| Activation | ms-settings:activation | licence key |
| Bluetooth & devices | ms-settings:bluetooth | pair |
| Printers & scanners | ms-settings:printers | print |
| Mouse | ms-settings:mousetouchpad | pointer speed buttons scroll |
| Touchpad | ms-settings:devices-touchpad | gestures |
| Typing | ms-settings:typing | autocorrect spelling |
| Pen & Windows Ink | ms-settings:pen | stylus |
| AutoPlay | ms-settings:autoplay | |
| USB | ms-settings:usb | |
| Cameras | ms-settings:camera | webcam |
| Network & internet | ms-settings:network-status | connection |
| Wi-Fi | ms-settings:network-wifi | wireless |
| Ethernet | ms-settings:network-ethernet | cable lan |
| VPN | ms-settings:network-vpn | |
| Airplane mode | ms-settings:network-airplanemode | flight radios |
| Mobile hotspot | ms-settings:network-mobilehotspot | tethering |
| Proxy | ms-settings:network-proxy | |
| Personalization | ms-settings:personalization | |
| Background | ms-settings:personalization-background | wallpaper desktop |
| Colors | ms-settings:colors | accent dark mode light mode theme |
| Themes | ms-settings:themes | |
| Lock screen | ms-settings:lockscreen | |
| Start | ms-settings:personalization-start | menu |
| Taskbar | ms-settings:taskbar | tray icons |
| Fonts | ms-settings:fonts | |
| Installed apps | ms-settings:appsfeatures | uninstall programs |
| Default apps | ms-settings:defaultapps | file types browser |
| Optional features | ms-settings:optionalfeatures | |
| Startup apps | ms-settings:startupapps | login items autostart |
| Your info | ms-settings:yourinfo | account profile |
| Sign-in options | ms-settings:signinoptions | password pin hello fingerprint |
| Email & accounts | ms-settings:emailandaccounts | |
| Other users | ms-settings:otherusers | family |
| Access work or school | ms-settings:workplace | |
| Date & time | ms-settings:dateandtime | time zone clock |
| Language & region | ms-settings:regionlanguage | keyboard layout |
| Region | ms-settings:regionformatting | formats |
| Speech | ms-settings:speech | voice |
| Game Bar | ms-settings:gaming-gamebar | xbox |
| Game Mode | ms-settings:gaming-gamemode | |
| Captures | ms-settings:gaming-captures | |
| Accessibility | ms-settings:easeofaccess | |
| Text size | ms-settings:easeofaccess-display | |
| Mouse pointer & touch | ms-settings:easeofaccess-cursorandpointersize | cursor |
| Magnifier | ms-settings:easeofaccess-magnifier | zoom |
| Color filters | ms-settings:easeofaccess-colorfilter | color blind |
| Contrast themes | ms-settings:easeofaccess-highcontrast | |
| Narrator | ms-settings:easeofaccess-narrator | screen reader |
| Closed captions | ms-settings:easeofaccess-closedcaptioning | |
| Keyboard accessibility | ms-settings:easeofaccess-keyboard | sticky keys |
| Privacy & security | ms-settings:privacy | |
| Location | ms-settings:privacy-location | gps |
| Camera privacy | ms-settings:privacy-webcam | |
| Microphone privacy | ms-settings:privacy-microphone | |
| Windows Update | ms-settings:windowsupdate | updates |
| Update history | ms-settings:windowsupdate-history | |
| Delivery Optimization | ms-settings:delivery-optimization | |
| Windows Security | ms-settings:windowsdefender | antivirus defender firewall |
| Backup | ms-settings:backup | |
| Find my device | ms-settings:findmydevice | |
| For developers | ms-settings:developers | developer mode |
| Windows Insider Program | ms-settings:windowsinsider | |

Also useful "pane" rows outside `ms-settings:` (extras, optional): Device Manager `devmgmt.msc`, Disk Management `diskmgmt.msc`, Services `services.msc`, Task Manager `taskmgr`, Control Panel `control`, Network connections `ncpa.cpl`, Programs & Features `appwiz.cpl`, System properties `sysdm.cpl`, Registry Editor `regedit` (needs elevation), Event Viewer `eventvwr.msc`. Keywords for each include their translated names (see `scan`: no scraping on Windows).

#### 1.11.5 Settings-page rows (`settings.*`)
Built from `SettingsDirectory.searchItems` (every Settings page, every hub feature's page, and individual settings by anchor, each with title, search keywords and icon) except pages already covered by actions (`general, cleaner, uninstaller, appUpdates`) and pages hidden by feature availability. Ids `settings.<page>`, `settings.feature.<feature>`, `settings.setting.<anchor>`; subtitle = "Vorssaint Settings" `[WIN]` "Trayloom Settings"; rank bias **-40**, cap 4 in search; Return opens the Settings window at the routed destination (`SettingsSearchSupport.route`, scroll to the anchor and highlight, optionally select the sidebar feature). `[WIN]` generate from the Trayloom Features hub model (feature name + description words as keywords) and the settings page registry.

#### 1.11.6 Machine answers
| id | title (live) | subtitle | value chip | icon | run |
|---|---|---|---|---|---|
| answer.battery | Battery | "charging" / "on power" / "Battery" (state from `BatteryPowerSupport.state`) | `NN%` | battery.75 (on battery) / battery.100.bolt -> E83F (Battery) / E83E (Charging) | copy `NN%` |
| answer.memory | "Memory" or "App Memory" (when monitor metric = app) | "%@ of %@ in use" (used, total, byte-formatted) | `NN%` | memorychip -> E950 | copies "used / total" |
| answer.storage | Storage | "%@ available of %@" (free, total) | free percent `NN%` | internaldrive -> EDA2 | copies the free size |
Offered only when read succeeded (laptop with a battery; memory monitor feature available; boot volume readable). Values come from **background reads** started on open (`cachedBattery`, `cachedMemory`, `cachedBootVolumeSpace`) so the keystroke path never blocks.
`[WIN]` battery: `GetSystemPowerStatus` (`BatteryLifePercent` 0-100, `ACLineStatus`, `BatteryFlag` 8 = charging, 128 = no battery -> no row); memory: `GlobalMemoryStatusEx` (`dwMemoryLoad`, `ullTotalPhys`, `ullAvailPhys`; "app memory" metric not available -> only "Memory"); storage: `new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory))` `AvailableFreeSpace` / `TotalSize`. Byte formatting = decimal "GB/TB" like `ByteCountFormatter(.file)`.

---

### 1.12 Settings page ("Command Bar") and the App shortcuts sheet

#### 1.12.1 Layout (grouped Form, top to bottom)
1. **Section, header "Command Bar"** (first card):
   * Row of two buttons: **Open the bar now** (no icon); **Recenter the bar** (disabled unless the bar was dragged, i.e. `hasCustomPosition`).
   * One explanation block (a single row, `.caption`, secondary, spacing 6): `positionCaption`; `settingsCaption`; `Label(privacyNote, systemImage: "lock.laptopcomputer")` (`[WIN]` glyph E72E/`EC47`); a line "Try" (tertiary) + example capsules (10.5 pt rounded, padding 6x2, primary 6 % fill): `100 km to mi`, `2+2*3`, `brightness 40`, `fire`, `battery` (battery chip only if an internal battery exists).
   * **Toggle** "Compact mode" (`commandBarCompactMode`, default off; no callback: the bar reads it on every open) + caption "Bar opens without suggestions. Results appear as you type."
   * Only if the Emoji source is enabled: **segmented Picker** "Skin tone" (`commandBarEmojiSkinTone`; six segments showing ✋ in default/light/medium-light/medium/medium-dark/dark; tag = raw value) + caption "By default, emoji that can take a tone use this one. Open the actions on an emoji row to choose a different one."
   * **Toggle** "Global shortcut to open the bar" (`commandBarShortcutEnabled`, default **off**; onChange -> `syncWithPreferences`). Not the generic "Global shortcut" label because the page already has an "open now" button.
   * **Shortcut row** (`ShortcutPreferenceRow(role: .commandBar)`): recorder button showing the saved combination (default ⌥Space / `[WIN]` Alt+Space), disabled while the toggle is off; commits call `syncWithPreferences`.
   * Orange caption "macOS rejected this shortcut. Choose another one." `[WIN]` Windows - when enabled and registration failed.
   * Secure-input row: shown when macOS secure input is held by an app (typing actions blocked). `[WIN]` omit.
   * **Disclosure row "More options"** (full-row toggle, chevron at the trailing side) -> when expanded: **Toggle** "Switch to an ABC layout while the bar is open" (`commandBarASCIILayoutEnabled`, default off) + caption "The field types Latin characters whatever layout was active, and the previous layout comes back when the bar closes." `[WIN]` toggle text "Switch to an English (US) layout while the bar is open".
2. **Section (no header)**: button with icon `app.badge` (`[WIN]` E71D/`E8FD`) "App shortcuts" -> opens the sheet 1.12.2; caption "Open apps with their own shortcuts, even when the bar is closed. Set search names and pin favorites here."
3. **Section, header "What the bar searches"**, footer caption "Turn off what you never want to see. Your own actions always stay.": one **Toggle** per `CommandBarSource` in enum order (`actions` first, **disabled**; value = not in `commandBarDisabledSources`): rows labelled with the source symbol + title: Vorssaint actions (Trayloom actions), Apps, Menu commands of the app in front, Open windows, Quit an app, Uninstaller, Settings pages, System Settings panes (Windows Settings pages), Snippets, Clipboard history, Emoji, Folders, Answers about this Mac (PC), Sums and conversions, What is selected, Your shortcuts, Files, Kill Process. Setting writes the sorted comma list and calls `syncWithPreferences` (which re-reads caches and, for files/panes, re-scans or drops caches).
4. **Section, header "Search your files"**: caption `filesCaption`; empty text "No folders yet, so the bar looks for no files." when none; one row per scope (icon `folder` 11 pt accent 16 wide, path 12 pt single-line middle-truncated, **Remove** mini bordered); **Add a folder** (`plus` label) -> folder picker, multi-select, stored `~`-abbreviated; **More options** disclosure -> caption "Names never worth showing: a whole folder or file name, or an extension written as *.log.", rows (icon `eye.slash`, pattern 12 pt, Remove), a text field "A folder or file name" (Return = add) + **Add** (small bordered, disabled when blank). Lists are indented under the disclosure. Every change -> `syncWithPreferences` (invalidates file-search caches).
5. **Section, header "Your shortcuts"**: empty caption "Nothing saved yet. Add a site, folder, search or script you use every day."; rows: kind icon (`link`/`folder`/`terminal`, 11 pt accent), name (12 pt semibold rounded), destination (11 pt secondary, single line, middle truncation), spacer, **Edit** / **Remove** (mini bordered); button **Add shortcut** (`plus`). **Link editor sheet** (440 wide, padding 20, VStack spacing 14): title "Your shortcuts" (15 pt semibold); segmented picker (no label) "Site or link" / "Folder or file" / "Script"; labelled text field "Name" (label caption secondary); labelled text field "Where it goes" + **Choose…** button for Folder/Script kinds (file picker; folders allowed only for `place`; fills the name with the file name when empty); for Site/Folder: caption "Drop one of these in and it gets filled in when it opens" and four capsule buttons (token on top 10.5 pt semibold rounded, meaning below 9 pt secondary: `{query}` what you type, `{clipboard}` what you copied, `{selection}` what is selected, `{date}` today; clicking appends the token to the destination, tooltip = meaning); for Script: caption `scriptHint` + two small toggles "Also run when its name is typed on its own" (`runsWithoutArgument`) and "Run from its global shortcut without opening the bar" (`runsDirectly`); footer: **Cancel**, **Save** (prominent, disabled until name and destination are non-blank; saving trims both). Max 60 links.
6. **Section, header "Rows with their own shortcut"**: empty "No row has its own shortcut yet. Open the actions on any row to give it one."; rows: shortcut capsule text (11 pt semibold rounded accent), title (12 pt secondary, 1 line), spacer, orange "macOS rejected this shortcut. Choose another one." if refused, **Remove** (mini). Sorted by title (localized standard).
7. **Section, header "Names you gave"**: empty "Nothing named yet. Open the bar, pick a row and press the actions key."; rows alias (12 pt semibold rounded accent), title (12 pt secondary), **Remove**; sorted by alias.
8. **Section, header "Pinned"**: empty "Nothing pinned yet."; rows title + **Remove**.
9. **Section, header "Never shown"**: empty "Nothing hidden."; rows title + **Remove** (unhide, then `syncWithPreferences`); last row button **Forget what I use most** (`forgetLearnedRanking`: clears `commandBarUsage`, `commandBarQueryHabits`, session memory, prepared digests).
Titles for stored keys come from the live catalog (`entryTitle(forStableKey:)`); an app that is not installed any more still shows by its **key** so nothing set can become invisible.

#### 1.12.2 App shortcuts sheet (`CommandBarAppShortcutsView`)
Sheet 780 x 560, padding 24, spacing 16.
* Header: "App shortcuts" (title2 semibold) + caption "Open apps with their own shortcuts, even when the bar is closed. Set search names and pin favorites here." (callout secondary).
* Toolbar row: search field (magnifier glyph, placeholder "Search apps", plain, clear X when non-empty; background quaternary, radius 7, padding 7) + segmented **"All" / "Pinned" / "With shortcuts"** (label "Apps", labels hidden, fixed size).
* **Table** (inset style, sortable columns): **Apps** (icon 24 + name, 1 line, tooltip title; width min 150 ideal 210), **Alias** (rounded text field, placeholder "The name you call it", min 110 ideal 150; commit on Return, focus loss or disappear; trims, max 60; if another row has it -> message "<app>: <name> already answers to that" and the draft reverts; empty clears), **Shortcut** (recorder button width 140: shows combination or **"Record…"** when none; waiting label "Press keys"; clear button `xmark.circle` with tooltip "Take the shortcut off"; orange triangle when the system refused it; width 180), **Pinned** (button `pin`/`pin.fill`, accent when pinned, tooltip "Pin to the top"/"Unpin"; min 60 ideal 80). Overlay when empty: loading -> spinner + "Still looking"; else large magnifier + "Nothing here by that name."
* Data: `appEntries` from the bar's scan reversed, then one entry per `stableKey` (several installed copies share one binding; the last entry wins like the launcher), filtered by search (folded `title keywords alias` contains folded query) and by the segmented filter, sorted by `CommandBarAppSort` (name = localizedStandardCompare; alias/shortcut = rows that have a value first, nil last, ties by name; pinned = pinned first, ties by name; ascending/descending flip applies to the primary key only).
* Footer: left caption = last error "<app>: <message>" (orange) or "Press the keys you want · Delete clears it · Esc goes back"; right **Done** (Esc closes). Take-over offer card (system-owned combination) only on macOS. The sheet calls `refreshApplications()` on appear, dismisses itself if the feature becomes unavailable, clears messages when the UI language changes. `[WIN]` Same layout in a `Window` (ShowDialog) with a `DataGrid`.

---

### 1.13 Settings keys and persistence (names are the macOS `UserDefaults` keys; keep them in `Ctx.Settings`)

| Key | Type | Default | Meaning |
|---|---|---|---|
| commandBarShortcutEnabled | bool | **false** | registers the global shortcut |
| commandBarShortcut | string (`GlobalShortcut.storageValue`) | Option+Space (`[WIN]` "Alt+Space") | bar shortcut |
| commandBarCompactMode | bool | false | field-only home |
| commandBarASCIILayoutEnabled | bool | false | borrow a Latin layout while open |
| commandBarEmojiSkinTone | string | `""` | `""`, `light`, `mediumLight`, `medium`, `mediumDark`, `dark` |
| commandBarDisabledSources | string | `""` | comma list of disabled `CommandBarSource.rawValue`s, sorted when written; `actions` ignored |
| commandBarAliases | string (JSON object `{stableKey: alias}`) | `""` | aliases; values trimmed, empty dropped on read |
| commandBarPins | string (newline-separated stable keys) | `""` | ordered, unique, max 30 |
| commandBarHidden | string (newline-separated) | `""` | rows never shown |
| commandBarLinks | Data/JSON array of `CommandBarLink` | empty | your shortcuts; max 60 |
| commandBarRowShortcuts | string (JSON `{stableKey: GlobalShortcut storage}`) | `""` | per-row global shortcuts, max 64 |
| commandBarPositionOffset | string `"dx,dy"` | `""` | integer offset from default spot |
| commandBarFileScopes | string (newline-separated, `~`-abbreviated) | `""` | folders to search; empty = feature off |
| commandBarFileIgnores | string (newline-separated) | `""` | user ignore names/patterns |
| commandBarUsage | string (JSON `{rowId: {count, lastUsed}}`) | unset | max 200 ids |
| commandBarQueryHabits | string (JSON `{digest: {rowId: {count,lastUsed}}}`) | unset | max 320 digests x 4 rows |
| commandBarQueryHabitKey | string (base64 of 32 random bytes) | created lazily | HMAC key; deleting it wipes the habits |
| panelUtilityCommandBar | bool | true | show the quick-panel tile |
| notchCommandBar | bool | true | bar comes out of the island (island setting) |
| notchCommandBarStyle | string `droplet`/`island` | `droplet` | island presentation |
| uninstallerCommandBarEnabled | bool | false | uninstall rows in the bar (Uninstaller page toggle "Show in Command Bar") |
| killProcessCommandBarEnabled | bool | true | kill-process rows (Kill Process page toggle "Show in Command Bar") |
Stable key formats: app `app.bundle.<bundleId>` else `app.<path>`; uninstall `uninstall.bundle.<id>`/`uninstall.<path>`; everything else = its row id. **Row ids that persist must never change**: `emoji.browse`, `kill.browse`, every `action.*`/`toggle.*` id, `snippet.<uuid>`, `link.<uuid>`, `folder.<path>`, `macsettings.<id>`, `settings.*`.
`[WIN]` stable key for an app = `app.aumid.<AUMID>` when it has one (UWP / shortcut `System.AppUserModel.ID`), else `app.path.<lower-cased resolved .lnk target path>`; keep ids `app.<id>` for rows. A shortcut moving between Start-menu folders must not lose its alias.
Nothing a user types is persisted. The "last query" is kept in memory for the next open (the first keystroke replaces it).

---

### 1.14 Gap vs Trayloom (`src/Trayloom.Features.Tools/CommandBar/{CommandBarFeature,CommandBarSupport,CommandBarWindow}.cs`)

What exists today: a feature `command-bar` (Id/Name "Command bar"), Ctrl+Alt+R hotkey via `BindHotkey`, a 640 px wide WPF window (14 px radius, magnifier glyph, `TextBox`, `ListBox` max 8 rows x52 px with a "kind" label on the right), `Fuzzy.Score` (exact 1000 / prefix 850 / contains 650|420 / subsequence), per-token ranking, `+30` for actions, usage counter `usage` (`Dictionary<string,int>`, `min(count,20)*6`), `AppIndex` (Start Menu `.lnk` scan every 10 min, icon cache), `Calculator` via `DataTable.Compute`, direct URL/path rows, a "Search the web" row, Esc/Up/Down/Tab/Enter, hide on deactivate, 160 ms dispatch delay, three settings (hotkey, include apps, include calculator, web search url) + "Rescan installed apps".

Prioritised checklist (P0 = needed for replica parity of the main experience):
* **P0 Branding/layout**: rename to "Command Bar"; hub description "One field that finds and runs everything the app does"; panel 560 px, radius 22, acrylic+plate, brand mark (22 px) instead of the magnifier, clear (X) button, field 50 px, 16 pt text, divider, footer (shortcut + "Ctrl+K Actions", hints, "Esc"), selection highlight accent 14 % (radius 10), 30 px glyph plates, matched-letter highlight, right-side answer value/shortcut chips with a return glyph on the selected row, section headers, category chips row, "Try" example chips, empty state with "See suggestions", compact mode, row/list sizing (452 pt ceiling), "ESC" hints.
* **P0 Default behaviour**: shortcut default Alt+Space and the separate "Global shortcut to open the bar" toggle (default off) - today it is always armed on Ctrl+Alt+R; position at 28 % from top on the pointer's monitor (today 16 %), top-anchored growth, draggable via the mark + double-click recenter + persisted offset + "Recenter the bar".
* **P0 Search engine**: replace `Fuzzy` with the exact algorithm (folding, tokens, 140/80/44/24/16 scoring, +1200/900/700/350 whole-query bonuses, tiers, priority/boost, kind caps, 12 rows total, feature ordering); usage with age weights and cap 200; query habits (HMAC digests), session memory, aliases (2400/1100), pins (+60), hidden, rank bias per source (-80 menus, -40 files/settings, +80 apps), duplicate-id removal, typing always selects row 0.
* **P0 Empty bar**: Selected / Pinned / Suggestions / browse groups (12 per group) / clipboard (6) with headings; categories + chip walk with ←/→; Esc ladder.
* **P0 Result kinds missing**: Open windows (EnumWindows), Menu commands (UIA / `GetMenu`), Quit an app, Clipboard paste (from `ClipboardHistoryStore`), Snippets (from the Input feature), Emoji (`:` + category, skin tones, data resource), Folders, Files (Windows Search), machine Answers (battery/memory/storage/date/time), Selection rows (UIA TextPattern), Your shortcuts (links/places/scripts + placeholders), Settings pages/features/settings rows, System Settings panes (`ms-settings:` table), feature toggles, power rows (sleep/restart/shut down/log out with confirmation), Wi-Fi toggle, volume + brightness numeric argument mode, mute, sound output switch, keep-awake presets, quick-toggle rows (dark mode, lock, display off, screen saver, eject, hidden files, desktop icons, recycle bin), quit-all rows, clean URL, window-layout rows (43), feedback rows, "Restart Trayloom", "Open Settings", `action.openURL`.
  Today only `Ctx.Actions.All` (generic per-feature actions) and Start-Menu apps are searched; row ids are `"{feature}/{action}"` and `"app/{name}"` - they must become the stable ids of 1.5/1.11 so pins/aliases/shortcuts survive.
* **P0 Modes**: `argument` (range placeholder, chip), `confirm` (card + Cancel/Confirm, mouse path), `actions` (⌘K panel), `naming`, `capturingShortcut`; run algorithm (needsSetup -> confirm -> numeric -> finish), `queryWhenRun`/`selectionWhenRun`, `keepsBarOpen`, `prefill`.
* **P1 Parsers**: calculator rewrite (functions, constants, percent semantics, bracket completion, number formats, Tab-reuse), unit conversion, colours (+ conversion + preview), dates/time zones; "= value" row styling (17 pt, subtitle "Return copies").
* **P1 Settings page** with all nine sections of 1.12 (today: one hotkey + 3 fields); App-shortcuts sheet; row-shortcut registration/capture/conflict rules; sources toggles; file-search scopes/ignores; links editor.
* **P1 Typing at the caret / paste**: foreground-window restore + modifier wait + `SendInput`; selection read **before** showing the bar (the bar activates on Windows, so capture the foreground window and its UIA selection in `Show()` before `Present()`); today `Execute` only delays 160 ms and relies on the previous window regaining focus on `Hide()`.
* **P1 ASCII layout switch**, IME-aware key handling (do not steal Enter/arrows while composing: check `InputMethod.Current`/TSF composition).
* **P2 Droplet/island presentation + mascot** (depends on Island feature), hurry/rewind/retract motions, reduce-motion.
* **Remove / change**: the "Search the web" row (Vorssaint never sends typed text to the internet except through user-saved links; its privacy note says so) and its `webSearchUrl` setting - or demote to an opt-in link the user creates in "Your shortcuts" (e.g. name `g`, `https://www.google.com/search?q={query}`); direct "Open <path>" rows for typed local paths are not in Vorssaint (only typed web addresses); `Include installed apps`/`Include calculator` toggles become the Apps and "Sums and conversions" source toggles; the "kind" text on the right of rows is replaced by subtitles + chips; the usage dictionary shape changes (migrate or reset).
* **Window mechanics**: keep `Deactivated` -> hide but also close on outside click; add the `Ctrl+,` shortcut; `Topmost`+tool window; DPI-aware positioning (existing SetWindowPos code is good); animate height on list growth (Vorssaint re-fits instantly, no animation - keep instant).

#### Implementation notes (suggested class split in `Trayloom.Features.Tools/CommandBar/`)
`CommandBarFeature` (lifecycle, hotkeys, settings), `CommandBarCatalog` (+ `CommandBarEntry` record carrying the 20 fields of 1.5), `CommandBarSearch` (fold/score/rank/highlight), `CommandBarPreferences` (sources, aliases, pins, hidden, position), `CommandBarUsage` + `QueryHabits` + `QueryMemory`, `CommandBarService` (mode machine, presentation id guard, background loaders), `CommandBarMath/Units/Dates/Colors/Emoji`, `CommandBarLinks` + `ScriptRunner`, `CommandBarFileSearch`, `CommandBarMenus` (UIA), `CommandBarSelection`, `CommandBarExtras` (power, Wi-Fi, folders, typing), `CommandBarWindow` + `CommandBarView` pieces, `CommandBarAppShortcutsWindow`, `CommandBarDroplet(Motion)`. Unit-test the pure parts (search, math, units, dates, colors, links, file rules, preferences) with the vectors in this section.

##### Test vectors worth copying
* `normalized("  Ｃafé   ")` -> `cafe`. `score("Battery","", "bat")` = token 80 + prefix 900 = 980. `score("Quit Safari","", "sfr")` -> 24 (in-order subsequence of the word `safari`, no bonus); `"qs"` -> no match (fuzzy needs >= 3 letters).
* `2+2*3` -> `8`; `10 + 20%` -> `12`; `20% of 150` -> `30`; `(1+2` -> `3` with closing `)`; `2^3^2` -> `512`; `0.1+0.2` -> `0.3`; `1,234 + 1` -> `1,235`; `sqrt(16)` -> `4`; `1/0` -> not a calculation; `3` -> not a calculation; `2026-07-27` -> not a calculation.
* `100 km to mi` -> `62.14 mi` (en-US); `20c to f` -> `68°F`; `5 in to cm` -> `12.7 cm`; `1 gb to mb` -> `1,000 MB`; `1 gib to mib` -> `1,024 MiB`; `70 in to ft` -> `5 ft 10 in`.
* `#a2b3b4 to rgb` -> `rgb(162, 179, 180)`; `rgba(255,0,0,.5) to hex` -> `#FF000080`; `hsl(120, 100%, 50%) to rgb` -> `rgb(0, 255, 0)`.
* `in 3 weeks`, `3 days ago`, `days until 25/12`, `time in tokyo` (answers); `3 dias` alone -> no answer.
* Link `gh` = `https://github.com/search?q={query}`: query `gh vorssaint utils` opens `https://github.com/search?q=vorssaint%20utils`; query `ghost` does not match; `gh` alone prefills `gh `.
* File ignore: pattern `build` hides `/x/build/a.txt` but not `/x/rebuild-notes.md`; `*.log` hides `a.log`; `.log` hides `x.log` but not a file named exactly `.log`.


---

## 2. Quick panel and quick tools

Scope of this part: Quick panel (Swift name "Quick Launcher"), Quick toggles, Quit apps, Color picker, Copy text from screen (Screen OCR), QR/barcode reading, and the shared quick-tool plumbing (`QuickToolHUD`, `QuickToolHotkey`, `WindowActivationPolicy`). The Mic mute feature itself belongs to another part; only the places where it surfaces in the quick tools are listed here (2.2 row 3, 2.1 item `micMute`, 2.8 Mic mute section).

Source files covered (all under `E:\REA TEST 1\vorssaint-mac\Sources\Vorssaint`): `Services/QuickTools/{QuickLauncherService, QuickTogglesService, QuickTogglesSupport, QuickToolsSupport, QuickToolHotkey, QuickToolHUD, WindowActivationPolicy, QuitAppsPanelController, ColorSamplerService, ScreenTextService, BarcodeDetector, QRResultController}.swift`, `UI/QuickLauncher/QuickLauncherView.swift`, `UI/MenuPanel/QuickTogglesSection.swift`, `UI/ColorSwatch.swift`, `Core/{ColorValue, QuickToggleStrings}.swift`, `UI/Settings/{QuickToolsSettings, GeneralToolSettings, ScreenCaptureSettings, QuitAllExceptionsList, DiskExclusionsList}.swift`, and the colour-loupe code inside `Services/QuickTools/ScreenshotSelectionController.swift` + `ScreenshotSupport.swift`.

Conventions used below:
- `[WIN]` = platform noun substituted for Windows. The original English text is always quoted next to it; implementers keep the original wording for any string with no `[WIN]` marker.
- Segoe Fluent Icons code points are best-effort suggestions (marked "verify"): confirm each glyph in the Segoe Fluent Icons catalogue before shipping.
- "Panel" = the Vorssaint menu-bar popover (Windows: the tray flyout). "Quick panel" = the floating launcher (this part, 2.1).
- Defaults are the values registered in `Core/Defaults.swift`. Storage on Windows is the `IFeatureSettings` JSON store; the Swift key names are given so settings stay 1:1 (keys can be namespaced per feature id on disk).

### 2.0 Shared conventions

#### 2.0.1 Keyboard shortcut mapping (used by every hotkey in this part)
macOS modifier combos have no 1:1 on Windows (the Win key is reserved by the shell: Win+V = clipboard history, Win+Ctrl+C = colour filters, etc.). Proposed mapping, to be reconciled with the global-shortcuts part of the spec:

| macOS | Windows | Notes |
|---|---|---|
| ⌃⌘ (Ctrl+Cmd) | Ctrl+Alt | `RegisterHotKey` MOD_CONTROL+MOD_ALT |
| ⌃⌥⌘ (Ctrl+Opt+Cmd) | Ctrl+Alt+Shift | matches what Trayloom already uses for Colour picker (Ctrl+Alt+Shift+C) and OCR (Ctrl+Alt+Shift+T) |
| ⌘ inside a window | Ctrl | e.g. ⌘↩ -> Ctrl+Enter |
| ⌥ inside a window | Alt | e.g. wheel "hold ⌥ to temporarily use the other mode" -> hold Alt |
| ⌫ / ↩ / ⎋ | Backspace / Enter / Esc | |

Default shortcut table for this part (all stored as `GlobalShortcut.storageValue` strings in Swift; on Windows store the `Hotkey` text such as `Ctrl+Alt+V`):

| Role | macOS default | Windows default (proposed) | Enabled by default |
|---|---|---|---|
| Quick panel (`quickLauncher`) | ⌃⌘V | Ctrl+Alt+V | **true** |
| Colour picker | ⌃⌥⌘C | Ctrl+Alt+Shift+C | false |
| Copy text from screen | ⌃⌥⌘T | Ctrl+Alt+Shift+T | false |
| Mic mute | ⌃⌥⌘M | Ctrl+Alt+Shift+M | false |
| Quit all apps | ⌃⌥⌘Q | Ctrl+Alt+Shift+Q | false |
| Quit all apps except the front one | ⌃⌥⌘O | Ctrl+Alt+Shift+O | false |

The shortcut display string shown in rows/footers uses the Windows format `Ctrl+Alt+V` (Swift shows glyphs like `⌃⌘V`).

"Shortcut rejected" caption (`shortcutUnavailable`, shown orange `.caption` under a shortcut row when the toggle is on and `RegisterHotKey` failed): "macOS rejected this shortcut. Choose another one." -> `[WIN]` "Windows rejected this shortcut. Choose another one."

#### 2.0.2 Strings for shared UI verbs
| Key | English |
|---|---|
| `uninstallerCancel` | Cancel |
| `menuClose` | Close |
| `obBack` | Back |
| `menuSettings` | Settings… |
| `panelHideItem` | Hide from panel |
| `panelShowItem` | Show in panel |
| `panelHiddenItem` | Hidden |
| `actionRemove` | Remove |
| `autoQuitAddApp` | Add app… |
| `quickToolShortcutToggle` | Global shortcut |
| `permissionRequired` | Permission required |

(`permission*`, "Open System Settings…", Screen Recording / Automation rows are macOS-only concepts; on Windows no quick tool needs a permission, see Windows mapping per feature. Do not render permission rows.)

#### 2.0.3 Persistence helpers
- Item order (`PanelLayout.itemOrder`): the stored value is a comma-joined list of enum raw ids. Sanitiser (`Defaults.sanitizedPanelItemOrder`): split on `,`; keep only ids that exist in the enum's default order, drop duplicates (first occurrence wins); then append every enum id that is missing, in default (declaration) order. So a new item always joins a saved order at the end. Reset = delete the key.
- Hidden set (`QuickToolsSupport.hiddenIDs/serializeHiddenIDs`): comma-joined raw ids, sorted alphabetically on write; read = split on `,`, drop empties.
- Drag-to-reorder (`PanelReorderableItem` + `PanelItemDropDelegate`): while a drag hovers over another item, the dragged item is moved to that item's index immediately (move-offsets semantics: if dragging downward insert after the target, else before); drop clears the dragging state. Only active in edit mode; the drag payload is the raw id as text. Windows: WPF `DragDrop.DoDragDrop` with a custom data format, `DragEnter`/`DragOver` handlers doing the live reorder.

---

### 2.1 Quick panel (Quick Launcher)

Hub identity: title `launcherName` = "Quick panel"; hub description `descQuickLauncher` = "A floating panel with your favorite tools"; feature symbol `wand.and.rays` (suggest U+E945 or any "magic wand/lightning" glyph, verify); group = Tools; `installedByDefault = true`; no permission. Settings page = "Quick panel" (`quickToolsTab`), section anchor `.quickLauncher`. Features-hub row in the tray-panel Utilities tab: title "Quick panel", caption `launcherCaption`, icon `square.grid.2x2` (U+F0E2 or U+E80A "GridView", verify), chevron, shortcut chip showing `⌃⌘V` when enabled (see `panel-utilities.png`).

#### 2.1.1 Strings
| Key | English |
|---|---|
| `launcherName` | Quick panel |
| `launcherCaption` | A floating panel with your favorite tools, summoned by a shortcut from anywhere. |
| `launcherOpenNow` | Open quick panel |
| `launcherEditHint` | Use the tune button to choose, hide and drag the tools around. |
| `launcherEmptyState` | All tools are hidden. Use the tune button to add them back. |
| `launcherAddSection` | Add back (rendered UPPERCASE: "ADD BACK") |
| `launcherKeysHint` | Arrows navigate, Enter opens, 1 to 9 open directly (defined, not shown in the current views) |
| `quickToolShortcutToggle` | Global shortcut |
| notch-only (`FeatureStrings.notch.customizeTools`) | Customize tools (tooltip of the island's customise button) |

Tile titles (see table 2.1.3). Option-card strings: `clamshellTitle` "Keep going with the lid closed"; `defaultDurationLabel` "Default duration"; `minutes15` "15 minutes", `minutes30` "30 minutes", `hour1` "1 hour", `hours2` "2 hours", `hours4` "4 hours", `hours8` "8 hours", `indefinite` "Indefinite"; `micMuteMenuBarToggle` "Show in the menu bar while Mic Mute is on" -> `[WIN]` "Show in the system tray while Mic Mute is on"; clipboard `enable` "Save clipboard history", `limit` "Limit", `limitUnlimited` "Unlimited"; `colorPickerFormatLabel` "Copied format"; `colorPickerBareHexToggle` "Copy without the # prefix".

#### 2.1.2 Layout (floating panel, the non-island variant)
Window: borderless, transparent, always-on-top ("floating" level), joins all desktops, not in Alt-Tab; takes keyboard focus (needed for arrows/digits/Esc) but does not activate the app in a way that steals the front app's menu. Content width **420**, height = fitting size (grid height + header + footer; default content rect 420x380). Padding 16, vertical stack spacing 12. Background: HUD blur ("hudWindow" material = acrylic/blur-behind) with a legibility plate (black 55% in dark theme / white 50% in light) and a hairline border (dark: white 12%, light: black 8%, 0.8 px). Corner radius **22** (continuous). Reduce-transparency => opaque.

Top to bottom:
1. **Header** (VStack spacing 6): a ZStack, centred brand mark 52 pt wide x 30 pt tall (Trayloom mark; tint near-black 3% white in light theme, white in dark), left button, right button.
   - Grid state: left = close button (`xmark.circle.fill`, 15 pt semibold, secondary colour, 26x26 hit area, tooltip/a11y "Close"; Segoe U+E711 or U+EA39 verify); right = **tune** toggle (`slider.horizontal.3` when not editing, secondary, 15 semibold, 26x26, tooltip = `launcherEditHint`, a11y label "Settings…"; when editing it becomes `checkmark.circle.fill` in accent colour). Suggest U+E9E9 (Equalizer/"Sliders") for tune, U+E73E for check (verify). Toggling edit animates ease-out 0.15 s.
   - Utility-hosted state: left = back button (`chevron.backward.circle.fill`, same size, tooltip/a11y "Back"), right = close.
   - When editing (grid state) the **edit hint** appears under the header row: `launcherEditHint`, 9.5 pt, secondary, centred, multi-line.
2. **Body**, exactly one of:
   - Hosted utility (see 2.1.5): a scroll view (no indicators), fixed height **470**, content = the utility's panel view.
   - **Empty state** (no visible items and not editing): text `launcherEmptyState`, 11 pt, secondary, centred, vertical padding 26.
   - **Grid**: 3 flexible columns (`QuickLauncherService.columns = 3`), spacing 10 both axes, row-major.
3. **Options card** (only when a utility is not open, editing, and an options item is selected that has quick options): see 2.1.6.
4. **Hidden tray** (only when editing and at least one item is hidden): heading "ADD BACK" (`launcherAddSection.uppercased()`, 9 pt bold, secondary, letter-spacing 0.5), then a wrapping flow (spacing 6) of chips: `Label(title, plus.circle.fill)` 10 pt medium, horizontal padding 8, vertical 4, capsule fill accent 13%. Click = un-hide (animated 0.15 s).
5. **Footer** (HStack spacing 5): keyboard glyph (`keyboard`, 8.5 pt, tertiary) + the Quick panel shortcut display string (9 pt semibold rounded, tertiary; e.g. `Ctrl+Alt+V`), spacer, "Esc" (9 pt semibold rounded, tertiary).

**Tile (grid cell)** — a plain button, `contentShape` rounded 13:
- Cell: VStack spacing 7 (icon tile + title); cell padding vertical 9; width = column width; background RoundedRect r13: selected = accent 14%, hovered = primary 7%, otherwise primary 3.5%; selected adds a 1.2 px border accent 55%.
- Icon tile: RoundedRect r12, 46x46, fill: active = accent 18%; selected or hovered = primary 10%; else primary 7%. Glyph 19 pt semibold, colour `.primary` 85%, accent when the item is "active", **red** for muted microphone.
- Title: 10.5 pt medium, `.primary`, centred, up to 2 lines with space reserved for 2 lines (tiles stay equal height), no horizontal truncation beyond the wrap.
- Overlays (top-right/left of the 46x46 icon): 
  - Not editing and item is active (Keep awake on, Mic muted, Recorder recording): green dot 8x8 at offset (+3, -3) from top-right.
  - Not editing and index < 9: number badge `N.circle.fill` (N=index+1), 13 pt, white glyph over a circle in accent (selected) or secondary 80%; placed top-left offset (-6, -6); non-interactive. (Windows: draw a 13 px circle with the digit.)
  - Editing: red **minus** badge (`minus.circle.fill`, 14 pt, white minus on red) top-right offset (+7, -7), tooltip/a11y "Hide from panel", click hides the tile; for items with quick options also a **gear** badge (`gearshape.circle.fill`, 14 pt; white on accent when its card is open, else white on secondary) top-left offset (-7, -7), non-interactive (the tile body click opens the card).
- Hover (not editing) = selects the tile (mouse selection; does not scroll). Click (not editing) = run. Click (editing) = open/close the options card for that tile if it has options (toggle); tiles without options do nothing in edit mode (drag reorders).

**Island (embedded) variant** (when the Dynamic Island routes the quick panel; implemented by the island part, but the view must support it): no header, no footer; padding 0; width/height come from the island; columns = island tool columns; spacing 6; tile icon 32x32 r16, glyph 16; title 10 pt; cell vertical padding 4; no cell background/border; number badges hidden; hidden-chip fill black; options card fill black; edit toggle is the island's top-row button (`slider.horizontal.3` / `checkmark`); the back button names the open utility. In normal (non-edit) island mode tiles run sideways as a rail with the number of rows the height allows; editing keeps a plain grid.

#### 2.1.3 Items (17) — default order, feature gate, title, icon, behaviour
Default grid order = enum declaration order. Each item is gated by its Features-hub feature: if the feature is not installed the item disappears from the grid, hidden tray and edit mode (`orderedItems` filters by `feature.isAvailable`). Saved orders only store ids; unavailable ones are skipped on read.

| # | id (raw) | Feature gate | Title (enUS) | Icon (SF -> suggested Segoe) | "Active" state | On click (`run`) |
|---|---|---|---|---|---|---|
| 1 | `keepAwake` | keepAwake | "Keep awake" | `bolt` / `bolt.fill` when active -> U+E945 | keepAwake.isActive | toggles Keep awake (stays open) |
| 2 | `cleaner` | cleaner | "Cleaner" | `sparkle` -> U+E734 (verify) | – | hosts Cleaner utility view |
| 3 | `toggles` | quickToggles | "Quick toggles" | `togglepower` -> U+E7E8 | – | hosts Quick toggles list (2.2.4) |
| 4 | `micMute` | micMute | "Mute microphone" / "Unmute microphone" when muted | `mic` / `mic.slash.fill` -> U+E720 / U+F781 (verify) | micMute.isMuted (icon red) | toggles mic mute (stays open) |
| 5 | `screenOCR` | screenOCR | "Copy text from screen" | `text.viewfinder` -> U+E8D2/U+F7A3 (verify) | – | hide panel, after 0.15 s `ScreenTextService.capture()` |
| 6 | `colorPicker` | colorPicker | "Color picker" | `eyedropper` -> U+EF3C | – | hide, after 0.15 s `ColorSamplerService.pick()` |
| 7 | `clipboard` | clipboardHistory | "Clipboard" | `doc.on.clipboard` -> U+F0E3 / U+E8C8 (verify) | – | hide, after **0.10 s** open Clipboard history window |
| 8 | `windowLayout` | windowLayout | "Window layout" | `rectangle.3.group` -> U+F0E2 / U+E8A9 (verify) | – | hosts Window layout utility view |
| 9 | `cleaning` | cleaningMode | "Cleaning Mode" | `keyboard` -> U+E765 | – | hide, after **0.10 s** `CleaningModeManager.activate()` |
| 10 | `homebrew` | homebrew | "Homebrew" (Windows: the package-manager tile; title decided by the Homebrew/winget part) | `shippingbox` -> U+E7B8 (verify) | – | hosts the utility view |
| 11 | `media` | mediaTools | "Media" | `photo.on.rectangle.angled` -> U+EB9F (verify) | – | hosts Media utility view |
| 12 | `urlCleaner` | urlCleaner | "Clean URL" | `link` -> U+E71B | – | hosts Clean URL view |
| 13 | `uninstaller` | uninstaller | "Uninstaller" | `trash` -> U+E74D | – | hosts Uninstaller view |
| 14 | `screenshot` | screenshot | "Screenshot" | `camera.viewfinder` -> U+E722 / U+F7ED (verify) | – | hide, after 0.15 s `ScreenshotService.capture()` |
| 15 | `screenRecorder` | screenRecorder | "Screen recording" ("Stop recording" while recording) | `record.circle` / `stop.circle` -> U+E7C8 / U+E71A (verify) | recorder.isRecording | hide, after 0.15 s `ScreenRecorderService.toggle()` |
| 16 | `cameraPreview` | cameraPreview | "Camera preview" | `web.camera` -> U+E714 | – | if island embeds the camera: embed; else hide + after 0.15 s `CameraPreviewService.show()` |
| 17 | `scratchpad` | scratchpad | "Scratchpad" | `note.text` -> U+E70B / U+F0E5 (verify) | – | hide, after 0.15 s `ScratchpadService.show()` |

`run` is a no-op while editing or when the feature is unavailable. Utilities hosted inside the panel (set `activeUtility`): `windowLayout, homebrew, media, urlCleaner, uninstaller, cleaner, toggles` (the hosted views are other parts' content; only `toggles` is in this part).

Items with inline quick options (edit-mode gear): `keepAwake`, `micMute`, `colorPicker`, `clipboard`.

#### 2.1.4 Selection and keyboard
State: `selectedIndex` (0-based into `visibleItems`, nil when empty), `keyboardIndex` (set by keyboard moves; cleared by hover; the island rail scrolls to it).
- On presentation (`prepareForPresentation`): refresh availability, new presentation id (clears hover/drag state), `isEditing=false`, `editingOptionsItem=nil`, `selectedIndex = 0` (nil if no visible items), `keyboardIndex = selectedIndex`. A utility already open keeps its state if the island is reopened.
- Arrows move one cell, **no wrap, clamped to existing items** (`QuickToolsSupport.gridIndex`): left = i-1 (blocked when `i % columns == 0`), right = i+1 (blocked when `i % columns == columns-1`), up = i-columns, down = i+columns; any candidate outside `[0,count)` leaves the index unchanged. For a column-flowing rail (island) the axes are transposed (`up->left, down->right, left->up, right->down`, with `columns = rows`).
- Enter / numpad Enter = run selected. Digits 1-9 (also numpad) = run item at index 0-8 (if it exists).
- Keys are ignored (passed on) when Cmd/Ctrl/Option (Windows: Ctrl/Alt/Win) is held, when editing, or when a utility is hosted. While a text-input method is composing in a hosted utility's field, Esc belongs to the IME.
- **Esc cascade**: utility open -> close utility (back to grid); else options card open -> close card; else editing -> leave edit mode; else hide panel.
- `clampSelection()` after any visibility change.
- Hover selects the hovered tile when not editing.

#### 2.1.5 Presentation, positioning, dismissal
- `toggle()` (hotkey): if the island routes the quick panel -> let the island open/toggle its Tools tab and return; otherwise visible ? hide : show. `show()` also first tries the island.
- Show: ensure panel, `prepareForPresentation`, position, install monitors, fade in alpha 0 -> 1 over **0.13 s**, bring front and make key.
- **Position** (Spotlight-like): on the screen that contains the mouse pointer (visible frame = work area), centred horizontally; vertical: in AppKit bottom-origin coordinates `y = visible.minY + (visible.height - panelHeight) * 0.62`, i.e. the panel's **bottom** sits at 62% of the free height, equivalently the panel's top edge is 38% of the free height below the work-area top. Clamp so there is at least 16 px from every edge (`x` in `[minX+16, maxX-w-16]`, `y >= minY+16`).
- **Re-fit** (`refreshPanelLayout`) when the content height changes (grid <-> utility, edit toggled, options card opened/closed, rows inside the options card appear/disappear): after one main-loop turn, measure the fitting size, keep the **top edge and horizontal centre** fixed, resize (animated), clamp 16 from the work-area edges.
- Window flags: level floating; `canJoinAllSpaces + fullScreenAuxiliary + ignoresCycle`; `hidesOnDeactivate=false`; not movable by background (so item drag works).
- **Dismissal policy** (`dismissesOnOutsideInteraction = activeUtility == nil && !isEditing`): the bare grid is a transient HUD and hides on (a) a mouse-down anywhere outside the panel (local or global monitor; the panel frame is inflated 2 px), (b) another app becoming active (except the app itself and the OS accessibility keyboard), (c) Esc per cascade, (d) the close button, (e) running a tile that hides it. With a hosted utility or in edit mode the panel is a **working window**: it must survive its own file dialogs, admin prompts and clicks in other apps (e.g. dragging a file into Media). `refocusAfterModal()` re-makes the panel key after a modal dialog.
- `hide()`: collapse the island tools if shown there, remove monitors, `isEditing=false`, close card, `activeUtility=nil`, order out.
- `suspend()` (feature uninstalled/app quitting) = unregister hotkey + hide.

#### 2.1.6 Inline options card (edit mode, shown below the grid)
Card: padding 10, RoundedRect r12 fill primary 5% (black in island), content font 11 pt, switch toggles `.small`. Header row: `Label(title(for:item), icon)` 11 pt semibold secondary, spacer, close x (`xmark.circle.fill` 12 pt secondary, a11y "Close"). The card closes when its tile is hidden, when edit mode ends, or by Esc.

| Item | Controls (in order) | Binding |
|---|---|---|
| `keepAwake` | Toggle "Keep going with the lid closed" (disabled while `clamshellSetupInProgress`, because enabling can run an admin setup step); Picker "Default duration": 15 minutes (15), 30 minutes (30), 1 hour (60), 2 hours (120), 4 hours (240), 8 hours (480), Indefinite (0) | `keepAwake.clamshellPreferred`; `defaultDuration` (int minutes, default 0) |
| `micMute` | Toggle "Show in the menu bar while Mic Mute is on" -> `[WIN]` "...system tray..." | `micMuteMenuBarIndicator` (default **true**) |
| `clipboard` | Toggle "Save clipboard history" (on change -> `ClipboardHistoryService.syncWithPreferences()`); when on: Picker "Limit" with values 20, 50, 100, 250, 500, 1000, 10000 and "Unlimited" (0) (`Defaults.allowedClipboardHistoryLimits = [20,50,100,250,500,1000,10000,0]`); on change sanitise (`value in allowed ? value : 50`) and `trimToLimit()` | `clipboardHistoryEnabled` (default false), `clipboardHistoryLimit` (default 50) |
| `colorPicker` | Segmented picker (no label) with the four formats "HEX", "RGB", "HSL", "SwiftUI"; when HEX selected also Toggle "Copy without the # prefix" | `colorPickerFormat` ("hex"\|"rgb"\|"hsl"\|"swiftui", default "hex"), `colorPickerBareHex` (default false) |

#### 2.1.7 Settings (Quick panel section of the "Quick panel" settings page)
| Key | Type | Default | Meaning |
|---|---|---|---|
| `quickLauncherShortcutEnabled` | bool | **true** | registers the global hotkey |
| `quickLauncherShortcut` | shortcut string | ⌃⌘V (Windows: Ctrl+Alt+V) | hotkey combination |
| `quickLauncherItemOrder` | string (CSV of raw ids) | "" (= default order) | tile order, see 2.0.3 |
| `quickLauncherHiddenItems` | string (CSV of raw ids) | "" | hidden tiles |

Settings-page section (header text `launcherName` "Quick panel"; shown only if the feature is installed):
1. Button `Label("Open quick panel", square.grid.2x2)` -> `QuickLauncherService.show()`.
2. Caption `launcherCaption` (caption, secondary).
3. Caption `launcherEditHint` (caption, tertiary).
4. Toggle "Global shortcut" (`quickLauncherShortcutEnabled`; on change `syncWithPreferences()`).
5. Shortcut recorder row (`ShortcutPreferenceRow(role: .quickLauncher, isEnabled: toggle)`) - the recorder control is defined in the shortcuts part; while it listens, **all** quick-tool hotkeys are unregistered (`QuickToolHotkey.unregisterAll()`), and each owner re-registers on its next `sync`.
6. If enabled and registration failed: orange caption `shortcutUnavailable`.

`syncWithPreferences()`: `enabled = feature installed && shortcutEnabled`; `hotkey.sync(enabled, shortcut = saved ?? default, storageKey)`; `shortcutRegistrationFailed = !result`.

#### 2.1.8 Windows mapping
- Window: WPF `Window` (WindowStyle=None, AllowsTransparency=True, ShowInTaskbar=False, Topmost=True, ShowActivated=True, ResizeMode=NoResize, SizeToContent=Height, Width=420). Add `WS_EX_TOOLWINDOW` (not in Alt-Tab). Blur: `SetWindowCompositionAttribute` ACCENT_ENABLE_ACRYLICBLURBEHIND (or DWM system backdrop `DWMWA_SYSTEMBACKDROP_TYPE=3` transient) plus a rounded clip: `SetWindowRgn` with radius 22 px x DPI scale, or a clipped `Border CornerRadius=22` over the blur. Plate = semi-transparent black 55% / white 50% following the Windows app theme.
- Hotkey: `IHotkeyService.Register`; on press `Dispatcher.BeginInvoke(Toggle)`.
- Dismissal: `Window.Deactivated` when `dismissesOnOutsideInteraction` is true (covers outside click and app switch); for the working-window cases ignore `Deactivated`. Use a low-level mouse hook only if `Deactivated` proves unreliable for transient focus loss from dialogs.
- Position by `Screens.FromPoint(cursor).WorkArea` with the 38%-from-top formula and 16 px clamps; DPI-aware (per-monitor v2) conversion to DIPs.
- Fade: window `Opacity` animation 0 -> 1 over 130 ms.
- The tile catalogue is a hard-coded `QuickLauncherItem` enum with a `FeatureId` per item; hide/availability follows the installed set in `FeatureRegistry`, **not** `IFeature.Actions` (those are a generic action list, not the Vorssaint launcher).
- Footer shortcut string and the tray-panel chip read the saved hotkey text.
- Island variant: expose the grid/rail as a reusable `QuickLauncherView` with an `Embedded` size parameter so the Island part can host it.

#### 2.1.9 Gap vs Trayloom
Current: `src/Trayloom.App/QuickPanelWindow.xaml(.cs)` is a flyout opened from the tray (`App.xaml.cs: ToggleQuickPanel`, `TrayController.QuickPanelRequested`) that lists **every** `FeatureAction` of every installed feature as an 88 px-wide wrap tile (icon glyph, title, feature name), plus a "Features..." button. It anchors near the tray/cursor (`Screens.CursorPosition()`), closes on `Deactivated`, Esc, 300 ms tick guard for the tray toggle, rounded DWM corners, no fade. `FeaturesWindow.xaml(.cs)` is the hub (list + Install/Uninstall + settings).
Checklist (priority order):
1. **P0** Replace the generic action flyout with the real launcher: 420 px panel, header (logo, close, tune/edit), 3-column grid of the 17 fixed items with exact titles/icons/behaviours (2.1.3), footer.
2. **P0** Global hotkey (`quickLauncherShortcutEnabled` default true, Ctrl+Alt+V) – currently only reachable from the tray. Settings section with "Open quick panel", captions, toggle, recorder.
3. **P0** Position centred on the pointer's screen at 38% from top (not at the tray); fade 130 ms; floating over fullscreen where possible.
4. **P1** Keyboard: arrows (no wrap), Enter, digits 1-9 with number badges, Esc cascade; hover selection; selection highlight styles.
5. **P1** Edit mode: hide/show tiles (minus badge, "ADD BACK" tray), drag reorder, persistence of `quickLauncherItemOrder` / `quickLauncherHiddenItems`, empty state.
6. **P1** Inline options cards (Keep awake, Mic mute, Clipboard, Colour picker) and the "active" indicators (green dot, red muted mic, accent tint).
7. **P1** Hosted utilities (Quick toggles, Window layout, Cleaner, URL cleaner, Uninstaller, Media, package manager) with back/close, 470 px scroll area, re-fit, working-window dismissal rules.
8. **P2** Island-embedded variant; "Features..." button is not part of the Vorssaint design (remove or keep as extra, flagged).
9. **P2** Feature availability filter (items vanish when their feature is uninstalled) based on the registry.

---

### 2.2 Quick toggles

Hub identity: title `pageTitle` = "Quick toggles"; hub description `hubDescription` = "One-click actions like dark mode and Trash" -> `[WIN]` "One-click actions like dark mode and the Recycle Bin"; symbol `togglepower` (U+E7E8); group Tools; installed by default; macOS permission = Finder automation (Empty Trash only) -> **none on Windows**. Panel caption (`panelCaption`, caption under the Settings section): "One-click system actions in the menu bar panel and in the quick panel." -> `[WIN]` "One-click system actions in the tray panel and in the quick panel."

The list exists in three places that share one component and one order/visibility store: (1) the tray/menu-bar **panel tab** "Quick toggles" (collapsible section with edit mode), (2) hosted inside the **Quick panel** (2.1, header "Quick toggles" + close), (3) the Command Bar and Radial Menu call the same service methods (2.2.6). Nothing runs, observes or polls while the list is closed (state is read on demand).

#### 2.2.1 Items — 11 rows, default panel order (= `QuickToggleAction` case order)
There are exactly these 11. (Night Shift, Caffeinate, True Tone, Stage Manager, Do Not Disturb, Clean desktop, Hide Dock, file extensions etc. are **not** Vorssaint quick toggles; see 2.2.7.) Raw id = visibility/order id; each row's feature gate is `quickToggles` except `micMute` (gate `micMute`) and `quitAllApps` (gate `quitApps`).

| # | id | Visibility key (default true) | Title (enUS) | Caption (tooltip unless it stays visible) | SF icon -> Segoe | Row kind |
|---|---|---|---|---|---|---|
| 1 | `darkMode` | `panelToggleDarkMode` | dark: "Switch to light mode"; light: "Switch to dark mode" | "Changes the appearance of the whole system." | dark: `sun.max.fill` U+E706; light: `moon.fill` U+E708 | action |
| 2 | `keyboardLight` | `panelToggleKeyboardLight` | "Keyboard light" (from Brightness strings) | "Turns the keyboard backlight on or off." | `keyboard` U+E765 | **switch row**; only listed when a keyboard light exists |
| 3 | `micMute` | `panelUtilityMicMute` (sic) | "Mute microphone" / "Unmute microphone" | "Uses each microphone’s mute or input-volume controls. Siri and some apps may still hear you. This is not a privacy lock." | `mic` / `mic.slash.fill` U+E720 / U+F781 | action, shortcut hint |
| 4 | `emptyTrash` | `panelToggleEmptyTrash` | "Empty the Trash" `[WIN]` "Empty the Recycle Bin" | "Removes everything from the Trash." `[WIN]` "...Recycle Bin." | `trash` U+E74D | action with confirm |
| 5 | `quitAllApps` | `panelToggleQuitAllApps` | "Quit all apps" | `quitAppsCaption` = "Lists your open apps to quit one by one, all at once, or all but the one in front. Always shows the list before anything quits." | `xmark.app` U+E8BB | action, opens Quit apps window, shortcut hint |
| 6 | `ejectDisks` | `panelToggleEjectDisks` | "Eject all disks" | "Safely ejects every external disk." (dynamic captions below) | `eject.fill` U+E88E/U+F847 (verify) | action |
| 7 | `hiddenFiles` | `panelToggleHiddenFiles` | shown? "Hide hidden files" : "Show hidden files" | "The Finder restarts to apply it." `[WIN]` "File Explorer refreshes to apply it." (always visible) | shown? `eye.slash` U+ED1A : `eye` U+E7B3 | action |
| 8 | `desktopIcons` | `panelToggleDesktopIcons` | shown? "Hide desktop icons" : "Show desktop icons" | same Finder caption `[WIN]` as above (always visible) | `desktopcomputer` (constant) U+E7F4 | action |
| 9 | `lockScreen` | `panelToggleLockScreen` | "Lock the screen" | "Asks for the password to come back." | `lock.fill` U+E72E | action |
| 10 | `displayOff` | `panelToggleDisplayOff` | "Turn off the display" | "The Mac keeps running with the display off." `[WIN]` "The PC keeps running with the display off." | `display` U+E7F4 | action |
| 11 | `screenSaver` | `panelToggleScreenSaver` | "Start the screen saver" | "Starts right away, on every display." | `sparkles.tv` U+E7F4/U+F0E3 (verify) | action |

Order key: `panelToggleOrder` (CSV of the raw ids above, "" = default order). Reset action of the panel section (`resetPanelDefaults`): delete the order key and set all 11 visibility keys to `true`.

Other strings (all from `QuickToggleFeatureStrings.enUS`, verbatim):
| Key | English |
|---|---|
| `pageTitle` | Quick toggles |
| `quitOthersTitle` | Quit all apps except the front one |
| `emptyTrashConfirmTitle` | Empty the Trash? `[WIN]` Empty the Recycle Bin? |
| `emptyTrashConfirmMessage` | All items in the Trash will be removed. This cannot be undone. `[WIN]` ...in the Recycle Bin... |
| `emptyTrashConfirmButton` | Empty the Trash `[WIN]` Empty the Recycle Bin |
| `exceptionsTitle` | Kept apps |
| `actionFailed` | Could not complete. |
| `ejectCaption` | Safely ejects every external disk. |
| (disk strings) | `diskEjecting` "Ejecting…", `diskEjectFailed` "Could not eject", `diskNoExternal` "No external disk ready to eject." |
| (Excluded drives list) | see 2.2.5 |
Quit-app strings are in 2.3.

#### 2.2.2 Row layout (shared `UtilityActionButton` / `PanelToggleRow`)
All rows live in one card (`PanelRowGroup`): hairline dividers between rows inset to align with the titles (separator inset = horizontal inset + 22 icon width + 9 spacing; + 16 + 9 more when drag handles are shown).
- Metrics: row insets horizontal 10 / vertical 8 (island: 12 / 10); icon column width 22, icon-to-text spacing 9, drag-handle width 16.
- Action row: HStack: icon (15 pt semibold, accent colour; **orange** when the row needs attention; secondary when hidden-in-editor) 22 wide; VStack(spacing 1): title (12 pt semibold, up to 2 lines, primary; secondary when hidden-in-editor) and caption (10 pt, secondary; orange when attention) **only when** `needsAttention || captionStaysVisible || isEditing`, otherwise the caption is the tooltip + accessibility help; spacer; trailing: shortcut hint chip (9 pt semibold rounded, tertiary, horizontal 5 / vertical 2, r5 fill primary 6%) when the feature's global shortcut is enabled, then chevron `chevron.right` (9 pt semibold, tertiary; Segoe U+E76C). Whole row is a plain button with hover highlight (`PanelRowHighlight`: r7 fill primary 6% inset 3; island: white 8%, r14, inset 4).
- Switch row (`keyboardLight`): same left side, trailing = `Toggle` (switch, small); caption only in edit mode or when attention.
- **Edit mode** (panel section edit toggle / "tune" button): each row shows a drag handle (`line.3.horizontal`, 11 pt semibold tertiary, 16x22) before the icon, captions become visible, trailing = hide/show button (`eye.slash.fill` when visible, `eye.fill` accent when hidden; 24x22, r7, fill 10%, tooltip/a11y "Hide from panel"/"Show in panel") and a "Hidden" badge (`eye.slash.fill` + "Hidden", 9.5 pt bold, secondary, capsule 8%) when the item is hidden. Hidden items are listed (dimmed: title and caption secondary, icon secondary) only in edit mode; otherwise filtered out. Rows are draggable only in edit mode (`previewsAsCard`: the drag preview is a card min width 220).
- While an action is `running` the row is disabled.
- Filtering: `items = orderedItems.filter { feature.isAvailable && (keyboardLight → keyboard light detected) && (editing || visible) }`.
- Panel section chrome (menu-panel tab): `PanelSection(.toggles, title "Quick toggles", collapsible, supportsEditing: true, reset action)`; while this tab is visible it asks the popover to stay open (`viewKeepsPopoverOpen = true`) because actions may activate other apps (Explorer refresh, confirmation dialog) and would otherwise dismiss the panel; the status icon and tab switches still close it. Windows: do not close the tray flyout on `Deactivated` while this tab is shown.
- Hosted in the Quick panel: `VStack(spacing 10)`: header row = `Label("Quick toggles", togglepower)` 12 pt semibold + spacer + close button (`xmark.circle.fill` 14 pt, secondary, 22x22, tooltip "Cancel") -> closes the hosted utility; below it the same list with `editing=false`; the dismiss callback hides the Quick panel (`QuickLauncherService.hide()`).
- On appear of the list: `refreshPermissionStates()` (macOS only) and `brightness.refreshKeyboardLight()`.

#### 2.2.3 Run states, feedback, concurrency
`RunState`: `running`, `failed`, `needsPermission` (macOS Automation only). Per action:
- `beginRun` refuses a second click while `running`.
- Success clears the state immediately (the row itself — new title/icon — is the feedback; no flash).
- Failure shows caption "Could not complete." for **2.4 s** then clears (`captionStaysVisible` while failed). `needsPermission` caption = "Permission required: Finder automation" + a bordered mini button "Open System Settings…" (orange icon). **Not applicable on Windows.**
- Eject row captions: running "Ejecting…"; failed "Could not eject"; otherwise `volumeCount > 0 ? "Safely ejects every external disk." : "No external disk ready to eject."`. The caption stays visible whenever it differs from the plain `ejectCaption`.
- Lock, Turn off display, Screen saver: first dismiss the host surface (close the Quick panel / tray flyout), then run the action **0.15 s** later so focus changes do not fight the open panel.
- Empty trash: shows its own confirmation (2.2.4 row 4) over everything; the panel stays put behind it.

#### 2.2.4 macOS mechanism vs Windows equivalent, per row
| Row | macOS mechanism (exact) | Windows equivalent (exact) |
|---|---|---|
| 1 Dark mode | Reads `SLSGetAppearanceThemeLegacy()` / flips `SLSSetAppearanceThemeLegacy(!dark)` from the private SkyLight framework, resolved lazily with `dlopen/dlsym`; instant, system-wide, no consent; if the symbol is missing the row shows "Could not complete." | Registry `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`: write **both** `AppsUseLightTheme` and `SystemUsesLightTheme` (DWORD; `0` = dark, `1` = light). Then `SendMessageTimeoutW(HWND_BROADCAST, WM_SETTINGCHANGE, 0, "ImmersiveColorSet", SMTO_ABORTIFHUNG, 100-200 ms)`. Read state = `AppsUseLightTheme == 0`. Title/icon of the row follow the **live** system scheme (listen to `SystemEvents.UserPreferenceChanged` or `RegNotifyChangeKeyValue`) and the app re-themes itself (`ThemeManager`). Possible brief taskbar flicker is normal. |
| 2 Keyboard light | via BrightnessService `keyboardLightBridge` (private KeyboardBrightnessClient). Switch on = restore last non-zero level (`BrightnessSupport.keyboardLightOnLevel(lastNonzero)`), off = 0 (remembers the last non-zero level); level 0...1; `keyboardLightEnabled` is nil when the Mac has no controllable keyboard light => row hidden. A slider (0-100 %) lives in Settings. | **Largely impossible**: Windows has no standard API for the Fn-key backlight (it is firmware/OEM-driver level). Closest: Dynamic Lighting `Windows.Devices.Lights.LampArray` (Win 11 23H2+) – enumerate with `DeviceInformation.FindAllAsync(LampArray.GetDeviceSelector())`, pick `LampArrayKind.Keyboard`, set `IsEnabled` / `BrightnessLevel` (verify member names on the target SDK). If none is found, behave exactly like a Mac without a keyboard light: **do not list the row** (and omit the Settings switch + slider). Vendor WMI (ASUS/Lenovo/HP) is out of scope. |
| 3 Mic mute | `MicMuteService.shared.toggle()`; row title toggles with `isMuted`; shortcut hint from `micMute` shortcut when enabled; checks `AppFeature.micMute` | Same call into the Mic mute feature (spec elsewhere: WASAPI `IAudioEndpointVolume.SetMute` on every active capture endpoint, via NAudio `MMDevice.AudioEndpointVolume.Mute`). This row only forwards. |
| 4 Empty trash | `NSAlert` (warning style): messageText "Empty the Trash?", informativeText "All items in the Trash will be removed. This cannot be undone.", first button "Empty the Trash" (default), second "Cancel". On confirm: AppleScript `tell application "Finder" to empty trash` on a work queue; checks Automation consent first (denied -> `needsPermission`); errors -1743/-1744 also map to `needsPermission`; any other failure -> `failed`. `emptyTrashConfirmed()` skips the alert for surfaces that already asked (Command Bar). | Always show the confirmation first (a TaskDialog / WPF dialog with warning icon, same title/message/buttons `[WIN]`-substituted, default button = Empty). On confirm: `SHEmptyRecycleBinW(IntPtr.Zero, null, SHERB_NOCONFIRMATION \| SHERB_NOPROGRESSUI \| SHERB_NOSOUND)` on a background thread; `S_OK` = success, `E_UNEXPECTED` (0x8000FFFF, bin already empty) = success, anything else = `failed`. No permission concept. (Trayloom may additionally use `SHQueryRecycleBinW` to show count/size in the dialog – allowed enhancement, not in Vorssaint.) |
| 5 Quit all apps | Opens the Quit apps window (2.3); never quits anything directly | same |
| 6 Eject all disks | Enumerates `mountedVolumeURLs(skipHiddenVolumes)` fresh on each click, keeps volumes passing `shouldOfferEject`, then `NSWorkspace.unmountAndEjectDevice(at:)` for each on a work queue. A drive carrying several volumes leaves whole on the first eject, so later volumes fail with "not mounted": a failure counts only if the volume is **still mounted** (`isMounted`, a list that cannot be read counts as mounted). Result: no failures -> clear; else `failed`. | See 2.2.5. |
| 7 Hidden files | `CFPreferencesSetAppValue("AppleShowAllFiles", !current, "com.apple.finder")` + sync, then restart Finder: if Finder Automation is granted, AppleScript `tell application "Finder" to quit`, poll `pgrep -x Finder` up to 30 x 100 ms, relaunch `/System/Library/CoreServices/Finder.app` without activating; else `killall Finder`. State read = `AppleShowAllFiles` (bool/number/"YES"/"TRUE"/"1" tolerated; default false). | `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` value `Hidden` (DWORD: `1` = show hidden files, `2` = don't show). State read = `Hidden == 1` (default shown = false). Do **not** restart explorer.exe; apply live: `SendMessageTimeoutW(HWND_BROADCAST, WM_SETTINGCHANGE, 0, "ShellState"…)`, then refresh every open Explorer window (`Shell.Application.Windows()` -> `Refresh()`, or `PostMessage(hwnd, WM_KEYDOWN, VK_F5)`/`WM_COMMAND 0x7103` to each `SHELLDLL_DefView`), then `SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, 0, 0)` for the desktop. (Optional: leave `ShowSuperHidden` untouched.) |
| 8 Desktop icons | `CFPreferences` `CreateDesktop` (Finder domain; read default **true**; write `!shown`), then the same Finder restart. | Truth = visibility of the desktop `SysListView32` (parent `SHELLDLL_DefView` under `Progman`, or under a `WorkerW` sibling when a slideshow wallpaper is active); mirror = `...\Explorer\Advanced\HideIcons` (DWORD, `1` = hidden, `0`/absent = shown). To toggle: if the current visibility != desired, `SendMessageW(defView, WM_COMMAND, 0x7402, 0)` (the shell's own "Show desktop icons" command, which also updates `HideIcons`). If the DefView cannot be found -> `failed`. No Explorer restart. Row title uses the live state. |
| 9 Lock screen | Calls `SACLockScreenImmediate()` from the private login framework (same as ⌃⌘Q); if the symbol is missing, falls back to starting `ScreenSaverEngine.app` (locks when a password is required). | `user32!LockWorkStation()` (returns 0 on failure -> `failed`). |
| 10 Turn off display | `/usr/bin/pmset displaysleepnow` on a work queue; exit status 0 = ok else `failed`. | `SendMessage(hwndOfOwnWindow, WM_SYSCOMMAND, SC_MONITORPOWER, 2)` (2 = off; use a hidden top-level window of the app, or `HWND_BROADCAST` via `PostMessage`). Wait the 0.15 s dismiss delay **plus** ~300 ms after the click so the click's own mouse-up does not instantly wake the display; Windows wakes on any input. Failure to send -> `failed`. Works with Modern Standby displays; no admin needed. |
| 11 Screen saver | `NSWorkspace.openApplication(ScreenSaverEngine.app)`. | `SendMessage(GetDesktopWindow(), WM_SYSCOMMAND, SC_SCREENSAVE, 0)` starts the user's configured saver on all monitors. If no saver is configured (`HKCU\Control Panel\Desktop\SCRNSAVE.EXE` empty or `SPI_GETSCREENSAVEACTIVE` false) fall back to launching `%WINDIR%\System32\Blank.scr /s`; if that is missing -> `failed`. |

Windows state reads happen on demand (when the list appears and after each action; re-read after ~300 ms with an `ApplicationIdle` dispatch so the title flips once Explorer applied the change).

#### 2.2.5 Eject all disks and the "Excluded drives" list
**Which volumes are offered** (`QuickTogglesSupport.shouldOfferEject`): `isLocal && !isRootFileSystem && (!isInternal || isRemovable || isEjectable)`; then drop volumes matched by the exclusion list. A volume that states no internal flag at all (a mounted disk image) counts as *not internal* (so it is offered); only a stated internal bus counts as internal. The volume the OS booted from is never offered, even if its flags are missing (falls back to mount point "/").
Exclusion test (`isExcluded`): an entry matches if it equals (case-insensitive, trimmed) the volume name, the volume UUID, the full mount path, or the last component of the mount path.

**Windows classification** for each logical drive letter (`GetLogicalDrives`/`DriveInfo`):
- `isLocal` = `GetDriveTypeW` in {DRIVE_REMOVABLE(2), DRIVE_FIXED(3), DRIVE_CDROM(5)}; network (4) and RAM disks are not local.
- `isRoot` = the system drive (`%SystemDrive%`, also the drive holding the page file / boot volume).
- `isRemovable`/`isEjectable` = `DRIVE_REMOVABLE`/`DRIVE_CDROM` or `STORAGE_DEVICE_DESCRIPTOR.RemovableMedia` (via `IOCTL_STORAGE_QUERY_PROPERTY`/`StorageDeviceProperty` on `\\.\E:`).
- `isInternal` = bus type is an internal bus (SATA 0x0B, ATA 0x03, NVMe 0x11, SCSI 0x01, SAS 0x0A, RAID 0x08, SPACES 0x10) **and** not USB (0x07) / 1394 (0x04) / SD (0x0C) / MMC (0x0D) / virtual file-backed (0x0F, mounted ISO/VHD – treat as "not internal" like a mounted disk image).
- Name = volume label (`GetVolumeInformationW`), UUID = volume GUID (`GetVolumeNameForVolumeMountPointW`, `\\?\Volume{GUID}\`) and/or serial number, mount path = `E:\`, last component = `E:` (match entries `E:`, `E:\`, `E`, label, GUID).
**Eject**: for optical/ISO use `DeviceIoControl(IOCTL_STORAGE_EJECT_MEDIA)` after `FSCTL_LOCK_VOLUME` + `FSCTL_DISMOUNT_VOLUME`; for removable/USB disks use the PnP safe-removal path: map the drive to its disk devnode (`IOCTL_STORAGE_GET_DEVICE_NUMBER` -> SetupAPI `GUID_DEVINTERFACE_DISK`), take the parent USB storage devnode via `CM_Get_Parent`, call `CM_Request_Device_EjectW(devInst, &vetoType, vetoName…)`; a veto = failure. (Fallback: shell verb `Shell.Application.NameSpace(17).ParseName("E:\\").InvokeVerb("Eject")` then poll `GetLogicalDrives`.) Apply the multi-volume rule: after a failed eject only count it as a failure if the drive letter is still present.
Enumeration is fresh on every click; nothing is cached.

**"Excluded drives" disclosure** (`DiskExclusionsList`, in the Quick toggles settings section; also feeds the Disk-monitor "Eject all"):
- Key `diskEjectExcludedVolumes`: `[String]`, default `[]`; sanitiser trims, drops empties, case-insensitive de-dupe (keeps first spelling).
- Collapsible row (disclosure; whole row toggles, label left "Excluded drives", right: count in secondary when > 0; initially expanded iff the list is non-empty).
- Rows: `externaldrive` 11 pt secondary (16 wide) + name (12 pt, 1 line) + spacer + `minus.circle.fill` remove button (secondary, a11y "Remove").
- Add control: if currently mounted candidate drives exist (offerable per `shouldOfferEject` ignoring exclusions, not already excluded by name/UUID/path, de-duplicated, sorted): a menu button `Label("Add drive…", plus)` listing them, a divider, then "Other drive name…"; if none, a plain `Label("Add drive…", plus)` button. "Other drive name…" (or the plain button) reveals an inline row: text field placeholder "Drive or volume name" (Return submits), bordered small button "Add" (disabled when trimmed text empty), plain small "Cancel" (clears the draft, hides the field).
- Footer caption: "Drives in this list are never unmounted when using Eject all disks." (caption, secondary, wraps).

#### 2.2.6 Service API (must exist so Command Bar and Radial Menu can reuse it)
`QuickTogglesService`: `toggleDarkMode()`, `emptyTrash()` (with confirmation) and `emptyTrashConfirmed()` (no confirmation), `showQuitApps(scope)`, `quitAllApps(keepingPID)`, `quitApp(pid)`, `toggleHiddenFiles()`, `toggleDesktopIcons()`, `ejectAllDisks()`, `lockScreen()`, `turnDisplayOff()`, `startScreenSaver()`, `hiddenFilesShown`, `desktopIconsShown`, `systemAppearanceIsDark`, `ejectableVolumeCount()`, `state(for:)`, `syncWithPreferences()`. Radial menu quick toggle ids: `darkMode, emptyTrash, quitAllApps, ejectDisks, hiddenFiles, desktopIcons, lockScreen, displayOff, screenSaver` (no keyboardLight/micMute).

#### 2.2.7 Settings section "Quick toggles" (inside the Quick panel settings page)
Rendered only when `quickToggles` is installed. Header text "Quick toggles". Rows:
1. Button `Label(<dark-mode title>, sun.max.fill | moon.fill)` -> `toggleDarkMode()` (title/icon follow the live scheme).
2. If a keyboard light exists: Toggle `Label("Keyboard light", keyboard)` (switch, on = level > 0) and below it an HStack(spacing 8): Slider 0...1 bound to the keyboard level (drag begin/end bracket the "last non-zero level" memory; a drag to 0 restores the pre-drag level for the switch) + percent text `NN%` (caption monospaced digits, secondary, 34 wide, right aligned). Hidden entirely when no keyboard light.
3. `DiskExclusionsList` (2.2.5).
4. Caption `panelCaption` (secondary).
Refresh the keyboard-light read on appear.

**Not in Vorssaint (do not build for the replica):** Night Shift/Night light, Caffeinate (that is the separate *Keep awake* feature), True Tone, Stage Manager, Do Not Disturb/Focus, Clean desktop, Hide Dock (taskbar auto-hide), file extensions toggle. If the product owner later wants Windows-only extras they must be separate, hidden-by-default rows with their own visibility keys; mechanisms for reference: file extensions `...\Explorer\Advanced\HideFileExt` (0 show / 1 hide) + same refresh as hidden files; taskbar auto-hide `SHAppBarMessage(ABM_SETSTATE, ABS_AUTOHIDE)` on `Shell_TrayWnd`; Night light = undocumented `HKCU\...\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate` binary blob (fragile, not recommended); Focus assist/Do Not Disturb = `HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\NOC_GLOBAL_SETTING_TOASTS_ENABLED` (Windows 11) or opening `ms-settings:quietmomentshome`; Caffeinate = `SetThreadExecutionState(ES_CONTINUOUS|ES_DISPLAY_REQUIRED|ES_SYSTEM_REQUIRED)`; True Tone and Stage Manager have **no** Windows equivalent (impossible).

#### 2.2.8 Gap vs Trayloom
Current `src/Trayloom.Features.Tools/QuickToggles/QuickTogglesFeature.cs` (id `quick-toggles`, 235 lines): exposes 6 `FeatureAction`s ("Dark mode: on (switch to light)" style titles, desktop icons, hidden files, **file extensions** [not in Vorssaint], Empty Recycle Bin, Lock screen) and a settings view of switch cards + two buttons. Dark mode already writes both registry values + `ImmersiveColorSet` broadcast; hidden files uses `Hidden` 1/2 + refresh; desktop icons uses `WM_COMMAND 0x7402`; Recycle Bin uses `SHEmptyRecycleBinW` with a custom `MessageBox`; lock = `LockWorkStation`.
Checklist:
1. **P0** Missing rows: Keyboard light (conditional), Mic mute row, Quit all apps (opens window), Eject all disks, Turn off the display, Start the screen saver (2.2.4).
2. **P0** Row titles/captions/icons must be the Vorssaint strings (e.g. "Switch to light mode"), not "Dark mode: on (switch to light)"; icon per 2.2.1; visible-caption rules; failure caption "Could not complete." for 2.4 s; running state.
3. **P0** Real list UI (card with dividers, hover highlight, chevrons, shortcut hints) in the tray-panel tab and hosted in the Quick panel; edit mode (hide/show, drag reorder) with `panelToggle*` keys + `panelToggleOrder`; section reset.
4. **P1** Empty-trash confirmation: exact title/message/button strings `[WIN]`; always ask first; success/`E_UNEXPECTED` handling (current code also toasts "already empty" – acceptable extra).
5. **P1** Eject disks + Excluded drives list + `diskEjectExcludedVolumes`.
6. **P1** Lock/Display-off/Screen-saver call after 150 ms dismiss; display-off input-wake delay.
7. **P2** Remove or hide the file-extensions toggle (not in replica); remove the Settings switch cards in favour of 2.2.7 layout.
8. **P2** Service API for Command Bar/Radial Menu (2.2.6).

---

### 2.3 Quit apps (Quit apps window)

Hub identity: title `quitAppsName` = "Quit apps"; hub description = `quitAppsCaption` ("Lists your open apps to quit one by one, all at once, or all but the one in front. Always shows the list before anything quits."); symbol `xmark.app` (U+E8BB); group Tools; **not** installed by default (`installedByDefault = false`, opt-in via the hub); permission none (each app receives the ordinary quit request). The Quick-toggles row `quitAllApps` is gated by this feature (row disappears when the feature is not installed). Command Bar has two actions (`action.quitAll`, `action.quitOthers`) that call `showQuitApps(scope:)`.

#### 2.3.1 Strings
`quitAppsHeader` "Open apps"; `quitAppsQuitAll` "Quit all"; `quitAppsQuitAllExcept` "Quit all except %@"; `quitAppsQuitOne` "Quit %@" (tooltip/a11y of the row's x); `quitAppsFrontBadge` "Front"; `quitAppsKeptBadge` "Kept"; `quitAppsEmpty` "No other apps are open."; `exceptionsTitle` "Kept apps"; `exceptionsCaption` "Quit all leaves these apps running. Finder, Vorssaint and Phone are always kept. You can still quit a kept app from its own row." -> `[WIN]` "Quit all leaves these apps running. File Explorer, Trayloom and Phone Link are always kept. You can still quit a kept app from its own row."; `quitAllTitle` "Quit all apps"; `quitOthersTitle` "Quit all apps except the front one"; Cancel = "Cancel". Settings role titles: "Quit all apps" and "Quit all apps except the front one" (shortcut roles `quitAll`, `quitAllExceptFront`).

#### 2.3.2 Window layout
Non-activating-style borderless panel, transparent, shadowed, level **above the status bar +2** (stays above an open island), joins all desktops, not in Alt-Tab, takes keyboard focus without pulling the front app backward.
- Width **460**, corner radius **22**, `HUDBackdrop` high-contrast (same blur+plate as the Quick panel), clipped.
- Position: centred horizontally on the pointer's screen; vertically `origin.y = visible.midY - h/2 + visible.height * 0.12` in bottom-origin coordinates, i.e. centre 12% of the work-area height **above** the screen centre. Fade in 0.12 s. Height = fitting size; when rows change, keep the **top edge** fixed (`origin.y += oldH - newH`).
- Top to bottom:
  1. Header: HStack(spacing 6): "Open apps" (13 pt semibold) + count of rows (13 pt medium rounded, tertiary, monospaced digits); padding horizontal 17, top 14, bottom 8.
  2. Empty: text "No other apps are open." (12.5 pt, secondary, centred, min height 56) **or** a vertical scroll list: row height **42**, up to **8** rows visible (height = 42 x min(count, 8)), horizontal padding 8; scroll follows the focus (`scrollTo`).
  3. Divider (top padding 6).
  4. Button bar: HStack(spacing 8), padding horizontal 16 / vertical 12: `Cancel` + key label "Esc" (left); spacer; optional `Quit all except <FrontName>` + key label `⌥⌘↩` (Windows `Ctrl+Alt+Enter`); optional `Quit all` + key label `⌘↩` (Windows `Ctrl+Enter`). Each button = bordered `Text(title)` + `Text(keys)` (11 pt medium rounded, 60% opacity, hidden from a11y), single line; the **focused** button is prominent (accent fill), the rest bordered.
- Row (`QuitAppsRowView`): HStack(spacing 10): app icon 28x28; name 13 pt medium, 1 line tail-truncated; badge "Front" if frontmost; badge "Kept" if in the exceptions (its tooltip = `exceptionsCaption`); spacer(min 12); trailing = spinner (small) while quitting, else a plain button `xmark.circle.fill` (15 pt; secondary, primary on hover) shown (opacity 1, hit-testable) **only on the focused row**, tooltip/a11y "Quit <name>" (layout is reserved on every row so nothing shifts). Row padding horizontal 9 / vertical 7, shape r10; focused fill accent 14%. Hover focuses the row; tap focuses it. Badge style: 10 pt medium, secondary, horizontal 6 / vertical 2, capsule fill primary 8%.
- Rows are sorted: frontmost first, then name `localizedCaseInsensitiveCompare` ascending.

#### 2.3.3 Behaviour
- Entry points all open this window (nothing quits in bulk without the list): Quick-toggle row, Settings button, global shortcuts, Command Bar. `scope` (`all` | `allExceptFront`) only decides which batch button holds the initial focus. If the window is already open: update scope/front pid, reload rows, reset focus to the resting button, bring to front.
- **Targeting rules** (`QuickTogglesSupport`):
  - Listed (`listsApp`): has a bundle id, is not the Finder (`com.apple.finder`), not Trayloom (own ids `com.vorssaint.utils`, `com.vorssaint.utils.dev` -> own app ids), not terminated, activation policy = regular (a normal GUI app with a Dock/taskbar presence). Exceptions are **listed** (marked Kept) so they can be quit by hand.
  - Batch-quit targets (`shouldQuitApp`): regular, alive, has an id, not Trayloom, **not in the exceptions set**; `keepingPID` removes one more process (batch "except front").
  - Kept apps always start with the locked pair Finder + Phone (`Defaults.mandatoryAutoQuitExceptionBundleIDs`); Trayloom itself is never stored. `sanitizedQuitAllExceptions` = mandatory + stored, de-duped (trim/ordered), minus own ids.
  - `exceptFrontName` = name of the frontmost row if it is **not** kept; "Quit all except X" is offered only when that name exists and there is at least one batch target besides the front app. A kept front app would make "except" equal to Quit all, so no button.
  - `buttons` (on-screen order): `[cancel] + [allExceptFront if offered] + [all if any non-kept target exists]`.
  - Resting button (where the highlight starts): `all` when opened with `.all`; `allExceptFront` when opened with `.allExceptFront` and offered; when opened as except-front but the front app is kept, falls to `all` (same apps kept); when there is no listed front app it rests on **Cancel** (Quit all could quit the app the user meant to keep).
- **Quitting is soft only**: `terminate()` never `forceTerminate()`, so unsaved-document sheets still appear. Single app (`quitApp`): allowed even for exceptions (picking the row is explicit) but never for Trayloom; returns whether the request was delivered. After a single quit the row shows a spinner for **4 s** (`quitSettleDelay`); an app still running afterwards gets its x back. Batch: closes the window first (next run-loop turn) then runs `quitAllApps(keepingPID:)` which terminates every target on the main thread.
- Live updates: the list reloads on app launch/terminate notifications (and the focus follows `quitAppsFocusAfterListChange`: a vanished row hands focus to the row now at its former index, clamped; no rows -> resting button; a vanished button -> resting).
- **Focus model**: exactly one highlight, either a row (by pid) or a button. Moves (`movedQuitAppsFocus`): from a row — up = previous row (clamped at the top), down = next row or the resting button when at the last row, left/right = unchanged, Tab/Shift-Tab = resting button; from a button — up = last row (or unchanged if no rows), down = unchanged, left/right = neighbouring button (clamped), Tab/Shift-Tab = cycle through buttons with wrap.
- **Keys** (every key reaching the window is consumed; none beep): Esc = close; Return/Enter (ignore auto-repeat) = Cmd+Option -> Quit all except (if offered), Cmd -> Quit all (if offered), none -> activate the focus (row = quit it, Cancel = close, batch button = batch); Cmd+Delete (ignore repeat) on a focused row = quit that row; arrows/Tab as above; any other printable character without Cmd/Ctrl = **type-to-jump**: appended to a prefix that resets after a pause of > **1 s**; focuses the first row whose name starts with the prefix (case- and diacritic-insensitive, anchored). Windows: Ctrl replaces Cmd, Alt replaces Option.
- Dismissal: mouse-down in another window or any other app (local or global monitor, except the on-screen/assistive keyboard) closes the window; Esc closes.
- Frontmost app (`frontmostAppPID`): the foreground app if it is not Trayloom, else the app that was active before the panel took focus (`panelActivationSourcePID`). An unrelated visible window is not a substitute.

#### 2.3.4 Settings section "Quit apps"
Shown when installed. Header "Quit apps". Rows: (1) Button `Label("Quit all apps", xmark.app)` -> `showQuitApps()`. (2) `QuitAllExceptionsList` (below). (3) Toggle "Global shortcut: Quit all apps" (`quitAppsShortcutEnabled`, default false; text is `quickToolShortcutToggle + ": " + quitAllTitle`) + recorder row (`quitAppsShortcut`, default ⌃⌥⌘Q) + orange `shortcutUnavailable` when enabled and failed. (4) Toggle "Global shortcut: Quit all apps except the front one" (`quitAppsExceptFrontShortcutEnabled`, default false) + recorder (`quitAppsExceptFrontShortcut`, default ⌃⌥⌘O) + failure caption. (5) Caption `quitAppsCaption`.
`syncWithPreferences()`: register each hotkey only when the feature is installed **and** its toggle is on (ids 63 = quit all, 62 = except front, via `QuickToolHotkey`); uninstalling unregisters both and closes the window; also reloads exceptions.

**Kept apps list** (`QuitAllExceptionsList`): `DisclosureGroup` (initially expanded only if the stored list contains an app beyond the mandatory pair), label "Kept apps" + count; rows: app icon 18x18 + name (1 line) + spacer + `lock.fill` (tertiary) for mandatory rows or `minus.circle.fill` remove (a11y "Remove") otherwise; rows hidden when the app is not installed; sorted by display name; small button `Label("Add app…", plus)` opens the installed-apps picker sheet (excludes apps already kept); footer caption `exceptionsCaption`. Key `quitAppsKeptApps`: `[String]`, default = mandatory pair (Finder, Phone); read path re-sanitises and writes back if changed. Windows identifiers: see below.

#### 2.3.5 Windows mapping
- **App identity** (macOS bundle id) -> a stable string per app: AppUserModelID / package family name for packaged apps, else the lower-cased full exe path. Mandatory pair: **File Explorer** (`%WINDIR%\explorer.exe`; it owns the desktop/taskbar so it is never listed and never quit — the Finder equivalent; File-Explorer folder windows are therefore not quit either) and **Phone Link** (`Microsoft.YourPhone_8wekyb3d8bbwe` / `PhoneExperienceHost.exe`; row hidden in Settings until installed, mirroring Phone.app).
- **"Regular app"** (Dock-visible) = a process owning at least one top-level window that: is visible (`IsWindowVisible`), has no owner (`GetWindow(GW_OWNER)==0`), is not `WS_EX_TOOLWINDOW` unless `WS_EX_APPWINDOW`, is not cloaked (`DwmGetWindowAttribute(DWMWA_CLOAKED)==0`; excludes hidden UWP/virtual-desktop ghosts), has a non-empty title or is a known app. UWP frames hosted by `ApplicationFrameHost.exe` must be resolved to the real app (child `Windows.UI.Core.CoreWindow`, `GetApplicationUserModelId`). Tray-only apps without a window are "accessory" and are left alone (same as macOS).
- Name = `FileVersionInfo.FileDescription` (fallback ProductName, then exe name without extension) or package DisplayName; icon = shell icon (`SHGetFileInfo`/`IShellItemImageFactory` 28 px, or `ExtractAssociatedIcon`).
- **Soft quit** = `PostMessage(hwnd, WM_CLOSE, 0, 0)` to **every** top-level window of the process (`Process.CloseMainWindow` only handles the main one); never `TerminateProcess`. Apps with unsaved work show their own prompt. "Delivered" = at least one `PostMessage` succeeded; elevated processes reject it (UIPI) -> treat as not delivered (row gets its x back immediately).
- Front app: capture `GetForegroundWindow()`'s process **before** showing the panel (the hotkey handler does this and stores it, because showing the panel activates Trayloom).
- Observation: poll `EnumWindows` on a 1 s timer only while the window is open (plus `SetWinEventHook(EVENT_OBJECT_CREATE/DESTROY, WINEVENT_OUTOFCONTEXT)` for faster reaction); nothing runs when closed.
- Hotkeys via `IHotkeyService` (ids not needed). Window = WPF as in 2.1.8 (WS_EX_TOOLWINDOW, topmost, DWM blur, rounded clip 22).

#### 2.3.6 Gap vs Trayloom
Nothing exists: no Quit apps window, no kept-apps list, no shortcuts, no feature in the hub, and no `quitApps` quick toggle. P1: build `QuitAppsPanel` + model + settings section + exceptions list + two hotkeys; P1: process/window enumeration helper in `Trayloom.Core` (shared with the Auto Quit / Window switcher parts).

---

### 2.4 Color picker

Hub identity: title `colorPickerName` = "Color picker"; hub description `descColorPicker` = "Pick any color on screen"; symbol `eyedropper` (U+EF3C, verify); group Tools; installed by default; no permission on Windows (macOS: Screen Recording, with a permission-free `NSColorSampler` fallback).

#### 2.4.1 Strings
| Key | English |
|---|---|
| `colorPickerName` | Color picker |
| `colorPickerCaption` | Grab the color of any pixel on screen and copy it in your favorite format. |
| `colorPickerPickNow` | Pick color |
| `colorPickerFormatLabel` | Copied format |
| `colorPickerBareHexToggle` | Copy without the # prefix |
| Format labels (not localized) | HEX, RGB, HSL, SwiftUI |
| Role title (shortcut) | Color picker |
| Capture-menu toggle | Show capture menu when using keyboard shortcut |
| Capture-surface tool title | Color picker (tool 4 in the chooser) |
| Failure HUD (`captureFailed`) | The screen could not be captured |

#### 2.4.2 Settings
Lives on the "Screen capture" page (tool picker "Screen capture": Screenshot, Screen recording, Copy text from screen, Color picker) — selecting "Color picker" shows this section (header "Color picker"). The page's first section ("Screen capture") shows for the selected tool: Toggle "<role title>" (`colorPickerShortcutEnabled`, default **false**), shortcut recorder row (`colorPickerShortcut`, default ⌃⌥⌘C -> Ctrl+Alt+Shift+C), Toggle "Show capture menu when using keyboard shortcut" (`colorPickerShowCaptureMenuOnShortcut`, default true, disabled while the shortcut is off), orange `shortcutUnavailable` when enabled and registration failed.
Section "Color picker":
1. Button `Label("Pick color", eyedropper)` -> `ColorSamplerService.pick()`.
2. Caption `colorPickerCaption`.
3. Segmented Picker "Copied format" (labels HEX | RGB | HSL | SwiftUI) bound to `colorPickerFormat` (default `"hex"`; sanitiser: unknown -> hex).
4. Only when HEX is selected: Toggle "Copy without the # prefix" -> `colorPickerBareHex` (default false).
Magnifier options (shared with Screenshot; shown on the Screenshot section inside a "magnifier" disclosure but they govern the colour loupe too): `screenshotLoupeStartsOn` (bool, false; note: colour mode always starts with the loupe on), `screenshotLoupeRememberZoom` (bool, false), `screenshotLoupeDefaultZoom` (double, 1.0; picker values 0.5×, 1×, 2×, 4× shown with `.number` 0...1 fraction digits + "×"; hidden when remember-zoom is on), `screenshotLoupeLastZoom` (double, 1.0, written on every zoom change and at session start), `screenshotLoupeSteppedZoomByDefault` (bool, false; segmented "Fast"/"Step by step"; caption "Hold ⌥ to temporarily use the other mode." -> `[WIN]` "Hold Alt..."). Labels: "Start selection with the magnifier on", "Remember the magnifier’s last zoom", "Default magnifier zoom", "Wheel zoom".

#### 2.4.3 Behaviour
- Entry points: Quick-panel tile (hide panel, 0.15 s, `pick()`), Settings button, global shortcut (`ScreenCaptureService.capture(initial: .color, fromShortcut: true)`; the chooser menu is shown only if `colorPickerShowCaptureMenuOnShortcut`), Command Bar, tray panel.
- `pick()` opens the **unified capture chooser** (`ScreenCaptureService`) with the Color tool selected. The chooser shows a palette of the available tools (Screenshot 1, Screen recording 2, Copy text from screen 3, Color picker 4 - digit keys 1-4 switch tool, even with the menu hidden) and each tool's hint; tools whose feature is not installed are omitted. Chooser details for the other tools belong to the Screenshot part; the Color/Text-specific parts are below. Only one capture session may be on screen at a time (a second request is turned away). No Screen-Recording permission exists on Windows, so the permission-free fallback (`pickNative`) is **not needed** on Windows: always use the in-app frozen overlay.
- In Color mode (`policy.freeze = true` for every tool except Screenshot): the screen is frozen (a snapshot of every monitor is taken before the overlay appears and the app's own windows are hidden from it unless the "hide app windows" preference says otherwise), crosshair cursor, a black dim overlay of **22%** alpha over the frozen image (18% for live/unfrozen sessions; the sampled pixels always come from the undimmed snapshot), and the **loupe is on from the start** (`loupeEnabled = isPickingColor`). No region drag exists in colour mode.
- Pick: mouse-**up** anywhere (a drag only moves the loupe; release picks) on the display under the pointer, or **Return/Enter** at the current pointer position. Escape (also a global key monitor) or the chooser's esc hint cancels. Right-click: not special-cased.
- **C** (loupe visible, no drag in flight) copies the colour under the pointer in the configured format **without ending the session** (palette picking from one frozen screen); the loupe's info bar then shows `✓ <value>` for **1.4 s**. **Z** toggles the loupe (on again re-captures a fresh frame in live mode). Arrow keys nudge the pointer by exactly **1 device pixel** (**10** with Shift) while the loupe is up (`captureLoupeNudge`: step = (fast ? 10 : 1) / displayScale points); the pointer may cross to a neighbouring display; at a screen edge with no neighbour it stops. **Mouse wheel** zooms the loupe: stepped mode vs fast mode chosen by `screenshotLoupeSteppedZoomByDefault` XOR Option held (Alt on Windows).
- Pixel read (`sampledColor`): crop the 1x1 pixel at `(x, y) = floor(view point mapped to image pixels)` clamped to the image; take its components in the bitmap's own colour space and convert to **sRGB** (never read as generic RGB). Windows: GDI/DXGI captures are 8-bit sRGB (SDR); with HDR on, capture the SDR composition (GDI `BitBlt` from the screen DC) so values stay sRGB. If the sample cannot be read the session finishes `.failed` -> HUD "The screen could not be captured" (camera icon).
- Result (`receiveUnifiedColor`): copy the formatted string to the clipboard, then show the HUD (2.7.1) with icon `eyedropper`, the value as message and a **swatch** (18x18, r5, 0.5 px primary 20% border) instead of the icon. The clipboard write is "signed" with the app's source marker so Clipboard history attributes it to Trayloom; Clipboard history keeps every picked colour automatically (no separate colour history exists in Vorssaint).
- While the surface is up the HUD would be invisible (shielding-level panels), so C-copy uses the loupe's own `✓` confirmation; the final pick uses the HUD after the surface closes.

#### 2.4.4 Output formats (`ColorValue.string(red:green:blue:alpha:format:bareHex:)`)
Input: sRGB components 0...1, clamped. Integers use round-half-away (`rounded()`); alpha text uses 3 decimals via `%g` of `round(a*1000)/1000` with POSIX decimal point.
| Format | Opaque | With alpha (`alpha != nil`) |
|---|---|---|
| `hex` | `#RRGGBB` uppercase (bare: `RRGGBB`) | `#RRGGBBAA` (bare: `RRGGBBAA`); AA = round(a*255) as 2 hex digits |
| `rgb` | `rgb(R, G, B)` ints 0-255 | `rgba(R, G, B, A)` |
| `hsl` | `hsl(H, S%, L%)` ints (H 0-360 rounded, S/L percent rounded) | `hsla(H, S%, L%, A)` |
| `swiftui` | `Color(red: 0.000, green: 0.000, blue: 0.000)` 3 decimals, POSIX locale (period, never comma) | `Color(red: …, green: …, blue: …, opacity: 0.000)` |
The picker itself copies opaque values (no alpha). `bareHex` only affects `hex`.
HSL conversion (`hsl(red:green:blue:)`): max/min/delta; `lightness = (max+min)/2`; if `delta <= 1e-6` -> `(0, 0, lightness)`; `saturation = delta / (1 - |2*lightness - 1|)` clamped 0...1; hue: if max==red `((g-b)/delta) mod 6`; max==green `(b-r)/delta + 2`; else `(r-g)/delta + 4`; ×60; add 360 if negative.

#### 2.4.5 Colour parser (`ColorValue`, used by Clipboard history swatches and Command Bar colour answers)
`init?(text:)` — accepts only when the **whole** (trimmed, lower-cased, ≤ **96 UTF-8 bytes**) text is a colour; bare `RRGGBB` is **not** accepted (would match numbers/hashes):
- `#RGB`, `#RGBA`, `#RRGGBB`, `#RRGGBBAA` (3/4/6/8 hex digits; short forms double each digit).
- `rgb(...)`/`rgba(...)`: args split on commas, `/`, or whitespace; 3-4 args; each channel is `N%` (0-100) or a number 0-255; alpha absent = 1, else a number 0-1 or `N%`.
- `hsl(...)`/`hsla(...)`: hue number or `<n>deg` (mod 360, normalised), saturation and lightness as `N%` (0-100), optional alpha like rgb. Conversion: `h=hue/360`, `chroma=(1-|2L-1|)*S`, `channel(k0)`: `k=(k0 + h*12) mod 12; L - chroma/2 * max(-1, min(k-3, 9-k, 1))` with offsets 0 (R), 8 (G), 4 (B).
- `Color(red: x, green: x, blue: x[, opacity: x])` (SwiftUI form): exactly the labels in that order, values 0-1, 3-4 comma-separated pairs, spaces around labels allowed.
`ColorSwatch` (UI): rounded square `size` (default 14), corner radius `size * 0.25`, fill = the colour with its alpha, hairline border primary 22% 0.5 px, hidden from accessibility (the value text beside it already says the colour).

#### 2.4.6 Loupe geometry and drawing (colour mode and screenshot selection share it)
Constants (`ScreenshotSupport`): base sample side **13** px; min sample side **3**; zoom range **0.5 ... 13/3 (≈4.333)**; default-zoom choices {0.5, 1, 2, 4}; frame side **132** pt; info-bar height 24; info gap 6; corner radii: frame 9, bar 7.
- Sample side = `13 / zoom`, then forced **odd** (`2*floor(raw/2)+1`), never below 3 (so a real centre pixel exists).
- Sample rect = square of that side centred on `floor(pixelPoint)` (side clamped to the image, rect clamped inside the image; near an edge the window slides inward instead of shrinking).
- Drawing order inside a rounded-rect clip (r9) of the 132x132 frame: the sampled pixels scaled up with **no interpolation** (nearest); a faint pixel grid (black 28%, 1 px lines between every pair of sampled pixels) **only when** `132 / sampleSide >= 6`; a **reticle**: a rectangle ring around the target pixel inset by 1 px (the ring sits just outside the pixel so the sampled colour stays visible) plus four arms from the frame edges toward the ring (stopping before it), stroked twice: black 76% width 3, then white 92% width 1; then the frame border white 95% width 1.5.
- Info bar below the frame (gap 6, height 24, width = max(content+20, frame width) clamped to the screen with 6 px margins and centred on the frame): rounded r7 fill black 72%; contents centred: swatch 12x12 (white 85% 1 px border) + 7 gap + text (monospaced digits 10.5 pt semibold white): `"<value>   <x>, <y>"` (value in the current copy format, then **three spaces**, then display-pixel coordinates), or `"✓ <value>"` for 1.4 s after C; with an unreadable pixel just `"<x>, <y>"`.
- Placement (`captureLoupeFrame`): block = frame + gap + bar (132 x 162); origin = pointer + (16, 16 below-right in screen terms); if it would cross the right edge (inset 8) flip to the left of the pointer; if it would cross the bottom edge flip above; finally clamp 8 px inside the display.
- Zoom (wheel): fast mode = x1.15 per event (x1.15³ per plain notch when the device is non-continuous), clamped to range; stepped mode = move exactly one odd sample size (±2 px of side) per event, with the widest level resolving to 0.5; delta source priority: fixed-point wheel delta, then line delta, then scrolling delta. Initial zoom = remember-last ? last : default, clamped; zoom is persisted to `screenshotLoupeLastZoom` on every change.
- Guide bar for colour mode (inside the chooser): subtitle `colorPickerCaption`, key hints "1–4" (keyboard), and nothing else (the `↩`, `Z`, `C`, `R` chips are hidden for the colour tool in the unified chooser; they appear in screenshot/text modes: `↩` full-screen, `S`, `Z`/`Z on`, `C`, `R`, each chip 26 pt high, r7 fill primary 6%, 10 pt semibold rounded). Esc hint chip `xmark` + "esc".

#### 2.4.7 Windows mapping
- Capture: `Graphics.CopyFromScreen`/`BitBlt` of the virtual screen (Trayloom `ScreenGrab.CaptureVirtualScreen` already exists) or `Windows.Graphics.Capture` per monitor; keep per-monitor DPI scale for nudging (`GetDpiForMonitor`).
- Overlay: one borderless topmost window per monitor (or one spanning the virtual desktop, as Trayloom's `FrozenScreenWindow` does), crosshair cursor (`Cursors.Cross`), `SetCursorPos` for nudging, `WM_MOUSEWHEEL` for zoom.
- Clipboard: `Clipboard.SetText`; register a custom clipboard format (e.g. `Trayloom.Source`) holding the app id so Clipboard history can attribute the copy.
- Default hotkey disabled until the user turns it on (replica rule), recorder in the Screen capture page.

#### 2.4.8 Gap vs Trayloom
Current `src/Trayloom.Features.Tools/ColorPicker/ColorPickerFeature.cs` + `ColorPickerWindow` in `Support/FrozenScreenWindow.cs`: own frozen window (no chooser), **circular** 150 px loupe (15x15 samples at 10x), centre box, caption "#RRGGBB   rgb(r, g, b)"; arrow-key nudge by 1 physical px, Enter/Space to confirm, click to pick (on mouse-down), Esc cancels; formats HEX/RGB/HSL only (setting key `format` = "HEX"/"RGB"/"HSL"); hotkey bound **always** (Ctrl+Alt+Shift+C); a "Recent colours" history (12 swatches, `history` setting, Clear button) that Vorssaint does **not** have; toast `Copied <text>`.
Checklist:
1. **P0** Settings: add SwiftUI format, "Copy without the # prefix" (`colorPickerBareHex`), key names/values lower-case, labels/captions of 2.4.2; hotkey toggle default off; Pick-color button; remove "Recent colours" (or move to Clipboard history).
2. **P0** Exact output formats 2.4.4 (HSL integer rounding, bare hex, SwiftUI 3 decimals).
3. **P1** Loupe: rounded 132 px frame, variable odd sample side from zoom 0.5-4.33, pixel grid, reticle ring + arms, info bar `"<value>   x, y"`, edge flipping; wheel zoom (fast/stepped), Z toggle, C copy-and-continue with `✓` for 1.4 s, Shift = 10 px nudge, loupe on from start; 22% dim; pick on mouse-up.
4. **P1** HUD confirmation with swatch (replace the toast) and clipboard source marker.
5. **P1** Unified capture chooser (tools 1-4, digit switching, `colorPickerShowCaptureMenuOnShortcut`).
6. **P2** Loupe options in Screenshot settings (`screenshotLoupe*`), `ColorValue` parser + `ColorSwatch` control for Clipboard history/Command Bar.

---

### 2.5 Copy text from screen (Screen OCR)

Hub identity: title `ocrName` = "Copy text from screen"; hub description `descScreenOCR` = "Copy text or QR codes from anything on screen"; symbol `text.viewfinder`; group Tools; installed by default; macOS permission Screen Recording (none on Windows).

#### 2.5.1 Strings
| Key | English |
|---|---|
| `ocrName` | Copy text from screen |
| `ocrCaption` | Select an area of the screen and the recognized text is copied, ready to paste. |
| `ocrCopied` | Text copied |
| `ocrNoText` | No text found |
| `ocrRemoveLineBreaksToggle` | Remove line breaks |
| `ocrRemoveLineBreaksCaption` | Removes line breaks so copied text pastes as one paragraph. |
| `ocrQRToggle` | Read QR codes |
| `ocrQRCaption` | If the area has a QR code, its content is shown to copy or open. |
| `ocrQRCopied` | QR code copied |
| `qrResultTitle` | QR code |
| `qrResultCopy` | Copy |
| `qrResultOpen` | Open link |
| Chooser hint (`subtitle`) | = `ocrCaption` |
| Failure HUD (`captureFailed`) | The screen could not be captured |

#### 2.5.2 Settings (Screen capture page, tool "Copy text from screen")
Top section ("Screen capture"): Toggle "Copy text from screen" (`screenOCRShortcutEnabled`, default false), recorder (`screenOCRShortcut`, default ⌃⌥⌘T -> Ctrl+Alt+Shift+T), Toggle "Show capture menu when using keyboard shortcut" (`screenOCRShowCaptureMenuOnShortcut`, default true, disabled while off), orange `shortcutUnavailable`.
Section "Copy text from screen" (header `ocrName`):
1. Button `Label("Copy text from screen", text.viewfinder)` -> `ScreenTextService.capture()`.
2. Caption `ocrCaption`.
3. Toggle "Remove line breaks" -> `screenOCRRemoveLineBreaks` (bool, **false**) + caption `ocrRemoveLineBreaksCaption`.
4. Toggle "Read QR codes" -> `screenOCRDetectQRCodes` (bool, **true**) + caption `ocrQRCaption`.
5. macOS only: Screen Recording permission row when missing (omit on Windows).

#### 2.5.3 Behaviour
- `capture()` -> unified chooser with tool `text` (tool key 3). Selection mode `.image`: drag a rectangle, **or click a window** to capture it, or Return for the full display under the pointer (screenshot-style chooser; `S`, `R`, `Z`, `C` keys etc. as in the Screenshot part). `receiveUnifiedCapture(capture)` hands the cropped `CGImage` + the QR setting to `recognize`.
- `copyText(from:)` (used by the screenshot preview/editor and Clipboard-history "Copy text") runs the same recognition with QR detection **off** (screenshot previews show QR results separately).
- `recognize` pipeline (`ScreenTextService`):
  1. Guard: feature installed. Bump `recognitionGeneration`; remember the clipboard change counter and the two settings; `fallbackLanguages = recognitionLanguages(for: appLanguage)`.
  2. Off the main thread (`userInitiated`): scale the image down if it exceeds **24,000,000 pixels** (`ClipboardImageRecognition.maxPixels`): `side = longest * sqrt(24e6 / pixels)` (floor, ≥ 1), the other side scaled proportionally; redraw in sRGB with high interpolation; otherwise use the image as is.
  3. `outcome`: if QR detection is on and `BarcodeDetector.read(image)` finds a code -> `.qr(reading)` (**a QR code wins over text**: the scan is a fast pass; if nothing is found it falls through to text). Else text recognition: first pass **accurate** model with automatic language detection and language correction, preferred languages = `fallbackLanguages` filtered by what the engine supports; if it yields **no lines**, a second pass with the **fast** model, no auto-detect, same preferred languages. Lines -> text via `joinedRecognizedText(lines, removingLineBreaks:)`; empty -> `.empty`.
  4. Back on main, **drop the result** unless: this is still the latest generation, the feature is still installed, and the clipboard change counter is unchanged (a later copy by the user is never overwritten by an earlier OCR).
  5. `.qr` -> `QRResultController.show(reading)` (2.6). `.text` -> write the text to the clipboard (signed with the source marker) + HUD icon `text.viewfinder` message "Text copied". `.empty` -> HUD message "No text found".
- **Language handling** (`MediaSupport.recognitionLanguages`): app UI language -> preferred OCR languages: pt-BR `["pt-BR","en-US"]`, tr `["tr-TR","en-US"]`, ru `["ru-RU","en-US"]`, es `["es-ES","en-US"]`, de `["de-DE","en-US"]`, fr `["fr-FR","en-US"]`, it `["it-IT","en-US"]`, ja `["ja-JP","en-US"]`, ko `["ko-KR","en-US"]`, uk `["uk-UA","en-US"]`, zh-Hans `["zh-Hans","en-US"]`, zh-TW and zh-HK `["zh-Hant","en-US"]`, default (English) `["en-US"]`.
- **Reading order / joining** (`QuickToolsSupport.joinedRecognizedText`): drop lines whose trimmed text is empty; sort by row bucket `(1 - y) * 50` (y = normalised vertical *centre* with bottom-left origin, so top-of-screen lines first; rows whose buckets differ by less than **0.5** are the same visual row) ascending, then by `x` (normalised left edge) ascending. Without "Remove line breaks": join with `\n`. With it: trim each line and concatenate; between two lines insert **one space** unless the last scalar of the text so far **and** the first scalar of the next line are both "tight script" (join with nothing): U+3000-312F, U+3190-9FFF, U+F900-FAFF, U+FF01-FF9F, U+20000-2FA1F (CJK punctuation, kana, Han; Hangul syllables deliberately excluded because Korean keeps word spaces).
- `syncWithPreferences()`: if the feature is not installed, cancel any pending recognition (bump generation). `suspend()` same.

#### 2.5.4 Windows mapping — `Windows.Media.Ocr`
- Engines: `OcrEngine.TryCreateFromLanguage(new Language(tag))` for each tag in the app-language preference list (table above, mapped to BCP-47 e.g. `zh-Hans-CN`, `zh-Hant-TW`) **filtered by `OcrEngine.AvailableRecognizerLanguages`**; always also try English `en-US`; if none is available fall back to `OcrEngine.TryCreateFromUserProfileLanguages()`. There is **no** automatic language detection: run recognition with up to 3 engines in order (app language, en-US, remaining user-profile languages) and keep the **first non-empty** result (or the one with the most recognised characters if you want parity with "auto-detect" quality). If no OCR language is installed at all: show HUD/notification "No OCR language pack…" (Trayloom already words it; add to Settings an explanatory note listing installed OCR languages and pointing at Settings > Time & language > Language & region > Add language > Optical character recognition).
- The "accurate then fast" two-pass has no equivalent; replace the second pass with a **retry at 2x upscaling** when the first pass returns nothing (Trayloom already upscales ×3/×2 for images under 60/140 px tall; keep as an enhancement). Clamp both sides to `OcrEngine.MaxImageDimension` (preserve aspect).
- Image path: crop the frozen capture -> PNG -> `BitmapDecoder` -> `SoftwareBitmap` (Bgra8, Premultiplied) -> `engine.RecognizeAsync`.
- Line geometry: `OcrLine` has no rect; compute from its `Words[].BoundingRect` (union): x = min left / imageWidth; y centre = (top+bottom)/2 / imageHeight measured **from the top** -> use bucket `y_top_based * 50` (equivalent to `(1 - y_bottomLeft) * 50`). Apply the same 0.5-bucket + x sort and the same CJK join rule. Note: the Windows engine inserts spaces between CJK characters in `OcrLine.Text`; when the line language is zh/ja, strip spaces between adjacent tight-script characters (and use `OcrWord`s joined without spaces) before joining.
- Clipboard staleness guard: `GetClipboardSequenceNumber()` captured at start and compared before writing.
- No permission needed; capture by GDI/WGC.
- HUD: see 2.7.1.

#### 2.5.5 Gap vs Trayloom
Current `src/Trayloom.Features.Tools/ScreenOcr/ScreenOcrFeature.cs` (130 lines): own `RegionSelectorWindow` (hint "Drag over the text you want to copy - Esc to cancel"), `OcrEngine.TryCreateFromUserProfileLanguages()`, simple line join, `keepLineBreaks` toggle (default true; **inverse** of Vorssaint's `screenOCRRemoveLineBreaks` default false), toast `Copied N characters: <preview>`, hotkey always bound (Ctrl+Alt+Shift+T), settings: hotkey row, toggle "Keep line breaks", note on installed OCR languages; small-image upscaling; tests `_busy` re-entrancy guard.
Checklist:
1. **P0** Settings parity: "Remove line breaks" (+caption), "Read QR codes" (+caption), Pick button, hotkey off by default, capture-menu toggle, captions/labels per 2.5.1-2.5.2; migrate `keepLineBreaks` -> `!screenOCRRemoveLineBreaks`.
2. **P0** HUD feedback "Text copied" / "No text found" instead of toasts with a preview.
3. **P1** QR priority path + QR result panel (2.6) and `screenOCRDetectQRCodes`.
4. **P1** Language handling per 2.5.4 (app-language-first engine list), reading-order sort + CJK join, 24 MP downscale.
5. **P1** Clipboard-sequence staleness guard; clipboard source marker.
6. **P1** Unified capture chooser (also supports clicking a window and Return for the full display) instead of a bare region selector.
7. **P2** `copyText(from:)` entry for Screenshot preview / Clipboard history "Copy text".

---

### 2.6 QR codes and barcodes

Used by Copy text from screen and by the screenshot preview/editor ("Read QR"). Vorssaint supports **matrix (2D) symbologies only** because 1D barcodes false-fire on striped screen areas.

#### 2.6.1 Detector (`BarcodeDetector`)
- Symbologies: QR, Micro QR, Aztec, Data Matrix, PDF417 (`VNDetectBarcodesRequest.symbologies`). 1D codes (EAN/UPC/Code 128…) are intentionally **excluded**.
- `decode(image)` -> list of `(payload, x = box.minX, y = box.midY)` (normalised, bottom-left origin), dropping empty payloads.
- `read(image)` -> `Reading(payload, url)`: payload = codes joined in reading order (same bucket sort as OCR: `(1-y)*50` with 0.5 tolerance, then x) separated by `\n`, empty payloads dropped; nil when nothing found. `url` = the payload as a link **only when exactly one code was found** and it is an openable URL.
- `openableURL(payload)`: trimmed non-empty, **no whitespace**, parseable, scheme (case-insensitive) is exactly `http` or `https`, host non-empty. Everything else (mailto, tel, custom app schemes…) is **never openable** — a scanned code must not launch arbitrary schemes.

#### 2.6.2 Result panel (`QRResultController` / `QRResultView`)
- Shows what the code holds **before anything is copied**. One panel at a time (`show` closes any previous one).
- Panel: borderless, transparent, shadowed, level above the status bar, joins all desktops, not in Alt-Tab, takes keyboard focus. Width **320**, padding 16, corner radius **16**, `.regularMaterial` fill (blur) + 1 px border primary 10%.
- Content VStack(spacing 12): header HStack(spacing 7): `qrcode` glyph (14 pt semibold, accent; Segoe U+F029/"QRCode" verify) + "QR code" (13 pt semibold). Payload box: vertically scrollable text, 12.5 pt, selectable, padding 10, max height **132**, fill primary 5%, r8, border primary 10%. Button row right-aligned (spacer first), spacing 8: if the reading has a URL -> `Copy` (bordered) + `Open link` (prominent, **default button / Enter**); else only `Copy` (prominent, default).
- Position: just **below the pointer**: `x = pointer.x - w/2`, top edge 18 px under the pointer (AppKit: `y = pointer.y - h - 18`), clamped fully inside the work area with 12 px margins; fade-in 0.12 s; made key.
- Dismiss: Esc, or any left/right mouse-down outside the panel (global + local monitors; clicks on the app's own windows reach the local monitor only).
- Copy: write payload to the clipboard (source-marked), close, HUD icon `qrcode` message "QR code copied". Open: close, then open the URL with the default browser.

#### 2.6.3 Windows mapping — **"ask first"**
- **No built-in Windows API decodes barcodes from an image**: `Windows.Media.Ocr` is text-only; `Windows.Devices.PointOfService.BarcodeScanner` is for hardware scanners, not bitmaps; no Windows App SDK/Windows AI API does it either. A third-party decoder is required.
- Recommendation: **ZXing.Net** (NuGet `ZXing.Net`, Apache-2.0, pure managed; use `BarcodeReaderGeneric` with a custom `RGBLuminanceSource` over the BGRA bytes, no System.Drawing dependency). **Not on the allowed NuGet list (NAudio, CommunityToolkit.Mvvm) -> flagged "ask first".** Options if refused: (a) bundle a small port of the QR decoder only, (b) detect QR only via a hand-written decoder (large), (c) ship the feature as OCR-only with the "Read QR codes" setting hidden.
- With ZXing.Net: `new BarcodeReaderGeneric { AutoRotate = true, Options = { TryHarder = true, TryInverted = true, PossibleFormats = { QR_CODE, AZTEC, DATA_MATRIX, PDF_417 }, PureBarcode = false } }` and `DecodeMultiple(luminanceSource)` to get all codes; use `Result.ResultPoints` (average/min x, mid y) to derive the position for the reading-order sort. **Micro QR is not supported by ZXing.Net** (document this gap). Run before OCR on the same downscaled image; it must stay fast (it is "a fast pass that runs before the heavier text recognition").
- Panel: WPF window with DWM blur as in 2.1.8; `Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })` only for the validated http/https URL.

#### 2.6.4 Gap vs Trayloom
No QR/barcode code exists (grep: none). P1: add `BarcodeDetector` (after approval for ZXing.Net), `QuickToolsSupport.openableURL` + ordered join, `QRResultWindow`, the `screenOCRDetectQRCodes` setting; P2: reuse from the screenshot preview/editor.

---

### 2.7 Shared plumbing

#### 2.7.1 `QuickToolHUD` (confirmation toast)
Non-activating, non-interactive, fading confirmation used by: colour picked, text copied, QR copied, mic muted/unmuted, capture failures, OCR from clipboard/editor.
- API: `show(icon: <symbol>, message: String, swatch: Color?)` (any thread; marshals to main).
- Content: HStack(spacing 8): left = the **swatch** (RoundedRect r5, 18x18, fill the colour, border primary 20% 0.5 px) if provided, else the symbol (13 pt semibold, accent colour); then the message (12 pt semibold, **max 2 lines, tail-truncated** — never middle — max width **360**, a short message sizes to itself). Padding horizontal 14 / vertical 9; background `.regularMaterial` in a RoundedRect r10 (continuous).
- Panel: borderless, transparent, shadow on, ignores mouse, never takes focus, level status-bar, joins all desktops, `transient`. Position: **centred horizontally and 24 px below the top** of the visible frame of the screen under the pointer (`x = midX - w/2`, `y = maxY - h - 24` bottom-origin).
- Timing: fade in **0.12 s**; stays **1.5 s**; fade out **0.22 s**. A new `show` cancels the pending dismiss and resets the timer (a `generation` counter prevents a stale fade-out from ordering out a newer toast). The panel window id is excluded from screen recordings/captures while visible.
- `showCountdown(n)`: 82x82 circle (`.regularMaterial`, padding 12, no shadow) with a ring: base ring white 12% 1 px; progress arc accent 3 px round caps trimmed `0.04 ... 0.04 + progress*0.92`, rotated -90°, padded 6, plus the digit (32 pt bold rounded, monospaced); redraws at 30 fps; dismisses after **0.92 s** (used for the screenshot delay).
- `showScrollingCapture(...)`: HUD with controls for scrolling screenshots (message, live "NNN px" height, Cancel (Esc) + Finish (Return, prominent)); belongs to the Screenshot part.
- Windows: a WPF topmost, `WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW` window; fade with `DoubleAnimation`; 24 px below the top of the cursor monitor's work area; blur via DWM acrylic; replaces the toast notifications currently used by Trayloom features (`Ctx.Notifier`) for these confirmations (Notifier stays for errors).

#### 2.7.2 `QuickToolHotkey`
One global hotkey per quick tool. Fixed ids: Quick panel **14**, Quit all **63**, Quit all except front **62**; per-capture-tool hotkeys are created from `ScreenCaptureTool.allCases` starting at id **25** (screenshot 25, recording 26, text 27, color 28; the hand-assigned range ends at 24). Behaviour:
- `sync(enabled, shortcut, storageKey) -> Bool`: not enabled -> unregister, return true; already registered with the same combo -> true; else unregister + register; returns **false when the OS refuses** (combination taken) -> the owner sets `shortcutRegistrationFailed` and settings show the orange caption.
- `onPress` is dispatched asynchronously on the main queue.
- `unregisterAll()` is called while a shortcut recorder listens, so the combination being typed reaches the recorder instead of firing a tool; each owner re-registers on its next `sync`.
- macOS also registers a "system shortcut takeover" (disables a conflicting symbolic hotkey) — **no Windows equivalent**; on Windows a conflict simply fails the registration (`ERROR_HOTKEY_ALREADY_REGISTERED`) -> caption.
- Windows: `IHotkeyService.Register` (RegisterHotKey on a hidden message window); ids are internal.

#### 2.7.3 `WindowActivationPolicy`
macOS-only concept: the app is normally an *accessory* (no Dock icon, absent from Cmd-Tab); while a user-facing window must stay reachable (Settings, Features, editors) `retain()` promotes it to a *regular* app, and the last `release()` demotes it. A `WindowActivationRetention` counter makes it re-entrant (`retain` returns true on 0→1, `release` true on 1→0; `release` with count 0 is a no-op). On Windows the equivalent is **automatic**: tray apps are never in Alt-Tab/taskbar unless a window has `ShowInTaskbar=true`; so use `ShowInTaskbar=True` for Settings/Features/editor windows and `WS_EX_TOOLWINDOW`/`ShowInTaskbar=False` for panels, HUDs and overlays. No code needed beyond that; keep a no-op `WindowActivationPolicy.Retain/Release` only if shared code calls it.

#### 2.7.4 Quick-tool support helpers (port verbatim)
`QuickToolsSupport`: `sampledColor`, `GridDirection`, `GridFlow`, `gridIndex(after:count:columns:direction:)`, hidden-id (de)serialisation, `joinedRecognizedText`, `joinedBarcodePayloads`, `openableURL`, and the paste-plain helpers (`isMatchStyleEquivalent`, `isMediaTextFallback`) that belong to the Paste-plain part.

---

### 2.8 Settings page "Quick panel" (`QuickToolsSettings`) and Features-hub tiles

Sidebar entry: "Quick panel" (`quickToolsTab`, the Tools "quick tools" destination) hosts these sections in this order, each shown only when its feature is installed and each with a `settingsFormSectionAnchor` for deep links from the Features hub: **Quick panel** (2.1.7), **Quick toggles** (2.2.7), **Quit apps** (2.3.4), **Mic mute** (below, details in its own part), **Camera preview**, **Wallpaper**, **Scratchpad**, **Cleaning Mode** (the last four belong to other parts). Form style = grouped sections; captions are `.caption` secondary; warnings orange.

Mic mute section (rows only; feature spec elsewhere): header "Mute microphone"; Button `Label(micMute.isMuted ? "Unmute microphone" : "Mute microphone", mic.slash.fill | mic)`; when muted an orange caption row `Label("Mic Mute on", mic.slash.fill)`; caption `micMuteCaption`; Toggle `micMuteMenuBarToggle` (`micMuteMenuBarIndicator`, default **true**) + caption `micMuteMenuBarCaption` ("The red mic in the menu bar indicates that Mic Mute is on. It does not guarantee that all apps stop capturing audio." -> `[WIN]` "...in the system tray..."); Toggle "Global shortcut" (`micMuteShortcutEnabled`, default false) + recorder (`micMuteShortcut`, default ⌃⌥⌘M) + failure caption. Related persistence keys: `micMuteActive`, `micMuteSavedVolume` (0.75), `micMuteSavedVolumes`, `micMuteSavedChannelVolumes`, `micMuteMutedDevices`.

`GeneralToolSettings.swift` (the other file in scope) is **not quick-tools UI**: it is the General/"Panel configuration" destination (menu-bar panel layout editor, menu-bar icon symbol gallery, "icon missing" row), Mixer, Sound-output switcher, Audio priority and Music blocking pages. It is covered by the General/Sound parts of the spec, not here; the only quick-tool tie-in is that the panel-layout editor's "Quick toggles" tab order/visibility is the 2.2 store.

Features-hub tile data (title / description / SF symbol -> suggested Segoe / group / default installed):
| Feature | Title | Description | Symbol | Group | Installed by default |
|---|---|---|---|---|---|
| quickLauncher | Quick panel | A floating panel with your favorite tools | `wand.and.rays` | Tools | yes |
| quickToggles | Quick toggles | One-click actions like dark mode and Trash `[WIN]` ...the Recycle Bin | `togglepower` (U+E7E8) | Tools | yes |
| quitApps | Quit apps | Lists your open apps to quit one by one, all at once, or all but the one in front. Always shows the list before anything quits. | `xmark.app` (U+E8BB) | Tools | **no** |
| colorPicker | Color picker | Pick any color on screen | `eyedropper` (U+EF3C) | Tools | yes |
| screenOCR | Copy text from screen | Copy text or QR codes from anything on screen | `text.viewfinder` | Tools | yes |
| micMute | Mute microphone | Mute the microphone from anywhere | `mic.slash` (U+F781) | **Sound** | yes |
Hub group header text for Tools: "Tools". Settings destinations: quickLauncher/quickToggles/quitApps/micMute -> "Quick panel" page with the matching section anchor; colorPicker/screenOCR -> "Screen capture" page with anchors `.colorPicker` / `.screenOCR` (tool picker preselected).

Quick-panel-row in the tray panel (`panel-utilities.png`, Utilities tab, first row): icon `square.grid.2x2` accent, title "Quick panel", caption `launcherCaption` (wraps to 3 lines), shortcut chip `^⌘V` when enabled, chevron; hover highlight; tapping opens the Quick panel.

#### 2.8.1 Gap vs Trayloom
- `FeaturesWindow.xaml(.cs)` already shows grouped features with Install/Uninstall and per-feature settings views via `IFeature.CreateSettingsView()`. Vorssaint instead merges Quick panel + Quick toggles + Quit apps + Mic mute (+ others) onto **one** "Quick panel" settings page with anchored sections, and Colour picker + Screen OCR (+ Screenshot, Recorder) onto one **"Screen capture"** page with a tool picker. P1: add a hub "settings destination" concept (page + section anchor) so deep links and shared pages work; P1: split Trayloom's per-feature settings views accordingly or make the hub scroll to the anchored section.
- Missing hub entries: `quickLauncher`, `quitApps` (not installed by default).
- Hub descriptions in the current features differ from the Vorssaint text ("Pick any colour on your screen with a magnifier and copy it as HEX, RGB or HSL.", "Draw a box around any text…", "Flip dark mode, desktop icons…"); P2: replace with the hub strings above (American "color").

---

### 2.9 Consolidated implementation order for this part
1. Shared plumbing: `QuickToolHUD` WPF window (2.7.1), hotkey ids/sync semantics (2.7.2), shortcut display + settings storage keys, `ColorValue` formats/parser, clipboard source marker, reading-order/joining helpers (2.7.4).
2. Quick toggles service + 11 rows + settings + eject exclusions (2.2); Quit apps window + service + hotkeys (2.3).
3. Quick panel launcher window + edit mode + options + hosted utilities + hotkey (2.1).
4. Unified capture chooser integration for Colour picker and Copy text from screen (shared with the Screenshot part), colour loupe (2.4.6), OCR pipeline (2.5), QR panel (2.6, after the ZXing.Net decision).
5. Hub/Settings restructuring (2.8).

Open questions for the owner: (a) approve **ZXing.Net** for QR/barcode (2.6.3); (b) Windows hotkey mapping 2.0.1 (Ctrl+Alt / Ctrl+Alt+Shift instead of ⌃⌘ / ⌃⌥⌘); (c) keep a Windows-only "Show file extensions" row? (default recommendation: no, replica rule); (d) the Homebrew tile title on Windows (winget) is owned by another part.


---

## 3. Radial menu

Source of truth: `Services/RadialMenu/{RadialMenuService,RadialMenuSupport,RadialNowPlayingService}.swift`, `UI/RadialMenu/RadialMenuView.swift`, `UI/Settings/RadialMenuSettings.swift`, `UI/Settings/RadialMenuVisualCanvas.swift`, `Core/RadialMenuStrings.swift` (enUS block used verbatim). Notation: `[WIN]` marks a deliberate Windows wording/behaviour substitution; the original macOS text is kept next to it. "pt" are SwiftUI points = WPF DIPs (1 pt = 1 DIP).

What the feature is: a global "wheel of actions". A hotkey, an extra mouse button or a four-finger trackpad tap opens a glass disc (300 pt) around the pointer carrying up to 12 round "chips" (one per action). Point at a slice, release (or click) to run it. Several **profiles** (named wheels, each with its own colour, shortcut, mouse button, trackpad-tap flag and action list) exist; submenus (one level) open a second wheel.

There is **no per-app ring** (profiles are selected only by their own trigger, never by the frontmost app) and **no per-slot size/colour/theme**: the only visual customisation is the profile colour (12 choices). The "visual canvas" in Settings is an editor/preview of the ring (reorder by drag, click to edit), not a theme engine.

---

### 3.1 Features-hub / panel entries

| Item | Value |
|---|---|
| Feature enum | `AppFeature.radialMenu`, group **Tools** (hub section), `installedByDefault = true` |
| Hub title (`hubTitle`) | `Radial menu` (= `pageTitle`) |
| Hub description (`hubDescription`) | `Opens a wheel of your favorite actions around the pointer` |
| Hub/panel SF symbol | `circle.grid.cross` -> Segoe Fluent suggestion: no exact glyph; draw a small custom plus-in-circle-grid geometry; closest font glyph `U+E80A` (GridView) |
| Enable key | `radialMenuEnabled` (bool, default **false**) |
| Availability key | `featureAvailable.radialMenu` (default true) |
| Permission | Accessibility on macOS (only when any wheel uses key/media/window slices, a mouse button or the trackpad tap). `[WIN]` none required (see 3.9) |
| Resource cost class in hub (`FeaturePresets`) | `.mouse` when any profile has a mouse button or trackpad tap, otherwise `.idle` |
| Menu-panel row (`MenuPanelView`) | Toggle row: title `Radial menu`, caption `Your favorite actions on a wheel` (`panelCaption`), icon `circle.grid.cross`, drag handle, per-row visibility key `panelControlRadialMenu` (bool, default true). When on, an accessory button `Manage the menu` (`manageButton`) opens Settings > Radial menu. When enabled and Accessibility is needed but missing: caption becomes "missing permission" text + request button (`[WIN]` never shown) |
| Settings page | `SettingsPage.radialMenu` (sidebar entry "Radial menu") |
| Shortcut-conflict role | `GlobalShortcutRole.radialMenu` titled `Radial menu` (storage key `radialMenuShortcut`, default `GlobalShortcut.radialMenuDefault`); a combination used by any wheel counts as taken by the role "Radial menu" for other shortcut recorders |
| Also reused strings | `mediaNowPlaying` (`Now Playing`) is the label for the music tile/strip of the Dynamic Island (`NotchView`, `NotchCapsuleViews`, `NotchMusicStrip`, `NotchTimerStrip`) and `mediaPlayPause` is its accessibility action; `chooseButton` (`Choose…`) is used by `CommandBarSettings` |

---

### 3.2 Settings keys (UserDefaults) and persistence

#### 3.2.1 Keys

| Key | Type | Default | Allowed / notes |
|---|---|---|---|
| `radialMenuEnabled` | Bool | `false` | master switch; turning it on re-syncs service and (macOS) requests Accessibility if needed |
| `radialMenuAtPointer` | Bool | `true` | `true` = wheel centred on the pointer; `false` = centred on the visible area of the screen under the pointer |
| `radialMenuActivationMode` | String (`RadialMenuActivationMode.rawValue`) | `pressOrHold` | `pressOrHold` \| `press` \| `hold`; unknown value reads as `pressOrHold` |
| `radialMenuProfiles` | Data (UTF-8 JSON array of `RadialMenuProfile`), see 3.2.2 | absent -> migrated/seeded (3.2.4) | max profiles: unbounded (no cap in code) but at least 1 |
| `radialMenuShortcut` | String (`modifiers:keyCode`) | `control+option+command:49` (Space) | **legacy** (pre-profiles); read only to migrate the first profile; still registered as role default |
| `radialMenuMouseButton` | String | `off` | **legacy** single mouse trigger (see 3.2.3); migrated to first profile |
| `radialMenuItems` | Data (JSON array of `RadialMenuItem`) | absent | **legacy** single-wheel item list; migrated; included in settings backup |
| `panelControlRadialMenu` | Bool | `true` | menu-panel row visibility |
| `liquidGlassEnabled` | Bool | `false` | global appearance flag: on macOS 26+ (and Reduce Transparency off) the disc uses the system glass effect instead of `regularMaterial` `[WIN]` map to acrylic/mica or ignore |
| Settings backup | `SettingsBackupSupport` includes `radialMenuItems` and `radialMenuProfiles` in exported settings (the scalar keys travel with the normal key list) |

Windows storage suggestion: scope `radial-menu` in `%APPDATA%\Trayloom\settings.json` (`SettingsStore`), keys `enabled`, `atPointer`, `activationMode`, `profiles` (store the profile array as a JSON array node or a JSON string; same shape as below). The legacy keys need not exist on Windows.

#### 3.2.2 Profile JSON (exact shape; Swift `JSONEncoder` defaults)

```json
[
  {
    "id": "7B1F0C0E-1B3A-4D8C-9E6B-2A6E1C3D4F55",
    "name": "",
    "color": "accent",
    "shortcut": "control+option+command:49",
    "mouseButton": "off",
    "preset": "general",
    "trackpadTap": false,
    "items": [
      { "id": "UUID", "kind": "media", "name": "", "symbolName": "", "payload": "playPause", "children": [] },
      { "id": "UUID", "kind": "file",  "name": "", "symbolName": "", "payload": "~/Downloads", "children": [] },
      { "id": "UUID", "kind": "url",   "name": "Docs", "symbolName": "", "payload": "https://example.com",
        "customIconData": "<base64 PNG, <= 65536 bytes>", "children": [] },
      { "id": "UUID", "kind": "submenu", "name": "More", "symbolName": "ellipsis.circle", "payload": "",
        "children": [ { "id": "UUID", "kind": "tool", "payload": "screenshot", "name": "", "symbolName": "", "children": [] } ] }
    ]
  }
]
```

- `id`: UUID string (uppercase in Swift output; accept any case). Missing -> new UUID.
- `name`: string, trimmed, max 60 chars (`prefix(60)`); empty means "General" (`presetGeneral`) is displayed.
- `color`: one of `accent, blue, purple, pink, red, orange, yellow, green, mint, cyan, indigo, graphite`; unknown/absent -> `accent`.
- `shortcut`: `GlobalShortcut` storage string `<mods joined by +>:<macOS virtual keycode>`, mods tokens `control|option|shift|command`; empty string = no shortcut. Invalid non-empty value is cleared to `""` on save. `[WIN]` store a Windows hotkey string instead (e.g. `Ctrl+Alt+Space`) in the same field; map macOS command->Win/Ctrl per the global shortcut spec.
- `mouseButton`: `off` | `back` (CG button 3) | `forward` (CG button 4) | `button:N` (N = 3...31, other numbers serialize as `button:N`). Anything else -> `off`. A button number may belong to only ONE wheel; on save, later duplicates become `off`.
- `preset`: optional string (`general|media|tools|windowLayout|quickToggles|blank`), the starter set the wheel came from, used by the **Reset** button. Omitted when nil.
- `trackpadTap`: bool, default false. At most one wheel keeps it (Settings enforces; at runtime the first with true wins).
- `items`: lossy array; an item that fails to decode (e.g. unknown `kind`) is dropped, the rest survive. Missing items array -> empty.
- Item fields: `id` (UUID), `kind` (`app|file|url|shortcut|tool|quickToggle|windowLayout|media|submenu`), `name` (custom label, <= 60 chars after trim; empty = automatic), `symbolName` (SF symbol name; empty = automatic icon), `payload` (target, see 3.4), `customIconData` (optional base64 PNG, only produced by "Fetch Website Icon"), `children` (only meaningful for `submenu`; cleared for other kinds).
- Never rename raw values (`playPause`, `screenshot`, `darkMode`, `leftHalf`, ...): they persist in user data.

#### 3.2.3 Sanitising rules (apply on decode and on every save)

`RadialMenuSupport.sanitized(items, depth)`:
1. `maxItemsPerWheel = 12`; extras dropped (in order).
2. `maxDepth = 2`: root (depth 0) and one submenu level (depth 1). A submenu item inside a submenu is dropped; the editor does not offer "Submenu" in a submenu.
3. Duplicate item ids dropped.
4. `name` trimmed + max 60 chars; `payload` trimmed.
5. Items failing `isValidPayload` are dropped: `app`/`file` payload non-empty; `url` payload must `normalizedURL` successfully; `shortcut` must parse as a valid `GlobalShortcut` (needs a primary modifier or be a standalone function key); `tool`/`quickToggle`/`windowLayout`/`media` must name a known raw value; `submenu` always valid.
6. `url` payload is rewritten to its normalised form.
7. `customIconData` dropped when > 65 536 bytes or not a decodable image.
8. Non-submenu items get `children = []`; submenu children are sanitised at depth 1.
9. Profiles: dedupe ids, name <= 60, shortcut trimmed/validated, mouse button sanitised and unique across profiles; an empty result list yields one **General** profile with shortcut `control+option+command:49`.

`normalizedURL(raw)`: trim; reject empty or containing a space; if it has an explicit scheme (`://`, or a leading alphabetic scheme followed by `:` where the part before the colon is letters/digits/`+`/`-`/`.`; a colon followed by digits is a port, not a scheme, when the part before has a `.` or is `localhost` (so `example.com:8080` is a host, `tel:5551234` a scheme)) keep as typed (http/https must have a host; other schemes such as `mailto:` pass); otherwise prefix `https://` and require a host.

#### 3.2.4 Legacy migration (`decodeProfiles`)
If `radialMenuProfiles` is missing/undecodable/empty: build one profile: id new, name `""`, color `accent`, shortcut = `radialMenuShortcut` or default `control+option+command:49`, mouseButton = legacy `radialMenuMouseButton` (default `off`), items = `decode(radialMenuItems)` (**missing blob -> starter items; present blob incl. empty list = the user's own list**), `preset = "general"`.

---

### 3.3 Trigger gestures, activation modes and lifecycle

#### 3.3.1 Triggers (any profile can have all three)
| Trigger | How it opens | Notes |
|---|---|---|
| Keyboard shortcut (per profile) | Carbon hotkey (id `1700 + profileIndex`), default combination Control+Option+Command+Space for the migrated/first wheel and `General`-preset profiles created via `createProfile(shortcut:)`; a new profile added from "+" has **no** shortcut (empty) | Hotkey never auto-repeats; a press during a session closes it (same profile) or closes it and opens the other profile. Registration failure -> `registrationFailed` (orange caption in Settings) |
| Extra mouse button (per profile) | CGEventTap on `otherMouseDown/Up`, head-insert, consumes **both** halves of every click of the claimed button so the app below never sees half a gesture | Only buttons 3...31 (back = 3, forward = 4, rest "Button N+1"). Left/right/middle are never offered |
| Four-finger trackpad tap (per profile, one wheel max) | Detected by the Middle-click service's touch reader (4 fingers); calls `toggleFromTrackpad()` | Always opens a *sticky* session (hold=false); a second tap closes. If Middle click is already set to 4-finger tap the radial tap is disabled and Settings shows the conflict caption |
| "Try it" button in Settings | `presentPreview(for: selectedProfile)`: ends any session, opens a sticky session for the selected profile with the saved placement; works even when the feature is off | |

A wheel without any trigger never opens outside "Try it" (Settings shows `profileNoTrigger`).

#### 3.3.2 Activation modes (`radialMenuActivationMode`, read ONCE at session start)
| Mode | Picker label | Behaviour |
|---|---|---|
| `pressOrHold` (default) | `Press or hold` | Adaptive. Summoner held (shortcut modifiers still down, or mouse button still down) = *hold phase*: release over a slice runs it; release with nothing highlighted leaves the wheel open (sticky). Any arrow key press while in hold phase switches to sticky. |
| `press` | `Press to open` | Never held: the wheel opens sticky; release of the summoner does nothing; closes on selection, second press, Esc, outside click |
| `hold` | `Hold to select` | Wheel exists while summoner down; release runs highlighted slice or dismisses if none. Arrow keys move highlight but do NOT leave hold phase |

Caption under the picker (`activationModeCaption`): `Press or hold keeps the current adaptive gesture. Press stays open; hold runs the highlighted action on release.`

`startsHeld = mode != press && (heldButton != nil || (requestedHold && shortcutHasModifiers))`. `requestedHold` is true for a shortcut press, false for trackpad tap / Try it / `presentPreview`. A shortcut **without modifiers** (standalone F-key) can never be held -> sticky. For hold sessions via shortcut the held state ends when `shortcut.requiredModifiersHeld(in: currentFlags)` becomes false (or the "Super key" virtual modifier is released: the Super Key service (`SuperKeyService.isEngaged && isHeld`) counts as holding when it decorated the summoning key; its `onHoldEnded(released)` callback ends hold via `endHoldPhase` (released=true) or `endSession` (cancelled)). Release resolution `releaseAction(hasSelection)`: selection -> `select`; no selection -> `hold ? dismiss : stayOpen`.

#### 3.3.3 Session lifecycle
1. `beginSession(profile, hold, heldButton)`: items = `availableItems(profile.items)` (3.4.2); if empty -> system beep (`NSSound.beep`), no wheel. Cancels an in-flight closing animation. `visible=false`. If any item (any depth) is Now Playing -> `RadialNowPlayingService.dismissDetails()` + `refresh` (3.7) else state = `nothingPlaying`.
2. `stack = [items]`, `trail = []`, `highlightedIndex = nil`, `pointerActivated = false`, `openPointerLocation = mouse`.
3. Placement: `wanted = atPointer ? mouseLocation : center of screen-under-pointer visibleFrame`; `wheelCenter = clamp(wanted)`: the 400x400 panel must lie fully inside the visible frame (centre kept `>= minX+200`, `<= maxX-200`, same for Y) - only when the frame is larger than 400 in both axes, otherwise unclamped. Panel frame = centre ± 200.
4. Panel shown (alpha 0 -> 1, 0.18 s ease-out; Reduce Motion: instant), made key without activating the app; monitors installed; initial highlight computed; on the next run-loop turn `visible = true` (drives scale-in).
5. `endSession()`: if a close is already running do nothing; remove monitors; if the panel is visible: `visible=false` and fade out (alpha 1 -> 0, 0.13 s ease-in, `ignoresMouseEvents`, key status given back on frame 1 by ordering out/in) then `forgetSession()` (panel ordered out, stack/profile/trail/highlight/hold state cleared; panel released when feature disabled). A new summon during the fade cancels the fade and takes the panel back.
6. `sessionActive = !stack.isEmpty && !dismissal.isActive`.

Session-scoped monitors (installed at begin, removed at end; nothing runs at rest except hotkeys and the optional mouse tap, panel is pre-built hidden so the wheel appears instantly):
- local `keyDown` (only events whose window is the wheel panel), local `flagsChanged`, local+global `mouseMoved/leftMouseDragged/otherMouseDragged`, local+global `leftMouseDown/rightMouseDown/otherMouseDown` (a click outside the panel +/- 2 pt ends the session), `NSWorkspace.didActivateApplicationNotification` for any app other than self -> end session (`[WIN]` foreground-window change).

Session end triggers: selecting a leaf, Esc at root, Esc/hub-click at root (`stepBack`), click outside the wheel disc (distance > 150 from centre), second press of the same shortcut, another profile's shortcut (switches wheel), another app activating, mouse button of a non-hold session pressed again, login session switch (`SessionActivity.onChange(!active)`), feature disabled (`suspend`).

#### 3.3.4 Pointer tracking
- Pointer moves update `highlightedIndex` through `refreshHighlight()`. Until the pointer has travelled **>= 8 pt** from where the wheel opened (`moveActivationDistance`) nothing is highlighted by the pointer (so a keyboard-picked highlight is not erased by tremor, and a centred wheel doesn't fire on the resting pointer direction). Submenu entry re-arms this (pointerActivated=false, new origin).
- A pending close from a missed flagsChanged is settled on the next pointer move (`handleFlagsChanged(current modifiers)` if hold phase and no held button).
- Click on the panel (whole 400x400 is tappable; `activatePointer()` decides by distance from `wheelCenter`): `> 150` (wheelDiameter/2) -> end session; `< 40` (dead zone) -> `stepBack()`; otherwise if a slice is highlighted -> `select(index)`; (slice not yet armed -> nothing).

#### 3.3.5 Keyboard (wheel panel is key window)
| Key | Action |
|---|---|
| `Esc` | `stepBack()`: pop one submenu level (`stack.removeLast`, `trail.removeLast`, highlight cleared, re-evaluated) else end session |
| `Return` / keypad Enter | run highlighted slice (`select`), swallowed even when nothing highlighted |
| `Left` / `Up` | highlight previous (wrap). From no highlight it goes to the **last** slice. In `pressOrHold`/`press` it first leaves the hold phase (sticky) |
| `Right` / `Down` | highlight next (wrap). From no highlight it goes to slice 0 |
| `1`...`9` | run slice 1..9 directly if it exists (digits via `charactersIgnoringModifiers`) |
| anything else | passes through |

#### 3.3.6 Selection / running (`select(index)`)
- Submenu: pushes children on the stack, trail gets the submenu's `name` (raw, may be empty -> hub shows `Submenu`), highlight cleared, **enters sticky phase** (a release over a submenu never leaves the wheel; holds end), pointer re-armed.
- Anything else: `endSession()` first, then `run(item)`.

---

### 3.4 Item kinds (the "actions")

`RadialMenuItem.Kind` picker order and labels (enUS):

| # | Kind raw | Editor picker label | Payload | Runs | Default icon (SF) | Windows equivalent |
|---|---|---|---|---|---|---|
| 1 | `app` | `Open an app` | absolute path (tilde expanded) of an `.app` bundle | `NSWorkspace.openApplication(at:)`; failure -> beep | `app` + real file icon (34 pt) | path to `.exe`/`.lnk`/UWP; run `Process.Start(UseShellExecute=true)`; `shell:AppsFolder\<AUMID>` for packaged apps; picker: open-file dialog filter `Programs (*.exe;*.lnk;*.bat;*.cmd)`, initial dir `%ProgramFiles%`; display name from `FileVersionInfo.FileDescription` or shortcut name; icon via `IShellItemImageFactory` |
| 2 | `file` | `Open a file or folder` | path (tilde expanded; default starter `~/Downloads`) | if path missing -> beep; else `NSWorkspace.open` | `folder` + real file icon | `ShellExecute "open"`; folder picker via `IFileOpenDialog` with `FOS_PICKFOLDERS` *or* files; `~` expands to `%USERPROFILE%`; Downloads = `FOLDERID_Downloads` |
| 3 | `url` | `Open a link` | normalised URL | `NSWorkspace.open(url)` | `link` (or fetched favicon) | `ShellExecute` with the URL (default browser / mailto: handler) |
| 4 | `shortcut` | `Press a shortcut` | `GlobalShortcut` storage string | waits until the summoning chord's modifiers are released (poll every 15 ms, up to 100 tries = 1.5 s, then +60 ms), then posts key down, +40 ms key up with the shortcut's flags | `command` | `SendInput` (KEYEVENTF_KEYUP after 40 ms, modifiers pressed first) after polling `GetAsyncKeyState` for Ctrl/Alt/Shift/Win until clean; tag with the Trayloom `dwExtraInfo` so own hooks ignore it |
| 5 | `tool` | `Vorssaint tool` -> `[WIN] Trayloom tool` | `RadialMenuTool.rawValue` | see 3.4.1 | feature's own symbol | see 3.4.1; label shown only when at least one tool is runnable |
| 6 | `quickToggle` | `Quick toggles` (the Quick-toggles page title; shown only if any quick toggle's feature is available) | `RadialMenuQuickToggle.rawValue` | see 3.4.3 | per-toggle symbol | see 3.4.3 |
| 7 | `windowLayout` | `Window layout` (shown only if the Window layout feature is available) | `WindowLayoutAction.rawValue`, picker default `leftHalf` | `WindowLayoutService.apply(action)` after 0.15 s; failure -> beep; requires Accessibility (asked once, then beep) | the action's own symbol (`rectangle.3.group` for unknown) | existing Trayloom window-layout service (see Gap) |
| 8 | `media` | `Media control` | `playPause|previousTrack|nextTrack|nowPlaying` | key press via aux HID media keys (after the modifier wait) or the Now Playing card | `playpause.fill` / `backward.fill` / `forward.fill` / `music.note` | see 3.4.4 / 3.7 |
| 9 | `submenu` | `Submenu` (only offered at root level) | none | opens children wheel | `ellipsis.circle` | - |

The editor starts a new item as `Kind.app` with empty payload. Changing the kind clears the payload to the new kind's default (`tool` -> first runnable tool, `quickToggle` -> first available toggle, `windowLayout` -> `leftHalf`, `media` -> `playPause`, others `""`), clears `children` unless submenu, clears `customIconData` unless URL, **keeps** the custom name and icon.

##### 3.4.1 Tools (`RadialMenuTool`, order = picker order)
A tool appears in the picker and on the wheel only if `tool.feature.isAvailable` (hub availability) and, for `shelf`, `shelfEnabled` is true. A saved slice of an unavailable tool is hidden at runtime (stays in the data) and returns when the feature is re-enabled. Running waits 0.15 s so the wheel is gone before capture/presenting.

| rawValue | Picker label (enUS, = feature hub title) | Run action (macOS) | Feature symbol | Windows mapping (Trayloom ids from the existing code/ARCHITECTURE) |
|---|---|---|---|---|
| `screenshot` | `Screenshot` | `ScreenshotService.capture()` | `camera.viewfinder` | screenshot feature: region capture action (`screenshot/region`) |
| `screenRecorder` | `Screen recording` | `ScreenRecorderService.toggle()` | `record.circle` | screen-recorder toggle |
| `colorPicker` | `Color picker` | `ColorSamplerService.pick()` | `eyedropper` | `color-picker/pick` |
| `screenOCR` | `Copy text from screen` | `ScreenTextService.capture()` | `text.viewfinder` | `screen-ocr/capture` |
| `micMute` | `Mute microphone` | `MicMuteService.toggle()` | `mic.slash` | mic-mute toggle |
| `clipboardHistory` | `Clipboard` | `showHistoryWindow()` | `doc.on.clipboard` | clipboard-history window |
| `quickLauncher` | `Quick panel` | `QuickLauncherService.show()` | `wand.and.rays` | quick panel/launcher |
| `cameraPreview` | `Camera preview` | `CameraPreviewService.show()` | `web.camera` | camera-preview window |
| `scratchpad` | `Scratchpad` | `ScratchpadService.show()` | `note.text` | `scratchpad/toggle` |
| `shelf` | `Shelf` | `ShelfService.summon()` | `tray.full` | shelf summon (needs shelf enabled) |
| `cleaner` | `Cleaner` | open Settings at Cleaner page | `sparkles` | open Trayloom settings at cleaner |
| `uninstaller` | `Uninstaller` | open Settings at Uninstaller page | `trash` | open settings at uninstaller |
| `appUpdates` | `App updates` | `AppUpdatesService.check()` + open Settings at App updates | `arrow.down.app` | trigger update check + open settings page |
| `cleaningMode` | `Cleaning Mode` | `CleaningModeManager.activate()` | `bubbles.and.sparkles` | `cleaning-mode` activate |
| `keepAwake` | `Keep awake` | `KeepAwakeManager.toggle()` | `moon.zzz.fill` | keep-awake toggle |

Tool label on wheel = the feature's hub title above (not user-localised separately).

##### 3.4.2 `availableItems` filter at session start
Drops: runnable-less tools; quick toggles whose owning feature is unavailable (`quitAllApps` belongs to the *Quit apps* feature, which is **not installed by default**; all other toggles to *Quick toggles*); window-layout items if the Window layout feature is unavailable; submenus whose filtered children are empty. If the result is empty -> beep and no wheel.

##### 3.4.3 Quick toggles (`RadialMenuQuickToggle`, picker order = enum order)
Run after 0.15 s; guarded by owning feature availability.

| rawValue | Wheel/picker title (enUS, dynamic) | Symbol | macOS action | Windows mapping |
|---|---|---|---|---|
| `darkMode` | `Switch to dark mode` (when system is light) / `Switch to light mode` (when dark) | `moon.fill` | flips system appearance | write `AppsUseLightTheme` and `SystemUsesLightTheme` (HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize) and broadcast `WM_SETTINGCHANGE` "ImmersiveColorSet"; title from current `AppsUseLightTheme` |
| `emptyTrash` | `Empty the Trash` | `trash` | confirm alert (title `Empty the Trash?`, message `All items in the Trash will be removed. This cannot be undone.`, buttons `Empty the Trash` / `Cancel`), then empties via Finder | `[WIN]` label `Empty the Recycle Bin` (replace "Trash"); `SHEmptyRecycleBin(NULL,NULL,SHERB_NOCONFIRMATION)` after the same style of confirm dialog |
| `quitAllApps` | `Quit all apps` | `xmark.app` | opens the Quit-apps window (`showQuitApps`) | open the Quit apps window (feature must be installed) |
| `ejectDisks` | `Eject all disks` | `eject.fill` | safely ejects external disks | eject removable/USB volumes: `IOCTL_STORAGE_EJECT_MEDIA` / Shell "Eject" verb |
| `hiddenFiles` | `Show hidden files` / `Hide hidden files` (by state) | `eye` | toggles Finder hidden files, restarts Finder | `HKCU\...\Explorer\Advanced\Hidden` 1/2 (+ `ShowSuperHidden` unchanged) then `SHChangeNotify`/refresh Explorer windows |
| `desktopIcons` | `Hide desktop icons` / `Show desktop icons` | `desktopcomputer` | toggles desktop icons | `HKCU\...\Explorer\Advanced\HideIcons` or `WM_COMMAND 0x7402` to the Progman/WorkerW `SHELLDLL_DefView` |
| `lockScreen` | `Lock the screen` | `lock.fill` | locks | `LockWorkStation()` |
| `displayOff` | `Turn off the display` | `display` | `pmset displaysleepnow` | `SendMessage(HWND_BROADCAST, WM_SYSCOMMAND, SC_MONITORPOWER, 2)` |
| `screenSaver` | `Start the screen saver` | `sparkles.tv` | starts ScreenSaverEngine | `SendMessage(.., WM_SYSCOMMAND, SC_SCREENSAVE, 0)` |

Other quick-toggle strings used elsewhere (for reference): `Switch to dark mode`/`Switch to light mode`, `Changes the appearance of the whole system.`, `Removes everything from the Trash.`, `Quit all apps except the front one`, `Safely ejects every external disk.`, `The Finder restarts to apply it.` (`[WIN]` Explorer), `Asks for the password to come back.`, `The Mac keeps running with the display off.` (`[WIN]` The PC), `Starts right away, on every display.`, `Could not complete.`.

##### 3.4.4 Media keys (`RadialMenuMediaKey`)
| rawValue | Picker label | Wheel label | macOS | Windows |
|---|---|---|---|---|
| `playPause` | `Play or pause` | `Play or pause` | aux key 16 (NX_KEYTYPE_PLAY), down+up | `VK_MEDIA_PLAY_PAUSE` (0xB3) via `SendInput` (KEYEVENTF_EXTENDEDKEY) or `session.TryTogglePlayPauseAsync()` |
| `previousTrack` | `Previous track` | `Previous track` | aux 20 (REWIND) | `VK_MEDIA_PREV_TRACK` (0xB1) / `TrySkipPreviousAsync` |
| `nextTrack` | `Next track` | `Next track` | aux 19 (FAST) | `VK_MEDIA_NEXT_TRACK` (0xB0) / `TrySkipNextAsync` |
| `nowPlaying` | `Now Playing` | dynamic: `Title\nArtist`, `Nothing playing`, or `Now Playing` while loading | opens the metadata card, no key | open the card (3.7) |

Media keys wait for the modifier-release poll like shortcuts (so Ctrl+Alt do not turn the key into a chord). Posting needs Accessibility on macOS (`[WIN]` none).

#### 3.4.5 Display name resolution (`displayName`)
Custom `name` wins. Else: app/file -> file display name (cached, never touches disk while tracking); url -> host of normalised URL (or raw payload); shortcut -> `GlobalShortcut.displayString` (e.g. `⌃⌥⌘Space`; `[WIN]` render Ctrl+Alt+... text) or `Press a shortcut` if invalid; tool -> hub title (`Vorssaint tool`->`Trayloom tool` if unknown); quickToggle -> dynamic title above or `Quick toggles`; windowLayout -> action title (see 3.4.6) or `Window layout`; media -> as 3.4.4 (`Media control` if unknown); submenu -> `Submenu`.

#### 3.4.6 Window-layout action titles (enUS; all 40 selectable in the picker, order = enum order)
`leftHalf` Left · `rightHalf` Right · `topHalf` Top · `bottomHalf` Bottom · `centerHalf` Center half · `leftThird` Left 1/3 · `centerThird` Center 1/3 · `rightThird` Right 1/3 · `leftTwoThirds` Left 2/3 · `rightTwoThirds` Right 2/3 · `centerTwoThirds` Center 2/3 · `topThird` Top 1/3 · `middleThird` Middle 1/3 · `bottomThird` Bottom 1/3 · `topTwoThirds` Top 2/3 · `bottomTwoThirds` Bottom 2/3 · `topQuarter` Top 1/4 · `upperMiddleQuarter` Upper middle 1/4 · `lowerMiddleQuarter` Lower middle 1/4 · `bottomQuarter` Bottom 1/4 · `leftQuarter` Left 1/4 · `leftMiddleQuarter` Left middle 1/4 · `rightMiddleQuarter` Right middle 1/4 · `rightQuarter` Right 1/4 · `topLeftSixth` Top left 1/6 · `topCenterSixth` Top center 1/6 · `topRightSixth` Top right 1/6 · `bottomLeftSixth` Bottom left 1/6 · `bottomCenterSixth` Bottom center 1/6 · `bottomRightSixth` Bottom right 1/6 · `topLeft` Top left · `topRight` Top right · `bottomLeft` Bottom left · `bottomRight` Bottom right · `maximize` Maximize · `marginMaximize` Maximize with Margin · `fullScreen` Full Screen · `center` Center · `previousDisplay` Previous display · `nextDisplay` Next display · `restore` Restore. (Enum declaration order is: leftHalf,rightHalf,topHalf,bottomHalf,centerHalf, leftThird,centerThird,rightThird,leftTwoThirds,rightTwoThirds,centerTwoThirds, topThird,middleThird,bottomThird,topTwoThirds,bottomTwoThirds, topQuarter,upperMiddleQuarter,lowerMiddleQuarter,bottomQuarter, leftQuarter,leftMiddleQuarter,rightMiddleQuarter,rightQuarter, topLeftSixth,topCenterSixth,topRightSixth,bottomLeftSixth,bottomCenterSixth,bottomRightSixth, topLeft,topRight,bottomLeft,bottomRight, maximize,marginMaximize,fullScreen,center, previousDisplay,nextDisplay,restore.) The picker item shows a `Label(title, symbol)`; symbols are the `WindowLayoutAction.symbolName` (rectangle.* family); `[WIN]` draw small rectangle glyphs with a highlighted portion (custom Path), no font glyph exists.

Window-layout action runs only if the Window-layout feature is available; on macOS it additionally needs Accessibility (asked once per session, then beeps). `[WIN]` no permission; if the foreground window is elevated (UIPI) the move fails -> beep (`SystemSounds.Beep`).

---

### 3.5 Presets (starter sets) and default layout

`RadialMenuProfilePreset` order (the "+" menu shows them in this order with these titles): `General`, `Media`, `Tools`, `Window layout`, `Quick toggles`, `Blank`. Each creates a profile with `preset = rawValue`, a default colour, items below, and empty shortcut/mouse unless given.

| Preset (raw) | Title | Default colour | Items (index 0 = 12 o'clock, clockwise) |
|---|---|---|---|
| `general` | General | Accent | 0 media `playPause`; 1 media `nextTrack`; 2 tool `screenshot`; 3 file `~/Downloads`; 4 tool `colorPicker`; 5 media `previousTrack` |
| `media` | Media | Purple | `playPause`, `nextTrack`, `nowPlaying`, `previousTrack` |
| `tools` | Tools | Cyan | `screenshot`, `colorPicker`, `screenOCR`, `screenRecorder`, `micMute`, `scratchpad` |
| `windowLayout` | Window layout | Orange | windowLayout `maximize`, `rightHalf`, `bottomHalf`, `leftHalf`, `topHalf` |
| `quickToggles` | Quick toggles | Mint | `darkMode`, `desktopIcons`, `hiddenFiles`, `lockScreen`, `emptyTrash` |
| `blank` | Blank | Graphite | (none) |

The shipped first profile on a fresh install: name `""` (displays **General**), colour Accent, shortcut Control+Option+Command+Space, mouse `off`, items = General starter set, `preset = general`. The starter item ids are generated once per run so seed-equality works. Item names are left **empty** so every language derives its own labels.
`addProfile(preset)` names the profile: `General N` (N = profiles.count + 1) for the General preset, otherwise the preset title. `duplicateProfile` names it `<display name> 2`, copies items, colour, preset; **clears** shortcut, mouseButton, trackpadTap (each opens only one wheel). New profile gets selected; submenu drill-down and drag state reset; save immediately.
`deleteProfile`: allowed only when more than one profile; selection moves to `min(index, count-1)`.
Reset button (canvas header): visible only at root level and when the preset is known: `profile.preset`, else (older wheels) the preset whose localised title or raw id case-insensitively equals the profile name; replaces **all** items with `preset.makeItems()` (new ids), exits submenu.

Colour names (picker order, enUS): `Accent, Blue, Purple, Pink, Red, Orange, Yellow, Green, Mint, Cyan, Indigo, Graphite`.

| Colour | Light-scheme RGB | Dark-scheme value |
|---|---|---|
| accent | system accent | system accent (`[WIN]` `UISettings.GetColorValue(UIColorType.Accent)`) |
| blue | (0.00, 0.48, 1.00) | system blue ≈ #0A84FF |
| purple | (0.58, 0.20, 0.85) | system purple ≈ #BF5AF2 |
| pink | (0.88, 0.16, 0.45) | ≈ #FF375F |
| red | (0.85, 0.18, 0.18) | ≈ #FF453A |
| orange | (0.95, 0.45, 0.00) | ≈ #FF9F0A |
| yellow | (0.85, 0.65, 0.00) | ≈ #FFD60A |
| green | (0.18, 0.65, 0.25) | ≈ #30D158 |
| mint | (0.00, 0.68, 0.60) | ≈ #63E6E2 (system mint dark) |
| cyan | (0.15, 0.65, 0.85) | ≈ #64D2FF |
| indigo | (0.35, 0.35, 0.85) | ≈ #5E5CE6 |
| graphite | white 0.40 (#666) | white 0.65 (#A6A6A6) |

(Dark values are the macOS system colours; use the listed approximations on Windows.)

---

### 3.6 Wheel geometry, drawing and animation (runtime panel)

#### 3.6.1 Constants (`RadialMenuLayout`)
`panelSize 400` · `wheelDiameter 300` (radius 150) · `ringRadius 112` (chip centre distance) · `chipSize 52` · `hubDiameter 76` · `deadZoneRadius 40` · `moveActivationDistance 8`.
Max slices 12, practical minimum 1 (a single item is a valid wheel).

#### 3.6.2 Slice maths (`RadialMenuGeometry`)
- Slice 0 centred at 12 o'clock, indices grow **clockwise**. Angles measured clockwise from top in radians. `angle(dx, dyUp) = atan2(dx, dyUp)` normalised to [0, 2π); `dyUp` points to the screen top (`[WIN]` dyUp = centerY - pointerY since screen Y grows down).
- `step = 2π/n`; `index = Int(((angle + step/2) mod 2π) / step)` clamped to [0, n-1].
- Highlight is nil when `distance < 40` (dead zone) or `n == 0`; no outer limit on highlighting (the pointer may be anywhere beyond; clicks beyond 150 dismiss).
- Chip centre: `θ = 2π·i/n`; offset `(sin θ·112, -cos θ·112)` from the centre in screen coordinates.

#### 3.6.3 Visual structure (z-order, back to front) - `RadialMenuView`
1. **Backplate** disc Ø300: material fill (`regularMaterial` + `PanelSurface.baseFill`: white 0.68 in light / black 0.42 in dark; with liquid glass on macOS 26: glass effect + baseFill at 0.35 light / 0.45 dark); rim highlight stroke 1.2 pt (top->bottom gradient: light white 0.95 -> 0.12, dark white 0.30 -> 0.04); border stroke 0.8 (light black 0.09, dark white 0.11; high-contrast 0.24 / 0.28); drop shadow black 0.22 (light) / 0.55 (dark), blur radius 24, y offset 8. `[WIN]` Acrylic/blur-behind or a translucent #000/#FFF tinted `Ellipse` with `DropShadowEffect(BlurRadius≈24, ShadowDepth 8, Direction 270)`; Reduce Transparency (`SystemParameters`/UISettings `AdvancedEffectsEnabled == false`) -> opaque base fill.
2. **Slice guides** (only when n > 1): n hairlines, each 1 pt wide, length `150-40-14 = 96`, centred at radius `40 + 48 + 7 = 95` (i.e. from r=47 to r=143), rotated to angle `2π(i+0.5)/n` (halfway between chips). Colour `primary` at 0.07 (light) / 0.09 (dark).
3. **Wedge highlight**: annular sector between r=40 (inner) and r=146 (`150-4`), angular width = one slice, centred on the highlighted index. Fill radial gradient centred on the wheel: at r=40 `profileColor` opacity 0.05 (light)/0.08 (dark) -> at r=150 opacity 0.20/0.30 (strongest outside). Opacity 0 when nothing highlighted.
4. **Ring**: n chips (see 3.6.4). The ring view is re-created (`id(stack.count)`) when changing submenu level so chips re-bloom.
5. **Hub** (centre, Ø76): faint plate `primary` 0.045 (light)/0.075 (dark); content cross-fades with `scale 0.92 + opacity` transition, `easeOut 0.14 s`, keyed by face: highlighted item id | `back.<parent>` | `brand`.
   - Highlighted: item display name, 11 pt semibold, centred, max 3 lines, `minimumScaleFactor 0.8`, 8 pt horizontal padding.
   - In a submenu with nothing highlighted: `chevron.backward` (12 pt semibold, secondary) over the parent name (9 pt medium, secondary, 2 lines, 8 pt padding; empty name -> `Submenu`); accessibility label `Back`.
   - Root with nothing highlighted: brand mark, 34 pt wide, template-tinted `primary` (black on light, white on dark). `[WIN]` Trayloom mark, never the Vorssaint saturn.
6. Whole 400x400 view is a hit-test rectangle; accessibility label of the view = `Radial menu`.

#### 3.6.4 Chip (`RadialChipView`)
- Circle Ø52. Normal: fill `PanelSurface.raisedFill` (white 0.88 light / white 0.14 dark), stroke `raisedBorder` 0.8 (light black 0.07 / dark white 0.16; increased contrast 0.22 / 0.32), shadow `raisedShadow` (black 0.14 light / 0.38 dark) radius 5, y 2.
- Highlighted: fill = profile colour (solid), no stroke, shadow profileColor@0.45 radius 13, y 4, and **scale 1.14** (spring response 0.24, damping 0.72; no scale if Reduce Motion).
- Icon (centred): Now-Playing playing -> running app icon 34x34; custom favicon (only when `symbolName` empty) 34x34; app/file with empty `symbolName` -> real file icon 34x34; else SF symbol 20 pt semibold, `primary` colour normally and **white** when highlighted.
- Now-Playing nothing/loading -> `music.note` 20 pt.

#### 3.6.5 Animations and timings
| What | Spec |
|---|---|
| Panel fade-in | alpha 0 -> 1, 0.18 s, ease-out; Reduce Motion: instant |
| Panel fade-out | alpha -> 0, 0.13 s, ease-in; `ignoresMouseEvents` during it |
| Wheel scale | `scale(visible ? 1 : 0.9)`; opening: spring(response 0.27, damping 0.86); closing: easeIn 0.13 s |
| Chip bloom (open or newly appeared submenu) | each chip starts at scale 0.35, opacity 0, radius `0.2·40 = 8` from centre, then flies to 112: spring(response 0.36, damping 0.75), delayed `openDelay = 0.17·i/(n-1)` s (whole sweep always 0.17 s regardless of n; 0 when n == 1) |
| Chip fold (close) | easeIn 0.1 s, delay `openDelay·0.25`; chips return to the hub |
| Chip highlight scale | spring(response 0.24, damping 0.72) to 1.14 |
| Wedge appear / disappear | opacity easeOut 0.12 s; first appearance is placed without animation |
| Wedge sweep between slices | angle + slice width animate with spring(response 0.2, damping 0.86); angle kept *unwrapped* and moved the short way round (`angle = current + remainder(target - current, 2π)`) so last->first crosses the top |
| Hub face cross-fade | easeOut 0.14 s (opacity + scale 0.92) |
| Reduce Motion (`accessibilityReduceMotion`; `[WIN]` `UISettings.AnimationsEnabled` / `SystemParameters.ClientAreaAnimation == false`) | every animation above disabled, chips and wheel appear final |
| Spring conversion for WPF | SwiftUI `spring(response r, dampingFraction ζ)`: angular freq `ω = 2π/r`, stiffness `k = ω²` (mass 1), damping `c = 2ζω`. E.g. r=0.27,ζ=0.86 -> ω=23.27, c=40.0; r=0.36,ζ=0.75 -> ω=17.45, c=26.2; r=0.24,ζ=0.72 -> ω=26.18, c=37.7; r=0.2,ζ=0.86 -> ω=31.4, c=54.0. Implement one small spring integrator driven by `CompositionTarget.Rendering` (or approximate with `QuinticEase`/`BackEase(Amplitude≈0.12)`) |

#### 3.6.6 Panel window
Borderless, transparent, 400x400, no shadow of its own (the disc draws its own), level above everything (status-bar level; `[WIN]` `Topmost`, `WS_EX_TOOLWINDOW|WS_EX_LAYERED|WS_EX_NOACTIVATE`), on all Spaces/over full-screen apps (`[WIN]` all virtual desktops: `WS_EX_TOOLWINDOW`, `VirtualDesktopManager` not needed), not movable, `canBecomeKey = true` while the app stays non-active (for Esc/arrows/digits). Pre-created hidden when the feature starts so the first open is instantaneous; destroyed when the feature is disabled. `[WIN]` the OverlayWindow (no-activate) cannot get keyboard focus: while a session is active read keys from the shared low-level keyboard hook (`Ctx.Hooks.SubscribeKeyboard` high priority) and swallow Esc/Enter/arrows/digits 1-9 (only those) instead of using window key events; do not activate the window.

---

### 3.7 Now Playing integration

Used by (a) the `nowPlaying` media slice (label + icon + card) and (b) the Island music UI shares the same enUS words. Session-scoped, never required for the wheel to work.

**Flow**: when a session starts and any item at any depth is `nowPlaying`: `RadialNowPlayingService.refresh` (generation counter prevents stale replies): state `.loading` -> fetch -> `.playing(snapshot)` or `.nothingPlaying`; the wheel's `nowPlayingState` is updated on the main thread. Slice label: `loading` -> `Now Playing`; `nothingPlaying` -> `Nothing playing`; `playing` -> `snapshot.radialLabel` = title and artist joined by a newline (either may be absent; both absent -> `Now Playing`).

**Selecting the slice** (after the wheel closes): `presentDetails(at: wheelCenter)`: `.playing` -> show card; `.loading` -> remember the anchor and show the card when the fetch finishes playing; `.nothingPlaying` -> nothing.

**macOS fetch**: `/usr/bin/perl now-playing.pl libVorssaintNowPlaying.dylib` (MediaRemote adapter, 2.0 s timeout, output cap = 12 MiB/3*4 + 1 MiB), JSON line with keys title/artist/album, playbackRate, duration, elapsed, artworkBase64, pid, displayID (bundle id), isPlaying, canSeek, canSkipNext, canSkipPrevious, itemIdentifier. Any failure/timeout/malformed output = nothing playing.

**Snapshot sanitising** (`RadialNowPlayingSupport.snapshot`): only when playing (explicit `isPlaying` wins, else `playbackRate > 0`); title/artist/album trimmed, control chars and newlines removed, truncated to 300 chars, empty -> nil; bundle id trimmed, <= 255, no control chars; pid > 0; artwork non-empty and <= 12 MiB (12·1024·1024); snapshot is nil unless it has a title, a bundle id or a pid.

**Card** (`RadialNowPlayingCard`, a borderless non-activating panel, level above others, with its own shadow, alpha fade-in 0.12 s): whole card is one button. Layout: HStack(spacing 12) { artwork 76x76 (cropped fill, corner radius 10; fallback: near-black space gradient (white 0.10 -> 0.04 -> black, top-left to bottom-right) rounded 10 with the player's app icon 42x42, else white `music.note` 24 pt) ; VStack(leading, spacing 3) { title 13 pt semibold up to 2 lines (falls back to the app name); album 11 pt secondary 1 line; artist 10.5 pt secondary 1 line; row (top padding 2): player app icon 14x14, text `Open %@` (the player's name) 9.5 pt medium secondary, `arrow.up.forward.app` 8.5 pt semibold secondary } }. Padding 12, width 320, `HUDBackdrop(cornerRadius 16)` (hudWindow material; high-contrast plate 0.55 dark/0.5 light), border primary 0.14 / 0.6 pt, continuous corners 16. Accessibility label = title, hint = `Open %@`.
Position: card centred on the wheel centre, clamped inside the visible frame of the screen containing the anchor with a 12 pt margin. Card dismissed by: any click outside it, any app activation, a new wheel session, or clicking the card.
**Click action** (`RadialNowPlayingApplication.open`): running app of the snapshot (match by bundle id, else pid); helper (non-regular) apps are ignored; unhide if hidden; activate with all windows; if the app is running without a window on screen, re-open it (like a Dock click) with `activates=false`; if not running, launch by bundle id. App name = running app's localised name, else last dot-component of bundle id; icons cached (also negative cache).

**Windows implementation** (`Windows.Media.Control`, already used by `Trayloom.Features.Island/MediaService.cs`): `GlobalSystemMediaTransportControlsSessionManager.RequestAsync()`; pick session = current session if `PlaybackInfo.PlaybackStatus == Playing`, else the first Playing session from `GetSessions()`, else nothing; `TryGetMediaPropertiesAsync()` -> `Title`, `Artist`, `AlbumTitle`, `Thumbnail` (`IRandomAccessStreamReference` -> open read, cap 12 MiB, decode to BitmapImage); app identity from `SourceAppUserModelId` (resolve display name + icon: for packaged apps via `Windows.ApplicationModel.AppInfo.GetFromAppUserModelId`, for desktop apps by matching the process/exe or Start-menu shortcut). Timeout 2 s -> nothing playing; 300-char sanitising as above. Click: bring the player to front (find process by AUMID/exe name -> `ShowWindow(SW_RESTORE)` + `SetForegroundWindow`, using `AllowSetForegroundWindow`/keyboard-trick; packaged apps -> `explorer.exe shell:AppsFolder\<AUMID>`). No external bridge/permission needed. `HUDBackdrop` -> acrylic/dark translucent rounded 16.

---

### 3.8 Settings page layout (Settings > Radial menu)

`Form` with grouped style (macOS grouped sections; `[WIN]` stacked cards with section header). Controls disabled while the master toggle is off are marked (dis).

**Section 1 - header `Radial menu`**
1. Toggle `Use the radial menu` (`radialMenuEnabled`).
2. Caption (12 pt secondary): `Opens a wheel of your favorite actions around the pointer`.
3. If the hotkey registration failed: orange caption (uses the shortcut-invalid text `Use at least Control, Option or Command with a key.`; `[WIN]` better: "This shortcut is already taken by another app." because the cause on Windows is a duplicate RegisterHotKey).
4. Picker `Opening behavior` (dis): `Press or hold` / `Press to open` / `Hold to select`.
5. Caption `Press or hold keeps the current adaptive gesture. Press stays open; hold runs the highlighted action on release.`
6. Segmented picker `Opens`: `At the pointer` (true) | `At the screen center` (false). (Not disabled.)
7. Button `Try it`.

**Section 2 (macOS only, shown when profiles need Accessibility and it isn't granted)**: `PermissionRow(.accessibility)` + caption `Key actions and the side mouse button trigger need the Accessibility permission.` (or the Window-layout "Grant Accessibility to move windows." when the current wheel uses window layout). `[WIN]` omit entirely.

**Section 3 - header `Profiles`**
1. Row: Picker `Profile` (dis; entries = display names, empty name shown as `General`) + `+` menu (help `Add profile`; items General, Media, Tools, Window layout, Quick toggles, Blank; dis) + button `plus.square.on.square` (help `Duplicate profile`; dis) + button `trash` (help `Delete profile`; dis and disabled when only one profile). Delete shows confirmation dialog: title `Delete “%@”?` (profile name), message `Its actions, shortcut, mouse button and four-finger tap will be removed. This can’t be undone.`, buttons `Delete profile` (destructive) and `Cancel`.
2. When enabled and the selected profile has no trigger (no shortcut, mouse button or tap): caption `Picking a profile here only edits it. Give this one a shortcut, a mouse button or the four-finger tap below to open it.`
3. TextField `Profile name` (dis), prompt/placeholder `General`.
4. Row `Color` + 12 round swatches (dis): swatch Ø16 in a 22x22 hit area, 6 pt spacing; selected = inner white ring (Ø12, 2 pt, white 0.9 light / 0.8 dark) + outer ring (Ø20, 1.5 pt, in the swatch colour); tooltip/accessibility = colour name.
5. Row `Shortcut` with a shortcut recorder 130 pt wide: shows current combination, `None` when empty, `Press keys` while waiting; Delete clears; errors shown as caption under the row (orange): invalid -> `Use at least Control, Option or Command with a key.`; conflict -> `This shortcut is already used by %@.` (name = other wheel name / other shortcut role / window-layout shortcut / command-bar row); not captured -> `Nothing was captured. macOS or another app already uses that combination. Try another one.` (`[WIN]` "Windows or another app already uses that combination."). Conflicting shortcuts are refused (not saved).
6. Picker `Mouse button` (dis): `Off`, then buttons 3...31: `Back side button` (3), `Forward side button` (4), `Button 6` ... `Button 32` (format `Button %d` with number+1). `[WIN]` Windows only delivers X1/X2 reliably: offer `Off`, `Back side button`, `Forward side button` (keep stored values `off|back|forward`; optional extra `button:N` only if raw-input HID is implemented). Choosing a button releases it from every other profile; choosing non-off requests Accessibility on macOS.
7. Caption `Only extra mouse buttons work here. With a mouse without extra buttons, use the keyboard shortcut above. With a trackpad, you can also use the four-finger tap below.`
8. If a button is set: when it is Back or Forward: caption `While this is on, that button no longer goes back or forward in apps. If you navigate with it, leave this off.` Then the **Button test** row (see below).
9. Toggle `Open with a four-finger tap` (dis); setting it on turns it off on all other profiles; requests Accessibility on macOS.
10. If the tap is on and Middle click uses 4 fingers: orange caption `Middle click already uses the four-finger tap, so the tap does not open this wheel.`

**Button test row** (shown under 8): `[icon] Button test` ... right-aligned status text; caption below `If nothing lights up, macOS is not sending that button to apps. Mice that come with their own software often turn the side buttons into something else. Change it back there, or give the button a key combination and use that combination above.` Icon/status: tap not running -> orange `exclamationmark.circle.fill` + `Vorssaint cannot watch the mouse right now`; waiting -> grey dashed circle + `Press the button now`; the expected button arrived -> green `checkmark.circle.fill` + `Vorssaint sees this button`; another button arrived -> orange + `That was a different button`. While this row is on screen the service reports every extra button press (`lastMouseButtonSeen`) and keeps the tap alive even if the feature is off; reporting stops (and last seen cleared) when it disappears. `[WIN]` replace Vorssaint -> Trayloom, "macOS" -> "Windows", "Mice that come with their own software..." unchanged.

**Section 4 - header `Actions`**
1. The **visual canvas** (3.10), row insets top/bottom 4, leading/trailing 0.
2. Caption `Click a button to choose what it starts, or to remove it. Drag a button to move it.`
3. If the level has < 12 items: button `Add action` with `plus` icon (starts the editor with a new `.app` item).
4. DisclosureGroup titled `Show as list` / `Hide list` (collapsed by default): in a submenu first a `chevron.backward  Back  <Submenu name>` accent link (label text `Back  <name>`, two spaces); if empty caption `No actions here yet.`; then one row per item (3.10.3).
5. Footer (caption): `A wheel holds up to 12 actions.` when the level has 12 items, else `Opens a wheel of your favorite actions around the pointer`.

Drag-and-drop anywhere in the page clears the "dragging" state on drop. Every edit is persisted immediately (`radialMenuProfiles`) and `syncWithPreferences()` re-registers hotkeys. Toggling `Use the radial menu` calls `syncWithPreferences` (start/suspend).

#### 3.8.1 Action editor sheet (`RadialItemEditor`)
Modal sheet 440 pt wide, 18 pt padding, 14 pt vertical spacing.
- Heading (headline): `Add action` if new, else the item's display name.
- Form (column style): Picker `Action` (kind list, 3.4) → payload row → TextField `Name` (prompt `Automatic`; custom label, empty = automatic) → Icon picker.
- Payload rows: `app`: labelled row `Open an app` with a button showing `Choose…` (empty) or the chosen app's display name (opens file chooser: for apps only `.app`, dir `/Applications`; for files files+folders; attached as sheet to the editor window); `file`: row `Open a file or folder` same; `url`: text field `Open a link` prompt `example.com`, button `Fetch Website Icon` (shows spinner + `Fetching icon…` while loading; disabled while loading, empty or invalid URL), status: green check + `Icon downloaded` or orange triangle + `Could not find a website icon`, disclaimer caption `Connects to the website once to download its icon. Saved locally.`; editing the payload clears the status; `shortcut`: row `Press a shortcut` with a recorder 108 pt wide (`None` when empty, `Press keys` when listening, caption while listening `Press the new shortcut Escape cancels. Delete clears.`, failures in orange: `Nothing was captured...` / invalid text); `tool`: Picker `Tool` listing runnable tools by hub title; `quickToggle`: Picker `Quick toggles` with items `Label(dynamic title, symbol)`; `windowLayout`: Picker `Window layout` with all 40 actions as Label; `media`: Picker `Control` with `Play or pause`, `Previous track`, `Next track`, `Now Playing` (in that order); `submenu`: none.
- Under the form: orange caption `Enter a valid link.` when a URL is non-empty and invalid; for submenu: caption `Opens a second wheel with its own actions.` and, if opened from an existing item, accent link `Edit actions >` (closes sheet and drills in).
- Buttons row: `Remove` (destructive; only when editing an existing item; deletes and closes) · spacer · `Cancel` · `Save` (prominent, default). `Save` is disabled when `isValidPayload` fails (so the editor can never save what the wheel would drop). Save trims the name. New submenu → after saving, the canvas drills into it automatically.
- **Icon picker** (`Icon`): adaptive grid (min cell 34, spacing 6): first cell = "Automatic" (tooltip `Automatic`; shows the real file icon 20x20 for app/file, the fetched favicon 20x20, or the kind's default symbol), then the curated symbols below (only those the OS has). Cell 34x30, corner 7, selected = accent fill 0.14 + accent stroke 0.75 (1.2 pt), unselected = secondary 0.06 fill + secondary 0.13 stroke (1 pt). Tooltip = symbol name. Selecting a symbol sets `symbolName`; Automatic sets it empty (custom favicon and file icon only render when `symbolName` is empty). Fetch Website Icon sets `customIconData` and clears `symbolName`.

**Curated symbol list** (`RadialMenuSupport.symbolNames`, 88 entries, in order) with suggested Segoe Fluent Icons glyph (verify against the font map; entries marked ≈ have no exact glyph - use closest, an emoji-free geometry or a bundled vector set (Fluent System Icons, MIT; adding it as NuGet/asset = **ask first**)):
`star.fill` E735 · `heart.fill` EB52 · `bolt.fill` E945 · `flame.fill` ≈ (custom) · `sparkles` ≈ U+2726 (Segoe UI Symbol) · `folder.fill` E8B7 · `doc.fill` E8A5 · `tray.full.fill` ≈ E7B8 · `terminal.fill` E756 · `globe` E774 · `envelope.fill` E715 · `message.fill` E8BD · `music.note` E8D6 · `headphones` E7F6 · `camera.fill` E722 · `photo.fill` E91B · `video.fill` E714 · `gamecontroller.fill` E7FC · `calendar` E787 · `clock.fill` E121 · `house.fill` E80F · `cart.fill` E7BF · `hammer.fill` ≈ E90F · `paintbrush.fill` ≈ E771 · `book.fill` ≈ E736 · `keyboard` E765 · `magnifyingglass` E721 · `airplane` E709 · `checkmark.circle.fill` EC61 · `xmark.circle.fill` EB90 · `plus.circle.fill` ≈ E710 · `minus.circle.fill` ≈ E738 · `exclamationmark.triangle.fill` E7BA · `questionmark.circle.fill` E897 · `info.circle.fill` E946 · `lock.fill` E72E · `lock.open.fill` E785 · `key.fill` ≈ E192 · `person.fill` E77B · `person.2.fill` E716 · `bell.fill` ≈ E7ED · `flag.fill` E7C1 · `bookmark.fill` ≈ E728 · `tag.fill` E8EC · `paperclip` E723 · `link` E71B · `scissors` E8C6 · `doc.on.clipboard` E77F · `square.and.arrow.up` E72D · `square.and.arrow.down` E896 · `trash.fill` E74D · `archivebox.fill` ≈ E7B8 · `externaldrive.fill` EDA2 · `internaldrive.fill` ≈ EDA2 · `display` E7F4 · `desktopcomputer` ≈ E977 · `laptopcomputer` E7F8 · `iphone` E8EA · `ipad` E70A · `applewatch` ≈ E916 · `wifi` E701 · `network` ≈ E839 · `antenna.radiowaves.left.and.right` ≈ E968 · `speaker.wave.2.fill` E767 · `mic.fill` E720 · `waveform` ≈ E9D9 · `play.fill` E768 · `pause.fill` E769 · `stop.fill` E71A · `backward.fill` E892 · `forward.fill` E893 · `shuffle` E8B1 · `repeat` E8EE · `sun.max.fill` E706 · `moon.fill` E708 · `lightbulb.fill` ≈ EA80 · `battery.100` E83F · `power` E7E8 · `eye.fill` E7B3 · `eye.slash.fill` ED1A · `location.fill` E707 · `map.fill` ≈ E826 · `paperplane.fill` E724 · `bubble.left.fill` E8F2 · `phone.fill` E717 · `gearshape.fill` E713 · `slider.horizontal.3` ≈ E9E9 · `switch.2` ≈ E8AB · `command` ≈ U+2318 · `printer.fill` E749 · `textformat` E8D2 · `number` ≈ "#".

Default (automatic) glyphs: app ECAA (AppIconDefault), file/folder E8B7, url E71B, shortcut ⌘/U+2318, tool = the tool's feature glyph, quickToggle E7E8, windowLayout grid of rectangles, media: play/pause E768 (playPause), E892 (prev), E893 (next), E8D6 (nowPlaying), submenu E712 inside a circle.

---

### 3.9 Behaviour details and edge cases

- Feature off: no hotkeys, no mouse tap, no panel, no monitors. Feature on with only shortcut triggers: only Carbon hotkeys + hidden panel. A mouse tap exists only while some profile has a mouse button (or the Settings button-test row is visible).
- Hotkeys are re-registered whenever profiles change (`syncWithPreferences`): all unregistered then one per profile with a non-empty valid shortcut, id `1700 + index`, claim name `radialMenuProfiles.<uuid>`. Two wheels on one combination are refused in Settings.
- The mouse tap (macOS) is handed back when the user's login session is switched away; it is rebuilt from preferences on return and the open wheel is closed. It is torn down and rebuilt if the system disables it; during the "mouse button shortcuts" capture row the tap lets events pass; a claimed button's down/up are always swallowed even if no wheel can open (corrupt blob case).
- Press of the claimed button while a *non-hold* wheel is open ends the session; while a hold-phase wheel is open the press is swallowed and meaningless; release of the held button resolves the hold per mode.
- `claimsMouseButton` is cheap by design (reads only mouseButton fields from the JSON; doesn't decode icons) so Mouse navigation and Mouse button shortcuts can ask whether the radial menu owns a button on every event; the Mouse buttons capture row says the button is taken (string in MouseButtonStrings: `This button already opens the radial menu...`).
- Shortcut/media/window/quick-toggle/tool actions run after the panel is closed; tools use a 0.15 s delay; shortcut/media wait for modifiers up (max 1.5 s).
- Item name length 60; payload trimmed; profile name 60.
- Favicon fetch (only on explicit button press): `https://<host[:port]>/favicon.ico` with user/password/path/query/fragment stripped; http(s) only; ephemeral session, 5 s request+resource timeout, `Accept: image/*`; HTTP 200-299 only; abort when `Content-Length` > 2 MiB or the streamed body exceeds 2 MiB; redirects followed only to the identical origin (scheme, host, port); decoded image must be <= 4096 px per side and <= 16 777 216 pixels, non-zero; resampled to 64x64 (high interpolation) and PNG-encoded; result rejected if PNG > 65 536 bytes. Failure shows `Could not find a website icon`.
- Icon/name caches (`RadialMenuIconStore`): file icons (34x34) and display names are cached per payload so tracking never touches the disk (dead network mounts); invalidated when the payload is edited; custom icons cached per item id.
- Reduce Transparency disables the glass effect; Reduce Motion disables every animation.
- Localisation: all strings come from `RadialMenuFeatureStrings` per language; the wheel re-renders on language change.

---

### 3.10 Visual canvas (Settings editor of the ring) - `RadialMenuVisualCanvas`

An interactive miniature of the wheel inside Settings; it is the primary editor (the list below it is the accessible alternative). Mirrors the real geometry with smaller constants. No preview of animation; `Try it` opens the real wheel.

#### 3.10.1 Constants and layout
Stage: full-width, height **330**, corner radius 14 (continuous), clipped. Background: solid (0.08, 0.09, 0.11) + radial gradient (centre, profile colour @0.15 -> clear, start radius 20, end radius 150) + `spaceGradient` @0.4 (white 0.10 -> 0.04 -> black, top-left -> bottom-right). Border white 0.12 (light) / 0.14 (dark), 0.8 pt. Shadow black 0.08 (light) / 0.28 (dark), radius 10, y 4.
Geometry: wheel Ø **250**, ring radius **92**, hub Ø **68**, dead zone radius **36**; chip Ø **44**; chip icon 16 pt semibold (images 26x26). Always dark-themed regardless of OS theme.
Wheel centre = centre of the stage's canvas area (stage height minus header). Chip positions use the same `unitPosition` as the runtime wheel (slice 0 at top, clockwise).

#### 3.10.2 Parts
- **Header bar** (padding horizontal 14, top 10): left = if inside a submenu: button `chevron.backward` (11 pt bold) + submenu display name (12 pt medium) in the profile colour, tooltip `Back` (click -> parent level); else static title `Radial menu` (`pageTitle`) 11 pt semibold, white 0.6, **uppercased**. Right = `Reset` (borderless small button, white 0.7) shown only if the reset is possible (root level and preset known); click shows a confirmation dialog titled `Reset actions` (message `Restore the default actions for this profile? Custom actions will be replaced.`, destructive button `Reset actions`).
- **Backplate**: Ø250 circle black 0.42, 0.8 pt white-0.12 border, shadow black 0.4 radius 14 y 4.
- **Dividers** (n > 1): lines at angles `step·(i+0.5)` from r=36 to r=123 (`250/2 - 2`), stroke white 0.09, width 1.
- **Wedge highlight**: sector inner 36, outer 123, for the target slice (during a drag) or hovered slice; fill profile colour @0.32 when it is the drag target, @0.22 when hovered; animates easeInOut 0.12 s.
- **Chips**: `RadialCanvasChip`: Ø44 circle; idle fill (0.18, 0.19, 0.22), border white 0.18 / 0.8 pt; highlighted (hover or drag target) fill profile colour, border white 0.5 / 1.4 pt; shadow black 0.35 radius 4 y 2; icon white 0.9 (white when highlighted); app/file real icon; favicon; Now Playing always `music.note`. Hovered chip scale 1.08 (hover tracking disabled while dragging); dragged chip is shown at 0.25 opacity in its slot.
- **Hub** (Ø68): fill (0.12, 0.13, 0.16), border white 0.16 / 0.8 pt, shadow black 0.3 radius 6 y 2. Content by state: dragging over a target -> `arrow.triangle.swap` (13 pt bold, profile colour) + target name (9 pt semibold white, 2 lines); hovering -> item name (10 pt semibold white, 2 lines) + kind label (8 pt medium white 0.6); inside a submenu -> `chevron.backward` (12 bold, profile colour) + `Back` (9 medium white 0.7); empty level -> `plus` (14 bold, profile colour) + `Add action` (8 medium white 0.7); otherwise the brand mark 28 pt white 0.85. Tapping the hub: goes back (in a submenu) or opens the add editor (empty level).
- **Floating dragged chip**: highlighted chip, scale 1.15, shadow profile colour @0.6 radius 10 y 4, follows the pointer (`position(dragPosition)`), not hit-testable.
- Kind labels (hub hover text): `Open an app`, `Open a file or folder`, `Open a link`, `Press a shortcut`, `Vorssaint tool` (`[WIN]` Trayloom tool), `Quick toggles`, `Window layout`, `Media control`, `Submenu`.

#### 3.10.3 Interactions
- Click a chip (movement <= 6 pt): open the action editor for it (3.8.1).
- Drag a chip (movement > 6 pt): chip floats with the pointer; the drop slot is the slice under the pointer (angle-based, same index maths as runtime, no dead-zone requirement); on release the dragged item and the item in the target slice are **swapped** (`swapAt`, spring(response 0.25, damping 0.8), none when Reduce Motion). Dropping in place or outside does nothing. Drag state clears on end.
- Right-click / context menu on a chip: `Radial menu` (opens the editor - the page title string is reused as the label), `Edit actions` (only for submenus; drills in), divider, `Remove` (destructive).
- Accessibility label of a chip = item display name.
- Empty level shows the plus hub as the call to action.
- List alternative (DisclosureGroup rows): drag handle (`line.3.horizontal`, tertiary), 30x30 badge (real file icon 26 for app/file with automatic icon, otherwise a 30x30 corner-8 `spaceGradient` tile with a white 13 pt semibold symbol), two-line label (name; kind in caption secondary), for submenus a right-aligned borderless `Edit actions >` button (tooltip `Edit actions`); click row = edit; context menu `Remove`; rows are drag-reorderable with **insert** semantics (`move before target`, animated easeInOut 0.12 s as the drag enters a row; row at 0.45 opacity while dragged).

---

### 3.11 Strings (enUS, verbatim `RadialMenuFeatureStrings`)

Format: key = "text". `%@` placeholders preserved. Substitutions flagged `[WIN]`.

pageTitle = "Radial menu"
hubDescription = "Opens a wheel of your favorite actions around the pointer"
enableLabel = "Use the radial menu"
enableCaption = "Hold the shortcut, point at an action and release. A quick press keeps the wheel open." (defined, not currently shown in Settings)
positionLabel = "Opens"
positionPointer = "At the pointer"
positionCenter = "At the screen center"
tryButton = "Try it"
actionsHeader = "Actions"
addButton = "Add action"
limitCaption = "A wheel holds up to 12 actions."
emptyCaption = "No actions here yet."
backButton = "Back"
editActionsButton = "Edit actions"
nameLabel = "Name"
automaticLabel = "Automatic"
iconLabel = "Icon"
actionLabel = "Action"
kindApp = "Open an app"
kindFile = "Open a file or folder"
kindURL = "Open a link"
kindShortcut = "Press a shortcut"
kindTool = "Vorssaint tool"  `[WIN]` "Trayloom tool"
kindMedia = "Media control"
kindSubmenu = "Submenu"
chooseButton = "Choose…"
urlPlaceholder = "example.com"
urlInvalid = "Enter a valid link."
toolLabel = "Tool"
mediaLabel = "Control"
mediaPlayPause = "Play or pause"
mediaPrevious = "Previous track"
mediaNext = "Next track"
mediaNowPlaying = "Now Playing"
mediaNothingPlaying = "Nothing playing"
mediaOpenAppFormat = "Open %@"
submenuCaption = "Opens a second wheel with its own actions."
saveButton = "Save"
deleteButton = "Remove"
permissionCaption = "Key actions and the side mouse button trigger need the Accessibility permission."  `[WIN]` not shown
manageButton = "Manage the menu"
panelCaption = "Your favorite actions on a wheel"
mouseTriggerLabel = "Mouse button"  (defined; the page uses `profileMouseTriggerLabel`)
mouseTriggerOff = "Off"
mouseTriggerBack = "Back side button"
mouseTriggerForward = "Forward side button"
mouseTriggerWarning = "While this is on, that button no longer goes back or forward in apps. If you navigate with it, leave this off."
buttonTestLabel = "Button test"
buttonTestWaiting = "Press the button now"
buttonTestSeen = "Vorssaint sees this button"  `[WIN]` "Trayloom sees this button"
buttonTestOther = "That was a different button"
buttonTestBlind = "Vorssaint cannot watch the mouse right now"  `[WIN]` "Trayloom cannot watch the mouse right now"
buttonTestHint = "If nothing lights up, macOS is not sending that button to apps. Mice that come with their own software often turn the side buttons into something else. Change it back there, or give the button a key combination and use that combination above."  `[WIN]` "...Windows is not sending that button to apps..."
activationModeLabel = "Opening behavior"
activationModePressOrHold = "Press or hold"
activationModePress = "Press to open"
activationModeHold = "Hold to select"
activationModeCaption = "Press or hold keeps the current adaptive gesture. Press stays open; hold runs the highlighted action on release."
profilesHeader = "Profiles"
profilePickerLabel = "Profile"
addProfileButton = "Add profile"
duplicateProfileButton = "Duplicate profile"
deleteProfileButton = "Delete profile"
deleteProfileConfirmFormat = "Delete “%@”?"
deleteProfileConfirmMessage = "Its actions, shortcut, mouse button and four-finger tap will be removed. This can’t be undone."
profileNameLabel = "Profile name"
profileColorLabel = "Color"
profileShortcutLabel = "Shortcut"
profileMouseTriggerLabel = "Mouse button"
presetGeneral = "General"
presetMedia = "Media"
presetTools = "Tools"
presetWindowLayout = "Window layout"
presetQuickToggles = "Quick toggles"
presetBlank = "Blank"
colorAccent = "Accent" · colorBlue = "Blue" · colorPurple = "Purple" · colorPink = "Pink" · colorRed = "Red" · colorOrange = "Orange" · colorYellow = "Yellow" · colorGreen = "Green" · colorMint = "Mint" · colorCyan = "Cyan" · colorIndigo = "Indigo" · colorGraphite = "Graphite"
fetchFaviconButton = "Fetch Website Icon"
fetchFaviconDisclaimer = "Connects to the website once to download its icon. Saved locally."
fetchFaviconLoading = "Fetching icon…"
fetchFaviconSuccess = "Icon downloaded"
fetchFaviconError = "Could not find a website icon"
mouseTriggerRequirement = "Only extra mouse buttons work here. With a mouse without extra buttons, use the keyboard shortcut above. With a trackpad, you can also use the four-finger tap below."
canvasHint = "Click a button to choose what it starts, or to remove it. Drag a button to move it."
resetActionsButton = "Reset"
resetActionsConfirm = "Reset actions"
resetActionsConfirmMessage = "Restore the default actions for this profile? Custom actions will be replaced."
showListButton = "Show as list"
hideListButton = "Hide list"
trackpadTapLabel = "Open with a four-finger tap"
trackpadTapConflict = "Middle click already uses the four-finger tap, so the tap does not open this wheel."
profileNoTrigger = "Picking a profile here only edits it. Give this one a shortcut, a mouse button or the four-finger tap below to open it."

Strings borrowed from other modules: shortcut recorder `Press keys` (shortcutPressKeys), `None` (shortcutNone), `Press the new shortcut` + `Escape cancels.` + `Delete clears.` (shortcutRecording/EscapeHint/DeleteHint joined by a space), `Use at least Control, Option or Command with a key.` (shortcutInvalid), `Nothing was captured. macOS or another app already uses that combination. Try another one.` (shortcutNotCaptured), `This shortcut is already used by %@.` (shortcutConflictFormat), `Cancel` (uninstallerCancel); `Grant Accessibility to move windows.` (window-layout missingPermission); the panel title attribute of the wheel window is the app name (`Vorssaint` -> `[WIN]` `Trayloom`) and the card window title `Now Playing`.

---

### 3.12 Windows mapping summary

| macOS piece | Windows implementation |
|---|---|
| Carbon `RegisterEventHotKey` per profile | `RegisterHotKey` with `MOD_NOREPEAT` on a message window (id 1700+index) **or** the shared LL keyboard hook (`Ctx.Hooks.SubscribeKeyboard`) when hold detection needs key-up. Hold = poll `GetAsyncKeyState` of the shortcut's modifiers on every pointer move + low-level key-up events. Default `[WIN]` Ctrl+Alt+Space (Control+Option+Command+Space has no 1:1; Cmd ≈ Win/Ctrl per global mapping) |
| CGEventTap otherMouseDown/Up swallow | `WH_MOUSE_LL` hook swallowing `WM_XBUTTONDOWN/UP` for XBUTTON1 (back) / XBUTTON2 (forward) (return non-zero); button numbers map 3->X1, 4->X2; wheel buttons beyond 5 not available without vendor drivers (use Raw Input `RI_MOUSE_BUTTON_4/5` only to *observe* for the test row) |
| Four-finger trackpad tap | Precision-touchpad HID via Raw Input (`WM_INPUT`, usage page 0x0D/usage 0x05, contact count 4, short tap): possible but Windows' own 4-finger gesture also fires unless the user sets *Settings > Bluetooth & devices > Touchpad > Four-finger tap* to "Nothing". Priority P3; show a hint caption. Conflict caption with Middle click kept |
| `NSEvent` local/global monitors | `WH_MOUSE_LL` for moves/clicks while a session is active (or `GetCursorPos` timer at 16 ms like the current port) + `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` for "another app activated" |
| Accessibility permission | not needed (hooks/SendInput work without consent); skip permission rows and the beep-once logic; UIPI limitation: cannot inject into elevated windows unless Trayloom runs elevated |
| Panel (NSPanel, non-activating, key) | WPF `OverlayWindow` (no-activate, topmost, tool window, per-monitor DPI), 400x400 DIPs, centre clamped to the monitor **work area** (`MonitorFromPoint` + `GetMonitorInfo.rcWork`) of the monitor under the cursor. Convert physical px <-> DIPs with the window's DPI |
| `NSScreen.pointerVisibleFrame` | work area of the monitor containing the cursor |
| Material/glass | acrylic or semi-opaque tinted ellipse; follow system light/dark theme (`ThemeManager`) |
| Reduce Motion / Transparency | `SystemParameters.ClientAreaAnimation`, `UISettings.AnimationsEnabled`, `UISettings.AdvancedEffectsEnabled` |
| `NSWorkspace.open` | `Process.Start(new ProcessStartInfo(path){ UseShellExecute = true })` |
| File/app icons | `IShellItemImageFactory.GetImage(34x34 * dpi)` (or `SHGetFileInfo`) cached by payload |
| `NSSound.beep` | `System.Media.SystemSounds.Beep.Play()` |
| Media keys | `SendInput` VK_MEDIA_* (or GSMTC commands) |
| Now Playing | GSMTC (3.7) |
| Settings persistence | `SettingsStore` scope `radial-menu` (see 3.2) |
| Favicon fetch | `HttpClient` (5 s timeout, manual redirect with same-origin check, 2 MiB cap), decode with WIC (`BitmapDecoder` handles `.ico`), render 64x64, `PngBitmapEncoder`, <= 65 536 bytes |

---

### 3.13 Gap vs Trayloom (`src/Trayloom.Features.Tools/RadialMenu/RadialMenuFeature.cs`, 453 lines, one file)

What exists: feature id `radial-menu`; a held trigger (single keyboard hotkey via the shared LL keyboard hook, or middle/X1/X2 mouse via the LL mouse hook, swallowed); `RadialWindow : OverlayWindow` (380x380, wedge `Path`s, 8 fixed slots, hub text "Release to run"); selection by polling `GetCursorPos` every 16 ms; release runs the highlighted slot via `Ctx.Actions` (`feature/action` catalog) after closing; a manual "Show radial menu at pointer" `FeatureAction` (click completes); notification when < 2 slots; slots chosen via 8 combo boxes; default slots seeded from `screenshot/region, color-picker/pick, screen-ocr/capture, quick-toggles/dark-mode, command-bar/open, scratchpad/toggle, kill-process/open, cleaner/open`.

What differs / is missing (prioritised):

**P0 - data model & architecture**
1. No profile model: replace the 8 `slotN` settings with `profiles` (JSON, 3.2) holding colour, shortcut, mouse button, trackpad tap, preset and items. Add profile CRUD (add from 6 presets, duplicate, delete with confirmation, rename, colour), uniqueness rules for shortcut/mouse button/tap, sanitising (12 items, depth 2, 60-char names, URL normalisation, icon size) and legacy fallback seeding (General profile with the starter wheel).
2. Item kinds: only "invoke a Trayloom feature action". Needs the 9 kinds (app, file/folder, URL, shortcut, tool, quick toggle, window layout, media, submenu) with payload validation, display names, icons (file icons, favicons, curated glyph list) and the run semantics of 3.4 (150 ms tool delay, modifier-wait before SendInput, ShellExecute, etc.). Keep the `feature/action` catalog only as the backing for the `tool` and `quickToggle` kinds (map table 3.4.1/3.4.3) - the existing `FeatureAction.Glyph` can supply default icons.
3. Slice count: current fixed max 8 (min 2); Vorssaint allows 1-12 slices and submenu stack (one level).

**P0 - triggers & gesture**
4. Activation modes `Press or hold` / `Press to open` / `Hold to select`; current behaviour is only "hold, release runs". Add sticky mode (click a slice runs it, click outside / Esc / hub back / second hotkey press closes), hold-then-sticky transition when release happens with no highlight, 8 pt move-activation distance, arrow-key navigation, Enter, digits 1-9, Esc step-back, click on dead zone = back, click beyond radius = dismiss.
5. Hotkey mode currently requires the modifiers to match exactly and uses key repeat suppression via `_open`; Vorssaint triggers on the shortcut press and ends the *hold* when the modifiers are released (not the main key). Implement per profile, multiple wheels simultaneously.
6. Mouse trigger limited to a single global choice (middle/X1/X2); Vorssaint is per profile, offers Off/Back/Forward (no middle), plus the button-test row and the "while this is on the button no longer goes back/forward" warning.
7. Four-finger trackpad tap not implemented (P3, optional); conflict caption with Middle click.
8. "Opens at pointer / screen centre" option missing (always at pointer), no clamping to the work area (the ring can leave the screen); add `atPointer` and clamp as in 3.3.3.
9. Wheel ends on foreground-app change / another profile hotkey / login-session change - not handled; no Try it button.

**P1 - visuals**
10. Visual style differs substantially: current = 8 flat annular wedges (Outer 170, Inner 54, opaque #E6202026, indigo #5B5BE8 hover) with glyph + text inside each wedge. Target = glass disc Ø300 with 52 pt round **chips** on a 112 radius ring, hairline slice guides, gradient wedge highlight, 76 pt hub with faint plate / selected name / back hint / brand mark, profile-colour accent, shadows (3.6). Panel is 400x400 with soft shadow.
11. No animations: add the open/close scale + fade, chip bloom sweep (0.17 s) and fold, chip highlight spring (1.14x), wedge sweep spring along the short arc, hub cross-fade, Reduce Motion support (3.6.5).
12. No light/dark awareness (uses fixed dark brushes); implement the light/dark values in 3.6 and the 12 colours (3.5).
13. Labels: Vorssaint shows names only in the hub (not under each chip); current port shows text in each wedge - remove.
14. Uses `Segoe Fluent Icons` text glyphs via `UiKit.IconFont`; also need real file icons, favicon images and the curated glyph set.

**P1 - settings UI**
15. Settings view is an auto-generated list (`SettingsViewBuilder`: trigger choice, hotkey, 8 slot combo boxes, a note). Need the full page of 3.8: master toggle, Opening behavior picker + caption, Opens segmented picker, Try it, Profiles section (picker, add menu, duplicate, delete, name, colour swatches, shortcut recorder with conflict detection, mouse button picker + warnings + test row, trackpad tap), Actions section with the **visual canvas** (drag-swap, hover wedge, hub states, context menu, header with back/Reset), hint, Add action, collapsible list with drag-reorder, footer caption; plus the item editor sheet (3.8.1) with the symbol grid and "Fetch Website Icon".
16. Menu-panel row (toggle `Radial menu` / `Your favorite actions on a wheel` / `Manage the menu`) and hub tile entry text/description are not present with these strings - add per 3.1.
17. Strings: none of the Vorssaint enUS strings exist in the port (it uses its own "Show radial menu at pointer", "Open the menu with", "Held keyboard shortcut", "Slot N", ...). Replace with 3.11.

**P2 - media / now playing**
18. No media slices, no Now Playing card. `Trayloom.Features.Island/MediaService.cs` already wraps GSMTC (title/artist/album/artwork/commands); extract or duplicate a small reader for the radial slice (label `Title\nArtist`, `Nothing playing`, app icon chip, 320 pt card with artwork 76, `Open <App>`), 2 s timeout, 300-char sanitising, 12 MiB artwork cap, card positioning and dismissal rules (3.7).
19. Media keys through `SendInput` with the modifier-release wait.

**P2 - quick toggles / window layout / tools**
20. Quick toggles (dark mode, empty Recycle Bin with confirm, quit all apps, eject disks, hidden files, desktop icons, lock, display off, screen saver) need Windows implementations (3.4.3) or catalogue actions from the existing quick-toggles feature; window-layout slices need the existing window-layout feature's `Apply(action)` for the 40 actions (verify Trayloom has all thirds/quarters/sixths/display moves; otherwise those entries must be hidden like unavailable features).
21. `availableItems` filtering (tools of uninstalled/disabled features disappear, dormant `shelf` unless enabled, empty submenus removed) and the beep when nothing is available (current port shows a notification "Assign at least two actions...").

**P3 - polish**
22. Persist/restore through settings backup; `Reset` restores the preset; dedupe/sanitise on load; lossy decoding (drop only the broken item).
23. Per-app rings and per-slot size/colour/theme do not exist in Vorssaint either - do **not** add them.


---

## 4. Screenshot

Source of truth: `Services/QuickTools/Screenshot*.swift`, `ScreenCaptureService.swift`, `RecentCapture*.swift`, `UI/Screenshot/*`, `UI/Settings/ScreenshotSettings.swift`, `ScreenCaptureSettings.swift`, `ScreenCaptureToolPicker.swift`, `Core/ScreenshotStrings.swift` (enUS), `Core/RecentCaptureStrings.swift`, `Core/Defaults.swift`, `Core/GlobalShortcut.swift`.

Conventions used below
- `[WIN]` = the Vorssaint text/behaviour is macOS-specific and the Windows replacement is given; keep the original verbatim next to it in the string table so a later localisation pass can diff.
- macOS modifier mapping used for every shortcut in this section: `⌘` -> `Ctrl` (inside the editor, pins, preview), `⌃⌥⌘` (the "free layer" the app uses for every global default) -> `Ctrl+Alt+Shift` (the Windows key `Win` is reserved by the shell, so it is never used), `⌥` -> `Alt`, `⌃` (control-scroll for zoom) -> `Ctrl`, `⇧` -> `Shift`, `⌫` -> `Backspace`/`Delete`, `⏎` -> `Enter`, `⎋` -> `Esc`. If the global hotkey spec (another part) fixes a different layer, apply it consistently; the digit/letter keys stay the same.
- "px" = physical screen pixels, "pt/DIP" = WPF device-independent units. All image/annotation geometry is in **image pixels** (top-left origin); only the editor view scales it.
- Screenshot is PNG only. There is no JPEG/WebP/format option, no shutter sound (the only sound is a system beep on a failure), and no "aspect ratio" option on the backdrop.

---

### 4.0 Feature identity, hub tile, menu-panel tile

| Item | Value |
|---|---|
| Feature id | `screenshot` (AppFeature `.screenshot`), hub group **Tools** |
| Hub / page title (`pageTitle`) | `Screenshot` |
| Hub description (`hubDescription`) | `Captures an area, window or screen and annotates it` |
| Hub/menu symbol | SF `camera.viewfinder` -> Segoe Fluent `U+E722` (Camera) or a custom viewfinder vector (preferred: corners + dot) |
| Menu-panel utility tile | Title `Screenshot`, caption (`panelCaption`) `Capture an area, window or the whole screen`; shows the configured shortcut as `shortcutHint`; clicking runs "Capture now". Tile visibility key `panelUtilityScreenshot` (Bool, default `true`). The Mac shows `Permission required: Screen Recording` in place of the caption when the permission is missing -> **[WIN] never shown** (no permission on Windows). |
| Companion tile | "Recent captures" (`clock.arrow.circlepath` -> `U+E81C` History), title `Recent captures`; opened from the menu panel, the editor's top bar, the Command Bar and the history hotkey. |
| Feature requirement | Mac: Screen Recording permission (`.screenRecording`) -> **[WIN] none**. |
| Shared page | In Settings the Screenshot options live on the shared **Screen capture** page together with Screen recording, "Copy text from screen" (OCR) and "Color picker" (those three are other spec parts). The page's first section carries the tool picker + per-tool hotkey; the screenshot-specific sections follow (see 4.9). |

---

### 4.1 Capture entry points and modes

All entry points end in the same pipeline: **(optional countdown) -> selection surface (or direct full-screen grab) -> `route(capture)` (4.5)**.

| Mode | How it starts | Result |
|---|---|---|
| **Area / window chooser** (main mode) | Global hotkey (default off, `⌃⌥⌘4` -> Ctrl+Alt+Shift+4), menu-panel tile, "Capture now" button in Settings, Command Bar | Frozen (or live) selection overlay on every monitor; drag = region, click = window under pointer |
| **Scrolling capture** | Press `S` in the overlay (toggle) then drag a region; or the "Scrolling capture" button in Settings (`captureScrolling()` opens an overlay already in geometry mode with title `Scrolling screenshot`) | Stitched tall image (4.4) |
| **Full screen** | (a) pill button `Full screen` at the top of the overlay, (b) `Enter` in the overlay (display under the pointer), (c) dedicated hotkey (default off, `⌃⌥⌘3`) which captures the display under the pointer **without** showing the overlay | Entire display under the pointer |
| **Window** | Click (press+release moved < 4 px) on a highlighted window in the overlay | That window's own buffer (rounded corners transparent, dialogs attached, 4.3) |
| **Timed / delay** | Setting `Delay` Off/3/5/10 s. A circular countdown HUD (82x82 DIP, ring) shows `3…2…1` once per second before the overlay appears or the direct full-screen grab happens. Pressing the same hotkey again while counting cancels | Applies to the main chooser and to full-screen hotkey; **not** to Recording/Text/Color tools |
| **Repeat last region** | `R` in the overlay (only offered when the last confirmed region's display is still connected; last region is kept for the **app session only**, one per display) | Re-captures that region of the current frozen frame instantly |
| **Freeze screen** | Setting `Freeze the screen while selecting` (default on): all monitors are photographed first and the overlay shows the stills; off = transparent overlay and the pixels are taken after the overlay is hidden (120 ms later) | - |
| **Edit latest screenshot** | Hotkey (default off, `⌃⌥⌘E`) | Opens the editor on the last capture (persisted `LatestScreenshot.png`); HUD `Take a screenshot first` if none/withheld |
| **Edit clipboard image** | Hotkey (default off, `⌃⌥⌘P`) | Opens the editor on the clipboard image (file copied in Finder wins over bitmap data); HUD `Copy an image first` if none |
| **Upload latest screenshot** | Hotkey (default off, `⌃⌥⌘U`; only when "Allow temporary links" is on) | Uploads the latest un-edited capture and copies the link (see 4.12) |
| **Recent captures** | Hotkey (default off, `⌃⌥⌘H`) | History palette (4.13) |
| **Edit image from Shelf** | `ScreenshotService.editImage(at:)` | Opens any readable image file in the editor; unreadable = beep |

Concurrency rules: only one selection session on screen at a time (`isSessionOnScreen`); a second request while a session or a direct capture task is running is ignored; while a **scrolling capture** is running, pressing the capture hotkey again acts as "Done"; a running countdown is cancelled by pressing the hotkey again; during a screen **recording** the chooser is blocked unless the shortcut skips the menu (then only its own tool may open).

#### Unified chooser (mode bar)
When a screenshot hotkey has `Show capture menu when using keyboard shortcut` ON (default), or the capture starts from any button, the overlay also shows a **mode bar** with the available tools in order: `1` Screenshot, `2` Screen recording, `3` Copy text from screen (OCR), `4` Color picker (symbols `camera.viewfinder`, `record.circle`, `text.viewfinder`, `eyedropper`). Digits 1-4 (no modifier) switch tool; tools whose feature is not installed are absent. Switching tools re-photographs only when the new tool needs different pixels (policy: freeze, pointer, hide-own-windows differ). This is the shared "ScreenCapture" chooser; the recording/OCR/color halves are specified in their own parts. Screenshot-only parts follow.

Unified capture policy (`unifiedCapturePolicy`)
| Field | Screenshot | Recording | Text / Color |
|---|---|---|---|
| freeze | setting `screenshotFreeze` | always true | always true |
| includePointer | setting `screenshotIncludePointer` | false | false |
| hideOwnWindows | setting `screenshotHideVorssaintWindows` | false (recorder handles it) | setting |
| keepsContentWindowsOut | setting | true | setting |
| usesGeometry (returns a region, not pixels) | false | true | false |

---

### 4.2 Hotkeys and defaults (global)

All hotkeys default **disabled** (`…Enabled = false`) so nothing is stolen until the user opts in. Key storage format is `"<mods joined by +>:<keyCode>"` (macOS key codes) -> **[WIN]** store `"Ctrl+Alt+Shift+4"` style via the project `Hotkey` class (existing `Hotkey.Parse`).

| Role (title shown in Settings) | Enabled key | Shortcut key | macOS default | [WIN] default |
|---|---|---|---|---|
| `Screenshot` (opens chooser on Screenshot mode) | `screenshotShortcutEnabled` | `screenshotShortcut` | ⌃⌥⌘4 | Ctrl+Alt+Shift+4 |
| `Capture the whole screen` (`fullScreenShortcutTitle`) | `screenshotFullScreenShortcutEnabled` | `screenshotFullScreenShortcut` | ⌃⌥⌘3 | Ctrl+Alt+Shift+3 |
| `Edit latest screenshot` (`editLastCapture`) | `screenshotLastCaptureShortcutEnabled` | `screenshotLastCaptureShortcut` | ⌃⌥⌘E | Ctrl+Alt+Shift+E |
| `Edit clipboard image` (`editClipboardImage`) | `screenshotClipboardShortcutEnabled` | `screenshotClipboardShortcut` | ⌃⌥⌘P | Ctrl+Alt+Shift+P |
| `Upload latest screenshot` (`uploadLastCapture`) | `screenshotUploadShortcutEnabled` (+ requires `screenshotSharingEnabled`) | `screenshotUploadShortcut` | ⌃⌥⌘U | Ctrl+Alt+Shift+U |
| `Recent captures` (`FeatureStrings.recentCaptures.title`) | `recentCapturesShortcutEnabled` | `recentCapturesShortcut` | ⌃⌥⌘H | Ctrl+Alt+Shift+H |
| (sibling, other part) Screen recording | `recorderShortcutEnabled` | `recorderShortcut` | ⌃⌥⌘5 | Ctrl+Alt+Shift+5 |
| (sibling) Copy text from screen | `screenOCRShortcutEnabled` | `screenOCRShortcut` | ⌃⌥⌘T | Ctrl+Alt+Shift+T |
| (sibling) Color picker | `colorPickerShortcutEnabled` | `colorPickerShortcut` | ⌃⌥⌘C | Ctrl+Alt+Shift+C |

Also (Bool, default `true` each): `screenshotShowCaptureMenuOnShortcut`, `recorderShowCaptureMenuOnShortcut`, `screenOCRShowCaptureMenuOnShortcut`, `colorPickerShowCaptureMenuOnShortcut` - when `false` the tool's dedicated hotkey skips the mode bar and starts its own tool directly (and may then run over a recording).

Registration: one hotkey id per role; if `RegisterHotKey` fails the Settings row shows an orange caption (`.caption`, orange) `macOS rejected this shortcut. Choose another one.` -> **[WIN]** `Windows rejected this shortcut. Choose another one.` (the combination is owned by another app). The shortcut recorder rows are the shared `ShortcutPreferenceRow` (other part): "Press keys" waiting title, `Reset`, `None`, conflict text `This shortcut is already used by %@.`.

Legacy migrations (macOS only, do not port): `unifiedScreenCaptureShortcutMigrated`, `restoredScreenCaptureShortcutsMigrated`, `orphanedCaptureShortcutMigrated`, `screenshotOpenEditorDirectly` (-> `screenshotDefaultAction = "edit"`), `migrateUtilityOrderForScreenshot` (inserts `screenshot` first in `panelUtilityOrder`).

---

### 4.3 Selection overlay (the capture surface)

#### 4.3.1 Windows / z-order
- One borderless, topmost, non-activating-style overlay **per monitor** (Mac: `.borderless, .nonactivatingPanel`, level = `CGShieldingWindowLevel`, joins all Spaces). Frame = the monitor's full bounds; it must never be resized by content (a hosting view must not shrink it). Opaque black background when frozen (the still is drawn in a separate image view below the chrome), fully transparent when live (`freeze` off). Panels accept mouse events everywhere even where transparent (WPF: `Background="#01000000"` / `Brushes.Transparent` with hit-testing, or WS_EX_LAYERED + per-pixel alpha with a 1 % fill).
- Cursor: crosshair over the capture area, normal arrow over the clickable chrome (Full-screen pill, mode bar, guide). Set on every pointer event (the app is not active).
- Esc cancels from **any** window (Mac installs a global key monitor) -> **[WIN]** the overlay is activated (`SetForegroundWindow` + `AttachThreadInput` trick or a `WH_KEYBOARD_LL` hook for Esc only while the session is open).
- Own windows (overlays, countdown HUD, scrolling HUD, quick preview) are **always** excluded from the photograph. Editors and pins are excluded when `Hide Vorssaint windows` is on (always excluded for recording). **[WIN]** `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)` (Win10 2004+) on those windows; this removes them from BitBlt, DXGI duplication and Windows.Graphics.Capture. For older builds fall back to hiding the window for the 120 ms of a live capture.

#### 4.3.2 Visual spec (all values from `ScreenshotOverlayView`)
| Element | Spec |
|---|---|
| Dim | Black over everything outside the selection, alpha: frozen = **0.22**, live = **0.18** (when the controls are in the Dynamic Island: 0 until a drag starts, then 0.18). Even-odd fill: full rect minus the selection rounded rect. |
| Selection rect | Rounded rect, corner radius **8** (inner), clear (undimmed) inside. Chrome: outer stroke rect inset -1.5, radius 9, colour `sRGB(0.18,0.55,1.0)` alpha 0.98, width **3**, with a glow (shadow blur 9, same colour alpha 0.55); inner hairline inset -0.5, radius 8, white alpha 0.95, width 1. |
| Size readout (badge) | Text `"{pixelWidth} × {pixelHeight}"` (U+00D7), monospaced-digit semibold **11 pt**, white, on a rounded rect (radius **5**, fill black alpha 0.72, padding 7 h / 3 v), centred under the selection (`midX`, `maxY + 10`), clamped 6 px inside the panel. Values are **device pixels** = selection points x display scale, rounded. |
| Window highlight (hover, no drag) | The window under the pointer is un-dimmed; rounded rect inset 1.25, radius **9**, fill `sRGB(0.35,0.62,1.0)` alpha 0.14, stroke same colour alpha 0.95 width **2.5**. Disabled by setting `Highlight the window under the pointer` (clicking a window still works). |
| Ghost outline of last region | Dashed rect (dash 5/4), white alpha 0.65, width 1, shown while nothing is being dragged, when `Show the last capture outline` is on (default on) and a region exists on this display. |
| Guide bar (standalone screenshot, no mode bar) | Bottom centre, 32 pt above the bottom edge, width `min(680, max(280, screenW-32))` (620 for unified), height 72. Rounded **16** card (regular material + 10 % plate), 1 px stroke primary alpha 0.10, shadow black 0.22 r18 y7, padding 14 h. Left: `viewfinder` icon 17 pt semibold hierarchical secondary in a 28x28 box; then 2-line text: title 13 pt semibold (`purpose` if given, else `Drag to select an area`), subtitle 11 pt secondary: `Click a window to capture it` (with purpose: `Drag to select an area  ·  Click a window to capture it`; when scrolling mode / dragged region required: `Drag only the part of the page that moves.`) + `  ·  ` + `S toggles scrolling` / `Scrolling on` when scrolling is offered. Right: key hints (below). |
| Key hint chip | Height 26, padding 8 h, radius 7, fill primary alpha 0.06, icon 10 pt semibold + label 10 pt semibold rounded, secondary. Hints shown in order: `↩` (`rectangle.inset.filled`) unless scrolling/dragged-only; `S`/`S on` (`rectangle.stack`) if scrolling offered; `Z`/`Z on` (`plus.magnifyingglass`); `C` (`doc.on.doc`) only while the loupe is on; `R` (`rectangle.dashed`) only when repeat is available; `esc` (`xmark`). |
| Guide visibility | Visible only on the display holding the pointer and only while no drag is in progress and no capture pending. |
| Full-screen pill | Capsule, top centre at `max(screen top chrome height, notch controls height) + 12`; label `Full screen` with `rectangle.inset.filled`, `.callout` semibold, padding 14 h, min height 32, regular material, border primary alpha 0.12 (0.34 / 1.5 px on hover), hover scale **1.02** (ease-out 0.12 s), shadow black 0.20 r12 y5. Tooltip `Capture the whole screen`. Shown only for the screenshot tool, when a full region is not required and scrolling mode is off, pointer on this display, no drag. |
| Mode bar (unified) | `HStack` of buttons 116x48 (radius 10, padding 4 in a 14-radius capsule): digit chip 19x19 (radius 5; selected = accent fill, white text, else primary 0.11) + icon 13 pt + title 10 pt; selected fill accent 0.16 and accent 0.48 border; hover fill primary 0.08. Tooltip `"{title} ({digit})"`. A second capsule (height 30) holds the contextual hint (10.5 pt medium secondary): Screenshot: `Drag to select an area  ·  Click a window to capture it  ·  S toggles scrolling`; plus `1–4` key chip (`keyboard`), `↩`, `S`, `Z`, `C`, `R` chips. An `esc` chip (radius 14, height 56 beside the bar / 30 alone). |

#### 4.3.3 Interaction
| Input | Behaviour |
|---|---|
| Mouse move | Updates hover window (z-order = window server order, front-most containing window wins). |
| Press | Starts a drag; hides guide and full-screen pill (`selectionInProgress`). |
| Drag | Selection = rect(origin, current). **Shift** = constrain to square (largest square fitting the drag). **Alt (⌥)** = draw from centre. **Space held** while dragging = move the whole selection without resizing (the origin follows). Selection is clamped to the panel. |
| Release | Distance < **4 px** in both axes = click: captures the hovered window (if allowed). Otherwise selection must be ≥ **2x2** pt or it is discarded. A valid selection immediately captures (no confirm step). Selection stores `lastRegion`. |
| Esc | Cancel session (global). |
| Enter / numpad Enter | Capture the whole display under the pointer (screenshot/text); color tool: pick at the pointer. |
| `R` | Repeat last region (see 4.1). |
| `S` | Toggle scrolling mode (only when scrolling is offered = screenshot tool active in image mode): dim + all clicks now require a dragged region; Enter hint hidden; guide shows `Scrolling on`. |
| `Z` | Toggle the pixel loupe (below). `Settings: Start selection with the magnifier on` auto-enables. |
| `C` | While the loupe is on and not dragging: copy the colour under the pointer in the Colour-picker's configured format without ending the session; the loupe info bar shows `✓ <value>` for **1.4 s**. |
| Arrow keys | While the loupe is on and not dragging: nudge the pointer by exactly **1 device pixel** (Shift = 10) and may cross onto a neighbouring display. **[WIN]** `SetCursorPos` in physical px; refresh overlays manually. |
| `1`-`4` | Switch capture tool (unified chooser only; no Cmd/Ctrl/Alt modifiers). |
| Right click | (Mac: nothing.) **[WIN]** existing `FrozenScreenWindow` cancels on right click - keep as an extra, harmless. |
| Wheel (loupe on) | Loupe zoom (below). |
| Input after a capture started | All pointer events ignored (`isCapturePending`) so a multi-release gesture cannot start a second capture. |

Live (non-frozen) confirm: hide the panels (`ignoresMouseEvents`, flush, order out), wait **120 ms**, photograph the display, crop to the selection in device pixels.

#### 4.3.4 Pixel loupe (magnifier)
- Source: the frozen image of the display (live mode: fetches a fresh snapshot each time `Z` turns the loupe on).
- Sample: a square of source pixels, side = `captureLoupeSampleSide(zoom)` = odd integer `2*floor((13/zoom)/2)+1`, min 3; zoom range **0.5-4.33** (`13/3`), constants: base side 13, min side 3, min zoom 0.5, max zoom 13/3. The sample centres on the **floored** pixel under the pointer (always odd -> a real middle cell); near an image edge the sample slides inward instead of shrinking.
- Frame: **132x132** pt rounded rect (radius 9), nearest-neighbour scaling, placed at pointer + 16 px right/up (flips left/below when it would leave the panel, inset 8). Below it a 6 px gap then a **24 pt** info bar (radius 7, black alpha 0.72, min width = frame, grows to text) with a 12x12 colour swatch (1 px white 0.85 outline), 7 px gap, text 10.5 pt semibold monospaced-digit white: `"<formatted colour>   x, y"` (colour in the colour picker's current format; if no colour `"x, y"`; after `C`: `"✓ <value>"`). x,y are pixel coordinates in the display image.
- Reticle: ring around the target pixel (inset -1) plus four arms from the frame edges to the ring; drawn twice: black alpha 0.76 width 3, then white alpha 0.92 width 1. Frame border white alpha 0.95 width 1.5. A pixel grid (black alpha 0.28, 1 px) is drawn only when `frameSide / sampleSide ≥ 6`.
- Wheel zoom: **Fast** mode multiplies zoom by 1.15 per event (up) or 1/1.15 (down); a discrete (non-smooth) wheel notch applies that factor **three times**; smooth/precision devices apply it per packet. **Step by step** mode moves exactly one odd sample size (±2) per event, snapping to the min/max boundaries. Holding **Alt** temporarily uses the other mode. Wheel delta source order: fixed-point delta, else line delta, else scrolling delta.
- Zoom persistence: `screenshotLoupeLastZoom` is written on every change; initial zoom = last zoom if `screenshotLoupeRememberZoom` else `screenshotLoupeDefaultZoom` (clamped to the range).
- The loupe is hidden while Space-dragging.

#### 4.3.5 Window picking rules
- Candidates: ordinary on-screen windows, front to back, in global top-left coordinates; the window under the pointer is the first whose frame contains it. **[WIN]** `EnumWindows` (z-order) filtered by `IsWindowVisible`, not iconic, not cloaked (`DWMWA_CLOAKED`), not `Progman/WorkerW/Shell_TrayWnd/Windows.UI.Core.CoreWindow`, non-empty title, using `DWMWA_EXTENDED_FRAME_BOUNDS` (visible frame without the invisible resize border) - the current `ScreenGrab.TopLevelWindows` already does this.
- Decoration windows (an untitled window of another process whose frame surrounds its in-front/behind neighbour by an identical 1-32 px margin on all four sides, ±1) are removed so a click on a focus border picks the framed window (`decorationWindowIDs`). **[WIN]** analogous: skip `WS_EX_TRANSPARENT|WS_EX_LAYERED` overlay windows with no title (e.g. Windows' own focus rectangle, third-party border tools).
- Own-process windows: not pickable when `Hide Vorssaint windows` is on or when they are protected workflow windows.
- Attached dialogs (issue "sheet" case): when a clicked window has windows of the **same process**, in front of it and **fully inside its frame**, the capture is a composite: clicked window first then attached windows back to front, on a canvas the size of the clicked window, each layer must cover its frame at one scale (else recaptured through its own filter). If no layer set is valid fall back to the single window. **[WIN]** same rule using owned windows (`GetWindow(GW_OWNER)` chain, same PID, contained): composite via Windows.Graphics.Capture per window, or simply crop the frozen desktop frame to the clicked window's rect **only if** nothing foreign overlaps it.

#### 4.3.6 Notch ("Dynamic Island") integration
When the controls are routed to the notch, the mode bar lives there and the overlay dims 0 until a drag. **[WIN]** not applicable (no notch); keep the overlay-hosted guide.

---

### 4.4 Scrolling capture (`ScreenshotScrollingCapture` + `ScreenshotSupport.scrolling*`)

**Important: Vorssaint never injects scroll events.** The user scrolls the target themselves (wheel, trackpad, scrollbar drag, keyboard); the tool *polls pixels*. Replicate that on Windows (no `SendInput`; an optional "auto-scroll" would be a new feature, flag as out-of-scope).

Flow
1. Start via `S` + drag a region (or Settings button -> drag; title `Scrolling screenshot`, the selection hint `Drag only the part of the page that moves.`). The overlay returns a `scrollingRegion` (display, pixel rect, scale) - no pixels yet.
2. A floating **scrolling HUD** appears top-centre of the pointer's monitor work area (24 pt below the top): rounded **10** regular-material card, padding 14 h / 9 v, spacing 10: 16x16 icon (`rectangle.stack.fill`, accent; a spinner while finishing), 2-line message (12 pt semibold, max width 360) `Scroll with your mouse or trackpad. Press Enter or choose Done.` and below it `"{height} px"` (10 pt medium rounded secondary, monospaced digits, current stitched height), buttons `Cancel` (Esc, small) and `Done` (default/Enter, prominent small, disabled once finishing). The HUD window must own Enter/Esc without activating the target (non-activating but focus-capable) and must be excluded from capture.
3. Pixel polling loop: take a frame of the region every **90 ms** (`sampleInterval`), crop transparent outer rows/columns (`contentFrame`), downsample to 8-bit grey (width ≤ 256, native rows) and compare with the previous accepted sample.
4. Matching (`scrollingTransition`): frames identical (mean abs diff ≤ 1.5 over every 1/8-width column tile, ignoring top/bottom 1/24 rows) = `end/stable`. Otherwise search a vertical offset between `max(2, 1 % of height)` and `min(height-8, 88 % of height)`, both directions, comparing the overlap per column tile (tile width `max(2, w/8)`); a row matches when its summed abs diff ≤ `columns*8`; require a longest run ≥ `max(8, min(28, height/12))` and matching rows ≥ `max(run, compared/3)`; prefer the candidate with the widest contiguous matching column span, then more supporting tiles, then lower mean difference, then longer run. Reject (unmatched) if a rival at a different offset is almost as good (ambiguity tolerance `min(0.75, bestDiff)`). For wide samples (>32 columns) the 32 most-changing columns are used to bound cost. Direction `forward` appends; `backward` is ignored (never duplicates pixels). Sticky footer: rows at the bottom that stay identical frame-to-frame (≥ `max(4, min(12, h/100))` rows, < overlap) are cut from every strip and appended once at the end; the first strip is trimmed of it too.
5. Only the **new pixels** of each advance are retained as a strip (not full frames).
6. Settling: after a change wait `settleInterval` **0.22 s** of stability before accepting; on Done take one more frame and allow a `finishGraceInterval` of **0.85 s**.
7. Limits (`ScreenshotSupport`): **120 s** max duration, **512** frames, **60,000,000** retained pixels and **60,000,000** total output pixels. Hitting a limit ends with result `limited` (HUD `Capture stopped at the safe limit`). A frame that cannot be matched at completion yields `partial` (HUD `The page changed. The completed part was kept.`), which keeps the completed part. A frame read failure ends as `partial`; an unrecoverable error as `failed` (HUD `The screen could not be captured`). `Cancel`/Esc -> HUD `Cancelled.` (icon `xmark`; `mediaCancelled`).
8. Stitch: output width = min slice width, slices drawn top to bottom, no interpolation. Result is an ordinary `Capture` (scale = region scale, anchor = region rect) fed to `route`.

**[WIN]** implementation: capture the region with `BitBlt` from the screen DC (or `Windows.Graphics.Capture` frame pool for GPU-accelerated browsers; BitBlt returns black for some hardware overlays) every 90 ms on a worker thread; implement the matching with `Span<byte>` loops (the Swift uses `vDSP`; use `System.Numerics.Vector<byte>` or plain loops, offsets ≤ ~0.88*h so cost is fine on a 256-column sample). Exclude the HUD by `WDA_EXCLUDEFROMCAPTURE`. Per-monitor DPI: region is in physical px.

---

### 4.5 Post-capture behaviour (`ScreenshotService.route`)

Order of operations for every finished `Capture` {image, scale (pixels per point), anchorRect}:
1. `beginLatestCapture`: previous pending upload is invalidated; `latestCaptureToken` renewed; capture saved to `ScreenshotLastCaptureStore` (`Caches/<bundle>/LatestScreenshot.png`, PNG with DPI = scale*72) **only if** the "Edit latest screenshot" or the upload shortcut is enabled (`retainsLatestCapture`). A `.withheld` marker file means "went through an editor or was discarded" (upload shortcut then refuses; edit shortcut still opens it).
2. Island mascot flash (n/a Windows).
3. Close any open quick preview.
4. `RecentCaptureService.recordScreenshot(capture)` (full PNG + 360-px thumbnail into history).
5. If `Copy to the clipboard automatically` (`screenshotCopyToClipboard`) -> `autoCopy` (silent on success; failure = beep). It copies independently of everything below and also when the default action is Edit.
6. Read `screenshotDefaultAction`:
   - `edit` -> open the editor directly (no preview). Return.
   - otherwise run the action (`save`, `saveAndCopy`, `copy`, or none), then `autoShelve` (if "Add to the shelf automatically" is on and the Shelf feature is installed and on), then decide the preview.
7. Preview decision (`quickPreviewPresentation`): 
   - Action `none` (= "Ask each time") -> always show the preview, auto-dismiss **12 s** (`recoveryPreviewDismissInterval`).
   - Action `save`/`saveAndCopy`/`copy`: if the action fully succeeded -> show the preview only if `Show confirmation preview` (`screenshotPreviewEnabled`, default on), dismiss after `screenshotPreviewDuration` seconds (0 = until dismissed); if the action failed or only half-succeeded (saveAndCopy where copy failed) -> show the recovery preview (12 s) regardless of the toggle.
8. Shared constants: `confirmationPreviewDurations = [1,2,3,5,10,0]`, default **3**; unknown stored values read as 3 (not 0). `allowedDelays = [0,3,5,10]` (unknown -> 0).

Default-action enum `screenshotDefaultAction` (String): `""` (Ask each time), `save`, `saveAndCopy`, `copy`, `edit`. `copiesToClipboard` = copy or saveAndCopy. `withoutCopy`: copy->none, saveAndCopy->save. Toggling the auto-copy switch off also converts the default action with `withoutCopy`; the switch reads ON while either auto-copy or a copying default action is set.

HUD messages (QuickToolHUD, icon `camera.viewfinder`, 1.5 s): copy `Screenshot copied`; save `Saved to %@` (%@ = **name of the destination folder** `lastPathComponent`, not the full path); save+copy `Saved to %@ and copied` (falls back to `Saved to %@` if the clipboard write failed); failure `The screen could not be captured`.

#### 4.5.1 Save destination (`saveDestination`)
1. Base folder = `screenshotSaveFolder` (tilde expanded) if it exists and is a directory, else the **Desktop**. **[WIN]** `KnownFolders.Desktop` (the existing port defaults to `Pictures\Screenshots`; follow the Swift: Desktop, unless the product owner prefers Screenshots - flag in gap list).
2. Optional subfolder from `screenshotSaveSubfolder` pattern (see tokens); created recursively; if creation fails the base folder is used. Slashes become nested folders; empty/`.`/`..` components dropped (cannot escape the base).
3. File name: pattern empty -> `"{prefix} {yyyy-MM-dd} at {HH.mm.ss}.png"` with prefix `Screenshot` (`fileNamePrefix`, localizable), locale `en_US_POSIX` (e.g. `Screenshot 2026-11-02 at 14.05.09.png`); non-empty (trimmed) -> expanded pattern + `.png`.
4. Uniqueness: if the name exists, `name 2.png`, `name 3.png`, … up to 9999.
5. Write atomically; on success mark as screen capture (macOS `com.apple.metadata:kMDItemIsScreenCapture` so the Cleaner treats it like a native screenshot) -> **[WIN]** write an NTFS alternate data stream `:Trayloom.ScreenCapture` (optional, best effort; Cleaner part decides) .
6. A consumed `%#` number is rewound on a failed write and on "Discard" of a just-saved file **only if** nothing advanced the counter since (`rewindNumberSequence`); Discard moves the saved file to the Recycle Bin (`SHFileOperation FOF_ALLOWUNDO`).

Pattern tokens (applied in this order, longer first so `%year` is not eaten by `%y`):
| Token | Value |
|---|---|
| `%year` | 4-digit year |
| `%month` | English month name (`LLLL`, `en_US_POSIX`), e.g. `March` |
| `%y` | 2-digit year |
| `%mo` | 2-digit month |
| `%d` | 2-digit day |
| `%h` | 2-digit hour (24 h) |
| `%mi` | 2-digit minute |
| `%s` | 2-digit second |
| `%#`, `%##`, `%###`… | auto-increment number from `screenshotFileNumberNext`, zero-padded to the count of `#` (min 1) |
File-name patterns additionally replace `/` and `:` with `-` (**[WIN]** also `\ * ? " < > |`). A number is only consumed when the pattern contains `%#` (`screenshotFileNumberNext` then increments and persists). Settings row `Starts at` edits `screenshotFileNumberStart` (0…999,999) and writes it to `…Next` on change; button `Reset` sets Next = Start; label `Next: %d`.

#### 4.5.2 Clipboard (`copyImage`, auto-copy)
- Default (`Copy as image only` OFF): writes a PNG file named like a saved capture into the cache folder (`Caches/<bundle>/Copied Screenshots/`, 0600) and puts **one pasteboard item** with `fileURL` + PNG + TIFF (TIFF's size = point size so a paste lands at on-screen size). Cache pruning after publishing: delete files older than **24 h**, then keep the newest ≤ **100** files and ≤ **256 MB** total (the just-copied file is always preserved; max single file 256 MB).
- `Copy as image only` ON: PNG + TIFF only (no file) - for browsers/chat apps (Chromium treats a fileURL as a file).
- Every write also declares the Vorssaint-source marker so the app's own clipboard history ignores it.
- **[WIN]** clipboard formats: `CF_DIBV5` (alpha-preserving) + registered `"PNG"` format (+ `CF_DIB` fallback); default mode additionally `CF_HDROP` with the cached PNG path (Explorer "paste as file" / chat apps). Register a private format `Trayloom.Source` as the "own write" marker and exclude from cloud clipboard history via `ExcludeClipboardContentFromMonitorProcessing`/`CanIncludeInClipboardHistory` = 0 only if the Clipboard-history part wants it. Cache dir `%LOCALAPPDATA%\Trayloom\Cache\Copied Screenshots`.
- Auto-copy renders off the main thread; before publishing it checks the clipboard sequence number is unchanged (`GetClipboardSequenceNumber`) and the generation matches, otherwise the temp file is removed; a newer capture cancels an older pending copy.
- `Add to the shelf automatically`: the shelf receives a **copy of its own** (PNG bytes + the saved name or default name), not the saved file; only the latest capture's shelf item can be removed by its preview's Discard.

#### 4.5.3 1x downscale
`Save at 1x size` (`screenshotDownscale`, default off): applied to **every** output (save, copy, share, drag, editor export). When `scale > 1`, the final image is resized with high-quality interpolation to `round(pixel / scale)` and exported with scale 1. Caption: `Retina captures are saved at half their pixel size, which makes smaller files.` **[WIN]** keep the string verbatim but replace the platform noun: `High-DPI captures are saved at their logical size, which makes smaller files.` (`[WIN]` Retina -> high-DPI; effective for display scales 125-300 %).

Direct outputs (no editor) use `flatten` = raw pixels with **no** backdrop, no corners, no watermark, no annotations (only the downscale applies).

PNG DPI metadata: `pHYs` = `scale*72` dpi (macOS convention: 72 dpi at 1x). **[WIN]** write `scale*96` dpi so Windows viewers report logical size, and read back scale = dpi/96 (accepted range 0.5…4). Clipboard-image scale inference: `pixel/point` if both axes agree within 5 %, else 1.


---

### 4.6 Quick preview (floating thumbnail after capture)

Panel: borderless, non-activating, level `statusBar`, all Spaces, `transient`, no Cmd-Tab entry; shadow on; fixed **350x210 pt** (**350x268** while a share-link row is shown). Hover pauses auto-dismiss; leaving restarts the full duration. Click outside = close (not while a link is being created). Becomes key (keyboard focus) on show only if `screenshotPreviewTakesFocus` (default true) **and** the preview is not persistent; persistent previews (`dismiss = until dismissed`) never take focus, show an `×` dismiss button on the thumbnail's top-left and close when the island collapses. A click on the panel always hands it focus.

Layout (top to bottom, padding 10, spacing 10, card radius **16**, regular material, 1 px primary 0.10 stroke):
1. **Thumbnail** button (320x138 box, image `scaledToFit`, high interpolation, fill black 0.12, radius 9, 1 px primary 0.12 stroke): click = Edit; **drag** = drag-out of a full-resolution PNG (temp file in its own folder `ScreenshotDrag-<uuid>` kept 1 h; folders from older runs are purged at launch). The image is downscaled to max 1200 px longest side for display. Overlays on the thumbnail: top-left `×` (only persistent), top-right three round 24x24 buttons (regular material circle, 1 px stroke primary 0.12, glyph 11 pt semibold): `Copy text` (`text.viewfinder`; only if the OCR feature is installed), `Share` (`square.and.arrow.up`), `Pin to screen` (`pin`).
2. Optional **shared link row** (48 pt high, radius 9, fill primary 0.055, stroke 0.09): `link` icon, link text 11 pt rounded (truncate middle, selectable), `Expires <relative time>` (caption2 secondary), icon buttons copy (`doc.on.doc`, tooltip `Copy link`) and delete (`trash`, red; spinner while deleting; tooltip `Delete now`).
3. **Action bar** (HStack spacing 5): `trash` (tooltip `Discard  (⌫)`), `qrcode` (only when a QR is detected; accent tint; tooltip `QR code`), then `Save` (`square.and.arrow.down`, `⌘S`), `Copy` (`doc.on.doc`, `⌘C`) - bordered small buttons that are **disabled at 0.4 opacity** if the after-capture action already did that half - then, unless the user disabled temporary links, a link **menu button** (`link` icon; click = upload with the saved default expiry `screenshotUploadDuration`; menu lists `For 1 hour`, `For 6 hours`, `For 24 hours`; tooltip `Temporary links · For 1 hour`; spinner while uploading, tooltip `Creating link…`), spacer, `Edit` (borderedProminent, tooltip `Edit  (⏎)`).
   - Island-embedded variant (28x28 icon-only buttons, adds a share and a pencil button) is **not ported**.
4. Keyboard (only when the panel is key): `Enter`/`E` = Edit; `Esc` = close only (never discards); `Backspace`/`Delete` or `Ctrl+Backspace/Delete` = Discard; `Ctrl+C` copy; `Ctrl+S` save; `Ctrl+W` close. Held keys do not repeat their action.
5. Actions semantic: `Edit` closes the preview then opens the editor on the next run-loop turn and withholds the latest capture from upload; `Pin to screen` creates a pin; `Copy`/`Save`/`Save & Copy` close the preview if they succeed (otherwise it stays and the dismiss timer is rescheduled); `Discard` trashes an already saved file (undoing the number sequence if possible), removes it from the shelf and withholds the latest capture. After a successful system-share the preview closes; if the share sheet is cancelled the timer restarts.
6. Placement (`quickPreviewFrame`): visible frame = screen holding most of the capture's anchor (pointer breaks ties; fallback to pointer screen). `Preview position` `Automatic` (inset 10): to the right of the selection (gap 14) else left, else at the pointer; vertically centred on the selection, else below/above, else at the pointer; always clamped to the visible frame. If a default action ran and position is Automatic the preview sits **bottom right**. Corner positions use inset 16 against the visible frame.
7. QR: after showing, the full-resolution capture is scanned off-thread; if a code is found the `qrcode` button animates in (spring 0.3/0.82). Clicking opens the shared QR result panel and closes the preview.
8. `Copy text` waits for a pending auto-copy to reach the clipboard, then runs OCR on the full-res image and copies the text (HUD `Text copied`).
9. Sharing (system): `Share` writes the capture (as it would be saved, downscale applied) to a temp PNG and opens the OS share UI; **[WIN]** `DataTransferManager` (4.12).
10. Restoring from history (`restorePreview`): same preview with no default action and the 12 s recovery timer; performs no auto copy/save again.

**[WIN] panel**: `Window` with `WindowStyle=None, AllowsTransparency=true, ShowInTaskbar=false, Topmost=true, ShowActivated` per focus setting (`WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW` when it must not steal focus), acrylic/Mica backdrop via `DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE, 3)` on Win11 or a `#CC2B2B2B` solid fallback, rounded 16 by `DWMWA_WINDOW_CORNER_PREFERENCE` or a clip geometry, `WDA_EXCLUDEFROMCAPTURE`. Position against the monitor's work area (`GetMonitorInfo.rcWork`). Drag-out = `DragDrop.DoDragDrop` with `FileDrop` + `Bitmap`.

---

### 4.7 Annotation editor

#### 4.7.1 Window
- One editor window per capture (multiple allowed). Title `Screenshot` (hidden: the title bar is transparent/full-size content). Style: titled, closable, miniaturizable, resizable. Always **dark** appearance. Not movable by background (only the title strip drags the window). Opens centred on the pointer's screen, activates the app.
- Content size: minimum = `(min(vw, min(980, max(760, vw*0.76))), min(vh, min(680, max(560, vh*0.72))))` (visible work area `vw x vh`); initial = image point size scaled to fit (never above 1x) + chrome `(96, 140)`, clamped between that minimum and `(max(min.w, vw*0.90), max(min.h, vh*0.88))`.
- Surface: `#1D1D1D` (white 0.115) with a top-to-transparent white 3.5 % gradient overlay; everything floats over it (no toolbar strips).
- Top band (height 38, margins 10 top / 12 sides): centred brand mark width 40 tint white 0.92 (**[WIN] Trayloom mark, never the Saturn logo**) and the **action cluster** on the right.
- Left: **tool rail**; centre: **canvas area**; bottom: **footer row** (info chip | contextual style bar | zoom chip). Rail left padding 10; band-to-rail gap 6; footer padding 12 sides, 6 top, 10 bottom.
- Close: window close with unsaved changes asks (alert): title `Discard this screenshot?`, message `It was not copied or saved yet.`, buttons `Discard` (first/destructive) and `Cancel`. "Dirty" = any annotation/crop/canvas/backdrop/watermark/shadow/spotlight-dim change since open or since the last export (copy/save/pin/share/drag mark the editor exported = clean). The trash button in the action cluster closes **without** asking. Every final output (Copy, Save, Save As, Add to Shelf) closes the editor; Pin and Share do not.
- Window appear animation: canvas fades/scales in from 0.965 with spring(response 0.32, damping 0.82).
- Text recognition (words for text selection and text-only blur) and QR scan start asynchronously right after the window shows.

#### 4.7.2 Action cluster (top right capsule: padding 9 h/6 v, regular material, radius 12, stroke primary 0.08, shadow black 0.16 r14 y4; 24x24 borderless icon buttons separated by 16 pt dividers with 3 pt padding)
Order left to right:
| # | Control | Icon (SF) | Tooltip / label | Action |
|---|---|---|---|---|
| 1 | Recent captures | `clock.arrow.circlepath` | `Recent captures` | opens history palette (4.13) |
| - | divider | | | |
| 2 | Discard | `trash` | `Discard` | close without asking |
| - | divider | | | |
| 3 | Undo | `arrow.uturn.backward` | - | undo (disabled when empty) |
| 4 | Redo | `arrow.uturn.forward` | - | redo |
| - | divider + QR (only if a QR code is in the capture) | `qrcode` | `QR code` | shows QR result panel; editor stays open |
| - | divider | | | |
| 5 | Pin | `pin` | `Pin to screen  (⌘P)` | pins the current export **without backdrop** and leaves the editor open |
| - | divider | | | |
| 6 | Share | `square.and.arrow.up` | `Share` | system share sheet for the exported PNG; marks exported if a target was chosen |
| 7 | Temporary link menu (only if `screenshotSharingEnabled`) | `link` | `Temporary links` (spinner + `Creating link…` while uploading) | menu `For 1 hour` / `For 6 hours` / `For 24 hours`; on success a sheet `Shared links` shows the link (copy `⌘C`, `Delete now`, `Done`) - the link is **not** auto-copied here |
| - | divider | | | |
| 8 | Save menu button (`.fixedSize()`, tooltip `⌘S`) | text `Save` | primary action = Save; menu: `Save`, `Save As…`, `Add to Shelf` (only when the Shelf feature is installed and on) | |
| 9 | Copy | `Copy` borderedProminent, tooltip `⏎` | copies and closes | |

#### 4.7.3 Tool rail
Vertical rail (padding 5, regular material, radius 15, stroke 0.08, shadow black 0.18 r16 y5). Each tool button 33 pt wide, row height 29 (compact 23), spacing 2 (compact 1); icon 13.5 pt medium; active = accent fill 0.22 + accent foreground + bounce symbol effect; hover = primary 0.08 + scale 1.06 (spring 0.2/0.8); radius 9. A tiny **shortcut badge** (8 pt bold rounded) sits top-trailing (secondary; accent when active; opacity 0.55, 0.9 on hover/active) showing the tool's digit (position 1-9) or its custom key. Tooltip = `"<Tool name>  (<badge>)"`.
Below the tools: divider (22 pt wide) then the **gear** button (`gearshape.fill`, 13 pt) -> popover (arrow edge leading, 340 wide, padding 14) holding the tool-order controls (4.8). Tooltip `Editor tools`.
Overflow: if the rail column is shorter than needed (chrome 15 + rows*(rowHeight+spacing)), rows go compact (23/1), and if still too many the tail tools collapse into a menu button (`chevron.forward.2`, tooltip `More tools`) holding toggle rows `Label(title, icon)` with a checkmark on the active tool. Rail height planned = `min(column, column + footerHeight - 76)` so choosing a tool never reflows the rail.

**Default order (= default digits 1-9; stored `screenshotToolOrder` CSV of raw ids)**: `select,arrow,pixelate,crop,text,sticker,rect,highlight,freehand,line,ellipse,counter,redact,spotlight,expandCanvas` (15 tools; `image` is content-only and never in the rail). Tools 10-15 have no digit unless a custom key is bound. Starting tool = remembered `screenshotLastTool`, but `select`/`crop`/`expandCanvas`/`image` fall back to `arrow`.

| # | Raw id | Title (enUS) | SF symbol | Suggested Segoe Fluent / vector | Default digit | Gesture & options |
|---|---|---|---|---|---|---|
| 1 | `select` | `Select` | `cursorarrow` | `U+E7C9` (TouchPointer)/pointer vector | 1 | Pick/move/resize annotations; drag over recognised text to select words; double-purpose with text selection |
| 2 | `arrow` | `Arrow` | `arrow.up.right` | arrow-up-right vector (`U+E8AD`-like) | 2 | Drag tail->tip; options: arrow style, colour, thickness |
| 3 | `pixelate` | `Blur` (`toolBlur`; the id is historical) | `aqi.medium` | grid/mosaic vector | 3 | Drag a rect; options: Blur style (Pixelate/Blur/Erase), Text only, strength 1-5 |
| 4 | `crop` | `Crop` | `crop` | `U+E7A8` | 4 | Shows crop draft = full image; drag grips (14 px tolerance x scale), drag outside to start a new rectangle, drag inside to move; edges snap to whole pixels; `Crop` (↩) / `Cancel` bar; loupe 72x72 on grip |
| 5 | `text` | `Text` | `textformat` | `U+E8D2` (Font) | 5 | Tap to place; inline field; options: colour, font size |
| 6 | `sticker` | `Sticker` | `face.smiling` | `U+E899` (Emoji) / `U+E76E` | 6 | Tap to place a glyph; menu of 12 stickers |
| 7 | `rect` | `Rectangle` | `rectangle` | rounded-rect outline vector | 7 | Drag; stroke only; colour, thickness |
| 8 | `highlight` | `Highlighter` | `highlighter` | `U+E7E6` | 8 | Drag a rect; multiply blend, colour alpha 0.42 |
| 9 | `freehand` | `Pen` | `scribble.variable` | `U+E76D`/`U+E70F` pencil | 9 | Freehand path, smoothed |
| 10 | `line` | `Line` | `line.diagonal` | diagonal line vector | - | Drag; colour, thickness |
| 11 | `ellipse` | `Ellipse` | `circle` | circle outline | - | Drag; colour, thickness |
| 12 | `counter` | `Number` | `1.circle` | numbered circle vector | - | Tap to drop auto-numbered badges 1…n |
| 13 | `redact` | `Solid block` | `rectangle.fill` | filled rect | - | Drag; opaque fill with chosen colour |
| 14 | `spotlight` | `Spotlight` | `viewfinder` | viewfinder vector | - | Drag light regions; everything else dimmed (slider) |
| 15 | `expandCanvas` | `Expand canvas` | `arrow.up.left.and.arrow.down.right` | `U+E740`/expand vector | - | Drag a grip outward; fill colour choice |
| - | `image` | `Image` (`pastedImage`) | `photo` | - | rail-less | Created by pasting (Ctrl+V); move/resize with handles |

Tool-mode rules at press time: any tool other than select/crop/expand that is pressed on the **currently selected annotation's handle, edge or (for non-spotlight) interior** edits that annotation instead of drawing; spotlight claims only its handles and edge so a drag inside draws another light. Pressing elsewhere deselects.

Tap detection: movement measured in **view points** (7 pt threshold; pen path counts as tap only if it never left the 7 pt radius). A tap with arrow/line/rect/ellipse/highlight/pixelate/redact/spotlight/pen never leaves a degenerate shape: the shape and its undo step are removed and the tap just selects what is under it. Tap with text/sticker/counter on an existing annotation selects it; on empty canvas inside the image creates the item (taps outside the image bounds are ignored).

#### 4.7.4 Contextual style bar (footer, capsule: padding 12 h/8 v, regular material, stroke 0.08, shadow black 0.16 r12 y3)
Groups separated by 16 pt dividers, shown according to the **active tool or the selected annotation's tool** (tool `select` + a picked mark shows that mark's controls and edits it live):
| Group | Shown when | Controls |
|---|---|---|
| Arrow style menu | arrow tool / selected arrow | Menu with a 36x16 template sample image + label; items (inline picker with samples): `Solid`, `Outline`, `Open`, `Double-ended`, `Scribbly`; tooltip `Arrow style` |
| Sticker menu | sticker tool / selected sticker | Shows current glyph (16 pt) + label `Sticker`; menu of 12 glyphs with checkmark: ✅ check, ❌ cross, ⭐️ star, ❤️ heart, 👍 thumbsUp, 👎 thumbsDown, 😀 smile, 😂 laugh, 🎉 party, 🔥 fire, ⚠️ warning, 👀 eyes. **[WIN]** render with Segoe UI Emoji (Color) |
| Blur controls | pixelate tool / selected blur area | (1) Blur-style menu (tooltip `Blur style`) with items `Pixelate` (`aqi.medium`; string `toolPixelate`), `Blur` (`drop.halffull`; `toolBlur`), `Erase` (`eraser`; `blurStyleErase`) - the rail tool itself is titled `Blur` (id `pixelate`); (2) `Text only` toggle button (`character.textbox`; accent fill 0.20 when on; tooltip `Text only`); (3) strength slider 1-5 step 1 width 84, icons `aqi.low` ... `aqi.high`, tooltip `Blur strength` (hidden for Erase) |
| Spotlight slider | spotlight tool / selected spotlight | `sun.max.fill` — slider 0…1 (value rounded to **5 %** steps) width 84 — `moon.fill`; tooltip `Dim outside`; persisted `screenshotSpotlightDim` (default 0.5) |
| Colour dots | arrow, line, rect, ellipse, pen, highlight, text, counter, redact (and selected marks of those; not sticker/image/pixelate/spotlight) | 8 dots 17 pt in 24 pt cells (selected: extra 23 pt ring primary 0.9, scale 1.05, spring 0.22/0.7): red, orange, yellow, green, blue, purple, black, white (sRGB below) |
| Thickness | colour tools except text | 3 capsule glyphs 15 pt wide, heights 1.8/3.4/5.4, in 25x24 cells; selected = accent + accent 0.18 plate; tooltip `Thickness`; widths 2/4/7 image points x scale |
| Font size | text tool / selected text | `textformat.size.smaller` button, menu button showing `"{n} pt"` (11.5 pt monospaced) with inline picker of presets, `textformat.size.larger` button; presets 10, 12, 14, 16, 19, 24, 28, 36, 48, 64, 72, 96; default **19**; label `Font size` |
| Layer buttons | only if ≥ 2 annotations exist | `square.2.layers.3d.bottom.filled` (`Send backward`) and `square.2.layers.3d.top.filled` (`Bring forward`); each dims at its end; disabled without a selection; reorders one step; counters renumber by array order |
| Shadows toggle | always | `circle.lefthalf.filled`, tooltip `Shadows`; accent when on; persisted `screenshotAnnotationShadows` |
| Backdrop button | always | swatch (21x14, radius 4.5) or `sparkles.rectangle.stack` (none) / `photo.fill` (image) + label `Background`; opens the backdrop popover (4.9) |
| Watermark button | always | `signature` + `Watermark`; tinted accent when a mark is active; opens the watermark popover (4.10) |
In crop mode with a draft the bar is replaced by `Cancel` + `Crop` (prominent, tooltip `⏎`); in expand mode by the canvas-fill controls (below).

Colours (sRGB, shared by canvas/export/watermark): red `0.93,0.26,0.21`; orange `1.00,0.58,0.00`; yellow `1.00,0.80,0.00`; green `0.20,0.78,0.35`; blue `0.04,0.52,1.00`; purple `0.69,0.32,0.87`; black `0.09,0.09,0.11`; white `1,1,1`. Remembered: `screenshotLastColor` (default `red`), `screenshotLastStroke` (`small|medium|large`, default `medium`), `screenshotLastTextSize` (19), `screenshotLastBlurLevel` (3; a new editor starts at `max(remembered, 3)`), `screenshotLastBlurStyle` (`pixelate`), `screenshotLastBlurTextOnly` (false), `screenshotLastArrowStyle` (`filled`), `screenshotLastSticker` (`check`).

#### 4.7.5 Footer chips
- **Info chip** (left, capsule, regular material, shadow 0.12 r8 y2): drag-out handle (`arrow.up.doc`, 26x18, tooltip/label `Drag and drop`; drags the flattened export as a PNG file; marks exported) + dimensions label `"{w} × {h} px"` plus `"  @{n}x"` when scale > 1, 11 pt medium monospaced-digit secondary.
- **Zoom chip** (right, radius 9, padding 4): fit button (`arrow.down.right.and.arrow.up.left`, tooltip `⌘0`; active=accent when auto-fit), percent label `"{round(zoom*scale*100)}%"` 10.5 pt, `1:1` button (tooltip `⌘1`, active when zoom == 1/scale). Chip tooltip `⌃ scroll · ⌘+ ⌘-`. **[WIN]** `Ctrl+Wheel · Ctrl+Plus Ctrl+Minus` (Ctrl+0, Ctrl+1).
- If the whole footer does not fit in one row it wraps to two rows (contextual bar on top; info + zoom below); wrapped height constant **76**.

#### 4.7.6 Canvas, zoom, pan, expand
- Canvas fits the available area with margin 14 (64 in expand mode); fit zoom capped at 1 (small captures are *not* enlarged beyond 1 view point per image pixel... in units of points: `zoom = min(fit, 1)` where zoom is view points per image pixel). Zoom range **0.05-3** (view points per pixel). `Ctrl+Wheel`: `1 + clamp(delta,-24,24) * 0.014` factor; `Ctrl+=` x1.25, `Ctrl+-` x0.8, pinch (trackpad) anchored at the gesture-start zoom; `Ctrl+0` = fit, `Ctrl+1` = 1:1 (100 % of device pixels). Scroll bars hidden; panning is the scroll view (two-finger / wheel / shift-wheel). Canvas corner radius: 6 with a backdrop, else `max(4, cardCorner*zoom)`; border primary 0.14; shadows black 0.30 r24 y10 + 0.18 r3 y1. Transparent regions show an 8 px checkerboard (greys 0.7/0.9) when no backdrop is on.
- Cursor: crosshair for drawing tools; open-hand over the selected annotation (when it would be edited); I-beam over recognised text with the select tool; arrow otherwise.
- **Expand canvas**: 8 grips (3.5 pt ring, 1.5 pt blue-white) around the image; press within 10 pt of a grip (grips also pick up presses 10 pt outside the image), drag outward to expand (only outward - a rect never smaller than the image); draft size is validated: integers ≥ 1, each dimension ≤ 60,000,000 and `w*h ≤ 60M`; an invalid draft is rejected with a beep. Live preview (`CanvasExpansionPreview`): new margin filled with the chosen fill (checkerboard for transparent), 1 px blue `sRGB(0.04,0.52,1.0)` outline, existing card keeps its rounded corners, margin extends in whole 8 pt checker tiles. Controls: popup `Color` with `White` / `Transparent` / `Custom color` (colour well appears for Custom) and a live size label `"{w} × {h} px"`. On release the image is expanded (pixels outside filled; existing pixels copied with copy blend, no interpolation); annotations shift by the new origin; spotlight regions are clipped to the old image (dropped if outside); counters and blur areas keep their `renderReferenceSize` of the old image; blur samples and text recognition restart. `Esc` leaves expand mode. Undo restores the previous image.
- **Crop** (apply): rect snapped to whole pixels, minimum **8x8**; image cropped, annotations shifted, recognised words/runs shifted; tool returns to `select`; `Enter` = apply, `Esc` = back to select. Loupe appears only while dragging a resize grip: 72x72 (radius 9, white 0.9 border 1.5, shadow black 0.45 r8 y3), 14-px even sample (the crop *edge* sits between pixels), black crosshair lines (alpha 0.72) + 7 pt ring, placed up-right (gap 14) of the grip or flipped.
- **Text tool**: tap creates a text box and opens an inline field at the box (font = system semibold at `textSize*scale` image pixels, min 11 view points, colour = chosen, black 35 % fill radius 6 with accent 0.7 border, min width 130, padding 6/3, placeholder `Text`). `Enter` or `Esc` commits; an empty new text is discarded (and its undo step with it); editing existing text registers undo only if the text changed. While a field is open all keys belong to it. Text hit box = measured string + 4 px.
- **Counter**: diameter = `max(22, min(w,h)/24)` (image px); fill = chosen colour; white 0.9 ring (width `max(1.5, d*0.05)`, inset 1); numeral bold at `d*0.52` white (dark `#17171C` for the white colour); numbers are re-assigned 1…n in array order after every add/delete/reorder. A canvas expansion freezes each counter's `renderReferenceSize` so its size is unchanged.
- **Sticker**: side = `max(max(52*scale, 0.45*short), min(0.16*short, 128*scale))`, centred on the tap and kept inside the image; glyph drawn at `0.82*min(w,h)`; shadows apply.
- **Highlighter**: multiply blend with colour at alpha 0.42. **Redact**: opaque fill (any of the 8 colours; black default look). **Rectangle/Ellipse/Line/Pen**: stroke width x scale, round joins/caps; pen = quadratic curves through midpoints. **Arrow styles**: `filled` = single closed silhouette (shaft with round tail + head); `outline` closed head+shaft outline; `open` line + open V head; `doubleEnded` heads at both ends; `scribbly` seeded wobble (per-annotation random seed so exports match). Head: preferred length `max(10, 3.4*stroke)`, limited to `max(0.72*len, min(1.2*stroke, len))`, spread π/7.
- **Shadows**: when on, every stroke/text/sticker/counter gets shadow offset (0,-1*scale), blur 3*scale (text: blur 2.5*scale, black 0.55; sticker: blur 3*scale black 0.45), colour black 0.38.
- **Blur areas** (tool `pixelate`): `Pixelate` = mosaic of the base, block = `max(2, round(base * factor))` with `base = max(10, min(w,h)/55)` and factor by level 1:0.4, 2:0.65, 3:1, 4:1.5, 5:2.2, with a per-sample random noise (±small) so identical text never maps to the same values; `Blur` = 4 samples per mosaic block + Gaussian (radius `0.8*block/step`) drawn smooth; `Erase` = fills the area from its surroundings (reads bands 2-8 px wide around the area, inverse-distance blend of opposite sides on a node grid every `max(4, maxSide/128)` px, skips pixels inside recognised text runs, falls back to inside border, inserts under pasted images too). `Text only` limits the effect to recognised text runs intersecting the area (words merged per line, run padding 0.6x height horizontally and 0.3x vertically; a word-gap > 1.5x height starts a new run); until OCR has read **all** bands the whole area is covered so early exports never leak text. Blur always paints from what lies *below* it (pasted images included) and hides the base under it with `copy` blend.
- **Spotlight**: union of lit regions, everything else on the image dimmed black at `dim` (default 0.5) using source-atop (transparency kept); applied once, at the layer position of the topmost spotlight region, so marks above it stay bright; a bare press (no area) dims nothing.
- **Selected-annotation chrome**: dashed selection box (blue `sRGB(0.04,0.52,1.0)` alpha 0.9, dash 4/3 x scale, width 1.5 x scale, inset -3) with 7 pt white round handles on rect-like marks (handle tolerance 12 x scale) and two 8 pt end handles for arrow/line; counters get a box around the badge. Pasted image layers use `imageHandle` (corner handles keep aspect, edge handles stretch one axis, minimum size 1 px, moved on whole pixels; interpolation high).
- Selecting text (OCR words): with `select`, dragging over recognised text highlights words (blue 0.32 fill, rounded 2x scale, padded 1.5x scale); `Ctrl+C` copies the selected text (words joined by spaces, lines by `\n`, order by index) and keeps the editor open, HUD `Text copied`; `Esc` clears the selection first. OCR runs word-level in horizontal bands (max band `min(4096, max(512, 12,000,000/width))` px tall, overlap `min(256, h/4)` rounded even) and de-duplicates words read by two bands at the seam. **[WIN]** `Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages()` with `OcrLine.Words[].BoundingRect`; `OcrEngine.MaxImageDimension` (10,000 px) bounds the band size anyway.

#### 4.7.7 Keyboard (editor window; ignored while a text field/recorder owns the keyboard)
| Key | Action |
|---|---|
| `1`-`9` (no modifier, shortcuts enabled) | Select the tool at that rail position (unless that tool has a custom key) |
| custom per-tool key (no primary modifier required) | Select that tool |
| `Ctrl+C` | Text selection non-empty -> copy text (editor stays); else Copy image & close (ignored for ~0.6 s after pointer activity if the event came from another process = "select-to-copy" utilities) |
| `Ctrl+V` | Paste clipboard image as an image layer (centred, fitted ≤ 1x, whole pixels); beep if none/too big (> 60M px) |
| `Ctrl+S` / `Ctrl+Shift+S` | Save / Save As… |
| `Ctrl+Z` / `Ctrl+Shift+Z` | Undo / Redo (undo limit **60**) |
| `Ctrl+P` | Pin |
| `Ctrl+Backspace/Delete` | Discard & close |
| `Ctrl+0` / `Ctrl+1` / `Ctrl+=` / `Ctrl+-` | Fit / 1:1 / zoom in / zoom out |
| `Backspace`/`Delete` | Delete selected annotation (counters renumber) |
| `Enter` | In crop with draft: apply crop; else Copy & close |
| `Esc` | In expand: leave; crop draft: back to select; selected words: clear; selected annotation: deselect; else close (with confirm if dirty) |
Key repeat is ignored for `Enter`, `Ctrl+C`, `Ctrl+V`. Reserved (cannot be bound to a tool): with Ctrl: C, V, S, Z, P, 0, 1, =, -, Backspace, Delete, W, Q; without a primary modifier: Esc, Return, Enter, Backspace, Delete.

#### 4.7.8 Export pipeline (`renderExport`)
1. Flatten: base image + annotations (in array order) + watermark, in image pixels, all at the capture's scale.
2. Card rounding: radius `round(cornerFactor * min(w,h) * 0.2)`. Without a backdrop the corners become **transparent alpha**; with a backdrop the card is clipped to that radius.
3. Backdrop (if any fill): canvas = image + `2*padding` on each side, `padding = max(24, round(min(w,h) * (0.035 + 0.14*factor)))`; fill drawn; if blur > 0 the *fill only* is Gaussian-blurred with radius `blur * min(canvasW, canvasH) * 0.035` (edge pixels extended); then the card shadow (offset (0,-6*scale), blur 22*scale, black 0.4) under an outline path = max(user corner, the picture's own transparent-corner radius so window captures keep their native corners), then the card.
4. Optional 1x downscale (4.5.3).
5. Output: PNG with DPI metadata; clipboard payload PNG + TIFF (+ file).
6. Wallpaper/solid/gradient/image fills: gradient is linear **top-left -> bottom-right**, two stops; image fill is aspect-fill centred.
Live canvas and export are guaranteed identical by sharing one renderer; annotations never spill into the backdrop margin (clipped to the image rect).

#### 4.7.9 Undo / redo / dirty
Snapshots of `(baseImage, annotations)`; max **60**; every discrete edit (new shape, move/resize first change per drag, delete, reorder, crop, expand, paste, text change) registers one step. Style-only changes (colour/stroke/size/blur/arrow style/sticker) applied to a selected mark mutate it live (no extra undo step beyond the drag one, but they mark dirty). Shadows/backdrop/watermark/spotlight dim mark dirty. Selection after undo is cleared.

---

### 4.8 Tool order & custom tool shortcuts (`ScreenshotToolOrderControls`)

Used in two places: the editor's gear popover (340 wide, padding 14, with a headline) and Settings (section titled `Editor tools`, no headline).
Layout (VStack spacing 10):
1. (popover only) headline `Editor tools` (.headline).
2. Toggle `Use tool shortcuts` (`screenshotToolShortcutsEnabled`, default **on**).
3. Caption (.caption secondary) `Click a shortcut to record a key. Digits 1–9 move the tool. Delete clears its shortcut.`
4. Tool list (spacing 2, divider with 28 pt leading inset between rows), one row per tool in current order, row min height 26: icon (secondary, 20 wide) | title | spacer | optional reset glyph button (`arrow.counterclockwise`, 20x22, radius 6 fill primary 0.045; only if a custom key exists; tooltip `Reset`) | **shortcut recorder** field 86 wide (opacity 0.48 when shortcuts are off; empty title shows the position digit, else `None`; waiting title `Press keys`; no modifier required) | chevron-up button | chevron-down button (20x22 borderless; disabled at the ends; labels `Move up` / `Move down`; reorder animates easeInOut 0.14 and swaps adjacent items).
5. Status text (min height 32, caption): when recording shows `Press the new shortcut Escape cancels. Delete clears.`; on error orange text `"<Tool> · <key>\n<reason>"`; otherwise blank.
6. Trailing button `Reset` (disabled when order is default and no custom keys): restores default order and clears all custom keys.

Rules
- Digits 1-9 are *position slots*: recording a digit moves that tool into that slot (insert at index digit-1) and removes its custom key; clearing (Delete) moves the tool to just after the ninth slot (index 9) unless already there. Digits typed on non-QWERTY layouts are resolved by what the key *types* (AZERTY `&` + Shift = 1, Caps Lock variants); a stored custom key that would type a digit under the current layout/Caps Lock state is suspended without being erased.
- Custom keys: stored in `screenshotToolShortcuts` as CSV `toolId=<key>` (e.g. `arrow=A`), one owner per key (duplicates: the first tool in default order keeps it). Rejections (message, verbatim): `This key belongs to the editor.` (reserved), `This shortcut is already used by %@.` (another tool, another app shortcut role, a window-layout shortcut, or a system shortcut -> `Windows`/`macOS`). Checks run in order reserved -> tool -> role -> window layout -> system.
- Persisted: `screenshotToolOrder` (CSV, default above; unknown/duplicate ids dropped, newly introduced tools appended in canonical order), `screenshotToolShortcuts` (default `""`), `screenshotToolShortcutsEnabled` (true).


---

### 4.9 Backdrop ("Background") popover and model

Button label `Background` (editor footer, see 4.7.4). Popover: width **292**, padding 14, VStack spacing 12: swatch grid / Divider / custom section / Divider / sliders.

**Swatch grid**: 4 columns, spacing 8, cell height 38, radius 8, border primary 0.12 (selected: accent, width 2.5). In order:
1. `None` - slash.circle on primary 0.06 plate (accessibility `None`).
2. Five built-in gradients (accessibility label `Background`), linear top-left -> bottom-right, stops (sRGB):
   | id | start | end |
   |---|---|---|
   | `ocean` | 0.20,0.47,0.96 | 0.45,0.83,0.98 |
   | `sunset` | 0.99,0.36,0.42 | 1.00,0.75,0.35 |
   | `forest` | 0.07,0.56,0.43 | 0.62,0.87,0.50 |
   | `candy` | 0.66,0.32,0.95 | 0.99,0.56,0.65 |
   | `graphite` | 0.23,0.25,0.31 | 0.55,0.60,0.70 |
3. Saved customs (max **12**, newest kept; label `Custom`; context menu `Remove`; image presets show a thumbnail).
4. Current desktop wallpapers (one per monitor, de-duplicated, only existing files; icon `menubar.dock.rectangle` in a 4-px black-0.45 corner badge; label `Wallpaper`). **[WIN]** `SystemParametersInfo(SPI_GETDESKWALLPAPER)`; per-monitor wallpapers via `IDesktopWallpaper::GetWallpaper(monitorId)` (COM `CLSID_DesktopWallpaper`); skip slideshows with no path.
5. `Image…` dashed-border swatch (`photo.badge.plus`) -> file picker (images) -> `kind=image`.

**Custom section**: segmented `Solid` | `Gradient`, up to two colour wells (circle 18, accent ring when active; ring 2 px, padding -3), a `+` button (`plus.circle.fill`, tooltip `Save background`, enabled for solid/gradient/image looks only), and a palette of **20** circles (10 columns, spacing 5, 17 pt, border primary 0.18) that paints the active well (gradient: tap a well to choose which end). Palette (sRGB): `0.96,0.26,0.21`; `1.00,0.58,0.00`; `1.00,0.80,0.00`; `0.55,0.86,0.25`; `0.20,0.78,0.35`; `0.10,0.74,0.61`; `0.15,0.78,0.85`; `0.04,0.52,1.00`; `0.35,0.34,0.84`; `0.69,0.32,0.87`; `1.00,0.45,0.66`; `0.91,0.12,0.39`; `0.55,0.39,0.29`; `0.11,0.16,0.32`; `0.05,0.05,0.06`; `0.25,0.25,0.28`; `0.55,0.55,0.58`; `0.85,0.85,0.87`; `1.00,1.00,1.00`; `0.99,0.93,0.85`. Defaults: solid `0.20,0.47,0.96`; gradient `0.20,0.47,0.96` -> `0.45,0.83,0.98`. Selecting a look keeps the user's slider values.

**Sliders** (label column 64 wide, 12 pt, shrink to 82 %): `Margin` (padding 0…1, default 0.5, disabled without a backdrop), `Corners` (cornerRadius 0…1, default 0 for new, sanitiser default 0.1, always enabled - also rounds the card with no backdrop), `Blur` (0…1, default 0, disabled without a backdrop; blurs only the fill). Label strings: `Margin`, `Corners`, `Blur`.

Model (`BackdropStyle`, JSON in `screenshotBackdropStyle`, default `""` = none): `{ "kind": none|preset|solid|gradient|image, "presetID": "ocean"…, "colors": [[r,g,b]] (1 for solid, 2 for gradient), "imagePath": "...", "padding": 0…1, "cornerRadius": 0…1, "blur": 0…1 }`; the sanitiser clamps sliders (non-finite -> padding 0.5, corner 0.1, blur 0), validates colours (3 finite comps, clamped 0…1) and demotes a broken value to `none`. Presets list in `screenshotBackdropPresets` (JSON array, default `"[]"`, max 12, entries of kind `none` dropped). A missing image file quietly renders as no backdrop. Live canvas scales the fill by `1 + blur*0.08` before blurring so the blur edge never shows.

Maths: margin px = `max(24, round(min(w,h) * (0.035 + 0.14*padding)))`; corner px = `round(corner * min(w,h) * 0.2)`; blur radius = `blur * min(canvasW, canvasH) * 0.035`.

There is **no aspect-ratio/size preset** in the Mac feature; do not invent one.

### 4.10 Watermark popover

Button `Watermark` (`signature`). Popover width 292, padding 14, spacing 12:
1. Segmented `None` (`backdropNone`) | `Text` (`toolText`) | `Image` (`watermarkImageLabel`).
2. Text: single-line field placeholder `Watermark text` (max **120** chars, trimmed on sanitise; auto-focused when empty) + the 8 colour dots (accessibility names `Red`…`White`).
   Image: 28x22 thumbnail (or `photo`), file name (middle-truncated; `None` when empty), button `Choose…`. Choosing `Image` with no picture opens the file picker immediately.
3. Preset grid (shown when a mark is on or presets exist): 4 columns, cells 38 high on a neutral plate (grey 0.5 at 0.35), selected border accent 2.5; text preset shows its text in its colour (11 pt semibold, min scale 0.7); image preset its thumbnail; trailing dashed `+` cell (tooltip `Save watermark`, disabled if nothing to save or the exact look is already saved); context menu `Remove` per preset; max 12 (`screenshotWatermarkPresets`, JSON array).
4. Divider, then placement (disabled while kind = none): `Position` with a 3x3 grid of 22x15 radius-3 cells (selected accent 0.85, else primary 0.10) with names: `Top left`, `Top center`, `Top right`, `Center left`, `Center`, `Center right`, `Bottom left`, `Bottom center`, `Bottom right`; `Size` slider 0…1; `Opacity` slider 0.05…1; `Rotation` slider −90…90 step 1 with a `n°` readout (34 wide, monospaced). Label column 80 wide.

Defaults (`WatermarkStyle`): kind none, text "", colour `white`, anchor `bottomTrailing`, size **0.3**, opacity **0.4**, rotation **0**. JSON in `screenshotWatermarkStyle` (default `""`).
Rendering: font size = `max(8, round(min(w,h) * (0.02 + 0.14*size)))` semibold system; image width = `max(1, round(w * (0.05 + 0.45*size)))` keeping aspect; the mark is drawn as one transparency layer with alpha = opacity (and the shadow when "Shadows" is on), rotated about its centre (positive = up-right), shrunk to fit; placement: the rotated bounding box is positioned on a 3x3 grid inside a margin = `min(0.45*short, max(0.05*short, ceil(r*(1-√½))+1))` (r = card corner radius) so it never crosses rounded corners; drawn **after** annotations, inside the capture (not in the backdrop margin). The mark applies to every output of the editor (copy, save, share, drag) and to the live canvas.

### 4.11 Pinned screenshots (`ScreenshotPinController`)

A pin is a borderless always-on-top floating window showing the image.
- Created by Pin to screen from the preview or editor (editor pins the export **without** backdrop; the preview pins the raw capture). Keeps its own scale.
- Initial size: image point size scaled down to at most **55 %** of `min(screenW, screenH)` of the pointer's visible frame (never up), min **90x60**; centred on that frame; each new pin cascades **+26 px right / 26 px down**, repeating every 5 pins.
- Window: level floating, all Spaces, not in the Cmd-Tab list, shadow on, 7 pt corner radius, 1 px border grey 0.5 alpha 0.4, resizable from edges **keeping the aspect ratio**, min size 90x60, movable by dragging anywhere, can become key (not activating).
- Interactions: drag moves; **double-click closes**; `Esc` or `Ctrl+W` closes; `Ctrl+C` copies the image (HUD `Screenshot copied`); arrow keys nudge **1 px** (Shift = **12 px**); right click opens a context menu:
  - `Copy`
  - `Save As…` (PNG save panel, default name = default dated name)
  - -----
  - `Opacity` submenu: `100%`, `85%`, `70%`, `50%` (checkmark on the current)
  - `Ignore clicks` (checkmark when click-through)
  - -----
  - `Close` (`menuClose`) and `Close all pins`
- **Ignore clicks** = the pin becomes click-through (mouse events pass to windows below). While any pin is click-through a global monitor watches for a left-mouse-down with **Option held** inside a click-through pin and restores interaction (`Alt`-click is the escape hatch); the monitor exists only while needed.
- Pins are excluded from captures when `Hide Vorssaint windows` is on; all pins close when the feature is turned off.
- **[WIN]** `Window{WindowStyle=None, AllowsTransparency=true, Topmost=true, ShowInTaskbar=false}` with `WS_EX_TOOLWINDOW` (no Alt-Tab), `WS_EX_NOACTIVATE` off. Aspect-locked resize: handle `WM_SIZING`. Opacity: `Window.Opacity`. Click-through: toggle `WS_EX_TRANSPARENT|WS_EX_LAYERED` via `SetWindowLongPtr`; escape hatch via a `WH_MOUSE_LL` hook installed only while a click-through pin exists (`Alt`-click inside a pin's rect). Context menu = WPF `ContextMenu`. `Window.AllowsTransparency` + `WDA_EXCLUDEFROMCAPTURE` for the hide-own-windows rule.

### 4.12 Sharing: system share, temporary links, privacy

#### System share
`Share` (`square.and.arrow.up`) on the preview thumbnail and the editor: write the export as a dated PNG into a private temp folder (kept 1 h) and present the OS share UI anchored to the button; the preview/editor treat "target chosen" as exported (closes the preview). **[WIN]** `Windows.ApplicationModel.DataTransfer.DataTransferManager` through `IDataTransferManagerInterop` (`DataTransferManagerHelper.GetForWindow(hwnd)` / `ShowShareUIForWindow(hwnd)`; interop IID `3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8`), target framework `net9.0-windows10.0.19041.0` (or later). In `DataRequested` set `Title = "Screenshot"` and `SetStorageItems([StorageFile.GetFileFromPathAsync(png)])` plus `SetBitmap`. The window handle must be the editor/preview HWND (the preview must be activatable while the share UI is open). Fallback when unavailable: `Process.Start` "Open with…" shell verb `openas` or copy the file path.

#### Temporary links ("Temporary links" section, key `screenshotSharingEnabled`, default **true**)
Behaviour (API contract preserved):
- `POST {endpoint}/v1/screenshots?expiresIn=<seconds>` body = PNG bytes, headers `Content-Type: image/png`, `Accept: application/json`, `Cache-Control: no-store`; expected `201` with JSON `{ "id": "<32 chars [A-Za-z0-9_-]>", "viewPath": "/s/<id>", "expiresAt": "<ISO-8601>", "deleteToken": "<43 chars [A-Za-z0-9_-]>" }`. Link = `{endpoint}/s/<id>`. Accept only if `expiresAt` is in the future and ≤ now + 24 h + 5 min. Statuses 429/503/507 = "unavailable"; other non-201 = "rejected". Body limits: upload ≤ **25 MiB**, PNG signature required, response ≤ 64 KiB.
- `DELETE {endpoint}/v1/screenshots/<id>` with `Authorization: Bearer <deleteToken>`; `204` and `404` both = deleted.
- Expiry durations: `3600`, `21600`, `86400` s (`oneHour`, `sixHours`, `twentyFourHours`); saved default `screenshotUploadDuration` (Int, default 3600).
- Local records (id, endpoint, expiresAt, deleteToken) persisted to `TemporaryScreenshotLinks/records.json` (private, atomic); a link is **not created** unless that file can be written first; expired records are pruned (timer + on wake from sleep); delete failures keep the record. Ephemeral HTTP session (no cookies, no cache, request timeout 75 s, resource timeout 90 s).
- Production endpoint on the Mac is Vorssaint's own server. **Trayloom must not use it**: the endpoint must be a Trayloom-operated service or a user-configurable base URL (HTTPS only, no user/query/fragment/path). Until such a backend exists, **hide the whole "Temporary links" section and every link button** (product decision: flag in gap list); the code path and UI below are specified for when it exists.
- Upload shortcut (`Upload latest screenshot`): shows HUD `Creating link…`, uploads the latest capture that is not withheld (an edited/discarded capture is refused with a beep), copies the URL (`Link copied` HUD; `Link created, but copying failed. Copy it from Temporary links in Settings.` on clipboard failure, retried on the next press for the same capture); if the capture is no longer the latest by the time the response arrives, the link is revoked instead of copied. A pending upload is invalidated when a newer capture arrives or the sharing/shortcut setting is turned off.
- HUDs: `Creating link…`, `Link copied`, `The link could not be created`, `Link deleted`, `The link could not be deleted`.

Settings block (section header `Temporary links`; row order):
1. Toggle `Allow temporary links` (`screenshotSharingEnabled`).
2. (only when on) Toggle `Upload latest screenshot` + shortcut recorder (default ⌃⌥⌘U) ; warning caption `macOS rejected this shortcut. Choose another one.` [WIN] `Windows rejected …` when registration fails.
3. (only when on) Picker `Default link expiry` with `For 1 hour` / `For 6 hours` / `For 24 hours`.
4. (only when on) Caption `Choose 1, 6 or 24 hours when sharing. The image is deleted automatically.`
5. Button `Privacy` (`hand.raised`) -> sheet 560x390: title row (`hand.raised` + `Privacy for temporary links`, title3 semibold; trailing default button `Done`), divider, three icon paragraphs (`photo`, `externaldrive`, `person.2`; 22-wide icon + wrapped text) with the strings `sharePrivacyData`, `sharePrivacyStorage`, `sharePrivacyAccess` (text in 4.14; **[WIN]** replace `Vorssaint` with `Trayloom` and `On this Mac` with `On this PC`).
6. Row `Shared links` (`link`) `n` `>` shown only when there is at least one active link -> sheet 560x360: title `Shared links` + `Done`; list rows: `link` icon, URL (middle-truncated, selectable) + `Expires <relative>`, buttons copy (`doc.on.doc`, `Copy link`), open (`arrow.up.forward.app`, `Open`), delete (`trash`, `Delete now`, spinner while deleting; all disabled while one deletes); empty state `No active links` (`link.badge.plus`); failure alert `The link could not be deleted` (button `Done`).

### 4.13 Recent captures (history)

Service `RecentCaptureService`: last **12** captures (`recentCaptureLimit`) of screenshots and recordings, newest first, total screenshot cache ≤ **256 MB** (oldest screenshots dropped first, but the newest screenshot is always kept; entries without a screenshot file are never size-limited). Entries: `{ id (UUID), kind: screenshot|recording, createdAt, screenshotName "<uuid>.png", recordingPath, thumbnailName "<uuid>-thumbnail.png", scale, anchorX/Y/Width/Height }`. Stored in `Caches/<bundle>/RecentCaptures/` (0700) as `history.json` (sorted keys, 0600) + PNG files (full image with DPI = scale*72; thumbnail ≤ **360 px** longest side). Safety: refuses to overwrite/read through symlinks; an unreadable/missing index with existing cache files refuses to write (never erases); orphaned cache files not referenced by the index are removed after each commit; entries whose files vanished are pruned on load; a recording re-recorded to the same path replaces the older entry. Thumbnails cached in memory (limit 12). Clear bumps a generation so a late recording thumbnail cannot reappear.
**[WIN]** `%LOCALAPPDATA%\Trayloom\RecentCaptures` (ACL: current user only); `System.Text.Json`.

UI (`RecentCapturesView`):
- Header: `Label("Recent captures", clock.arrow.circlepath)` 12 pt semibold; trailing trash button (22x22, tooltip/label `Clear history`, disabled when empty; asks a confirmation dialog titled `Clear history` with destructive button `Clear history` and `Cancel` (`uninstallerCancel`)), and (palette only) a close `xmark.circle.fill` (tooltip `Cancel`).
- Empty state (card, height 92): `Take a screenshot or save a recording to find it here.` (10.5 pt tertiary, centred).
- List: scroll, max height **300**, spacing 7. Row (panel card): 104x68 thumbnail (radius 7, fill black 0.13, 1 px stroke 0.1; fallback icon `photo`/`film` 20 pt) | kind label `Screenshot`/`Recording` (11.5 pt semibold), for recordings the file name (9.5 pt secondary, middle truncated), relative time (9.5 pt tertiary) | buttons: prominent mini `Restore` (screenshots; reopens the same floating preview with the 12 s timer) or `Open` (recordings: opens the file with the default app, 0.12 s after closing the menu panel; a missing file removes the entry + beep) and a bordered mini trash (tooltip `Remove from history`). A screenshot whose file is gone is removed and beeps.
- Entries are filtered by feature availability (screenshot entries need Screenshot installed, recordings need Screen recorder).
- Floating **palette** (`showHistoryWindow`): borderless floating panel 440 wide (content padding 14, HUD backdrop radius 18 high contrast), centred over the key window (else the pointer's screen), clamped 16 px inside the visible frame, fade in 0.12 s; hides on `Esc`, on any click outside (the confirm dialog counts as inside), or when another app activates. Opened from: the history hotkey (`recentCapturesShortcut`, default ⌃⌥⌘H), the editor's clock button, the Command Bar, the menu panel tile.
- Strings (`RecentCaptureStrings`): `Recent captures`, `Take a screenshot or save a recording to find it here.`, `Screenshot`, `Recording`, `Restore`, `Open`, `Remove from history`, `Clear history`.

---

### 4.14 Settings page ("Screen capture" -> Screenshot sections)

`Form` grouped style (macOS). **[WIN]** the shared Settings view builder: sections as cards, captions as small secondary text, `DisclosureGroup` -> `Expander`, segmented -> `ListBox`/`RadioButton` strip.

**Section 1 - header `Screen capture`** (shared; shown whenever any capture tool is installed)
1. Tool picker (only when >1 tool): segmented control with `Screenshot` (`camera.viewfinder`), `Screen recording` (`record.circle`), `Copy text from screen` (`text.viewfinder`), `Color picker` (`eyedropper`); falls back to a popup menu when the segments do not fit. Selecting a tool navigates the page to that tool's options.
2. Toggle labelled with the selected tool's role title (for Screenshot: `Screenshot`) bound to its `…ShortcutEnabled`, followed by the shortcut recorder row (default ⌃⌥⌘4 -> Ctrl+Alt+Shift+4).
3. Toggle `Show capture menu when using keyboard shortcut` (disabled while the shortcut toggle is off; default on).
4. Orange caption on registration failure (4.2).
5. If Screenshot or Recorder installed: toggle `Recent captures` + recorder row (⌃⌥⌘H) + failure caption.

**Section 2 - header `Screenshot`** (anchor `.screenshot`)
1. Two large bordered buttons side by side (control size large, full width): `Capture now` (`camera.viewfinder`) and `Scrolling capture` (`rectangle.stack`).
2. Caption `Capture an area, window or the whole screen`.
3. Toggle `Capture the whole screen` + shortcut row (⌃⌥⌘3) + failure caption.
4. Toggle `Edit latest screenshot` + shortcut row (⌃⌥⌘E) + failure caption.
5. Toggle `Edit clipboard image` + shortcut row (⌃⌥⌘P) + failure caption.
6. Permission row (Screen Recording) when missing - **[WIN] omitted**.

**Section 3 - (no header) capture behaviour**
1. Toggle `Freeze the screen while selecting` (`screenshotFreeze`, true) + caption `The picture stops while you choose the area, so nothing moves away or changes during the selection.`
2. Toggle `Hide Vorssaint windows` (`screenshotHideVorssaintWindows`, true) **[WIN]** `Hide Trayloom windows`.
3. Segmented `Delay`: `Off`, `3 s`, `5 s`, `10 s` (`screenshotDelay`, Int, default 0; format `%d s`).
4. Toggle `Include the pointer` (`screenshotIncludePointer`, false).
5. Toggle `Show the last capture outline` (`screenshotShowLastRegion`, true).
6. Toggle `Highlight the window under the pointer` (`screenshotHighlightWindows`, true) + caption `Turn off the blue window highlight while choosing an area. You can still click to capture a window.`
7. Disclosure group titled `More options` containing:
   - Toggle `Start selection with the magnifier on` (`screenshotLoupeStartsOn`, false)
   - Toggle `Remember the magnifier’s last zoom` (`screenshotLoupeRememberZoom`, false)
   - (hidden while remember is on) Picker `Default magnifier zoom`: `0.5×`, `1×`, `2×`, `4×` (`screenshotLoupeDefaultZoom`, Double 1.0; shown with up to 1 fraction digit + "×")
   - Segmented `Wheel zoom`: `Fast` | `Step by step` (`screenshotLoupeSteppedZoomByDefault`, false) + caption `Hold ⌥ to temporarily use the other mode.` **[WIN]** `Hold Alt to temporarily use the other mode.`
   - Picker `Preview position`: `Automatic`, `Top left`, `Top right`, `Bottom left`, `Bottom right` (`screenshotPreviewPosition`: `""`, `topLeft`, `topRight`, `bottomLeft`, `bottomRight`)
   - Toggle `Focus the preview automatically` (`screenshotPreviewTakesFocus`, true) + caption `Shortcuts work the moment the preview appears, but the keyboard leaves the app you were using until it closes.`
   - Picker `Default action`: `Ask each time`, `Save`, `Save & Copy`, `Copy`, `Edit` (`screenshotDefaultAction`: `""`, `save`, `saveAndCopy`, `copy`, `edit`); caption `Runs automatically right after capture.` (hidden when Edit is chosen); when the action is save/saveAndCopy/copy additionally: toggle `Show confirmation preview` (`screenshotPreviewEnabled`, true), (when on) picker `Confirmation duration`: `1 s`, `2 s`, `3 s`, `5 s`, `10 s`, `Until dismissed` (`screenshotPreviewDuration` Int, default 3, 0 = until dismissed), and caption `After a successful automatic action, keep the preview available for editing or discarding.`

**Section 4 - (no header) output**
1. Toggle `Copy to the clipboard automatically` (`screenshotCopyToClipboard`, false) + caption `Every capture goes to the clipboard as soon as it is taken, ready to paste. Saving a file stays a separate choice.`
2. Toggle `Copy as image only` (`screenshotCopyImageOnly`, false) + caption `Helps images paste into browsers and chat apps. Turn off for apps that need a file path.`
3. Toggle `Add to the shelf automatically` (`screenshotAddToShelf`, false; disabled and reading off while the Shelf feature is off - the stored choice is kept) + caption `Every capture also goes to the shelf, ready to drag into any app. One that opens in the editor goes there with Add to Shelf. Works while the shelf is on.`
4. Row `Save to` | folder display name (secondary, middle-truncated; the Desktop when empty) | `xmark.circle.fill` clear button (tooltip `Reset`, only when set) | button `Choose…` (folder picker: choose folders, can create folders) -> `screenshotSaveFolder` (String path, default `""`). **[WIN]** `Microsoft.Win32.OpenFolderDialog` / `IFileDialog` with `FOS_PICKFOLDERS`.
5. Row `Subfolder pattern` | live expansion preview (secondary) | 150-wide rounded text field (`screenshotSaveSubfolder`, default `""`) ; caption `Optional. Creates dated subfolders inside the folder above using %y, %year, %mo, %month, %d, %h, %mi and %s, for example %y-%mo for 24-03.`
6. Row `File name` | live preview (default-name preview when empty; else pattern + `.png`) | 150-wide field (`screenshotFileNamePattern`, default `""`); caption `Optional. Overrides the default name using the same %y, %year, %mo, %month, %d, %h, %mi and %s tokens, plus %# for an auto-incrementing number (%## pads to 2 digits, %### to 3, and so on).`; when the pattern contains `%#`: row `Starts at` + 70-wide numeric field + stepper (0…999,999) + button `Reset` + trailing `Next: %d`.
7. Toggle `Save at 1x size` (`screenshotDownscale`, false) + caption (see 4.5.3).

**Section 5 - header `Editor tools`** = tool order controls (4.8) without headline.
**Section 6 - header `Temporary links`** (4.12).

All keys are plain UserDefaults today -> **[WIN]** the existing settings store (`Ctx.Settings`, JSON), same key names recommended.

---

### 4.15 Defaults and persistence reference

| Key | Type | Default | Notes |
|---|---|---|---|
| `screenshotShortcutEnabled` / `screenshotShortcut` | Bool / String | false / ⌃⌥⌘4 | opens chooser |
| `screenshotFullScreenShortcutEnabled` / `…Shortcut` | Bool / String | false / ⌃⌥⌘3 | |
| `screenshotLastCaptureShortcutEnabled` / `…Shortcut` | Bool / String | false / ⌃⌥⌘E | |
| `screenshotClipboardShortcutEnabled` / `…Shortcut` | Bool / String | false / ⌃⌥⌘P | |
| `screenshotUploadShortcutEnabled` / `…Shortcut` | Bool / String | false / ⌃⌥⌘U | |
| `recentCapturesShortcutEnabled` / `…Shortcut` | Bool / String | false / ⌃⌥⌘H | |
| `screenshotShowCaptureMenuOnShortcut` | Bool | true | |
| `screenshotFreeze` | Bool | true | |
| `screenshotHideVorssaintWindows` | Bool | true | rename to `…HideOwnWindows` allowed |
| `screenshotSaveFolder` | String | `""` | `""` = Desktop |
| `screenshotSaveSubfolder` | String | `""` | tokens |
| `screenshotFileNamePattern` | String | `""` | tokens |
| `screenshotFileNumberStart` / `…Next` | Int | 1 / 1 | `…Start` 0…999,999 |
| `screenshotDefaultAction` | String | `""` | `""|save|saveAndCopy|copy|edit` |
| `screenshotIncludePointer` | Bool | false | |
| `screenshotShowLastRegion` | Bool | true | |
| `screenshotHighlightWindows` | Bool | true | |
| `screenshotLoupeStartsOn` | Bool | false | |
| `screenshotLoupeRememberZoom` | Bool | false | |
| `screenshotLoupeDefaultZoom` / `…LastZoom` | Double | 1.0 / 1.0 | 0.5…4.33 |
| `screenshotLoupeSteppedZoomByDefault` | Bool | false | |
| `screenshotDownscale` | Bool | false | |
| `screenshotDelay` | Int | 0 | 0,3,5,10 |
| `screenshotLastTool` | String | `arrow` | |
| `screenshotLastColor` | String | `red` | |
| `screenshotLastStroke` | String | `medium` | `small|medium|large` |
| `screenshotLastTextSize` | Int | 19 | |
| `screenshotLastBlurLevel` | Int | 3 | 1…5 |
| `screenshotLastBlurStyle` | String | `pixelate` | `pixelate|blur|erase` |
| `screenshotLastBlurTextOnly` | Bool | false | |
| `screenshotSpotlightDim` | Double | 0.5 | 0…1 |
| `screenshotLastArrowStyle` | String | `filled` | `filled|outline|open|doubleEnded|scribbly` |
| `screenshotLastSticker` | String | `check` | |
| `screenshotAnnotationShadows` | Bool | false | |
| `screenshotToolOrder` | String | default CSV | |
| `screenshotToolShortcuts` | String | `""` | |
| `screenshotToolShortcutsEnabled` | Bool | true | |
| `screenshotBackdropStyle` | JSON String | `""` | |
| `screenshotBackdropPresets` | JSON String | `"[]"` | ≤ 12 |
| `screenshotWatermarkStyle` | JSON String | `""` | |
| `screenshotWatermarkPresets` | JSON String | `"[]"` | ≤ 12 |
| `screenshotOpenEditorDirectly` | Bool | false | legacy; migrated to default action `edit` |
| `screenshotCopyToClipboard` | Bool | false | |
| `screenshotCopyImageOnly` | Bool | false | |
| `screenshotAddToShelf` | Bool | false | needs Shelf installed+on |
| `screenshotPreviewPosition` | String | `""` | |
| `screenshotPreviewTakesFocus` | Bool | true | |
| `screenshotPreviewEnabled` | Bool | true | confirmation preview |
| `screenshotPreviewDuration` | Int | 3 | 1,2,3,5,10,0 |
| `screenshotSharingEnabled` | Bool | true | **[WIN] default false until a backend exists** |
| `screenshotUploadDuration` | Int | 3600 | 3600/21600/86400 |
| `screenshotSharingDeveloperEndpoint` | String | - | developer-only; never exported by settings backup |
| `panelUtilityScreenshot` | Bool | true | menu tile |

Files: `LatestScreenshot.png` (+ `.withheld` marker) in the cache dir; `Copied Screenshots/` cache (24 h / 100 files / 256 MB); `RecentCaptures/history.json` + PNGs; `TemporaryScreenshotLinks/records.json`; temp `ScreenshotDrag-<uuid>/` folders (kept 1 h, purged at launch).

---

### 4.16 Strings (enUS, verbatim from `ScreenshotFeatureStrings.enUS`)

Format strings keep `%@` (string) / `%d` (integer). `[WIN]` rows show the replacement used on Windows; the original is the first value.

| Key | Text |
|---|---|
| pageTitle | Screenshot |
| hubDescription | Captures an area, window or screen and annotates it |
| captureButton | Capture now |
| panelCaption | Capture an area, window or the whole screen |
| fileNamePrefix | Screenshot |
| hintDrag | Drag to select an area |
| hintClick | Click a window to capture it |
| hintFullScreen | Return captures the whole screen |
| hintCancel | Esc cancels |
| hintRepeat | R repeats the last area |
| hintLoupe | Z toggles the loupe |
| freezeToggle | Freeze the screen while selecting |
| freezeCaption | The picture stops while you choose the area, so nothing moves away or changes during the selection. |
| folderLabel | Save to |
| folderChoose | Choose… |
| subfolderLabel | Subfolder pattern |
| subfolderCaption | Optional. Creates dated subfolders inside the folder above using %y, %year, %mo, %month, %d, %h, %mi and %s, for example %y-%mo for 24-03. |
| fileNamePatternLabel | File name |
| fileNamePatternCaption | Optional. Overrides the default name using the same %y, %year, %mo, %month, %d, %h, %mi and %s tokens, plus %# for an auto-incrementing number (%## pads to 2 digits, %### to 3, and so on). |
| fileNumberStartLabel | Starts at |
| fileNumberResetButton | Reset |
| fileNumberNextFormat | Next: %d |
| delayLabel | Delay |
| delayOff | Off |
| delaySecondsFormat | %d s |
| pointerToggle | Include the pointer |
| downscaleToggle | Save at 1x size |
| downscaleCaption | Retina captures are saved at half their pixel size, which makes smaller files. [WIN] High-DPI captures are saved at their logical size, which makes smaller files. |
| editorTitle | Screenshot |
| toolShortcutsTitle | Editor tools |
| toolShortcutsToggle | Use tool shortcuts |
| toolShortcutsCaption | Click a shortcut to record a key. Digits 1–9 move the tool. Delete clears its shortcut. |
| toolShortcutReserved | This key belongs to the editor. |
| toolSelect | Select |
| toolArrow | Arrow |
| arrowStyleLabel | Arrow style |
| arrowStyleFilled | Solid |
| arrowStyleOutline | Outline |
| arrowStyleOpen | Open |
| arrowStyleDoubleEnded | Double-ended |
| arrowStyleScribbly | Scribbly |
| toolLine | Line |
| toolRect | Rectangle |
| toolEllipse | Ellipse |
| toolFreehand | Pen |
| toolHighlight | Highlighter |
| toolSpotlight | Spotlight |
| toolText | Text |
| toolSticker | Sticker |
| toolCounter | Number |
| toolPixelate | Pixelate |
| toolBlur | Blur |
| toolRedact | Solid block |
| toolCrop | Crop |
| toolExpandCanvas | Expand canvas |
| moreTools | More tools |
| pastedImage | Image |
| canvasFillWhite | White |
| canvasFillTransparent | Transparent |
| canvasFillCustom | Custom color |
| textPlaceholder | Text |
| cropApply | Crop |
| cancel | Cancel |
| colorLabel | Color |
| strokeLabel | Thickness |
| fontSizeLabel | Font size |
| blurStrengthLabel | Blur strength |
| spotlightDimStrengthLabel | Dim outside |
| blurStyleLabel | Blur style |
| blurStyleErase | Erase |
| blurTextOnly | Text only |
| shadowLabel | Shadows |
| backdropLabel | Background |
| backdropNone | None |
| editButton | Edit |
| copyButton | Copy |
| saveButton | Save |
| saveAsButton | Save As… |
| pinButton | Pin to screen |
| copyTextButton | Copy text |
| discardTitle | Discard this screenshot? |
| discardMessage | It was not copied or saved yet. |
| discardConfirm | Discard |
| copiedHUD | Screenshot copied |
| savedHUDFormat | Saved to %@ |
| savedAndCopiedHUDFormat | Saved to %@ and copied |
| defaultActionLabel | Default action |
| defaultActionCaption | Runs automatically right after capture. |
| confirmationPreviewToggle | Show confirmation preview |
| confirmationPreviewDurationLabel | Confirmation duration |
| confirmationPreviewUntilDismissed | Until dismissed |
| confirmationPreviewCaption | After a successful automatic action, keep the preview available for editing or discarding. |
| defaultActionNone | Ask each time |
| defaultActionSaveAndCopy | Save & Copy |
| captureFailed | The screen could not be captured |
| pinOpacity | Opacity |
| pinClickThrough | Ignore clicks |
| pinCloseAll | Close all pins |
| backdropPaddingLabel | Margin |
| backdropWallpaperLabel | Wallpaper |
| backdropImageButton | Image… |
| backdropSolidLabel | Solid |
| backdropGradientLabel | Gradient |
| backdropCornersLabel | Corners |
| backdropSavePreset | Save background |
| backdropDeletePreset | Remove |
| backdropCustomLabel | Custom |
| backdropBlurLabel | Blur |
| openEditorToggle | Open the editor right after capturing *(legacy, no UI)* |
| openEditorCaption | The capture skips the floating preview and opens ready to annotate. *(legacy)* |
| autoCopyToggle | Copy to the clipboard automatically |
| autoCopyCaption | Every capture goes to the clipboard as soon as it is taken, ready to paste. Saving a file stays a separate choice. |
| copyImageOnlyToggle | Copy as image only |
| copyImageOnlyCaption | Helps images paste into browsers and chat apps. Turn off for apps that need a file path. |
| addToShelfToggle | Add to the shelf automatically |
| addToShelfCaption | Every capture also goes to the shelf, ready to drag into any app. One that opens in the editor goes there with Add to Shelf. Works while the shelf is on. |
| addToShelfButton | Add to Shelf |
| lastRegionToggle | Show the last capture outline |
| highlightWindowsToggle | Highlight the window under the pointer |
| highlightWindowsCaption | Turn off the blue window highlight while choosing an area. You can still click to capture a window. |
| scrollingCaptureButton | Scrolling capture |
| scrollingCaptureTitle | Scrolling screenshot |
| scrollingCaptureProgressHUD | Scroll with your mouse or trackpad. Press Enter or choose Done. |
| scrollingCaptureHintOff | S toggles scrolling |
| scrollingCaptureHintOn | Scrolling on |
| scrollingCaptureTooLongHUD | Capture stopped at the safe limit |
| scrollingCaptureCaption | Choose an area, scroll with your mouse or trackpad, then press Enter or Done to join it. |
| scrollingCaptureSelectionHint | Drag only the part of the page that moves. |
| scrollingCapturePartialHUD | The page changed. The completed part was kept. |
| uploadLastCapture | Upload latest screenshot |
| uploadExpiry | Default link expiry |
| editLastCapture | Edit latest screenshot |
| lastCaptureMissing | Take a screenshot first |
| fullScreenCaptureButton | Full screen |
| fullScreenShortcutTitle | Capture the whole screen |
| bringForward | Bring forward |
| sendBackward | Send backward |
| shareSectionTitle | Temporary links |
| shareCaption | Choose 1, 6 or 24 hours when sharing. The image is deleted automatically. |
| shareButton | Share |
| shareOneHour | For 1 hour |
| shareSixHours | For 6 hours |
| shareTwentyFourHours | For 24 hours |
| sharingHUD | Creating link… |
| sharedHUD | Link copied |
| shareFailedHUD | The link could not be created |
| linkCopyFailedHUD | Link created, but copying failed. Copy it from Temporary links in Settings. |
| sharedLinksTitle | Shared links |
| sharedLinksEmpty | No active links |
| expiresLabel | Expires |
| deleteLink | Delete now |
| linkDeletedHUD | Link deleted |
| deleteFailedHUD | The link could not be deleted |
| openLink | Open |
| copyLink | Copy link |
| done | Done |
| sharePrivacyButton | Privacy |
| sharePrivacyTitle | Privacy for temporary links |
| sharePrivacyData | Vorssaint sends only the rendered image and the expiration you choose. It does not send your name, account, device identifier or MAC address. On this Mac, it keeps only the link, expiration and private deletion token while the link is active. [WIN] Trayloom sends only … On this PC, it keeps … |
| sharePrivacyStorage | Network providers and the service temporarily process your public IP to prevent abuse. The image and its link metadata are permanently deleted when you delete the link or its time ends. The service does not create screenshot backups. |
| sharePrivacyAccess | Anyone with the link can view, download, save or redistribute the image. Active links are available to the service operator for abuse moderation. Share only with people you trust. |
| previewPositionLabel | Preview position |
| previewPositionAutomatic | Automatic |
| previewPositionTopLeft | Top left |
| previewPositionTopRight | Top right |
| previewPositionBottomLeft | Bottom left |
| previewPositionBottomRight | Bottom right |
| previewFocusToggle | Focus the preview automatically |
| previewFocusCaption | Shortcuts work the moment the preview appears, but the keyboard leaves the app you were using until it closes. |
| hideVorssaintWindowsToggle | Hide Vorssaint windows [WIN] Hide Trayloom windows |
| shareEnabledToggle | Allow temporary links |
| editClipboardImage | Edit clipboard image |
| clipboardImageMissing | Copy an image first |
| showCaptureMenuOnShortcut | Show capture menu when using keyboard shortcut |
| screenCaptureTitle | Screen capture |
| dragOutHandleLabel | Drag and drop |
| loupeStartsOnToggle | Start selection with the magnifier on |
| loupeRememberZoomToggle | Remember the magnifier’s last zoom |
| loupeDefaultZoomLabel | Default magnifier zoom |
| loupeWheelZoomLabel | Wheel zoom |
| loupeZoomFast | Fast |
| loupeZoomStepped | Step by step |
| loupeZoomOptionCaption | Hold ⌥ to temporarily use the other mode. [WIN] Hold Alt to temporarily use the other mode. |
| watermarkLabel | Watermark |
| watermarkImageLabel | Image |
| watermarkTextPlaceholder | Watermark text |
| watermarkPositionLabel | Position |
| watermarkSizeLabel | Size |
| watermarkOpacityLabel | Opacity |
| watermarkRotationLabel | Rotation |
| watermarkSavePreset | Save watermark |
| watermarkColorRed…White | Red, Orange, Yellow, Green, Blue, Purple, Black, White |
| watermarkPositionTopLeading / Top / TopTrailing | Top left / Top center / Top right |
| watermarkPositionLeading / Center / Trailing | Center left / Center / Center right |
| watermarkPositionBottomLeading / Bottom / BottomTrailing | Bottom left / Bottom center / Bottom right |

Shared strings used by this feature (other files, enUS): `Reset` (shortcutReset), `None` (shortcutNone), `Press keys` (shortcutPressKeys), `Press the new shortcut` / `Escape cancels.` / `Delete clears.` (shortcutRecording/EscapeHint/DeleteHint, joined by spaces), `Nothing was captured. macOS or another app already uses that combination. Try another one.` [WIN] `…Windows or another app…` (shortcutNotCaptured), `This shortcut is already used by %@.` (shortcutConflictFormat), `macOS rejected this shortcut. Choose another one.` [WIN] `Windows rejected …` (shortcutUnavailable), `Close` (menuClose), `Cancelled.` (mediaCancelled), `Shelf` (shelfName), `QR code` (qrResultTitle), `Text copied` (ocrCopied), `Cancel` (uninstallerCancel), `Move up` / `Move down`, `More options` (recorder strings moreOptions), `Copy text from screen` (ocrName), `Color picker` (colorPickerName), `Choose what to record` (recorder selectionPurpose).


---

### 4.17 Windows mapping (implementation guide)

#### 4.17.1 Capture back-ends
| Need | Windows API / approach | Notes |
|---|---|---|
| Per-monitor DPI | App manifest `PerMonitorV2`; `GetDpiForMonitor(MDT_EFFECTIVE_DPI)`; scale = dpi/96 per monitor | A capture's `scale` is the monitor's scale (1, 1.25, 1.5, 2…). Mixed-DPI desktops: one overlay window per monitor positioned with `SetWindowPos` in physical pixels (a single virtual-screen WPF window, as in `FrozenScreenWindow`, mis-scales on mixed DPI). |
| Full-display / region (freeze + live) | `BitBlt(CAPTUREBLT)` from the screen DC per monitor (`EnumDisplayMonitors`), BGRA -> `WriteableBitmap`; or `Windows.Graphics.Capture` (`GraphicsCaptureItem.TryCreateFromDisplayId` / `IGraphicsCaptureItemInterop.CreateForMonitor`, `Direct3D11CaptureFramePool`, one frame) | BitBlt is instant, shows no yellow border and honours `WDA_EXCLUDEFROMCAPTURE` (Win10 2004+), but returns black for protected / hardware-overlay content. Use WGC when the BitBlt result is all black; DXGI Desktop Duplication (`IDXGIOutputDuplication`) is a third fallback (no border, no pointer). HDR displays: capture the SDR composition or tone-map float16 to sRGB. |
| Pointer in image | `GetCursorInfo` + `DrawIconEx` at hotspot-adjusted position into the bitmap (BitBlt never includes it); WGC: `GraphicsCaptureSession.IsCursorCaptureEnabled = true` | Option `Include the pointer`. |
| Yellow capture border | `Windows.Graphics.Capture` draws a yellow border on most builds (can be disabled with `GraphicsCaptureSession.IsBorderRequired = false` only when the app has the borderless-capture capability, Win11 22H2+). Because the border would end up in/around the picture, use WGC only as a fallback or for single windows; BitBlt/DXGI never show it. |
| Own-window exclusion | `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE = 0x11)` on overlays, HUDs, preview, (editors & pins when "Hide Trayloom windows"). Re-apply when the setting changes. | Pre-2004 Windows: hide windows for the live capture. |
| Single window | Preferred: `Windows.Graphics.Capture` `CreateForWindow(hwnd)` (captures occluded, off-screen & layered windows crisply; Win11 returns rounded corners with alpha); fallback `PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT=0x2)`; last fallback: crop the frozen desktop frame to `DWMWA_EXTENDED_FRAME_BOUNDS`. | For a crop-based fallback on Win11 apply a corner mask of radius 8 px x scale (`DWMWA_WINDOW_CORNER_PREFERENCE`/`DWMWCP_ROUND`), 0 for maximised/snapped/Win10 windows, so backdrop corner detection (`transparentCornerRadius`) behaves like macOS. Remove the DWM drop shadow (extended frame bounds already exclude it). |
| Window enumeration | `EnumWindows` (z-order) + filters in 4.3.5; `DwmGetWindowAttribute(DWMWA_CLOAKED, DWMWA_EXTENDED_FRAME_BOUNDS)`; `GetWindow(GW_OWNER)` for attached dialogs | Existing `ScreenGrab.TopLevelWindows` is a good base; add owner/PID. |
| Scrolling capture | Repeated region `BitBlt` (or WGC frame) every 90 ms on a worker thread + the matcher of 4.4. **No `SendInput`.** | Optional future "auto-scroll" would use `SendInput(MOUSEEVENTF_WHEEL, WHEEL_DELTA*n)` with a 150-250 ms settle per step - not part of the Mac feature. |
| Global hotkeys | `RegisterHotKey` (existing `BindHotkey` helper) | Win key never used. |
| Esc during overlay | Overlay window is foreground; `WH_KEYBOARD_LL` hook only as safety net | |
| Overlay | Per-monitor borderless topmost `Window` (`AllowsTransparency=false` when frozen; `WS_EX_TOOLWINDOW`; use `WS_EX_NOACTIVATE` only for the always-visible HUDs). Cursor `Cursors.Cross` (the crosshair); arrow over chrome. | |
| Loupe pixel source | The frozen `BitmapSource` (live mode: one fresh BitBlt on `Z`); draw via `RenderOptions.BitmapScalingMode=NearestNeighbor` on a `CroppedBitmap` | |
| Magnifier wheel | `MouseWheel` `Delta` (120 = one notch, <120 = precision touchpad); fast = x1.15^3 per discrete notch, x1.15 per precision packet | |
| Cursor nudge | `SetCursorPos` (physical px) | |

#### 4.17.2 Rendering / imaging
- Keep all document geometry in image pixels. The editor canvas: a WPF `Canvas`/`DrawingVisual` host scaled with a `ScaleTransform` (view points per image pixel), `RenderOptions.EdgeMode`/`BitmapScalingMode=NearestNeighbor` when zoom ≥ 2 and high-quality below.
- Export compositing must be pixel-exact and shared with the live view: build one `ScreenshotRenderer` that draws into a `DrawingContext` (vector marks, text with `FormattedText`, shadows via `DropShadowEffect`-equivalent geometry or pre-rendered blur) and use `RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32)` for the flattened result. Operations WPF's DrawingContext cannot express need pixel loops on a `WriteableBitmap` (premultiplied BGRA): **multiply** blend for the highlighter (`out = dst*src` per channel with src = colour premultiplied by 0.42 alpha), **source-atop** dim for the spotlight (`dst.rgb *= (1-dim)` keeping alpha), **copy** blend for blur/erase areas, rounded-alpha clip for card corners.
- Preferred option: use **SkiaSharp** (`SKCanvas`, `SKBlendMode.Multiply/SrcATop/Src`, `SKImageFilter.CreateBlur`, `SKMaskFilter`/drop shadow) for the renderer; it is a new NuGet -> **ask first** (not in ARCHITECTURE.md's allowed list). Without it use the WPF + `WriteableBitmap` approach above.
- Text: Segoe UI Variable (fallback Segoe UI) **SemiBold** at `textSize*scale` px (pixels, not points: `FormattedText` size in DIP at 96 dpi = px). Counter numerals Segoe UI Bold. Stickers: `Segoe UI Emoji` (Color). Watermark text SemiBold.
- Gaussian blur: downsample to the block sample, then a separable Gaussian (or 3 box blurs) on the small bitmap, radius `0.8*block/step`; Pixelate: downsample with `BitmapScalingMode.Linear` (medium) to `w/block x h/block`, add ±noise per sample (random per sample, bytes shifted by a small random amount), draw upscaled with nearest-neighbour. Gaussian blur of the backdrop uses radius `blur*min(w,h)*0.035` with edge extension (`ClampToEdge`).
- PNG: `PngBitmapEncoder` writes DPI from `BitmapSource.DpiX/DpiY` (set `scale*96`). TIFF not needed on Windows.
- Memory guards: reject/clip anything above 60,000,000 pixels for expand-canvas, paste and scrolling output.
- OCR: `Windows.Media.Ocr.OcrEngine` (`TryCreateFromUserProfileLanguages`; fall back to `en-US` pack). Convert `OcrWord.BoundingRect` (image px, top-left) to `BandWord` with line index = `OcrLine` index; apply the band de-duplication in 4.7.6; if no OCR language pack is installed the editor still works but "Text only" covers the whole area and word selection is unavailable.
- QR: no built-in Windows API -> `ZXing.Net` (**ask first**) or omit the QR button.
- Recycle bin on discard: `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin)` or `SHFileOperation`.
- HUD / confirmation toasts: reuse the shared HUD (the Quick tools part) - parameters used here: top centre of the pointer's monitor work area, 24 DIP from the top, regular-material card radius 10, padding 14 h/9 v, icon 13 pt accent semibold + message 12 pt semibold (2 lines, max width 360), fade in 0.12 s, visible **1.5 s**, fade out 0.22 s; countdown variant: 82x82 circle, number 32 pt bold rounded, ring 3 px accent (progress `1 - elapsed/0.92`, the ring starts at 4 % and sweeps 92 %), visible 0.92 s.
- Sounds: none (macOS beep on failure -> `SystemSounds.Beep.Play()`).

#### 4.17.3 Windows-specific substitutions of text/behaviour
| macOS notion | Windows |
|---|---|
| Screen Recording permission | none; remove permission rows and the "Permission required" tile caption |
| ScreenCaptureKit | BitBlt / WGC / DXGI (4.17.1) |
| Retina | high-DPI display scale; `Save at 1x size` = save at logical size |
| ⌘ / ⌥ / ⌃⌥⌘ | Ctrl / Alt / Ctrl+Alt+Shift |
| Finder cut/copy of a file onto the clipboard wins over image data (Edit clipboard image) | `Clipboard.GetFileDropList()` containing one image file wins over `Clipboard.GetImage()` |
| Desktop save default | `Environment.SpecialFolder.DesktopDirectory` |
| Trash | Recycle Bin |
| Spaces (`canJoinAllSpaces`) | n/a - windows already span virtual desktops with `WS_EX_TOOLWINDOW`; for pins/preview use `VirtualDesktopManager`-agnostic topmost windows (they follow only if pinned; optional `IVirtualDesktopManager.MoveWindowToDesktop`) |
| Dock/Cmd-Tab hiding | `ShowInTaskbar=false` + `WS_EX_TOOLWINDOW` |
| Dynamic Island / notch controls | not ported |
| NSSavePanel | `Microsoft.Win32.SaveFileDialog` (filter `PNG image|*.png`, default name = default dated name) |
| NSSharingServicePicker | `DataTransferManager` (4.12) |
| QuickLook-like drag out | `DragDrop.DoDragDrop` with `DataObject` `FileDrop` |
| Window shadows on cards | `DwmSetWindowAttribute` Win11 corner rounding; `Effect=DropShadowEffect` |

#### 4.17.4 Threading / lifetime
- Capture + stitching on a worker thread; matcher cancellable (`CancellationToken` checked per offset).
- Auto-copy/auto-shelf tasks tied to a generation counter; a newer capture cancels older work; late results are dropped.
- Everything is torn down when the feature is turned off: countdown, selection session, direct capture task, auto-copy, scrolling task + HUD, uploads, preview, editors, pins, hotkeys, latest-capture store.
- Only one capture session at a time across Screenshot / OCR / Color / Recording (shared `IsSessionOnScreen` flag; a leaked flag must default to *allowing* a capture).

---

### 4.18 Gap vs Trayloom (as of `src/Trayloom.Features.Tools/Screenshot/*`, `Support/FrozenScreenWindow.cs`, `Support/ScreenGrab.cs`)

Current port summary: `ScreenshotFeature` has 3 actions (region/window pick via `RegionSelectorWindow`, all screens, active window) with default hotkeys Ctrl+Alt+S / Ctrl+Alt+Shift+S / none; settings: 3 hotkeys, `openEditor`, `copyOnCapture`, `saveOnCapture`, `folder` (default `Pictures\Screenshots`, file `Trayloom-yyyyMMdd-HHmmss.png`). Editor `ScreenshotEditorWindow`: 6 tools (Arrow, Box, Pen, Text, Blur, Crop), 6 colours, 3 widths (3/6/10 px), Undo, Copy, Save, Save as…, Ctrl+Z/C/S, Esc; no zoom, no backdrop, no layers/selection, plain WPF chrome. `FrozenScreenWindow` = single topmost window over the whole virtual screen using a BitBlt still, dim alpha 120, white 1.5 px selection rectangle, blue window hover (`#4F8CFF`, 3 px, fill 40), size label, text hint, Esc/right-click cancel. `ColorPickerWindow` already has a loupe (reuse ideas).

Prioritised checklist

P0 - structure / correctness (blocks "exact replica")
1. [ ] Replace the 3-action feature with the Mac model: main chooser (area/window/full-screen pill/Enter), dedicated hotkeys (4.2 table), "Capture now" + "Scrolling capture" buttons, key names/defaults per 4.15. Rename keys `hotkeyRegion/hotkeyScreen/hotkeyWindow/openEditor/copyOnCapture/saveOnCapture/folder` -> Mac key names (migrate old values).
2. [ ] Per-monitor DPI-correct overlay windows (one per monitor), 0.22/0.18 dim, rounded 8 selection with glow, device-pixel size badge `W × H`, window highlight style (rounded 9, 2.5 px) and Shift (square) / Alt (from centre) / Space (move) modifiers; guide bar (bottom, 680x72) with key-hint chips instead of the top text hint; Enter = capture display; `R`, `S`, `Z`, `C`, arrows; ghost outline of last region.
3. [ ] Pixel loupe (132 pt, grid, reticle, info bar, fast/stepped zoom, remember zoom, starts-on) in the region selector (currently only in `ColorPickerWindow`).
4. [ ] Post-capture pipeline: quick preview panel (350x210, actions, drag-out, hover pause, positions), `Default action` (none/save/saveAndCopy/copy/edit) with confirmation preview (+duration), auto-copy (file + image / image only), auto-shelf, 12 s recovery preview, `Discard`→Recycle Bin. Today `Deliver` either opens the editor or silently copies/saves with a toast.
5. [ ] Save-location engine: folder (Desktop default), subfolder tokens, file-name tokens `%year %month %y %mo %d %h %mi %s %#`, numbering Start/Next/Reset, unique `name 2.png`, default name `Screenshot yyyy-MM-dd at HH.mm.ss.png`, HUD `Saved to <folder>`.
6. [ ] Settings page rebuilt to the layout in 4.14 (sections, captions, disclosure `More options`, delay segmented, pickers, shortcut rows with "rejected" warning). The current `SettingsViewBuilder` page has 3 hotkeys + 3 toggles + a folder box.
7. [ ] Editor rewritten to the Mac layout: dark artboard, top band (brand mark + action cluster), left tool rail with 15 tools + gear popover, canvas with zoom/pan, bottom style bar + info chip + zoom chip, dirty-close confirmation, 60-step undo/redo.

P1 - editor features missing entirely
8. [ ] Tools: Select (move/resize/delete annotations, handles, layer order), Line, Rectangle, Ellipse, Highlighter (multiply 0.42), Spotlight (+dim slider), Sticker (12), Number counter, Solid block, Expand canvas, Image layer (paste), Pixelate/Blur/Erase + Text only + strength 1-5. Existing Arrow/Box/Pen/Text/Blur(pixelate)/Crop need to match specs (arrow styles ×5, 8 colours, 3 strokes 2/4/7, text presets 10-96 pt default 19, shadows toggle, crop with pixel snapping + loupe + Enter/Esc).
9. [ ] Tool-order & per-tool shortcut customisation (4.8) incl. digits 1-9 slots, recorder, reserved/conflict messages, Reset.
10. [ ] Backdrop (5 gradients, solid, gradient, wallpaper, image, 12 saved, margin/corners/blur sliders) and Watermark (text/image, 9 anchors, size/opacity/rotation, 12 presets) with persisted JSON; export pipeline with card rounding/shadow.
11. [ ] Text recognition in the editor (selectable words, Ctrl+C copies text, text-only blur) via `Windows.Media.Ocr`; QR detection (needs ZXing - ask first).
12. [ ] Zoom (0.05-3, Ctrl+wheel, Ctrl+0/1/+/-, 1:1, fit), checkerboard for transparency, drag-out handle, dimension chip `W × H px @nx`.
13. [ ] Pinned screenshots (always-on-top, aspect-locked resize, opacity 100/85/70/50, click-through + Alt-click escape, copy/save, double-click close, arrows nudge, Close all).
14. [ ] Scrolling capture (S / button): pixel-polling matcher + HUD (Cancel/Done, height counter) + limits + partial/limited HUDs. Not present at all.
15. [ ] Freeze toggle (live mode = hide overlay, wait 120 ms, grab), Delay countdown (0/3/5/10 s ring), Include pointer, Hide own windows (`WDA_EXCLUDEFROMCAPTURE`), Highlight windows toggle, last-region ghost, repeat-last (R).
16. [ ] Mode bar of the unified chooser (1-4 tools) once OCR/Recorder/Color parts exist; "Show capture menu when using keyboard shortcut" per tool.

P2 - secondary / ecosystem
17. [ ] Recent captures history (12 items, 256 MB cap, palette 440 wide, hotkey, Restore/Open/Remove/Clear) + cache of latest capture, `Edit latest screenshot`, `Edit clipboard image`.
18. [ ] Share: Windows share sheet (`DataTransferManager` with HWND interop), copy-as-file + `PNG`/DIB formats (`CopyToClipboard` currently sets DIB + PNG only; no `CF_HDROP` file mode / image-only toggle / cache folder pruning).
19. [ ] Temporary links (upload/delete, records store, privacy sheet, shared-links sheet) - requires a Trayloom backend (product decision; hide until available).
20. [ ] 1x downscale option; PNG DPI metadata; optional ADS mark for Cleaner; Shelf integration (`Add to Shelf`).
21. [ ] Window capture quality: `CreateForWindow`/`PrintWindow` rather than cropping the virtual-screen frame (`CaptureActiveWindow` currently crops the desktop, so overlapping windows leak into the shot, no rounded-corner alpha, no attached dialogs); DWM extended-frame bounds are already used for picking.
22. [ ] Hub/menu-panel tile text and icon (4.0), `panelUtilityScreenshot` visibility toggle, Command Bar entries (`Recent captures`, capture).

Differences to correct in existing code
- Existing default hotkeys (Ctrl+Alt+S, Ctrl+Alt+Shift+S) differ from the Mac model, which is **disabled by default** (all `…Enabled` false). Decide: keep opt-in defaults (replica) vs the current always-on region hotkey.
- `ScreenshotFeature.Description` text differs from the hub text; use `Captures an area, window or screen and annotates it`.
- Default save folder is `Pictures\Screenshots` and name `Trayloom-…png` (Mac: Desktop, `Screenshot yyyy-MM-dd at HH.mm.ss.png`).
- `FrozenScreenWindow` uses one virtual-screen window with `Scale = DpiScaleX` - wrong on mixed-DPI setups; sizing uses `SetWindowPos` in px which is right, but content/DIP maths needs per-monitor windows.
- Editor palette (6 swatches, different hexes) and widths (3/6/10) differ from 8 named colours (4.7.4) and 2/4/7.
- Editor Esc closes immediately; Mac asks to discard if dirty and layers Esc (deselect first).
- Editor Copy does not close the editor nor honour the "image only/file" clipboard modes; Save always writes to the folder silently (Mac closes the editor after Save/Copy and shows the HUD).

Open questions for the product owner
1. Global-hotkey modifier layer: `Ctrl+Alt+Shift+<key>` (this spec) vs keeping `Ctrl+Alt+<key>`; and whether screenshot hotkeys should be opt-in (Mac) or on.
2. Temporary links: provide a Trayloom backend (contract in 4.12) or hide the feature.
3. SkiaSharp and ZXing.Net NuGet packages (both "ask first").
4. Windows.Graphics.Capture vs BitBlt as the default for the frozen still (spec recommends BitBlt first, WGC for protected/HDR content and window capture).


---

## 5. Screen Recorder

Source of truth: `Services/Recorder/*` (32 files), `UI/Recorder/*` (4 files), `UI/Settings/ScreenRecorderSettings.swift`, `Core/RecorderStrings.swift`, `Core/RecorderExportStrings.swift`, `Core/RecorderShareStrings.swift`, plus the pieces it borrows from the screenshot tool (`ScreenshotSupport.BackdropStyle`, `ScreenshotBackdropPopover`, `ScreenshotSupport.BlurStrength`, the unified capture chooser in `ScreenshotSelectionController`/`ScreenCaptureService`, `QuickToolHUD`, `RecentCaptureService`). Those borrowed pieces are specified in the Screenshot part of this spec; this part states exactly how the recorder uses them.

Feature identity (Features hub / Quick panel / command bar):

| Item | Value |
|---|---|
| Feature id (Trayloom) | `screen-recorder` (new; the port has no recorder at all) |
| Hub title / page title | **Screen recording** (`pageTitle`) |
| Hub description (`hubDescription`) | Records an area, window or screen and edits it afterwards |
| Quick-panel caption (`panelCaption`) | Record an area, window or the whole screen |
| Hub symbol | `record.circle` -> Segoe Fluent `U+E7C8` (Record); while recording the tile uses `stop.circle` -> `U+E71A` (Stop) |
| Hub group | Tools (`FeatureGroup.Tools`) |
| Default installed | No (every Tools feature is `InstalledByDefault = false` in the port) |
| Panel tile setting | `panelUtilityScreenRecorder` (bool, default `true`) = whether the tile is visible in the Quick panel utilities grid |
| Panel tile title | `Screen recording` idle, `Stop recording` while recording; tile caption = `panelCaption` idle, elapsed label (`0:07`, monospaced digits) while recording; tile symbol swaps to stop while recording; tile shows "needs attention" + permission caption on macOS only (`Permission required: Screen Recording`) -> **[WIN]** never shown (no permission exists on Windows) |
| Command bar / shortcut / panel entry point | all call the same toggle: `ScreenRecorderService.toggle()` = stop/cancel if anything is active, else open the capture chooser on the **Recording** tool |
| Default global shortcut | macOS Control+Option+Command+5 (`GlobalShortcut.screenRecorderDefault`); **[WIN]** `Ctrl+Alt+Shift+5`; disabled by default (`recorderShortcutEnabled=false`) |

There is **no** webcam/camera source, **no** keystroke-display overlay, **no** per-segment speed ramps, **no** cursor-style picker (the pointer is the real captured system pointer), **no** MOV/ProRes/WebM export. Exports are MP4 and GIF only. Do not invent these; implement exactly what is below.

---

### 5.1 Layout

#### 5.1.1 Settings page ("Screen capture" > Recording tab)

The recorder has no page of its own: it is the **Recording** tab of the shared "Screen capture" settings page (`ScreenCaptureSettings`): a segmented tool picker (Screenshot / Recording / Text / Color; only installed tools shown, hidden when only one is installed), then the selected tool's sections. The shared top section (header `Screen capture`, from `ScreenshotFeatureStrings.screenCaptureTitle`) contains, for the Recording tool:

1. Toggle titled by the role title `Screen recording` -> enables the dedicated global shortcut (`recorderShortcutEnabled`).
2. Shortcut row (hotkey recorder control + **Reset** button, `shortcutReset`) bound to `recorderShortcut`.
3. Toggle `Show capture menu when using keyboard shortcut` (`showCaptureMenuOnShortcut`), key `recorderShowCaptureMenuOnShortcut` (default `true`), disabled while the shortcut toggle is off. When off, the shortcut skips the tool chooser's tool strip and goes straight to the Recording selection (and such a shortcut-started selection is the only capture allowed on top of a running recording).
4. Orange caption `macOS rejected this shortcut. Choose another one.` (**[WIN]** `Windows rejected this shortcut. Choose another one.` / "Another app is using this shortcut") when `RegisterHotKey` fails.
5. (Shared) `Recent captures` shortcut rows (screenshot spec).

Then the Recording sections (`ScreenRecordingCaptureSettings`), in a grouped form, in this order:

**Section A - header `Screen recording`** (anchor `.screenRecorder`, used for deep links)
- Button (icon + label): `Record now` with `record.circle` (U+E7C8); while recording: `Stop recording` with `stop.circle` (U+E71A). Action = `toggle()`.
- Caption (11-12 pt, secondary, monospaced digits): `Record an area, window or the whole screen` idle; elapsed label `m:ss` / `h:mm:ss` while recording (updates every second).
- macOS-only `PermissionRow` for Screen Recording and Accessibility (**[WIN]** omit both; see 5.5.9).

**Section B (no header)**
- Segmented picker `Countdown`: `Off`, `3 s`, `5 s`, `10 s` (values 0/3/5/10; format `%d s`).
- Toggle `Record the sound of the Mac` (**[WIN]** `Record the sound of the PC`) + caption `Everything you hear goes into the recording, on its own track, so you can silence it later.`
- Toggle `Record the microphone` + caption `Your voice goes into its own track and stays adjustable in the editor.` Turning it on while mic permission is undetermined asks for permission (**[WIN]** nothing to ask; if the OS privacy switch blocks the mic show a note row `Microphone` with the explanation `Lets screen recordings include your voice when you turn it on.` and a link to `ms-settings:privacy-microphone`). A mic row is shown only if the toggle is on and the mic is unavailable.
- Toggle `Open the editor after recording` + caption `The recording opens ready to trim, mute and save. Turn this off to get the file straight away.`
- Toggle `Add zooms automatically` + caption `Turn this off to start new recordings without zooms. You can still add them in the editor.`

**Section C (no header)**
- Row `Save to` | folder display name (secondary, single line, middle truncation; default shows `Desktop` localized) | `x` circle button (U+E711/E894, only when a custom folder is set; tooltip `Reset`; sets the key to "") | button `Choose…` (folder picker with "create folder" allowed).
- Disclosure row `More options` (chevron, collapsed by default, state not persisted). When expanded (children indented):
  - Menu/inline picker `Quality`: `Small file`, `Balanced`, `High` + caption `Balanced fits most uses. High keeps every detail and makes bigger files.`
  - Segmented `Frames per second`: `30 fps`, `60 fps`.
  - Segmented `GIF size`: `Small`, `Medium`, `Large`.
  - Segmented `GIF smoothness`: `8 fps`, `12 fps`, `15 fps`.

**Section D - header `Temporary links`** (from `ScreenshotFeatureStrings.shareSectionTitle`)
- Toggle `Allow temporary links` (`recorderSharingEnabled`, default true).
- If on: caption `Choose 1 or 6 hours. The final video is compressed on this Mac to fit under 100 MB and deleted automatically.` (**[WIN]** `...on this PC...`)
- Button `Privacy` with hand icon (**[WIN]** lock U+E72E) -> opens the privacy sheet (below).
- If the share record list is non-empty: a navigation-style row `Shared links` (link icon U+E71B, right-aligned count, chevron U+E76C) -> opens the shared-links sheet.

**Privacy sheet** (modal, 560 x 390): header row padding 20: title `Privacy for temporary links` (hand icon, title3 semibold) + default button `Done`; divider; scrollable body padding 20, VStack spacing 18 of three paragraphs, each = icon (22 pt column, secondary) + wrapped text. Icons: `video` (U+E714), `externaldrive` (U+EDA2), `person.2` (U+E716). Texts (en, `[WIN]` replaces Vorssaint/Mac):
1. `Vorssaint sends only the final video created from this recording, including the audio you kept, and the expiration you choose. It does not send your name, account or device identifier.` -> **[WIN]** `Trayloom sends only ...`
2. `Network providers and the service temporarily process your public IP to prevent abuse. The video and link metadata are permanently deleted when you delete the link or its time ends. The service does not create backups.`
3. `Anyone with the link can view, download, save or redistribute the video. Active links are available to the service operator for abuse moderation. Share only with people you trust.`

**Shared links sheet** (560 x 360): header padding 18, title `Shared links` + `Done`; divider. Empty: centered `No active links` with `link.badge.plus` icon. Otherwise an inset list; row (vertical padding 5, spacing 14): link icon 24 pt | VStack{ URL (1 line, middle truncation, selectable), `Expires` + relative remaining time (`in 5 hours`; caption secondary) } | 3 borderless icon buttons: Copy (U+E8C8, tooltip `Copy link`), Open (U+E8A7, tooltip `Open`), Delete (U+E74D, tooltip `Delete now`; shows a spinner while deleting; all three disabled while a delete is running). Delete success -> HUD (link icon) `Link deleted`; failure -> alert `The link could not be deleted` with button `Done`. List refreshes on appear.

#### 5.1.2 Capture chooser extras (Recording tool of the unified selection overlay)

The selection overlay itself (dimmed screens, drag rectangle with pixel-size badge, window hover highlight, tool strip with keys `1`-`4`, `Esc cancels`, `Return`) belongs to the Screenshot spec. Recorder-specific parts:
- Recording is tool **2** (`shortcutKey "2"`, symbol `record.circle`). Uses "geometry" mode: no frozen image; no scrolling capture/loupe-copy; the picked rectangle is snapped to **even** pixel sizes (min side 32 px) and clamped inside the display.
- Subtitle line: `Choose what to record  ·  Drag to select an area  ·  Click a window to capture it` (10.5 pt medium, secondary; `selectionPurpose` + `hintDrag` + `hintClick`). `Return` records the whole display under the pointer; there is no on-screen "Full screen" button for recording (screenshot only).
- **Audio pill** under the guide line (visible/hit-testable only when Recording is the selected tool): capsule, padding 4, regularMaterial backdrop, 1 px stroke `primary @ 0.10`, shadow black .16 r10 y4; contains two *toggle-buttons* (`.small`, bordered): `Mac sound` with `speaker.wave.2.fill` (**[WIN]** label `PC sound`, U+E767) and `Microphone` with `mic.fill` (U+E720). They bind to `recorderSystemAudio` and `recorderMicrophone` and write through to settings immediately (`RecorderSelectionAudioOptions`), so overlay and Settings are always in sync. Defaults when the overlay opens = stored values (`true` / `false`).
- Confirming a rectangle/window/display returns a `Region`; the overlay closes and the recorder takes over (5.4.1).

#### 5.1.3 Countdown HUD (`QuickToolHUD.showCountdown`)

Circle 82 x 82 + 12 pt padding (regularMaterial / acrylic), no window shadow; a 1 px ring `white @ 0.12`; an accent-coloured arc (3 px, round caps, rotated -90 deg, 6 px inset) whose length goes from full to empty over **0.92 s** (`trim from 0.04 to 0.04 + progress*0.92`); centered number 32 pt bold rounded, monospaced digits. Positioned top-centre of the screen containing the pointer, 24 px below the visible top. One HUD per remaining second (`3`,`2`,`1`), each shown for 0.92 s with 0.12 s fade-in / 0.22 s fade-out. Excluded from capture.

#### 5.1.4 Region guide + recording indicator (`RecorderIndicator`)

- **Region guide** (only when an *area/display* was picked, not for a window): a borderless click-through topmost window the size of the recorded display, drawn behind the pill: even-odd fill of everything outside the selection with black @ 0.30, plus a 2 px rectangle stroke inset by 1 px, colour sRGB (0.18, 0.55, 1.0) @ 0.95 around the selection. Shown from confirmation until the recording ends. Excluded from capture.
- **Pill** (150 x 32, topmost "status bar" level, shadow on, fully rounded: corner radius = height/2): fill gray 0.12 @ 0.94, 1 px stroke white @ 0.18. Contents (x offsets from left): red dot 8 px at x=12 (system red, pulses opacity 1 -> 0.28, 0.85 s ease-in-out, autoreverse, forever; when paused: static opacity 0.4); time label x=25..75 (width 50), 13 pt semibold monospaced digits, white, centred, text `m:ss` (`h:mm:ss` after an hour); thin divider at x=78.5 from y=8 to h-8 (white @ 0.14); **pause button** at (82, 3) size 30 x 26 with `pause.fill` (U+E769) 11 pt semibold white (becomes `play.fill` U+E768 while paused); **stop button** at (116, 3) size 30 x 26 with `stop.fill` (U+E71A), tinted system red. Buttons: borderless, hover fill white @ 0.10, pressed @ 0.18, rounded rect radius 8 inset 1, pointing-hand cursor, accept first mouse click.
- Tooltips: pill `Recording controls`; pause `Pause recording` / `Resume recording`; stop `Stop recording`. Also set as automation names.
- Position: horizontally centred on the **visible frame of the recorded display** (for a window recording: the display the window was on), 10 px below the top of the visible area (below the taskbar if it is docked on top). Fade in 0.18 s, out 0.16 s. It never takes focus.
- It floats (not in the tray) on purpose so the stop control can never be hidden; it is **excluded from the video** (5.5.2).

#### 5.1.5 Editor window (`RecorderEditorView`)

Window: title `Recording`, dark theme forced, resizable, minimizable, no fullscreen; content min size **940 x 560**; initial content size = width `min(max(1060, 0.70 * visibleW), visibleW - 60)`, height `min(max(600, 0.68 * visibleH), visibleH - 80)` (visible area of the screen with the pointer), centred. Dragging inside never moves the window (the trim handles own the mouse). The top band doubles as the title bar (full-size content): **[WIN]** use `WindowChrome` with the band as caption area, native min/max/close buttons at the right, 14 px left padding (the Mac reserves 76 px for traffic lights).

Vertical structure (all fills over `Color(white 0.115)` = #1D1D1D with a top->bottom gradient `white @ 0.035 -> clear`):
```
Top band      h=54  ultraThin material (acrylic), bottom divider @0.45
Middle        HStack: Stage (fills) | 1px divider @0.5 | Inspector (fixed 272 px)
Bottom band   transport row + timeline rows; ultraThin material, top divider @0.45; padding L/R 18, top 10, bottom 16, row spacing 6
```

**Top band (left -> right, spacing 8, trailing padding 14):**
1. Brand mark 24 px wide (**[WIN]** Trayloom mark, tint white 0.92) + title `Recording` (13 pt semibold).
2. Button `clock.arrow.circlepath` (U+E81C History), compact 30x30, tooltip = Recent captures title (`Recent captures`) -> opens the recent-captures history window (Screenshot spec).
3. 18 px vertical divider (3 px side padding).
4. Undo (`arrow.uturn.backward` U+E7A7, tooltip `⌘Z` -> **[WIN]** `Ctrl+Z`), Redo (`arrow.uturn.forward` U+E7A6, tooltip `⇧⌘Z` -> `Ctrl+Shift+Z`). Disabled when stacks are empty.
5. **Presets menu**: label `Presets` + `slider.horizontal.3` (U+E713 / sliders), 12 pt medium, padding-h 9, h 30, fill white@0.055, radius 8, 1 px stroke white@0.08; disabled while a preset image copy runs. Items: `Original`, `Smooth`, `Studio` (the three looks); if user presets exist: divider, one item per preset name, submenu `Remove preset` listing every preset; divider; `Save current preset…`.
6. Spacer (min 10).
7. **Finished-file chip** (when `lastExportedURL != nil` and not exporting): bordered large button, green check (U+E73E/E930) + file name (11 pt medium, 1 line, middle truncation, max 190 px); click = reveal in Explorer (`explorer.exe /select,"path"`, tooltip `Reveal in File Explorer` **[WIN]** from `Reveal in Finder`); the chip is **draggable** (file drag-out as a `FileDrop`).
8. **Export progress chip** (while exporting): capsule (regularMaterial, padding 12/6): linear progress 24..110 px wide (determinate for `saving`/`compressing`; indeterminate 20 px spinner for `uploading`), label (11 pt medium, 0.8 white) = `Saving…` / `Compressing for sharing…` / `Uploading securely…`, borderless accent button `Cancel`.
9. Action cluster (spacing 5): trash icon button (U+E74D, destructive red, tooltip + automation name `Delete`) -> discard; button `Copy and delete` (`doc.on.doc` U+E8C8; tooltip `⌥⌘C` -> `Ctrl+Alt+C`; disabled while exporting); **split button** `Copy` (U+E8C8; tooltip `⌘C` -> `Ctrl+C`) + chevron-down menu (U+E70D, 30 px wide, tooltip `Copy as GIF`) whose single item is `Copy as GIF`; the pair is clipped to a radius-8 rounded rect with a 1 px stroke white@0.08 (hit targets separate; fills white@0.055, pressed 0.11); disabled while exporting.
10. If `recorderSharingEnabled`: **share menu** = borderless link icon (U+E71B, 24x24, tooltip `Share`), items `For 1 hour`, `For 6 hours`; disabled while exporting.
11. **Save split button** (bordered prominent, accent): label `Save` + `square.and.arrow.down` (U+E74E); primary action `Save` (tooltip `⌘S` -> `Ctrl+S`); dropdown menu: `Save` (`Ctrl+S`), `Save as…` (`Ctrl+Shift+S`), `Save as GIF`, divider, `Save to…` (= `strings.folderLabel + "…"` = `Save to…`; opens a folder picker that writes `recorderSaveFolder`). Disabled while exporting.

**Stage:** padded 18 (h) / 14 (v); rounded rect radius 14 filled black@0.72, content clipped to it, 1 px stroke white@0.12; shows the video (aspect-fit, letterboxed) rendered by the composer (5.4.6). Click on the stage toggles play/pause unless a selection exists: first click clears a selected zoom, else a selected blur, else toggles play. Special overlays:
- **Aim mode** (`isAimingZoom`): invisible hit layer; pointer = crosshair; a capsule hint (regularMaterial, 11 pt medium, padding 10/5, 10 px from top) `Click the picture to aim it`. The next click on the picture sets the focus point (must be inside the picture, in 0..1 unit space) and exits aim mode.
- **Blur draw mode** (`isPickingBlurArea`): the stage shows the recording **as captured** (no zoom, background, pointer or other blurs); a drag draws a rectangle (fill teal@0.22, 1.5 px teal stroke) with a hint capsule `Drag over what should stay hidden`; minimum drag distance 2 px; on release the rectangle (clamped into the picture, min side 0.01 of the picture) becomes the selected blur's area and draw mode ends.

**Bottom band** (spacing 6): 
- *Transport row* (spacing 12, bottom padding 4): play/pause bordered button 26x20 (`play.fill` U+E768 / `pause.fill` U+E769 13 pt semibold; tooltip `Space`); time label `current / total` (12 pt medium monospaced, white 0.78) = `elapsedLabel(currentTime) / elapsedLabel(round(outputDuration))` (whole seconds, truncating the current time); borderless buttons (12 pt medium, white 0.8): `Add text` (`textformat` U+E8D2), `Add image` (`photo` U+E91B), `Blur an area` (`aqi.medium`; draw a 3x3 mosaic glyph); when a cut range is selected: prominent **red** button `Cut out  m:ss` (scissors U+E8C6; duration of the selection rounded to whole seconds; disabled if cutting would leave < 0.4 s); spacer; **Export speed control** (5.1.6); **Quality menu** = `slider.horizontal.3` + `<Quality title>  <W> × <H>` (e.g. `Balanced  1920 × 1080`, the actual export size for this recording; borderless menu with an inline radio group `Small file`/`Balanced`/`High`; tooltip `Quality`).
- *Timeline rows* each = right-aligned 64 px label (10 pt semibold secondary, 1 line) + lane content, 10 px gap. Rows in order: `Zoom` (ClickRuler 12 px high over the Zoom lane 30 px); `Text` (shown only if texts exist; label is `textContentLabel` = `Text`; lane 30); `Image` (only if images exist; lane 30); `Blur` (only if blurs exist; lane 30); `Mac sound` (**[WIN]** `PC sound`; audio lane 32; only if the take has a system track); `Microphone` (audio lane 32; only if the take has a mic track); `Recording` (filmstrip 54 px; label = `editorTitle`).

Lane / filmstrip drawing details are in 5.1.7. Audio lane: HStack(spacing 8, padding-h 8) of waveform canvas (fills; opacity 0.3 when removed) + volume slider (92 px, 0..1, disabled when removed, tooltip `Volume`) + percent label (10 pt medium monospaced, 34 px right-aligned, `100%`) + toggle button 18x18 (`xmark` U+E711 when kept, tooltip `Remove`; `arrow.uturn.backward` U+E7A7 when removed, tooltip `Restore`, tinted with the track colour). Track colours: system = blue (#0A84FF), mic = purple (#BF5AF2). Lane background tint @ 0.11 (0.04 when removed), 7 px radius, 1 px border tint @ 0.28 (0.10 removed). Waveform: 220 peak bars across the duration; bar width = 0.68 of slot, height = peak * (h - 8) min 1 px, tint @ 0.8, 0.7 px radius; empty = 1 px line at mid-height tint @ 0.35.

#### 5.1.6 Export speed popover (`RecorderExportSpeedControl`)

Trigger: borderless button `speedometer` icon (**[WIN]** U+E916 Timer, closest) + label like `1×`, `1.25×` (`0..2` fraction digits, localized, followed by `×`), 12 pt medium monospaced; tooltip `Export speed`; disabled while exporting. Popover (320 wide, padding 18, spacing 14, disabled while exporting):
- Headline `Export speed`.
- 4-column grid (spacing 8) of preset buttons: `0.5×`, `0.75×`, `1×`, `1.25×`, `1.5×`, `2×`, `3×`, `4×` (selected one in bold).
- Stepper row `Custom speed` ... current value (step 0.01, range 0.25...4).
- Slider 0.25...4 step 0.01; min/max captions `0.25×` and `4×` (caption, secondary).
- Row `Export duration` ... `elapsedLabel(round(outputDuration / draftSpeed))`.
- Caption `Applies to video, GIF and shared links. The editing preview stays at 1×; the original recording is unchanged.`
- Buttons `Cancel` (cancel role) and `Done` (default): Done writes `exportSpeed = round(draft*100)/100` as one undoable document change. The draft is local so scrubbing does not fill the undo stack.

#### 5.1.7 Timeline widgets (exact drawing)

**Zoom/Text/Image/Blur lane** (`RecorderZoomLane`, one widget with `kind`): height 30; background rounded rect radius 6 white@0.04. Item rect: x from time, y=2, height = lane-4, min width 6. Colours: zoom = accent (Windows accent), text = purple (#AF52DE), image = orange (#FF9F0A), blur = teal (#30B0C7). Item fill accent@0.22; "hold" part (after the ramp-in: zoom ramp 0.42 s, text/image fade 0.25 s, blur 0) overpainted accent@0.16 clipped to the item; stroke accent@0.65 1 px (selected: @1.0, 2 px); if width > 34 draw two 2 px grab bars (at minX+3.5 and maxX-5.5, height 36% of the item, accent@0.9); centred label 10 pt semibold white@0.92: if width > 56 `"<glyph> <label>"` else glyph only; skipped if text + 10 px does not fit. Labels/glyphs: zoom = `"<amount %.1f>×"` and glyph `↗` (U+2197, follows pointer) or `◉` (U+25C9, aimed spot); text = first 18 chars of text, glyph `T`; image = file name without extension (18 chars), glyph `▣` (U+25A3); blur = `Blur` (`blurLaneLabel`), glyph `▦` (U+25A6). Hover shows a 1 px vertical line white@0.12 at the mouse x. Empty lane: 1 px dashed (5 on / 4 off) rounded outline white@0.22 inset 1 and the centred hint (10.5 pt medium white@0.40): zoom `Click here to add a zoom`, text `Click here to add text`, image `Click here to add an image`, blur `Click here to add a blur`.
Interaction (single mouse-down decides the gesture): edge zone = min(11 px, 40% of block width) -> resize that edge (starts after 3 px travel; cursor left-right arrows); body -> select, then move after 4 px travel (cursor open/closed hand); click on empty lane: if something on this lane is selected -> deselect (the next click adds), else **add** a new block at the clicked time (snapped). Right-click on a block -> context menu with one item `Remove`. Delete/Backspace removes the selected block (lane has keyboard focus). **Snap**: tolerance 8 px (converted to time); snap targets: 0, duration, playhead (source time), every click-down time of the pointer track, and the start/end of every other block on the lane. One gesture = one undo step (5.4.8).

**Click ruler** (12 px, above the Zoom lane): transparent scrub surface (drag seeks); 1 px baseline white@0.05 at the bottom; a 1.5 x 7 px tick white@0.35 for each click-down (at y = h-8); the playhead 2 px white spanning the height. Scrubbing lives here.

**Filmstrip** (54 px, corner radius 8 clip): 14 thumbnails (equal slots, aspect-fill, max 240 px; placeholder `gray 0.18` until loaded). Gestures on the strip: click = seek + clear cut selection; **drag = scrub**; **Shift+drag = pick a cut range** (decided on first movement, kept even if Shift is released) while the picture follows the pointer; Trim handles: 12 px wide accent rounded rects (radius 4) with a 2 x 36%-height white@0.85 grip, hit area inflated 6 px; drag changes `trimStart`/`trimEnd`; the outside of the trim and every cut is dimmed with black@0.62; the kept part has a 2 px accent@0.9 rounded (8) outline; each existing cut shows a **3 px orange@0.9 seam** at its start (click = restore that cut, hit zone +-4 px); the pending cut selection is red@0.28 fill with 1.5 px red@0.9 outline; the playhead is a 2 px white line with 1 px black@0.6 shadow. The end handle can never be closer than 2 handle widths to the start handle. Trim handle drags seek to the new start / just before the new end.

**Inspector** (272 px wide, ultraThin material, scroll without indicators, padding 16 h / 14 v, section spacing 18). State machine: if a text is selected -> *One text* panel; else if an image -> *One picture*; else a blur -> *One blur*; else a zoom -> *One zoom*; else the tabbed panel. Tabs = segmented `Look` | `Pointer` | `Zoom`.

*Look tab*
- Section title `LOOK` (uppercase, 11 pt semibold, white 0.62, kerning 0.4). Three look cards in an HStack(spacing 7): `Original` (rect icon), `Smooth` (`cursorarrow.motionlines`), `Studio` (sparkles). Card: vertical stack (spacing 6): 48x36 preview tile (radius 7, stroke white@0.12; studio = indigo@0.9 -> purple@0.55 diagonal gradient, raw = black@0.82, smooth = black@0.62; inner 31x20 rounded (4) mock window gray(0.55 raw / 0.78 else)@0.82 with shadow and a tiny icon bottom-right) + title 12.5 pt semibold (min scale 0.78); card radius 11, padding 8/5, fill accent@0.10 when selected else white@0.07 hovered / 0.035; 1.25 px accent@0.72 stroke when selected else white@0.14/0.07; checkmark circle (selected, accent) / empty circle (secondary@0.28) top-right padding 6. Selected = `document.matches(look)`.
- Divider (@0.35), section `BACKGROUND`: swatch button (bordered, large: 26x20 gradient preview (radius 5, 1 px stroke white@0.16) + label `Background` if a backdrop is set else `None` + up/down chevron) -> popover = shared screenshot **backdrop popover** (width 292, padding 14: 4-column swatch grid [None, 5 gradient presets Ocean/Sunset/Forest/Candy/Graphite, user-saved customs, wallpapers, `Image…`], `Solid`/`Gradient` segmented custom colours with a 20-colour inline palette, `Save background`/`Remove`) **without** its sliders (`showsAdjustments:false`); `Shape` menu picker: `Original`, `Wide`, `Square`, `Tall`; when a backdrop is set a rounded (10) panel (white@0.04, padding 9, spacing 7) with three sliders (title 10.5 pt, 50 px column; slider 0..1 mini; value `%.0f%%` 10.5 pt medium monospaced 34 px): `Margin` (padding, default 0.5), `Corners` (cornerRadius, default 0), `Blur` (blur, default 0). Double-click a slider resets it.

*Pointer tab* (only if the take has a pointer track; else the paragraph `This recording has no pointer track, so there is nothing to smooth. Zooms placed by hand still work.`): title `POINTER`; switch `Draw the pointer`; when on: menu `Smoothing` (`None`, `Light`, `Smooth`, `Cinematic`), slider `Size` (0.5..2.0, shown `%.2f×`, reset 1), switch `Mark where you click`.

*Zoom tab*: title `ZOOM`; if the take has a pointer track: switch `Zoom in on every click` (= `zoomEnabled`). If `zoomEnabled`: switch `Keep zoomed in while typing` + caption `After a click, typing keeps the automatic zoom on that spot.`; then either the **empty state** (card radius 12, white@0.035, 1 px white@0.07; 38 px accent@0.12 circle with `plus.magnifyingglass` U+E8A3 icon; title `No zooms yet` 12 pt semibold; caption `Create them from your clicks or add one on the timeline.` 10.5 pt centred; prominent small button `Create automatic zooms` if click-derived zooms exist, else `Add a zoom`) or, with segments, a panel (radius 11, white@0.035, padding 11) with slider `How close` (1.2..3.0, `%.1f×`, reset 1.8) and, with a pointer track, a borderless accent link `Back to one per click` (regenerates all zooms from clicks).

*One zoom* (max four rows): accent `‹ All options` back link (Esc also backs out); title `THIS ZOOM`; slider `How close` (1.2..3.0 `%.1f×`, reset 1.8); `Where it looks` with a segmented control `Follows the pointer` | `Pick a spot` (choosing Pick a spot starts aim mode; choosing Follow clears the focus); when aimed, a small accent link `Click the picture to aim it`; bordered small full-width `Remove`.

*One text*: back link, title `THIS TEXT`; multi-line text field (placeholder `Your text here`, 1-3 lines; **committed as one edit when it loses focus / Enter-commit / panel disappears**; Return inserts a new line); slider `Size` (3..16 %, shown `%.0f%%` of the picture height, reset 6 %); `Position` 3x3 grid of small rounded rects (20 px high, spacing 4; selected accent@0.85 else white@0.08) = the nine anchors; `Colour` six 20 px circles (white, black, accent, yellow, red, green; selected = 2.5 px accent ring else 1 px white@0.18); button `Remove`.

*One picture*: back link; title `THIS IMAGE`; file name (11 pt secondary, middle truncation); slider `Size` (4..60 %, `%.0f%%`, reset 18 %); slider `Opacity` (5..100 %, reset 100 %); `Position` 3x3 grid; button `Remove` (the panel reuses `removeText` = `Remove`).

*One blur*: back link; title `THIS BLUR`; prominent small full-width button `Choose the area` (aqi icon; disabled while drawing) with caption below: `Hidden for as long as its block lasts on the timeline.` (while drawing: `Drag over what should stay hidden`); slider `Blur strength` (1..5 integer steps, reset 3); `Remove`.

#### 5.1.8 Dialogs and alerts

| Dialog | Title / message | Buttons |
|---|---|---|
| Discard (trash button, Delete key with nothing selected, closing an un-exported window) | `Delete this recording?` / `It has not been saved anywhere yet.` (if at least one file was already written: `Saved and copied files will stay where they are.`); warning style | `Delete` (destructive, first), `Cancel` |
| Save preset | title `Save current preset…`; accessory text field 260x24 placeholder `Preset name` | `Save`, `Cancel` |
| Not enough space (alert, warning) | `Not enough space to record` / `Free up some space on the disk and try again.` | OK |
| Share result sheet (480 wide, padding 20, spacing 18) | header: link icon + `Shared links` title3 semibold + default `Done`; a card (radius 10, white@0.055 fill, 1 px white@0.10) with the URL (rounded body font, 1 line, middle truncation, selectable) and `Expires <relative time>` (caption secondary); right-aligned buttons `Copy link` (Ctrl+C; success HUD `Link copied`; failure = system beep) and `Delete now` (destructive; spinner while deleting; success HUD `Link deleted` and closes; failure alert `The link could not be deleted` + `Done`) | |
| Save as | save-file dialog, MP4 only, suggested name from 5.3.2, initial folder = the configured folder | |
| Add image | open-file dialog, images only, single file | |

#### 5.1.9 HUD toasts (QuickToolHUD)

Capsule/rounded rect (radius 10, regularMaterial, padding 14/9), icon 13 pt semibold accent + text 12 pt semibold (max 2 lines, 360 px max width, tail truncation), top-centre of the pointer's screen, 24 px below the top; 0.12 s fade in, shown 1.5 s, 0.22 s fade out; non-activating, click-through, **excluded from captures**. Icons used by the recorder: `record.circle` (U+E7C8), `mic.slash` (U+F781), `doc.on.doc` (U+E8C8), `link` (U+E71B), `photo` (U+E91B).

#### 5.1.10 Reference image `Resources/Images/highlights-recorderblur.png` (1024 x 550, marketing mock-up)

What it shows (viewed): a dark UI card on a near-black navy background (`~#1F2128`).
- **Editor window** (rounded 16 px, top 50 -> 400 px): macOS traffic lights (red/yellow/green) top-left and the centred grey title `Recording_Preview_2026-09.mp4`; title bar slightly lighter (`~#363636`) than the body (`~#2B2B2B`).
- **Preview area**: a mock "document" made of grey bars (a short heading bar left, three longer text bars). A **rectangular blur region** is overlaid on the right-hand bars: a **dashed cyan outline** (`~#12D5E0`, 2 px, dash ~6/4) with **white square handles with cyan border at its corners** (4 visible; edge-midpoint handles would make 8) and a **cyan pill label `Privacy Blur`** (dark bold text, fully rounded, sitting on the top edge slightly inset from the left corner). The area inside is rendered lighter/frosted (`~#555`) against the darker surroundings to suggest the blur; the bars inside are shown shifted/softened.
- **Timeline card** (below, 80 -> 945 x 420 -> 515, rounded 12, `~#202020` with a 1 px border): a ruler row with bold grey timecodes `00:00.0` (left), `00:05.0` (centre), `00:10.0` (right) (format `mm:ss.t`), a **white playhead** (2 px line with a round head) at about 38 % of the ruler, and a darker rounded track below containing one **teal region bar** (fill `~#1F5C63`, 1.5 px cyan border `~#12D5E0`, radius 4) labelled in white bold `Blur Region (00:02.4 - 00:07.1)`, spanning roughly 22 % -> 64 % of the track.

How it relates to the real code (the implementation must follow the Swift behaviour, not the mock-up): the shipped editor has **no on-stage resize handles or `Privacy Blur` label** for an existing blur (the area is (re)drawn with a drag while "Choose the area" is active, teal fill@0.22 + 1.5 px teal stroke, hint capsule `Drag over what should stay hidden`); the blur block on the lane is labelled just `▦ Blur`; timecodes use `m:ss`. The mock-up's colour language (teal for blur) matches `systemTeal` lane colour. Optional P2 enhancement (not in Vorssaint): draw the selected blur's rectangle on the stage as a dashed teal outline with corner/edge handles and let it be moved/resized there; if done, persist through the same `x,y,width,height` fields and one undo entry per drag.

---

### 5.2 Strings (enUS, verbatim)

`%@`/`%d` are format placeholders. `[WIN]` = platform substitution; keep the original beside it.

#### 5.2.1 `RecorderFeatureStrings.enUS`

| Key | English |
|---|---|
| pageTitle | Screen recording |
| hubDescription | Records an area, window or screen and edits it afterwards |
| panelCaption | Record an area, window or the whole screen |
| startButton | Record now |
| stopButton | Stop recording |
| fileNamePrefix | Recording |
| selectionPurpose | Choose what to record |
| indicatorTooltip | Recording controls |
| countdownLabel | Countdown |
| countdownOff | Off |
| countdownSecondsFormat | %d s |
| qualityLabel | Quality |
| qualitySmall | Small file |
| qualityBalanced | Balanced |
| qualityHigh | High |
| qualityCaption | Balanced fits most uses. High keeps every detail and makes bigger files. |
| frameRateLabel | Frames per second |
| frameRateFormat | %d fps |
| systemAudioToggle | Record the sound of the Mac  **[WIN]** Record the sound of the PC |
| systemAudioCaption | Everything you hear goes into the recording, on its own track, so you can silence it later. |
| folderLabel | Save to |
| folderChoose | Choose… |
| moreOptions | More options |
| copyButton | Copy |
| copyGIFButton | Copy as GIF |
| saveButton | Save |
| discardButton | Delete |
| copiedHUD | Recording copied |
| savedHUDFormat | Saved to %@ |
| recordFailed | The screen could not be recorded |
| noSpaceTitle | Not enough space to record |
| noSpaceMessage | Free up some space on the disk and try again. |
| stoppedNoSpaceHUD | Recording stopped, the disk is almost full |
| shortcutLabel | Shortcut |
| editorTitle | Recording |
| saveVideoButton | Save |
| saveGIFButton | Save as GIF |
| exportingLabel | Saving… |
| cancelButton | Cancel |
| exportFailed | The recording could not be saved |
| gifTooLongFormat | A GIF can be up to %d seconds long  (used by Media tools, not by the recorder editor) |
| gifSizeLabel | GIF size |
| gifSizeSmall / Medium / Large | Small / Medium / Large |
| gifFrameRateLabel | GIF smoothness |
| discardTitle | Delete this recording? |
| discardMessage | It has not been saved anywhere yet. |
| openEditorToggle | Open the editor after recording |
| openEditorCaption | The recording opens ready to trim, mute and save. Turn this off to get the file straight away. |
| lookLabel | Look |
| lookRaw / lookClean / lookStudio | Original / Smooth / Studio |
| lookCaption | A starting point. Change anything below and it stays changed.  (defined, not rendered anywhere) |
| pointerSectionLabel | Pointer |
| pointerShowToggle | Draw the pointer |
| pointerSmoothingLabel | Smoothing |
| pointerSmoothingOff / Light / Smooth / Cinematic | None / Light / Smooth / Cinematic |
| pointerSizeLabel | Size |
| clickRingToggle | Mark where you click |
| zoomSectionLabel | Zoom |
| zoomToggle | Zoom in on every click |
| zoomAmountLabel | How close |
| backgroundSectionLabel | Background |
| shapeLabel | Shape |
| shapeOriginal / shapeWide / shapeSquare / shapeVertical | Original / Wide / Square / Tall |
| noPointerNote | This recording has no pointer track, so there is nothing to smooth. Zooms placed by hand still work. |
| zoomLaneEmptyHint | Click here to add a zoom |
| addZoomButton | Add a zoom |
| removeZoom | Remove (also the Remove label of blur, context menu and the zoom/blur panels) |
| thisZoomLabel | This zoom |
| zoomWhereLabel | Where it looks |
| zoomFollowsPointer | Follows the pointer |
| zoomPickSpot | Pick a spot |
| zoomPickSpotHint | Click the picture to aim it |
| regenerateZooms | Back to one per click |
| backToOptions | All options |
| cutOutButton | Cut out |
| cutHint | Drag across the film to pick a part to remove  (defined, not rendered) |
| addTextButton | Add text |
| textLaneEmptyHint | Click here to add text |
| thisTextLabel | This text |
| textPlaceholder | Your text here |
| textContentLabel | Text |
| textSizeLabel | Size |
| textPositionLabel | Position |
| textColorLabel | Colour |
| removeText | Remove (also the Remove button of the image panel) |
| copyAndDeleteButton | Copy and delete |
| saveAsButton | Save as… |
| discardSavedMessage | Saved and copied files will stay where they are. |
| presetsButton | Presets |
| savePreset | Save current preset… |
| presetNamePlaceholder | Preset name |
| removePreset | Remove preset |
| zoomEmptyTitle | No zooms yet |
| zoomEmptyCaption | Create them from your clicks or add one on the timeline. |
| createAutomaticZooms | Create automatic zooms |
| typingZoomToggle | Keep zoomed in while typing |
| typingZoomCaption | After a click, typing keeps the automatic zoom on that spot. |
| microphoneToggle | Record the microphone |
| microphoneCaption | Your voice goes into its own track and stays adjustable in the editor. |
| systemAudioTrackLabel | Mac sound  **[WIN]** PC sound |
| microphoneTrackLabel | Microphone |
| audioVolumeLabel | Volume |
| removeAudio | Remove |
| restoreAudio | Restore |
| microphoneUnavailableHUD | Microphone unavailable |
| microphonePermissionName | Microphone |
| microphonePermissionExplain | Lets screen recordings include your voice when you turn it on. |
| automaticZoomToggle | Add zooms automatically |
| automaticZoomCaption | Turn this off to start new recordings without zooms. You can still add them in the editor. |
| pauseButton | Pause recording |
| resumeButton | Resume recording |
| blurLaneLabel | Blur |
| addBlurButton | Blur an area |
| blurLaneEmptyHint | Click here to add a blur |
| thisBlurLabel | This blur |
| blurPickArea | Choose the area |
| blurPickAreaHint | Drag over what should stay hidden |
| blurCaption | Hidden for as long as its block lasts on the timeline. |
| addImageButton | Add image |
| imageLaneLabel | Image |
| imageLaneEmptyHint | Click here to add an image |
| thisImageLabel | This image |
| imageSizeLabel | Size |
| imageOpacityLabel | Opacity |
| imagePositionLabel | Position |
| imageImportFailed | Couldn’t add this image. |

#### 5.2.2 `RecorderExportStrings.enUS`
`speed` = Export speed; `custom` = Custom speed; `duration` = Export duration; `previewNote` = Applies to video, GIF and shared links. The editing preview stays at 1×; the original recording is unchanged.

#### 5.2.3 `RecorderShareStrings.enUS`
- caption: Choose 1 or 6 hours. The final video is compressed on this Mac to fit under 100 MB and deleted automatically. **[WIN]** `...on this PC...`
- privacyData / privacyStorage / privacyAccess: see 5.1.1 (Vorssaint -> Trayloom).
- compressing: `Compressing for sharing…`; uploading: `Uploading securely…`
- tooLarge: `This recording cannot fit under 100 MB without losing too much quality.`
- failed: `The temporary link could not be created`
- tourCaption: `Compress a finished recording on this Mac and share it for 1 or 6 hours.` **[WIN]** `...on this PC...` (hub tour card)

#### 5.2.4 Borrowed strings (from `ScreenshotFeatureStrings` / `Strings`)
`Temporary links` (section header) · `Allow temporary links` · `Privacy` · `Privacy for temporary links` · `Shared links` · `No active links` · `Expires` · `Delete now` · `Link deleted` · `The link could not be deleted` · `Open` · `Copy link` · `Done` · `Share` (tooltip) · `For 1 hour` · `For 6 hours` · `Link copied` · `Show capture menu when using keyboard shortcut` · `Screen capture` · `Drag to select an area` · `Click a window to capture it` · `Esc cancels` · `Return captures the whole screen` · `Reset` (shortcut reset) · `macOS rejected this shortcut. Choose another one.` **[WIN]** · `Reveal in Finder` **[WIN]** `Reveal in File Explorer` · Recent captures title `Recent captures` · Backdrop: `Background`, `None`, `Margin`, `Corners`, `Blur`, `Wallpaper`, `Image…`, `Solid`, `Gradient`, `Save background`, `Remove`, `Custom` · `Blur strength`.

---

### 5.3 Settings

#### 5.3.1 Preference keys (UserDefaults -> Trayloom `IFeatureSettings` of feature `screen-recorder`)

| Key | Type | Default | Allowed / sanitising | Effect |
|---|---|---|---|---|
| recorderShortcutEnabled | bool | false | - | registers the global hotkey |
| recorderShortcut | string (hotkey) | mac ⌃⌥⌘5 -> **[WIN]** `Ctrl+Alt+Shift+5` | parse failure -> default | start/stop toggle |
| recorderShowCaptureMenuOnShortcut | bool | true | - | shortcut shows the tool strip or goes straight to Recording |
| recorderCountdown | int | 3 | `{0,3,5,10}` else 0 (`ScreenshotSupport.sanitizedDelay`) | seconds counted down before the first frame |
| recorderQuality | string | `balanced` | `small`/`balanced`/`high` else `balanced` | **default** export quality of a *new* edit document (the editor menu changes it per recording) |
| recorderFrameRate | int | 60 | `{30,60}` else 60 | capture frame cap |
| recorderSystemAudio | bool | true | - | record system sound track; also the editor's initial `keepsSystemAudio` |
| recorderMicrophone | bool | false | - | record microphone track |
| recorderSystemAudioTapVerified | bool | false (unregistered) | internal | macOS-only: whether the process-tap path has been proven; **[WIN]** not needed (see 5.5.4) |
| recorderSaveFolder | string | "" (= Desktop) | must exist and be a directory, else Desktop | destination of Save / direct-save |
| recorderOpenEditor | bool | true | - | open the editor after recording (else direct save) |
| recorderAutomaticZoom | bool | true | - | initial `zoomEnabled` of a new edit document |
| recorderGIFSize | string | `medium` | `small`/`medium`/`large` | initial GIF size (long edge 420/600/800 px) |
| recorderGIFFrameRate | int | 12 | `{8,12,15}` else 12 | initial GIF fps |
| recorderEditorPresets | data (JSON) | empty | max 12 kept (suffix) | saved editor presets |
| recorderSharingEnabled | bool | true | - | show share menu; gate `createLink` |
| panelUtilityScreenRecorder | bool | true | - | Quick panel tile visibility |
| screenshotBackdropPresets | string (JSON) | "" | max 12 | **shared with the screenshot editor**: user-saved backgrounds |
| screenshotSharingDeveloperEndpoint | string | "" | dev builds only | **[WIN]** ignore |

Windows storage: `%APPDATA%\Trayloom\settings.json` through `IFeatureSettings` (keys above under the feature scope); presets JSON may live in the same file as a string or in `%LOCALAPPDATA%\Trayloom\screen-recorder\presets.json`.

#### 5.3.2 Output naming and folders
- Direct-save container: `.mov` on macOS (a plain copy of the take) -> **[WIN]** `.mp4` (finalised take). Editor "Save" / "Save as GIF" use `.mp4` / `.gif`.
- File name = `Recording yyyy-MM-dd 'at' HH.mm.ss.<ext>` (en_US_POSIX formatter, e.g. `Recording 2026-11-02 at 14.05.30.mp4`). **[WIN]** keep verbatim (colons are not used, so the name is filesystem-safe; fine on Windows).
- Uniqueness: if the name exists append ` 2`, ` 3` ... up to 9999 before the extension (`Recording 2026-11-02 at 14.05.30 2.mp4`).
- Folder: `recorderSaveFolder` when it still exists as a directory, else the user's Desktop (`Environment.SpecialFolder.DesktopDirectory`), else the user profile. `~` expansion -> **[WIN]** `Environment.ExpandEnvironmentVariables` + `%USERPROFILE%`.
- "Copy" / "Copy as GIF" / "Copy and delete" export into `%LOCALAPPDATA%\Trayloom\Copied Recordings\` with the same naming rule; files older than 24 h in that folder are deleted each time (macOS: Caches/<bundle>/Copied Recordings).
- Share artifacts: `%TEMP%\Trayloom\Temporary Recording Uploads\<guid>.mp4`, files older than 24 h purged each time; folder ACL = current user only.
- Take store: `%LOCALAPPDATA%\Trayloom\Recordings\Take-<GUID>\` containing `take.mp4` (**[WIN]** video-only fragmented MP4; macOS `take.mov`), `system.wav`/`mic.wav` sidecars (**[WIN]**), `pointer.bin`, `typing.json`, `edit.json`, plus imported overlay-image subfolders `<guid>\<original file name>`. The takes root is created with current-user-only access.

#### 5.3.3 Quality tables (shared by capture master and export)

| Quality | outputScale | bits per pixel per frame | keyframe interval |
|---|---|---|---|
| Small file | 0.5 | 0.05 | 10 s |
| Balanced | 1.0 | 0.06 | 10 s |
| High | 1.0 | 0.09 | 2 s |

`averageBitRate = clamp(width * height * fps * bpp, 800_000, 60_000_000)` (bits/s). The take (master) is **always** written at High (2 s keyframes). Examples: 1920x1080@60 High = 11.2 Mbps, Balanced 7.5 Mbps; Small export is 960x540 at 1.55 Mbps. Audio everywhere: AAC-LC, 48 kHz, stereo, 160 kbps (sharing: 128 kbps). Colour: BT.709 primaries/transfer/matrix. No B-frames (frame reordering off) for take and export; sharing export allows B-frames, 4 s keyframes, H.264 High profile, `shouldOptimizeForNetworkUse` (faststart).

---

### 5.4 Behaviour

#### 5.4.1 Recording state machine (`ScreenRecorderService`)

States: idle -> (selecting) -> awaiting-mic -> countdown -> preparing writer -> starting stream -> **recording** <-> **paused** -> finishing -> (editor | direct save). Flags: `isRecording`, `isPaused`, `elapsedSeconds` (int); also private `isFinishing`, `isPreparingRecording`, `isAwaitingMicrophone`, countdown work item, `pendingStartGeneration` (every cancel bumps it so late callbacks are ignored).

1. **Entry** `toggle()`: if `stopOrCancelActiveCapture()` returns true (stops a running recording; cancels a countdown/mic wait/preparation; or just returns true while finishing) -> done. Otherwise open the unified capture chooser with Recording preselected (`ScreenCaptureService.capture(initial: .recording)`).
2. **prepareForSelection()** gate (also called by the chooser): feature installed, not finishing, no session/countdown/mic wait/preparation; [macOS permission gates: Screen Recording, Accessibility -> **[WIN]** none]; disk gate `freeBytes >= 2_000_000_000` else show the "Not enough space" alert and refuse.
3. On `.region(region)` from the chooser: create the indicator, show the **region guide** (area/display only), bump generation, `prepareCountdown`.
4. **Mic gate**: if microphone wanted: permission granted -> continue; undetermined -> request and wait (`isAwaitingMicrophone`); on deny/unknown show HUD `mic.slash` `Microphone unavailable` and continue **without** mic (recording is not blocked).
5. **Countdown**: `delay = sanitizedDelay(recorderCountdown)`. 0 -> start now. Else show the countdown HUD for `delay`, `delay-1`, ... `1`, one per second via a cancellable timer; any cancel/stop invalidates and removes the guide/pill.
6. **beginRecording**: create a take folder (failure -> hide indicator, HUD `The screen could not be recorded`); read fps (`{30,60}`), `capturesSystemAudio` (`recorderSystemAudio`), `capturesMicrophone` (`recorderMicrophone` AND permission granted); create one `PauseClock`; build the **writer on a background queue** (encoder probing can block) at the region's even pixel size; failure -> delete take + HUD `The screen could not be recorded`.
7. **startPreparedRecording**: install `onUnexpectedStop` (-> `stop()`; what was recorded is kept) and `onMicrophoneUnavailable` (-> HUD `Microphone unavailable`); show the pill (tooltips as 5.1.4) **before** the stream starts (exclusion list); wait **120 ms** for the selection overlay to leave; compute exclusions (indicator, region guide, HUD); start stream+audio+samplers; on failure: delete take, hide indicator, report (`permissionDenied` -> request permission [macOS], `diskFull` -> no-space alert, others -> HUD `The screen could not be recorded`).
8. **recordingDidStart**: `isRecording=true`; elapsed 0; prevent system sleep for the duration (**[WIN]** `SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED)`); a 1 s repeating timer (0.1 s tolerance) updates `elapsedSeconds` (= `floor(pauseClock.elapsed(now))`, excludes paused time), the pill label, the Quick-panel tile, and **checks disk space** (off the UI thread; only one query in flight; if `free < 500_000_000` and the answer belongs to the *same* take -> `stop(reason: "Recording stopped, the disk is almost full")`).
9. **Pause/resume** `togglePause()` (pill button): records the QPC instant into the pause clock; paused time is removed from the timeline; frames/audio/pointer/click/typing events inside a pause are discarded; elapsed freezes; pill shows play glyph + static dot (0.4); the capture session is **not** torn down.
10. **Stop** (pill stop button, hotkey, tile, command bar, indicator): ignored if already finishing (a second shortcut press during finalisation cannot start a new recording). Sets `isFinishing`, invalidates pending starts, hides surfaces, ends sleep prevention, stops the session (waits for engine -> audio sources -> pointer/typing samplers; writes `pointer.bin` if not empty and `typing.json` if not empty; closes the file), then: written OK -> `deliver`; else delete take + HUD `The screen could not be recorded`. Then sweep orphan takes.
11. **deliver(take, reason)**: if `reason == nil` and `recorderOpenEditor` -> open the editor (`openEditor`); otherwise (editor disabled, or the recording was stopped for low disk) **direct save**: copy/finalise the take to `saveDestination(ext)`, delete the take folder, `RecentCaptureService.recordRecording(at:)`, HUD `reason ?? "Saved to <folder name>"`. If the direct save throws: system beep, HUD `The screen could not be recorded`, and the take opens in the editor instead.
12. Uninstalling the feature while recording stops and saves what was recorded but closes all recorder-owned editors immediately (editors opened by Media tools survive).
13. Concurrency rules: only one recording; a screenshot/OCR/colour pick can start over a recording only via a dedicated shortcut with "show capture menu" off; the chooser's own tool strip offers only its own tool during a recording.

#### 5.4.2 Pure policies (port verbatim; all in `RecorderSupport`)
- `Region`: display id, optional window id (set only when a window was clicked), `pixelRect` (top-left origin in display pixels, even sizes), anchor rect (global desktop coords for panels), `scale` (pixels per point). Resolved **once**; a moved window keeps recording the region it had when picked (pointer track and zoom assume it).
- `evenSize`: `max(32, floor(v))` then minus 1 if odd. `snappedPixelRect(rect, bounds)`: intersect with bounds (empty -> whole bounds), floor origin, even size, **shrink rather than shift** when overflowing.
- `elapsedLabel(seconds)`: `m:ss` (no leading zero on minutes), `h:mm:ss` from 1 h.
- Pause timeline: pauses are stored as intervals; `elapsed(t) = t - origin - totalPausedBefore(t)`; `sampleTime(start,duration)` drops any audio/video sample overlapping a pause (whole buffer dropped when it crosses a boundary); `eventTime(t)` returns nil inside a pause. The origin is set once at `beginSession` and never rebased.
- Disk thresholds: start >= 2 GB free; continue >= 500 MB. Imports (`canImport`): file size > 0, free >= size, and leaves >= 500 MB.
- Trim: `sanitizedTrim`: end <= 0 or non-finite = duration; start clamped to 0..duration; minimum length 0.2 s (the dragged handle gives way; result is always a playable clip).
- Canvas: padding margin = `min(0.35, backdrop.padding * 0.18)` (0 with no backdrop); `canvasSize(source, padding, aspect, cropsToAspect)`: inner = 1 - 2*margin; width,height = source / inner; aspect ratios `wide 16:9`, `square 1:1`, `vertical 9:16`; with a visible backdrop the **short side grows** to reach the ratio; without a backdrop (`cropsToAspect`) the largest centred crop of the source is used; long edge capped at **3840**; result even. `cardRect` = recording rect centred inside the canvas (fit; or fill/cover when cropping to an aspect with no backdrop), rounded to whole pixels. `cardCorner = round(clamp(cornerRadius,0,1) * min(card.w, card.h) * 0.09)`.
- Output size for a quality = `evenSize(canvas * outputScale)`; GIF size: scale so the long edge <= 420/600/800 (never upscale), even.
- `unitPoint(location, viewSize, sourceSize)`: aspect-fit letterbox maths -> 0..1 picture coordinates (not clamped; caller validates).
- Blur block size: `base = clamp(round(min(w,h)/3), 8, 48)` of the blurred area in source pixels; `block = max(2, round(base * factor(strength)))`, factor by level 1..5 = 0.4, 0.65, 1, 1.5, 2.2 (default level 3).
- Pointer pixel size (fallback arrow sizing) = `max(24, sourceHeight * 0.028) * userScale * max(1, systemScale)`.
- Sample timestamp helpers (`RecorderSampleTiming`): `converted` maps a buffer's presentation time between clocks, `retimed` shifts timing without changing cadence (per-sample duration preserved). **[WIN]** equivalents are trivial (all clocks are QPC; see 5.5.5).
- Take sweep: a take folder is deleted when no editor owns it AND (finished > 24 h ago, or no master yet and created > 1 h ago). Run after every recording and on feature sync/start-up. A take lives exactly as long as its editor: closing the editor deletes it, saved or not.
- Stage points -> unit space for aiming/blur use the video-letterbox maths above.

#### 5.4.3 Pointer, click and typing tracks

- **Pointer sampler**: fixed 125 Hz cadence on its own high-priority thread (not an event tap): each tick, if `pauseClock.eventTime(now)` is non-nil and the location is finite, append a `Sample{ time, point (0..1 inside the recorded area, top-left origin; may be outside -0.2..1.2 when the pointer left the area), shapeIndex, isPointerVisible }`. Shape identity: a cheap "cursor changed" check every tick; the (expensive) pixel read only when it changed; shapes are keyed by **content hash** (FNV-1a over pixels + size + hotspot) because the OS handle is not stable; an animated cursor strip (height >= 2*width and divisible) keeps only the first frame; a sharper bitmap is substituted later if available. The first shape is whatever was current at start, so index 0 is a sane fallback. Visibility is sampled separately (typing hides the pointer without changing the shape).
- **Clicks**: mouse-only monitor of left/right down+up (no permission) -> `Click{time, isDown}` in pause-clock time (middle button not recorded).
- **Typing track**: `{ "times": [seconds...] }` JSON, **key-down timing only (auto-repeat ignored), never the key or text**. There is no keystroke-visualisation feature.
- **`pointer.bin`** (little-endian): header 28 bytes = magic `VRPT` (0x56 0x52 0x50 0x54), u16 version = **3**, u16 0, f32 systemScale, f32 displayScale, u32 sampleCount, u32 clickCount, u32 shapeCount; then samples (16 B: f32 time, f32 x, f32 y, u16 shapeIndex, u16 visible), clicks (8 B: f32 time, u32 isDown), shapes (20 B header: f32 hotX, f32 hotY, f32 width, f32 height, u32 pngLength; then PNG bytes). Decoder is tolerant: truncated tail keeps whole records; non-finite samples skipped. **[WIN]** keep the format byte-for-byte (a "Trayloom" magic is allowed but a port-defined magic `WKPT` + version is cleaner; if changed, also change nothing else). Windows writes `displayScale = 1`, `systemScale = 1` and cursor sizes in **physical pixels** (5.5.6).
- Ten minutes = 75,000 samples = ~1.2 MB.

#### 5.4.4 Audio behaviour
- Two sources, each its own track: system ("Mac sound"/"PC sound") and microphone; both 48 kHz stereo AAC 160 kbps in the finished file. System audio excludes this app's own sound; for a **window** recording it follows the picture (only that app's audio), for an area/display it is the whole mix.
- macOS specifics (not ported): ScreenCaptureKit audio + a CoreAudio process tap, trust-on-first-heard logic (`recorderSystemAudioTapVerified`), stereo-mixdown level compensation + limiter for >2-channel outputs, device-change rebuilds. **[WIN]**: WASAPI loopback/process loopback solve all of this natively (5.5.4); implement only: default-device change handling (rebuild the capture on `DefaultAudioEndpointChanged`), silence insertion for gaps, and mic-unavailable fallback.
- Mic failure at start -> HUD `Microphone unavailable`, recording continues without it. 
- Editor: each present track has Keep/Remove (`keepsSystemAudio`/`keepsMicrophone`) and gain 0..1 (default 1). A removed track is silent and its lane dimmed. Waveform is a one-time 220-bin peak summary (abs max over channels per bin) loaded off the UI thread.

#### 5.4.5 Edit document (`edit.json`, camelCase keys, written atomically next to the take on every committed change)

```
trimStart, trimEnd(0 = to end), quality, exportSpeed(1), keepsSystemAudio(true), gifSize, gifFrameRate,
backdrop (BackdropStyle JSON string or ""), aspect, showsPointer(true), pointerSmoothing("smooth"),
pointerSize(1), showsClickRing(true), zoomEnabled(true), zoomAmount(1.8), zoomsOnTyping(false),
cuts[{start,end}], zoomSegments[{id,start,end,amount,focusX?,focusY?}], zoomsGenerated(false),
texts[{id,text,start,end,anchor,size,palette}], images[{id,path,start,end,anchor,size,opacity}],
blurs[{id,start,end,x,y,width,height,strength}], keepsMicrophone(true), systemAudioGain(1), microphoneGain(1)
```
Every field is optional on decode and falls back to the value a new recording gets. `sanitized(duration)` clamps everything (quality/gif enums, speed 0.25..4, pointer size 0.5..2, zoom amount 1.2..3, gains 0..1, drops invalid overlays, merges cuts). A new take's document starts from preferences: `quality=recorderQuality`, `keepsSystemAudio=recorderSystemAudio`, `gifSize`, `gifFrameRate`, `zoomEnabled=recorderAutomaticZoom`. All times are in the **recording's own clock** (never the output clock); one function maps between clocks (5.4.7). `isEdited` = trim moved > 0.01 s, audio removed/gain != 1, cuts, zooms, texts, images, blurs or speed != 1.

`BackdropStyle` JSON (shared with screenshots): `{kind: none|preset|solid|gradient|image, presetID?, colors?[[r,g,b]...], imagePath?, padding(0..1, default 0.5), cornerRadius(0..1, default 0; sanitised NaN -> 0.1), blur(0..1, default 0)}`; presets (top-left -> bottom-right gradient, sRGB): ocean (0.20,0.47,0.96)->(0.45,0.83,0.98); sunset (0.99,0.36,0.42)->(1.00,0.75,0.35); forest (0.07,0.56,0.43)->(0.62,0.87,0.50); candy (0.66,0.32,0.95)->(0.99,0.56,0.65); graphite (0.23,0.25,0.31)->(0.55,0.60,0.70). Image backdrops fill (cover) the canvas, thumbnail max 4096 px; backdrop `blur` = Gaussian radius `blur * min(w,h) * 0.035` applied to the **background only**.

**Looks** (one click, no stored "active look"; the card is "selected" iff `matches`): Original = backdrop none, aspect original, pointer shown, smoothing off, ring off, zoom off. Smooth = backdrop none, aspect original, pointer shown, smoothing smooth, ring on, zoom on. Studio = backdrop preset graphite with padding 0.45, cornerRadius 0.35, pointer shown, smoothing smooth, ring on, zoom on. Applying a look that turns zoom on regenerates click zooms **only if the zoom list is empty**.

**Presets** (user "Presets" menu): saved fields = backdrop, aspect, showsPointer, pointerSmoothing, pointerSize, showsClickRing, zoomEnabled, zoomAmount (+ the picture overlays, which are copied into a private preset image store; applying restores fresh copies with `start=0,end=duration`); max **12** (oldest dropped); same name (case/diacritic-insensitive) replaces; applying then runs `restoringAutomaticZooms` + `sanitized`. Cuts, captions, blurs, sound are *not* part of a preset.

#### 5.4.6 Motion maths (port exactly; all offline so smoothing sees the future)

Pointer path per output frame (`makePlan`): resample samples to a uniform grid at the capture frame rate over the whole recording (linear in time); smoothing = zero-phase one-pole low-pass run forward then backward (`filtfilt`) with edge padding `min(n, max(1, round(3/alpha)))`, `alpha = 1 - exp(-2π * cutoff * dt)`, `cutoff = preset * 1.553773974` (compensating the squared response). Cutoffs: None=off, Light 4.0 Hz, Smooth 2.0 Hz, Cinematic 1.2 Hz; default **Smooth**. Then **click anchoring**: blend smoothed -> raw by weight `smoothstep(1 - |t - clickTime| / 0.12)` within 0.12 s of any click, and weight 1 for the whole press->release span (and after a press with no release); then **shake exception**: around frame `i`, a window of `max(2, round(0.1*fps))` frames (i +- span/2, needs >= 4 samples) whose summed travel is > 0.06 (`0.015*4`, normalised units) and that has >= 3 x-direction reversals uses the raw path for frame `i`. Shape/visibility: nearest sample in time (never interpolated).

Auto-zoom segments from clicks (`zoomSegments`): consider only click-downs with `time < duration - 1.0` (the stop click never zooms); each makes `[max(0.001, t - 0.3), t + 2.5]`; merge when `next.start - last.end <= 2.5` (a burst = one zoom); typing times (only if `zoomsOnTyping`): for each typing time find the last segment with `start <= time <= end + 8.0` and extend its end to `max(end, time + 1.4)`; clamp end to `duration - 0.8`; drop segments shorter than **0.9 s**. Segments created are `ZoomSegment(start,end,amount=document.zoomAmount)` with no focus (follows pointer); generated **once** (`zoomsGenerated`) when the editor first opens a take; afterwards only an explicit action regenerates (zoom tab link `Back to one per click`, empty-state `Create automatic zooms`, switching the zoom switch on with an empty list, typing-zoom toggle, a Look/Preset that turns zoom on).

Zoom progress/state: ramp-in `0.42 s` (`min(0.42, segmentDuration/2)`), ramp-out `0.65 s` after the end, both `smootherstep(t)=6t^5-15t^4+10t^3`; the segment that **owns** the instant wins over an earlier one still fading out (touching zooms hand over without zooming out). `zoomAmount target[frame] = 1 + (amount-1)*progress`. Focus for an automatic zoom = centre of the **focus cluster** (pointer samples grouped into boxes `width <= 0.5/zoom`, `height <= 0.7/zoom` of the frame; the zoom aims at the centre of the cluster it is in; clusters keyed by time); viewport travel parameter uses an **edge band** `0.25`: `travel = clamp((focus-0.25)/(1-0.5), 0, 1)`; for a **hand-aimed** zoom (`focusX/Y` set) `travel = clamp((focus - visible/2)/(1 - visible), 0, 1)` with `visible = 1/zoom` (lands exactly where aimed). `viewportOrigin = travel * (1 - 1/zoom)` (so the viewport is inside the frame by construction). Then the three arrays (amount, travelX, travelY) run through a **critically-damped spring** (stiffness 200, mass 2.25, damping 40; semi-implicit Euler at 240 substeps/s, stepped per output frame) so retargeting keeps velocity. Zoom is applied to the *content including the pointer and ring* (zoom magnifies them) but captions/images are drawn **after** the zoom (unmagnified).

Idle pointer fade: pointer counts as parked when per-frame travel < 0.0006 (normalised); after **2.5 s** still it fades out over 0.4 s (`1 - smoothstep(hidden)`), and fades back in over 0.25 s *before* the next movement (known in advance). Click punch: pointer scales to **0.8** from 0.13 s before a press (smoothstep) to release, easing back over 0.13 s. Click ring: only for click-downs, 0.4 s long; `eased = 1-(1-p)^3`; radius `22 * unit * eased` where `unit = pointerPixelSize/34`; line width `max(0.6, 2.4*(1-p)) * unit`; alpha `0.35*(1-p^2)`; white; a burst restarts the same ring. Pointer drawn at `pointSize * pointerScale * press` with `pointerScale = displayScale * max(1,systemScale) * userSize`; hotspot honoured; if the take captured no shape at all, draw the **fallback arrow** (vector outline in a 28x40 box, hotspot (4.5, 4.0): points (4.5,4) (4.5,31.5) (10.5,25.5) (14.5,35.5) (18.5,33.8) (14.6,24) (22.5,23.5); black fill, white 2.4-unit round-join outline, shadow blur 2.2 alpha 0.35 offset 0.8 down). Pointer is drawn only when the plan has pointer samples, `showsPointer`, the OS had a visible pointer at that moment and `opacity > 0.01`, and the position is within -0.2..1.2.

Overlay opacity helper `overlayOpacity(t,start,end,fade)`: 0 outside; ramp = `min(fade, duration/2)`; smoothstep up over the first ramp and down over the last ramp; text and image fade = **0.25 s**; blurs never fade.

#### 5.4.7 Timeline and clocks
- `keptRanges(trim, cuts)` = the trimmed span minus the (normalised, merged, >= 0.1 s) cuts, dropping slivers <= 1 ms. `outputDuration = sum of ranges`. `sourceTime(forOutput)` walks the ranges; `outputTime(forSource)` returns nil if the moment was cut (the playhead then lands on the nearest kept range start).
- A cut can never leave < 0.4 s (`canCutSelection`); picking a cut range needs >= 0.1 s. After cutting: selection cleared, composition rebuilt, playhead to the cut start. Clicking an orange seam restores that cut (matches a cut containing `time +-0.15`).
- Zoom normalise: clamp, drop `< 0.4 s`, sort, push the later of two overlapping zooms to start where the earlier ends (drop if that leaves < 0.4 s). Add at playhead: default length **2.0 s**, standing inside a zoom pushes the new start to its end, refused when < 0.4 s of room; new zoom inherits the last amount used (else `document.zoomAmount`). Move keeps length and stays between neighbours; resize keeps >= 0.4 s and cannot cross neighbours. Text/image/blur blocks: min **0.4 s** in lane edits (and `>= 0.2 s` on sanitise); move clamps to `[0, duration - length]`.
- New text: starts at playhead, length 3.0 s, anchor bottom, size 0.06, colour white, placeholder text `Your text here`. New image: file picker first; copies the image into the take folder (`<take>/<guid>/<filename>`, validated as a decodable image, size/space checks, mode 0600); runs from the playhead to the **end** of the recording; defaults anchor bottomTrailing, size 0.18, opacity 1. New blur: starts at playhead, runs to the **end** (a hidden thing that comes back is worse than a long blur), default rect (0.35, 0.40, 0.30, 0.20), strength 3, then immediately enters draw mode. Text max 200 chars; text size range 0.03..0.16 of canvas height; image size 0.04..0.6 of canvas width, opacity 0.05..1; blur strength 1..5; blur min side 0.01 of the picture; hard edges (on/off exactly at its time span).
- Anchor placement (9 anchors topLeading, top, topTrailing, leading, center, trailing, bottomLeading, bottom, bottomTrailing): margin = `min(canvasW, canvasH) * 0.05`; `x = margin + (canvasW - contentW - 2*margin) * ux`; `y(top) = margin + (canvasH - contentH - 2*margin) * uy`. Image drawn size = canvasWidth*size (height from aspect), shrunk to fit inside canvas minus margins, rounded.
- Text palette (sRGB): white (1,1,1), black (0.06,0.06,0.07), accent (0.04,0.52,1.0), yellow (1.0,0.80,0), red (0.96,0.26,0.21), green (0.20,0.78,0.35). Text rasterised **once** (cache key text|pointSize|palette, max 48 entries): semibold system font, `pointSize = max(8, round(canvasHeight * size))`, drop shadow black@0.55, blur `0.18 * pointSize`, offset `0.04 * pointSize` down, padding `0.5 * pointSize` around the glyph box; multi-line allowed.
- Image sprites cached by (path, size, file size + mtime), budget 24M pixels; decoded no larger than drawn size, resampled high quality.

#### 5.4.8 Undo/redo and persistence
Undo stack = whole `EditDocument` values (cheap). A change to `document` pushes the previous value, clears redo, writes `edit.json`, and triggers the cheapest refresh: timing change (trim/cuts) -> rebuild the playback timeline; audio change -> update the mix only; picture change -> rebuild the preview plan after **120 ms** debounce. **Interactions** (drag a block, slider drag, typing text) call `beginInteraction` (snapshot once) and `commitZoomEdit` on release -> exactly one undo entry. Undo/redo apply with undo suppressed, persist, refresh, and seek to the trim start. Documents are persisted only while an editor owns the take.

#### 5.4.9 Editor lifecycle and keyboard
- Opening: loads duration, size, nominal frame rate (sanitised to 30/60), pointer + typing tracks, edit.json (or new document from preferences), audio tracks (matched by metadata id `com.vorssaint.recorder.audio.system|microphone` with a fallback: first unmatched track = system, second = microphone -> **[WIN]** sidecar file names), generates first-time zooms, builds the filmstrip (14 frames at slot centres `(i+0.5)/14 * duration`, 240 px max, 0.5 s seek tolerance), builds the playback composition and preview.
- Playhead observer 30 Hz; when playing and `current >= outputDuration - 0.02` it pauses and seeks to 0. Play at the very end restarts from 0. Seek is frame-accurate (zero tolerance).
- Closing: if an export is running it is cancelled. If at least one file was ever written (`exported`) or the discard was confirmed, the window closes silently; otherwise the discard alert is shown first. Closing always **deletes the take folder** (the recording only exists in its editor). `Copy and delete` closes without the alert after a successful clipboard write.
- Exporting never closes the window ("exports are slow and plural"); a successful export sets `exported = true`, shows the finished-file chip, calls `RecentCaptureService.recordRecording(at:)` and HUD `Saved to <folder name>` (not for clipboard copies).
- Keyboard (editor window must be key; while a text box has focus only Save / Save as are handled): `Ctrl+S` save MP4; `Ctrl+Shift+S` Save as…; `Ctrl+Z` undo; `Ctrl+Shift+Z` (also accept `Ctrl+Y`) redo; `Ctrl+C` copy video; `Ctrl+Alt+C` copy and delete (macOS ⌥⌘C); `Space` play/pause; `Delete`/`Backspace` removes, in priority order: the pending cut selection (performs the cut), selected zoom, selected text, selected image, selected blur, else asks to delete the whole recording; `Esc`, in order: cancel export; end aim mode; end blur-draw mode; deselect zoom / text / image / blur; else nothing (event passes through). In the inspector's back link `Esc` also backs out.

#### 5.4.10 Export pipeline (behaviour)
- **Save** (`Ctrl+S`, primary): export MP4 to the configured folder with the unique name; remember `lastExportedURL`. **Save as…**: native save dialog with the suggested name/folder, replaces an existing file only when the new one is **complete**. **Save as GIF**: GIF to the configured folder. **Copy**: MP4 into Copied Recordings, then the clipboard receives the file (`CF_HDROP`), HUD `Recording copied`. **Copy as GIF**: GIF into Copied Recordings, then `RecorderGIFClipboard.publish` (below), HUD `Recording copied`. **Copy and delete**: Copy, then close the editor (deleting the take).
- All exports write to a hidden sibling staging file `.vorssaint-partial-<guid>.<ext>` (**[WIN]** `.trayloom-partial-<guid>.<ext>`, hidden attribute) in the destination folder and only `File.Replace`/`File.Move` over the destination at the end, so a cancelled or failed export never harms an existing file; staging is deleted on every exit. Cancel is honoured within about a frame and again just before the replace.
- Failure -> system beep + HUD `The recording could not be saved` (cancel is silent). Failures enum: noVideo, readFailed, writeFailed, tooLargeForSharing, invalidShareArtifact, cancelled.
- Progress: video = `min(0.9, framesDone/expectedFrames * 0.9)` while encoding, 0.95 while reader completes, 1.0 when the file is closed (never parks at "almost done" while sound is still being written); GIF = `min(0.99, (i+1)/frameCount)`; progress callbacks are throttled to <= 10/s and >= 0.5 % steps.
- Video export steps: (1) kept ranges -> one continuous timeline (video + both audio tracks cut identically); (2) if `exportSpeed != 1` scale the *whole* timeline once (video times and audio) with pitch-preserving audio (spectral) and the speed applied after trim and cuts; (3) build the plan on the unscaled edited clock and map output time back via `sourceTime(out * speed)`; (4) canvas = `canvasSize(...)*quality.outputScale` (the preset is folded into the canvas, so the background is rendered at its shipped size, never resampled); (5) frame rate = the take's nominal rate (30 or 60); output frames at a steady rate resampled from the variable-rate master; (6) audio mix = keep flags and gains per track, AAC 48k stereo 160 kbps; (7) HEVC if the encoder passes a one-frame probe, else H.264 High (`hevcEncodes` test); video settings per 5.3.3; no reordering. Only when the document has **no** effects does the plain scaling path run (it is only valid with nothing drawn on the picture); if composing an edited recording fails the export **fails** (never silently emits a file missing the blurs).
- **GIF export**: same composer/timeline as the video (cuts, speed, backdrop, pointer, zoom, overlays), no audio; `fps = gifFrameRate` (8/12/15); `frameCount = max(1, floor(outputDuration * fps))`; frame i samples time `min(outputDuration, i/fps)`; size via 5.3.3 (long edge 420/600/800); a frame that fails to decode repeats the previous (frames before the first decodable take the first) so the length is preserved; zero decodable frames -> `.readFailed`. Delay per frame = difference of **cumulative** rounded centiseconds (`round(n*100/fps)`) so 12/15 fps do not drift. Header `GIF89a`, NETSCAPE2.0 infinite loop. macOS encodes in chunks of 300 frames through ImageIO and stitches; **[WIN]** own encoder streams frame by frame (5.5.8).
- **GIF clipboard** (`RecorderGIFClipboard.publish`): verify the file is a real GIF with >= 1 frame **before** clearing the clipboard; payload = GIF bytes (native "GIF" flavour) only if the file is <= **64 MB**, plus a file reference so attachment-oriented apps paste the file; larger GIFs go as the file reference only (after ~256 MB macOS refuses inline data after already clearing). Run the check off the UI thread; failure -> beep + HUD `The recording could not be saved`.
- **Share**: only artifact allowed is the one produced by `exportForSharing` from a take the store owns (checked: folder == expected path, not a symlink, master regular file > 0 bytes). Encoding plan (`RecordingSharingSupport.encodingPlan`): frame rate `min(30, sourceFps)`; size = the quality-scaled canvas limited to **1920** long edge; audio bitrate 128 kbps if any audio kept else 0; `available = floor(90_000_000 * 8 * 0.94 / duration) - audio`; `qualityCeiling = w*h*fps*0.085`; `video = min(available, max(800_000, ceiling)) * min(1, scale)`; reject if `video < 350_000`; `scale = min(1, sqrt((video/(fps*0.055)) / (w*h)))` with a floor so the long edge stays >= 640; even sizes. Up to **3 attempts**: after each, if bytes > **96,000,000** compute `retryScale = min(current*0.9, current * 90e6/bytes * 0.94)` and retry; still too big -> `tooLargeForSharing` (HUD `This recording cannot fit under 100 MB without losing too much quality.`). The file is validated: regular, not a symlink, 0 < size <= 96 MB, exactly 1 video track (H.264) and <= 1 audio track (AAC). Progress phases: `Compressing for sharing…` (determinate) then `Uploading securely…` (indeterminate).
- **Upload** (`RecordingShareService`): `POST <endpoint>/v1/recordings?expiresIn=3600|21600`, headers `Content-Type: video/mp4`, `Accept: application/json`, `Cache-Control: no-store`, explicit `Content-Length`; ephemeral session (no cache, no cookies), request timeout 180 s, resource 300 s; success = HTTP **201** with <= 64 KB JSON `{id, viewPath, expiresAt, deleteToken}`; validations: `id` matches `^[A-Za-z0-9_-]{32}$`, `deleteToken` `^[A-Za-z0-9_-]{43}$`, `viewPath == "/s/" + id`, expiry in the future and <= now + 6 h + 5 min. 429/503/507 -> "unavailable", other non-201 -> "rejected". The link record `{id, endpoint, expiresAt, deleteToken}` is persisted (`records.json` in a private folder) **before** the upload so a crash can still be cleaned; view URL = `<endpoint>/s/<id>`; delete = `DELETE <endpoint>/v1/recordings/<id>` with `Authorization: Bearer <deleteToken>` (204 or 404 = success); expired records are pruned on refresh and on wake from sleep, and an expiry timer re-prunes at the next expiry. After upload the link is copied to the clipboard (HUD `Link copied`) and the result sheet shows it. Failure HUD: `The temporary link could not be created`. The production endpoint on macOS is a Vorssaint-operated host -> **[WIN] open decision (5.6.4)**: do not point at it.
- **Recent captures**: every file the recorder finishes (direct save or editor Save) is added to the recent-captures history with a thumbnail of the first video frame.

#### 5.4.11 Media tools integration
`RecorderTakeStore.importVideo(at:)` lets the Media workspace "video compressor" open any ordinary movie in this same editor (`owner: .mediaTools`): it copies the file as a take master (must be a regular non-symlink file, size > 0, free space >= size + 500 MB), has no pointer/typing/audio-source metadata (pointer/zoom controls hide; audio tracks matched by the fallback rule), and survives uninstalling the recorder (closed only when Media tools is uninstalled). Port when Media tools gets the same flow.

---

### 5.5 Windows mapping (exact pipeline)

Target: .NET 9, `net9.0-windows10.0.19041.0`, WPF; unpackaged desktop app. Allowed NuGet: NAudio 2.2.1 (already referenced by `Trayloom.Features.System`). Everything below uses only the BCL, built-in WinRT projections, Media Foundation / D3D11 / DXGI COM via hand-written `[ComImport]`/`[DllImport]` interop **unless a line is flagged "ask first"**.

#### 5.5.1 Process/threads overview
```
 UI thread (WPF)  <-- state/HUD/indicator
 CaptureThread    : WGC FrameArrived (free-threaded) -> GPU copy/convert -> enqueue sample
 VideoWriterThread: IMFSinkWriter.WriteSample (serial)          = macOS "writerQueue"
 AudioLoopThread  : WASAPI loopback packets -> resample/gap-fill -> WAV sidecar
 AudioMicThread   : WASAPI capture packets  -> resample -> WAV sidecar
 PointerThread    : 125 Hz GetCursorInfo (time-critical, high-res timer)
 HookThread       : shared WH_MOUSE_LL / WH_KEYBOARD_LL (IInputHooks) -> click/typing times
```
All time stamps are **QueryPerformanceCounter** (100 ns units): WGC `Direct3D11CaptureFrame.SystemRelativeTime` is QPC-based; WASAPI `qpcPosition` is QPC-based (100 ns); hook handlers sample `Stopwatch.GetTimestamp()`. One `PauseClock` origin = QPC at the first accepted video frame (macOS: `beginSession`). Because everything is one clock, `RecorderSampleTiming.converted` is not needed.

#### 5.5.2 Capture sources (Windows.Graphics.Capture)

| macOS | Windows |
|---|---|
| Area / display | `GraphicsCaptureItem` for the **monitor** (`IGraphicsCaptureItemInterop.CreateForMonitor(HMONITOR)`), then crop the picked rectangle on the GPU |
| Window | `IGraphicsCaptureItemInterop.CreateForWindow(HWND)` (window's own pixels, even if covered; follows resizes) |
| Whole screen | monitor item, full rect (Return key in chooser) |
| Camera | not in Vorssaint; none |

Interop (no NuGet): `[ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(InterfaceIsIUnknown)] interface IGraphicsCaptureItemInterop { IntPtr CreateForWindow(IntPtr hwnd, ref Guid iid); IntPtr CreateForMonitor(IntPtr hmon, ref Guid iid); }`, activated with `RoGetActivationFactory("Windows.Graphics.Capture.GraphicsCaptureItem")` (IID of `IGraphicsCaptureItem` = `79C3F95B-31F7-4EC2-A464-632EF5D30760`) and wrapped with `WinRT.MarshalInspectable<GraphicsCaptureItem>.FromAbi(ptr)`. Check `GraphicsCaptureSession.IsSupported()`; if false (pre-1803 or policy) -> HUD `The screen could not be recorded`.

Pipeline per recording:
1. `D3D11CreateDevice(adapter of the monitor, D3D11_CREATE_DEVICE_BGRA_SUPPORT | VIDEO_SUPPORT)`; wrap with `CreateDirect3D11DeviceFromDXGIDevice` (d3d11.dll) -> `IDirect3DDevice`.
2. `Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, numberOfBuffers: 3, item.Size)`; subscribe `FrameArrived` (runs on a pool thread; **do not** touch the UI thread).
3. `session = pool.CreateCaptureSession(item); session.IsCursorCaptureEnabled = false` (the pointer is drawn by the editor from the pointer track; leaving it on would bake a second pointer in - needs 19041+). `session.IsBorderRequired = false` only if available (Windows 11/Win10 2104+ and `GraphicsCaptureAccess.RequestAccessAsync(Borderless)` allowed); the yellow capture border is drawn by the OS **outside** the captured pixels and cannot be disabled for unpackaged apps in general: document it, do not fight it. On Windows 11 22H2+ set `session.MinUpdateInterval = TimeSpan.FromSeconds(1.0/fps)` to implement the 30/60 fps cap; on older builds drop frames whose `SystemRelativeTime` delta < `0.9/fps`.
4. In `FrameArrived`: `frame = pool.TryGetNextFrame()`; **skip stale/identical frames** (WGC only delivers when the desktop changes - this gives the same *variable frame rate* as the Mac master: a still screen costs nothing). If `frame.ContentSize != lastSize` (window resized) -> `pool.Recreate(...)` with the new size for the next frames and **scale the frame into the fixed take size** (Mac `scalesToFit` + `preservesAspectRatio` for windows) using the video processor below with letterbox fill (black); never change the take resolution mid-file.
5. **Region crop on the GPU** (area recordings): get `ID3D11Texture2D` from `frame.Surface` via `IDirect3DDxgiInterfaceAccess.GetInterface(IID_ID3D11Texture2D)`; `ID3D11DeviceContext.CopySubresourceRegion(dst, 0, 0, 0, 0, src, 0, &D3D11_BOX{left=x, top=y, right=x+w, bottom=y+h, front=0, back=1})` into a pooled `DXGI_FORMAT_B8G8R8A8_UNORM` texture of exactly the even take size (BIND_SHADER_RESOURCE | RENDER_TARGET, `D3D11_RESOURCE_MISC_SHARED` not required). x/y/w/h = `Region.pixelRect` in monitor pixels (the monitor item's origin is the monitor's top-left). Full-monitor recordings can pass the capture texture straight through if size == take size. Release the capture frame as soon as the copy is queued (the pool has only 3 buffers).
6. Hand the texture to the encoder (5.5.3) with timestamp `frame.SystemRelativeTime`.
7. Stop: `session.Dispose(); pool.Dispose(); item.Closed` (window closed / monitor removed) -> treated as an unexpected stop: finish the file and open the editor with what exists.

Own-window exclusion (macOS excludes the indicator, region guide and HUD from the stream; the rest of Trayloom stays recordable): call `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE = 0x00000011)` on the indicator pill, region-guide window and HUD windows (Windows 10 2004+; on older builds the affinity call fails -> accept the artefact or fall back to `WDA_MONITOR`, which renders black, not acceptable: just show them). The WGC monitor item then simply does not contain them. For window capture nothing else is needed.

Multi-monitor/DPI: the app must be Per-Monitor-V2 DPI aware (it already is per ARCHITECTURE.md) so rectangles are physical pixels; `Region.scale = dpi/96`; the take stores pixel sizes and `displayScale = 1` (5.4.3). HDR: if the monitor reports advanced colour (`DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020`), request `R16G16B16A16Float` and tone-map to SDR BGRA in the video processor, or fail gracefully with HUD `The screen could not be recorded`; mark as a known limitation.

Per-frame cost target: capture -> copy -> encode with zero CPU readback (hardware encoder + D3D11 surfaces).

#### 5.5.3 Encoding: **IMFSinkWriter**, not MediaTranscoder

Chosen: Media Foundation `IMFSinkWriter` created with `MFCreateSinkWriterFromURL(path, null, attributes, out writer)`. Reasons: (1) it takes `ID3D11Texture2D` samples directly (`MFCreateDXGISurfaceBuffer` + `MFCreateSample`; attribute `MF_SINK_WRITER_D3D_MANAGER` = `IMFDXGIDeviceManager` created with `MFCreateDXGIDeviceManager`/`ResetDevice`) so the whole path stays on the GPU with a hardware H.264/HEVC MFT (`MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = 1`); (2) it accepts arbitrary per-sample timestamps (the take is variable-rate); (3) exact control of bitrate/GOP/profile/B-frames through `ICodecAPI` (`CODECAPI_AVEncCommonRateControlMode = eAVEncCommonRateControlMode_PeakConstrainedVBR` or `UnconstrainedVBR`, `CODECAPI_AVEncCommonMeanBitRate`, `CODECAPI_AVEncMPVGOPSize = fps * keyframeSeconds`, `CODECAPI_AVEncMPVDefaultBPictureCount = 0`, `CODECAPI_AVLowLatencyMode = true` for the live take); (4) fragmented MP4 for crash resilience (`MF_TRANSCODE_CONTAINERTYPE = MFTranscodeContainerType_FMPEG4`, fragment interval via `MF_MPEG4SINK_MOOV_BEFORE_MDAT` unset; plays the Mac's 10 s `movieFragmentInterval` role); (5) the same API serves the offline export. `Windows.Media.Transcoding.MediaTranscoder + MediaStreamSource` is **rejected for the take** (live latency, no pause discontinuity control, no fMP4, hardware zero-copy only via a private path) but is an acceptable fallback if COM interop proves too costly: `MediaStreamSource(new VideoStreamDescriptor(VideoEncodingProperties.CreateUncompressed("NV12"/"Bgra8", w, h)))`, `SampleRequested` supplying `MediaStreamSample.CreateFromDirect3D11Surface(surface, timestamp)`, `MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p)` with `Video.Bitrate`, `Video.Subtype = "H264"|"HEVC"`.

Writer setup (`RecorderWriter` port):
- Output media type: `MF_MT_MAJOR_TYPE=Video`, `MF_MT_SUBTYPE = H264 | HEVC`, `MF_MT_AVG_BITRATE = averageBitRate(w,h,fps,High)`, `MF_MT_FRAME_RATE = fps/1`, `MF_MT_FRAME_SIZE = w x h`, `MF_MT_INTERLACE_MODE = Progressive`, `MF_MT_PIXEL_ASPECT_RATIO = 1/1`, `MF_MT_MPEG2_PROFILE = eAVEncH264VProfile_High` (H.264) / `eAVEncH265VProfile_Main_420_8` (HEVC), BT.709 (`MF_MT_VIDEO_PRIMARIES=MFVideoPrimaries_BT709`, `MF_MT_TRANSFER_FUNCTION=MFVideoTransFunc_709`, `MF_MT_YUV_MATRIX=MFVideoTransferMatrix_BT709`). Input type = `MFVideoFormat_ARGB32` (BGRA) so the sink writer inserts the hardware colour converter (or `NV12` if the video processor already converted).
- **HEVC probe** = `MFTEnumEx(MFT_CATEGORY_VIDEO_ENCODER, MFT_ENUM_FLAG_HARDWARE | MFT_ENUM_FLAG_SORTANDFILTER, null, {Video, HEVC})` then encode one blank frame (as `hevcEncodes`); any failure -> H.264. HEVC needs the "HEVC Video Extensions" or a hardware vendor MFT; H.264 is always present.
- **VFR / first frame**: first appended frame is time **0** (hold the first image over start-up latency), later frames use `mapped = pauseClock.sampleTime(start, duration)` (null = inside a pause -> drop whole sample); `SetInputMediaType` once; `BeginWriting()` before the first sample; `WriteSample(0, sample)` with `SampleTime` in 100 ns, `SampleDuration` = time to next frame (if unknown use `1/fps`); on `Finalize`, write the **last frame again at the stop instant** (`pauseClock.elapsed(stopQpc)`) so a recording ending on a still screen is as long as it felt, then `NotifyEndOfSegment` is not needed; `Finalize()`.
- **Backpressure**: encoder input can block; WGC delivers on its own threads so queue samples (max ~8) on `VideoWriterThread`; if the queue is full drop the frame (the macOS writer does the same: `isReadyForMoreMediaData` false -> `.notReady`).
- **Failure modes**: `MF_E_TRANSFORM_*`/`E_INVALIDARG` on size (odd sizes) -> sizes are even by construction; any `WriteSample` failure sets `failed` and later samples are dropped; `Finalize` returns false if no frame was written (`videoFrameCount == 0`) -> `cancelWriting` equivalent: delete the file and report `writerFailed`.
- **Audio in the take (decision)**: write **two sidecar files** (`system.wav`, `mic.wav`: 48 kHz, 2 ch, IEEE float32, written with NAudio `WaveFileWriter`) rather than extra MP4 audio streams, because the MPEG-4 sink's multi-audio-track behaviour is unverified and the editor needs independent gain/mute anyway. Header lengths are patched at stop; a crash leaves a recoverable (truncated) WAV. The Mac's "mixed on export through an audio mix" becomes "mixed by our own mixer at export" (5.5.7). **Direct save** (no editor) must produce a normal single-audio MP4: mix the sidecars (keep flags = true, gains = 1) and encode AAC while **remuxing the compressed H.264/HEVC video untouched** (source reader with `MF_SOURCE_READER_...` passthrough: read with `MF_MT_SUBTYPE` = the encoded subtype and set the same type as the sink writer's *input* so no transcode happens); if remux is not possible, do a full export with the default (Balanced) settings.

#### 5.5.4 Audio capture

**System sound ("PC sound")** - WASAPI loopback on the default **render** endpoint:
- Whole screen/area: `NAudio.CoreAudioApi.MMDeviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)` + `WasapiLoopbackCapture(device)` (shared mode, event driven, mix format = device mix format, usually 32-bit float 48 kHz stereo; convert/resample to 48 kHz stereo float with `WdlResamplingSampleProvider`/`MultiplexingSampleProvider` when different). Use `AudioClient` directly if NAudio's `DataAvailable` timestamps are insufficient: `AudioCaptureClient.GetBuffer(out frames, out flags, out devicePosition, out qpcPosition)` gives the QPC of each packet; check `AudioClientBufferFlags.DataDiscontinuity`/`Silent`.
- **Silence gaps**: loopback delivers **no packets while nothing plays**. The writer must fill gaps: before appending a packet at `qpc`, compute `expectedFrames = round((qpc - nextExpectedQpc)*48000/1e7)`; if > 0 insert that many zero frames (cap each gap to avoid memory blow-ups), and at stop pad silence up to the stop instant. Pause: drop packets that overlap a pause interval (whole packet if it crosses), exactly like `sampleTime`.
- **Window recording follows the picture** (macOS taps only the window owner's processes): use **process loopback** (documented as Windows build 20348+, i.e. Windows 11 / Server 2022; present on recent Windows 10 21H2+ builds too - probe at runtime and fall back to endpoint loopback on `E_NOTIMPL`/activation failure): `ActivateAudioInterfaceAsync("VAD\\Process_Loopback", IAudioClient, AUDIOCLIENT_ACTIVATION_PARAMS{ ActivationType = PROCESS_LOOPBACK, ProcessLoopbackParams{ TargetProcessId = ownerPid, ProcessLoopbackMode = PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE } })`, then `Initialize(Shared, AUDCLNT_STREAMFLAGS_LOOPBACK | EVENTCALLBACK | AUTOCONVERTPCM | SRC_DEFAULT_QUALITY, hnsBuffer, 0, &waveFormat48kStereoFloat)` (the format must be supplied; `GetMixFormat` is not supported on this client). NAudio 2.2.1 has no wrapper -> small P/Invoke class `ProcessLoopbackCapture` (flag **"ask first"** only if a library is wanted; hand-written interop is within rules). Browsers/Electron apps play from child processes: `INCLUDE_TARGET_PROCESS_TREE` covers them (this replaces the macOS process-list rebuild logic).
- **Exclude Trayloom's own sound** (macOS `excludesCurrentProcessAudio`): for whole-system recordings, if process loopback is available prefer `PROCESS_LOOPBACK_MODE_EXCLUDE_TARGET_PROCESS_TREE` with Trayloom's own PID (the mixer/HUD sounds stay out); else plain endpoint loopback.
- Device changes (headphones plugged): handle `IMMNotificationClient.OnDefaultDeviceChanged` -> stop and re-open the loopback on the new default endpoint, keep inserting silence on the shared clock; no re-verification logic is needed (this is why `recorderSystemAudioTapVerified` is dropped).

**Microphone** - `NAudio.CoreAudioApi.WasapiCapture` on `GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications|Multimedia)` (event-sync, shared; convert integer/mono/other rates to 48 kHz stereo float32; mono duplicated to both channels). Timestamps via `AudioCaptureClient.qpcPosition`, gap/pause handling as above. Start order: system loopback, then mic, then pointer/typing samplers (matches the macOS ordering so the head of the file has the shortest gap). Failure to open (privacy switch off, device busy, no mic) -> `onMicrophoneUnavailable` -> HUD `Microphone unavailable`, recording continues.

#### 5.5.5 Pointer / clicks / typing capture
- Pointer: `GetCursorInfo(ref CURSORINFO{cbSize, flags, hCursor, ptScreenPos})` each 8 ms (use a high-resolution waitable timer `CreateWaitableTimerEx(CREATE_WAITABLE_TIMER_HIGH_RESOLUTION)` or `timeBeginPeriod(1)` + `Thread.Sleep` correction; thread priority `AboveNormal`). Normalise: `u = (pt.X - region.origin.X) / region.width`, `v = (pt.Y - region.origin.Y) / region.height` where origin = the monitor's virtual-screen origin + `pixelRect` origin; **requires DPI-aware process** so coordinates are physical. Visible = `(flags & CURSOR_SHOWING) != 0 && (flags & CURSOR_SUPPRESSED) == 0`.
- Shape capture only when `hCursor` differs from the last one (cheap equality) or every ~1 s to catch animated/changed cursors that reuse a handle: `GetIconInfoEx(hCursor)` -> hotspot (`xHotspot`,`yHotspot`) + `hbmMask/hbmColor`; render to a 32-bpp top-down DIB with `DrawIconEx(hdc, 0, 0, hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL)` (size 0 = the size the user actually sees, already scaled by the cursor-size and DPI settings). **Alpha**: for colour cursors with alpha the DIB has correct premultiplied alpha; for monochrome/XOR cursors (I-beam in text fields, many legacy cursors) draw onto black then onto white and derive `alpha = 255 - (white - black)`, `color = black / alpha`; then store as PNG (premultiplied -> straight) in the track. Identity = FNV-1a over pixels + size + hotspot. Animated cursors (`.ani`) arrive frame by frame; keep only the first frame once identity repeats (same rule as the macOS strip case).
- Cursor size: effective size comes from `DrawIconEx`; no separate scale needed: write `systemScale = 1`, `displayScale = 1`, `pointSize = bitmap pixel size`, `hotSpot` in bitmap pixels. (Optional: read `HKCU\Control Panel\Cursors\CursorBaseSize` / `GetSystemMetricsForDpi(SM_CXCURSOR)` only for the fallback arrow sizing.)
- Clicks: `IInputHooks.SubscribeMouse` (shared `WH_MOUSE_LL`) for `WM_LBUTTONDOWN/UP`, `WM_RBUTTONDOWN/UP` (ignore injected events from Trayloom's own `TrayloomExtraInfo`), handler stamps `Stopwatch.GetTimestamp()` and enqueues; handlers must be < 1 ms. Fallback without hooks: poll `GetAsyncKeyState(VK_LBUTTON/VK_RBUTTON)` inside the 125 Hz loop (misses sub-8 ms taps).
- Typing: `IInputHooks.SubscribeKeyboard` (`WM_KEYDOWN`/`WM_SYSKEYDOWN`); keep a 256-bit "currently down" set and record only the **transition** to down (auto-repeat ignored); store only the time. Never store vkCode/scancode/text.
- All three samplers live only between start and stop; hooks are subscribed in `start()` and disposed in `stop()` (the shared hook installs lazily).

#### 5.5.6 Take files, indicator, HUD, hotkey, power
- Take store: `%LOCALAPPDATA%\Trayloom\Recordings` (not Roaming; large). Free space = `new DriveInfo(root).AvailableFreeSpace`. Restrict ACL to the current user (`DirectorySecurity`, inheritance removed).
- Indicator pill / region guide / HUDs: `OverlayWindow` base from Core (borderless, topmost, `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, per-monitor DPI), `AllowsTransparency=true`, `Topmost`, `WDA_EXCLUDEFROMCAPTURE`. The region guide is `WS_EX_TRANSPARENT` (click-through). Pill shadow = `DropShadowEffect`. The dot pulse = WPF `DoubleAnimation` on Opacity (1->0.28, 0.85 s, autoreverse, `SineEase InOut`, RepeatBehavior.Forever).
- HUD (`QuickToolHud`) and countdown ring are shared Core components (Screenshot spec); the recorder only calls `Show(icon, message)` and `ShowCountdown(n)`.
- Global hotkey: `IHotkeyService.Register`; `Hotkey.Parse("Ctrl+Alt+Shift+5")`. The Windows key is reserved for the shell - do not use Win+... defaults.
- Sleep prevention while recording: `SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED)` on start, `ES_CONTINUOUS` on stop (add `ES_DISPLAY_REQUIRED` so the screen does not blank mid-recording; document as intentional difference if desired).
- Elapsed timer: `DispatcherTimer` 1 s (not used for timestamps).

#### 5.5.7 Editor engine on Windows

Goal (from the Swift architecture): **one composer decides what a frame looks like, for the editor preview and for export**, so they cannot disagree. Port the pure logic **exactly** (5.4.2-5.4.7): `RecorderPlan` (per-output-frame arrays: positions, zoom, travel, pressScale, ring progress, pointer visibility/opacity/shape index, per-overlay opacity, blur cover flags) built once from the edit document + pointer track.

**Composer (recommended, no NuGet): WPF visual composer** `RecorderFrameComposer`:
- Input: BGRA source frame (as `BitmapSource`/`WriteableBitmap`) + `plan` + output frame index. Output: BGRA frame at the canvas size.
- Visual tree (built once per plan, mutated per frame): root `Canvas` of canvas size -> background plate (`Image` pre-rendered once: gradient/solid/image + optional `BlurEffect`; shadow = `DropShadowEffect` or pre-drawn blurred rounded rect, offset y = `min(h*0.012, 26)`, blur `min(h*0.045, 90)`, black@0.42) -> **content group** (`Canvas` with `RenderTransform` = scale z about the viewport + translate by `-origin*size*z`, clip to the source rect) containing: source frame `Image`, blur mosaics (`Image`s of the pixelated region, see below), pointer sprite `Image` (RenderTransform scale=press, translate to `hot` position), click ring (`Ellipse`, stroke white, `Opacity`) -> card transform (`scaleX/Y = cardRect/source`, translate to card origin) + **rounded-rect `Clip`** (`RectangleGeometry` with `RadiusX/Y = corner`) -> images layer -> texts layer (rasterised text bitmaps with their anchors/opacity, never zoomed).
- **Blur regions**: for each active region compute the pixel rect (`pixelRect(in:)`: bottom-origin maths becomes top-origin on WPF) -> crop the frame (`CroppedBitmap`) -> mosaic: average blocks of `block` px (CPU block average or `TransformedBitmap` scale-down with `BitmapScalingMode.Fant` then nearest-neighbour upscale via `RenderOptions.BitmapScalingMode=NearestNeighbor`), then `BlurEffect(Radius = block * 0.6)`; the effect is applied **before** pointer/zoom so zoom magnifies the blur and the pointer travels over it; clamp sampling to the picture so no transparent edge leaks. Include the macOS safeguard: when the output frame time falls **between two plan samples**, a region counts as covering if *either* neighbouring sample (`floor`/`ceil` of `t*fps`) is covered, so retiming can never uncover a frame early.
- Z-order inside the content: source -> blurs -> ring -> pointer (so the zoom carries them); then rounded mask over the plate; then images; then texts.
- Per-frame pixel read-back: `RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32).Render(root)` then `CopyPixels` into a reusable `byte[]` (or `WriteableBitmap`) -> feed the sink writer. Runs on a **dedicated STA dispatcher thread** (WPF visuals are thread-affine; use `Dispatcher.CurrentDispatcher` on a new `Thread { ApartmentState = STA }` and `RenderOptions.ProcessRenderMode`-independent offscreen rendering). Reuse visuals; only update transforms/opacities/bitmaps per frame. Expected ~8-20 ms/frame at 1080p; 4K exports are slower but correct (offline).
- Text rasterisation: WPF `FormattedText` -> `DrawingVisual` -> `RenderTargetBitmap`, semibold `Segoe UI Variable`/`Segoe UI`, with `DropShadowEffect` black@0.55 (BlurRadius `0.18*pt`, ShadowDepth `0.04*pt`, Direction 270) - cached per (text, size, palette).
- Playback preview: **own decode loop** (below) -> plan lookup by current output time -> same composer on the UI dispatcher (preview canvas sized to the stage; the composer renders at the **canvas** size scaled down by the stage for display; for performance the preview may render at <= 1280 px long edge). A `ZoomLane`-style interactive overlay on the stage (aim crosshair, blur-draw rectangle) sits above the composed image.

*Why not MediaElement/MediaPlayer for preview*: cannot crop/pixelate frames per region or share the composer with export; cuts would need a pre-baked composition. *Why not Windows.Media.Editing.MediaComposition*: trim/overlay/audio-track volume exist, but there is no way to do the spring-solved zoom, pixelated blur and pointer sprites without writing a custom `IBasicVideoEffect` WinRT component (heavy; rejected). 

**Decode loop** (`RecorderSourceReader`): `MFCreateSourceReaderFromURL(take.mp4)` with `MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING = 1` (YUV -> RGB32 conversion, hardware where possible); `SetCurrentMediaType` = `MFVideoFormat_RGB32`; seeks via `SetCurrentPosition` (frame-accurate = seek to previous keyframe then decode forward and drop until time >= target; the take's 2 s keyframes keep this < ~0.5 s); because the take is variable-rate the loop keeps `lastFrameAtOrBefore(t)` and holds it. Cuts are handled **logically**: the kept ranges list maps output time -> source time (`sourceTime(forOutput:)`); when playback crosses a range boundary seek the source reader to the next range start (prefetch the next keyframe on a background thread to hide the gap). Audio playback: NAudio `WasapiOut`/`WaveOutEvent` fed by a `MixingSampleProvider` of the two WAV sidecars through `VolumeSampleProvider` (gain, or 0 if removed) with the same range mapping (`OffsetSampleProvider` per range or a custom `RangeConcatSampleProvider` that seeks the `AudioFileReader`); the audio clock is the master for A/V sync (video frame chosen by audio position). Scrubbing pauses audio and seeks the decoder; frames come back on a worker thread and are posted to the UI.

**Export pass** (`RecorderExporter` port): worker thread: for output frame `i` at `t_out = i / outFps` (outFps = take nominal 30/60, or share plan's fps): `t_src_edit = t_out * speed`; `t_src = sourceTime(forOutput: t_src_edit)`; fetch the latest source frame <= `t_src` from the decoder (sequential read: since time is monotonic, step the source reader forward and hold; at a cut boundary seek); compose (5.5.7) -> `MFCreateMemoryBuffer`/`MFCreateSample` (system-memory ARGB32) -> `sinkWriter.WriteSample(video, sample)` with `SampleTime = i*10_000_000/outFps`, duration one frame; audio: pull from the mixer in 20-48k-sample blocks: sample-accurate gain, kept ranges concatenated, **speed** applied with a pitch-preserving time-stretch (WSOLA/overlap-add: window 40 ms, overlap 50 %, hop adaptive by speed; own ~150-line implementation, or `ffmpeg atempo` chain `atempo=2.0,atempo=2.0` for >2 and `atempo=0.5` steps for <0.5 when ffmpeg is present) -> AAC via an `IMFSinkWriter` audio stream (input PCM 16-bit; output `MFAudioFormat_AAC`, 48000 Hz, 2 ch, 16 bit, `MF_MT_AUDIO_AVG_BYTES_PER_SECOND = 20000` = 160 kbps (the AAC encoder only accepts 12000/16000/20000/24000 bytes/s), `MF_MT_AAC_PAYLOAD_TYPE = 0` raw AAC, `MF_MT_AAC_AUDIO_PROFILE_LEVEL_INDICATION = 0x29`); for the share plan use 16000 bytes/s (128 kbps). Interleave audio and video (write audio blocks whenever its timestamp <= current video time; sink writer needs ordered per-stream timestamps). Progress per 5.4.10. `Finalize()` then rename (staging pattern). Cancellation: flag checked every frame/block.

*GPU upgrade path (flag "ask first")*: **Vortice.Windows** (`Vortice.Direct3D11`, `Vortice.Direct2D1`, `Vortice.MediaFoundation`) lets the composer run as a Direct2D effect graph that matches Core Image 1:1 (`Scale`, `Crop`, `GaussianBlur`, `Composite`, `Opacity`, custom pixelate effect or `Scale`+nearest, `DirectWrite` text, rounded-rect `Geometry` layers, shadow effect) and removes the CPU read-back; also gives a safer MF wrapper. SharpDX is unmaintained -> prefer Vortice. Until approved, ship the WPF composer.

*ffmpeg path (optional, only when `ffmpeg.exe` is found - reuse `FfmpegLocator` from MediaTools: PATH, then `%ProgramFiles%\ffmpeg\bin`, then a bundled `tools\ffmpeg.exe` if the project decides to bundle a GPL build; flag as "ask first")*: can replace the encoder (receive composed BGRA frames over stdin: `ffmpeg -y -f rawvideo -pix_fmt bgra -s {W}x{H} -r {fps} -i - -i mix.wav -c:v libx264|hevc_mf|h264_nvenc|hevc_nvenc -b:v {bits} -g {fps*keyframeSeconds} -bf 0 -pix_fmt yuv420p -colorspace bt709 -color_primaries bt709 -color_trc bt709 -c:a aac -b:a 160k -movflags +faststart out.mp4`), and do **everything except the pointer/zoom** natively if desired. Filter-graph building blocks (source time `t` in seconds, kept ranges, all expressed on the *recording clock*):
  - trim + cuts: per kept range `[a,b]`: `[0:v]trim=start=a:end=b,setpts=PTS-STARTPTS[v0]; [1:a]atrim=start=a:end=b,asetpts=PTS-STARTPTS[a0]` then `concat=n=N:v=1:a=1`;
  - speed S: `setpts=PTS/S` and audio `atempo=S` (chain `atempo=2,atempo=S/2` above 2; `atempo=0.5` stages below 0.5);
  - crop (area/aspect): `crop=w:h:x:y`; canvas/backdrop: `color=c=#223344:s=WxH` or `gradients=s=WxH:c0=...:c1=...:x0=0:y0=0:x1=W:y1=H` (ffmpeg >= 6) as base, card `scale=cw:ch,format=rgba,geq=lum='p(X,Y)':a='if(gt(abs(X-cw/2),cw/2-r)*gt(abs(Y-ch/2),ch/2-r)*gt(hypot(abs(X-cw/2)-(cw/2-r),abs(Y-ch/2)-(ch/2-r)),r),0,255)'` for rounded corners, shadow `boxblur=lr:lp` on a black copy of the card offset then `overlay`;
  - blur region (mosaic + blur): `[v]split[a][b];[b]crop=bw:bh:bx:by,scale=bw/B:bh/B:flags=area,scale=bw:bh:flags=neighbor,boxblur=B*0.6:1[m];[a][m]overlay=bx:by:enable='between(t,s,e)'`;
  - text: `drawtext=text='...':fontfile=C\:/Windows/Fonts/segoeuib.ttf:fontsize=h*size:fontcolor=white:shadowcolor=black@0.55:shadowx=0:shadowy=h*size*0.04:x=...:y=...:enable='between(t,s,e)':alpha='...fade expr...'`;
  - image overlay: `movie=logo.png,scale=iw*k:-1,format=rgba,colorchannelmixer=aa=opacity[logo];[v][logo]overlay=x:y:enable='between(t,s,e)'` (fades via `fade=in:st=s:d=0.25:alpha=1,fade=out:st=e-0.25:d=0.25:alpha=1`);
  - auto-zoom: the spring-solved `zoom`/`travel` arrays are per-frame; either feed a `sendcmd`/`zoompan` expression generated from the arrays (`zoompan=z='<table>':x='<table>':y='<table>':d=1:s=WxH:fps=F`, using a piecewise-linear lookup in `lerp()` expressions per segment) or (recommended) bake the zoom in the .NET composer and pipe frames;
  - pointer: not expressible practically (hundreds of thousands of positions) -> composer.
  - GIF: `fps={gifFps},scale={W}:-1:flags=lanczos,split[s0][s1];[s0]palettegen=max_colors=256:stats_mode=diff[p];[s1][p]paletteuse=dither=none:diff_mode=rectangle` (`-loop 0`).

#### 5.5.8 GIF encoder (own, a port of `RecorderGIFWriter`'s *output behaviour*)
macOS reuses ImageIO's quantiser/LZW per chunk and stitches chunks; Windows has no equivalent (`GifBitmapEncoder` quantises poorly with a fixed palette, writes no frame deltas, and needs per-frame metadata for delays: `BitmapMetadata("gif")` with `/grctlext/Delay` in centiseconds and `/appext/Application = "NETSCAPE2.0"`, `/appext/Data = [3,1,0,0,0]`; acceptable only as a last-resort fallback). Implement `GifStreamWriter`:
1. Quantise: build **one global palette** from a sample of frames (e.g. every 10th frame, up to ~2 M pixels) with Wu's colour quantiser or median-cut to 255 colours + 1 transparent index; screen content is flat so **no dithering** (default); optional 4x4 ordered dither toggle internal only.
2. Per frame: map to indices (nearest in a 5-5-5 RGB lookup cache); compute the **changed bounding rectangle** vs the previous frame; first frame full; unchanged pixels inside the rect become the transparent index with disposal method 1 (do not dispose); when a frame changes most of the canvas write it whole with disposal 2 / 1 appropriately. (This mirrors "only the changed part of each later frame" from ImageIO's output.)
3. LZW: variable code size 2..12 bits, clear code on table full (4096), sub-blocks <= 255 bytes, image descriptor with local colour table only if the palette differs.
4. Structure: `GIF89a`, logical screen (w,h up to 65535), global colour table, `NETSCAPE2.0` loop count 0 (infinite), per frame Graphic Control Extension (`0x21 0xF9 0x04`) with disposal, transparency flag, delay `round((i+1)*100/fps) - round(i*100/fps)` centiseconds, transparent index; trailer `0x3B`. Frames are streamed to disk as produced (no 300-frame chunking needed because quantisation is ours); memory = 2 frames.
5. Workspace: `%TEMP%\Trayloom\GIF Exports\<guid>\animation.gif` with an exclusive-open lock file `.lease` (opened `FileShare.None`); on start sweep sibling folders whose lease can be opened (abandoned by a crash) - same semantics as `RecorderGIFWorkspace` using `FileStream(FileShare.None)` instead of `flock`; copy the finished file to the staging sibling then replace the destination (cancel-safe).
6. Limits: width/height 1..65535; fps 1..100 validated; every frame must have the same size.

**GIF clipboard (Windows)**: build one `System.Windows.DataObject`: `SetData("GIF", MemoryStream gifBytes)` (registered format name `GIF`, read by browsers/Discord/Slack/Teams) when size <= 64 MB; `SetFileDropList(new StringCollection{path})` always (`CF_HDROP`); optional `SetImage(firstFrame)` (`CF_BITMAP/DIBV5`) as a static fallback; `Clipboard.SetDataObject(obj, copy: true)`. Validate the file (`GIF8` magic, >= 1 image block) before touching the clipboard; do the validation and the big-memory read off the UI thread, then marshal. Retry the clipboard open (`CLIPBRD_E_CANT_OPEN`) up to 5 times, 20 ms apart. Copy video = `SetFileDropList` with the MP4 (+ `Preferred DropEffect` copy).

#### 5.5.9 Permissions, privacy and other macOS concepts
- Screen Recording permission: **none** (WGC shows the yellow border; no consent dialog for desktop apps). Accessibility permission: **none** (hooks/polling). Microphone: Windows 10/11 privacy switch (`Settings > Privacy > Microphone`; apps blocked -> `WasapiCapture` start fails or returns silence): detect via `HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone` (`Value=Allow|Deny`, plus the `NonPackaged` subkey for desktop apps) for the Settings note; otherwise rely on the open failure -> HUD.
- Window picking needs owner PID: `GetWindowThreadProcessId`; for UWP/ApplicationFrameHost windows resolve the real app via `Windows.UI.Core.CoreWindow` child; document fallback = whole-system loopback for those.
- Protected/DRM content (Netflix, etc.) and windows with `WDA_EXCLUDEFROMCAPTURE` record as black or missing - nothing to do.
- Elevated windows (UAC/Task Manager as admin): WGC window capture of an elevated window from a non-elevated app is allowed for monitor capture; hooks do not see input to elevated windows (clicks/typing times missing) - known limitation.

---

### 5.6 Gap vs Trayloom

State of the port (checked with `grep -ri record`/`GraphicsCapture`/`MediaFoundation`/`ffmpeg`/`wasapi` over `E:\REA TEST 1\trayloom\src`): **there is no screen recorder.** Matches are unrelated (`HotkeyRecorder` UI control, `Features.System/Audio/*` NAudio device helpers, `MediaToolsFeature` + `FfmpegLocator` for compressing existing files, `Screenshot` tool). Everything in sections 5.1-5.5 is missing. Reusable existing pieces:

| Existing file | Use |
|---|---|
| `Features.Tools/Support/FrozenScreenWindow.cs` (`RegionSelectorWindow`, `PickAsync`) | region/window picking; needs the unified chooser (tool strip 1-4, audio pill, Return = whole display, even-pixel snapping, no freeze for recording) |
| `Features.Tools/Support/ScreenGrab.cs` (`VirtualScreen`, `TopLevelWindows`, `BitmapUtil`) | monitor/window enumeration helpers; add PID + HMONITOR + DPI |
| `Features.Tools/Support/FeatureBase.cs` (`ToolFeatureBase`, `BindHotkey`), `Core/Ui/SettingsCard.cs` (`SettingsViewBuilder`) | feature scaffolding + hotkey; builder lacks segmented picker, disclosure row, folder row with reset, link-count row - extend |
| `Core/InputHooks.cs` (`IInputHooks`) | click + typing times |
| `Core/Ui/OverlayWindow.cs` | indicator pill, region guide, countdown, HUD |
| `Features.Tools/MediaTools/MediaToolsFeature.cs` (`FfmpegLocator`) | optional ffmpeg discovery |
| `Features.System/Audio/*` + NAudio 2.2.1 | MMDevice enumeration, device-change notifications |
| `Features.Tools/Screenshot/ScreenshotEditorWindow.cs` | backdrop/blur concepts to share (BackdropStyle JSON, presets list `screenshotBackdropPresets`) |

Missing (everything), prioritised:

**P0 - capture to a playable file**
1. `ScreenRecorderFeature : ToolFeatureBase` (id `screen-recorder`, name `Screen recording`, description = `hubDescription`, FeatureAction `Screen recording: record / stop` + tile glyph swap), hotkey binding `recorderShortcut` (+ enabled toggle).
2. Settings view (5.1.1) incl. folder picker, disclosure, share section, privacy and shared-links sheets.
3. Unified chooser integration: Recording tab (audio pill, subtitle, even snapping, Return = full display) - coordinate with the Screenshot part.
4. `Recording/` capture stack: WGC item/frame-pool/session (`GraphicsCaptureInterop`, `WgcCapture`), D3D11 helper (device, region crop, scaling), `MfVideoWriter` (IMFSinkWriter, HEVC probe, fMP4, VFR, tail frame), `PauseClock`, `RecorderSession`, `RecorderIndicator` + `RegionGuideWindow` + countdown, disk-space watchdog, direct-save path (mux sidecars -> MP4).
5. Audio: `LoopbackCapture` (endpoint + process loopback), `MicCapture`, `SilenceFiller`, `WavSidecarWriter`.
6. Pointer/click/typing samplers + `PointerTrack` (VRPT v3 codec), `TypingTrack`, cursor shape catalog with mono-cursor alpha recovery.
7. `TakeStore` (folders, sweep, import image/video, ownership checks).

**P1 - editor and export**
8. Ported pure logic with unit tests: `RecorderSupport`, `RecorderMotion`, `RecorderTimeline`, `RecorderEditDocument` (+Look/Preset/ExportTiming), `RecorderTextOverlay/ImageOverlay/BlurRegion`, `ComposerPlan` (`makePlan`).
9. `RecorderEditorWindow` (WPF): top band, stage, inspector (all panels), bottom band, filmstrip, click ruler, lane control, audio lanes, export speed popover, quality menu, dialogs, keyboard map, undo/redo, persistence.
10. `RecorderFrameComposer` (WPF visual tree) + decode loop + audio playback; `RecorderExporter` (video + GIF + staging/commit/cancel/progress).
11. `GifStreamWriter` + GIF workspace + GIF clipboard; copy/copy-and-delete/save-as flows; recent-captures hook.

**P2 - extras**
12. Share: `RecordingShareService` (+ sharing support maths, records.json, expiry refresh, privacy/links sheets) - **blocked on a backend decision** (5.6.4).
13. Media-tools hand-off: `importVideo` into the editor.
14. Quick panel tile, command-bar entry, hub tour card, settings deep link anchor `screen-recorder`.
15. Presets menu (12 max) + preset image store.

**P3 - hardening**: HDR monitors, UWP-window audio PID resolution, protected-content black frames message, elevated-window input limitation, multi-GPU capture (create the D3D device on the adapter that owns the monitor), HEVC availability prompt.

#### 5.6.1 Proposed file/class layout

```
src/Trayloom.Features.Tools/ScreenRecorder/
  ScreenRecorderFeature.cs            // IFeature, actions, hotkey, settings view, panel tile state, toggle()
  ScreenRecorderService.cs            // state machine 5.4.1 (generation counter, countdown, stop/finish, deliver)
  RecorderSession.cs                  // one recording: engine + writer + audio + samplers
  Capture/
    GraphicsCaptureInterop.cs         // IGraphicsCaptureItemInterop, RoGetActivationFactory, D3D11 CreateDirect3D11DeviceFromDXGIDevice
    D3D11Interop.cs                   // minimal ID3D11Device/Context/Texture2D/VideoProcessor COM (or Vortice if approved)
    WgcCaptureEngine.cs               // frame pool, session, region copy, resize handling, lifecycle (idle/starting/running/stopped)
    RecorderRegion.cs                 // Region + snapping/evenSize
  Encode/
    MfInterop.cs                      // MF COM + P/Invoke (sink writer, source reader, DXGI manager, MFT enum, ICodecAPI)
    RecorderVideoWriter.cs            // RecorderWriter port (HEVC probe, fMP4, VFR, tail frame, backpressure)
    RecorderSupport.cs                // pure policies (quality, bitrate, canvas, trim, disk, filmstrip, elapsed label)
    PauseClock.cs                     // PauseTimeline + PauseClock + StartGate
  Audio/
    LoopbackCapture.cs                // endpoint + process-loopback
    ProcessLoopbackInterop.cs         // ActivateAudioInterfaceAsync
    MicrophoneCapture.cs
    SidecarWavWriter.cs, SilenceFiller.cs, AudioMixer.cs (gains, keeps, range-concat), TimeStretch.cs (WSOLA)
  Tracks/
    PointerSampler.cs, CursorCatalog.cs (GetIconInfoEx/DrawIconEx + hash + mono alpha), PointerTrack.cs (VRPT v3),
    TypingTrack.cs / TypingSampler.cs, ClickSampler.cs
  Editing/
    RecorderMotion.cs, RecorderTimeline.cs, RecorderEditDocument.cs (+ExportTiming, Look, EditPreset),
    TextOverlay.cs, ImageOverlay.cs, BlurRegion.cs, ComposerPlan.cs,
    RecorderFrameComposer.cs, TextRenderer.cs, ImageRenderer.cs, CursorSprite.cs (fallback arrow + ring)
  Export/
    RecorderExporter.cs, GifStreamWriter.cs, GifWorkspace.cs, GifClipboard.cs, SharingSupport.cs, ShareService.cs
  Store/
    TakeStore.cs, PresetImageStore.cs
  UI/
    RecorderEditorWindow.xaml(.cs), RecorderEditorModel.cs (undo, interactions, lanes API),
    RecorderInspector.cs, TimelineLane.cs (zoom/text/image/blur), AudioLane.cs, Filmstrip.cs, ClickRuler.cs,
    ExportSpeedPopover.cs, IndicatorPill.cs, RegionGuideWindow.cs, ShareSheets.cs (result, privacy, links)
```
Shared (coordinate with other parts): `QuickToolHud` and countdown view, `BackdropStyle` + backdrop popover, recent-captures service, unified chooser.

#### 5.6.2 Gaps in shared Trayloom infrastructure needed first
- `IInputHooks` mouse events must expose button-up/down for left/right with a QPC-quality timestamp; the shared hook must tolerate start/stop per recording.
- `SettingsViewBuilder`: add segmented picker, disclosure group, folder-with-reset row, navigation row with count.
- `OverlayWindow`: add `ExcludeFromCapture` (SetWindowDisplayAffinity) and click-through options.
- Core `Hotkey`: parse/format `Ctrl+Alt+Shift+5` (digits) - verify.

#### 5.6.3 NuGet / "ask first" list
| Item | Why | Status |
|---|---|---|
| Vortice.Windows (D3D11/D2D/MF) or SharpDX | GPU composition, safer COM wrappers | **ask first**; not required (hand COM + WPF composer is the default) |
| ffmpeg.exe bundling (GPL build) | GIF palettegen, atempo, optional encoders | **ask first**; use `FfmpegLocator` discovery otherwise |
| Windows.Win32 (CsWin32 source generator) | P/Invoke signatures for MF/WASAPI | **ask first** (build-time only) |
| NAudio 2.2.1 | WASAPI loopback/mic, resampling, mixing, WAV | allowed |

#### 5.6.4 Open questions for the lead
1. **Sharing backend**: macOS uploads to a Vorssaint-operated service (`screenshots.vorssaint.com`; `/v1/recordings`, 1 h/6 h expiry, <= 96 MB). Trayloom must not use it (branding/ownership). Options: (a) ship the feature UI behind `recorderSharingEnabled` but disabled until a Trayloom endpoint exists (hide the share menu and the `Temporary links` section when no endpoint is configured), (b) point to a self-hosted compatible service configured via a hidden `screenshotSharingDeveloperEndpoint`-style key. Contract is fully specified in 5.4.10.
2. **HEVC vs H.264 default for exports on Windows**: replicating the macOS "HEVC when the encoder passes" gives smaller files, but stock Windows Media Player/Photos need the paid-free "HEVC Video Extensions" to play it, and Chrome/Teams may not preview it. Recommend defaulting the *export* to H.264 on Windows and keeping HEVC only for the take when hardware encodes it (take is internal). Needs a decision (no Settings row exists in the original for this).
3. **Direct-save container**: `.mp4` (recommended) vs `.mov`.
4. **Bundled audio decoder/encoder**: AAC encoder MFT is built into Windows 8+ (OK); confirm no codec pack is required on N/KN editions (Media Feature Pack).
5. **Window-audio follow** needs Windows 10 build 20348+/Windows 11 for process loopback; on older builds fall back to whole-system loopback (still functional).
6. **Take audio as sidecar WAVs** (recommended) vs multi-audio-stream MP4: verify the MPEG-4 sink with two audio streams before choosing the second.


---

## 6. Small tools

## Part 04-6: Six smaller tools

Covers 6.1 Scratchpad, 6.2 Camera Preview, 6.3 Cleaning Mode, 6.4 Kill Process, 6.5 Port Manager, 6.6 Wallpaper, 6.7 Feedback.
Source of truth: `E:\REA TEST 1\vorssaint-mac\Sources\Vorssaint` (called "the Swift" below). English strings are the `enUS` blocks, verbatim; typographic quotes `“ ”`, apostrophes `’` and ellipses `…` are real Unicode characters in the originals. `[WIN]` marks a deliberate platform-noun substitution; the original text is given next to it and the original must stay the source for translations.

Conventions used in all sections:
- Mac modifier triple `⌃⌥⌘` (Control+Option+Command, used by every default shortcut in this part) has no direct Windows equivalent. Recommended mapping: `Ctrl+Alt+Win`. Alternative if the shortcuts spec (04-x / Settings spec) already decided otherwise: follow that. Open question for the integrator, see each section.
- Icon column: SF Symbol -> Segoe Fluent Icons (font "Segoe Fluent Icons", fallback "Segoe MDL2 Assets") code point. Code points marked (v) should be verified in the Segoe Fluent Icons gallery before use.
- "Settings page" for Camera preview, Wallpaper, Scratchpad and Cleaning mode is the shared **Quick tools** page (`SettingsPage.quickTools`), which holds one `Form` section per tool (`.formStyle(.grouped)`: grouped card sections with a small header text). Their section order on that page is: Quick Launcher, Quick Toggles, Quit apps, Mic mute, **Camera preview**, **Wallpaper**, **Scratchpad**, **Cleaning Mode** (the last four are defined here). Kill Process and Port Manager each have their own Settings page.
- All six tools are "idle" in the Features hub energy profile: badge text `Nothing at rest`, icon `leaf` (Segoe: `E945` (v) or a leaf glyph).
- Feature availability (Features hub install state) is a layer above every enable key: an uninstalled tool disappears from Settings, the panel, shortcuts and Command Bar and its service tears down. Installed-by-default: Scratchpad, Camera preview, Cleaning Mode = yes; **Wallpaper, Kill Process, Port Manager = no (opt-in)**. Kill Process is flagged Beta.

---

## 6.1 Scratchpad

### 6.1.1 Features-hub entry
| Item | Value |
|---|---|
| Hub title | `Scratchpad` (`pageTitle`) |
| Hub description | `Floating pads for short-lived notes` |
| Symbol | `note.text` -> Segoe `E70B` (v) / alternative `E8A5` (Document) |
| Group | Tools |
| Permissions | none |
| Energy | Nothing at rest |
| Hub click | opens Settings -> Quick tools -> Scratchpad section |
| Panel tile (menu panel "Utilities" block) | title `Scratchpad`, caption `Quick notes in separate tabs` (`panelCaption`), symbol `note.text`, shows shortcut hint when the shortcut is registered. Visibility key `panelUtilityScratchpad` (default true). Clicking closes the panel, waits 0.15 s, then opens the pad. |

### 6.1.2 What it is
A single floating, resizable, always-on-top pad for short-lived text with **up to 12 named tabs**. It autosaves every edit (debounced) to one small JSON document, renders an on-demand formatted Markdown "reading" (preview), has a Markdown formatting toolbar, find, list continuation, line moving, file export and optional auto-clear (retention). It steps aside on a click outside unless pinned. The same document is also shown inside the Dynamic Island ("Island" part of the spec owns `NotchScratchpadView`; this part only specifies the shared service/document so both agree).

### 6.1.3 Layout (floating pad)
Window: borderless, non-activating-style tool window that can still take keyboard focus, topmost ("floating") on all virtual desktops, no taskbar entry, drop shadow. Content size on first creation **380 x 300** px (DIPs); **minimum 280 x 220**; user-resizable (invisible 6 px resize border on the four edges, 12 px corner zones; resize never goes below minimum). Rounded **14 px** continuous corners, 1 px inner stroke `white @ 12 %`. Background = system HUD/acrylic material (Windows: Acrylic/Mica-Alt via `DWMWA_SYSTEMBACKDROP_TYPE`=3 Acrylic or `SetWindowCompositionAttribute`) with an extra opaque fill of the window-background colour laid over it at alpha = `scratchpadBackgroundOpacity` (0 = pure translucent material, 1 = fully opaque).

Vertical stack (VStack spacing 0):
1. **Header** (height 34, trailing padding 12)
   - Title text `Scratchpad` (`pageTitle`), 12 pt semibold, secondary colour, leading padding 12, fills width; the whole area is the **drag handle** (mouse-down starts a window drag; the rest of the pad never drags, so text selection works).
   - Pin button 22x22: icon `pin` (unpinned) / `pin.fill` (pinned, accent colour), 12 pt semibold. Tooltip + accessibility label: pinned -> `Close when I click outside`; unpinned -> `Keep open`. (Tooltip names the action a click would perform.) Segoe: `E718` Pin / `E77A` Unpin (v).
   - Close button 22x22: `xmark.circle.fill` 14 pt secondary, tooltip `Close` (`menuClose`, the global string). Hides the pad (does not delete anything). Segoe `E711`/`EA39` (v).
2. **Tab bar** (height 32, horizontal padding 10, bottom hairline divider at 45 % opacity, HStack spacing 6)
   - Horizontally scrolling tab strip (no scroll indicator, tab spacing 4, vertical padding 3). Auto-scrolls the selected tab to centre (0.15 s ease-out) on appear and on selection change.
   - **Tab** = button, height 24, min width 46, max width 130, leading padding 8, trailing 8 (4 when the close "x" is visible), corner radius 6; label 11 pt (semibold when selected, regular otherwise), one line, tail truncation; text colour primary when selected else secondary; background = accent @ 16 % when selected else clear. A small close "x" (`xmark` 8 pt semibold, 14x14 hit, secondary, tooltip `Close scratchpad`) shows only when more than one tab exists AND (tab is hovered OR selected). Accessibility: label = tab name, trait selected.
   - Tab context menu (right-click): `Rename scratchpad`, `Close scratchpad` (destructive; disabled when only one tab).
   - **New tab button** 22x22, `plus` 12 pt semibold, tooltip/label `New scratchpad`; disabled at 12 tabs, where the tooltip becomes `You can keep up to 12 scratchpads` (`padLimitFormat`, %d = 12). Segoe `E710`.
   - **Pad actions menu** 22x22 `ellipsis` 12 pt semibold (borderless menu, no indicator), tooltip/label `Scratchpad actions`; items: `Rename scratchpad`, `Close scratchpad` (destructive, disabled with a single tab). Segoe `E712`.
3. **Save-failed banner** (only while the last write failed): `Label` with SF `exclamationmark.triangle`, text `Your notes could not be saved. Copy them elsewhere before quitting.`, 10.5 pt secondary, leading, padding 12 horizontal / 6 vertical, wraps. Segoe `E7BA`.
4. **Editor area** (fills): plain-text editor (see 6.1.6) or, while previewing, the formatted reading overlay. Placeholder text `Type anything. It saves by itself.` drawn at the exact first-line position, tertiary colour, same font size as the editor, never intercepts clicks, only when the text is empty and not previewing. Editor text inset: 7 px horizontal, 2 px vertical, plus 5 px line-fragment padding; font = system UI font at `scratchpadTextSize` (default 13), colour = label colour; no background.
5. **Format bar** (only when "marks expanded" and not previewing; height 30, horizontal padding 12): see 6.1.5.
6. **Footer** (height 36, horizontal padding 12, spacing 12): icon-only buttons 26x26, icon 13 pt medium, secondary colour unless tinted, tooltip = accessibility label:
   | # | SF symbol | Segoe | Label / tooltip | Notes |
   |---|---|---|---|---|
   | 1 | `textformat` | `E8D2` (Font) (v) | `Formatting` | toggles the format bar; accent tint when expanded; animated 0.15 s ease-out; disabled while previewing; **never disabled by an empty pad** |
   | 2 | `eye` / `pencil` | `E7B3` / `E70F` | `Show formatting` / `Edit text` | toggles preview; accent tint while previewing |
   | 3 | `doc.on.doc` -> `checkmark` | `E8C8` -> `E73E` | `Copy all` -> `Copied` | after click the icon+label switch to check/`Copied` in **green** for 1.2 s (0.15 s ease-in, 0.2 s ease-out back) |
   | 4 | `square.and.arrow.down` | `E74E` | `Save as file` | export (6.1.8) |
   | - | Spacer | | | |
   | 5 | `trash` | `E74D` | `Clear` | clears the selected pad (undoable, 6.1.9) |
   Buttons 2-5 sit in one group that is **disabled and 50 % opacity when the selected pad's text is empty**.
7. Window border overlay: invisible resize hit zones (6/12 px) with resize cursors.

Dialogs (modal alerts that float over the pad; while one is up "modal interaction" is active and outside clicks must not close the pad):
- **Rename**: title `Rename scratchpad`, a text field prefilled with the current name, buttons `Cancel` (cancel role) and `Save` (`saveName`). Empty/whitespace-only result is rejected (name unchanged).
- **Close**: title `Close scratchpad`, message `Delete “<name>” and everything in it?` (`deletePadMessageFormat`), buttons `Cancel` and `Close scratchpad` (destructive). A pad with **no text closes without any confirmation**; a pad with text asks.

### 6.1.4 Strings (enUS, verbatim)
| Key | Text |
|---|---|
| pageTitle | Scratchpad |
| hubDescription | Floating pads for short-lived notes |
| panelCaption | Quick notes in separate tabs |
| openButton | Open scratchpad |
| placeholder | Type anything. It saves by itself. |
| copyAll | Copy all |
| copied | Copied |
| exportAction | Save as file |
| exportFailed | The file could not be saved |
| loadFailed | Your notes could not be opened. They were left unchanged. |
| saveFailed | Your notes could not be saved. Copy them elsewhere before quitting. |
| clearAction | Clear |
| retentionTitle | Clear on its own |
| retentionNever | Never |
| retentionDay | After a day unused |
| retentionWeek | After a week unused |
| retentionMonth | After a month unused |
| retentionCaption | The pad empties itself once the text goes that long without edits. |
| closeOnClickOutside | Close when I click outside |
| keepOpen | Keep open |
| backgroundOpacity | Pad background |
| backgroundTranslucent | Translucent |
| backgroundOpaque | Opaque |
| newPad | New scratchpad |
| padActions | Scratchpad actions |
| renamePad | Rename scratchpad |
| closePad | Close scratchpad |
| saveName | Save |
| cancel | Cancel |
| deletePadMessageFormat | Delete “%@” and everything in it? |
| padLimitFormat | You can keep up to %d scratchpads |
| previewFormatting | Show formatting |
| editText | Edit text |
| taskDone | Done |
| taskNotDone | Not done |
| markBold | Bold |
| markItalic | Italic |
| markStrikethrough | Strikethrough |
| markHeading | Heading |
| markBullet | Bulleted list |
| markNumbered | Numbered list |
| markQuote | Quote |
| markCode | Code |
| markLink | Link |
| formatMarks | Formatting |
| textSize | Text size |
Global strings reused: `menuClose` = `Close`; `quickToolShortcutToggle` = `Global shortcut`; `shortcutUnavailable` = `macOS rejected this shortcut. Choose another one.` ([WIN] `Windows rejected this shortcut. Choose another one.`). Default pad name: `Scratchpad 1`, `Scratchpad 2`, ... (= `pageTitle` + " " + number).

### 6.1.5 Format bar (Markdown marks)
Shared by the floating pad and the island (style param: pad vs island). In the pad: horizontally scrolling row (no indicator), HStack spacing 4, nine buttons **in this order**, each 26x26 (island 28x28), content-shape rounded rect radius 6 (island 9), colour secondary (island: white @ 55 %), tooltip + accessibility = mark label:
| Order | Mark | Button content | Segoe | Label | Markdown written |
|---|---|---|---|---|---|
| 1 | bold | SF `bold` 12 pt medium | `E8DD` | Bold | wraps selection in `**` |
| 2 | italic | `italic` | `E8DB` | Italic | wraps in `*` |
| 3 | strikethrough | `strikethrough` | `EDE0` (v) | Strikethrough | wraps in `~~` |
| 4 | heading | the **letter "H"** 13 pt semibold (not an icon) | text "H" | Heading | line prefix cycle `# ` -> `## ` -> `### ` -> removed |
| 5 | bullet | `list.bullet` | `EA64` (v) | Bulleted list | line prefix `- ` (on/off) |
| 6 | numbered | `list.number` | `E9D5`/`EA65` (v) | Numbered list | line prefix `1. ` renumbered 1., 2., 3. per selected line |
| 7 | quote | `text.quote` | `E9B5` (v) | Quote | line prefix `> ` |
| 8 | code | `chevron.left.slash.chevron.right` | `E943` | Code | wraps in `` ` `` |
| 9 | link | `link` | `E71B` | Link | `[selection](url)` with `url` selected |
Marks type the Markdown a user would type, **through the text view's undoable edit path** so one Ctrl+Z removes the whole mark. Clicking a button must not steal focus from the editor (the editor is re-focused and its selection restored).

### 6.1.6 Plain-text editor (`PlainTextEditor`, `PlainTextLineMover`)
Behaviour of the editor control (WPF `TextBox` with `AcceptsReturn`, `AcceptsTab=false`? -> Mac inserts a tab character; keep `AcceptsTab=true`, `TextWrapping=Wrap`, undo enabled; or a RichTextBox in plain mode):
- Pure plain text: no smart quotes/dashes, no text replacement, no autocorrect, no link/data detection, no rich paste (paste as plain text; drop images), no font panel. Spell-check off (`SpellCheck.IsEnabled=false`).
- Font size = `scratchpadTextSize`; may change live under an open editor (applied before text sync).
- Scrollbar vertical, auto-hiding. Text set programmatically (load, retention, restore) **clears the undo stack**; user edits keep it.
- Never replaces text while an IME composition is active.
- **Task box completion** (`completesTaskBoxes`): typing `]` directly after `- [` (also `* [`, `+ [`, indented, or inside `> `) on a list line inserts ` ]` so the result is `- [ ]`. Regex of the context: `^[\s>]*[-*+][ \t]+\[( |x|X)?\]([ \t]|$)` tested on the line-before-caret + `" ]"`; only on typing a single `]` with an empty selection; deleting the space later leaves `[]`.
- **Shift+Enter** (`continuesLists`) = continue the list (Return stays a plain newline): see `lineBreakEdit` below. **Ctrl+Enter** (Cmd+Return on Mac) toggles the formatted preview (`onCommandReturn`). Both are ignored during IME composition and only when the editor has focus.
- **Alt+Up / Alt+Down** (Option+Arrow) = move the current line, or every line a selection touches, past its neighbour (`PlainTextLineMover`). Exactly Alt (no Ctrl/Shift/Win).
- Find: Windows has no `NSTextFinder`; implement an in-pad find bar (Ctrl+F opens/focuses; Ctrl+G / Shift+Ctrl+G next/previous; Esc inside the find bar closes the bar first, the next Esc hides the pad). Incremental search, highlight all matches, match counter. Opening find while previewing first leaves preview. The find bar is hidden when entering preview.

**`lineBreakEdit(text, selection)`** (Shift+Enter): list marker regex `^([ \t]*(?:>[ \t]*)*)([-*+]|\d+[.)])([ \t]+)(\[[ xX]?\](?:[ \t]+|$))?`. Applied to the caret's line (line terminator trimmed). If selection length != 0, or caret is left of the end of the marker, or no match -> plain `\n`. If the caret is at the end of the line and the item is empty (only marker/box/whitespace) -> the list ends: the whole line content is replaced by empty (marker removed), caret at line start. Otherwise insert `"\n" + prefix + nextMarker + spacing + (hasBox ? "[ ] " : "")` where `nextMarker` = same bullet, or for numbered items `n+1` with the same delimiter (`.`/`)`), the largest representable number repeats. The edit is its own undo step; caret goes to end of insertion.

**`PlainTextLineMover.moving(direction, text, selection)`** returns new text + new selection or nil: block = full lines covered by the selection (line range includes terminator). Up: nil when block starts at 0; the line above is swapped with the block: `blockContent + aboveTerminator + aboveContent + blockTerminator` (the block's own terminator moves to the tail so a block that ended the text still ends it). Down: nil when block ends the text; mirror. Selection keeps its offsets within the moved content; if the selection included the block's terminator the new length uses the terminator of the line it traded with (it may be empty at the end of text). Only the two swapped lines are replaced in the text view (so undo restores two lines, not the whole note), as its own undo step; undo/redo restore the selection before/after. Works with `\n`, `\r\n`, `\r`.

### 6.1.7 Formatted reading (preview)
Toggle: footer eye/pencil button or Ctrl+Enter; ignored when the text is empty (`togglePreview` guards `!text.isEmpty`); collapses the format bar; the editor is kept (hidden, opacity 0, no hit-testing, undo history kept) beneath. Empty text always leaves preview. Parsed on demand with a Markdown parser (CommonMark + GFM-style `~~strike~~`, task lists) into blocks, never stored.
Block model: `paragraph`, `heading(1..6)`, `unorderedListItem(depth)`, `taskListItem(depth, checked, box)` (box = UTF-16 offset of the `[` in the source), `orderedListItem(ordinal, depth)`, `quote(depth)`, `code`, `thematicBreak`; list items inside quotes keep `quoteDepth`. Tables/images/HTML render as ordinary paragraphs of their text.
Rendering rules (base = `scratchpadTextSize`; system UI font; line spacing 2):
- Heading: font size base+5 (H1), base+3 (H2), base+1 (H3-H6), semibold.
- Bullet: prefix `"• "` (two spaces per extra depth before it). Ordered: `"<n>. "`. Nested items indent by the width of the parent marker.
- Quote: prefix `"▏ "` (U+258F) with two spaces per extra depth; text secondary colour. List inside a quote keeps the bar and secondary colour before its own marker.
- Code block: monospaced at base-1, background label-colour @ 7 %. Inline code: monospaced max(9, base-1) with the same background. Trailing newlines of a code block are trimmed.
- Thematic break: 24 x `─` in secondary colour.
- Inline: bold, italic, strikethrough (single underline-through), links in accent colour (clicking opens the URL in the default browser; non-http schemes follow `Process.Start` with shell execute).
- Blocks are joined by `\n\n`, except consecutive blocks of the same list/quote container joined by `\n`.
- **Task items**: the leading `[ ]`/`[x]`/`[X]`/`[]` is replaced by a drawn checkbox of size base+3: empty = hollow ring in tertiary colour (SF `circle` hierarchical), checked = accent-filled circle with white check (`checkmark.circle.fill`); a space follows. Checked task text is secondary and struck through. The checkbox is a hit target (hand cursor): clicking toggles the box in the **source text** as one undoable edit (`[ ]`<->`[x]`; bare `[]` becomes `[x]`) and re-renders; accessibility: role checkbox, name = task text, value = `Done`/`Not done` (`taskDone`/`taskNotDone`). A task is only recognised when `[ ]` opens a plain-text list item (not in code/emphasis/link, not escaped).
- The reading is selectable, read-only, links clickable. Redrawn only when blocks/base size/strings change (to keep selection).
Windows implementation: parse with a small hand-written parser or Markdig (NuGet -> **ask first**), render to a `FlowDocument` in a read-only `RichTextBox`/`FlowDocumentScrollViewer`; checkbox as an `InlineUIContainer`.

### 6.1.8 Export, copy, clear
- **Copy all**: copies the *selected* pad's text to the clipboard as plain text (no-op when empty). (Mac also marks the pasteboard as app-originated so Clipboard History ignores/labels it: on Windows set the clipboard-format flag the Clipboard History feature uses, e.g. `ExcludeClipboardContentFromMonitorProcessing` is NOT wanted; use Trayloom's own internal marker.)
- **Save as file**: only when text non-empty and no other modal is active. Flushes the document first, shows a Save dialog (plain text `.txt` filter, extension shown, can create folders) with suggested name `"<padName> yyyy-MM-dd.txt"` (date = today, ISO `yyyy-MM-dd`, invariant culture; pad name sanitised: runs of whitespace collapsed, trimmed, max 40 chars, `/` and `:` replaced by `-` — on Windows also replace `\ * ? " < > |`). Writes the pad's *current* text (re-read at confirmation time; a pad closed meanwhile counts as failure) as UTF-8 atomically. On failure shows the HUD `exclamationmark.triangle` + `The file could not be saved`. While the dialog is up, outside clicks never close the pad; afterwards the pad re-takes key focus.
- **Clear**: empties the selected pad *through the editor's undo path* (one Ctrl+Z restores everything while the pad stays open); commits any IME composition first; leaves preview if previewing; flushes the save immediately.

### 6.1.9 Document model and persistence
File: **`Scratchpad.json`** in the app data container (Mac: `PrivateFileStore.containerURL`; Windows: `%APPDATA%\Trayloom\Scratchpad.json` or `Ctx.DataDirectory`). Legacy single-pad file `Scratchpad.txt` is migrated (text becomes pad 1 `Scratchpad 1`, `modifiedAt` = file modification date), then deleted after a successful save. A legacy UserDefaults blob `scratchpadDocument` (Data, same JSON) is also migrated and the key removed after the first successful save. **Trayloom's existing `notes.json` (`NoteData`: Id, Text, X, Y, Width, Height, OnTop) must be migrated** into pads (one pad per note, ordered, max 12, name `Scratchpad n`, `modifiedAt`=file time) then archived.
JSON schema (Swift `Codable`): `{"pads":[{"id":"<UUID>","name":"...","text":"...","modifiedAt":<number|omitted>}],"selectedID":"<UUID>"}`; `modifiedAt` is the default `JSONEncoder` date = **seconds since 2001-01-01 UTC (Double)**, omitted when null. For Windows interchangeability with Mac settings backups keep this shape (a reader should also accept ISO-8601 strings).
Rules (`ScratchpadDocument.sanitized`): keep first 12 pads; drop duplicate ids; empty/whitespace names replaced by the next free `Scratchpad n`; names whitespace-collapsed and cut to 40 chars; `modifiedAt` forced null when text empty; if `selectedID` is unknown select the first pad; zero pads -> one empty `Scratchpad 1`.
- `nextPadName(default, existing)`: base = sanitised default (fallback "Scratchpad"); first candidate `"<base> 1"` if neither `<base>` nor `<base> 1` is used; else the lowest unused `"<base> n"` for n in 2...12; else `"<base> <count+1>"`.
- Operations (all validated, each saves immediately and aborts silently if the save fails): add pad (max 12, becomes selected, empty, focus), select, rename (sanitised, non-empty), remove (needs >1 pad; if the removed pad was selected, select the pad now at the same index, clamped), update selected text (only if changed; `modifiedAt = now` or null when empty).
- **Autosave**: every text change updates the in-memory document and schedules a flush **0.8 s** later (debounced; cancelled/replaced by each edit). Immediate flush on hide, tab operations, clear, task toggle, mark apply, export and app exit. A write counts only if the file read-back equals the data written; atomic write (temp + replace); directory created if missing. On failure the edits stay in memory, the inline banner appears and the next change retries; if the final flush on hide still fails show the HUD (`exclamationmark.triangle`, `saveFailed`).
- **Load** happens when the pad opens (not at app launch: no content read at launch). A read/decode error -> HUD `exclamationmark.triangle` + `loadFailed` and the pad does **not** open; no autosave or migration cleanup may happen after a failed load (`canSave=false`). If the in-memory doc differs from the last saved one it is flushed instead of reloaded.
- **Retention** (`scratchpadRetention`): evaluated **only at load/open time** against each pad's `modifiedAt`: idle > 1 day (86 400 s) / 7 days / 30 days -> that pad's text and `modifiedAt` are cleared (tab stays). Pads without `modifiedAt` and clock-backwards cases never clear. No timer.
- Settings backup/restore hooks: before export flush/load the document; after import drop the in-memory copy so exit-flush cannot overwrite the restored file.

### 6.1.10 Settings (Quick tools page -> Scratchpad section; header text `Scratchpad`)
Section rows in order:
1. Button (label with `note.text` icon) `Open scratchpad` -> opens the pad.
2. Caption (caption font, secondary): `Quick notes in separate tabs`.
3. Picker `Clear on its own` (pop-up/menu) with options `Never`, `After a day unused`, `After a week unused`, `After a month unused`.
4. Caption: `The pad empties itself once the text goes that long without edits.`
5. Toggle `Close when I click outside`. Changing it updates an open pad live.
6. Slider row `Text size` (label left, current integer value right, monospaced digits, secondary) — slider 10...22 step 1.
7. Slider row `Pad background` — slider 0...1 step 0.05, under it two caption labels left `Translucent` / right `Opaque`.
8. Toggle `Global shortcut` (`scratchpadShortcutEnabled`) + shortcut recorder row (role scratchpad) + orange caption `…rejected this shortcut…` if registration failed.
| Key | Type | Default | Range / values | Effect |
|---|---|---|---|---|
| scratchpadShortcutEnabled | bool | false | | registers the global hotkey |
| scratchpadShortcut | storage string | `⌃⌥⌘N` (Ctrl+Opt+Cmd+N) [WIN] suggest `Ctrl+Alt+Win+N` | any combo | toggle/open |
| scratchpadRetention | string | `never` | never, day, week, month (unknown -> never) | auto-clear idle pads at open |
| scratchpadCloseOnClickOutside | bool | true | | pad starts un-pinned when true; live update |
| scratchpadBackgroundOpacity | double | 0.0 | 0...1 (non-finite -> 1) | opaque fill over the material |
| scratchpadTextSize | double | 13 | 10...22, rounded to int (non-finite -> 13) | editor and preview base size; headings step from it |
| scratchpadDocument | Data | (legacy) | | migrated to file then removed |
| panelUtilityScratchpad | bool | true | | panel tile visibility |
| notchScratchpad / notchScratchpadControlHidden | bool | true / (one-time migration sets hidden) | | island integration, owned by the Island spec |
Pin state is **not** a preference: it is `!scratchpadCloseOnClickOutside` at open, toggled per session by the pin button. "Formatting row expanded" is also not persisted: every opening starts collapsed.

### 6.1.11 Behaviour: opening, closing, focus
- Triggers: panel tile, Settings button, global shortcut, Command Bar, island control, radial menu/quick launcher entries. `show(allowsIsland:)`: if the island hosts the scratchpad it moves there instead (island part); otherwise the floating pad.
- **Shortcut is a strict toggle only while the pad has the keyboard**: pad visible AND key window -> hide; visible but not focused -> grab focus; hidden -> show. Ignored while a modal (rename/close/save dialog) is active.
- Show: resets preview=false, marks collapsed, pin = !closeOnClickOutside; loads (with retention); position: keeps the last spot/size during the app run, re-centres only if the old frame no longer intersects any screen's work area. Centre rule: horizontally centred on the work area of the screen with the pointer; vertical: `y_top_from_bottom = workArea.minY + (workArea.height - panelHeight) * 0.58` (i.e. the pad sits 42 % of the free space below the top; AppKit coordinates), clamped to a **16 px** margin on all sides. Fade-in alpha 0 -> 1 over **0.13 s**; text caret placed at the **end of the text** and scrolled into view. Pad shows at the pointer's screen even when another app is active (no app activation on Mac; on Windows `Show()` + `Activate()` is fine but must not steal the foreground from an elevated app unexpectedly).
- Hide: commits edits (flush; HUD on failure), removes monitors, clears pin/preview/modal flags. Window object is kept (and discarded when the feature is uninstalled).
- Keys while the pad is focused (Mac Command -> Windows Ctrl): `Esc` hides (but not mid-IME composition, and not when a find bar field has the keyboard — Esc then closes the find bar first); `Ctrl+T` new tab (at the 12-tab limit the key is still swallowed so it never types a `t`); `Ctrl+W` close selected tab (confirmation rules above) or **hide the pad when only one tab remains**; `Ctrl+F`, `Ctrl+G`, `Shift+Ctrl+G` find. Tab/find shortcuts require "command only" (no Alt/Control-extra) and Shift only for G. All ignored while a modal is active (except Esc). Ctrl+Z / Ctrl+Y undo/redo in the editor.
- **Outside click**: when not pinned and no modal: a mouse-down (left/right/other) anywhere outside the pad's frame inset by -2 px hides it. The Accessibility/on-screen keyboard is exempt (Windows: ignore clicks whose target window is the Touch Keyboard / OSK `TabTip`/`osk.exe`). Windows: `WH_MOUSE_LL` (via the shared hook manager) watching `WM_LBUTTONDOWN/RBUTTONDOWN/MBUTTONDOWN/XBUTTONDOWN`, never consuming the click.
- Window title string (task switcher): app name.

### 6.1.12 Windows mapping
| Piece | Windows implementation |
|---|---|
| Panel | WPF `Window`, `WindowStyle=None`, `AllowsTransparency=true`, `ResizeMode=CanResizeWithGrip`/custom `WM_NCHITTEST` borders, `Topmost`, `ShowInTaskbar=false`, `WS_EX_TOOLWINDOW`; `MinWidth=280 MinHeight=220`; `SizeToContent=Manual`. Rounded corners 14: window `Clip` or Win11 `DWMWA_WINDOW_CORNER_PREFERENCE` (but 14 px differs from Win11's 8 px; use `AllowsTransparency` + `Border.CornerRadius=14`). |
| Material | Acrylic via `DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE, 3)` (Win11 22H2+) / `SetWindowCompositionAttribute(ACCENT_ENABLE_ACRYLICBLURBEHIND)` on Win10; fallback solid theme colour when transparency effects are disabled (system "reduce transparency"). Overlay fill = theme window-background colour at `scratchpadBackgroundOpacity`. |
| Virtual desktops | `.canJoinAllSpaces`: use `IVirtualDesktopManager`/pin API is unsupported publicly; acceptable: pad appears on the current desktop each time `Show()` is called; document as a difference. |
| Editor | `TextBox` (plain) or `RichTextBox` + custom commands; implement Shift+Enter, Ctrl+Enter, Alt+Up/Down, `]` completion via `PreviewKeyDown`/`PreviewTextInput`, with edits applied through `TextBox.Undo` grouping (`BeginChange/EndChange` + `DeclareChangeBlock`) so each mark is one undo step. Pure functions (`edit(applying:)`, `lineBreakEdit`, `PlainTextLineMover`, retention, naming, sanitising) should live in `Trayloom.Core` as testable static classes operating on UTF-16 offsets (.NET strings are UTF-16, same units as NSString). |
| Preview | FlowDocument renderer, checkbox glyphs `E739` (circle)/`E73E` check on accent circle. |
| Find | custom find bar (no OS equivalent): TextBox + counter + prev/next, highlighting via `TextRange` selection brushes. |
| Persistence | `System.Text.Json`, atomic temp file + `File.Replace`, read-back verify. |
| HUD | reuse the shared `QuickToolHud` (icon + message pill, radius 10, h 14/v 9 padding, 12 pt semibold, max 360 wide, 2 lines, 1.5 s, near top of the pointer screen). |
| Hotkey | `RegisterHotKey`/`BindHotkey`. |
| Elevation | none needed. |

### 6.1.13 Gap vs Trayloom (`Trayloom.Features.Tools\Scratchpad\ScratchpadFeature.cs`, 381 lines)
Has: floating always-on-top `NoteWindow`s (one window per note), plain `TextBox`, 700 ms debounced save to `notes.json`, pin (on-top per note), delete with confirmation, Esc hides, hotkey `Ctrl+Alt+N`, extra hotkey "new note"; description `Keep quick notes in floating pads that stay on top and save themselves.`
Prioritised gaps:
1. **Document model**: replace multi-window notes by one pad window with up to 12 renameable tabs and `Scratchpad.json`; migrate `notes.json`.
2. Settings page: retention picker, close-on-click-outside toggle, text size slider (10-22), background opacity slider (0-1), shortcut toggle/recorder (currently only two raw hotkey rows); description/name strings to the Mac text; panel caption.
3. Formatted preview + format bar (nine marks) + task boxes + list continuation + `]` completion + line mover + find bar — none exist.
4. Footer actions: Copy all (with 1.2 s Copied state), Save as file (.txt, suggested name), Clear (undoable), disabled-when-empty behaviour; HUD error toasts.
5. Outside-click dismiss (+pin toggle semantic inverted: Mac pin = "keep open", Trayloom pin = "on top"), 0.13 s fade-in, positioning rule (58 % rule, 16 px margin), 380x300 / min 280x220 sizes (Trayloom is 360x300, min 220x140), rounded 14 acrylic styling, header drag handle, resize borders.
6. Autosave semantics: 0.8 s debounce (Trayloom 0.7 s), read-back verify, banner on failure, no-save-after-failed-load, retention at open.
7. Island/panel integration (panel tile caption, island scratchpad) and Command Bar entry.

---

## 6.2 Camera Preview

### 6.2.1 Features-hub entry
| Item | Value |
|---|---|
| Hub title | `Camera preview` |
| Hub description | `Opens a floating mirror with your camera` |
| Symbol | `web.camera` -> Segoe `E722` (Camera) / `E8B2` (v) |
| Permission | Camera (`AppPermission.camera`) — Windows: "Camera privacy access for desktop apps" |
| Energy | Nothing at rest (the capture session exists only while the mirror is on screen) |
| Panel tile | title `Camera preview`, caption `Check how you look before a call` (or `Permission required: Camera` when camera access is denied; `permissionRequired` = `Permission required`), symbol `web.camera`, shortcut hint; when denied the tile shows a button `Open System Settings…` ([WIN] opens `ms-settings:privacy-webcam`). Visibility key `panelUtilityCameraPreview` (default true). Click closes the panel, waits 0.15 s, opens the preview. |
| Island | the island has an optional Camera module (`notchCameraEnabled`, default true) that hosts the same session (Island spec). When enabled, `show()` opens it inside the island instead of the floating mirror; the floating mirror and the island never capture at the same time. |

### 6.2.2 Strings (enUS)
| Key | Text |
|---|---|
| pageTitle | Camera preview |
| hubDescription | Opens a floating mirror with your camera |
| panelCaption | Check how you look before a call |
| openButton | Open preview |
| cameraMenuLabel | Camera |
| deniedMessage | Camera access for Vorssaint is turned off in System Settings. ([WIN] `Camera access for Trayloom is turned off in Settings.`) |
| noCameraMessage | No camera detected |
| permName | Camera |
| permExplain | Shows your camera only in the preview window, so you can check how you look before a call. Nothing is recorded or leaves your Mac. ([WIN] `…leaves your PC.`) |
Also reused: `permissionOpenSettings` = `Open System Settings…` ([WIN] `Open Settings…`), `permissionMissing` = `Not granted`, from NotchActivityStrings: `cameraUnavailable` = `The camera could not start. Try opening it again.`, `startCamera` = `Open camera`. `shortcutUnavailable` as in 6.1.

### 6.2.3 Layout (the mirror)
Floating borderless panel, **320 x 240** (4:3), always on top, all desktops, no taskbar entry, drop shadow, movable by dragging anywhere on its background.
- Background solid **black**, content clipped to rounded rect **radius 14**, 1 px stroke `white @ 14 %`. All controls render in dark colour scheme regardless of system theme.
- Content by state:
  - `idle`, `waitingPermission`, `starting`: small spinner (`ProgressView` small, white tint) centred.
  - `running`: live preview filling the frame, **aspect-fill** (crop, `UniformToFill`), **always mirrored horizontally** (flipped on every camera, "like Photo Booth").
  - `denied`: status block (icon `video.slash` 26 pt light, white @ 55 %; message `deniedMessage` caption white @ 80 %, centred, wraps; padding 28 horizontal; spacing 10) + small button `Open System Settings…`.
  - `unavailable`: same block with `cameraUnavailable` + small button `Open camera` (retries capture).
  - `noCamera`: icon `web.camera`, text `No camera detected`, no button.
- **Camera picker** (only when state=running and >1 camera and the pointer is over the mirror): capsule at bottom centre, bottom/horizontal padding 10, fade 0.15 s; capsule background black @ 55 %, white content: icon `web.camera` 10 pt semibold + camera name (caption, 1 line) + `chevron.up.chevron.down` 8 pt semibold, padding 10 x 5. If the name does not fit the width, the name is dropped and only the icon+chevron remain. Click opens a menu of cameras (names), the selected one with a check mark (`checkmark`). Accessibility label `Camera`. The island version hides this overlay picker (its header row carries it).

### 6.2.4 Behaviour
- Open: guard feature available and user session active. Position: top-centre of the **screen with the pointer** (work area): `x = midX - w/2`, `y = top - h - 48` (48 px below the top edge), clamped to 16 px margins. Fade 0 -> 1 over 0.13 s, takes keyboard focus (so Esc works). Then begins capture.
- Close triggers: `Esc` (while the mirror is focused); a mouse-down (left/right/other) outside the mirror (frame inset -2 px) — local and global; **activation of any other application** (user "joined the meeting"); the shortcut (toggle); feature uninstalled/suspended/session switched. **While the permission prompt may be up (`waitingPermission`) neither outside clicks nor app activations close it**, and an app activation within **1 s** after the permission prompt resolved is ignored. Clicks on the on-screen/assistive keyboard are exempt.
- Capture lifecycle: the capture session (camera light) exists only while visible; every hide stops and tears it down, resets `devices`, `selectedDeviceID`, state=idle. A generation token invalidates callbacks from older starts.
- Permission flow (`beginCapture`): authorised -> start; not determined -> state=waitingPermission and request access; on result refresh permissions, ignore if no longer presented/stale generation, record the resolve time, force window alpha 1 (the dialog was observed to leave the panel transparent), granted -> start else `denied`; denied/restricted -> `denied`.
- Start session: stop any old session first (two sessions must never run); state=starting; install device-change observers; list cameras; none -> `noCamera`; pick `userPreferredCamera` if it is in the list else the first; configure **medium preset** (~640x480 class); run the (blocking) start on a serial queue; on success `running`, else `unavailable` (and stop). Runtime-error / interruption notifications -> stop + `unavailable`.
- Device changes while visible (connect/disconnect): refresh list; if running/starting and the selected camera vanished: switch to the first remaining camera **without remembering it as the preference**, or if none stop and `noCamera`; if `noCamera` and a camera appears: start. Other states ignore.
- Manual camera pick: sets selection, remembers it as the user's preferred camera (Mac: system-wide per app `AVCaptureDevice.userPreferredCamera`; **Windows: store the device id in a new key `cameraPreviewDeviceId`**), swaps the input on the running session (no full restart); result sets `running`/`unavailable`.
- Retry (`Open camera` button in `unavailable`): re-runs `beginCapture`.
- Toggle (shortcut / second click): if presented (floating or embedded) hide, else show.

### 6.2.5 Settings (Quick tools page -> Camera preview section; header `Camera preview`)
Rows: Button (icon `web.camera`) `Open preview`; caption `Check how you look before a call`; Toggle `Global shortcut`; shortcut recorder (role cameraPreview); orange caption if registration failed; **if camera access is denied** a permission row: orange `exclamationmark.circle.fill`, text `Camera`, orange caption `Not granted`, small button `Open System Settings…`.
| Key | Type | Default | Notes |
|---|---|---|---|
| cameraPreviewShortcutEnabled | bool | false | |
| cameraPreviewShortcut | storage | `⌃⌥⌘W` (W for webcam) [WIN] suggest `Ctrl+Alt+Win+W` | |
| panelUtilityCameraPreview | bool | true | panel tile |
| notchCameraEnabled | bool | true | island module (Island spec); migration can turn it off |
| notchCameraFitWidth / notchCameraFitHeight | double | 0 / 0 | island sizing (Island spec) |
| [WIN new] cameraPreviewDeviceId | string | "" | remembered camera |
There are **no mirror/shape/size options on the Mac**: the mirror is always flipped and always the 320x240 rounded rectangle.

### 6.2.6 Windows mapping
- Capture: `Windows.Media.Capture.MediaCapture` + `MediaFrameSourceGroup.FindAllAsync()` (`MediaFrameSourceKind.Color`), `MediaCaptureInitializationSettings{ SharingMode=SharedReadOnly, StreamingCaptureMode=Video, MemoryPreference=Cpu }`, `MediaFrameReader` (Realtime) -> `SoftwareBitmap` (convert to `Bgra8`, premultiplied) -> `WriteableBitmap` bound to a WPF `Image` with `ScaleTransform(-1,1)` (mirror) and `Stretch=UniformToFill`. Pick the video format closest to 640x480 @ 30 fps (`MediaFrameSource.SupportedFormats`/`SetFormatAsync`). Drop frames while the previous one is still being converted (the existing code has a busy flag). Stop: `StopAsync`, dispose reader/capture on hide.
- Camera list: `DeviceInformation.FindAllAsync(DeviceClass.VideoCapture)`; live updates via `DeviceWatcher`.
- Permission: Windows has no per-app first-run prompt for desktop (Win32) apps; it is governed by Settings > Privacy & security > Camera > "Let desktop apps access your camera". Detect denial with `UnauthorizedAccessException` from `MediaCapture.InitializeAsync` or `DeviceAccessInformation.CreateFromDeviceClass(DeviceClass.VideoCapture).CurrentStatus` (`DeniedByUser`/`DeniedBySystem`); map to state `denied`; `waitingPermission` is effectively never shown on desktop (keep the state). Open settings with `Process.Start("ms-settings:privacy-webcam")`. There is no "denied -> prompt again" on either OS, so the denied block links straight to Settings.
- Dismissal: `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` for "another app activated" (ignore own process and the touch keyboard), `WH_MOUSE_LL` for outside clicks, `PreviewKeyDown` for Esc. Always on top: `Topmost=true`, `WS_EX_TOOLWINDOW`.
- Fade 0.13 s: window `Opacity` animation.
- Shortcut: global hotkey toggling.

### 6.2.7 Gap vs Trayloom (`CameraPreview\CameraPreviewFeature.cs`, 336 lines)
Has: `MediaCapture` + `MediaFrameReader` preview in a topmost `CameraWindow`, mirror toggle, size choice (Small 160/Medium 240/Large 360/XL 520), scroll-to-resize, drag, Esc closes, Notifier toasts for errors, hotkey `Ctrl+Alt+Shift+K`, circular shape.
Gaps / differences (prioritised):
1. Visual: Mac is a **320x240 rounded-rect (radius 14), black, white 14 % stroke** — Trayloom is a circle with user sizes and mirror option; remove the options (or keep behind an "advanced" flag) to match.
2. States UI: spinner / denied / unavailable / no-camera blocks with the exact messages and buttons (Trayloom uses toast notifications).
3. Camera picker overlay on hover with >1 camera; remembered camera (`cameraPreviewDeviceId`); device hot-plug handling and fallback rules.
4. Dismiss rules: click-outside and other-app-activation close the mirror; permission-prompt grace (1 s) logic.
5. Positioning rule (top-centre of pointer screen, 48 px below top) and 0.13 s fade; focus on open.
6. Settings section content (Open preview button, caption, global shortcut toggle + recorder, denied-permission row) and default shortcut `⌃⌥⌘W` mapping; panel tile caption + permission-required state; island Camera module integration.
7. Name/description strings: `Camera preview` / `Opens a floating mirror with your camera` (Trayloom description differs).

---

## 6.3 Cleaning Mode

### 6.3.1 Features-hub entry
| Item | Value |
|---|---|
| Hub title | `Cleaning Mode` (`cleaningMenuItem`) |
| Hub description | `Lock keyboard and screen for cleaning` (`descCleaningMode`) |
| Hub symbol | `bubbles.and.sparkles` -> Segoe `E9CE`/`F0B5`? none exact; use `EA37` (v) or `E7BE` sparkle substitute |
| Panel tile / overlay icon | `keyboard` -> Segoe `E765` |
| Panel tile caption | `Locks the keyboard so you can clean safely.` (`cleaningPanelCaption`); tile title `Cleaning Mode`; on Mac when Accessibility is missing: `Permission required: Accessibility` + `Grant access` button -> **not applicable on Windows** (no permission) |
| Permissions | Mac: Accessibility. Windows: none |
| Energy | Nothing at rest |
| Visibility key | `panelUtilityCleaning` (default true); Command Bar action `action.cleaningMode` (title `Cleaning Mode`) starts it after 0.1 s |

### 6.3.2 What it does (Mac behaviour, to replicate)
Temporarily **locks the keyboard** (all key events, including media/volume/brightness/power keys, scroll wheel and trackpad gestures are swallowed) so the user can wipe the device; **the pointer still moves and mouse buttons still click** (the Unlock button must be clickable). A full-screen overlay on every display makes the state obvious, shows live progress toward the unlock gesture and an Unlock button. Two guaranteed ways out: press **Escape 5 times** in a row, or click **Unlock**.
Safety: if the lock cannot be installed (permission missing on Mac) it never activates, so nobody is stranded.

### 6.3.3 Strings (global `Localization` struct, enUS)
| Key | Text |
|---|---|
| cleaningMenuItem | Cleaning Mode |
| cleaningPanelCaption | Locks the keyboard so you can clean safely. |
| cleaningOverlayTitle | Keyboard locked for cleaning |
| cleaningOverlaySubtitle | Press Escape 5 times to unlock |
| cleaningOverlayUnlock | Unlock |
| cleaningOverlayMouseHint | Your mouse and trackpad still work |
| cleaningKeepScreenVisibleToggle | Keep screen visible |
| cleaningKeepScreenVisibleCaption | Shows a discreet indicator in the corner of the screen instead of blacking out content. |
| cleaningStartNow | Lock keyboard now |
| cleaningNeedsAxTitle | Accessibility needed (Mac only; drop) |
| cleaningNeedsAxBody | To lock the keyboard safely, Vorssaint needs Accessibility permission. Grant it in System Settings and try again. (Mac only; drop) |
Hub: `Lock keyboard and screen for cleaning`.

### 6.3.4 Layout — full blackout overlay (`keepScreenVisible = false`, default)
One borderless panel per display, covering the display's full frame, above the menu bar/taskbar and full-screen apps (shielding level), on all desktops, no shadow. Background **solid black**, content centred in a column max width 460, padding 44, VStack spacing 18:
1. Icon `keyboard`, 48 pt regular, white.
2. Title `Keyboard locked for cleaning`, 23 pt semibold, white.
3. Subtitle `Press Escape 5 times to unlock`, 15 pt, white @ 75 %, centred.
4. **Progress dots**: 5 circles 12 px, spacing 11, top padding 2; filled white when `index < progress` else white @ 22 %; animation easeOut 0.15 s on progress change.
5. **Unlock button**: capsule, white fill, black label `Unlock` 14 pt semibold, min width 130, padding 10 vertical / 18 horizontal, top padding 6; click -> `deactivate()`. Fires on the first click (no activating click needed).
6. Hint `Your mouse and trackpad still work`, 12 pt, white @ 50 %.

### 6.3.5 Layout — compact HUD (`keepScreenVisible = true`)
Panel background clear: the screen stays visible and clicks fall through to apps. A HUD card at the **top-right** of each display (top padding 40, trailing padding 24): HStack spacing 12, padding 14 h / 10 v, `HUDBackdrop` material, radius 16 continuous, 1 px stroke primary @ 12 %, shadow black @ 22 %, radius 16, y 6. Content: circle 28x28 (fill primary @ 8 %) with `keyboard` 16 pt medium secondary; VStack(spacing 3): title `Keyboard locked for cleaning` 13 pt semibold primary; HStack(spacing 8): subtitle 11 pt secondary + compact dots (5 circles 6 px, spacing 4, filled accent else primary @ 20 %); trailing small prominent button `Unlock` (11 pt semibold).

### 6.3.6 Settings (Quick tools page -> Cleaning Mode section; header `Cleaning Mode`)
1. Button (icon `bubbles.and.sparkles`) `Lock keyboard now` -> `CleaningModeManager.activate()`.
2. Caption: `Locks the keyboard so you can clean safely.`
3. Toggle `Keep screen visible` (`cleaningModeKeepScreenVisible`, bool, default **false**).
4. Caption: `Shows a discreet indicator in the corner of the screen instead of blacking out content.`
No shortcut option exists on the Mac for this tool (Trayloom's optional hotkey/auto-timeout are extras). The overlay reads the toggle live.

### 6.3.7 Behaviour / algorithms
- **Unlock counter** (`CleaningUnlockCounter`): required key = Escape, threshold **5**, pressWindow **6.0 s**. `registerKeyDown(code, time, isRepeat)`: auto-repeat never counts or resets (returns false); any key other than Escape sets progress=0 and forgets last time; Escape within 6 s of the previous Escape -> progress+1, otherwise progress=1; returns true when progress >= 5. Modifier key presses AND releases (Shift, Ctrl, Alt, Win, Fn, Caps Lock) and media/system keys (power, volume, brightness...: synthetic code 10000 + keyCode) count as "other key" and reset. Rationale (documented in source): the 6 s window forgives slow deliberate presses; the reset rule rejects a cloth striking Esc five isolated times. Mac key code Escape=53 -> Windows `VK_ESCAPE` 0x1B.
- Progress is published (`unlockProgress`, 0...5) for the dots.
- **Mouse release gate** (`CleaningMouseReleaseGate`): tracks buttons pressed while locked. A *user-requested* unlock (Esc x5 or Unlock click) waits until every tracked press has received its real release before tearing the overlay down, so a click is never cut in half; nothing is synthesised. `buttonDown(b)` inserts; `buttonUp(b)` returns true when this release completes a pending deactivation; `requestDeactivation()` sets pending and returns whether it can proceed now (no pressed buttons); `invalidateTrackedPresses()` (hook was blind) clears pressed set but keeps the request; `releaseWaitExpired()` after **5 s** (`releaseWaitLimit`) clears pressed set and proceeds (a mouse that disconnects mid-press must not keep the lock on). Teardown itself is dispatched to the next UI turn (never from inside the hook callback). Forced teardown (session switch, permission loss, hook failure) bypasses the gate.
- **Activate**: no-op when already active; (Mac: Accessibility check -> alert) ; install the lock hook (abort if it cannot be installed); **suspend** features whose own input hooks could interfere or fire from stray contacts: Keyboard Debounce, Mouse Click Debounce, Middle Click, Mouse Navigation (side buttons), Mouse Button Shortcuts, Radial Menu; reset unlock counter and progress; `isActive=true`; install display-change observer; show overlays (one per display; panels for unchanged frames are reused so a burst of display notifications never flashes or drops a click).
- **Deactivate**: on first request arm the 5 s release deadline; gate decides; then remove hook, observer and overlays, reset counter/progress, `isActive=false`, and call `syncWithPreferences()` on each suspended feature (each re-reads its own availability/prefs/permission, so changes made meanwhile stick). Teardown due to session switch/lock does **not** restore features until the session becomes active again (`shouldRestoreSuspendedFeaturesOnSessionReturn`).
- **Hook failure / timeout** (Mac `tapDisabledByTimeout/UserInput`): if the session is active and permission still granted re-enable the tap and invalidate tracked presses (and finish a pending user deactivation); otherwise tear down immediately with fail-open (events pass).
- `toggle()` = active ? deactivate : activate (used by Command Bar / panel).
- Panel start: closes the popover first so any alert is not stranded, then activates.

### 6.3.8 Windows mapping
- **Keyboard lock**: `SetWindowsHookEx(WH_KEYBOARD_LL)` on a dedicated message-loop thread (or the shared `Ctx.Hooks.SubscribeKeyboard(handler, priority: int.MaxValue)` — highest priority so Keyboard Debounce etc. cannot eat repeated Esc presses). Callback returns non-zero for **every** event (`WM_KEYDOWN/KEYUP/SYSKEYDOWN/SYSKEYUP`), after feeding key-downs (incl. modifiers and `VK_VOLUME_*`, `VK_MEDIA_*`, `VK_BROWSER_*`, `VK_LAUNCH_*`, `VK_SLEEP`) to the unlock counter. Auto-repeat detection: keep a `bool[256]` of keys currently down; a down for an already-down vk is a repeat. Callback must stay tiny (< ~200 ms; default `LowLevelHooksTimeout` is 300 ms; exceeding it makes Windows silently drop the hook). Run a watchdog that re-installs the hook periodically (e.g. every 5 s via `UnhookWindowsHookEx` + `SetWindowsHookEx`) and on `WM_WTSSESSION_CHANGE`.
- **`BlockInput` is not used**: it needs administrator rights, blocks the mouse too (the Unlock button must stay clickable), and is cancelled by Ctrl+Alt+Del.
- **Mouse**: `WH_MOUSE_LL` is installed only to (a) track button down/up for the release gate and (b) **swallow `WM_MOUSEWHEEL` / `WM_MOUSEHWHEEL`** (scroll). Moves and button events pass through. (The existing Trayloom code swallows the mouse completely: change.)
- **Cannot be blocked on Windows**: `Ctrl+Alt+Del` (Secure Attention Sequence), `Win+L`, hardware/firmware keys that never reach user mode (Fn, most laptop brightness/airplane/backlight keys, power button), and Precision-touchpad multi-finger gestures (3/4-finger swipes, handled by the shell input stack before hooks). State these in the Settings caption/README; the blackout overlay hides what such gestures reveal. Elevation caveat: a non-elevated low-level hook does not see keystrokes destined for an **elevated** foreground window (UIPI); the overlay must take the foreground (`SetForegroundWindow` + `AttachThreadInput` workaround / `AllowSetForegroundWindow`) so keys go to our window; running elevated avoids the edge case.
- **Overlays**: per monitor (`Screen.AllScreens`/`EnumDisplayMonitors`, full `rcMonitor`), `Window` with `WindowStyle=None`, `ResizeMode=NoResize`, `Topmost`, `ShowInTaskbar=false`, `WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE`; re-assert `HWND_TOPMOST` every ~500 ms to stay above the taskbar and full-screen apps (also `SetWindowPos(... SWP_NOACTIVATE)`). Rebuild/reuse on `SystemEvents.DisplaySettingsChanged` / `WM_DISPLAYCHANGE` (reuse windows whose bounds are unchanged). Compact mode: use a **separate small topmost HUD window per monitor** at top-right (Windows has no menu bar: keep the 40 px/24 px offsets) and no full-screen window at all, so clicks reach apps; the HUD must be activatable-by-first-click (`WS_EX_NOACTIVATE`, handle `WM_MOUSEACTIVATE` -> `MA_NOACTIVATE`).
- Session: subscribe `WTSRegisterSessionNotification`; on `WTS_SESSION_LOCK`/`WTS_CONSOLE_DISCONNECT` -> deactivate without restoring suspended features; on `WTS_SESSION_UNLOCK` restore them.
- Elevation: none required.
- The "Accessibility needed" alert/permission row does not exist on Windows.

### 6.3.9 Gap vs Trayloom (`CleaningMode\CleaningModeFeature.cs`, 296 lines)
Has: black multi-monitor overlays, low-level keyboard and mouse hooks (mouse fully swallowed), hold-Esc-2-s to leave (progress text), optional auto-exit timer (`maxMinutes`: Never/2/5/10/30), optional hotkey, Ctrl+Alt+Del note; description `Turn the screen black and lock the keyboard and mouse so you can clean them; hold Esc to leave.`
Prioritised gaps:
1. **Unlock gesture**: replace hold-Esc by 5 presses of Esc (6 s window, any other key resets, repeats ignored) with 5 progress dots; add clickable **Unlock** button (needs the mouse to stay live) and the release-gate logic.
2. **Mouse stays usable** (moves + clicks); only the wheel is swallowed. Remove "mouse locked".
3. Overlay content/styling (keyboard icon 48, title 23 semibold, subtitle, dots, white capsule Unlock button, mouse hint) and strings; **Keep screen visible** compact HUD mode + its Settings toggle (`cleaningModeKeepScreenVisible`).
4. Settings page: `Lock keyboard now` button + caption + toggle + caption (drop `maxMinutes`, hotkey rows or keep as Trayloom extras but not at Mac parity); Hub name `Cleaning Mode` / description `Lock keyboard and screen for cleaning`.
5. Suspend/resume of Keyboard Debounce, Mouse Click Debounce, Middle Click, Mouse Navigation, Mouse Button Shortcuts, Radial Menu around the lock.
6. Display-change handling (overlay reuse), session lock handling, hook watchdog.

---

## 6.4 Kill Process (Beta)

### 6.4.1 Features-hub entry
| Item | Value |
|---|---|
| Hub title | `Kill Process` (`pageTitle`) + blue capsule badge `BETA` (`betaBadge`, 8 pt bold white on accent) — a11y adds `Beta. You may run into some bugs.` |
| Hub description | `Search running processes and force quit, restart, or kill process trees` |
| Symbol | `xmark.octagon` -> Segoe `E711` / `EA39` (v) / `F140` (StatusCircleBlock, v) |
| Permissions | none |
| Installed by default | **no** (opt-in) |
| Energy | Nothing at rest (it only refreshes while its Settings page is the active window) |
| Where it appears | its own Settings page `Kill Process` (sidebar, Tools group; search keywords: `force quit`, `process`, `cpu`, `memory`, `kill`), the Command Bar (`Kill Process` browser row + per-process rows) and Port Manager (terminate actions require Kill Process installed). No panel tile, no shortcut. |

### 6.4.2 Strings (enUS)
| Key | Text |
|---|---|
| pageTitle | Kill Process |
| browseSubtitle | Browse & Kill |
| hubDescription | Search running processes and force quit, restart, or kill process trees |
| searchPlaceholder | Filter by name |
| columnProcess | Process |
| columnCPU | CPU |
| columnMemory | Memory |
| columnPID | PID |
| groupToggle | Group related processes |
| groupCaption | Groups helper processes under the app responsible for them. |
| commandBarToggle | Show in Command Bar |
| commandBarCaption | Adds running processes to the Command Bar, so you can find and kill them without opening Settings. |
| refreshTooltip | Refresh |
| pidLabelFormat | PID %d |
| processCountFormat | Processes: %d |
| killButton | Kill |
| forceKillButton | Force Kill |
| killAllFormat | Kill All “%@” |
| killTreeButton | Kill Process Tree |
| restartButton | Restart |
| copyPID | Copy PID |
| copyPath | Copy Path |
| emptyStateTitle | No Processes Found |
| confirmKillFormat | Kill %@? |
| confirmForceKillFormat | Force Kill %@? |
| confirmKillAllFormat | Kill all “%@” processes? |
| confirmKillTreeFormat | Kill %@ and all its child processes? |
| killFailedTitle | Couldn’t Kill Process |
| killFailedMessage | The process may have already exited or require additional privileges. |
| adminPromptFormat | Vorssaint needs administrator access to end “%@”. ([WIN] Trayloom) |
Also reused: `Cancel` (`uninstallerCancel`), `Open in browser` etc. n/a here. (`killFailedTitle/Message` exist in the strings struct; the Mac UI does not currently surface them — implement them on Windows as the error dialog when a kill fails after elevation.)

### 6.4.3 Layout (Settings page "Kill Process")
Whole page is a VStack(spacing 0): header block, divider, column header, divider, list. Frame fills the page.
1. **Header** (VStack spacing 12, padding 16):
   - A grouped `Form` (non-scrolling, `fixedSize` vertically) with two sections: (a) Toggle `Group related processes` (`killProcessGroupRelated`) + caption `Groups helper processes under the app responsible for them.`; (b) Toggle `Show in Command Bar` (`killProcessCommandBarEnabled`) + caption `Adds running processes to the Command Bar, so you can find and kill them without opening Settings.`
   - Right-aligned HStack (spacing 16): caption secondary `Processes: <count of filtered rows>` and a plain refresh button (`arrow.clockwise`, secondary, tooltip `Refresh`; Segoe `E72C`) which force-refreshes.
   - Search row (HStack spacing 10): `magnifyingglass` secondary (Segoe `E721`) + plain text field placeholder `Filter by name` bound to `query`; small spinner while refreshing.
2. Divider.
3. **Column header** (HStack spacing 10, horizontal padding 16, vertical 6): [22 pt spacer for the row icon], `Process` (flex, left), `CPU` (width 52, right), `Memory` (72, right), `PID` (56, right), [98 pt spacer for the Kill + "…" cluster]. Each label is a plain button, 10.5 pt semibold secondary; the active sort column shows a 8 pt bold chevron (`chevron.up` ascending / `chevron.down` descending; Segoe `E70E`/`E70D`).
4. Divider.
5. **List** (inset style, row separators as default): 
   - Empty states: while the first snapshot has not completed -> centred small spinner; after first load and no rows (or filter matched nothing) -> centred secondary text `No Processes Found`.
   - **Row** (HStack spacing 10, vertical padding 2, buttons bordered small): 22x22 app/process icon (executable icon; Windows: `SHGetFileInfo`/`ExtractIconEx` of the image path, generic exe icon fallback); name block (VStack spacing 1): line 1 = name 12.5 pt (1 line, middle truncation) and, when grouped count > 1, ` Processes: N` in 10 pt tertiary (reuses `processCountFormat`); line 2 = full path 10.5 pt tertiary, 1 line, **head** truncation; flexible spacer; CPU `"%.1f%%"` mono 11 pt secondary width 52 right; Memory `MetricFormat.bytes` mono 11 pt secondary width 72 right; PID mono 11 pt secondary width 56 right; button `Kill` (min width 44, disabled if protected); "…" menu button (min width 44).
   - "…" menu items: `Force Kill` (disabled if protected), `Kill All “<name>”` (disabled if protected), `Kill Process Tree` (disabled if protected), `Restart` (only if restartable), divider, `Copy PID`, `Copy Path`.
   - Byte format (`MetricFormat.bytes`): base 1024, units B/KB/MB/GB/TB/PB, one decimal when value < 10 else no decimals; B shows integer; value that rounds to >= 1024 is promoted to the next unit. Locale-aware decimal separator.
6. **Confirmation alert** (modal, for Kill, Force Kill, Kill All, Kill Tree): title/message below, buttons `Cancel` and the destructive confirm. 
   | Action | Title | Message | Confirm button |
   |---|---|---|---|
   | Kill | `Kill <name>?` | full path | `Kill` |
   | Force Kill | `Force Kill <name>?` | full path | `Force Kill` |
   | Kill All | `Kill all “<name>” processes?` | (empty) | `Kill` |
   | Kill Tree | `Kill <name> and all its child processes?` | full path | `Kill Process Tree` |
   Restart, Copy PID, Copy Path act immediately (no confirmation). Command Bar uses `critical`-style alerts with the same strings (Force Kill, Kill All, Kill Tree); its plain Kill uses `confirmKillFormat` as inline confirmation prompt.

### 6.4.4 Settings keys
| Key | Type | Default | Notes |
|---|---|---|---|
| killProcessGroupRelated | bool | true | group helpers under owning app; toggling forces a refresh |
| killProcessCommandBarEnabled | bool | true | Command Bar browser row + `kill.<pid>` rows |
| killProcessSortBy | string | `cpu` | `cpu`, `memory`, `name`, `pid` |
| killProcessSortAscending | bool | false | |
Availability key `featureAvailable.killProcess` default false.

### 6.4.5 Behaviour and algorithms
- **Entry model** (`KillProcessEntry`): pid, ppid, name (display name of the responsible app/process), path (command path), cpuPercent, memoryBytes (RSS), isRegularApp (has a normal GUI app presence), bundleURL (relaunch target), groupedCount (1, or members when grouped), isProtected, startedAt (kernel start time in microseconds; identity). **A PID alone is reusable, so every destructive action re-verifies `(pid, startedAt)` immediately before signalling.**
- **Snapshot** (Mac: `ps -eo pid,ppid,pcpu,rss,comm`): rows parsed; pid > 0; short name = last path component of the command; `startedAt` also checked against expected parent and path (identity must be readable); a row whose identity cannot be read is shown but **protected** (actions disabled). If grouping: members are folded under their owner process (`ResponsibleProcess.owner(of:)`): CPU and memory summed, groupedCount = members, owner row must exist else the group is dropped; initial sort by cpu descending. A failed/empty snapshot (non-zero, timeout, zero rows) **never wipes** an already-shown list (`hasLoadedOnce` distinguishes first load from empty result).
- **Refresh policy**: service-level cache fresh for **3 s** (`refresh()` is a no-op inside the window; `force` bypasses). The page refreshes on appear (forced) and via a **3 s repeating timer that runs only while the Settings window is key/active and this page is shown** (stops when the window goes inactive or the page disappears; restarts with an immediate refresh). Refresh runs off the UI thread and publishes on the UI thread; a spinner shows while running.
- **Sorting/filter**: `sortedEntries` by chosen column with PID as tiebreaker (numbers: equal -> lower pid first; direction applies to the value only); name sort is locale-aware case-insensitive. Clicking the active header flips direction; clicking another switches to it with default direction (**ascending for Name, descending for CPU/Memory/PID**); both persisted. Filter = trimmed lowercase query contained in the lowercase name, OR exact PID string match.
- **Kill** (graceful = Mac `terminate()`/SIGTERM; Force = `forceTerminate()`/SIGKILL): background work; for each target verify identity; regular apps use the app-terminate API, others signals. Direct failure with permission error (EPERM) collects the target for a **single batched admin escalation** (one password prompt for all, running `/bin/kill -15|-9 pid` only if the process start description still equals the one captured). Result classes: killed, alreadyGone, needsAdmin, failed, stale (identity mismatch -> silently skipped). On completion removed rows disappear immediately from the list and a forced reconcile refresh runs **0.5 s** later.
- **Kill All** (by exact name among current non-protected rows), **Kill Tree**: root verified; descendants computed from one `ps -eo pid,ppid` table by BFS (`KillProcessSupport.descendants`: each pid visited once so cycles/self-parent cannot loop; cap **4096**; breadth-first, then **reversed so deepest die first**, root last); protected descendants skipped; each descendant's identity captured at plan time and re-verified at kill time. Grandchildren forked after the snapshot are missed (documented).
- **Restart**: requires not protected, a bundle/relaunch target and identity; cancels any pending restart; registers a termination observer for that pid, kills **gracefully**, **waits for the real termination notification (no fixed delay)**, then relaunches the app and force-refreshes; if the graceful kill returns failed/needsAdmin/stale the restart is cancelled; a 10 s timeout also cancels.
- **Protected set** (`isProtected`): pid <= 1, the app's own pid, names `kernel_task`, `launchd`, `windowserver`, `loginwindow`, app's own names (also dev build names), or paths ending `/windowserver`, `/loginwindow`, `/launchd` -> Kill buttons/menu entries disabled (Restart not offered; Copy PID/Path still enabled); Command Bar omits protected rows; Port Manager disables terminate. **Windows equivalent set**: PID 0 (System Idle), 4 (System), self, plus (case-insensitive, without `.exe`) `smss`, `csrss`, `wininit`, `winlogon`, `services`, `lsass`, `lsaiso`, `registry`, `memory compression`, `secure system`, `fontdrvhost`? (no: user-killable), `dwm` (equivalent of WindowServer), `system`, `idle`, `winlogon`; plus the Trayloom process. (`explorer` is NOT protected: ending it just restarts the shell.)
- Command Bar integration: browser row id `killProcessBrowserRowID`, title `Kill Process`, subtitle `Browse & Kill`, icon `xmark.octagon`, keeps the bar open, switches category to killProcess and clears the query; rows `kill.<pid>`: title = process name, subtitle `PID %d`, keywords = path, icon = app icon (else `xmark.octagon`), confirmation prompt `Kill %@?`, not counted in usage; row Actions panel: `Force Kill` (symbol `exclamationmark.octagon`, destructive), `Kill All “%@”`, `Kill Process Tree`, `Restart` (symbol `arrow.clockwise`, only if restartable). Rows load lazily from `KillProcessService`'s cache (completion-sequenced refresh).

### 6.4.6 Windows mapping
- **Enumeration**: `NtQuerySystemInformation(SystemProcessInformation)` (one call: pid, ppid, working set, kernel/user times, creation time) or `CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS)` + `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)`. Path: `QueryFullProcessImageNameW`. Memory: `WorkingSetSize` (or private working set `PROCESS_MEMORY_COUNTERS_EX2.PrivateWorkingSetSize`; pick WorkingSet to be comparable with `rss`). **CPU %**: sample `GetProcessTimes` (kernel+user, 100 ns) each refresh; `delta / wallClockDelta` as a percentage of ONE core (can exceed 100 %, same semantics as `ps pcpu` on a multi-core Mac) — flag: Task Manager divides by logical cores; state this choice in the settings caption only if product owner asks. First sample shows 0.0 %.
- **Identity** (`startedAt`): process creation FILETIME (100 ns ticks; convert to microseconds). Verify `(pid, creationTime)` before every kill; for descendants also require `child.creationTime >= parent.creationTime` (Windows does not clear ppid when the parent exits, so a recycled pid can fake a parent).
- **Display name** (`ResponsibleProcess.displayName`): `FileVersionInfo.FileDescription` of the image if non-empty, else process name without `.exe`; **icon** from the exe.
- **Grouping** ("responsible app"): owner(pid) = walk up `ppid` while the parent exists, has a creation time earlier than the child, is not one of {`explorer`, `services`, `wininit`, `System`, `svchost`, `winlogon`, Trayloom} and shares the same install root (parent directory chain) or the same image name as the child; the topmost such ancestor is the owner (Chrome/Edge/Electron helpers fold under their main process). Owner row must exist else group dropped (as Mac). Sum CPU and memory.
- **isRegularApp**: process owns a visible top-level window (`EnumWindows` + `GetWindowThreadProcessId` + `IsWindowVisible` + not tool window) or is a packaged app.
- **Graceful Kill**: for processes with top-level windows post `WM_CLOSE` to all of them (`EnumWindows`), then if still alive after the 0.5 s reconcile nothing more (Mac `terminate()` is also a request); for processes without windows (console/service/background) there is no graceful signal: attempt `GenerateConsoleCtrlEvent(CTRL_C_EVENT)` only for console children we own, otherwise fall back to `TerminateProcess(handle, 1)`. **Force Kill**: `TerminateProcess` via `Process.Kill()`. Kill tree: `Process.Kill(entireProcessTree: true)` is racy; implement the BFS with `ppid`+creation-time validation then `TerminateProcess` deepest first (same 4096 cap).
- **Elevation** (Mac: admin shell prompt once for all `EPERM` targets): on `ERROR_ACCESS_DENIED (5)` collect the targets; show Trayloom's own confirmation `Trayloom needs administrator access to end “<name>”.` ([WIN] of `adminPromptFormat`), then launch **one elevated helper** (`Trayloom.exe --kill-batch <pid:creationTicks:force,...>` with `ShellExecute verb "runas"` -> UAC) that re-verifies identities and terminates; one UAC prompt for the whole batch. If the user declines UAC or the process is protected/PPL (e.g. antivirus) show alert `Couldn’t Kill Process` / `The process may have already exited or require additional privileges.`. Processes running as SYSTEM or protected processes (PPL) cannot be ended even by an elevated admin: same alert.
- **Restart**: capture before the kill: image path, command line (`NtQueryInformationProcess(ProcessBasicInformation)` -> PEB -> `RTL_USER_PROCESS_PARAMETERS.CommandLine`, or WMI `Win32_Process.CommandLine`), working directory; graceful close (`WM_CLOSE`); wait with `Process.WaitForExit`/`Exited` event up to **10 s** (no fixed sleep); relaunch via `Process.Start` (`UseShellExecute=true`; for packaged apps `explorer.exe shell:AppsFolder\<AUMID>`); then forced refresh. Offered only for non-protected processes with a readable path and `isRegularApp`.
- **Copy PID / Copy Path**: clipboard text.
- **Timer**: `DispatcherTimer` 3 s, started/stopped on `Window.Activated/Deactivated` + page visibility.
- **Beta badge** in hub: standard.
- Command Bar integration as above.

### 6.4.7 Gap vs Trayloom (`KillProcess\KillProcessFeature.cs`, 525 lines)
Has: `ProcessKiller` (critical-name refusal, `Process.Kill(tree)`, access-denied message, restart without args), a standalone `KillProcessWindow` with `DataGrid` (Name/PID/CPU %/Memory/Path), search box, 2 s timer, buttons `End process`, `End process tree`, `Restart`, `Open file location`, Delete key = end, context menu, hotkey option.
Prioritised gaps:
1. Move from a standalone window to a **Settings page** with the Mac layout (settings form at top, sortable Process/CPU/Memory/PID headers with chevrons, row layout with icon, two-line name/path, count suffix, Kill + "…" menu).
2. Strings/labels: `Kill`, `Force Kill`, `Kill All “x”`, `Kill Process Tree`, `Restart`, `Copy PID`, `Copy Path`, confirmation alerts (Trayloom kills without confirming), `Processes: N`, `No Processes Found`.
3. **Group related processes** (+ persisted toggle), **Kill All by name**, graceful vs force distinction (currently only `Kill`), identity-verified kills (pid + creation time) and tree algorithm (4096 cap, deepest first).
4. **Admin elevation helper** (batched UAC) instead of a "run as administrator" message; `Couldn’t Kill Process` dialog.
5. Command Bar integration (`Show in Command Bar` toggle, browser row, per-process rows, Actions) and Settings keys (sortBy/ascending/group/commandBar); refresh policy (3 s cache, only while page active, preserve list on failed snapshot); persisted sort.
6. Protected-set parity (add `dwm`, own process, unreadable-identity rows disabled) and CPU/memory formatting (`%.1f%%`, `MetricFormat.bytes`).
7. Hub: Beta badge, opt-in default, description text; sidebar keywords.

---

## 6.5 Port Manager

### 6.5.1 Features-hub entry
| Item | Value |
|---|---|
| Hub title | `Port Manager` (`title`) |
| Hub description | `View active listening ports and, with Kill Process installed, terminate the processes using them` |
| Symbol | `network` -> Segoe `E968` (NetworkTower) / `E839` (Ethernet) (v) |
| Permissions | none |
| Installed by default | **no** (opt-in). Terminate actions appear only when **Kill Process is installed** (`AppFeature.killProcess.isAvailable`) |
| Energy | Nothing at rest |
| Surfaces | (a) Settings page `Port Manager` (sidebar Tools, keywords `port`, `ports`, `listening`, `socket`, `PID`, `kill port`); (b) Menu-panel Utilities tile: title `Port Manager`, caption `Your listening ports` (`listeningCaption`), symbol `network`, opens an inline panel view (visibility key `panelUtilityPortManager`, default true). No global shortcut, no own settings keys. |

### 6.5.2 Strings (enUS)
| Key | Text |
|---|---|
| title | Port Manager |
| filter | Filter by port, process, or PID |
| openFormat | %d open |
| empty | No listening ports found |
| emptyHint | Try refreshing or changing your search. |
| listeningCaption | Your listening ports |
| kill | Kill |
| forceKill | Force Kill |
| loadFailed | Could not read listening ports. Try refreshing. |
| refresh | Refresh |
| copyPort | Copy port |
| copyAddress | Copy address:port |
| terminateFormat | Terminate %@? |
| terminateMessageFormat | This closes port %d by terminating PID %d. |
| hubDescription | View active listening ports and, with Kill Process installed, terminate the processes using them |
| allInterfaces | All interfaces |
| allInterfacesHelp | Listening on every network interface, so other devices on the network may be able to connect. |
Reused: `Copy PID` (`killProcess.copyPID`), `Open in browser` (`CommandBar.openInBrowser`), `Cancel`, `Settings` (`menuSettings`, tooltip of the panel's gear).

### 6.5.3 Layout — Settings page
VStack(spacing 0), top-leading:
1. Title row (padding: h 14, top 10, bottom 8): `network` icon 15 pt semibold accent + `Port Manager` (headline) + spacer + refresh button (plain, `arrow.clockwise` 11 pt semibold, tooltip `Refresh`).
2. Search box (HStack spacing 8, padding 9, fill primary @ 6 %, radius 9; outer h 14, bottom 8): `magnifyingglass` secondary + text field `Filter by port, process, or PID` (plain) + small spinner while refreshing.
3. Divider.
4. If the last refresh failed: `Label` (`exclamationmark.triangle`) `Could not read listening ports. Try refreshing.` callout secondary, wraps, padding 14.
5. Content: failed -> empty spacer; else if no rows after a successful load -> **empty state** (VStack spacing 8: `network.slash` 25 pt tertiary, `No listening ports found` callout secondary, `Try refreshing or changing your search.` caption tertiary) centred; if first load not done -> centred spinner; else a list (inset, hidden row separators) of rows.
6. **Row** (HStack spacing 8, vertical padding 3, full-width content shape): `network` icon 11 pt semibold accent, width 18; VStack(spacing 1): line 1 (HStack spacing 6) = port number (mono 12 pt semibold) + protocol capsule (`TCP`, 9 pt bold rounded secondary, padding 4/1.5, fill primary @ 8 %) + **All-interfaces badge** (if the bind address is a wildcard: globe icon + `All interfaces`, 9 pt bold rounded, orange text on orange @ 14 % capsule, padding 4/1.5, spacing 2.5; tooltip/a11y hint = `allInterfacesHelp`); line 2 = process name 11 pt, 1 line; line 3 = `PID <pid>  •  <address>` mono 9 pt tertiary, 1 line, middle truncation; spacer; (if Kill Process installed) HStack spacing 4: bordered mini button `Kill` and bordered mini icon button `bolt.fill` (a11y label `Force Kill`; Segoe `E945` (v)), both **disabled** when `startedAt == nil` (identity unstable) or the process is protected.
   Context menu (right-click): `Copy port`; `Copy PID`; `Copy address:port` (copies the address string as shown, e.g. `*:3000`); if an openable URL exists: divider + `Open in browser` (opens `http://host:port`; beeps if opening fails).
7. Confirmation alert: title `Terminate <processName>?`, message `This closes port <port> by terminating PID <pid>.`, buttons `Cancel` and `Kill` / `Force Kill` (destructive, chosen by which button opened it).
The page calls `refresh()` on appear. **No timer**: the list updates on appear, Refresh and after a terminate (completion refresh).

### 6.5.4 Layout — Menu-panel inline view (`PanelPortManagerView`)
Shown inside the panel when the tile is clicked; keeps the popover open while visible; VStack(leading, spacing 10):
- Header (HStack spacing 8): `Label("Port Manager", network)` 12 pt semibold; spacer; gear button 22x22 (`gearshape` 12 pt secondary, tooltip/label `Settings`; opens the Settings window on the Port Manager page); close button 22x22 (`xmark.circle.fill` 14 pt secondary, tooltip `Cancel`) returning to the tile list.
- Controls card (panel card): HStack = caption `Your listening ports` 10 pt secondary, spacer, `<n> open` (9.5 pt semibold rounded secondary; n = filtered row count); below HStack spacing 6: rounded-border text field `Filter by port, process, or PID` 11 pt + bordered mini refresh button (24x22, tooltip `Refresh`).
- Failure label in a panel card (10.5 pt secondary); empty state (72 px high card: `network.slash` 16 pt tertiary, `No listening ports found` 10.5 pt medium secondary, hint 9.5 pt tertiary) or loading card (72 px, spinner).
- List: scroll view max height **260**, lazy rows spacing 6; row as above with smaller fonts (port 11.5, protocol 8.5, name 10.5, PID line 8.5 mono), badge shows only the globe (no label; label stays in tooltip/a11y), row background primary @ 3 %, radius 6, padding 6/4, same context menu.
- Terminate inside the Dynamic Island uses a floating confirm (`confirmAboveIsland`) with the same texts instead of a sheet.

### 6.5.5 Data / algorithms
- **Entry**: port (Int), protocolName (`TCP`), address (string: `*:3000`, `127.0.0.1:3000`, `[::1]:3000`, `192.168.1.5:3000`), pid, processName, startedAt (identity or nil). `id = "<proto>-<port>-<pid>-<address>"`.
- **Snapshot** (Mac: `lsof -nP +c0 -iTCP -sTCP:LISTEN -F pcnPT` + a process identity table): only **TCP LISTEN** sockets. Parse by field: `p` pid (resets port/address), `c` command name, `P` protocol, `n` name ("addr:port"; the port is the integer after the last `:`; rows need pid > 0 and port > 0); dedupe key `proto|port|address|pid`; sort by port ascending then process name ascending. IPv4 and IPv6 wildcard binds both print as `*`, so they dedupe into one row. A negative shell status (own timeout) = failure -> `refreshFailed=true` and list left as it was; lsof exiting 1 with no output = empty list (not a failure). Identity: `startedAt` is attached only if the same start time is observed both in the pre-listing table and again at row-building time; otherwise nil (row visible, terminate disabled until a later refresh).
- **Filter**: lowercase trimmed query contained in lowercase `"<port> <processName> <pid> <address>"`.
- **`listensOnAllInterfaces(endpoint)`**: host = text before the last `:`; strip surrounding `[]`; true for `*`, `0.0.0.0`, `::`.
- **`browserURL(entry)`**: only TCP with port 1...65535 and an address containing `:`; host mapping `*` -> `localhost`, `0.0.0.0` -> `127.0.0.1`, `[::]`/`::` -> `[::1]`, anything else unchanged; result `http://<host>:<port>`.
- **Terminate**: requires Kill Process installed and a non-nil `startedAt`; calls the Kill Process service's identity-verified kill (graceful or force), then re-refreshes the list when done.
- Copy actions write plain text to the clipboard (asynchronously, beep on failure).

### 6.5.6 Windows mapping
- **Listing**: `GetExtendedTcpTable(buffer, ref size, true, AF_INET /*2*/ and AF_INET6 /*23*/, TCP_TABLE_OWNER_PID_LISTENER /*3*/, 0)` (use `MIB_TCPROW_OWNER_PID` 24-byte / `MIB_TCP6ROW_OWNER_PID` 56-byte rows; retry on `ERROR_INSUFFICIENT_BUFFER`). Ports are network byte order (`ntohs`). Address text: IPv4 `a.b.c.d`; IPv6 `[addr%scope]` formatted with brackets; `0.0.0.0` and `::` displayed as `*`. Process name: `Process.GetProcessById(pid).ProcessName` (no `.exe`), fallbacks `System` (pid 4), `System Idle` (0), `(unknown)`. Identity: process creation time via `GetProcessTimes` (same helper as Kill Process); to emulate the "observed twice" rule read it before and after the table call.
- **UDP/other states are not part of the Mac feature** (`Show all TCP connections`, UDP rows in Trayloom are extras; hide them or remove).
- **Termination**: via Kill Process service (6.4): `TerminateProcess`; PID 4 (`System`, owns HTTP.sys/SMB ports) and other protected processes are disabled. **Processes owned by other users / SYSTEM / elevated need administrator rights**: on access denied use the same one-shot UAC helper (`Trayloom.exe --kill-batch ...`), confirmation text `Trayloom needs administrator access to end “<name>”.`; services (`svchost` hosting several services) should still be offered but killing them can disrupt Windows — keep them enabled (Mac has no equivalent), only PID 4 and the critical set are protected.
- Clipboard/Open-in-browser: `Process.Start(new ProcessStartInfo(url){UseShellExecute=true})`.
- Panel view: reuse the Utilities-panel host; same row template with smaller fonts.

### 6.5.7 Gap vs Trayloom (`PortManager\PortManagerFeature.cs`, 385 lines)
Has: `GetExtendedTcpTable/GetExtendedUdpTable` interop (v4+v6), a standalone `PortManagerWindow` (grid) showing TCP+UDP (+optional all TCP connections via `showAll`), buttons `End owner process` / `End owner tree`, shared `ProcessKiller`, hotkey option; description `See which program is listening on every TCP and UDP port…`.
Prioritised gaps:
1. Restrict to **TCP LISTEN** and add the Mac row design (port, protocol capsule, All-interfaces badge, process, `PID n  •  addr`), wildcard `*` formatting and dedupe/sort rules; delete the `showAll`/UDP options.
2. Settings page + **menu-panel inline view** (`Your listening ports`, `<n> open`, filter, refresh, gear to Settings, 260 px list) and the panel tile; strings `Port Manager`, `Filter by port, process, or PID`, empty/hint/failed states.
3. Kill / Force Kill buttons (and confirmation `Terminate <name>?` / `This closes port <p> by terminating PID <n>.`) gated on Kill Process being installed and identity stability; context menu `Copy port` / `Copy PID` / `Copy address:port` / `Open in browser`.
4. Elevation helper for access-denied listeners (shared with 6.4); protected-process disabling (PID 4).
5. Opt-in default, hub description text, hub symbol.

---

## 6.6 Wallpaper

### 6.6.1 What it is (answering "what does it do")
**Not** a live/dynamic/video wallpaper engine. It is a **still-image wallpaper picker**: a paged thumbnail gallery of (a) Apple's built-in desktop stills and (b) the user's own images/folders, living in the menu panel (and Island app panel), that sets the desktop picture with one click — without opening System Settings — optionally on **all Spaces and displays** (it patches WallpaperAgent's `Index.plist` store so the still replaces every Space's wallpaper) or only the current Space of the display under the pointer. iCloud-only placeholders are downloaded before applying. There is no scheduling, rotation, video, shuffle or dynamic-HEIC handling. (Apple "dynamic" `.madesktop` wallpapers are listed using their full-size still.)

### 6.6.2 Features-hub entry
| Item | Value |
|---|---|
| Hub title | `Wallpaper` |
| Hub description | `Pick a still wallpaper without opening System Settings` ([WIN] `Pick a still wallpaper without opening Settings`) |
| Panel description (panel layout editor) | `Pick a still wallpaper without opening System Settings.` ([WIN] `…Settings.`) |
| Symbol | `photo.on.rectangle` -> Segoe `E91B` (Photo2) / `EB9F` (v) |
| Permissions | none (Mac: sandbox bookmarks only) |
| Installed by default | **no** (opt-in) |
| Energy | Nothing at rest |
| Surfaces | Menu-panel section "Wallpaper" (panel section id `wallpaper`, title `Wallpaper`, symbol `photo.on.rectangle`, visibility key `panelShowWallpaper` default **true**, estimated panel height 480) — also usable inside the island's app panel; a small Settings section on the Quick tools page. |

### 6.6.3 Strings (enUS)
| Key | Text |
|---|---|
| pageTitle | Wallpaper |
| hubDescription | Pick a still wallpaper without opening System Settings |
| panelDescription | Pick a still wallpaper without opening System Settings. |
| filterAll | All |
| filterOwn | Your pictures |
| filterApple | Apple ([WIN] `Windows`) |
| applyAllDisplays | Show on all Spaces ([WIN] `Show on all displays`) |
| addImage | Add image |
| addFolder | Add folder |
| removeAdded | Remove |
| doneRemoving | Done |
| sourceUnavailable | Unavailable |
| addImagePrompt | Choose images to keep in Vorssaint’s wallpaper list (open-file dialog message; [WIN] Trayloom’s) |
| addFolderPrompt | Choose a folder of images to keep in Vorssaint’s wallpaper list ([WIN] Trayloom’s) |
| openSystemSettings | Open Wallpaper settings ([WIN] `Open Background settings`/keep `Open Wallpaper settings`) |
| emptyAll | No wallpapers found |
| emptyOwn | No pictures added yet |
| emptyApple | No Apple stills found ([WIN] `No Windows wallpapers found`) |
| downloading | Downloading… |
| downloadFailed | Could not download the wallpaper |
| applyFailed | Could not set the wallpaper |
| previousPage | Previous |
| nextPage | Next |

### 6.6.4 Layout — panel section (`WallpaperSection`)
`PanelSection(.wallpaper, title: "Wallpaper", collapsible)` containing VStack(leading, spacing 10):
1. **Controls** (VStack spacing 8):
   - Filter `Picker` (pop-up menu, label hidden): `All`, `Your pictures`, `Apple`. Changing the filter animates nothing (animations disabled), resets to page 1, rebuilds thumbs.
   - Toggle (switch style, small) `Show on all Spaces` bound to `wallpaperApplyAllDisplays`.
   - HStack spacing 8: bordered small buttons `Add image`, `Add folder` (both disabled while in remove mode); when any own sources exist (or already in remove mode) a bordered small button `Remove` (label becomes `Done` while in remove mode, toggles remove mode); spacer.
2. **Remove strip** (only in remove mode and when `removableChipSources` is non-empty): horizontally scrolling chips (spacing 8). Chip = HStack spacing 6, padding 8/5, radius 8 continuous, fill secondary @ 15 %: icon (`folder` for folders, `photo` for files, `exclamationmark.triangle` when unreachable; 11 pt semibold secondary), title (caption, 1 line, middle truncation; `Unavailable` when the bookmark cannot be resolved), red-filled `xmark.circle.fill` (palette white on red, 14 pt; tooltip/label `Remove`). Chips list: folders + unreachable sources + file sources with no gallery cell (while scanning only folders+unreachable).
3. **Gallery**: 3-column `LazyVGrid` (flexible columns, spacing 8), thumbnail height **54**, radius 6 continuous, placeholder fill secondary @ 15 % with `photo` 16 pt secondary, image aspect-fill clipped. Page size **24** (3 x 8). Fixed gallery height: with more than one page the full 8 rows (8*54 + 7*8 = 488); with a single page only the rows needed (`ceil(n/3)`, 2 rows when empty). Cell: button; applied wallpaper has a 2 px accent border (radius 6); tooltip/a11y label = title (file name without extension); in remove mode own images get a red `xmark.circle.fill` badge (16 pt, offset (+4,-4), shadow) and applying is disabled; also disabled while an apply is running. Empty: centred spinner while loading, else a caption secondary message by filter: `No wallpapers found` / `No pictures added yet` / `No Apple stills found`.
4. **Pager** (HStack spacing 12, height 22): only when pages > 1: borderless `chevron.left` button (22x22, tooltip/label `Previous`, disabled on page 1), `"<page> / <pages>"` caption secondary monospaced digits, `chevron.right` (`Next`, disabled on last). Then spacer and a plain caption-secondary link button `Open Wallpaper settings` (1 line, min scale 0.85) that opens the OS wallpaper settings.
5. Status line: caption secondary `Downloading…` while a cloud file downloads; else if `lastError` caption **orange** with that message (`Could not set the wallpaper` / `Could not download the wallpaper`).

### 6.6.5 Settings (Quick tools page -> Wallpaper section; header `Wallpaper`)
Rows: Toggle `Show on all Spaces` ([WIN] `Show on all displays`) bound to `wallpaperApplyAllDisplays` (reads default true when unset); Button (icon `gearshape`) `Open Wallpaper settings`.
| Key | Type | Default | Notes |
|---|---|---|---|
| wallpaperApplyAllDisplays | bool | true | all Spaces/displays vs pointer's display, current Space |
| wallpaperFilter | string | `all` | `all`, `own`, `apple`; invalid -> all; restored on launch |
| wallpaperOwnBookmarks | [Data] | [] | security-scoped bookmarks of added images/folders (Windows: `[string]` of absolute paths) |
| wallpaperExcludedOwnPaths | [String] | [] | sorted paths hidden from the gallery (images inside added folders the user removed with the badge; files remain on disk). Re-adding a path (or a parent folder) clears the exclusion(s) |
| panelShowWallpaper | bool | true | panel section visibility |
Settings backup keeps bookmarks; uninstalling the feature keeps them (clears only in-memory catalog and thumbnails) and deletes the Index.plist backup.

### 6.6.6 Behaviour
- **Catalog** (`refresh`): runs only while a gallery is visible (viewer registry; closing the last viewer cancels scans and bumps the thumbnail generation); Apple entries scanned once and cached; own roots resolved once per pass; scanning off the UI thread; results published only if the scan token is still the latest; first load shows a spinner only when there are no entries yet. `merge(apple, own)` removes duplicates by id (path) and sorts all by title case-insensitively (locale-aware). `filtered` by `all|own|apple`.
- **Apple stills** (Mac): `/System/Library/Desktop Pictures`: top-level image files (heic jpg jpeg png tif tiff gif bmp webp, case-insensitive, hidden files skipped) and `.madesktop` entries resolved to `.wallpapers/<stem>/<stem>.heic` (full still) with preview = plist `thumbnailPath` if it exists else `.thumbnails/<stem>.heic` else the still; entries without a still are dropped; ids = standardised path; titles = file stem.
- **Own sources**: `Add image` (multiple files, allowed types = the extension list) / `Add folder` (single folder) via open dialogs; the dialog is brought to the front even when the app is inactive or the island is hosting; dialog message = prompt strings; only still-image files (or directories) are remembered; adding replaces older bookmarks to the same path, clears exclusions for that path/children, persists, rebuilds sources and refreshes. Folder scan is **recursive**, skipping hidden files, symlinks and packages/bundles, sorted by file name case-insensitively; image test by extension. File bookmark entries whose file is missing show as `Unavailable`/unreachable chips so they can be removed. Removing a file source drops its bookmark; removing a folder source drops the bookmark and clears exclusions under it; the per-image `x` badge removes a file bookmark or, for an image found through a folder, adds its path to the exclusion list (the file is untouched).
- **Apply** (`apply(entry)`): ignores when feature unavailable; clears `lastError`; target displays = all (apply-all on) else the display under the pointer else the first; none -> `applyFailed`. New apply token cancels older ones. Steps: (1) if the file is an iCloud placeholder, show `Downloading…` and download with a **60 s** timeout polling every **250 ms**, aborting on error/timeout (`downloadFailed`); (2) set the desktop image for the chosen displays with *fill (crop)* scaling and clipping allowed — if a display already shows the same URL, first set an empty URL and wait **400 ms** (macOS skips refreshes for identical URLs); (3) when apply-all is on, additionally patch the wallpaper store for every Space/display (see below) off the main thread, abandoning if a newer apply superseded it; success if either the current-space or the store patch succeeded (`appliedPath` = file path -> thumbnail gets the accent border); a store-patch miss with a successful current-space apply is **not** an error; both failing -> `applyFailed`. With apply-all off only step 2 counts.
- **Store patch** (Mac internals, informational): reads `~/Library/Application Support/com.apple.wallpaper/Store/Index.plist`, creates a one-time backup `WallpaperIndex.vorssaint-bak` in the app container (never overwritten; deleted on feature uninstall; migrates the legacy next-to-plist backup), stamps an `imageFile` choice (`com.apple.wallpaper.choice.image`, bplist `Configuration {type:imageFile, url:{relative:<file url>}}`, option `placement = Crop`, `LastSet/LastUse = now`) into `AllSpacesAndDisplays`, every `Displays` entry, every `Spaces[*].Default` and `Spaces[*].Displays[*]`, and `SystemDefault`, honouring Linked vs Desktop slot types (unknown layout -> abort, leave the file alone), writes atomically, then `killall WallpaperAgent`. Not applicable on Windows.
- **Thumbnails**: decoded off the UI thread at **max 160 px** (ImageIO thumbnail with transform), cached in memory (**400** items, process lifetime, cleared on uninstall); prefetch current + next page; a generation counter cancels stale decodes when paging/filtering/closing; iCloud placeholders are not read (placeholder tile instead).
- Paging: `pageCount = ceil(n / 24)`; current page clamped to `[1, pages]`; changing filter or entry count clamps/resets.

### 6.6.7 Windows mapping
| Mac piece | Windows implementation |
|---|---|
| Apple stills | Windows built-in pictures: `%WINDIR%\Web\Wallpaper\**\*.jpg|png` and `%WINDIR%\Web\4K\Wallpaper\**` (prefer the largest variant per folder), plus `%WINDIR%\Web\Screen`. Title = theme folder name (suffix ` 2`, ` 3` for several images in one folder). Filter label `Apple` -> `Windows` [WIN]. If none exist show `emptyApple` equivalent. |
| Own bookmarks | plain absolute paths (JSON list), with existence checks; unreachable paths shown as `Unavailable` chips; no sandbox. |
| Open panel | `OpenFileDialog` (multi-select, image filter) / `FolderBrowserDialog` or `IFileOpenDialog` with `FOS_PICKFOLDERS`, owner = foreground Trayloom window, message via `Title`. |
| Set wallpaper | `IDesktopWallpaper` COM (`CLSID_DesktopWallpaper` {C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD}): `SetPosition(DWPOS_FILL=4)` ("Fill Screen = Crop"), `SetWallpaper(monitorId, path)` per monitor id from `GetMonitorDevicePathAt`; **apply-all = every monitor** (`monitorId=null` sets all), off = the monitor under the pointer (`MonitorFromPoint`/`GetMonitorDevicePathAt`). Windows 11 per-virtual-desktop wallpapers are not exposed by any public API, so "Show on all Spaces" can only mean "all displays" (rename [WIN] `Show on all displays`). Fallback: `SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE|SPIF_SENDCHANGE)` + registry `HKCU\Control Panel\Desktop` `WallpaperStyle=10` (Fill) `TileWallpaper=0`. No same-URL clearing hack is needed. Existing Trayloom `WallpaperApi` already has this. Supported formats: jpg, jpeg, png, bmp, gif(static first frame), tif/tiff, heic/webp only if the OS codecs are installed (decode check with WIC before applying; else `applyFailed`). |
| iCloud download | OneDrive/cloud placeholders: detect `FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS (0x400000)` or `FILE_ATTRIBUTE_OFFLINE`; trigger hydration by opening/reading (or `CfHydratePlaceholder`), poll every 250 ms up to 60 s, show `Downloading…`, else `downloadFailed`. |
| Thumbnails | WIC `BitmapDecoder` / WPF `BitmapImage` with `DecodePixelWidth=160`, `CacheOption=OnLoad`, frozen, `ConcurrentDictionary` LRU of 400, `CancellationToken`/generation. |
| Open Wallpaper settings | `Process.Start("ms-settings:personalization-background")`. |
| Panel section | the Utilities-style panel host; page size 24; same grid metrics. |
| Persistence | feature settings bag (keys above); `wallpaperOwnBookmarks` as `List<string>`. |
| Dynamic/video wallpapers | **not part of this feature**; (a WorkerW-hosted video wallpaper would be a separate product; do not build). |

### 6.6.8 Gap vs Trayloom (`Wallpaper\WallpaperFeature.cs`, 354 lines)
Has: `IDesktopWallpaper` COM + SPI fallback + registry style (`WallpaperFit` Center/Tile/Stretch/Fit/Fill/Span), standalone `WallpaperWindow` (Choose image…, monitor ComboBox `All monitors`/`Monitor n`, recent list of up to 12 with click-to-apply and drag-and-drop), `Random recent wallpaper` action, hotkey option, picture-position setting; description `Change your desktop wallpaper from any image, per monitor, and jump back to recent ones.`
Prioritised gaps:
1. Different product shape: Mac is a **panel gallery** (3x8 paged grid, filter All/Your pictures/Windows built-ins, add image/folder sources, remove mode, applied highlight), not a recent-list window.
2. Source management: persisted image + recursive folder sources with exclusions, unreachable chips, `Unavailable`, empty-state messages; built-in Windows wallpaper catalog.
3. Settings: only `Show on all Spaces/displays` + `Open Wallpaper settings` (drop fit option, hotkey, monitor picker, `random recent`, or keep as extras); keys `wallpaperApplyAllDisplays`, `wallpaperFilter`, `wallpaperOwnBookmarks`, `wallpaperExcludedOwnPaths`, `panelShowWallpaper`.
4. Thumbnails pipeline (160 px, cache 400, prefetch, generation cancellation) and OneDrive placeholder handling (`Downloading…`, `downloadFailed`).
5. Always apply with Fill; error line `Could not set the wallpaper`; `appliedPath` highlight; panel section integration and opt-in default, hub description text.

---

## 6.7 Feedback

### 6.7.1 What it is / where it is launched
A small **"Send feedback" window** that posts a bug report or feature idea (plain text up to 2 000 characters, optional technical details) directly to the maintainer's server. It is not a Features-hub tool (no tile, no availability key, no energy profile). Entry points:
1. **Settings -> General page**, last card (below Keep Awake shortcut card): a `SettingsCard` with one `SettingsRow` — icon `bubble.left.and.text.bubble.right` (Segoe `E939` Feedback / `E8F2` ChatBubbles (v)), title `Feedback`, caption `Send a bug report or feature idea directly to the person who maintains Vorssaint.` ([WIN] Trayloom), trailing button `Send feedback`. Opens the window with kind = Bug.
2. **Menu panel header** — shown **only on beta builds** (`AppInfo.isBeta`: version contains `-beta`/`-rc`/`-alpha`, or developer build with simulate-beta set): next to a `BETA` pill (orange text on orange @ 18 % capsule, 9 pt bold) a plain icon button (`bubble.left.and.text.bubble.right`, 11 pt medium, secondary, padding 4) with tooltip `Send feedback`; opens kind = Bug; panel closes first.
3. **Command Bar** actions: `Report a bug` (symbol `ladybug`, subtitle `Send feedback`) opens kind=Bug; `Suggest a feature` (symbol `lightbulb`, subtitle `Send feedback`) opens kind=Feature (after a short beat, so the bar can close). Segoe suggestions: bug `EBE8`, lightbulb `EA80` (v).
`openFeedbackWindow(kind)`: closes the panel popover; builds (or **replaces the content of**) a single reusable window (so re-opening resets state and kind), `titled + closable` only (no minimize/resize), title hidden in the content area (the system title text is `Send feedback`), centred, not restorable; activates the app and brings the window to front. Closing: the `Done` header button or the window close button.

### 6.7.2 Strings (enUS, `FeedbackStrings`)
| Key | Text |
|---|---|
| sectionTitle | Feedback |
| sectionCaption | Send a bug report or feature idea directly to the person who maintains Vorssaint. |
| openButton | Send feedback |
| windowTitle | Send feedback |
| bugTitle | Bug |
| featureTitle | Feature idea |
| messageLabel | What would you like to share? |
| bugPlaceholder | Tell me what happened and what you expected. |
| featurePlaceholder | Describe the idea and how it would help. |
| charactersFormat | %d of 2000 characters |
| includeDiagnostics | Include technical details |
| includeDiagnosticsCaption | Adds only the technical details shown below. It does not include logs. |
| whatSentTitle | What will be sent |
| whatSentBasic | Your chosen category and the text above. |
| whatSentDiagnostics | The technical details listed below. |
| privacyNote | No name, account, email, device identifier, logs, screenshots, files or clipboard content are included. Your public IP is temporarily processed for abuse protection and is not attached to the feedback. |
| retentionNote | After delivery, the text remains in private support channels until the service owner deletes it. An undelivered copy is permanently deleted after 7 days. |
| sendButton | Send feedback |
| sending | Sending… |
| sentTitle | Feedback sent |
| sentCaption | Thank you. No contact information was sent, so you will not receive a direct reply. |
| unavailableError | Could not connect. Check your internet connection and try again. |
| rateLimitError | Too many submissions from this network. Please try again later. |
| genericError | Could not send the feedback right now. |
| done | Done |
| commandBug | Report a bug |
| commandFeature | Suggest a feature |
| commandSubtitle | Send feedback |
| diagnosticsChannelLabel | Update channel |
Also: `Language` (`languageLabel`), `Beta` (`betaBadgeLabel`) labels in the technical-details preview.

### 6.7.3 Layout (window 600 x 650, fixed)
VStack(spacing 0): header, divider, then the form or the sent view.
- **Header** (HStack spacing 12, padding h 24 / v 18): `bubble.left.and.text.bubble.right.fill` icon (title2 size, accent tint), title `Send feedback` (title2 semibold), spacer, button `Done`.
- **Form** = scroll area (VStack leading spacing 18, padding 24) + bottom bar:
  1. Segmented control (labels hidden) with two segments: `Bug` | `Feature idea`.
  2. Message block (VStack spacing 8): headline `What would you like to share?`; multi-line text editor (body font, content padding 6, min height **145**, background secondary/background-secondary fill, radius 9, 1 px separator stroke) with an overlay placeholder (`bugPlaceholder` for Bug, `featurePlaceholder` for Feature; tertiary, offset 11/6, hit-test disabled) shown while empty; below it, right-aligned caption monospaced digits `"<n> of 2000 characters"` (n = UTF-16 length of the raw text; **red when n > 2000**, secondary otherwise).
  3. Diagnostics toggle `Include technical details` (default off) + caption `Adds only the technical details shown below. It does not include logs.`
  4. "What will be sent" card (VStack leading spacing 12, padding 16, quaternary @ 45 % fill, radius 12): headline label `What will be sent` (`eye`); label `Your chosen category and the text above.` (`text.alignleft`); **if diagnostics on**: label `The technical details listed below.` (`info.circle`) and a monospaced caption grid (indented 26) of rows (label tertiary / selectable value secondary, column gap 14, row gap 5): `Vorssaint <version> (<build>)`, `macOS <major.minor.patch>`, `Mac <hw.model identifier>` (omitted if unknown), `Language <language raw value e.g. en-US>`, `Beta ✓` (beta build) or `○`, `Update channel <value>` where the channel is `developer`/`beta`/`beta-opt-in`/`stable` with `-` replaced by a space and capitalised (`Beta Opt In`); divider; label `privacyNote` (`hand.raised`, caption secondary); label `retentionNote` (`clock`, caption secondary). [WIN] labels: `Trayloom <version> (<build>)`, `Windows <10.0.build>`, `PC <model>` (WMI `Win32_ComputerSystem.Model`, or omit), others unchanged.
  - **Bottom bar** (divider above; padding h 24 / v 16; HStack spacing 12): optional error `Label` (`exclamationmark.triangle.fill`, caption, **red**, max 2 lines) left; spacer; while sending a small spinner + `Sending…` (secondary); prominent large button `Send feedback`, disabled unless `canSend`; shortcut **Cmd+Return -> Ctrl+Enter**.
- **Sent view** (replaces the form, centred, padding 40, spacing 14): `checkmark.circle.fill` 54 pt green, `Feedback sent` (title2 semibold), caption `Thank you. No contact information was sent, so you will not receive a direct reply.` (secondary, centred, max width 400), prominent large `Done` button (top padding 8).

### 6.7.4 Behaviour and wire protocol
- `canSend` = trimmed message UTF-16 length **>= 10 and <= 2000** and not already sending. The raw text is hard-limited: input beyond 2000 UTF-16 units is cut at the last whole character (if everything would be cut, revert to the previous text). Any edit clears a displayed error.
- Send: sets sending, clears error, async; success -> sent view and clears the text; errors map: rate-limited -> `rateLimitError`, unavailable (network/5xx) -> `unavailableError`, anything else -> `genericError`. Sending resets afterwards.
- **Request** (`FeedbackService`): `POST https://screenshots.vorssaint.com/v1/feedback` ([WIN] **Trayloom needs its own endpoint; the Vorssaint URL must not be used — OPEN QUESTION, see below**). Ephemeral session: no cache, `reloadIgnoringLocalCacheData`, cookies disabled, `waitsForConnectivity=false`, request timeout **20 s**, resource timeout **30 s**. Headers: `Content-Type: application/json`, `Accept: application/json`, `Cache-Control: no-store`. Body JSON (UTF-8): `{"kind":"bug"|"feature","message":"<trimmed text>","diagnostics":{"appVersion","appBuild","macOS","macModel"(optional),"language","isBeta","updateChannel"}}` where `diagnostics` is omitted when the toggle is off. Client-side rejects (throws `rejected` before sending): trimmed length < 10 or > 2000, encoded body > **8 KiB**. Response rules: body > 16 KiB or non-HTTP -> invalidResponse; **201** with JSON `{"id": "<32 chars [A-Za-z0-9_-]>"}` = success (any other 201 body = invalidResponse); **429** -> rateLimited; **5xx** -> unavailable; anything else -> rejected; transport error -> unavailable.
- Privacy statement in-UI: no identifiers, logs, screenshots or clipboard; public IP processed for abuse protection; text retained until deleted; undelivered copy removed after 7 days.
- Developer-build channel: `developer` if bundle id ends `.dev`, else `beta` if beta version, else `beta-opt-in` if the user enabled beta updates, else `stable`.

### 6.7.5 Windows mapping
- Window: WPF `Window` 600x650, `ResizeMode=NoResize`, `WindowStyle=SingleBorderWindow` without min/max (`WS_MINIMIZEBOX|WS_MAXIMIZEBOX` cleared), `WindowStartupLocation=CenterScreen`, single instance reused (replace content on re-open).
- HTTP: `HttpClient` with `HttpClientHandler{UseCookies=false}`, `Timeout=20s` (+ 30 s total via `CancellationTokenSource`), `CacheControl no-store`; JSON via `System.Text.Json` (camelCase as above; use `isBeta` bool). Diagnostics values: `appVersion`/`appBuild` from assembly, `windows`/OS version from `RtlGetVersion`, `deviceModel` from WMI or omitted. The backend contract currently names the fields `macOS`/`macModel`: agree the Windows field names (`os`, `osVersion`, `deviceModel`) with whoever builds the endpoint.
- Segmented control: two `RadioButton`s styled as a segmented pill. Text editor: `TextBox` `AcceptsReturn`, wrapped, min height 145. Counter uses UTF-16 `.Length` (same as Swift `utf16.count`).
- Beta pill / header icon: depends on the app's version string (`-beta`, `-rc`, `-alpha`).
- No elevation, no permission.

### 6.7.6 Gap vs Trayloom
`grep -ri feedback src` finds only unrelated strings (`InputFeatureBase.cs`, `IslandTheme.cs`): **the Feedback window, service, strings, Settings card, panel beta icon and Command Bar actions do not exist** in Trayloom. Build all of 6.7 new (General settings card is owned by the Settings/General spec but must contain the row defined here).
Open questions for the integrator: (1) the feedback endpoint host and field names (Mac uses `https://screenshots.vorssaint.com/v1/feedback`, a Vorssaint-owned server shared with the screenshot upload; Trayloom needs its own or must disable submission / fall back to opening a pre-filled GitHub issue URL — product decision); (2) the Windows equivalent of the `⌃⌥⌘` default-shortcut modifier triple (affects 6.1/6.2 defaults); (3) Markdig (NuGet) for the Scratchpad preview — allowed list in ARCHITECTURE.md only names NAudio and CommunityToolkit.Mvvm, so ask first or hand-write the parser; (4) CPU % normalisation in Kill Process (per-core vs per-total); (5) whether to drop Trayloom's extra options (camera shape/size, cleaning auto-timeout/hotkey, wallpaper fit/random) or keep them as hidden extras.


---

## 7. Maintenance tools

# 04-7 Maintenance tools (Cleaner, Uninstaller, App updates, winget, Media)

Source of truth: `Sources/Vorssaint/{Services,UI}/{Cleaner,Uninstall,AppUpdates,Homebrew,Media}`, strings in `Core/Localization.swift` (enUS block: `cleaner*`, `uninstaller*`, `homebrew*`, `media*`), `Core/AppUpdateStrings.swift`, `Core/MediaImageStrings.swift`, defaults in `Core/Defaults.swift`.
All English strings below are the enUS strings verbatim (typographic apostrophe is U+2019, ellipsis U+2026, arrow U+2192). Text marked `[WIN]` is a platform substitution: the verbatim Vorssaint text is quoted next to it. Segoe Fluent code points marked "(verify)" must be checked in Character Map before use.

Common conventions used in this part (apply to all five tools):

| macOS concept | Windows replacement |
|---|---|
| Trash (`trashItem`) | Recycle Bin via `IFileOperation` with `FOF_ALLOWUNDO\|FOF_NOCONFIRMATION\|FOF_NOERRORUI\|FOF_SILENT` (or `SHFileOperationW` with `FO_DELETE`+`FOF_ALLOWUNDO`); permanent delete only where stated |
| Finder "Reveal in Finder" | `explorer.exe /select,"<path>"` |
| Finder admin prompt (`tell Finder to delete`) | One batched UAC elevation: relaunch `Trayloom.exe --elevated-clean <manifest.json>` with `Verb = "runas"`; cancel leaves items in place and counts them as failed |
| Full Disk Access note card | "Administrator" note card (see 7.1.4); no permission exists on Windows |
| Menu-bar panel (`compact: true`) | Trayloom quick panel / tray flyout hosting the same view with the compact metrics |
| `NSOpenPanel` / `NSSavePanel` | `Microsoft.Win32.OpenFileDialog` / `SaveFileDialog` / `OpenFolderDialog` (.NET 8+) |
| `UserDefaults` key | `IFeatureSettings` key, scoped to the feature id; keep the Swift key names (they are listed in each Settings table) |
| `Notifier.post(title:body:)` | `INotifier.Show(title, body)` (toast) |
| `ByteCountFormatter(.file)` | 1000-based decimal units: `B`, `KB`, `MB`, `GB`, `TB` with 1 fractional digit above KB (macOS file style; `UiKit.FormatBytes` is acceptable if it is 1000-based, otherwise fix it) |

Typography/size map used below: SwiftUI point sizes are rendered 1:1 as WPF DIPs (12 pt text = `FontSize=12`). Fonts: `Segoe UI Variable Text`. Semantic colours: `secondary` = theme muted text, `tertiary` = ~55 % of muted, `green` #34C759, `orange` #FF9500, accent = system accent. A "card" is `RoundedRectangle(cornerRadius: 9, continuous)` filled with foreground colour at 5 % opacity (light: #0D000000, dark: #0DFFFFFF), inner padding 11.

---

## 7.1 Cleaner

### 7.1.1 Feature identity

| Item | Value |
|---|---|
| Feature id | `cleaner` (Trayloom id already `cleaner`) |
| Hub tile title | `Cleaner` (`cleanerName`) |
| Hub tile description | `Clear caches and junk files` (`descCleaner`). When the WhatsApp downloads module is enabled Vorssaint appends ` · ` + that module's `hubDescription` (not part of this spec) |
| Hub group | Tools (`groupTools`) |
| SF symbol | `sparkles` -> Segoe Fluent: no exact glyph, use U+E734 (Favorite/star outline, verify) or draw a 3-point sparkle `Path`; the same glyph is used as the page glyph |
| Panel/launcher entry | title `Cleaner`, caption `App leftovers, caches and logs` (`cleanerPanelCaption`), panel header glyph SF `sparkle` 13 pt semibold, title 13 semibold, close `xmark.circle.fill` 14 pt secondary, tooltip/accessibility `Close` (`menuClose`) |
| Permissions chips on hub | macOS lists Full Disk Access, Files & Folders, Notifications. Windows: only Notifications (toast permission). Do not show the other two |
| Settings page | Sidebar item `Cleaner` hosts `CleanerView` directly (non-compact). Vorssaint shows a 2-segment picker (`Cleaner` / `WhatsApp downloads`) above it only when the WhatsApp module is enabled; ignore here |

### 7.1.2 Layout

The view is a state machine driven by `JunkCleaner.phase`: `idle -> scanning -> results -> cleaning -> done(freed, failed)`. `compact` = hosted in the panel (tighter paddings, scroll provided by host); non-compact = Settings page.

**Idle state** (`idleState`), centred column, spacing 18 (compact 12), padding 28 (compact 14), top/bottom `Spacer` (non-compact only):
1. Sparkle glyph, 46 pt (compact 34), light weight, secondary colour. Static when idle (it animates only while busy: a repeating "variable colour" shimmer layer by layer, ~1 s cycle, reversing; WPF: animate the three star paths' opacity 0.35 -> 1 sequentially, `Duration=0.35s`, `AutoReverse`, `RepeatBehavior=Forever`).
2. Title `Clean up your Mac` -> [WIN] `Clean up your PC` (`cleanerIntroTitle`), 17 semibold (compact 15).
3. Caption `Scans for leftovers from uninstalled apps, caches, logs and the Trash. You review everything first and removed items go to the Trash.` -> [WIN] `Scans for leftovers from uninstalled apps, caches, logs and the Recycle Bin. You review everything first and removed items go to the Recycle Bin or are deleted as described in the list.` (`cleanerIntroCaption`), 12 secondary, centred, max width 380.
4. `Scan` button (`cleanerScan`), large, prominent (accent fill). Click -> `scan(attended: true)`.
5. **Schedule card** (7.1.3).
6. **Forgotten screenshots card**: card (max width 380, padding 11, radius 9, 5 % fill); row HStack spacing 8: glyph `camera.viewfinder` (U+E722, secondary), label `Forgotten screenshots` (`cleanerCatScreenshots`, 12 medium), Spacer, dropdown (see Settings table). Under it (non-compact only) caption 10 (`.caption2`) secondary: `The scan lists the screenshots you have not opened for this long, unchecked, with their total size.` (`cleanerScreenshotsSettingCaption`).
7. (WhatsApp opt-in card / WhatsApp row — skip.)
8. Administrator note (Windows replacement of `FullDiskAccessNote`), only when the process is not elevated; max width 380. See 7.1.4.

**Busy states** (`scanning` / `cleaning`): column spacing 16, min height 320 (compact 0 with 24 pt spacers): animated sparkle 54 pt (compact 40), message (`Scanning…` / `Cleaning…`, secondary), optional detail line (caption, tertiary) = display-group title of the category currently being scanned (`scanningCategory`), and during scanning only a `Cancel` button (`uninstallerCancel`; large, compact small) -> `reset()` (cancels the scan).

**Results state**: VStack spacing 0: header, Divider, then either empty view or list + Divider + footer.
- Header (padding 16, compact 12): sparkle 22 pt, VStack(spacing 2): `Cleaner` 15 semibold + `"<total size> found"` (`ByteCountFormatter` + space + `uninstallerFoundTitle`="found"; 12-pt caption secondary); Spacer; `xmark.circle.fill` button 16 pt secondary (plain) -> `reset()`.
- Empty (no items): centred, `checkmark.seal` 34 pt light green (use U+E930 or U+E73E in green), text `Nothing to clean. Your Mac is tidy.` -> [WIN] `Nothing to clean. Your PC is tidy.` (`cleanerNothingFound`, 13 secondary).
- Non-empty: list (`List` inset style in Settings; plain VStack padding 12 spacing 16 in the panel) with two sections, headers `Safe cleanup` (`cleanerSafeSection`) and `Optional, review first` (`cleanerOptionalSection`) (compact: 11 semibold secondary labels; sections hidden if none of their groups has items):
  - Safe section groups in order: **loginItems, safeCaches, logs, developer**.
  - Optional section groups in order: **leftovers, otherCaches, deviceBackups, screenshots, trash**.
  - **Group row** = collapsed `DisclosureGroup` (collapsed by default, expanding shows the file-by-file rows). Label HStack spacing 10: tri-less checkbox (checked iff group non-empty and every item included; toggling sets all items), group icon 14 pt secondary in a 20-wide frame, VStack(spacing 1) {title 13 medium; caption 10.5 secondary wrapping}, Spacer, total size 11.5 secondary tabular digits. Vertical padding 3. Compact mode adds a Divider after every group.
  - Inside an expanded `leftovers` group first a 10-pt (`caption2`) **orange** note `Found by analysis and left unchecked. Check the path before ticking.` (`cleanerLeftoversNote`); inside `loginItems` a secondary note `The entry under Login Items disappears after restarting the Mac.` -> [WIN] `The entry under Startup apps disappears after you sign out and back in.` (`cleanerLoginItemsNote`).
  - **Item row** (leading padding 8, vertical 1): checkbox; VStack(spacing 1){ name (`lastPathComponent`, 12, 1 line, middle truncation); parent path (10, tertiary, 1 line, **head** truncation, home replaced by `~` -> [WIN] `%USERPROFILE%` shown as `~`) }; Spacer; size 11 secondary tabular. Context menu: `Reveal in Finder` (`cleanerRevealInFinder`) -> [WIN] `Reveal in File Explorer`.
  - Display-group icons (SF -> Segoe Fluent): loginItems `power` U+E7E8; safeCaches `archivebox` (use U+E7B8 package box, verify); logs `doc.text` U+E8A5; developer `hammer` (U+E90F, verify); leftovers `puzzlepiece` (U+EA86 puzzle, verify); otherCaches `internaldrive` U+EDA2 (verify); deviceBackups `iphone` U+E8EA; screenshots `camera.viewfinder` U+E722; trash `trash` U+E74D.
- Footer (padding 16, compact 12): `"<n> of <m> selected"` (`uninstallerSelectedFormat`, 12 secondary); Spacer; `Cancel` (reset); prominent large `Clean <size>` (`cleanerCleanSizeFormat`) disabled when `selectedCount == 0` -> `cleanSelected(escalate: true)`.

**Done state**: column spacing 16, padding 28 (compact 14), min height 320: `checkmark.circle.fill` 54 pt (compact 40) green; `Done!` (`uninstallerDoneTitle`) 20 bold (compact 17); `"<size> removed"` (`uninstallerFreedFormat`, 13 secondary); note `Items went to the Trash and can be recovered from there.` (`cleanerDoneNote`, caption tertiary, centred, max width 340) -> [WIN] see 7.1.6 (two sentences depending on what was permanently deleted); if `failed > 0` an orange caption `Some items couldn’t be moved to the Trash.` -> [WIN] `Some items couldn’t be removed.` (`uninstallerSomeFailed`, padding h 40); large button `Scan again` (`cleanerAgain`) -> `reset()`; 6 pt extra top padding.

### 7.1.3 Automatic cleanup (schedule card)

Full card (Settings page): VStack spacing 8, padding 11, max width 380, 5 % fill, radius 9.
- Row: `clock` glyph (U+E121 secondary), `Automatic cleanup` (`cleanerScheduleTitle`, 12 medium), Spacer, frequency dropdown with items `Off` / `Daily` / `Weekly` (`cleanerScheduleOff/Daily/Weekly`).
- When frequency != off (`scheduleDetails`):
  - Time row (HStack spacing 6; in a narrow panel the weekday wraps onto its own row): weekday dropdown (only when weekly; items = culture day names Sunday..Saturday, value 1..7 with 1 = Sunday); hour dropdown (12-h clock: `1..12`, then minute dropdown, then AM/PM dropdown using the culture's AM/PM designators; 24-h clock: `0..23`); minute dropdown items `:00 :05 … :55` (`":%02d"`) plus the stored value if it is off-grid (0..59).
  - Checkbox `Notify when done` (`cleanerScheduleNotifyToggle`, 11.5).
  - If notify is on and toasts are disabled for the app: orange caption `Vorssaint notifications are turned off in the system.` -> [WIN] `Trayloom notifications are turned off in the system.` (`cleanerNotifDenied`) + small button `Open Notification Settings…` (`cleanerNotifOpenSettings`) -> `Process.Start("ms-settings:notifications")`.
  - Next-run line (caption secondary): `Next cleanup %@.` (`cleanerScheduleNextFormat`) with the formatted fire date.
  - If `lastAutoRun > 0` a caption tertiary line: if `lastAutoFreed > 0` -> `The last automatic cleanup freed %@.` (`cleanerScheduleLastFormat`, size string) else `Last automatic cleanup %@.` (`cleanerScheduleRanFormat`, date); when `lastAutoFailed > 0` append a space + `Some items couldn’t be moved to the Trash.` ([WIN] `Some items couldn’t be removed.`).
- Footer caption (10 secondary, wraps): `Cleans only the safe part on its own at the chosen time and sends everything to the Trash.` -> [WIN] `Cleans only the safe part on its own at the chosen time. Items that must not be lost go to the Recycle Bin.` (`cleanerScheduleCaption`).
- Date format for next/last: medium date + short time with relative day words ("Today, 9:00 AM", "Tomorrow"): `DateTimeFormatInfo` + manual Today/Tomorrow/Yesterday substitution.

Compact (panel) card: folded button row: chevron (`chevron.right` U+E76C / `chevron.down` U+E70D, 9 pt semibold, 9-wide), clock glyph, `Automatic cleanup` 12 medium, Spacer, summary text (`Off`/`Daily`/`Weekly`, 10 secondary). Clicking toggles (no animation) an expanded body (padding h11, top 5, bottom 11) with the frequency dropdown, the same details and the caption. Starts folded every time.

Reactions (`ScheduleChangeSync`): any change of frequency/hour/minute/weekday -> `CleanerScheduler.syncWithPreferences()`; turning Notify on requests notification permission (no-op on Windows) and re-reads the toast state; the toast state is re-read on appear and whenever the app becomes active, 1 s delayed.

### 7.1.4 Windows replacement for the Full Disk Access note

Shown only when `!IsElevated`. Card (radius 9, 5 % fill, padding 11 / compact 9): info glyph (U+E946) + text 12 secondary: [WIN] `Run as administrator for a more thorough scan.` (replaces `uninstallerFDANote` = `Grant Full Disk Access for a more thorough scan.`), second paragraph 10 secondary: [WIN] `System caches such as Windows Update downloads, Prefetch and Windows\Temp are only visible to an administrator. Nothing is changed until you review the list.` (replaces `uninstallerFDAHint`), buttons (small): [WIN] `Relaunch as administrator` (replaces `uninstallerFDAGrant` `Grant access…` + `uninstallerFDARelaunch` `Relaunch now`; use only one button) -> `Process.Start(runas)` of the same exe then exit. If Trayloom is installed per-user without a manifest requiring elevation this relaunch is the only way to read `C:\Windows\SoftwareDistribution`.

### 7.1.5 Settings / persisted state (all in the feature's store)

| Key (keep name) | Type | Default | Allowed | Effect |
|---|---|---|---|---|
| `cleanerScheduleFrequency` | string | `off` | `off`, `daily`, `weekly` (anything else -> off) | Arms/disarms the scheduler |
| `cleanerScheduleHour` | int | 9 | 0..23 (clamped) | Fire hour (24-h value even on a 12-h UI) |
| `cleanerScheduleMinute` | int | 0 | 0..59 (clamped) | Fire minute |
| `cleanerScheduleWeekday` | int | 2 | 1..7, 1 = Sunday (clamped) | Weekly only; default Monday |
| `cleanerScheduleNotify` | bool | true | | Toast after an automatic pass |
| `cleanerLastAutoRun` | double | 0 | epoch seconds (> 0 = ran) | Last automatic pass |
| `cleanerLastAutoFreed` | int | 0 | bytes | Last automatic result |
| `cleanerLastAutoFailed` | int | 0 | items | Items an unattended pass had to leave |
| `cleanerScreenshotAgeDays` | int | 30 | 0 = off, else offered 7/14/30/60/90, any stored value 1..3650 is shown too (sanitised: `<=0 -> 0`, `min(x, 3650)`) | Age for "Forgotten screenshots"; dropdown items: `Off` (`cleanerScheduleOff`) then `After %d days` (`cleanerScreenshotsAfterFormat`) |
| `panelUtilityCleaner` | bool | true | | Show the Cleaner row in the panel |

All of these are portable settings (included in backup/restore).

### 7.1.6 Behaviour

**Categories** (`CleanerSupport.Category`, raw order is identity): `leftovers, loginItems, caches, logs, developer, trash, deviceBackups, screenshots`. Scan order in code: leftovers, loginItems, caches, logs, developer, trash, deviceBackups, then screenshots (only attended and only if age > 0). A path claimed by leftovers must not reappear under caches/logs (`claimed` set). The scan runs on a background thread, checks a cancellation token between categories, publishes `scanningCategory` before each category and appends results after it. A new `scan` while one runs is ignored.

**Item model**: `{id (GUID), path, category, size, detail (owning id / label / entry name / device+date), fileIdentity, recommended, include}`; `include` starts equal to `recommended`. Items per category are sorted by size descending. `selectedSize/selectedCount/totalSize` are derived.

**Pre-check policy (`CleanerPolicy`)** - "pre-check only when evidence is strong AND rebuilding is cheap":

| Group | Pre-checked |
|---|---|
| leftovers | no (`precheckLeftovers = false`) |
| loginItems (orphaned startup items) | yes (two independent signals required) |
| logs | yes |
| developer | yes |
| caches | per entry: third-party known-safe yes; Apple/Microsoft system caches no; sensitive no; plain unattributable names no (list below) |
| deviceBackups | no |
| screenshots | no |
| trash (Recycle Bin) | no (never recommended) |

Cache entry decision (macOS): hidden entirely (never shown) if name lower-cased ends with `.localized` or starts with any of: `com.vorssaint`, `CloudKit`, `com.apple.bird`, `com.apple.coreaudio`, `com.apple.audio.`, `coreaudiod`, `com.apple.systempreferences`, `com.apple.controlcenter`, `com.apple.finder`, `com.apple.dock`, `com.apple.FontRegistry`, `com.apple.ATS`, `com.apple.akd`, `com.apple.AuthKit`, `com.paceap.`, `com.native-instruments`, `com.fabfilter`, `com.spotify.client`. Shown but never pre-checked if it starts with `ms-playwright`. Pre-checked when it looks like a reverse-DNS bundle id and is not `com.apple.*`, or when the plain name is in {`homebrew, pip, node-gyp, yarn, npm, google, electron, cypress, typescript, puppeteer`}. A group `safeCaches` = caches with `recommended`, `otherCaches` (title `Other caches`, caption `Safe to remove, nothing breaks. Apps may open slower once and downloaded content, like offline music, downloads again.`) = the rest.

**Leftover detection (macOS, for reference of the algorithm to port)**: candidates are only direct children of 27 fixed Library roots (`Application Support, Caches, Preferences, Preferences/ByHost, Saved Application State, HTTPStorages, WebKit, Logs, Containers (reads owner from .com.apple.containermanagerd.metadata.plist), Cookies, PreferencePanes, Internet Plug-Ins, Services, QuickLook, Spotlight, Input Methods, Screen Savers, ColorPickers, Widgets, Address Book Plug-Ins, Contextual Menu Items, Safari/Extensions, Automator, CoreImage, Dictionaries, Components, Audio/Plug-Ins`) under `~/Library` and `/Library`. An entry becomes a candidate only if its name maps to a reverse-DNS identifier (strip one payload suffix from {.plist .log .savedState .binarycookies .prefPane .qlgenerator .mdimporter .service .appex .plugin .webplugin .saver .colorPicker .wdgt .app .framework .component .vst .vst3 .clap .dpm .aaxplugin .dictionary .safariextz .mailbundle}, a trailing UUID component, `group.`/`systemgroup.` prefixes only for shared-container detection, a leading 10-char team id) and the identifier is not protected (`com.apple.*` in any wrapping, `.developer.apple.`, `.is.workflow.`, `vorss.`, and shared infrastructure `org.sparkle-project, com.plausiblelabs, com.crashlytics, com.segment, io.sentry, com.amplitude, com.rollbar, com.google.keystone, com.google.softwareupdate, org.cups, org.swift`) and has **no living owner**: installed ids (apps found 3 levels deep in /Applications, /System/Applications, ~/Applications + login-item helpers + running apps), exact or dot-boundary prefix in either direction, or same vendor namespace (first two components, except code-hosting namespaces `github, gitlab, bitbucket, sourceforge, googlecode`), or Launch Services knows the id. Symlinks are skipped. This is a strictly name/identifier based decision with no numeric scoring.

**Orphaned startup items (loginItems)**: LaunchAgents/LaunchDaemons plists whose referenced absolute executable(s) (`Program`, `ProgramArguments[0]`, `BundleProgram`) all no longer exist (executables under `/Volumes/` count as present: unmounted drive is inconclusive) AND the label has no living owner and is not protected. Cleaning boots the agent out first (`launchctl bootout gui/<uid>/<label>`).

**Developer junk**: existing dirs with size > 0 among `~/Library/Developer/{Xcode/DerivedData, Xcode/DocumentationCache, CoreSimulator/Caches, Xcode/iOS DeviceSupport, Xcode/watchOS DeviceSupport, Xcode/tvOS DeviceSupport}`; group title `Developer junk`, caption `Xcode build and simulator leftovers.` -> [WIN] `Build and tooling leftovers that are rebuilt on demand.`.

**Device backups**: `~/Library/Application Support/MobileSync/Backup/*` directories (>0 bytes); detail = `"<Device Name>, <Last Backup Date medium>"` read from `Info.plist`, else the folder name. Title `iPhone backups`, caption `Old iPhone and iPad backups take a big slice of the storage macOS calls Other. Remove only the ones you no longer need; a new backup is made when you plug the device in again.` -> [WIN] replace `storage macOS calls Other` by `storage Windows counts as Other`.

**Forgotten screenshots**: only the top level of the screenshot folder(s) (macOS: the `com.apple.screencapture location` pref or Desktop; plus the app's own screenshot folder), only files carrying the system's screen-capture flag, still carrying the default dated name (renamed/moved = a user decision = never offered), with `max(created, modified, lastOpened) <= now - days`; size > 0; unchecked. Title `Forgotten screenshots`, caption `Screenshots you have not opened in %d days that still have the name and folder macOS gave them. Ones you renamed or moved never show up here.` -> [WIN] `... that still have the name and folder Windows gave them ...`.

**Trash**: one row, size = sum of visible (non-dot) entries; title `Trash` -> [WIN] `Recycle Bin`, caption (`cleanerTrashNote`) `Emptying the Trash is permanent.` -> [WIN] `Emptying the Recycle Bin is permanent.`. It is cleaned FIRST (before the moves, so that the clean does not empty what it just moved).

**Clean algorithm (`cleanSelected(escalate:)`)**: phase `cleaning`; for the trash row empty the bin (failure counts +1); for every other included item re-validate with `mayRemove` (see below), for `loginItems` retire the job then re-validate, then move to the Trash, on failure remember as "stubborn". Stubborn (root-owned) items: if `escalate` is false (unattended) count as failed; otherwise elevate once (single prompt) and count survivors as failed. Finish: `items = []`, `phase = done(freed, failed)`. Freed = sum of sizes of removed items.

**`mayRemove` safety guard** (last line of defence; port exactly): path is not one of the critical roots (`/ /Applications /Library /System /Users /usr /bin /sbin /etc /var /private /opt`, home, home/Library, Documents, Desktop, Downloads, Pictures, Music, Movies), name not `*.localized`, the file identity (device+inode) equals the one observed at scan time, not a symlink, `realpath == path`; for leftovers additionally: direct child of one of the leftover roots, `detail` is an attributable non-protected id with no living owner; for screenshots: a direct child of a screenshot folder that is a capture; finally `pathComponents.count >= 4`.

**Scheduler (`CleanerScheduler`)**: nothing exists while off (no timer/observers). `syncWithPreferences()` -> if feature installed and frequency != off: observe wake + clock changes, `scheduleNext()`, else `stop()`. `scheduleNext`: if `missedRun(now, lastRun, ...)` fire at `now + 120 s`; else `nextFireDate(after: now)`; one-shot timer with 5 s tolerance; `nextFire` is published (UI shows it). `nextFireDate(after, frequency, hour, minute, weekday)`: clamp hour 0..23, minute 0..59, weekday 1..7; `off -> nil`; daily -> next local time `hour:minute` strictly after `after`; weekly -> next such time on that weekday; `matchingPolicy: nextTime` (a non-existent DST local time moves to the next valid one). `missedRun`: false when off; false when never ran (no catch-up on first enable); else `nextFireDate(after: lastRun) <= now`. Hour helpers: `hour24(hour12, isPM) = (clamp(hour12,1,12) % 12) + (isPM ? 12 : 0)`; `hour12Components(h) = (h%12==0 ? 12 : h%12, h >= 12)`.
`runAutomaticCleanup()`: if phase != idle (user in a manual session) or a pass already observing -> retry in 600 s. Else observe `phase` (ignoring the replayed current value), `scan(attended: false)` (skips the screenshot search); on `results`: if `selectedCount > 0` -> `cleanSelected(escalate: false)` else finish with 0; on `done` -> finish; on `idle` (user interrupted) -> drop observer and `scheduleNext()`. Finish: reset cleaner, store `cleanerLastAutoRun = now`, `cleanerLastAutoFreed`, `cleanerLastAutoFailed`, notify if wanted, `scheduleNext()`. Notification (if `cleanerScheduleNotify`): title `Automatic cleanup`, body = sentences joined by a space: if freed > 0 `"%@ freed and sent to the Trash."` ([WIN] `"%@ freed."`; `cleanerAutoNotificationFormat`); if failed > 0 `Some items couldn’t be moved to the Trash.` ([WIN] `Some items couldn’t be removed.`); if both empty `Nothing to clean. Your Mac is tidy.` ([WIN] `...Your PC is tidy.`). It posts even when nothing was found (proof of life).
Unattended pass only ever cleans what the scan pre-checked, never elevates and never touches screenshots.

### 7.1.7 Windows mapping: junk categories, paths and decisions

Deletion policy decision `[WIN]`: sending temp/caches to the Recycle Bin on the same drive frees **no** disk space, so a literal port would defeat the purpose. Spec: **caches, logs, temp, crash dumps, Windows Update/Delivery Optimization caches, thumbnail/icon caches = permanent delete** (they are rebuilt; Trayloom's present engine already does this); **leftovers, orphaned startup items, device backups, forgotten screenshots = Recycle Bin** (user-meaningful, recoverable). Orphaned registry values/keys cannot be recycled: export the key/value to `%APPDATA%\Trayloom\cleaner\backups\<yyyyMMdd-HHmmss>.reg` first and mention that file in the done note. Done-note strings `[WIN]`: recoverable part `Items went to the Recycle Bin and can be recovered from there.` (keep `cleanerDoneNote` wording with Trash -> Recycle Bin), permanent part `Caches, logs and temporary files were deleted permanently.` Show only the sentences that apply. Size of a recycled item still counts as "freed" only for permanent items; for recycled items say `"%@ removed"` as in Swift (the Recycle Bin shows up as a Recycle Bin row on the next scan).

Mapping of every Vorssaint category to Windows (rows are the "item" granularity; each existing, non-empty path is one row with detail = the owning app/folder name; sizes use allocated size where cheap, else logical length; never follow reparse points):

| Group (Safe/Optional) | macOS source | Windows source (all roots must exist; items are direct children unless noted) | Pre-checked |
|---|---|---|---|
| **Orphaned startup items** (Safe) | LaunchAgents/Daemons | (a) Run values: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, `RunOnce`, `HKLM\...\Run`, `HKLM\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run`; (b) `shell:startup` and common startup folders (`%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup`, `%ProgramData%\Microsoft\Windows\Start Menu\Programs\Startup`) `.lnk`/`.exe` whose target is missing; (c) Task Scheduler tasks (outside `\Microsoft\Windows\*`) whose `Actions/Exec/Command` file is missing; (d) services (`HKLM\SYSTEM\CurrentControlSet\Services\*`, `Type` 0x10/0x20, not Microsoft, `ImagePath` file missing). Rule: **every referenced executable is missing** (drive letters on removable/network/not-ready volumes count as present) AND the name has no living owner (no installed program/AppX/running process with that normalised name or publisher) AND it is not a protected Microsoft/Windows entry. Detail = value name / task path / service name. Cleaning: delete the Run value (after `.reg` backup), `schtasks /delete /tn <name> /f`, for services `sc delete` only in the elevated helper; startup-folder shortcuts go to the Recycle Bin | yes |
| **Caches** (Safe if known-safe, else "Other caches" Optional) | `~/Library/Caches/*` | `%LOCALAPPDATA%\Temp` contents older than 1 h (row `Temporary files`), `%LOCALAPPDATA%\Microsoft\Windows\INetCache`, `%LOCALAPPDATA%\Microsoft\Windows\Explorer\thumbcache_*.db` and `iconcache_*.db` (row `Thumbnail and icon cache`; files in use are skipped), `%LOCALAPPDATA%\D3DSCache`, browser caches per profile (`Default`, `Profile *`): Chrome `%LOCALAPPDATA%\Google\Chrome\User Data\<profile>\{Cache,Code Cache,GPUCache,Service Worker\CacheStorage}`, Edge `%LOCALAPPDATA%\Microsoft\Edge\User Data\...`, Brave `%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data\...`, Vivaldi `%LOCALAPPDATA%\Vivaldi\User Data\...`, Opera `%APPDATA%\Opera Software\Opera Stable\{Cache,Code Cache,GPUCache}`, Firefox `%LOCALAPPDATA%\Mozilla\Firefox\Profiles\*\cache2`; Electron/Chromium apps `%APPDATA%\<App>\{Cache,Code Cache,GPUCache,DawnCache,Cache_Data}` (Slack, Discord, Teams classic, VS Code, Obsidian, Signal ... one row per app = the app folder name); package-manager caches (the "plain named safe list" analog): npm `%LOCALAPPDATA%\npm-cache`, yarn `%LOCALAPPDATA%\Yarn\Cache`, pnpm `%LOCALAPPDATA%\pnpm-cache`, pip `%LOCALAPPDATA%\pip\Cache`, node-gyp `%LOCALAPPDATA%\node-gyp\Cache`, electron `%LOCALAPPDATA%\electron\Cache`, Cypress `%LOCALAPPDATA%\Cypress\Cache` (unchecked: re-download is large), puppeteer `%USERPROFILE%\.cache\puppeteer`, typescript `%LOCALAPPDATA%\Microsoft\TypeScript`, winget `%TEMP%\WinGet`, Windows Store `%LOCALAPPDATA%\Packages\Microsoft.WindowsStore_8wekyb3d8bbwe\LocalCache`. System (needs admin, unchecked, Optional > Other caches): Delivery Optimization `%ProgramData%\Microsoft\Windows\DeliveryOptimization\Cache` and `C:\Windows\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache`, Windows Update downloads `C:\Windows\SoftwareDistribution\Download` (stop `wuauserv`/`bits` first in the elevated helper, restart after), Prefetch `C:\Windows\Prefetch\*.pf` (slows next boot slightly: unchecked), `C:\Windows\Temp` older than 1 h (row `Windows temporary files`). Hidden (never listed): Trayloom's own data (`%APPDATA%\Trayloom`, `%LOCALAPPDATA%\Trayloom`), `Microsoft\Windows\Fonts`, `Microsoft\Windows\Themes`, `Microsoft\Windows\CloudStore`, `Microsoft\CLR_v4.0*` (JIT), `Packages\*` except the Store cache above, anything under `OneDrive`, `Spotify\Storage` (offline music), `ms-playwright` (listed unchecked), audio-plugin vendor caches `Avid`, `Native Instruments`, `FabFilter`, any directory name ending `.localized` | known-safe yes (browsers, Electron app caches, npm/yarn/pip/node-gyp/typescript, temp, thumbnail/icon, D3DS, INetCache); Microsoft system ones, Cypress, playwright, Prefetch, Windows Update, Delivery Optimization: no |
| **Logs** (Safe) | `~/Library/Logs/*` + DiagnosticReports | `%LOCALAPPDATA%\Microsoft\Windows\WER\{ReportArchive,ReportQueue,Temp}` (row `Windows error reports`), `%ProgramData%\Microsoft\Windows\WER\{ReportArchive,ReportQueue}` (admin), `%LOCALAPPDATA%\CrashDumps` (row `Crash dumps`), `C:\Windows\Minidump`, `C:\Windows\MEMORY.DMP`, `C:\Windows\LiveKernelReports\*.dmp` (admin), `C:\Windows\Logs\CBS\*.log` older than 7 days (admin), `C:\Windows\Logs\DISM`, `C:\Windows\Panther\*.log` (unchecked), per-app logs: `%LOCALAPPDATA%\<Vendor>\<App>\logs` and `%APPDATA%\<App>\logs` (Electron apps) rows named after the app, only `*.log` files older than 7 days. Event logs are not touched | yes (age rule above) |
| **Developer junk** (Safe) | Xcode DerivedData etc. | `%LOCALAPPDATA%\Microsoft\VisualStudio\*\ComponentModelCache`, `...\*\Roslyn`, `%LOCALAPPDATA%\Temp\VSLogs`, `%LOCALAPPDATA%\JetBrains\*\{caches,log}`, `%APPDATA%\Code\{CachedData,CachedExtensionVSIXs,logs}`, NuGet HTTP/v3 cache `%LOCALAPPDATA%\NuGet\{v3-cache,plugins-cache}`, Go build cache `%LOCALAPPDATA%\go-build`, Gradle `%USERPROFILE%\.gradle\caches` (unchecked), NuGet global packages `%USERPROFILE%\.nuget\packages` (unchecked), Maven `%USERPROFILE%\.m2\repository` (unchecked), Cargo `%USERPROFILE%\.cargo\registry\cache` (unchecked), Android `%USERPROFILE%\.android\cache`, Docker Desktop disk image never offered | yes for rebuild-on-demand build products, no for dependency stores |
| **Leftovers from uninstalled apps** (Optional) | Library roots by bundle id | Direct children of `%APPDATA%`, `%LOCALAPPDATA%`, `%LOCALAPPDATA%\Programs`, `%ProgramData%`, `%PROGRAMFILES%`, `%PROGRAMFILES(X86)%` whose normalised name (lower-case alphanumerics, bitness/version suffixes stripped) matches **no** living owner. Living-owner oracle (built once per scan/clean pass): every `DisplayName`, `Publisher`, `InstallLocation` leaf and `DisplayIcon` exe base-name from the three Uninstall hives (`HKLM`, `HKLM\WOW6432Node`, `HKCU`), every AppX/MSIX package name + publisher + `InstallLocation` (`PackageManager.FindPackagesForUser("")`), every Start-menu shortcut target, every running process image folder, every service `ImagePath` folder, plus the protected list (below). A folder is a leftover only when its normalised name is >= 4 chars, is shaped like an app/vendor folder, is not in the protected list and no oracle entry equals it, starts with it or contains it (>= 5 chars). Windows has no reverse-DNS bundle id, so there is no equivalent of the "name maps to an identifier" proof; every Windows leftover is therefore a name-based guess and stays "found by analysis" and **unchecked** (this matches `precheckLeftovers = false`). Protected (never offered): `Microsoft, Windows, WindowsApps, Packages, Common Files, Programs, Temp, Microsoft Edge, Microsoft OneDrive, Google (shared updater), Adobe (shared), Intel, NVIDIA, NVIDIA Corporation, AMD, Apple, Oracle, Java, Package Cache, regid.*, SystemApps, Trayloom` and every id in the shared-infrastructure analog (`Sentry`, `CrashPad`, `Squirrel`, `SquirrelTemp`, `WinSparkle`, `Crashlytics`) | no |
| **iPhone backups** (Optional) | MobileSync/Backup | `%APPDATA%\Apple Computer\MobileSync\Backup\*` (iTunes) and `%USERPROFILE%\Apple\MobileSync\Backup\*` (Apple Devices app); detail from `Info.plist` (XML plist: `Device Name`, `Last Backup Date` ISO-8601) | no |
| **Forgotten screenshots** (Optional) | Desktop / screencapture location | `%USERPROFILE%\Pictures\Screenshots` (Win+PrintScreen / Snipping Tool; read the real path from `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders` value `{B7BB6C06-...}`/Known Folder `FOLDERID_Screenshots`), the Snipping Tool folder `%USERPROFILE%\Videos\Screenshots` excluded (videos), plus Trayloom's own screenshot save folder. A file qualifies only if (a) it is a direct child, regular file, not a reparse point, (b) its name matches the default pattern `^Screenshot( \(\d+\)| \d{4}-\d{2}-\d{2} \d{6}( \(\d+\))?)\.(png|jpg|jpeg)$` or Trayloom's own default screenshot pattern, (c) `max(CreationTime, LastWriteTime, LastAccessTime) <= now - days` (`LastAccessTime` is only trustworthy if `fsutil behavior query disablelastaccess` is 0 or 2; otherwise ignore it). Windows has no capture-flag attribute, so the name pattern IS the proof; a renamed file is never offered | no |
| **Recycle Bin** (Optional) | ~/.Trash | `SHQueryRecycleBinW` (size/items), `SHEmptyRecycleBinW(NOCONFIRMATION\|NOPROGRESSUI\|NOSOUND)`; row only when items > 0 | no |

Elevation: every row whose root is outside the user profile is tagged `NeedsAdmin`. In a non-elevated process these rows are not listed (note card 7.1.4 explains) unless the user relaunched as administrator; in an elevated process the clean runs without another prompt. When the app is not elevated but the user ticked rows that need it (only possible if the row was found through an ACL that allowed reading), run them in one elevated helper batch as in the table at the top.

Windows `mayRemove` guard: not a drive root, not in {`%WINDIR%`, `%WINDIR%\System32`, `%PROGRAMFILES%`, `%PROGRAMFILES(X86)%`, `%ProgramData%`, `C:\Users`, `%USERPROFILE%`, `%APPDATA%`, `%LOCALAPPDATA%`, Documents, Desktop, Downloads, Pictures, Music, Videos, OneDrive roots, `%LOCALAPPDATA%\Packages`}, file identity (volume serial + 64-bit file index from `GetFileInformationByHandle`) unchanged since scan, the path and every ancestor up to the scan root is not a reparse point (`FILE_ATTRIBUTE_REPARSE_POINT`), `Path.GetFullPath(path)` is a descendant of an allowed root for its category, and at least 4 path segments after the drive.

**Windows cleaner safety amendments (audit fixes).**
* Temp rule: the `Temporary files` row is offered only when `%TEMP%` passes `FileOps.IsSafeTempRoot` (not a link, not critical, at least 3 segments, leaf `Temp`/`Tmp` or an all-digit session folder under `Temp`); otherwise it is skipped and `Cleaner: Temp folder skipped (not a Temp folder)` is logged. The winget cache uses the same safe temp.
* `Validate` always refuses a critical folder or an ancestor of one. The only paths allowed to be shallower than `mayRemove` permits are `%WINDIR%\Temp`, `%WINDIR%\Prefetch` and `%WINDIR%\Minidump`, and only for the contents-only kind.
* `Internet cache` lists the child folders of INetCache except `Content.Outlook`. `Crash dumps` is not pre-checked. Electron app caches are offered only for `%APPDATA%\<App>` folders holding `Local State` or `Preferences`.
* Orphaned startup items: a target is missing only when its drive is a fixed, ready drive letter and nothing exists there; removable, network, optical, not-ready and UNC targets are never orphans. Unattended (automatic) runs do not pre-check startup items. An automatic run is skipped while Trayloom runs elevated.
* Recycle: items go through `FileOps.TryRecycle`; an item the bin cannot keep is left in place, counted as failed and reported on the Done screen as `{0} items were too large for the Recycle Bin, so they were left in place.` The recycle sentence shows only when something was really recycled.
* Registry backup: each clean writes to a new folder `%APPDATA%\Trayloom\cleanerackups\<yyyyMMdd-HHmmss-fff>`, exports every distinct (hive, view, key) once as `NNN-<key>.reg` before deleting any value, and leaves the values of a key whose export failed. Done text: `Registry entries were saved to {0} first.`

### 7.1.8 Gap vs Trayloom (`Trayloom.Features.Tools/Cleaner/CleanerFeature.cs`, 534 lines)

Has: a separate `CleanerWindow` (640x640) with a flat checkbox list of fixed categories (user temp, `Windows\Temp`, thumbnail/icon cache, crash dumps, Chrome/Edge/Brave/Firefox caches, Recycle Bin), per-category size, scan, delete with a modal confirmation, optional hotkey, `IsSafeRoot` + prefix guard, 1-hour minimum age for temp.
Lacks / differs (priority order):
1. P0 No review step per path: Vorssaint lists every path with size and a per-item checkbox under collapsible groups; Trayloom only has category checkboxes. Rebuild as `CleanerView` with phases idle/scanning/results/cleaning/done, group + item rows, "Safe cleanup" / "Optional, review first" sections (7.1.2).
2. P0 UI is not hosted in the Settings page nor the quick panel (compact variant); there is no idle intro, busy animation, done page, Cancel while scanning.
3. P0 Pre-check policy and sections (safe vs optional) are absent; Recycle Bin is pre-unchecked only; developer junk, logs, leftovers, orphaned startup items, device backups, forgotten screenshots categories do not exist.
4. P0 Safety model: file-identity binding, reparse-point checks, critical-root denylist, re-validation right before deletion, Recycle Bin for the user-meaningful categories are missing (it deletes with `File.Delete` and a MessageBox confirm; Vorssaint has no confirm dialog because the review list is the confirmation).
5. P1 Scheduler (`off/daily/weekly`, time/weekday pickers, notify, last/next lines, missed-run catch-up in 120 s, 600 s retry while the user is in a manual session, wake/clock-change handling) is missing entirely, as are the settings keys.
6. P1 Forgotten-screenshots age setting (Off/7/14/30/60/90 days) and card are missing.
7. P1 No "Run as administrator" flow for system caches (Windows Update, Delivery Optimization, Prefetch, WER ProgramData); `Windows\Temp` is attempted without elevation.
8. P2 Strings are Trayloom-specific ("Free up disk space", "Clean selected"); replace with the strings in 7.1.2/7.1.3 (`Cleaner`, `Scan`, `Clean <size>`, ...).
9. P2 Hub description (`Clear caches and junk files`), panel entry (`panelUtilityCleaner`, caption `App leftovers, caches and logs`) and settings page layout (card hosting) differ.
10. P2 No `include` persistence of last run (`cleanerLastAuto*`).

---

## 7.2 Uninstaller (+ leftovers)

### 7.2.1 Feature identity

| Item | Value |
|---|---|
| Feature id | `uninstaller` |
| Hub tile | title `Uninstaller` (`uninstallerName`), description `Remove apps and their leftovers` (`descUninstaller`), group Tools, SF `trash` -> U+E74D |
| Enable caption (hub detail) | `Removes an app together with the caches, preferences, logs and leftovers it leaves behind.` (`uninstallerEnableCaption`) |
| Three-step explainer | `Drag an app onto Settings, or pick one from the list.` / `Review the files found and how much space they take.` / `Move what you want to the Trash. Nothing is deleted permanently.` ([WIN] step 1 `Drag a program or its shortcut onto Settings, or pick one from the list.`; step 3 `Remove what you want. Files go to the Recycle Bin; nothing is deleted permanently.`) (`uninstallerStep1/2/3`) |
| Menu entry | `Uninstall an app…` (`uninstallerMenuItem`) |
| Panel | `panelUtilityUninstaller` (default true); panel header `Label("Uninstaller", trash)` 12 semibold |
| Command Bar | toggle `Show in Command Bar` (`uninstallerCommandBarToggle`) with caption `Choose and uninstall apps in the Command Bar.` (`uninstallerCommandBarCaption`), key `uninstallerCommandBarEnabled` bool default **false**; command titles `Uninstall Application` (`uninstallerCommandBarBrowseTitle`) and `Uninstall app selected in Finder` ([WIN] `Uninstall program selected in File Explorer`, `uninstallerCommandBarFinderTitle`); unavailable selection message `Select an app that can be removed in Finder, or choose another from the list.` ([WIN] `...removed in File Explorer...`, `uninstallerSelectionUnavailable`) |
| Permissions | macOS: Full Disk Access + Finder automation. Windows: none; elevation only for HKLM / Program Files / ProgramData items |

### 7.2.2 Layout (Settings page, `UninstallerView`; phases `empty, scanning, results, removing, done(freed, failed)`)

**Empty state** (centred column, spacing 20, padding 28, spacers top/bottom):
1. Drop zone: `RoundedRectangle(cornerRadius 18, continuous)`, 360 x 200, dashed stroke 2 pt, dash [9, 7]; idle stroke = secondary @ 35 %; drag-targeted: accent stroke + accent fill @ 6 %, animation `easeOut 0.15 s`. Content VStack spacing 12: `trash.square` glyph 44 pt light (U+E74D inside a 44-px rounded square outline; secondary, accent while targeted), `Drag an app here` (`uninstallerDropTitle`, 16 semibold) -> [WIN] `Drag a program here`, `or choose one to scan` (`uninstallerDropSubtitle`, 12 secondary).
2. If the last selection was refused: `UninstallRefusalNote` (width 360): HStack spacing 8, orange `exclamationmark.triangle.fill` (U+E7BA) + secondary caption (12 pt `.caption`) with the refusal message (7.2.4).
3. Button `Choose app…` (`uninstallerChoose`, large) -> opens the picker sheet (7.2.3). [WIN] `Choose program…`.
4. Caption `Nothing is removed without your confirmation.` (`uninstallerEmptyNote`, caption tertiary).
5. Mini form (grouped, width 360): toggle `Show in Command Bar` + caption 12 secondary (see above).
6. Administrator note (replacement of the Full Disk Access note, same card as 7.1.4) when not elevated: [WIN] text `Run as administrator for a more thorough scan.`, hint `Some leftovers live in ProgramData, Program Files and HKEY_LOCAL_MACHINE and can only be removed by an administrator.`
7. Whole page is a drop destination for files (`URL`): prefers the first `.app` ([WIN] `.exe` / `.lnk` / `.msi`), else the first URL. Result of `select()`: accepted -> scanning; refused -> springs back and the note shows the reason.
8. `onAppear/onDisappear` clear any stored refusal.

**Busy** (`scanning` / `removing`): spacing 14: large `ProgressView` (ring, 32 px), message (`Scanning files…` / `Moving to the Trash…` [WIN] `Removing…`; secondary), the target row (icon 18 + name `.callout`) when a target is set, and during scanning a large `Cancel` button -> `reset()`.

**Results**: VStack spacing 0: `targetHeader`, Divider, inset `List` with one `Section` per category in this order and with these titles, only non-empty ones (`uninstallerCat*`): `Application`, `Support`, `Caches`, `Preferences`, `Containers`, `Logs`, `Saved state`, `Other`; items inside a section sorted by size descending (the whole result is sorted by `(category rank, -size)`); Divider; Homebrew/winget status strip (7.2.5); footer.
- Target header (padding 16): app icon 44 x 44, VStack spacing 2 {name 16 semibold; bundle id (else path) caption secondary, 1 line, middle truncation}; Spacer; VStack trailing {total size 15 semibold **rounded design** (Segoe UI Variable Display), caption2 secondary `found` (`uninstallerFoundTitle`)}; `xmark.circle.fill` 16 plain secondary -> `reset()` (disabled while a package-manager removal runs).
- Row (spacing 10, vertical padding 2): checkbox; file icon 22 x 22 (shell icon of the path: `SHGetFileInfo`; for registry rows a generic "registry" glyph); VStack spacing 1 {name 12.5 (1 line, middle truncation); HStack spacing 5, font 10.5 { if confidence == related: orange label `Optional, review first` with `questionmark.circle` (U+E897 help, verify) ; parent path tertiary 1 line **head** truncation with home -> `~` } }; Spacer; `folder` button (plain, U+E838) with tooltip + accessibility `Reveal in Finder` ([WIN] `Reveal in File Explorer`; for registry rows `Open in Registry Editor`); size 11.5 secondary tabular.
- Footer (padding 16): VStack spacing 1 {`"<n> of <m> selected"` 12 medium (`uninstallerSelectedFormat`), selected size caption secondary}; Spacer; `Cancel` (disabled during package removal); prominent **red-tinted** button titled `Move to Trash` (`uninstallerRemove`) -> [WIN] `Uninstall` when the Application row is checked, else `Move to Recycle Bin`. Disabled when `!canRemoveSelected` (not in results, removing, package check failed, or nothing selected).
**Done**: spacing 16, padding 28: result glyph 54 pt: `checkmark.circle.fill` green when nothing failed else `exclamationmark.triangle.fill` orange; `Done!` 20 bold; `"<size> removed"` 13 secondary; if failures: failure note (width 360): orange caption `Some items couldn’t be moved to the Trash.` ([WIN] `Some items couldn’t be removed.`), up to 4 failed names (caption2 secondary, middle truncation), then `and %d more` (`uninstallerFailedMoreFormat`, tertiary) when more than 4, and when the failed paths are in protected locations (Windows: `%ProgramData%`, `%ProgramFiles%`, `HKLM`) the administrator note card with `Sandboxed app data can only be moved with Full Disk Access. The administrator password does not stand in for it.` replaced by [WIN] `These items are in protected locations. Relaunch Trayloom as administrator to remove them.`; prominent default button `Uninstall another` (`uninstallerAnother`), top padding 6, -> `reset()`.
Panel variant (`PanelUninstallerView`, compact): same states with 20-px target icons, 10-px secondary texts, category headers shown **uppercased** (`label.uppercased()`), `Label(removeButtonTitle, trash)` button, same alerts.

Demo reference `docs/assets/readme/uninstall-demo.gif` (800x585): Settings window with sidebar (General, Energy, Monitor / Features: Mouse, Switcher, Cut & paste, Quit on close, **Uninstaller** (selected), Shelf, Advanced, About, Support). Frame 2: dashed rounded rectangle (accent blue while dragging an app icon over it), centred blue trash glyph, `Drag an app here`, `or choose one to scan`, below it a rounded `Choose app…` button and the tertiary `Nothing is removed without your confirmation.`. Frame 3: header with 44-px AnyDesk icon, `AnyDesk`, `com.philandro.anydesk`, right `65.4 MB` + `found`, close x; section `Application` with row `AnyDesk.app` `/Applications` `65.4 MB`, section `Preferences` with `com.philandro.anydesk.plist` `~/Library/Preferences` `4 KB`; footer `2 of 2 selected` / `65.4 MB`, buttons `Cancel`, `Move to Trash`.

### 7.2.3 App picker sheet (`AppPickerView`, sheet 520 x 560, padding 18; compact: padding 10, no fixed size)

Top to bottom (spacing 12, compact 9): header HStack {title `Choose app` (`uninstallerPickerTitle`, 16 semibold; compact 12), Spacer, optional `Choose app…` folder button (only when the host passes `canBrowseApplications`; not in the uninstaller), `Cancel`}; search field (rounded border, placeholder `Search apps` (`uninstallerPickerSearch`), 13 pt; compact 11); list. Filter = case-insensitive contains on name, bundle id and identity. List states: loading (`ProgressView` + `Scanning files…`, 360 high / compact 150), empty (glyph `app.dashed` 34 pt light secondary, `No apps found` (`uninstallerPickerEmpty`) 13 secondary, then the tertiary 11 note `Apps that are part of macOS or made by Apple aren’t listed.` (`uninstallerPickerHiddenNote`) -> [WIN] `Programs that are part of Windows or made by Microsoft aren’t listed.`), else a `LazyVStack` (spacing 2) of rows: icon 28 (compact 20), name 13 medium (compact 11), second line 11 secondary 1 line = location (for path identities, head truncation) else bundle id else path; row padding h 8 v 7, radius 6, hit-test over the whole row. Selecting calls `select(appURL)` and closes the sheet. The app list is loaded off the main thread (`UninstallerSupport.offeredApplications()` = installed apps that `selection(for:)` would accept, i.e. refused apps are never offered, sorted by localized name).

### 7.2.4 Selection rules and refusal messages

`evaluate(url) -> Selection | Refusal`: refuse when (in order) the URL is not a file URL (`notAnApp`); a parent folder is itself an `.app` (`insideAnotherApp`); bundle unreadable (`unidentified` if the extension is `.app` else `notAnApp`); system application (`/System/`, `/Library/Apple/` after resolving symlinks) (`systemApp`); no verifiable reverse-DNS bundle id (`unidentified`/`notAnApp`); id is protected -> `thisApp` when it is Vorssaint's own, else `sharedIdentifier`; path != resolved path or is a symlink (`link`); it is the running app (`thisApp`); no stable file identity / `Info.plist` unreadable or not a safe path (`unidentified`/`notAnApp`). Messages (enUS; `%@` = app name):

| Refusal | Text | [WIN] |
|---|---|---|
| notAnApp | `Only apps can be uninstalled.` | `Only programs can be uninstalled.` |
| systemApp | `%@ can’t be uninstalled because it’s part of macOS.` | `...part of Windows.` |
| sharedIdentifier | `%@ can’t be uninstalled here because it shares files with macOS or other apps.` | `...shares files with Windows or other apps.` |
| insideAnotherApp | `%@ can’t be uninstalled on its own because it’s part of another app.` | same (e.g. a registry entry with `ParentKeyName`/`ParentDisplayName`, or an exe inside another program's install folder that has its own uninstall entry) |
| thisApp | `Vorssaint can’t uninstall itself here. Use Settings › %@ instead.` (`%@` = Advanced tab name) | `Trayloom can’t uninstall itself here. Use Settings › Advanced instead.` |
| link | `%@ can’t be uninstalled because it’s a link to an app stored elsewhere.` | `...because it’s a link...` (symlink/junction/reparse point; a plain `.lnk` shortcut is **resolved** to its target instead of refused) |
| unidentified | `%@ can’t be uninstalled because it can’t be identified as an app.` | `...identified as a program.` (no Uninstall entry / AppX package found for the file) |

### 7.2.5 Leftover scan (Swift algorithm) and Windows equivalents

**Identity** of the selected app: `bundleIDs` (the main id + ids of owned embedded code: `.appex`, `.xpc`, login-item `.app`s under `Contents/Library/LoginItems`, at most 256 inspected, restricted to **exclusive** ids = no other installed/running app carries the same id; only claimed when the app lives in `/Applications` or `~/Applications`), `nameTokens` (display names: localized, file name, `CFBundleName`, `CFBundleDisplayName`, executable name, each stripped of `.app`, normalised = lower-case letters/digits only, plus the same with a trailing version/channel word removed (`\s+(\d+(\.\d+)*|nightly|beta|alpha|dev|canary|preview|insider|stable|release|rc|lts|developer edition|technology preview)$`); tokens < 3 chars or in the ignored list `app mac macos osx helper agent daemon service desktop client launcher plugin extension web free pro lite plus ui xpc login updater installer renderer gpu worker crashpad broker utility alert network audio server shared core common framework bundle process handler tool runtime electron java python node mono wine` are dropped; the last component of the primary id is added when >= 4 chars and not ignored), `teamIDs` (code-signing team, valid signature required), `groupIDs` (exclusive app-group ids from entitlements).

**Match tiers** (`leftoverMatch(name)`; no numeric score): rejects names starting with `.`, containing `..` or `/`. Strip one known suffix (`.plist .savedState .binarycookies .prefPane .qlgenerator .mdimporter .service .appex .plugin .webplugin .saver .colorPicker .wdgt .bundle .sfl .sfl2 .sfl3 .bom .action .workflow .app .framework .component .vst .vst3 .clap .dpm .aaxplugin .dictionary .safariextz .mailbundle`).
- **exact** (starts checked): equals a signed group id; equals an owned bundle id (case-insensitive, with or without suffix); `ByHost` preference `<id>.<UUID>.plist`; `<TEAMID>.<id>`; container metadata identifier that matches.
- **related** (starts unchecked, row shows the orange `Optional, review first` label): name starts with `<owned id>.`; normalised name equals a name token (>= 3 chars); crash-report folders: prefix before `_`/`-` equals a token, or starts with an owned id (exact); name starts with a token of >= 5 chars **unless** it looks like a reverse-DNS id or a crash dump (`_YYYY-MM-DD`/`-YYYY-MM-DD`); equals or starts with `<TEAMID>.`.
- Technical folders (`Containers`, `ByHost`, `SyncedPreferences`, `Launch*`, `PrivilegedHelperTools`, `Extensions`, `StartupItems`, `Application Scripts`, `Group Containers` (signed group only), `~/.config`, `~/.cache`, `~/.local/share`, receipts, Darwin temp/cache) accept only ids/groups (`technicalIdentity`), never display names.
- Vendor nesting: unmatched directories are opened `extraChildDepth` levels (Application Support 2, Caches 1, Logs 1, Audio/Plug-Ins 1, `/Users/Shared/Library/Application Support` 2) so `Vendor/App` is found; a *related* vendor folder with matching children yields the children, never the shared vendor folder.
- Spotlight supplement: query `kMDItemFSName == "<id>*"cd || "<token>*"cd` (max 16 ids + tokens up to 24 clauses; tokens >= 4 chars), scope = `~/Library`, `/Library`, `/Users/Shared/Library`, max 200 results, at most 80 added, result path must not contain `/Desktop/ /Documents/ /Downloads/ /Movies/ /Music/ /Pictures/ /Public/` and must be at most 2 components below its root; always `include = false`; category from the path text.
- De-duplication: shortest paths first; a *related* candidate is dropped if an *exact* one lives beneath it; same path keeps the exact candidate and `include = old || new`; anything nested inside an already accepted path is dropped.
- Final filter: nothing outside the scan roots (or the app bundle itself) reaches the list; each item must pass `removalPathIsSafe(item, within: root)` (item and every ancestor to the root exist and are not symlinks); sizes are allocated-size sums (symlinks counted as their own size here, not followed).

Scan search folders (macOS) for reference: `~/Library` and `/Library`: Application Support (d2), Application Support/CrashReporter (logs, crash), Caches (d1), Preferences, Logs (d1), Logs/DiagnosticReports (crash), LaunchAgents (ids only), PreferencePanes, Internet Plug-Ins, Services, QuickLook, Spotlight, Input Methods, Screen Savers, ColorPickers, Audio/Plug-Ins (d1), PrivilegedHelperTools (ids only), Frameworks, Automator, CoreImage, Dictionaries, Components; `~/Library` only: Preferences/ByHost, Saved Application State, Autosave Information, SyncedPreferences, HTTPStorages, WebKit, WebKit/com.apple.WebKit.WebContent, Containers (metadata), Group Containers (signed group), Application Scripts, Application Support/FileProvider, Caches/com.apple.nsurlsessiond/Downloads, Cookies, Widgets, Address Book Plug-Ins, Contextual Menu Items, Safari/Extensions, Accessibility, Mail/Bundles, Workflows, recent-documents shared file list; `/Library` only: LaunchDaemons, Extensions, StartupItems; plus `~/.config`, `~/.cache`, `~/.local/share`, `/private/var/db/receipts`, `/Users/Shared/Library/Application Support`.

**Windows search folders** (`[WIN]`; same machinery: direct children + `extraChildDepth`, listings read once, symlinks/reparse points skipped, hidden dot-names skipped):

| Category (section) | Root | Depth / rule |
|---|---|---|
| Application | the install folder (`InstallLocation` or folder of `DisplayIcon`/uninstall exe, or the AppX `InstallLocation`) | the root item itself, **exact**, checked, size = folder size. Must be under `%PROGRAMFILES%`, `%PROGRAMFILES(X86)%`, `%LOCALAPPDATA%\Programs` or an `InstallLocation` that is not a drive root / Windows folder / user profile root |
| Support | `%APPDATA%`, `%LOCALAPPDATA%`, `%ProgramData%`, `%PUBLIC%\Documents` no, `%USERPROFILE%\.config`, `.cache`, `.local\share` (ids only) | depth 2 for `%APPDATA%`/`%LOCALAPPDATA%`/`%ProgramData%` (Vendor\App), names match against Identity |
| Caches | `%LOCALAPPDATA%\Temp`, `%TEMP%`, `%LOCALAPPDATA%\<App>\Cache*`, `%LOCALAPPDATA%\Microsoft\Windows\INetCache` (names false), `%LOCALAPPDATA%\Packages\<PFN>\{LocalCache,TempState}` | depth 1 |
| Preferences | registry: `HKCU\Software\<Publisher>\<App>`, `HKLM\SOFTWARE\<Publisher>\<App>`, `HKLM\SOFTWARE\WOW6432Node\<Publisher>\<App>`, `HKCU\Software\Classes` ProgIDs that point into the install folder; config files `%APPDATA%\<App>\*.ini` are Support | key name equals / starts with identity token; the publisher key itself is **never** offered unless it has no remaining subkeys after the app key is removed; removal = `.reg` backup then `RegDeleteTree` (HKLM needs elevation) |
| Containers | `%LOCALAPPDATA%\Packages\<PackageFamilyName>` (MSIX/UWP data), `%LOCALAPPDATA%\Microsoft\WindowsApps\<PFN>` | exact only (family name equals the removed package's PFN), ids only |
| Logs | `%LOCALAPPDATA%\<App>\logs`, `%APPDATA%\<App>\logs`, `%LOCALAPPDATA%\CrashDumps\<exe>.<pid>.dmp`, `%LOCALAPPDATA%\Microsoft\Windows\WER\ReportArchive\*<exe>*` | crash-reporter rules: prefix before `_`/`.` equals executable base name |
| Saved state | Start-menu entries and pins: `%APPDATA%\Microsoft\Windows\Start Menu\Programs\<Vendor or App>`, `%ProgramData%\Microsoft\Windows\Start Menu\Programs\...`, `%APPDATA%\Microsoft\Internet Explorer\Quick Launch\User Pinned\{TaskBar,StartMenu}\*.lnk`, Jump lists `%APPDATA%\Microsoft\Windows\Recent\{Automatic,Custom}Destinations\<AppID>.*-ms` (AppID = hash, only when known: skip otherwise) | shortcut `.lnk` whose target is inside the install folder = exact |
| Other | Desktop shortcuts (`%USERPROFILE%\Desktop`, `%PUBLIC%\Desktop`) targeting the install folder; Task Scheduler tasks whose action path is inside the install folder; services whose `ImagePath` is inside the install folder; `Run` / `RunOnce` values (HKCU/HKLM/Wow6432Node) pointing into the install folder; firewall rules (`HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules` entries with `App=<install path>`); shell extension / context-menu CLSIDs whose `InprocServer32` points into the install folder; `C:\Windows\Prefetch\<EXE>-*.pf` (admin, names false); the app's own Uninstall key (`HKLM|HKCU\...\Uninstall\<key>`) once the uninstaller has run | exact when the path/target is inside the install folder or the app's recorded id; **services and tasks are stopped/deleted only in the elevated helper** |

**Identity on Windows**: `ids` = {registry Uninstall key name, MSI ProductCode/UpgradeCode GUID (braces and bare), AppX `PackageFamilyName` and `PackageFullName`, `AppUserModelID`, executable base names found in the install folder root}; `nameTokens` = normalised `DisplayName` (+ version-stripped variant), publisher-less install-folder leaf, main exe base name; `teamIDs` analog = normalised `Publisher` (a publisher token yields **related** matches only, never exact, and the publisher folder itself is never selectable when other installed programs share that publisher). Same ignore list as above plus `microsoft windows system program programs common files data temp cache`.

**Safety (`removalIsStillSafe`, run again right before every removal)**: path must be in `allowedRemovalPaths` (the set captured at scan time), its file identity (volume serial + file index; for registry rows the key's last-write time + path) equals the recorded identity, the path and all ancestors up to the scan root are not reparse points, and non-Application items must still be exclusively owned (`exclusiveBundleIDs` analog: no other installed program/AppX shares the id, the install folder, or the product code). Failed ownership = the item is counted as failed (not removed).

### 7.2.6 Removal behaviour

1. `removeSelected()` is allowed only in phase `results`, not removing, no failed package check, at least one item included. Phase -> `removing`.
2. Quit the app (and processes embedded in its folder): macOS `app.terminate()` for each running app whose bundle equals/is nested in the target. Windows: for each process whose `MainModule.FileName` lies under the install folder: `CloseMainWindow()`, wait up to 3 s, then `Kill()` only processes whose image path is inside the install folder (never by name alone).
3. After 0.3 s on a background thread: if the target is still the original (identity matches), or a package manager already removed it, shared-data claims are computed; for every included item other than `Application` the ownership test (above) must pass else it is a failure; then `removalIsStillSafe`; then move to the Trash. If the move throws: absent -> counted freed; present -> "stubborn".
4. Stubborn items (root-owned, `/Library`, root-owned apps) are retried in one Finder batch (admin prompt); survivors are failures. Windows: one elevated helper batch (`--elevated-clean`).
5. `phase = done(freed, failed)`; the command-bar state for the removed app (recent/frecency entries) is cleaned asynchronously if the app is confirmed absent and no other copy with the same id remains.

**Windows application removal ("Application" row)**: the Application row represents running the program's own uninstaller (the folder is a leftover only if it still exists afterwards). Order: (1) AppX/MSIX -> `Remove-AppxPackage -Package <PackageFullName>` (or `winget uninstall --id <id> --source msstore --silent`); (2) MSI (`UninstallString` starts with `MsiExec.exe /I{GUID}` or `WindowsInstaller=1`) -> `msiexec.exe /x {GUID} /passive /norestart` (use `/qn` when the user chose silent); (3) `QuietUninstallString` if present and the user ticked "silent" (default off, see gap list), else `UninstallString` (parse quoted exe + args with `CommandLineToArgvW`); (4) if the package is managed by winget (see 7.4 correlation) the button is `Uninstall` and goes through `winget uninstall --id <id> --exact --silent --accept-source-agreements --disable-interactivity`. Wait for exit (handle self-deleting uninstallers that spawn a child: wait for the original PID and then poll for the Uninstall key / install folder for up to 30 s); success when the Uninstall key is gone. Then run the leftover removals for the remaining checked rows. Exit codes 3010 / 1641 = success + reboot required (show an orange caption `A restart is required to finish.` [WIN, new]).

**Winget/Homebrew integration inside the uninstaller (Vorssaint)**: while scanning, `packageManagingApplication(at:)` asks Homebrew whether a cask owns this exact app path (`brew info --json=v2 --installed`, match `artifacts[].app` path or unique file name). Result `.success(package)` -> `homebrewPackage`; `.failure` -> `homebrewCheckFailed`. Then: if the Application row is checked and a package exists the footer button reads `Uninstall` (`homebrewUninstall`), a status strip above the footer shows `Label("%@ will also be removed from Homebrew.", shippingbox)` (`uninstallerHomebrewPackageFormat`; caption secondary, padding h16 v10) and the click opens the confirmation alert `Uninstall with Homebrew?` / `Homebrew will uninstall %@. Configuration files may remain on the system.` (`homebrewConfirmUninstallTitle/BodyFormat`; buttons `Cancel` and destructive `Uninstall`). On confirm the guard `Set(selectedIDs)` must still equal what the alert showed else HUD `This confirmation is no longer valid. Review the current items and confirm again.` (`uninstallerConfirmationExpired`). After `brew uninstall` succeeds the app row is unchecked and the remaining items are trashed; if the bundle still exists the app row is re-checked; if the app vanished unconfirmed the done state shows it failed. If the check failed: orange note `Could not verify whether Homebrew manages this app. Refresh to try again, or uninstall it from Homebrew.` + small `Refresh` button (`uninstallerHomebrewCheckFailed`, `homebrewRefresh`) and **Remove stays disabled**. While the package operation runs the footer shows the shared operation status view (7.4.5). [WIN] replace Homebrew by `winget` in every string above (`%@ will also be removed from winget.`, `Uninstall with winget?`, `winget will uninstall %@. Configuration files may remain on the system.`, `Could not verify whether winget manages this app. Refresh to try again, or uninstall it from winget.`).

### 7.2.7 Windows mapping

- Enumerate programs (picker + drop resolution): union of `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall`, `HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall`, `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall` (open with `RegistryView.Registry64/32` explicitly), plus AppX packages for the current user (`Windows.Management.Deployment.PackageManager.FindPackagesForUser("")`; skip frameworks (`IsFramework`), resource packages, `SignatureKind == System`, and `NonRemovable`) - or `Get-AppxPackage` if WinRT projection is unavailable. Skip entries: no `DisplayName`, `SystemComponent == 1`, `ParentKeyName`/`ParentDisplayName` present, `ReleaseType` in {Update, Hotfix, Security Update, Service Pack}, name starts with `KB`, and the system/shared list (Microsoft Visual C++ Redistributable, .NET runtimes/SDKs, WebView2 Runtime, Windows SDK components, drivers `Intel(R)/NVIDIA/AMD` runtime pieces flagged `SystemComponent`, Microsoft Edge, Microsoft Update Health Tools). Key fields: `DisplayName`, `DisplayVersion`, `Publisher`, `InstallLocation`, `InstallDate`, `EstimatedSize` (KB), `DisplayIcon`, `UninstallString`, `QuietUninstallString`, `ModifyPath`, `WindowsInstaller`, `NoRemove`, `NoRepair`. De-duplicate on `(DisplayName, DisplayVersion, Publisher)`. Honour `NoRemove == 1` as `systemApp` refusal.
- Drop resolution: `.lnk` -> `IShellLink` target (+ arguments ignored); `.exe` -> find the Uninstall entry whose `InstallLocation` is a prefix of the exe path or whose `DisplayIcon` exe equals it, or the AppX package whose `InstallLocation` is a prefix; `.msi` -> `MsiGetProductInfo`/`MsiEnumRelatedProducts` by Product code from the table; a folder -> the entry whose `InstallLocation` equals it; otherwise `notAnApp`/`unidentified`.
- Icon: `DisplayIcon` (strip `,index`), else the exe from `InstallLocation`, else the AppX logo; `SHGetFileInfo(SHGFI_ICON|SHGFI_LARGEICON)` or `ExtractIconEx`.
- Folder sizes: iterate with `EnumerationOptions { RecurseSubdirectories, IgnoreInaccessible, AttributesToSkip = ReparsePoint }`, sum `FileInfo.Length` (allocated size optional via `GetCompressedFileSizeW`); keep it cancellable.
- Registry removal: export with `RegSaveKeyEx`/`reg.exe export "<key>" "<file>.reg" /y` into `%APPDATA%\Trayloom\uninstaller\backups\<app>-<yyyyMMdd-HHmmss>.reg` before delete; show `Open backup folder` in the done state when registry rows were removed.
- Hotkey/menu: `Uninstall an app…` is a FeatureAction `open`; the Command Bar commands (above) call `select(url)` directly.

### 7.2.8 Gap vs Trayloom (`Uninstaller/UninstallerFeature.cs`, 741 lines)

Has: registry-based program list (HKLM/HKLM-WOW/HKCU, both views) with filtering of system components/updates, `QuietUninstallString` support, a grid window (Name/Publisher/Version/Size), "Uninstall", "Quiet uninstall", "Find leftovers", "Open install folder", "Refresh"; `LeftoverFinder` that matches **only direct children** (plus one `Publisher\App` level) of `%APPDATA%`, `%LOCALAPPDATA%`, `%LOCALAPPDATA%\Programs`, `%ProgramData%`, Program Files with a normalised-name match (`n == name` or prefix/contains for >= 5 chars) and a shared-folder denylist; a `LeftoverWindow` with nothing checked and "Move selected to Recycle Bin" through `IsAllowed`.
Lacks / differs (priority):
1. P0 Flow: Vorssaint is *scan first* (drop/pick -> scan -> categorised review list with sizes -> remove) and removes the app and leftovers in one action; Trayloom is a program grid with separate buttons and a second window for leftovers. Rebuild the empty/drop state, busy, results (categories `Application/Support/Caches/Preferences/Containers/Logs/Saved state/Other`), done pages and picker sheet as specified.
2. P0 Drag-and-drop target, refusal notes (7 reasons) and the refusal-springs-back behaviour are missing.
3. P0 Leftover scan coverage: no registry keys (`HKCU/HKLM\Software\<Vendor>\<App>`, Run keys, firewall rules, shell extensions), no Start-menu/desktop shortcuts, scheduled tasks, services, AppX `Packages\<PFN>`, Prefetch, WER/CrashDumps, `%TEMP%`; no exact/related tiers (everything is one unchecked tier), no "Optional, review first" label, no ownership/exclusivity checks, no identity re-validation before removal, no reparse-point protection.
4. P1 Exact matches should start **checked** (Vorssaint pre-checks `exact`), related unchecked; Trayloom leaves everything unchecked.
5. P1 No AppX/MSIX packages, no winget correlation (`Uninstall with winget?` flow), no reboot-required handling, no process termination before removal, no wait-for-self-deleting uninstaller polling.
6. P1 No registry `.reg` backups; items removed outside the Recycle Bin must be recoverable.
7. P2 Command Bar integration (`uninstallerCommandBarEnabled`), panel variant, hub strings, done-state symbol logic (`checkmark.circle.fill` vs `exclamationmark.triangle.fill`), failed-names list (4 + `and %d more`) and administrator-needed note are missing.
8. P2 Keep "Quiet uninstall" as an advanced option (secondary action in the footer menu or a setting `uninstallerSilent`, default off) - it is a Windows-only addition that is compatible with the replica.

---

## 7.3 App updates

### 7.3.1 Feature identity

| Item | Value |
|---|---|
| Feature id | `app-updates` (Trayloom) / `appUpdates` (Vorssaint) |
| Hub tile | title `App updates` (`pageTitle`), description `Find and install updates for the apps you have` (`hubDescription`), group Tools, SF `arrow.down.app` -> U+E896 (Download) or U+E118 "Update" (verify) |
| Page caption (top of Settings page) | `Looks for newer versions of the apps on this Mac and helps you finish each update from its original source.` -> [WIN] `Looks for newer versions of the apps on this PC and helps you finish each update from its original source.` (`caption`) |
| Panel caption (panel row) | `See which apps have a newer version` (`panelCaption`); panel toggle `Show in panel` (`showInPanel`, key `panelUtilityAppUpdates`, default true) |
| Permissions | macOS: Notifications, App Management. Windows: notifications only |

### 7.3.2 Settings page layout (`AppUpdatesSettings`, grouped form)

1. **Section `App updates`** (`pageTitle`): caption (`.caption` secondary) = page caption; then the **update list** (`AppUpdatesListView`, non-compact), vertical padding 2.
2. **Section `Check in the background`** (`frequencyLabel`): segmented picker `Off` / `Every day` / `Every week` (`frequencyOff/Daily/Weekly`) bound to `appUpdatesCheckFrequency`; if a next check exists: caption `Next check %@` (`nextCheckFormat`, short date + short time); toggle `Tell me when an app has an update` (`notifyToggle`, `appUpdatesNotify`, disabled while frequency is Off; turning it on requests notification permission). Changing frequency calls `syncWithPreferences()` and requests permission when `frequency != off && notify`.
3. **Section `Sources`** (`sourcesTitle`): toggle `Include Homebrew apps` (`includeHomebrewToggle`, `appUpdatesIncludeHomebrewApps`) -> [WIN] `Include winget apps`; toggle `Include apps from the App Store` (`includeStoreToggle`, `appUpdatesIncludeAppStore`) -> [WIN] `Include apps from the Microsoft Store` + caption `Checks store versions using this Mac’s region. Apple installs these updates.` -> [WIN] `Checks store versions using this PC’s region. Microsoft installs these updates.` (`includeStoreCaption`); toggle `Include other installed apps` (`includeOnlineToggle`, `appUpdatesIncludeOnlineCatalog`) + caption `Checks directly with app developers when supported, then uses a public catalog. The app’s own updater installs the update.` (`includeOnlineCaption`). A toggle is disabled when it is on and it is the **only** enabled source (at least one source stays on). Every toggle change calls `sourceSelectionDidChange()` (re-scan immediately, or after the running scan).
4. **Section (no title)**: toggle `Show in panel` (`showInPanel`).
`onAppear` -> `checkIfNeeded()`.

### 7.3.3 List view (`AppUpdatesListView`; the reference screenshot)

Reference `Resources/Images/highlights-appupdates.jpg` (about 1000 x 740 px, 2x): top row - left `Last checked now` (secondary grey, 12 pt), right a bordered rounded `Check now` button with a circular-arrow icon (↻, `arrow.clockwise`, U+E72C) and medium-weight label; below it a left-aligned link row `Select all` (rendered pale blue = disabled because every row is already ticked) and `Clear` (blue, enabled); then six rounded rows (radius 8, very light grey fill = foreground @ 3.5 %), each: blue checked checkbox (14 px), 26-px app icon (colourful rounded squares: Photo Editor blue-violet, Music Player orange-red, Video Player amber, Code Editor green, File Browser blue, Chat Client purple), a two-line text block (name 12.5 medium dark; below it `4.2.1 → 4.3.0` 10.5 secondary), and a bordered grey `Update` button at the right edge. The last row (`Chat Client`, `9.1.0 → 9.2.4`) carries a light-blue accent capsule pill `App Store` right after its name and its button reads `App Store` (wider). Bottom-left: a blue prominent `Update 6` button. (The five rows show no source pill in the image while the current code draws a pill on every row - `Homebrew` / `App Store` / `Online`; follow the code and flag it as an open question, the pill is a 2-line change.)

Structure (VStack spacing 12, compact 8; `compact` = panel):
1. **Summary row** (HStack baseline, spacing 8): `lastCheckLine` (12, compact 10.5, secondary): `Not checked yet` (`neverChecked`) or `Last checked %@` (`lastCheckFormat`) with a *named* relative date: `now`, `N minutes ago`, `N hours ago`, `yesterday`, `N days ago` (Windows: build from `DateTime.Now - last`, `now` for < 1 min); Spacer; `Check now` bordered small button with refresh glyph (`checkNow`), while checking it becomes `[mini progress ring] Checking` (`checking`); disabled while `isChecking || a package operation runs`.
2. If there are no rows: **empty state** (VStack spacing 6): if `hasCheckedThisSession && !isChecking`: a label `No updates found` (`upToDate`) with green `checkmark.circle.fill` (12 medium green); `No updates found in this partial check` (`partialUpToDate`) with grey `info.circle` when the check had gaps (`coveragePartial`); `No updates outside your rules` (`noVisibleUpdates`) with grey `info.circle` when rules hide results. Then the coverage note (11 secondary, compact 9.5): `Checks the original sources of installed apps and a public catalog. Updates install through their original source.` (`coverageNote`); and if checked and the package manager is missing: `Homebrew is not installed, so apps cannot be updated from here yet.` (`packageMissing`) -> [WIN] `winget is not installed, so apps cannot be updated from here yet.`
3. If there are rows: if any row is selectable the **selection bar** (links, 11, compact 10): `Select all` (disabled when all selected, `selectAll`) and `Clear` (disabled when none, `clearSelection`); then the **list**; then, if any selectable, the primary button `Update %d` (`updateSelectedFormat`; 12 semibold, compact 11 full width small; prominent; disabled when 0 selected or busy).
   - Compact list: scroll area whose height is whole rows: `visible = min(max(rows,1),4)`, `height = visible * 40 + (visible - 1) * 5` (row height 40, spacing 5). Non-compact: rows expand, spacing 5.
   - **Row** (HStack spacing 9, padding h7 v5, radius 8 continuous, fill foreground 3.5 %): checkbox 14 (only when selectable; otherwise a 14 x 14 transparent spacer); app icon 26 (compact 22); VStack spacing 1 { HStack spacing 5 {name 12.5 (compact 11.5) medium, 1 line, middle truncation; **source pill** (8.5 semibold, accent text, capsule filled accent 12 %, padding h5 v1)}; version summary `"<installed> → <latest>"` 10.5 (compact 9.5) secondary }; Spacer; action button (bordered small, 11/10 medium; disabled when busy; tooltip = hint); non-compact rows that have a bundle id also get an `ellipsis.circle` borderless menu (U+E712 More; tooltip/accessibility `More options` (`moreOptions`) + `: <name>`) with items `Skip version %@` (latest version) and `Don’t check this app`; the same two items are the row's context menu in both modes.
   - Source -> pill text / button text / tooltip: packageManager: `Homebrew` (`homebrewBadge`) / `Update` (`updateOne`) / `Update`; appStore: `App Store` (`appStoreBadge`) / `App Store` / `Opens the App Store, where this update is installed` (`storeHint`); onlineCatalog: `Online` (`onlineBadge`) / `Open` (`openApp`) / `Opens the app so its own updater can finish` (`openAppHint`). Online rows are **not selectable**.
   - Row icon = icon of the app bundle path if it exists, else the generic application icon.
4. **Rules** disclosure `Update exceptions` (`rulesTitle`, 12 / compact 10.5): body = hint text (`Skipping a version still allows newer releases. After removing an app exclusion, use Check now to refresh it.` `rulesHint`, 11 secondary) + a `+` bordered small button (tooltip/accessibility `Add an app that should not be checked` (`addException`); disabled while busy) which opens a file picker (applications only, start in `/Applications`, message = `addException`) -> `addExclusion(appAt:)`; below, one row per rule: name (medium, 1 line, tooltip = bundle id) and caption `Skipped version %@` (`skippedVersionFormat`) or `Not checked until this rule is removed` (`excludedApp`); button `Remove` (`removeRule`, bordered small, accessibility `Remove: <name>`, disabled while busy); compact lists scroll in a frame of `min(count,3) * 54` and rows are 48 high.
5. If `coverageIncomplete` (checked, not checking, and (store included but unavailable) or (online included but unavailable)): orange label `Check incomplete` (`incompleteCheck`, triangle glyph, 11.5 medium) + the names of unchecked apps joined by `, ` (11 secondary, 3 lines max in compact, tooltip full) + `The online check could not be completed. Other results are still shown.` (`onlineUnavailable`) + (when the store part failed) a link button `Open the App Store` (`openAppStore`) -> [WIN] `Open the Microsoft Store`.
6. If `versionsUncompared` (online included, catalog answered, some apps could not be compared): grey `info.circle` label `Versions not compared` (`uncomparableVersions`) + the app names + `No update is shown when an app’s own version can’t be compared with the catalog. Check for updates in the app itself.` (`uncomparableVersionsNote`).
7. Package operation status (the shared `HomebrewOperationStatusView`, padding 4 / 0 compact) when a winget operation exists.
8. Last error: orange `exclamationmark.triangle.fill` label (11, compact 10), 3 lines max.

Selection semantics: all selectable rows start **ticked**; after every scan `reconciledSelection` removes vanished ids and **ticks newly appeared selectable ids**; `selectAll()` ticks every selectable row, `selectNone()` clears; `toggle(item)` ignores non-selectable rows. `updateSelected()`: the ticked winget/Homebrew tokens run as one batch `upgradeCasks(tokens)`; ticked store rows: if exactly one store row is ticked and it has a store page open that page, else open the store updates page. Per-row `update(item)`: package -> single upgrade; store -> its page (or the updates page); online -> open the installed app (`handOff(path)`).

### 7.3.4 Check pipeline (Swift)

`check(automatic:)` (guards: feature installed; if already checking just remember an `automatic` request):
1. Reload rules; snapshot `includeHomebrewApps`, `includeAppStore`, `includeOnlineCatalog`, region code; `isChecking = true`; `lastError = nil`; generation counter ++.
2. Work queue (utility QoS): scan installed apps (below) minus apps excluded by a rule (`version == nil`); package source (if Homebrew apps **or** online catalog enabled): ask `brew` for installed casks (`info --json=v2 --installed`), `coveredPaths` = bundles owned by a cask; if Homebrew apps enabled also `outdated --cask --greedy --json=v2` (also self-updating casks) -> rows only when the app bundle still exists, is not from the App Store, the catalog version is comparable (not empty/`latest`) and `isNewer(catalog, bundleVersion)` (the bundle's own version is the truth, receipts can be stale); pinned casks and Vorssaint's own tokens ignored.
3. App Store source (if enabled): candidates = store-receipt apps not covered by a package, unique by bundle id, with a version; batches of 20 ids per request to `https://itunes.apple.com/lookup?bundleId=a,b&entity=macSoftware[&country=XX]` (only `kind == mac-software` entries accepted) and, when the app has an Adam id, `https://uclient-api.itunes.apple.com/WebObjects/MZStorePlatform.woa/wa/lookup?id=..&version=2&p=mdm-lockup&caller=MDM&platform=macappstore[&cc=XX]` (preferred: carries the Mac build and Mac minimum OS); row only if `minimumOS <= current OS` and `isNewer(storeVersion, installed)`; store coverage complete only if every candidate was answered. URL session: ephemeral, 10 s per request / 20 s resource.
4. Publisher feeds (online source, apps with a declared feed, not store apps): `SUFeedURL` (Sparkle appcast, `.appcast` format) or Electron `Contents/Resources/app-update.yml` (provider `generic` -> `<url>/latest-mac.yml`; provider `github` -> `https://github.com/<owner>/<repo>/releases/latest/download/latest-mac.yml`; ignored when `private: true`, a `token`, `requestHeaders`, a non-`latest` channel, or the file is > 64 KB). Feed URLs must be `https`, no credentials/fragment, host with a dot and letters, not `.local/.localhost`, no IP/port, ASCII host labels. Loader: ephemeral session, 10 s request / 20 s resource, no cookies/credentials/cache, max 5 redirects each re-validated, body <= 2 MiB (`byteLimit`), `404/410` = feed absent (defers to the catalog), any other failure = unchecked (check incomplete). Four feeds at a time, coalesced by URL, 60 s ceiling for the whole pass (feeds past the deadline stay unchecked and are named). Appcast parser: namespaces on, external entities off, any entity declaration aborts; path depth <= 32; <= 4096 items; field text <= 64 KB; items need an `enclosure` with an https `url` and no `sparkle:deltaFrom`, `sparkle:os` absent or `macos`; fields `sparkle:version`, `shortVersionString`, `minimumSystemVersion`, `maximumSystemVersion`, `minimumUpdateVersion`, `channel` (must be empty), `hardwareRequirements` (empty or equals `arm64`/`x86_64` of the Mac) and `link`. Eligible release: has download, empty channel, version and display version are plain dotted numbers, platform/hardware/min-max OS/min-installed constraints pass, and `isNewer(version, installedComparableVersion)`; installed comparable version = the bundle build for appcasts (`CFBundleVersion`) or the short version for manifests; manifest OS check uses the Darwin kernel version (`kern.osrelease`).
5. Online catalog (apps not store-owned, not package-covered, with a version): `https://formulae.brew.sh/api/cask.json` (cached 1 h unless the user pressed Check now; fetch with `useProtocolCachePolicy`, `reloadIgnoringLocalCacheData` when forced). Match by app file name (`artifacts[].app` target/source, or `uninstall.delete` paths in `/Applications` or `~/Applications` that are literal `.app`s) with optional bundle-id disambiguation (`uninstall.quit`), else by a *single* declared bundle id; exactly one match required; the catalog entry must be compatible (`depends_on.macos` supports only `>=`/`==`; unknown constraints exclude the entry). Then version logic (7.3.5). Results for apps with their own feed are replaced by the feed's result; an app whose feed is absent (404/410) is answered by the catalog (compared or named as uncomparable).
6. Merge: de-dup by row id; sort `packageManager`, `appStore`, `onlineCatalog` groups, each by `localizedCaseInsensitiveCompare(name)`.
7. `finishCheck` (main thread, only if the generation is still current): apply rules, reconcile selection, publish `items`, flags `packageManagerAvailable`, `onlineCatalogAvailable`, `appStoreAvailable`, `uncheckedAppNames`, `uncomparableAppNames`, `hasCheckedThisSession = true`, `lastCheck = now` (persisted `appUpdatesLastCheck`), `appUpdatesLastCount`. If this was an automatic pass: notify (below) and `scheduleNext()`.

**Installed-app scan**: bundles under `/Applications` and `~/Applications` (recursive, packages not descended) plus shallow Spotlight results in the home folder (`kMDItemContentType == 'com.apple.application-bundle'` via `mdfind -onlyin ~`), reading `Contents/Info.plist`: id, `CFBundleShortVersionString` (else `CFBundleVersion`), display name (`CFBundleDisplayName` else file name), store receipt (`Contents/_MASReceipt/receipt`) + Adam id from Spotlight, build version, publisher feed, and a Positron special case (`Contents/Resources/app/product.json` -> `positronVersion` / `positronBuildNumber`). The app itself (own bundle id / `com.vorssaint*`) is never listed.

**Notifications**: only the automatic pass; only about findings not yet announced (`appUpdatesNotifiedIDs` = announced row ids, persisted, intersected with current ids so a row that disappears and returns is announced again); title `App updates`; body `One app has a newer version.` (`notificationBodyOne`) when the total is 1, else `%@ apps have a newer version.` (`notificationBodyFormat`, total as text); if the toast could not be posted the ids are not marked announced.

**Schedule**: `nextCheckDate(lastCheck, frequency, now)`: off -> nil; never checked -> `now + 180 s` (`catchUpDelay`); else `lastCheck + interval` (1 day / 7 days) and if that is already past `now + 180 s`. One-shot timer, tolerance 60 s; wake-from-sleep recomputes; opening a surface calls `checkIfNeeded()` -> rescan when: a hand-off to an updater is pending, nothing has been scanned in this process, or the last answer is older than `staleAfter = 10 min`. `applicationBecameActive()` re-checks when a hand-off is pending (returning from an updater), and `consumeUpdateHandoffReturn()` reopens the window the update hand-off buried.

**Rules**: `appUpdatesRules` = JSON array of `{bundleID, name, version?}` (one rule per bundle id; `version == null` = exclude the app; else skip exactly that release - compared by `versionCore`); invalid rules (empty id/name, whitespace or `/` in id, uncomparable skip versions, duplicates) are dropped on load. `skipVersion(item)`, `excludeApp(item)`, `addExclusion(appAt:)`, `removeRule` all require `!isChecking`; removing an *exclusion* sets `hasCheckedThisSession = false` (no invented result; Check now refreshes). Settings restore reloads rules and invalidates the session.

**Settings table**

| Key | Type | Default | Notes |
|---|---|---|---|
| `appUpdatesCheckFrequency` | string | `off` | `off`/`daily`/`weekly` |
| `appUpdatesIncludeHomebrewApps` | bool | true | [WIN] winget apps |
| `appUpdatesIncludeAppStore` | bool | true | [WIN] Microsoft Store apps |
| `appUpdatesIncludeOnlineCatalog` | bool | true | other installed apps |
| `appUpdatesNotify` | bool | true | |
| `appUpdatesRules` | string(JSON) | `[]` | portable |
| `appUpdatesLastCheck` | double | 0 | epoch s |
| `appUpdatesLastCount` | int | 0 | |
| `appUpdatesNotifiedIDs` | [string] | [] | |
| `panelUtilityAppUpdates` | bool | true | |

### 7.3.5 Version comparison (port verbatim; pure functions, unit-test them)

- `versionCore(v)`: trim; drop a leading `v`/`V` followed by a digit; cut at the first `,` (package revisions like `3.5.262,260717d`).
- `isUncomparable(v)`: `versionCore(v).lowercased()` is empty or `latest`.
- `compare(a, b)`: split on any non-alphanumeric into parts; compare part by part (missing part = empty): both numeric -> numeric compare ignoring leading zeros and length (compare by stripped length then ordinal); a missing part vs a numeric zero part = equal, otherwise the missing side is lower (so `1.2 == 1.2.0`, `1.2 < 1.2.1`, and, per the code, `1.2 < 1.2beta` because a missing part loses against a word part); mixed: leading number decides (`9a < 10`), with equal numbers the bare part beats a suffixed one (`5 > 5beta`); text tie-break is case-insensitive and splits digit/non-digit runs so `beta10 > beta9`; equal ignoring case = equal. `isNewer(candidate, installed) = compare(versionCore(candidate), versionCore(installed)) == descending`.
- `catalogRelease`, `buildMatchesCatalog`, `installedNumber`, `catalogOnlyAddsUnreportedParts`, `cannotCompare`, `reportsPlaceholderVersion` (placeholder: version `0` or version `1` with build `1`) implement the conservative rules for catalog versions with builds after `-`, `+`, `,` or dotted builds, hashes (>= 7 hex chars with letters and digits after the last dash are dropped), and numbers that go past what the bundle reports (`2026.1` standing for `2026.1.x.y`); in those cases the app is **named as uncomparable** (`uncomparableAppNames`), never reported as an update and never claimed current. Port `AppUpdatesSupport.swift` (951 lines) function-for-function; the file is pure (no IO) and covered by tests.
- Row display strings: `versionSummary = "<installed> → <latest>"` (arrow U+2192); for feed rows whose display versions are equal the build is appended `"<ver> (<build>)"`.

### 7.3.6 Windows mapping

Sources: macOS (Homebrew casks / App Store / Sparkle + Electron feeds + public cask catalog) -> Windows (winget / Microsoft Store / publisher feeds + winget catalog / other).

| Vorssaint source | Windows source | How |
|---|---|---|
| `packageManager` (Homebrew casks, in-place update, selectable) | **winget** source `winget` | `winget upgrade --source winget --accept-source-agreements --disable-interactivity [--include-unknown]`; parse the table (7.4.7 parser). Row id `packageManager:<PackageIdentifier>`; token = Id. Update = `winget upgrade --id <Id> --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity` (batch: one process per id sequentially, or `--all`). The installed version used for the "x → y" text is the registry `DisplayVersion` of the matched app when it exists (the bundle-is-truth rule), else winget's `Version` column. Rows whose Available column is empty or `Unknown` are dropped (uncomparable). Pinned packages (`winget pin list`) are hidden |
| `appStore` (pill `App Store`, hand-off) | **Microsoft Store** source `msstore` | `winget upgrade --source msstore` rows (Store apps have 12-char product ids like `9NBLGGH4NNS1`) or the AppX package list vs `https://storeedgefd.dsx.mp.microsoft.com/v9.0/products/<ProductId>?market=<CC>&locale=<loc>&deviceFamily=Windows.Desktop` (unofficial; do not rely on it). Pill `Microsoft Store`, button text `Microsoft Store`, tooltip `Opens the Microsoft Store, where this update is installed`; action = `Process.Start("ms-windows-store://pdp/?ProductId=<id>")` for one row, `ms-windows-store://downloadsandupdates` for several; row is selectable (batch "Update N" opens the updates page) |
| `onlineCatalog` (pill `Online`, `Open`) | other installed apps found in the registry that winget could not correlate, checked against **publisher feeds** (WinSparkle appcasts = same XML as Sparkle with `sparkle:os="windows"`; Electron `resources\app-update.yml` + `latest.yml`; Squirrel `RELEASES` is not parseable for versions -> skip) and, when `winget upgrade --include-unknown` lists an Available version, against that. Button `Open` launches the installed exe (`DisplayIcon`/`InstallLocation`); non-selectable |

Installed-app scan on Windows: the Uninstall hives (HKLM 64/32, HKCU) + AppX packages (7.2.7) with `DisplayName`, `DisplayVersion`, `InstallLocation`, `DisplayIcon`; icon via shell icon; "from Store" = AppX with `SignatureKind == Store` or `Source` column `msstore`. Rows for apps that are themselves Trayloom are skipped. Feed `Info.plist.SUFeedURL` equivalent: WinSparkle stores `FeedURL` in `HKCU\Software\<Company>\<App>\WinSparkle` or an exe resource; practical rule: only `app-update.yml` (Electron) is read from disk, appcast URLs are looked up from a small bundled mapping table (`AppUpdateFeedMap.json`, ask first before adding network-bound maps).

Network: `HttpClient` with `SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false }` and manual redirect loop (max 5, re-validate every URL with the same `publicURL` rules), `Timeout = 10 s`, overall 20 s via `CancellationTokenSource`, read at most 2 MiB (`Content-Length` and streamed). Parse appcast with `XmlReader` (`DtdProcessing = Prohibit`, `XmlResolver = null`, `MaxCharactersInDocument = 2 MiB`).

winget availability: `where winget` / `%LOCALAPPDATA%\Microsoft\WindowsApps\winget.exe`. Missing -> list shows the `packageMissing` text. First-run source agreements: add `--accept-source-agreements` to every call; Trayloom's existing consent screen (`agreementsAccepted`) may stay but must not replace the layout in 7.3.3.

Interaction with elevation: winget installers may show UAC; `--silent` keeps UI hidden; exit code `0x8A15002B` (no applicable update) must be treated as success/no change - verify the code list in the winget-cli docs before shipping. After a successful batch re-run `check()` (rows disappear by themselves, `startUpgrade` observer semantics) without touching the schedule.

### 7.3.7 Gap vs Trayloom (`AppUpdates/AppUpdatesFeature.cs`, 437 lines)

Has: winget detection, `winget upgrade` fixed-width table parser (header-column based, handles `\r` spinner), a window with DataGrid (Name/Id/Installed/Available), `Check again`, `Update selected`, `Update all`, `Stop`, a log box streaming `winget upgrade --id --exact --silent ...` output, first-use agreement screen, `includeUnknown` toggle, hotkey.
Lacks (priority):
1. P0 The Vorssaint card list: checkbox rows with icon, name, source pill, `x → y`, per-row `Update`/`Microsoft Store`/`Open` buttons, `Select all`/`Clear`, bottom `Update N`, `Last checked`/`Check now` header, empty/partial/incomplete/uncomparable states. Trayloom has a DataGrid + 4 buttons.
2. P0 Source model (winget / Microsoft Store / other apps) and the three toggles, including "at least one source stays on"; scanning of the registry/AppX for icons and real installed versions; `Online` rows with `Open`.
3. P1 Background schedule (off/daily/weekly), `Next check`, notifications of *new* findings only (`appUpdatesNotifiedIDs`), catch-up 180 s, wake handling, staleness 10 min re-check, hand-off return handling.
4. P1 Rules: skip version / exclude app / add exception via picker / `Update exceptions` disclosure + persistence (`appUpdatesRules`).
5. P1 Version-comparison module (port of `AppUpdatesSupport`) with unit tests; publisher feeds (Electron `latest.yml`, WinSparkle appcasts) with the hardened loader.
6. P2 Panel variant (compact rows 40/5, max 4 rows), `Show in panel`, hub strings, settings layout (sections listed in 7.3.2), operation status strip shared with the winget page (7.4.5).
7. P2 Existing UI strings (`Check again`, `Update selected`, `Update all`, `Stop`) are Trayloom-only; replace by the strings above (`Check now`, `Update N`, ...). `Update all` can survive as an extra menu item.

---

## 7.4 Homebrew -> winget (package manager page)

Trayloom has **no** page for this today. Replica = a "Homebrew" page re-implemented over the Windows Package Manager (`winget.exe`). Name on Windows `[WIN]`: **`winget`** wherever Vorssaint says `Homebrew` (feature title, labels, alerts). Keep the feature id `homebrew` for settings/hub compatibility or use `winget`; the Features hub tile reads title `winget` (Vorssaint: `Homebrew`, `homebrewName`), description `Keep winget packages up to date` (Vorssaint `Keep Homebrew packages up to date`, `descHomebrew`), enable caption `Search, install and remove packages.` (Vorssaint `Search, install and remove formulae and casks.`, `homebrewEnableCaption`), group Tools, SF `shippingbox` -> U+E7B8 (Package, verify). Panel toggle `panelUtilityHomebrew` (default true). Permission chips: macOS App Management (not applicable on Windows). Brand: never use the word "Homebrew" in Trayloom.

Vorssaint scope reminder: the page implements **search, installed list (+ updates), details, install, uninstall, upgrade, upgrade all, update Homebrew (index refresh), dependency grouping + orphans, tap-trust card, shell-setup banner, install-Homebrew screen, terminal fallback for sudo**. It does **not** implement taps management, services, doctor, cleanup, pin, bundle/export or analytics beyond popularity badges; those only appear in the mapping table 7.4.6 as optional winget extras.

### 7.4.1 Settings page layout (`HomebrewSettings`)

Whole page = VStack spacing 0 filling the detail pane:
1. **Page header** (padding h16 v12): `Label("Homebrew", shippingbox)` 14 semibold, then Divider.
2. **Missing state** (when no `brew`/`winget`): centred column, spacing 12, padding 28: glyph 44 pt light secondary, `Homebrew not found` (`homebrewMissingTitle`, 16 semibold) -> [WIN] `winget not found`; body (callout secondary, centred, max 360) `Vorssaint can open the official Homebrew installer in your terminal. It shows the steps and asks for your password if needed.` -> [WIN] `Trayloom can open the Microsoft Store page for App Installer, which provides winget. Install or update it there, then come back.` (`homebrewMissingBody`); prominent `Install Homebrew` button with terminal glyph (`homebrewInstallHomebrew`) -> [WIN] `Install winget` (opens `ms-windows-store://pdp/?ProductId=9NBLGGH4NNS1`); caption (12 pt `.caption` secondary centred max 360) `When the installer finishes, come back here and click Refresh.` (`homebrewInstallHomebrewCaption`) which changes to `Installer opened in your terminal.` (`homebrewInstallHomebrewOpened`, after the button was used) -> [WIN] `Microsoft Store opened.`; optional orange error label; `Refresh` button with arrow glyph (`homebrewRefresh`) -> re-detect + reload.
3. **Content** when present: toolbar (padding h16, top 16, bottom 12), optional shell-setup banner, Divider, then a horizontal split: **package list** (fixed width 232, full height) | Divider | **detail pane** (flexible).
   - **Toolbar** (VStack spacing 9). Row 1 (adaptive: single line if it fits, else two lines): search field (rounded border, min width 150, placeholder `Search packages` (`homebrewSearchPlaceholder`), Enter = search) ; kind picker (dropdown, width 116: `Casks` / `Formulae` (`homebrewCasks/Formulas`), default Casks) -> [WIN] source picker `winget` / `Microsoft Store`; `Search` button with magnifier glyph (`homebrewSearchButton`; disabled when the trimmed query is empty or busy); refresh icon button (`homebrewRefresh` tooltip; disabled when busy); `Update Homebrew` button with `arrow.triangle.2.circlepath` glyph (`homebrewUpdateHomebrew`; opens a confirmation) -> [WIN] `Update sources` (`winget source update`). Row 2: segmented filter of 300 width: `All <n>`, `Casks <n>`, `Formulae <n>` (counts of installed) -> [WIN] `All <n>`, `Apps <n>`, `Tools <n>` (7.4.4); next to it the **outdated summary**: while loading `[mini ring] Updates`; when `outdatedCount > 0`: orange caption `Updates <n>` with `arrow.up.circle.fill` and a small `Update all` button (`homebrewUpgradeAll`, `arrow.up.circle`; disabled when busy). Row 3 (right aligned): small checkbox `Group dependencies` (`homebrewGroupDependencies`, key `homebrewGroupDependencies` default true) - **omit on Windows** (see 7.4.4).
   - **Shell setup banner** (only when brew exists but the shell cannot find it): `terminal` glyph 15 pt (20 wide), title `Finish terminal setup` (`homebrewShellSetupTitle`, 12 semibold), body caption `Homebrew is installed, but your terminal may not find the brew command yet. Vorssaint can open the setup command in your terminal.` (`homebrewShellSetupBody`) or `Command opened in your terminal. Then come back here and click Refresh.` (`homebrewShellSetupOpened`) ; prominent small `Set up terminal` button with wrench glyph (`homebrewShellSetupButton`) and a small refresh icon button. [WIN] show only if `winget.exe` exists in `%LOCALAPPDATA%\Microsoft\WindowsApps` but that folder is not on the user `PATH`: body `winget is installed, but your terminal may not find the winget command yet. Trayloom can add its folder to your PATH.`, button `Set up terminal` -> append the folder to `HKCU\Environment\Path` and broadcast `WM_SETTINGCHANGE`.
   - **Package list** (ScrollView, content padding h10 v12, section spacing 14, row spacing 4): section **Results** (`homebrewSearchResults`) shown while searching (`[ring] Loading…`, `homebrewLoading`), with results (title + count badge), or after a non-empty query with no results: count badge 0 + `No results` (`homebrewSearchEmpty`); section **Installed** (`homebrewInstalled`) with count badge (the filtered count): loading row `Loading…`, empty `No packages found` (`homebrewNoPackages`), else rows; section **No longer needed** (`homebrewOrphans`) + `Installed as dependencies, but no installed package needs them any more.` (`homebrewOrphansNote`) - omit on Windows. Section title 11 semibold secondary, count badge capsule (9 rounded semibold, secondary on foreground 6 %, padding h5 v1), title padding h6.
   - **Package row** (HStack spacing 8, padding h6 v6, radius 6 continuous; selected = accent @ 12 % fill; context menu): [WIN-optional chevron button for dependency expansion - omit]; icon (`terminal` for formula = [WIN] portable/zip/CLI package, `app` for cask = [WIN] installer-based package, 12 semibold secondary, 16 wide); VStack spacing 2 {display name 12 (semibold when selected), 1 line; subtitle 10 secondary = installed/stable version else name}; Spacer; `Dependency updates: %d` orange 10 (only with grouping); popularity capsule (10 semibold accent on accent 12 %, padding h6 v2; compact count `1.2K` / `15K` / `1.5M`, tooltip `%@ installs in %@ days`) - [WIN] none available (see 7.4.4); trailing: mini ring while an operation targets this package, else (update available) orange `arrow.up.circle.fill` icon button 22 x 20 (tooltip `Update: <installed -> current>`, accessibility `Update`; disabled when busy) else (installed) green `checkmark.circle.fill` 11. Context menu: `Package details` (info glyph), `Update` (if update; disabled when busy), `Uninstall` (destructive, trash) if installed else `Install` (`arrow.down.circle`).
   - **Detail pane** (scroll, padding 16; spacing 14): `[ring] Loading…` while loading details; the package (below) or the empty detail: `Label("Package details", shippingbox)` 18 semibold + `Select an installed package or search for a new one.` (`homebrewNoSelection`, secondary); trust card (7.4.3); orange error label (caption); when an operation exists: Divider + the operation status view (padding 12).
   - **Package detail** (spacing 12): title row (baseline): display name 18 semibold (2 lines) + status badge capsule (10 semibold; padding h7 v3; colour/12 % fill): while operating = accent with the phase text (`Installing files…` etc.), update available = orange `Update available` (`homebrewUpdateAvailableBadge`), installed = green `Installed` (`homebrewInstalledBadge`), else secondary `Not installed` (`homebrewNotInstalledBadge`); id/token (caption monospaced secondary, selectable); description (callout, wraps); rows (caption): `Version` (`homebrewVersion`; installed version(s) or stable), `Latest` (`homebrewLatestVersion`, only when an update exists), `Type` (`homebrewDescription` = "Type", value `Formulae`/`Casks`), `Popularity` (`homebrewPopularity`, `<n> installs in 30 days`), link `Open website` (`homebrewHomepage`); rows are label-left (secondary) / value-right (right aligned, selectable). Buttons (spacing 8, disabled while busy): while this package is operating a label `[hourglass] <phase>`; installed -> prominent `Update` (if update; `homebrewUpgrade`) + `Uninstall` (destructive, `homebrewUninstall`); else prominent `Install` (`homebrewInstall`).
   - Confirmation dialog (`confirmationDialog`, title visible; buttons: the action (destructive for uninstall) + `Cancel`): titles/bodies: Install `Install with Homebrew?` / `Homebrew will download and install %@. Dependencies may also be installed.`; Uninstall `Uninstall with Homebrew?` / `Homebrew will uninstall %@. Configuration files may remain on the system.`; Update `Update with Homebrew?` / `Homebrew will download and apply the latest version of %@. Dependencies may also be updated.`; Update all `Update all with Homebrew?` / `Homebrew will download and apply the latest versions for packages with updates available. Dependencies may also be updated.`; Update Homebrew `Update Homebrew?` / `Homebrew will fetch the latest information and then reload your packages.` [WIN] replace `Homebrew` by `winget`; update-sources body `winget will refresh its package sources and then reload your packages.`
Selection resets (pending action cleared, details hidden, selection cleared) when the query, the kind or the filter changes; the filter change only clears the selection if it became hidden. After a successful action the list scrolls to the top when the action clears the selection (uninstall).

**Panel variant** (`PanelHomebrewView`): header `Label("Homebrew", shippingbox)` 12 semibold + close `xmark.circle.fill` 14 (tooltip `Cancel`); missing card (glyph 30, title 13 semibold, body 10.5, `Install Homebrew` small prominent, caption 9.5, error 9.5 orange, `Refresh` small bordered); otherwise: segmented `Search` / `Installed <n>` (`homebrewSearchButton`, `homebrewInstalled`), shell-setup card, then **Search mode**: card with search field (24 high) + prominent small magnifier button, kind segmented (`Casks`/`Formulae`) and the 9-pt tertiary hint `Space or Return closes the macOS panel. Use the search button.` (`homebrewKeyboardHint`; [WIN] `Press Enter to search.`), results list; **Installed mode**: card with filter segmented (`All n / Casks n / Formulae n`), buttons `Check packages` (`homebrewCheckPackages`, refresh glyph) and `Update Homebrew`, outdated summary (`Updates n` orange with `Update all` / or `Updates 0` with green check when none), installed list; list = section label (10 semibold tertiary) + count badge + ring while loading, rows with spacing 5, empty/loading text 10 secondary in a 64-high area; then an error banner, a detail card and the operation card. Same confirmation dialog.

### 7.4.2 Operation status view (`HomebrewOperationStatusView`, shared by 7.2, 7.3, 7.4)

`compact: Bool` (panel / app-updates row). VStack spacing 9 (compact 7):
- **Header** HStack(top, 8): round icon 28 (compact 22) with a circle fill of the status colour @ 14 % and a 12 pt (compact 10) semibold glyph; VStack spacing 2 {**title** 13 (compact 11.5) semibold, <= 2 lines; sub-row font 11 (compact 9.5) secondary: while running `<phase text>` + `|` + elapsed; else just elapsed}; Spacer; button `Cancel` (running) or `Clear log` (finished) mini (`homebrewCancelOperation`, `homebrewClearLog`).
  Elapsed text: `%@ elapsed` (`homebrewOperationElapsedFormat`) with `Ns` (< 60 s), `Nmin` (< 60 min), `Nh Mmin`; refreshed every 1 s (`TimelineView periodic 1`), frozen at `finishedAt`.
- **Progress area**: running: determinate linear bar when a fraction is known else an **indeterminate bar** (height 5, track secondary @ 18 %, a capsule of width max(34 % of track, 36) in accent @ 72 % sliding from -0.35 to 1.05 of the width, `linear 1.15 s` repeat forever, clipped) + caption `Homebrew has not reported a percentage yet.` (`homebrewOperationProgressUnknown`, 10 tertiary, 1 line) -> [WIN] `winget has not reported a percentage yet.`; succeeded: full bar.
- Last activity line (11, compact 9.5 secondary, 3 lines / compact 2) when present.
- `needsTerminal`: label `This operation needs your terminal to ask for the administrator password. Vorssaint does not capture passwords.` (`homebrewTerminalFallback`, terminal glyph, 11 secondary) -> [WIN] `This operation needs administrator approval. Trayloom does not capture passwords.`, and, if a command was recorded, small buttons `Open terminal` (`homebrewOpenTerminal`) and `Copy` (`menuCopy`, copy glyph, copies the full command).
- When a log exists: a plain button `Show details` / `Hide details` (`homebrewOperationShowDetails/HideDetails`, chevron down/up, 11 medium secondary); expanded: `Technical details` (`homebrewOperationTechnicalLog`, 11 semibold tertiary) + mini `Copy` button + monospaced log box (font 10, compact 9; height 96 / compact 58; selectable, scroll).
- Title strings: running `Installing %@` / `Uninstalling %@` / `Updating %@` / `Updating packages` / `Updating Homebrew` (`homebrewOperationInstallFormat`, `...UninstallFormat`, `...UpgradeFormat`, `...UpgradeAll`, `...UpdateHomebrew`); succeeded `%@ installed.` / `%@ uninstalled.` / `%@ updated.` / `Packages updated.` / `Homebrew updated.`; failed `Could not finish %@.` (`%@` = package display name, or `packages` (`homebrewAllPackages`) for all-package ops); cancelled `Operation cancelled.`; needsTerminal `Continue in your terminal.`.
- Phase texts (`homebrewOperation*`): `Preparing…`, `Downloading files…`, `Installing files…`, `Removing files…`, `Updating files…`, `Finishing…`, `Refreshing list…`.
- Icons/colours: running = action glyph (install `arrow.down.circle.fill`, uninstall `trash.circle.fill`, upgrade/upgrade-all `arrow.up.circle.fill`, update `arrow.triangle.2.circlepath`) accent; succeeded `checkmark.circle.fill` green; failed `exclamationmark.triangle.fill` orange; cancelled `xmark.circle.fill` secondary; needsTerminal `terminal.fill` orange.
- Auto cleanup of a finished status: succeeded after 8 s and cancelled after 6 s clear the whole status (and log, terminal command); failed / needsTerminal clear only the log after 20 s and keep the status.

### 7.4.3 Trust card (`HomebrewTrustCard`) - Windows: "source agreements" card

Card (radius 10, orange @ 9 % fill, orange @ 22 % 1-px border; padding 12, compact 8): `Label("Tap not trusted yet", checkmark.shield)` (`homebrewTrustTitle`, headline / compact 11 semibold), caption `Homebrew now asks for your confirmation before using third party taps. Trust %@ to continue.` (`homebrewTrustCaption`, `%@` = tap name; 10 pt-ish caption secondary), prominent button `Trust and continue` (`homebrewTrustButton`) with a mini ring while trusting. Vorssaint shows it when a command fails with `Refusing to load formula x from untrusted tap owner/repo` (regex `from untrusted tap ([A-Za-z0-9._-]+/[A-Za-z0-9._-]+)`); `Trust` runs `brew trust --tap <tap>` and then **retries** the interrupted operation (refresh / select / the action). [WIN] the equivalent gate is the source agreement prompt: when winget output contains `The source requires that you view the following agreements before using` / `Do you agree to all the source agreements terms?`: show the card with title `Source terms not accepted yet`, caption `winget needs your confirmation before using the %@ source. Accept to continue.` (`%@` = `winget` or `msstore`), button `Accept and continue`, which records `agreementsAccepted = true` and retries with `--accept-source-agreements`. In normal use pass `--accept-source-agreements` from the start once the user accepted once (the existing Trayloom consent screen).

### 7.4.4 Behaviour (Swift), and how each maps

State: `brewPath`, `installed[]`, `searchResults[]`, `selectedPackage`, flags `isLoadingInstalled/Outdated/Searching/Popularity/Details`, `operation`, `operationStatus`, `log`, `errorMessage`, `terminalFallbackCommand`, `untrustedTap`, `isShellConfigured`, `outdatedPackagesByID`. `isBusy` = an operation is running (reads queue on the same serial queue). Generation counters (`searchGeneration`, `detailsGeneration`, `outdatedGeneration`) drop stale answers.

| Screen / operation | Vorssaint command | winget equivalent (CLI) | Parsing strategy |
|---|---|---|---|
| Detect | `FileManager.isExecutableFile(/opt/homebrew/bin/brew \| /usr/local/bin/brew)` | `where winget` or `%LOCALAPPDATA%\Microsoft\WindowsApps\winget.exe`; `winget --version` (e.g. `v1.9.25200`) | version string `^v?\d+\.\d+\.\d+` |
| Installed list | `brew info --json=v2 --installed` | `winget list --accept-source-agreements --disable-interactivity` (add `--source winget` / `--source msstore` for filters) | fixed-width table, see 7.4.7; richer JSON alternative `winget export -o <tmp.json> --include-versions --accept-source-agreements` (only packages that match a source) |
| Outdated / updates | `brew outdated --json=v2` (+ `--cask --greedy` for self-updating casks) | `winget upgrade --accept-source-agreements --disable-interactivity` (+ `--include-unknown` to show packages whose version cannot be compared; `--include-pinned`) | same table parser, columns `Name Id Version Available Source`; footer `<n> upgrades available.`; second table "The following packages have an upgrade available, but require explicit targeting for upgrade:" = rows needing `--id` (installed per-machine/other scope): show them but mark as `explicit` |
| Search | `brew search --formula\|--cask <q>` then enrich with popularity + installed state | `winget search "<q>" [--source winget\|msstore] --accept-source-agreements --disable-interactivity` (use `--count 50`, `--exact` for an id match; `--id`, `--name`, `--moniker`, `--tag`, `--command` to scope) | table `Name Id Version Match Source` (the `Match` column exists only for `search`); "No package found matching input criteria." (exit code `0x8A150014`/-1978335212; verify) = empty result, not an error |
| Details | `brew info --json=v2 --formula\|--cask <token>` | `winget show --id <Id> --exact [--source ..] --accept-source-agreements` (+ `--versions` for the version list) | `Key: value` lines: first line `Found <Name> [<Id>]`, then `Version:`, `Publisher:`, `Publisher Url:`, `Author:`, `Moniker:`, `Description:` (may wrap with 2-space continuation lines), `Homepage:`, `License:`, `License Url:`, `Privacy Url:`, `Copyright:`, `Release Notes:`, `Tags:` (indented list), `Installer:` block with `Installer Type:` (msi, msix, exe, inno, nullsoft, wix, burn, portable, zip), `Installer Url:`, `Installer SHA256:`, `Release Date:`, `Package Dependencies:` (indented list when present). winget's console text (headers and `Key:` names) follows the Windows display language and cannot be forced to English from the command line, so parse details by line order / first colon, match keys through a small per-language alias table (en first), and read UTF-8 output (`StandardOutputEncoding = UTF8`); the description text is a best-effort field |
| Install | `brew install [--cask] <token>` | `winget install --id <Id> --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity [--source ..] [--scope user\|machine] [--version <v>]` | streaming (7.4.8) |
| Uninstall | `brew uninstall [--cask] <token>` | `winget uninstall --id <Id> --exact --silent --disable-interactivity [--source ..]` (`--purge` to delete portable data, `--preserve` to keep) ; for entries without a winget id: `winget uninstall --product-code "{GUID}"` or the Uninstall key name | streaming |
| Upgrade one | `brew upgrade [--cask] <token>` | `winget upgrade --id <Id> --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity` (`--uninstall-previous` for installers that cannot upgrade in place) | streaming |
| Upgrade many (App updates batch) | `brew upgrade --cask --greedy <tokens…>` | one `winget upgrade --id` per token, sequentially (keeps per-package progress/failure) | streaming per token, aggregate result |
| Upgrade all | `brew upgrade` | `winget upgrade --all --silent --accept-package-agreements --accept-source-agreements --disable-interactivity [--include-unknown]` | streaming |
| Update Homebrew (refresh index) | `brew update` | `winget source update` (alias `winget source update --name winget`); there is no `update winget itself` (App Installer updates through the Store / `winget upgrade --id Microsoft.AppInstaller`) | streaming; phase `Refreshing list…` |
| Tap trust | `brew trust --tap <tap>` | source agreements: `winget source update --accept-source-agreements`; list sources: `winget source list` | text |
| Taps (not in Vorssaint UI) | `brew tap`, `brew tap <user/repo>` | `winget source list`, `winget source add --name <n> --arg <url> --type Microsoft.Rest`, `winget source remove --name <n>`, `winget source reset --force` (admin), `winget source export` | table `Name Argument` / `Name: x` blocks |
| Services (not in Vorssaint) | `brew services` | no winget equivalent; Windows services are managed with `Get-Service`/`sc.exe`/`ServiceController` (out of scope) | - |
| Doctor (not in Vorssaint) | `brew doctor` | `winget --info` (version, paths, settings link, admin settings), `winget source list`, `winget settings export`, `winget features` | text |
| Cleanup (not in Vorssaint) | `brew cleanup` | no command; closest = delete `%TEMP%\WinGet\*` and `%LOCALAPPDATA%\Packages\Microsoft.DesktopAppInstaller_8wekyb3d8bbwe\LocalState\DiagOutputDir\*.log` (add to the Cleaner as `Package manager caches`) | - |
| Pin (not in Vorssaint) | `brew pin` | `winget pin add --id <Id> [--version <v>] [--blocking]`, `winget pin list`, `winget pin remove --id <Id>` | table |
| Export/import (not in Vorssaint) | `brew bundle dump/install` | `winget export -o file.json [-s winget] [--include-versions]`, `winget import -i file.json --accept-package-agreements --accept-source-agreements` | JSON |
| Popularity badge | `https://formulae.brew.sh/api/analytics/{install-on-request/homebrew-core\|cask-install/homebrew-cask}/30d.json` cached 24 h, ranking by install count | none (no public analytics). Omit the badge (keep the layout slot empty); optionally sort search results by winget's own `Match`/ordering | - |
| Cask vs formula | `--cask`/`--formula`, `terminal` vs `app` icon, `Casks`/`Formulae` pickers | winget has one package kind. Classify each package by installer type (`portable`/`zip` -> "Tool", terminal icon; everything else -> "App", app icon); unknown until `winget show` was read, so installed rows are classified from `winget export` + a cached map and search rows default to "App". Replace the *search kind* picker by a **source** picker (`winget` / `Microsoft Store`) and the *installed filter* by `All / Apps / Tools` (or `All / winget / Store`); the `Type` detail row shows `Apps`/`Tools` |
| Dependencies | `runtime_dependencies`, `installed_on_request`, orphan section | `winget show` lists `Package Dependencies` only when the manifest declares them (rare); `installed_on_request` does not exist. **Do not implement grouping/orphans**; hide the `Group dependencies` toggle and the `No longer needed` section; the key `homebrewGroupDependencies` stays registered (default true) but unused |

Command validation: package tokens must match `^[A-Za-z0-9][A-Za-z0-9._+@/-]*$` (no leading `-`, no `..`, no `//`); winget ids (`Microsoft.VisualStudioCode`, `7zip.7zip`, `9NBLGGH4NNS1`) satisfy it. Arguments are passed as an argument list (`ProcessStartInfo.ArgumentList`), never concatenated through a shell.

**Selecting a package**: `select(package)` sets it immediately, then loads details (`brew info`); apply the enriched result only if it is still the latest request; a refreshed list re-selects the same id. **Refresh**: `refreshInstalled()` loads installed, then outdated, then re-applies selection and update info; failure shows the (trimmed) output as orange error; untrusted-tap output triggers the trust card. **After an operation**: success -> mark complete (progress 1), clear the selection if the action `clearsSelectionOnSuccess` (uninstall), `refreshInstalled()`, re-select the package otherwise; cancelled -> `refreshInstalled(clearingError: false)`; needs terminal -> set `terminalFallbackCommand`, status `needsTerminal`, refresh without clearing; untrusted tap -> trust card with retry (no refresh, it would clear the card); other failure -> `errorMessage` = last 3 non-progress lines (or the trimmed output) and refresh without clearing (brew may have done part of the work).

**Timeouts**: read-only commands 30 s (`brewReadTimeout`); long operations are bounded by **silence** (no output for 15 min, `brewSilenceTimeout`), not by total time; SIGTERM then kill after 2 s (`processTerminationGrace`). Windows: `Process.Kill(entireProcessTree: true)` after a graceful `CloseMainWindow`/Ctrl-C attempt is not possible for hidden consoles, so just kill the tree after 2 s.

**Environment**: Vorssaint merges the login shell's proxy and `HOMEBREW_*` exports into brew's environment (once per launch, 10 s limit). Windows has no login shell; winget reads proxy from system settings/`winget settings`; pass the process environment unchanged.

**Terminal fallback (needs sudo password)**: detected when output contains `sudo:`, `a terminal is required`, `password is required`, `password:` or `administrator privileges`; Vorssaint writes a 0700 script that deletes itself and `exec`s the login shell with the command, opens it with the user's terminal app. Windows: detect UAC/elevation failures (`administrator`, `requires elevation`, exit codes for installer elevation required) and offer `Open terminal` = start `wt.exe`/`powershell.exe -NoExit -Command "<cmd>"` with `Verb = runas`, plus `Copy`.

### 7.4.5 Progress parsing (port)

Applied to each output chunk of a running operation (`updateOperationStatus`):
- `progressFraction`: the **last** match of `([0-9]{1,3}(?:\.[0-9]+)?)%` in the chunk, `/100`, clamped 0...1. winget additionally prints byte progress `  3.25 MB /  7.90 MB` and a bar of `█`/`▒` blocks: derive a fraction from the byte pair when no percent is present; otherwise leave `nil` (indeterminate bar).
- ANSI stripping regex `\u001B\[[0-9;?]*[ -/]*[@-~]`; carriage returns replaced by newlines.
- `phase(in:action:)` on lower-cased, ANSI-stripped text, first hit wins: contains `downloading`/`fetching`/`downloaded` -> downloading; `uninstalling`/`zap`/`purging` -> uninstalling; `upgrading`/`upgraded` -> upgrading; (updateHomebrew action and `updating`/`updated`/`already up-to-date`/`already up to date`) -> refreshing; `installing`/`pouring`/`moving app`/`linking` -> by action (uninstall -> uninstalling, upgrade/upgrade-all -> upgrading, update -> refreshing, install -> installing); `cleanup`/`cleaning`/`caveats`/`summary`/`installed!`/`uninstalled` -> finalizing. Initial phase: uninstall -> uninstalling, everything else -> preparing. winget lines to add: `Found <name> [<id>]` -> preparing; `Downloading <url>` -> downloading; `Successfully verified installer hash` / `Starting package install...` -> installing; `Starting package uninstall...` -> uninstalling; `Successfully installed` / `Successfully uninstalled` / `Successfully upgraded`(winget prints `Successfully installed` also for upgrades) -> finalizing; `No applicable upgrade found` / `No installed package found matching input criteria.` -> failed with that line.
- `activity(in:)`: last line, from the end, that is non-empty, not starting with `$ `, and not "mostly progress symbols" (> 85 % of non-whitespace chars in `#=-> .:%0123456789`); leading `==>` / `->` markers stripped. winget: also drop the spinner glyph lines (`-`, `\`, `|`, `/`) and block-bar lines.
- `visibleError(from:)`: the last 3 such lines joined by `\n`.
- `needsTerminalFallback(output)`: see above.
- Log: every chunk appended to `log` (cleared when an operation starts, by `Clear log`, and by the auto cleanup). Process output is read incrementally from stdout+stderr merged.

### 7.4.6 Settings / persisted state

| Key | Type | Default | Meaning |
|---|---|---|---|
| `homebrewGroupDependencies` | bool | true | Group installed dependencies under their parents (Windows: unused) |
| `panelUtilityHomebrew` | bool | true | Panel row visibility |
| `appUpdates*` | | | See 7.3 (App updates reuses this manager for in-place upgrades) |

No other settings. No caches are persisted (popularity cache 24 h is in-memory).

### 7.4.7 `winget` table parser (implementation guidance)

winget has no `--output json` for `list/upgrade/search`. Parse the console table:
1. Run with `UseShellExecute=false`, redirect stdout/stderr/stdin (close stdin), `StandardOutputEncoding = UTF8`, `CreateNoWindow = true`, and arguments `--accept-source-agreements --disable-interactivity`. Normalise `\r\n`; for each line keep only the text after the last `\r` (spinner/progress frames are overwritten with `\r`).
2. Find the separator line `^-{5,}$`; the header is the line before it. Column starts = positions of header words separated by 2+ spaces (`\S+(?: \S+)*`). Do not trust English header names; use positions: col0 Name, col1 Id, col2 Version, col3 Available (upgrade/list) or Match (search), col4 Source (optional).
3. **Width caveat**: winget pads columns by *display width*, so rows containing full-width (CJK/emoji) characters are shifted; compute positions in terminal cells (East Asian Width: wide = 2) or, for such rows, resolve the id by re-querying `winget list --id <Id>`. Truncated names end with `…` (U+2026) - keep as is.
4. Stop at the first blank line or a footer line matching `^\d+\s+(upgrades?|packages?)\b`; a following sentence beginning `The following packages have an upgrade available, but require explicit targeting` starts a second table (same parser).
5. Unknown versions: `Version` column `Unknown` or `< <ver>` prefixes (`< 1.0`): strip `< `, treat `Unknown` as uncomparable (hide unless `--include-unknown`).
6. IDs: Store apps have 12-char alphanumeric product ids (source `msstore`); ARP entries not matched to a manifest appear as `ARP\Machine\X64\{GUID}` / `ARP\User\X64\<name>` / `MSIX\<PackageFullName>` with an empty Source - these are the "Other" bucket (cannot be upgraded by id; app updates may still show them).
7. Prefer COM later: `Microsoft.Management.Deployment` (WinGet COM API: `PackageManager.CreateCompositePackageCatalog`, `FindPackagesAsync`, `UpgradePackageAsync` with progress callbacks) avoids all parsing and gives real progress. It needs the CsWinRT-projected NuGet `Microsoft.WindowsPackageManager.ComInterop` -> **ask first** (not in the allowed list). Until then keep the CLI path behind an interface `IPackageSource` so it can be swapped.

### 7.4.8 Streaming runner

`runStreaming`: start process, read stdout+stderr as they arrive in chunks, call `onOutput(chunk)` on a work queue (append to log + update status), watchdog for silence, `cancelOperation()` sets `cancelRequested`, terminates, and on exit decides the result (7.4.4 "After an operation"). Only one operation at a time (`operation != nil` rejects a new one; all buttons disabled while `isBusy`). `clearLog()` clears log + fallback command and drops the status when nothing runs. In Trayloom use `Process` + `OutputDataReceived`/`ErrorDataReceived` (already done in `AppUpdatesFeature.RunStreamedAsync`) and marshal to the UI thread.

### 7.4.9 Gap vs Trayloom

- `Trayloom.Features.Tools` has no winget page: only `AppUpdatesFeature.cs` contains a `Winget` helper (`IsAvailable`, `Start`, `ParseUpgrade`) and `winget upgrade` / `upgrade --id` execution. There is no `homebrew`/`winget` feature id, no hub tile, no settings page, no panel view, no operation status view.
- To build (priority): P0 `IPackageSource` + `WingetCli` (list/upgrade/search/show parsers incl. width-aware columns, install/uninstall/upgrade/upgrade-all/source update streaming, cancel, timeouts); P0 page layout 7.4.1 (toolbar, 232-px list, detail pane, confirmation dialogs, missing state); P0 `OperationStatusView` (7.4.2) shared with Uninstaller/App updates; P1 panel variant; P1 source-agreement card and PATH banner; P2 optional extras (pin, export/import, sources) behind an "Advanced" disclosure; P2 classification of Apps vs Tools and the hub tile/description.
- Reuse: `Winget.ParseUpgrade` can be promoted into `Trayloom.Core` (it is the table parser of 7.4.7 without the cell-width logic).

---

## 7.5 Media tools (video, GIF, image, text)

### 7.5.1 Feature identity

| Item | Value |
|---|---|
| Feature id | `media-tools` (Trayloom; currently titled "Media compress") / `mediaTools` (Vorssaint) |
| Hub tile | title `Media` (`mediaName`), description `Compress videos, images and GIFs` (`descMediaTools`), group Tools, SF `photo.on.rectangle.angled` -> U+E91B (Photo2, verify) |
| Enable caption | `Compress videos, convert and process images, make GIFs and extract text locally.` (`mediaEnableCaption`) |
| Header note (top right of the view) | `Local. No network.` (`mediaLocalNote`, 11 medium secondary; compact 9.5) |
| Hosting | Settings page `MediaSettings` = `MediaWorkspaceView(compact: false)` with padding 16; panel/island = `MediaWorkspaceView(compact: true, onClose:)` (panel row key `panelUtilityMedia`, default true; the panel keeps itself open while a file dialog is up) |
| External entry | The workspace can be opened with `initialInputs: [URL]`, `initialTool`, `preservesServiceState` (used by the shelf / recent-capture / screenshot flows to send files to a tool); on Windows expose `MediaToolsFeature.OpenWith(string[] paths, MediaTool tool)` |
| Permissions | none |

### 7.5.2 Layout

Outer VStack spacing 14 (compact 10): **header**, **tool picker**, then scrolling **content** (compact: max height 430; in the island host the whole thing is one scroll view that reports its content height).
- **Header** (HStack spacing 9): `Label("Media", photo.on.rectangle.angled)` 16 semibold (compact 12), Spacer, `Local. No network.`, optional close button `xmark.circle.fill` 14 pt in a 22 x 22 frame (tooltip `Cancel`).
- **Tool picker**: segmented control, 4 segments in this order: `Video` (`mediaToolVideo`), `GIF`, `Image`, `Text` (`mediaToolGIF/Image/Text`). Tool ids: `videoCompressor`, `gifMaker`, `imageCompressor`, `textExtractor`. Changing tool: cancels any pending video import, recomputes the image size for the first input, resets the default output name, resets trim to full duration, clears the local message, and resets the service state to idle.
- **Content** VStack spacing 12 (compact 9): `fileCard`, `optionsCard`, `actionRow`, `statusCard`. Every "card" uses `panelCard()`: padding 10, radius 10 continuous, fill = foreground-on-glass (light white @ 38 %, dark white @ 7.5 %), 0.7-px border (light black @ 9 %, dark white @ 11 %). "Control fill" = light black @ 5.5 % / dark white @ 8.5 %; control border = same border colour at 0.8 px.

**File card** (spacing 9): 
1. **Drop/choose button** (min height 62, compact 52, radius 10, fill = control fill; while a drag hovers: accent @ 16 % fill + accent @ 70 % border 1.2 px): HStack spacing 9: glyph 16 semibold (`doc.badge.plus`; for the Text tool `doc.text.viewfinder`; Segoe U+E8E5 / U+E8A5, verify), VStack spacing 2 {title 12.5 semibold (compact 11.5), 1 line, middle truncation: `Choose file` (`mediaSelectFile`) when empty, the file name, or `%d files selected` (`filesSelectedFormat`) for several images; subtitle 10.5 (compact 9.5) secondary `Drop a file here or click to choose one.` (`mediaDropHint`)}. When an input exists an `xmark.circle.fill` clear button (16 / compact 14, 28 / 24 hit area, tooltip `Cancel`) sits at the trailing edge (extra trailing padding 34 / 30). Not a drop target inside the island (clicks only).
2. **Output row** (HStack spacing 7): `Output` (`mediaOutput`, 10.5 semibold secondary), the output file name (11, 1 line, middle truncation) or `Automatic` (`mediaOutputAutomatic`), Spacer, small button `Destination` with folder glyph (`mediaChooseOutput`; disabled with no input or while running).
3. If tool == Image and more than one file: checkbox `Save in “Converted” subfolder` (`saveInSubfolder`; key `mediaImageSaveInSubfolder`, default false; disabled while running).

**Options card per tool** (spacing 10):
- **Video**: trim row (two numeric fields `Start` / `End` with suffix `s`); sizing segmented `Resolution` | `File size` (`mediaSizingResolution`, `mediaSizingFileSize`); if Resolution: **compression row** then stepper `Size` (`mediaMaxSize`) `640...3840` step 320 suffix `px`; if File size: **target size row**.
- **GIF**: trim row; sizing segmented; if Resolution: FPS slider row (`FPS`, `mediaFPS`: label left, rounded value right in monospace, slider 1...30 step 1) and stepper `Width` (`mediaWidth`) `160...1600` step 80 `px`; else target size row; checkbox `Loop GIF` (`mediaLoopGIF`, default on).
- **Image**: quick presets row (small buttons `Web`, `Social`, `Docs`); **preview section**; format segmented `JPEG` | `PNG` | `HEIC` | `PDF` (literal labels); compression row; **resize section**; a `DisclosureHeaderRow` titled `More options` (`moreOptions`; chevron rotates 90 deg, 0.18 s ease-in-out; collapsed by default; state not persisted) whose body (padding top 6, indented) holds: profile row, checkbox `Remove metadata` (`mediaStripMetadata`, hidden when the format is PDF), background segmented, watermark section, rename section, checkbox `Keep original modified date` (`preserveDate`).
- **Text**: segmented `Accurate` | `Fast` with the accessibility label `OCR` (`mediaOCRMode`, `mediaOCRAccurate`, `mediaOCRFast`).

**Compression row** (spacing 5): label `Compression` (`mediaQuality`, 11 semibold; compact 10); three equal buttons (spacing 6, height 32 / compact 28, radius 7; selected = accent text, accent @ 16 % fill, accent @ 45 % border; unselected = primary @ 78 % text, control fill, normal border) each with a glyph and label: `Low` (`circle` empty, quality 0.88), `Medium` (`circle.lefthalf.filled`, 0.68), `High` (`circle.fill`, 0.28) (`mediaCompressionLow/Medium/High`); selected = the level nearest to the stored quality (`nearest(to:)`); below it a 10.5 secondary caption: `High quality, large file size` / `Balanced quality and file size` / `Low quality, small file size` (`mediaCompression{Low,Medium,High}Description`). Segoe glyph substitutes for the three circles: U+EA3A (circle outline), U+F1DB (half), U+EA3B (filled) - or draw `Ellipse` shapes.
**Trim row**: two `numberField`s (HStack spacing 10): label 11 semibold (width 62, compact 48; scales to 80 % to fit), a monospaced 14 (compact 13) medium text box 76 x 30 (compact 62 x 28), padding h9, radius 7, control fill + border, then suffix `s` (10.5 secondary). Number format: decimal, 0...1 fraction digits, minimum 0. Labels `Start` / `End` (`mediaStartTime`, `mediaEndTime`).
**Target size row**: integer field (label width 92, compact 74) `Target size` (`mediaTargetSize`) + suffix ` MB` (`mediaMegabytesSuffix`, the literal contains a leading space) with formatter min 1 max 512 no decimals; below: `Resolution adapts to stay under the limit.` (`mediaTargetSizeHint`, 10.5 secondary wrapping). Defaults: video 20 MB, GIF 10 MB.
**Preview section** (HStack spacing 10): a rounded (7) frame sized to the *output aspect ratio* inside a 136 x 92 box (compact 108 x 74): background = white for JPEG/PDF (black when the background option is Black), else Transparent -> primary @ 4.5 %, White -> white, Black -> black; the source thumbnail (128 pt, compact 96) drawn stretched (exact + Stretch), fill-clipped (exact + Fill) or aspect-fit; a 0.8-px border; the live **watermark overlay** (white semibold text 10 pt, compact 8, 1 line, black shadow r2 y1 @ 55 %; logo square side = min(frame side, max(4, side * scale)); overlay padded by `margin * previewSide / outputSide` capped at half the side; opacity = watermark opacity; alignment from the position); placeholder glyph `photo` 32 (compact 26) when no thumbnail. Right: `Preview` (`preview`, 11 semibold) and `Output: <file name>` (`outputName` = "Output"; 10.5 secondary, 2 lines, middle truncation; the next output name, or `Automatic`).
**Resize section** (spacing 7): dropdown `Resize` (`resize`) with `No change`, `Max side`, `Width`, `Height`, `Custom` (`resizeNone/resizeMax/resizeWidth/resizeHeight/resizeExact`); then: Max side -> stepper `Size` `64...20000` step 128 `px`; Width -> stepper `Width` `1...20000` step 64; Height -> stepper `Height` (`height`) same; Custom -> two steppers side by side (`Width`, `Height`) and a segmented `Stretch` | `Fit` | `Fill` (`exactStretch/exactFit/exactFill`).
**Profile row** (spacing 6): dropdown labelled `Profile` (hidden label) with `No profile` (`noProfile`) + saved profile names; trash button (`deleteProfile` tooltip; disabled with none selected); if a profile is selected and differs from the current settings: `Modified` (`profileModified`, pencil glyph, 10 medium secondary); then name text box (`Profile name`, `profileName`) + small buttons `Update` (`updateProfile`, check glyph; disabled with none selected) and `Save new` (`saveAsNew`, plus glyph). Selecting a profile applies all its options. New profile default name `Profile %d` (count + 1) (`profileDefaultNameFormat`). Names are trimmed; empty names become `Profile <n>`; ids de-duplicated.
**Background section**: segmented `Transparent` | `White` | `Black` (`background` label hidden; `backgroundTransparent/White/Black`).
**Watermark section** (spacing 7): dropdown `Watermark` with `Off`, `Text`, `Logo`, `Text + logo` (`watermark`, `watermarkOff/Text/Logo/Both`); when text: text box placeholder `Watermark text` (`watermarkTextPlaceholder`); when logo: row {logo file name or `No logo` (`noLogo`) (11, 1 line, middle truncation), Spacer, small button `Choose logo` (`chooseLogo`, photo glyph) opening an image-only file dialog, and an `xmark.circle.fill` plain clear button when set}; when kind != Off: dropdown `Position` (`position`: `Top left`, `Top right`, `Center`, `Bottom left`, `Bottom right`), stepper `Opacity` `10...100 %` step 5 (shown as integer + `%`), stepper `Margin` `0...2000` step 8 `px`, stepper `Scale` `5...80 %` step 1.
**Rename section**: label `Rename` (`rename`, 11 semibold), text box with placeholder `{name}-{index:03}` and a borderless menu button `{…}` (monospace 11; tooltip/accessibility `Rename`) that appends a token to the pattern; tokens listed: `{name}`, `{index}`, `{index:03}`, `{counter}`, `{date}`, `{time}`, `{datetime}`, `{width}`, `{height}`, `{format}`.

**Action row** (HStack spacing 8): prominent button with glyph `play.fill` (Text tool: `text.viewfinder`) and title by tool: `Compress video` (`mediaStartVideo`), `Make GIF` (`mediaStartGIF`), `Process image` (`mediaStartImage`) or `Convert to PDF` (`mediaStartConvertPDF`, when the image format is PDF), `Extract text` (`mediaStartText`); disabled with no input or while running. Video tool only: button `Edit` (crop glyph; `editButton` from the screenshot strings = "Edit") enabled when exactly one file, not running and not importing, with a mini ring while importing: it checks the file has a video track (else local message `This file has no video track.`), imports the file as a recording take and opens the recorder editor with owner `mediaTools`. While running: button `Cancel` with xmark (`mediaCancel`) -> `media.cancel()`. Compact control size small.

**Status card** (by `MediaServiceState`): idle/ready: only the orange local message when set; running: `Label("Processing", gearshape.2)` (`mediaRunning`, 11.5 semibold) + `NN%` (monospaced 11 secondary) + determinate progress bar; completed: **result card** (below); failed: orange message card with `exclamationmark.triangle.fill`; cancelled: grey `xmark.circle.fill` + `Cancelled.` (`mediaCancelled`). Message cards: 11.5 medium, full width, card style.
**Result card** (spacing 8): `Label("Done", checkmark.circle.fill)` green 11.5 semibold (`mediaCompleted`); when there is an output: line `Saved as %@` (`mediaResultSavedFormat`; 11, 2 lines, middle truncation) or, for a multi-image batch, `%d images saved` / `%d saved, %d failed` (`batchSavedFormat`/`batchPartialFormat`); `%@ to %@` (`mediaResultSizeFormat`: original size to output size, 10.5 secondary); a delta line `%@ saved` or `%@ larger` (`savedBytesFormat`/`grewBytesFormat`, only when delta != 0); if the output is larger than the original (and the original size > 0): `The converted file came out larger than the original.` (`mediaResultGrewCaption`); up to 3 failure lines `<file>: <message>` (10 pt, 3 lines max) for failed batch items; for OCR: the extracted text in a monospaced box (11, 8 lines max / compact 5 lines, selectable, padding 8, radius 7, primary @ 4.5 % fill) or `No text found.` (`mediaEmptyText`). Buttons (small, spacing 8): `Show` (`mediaOpenInFinder`, folder glyph; reveals all output URLs in Explorer) [WIN] keep `Show`; `Copy text` (`mediaCopyText`, copy glyph; OCR only); `Copy summary` (`copySummary`; batch > 1 only; text = header `%d saved, %d failed`, optional delta line, then per item `%@ -> %@` (`batchSummaryItemFormat`) or `<file>: <message>`); `Run again` (`mediaRunAgain`, refresh glyph).

### 7.5.3 Strings (enUS, complete)

Core: `Media`, `Compress videos, convert and process images, make GIFs and extract text locally.`, `Local. No network.`, `Video`, `GIF`, `Image`, `Text`, `Choose file`, `Drop a file here or click to choose one.`, `Output`, `Automatic`, `Destination`, `Compress video`, `Make GIF`, `Process image`, `Convert to PDF`, `Extract text`, `Cancel`, `Start`, `End`, `Compression`, `Low`, `Medium`, `High`, `High quality, large file size`, `Balanced quality and file size`, `Low quality, small file size`, `Size`, `Resolution`, `File size`, `Target size`, `Resolution adapts to stay under the limit.`, ` MB`, `Target size too small for this clip. Trim it or raise the limit.`, `Width`, `FPS`, `Keep audio` (`mediaKeepAudio`, unused in UI), `Codec` (unused), `Format`, `Remove metadata`, `Loop GIF`, `OCR`, `Accurate`, `Fast`, `Language correction` (`mediaLanguageCorrection`, unused in UI), `Extracted text can be copied and saved as TXT.` (`mediaTextOutputNote`, unused), `Processing`, `Done`, `Cancelled.`, `Show`, `Copy text`, `Run again`, `No text found.`, `Saved as %@`, `%@ to %@`, `The converted file came out larger than the original.`, `Choose a file first.`, `This file has no video track.`, `Choose a destination different from the original file.`, `Format not supported by macOS.` (`mediaErrorUnsupported`) -> [WIN] `Format not supported by Windows.`.
Image converter (`MediaImageConverterStrings`): `%d files selected`, `Profile`, `No profile`, `Profile name`, `Save new`, `Update`, `Delete profile`, `Modified`, `Profile %d`, `Web`, `Social`, `Docs`, `Resize`, `No change`, `Max side`, `Width`, `Height`, `Custom`, `Stretch`, `Fit`, `Fill`, `Height`, `Watermark`, `Off`, `Text`, `Logo`, `Text + logo`, `Watermark text`, `No logo`, `Choose logo`, `Position`, `Top left`, `Top right`, `Center`, `Bottom left`, `Bottom right`, `Opacity`, `Margin`, `Scale`, `Rename`, `Preview`, `Output`, `Background`, `Transparent`, `White`, `Black`, `Keep original modified date`, `Save in “Converted” subfolder`, `More options`, `These dimensions are too large to process safely. Choose a smaller size.`, `Copy summary`, `%@ saved`, `%@ larger`, `%d images saved`, `%d saved, %d failed`, `%d saved, %d failed` (summary header), `%@ -> %@`.
From other string tables: `Edit` (screenshot `editButton`), `A GIF can be up to %d seconds long` (`gifTooLongFormat`), `Cancel` (`uninstallerCancel` used as tooltip), `Copy` (`menuCopy`).
Quality-level glyph/labels, extension names `JPEG`, `PNG`, `HEIC`, `PDF` are literals.

### 7.5.4 Settings / persisted state (all `UserDefaults` -> feature store)

| Key | Type | Default | Range / allowed | Used by |
|---|---|---|---|---|
| `mediaLastTool` | string | `videoCompressor` | `videoCompressor`, `gifMaker`, `imageCompressor`, `textExtractor` (invalid -> video) | selected tool (restored on open; not when `preservesServiceState`) |
| `mediaVideoStart` / `mediaVideoEnd` | double | 0 / 0 | seconds; 0 = start / full duration | trim; reset to `0` and the file's duration (rounded to 0.1 s) on every new input |
| `mediaVideoQuality` | double | 0.68 | 0.1...1 (UI sets 0.88 / 0.68 / 0.28) | Video compression |
| `mediaVideoMaxDimension` | int | 1280 | 640...3840 step 320 | Video `Size` |
| `mediaVideoFPS` | double | 30 | unused by the UI (run passes 30) | reserved |
| `mediaVideoKeepAudio` | bool | true | unused by the UI (audio always kept) | reserved |
| `mediaVideoCodec` | string | `h264` | `h264`, `hevc` (UI always H.264) | reserved |
| `mediaVideoSizing` | string | `resolution` | `resolution`, `targetSize` | Video sizing mode |
| `mediaVideoTargetMegabytes` | int | 20 | 1...512 | Video target size |
| `mediaGIFStart` / `mediaGIFEnd` | double | 0 / 0 | seconds | GIF trim |
| `mediaGIFQuality` | double | 0.74 | not shown; run uses fixed 0.74 | per-frame lossy quality |
| `mediaGIFWidth` | int | 720 | 160...1600 step 80 | GIF Width |
| `mediaGIFFPS` | double | 12 | 1...30 | GIF FPS |
| `mediaGIFLoops` | bool | true | | Loop GIF |
| `mediaGIFSizing` | string | `resolution` | `resolution`, `targetSize` | |
| `mediaGIFTargetMegabytes` | int | 10 | 1...512 | |
| `mediaImageQuality` | double | 0.72 | 0.1...1 | Image compression |
| `mediaImageMaxDimension` | int | 1600 | 64...20000 step 128 | Resize: Max side |
| `mediaImageFormat` | string | `jpeg` | `jpeg`, `heic`, `png`, `pdf` (invalid -> jpeg) | |
| `mediaImageStripMetadata` | bool | true | | Remove metadata |
| `mediaImageResizeKind` | string | `maxDimension` | `none`, `maxDimension`, `width`, `height`, `exact` | |
| `mediaImageResizeWidth` / `Height` | int | 1600 / 1200 | 1...20000 step 64 | |
| `mediaImageExactResizeMode` | string | `stretch` | `stretch`, `fit`, `fill` | |
| `mediaImageWatermarkKind` | string | `off` | `off`, `text`, `logo`, `textAndLogo` | |
| `mediaImageWatermarkText` | string | `` | | |
| `mediaImageWatermarkLogoPath` | string | `` | absolute path (not portable across machines) | |
| `mediaImageWatermarkPosition` | string | `bottomRight` | `topLeft`, `topRight`, `center`, `bottomLeft`, `bottomRight` | |
| `mediaImageWatermarkOpacity` | double | 0.45 | 0.1...1 | |
| `mediaImageWatermarkMargin` | int | 32 | 0...2000 | |
| `mediaImageWatermarkScale` | double | 0.18 | 0.05...0.8 | logo side = scale x min(canvas w,h) |
| `mediaImageRenamePattern` | string | `` | empty = `{name}` | |
| `mediaImageBackground` | string | `transparent` | `transparent`, `white`, `black` | |
| `mediaImagePreserveModificationDate` | bool | false | | |
| `mediaImageSaveInSubfolder` | bool | false | | batch only |
| `mediaImageProfiles` | string (JSON) | `[]` | array of `{id, name, options{quality, maxDimension, format, stripMetadata, resizeMode{kind,maxDimension,width,height,exactMode}, watermark{kind,text,logoPath,position,opacity,margin,scale}, renamePattern{rawValue}, background, preserveModificationDate}}` | profiles; "portable" export clears `logoPath` and downgrades logo watermarks (logo -> off, text+logo -> text, or off if the text is blank) |
| `mediaImageSelectedProfileID` | string | `` | | |
| `mediaTextAccurate` | bool | true | | OCR mode |
| `mediaTextLanguageCorrection` | bool | true | not shown; the run always passes `true` | reserved |
| `panelUtilityMedia` | bool | true | | panel row |

### 7.5.5 Behaviour

**Inputs**: choose via a file dialog (multi-select only for Image) or drop. Allowed content types: Video/GIF tools -> movies (`movie, video, mpeg4Movie, quickTimeMovie`); Image/Text tools -> any `image`. A drop is accepted only if every file matches the tool's types (Image: the matching subset is taken, others ignored; if none match, or for single-file tools any file does not match, the service state becomes `failed(unsupported)` = `Format not supported by macOS.`; PDFs dropped on the image tool are rejected on purpose). Single-file tools keep the first file. New inputs reset trim to 0/duration, recompute the output name, clear messages and reset the service state. Duration is loaded asynchronously (`MediaDurationLoading`: one pending request per (url, tool); cancelling keeps it resumable; a finished load is attached to its input/tool and rounded to 0.1 s; durations <= 0 or non-finite are ignored); sets `start = 0`, `end = duration`. Changing the tool or closing the view cancels pending loads. The workspace selection survives panel reopen (`MediaWorkspaceSelection`: inputs, image size, output, `outputWasChosenManually`, tool, duration loader).
**Output naming**: Video `<input base>-compressed.mp4`; GIF `<base>.gif`; Text `<base>-text.txt`; Image `<pattern result>.<ext>` (`jpg`, `heic`, `png`, `pdf`) next to the original (multi: output = the input's folder, or a chosen folder, plus the optional `Converted` subfolder). Base name = input name without extension, trimmed, leading dots dropped (empty -> `Output`). Uniqueness: if the candidate exists or is reserved, append ` 2`, ` 3`, ... to the base (space + counter) until free. Sanitising: replace `/ : \ NUL`, newlines and control chars with `-`, trim whitespace and `.`/`-`/space at both ends, empty -> `Output`, limit to 255 UTF-8 bytes minus extension minus the uniqueness reserve (4 bytes for generated names). Rename tokens replace (in this order): `{name}` (visible base name), `{date}` `yyyyMMdd`, `{time}` `HHmmss`, `{datetime}` `yyyyMMdd-HHmmss` (all in **UTC**, POSIX locale), `{width}` `{height}` (output pixel size), `{format}`/`{ext}` (file extension), `{index}`/`{counter}` (1-based position in the batch), `{index:0N}`/`{counter:0N}` for N = 2...6 (zero padded). Single image: if the user picked a destination and then changes the format, the extension is swapped (with uniqueness) so the chosen folder/name stays. If the user did not pick one, the name is recomputed whenever any image option changes (the preview's `Output:` line follows).
**Choose destination**: single file -> save dialog (filter = output type: mp4, gif, the image type, txt), directory + name preset; multiple images -> folder chooser starting at the current folder. Output equal to input (same file identity, or same resolved path if neither exists) -> `Choose a destination different from the original file.`.
**Run**: no input -> local message `Choose a file first.`; resolve the output (current or default); then call the service. Video options: `start,end` (seconds), `quality`, `maxDimension`, `fps = 30`, `keepAudio = true`, `codec = h264`, `sizing`, `targetBytes = megabytes x 1,000,000` (clamped 1...512 MB; 1000-based on purpose). GIF options: `quality = 0.74`, `width`, `fps`, `loops`, `sizing`, `targetBytes`. Image: all current options; one file -> `compressImage` to the exact output; several -> `processImages` into the output directory. Text: `accurate`, `languageCorrection = true`, languages from the UI language (below).
**Service (`MediaService`)**: one operation at a time (a new run cancels the previous one: token cancelled, child process terminated, OCR request cancelled); state `idle -> ready -> running(progress, message) -> completed(result) | failed(failure) | cancelled`; published on the main queue; stale operation ids are ignored; failures: `noInput`, `noVideoTrack`, `sameOutput`, `unsupported`, `imageTooLarge`, `gifTooLong(maxSeconds)`, `targetTooSmall`, `watermarkUnavailable`, `cancelled`, `failed(message)` -> texts `Choose a file first.`, `This file has no video track.`, `Choose a destination different from the original file.`, `Format not supported by macOS.`, `These dimensions are too large to process safely. Choose a smaller size.`, `A GIF can be up to %d seconds long`, `Target size too small for this clip. Trim it or raise the limit.`, `No logo`, `Cancelled.`, the raw message (or the unsupported text when empty). **Staged output**: every output is written to a private temp location (`itemReplacementDirectory` next to the destination) and atomically moved into place at the end (replacing an existing file only when allowed); the move is refused if the operation was cancelled in the meantime. After commit the file is made visible (hidden attribute cleared unless the name starts with `.`). `MediaResult { tool, inputURL, outputURL(s), originalBytes, outputBytes, elapsed, text, imageBatchItems }`; `outputGrew = original > 0 && output > original`.
**Progress**: `running(progress 0...1)`. Video (resolution mode): driven by time since start over `max(1, trimDuration x 0.75)` capped at 0.95, polled every 0.08 s while the converter runs; video (target mode): per frame, published only when the integer percent changes; GIF: `(frameIndex + 1) / frameCount`; image batch: `(i + 1) / n`; OCR: 0.2 before recognition, then completion.

**Video, resolution mode (`convertVideo`)**: output size `scaledVideoSize(displaySize, maxDimension)` = scale the *display* size (after the preferred transform) so the long side <= maxDimension, never enlarge, each side even (min 2), then each side rounded **down to a multiple of 16** (min 16). Preset chosen by `quality` and the larger output side (avconvert): `quality < 0.4` -> `PresetLowQuality`; HEVC (not exposed): `>= 0.82` -> HEVC highest, `<= 1920` -> HEVC1920x1080, `<= 3840` -> HEVC3840x2160, else highest; H.264: `>= 0.82` -> `PresetHighestQuality`; `< 0.58` -> `PresetMediumQuality`; else by side: `<= 640` -> 640x480, `<= 960` -> 960x540, `<= 1280` -> 1280x720, `<= 1920` -> 1920x1080, else highest; `--multiPass` when quality >= 0.82; `--start`/`--duration` with 3 decimals, POSIX locale. Trim = `sanitizedTrim(start, end, assetDuration)`: start clamped to 0...duration, end = duration when end <= 0, end clamped to start...duration; zero length -> `unsupported`.
**Video, file-size mode (`MediaVideoTargetEncoder`, max 3 passes)**: `targetBytes` -> bitrate budget: `audio = hasAudio ? (targetBits/duration < 1.2 Mbps ? 64 kbps : 128 kbps) : 0`; `budget = floor(targetBytes x 8 x 0.94 / duration) - audio`; `video = budget x min(1, scale)`; must be >= 240,000 bit/s else `targetTooSmall`; pixels affordable = `video / (fps x 0.07)` (fps = source frame rate clamped 1...60, fallback 30); `ratio = min(1, sqrt(affordable / sourcePixels))`; floor ratio so the long edge never drops below 480 px (`min(1, 480/longEdge)`); output = `scaledEvenSize(source, round(longEdge x max(ratio, floor)))`; first pass scale = 1; after each pass, if the file is larger than the target and passes remain, `scale' = min(scale x 0.9, scale x target / actual x 0.94)`; after the last pass still larger -> delete the file and `targetTooSmall`. Encoder: H.264 MP4, explicit average bitrate (video) and AAC (audio), reader/writer pipeline, cancellable, per-frame progress.
**GIF (`makeGIF`)**: trim as above; frames = `ceil(duration x fps)` and must be `<= 300` (`maximumGIFFrames`) else `gifTooLong(maxSeconds = max(1, floor(300 / fps)))`; frame delay = `1 / fps` s; sample time for frame i = `min(trimEnd, trimStart + i / fps)` with **zero** time tolerance and the video transform applied; each frame is scaled so its long side <= `width` (even sizes, never enlarged) and encoded with per-frame lossy quality 0.74; loop count 0 (forever) when `Loop GIF`, else the GIF has no loop extension (plays once). Target-size mode: start `width = clamp(sourceDisplayWidth, 160, 1600)`, `fps = max(6, min(15, floor(300 / duration)))`; after each pass larger than the target: `ratio = min(0.9, target x 0.94 / actual)`, `nextFPS = max(6, round(fps x ratio))`, `remaining = min(1, ratio / (nextFPS / fps))`, `nextWidth = max(160, round(width x sqrt(remaining)))`; stop when neither changes or after 4 passes -> `targetTooSmall`.
**Image (`processImagesWork` / `makeProcessedImage`)**: for each input (continue on per-item failure when > 1 file; with one file the failure is the result): read properties; display size honours EXIF orientation (5...8 swap w/h); target size by resize mode - none: same; max side: `scale = min(1, maxSide / max(w,h))`; width: `outW = width`, `outH = width x h / w` (may enlarge); height: symmetric; exact: `width x height`; sizes rounded and >= 1; **safety**: each side <= 20,000 and area <= 8192 x 8192 (67,108,864 px) else `imageTooLarge`; decode at the minimum pixel size required (for exact: Fit needs `min(hScale, vScale)`, Fill/Stretch need `max(...)`, capped at 1) with orientation applied; draw on a canvas of the target size with high-quality interpolation: background fill (Transparent -> none, but white when the output format is JPEG/PDF; White; Black), image rect = canvas (exact/Stretch or non-exact), or centred aspect-fit / aspect-fill rect (exact Fit / Fill), then the watermark. Encode: JPEG / HEIC / PNG via the image encoder with lossy quality `quality` (0.1...1; ignored by PNG); metadata: when `Remove metadata` is off, merge the source properties (minus orientation, with new pixel width/height) into the output; PDF: re-encode as JPEG at the quality, then a one-page PDF whose media box = the JPEG's pixel size in points (1 px = 1 pt) embedding that JPEG stream. `preserveModificationDate` copies the input's modified time to the output. Batch result `originalBytes`/`outputBytes` = sums over successes; no successes -> the first failure (or `unsupported`) is thrown.
**Watermark drawing** (white text, semibold system font): `side = min(canvasW, canvasH)`; `gap = max(6, side x 0.015)`; logo: fit inside `max(12, side x scale)` square; text font size `max(12, min(96, side x 0.045))`, colour white at `opacity`, shadow blur `max(2, size x 0.14)` offset (0,-1) black @ `0.45 x opacity`; content width = logo + gap (if both) + text; if wider than `canvasW - 2 x margin` everything shrinks by that ratio (font >= 8); origin from the position with `maxX = max(margin, canvasW - contentW - margin)` (same for Y; Cocoa Y is bottom-up so Top* uses `maxY`, Bottom* uses `margin`, Center uses the centred value clamped to >= margin); logo drawn at `opacity`, vertically centred against the text. Logo loading failure when a logo is required -> `watermarkUnavailable` (`No logo`).
**OCR (`extractTextWork`)**: image read (same size safety), recognition level Accurate/Fast, language correction on, recognition languages = the UI language mapping filtered by what the recognizer supports (if none supported, the recognizer default is used): `pt-BR -> [pt-BR, en-US]`, `tr -> [tr-TR, en-US]`, `ru -> [ru-RU, en-US]`, `es -> [es-ES, en-US]`, `de -> [de-DE, en-US]`, `fr -> [fr-FR, en-US]`, `it -> [it-IT, en-US]`, `ja -> [ja-JP, en-US]`, `ko -> [ko-KR, en-US]`, `uk -> [uk-UA, en-US]`, `zh-Hans -> [zh-Hans, en-US]`, `zh-TW, zh-HK -> [zh-Hant, en-US]`, default `[en-US]`. Text = top candidate of each observation joined by `\n`; written (UTF-8, atomic via staging) to `<name>-text.txt` when an output is set (always set by default); `outputBytes` = file size (or text byte count if no file).
**Edit (video)**: only for the Video tool and exactly one file; imports it as a recording take (`RecorderTakeStore.importVideo`), opens the recorder editor; on failure local message `Format not supported by macOS.`; leaving the tool/closing cancels the import and deletes a half-imported take.
**Modal dialogs** (menu/launcher hosts do not activate the app): activate first, run the dialog one turn later, guard against a second dialog while one is open (`panelModalActive`); the island keeps its surface open while a dialog is active. Windows: `ShowDialog` with the owner window, same single-dialog guard.

### 7.5.6 Windows mapping

| Vorssaint | Windows implementation |
|---|---|
| AVFoundation read/trim/export (`avconvert` presets) | **Media Foundation** through `Windows.Media.Transcoding.MediaTranscoder` (`PrepareFileTranscodeAsync(StorageFile, StorageFile, MediaEncodingProfile)` -> `PrepareTranscodeResult.TranscodeAsync()` with `Progress` callback). Set `transcoder.TrimStartTime` / `TrimStopTime` from the trim. Profile: start from `MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto)` (or `HD1080p/HD720p/Wvga` by output side), then set `Video.Width/Height` (the multiple-of-16 size above), `Video.Bitrate`, `Video.FrameRate` (= source fps up to 60), `Audio` AAC `Bitrate`. Quality levels (proposal, tune by test): bits-per-pixel `bpp = 0.16` (Low compression 0.88), `0.10` (Medium 0.68), `0.05` (High 0.28; also any quality < 0.4), `bitrate = clamp(W x H x fps x bpp, 300 kbps, 40 Mbps)`; audio 128 kbps (64 kbps for High compression). HEVC (hidden option, keep `mediaVideoCodec`): `MediaEncodingProfile.CreateHevc(...)` when the HEVC encoder is present (`MediaTranscoder.HardwareAccelerationEnabled = true` first) |
| `MediaVideoTargetEncoder` | same `MediaTranscoder` with a custom profile built from `MediaSupport.videoSizePlan` (port the planner, constants `targetSizeHeadroom 0.94`, `targetBitsPerPixel 0.07`, `minimumTargetVideoBitRate 240000`, floor 480 px, `maximumPasses 3`, `targetRetryScale`); after each pass `FileInfo.Length` decides. Alternative when `ffmpeg.exe` is found: `-b:v <plan> -maxrate -bufsize` two-pass |
| Unsupported containers for MF (WebM, some MKV/AVI codecs) | fallback to `ffmpeg.exe` (existing `FfmpegLocator`: PATH, then `%LOCALAPPDATA%\Trayloom\tools\ffmpeg.exe`, then app folder): `-c:v libx264 -crf <18 + (100 - q%) x 0.23> -preset medium -c:a aac -b:a 128k -movflags +faststart`; never bundle ffmpeg without asking |
| `AVAssetImageGenerator` frames (zero tolerance, transform applied) | `Windows.Media.Editing.MediaComposition` + `MediaClip.CreateFromFileAsync`, `composition.GetThumbnailAsync(TimeSpan, width, height, VideoFramePrecision.NearestFrame)` (apply trim by sampling inside the range); or `MediaPlayer` frame server; or `ffmpeg -ss t -i in -frames:v 1` |
| `CGImageDestination` GIF | preferred: `ffmpeg -vf "fps=<fps>,scale=<w>:-2:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse" -loop 0\|-1` (best quality, much smaller files; this is what Trayloom already does); built-in fallback: `Windows.Graphics.Imaging.BitmapEncoder` with `BitmapEncoder.GifEncoderId`, per-frame `BitmapProperties` `/grctlext/Delay` (UInt16, 1/100 s = `round(100/fps)`), first frame `/appext/Application = "NETSCAPE2.0"` and `/appext/Data = {3,1,0,0,0}` for infinite loop, `/imgdesc/...` left default; quantise with an optimised 256-colour palette per frame (`IWICPalette.InitializeFromBitmap`) - quality is lower than ffmpeg's. A NuGet quantiser (ImageSharp / Magick.NET) is **ask first** |
| `CGImageSource` / ImageIO (decode: JPEG, PNG, HEIC, WebP, TIFF, BMP, GIF, ...) | WIC via `System.Windows.Media.Imaging.BitmapDecoder.Create(...PreservePixelFormat/OnLoad)` (JPEG, PNG, BMP, GIF, TIFF, ICO, JPEG-XR, and any installed codec: HEIC/HEIF need the free **HEIF Image Extensions** (+ **HEVC Video Extensions** for HEVC-coded iPhone photos), WebP needs **WebP Image Extensions**, RAW needs a RAW codec). Detect failures and show `Format not supported by Windows.` plus (optional new string) `Install the HEIF/HEVC image extensions from the Microsoft Store to open this format.` Orientation: read `System.Photo.Orientation` / `/app1/ifd/{ushort=274}` and rotate |
| Render canvas (`NSBitmapImageRep`/`NSGraphicsContext`) | WPF `DrawingVisual` + `RenderTargetBitmap` (Pbgra32) with `RenderOptions.SetBitmapScalingMode(HighQuality)`; background `Brushes.White/Black` or none; watermark with `FormattedText` (Segoe UI Variable, semibold) + `DropShadowEffect`-equivalent (draw the text twice: black offset @ 0.45 x opacity, then white) and `ImageBrush`/`DrawImage` for the logo at `Opacity` |
| Encode JPEG / PNG | `JpegBitmapEncoder { QualityLevel = round(q x 100) }`, `PngBitmapEncoder`; keep/strip metadata: transcode through a `BitmapMetadata` clone when `Remove metadata` is off (drop orientation, set `PixelXDimension/YDimension`), otherwise write a bare frame (existing `ImageProcessor` already flattens alpha onto white for JPEG) |
| Encode HEIC | `Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(BitmapEncoder.HeifEncoderId, stream)` with `ImageQuality` property (`BitmapPropertySet`), available only when the HEIF + HEVC extensions are installed; otherwise disable the `HEIC` segment with tooltip `Needs the HEIF and HEVC extensions from the Microsoft Store` (new string) |
| Encode PDF | no built-in headless writer. Preferred: write the minimal PDF by hand (header `%PDF-1.4`; objects: Catalog, Pages, Page with `/MediaBox [0 0 W H]`, content stream `q W 0 0 H 0 0 cm /Im0 Do Q`, image XObject `/Subtype /Image /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode` carrying the JPEG bytes encoded at the chosen quality; classic xref table + trailer) = zero dependencies and keeps the quality slider meaningful (matches `writePDF`). Alternative: `Microsoft Print to PDF` printer via `PrintDocument` (page size not controllable) - rejected |
| OCR (`VNRecognizeTextRequest`) | `Windows.Media.Ocr.OcrEngine`: `OcrEngine.TryCreateFromLanguage(new Language(tag))` for the first supported language of the mapping above (BCP-47: `en-US`, `pt-BR`, `tr-TR`, `ru-RU`, `es-ES`, `de-DE`, `fr-FR`, `it-IT`, `ja-JP`, `ko-KR`, `uk-UA`, `zh-Hans-CN`, `zh-Hant-TW`) checked against `OcrEngine.AvailableRecognizerLanguages`, falling back to `TryCreateFromUserProfileLanguages()`; input `SoftwareBitmap` (Bgra8, Premultiplied) from `BitmapDecoder.GetSoftwareBitmapAsync`; reject/scale images with a side > `OcrEngine.MaxImageDimension` (10,240); text = `OcrResult.Lines` joined with `\n`. There is no accurate/fast switch and no language-correction control: keep the `Accurate`/`Fast` segmented control for parity but implement Accurate = decode at full resolution (upscale x2 when the long side < 1000 px), Fast = as is; do not expose language correction. Trayloom's `ScreenOcr` feature already wraps this engine - share the helper through `Trayloom.Core`. A missing language pack -> message (new string) `Install the OCR language pack in Settings > Time & language > Language & region.` |
| `NSOpenPanel` / `NSSavePanel` / UTType filters | `OpenFileDialog` (`Multiselect` only for Image) / `SaveFileDialog` / `OpenFolderDialog`; filters: video `*.mp4;*.m4v;*.mov;*.mkv;*.avi;*.wmv;*.webm;*.mpg;*.mpeg;*.3gp;*.ts;*.mts;*.m2ts`, image `*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.heic;*.heif;*.jxr;*.ico`; "conforms to type" = extension list or `HKCR\.<ext>` `PerceivedType` (`video`/`image`) |
| `fileURLsReferToSameItem` | compare `(VolumeSerialNumber, FileIndex)` from `GetFileInformationByHandle`; fall back to `Path.GetFullPath` equality (case-insensitive) when either file does not exist |
| Staged output (`itemReplacementDirectory` + atomic rename) | write to a temp file in the **destination folder** (`<name>.trayloom-tmp`) then `File.Move(tmp, dest, overwrite)` / `File.Replace`; delete the temp on failure/cancel; clear `FileAttributes.Hidden` on the result unless the name starts with `.` |
| Invalid file-name chars | Windows set `< > : " / \ | ? *` + control chars -> `-`; trim trailing space/dot; avoid reserved names (`CON PRN AUX NUL COM1..9 LPT1..9`) by suffixing `-`; limit 255 UTF-16 chars per component and enable long paths |
| Modification date | `File.SetLastWriteTimeUtc(output, File.GetLastWriteTimeUtc(input))` |
| Byte formatting | 1000-based (`ByteCountFormatter`), `MB` suffix for target size is also 1000-based (`targetBytes = MB x 1,000,000`) |
| Drag and drop | WPF `AllowDrop` + `DragEnter/DragLeave` (accent fill/border while hovering) + `DragDrop` `DataFormats.FileDrop`; dropped folders are not expanded (Vorssaint ignores directories) |
| Threading/cancel | one background `Task` with a `CancellationToken`; cancel kills the ffmpeg child (`Kill(true)`) and calls `IAsyncActionWithProgress.Cancel()` on the transcode |

### 7.5.7 Gap vs Trayloom (`MediaTools/MediaToolsFeature.cs`, 596 lines; titled "Media compress")

Has: a list-based batch window (columns File / Before / After / Status), `Add files...`, `Clear`, `Process`, `Stop`, drag-drop of files/folders, global options in the feature settings (format Keep/JPEG/PNG, quality 30-100, longest side presets, output folder), WPF image re-encode (JPEG/PNG, EXIF orientation, alpha flatten on white, skip when no gain) with `-min` suffix naming, optional `ffmpeg.exe` (PATH) for MP4 (libx264 CRF) and GIF (palettegen), size before/after.
Lacks / differs (priority):
1. P0 Whole UI/UX: Vorssaint is a single-file (images: multi) **tool workspace** with a 4-segment tool picker, file card, per-tool option card, action row and status/result card (7.5.2). Trayloom is a job queue grid with global settings in the Settings page. Rebuild as `MediaWorkspaceView` (hosted in Settings and panel).
2. P0 Tools: **Video** (trim, resolution mode with 3 compression levels + max size, file-size target with the planner), **GIF** from video (trim, FPS, width, loop, size target; 300-frame limit), **Image** (format JPEG/PNG/HEIC/PDF, 3 compression levels, resize none/max/width/height/custom + stretch/fit/fill, background, watermark text/logo with position/opacity/margin/scale, rename tokens, profiles, Web/Social/Docs presets, preview, metadata, keep-date, Converted subfolder) and **Text** (OCR). Trayloom has JPEG/PNG + longest-side only, GIF only as GIF->GIF via ffmpeg, no OCR, no PDF/HEIC, no target size, no trim.
3. P0 Video without ffmpeg: Trayloom skips videos unless ffmpeg is on PATH; Vorssaint always works. Implement Media Foundation transcoding (7.5.6) so video/GIF work out of the box; ffmpeg only as a fallback.
4. P1 Result/status UX: determinate progress, `Done` card with `Saved as`, sizes, `saved/larger` delta, `came out larger` caption, batch summary + `Copy summary`, `Show`, `Copy text`, `Run again`, `Cancelled.`, failure messages.
5. P1 Output model: staged atomic output, unique naming with ` 2` suffixes, `Destination` chooser, same-file guard, name sanitising/255-limit, UTC date tokens, per-image rename pattern; Trayloom always writes `<name>-min` and forbids nothing about same file.
6. P1 Persisted settings keys (35 `media*` keys, 7.5.4) and the profile store; Trayloom only has `format/quality/maxSize/outputFolder`.
7. P2 Hub strings (`Media`, `Compress videos, images and GIFs`), panel host + `panelUtilityMedia`, `Local. No network.` note, `OpenWith(paths, tool)` entry for other features.
8. P2 Strings in the old window ("Free up...", "Process", "Add files...") must be replaced by the strings in 7.5.3. Keep "ffmpeg found/not found" as a small caption in the settings page (Windows-specific addition).

---
Open questions for the orchestrator
1. Cleaner deletion policy on Windows (7.1.7): permanent delete for caches/logs/temp vs everything to the Recycle Bin (Vorssaint literal). Spec chooses the hybrid.
2. App updates source pills: the reference image shows a pill only on the Store row, the Swift code draws one on every row (7.3.3). Spec follows the code.
3. NuGet additions that may be needed and are **ask first**: a WinGet COM projection (`Microsoft.WindowsPackageManager.ComInterop`), a GIF quantiser (ImageSharp/Magick.NET), Win2D (not required if WPF `RenderTargetBitmap` is used).
4. Recorder editor dependency for the Video tool's `Edit` button: needs the recorder part's `OpenEditor(path)`; hide the button until that exists.
