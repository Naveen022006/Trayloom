// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace Trayloom.Features.Island;

/// <summary>
/// What the music page needs from the panel it shows in its Library area: the Apple Music <see cref="LibraryPanel"/> or a
/// <see cref="StreamPanel"/> for Spotify and YouTube Music. The keyboard members hand the island's keyboard around the same way for both.
/// </summary>
internal interface IMusicBrowserPanel
{
    /// <summary>The search box has text (Space then belongs to it, not to play / pause).</summary>
    bool HasText { get; }

    /// <summary>Takes the island's keyboard and puts the caret in the search box (an explicit gesture only).</summary>
    void FocusSearch(bool selectAll = false);

    /// <summary>Types into the search box; false when it cannot take the text right now.</summary>
    bool TypeInto(string text);

    /// <summary>Esc: clear the text, then leave the box, then the open list; false when nothing was open.</summary>
    bool StepBack();

    /// <summary>The page is about to be rebuilt: parks what the box holds for the box that comes next.</summary>
    void Park();

    /// <summary>Gives the keyboard back now (the browser is closed, switched, or the page is left).</summary>
    void ReleaseNow();
}
