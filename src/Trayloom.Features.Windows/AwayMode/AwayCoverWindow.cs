// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Trayloom.Core;
using Trayloom.Core.Native;
using Trayloom.Core.Ui;

namespace Trayloom.Features.WindowTools.AwayMode;

/// <summary>
/// The optional dark cover over one display while the person is away (spec 06 section 4.8). It never takes the focus and does not
/// capture input (input is read with <c>GetLastInputInfo</c>); it is a Trayloom window, so it dies with the process and a crash can
/// never leave a screen covered.
/// </summary>
internal sealed class AwayCoverWindow : Window
{
    private static readonly SolidColorBrush Background0 = Frozen(new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0D)));
    private static readonly SolidColorBrush White = Frozen(new SolidColorBrush(Colors.White));
    private static readonly SolidColorBrush White60 = Frozen(new SolidColorBrush(Color.FromArgb(153, 255, 255, 255)));
    private static readonly SolidColorBrush White12 = Frozen(new SolidColorBrush(Color.FromArgb(31, 255, 255, 255)));
    private static readonly SolidColorBrush Amber = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)));

    private readonly RECT _bounds;
    private readonly TextBlock? _notice;
    private readonly TextBlock? _closeLine1;
    private readonly TextBlock? _closeLine2;

    /// <param name="bounds">The monitor's rectangle in physical pixels.</param>
    /// <param name="primary">The primary display's cover carries the text and the button; the others are plain.</param>
    /// <param name="since">Local time the person left.</param>
    /// <param name="looking">Face detection is running.</param>
    /// <param name="back">Called when the person presses "I’m back".</param>
    public AwayCoverWindow(RECT bounds, bool primary, DateTime since, bool looking, Action back)
    {
        _bounds = bounds;
        Title = AwayStrings.Title;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Background0;
        AllowsTransparency = false;

        var root = new Grid { Background = Background0 };
        if (primary)
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 460 };
            stack.Children.Add(Label(AwayStrings.CoverAway, 34, FontWeights.SemiBold, White));
            stack.Children.Add(Label(AwayStrings.CoverSince(since.ToString("t")), 14, FontWeights.Normal, White60, 6));
            _closeLine1 = Label(string.Empty, 12, FontWeights.Normal, White60, 10);
            _closeLine1.TextWrapping = TextWrapping.Wrap;
            _closeLine1.Visibility = Visibility.Collapsed;
            stack.Children.Add(_closeLine1);
            _closeLine2 = Label(string.Empty, 12, FontWeights.Normal, White60, 2);
            _closeLine2.TextWrapping = TextWrapping.Wrap;
            _closeLine2.Visibility = Visibility.Collapsed;
            stack.Children.Add(_closeLine2);
            if (looking)
            {
                var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0) };
                line.Children.Add(new TextBlock { Text = "", FontFamily = UiKit.IconFont, FontSize = 13, Foreground = White60, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
                line.Children.Add(new TextBlock { Text = AwayStrings.CoverLooking, FontSize = 12, Foreground = White60, VerticalAlignment = VerticalAlignment.Center });
                stack.Children.Add(line);
            }

            _notice = Label(AwayStrings.CoverNotRecognised, 12, FontWeights.Normal, Amber, 10);
            _notice.TextWrapping = TextWrapping.Wrap;
            _notice.Visibility = Visibility.Collapsed;
            stack.Children.Add(_notice);

            var button = new Button
            {
                Content = new TextBlock { Text = AwayStrings.ImBack, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = White },
                Padding = new Thickness(22, 9, 22, 9),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 24, 0, 0),
                Cursor = Cursors.Hand,
                Template = CapsuleTemplate(),
            };
            AutomationProperties.SetAutomationId(button, "AwayCover.Back");
            AutomationProperties.SetName(button, AwayStrings.ImBack);
            button.Click += (_, _) => back();
            stack.Children.Add(button);
            root.Children.Add(stack);
        }

        Content = root;
    }

    /// <summary>Shows what happened to the apps that are closed while away (primary display only; null hides a line).</summary>
    public void SetCloseStatus(string? line1, string? line2)
    {
        Apply(_closeLine1, line1);
        Apply(_closeLine2, line2);
    }

    private static void Apply(TextBlock? block, string? text)
    {
        if (block is null)
        {
            return;
        }

        block.Text = text ?? string.Empty;
        block.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Shows the amber "Not recognised?" line after an unsuccessful quick look.</summary>
    public void ShowNotRecognised()
    {
        if (_notice is not null)
        {
            _notice.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Shows the cover on its display without taking the focus.</summary>
    public void ShowCover()
    {
        var scale = WinUtil.PrepareOnMonitorAt(this, _bounds.Left, _bounds.Top);
        Left = _bounds.Left / scale;
        Top = _bounds.Top / scale;
        Width = _bounds.Width / scale;
        Height = _bounds.Height / scale;

        var handle = new WindowInteropHelper(this).EnsureHandle();
        var style = NativeMethods.GetWindowLongPtr(handle, NativeConstants.GWL_EXSTYLE).ToInt64();
        NativeMethods.SetWindowLongPtr(handle, NativeConstants.GWL_EXSTYLE, (nint)(style | NativeConstants.WS_EX_TOOLWINDOW | NativeConstants.WS_EX_NOACTIVATE));
        Show();
        NativeMethods.SetWindowPos(handle, NativeConstants.HWND_TOPMOST, _bounds.Left, _bounds.Top, _bounds.Width, _bounds.Height, NativeConstants.SWP_NOACTIVATE);
    }

    private static TextBlock Label(string text, double size, FontWeight weight, Brush brush, double top = 0)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight,
            Foreground = brush,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, top, 0, 0),
        };
    }

    private static ControlTemplate CapsuleTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, White12);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(100));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        return template;
    }

    private static SolidColorBrush Frozen(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
