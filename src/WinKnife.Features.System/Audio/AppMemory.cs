// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using WinKnife.Core;

namespace WinKnife.Features.SystemTools;

/// <summary>
/// What the mixer remembers about each app, with the Vorssaint keys and rules: <c>appVolumes</c> (app id to level, an untouched 100 % is not stored),
/// <c>appOutputDevices</c> (app id to output endpoint id) and, because Windows has a real per-app mute that macOS lacks, <c>appMutedApps</c>.
/// The app id is the lower-case executable name (Vorssaint uses the bundle id).
/// </summary>
internal static class AppMemory
{
    /// <summary>Highest level a saved volume may have (Vorssaint boosts to 200 %; a Windows session stops at 100 %).</summary>
    public const double MaximumLevel = 2;

    /// <summary>The app id is stored only when it is stable: not a bare process id, not empty, not absurdly long.</summary>
    public static string? SanitizeId(string? raw)
    {
        var text = raw?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 512 || text.StartsWith("pid:", StringComparison.Ordinal) || text.Any(char.IsControl))
        {
            return null;
        }

        return text.ToLowerInvariant();
    }

    /// <summary>Clamps a level to 0 ... 2; anything that is not a number counts as untouched.</summary>
    public static double SanitizeVolume(double volume) => double.IsFinite(volume) ? Math.Clamp(volume, 0, MaximumLevel) : 1;

    /// <summary>A level the UI would show as 100 % counts as unity and is not stored.</summary>
    public static bool IsUnity(double volume) => Math.Abs(volume - 1) < 0.005;

    public static Dictionary<string, double> Volumes(IAppSettings app)
    {
        var result = new Dictionary<string, double>();
        foreach (var (key, value) in app.Get<Dictionary<string, double>>("appVolumes") ?? [])
        {
            if (SanitizeId(key) is { } id && double.IsFinite(value))
            {
                result[id] = SanitizeVolume(value);
            }
        }

        return result;
    }

    public static HashSet<string> Muted(IAppSettings app) =>
        new((app.Get<List<string>>("appMutedApps") ?? []).Select(SanitizeId).Where(id => id is not null)!, StringComparer.Ordinal);

    public static Dictionary<string, string> Routes(IAppSettings app)
    {
        var result = new Dictionary<string, string>();
        foreach (var (key, value) in app.Get<Dictionary<string, string>>("appOutputDevices") ?? [])
        {
            if (SanitizeId(key) is { } id && !string.IsNullOrWhiteSpace(value) && value.Length <= 512)
            {
                result[id] = value.Trim();
            }
        }

        return result;
    }

    public static void SaveVolume(IAppSettings app, string key, double volume)
    {
        if (SanitizeId(key) is not { } id)
        {
            return;
        }

        var volumes = Volumes(app);
        var level = SanitizeVolume(volume);
        if (IsUnity(level))
        {
            volumes.Remove(id);
        }
        else
        {
            volumes[id] = level;
        }

        app.Set("appVolumes", volumes);
        app.Set("appVolumesArePositions", true);
    }

    public static void SaveMuted(IAppSettings app, string key, bool muted)
    {
        if (SanitizeId(key) is not { } id)
        {
            return;
        }

        var set = Muted(app);
        if (muted ? !set.Add(id) : !set.Remove(id))
        {
            return;
        }

        app.Set("appMutedApps", set.OrderBy(s => s, StringComparer.Ordinal).ToList());
    }

    public static void SaveRoute(IAppSettings app, string key, string? endpointId)
    {
        if (SanitizeId(key) is not { } id)
        {
            return;
        }

        var routes = Routes(app);
        if (string.IsNullOrWhiteSpace(endpointId))
        {
            routes.Remove(id);
        }
        else
        {
            routes[id] = endpointId.Trim();
        }

        app.Set("appOutputDevices", routes);
    }

    /// <summary>Puts the remembered level and mute on one audio session (any thread).</summary>
    public static void Restore(IAppSettings app, string key, AudioSessionControl session)
    {
        if (SanitizeId(key) is not { } id)
        {
            return;
        }

        try
        {
            var volume = session.SimpleAudioVolume;
            if (Volumes(app).TryGetValue(id, out var level))
            {
                volume.Volume = (float)Math.Min(level, 1);
            }

            if (Muted(app).Contains(id))
            {
                volume.Mute = true;
            }
        }
        catch (Exception)
        {
            // The session ended while it was being restored.
        }
    }

    /// <summary>Re-applies a remembered output route to a process (only when that output is currently active and the route differs).</summary>
    public static void RestoreRoute(IAppSettings app, string key, uint processId, ICollection<string>? activeEndpoints = null)
    {
        if (processId == 0 || !AppRouting.IsSupported || SanitizeId(key) is not { } id || !Routes(app).TryGetValue(id, out var endpoint))
        {
            return;
        }

        var active = activeEndpoints ?? AudioDevices.List(DataFlow.Render).Select(d => d.Id).ToList();
        if (!active.Contains(endpoint) || string.Equals(AppRouting.Get(processId), endpoint, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        AppRouting.Set(processId, endpoint);
    }
}

/// <summary>
/// Keeps the remembered per-app levels, mutes and outputs applied while the mixer feature is on: on start for every app already playing,
/// and whenever an app's audio session appears again (an app that was closed and reopened, or a new stream).
/// </summary>
internal sealed class AppMemoryWatcher : IDisposable
{
    private readonly IAppSettings _app;
    private readonly ILog _log;
    private readonly object _gate = new();
    private readonly List<(MMDevice Device, AudioSessionManager Manager)> _attached = [];
    private MMDeviceEnumerator? _enumerator;
    private bool _disposed;

    public AppMemoryWatcher(IAppSettings app, ILog log)
    {
        _app = app;
        _log = log;
    }

    /// <summary>(Re)attaches to every active playback device and restores the sessions that already exist.</summary>
    public void Attach()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            Detach();
            try
            {
                _enumerator ??= new MMDeviceEnumerator();
                var active = new List<string>();
                var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                foreach (var device in devices)
                {
                    active.Add(device.ID);
                }

                foreach (var device in devices)
                {
                    try
                    {
                        var manager = device.AudioSessionManager;
                        manager.OnSessionCreated += OnSessionCreated;
                        manager.RefreshSessions();
                        _attached.Add((device, manager));
                        var sessions = manager.Sessions;
                        for (var i = 0; i < sessions.Count; i++)
                        {
                            using var session = sessions[i];
                            RestoreSession(session, active);
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.Warn($"App volume memory: could not watch {device.FriendlyName}: {ex.Message}");
                        device.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"App volume memory: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            Detach();
            _enumerator?.Dispose();
            _enumerator = null;
        }
    }

    private void Detach()
    {
        foreach (var (device, manager) in _attached)
        {
            try
            {
                manager.OnSessionCreated -= OnSessionCreated;
                device.Dispose();
            }
            catch (Exception)
            {
            }
        }

        _attached.Clear();
    }

    /// <summary>COM thread: a new audio session exists; give it back what the user chose for that app.</summary>
    private void OnSessionCreated(object sender, object newSession)
    {
        try
        {
            using var session = new AudioSessionControl((IAudioSessionControl)newSession);
            RestoreSession(session, null);
        }
        catch (Exception)
        {
            // Never let an exception cross back into COM.
        }
    }

    private void RestoreSession(AudioSessionControl session, ICollection<string>? active)
    {
        try
        {
            var pid = session.GetProcessID;
            var key = AppIdentity.KeyOf(pid, session.IsSystemSoundsSession);
            if (key is null)
            {
                return;
            }

            AppMemory.Restore(_app, key, session);
            AppMemory.RestoreRoute(_app, key, pid, active);
        }
        catch (Exception)
        {
        }
    }
}
