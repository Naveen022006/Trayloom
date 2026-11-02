// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;

namespace WinKnife.Features.SystemTools;

/// <summary>Small non-activating pill (icon + text) near the bottom or a corner of the primary monitor.</summary>
internal sealed class Osd : OverlayWindow
{
    private readonly TextBlock _glyph;
    private readonly TextBlock _text;
    private readonly Border _pill;
    private readonly DispatcherTimer _hideTimer;
    private readonly bool _corner;

    public Osd(bool corner = false)
    {
        _corner = corner;
        SizeToContent = SizeToContent.WidthAndHeight;
        ClickThrough = true;

        _glyph = new TextBlock { FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 18, VerticalAlignment = VerticalAlignment.Center };
        _glyph.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.OverlayText);
        _text = new TextBlock { FontSize = 14, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 420, TextTrimming = TextTrimming.CharacterEllipsis };
        _text.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.OverlayText);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_glyph);
        row.Children.Add(_text);
        _pill = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(16, 9, 18, 9), Margin = new Thickness(8), Child = row };
        _pill.SetResourceReference(Border.BackgroundProperty, ThemeKeys.OverlayBackground);
        Content = _pill;

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
        };
    }

    /// <summary>Shows the pill; when <paramref name="durationMs"/> is 0 it stays until <see cref="Dismiss"/>.</summary>
    public void Display(string glyph, string text, int durationMs = 1600)
    {
        _glyph.Text = glyph;
        _text.Text = text;
        _hideTimer.Stop();
        if (!IsVisible)
        {
            ShowNoActivate();
        }

        UpdateLayout();
        Place();
        if (durationMs > 0)
        {
            _hideTimer.Interval = TimeSpan.FromMilliseconds(durationMs);
            _hideTimer.Start();
        }
    }

    public void Dismiss()
    {
        _hideTimer.Stop();
        if (IsVisible)
        {
            Hide();
        }
    }

    private void Place()
    {
        var handle = Handle;
        if (handle == 0)
        {
            return;
        }

        var area = Screens.Primary();
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var w = ActualWidth * dpi;
        var h = ActualHeight * dpi;
        double x;
        double y;
        if (_corner)
        {
            x = area.WorkArea.Right - w - (12 * dpi);
            y = area.WorkArea.Top + (12 * dpi);
        }
        else
        {
            x = area.WorkArea.Left + ((area.WorkArea.Width - w) / 2);
            y = area.WorkArea.Bottom - h - (72 * dpi);
        }

        NativeMethods.SetWindowPos(handle, NativeConstants.HWND_TOPMOST, (int)x, (int)y, 0, 0, NativeConstants.SWP_NOSIZE | NativeConstants.SWP_NOACTIVATE);
    }
}
