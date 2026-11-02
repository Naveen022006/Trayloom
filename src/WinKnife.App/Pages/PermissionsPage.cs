// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.App;

/// <summary>
/// The Permissions tab of the Features hub (spec 02-settings section 6): one card with a row per capability, status chip,
/// explainer, "Used by" line and request / open-settings buttons. Statuses are re-read every 2.5 s while the page is visible.
/// </summary>
internal static class PermissionsPage
{
    public static FrameworkElement Build(ShellServices s)
    {
        var card = new SettingsCard();
        var host = new StackPanel();
        card.Add(host);
        void Fill()
        {
            host.Children.Clear();
            var index = 0;
            foreach (var p in PermissionService.Rows)
            {
                if (index > 0)
                {
                    host.Children.Add(Spaced(UiKit.Divider(42, 0), 12));
                }

                host.Children.Add(Spaced(Row(s, p), index > 0 ? 12 : 0));
                index++;
            }
        }

        Fill();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        var last = Snapshot();
        timer.Tick += (_, _) =>
        {
            var now = Snapshot();
            if (now != last)
            {
                last = now;
                Fill();
            }
        };
        card.Loaded += (_, _) => timer.Start();
        card.Unloaded += (_, _) => timer.Stop();
        return card;
    }

    private static string Snapshot() => string.Join("|", PermissionService.Rows.Select(p => PermissionService.Check(p).State));

    private static UIElement Row(ShellServices s, AppPermission permission)
    {
        var status = PermissionService.Check(permission);
        var (colorKey, label) = status.State switch
        {
            PermissionState.Granted => (ThemeKeys.Green, "Granted"),
            PermissionState.Missing => (ThemeKeys.Orange, "Not granted"),
            _ => (ThemeKeys.TextSecondary, "The app can’t check this one"),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var tile = UiKit.IconTile(PermissionService.Glyph(permission), 30, 13, 8, colorKey, ThemeKeys.TileFill);
        tile.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(tile);

        var col = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(UiKit.Text(PermissionService.Name(permission), 15, FontWeights.Medium, ThemeKeys.TextPrimary));
        var chip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 2, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var dot = new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, colorKey);
        chip.Children.Add(dot);
        var chipText = UiKit.Text(label, 12, FontWeights.Normal, ThemeKeys.TextSecondary);
        chipText.Margin = new Thickness(5, 0, 0, 0);
        chip.Children.Add(chipText);
        head.Children.Add(chip);
        col.Children.Add(head);
        col.Children.Add(Spaced(UiKit.Text(PermissionService.Explainer(permission), 12.5, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true), 4));
        if (status.Note is not null)
        {
            col.Children.Add(Spaced(UiKit.Text(status.Note, 12, FontWeights.Normal, ThemeKeys.TextTertiary, wrap: true), 4));
        }

        var users = FeatureCatalog.UsingPermission(permission).Where(e => s.Runtime.IsAvailable(e.Id)).Select(e => e.Title).ToList();
        if (users.Count > 0)
        {
            col.Children.Add(Spaced(UiKit.Text("Used by " + string.Join(", ", users), 12, FontWeights.Normal, ThemeKeys.TextTertiary, wrap: true), 4));
        }
        else
        {
            col.Children.Add(Spaced(UiKit.Text("Nothing that is on uses this permission right now.", 12, FontWeights.Normal, ThemeKeys.TextTertiary, wrap: true), 4));
            if (status.State == PermissionState.Granted && permission is not (AppPermission.Accessibility or AppPermission.ScreenRecording or AppPermission.AudioCapture))
            {
                var banner = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(8), Margin = new Thickness(0, 6, 0, 0) };
                banner.SetResourceReference(Border.BackgroundProperty, ThemeKeys.TileFill);
                banner.Child = UiKit.Text("You granted this permission, but nothing that is on needs it. If you like, revoke it in Windows Settings.", 12, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
                col.Children.Add(banner);
            }
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        if (PermissionService.CanRequest(permission, status))
        {
            buttons.Children.Add(UiKit.Button(PermissionService.RequestLabel(permission), ButtonKind.Bordered, 12.5, onClick: () => PermissionService.Request(permission)));
        }

        if (PermissionService.SettingsUri(permission) is not null)
        {
            var open = UiKit.Button("Open Windows Settings", ButtonKind.Bordered, 12.5, onClick: () => PermissionService.OpenSettings(permission));
            open.Margin = new Thickness(buttons.Children.Count > 0 ? 8 : 0, 0, 0, 0);
            buttons.Children.Add(open);
        }

        if (buttons.Children.Count > 0)
        {
            col.Children.Add(buttons);
        }

        Grid.SetColumn(col, 2);
        grid.Children.Add(col);
        return grid;
    }

    private static T Spaced<T>(T element, double top) where T : UIElement
    {
        if (element is FrameworkElement fe)
        {
            fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top + top, fe.Margin.Right, fe.Margin.Bottom);
        }

        return element;
    }
}
