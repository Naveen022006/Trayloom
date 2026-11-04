// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Trayloom.Core.Ui;

namespace Trayloom.Features.GitHub;

/// <summary>The island kit of the GitHub page: white on black at fixed opacities, the three check colours, flat buttons and pills.</summary>
internal static class GitHubLook
{
    public static readonly Color Green = Color.FromRgb(0x30, 0xD1, 0x58);
    public static readonly Color Red = Color.FromRgb(0xFF, 0x45, 0x3A);
    public static readonly Color Amber = Color.FromRgb(0xFF, 0x9F, 0x0A);

    public static Brush White(double opacity)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * opacity), 255, 255, 255));
        brush.Freeze();
        return brush;
    }

    public static Brush Of(Color color, double opacity = 1)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(color.A * opacity), color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    public static Color StateColor(CheckState state) => state switch
    {
        CheckState.Passing => Green,
        CheckState.Failing => Red,
        CheckState.Running => Amber,
        _ => Color.FromArgb(0x52, 255, 255, 255),
    };

    public static TextBlock Text(string text, double size, FontWeight? weight = null, Brush? brush = null, bool ellipsis = false, bool mono = false, bool wrap = false)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight ?? FontWeights.Normal,
            Foreground = brush ?? White(1),
            FontFamily = UiKit.TextFont,
            TextTrimming = ellipsis ? TextTrimming.CharacterEllipsis : TextTrimming.None,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (mono)
        {
            Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular);
        }

        return block;
    }

    public static TextBlock Icon(string glyph, double size, Brush? brush = null) => new()
    {
        Text = glyph,
        FontFamily = UiKit.IconFont,
        FontSize = size,
        Foreground = brush ?? White(0.6),
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        TextAlignment = TextAlignment.Center,
    };

    public static void Tip(FrameworkElement element, string text)
    {
        element.ToolTip = new ToolTip { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 } };
        ToolTipService.SetInitialShowDelay(element, 500);
    }

    /// <summary>
    /// A flat clickable surface that is a real <see cref="Button"/> (so UI Automation can find and invoke it) with its own template:
    /// no chrome, a fill that changes on hover, and optionally a 1 px border.
    /// </summary>
    public static Button Flat(UIElement content, Action onClick, Brush fill, Brush hoverFill, CornerRadius radius, Thickness padding, string automationId, string name, Brush? border = null)
    {
        var template = new ControlTemplate(typeof(Button));
        var root = new FrameworkElementFactory(typeof(Border));
        root.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        root.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        root.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        root.SetValue(Border.CornerRadiusProperty, radius);
        root.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        root.AppendChild(presenter);
        template.VisualTree = root;
        var button = new Button
        {
            Template = template,
            Content = content,
            Background = fill,
            BorderBrush = border ?? Brushes.Transparent,
            BorderThickness = border is null ? new Thickness(0) : new Thickness(1),
            Padding = padding,
            Cursor = Cursors.Hand,
            Focusable = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(button, automationId);
        AutomationProperties.SetName(button, name);
        button.MouseEnter += (_, _) => button.Background = hoverFill;
        button.MouseLeave += (_, _) => button.Background = fill;
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>A list row: hover fill white 0.06, radius 8.</summary>
    public static Button Row(UIElement content, Action onClick, string automationId, string name) =>
        Flat(content, onClick, Brushes.Transparent, White(0.06), new CornerRadius(8), new Thickness(6, 0, 6, 0), automationId, name);

    /// <summary>A pill button (radius 10).</summary>
    public static Button Pill(string text, Action onClick, string automationId, Color? tint = null)
    {
        var label = Text(text, 11, FontWeights.Medium, tint is { } t ? Of(t) : White(0.9));
        return Flat(label, onClick, White(0.12), White(0.2), new CornerRadius(10), new Thickness(10, 4, 10, 4), automationId, text);
    }
}
