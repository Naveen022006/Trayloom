// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinKnife.Core.Ui;

namespace WinKnife.App;

/// <summary>Themed modal alerts (NSAlert replacement).</summary>
internal static class Dialogs
{
    /// <summary>Shows an alert and returns the index of the clicked button (-1 = closed with the window button).</summary>
    /// <param name="owner">Owner window (or null).</param>
    /// <param name="title">Bold title.</param>
    /// <param name="message">Body text.</param>
    /// <param name="buttons">Button labels, left to right in the order of importance: the first is the default.</param>
    /// <param name="destructiveIndex">Button drawn in red (or -1).</param>
    public static int Show(Window? owner, string title, string message, string[] buttons, int destructiveIndex = -1)
    {
        var result = -1;
        var window = new Window
        {
            Title = "WinKnife",
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is { IsVisible: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            ShowInTaskbar = false,
            FontFamily = UiKit.TextFont,
            MaxWidth = 460,
            Topmost = owner is null,
        };
        if (owner is { IsVisible: true })
        {
            window.Owner = owner;
        }

        window.SetResourceReference(Window.StyleProperty, ThemeKeys.WindowStyle);
        ThemeManager.BindWindow(window);
        var stack = new StackPanel { Margin = new Thickness(22, 20, 22, 18), MinWidth = 340 };
        stack.Children.Add(UiKit.Text(title, 15, FontWeights.SemiBold, ThemeKeys.TextPrimary, wrap: true));
        var body = UiKit.Text(message, 13, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        body.Margin = new Thickness(0, 8, 0, 18);
        stack.Children.Add(body);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        for (var i = 0; i < buttons.Length; i++)
        {
            var index = i;
            var kind = i == destructiveIndex ? ButtonKind.Destructive : i == 0 ? ButtonKind.Prominent : ButtonKind.Bordered;
            var button = UiKit.Button(buttons[i], kind, 13, onClick: () =>
            {
                result = index;
                window.Close();
            });
            button.Padding = new Thickness(14, 6, 14, 6);
            button.Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0);
            button.IsDefault = i == 0;
            row.Children.Add(button);
        }

        // Prominent default on the right (macOS order is reversed): put the primary button last.
        var reversed = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var child in row.Children.Cast<UIElement>().Reverse().ToList())
        {
            row.Children.Remove(child);
            if (child is FrameworkElement fe)
            {
                fe.Margin = new Thickness(reversed.Children.Count == 0 ? 0 : 8, 0, 0, 0);
            }

            reversed.Children.Add(child);
        }

        stack.Children.Add(reversed);
        window.Content = stack;
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                window.Close();
            }
        };
        window.ShowDialog();
        return result;
    }
}
