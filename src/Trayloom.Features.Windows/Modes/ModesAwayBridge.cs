// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Features.WindowTools.AwayMode;

namespace Trayloom.Features.WindowTools.Modes;

/// <summary>
/// The Modes side of the contract with Away mode (spec 08 section 1.8). Away's own reopen remains the only thing that brings apps
/// back: Modes only tells Away which apps belong to the active mode and updates its window tracking afterwards. Nothing is launched
/// from here, so nothing is opened twice.
/// </summary>
internal sealed partial class ModesRuntime
{
    /// <inheritdoc />
    public string? ActiveModeName => _file.Modes.FirstOrDefault(m => m.Id == _activeId)?.Name;

    /// <summary>
    /// The lower-cased executables of the active mode's app items and the browsers of its web items that Away mode can close. Packaged
    /// apps and programs that are never closed (the shell, terminals, Trayloom) are left out. Answers from the saved mode alone: no
    /// window is enumerated.
    /// </summary>
    public IReadOnlyList<string> ActiveAppsToClose()
    {
        var mode = _file.Modes.FirstOrDefault(m => m.Id == _activeId);
        if (mode is null)
        {
            return [];
        }

        var result = new List<string>();
        foreach (var item in mode.Items)
        {
            string? exe = null;
            if (item.Kind == ModeItemKind.App && string.IsNullOrEmpty(item.Aumid))
            {
                exe = item.Exe;
            }
            else if (item.Kind == ModeItemKind.Web)
            {
                var browser = ModesBrowsers.Resolve(item.Browser);
                exe = AwayBrowsers.KindOf(browser) == AwayAppKind.Browser ? browser : null;
            }

            if (!string.IsNullOrWhiteSpace(exe) && !AwayApps.NeverClosable(exe))
            {
                var lower = exe.Trim().ToLowerInvariant();
                if (!result.Contains(lower))
                {
                    result.Add(lower);
                }
            }
        }

        return result;
    }

    /// <summary>Away mode closed and reopened apps: the new windows take the place of the old handles. Nothing is launched.</summary>
    public void OnAwayReturned(AwayReturnInfo info)
    {
        _tracking.Replace(info.Replaced);
        _tracking.Prune();
    }
}
