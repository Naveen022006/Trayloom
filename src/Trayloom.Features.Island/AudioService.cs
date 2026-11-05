// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using Trayloom.Core;

namespace Trayloom.Features.Island;

[ComImport]
[Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfigIsland
{
    [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out nint format);

    [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool isDefault, out nint format);

    [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, nint endpointFormat, nint mixFormat);

    [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool isDefault, nint defaultPeriod, nint minimumPeriod);

    [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, nint period);

    [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, nint mode);

    [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, nint mode);

    [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool bFxStore, nint key, nint value);

    [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool bFxStore, nint key, nint value);

    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);

    [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool visible);
}

[ComImport]
[Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
internal class PolicyConfigClientIsland
{
}

internal sealed record AudioEndpointInfo(string Id, string Name, bool IsDefault, string Kind);

/// <summary>An app with an audio session (merged by executable).</summary>
internal sealed class AppAudio
{
    public required string Key { get; init; }

    public string Name { get; set; } = "";

    public ImageSource? Icon { get; set; }

    public double Volume { get; set; }

    public bool Muted { get; set; }

    public bool Playing { get; set; }

    public int Pid { get; set; }

    public List<AudioSessionControl> Sessions { get; } = [];
}

/// <summary>Core Audio through NAudio: endpoint volume, devices, microphone and per-app sessions. UI-thread object.</summary>
internal sealed class AudioService : IDisposable
{
    private sealed class NotifyClient(AudioService owner) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => owner.DevicesTick();

        public void OnDeviceAdded(string pwstrDeviceId) => owner.DevicesTick();

        public void OnDeviceRemoved(string deviceId) => owner.DevicesTick();

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (role == Role.Multimedia)
            {
                owner.DevicesTick();
            }
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
        {
        }
    }

    private readonly Dispatcher _ui;
    private readonly MMDeviceEnumerator _enum = new();
    private readonly NotifyClient _client;
    private readonly Dictionary<string, ImageSource?> _icons = [];
    private MMDevice? _render;
    private MMDevice? _capture;
    private bool _disposed;
    private DateTime _lastOwnChange = DateTime.MinValue;
    private string? _lastError;

    public AudioService(Dispatcher ui)
    {
        _ui = ui;
        _client = new NotifyClient(this);
        try
        {
            _enum.RegisterEndpointNotificationCallback(_client);
        }
        catch
        {
        }

        Bind();
    }

    public event Action? VolumeChanged;

    public event Action? DevicesChanged;

    public event Action? MicChanged;

    public string? LastError => _lastError;

    public bool Available => _render is not null;

    /// <summary>Volume changes made by the island's own controls do not show a notice.</summary>
    public bool RecentlyOwnChange => (DateTime.UtcNow - _lastOwnChange).TotalMilliseconds < 600;

    private void DevicesTick() => _ui.BeginInvoke(() =>
    {
        if (_disposed)
        {
            return;
        }

        Bind();
        DevicesChanged?.Invoke();
        VolumeChanged?.Invoke();
    });

    private void Bind()
    {
        try
        {
            if (_render is not null)
            {
                _render.AudioEndpointVolume.OnVolumeNotification -= OnVolume;
            }
        }
        catch
        {
        }

        try
        {
            _render = _enum.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) ? _enum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : null;
            if (_render is not null)
            {
                _render.AudioEndpointVolume.OnVolumeNotification += OnVolume;
            }
        }
        catch
        {
            _render = null;
        }

        try
        {
            if (_capture is not null)
            {
                _capture.AudioEndpointVolume.OnVolumeNotification -= OnMic;
            }
        }
        catch
        {
        }

        try
        {
            _capture = _enum.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Communications) ? _enum.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications) : null;
            if (_capture is not null)
            {
                _capture.AudioEndpointVolume.OnVolumeNotification += OnMic;
            }
        }
        catch
        {
            _capture = null;
        }
    }

    private void OnVolume(AudioVolumeNotificationData data) => _ui.BeginInvoke(() => VolumeChanged?.Invoke());

    private void OnMic(AudioVolumeNotificationData data) => _ui.BeginInvoke(() => MicChanged?.Invoke());

    // ---------------------------------------------------------------- master

    public double? Volume
    {
        get
        {
            try
            {
                return _render?.AudioEndpointVolume.MasterVolumeLevelScalar;
            }
            catch
            {
                return null;
            }
        }
    }

    public bool Muted
    {
        get
        {
            try
            {
                return _render?.AudioEndpointVolume.Mute ?? false;
            }
            catch
            {
                return false;
            }
        }
    }

    public string DefaultName
    {
        get
        {
            try
            {
                return _render?.FriendlyName ?? Strings.OutputUnavailable;
            }
            catch
            {
                return Strings.OutputUnavailable;
            }
        }
    }

    public void SetVolume(double value)
    {
        try
        {
            _lastOwnChange = DateTime.UtcNow;
            if (_render is null)
            {
                return;
            }

            _render.AudioEndpointVolume.MasterVolumeLevelScalar = (float)Math.Clamp(value, 0, 1);
            if (value > 0.0001 && _render.AudioEndpointVolume.Mute)
            {
                _render.AudioEndpointVolume.Mute = false;
            }
        }
        catch
        {
        }
    }

    public void SetMuted(bool muted)
    {
        try
        {
            _lastOwnChange = DateTime.UtcNow;
            if (_render is not null)
            {
                _render.AudioEndpointVolume.Mute = muted;
            }
        }
        catch
        {
        }
    }

    // ---------------------------------------------------------------- devices

    public List<AudioEndpointInfo> Outputs() => List(DataFlow.Render);

    public List<AudioEndpointInfo> Inputs() => List(DataFlow.Capture);

    private List<AudioEndpointInfo> List(DataFlow flow)
    {
        var result = new List<AudioEndpointInfo>();
        try
        {
            string? defId = null;
            try
            {
                defId = _enum.HasDefaultAudioEndpoint(flow, Role.Multimedia) ? _enum.GetDefaultAudioEndpoint(flow, Role.Multimedia).ID : null;
            }
            catch
            {
            }

            foreach (var d in _enum.EnumerateAudioEndPoints(flow, DeviceState.Active))
            {
                string kind = "";
                try
                {
                    kind = d.Properties.Contains(PropertyKeys.PKEY_AudioEndpoint_FormFactor) ? d.Properties[PropertyKeys.PKEY_AudioEndpoint_FormFactor].Value?.ToString() ?? "" : "";
                }
                catch
                {
                }

                result.Add(new AudioEndpointInfo(d.ID, d.FriendlyName, d.ID == defId, kind));
            }
        }
        catch
        {
        }

        return result;
    }

    /// <summary>Glyph for an output by form factor (headphones 3, headset 5, speakers 1, HDMI/SPDIF 8/9...).</summary>
    public static string GlyphFor(AudioEndpointInfo? e) => e?.Kind switch
    {
        "3" or "5" => G.Headphones,
        "1" => G.Speakers,
        "8" or "9" => G.Tv,
        _ => G.Speakers,
    };

    public bool SetDefaultOutput(string id)
    {
        try
        {
            var cfg = (IPolicyConfigIsland)new PolicyConfigClientIsland();
            foreach (var role in new[] { 0, 1, 2 })
            {
                cfg.SetDefaultEndpoint(id, role);
            }

            _lastError = null;
            return true;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            return false;
        }
    }

    // ---------------------------------------------------------------- microphone

    public bool MicAvailable => _capture is not null;

    public double? MicVolume
    {
        get
        {
            try
            {
                return _capture?.AudioEndpointVolume.MasterVolumeLevelScalar;
            }
            catch
            {
                return null;
            }
        }
    }

    public bool MicMuted
    {
        get
        {
            try
            {
                return _capture?.AudioEndpointVolume.Mute ?? false;
            }
            catch
            {
                return false;
            }
        }
    }

    public void SetMicVolume(double v)
    {
        try
        {
            if (_capture is not null)
            {
                _capture.AudioEndpointVolume.MasterVolumeLevelScalar = (float)Math.Clamp(v, 0, 1);
            }
        }
        catch
        {
        }
    }

    public void SetMicMuted(bool muted)
    {
        try
        {
            if (_capture is not null)
            {
                _capture.AudioEndpointVolume.Mute = muted;
            }
        }
        catch
        {
        }
    }

    // ---------------------------------------------------------------- app sessions

    /// <summary>Audio sessions of the default output merged by executable.</summary>
    private readonly Dictionary<int, (string Key, string Name, string? Path, long At)> _identity = [];

    public List<AppAudio> Apps()
    {
        var result = new Dictionary<string, AppAudio>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (_render is null)
            {
                return [];
            }

            var sessions = _render.AudioSessionManager.Sessions;
            for (var i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                try
                {
                    if (s.IsSystemSoundsSession)
                    {
                        continue;
                    }

                    var pid = (int)s.GetProcessID;
                    string key, name;
                    string? path = null;
                    var stamp = Environment.TickCount64;
                    if (_identity.TryGetValue(pid, out var known) && stamp - known.At < 60_000)
                    {
                        // process lookups (module path, file version) cost milliseconds per session: remembered per pid
                        (key, name, path) = (known.Key, known.Name, known.Path);
                    }
                    else
                    {
                        try
                        {
                            using var p = Process.GetProcessById(pid);
                            path = p.MainModule?.FileName;
                            name = p.ProcessName;
                            key = path ?? name;
                            if (!string.IsNullOrEmpty(path))
                            {
                                try
                                {
                                    var desc = FileVersionInfo.GetVersionInfo(path).FileDescription;
                                    if (!string.IsNullOrWhiteSpace(desc))
                                    {
                                        name = desc;
                                    }
                                }
                                catch
                                {
                                }
                            }
                        }
                        catch
                        {
                            key = s.GetSessionIdentifier ?? ("pid" + pid);
                            name = string.IsNullOrWhiteSpace(s.DisplayName) ? key : s.DisplayName;
                        }

                        _identity[pid] = (key, name, path, stamp);
                    }

                    if (!result.TryGetValue(key, out var app))
                    {
                        if (!_icons.TryGetValue(key, out var icon))
                        {
                            icon = path is not null ? PlaybackService.IconOf(path) : null;
                            _icons[key] = icon;
                        }

                        app = new AppAudio { Key = key, Name = name, Icon = icon, Pid = pid };
                        result[key] = app;
                    }

                    app.Sessions.Add(s);
                    var vol = s.SimpleAudioVolume;
                    app.Volume = Math.Max(app.Volume, vol.Volume);
                    app.Muted = app.Muted || vol.Mute;
                    app.Playing = app.Playing || s.State == AudioSessionState.AudioSessionStateActive && s.AudioMeterInformation.MasterPeakValue > 0.001f;
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        return [.. result.Values];
    }

    public static void SetAppVolume(AppAudio app, double value)
    {
        foreach (var s in app.Sessions)
        {
            try
            {
                s.SimpleAudioVolume.Volume = (float)Math.Clamp(value, 0, 1);
                if (value > 0.0001)
                {
                    s.SimpleAudioVolume.Mute = false;
                }
            }
            catch
            {
            }
        }
    }

    public static void SetAppMuted(AppAudio app, bool muted)
    {
        foreach (var s in app.Sessions)
        {
            try
            {
                s.SimpleAudioVolume.Mute = muted;
            }
            catch
            {
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        try
        {
            _enum.UnregisterEndpointNotificationCallback(_client);
        }
        catch
        {
        }

        try
        {
            if (_render is not null)
            {
                _render.AudioEndpointVolume.OnVolumeNotification -= OnVolume;
            }

            if (_capture is not null)
            {
                _capture.AudioEndpointVolume.OnVolumeNotification -= OnMic;
            }
        }
        catch
        {
        }

        _enum.Dispose();
    }
}

/// <summary>
/// Panel brightness through WMI (late bound, no package needed). Works for built-in panels. WMI scripting objects give every
/// instance its own dispatch ids, so properties are read through <c>Properties_</c> and methods called through IDispatch by name:
/// the C# dynamic binder would cache the ids of the first object and fail on every later call.
/// </summary>
internal static class BrightnessService
{
    private static readonly object Gate = new();
    private static readonly object CacheGate = new();
    private static readonly object LogGate = new();
    private static readonly Dictionary<string, DateTime> FailureLogged = [];
    private static bool? _available;
    private static int _probing;
    private static double? _pending;
    private static bool _writing;
    private static double _lastKnown = double.NaN;
    private static long _lastOwnWriteTicks;
    private static int _probeLogged;
    private static int _firstWriteLogged;

    // One WMI connection and the method objects are kept while the slider is being dragged: a write takes ~5 ms instead of ~15 ms.
    private static object? _writeLocator;
    private static object? _writeService;
    private static List<object>? _writeTargets;
    private static long _writeStamp;

    /// <summary>
    /// Whether the built-in panel can be controlled. The answer comes from a background check (WMI must not be called on the
    /// island's UI thread: COM refuses calls made while a window message is being handled), and only a check that ran and found
    /// no panel says "no"; a failed check is retried. Until the first answer the slider is offered.
    /// </summary>
    public static bool Available
    {
        get
        {
            if (_available is null)
            {
                Probe();
            }

            return _available ?? true;
        }
    }

    /// <summary>Raised (on a worker thread) when the background check changes the answer.</summary>
    public static event Action? AvailabilityChanged;

    /// <summary>The panel level changed (0..1), whoever changed it; raised on the UI thread by the island's brightness watcher.</summary>
    public static event Action<double>? LevelChanged;

    /// <summary>When the island itself last set the level (the island's own changes do not move its sliders).</summary>
    public static DateTime LastOwnWrite => new(Interlocked.Read(ref _lastOwnWriteTicks), DateTimeKind.Utc);

    /// <summary>Where failures and the one-off probe / first-write lines are logged (set by the island feature; may be called on any thread).</summary>
    public static Action<string, Exception?>? Log { get; set; }

    /// <summary>
    /// The newest panel level (0..1) seen from a read, a notification or a write, or null before the first one. Sliders start from it
    /// so they never open empty and animate up.
    /// </summary>
    public static double? LastKnown
    {
        get
        {
            var v = Volatile.Read(ref _lastKnown);
            return double.IsNaN(v) ? null : v;
        }
    }

    public static void Announce(double level)
    {
        Remember(level);
        LevelChanged?.Invoke(level);
    }

    private static void Remember(double level) => Volatile.Write(ref _lastKnown, Math.Clamp(level, 0, 1));

    public static void Probe()
    {
        if (Interlocked.Exchange(ref _probing, 1) == 1)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                double? level = null;
                var ran = ForEach("probe", "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active=TRUE", item => level ??= Level(item));
                bool? answer = ran ? level is not null : null;
                if (level is { } known)
                {
                    Remember(known);
                }

                if (answer is not null && Interlocked.Exchange(ref _probeLogged, 1) == 0)
                {
                    Log?.Invoke(level is { } l ? $"Island brightness probe: built-in panel found at {(int)Math.Round(l * 100)}%" : "Island brightness probe: no controllable built-in panel", null);
                }

                if (answer is not null && answer != _available)
                {
                    _available = answer;
                    DropWriteCache();
                    AvailabilityChanged?.Invoke();
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke("Island brightness probe failed", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _probing, 0);
            }
        });
    }

    public static double? Read()
    {
        double? result = null;
        var ran = ForEach("read", "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active=TRUE", item => result ??= Level(item));
        if (result is { } known)
        {
            Remember(known);
        }

        if (ran && _available != (result is not null))
        {
            _available = result is not null;
            DropWriteCache();
            AvailabilityChanged?.Invoke();
        }

        return result;
    }

    private static double Level(object item)
    {
        dynamic d = item;
        return Convert.ToInt32(d.Properties_.Item("CurrentBrightness").Value, System.Globalization.CultureInfo.InvariantCulture) / 100.0;
    }

    /// <summary>The lowest level the island writes (5 %), so a slider drag to the end can never turn the panel black.</summary>
    public const double MinimumLevel = 0.05;

    /// <summary>Keeps a requested level between <see cref="MinimumLevel"/> and 1; NaN becomes the minimum.</summary>
    internal static double ClampLevel(double value) => double.IsNaN(value) ? MinimumLevel : Math.Clamp(value, MinimumLevel, 1);

    /// <summary>Sets the level off the UI thread; while a write runs, only the newest requested level is kept (dragging stays smooth).</summary>
    public static void Write(double value)
    {
        value = ClampLevel(value);
        Interlocked.Exchange(ref _lastOwnWriteTicks, DateTime.UtcNow.Ticks);
        BrightnessWrites.MarkOwn();
        lock (Gate)
        {
            _pending = value;
            if (_writing)
            {
                return;
            }

            _writing = true;
        }

        _ = Task.Run(() =>
        {
            try
            {
                while (true)
                {
                    double next;
                    lock (Gate)
                    {
                        if (_pending is not { } p)
                        {
                            _writing = false;
                            return;
                        }

                        next = p;
                        _pending = null;
                    }

                    var percent = (byte)Math.Clamp(Math.Round(next * 100), 0, 100);
                    var started = Stopwatch.GetTimestamp();
                    if (SetPanel(percent))
                    {
                        Remember(percent / 100.0);
                        Interlocked.Exchange(ref _lastOwnWriteTicks, DateTime.UtcNow.Ticks);
                        BrightnessWrites.MarkOwn();
                        if (Interlocked.Exchange(ref _firstWriteLogged, 1) == 0)
                        {
                            Log?.Invoke($"Island brightness first write: {percent}% in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:0} ms", null);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // the next write must be able to start again
                lock (Gate)
                {
                    _writing = false;
                }

                Log?.Invoke("Island brightness write failed", ex);
            }
        });
    }

    /// <summary>
    /// Calls <c>WmiSetBrightness</c> on every active panel. The WMI objects are reused between writes (and dropped on any error or after a
    /// pause, then rebuilt once); false when no panel took the level.
    /// </summary>
    private static bool SetPanel(byte percent)
    {
        lock (CacheGate)
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    if (_writeTargets is null || Environment.TickCount64 - _writeStamp > 5000)
                    {
                        DropWriteCacheLocked();
                        BuildWriteCache();
                    }

                    foreach (var target in _writeTargets!)
                    {
                        target.GetType().InvokeMember("WmiSetBrightness", System.Reflection.BindingFlags.InvokeMethod, null, target, [1, percent]);
                    }

                    _writeStamp = Environment.TickCount64;
                    if (_writeTargets.Count > 0)
                    {
                        return true;
                    }

                    LogFailure("write", new InvalidOperationException("no active WmiMonitorBrightnessMethods instance"));
                    return false;
                }
                catch (Exception ex)
                {
                    DropWriteCacheLocked();
                    if (attempt == 1)
                    {
                        LogFailure("write", ex);
                    }
                }
            }
        }

        return false;
    }

    private static void BuildWriteCache()
    {
        var type = Type.GetTypeFromProgID("WbemScripting.SWbemLocator") ?? throw new InvalidOperationException("WbemScripting.SWbemLocator is not registered");
        var targets = new List<object>();
        object? set = null;
        try
        {
            _writeLocator = Activator.CreateInstance(type);
            _writeService = ((dynamic)_writeLocator!).ConnectServer(".", "root\\wmi");
            set = ((dynamic)_writeService!).ExecQuery("SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=TRUE", "WQL", 0x10);
            foreach (object item in (dynamic)set!)
            {
                targets.Add(item);
            }
        }
        catch
        {
            foreach (var t in targets)
            {
                Release(t);
            }

            throw;
        }
        finally
        {
            Release(set);
        }

        _writeTargets = targets;
        _writeStamp = Environment.TickCount64;
    }

    private static void DropWriteCache()
    {
        lock (CacheGate)
        {
            DropWriteCacheLocked();
        }
    }

    private static void DropWriteCacheLocked()
    {
        if (_writeTargets is not null)
        {
            foreach (var t in _writeTargets)
            {
                Release(t);
            }
        }

        Release(_writeService);
        Release(_writeLocator);
        _writeTargets = null;
        _writeService = null;
        _writeLocator = null;
    }

    /// <summary>Logs a WMI failure: the first of each kind, then at most one a minute.</summary>
    private static void LogFailure(string kind, Exception ex)
    {
        var log = Log;
        if (log is null)
        {
            return;
        }

        lock (LogGate)
        {
            if (FailureLogged.TryGetValue(kind, out var at) && DateTime.UtcNow - at < TimeSpan.FromSeconds(60))
            {
                return;
            }

            FailureLogged[kind] = DateTime.UtcNow;
        }

        log($"Island brightness {kind} failed (0x{ex.HResult:X8}): {ex.Message}", ex);
    }

    /// <summary>Runs the query and calls <paramref name="action"/> per object; false when WMI could not be asked.</summary>
    private static bool ForEach(string kind, string query, Action<object> action)
    {
        object? locator = null;
        object? service = null;
        object? set = null;
        try
        {
            var type = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
            if (type is null)
            {
                LogFailure(kind, new InvalidOperationException("WbemScripting.SWbemLocator is not registered"));
                return false;
            }

            locator = Activator.CreateInstance(type);
            service = ((dynamic)locator!).ConnectServer(".", "root\\wmi");
            set = ((dynamic)service!).ExecQuery(query, "WQL", 0x10);
            foreach (object item in (dynamic)set!)
            {
                try
                {
                    action(item);
                }
                finally
                {
                    Release(item);
                }
            }
        }
        catch (Exception ex)
        {
            // WMI unavailable, access denied, or the call was refused for now.
            LogFailure(kind, ex);
            return false;
        }
        finally
        {
            Release(set);
            Release(service);
            Release(locator);
        }

        return true;
    }

    private static void Release(object? o)
    {
        try
        {
            if (o is not null && System.Runtime.InteropServices.Marshal.IsComObject(o))
            {
                System.Runtime.InteropServices.Marshal.ReleaseComObject(o);
            }
        }
        catch
        {
        }
    }
}
