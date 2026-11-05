// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace Trayloom.Features.WindowTools.AwayMode;

/// <summary>The workspace the person is in (provided by Modes through the service hub).</summary>
internal interface IAwayWorkspace
{
    /// <summary>Display only, never logged; null when no mode is active.</summary>
    string? ActiveModeName { get; }

    /// <summary>Lower-cased full exe paths of the active mode's app items plus the browsers of its web items; UI thread; no window enumeration.</summary>
    IReadOnlyList<string> ActiveAppsToClose();

    /// <summary>UI thread, after the return finished.</summary>
    void OnAwayReturned(AwayReturnInfo info);
}

/// <summary>What the return brought back: which old windows were replaced by new ones, which apps were opened again, and what triggered it.</summary>
internal sealed record AwayReturnInfo(IReadOnlyDictionary<long, nint> Replaced, IReadOnlySet<string> ReopenedExes, string Trigger);
