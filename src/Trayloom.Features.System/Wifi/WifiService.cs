// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.IO;
using System.Text.Json;
using Trayloom.Core;
using Windows.Devices.Radios;
using Windows.Devices.WiFi;
using Windows.Networking.Connectivity;
using Windows.Security.Credentials;

namespace Trayloom.Features.SystemTools.Wifi;

/// <summary>How a connect attempt ended (spec 3.7).</summary>
internal enum WifiConnectOutcome
{
    Success,
    InvalidCredential,
    NetworkNotAvailable,
    Timeout,
    Unsupported,
    AccessRevoked,
    Failed,
}

/// <summary>
/// Adapter, radio, connection, scan and connect for the Wi-Fi views (spec 3.3). Nothing is read before a view asks (the location prompt of
/// Windows appears on the first call), nothing scans in the background, and no network name is ever logged or stored. State is kept on the UI
/// thread as an immutable <see cref="WifiSnapshot"/>; every WinRT call runs off it with a timeout.
/// </summary>
internal sealed class WifiService : IDisposable
{
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    private readonly IFeatureContext _context;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<WiFiAdapter> _adapters = [];
    private WiFiAdapter? _adapter;
    private Radio? _radio;
    private WiFiAccessStatus _access = WiFiAccessStatus.Unspecified;
    private bool _initialized;
    private bool _disposed;
    private bool _refreshQueued;
    private DateTime _lastScan = DateTime.MinValue;

    public WifiService(IFeatureContext context) => _context = context;

    /// <summary>The latest picture. Read on the UI thread.</summary>
    public WifiSnapshot Snapshot { get; private set; } = new();

    /// <summary>Raised on the UI thread after <see cref="Snapshot"/> was replaced.</summary>
    public event Action? Changed;

    /// <summary>Subscribes the cheap network-status event; no Wi-Fi API is touched yet.</summary>
    public void Start()
    {
        NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
    }

    public void Dispose()
    {
        _disposed = true;
        NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged;
        if (_radio is not null)
        {
            _radio.StateChanged -= OnRadioChanged;
            _radio = null;
        }

        _adapter = null;
        _adapters = [];
    }

    /// <summary>
    /// Reads only whether a Wi-Fi radio exists and its state (no Wi-Fi names, so no location prompt) so the toggle action and the Command Bar know
    /// what to offer before any view opened. Does nothing once a view has loaded the full picture.
    /// </summary>
    public async Task PeekRadioAsync()
    {
        if (_disposed || Snapshot.Loaded)
        {
            return;
        }

        var state = await Task.Run(async () =>
        {
            try
            {
                if (await Radio.RequestAccessAsync().AsTask().WaitAsync(CallTimeout) != RadioAccessStatus.Allowed)
                {
                    return WifiRadioState.None;
                }

                var radio = (await Radio.GetRadiosAsync().AsTask().WaitAsync(CallTimeout)).FirstOrDefault(r => r.Kind == RadioKind.WiFi);
                return radio?.State switch
                {
                    RadioState.On => WifiRadioState.On,
                    RadioState.Off => WifiRadioState.Off,
                    RadioState.Disabled => WifiRadioState.Disabled,
                    _ => WifiRadioState.None,
                };
            }
            catch (Exception)
            {
                return WifiRadioState.None;
            }
        });
        if (!_disposed && !Snapshot.Loaded)
        {
            Apply(s => s with { Radio = state });
        }
    }

    /// <summary>True while some Wi-Fi profile has connectivity; cheap and name-free, so it is safe before a view loaded anything.</summary>
    public bool CheapConnected()
    {
        if (Snapshot.Loaded)
        {
            return Snapshot.Connected;
        }

        try
        {
            return NetworkInformation.GetConnectionProfiles().Any(p => p.IsWlanConnectionProfile && p.GetNetworkConnectivityLevel() != NetworkConnectivityLevel.None);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void OnNetworkStatusChanged(object? sender) => QueueRefresh();

    private void OnRadioChanged(Radio sender, object args) => QueueRefresh();

    /// <summary>Re-reads the connection and radio after Windows reported a change (only once a view has asked for data).</summary>
    private void QueueRefresh()
    {
        if (_disposed)
        {
            return;
        }

        _context.Ui.BeginInvoke(() =>
        {
            if (_disposed || !_initialized || _refreshQueued)
            {
                return;
            }

            _refreshQueued = true;
            WifiGuard.FireAndLog(async () =>
            {
                try
                {
                    // Windows raises a burst of events for one change; one read after the burst is enough.
                    await Task.Delay(400);
                    await RefreshAsync();
                }
                finally
                {
                    _refreshQueued = false;
                }
            }, _context.Log, "refresh after change");
        });
    }

    private void Apply(Func<WifiSnapshot, WifiSnapshot> change)
    {
        if (_disposed)
        {
            return;
        }

        Snapshot = change(Snapshot);
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------------------------------ reading

    /// <summary>Asks Windows for access and finds the adapter and radio, once per run.</summary>
    private async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        var found = await Task.Run(async () =>
        {
            WiFiAccessStatus access;
            try
            {
                access = await WiFiAdapter.RequestAccessAsync().AsTask().WaitAsync(CallTimeout);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                access = WiFiAccessStatus.DeniedBySystem;
            }

            IReadOnlyList<WiFiAdapter> adapters = [];
            try
            {
                adapters = await WiFiAdapter.FindAllAdaptersAsync().AsTask().WaitAsync(CallTimeout);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _context.Log.Warn($"[wifi] adapters failed: {ex.GetType().Name} 0x{ex.HResult:X8}");
            }

            Radio? radio = null;
            try
            {
                if (await Radio.RequestAccessAsync().AsTask().WaitAsync(CallTimeout) == RadioAccessStatus.Allowed)
                {
                    radio = (await Radio.GetRadiosAsync().AsTask().WaitAsync(CallTimeout)).FirstOrDefault(r => r.Kind == RadioKind.WiFi);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _context.Log.Warn($"[wifi] radios failed: {ex.GetType().Name} 0x{ex.HResult:X8}");
            }

            return (access, adapters, radio);
        });

        if (_disposed)
        {
            return;
        }

        _access = found.access;
        _adapters = found.adapters;
        _adapter = found.adapters.FirstOrDefault();
        _radio = found.radio;
        if (_radio is not null)
        {
            _radio.StateChanged += OnRadioChanged;
        }

        _initialized = true;
    }

    /// <summary>First read for a view: initialises when needed, then reads radio and connection (and scans when asked).</summary>
    public async Task LoadAsync(bool scan)
    {
        await RefreshAsync();
        if (scan)
        {
            await ScanAsync(false);
        }
    }

    /// <summary>Reads radio and connection.</summary>
    public async Task RefreshAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await InitializeAsync();
            if (_disposed)
            {
                return;
            }

            var adapters = _adapters;
            var radio = _radio;
            var read = await Task.Run(() => ReadAsync(adapters, radio));
            if (read.Adapter is not null)
            {
                _adapter = read.Adapter;
            }

            Apply(s => s with
            {
                Loaded = true,
                HasAdapter = adapters.Count > 0,
                Radio = read.Radio,
                Connection = read.Connection,
                ConnectedWithoutName = read.WithoutName,
                Access = _access.ToString(),
                LocationNeeded = adapters.Count > 0 && (_access != WiFiAccessStatus.Allowed || read.WithoutName) && read.Radio != WifiRadioState.Off,
            });
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<(WifiRadioState Radio, WifiConnection? Connection, bool WithoutName, WiFiAdapter? Adapter)> ReadAsync(IReadOnlyList<WiFiAdapter> adapters, Radio? radio)
    {
        var state = radio?.State switch
        {
            RadioState.On => WifiRadioState.On,
            RadioState.Off => WifiRadioState.Off,
            RadioState.Disabled => WifiRadioState.Disabled,
            _ => adapters.Count > 0 ? WifiRadioState.On : WifiRadioState.None,
        };

        foreach (var adapter in adapters)
        {
            try
            {
                var profile = await adapter.NetworkAdapter.GetConnectedProfileAsync().AsTask().WaitAsync(CallTimeout);
                if (profile is null || !profile.IsWlanConnectionProfile)
                {
                    continue;
                }

                var ssid = profile.WlanConnectionProfileDetails.GetConnectedSsid() ?? "";
                var bars = (int)(profile.GetSignalBars() ?? 0);
                var level = profile.GetNetworkConnectivityLevel() switch
                {
                    NetworkConnectivityLevel.InternetAccess => WifiLevel.Internet,
                    NetworkConnectivityLevel.ConstrainedInternetAccess => WifiLevel.SignInNeeded,
                    _ => WifiLevel.NoInternet,
                };
                return ssid.Length == 0
                    ? (state, null, true, adapter)
                    : (state, new WifiConnection(ssid, bars, level), false, adapter);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // UnauthorizedAccessException means the names are hidden: the connection is real, only its name is not shared.
                if (ex is UnauthorizedAccessException)
                {
                    return (state, null, true, adapter);
                }

                _context.Log.Warn($"[wifi] connection read failed: {ex.GetType().Name} 0x{ex.HResult:X8}");
            }
        }

        return (state, null, false, null);
    }

    /// <summary>Scans once and replaces the list. At most every 5 s unless <paramref name="force"/>.</summary>
    public async Task ScanAsync(bool force)
    {
        if (!force && DateTime.UtcNow - _lastScan < TimeSpan.FromSeconds(5))
        {
            return;
        }

        _lastScan = DateTime.UtcNow;
        await _gate.WaitAsync();
        try
        {
            await InitializeAsync();
            var adapter = _adapter;
            if (_disposed || adapter is null || Snapshot.Radio is WifiRadioState.Off or WifiRadioState.Disabled)
            {
                Apply(s => s with { Scanning = false, Scanned = true, Networks = [] });
                return;
            }

            Apply(s => s with { Scanning = true });
            var connected = Snapshot.Connection?.Ssid;
            var savedFirst = _context.App.Get("notchWifiShowSaved", true);
            var result = await Task.Run(async () =>
            {
                try
                {
                    await adapter.ScanAsync().AsTask().WaitAsync(ScanTimeout);
                    var saved = SavedNames();
                    var all = adapter.NetworkReport.AvailableNetworks
                        .Where(n => !n.IsWiFiDirect)
                        .Select(n => new WifiNetwork(
                            n.Ssid ?? "",
                            (int)n.SignalBars,
                            (int)Math.Round(n.NetworkRssiInDecibelMilliwatts),
                            WifiRules.SecurityOf(n.SecuritySettings.NetworkAuthenticationType),
                            saved.Contains(n.Ssid ?? ""),
                            n))
                        .ToList();
                    return (All: all, Denied: false);
                }
                catch (UnauthorizedAccessException)
                {
                    return (All: new List<WifiNetwork>(), Denied: true);
                }
            });

            if (_disposed)
            {
                return;
            }

            var shown = WifiRules.Arrange(result.All, connected, savedFirst);
            var named = result.All.Count(n => n.Ssid.Length > 0);
            var hidden = result.Denied || (result.All.Count > 0 && named == 0);
            _lastScanFound = (result.All.Count, shown.Count, shown.Count(n => n.Saved));
            Apply(s => s with
            {
                Scanning = false,
                Scanned = true,
                Networks = shown,
                LocationNeeded = s.LocationNeeded || hidden,
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _context.Log.Warn($"[wifi] scan failed: {ex.GetType().Name} 0x{ex.HResult:X8}");
            Apply(s => s with { Scanning = false, Scanned = true });
        }
        finally
        {
            _gate.Release();
        }
    }

    private (int Found, int Shown, int Saved) _lastScanFound;

    private static HashSet<string> SavedNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var profile in NetworkInformation.GetConnectionProfiles())
            {
                if (profile.IsWlanConnectionProfile)
                {
                    names.Add(profile.ProfileName);
                }
            }
        }
        catch (Exception)
        {
            // saved labels are a convenience; the list works without them
        }

        return names;
    }

    // ------------------------------------------------------------------------------------------ doing

    /// <summary>Switches the radio. Windows answers with an access status; anything but Allowed means it refused.</summary>
    public async Task<RadioAccessStatus> SetRadioAsync(bool on)
    {
        await InitializeAsync();
        var radio = _radio;
        if (radio is null)
        {
            return RadioAccessStatus.DeniedBySystem;
        }

        var status = await Task.Run(() => radio.SetStateAsync(on ? RadioState.On : RadioState.Off).AsTask().WaitAsync(CallTimeout));
        await RefreshAsync();
        return status;
    }

    /// <summary>Drops the current connection.</summary>
    public async Task DisconnectAsync()
    {
        var adapter = _adapter;
        if (adapter is null)
        {
            return;
        }

        await Task.Run(adapter.Disconnect);
        await Task.Delay(300);
        await RefreshAsync();
    }

    /// <summary>
    /// Joins <paramref name="network"/>. <paramref name="password"/> is handed to Windows at this moment only and never kept or logged.
    /// </summary>
    public async Task<WifiConnectOutcome> ConnectAsync(WifiNetwork network, string? password, bool automatic)
    {
        var adapter = _adapter;
        if (adapter is null || network.Native is not WiFiAvailableNetwork native || Snapshot.ConnectingSsid is not null)
        {
            return WifiConnectOutcome.Failed;
        }

        Apply(s => s with { ConnectingSsid = network.Ssid });
        var outcome = WifiConnectOutcome.Failed;
        try
        {
            var kind = automatic ? WiFiReconnectionKind.Automatic : WiFiReconnectionKind.Manual;
            var status = await Task.Run(async () =>
            {
                var result = password is null
                    ? await adapter.ConnectAsync(native, kind).AsTask().WaitAsync(ConnectTimeout)
                    : await adapter.ConnectAsync(native, kind, new PasswordCredential { Password = password }).AsTask().WaitAsync(ConnectTimeout);
                return result.ConnectionStatus;
            });
            outcome = status switch
            {
                WiFiConnectionStatus.Success => WifiConnectOutcome.Success,
                WiFiConnectionStatus.InvalidCredential => WifiConnectOutcome.InvalidCredential,
                WiFiConnectionStatus.NetworkNotAvailable => WifiConnectOutcome.NetworkNotAvailable,
                WiFiConnectionStatus.Timeout => WifiConnectOutcome.Timeout,
                WiFiConnectionStatus.UnsupportedAuthenticationProtocol => WifiConnectOutcome.Unsupported,
                WiFiConnectionStatus.AccessRevoked => WifiConnectOutcome.AccessRevoked,
                _ => WifiConnectOutcome.Failed,
            };
        }
        catch (TimeoutException)
        {
            outcome = WifiConnectOutcome.Timeout;
        }
        catch (UnauthorizedAccessException)
        {
            outcome = WifiConnectOutcome.AccessRevoked;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _context.Log.Warn($"[wifi] connect failed: {ex.GetType().Name} 0x{ex.HResult:X8}");
        }
        finally
        {
            Apply(s => s with { ConnectingSsid = null });
        }

        // The page may be gone by now: the result is logged without the network name.
        _context.Log.Info($"[wifi] connect: {outcome}");
        await RefreshAsync();
        return outcome;
    }

    // ------------------------------------------------------------------------------------------ developer aids

    /// <summary>One log line about the adapter, radio, access and connectivity (no names).</summary>
    public void LogState()
    {
        var s = Snapshot;
        _context.Log.Info($"[wifi] state: adapters={_adapters.Count}, adapter={(s.HasAdapter ? "yes" : "no")}, radio={s.Radio}, access={s.Access}, connected={(s.Connected ? "yes" : "no")}, level={(s.Connection?.Level.ToString() ?? "-")}, bars={(s.Connection?.Bars.ToString() ?? "-")}, locationNeeded={s.LocationNeeded}");
    }

    /// <summary>One log line about the last scan (counts only).</summary>
    public void LogScan()
    {
        var s = Snapshot;
        _context.Log.Info($"[wifi] scan: {_lastScanFound.Found} networks ({_lastScanFound.Shown} shown), saved={_lastScanFound.Saved}, access={s.Access}, radio={s.Radio}, connected={(s.Connected ? "yes" : "no")}");
    }

    /// <summary>Writes <c>dump.json</c> into the feature folder: the state fields and per network signal, security and saved flag, without names.</summary>
    public void Dump()
    {
        var s = Snapshot;
        var dump = new
        {
            adapters = _adapters.Count,
            hasAdapter = s.HasAdapter,
            radio = s.Radio.ToString(),
            access = s.Access,
            connected = s.Connected,
            level = s.Connection?.Level.ToString(),
            bars = s.Connection?.Bars,
            locationNeeded = s.LocationNeeded,
            scanned = s.Scanned,
            found = _lastScanFound.Found,
            networks = s.Networks.Select((n, i) => new { index = i, bars = n.Bars, rssi = n.Rssi, security = n.Security.ToString(), saved = n.Saved }).ToList(),
        };
        var path = Path.Combine(_context.DataDirectory, "dump.json");
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(dump, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
        _context.Log.Info($"[wifi] dump: {s.Networks.Count} networks written");
    }
}
