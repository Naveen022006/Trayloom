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

/// <summary>"Window thumbnails" card shared by the Switcher and Taskbar pages (spec 02b 2.3): preview size tiles, minimal previews, pause list.</summary>
internal static class ThumbnailsCard
{
    public static FrameworkElement Build(IAppSettings app, string sizeKey, string excludedKey)
    {
        var card = new SettingsCard(Strings.WindowThumbnails);
        card.Add(UiKit.Text(Strings.PreviewSize, 11.5, FontWeights.Medium, ThemeKeys.TextPrimary));

        var tiles = new UniformGrid { Columns = 4 };
        var items = new (string Value, string Label, double Sketch)[]
        {
            ("small", Strings.SizeSmall, 0.5), ("normal", Strings.SizeNormal, 0.66), ("large", Strings.SizeLarge, 0.83), ("xlarge", Strings.SizeXLarge, 1.0),
        };

        void Draw()
        {
            tiles.Children.Clear();
            var current = app.Get<string>(sizeKey) ?? "normal";
            for (var i = 0; i < items.Length; i++)
            {
                var (value, label, sketch) = items[i];
                var selected = value == current;
                var tile = new Border
                {
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(8),
                    Margin = new Thickness(i == 0 ? 0 : 10, 0, 0, 0),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                };
                if (selected)
                {
                    tile.SetResourceReference(Border.BackgroundProperty, ThemeKeys.AccentFill12);
                    tile.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Accent);
                }
                else
                {
                    tile.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
                    tile.BorderBrush = Brushes.Transparent;
                }

                var stack = new StackPanel();
                var slot = new Grid { Height = 42 };
                var thumb = new Border { Width = 60 * sketch, Height = 42 * sketch, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                thumb.SetResourceReference(Border.BackgroundProperty, ThemeKeys.TextTertiary);
                thumb.Opacity = 0.4;
                var strip = new Border { Height = 4, Margin = new Thickness(3, 3, 3, 0), VerticalAlignment = VerticalAlignment.Top, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)) };
                thumb.Child = strip;
                slot.Children.Add(thumb);
                stack.Children.Add(slot);
                var text = UiKit.Text(label, 11, FontWeights.Medium, selected ? ThemeKeys.Accent : ThemeKeys.TextPrimary);
                text.HorizontalAlignment = HorizontalAlignment.Center;
                text.Margin = new Thickness(0, 8, 0, 0);
                stack.Children.Add(text);
                tile.Child = stack;
                tile.MouseLeftButtonUp += (_, _) =>
                {
                    app.Set(sizeKey, value);
                    Draw();
                };
                tiles.Children.Add(tile);
            }
        }

        Draw();
        card.Add(tiles);
        card.Add(FormUi.Toggle(app, Keys.MinimalPreviews, "", Strings.MinimalPreviews, Strings.MinimalPreviewsCaption));
        card.Add(new AppBundleList(
            Strings.PauseInApps,
            Strings.PauseCaption,
            () => [.. app.Get<List<string>>(excludedKey) ?? []],
            list => app.Set(excludedKey, list)));
        return card;
    }
}
