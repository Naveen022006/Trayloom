// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows.Threading;
using Trayloom.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace Trayloom.Features.SystemTools.Bluetooth;

// The display feature of this project has its own DeviceWatcher type, so the Windows one is named here.
using DeviceWatcher = Windows.Devices.Enumeration.DeviceWatcher;

/// <summary>
/// Paired Bluetooth devices, their connection, their battery and the radio, for the island page, the tray and the closed-island strips.
/// Every field is touched on the UI thread only: Windows callbacks hop there first, and each burst of them ends in one merge.
/// </summary>
/// <remarks>
/// The battery is not on the paired endpoint but on the PnP node of the device (the Hands-Free node of earbuds, the <c>BTHLE</c> node of
/// low-energy devices), so one watcher follows those nodes and the devices are merged by container. Logs carry counts and kinds, never addresses.
/// </remarks>
internal sealed class BluetoothService : IBluetoothState, IDisposable
{
    private const string BatteryProp = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private const string NodeQuery = "System.Devices.DeviceInstanceId:~<\"BTH\"";
    private const string IsConnectedProp = "System.Devices.Aep.IsConnected";
    private const string AddressProp = "System.Devices.Aep.DeviceAddress";
    private const string AepContainerProp = "System.Devices.Aep.ContainerId";
    private const string NodeContainerProp = "System.Devices.ContainerId";
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(10);
    private static readonly string[] AepProps = [IsConnectedProp, AddressProp, AepContainerProp];
    private static readonly string[] NodeProps = [BatteryProp, NodeContainerProp];

    /// <summary>One device watcher with what it takes to restart it.</summary>
    private sealed class Watch
    {
        public required string Name { get; init; }

        public required Func<DeviceWatcher> Create { get; init; }

        public required Action<Watch, DeviceWatcher> Hook { get; init; }

        public DeviceWatcher? Watcher { get; set; }

        public Action? Unhook { get; set; }

        public bool Enumerated { get; set; }

        public int Failures { get; set; }
    }

    private readonly IFeatureContext _context;
    private readonly Dictionary<string, AepRow> _aeps = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NodeRow> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (uint Major, uint Minor)> _class = new(StringComparer.Ordinal);
    private readonly HashSet<string> _classTried = new(StringComparer.Ordinal);
    private readonly Queue<(string Id, string Key)> _classQueue = new();
    private readonly Dictionary<string, (int Percent, DateTime At)> _gatt = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _gattTried = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _wasConnected = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _lastDisconnect = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _since = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BtLowState> _low = new(StringComparer.Ordinal);
    private readonly List<(BtNotice Notice, DateTime Queued)> _notices = [];
    private readonly List<Watch> _watches = [];
    private readonly DispatcherTimer _startTimer;
    private readonly DispatcherTimer _tick;
    private DispatcherTimer? _drain;
    private Radio? _radio;
    private BtRadio _radioState = BtRadio.Unknown;
    private bool _accessDenied;
    private bool _radioRead;
    private bool _loaded;
    private bool _disposed;
    private bool _mergeQueued;
    private bool _classBusy;
    private bool _refreshing;
    private int _attached;
    private DateTime _startedUtc;
    private DateTime _lastRefresh = DateTime.MinValue;
    private string _lowSignature = string.Empty;
    private (BtDevice Device, DateTime Until)? _simulatedLow;

    public BluetoothService(IFeatureContext context)
    {
        _context = context;
        _startTimer = new DispatcherTimer(DispatcherPriority.Background, context.Ui) { Interval = TimeSpan.FromSeconds(3) };
        _startTimer.Tick += (_, _) =>
        {
            _startTimer.Stop();
            Begin();
        };
        _tick = new DispatcherTimer(DispatcherPriority.Background, context.Ui) { Interval = TimeSpan.FromSeconds(10) };
        _tick.Tick += (_, _) => OnTick();
        Snapshot = new BtSnapshot(false, BtRadio.Unknown, false, []);
    }

    /// <summary>The latest picture. Read on the UI thread.</summary>
    public BtSnapshot Snapshot { get; private set; }

    /// <summary>The island context, set by the page and the strips when the island creates them; used to ask for a redraw.</summary>
    public IIslandContext? IslandContext { get; set; }

    /// <summary>Raised on the UI thread after <see cref="Snapshot"/> was replaced.</summary>
    public event Action? Changed;

    // ------------------------------------------------------------------------------------------ IBluetoothState

    public bool? IsOn => Snapshot.Radio switch
    {
        BtRadio.On => true,
        BtRadio.Off => false,
        _ => null,
    };

    public bool? HasAdapter => !Snapshot.Loaded ? null : Snapshot.Radio != BtRadio.None;

    public IReadOnlyList<BluetoothDeviceInfo> Devices => Visible().Select(d => new BluetoothDeviceInfo(d.Key, d.Name, d.Connected, d.Battery)).ToList();

    public void SetRadio(bool on) => BtGuard.FireAndLog(async () =>
    {
        var status = await SetRadioAsync(on);
        if (status != RadioAccessStatus.Allowed)
        {
            _context.Log.Warn($"[bluetooth] radio change refused: {status}");
        }
    }, _context.Log, "radio change");

    public void OpenBluetoothSettings() => BtKeys.OpenSettings();

    public void OpenAddDevice() => BtKeys.OpenAddDevice();

    public void OpenConnect() => BtKeys.OpenConnect();

    /// <summary>The devices the person chose to show, in list order.</summary>
    public IReadOnlyList<BtDevice> Visible() => BtRules.Visible(Snapshot.Devices, _context.App.Get(BtKeys.Show, "all"), BtKeys.HiddenSet(_context.App));

    // ------------------------------------------------------------------------------------------ lifetime

    /// <summary>Arms the start: nothing is asked of Windows for the first 3 seconds, so the app's own start-up is not slowed.</summary>
    public void Start()
    {
        _context.App.Changed += OnSettingChanged;
        _startTimer.Start();
    }

    public void Dispose()
    {
        _disposed = true;
        _context.App.Changed -= OnSettingChanged;
        _startTimer.Stop();
        _tick.Stop();
        _drain?.Stop();
        foreach (var watch in _watches)
        {
            StopWatch(watch);
        }

        _watches.Clear();
        if (_radio is not null)
        {
            _radio.StateChanged -= OnRadioChanged;
            _radio = null;
        }

        _notices.Clear();
        IslandContext = null;
    }

    private void Begin()
    {
        if (_disposed)
        {
            return;
        }

        _startedUtc = DateTime.UtcNow;
        BtGuard.FireAndLog(InitRadioAsync, _context.Log, "radio");
        _watches.Add(new Watch { Name = "classic", Create = () => CreateAepWatcher(BluetoothDevice.GetDeviceSelectorFromPairingState(true)), Hook = HookAep });
        _watches.Add(new Watch { Name = "low energy", Create = () => CreateAepWatcher(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)), Hook = HookAep });
        _watches.Add(new Watch { Name = "nodes", Create = () => DeviceInformation.CreateWatcher(NodeQuery, NodeProps, DeviceInformationKind.Device), Hook = HookNode });
        foreach (var watch in _watches)
        {
            StartWatch(watch);
        }

        _tick.Start();
    }

    private static DeviceWatcher CreateAepWatcher(string selector) => DeviceInformation.CreateWatcher(selector, AepProps, DeviceInformationKind.AssociationEndpoint);

    /// <summary>Views that are on screen register here; while one is, the battery is read again every 30 seconds.</summary>
    public void Attach()
    {
        _attached++;
        if (_attached == 1 && _loaded && (DateTime.UtcNow - _lastRefresh).TotalSeconds > 5)
        {
            BtGuard.FireAndLog(RefreshNodesAsync, _context.Log, "refresh on show");
        }
    }

    /// <summary>The counterpart of <see cref="Attach"/>.</summary>
    public void Detach() => _attached = Math.Max(0, _attached - 1);

    private void OnSettingChanged(string key)
    {
        if (key.StartsWith("notchBluetooth", StringComparison.Ordinal))
        {
            _context.Ui.BeginInvoke(ScheduleMerge);
        }
    }

    // ------------------------------------------------------------------------------------------ radio

    private async Task InitRadioAsync()
    {
        var found = await Task.Run(async () =>
        {
            var status = RadioAccessStatus.DeniedBySystem;
            Radio? radio = null;
            try
            {
                status = await Radio.RequestAccessAsync().AsTask().WaitAsync(CallTimeout);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _context.Log.Warn($"[bluetooth] radio access failed: {ex.GetType().Name} 0x{ex.HResult:X8}");
            }

            try
            {
                radio = (await Radio.GetRadiosAsync().AsTask().WaitAsync(CallTimeout)).FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _context.Log.Warn($"[bluetooth] radios failed: {ex.GetType().Name} 0x{ex.HResult:X8}");
            }

            return (status, radio);
        });

        if (_disposed)
        {
            return;
        }

        _accessDenied = found.status != RadioAccessStatus.Allowed;
        _radio = found.radio;
        if (_radio is not null)
        {
            _radio.StateChanged += OnRadioChanged;
            _radioState = MapRadio(_radio.State);
        }
        else
        {
            // Without access Windows cannot say whether an adapter exists, so the state stays unknown rather than claiming there is none.
            _radioState = _accessDenied ? BtRadio.Unknown : BtRadio.None;
        }

        _radioRead = true;
        CheckLoaded();
        Merge();
    }

    private static BtRadio MapRadio(RadioState state) => state switch
    {
        RadioState.On => BtRadio.On,
        RadioState.Off => BtRadio.Off,
        RadioState.Disabled => BtRadio.Disabled,
        _ => BtRadio.Unknown,
    };

    private void OnRadioChanged(Radio sender, object args) => _context.Ui.BeginInvoke(() =>
    {
        if (_disposed)
        {
            return;
        }

        _radioState = MapRadio(sender.State);
        ScheduleMerge();
    });

    /// <summary>Switches the radio. Windows answers with an access status; anything but Allowed means it refused.</summary>
    public async Task<RadioAccessStatus> SetRadioAsync(bool on)
    {
        var radio = _radio;
        if (radio is null)
        {
            return RadioAccessStatus.DeniedBySystem;
        }

        var status = await Task.Run(() => radio.SetStateAsync(on ? RadioState.On : RadioState.Off).AsTask().WaitAsync(CallTimeout));
        if (!_disposed)
        {
            _radioState = MapRadio(radio.State);
            ScheduleMerge();
        }

        return status;
    }

    // ------------------------------------------------------------------------------------------ watchers

    private void StartWatch(Watch watch)
    {
        try
        {
            watch.Enumerated = false;
            var watcher = watch.Create();
            watch.Watcher = watcher;
            watch.Hook(watch, watcher);
            watcher.Start();
        }
        catch (Exception ex)
        {
            _context.Log.Warn($"[bluetooth] watcher {watch.Name} did not start: {ex.GetType().Name} 0x{ex.HResult:X8}");
            watch.Enumerated = true;
            CheckLoaded();
        }
    }

    private static void StopWatch(Watch watch)
    {
        watch.Unhook?.Invoke();
        watch.Unhook = null;
        var watcher = watch.Watcher;
        watch.Watcher = null;
        try
        {
            if (watcher is { Status: DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted })
            {
                watcher.Stop();
            }
        }
        catch (Exception)
        {
            // already stopped by Windows
        }
    }

    private static object? Prop(IReadOnlyDictionary<string, object> properties, string key) => properties.TryGetValue(key, out var value) ? value : null;

    private void HookAep(Watch watch, DeviceWatcher watcher)
    {
        var le = watch.Name == "low energy";
        void Added(DeviceWatcher _, DeviceInformation info)
        {
            var row = new AepRow(info.Id, info.Name ?? string.Empty, Prop(info.Properties, IsConnectedProp) is true, Prop(info.Properties, AddressProp) as string, BtRules.ParseContainer(Prop(info.Properties, AepContainerProp)), le);
            _context.Ui.BeginInvoke(() =>
            {
                if (_disposed)
                {
                    return;
                }

                _aeps[row.Id] = row;
                MaybeLoadClass(row);
                ScheduleMerge();
            });
        }

        void Updated(DeviceWatcher _, DeviceInformationUpdate update)
        {
            var properties = update.Properties;
            _context.Ui.BeginInvoke(() =>
            {
                if (_disposed || !_aeps.TryGetValue(update.Id, out var row))
                {
                    return;
                }

                if (properties.TryGetValue(IsConnectedProp, out var connected))
                {
                    row = row with { Connected = connected is true };
                }

                if (properties.TryGetValue(AddressProp, out var address))
                {
                    row = row with { Address = address as string };
                }

                if (properties.TryGetValue(AepContainerProp, out var container))
                {
                    row = row with { Container = BtRules.ParseContainer(container) };
                }

                _aeps[row.Id] = row;
                ScheduleMerge();
            });
        }

        void Removed(DeviceWatcher _, DeviceInformationUpdate update) => _context.Ui.BeginInvoke(() =>
        {
            if (_aeps.Remove(update.Id))
            {
                ScheduleMerge();
            }
        });

        HookCommon(watch, watcher, Added, Updated, Removed);
    }

    private void HookNode(Watch watch, DeviceWatcher watcher)
    {
        void Added(DeviceWatcher _, DeviceInformation info)
        {
            var row = new NodeRow(info.Id, BtRules.ParseContainer(Prop(info.Properties, NodeContainerProp)), BtRules.ParseBattery(Prop(info.Properties, BatteryProp)));
            _context.Ui.BeginInvoke(() =>
            {
                if (_disposed)
                {
                    return;
                }

                _nodes[row.Id] = row;
                ScheduleMerge();
            });
        }

        void Updated(DeviceWatcher _, DeviceInformationUpdate update)
        {
            var properties = update.Properties;
            _context.Ui.BeginInvoke(() =>
            {
                if (_disposed || !_nodes.TryGetValue(update.Id, out var row))
                {
                    return;
                }

                if (properties.TryGetValue(BatteryProp, out var battery))
                {
                    row = row with { Battery = BtRules.ParseBattery(battery) };
                }

                if (properties.TryGetValue(NodeContainerProp, out var container))
                {
                    row = row with { Container = BtRules.ParseContainer(container) };
                }

                _nodes[row.Id] = row;
                ScheduleMerge();
            });
        }

        void Removed(DeviceWatcher _, DeviceInformationUpdate update) => _context.Ui.BeginInvoke(() =>
        {
            if (_nodes.Remove(update.Id))
            {
                ScheduleMerge();
            }
        });

        HookCommon(watch, watcher, Added, Updated, Removed);
    }

    private void HookCommon(
        Watch watch,
        DeviceWatcher watcher,
        Windows.Foundation.TypedEventHandler<DeviceWatcher, DeviceInformation> added,
        Windows.Foundation.TypedEventHandler<DeviceWatcher, DeviceInformationUpdate> updated,
        Windows.Foundation.TypedEventHandler<DeviceWatcher, DeviceInformationUpdate> removed)
    {
        Windows.Foundation.TypedEventHandler<DeviceWatcher, object> completed = (_, _) => _context.Ui.BeginInvoke(() =>
        {
            if (_disposed || !ReferenceEquals(watch.Watcher, watcher))
            {
                return;
            }

            watch.Enumerated = true;
            watch.Failures = 0;
            CheckLoaded();
            ScheduleMerge();
        });
        Windows.Foundation.TypedEventHandler<DeviceWatcher, object> stopped = (sender, _) => _context.Ui.BeginInvoke(() => OnWatchStopped(watch, sender));
        watcher.Added += added;
        watcher.Updated += updated;
        watcher.Removed += removed;
        watcher.EnumerationCompleted += completed;
        watcher.Stopped += stopped;
        watch.Unhook = () =>
        {
            watcher.Added -= added;
            watcher.Updated -= updated;
            watcher.Removed -= removed;
            watcher.EnumerationCompleted -= completed;
            watcher.Stopped -= stopped;
        };
    }

    /// <summary>A watcher that stops without being asked is started again once after 10 seconds, then every 5 minutes.</summary>
    private void OnWatchStopped(Watch watch, DeviceWatcher sender)
    {
        if (_disposed || !ReferenceEquals(watch.Watcher, sender))
        {
            return;
        }

        _context.Log.Warn("[bluetooth] watcher aborted");
        watch.Failures++;
        var delay = watch.Failures == 1 ? TimeSpan.FromSeconds(10) : TimeSpan.FromMinutes(5);
        BtGuard.FireAndLog(async () =>
        {
            await Task.Delay(delay);
            if (!_disposed && ReferenceEquals(watch.Watcher, sender))
            {
                StopWatch(watch);
                StartWatch(watch);
            }
        }, _context.Log, "watcher restart");
    }

    private void CheckLoaded()
    {
        if (!_loaded && _radioRead && _watches.Count > 0 && _watches.All(w => w.Enumerated))
        {
            _loaded = true;
        }
    }

    // ------------------------------------------------------------------------------------------ class of device

    /// <summary>Reads the class of device of a classic endpoint once, one at a time, so a device that does not answer cannot hold up the others.</summary>
    private void MaybeLoadClass(AepRow row)
    {
        if (row.Le)
        {
            return;
        }

        var key = BtRules.NormalizeAddress(row.Address) ?? BtRules.NormalizeAddress(row.Id);
        if (key is null || !_classTried.Add(key))
        {
            return;
        }

        _classQueue.Enqueue((row.Id, key));
        if (!_classBusy)
        {
            BtGuard.FireAndLog(DrainClassAsync, _context.Log, "class of device");
        }
    }

    private async Task DrainClassAsync()
    {
        _classBusy = true;
        try
        {
            while (!_disposed && _classQueue.Count > 0)
            {
                var (id, key) = _classQueue.Dequeue();
                var read = await Task.Run(async () =>
                {
                    BluetoothDevice? device = null;
                    try
                    {
                        device = await BluetoothDevice.FromIdAsync(id).AsTask().WaitAsync(CallTimeout);
                        return device is null ? ((uint, uint)?)null : ((uint)device.ClassOfDevice.MajorClass, (uint)device.ClassOfDevice.MinorClass);
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                    finally
                    {
                        device?.Dispose();
                    }
                });
                if (read is { } value && !_disposed)
                {
                    _class[key] = value;
                    ScheduleMerge();
                }
            }
        }
        finally
        {
            _classBusy = false;
        }
    }

    // ------------------------------------------------------------------------------------------ merging

    /// <summary>Windows reports a burst of changes for one event; one merge after the burst is enough.</summary>
    private void ScheduleMerge()
    {
        if (_disposed || _mergeQueued)
        {
            return;
        }

        _mergeQueued = true;
        _context.Ui.BeginInvoke(DispatcherPriority.Background, Merge);
    }

    private void Merge()
    {
        _mergeQueued = false;
        if (_disposed)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var merged = BtRules.Merge(_aeps.Values, _nodes.Values);
        var devices = new List<BtDevice>(merged.Count);
        foreach (var device in merged)
        {
            var kind = _class.TryGetValue(device.Key, out var cls) ? BtRules.KindFrom(cls.Major, cls.Minor, device.Name) : device.Kind;
            var battery = device.Connected ? device.Battery : null;
            if (device.Connected && battery is null && _gatt.TryGetValue(device.Key, out var gatt))
            {
                battery = gatt.Percent;
            }

            DateTime? since = null;
            if (device.Connected)
            {
                since = _since.TryGetValue(device.Key, out var first) ? first : (_since[device.Key] = now);
            }

            devices.Add(device with { Kind = kind, Battery = battery, ConnectedSinceUtc = since });
        }

        Snapshot = new BtSnapshot(_loaded, _radioState, _accessDenied, devices);
        TrackConnections(devices, now);
        TrackBatteries(devices);

        var signature = string.Join('|', LowDevices().Select(d => d.Key));
        if (signature != _lowSignature)
        {
            _lowSignature = signature;
            InvalidateIsland();
        }

        Changed?.Invoke();
    }

    /// <summary>Compares each device with how it was at the last merge: a connect after the first read becomes a notice, a disconnect is remembered.</summary>
    private void TrackConnections(List<BtDevice> devices, DateTime now)
    {
        var aepsReady = _watches.Count >= 2 && _watches.Take(2).All(w => w.Enumerated);
        var settled = aepsReady && _startedUtc != default && (now - _startedUtc).TotalSeconds > 5;
        foreach (var device in devices)
        {
            var was = _wasConnected.TryGetValue(device.Key, out var before) && before;
            if (device.Connected && !was)
            {
                if (settled)
                {
                    OnConnected(device, now);
                }
            }
            else if (!device.Connected && was)
            {
                _lastDisconnect[device.Key] = now;
                _since.Remove(device.Key);
                _low.Remove(device.Key);
                _gatt.Remove(device.Key);
            }

            _wasConnected[device.Key] = device.Connected;
        }

        if (_wasConnected.Count > devices.Count)
        {
            var present = devices.Select(d => d.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var key in _wasConnected.Keys.Where(k => !present.Contains(k)).ToList())
            {
                _wasConnected.Remove(key);
                _low.Remove(key);
                _since.Remove(key);
            }
        }
    }

    private void TrackBatteries(List<BtDevice> devices)
    {
        var threshold = BtKeys.Threshold(_context.App);
        var alerts = _context.App.Get(BtKeys.LowAlerts, true);
        var hidden = BtKeys.HiddenSet(_context.App);
        foreach (var device in devices)
        {
            if (!device.Connected || device.Battery is not { } percent)
            {
                continue;
            }

            _low.TryGetValue(device.Key, out var state);
            var crossed = BtRules.StepLow(ref state, percent, threshold);
            _low[device.Key] = state;
            if (crossed && alerts && !hidden.Contains(device.Key))
            {
                _context.Log.Info($"[bluetooth] low kind={device.Kind} battery={percent}");
                QueueNotice(new BtNotice(device.Kind, device.Name, BtStrings.LowBattery, percent, true));
            }
        }
    }

    // ------------------------------------------------------------------------------------------ connect notice

    private void OnConnected(BtDevice device, DateTime now)
    {
        if (_lastDisconnect.TryGetValue(device.Key, out var left) && (now - left).TotalSeconds < 10)
        {
            return;
        }

        var wantNotice = device.Kind is not (BtKind.Computer or BtKind.Phone)
            && _context.App.Get(BtKeys.ConnectAlerts, true)
            && !BtKeys.HiddenSet(_context.App).Contains(device.Key);
        BtGuard.FireAndLog(() => AnnounceAsync(device.Key, wantNotice), _context.Log, "connect");
    }

    /// <summary>Waits up to 5 seconds for a battery level (reading it again after 1.5 and 4 seconds), then queues the connect notice.</summary>
    private async Task AnnounceAsync(string key, bool wantNotice)
    {
        var start = DateTime.UtcNow;
        var first = false;
        var second = false;
        BtDevice? device;
        while (true)
        {
            device = Snapshot.Devices.FirstOrDefault(d => d.Key == key);
            if (_disposed || device is not { Connected: true })
            {
                return;
            }

            var elapsed = (DateTime.UtcNow - start).TotalSeconds;
            if (device.Battery is not null || elapsed >= 5)
            {
                break;
            }

            if (!first && elapsed >= 1.5)
            {
                first = true;
                await RefreshNodesAsync();
            }
            else if (!second && elapsed >= 4)
            {
                second = true;
                await RefreshNodesAsync();
            }
            else
            {
                await Task.Delay(250);
            }
        }

        _context.Log.Info($"[bluetooth] connect kind={device.Kind} battery={(device.Battery is { } p ? p.ToString(System.Globalization.CultureInfo.InvariantCulture) : "none")}");
        if (wantNotice)
        {
            QueueNotice(new BtNotice(device.Kind, device.Name, string.Empty, device.Battery, false));
        }
    }

    // ------------------------------------------------------------------------------------------ battery reads

    private void OnTick()
    {
        if (_disposed)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (!_loaded && _startedUtc != default && (now - _startedUtc).TotalSeconds > 10)
        {
            // A watcher that never finishes must not leave the pages on their spinner for good.
            _loaded = true;
            ScheduleMerge();
        }

        if (_simulatedLow is { } simulated && now >= simulated.Until)
        {
            _simulatedLow = null;
            ScheduleMerge();
        }

        var anyConnected = Snapshot.Devices.Any(d => d.Connected);
        var interval = _attached > 0 ? 30 : anyConnected ? 120 : 0;
        if (interval > 0 && Snapshot.Radio is not (BtRadio.Off or BtRadio.Disabled or BtRadio.None) && (now - _lastRefresh).TotalSeconds >= interval - 5)
        {
            BtGuard.FireAndLog(RefreshNodesAsync, _context.Log, "battery refresh");
        }
    }

    /// <summary>
    /// Reads every Bluetooth node again, in case a battery change was missed, then asks the low-energy devices that report no node battery
    /// (at most once per 10 minutes each).
    /// </summary>
    public async Task RefreshNodesAsync()
    {
        if (_refreshing || _disposed)
        {
            return;
        }

        _refreshing = true;
        try
        {
            _lastRefresh = DateTime.UtcNow;
            var rows = await Task.Run(async () =>
            {
                var found = await DeviceInformation.FindAllAsync(NodeQuery, NodeProps, DeviceInformationKind.Device).AsTask().WaitAsync(CallTimeout);
                return found.Select(i => new NodeRow(i.Id, BtRules.ParseContainer(Prop(i.Properties, NodeContainerProp)), BtRules.ParseBattery(Prop(i.Properties, BatteryProp)))).ToList();
            });
            if (_disposed)
            {
                return;
            }

            foreach (var row in rows)
            {
                _nodes[row.Id] = row;
            }

            ScheduleMerge();
            await GattFallbackAsync();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async Task GattFallbackAsync()
    {
        var now = DateTime.UtcNow;
        foreach (var device in Snapshot.Devices.Where(d => d.Connected).ToList())
        {
            if (device.Battery is not null && !_gatt.ContainsKey(device.Key))
            {
                continue;
            }

            if (_gattTried.TryGetValue(device.Key, out var tried) && (now - tried).TotalMinutes < 10)
            {
                continue;
            }

            var leId = device.AepIds.FirstOrDefault(id => _aeps.TryGetValue(id, out var row) && row.Le);
            if (leId is null)
            {
                continue;
            }

            _gattTried[device.Key] = now;
            if (await Task.Run(() => ReadGattAsync(leId)) is { } percent && !_disposed)
            {
                _gatt[device.Key] = (percent, DateTime.UtcNow);
                ScheduleMerge();
            }
        }
    }

    /// <summary>The standard battery service (0x180F), for low-energy devices Windows reports no battery for.</summary>
    private static async Task<int?> ReadGattAsync(string id)
    {
        BluetoothLEDevice? device = null;
        try
        {
            device = await BluetoothLEDevice.FromIdAsync(id).AsTask().WaitAsync(CallTimeout);
            if (device is null)
            {
                return null;
            }

            var services = await device.GetGattServicesForUuidAsync(GattServiceUuids.Battery).AsTask().WaitAsync(CallTimeout);
            var service = services.Services.FirstOrDefault();
            if (service is null)
            {
                return null;
            }

            try
            {
                var characteristics = await service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel).AsTask().WaitAsync(CallTimeout);
                var characteristic = characteristics.Characteristics.FirstOrDefault();
                if (characteristic is null)
                {
                    return null;
                }

                var read = await characteristic.ReadValueAsync().AsTask().WaitAsync(CallTimeout);
                return read.Status == GattCommunicationStatus.Success ? BtRules.ParseBattery(Windows.Storage.Streams.DataReader.FromBuffer(read.Value).ReadByte()) : null;
            }
            finally
            {
                service.Dispose();
            }
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            device?.Dispose();
        }
    }

    // ------------------------------------------------------------------------------------------ island notices and strip

    /// <summary>The devices the closed island flags as low: connected, at or under the warning level, not hidden, and only while the radio is on.</summary>
    public IReadOnlyList<BtDevice> LowDevices()
    {
        var list = new List<BtDevice>();
        if (_context.App.Get(BtKeys.LowStrip, true) && Snapshot.Radio == BtRadio.On)
        {
            var threshold = BtKeys.Threshold(_context.App);
            var hidden = BtKeys.HiddenSet(_context.App);
            list.AddRange(Snapshot.Devices.Where(d => d.Connected && d.Battery is { } b && b <= threshold && !hidden.Contains(d.Key)));
        }

        if (_simulatedLow is { } simulated && !list.Any(d => d.Key == simulated.Device.Key))
        {
            list.Add(simulated.Device);
        }

        return list.OrderBy(d => d.Battery ?? 101).ToList();
    }

    /// <summary>Takes the oldest notice that is still fresh (30 seconds); the next one is offered 7.5 seconds later.</summary>
    public BtNotice? TakeNotice()
    {
        _notices.RemoveAll(n => (DateTime.UtcNow - n.Queued).TotalSeconds > 30);
        if (_notices.Count == 0)
        {
            return null;
        }

        var notice = _notices[0].Notice;
        _notices.RemoveAt(0);
        if (_notices.Count > 0)
        {
            StartDrain();
        }

        return notice;
    }

    private void QueueNotice(BtNotice notice)
    {
        _notices.RemoveAll(n => (DateTime.UtcNow - n.Queued).TotalSeconds > 30);
        while (_notices.Count >= 4)
        {
            _notices.RemoveAt(0);
        }

        _notices.Add((notice, DateTime.UtcNow));
        InvalidateIsland();
        StartDrain();
    }

    /// <summary>
    /// Keeps asking the island for the waiting notices while some are left. The island spaces external notices at least 7 seconds apart and
    /// skips one that comes too soon, so without this a notice queued behind another could wait for an unrelated redraw.
    /// </summary>
    private void StartDrain()
    {
        if (_drain is null)
        {
            _drain = new DispatcherTimer(DispatcherPriority.Background, _context.Ui) { Interval = TimeSpan.FromSeconds(7.5) };
            _drain.Tick += (_, _) =>
            {
                _notices.RemoveAll(n => (DateTime.UtcNow - n.Queued).TotalSeconds > 30);
                if (_notices.Count == 0 || _disposed)
                {
                    _drain?.Stop();
                    return;
                }

                InvalidateIsland();
            };
        }

        if (!_drain.IsEnabled)
        {
            _drain.Start();
        }
    }

    private void InvalidateIsland() => _context.Ui.BeginInvoke(() =>
    {
        if (!_disposed)
        {
            IslandContext?.Invalidate();
        }
    });

    // ------------------------------------------------------------------------------------------ developer aids

    /// <summary>Queues a connect notice for the first connected device, or for made-up headphones at 64 %.</summary>
    public void SimulateConnect()
    {
        var device = Snapshot.Devices.FirstOrDefault(d => d.Connected);
        QueueNotice(device is null
            ? new BtNotice(BtKind.Headphones, "Test headphones", string.Empty, 64, false)
            : new BtNotice(device.Kind, device.Name, string.Empty, device.Battery, false));
    }

    /// <summary>Queues a low-battery notice at 12 % and shows the low strip for a minute.</summary>
    public void SimulateLow()
    {
        var device = Snapshot.Devices.FirstOrDefault(d => d.Connected)
            ?? new BtDevice("simulated", "Test headphones", BtKind.Headphones, true, null, null, [], DateTime.UtcNow);
        _simulatedLow = (device with { Battery = 12 }, DateTime.UtcNow.AddMinutes(1));
        QueueNotice(new BtNotice(device.Kind, device.Name, BtStrings.LowBattery, 12, true));
        ScheduleMerge();
    }

    /// <summary>Writes counts and kinds to the log, never names or addresses.</summary>
    public void LogDevices()
    {
        var s = Snapshot;
        var kinds = string.Join(',', s.Devices.GroupBy(d => d.Kind).Select(g => $"{g.Key}={g.Count()}"));
        _context.Log.Info($"[bluetooth] radio={s.Radio} loaded={s.Loaded} denied={s.AccessDenied} devices={s.Devices.Count} connected={s.Devices.Count(d => d.Connected)} withBattery={s.Devices.Count(d => d.Battery is not null)} kinds={kinds}");
    }
}
