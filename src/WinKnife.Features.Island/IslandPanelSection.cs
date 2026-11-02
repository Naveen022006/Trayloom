// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Island;

/// <summary>
/// The "Dynamic Island" row of the tray panel's Controls tab (Windows category, spec 01 5.9): a switch for <c>notchEnabled</c>, a
/// <c>panelControlNotch</c> eye in edit mode and a "Settings" accessory that opens the island page while it is on.
/// </summary>
public sealed class IslandControlsPanelSection : IPanelSection
{
    public string? FeatureId => "notch";

    public string TabId => PanelTabs.Controls;

    public string? Category => PanelTabs.CategoryWindows;

    public bool SupportsEditing => true;

    public int Order => 40;

    public (int Enabled, int Total) GetCategoryCounts(IPanelContext context)
    {
        context.App.Register(K.PanelControlNotch, true);
        return (context.App.Get<bool>(K.Enabled) ? 1 : 0, context.App.Get<bool>(K.PanelControlNotch) || context.IsEditing ? 1 : 0);
    }

    public FrameworkElement CreateView(IPanelContext context)
    {
        var app = context.App;
        K.RegisterDefaults(app);
        var visible = app.Get<bool>(K.PanelControlNotch);
        if (!visible && !context.IsEditing)
        {
            return new Border { Visibility = Visibility.Collapsed };
        }

        var on = app.Get<bool>(K.Enabled);
        var stack = new StackPanel();
        var row = new PanelToggleRow(G.Controls, Loc.T("Dynamic Island"), Loc.T("Your music, controls and everyday tools at the top of your screen."), on, value =>
        {
            app.Set(K.Enabled, value);
            context.RequestRelayout();
        }, showCaption: false);
        var group = new PanelRowGroup().Add(row);
        if (context.IsEditing)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(group);
            var eye = new Button
            {
                Content = UiKit.Icon(visible ? Glyphs.Eye : Glyphs.EyeOff, 14, visible ? ThemeKeys.Accent : ThemeKeys.TextTertiary),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(6, 0, 0, 0),
                Cursor = Cursors.Hand,
            };
            eye.Click += (_, _) =>
            {
                app.Set(K.PanelControlNotch, !visible);
                context.RequestRelayout();
            };
            Grid.SetColumn(eye, 1);
            grid.Children.Add(eye);
            group.Opacity = visible ? 1 : 0.5;
            stack.Children.Add(grid);
            return stack;
        }

        stack.Children.Add(group);
        if (on)
        {
            var manage = PanelButtons.Mini(Loc.T("Settings"), null, () =>
            {
                context.ClosePanel();
                context.InvokeAfterClose(() => context.Shell.OpenSettings(SettingsPages.Island));
            });
            manage.HorizontalAlignment = HorizontalAlignment.Left;
            manage.Margin = new Thickness(41, 0, 10, 8);
            stack.Children.Add(manage);
        }

        return stack;
    }
}
