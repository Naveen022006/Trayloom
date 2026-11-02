// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace WinKnife.Features.SystemTools;

internal enum DeviceChange
{
    Added,
    Removed,
    StateChanged,
    DefaultChanged,
}

/// <summary>Forwards IMMNotificationClient callbacks (raised on COM threads) to a single handler.</summary>
internal sealed class DeviceWatcher : IMMNotificationClient, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly Action<DeviceChange, string> _handler;
    private readonly Action<string, DeviceState>? _stateHandler;
    private bool _disposed;

    public DeviceWatcher(Action<DeviceChange, string> handler, Action<string, DeviceState>? stateHandler = null)
    {
        _handler = handler;
        _stateHandler = stateHandler;
        _enumerator.RegisterEndpointNotificationCallback(this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _enumerator.UnregisterEndpointNotificationCallback(this);
        }
        catch (Exception)
        {
        }

        _enumerator.Dispose();
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        try
        {
            _stateHandler?.Invoke(deviceId, newState);
        }
        catch (Exception)
        {
        }

        Raise(DeviceChange.StateChanged, deviceId);
    }

    public void OnDeviceAdded(string pwstrDeviceId) => Raise(DeviceChange.Added, pwstrDeviceId);

    public void OnDeviceRemoved(string deviceId) => Raise(DeviceChange.Removed, deviceId);

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => Raise(DeviceChange.DefaultChanged, defaultDeviceId);

    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
    }

    private void Raise(DeviceChange change, string id)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _handler(change, id);
        }
        catch (Exception)
        {
            // Never let an exception cross back into COM.
        }
    }
}
