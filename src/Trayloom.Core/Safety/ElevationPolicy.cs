// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Security.Principal;

namespace Trayloom.Core;

/// <summary>
/// When may Trayloom offer to run elevated? Only when its own files sit where an unprivileged user cannot rewrite them (under
/// Program Files). From a per-user folder an elevated copy would run code a normal process could have replaced, so no
/// "restart as administrator" is offered there. Admin-only work goes through per-operation scripts instead.
/// </summary>
public static class ElevationPolicy
{
    /// <summary>True when the current process runs with administrator rights.</summary>
    public static bool IsElevated
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>True when <paramref name="path"/> is under Program Files, Program Files (x86) or <c>%ProgramW6432%</c>.</summary>
    public static bool IsAdminOwnedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return SafePaths.IsUnder(path, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))
            || SafePaths.IsUnder(path, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86))
            || SafePaths.IsUnder(path, Environment.GetEnvironmentVariable("ProgramW6432") ?? string.Empty);
    }

    /// <summary>True when the running executable lives in an admin-owned folder.</summary>
    public static bool IsAdminOwnedInstall => IsAdminOwnedPath(Environment.ProcessPath ?? string.Empty)
        && IsAdminOwnedPath(AppContext.BaseDirectory); // under "dotnet Trayloom.dll" the process is the host in Program Files, the code is not

    /// <summary>
    /// The one restart-as-administrator path, set by the App (it shuts down through the recorder-aware quit and starts the new copy with
    /// <c>--relaunch</c>). Feature pages call <see cref="RequestRestartElevated"/> instead of starting a process themselves.
    /// </summary>
    public static Action? RestartElevatedHandler { get; set; }

    /// <summary>Restarts Trayloom elevated through the App's handler; false when elevation is not allowed or no handler is set.</summary>
    public static bool RequestRestartElevated()
    {
        if (!CanOfferElevation || RestartElevatedHandler is not { } handler)
        {
            return false;
        }

        handler();
        return true;
    }

    /// <summary>True when a "restart as administrator" button may be shown. No current install qualifies.</summary>
    public static bool CanOfferElevation => IsAdminOwnedInstall;
}
