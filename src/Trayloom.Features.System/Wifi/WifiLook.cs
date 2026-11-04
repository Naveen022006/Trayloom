// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.SystemTools.Wifi;

/// <summary>The island is always black: fixed colours and small builders for its dark controls (spec 06 section 0.2).</summary>
internal static class WifiLook
{
    public static readonly Color Blue = Color.FromRgb(0x32, 0xAD, 0xE6);

    public static readonly Color Orange = Color.FromRgb(0xFF, 0x9F, 0x0A);

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

    public static TextBlock Text(string text, double size, FontWeight? weight = null, Brush? brush = null, TextTrimming trimming = TextTrimming.None) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight ?? FontWeights.Normal,
        Foreground = brush ?? White(1),
        FontFamily = UiKit.TextFont,
        TextTrimming = trimming,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static TextBlock Icon(string glyph, double size, Brush? brush = null) => new()
    {
        Text = glyph,
        FontFamily = UiKit.IconFont,
        FontSize = size,
        Foreground = brush ?? White(0.6),
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
    };

    /// <summary>Names an element for UI Automation (id and visible label).</summary>
    public static T Automate<T>(T element, string id, string name) where T : DependencyObject
    {
        AutomationProperties.SetAutomationId(element, id);
        AutomationProperties.SetName(element, name);
        return element;
    }

    /// <summary>A pill with white text on white 0.1 (hover 0.16); the whole pill is the button.</summary>
    public static Button Pill(string text, string automationId, Action onClick, string? glyph = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (glyph is not null)
        {
            var icon = Icon(glyph, 11, White(0.9));
            icon.Margin = new Thickness(0, 0, 5, 0);
            row.Children.Add(icon);
        }

        row.Children.Add(Text(text, 11.5, FontWeights.Medium, White(0.95)));
        var box = new Border { Child = row, Background = White(0.1), CornerRadius = new CornerRadius(10), Padding = new Thickness(11, 4, 11, 5) };
        var button = new Button { Content = box, Style = PlainStyle, Cursor = Cursors.Hand, Focusable = false };
        button.MouseEnter += (_, _) => box.Background = White(0.16);
        button.MouseLeave += (_, _) => box.Background = White(0.1);
        button.Click += (_, _) => onClick();
        return Automate(button, automationId, text);
    }

    /// <summary>No chrome: the content is the button.</summary>
    public static readonly Style PlainStyle = CreatePlain();

    private static Style CreatePlain()
    {
        var template = new ControlTemplate(typeof(Button));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        // Pills keep the default centred content; list rows set HorizontalContentAlignment to Stretch.
        presenter.SetBinding(ContentPresenter.HorizontalAlignmentProperty, new System.Windows.Data.Binding(nameof(Control.HorizontalContentAlignment)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        template.VisualTree = presenter;
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        style.Seal();
        return style;
    }

    /// <summary>A small rotating arc; it animates only while it is loaded.</summary>
    public static FrameworkElement Spinner(double size)
    {
        var rotate = new RotateTransform();
        var arc = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 6,1.2 A 4.8,4.8 0 1 1 1.2,6"),
            Stroke = White(0.75),
            StrokeThickness = 1.4,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Width = 12,
            Height = 12,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = rotate,
        };
        var host = new Viewbox { Width = size, Height = size, Child = new Canvas { Width = 12, Height = 12, Children = { arc } }, IsHitTestVisible = false };
        host.Loaded += (_, _) => rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
        host.Unloaded += (_, _) => rotate.BeginAnimation(RotateTransform.AngleProperty, null);
        return host;
    }
}

/// <summary>
/// The island-style switch of the top row: white knob, blue track when on, 34 by 19 (spec 3.5). A real <see cref="ToggleButton"/> so UI
/// Automation reads (and could operate) it through the standard Toggle pattern; a toggle asks for the opposite state, and the shown state only
/// follows what the radio really does.
/// </summary>
internal sealed class WifiSwitch : ToggleButton
{
    private static readonly ControlTemplate PresenterOnly = CreateTemplate();

    private readonly Border _track;
    private readonly Border _knob;
    private readonly Action<bool> _onClick;

    public WifiSwitch(Action<bool> onClick)
    {
        _onClick = onClick;
        Template = PresenterOnly;
        Cursor = Cursors.Hand;
        Focusable = false;
        _knob = new Border { Width = 15, Height = 15, CornerRadius = new CornerRadius(7.5), Background = Brushes.White, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        _track = new Border { Width = 34, Height = 19, CornerRadius = new CornerRadius(9.5), Child = _knob };
        Content = _track;
        IsEnabledChanged += (_, _) => Apply();
        Apply();
        WifiLook.Automate(this, "Wifi.Radio", WifiStrings.Title);
    }

    /// <summary>Shows the state without asking for a change (the radio itself changes through <c>WifiService</c>).</summary>
    public bool IsOn
    {
        get => IsChecked == true;
        set
        {
            IsChecked = value;
            Apply();
        }
    }

    /// <summary>A click or the Toggle pattern asks for the opposite state; the shown state is not flipped here.</summary>
    protected override void OnToggle()
    {
        if (IsEnabled)
        {
            _onClick(!IsOn);
        }
    }

    private void Apply()
    {
        _track.Background = IsOn ? WifiLook.Of(WifiLook.Blue) : WifiLook.White(0.2);
        _knob.Margin = new Thickness(IsOn ? 17 : 2, 0, 0, 0);
        Opacity = IsEnabled ? 1 : 0.4;
    }

    private static ControlTemplate CreateTemplate()
    {
        var template = new ControlTemplate(typeof(ToggleButton)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
        template.Seal();
        return template;
    }
}

/// <summary>The network list: a stack that shows up in UI Automation (a plain stack panel has no peer) so the rows can be found under it.</summary>
internal sealed class WifiList : StackPanel
{
    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new ListPeer(this);

    private sealed class ListPeer(WifiList owner) : System.Windows.Automation.Peers.FrameworkElementAutomationPeer(owner)
    {
        protected override System.Windows.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() => System.Windows.Automation.Peers.AutomationControlType.List;

        protected override string GetClassNameCore() => "WifiList";
    }
}

/// <summary>Starts async work and logs a failure instead of letting an <c>async void</c> take the process down.</summary>
internal static class WifiGuard
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
                log.Warn($"[wifi] {what} failed: {ex.GetType().Name} 0x{ex.HResult:X8}");
            }
            catch
            {
                // a logger that fails has nothing left to report to
            }
        }
    }
}
