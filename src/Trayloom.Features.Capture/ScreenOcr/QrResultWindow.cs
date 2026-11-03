// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Capture;

/// <summary>Shows what a scanned code holds before anything is copied: payload box, Copy and (for web links) Open link.</summary>
internal sealed class QrResultWindow : Window
{
    private static QrResultWindow? _current;

    private QrResultWindow(QrReading reading)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        var card = new Border { Width = 320, CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Padding = new Thickness(16), Margin = new Thickness(14) };
        card.SetResourceReference(Border.BackgroundProperty, ThemeKeys.PanelBase);
        card.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.CardBorder);
        card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 5, Opacity = 0.35, Direction = 270 };
        var stack = new StackPanel();
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(UiKit.Icon(G.QrCode, 14, ThemeKeys.Accent, FontWeights.SemiBold));
        var title = UiKit.Text(Strings.Ocr.QrTitle, 13, FontWeights.SemiBold);
        title.Margin = new Thickness(7, 0, 0, 0);
        header.Children.Add(title);
        stack.Children.Add(header);
        var box = new TextBox
        {
            Text = reading.Payload, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 132, FontSize = 12.5, Padding = new Thickness(10),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(1), Margin = new Thickness(0, 12, 0, 12),
            Background = Brushes.Transparent,
        };
        box.SetResourceReference(TextBox.ForegroundProperty, ThemeKeys.TextPrimary);
        box.SetResourceReference(TextBox.BorderBrushProperty, ThemeKeys.CardBorder);
        stack.Children.Add(box);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        void CopyIt()
        {
            Output.CopyText(reading.Payload);
            Close();
            QuickToolHud.Show(G.QrCode, Strings.Ocr.QrCopied);
        }

        if (reading.Url is { } url)
        {
            var copy = UiKit.Button(Strings.Ocr.Copy, ButtonKind.Bordered, 12, null, CopyIt);
            var open = UiKit.Button(Strings.Ocr.OpenLink, ButtonKind.Prominent, 12, null, () =>
            {
                Close();
                UiKit.OpenUrl(url);
            });
            open.IsDefault = true;
            copy.Margin = new Thickness(0, 0, 8, 0);
            buttons.Children.Add(copy);
            buttons.Children.Add(open);
        }
        else
        {
            var copy = UiKit.Button(Strings.Ocr.Copy, ButtonKind.Prominent, 12, null, CopyIt);
            copy.IsDefault = true;
            buttons.Children.Add(copy);
        }

        stack.Children.Add(buttons);
        card.Child = stack;
        Content = card;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
        Deactivated += (_, _) => Close();
        Closed += (_, _) =>
        {
            if (ReferenceEquals(_current, this))
            {
                _current = null;
            }
        };
    }

    public static void Show(QrReading reading, Window? owner)
    {
        _current?.Close();
        var w = new QrResultWindow(reading);
        _current = w;
        var (cx, cy) = ScreenGrab.CursorPos();
        var mon = ScreenGrab.MonitorAt(cx, cy);
        w.Opacity = 0;
        w.SourceInitialized += (_, _) => { };
        w.Show();
        w.UpdateLayout();
        var scale = mon.Scale;
        var pw = (int)(w.ActualWidth * scale);
        var ph = (int)(w.ActualHeight * scale);
        var x = Math.Clamp(cx - pw / 2, mon.Work.X + (int)(12 * scale), Math.Max(mon.Work.X, mon.Work.X + mon.Work.Width - pw - (int)(12 * scale)));
        var y = Math.Clamp(cy + (int)(18 * scale), mon.Work.Y + (int)(12 * scale), Math.Max(mon.Work.Y, mon.Work.Y + mon.Work.Height - ph - (int)(12 * scale)));
        N.SetWindowPos(new System.Windows.Interop.WindowInteropHelper(w).Handle, -1, x, y, pw, ph, 0x0040);
        w.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        w.Activate();
    }
}
