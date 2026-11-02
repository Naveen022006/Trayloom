// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;
using System.Windows.Media;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using WinKnife.Core;

namespace WinKnife.Features.SystemTools;

/// <summary>One row of the volume mixer: an app with its audio sessions across the active playback devices.</summary>
internal sealed class MixerApp
{
    public required string Key { get; init; }

    public required string Name { get; init; }

    public ImageSource? Icon { get; init; }

    public List<uint> ProcessIds { get; } = [];

    public List<AudioSessionControl> Sessions { get; } = [];

    public bool Pinned { get; set; }

    public float Volume { get; set; } = 1;

    public bool Muted { get; set; }

    public float Peak { get; set; }

    public bool Playing { get; set; }

    public string? RouteId { get; set; }
}

/// <summary>The pinned / ordered app lists (<c>mixerAppArrangement</c> = <c>{"order":[...],"pinned":[...]}</c>).</summary>
internal sealed class MixerArrangement
{
    public List<string> Order { get; set; } = [];

    public List<string> Pinned { get; set; } = [];

    public static MixerArrangement Load(IAppSettings app)
    {
        try
        {
            var json = app.Get<string>("mixerAppArrangement");
            return string.IsNullOrWhiteSpace(json) ? new MixerArrangement() : JsonSerializer.Deserialize<MixerArrangement>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new MixerArrangement();
        }
        catch (Exception)
        {
            return new MixerArrangement();
        }
    }

    public void Save(IAppSettings app) => app.Set("mixerAppArrangement", JsonSerializer.Serialize(this, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
}

/// <summary>
/// Reads the audio sessions of every active playback device and groups them by app. One instance lives while a mixer view is visible;
/// <see cref="Refresh"/> rebuilds the model, <see cref="Poll"/> updates volume / mute / level in place. UI thread only.
/// </summary>
internal sealed class MixerSource : IDisposable
{
    private sealed class SessionEvents(Action changed) : IAudioSessionEventsHandler
    {
        public void OnVolumeChanged(float volume, bool isMuted) => changed();

        public void OnDisplayNameChanged(string displayName)
        {
        }

        public void OnIconPathChanged(string iconPath)
        {
        }

        public void OnChannelVolumeChanged(uint channelCount, nint newVolumes, uint channelIndex)
        {
        }

        public void OnGroupingParamChanged(ref Guid groupingId)
        {
        }

        public void OnStateChanged(AudioSessionState state) => changed();

        public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason) => changed();
    }

    private readonly IAppSettings _app;
    private readonly Action _changed;
    private readonly List<MMDevice> _devices = [];
    private readonly SessionEvents _events;
    private MMDeviceEnumerator? _enumerator;
    private List<MixerApp> _groups = [];
    private bool _disposed;

    public MixerSource(IAppSettings app, Action changed)
    {
        _app = app;
        _changed = changed;
        _events = new SessionEvents(changed);
    }

    /// <summary>All listed apps in display order (pinned first).</summary>
    public List<MixerApp> Apps { get; private set; } = [];

    /// <summary>Every app seen (hidden or not), by key, for the "Apps in the list" options.</summary>
    public Dictionary<string, string> Known { get; } = [];

    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        Release();
        var hidden = _app.Get<List<string>>("mixerHiddenApps");
        var hideInactive = _app.Get<bool>("mixerHideInactiveApps");
        var arrangement = MixerArrangement.Load(_app);
        var groups = new Dictionary<string, MixerApp>();
        try
        {
            _enumerator ??= new MMDeviceEnumerator();
            foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                _devices.Add(device);
                try
                {
                    var manager = device.AudioSessionManager;
                    manager.OnSessionCreated -= OnSessionCreated;
                    manager.OnSessionCreated += OnSessionCreated;
                    manager.RefreshSessions();
                    var sessions = manager.Sessions;
                    for (var i = 0; i < sessions.Count; i++)
                    {
                        var session = sessions[i];
                        try
                        {
                            if (session.State == AudioSessionState.AudioSessionStateExpired)
                            {
                                session.Dispose();
                                continue;
                            }

                            var pid = session.GetProcessID;
                            var (key, name, icon) = AppIdentity.Resolve(pid, session.IsSystemSoundsSession);
                            Known[key] = name;
                            if (!groups.TryGetValue(key, out var app))
                            {
                                app = new MixerApp { Key = key, Name = name, Icon = icon, Pinned = arrangement.Pinned.Contains(key) };
                                groups[key] = app;
                            }

                            app.Sessions.Add(session);
                            if (pid != 0 && !app.ProcessIds.Contains(pid))
                            {
                                app.ProcessIds.Add(pid);
                            }

                            session.RegisterEventClient(_events);
                        }
                        catch (Exception)
                        {
                            session.Dispose();
                        }
                    }
                }
                catch (Exception)
                {
                    // The device vanished while enumerating.
                }
            }
        }
        catch (Exception)
        {
            // No audio service.
        }

        // Remember names of everything seen so the "Apps in the list" options can bring hidden apps back while they are not running.
        var known = _app.Get<Dictionary<string, string>>("mixerKnownApps");
        var changedKnown = false;
        foreach (var (key, name) in Known)
        {
            if (!known.TryGetValue(key, out var old) || old != name)
            {
                known[key] = name;
                changedKnown = true;
            }
        }

        if (changedKnown)
        {
            _app.Set("mixerKnownApps", known);
        }

        _groups = [.. groups.Values];
        foreach (var app in groups.Values)
        {
            ReadState(app);
            if (AppRouting.IsSupported && app.ProcessIds.Count > 0)
            {
                app.RouteId = app.ProcessIds.Select(AppRouting.Get).FirstOrDefault(r => !string.IsNullOrEmpty(r));
            }
        }

        var list = groups.Values.Where(a => !hidden.Contains(a.Key)).ToList();
        if (hideInactive)
        {
            list = list.Where(a => a.Playing || Math.Abs(a.Volume - 1f) > 0.005f || a.Muted || !string.IsNullOrEmpty(a.RouteId) || a.Pinned).ToList();
        }

        Apps = [.. list
            .OrderBy(a => a.Pinned ? 0 : 1)
            .ThenBy(a => a.Pinned ? Index(arrangement.Pinned, a.Key) : Index(arrangement.Order, a.Key))
            .ThenByDescending(a => a.Playing ? 1 : 0)
            .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>Updates volume, mute and level of every row from its sessions.</summary>
    public void Poll()
    {
        foreach (var app in Apps)
        {
            ReadState(app);
        }
    }

    public void SetVolume(MixerApp app, float volume)
    {
        foreach (var session in app.Sessions)
        {
            try
            {
                session.SimpleAudioVolume.Volume = Math.Clamp(volume, 0f, 1f);
            }
            catch (Exception)
            {
            }
        }

        app.Volume = volume;
        AppMemory.SaveVolume(_app, app.Key, volume);
    }

    public void SetMute(MixerApp app, bool mute)
    {
        foreach (var session in app.Sessions)
        {
            try
            {
                session.SimpleAudioVolume.Mute = mute;
            }
            catch (Exception)
            {
            }
        }

        app.Muted = mute;
        AppMemory.SaveMuted(_app, app.Key, mute);
    }

    /// <summary>Pins the app's playback to <paramref name="endpointId"/> (null = follow the default output) and remembers the choice.</summary>
    public void SetRoute(MixerApp app, string? endpointId)
    {
        foreach (var pid in app.ProcessIds)
        {
            AppRouting.Set(pid, endpointId);
        }

        app.RouteId = endpointId;
        AppMemory.SaveRoute(_app, app.Key, endpointId);
    }

    public void Dispose()
    {
        _disposed = true;
        Release();
        _enumerator?.Dispose();
        _enumerator = null;
    }

    private static int Index(List<string> list, string key)
    {
        var i = list.IndexOf(key);
        return i < 0 ? int.MaxValue : i;
    }

    private static void ReadState(MixerApp app)
    {
        var peak = 0f;
        var volume = 0f;
        var any = false;
        var muted = app.Sessions.Count > 0;
        var playing = false;
        foreach (var session in app.Sessions)
        {
            try
            {
                peak = Math.Max(peak, session.AudioMeterInformation.MasterPeakValue);
                volume = Math.Max(volume, session.SimpleAudioVolume.Volume);
                muted &= session.SimpleAudioVolume.Mute;
                playing |= session.State == AudioSessionState.AudioSessionStateActive;
                any = true;
            }
            catch (Exception)
            {
            }
        }

        app.Peak = peak;
        app.Volume = any ? volume : 1f;
        app.Muted = any && muted;
        app.Playing = playing || peak > 0.001f;
    }

    private void OnSessionCreated(object sender, object newSession) => _changed();

    private void Release()
    {
        foreach (var app in _groups)
        {
            foreach (var session in app.Sessions)
            {
                try
                {
                    session.UnRegisterEventClient(_events);
                    session.Dispose();
                }
                catch (Exception)
                {
                }
            }
        }

        _groups = [];
        Apps = [];
        foreach (var device in _devices)
        {
            try
            {
                device.AudioSessionManager.OnSessionCreated -= OnSessionCreated;
                device.Dispose();
            }
            catch (Exception)
            {
            }
        }

        _devices.Clear();
    }
}
