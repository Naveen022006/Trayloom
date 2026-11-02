// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

/// <summary>The warning alert of the quick toggles (Vorssaint's NSAlert): title, message, a default confirm button and Cancel.</summary>
internal sealed class ToggleConfirmDialog : Window
{
    private ToggleConfirmDialog(string title, string message, string confirm)
    {
        Title = title;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        this.Themed(BackgroundProperty, ThemeKeys.WindowBackground);
        SourceInitialized += (_, _) => ThemeManager.BindWindow(this);

        var root = new Grid { Margin = new Thickness(22, 20, 22, 18), MinWidth = 340, MaxWidth = 420 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var icon = UiKit.Icon(Glyphs.Warning, 30, ThemeKeys.Orange);
        icon.Margin = new Thickness(0, 0, 16, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        root.Children.Add(icon);
        var text = new StackPanel();
        text.Children.Add(UiKit.Text(title, 14, FontWeights.SemiBold, ThemeKeys.TextPrimary, wrap: true));
        var body = UiKit.Text(message, 12, FontWeights.Normal, ThemeKeys.TextSecondary, wrap: true);
        body.Margin = new Thickness(0, 6, 0, 0);
        text.Children.Add(body);
        Grid.SetColumn(text, 1);
        root.Children.Add(text);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var ok = UiKit.Button(confirm, ButtonKind.Prominent, 12.5);
        ok.IsDefault = true;
        ok.Click += (_, _) =>
        {
            DialogResult = true;
        };
        var cancel = UiKit.Button(Loc.T("Cancel"), ButtonKind.Bordered, 12.5);
        cancel.IsCancel = true;
        cancel.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 2);
        root.Children.Add(buttons);
        Content = root;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
            }
        };
        Loaded += (_, _) => ok.Focus();
    }

    /// <summary>Shows the dialog modally; true when the user confirmed.</summary>
    public static bool Ask(string title, string message, string confirm) => new ToggleConfirmDialog(title, message, confirm).ShowDialog() == true;
}
