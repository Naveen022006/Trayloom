# R2 rules — feature parity agents (8 run in parallel)

Goal: your features must look and behave exactly like Vorssaint, per the specs. Read:
`ARCHITECTURE_V2.md` (binding decisions), `specs/SPEC_RULES.md`, `specs/CONTRACTS.md` (the Core API
you build against — trust the code in `src/WinKnife.Core` if they differ), then your assigned spec
sections. Reference images live in `E:\REA TEST 1\vorssaint-mac\docs\assets\readme\` and
`E:\REA TEST 1\vorssaint-mac\Resources\Images|Gifs`. The Swift source is at
`E:\REA TEST 1\vorssaint-mac\Sources\Vorssaint` if a spec detail is unclear — the Swift wins.

## Hard rules
- Edit ONLY your own project folder(s) under `src/`. Never edit Core, App, the .sln,
  Directory.Build.*, specs, or another project. 7 other agents work at the same time.
  If you need something from Core/App, work around it locally and list it in your report.
- Build ONLY your project:
  `dotnet build "E:\REA TEST 1\winknife\src\<Project>\<Project>.csproj" -p:BuildProjectReferences=false`
  (retry after a few seconds on a transient file lock). Never build the solution or the App.
- Do NOT run `git` commands that change state (no add/commit/stash/checkout). The lead commits.
- Do NOT start `WinKnife.exe` (single global instance; the user's copy is running and other agents
  share the machine). To see your UI, write a throwaway WPF harness in your own scratch folder
  `C:\Users\ADMIN\AppData\Local\Temp\claude\E--REA-TEST-1\7b91bf88-d61e-42ce-a3bd-2a6a3cdaf08c\scratchpad\r2-<project>\`
  that references the built `WinKnife.Core.dll` and your project's dll by file path (not by
  ProjectReference), hosts your sections/windows, and screenshots them. Compare with the reference
  images and iterate. Save your final screenshots to `E:\REA TEST 1\winknife\screenshots\r2\<project>\`.
- Never change system state while testing beyond what you restore immediately (audio devices,
  brightness, mouse settings, dark mode, wallpaper, files). Never delete user files, uninstall apps,
  kill processes, run winget upgrades, or consume Codex reset credits.
- Replace the v1 temporary adapters for your features: provide real `ISettingsSection`,
  `IPanelSection` / `IPanelUtility` / island contributions as CONTRACTS.md describes, bind to the
  catalog ids, use flat `IAppSettings` keys with the Vorssaint names/defaults, `Shortcuts` for hotkeys,
  and the shared `WinKnife.Core.Ui` controls and tokens (don't restyle locally). Remove the v1
  `CreateSettingsView()` once the replacement exists. Keep `FeatureAction`s that the Command Bar /
  Radial menu / Quick panel should list (the Command Bar spec lists them).
- Implement EVERY catalog feature in your scope, including ones WinKnife never had, using the spec's
  Windows mapping. Catalog entries marked unavailable on Windows stay unavailable.
- Exact English strings from the specs. GPL header on every new file. No Vorssaint branding.
- Packages: only those allowed in ARCHITECTURE_V2.md for your project; ask (report) for others.

## Report (concise)
Per catalog feature: done / partial (what's missing) / not possible (why). Contributions added
(settings pages+anchors, panel tabs, utilities, island sections). Screenshots path and how close they
are to the references. Anything Core/App must change.
