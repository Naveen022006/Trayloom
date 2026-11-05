# 02e. Settings pages: App updates, Packages (Homebrew), Cleaner (+ WhatsApp downloads), Uninstaller, Kill process, Port manager, Media, About, What's New, Support + update windows

Source of truth: `vorssaint-mac/Sources/Vorssaint/UI/Settings/{HomebrewSettings, AppUpdatesSettings, WhatsAppDownloadsSettings, SettingsView (lines 614-1010: UpdatesView, AboutSettings, ReleaseNotesSettings, SupportSettings)}.swift`,
`UI/{Cleaner/CleanerView, Uninstall/UninstallerView, Uninstall/AppPickerView, KillProcess/KillProcessView, PortManager/PortManagerView, Media/MediaWorkspaceView, AppUpdates/AppUpdatesListView, HomebrewOperationStatusView, UpdateHighlightsView, Feedback/FeedbackView}.swift`,
`UI/Onboarding/{WhatsNewView, WhatsNewDigestView, WhatsNewSupport}.swift`, services `Services/{Cleaner,Uninstall,KillProcess,PortManager,Media,ManagedDownloads,AppUpdates,Update,Homebrew}/*`,
`App/AppDelegate.swift` (update-window flow, lines ~2190-2640), registered defaults in `Core/Defaults.swift` (`Defaults.registeredDefaults`),
strings in `Core/Localization.swift` (`Strings.enUS`) and `Core/{AppUpdateStrings, WhatsAppDownloadStrings, WhatsAppOrganizerStrings, KillProcessStrings, PortManagerStrings, MediaImageStrings, WhatsNewStrings, FeedbackStrings}.swift` (`static let enUS`).

Conventions are the ones of `02a-settings-general-energy-monitor.md` §0 (SettingsCard, SettingsRow, chips, grouped Form, SF-symbol -> Segoe Fluent Icons map). Additional points:

* All English strings are verbatim. `Vorssaint` is written **Trayloom**. A platform substitution is written `[WIN]` and the original is kept after it (`macOS: "..."`). Strings that exist only on macOS and have no Windows equivalent are marked **[MAC-ONLY]** with the closest Windows equivalent.
* Typographic apostrophes (’ U+2019), ellipsis (… U+2026) and curly quotes (“ ”) are part of the strings; copy them as they are.
* Keys are the raw `UserDefaults` names. On Windows they live in `%APPDATA%\Trayloom\settings.json` through `ScopedSettings` (scope `core` for app-wide keys; scope = feature id for feature-owned keys). Keep the same key names. Defaults listed are the **registered** defaults (`Defaults.registeredDefaults`), which win over the `@AppStorage(... = x)` fallbacks in the views; where the two differ it is called out.
* "Grouped Form" = SwiftUI `Form{}.formStyle(.grouped)` (macOS): window-background canvas, each `Section` is a rounded group (radius ~10, fill `controlBackground`: light `#FFFFFF`, dark `#FFFFFF @ 6%` on a darker canvas), section header above the group in 13 pt semibold secondary colour, rows 10-12 pt padding with a hairline separator between rows, label left / control right, 1-line captions below a row in `caption` (11 pt) secondary colour. Content column is not width-limited (fills the detail pane, 20 pt outer padding). Trayloom implements it as a `SettingsViewBuilder` / `SettingsCard` stack.
* "Panel card" = `panelCard()`: padding 10, radius 10 continuous, fill `cardFill` (light `#FFFFFF @ 38%`, dark `#FFFFFF @ 7.5%`), 0.7 px border `border` (light `#000 @ 9%`, dark `#FFF @ 11%`; high-contrast 24% / 28%). `controlFill` = light `#000 @ 5.5%`, dark `#FFF @ 8.5%`.
* "Soft card" (cleaner cards): padding 11, radius 9 continuous, fill `Color.primary @ 5%`, max width 380.
* All pages below are hosted in the Settings detail pane (see 02a for the window, sidebar, search). Page order inside the sidebar category **App management** (`categories.appManagement` = "App management"): App updates, Cleaner, Homebrew, Uninstaller, Kill Process, Port Manager. Media is the last row of category **Files** (`categories.files` = "Files"), directly above App management (`SettingsDirectory.swift` line 334). About / What's New / Support are the last three rows of category **App** (`categories.app` = "App"), after Shortcuts and Advanced.

---------------------------------------------------------------------------------------------------------------

## 0. Sidebar rows owned by this file

| Page | Sidebar title (EN) | SF symbol | Segoe Fluent glyph | Search keywords (beyond the title) |
|---|---|---|---|---|
| App updates | `AppUpdateStrings.pageTitle` = "App updates" | `arrow.down.app` | E896 (Download) ? | "Check now", "Check in the background", "App Store" [WIN: "Microsoft Store"], "Homebrew" [WIN: "winget"] |
| Cleaner | `cleanerName` = "Cleaner" | `sparkles` | no dedicated glyph in Segoe MDL2; use U+2728 (sparkles) from Segoe UI Symbol (monochrome) | "Automatic cleanup", "WhatsApp downloads", "Clean up automatically", "File types" |
| Homebrew | `homebrewName` = "Homebrew" [WIN: see §2 "Packages"] | `shippingbox` | E7B8 (Package) ? | - |
| Uninstaller | `uninstallerName` = "Uninstaller" | `trash` | E74D (Delete) | - |
| Kill Process | `pageTitle` = "Kill Process" | `xmark.octagon` | EA39 (Blocked2) ? / fallback E711 (Cancel) | "force quit", "process", "cpu", "memory", "kill" |
| Port Manager | `title` = "Port Manager" | `network` | E968 (NetworkTower) / E774 | "port", "ports", "listening", "socket", "PID", "kill port" |
| Media | `mediaName` = "Media" | `photo.on.rectangle.angled` | E91B (Photo2) | "PDF", "GIF", "PNG", "JPEG", "convert", "resize", "watermark", "rename", "profile", "fit", "fill", "crop", "Convert to PDF", `ocrName` |
| About | `tabAbout` = "About" | `info.circle` | E946 (Info) | "Review introduction", "Review highlights" |
| What's New | `tabReleaseNotes` = "What’s New" | `sparkles` | same as Cleaner (U+2728 in Segoe UI Symbol) | - |
| Support | `tabSupport` = "Support" | `heart.fill` | EB52 (HeartFill) ? (EB51 = Heart outline) | "Support on Buy Me a Coffee", "Star Trayloom on GitHub" |

`?` = verify the code point in Character Map ("Segoe Fluent Icons") before shipping. Feature hub icons (FeatureCatalog.symbolName): mediaTools `photo.on.rectangle.angled`, cleaner `sparkles`, uninstaller `trash`, homebrew `shippingbox`, appUpdates `arrow.down.app`, killProcess `xmark.octagon`, portManager `network`.
Hub one-line descriptions (Features hub, `FeatureHubStrings`): `descMediaTools` "Compress videos, images and GIFs"; `descCleaner` "Clear caches and junk files"; `descUninstaller` "Remove apps and their leftovers"; `descHomebrew` "Keep Homebrew packages up to date" [WIN: "Keep your packages up to date"]; App updates `hubDescription` "Find and install updates for the apps you have"; Kill Process `hubDescription` "Search running processes and force quit, restart, or kill process trees"; Port Manager `hubDescription` "View active listening ports and, with Kill Process installed, terminate the processes using them"; WhatsApp downloads `hubDescription` "Keeps WhatsApp files in Downloads under control". Kill Process is flagged **BETA** in the Features hub (`FeatureCatalog.isBeta`: `fanControl`, `killProcess`) with `betaBadge` = "BETA" capsule and `betaFeatureWarning` = "Beta. You may run into some bugs."; there is no beta badge inside the Kill Process page itself. Fresh-install availability (`FeatureCatalog.installedByDefault`, key `featureAvailable.<id>`): Cleaner, Uninstaller, Homebrew/Packages, App updates and Media tools are **installed by default**; Kill Process (beta) and Port Manager are **opt-in** (not installed until the user installs them in the Features hub); a page appears in the sidebar only while its feature is installed. WhatsApp downloads is not a separate feature: it is the opt-in switch `whatsAppDownloadsEnabled` inside the Cleaner page.
Required permissions per feature (`FeatureCatalog` lines 379-382, shown in the Permissions portal): cleaner = Full Disk Access + Files & Folders + Notifications; uninstaller = Full Disk Access + Finder automation; homebrew = App Management; appUpdates = Notifications + App Management. **[MAC-ONLY]** all of them; Windows equivalents are in each page's section (UAC elevation, toast notification setting, Controlled folder access).

---------------------------------------------------------------------------------------------------------------

## 1. App updates page (`AppUpdatesSettings` + shared `AppUpdatesListView`)

### 1.1 Layout (grouped Form, top to bottom)

**Section 1, header = `pageTitle` "App updates"** (header text is the section title)
1. Caption (`caption` 11 pt secondary, wraps): "Looks for newer versions of the apps on this Mac and helps you finish each update from its original source." [WIN: "...apps on this PC..."]
2. `AppUpdatesListView` (vertical padding 2, VStack spacing 12, full width) with, in order:
   a. **Summary row** (HStack baseline, spacing 8): left = last-check line (12 pt secondary): `neverChecked` "Not checked yet" or `lastCheckFormat` "Last checked %@" with a *relative, named* time ("now", "2 minutes ago"... `RelativeDateTimeFormatter` full style, named: a check that just ran reads "now"). Right = bordered small button: idle -> glyph `arrow.clockwise` (Segoe E72C) + `checkNow` "Check now", 12 pt medium; while checking -> mini spinner + `checking` "Checking". Disabled while `isChecking` or a package-manager operation runs.
   b. **Empty state** (when no items): VStack(spacing 6): if a check ran this session and is not running: a label 12 pt medium - text `partialUpToDate` "No updates found in this partial check" (grey, `info.circle`) when coverage is partial; else if rules exist `noVisibleUpdates` "No updates outside your rules" (grey, `info.circle`); else `upToDate` "No updates found" (green, `checkmark.circle.fill`). Then `coverageNote` (11 pt secondary): "Checks the original sources of installed apps and a public catalog. Updates install through their original source." And, if checked and no package manager: `packageMissing` "Homebrew is not installed, so apps cannot be updated from here yet." [WIN: "winget is not installed, so apps cannot be updated from here yet."]
   c. **Non-empty**: selection bar (only when at least one row is selectable): two link-style buttons 11 pt "Select all" (`selectAll`, disabled when all selected) and "Clear" (`clearSelection`, disabled when none), left aligned. Then the list (LazyVStack spacing 5, rows below). Then the primary button (bordered prominent, 12 pt semibold, left aligned, not full width) `updateSelectedFormat` "Update %d" (selected count; disabled when 0 selected or busy) - shown only when a selectable row exists.
   d. **Exceptions disclosure** (always shown): `DisclosureGroup` titled `rulesTitle` "Update exceptions" (12 pt). Body padded-top 4: HStack(top) = `rulesHint` (11 pt secondary: "Skipping a version still allows newer releases. After removing an app exclusion, use Check now to refresh it.") + trailing small bordered "+" button (`plus`, E710) with tooltip/label `addException` "Add an app that should not be checked" (disabled while busy). Opens an app picker (macOS `NSOpenPanel`, `.applicationBundle`, start dir /Applications, message = `addException`) [WIN: pick an installed program from the same picker as the uninstaller, or an .exe]; chosen app -> `addExclusion` (rule with `version = nil`). Below it, when rules exist, one row per rule (HStack spacing 8): name (medium, 1 line) over secondary 1-line text = `skippedVersionFormat` "Skipped version %@" (if the rule has a version) or `excludedApp` "Not checked until this rule is removed"; tooltip = bundle id; trailing small bordered **Remove** (`removeRule` "Remove"; accessibility "Remove: <name>"; disabled while busy). No rules -> nothing (empty).
   e. **Incomplete-check block** (shown when a check ran this session, not running, and (store coverage incomplete or online catalog enabled but unavailable)): orange label `incompleteCheck` "Check incomplete" (`exclamationmark.triangle.fill`, 11.5 pt medium); if unchecked app names exist: comma-joined names (11 pt secondary, full list in tooltip); then `onlineUnavailable` "The online check could not be completed. Other results are still shown."; if the App Store source is incomplete: link button `openAppStore` "Open the App Store" [WIN: "Open the Microsoft Store"].
   f. **Uncomparable-versions block** (only with online catalog on, check done, list non-empty): grey label `uncomparableVersions` "Versions not compared" (`info.circle`), comma-joined app names, then `uncomparableVersionsNote` "No update is shown when an app’s own version can’t be compared with the catalog. Check for updates in the app itself." When this block shows, the empty state's text is the "partial" one.
   g. **Package-manager operation status** (`HomebrewOperationStatusView`, see §2.2) when an upgrade is running or just finished, padded 4.
   h. **Error line** (`lastError`): orange `exclamationmark.triangle.fill` label, 11 pt, max 3 lines.

   **Row (update item)**: HStack spacing 9, padding h7 v5, radius 8 continuous, fill `primary @ 3.5%`:
   * 14x14 checkbox (only when `isSelectable`, i.e. source is not the online catalog; otherwise an invisible 14x14 spacer). Accessibility label = app name.
   * 26x26 app icon (22 in the panel); if the bundle path no longer exists, the generic application icon.
   * VStack(spacing 1): line 1 = app name (12.5 pt medium, 1 line, truncate middle) + **source badge** (8.5 pt semibold accent text on `accent @ 12%` capsule, h5 v1): `homebrewBadge` "Homebrew" [WIN: "winget"] / `appStoreBadge` "App Store" [WIN: "Microsoft Store"] / `onlineBadge` "Online". Line 2 = `versionSummary` "<installed> → <latest>" (10.5 pt secondary).
   * Trailing small bordered button 11 pt medium, disabled while busy: package-manager source -> "Update" (`updateOne`), tooltip "Update"; App Store source -> title `appStoreBadge` "App Store" [WIN: "Microsoft Store"], tooltip `storeHint` "Opens the App Store, where this update is installed" [WIN: "Opens the Microsoft Store, where this update is installed"]; online source -> "Open" (`openApp`), tooltip `openAppHint` "Opens the app so its own updater can finish".
   * (Settings only, not the panel) `ellipsis.circle` borderless menu (E712 More) with tooltip/label `mouseClickDebounce.moreOptions` ("More options" - MouseClickDebounceStrings.moreOptions), for rows with a bundle id; same items as the context menu: **"Skip version %@"** (`skipVersionFormat`, %@ = latest version; disabled if the version is uncomparable or busy) and **"Don’t check this app"** (`excludeApp`).

**Section 2, header = `frequencyLabel` "Check in the background"**
1. Segmented picker (labels hidden, sized to content): "Off" (`frequencyOff`) | "Every day" (`frequencyDaily`) | "Every week" (`frequencyWeekly`). Setting it saves `appUpdatesCheckFrequency`, re-syncs the scheduler, and (if not Off and notify is on) asks the notification permission.
2. If a next check date exists: caption `nextCheckFormat` "Next check %@" (%@ = short date + short time, system locale, e.g. "10/31/26, 9:00 AM").
3. Toggle (switch) `notifyToggle` "Tell me when an app has an update" (default on; **disabled while frequency is Off**; turning on requests notification permission).

**Section 3, header = `sourcesTitle` "Sources"**
1. Toggle `includeHomebrewToggle` "Include Homebrew apps" [WIN: "Include winget apps"] (default on).
2. Toggle `includeStoreToggle` "Include apps from the App Store" [WIN: "Include apps from the Microsoft Store"] (default on) + caption `includeStoreCaption` "Checks store versions using this Mac’s region. Apple installs these updates." [WIN: "Checks store versions using this PC’s region. Microsoft installs these updates."]
3. Toggle `includeOnlineToggle` "Include other installed apps" (default on) + caption `includeOnlineCaption` "Checks directly with app developers when supported, then uses a public catalog. The app’s own updater installs the update."
Each source toggle is **disabled when it is on and it is the only enabled source** (at least one source always stays on). Any change calls `sourceSelectionDidChange()`: if a scan is running, the scan finishes, its answer is discarded and a new scan starts immediately with the new choice; otherwise a scan starts immediately.

**Section 4 (no header)**: Toggle `showInPanel` "Show in panel" (key `panelUtilityAppUpdates`, default on) - shows the compact list in the menu panel (see 01-menu-panel; panel caption `panelCaption` "See which apps have a newer version").

`onAppear` of the page: `checkIfNeeded()` (rechecks when nothing was scanned this process, an update hand-off is pending, or the last scan is older than 10 minutes).

### 1.2 Settings (App updates)

| Key | Type | Default | Allowed | Effect |
|---|---|---|---|---|
| `appUpdatesCheckFrequency` | string | `off` | `off`,`daily`,`weekly` (anything else sanitised to `off`) | Background schedule: daily = every 24 h, weekly = every 7 d after the last check; Off = no timer, no wake observer |
| `appUpdatesNotify` | bool | true | - | Background pass posts one notification; only for apps not announced before |
| `appUpdatesIncludeHomebrewApps` | bool | true | - | Include package-manager results |
| `appUpdatesIncludeAppStore` | bool | true | - | Include store apps |
| `appUpdatesIncludeOnlineCatalog` | bool | true | - | Include publisher feeds + public catalog results |
| `panelUtilityAppUpdates` | bool | true | - | Shows the tool in the menu panel |
| `appUpdatesRules` | string (JSON array `[{bundleID,name,version?}]`) | `"[]"` | rules with `version` set hide only that version (compared by version core); without version hide the app | Exception list |
| `appUpdatesLastCheck` | double (epoch s) | 0 | - | Persisted last-check time |
| `appUpdatesLastCount` | int | 0 | - | Number of visible updates after the last check (panel badge) |
| `appUpdatesNotifiedIDs` | string[] | [] | - | Update ids already announced (survives relaunch; entries drop out when the finding disappears) |

### 1.3 Behaviour
* **Check** (`check(automatic:)`): ignored when the feature is uninstalled; if already checking, remembers an automatic request. Steps: reload rules -> scan installed apps (folders + Spotlight; excludes own bundle) -> package-manager findings (only if Homebrew or Online is on) -> App Store candidates (apps not covered by the package manager) via the iTunes lookup in the Mac's region -> publisher feeds (Sparkle-style feeds) + public online catalog for the rest (force-refresh only for manual checks) -> merge (`AppUpdatesSupport.merged`), hide by rules, reconcile selection (new items start selected only if selectable; previous selection kept for known ids). `lastCheck` and `appUpdatesLastCheck/Count` are written when done. The scan result is discarded if the generation changed (source switch).
* **Background schedule**: next fire = `lastCheck + interval`; if that is in the past (or never checked) = now + 180 s (catch-up). One-shot timer with 60 s tolerance, re-armed after each automatic run; wake-from-sleep re-computes. Only the automatic pass notifies and re-arms.
* **Notification**: title = `pageTitle` ("App updates"); body = `notificationBodyOne` "One app has a newer version." when exactly one, else `notificationBodyFormat` "%@ apps have a newer version." (count). Only when there are *fresh* ids (not yet announced) and notify is on; announced set stored in `appUpdatesNotifiedIDs`.
* **Update one / selected**: package-manager rows go through one upgrade command (`brew upgrade --cask --greedy <tokens>`); App Store rows hand off (open the item's store page, or the Updates page `macappstore://showUpdatesPage`); online rows just launch the app. After a successful package upgrade the list re-checks itself. After any hand-off the app records a pending hand-off and re-checks as soon as the app/page becomes active again (`applicationBecameActive`).
* **Rules**: `skipVersion` stores `{bundleID,name,version=latest}`; `excludeApp`/`addException` store `{bundleID,name,version=nil}`; both refused while a scan runs; removing an *app* exclusion resets `hasCheckedThisSession` so the empty state does not claim "No updates found" until Check now is pressed. Rules re-load from the preference on appear (backup restore).
* Version comparison: semantic-ish `versionCore`; versions that cannot be compared are never shown as updates and their app names go to the "Versions not compared" list.

### 1.4 Windows mapping
* **Package manager source = winget** (`winget upgrade` table parse; prefer `winget upgrade --accept-source-agreements --disable-interactivity`; the existing `Winget.ParseUpgrade` can be reused). Row id = winget package `Id`; token for the upgrade = `Id`; command `winget upgrade --id <Id> --exact --silent --accept-source-agreements --accept-package-agreements --disable-interactivity` (one command per id, sequential, or one `winget upgrade --id a --id b`? - winget accepts only one `--id` per call, so run sequentially while presenting a single operation status "Updating packages"). Apps with `Unknown` installed version are listed only when the Trayloom extra `includeUnknown` is on (see gap).
* **"App Store" source = Microsoft Store**: UWP/MSIX apps from `Windows.Management.Deployment.PackageManager` (family name), latest version from `winget upgrade --source msstore` output or `https://displaycatalog.mp.microsoft.com/v7.0/products?bigIds=<productId>&market=<region>&languages=en-US` ; action opens `ms-windows-store://downloadsandupdates` (or `ms-windows-store://pdp/?productid=<id>` for one). Badge/button label "Microsoft Store".
* **Online catalog** = apps from registry Uninstall keys not covered by winget/Store; use the publisher feed when it exists (GitHub releases of the app's repo: `https://api.github.com/repos/<owner>/<repo>/releases/latest` when the registry `URLInfoAbout`/`HelpLink` points to github.com; Squirrel `RELEASES`/`latest.yml`; Sparkle appcast `.xml`), else a public catalog (winget manifests index / `https://api.winget.run`-style, or chocolatey community feed). "Open" launches the app exe (`DisplayIcon`/`InstallLocation` exe) so its own updater runs.
* Identity for rules: `bundleID` -> winget `Id` or registry key name (ProductCode / `{GUID}`) or MSIX `PackageFamilyName`; keep the JSON shape `{bundleID, name, version}` (field name `bundleID` kept for settings-backup compatibility).
* Installed-app scan: HKLM/HKCU `...\Uninstall` (64+32 bit), MSIX packages, Start-menu `App Paths`; icon from the exe (`SHGetFileInfo`/`ExtractAssociatedIcon`) or MSIX logo.
* Timer: `System.Threading.Timer` + `SystemEvents.PowerModeChanged(Resume)` + `SystemEvents.TimeChanged` re-arm. Notification: toast (`Microsoft.Toolkit.Uwp.Notifications`/`AppNotificationManager`). Notification permission-denied state: `ToastNotifier.Setting != Enabled`; warning copy + "Open Notification Settings…" -> `ms-settings:notifications` (see §3.2.1 item 5 for the strings `cleanerNotifDenied` / `cleanerNotifOpenSettings`).
* Elevation: many installers need UAC; winget prompts itself. Show the status view with `homebrewTerminalFallback`-style text "This operation needs your terminal..." [WIN] -> "This operation needs administrator approval. Continue in the elevated window." and "Open terminal" -> open an elevated `wt`/`powershell` running the printed command.
* The "App Management" permission is **[MAC-ONLY]**; no Windows equivalent needed.
* First-run winget source agreement (existing Trayloom `agreementsAccepted` consent page) stays as a Trayloom-specific extra shown before the first check (not in Vorssaint).

### 1.5 Gap vs Trayloom (`Trayloom.Features.Tools/AppUpdates/AppUpdatesFeature.cs`)
Has: winget detection, `winget upgrade` table parsing, a stand-alone window (DataGrid: Name/Id/Installed/Available, buttons Check/Update selected/Update all/Cancel, log TextBox, consent card), settings = hotkey + `includeUnknown` toggle, key `agreementsAccepted`.
Lacks: the whole page layout (summary row with relative "Last checked", empty states, selection bar, row cards with icon/source badge/"Update" button/more menu, exceptions disclosure + rules, incomplete/uncomparable blocks); background schedule (`appUpdatesCheckFrequency`, next-check caption, notify toggle + notification); Sources toggles (winget / Microsoft Store / online catalog) and the "last source cannot be turned off" rule; "Show in panel" toggle (`panelUtilityAppUpdates`); persisted last check/count; rules/skip-version; Store and online-catalog sources; operation status card (progress, elapsed, details, cancel, clear); keys listed in 1.2. Uses a modal window and `MessageBox` confirmations where Vorssaint updates immediately without a confirm (selected/one update starts directly; only the package manager's own prompts appear).

---------------------------------------------------------------------------------------------------------------

## 2. Homebrew page -> Windows "Packages" page (`HomebrewSettings` + `HomebrewOperationStatusView` + `HomebrewTrustCard`)

This is a two-pane package browser, not a Form. Windows proposal: keep the exact layout and replace Homebrew by the Windows package managers. Recommended product naming: page title **"Packages"** (sidebar "Packages", hub description "Keep your packages up to date"), toolbar/pickers speak of the *source* (winget / Scoop / Chocolatey). If a literal 1:1 string diff is wanted, every "Homebrew" below becomes the active manager's display name ("winget", "Scoop", "Chocolatey").

### 2.1 Layout (page = VStack spacing 0)
1. **Page header** (padding h16 v12): `Label(homebrewName "Homebrew", systemImage "shippingbox")`, 14 pt semibold. Divider.
2. **Body**, two cases:
   * **Missing state** (no manager found; `brewPath == nil`) centred VStack (padding 28, spacing 12): 44 pt light `shippingbox` (secondary); `homebrewMissingTitle` "Homebrew not found" (16 pt semibold); `homebrewMissingBody` "Trayloom can open the official Homebrew installer in your terminal. It shows the steps and asks for your password if needed." (callout secondary, centre, max 360); prominent button `terminal` + `homebrewInstallHomebrew` "Install Homebrew"; caption (centre, max 360) = `homebrewInstallHomebrewOpened` "Installer opened in your terminal." once clicked, else `homebrewInstallHomebrewCaption` "When the installer finishes, come back here and click Refresh."; optional orange error label; plain button `arrow.clockwise` + `homebrewRefresh` "Refresh".
   * **Content**:
     a. **Toolbar** (padding h16 top16 bottom12, VStack spacing 9):
        * Row 1 (ViewThatFits: single HStack, else two rows): search field (rounded-border text field, placeholder `homebrewSearchPlaceholder` "Search packages", min width 150, Return runs the search), kind picker (menu, width 116: "Casks" `homebrewCasks` / "Formulae" `homebrewFormulas`, default Casks), button `magnifyingglass` + `homebrewSearchButton` "Search" (disabled when query empty or busy), icon button `arrow.clockwise` with tooltip `homebrewRefresh` "Refresh" (disabled when busy), button `arrow.triangle.2.circlepath` + `homebrewUpdateHomebrew` "Update Homebrew" (disabled when busy).
        * Row 2: segmented picker width 300: "All <n>" (`homebrewAll`) / "Casks <n>" / "Formulae <n>" (counts of installed); then the **outdated summary**: while loading outdated -> mini spinner + `homebrewUpdates` "Updates"; else if outdated count > 0 -> orange caption `arrow.up.circle.fill` + "Updates <n>" and a small button `arrow.up.circle` + `homebrewUpgradeAll` "Update all".
        * Row 3 (right aligned): small caption checkbox `homebrewGroupDependencies` "Group dependencies" (key `homebrewGroupDependencies`, default **true**).
     b. If the shell is not configured (`!isShellConfigured`): Divider + **shell setup banner** (padding h16 v10): `terminal` 15 pt, title `homebrewShellSetupTitle` "Finish terminal setup" (12 semibold), body `homebrewShellSetupBody` "Homebrew is installed, but your terminal may not find the brew command yet. Trayloom can open the setup command in your terminal." (or `homebrewShellSetupOpened` "Command opened in your terminal. Then come back here and click Refresh." after the click), prominent small button `wrench.and.screwdriver` + `homebrewShellSetupButton` "Set up terminal", small icon `arrow.clockwise` (tooltip "Refresh"). **[MAC-ONLY]** (PATH not configured); Windows: show the same banner when the manager is installed but not on PATH ("winget"/"scoop" shim folder missing from PATH) with a button that appends the shim folder to the user PATH; otherwise never shown.
     c. Divider, then HStack: **package list** (fixed width 232, scroll, LazyVStack spacing 14, padding h10 v12) | Divider | **detail pane** (fills).
   * **Package list sections** (`packageSection`: header row = title 11 pt semibold secondary + optional count capsule (9 pt rounded semibold, `primary @ 6%` capsule), padded h6; content below, spacing 7):
     1. "Results" (`homebrewSearchResults`) with count: shows loading row (spinner + `homebrewLoading` "Loading…") while searching; results rows; if the query is non-empty and nothing found: count 0 + message `homebrewSearchEmpty` "No results". Absent when the query is empty and there are no results.
     2. "Installed" (`homebrewInstalled`) with count of the filtered list: loading row, or message `homebrewNoPackages` "No packages found", or rows. Updates sort first (`updatesFirst`). With "Group dependencies" on, installed packages that exist only as dependencies of another installed package are folded under their parent: a 12x20 chevron (`chevron.right`/`chevron.down`, 9 pt semibold, invisible when no dependencies; tooltip "Dependencies: <n>" (`homebrewDependencies`); VoiceOver value Expanded/Collapsed) toggles the dependency rows (indented 28).
     3. "No longer needed" (`homebrewOrphans`) with count (only when not loading and orphans exist): note `homebrewOrphansNote` "Installed as dependencies, but no installed package needs them any more." + rows.
   * **Package row** (HStack spacing 8, padding h6 v6, radius 6, selected fill `accent @ 12%`): icon (12 pt semibold secondary, width 16): `terminal` for a formula, `app` for a cask; name (12 pt, semibold when selected, 1 line) over version-or-token (10 pt secondary); if the package has outdated dependencies: orange 10 pt `homebrewDependencyUpdatesFormat` "Dependency updates: %d"; popularity badge (10 pt semibold accent on `accent @ 12%` capsule; compact count e.g. "12K"; tooltip `homebrewPopularityFormat` "%@ installs in %@ days"); trailing: mini spinner if an operation is running for this package, else an orange `arrow.up.circle.fill` 13 pt button (tooltip "Update: <installed → latest>", `homebrewUpgrade`) if outdated, else green `checkmark.circle.fill` 11 pt if installed. Click selects (loads details). Context menu: "Package details" (`homebrewDetailsTitle`, `info.circle`), "Update" (`arrow.up.circle`, only if outdated), "Uninstall" (`trash`, destructive, if installed) or "Install" (`arrow.down.circle`, if not); all disabled while busy.
   * **Detail pane** (scroll, padding 16, spacing 14): spinner `homebrewLoading` while loading details; package detail or empty detail; trust card; error label; below the scroll, when an operation exists: Divider + operation log (padding 12).
     * *Empty detail*: `Label(homebrewDetailsTitle "Package details", shippingbox)` 18 pt semibold + `homebrewNoSelection` "Select an installed package or search for a new one." (secondary).
     * *Package detail* (VStack spacing 12): title row = display name (18 pt semibold, 2 lines) + **status badge** (10 pt semibold capsule h7 v3 `colour @ 12%`): in-progress action title (accent) / `homebrewUpdateAvailableBadge` "Update available" (orange) / `homebrewInstalledBadge` "Installed" (green) / `homebrewNotInstalledBadge` "Not installed" (secondary). Then the token (monospaced caption, selectable). Description (callout). Detail rows (caption, label secondary left / value right selectable): "Version" (`homebrewVersion`, installed version or stable), "Latest" (`homebrewLatestVersion`, only when outdated), "Type" (`homebrewDescription` - value is "Casks" or "Formulae"), "Popularity" (value = popularityFormat), link "Open website" (`homebrewHomepage`). Action row (disabled while busy): if an operation runs for it: `hourglass` label with the active-action title; else if installed: [prominent `arrow.up.circle` "Update" if outdated] + destructive `trash` "Uninstall"; else prominent `arrow.down.circle` "Install".
   * **Trust card** (`HomebrewTrustCard`, orange `@ 9%` fill, 10 radius, orange `@ 22%` 1 px stroke, padding 12): `checkmark.shield` + `homebrewTrustTitle` "Tap not trusted yet" (headline); `homebrewTrustCaption` "Homebrew now asks for your confirmation before using third party taps. Trust %@ to continue."; prominent button `homebrewTrustButton` "Trust and continue" (spinner while trusting). **[MAC-ONLY]** (brew tap trust) - no Windows equivalent except Chocolatey/Scoop untrusted buckets: show the same card for a custom Scoop bucket / winget non-default source if one refuses; otherwise omit.
   * **Confirmation dialog** (before every mutating action; destructive role on uninstall): title + body, buttons = action title + "Cancel" (`uninstallerCancel`). Titles/bodies: `homebrewConfirmInstallTitle` "Install with Homebrew?" / `homebrewConfirmInstallBodyFormat` "Homebrew will download and install %@. Dependencies may also be installed."; `homebrewConfirmUninstallTitle` "Uninstall with Homebrew?" / `...BodyFormat` "Homebrew will uninstall %@. Configuration files may remain on the system."; `homebrewConfirmUpgradeTitle` "Update with Homebrew?" / "Homebrew will download and apply the latest version of %@. Dependencies may also be updated."; `homebrewConfirmUpgradeAllTitle` "Update all with Homebrew?" / `homebrewConfirmUpgradeAllBody` "Homebrew will download and apply the latest versions for packages with updates available. Dependencies may also be updated."; `homebrewConfirmUpdateHomebrewTitle` "Update Homebrew?" / `homebrewConfirmUpdateHomebrewBody` "Homebrew will fetch the latest information and then reload your packages." Action buttons: "Install" (`homebrewInstall`), "Uninstall" (`homebrewUninstall`), "Update" (`homebrewUpgrade`), "Update all" (`homebrewUpgradeAll`), "Update Homebrew" (`homebrewUpdateHomebrew`).
3. `onAppear`: load installed if empty. Changing query or kind clears the selection/pending action/details; changing the installed filter clears the selection if it is no longer visible; a changed operation target closes the details log.

### 2.2 Operation status card (`HomebrewOperationStatusView`, also used in App updates and the Uninstaller)
VStack (spacing 9; 7 compact): **header** = 28 pt circle (`icon colour @ 14%`) with an SF icon (12 pt semibold) - running: the action's `runningSystemImage` (accent), succeeded `checkmark.circle.fill` (green), failed `exclamationmark.triangle.fill` (orange), cancelled `xmark.circle.fill` (secondary), needs-terminal `terminal.fill` (orange); title (13 pt semibold, 2 lines) and sub line (11 pt secondary: while active "<phase> | " then "<elapsed> elapsed"); trailing mini button: **Cancel** (`homebrewCancelOperation` "Cancel") while active, **Clear log** (`homebrewClearLog` "Clear log") when finished. **Progress**: while active a linear determinate bar if brew reports a fraction, else a 5 px indeterminate capsule (accent @ 72%, 34% width, sliding -0.35 -> 1.05 over 1.15 s linear, repeating) + caption (tertiary 10 pt) `homebrewOperationProgressUnknown` "Homebrew has not reported a percentage yet."; on success a full bar. Last activity line (11 pt secondary, 3 lines). If `needsTerminal`: label `terminal` + `homebrewTerminalFallback` "This operation needs your terminal to ask for the administrator password. Trayloom does not capture passwords." and, if a command is known, small buttons "Open terminal" (`homebrewOpenTerminal`) and `doc.on.doc` "Copy" (`menuCopy`). If a log exists: plain toggle `chevron.down/up` + `homebrewOperationShowDetails` "Show details" / `homebrewOperationHideDetails` "Hide details"; expanded: "Technical details" (`homebrewOperationTechnicalLog`, 11 pt semibold tertiary) + mini "Copy" button + monospaced 10 pt scroll log (96 pt tall; 58 compact).
Titles (running / succeeded / failed): "Installing %@" / "%@ installed."; "Uninstalling %@" / "%@ uninstalled."; "Updating %@" / "%@ updated."; "Updating packages" / "Packages updated."; "Updating Homebrew" / "Homebrew updated."; failed "Could not finish %@." (name = package display name, or `homebrewAllPackages` "packages"); cancelled "Operation cancelled."; needs-terminal "Continue in your terminal." Phases: `Preparing…`, `Downloading files…`, `Installing files…`, `Removing files…`, `Updating files…`, `Finishing…`, `Refreshing list…`. Elapsed: `homebrewOperationElapsedFormat` "%@ elapsed" with "<s>s" under a minute, "<m>min", "<h>h <m>min". The `…` is U+2026.

### 2.3 Settings
| Key | Type | Default | Effect |
|---|---|---|---|
| `homebrewGroupDependencies` | bool | true | Folds dependency-only installed packages under their parent (and lists unreferenced ones under "No longer needed") |
All other state (query, kind, filter, selection, expansion) is transient.

### 2.4 Behaviour
* Search: `brew search --cask|--formula <query>` (kind from the picker), results reconciled with the installed list (installed ones show the installed state); popularity from the Homebrew analytics JSON (cached per kind); one operation lane: install/uninstall/upgrade/upgrade-all/update never run concurrently (`isBusy`).
* Details: `brew info --json=v2 --cask|--formula <name>`; installed list: `brew info --json=v2 --installed`; outdated: `brew outdated --json=v2` (+ `--cask --greedy` for the update-check feature). Tokens validated `^[A-Za-z0-9][A-Za-z0-9._+@/-]*$`, no `..`, no leading `-`.
* Operations stream output into the log; phase/percent/activity are parsed from it; password-requiring casks hand over to the user's default terminal with the command copied/printed (no Automation permission). Completed install/uninstall/upgrade refresh the installed list and clear selection (list scrolls to top on success).
* Uninstaller integration: a Homebrew-managed app removed in the Uninstaller goes through the same lane (§5).

### 2.5 Windows mapping
* Managers (detect in this order, show the first found; the toolbar's kind picker becomes a **source picker** when more than one exists):
  * **winget**: search `winget search "<q>" --accept-source-agreements` (table), installed `winget list`, outdated `winget upgrade`, details `winget show --id <Id>` (fields: Version, Publisher, Description, Homepage, License, Installer type), install `winget install --id <Id> --exact --accept-package-agreements --accept-source-agreements`, uninstall `winget uninstall --id <Id> --exact`, upgrade `winget upgrade --id <Id> --exact`, upgrade all `winget upgrade --all`, "Update Homebrew" -> `winget source update`. Token validation: ids `^[A-Za-z0-9][A-Za-z0-9._+-]*$`.
  * **Scoop**: `scoop search`, `scoop list`, `scoop status`, `scoop info <app>`, `scoop install|uninstall|update <app>`, `scoop update *`, "Update" = `scoop update`. **Chocolatey**: `choco search <q> --limit-output`, `choco list --local-only`, `choco outdated`, `choco info`, `choco install|uninstall|upgrade -y`, `choco upgrade all -y` (needs elevation).
  * Kinds: Cask/Formula distinction does not exist; use "Apps" vs "Command-line tools" only if the manager exposes it (Scoop: buckets `main` = CLI vs `extras` = GUI; Chocolatey: package tags `cli`/`gui`); otherwise hide the kind picker and make the installed filter "All <n>" + one segment per detected source ("winget <n>", "Scoop <n>", "Chocolatey <n>").
  * Popularity: winget has none -> hide the popularity badge/row (Scoop/Chocolatey: download counts from the Chocolatey API only).
  * "Group dependencies": Scoop/Chocolatey know dependencies (`scoop depends`, `choco info`); winget does not -> hide the toggle for winget.
  * Missing state: "Install winget" button opens `https://aka.ms/getwinget` / Microsoft Store App Installer page (`ms-windows-store://pdp/?productid=9NBLGGH4NNS1`); caption "When the installer finishes, come back here and click Refresh."; Windows 10 1809+/11 normally ships winget (App Installer).
  * Elevation (needs-terminal state): launch the command in an elevated Windows Terminal/PowerShell (`Start-Process wt -Verb RunAs`), copy button as in the original; Trayloom never captures the UAC prompt.
* Log: stream `StandardOutput/Error` (strip progress spinner characters `\ | / -` and CR-overwritten lines; winget prints `  ████▒▒  34%` bars -> parse the percent for the determinate bar).
* Icons: package row icons = Segoe E7B8 (Package) for apps, E756 (CommandPrompt) for CLI; chevrons E76C/E70D; badges identical.

### 2.6 Gap vs Trayloom
No Packages/Homebrew page exists at all (only the winget-based App updates window). Needs: the two-pane browser, search, installed list with filter counts, outdated detection and "Update all", detail pane, confirmations, operation status card, key `homebrewGroupDependencies`, missing-state installer flow. The winget argument builders and table parser in `AppUpdatesFeature.cs` can be reused. Sidebar entry and Features-hub entry (`homebrew`, installed by default) are missing.

---------------------------------------------------------------------------------------------------------------

## 3. Cleaner page (`CleanerSettings` wrapper + `CleanerView` + `JunkCleaner`/`CleanerScheduler`)

### 3.1 Page frame (`CleanerSettings`)
VStack(spacing 0), fills the pane, top aligned.
1. **Tool switcher** - shown **only when** `whatsAppDownloadsEnabled` is on: segmented picker (labels hidden, max width 440, padding h24 v14) with two segments, each icon + text: `sparkles` + "Cleaner" (`cleanerName`) | `arrow.down.doc` + "WhatsApp downloads" (`WhatsAppDownloadStrings.title`). Then a Divider. When WhatsApp downloads is off there is no switcher and the page is the plain cleaner.
2. Content: `WhatsAppDownloadsSettings` (§4) when the switcher is on the WhatsApp segment, else `CleanerView`.
3. Behaviour: `onAppear` -> tool = system if WhatsApp is off; consumes the one-shot hint `router.cleanerTool` ("whatsApp" from the panel's "Manage…" button; honoured only if WhatsApp is enabled). When `whatsAppDownloadsEnabled` flips: tool = (enabled ? WhatsApp : system); `WhatsAppDownloadScheduler` and `WhatsAppDownloadOrganizer` re-sync with preferences; on disable the WhatsApp manager `reset()`s and the organizer `stop()`s.

### 3.2 States of `CleanerView` (settings variant, `compact = false`; the panel hosts the same view with `compact = true` and tighter padding - see 01-menu-panel)
`JunkCleaner.phase`: `idle` -> `scanning` -> `results` -> `cleaning` -> `done(freed, failed)`. Every centred state is wrapped in a ScrollView whose content has `minHeight = pane height`, so it stays vertically centred.

#### 3.2.1 Idle (padding 28, VStack spacing 18, everything centred, Spacer above and below)
1. Sparkle glyph: `sparkles` 46 pt light, secondary (animates only while busy).
2. Title "Clean up your Mac" [WIN: "Clean up your PC"] (`cleanerIntroTitle`, 17 pt semibold).
3. Caption (`cleanerIntroCaption`, 12 pt secondary, centre, max width 380): "Scans for leftovers from uninstalled apps, caches, logs and the Trash. You review everything first and removed items go to the Trash." [WIN: "...caches, logs and the Recycle Bin. You review everything first and removed items go to the Recycle Bin."]
4. **Scan** button (`cleanerScan` "Scan"): large, bordered prominent. -> `scan(attended: true)`.
5. **Schedule card** (full variant, soft card, max 380, VStack spacing 8):
   * Row: `clock` (secondary) + "Automatic cleanup" (`cleanerScheduleTitle`, 12 pt medium) + spacer + frequency menu picker (fixed size): "Off" (`cleanerScheduleOff`) / "Daily" (`cleanerScheduleDaily`) / "Weekly" (`cleanerScheduleWeekly`).
   * When frequency != Off (ViewThatFits horizontal, falls back to two rows): weekday menu picker (Weekly only; seven standalone weekday names, capitalised, values 1 = Sunday ... 7 = Saturday, **not** locale-first-day ordered) + time pickers: hour menu (12 h locale: 1...12; 24 h locale: 0...23, shown as plain numbers) + minute menu (":00", ":05" ... ":55", step 5, plus the stored value if it is off-grid, sorted) + AM/PM menu (12 h locale only; labels = the locale's AM/PM symbols).
   * Checkbox "Notify when done" (`cleanerScheduleNotifyToggle`, 11.5 pt).
   * If notify is on and the system denied notifications: orange caption2 `cleanerNotifDenied` "Trayloom notifications are turned off in the system." + small button `cleanerNotifOpenSettings` "Open Notification Settings…" (opens the OS notification settings; re-read on every app activation, with a 1 s delay after toggling so a just-shown permission prompt can be answered first).
   * Caption2 secondary `cleanerScheduleNextFormat` "Next cleanup %@." (relative medium date + short time, e.g. "Tomorrow, 9:00 AM") - present when the scheduler has a fire date.
   * Caption2 tertiary last-run line (only if `cleanerLastAutoRun > 0`): if last freed > 0 -> `cleanerScheduleLastFormat` "The last automatic cleanup freed %@." else `cleanerScheduleRanFormat` "Last automatic cleanup %@." (date); if the pass left items, append " " + `uninstallerSomeFailed` "Some items couldn’t be moved to the Trash." [WIN: "...to the Recycle Bin."]
   * Caption2 secondary `cleanerScheduleCaption` "Cleans only the safe part on its own at the chosen time and sends everything to the Trash." [WIN: "...Recycle Bin."]
6. **Forgotten screenshots card** (soft card): row `camera.viewfinder` (secondary) + "Forgotten screenshots" (`cleanerCatScreenshots`, 12 pt medium) + menu picker (fixed size): "Off" (0) then "After 7 days", "After 14 days", "After 30 days", "After 60 days", "After 90 days" (`cleanerScreenshotsAfterFormat` "After %d days"); if the stored value is not one of these it is added (sorted). Caption2 secondary (settings page only): `cleanerScreenshotsSettingCaption` "The scan lists the screenshots you have not opened for this long, unchecked, with their total size."
7. **WhatsApp opt-in card** (soft card, settings page only, only when WhatsApp downloads is off): switch "WhatsApp downloads" (12 pt medium; key `whatsAppDownloadsEnabled`) + caption2 "Keeps WhatsApp files in Downloads under control". Turning it on switches the page to the WhatsApp segment.
8. **Permission note** when Full Disk Access is missing (`FullDiskAccessNote`, width 380): **[MAC-ONLY]** see §3.5 for the Windows equivalent.

#### 3.2.2 Scanning / Cleaning (busy, min height 320, spacing 16)
Sparkle glyph 54 pt (variable-colour twinkle animation while busy; static if reduce-motion), text "Scanning…" (`cleanerScanning`) or "Cleaning…" (`cleanerCleaning`) in secondary, optional detail line (caption tertiary) = title of the display group being scanned (e.g. "Caches", "Logs", "Trash"). While scanning there is a large **Cancel** (`uninstallerCancel`) which resets (cancels the background loop and returns to idle); while cleaning there is no cancel.
Scan order: leftovers, startup items, caches, logs, developer, trash, device backups, screenshots (screenshots only when the scan is "attended" and the age setting is not Off).

#### 3.2.3 Results (VStack spacing 0)
1. **Header** (padding 16): sparkle glyph 22, VStack: "Cleaner" (15 pt semibold) over "<total size> found" (caption secondary, `ByteCountFormatter` file style + space + `uninstallerFoundTitle` "found"); trailing plain `xmark.circle.fill` 16 pt secondary button -> reset to idle. Divider.
2. If there are no items: centred green `checkmark.seal` 34 pt light + "Nothing to clean. Your Mac is tidy." [WIN: "...Your PC is tidy."] (`cleanerNothingFound`, 13 pt secondary). No footer.
3. Else an inset `List` with two sections (a section is omitted if all its groups are empty):
   * Section "Safe cleanup" (`cleanerSafeSection`): groups **Orphaned startup items**, **Caches**, **Logs**, **Developer junk** - all items pre-checked.
   * Section "Optional, review first" (`cleanerOptionalSection`): groups **Leftovers from uninstalled apps**, **Other caches**, **iPhone backups**, **Forgotten screenshots**, **Trash** - all items unchecked.
   * **Group row** = `DisclosureGroup` (collapsed by default), label HStack(spacing 10): group checkbox (checked only if the group is non-empty and every item is included; toggling sets all items), icon (14 pt secondary, 20 wide), VStack: title (13 pt medium) over caption (10.5 pt secondary, wraps), trailing group size (11.5 pt secondary monospaced digits). Icons/titles/captions:

     | Group | SF symbol | Title | Caption |
     |---|---|---|---|
     | loginItems | `power` | "Orphaned startup items" | "Startup entries left by apps that no longer exist." |
     | safeCaches | `archivebox` | "Caches" | "Temporary files apps rebuild on their own." |
     | logs | `doc.text` | "Logs" | "Old diagnostic logs." |
     | developer | `hammer` | "Developer junk" | "Xcode build and simulator leftovers." [WIN: "Visual Studio, NuGet and build leftovers."] |
     | leftovers | `puzzlepiece` | "Leftovers from uninstalled apps" | "Files left behind by apps you uninstalled." |
     | otherCaches | `internaldrive` | "Other caches" | "Safe to remove, nothing breaks. Apps may open slower once and downloaded content, like offline music, downloads again." |
     | deviceBackups | `iphone` | "iPhone backups" | "Old iPhone and iPad backups take a big slice of the storage macOS calls Other. Remove only the ones you no longer need; a new backup is made when you plug the device in again." [WIN: "...take a big slice of your disk. Remove only..."] |
     | screenshots | `camera.viewfinder` | "Forgotten screenshots" | "Screenshots you have not opened in %d days that still have the name and folder macOS gave them. Ones you renamed or moved never show up here." [WIN: "...the name and folder Windows gave them..."] (%d = the age setting) |
     | trash | `trash` | "Trash" [WIN: "Recycle Bin"] | "Emptying the Trash is permanent." [WIN: "Emptying the Recycle Bin is permanent."] |

   * Disclosure body: for **Leftovers** a note first - orange caption2 `cleanerLeftoversNote` "Found by analysis and left unchecked. Check the path before ticking."; for **startup items** a caption2 secondary `cleanerLoginItemsNote` "The entry under Login Items disappears after restarting the Mac." [WIN: "The entry under Startup apps disappears after signing out or restarting the PC."]. Then one **item row** each (HStack spacing 10, left padding 8): checkbox, VStack: name (12 pt, 1 line, truncate middle) over parent folder with the home directory replaced by `~` (10 pt tertiary, truncate head), trailing size (11 pt secondary, monospaced digits). Context menu: "Reveal in Finder" (`cleanerRevealInFinder`) [WIN: "Show in File Explorer"].
4. Divider + **footer** (padding 16): "%d of %d selected" (`uninstallerSelectedFormat`, 12 pt secondary); spacer; **Cancel** (`uninstallerCancel`, resets); **"Clean %@"** (`cleanerCleanSizeFormat`, %@ = size of the selection; large, bordered prominent; disabled when nothing is selected) -> `cleanSelected(escalate: true)`.

#### 3.2.4 Done (padding 28, spacing 16, min height 320, centred)
Green `checkmark.circle.fill` 54 pt; "Done!" (`uninstallerDoneTitle`, 20 pt bold); "%@ removed" (`uninstallerFreedFormat`, size freed, 13 pt secondary); note `cleanerDoneNote` "Items went to the Trash and can be recovered from there." [WIN: "...went to the Recycle Bin..."] (caption tertiary, centre, max 340); if any failed: orange caption `uninstallerSomeFailed` "Some items couldn’t be moved to the Trash." [WIN: Recycle Bin]; large **"Scan again"** (`cleanerAgain`) -> reset to idle.

### 3.3 Settings (Cleaner)
| Key | Type | Default | Allowed | Effect |
|---|---|---|---|---|
| `cleanerScheduleFrequency` | string | `off` | `off`,`daily`,`weekly` | Automatic cleanup frequency |
| `cleanerScheduleHour` | int | 9 | 0...23 (clamped) | Fire hour (24 h storage) |
| `cleanerScheduleMinute` | int | 0 | 0...59 (menu offers 5-min steps + stored value) | Fire minute |
| `cleanerScheduleWeekday` | int | 2 (Monday) | 1 = Sunday ... 7 = Saturday | Weekly day |
| `cleanerScheduleNotify` | bool | true | - | Notification after an automatic pass; ticking requests notification permission |
| `cleanerLastAutoRun` | double (epoch s) | 0 | - | Last automatic run time |
| `cleanerLastAutoFreed` | int bytes | 0 | - | Bytes freed by the last automatic run (0 -> "ran" wording) |
| `cleanerLastAutoFailed` | int | 0 | - | Items left in place by the last automatic run |
| `cleanerScreenshotAgeDays` | int | 30 | 0 (off) or 1...3650 (UI offers 7,14,30,60,90) | Age threshold of the "Forgotten screenshots" search |
| `whatsAppDownloadsEnabled` | bool | false | - | Shows the WhatsApp tool (§4); one-time migration: set true when automation or organizer was already used |

### 3.4 Behaviour (Cleaner)
* **Safety model** (`JunkCleaner`): review first; every removal is a reversible move to the Trash (only the Trash group itself is permanent - done through Finder "Empty Trash"); scoped roots only (fixed well-known junk locations + top level of screenshot folders, never inside another app's support tree, symlinks never followed, each item bound to its file identity and re-validated right before removal; a last-resort `mayRemove` guard refuses critical roots); a find is pre-checked only when evidence is strong and rebuilding is cheap (`CleanerPolicy`).
* **Pre-check policy**: leftovers off; orphaned startup items on (needs two independent signals: every executable gone AND no living owner for the label); logs on; developer junk on; device backups off; screenshots off; caches: third-party bundle-id-named folders on unless sensitive (`ms-playwright`) or Apple's (`com.apple.*` off), plain-named folders on only for a known list (homebrew, pip, node-gyp, yarn, npm, google, electron, cypress, typescript, puppeteer); entries never listed at all: the app's own data and system entries known to break things (CloudKit, coreaudio, systempreferences, controlcenter, finder, dock, FontRegistry, ATS, akd, AuthKit, `.localized`, Pace/NI/FabFilter licensing, Spotify client).
* **Leftover detection**: bundle-id shaped names under `~/Library` roots (direct children only) whose owner is not installed, not running, not Apple, not related to any installed identifier (dot-boundary family match), or container metadata naming that owner. Plain names are never proof.
* **Cleaning**: `cleanSelected(escalate:)`: per item re-check identity, `trashItem`; items the Trash move refuses are handed to Finder (administrator password prompt) when `escalate` is true (manual clean); the scheduled pass uses `escalate: false` and leaves them (counted as failed). Login-item plists are unloaded (`launchctl bootout`) before trashing.
* **Scheduler**: `syncWithPreferences` on every preference change; off -> no timer, no observers. On: next fire = `CleanerSchedule.nextFireDate(after: now)` (calendar match of hour/minute[/weekday]); wake and clock/time-zone change re-compute; a missed run (the fire after `lastRun` is already past) runs shortly after wake/launch (about two minutes later); the very first enable never does a surprise catch-up. The pass scans *unattended* (no screenshot search), pre-selects only the safe groups, cleans with `escalate: false`, writes `cleanerLastAuto*`, and notifies when `cleanerScheduleNotify`: title `cleanerName`, body `cleanerAutoNotificationFormat` "%@ freed and sent to the Trash." [WIN: "%@ freed and sent to the Recycle Bin."].

### 3.5 Windows mapping (Cleaner)
Finds (all direct enumeration, no shell): 
* **Orphaned startup items** (safe, checked): entries in `HKCU/HKLM\Software\Microsoft\Windows\CurrentVersion\Run`, `RunOnce`, the Startup folders (`shell:startup`, `shell:common startup`), and "At log on"/"At startup" Scheduled Tasks whose every referenced executable no longer exists AND whose name/owner matches no installed program. Removal: registry values/tasks cannot go to the Recycle Bin -> export the value/task XML to `%APPDATA%\Trayloom\cleaner\backups\<yyyyMMdd-HHmmss-fff>\` (one `.reg` per key, exported once before any value is deleted; a key whose export fails keeps its values) first, then delete (documented deviation); shortcuts go to the Recycle Bin.
* **Caches** (safe): `%TEMP%` and `%LOCALAPPDATA%\Temp` (skip files modified in the last hour, as the current port does), thumbnail/icon cache (`%LOCALAPPDATA%\Microsoft\Windows\Explorer\thumbcache_*.db`, `iconcache_*.db` - delete after Explorer releases them or skip locked), `%LOCALAPPDATA%\D3DSCache`, `%LOCALAPPDATA%\Microsoft\Windows\INetCache`, `...\WER\ReportArchive`, browser caches (`Cache`, `Code Cache`, `GPUCache` under Chrome/Edge/Brave/Firefox profiles - never cookies, history, passwords), and known-good dev caches (npm `%LOCALAPPDATA%\npm-cache`, pip `%LOCALAPPDATA%\pip\Cache`, yarn, node-gyp, electron, cypress, puppeteer, NuGet `v3-cache`). Windows system temp (`C:\Windows\Temp`, `C:\ProgramData\Microsoft\Windows\WER`) needs elevation: list only when elevated. 

* **Administrator note (audit fix):** Trayloom does not relaunch itself as administrator. The Cleaner shows `System caches such as Windows Update downloads are cleaned by Windows itself.` with a button `Open Storage settings` (`ms-settings:storagesense`).
* **Other caches** (optional): remaining `Cache`/`Caches` folders under `%LOCALAPPDATA%\<vendor>\<app>`; apps' own data excluded by prefix (`Trayloom`, `Microsoft\Windows\*` core, `Packages\*`, `Microsoft\Credentials`, `Microsoft\Vault`, `Microsoft\Protect`).
* **Logs** (safe): `*.log`/`*.etl` older than 14 days directly under `%LOCALAPPDATA%\*\Logs`, `%PROGRAMDATA%\*\Logs`, `%WINDIR%\Logs\CBS` (elevated), `%LOCALAPPDATA%\CrashDumps`, `%LOCALAPPDATA%\Microsoft\Windows\WER\ReportQueue`.
* **Developer junk** (safe): `%LOCALAPPDATA%\Microsoft\VisualStudio\*\ComponentModelCache`, `%LOCALAPPDATA%\Temp\VSLogs`, `%LOCALAPPDATA%\NuGet\v3-cache`, `bin\obj` is NOT touched (not scoped).
* **Leftovers** (optional, unchecked): direct children of `%APPDATA%`, `%LOCALAPPDATA%`, `%PROGRAMDATA%`, `%USERPROFILE%\.config`-style dotfolders whose name is not a registered program (Uninstall keys' DisplayName/Publisher/InstallLocation leaf, Start Menu entries, `App Paths`, running processes, MSIX package names) and not Windows/Microsoft. Same "analysis only, check the path" note.
* **iPhone backups** (optional): `%APPDATA%\Apple Computer\MobileSync\Backup\*` (iTunes) and `%USERPROFILE%\Apple\MobileSync\Backup\*` (Apple Devices app); name the device from `Info.plist` (`Device Name`, `Last Backup Date`).
* **Forgotten screenshots** (optional): `%USERPROFILE%\Pictures\Screenshots` top level (Win+PrtScn names `Screenshot (N).png`, Snipping Tool `Screenshot YYYY-MM-DD HHMMSS.png`) plus Trayloom's own screenshot folder; only files still named like that and whose last-access (fallback last-write) is older than N days.
* **Recycle Bin** (optional, permanent): size via `SHQueryRecycleBinW`; empty with `SHEmptyRecycleBinW(NULL, NULL, SHERB_NOCONFIRMATION)`.
* Removal = `IFileOperation` with `FOFX_RECYCLEONDELETE`/`SHFileOperation(FOF_ALLOWUNDO)`; elevated retry through a UAC-elevated helper ("escalate").  Note: items moved to the Recycle Bin do not free disk space until it is emptied - the done card note already says so; consider an extra "Empty Recycle Bin" suggestion after a manual clean (not in Vorssaint).
* **Full Disk Access note [MAC-ONLY]** -> Windows: show only when the process is not elevated: info card (`info.circle` + text, hint, two small buttons) with Windows copy: `uninstallerFDANote` -> "Run Trayloom as administrator for a more thorough scan."; `uninstallerFDAHint` -> "Windows temp and log folders are protected. Access only applies after you reopen the app."; button `uninstallerFDAGrant` -> "Restart as administrator…" (re-launch `runas`); `uninstallerFDARelaunch` stays "Relaunch now" (relaunch normally). Card: padding 11, radius 9, fill `primary @ 5%`, width = host width.
* Scheduler: `System.Threading.Timer`/`DispatcherTimer`; recompute on `SystemEvents.PowerModeChanged`, `TimeChanged`, `UserPreferenceChanged`. Toast for the result.
* Time pickers follow `CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern` containing "t" for 12 h; weekday names `DateTimeFormat.DayNames` (Sunday first, capitalised).

### 3.6 Gap vs Trayloom (`Trayloom.Features.Tools/Cleaner/CleanerFeature.cs`)
Has: stand-alone "Cleaner" window with a flat checkbox list of fixed categories (Temp, Windows Temp, thumbnail cache, crash dumps, Chrome/Edge/Brave/Firefox caches, Recycle Bin), Scan / Clean selected, permanent delete with confirmation, "Freed X" status and toast, settings = hotkey + note. No keys besides `hotkey`.
Lacks: the state machine (idle intro with Scan, animated busy states with Cancel, results with Safe/Optional sections and collapsible groups, per-item rows with paths and Reveal, footer with counts and "Clean <size>", Done card); the leftover/startup-item/log/developer/iPhone-backup/screenshot groups; Recycle-Bin-based removal and re-validation; automatic cleanup schedule (all `cleanerSchedule*`/`cleanerLastAuto*` keys, next-run line, notify toggle and permission warning); Forgotten-screenshots setting; the WhatsApp opt-in card and the Cleaner/WhatsApp switcher; administrator note; sidebar entry/page frame.

---------------------------------------------------------------------------------------------------------------

## 4. WhatsApp downloads (tool inside the Cleaner page: `WhatsAppDownloadsSettings` + `WhatsAppDownloadManager/Scheduler/Organizer`)

Appears only when `whatsAppDownloadsEnabled` is on (segment "WhatsApp downloads" of the Cleaner page, §3.1). Grouped Form with seven sections, top to bottom.

### 4.1 Layout
**Section 1 (no header) - Introduction**
1. Switch "WhatsApp downloads" (`title`; key `whatsAppDownloadsEnabled`). Turning it off returns the page to the Cleaner segment and shuts the tool down.
2. Callout text: if the organizer is on -> `organizer.privacyNote` "To identify exact duplicates, file bytes are read locally only while calculating a cryptographic digest. Contents and chats are never stored or uploaded."; else `intro` "Finds files that macOS confirms came from WhatsApp. File contents and chats are never read." [WIN: "Finds files that Windows confirms came from WhatsApp. File contents and chats are never read." - see mapping for what "confirms" means].
3. Folder row (HStack): `folder` icon (secondary), VStack: "Watched folder" (`folder`, caption secondary) over the Downloads path with the home directory shown as `~` (monospaced caption; fallback "~/Downloads"); spacer; button "Reveal in Finder" (`cleanerRevealInFinder`) [WIN: "Show in File Explorer"].
4. Access row: *available* -> green caption `checkmark.circle.fill` + `accessReady` "Downloads is accessible"; *denied* -> orange caption `exclamationmark.triangle.fill` + `accessDenied` "Trayloom cannot access Downloads. Allow it in Files & Folders." and small button `FeatureHubStrings.openSystemSettings` "Open System Settings" (opens the Files & Folders privacy pane) **[MAC-ONLY]**; *unknown* -> nothing.

**Section 2 - Automation**
1. Switch "Clean up automatically" (`automatic`; key `whatsAppDownloadsAutomaticEnabled`, default off) + caption "Checks once a day and sends matching files older than your limit to the Trash." (`automaticCaption`) [WIN: Recycle Bin]. Enabling is *asynchronous*: it starts a scan (`waitingToEnable`); when results arrive: if `eligibleCount > 0` show the alert below, else enable immediately with "future only"; if the scan fails, nothing is enabled. Disabling is immediate and re-syncs the scheduler.
   * Alert (`firstTitle`) "What about existing files?" / message `firstMessageFormat` "%d existing files already match your rules. Choose whether automation may manage them or only future downloads." / buttons "Only future downloads" (`futureOnly`), "Include existing files" (`includeExisting`), "Cancel" (`presetConfirmCancel`, cancel role). Choosing either sets `whatsAppDownloadsIncludeExisting` (bool) and `whatsAppDownloadsAutomaticStartDate` (now), sets automatic on, requests notification permission if notify is on, syncs the scheduler, rescans.
2. Menu picker "Keep for" (`retention`) with options "1 days"... as `daysFormat` "%d days" for **1, 2, 7, 14, 30** (default 7). Changing sanitises (invalid -> 7) and, if results or a done card are showing, rescans. Caption `retentionCaption` "Recently edited files wait for the full period again."
3. Switch "Notify when done" (`cleanerScheduleNotifyToggle`; key `whatsAppDownloadsNotify`, default on; enabling requests notification permission). If on and the system denied notifications: orange caption `cleanerNotifDenied` + small button `cleanerNotifOpenSettings`.

**Section 3 - header "File types"** (`fileTypes`)
Checkbox "All" (`allTypes`, checked when all six are on; toggling sets/clears all) then three rows of two checkboxes (equal widths, spacing 24): "Images" (`image`) + "Videos" (`video`); "Audio and voice notes" (`audio`) + "Documents" (`document`); "Archives" (`archive`) + "Other" (`other`). Key `whatsAppDownloadsCategories` (comma-joined subset of `image,video,audio,document,archive,other`, canonical order; default `image,video,audio`; stored order is irrelevant; an empty string = none). A change rescans when results are showing.

**Section 4 - Organizer** (no header; card is its own section)
* Title row: "Automatic organization" (`organizer.title`, headline) + orange capsule badge "EXPERIMENTAL" (`experimental` uppercased, 9 pt bold, orange on `orange @ 14%`, h7 v3).
* Caption `organizer.description` "Moves stable WhatsApp downloads to a dedicated folder and detects exact repeat downloads."
* Switch "Organize automatically" (`enabled`; key `whatsAppOrganizerEnabled`, default off; disabled while the organizer is busy). Turning on starts a scan, requests notification permission if notify is on, and syncs the organizer.
* When on:
  * Orange caption `enabledCaption` "WhatsApp may download a moved file again. Trayloom cannot prevent the network download, but it can detect and discard an identical extra copy."
  * **Destination row**: caption "Destination folder" (`destination`); HStack: `folder.fill` + path (monospaced caption, 1 line, truncate middle; default `~/Downloads/WhatsApp`) + small "Choose…" (`chooseFolder`; folder picker with "create folder" allowed, starting at the current destination) + small "Use Downloads/WhatsApp" (`useDefault`, only when a custom path is set) + borderless `arrow.forward.circle` icon (tooltip "Reveal in Finder"). Disabled while busy. Picking Downloads itself (or an invalid folder) shows an alert with the message `invalidDestination` "Choose a folder other than Downloads itself." and an OK button. Key `whatsAppOrganizerDestinationPath` (string, default `""` = default folder).
  * Menu picker "Folder structure" (`organization`): "No subfolders" (`flat`) / "By file type" (`byType`) / "By year and month" (`byMonth`); key `whatsAppOrganizerLayout` = `flat` (default) | `category` | `month`. Layout paths: flat = destination root; category = `Images`/`Videos`/`Audio`/`Documents`/`Archives`/`Other`; month = `YYYY/MM`.
  * Menu picker "Wait before moving" (`delay`) options "1 minutes", "5 minutes", "15 minutes", "60 minutes" (`minutesFormat` "%d minutes"; values 1,5,15,60; default 5; key `whatsAppOrganizerDelayMinutes`). A file is moved only when `now - max(downloadedAt, modifiedAt) >= delay`.
  * Menu picker "When the same file is downloaded again" (`duplicateAction`): "Move the new copy to Trash" (`trashDuplicate`) / "Keep both copies" (`keepBoth`) / "Replace the organized copy" (`replaceExisting`); key `whatsAppOrganizerDuplicateAction` = `trashNew` (default) | `keepBoth` | `replaceExisting`. Caption `duplicateCaption` "Duplicates are confirmed with a private SHA-256 digest. The organized copy is rechecked before another copy is discarded."
  * Disclosure row "File types" (chevron, 0.18 s) containing the same checkbox grid (All + six) but bound to `whatsAppOrganizerCategories` (default all six: `image,video,audio,document,archive,other`); indent 25. Whole group disabled while busy.
  * Button row: prominent "Organize eligible files now" (`organizeNow`; disabled while busy) + spinner when busy; spacer; "Undo last organization" (`undo`; disabled when no valid undo transaction or busy; the undo transaction lives 7 days).
  * Status line (caption): organizing/undoing -> secondary `working` "Organizing WhatsApp files…"; done -> `resultFormat` "%1$d moved · %2$d duplicates · %3$d failed" (green if 0 failed, orange otherwise); failed -> same with 0,0,1 (orange); idle/waiting -> if ever run: `lastRunFormat` "Last organization %@: %d moved · %d duplicates · %d failed" (date medium+short, relative) else `neverRun` "No organization has run yet." (secondary). (`waiting` = "Watching Downloads" string exists but the view treats waiting like idle.)
  * Caption2 secondary: `privacyNote` (as above).

**Section 5 - header "Clean now"** (`urlCleanerManualTitle`)
Caption `manualIntro` "Scan at any time. The initial selection follows your types and age limit; you can review every confirmed file." Then prominent button `magnifyingglass` + "Scan" (`cleanerScan`; disabled while scanning/cleaning) + (while scanning) spinner + "Scanning…" (caption secondary). Then by `manager.phase`:
* *idle/scanning*: nothing.
* *results*: if no candidates -> secondary label `checkmark.circle` + `noFiles` "No confirmed WhatsApp files found in Downloads." Else: row = caption `resultsFormat` "%1$d confirmed files · %2$@" (count, total size) + small button "Select by my rules" (`selectRules`); a scroll area (max height 320, radius 8, fill `primary @ 3.5%`) of **candidate rows** (dividers between): checkbox (disabled if excluded), category icon (18 wide; accent, or secondary when excluded; photo/film/waveform/doc.text/archivebox/doc for image/video/audio/document/archive/other), VStack: file name (12 pt, 1 line, middle truncation) over "<Category> · <medium date short time>" (10 pt tertiary); size (11 pt secondary, monospaced digits); if excluded -> mini button "Manage again" (`manageAgain`); else a borderless `ellipsis.circle` menu with "Keep" (`keep`) and "Reveal in Finder". Context menu: Keep / Manage again, Reveal in Finder. Footer row: caption2 tertiary `trashNote` "Files are moved to the Trash and remain recoverable until you empty it." + prominent button `cleanSelectedFormat` "Move %1$d to Trash · %2$@" (selected count and size; disabled when 0 selected).
* *cleaning*: spinner + "Cleaning…" (`cleanerCleaning`).
* *done(moved, bytes, failed)*: icon `checkmark.circle.fill` green (or orange `exclamationmark.triangle.fill` when failed > 0) + caption `notificationFormat` "%1$d files (%2$@) moved to the Trash. %3$d failed."
* *failed*: orange caption `scanFailed` "Downloads could not be scanned. Check Files & Folders in System Settings." [WIN: "Downloads could not be scanned. Check Controlled folder access in Windows Security."]

**Section 6 - header "Activity"** (`activity`): if `whatsAppDownloadsLastCleanup > 0` -> caption `lastRunFormat` "Last cleanup %@: %d files · %@ · %d failed" (date medium+short relative, count, size, failed) else secondary `neverRun` "No cleanup has run yet."; if automatic and the scheduler has a fire date -> secondary caption `nextRunFormat` "Next automatic check %@."

**Section 7 - Privacy** (no header): caption with `hand.raised.fill`: organizer on -> `organizer.privacyNote`, else `localNote` "Only local file metadata is inspected. Trayloom never reads chats or file contents."; then caption `trashNote`.

`onAppear`: tell the manager the review is visible, sanitise `retentionDays` and `organizerDelay`, scan if idle. `onDisappear`: review not visible. When the scan reaches results/failed and automation/organizer are on, they re-sync with preferences.

### 4.2 Settings (WhatsApp downloads) - all keys with registered defaults
| Key | Type | Default | Allowed / meaning |
|---|---|---|---|
| `whatsAppDownloadsEnabled` | bool | false | tool installed/shown |
| `whatsAppDownloadsAutomaticEnabled` | bool | false | daily automatic cleanup |
| `whatsAppDownloadsCategories` | string | `image,video,audio` | comma list of categories to clean |
| `whatsAppDownloadsRetentionDays` | int | 7 | 1,2,7,14,30 (else 7) |
| `whatsAppDownloadsNotify` | bool | true | notification after automatic pass |
| `whatsAppDownloadsIncludeExisting` | bool | false | automation may also clean files that existed before it was armed |
| `whatsAppDownloadsAutomaticStartDate` | double | 0 | epoch when automation was armed (files downloaded before it are skipped unless includeExisting) |
| `whatsAppDownloadsLastAutoRun` | double | 0 | last automatic pass |
| `whatsAppDownloadsLastCleanup` / `...Count` / `...Bytes` / `...Failed` | double / int / int / int | 0 | Activity card data (manual and automatic) |
| `whatsAppDownloadsLastCleanupAutomatic` | bool | false | whether that last cleanup was automatic |
| `whatsAppDownloadsExclusions` | string[] | [] | "Keep" list of file ids (`device:inode`) |
| `whatsAppDownloadsAccessConfirmed` | bool | false | a scan of Downloads succeeded once; the scheduler needs it |
| `whatsAppOrganizerEnabled` | bool | false | |
| `whatsAppOrganizerDestinationPath` | string | "" | custom destination |
| `whatsAppOrganizerDelayMinutes` | int | 5 | 1,5,15,60 |
| `whatsAppOrganizerCategories` | string | `image,video,audio,document,archive,other` | |
| `whatsAppOrganizerLayout` | string | `flat` | `flat`,`category`,`month` |
| `whatsAppOrganizerDuplicateAction` | string | `trashNew` | `trashNew`,`keepBoth`,`replaceExisting` |
| `whatsAppOrganizerRecords` | data (JSON) | empty | digest records of organized files |
| `whatsAppOrganizerUndoTransaction` | data (JSON) | empty | last transaction for Undo (valid 7 days) |
| `whatsAppOrganizerLastRun` / `...LastMoved` / `...LastDuplicates` / `...LastFailed` | double / int / int / int | 0 | status line |

### 4.3 Behaviour (WhatsApp downloads)
* **Candidates** (`scan`): only the *top level* of Downloads (plus, for review only, the organizer destination if it is outside Downloads - such organized files are listed for manual review but never trashed automatically); skips hidden files, directories, packages, symlinks, aliases, incomplete extensions (`download, part, partial, crdownload, tmp`); a candidate must carry a quarantine record whose agent name equals "WhatsApp" (macOS `LSQuarantineAgentName`); downloadedAt = quarantine timestamp ?? date added ?? creation date; modifiedAt = content modification date; id = `device:inode`.
* **Category**: by extension first - archive (`7z bz bz2 cab dmg gz iso rar tar tgz xz zip`), document (`csv doc docm docx epub key md numbers odp ods odt pages pdf ppt pptm pptx rtf tex txt xls xlsm xlsx`); else by content type: image / movie|video / audio / archive / pdf|text -> document; else other.
* **Eligible for rules** = category enabled AND both downloadedAt and modifiedAt older than the retention window (editing a file in place postpones cleanup). **Eligible for automatic cleanup** additionally requires: not an organized file, and (`includeExisting` or downloadedAt >= automatic start date). Initial checkbox selection = eligible-for-rules and not excluded.
* **Clean**: re-validates each candidate (same id), skips non-eligible ones in automatic mode, moves to the Trash (reversible), records Activity data, shows done card.
* **Scheduler**: daily at 09:00 local; next fire = next 09:00 after now; if the 09:00 of today already passed and the last automatic run was before it -> catch-up in 120 s (also after wake/clock change). Needs: Cleaner feature installed, tool enabled, automatic on, access confirmed. A pass never runs while the user is reviewing results (retry in 10 min), while the organizer is busy, or while scanning/cleaning. After a pass: `whatsAppDownloadsLastAutoRun` set; notification (if notify) title `notificationTitle` "WhatsApp cleanup", body `notificationFormat`.
* **Organizer**: watches the top level of Downloads (only quarantined-by-WhatsApp, enabled categories, stable for `delay` minutes), moves into the destination/layout, hashes with SHA-256 (private digest records) to detect exact repeats, applies the duplicate action, supports Undo of the last run (moves back / restores trashed duplicates, 7-day lifetime), same-volume move = rename, cross-volume = verified copy. Notification title `organizer.notificationTitle` "WhatsApp organization", body `organizer.notificationFormat` "%1$d files organized. %2$d duplicate downloads handled. %3$d failed."

### 4.4 Windows mapping (WhatsApp downloads)
* "Confirmed from WhatsApp": Windows has no quarantine-agent attribute. Use the NTFS **Zone.Identifier** alternate data stream (`<file>:Zone.Identifier`): `ReferrerUrl`/`HostUrl` whose host ends with `whatsapp.com` or `whatsapp.net` (WhatsApp Web in any browser, and the WhatsApp desktop app which stamps `ZoneId=3` + `HostUrl`). As a *secondary* signal accept the app's fixed file-name patterns (`WhatsApp Image|Video|Audio|Ptt|Document|Sticker <yyyy-mm-dd> at ...`) *only when* the stream is absent, and label it identically. Never read file contents. downloadedAt = file creation time (`CreationTimeUtc`); modifiedAt = `LastWriteTimeUtc`; id = `volumeSerial:fileIndex` (`GetFileInformationByHandle`). Incomplete extensions on Windows: also `.crdownload, .part, .tmp, .download, .opdownload`. Hidden = `FILE_ATTRIBUTE_HIDDEN|SYSTEM`; symlink/junction = reparse point.
* Downloads folder: `SHGetKnownFolderPath(FOLDERID_Downloads)`; Trash = Recycle Bin (`IFileOperation` + `FOFX_RECYCLEONDELETE`); "Reveal" = `explorer.exe /select,"<path>"`.
* Access: normally always available; the "denied" state = `UnauthorizedAccessException`, usually Defender **Controlled folder access** -> text [WIN] `accessDenied` "Trayloom cannot access Downloads. Allow it in Windows Security > Controlled folder access." and button "Open Windows Security" (`windowsdefender://ransomwareprotection`). The `openSystemSettings` label "Open System Settings" becomes [WIN] "Open Windows Security".
* Folder watching: `FileSystemWatcher` + a 1-minute poll; hashing with `SHA256.HashDataAsync`; same-volume `File.Move`, cross-volume copy+verify+delete; organizer records and undo in `settings.json`.
* Scheduler: timer at 09:00 + `SystemEvents.PowerModeChanged/TimeChanged` re-arm; toast notifications.
* WhatsApp for Windows (Store/native) normally saves into `%USERPROFILE%\Downloads`; the packaged app's own `Packages\5319275A.WhatsAppDesktop_*\LocalState\shared\transfers` is a private store and is never touched (matches "never reaches into WhatsApp's container").

### 4.5 Gap vs Trayloom
Entirely absent (no WhatsApp tool in `src`); the `Cleaner` code only mentions browser caches. All of §4 must be built, including the shared `whatsApp*` settings, scheduler (09:00), organizer, undo, exclusions and Activity card.

---------------------------------------------------------------------------------------------------------------

## 5. Uninstaller page (`UninstallerView` + `AppPickerView` + `AppUninstaller`)

A single-column wizard with five phases (`AppUninstaller.Phase`): `empty` -> `scanning` -> `results` -> `removing` -> `done(freed, failed)`. The page fills the pane (`maxWidth/Height .infinity`). On appear/disappear the previous refusal note is cleared.

### 5.1 Layout per phase
**Empty / drop** (padding 28, VStack spacing 20, centred, Spacer above and below)
1. Drop zone: rounded rect 360 x 200, radius 18, dashed stroke 2 px (dash 9/7); idle stroke `secondary @ 35%`, no fill; drag-hover stroke accent + fill `accent @ 6%` (0.15 s ease-out). Inside (VStack spacing 12): `trash.square` 44 pt light (secondary; accent on hover), "Drag an app here" (`uninstallerDropTitle`, 16 pt semibold), "or choose one to scan" (`uninstallerDropSubtitle`, 12 pt secondary).
2. If the last selection was refused: `UninstallRefusalNote` (width 360): orange `exclamationmark.triangle.fill` + secondary caption with the message (below). Cleared by an accepted selection or a reset.
3. Large button "Choose app…" (`uninstallerChoose`) -> opens the app picker as a sheet.
4. Caption tertiary: "Nothing is removed without your confirmation." (`uninstallerEmptyNote`).
5. Command Bar toggle (an inset grouped Form, fixed 360 wide): switch "Show in Command Bar" (`uninstallerCommandBarToggle`; key `uninstallerCommandBarEnabled`, default **false**) + caption "Choose and uninstall apps in the Command Bar." (`uninstallerCommandBarCaption`).
6. `FullDiskAccessNote` (360 wide) while Full Disk Access is missing **[MAC-ONLY]**: texts `uninstallerFDANote` "Grant Full Disk Access for a more thorough scan.", `uninstallerFDAHint` "Turn Trayloom on in the list. If it isn’t there, click + and pick Trayloom from Applications. Access only applies after you reopen the app.", buttons "Grant access…" (`uninstallerFDAGrant`) and "Relaunch now" (`uninstallerFDARelaunch`). Windows equivalent: see §3.5 (administrator note).
The whole page is a drop target (drop of a file URL; first `.app` among the dropped items, else the first item). Drop acceptance = `select(appURL:)` result.

**App picker sheet** (`AppPickerView`, 520 x 560, padding 18, VStack spacing 12): header row = "Choose app" (`uninstallerPickerTitle`, 16 pt semibold) + spacer + (only when browsing is allowed - not in this sheet) folder button "Choose app…" + **Cancel** (`uninstallerCancel`). Search field (rounded border, 13 pt) placeholder "Search apps" (`uninstallerPickerSearch`); matches name, bundle id or identity, case-insensitive substring. List states: loading (centred spinner + "Scanning files…" `uninstallerScanning`, 360 pt tall); empty (centred `app.dashed` 34 pt light + "No apps found" (`uninstallerPickerEmpty`) + tertiary 11 pt note "Apps that are part of macOS or made by Apple aren’t listed." (`uninstallerPickerHiddenNote`) [WIN: "Windows components and system updates aren’t listed."]); rows (LazyVStack spacing 2, row padding h8 v7, hit-testable whole row): 28 pt icon, VStack: name (13 pt medium, 1 line) over location/id line (11 pt secondary, 1 line; location = the app's folder when known, else bundle id, else path). Clicking a row calls `onSelect(url)`, closes the sheet and starts the scan. Apps the selection check would refuse are not listed.

**Scanning / Removing** (busy): centred VStack spacing 14: large spinner, message "Scanning files…" (`uninstallerScanning`) or "Moving to the Trash…" (`uninstallerRemoving`) [WIN: "Moving to the Recycle Bin…"], and the target (18 pt icon + name, callout). While scanning, a large **Cancel** (resets).

**Results** (VStack spacing 0)
1. Target header (padding 16): 44 pt app icon; VStack: name (16 pt semibold) over bundle id or path (caption secondary, truncate middle); spacer; VStack trailing: total size (15 pt semibold rounded) over "found" (`uninstallerFoundTitle`, caption2 secondary); plain `xmark.circle.fill` 16 pt secondary reset button (disabled while a package-manager removal runs). Divider.
2. Inset `List` with one section per category present, in this fixed order and with these headers (empty categories omitted): "Application" (`uninstallerCatApp`), "Support" (`uninstallerCatSupport`), "Caches" (`uninstallerCatCaches`), "Preferences" (`uninstallerCatPreferences`), "Containers" (`uninstallerCatContainers`), "Logs" (`uninstallerCatLogs`), "Saved state" (`uninstallerCatState`), "Other" (`uninstallerCatOther`).
   Row (HStack spacing 10): checkbox; 22 pt file icon; VStack: name (12.5 pt, 1 line, truncate middle) over (10.5 pt) [if match is `related`: orange label `questionmark.circle` + "Optional, review first" (`cleanerOptionalSection`)] + parent folder with `~` for the home (tertiary, truncate head); spacer; borderless `folder` button (tooltip/label "Reveal in Finder"); size (11.5 pt secondary, monospaced digits). Start state: exact-match items checked, `related` items unchecked.
3. Divider; Homebrew status block (if applicable, see below); **footer** (padding 16): VStack: "%d of %d selected" (`uninstallerSelectedFormat`, 12 pt medium) over selected size (caption secondary); spacer; **Cancel** (`uninstallerCancel`; disabled while a package removal runs); **"Move to Trash"** (`uninstallerRemove`; label becomes "Uninstall" (`homebrewUninstall`) when the selected Application item is managed by the package manager; bordered prominent, **red tint**; disabled when nothing is selected, a removal is running, the Homebrew check failed, or (package-managed and the package manager is busy)).
   * Package-managed (Homebrew) block: if the ownership check failed -> `UninstallerHomebrewFailureNote`: orange caption label `exclamationmark.triangle.fill` + "Could not verify whether Homebrew manages this app. Refresh to try again, or uninstall it from Homebrew." (`uninstallerHomebrewCheckFailed`) + small bordered "Refresh" (`homebrewRefresh`); else if a package manages the selected app: caption secondary `shippingbox` + "%@ will also be removed from Homebrew." (`uninstallerHomebrewPackageFormat`, package display name) - replaced by the operation status card (§2.2) + trust card + error label once an uninstall operation for that package exists. Clicking the red button in this state first shows an alert (`homebrewConfirmUninstallTitle` "Uninstall with Homebrew?" / `homebrewConfirmUninstallBodyFormat` "Homebrew will uninstall %@. Configuration files may remain on the system." / buttons "Cancel", destructive "Uninstall"); the confirmation is bound to the selection shown when it opened (if the selection changed: `uninstallerConfirmationExpired` "This confirmation is no longer valid. Review the current items and confirm again.").

**Done** (padding 28, spacing 16, centred): 54 pt icon (`checkmark.circle.fill` green, or `exclamationmark.triangle.fill` orange when leftovers remain); "Done!" (20 pt bold); "%@ removed" (`uninstallerFreedFormat`, 13 pt secondary); if some items failed: `UninstallFailureNote` (width 360): orange caption "Some items couldn’t be moved to the Trash." (`uninstallerSomeFailed`), up to 4 failed names (caption2 secondary, middle truncation), then "and %d more" (`uninstallerFailedMoreFormat`, caption2 tertiary) if more, and - when Full Disk Access is missing and a failed path is sandbox container data - an FDA card with reason "Sandboxed app data can only be moved with Full Disk Access. The administrator password does not stand in for it." (`uninstallerFailedNeedsFDA`); then large bordered prominent **"Uninstall another"** (`uninstallerAnother`) -> reset.

Refusal messages (`UninstallerSupport.RefusedApp.message`, shown in the empty state):
* not an app: `uninstallerRefusedNotApp` "Only apps can be uninstalled."
* system app: `uninstallerRefusedSystemFormat` "%@ can’t be uninstalled because it’s part of macOS." [WIN: "...part of Windows."]
* shared identifier: `uninstallerRefusedSharedFormat` "%@ can’t be uninstalled here because it shares files with macOS or other apps." [WIN: "...shares files with Windows or other apps."]
* inside another app: `uninstallerRefusedNestedFormat` "%@ can’t be uninstalled on its own because it’s part of another app."
* Trayloom itself: `uninstallerRefusedSelfFormat` "Trayloom can’t uninstall itself here. Use Settings › %@ instead." with %@ = "Advanced" (`tabAdvanced`) - the self-uninstall lives in Advanced (02a).
* link: `uninstallerRefusedLinkFormat` "%@ can’t be uninstalled because it’s a link to an app stored elsewhere." [WIN: shortcut/junction to an app elsewhere: "...because it’s a shortcut to an app stored elsewhere."]
* unidentified: `uninstallerRefusedUnidentifiedFormat` "%@ can’t be uninstalled because it can’t be identified as an app."

### 5.2 Settings (Uninstaller)
| Key | Type | Default | Effect |
|---|---|---|---|
| `uninstallerCommandBarEnabled` | bool | **false** | Adds "Uninstall Application" (`uninstallerCommandBarBrowseTitle`) and "Uninstall app selected in Finder" (`uninstallerCommandBarFinderTitle`, [WIN: "...selected in File Explorer"]) entries to the Command Bar; unavailable selection text `uninstallerSelectionUnavailable` "Select an app that can be removed in Finder, or choose another from the list." |
No other persisted state (the menu panel has its own `PanelUninstallerView`, see 01).

### 5.3 Behaviour (Uninstaller)
* `select(appURL:)` -> `UninstallerSupport.evaluate`: refuses links, system apps, shared identifiers, nested apps, itself, unidentifiable bundles; on success reads identity (bundle id, group ids, display names, team prefix) and starts the scan.
* **Scan** (cancellable): the app bundle itself ("Application", exact, checked) + exact/related hits in `~/Library` roots by bundle id / group id / name tokens (Application Support, Caches, Preferences + `.plist`, Containers, Group Containers, Logs, Saved Application State, LaunchAgents, WebKit, HTTPStorages, Cookies, preference panes, plugins, receipts...). A Spotlight identity query adds hits (restricted to allowed roots); nested/duplicate paths collapse (exact beats related at the same path); shared preferences are kept when another copy of the app remains installed. Every item is bound to a file identity and revalidated before removal; paths must stay inside their root (`removalPathIsSafe`); symlinks are never followed.
* **Remove**: moves checked items to the Trash in one batch through Finder (administrator prompt appears for root-owned items; a cancelled prompt leaves the items); items still present afterwards are reported as failed. If the Application item is selected and the app is Homebrew-managed (cask), the app and its receipt are removed by `brew uninstall --cask` after the confirmation, then only the other selected items are trashed (the app is excluded to avoid removing it twice).
* After success `freed` = sum of moved sizes.

### 5.4 Windows mapping (Uninstaller)
Windows has no "drag the .app to the trash" model, so the vendor uninstaller must run first. Keep the exact page/phases but change what each phase does:
* **Selection**: "Choose app…" opens the same sheet listing installed programs from `HKLM/HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall` (+ `WOW6432Node`) and MSIX/UWP packages (`PackageManager.FindPackagesForUser("")`, framework/resource/`NonRemovable` packages excluded). Skip entries with `SystemComponent=1`, `ParentKeyName`/`ParentDisplayName` (updates), `ReleaseType` Update/Hotfix/Security Update/Service Pack, and Windows' own components (name-less entries). Drop target accepts a dropped `.exe`, a `.lnk` (resolve target and match by `InstallLocation`/`DisplayIcon`/exe path), or an install folder; no match -> refusal "…can’t be identified as an app." Row subtitle = publisher + version (instead of location/bundle id). Existing Trayloom `UninstallerFeature` already has the registry scan and size/publisher/version columns - reuse.
* **Application item** = the program itself. When it is checked and the red button is pressed, the program's own uninstaller runs and the wizard waits for it: `QuietUninstallString` when available and chosen, else `UninstallString`; MSI entries run `msiexec /x {ProductCode}`; MSIX entries use `PackageManager.RemovePackageAsync`; programs also known to winget (matched by `winget list` id) are removed with `winget uninstall --id <Id>` behind the same confirmation dialog and the operation status card of §2.2. The vendor uninstaller may show its own UI/UAC prompt. Leftover rows are moved to the Recycle Bin afterwards. Footer button title: "Uninstall" (`homebrewUninstall`) when the Application item is checked, otherwise "Move to Trash" [WIN: "Move to Recycle Bin"]. Busy text while the vendor uninstaller runs: "Running the uninstaller for <name>…" instead of "Moving to the Trash…".
* **Leftover search roots** (instead of `~/Library`): `%APPDATA%` (Support/Preferences), `%LOCALAPPDATA%` (Caches/Support), `%LOCALAPPDATA%\Packages\<PFN>` (Containers), `%PROGRAMDATA%`, `%USERPROFILE%` dotfolders/`AppData\LocalLow`, Start Menu shortcuts (`Start Menu\Programs`), `%LOCALAPPDATA%\Temp\<name>`, registry keys `HKCU\Software\<Publisher>\<App>` + `HKLM\SOFTWARE\<Publisher>\<App>` (+WOW6432Node) (category Preferences), services (`Services\<name>` whose ImagePath is in the install dir), Scheduled Tasks, Run/RunOnce values, firewall rules (category Other), Logs (`...\Logs`, `%PROGRAMDATA%\<App>\Logs`), saved state = Jump Lists / Recent (`%APPDATA%\Microsoft\Windows\Recent\AutomaticDestinations` entries by AppID) and `Prefetch` entries (admin). Category headers keep the same English words.
* Match strength: *exact* = folder/key name equals publisher+app, product code, `InstallLocation` leaf, or UWP package family; *related* = shares a ≥6-char name token. Registry keys cannot go to the Recycle Bin: export each deleted key to a `.reg` backup under `%LOCALAPPDATA%\Trayloom\UninstallBackups\<app>-<yyyymmdd-hhmmss>\` first, delete after a successful export; mention the backup path in the done card tooltip.
* Safety: refuse removal paths that are a Windows root (`C:\`, `C:\Windows`, `C:\Program Files` itself, `%USERPROFILE%`, `%APPDATA%` itself...), always keep to the matched subfolder; symlink/junction-aware; protect other apps' sibling data (publisher folders shared with other installed apps are only offered when empty after the app's own subfolder is removed).
* Elevation: HKLM/Program Files leftovers need admin -> perform via an elevated helper ("escalate") after the standard UAC prompt, same as the macOS Finder prompt; refusal of the prompt leaves items and counts them failed.
* **Full Disk Access / Finder automation [MAC-ONLY]** -> administrator note (3.5). The `uninstallerFailedNeedsFDA` card becomes "Some protected items need administrator rights." (only for `Program Files`/`ProgramData`/HKLM paths when not elevated) with "Restart as administrator…".
* Trayloom cannot uninstall itself here -> self-uninstall page in Advanced.

### 5.5 Gap vs Trayloom (`UninstallerFeature.cs`)
Has: stand-alone window with a searchable DataGrid of registry programs (icon, Name, Publisher, Version, Size), Uninstall / Quiet uninstall / Find leftovers / Open install folder / Refresh, post-uninstall banner "<name> was removed. Check for leftover files?" -> a "Leftover folders" window (candidate folders by name match, nothing pre-selected, "Move selected to Recycle Bin"). No settings except hotkey; no Command Bar toggle.
Lacks: the wizard page (drop zone, refusal notes, picker sheet, scanning/results/removing/done phases), category sections (Application / Support / Caches / Preferences / Containers / Logs / Saved state / Other) with exact vs related pre-checking and per-item reveal, registry/service/task leftovers with backup, MSIX/winget support, package-manager confirmation + operation status card, administrator note, "Uninstall another", key `uninstallerCommandBarEnabled`, Command Bar entries.

---------------------------------------------------------------------------------------------------------------

## 6. Kill Process page (beta) (`KillProcessView` + `KillProcessService`)

### 6.1 Layout (VStack spacing 0, fills pane)
1. **Header** (padding 16, VStack spacing 12):
   * Inset grouped Form (fixed to content height), two sections: (a) switch "Group related processes" (`groupToggle`; key `killProcessGroupRelated`) + caption "Groups helper processes under the app responsible for them." (`groupCaption`); (b) switch "Show in Command Bar" (`commandBarToggle`; key `killProcessCommandBarEnabled`) + caption "Adds running processes to the Command Bar, so you can find and kill them without opening Settings." (`commandBarCaption`).
   * Right-aligned row (spacing 16): caption secondary `processCountFormat` "Processes: %d" (count of the *filtered* rows) and a plain `arrow.clockwise` icon button (secondary; tooltip "Refresh" `refreshTooltip`) -> forced refresh.
   * Search row (spacing 10): `magnifyingglass` (secondary) + plain text field, placeholder "Filter by name" (`searchPlaceholder`), bound to the live `query`; trailing small spinner while refreshing. Filter = case-insensitive substring of the process name OR exact PID match (query trimmed).
2. Divider; **Column header** (padding h16 v6; HStack spacing 10, mirrors the rows): 22 pt spacer, "Process" (`columnProcess`, flexible, left), "CPU" (`columnCPU`, 52 wide, right), "Memory" (`columnMemory`, 72, right), "PID" (`columnPID`, 56, right), 98 pt spacer for the buttons. Each label is a plain button 10.5 pt semibold secondary; the active column shows a `chevron.up` (ascending) or `chevron.down` (descending) 8 pt bold. Divider.
3. **List** (inset `List`):
   * Not loaded yet (no successful snapshot ever): centred small spinner.
   * Loaded but filtered list empty: centred secondary text "No Processes Found" (`emptyStateTitle`).
   * Else rows (HStack spacing 10, vertical padding 2, buttons bordered + small): 22 pt process/app icon; VStack: name (12.5 pt, 1 line, middle truncation) + (when grouped count > 1) tertiary 10 pt "Processes: N", over the executable path (10.5 pt tertiary, 1 line, truncate head); spacer; CPU `String(format: "%.1f%%")` (11 pt monospaced secondary, 52 wide, right); memory `MetricFormat.bytes` (11 pt monospaced, 72 wide); PID (11 pt monospaced, 56 wide); button **"Kill"** (`killButton`, min width 44, disabled if protected); `ellipsis` borderless menu (min width 44, no indicator) with, in order: "Force Kill" (disabled if protected), "Kill All “<name>”" (`killAllFormat`; disabled if protected), "Kill Process Tree" (disabled if protected), "Restart" (`restartButton`, only when restartable), divider, "Copy PID", "Copy Path".
4. **Confirmation alert** (every kill action; destructive confirm): titles `confirmKillFormat` "Kill %@?", `confirmForceKillFormat` "Force Kill %@?", `confirmKillAllFormat` "Kill all “%@” processes?", `confirmKillTreeFormat` "Kill %@ and all its child processes?"; message = the process's path (empty for Kill all); buttons: "Cancel" (`uninstallerCancel`) and the destructive confirm "Kill" / "Force Kill" / "Kill" (kill-all) / "Kill Process Tree".
5. Failure copy (`killFailedTitle` "Couldn’t Kill Process", `killFailedMessage` "The process may have already exited or require additional privileges."), admin prompt `adminPromptFormat` "Trayloom needs administrator access to end “%@”." Other strings: `pageTitle` "Kill Process", `browseSubtitle` "Browse & Kill", `pidLabelFormat` "PID %d", `copyPID` "Copy PID", `copyPath` "Copy Path".

### 6.2 Settings (Kill Process)
| Key | Type | Default | Allowed | Effect |
|---|---|---|---|---|
| `killProcessGroupRelated` | bool | true | - | Fold helper processes under the responsible app (sums CPU/memory, shows "Processes: N") |
| `killProcessSortBy` | string | `cpu` | `cpu`,`memory`,`name`,`pid` | Active sort column |
| `killProcessSortAscending` | bool | false | - | Sort direction |
| `killProcessCommandBarEnabled` | bool | **true** | - | Command Bar process rows (the view's `@AppStorage` fallback is also true) |

### 6.3 Behaviour
* Sorting: clicking the active header flips direction; clicking another column switches to it at its natural direction (descending = highest first for CPU, Memory, PID; ascending A-Z for Name). Ties break by PID ascending. Name compare is localized case-insensitive. Both keys persist on every click. Default is CPU descending.
* Refresh: on appear (forced) and every **3 s** while the page is open **and its window is active** (timer stops when the window becomes inactive; restarts and refreshes when active); a service-level 3 s freshness cache makes calls inside the window no-ops unless `force`. A failed/zero-row snapshot keeps the last good list (so a hiccup never empties the page). Toggling "Group related processes" forces a refresh.
* Protected processes (Kill/Force/All/Tree disabled, also in the menu): PID <= 1, Trayloom's own PID, names `kernel_task`, `launchd`, `windowserver`, `loginwindow`, `vorssaint` variants, and paths ending `/windowserver`, `/loginwindow`, `/launchd`.
* **Kill** = polite terminate (macOS: `NSRunningApplication.terminate()` for regular GUI apps, else SIGTERM); **Force Kill** = `forceTerminate()`/SIGKILL; **Kill All** = every listed *non-protected* entry with the exact same name; **Kill Process Tree** = descendants (BFS over a single `ps` parent table, cap 4096, depth-first reverse = deepest first, protected ones skipped) then the root. Every target is identified by PID + kernel start time (a reused PID is never killed - `stale`). EPERM results are batched into **one** administrator prompt (password once) that re-checks each target's start time before signalling.
* After a kill: killed rows disappear immediately, then a forced refresh runs 0.5 s later.
* **Restart** (only for non-protected GUI apps with a bundle URL and start time): terminate, wait for the real termination notification (not a fixed delay), relaunch the bundle.
* Copy PID/Path: write plain text to the clipboard (marked as Trayloom-sourced so clipboard history can ignore it).

### 6.4 Windows mapping (Kill Process)
* Snapshot: `CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS)` (pid, ppid, exe name) + per process: `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)`; CPU% = delta of `GetProcessTimes` (kernel+user) / (elapsed x logical CPUs) between ticks (first tick 0.0); memory = `PROCESS_MEMORY_COUNTERS_EX.PrivateUsage` (private bytes) or working set; path = `QueryFullProcessImageNameW`; creation time = `GetProcessTimes` CreationTime (the identity "startedAt"); name = exe base name without `.exe` (fall back to `FileDescription` for display if desired); icon = `SHGetFileInfo`/`ExtractIconEx` of the path (cache by path). Sample for ~1 s only on the 3 s tick.
* Grouping ("responsible app"): group a process under its nearest ancestor chain root that has a top-level visible window or is a service host the user would recognise (stop before `explorer.exe`, `services.exe`, `svchost.exe`, `wininit/winlogon`); processes with identical image path also fold (e.g. 30 `chrome.exe`). Group row: CPU/memory summed, "Processes: N".
* Kill: first try `PostMessage(WM_CLOSE)` to each top-level window of the PID and wait 1.5 s, then (if still alive and has no windows) `TerminateProcess` - practical rule: **Kill** = WM_CLOSE for windowed processes, `TerminateProcess` for windowless ones; **Force Kill** = `TerminateProcess(handle, 1)` always; **Tree** = enumerate descendants from the snapshot table (ppid), reverse order, same per-process rule (job objects/`taskkill /T` optional); **Kill All** by exact name. Identity check = PID + `CreationTime` before every call. Access denied (elevated/other-user processes) -> collect, then ONE UAC-elevated helper performs them (`runas` of Trayloom with a hidden `--kill-batch` verb, passing pid+creation time); prompt text `adminPromptFormat` where UAC shows the process name.
* Protected on Windows: PID 0 and 4, `System`, `Registry`, `Memory Compression`, `Secure System`, `smss`, `csrss`, `wininit`, `winlogon`, `services`, `lsass`, `lsaiso`, `fontdrvhost`, `dwm`, Trayloom's own PID/name (list already in `KillProcessFeature.cs`).
* Restart: only for processes with a resolvable exe path and a top-level window (GUI): close politely, wait on `WaitForExit`, relaunch with `ProcessStartInfo(path)` (no original arguments are available without WMI; may read `Win32_Process.CommandLine` via WMI when allowed).
* "Reveal"/Copy Path stays. Add Windows-only context entry "Open file location" is a Trayloom extra (keep).
* Timer: `DispatcherTimer` 3 s, paused when the host window deactivates (`Window.Deactivated`).

### 6.5 Gap vs Trayloom (`KillProcessFeature.cs`)
Has: separate window with DataGrid (Name, PID, CPU %, Memory, Path), search box + status, buttons "End process", "End process tree", "Restart", "Open file location", context menu, protected list, process refresh ~ timer, UAC-less access-denied message, confirmation `MessageBox`. Only setting: hotkey. Extra action "Force quit the active app".
Lacks: grouped Form header (Group related processes, Show in Command Bar toggles + captions), "Processes: N" counter + refresh button, column-header sorting with persisted `killProcessSortBy/Ascending`, `killProcessGroupRelated`/`killProcessCommandBarEnabled` keys, Kill / Force Kill / Kill All / Kill Process Tree / Restart / Copy PID / Copy Path menu in a per-row button cluster, identity (PID+start time) guard, batched elevation, localized confirmation titles ("Kill %@?" etc.), empty ("No Processes Found") vs loading states, 3 s active-window timer, process icons.

---------------------------------------------------------------------------------------------------------------

## 7. Port Manager page (`PortManagerView` + `PortManagerService`)

### 7.1 Layout (VStack spacing 0, fills pane, top-leading)
1. **Title bar** (padding h14, top 10, bottom 8; HStack spacing 9): `network` 15 pt semibold accent + "Port Manager" (`title`, headline) + spacer + plain `arrow.clockwise` 11 pt semibold button (tooltip "Refresh", `refresh`).
2. **Filter field** (padding 9, radius 9, fill `primary @ 6%`, outer padding h14 bottom 8): `magnifyingglass` (secondary) + plain text field placeholder "Filter by port, process, or PID" (`filter`) bound to `query` + small spinner while refreshing. Filter matches (lower-case substring) in "<port> <process> <pid> <address>".
3. Divider.
4. If the last refresh failed: callout secondary label `exclamationmark.triangle` + "Could not read listening ports. Try refreshing." (`loadFailed`), padding 14.
5. Body: empty+failed -> blank spacer; empty+loaded -> centred empty state: `network.slash` 25 pt (tertiary), "No listening ports found" (`empty`, callout secondary), "Try refreshing or changing your search." (`emptyHint`, caption tertiary); empty+not yet loaded -> centred small spinner; else inset `List` (row separators hidden) of rows.
6. **Row** (HStack spacing 8, vertical padding 3): `network` 11 pt semibold accent (18 wide); VStack spacing 1: line 1 = port (12 pt semibold monospaced) + protocol capsule ("TCP", 9 pt bold rounded secondary, padding h4 v1.5, fill `primary @ 8%`) + (if listening on all interfaces) **All interfaces** badge (globe + label "All interfaces", 9 pt bold rounded, orange tint `PanelMetricColor.orange` on `orange @ 14%` capsule; tooltip/hint `allInterfacesHelp` "Listening on every network interface, so other devices on the network may be able to connect."); line 2 = process name (11 pt, 1 line); line 3 = "PID <pid>  •  <address>" (9 pt monospaced tertiary, truncate middle; address e.g. `*:3000`, `127.0.0.1:5432`, `[::1]:8080`). Trailing (only when Kill Process is installed - `AppFeature.killProcess.isAvailable`): small bordered "Kill" (`kill`) and small bordered `bolt.fill` icon button (label "Force Kill", `forceKill`); both disabled when the entry has no stable identity (`startedAt == nil`) or the process is protected.
   Context menu: "Copy port" (`copyPort`), "Copy PID" (`copyPID`), "Copy address:port" (`copyAddress`) and, for TCP listeners with a usable host, a divider + "Open in browser" (`CommandBarStrings.openInBrowser`) -> `http://<host>:<port>` where host mapping is `*` -> `localhost`, `0.0.0.0` -> `127.0.0.1`, `::`/`[::]` -> `[::1]`, else the bound address (beep if the open fails).
7. **Confirmation alert**: title `terminateFormat` "Terminate %@?" (process name), message `terminateMessageFormat` "This closes port %d by terminating PID %d.", buttons "Cancel" and destructive "Kill" (or "Force Kill" when force). Confirm -> `KillProcessService.kill(pid:name:startedAt:force:)`, then a refresh.
Other strings (panel): `openFormat` "%d open", `listeningCaption` "Your listening ports".

### 7.2 Settings
No persisted keys. (Feature availability only; the Kill buttons need the Kill Process feature.)

### 7.3 Behaviour
* `onAppear` -> `refresh()` (single in-flight; sets `isRefreshing`). Snapshot = `lsof -nP +c0 -iTCP -sTCP:LISTEN -F pcnPT` (TCP LISTEN only, IPv4+IPv6), de-duplicated (protocol-port-pid-address), **sorted by port ascending, then process name**. A timeout/non-zero infrastructure failure -> `refreshFailed` (list kept); exit code 1 with no output = empty list, not a failure.
* Each entry carries `startedAt` (process start time) only if the same process identity was observed both when listing and when re-reading (otherwise the row has no kill actions until the next refresh).
* No auto-refresh timer (manual refresh button, reopening the page, and after a kill).

### 7.4 Windows mapping
* `GetExtendedTcpTable(TCP_TABLE_OWNER_PID_LISTENER, AF_INET)` and `AF_INET6` (`TCP6ROW_OWNER_PID` with scope id); address text `0.0.0.0:3000` / `[::]:3000` / `127.0.0.1:5432` / `[::1]:8080`; "All interfaces" when the address is `0.0.0.0` or `::`. PID -> name via `QueryFullProcessImageNameW` (fall back to Toolhelp name; PID 4 = "System"). Identity: process `CreationTime`. Protected: shared with Kill Process (PID 0/4, csrss, wininit, services, lsass, Trayloom itself...). Same sort (port, then name). "Open in browser" -> `Process.Start("http://127.0.0.1:port")` with the same host mapping. Optional (existing Trayloom behaviour, keep as an extra, off by default): UDP table (`GetExtendedUdpTable`) and all TCP states via a "Show all TCP connections" toggle (key `showAll` in scope `port-manager`).
* Kill/Force Kill go through the Kill Process service (§6.4) incl. the single UAC prompt; the listener table is re-read 0.5 s later.
* Icons: `network` = E968 (NetworkTower) ? or E774; `network.slash` = E871 (Disconnected)? ; globe E774; bolt E945 (LightningBolt).

### 7.5 Gap vs Trayloom (`PortManagerFeature.cs`)
Has: separate window with DataGrid (Protocol, Port, Local address, State, Remote, PID, Process), search, "All TCP connections" checkbox (`showAll`), buttons "End owner process", "End owner tree", "Open in browser", "Copy port", "Refresh"; UDP + TCP/TCP6 via the IP helper tables; settings = hotkey + `showAll`.
Lacks: Vorssaint's row-card layout (port/protocol/All-interfaces badge/process/PID-address), title bar with refresh, empty/loading/failed states, filter field style, the conditional Kill/Force Kill pair (only when Kill Process is installed; Force = bolt), confirmation copy ("Terminate %@?"/"This closes port %d by terminating PID %d."), context menu (Copy port / Copy PID / Copy address:port / Open in Browser), identity-checked kill, port-ascending sort + dedupe, localisation of strings. Extras to keep behind a setting: UDP and non-listening states.

---------------------------------------------------------------------------------------------------------------

## 8. Media page (`MediaSettings` -> `MediaWorkspaceView(compact: false)` + `MediaService`)

Media tools = **four tools** in one workspace: Video (compress), GIF (make from video), Image (compress / convert / resize / watermark / rename / PDF, single or batch) and Text (OCR). "Convert", "resize", "watermark" and "PDF" are options *inside the Image tool* (format picker, resize picker, watermark section, "Docs" preset / PDF format); there are no separate pages for them. Everything is local ("Local. No network.").

### 8.1 Layout
Page = `MediaWorkspaceView` with `padding(16)`; root VStack(alignment leading, spacing 14), non-scrolling header + tool picker, then a ScrollView holding the content (`maxHeight .infinity`).

1. **Header** (HStack spacing 9): `Label("Media", systemImage: photo.on.rectangle.angled)` 16 pt semibold (`mediaName`); spacer; "Local. No network." (`mediaLocalNote`, 11 pt medium secondary). (The panel variant also shows an `xmark.circle.fill` close button; not on the Settings page.)
2. **Tool picker**: segmented, labels hidden, full width: "Video" (`mediaToolVideo`) | "GIF" (`mediaToolGIF`) | "Image" (`mediaToolImage`) | "Text" (`mediaToolText`). Key `mediaLastTool` (default `videoCompressor`; values `videoCompressor`, `gifMaker`, `imageCompressor`, `textExtractor`; unknown -> video). Switching tool: cancels a pending video import, resets the service state and message, recomputes the default output and the trim defaults.
3. **Content** (VStack spacing 12): file card, options card, action row, status card.

**File card** (panel card, VStack spacing 9):
* *Input selector* (drop target + button): rounded rect radius 10, min height 62, fill `controlFill` (hover-with-drag: `accent @ 16%`), 0.8 px border `border` (drag: 1.2 px `accent @ 70%`). Content padding 12 (+34 trailing when a file is set): icon 16 pt semibold (`doc.badge.plus`, or `doc.text.viewfinder` for the Text tool), VStack: title 12.5 pt semibold 1 line middle-truncated = "Choose file" (`mediaSelectFile`) | the file name | "%d files selected" (`filesSelectedFormat`, Image tool with several files); second line 10.5 pt secondary "Drop a file here or click to choose one." (`mediaDropHint`). When a file is set, a trailing circular `xmark.circle.fill` (16 pt, secondary, 28x28 hit area, tooltip "Cancel" `mediaCancel`) clears the input and resets the state. Click = open file dialog (single file for Video/GIF/Text; multiple files for Image). Allowed types: Video/GIF = movie/video/mpeg4/quicktime; Image/Text = image. A dropped file of the wrong type (or any wrong file among several for non-image tools) -> failed state with `mediaErrorUnsupported`. Image tool accepts all dropped image files in drop order; other tools take the first.
* *Output row* (HStack spacing 7): "Output" (`mediaOutput`, 10.5 pt semibold secondary) + the output file name (11 pt, 1 line, middle truncation) or "Automatic" (`mediaOutputAutomatic`); spacer; small button `folder` + "Destination" (`mediaChooseOutput`; disabled with no input or while running). Single file -> a Save dialog (type = tool output type, prefilled name/folder); several images -> a folder chooser (the chosen folder becomes the output directory; mark "chosen manually").
* *Subfolder toggle* (Image tool with more than one file): checkbox "Save in “Converted” subfolder" (`saveInSubfolder`; key `mediaImageSaveInSubfolder`, default false).

**Options card** (panel card, VStack spacing 10) by tool:

*Video tool*
1. Time range: two numeric fields side by side: "Start" (`mediaStartTime`) and "End" (`mediaEndTime`), each = label (62 wide, 11 pt semibold) + monospaced text field (76 x 30, radius 7, 14 pt medium, `controlFill`, 0.8 px border) + unit "s"; decimal with at most 1 fraction digit, minimum 0. Keys `mediaVideoStart` (default 0), `mediaVideoEnd` (default 0 = to the end; filled with the real duration, rounded to 0.1 s, once the file loads; reset to 0/0 when a new file is chosen).
2. Sizing segmented (labels hidden): "Resolution" (`mediaSizingResolution`) | "File size" (`mediaSizingFileSize`); key `mediaVideoSizing` (`resolution` default | `targetSize`).
3. Resolution mode: **Compression row** then **Size stepper**. Target-size mode: **Target size row**.
   * *Compression row* (VStack spacing 5): label "Compression" (`mediaQuality`, 11 pt semibold); three equal buttons (height 32, radius 7, spacing 6; selected = accent text + `accent @ 16%` fill + 0.8 px `accent @ 45%` border; unselected = `primary @ 78%` text, `controlFill`, `border`): icon (`circle` / `circle.lefthalf.filled` / `circle.fill`, 11 pt semibold) + "Low" / "Medium" / "High" (`mediaCompressionLow/Medium/High`); description below (10.5 pt secondary) for the *nearest* level: "High quality, large file size" / "Balanced quality and file size" / "Low quality, small file size". Level -> quality value: Low 0.88, Medium 0.68, High 0.28; the stored value selects the nearest level. Key `mediaVideoQuality` default 0.68.
   * *Size stepper* (macOS `Stepper`): label "Size" (`mediaMaxSize`) left, value `"<n>px"` right (11 pt monospaced secondary); range 640...3840, step 320; key `mediaVideoMaxDimension` default 1280.
   * *Target size row*: label "Target size" (`mediaTargetSize`, 92 wide) + integer field (76 wide) + unit " MB" (`mediaMegabytesSuffix`); range 1...512 (1000-based megabytes); hint (10.5 pt secondary) "Resolution adapts to stay under the limit." (`mediaTargetSizeHint`). Key `mediaVideoTargetMegabytes` default 20.

*GIF tool*: Time range (keys `mediaGIFStart`/`mediaGIFEnd`, default 0), sizing segmented (key `mediaGIFSizing`), then in Resolution mode: **FPS slider row** (label "FPS" (`mediaFPS`) + value on the right in monospaced secondary; slider 1...30, step 1; key `mediaGIFFPS` default 12) and **Width stepper** "Width" (`mediaWidth`) range 160...1600 step 80 "px" (key `mediaGIFWidth` default 720); in Target-size mode the Target size row (key `mediaGIFTargetMegabytes` default 10). Always: checkbox "Loop GIF" (`mediaLoopGIF`; key `mediaGIFLoops` default true). Quality is fixed at 0.74 (key `mediaGIFQuality` 0.74, no UI). A GIF holds at most **300 frames**; if `duration x fps` exceeds it the run fails with "A GIF can be up to %d seconds long" (`gifTooLongFormat`, %d = floor(300 / fps)).

*Image tool* (top to bottom):
1. **Quick presets** row (small buttons, spacing 6): "Web" (`presetWeb`): JPEG, quality 0.72, resize Max side 1600, strip metadata, rename `{name}-web`; "Social" (`presetSocial`): PNG, quality 0.82, Max side 2048, strip metadata, rename `{name}-social`; "Docs" (`presetDocs`): PDF, quality 0.7, Max side 1600, strip metadata, background White, keep modified date on. Each preset overwrites the corresponding keys (other keys keep their values, defaults for fields the preset does not set: no watermark, rename pattern empty except where stated).
2. **Preview** (HStack spacing 10): thumbnail box (max 136 x 92, aspect = the *output* size after resize; radius 7, 0.8 px border; background white for JPEG/PDF (black when background = Black), transparent-ish `primary @ 4.5%` for PNG/HEIC with background Transparent); image drawn stretched / filled / fitted according to the exact-resize mode; live **watermark overlay** (white text 10 pt semibold with shadow, logo side = min(box) x scale, margin scaled to the box, opacity applied, anchored per position); placeholder `photo` 32 pt secondary when no file. Beside it: "Preview" (`preview`, 11 pt semibold) and "Output: <name>" (`outputName`, 10.5 pt secondary, 2 lines).
3. **Format** segmented: "JPEG" | "PNG" | "HEIC" | "PDF" (literal labels, tags `jpeg`,`png`,`heic`,`pdf`; key `mediaImageFormat` default `jpeg`; unknown -> jpeg). Label for accessibility `mediaFormat` "Format".
4. **Compression row** (same control as video; key `mediaImageQuality` default 0.72).
5. **Resize section**: menu picker titled "Resize" (`resize`) with items "No change" (`resizeNone`), "Max side" (`resizeMax`), "Width" (`resizeWidth`), "Height" (`resizeHeight`), "Custom" (`resizeExact`); key `mediaImageResizeKind` (`none`,`maxDimension` default,`width`,`height`,`exact`). Controls per kind: Max side -> stepper "Size" 64...20000 step 128 px (key `mediaImageMaxDimension` default 1600); Width -> stepper "Width" 1...20000 step 64 px (`mediaImageResizeWidth` default 1600); Height -> stepper "Height" (`height`) 1...20000 step 64 px (`mediaImageResizeHeight` default 1200); Custom -> two steppers side by side (Width, Height) + segmented "Stretch" (`exactStretch`) | "Fit" (`exactFit`) | "Fill" (`exactFill`) (key `mediaImageExactResizeMode` default `stretch`). Geometry: Max side scales so the longest side <= N and never enlarges; Width/Height scale proportionally to that exact size (may enlarge); Custom = exact canvas; Stretch distorts, Fit letterboxes on the chosen background, Fill crops to cover.
6. **More options** disclosure (`moreOptions` "More options", chevron row, 0.18 s; collapsed by default each time the view appears; content indented 25, top padding 6):
   * *Profile row*: menu picker titled "Profile" (`profile`): "No profile" (`noProfile`, tag "") + saved profiles; a `trash` button (tooltip "Delete profile", `deleteProfile`; disabled when none selected); if the selected profile differs from the current options: caption `pencil` + "Modified" (`profileModified`, 10 pt medium secondary); text field "Profile name" (`profileName`) + small `checkmark` "Update" (`updateProfile`; disabled with no profile selected; renames if a name is typed, else keeps the name) + small `plus` "Save new" (`saveAsNew`; empty name -> "Profile %d" (`profileDefaultNameFormat`, count+1)). Selecting a profile applies all its options. Keys `mediaImageProfiles` (JSON string, default `"[]"`, array of `{id,name,options}` sanitised) and `mediaImageSelectedProfileID` (default `""`).
   * Checkbox "Remove metadata" (`mediaStripMetadata`; key `mediaImageStripMetadata` default **true**; **hidden when format is PDF**).
   * *Background* segmented titled "Background" (`background`): "Transparent" | "White" | "Black" (`backgroundTransparent/White/Black`; key `mediaImageBackground` default `transparent`). JPEG and PDF have no alpha, so transparent areas render white (black if Black chosen).
   * *Watermark section*: menu picker "Watermark" (`watermark`): "Off" | "Text" | "Logo" | "Text + logo" (`watermarkOff/Text/Logo/Both`; key `mediaImageWatermarkKind` default `off`). Text/Both -> text field placeholder "Watermark text" (`watermarkTextPlaceholder`; key `mediaImageWatermarkText` default ""). Logo/Both -> row: name of the chosen logo file or "No logo" (`noLogo`) + spacer + small `photo` "Choose logo" (`chooseLogo`; image file dialog) + (when set) plain `xmark.circle.fill` to clear; key `mediaImageWatermarkLogoPath` default "". When not Off: menu picker "Position" (`position`): "Top left", "Top right", "Center", "Bottom left", "Bottom right" (`topLeft/topRight/center/bottomLeft/bottomRight`; key `mediaImageWatermarkPosition` default `bottomRight`); stepper "Opacity" (`opacity`) 10%...100% step 5% shown as `"45%"` (key `mediaImageWatermarkOpacity` default 0.45); stepper "Margin" (`margin`) 0...2000 step 8 px (`mediaImageWatermarkMargin` default 32); stepper "Scale" (`scale`) 5%...80% step 1% (`mediaImageWatermarkScale` default 0.18; logo side = scale x shorter canvas side; text size scales likewise). A logo watermark with an unreadable file fails the run with `noLogo` ("No logo").
   * *Rename*: caption "Rename" (`rename`, 11 pt semibold); text field placeholder `{name}-{index:03}` (key `mediaImageRenamePattern` default "" = `{name}`) + borderless menu button labelled `{…}` (monospaced) listing tokens `{name}`, `{index}`, `{index:03}`, `{counter}`, `{date}`, `{time}`, `{datetime}`, `{width}`, `{height}`, `{format}`; selecting appends the token to the field. Token semantics: `{name}` original base name (leading dots stripped; "Output" if empty), `{index}`/`{counter}` 1-based position (also `{index:02}`...`{index:06}` zero padded), `{date}` UTC `yyyyMMdd`, `{time}` UTC `HHmmss`, `{datetime}` UTC `yyyyMMdd-HHmmss`, `{width}`/`{height}` output pixel size, `{format}`/`{ext}` output extension; result is sanitised for file names (255-byte limit, reserve 4 bytes for a uniqueness suffix) and made unique (`name 2`... style).
   * Checkbox "Keep original modified date" (`preserveDate`; key `mediaImagePreserveModificationDate` default false).

*Text tool*: segmented titled "OCR" (`mediaOCRMode`): "Accurate" (`mediaOCRAccurate`, tag true) | "Fast" (`mediaOCRFast`, false); key `mediaTextAccurate` default true. (`mediaTextLanguageCorrection` default true exists but has no control. Strings `mediaLanguageCorrection` "Language correction" and `mediaTextOutputNote` "Extracted text can be copied and saved as TXT." exist for other hosts.)

**Action row** (HStack spacing 8): prominent button `play.fill` (or `text.viewfinder` for Text) + title: "Compress video" (`mediaStartVideo`) / "Make GIF" (`mediaStartGIF`) / "Process image" (`mediaStartImage`) or "Convert to PDF" (`mediaStartConvertPDF`) when the format is PDF / "Extract text" (`mediaStartText`); disabled without input or while running. Video tool only: button `crop` + "Edit" (`ScreenshotFeatureStrings.editButton`) - opens the file in the screen-recorder editor (trim) - disabled unless exactly one file is chosen and not running/importing; a small spinner shows while importing; if the file has no video track the local message `mediaErrorNoVideo` appears. While running: button `xmark` + "Cancel" (`mediaCancel`). Trailing spacer.

**Status card** (by `MediaServiceState`): 
* idle/ready: if a local message exists -> orange warning label (`exclamationmark.triangle.fill`, 11.5 pt medium) in a panel card.
* running(progress): panel card with `gearshape.2` + "Processing" (`mediaRunning`, 11.5 pt semibold), right-aligned "NN%" (11 pt monospaced secondary), and a determinate progress bar.
* completed: panel card: green `checkmark.circle.fill` + "Done" (`mediaCompleted`); text "Saved as %@" (`mediaResultSavedFormat`, file name; 11 pt, 2 lines, middle truncation) or, for a batch of >1 images, "%d images saved" (`batchSavedFormat`) / "%d saved, %d failed" (`batchPartialFormat`); "%@ to %@" (`mediaResultSizeFormat`, original size to output size, file style, 10.5 pt secondary); then "%@ saved" (`savedBytesFormat`) or "%@ larger" (`grewBytesFormat`) when sizes differ; if the output grew: "The converted file came out larger than the original." (`mediaResultGrewCaption`; shown only when original and output sizes are known and output > original); up to three failed batch items "<file>: <message>" (9 pt); for Text: the recognised text in a monospaced box (11 pt, 8 lines max, selectable, radius 7, `primary @ 4.5%`) or "No text found." (`mediaEmptyText`). Buttons (small): `folder` "Show" (`mediaOpenInFinder`; reveals the output(s)), `doc.on.doc` "Copy text" (`mediaCopyText`; Text tool), `doc.on.doc` "Copy summary" (`copySummary`; batches; copies "%d saved, %d failed" header + optional saved/larger line + one line per file "%@ -> %@" (`batchSummaryItemFormat`) or "<file>: <error>"), `arrow.clockwise` "Run again" (`mediaRunAgain`).
* failed: warning label with the message: no input "Choose a file first." (`mediaErrorNoFile`), no video track "This file has no video track.", same output "Choose a destination different from the original file.", unsupported "Format not supported by macOS." [WIN: "Format not supported by Windows."], image too large "These dimensions are too large to process safely. Choose a smaller size." (`tooLarge`), GIF too long, target too small "Target size too small for this clip. Trim it or raise the limit." (`mediaErrorTargetTooSmall`), watermark "No logo", or the engine's message (falls back to unsupported when empty).
* cancelled: secondary `xmark.circle.fill` + "Cancelled." (`mediaCancelled`).

### 8.2 Settings (Media) - all registered defaults
| Key | Type | Default |
|---|---|---|
| `mediaLastTool` | string | `videoCompressor` |
| `mediaVideoStart` / `mediaVideoEnd` | double | 0 / 0 |
| `mediaVideoQuality` | double | 0.68 |
| `mediaVideoMaxDimension` | int | 1280 |
| `mediaVideoFPS` | double | 30 (no UI; run uses 30) |
| `mediaVideoKeepAudio` | bool | true (no UI) |
| `mediaVideoCodec` | string | `h264` (no UI) |
| `mediaVideoSizing` | string | `resolution` (`resolution`,`targetSize`) |
| `mediaVideoTargetMegabytes` | int | 20 (1...512) |
| `mediaGIFStart` / `mediaGIFEnd` | double | 0 / 0 |
| `mediaGIFQuality` | double | 0.74 (no UI) |
| `mediaGIFWidth` | int | 720 (160...1600) |
| `mediaGIFFPS` | double | 12 (1...30) |
| `mediaGIFLoops` | bool | true |
| `mediaGIFSizing` | string | `resolution` |
| `mediaGIFTargetMegabytes` | int | 10 |
| `mediaImageQuality` | double | 0.72 (0.1...1) |
| `mediaImageMaxDimension` | int | 1600 |
| `mediaImageFormat` | string | `jpeg` (`jpeg`,`png`,`heic`,`pdf`) |
| `mediaImageStripMetadata` | bool | true |
| `mediaImageResizeKind` | string | `maxDimension` (`none`,`maxDimension`,`width`,`height`,`exact`) |
| `mediaImageResizeWidth` / `mediaImageResizeHeight` | int | 1600 / 1200 |
| `mediaImageExactResizeMode` | string | `stretch` (`stretch`,`fit`,`fill`) |
| `mediaImageWatermarkKind` | string | `off` (`off`,`text`,`logo`,`textAndLogo`) |
| `mediaImageWatermarkText` / `...LogoPath` | string | "" / "" |
| `mediaImageWatermarkPosition` | string | `bottomRight` (`topLeft`,`topRight`,`center`,`bottomLeft`,`bottomRight`) |
| `mediaImageWatermarkOpacity` | double | 0.45 (0.1...1) |
| `mediaImageWatermarkMargin` | int | 32 (0...2000) |
| `mediaImageWatermarkScale` | double | 0.18 (0.05...0.8) |
| `mediaImageRenamePattern` | string | "" |
| `mediaImageBackground` | string | `transparent` (`transparent`,`white`,`black`) |
| `mediaImagePreserveModificationDate` | bool | false |
| `mediaImageSaveInSubfolder` | bool | false |
| `mediaImageProfiles` | string (JSON) | `"[]"` |
| `mediaImageSelectedProfileID` | string | "" |
| `mediaTextAccurate` | bool | true |
| `mediaTextLanguageCorrection` | bool | true (no UI) |
The `@AppStorage` fallbacks in the view equal the registered defaults for every media key.

### 8.3 Behaviour (Media)
* Default output names: Video `<name>-compressed.mp4` next to the input; GIF `<name>.gif`; Image `<pattern>.<ext>` in the input's folder (or `<folder>/Converted/` for batches with the subfolder toggle); Text `<name>-text.txt`. All made unique (never overwrite; "same as input" is refused with `mediaErrorSameOutput`). When the user did not pick a destination manually the output name follows format/option changes live (for single files; if picked manually only the extension follows a format change).
* Output is written to a staged temp file next to the destination and moved into place on success (a cancelled/failed run leaves nothing); hidden dot-file outputs are made visible.
* Video: trim `[start,end]` (end 0 = full), re-encode H.264 MP4 keeping audio, 30 fps cap; *Resolution* mode picks an export preset from quality + max dimension (scaled to even dimensions); *File size* mode plans a bitrate budget (target bytes x 0.94 headroom, min video bitrate 240 kb/s, ~0.07 bits/pixel) and retries (scale step down >= 10%, up to the encoder's pass limit) until under the ceiling, else `mediaErrorTargetTooSmall`. Progress reported 0...1; Cancel stops immediately.
* GIF: samples frames over the trimmed range at `fps` (max 300 frames), scales to `width`, writes GIF with loop count 0 (infinite) or none; *File size* mode starts at a high fps and a start width then drops frames first, then width, up to 4 passes, min width 160, min fps 6.
* Image: decode with orientation applied (thumbnail-with-transform), render at target size onto the background (with watermark text/logo), encode as JPEG/HEIC with `quality`, PNG lossless, PDF = JPEG at `quality` embedded in a one-page PDF (page = image size); metadata stripped when the toggle is on (and always for PDF); EXIF orientation reset to 1 when rendered; limits: any side <= 20000 px and pixels <= 8192 x 8192 (else `tooLarge`); batch runs sequentially, continues past failures, reports processed/failed counts.
* Text: Vision text recognition (`accurate` or `fast`, language correction on, languages by app language); output saved as UTF-8 TXT (`<name>-text.txt`) and shown in the result box.
* Workspace selection survives panel re-hosting; duration lookup (video/GIF) runs async and fills End once.
* The feature being uninstalled while a job runs cancels it.

### 8.4 Windows mapping (Media)
* **Video**: Windows.Media.Transcoding (`MediaTranscoder`) with `MediaEncodingProfile.CreateMp4(VideoEncodingQuality.*)` customised (`Video.Bitrate`, `Video.Width/Height` even), `TrimStartTime/TrimStopTime` for the range, `PrepareFileTranscodeAsync` -> progress via `IAsyncActionWithProgress<double>` -> exactly the 0-1 bar; Cancel = `AsyncInfo.Cancel()`. Quality levels map to bitrate = f(pixels x fps x {0.12, 0.07, 0.035}); target-size = compute bitrate budget as above (audio 96-128 kb/s) and re-encode with smaller resolution until size <= target x 0.94. **Alternative/fallback** (and what Trayloom does today): `ffmpeg.exe` on PATH (`libx264 -crf` mapped from quality, `-vf scale`, `-ss/-to`), show "ffmpeg not found" note otherwise.
* **GIF**: extract frames with `MediaComposition` + `MediaClip.GetThumbnailAsync`/Media Foundation source reader, quantise (WIC `GifBitmapEncoder` palette generation, or an octree/median-cut quantiser per frame set), write with NETSCAPE2.0 loop extension (0 = infinite) and per-frame delay `1/fps`; or `ffmpeg` palettegen/paletteuse (existing Trayloom implementation).
* **Image**: WIC (`BitmapDecoder` -> `BitmapTransform` for scale/flip + EXIF orientation -> compose via `DrawingVisual`/`RenderTargetBitmap` or Direct2D for background/fit/fill/watermark -> `JpegBitmapEncoder` (QualityLevel 1-100 = quality x 100) / `PngBitmapEncoder` / HEIC via `Windows.Graphics.Imaging.BitmapEncoder` HEIF codec (needs the HEIF Image Extensions; hide HEIC or show "Format not supported by Windows." if the codec is missing)). PDF: write a minimal one-page PDF containing the JPEG stream (`/DCTDecode`, MediaBox = image px x 72/96) or use `PdfSharp`; metadata: copy via `BitmapMetadata` when stripping is off, drop when on. `preserveDate` = `File.SetLastWriteTime` from the source. Output naming/rename tokens as above.
* **Text (OCR)**: `Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages()`; Accurate = upscale small images (shorter side < 1000 px -> 2x) before recognition, Fast = single pass at native size; language correction n/a.
* Dialogs: `IFileOpenDialog` (multi-select for images), `IFileSaveDialog` with extension filter, `IFileOpenDialog(FOS_PICKFOLDERS)` for batch destination; drag & drop `DataFormats.FileDrop`; "Show" = `explorer /select,"<path>"`; steppers = WPF `Stepper`-like repeat buttons around a monospaced `TextBlock` (min/max/step as above); slider `Slider` (1...30, snap 1); segmented controls = `ListBox`/radio strips; disclosure = `Expander` header row with rotating chevron (0.18 s).
* "Edit" (trim video) opens the Screen Recorder editor (Windows recorder spec); hide the button when that feature is not installed.
* Strings: `mediaErrorUnsupported` -> "Format not supported by Windows."; `ocrName` "Copy text from screen" unchanged.

### 8.5 Gap vs Trayloom (`MediaToolsFeature.cs`)
Has: stand-alone "Media compress" window (batch DataGrid of File/Before/After/Status, Add files, Clear, Compress/Stop, progress bar, option side panel), image compression through WIC with "Format" (keep/JPEG/PNG), "Quality" 30-100 slider, "Maximum size" choices, "Output folder" text (default: `-min` suffix next to the original), optional ffmpeg for video/GIF; keys: `hotkey`, `format`, `quality`, `maxSize`, `outputFolder`.
Lacks: the four-tool segmented workspace (Video | GIF | Image | Text) and `mediaLastTool`; header + "Local. No network."; drop-zone input card with output row/Destination; trim fields; Resolution / File size modes and the target-size encoder; Compression Low/Medium/High buttons with descriptions; FPS/Width/Loop options for GIF; Image presets, preview with live watermark overlay, HEIC and PDF output, resize modes (Max side/Width/Height/Custom with Stretch/Fit/Fill), background, watermark (text/logo/both, position, opacity, margin, scale), rename tokens, profiles, keep-date, metadata toggle, subfolder toggle; OCR tool; result card (Saved as, sizes, saved/larger, Show/Copy/Run again) and error copy; all `media*` keys (quality scale in Trayloom is 30-100 ints vs 0.1-1 doubles in Vorssaint - migrate to the double scale); "Edit" hand-off to the recorder editor.

---------------------------------------------------------------------------------------------------------------

## 9. About page (`AboutSettings` + `UpdatesView`)

### 9.1 Layout (grouped Form, two sections)
**Section 1 (no header) - identity block** (VStack spacing 14, centred, vertical padding 20, full width):
1. **Brand badge** 76 x 76: rounded square, corner radius 0.26 x size (continuous), fill `Theme.spaceGradient` (linear top-leading -> bottom-trailing: white 10% -> white 4% -> black), containing the brand mark at 80% of the width. **Trayloom must ship its own icon/mark here (never the Vorssaint saturn).**
2. VStack spacing 3: app name `AppInfo.name` = "Trayloom" (`title2` bold = 17 pt bold); HStack spacing 6: "Version <x.y.z>" (`versionPrefix` "Version" + `AppInfo.version`, caption secondary) and, for pre-release builds (`-beta`, `-rc`, `-alpha` in the version), an orange capsule "Beta" (`betaBadgeLabel`; 9 pt bold, padding h5 v1.5, text orange, fill `orange @ 18%`); developer builds only: the build commit (caption2 monospaced tertiary, selectable) - never shipped.
3. Description (12 pt secondary, centred, multi-line): "A utility hub for your Mac.\nEnergy, system monitor, scrolling and a window switcher, right in the menu bar." (`aboutDescription`) [WIN: "A utility hub for your PC.\nEnergy, system monitor, scrolling and a window switcher, right in the system tray."]
4. Button row (HStack spacing 12): bordered "Review introduction" (`reviewIntro`; opens the onboarding window - see onboarding spec), bordered "Review highlights" (`reviewHighlights`; opens the update-highlights window in review mode, §12.3), link "View on GitHub" (`viewOnGitHub`; `AppInfo.repositoryURL`, Trayloom's repository).
5. Copyright (caption2 tertiary): "© 2026 Trayloom" (`AppInfo.copyright`).

**Section 2, header "Updates"** (`updatesSection`) - `UpdatesView`:
1. Switch "Check for updates automatically" (`autoCheckToggle`; key `autoCheckUpdates`, default **true**) - writing it re-configures the service's hourly timer.
2. Switch "Receive beta updates" (`includeBetaUpdatesToggle`; key `includeBetaUpdates`) + caption (4 pt below) "Beta versions include features in development and may contain bugs or incomplete behavior." (`includeBetaUpdatesCaption`). Registered default is **false**; the view fallback is `AppInfo.isBeta`; the update service additionally sets it to true once when the running build is a beta and the key was never written.
3. Switch "Show what’s new before updating" (`WhatsNewStrings.showPreviewToggle`; key `showReleaseNotesBeforeUpdate`, default true) + caption (only while "Show what’s new after updating" is on) "What changed in your features still appears after each update." (`showPreviewCaption`).
4. Switch "Show what’s new after updating" (`releaseNotesOnUpdateToggle`; key `releaseNotesOnUpdate`, default true).
5. **Status row** (HStack spacing 6, callout): by `UpdateService.State`:
   * idle - nothing; checking - `arrow.triangle.2.circlepath` (secondary) "Checking…" (`updateChecking`); upToDate - green `checkmark.circle.fill` "You’re on the latest version." (`updateUpToDate`); available - accent `arrow.down.circle.fill` "Update available: <version>" (`updateAvailablePrefix` + space + version); downloading - secondary `arrow.down.circle` "Downloading update…" (`updateDownloading`) + " NN%" when the size is known; installing - secondary `gearshape.2.fill` "Installing and restarting…" (`updateInstalling`); failed - orange `exclamationmark.triangle.fill` "Couldn’t check: <reason>" (`updateFailedPrefix`).
6. Button row: bordered "Check now" (`checkNowButton`; disabled while checking/downloading/installing) -> `check(manual: true)`; when an update is available also bordered-prominent "Download and install" (`updateInstallButton`) -> opens the pre-install What's New window (§12.1) (or downloads right away when `showReleaseNotesBeforeUpdate` is off).
7. If a check has run: caption tertiary "Last checked: <short date, short time>" (`updateLastChecked`).

### 9.2 Settings
| Key | Type | Default | Effect |
|---|---|---|---|
| `autoCheckUpdates` | bool | true | Check 6 s after launch, then hourly (5-min tolerance), plus when the app reactivates / the panel opens and the last check is older than 15 min |
| `includeBetaUpdates` | bool | false (true on beta builds) | `releases?per_page=10` instead of `releases/latest`; pre-releases eligible |
| `showReleaseNotesBeforeUpdate` | bool | true | Pre-install What's New window |
| `releaseNotesOnUpdate` | bool | true | Post-update What's New window |
| `simulateUpdate`, `simulateBetaUI` | bool | false | developer-build only simulation flags (do not expose) |

### 9.3 Behaviour (self-update)
* `UpdateService` states: idle, checking, upToDate, available(version), downloading(progress?), installing, failed(text). Check = GitHub Releases API (`/repos/<owner>/<repo>/releases/latest`, or `/releases?per_page=10` with betas), `Accept: application/vnd.github+json`, `User-Agent: Trayloom/<version>`, no cache; chooses the newest eligible semantic version greater than the running one (drafts ignored; pre-releases only with the beta option); takes the release body as the notes (distribution footer lines removed) and the `.dmg` asset URL + expected size. A background (non-manual) find posts one notification per distinct version: title `updateNotifyTitle` "Trayloom update", body "Update available: <version>".
* Download is bounded by the advertised size (aborts on overrun), verified, then a detached installer replaces the app and relaunches; a failed swap is reported on next launch. A developer build never updates.
* The menu panel shows an update banner whose button also opens the pre-install window.

### 9.4 Windows mapping
* Source: GitHub Releases of Trayloom's repository; asset = installer (`Trayloom-Setup-<ver>.exe` Inno Setup/WiX bootstrapper or `.msi`/`.msix`). Download to `%TEMP%\Trayloom-update`, verify size and a published SHA-256 (asset `SHA256SUMS` or digest in the release body), then run the installer detached with silent switches (`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS` for Inno) and exit; per-user install (`%LOCALAPPDATA%\Programs\Trayloom`) needs no UAC, per-machine installs elevate via the installer manifest. Portable build: replace-on-exit helper. Store/winget installs: show "Update available" but the button opens the Store page / runs `winget upgrade --id <Trayloom id>`.
* Version display: `FileVersionInfo`/`AssemblyInformationalVersion` (semver incl. `-beta.N`). Beta detection by `-beta|-rc|-alpha`.
* Toast for the update notification; timers via `DispatcherTimer`/`System.Threading.Timer`; reactivation via `Application.Activated`.
* Description copy and the brand badge must be Trayloom's own.

### 9.5 Gap vs Trayloom
No About page and no self-update service exist in `src` (only `AutoStart.cs`, tray, features window). Needs: About page, Updates section (4 keys), `UpdateService` with GitHub check + download/install, notification, banner hook, pre-install window.

---------------------------------------------------------------------------------------------------------------

## 10. What's New page (sidebar "What’s New", `ReleaseNotesSettings` -> shows `ReleaseNotes.current`)

### 10.1 Layout
VStack(alignment leading, spacing 14), fills the pane, top-leading:
1. Header (padding h24, top 24): title "What’s new in this version" (`obWhatsNewTitle`, title2 bold) over version line (caption secondary): `v<version> · <date>` (e.g. "v3.4.3 · 2026-10-29") or just `v<version>` when the changelog heading has no date.
2. Scroll area (padding h24, bottom 24; sections spacing 18):
   * No sections found for the running version (and no "Unreleased" either): `fallbackNote` = accent `checkmark.circle.fill` 13 pt semibold (18 wide) + "This update includes the latest fixes and improvements." (`obWhatsNewFallback`, 12.5 pt secondary).
   * Else, per section: heading = section title UPPERCASED, 11 pt bold secondary, letter-spacing 1.2 (omitted if the title is empty); then items (VStack spacing 9):
     * paragraph: 12.8 pt primary text.
     * bullet: HStack(top, spacing 9): accent icon (12 pt semibold, 18 wide) chosen from the section title (case-insensitive): "added" -> `plus.circle.fill`, "changed" -> `slider.horizontal.3`, "fixed" -> `checkmark.circle.fill`, anything else -> `circle.fill`; text 12.5 pt primary, wraps.
     * image (`![alt](path)` line): resizable, scaled to fit, radius 8, 1 px `quaternary` stroke, left padding 27, accessibility label = alt; path resolved against the bundled `Resources/` folder (missing file -> nothing drawn).
   * Text cleanup before display: markdown links `[label](url)` -> `label`, `**` and backticks removed, continuation lines (2-space indented) appended to the previous bullet; a leading paragraph before any `###` becomes a section titled "Summary"; sections "Website" and "Links" are dropped.

### 10.2 Data
`ReleaseNotes.current` = parsed notes for `AppInfo.version` from the bundled `CHANGELOG.md` (Keep-a-Changelog: `## [x.y.z] - YYYY-MM-DD`, `### Added|Changed|Fixed|Summary|...`, `- bullet`); if that version has no sections, the `## [Unreleased]` block; else empty. Bundle `CHANGELOG.md` with Trayloom's own release history.

### 10.3 Settings
None (read-only page).

### 10.4 Windows mapping / Gap
Render with a `FlowDocument` or a `StackPanel` of `TextBlock`s; icons Segoe: plus.circle.fill = E710 in a circle -> use E948 ? (AddTo) / fallback "+" glyph E710; `slider.horizontal.3` = E9E9 (Equalizer) ?; `checkmark.circle.fill` = E73E / EC61 (CompletedSolid); `circle.fill` = E91F (FullCircleMask) ?. Load the changelog as an embedded resource. **Gap:** no What's New page, no changelog parser, no bundled changelog in Trayloom.

---------------------------------------------------------------------------------------------------------------

## 11. Support page (`SupportSettings`)

### 11.1 Layout
ScrollView; VStack(spacing 18), centred, horizontal padding 28, vertical 26:
1. 78 x 78 circle filled with `Theme.spaceGradient` (near-black) with `heart.fill` 29 pt semibold white centred.
2. VStack spacing 7: heading "Help Trayloom keep growing" (`donateHeading`, title2 bold, centred); message (13.5 pt secondary, centred, max width 460) "Trayloom stays free and local, with no ads or usage tracking. If it helps you, Buy Me a Coffee is the only way to help fund its development." (`donateMessage`).
3. Prominent large button `cup.and.saucer.fill` + "Support on Buy Me a Coffee" (`donateButton`) -> `AppInfo.coffeeURL` (Trayloom's own Buy-Me-a-Coffee page; replace `buymeacoffee.com/vorssaint`).
4. **Star card** (HStack top, spacing 14, padding 16, max width 510, radius 14 continuous, fill `controlBackground`, 1 px `separator @ 45%`): 38 x 38 circle `yellow @ 14%` containing `star.fill` 18 pt semibold yellow; VStack(spacing 10): text "Financial support is never expected. A star on GitHub helps more people discover Trayloom and makes a real difference to its development." (`supportIntroStarMessage`, 13.5 pt medium), bordered large button `star.fill` + "Star Trayloom on GitHub" (`supportIntroStarButton`) -> `AppInfo.repositoryURL`.
5. Caption tertiary `donateThanks` "Thank you for being here. 🖤" - in dark appearance the black heart is replaced by a white heart 🤍.
Other strings: `supportIntroLaterButton` "Not now", `supportIntroDoneButton` "Done", `supportIntroCoffeeButton` "Support on Buy Me a Coffee", `supportIntroStarHint` "A star on GitHub also helps more people find Trayloom, and it means a lot."

### 11.2 Settings
None.

### 11.3 Windows mapping / Gap
Open URLs with `Process.Start(new ProcessStartInfo(url){UseShellExecute=true})`; hearts in Segoe UI Emoji; icons Segoe: heart E006 (Heart, MDL2) or EB51 (HeartFill) ?; star E734/E735; coffee: no glyph - use U+2615 from Segoe UI Emoji (monochrome Segoe UI Symbol). Dark/light heart swap via theme. **Gap:** no Support page, no `AppInfo` URLs (website/repository/coffee/discord/social) in Trayloom. (Also add the feedback entry, §13.)

---------------------------------------------------------------------------------------------------------------

## 12. Update windows (post/pre-update surfaces)

All are separate top-level windows hosted by the app delegate; style: titled + closable, full-size content with transparent title bar and hidden title, not restorable, centred on the active screen (origin clamped inside the visible frame so a tall window starts at the top), content decides the size. Windows that must stay reachable (What's New, support intro) are floating level, never hide on deactivate, and keep the app in "regular" activation policy while open. Window titles (accessibility/task-switcher): `tabReleaseNotes` "What’s New" (pre-install and digest windows), `highlightsTitle` "New in this update" (highlights), `supportIntroTitle` "Help Trayloom keep growing" (support intro), `updateShowcaseTitle` (showcase).

### 12.1 Pre-install preview (`UpdatePreviewView`, 640 x 600)
Opened from the About page "Download and install" button, the menu-panel update banner and the island banner, **only when** `showReleaseNotesBeforeUpdate` is on (otherwise the download starts immediately and the panel keeps showing progress).
VStack spacing 0: header HStack (padding h28, top 24, bottom 16): "What’s New" (`tabReleaseNotes`, 22 pt bold); Divider; scroll (padding h28 v22) with `ReleaseNotesContent` for the offered version: a block per release - heading "v<version>" (16 pt bold, accent for the latest/first, primary otherwise) + date caption; the release's sections as in §10 (the release body is synthesised as `## [<version>]` + the release-notes markdown without the distribution footer); several releases are separated by a Divider with 18 pt vertical padding; Divider; footer (padding 16): checkbox "Don’t show what’s new before updating" (`skipPreviewCheckbox`, wraps), spacer(min 8), **Cancel** (`uninstallerCancel`, Esc), prominent **"Download and install"** (`updateInstallButton`, Return). Ticking the checkbox only persists (`showReleaseNotesBeforeUpdate = false`) when "Download and install" is pressed (never on Cancel). Pressing it closes the window and starts the download (progress shown in the panel/About page).

### 12.2 Post-update digest (`WhatsNewDigestView` + `WhatsNewSupport`; size = min(800, screen w - 32) x min(560, screen h - 32))
Two-pane split view (sidebar fixed 230 wide, no sidebar toggle), detail has a bottom footer.
* **Sidebar list**: first row (selection `overview`): 28 pt brand badge + VStack: "Trayloom <version>" (semibold) over "Updated from %@" (`updatedFromFormat`, caption secondary; omitted without a previous version). Sections: **"New features"** (`newFeatures`) rows for features the app introduced since the last stored feature list; **"Your features"** (`yourFeatures`) rows for installed features that changed, grouped under the Settings sidebar row they live on, in Settings order, the rows `General` ("General" = `tabGeneral`, gear icon) for app-wide changes last; **"Not installed"** (`notInstalled`) rows for changed features this PC does not have (disclosure section, **collapsed** until opened). Row = HStack(spacing 9): icon (18 wide; primary when selected, accent tint when installed, secondary when not), title (primary if installed or selected, else secondary; tooltip = title), spacer, 7 px accent dot while unread (VoiceOver value "Updated" `updated`). All New/Your rows start unread; selecting a row clears its dot. Rows are fixed when the window opens (installing a feature from it does not reorder).
* **Overview detail** (selected by default; scroll, centred, padding h32 top 44 bottom 24, spacing 22): 64 pt brand badge; "Trayloom <version>" (26 pt bold) + "Updated from %@" (secondary); counts HStack (spacing 40) per kind that has changes: number (30 pt semibold rounded, monospaced digits) over label (caption secondary) "New" (`kindNew`), "Changed" (`kindChanged`), "Fixed" (`kindFixed`) - counts are unique changes across the New + Your features; summary paragraph (13 pt secondary, centred, max 460) = newest "Summary" paragraph; hint "Choose a feature to see what changed in it." (`chooseFeatureHint`, callout tertiary).
* **Feature page** (grouped Form): header section: icon 24 pt (tint), title (title2 bold) + the feature's hub description (secondary), spacer, action - if the feature is not installed: "Install" (`FeatureHubStrings.installButton`; prominent for a *new* feature, bordered otherwise; disabled with a tooltip when `installBlockedReason` applies, e.g. unsupported hardware) -> installs the feature (0.22 s ease-out animation); else if the row has a settings destination: bordered "Open Settings" (`CommandBarStrings.actionOpenSettings`) -> opens that Settings page beside this window. Then one section per kind present, in the order New, Changed, Fixed, headed `kindNew/kindChanged/kindFixed`; rows (HStack, baseline, spacing 8, selectable, vertical padding 2): accent `plus.circle.fill` for New items; text; and, if the entry ends with a "Settings → …" sentence, that path on its own caption line below.
* **Footer** (Divider + padding 16, right aligned): prominent **"Done"** (`supportIntroDoneButton`, Return); Esc also closes.
* Content logic: `ReleaseDigest.make(from: previous, to: current, changelog)` = changelog entries of every released version after the one the user updated from, up to the current (max 10 versions; a stable update reads stable sections only; a beta update skips stable sections whose betas it read); each bullet is classified by its `###` section (Added*/Highlights -> New; Fixed/Hotfix/Safety and reliability -> Fixed; others -> Changed; Summary/Contributors/Docs/Details/Website/Links/"Everything from x" ignored), de-duplicated by text, and linked to features by `ReleaseChangeAliases` (whole-word alias matching; unmatched bullets are app-wide -> "General"). The window opens only if `hasContentForYou` (new features or changes for installed features) - otherwise the update counts as read silently.
* Persistence: `whatsNewPendingVersion` (the version the next window starts after; kept across a second update; dropped on downgrade), `whatsNewSeenVersion` (last version that finished), `whatsNewKnownFeatures` (comma list of all feature ids known at that time; first run initialises it to "everything", so a fresh install shows no New features); on completion/skip: pending removed, seen = current version, known = current feature list. The window is skipped (and marked read) when `releaseNotesOnUpdate` is off.
* Opening Settings from a feature page: Settings opens next to this window (placed side by side by `tourPlacement`); on screens too narrow for both, Settings is raised above (floating) until What's New closes.
* Review mode: "Review highlights"/What's New review from the About page re-opens it with `includeUnreleased` and shows rows even when nothing concerns the user.

### 12.3 Update highlights (`UpdateHighlightsView`, max 600 x 660, content width <= 500, padding 24, spacing 16)
Per-release one-time tour of the headline feature (Vorssaint 3.4: Dynamic Island). Opened automatically after the update when `UpdateHighlightsInfo.shouldShow` (running version matches the release series and `updateHighlightsSeenVersion` differs from the marker), and from About -> "Review highlights".
* Header (42 pt tall): title = the feature's name (Vorssaint: `FeatureStrings.notch.title` "Dynamic Island" [WIN: the Island feature's Trayloom name]) in title2 semibold, subtitle "Trayloom <version>" (caption secondary; "Preview for 3.4" in developer builds).
* Scroll content: artwork area (height = min(400, max(80, window height - 260)), clipped, hidden from accessibility): animated GIF `highlights-notch.gif` (451 x 400, animates unless Reduce Motion is on) or, if the resource is missing, `rectangle.topthird.inset.filled` 72 pt light secondary; caption (callout secondary, centred): "Control music, timers, volume and brightness. Choose the floating shortcuts you use most." (`NotchTourStrings.caption`).
* Buttons (height 32): bordered "Set up" (`highlightsConfigure`; installs the feature if needed, opens its Settings page beside the tour) and, trailing, prominent "Done" (`supportIntroDoneButton`, Return).
* Window: floating panel, movable by background, closes with Done.
* Keys: `updateHighlightsSeenVersion` (string, default none): written when the window is closed after an automatic show (stable marker "3.4.0", beta marker "3.4.0-beta.1").
* Other highlight titles that exist in `Strings` for future releases: `highlightsTitleClipboardRedesign` "Redesigned clipboard", `highlightsTitleWindowLayout` "Window Layout", `highlightsTitleQuitProtection` "Quit and close protection", `highlightsTitleRecorderBlur` "Recording privacy blur" (artwork in `Resources/Images/highlights-*.{jpg,png}`).

### 12.4 Legacy release showcase intro (`UpdateShowcaseIntroView`, 680 x 600) and support intro (`UpdateSupportIntroView`, 560 wide)
* *Showcase*: step 1 - title (24 pt bold, centred) `updateShowcaseTitle` ("What’s new in 3.1.4" in Vorssaint; Trayloom: "What’s new in <version>"), message (13.5 pt secondary, centred, max 520) "Take a quick look at the main improvements in this update."; media surface 592 x 369 (radius 14, `primary @ 6%` fill, 1 px quaternary stroke, 16:9): loading (spinner + `homebrewLoading` "Loading…"), ready (looping muted video with inline controls, or an animated GIF with a small "Restart" button `gobackward` (`updateShowcaseRestart`) below), failed (`play.rectangle` 30 pt + "The video could not load right now. You can still continue."); media downloaded from the GitHub release asset with SHA-256 verification into the cache and deleted when the window closes. Footer: "Not now" (`supportIntroLaterButton`, Esc) and prominent "Continue" (`obContinue`, Return) -> step 2 = the support card (below) with a single "Not now" default button. Shown once per release (`updateShowcaseIntroVersion`, default "") on stable builds only.
* *Support intro* (shown once after the stable release 3.4.3 and later patches; marker `supportUpdateIntroVersion` = "3.4.3-support"): VStack spacing 18, padding h28 top 24 bottom 18: card contents - 44 pt circle (`spaceGradient`) with `heart.fill` 19 pt; title `supportIntroTitle` "Help Trayloom keep growing" (20 pt bold); `supportIntroMessage` (13.5 pt secondary); prominent large `cup.and.saucer.fill` "Support on Buy Me a Coffee" (`supportIntroCoffeeButton`); plain caption button `star` + "A star on GitHub also helps more people find Trayloom, and it means a lot." (`supportIntroStarHint`; tooltip `supportIntroStarButton`); thanks caption tertiary (heart colour swaps in dark mode); footer right-aligned bordered grey "Done" (`supportIntroDoneButton`, default). The window has no close button (only Done closes it). Trayloom should decide its own release gates (the 3.4.3 / 3.1.4 version constants are Vorssaint-specific).
* Order on launch (`presentUpdateIntros`, only one window per launch): What's New digest -> update highlights -> (stable builds only) support intro -> showcase -> brightness setup prompt.

### 12.5 Settings / keys (update windows)
| Key | Type | Default | Meaning |
|---|---|---|---|
| `whatsNewPendingVersion` | string? | none | Window starts after this version |
| `whatsNewSeenVersion` | string? | none | Last version whose window finished |
| `whatsNewKnownFeatures` | string | none (initialised at first launch to all feature ids) | Feature ids already known |
| `updateHighlightsSeenVersion` | string? | none | Highlights marker already seen |
| `supportUpdateIntroVersion` | string? | none | Support intro marker (`3.4.3-support`) |
| `updateShowcaseIntroVersion` | string | "" | Showcase seen for this release |
| `lastUpdateIntroVersion`, `featuresOnboardingVersion`, `brightnessUpdatePromptState` | string | none | Legacy/other intro gates (onboarding spec) |

### 12.6 Windows mapping / Gap
Windows: WPF windows `WindowStyle=SingleBorderWindow` (Mica backdrop on Win11, `WindowChrome` with extended title bar), `Topmost=true` for the floating ones, `ShowActivated`, `WindowStartupLocation=CenterScreen` clamped to the work area; Esc/Enter via `IsCancel`/`IsDefault`. Sidebar = `ListBox`/`TreeView` 230 px with section headers, unread dots, collapsed "Not installed" `Expander`. GIF playback: WpfAnimatedGif-style `ImageBehavior` or MediaElement; video: `MediaElement` (muted loop). Download of showcase assets: `HttpClient` + SHA-256 check. "Settings placed beside": compute side-by-side rectangles within `SystemParameters.WorkArea`.
**Gap:** none of these windows, the changelog digest, the feature-alias classifier, the pending/seen/known-features persistence, nor the launch ordering exist in Trayloom. The existing Features window (`FeaturesWindow.xaml`) is unrelated.

---------------------------------------------------------------------------------------------------------------

## 13. Feedback window (`FeedbackView`; entry rows live in General and the Command Bar)

Entry points: General page "Feedback" row (`sectionTitle` "Feedback", caption "Send a bug report or feature idea directly to the person who maintains Trayloom.", button `openButton` "Send feedback"); Command Bar commands `commandBug` "Report a bug", `commandFeature` "Suggest a feature" (subtitle `commandSubtitle` "Send feedback").
Window 600 x 650, VStack spacing 0:
1. Header (padding h24 v18): `bubble.left.and.text.bubble.right.fill` title2 tinted + `windowTitle` "Send feedback" (title2 semibold) + spacer + bordered "Done" (`done`). Divider.
2. Form (scroll, padding 24, spacing 18): segmented picker "Bug" (`bugTitle`) | "Feature idea" (`featureTitle`) (initial kind from the entry point, default Bug); label "What would you like to share?" (`messageLabel`, headline) over a multiline text editor (min height 145, padding 6, radius 9, `background.secondary` fill, 1 px separator stroke) with placeholder (tertiary) `bugPlaceholder` "Tell me what happened and what you expected." or `featurePlaceholder` "Describe the idea and how it would help."; right-aligned counter `charactersFormat` "%d of 2000 characters" (caption, monospaced digits, red when > 2000; input is truncated at 2000 UTF-16 units on paste); switch "Include technical details" (`includeDiagnostics`, default off) + caption "Adds only the technical details shown below. It does not include logs." (`includeDiagnosticsCaption`); "What will be sent" box (padding 16, radius 12, `quaternary @ 45%`): label `eye` "What will be sent" (`whatSentTitle`, headline); `text.alignleft` "Your chosen category and the text above." (`whatSentBasic`); when diagnostics are on: `info.circle` "The technical details listed below." (`whatSentDiagnostics`) + a monospaced caption grid: "Trayloom" `<version> (<build>)`; "macOS" `<version>` [WIN: "Windows" `<build>`]; "Mac" `<model>` [WIN: "PC" `<manufacturer model>`] (omitted if unknown); "Language" `<code>`; "Beta" ✓/○; "Update channel" (`diagnosticsChannelLabel`) one of Stable / Beta / Beta opt in / Developer; Divider; caption `hand.raised` privacy note (`privacyNote`: "No name, account, email, device identifier, logs, screenshots, files or clipboard content are included. Your public IP is temporarily processed for abuse protection and is not attached to the feedback."); caption `clock` retention note (`retentionNote`: "After delivery, the text remains in private support channels until the service owner deletes it. An undelivered copy is permanently deleted after 7 days.").
3. Footer (Divider; padding h24 v16): left error label (red caption `exclamationmark.triangle.fill`, 2 lines): `unavailableError` "Could not connect. Check your internet connection and try again." / `rateLimitError` "Too many submissions from this network. Please try again later." / `genericError` "Could not send the feedback right now."; spacer; while sending: spinner + "Sending…" (`sending`); prominent large **"Send feedback"** (`sendButton`, Ctrl+Return [WIN; macOS Cmd+Return]; enabled only when the trimmed text has >= 10 characters, <= 2000 units, and nothing is sending).
4. Sent state (replaces the form; centred, padding 40): green `checkmark.circle.fill` 54 pt, "Feedback sent" (`sentTitle`, title2 semibold), "Thank you. No contact information was sent, so you will not receive a direct reply." (`sentCaption`, secondary, max 400), prominent large "Done".
Service: HTTPS POST JSON `{kind, message, diagnostics?}` to the maintainer's feedback endpoint (Vorssaint: `https://screenshots.vorssaint.com/v1/feedback` - Trayloom needs its own endpoint or a GitHub-issue fallback), ephemeral session (no cookies), response body <= 16 KB, success when the JSON carries an `id`; HTTP 429 -> rate-limited error; network failure -> unavailable error. **Gap:** nothing in Trayloom; add the window, `FeedbackService`, General-page row and Command Bar commands.

---------------------------------------------------------------------------------------------------------------

## 14. Master key list for this file (all registered defaults)

App updates: `panelUtilityAppUpdates`=true, `appUpdatesCheckFrequency`="off", `appUpdatesIncludeHomebrewApps`=true, `appUpdatesIncludeAppStore`=true, `appUpdatesIncludeOnlineCatalog`=true, `appUpdatesNotify`=true, `appUpdatesRules`="[]", `appUpdatesLastCheck`=0.0, `appUpdatesLastCount`=0, `appUpdatesNotifiedIDs`=[].
Homebrew/Packages: `homebrewGroupDependencies`=true.
Cleaner: `cleanerScheduleFrequency`="off", `cleanerScheduleHour`=9, `cleanerScheduleMinute`=0, `cleanerScheduleWeekday`=2, `cleanerScheduleNotify`=true, `cleanerLastAutoRun`=0.0, `cleanerLastAutoFreed`=0, `cleanerLastAutoFailed`=0, `cleanerScreenshotAgeDays`=30.
WhatsApp downloads: see §4.2 (27 keys).
Uninstaller: `uninstallerCommandBarEnabled`=false.
Kill Process: `killProcessCommandBarEnabled`=true, `killProcessGroupRelated`=true, `killProcessSortBy`="cpu", `killProcessSortAscending`=false.
Media: see §8.2 (41 keys).
Updates/About: `autoCheckUpdates`=true, `includeBetaUpdates`=false, `releaseNotesOnUpdate`=true, `showReleaseNotesBeforeUpdate`=true, `updateShowcaseIntroVersion`="", `updateShowcaseMediaOverride`="" (dev only); unregistered/optional: `whatsNewPendingVersion`, `whatsNewSeenVersion`, `whatsNewKnownFeatures`, `updateHighlightsSeenVersion`, `supportUpdateIntroVersion`.
Feature availability (Features hub; `FeatureRuntime`): cleaner, uninstaller, homebrew, appUpdates, mediaTools, killProcess (beta), portManager - each independently installable; page visible only when installed.

## 15. SF Symbol -> Segoe Fluent Icons map (symbols used in this file)

`?` = verify code point; `-` = no glyph, use the stated fallback.

| SF Symbol | Use | Segoe glyph |
|---|---|---|
| arrow.clockwise | Check now, Refresh | E72C (Refresh) |
| arrow.down.app / arrow.down.circle(.fill) | App updates row, download state, Install | E896 (Download) ? |
| arrow.up.circle(.fill) | Update available / Update | E898 (Upload)? or EB11 (ArrowUp) ? |
| arrow.triangle.2.circlepath | Update Homebrew / checking | E895 (Sync) |
| exclamationmark.triangle.fill | warnings | E7BA (Warning) |
| checkmark.circle.fill | success | E73E (CheckMark) in a green circle / EC61 (CompletedSolid) ? |
| info.circle | info | E946 (Info) |
| plus | add exception | E710 (Add) |
| xmark.circle.fill | close/clear | E711 (Cancel) in circle / EA39? -> use E711 |
| ellipsis / ellipsis.circle | more menu | E712 (More) |
| chevron.right / down / up | disclosure | E76C / E70D / E70E |
| shippingbox / terminal | package, CLI | E7B8 (Package) ? / E756 (CommandPrompt) |
| trash | delete/uninstall | E74D (Delete) |
| magnifyingglass | search | E721 (Search) |
| folder / folder.fill | reveal, destination | E8B7 (Folder) / E838 (FolderOpen) |
| arrow.forward.circle | reveal destination | E8AD? (Forward) / E72A |
| doc.on.doc | Copy | E8C8 (Copy) |
| clock | schedule | E823 (Recent) / E917 (Clock) |
| camera.viewfinder | screenshots | E722 (Camera) / E7C5? |
| power / puzzlepiece / archivebox / hammer / internaldrive / iphone | cleaner groups | E7E8 (PowerButton) / EA86 (Puzzle)? / E7B8 / EC7A (DeveloperTools)? / EDA2 (HardDrive) / E8EA (CellPhone) |
| photo / film / waveform / doc.text / archivebox / doc | file categories | E91B / E714 (Video) / E8D6 (Audio) / E8A5 (Document) / E7B8 / E7C3 (Page) |
| arrow.down.doc | WhatsApp downloads tab | E896 + E8A5 -> use E896 |
| hand.raised.fill | privacy | E7B3 (Hand)? / E72E (Lock) |
| network / network.slash / globe | ports | E968 ? / E871 ? / E774 |
| bolt.fill | Force Kill | E945 (LightningBolt) |
| play.fill / gearshape.2 / crop / text.viewfinder | media actions | E768 (Play) / E713 (Settings) / E7A8 (Crop) ? / E8A1? |
| circle / circle.lefthalf.filled / circle.fill | compression levels | EA3A (CircleRing) / EA3B? / EA3B (CircleFill) - or custom drawn |
| pencil / photo | profile modified / logo | E70F (Edit) / E91B |
| heart.fill / star(.fill) / cup.and.saucer.fill | support | EB51 / E734-E735 / - (U+2615 in Segoe UI Emoji) |
| hourglass | operation active | E9D5? / E916 (Stopwatch) |
| checkmark.shield | trust card | E83D (Shield)? / EA18 |
| bubble.left.and.text.bubble.right.fill | feedback | E8F2 (ChatBubbles) |
| eye / text.alignleft / clock / hand.raised | feedback box | E7B3 / E8E4 (AlignLeft) / E823 / E7B3? |
| rectangle.topthird.inset.filled / play.rectangle / gobackward | highlights fallback / video failed / restart | E8A0? / E714 / E7A7 (Undo) |
| sparkles | Cleaner, What's New | - (U+2728 in Segoe UI Symbol) |

## 16. Cross-cutting Windows notes
* Permissions in the feature catalog (Full Disk Access, Files & Folders, App Management, Finder automation) are **[MAC-ONLY]**; the permission portal for these features lists instead: "Run as administrator (optional)" for Cleaner/Uninstaller/Packages, "Notifications" (toast setting) for Cleaner/App updates/WhatsApp, "Controlled folder access" hint for WhatsApp downloads.
* Trash -> Recycle Bin everywhere ("Move to Trash" -> "Move to Recycle Bin", "Trash" -> "Recycle Bin"); "Finder" -> "File Explorer"; "System Settings" -> "Settings"/"Windows Security"; "Mac" -> "PC"; "menu bar" -> "system tray"/"taskbar".
* Pasteboard "declareVorssaintSource" (marks copies as the app's own so clipboard history can ignore them) -> use a private clipboard format `Trayloom.Source` alongside the text when copying PID/path/summary.
* Timing summary: Kill Process 3 s refresh (active window only); Port Manager manual; App updates 10-min staleness, background daily/weekly with 180 s catch-up; Cleaner scheduled at user time with ~2 min catch-up; WhatsApp 09:00 daily with 120 s catch-up (retry 10 min when the user is reviewing); self-update hourly + 15-min stale re-check.

## 17. Gap summary / suggested build order
1. Settings shell pages (grouped-form builder + panel-card helpers) so each feature stops opening its own window and instead provides a page (App updates, Cleaner, Packages, Uninstaller, Kill Process, Port Manager, Media) - existing features keep their code as the engines.
2. About + Updates section + `UpdateService` + pre-install and What's New windows + changelog parser (§9, §10, §12).
3. Support page + Feedback window (§11, §13).
4. Cleaner state machine + groups + schedule; WhatsApp downloads tool (§3, §4).
5. App updates list/exceptions/schedule and the Packages page (§1, §2).
6. Uninstaller wizard (categories, registry leftovers with backup), Kill Process columns/sorting/grouping, Port Manager rows (§5-§7).
7. Media workspace (Video/GIF/Image/Text) with profiles/watermark/rename/PDF (§8).
Existing Trayloom reusable engines: `Winget.ParseUpgrade` and runner (AppUpdates), registry program scan + leftover finder (Uninstaller), `KillProcessCore` protect list/kill/restart (KillProcess), IP-helper port tables (PortManager), WIC image pipeline + ffmpeg wrapper (MediaTools), category scanners (Cleaner).
