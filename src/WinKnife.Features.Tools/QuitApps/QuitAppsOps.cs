// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using WinKnife.Core;

namespace WinKnife.Features.Tools;

/// <summary>Entry points to the Quit apps window (used by the quick toggles list, the Command Bar and the radial menu).</summary>
internal static class QuitAppsOps
{
    /// <summary>Opens the Quit apps window; <paramref name="exceptFront"/> selects which batch button rests focused.</summary>
    public static void Show(IContributionServices ctx, bool exceptFront) => QuitAppsWindow.Open(ctx, exceptFront);

    /// <summary>True when the quit-apps feature is installed.</summary>
    public static bool Available(IContributionServices ctx) => ctx.Runtime.IsAvailable("quit-apps");
}
