// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Collections.Concurrent;

namespace WinKnife.Features.Agents;

/// <summary>A private background thread that runs posted actions one at a time (the usage service's reading queue).</summary>
internal sealed class AgentWorker : IDisposable
{
    private readonly BlockingCollection<Action> _queue = [];
    private readonly Thread _thread;
    private readonly List<Timer> _timers = [];
    private bool _disposed;

    public AgentWorker(string name)
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = name, Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    public int ManagedThreadId => _thread.ManagedThreadId;

    public bool IsWorkerThread => Environment.CurrentManagedThreadId == _thread.ManagedThreadId;

    /// <summary>Queues <paramref name="action"/>; exceptions are swallowed so one bad step never kills the loop.</summary>
    public void Post(Action action)
    {
        try
        {
            if (!_disposed)
            {
                _queue.Add(action);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException)
        {
        }
    }

    /// <summary>Queues <paramref name="action"/> after <paramref name="delay"/>.</summary>
    public void Post(TimeSpan delay, Action action)
    {
        Timer? timer = null;
        timer = new Timer(_ =>
        {
            lock (_timers)
            {
                _timers.Remove(timer!);
            }

            timer!.Dispose();
            Post(action);
        }, null, delay, Timeout.InfiniteTimeSpan);
        lock (_timers)
        {
            _timers.Add(timer);
        }
    }

    /// <summary>Runs <paramref name="action"/> on the worker and waits for it (not from the worker itself).</summary>
    public bool Run(Action action, TimeSpan timeout)
    {
        if (IsWorkerThread)
        {
            action();
            return true;
        }

        using var done = new ManualResetEventSlim();
        Post(() =>
        {
            try
            {
                action();
            }
            finally
            {
                done.Set();
            }
        });
        return done.Wait(timeout);
    }

    private void Loop()
    {
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            try
            {
                action();
            }
            catch (Exception)
            {
                // A failing step must not stop the queue; the next tick tries again.
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_timers)
        {
            foreach (var t in _timers)
            {
                t.Dispose();
            }

            _timers.Clear();
        }

        _queue.CompleteAdding();
    }
}
