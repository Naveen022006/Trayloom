// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Media;

namespace WinKnife.Features.Island;

/// <summary>A tab in the expanded island panel, contributed by the island itself or by an extension feature.</summary>
internal sealed class IslandSection
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    /// <summary>Segoe Fluent Icons glyph shown on the tab.</summary>
    public required string Glyph { get; init; }

    /// <summary>Tab order (lower first).</summary>
    public int Order { get; init; }

    /// <summary>Creates the content (called once, lazily, on the UI thread). Laid out in roughly 430 x 215 DIPs.</summary>
    public required Func<FrameworkElement> Create { get; init; }

    /// <summary>Called with true when the section becomes visible in the expanded panel and false when hidden again.</summary>
    public Action<bool>? VisibilityChanged { get; init; }

    /// <summary>True when the content scrolls with the wheel (gestures then leave the wheel alone over it).</summary>
    public bool ScrollsContent { get; init; }
}

/// <summary>What an extension wants the collapsed pill to show (for example a running timer).</summary>
internal sealed record CompactInfo(string Glyph, string Text, Brush Accent, string SectionId, bool Dimmed = false);

/// <summary>A short-lived message shown in the collapsed pill.</summary>
internal sealed record IslandAlert(string Glyph, string Text, Brush Accent, string SectionId, TimeSpan Duration);

/// <summary>Immutable snapshot of where the pill is on screen (physical pixels), readable from any thread.</summary>
internal sealed record PillRect(int Left, int Top, int Right, int Bottom, int HeaderBottom, bool Expanded, bool ContentScrolls);

/// <summary>The island window as seen by the extension features.</summary>
internal interface IIslandController
{
    bool IsExpanded { get; }

    void Expand(string? sectionId = null);

    void Collapse();

    void Toggle();

    void NextTrack();

    void PreviousTrack();

    void ShowAlert(IslandAlert alert);

    /// <summary>Keeps the panel from auto-collapsing until the returned token is disposed (e.g. while dragging files out).</summary>
    IDisposable HoldOpen();
}

/// <summary>
/// Internal registry shared by the island and its extension features. Extensions register sections and compact
/// info here; the island renders whatever is registered. Use from the UI thread, except <see cref="Pill"/>
/// which the gestures hook reads from the hook thread.
/// </summary>
internal static class IslandHub
{
    private static readonly List<IslandSection> SectionList = [];
    private static readonly List<(string Owner, CompactInfo Info)> CompactList = [];

    /// <summary>The running island window, or null when the island feature is not installed/running.</summary>
    public static IIslandController? Controller { get; set; }

    /// <summary>True while the island feature is running; extensions use this to stay invisible otherwise.</summary>
    public static bool IsRunning => Controller is not null;

    /// <summary>Last published pill geometry; null when the island is hidden or not running.</summary>
    public static volatile PillRect? Pill;

    public static event Action? SectionsChanged;

    public static event Action? CompactChanged;

    public static IReadOnlyList<IslandSection> Sections => SectionList.OrderBy(s => s.Order).ToList();

    public static void RegisterSection(IslandSection section)
    {
        SectionList.RemoveAll(s => s.Id == section.Id);
        SectionList.Add(section);
        SectionsChanged?.Invoke();
    }

    public static void UnregisterSection(string id)
    {
        if (SectionList.RemoveAll(s => s.Id == id) > 0)
        {
            SectionsChanged?.Invoke();
        }
    }

    /// <summary>Sets (or clears with null) the compact info of one owner. Earlier owners win when several are set.</summary>
    public static void SetCompact(string owner, CompactInfo? info)
    {
        var index = CompactList.FindIndex(c => c.Owner == owner);
        if (info is null)
        {
            if (index < 0)
            {
                return;
            }

            CompactList.RemoveAt(index);
        }
        else if (index >= 0)
        {
            CompactList[index] = (owner, info);
        }
        else
        {
            CompactList.Add((owner, info));
        }

        CompactChanged?.Invoke();
    }

    public static CompactInfo? CurrentCompact => CompactList.Count > 0 ? CompactList[0].Info : null;

    public static void ShowAlert(IslandAlert alert) => Controller?.ShowAlert(alert);
}
