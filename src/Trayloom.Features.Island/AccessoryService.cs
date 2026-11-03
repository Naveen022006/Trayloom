// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Trayloom.Core;

namespace Trayloom.Features.Island;

/// <summary>Bluetooth accessory notices: "Connected" and a single low battery warning at 20 % (spec Part D 9).</summary>
internal sealed class AccessoryService : IDisposable
{
    private const string BatteryProp = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private const int Low = 20;
    private const int Recovery = 25;

    private sealed class Dev
    {
        public required string Id { get; init; }

        public string Name = "";
        public bool Connected;
        public int? Percent;
        public bool Low;
        public bool Seen;
        public bool Baseline;
        public uint? Major;
        public uint? Minor;
        public string Kind = "device";
        public DateTime BatteryRead = DateTime.MinValue;
    }

    private readonly IslandCore _core;
    private readonly Dictionary<string, Dev> _devices = [];
    private readonly List<(Notice Notice, DateTime Queued)> _pending = [];
    private readonly List<DeviceWatcher> _watchers = [];
    private DispatcherTimer? _present;
    private DispatcherTimer? _battery;
    private bool _enumerated;
    private DateTime _nextShow = DateTime.MinValue;

    public AccessoryService(IslandCore core) => _core = core;

    public bool Active => _watchers.Count > 0;

    public bool Wanted => _core.IslandOn && _core.Ctx.Runtime.IsAvailable("notch-accessories") && _core.App.Get<bool>(K.Accessories) && _core.Ctx.Runtime.IsAvailable("monitor-power");

    public void Start()
    {
        _core.Session.Changed += () => { if (!_core.Session.CanPresent) { Stop(true); } else { Sync(); } };
        Sync();
    }

    public void Sync()
    {
        if (!Wanted)
        {
            Stop(false);
            return;
        }

        if (Active)
        {
            return;
        }

        try
        {
            _enumerated = false;
            foreach (var selector in new[] { BluetoothDevice.GetDeviceSelectorFromPairingState(true), BluetoothLEDevice.GetDeviceSelectorFromPairingState(true) })
            {
                var watcher = DeviceInformation.CreateWatcher(selector, ["System.Devices.Aep.IsConnected", "System.Devices.Aep.DeviceAddress", BatteryProp], DeviceInformationKind.AssociationEndpoint);
                watcher.Added += (_, info) => _core.Ui.BeginInvoke(() => Safe(() => OnAdded(info)));
                watcher.Updated += (_, upd) => _core.Ui.BeginInvoke(() => Safe(() => OnUpdated(upd)));
                watcher.Removed += (_, upd) => _core.Ui.BeginInvoke(() => _devices.Remove(upd.Id));
                watcher.EnumerationCompleted += (_, _) => _core.Ui.BeginInvoke(() => _enumerated = true);
                watcher.Start();
                _watchers.Add(watcher);
            }
        }
        catch
        {
            Stop(false);
            return;
        }

        _battery = new DispatcherTimer(DispatcherPriority.Background, _core.Ui) { Interval = TimeSpan.FromMinutes(5) };
        _battery.Tick += (_, _) => _ = ReadBatteries();
        _battery.Start();
        _present = new DispatcherTimer(DispatcherPriority.Background, _core.Ui) { Interval = TimeSpan.FromSeconds(1) };
        _present.Tick += (_, _) => PresentNext();
        _present.Start();
    }

    private void Stop(bool keepEpisodes)
    {
        foreach (var w in _watchers)
        {
            try
            {
                if (w.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                {
                    w.Stop();
                }
            }
            catch
            {
            }
        }

        _watchers.Clear();
        _battery?.Stop();
        _present?.Stop();
        _pending.Clear();
        if (!keepEpisodes)
        {
            _devices.Clear();
        }
    }

    private static void Safe(Action a)
    {
        try
        {
            a();
        }
        catch
        {
        }
    }

    private static bool IsTrue(object? v) => v is bool b && b;

    private void OnAdded(DeviceInformation info)
    {
        if (!_devices.TryGetValue(info.Id, out var d))
        {
            d = new Dev { Id = info.Id, Name = info.Name ?? "" };
            _devices[info.Id] = d;
        }

        d.Name = info.Name ?? d.Name;
        d.Connected = info.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var c) && IsTrue(c);
        d.Baseline = !_enumerated && d.Connected;
        if (info.Properties.TryGetValue(BatteryProp, out var b) && b is byte pct)
        {
            Battery(d, pct);
        }

        d.Kind = KindByName(d.Name);
        if (d.Connected && _enumerated)
        {
            _ = Announce(d);
        }
    }

    private void OnUpdated(DeviceInformationUpdate upd)
    {
        if (!_devices.TryGetValue(upd.Id, out var d))
        {
            return;
        }

        if (upd.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var c))
        {
            var now = IsTrue(c);
            if (now && !d.Connected && !d.Baseline)
            {
                d.Connected = true;
                _ = Announce(d);
            }
            else if (!now)
            {
                d.Connected = false;
                d.Baseline = false;
            }
            else
            {
                d.Connected = true;
            }
        }

        if (upd.Properties.TryGetValue(BatteryProp, out var b) && b is byte pct)
        {
            Battery(d, pct);
        }
    }

    private async Task Announce(Dev d)
    {
        if (string.IsNullOrWhiteSpace(d.Name))
        {
            return;
        }

        try
        {
            if (!d.Id.Contains("BluetoothLE", StringComparison.OrdinalIgnoreCase))
            {
                var bt = await BluetoothDevice.FromIdAsync(d.Id);
                if (bt is not null)
                {
                    d.Major = (uint)bt.ClassOfDevice.MajorClass;
                    d.Minor = (uint)bt.ClassOfDevice.MinorClass;
                    if (d.Major is >= 1 and <= 3)
                    {
                        return;
                    }

                    d.Kind = KindByClass(d.Major.Value, d.Minor!.Value, d.Name);
                }
            }
        }
        catch
        {
        }

        Enqueue(new Notice
        {
            Event = NoticeEvents.Accessory,
            Glyph = GlyphFor(d.Kind),
            Title = Loc.T("Connected"),
            Detail = d.Name,
            Reaction = null,
        });
        _ = ReadBattery(d);
    }

    private void Battery(Dev d, int percent)
    {
        d.Percent = percent;
        d.BatteryRead = DateTime.Now;
        if (!d.Seen)
        {
            d.Seen = true;
            d.Low = percent <= Low;
            return;
        }

        if (!d.Low && percent <= Low)
        {
            d.Low = true;
            Enqueue(new Notice { Event = NoticeEvents.Accessory, Glyph = GlyphFor(d.Kind), Title = d.Name, Detail = Loc.F("Low battery · {0}%", percent), Level = percent / 100.0, Tint = System.Windows.Media.Color.FromRgb(0xFF, 0x9F, 0x0A) });
        }
        else if (d.Low && percent >= Recovery)
        {
            d.Low = false;
            _pending.RemoveAll(p => p.Notice.Title == d.Name && p.Notice.Level is not null);
        }
    }

    private async Task ReadBatteries()
    {
        foreach (var d in _devices.Values.Where(x => x.Connected).ToList())
        {
            await ReadBattery(d);
        }
    }

    private async Task ReadBattery(Dev d)
    {
        if ((DateTime.Now - d.BatteryRead).TotalSeconds < 15)
        {
            return;
        }

        try
        {
            var info = await DeviceInformation.CreateFromIdAsync(d.Id, [BatteryProp]);
            if (info.Properties.TryGetValue(BatteryProp, out var b) && b is byte pct)
            {
                Battery(d, pct);
                return;
            }

            if (d.Id.Contains("BluetoothLE", StringComparison.OrdinalIgnoreCase))
            {
                var le = await BluetoothLEDevice.FromIdAsync(d.Id);
                if (le is not null)
                {
                    var svc = await le.GetGattServicesForUuidAsync(GattServiceUuids.Battery);
                    var s = svc.Services.FirstOrDefault();
                    if (s is not null)
                    {
                        var ch = (await s.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel)).Characteristics.FirstOrDefault();
                        if (ch is not null)
                        {
                            var read = await ch.ReadValueAsync();
                            if (read.Status == GattCommunicationStatus.Success)
                            {
                                var reader = Windows.Storage.Streams.DataReader.FromBuffer(read.Value);
                                Battery(d, reader.ReadByte());
                            }
                        }
                    }
                }
            }
        }
        catch
        {
        }
    }

    private void Enqueue(Notice n)
    {
        if (_pending.Count >= 8)
        {
            _pending.RemoveAt(0);
        }

        _pending.Add((n, DateTime.Now));
    }

    private void PresentNext()
    {
        _pending.RemoveAll(p => (DateTime.Now - p.Queued).TotalSeconds > 30);
        if (_pending.Count == 0 || DateTime.Now < _nextShow || _core.Window is { Expanded: true } || (_core.Window?.CaptureUp ?? false))
        {
            return;
        }

        var (notice, _) = _pending[0];
        _pending.RemoveAt(0);
        _core.ShowNotice(notice);
        _nextShow = DateTime.Now.AddSeconds(4.1);
    }

    private static string KindByName(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("keyboard"))
        {
            return "keyboard";
        }

        if (n.Contains("mouse"))
        {
            return "mouse";
        }

        if (n.Contains("trackpad") || n.Contains("track pad"))
        {
            return "trackpad";
        }

        if (n.Contains("airpods") || n.Contains("headphone") || n.Contains("headset") || n.Contains("buds"))
        {
            return "audio";
        }

        return "device";
    }

    private static string KindByClass(uint major, uint minor, string name)
    {
        var byName = KindByName(name);
        if (byName != "device")
        {
            return byName;
        }

        return (major, minor) switch
        {
            (4, 4) => "mic",
            (4, 5) or (4, 7) or (4, 10) => "speaker",
            (4, 8) => "car",
            (4, 14) or (4, 15) => "tv",
            (4, 18) => "game",
            (4, 1) or (4, 2) or (4, 6) => "audio",
            (5, _) when (minor & 0x0F) is 1 or 2 => "game",
            (5, _) when (minor & 0x20) != 0 => (minor & 0x0F) == 5 ? "trackpad" : "mouse",
            (5, _) when (minor & 0x10) != 0 => "keyboard",
            (6, _) => "printer",
            (7, _) => "watch",
            (8, _) => "game",
            _ => "device",
        };
    }

    private static string GlyphFor(string kind) => kind switch
    {
        "audio" => G.Headphones,
        "keyboard" => G.Keyboard,
        "mouse" or "trackpad" => G.Mouse,
        "speaker" => G.Speakers,
        "game" => G.Gamepad,
        "printer" => G.Printer,
        "tv" => G.Tv,
        "mic" => G.Mic,
        _ => G.Bluetooth,
    };

    public void Dispose() => Stop(false);
}
