// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Capture;

/// <summary>
/// Non-activating confirmation toast (top centre of the pointer's monitor, 24 DIP below the top). Fade in 0.12 s, visible 1.5 s,
/// fade out 0.22 s; always excluded from captures. <see cref="ShowCountdown"/> draws the 82x82 ring used by delays.
/// </summary>
internal static class QuickToolHud
{
    private static HudWindow? _toast;
    private static HudWindow? _count;

    public static void Show(string glyph, string message, System.Windows.Media.Color? swatch = null)
    {
        CaptureHost.Ui.BeginInvoke(() =>
        {
            _toast?.Close();
            var content = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 9, 14, 9) };
            if (swatch is { } c)
            {
                var sw = new Border
                {
                    Width = 18, Height = 18, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(c),
                    BorderThickness = new Thickness(0.5), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
                };
                sw.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.CardBorder);
                content.Children.Add(sw);
            }
            else
            {
                var icon = UiKit.Icon(glyph, 13, ThemeKeys.Accent, FontWeights.SemiBold);
                icon.Margin = new Thickness(0, 0, 8, 0);
                content.Children.Add(icon);
            }

            var text = UiKit.Text(message, 12, FontWeights.SemiBold, ThemeKeys.TextPrimary, wrap: true, trimming: TextTrimming.CharacterEllipsis);
            text.MaxWidth = 360;
            text.MaxHeight = 36;
            content.Children.Add(text);
            var card = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Child = content };
            card.SetResourceReference(Border.BackgroundProperty, ThemeKeys.PanelBase);
            card.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.CardBorder);
            var w = new HudWindow(card, 1500);
            _toast = w;
            w.Closed += (_, _) =>
            {
                if (ReferenceEquals(_toast, w))
                {
                    _toast = null;
                }
            };
            w.Present();
        });
    }

    /// <summary>Shows the 82x82 ring with a digit for 0.92 s.</summary>
    public static void ShowCountdown(int number)
    {
        CaptureHost.Ui.BeginInvoke(() =>
        {
            _count?.Close();
            var grid = new Grid { Width = 82, Height = 82, Margin = new Thickness(12) };
            var disc = new Ellipse { Stroke = new SolidColorBrush(Color.FromArgb(31, 255, 255, 255)), StrokeThickness = 1 };
            disc.SetResourceReference(Shape.FillProperty, ThemeKeys.PanelBase);
            grid.Children.Add(disc);
            var arc = new Ellipse { Margin = new Thickness(6), StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(-90) };
            arc.SetResourceReference(Shape.StrokeProperty, ThemeKeys.Accent);
            grid.Children.Add(arc);
            var num = UiKit.Text(number.ToString(), 32, FontWeights.Bold, ThemeKeys.TextPrimary);
            num.HorizontalAlignment = HorizontalAlignment.Center;
            num.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(num);
            var w = new HudWindow(grid, 920, shadow: false);
            _count = w;
            var start = DateTime.UtcNow;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            timer.Tick += (_, _) =>
            {
                var p = Math.Clamp(1 - (DateTime.UtcNow - start).TotalSeconds / 0.92, 0, 1);
                // The arc length goes from full to empty (dash array is in units of stroke thickness).
                var circumference = Math.PI * (82 - 12);
                var len = circumference * 0.92 * p / 3;
                arc.StrokeDashArray = new DoubleCollection { Math.Max(0.001, len), 1000 };
                if (p <= 0)
                {
                    timer.Stop();
                }
            };
            timer.Start();
            w.Closed += (_, _) => timer.Stop();
            w.Present();
        });
    }

    public static void Dismiss()
    {
        CaptureHost.Ui.BeginInvoke(() =>
        {
            _toast?.Close();
            _count?.Close();
        });
    }

    private sealed class HudWindow : OverlayWindow
    {
        private readonly int _visibleMs;
        private readonly DispatcherTimer _timer = new();

        public HudWindow(UIElement content, int visibleMs, bool shadow = true)
        {
            _visibleMs = visibleMs;
            SizeToContent = SizeToContent.WidthAndHeight;
            var host = new Border { Child = content, Margin = new Thickness(shadow ? 10 : 0) };
            if (shadow)
            {
                host.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.3, Direction = 270 };
            }

            Content = host;
            Opacity = 0;
            ClickThrough = true;
            Focusable = false;
            ShowActivated = false;
        }

        public void Present()
        {
            SourceInitialized += (_, _) => N.ExcludeFromCapture(Handle);
            ShowNoActivate();
            UpdateLayout();
            Position();
            BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
            _timer.Interval = TimeSpan.FromMilliseconds(_visibleMs);
            _timer.Tick += (_, _) =>
            {
                _timer.Stop();
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
                fade.Completed += (_, _) => Close();
                BeginAnimation(OpacityProperty, fade);
            };
            _timer.Start();
        }

        private void Position()
        {
            var mon = ScreenGrab.MonitorUnderCursor();
            var dpi = VisualTreeHelper.GetDpi(this);
            var w = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
            var h = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
            var x = mon.Work.X + (mon.Work.Width - w) / 2;
            var y = mon.Work.Y + (int)(24 * mon.Scale);
            N.SetWindowPos(Handle, -1, x, y, w, h, 0x0010);
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer.Stop();
            base.OnClosed(e);
        }
    }
}
