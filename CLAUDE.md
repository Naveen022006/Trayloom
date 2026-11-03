# CLAUDE.md — Trayloom

Trayloom (formerly WinKnife) is a .NET 9 WPF tray utility for Windows 10 19041+, a Windows port of the GPL macOS app
Vorssaint. Public GitHub repo `Naveen022006/Trayloom`, branch `main`. Before larger work read `README.md`,
`ARCHITECTURE_V2.md` and `specs/` (source of truth for screens, strings, defaults and behaviour; `02-settings.md` wins on
structure, a feature's own spec on behaviour).

## Build, run, package
- Build: `dotnet build Trayloom.sln -c Debug` (or `-c Release`). Run: `dotnet run --project src/Trayloom.App`.
- One project only: `dotnet build src\Trayloom.Features.<X>\Trayloom.Features.<X>.csproj -p:BuildProjectReferences=false`.
- Package: `powershell -ExecutionPolicy Bypass -File scripts\package.ps1` → `dist\Trayloom\` (self-contained win-x64),
  `dist\Trayloom-win-x64.zip`, `dist\Trayloom-Setup.cmd`. `dist/` is git-ignored. Builds are unsigned.
- Trayloom is single-instance (`Local\Trayloom.SingleInstance`) and a running copy locks its DLLs. The user's copy runs from
  `%LOCALAPPDATA%\Programs\Trayloom`. `Trayloom.exe --quit` quits it gracefully; wait for the process to exit before
  copying a new build over it, then start the installed exe again.
- Isolate experiments: `TRAYLOOM_DATA` (settings + logs root), `TRAYLOOM_NO_INSTALL_PROMPT=1`; install tests also
  `TRAYLOOM_INSTALL_ROOT`, `TRAYLOOM_STARTMENU_DIR`, `TRAYLOOM_DESKTOP_DIR`, `TRAYLOOM_UNINSTALL_KEY`, `TRAYLOOM_RUN_KEY`.
- Dev flags (README "Build, run, publish"): `--open-settings <page#anchor>`, `--open-panel [tab]`, `--action <feature> <action>`,
  `--dump-info <file>`, `--shot <png> [title]`, `--dev-key escape` (in-process key event), `--set-language`, … A second
  launch hands them to the running copy. `TRAYLOOM_DEV=1` adds one island action per page.
- `Trayloom.exe --window-guardian*` runs the small window guardian (no WPF); see "Foreign windows" below.
- No automated test project in the solution; small console harnesses live under `tests/`. `TESTING.md` lists what only a
  person can test. Verify with a build, the log, dev flags and UI Automation of Trayloom's own windows.

## Layout
- `src/Trayloom.Core` — contracts (`Contracts.cs`, `Contributions.cs`), flat settings (`AppSettings.cs`, `SettingsStore.cs`),
  hooks/hotkeys, `Catalog/FeatureCatalog.cs` (all feature ids), `Localization/Loc.cs`, `Native/`, shared UI
  (`Trayloom.Core.Ui`), `LegacyNames.cs`.
- `src/Trayloom.App` (assembly `Trayloom.exe`) — `App.xaml.cs` (startup, single instance, dev flags, exit), `Shell/`
  (tray, settings window, router, install/uninstall, legacy migration), `Pages/`.
- `src/Trayloom.Features.*` — Input, Windows (namespace `Trayloom.Features.WindowTools`), System (`…SystemTools`), Tools,
  Island (Dynamic Island + Apple Music driver), Capture, Agents, Files, Maintenance. Feature projects reference only Core,
  never each other; the App references all of them and lists them in `App.FeatureAssemblies` (add new ones there).

## How features plug in
- `IFeature` (public parameterless ctor, stable kebab-case `Id`, bound to a catalog id) is discovered by reflection in the
  loaded `Trayloom.Features.*` assemblies. `Start(context)` / `Stop()`; `Stop()` must fully undo side effects.
- UI comes in by contribution, also discovered by reflection: `ISettingsSection` (page + anchor + order), `IPanelSection`,
  `IPanelUtility`, `IIslandSection` (island pages), `IIslandStrip` (compact strip items).
- Settings: one flat `IAppSettings` store with Vorssaint `DefaultsKey` names (island keys in `Island/Prefs.cs`, defaults
  registered at start) in `%APPDATA%\Trayloom\settings.json`. Saves are debounced (400 ms): never rely on them for state
  that must survive a crash or kill.
- Every user-visible string goes through `Loc.T("English")` / `Loc.F("…{0}", x)`; English verbatim from the specs. Never use
  the Vorssaint name, logo or icon in UI strings or assets.
- Hook handlers run on the hook thread: keep them tiny and marshal UI work to `context.Ui`.

## Code style
- Every .cs file starts with exactly:
  `// SPDX-License-Identifier: GPL-3.0-or-later`
  `// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)`
- File-scoped namespaces, 4-space indent, Allman braces, braces always, `_camelCase` fields, nullable on.
- XML `<summary>` on types and most members; comments are full sentences that say why. Match the density around you.
- Files are UTF-8 without BOM; the repo stores LF, the working tree is CRLF (`core.autocrlf=true`). Edit with editor tools,
  not PowerShell 5.1 `Set-Content`/`Out-File` (ANSI or BOM; breaks the "—" in the header). Commit messages too: no BOM.
- Reusing a cached WPF element in a rebuilt layout: `Detach.From(element)` first. No `_ = FooAsync()` without a top-level
  try/catch (use the logging fire-and-forget helper); no `Dispatcher.Invoke` from async code already on the UI thread.

## Foreign windows (Apple Music)
- Cross-process window calls must be bounded (`AmNative.Bounded`); never hold a lock across one, never block the UI thread.
- Any foreign window Trayloom hides, makes transparent, parks off-screen or moves must be tracked in the window guardian
  state (`WindowGuardianState`) so it is always given back — on quit, crash, Task Manager kill, or when the island is off.
  Clear a record only after the window is verified visible again.
- Never leave a window off-screen: sanitise placement against the monitor work area (`WindowPlacementMath`).

## Logs and debugging
- `%APPDATA%\Trayloom\logs\trayloom.log` (or `$TRAYLOOM_DATA\logs`); the guardian writes `window-guardian.log` beside it.
  Island lines are prefixed `[island]`, Apple Music driver lines `AppleMusic [ ms]`, one `result:` line per request. Read
  the log first. A normal quit logs "Stopped feature …"; a log that just ends means the process was killed.

## Working with the user
- Use subagents: Opus for analysis, diagnosis and design; Sonnet for writing code. Give each a precise brief.
- The user tests music playback manually; describe what to try rather than playing music yourself.

## Standing rules (always)
- No Claude/AI attribution in commits or PRs (no Co-Authored-By, no "Generated with" lines). The repo is public.
  Commit or push only when asked.
- Never synthesize keyboard or mouse input (SendInput, keybd_event, mouse_event, SendKeys) in tests, scripts or new code;
  a stuck Alt once broke the user's keyboard. Existing by-design injectors use Core's `InputSender`; don't add new ones.
  Use UI Automation patterns or `--dev-key` instead.
- Never change Smart App Control, Defender or other Windows security settings, and don't work around SAC blocks — report them.
- Never open `~/.claude/.credentials.json`, `~/.codex/auth.json` or OpenCode `auth.json`. Never consume a Codex reset credit
  except from an explicit user click in the UI.
- Apple Music: never modify the user's library, playlists or account. Don't play, pause, start or close Apple Music in
  automated tests unless the user asks; test window handling against a dummy window owned by a test process.
- Keep screenshots (`screenshots/` is ignored), logs, settings.json and other personal data out of git, issues and PRs.
- Old WinKnife names live only in `src/Trayloom.Core/LegacyNames.cs` (plus the migration code); new code uses Trayloom names.
- Any system state changed while testing (brightness, volume, mute, default device, mouse settings, hooks) must be put back.
- No new NuGet packages without asking (in use: NAudio, ZXing.Net, Microsoft.Data.Sqlite).
