# 02c — Settings pages: Clipboard, Explorer cut/paste, Shelf, URL cleaner, Quick tools, Screen capture (screenshot / recorder / OCR / colour), Command Bar, Radial menu

Source of truth: `vorssaint-mac/Sources/Vorssaint/UI/Settings/{ClipboardSettings, ClipboardIgnoredAppsList, CutPasteSettings, ShelfSettings, URLCleanerSettings, QuickToolsSettings, ScreenCaptureSettings, ScreenCaptureToolPicker, ScreenshotSettings, ScreenRecorderSettings, CommandBarSettings, CommandBarAppShortcutsView, RadialMenuSettings, RadialMenuVisualCanvas}.swift`, strings in `Core/*Strings.swift` + `Core/Localization.swift` (English `enUS` blocks), defaults in `Core/Defaults.swift`.
Windows port compared: `winknife/src/**`.
Every page below follows the SPEC_RULES order: 1 Layout, 2 Strings, 3 Settings, 4 Behaviour, 5 Windows mapping, 6 Gap vs WinKnife.

---

## 0. Conventions used by every page

### 0.1 Page chrome (macOS "grouped Form")
Every page is a SwiftUI `Form` with `.formStyle(.grouped)`: a vertical scroll of **rounded cards** ("Section"). A `Section { … } header: { Text }` renders a small secondary-coloured header label above the card; sections without a header are just a card. Inside a card each control is a row separated by hairline dividers. `Text(...).font(.caption).foregroundStyle(.secondary)` directly under a control is a **caption** (12px, secondary colour, wraps) that lives in the *same row group* as the control above (it does draw as its own row/divider in a grouped form, except where the source wraps toggle+caption in a `VStack(spacing: 3)`, which this spec marks "tight caption"). `.caption2` + `.tertiary` = 11px, tertiary colour. Orange captions = warnings; green captions = status. Windows target: WPF cards (`SettingsCard`, radius 8–10, 1px stroke, 12–16 px padding), 12px captions in `TextFillColorSecondary`, 11px in `TextFillColorTertiary`.

A row/section can be hidden when its `AppFeature.<x>.isAvailable` is false (feature "uninstalled" in the Features hub). Windows: same, via `IFeature` installed state.

### 0.2 String convention
* Every English string is verbatim from the `enUS` tables. `Vorssaint` -> `WinKnife` everywhere.
* Strings that contain macOS-only wording are marked **[mac]** and followed by `=> WIN:` with the suggested Windows wording (macOS/Finder/Mac/menu bar/⌘ etc.). Where no WIN: is given the string is OS-neutral and used as is.
* `%d` / `%@` are format placeholders (int / string).

### 0.3 Shared widgets referenced below (used by many pages; built once)
| Widget | Layout | Strings |
|---|---|---|
| **Toggle row** | label left, switch right (`Toggle(title, isOn:)`). Disabled = 40% opacity. | – |
| **ShortcutPreferenceRow(role, isEnabled)** | `HStack`: label (default **"Shortcut"** = `shelfHotkeyLabel`, optional SF symbol/context/status) – Spacer – [**recorder button, width 108**] [**"Reset"** bordered button, disabled when shortcut == role default or row disabled]. Below: orange caption error text OR (while recording) secondary caption. Optionally a `SystemShortcutTakeOverOffer` block (mac only). Optional 11px "super key alternative" caption under the recorder. | Recorder idle title = the shortcut (e.g. `⌃⌥⌘V` -> WIN `Ctrl+Alt+Shift+V`); recording title **"Press keys"**; recording caption **"Press the new shortcut Escape cancels."** (= `shortcutRecording` + " " + `shortcutEscapeHint`; with canClear add " Delete clears."); reset **"Reset"**; invalid **"Use at least Control, Option or Command with a key."** => WIN: "Use at least Ctrl, Alt or Win with a key."; not captured **"Nothing was captured. macOS or another app already uses that combination. Try another one."** => WIN: "Nothing was captured. Windows or another app already uses that combination. Try another one."; conflict **"This shortcut is already used by %@."**; registration failed (orange caption) **"macOS rejected this shortcut. Choose another one."** => WIN: "Windows rejected this shortcut. Choose another one." (shown when `RegisterHotKey` returned FALSE / ERROR_HOTKEY_ALREADY_REGISTERED) ; "None" for an empty shortcut |
| **PermissionRow(kind)** | card row: icon (`checkmark.circle.fill` green / `exclamationmark.circle.fill` orange) + permission name + right caption **"Granted"** (green) / **"Not granted"** (orange); when not granted a second line with small buttons **"Grant access"** and **"Open System Settings…"**. Kinds: Accessibility ("Accessibility"), Screen Recording ("Screen Recording"), Microphone ("Microphone"). | **[mac]** all three. WIN: **none of these permissions exist**; hide the row. Exception: microphone/camera privacy toggles (see pages). |
| **AppBundleList(title, caption, addTitle, removeLabel)** | A `DisclosureGroup`. Header row: title left, count (secondary, monospaced digit) right when non-empty. Collapsed by default when empty, **expanded when it has entries**. Body: one row per app sorted case-insensitively by display name (icon 18x18, name, optional path caption .caption secondary truncated head, [accessory], `minus.circle.fill` secondary borderless button, a11y label = removeLabel); then small bordered button `plus` + addTitle (left aligned); then caption. "Add" opens an app picker sheet. | titles per page |
| **DisclosureHeaderRow** | clickable row: label, `chevron.right` (caption semibold secondary) rotates 90° when open, 0.18 s ease. Content appears beneath, indented ("disclosureIndent"). | – |
| **App picker** | modal sheet listing installed apps (search field **"Search apps"**, **"Cancel"**). Identity stored = macOS bundle id. | WIN: pick from installed apps (Start-menu `.lnk` / UWP list) **and** running processes **and** "Browse… .exe"; identity stored = lower-cased process image name (e.g. `keepass.exe`); AppUserModelID for packaged apps. |
| **Section anchor highlight** | `settingsFormSectionAnchor` briefly outlines a section (radius 10, padding 10) when navigated to from search / hub. | WIN: same (flash a 2px accent border for ~1.5 s). |
| **PermissionRow / Accessibility** | see above | hidden on Windows |

### 0.4 Global-hotkey mapping (macOS -> Windows)
Vorssaint stores a shortcut as `"mod+mod:keyCode"` (e.g. `control+option+command:9`, mac virtual key codes). WinKnife already stores `Ctrl+Alt+Shift+K` strings via `Hotkey.Parse`. Keep the WinKnife format; **do not** import mac key codes. Default mapping (unique, no collisions between the defaults on these pages):

| Role (storage key) | macOS default | WIN default |
|---|---|---|
| clipboard (`clipboardHistoryShortcut`) | ⌃⌥⌘V | Ctrl+Alt+Shift+V |
| pastePlain (`pastePlainShortcut`) | ⇧⌥⌘V | Ctrl+Alt+V (already used by WinKnife) |
| shelf (`shelfShortcut`) | ⌃⌥⌘D | Ctrl+Alt+Shift+D |
| finderRename (`finderRenameShortcut`) | F2 (no modifier) | F2 (no modifier; `requiresModifier:false`) |
| colorPicker | ⌃⌥⌘C | Ctrl+Alt+Shift+C |
| screenOCR | ⌃⌥⌘T | Ctrl+Alt+Shift+T |
| micMute | ⌃⌥⌘M | Ctrl+Alt+Shift+M |
| quitAll / quitAllExceptFront | ⌃⌥⌘Q / ⌃⌥⌘O | Ctrl+Alt+Shift+Q / Ctrl+Alt+Shift+O |
| cameraPreview | ⌃⌥⌘W | Ctrl+Alt+Shift+W |
| quickLauncher | ⌃⌘V | Ctrl+Win+V |
| scratchpad | ⌃⌥⌘N | Ctrl+Alt+Shift+N |
| screenshot / fullScreen / lastCapture / upload / recent captures / clipboard-image | ⌃⌥⌘4 / 3 / E / U / H / P | Ctrl+Alt+Shift+4 / 3 / E / U / H / P |
| screenRecorder | ⌃⌥⌘5 | Ctrl+Alt+Shift+5 |
| radialMenu (legacy / profile default) | ⌃⌥⌘Space | Ctrl+Alt+Shift+Space |
| commandBar | ⌥Space | Alt+Space (RegisterHotKey(MOD_ALT, VK_SPACE) works, PowerToys Run does it; if registration fails fall back to Ctrl+Alt+Space and show the "rejected" caption) |

Conflict detection ("This shortcut is already used by %@.") must cover: every other WinKnife role, window-layout shortcuts, radial-menu profiles, command-bar row shortcuts, and (Windows) a short list of reserved OS combos (Win+L, Ctrl+Alt+Del, Alt+Tab, Win+D…) which the recorder rejects as "not captured".

### 0.5 SF Symbol -> Segoe Fluent Icons (font `Segoe Fluent Icons`, fall back `Segoe MDL2 Assets`)
`≈` = nearest stock glyph, verify in Character Map.
| SF Symbol | Glyph | | SF Symbol | Glyph |
|---|---|---|---|---|
| checkmark.circle.fill | U+EC61 | | exclamationmark.circle.fill | U+E7BA (Warning) ≈ |
| plus | U+E710 | | minus.circle.fill / minus.circle | U+E738 (Remove) in a circle ≈ |
| xmark.circle.fill / xmark.circle | U+E711 | | chevron.up / down | U+E70E / U+E70D |
| chevron.right / forward | U+E76C | | chevron.backward | U+E76B |
| doc.on.clipboard | U+E77F (Paste) | | magnifyingglass | U+E721 |
| square.grid.2x2 | U+E80A (GridView) | | sun.max.fill / moon.fill | U+E706 / U+E708 |
| keyboard | U+E765 | | mic / mic.slash.fill | U+E720 / U+F781 |
| web.camera / camera.viewfinder | U+E722 | | gearshape | U+E713 |
| bubbles.and.sparkles (cleaning) | no stock glyph - use U+EA3A ≈ or an embedded sparkle path | | note.text | U+E70B ≈ (QuickNote) / U+E8A5 Document |
| xmark.app | U+E711 | | tray.and.arrow.down | U+E896 (Download) ≈ |
| checkmark.shield / hand.raised | U+EA18 (Shield) ≈ | | link | U+E71B |
| doc.on.doc | U+E8C8 (Copy) | | arrow.up.forward.app | U+E8A7 (OpenInNewWindow) |
| trash | U+E74D | | folder | U+E8B7 |
| eye.slash | U+ED1A (Hide) ≈ | | externaldrive | U+EDA2 (HardDrive) |
| person.2 | U+E716 | | photo | U+E91B |
| video | U+E714 | | lock.laptopcomputer | U+E72E |
| pin / pin.fill | U+E718 / U+E840 ≈ | | app.badge | U+E71D (AllApps) ≈ |
| terminal | U+E756 | | equal.square (calculator) | U+E8EF |
| arrow.triangle.swap | U+E8AB (Switch) | | line.3.horizontal (drag grip) | U+E700 |
| record.circle | U+E7C8 ≈ | | stop.circle | U+E71A |
| rectangle.stack | U+E8F1 (Library) ≈ | | text.viewfinder (OCR) | U+E8D2 ≈ |
| eyedropper | U+EF3C ≈ | | eject.fill | U+F847 ≈ |
| lock.fill | U+E72E | | display | U+E7F4 |
| desktopcomputer | U+E770 ≈ | | sparkles.tv (screen saver) | U+E7F4 ≈ |
| music.note | U+E8D6 | | playpause / backward / forward | U+E768-E769 / U+E892 / U+E893 |

---
## 1. Clipboard page (`ClipboardSettings` + `ClipboardIgnoredAppsList`)

Sidebar page "Clipboard" (group Clipboard & files). One page hosts three features: **Clipboard history** (`AppFeature.clipboardHistory`), the **"Paste copied images as files"** switch (belongs to `AppFeature.finderCutPaste`) and **Paste as plain text** (`AppFeature.pastePlain`). Each block is shown only when its feature is installed.

### 1.1 Layout (top to bottom)

**Block H — only if Clipboard history installed**

1. **Card H1** (anchor `clipboardHistory`, no header)
   - Toggle **"Save clipboard history"** (`clipboardHistoryEnabled`, default off). Change -> `ClipboardHistoryService.syncWithPreferences()`.
   - caption (secondary): *"Stores copied text so you can reuse it later. Everything stays local and can be cleared anytime."*
   - caption2 (tertiary): *"Everything stays on this Mac. Very large items are ignored."* **[mac]** => WIN: "Everything stays on this PC. Very large items are ignored."
   - only when `enabled && service.isRunning`: green caption with `checkmark.circle.fill` (U+EC61): **"Saving new text"**.
2. **Card H2** — header **"History shortcut"**
   - Toggle **"History shortcut"** (`clipboardHistoryShortcutEnabled`, default **on**), disabled when history off; change -> `syncHotkey()`.
   - ShortcutPreferenceRow(role clipboard), enabled = `enabled && shortcutEnabled`; extra conflict source = window-layout shortcuts; change -> `syncHotkey()`.
   - orange caption "macOS rejected this shortcut. Choose another one." when `enabled && shortcutEnabled && registrationFailed`.
   - caption: *"Opens a quick window with search, pinned items and ⌘1 to ⌘9 shortcuts for pasting into the previous app."* **[mac]** => WIN: "Opens a quick window with search, pinned items and Ctrl+1 to Ctrl+9 shortcuts for pasting into the previous app."
   - **Segmented picker "History layout"** [**List** | **Cards**] (`clipboardHistoryLayout` = `list`|`cards`, default `list`). NOT disabled by history-off ("the window still opens on the saved items").
   - Toggle **"Show source app icons in list view"** (`clipboardHistoryShowSourceAppIcons`, default off). Not disabled.
   - Button (bordered, label with `doc.on.clipboard` U+E77F) **"History shortcut"** -> `showHistoryWindow()`; disabled when `history.entries.isEmpty`.
3. **Card H3** (no header)
   - Toggle **"Show latest copy in the menu bar"** (`clipboardHistoryMenuBarPreview`, default off), disabled when history off.
   - Row: label **"Preview length"** – Spacer – numeric text field (rounded border, width 50, right-aligned, no grouping, clamped 5...50, default 20, key `clipboardHistoryMenuBarPreviewLength`) – secondary text **"characters"**. Disabled unless `enabled && menuBarPreview`.
   - caption: *"Shows a shortened preview of your last copy next to the icon. Click it to open the history."* **[mac]** (menu bar icon) => WIN: "Shows a shortened preview of your last copy in the tray icon tooltip and the taskbar pill. Click it to open the history."
4. **Card H4** (no header) — everything here is `disabled(!enabled)` except where stated
   - Toggle **"Also save copied images and files"** (`clipboardHistoryIncludeImagesFiles`, default **on**) + caption *"Images join the history and files are remembered as links to their location. Pin and paste them like any text item."*
   - Toggle **"Skip text that looks sensitive"** (`clipboardHistoryFilterSensitiveText`, default off) + caption *"May skip ordinary text that resembles passwords or tokens. Content marked as confidential by its source app is always excluded."*
   - **AppBundleList** "Apps to skip" (`clipboardHistoryIgnoredApps`, array of ids, default `[]`): title **"Apps to skip"**, add button **"Add an app…"**, remove a11y **"Remove"**, caption **"Nothing you copy in these apps is saved to the history."**
   - **Picker "Limit"** (menu): options `20, 50, 100, 250, 500, 1000, 10000` (shown as the number, no grouping separator in the data; display via `"\(value)"`) and `0` shown as **"Unlimited"**; key `clipboardHistoryLimit`, default **50**.
   - **Picker "Keep items for"** (menu): options `1, 7, 30, 90, 365, 0` labelled **"1 day", "7 days", "30 days", "90 days", "365 days", "Forever"**; key `clipboardHistoryRetentionDays`, default **0** (Forever).
   - caption: *"Older items are removed even below the limit. Pinned items are always kept."*
5. **Card H5** — own card on purpose (comment in source: it keeps working with capture off; panel entry still opens saved items)
   - Toggle **"Show in panel"** (`panelUtilityClipboard`, default **on**) – whether the clipboard tile appears in the menu-bar/tray quick panel. NOT disabled.
6. **Card H6 — auto-clear** (never disabled by the capture toggle: "emptying the pasteboard is a security setting in its own right")
   - Row: Toggle **"Auto clear clipboard with a delay of"** (`clipboardAutoClearOnDelay`, default off) – numeric text field (rounded border, width 60, clamped **5...3600**, default **20**, key `clipboardAutoClearDelaySeconds`; disabled while the toggle is off) – secondary text **"seconds"**.
   - Toggle **"Clear clipboard on computer sleep"** (`clipboardAutoClearOnSleep`, default off) **[mac]** => WIN: "Clear clipboard when the PC sleeps".
   - Toggle **"Clear clipboard on display sleep"** (`clipboardAutoClearOnDisplaySleep`, default off).
   - Toggle **"Clear clipboard on screen lock"** (`clipboardAutoClearOnScreenLock`, default off).
   - caption: *"Clears the system clipboard only. Items already saved stay in the history."*

**Block P — only if Cut & paste (finderCutPaste) installed**

7. **Card P1** (no header)
   - Toggle **"Paste copied images as files"** (`finderPasteImageAsFile`, default off). Change -> `FinderCutPaste.syncWithPreferences()`.
   - caption: *"When Finder is active, ⌘V saves a copied image as a PNG in the current folder."* **[mac]** => WIN: "When File Explorer is active, Ctrl+V saves a copied image as a PNG in the current folder."
   - when on and Accessibility missing: PermissionRow(accessibility) => WIN: never shown.

**Block PP — only if Paste plain installed**

8. **Card PP** (anchor `pastePlain`, header **"Paste as plain text"**)
   - Toggle **"Paste as plain text"** (`pastePlainEnabled`, default off) -> `PastePlainService.syncWithPreferences()`.
   - caption *"Pastes what you copied without colors, fonts or formatting. The original stays on the clipboard."*
   - ShortcutPreferenceRow(role pastePlain, isEnabled = pastePlainEnabled) -> sync.
   - orange "macOS rejected this shortcut. Choose another one." when enabled && registration failed.
   - PermissionRow(accessibility) when enabled and not granted => WIN: none.

**Block S — only if Clipboard history installed**

9. **Card S (stats)**: one row `HStack`: **"{N}"** `Pinned` · **"{M}"** `Recent` – Spacer – bordered button **"Clear unpinned"** (disabled when no recent entries). N,M are live counts (pinned bold-ish number then secondary label; separator `·` tertiary). Button opens a confirmation alert: title **"Clear unpinned (%d)?"** (%d = recent count), message **"Pinned items stay, and so does anything copied after this. This can’t be undone."**, buttons **"Cancel"** (cancel) and **"Clear unpinned"** (destructive). The snapshot of ids is taken when the button is pressed, so items copied while the alert is open are kept.

On appear / on change the page sanitises `limit`, `retentionDays`, `autoClearDelay`, `menuBarPreviewLength` to allowed values (`limit` and `retentionDays` also call `trimToLimit()`; the delay change does NOT resync the service because the poll reads the value every tick).

### 1.2 Strings (verbatim, English)
`ClipboardFeatureStrings`: title "Clipboard"; enable "Save clipboard history"; caption "Stores copied text so you can reuse it later. Everything stays local and can be cleared anytime."; localNote "Everything stays on this Mac. Very large items are ignored."; skipSensitive "Skip text that looks sensitive"; skipSensitiveCaption "May skip ordinary text that resembles passwords or tokens. Content marked as confidential by its source app is always excluded."; limit "Limit"; limitUnlimited "Unlimited"; showInPanel "Show in panel"; shortcut "History shortcut"; shortcutCaption (above); pinned "Pinned"; recent "Recent"; clearRecent "Clear unpinned"; active "Saving new text"; includeImagesFiles "Also save copied images and files"; includeImagesFilesCaption (above); pasteImageAsFile "Paste copied images as files"; pasteImageAsFileCaption (above); autoClearEnable "Auto clear clipboard with a delay of"; autoClearSecondsSuffix "seconds"; autoClearOnSleep "Clear clipboard on computer sleep"; autoClearOnDisplaySleep "Clear clipboard on display sleep"; autoClearOnScreenLock "Clear clipboard on screen lock"; autoClearCaption (above); menuBarPreview "Show latest copy in the menu bar"; menuBarPreviewCaption (above); menuBarPreviewLength "Preview length"; menuBarPreviewLengthSuffix "characters"; clearRecentConfirmFormat "Clear unpinned (%d)?"; clearRecentConfirmMessage (above); historyLayout "History layout"; historyLayoutCards "Cards"; historyLayoutList "List"; showSourceAppIconsInList "Show source app icons in list view"; cancel "Cancel".
`ClipboardRetentionStrings`: title "Keep items for"; forever "Forever"; oneDay "1 day"; daysFormat "%d days"; caption "Older items are removed even below the limit. Pinned items are always kept."
`ClipboardIgnoredAppsStrings`: listTitle "Apps to skip"; addButton "Add an app…"; removeButton "Remove"; caption "Nothing you copy in these apps is saved to the history."
Common: pastePlainName "Paste as plain text"; pastePlainCaption "Pastes what you copied without colors, fonts or formatting. The original stays on the clipboard."; shortcutUnavailable "macOS rejected this shortcut. Choose another one."

(Other `ClipboardFeatureStrings` used by the History *window*, not this page: disabled "Enable history to start saving copied text.", search "Search copied text", empty "No saved text", noResults "No results", pin/unpin "Pin"/"Unpin", copy "Copy", copied "Copied", delete "Delete item", selectMultiple "Add to pile", unselectMultiple "Remove from pile", pasteSelectedFormat "Paste %d", copySelectedFormat "Copy %d", deleteSelectedFormat "Delete %d", clearSelection "Clear selection", moveUp "Move up", moveDown "Move down", edit "Edit", save "Save", previewLabel "Preview", imageEntryLabel "Image", fileCountFormat "%d files", shortcutHint "Click a row to paste it into the previous app. ⌘-click selects several; ⌘C copies without pasting.", clickRowShortcut "Click row", commandClickShortcut "⌘ Click", clearRecentKeywords "Clear recent", selectShortcutAction "Select".)

### 1.3 Settings table
| Key | Type | Default | Allowed | Effect |
|---|---|---|---|---|
| `clipboardHistoryEnabled` | bool | false | – | Starts/stops the clipboard watcher. |
| `clipboardHistoryShortcutEnabled` | bool | true | – | Registers/unregisters the history hotkey. |
| `clipboardHistoryShortcut` | shortcut | ⌃⌥⌘V (WIN Ctrl+Alt+Shift+V) | modifier+key | Opens the history window. |
| `clipboardHistoryLayout` | enum | `list` | `list`,`cards` (unknown -> list) | Window layout. |
| `clipboardHistoryShowSourceAppIcons` | bool | false | – | Icon of the app a copy came from beside list rows. |
| `clipboardHistoryMenuBarPreview` | bool | false | – | Show latest copy beside the status icon. |
| `clipboardHistoryMenuBarPreviewLength` | int | 20 | 5…50 | Characters before truncation. |
| `clipboardHistoryIncludeImagesFiles` | bool | true | – | Also capture images and file lists. |
| `clipboardHistoryFilterSensitiveText` | bool | false | – | Heuristic skip (see 1.4). |
| `clipboardHistoryIgnoredApps` | [string] | [] | bundle ids (WIN: exe names) | Copies made while these apps were frontmost are never saved. |
| `clipboardHistoryLimit` | int | 50 | 20,50,100,250,500,1000,10000,0(=unlimited) | Max unpinned entries kept (pinned never count). |
| `clipboardHistoryRetentionDays` | int | 0 | 1,7,30,90,365,0(=forever) | Max age of unpinned entries. |
| `panelUtilityClipboard` | bool | true | – | Clipboard tile in the quick panel. |
| `clipboardAutoClearOnDelay` | bool | false | – | Timer clear. |
| `clipboardAutoClearDelaySeconds` | int | 20 | 5…3600 | Seconds of stillness before clear. |
| `clipboardAutoClearOnSleep` / `…OnDisplaySleep` / `…OnScreenLock` | bool | false | – | Event clears. |
| `finderPasteImageAsFile` | bool | false | – | Paste image as PNG into the current folder. |
| `pastePlainEnabled` | bool | false | – | Enables the plain-paste hotkey. |
| `pastePlainShortcut` | shortcut | ⇧⌥⌘V (WIN Ctrl+Alt+V) | – | Plain-paste hotkey. |
| (state) `clipboardHistoryEntries` (JSON, ≤96 MB encoded), `clipboardHistoryQuickPreview` false, `clipboardHistoryWindowWidth/Height` 0.0 | – | – | – | not edited on this page. |

### 1.4 Behaviour
* **Capture**: a 0.8 s timer compares the pasteboard change count; reads happen off the main thread through one serialized lane (never block the UI/hook thread). Text up to 1,000,000 characters / 64 MiB UTF-8 per item; encoded history file capped at 96 MiB. Text rows preview 2,000 chars, tooltip 200. A retention timer (hourly, 600 s tolerance) removes entries older than `retentionDays` (unpinned only). Limit trim runs on every add and when the Limit/Keep pickers change.
* **Never saved** (always): content the source app marked confidential (`org.nspasteboard.ConcealedType` -> WIN `ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory`=0), transient/auto-generated marks (`TransientType`/`AutoGeneratedType` -> WIN: same formats, plus our own writes), copies by an ignored app, and a paste-and-restore (dictation app puts text, pastes, restores previous clipboard within 3 s) is not re-recorded.
* **"Skip text that looks sensitive"**: skips text that matches `name = value`/`--name value` where name contains password|passwd|secret|token|api_key|private_key|authorization (with optional `_key/_access_key/_key_base/_confirmation/_NN` endings), or any single token 20–160 chars without whitespace containing letter+digit+symbol. Web URLs and 8-4-4-4-12 hex GUID identifiers (optionally in braces) are exempt.
* **Ignored apps**: because the pasteboard never says who copied, the app tracks *every app that came to the front since the last poll* (seeded with the current front app) and drops the copy if any is on the list (window < 1 s; erring toward skipping). WIN: use `GetClipboardOwner()` -> `GetWindowThreadProcessId` -> image name for the exact source (more accurate), plus the foreground-window fallback.
* **Images/files**: images stored as PNG (max 24 MP decoded for recognition); files stored as path lists ("files are remembered as links to their location").
* **Auto-clear** (independent of history): 1 s timer (0.2 s tolerance, wall-clock based so a machine that slept past the delay clears on the first tick after waking). Algorithm per tick: read change count; if changed -> remember (count, now); elif count == the count produced by our own clear -> wait; elif `now - lastChangeDate >= delay` -> clear. Turning the option on or launching baselines "now" so something copied seconds earlier is not wiped. Event clears (sleep / display sleep / session lock) each register their own observer and work with the delay off. Clearing writes an empty pasteboard and remembers the resulting change count. Saved history is never touched. Reads never on the main thread (a blocked main thread froze typing system-wide in the original, issue #189).
* **Stats row** updates live; confirmation alert described above.
* **Menu-bar preview**: shortened text of the latest *text* copy, truncated to N characters with an ellipsis, shown next to the status icon; clicking it opens the history window.
* **Paste image as file**: only when the front app is Finder and no text field is focused; clipboard image type is preferred only when no `public.file-url` is present (file copies win); writes `Pasted_Image_yyyyMMdd_HHmmss.png` (en_US_POSIX, local time) into the current Finder folder (de-duplicated " 2", " 3"…), non-image content is passed back as a plain ⌘V.
* **Paste plain**: global hotkey; reads the clipboard as plain text, pastes it with a synthetic ⌘V, then restores the original contents.

### 1.5 Windows mapping
* Listener: `AddClipboardFormatListener` on a message-only window; handle `WM_CLIPBOARDUPDATE`; also compare `GetClipboardSequenceNumber()`. Debounce 0.8 s equivalent; retry `OpenClipboard` with back-off (it can be locked by another process).
* Sensitive/ignored: `ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory`, `CanUploadToCloudClipboard`; owner via `GetClipboardOwner`. Windows' own Win+V history is separate; do not touch it.
* Source app icon: `SHGetFileInfo`/`IShellItemImageFactory` on the exe path.
* Auto-clear: delay = `DispatcherTimer` 1 s + `GetClipboardSequenceNumber`; computer sleep = `SystemEvents.PowerModeChanged`/`WM_POWERBROADCAST` (PBT_APMSUSPEND); display sleep = `RegisterPowerSettingNotification(GUID_CONSOLE_DISPLAY_STATE)`; screen lock = `WTSRegisterSessionNotification` (`WTS_SESSION_LOCK`) or `SystemEvents.SessionSwitch`; clear = `OpenClipboard` + `EmptyClipboard`.
* Menu-bar preview: Windows tray icons cannot show text. Closest: set the `NotifyIcon` tooltip (≤127 chars) to the preview and/or a small always-on-top pill window docked to the taskbar next to the tray (click -> history). Keep the setting names and the 5…50 length.
* Image-as-file: current Explorer folder via `IShellWindows`/`Shell.Application` (match the foreground HWND -> `Document.Folder.Self.Path`); no UIAccess permission row.
* Paste plain: SendInput Ctrl+V after setting CF_UNICODETEXT only; restore afterwards (WinKnife already does).
* PermissionRow (accessibility): remove. If the foreground app is elevated and WinKnife is not, hooks/SendInput are blocked (UIPI) - show a one-line orange note "Run WinKnife as administrator to use this in elevated apps." (new WIN-only string).

### 1.6 Gap vs WinKnife (`WinKnife.Features.Windows/ClipboardHistoryFeature.cs`, `ClipboardHistoryStore.cs`, `ClipboardIO.cs`, `ClipboardPopupWindow.cs`, `PastePlainFeature.cs`)
* WinKnife has: hotkey (default Ctrl+Alt+H), "Items to keep" slider 10–300 (default 50), "Forget items after (days)" slider 0–90, "Remember images" toggle, ignored apps as a free-text comma list, a note, sensitive-format filter always on, popup with pin/search. Paste-plain feature exists with Ctrl+Alt+V + "trim" + "restoreDelay" options (not in Vorssaint).
* Missing / different: master toggle "Save clipboard history" with the "Saving new text" status; **History shortcut** enable toggle; default hotkey should be Ctrl+Alt+Shift+V; **layout picker (List/Cards)** and **source-app icons**; **menu-bar (tray) preview** + length; "Also save copied images and files" (files not supported); **"Skip text that looks sensitive"** heuristic; **ignored-apps disclosure list with app picker, icons and count** (currently text box); **Limit** picker with the exact stops 20/50/100/250/500/1000/10000/Unlimited; **"Keep items for"** picker 1/7/30/90/365/Forever; **Show in panel**; entire **auto-clear card** (delay + sleep/display-sleep/lock); **stats row + "Clear unpinned" confirmation**; "Paste copied images as files"; paste-plain should live inside this page (card "Paste as plain text") with Toggle + Shortcut + caption, and its extra WinKnife options should be dropped or hidden.

---

## 2. Explorer cut & paste + Rename shortcut (`CutPasteSettings`, Finder -> File Explorer)

Sidebar page "Cut & paste" / hub id `finderCutPaste` + `finderRename` (page title in the source's sidebar: "Finder shortcuts" = `FinderRenameFeatureStrings.pageTitle`). Two independent features on one page.

### 2.1 Layout
**Block C — only if `AppFeature.finderCutPaste` installed**
1. **Card C1** (anchor `finderCutPaste`, no header)
   - Toggle **"Cut & paste files in Finder"** (`finderCutPasteEnabled`, default off) -> `FinderCutPaste.syncWithPreferences()`. **[mac]** => WIN: "Cut & paste files in File Explorer".
   - caption *"Use ⌘X to cut and ⌘V to move files and folders in Finder."* **[mac]** => WIN: "Use Ctrl+X to cut and Ctrl+V to move files and folders in File Explorer."
   - if enabled: Toggle **"Show floating panel"** (`finderCutPasteShowHUD`, default **on**) + caption *"Display a floating indicator with the cut files while Finder is active."* **[mac]** => "…while File Explorer is active."
   - if enabled && service.isRunning: green caption `checkmark.circle.fill` **"Ready to cut in Finder"** [mac] => "Ready to cut in File Explorer".
2. **Card C2** header **"How to use"**: two rows each = key-caps (width 56, left) + callout text; row 1 caps `⌘` `X`: *"Select items in Finder and press ⌘X to cut them."*; row 2 caps `⌘` `V`: *"Open the destination folder and press ⌘V to move them there."*; then caption *"In text fields (like when renaming), ⌘X and ⌘V keep working as usual."* [mac] => WIN caps `Ctrl` `X` / `Ctrl` `V`, "Finder" -> "File Explorer".

**Block R — only if `AppFeature.finderRename` installed**
3. **Card R** (anchor `finderRename`, header **"Rename shortcut"**)
   - Toggle **"Use a shortcut to rename"** (`finderRenameEnabled`, default off) -> `FinderRenameService.syncWithPreferences()`.
   - caption *"The shortcut only acts in Finder and leaves text fields alone. F2 works as a regular key; on keyboards where it controls brightness, use Fn-F2 or choose another shortcut."* [mac] => WIN: "The shortcut only acts in File Explorer and leaves text fields alone. F2 works as a regular key; on laptops where it is a media key, use Fn-F2 or choose another shortcut."
   - Row: label **"Rename"** – Spacer – recorder (width 108, **no modifier required**, default `F2`) – **"Reset"** (disabled when default). Disabled when rename off. Then error/recording caption and (mac) take-over offer.
4. **Card N** header **"Permission required"** — only if (cutpaste enabled or rename enabled) and Accessibility missing: PermissionRow(accessibility); if cut&paste enabled also caption *"The first time, macOS asks for permission to control Finder."* [mac] => WIN: remove the whole card.

### 2.2 Strings
cutPasteEnable "Cut & paste files in Finder"; cutPasteEnableCaption "Use ⌘X to cut and ⌘V to move files and folders in Finder."; cutPasteShowHUD "Show floating panel"; cutPasteShowHUDCaption "Display a floating indicator with the cut files while Finder is active."; cutPasteActiveNow "Ready to cut in Finder"; cutPasteHowTitle "How to use"; cutPasteStep1; cutPasteStep2; cutPasteTextNote; cutPasteAutomationNote "The first time, macOS asks for permission to control Finder."; permissionRequired "Permission required". `FinderRenameFeatureStrings`: pageTitle "Finder shortcuts"; hubTitle "Rename shortcut"; hubDescription "Rename the selected file or folder with a shortcut you choose."; enableLabel "Use a shortcut to rename"; caption (above); shortcutLabel "Rename". Conflict (rename recorder) uses `shortcutConflictFormat` with "macOS" as the owner name when a system shortcut owns it (WIN: "Windows").

### 2.3 Settings
| Key | Type | Default | Effect |
|---|---|---|---|
| `finderCutPasteEnabled` | bool | false | Arms the Cut/Paste interceptor. |
| `finderCutPasteShowHUD` | bool | true | Floating HUD of marked items. |
| `finderPasteImageAsFile` | bool | false | (shown on Clipboard page) |
| `finderRenameEnabled` | bool | false | Arms the rename shortcut. |
| `finderRenameShortcut` | shortcut | `F2` (no modifier) | Key that triggers Rename. |

### 2.4 Behaviour
* Cut&paste: a global event tap claims plain ⌘X/⌘V **only while Finder is frontmost and focus is not an editable control** (AX roles AXTextField/AXTextArea/AXComboBox/AXSecureTextField excluded). ⌘X marks the current Finder selection (read via Apple Events off the tap thread); ⌘V moves the marked items into the folder shown in the front Finder window; marks survive until pasted or until a pasteboard change count changes (copying something else cancels). The tap decision is synchronous; slow work (reading selection, moving files) runs off-thread. HUD (floating panel) lists marked item icons+names, and while a move leaves the volume (real copy) shows a progress bar (byte-accurate for regular files by sampling the growing destination size; indeterminate bar with moving item counter when a folder is in the batch); progress only appears if at least one item changes volume. Name clashes get " 2", " 3" before the extension (Finder style). Result `moved/failed` counts are reported.
* Rename: tap lives only while feature on, accessibility granted and session active; the configured key (default F2) is turned into Finder's Rename command **only when Finder is frontmost and focus is not an editable text role**.

### 2.5 Windows mapping
* Explorer **already** cuts/pastes files with Ctrl+X/Ctrl+V and renames with F2, so these two features add: (a) the HUD of pending cut items, (b) a user-chosen rename key, (c) paste-image-as-file. Implementation: `WH_KEYBOARD_LL` is **not needed** for cut/paste; instead observe the clipboard (`AddClipboardFormatListener`) for `CF_HDROP` + registered format `Preferred DropEffect` == `DROPEFFECT_MOVE (2)` while the foreground window class is `CabinetWClass`/`ExploreWClass`; show the HUD with the file icons (`SHGetFileInfo`) and clear it when the clipboard changes or the paste completes (detect by files disappearing from the source / `Preferred DropEffect` cleared). Toggle semantics: "Cut & paste files in File Explorer" = arm this observer + HUD.
* Rename shortcut: `RegisterHotKey` is wrong for plain F2 (would steal F2 globally); use a `WH_KEYBOARD_LL` hook that passes the key through unless the foreground window is Explorer/desktop and the focused UIA element (`AutomationElement.FocusedElement`, control type Edit/Document/ComboBox/Text) is not editable; then `SendInput` F2 (or if the user chose another key, translate it to F2).
* Selection/folder detection: `IShellWindows` + `IShellBrowser`/`IFolderView2` (selected items `GetSelection`, current folder `Folder.Self.Path`) or UIA tree of the Explorer window.
* Elevation: if Explorer runs elevated and WinKnife does not, show the WIN-only note about running as administrator.

### 2.6 Gap vs WinKnife
No Explorer cut/paste or rename feature exists (`grep -i "cut\|rename"` only hits the command bar, hotkey recorder and snippets). Everything on this page is new: both features, their toggles, HUD, rename recorder with no-modifier support (`HotkeyRecorder.cs` currently requires a modifier - needs a `RequiresModifier` flag), and the paste-image-as-file part (also missing).

---
## 3. Shelf page (`ShelfSettings`)

Sidebar page "Shelf" (anchor `shelf`). Always rendered (no `isAvailable` gating inside the page); everything after the first two cards is hidden while the shelf is off.

### 3.1 Layout
1. **Card S1** (no header)
   - Toggle **"Temporary area for dragging files"** (`shelfEnabled`, default off) -> `ShelfService.syncWithPreferences()`.
   - caption *"A floating spot to gather files, images and text, then drag them anywhere later."*
   - Label (caption secondary, icon `checkmark.shield` U+EA18) **"Requires no permissions."** — shown only while NOT (`shelfShortcutEnabled && shelfShortcutAddsFinderSelection`) (adding the Finder selection needs Automation consent).
2. **Card S2** header **"How to use"**: three numbered bullets (a 18x18 accent-colour circle holding the number, white 11px bold rounded digit; text `.callout`, wraps):
   1 *"Open it with the shortcut, or by shaking the mouse during a drag."* 2 *"Drop files, images, links or text onto it to hold them."* 3 *"Drag each item back out to any app when you need it."*
3. **Only if `shelfEnabled`:**
   - **Card S3 (conditional, no header)** — only when the Dynamic Island is on and shows Files (`NotchSupport.showsFiles()`; refreshed on any UserDefaults change): **segmented Picker "Where things open"** [**Dynamic Island** | **Separate window**] (`notchShelf` bool: true = island (default), false = separate). Labels = `notch.title` "Dynamic Island" and `notchEditor.separate` "Separate window", picker label `notchEditor.destinations` "Where things open".
   - **Card S4 (no header)** — the shelf triggers:
     a. Toggle **"Shelf shortcut"** (`shelfShortcutEnabled`, default **on**) -> `syncHotkey()`.
     b. ShortcutPreferenceRow(role shelf, isEnabled = shortcutEnabled) -> `syncHotkey()`.
     c. orange "macOS rejected this shortcut. Choose another one." when shortcutEnabled && hotkey registration failed.
     d. only when shortcutEnabled — tight block: Toggle **"Add the Finder selection with the shortcut"** (`shelfShortcutAddsFinderSelection`, default off) + caption *"With Finder in front, the shortcut opens the shelf with the selected files already in it. With nothing selected, it opens as usual."*; if on, extra caption *"The first time, macOS asks for permission to control Finder."* [mac] => WIN: drop the permission caption.
     e. tight block: Toggle **"Open by shaking the mouse while dragging"** (`shelfShakeToOpen`, default **on**) + caption *"Shake the pointer quickly while holding an item to summon it near the cursor."* -> `syncDragMonitor()`.
     f. tight block: Toggle **"Keep dragged files in the menu bar"** (`shelfDropZoneEnabled`, default **on**) + caption (two variants): normal *"While you drag a file, the shelf appears below the menu bar icon. Whatever you drop is kept right there, in a button you shrink and open with a click that goes away once the shelf is empty."*; when placement == topCenter and the island is NOT on: *"While you drag a file, the shelf appears as a badge at the top center of the screen. Whatever you drop is kept right there, in a badge you open with a click that goes away once the shelf is empty."* -> `syncDragMonitor()`.
     g. only when dropZone on — tight block: **Picker "Position"** (menu) [**Below the menu bar icon** (`menuBar`, default) | **Top center of the screen** (`topCenter`)] (`shelfDockPlacement`), `disabled` while the island is on (island owns top center); change -> `syncDockedShelf()`; when island on, caption *"The Dynamic Island keeps the top center while it is on."*
     h. tight block: Toggle **"Open near a screen edge"** (`shelfEdgeDragEnabled`, default off) + caption *"Drag a file toward the screen edge to peek the shelf in. Drop it there, or pull back and it retreats."* -> `syncDragMonitor()`.
     i. Button (bordered, icon `tray.and.arrow.down` U+E896) **"Open now"** -> `ShelfService.summon()`.
   - **Card S5** header **"After use"**: three tight blocks
     * Toggle **"Close after dropping into another app"** (`shelfCloseAfterDrop`, default off) + caption *"Closes the shelf when the destination accepts the items. The pin in the panel keeps it open."*
     * Toggle **"Remove items after dropping"** (`shelfRemoveAfterDrop`, default **on**) + caption *"Items accepted by another app leave the shelf. Turn this off to keep a copy there. Pinned items always stay."*
     * Toggle **"Clear when closed"** (`shelfClearOnClose`, default off) + caption *"Empties the shelf only when you click its close button. Automatic hiding and collapsing keep the items."*
   - **Card S6** header **"Automatic exceptions"**: if the list is empty -> callout secondary **"No apps added."**; else rows (icon 20x20, name, Spacer, `minus.circle.fill` borderless); sorted by name. Then button **"Add app…"** (icon `plus`, opens app picker excluding already-listed apps) and caption *"Shake and the menu bar drop zone stay off for drags started in these apps. The shortcut and Open now still work."* Key `shelfAutomaticExclusions` ([string], default []). (Not a disclosure; always expanded.)

### 3.2 Settings
| Key | Type | Default | Effect |
|---|---|---|---|
| `shelfEnabled` | bool | false | Master. |
| `shelfShortcutEnabled` | bool | true | Hotkey on/off. |
| `shelfShortcut` | shortcut | ⌃⌥⌘D (WIN Ctrl+Alt+Shift+D) | Open/toggle shelf. |
| `shelfShortcutAddsFinderSelection` | bool | false | Shortcut preloads the selected Explorer files. |
| `shelfShakeToOpen` | bool | true | Shake-to-summon during drag. |
| `shelfDropZoneEnabled` | bool | true | Docked drop target while dragging. |
| `shelfDockPlacement` | enum | `menuBar` | `menuBar` \| `topCenter` (forced to menuBar behaviour while island on). |
| `shelfEdgeDragEnabled` | bool | false | Edge peek. |
| `shelfCloseAfterDrop` | bool | false | |
| `shelfRemoveAfterDrop` | bool | true | |
| `shelfClearOnClose` | bool | false | |
| `shelfAutomaticExclusions` | [string] | [] | Bundle ids of drag sources that never trigger shake/dock/edge. |
| `notchShelf` | bool | true | Files shelf lives inside the island (island-only). |
| `notchEnabled` | bool | false | (island master, read-only here). |

### 3.3 Behaviour (constants from `ShelfSupport`)
* Drag monitoring watches the global mouse-drag stream (only while a drag is in progress). Shake: rapid back-and-forth of the dragged pointer; summons the shelf near the cursor. Edge drag: trigger distance **200 pt** from a screen's outer left/right edge, dwell **0.15 s**, retreat distance **330 pt** (hysteresis); a seam shared by two displays never counts; parking on a Dock icon never triggers. Docked drop zone: dwell 0.15 s inside the collapsed pill's trigger frame (pill ∪ status-item anchor, margin **16 pt**), keep-open margin **32 pt** around the expanded card; top-center badge placement offset 4 pt from the visible-frame top (below the notch safe area if any).
* Accepts files (including file *promises*), images, URLs and text (text capped at 500 chars preview; dropped trees: max 200 leaves, 200,000 chars text, depth 4). Items are dragged back out as real file/URL/text drag sources. Pinned items survive "remove after drop".
* Close button vs automatic hiding differ only for "Clear when closed" (only the explicit close clears).
* Excluded apps are checked against the app that *started* the drag.
* Screenshots can add themselves to the shelf (Screenshot page toggle "Add to the shelf automatically", disabled when the shelf is off).

### 3.4 Windows mapping
* Drop target: a topmost tool-window (`WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`) accepting `IDropTarget` (WPF `AllowDrop`, `DataFormats.FileDrop`, `Bitmap`, `Text`, `UniformResourceLocatorW`); drag-out via `DragDrop.DoDragDrop` with a `DataObject` (FileDrop).
* Drag detection (cursor is dragging something): `GetAsyncKeyState(VK_LBUTTON)` + `WH_MOUSE_LL` hook movement while the button is held; confirm an OLE drag via the transient drag-image window (`DropTargetHelper` / class `SysDragImage`) or by checking the foreground has a mouse capture. Shake = ≥ N direction reversals of Δx within 0.5 s above a speed threshold.
* "Menu bar icon" -> **notification-area (tray) icon**; dock below the tray icon (use `Shell_NotifyIconGetRect`) or top-center of the primary work area. Strings: keep "Keep dragged files in the menu bar" **[mac]** => WIN "Keep dragged files in the tray"; "Below the menu bar icon" => "Below the tray icon"; "Top center of the screen" unchanged; captions that say "menu bar icon" => "tray icon". "Dynamic Island" stays (WinKnife has an island).
* "Add the Finder selection with the shortcut" => "Add the File Explorer selection with the shortcut"; selection via `IShellWindows`/`IFolderView2.GetSelection`.
* "Requires no permissions." is true on Windows for everything (no Automation prompt); show it always (still tie it to the same condition to keep layout identical, or simply always show).
* App exceptions: process image names from the foreground window at mouse-down.

### 3.5 Gap vs WinKnife (`ShelfFeature.cs`, `ShelfWindow.cs`)
WinKnife has: hotkey (Ctrl+Alt+Shift+F), "Show when dragging near the top edge" (autoShow, default on), "Remember shelf contents between sessions", one note. Missing: master toggle "Temporary area for dragging files" (WinKnife's shelf is just a feature on/off), "How to use" card, island destination picker, shortcut enable toggle (+ default D), Explorer-selection option, **shake to open**, **tray drop zone** + position picker, **edge drag** (WinKnife uses top-edge only), "Open now" button, **After use** card (close after drop / remove after drop / clear on close), **Automatic exceptions** list, the "Requires no permissions" line. Default hotkey should become Ctrl+Alt+Shift+D.

---

## 4. URL cleaner page (`URLCleanerSettings` + `Core/URLCleaning.swift`)

### 4.1 Layout
1. **Card U1** (no header)
   - Toggle **"Clean URLs as you copy them"** (`urlCleanerEnabled`, default off) -> `URLCleanerService.syncWithPreferences()`.
   - caption *"Removes tracking parameters from a link the moment it reaches the clipboard."*
   - caption2 tertiary *"Local. No network."*
   - when `enabled && service.isRunning`: green `checkmark.circle.fill` caption **"Active now"**; and when `lastRemoved` is non-empty a secondary caption **"Removed %@"** with the names joined by ", " (what the last automatic clean took out).
2. **Card U2** header **"Cleaning rules"**
   - one **DisclosureGroup per rule group** (`URLCleaning.ruleGroups`): first "All sites" (key `""`), then sites sorted alphabetically, only groups that have entries. Header row: group title (**"All sites"** or the host), Spacer, count label (**"1 parameter"** / **"%d parameters"** = *enabled* count) secondary, then a site-switch borderless button (20x20): `minus.circle` tooltip/a11y **"Turn off every rule for this site"** when ≥1 enabled, `plus.circle` **"Turn on every rule for this site"** when all off.
     Body: a **2-column grid** of checkboxes (one per name; checkbox label = name, truncated middle; checked = enabled); user-added names additionally show a `minus.circle` button with tooltip **"Delete name"**. Under the grid an **add-parameter row**: text field (rounded, prompt **"Parameter name"**, Return submits) + button **"Add"** (disabled while the draft is not a valid name); beneath it caption2 tertiary *"Write the name to the left of the = , like utm_source. A name that matches takes that one parameter out of the link and leaves the rest as it was."*
   - **DisclosureHeaderRow "Add a site"** -> when open (indented): row of two text fields (prompt `example.com` verbatim; prompt **"Parameter name"**) + button **"Add"** (disabled unless both a valid site key and a valid name).
   - caption *"A site attaches these parameters to its own share links to track where the link came from. Switched on, a name is removed when a link is cleaned; switched off, it stays. Names you add can be deleted."*
   - caption *"The list covers a site’s different share paths (the web page, the app, a live room), which is why it is long; a real link usually carries only two to four of them."*
3. **Card U3** header **"Clean now"**
   - Row: text field (rounded, prompt **"Paste a URL"**, a11y same) + borderless `xmark.circle.fill` clear button (22x22, tooltip **"Clear field"**, 35% opacity and disabled when empty).
   - Row: left button **"Paste"** (reads the clipboard text into the field); right prominent button **"Copy"** (disabled when no cleaned result).
   - If the field yields a result: monospaced caption (≤3 lines, middle truncation, selectable) with the cleaned URL, then secondary caption with the message; else tertiary caption: field empty -> **"The clean URL appears here"**, else the message.
   - Messages: copied just now **"Copied."**; not a URL **"Paste a valid URL."**; unchanged **"Nothing to clean."**; rewritten **"URL cleaned."**; names removed **"Removed %@"**.

### 4.2 Strings
urlCleanerEnable, urlCleanerEnableCaption, urlCleanerLocalNote, urlCleanerActiveNow, urlCleanerRemovedFormat, urlCleanerRulesTitle, urlCleanerRulesAllSites "All sites", urlCleanerRulesCountSingular "1 parameter", urlCleanerRulesCountPluralFormat "%d parameters", urlCleanerRulesRemoveSiteButton, urlCleanerRulesRestoreSiteButton, urlCleanerRulesRemoveButton "Delete name", urlCleanerRulesParameterPlaceholder "Parameter name", urlCleanerRulesAddButton "Add", urlCleanerRulesMatchCaption, urlCleanerRulesAddSite "Add a site", urlCleanerRulesCaption, urlCleanerRulesCoverageCaption, urlCleanerManualTitle "Clean now", urlCleanerInputPlaceholder "Paste a URL", urlCleanerClearButton "Clear field", urlCleanerPasteButton "Paste", urlCleanerCopyButton "Copy", urlCleanerOutputPlaceholder "The clean URL appears here", urlCleanerNoURL, urlCleanerNoChange, urlCleanerCleaned, urlCleanerCopied — all verbatim as quoted in 4.1.

### 4.3 Settings
| Key | Type | Default | Format |
|---|---|---|---|
| `urlCleanerEnabled` | bool | false | |
| `urlCleanerCustomParameters` | string | "" | global additions, comma-separated lower-cased names |
| `urlCleanerSiteParameters` | string | "" | `host\|name` tokens, comma-separated (sorted by host then name) |
| `urlCleanerDisabledParameters` | string | "" | `host\|name` tokens of built-ins switched off (empty host = every site, e.g. `\|utm_*`, `\|fbclid`). Stored as a *difference* so names added in later versions still reach edited installs. |

### 4.4 Behaviour — rules and algorithm
* **Global built-ins** (shown in "All sites" in this order: `utm_*` first, then sorted): `utm_*` (matches any name starting `utm_`: utm_source, utm_medium, utm_campaign, utm_term, utm_content, utm_id, utm_name, utm_reader, utm_viz_id, utm_pubreferrer …), and sorted: `__twitter_impression, _hsenc, _hsmi, dclid, fb_action_ids, fb_action_types, fb_source, fbclid, gbraid, gclid, igshid, li_fat_id, mc_cid, mc_eid, mibextid, mkt_tok, msclkid, ttclid, twclid, wbraid, yclid`.
* **Per-host built-ins** (key matches the host or any subdomain): youtube.com & youtu.be: `si, pp, feature, kw`; twitter.com & x.com: `s, t, cn, src, refsrc, ref_src, ref_url`; instagram.com: `igsh`; spotify.com: `si`; reddit.com: `correlation_id, ref_campaign, ref_source, rdt, share_id, _branch_match_id, $deep_link, $3p, $original_url`; tiktok.com: `u_code, preview_pb, _d, _t, _r, timestamp, user_id, share_app_name, share_iid`; bilibili.com: `spm_id_from, from_spmid, from_source, share_source, share_from, share_medium, share_plat, share_tag, share_session_id, msource, refer_from, seid, unique_k, vd_source, plat_id, buvid, bbid, up_id, is_story_h5, timestamp, ts, visit_id, session_id, broadcast_type, is_room_feed`; xiaohongshu.com: `xhsshare, author_share, xsec_source, share_from_user_hidden, shareredid, share_id, exsource, app_version, app_platform, apptime, appuid`. Site rules are additive (open.spotify.com gets spotify.com's set plus global). A host with no rules keeps only global ones.
* **Clean(text)**: trim; must contain no whitespace; must parse as `http`/`https` with a host. Split off the fragment at the first `#`, the query at the first `?` (slice by Unicode scalars). Split query on `&` (keep empty pieces), decode the *name half* (before `=`) percent-encoding (`%75tm_source` -> `utm_source`; undecodable falls back to raw text); match lower-cased name against the effective set (`utm_` prefix unless `|utm_*` is disabled); removed pairs keep first-seen order, no duplicates in the removed list. If the query becomes empty drop the `?`. Original bytes of surviving pairs/fragment are preserved untouched. `Outcome`: not a URL / unchanged (no removals and equal to trimmed input) / rewritten / removed(names).
* **Parameter name rule**: lower-cased, trimmed, non-empty, no whitespace and none of `| , & =`. **Site key rule** (accepts a pasted URL): lower-case, strip scheme, cut at first `/ ? #`, strip `user@`, strip `:port` (not inside `[ipv6]`), strip leading `www.`; must contain a dot, not start/end with `.`, no whitespace/`|`/`,`.
* Adding a name that was switched off earlier re-enables it. Deleting a user name also clears its "off" record. Site switch "off" stores every listed name (user's own included) as disabled; "on" clears the group's disabled record.
* **Automatic rewrite**: poll timer 0.8 s; on each new pasteboard change count, read text/URL; if `canRewritePasteboard(types)` (allowed types: plain text, URL, URL name, legacy string/URL pboard types, link-presentation metadata, RTF/RTFD of the same link, private app notes; **refused** if the clipboard carries image, file/promise, document, concealed/transient marks) clean and rewrite the clipboard with the cleaned link (text + URL; Safari preview metadata handled), declare our own write so history/other services ignore it; record `lastCleaned`/`lastRemoved` for the "Removed …" caption. Manual **Copy** writes the cleaned URL through the same serialized pasteboard lane and marks it as our own.
* Also used by the Command Bar action **"Clean the copied link"**.

### 4.5 Windows mapping
Clipboard listener as in 1.5; refuse rewrite when formats include `CF_BITMAP/CF_DIB(V5)/CF_HDROP/FileGroupDescriptorW/CF_RTF with non-link/ExcludeClipboardContentFromMonitorProcessing/CanIncludeInClipboardHistory=0`. Allowed: `CF_UNICODETEXT`, `CF_TEXT`, `UniformResourceLocator(W)`, `HTML Format` (re-generate or drop), `Rich Text Format` (drop). Write back `CF_UNICODETEXT` + `UniformResourceLocatorW`. No network. Reuse `UrlCleaner.cs` core.

### 4.6 Gap vs WinKnife (`CleanUrlFeature.cs`, `UrlCleaner.cs`)
Core algorithm, three stored strings and rule tables already exist (same storage format). Missing UI: master toggle semantics ("Clean URLs as you copy them" + "Active now" + "Removed …" summary), the **rules card with per-site disclosure groups, counts, checkboxes, per-name delete, site on/off button, add-parameter and add-site rows**, the **"Clean now" manual tester** (paste/clear/copy + outcome messages). WinKnife exposes the three raw strings as text boxes and a "show notification" toggle (not in Vorssaint) - replace with the real UI. Verify WinKnife's `TrackedParameters` includes `utm_*` wildcard row and the full host tables above (the first lines of `UrlCleaner.cs` list them; compare, esp. reddit `$deep_link/$3p/$original_url`, bilibili and xiaohongshu groups).

---
## 5. Quick tools page (`QuickToolsSettings`)

One long page "Quick tools" with up to eight cards, each gated by its `AppFeature` and each with a section anchor (for search/deep-link). Order (top to bottom): **Quick panel (launcher) -> Quick toggles -> Quit apps -> Mute microphone -> Camera preview -> Wallpaper -> Scratchpad -> Cleaning Mode**. Common string `quickToolShortcutToggle` = **"Global shortcut"** (toggle label above each ShortcutPreferenceRow). For every card the orange "macOS rejected this shortcut. Choose another one." appears under the recorder when its toggle is on and registration failed.

### 5.1 Card Q1 — Quick panel / launcher (`AppFeature.quickLauncher`, anchor `quickLauncher`, header **"Quick panel"**)
Rows:
1. Button (bordered; icon `square.grid.2x2` U+E80A) **"Open quick panel"** -> `QuickLauncherService.show()`.
2. caption *"A floating panel with your favorite tools, summoned by a shortcut from anywhere."*
3. caption2 tertiary *"Use the tune button to choose, hide and drag the tools around."*
4. Toggle **"Global shortcut"** (`quickLauncherShortcutEnabled`, default **on**) -> `QuickLauncherService.syncWithPreferences()`.
5. ShortcutPreferenceRow(role quickLauncher, isEnabled = toggle) (default ⌃⌘V; WIN Ctrl+Win+V).
Key `quickLauncherHiddenItems` (string, default "") stores tools hidden from the panel (edited in the panel's tune mode, not here).

### 5.2 Card Q2 — Quick toggles (`AppFeature.quickToggles`, anchor `quickToggles`, header **"Quick toggles"**)
1. Button (bordered) with label depending on the current appearance: dark -> **"Switch to light mode"** + icon `sun.max.fill` (U+E706); light -> **"Switch to dark mode"** + icon `moon.fill` (U+E708). -> `toggleDarkMode()`.
2. *Only when the Mac has a controllable keyboard backlight* (`brightness.keyboardLightEnabled != nil`): Toggle with label **"Keyboard light"** (icon `keyboard` U+E765) bound to on/off; below it a row: slider 0...1 (a11y label "Keyboard light") + right-aligned `NN%` (caption monospaced digit, width 34). Slider drag start/stop notified (`keyboardLightDragChanged`). Refreshed `onAppear` (`refreshKeyboardLight()`). Caption text for this feature exists (`keyboardLightCaption` "Turns the keyboard backlight on or off.") but is not rendered on this page.
3. **DiskExclusionsList** (disclosure "Excluded drives", see 5.2.1).
4. caption *"One-click system actions in the menu bar panel and in the quick panel."* [mac] => WIN: "One-click system actions in the tray panel and in the quick panel."

Other quick-toggle strings (used by the panel / radial menu / command bar, same feature): hubDescription "One-click actions like dark mode and Trash"; darkModeCaption "Changes the appearance of the whole system."; emptyTrashTitle "Empty the Trash" [mac] => "Empty the Recycle Bin"; emptyTrashCaption "Removes everything from the Trash."; emptyTrashConfirmTitle "Empty the Trash?"; emptyTrashConfirmMessage "All items in the Trash will be removed. This cannot be undone."; emptyTrashConfirmButton "Empty the Trash"; ejectTitle "Eject all disks"; ejectCaption "Safely ejects every external disk."; hiddenFilesShow "Show hidden files"; hiddenFilesHide "Hide hidden files"; desktopIconsHide "Hide desktop icons"; desktopIconsShow "Show desktop icons"; finderRestartCaption "The Finder restarts to apply it." [mac] => WIN "Explorer refreshes to apply it."; lockScreenTitle "Lock the screen"; lockScreenCaption "Asks for the password to come back."; displayOffTitle "Turn off the display"; displayOffCaption "The Mac keeps running with the display off." [mac] => "The PC keeps running…"; screenSaverTitle "Start the screen saver"; screenSaverCaption "Starts right away, on every display."; actionFailed "Could not complete."

#### 5.2.1 DiskExclusionsList (inside Q2)
Disclosure titled **"Excluded drives"** (count badge when non-empty; open by default if the list is non-empty), key `diskEjectExcludedVolumes` ([string], default []; entries are volume names, volume UUIDs or mount paths, all matched case-insensitively). Rows: icon `externaldrive` (11px secondary, 16 wide) + name (12px) + `minus.circle.fill` (a11y "Remove"). Footer: when not editing — if connected offerable drives exist a **menu button** (icon `plus`, text **"Add drive…"**) listing each drive name that is not already excluded, a divider, and **"Other drive name…"**; with no candidates a plain **"Add drive…"** button that reveals the custom field. Custom field: text field (placeholder **"Drive or volume name"**, Return adds) + bordered small **"Add"** (disabled when blank) + plain small **"Cancel"**. Caption *"Drives in this list are never unmounted when using Eject all disks."* Candidate drives = mounted, user-visible, removable/ejectable/external, not the root/internal volume.

### 5.3 Card Q3 — Quit apps (`AppFeature.quitApps`, anchor `quitApps`, header **"Quit apps"**)
1. Button (bordered; icon `xmark.app` U+E711) **"Quit all apps"** -> opens the Quit-apps panel (list of open apps with Quit buttons).
2. **QuitAllExceptionsList** — disclosure titled **"Kept apps"** (`quitAppsKeptApps`, [bundle id], default = [Finder, Phone] i.e. the *mandatory* ids). Count badge = rows shown. Rows: icon 18, name, Spacer, then either a `lock.fill` (tertiary) for mandatory ids (Finder, Phone, and the app itself) or a `minus.circle.fill` remove button (a11y "Remove"). Footer small button `plus` **"Add app…"** (app picker, excludes ones already kept) and caption *"Quit all leaves these apps running. Finder, WinKnife and Phone are always kept. You can still quit a kept app from its own row."* (Vorssaint -> WinKnife applied; WIN: "Finder, WinKnife and Phone" -> "File Explorer and WinKnife", see 5.10). Opened by default only if it holds a user-added app. Rows whose app is not installed are hidden.
3. Toggle **"Global shortcut: Quit all apps"** (`quitAppsShortcutEnabled`, default off) + ShortcutPreferenceRow(role quitAll, ⌃⌥⌘Q) + orange rejected text when `quickToggles.shortcutRegistrationFailed`.
4. Toggle **"Global shortcut: Quit all apps except the front one"** (`quitAppsExceptFrontShortcutEnabled`, default off) + ShortcutPreferenceRow(role quitAllExceptFront, ⌃⌥⌘O) + orange rejected text when `othersShortcutRegistrationFailed`.
5. caption *"Lists your open apps to quit one by one, all at once, or all but the one in front. Always shows the list before anything quits."*
Panel strings: quitAllTitle "Quit all apps"; quitOthersTitle "Quit all apps except the front one"; quitAppsHeader "Open apps"; quitAppsQuitAll "Quit all"; quitAppsQuitAllExcept "Quit all except %@"; quitAppsQuitOne "Quit %@"; quitAppsFrontBadge "Front"; quitAppsKeptBadge "Kept"; quitAppsEmpty "No other apps are open."; exceptionsTitle "Kept apps"; exceptionsCaption (above). Section name `quitAppsName` "Quit apps".

### 5.4 Card Q4 — Mute microphone (`AppFeature.micMute`, anchor `micMute`, header **"Mute microphone"**)
1. Button (bordered) label = muted ? **"Unmute microphone"** (icon `mic.slash.fill`) : **"Mute microphone"** (icon `mic`) -> `MicMuteService.toggle()`.
2. if muted: orange caption with `mic.slash.fill`: **"Mic Mute on"**.
3. caption *"Uses each microphone’s mute or input-volume controls. Siri and some apps may still hear you. This is not a privacy lock."* [mac] ("Siri") => WIN: "Uses each microphone’s mute control. Some apps may still hear you. This is not a privacy lock."
4. Toggle **"Show in the menu bar while Mic Mute is on"** (`micMuteMenuBarIndicator`; **registered default = true** even though the SwiftUI fallback is false) [mac] => WIN "Show in the tray while Mic Mute is on" + caption *"The red mic in the menu bar indicates that Mic Mute is on. It does not guarantee that all apps stop capturing audio."* => WIN "…red mic in the tray…".
5. Toggle **"Global shortcut"** (`micMuteShortcutEnabled`, default off) + ShortcutPreferenceRow(role micMute, ⌃⌥⌘M) + orange rejected caption.
State keys: `micMuteActive` false, `micMuteSavedVolume` 0.75 (volume restored on unmute for devices that only expose input volume).

### 5.5 Card Q5 — Camera preview (`AppFeature.cameraPreview`, anchor `cameraPreview`, header **"Camera preview"**)
1. Button (bordered; icon `web.camera` U+E722) **"Open preview"** -> `CameraPreviewService.show()`.
2. caption *"Check how you look before a call"*.
3. Toggle **"Global shortcut"** (`cameraPreviewShortcutEnabled`, default off) + ShortcutPreferenceRow(role cameraPreview, ⌃⌥⌘W) + orange rejected caption.
4. if camera permission == denied: **CameraPermissionRow**: `exclamationmark.circle.fill` orange + **"Camera"** + right orange caption **"Not granted"**; small button **"Open System Settings…"** (goes straight to Settings; no re-prompt once denied). [mac] => WIN: row shown when `Windows.Devices.Enumeration.DeviceAccessInformation.CreateFromDeviceClass(VideoCapture).CurrentStatus == DeniedByUser/DeniedBySystem`; button opens `ms-settings:privacy-webcam`, label "Open Settings…".
Other strings: hubDescription "Opens a floating mirror with your camera"; cameraMenuLabel "Camera"; deniedMessage "Camera access for Vorssaint is turned off in System Settings." => "…for WinKnife is turned off in Settings."; noCameraMessage "No camera detected"; permExplain "Shows your camera only in the preview window, so you can check how you look before a call. Nothing is recorded or leaves your Mac." => "…your PC."

### 5.6 Card Q6 — Wallpaper (`AppFeature.wallpaper`, anchor `wallpaper`, header **"Wallpaper"**)
1. Toggle **"Show on all Spaces"** (`wallpaperApplyAllDisplays`, default **on**) [mac, mislabelled key = apply to all displays] => WIN label **"Show on all displays"**. On: the chosen picture is applied to every display; off: only the display of the picker.
2. Button (bordered; icon `gearshape` U+E713) **"Open Wallpaper settings"** -> `openSystemWallpaperSettings()`. => WIN opens `ms-settings:personalization-background`.
State: `wallpaperFilter` string default `"all"` (picker tab: All / Your pictures / Apple). Picker strings: pageTitle "Wallpaper"; hubDescription "Pick a still wallpaper without opening System Settings"; panelDescription "Pick a still wallpaper without opening System Settings."; filterAll "All"; filterOwn "Your pictures"; filterApple "Apple" (=> WIN "Windows" bundled pictures from `C:\Windows\Web\Wallpaper`); addImage "Add image"; addFolder "Add folder"; removeAdded "Remove"; doneRemoving "Done"; sourceUnavailable "Unavailable"; addImagePrompt "Choose images to keep in Vorssaint’s wallpaper list" (=> WinKnife’s); addFolderPrompt "Choose a folder of images to keep in Vorssaint’s wallpaper list"; emptyAll "No wallpapers found"; emptyOwn "No pictures added yet"; emptyApple "No Apple stills found"; downloading "Downloading…"; downloadFailed "Could not download the wallpaper"; applyFailed "Could not set the wallpaper"; previousPage "Previous"; nextPage "Next".

### 5.7 Card Q7 — Scratchpad (`AppFeature.scratchpad`, anchor `scratchpad`, header **"Scratchpad"**)
1. Button (bordered; icon `note.text`) **"Open scratchpad"** -> `ScratchpadService.show()`.
2. caption *"Quick notes in separate tabs"*.
3. **Picker "Clear on its own"** (menu; `scratchpadRetention`): **"Never"** (`never`, default) / **"After a day unused"** (`day`, 86,400 s) / **"After a week unused"** (`week`, 604,800 s) / **"After a month unused"** (`month`, 30 days). Unknown stored value -> never.
4. caption *"The pad empties itself once the text goes that long without edits."* (checked only when the panel opens, against stored edit dates; no timer.)
5. Toggle **"Close when I click outside"** (`scratchpadCloseOnClickOutside`, default **on**) -> `outsideClickPreferenceDidChange()`.
6. Slider block: row label **"Text size"** + right value (integer, secondary monospaced); slider `scratchpadTextSize` range **10...22**, step **1**, default **13**.
7. Slider block: label **"Pad background"**; slider `scratchpadBackgroundOpacity` range **0...1** step **0.05** default **0.0**; below, a caption row **"Translucent"** (left) / **"Opaque"** (right).
8. Toggle **"Global shortcut"** (`scratchpadShortcutEnabled`, default off) + ShortcutPreferenceRow(role scratchpad, ⌃⌥⌘N) + orange rejected caption.
Notes: max 12 pads (`maximumPadCount`), pad name ≤ 40 chars. Other scratchpad strings: hubDescription "Floating pads for short-lived notes"; placeholder "Type anything. It saves by itself."; copyAll "Copy all"; copied "Copied"; exportAction "Save as file"; exportFailed "The file could not be saved"; loadFailed "Your notes could not be opened. They were left unchanged."; saveFailed "Your notes could not be saved. Copy them elsewhere before quitting."; clearAction "Clear"; keepOpen "Keep open"; newPad "New scratchpad"; padActions "Scratchpad actions"; renamePad "Rename scratchpad"; closePad "Close scratchpad"; saveName "Save"; cancel "Cancel"; deletePadMessageFormat "Delete “%@” and everything in it?"; padLimitFormat "You can keep up to %d scratchpads"; previewFormatting "Show formatting"; editText "Edit text"; taskDone "Done"; taskNotDone "Not done"; markBold/Italic/Strikethrough/Heading/Bullet/Numbered/Quote/Code/Link = "Bold","Italic","Strikethrough","Heading","Bulleted list","Numbered list","Quote","Code","Link"; formatMarks "Formatting".

### 5.8 Card Q8 — Cleaning Mode (`AppFeature.cleaningMode`, anchor `cleaningMode`, header **"Cleaning Mode"** = `cleaningMenuItem`)
1. Button (bordered; icon `bubbles.and.sparkles`) **"Lock keyboard now"** -> `CleaningModeManager.activate()`.
2. caption *"Locks the keyboard so you can clean safely."*
3. Toggle **"Keep screen visible"** (`cleaningModeKeepScreenVisible`, default off).
4. caption *"Shows a discreet indicator in the corner of the screen instead of blacking out content."*

### 5.9 Settings summary (Quick tools)
| Key | Type | Default |
|---|---|---|
| `quickLauncherShortcutEnabled` / `quickLauncherShortcut` | bool / shortcut | true / ⌃⌘V |
| `quickLauncherHiddenItems` | string | "" |
| `diskEjectExcludedVolumes` | [string] | [] |
| `quitAppsKeptApps` | [string] | [Finder, Phone] (mandatory) |
| `quitAppsShortcutEnabled` / `quitAppsShortcut` | bool / shortcut | false / ⌃⌥⌘Q |
| `quitAppsExceptFrontShortcutEnabled` / `quitAppsExceptFrontShortcut` | bool / shortcut | false / ⌃⌥⌘O |
| `micMuteShortcutEnabled` / `micMuteShortcut` | bool / shortcut | false / ⌃⌥⌘M |
| `micMuteMenuBarIndicator` | bool | **true** |
| `micMuteActive` / `micMuteSavedVolume` | bool / float | false / 0.75 |
| `cameraPreviewShortcutEnabled` / `cameraPreviewShortcut` | bool / shortcut | false / ⌃⌥⌘W |
| `wallpaperApplyAllDisplays` / `wallpaperFilter` | bool / string | true / "all" |
| `scratchpadShortcutEnabled` / `scratchpadShortcut` | bool / shortcut | false / ⌃⌥⌘N |
| `scratchpadRetention` | enum | never |
| `scratchpadCloseOnClickOutside` | bool | true |
| `scratchpadBackgroundOpacity` | double 0…1 | 0.0 |
| `scratchpadTextSize` | double 10…22 | 13 |
| `cleaningModeKeepScreenVisible` | bool | false |

### 5.10 Windows mapping (per tool; macOS-only mechanisms and proposed equivalents)
| Tool | macOS mechanism | Windows equivalent |
|---|---|---|
| Dark mode | AppleScript System Events appearance | HKCU `Software\Microsoft\Windows\CurrentVersion\Themes\Personalize` `AppsUseLightTheme` + `SystemUsesLightTheme` (0/1), then `SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, 0, "ImmersiveColorSet")`. Detect current via the same value (the button label flips). |
| Empty Trash (Finder AppleScript) | – | `SHEmptyRecycleBin(NULL, NULL, SHERB_NOCONFIRMATION…)` after our own confirm alert (strings above, "Trash" -> "Recycle Bin"). |
| Hidden files | `defaults write com.apple.finder AppleShowAllFiles` + Finder restart | HKCU `…\Explorer\Advanced` `Hidden` (1 show / 2 hide) (+ `ShowSuperHidden`), then broadcast `WM_SETTINGCHANGE` "ShellState" / refresh Explorer windows (`SHChangeNotify(SHCNE_ASSOCCHANGED)`; `SendMessage(hwnd, WM_COMMAND, 41504)` on `SHELLDLL_DefView` refreshes). |
| Desktop icons | `CreateDesktop` false + Finder restart | Post `WM_COMMAND` 0x7402 to the `SHELLDLL_DefView` under Progman/WorkerW (toggle desktop icons) or HKCU Advanced `HideIcons`. |
| Lock screen | `CGSession -suspend` / lock | `LockWorkStation()` |
| Turn off display | `pmset displaysleepnow` | `SendMessage(HWND_BROADCAST, WM_SYSCOMMAND, SC_MONITORPOWER, 2)` |
| Screen saver | `open -a ScreenSaverEngine` | `SendMessage(GetDesktopWindow(), WM_SYSCOMMAND, SC_SCREENSAVE, 0)` |
| Eject all disks | `diskutil eject` per external volume | enumerate `DriveInfo` removable/USB (`GetDriveType == DRIVE_REMOVABLE` or bus type USB via `IOCTL_STORAGE_QUERY_PROPERTY`), eject with `CM_Request_Device_Eject` / `DeviceIoControl(IOCTL_STORAGE_EJECT_MEDIA)`; skip the exclusion list. |
| Quit all apps | NSRunningApplication.terminate | enumerate visible top-level windows -> owning processes (skip shell, WinKnife, the kept list, services), `PostMessage(WM_CLOSE)` (graceful), never `TerminateProcess` by default; mandatory kept = `explorer.exe` + WinKnife (replace "Finder, Vorssaint and Phone" with "File Explorer and WinKnife": caption => *"Quit all leaves these apps running. File Explorer and WinKnife are always kept. You can still quit a kept app from its own row."*). |
| Mic mute | CoreAudio mute / input volume | `IAudioEndpointVolume::SetMute` on capture endpoints (all active, or default only - WinKnife already has "Mute every microphone"); fall back to master volume 0 saving `micMuteSavedVolume`. Indicator = red mic overlay on tray icon (+ WinKnife's top-right pill). |
| Keyboard backlight | IOKit | Windows has no generic API. Dynamic Lighting (`Windows.Devices.Lights.LampArray`, Win11 23H2+) can switch a LampArray keyboard; otherwise hide the whole row (same rule as the source: shown only when controllable). |
| Camera preview | AVFoundation | `Windows.Media.Capture.MediaCapture` + `MediaPlayerElement`/`CaptureElement` in a round topmost window (WinKnife has it); permission via `DeviceAccessInformation`. |
| Wallpaper | NSWorkspace.setDesktopImageURL per screen | `IDesktopWallpaper` COM (`SetWallpaper(monitorId or null, path)`, `SetPosition`) (WinKnife has `WallpaperApi`). |
| Scratchpad | – | WPF windows (WinKnife has them). |
| Cleaning Mode | CGEventTap that swallows keys (+ black overlay) | low-level keyboard/mouse hooks that swallow events + full-screen black overlay (WinKnife has it, plus "hold Esc 2 s to leave" and auto-timeout, both not in Vorssaint settings). |
| Quick panel | – | WPF flyout (`QuickPanelWindow` exists). |

### 5.11 Gap vs WinKnife
* **Quick panel card**: Vorssaint's launcher page entry (Open quick panel / shortcut toggle default on / tune button hint) — WinKnife has `QuickPanelWindow` but no settings card or shortcut.
* **Quick toggles**: WinKnife settings page has rows with toggles for dark mode / desktop icons / hidden files / file extensions plus "Empty Recycle Bin" and "Lock screen" buttons (the file-extensions toggle, display-off, screen-saver, eject are Windows/Vorssaint extras; Vorssaint's page shows only the dark-mode button, keyboard light, excluded drives and caption). Missing: appearance-aware button label ("Switch to light mode"/"Switch to dark mode" with sun/moon), **Excluded drives list**, panel caption, eject-all-disks, display-off, screen-saver actions in the panel.
* **Quit apps**: no feature in WinKnife (grep: none). Needs the page card, kept-apps list, two shortcuts, panel.
* **Mic mute**: exists (shortcut default?, indicator toggle default true, "Mute every microphone" extra). Missing: the explicit mute/unmute button with "Mic Mute on" status, the "Show in the tray…" caption text, shortcut enable toggle (WinKnife has hotkey only).
* **Camera preview**: exists with hotkey Ctrl+Alt+Shift+K, mirror toggle, size choice (not in Vorssaint). Missing: "Open preview" button, shortcut-enabled toggle (default off) with default W, denied-permission row, caption.
* **Wallpaper**: exists (picker + fit choice + hotkey). Missing: "Show on all displays" toggle and "Open Wallpaper settings" button, filter tabs (All / Your pictures / Windows), add-image/add-folder lists.
* **Scratchpad**: exists with hotkeys only. Missing: Open button + caption, **retention picker**, **close-on-click-outside**, **text size slider**, **background opacity slider**, shortcut-enabled toggle.
* **Cleaning mode**: exists with hotkey + auto-leave choice. Missing: "Lock keyboard now" button wording, caption, **Keep screen visible** toggle.

---
## 6. Screen capture page (`ScreenCaptureSettings`, `ScreenCaptureToolPicker`, `ScreenshotSettings`, `ScreenRecorderSettings`)

One Settings destination hosts four tools that share a single selection overlay: **Screenshot**, **Screen recording**, **Copy text from screen (OCR)**, **Color picker**. A tool picker at the top switches the card set below it. Sidebar entry / page title: "Screen capture" (`screenCaptureTitle`). Each tool is shown only when its `AppFeature` (`screenshot`, `screenRecorder`, `screenOCR`, `colorPicker`) is installed; if no tool is available the page is empty; with a single tool the picker is hidden.

### 6.1 Page skeleton (top to bottom)
1. **Card T (header "Screen capture")**
   a. **Tool picker** (only when ≥ 2 tools): `ViewThatFits`: a **segmented control** (large) of items `[icon + title]` in this order: **Screenshot** (`camera.viewfinder`, U+E722), **Screen recording** (`record.circle`, U+E7C8 ≈), **Copy text from screen** (`text.viewfinder`, U+E8D2 ≈), **Color picker** (`eyedropper`, U+EF3C ≈); falls back to a **drop-down menu** picker when the segments don't fit the column width. Changing the selection navigates (`router.request(tool.feature.settingsDestination)`) so sidebar/search highlight follows. Deep links select the tool from the section anchor (`screenshot`, `screenRecorder`, `screenOCR`, `colorPicker`); on feature-set change an unavailable selection falls back to the first available tool.
   b. **ToolShortcutRows(tool)** (re-created per tool): Toggle labelled with the tool's role title -> **"Screenshot"** / **"Screen recording"** / **"Copy text from screen"** / **"Color picker"**, bound to `screenshotShortcutEnabled` / `recorderShortcutEnabled` / `screenOCRShortcutEnabled` / `colorPickerShortcutEnabled` (all default **off**), change -> `ScreenCaptureService.syncWithPreferences()`. Then ShortcutPreferenceRow(role = screenshot ⌃⌥⌘4 / screenRecorder ⌃⌥⌘5 / screenOCR ⌃⌥⌘T / colorPicker ⌃⌥⌘C, isEnabled = toggle). Then Toggle **"Show capture menu when using keyboard shortcut"** (per tool: `screenshotShowCaptureMenuOnShortcut`, `recorderShowCaptureMenuOnShortcut`, `screenOCRShowCaptureMenuOnShortcut`, `colorPickerShowCaptureMenuOnShortcut`, all default **true**), disabled when the tool's shortcut toggle is off. Then orange "macOS rejected this shortcut. Choose another one." if the tool's hotkey registration failed.
   c. **RecentCapturesShortcutRows** (when Screenshot or Recorder installed): Toggle **"Recent captures"** (`recentCapturesShortcutEnabled`, default off) -> `RecentCaptureService.syncWithPreferences()`, ShortcutPreferenceRow(role recentCaptures, ⌃⌥⌘H) + orange rejected caption. Visible whichever tool is selected.
2. Tool-specific cards (6.2 / 6.3 / 6.4 / 6.5).

**ScreenCaptureTool facts**: `shortcutKey` digits "1"=screenshot, "2"=recording, "3"=text, "4"=color (pressing the digit while the overlay is up switches mode). Dedicated hotkeys open the overlay already on that mode; with "Show capture menu…" off the overlay shows no mode bar and acts only as that tool. A running recording blocks the capture menu (choosing recording again would stop the take); a menu-less tool shortcut can run on top of a recording. `capturesAudio` only for recording.

### 6.2 Screenshot cards (`ScreenshotCaptureSettings`)
**Card SC1 — header "Screenshot"** (anchor `screenshot`)
* Row (two equal-width bordered large buttons, spacing 10): **[camera.viewfinder] "Capture now"** -> `ScreenshotService.capture()`; **[rectangle.stack U+E8F1] "Scrolling capture"** -> `captureScrolling()`.
* caption *"Capture an area, window or the whole screen"*
* Toggle **"Capture the whole screen"** (`screenshotFullScreenShortcutEnabled`, default off) + ShortcutPreferenceRow(role screenshotFullScreen ⌃⌥⌘3) + orange rejected caption.
* Toggle **"Edit latest screenshot"** (`screenshotLastCaptureShortcutEnabled`, default off) + row (role screenshotLastCapture ⌃⌥⌘E) + rejected caption.
* Toggle **"Edit clipboard image"** (`screenshotClipboardShortcutEnabled`, default off) + row (role screenshotClipboard ⌃⌥⌘P) + rejected caption.
* PermissionRow(screenRecording) if not granted => WIN: none.

**Card SC2 — capture behaviour (no header)**
* Toggle **"Freeze the screen while selecting"** (`screenshotFreeze`, default **on**) + caption *"The picture stops while you choose the area, so nothing moves away or changes during the selection."*
* Toggle **"Hide Vorssaint windows"** => **"Hide WinKnife windows"** (`screenshotHideVorssaintWindows`, default **on**).
* **Segmented picker "Delay"** [**Off** | **3 s** | **5 s** | **10 s**] (`screenshotDelay`, values 0,3,5,10, default 0). Unknown -> 0. Countdown HUD shows the remaining seconds each second before the overlay opens (screenshot only; other tools have 0).
* Toggle **"Include the pointer"** (`screenshotIncludePointer`, default off).
* Toggle **"Show the last capture outline"** (`screenshotShowLastRegion`, default **on**) (overlay hint "R repeats the last area").
* Toggle **"Highlight the window under the pointer"** (`screenshotHighlightWindows`, default **on**) + caption *"Turn off the blue window highlight while choosing an area. You can still click to capture a window."*
* **DisclosureGroup "More options"** (collapsed initially) containing:
  - Toggle **"Start selection with the magnifier on"** (`screenshotLoupeStartsOn`, default off).
  - Toggle **"Remember the magnifier’s last zoom"** (`screenshotLoupeRememberZoom`, default off; last value in `screenshotLoupeLastZoom` 1.0).
  - if remember is off: menu Picker **"Default magnifier zoom"** with options `0.5×`, `1×`, `2×`, `4×` (`screenshotLoupeDefaultZoom` double, default 1.0; format number with 0…1 fraction digits + "×").
  - **Segmented picker "Wheel zoom"** [**Fast** | **Step by step**] (`screenshotLoupeSteppedZoomByDefault`, default false=Fast).
  - caption *"Hold ⌥ to temporarily use the other mode."* [mac] => WIN "Hold Alt to temporarily use the other mode."
  - menu Picker **"Preview position"** [**Automatic** (`""`, default) | **Top left** | **Top right** | **Bottom left** | **Bottom right**] (`screenshotPreviewPosition`: `""|topLeft|topRight|bottomLeft|bottomRight`).
  - Toggle **"Focus the preview automatically"** (`screenshotPreviewTakesFocus`, default **on**) + caption *"Shortcuts work the moment the preview appears, but the keyboard leaves the app you were using until it closes."*
  - **Default action** block: menu Picker **"Default action"** [**Ask each time** (`""`, default) | **Save** | **Save & Copy** | **Copy** | **Edit**] (`screenshotDefaultAction`: `""|save|saveAndCopy|copy|edit`). Below it, unless the value is `edit`, caption *"Runs automatically right after capture."* (`defaultActionCaption`). If the action is save / saveAndCopy / copy: Toggle **"Show confirmation preview"** (`screenshotPreviewEnabled`, default **on**); when on, menu Picker **"Confirmation duration"** [**1 s, 2 s, 3 s, 5 s, 10 s, Until dismissed**] (`screenshotPreviewDuration`: 1,2,3,5,10,0; default **3**); then caption *"After a successful automatic action, keep the preview available for editing or discarding."*

**Card SC3 — output (no header)**
* Toggle **"Copy to the clipboard automatically"** (computed: on if `screenshotCopyToClipboard` OR default action copies; setting off clears both, converting saveAndCopy->save and copy->ask) + caption *"Every capture goes to the clipboard as soon as it is taken, ready to paste. Saving a file stays a separate choice."*
* Toggle **"Copy as image only"** (`screenshotCopyImageOnly`, default off) + caption *"Helps images paste into browsers and chat apps. Turn off for apps that need a file path."*
* Toggle **"Add to the shelf automatically"** (`screenshotAddToShelf`, default off; displays off and is disabled while the shelf is off or not installed, the stored choice is kept) + caption *"Every capture also goes to the shelf, ready to drag into any app. One that opens in the editor goes there with Add to Shelf. Works while the shelf is on."*
* **Folder row**: "Save to" – Spacer – current folder display name (secondary, middle-truncated; Desktop when unset) – `xmark.circle.fill` clear button (tooltip "Reset"; only when a folder is set) – bordered **"Choose…"** (folder picker allowing creation of folders). Key `screenshotSaveFolder` (path string, "" = Desktop).
* **Subfolder row** (tight): label **"Subfolder pattern"** – Spacer – live expansion preview (secondary) when non-empty – text field width 150 (`screenshotSaveSubfolder`, default ""); caption *"Optional. Creates dated subfolders inside the folder above using %y, %year, %mo, %month, %d, %h, %mi and %s, for example %y-%mo for 24-03."*
* **File name row**: label **"File name"** – Spacer – live preview (secondary; default pattern shows `Screenshot <date>.png`-style name from `fileNamePrefix` "Screenshot", else expanded pattern + ".png") – text field width 150 (`screenshotFileNamePattern`, default ""); caption *"Optional. Overrides the default name using the same %y, %year, %mo, %month, %d, %h, %mi and %s tokens, plus %# for an auto-incrementing number (%## pads to 2 digits, %### to 3, and so on)."* If the pattern contains `%#`: an extra row **"Starts at"** + numeric field (width 70, 0…999,999, `screenshotFileNumberStart` default 1) + stepper + bordered **"Reset"** (sets next = start) – Spacer – **"Next: %d"** (`screenshotFileNumberNext` default 1; changing start also sets next).
* Toggle **"Save at 1x size"** (`screenshotDownscale`, default off) + caption *"Retina captures are saved at half their pixel size, which makes smaller files."* [mac] (Retina) => WIN "High-DPI captures are saved at their logical size (96-DPI-equivalent), which makes smaller files." (apply when display scale > 100%).

**Card SC4 — header "Editor tools"** = `ScreenshotToolOrderControls(showsTitle:false)`
* Toggle **"Use tool shortcuts"** (`screenshotToolShortcutsEnabled`, default **on**) + caption *"Click a shortcut to record a key. Digits 1–9 move the tool. Delete clears its shortcut."*
* A list (dividers inset 28) of the 15 tools in current order (`screenshotToolOrder`, comma-separated ids; default order = `select,arrow,pixelate,crop,text,sticker,rect,highlight,freehand,line,ellipse,counter,redact,spotlight,expandCanvas`; the `image` tool is never listed). Row: tool icon (20 wide) + title + Spacer + [per-tool reset button `arrow.counterclockwise`, 20x22, tooltip "Reset", only when the tool has a custom key] + **key recorder (width 86; empty state shows the position digit 1–9 for the first nine tools, or "None"; no modifier required)** + `chevron.up` / `chevron.down` borderless buttons (a11y "Move up"/"Move down"; disabled at ends). Min row height 26.
  Titles/SF symbols: Select `cursorarrow`; Arrow `arrow.up.right`; Blur (pixelate) `aqi.medium`; Crop `crop`; Text `textformat`; Sticker `face.smiling`; Rectangle `rectangle`; Highlighter `highlighter`; Pen `scribble.variable`; Line `line.diagonal`; Ellipse `circle`; Number `1.circle`; Solid block (redact) `rectangle.fill`; Spotlight `viewfinder`; Expand canvas `arrow.up.left.and.arrow.down.right`.
* A fixed 32-px-high message area: while recording shows "Press the new shortcut Escape cancels. Delete clears."; errors in orange: `"<Tool> · <shortcut>\n<reason>"` where reason = **"This key belongs to the editor."** (reserved: ⌘C/V/S/Z/P/0/1/=/-/⌫/W/Q, Esc, Return, Enter, Delete without modifier) | `This shortcut is already used by %@.` (another tool / another role / a window-layout shortcut / "macOS").
* Right-aligned **"Reset"** button (restores default order and clears all custom keys; disabled when already default).
* Rules: a recorded digit 1–9 *moves the tool to that position* (no conflict check); clearing a key moves the tool below the first nine if it was position-based; other keys are bound to the tool and survive reordering (`screenshotToolShortcuts`: `toolId=storageValue,…`); duplicate bindings are dropped on load; a layout/Caps-Lock change that makes a bound key type a digit suspends (not erases) it.

**Card SC5 — header "Temporary links"** (`shareSectionTitle`)
* Toggle **"Allow temporary links"** (`screenshotSharingEnabled`, default **on**) -> `syncWithPreferences()`.
* if on: Toggle **"Upload latest screenshot"** (`screenshotUploadShortcutEnabled`, default off) -> sync; ShortcutPreferenceRow(role screenshotUpload ⌃⌥⌘U); menu Picker **"Default link expiry"** [**For 1 hour** (3600, default) | **For 6 hours** (21600) | **For 24 hours** (86400)] (`screenshotUploadDuration`); orange rejected caption; caption *"Choose 1, 6 or 24 hours when sharing. The image is deleted automatically."*
* Button **[hand.raised] "Privacy"** -> sheet (560x390): header icon+**"Privacy for temporary links"** + **"Done"** default button; three paragraphs (icon + text): `photo`: *"WinKnife sends only the rendered image and the expiration you choose. It does not send your name, account, device identifier or MAC address. On this PC, it keeps only the link, expiration and private deletion token while the link is active."* ; `externaldrive`: *"Network providers and the service temporarily process your public IP to prevent abuse. The image and its link metadata are permanently deleted when you delete the link or its time ends. The service does not create screenshot backups."* ; `person.2`: *"Anyone with the link can view, download, save or redistribute the image. Active links are available to the service operator for abuse moderation. Share only with people you trust."* (Vorssaint -> WinKnife, "On this Mac" -> "On this PC".)
* if any records: row **[link] "Shared links"** – Spacer – count – `chevron.right` -> sheet (560x360) titled **"Shared links"** with **"Done"**: empty state **"No active links"**; list rows: `link` icon, URL (middle-truncated, selectable), `Expires <relative time>` caption; buttons: copy (`doc.on.doc`, tooltip **"Copy link"**), open (`arrow.up.forward.app`, tooltip **"Open"**), delete (`trash`, tooltip **"Delete now"**, spinner while deleting, disables the others). Success HUD **"Link deleted"**; failure alert **"The link could not be deleted"** + **"Done"**. `sharing.refresh()` on appear.
* Other share strings: shareButton "Share", shareOneHour "For 1 hour", shareSixHours "For 6 hours", shareTwentyFourHours "For 24 hours", sharingHUD "Creating link…", sharedHUD "Link copied", shareFailedHUD "The link could not be created", linkCopyFailedHUD "Link created, but copying failed. Copy it from Temporary links in Settings.", expiresLabel "Expires".

### 6.3 Screen recording cards (`ScreenRecordingCaptureSettings`)
**Card R1 — header "Screen recording"** (anchor `screenRecorder`)
* Button (bordered) toggles: idle **[record.circle] "Record now"**; recording **[stop.circle] "Stop recording"** -> `ScreenRecorderService.toggle()`.
* caption: idle *"Record an area, window or the whole screen"*; while recording the elapsed time `m:ss` / `h:mm:ss` (monospaced digits, via `elapsedLabel`).
* PermissionRow(screenRecording) and PermissionRow(accessibility) when missing (accessibility = click/typing tracking for auto-zoom) => WIN: none.

**Card R2 — behaviour (no header)**
* **Segmented picker "Countdown"** [**Off** | **3 s** | **5 s** | **10 s**] (`recorderCountdown`, default **3**; values 0,3,5,10).
* tight: Toggle **"Record the sound of the Mac"** (`recorderSystemAudio`, default **on**) [mac] => WIN "Record the sound of the PC" + caption *"Everything you hear goes into the recording, on its own track, so you can silence it later."*
* tight: Toggle **"Record the microphone"** (`recorderMicrophone`, default off; turning on while permission is undetermined triggers the permission request) + caption *"Your voice goes into its own track and stays adjustable in the editor."*; if on and mic permission ≠ granted: PermissionRow(microphone) (name "Microphone") => WIN: row only if Settings > Privacy > Microphone blocks desktop apps (`ms-settings:privacy-microphone`).
* tight: Toggle **"Open the editor after recording"** (`recorderOpenEditor`, default **on**) + caption *"The recording opens ready to trim, mute and save. Turn this off to get the file straight away."*
* tight: Toggle **"Add zooms automatically"** (`recorderAutomaticZoom`, default **on**) + caption *"Turn this off to start new recordings without zooms. You can still add them in the editor."*

**Card R3 — output (no header)**
* **Folder row** identical to screenshots: "Save to" … "Choose…" (`recorderSaveFolder`, "" = Desktop).
* **DisclosureHeaderRow "More options"** (state not persisted; closed on open). When open (indented):
  - Picker **"Quality"** [**Small file** (`small`) | **Balanced** (`balanced`, default) | **High** (`high`)] + caption *"Balanced fits most uses. High keeps every detail and makes bigger files."* (`recorderQuality`)
  - **Segmented "Frames per second"** [**30 fps** | **60 fps**] (`recorderFrameRate`, default **60**)
  - **Segmented "GIF size"** [**Small** | **Medium** | **Large**] (`recorderGIFSize`: small|medium|large, default **medium**; long edge 420 / 600 / 800 px)
  - **Segmented "GIF smoothness"** [**8 fps** | **12 fps** | **15 fps**] (`recorderGIFFrameRate`, default **12**)

**Card R4 — header "Temporary links"** (shares the screenshot strings)
* Toggle **"Allow temporary links"** (`recorderSharingEnabled`, default **on**); if on, caption *"Choose 1 or 6 hours. The final video is compressed on this Mac to fit under 100 MB and deleted automatically."* [mac] => "…on this PC…".
* Button **"Privacy"** -> sheet (same layout) with paragraphs `video`: *"WinKnife sends only the final video created from this recording, including the audio you kept, and the expiration you choose. It does not send your name, account or device identifier."* ; `externaldrive`: *"Network providers and the service temporarily process your public IP to prevent abuse. The video and link metadata are permanently deleted when you delete the link or its time ends. The service does not create backups."* ; `person.2`: *"Anyone with the link can view, download, save or redistribute the video. Active links are available to the service operator for abuse moderation. Share only with people you trust."*
* Shared-links row/sheet identical (recording records).
* Share strings: compressing "Compressing for sharing…"; uploading "Uploading securely…"; tooLarge "This recording cannot fit under 100 MB without losing too much quality."; failed "The temporary link could not be created"; tourCaption "Compress a finished recording on this Mac and share it for 1 or 6 hours."

Recorder editor/other strings (for the recorder spec, listed for completeness): hubDescription "Records an area, window or screen and edits it afterwards"; selectionPurpose "Choose what to record"; indicatorTooltip "Recording controls"; fileNamePrefix "Recording"; copyButton "Copy"; copyGIFButton "Copy as GIF"; saveButton "Save"; discardButton "Delete"; copiedHUD "Recording copied"; savedHUDFormat "Saved to %@"; recordFailed "The screen could not be recorded"; noSpaceTitle "Not enough space to record"; noSpaceMessage "Free up some space on the disk and try again."; stoppedNoSpaceHUD "Recording stopped, the disk is almost full"; pauseButton "Pause recording"; resumeButton "Resume recording"; editorTitle "Recording"; saveGIFButton "Save as GIF"; exportingLabel "Saving…"; exportFailed "The recording could not be saved"; gifTooLongFormat "A GIF can be up to %d seconds long"; discardTitle "Delete this recording?"; discardMessage "It has not been saved anywhere yet."; discardSavedMessage "Saved and copied files will stay where they are."; microphonePermissionName "Microphone"; microphonePermissionExplain "Lets screen recordings include your voice when you turn it on."; lookLabel "Look" (Original / Smooth / Studio); caption "A starting point. Change anything below and it stays changed."; pointer, zoom, background, text, blur, image lanes etc.

### 6.4 "Copy text from screen" card (`ScreenTextCaptureSettings`, header `ocrName`, anchor `screenOCR`)
* Button (bordered; icon `text.viewfinder`) **"Copy text from screen"** -> `ScreenTextService.capture()`.
* caption *"Select an area of the screen and the recognized text is copied, ready to paste."*
* Toggle **"Remove line breaks"** (`screenOCRRemoveLineBreaks`, default off) + caption *"Removes line breaks so copied text pastes as one paragraph."*
* Toggle **"Read QR codes"** (`screenOCRDetectQRCodes`, default **on**) + caption *"If the area has a QR code, its content is shown to copy or open."*
* PermissionRow(screenRecording) if missing => WIN: none.
Result strings: ocrCopied "Text copied"; ocrNoText "No text found"; ocrQRCopied "QR code copied".

### 6.5 Color picker card (`ColorCaptureSettings`, header `colorPickerName` "Color picker", anchor `colorPicker`)
* Button (bordered; icon `eyedropper`) **"Pick color"** -> `ColorSamplerService.pick()`.
* caption *"Grab the color of any pixel on screen and copy it in your favorite format."*
* **Segmented picker "Copied format"** [**HEX** | **RGB** | **HSL** | **SwiftUI**] (`colorPickerFormat` = `hex|rgb|hsl|swiftui`, default `hex`; labels are intentionally not localized).
* only when format == hex: Toggle **"Copy without the # prefix"** (`colorPickerBareHex`, default off).
Output formats: hex `#RRGGBB` (uppercase; bare = `RRGGBB`; alpha form appends `AA`), rgb `rgb(r, g, b)` / `rgba(r, g, b, a)`, hsl `hsl(h, s%, l%)` / `hsla(…)`, swiftui `Color(red: 0.000, green: 0.000, blue: 0.000)` / `Color(…, opacity: 0.000)` (POSIX locale, 3 decimals). Components clamped 0…1.

### 6.6 Settings summary (screen capture)
All keys are listed inline above with defaults. Additional state keys not on the page: `screenshotLastTool` "arrow", `screenshotLastColor` "red", `screenshotLastStroke` "medium", `screenshotLastTextSize` 19, `screenshotLastBlurLevel`, `screenshotLastBlurStyle` "pixelate", `screenshotLastBlurTextOnly` false, `screenshotSpotlightDim`, `screenshotLastArrowStyle` "filled", `screenshotLastSticker` "check", `screenshotAnnotationShadows` false, `screenshotBackdropStyle` "", `screenshotBackdropPresets` "[]", `screenshotWatermarkStyle` "", `screenshotWatermarkPresets` "[]", `screenshotOpenEditorDirectly` false, `recorderEditorPresets` (empty data), shortcut storage keys `screenshotShortcut`, `screenshotFullScreenShortcut`, `screenshotLastCaptureShortcut`, `screenshotClipboardShortcut`, `screenshotUploadShortcut`, `recentCapturesShortcut`, `recorderShortcut`, `screenOCRShortcut`, `colorPickerShortcut`.

### 6.7 Behaviour (settings-visible)
* Overlay: shared across all displays; mode bar (Screenshot / Recording / Text / Color; digits 1–4) shown unless the tool shortcut hides it. Hints: "Drag to select an area", "Click a window to capture it", "Return captures the whole screen", "Esc cancels", "R repeats the last area", "Z toggles the loupe", scrolling: "S toggles scrolling"/"Scrolling on".
* Freeze: the overlay shows a still of every display so the selection happens on a frozen picture. Hide windows: the app's own windows are excluded from the capture.
* Default action: runs right after capture; `edit` opens the editor immediately; with save/copy a floating confirmation preview (position/duration/focus per settings) offers Edit/Discard; "Ask each time" leaves a quick-preview HUD with Copy/Save/Edit/Pin/Share/Add to Shelf.
* File name: default `Screenshot yyyy-MM-dd at HH.mm.ss` style from `ScreenshotSupport.fileName(prefix:date:)` (+ ".png"); duplicates get " 2", " 3"…; pattern tokens: `%year` 4-digit, `%month` English month name, `%y` 2-digit year, `%mo` 2-digit month, `%d`, `%h`, `%mi`, `%s` (2-digit), `%#…` zero-padded counter; `/` and `:` in file names become `-`; subfolder pattern splits on `/` into nested folders and drops empty/`.`/`..` parts.
* Temp links: `POST {endpoint}/v1/screenshots?expiresIn=<seconds>` (PNG ≤ 25 MiB), response `{id (32 chars [A-Za-z0-9_-]), viewPath "/s/<id>", expiresAt (ISO), deleteToken (43 chars)}`; link = `{endpoint}/s/<id>`; delete = `DELETE {endpoint}/v1/…` with the token; only `https` endpoints without credentials/query. Production endpoint in the original is Vorssaint's hosted service - WinKnife needs its own service or a configurable endpoint (open decision).
* Recorder: countdown -> record the picked area/window/screen to a **take** (master `take.mov` encoded once at the high preset + `pointer.bin` pointer track + `typing.json`), opens the editor (trim, zoom lane, text/blur/image lanes, audio tracks "Mac sound" / "Microphone") unless "Open the editor after recording" is off (then export directly); export MP4 with Quality {Small file: half-size output, 0.05 bpp, 10 s keyframes; Balanced: full size, 0.06 bpp; High: 0.09 bpp, 2 s keyframes} (bitrate = pixels × fps × bpp clamped 0.8–60 Mbps) or GIF (size + fps above, chunked encoding); stops with a HUD when the disk is nearly full.

### 6.8 Windows mapping
| Piece | Windows implementation |
|---|---|
| Selection overlay / freeze | one topmost borderless WPF window per monitor showing a frozen frame from `BitBlt`/`PrintWindow` or DXGI Desktop Duplication (`IDXGIOutputDuplication`, no prompt, no yellow border). `Windows.Graphics.Capture` (`GraphicsCaptureItem.CreateFromMonitor/Window` via `IGraphicsCaptureItemInterop`) is the alternative for window capture and recording; on Win11 set `GraphicsCaptureSession.IsBorderRequired = false` after `GraphicsCaptureAccess.RequestAccessAsync(Borderless)` to avoid the yellow border. Window under pointer: `WindowFromPoint` + `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)`; "Hide WinKnife windows" -> `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` on own windows. Pointer: draw the current cursor (`GetCursorInfo` + `DrawIconEx`) into the frame. |
| Scrolling capture | repeated captures while the user scrolls + overlap stitching (WinKnife has none) |
| DPI | per-monitor DPI v2; "Save at 1x size" scales by monitor scale factor |
| OCR | `Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages()` over a `SoftwareBitmap` (WinKnife `ScreenOcrFeature`); QR: `Windows.Media.Ocr` does not read barcodes - use ZXing.Net (ask before adding, ARCHITECTURE allows only NAudio/CommunityToolkit) or a small in-house QR decoder; show decoded content with Copy/Open buttons |
| Colour picker | frozen magnifier overlay (WinKnife has it); `GetPixel` on the frozen frame; formats as above (keep the "SwiftUI" option for parity or relabel `XAML`/`Color(…)` - recommended: keep) |
| Screen recording | `Windows.Graphics.Capture` frames (Direct3D11CaptureFramePool) -> Media Foundation `MediaStreamSource`/`MediaTranscoder` H.264 MP4 (hardware MFT), or pipe frames to bundled **ffmpeg** (`gdigrab`/`ddagrab` + `wasapi`/dshow) - ffmpeg is the pragmatic choice for GIF/MP4 export (palettegen/paletteuse). System audio = WASAPI loopback (`WasapiLoopbackCapture`, NAudio), microphone = WASAPI capture; keep them as separate tracks. Pointer/click/typing tracking = `WH_MOUSE_LL` / `WH_KEYBOARD_LL` (no permission needed) sampled to the pointer/typing tracks. Disk-space guard via `DriveInfo.AvailableFreeSpace`. |
| Folder chooser | `Microsoft.Win32.OpenFolderDialog` (WPF .NET 8+) / `IFileOpenDialog` with `FOS_PICKFOLDERS`; "Desktop" default = `Environment.SpecialFolder.DesktopDirectory` (WinKnife currently uses Pictures\Screenshots - change to Desktop for parity) |
| Permissions | none (no Screen Recording / Accessibility). Microphone: `DeviceAccessInformation`/`ms-settings:privacy-microphone` |
| Tool shortcuts | `RegisterHotKey` per tool (ids must not collide); the "digit keys" in the overlay are handled by the overlay window |
| Temporary links | HttpClient multipart/octet-stream upload to the service; store records JSON in `%AppData%\WinKnife` |
| Editor tools / recorder editor | WPF editor windows (WinKnife has a basic `ScreenshotEditorWindow` with arrows, boxes, pen, text, blur, crop) |

### 6.9 Gap vs WinKnife
* **Page structure**: WinKnife has separate features (Screenshot, Screen OCR, Color picker) each with their own settings view; Vorssaint merges them into one "Screen capture" page with a tool picker, per-tool shortcut/menu toggles and "Recent captures". **Missing: the unified page + tool picker (segmented/menu), "Show capture menu when using keyboard shortcut", Recent captures shortcut and history window, shortcut-enabled toggles (WinKnife registers hotkeys unconditionally with Ctrl+Alt+S etc.), default hotkeys (should be Ctrl+Alt+Shift+4/3/E/U/H/P/5/T/C).**
* **Screenshot** (`ScreenshotFeature.cs`, `ScreenshotEditorWindow.cs`, `FrozenScreenWindow.cs`): has region/window/all-screens hotkeys, editor toggle, copy/save-on-capture toggles, folder text box. Missing: Capture now + Scrolling capture buttons and scrolling capture, whole-screen/edit-latest/edit-clipboard-image shortcuts, freeze toggle, hide-app-windows, delay (3/5/10), include pointer, last-region outline, window highlight toggle, magnifier options (start on, remember zoom, default zoom, wheel mode), preview position/focus, default action (+confirmation preview and duration), auto-copy semantic, copy-as-image-only, add-to-shelf, folder chooser with Desktop default, subfolder pattern, file-name pattern with counter, save at 1x, **editor tool order + per-tool shortcuts**, **temporary links** (+privacy, shared links, expiry, upload shortcut), file naming convention (`WinKnife-date-time.png` -> `Screenshot …`).
* **Recorder**: **no screen recorder at all** (no recording code; only `MediaToolsFeature` matched "ffmpeg"). Entire card set R1–R4, takes, editor, export, share are new.
* **OCR** (`ScreenOcrFeature.cs`): has hotkey (Ctrl+Alt+Shift+T ✓.), "Keep line breaks" toggle (inverse of Vorssaint's "Remove line breaks", default differs: Vorssaint default = keep line breaks i.e. removesLineBreaks false). Missing: the button, QR reading + toggle, shortcut-enabled toggle.
* **Color picker** (`ColorPickerFeature.cs`): hotkey ✓ (Ctrl+Alt+Shift+C), has a format choice (HEX/RGB/HSL). Missing: SwiftUI format, "Copy without the # prefix", "Pick color" button, caption, shortcut-enabled toggle, alpha/HSL formatting rules above.

---
## 7. Command Bar page (`CommandBarSettings` + `CommandBarAppShortcutsView`)

Sidebar page "Command Bar" (`pageTitle`). Single scrolling grouped form, up to 11 cards, plus two sheets (link editor, app shortcuts). Gated by `AppFeature.commandBar`.

### 7.1 Layout (top to bottom)
**Card B1 — header "Command Bar"**
1. Row (HStack spacing 10, no icons — "a ⌘ glyph in front of ‘Open the bar now’ reads as the shortcut"): bordered button **"Open the bar now"** -> `CommandBarService.show()`; bordered button **"Recenter the bar"** -> `resetPanelPosition()` (disabled until the bar was dragged: `hasCustomPosition`, key `commandBarPositionOffset` "dx,dy").
2. **One explanatory block** (single row, `.caption` secondary, VStack spacing 6): *"Drag the mark at the left of the field to put the bar anywhere on the screen; double-click the mark to recenter it."* / *"One shortcut opens a field over whatever you are doing. Type a few letters, press Return and it happens. Nothing you type is saved."* / Label (icon `lock.laptopcomputer`) *"Search runs on this Mac with no account or cloud, and what you type leaves it only through sites you open or scripts you add."* [mac] => WIN "…on this PC…" / "Try" (tertiary) followed by example **chips** (capsule, 10.5px rounded font, fill primary 6%, padding 6x2): `100 km to mi`, `2+2*3`, `brightness 40`, `fire`, `battery` (the `battery` chip only on machines with an internal battery).
3. Toggle **"Compact mode"** (`commandBarCompactMode`, default off; no callback - the bar reads it on each open) + caption *"Bar opens without suggestions. Results appear as you type."*
4. Only if the Emoji source is enabled: **segmented picker "Skin tone"** with 6 swatches = raised hand ✋ in each tone: default (`""`), light (U+1F3FB), medium-light (1F3FC), medium (1F3FD), medium-dark (1F3FE), dark (1F3FF) (`commandBarEmojiSkinTone`, default `""`; raw values `""|light|mediumLight|medium|mediumDark|dark`) + caption *"By default, emoji that can take a tone use this one. Open the actions on an emoji row to choose a different one."*
5. Toggle **"Global shortcut to open the bar"** (`commandBarShortcutEnabled`, default **off**) -> `syncWithPreferences()`; ShortcutPreferenceRow(role commandBar, ⌥Space) ; orange rejected caption.
6. If macOS Secure Input is active: **SecureInputRow** (orange icon `exclamationmark.circle.fill` + title "Secure input is on" + caption naming the holder app: "%@ was the active app when it turned on and may be holding it, so WinKnife cannot type for you. Dismiss its password field, or quit it, to release it." + button "Show %@"; or "The app holding it cannot be named, so … Dismiss any open password field, or log out and back in, to clear it." / "The app holding it could not be identified, so … cannot type for you."). **[mac only]** => WIN: omit (nothing equivalent; Windows has no secure-input lock).
7. **DisclosureHeaderRow "More options"** (state not persisted); open -> Toggle **"Switch to an ABC layout while the bar is open"** (`commandBarASCIILayoutEnabled`, default off; no callback) + caption *"The field types Latin characters whatever layout was active, and the previous layout comes back when the bar closes."* [mac layout] => WIN: "Switch to an English keyboard layout while the bar is open" via `ActivateKeyboardLayout` for the bar's thread; restore on close.

**Card B2 (no header)**: Button **[app.badge U+E71D] "App shortcuts"** (`appCenterTitle`) -> opens the App shortcuts sheet (7.3); caption *"Open apps with their own shortcuts, even when the bar is closed. Set search names and pin favorites here."*

**Card B3 — header "What the bar searches"** (`sourcesTitle`), footer *"Turn off what you never want to see. Your own actions always stay."*
One Toggle per `CommandBarSource` (icon + label), key `commandBarDisabledSources` (comma-joined raw ids of the *disabled* sources, sorted; default ""); the first row is always on (disabled toggle). Each change calls `syncWithPreferences()`. Order, ids, icons and labels:
| id | SF symbol | Label | WIN note |
|---|---|---|---|
| actions (always on) | wand.and.rays | Vorssaint actions => "WinKnife actions" | |
| apps | square.grid.2x2 | Apps | Start-menu apps |
| menus | filemenu.and.selection | Menu commands of the app in front | UI Automation menu bar traversal (hard; optional) |
| windows | macwindow | Open windows | `EnumWindows` |
| quitApps | xmark.circle | Quit an app | |
| uninstallApps | trash | Uninstaller (`uninstallerName`) | |
| settingsPages | gearshape | Settings pages | WinKnife settings pages |
| macSettings | gearshape.2 | System Settings panes | => "Windows Settings pages" (`ms-settings:` URIs) |
| snippets | text.append | Snippets | |
| clipboard | doc.on.clipboard | Clipboard history | |
| emoji | face.smiling | Emoji | |
| folders | folder | Folders | Windows Search index / known folders |
| answers | info.circle | Answers about this Mac | => "…this PC" |
| calculator | equal.square | Sums and conversions | |
| selection | text.cursor | What is selected | UIA `TextPattern` selection / copy fallback |
| links | bookmark | Your shortcuts (`linksTitle`) | |
| files | doc.text.magnifyingglass | Files | Windows Search index |
| killProcess | xmark.octagon | Kill Process (`killProcess.pageTitle`) | |

**Card B4 — header "Search your files"** (`filesTitle`)
* caption *"Name the folders to look in and the bar finds files by name as you type. Nothing is indexed and no permission is asked for: it uses the Mac’s own search and offers only what you can already see in Finder."* [mac] => WIN: "…it uses the PC’s own search index and offers only what you can already see in File Explorer."
* if no folders: caption *"No folders yet, so the bar looks for no files."*
* one row per folder: `folder` icon (11px accent, 16 wide), path (12px, middle-truncated), Spacer, bordered mini button **"Remove"**. Stored in `commandBarFileScopes` as newline-separated paths abbreviated with `~` (portable); picker allows multiple folders.
* Button **[plus] "Add a folder"** -> multi-select folder picker.
* **DisclosureHeaderRow "More options"**: (indented) caption *"Names never worth showing: a whole folder or file name, or an extension written as *.log."*; rows `eye.slash` + pattern + mini **"Remove"**; add row: text field (placeholder **"A folder or file name"**, Return adds) + small bordered **"Add"** (disabled when blank). Key `commandBarFileIgnores` (newline list).

**Card B5 — header "Your shortcuts"** (`linksTitle`)
* empty: caption *"Nothing saved yet. Add a site, folder, search or script you use every day."*
* rows: kind icon (`link` / `folder` / `terminal`; 11px accent), **name** (12px semibold rounded), **destination** (11px secondary, middle-truncated), Spacer, mini bordered **"Edit"** (`screenshot.editButton`) and **"Remove"**.
* Button **[plus] "Add shortcut"** -> link editor sheet. Max **60** links (`commandBarLinks`, JSON array of `{id,name,kind,destination,runsWithoutArgument,runsDirectly}`).
* **Link editor sheet** (width 440, padding 20, spacing 14): title **"Your shortcuts"** (15 semibold); segmented kind picker [**Site or link** | **Folder or file** | **Script**]; field label **"Name"** + text field; field label **"Where it goes"** + text field (+ bordered **"Choose…"** button for Folder/Script: file picker, folders allowed only for "Folder or file"; picking fills destination and, if name empty, the file name); for link/place: caption *"Drop one of these in and it gets filled in when it opens"* + chip buttons `{query}` (*what you type*), `{clipboard}` (*what you copied*), `{selection}` (*what is selected*), `{date}` (*today*) (capsule with token 10.5 semibold rounded + meaning 9px secondary; click appends the token to the destination; tooltip = meaning); for script: caption *"Choose an executable file. Type its name followed by what you want to send. It runs when you pause and shows the result here."* + small toggles **"Also run when its name is typed on its own"** (`runsWithoutArgument`, false) and **"Run from its global shortcut without opening the bar"** (`runsDirectly`, false). Footer right: **"Cancel"**, prominent **"Save"** (disabled until name and destination are non-blank after trimming; both trimmed on save).
* Placeholders: stored as literal `{query}` etc. (language independent). In a *link* destination substituted values are percent-encoded (everything except `A-Za-z0-9-._~`); in a *place/script* they are inserted raw. A query link answers when the typed text starts with the whole name + space ("gh vorssaint" finds "gh"; "ghost" doesn't); a script runs with whatever follows the name as its single argument when typing pauses (or with no argument when `runsWithoutArgument` and the whole query equals the name).

**Card B6 — header "Rows with their own shortcut"** (`rowShortcutsTitle`)
* empty: *"No row has its own shortcut yet. Open the actions on any row to give it one."*
* rows: shortcut caps (11px semibold rounded accent) + row title (12px secondary, truncated) + Spacer + orange caption "macOS rejected this shortcut. Choose another one." if the combo was refused + mini **"Remove"**. Sorted by title (natural order). Key `commandBarRowShortcuts` (JSON `{rowKey: shortcutStorage}`); removal re-syncs.

**Card B7 — header "Names you gave"** (`namedTitle`)
* empty: *"Nothing named yet. Open the bar, pick a row and press the actions key."*
* rows: alias (12px semibold rounded accent) + row title (12px secondary) + mini **"Remove"**; sorted by alias. Key `commandBarAliases` (JSON `{rowKey: alias}`, alias ≤ 60 chars).

**Card B8 — header "Pinned"** (`pinnedTitle`): empty *"Nothing pinned yet."*; rows: title + mini **"Remove"** (key `commandBarPins`, ordered JSON array of row keys).

**Card B9 — header "Never shown"** (`hiddenTitle`): empty *"Nothing hidden."*; rows: title + mini **"Remove"** (un-hides; key `commandBarHidden`); then bordered button **"Forget what I use most"** -> `forgetLearnedRanking()` (clears the learned usage ranking).

Keys that cannot be resolved to a current row (an uninstalled app) still show with the raw key as the title so nothing becomes invisible; uninstalled *hub features* are dropped from Pinned.

### 7.2 Strings (CommandBarFeatureStrings used on this page; verbatim)
openButton "Open the bar now"; resetPositionButton "Recenter the bar"; positionCaption, settingsCaption, privacyNote, tryTheseLabel "Try" (see above); compactModeToggle "Compact mode"; compactModeCaption; emojiSkinToneLabel "Skin tone"; emojiSkinToneCaption; shortcutToggle "Global shortcut to open the bar"; asciiLayoutToggle "Switch to an ABC layout while the bar is open"; asciiLayoutCaption; appCenterTitle "App shortcuts"; appCenterCaption; sourcesTitle "What the bar searches"; sourcesCaption; sourceActions "Vorssaint actions"; sourceApps "Apps"; sourceMenus "Menu commands of the app in front"; sourceWindows "Open windows"; sourceQuitApps "Quit an app"; sourceSettingsPages "Settings pages"; sourceMacSettings "System Settings panes"; sourceSnippets "Snippets"; sourceClipboard "Clipboard history"; sourceEmoji "Emoji"; sourceFolders "Folders"; sourceAnswers "Answers about this Mac"; sourceCalculator "Sums and conversions"; sourceSelection "What is selected"; sourceFiles "Files"; filesTitle "Search your files"; filesCaption; filesEmpty; filesAddFolder "Add a folder"; filesIgnoreCaption; filesIgnorePlaceholder "A folder or file name"; filesIgnoreAdd "Add"; linksTitle "Your shortcuts"; linksEmpty; linkAddButton "Add shortcut"; linkKindLink "Site or link"; linkKindPlace "Folder or file"; linkKindScript "Script"; linkDestinationLabel "Where it goes"; linkPlaceholdersHint; placeholderQuery "what you type"; placeholderClipboard "what you copied"; placeholderSelection "what is selected"; placeholderDate "today"; scriptHint; scriptRunsWithoutArgument; scriptRunsDirectly; rowShortcutsTitle "Rows with their own shortcut"; rowShortcutsEmpty; namedTitle "Names you gave"; namedEmpty; pinnedTitle "Pinned"; pinnedEmpty "Nothing pinned yet."; hiddenTitle "Never shown"; hiddenEmpty "Nothing hidden."; forgetAllButton "Forget what I use most"; removeButton "Remove". Elsewhere: `screenshot.editButton` "Edit"; snippets `nameLabel` "Name", `saveButton` "Save"; radialMenu `chooseButton` "Choose…"; `uninstallerCancel` "Cancel".
Other bar strings (for the bar itself): pageTitle "Command Bar"; hubDescription "One field that finds and runs everything the app does"; panelCaption "Type to find and run anything"; searchPlaceholder "Type what you want to do"; suggestionsLabel "Suggestions"; noResultsAction "See suggestions"; argumentRangeFormat "%d to %d"; argumentHint "Return applies · Esc goes back"; confirmHint "Return confirms · Esc cancels"; kindApp "App"; kindClipboard "Paste from history"; kindSnippet "Insert snippet"; soundOutputSubtitle "Switch sound output"; soundOutputCurrent "Current output"; volumeTitle "Volume"; brightnessTitle "Display brightness"; brightnessKeywords "screen"; keepAwakeForFormat "Keep awake for %@"; needsSetupFormat "Turn on %@ in Settings"; needsPermissionHint "Needs permission · Return asks"; actionCheckAppUpdates "Check for app updates"; actionCleanURL "Clean the copied link".

### 7.3 App shortcuts sheet (`CommandBarAppShortcutsView`)
Modal sheet 780x560, padding 24, spacing 16. Auto-closes if the Command Bar feature is uninstalled; clears message on language change; `service.refreshApplications()` on appear.
1. Title **"App shortcuts"** (title2 semibold) + callout secondary *"Open apps with their own shortcuts, even when the bar is closed. Set search names and pin favorites here."*
2. Controls row (spacing 12): search field (rounded 7 on quaternary fill, padding 7; `magnifyingglass` + placeholder **"Search apps"**; clear `xmark.circle.fill` with a11y "Remove" when non-empty) and a **segmented filter** (a11y label "Apps") [**All** (`categoryAll`) | **Pinned** | **With shortcuts**] (default All). Search matches normalised `title + keywords + alias`.
3. **Table (inset style)** with 4 sortable columns (click header to sort; default Name ascending; `localizedStandardCompare`; sort helper `CommandBarAppSort` also supports alias/shortcut/pinned):
   * **Apps** (min 150 / ideal 210): 24x24 icon + name (1 line, tooltip).
   * **Alias** (min 110 / ideal 150): rounded text field, placeholder **"The name you call it"**; saved on focus loss / Return / disappear; trimmed to 60 chars; a name already used by another row is refused: message **"%@ already answers to that"** (owner title) and the field reverts.
   * **Shortcut** (width 180): recorder (width 140, empty state **"Record…"**, clear via Delete) + orange `exclamationmark.triangle` (tooltip "macOS rejected this shortcut. Choose another one.") if refused + `xmark.circle` button (tooltip/a11y **"Take the shortcut off"**) when set. Defaults to showing `commandBarDefault` only as placeholder styling.
   * **Pinned** (min 60 / ideal 80): `pin.fill` accent / `pin` secondary toggle; tooltip **"Unpin"** / **"Pin to the top"**.
   Empty overlay: while loading spinner + **"Still looking"**; otherwise magnifier + **"Nothing here by that name."**
   Several installed copies of the same app share one durable binding (key = stable key, shown once; the last entry is what launches).
4. Optional take-over offer (mac): `SystemShortcutTakeOverOffer` when the recorded combination belongs to macOS (text "macOS uses %@ for one of its own shortcuts." / buttons "Don’t take over", "Take over while Vorssaint runs" / caption "macOS gets the key back whenever Vorssaint is not running or this feature is off.") => WIN: not applicable, except reserved Windows hotkeys which are simply refused.
5. Footer: message line (caption; secondary hint **"Press the keys you want · Delete clears it · Esc goes back"**, replaced by an orange error `"<App>: <error>"`) – Spacer – bordered **"Done"** (`supportIntroDoneButton`, Esc closes).
Errors come from `shortcutNotCaptured`, `shortcutInvalid`, conflicts (`shortcutConflictFormat`), refused registration.

### 7.4 Settings table (Command Bar)
| Key | Type | Default | Notes |
|---|---|---|---|
| `commandBarShortcutEnabled` | bool | false | |
| `commandBarShortcut` | shortcut | ⌥Space (WIN Alt+Space) | |
| `commandBarCompactMode` | bool | false | |
| `commandBarEmojiSkinTone` | string | "" | `""\|light\|mediumLight\|medium\|mediumDark\|dark` |
| `commandBarASCIILayoutEnabled` | bool | false | |
| `commandBarDisabledSources` | string | "" | CSV of source ids (never `actions`) |
| `commandBarAliases` | string(JSON) | "" | `{rowKey: alias}` |
| `commandBarPins` | string(JSON) | "" | `[rowKey]` |
| `commandBarHidden` | string | "" | set of row keys |
| `commandBarLinks` | Data(JSON) | – | ≤ 60 |
| `commandBarRowShortcuts` | string(JSON) | – | `{rowKey: shortcut}` |
| `commandBarFileScopes` / `commandBarFileIgnores` | string | "" | newline lists (`~` portable) |
| `commandBarPositionOffset` | string | "" | "dx,dy" from default spot |

### 7.5 Behaviour
* The bar is a floating panel with one text field; sources feed a ranked result list (fuzzy match, usage-learned ranking, pins first, aliases as extra names; hidden rows never shown; compact mode starts with no suggestions).
* **File search**: needs ≥ 2 characters; query = every word must appear in the file name (any order); max 1000 candidates before filtering, 200 results after; hidden paths and package internals are never offered (structural); built-in ignores always applied: `node_modules`, `.git`, `DerivedData`, `Pods`, `.build`, `vendor`; user ignores match a whole folder/file *name* (never half of one: "build" doesn't hide "rebuild-notes.md") or an extension pattern `*.log`; the home folder itself is expanded into its children so `~/Library` is never searched; **empty scope list = feature off** (never falls back to home); only the newest query may update the visible list.
* Link shortcuts, scripts, row shortcuts, pins, hidden, aliases as described; "Forget what I use most" wipes the learned ranking.
* A row shortcut registered globally runs that row even with the bar closed; refused combos are listed with the orange caption in both the page and the sheet.

### 7.6 Windows mapping
* Global hotkey: `RegisterHotKey` (Alt+Space; if the system menu steals it, fall back); row shortcuts: one `RegisterHotKey` per row (ids in a reserved range), re-registered on `syncWithPreferences`.
* Sources: apps = Start-menu `.lnk`/UWP via `shell:AppsFolder` (WinKnife `AppIndex` exists); windows = `EnumWindows` + DWM cloaked check; files/folders = Windows Search (`Windows.Storage.Search.QueryOptions` over `IndexedState`, or OLE DB `SystemIndex` with `System.FileName LIKE`/CONTAINS); selection = UIA `TextPattern.GetSelection()` with Ctrl+C fallback; menus = UIA `MenuBar` traversal (optional).
* Folder/file pickers: `OpenFolderDialog` (multi-select) / `OpenFileDialog`.
* "ABC layout": `ActivateKeyboardLayout(HKL_en-US)` + restore.
* Examples chips: all valid on Windows (`brightness 40` via WMI `WmiMonitorBrightnessMethods`; `battery` via `SystemInformation.PowerStatus`).

### 7.7 Gap vs WinKnife (`CommandBarFeature.cs`, `CommandBarSupport.cs`, `CommandBarWindow.cs`)
WinKnife: single hotkey (Ctrl+Alt+R, always on), toggles `includeApps` and `includeCalculator`, `webSearchUrl` text. Missing: shortcut-enabled toggle (default off) and Alt+Space default; Open/Recenter buttons and draggable position; compact mode; emoji + skin tone; ASCII layout; App shortcuts sheet (aliases, per-app shortcuts, pins); **sources list (18 toggles)**; **file search** with folders/ignores; **saved shortcuts (links/places/scripts)** with placeholders; rows-with-shortcuts / names / pinned / never-shown lists and "Forget what I use most"; learned ranking; the example chips. The web-search URL option is a WinKnife extra that Vorssaint does not have (it uses saved link shortcuts with `{query}` instead) - remove or keep as a hidden advanced option.

---

## 8. Radial menu page (`RadialMenuSettings`, `RadialMenuVisualCanvas`)

Sidebar page "Radial menu". Master switch, behaviour, **profiles** (several wheels, each with its own colour, shortcut, mouse button, trackpad tap and up to 12 actions + one submenu level), a **visual canvas editor** and an optional list editor.

### 8.1 Layout (top to bottom)
**Card M1 — header "Radial menu"**
1. Toggle **"Use the radial menu"** (`radialMenuEnabled`, default off). On change: `RadialMenuService.syncWithPreferences()`; if turned on and any profile needs Accessibility (key/window-layout/media actions or mouse/trackpad triggers) and it isn't granted -> request it (mac) => WIN: nothing.
2. caption *"Opens a wheel of your favorite actions around the pointer"* (this is `hubDescription`; `enableCaption` "Hold the shortcut, point at an action and release. A quick press keeps the wheel open." exists but is not shown here).
3. orange caption when `service.registrationFailed`: *"Use at least Control, Option or Command with a key."* (the `shortcutInvalid` text, verbatim, shown when a profile shortcut couldn't register).
4. menu Picker **"Opening behavior"** [**Press or hold** (`pressOrHold`, default) | **Press to open** (`press`) | **Hold to select** (`hold`)] (`radialMenuActivationMode`), disabled while off.
5. caption *"Press or hold keeps the current adaptive gesture. Press stays open; hold runs the highlighted action on release."*
6. **Segmented picker "Opens"** [**At the pointer** (true, default) | **At the screen center**] (`radialMenuAtPointer`).
7. Button **"Try it"** -> `presentPreview(for: selectedProfile)` (shows the selected wheel without a trigger).

**Card M2 (conditional)** — only if any profile needs Accessibility and it is missing: PermissionRow(accessibility) + caption: *"Grant Accessibility to move windows."* (when the current wheel uses window layout) else *"Key actions and the side mouse button trigger need the Accessibility permission."* => WIN: never shown.

**Card M3 — header "Profiles"**
1. **Profile management row**: menu Picker **"Profile"** (lists profile display names; empty name displays "General"; disabled while off); borderless **menu button `+`** (tooltip **"Add profile"**; items = presets **General, Media, Tools, Window layout, Quick toggles, Blank**); borderless button `plus.square.on.square` (tooltip **"Duplicate profile"**; copy named "<name> 2" with shortcut, mouse button and trackpad tap cleared); borderless button `trash` (tooltip **"Delete profile"**; disabled with 1 profile) -> confirmation dialog title **“Delete “%@”?”**, message **"Its actions, shortcut, mouse button and four-finger tap will be removed. This can’t be undone."**, buttons **"Delete profile"** (destructive) / **"Cancel"**. All disabled while off. New preset profiles are named "General N" (N = count+1, for General) or the preset's title; each preset has default colour (General accent, Media purple, Tools cyan, Window layout orange, Quick toggles mint, Blank graphite).
2. if enabled and the selected profile has no trigger: caption *"Picking a profile here only edits it. Give this one a shortcut, a mouse button or the four-finger tap below to open it."*
3. text field **"Profile name"** (prompt "General"; ≤ 60 chars on save) disabled while off.
4. Row **"Color"** + 12 swatches (16 px circles in 22 px hit targets; selected = inner white ring 12 px 2 px + outer ring 20 px 1.5 px in the colour): **Accent, Blue, Purple, Pink, Red, Orange, Yellow, Green, Mint, Cyan, Indigo, Graphite** (tooltips/a11y = names). Light-scheme RGBs: blue (0,0.48,1), purple (0.58,0.20,0.85), pink (0.88,0.16,0.45), red (0.85,0.18,0.18), orange (0.95,0.45,0), yellow (0.85,0.65,0), green (0.18,0.65,0.25), mint (0,0.68,0.60), cyan (0.15,0.65,0.85), indigo (0.35,0.35,0.85), graphite white 0.40; dark scheme uses the system colours (graphite white 0.65); accent = system accent.
5. **Shortcut row**: label **"Shortcut"** – recorder (width 130; empty state "None"; Delete clears; conflicts refused with "This shortcut is already used by %@." naming another wheel / another shortcut role / a window-layout shortcut / a Command Bar row shortcut). Stored per profile as the shortcut storage string ("" = none).
6. menu Picker **"Mouse button"** [**Off** | **Back side button** | **Forward side button** | **Button 6 … Button 32**] (mouse buttons 3…31; names via `Button N+1`); a button belongs to one wheel - choosing it releases it from the others; choosing a button when Accessibility is missing requests it. Disabled while off. Then caption *"Only extra mouse buttons work here. With a mouse without extra buttons, use the keyboard shortcut above. With a trackpad, you can also use the four-finger tap below."*
7. If a button is chosen: for Back/Forward a caption *"While this is on, that button no longer goes back or forward in apps. If you navigate with it, leave this off."*; then the **button test row**: icon + label **"Button test"** + right status; states: not watching -> orange `exclamationmark.circle.fill` **"Vorssaint cannot watch the mouse right now"** (=> "WinKnife cannot…"); nothing seen yet -> `circle.dashed` secondary **"Press the button now"**; the expected button seen -> green `checkmark.circle.fill` **"Vorssaint sees this button"** (=> "WinKnife sees this button"); another button -> orange **"That was a different button"**. Underneath caption *"If nothing lights up, macOS is not sending that button to apps. Mice that come with their own software often turn the side buttons into something else. Change it back there, or give the button a key combination and use that combination above."* [mac] => WIN: "…Windows is not receiving that button…". The row starts/stops mouse-button reporting `onAppear/onDisappear`.
8. Toggle **"Open with a four-finger tap"** (per profile `trackpadTap`; only one wheel may own it - turning it on clears the others; requests Accessibility if needed). If on and Middle-click's four-finger tap is enabled (`middleClickEnabled` with `middleClickTapFingers` > 0 so the radial tap is reassigned): orange caption *"Middle click already uses the four-finger tap, so the tap does not open this wheel."*

**Card M4 — header "Actions"** (footer: *"A wheel holds up to 12 actions."* when 12 actions, otherwise the hub description *"Opens a wheel of your favorite actions around the pointer"*)
1. **RadialMenuVisualCanvas** (see 8.2), list-row insets 4/0.
2. caption *"Click a button to choose what it starts, or to remove it. Drag a button to move it."*
3. if fewer than 12 actions at this level: button **[plus] "Add action"**.
4. **DisclosureGroup** whose title toggles **"Show as list"** / **"Hide list"**:
   * inside a submenu: borderless accent button `chevron.backward` + **"Back  <submenu name>"**;
   * empty: caption *"No actions here yet."*;
   * rows (`RadialItemRow`): drag grip `line.3.horizontal` (tertiary), then a button with badge + name + kind caption (e.g. "Open an app"), and for submenus a borderless **"Edit actions >"** button. Badge 30x30: real app/file icon (26x26) when the item uses the file icon, else rounded-rect (radius 8) filled with the brand gradient and a white 13-px semibold symbol. Dragging a row reorders live (swap during drag-over, 0.12 s ease, row at 45% opacity while dragged); context menu **"Remove"**.

### 8.2 Visual canvas (`RadialMenuVisualCanvas`)
* Container: height **330**, full width, radius 14 (continuous), stroke white 12–14% 0.8 px, shadow black 8%/28% radius 10 y 4. Stage background = `rgb(0.08,0.09,0.11)` + radial gradient (profile colour 15% -> clear, radius 20…150, centred) + brand gradient at 40%.
* Header bar (padding h14, top10): at root the title **"RADIAL MENU"** (`pageTitle` uppercased, 11px semibold, white 60%); inside a submenu a plain accent button `chevron.backward` + submenu name (tooltip "Back"); right side: borderless small **"Reset"** (`resetActionsButton`, white 70%) only when the wheel came from a known preset and you're at the root; confirmation dialog title **"Reset actions"** with destructive button **"Reset actions"**, message **"Restore the default actions for this profile? Custom actions will be replaced."**
* Wheel: backplate circle diameter **250** (black 42%, stroke white 12% 0.8, shadow black 40% r14 y4); dividers between slices from radius **36** (dead zone) to 123 (white 9%, 1 px) at angles `step*(i+0.5)` (only when ≥ 2 slices); chips (circles **44**, fill rgb(0.18,0.19,0.22) / profile colour when highlighted, stroke white 18% / 50%) on a ring radius **92**, placed clockwise starting at 12 o'clock (`θ = 2π·i/n`); highlighted wedge fill = profile colour 22% (hover) / 32% (drag target) with 0.12 s ease; hover scales a chip 1.08; chip icon 16-px symbol white 90% or 26x26 real/custom icon; Now Playing uses `music.note`.
* Hub: circle 68 (fill rgb(0.12,0.13,0.16), stroke white 16%); content by state: drag target -> `arrow.triangle.swap` + target name (9 px bold white, 2 lines); hover -> item name (10 px semibold) + kind label (8 px, white 60%); in a submenu -> `chevron.backward` + "Back"; empty -> `plus` + "Add action" (click adds); default -> the brand mark (28 wide, white 85%). Click the hub = back (submenu) / add (empty).
* Interaction: click (movement ≤ 6 px) a chip -> edit; drag > 6 px -> a floating copy follows (scale 1.15, shadow in profile colour) and the slice under the pointer becomes the drop target (angle -> index); release over another slot swaps the two items (spring 0.25/0.8 unless Reduce Motion); context menu per chip: **"Radial menu"** (edit), **"Edit actions"** (submenus), **"Remove"**.

### 8.3 Action editor sheet (`RadialItemEditor`, width 440, padding 18, spacing 14)
Title: **"Add action"** when new, else the item's display name (headline). Form (columns style):
* menu Picker **"Action"** with kinds: **Open an app**, **Open a file or folder**, **Open a link**, **Press a shortcut**, **Vorssaint tool** (=> WinKnife tool; only if ≥ 1 tool runnable), **Quick toggles** (`quickToggles.pageTitle`; only if any toggle's feature available), **Window layout** (if installed), **Media control**, **Submenu** (only when editing the root level). Changing kind resets the payload (tool -> first runnable tool; quickToggle -> first; windowLayout -> Left half; media -> Play or pause; others "") and clears children when not a submenu, and clears a fetched favicon when leaving "link".
* Payload editor by kind: app/file -> labelled row (kind label) with bordered button showing **"Choose…"** or the chosen file name (picker: apps `.app` bundles starting in /Applications; file/folder: any); link -> text field (label "Open a link", prompt **"example.com"**) + button **"Fetch Website Icon"** (spinner + **"Fetching icon…"**) with result line `checkmark.circle.fill` green **"Icon downloaded"** or `exclamationmark.triangle.fill` orange **"Could not find a website icon"** and caption *"Connects to the website once to download its icon. Saved locally."*; shortcut -> recorder (width 108, empty "None", Delete clears) with hint line while recording ("Press the new shortcut Escape cancels. Delete clears.") or orange problem text; tool -> menu Picker **"Tool"** (one entry per runnable tool, text = that feature's hub title; Shelf only when the shelf is enabled; order = raw-id order listed in 8.5); quick toggle -> menu Picker (**Quick toggles**) with icon rows: Dark mode(toggle label), Empty the Trash, Quit all apps, Eject all disks, Show/Hide hidden files, Hide/Show desktop icons, Lock the screen, Turn off the display, Start the screen saver; window layout -> menu Picker (**Window layout**) of all layout actions with icons; media -> menu Picker **"Control"** [**Play or pause**, **Previous track**, **Next track**, **Now Playing**]; submenu -> no payload.
* text field **"Name"** (prompt **"Automatic"**; trimmed; ≤ 60 chars).
* **Icon picker**: label **"Icon"** + adaptive grid (cells ≥ 34 wide, spacing 6, each 34x30, radius 7; selected = accent 14% fill + accent 75% stroke 1.2; else secondary 6% / 13% stroke): first cell = **Automatic** (the real app/file icon, the fetched favicon, or the action's default symbol; tooltip "Automatic"), then the curated symbols (below). 
* Notes under the form: invalid link -> orange **"Enter a valid link."**; submenu -> caption **"Opens a second wheel with its own actions."** + accent plain button **"Edit actions >"** (closes the sheet and drills in).
* Footer: left destructive **"Remove"** (only for existing items), right **"Cancel"** and prominent **"Save"** (disabled while the payload is invalid).
Validity: app/file need a non-empty path; link must normalise (`example.com/page` -> https://…; explicit schemes kept; `host:port` treated as host); shortcut must parse; tool/toggle/layout/media must be known.
Curated symbol list (SF names, in order): star.fill heart.fill bolt.fill flame.fill sparkles folder.fill doc.fill tray.full.fill terminal.fill globe envelope.fill message.fill music.note headphones camera.fill photo.fill video.fill gamecontroller.fill calendar clock.fill house.fill cart.fill hammer.fill paintbrush.fill book.fill keyboard magnifyingglass airplane checkmark.circle.fill xmark.circle.fill plus.circle.fill minus.circle.fill exclamationmark.triangle.fill questionmark.circle.fill info.circle.fill lock.fill lock.open.fill key.fill person.fill person.2.fill bell.fill flag.fill bookmark.fill tag.fill paperclip link scissors doc.on.clipboard square.and.arrow.up square.and.arrow.down trash.fill archivebox.fill externaldrive.fill internaldrive.fill display desktopcomputer laptopcomputer iphone ipad applewatch wifi network antenna.radiowaves.left.and.right speaker.wave.2.fill mic.fill waveform play.fill pause.fill stop.fill backward.fill forward.fill shuffle repeat sun.max.fill moon.fill lightbulb.fill battery.100 power eye.fill eye.slash.fill location.fill map.fill paperplane.fill bubble.left.fill phone.fill gearshape.fill slider.horizontal.3 switch.2 command printer.fill textformat number. => WIN: ship an equivalent ~95-glyph Segoe Fluent set (unverified suggestions, check each code point in Character Map: Star E734/E735, Heart EB51, Lightning E945, Folder E8B7, Document E8A5, Globe E774, Mail E715, Chat E8BD, Music E8D6, Headphone E7F6, Camera E722, Photo E91B, Video E714, Calendar E787, Clock E121, Home E80F, Shop E719, Wrench E90F, Brush EF77, Book E82D, Keyboard E765, Search E721, Airplane E709, Accept E73E, Cancel E711, Add E710, Remove E738, Warning E7BA, Help E897, Info E946, Lock E72E, Unlock E785, Permissions/Key E8D7?, Contact E77B, People E716, Ringer EA8F, Flag E7C1, Tag E8EC, Attach E723, Link E71B, Cut E8C6, Paste E77F, Share E72D, Download E896, Delete E74D, Folder E8B7, HardDrive EDA2, Tablet E70A, Phone E717, WiFi E701, Network E968, Volume E767, Microphone E720, Play E768, Pause E769, Stop E71A, Previous E892, Next E893, Shuffle E8B1, Repeat E8EE, Brightness E706, QuietHours E708, Lightbulb EA80, Battery EBA7…, Power E7E8, View E7B3, Hide ED1A, Location E81D, Map E826, Send E724, Settings E713, Print E749, Font E8D2). The stored value should be a stable id (e.g. `star`, `heart`) not the glyph. `customIconData` (favicon PNG 64x64, ≤ 64 KB, source ≤ 2 MiB download, ≤ 4096 px / 16.7 MP, same-origin redirects only, fetch `/favicon.ico` with 5 s timeout) is stored with the item.

### 8.4 Strings
`RadialMenuFeatureStrings` (all used above): pageTitle "Radial menu"; hubDescription "Opens a wheel of your favorite actions around the pointer"; enableLabel "Use the radial menu"; enableCaption "Hold the shortcut, point at an action and release. A quick press keeps the wheel open."; positionLabel "Opens"; positionPointer "At the pointer"; positionCenter "At the screen center"; tryButton "Try it"; actionsHeader "Actions"; addButton "Add action"; limitCaption "A wheel holds up to 12 actions."; emptyCaption "No actions here yet."; backButton "Back"; editActionsButton "Edit actions"; nameLabel "Name"; automaticLabel "Automatic"; iconLabel "Icon"; actionLabel "Action"; kindApp "Open an app"; kindFile "Open a file or folder"; kindURL "Open a link"; kindShortcut "Press a shortcut"; kindTool "Vorssaint tool"; kindMedia "Media control"; kindSubmenu "Submenu"; chooseButton "Choose…"; urlPlaceholder "example.com"; urlInvalid "Enter a valid link."; toolLabel "Tool"; mediaLabel "Control"; mediaPlayPause "Play or pause"; mediaPrevious "Previous track"; mediaNext "Next track"; mediaNowPlaying "Now Playing"; mediaNothingPlaying "Nothing playing"; mediaOpenAppFormat "Open %@"; submenuCaption "Opens a second wheel with its own actions."; saveButton "Save"; deleteButton "Remove"; permissionCaption (above); manageButton "Manage the menu"; panelCaption "Your favorite actions on a wheel"; mouseTriggerLabel "Mouse button"; mouseTriggerOff "Off"; mouseTriggerBack "Back side button"; mouseTriggerForward "Forward side button"; mouseTriggerWarning, buttonTest* (above); activationMode* (above); profilesHeader "Profiles"; profilePickerLabel "Profile"; addProfileButton "Add profile"; duplicateProfileButton "Duplicate profile"; deleteProfileButton "Delete profile"; deleteProfileConfirmFormat "Delete “%@”?"; deleteProfileConfirmMessage; profileNameLabel "Profile name"; profileColorLabel "Color"; profileShortcutLabel "Shortcut"; profileMouseTriggerLabel "Mouse button"; presetGeneral "General"; presetMedia "Media"; presetTools "Tools"; presetWindowLayout "Window layout"; presetQuickToggles "Quick toggles"; presetBlank "Blank"; color names; fetchFavicon* (above); mouseTriggerRequirement; canvasHint; resetActionsButton "Reset"; resetActionsConfirm "Reset actions"; resetActionsConfirmMessage; showListButton "Show as list"; hideListButton "Hide list"; trackpadTapLabel "Open with a four-finger tap"; trackpadTapConflict; profileNoTrigger. Mouse names: `middleButtonName` "Middle button", `otherButtonFormat` "Button %d".

### 8.5 Settings
| Key | Type | Default | Notes |
|---|---|---|---|
| `radialMenuEnabled` | bool | false | master |
| `radialMenuActivationMode` | enum | `pressOrHold` | `pressOrHold`/`press`/`hold` |
| `radialMenuAtPointer` | bool | true | false = screen centre |
| `radialMenuProfiles` | Data (JSON `[RadialMenuProfile]`) | missing = one "General" profile with starter actions and the legacy shortcut ⌃⌥⌘Space | profile = `{id, name, color(accent/blue/…/graphite), shortcut, mouseButton ("off"/"back"/"forward"/"button:N"), items[], preset, trackpadTap}`; sanitised on every save: unique ids, name ≤ 60, invalid shortcut -> "", one wheel per mouse button |
| `radialMenuShortcut`, `radialMenuMouseButton`, `radialMenuItems` | legacy | ⌃⌥⌘Space / `off` / – | migrated into the first profile when `radialMenuProfiles` is absent |
| `middleClickEnabled`, `middleClickTapFingers` | bool / int | false / 0 | read-only here (four-finger tap conflict) |
Item = `{id, kind(app/file/url/shortcut/tool/quickToggle/windowLayout/media/submenu), name, symbolName, payload, customIconData, children}`; sanitising: ≤ 12 items per wheel, depth 2 (root + one submenu level, children max 12), unique ids, name ≤ 60, payload trimmed and validated, URL normalised, custom icon dropped if > 64 KB or undecodable, non-submenu items lose children.
**Starter wheel** (General, 6 slices clockwise from 12 o'clock): Play or pause, Next track, Screenshot (tool), `~/Downloads` (file), Color picker (tool), Previous track. Presets: Media = Play/pause, Next, Now Playing, Previous; Tools = Screenshot, Color picker, Copy text from screen, Screen recorder, Mute microphone, Scratchpad; Window layout = Maximize, Right half, Bottom half, Left half, Top half; Quick toggles = Dark mode, Desktop icons, Hidden files, Lock screen, Empty Trash; Blank = none.
Tools available as actions (raw ids): screenshot, screenRecorder, colorPicker, screenOCR, micMute, clipboardHistory, quickLauncher, cameraPreview, scratchpad, shelf, cleaner, uninstaller, appUpdates, cleaningMode, keepAwake. Quick toggles: darkMode, emptyTrash, quitAllApps, ejectDisks, hiddenFiles, desktopIcons, lockScreen, displayOff, screenSaver. Media keys: playPause, previousTrack, nextTrack, nowPlaying.

### 8.6 Behaviour of the live wheel (settings-relevant)
* Geometry constants (live wheel): panel 400, wheel diameter 300, ring radius 112, chip 52, hub 76, dead zone 40, pointer must travel 8 pt before slices highlight. Slice 0 at 12 o'clock, clockwise; highlighted slice = angle `atan2(dx, dyUp)` quantised with half-step offset; nothing highlighted inside the dead zone.
* Activation modes: **pressOrHold** — shortcut held + release over a slice runs it; release near the centre leaves the wheel open for clicking; **press** — a press opens a sticky wheel (release ignored); **hold** — wheel exists only while the summoner is down, release runs the highlighted slice or just dismisses. A side mouse button counts as "held"; a shortcut with modifiers can be held.
* Triggers: per-profile global shortcut, per-profile mouse button (extra buttons 3…31; event tap swallows the button while the menu is on and Mouse navigation / Mouse button shortcuts defer to it), per-profile four-finger trackpad tap (one owner). "Try it" and preview work without any trigger.
* Media keys post aux-key events (play 16, next 19 / prev 20 in `NX_KEYTYPE_*`); **Now Playing** shows title/artist/artwork card from the system media session (nothing playing -> "Nothing playing"; click opens the source app, label "Open %@").

### 8.7 Windows mapping
* Global shortcuts: `RegisterHotKey` for press modes; for **hold** mode use `WH_KEYBOARD_LL` to see key-up of the held modifiers, or `GetAsyncKeyState` polling (16 ms) while open.
* Mouse buttons: `WH_MOUSE_LL` receives only Left/Right/Middle/**XButton1 (Back) / XButton2 (Forward)** (`WM_XBUTTONDOWN`, `HIWORD(mouseData)`); Windows has no "Button 6…32" through standard hooks (only vendor HID), so the picker offers **Off, Middle button, Back side button, Forward side button** (WinKnife already has middle/x1/x2). Swallow by returning 1 from the hook.
* Four-finger tap: Windows reserves 3/4-finger gestures for the Precision Touchpad (Settings > Bluetooth & devices > Touchpad). Detect via Raw Input (`RegisterRawInputDevices` UsagePage 0x0D Usage 0x05, parse contact count/tip switch in HID reports) – feasible only on PTP touchpads; if the OS maps the 4-finger tap to an action, ours fires in addition. Mark the setting "Precision touchpads only"; hide the row otherwise.
* Media keys: `SendInput` `VK_MEDIA_PLAY_PAUSE (0xB3)`, `VK_MEDIA_NEXT_TRACK (0xB0)`, `VK_MEDIA_PREV_TRACK (0xB1)`; Now Playing = `GlobalSystemMediaTransportControlsSessionManager` (WinKnife `MediaService`).
* Press a shortcut = `SendInput` of the stored combination. Open app/file/link = `ShellExecute` (`Process.Start` with `UseShellExecute`). Window layout = the existing WinKnife window-layout engine. Quick toggles = see 5.10.
* Icons: app/file icon via `SHGetFileInfo`/`IShellItemImageFactory`; favicon fetch with `HttpClient` honouring the limits.
* Wheel window: layered topmost no-activate WPF window with per-pixel alpha (WinKnife `RadialWindow`); settings preview canvas = WPF `Canvas` implementing the 8.2 geometry; drag & drop in the list via `DragDrop.DoDragDrop`.
* Accessibility rows/captions: hide. Replace "macOS … not sending that button" with Windows wording.

### 8.8 Gap vs WinKnife (`RadialMenuFeature.cs`)
WinKnife: **one** wheel, 8 fixed "slot" dropdowns (each picking any registered feature action), trigger choice (held hotkey / middle / x1 / x2), hotkey default Ctrl+Alt+Q, centre/pointer not configurable. Missing: master "Use the radial menu" with Try it; **activation modes**; **At the pointer / At the screen center**; **profiles** (add/duplicate/delete, presets, colours, per-profile shortcut/mouse/tap); **visual canvas editor** with drag-reorder, hub, reset; **list editor**; **action editor** (app, file, link + favicon, shortcut, tool, quick toggle, window layout, media, submenu), **12 actions, submenu level**, icon picker, custom names, button test row, four-finger tap, legacy migration; default shortcut should be Ctrl+Alt+Shift+Space.

---

## 9. Consolidated defaults (registered values; use these when creating WinKnife defaults)

Booleans default **false** unless listed here as **true**.
**true**: `clipboardHistoryShortcutEnabled`, `clipboardHistoryIncludeImagesFiles`, `panelUtilityClipboard`, `finderCutPasteShowHUD`, `shelfShortcutEnabled`, `shelfShakeToOpen`, `shelfDropZoneEnabled`, `shelfRemoveAfterDrop`, `notchShelf`, `quickLauncherShortcutEnabled`, `micMuteMenuBarIndicator`, `wallpaperApplyAllDisplays`, `scratchpadCloseOnClickOutside`, `screenshotFreeze`, `screenshotHideVorssaintWindows`(->WinKnife), `screenshotShowLastRegion`, `screenshotHighlightWindows`, `screenshotToolShortcutsEnabled`, `screenshotPreviewTakesFocus`, `screenshotPreviewEnabled`, `screenshotSharingEnabled`, `screenshotShowCaptureMenuOnShortcut`, `recorderShowCaptureMenuOnShortcut`, `screenOCRShowCaptureMenuOnShortcut`, `colorPickerShowCaptureMenuOnShortcut`, `screenOCRDetectQRCodes`, `recorderSystemAudio`, `recorderOpenEditor`, `recorderAutomaticZoom`, `recorderSharingEnabled`, `radialMenuAtPointer`, `panelUtilityScreenshot`, `panelUtilityScreenRecorder`.
Numbers / strings: `clipboardHistoryLimit` 50; `clipboardHistoryRetentionDays` 0; `clipboardHistoryMenuBarPreviewLength` 20; `clipboardAutoClearDelaySeconds` 20; `clipboardHistoryLayout` "list"; `shelfDockPlacement` "menuBar"; `scratchpadRetention` "never"; `scratchpadBackgroundOpacity` 0.0; `scratchpadTextSize` 13; `micMuteSavedVolume` 0.75; `wallpaperFilter` "all"; `screenshotDelay` 0; `screenshotFileNumberStart` 1; `screenshotFileNumberNext` 1; `screenshotLoupeDefaultZoom` 1.0; `screenshotPreviewDuration` 3; `screenshotPreviewPosition` ""; `screenshotDefaultAction` ""; `screenshotUploadDuration` 3600; `screenshotToolOrder` (default rail order); `screenshotToolShortcuts` ""; `recorderCountdown` 3; `recorderQuality` "balanced"; `recorderFrameRate` 60; `recorderGIFSize` "medium"; `recorderGIFFrameRate` 12; `colorPickerFormat` "hex"; `radialMenuActivationMode` "pressOrHold"; `radialMenuMouseButton` "off"; `middleClickTapFingers` 0; empty strings/arrays for every list/pattern key.
Shortcut defaults: see §0.4.

---
## 10. Cross-cutting notes and open decisions for implementers

1. **Settings page architecture gap**: WinKnife renders each feature's settings with the generic `SettingsViewBuilder` (toggle/slider/choice/hotkey/text/note). The pages above need: conditional rows (visible/disabled by other settings), disclosure groups, segmented controls, tables (App shortcuts), tight caption blocks, status captions (green/orange), sheets, list editors with drag reorder, colour swatches, key-cap displays. Extend `WinKnife.Core.Ui` (`SettingsCard`, `SettingsViewBuilder`) with these primitives before porting pages.
2. **One page per sidebar entry, several features per page**: Clipboard page = history + paste-image-as-file + paste-plain; Quick tools page = 8 features; Screen capture page = 4 features. WinKnife's feature-per-page model (`IFeature.CreateSettingsView`) needs "page composition" so a page can aggregate several features' sections with their own anchors (`settingsFormSectionAnchor`).
3. **Hotkey enable toggles**: Vorssaint separates "shortcut enabled" (bool) from "shortcut" (value) for almost every feature, with defaults mostly **off** for secondary tools (colour picker, OCR, mic mute, quit apps, camera, scratchpad, command bar, recorder…) and **on** for clipboard, shelf, quick launcher. WinKnife registers most hotkeys always. Replicate the toggles and defaults above; the recorder must support "no modifier required" (F2, per-tool keys) and "clear with Delete".
4. **Windows privilege / UIPI**: hooks, `SendInput` and `RegisterHotKey` do not reach elevated windows unless WinKnife is elevated or has `uiAccess`. Add a single shared orange note (new string) wherever macOS shows the Accessibility PermissionRow.
5. **Hosted services**: "Temporary links" (screenshot + recording) uploads to `https://screenshots.vorssaint.com` (production) in the original. WinKnife must decide: own endpoint, user-configurable `https` endpoint, or ship the section disabled. Same API contract as 6.7.
6. **Dependencies**: screen recording/GIF export -> bundled ffmpeg (or Media Foundation + a GIF encoder); QR decoding -> ZXing.Net or in-house (NuGet beyond NAudio/CommunityToolkit needs approval per ARCHITECTURE.md); `OpenFolderDialog` is available in WPF .NET 8+.
7. **Branding**: replace "Vorssaint" with "WinKnife" in every string including privacy sheets and `deniedMessage`; macOS-only tokens (⌘, Finder, Mac, menu bar, Trash, Spaces, System Settings, Retina, Siri, Accessibility permission) are flagged **[mac]** with Windows replacements; where a replacement is not listed use: Finder -> File Explorer, Mac -> PC, menu bar -> tray (notification area), Trash -> Recycle Bin, System Settings -> Settings, ⌘ -> Ctrl, ⌥ -> Alt, ⌃ -> Ctrl, ⌃⌥⌘ -> Ctrl+Alt+Shift.
8. **Persistence names**: keep Vorssaint's key names (camelCase) as WinKnife setting keys under each feature scope so a future import/export of settings maps 1:1; store shortcuts in WinKnife's `Ctrl+Alt+Shift+K` string form rather than mac key codes.
