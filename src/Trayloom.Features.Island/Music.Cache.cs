// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace Trayloom.Features.Island;

/// <summary>
/// Memory-only cache of the answers to <see cref="StreamRequest"/>s with a time to live, a size limit (oldest entry out first) and
/// de-duplication of requests still in flight. Used from the UI thread only.
/// </summary>
internal sealed class StreamCache
{
    private readonly Func<DateTime> _clock;
    private readonly TimeSpan _ttl;
    private readonly int _max;
    private readonly Dictionary<StreamRequest, (StreamPage Page, DateTime At, long Seq)> _entries = [];
    private readonly HashSet<StreamRequest> _inFlight = [];
    private long _seq;

    public StreamCache(Func<DateTime> clock, TimeSpan ttl, int max = 64)
    {
        _clock = clock;
        _ttl = ttl;
        _max = Math.Max(1, max);
    }

    /// <summary>Number of cached answers (stale ones included until they are asked for).</summary>
    public int Count => _entries.Count;

    /// <summary>The fresh answer for <paramref name="request"/>; a stale one is dropped.</summary>
    public bool TryGet(StreamRequest request, out StreamPage page)
    {
        if (_entries.TryGetValue(request, out var entry))
        {
            if (_clock() - entry.At < _ttl)
            {
                page = entry.Page;
                return true;
            }

            _entries.Remove(request);
        }

        page = null!;
        return false;
    }

    /// <summary>Stores an answer, evicting the oldest entries beyond the limit.</summary>
    public void Set(StreamRequest request, StreamPage page)
    {
        _entries[request] = (page, _clock(), ++_seq);
        while (_entries.Count > _max)
        {
            var oldest = _entries.MinBy(e => e.Value.Seq).Key;
            _entries.Remove(oldest);
        }
    }

    /// <summary>Marks a request as running; false when one already is (so a second fetch must not start).</summary>
    public bool BeginFetch(StreamRequest request) => _inFlight.Add(request);

    /// <summary>Marks a request as finished, whatever its outcome.</summary>
    public void EndFetch(StreamRequest request) => _inFlight.Remove(request);

    /// <summary>True while a fetch for the request is running.</summary>
    public bool IsInFlight(StreamRequest request) => _inFlight.Contains(request);

    /// <summary>Forgets one cached answer.</summary>
    public void Remove(StreamRequest request) => _entries.Remove(request);

    /// <summary>Forgets every cached answer (e.g. on disconnect). Running fetches are left to finish.</summary>
    public void Clear() => _entries.Clear();
}
