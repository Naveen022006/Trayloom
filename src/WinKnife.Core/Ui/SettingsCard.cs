// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;

namespace WinKnife.Core.Ui;

/// <summary>
/// v1 single-row card (title + description on the left, a control on the right). Kept for v1 feature views; new code uses
/// <see cref="SettingsCard"/> + <see cref="SettingsRow"/>.
/// </summary>
public sealed class LegacySettingsCard : Border
{
    /// <summary>Creates a card.</summary>
    /// <param name="title">Primary label.</param>
    /// <param name="description">Optional muted explanation below the title.</param>
    /// <param name="control">Optional control shown at the right edge.</param>
    public LegacySettingsCard(string title, string? description = null, UIElement? control = null)
    {
        SetResourceReference(StyleProperty, ThemeKeys.CardStyle);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleBlock = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Text);
        text.Children.Add(titleBlock);
        if (!string.IsNullOrEmpty(description))
        {
            var desc = new TextBlock { Text = description, Margin = new Thickness(0, 2, 0, 0) };
            desc.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.MutedTextStyle);
            text.Children.Add(desc);
        }

        grid.Children.Add(text);
        if (control is not null)
        {
            if (control is FrameworkElement element)
            {
                element.VerticalAlignment = VerticalAlignment.Center;
                element.Margin = new Thickness(16, 0, 0, 0);
            }

            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
        }

        Child = grid;
    }
}

/// <summary>A value/label pair for <see cref="SettingsViewBuilder.AddChoice{T}"/>.</summary>
public sealed record Choice<T>(T Value, string Label);

/// <summary>
/// Fluent builder for feature settings views. Each <c>Add*</c> creates a <see cref="SettingsCard"/> bound to a key of
/// the feature's <see cref="IFeatureSettings"/> (reads the current value, writes on change).
/// </summary>
public sealed class SettingsViewBuilder
{
    private readonly IFeatureSettings _settings;
    private readonly StackPanel _panel = new() { Margin = new Thickness(0, 0, 0, 4) };

    /// <summary>Creates a builder writing to <paramref name="settings"/>.</summary>
    public SettingsViewBuilder(IFeatureSettings settings)
    {
        _settings = settings;
    }

    /// <summary>Adds a switch bound to a boolean setting.</summary>
    public SettingsViewBuilder AddToggle(string key, string title, bool fallback, string? description = null)
    {
        var toggle = new MacSwitch(_settings.Get(key, fallback), SwitchSize.Regular, on => _settings.Set(key, on));
        return Add(Row(title, description, toggle));
    }

    /// <summary>Adds a slider bound to a double setting.</summary>
    public SettingsViewBuilder AddSlider(string key, string title, double min, double max, double fallback, double step = 1, string format = "0.##", string? description = null)
    {
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(_settings.Get(key, fallback), min, max),
            Width = 180,
            TickFrequency = step,
            IsSnapToTickEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var label = new TextBlock { Width = 44, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        label.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextMuted);
        label.Text = slider.Value.ToString(format, System.Globalization.CultureInfo.CurrentCulture);
        slider.ValueChanged += (_, e) =>
        {
            label.Text = e.NewValue.ToString(format, System.Globalization.CultureInfo.CurrentCulture);
            _settings.Set(key, e.NewValue);
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(slider);
        row.Children.Add(label);
        return Add(Row(title, description, row));
    }

    /// <summary>Adds a slider bound to an integer setting.</summary>
    public SettingsViewBuilder AddIntSlider(string key, string title, int min, int max, int fallback, string? description = null)
    {
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(_settings.Get(key, fallback), min, max),
            Width = 180,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var label = new TextBlock { Width = 44, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Text = ((int)slider.Value).ToString() };
        label.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextMuted);
        slider.ValueChanged += (_, e) =>
        {
            var value = (int)Math.Round(e.NewValue);
            label.Text = value.ToString();
            _settings.Set(key, value);
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(slider);
        row.Children.Add(label);
        return Add(Row(title, description, row));
    }

    /// <summary>Adds a drop-down bound to a setting of type <typeparamref name="T"/> (string, int, enum...).</summary>
    public SettingsViewBuilder AddChoice<T>(string key, string title, IReadOnlyList<Choice<T>> choices, T fallback, string? description = null)
    {
        var combo = new ComboBox
        {
            ItemsSource = choices,
            DisplayMemberPath = nameof(Choice<T>.Label),
            MinWidth = 160,
        };
        var current = _settings.Get(key, fallback);
        combo.SelectedItem = choices.FirstOrDefault(c => EqualityComparer<T>.Default.Equals(c.Value, current)) ?? choices.FirstOrDefault();
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is Choice<T> choice)
            {
                _settings.Set(key, choice.Value);
            }
        };
        return Add(Row(title, description, combo));
    }

    /// <summary>Adds a hotkey recorder bound to a <see cref="Hotkey"/> setting (null = not set).</summary>
    public SettingsViewBuilder AddHotkey(string key, string title, Hotkey? fallback, string? description = null)
    {
        var recorder = new HotkeyRecorder { Hotkey = _settings.Get(key, fallback) };
        recorder.HotkeyChanged += (_, hotkey) => _settings.Set(key, hotkey);
        return Add(Row(title, description, recorder));
    }

    /// <summary>Adds a single-line text box bound to a string setting.</summary>
    public SettingsViewBuilder AddText(string key, string title, string fallback, string? description = null)
    {
        var box = new TextBox { Text = _settings.Get(key, fallback), MinWidth = 220 };
        box.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.TextBoxStyle);
        box.TextChanged += (_, _) => _settings.Set(key, box.Text);
        return Add(Row(title, description, box));
    }

    private static SettingsCard Row(string title, string? description, UIElement accessory) =>
        new SettingsCard().Add(new SettingsRow(null, title, description, accessory));

    /// <summary>Adds a muted explanatory paragraph.</summary>
    public SettingsViewBuilder AddNote(string text)
    {
        var block = new TextBlock { Text = text, Margin = new Thickness(2, 0, 2, 0) };
        block.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.MutedTextStyle);
        block.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextSecondary);
        return Add(block);
    }

    /// <summary>Adds an arbitrary element (e.g. a custom <see cref="SettingsCard"/>).</summary>
    public SettingsViewBuilder Add(UIElement element)
    {
        if (element is FrameworkElement fe && _panel.Children.Count > 0)
        {
            fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top + 8, fe.Margin.Right, fe.Margin.Bottom);
        }

        _panel.Children.Add(element);
        return this;
    }

    /// <summary>Returns the finished view (a vertical stack of cards).</summary>
    public FrameworkElement Build() => _panel;
}
