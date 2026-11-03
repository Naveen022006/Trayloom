// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Trayloom.Core;

namespace Trayloom.Features.Island;

/// <summary>
/// The row on the Controls page while an app uses the microphone: "Microphone in use · Claude" with the app's icon. A click closes
/// the island and opens Windows Settings > Privacy > Microphone, where the person sees (and can change) what each app may do.
/// </summary>
internal static class MicUseRow
{
    public const double Height = 32;
    public const string SettingsUri = "ms-settings:privacy-microphone";

    public static FrameworkElement Create(IslandCore core)
    {
        var users = core.MicUse.Users;
        var orange = Color.FromRgb(0xFF, 0x9F, 0x0A);
        var button = new HoverButton(() => core.Perform(() => OpenSettings(core)))
        {
            Height = Height,
            CornerRadius = new CornerRadius(12),
            Fill = D.Tint(orange, 0.14),
            HoverFill = D.Tint(orange, 0.24),
            Padding = new Thickness(10, 0, 10, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var pulse = new MicPulse(13, G.Mic) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        grid.Children.Add(pulse);

        var text = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var title = D.T(Loc.T("Microphone in use"), 12, FontWeights.SemiBold, D.Text);
        title.VerticalAlignment = VerticalAlignment.Center;
        text.Children.Add(title);
        if (users.Count > 0)
        {
            var dot = D.T("·", 12, FontWeights.Medium, D.White(0.55));
            dot.Margin = new Thickness(6, 0, 6, 0);
            dot.VerticalAlignment = VerticalAlignment.Center;
            text.Children.Add(dot);
            if (users[0].Icon is { } icon)
            {
                text.Children.Add(new Image { Source = icon, Width = 16, Height = 16, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, SnapsToDevicePixels = true });
            }

            var name = D.T(users[0].Name, 12, FontWeights.Medium, D.White(0.85), false, TextTrimming.CharacterEllipsis);
            name.MaxWidth = 160;
            name.VerticalAlignment = VerticalAlignment.Center;
            text.Children.Add(name);
            if (users.Count > 1)
            {
                var more = D.Digits($"+{users.Count - 1}", 12, FontWeights.SemiBold, D.Orange);
                more.Margin = new Thickness(6, 0, 0, 0);
                more.VerticalAlignment = VerticalAlignment.Center;
                text.Children.Add(more);
            }
        }

        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var chevron = D.I(G.ChevronRight, 9, D.White(0.5));
        chevron.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(chevron, 2);
        grid.Children.Add(chevron);

        button.Child = grid;
        D.Tip(button, users.Count > 1
            ? string.Join("\n", users.Select(u => u.Name)) + "\n" + Loc.T("Open microphone privacy settings")
            : Loc.T("Open microphone privacy settings"));
        return button;
    }

    public static void OpenSettings(IslandCore core)
    {
        try
        {
            Process.Start(new ProcessStartInfo(SettingsUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            core.Ctx.Log.Warn($"Could not open the microphone privacy settings: {ex.Message}");
        }
    }
}
