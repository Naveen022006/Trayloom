# R3 integration notes (collected from R2 reports)

## App / Core fixes needed
- Hub: Install/Uninstall buttons render as huge blue blocks; Vorssaint uses small grey rounded buttons (see `features-hub.png`).
- Core small switch style: knob drawn on the left while setting is on (seen in Windows agent's `panel-controls.png`).
- `LegacyPanel` lists every FeatureAction of running features on Utilities; must skip features that contribute IPanelUtility/IPanelSection (Window layout has 41 actions meant for Command Bar/Radial only).
- Key mismatch: Window layout "Show in panel" writes `panelUtilityWindowLayout`; App reads `panelUtilityShow.windowLayout`. Unify (honour both or pick one and fix the feature).
- Controls tab: App sums `CategoryCounts` before `CreateView` runs (Windows agent worked around with Ambient.App).
- Shared app-list / app-picker control (spec 02b 0.9) should move into Core; Windows project has local `AppBundleList` + `AppPickerWindow`.
- Shortcut defaults: commandBar = Ctrl+Alt+Space (Core); windowDirectional = Ctrl+Alt+Shift+Space. Re-audit all defaults for conflicts once all projects are in.
- windowGestureModifiers default Ctrl+Win (05 spec) vs 02b recommendation Ctrl+Alt — decide.

- Core `MacSwitch` knob renders on the left in BOTH states (confirmed by Input agent too) — real bug.
- `SettingsPages` title "Mouse & Trackpad" → "Mouse & Touchpad".
- `InputHooks` gives no signal when hook install fails; add a status so Super key can show its error row.
- Shared app picker/AppBundleList exists separately in Windows AND Input projects → move to Core.
- Command Bar ↔ snippets: needs a shared "insert snippet" service (Input owns snippets); Command Bar can read `textSnippets` JSON.
- Controls tab `panelControlOrder` not honoured by Input's section.

- Agents ↔ Island (check Island agent's hosting): show `agents.notice` strip at priority 1 for 5 s; use `agents.resting` as idle content when `notchIdleContent == "agents"`; Island must call Agents' CreateView/CreateStrip so its `IIslandContext.Invalidate()` is captured; Island/Content tab must write `notchAgentsEnabled` + `notchHiddenModules`; `island#agents` anchor must resolve; island has no leading "band" slot (agents' period menu sits inside its page); mascot "celebrates" hook not wired (no mascot API).
- Agents price download disabled (no Trayloom-hosted price list) — fine, bundled list used.

- Panel visibility keys: App reads `panelUtilityShow.<id>`; Vorssaint keys are `panelUtility<Name>` (Files writes both, Windows writes only the Vorssaint one). Make the App honour the Vorssaint keys.
- `IShelfService.Add(IEnumerable<string>)` needed (Screenshot "add to shelf", Island Files page keeps its own shelf.json).
- Legacy panel adapter duplicates rows: Files keeps FeatureActions for Command Bar; adapter must skip features with real panel contributions.
- HotkeyRecorder: bare-key option + width ~150 px for Win chords ("Ctrl+Alt+Win+V" clipped at 108).
- Clipboard image "Edit" button needs Screenshot to expose an edit-clipboard-image action.
- Island clipboard page reads old `clipboard-history\history.json` mirror — switch it to Files' store via a service.

- Capture: verify `zxing.dll` (ZXing.Net 0.16.11) lands in App/publish output; Capture csproj has `AllowUnsafeBlocks` + `InternalsVisibleTo CaptureHarness` (remove the harness friend before release); support hosted-only utilities (recentCaptures hidden row shows in edit mode); IShortcutService conditional roles (active only while Shelf/sharing on).
- Recorder gaps (later): pointer track/click ring/auto-zoom/zoom lane/backdrop/text lanes/presets, audio time-stretch, editor preview audio, per-window loopback, cursor capture, capture border.

- Tools: VERIFY cross-project action ids the Command Bar / Quick panel call: screenshot `region`, recorder `toggle`, color picker `pick`, OCR `capture`, mic mute `toggle`, clipboard `open`, shelf `toggle`, keep awake `toggle`, app updates `check`, window layout `layout.<raw>`, URL cleaner `clean-clipboard`, output switcher `cycle` — compare to what each project actually exposes and fix mismatches.
- Tray host must honour `IPanelContext.SetKeepOpen` (toggles/wallpaper open dialogs).
- `IPanelSection.CategoryCounts` needs a context arg (Windows + Tools both work around it).
- Recording-state service (Capture → Quick panel recording tile, Recent captures utility).
- Cleaning Mode needs a Core API to pause hook-based Input features during lock.
- Public clipboard-history (Files) and snippets (Input) services instead of Command Bar reading their files.
- Command Bar default: Ctrl+Alt+Space (Alt+Space reserved) — keep.

- Island: NAudio 2.2.1 added (approved — same as System). Scratchpad format mismatch: Island expects flat key `scratchpadDocument` JSON `{pads:[{id,name,text,updated}],selectedPadID}`; Tools stores `Scratchpad.json` (12-tab) → add a scratchpad service or align storage. Island clipboard page read-only (needs clipboard service); island keeps its own `shelf.json` (needs shelf service); island Tools page/Controls tiles depend on FeatureActions of tools; capture controls host hook; tray-icon hide + app panel in island need Core APIs; System page lacks GPU/connected devices. `notchEnabled=false` default → island invisible until turned on (check onboarding/bundle sets it). Lyrics use lrclib online only "when allowed" — confirm default is off.
- Island host contract implemented: section `agents` as ExternalPage; strip `agents` live; `agents.resting` wing; `*.notice` strips 5 s; `Invalidate()` re-queries. Verify end-to-end with the Agents project.

- System: App tray must honour `micMuteMenuBarIndicator`, keep-awake `keepAwakeActiveIcon`/`keepAwakeIconTint`/`showCountdownInMenuBar`; add `ITrayMetricsState` to Core so `menuBarHideIconWithMetrics` works (System will publish it); Excluded drives list contributed by System as `quick-tools#diskExclusions` — make sure Tools' Quick toggles card doesn't render a second copy; mic mute: unmute when the FEATURE is uninstalled (keep persistence across app relaunch only); per-app output routing disabled on build 26300 (vtable unknown) — leave hidden; Extra brightness stays unavailable.

- Maintenance: Core contract so Command Bar can reach kill service (browse, `kill.<pid>`) and uninstall finder (public hooks exist: `UninstallerFeature.SelectAsync`, `MediaToolsFeature.OpenWith`); App verbs `--elevated-clean` / `--kill-batch` or `IShell.Restart(elevated)` (single-instance mutex vs elevated relaunch helper waiting 2 s); panel keys `panelUtility<Name>` vs `panelUtilityShow.<id>` (ids: appUpdates, cleaner, homebrew, media, uninstaller, portManager); pages Packages/Kill Process/Port Manager draw their own title → avoid duplicate heading with shell header; winget page: sidebar says "Packages", view header says "winget" — pick one; Video "Edit" hidden until recorder editor wired (Capture has one now → connect via action).
- Build has 17 warnings — review.

## Live verification still needed (agents could not run the exe)
- Windows project: switcher Alt+Tab takeover, taskbar preview/clicks via UIA, edge snap, quit on close.
- Tools project: command bar hotkey + search, quick panel, quick toggles (NOT destructive ones), radial menu triggers, cleaning mode (only with user present).
- Capture project: real overlay interaction, editor Save buttons, recorder pill/editor interactions.
- Files project: clipboard capture, cut/paste & rename hooks, shelf shake/edge/dock, tray pill position.
- Input project: quit & close protection with real keystrokes, side buttons, mouse button shortcuts, focus follows mouse.

## R3 status (integration pass)

Done: MacSwitch thumb, hub buttons, HotkeyRecorder width/bare key, "Mouse & Touchpad", legacy adapters deleted, Vorssaint
panel keys (`panelUtility<Name>` / `panelControl<Item>`, `panelUtilityShow.*` migrated), hosted-only utilities,
`GetCategoryCounts(context)`, Core services (clipboard history, shelf Add/Paths, snippets, scratchpad, recording state,
kill / uninstall / media, tray metrics, screenshot editor, `IInputHooks.PauseOthers` + hook status), tray icon keys
(keep-awake icon / tint / countdown, mic indicator, hide with metrics), action-id verification (all ids the Command Bar,
Quick panel, Radial menu and island call exist; window layout ids are `layout.<raw>`, e.g. `layout.leftHalf`), shortcut
audit (no duplicates; `windowGestureModifiers` default Ctrl+Alt), island on first install, lyrics online off, mic mute undone on
uninstall, harness friends removed, warnings fixed, no duplicate feature ids (`--dump-info`).

Not done: shared app picker / AppBundleList in Core; Command Bar "Uninstall..." entries (the service is published);
elevated relaunch verbs; recorder gaps; island mascot "celebrates" hook; live tests that need real input (Alt+Tab takeover,
taskbar thumbnails, radial ring, selection overlays, recorder, camera, cleaning mode).
