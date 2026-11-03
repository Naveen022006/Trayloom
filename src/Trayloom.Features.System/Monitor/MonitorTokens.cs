// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.SystemTools;

/// <summary>One option inside a token's popover (<c>MonitorTokenOption</c>): icon, title, small switch.</summary>
internal sealed record TokenOption(string Glyph, string Title, Func<bool> Get, Action<bool> Set, string? Tint = null);

/// <summary>
/// The compact label of one item (spec 02a 5.3): a check button and the name, plus an optional options half that opens a popover. Checked
/// and tinted when shown; the selected token carries an accent stroke.
/// </summary>
internal sealed class MonitorToken : Border
{
    private readonly bool _large;
    private readonly TextBlock _check;
    private readonly TextBlock _name;
    private readonly TextBlock? _summary;
    private readonly TextBlock? _chevron;
    private readonly Func<bool> _included;
    private bool _selected;

    public MonitorToken(string title, string glyph, Func<bool> included, Action<bool> setIncluded, IReadOnlyList<TokenOption>? options = null, Func<string>? summary = null, bool large = false, Action? select = null, Func<UIElement>? customOptions = null)
    {
        _large = large;
        _included = included;
        MinHeight = large ? 40 : 28;
        CornerRadius = new CornerRadius(large ? 10 : 8);
        BorderThickness = new Thickness(1);
        ToolTip = title;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(9, 4, 6, 4), VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        _check = UiKit.Icon(Gl.CheckCircle, large ? 14 : 13, ThemeKeys.Accent);
        _check.VerticalAlignment = VerticalAlignment.Center;
        _check.Margin = new Thickness(0, 0, 6, 0);
        var icon = UiKit.Icon(glyph, large ? 12 : 11, ThemeKeys.TextSecondary);
        icon.Margin = new Thickness(0, 0, 5, 0);
        _name = UiKit.Text(title, large ? 13 : 12, large ? FontWeights.Medium : FontWeights.Normal, ThemeKeys.TextPrimary, trimming: TextTrimming.CharacterEllipsis);
        _name.VerticalAlignment = VerticalAlignment.Center;
        left.Children.Add(_check);
        left.Children.Add(icon);
        left.Children.Add(_name);
        left.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && IsWithin(source, _check))
            {
                setIncluded(!_included());
                Refresh();
                return;
            }

            if (select is not null)
            {
                select();
            }
            else
            {
                setIncluded(!_included());
                Refresh();
            }
        };
        grid.Children.Add(left);

        if (options is { Count: > 0 } || customOptions is not null)
        {
            var divider = new Border { Width = 1, Height = 14, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            divider.SetResourceReference(Border.BackgroundProperty, ThemeKeys.Divider);
            var optionsHalf = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            _summary = UiKit.Text(summary?.Invoke() ?? string.Empty, 11, FontWeights.Normal, ThemeKeys.TextSecondary);
            _summary.Margin = new Thickness(8, 0, 4, 0);
            _chevron = UiKit.Icon(Gl.ChevronDown, 7, ThemeKeys.TextSecondary, FontWeights.Bold);
            optionsHalf.Children.Add(divider);
            optionsHalf.Children.Add(_summary);
            optionsHalf.Children.Add(_chevron);
            var popup = new Popup { PlacementTarget = optionsHalf, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade };
            optionsHalf.MouseLeftButtonUp += (_, _) =>
            {
                if (!_included())
                {
                    return;
                }

                var panel = new StackPanel { Width = 222 };
                var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
                head.Children.Add(UiKit.Icon(glyph, 12, ThemeKeys.TextPrimary, FontWeights.SemiBold));
                var t = UiKit.Text(title, 13, FontWeights.SemiBold, ThemeKeys.TextPrimary);
                t.Margin = new Thickness(6, 0, 0, 0);
                head.Children.Add(t);
                panel.Children.Add(head);
                if (customOptions is not null)
                {
                    panel.Children.Add(customOptions());
                }

                foreach (var option in options ?? [])
                {
                    var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.Children.Add(UiKit.Icon(option.Glyph, 11, option.Tint ?? ThemeKeys.TextSecondary));
                    var label = UiKit.Text(option.Title, 12, FontWeights.Normal, ThemeKeys.TextPrimary, wrap: true);
                    Grid.SetColumn(label, 2);
                    row.Children.Add(label);
                    var sw = new MacSwitch(option.Get(), SwitchSize.Small, on =>
                    {
                        option.Set(on);
                        if (_summary is not null && summary is not null)
                        {
                            _summary.Text = summary();
                        }
                    });
                    Grid.SetColumn(sw, 3);
                    row.Children.Add(sw);
                    panel.Children.Add(row);
                }

                var card = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(14), Margin = new Thickness(8), BorderThickness = new Thickness(0.7), Child = panel, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.35 } };
                card.SetResourceReference(Border.BackgroundProperty, ThemeKeys.PanelBase);
                card.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.CardBorder);
                popup.Child = card;
                popup.IsOpen = !popup.IsOpen;
            };
            Grid.SetColumn(optionsHalf, 1);
            grid.Children.Add(optionsHalf);
        }

        Child = grid;
        Refresh();
    }

    public bool IsSelected
    {
        get => _selected;
        set
        {
            _selected = value;
            Refresh();
        }
    }

    /// <summary>Re-reads the included state and repaints.</summary>
    public void Refresh()
    {
        var on = _included();
        this.Themed(BackgroundProperty, on ? ThemeKeys.AccentFill12 : ThemeKeys.ControlFill);
        if (_selected)
        {
            this.Themed(BorderBrushProperty, ThemeKeys.Accent);
        }
        else
        {
            BorderBrush = Brushes.Transparent;
        }

        _check.Text = on ? Gl.CheckCircle : Gl.Circle;
        _check.SetResourceReference(TextBlock.ForegroundProperty, on ? ThemeKeys.Accent : ThemeKeys.TextSecondary);
        _name.SetResourceReference(TextBlock.ForegroundProperty, on ? ThemeKeys.TextPrimary : ThemeKeys.TextSecondary);
        _summary?.SetResourceReference(TextBlock.ForegroundProperty, on ? ThemeKeys.Accent : ThemeKeys.TextSecondary);
        _chevron?.SetResourceReference(TextBlock.ForegroundProperty, on ? ThemeKeys.Accent : ThemeKeys.TextSecondary);
        _ = _large;
    }

    private static bool IsWithin(DependencyObject source, DependencyObject target)
    {
        for (var d = source; d is not null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (ReferenceEquals(d, target))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>A small numeric stepper: label and value on the left, minus / plus on the right.</summary>
internal sealed class Stepper : Grid
{
    public Stepper(string label, int value, int min, int max, int step, Func<int, string> format, Action<int> changed, double width = 220, bool mono = true)
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = UiKit.Text(string.Empty, 12, FontWeights.Normal, ThemeKeys.TextPrimary);
        if (mono)
        {
            text.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        }

        text.VerticalAlignment = VerticalAlignment.Center;
        var current = value;
        void Paint() => text.Text = $"{label} {format(current)}".Trim();
        Paint();
        Children.Add(text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Bits.FlatIcon("", null, () =>
        {
            current = Math.Max(min, current - step);
            Paint();
            changed(current);
        }, 10, 22));
        buttons.Children.Add(Bits.FlatIcon(Gl.Add, null, () =>
        {
            current = Math.Min(max, current + step);
            Paint();
            changed(current);
        }, 10, 22));
        SetColumn(buttons, 1);
        Children.Add(buttons);
        Width = width;
    }
}
