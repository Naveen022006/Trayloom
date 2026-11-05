// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace Trayloom.Core;

/// <summary>Names of processes that must never be ended on a user's behalf (ending them crashes or disables Windows).</summary>
public static class ProcessSafety
{
    // Explorer is deliberately absent: Kill Process can restart it on purpose.
    private static readonly HashSet<string> s_protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "system idle process", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso",
        "registry", "memory compression", "secure system", "dwm",
        "svchost", "sihost", "fontdrvhost", "spoolsv", "audiodg", "ctfmon", "MsMpEng", "NisSrv", "SecurityHealthService",
        "MemCompression", "smartscreen",
    };

    /// <summary>True for a protected process; <paramref name="processName"/> is the name without <c>.exe</c> (case-insensitive).</summary>
    public static bool IsProtectedName(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return true;
        }

        var name = processName.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return s_protected.Contains(name);
    }
}
