// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

/// <summary>
/// Vorssaint's <c>HUDBackdrop</c>: a borderless rounded window with a behind-window blur (DWM acrylic) and a legibility plate
/// (black 55 % in dark, white 50 % in light) plus a hairline border. Falls back to an opaque themed plate when transparency
/// effects are off or the OS cannot draw the backdrop. Put the content into <see cref="Body"/>.
/// </summary>
internal class GlassWindow : Window
{
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmsbtTransientWindow = 3;

    private readonly double _radius;
    private readonly bool _backdrop;
    private nint _region;

    /// <summary>Creates the window; <paramref name="radius"/> is the corner radius in DIPs.</summary>
    public GlassWindow(double radius, bool highContrast = true)
    {
        _radius = radius;
        _backdrop = SupportsBackdrop();
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        AllowsTransparency = !_backdrop;
        Background = Brushes.Transparent;
        Plate = new Border { CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(1) };
        Plate.SizeChanged += (_, e) =>
        {
            Plate.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), radius, radius);
            UpdateRegion();
        };
        base.Content = Plate;
        ApplyPlate();
        ThemeManager.ThemeChanged += OnTheme;
        Closed += (_, _) =>
        {
            ThemeManager.ThemeChanged -= OnTheme;
            if (_region != 0)
            {
                _region = 0;
            }
        };
        SourceInitialized += (_, _) => AttachBackdrop();
        DpiChanged += (_, _) => UpdateRegion();
    }

    /// <summary>The rounded plate that hosts the content.</summary>
    protected Border Plate { get; }

    /// <summary>The content of the glass.</summary>
    protected UIElement? Body
    {
        get => Plate.Child;
        set => Plate.Child = value;
    }

    /// <summary>The plate fill follows the theme (dark: black 55 %, light: white 50 %).</summary>
    private void ApplyPlate()
    {
        var light = ThemeManager.IsLight;
        byte alpha = _backdrop ? (byte)(light ? 128 : 140) : (byte)255;
        Plate.Background = _backdrop
            ? new SolidColorBrush(light ? Color.FromArgb(alpha, 255, 255, 255) : Color.FromArgb(alpha, 0, 0, 0))
            : (Brush)(Application.Current?.TryFindResource(ThemeKeys.PanelBase) ?? new SolidColorBrush(light ? Colors.White : Color.FromRgb(0x1C, 0x1C, 0x1E)));
        Plate.BorderBrush = new SolidColorBrush(light ? Color.FromArgb(0x14, 0, 0, 0) : Color.FromArgb(0x1F, 255, 255, 255));
    }

    private void OnTheme(bool light)
    {
        ApplyPlate();
        SetDark(!light);
    }

    private static bool SupportsBackdrop()
    {
        if (Environment.OSVersion.Version.Build < 22621)
        {
            return false;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("EnableTransparency") is not int v || v != 0;
        }
        catch
        {
            return true;
        }
    }

    private void AttachBackdrop()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0)
        {
            return;
        }

        SetDark(!ThemeManager.IsLight);
        if (!_backdrop)
        {
            return;
        }

        try
        {
            if (HwndSource.FromHwnd(handle)?.CompositionTarget is { } target)
            {
                target.BackgroundColor = Colors.Transparent;
            }

            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(handle, ref margins);
            var type = DwmsbtTransientWindow;
            DwmSetWindowAttribute(handle, DwmwaSystemBackdropType, ref type, sizeof(int));
            var corner = 1; // DWMWCP_DONOTROUND: the region rounds the window
            DwmSetWindowAttribute(handle, 33, ref corner, sizeof(int));
        }
        catch
        {
            // plain plate remains
        }

        UpdateRegion();
    }

    private void SetDark(bool dark)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0)
        {
            return;
        }

        var value = dark ? 1 : 0;
        DwmSetWindowAttribute(handle, 20, ref value, sizeof(int));
    }

    private void UpdateRegion()
    {
        if (!_backdrop)
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var w = (int)Math.Ceiling(ActualWidth * dpi);
        var h = (int)Math.Ceiling(ActualHeight * dpi);
        var d = (int)Math.Round(_radius * 2 * dpi);
        var region = CreateRoundRectRgn(0, 0, w + 1, h + 1, d, d);
        SetWindowRgn(handle, region, true);
        _region = region;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);

    [DllImport("gdi32.dll")]
    private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(nint hwnd, nint region, bool redraw);
}
