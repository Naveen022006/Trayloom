// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace WinKnife.Features.Capture;

/// <summary>
/// Marker for the Capture project (screenshot, screen recording, copy text from screen, color picker). The assembly is
/// scanned for <c>IFeature</c> and contribution classes; this class only keeps the assembly non-empty so Smart App Control
/// does not block it.
/// </summary>
public static class CaptureProject
{
    /// <summary>Display name of the project.</summary>
    public const string Name = "Capture";

    /// <summary>The project's assembly name prefix used for discovery.</summary>
    public static string AssemblyName => typeof(CaptureProject).Assembly.GetName().Name ?? Name;
}
