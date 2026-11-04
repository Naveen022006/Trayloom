// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.ComponentModel;
using System.Diagnostics;
using Trayloom.Core;

namespace Trayloom.Features.Island;

/// <summary>Opens music links in the user's default handler. Callers pass only addresses rebuilt from validated ids, never URLs taken from API JSON.</summary>
internal static class MusicLinks
{
    /// <summary>Opens a URI or URL with the shell; false (and a log line without the address) when nothing handled it.</summary>
    public static bool Open(string uriOrUrl, ILog log)
    {
        if (string.IsNullOrWhiteSpace(uriOrUrl))
        {
            return false;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(uriOrUrl) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            // The address can hold a query or an auth state: log only that it failed.
            log.Warn("[island] Music: could not open a link");
            return false;
        }
    }
}
