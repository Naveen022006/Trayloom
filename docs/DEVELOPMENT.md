# Developing Trayloom

Notes for working on Trayloom itself. The [README](../README.md) covers installing and using it.

## Build, run, publish

Requires the .NET 9 SDK on Windows 10 (19041) or newer.

```
dotnet build Trayloom.sln -c Debug
dotnet run --project src/Trayloom.App

dotnet publish src/Trayloom.App/Trayloom.App.csproj -c Release -r win-x64 --self-contained true -o dist/Trayloom
```

`scripts\package.ps1` publishes the self-contained build, adds `Trayloom-Setup.cmd` and writes `dist\Trayloom-win-x64.zip`.
The publish output (about 175 MB unpacked, 70 MB zipped) is a self-contained folder: feature discovery loads the
`Trayloom.Features.*.dll` assemblies next to the exe, and NAudio, ZXing.Net and the SQLite natives ship beside them, so keep
the folder together. A self-extracting `Setup.exe` built with Windows IExpress is silently blocked on a Smart App Control PC,
so `Trayloom-Setup.cmd` is the setup equivalent.

## Developer flags

A second launch hands these to the running copy.

`--open-settings <page#anchor>`, `--open-panel [tab]`, `--open-menu`, `--search <text>`, `--action <feature> <action>`,
`--dev-install <id>`, `--dev-uninstall <id>`, `--close-panel`, `--dump-info <file>` (features, actions, panel utilities,
shortcut defaults, duplicate checks), `--open-onboarding [step]`, `--open-install`, `--show-toast`,
`--simulate-problem sac|crash`, `--dev-key escape` (raises a key event inside the app), `--slow-start <ms>`,
`--shot <png> [window title | type:Class | all]` (saves a picture of one of Trayloom's own windows), `--shot-delay <ms>`,
`--shot-full <png>` (the whole Settings page), `--set-language <code|system>`, `--set-appearance <system|light|dark>`,
`--install`, `--uninstall [--quiet]`, `--no-onboarding`, `--quit`.

Feature dev actions (most only with `TRAYLOOM_DEV=1`): `--action screen-time dump|flush|dev-add-minute|today`,
`--action github refresh|open-notifications|dump|connect-cli|simulate-notice`, `--action wifi scan|state|dump` (never
`toggle-radio` or `disconnect` in automation: they cut the machine's network), and `--action away-mode
simulate-away|simulate-return|simulate-face|dump`. Away mode's `simulate-away` only touches windows of the processes listed in
`away-mode-only.txt` in its data folder (the `tests\AwayDummy` test process); `TRAYLOOM_AWAY_NO_CAMERA=1` keeps the camera off.
Closing apps (0.4.0): `--action away-mode dev-close-now|simulate-session-end|reopen`; with `away-mode-only.txt` a browser name
(`chrome`, `msedge`, ...) only matches a browser started with `--user-data-dir` under `TRAYLOOM_DATA` or the temp folder, so
the isolated browser test of spec 07 section 11.4 can never touch your own browser. `tests\AwayDummy --mode
closable|asks|refuses|slow|tray` stands in for other apps.

With `TRAYLOOM_DEV=1` the island also exposes one action per page. Island placement: `--action island move` enters
"Move island" mode and `--action island reset-position` puts the island back at the top centre; with `TRAYLOOM_DEV=1` also
`--action island dev-place-top-left`, `dev-place-top-right`, `dev-place-bottom-left`, `dev-place-bottom-center`,
`dev-place-bottom-right`, `dev-place-float`, `dev-place-next-display` (cycles "Choose a display" through the connected
displays) and `dev-place-reset`.

## Isolating experiments

* `TRAYLOOM_DATA` moves the settings and logs folder (default `%APPDATA%\Trayloom`).
* `TRAYLOOM_INSTANCE=<name>` (with `TRAYLOOM_DATA`) lets a development copy run beside the installed one; flags and `--quit` go to
  the copy with the same name.
* `TRAYLOOM_NO_INSTALL_PROMPT=1` hides the install offer; builds run from `bin\Debug` / `bin\Release` never show it.
* To test the install flow without touching your real Start menu or registry, point `TRAYLOOM_INSTALL_ROOT`,
  `TRAYLOOM_STARTMENU_DIR`, `TRAYLOOM_DESKTOP_DIR`, `TRAYLOOM_UNINSTALL_KEY` (an HKCU sub key) and `TRAYLOOM_RUN_KEY` (an HKCU
  sub key) at scratch locations.

## Upgrading from WinKnife

The first start closes a still-running WinKnife copy, moves its settings folder and local data to the Trayloom names and
re-registers Start with Windows (on/off state kept). Installing Trayloom also removes the old Start menu shortcut and Apps &
features entry. The old program folder is left in place for the user to delete.

## Known gaps

* The shared app picker / app list lives in three projects (Input, Windows, Files) instead of Core (`specs/R3_NOTES.md`).
* The screenshot editor and recorder editor windows are hidden from screen capture on purpose, so screenshot tools show them
  blank.
* `TESTING.md` lists what needs a real person to verify (Alt+Tab takeover, taskbar previews, edge snapping, real clicks in
  recordings, the permission alert, and so on).
