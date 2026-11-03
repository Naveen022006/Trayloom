// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Trayloom.Core;
using Trayloom.Core.Ui;
using Orientation = System.Windows.Controls.Orientation;

namespace Trayloom.Features.Files;

/// <summary>A recorder that also accepts keys without a modifier (Rename shortcut: F2, Enter...).</summary>
internal sealed class BareHotkeyRecorder : HotkeyRecorder
{
    public event Action<Hotkey>? BareRecorded;

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var bare = Keyboard.Modifiers == ModifierKeys.None
            && key is not (Key.Escape or Key.Back or Key.Delete or Key.None or Key.DeadCharProcessed or Key.ImeProcessed or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            && !(key is >= Key.F1 and <= Key.F24);
        if (bare)
        {
            e.Handled = true;
            var hotkey = new Hotkey(HotkeyModifiers.None, key);
            Hotkey = hotkey;
            BareRecorded?.Invoke(hotkey);
            Keyboard.ClearFocus();
            Text = hotkey.ToString();
            return;
        }

        base.OnPreviewKeyDown(e);
    }
}

/// <summary>Builders for the "Form" look of Vorssaint's settings pages (section header above a rounded group).</summary>
internal static class SettingsKit
{
    /// <summary>A section: small secondary header (optional) above a rounded <see cref="SettingsCard"/> holding the rows.</summary>
    public static StackPanel Section(string? header, params UIElement?[] rows)
    {
        var host = new StackPanel();
        if (!string.IsNullOrEmpty(header))
        {
            var title = UiKit.Text(header, 11.5, FontWeights.SemiBold, ThemeKeys.TextSecondary);
            title.Margin = new Thickness(6, 0, 0, 6);
            host.Children.Add(title);
        }

        var card = new SettingsCard();
        foreach (var row in rows)
        {
            if (row is not null)
            {
                card.Add(row);
            }
        }

        host.Children.Add(card);
        return host;
    }

    /// <summary>Stacks sections with 20 DIP spacing.</summary>
    public static StackPanel Page(params UIElement[] sections)
    {
        var panel = new StackPanel();
        foreach (var section in sections)
        {
            panel.AddSpaced(section, 20);
        }

        return panel;
    }

    /// <summary>A toggle row bound to a boolean app setting (caption under the title, switch on the right).</summary>
    public static SettingsRow Toggle(IAppSettings app, string key, string title, string? caption, Action<bool>? changed = null, bool? valueOverride = null)
    {
        var on = valueOverride ?? app.Get<bool>(key);
        return new SettingsRow(null, title, caption, new MacSwitch(on, SwitchSize.Regular, value =>
        {
            app.Set(key, value);
            changed?.Invoke(value);
        }));
    }

    /// <summary>Greys a block out (45 %) and makes it inert.</summary>
    public static T Disable<T>(T element, bool disabled) where T : UIElement
    {
        element.IsEnabled = !disabled;
        element.Opacity = disabled ? 0.45 : 1;
        return element;
    }

    /// <summary>"Shortcut" row: label, recorder (108 wide), Reset, then the orange error / Windows problem caption.</summary>
    public static StackPanel ShortcutRow(ISettingsContext c, string roleId, string label, bool enabled, bool allowBare = false, string? resetLabel = null)
    {
        var host = new StackPanel();
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = UiKit.Text(label, 13, FontWeights.Normal, ThemeKeys.TextPrimary);
        title.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(title);

        var error = Ui.Warning(string.Empty);
        error.Margin = new Thickness(0, 6, 0, 0);
        error.Visibility = Visibility.Collapsed;
        var problem = c.Shortcuts.GetState(roleId).Problem;
        if (enabled && !string.IsNullOrEmpty(problem))
        {
            error.Text = problem;
            error.Visibility = Visibility.Visible;
        }

        HotkeyRecorder recorder;
        BareHotkeyRecorder? bare = null;
        if (allowBare)
        {
            bare = new BareHotkeyRecorder();
            recorder = bare;
        }
        else
        {
            recorder = new HotkeyRecorder();
        }

        recorder.Width = 150;
        recorder.Hotkey = c.Shortcuts.GetHotkey(roleId);
        var reset = UiKit.Button(resetLabel ?? Strings.CutPaste.Reset, ButtonKind.Bordered, 12);
        reset.Margin = new Thickness(8, 0, 0, 0);
        reset.IsEnabled = enabled && !c.Shortcuts.IsDefault(roleId);

        void Apply(Hotkey? hotkey)
        {
            var result = c.Shortcuts.TrySet(roleId, hotkey);
            if (!result.Ok)
            {
                error.Text = result.Error ?? string.Empty;
                error.Visibility = Visibility.Visible;
                recorder.Hotkey = c.Shortcuts.GetHotkey(roleId);
                return;
            }

            var state = c.Shortcuts.GetState(roleId);
            error.Text = state.Problem ?? string.Empty;
            error.Visibility = string.IsNullOrEmpty(state.Problem) ? Visibility.Collapsed : Visibility.Visible;
            reset.IsEnabled = enabled && !c.Shortcuts.IsDefault(roleId);
        }

        recorder.HotkeyChanged += (_, hotkey) => Apply(hotkey);
        if (bare is not null)
        {
            bare.BareRecorded += hotkey => Apply(hotkey);
        }

        reset.Click += (_, _) =>
        {
            c.Shortcuts.Reset(roleId);
            recorder.Hotkey = c.Shortcuts.GetHotkey(roleId);
            error.Visibility = Visibility.Collapsed;
            reset.IsEnabled = false;
        };
        recorder.IsEnabled = enabled;
        Grid.SetColumn(recorder, 1);
        grid.Children.Add(recorder);
        Grid.SetColumn(reset, 2);
        grid.Children.Add(reset);
        host.Children.Add(grid);
        host.Children.Add(error);
        return host;
    }

    /// <summary>A row with a label on the left and a ComboBox on the right.</summary>
    public static UIElement Picker<T>(string title, IReadOnlyList<(T Value, string Label)> items, T selected, Action<T> changed, double width = 140) =>
        new PickerRow<T>(null, title, null, items, selected, changed, width);

    /// <summary>A horizontal "label ... field suffix" row.</summary>
    public static Grid FieldRow(string label, UIElement field, string? suffix)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = UiKit.Text(label, 13, FontWeights.Normal, ThemeKeys.TextPrimary);
        title.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(title);
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
        if (suffix is not null)
        {
            var text = UiKit.Text(suffix, 12.5, FontWeights.Normal, ThemeKeys.TextSecondary);
            text.Margin = new Thickness(8, 0, 0, 0);
            text.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(text, 2);
            grid.Children.Add(text);
        }

        return grid;
    }

    /// <summary>A footnote caption line indented under a toggle.</summary>
    public static TextBlock Note(string text) => Ui.Caption(text, 11);

    /// <summary>Numbered "How to use" bullet (accent circle with a white digit).</summary>
    public static UIElement Bullet(int number, string text)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var circle = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), VerticalAlignment = VerticalAlignment.Top, Child = UiKit.Text(number.ToString(), 11, FontWeights.Bold, ThemeKeys.OnAccent) };
        circle.SetResourceReference(Border.BackgroundProperty, ThemeKeys.Accent);
        ((TextBlock)circle.Child).HorizontalAlignment = HorizontalAlignment.Center;
        ((TextBlock)circle.Child).VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(circle);
        var label = UiKit.Text(text, 13, FontWeights.Normal, ThemeKeys.TextPrimary, wrap: true);
        label.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        return row;
    }

    /// <summary>A key cap: rounded square with a hairline border and the key name.</summary>
    public static Border KeyCap(string key, double size)
    {
        var cap = new Border
        {
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(7, 2, 7, 2),
            MinWidth = size * 2,
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = UiKit.Text(key, size, FontWeights.SemiBold, ThemeKeys.TextPrimary),
        };
        ((TextBlock)cap.Child).HorizontalAlignment = HorizontalAlignment.Center;
        cap.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
        cap.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Divider);
        return cap;
    }

    /// <summary>Key caps row: [Ctrl][X] text.</summary>
    public static UIElement KeyCapsRow(string[] keys, string text)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var caps = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var key in keys)
        {
            caps.Children.Add(KeyCap(key, 12));
        }

        row.Children.Add(caps);
        var label = UiKit.Text(text, 13, FontWeights.Normal, ThemeKeys.TextPrimary, wrap: true);
        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        return row;
    }
}
