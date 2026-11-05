// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace Trayloom.Core;

/// <summary>
/// The time Trayloom may take to quit, and the waits of the other processes that depend on it. One place, so they cannot drift apart.
/// Worst case of a quit: finishing a recording (15 s) + the recorder's shutdown after a finish that already ran (2 s) + the other
/// features' stops (about 4 s) + "Uninstall completely" recovery (10 s) = 31 s. The uninstaller (kills the copy in the install folder
/// after <see cref="OtherProcessWait"/>) and a restarted copy (<c>--relaunch</c> gives up after the same time) both wait 35 s, so
/// neither cuts a quit short.
/// </summary>
public static class QuitBudget
{
    /// <summary>How long a quit waits for a recording to be finished and saved.</summary>
    public static readonly TimeSpan FinishWait = TimeSpan.FromSeconds(15);

    /// <summary>What <c>RecorderService.Shutdown</c> may still wait when the finish above already ran during this quit.</summary>
    public static readonly TimeSpan ResidualWait = TimeSpan.FromSeconds(2);

    /// <summary>Allowance for every other feature's <c>Stop()</c>.</summary>
    public static readonly TimeSpan OtherStops = TimeSpan.FromSeconds(4);

    /// <summary>"Uninstall completely" recovery hooks.</summary>
    public static readonly TimeSpan UninstallRecovery = TimeSpan.FromSeconds(10);

    /// <summary>How long the uninstaller and a <c>--relaunch</c> copy wait for this process to go away.</summary>
    public static readonly TimeSpan OtherProcessWait = TimeSpan.FromSeconds(35);

    /// <summary>The wait the recorder's shutdown may use: short when a finish already ran in this quit, the full wait otherwise.</summary>
    public static TimeSpan ShutdownWait(bool finishAlreadyRan) => finishAlreadyRan ? ResidualWait : FinishWait;

    /// <summary>The longest a quit can take for the given path.</summary>
    public static TimeSpan WorstCase(bool finishAlreadyRan, bool uninstallCompletely)
    {
        var total = (finishAlreadyRan ? FinishWait + ResidualWait : FinishWait) + OtherStops;
        return uninstallCompletely ? total + UninstallRecovery : total;
    }
}
