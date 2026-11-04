// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Microsoft.Win32;

namespace Trayloom.Features.SystemTools.Wifi;

/// <summary>
/// Reads (never writes) the Windows location consent switches to word the "Location needed" caption (spec 3.6). Windows 11 24H2 hides Wi-Fi
/// names from apps that may not use location, because nearby networks can reveal where the PC is.
/// </summary>
internal static class WifiLocation
{
    private const string ConsentPath = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location";

    /// <summary>The explanation that matches what the consent switches say.</summary>
    public static string Caption()
    {
        // The device switch (HKLM) and the global switch (HKCU) both gate every app; the NonPackaged key gates desktop apps.
        if (IsDeny(Registry.LocalMachine, "") || IsDeny(Registry.CurrentUser, ""))
        {
            return WifiStrings.LocationOffPc;
        }

        if (IsDeny(Registry.CurrentUser, @"\NonPackaged") || IsDeny(Registry.LocalMachine, @"\NonPackaged"))
        {
            return WifiStrings.LocationOffDesktop;
        }

        return WifiStrings.LocationGeneric;
    }

    private static bool IsDeny(RegistryKey hive, string sub)
    {
        try
        {
            using var key = hive.OpenSubKey(ConsentPath + sub, false);
            return key?.GetValue("Value") is string s && string.Equals(s, "Deny", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
