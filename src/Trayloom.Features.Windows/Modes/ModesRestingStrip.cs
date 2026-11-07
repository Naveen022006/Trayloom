// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Trayloom.Core;

namespace Trayloom.Features.WindowTools.Modes;

/// <summary>
/// The mode badge: the active mode's icon and name, shown in a free wing of the closed island while nothing else is going on. The island
/// only asks for a "…resting" strip when it is at rest, and never lists it as a live activity, so music, a timer or a notice always win.
/// </summary>
public sealed class ModesRestingStrip : IIslandStrip
{
    // The runtimes that already redraw the island when they change; a runtime is hooked once, however often the island asks.
    private static readonly ConditionalWeakTable<ModesRuntime, object> Hooked = [];

    public string? FeatureId => ModesKeys.FeatureId;

    public int Order => 47;

    public string Id => "modes.resting";

    public FrameworkElement? CreateStrip(IIslandContext context)
    {
        if (context.Services.Get<ModesRuntime>() is not { } runtime)
        {
            return null;
        }

        // Captured before anything else, so a mode that becomes active later can ask the island to draw the badge.
        runtime.IslandContext ??= context;
        Hook(runtime, context);
        if (!context.App.Get<bool>(ModesAutoKeys.ShowInIsland) || runtime.Active is not { } mode)
        {
            return null;
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var icon = ModesLook.Icon(mode.Glyph, 11, ModesLook.Of(ModesLook.ColorOf(mode.Color)));
        icon.Margin = new Thickness(0, 0, 4, 0);
        row.Children.Add(icon);
        var name = ModesLook.Text(mode.Name, 9, System.Windows.FontWeights.Medium, ModesLook.White(0.85));
        name.MaxWidth = 90;
        row.Children.Add(name);
        row.ToolTip = ModesStrings.ActionMode(mode.Name);
        return row;
    }

    /// <summary>Redraws the island when the active mode changes or the switch in Settings is flipped.</summary>
    private static void Hook(ModesRuntime runtime, IIslandContext context)
    {
        if (Hooked.TryGetValue(runtime, out _))
        {
            return;
        }

        Hooked.Add(runtime, new object());
        runtime.Changed += () => (runtime.IslandContext ?? context).Invalidate();
        context.App.Changed += key =>
        {
            if (key == ModesAutoKeys.ShowInIsland)
            {
                context.Ui.BeginInvoke(() => (runtime.IslandContext ?? context).Invalidate());
            }
        };
    }
}
