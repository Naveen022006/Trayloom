// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;

namespace WinKnife.App;

/// <summary>Small flyout near the tray listing the actions of all running features as tiles.</summary>
public partial class QuickPanelWindow : Window
{
    private readonly IActionCatalog _catalog;
    private readonly Action _openFeatures;
    private readonly Point _anchor;
    private bool _closing;

    internal QuickPanelWindow(IActionCatalog catalog, Action openFeatures, Point anchorPhysical)
    {
        InitializeComponent();
        _catalog = catalog;
        _openFeatures = openFeatures;
        _anchor = anchorPhysical;

        // Stay invisible until positioned so the window never flashes in the wrong place.
        Opacity = 0;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -10000;
        Top = -10000;

        BuildTiles();
        Loaded += (_, _) => PositionAndReveal();
        Deactivated += (_, _) => CloseOnce();
        KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                CloseOnce();
            }
        };
    }

    /// <summary>Tick count when the panel last closed; lets the tray toggle it instead of immediately reopening.</summary>
    public static long LastClosedTick { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var round = NativeConstants.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(handle, NativeConstants.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        var dark = ThemeManager.IsLight ? 0 : 1;
        NativeMethods.DwmSetWindowAttribute(handle, NativeConstants.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Closing deactivates the window; make sure that does not try to close it a second time.
        _closing = true;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        LastClosedTick = Environment.TickCount64;
        base.OnClosed(e);
    }

    private void BuildTiles()
    {
        var actions = _catalog.All;
        EmptyText.Visibility = actions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (feature, action) in actions)
        {
            var icon = new TextBlock { Text = action.Glyph ?? "", FontSize = 22, Margin = new Thickness(0, 0, 0, 6) };
            icon.SetResourceReference(StyleProperty, ThemeKeys.IconTextStyle);
            var title = new TextBlock { Text = action.Title, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 34 };
            var source = new TextBlock { Text = feature.Name, FontSize = 10, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            source.SetResourceReference(StyleProperty, ThemeKeys.MutedTextStyle);
            source.TextWrapping = TextWrapping.NoWrap;

            var content = new StackPanel { Width = 88 };
            content.Children.Add(icon);
            content.Children.Add(title);
            content.Children.Add(source);

            var tile = new Button { Content = content, ToolTip = $"{feature.Name}: {action.Title}" };
            tile.SetResourceReference(StyleProperty, ThemeKeys.TileStyle);
            var captured = action;
            tile.Click += (_, _) =>
            {
                CloseOnce();
                // Many actions act on the foreground window or the screen: wait until the panel is gone and focus has
                // returned to the previous window before running them.
                InvokeAfterClose(captured.Invoke);
            };
            Tiles.Children.Add(tile);
        }
    }

    private void PositionAndReveal()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
        var area = Screens.FromPoint(_anchor).WorkArea;
        const int margin = 12;

        var x = Math.Clamp((int)_anchor.X - width / 2, (int)area.Left + margin, Math.Max((int)area.Left + margin, (int)area.Right - width - margin));
        // The taskbar is normally at the bottom: open upwards when the anchor is in the lower half of the screen.
        var y = _anchor.Y > area.Top + area.Height / 2
            ? (int)area.Bottom - height - margin
            : (int)area.Top + margin;

        NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle, NativeConstants.HWND_TOPMOST, x, y, width, height, NativeConstants.SWP_NOACTIVATE);
        Opacity = 1;
        Activate();
    }

    private void OnFeaturesClick(object sender, RoutedEventArgs e)
    {
        CloseOnce();
        Application.Current.Dispatcher.BeginInvoke(_openFeatures);
    }

    /// <summary>Runs <paramref name="action"/> on the UI thread about 150 ms from now (after the panel has closed).</summary>
    private static void InvokeAfterClose(Action action)
    {
        var dispatcher = Application.Current.Dispatcher;
        var timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                action();
            }
            catch (Exception ex)
            {
                // Feature actions are guarded by the registry; this is the last line of defence for the UI thread.
                System.Diagnostics.Trace.WriteLine($"Quick panel action failed: {ex}");
            }
        };
        timer.Start();
    }

    private void CloseOnce()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        Close();
    }
}
