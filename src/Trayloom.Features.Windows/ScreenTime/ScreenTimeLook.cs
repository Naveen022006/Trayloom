// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.WindowTools.ScreenTime;

/// <summary>The island is always dark: fixed colours and small builders for its controls (modelled on the AI Agents kit, not shared with it).</summary>
internal static class ScreenTimeLook
{
    /// <summary>The module tint <c>#30B0C7</c>.</summary>
    public static readonly Color Tint = Color.FromRgb(0x30, 0xB0, 0xC7);

    private static readonly Dictionary<(uint, int), SolidColorBrush> Cache = [];

    /// <summary>A frozen brush for <paramref name="color"/> at <paramref name="opacity"/>.</summary>
    public static Brush Of(Color color, double opacity = 1)
    {
        var key = ((uint)(color.R << 16 | color.G << 8 | color.B), (int)Math.Round(opacity * 1000));
        lock (Cache)
        {
            if (!Cache.TryGetValue(key, out var brush))
            {
                brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * opacity), color.R, color.G, color.B));
                brush.Freeze();
                Cache[key] = brush;
            }

            return brush;
        }
    }

    public static Brush White(double opacity) => Of(Colors.White, opacity);

    public static TextBlock Text(string text, double size, FontWeight? weight = null, Brush? brush = null, bool mono = false, bool ellipsis = false)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight ?? FontWeights.Normal,
            Foreground = brush ?? White(1),
            FontFamily = UiKit.TextFont,
            TextTrimming = ellipsis ? TextTrimming.CharacterEllipsis : TextTrimming.None,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (mono)
        {
            System.Windows.Documents.Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular);
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
        TextAlignment = TextAlignment.Center,
    };

    public static void Tip(FrameworkElement element, string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        element.ToolTip = new ToolTip { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 } };
        ToolTipService.SetInitialShowDelay(element, 500);
    }

    /// <summary>Gives a control its UI Automation id and name.</summary>
    public static void Name(DependencyObject element, string id, string name)
    {
        AutomationProperties.SetAutomationId(element, id);
        AutomationProperties.SetName(element, name);
    }

    /// <summary>
    /// Wraps a plain panel in a <see cref="UserControl"/> so UI Automation exposes it (panels have no automation peer) under
    /// <paramref name="id"/>.
    /// </summary>
    public static UserControl Expose(FrameworkElement content, string id, string name)
    {
        var host = new UserControl { Content = content, Focusable = false, IsTabStop = false, Padding = new Thickness(0) };
        Name(host, id, name);
        return host;
    }

    /// <summary>A flat button that can be invoked through UI Automation: transparent chrome, a fill of <paramref name="fill"/> and hover tint.</summary>
    public static Button FlatButton(UIElement content, Action onClick, string automationId, string name, double radius = 10, Thickness? padding = null, double fill = 0.12)
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border), "chrome");
        border.SetValue(Border.BackgroundProperty, White(fill));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        border.SetValue(Border.PaddingProperty, padding ?? new Thickness(12, 5, 12, 5));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, White(fill + 0.1), "chrome"));
        template.Triggers.Add(hover);
        var button = new Button
        {
            Content = content,
            Template = template,
            Cursor = Cursors.Hand,
            Focusable = false,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        button.Click += (_, _) => onClick();
        Name(button, automationId, name);
        return button;
    }
}

/// <summary>Fire-and-forget with a top-level try/catch, so an exception is logged and never takes the process down.</summary>
internal static class ScreenTimeGuard
{
    internal static async void FireAndLog(Func<Task> work, ILog log, string what)
    {
        try
        {
            await work();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            try
            {
                log.Error(what + " failed", ex);
            }
            catch
            {
                // a logger that fails has nothing left to report to
            }
        }
    }
}
