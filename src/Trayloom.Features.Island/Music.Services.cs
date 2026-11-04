// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace Trayloom.Features.Island;

/// <summary>
/// The island's streaming services (Spotify and YouTube Music), created with the island and disposed with it: <see cref="Dispose"/> cancels
/// a running sign-in, closes the redirect listener, cancels requests and stops timers, so stopping the feature undoes everything.
/// </summary>
internal sealed class MusicServices : IDisposable
{
    private bool _disposed;

    /// <summary>Creates both services; neither talks to the network until the user sets it up.</summary>
    public MusicServices(IslandCore core)
    {
        Spotify = new SpotifyService(core);
        YouTube = new YouTubeService(core);
        All = [Spotify, YouTube];
        Spotify.Changed += Raise;
        YouTube.Changed += Raise;
    }

    /// <summary>Spotify.</summary>
    public SpotifyService Spotify { get; }

    /// <summary>YouTube Music.</summary>
    public YouTubeService YouTube { get; }

    /// <summary>Both services.</summary>
    public IReadOnlyList<IMusicService> All { get; }

    /// <summary>Raised on the UI thread whenever either service changed.</summary>
    public event Action? Changed;

    /// <summary>The service of a kind.</summary>
    public IMusicService Get(StreamKind kind) => kind == StreamKind.Spotify ? Spotify : YouTube;

    private void Raise() => Changed?.Invoke();

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Spotify.Changed -= Raise;
        YouTube.Changed -= Raise;
        Spotify.Dispose();
        YouTube.Dispose();
    }
}
