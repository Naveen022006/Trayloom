// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Capture;

/// <summary>The "Recent captures" list: used inside the tray panel (hosted utility) and in the floating palette.</summary>
internal sealed class RecentCapturesView : StackPanel
{
    private readonly bool _palette;
    private readonly Action? _afterAction;
    private readonly Action _close;

    public RecentCapturesView(bool palette, Action? afterAction = null, Action? close = null)
    {
        _palette = palette;
        _afterAction = afterAction;
        _close = close ?? (() => { });
        Rebuild();
        Loaded += (_, _) => RecentCaptureService.Instance.Changed += OnChanged;
        Unloaded += (_, _) => RecentCaptureService.Instance.Changed -= OnChanged;
    }

    private void OnChanged() => Dispatcher.BeginInvoke(Rebuild);

    private static bool Available(string kind) => kind == "recording" ? CaptureHost.Installed("screen-recorder") : CaptureHost.Installed("screenshot");

    private void Rebuild()
    {
        Children.Clear();
        var entries = RecentCaptureService.Instance.Entries(Available);
        if (_palette)
        {
            var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(UiKit.Icon(G.History, 12, ThemeKeys.TextPrimary, FontWeights.SemiBold));
            var t = UiKit.Text(Strings.Screenshot.RecentCaptures, 12, FontWeights.SemiBold);
            t.Margin = new Thickness(7, 0, 0, 0);
            title.Children.Add(t);
            header.Children.Add(title);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(IconButton(G.Trash, Strings.Screenshot.ClearHistory, entries.Count > 0, ConfirmClear));
            buttons.Children.Add(IconButton(G.CloseCircle, Strings.Cancel, true, _close));
            Grid.SetColumn(buttons, 1);
            header.Children.Add(buttons);
            Children.Add(header);
        }
        else
        {
            var bar = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            bar.Children.Add(IconButton(G.Trash, Strings.Screenshot.ClearHistory, entries.Count > 0, ConfirmClear));
            ((FrameworkElement)bar.Children[0]).HorizontalAlignment = HorizontalAlignment.Right;
            Children.Add(bar);
        }

        if (entries.Count == 0)
        {
            var empty = new PanelCard(new TextBlock { Text = Strings.Screenshot.RecentEmpty, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center }.Themed(TextBlock.ForegroundProperty, ThemeKeys.TextTertiary)) { Height = 92 };
            Children.Add(empty);
            return;
        }

        var list = new StackPanel();
        foreach (var e in entries)
        {
            list.Children.Add(Row(e));
        }

        Children.Add(new ScrollViewer { Content = list, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
    }

    private static Button IconButton(string glyph, string tip, bool enabled, Action click)
    {
        var b = new Button { Content = UiKit.Icon(glyph, 12, ThemeKeys.TextSecondary), Width = 22, Height = 22, ToolTip = tip, Padding = new Thickness(0), IsEnabled = enabled };
        b.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.ButtonPlain);
        b.Click += (_, _) => click();
        return b;
    }

    private FrameworkElement Row(RecentEntry e)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var thumb = new Border { Width = 104, Height = 68, CornerRadius = new CornerRadius(7), ClipToBounds = true, BorderThickness = new Thickness(1) };
        thumb.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.CardBorder);
        thumb.Background = new SolidColorBrush(Color.FromArgb(33, 0, 0, 0));
        if (RecentCaptureService.Instance.Thumbnail(e) is { } bmp)
        {
            thumb.Child = new Image { Source = bmp, Stretch = Stretch.Uniform };
        }
        else
        {
            thumb.Child = UiKit.Icon(e.Kind == "recording" ? "" : G.Image, 20, ThemeKeys.TextSecondary);
        }

        grid.Children.Add(thumb);
        var right = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(UiKit.Text(e.Kind == "recording" ? Strings.Screenshot.Recording : Strings.Tools.Screenshot, 11.5, FontWeights.SemiBold));
        if (e.Kind == "recording" && e.RecordingPath is { } rp)
        {
            right.Children.Add(UiKit.Text(Path.GetFileName(rp), 9.5, FontWeights.Normal, ThemeKeys.TextSecondary, trimming: TextTrimming.CharacterEllipsis));
        }

        right.Children.Add(UiKit.Text(Relative(e.CreatedAt), 9.5, FontWeights.Normal, ThemeKeys.TextTertiary));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        var main = PanelButtons.Mini(e.Kind == "recording" ? Strings.Screenshot.Open : Strings.Screenshot.Restore, null, () => Activate(e));
        buttons.Children.Add(main);
        var trash = PanelButtons.Mini(string.Empty, G.Trash, () => RecentCaptureService.Instance.Remove(e.Id));
        trash.ToolTip = Strings.Screenshot.RemoveFromHistory;
        trash.Margin = new Thickness(6, 0, 0, 0);
        buttons.Children.Add(trash);
        right.Children.Add(buttons);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return new PanelCard(grid, 8) { Margin = new Thickness(0, 0, 0, 7) };
    }

    private void Activate(RecentEntry e)
    {
        if (e.Kind == "recording")
        {
            if (e.RecordingPath is { } path && File.Exists(path))
            {
                _close();
                _afterAction?.Invoke();
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                    }
                    catch
                    {
                        CaptureHost.Beep();
                    }
                });
            }
            else
            {
                RecentCaptureService.Instance.Remove(e.Id);
                CaptureHost.Beep();
            }

            return;
        }

        var cap = RecentCaptureService.Instance.LoadCapture(e);
        if (cap is null)
        {
            RecentCaptureService.Instance.Remove(e.Id);
            CaptureHost.Beep();
            return;
        }

        _close();
        _afterAction?.Invoke();
        Application.Current.Dispatcher.BeginInvoke(() => CaptureHost.Screenshot?.RestorePreview(cap));
    }

    private void ConfirmClear()
    {
        var result = MessageBox.Show(Strings.Screenshot.ClearHistory + "?", Strings.Screenshot.ClearHistory, MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (result == MessageBoxResult.OK)
        {
            RecentCaptureService.Instance.Clear();
        }
    }

    private static string Relative(DateTime t)
    {
        var span = DateTime.Now - t;
        if (span.TotalSeconds < 45)
        {
            return "now";
        }

        if (span.TotalMinutes < 60)
        {
            return $"{(int)Math.Max(1, span.TotalMinutes)} min ago";
        }

        if (span.TotalHours < 24)
        {
            return $"{(int)span.TotalHours} hr ago";
        }

        return $"{(int)span.TotalDays} d ago";
    }
}

/// <summary>The floating "Recent captures" palette (hotkey, editor clock button, Command Bar).</summary>
internal sealed class RecentCapturesWindow : Window
{
    private static RecentCapturesWindow? _current;

    private RecentCapturesWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        var card = new Border { Width = 440, Padding = new Thickness(14), CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), Margin = new Thickness(14) };
        card.SetResourceReference(Border.BackgroundProperty, ThemeKeys.PanelBase);
        card.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.CardBorder);
        card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 20, ShadowDepth = 6, Opacity = 0.35, Direction = 270 };
        card.Child = new RecentCapturesView(true, null, Close);
        Content = card;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
        Deactivated += (_, _) =>
        {
            if (!IsActive)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (!IsActive)
                    {
                        Close();
                    }
                });
            }
        };
        Closed += (_, _) => _current = null;
    }

    public static void ShowPalette()
    {
        if (_current is not null)
        {
            _current.Close();
            return;
        }

        var w = new RecentCapturesWindow();
        _current = w;
        var mon = ScreenGrab.MonitorUnderCursor();
        w.Opacity = 0;
        w.Show();
        w.UpdateLayout();
        var scale = mon.Scale;
        var pw = (int)(w.ActualWidth * scale);
        var ph = (int)(w.ActualHeight * scale);
        var x = mon.Work.X + (mon.Work.Width - pw) / 2;
        var y = mon.Work.Y + (mon.Work.Height - ph) / 2;
        N.SetWindowPos(new WindowInteropHelper(w).Handle, -1, x, y, pw, ph, 0x0040);
        w.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        w.Activate();
    }
}
