// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;

namespace WinKnife.Features.SystemTools;

/// <summary>
/// Borderless flyout anchored to the bottom-right of the monitor under the cursor (next to the tray).
/// Hides when it loses focus or on Escape. Content goes into <see cref="Body"/>.
/// </summary>
internal class FlyoutWindow : Window
{
    private readonly Border _frame;
    private DateTime _shownAt;
    private bool _placing;

    public FlyoutWindow(string title, string glyph, double width = 380)
    {
        SetResourceReference(StyleProperty, ThemeKeys.WindowStyle);
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Width = width;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        UseLayoutRounding = true;
        Title = title;

        var header = new DockPanel { Margin = new Thickness(16, 14, 16, 8), LastChildFill = true };
        var icon = new TextBlock { Text = glyph, FontSize = 16, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.IconTextStyle);
        DockPanel.SetDock(icon, Dock.Left);
        header.Children.Add(icon);
        HeaderExtra = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(HeaderExtra, Dock.Right);
        header.Children.Add(HeaderExtra);
        var heading = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center };
        heading.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.SubtitleTextStyle);
        header.Children.Add(heading);

        Body = new StackPanel { Margin = new Thickness(12, 0, 12, 14) };
        var scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = Body };

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(scroller);

        _frame = new Border { BorderThickness = new Thickness(1), Child = root };
        _frame.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Border);
        Content = _frame;

        Deactivated += OnDeactivated;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Hide();
                e.Handled = true;
            }
        };
        SizeChanged += (_, _) => Place();
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue)
            {
                _shownAt = DateTime.UtcNow;
                OnShown();
            }
            else
            {
                OnHidden();
            }
        };
    }

    /// <summary>Extra header content docked to the right (e.g. a refresh button).</summary>
    protected StackPanel HeaderExtra { get; }

    /// <summary>Vertical stack the content is added to.</summary>
    protected StackPanel Body { get; }

    /// <summary>Toggles visibility (shows on the monitor under the cursor).</summary>
    public void Toggle()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        Present();
    }

    public void Present()
    {
        var area = Screens.UnderCursor();
        MaxHeight = Math.Max(240, (area.WorkArea.Height / VisualTreeHelper.GetDpi(this).DpiScaleY) - 24);
        Show();
        Place();
        Activate();
        Focus();
    }

    protected virtual void OnShown()
    {
    }

    protected virtual void OnHidden()
    {
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var corner = NativeConstants.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(handle, NativeConstants.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // Ignore the transient deactivation that happens while the window is still being activated.
        if ((DateTime.UtcNow - _shownAt).TotalMilliseconds < 400)
        {
            return;
        }

        if (IsVisible && !IsActive)
        {
            Hide();
        }
    }

    private void Place()
    {
        if (_placing || !IsVisible)
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0)
        {
            return;
        }

        _placing = true;
        try
        {
            var area = Screens.UnderCursor();
            var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            var w = ActualWidth * dpi;
            var h = ActualHeight * dpi;
            var margin = 12 * dpi;
            var x = area.WorkArea.Right - w - margin;
            var y = area.WorkArea.Bottom - h - margin;
            NativeMethods.SetWindowPos(handle, NativeConstants.HWND_TOPMOST, (int)x, (int)y, 0, 0, NativeConstants.SWP_NOSIZE | NativeConstants.SWP_NOACTIVATE);
        }
        finally
        {
            _placing = false;
        }
    }
}
