# Phase 2 rules (all feature agents)

Read first: `ARCHITECTURE.md` (feature list for your project), then the Core source
under `src/WinKnife.Core` (it is the exact API — trust it over any doc), and the two
sample features: `src/WinKnife.Features.Input/InvertScrollFeature.cs`,
`src/WinKnife.Features.System/KeepAwakeFeature.cs`.

Hard rules
- Edit ONLY files inside your own project folder `src/WinKnife.Features.<X>/`.
  Never touch Core, App, the .sln, Directory.Build.*, or another feature project.
  Four other agents are working in the other feature projects at the same time.
- Build ONLY your project:
  `dotnet build "E:\REA TEST 1\winknife\src\WinKnife.Features.<X>\WinKnife.Features.<X>.csproj" -p:BuildProjectReferences=false`
  If you hit a transient file-lock / restore error caused by a parallel build, wait a few
  seconds and retry. Never build the solution or the App.
- Root namespaces: Input → `WinKnife.Features.Input`, System → `WinKnife.Features.SystemTools`,
  Windows → `WinKnife.Features.WindowTools`, Tools → `WinKnife.Features.Tools`,
  Island → `WinKnife.Features.Island`. (Avoid shadowing `System.*` / `Windows.*`.)
- Delete `Placeholder.cs` once you add real code.
- Every .cs file starts with the GPL header from ARCHITECTURE.md. Never use the
  Vorssaint name, logo or icon in UI strings or assets.
- Each feature: public parameterless ctor, stable `Id` from ARCHITECTURE.md, a one-sentence
  Description, `Stop()` fully undoes side effects (restore system settings, close windows,
  dispose timers/watchers). Use `SettingsViewBuilder` for settings and `ThemeKeys` resources
  so light/dark works. Expose useful `FeatureAction`s (they appear in the Quick panel and
  Command bar) with Segoe Fluent Icons glyphs.
- Hook handlers run on the hook thread: keep them tiny, no allocation-heavy work, marshal UI
  to `context.Ui`. Ignore `IsFromWinKnife` events to avoid feedback loops.
- Prefer reliability over breadth: a feature that works well beats three half-done ones.
  If a feature proves infeasible on Windows, skip it and say why in your report.
- Smart App Control is enforcing on this machine; do not try to bypass it.
- No NuGet packages except NAudio (System project only) unless unavoidable — then explain.

Reference behaviour: `E:\REA TEST 1\vorssaint-mac\Sources\Vorssaint\Services\<Feature>` and
`...\UI\<Feature>` (Swift). Read for intent/edge cases; implement the Windows way.

Report back (concise): features implemented (id + one line each), features skipped and why,
anything the App/Core would need to change for your features to work better, final build
result of your project.
