// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Trayloom.Core.Native;

/// <summary>
/// Gives memory back to Windows once the UI has been idle for a while: a full compacting GC followed by trimming the
/// working set. A tray app spends most of its life hidden, so the pages touched while a window was open are dead weight.
/// </summary>
public static class MemoryTrimmer
{
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(45);
    private static DispatcherTimer? _timer;

    /// <summary>
    /// Optional check set by a feature that knows when trimming would be wasteful (for example the island is open).
    /// While it returns true the trim is postponed by another delay.
    /// </summary>
    public static Func<bool>? SkipWhen { get; set; }

    /// <summary>(Re)starts the idle countdown. Call on the UI thread after a window closes; safe to call often.</summary>
    public static void Schedule()
    {
        try
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                return;
            }

            if (!dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(Schedule));
                return;
            }

            if (_timer is null)
            {
                _timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle, dispatcher) { Interval = Delay };
                _timer.Tick += (_, _) => OnTick();
            }

            _timer.Stop();
            _timer.Start();
        }
        catch
        {
            // Trimming is an optimisation only.
        }
    }

    private static void OnTick()
    {
        try
        {
            _timer?.Stop();
            if (SkipWhen?.Invoke() == true)
            {
                _timer?.Start();
                return;
            }

            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            using var self = Process.GetCurrentProcess();
            EmptyWorkingSet(self.Handle);
        }
        catch
        {
            // Never let a trim take the app down.
        }
    }

    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(nint process);
}
