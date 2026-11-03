// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.App;

/// <summary>
/// A small "Starting Trayloom…" toast shown when start-up takes longer than two seconds (the first start after copying new files can
/// take ~10 s while Windows scans them). It runs on its own UI thread so it stays alive while the main thread is busy loading
/// features, and disappears when start-up is done. It uses fixed dark colours: nothing of the main thread's resources is touched.
/// </summary>
internal static class StartupSplash
{
    private static readonly ManualResetEventSlim Finished = new(false);
    private static Dispatcher? s_dispatcher;
    private static readonly object Gate = new();

    /// <summary>Starts the watchdog; the toast appears only if <see cref="Disarm"/> is not called within <paramref name="delay"/>.</summary>
    public static void Arm(TimeSpan delay)
    {
        var thread = new Thread(() =>
        {
            try
            {
                if (Finished.Wait(delay))
                {
                    return;
                }

                Run();
            }
            catch (Exception)
            {
                // a cosmetic toast must never take the app down
            }
        })
        {
            IsBackground = true,
            Name = "Trayloom start-up toast",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    /// <summary>Start-up finished: close the toast (or cancel it when it never appeared).</summary>
    public static void Disarm()
    {
        Finished.Set();
        lock (Gate)
        {
            s_dispatcher?.BeginInvokeShutdown(DispatcherPriority.Normal);
        }
    }

    private static void Run()
    {
        var window = Build();
        lock (Gate)
        {
            s_dispatcher = Dispatcher.CurrentDispatcher;
        }

        if (Finished.IsSet)
        {
            return;
        }

        window.Show();
        Dispatcher.Run();
        window.Close();
    }

    private static Window Build()
    {
        const double Width = 340;
        var work = SystemParameters.WorkArea;
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            ShowActivated = false,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.Height,
            Width = Width + 32,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = work.Right - Width - 32 - 8,
            Top = work.Bottom - 120 - 8,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
        };

        var mark = new System.Windows.Shapes.Path
        {
            Data = BrandMark.Geometry,
            Stretch = Stretch.Uniform,
            Width = 40,
            Height = 40 * BrandMark.Height / BrandMark.Width,
            Fill = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var title = new TextBlock { Text = Loc.T("Starting Trayloom…"), FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
        var sub = new TextBlock
        {
            Text = Loc.T("The first start can take a few seconds while Windows checks the files."),
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromArgb(0xB3, 255, 255, 255)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0),
        };
        var track = new Border { Height = 3, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)), ClipToBounds = true, Margin = new Thickness(0, 10, 0, 0) };
        var runner = new Border { Width = 90, Height = 3, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromRgb(0x4D, 0xA3, 0xFF)), HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = new TranslateTransform(-90, 0) };
        track.Child = runner;
        runner.RenderTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-90, Width - 28 - 40 - 14, TimeSpan.FromMilliseconds(1100)) { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(title);
        text.Children.Add(sub);
        text.Children.Add(track);
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(mark);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        var card = new Border
        {
            Margin = new Thickness(16),
            Padding = new Thickness(16, 14, 16, 14),
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x22)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = 0.45 },
            Child = row,
        };
        window.Content = card;
        return window;
    }
}
