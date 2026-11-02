// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics.CodeAnalysis;

namespace WinKnife.Core;

/// <summary>
/// A tiny service registry so feature projects can offer well-known services to the shell (tray menu, tray icon states,
/// panel). A feature calls <c>context.Services.Provide&lt;IKeepAwakeService&gt;(this)</c> in <c>Start</c> and
/// <see cref="Withdraw{T}"/> in <c>Stop</c>.
/// </summary>
public interface IServiceHub
{
    /// <summary>Publishes <paramref name="service"/> under the interface type <typeparamref name="T"/> (replaces an earlier one).</summary>
    void Provide<T>(T service) where T : class;

    /// <summary>Removes the service when it is still <paramref name="service"/>.</summary>
    void Withdraw<T>(T service) where T : class;

    /// <summary>Gets a published service (null when the providing feature is not installed).</summary>
    T? Get<T>() where T : class;

    /// <summary>Raised when a service was provided or withdrawn.</summary>
    event Action? Changed;
}

/// <summary>Default <see cref="IServiceHub"/>.</summary>
public sealed class ServiceHub : IServiceHub
{
    private readonly Dictionary<Type, object> _services = [];
    private readonly object _gate = new();

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public void Provide<T>(T service) where T : class
    {
        lock (_gate)
        {
            _services[typeof(T)] = service;
        }

        Changed?.Invoke();
    }

    /// <inheritdoc />
    public void Withdraw<T>(T service) where T : class
    {
        var removed = false;
        lock (_gate)
        {
            if (_services.TryGetValue(typeof(T), out var current) && ReferenceEquals(current, service))
            {
                removed = _services.Remove(typeof(T));
            }
        }

        if (removed)
        {
            Changed?.Invoke();
        }
    }

    /// <inheritdoc />
    public T? Get<T>() where T : class
    {
        lock (_gate)
        {
            return _services.TryGetValue(typeof(T), out var s) ? (T)s : null;
        }
    }
}

/// <summary>Keep awake state for the tray (icon state, tooltip, countdown, right-click menu). Provided by the Keep awake feature.</summary>
public interface IKeepAwakeService
{
    /// <summary>True while the PC is being kept awake.</summary>
    bool IsActive { get; }

    /// <summary>When it ends (null = indefinitely or inactive).</summary>
    DateTime? EndsAt { get; }

    /// <summary>Activates for <paramref name="duration"/> (null = indefinitely).</summary>
    void Activate(TimeSpan? duration);

    /// <summary>Deactivates.</summary>
    void Deactivate();

    /// <summary>Raised (any thread) when the state changed.</summary>
    event Action? Changed;
}

/// <summary>Microphone mute state for the tray badge. Provided by the Mute microphone feature.</summary>
public interface IMicMuteState
{
    /// <summary>True while the microphone is muted.</summary>
    bool IsMuted { get; }

    /// <summary>Raised (any thread) when the state changed.</summary>
    event Action? Changed;
}

/// <summary>Start a Cleaning Mode session (tray menu item). Provided by the Cleaning Mode feature.</summary>
public interface ICleaningModeService
{
    /// <summary>Starts cleaning mode.</summary>
    void Start();
}

/// <summary>Open / toggle the shelf (tray menu item). Provided by the Shelf feature.</summary>
public interface IShelfService
{
    /// <summary>Shows or hides the shelf.</summary>
    void Toggle();
}
