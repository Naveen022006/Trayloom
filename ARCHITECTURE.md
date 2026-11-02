# WinKnife — architecture spec

WinKnife is a Windows port of Vorssaint (https://vorssaint.com, GPL-3.0-or-later,
source cloned at `E:\REA TEST 1\vorssaint-mac`). The Vorssaint name, logo and
icon must NOT be used (see `vorssaint-mac/TRADEMARKS.md`). This port is licensed
GPL-3.0-or-later; every source file starts with:

```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)
```

Behaviour reference: the Swift sources under
`vorssaint-mac/Sources/Vorssaint/Services/<Feature>` and `.../UI/<Feature>`.
Read them for intent and edge cases, then implement with Win32/WinRT
equivalents. Do not translate macOS-only concepts literally.

## Stack

- .NET 9 SDK (installed), C# latest, WPF + WinForms interop (NotifyIcon).
- Target framework `net9.0-windows10.0.19041.0` so WinRT APIs are available
  (Windows.Media.Control, Windows.Media.Ocr, Windows.Graphics.Capture, etc.).
- Allowed NuGet packages: `NAudio` (Core Audio / per-app volume),
  `CommunityToolkit.Mvvm` (optional). Ask before adding others.
- P/Invoke for Win32 (user32, kernel32, dwmapi, shell32, iphlpapi, dxva2).
  Put shared interop in `WinKnife.Core/Native`, feature-specific interop
  inside the feature project.

## Solution layout

```
winknife/
  WinKnife.sln
  Directory.Build.props          shared TFM / WPF / nullable settings
  src/WinKnife.Core/             contracts, settings, hooks, hotkeys, interop, shared UI
  src/WinKnife.App/              WPF exe: tray, Features hub, quick panel, command bar host
  src/WinKnife.Features.Input/   Mouse and keyboard
  src/WinKnife.Features.Windows/ Windows + Clipboard and files
  src/WinKnife.Features.System/  Sound, Energy and display, System monitor
  src/WinKnife.Features.Tools/   Tools
  src/WinKnife.Features.Island/  Dynamic Island
```

`WinKnife.App` references every feature project. Feature projects reference
only `WinKnife.Core`. Feature projects never reference each other.

## Core contracts (WinKnife.Core)

```csharp
public enum FeatureGroup { WindowsTaskbar, MouseKeyboard, ClipboardFiles, Sound, EnergyDisplay, Tools, Island, Monitor }

public interface IFeature
{
    string Id { get; }                 // stable kebab-case, persisted; never rename
    string Name { get; }
    string Description { get; }        // one sentence, like the Vorssaint site
    FeatureGroup Group { get; }
    bool InstalledByDefault => false;
    void Start(IFeatureContext context); // feature installed (and app running)
    void Stop();                         // uninstalled or app exiting; must fully undo side effects
    FrameworkElement? CreateSettingsView() => null;
    IReadOnlyList<FeatureAction> Actions => Array.Empty<FeatureAction>();
}

public sealed record FeatureAction(string Id, string Title, Action Invoke, string? Glyph = null);

public interface IFeatureContext
{
    IFeatureSettings Settings { get; }  // scoped to this feature id
    IInputHooks Hooks { get; }
    IHotkeyService Hotkeys { get; }
    INotifier Notifier { get; }        // tray balloon / toast
    Dispatcher Ui { get; }
    ILog Log { get; }
    string DataDirectory { get; }      // %APPDATA%\WinKnife\<feature-id>\ (created on demand)
    IActionCatalog Actions { get; }    // all FeatureActions of currently installed features
}

public interface IActionCatalog
{
    IReadOnlyList<(IFeature Feature, FeatureAction Action)> All { get; }
    event Action? Changed;
}
```

- `IFeatureSettings`: `T Get<T>(string key, T fallback)`, `void Set<T>(string key, T value)`,
  `event Action<string>? Changed`. Backed by one JSON file
  `%APPDATA%\WinKnife\settings.json` (debounced atomic save).
- `IInputHooks`: one shared WH_KEYBOARD_LL + WH_MOUSE_LL pair on a dedicated
  thread with its own message loop. Subscribers:
  `IDisposable SubscribeKeyboard(Func<KeyboardHookEvent, bool> handler, int priority = 0)`
  and `SubscribeMouse(Func<MouseHookEvent, bool> handler, int priority = 0)`; return
  `true` to swallow. Events expose message, vkCode/scanCode/flags, point, wheel delta,
  xbutton, timestamp, `IsInjected`, modifier state. Handlers run ON THE HOOK THREAD:
  they must return in well under 1 ms and marshal UI work to the dispatcher.
  `InputSender` helper wraps SendInput and tags injected input with
  `WinKnifeExtraInfo` (dwExtraInfo marker) so features can ignore their own output.
  Hooks are installed lazily (only while at least one subscriber exists).
- `IHotkeyService`: `IDisposable Register(Hotkey hotkey, Action callback)` using
  RegisterHotKey on a hidden message window. `Hotkey` record (modifiers + key),
  parse/format like `Ctrl+Alt+K`. Includes a reusable WPF `HotkeyRecorder` control.
- `FeatureRegistry`: discovers all non-abstract `IFeature` types with a
  parameterless ctor in loaded `WinKnife.Features.*` assemblies; tracks installed
  set in settings (`installed` list); starts/stops features on install/uninstall;
  exceptions in one feature are logged and never crash the app.
- `ILog`: file log at `%APPDATA%\WinKnife\logs\winknife.log` (rolling, small).
- Shared UI in Core: theme resources (light/dark following Windows app theme),
  `OverlayWindow` base (borderless, topmost, no-activate, toolwindow, per-monitor DPI aware),
  `SettingsCard` helpers.

## App (WinKnife.App)

- Single instance (named mutex); per-monitor-v2 DPI manifest; `asInvoker`.
- Tray icon (NotifyIcon) with menu: Quick panel, Features…, Settings, Start with Windows, Quit.
- **Features hub**: grouped list of every feature with Install/Uninstall toggle,
  description, and its settings view (if any) when selected. Search box.
- **Quick panel**: small flyout near the tray showing all `FeatureAction`s of
  installed features as tiles.
- Start with Windows: HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
- On exit: Stop() every feature (restores mouse acceleration etc.).

## Building

- Phase 1 builds everything: `dotnet build winknife\WinKnife.sln`.
- Phase 2 agents run in parallel: build ONLY your own project and never the
  solution or App: `dotnet build src\WinKnife.Features.X\WinKnife.Features.X.csproj -p:BuildProjectReferences=false`.
- Phase 2 agents must not edit Core, App, the .sln or other feature projects.
  If something is missing from Core, implement a local helper in your project and
  mention it in your report.
- Zero warnings-as-errors policy is not required, but builds must succeed.

## Feature mapping (Vorssaint → WinKnife)

Skip/adapt macOS-only items as noted. `Id` in brackets.

**Input project — Mouse and keyboard**
invert mouse scrolling [invert-scroll], scroll sideways while holding a key [horizontal-scroll],
focus follows mouse [focus-follows-mouse], smooth scrolling [smooth-scroll], linear scrolling [linear-scroll],
disable mouse acceleration [mouse-acceleration] (SPI_GETMOUSE/SETMOUSE, restore on stop),
side buttons back/forward [side-buttons] (Windows already does this in most apps — implement as remappable),
mouse button shortcuts [mouse-button-shortcuts], extra click filter [click-debounce],
keyboard debounce [key-debounce], text snippets [text-snippets],
super key [super-key], quit & close protection [close-protection] (hold Alt+F4 / Ctrl+W / Ctrl+Q to confirm).
Skip: trackpad middle click (Windows precision touchpad settings cover it).

**Windows project — Windows + Clipboard and files**
app switcher with previews [app-switcher] (DWM thumbnails, alt-tab style on its own hotkey),
window layout [window-layout] (shortcuts for halves/thirds/quarters/maximize/center + edge snapping on drag),
taskbar preview tweaks: skip (native), quit on close: skip (not a Windows concept),
clipboard history [clipboard-history] (local, text+images, searchable popup, hotkey),
paste as plain text [paste-plain], clean URL [clean-url] (strip utm_*, fbclid, gclid, etc. on copy),
rename shortcut: skip (F2 native), cut & paste: skip (native), shelf [shelf] (drop-target floating window holding files, drag back out),
disk image installer: skip.

**System project — Sound, Energy and display, System monitor**
volume mixer [volume-mixer] (per-app volume via Core Audio sessions, NAudio),
output switcher [output-switcher] (hotkey cycles default render device via IPolicyConfig),
audio device priority [audio-priority], mute microphone [mic-mute] (hotkey + tray indicator),
keep awake [keep-awake] (SetThreadExecutionState, timed options),
displays [displays] (brightness: WMI for internal, DDC/CI via dxva2 for external),
bluetooth on sleep: optional/skip, extra brightness: skip, music blocker: skip.
System monitor: CPU [monitor-cpu], GPU [monitor-gpu] (PDH GPU Engine counters), memory [monitor-memory],
network [monitor-network], disks [monitor-disk], power/battery [monitor-power],
connected USB devices [connected-devices]. Show as a compact monitor flyout plus optional
tray-text/tooltip. Fan control: skip (no standard Windows API).

**Tools project — Tools**
quick toggles [quick-toggles] (dark mode, empty Recycle Bin, hide desktop icons, show hidden files),
color picker [color-picker], copy text from screen [screen-ocr] (Windows.Media.Ocr + region selection; QR optional),
cleaning mode [cleaning-mode] (fullscreen black overlay, block keyboard/mouse via hooks until hold-to-exit),
media compress [media-tools] (images via WIC/System.Drawing; video only if ffmpeg found on PATH),
cleaner [cleaner] (temp folders, browser caches with confirmation), uninstaller [uninstaller]
(list from Uninstall registry keys, run UninstallString, find leftovers), app updates [app-updates]
(winget upgrade list/upgrade, replaces Homebrew), screenshot [screenshot] (region/window/screen + simple annotate + copy/save),
camera preview [camera-preview] (Windows.Media.Capture floating mirror), radial menu [radial-menu],
scratchpad [scratchpad], command bar [command-bar] (searches every installed feature's FeatureAction via
`IFeatureContext.Actions` — see Core contracts), kill process [kill-process], port manager [port-manager]
(GetExtendedTcpTable/UdpTable), wallpaper [wallpaper], screen recording: phase 3 stretch goal.

**Island project — Dynamic Island**
island [island] (top-center pill overlay on primary monitor; expands on hover/click),
now playing via `GlobalSystemMediaTransportControlsSessionManager` with play/pause/next/prev + artwork,
timer/stopwatch [island-timer], calendar month view [island-calendar] (no account sync),
downloads watcher [island-downloads] (FileSystemWatcher on a chosen folder),
notifications [island-notifications]: skip unless UserNotificationListener works unpackaged,
lyrics/AI agents/watch/companion: phase 3 stretch goals.
