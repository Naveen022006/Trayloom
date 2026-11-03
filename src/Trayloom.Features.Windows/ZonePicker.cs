// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.WindowTools;

/// <summary>
/// The edge-snap zone map (spec 02b 4.2): a 340 px box (258 compact) with the eight zones around a blank centre. Each zone holds
/// one to four area buttons showing the placement; clicking one opens the menu to turn the zone on/off, change the placement or the
/// number of areas. A reset button sits right of the box.
/// </summary>
internal sealed class EdgeSnapZonePicker : StackPanel
{
    private readonly IAppSettings _app;
    private readonly bool _compact;
    private readonly Border _box = new();
    private readonly Button _reset;

    public EdgeSnapZonePicker(IAppSettings app, bool compact = false)
    {
        _app = app;
        _compact = compact;
        Orientation = Orientation.Horizontal;
        HorizontalAlignment = HorizontalAlignment.Center;

        _box.Width = compact ? 258 : 340;
        _box.CornerRadius = new CornerRadius(compact ? 9 : 12);
        _box.Padding = new Thickness(compact ? 5 : 7);
        _box.BorderThickness = new Thickness(1);
        _box.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Divider);
        _box.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
        Children.Add(_box);

        _reset = new Button
        {
            Width = 24,
            Height = 24,
            Margin = new Thickness(9, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            ToolTip = Strings.Reset,
            Content = UiKit.Icon("", 11, ThemeKeys.TextSecondary, FontWeights.SemiBold),
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
        };
        _reset.SetResourceReference(BackgroundProperty, ThemeKeys.ControlFill);
        _reset.Click += (_, _) =>
        {
            EdgeSnapModel.ResetAll(_app);
            Rebuild();
        };
        Children.Add(_reset);
        Rebuild();
    }

    private double ZoneWidth => _compact ? 58 : 78;

    private void Rebuild()
    {
        var parts = Math.Max(EdgeSnapModel.Actions(_app, SnapZone.Left).Count, EdgeSnapModel.Actions(_app, SnapZone.Right).Count);
        var cornerHeight = _compact ? 38 : 50;
        var middleHeight = Math.Max(_compact ? 46 : 62, parts * 22 + (parts - 1) * 3);
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(cornerHeight) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(middleHeight) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(cornerHeight) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ZoneWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ZoneWidth) });

        void Place(SnapZone zone, int row, int column)
        {
            var cell = ZoneCell(zone);
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }

        Place(SnapZone.TopLeft, 0, 0);
        Place(SnapZone.Top, 0, 2);
        Place(SnapZone.TopRight, 0, 4);
        Place(SnapZone.Left, 2, 0);
        Place(SnapZone.Right, 2, 4);
        Place(SnapZone.BottomLeft, 4, 0);
        Place(SnapZone.Bottom, 4, 2);
        Place(SnapZone.BottomRight, 4, 4);

        var centre = new Border { CornerRadius = new CornerRadius(7), Opacity = 0.7 };
        centre.SetResourceReference(Border.BackgroundProperty, ThemeKeys.TileFill);
        centre.Child = UiKit.Icon("", 14, ThemeKeys.TextTertiary);
        Grid.SetRow(centre, 2);
        Grid.SetColumn(centre, 2);
        grid.Children.Add(centre);
        _box.Child = grid;

        var standard = EdgeSnapModel.IsStandard(_app);
        _reset.IsEnabled = !standard;
        _reset.Opacity = standard ? 0.35 : 1;
    }

    private FrameworkElement ZoneCell(SnapZone zone)
    {
        var enabled = EdgeSnapModel.IsEnabled(_app, zone);
        var actions = EdgeSnapModel.Actions(_app, zone);
        var panel = new UniformGrid
        {
            Rows = EdgeSnapModel.SplitsHorizontally(zone) || actions.Count == 1 ? 1 : actions.Count,
            Columns = EdgeSnapModel.SplitsHorizontally(zone) ? actions.Count : 1,
        };
        for (var i = 0; i < actions.Count; i++)
        {
            var area = Area(zone, i, actions[i], enabled, actions.Count);
            area.Margin = new Thickness(i == 0 ? 0 : (EdgeSnapModel.SplitsHorizontally(zone) ? 3 : 0), i == 0 ? 0 : (EdgeSnapModel.SplitsHorizontally(zone) ? 0 : 3), 0, 0);
            panel.Children.Add(area);
        }

        var host = new Grid();
        host.Children.Add(panel);
        if (!enabled)
        {
            var badge = UiKit.Icon("", 9, ThemeKeys.TextSecondary);
            badge.HorizontalAlignment = HorizontalAlignment.Right;
            badge.VerticalAlignment = VerticalAlignment.Top;
            badge.Margin = new Thickness(0, 2, 3, 0);
            badge.IsHitTestVisible = false;
            host.Children.Add(badge);
        }

        return host;
    }

    private FrameworkElement Area(SnapZone zone, int index, string actionRaw, bool enabled, int count)
    {
        var action = LayoutActions.Get(actionRaw) ?? LayoutActions.Require("maximize");
        var border = new Border
        {
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = action.Title,
            Child = FormUi.MiniScreen(action.X0, action.Y0, action.X1, action.Y1, _compact ? 18 : 24, _compact ? 12 : 16, enabled ? ThemeKeys.Accent : ThemeKeys.TextSecondary, enabled ? ThemeKeys.Accent : ThemeKeys.TextSecondary),
            Opacity = enabled ? 1 : 0.55,
        };
        ((FrameworkElement)border.Child).HorizontalAlignment = HorizontalAlignment.Center;
        ((FrameworkElement)border.Child).VerticalAlignment = VerticalAlignment.Center;
        if (action.Kind == PlacementKind.Maximize)
        {
            border.Child = FormUi.MiniScreen(0, 0, 1, 1, _compact ? 18 : 24, _compact ? 12 : 16, enabled ? ThemeKeys.Accent : ThemeKeys.TextSecondary, enabled ? ThemeKeys.Accent : ThemeKeys.TextSecondary);
        }

        void Style(bool hover)
        {
            if (enabled)
            {
                border.SetResourceReference(Border.BackgroundProperty, hover ? ThemeKeys.AccentFill18 : ThemeKeys.AccentFill12);
                border.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Accent);
                border.BorderBrush = ((Brush)border.BorderBrush).Clone();
                border.BorderBrush.Opacity = hover ? 0.65 : 0.42;
            }
            else
            {
                border.SetResourceReference(Border.BackgroundProperty, hover ? ThemeKeys.ControlFill : ThemeKeys.TileFill);
                border.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Divider);
            }
        }

        Style(false);
        border.MouseEnter += (_, _) => Style(true);
        border.MouseLeave += (_, _) => Style(false);
        border.MouseLeftButtonUp += (_, _) => OpenMenu(border, zone, index, actionRaw, count);
        return border;
    }

    private void OpenMenu(FrameworkElement anchor, SnapZone zone, int index, string current, int count)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var enabled = EdgeSnapModel.IsEnabled(_app, zone);
        var toggle = new MenuItem
        {
            Header = EdgeSnapModel.IsCorner(zone) ? Strings.UseThisCorner : Strings.UseThisEdge,
            IsCheckable = true,
            IsChecked = enabled,
        };
        toggle.Click += (_, _) =>
        {
            EdgeSnapModel.SetEnabled(_app, zone, !enabled);
            Rebuild();
        };
        menu.Items.Add(toggle);
        menu.Items.Add(new Separator());

        foreach (var group in new[] { LayoutGroup.Halves, LayoutGroup.Thirds, LayoutGroup.QuarterRows, LayoutGroup.QuarterColumns, LayoutGroup.Sixths, LayoutGroup.Corners })
        {
            var sub = new MenuItem { Header = LayoutActions.GroupTitle(group), IsChecked = LayoutActions.InGroup(group).Any(a => a.Raw == current) };
            foreach (var action in LayoutActions.InGroup(group))
            {
                sub.Items.Add(PlacementItem(zone, index, action, current));
            }

            menu.Items.Add(sub);
        }

        foreach (var raw in new[] { "maximize", "marginMaximize" })
        {
            menu.Items.Add(PlacementItem(zone, index, LayoutActions.Require(raw), current));
        }

        if (!EdgeSnapModel.IsCorner(zone))
        {
            menu.Items.Add(new Separator());
            var areas = new MenuItem { Header = Strings.AreasOnEdge };
            for (var n = 1; n <= EdgeSnapModel.MaximumParts; n++)
            {
                var parts = n;
                var item = new MenuItem { Header = n.ToString(), IsCheckable = true, IsChecked = count == n };
                item.Click += (_, _) =>
                {
                    if (parts != count)
                    {
                        EdgeSnapModel.SetActions(_app, zone, EdgeSnapModel.ForCount(zone, parts));
                    }

                    EdgeSnapModel.SetEnabled(_app, zone, true);
                    Rebuild();
                };
                areas.Items.Add(item);
            }

            menu.Items.Add(areas);
        }

        menu.IsOpen = true;
    }

    private MenuItem PlacementItem(SnapZone zone, int index, LayoutAction action, string current)
    {
        var item = new MenuItem { Header = action.Title, IsCheckable = true, IsChecked = action.Raw == current };
        item.Click += (_, _) =>
        {
            var list = EdgeSnapModel.Actions(_app, zone).ToList();
            list[Math.Min(index, list.Count - 1)] = action.Raw;
            EdgeSnapModel.SetActions(_app, zone, list);
            EdgeSnapModel.SetEnabled(_app, zone, true);
            Rebuild();
        };
        return item;
    }
}
