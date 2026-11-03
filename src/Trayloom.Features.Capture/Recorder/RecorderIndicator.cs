// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Capture;

/// <summary>The floating 150x32 pill (red dot, time, pause, stop). Never takes focus and is excluded from the video.</summary>
internal sealed class RecorderPill : OverlayWindow
{
    private readonly Ellipse _dot = new() { Width = 8, Height = 8, Fill = new SolidColorBrush(Color.FromRgb(255, 69, 58)) };
    private readonly TextBlock _time = new() { Text = "0:00", Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Cascadia Mono, Consolas"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _pauseGlyph = new() { Text = G.Pause, FontFamily = UiKit.IconFont, FontSize = 11, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _pauseBtn;
    private readonly Border _stopBtn;

    public RecorderPill(Action onPause, Action onStop)
    {
        Width = 150 + 20;
        Height = 32 + 20;
        var grid = new Grid { Width = 150, Height = 32 };
        _dot.HorizontalAlignment = HorizontalAlignment.Left;
        _dot.VerticalAlignment = VerticalAlignment.Center;
        _dot.Margin = new Thickness(12, 0, 0, 0);
        grid.Children.Add(_dot);
        _time.Width = 50;
        _time.HorizontalAlignment = HorizontalAlignment.Left;
        _time.Margin = new Thickness(25, 0, 0, 0);
        grid.Children.Add(_time);
        grid.Children.Add(new Border { Width = 1, Height = 16, Background = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(78, 0, 0, 0) });
        _pauseBtn = Button(_pauseGlyph, 82, Strings.Recorder.Pause, onPause);
        var stopGlyph = new TextBlock { Text = G.Stop, FontFamily = UiKit.IconFont, FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(255, 69, 58)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _stopBtn = Button(stopGlyph, 116, Strings.Recorder.Stop, onStop);
        grid.Children.Add(_pauseBtn);
        grid.Children.Add(_stopBtn);
        var pill = new Border { CornerRadius = new CornerRadius(16), Background = new SolidColorBrush(Color.FromArgb(240, 31, 31, 31)), BorderBrush = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)), BorderThickness = new Thickness(1), Child = grid, Margin = new Thickness(10), ToolTip = Strings.Recorder.IndicatorTooltip };
        pill.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 3, Opacity = 0.4, Direction = 270 };
        Content = pill;
        Opacity = 0;
        ClickThrough = false;
        StartPulse();
    }

    private static Border Button(UIElement glyph, double left, string tooltip, Action click)
    {
        var b = new Border { Width = 30, Height = 26, CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(left, 0, 0, 0), Child = glyph, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tooltip };
        b.MouseEnter += (_, _) => b.Background = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255));
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonUp += (_, _) => click();
        return b;
    }

    private void StartPulse()
    {
        var anim = new DoubleAnimation(1, 0.28, TimeSpan.FromSeconds(0.85)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
        _dot.BeginAnimation(OpacityProperty, anim);
    }

    public void SetState(int seconds, bool paused)
    {
        _time.Text = RecorderSupport.ElapsedLabel(seconds);
        _pauseGlyph.Text = paused ? G.Play : G.Pause;
        _pauseBtn.ToolTip = paused ? Strings.Recorder.Resume : Strings.Recorder.Pause;
        if (paused)
        {
            _dot.BeginAnimation(OpacityProperty, null);
            _dot.Opacity = 0.4;
        }
        else if (_dot.Opacity == 0.4)
        {
            StartPulse();
        }
    }

    public void Present(MonitorInfo monitor)
    {
        SourceInitialized += (_, _) => N.ExcludeFromCapture(Handle);
        ShowNoActivate();
        UpdateLayout();
        var scale = monitor.Scale;
        var w = (int)(170 * scale);
        var h = (int)(52 * scale);
        var x = monitor.Work.X + (monitor.Work.Width - w) / 2;
        var y = monitor.Work.Y + (int)(10 * scale) - (int)(10 * scale);
        N.SetWindowPos(Handle, -1, x, y, w, h, 0x0010);
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
    }
}

/// <summary>Click-through dim around the recorded area with a blue frame (area / display recordings only).</summary>
internal sealed class RegionGuideWindow : OverlayWindow
{
    public RegionGuideWindow(MonitorInfo monitor, Int32Rect pixelRect)
    {
        ClickThrough = true;
        var canvas = new GuideCanvas(monitor.Bounds.Width, monitor.Bounds.Height, pixelRect);
        Content = new Viewbox { Stretch = Stretch.Fill, Child = canvas };
        Width = 100;
        Height = 100;
        Monitor = monitor;
    }

    public MonitorInfo Monitor { get; }

    public void Present()
    {
        SourceInitialized += (_, _) => N.ExcludeFromCapture(Handle);
        ShowNoActivate();
        SetPhysicalBounds(Monitor.Bounds.X, Monitor.Bounds.Y, Monitor.Bounds.Width, Monitor.Bounds.Height);
    }

    private sealed class GuideCanvas : FrameworkElement
    {
        private readonly Int32Rect _sel;

        public GuideCanvas(double w, double h, Int32Rect sel)
        {
            Width = w;
            Height = h;
            _sel = sel;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var full = new Rect(0, 0, Width, Height);
            var hole = new Rect(_sel.X, _sel.Y, _sel.Width, _sel.Height);
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(77, 0, 0, 0)), null, new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(full), new RectangleGeometry(hole)));
            dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(242, 46, 140, 255)), 2), Rect.Inflate(hole, -1, -1));
        }
    }
}
