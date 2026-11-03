// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace Trayloom.Features.Agents;

/// <summary>
/// Marker for the Agents project (AI Agents data readers and island pages for Claude, Codex, OpenCode and Copilot).
/// The assembly is scanned for <c>IFeature</c> and contribution classes; this class only keeps the assembly non-empty so
/// Smart App Control does not block it.
/// </summary>
public static class AgentsProject
{
    /// <summary>Display name of the project.</summary>
    public const string Name = "Agents";

    /// <summary>The project's assembly name used for discovery.</summary>
    public static string AssemblyName => typeof(AgentsProject).Assembly.GetName().Name ?? Name;
}
