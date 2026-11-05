// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.WindowTools;

/// <summary>What the Quit on close settle loop does at one look at an app.</summary>
internal enum QuitStep
{
    /// <summary>Do not quit this app (excluded, protected, installer, gone).</summary>
    Abort,

    /// <summary>A visible window exists: start counting the quiet period again.</summary>
    Reset,

    /// <summary>Not yet: keep looking.</summary>
    Wait,

    /// <summary>Send WM_CLOSE to the windows the app showed earlier.</summary>
    AskClose,

    /// <summary>The app ignored WM_CLOSE and is still windowless: post WM_QUIT to its main GUI thread.</summary>
    PostQuit,
}

/// <summary>
/// The pure timing and name rules of Quit on close (no window or process calls), so a harness can check them. The feature calls these
/// at every look; nothing here ever kills a process.
/// </summary>
internal static class AutoQuitPolicy
{
    /// <summary>An app must have had no visible top-level window for this long before it is asked to quit.</summary>
    public const long GraceMs = 15_000;

    /// <summary>Spacing of the looks during the grace period.</summary>
    public const int SampleMs = 5_000;

    /// <summary>How long WM_CLOSE gets before WM_QUIT is considered.</summary>
    public const long CloseWaitMs = 5_000;

    /// <summary>The next step for an app. <paramref name="closeAskedMs"/> is negative while WM_CLOSE has not been sent.</summary>
    public static QuitStep Decide(bool blocked, bool hasVisibleWindow, long windowlessMs, long closeAskedMs)
    {
        if (blocked)
        {
            return QuitStep.Abort;
        }

        if (hasVisibleWindow)
        {
            return QuitStep.Reset;
        }

        if (windowlessMs < GraceMs)
        {
            return QuitStep.Wait;
        }

        if (closeAskedMs < 0)
        {
            return QuitStep.AskClose;
        }

        return closeAskedMs < CloseWaitMs ? QuitStep.Wait : QuitStep.PostQuit;
    }

    /// <summary>
    /// Installers and updaters are often windowless between steps: names containing setup, install or update, msiexec, unins*, Inno Setup's
    /// <c>.tmp</c> images, and anything running from a temp folder.
    /// </summary>
    public static bool IsInstallerPath(string path, IEnumerable<string> tempRoots)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        var file = Path.GetFileName(path);
        if (string.Equals(Path.GetExtension(file), ".tmp", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
        if (name == "msiexec" || name.Contains("setup", StringComparison.Ordinal) || name.Contains("install", StringComparison.Ordinal)
            || name.Contains("update", StringComparison.Ordinal) || name.StartsWith("unins", StringComparison.Ordinal))
        {
            return true;
        }

        return tempRoots.Any(root => !string.IsNullOrWhiteSpace(root) && SafePaths.IsUnder(path, root));
    }

    /// <summary>True when the app must never be quit by name alone: a protected process or an installer.</summary>
    public static bool IsNeverQuit(string path, IEnumerable<string> tempRoots) =>
        ProcessSafety.IsProtectedName(Path.GetFileNameWithoutExtension(path)) || IsInstallerPath(path, tempRoots);
}
