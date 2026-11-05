# Trayloom: design for Bluetooth, GitHub while coding, the Claude mascot and Google Calendar

All file:line references are to `E:\REA TEST 1\trayloom` at 0.5.0 (commit `0962f4a`).

**I checked these on this PC, read-only:**
- **Bluetooth battery location.**
  - The battery level is not on the association endpoint (AEP).
  - The Galaxy Buds AEP showed `IsConnected=True` with `{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2` empty.
  - The value is on the PnP child node `BTHENUM\{0000111E-…}` ("… Hands-Free AG"), value 75.
  - BLE devices carry it on the `BTHLE\DEV_…` node (CMF Watch = 100).
- **One query finds every battery-bearing node.** The AQS `System.Devices.DeviceInstanceId:~<"BTH"` with `DeviceInformationKind.Device` returned all 54 Bluetooth nodes, with battery, in 31 ms.
- **Custom property keys can't be used in AQS filters.** `{fmtid} pid:<>[]` failed with "parameter is incorrect".
- **Disconnected devices keep a stale value.** LM-BALLOT was not connected but still showed 10.
- **Container ID format.** The AEP `System.Devices.Aep.ContainerId` is a lowercase GUID without braces. The Device-kind `System.Devices.ContainerId` holds the same GUID, so a device can be merged by container. The classic and LE AEPs of the CMF Watch share one container.
- **`git` 2.55 is installed.**
- **Mascot image.** The photo is 121×73 px with a 6-px pixel unit. Body is `#D77757`, eyes are `#000000`, background is `#0C0C0C`.

---

## PART 1 — Bluetooth battery, island access and tray

### What already exists
**1. `AccessoryService` (`src/Trayloom.Features.Island/AccessoryService.cs`):**
- It watches the paired classic and LE AEPs (`:72-81`) and asks for `BatteryProp` on the AEP (`:15`, `:74`).
- It posts a "Connected" notice (`:220-227`) and a one-off "Low battery · {0}%" notice at ≤20 % with re-arm at ≥25 % (`:16-17`, `:231-252`).
- It reads again every 5 min (`:89`).
- For LE devices only, it falls back to GATT 0x180F (`:278-299`).
- It is gated on `notch-accessories` + `notchAccessoriesEnabled` + `monitor-power` (`:48`).

**Bug:** reading the battery from the AEP (`:147`, `:185`, `:271-275`) never finds a value for classic headphones (verified above). So AirPods and Buds never show a battery today; only LE devices that answer GATT do.

**2. `PeripheralReader` (`src/Trayloom.Features.System/Monitor/StorageReader.cs:126-172`):**
- It enumerates every PnP device with an empty AQS and a 20 s wait.
- It filters on `System.Devices.Connected`, which the HFP node does not carry, so stale values (LM-BALLOT 10 %) leak through.
- It is used by the monitor's "Peripheral battery" block (`SystemSampler.cs:262-267`, `PowerPanel.cs:252-285`).

**3. Other existing pieces:**
- Catalog: `notch-accessories` (`FeatureCatalog.cs:143`) and `bluetooth-sleep` (`:110`).
- Radio use: `BluetoothSleepFeature.FindBluetoothAsync` (`Display/DisplaysFeature.cs:187-197`).
- Glyphs: `G.Bluetooth` = `\uE702` (`Island/Glyphs.cs:91`) and `Gl.Bluetooth` (`Shared/UiBits.cs:66`).
- Mixer: only the text "wired or Bluetooth headphones" (`Pages.Mixer.cs:488`). `AudioDevices.cs` and `OutputAndPriorityFeatures.cs:387-392` treat Bluetooth only as endpoint names ("Hands-Free").

**4. The Wi-Fi pattern to copy:**
- Feature, keys and `OpenUri`: `Wifi/WifiFeature.cs:11-67`.
- Island page: `WifiIslandSection` (`Wifi/WifiIsland.cs:18-38`), with the switch in the top row through `IIslandTopRowHost` (`:65`, `:117-129`).
- Tray: `WifiPanelSection` on the Network tab, Order 900 (`WifiPanelSection.cs:13-30`).
- Settings: Settings › Dynamic Island, anchor `wifi`, Order 190 (`WifiSettings.cs:11-23`).
- Island catalog rows: `IslandModel.cs:42`, `:74`, `:85`.
- Controls tile: `Pages.Controls.cs:325`, `IslandWindow.Input.cs:1353`, `:1508-1510`.
- Look helpers: `WifiLook`/`WifiSwitch` (`WifiLook.cs`).

**5. How the island hosts contributed strips:**
- Any `IIslandStrip` whose id does not end in `.notice/.resting/.attention` and returns an element becomes a live activity, ranked after keep-awake (`IslandCore.cs:510-521`).
- `<owner>.notice` strips become one 5 s notice each, at least 7 s apart, and open module `<owner>` when it exists (`IslandCore.cs:605-651`).
- The host is at most 220 DIP wide (`ClosedViews.cs:659-677`).
- A click on an external activity opens `ModuleForActivity(act)`, which returns `act` unchanged (`IslandWindow.State.cs:392-403`).

### The gap
- No correct battery for classic devices.
- No page or list of devices, no radio switch, no tray entry, no persistent low-battery strip.
- No per-device choice, and the threshold is fixed at 20.

### What Windows can and cannot do
| Need | Possible? | How |
|---|---|---|
| Paired devices and live connected state | Yes | `DeviceWatcher` on AEP selectors (`BluetoothDevice/BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)`), property `System.Devices.Aep.IsConnected` |
| Battery level | Yes, where the device reports one | PnP node property `{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2` (byte 0–100). Classic: the HFP "Hands-Free AG" node. LE: the `BTHLE` node, with GATT 0x180F/0x2A19 as fallback |
| AirPods left / right / case | **No** (officially) | Windows reports one HFP level, in coarse steps (10–20 %). Per-bud data only exists in Apple's proprietary BLE advertisements (manufacturer 0x004C); decoding them is reverse-engineered and unreliable, so it is out of scope |
| Radio on/off | Yes, unless refused | `Windows.Devices.Radios.Radio.RequestAccessAsync` / `GetRadiosAsync` / `SetStateAsync`, plus the `StateChanged` event. `RadioState.Disabled` = hardware switch or airplane mode. Can be refused (`DeniedByUser`/`DeniedBySystem`) |
| Connect / disconnect a paired device | **No public API** | Pairing APIs pair or unpair only. Audio devices have a semi-documented KS property, `KSPROPSETID_BtAudio` with `KSPROPERTY_ONESHOT_RECONNECT=0` / `DISCONNECT=1`; the GUID must be checked against the WDK `bthhfpddi.h`. Using it needs IDeviceTopology/IKsControl COM interop, works only for A2DP/HFP and varies by driver. **Not in v1.** v1 opens Windows' own list |
| Add a device | Through Windows | `ms-settings-connectabledevices:devicediscovery` (Add-a-device dialog), falling back to `ms-settings:bluetooth` |
| Connect UI | Through Windows | Windows 11 22621+: try `ms-actioncenter:controlcenter/bluetooth` (Quick Settings Bluetooth list, one-click connect); else `ms-settings:bluetooth`. **Check manually** that this URI opens the list; if not, use `ms-settings:bluetooth` everywhere |

### Where it lives
- Put it in a new folder `src/Trayloom.Features.System/Bluetooth/`, beside Wi-Fi.
- Catalog id `notch-bluetooth`, namespace `Trayloom.Features.SystemTools.Bluetooth`.
- **No new project** is needed: System already uses WinRT radios, has NAudio for a later KS phase, and already has its harness.
- So `App.FeatureAssemblies`, the `.sln` and csproj references stay unchanged.

### New files (System project)
1. **`Bluetooth/BluetoothKeys.cs`** — `internal static class BtKeys`:
   - Constants:
     - `FeatureId = "notch-bluetooth"`
     - `ConnectAlerts = "notchBluetoothConnectAlerts"` (true)
     - `LowAlerts = "notchBluetoothLowAlerts"` (true)
     - `LowThreshold = "notchBluetoothLowThreshold"` (int 20; choices 10, 15, 20, 30)
     - `LowStrip = "notchBluetoothLowStrip"` (true)
     - `Show = "notchBluetoothShow"` (`"all"` | `"connected"` | `"battery"`; default `"all"`)
     - `Hidden = "notchBluetoothHidden"` (comma-separated device keys, default `""`)
   - `Register(IAppSettings)`.
   - `OpenUri(string)` (copy of `WifiKeys.OpenUri`).
   - `OpenSettings()` → `ms-settings:bluetooth`.
   - `OpenAddDevice()` → `ms-settings-connectabledevices:devicediscovery`; on exception or `Process.Start` failure, `ms-settings:bluetooth`.
   - `OpenConnect()` → `Environment.OSVersion.Version.Build >= 22621` ? `ms-actioncenter:controlcenter/bluetooth` : `ms-settings:bluetooth`, with fallback to settings.
2. **`Bluetooth/BluetoothModels.cs`:**
   - `enum BtKind { Headphones, Speaker, Keyboard, Mouse, Gamepad, Phone, Computer, Watch, Other }`
   - `enum BtRadio { Unknown, None, On, Off, Disabled }`
   - `sealed record BtDevice(string Key, string Name, BtKind Kind, bool Connected, int? Battery, Guid? Container, IReadOnlyList<string> AepIds, DateTime? ConnectedSinceUtc)`
     - `Key` = Bluetooth address as 12 lowercase hex digits.
     - `Battery` is always null when not connected.
   - `sealed record BtSnapshot(bool Loaded, BtRadio Radio, bool AccessDenied, IReadOnlyList<BtDevice> Devices)`.
   - `sealed record BtNotice(BtKind Kind, string Name, string Detail, int? Battery, bool Low)`.
3. **`Bluetooth/BluetoothRules.cs`** — pure, harness-tested; `internal static class BtRules`:
   - `int? ParseBattery(object? v)`: byte/sbyte/ushort/short/uint/int in 0..100 → value; anything else → null.
   - `string? NormalizeAddress(string? s)`: strip `:`/`-`, lowercase, must be 12 hex digits. Also accepts AEP ids by taking their last 17 chars.
   - `Guid? ParseContainer(object? v)`: Guid, or a string with or without braces.
   - `BtKind KindFrom(uint? major, uint? minor, string name)`:
     - Name rules first: airpods, buds, headphone, headset, earbuds, `wh-`, `wf-` → Headphones; keyboard, keys → Keyboard; mouse, `mx master`, `mx anywhere`, trackpad → Mouse; controller, gamepad, xbox, dualsense → Gamepad; watch → Watch; speaker, soundbar → Speaker.
     - Then the class table copied from `AccessoryService.cs:364-379`; major 1 → Computer, 2 → Phone.
   - `IReadOnlyList<BtDevice> Merge(IEnumerable<AepRow> aeps, IEnumerable<NodeRow> nodes)`:
     - `AepRow(string Id, string Name, bool Connected, string? Address, Guid? Container, bool Le)`; `NodeRow(string Id, Guid? Container, int? Battery)`.
     - Group AEPs by `Container ?? address`.
     - Name = the classic AEP's name if any, else the first.
     - Connected = any.
     - Battery = max non-null node battery in that container, only when Connected.
   - `IReadOnlyList<BtDevice> Visible(IReadOnlyList<BtDevice> all, string show, IReadOnlySet<string> hidden)`:
     - Sort: connected first, then has-battery, then name (current culture, ignore case).
     - `"connected"` keeps only connected devices; `"battery"` keeps only devices with a battery.
   - `bool StepLow(ref BtLowState s, int percent, int threshold)`:
     - The first reading only sets the baseline (`Low = percent <= threshold`).
     - Returns true once when crossing down to ≤ threshold.
     - Re-arms at ≥ threshold + 5.
   - `string RingTint(int pct, int threshold)`: ≤10 → red `#FF453A`; ≤ threshold → orange `#FF9F0A`; else white.
4. **`Bluetooth/BluetoothService.cs`** — `internal sealed class BluetoothService : IBluetoothState, IDisposable`; `Snapshot`, `event Changed` (UI thread).
   - **Start(context):**
     - After 3 s:
       - **Radio:** `Radio.RequestAccessAsync()`. If not `Allowed`, set `AccessDenied = true`. Then `GetRadiosAsync()` → first `Kind == Bluetooth`. None → `BtRadio.None`. Subscribe `StateChanged`.
       - **AEP watchers (2):** `DeviceInformation.CreateWatcher(selector, ["System.Devices.Aep.IsConnected","System.Devices.Aep.DeviceAddress","System.Devices.Aep.ContainerId"], DeviceInformationKind.AssociationEndpoint)`. Do **not** request the battery key on AEPs.
       - **Node watcher (1):** `CreateWatcher("System.Devices.DeviceInstanceId:~<\"BTH\"", ["{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2","System.Devices.ContainerId"], DeviceInformationKind.Device)`. `Updated` delivers battery changes.
     - All callbacks: `context.Ui.BeginInvoke`, then re-merge, then `Changed`.
   - **Class of device:**
     - Once per classic AEP, `BluetoothDevice.FromIdAsync(id)` → `ClassOfDevice.MajorClass/MinorClass`, cached by key (pattern from `AccessoryService.cs:200-213`).
     - Dispose the `BluetoothDevice` right away.
   - **Fallback battery refresh:**
     - `DeviceInformation.FindAllAsync(sameAqs, sameProps, Device)`.
     - Every 30 s while a Bluetooth view is visible (page, tray tab or settings register through `Attach()`/`Detach()` counters).
     - Every 120 s while any device is connected.
     - Never when nothing is connected.
     - Also 1.5 s and 4 s after a connect.
   - **LE GATT fallback:**
     - Only for a connected LE device with no node battery, at most once per 10 min.
     - Same code as `AccessoryService.cs:278-299`.
     - Dispose the `BluetoothLEDevice`.
   - **Connect notice:**
     - Trigger: AEP `IsConnected` false→true after `EnumerationCompleted`, more than 5 s after start.
     - Skip if the same key disconnected less than 10 s ago (AirPods flapping), or the kind is Computer/Phone.
     - Wait up to 5 s for a battery, then queue `BtNotice(kind, name, battery?…)` if `ConnectAlerts` is on.
   - **Low battery:** on every battery value of a connected device, if `LowAlerts` and `StepLow(...)` → queue a low notice.
   - **Queue:**
     - At most 4 items; drop items older than 30 s.
     - `TakeNotice()` dequeues one.
     - After a take, if more remain, a one-shot 7.5 s `DispatcherTimer` calls `IslandContext?.Invalidate()`.
     - Queuing calls `IslandContext?.Invalidate()`.
   - **Radio:** `Task<RadioAccessStatus> SetRadioAsync(bool on)` (mirror `WifiService.SetRadioAsync`).
   - **Watcher failure:** if a watcher goes `Aborted`/`Stopped` unexpectedly, restart once after 10 s, then every 5 min. Log `[bluetooth] watcher aborted`.
   - **Logging:** counts and kinds only, never addresses. Example: `[bluetooth] connect kind=Headphones battery=75`.
   - Dispose stops all watchers and timers.
5. **`Bluetooth/BluetoothFeature.cs`** — `public sealed class BluetoothFeature : IFeature`, `Id => "notch-bluetooth"`.
   - **Start:** `BtKeys.Register`, create the service, `context.Services.Provide(service)` and `Provide<IBluetoothState>(service)`.
   - **Stop:** `Withdraw` both, then `Dispose`.
   - **Actions:**
     - `toggle-radio`: "Turn Bluetooth off" / "Turn Bluetooth on", only when the radio is On or Off.
     - `settings`: "Open Bluetooth settings".
     - `add-device`: "Add a Bluetooth device".
     - Under `TRAYLOOM_DEV=1`:
       - `dump` "Bluetooth: log devices".
       - `simulate-connect` "Bluetooth: simulate a connect notice": the first connected device, or "Test headphones" at 64 %.
       - `simulate-low` "Bluetooth: simulate low battery": 12 %.
6. **`Bluetooth/BluetoothIsland.cs`:**
   - `BluetoothIslandSection : IIslandSection`: `FeatureId notch-bluetooth`, `Order 172`, `Id "bluetooth"`, `Title "Bluetooth"`, `Glyph "\uE702"`.
   - `BluetoothPageView : Grid, IIslandTopRowHost` — a structural copy of `WifiPageView`:
     - A `WifiSwitch` re-automated as `Bluetooth.Radio` (`WifiLook.Automate(sw,"Bluetooth.Radio","Bluetooth")`).
     - `_service.Attach()` on Loaded, `Detach()` on Unloaded.
   - `BluetoothNoticeStrip : IIslandStrip`, `Id "bluetooth.notice"`, `Order 43`. Returns `TakeNotice()` rendered as:
     - kind glyph (12 px; orange if Low)
     - name (11 Medium, MaxWidth 150, ellipsis)
     - `BatteryRing` 11×11 with 2 px line
     - percent (11 Medium, tabular), or "Connected" when there is no battery.
   - `BluetoothLowStrip : IIslandStrip`, `Id "bluetooth.low"`, `Order 46`:
     - Shown when `LowStrip` is on, there is a connected visible device with Battery ≤ threshold, and the radio is On.
     - Content: kind glyph (orange/red), ring, `"{0}%"`, name (MaxWidth 90). Tooltip `"{0} battery is low: {1}%"`.
     - The element subscribes to `Changed` and updates itself. The service calls `Invalidate()` when the set of low devices changes.
7. **`Bluetooth/BluetoothPanelSection.cs`:**
   - `IPanelSection`: `TabId = PanelTabs.Bluetooth`, `Order 100`, `FeatureId notch-bluetooth`.
   - Card built like `WifiPanelCard` (`WifiPanelSection.cs:33-123`).
8. **`Bluetooth/BluetoothSettings.cs`** — `ISettingsSection`:
   - `PageId Island`, `Anchor "bluetooth"`, `Title "Bluetooth"`, `Order 195`.
   - Keywords: `["battery","headphones","AirPods","earbuds","mouse","keyboard","pair","connect"]`.
9. **`Bluetooth/BluetoothStrings.cs`** — all strings below.
10. **`Bluetooth/BluetoothLook.cs`:**
    - `BatteryRing : FrameworkElement`: copy of `AgentRing` (`AgentsUiKit.cs:244-285`); track white 0.18, tint from `RingTint`.
    - `KindGlyph(BtKind)`: Headphones E7F6, Speaker E7F5, Keyboard E765, Mouse E962, Gamepad E7FC, Phone E8EA, Computer E7F4, Watch E702, Other E702.

**New Core file `src/Trayloom.Core/BluetoothState.cs`** (needed so the App's tray menu stays decoupled):
```csharp
public sealed record BluetoothDeviceInfo(string Key, string Name, bool Connected, int? Battery);
public interface IBluetoothState
{
    bool? IsOn { get; }            // null = no radio or not known yet
    bool? HasAdapter { get; }      // null = not known yet
    IReadOnlyList<BluetoothDeviceInfo> Devices { get; }  // visible list, connected first
    event Action? Changed;         // any thread
    void SetRadio(bool on);        // fire-and-forget; refusals logged
    void OpenBluetoothSettings();
    void OpenAddDevice();
    void OpenConnect();
}
```

### Small edits to shared files (owned by this package)
**Core:**
- `Core/Catalog/FeatureCatalog.cs` after `:152`:
  `list.Add(Def("notch-bluetooth", I, Loc.T("Bluetooth"), Loc.T("See your Bluetooth devices and their battery, turn Bluetooth on or off and get an alert when a device runs low, from the Dynamic Island and the tray."), "\uE702", Per, "", true, "island#bluetooth", []));`
  - `true` is installed by default, on purpose: it replaces the accessory alerts, which are on by default. Set it to `false` to match Wi-Fi.
- `Core/Contributions.cs`:
  - Add `public const string Bluetooth = "bluetooth";` after `:16`.
  - Add a tab entry after `:38`: `new(Bluetooth, Loc.T("Bluetooth"), "\uE702", Loc.T("Your Bluetooth devices, their battery and the Bluetooth switch."), ["notch-bluetooth"], false),`
- `Core/AppSettings.cs`:
  - `:202-216` add `PanelTabs.Bluetooth => "panelShowBluetooth",`.
  - `:256-266` add `["panelShowBluetooth"] = true,`.

**App:**
- `App/Shell/PanelLayout.cs:14`: add `PanelTabs.Bluetooth` to `AfterPredecessor`, so saved orders place it after Network.
- `App/Shell/TrayPanel.cs:277` (inside `TabHasContent`): `if (tab.Id == PanelTabs.Bluetooth && _s.Host.Services.Get<IBluetoothState>() is { HasAdapter: false }) return false;` — no empty tab on PCs without Bluetooth.
- New `App/Shell/BluetoothMenu.cs`, `internal static MenuItem Build(IBluetoothState bt, IShell shell)` → submenu "Bluetooth":
  - "Turn Bluetooth off/on" when `IsOn` is not null.
  - A separator.
  - Each connected device: "{name} — {n}%" or "{name} — Connected" → `shell.OpenPanel(PanelTabs.Bluetooth)`.
  - "Connect a device…" → `OpenConnect`.
  - "Bluetooth settings…" → `OpenBluetoothSettings`.
  - "Show in panel" → `shell.OpenPanel("bluetooth")`.
- `App/Shell/AppShell.cs:568` (after the cleaning-mode block, before the `if (any)` separator): `if (rt.IsAvailable("notch-bluetooth") && _host.Services.Get<IBluetoothState>() is { } bt) { any = true; menu.Items.Add(BluetoothMenu.Build(bt, this)); }`

**Island:**
- `Island/IslandModel.cs`:
  - Module after `:42`: `new("bluetooth", Loc.T("Bluetooth"), G.Bluetooth, 'j', C("#0A84FF"), false, Loc.T("Your Bluetooth devices, their battery and the Bluetooth switch."), ["notch-bluetooth"], null, true),`
  - Control after `:74`: `new("bluetooth", Loc.T("Bluetooth"), G.Bluetooth, false, ["notch-bluetooth"]),`
  - `:85` `IsContributed`: add `or "bluetooth"`.
- `Island/Pages.Controls.cs:325`: add `"bluetooth" => Core.ModuleVisible("bluetooth"),`.
- `Island/IslandWindow.Input.cs`:
  - `:1353` add the same visibility line.
  - `RunControl` `:1508`: add `case "bluetooth": ShowModule("bluetooth"); break;`.
- `Island/IslandWindow.State.cs:402` (`ModuleForActivity`, before `_ => act`): `_ when act.IndexOf('.') > 0 => act[..act.IndexOf('.')],`
  - Clicking `bluetooth.low` then opens Bluetooth, and `github.coding` opens GitHub.
- `Island/AccessoryService.cs:48` `Wanted`: append `&& !_core.Ctx.Runtime.IsAvailable("notch-bluetooth")`.
  - Use `IsAvailable`, not `IsRunning`: `IslandCore.OnRuntime` → `Accessories.Sync()` (`IslandCore.cs:217-230`) may fire before the feature starts.
- `Island/Settings.Section.cs:356-357`: when `rt.IsAvailable("notch-bluetooth")`:
  - Pass `ok=false`, reason `Loc.T("Bluetooth shows these alerts now.")`, action `c.Shell.OpenSettings("island#bluetooth")`.
  - Hide the `:368-371` caption.

**Optional (Tools):** `Tools/CommandBar/BarCatalog.cs` after `:406`: three rows for `notch-bluetooth` actions, copying the Wi-Fi lines `:394-406`.

### Island page layout (`BluetoothPageView`)
- **Top row:** the radio switch, enabled only when the radio is On or Off.
- **Loading:** `WifiLook.Spinner(16)`.
- **No adapter** (`BtRadio.None`): `Message("\uE702", "No Bluetooth on this PC.", "Plug in a Bluetooth adapter to use this page.")`.
- **Disabled:** "Bluetooth is turned off by a hardware switch or airplane mode."
- **Off:** "Bluetooth is off." with pill "Turn on" (`Bluetooth.TurnOn`).
- **On:**
  - Header "Connected" (11 semibold, white 0.6).
  - Rows, 34 high:
    - kind glyph 14
    - name 12.5
    - right: ring 11 plus "75%"; or caption "Connected"
  - Header "Paired devices".
  - Rows with caption "Not connected" and a trailing pill "Connect…" (`Bluetooth.Connect`) → `BtKeys.OpenConnect()`.
- **Empty list:** "No paired devices."
- **Footer pills:** "Add a device" (`Bluetooth.Add`) and "Bluetooth settings" (`Bluetooth.Settings`).
- **Bottom line** (10.5, white 0.45, wrap): "Windows connects and disconnects devices itself. Connect… opens its Bluetooth list."
- **Refused radio change:** orange notice for 6 s: "Windows did not let Trayloom switch Bluetooth. Use Bluetooth settings instead."
- **Controls page:** gets a "Bluetooth" tile → `ShowModule("bluetooth")`.

### Tray tab "Bluetooth" (`BluetoothPanelSection`)
- `PanelRowGroup`, starting with a `PanelToggleRow("\uE702", "Bluetooth", caption, on, v => service.SetRadio(v), true)`.
  - Caption: "On" / "Off" / "Turned off by hardware".
  - Disabled when the radio is not On or Off.
  - Named `Bluetooth.PanelRadio`, like `NameSwitch`.
- One `PanelActionRow(kindGlyph, name, caption, click, showCaption: true, chevron: false)` per visible device:
  - Caption "Connected · {0}%", "Connected" or "Not connected".
  - Click: connected → `OpenBluetoothSettings`; not connected → `InvokeAfterClose(OpenConnect)`.
- Then "Add a device…" (`InvokeAfterClose(OpenAddDevice)`) and "Bluetooth settings…".
- `context.ActiveChanged` → `Attach`/`Detach` (fast refresh only while visible).
- The whole card is collapsed when `HasAdapter == false`.

### Settings card (Settings › Dynamic Island › Bluetooth, anchor `bluetooth`)
- `SettingsCard("Bluetooth")`, in this order:
  1. Switch "Show when a device connects" — caption "Its name and battery appear in the closed island for a moment."
  2. Switch "Warn when a battery runs low" — caption "Once per charge, when a connected device falls to the level you choose."
     - Indented picker "Warn at": 10 %, 15 %, 20 %, 30 %.
     - Indented switch "Keep a low battery in the closed island" — caption "Until you charge it or it disconnects."
  3. Picker "Devices to show": "All paired devices" / "Connected devices" / "Devices that report a battery".
  4. Subheading "Shown devices": one checkbox row per paired device (name + "Connected · 75%" / "Not connected"), which edits `Hidden`.
  5. Caption: "Battery levels come from Windows. Many earbuds report one level for both buds, in steps of 10 or 20%. AirPods do not report each bud or the case to Windows. Some devices report no battery at all."
  6. Bordered button "Open Bluetooth settings".

### Timings
| What | Timing |
|---|---|
| Startup | Watchers start 3 s after feature start |
| Connect notice | Waits up to 5 s for a battery value |
| Connect flapping | A reconnect within 10 s of a disconnect gets no notice |
| Fallback battery read | 30 s while a view is visible, 120 s while any device is connected, none otherwise |
| Notice queue | At most 4, expire after 30 s, shown 7.5 s apart |
| Low alert | Hysteresis +5 % |
| Radio | Event-driven |

### Failure states
- `AccessDenied`: switch disabled, with caption "Windows did not let Trayloom switch Bluetooth".
- No adapter: page message, no tray tab, no menu entry.
- Watcher aborted: retry as described above.
- No battery: the row shows "Connected" without a ring.
- Disconnected device: the battery is always hidden (no stale values).

### Later, not in these packages
- **Optional fix:** `PeripheralReader` could read `IBluetoothState`. That would remove its 20 s full enumeration and its stale values.

---

## PART 2 — GitHub while you code

### What already exists
- GitHub data:
  - `GitHubRuntime` polls notifications plus one GraphQL query (`GitHubRuntime.cs:481-536`).
  - The transport is gh CLI or a DPAPI token. It is built privately at `:493-494`; `IGhTransport.GraphQlAsync(query, withRepoCount)` is at `GitHubClient.cs:23-28` and `:97-107`.
  - Notices: `Queue`/`TakeNotice` (`:629-641`), shown by the `github.notice` strip (`GitHubIsland.cs:41-76`).
- Query and parsing helpers:
  - Repo-name validation: `GitHubQuery.cs:14`.
  - Inlined-literal query pattern: `:53-76`.
  - Check mapping (`statusCheckRollup.state`): `GitHubModels.cs:131-141`.
  - Process runner with timeout, kill and output cap to copy for `git`: `GhCli.cs:88-161`.
- UI:
  - The page `Build()` shows a not-connected box or tiles (`GitHubIsland.cs:109-150`).
  - Settings section: Order 180 (`GitHubSettings.cs:14-28`).
- Claude Code sessions publish `cwd` in `%USERPROFILE%\.claude\sessions\<pid>.json`, with fields `pid`, `sessionId`, `cwd`, `entrypoint`, `updatedAt`, `pidDomain`, `procStart` (`AgentSessions.cs:344-361`, `:449-461`).
- A foreground hook pattern exists in another project and must be copied, not referenced (`Files/Common.cs:234-292`).

**Gap:** nothing knows the repository you are in. There is no branch, change count, ahead/behind, pull request for the branch, or checks for your latest push, and no "checks finished" alert.

### What is reliable on Windows without new packages
| Signal | Reliability | Decision |
|---|---|---|
| Foreground editor title | Good for VS Code, Cursor, Windsurf, VS and JetBrains with default title settings. Gives a folder name, not a path. Custom `window.title` breaks it | Use, with a folder-name → repo index |
| Terminal title | Gives a full path for Git Bash (`MINGW64:/e/…`) and PowerShell/cmd prompts that show the path. Claude Code sets a task title (no path) | Use when a path is present |
| Claude Code `sessions\*.json` `cwd` | Exact path, live while the process runs | Use (read-only, not credentials) |
| Recent `.git` activity (mtime of `index`, `HEAD`, `logs/HEAD`) | Good fallback for indexed repos | Use |
| Another process's current directory (PEB read) | Undocumented and fragile | **Do not use** |
| `git` CLI with timeout, `--no-optional-locks`, `GIT_OPTIONAL_LOCKS=0` | Reliable, read-only, never writes the index | Use |
| GitHub GraphQL through the existing transport | Reliable when connected | Use |

### New files (`src/Trayloom.Features.GitHub/Coding/`, namespace `Trayloom.Features.GitHub`)
1. **`CodingModels.cs`:**
   - `record TitleHint(TitleHintKind Kind, IReadOnlyList<string> Candidates)`, where `TitleHintKind` = None | Path | Folder.
   - `record RepoRef(string Root, string Name)`.
   - `record GitState(string? Branch, bool Detached, string? HeadSha, string? Upstream, int Ahead, int Behind, int Staged, int Unstaged, int Untracked, int Conflicts, bool UntrackedUnknown)` with `Changes => Staged+Unstaged+Untracked+Conflicts` (counted per entry).
   - `record GhSlug(string Owner, string Name)`.
   - `enum CodingReason { Foreground, Recent, Agent, Git }`.
   - `record CheckSummary(CheckState State, int Total, int Passed, int Failed, int Running, int Skipped, IReadOnlyList<string> Failing, string? Url)`.
   - `record BranchPull(int Number, string Title, string Url, bool IsDraft, string? ReviewDecision, string Repo)`.
   - `record CodingSnapshot(RepoRef? Repo, CodingReason? Reason, GitState? Git, GitError GitError, GhSlug? Slug, string? DefaultBranch, BranchPull? Pull, CheckSummary? Checks, DateTime? GitAt, DateTime? GitHubAt, GhError GitHubError)`, where `enum GitError { None, Missing, NotTrusted, TimedOut, Failed }`.
2. **`CodingTitles.cs`** — pure; `static TitleHint Parse(string image, string title)`:
   - Image is the file name, compared without case.
   - **VS Code family** (`Code.exe`, `Code - Insiders.exe`, `Cursor.exe`, `Windsurf.exe`, `VSCodium.exe`):
     - Strip a leading `● `.
     - Strip the suffix ` - Visual Studio Code - Insiders`, ` - Visual Studio Code`, ` - Cursor`, ` - Windsurf` or ` - VSCodium`.
     - If the title contains `[WSL:`, `[SSH:`, `[Dev Container`, `[Codespaces` or `[Tunnel` → None.
     - Split on ` - `. Candidates = segments from last to first, each with ` (Workspace)` removed → Folder.
   - **`devenv.exe`:** strip ` - Microsoft Visual Studio` and any ` (…)` groups; first segment → Folder.
   - **JetBrains** (`idea64`, `rider64`, `pycharm64`, `webstorm64`, `goland64`, `clion64`, `rustrover64`, `phpstorm64`, `datagrip64`): split on ` – ` (U+2013); first segment → Folder.
   - **Terminals** (`WindowsTerminal.exe`, `OpenConsole.exe`, `conhost.exe`, `cmd.exe`, `powershell.exe`, `pwsh.exe`, `mintty.exe`, `bash.exe`, `wezterm-gui.exe`, `alacritty.exe`, `Tabby.exe`, `Hyper.exe`, `claude.exe`):
     - Regex `(?<![A-Za-z])[A-Za-z]:\\[^<>"|?*\r\n]*` → Path, trimmed of `> ` and trailing spaces.
     - Or `MINGW(32|64)?:/([a-zA-Z])/(.*)` and `^/([a-zA-Z])/(.*)` → `X:\…`.
     - Else None.
   - **Anything else** → None (not a coding app). `IsCodingApp(image)` exposes the same lists.
3. **`CodingFocus.cs`** — pure; `static (RepoRef? Repo, CodingReason? Reason) Decide(CodingInput i)`.
   - Input: `CodingInput(RepoRef? ForegroundRepo, bool ForegroundIsCoding, (RepoRef Repo, DateTime At)? LastCoding, IReadOnlyList<(RepoRef Repo, DateTime UpdatedAt)> AgentSessions, IReadOnlyList<(RepoRef Repo, DateTime At)> GitActivity, DateTime Now)`.
   - Rules, first match wins:
     1. A coding app is in front and resolves → Foreground.
     2. `LastCoding` within 15 min → Recent (sticky while you switch to a browser).
     3. The agent session with the newest `UpdatedAt` within 15 min → Agent.
     4. The newest git activity within 10 min → Git.
     5. Otherwise null (not coding).
4. **`RepoIndex.cs`:**
   - Persisted at `<ctx.DataDirectory>\coding-repos.json` as `{"v":1,"repos":[{"root","name","seenUtc"}]}`, at most 200 entries, least recently seen dropped first.
   - Fed by:
     - resolved title paths
     - session `cwd`s
     - user folders (`notchGitHubCodingFolders`, `|`-separated, scanned 2 levels deep for `.git`, every 30 min)
     - `%APPDATA%\Code\User\globalStorage\storage.json` and the same under `Cursor`: any string value starting with `file:///` whose path has `.git`; files over 2 MB skipped; read at start and every 10 min.
   - `RepoRef? FindRoot(string path)`: walk up at most 30 levels looking for a `.git` directory or file (worktrees and submodules).
   - `RepoRef? ByName(string folder)`: unique case-insensitive name match. On ties, the newest `.git/index` modification time wins.
5. **`GitCli.cs`:**
   - `Find()`: PATH `git.exe`, then `%ProgramFiles%\Git\cmd\git.exe`, then `%LOCALAPPDATA%\Programs\Git\cmd\git.exe`.
   - `RunAsync(root, args, ct)`:
     - Copy of `GhCli.RunAsync`: `ArgumentList`, `CreateNoWindow`, 6 s timeout, 1 MB cap.
     - Environment: `GIT_OPTIONAL_LOCKS=0`, `GIT_TERMINAL_PROMPT=0`, `LC_ALL=C`, `GIT_PAGER=`.
     - Arguments always start with `-C <root> --no-optional-locks`.
   - `static GitState? ParseStatus(string porcelainV2)` (pure):
     - Headers `# branch.oid`, `# branch.head`, `# branch.upstream`, `# branch.ab +A -B`.
     - Entries `1 `/`2 ` counted by XY: X≠'.' → staged, Y≠'.' → unstaged. `u ` → conflict. `? ` → untracked.
   - `static GhSlug? ParseRemote(string url)` (pure): github.com only, in the forms `https://[user[:tok]@]github.com/o/n[.git][/]`, `git@github.com:o/n[.git]` and `ssh://git@github.com/o/n[.git]`, validated by `GitHubQuery`'s regex. **Never log or show a remote URL; it can contain a token.**
   - Commands per refresh:
     - `status --porcelain=v2 --branch --untracked-files=normal`. On timeout, retry once with `--untracked-files=no` and set `UntrackedUnknown`.
     - `config --get remote.<upstream-remote or origin>.url` (cached until `.git/config` changes).
     - `rev-parse --verify --quiet @{u}` (only when there is an upstream).
     - `symbolic-ref --quiet --short refs/remotes/origin/HEAD` (cached).
   - Errors:
     - stderr contains `dubious ownership` → `NotTrusted`.
     - Process start failed → `Missing`.
6. **`CodingChecks.cs`** — pure:
   - `static string BuildQuery(GhSlug slug, string branch, string? pushedSha, bool withParent)`:
     - Inline literals like `GitHubQuery`.
     - Branch as `JsonSerializer.Serialize(branch)`, which is a valid GraphQL string literal.
     - Sha only if `^[0-9a-f]{40}$`.
   - Query text:
```
query {
  repository(owner: "O", name: "N") {
    nameWithOwner url defaultBranchRef { name }
    pullRequests(headRefName: "B", states: OPEN, first: 5, orderBy: {field: UPDATED_AT, direction: DESC}) { nodes { ...P } }
    parent { nameWithOwner pullRequests(headRefName: "B", states: OPEN, first: 5) { nodes { ...P } } }   # only when withParent
    pushed: object(oid: "SHA") { ... on Commit { oid statusCheckRollup { ...R } } }                  # only when sha known
  }
  rateLimit { cost remaining resetAt }
}
fragment P on PullRequest { number title url isDraft reviewDecision headRepositoryOwner { login }
  commits(last: 1) { nodes { commit { oid statusCheckRollup { ...R } } } } }
fragment R on StatusCheckRollup { state contexts(first: 30) { totalCount nodes { __typename
  ... on CheckRun { name status conclusion detailsUrl } ... on StatusContext { context state targetUrl } } } }
```
   - **PR choice:** among the nodes, keep those with `headRepositoryOwner.login == slug.Owner` (case-insensitive); take the first. The parent repository is searched too (fork workflow).
   - **Checks source:** the PR's last commit when its `oid == pushedSha`, else `pushed`.
   - **`static CheckSummary Summarize(JsonElement rollup)`:**
     - CheckRun with `status` ≠ `COMPLETED` → Running.
     - `COMPLETED` + `SUCCESS` → Passed.
     - `NEUTRAL`, `SKIPPED`, `STALE` → Skipped.
     - `FAILURE`, `TIMED_OUT`, `CANCELLED`, `ACTION_REQUIRED`, `STARTUP_FAILURE` → Failed.
     - StatusContext `SUCCESS` → Passed; `PENDING`, `EXPECTED` → Running; `FAILURE`, `ERROR` → Failed.
     - Overall: Failed>0 → Failing; else Running>0 → Running; else Passed+Skipped>0 → Passing.
     - With no contexts, fall back to `rollup.state` (as `GitHubModels.cs:131-141`).
     - `Failing` lists the first 3 names. `Url` = the first failing `detailsUrl`/`targetUrl`, else `https://github.com/O/N/commit/SHA/checks`.
   - **`static bool Finished(CheckSummary? before, CheckSummary now, bool sameSha)`:** true when `sameSha && before?.Running > 0 && now.Running == 0 && now.Total > 0`.
7. **`CodingTracker.cs`** — published service; `Snapshot`, `event Changed`, `IIslandContext? IslandContext`.
   - **Foreground hook:**
     - `SetWinEventHook(EVENT_SYSTEM_FOREGROUND=3, 3, 0, cb, 0, 0, WINEVENT_OUTOFCONTEXT|WINEVENT_SKIPOWNPROCESS)` on the UI thread. Own P/Invoke, plus `QueryFullProcessImageName` through `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` and `GetWindowTextW`.
     - Re-read the title every 5 s while a coding app is in front.
     - Resolve on the thread pool.
   - **Claude sessions:**
     - Read `(CLAUDE_CONFIG_DIR ?? %USERPROFILE%\.claude)\sessions\*.json` every 15 s while the feature is on.
     - Keep a session if its pid is alive (`Process.GetProcessById` not exited) and `updatedAt` is within 15 min.
     - Read only `pid`, `cwd` and `updatedAt`.
   - **Git activity:** stat `.git/index`, `.git/HEAD` and `.git/logs/HEAD` of the 20 most recently seen indexed repos every 60 s.
   - **Active repo:**
     - `FileSystemWatcher` on the git dir, recursive, `NotifyFilter = LastWrite|FileName`. Ignore paths under `objects` and `lfs`. Debounce 1.5 s.
     - Git refresh: 20 s while the strip or page is visible, 60 s otherwise, and right away when the active repo changes.
   - **GitHub refresh** (only when `runtime.Connected` and there is a slug):
     - 30 s while checks are Running, 120 s otherwise.
     - After a push is detected (upstream sha changed, or ahead went from >0 to 0): immediately, then every 15 s for 2 min.
     - Page opened and data older than 30 s → immediately.
     - `remaining < 200` → 5 min.
     - Nothing while not coding.
   - **Finished notice:**
     - `(slug, sha)` seen Running in this run, then `Finished(...)` → `runtime.Queue(new GitHubNotice("", passed ? "Checks passed" : "Checks failed", $"{repo} · {branch}", passed ? Green : Red))`.
     - Gated by `notchGitHubCodingAlerts`.
     - Shown by the existing `github.notice` strip, which opens the GitHub page.
   - **Coding state:**
     - Calls `IslandContext?.Invalidate()` when coding or visibility flips, or when the strip shape changes (repo, branch, number of digits).
     - The strip updates its texts from `Changed`.
8. **`CodingIsland.cs`:**
   - **`GitHubCodingStrip : IIslandStrip`** — `FeatureId notch-github`, `Id "github.coding"`, `Order 45`.
     - Returns null unless `notchGitHubCoding` and `notchGitHubCodingStrip` are on and the snapshot has a repo.
     - Row (≤ 200 DIP):
       - icon `\uE943` (12, white 0.8)
       - branch (11 SemiBold, MaxWidth 110, ellipsis; detached → the 7-char sha)
       - `±N` when Changes>0 (11, tabular, white 0.6)
       - `↑A` when Ahead>0
       - `↓B` when Behind>0
       - a 6 px check dot: green/red/amber, pulsing (copy of `AgentPulse`) while Running; hidden for None
     - Tooltip: `"{repo} · {branch} · {n} changed · {a} to push · {b} to pull · {checks text}"`.
   - **`static FrameworkElement CodingCard(CodingTracker t, GitHubRuntime r)`** for the page: radius-14 card, white 0.08.
     - Line 1: repo name (13 SemiBold) and a branch chip (`AU.Chip` look: 9 SemiBold, tint `#8250DF`).
     - Line 2: `"{0} changed"` / `"No changes"`, `" · {0} to push"`, `" · {0} to pull"`, or `"Not pushed yet"` when there is no upstream.
     - Line 3 (connected):
       - PR: `"#{0} {1}"` plus `" · Draft/Approved/Changes requested/Review required"` (reuse `GhText`), or `"No pull request for this branch"`.
       - Line 4, checks: `"Checks running · {0} of {1} done"` / `"All {0} checks passed"` / `"{0} failing: {1}"` / `"No checks"`.
     - Pills (`L.Pill`): "Open pull request", "Open checks", "Create pull request", "Open repository".
       - "Create pull request" → `https://github.com/O/N/compare/{Uri.EscapeDataString(branch)}?expand=1`, only when there is no PR, the branch is pushed and it is not the default branch.
       - All URLs go through `GitHubLinks.Open` (github.com only).
     - Not connected: line `"Connect GitHub to see the pull request and checks of this branch."`.
     - Git errors:
       - `"Git was not found. Install Git for Windows to follow repositories."`
       - `"Git does not trust this folder. Run “git config --global --add safe.directory <path>” in a terminal."`
       - `"Git did not answer in time."`
9. **`CodingSettings.cs`** — `ISettingsSection`: `PageId Island`, `Anchor "githubCoding"`, `Title "GitHub while you code"`, `Order 181`, `FeatureId notch-github`.
   - Switch "Follow the repository you are working in" (`notchGitHubCoding`, default true). Caption: "Trayloom reads the title of the editor or terminal in front, Claude Code’s open sessions and recent Git activity to find the repository. It runs read-only Git commands on this PC. With GitHub connected, it asks GitHub for that branch’s pull request and checks."
   - Indented switch "Show it in the closed island" (`notchGitHubCodingStrip`, true). Caption: "Branch, changes and checks while you code. Music, timers and agents come first; click the island to switch."
   - Indented switch "Alert when checks finish" (`notchGitHubCodingAlerts`, true). Caption: "Passed or failed, once for each push."
   - Row "Project folders" (`notchGitHubCodingFolders`, `""`). Caption: "Optional. Repositories inside these folders are recognised from editor titles that show only a folder name." Buttons "Add folder…" (`OpenFolderDialog`) and "Remove".
   - Status line: `"Working in {0} on {1}"` / `"Not coding right now"` / the git error text above.
10. **`CodingStrings.cs`** — all strings above, plus "Checks passed" (new) and the existing `GhText.NoticeChecks`.

### Edits to shared files (owned by this package)
- `GitHubFeature.cs:21-48`:
  - Start: `_tracker = new CodingTracker(context, _runtime); context.Services.Provide(_tracker); _tracker.Start();`
  - Stop: `Withdraw`; `if (uninstalled) _tracker.Erase()` (deletes `coding-repos.json`); `Dispose`.
  - Dev actions: "GitHub: log coding state" and "GitHub: simulate checks finished".
- `GitHubRuntime.cs` (one member, next to `:494`): `internal IGhTransport? CreateTransport() => Source switch { "cli" => new GhCliTransport(), "token" => new TokenTransport(_vault.Load), _ => null };`
- `GitHubIsland.cs`:
  - `:111` in `Build()`, after `Children.Clear()`: `if (_runtime.Services.Services.Get<CodingTracker>() is { Snapshot.Repo: not null } t) Children.Add(CodingIsland.CodingCard(t, _runtime));`
  - This stays above both the not-connected box and the tiles.
  - Subscribe to `t.Changed` in the existing Loaded/Unloaded handlers (`:94-100`).
- `Trayloom.Features.GitHub.csproj`: `<InternalsVisibleTo Include="Trayloom.GitHub.Harness" />`.
- New harness `tests/Trayloom.GitHub.Harness/` (csproj + `Program.cs`), modelled on `tests/Trayloom.System.Harness/`.
- Clicking the strip relies on Part 1's `ModuleForActivity` rule.

**Honest limits:**
- Custom VS Code `window.title` settings, remote windows and terminals without a path fall back to Claude sessions and git activity.
- Repositories outside github.com show git data only.
- While music plays, the music strip outranks this one (`IslandCore.cs:500-521`).

---

## PART 3 — The Claude Code pixel mascot

### What already exists
- **Closed strip** (`AgentStripView`, `AgentsIsland.cs:14-78`): one `AgentGlyph` per working provider (`:49-52`). Waiting adds an amber `AgentPulse` (`:43-47`). Size 12, or 10 when several agents work (`:41`).
- **Notices:**
  - Finish notice: `AgentNoticeView` (`:170-195`), still glyph.
  - Attention notice: `AgentAttentionView` (`AgentSessionView.cs:221-245`), with pulse + glyph (`:228-229`).
- **Now card:**
  - Header: custom Claude icon (`AgentSessionView.cs:52-55`).
  - Rows: glyph per session, size 9 (`:122`).
- **Limits card header:** provider mark (`AgentsCards.cs:53-86`, called at `:202`).
- **Glyph animation:** `AgentGlyph` breathes and spins on the shared 30 Hz `AgentClock` (`AgentsUiKit.cs:68-138`, `:385-459`). `AgentClock.ReduceMotion` = `!SystemParameters.ClientAreaAnimation` (`:76`), the same as island `D.ReduceMotion` (`IslandUi.cs:152`).
- **Live strip condition** (`AgentsIsland.cs:311-315`): live turns or non-idle sessions only.
- **Settings:**
  - Keys: `AgentModels.cs:460-488`.
  - Registration: `AgentSupport.cs:52-73`.
  - "Live activity" block: `AgentsSettingsSection.cs:157-170`.
  - Claude orange: `ClaudeMark.Orange` `#D97757` (`Core/Ui/ClaudeMark.cs:22`).
- **Zoom:** island default 1.10, applied as a LayoutTransform (`IslandWindow.cs:85`, `IslandWindow.Input.cs:1582`).

**Gap:** there is no mascot, no idle presence between prompts, and no symbol setting.

### Pixel map, measured from the photo
- One cell = 6 photo pixels, and every band is 2 cells tall.
- Measured: arms on x 10–111, body on x 22–100, eyes on x 34–39 and 84–89, legs on x 22–27, 34–39, 84–89 and 96–100.
- Grid: 17 columns × 10 rows, square cells.
- Legend: `#` body `#D97757` (photo `#D77757`; use `ClaudeMark.Orange`); `e` eye `#000000` (painted, never transparent, so it reads on light previews); `.` empty.
```
     col 0123456789ABCDEFG
r0       ..#############..
r1       ..#############..
r2       ..##e#######e##..
r3       ..##e#######e##..
r4       #################
r5       #################
r6       ..#############..
r7       ..#############..
r8       ..#.#.......#.#..
r9       ..#.#.......#.#..
```
- Every row is mirror-symmetric.
- Body: cols 2–14. Arms: cols 0–1 and 15–16 on rows 4–5. Eyes: cols 4 and 12 on rows 2–3. Legs: cols 2, 4, 12 and 14 on rows 8–9.

### The animation, rebuilt from a recording (supersedes the single-frame pixel map above)
The mascot opens a laptop and types on it, as it does when a Claude Code session opens. The frames were read from a 30 fps screen recording and redrawn as data in `ClaudeMascot.cs`; no code or asset of another app is used.
- **Grid:** one cell is 3.66 px of the recording. Every frame is 17 columns × 11 rows and every pose stands on row 10, so the floor never moves. The standing body is 8 cells wide and 8 rows tall (rows 3–10) with 2-cell arms on rows 5–6 and four legs; the rows above it are air for the lifted laptop.
- **Inks:** `O` front of the body (`ClaudeMark.Orange`), `D` its shaded side (`#C0684C`), `e` eye (black), `G` laptop (`#8C8C8C`), `W` the lighter edge of the laptop (`#B8B8B8`), `.` empty.
- **Frames:** 19 distinct poses written as text grids. A clip is a list of (pose, milliseconds):

| Clip | Steps | Length | What it shows |
|---|---|---|---|
| Intro | 12 | 1268 ms | stand 200 ms, wink 400 ms, grab the closed laptop, lift it open as an L, swing it down, land it on the floor, turn to the side |
| TypingLoop | 16 | 1501 ms | the side view; the arm alternates between three poses (resting, hand on the keys, raised) at 67–133 ms each |
| Outro | 4 | 533 ms | pick the laptop up, fold it into a small wedge, settle back (squashed, then arms low) |
| Wink | 5 | 401 ms | the squash and the wink of the intro, played by an idle mascot |

### Animation (`MascotMood`: Idle, Working, Waiting)
- **Working:** the Intro once, then the TypingLoop for as long as Claude works. When the mood leaves Working the Outro plays and then the new mood takes over; if the laptop never came out (the first 0.6 s), nothing is put away.
- **Waiting, wave:** the front pose and the same body with both arms one row higher, swapped when `floor(t / 0.4) % 2 == 1`. The amber `AgentPulse` stays beside it.
- **Idle:** the front pose, and the Wink once every `4.0 + (seed % 1500)/1000.0` s, never in the first second.
- **Reduced motion:** Working → the resting typing pose; Waiting → the arms-up pose, so the state still shows; Idle → the front pose. Nothing moves.
- **Driver:**
  - `AgentClock.Instance.Subscribe` while Loaded, IsVisible and not ReduceMotion (same rules as `AgentGlyph:427-445`).
  - `ClaudeMascotArt.FrameAt(mood, t, seed, reduceMotion)` hands out the same array for the same pose; call `InvalidateVisual()` only when the array changes.
  - The cell size still comes from `CellPixels` over 10 rows, so the body is a little smaller than the old 10-row picture and the laptop makes the art wider than the body; the art is centred horizontally in the footprint, and vertically the standing body (rows 3–10) is centred so it lines up with the text beside it.

### Rendering, crisp at any scale
- New file `src/Trayloom.Features.Agents/ClaudeMascot.cs`:
  - `enum MascotMood`, `readonly record struct MascotFrame(string[] Rows, int Milliseconds)`.
  - **`static class ClaudeMascotArt`** (pure):
    - `Columns=17`, `Rows=11`, `HeightRows=10`, `BodyTop=3`, `BodyRows=8`, the frames and the clips `Intro`, `TypingLoop`, `Outro` and `Wink` (see the table above).
    - `string[] FrameAt(MascotMood mood, double t, int seed, bool reduceMotion)`, plus `FrameAt(clip, t)`, `WorkingFrameAt(t)` and `OutroFrameAt(t)`.
    - `int CellPixels(double targetHeightDip, double deviceScale) => Math.Max(1, (int)Math.Round(targetHeightDip * deviceScale / HeightRows, MidpointRounding.AwayFromZero))`.
    - `IEnumerable<(int Row,int Col,int Len,char Ink)> Runs(string[] frame)`: merges horizontal runs of the same ink.
  - **`sealed class ClaudeMascotView : FrameworkElement`**:
    - `ctor(double targetHeight, MascotMood mood)`; settable `Mood`.
    - `IsHitTestVisible=false`, `SnapsToDevicePixels=true`, `UseLayoutRounding=true`, `RenderOptions.SetEdgeMode(this, EdgeMode.Aliased)`.
    - **Scale:**
      - `src = PresentationSource.FromVisual(this)`.
      - `s = src.CompositionTarget.TransformToDevice.M11 × ((Transform)TransformToAncestor(src.RootVisual)).Value.M11`. This includes the 1.1 zoom.
      - Before attach, fall back to `VisualTreeHelper.GetDpi(this).DpiScaleX`.
    - **Measure:** `cell = CellPixels(target, s)`; size = the classic glyph's square footprint (`target × 1.45 + 1`), with the art centred in it (the standing body, not the whole box, vertically).
    - **OnRender:**
      - Recompute `s`. If `cell` changed, `InvalidateMeasure()` and return (guard against loops).
      - Snap the origin: transform `(0,0)` to device space, take the fractional part, and `PushTransform(new TranslateTransform(-fracX/s, -fracY/s))`.
      - Draw each run as `DrawRectangle(brush, null, new Rect(col·cell/s, row·cell/s, len·cell/s, cell/s))`. Brushes frozen.
    - Re-measure on `OnDpiChanged` and on Loaded.
  - **Worked example** at 125 % × 1.1 = 1.375:
    - Strip, target 12 → cell 2 → 34×22 device px (24.7×16 DIP), body 16 px tall.
    - Target 10/9/8 → cell 1 → 17×11 px (12.4×8 DIP), body 8 px tall.
  - **`static class AgentSymbol`:** `FrameworkElement Create(AgentProvider p, double size, MascotMood mood, bool useMascot)`.
    - Claude with `useMascot` → `ClaudeMascotView(size, mood)`.
    - Otherwise → `new AgentGlyph(p, size, mood != MascotMood.Idle, spin: mood != MascotMood.Waiting)`.
  - **`static class MascotStrings`:**
    - "Claude symbol"
    - "Pixel mascot"
    - "Classic spark"
    - "The mascot opens a laptop and types while Claude works, waves when Claude needs you, and winks in between."
    - "Keep agents in the island between prompts"
    - "For 3 minutes after an agent answers, so the closed island does not come and go with every turn."

### Where it goes
| Place | Change | Size | Mood |
|---|---|---|---|
| Closed strip working/waiting, `AgentsIsland.cs:49-52` | `AgentSymbol.Create(p, size, waiting && p==Claude ? Waiting : Working, settings.ClaudeMascot())`, replacing `new AgentGlyph(p,size)`. Keep the amber pulse at `:43-47` | 12 or 10 | Working / Waiting |
| Waiting-for-permission notice, `AgentSessionView.cs:229` | `AgentAttentionView(attention, onClick, bool mascot = false)`; mascot when Claude. Caller `AgentsIsland.cs:338` passes `runtime.Settings.ClaudeMascot()` | 11 | Waiting |
| Finish notice, `AgentsIsland.cs:177-180` | `AgentNoticeView(notice, bool mascot=false)`; caller `:359` passes the setting | 11 | Idle |
| AI Agents "Now" card header, `AgentSessionView.cs:52-55` | `customIcon = AgentSymbol.Create(Claude, 8, waiting?Waiting:working?Working:Idle, c.Runtime.Settings.ClaudeMascot())` | 8 | as stated |
| Now card rows, `AgentSessionView.cs:122` | Same factory for Claude sessions | 9 | from the session state |
| Claude limits card header, `AgentsCards.cs:202` | Pass `customIcon` when provider is Claude and the mascot is on (fits the 13-wide slot at `:60-63`) | 9 | Working if `snapshot.Working(Claude).Any()`, else Idle |
| Settings sample, `AgentsSettingsSection.cs:169` | Automatic, through `AgentStripView` | | |

**Linger** (the user said "while I am using Claude"):
- `AgentsIsland.cs:311-315`: also return a strip when `settings.Linger()` and a session is Idle with `now - LastActivity <= 3 min`.
- `AgentStripView:36`: include those sessions with mood Idle. The reading is `session.Project` (or "Claude") in white 0.6.
- A one-shot `DispatcherTimer` at the earliest expiry calls `_runtime.IslandContext?.Invalidate()`.

**Settings:**
- `AgentModels.cs:483`: add `ClaudeSymbol = "notchAgentsClaudeSymbol"` and `Linger = "notchAgentsLinger"`.
- `AgentSupport.cs:73`: `app.Register(AgentKeys.ClaudeSymbol, "mascot"); app.Register(AgentKeys.Linger, true);`. Accessors `public bool ClaudeMascot() => _app.Get(AgentKeys.ClaudeSymbol, "mascot") != "spark";` and `public bool Linger() => _app.Get(AgentKeys.Linger, true);`.
- `AgentsSettingsSection.cs`, after `:159`, outside the `if`:
  - `card.Add(Menu("", MascotStrings.Symbol, [("mascot", MascotStrings.Mascot), ("spark", MascotStrings.Spark)], _app.Get(AgentKeys.ClaudeSymbol, "mascot"), v => { _app.Set(AgentKeys.ClaudeSymbol, v); _runtime?.IslandContext?.Invalidate(); _context.RequestRebuild(); }));`
  - Then the caption, then `Switch("", MascotStrings.Linger, AgentKeys.Linger, true, MascotStrings.LingerCaption)`.

**Honest limits:**
- "Using Claude" means a live Claude Code session: CLI, VS Code or the desktop app's Code tab, from `AgentSessions`.
- Plain Claude desktop chats write no session files and are not detected.

---

## PART 4 — Google Calendar

### What already exists
**1. `CalendarService` (`Island/CalendarService.cs`):**
- Windows `AppointmentStore` (`:110-145`, `:199-271`).
- ICS URLs/files from `notchCalendarIcsSources`, a **plaintext list in settings.json** with no UI (`Prefs.cs:150`, `:275-318`). Re-fetched at most every 15 min (`:285`).
- Refresh scheduling at the next boundary, at most every 15 min (`:322-348`).
- Countdown target (`:364-396`); `OpenEvent` opens Outlook/Windows Calendar (`:418-434`).

**2. The `IcsParser` (`:446-661`) is too weak for Google:**
- `TZID` is ignored: zoned times are read as local time, which is wrong across time zones.
- `RECURRENCE-ID` overrides are ignored.
- Monthly `BYDAY` is ignored, `STATUS:CANCELLED` is ignored, and EXDATEs are compared without zones.
- The colour is fixed green and the name comes from the file name.

**3. Page, settings and strip:**
- Page (`Pages.Calendar.cs`): asks for Windows access on show (`:54-63`), shows the permission card unless allowed (`:107-116`, `:552-590`), and draws event cards (`:472-550`).
- Settings (`Settings.Content.cs:556-634`): Windows access rows, countdown switches and "Calendars shown" (only when Windows access is allowed, `:599`). Reached through `island#content-calendar` (`Settings.Section.cs:60-66`).
- Closed strip: `CalendarStripView` (`ClosedViews.cs:556-627`).
- Live activity only when a countdown target exists (`IslandCore.cs:495-498`). `notchCalendarCountdown` defaults to false (`Prefs.cs:223`).
- There are no reminder notices; "reminders" are the countdown strip and "time left".

**4. DPAPI pattern:** `SecretStore` (`Music.Secrets.cs:15-84`).

**Gap:**
- Windows Calendar is often empty: Microsoft retired Mail & Calendar at the end of 2024, and new Outlook does not feed `AppointmentStore`.
- Google events are not available with correct zones, recurrence, colours, names or join links, and their addresses are not stored as secrets.

### Options compared
| | (a) Secret iCal address | (b) OAuth, `calendar.readonly` |
|---|---|---|
| Setup | Paste one URL per calendar | Each user creates a Google Cloud project and OAuth client, or the project ships a verified client |
| Google approval | None | Sensitive scope → app verification (privacy policy, domain). Unverified apps show a warning, allow ≤100 test users, and Testing-mode refresh tokens expire after 7 days |
| Works for | Anyone, unless a Workspace admin disabled secret addresses | Anyone once verified |
| Data | Events, times, zones, recurrence, Meet link (`X-GOOGLE-CONFERENCE`), calendar name (`X-WR-CALNAME`) | Also colours, the calendar list, attendee status (hide declined), `htmlLink`, incremental sync |
| Freshness | Google can lag minutes behind edits | Near real time |
| Risk | The URL is a password; must be stored encrypted | Tokens must be stored encrypted; a client secret in a public GPL repo is not confidential |

**Pick (a).** Option (b) is worth adding later only if the project registers and verifies its own Google client, as "Sign in with Google" reusing the `Music.OAuth.cs` PKCE loopback flow. Do not add it as bring-your-own-client: it is too hard for the users this feature is for.

### Design (Island project, namespace `Trayloom.Features.Island`)
**1. `Calendar.GoogleRules.cs`** — pure; `internal static class GoogleRules`:
- `bool TryNormalize(string input, out string url, out GoogleUrlProblem problem)`:
  - Trim; `webcal://` → `https://`.
  - Scheme must be https and host exactly `calendar.google.com`.
  - Path must match `^/calendar/ical/[^/]+/(private-[A-Za-z0-9]+|public)/basic\.ics$`.
  - Strip the query and fragment.
- `string Redact(string url)` → `"calendar.google.com/…/basic.ics"`. **The full URL is never logged; logs use the feed id.**
- `string? MeetingLink(string? conference, string? url, string? location, string? description)`:
  - First https URL, trailing `).,>` trimmed, whose host is `meet.google.com`, `zoom.us` / `*.zoom.us`, `teams.microsoft.com`, `teams.live.com`, `*.webex.com`, `whereby.com`, `meet.goto.com` or `chime.aws`.
  - Checked in the order conference → URL → location → description (description `\n` unescaped).
- `CalendarSourceMode Resolve(string setting, int feeds)`:
  - `"google"`/`"windows"`/`"both"` as named.
  - `"auto"` → `feeds > 0 ? Google : Windows`.
- `Palette`: Peacock `#039BE5`, Tomato `#D50000`, Sage `#33B679`, Banana `#F6BF26`, Grape `#8E24AA`, Tangerine `#F4511E`, Lavender `#7986CB`, Basil `#0B8043`, Flamingo `#E67C73`, Blueberry `#3F51B5`, Graphite `#616161`, Cocoa `#795548`. A new feed gets the first colour not yet used. ICS carries no colour, so the user picks.
- `string DayUrl(DateTime d)` → `https://calendar.google.com/calendar/r/day/{yyyy}/{M}/{d}`.

**2. `Calendar.Ics.cs`** — pure; replaces `IcsParser`:
- Types:
  - `record IcsTime(DateTime Value, IcsTimeKind Kind, string? Tzid)`, where Kind = Utc | Floating | Zoned | Date.
  - `record IcsEvent(string Uid, IcsTime? RecurrenceId, string Summary, string Location, string Description, IcsTime Start, IcsTime? End, TimeSpan? Duration, string? RRule, IReadOnlyList<IcsTime> ExDates, string Status, string? Conference, string? Url)`.
  - `record IcsCalendar(string? Name, string? DefaultTz, IReadOnlyList<IcsEvent> Events)`.
  - `record IcsOccurrence(IcsEvent Event, DateTime StartLocal, DateTime EndLocal, bool AllDay)`.
- **`IcsCalendar Parse(string text)`:**
  - Unfold lines; handle parameters (`TZID=` with quotes or a `/mozilla.org/…` prefix → last two path segments); `VALUE=DATE`; unescape `\, \; \n \\`.
  - Read `X-WR-CALNAME`, `X-WR-TIMEZONE`, `X-GOOGLE-CONFERENCE`, `URL`, `STATUS`, `DURATION` (ISO).
  - Skip `VALARM`/`VTIMEZONE` bodies: zones come from TimeZoneInfo.
- **`IEnumerable<IcsOccurrence> Expand(IcsCalendar c, DateTime fromLocal, DateTime toLocal, TimeZoneInfo local)`:**
  - **Zone lookup:** `TimeZoneInfo.FindSystemTimeZoneById(tzid)` (.NET 9 on Windows accepts IANA ids through ICU) → `TryConvertIanaIdToWindowsId` → `X-WR-TIMEZONE` → local.
  - **Expansion in the event's zone wall time,** then convert each start to local, so DST keeps the wall-clock time.
  - **RRULE support:**
    - `FREQ` = DAILY | WEEKLY (`BYDAY` list, `WKST` default MO) | MONTHLY (`BYMONTHDAY` incl. negative; `BYDAY` with ordinals `2TU`/`-1FR`; plain weekdays; `BYSETPOS ±n`) | YEARLY (`BYMONTH`).
    - `INTERVAL`, `COUNT`, `UNTIL` (date or UTC).
    - Any other part (`BYHOUR`, `BYWEEKNO`, `BYYEARDAY`, `HOURLY` and below) → the first occurrence only, plus one log line "unsupported RRULE part" without the title.
    - Cap: 2000 iterations per event.
  - **Exceptions:**
    - `EXDATE` compared as instants.
    - Override VEVENTs with the same UID and `RECURRENCE-ID` replace the matching instance.
    - `STATUS:CANCELLED` (master or override) removes it.
  - **All-day:** `DTEND` is exclusive; if it is missing, one day.

**3. `Calendar.Google.cs`** — `internal sealed class GoogleCalendarSource : IDisposable`:
- **Constructor:** `GoogleCalendarSource(IAppSettings app, ILog log, HttpMessageHandler? handler = null)`.
- **Members:**
  - `Feeds`
  - `FeedStatus StatusOf(string id)`: Ok/Offline/Rejected/Failed + `LastOkUtc`
  - `DateTime NextDueUtc`
  - `event Changed`
  - `Task<GoogleAddResult> AddAsync(string url, CancellationToken)`, where the result is Added | InvalidAddress | AlreadyAdded | Rejected | Offline | NotCalendar | TooMany (max 10)
  - `Remove(id)`, `Rename(id, name)`, `SetColor(id, hex)`, `MarkStale(TimeSpan)`, `RefreshNow()`
  - `internal Task ReadAsync(List<CalEvent> events, List<CalendarInfo> calendars, IReadOnlyCollection<string> excluded, DateTime from, DateTime to)`
- **Storage** (`GoogleSecretFile`: own DPAPI helper with entropy `"Trayloom.Calendar.v1:" + name`, CurrentUser scope, temp + replace; same code shape as `SecretStore` but **not** reusing it, so `Music.Secrets.cs` stays untouched and logs don't say "Music"), in `%APPDATA%\Trayloom\calendar\`:
  - `google.bin` = `{"v":1,"feeds":[{"id":"g-<8 hex>","url":"…","name":"…","color":"#039BE5"}]}`
  - `google-cache-<id>.bin` = `{"v":1,"etag":…,"lastModified":…,"fetchedUtc":…,"ics":"…"}`, written only on a 200 response, capped at 8 MB.
  - Remove deletes both.
- **HTTP:**
  - One static `HttpClient` with `AllowAutoRedirect=false`, timeout 20 s, User-Agent `Trayloom/<ver> (+https://github.com/Naveen022006/Trayloom)`.
  - Follow at most 3 redirects, only to https hosts ending in `google.com` or `googleusercontent.com`.
  - Conditional GET (`If-None-Match` / `If-Modified-Since`); body capped at 8 MB.
  - Status mapping:
    - 200 → parse (on the thread pool).
    - 304 → keep.
    - 401/403/404 → Rejected (Google answers 404 for a reset address).
    - 429 → retry in 30 min.
    - 5xx or network → backoff 2, 4, 8 … max 60 min.
  - At most 3 feeds in parallel.
- **Due times:**
  - Every `notchCalendarGoogleInterval` minutes (int, default 15; choices 5, 15, 30, 60).
  - Page shown → `MarkStale(5 min)`.
  - Session unlock or resume when older than the interval.
- **Events:**
  - Id = `$"google:{feedId}|{uid}"`, `Source = "google"`.
  - `CalendarId = "google:" + feedId`, colour = the feed colour, `CalendarName` = feed name (default `X-WR-CALNAME`).
  - `JoinUrl = MeetingLink(...)`, `WebUrl = DayUrl(start)`.
  - `CalendarInfo(id, name, "Google Calendar", color, "google")`.

**4. `Calendar.GoogleWindow.cs`** — the "Add a Google calendar" dialog (WPF window built with `UiKit`; style as `GitHubTokenWindow`):
- Intro: "Paste the secret address of a calendar. Trayloom reads it to show your events and never changes them."
- Steps:
  - "1. Open Google Calendar on the web and go to Settings."
  - "2. Under “Settings for my calendars”, pick the calendar, then open “Integrate calendar”."
  - "3. Copy “Secret address in iCal format” and paste it here."
- Link button "Open Google Calendar settings" → `https://calendar.google.com/calendar/r/settings`.
- `PasswordBox` (the address is secret); placeholder caption "https://calendar.google.com/calendar/ical/…/basic.ics".
- Note: "The address works like a password: anyone who has it can see this calendar. Trayloom stores it encrypted for your Windows account. You can reset it in Google Calendar at any time."
- Buttons "Cancel" and "Add"; "Checking…" while it works.
- Errors:
  - InvalidAddress: "This is not a Google Calendar secret address. It starts with https://calendar.google.com/calendar/ical/ and ends with basic.ics."
  - Rejected: "Google did not accept this address. Copy it again; it changes when you reset it."
  - Offline: "Could not reach Google. Check your connection and try again."
  - NotCalendar: "This address did not return a calendar."
  - AlreadyAdded: "This calendar is already added."
  - TooMany: "You can add up to 10 calendars."
  - Extra hint under the Rejected text: "If your work account hides the secret address, your administrator has turned it off."

**5. `Calendar.GoogleSettings.cs`** — `static void Add(SettingsCard card, IAppSettings app, CalendarService? svc, ISettingsContext c)`; subsection "Google Calendar":
- Picker "Show events from" (`notchCalendarSource`, default `"auto"`): "Automatic" / "Google Calendar" / "Windows Calendar" / "Google and Windows". Caption: "Automatic uses Google Calendar once you add one, and Windows Calendar until then."
- One row per feed:
  - colour dot (click → the palette menu)
  - name
  - status: "Updated {0}" / "Offline. Showing what was loaded at {0}." / "Google no longer accepts this address. Copy it again from Google Calendar."
  - buttons "Rename…" and "Remove"
- Button "Add Google calendar…", plus "Refresh now".
- Picker "Check for changes every": "5 minutes" / "15 minutes" / "30 minutes" / "1 hour". Caption: "Google can take a few minutes to update this address after you change an event."
- Privacy caption: "Events come from Google over HTTPS and are kept in memory and in an encrypted cache on this PC. Nothing is sent anywhere else."

### Edits to shared Island files (owned by this package)
- **`CalendarService.cs`:**
  - `CalEvent` (`:13-40`): add `public string? JoinUrl { get; init; }` and `public string? WebUrl { get; init; }`.
  - Add a field `_google` (created in the ctor `:70-78`, `Changed` forwarded), plus `public GoogleCalendarSource Google`, `public bool UsesWindows`, `public bool UsesGoogle` (from `GoogleRules.Resolve`).
  - `:165` `if (UsesWindows && _store is null && Access == CalendarAccess.Unknown)`; `:173` `if (UsesWindows && _store is not null)`.
  - After `:178`: `if (UsesGoogle) await _google.ReadAsync(events, calendars, excluded, _rangeStart, _rangeEnd);`.
  - `ReadIcs` (`:311`): switch to `IcsReader` and delete `IcsParser` (`:446-661`).
  - `ScheduleNext` (`:322-348`): `if (UsesGoogle && _google.NextDueUtc.ToLocalTime() < next) next = …`.
  - `OpenEvent` (`:418`): `if (e.Source == "google" && e.WebUrl is {} u) Process.Start(u)`.
  - `Dispose`: `_google.Dispose()`.
  - Register `notchCalendarSource="auto"` and `notchCalendarGoogleInterval=15` in a static `GoogleCalendarKeys.Register(app)`, called from the ctor (no edit to `Prefs.cs`).
- **`Pages.Calendar.cs`:**
  - `:57` gate on `Core.Calendar.UsesWindows`.
  - `Shown()`: `Core.Calendar.Google.MarkStale(TimeSpan.FromMinutes(5))`.
  - `:112` show the permission card only when `UsesWindows && !UsesGoogle` and access is not allowed.
  - Mode Google with 0 feeds → card "Add a Google calendar in Settings to see its events here." with pill "Open Settings" → `Core.Ctx.Shell.OpenSettings("island#content-calendar")`.
  - `EventCard` (`:472-550`):
    - When `JoinUrl != null && !AllDay && now >= Start-15min && now < End`, a right-aligned `D.PillButton(Loc.T("Join"), open)` that marks the click handled.
    - Otherwise, a `G.Video` 10 px icon after the time, tooltip "Has a video call".
    - Card tooltip for Google events: "Open in Google Calendar".
- **`Settings.Content.cs` `CalendarOptions`:**
  - After `:560`: `GoogleCalendarSettings.Add(card, app, svc, _c); card.AddDivider();`.
  - Wrap `:561-585` in `if (svc is null || svc.UsesWindows)`.
  - `:599` condition becomes `svc is { Access: CalendarAccess.Allowed } || svc is { UsesGoogle: true }`.
- **`ClosedViews.cs` `CalendarStripView` (`:571-627`)** — optional; drop it if the music session touched this file:
  - Add `_join`, a capsule with `G.Video` 11 on white 0.14, tooltip "Join".
  - Visible when `e.JoinUrl != null && (ongoing || e.Start - now <= 5 min)`.
  - `MouseLeftButtonUp` opens the link and sets `e.Handled = true`.
  - Include it in the shape key.
- **Reminders:** they work unchanged: Google events feed `CountdownTarget` (`:364-396`), so the countdown strip and "time left" behave as for Windows events.

---

## Test plan

### Harness checks (pure logic)
**`tests/Trayloom.System.Harness`** — new file `BluetoothChecks.cs`, plus one call in `Program.cs`:
- `ParseBattery`: `(byte)75`→75, `(byte)0`→0, `(byte)101`→null, null→null, `(uint)55`→55, `"75"`→null.
- `NormalizeAddress`: `"5c:5e:0a:04:06:9c"`, `"5C5E0A04069C"` and an AEP id ending in `-5c:5e:0a:04:06:9c` all give `5c5e0a04069c`; `"xyz"`→null.
- `ParseContainer`: with braces and without, the same Guid.
- `Merge`:
  - Classic AEP connected + LE AEP not connected, same container, with nodes [null, 75, null] → one device, Connected, 75.
  - The same with AEP not connected and node value 10 → Battery null (the LM-BALLOT case).
  - No container → merged by address.
- `KindFrom`: "AirPods Pro"→Headphones, "Galaxy Buds Core"→Headphones, "MX Master 3S"→Mouse, "Xbox Wireless Controller"→Gamepad, (4,6)→Headphones, (5,0x40)→Keyboard, (2,_)→Phone.
- `StepLow`, threshold 20:
  - 25→false, 21→false, 20→true, 18→false, 24→false, 25 re-arms, 19→true.
  - A first reading of 15 → false (baseline).
- `Visible`: sort order; `"connected"` and `"battery"` filters; hidden keys.
- `RingTint`: 9 → red, 20 → orange, 21 → white.

**`tests/Trayloom.GitHub.Harness`** (new):
- `CodingTitles.Parse` table:
  - `Code.exe`, "Program.cs - trayloom - Visual Studio Code" → Folder [trayloom, Program.cs].
  - The same title starting with "● " → same result.
  - "trayloom - Visual Studio Code" → [trayloom].
  - "main.py - api [WSL: Ubuntu] - Visual Studio Code" → None.
  - "README.md - trayloom (Workspace) - Visual Studio Code" → [trayloom, README.md].
  - `Code - Insiders.exe`, "a.ts - web - Visual Studio Code - Insiders" → [web, a.ts].
  - `Cursor.exe`, "x.rs - engine - Cursor" → [engine, x.rs].
  - `devenv.exe`, "Trayloom (Running) - Microsoft Visual Studio" → [Trayloom].
  - `rider64.exe`, "Trayloom – App.xaml.cs" → [Trayloom].
  - `WindowsTerminal.exe`, "PS E:\REA TEST 1\trayloom> " → Path `E:\REA TEST 1\trayloom`.
  - "Administrator: PS C:\src\app>" → `C:\src\app`.
  - `mintty.exe`, "MINGW64:/e/REA TEST 1/trayloom" → `E:\REA TEST 1\trayloom`.
  - "✳ Fix the tray menu" → None.
  - `chrome.exe` → None.
- `CodingFocus.Decide`:
  - Foreground repo A beats a Claude session in B.
  - Browser in front, last coding in A 5 min ago → A (Recent).
  - Last coding 20 min ago, Claude B updated 2 min ago → B (Agent).
  - Only git activity in C 3 min ago → C (Git); 12 min ago → null.
- `GitCli.ParseStatus`:
  - The sample with `+2 -1`, one `.M`, one `M.`, one `2 R.`, one `u UU` and one `?` → Ahead 2, Behind 1, Staged 2, Unstaged 1, Conflicts 1, Untracked 1.
  - `(detached)` → Detached; `(initial)` → HeadSha null.
- `ParseRemote`:
  - The https, ssh (`git@`) and `ssh://` forms, `https://user:tok@github.com/o/n` and a trailing slash all give `o/n`.
  - A gitlab URL → null; `github.example.com` → null.
- `BuildQuery`:
  - A branch with `"`, `\` and `é` is escaped.
  - An invalid sha leaves out `pushed:`.
  - An invalid owner → throws or null.
- `Summarize`:
  - All CheckRun mappings.
  - StatusContext PENDING + CheckRun SUCCESS → Running.
  - FAILURE + IN_PROGRESS → Failing with Running>0.
  - No contexts with rollup SUCCESS → Passing.
- `Finished`: Running→Passing on the same sha → true; first sight Passing → false; a different sha → false; Running→Running → false.

**`tests/Trayloom.Agents.Harness`** — new file `MascotChecks.cs`:
- Every frame of every clip is 11 rows × 17 columns, uses only `.OeDGW`, and stands on the last row.
- The front pose paints 64 cells (2 eyes) and mirrors about its centre; the wave moves only the arms.
- Clip lengths add up (Intro 1268 ms, TypingLoop 1501 ms, Outro 533 ms, Wink 401 ms); the laptop first shows at 0.6 s.
- The frame chosen at a time inside the loop; Working plays Intro, then loops, never back; the Outro starts with the laptop and ends on the settled pose.
- `FrameAt`: reduced motion → typing pose / front pose / arms up; Waiting at t=0.5 → the wave; Idle winks at the period, never in the first second.
- `CellPixels`: (12,1.375)→2, (10,1.375)→1, (8,1.375)→1, (12,1.1)→1, (12,2.2)→3, (12,1.65)→2.
- `Runs`: front row 5 → one run of 12.

**`tests/Trayloom.Island.Harness`** — new file `GoogleCalendarChecks.cs`, plus one call in `Program.cs`:
- URL validation:
  - The private https form is accepted; `webcal` → https.
  - The `public` form is accepted.
  - `http://` is rejected; other hosts are rejected; a path without `basic.ics` is rejected.
  - `Redact` never contains `private-`.
- `MeetingLink`: conference beats description; "Join: https://zoom.us/j/1)." → trailing ")." trimmed; `http://meet.google.com` is rejected.
- `Resolve`: auto with 0 feeds → Windows, with 1 → Google; explicit values.
- ICS parsing and expansion, local zone fixed to India Standard Time in the test:
  - `TZID=America/New_York` 09:00 on 2026-11-10 → 19:30 IST.
  - UTC `Z` times.
  - `VALUE=DATE` all-day, two days via `DTEND`.
  - `DURATION:PT45M`.
  - Weekly `BYDAY=MO,WE;COUNT=4`.
  - Monthly `BYDAY=2TU` and `-1FR`; `BYMONTHDAY=-1`.
  - `UNTIL` date.
  - `EXDATE;TZID=…`.
  - A `RECURRENCE-ID` override moves one instance; a cancelled override removes it.
  - A master with `STATUS:CANCELLED` is skipped.
  - Across the DST change (New York, 1 Nov 2026), the wall clock stays at 09:00 New York time.
  - Folded line plus `\,` unescape.
  - `X-WR-CALNAME` read.
  - Unsupported `BYHOUR` → one occurrence.

### Manual tests (add to `TESTING.md`)
**Bluetooth:**
1. Connect the Galaxy Buds: within about 5 s, the island shows name, ring and %; a reconnect within 10 s shows nothing.
2. Check that the % matches Windows Settings › Bluetooth & devices.
3. The page lists connected first and hides the battery of the disconnected LM-BALLOT.
4. Radio switch from the page, the tray tab and the tray menu; the switch follows `StateChanged`. Put the radio back afterwards.
5. Airplane mode → "turned off by a hardware switch" text.
6. "Connect…" opens the Windows 11 Bluetooth Quick Settings list (else Settings). "Add a device" opens the Add-device dialog.
7. Dev action `simulate-low` → low notice and the `bluetooth.low` strip; clicking the strip opens the Bluetooth page.
8. Threshold and devices-to-show settings apply live.
9. The old accessory alerts no longer appear twice.
10. On a PC or VM without Bluetooth: no tray tab, no menu entry.
11. Reduced motion: no regression.

**GitHub coding:**
1. Open this repo in VS Code: the strip shows `main`, `±n` and checks.
2. Edit a file and save: the count updates within 20 s.
3. Commit: ahead `↑1`. Push: checks go Running (amber pulse), then the "Checks passed/failed" notice.
4. Switch to a browser: the strip stays for 15 min.
5. Git Bash title path works; a Claude Code session in another repo is followed when no editor is in front.
6. Disconnect GitHub: only git data, with the connect line.
7. Rename `git.exe` on PATH → "Git was not found".
8. A non-GitHub remote → git only.
9. Check `Get-Process git` while idle: no stray processes; no `index.lock` ever created.

**Mascot:**
1. Start a Claude Code turn: the mascot opens a laptop and types in the closed strip and is crisp at 125 % × 1.1 (zoom a screenshot: no blurred edges).
2. Permission prompt: arms wave plus the amber pulse.
3. Between prompts it stays 3 min, blinking.
4. Turn on Windows "Animation effects off": the mascot is static, arms up while waiting.
5. Switch "Claude symbol" to Classic spark: the old spinning spark returns everywhere.
6. Now card header and rows, Claude limits card, settings sample.
7. At 100 %, 150 % and 200 % scaling.

**Google Calendar:**
1. Add the primary calendar's secret address: events appear with the right times.
2. An event created in another time zone shows correctly; a recurring event with one moved instance and one deleted instance is right.
3. A Meet event shows "Join" 15 min before and in the closed strip within 5 min.
4. Countdown strip with "Event countdown" on.
5. Reset the secret address in Google: "Google no longer accepts…".
6. Offline: cached events still show after a restart.
7. Source "Windows Calendar" brings back the permission card.
8. Remove the calendar: both `.bin` files are gone from `%APPDATA%\Trayloom\calendar`.
9. Grep `trayloom.log` for `private-`: there must be no matches.

---

## Work packages
**Merge order: WP1 → WP2 → WP3 → WP4 → WP5.** WP1 and WP4 are independent and can run in parallel with WP2. WP3 should merge after WP2 because of the `ModuleForActivity` rule. Each package rebases on `main` first: the other session commits music and GitHub work.

**WP1 — Claude mascot.**
- Files: new `src/Trayloom.Features.Agents/ClaudeMascot.cs` and `tests/Trayloom.Agents.Harness/MascotChecks.cs`.
- Edits: `AgentsIsland.cs`, `AgentSessionView.cs`, `AgentsCards.cs`, `AgentModels.cs`, `AgentSupport.cs`, `AgentsSettingsSection.cs`, and the Agents harness `Program.cs` (one line).
- Contract: none outside Agents.
- Verify: `dotnet build src\Trayloom.Features.Agents\…csproj -p:BuildProjectReferences=false`, then the harness.

**WP2 — Bluetooth.**
- Files: new `src/Trayloom.Features.System/Bluetooth/*.cs` (10 files), `src/Trayloom.Core/BluetoothState.cs`, `src/Trayloom.App/Shell/BluetoothMenu.cs` and `tests/Trayloom.System.Harness/BluetoothChecks.cs`.
- Edits: `FeatureCatalog.cs`, `Contributions.cs`, `AppSettings.cs`, `PanelLayout.cs`, `TrayPanel.cs`, `AppShell.cs`, `IslandModel.cs`, `Pages.Controls.cs`, `IslandWindow.Input.cs`, `IslandWindow.State.cs` (the `ModuleForActivity` rule), `AccessoryService.cs`, `Settings.Section.cs`, the System harness `Program.cs`, and optionally `BarCatalog.cs`.
- Contracts it provides:
  - `IBluetoothState` (Core).
  - `PanelTabs.Bluetooth = "bluetooth"`.
  - Module id `bluetooth`.
  - The rule "an external activity `owner.x` opens module `owner`".
- Verify: a full `dotnet build Trayloom.sln` (Core changed), the harness, then `--open-panel bluetooth` and `--open-settings island#bluetooth`.

**WP3 — GitHub while you code.**
- Files: new `src/Trayloom.Features.GitHub/Coding/*.cs` (10 files) and `tests/Trayloom.GitHub.Harness/{csproj,Program.cs}`.
- Edits: `GitHubFeature.cs`, `GitHubRuntime.cs` (one `CreateTransport()` member), `GitHubIsland.cs` (card insert and subscribe), and the GitHub `csproj` (`InternalsVisibleTo`).
- Contracts it consumes: `GitHubRuntime.Queue`, `IGhTransport.GraphQlAsync`, and WP2's `ModuleForActivity` rule.

**WP4 — Google Calendar.**
- Files: new `src/Trayloom.Features.Island/Calendar.GoogleRules.cs`, `Calendar.Ics.cs`, `Calendar.Google.cs`, `Calendar.GoogleWindow.cs`, `Calendar.GoogleSettings.cs` and `tests/Trayloom.Island.Harness/GoogleCalendarChecks.cs`.
- Edits: `CalendarService.cs`, `Pages.Calendar.cs`, `Settings.Content.cs`, the Island harness `Program.cs`, and optionally `ClosedViews.cs` (the strip Join capsule, done last).
- Contracts it provides: `CalEvent.JoinUrl` and `WebUrl`; `CalendarService.UsesWindows`, `UsesGoogle` and `Google`.
- No file overlaps with WP2's Island edits.

**WP5 — Docs, after WP1–WP4 merge.**
- `README.md`:
  - Replace the Calendar row at `:162` with: "| **Calendar** | Browse the month and your upcoming appointments, from Google Calendar or Windows Calendar. Join Google Meet and other video calls in one click. |"
  - After `:167` add: "| **Bluetooth** | See your Bluetooth devices and their battery in the island and the tray, turn Bluetooth on or off, and get one alert when a device runs low. |"
  - Append to the GitHub row at `:174`: " While you code, the island follows the repository in front of you: branch, changes, pull request and checks, with an alert when checks finish."
  - In the AI Agents section: "Claude Code’s pixel mascot opens a laptop and types on it in the island while Claude works, as it does in Claude Code, and waves when it needs you."
- Privacy section (after `:331`), two bullets:
  - "**Google Calendar (optional).** Nothing is sent until you add a calendar in Settings › Dynamic Island › Content › Calendar. Trayloom then downloads that calendar’s secret iCal address from calendar.google.com every 15 minutes (you can change this) and shows its events in the island. It is read-only: Trayloom never signs in to Google and never changes your calendar. The address works like a password, so Trayloom stores it, and a copy of the downloaded calendar, encrypted for your Windows account in `%APPDATA%\Trayloom\calendar`; removing the calendar deletes both. Resetting the secret address in Google Calendar cuts off access at once."
  - "**GitHub while you code** reads the title of the editor or terminal window in front, Claude Code’s session files and the `.git` folders of your projects, all on this PC, to know which repository you are working in. It runs read-only Git commands with optional locks turned off, so it never changes your repository. Only when GitHub is connected does it ask GitHub for that branch’s pull request and checks."
- `specs/09-bluetooth-coding-mascot-gcal.md`: this design.
- `TESTING.md`: the manual tests above.
- New `Loc.T` strings are picked up by the `scripts/l10n` workflow (`specs/L10N_TODO.md`).

**Open decisions for you:**
- `notch-bluetooth` default install: `true` here, versus `false` like Wi-Fi.
- "Keep agents in the island between prompts" defaults to on.
- `ms-actioncenter:controlcenter/bluetooth` must be checked on Windows 11 before keeping it.
- The `KSPROPSETID_BtAudio` direct connect for audio devices is left as a separate, later experimental package.

### Critical Files for Implementation
- `E:\REA TEST 1\trayloom\src\Trayloom.Features.Island\AccessoryService.cs`
- `E:\REA TEST 1\trayloom\src\Trayloom.Features.System\Wifi\WifiIsland.cs`
- `E:\REA TEST 1\trayloom\src\Trayloom.Features.GitHub\GitHubRuntime.cs`
- `E:\REA TEST 1\trayloom\src\Trayloom.Features.Agents\AgentsIsland.cs`
- `E:\REA TEST 1\trayloom\src\Trayloom.Features.Island\CalendarService.cs`