// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

/// <summary>Controls tab (Mouse and keyboard category): the "Radial menu" switch row with a "Manage the menu" accessory.</summary>
public sealed class RadialMenuPanelSection : IPanelSection
{
    /// <inheritdoc />
    public string? FeatureId => "radial-menu";

    /// <inheritdoc />
    public string TabId => PanelTabs.Controls;

    /// <inheritdoc />
    public string? Category => PanelTabs.CategoryInput;

    /// <inheritdoc />
    public bool SupportsEditing => true;

    /// <inheritdoc />
    public int Order => 180;

    /// <inheritdoc />
    public (int Enabled, int Total) CategoryCounts => (RadialState.Ctx?.App.Get<bool>("radialMenuEnabled") == true ? 1 : 0, 1);

    /// <inheritdoc />
    public FrameworkElement CreateView(IPanelContext context)
    {
        var app = context.App;
        var group = new PanelRowGroup();
        var visible = app.Get<bool>("panelControlRadialMenu", true);
        if (!visible && !context.IsEditing)
        {
            return new Border { Visibility = Visibility.Collapsed };
        }

        var enabled = app.Get<bool>("radialMenuEnabled");
        var accessory = new StackPanel { Margin = new Thickness(41, 0, 10, 8), Visibility = enabled ? Visibility.Visible : Visibility.Collapsed };
        accessory.Children.Add(PanelButtons.Mini("Manage the menu", null, () =>
        {
            context.ClosePanel();
            context.Shell.OpenSettings(SettingsPages.RadialMenu);
        }));
        ((FrameworkElement)accessory.Children[0]).HorizontalAlignment = HorizontalAlignment.Left;

        var toggle = new PanelToggleRow("\uE80A", "Radial menu", RadialStrings.PanelCaption, enabled, on =>
        {
            app.Set("radialMenuEnabled", on);
            accessory.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            context.RequestRelayout();
            RadialState.Raise(RadialChange.Enabled);
        }, showCaption: context.IsEditing);
        var content = new StackPanel();
        if (context.IsEditing)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(toggle);
            var eye = new Border
            {
                Width = 24,
                Height = 22,
                CornerRadius = new CornerRadius(7),
                Margin = new Thickness(0, 0, 10, 0),
                Cursor = Cursors.Hand,
                ToolTip = visible ? "Hide from panel" : "Show in panel",
                Child = UiKit.Icon(visible ? Glyphs.EyeOff : Glyphs.Eye, 12, visible ? ThemeKeys.TextSecondary : ThemeKeys.Accent),
            };
            eye.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
            eye.MouseLeftButtonUp += (_, _) =>
            {
                app.Set("panelControlRadialMenu", !visible);
                context.RequestRelayout();
            };
            Grid.SetColumn(eye, 1);
            grid.Children.Add(eye);
            content.Children.Add(grid);
        }
        else
        {
            content.Children.Add(toggle);
        }

        content.Children.Add(accessory);
        group.Add(content);
        return group;
    }
}
