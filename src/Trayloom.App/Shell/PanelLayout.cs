// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Trayloom.Core;

namespace Trayloom.App;

/// <summary>Order and visibility of the flyout tabs (<c>panelSectionOrder</c> + the <c>panelShow*</c> keys).</summary>
internal static class PanelLayout
{
    private static readonly string[] AfterPredecessor = [PanelTabs.Brightness, PanelTabs.Disk, PanelTabs.Controls];

    /// <summary>All tabs that can exist on Windows, in the saved order (missing ids at their canonical positions).</summary>
    public static IReadOnlyList<PanelTabInfo> Ordered(IAppSettings app)
    {
        var canonical = PanelTabs.All.Where(t => t.AvailableOnWindows).ToList();
        var saved = app.Get<string>(AppKeys.PanelSectionOrder)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(id => canonical.Any(t => t.Id == id)).Distinct().ToList();
        foreach (var tab in canonical)
        {
            if (saved.Contains(tab.Id))
            {
                continue;
            }

            var index = Array.IndexOf(AfterPredecessor, tab.Id);
            if (index >= 0)
            {
                var predecessor = canonical[canonical.IndexOf(tab) - 1].Id;
                var at = saved.IndexOf(predecessor);
                saved.Insert(at >= 0 ? at + 1 : saved.Count, tab.Id);
            }
            else
            {
                saved.Add(tab.Id);
            }
        }

        return [.. saved.Select(id => canonical.First(t => t.Id == id))];
    }

    /// <summary>The gating features of the tab are installed (and the Displays control is enabled for that tab).</summary>
    public static bool IsAvailable(FeatureRuntime rt, IAppSettings app, PanelTabInfo tab) =>
        tab.AvailableOnWindows
        && rt.AnyAvailable(tab.GateFeatures)
        && (tab.Id != PanelTabs.Brightness || app.Get<bool>(AppKeys.BrightnessControlEnabled));

    public static bool IsShown(IAppSettings app, PanelTabInfo tab) => app.Get<bool>(AppKeys.PanelShow(tab.Id));

    /// <summary>Tabs listed in the Tray &amp; panel editor: available, in order.</summary>
    public static IReadOnlyList<PanelTabInfo> Editable(FeatureRuntime rt, IAppSettings app) =>
        [.. Ordered(app).Where(t => IsAvailable(rt, app, t))];

    public static void SaveOrder(IAppSettings app, IEnumerable<string> ids) =>
        app.Set(AppKeys.PanelSectionOrder, string.Join(',', ids));
}

/// <summary>Minimal drag-to-reorder for a vertical stack of rows with a drag handle.</summary>
internal static class DragReorder
{
    /// <summary>Wires <paramref name="handle"/> so dragging moves <paramref name="row"/> inside <paramref name="container"/>.</summary>
    public static void Attach(StackPanel container, FrameworkElement row, FrameworkElement handle, Action<IReadOnlyList<FrameworkElement>> onReordered)
    {
        handle.Cursor = Cursors.SizeAll;
        handle.MouseLeftButtonDown += (_, e) =>
        {
            handle.CaptureMouse();
            row.Opacity = 0.45;
            e.Handled = true;
        };
        handle.MouseMove += (_, e) =>
        {
            if (!handle.IsMouseCaptured)
            {
                return;
            }

            var y = e.GetPosition(container).Y;
            var current = container.Children.IndexOf(row);
            var target = current;
            var top = 0.0;
            for (var i = 0; i < container.Children.Count; i++)
            {
                var child = (FrameworkElement)container.Children[i];
                var bottom = top + child.ActualHeight + child.Margin.Top + child.Margin.Bottom;
                if (y >= top && y < bottom)
                {
                    target = i;
                    break;
                }

                top = bottom;
            }

            if (target != current)
            {
                container.Children.RemoveAt(current);
                container.Children.Insert(target, row);
            }
        };
        handle.MouseLeftButtonUp += (_, _) =>
        {
            if (!handle.IsMouseCaptured)
            {
                return;
            }

            handle.ReleaseMouseCapture();
            row.Opacity = 1;
            onReordered([.. container.Children.Cast<FrameworkElement>()]);
        };
    }
}
