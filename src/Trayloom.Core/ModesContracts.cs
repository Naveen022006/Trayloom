// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace Trayloom.Core;

// Modes contract. The interface lives here so the island (Focus session) can start and leave a mode without referencing the
// Windows project; the Modes feature publishes the implementation in Start() and withdraws it in Stop(). Consumers fall back
// gracefully when Services.Get<IModesService>() is null because Modes is not installed.

/// <summary>A mode as other projects see it: enough to list it and to draw its icon.</summary>
/// <param name="Id">Stable id.</param>
/// <param name="Name">Name shown in lists.</param>
/// <param name="Glyph">Segoe Fluent glyph character of the mode's icon.</param>
/// <param name="ColorHex">The mode's colour as <c>#RRGGBB</c>.</param>
public sealed record ModeSummary(string Id, string Name, string Glyph, string ColorHex);

/// <summary>Modes (Windows project). Consumers: island Focus session.</summary>
public interface IModesService
{
    /// <summary>Every mode, in the order the person arranged them.</summary>
    IReadOnlyList<ModeSummary> List { get; }

    /// <summary>Id of the active mode, or null when none is active.</summary>
    string? ActiveId { get; }

    /// <summary>True while a mode is being entered or left.</summary>
    bool IsSwitching { get; }

    /// <summary>
    /// Enters a mode. <paramref name="trigger"/> says who asked: the existing triggers plus <c>"auto"</c> (a rule) and
    /// <c>"focus"</c> (a Focus session). Returns false when the request was refused.
    /// </summary>
    bool Enter(string modeId, string trigger);

    /// <summary>Leaves the active mode. Returns false when there is nothing to leave or the request was refused.</summary>
    bool Leave(string trigger);

    /// <summary>Raised (UI thread) when the list, the active mode or the switching state changed.</summary>
    event Action? Changed;
}
