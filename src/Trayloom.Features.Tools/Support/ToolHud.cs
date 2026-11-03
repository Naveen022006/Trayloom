// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Tools;

/// <summary>
/// Vorssaint's <c>QuickToolHUD</c>: a non-activating, click-through toast (icon or colour swatch + one or two lines of text)
/// centred 24 px below the top of the screen under the pointer. Fade in 0.12 s, stay 1.5 s, fade out 0.22 s.
/// </summary>
internal static class ToolHud
{
    private static HudWindow? _window;

    /// <summary>Shows a confirmation. Safe from any thread.</summary>
    public static void Show(string glyph, string message, Color? swatch = null)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        if (app.Dispatcher.CheckAccess())
        {
            ShowCore(glyph, message, swatch);
        }
        else
        {
            app.Dispatcher.BeginInvoke(() => ShowCore(glyph, message, swatch));
        }
    }

    private static void ShowCore(string glyph, string message, Color? swatch)
    {
        _window ??= new HudWindow();
        _window.Present(glyph, message, swatch);
    }

    private sealed class HudWindow : OverlayWindow
    {
        private readonly Border _pill = new();
        private readonly TextBlock _text = new() { FontSize = 12, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 360, MaxHeight = 34 };
        private readonly ContentControl _lead = new();
        private readonly DispatcherTimer _dismiss = new() { Interval = TimeSpan.FromMilliseconds(1500) };
        private int _generation;

        public HudWindow()
        {
            ClickThrough = true;
            SizeToContent = SizeToContent.WidthAndHeight;
            _text.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextPrimary);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            _lead.VerticalAlignment = VerticalAlignment.Center;
            _lead.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(_lead);
            row.Children.Add(_text);
            _pill.CornerRadius = new CornerRadius(10);
            _pill.Padding = new Thickness(14, 9, 14, 9);
            _pill.BorderThickness = new Thickness(1);
            _pill.Margin = new Thickness(12);
            _pill.Child = row;
            _pill.SetResourceReference(Border.BackgroundProperty, ThemeKeys.PanelBase);
            _pill.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.CardBorder);
            _pill.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.3, Direction = 270 };
            Content = _pill;
            Opacity = 0;
            _dismiss.Tick += (_, _) =>
            {
                _dismiss.Stop();
                FadeOut(_generation);
            };
        }

        public void Present(string glyph, string message, Color? swatch)
        {
            _generation++;
            _dismiss.Stop();
            _text.Text = message;
            if (swatch is { } color)
            {
                _lead.Content = new Border
                {
                    Width = 18,
                    Height = 18,
                    CornerRadius = new CornerRadius(5),
                    Background = new SolidColorBrush(color),
                    BorderThickness = new Thickness(0.5),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 128, 128, 128)),
                };
            }
            else
            {
                _lead.Content = UiKit.Icon(glyph, 13, ThemeKeys.Accent, FontWeights.SemiBold);
            }

            if (!IsVisible)
            {
                Opacity = 0;
                ShowNoActivate();
            }

            UpdateLayout();
            var area = Screens.UnderCursor().WorkArea;
            var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            var width = ActualWidth * dpi;
            Left = (area.Left + area.Width / 2 - width / 2) / dpi;
            Top = (area.Top + 12) / dpi;
            BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
            _dismiss.Start();
        }

        private void FadeOut(int generation)
        {
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
            fade.Completed += (_, _) =>
            {
                if (generation == _generation)
                {
                    Hide();
                }
            };
            BeginAnimation(OpacityProperty, fade);
        }
    }
}
