# Trayloom v2 - Core contracts (API reference for feature agents)

Written by the shell agent (R1). This file is the exact public API you build against. Trust the source over this
file when they disagree (`src/Trayloom.Core`), and report the mismatch. Rules that did not change from
`PHASE2_RULES.md`: edit only your own project, build only your project with `-p:BuildProjectReferences=false`, GPL header
in every file, never the Vorssaint name/logo, `Stop()` must fully undo side effects.

Everything here lives in `Trayloom.Core` (namespace `Trayloom.Core`, UI in `Trayloom.Core.Ui`). Feature projects
reference only Core. Contributions are discovered by reflection in assemblies named `Trayloom.Features.*`
(parameterless public constructor, non-abstract class, implements one or more provider interfaces).

---------------------------------------------------------------------------------------------------------------------

## 1. Catalog and ids

`FeatureCatalog.All` is the single source of truth (79 entries; 73 installable on Windows). Each entry
(`FeatureCatalogEntry`) has: `Id` (kebab, e.g. `scroll-inverter`), `RawId` (Vorssaint camelCase, e.g. `scrollInverter`),
`Group`, `Title`, `Description`, `Glyph`, `Energy`, `Permissions`, `DefaultInstalled`, `Destination` (`page#anchor`),
`EnabledKeys`, `InitialEnableKeys`, `IsBeta`, `IsExperimental`, `Support` (Supported / Adapted / Unavailable),
`UnavailableReason`, `LegacyId` (the id the v1 class used).

* **Use the catalog `Id` everywhere** (contribution `FeatureId`, `Runtime.IsAvailable(...)`). `FeatureCatalog.Resolve(x)` accepts
  a catalog id, a raw id or a v1 id and returns the catalog id.
* Your `IFeature.Id` may stay the v1 id (existing per-feature settings scope and data folder are keyed by `IFeature.Id`)
  or become the catalog id; the registry binds either (`IFeature.CatalogId`). `Name`, `Description` and `Group` of `IFeature`
  now have default implementations taken from the catalog - delete your own overrides. `InstalledByDefault` is ignored.
* A catalog entry without an `IFeature` class (e.g. `dock-preview`) can still be installed (flag only). Create a class with the
  catalog id and the registry starts it.
* Unavailable entries (`window-maximizer`, `spaces-order`, `middle-click`, `disk-image-installer`, `extra-brightness`,
  `fan-control`) are disabled in the hub and never installable; do not build them.

Install state: `featureAvailable.<RawId>` (bool) in `IAppSettings`. Never read/write it directly; use `IFeatureRuntime`.

### IFeature (unchanged lifecycle)
```csharp
public interface IFeature {
    string Id { get; }
    string Name => ...catalog title; string Description => ...catalog; FeatureGroup Group => ...catalog;
    string CatalogId => FeatureCatalog.Resolve(Id);
    void Start(IFeatureContext context);     // feature installed and app running
    void Stop();                             // uninstalled / app exit: undo every side effect
    FrameworkElement? CreateSettingsView() => null;   // LEGACY: see section 8
    IReadOnlyList<FeatureAction> Actions => [];        // LEGACY: see section 8
}
```
`IFeatureContext : IContributionServices` (section 2) plus `IFeatureSettings Settings` (old scoped store) and
`string DataDirectory`.

### Enable keys (first install)
Vorssaint features are "available" (installed) AND "enabled" (their own switch). Each catalog entry lists `EnabledKeys`
(flat `IAppSettings` keys, e.g. `scrollInverterEnabled`). On first install the runtime switches `InitialEnableKeys` on, but only
when none of the entry's keys was ever saved (`IAppSettings.IsSet`). Read your keys from `context.App` and react to
`App.Changed(key)`. v1-installed features were migrated with their first key set to true.

---------------------------------------------------------------------------------------------------------------------

## 2. Services every context offers - `IContributionServices`

`IFeatureContext`, `ISettingsContext`, `IPanelContext` and `IIslandContext` all inherit it:

```csharp
public interface IContributionServices {
    IAppSettings App { get; }          // flat app-wide key store
    IFeatureRuntime Runtime { get; }   // install state
    IShell Shell { get; }              // open Settings / panel / quit
    IShortcutService Shortcuts { get; }// shortcut roles, conflicts
    IHotkeyService Hotkeys { get; }    // raw RegisterHotKey (prefer Shortcuts)
    IInputHooks Hooks { get; }         // shared LL hooks (scoped to the feature when from IFeatureContext)
    INotifier Notifier { get; }
    ILog Log { get; }
    Dispatcher Ui { get; }
    IActionCatalog Actions { get; }    // FeatureActions of running (legacy) features
    IIslandRegistry Island { get; }
    IServiceHub Services { get; }      // publish / consume well-known services
}
```

### IAppSettings (flat key store, Vorssaint `DefaultsKey` names)
```csharp
T Get<T>(string key);                  // stored value, else registered default, else default(T)
T Get<T>(string key, T fallback);      // fallback only when neither exists
void Set<T>(string key, T value);      // JSON-serialisable; raises Changed(key) when different
bool IsSet(string key);                // ever saved (registered defaults do not count)
void Reset(string key);
void Register<T>(string key, T defaultValue);          // call in Start() / your section ctor, repeatedly is fine
void RegisterDefaults(IEnumerable<KeyValuePair<string, object?>> defaults);
IFeatureSettings Scope(string featureId);              // the v1 per-feature scoped store (still readable)
event Action<string>? Changed;                         // any thread
```
Use the exact key names and registered defaults from the specs (`specs/02a..02e`). Register your defaults before reading
(registered defaults win over view fallbacks, like Vorssaint). Values: bool, int, double, string, `Hotkey`, enums (as
strings), `List<T>`, `Dictionary<string,T>`. Shell keys already registered: `appAppearance`, `hotkeyEnabled`,
`menuBarIconSymbol`, `panelSectionOrder`, `panelShow*`, `monitorShow*`, `brightnessControlEnabled`, `settingsWindowWidth/Height`,
`launchAtLoginWanted`, `autoCheckUpdates`, ... (see `AppKeys`).

### IFeatureRuntime (read-mostly)
```csharp
bool IsAvailable(string featureId);  bool IsRunning(string featureId);
bool AnyAvailable(IEnumerable<string> featureIds);   // empty list = true
int Revision { get; }  event Action? Changed;        // after every install/uninstall (once per bulk change)
string? InstallBlockedReason(string featureId);
```
Check optional integrations with it (e.g. Port Manager shows `Kill` only when `Runtime.IsAvailable("kill-process")`).

### IShell
```csharp
void OpenSettings(string destination, string? targetFeature = null);   // "page#anchor", see SettingsPages
void OpenFeatures(string? featureId = null);                            // hub, reveals that feature's row
void OpenPanel(string tabId);   void ClosePanel();   bool IsPanelOpen { get; }
void Quit();   void Restart();
```
`SettingsPages` has the page id constants (`General`, `Mouse`, `QuickTools`, `ScreenCapture`, `Island`, ...) and `Join(page, anchor)`.

### IServiceHub (features offer services to the shell)
`Provide<T>(service)`, `Withdraw<T>(service)`, `Get<T>()`, `Changed`. Publish in `Start`, withdraw in `Stop`. Well-known
interfaces the shell consumes (implement them in your feature class or a helper): `IKeepAwakeService` (tray icon state,
tooltip, countdown, right-click menu), `IMicMuteState` (tray badge), `ICleaningModeService`, `IShelfService` (tray menu items).
Also use the hub to share state between your `IFeature` and your stateless contribution classes (they are separate instances).

### IShortcutService (central shortcuts)
Static role table = spec 02a 6.2 with Windows defaults (`ShortcutRoles`): ids like `keepAwake`, `clipboardHistory`, `screenshot`,
`commandBar`, `windowLayout.left` ... (see `Shortcuts.cs`). Storage key per role: `<id>Shortcut` (a `Hotkey`; stored `null` = cleared).
```csharp
IDisposable Bind(string roleId, Action callback);   // registers while the role is Active; re-registers on change; disposed on Stop
Hotkey? GetHotkey(string roleId);                    // configured (saved or default) - hook-based features read this
ShortcutState GetState(string roleId);               // Configured, Effective, IsActive, IsRegistered, Problem
ShortcutSetResult TrySet(string roleId, Hotkey? hotkey);  // validates modifier + conflicts ("This shortcut is already used by %@.")
void Reset(string roleId); bool IsDefault(string roleId); string? FindConflict(Hotkey, string exceptRoleId);
void DefineRole(ShortcutRole role);                  // extra roles at run time
event Action<string>? Changed;
```
A role is **Active** when all its `EnableKeys` are true (e.g. `clipboardHistoryEnabled` and `clipboardHistoryShortcutEnabled`).
Mapping: macOS (cmd) becomes Win in multi-modifier hotkeys (`HotkeyMap.Mac("⌃⌥⌘", Key.K)` = Ctrl+Alt+Win+K). If Windows reserves the
combination or `RegisterHotKey` fails, the service falls back to Shift in place of Win and reports it in `ShortcutState.Problem`
(show it under your recorder row). Hook-based roles (`HookBased`: switcher, finderRename) are only stored; your hook reads `GetHotkey`.
In a settings row use `HotkeyRecorder` and call `Shortcuts.TrySet`; show `result.Error` in orange under the row.

---------------------------------------------------------------------------------------------------------------------

## 3. Settings sections - `ISettingsSection`

A Settings page = page header + the sections of that page in `Order`. Pages: `SettingsPages.*`. Page-specific rules:

* `general`: no anchor = General cards (app owned) + your sections with `Anchor == null`. `general#X` shows ONLY sections whose `Anchor == "X"`
  (mixer, soundOutputSwitcher, audioPriority, musicBlocking, panelConfiguration is app owned). `energy` behaves the same (one tool at a time:
  `energy#keepAwake`, `brightness`, `extraBrightness`, `bluetoothSleep`).
* Every other page shows all its sections; the router scrolls to the section with the requested `Anchor` and tints it for 1.2 s.
* `island` hosts the Dynamic Island page; `quick-tools` hosts the Quick tools features; `screen-capture`, `mouse`, ... see `FeatureCatalog` destinations.

```csharp
public interface IContributionProvider {
    string? FeatureId => null;                       // catalog id; shown only while installed. null = always
    IReadOnlyList<string> FeatureIds => ...;         // several gates: shown while ANY is installed
    int Order => 0;                                  // ascending; ties keep discovery order
}
public interface ISettingsSection : IContributionProvider {
    string PageId { get; }                           // SettingsPages.*
    string? Anchor => null;
    string? Title => null;                           // used by "Search settings"
    IReadOnlyList<string> Keywords => [];            // extra search words (option labels)
    FrameworkElement CreateView(ISettingsContext context);   // called on every (re)build
}
public interface ISettingsContext : IContributionServices { string PageId; string? RequestedAnchor; void RequestRebuild(); }
```
Build the view with `SettingsCard` + `SettingsRow` (section 6); read values from `context.App`, write back immediately.

```csharp
public sealed class InvertScrollSection : ISettingsSection
{
    public string? FeatureId => "scroll-inverter";
    public string PageId => SettingsPages.Mouse;
    public string? Anchor => "scrollDirection";
    public string? Title => "Invert mouse scrolling";
    public IReadOnlyList<string> Keywords => ["vertical", "horizontal", "touchpad"];
    public int Order => 10;

    public FrameworkElement CreateView(ISettingsContext c)
    {
        c.App.Register("scrollInverterEnabled", true);
        var card = new SettingsCard("Invert mouse scrolling");
        card.Add(new SettingsRow("", "Invert vertical scrolling", "Reverses the mouse wheel direction.",
            new MacSwitch(c.App.Get<bool>("scrollInverterEnabled"), SwitchSize.Regular, on => c.App.Set("scrollInverterEnabled", on))));
        card.AddDivider();
        card.Add(new FeatureSwitchRow(c.Runtime, c.Shell, "window-layout", "", "Also flip window snapping", null,
            c.App.Get<bool>("x"), on => c.App.Set("x", on), "Install Window layout to use this."));
        return card;
    }
}
```

---------------------------------------------------------------------------------------------------------------------

## 4. Tray flyout - `IPanelSection` and `IPanelUtility`

Tabs (`PanelTabs`, canonical order): `keepAwake, brightness, mixer, system, network, disk, power, utilities, controls, toggles, wallpaper`
(`fanControl` never on Windows). `PanelTabs.All` has title, glyph, description, gating features. The host (App) owns the tab bar,
order/visibility settings (`panelSectionOrder`, `panelShow*`), the uppercase title row, the edit button, the scroll area and the
Settings / Quit footer. A tab is visible only when its gating features are installed AND at least one contribution (or legacy action) exists for it.
Panel width is 332 (content 308); height is measured, the body scrolls when taller than the cap.

```csharp
public interface IPanelSection : IContributionProvider {
    string TabId { get; }                       // PanelTabs.*
    bool SupportsEditing => false;              // true: the header shows the edit (sliders) button; react to context.IsEditing
    string? Category => null;                   // controls tab: PanelTabs.CategoryWindows | CategoryInput | CategoryFiles
    (int Enabled, int Total) CategoryCounts => (0, 0);   // controls tab: "6/6" on the category header
    FrameworkElement CreateView(IPanelContext context);
}
public interface IPanelContext : IContributionServices {
    string TabId; bool IsEditing; event Action? EditingChanged;  event Action? ResetRequested;   // edit bar: OK / Default (reset)
    bool IsActive; event Action? ActiveChanged;          // visible tab: start/stop sampling with it
    void RequestRelayout(); void ClosePanel();
    void InvokeAfterClose(Action action, int delayMs = 150);   // close, then run (focus has returned)
    void SetKeepOpen(bool keepOpen);                     // viewKeepsPopoverOpen
    void ShowDetail(string title, string glyph, FrameworkElement view); void CloseDetail();   // metric-detail mode
    void HostUtility(string utilityId);
}
```
The view is rebuilt whenever the panel rebuilds (open, tab switch, install state or `panel*` setting change, theme change). Keep sampling timers
outside the view (start on `IsActive`).

```csharp
public sealed class KeepAwakePanelSection : IPanelSection
{
    public string? FeatureId => "keep-awake";
    public string TabId => PanelTabs.KeepAwake;
    public FrameworkElement CreateView(IPanelContext c)
    {
        var card = new PanelCard();
        var stack = new StackPanel();
        stack.Children.Add(UiKit.Text("Keep awake", 12, FontWeights.SemiBold));
        stack.AddSpaced(new PanelChip("15 min", () => Activate(15), selected: false), 8);
        card.Child = stack;
        return card;
    }
}
```

**Utilities tab rows** - `IPanelUtility` (one per utility; plain action or hosted panel view):
```csharp
public interface IPanelUtility : IContributionProvider {
    string Id { get; } string Title { get; } string Glyph { get; }
    string? Caption => null;  bool CaptionStaysVisible => false;  bool NeedsAttention => false;   // caption is the tooltip otherwise
    string? ShortcutText => null;                    // chip: only the currently registered shortcut, e.g. "Ctrl+Alt+V"
    int InvokeDelayMs => 150;                        // 150 or 200
    void Invoke(IPanelContext context) { }           // plain action, run after the flyout closed
    FrameworkElement? CreateHostedView(IPanelContext context) => null;   // non-null: hosted utility (replaces the list)
    string? SettingsDestination => null;  bool ShowsSettingsGear => false;  bool HostedKeepsPanelOpen => true;
    string? AccessoryTitle => null; string? AccessoryGlyph => null; void OnAccessory(IPanelContext context) { }   // e.g. "Recent captures"
    event Action? Changed { add {} remove {} }
}
```
The host draws the row (`PanelActionRow`), the hosted header (title, optional gear, close) and wires the footer Settings button to
`SettingsDestination` while hosted. Order/visibility is host-owned (`panelUtilityOrder`, `panelUtilityShow.<Id>`).

```csharp
public sealed class ClipboardUtility : IPanelUtility
{
    public string? FeatureId => "clipboard-history";
    public string Id => "clipboard";
    public string Title => "Clipboard";
    public string Glyph => "";
    public string? Caption => "Stores copied text so you can reuse it later. Everything stays local and can be cleared anytime.";
    public string? SettingsDestination => SettingsPages.Clipboard;
    public FrameworkElement? CreateHostedView(IPanelContext c) => new ClipboardPanelView(c);   // your view, <= 308 wide
}
```
**Controls / Quick toggles tabs**: use `IPanelSection` with `TabId = PanelTabs.Controls` (+`Category`, `CategoryCounts`) or `PanelTabs.Toggles`, returning a
`PanelRowGroup` of `PanelToggleRow` / `PanelActionRow`. Honour `context.IsEditing` (eye buttons / drag) and `ResetRequested`.

---------------------------------------------------------------------------------------------------------------------

## 5. Dynamic Island - `IIslandSection`, `IIslandStrip`, `IIslandRegistry`

The Island project hosts them; other projects (Agents, Tools...) contribute. `context.Island` (an `IIslandRegistry`) returns only
contributions of installed features, ordered, with a `Changed` event (install/uninstall or provider added) to rebuild on.
```csharp
public interface IIslandRegistry { IReadOnlyList<IIslandSection> Sections {get;} IReadOnlyList<IIslandStrip> Strips {get;} event Action? Changed; }
public interface IIslandSection : IContributionProvider {          // an expanded page / module
    string Id { get; } string Title { get; } string Glyph { get; }
    FrameworkElement CreateView(IIslandContext context);
}
public interface IIslandStrip : IContributionProvider {            // compact strip item (collapsed / live activity)
    string Id { get; }
    FrameworkElement? CreateStrip(IIslandContext context);         // null = nothing to show now
}
public interface IIslandContext : IContributionServices { void Invalidate(); }   // ask the island to re-render
```
The Island feature wraps its `IFeatureContext` (which already is an `IContributionServices`) in a context:
`sealed class IslandContext(IContributionServices s) : ContributionServicesBase(s), IIslandContext { public void Invalidate() => ...; }`
```csharp
public sealed class AgentsIslandSection : IIslandSection
{
    public string? FeatureId => "notch-agents";     // shown only while the AI Agents extension is installed
    public string Id => "agents";   public string Title => "AI Agents";   public string Glyph => "";
    public int Order => 60;
    public FrameworkElement CreateView(IIslandContext c) => new AgentsPage(c);
}
```
Island extensions are catalog entries (`notch-calendar`, `notch-timer`, ...). Gate on the extension id, not on `notch`; the Island feature
itself decides how to render when `notch` is uninstalled (extensions need it).

---------------------------------------------------------------------------------------------------------------------

## 6. Visual system - `Trayloom.Core.Ui`

Never hard-code colours; use theme keys with `SetResourceReference` / `UiKit.Themed` so light/dark and the app's Appearance override work.
`ThemeKeys`: `PanelBase, CardFill, CardBorder, ControlFill, TileFill, Track, Divider, RowHover, TextPrimary, TextSecondary, TextTertiary, TabIdle,
TabSelectedFill, Accent, AccentFill12, AccentFill18, OnAccent, Green, Cyan, Mint, Yellow, Red, Orange, Pink, SettingsWindow, SettingsCard, SettingsControl`
(+ the v1 keys `Text, TextMuted, Surface, Border, ...`). Accent = Windows accent (falls back to Vorssaint blue when unreadable).
`ThemeManager.IsLight`, `ThemeManager.ThemeChanged`, `ThemeManager.BindWindow(window)` (dark title bar), `ThemeManager.SetAppearance`.
Fonts: `UiKit.TextFont` (Segoe UI Variable Text), `UiKit.IconFont` (Segoe Fluent Icons), `UiKit.DisplayFont`. Icons are glyph strings; `Glyphs.*` has common ones.

| Control | Use |
|---|---|
| `UiKit.Text(text, size, weight, brushKey, wrap)`, `UiKit.Icon(glyph, size, brushKey)`, `UiKit.IconTile(glyph)`, `UiKit.Divider(left,right)`, `UiKit.Button(text, ButtonKind, size, glyph, onClick)`, `UiKit.VStack`, `StackPanel.AddSpaced(child, spacing)` | primitives |
| `MacSwitch(isOn, SwitchSize.Regular/Small/Mini, onToggled)` | the switch (Regular 40x22 settings, Small 34x19 panel rows, Mini 28x16 option toggles) |
| `SegmentedControl(items, selected, stretch)` | segmented picker (`SelectionChanged`) |
| `SettingsCard(title?)` + `.Add(el)` `.AddDivider()` `.AddCaption(text)` | settings card (radius 16, spacing 13) |
| `SettingsRow(glyph?, title, caption?, accessory?, badge?)` | icon tile + text + trailing control; `SettingsRow.TextInset = 38` for follow-up controls |
| `SettingsPageHeader(title, subtitle?)`, `ChevronRow`, `SliderRow`, `PickerRow<T>`, `FeatureSwitchRow` | settings rows |
| `BadgePill(text, BadgeStyle.Accent/Orange/Neutral/Green)`, `ShortcutChip(text)`, `InfoPopoverButton(contentFactory)`, `PanelChip` | small pieces |
| `PanelCard`, `PanelRowGroup.Add(row)`, `PanelActionRow`, `PanelToggleRow`, `PanelButtons.Mini`, `SectionTitleRow` | tray panel |
| `UsageBar(value 0..1, fillKey)`, `Sparkline(lineKey, height).SetValues(values, ceiling)` | graphs |
| `HotkeyRecorder` | shortcut field (width 108-150; themed; `Hotkey` / `HotkeyChanged`) |
| `BrandMark.Create(width)` / `BrandMark.Render(...)` | the Trayloom mark |
| `SettingsViewBuilder` (v1 builder) still works; each `Add*` is now one `SettingsCard` with a `SettingsRow` | legacy settings views |

The old single-row card was renamed `LegacySettingsCard` (3 usages updated). Typography (DIP): row title 13, caption 11.5, panel row title 12.5,
panel caption 10.5, section title 10.5 semibold uppercase.

---------------------------------------------------------------------------------------------------------------------

## 7. Windows, sizing and DPI (shared rules)

* Never `SetWindowPos` a WPF window with placeholder sizes before `Show()`. To put a window on another monitor use `SWP_NOSIZE`
  first (so WPF picks up that monitor's DPI), then read `VisualTreeHelper.GetDpi` and size/position with real values.
* Moving windows of other processes: enter their DPI context first (`WinApiDpi.EnterWindowDpiContext(hwnd)` in the Windows project) so
  work area, frame bounds and `SetWindowPos` share one coordinate space.
* Floating windows: `OverlayWindow` (no-activate topmost tool window) or a plain `Window` + `ThemeManager.BindWindow`.

---------------------------------------------------------------------------------------------------------------------

## 8. Temporary adapters (delete the v1 pieces when you replace them)

* **Settings:** every `IFeature` with `CreateSettingsView() != null` is shown by `LegacySettingsSection` at its catalog destination
  (`page#anchor`, order 10 000, after real sections). When you contribute real `ISettingsSection`s, remove `CreateSettingsView`.
* **Panel:** `FeatureAction`s of running v1 features appear as plain rows on the tab their feature belongs to (`LegacyPanel` in the App).
  When you contribute `IPanelSection` / `IPanelUtility`, remove the matching `Actions`.
  (`IActionCatalog.All` stays available to the Command Bar.)
* The v1 `FeaturesWindow` and `QuickPanelWindow` are gone. The "Quick panel" FEATURE (`quick-launcher`) has no class yet (Tools agent builds it).

## 9. Misc facts

* Settings file: `%APPDATA%\Trayloom\settings.json` (`TRAYLOOM_DATA` overrides the folder). Flat keys live in scope `_app`; v1 scopes are unchanged.
* First run (`hasOnboarded` false, no step): Essentials bundle only, then onboarding. Existing v1 installs are migrated once (`settingsVersion` = 2):
  the old `installed` list becomes `featureAvailable.*`, `hasOnboarded` = true.
* Dev flags for screenshots: `Trayloom.exe --open-settings <page#anchor>`, `--open-panel [tab]`, `--quit`, `--relaunch <pid>`.
* Tray Quit / restart always stops every feature first (`FeatureRegistry.StopAll`).
