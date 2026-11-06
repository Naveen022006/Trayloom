// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Trayloom.Core.Native;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Island;

/// <summary>
/// The preview that follows the pointer while the closed island is dragged: a small click-through window showing where the island would land
/// (the snapped candidate, on whichever display it falls on). It copies the closed island with a visual brush, like the mirrors do, and is excluded
/// from screen captures exactly when the island is. The island window itself stays where it is, dimmed, until the drop.
/// </summary>
internal sealed class IslandGhostWindow : OverlayWindow
{
    private readonly Border _plate = new() { Background = Brushes.Black, BorderBrush = D.White(0.30), BorderThickness = new Thickness(0.75), IsHitTestVisible = false };
    private readonly System.Windows.Shapes.Rectangle _view = new() { IsHitTestVisible = false };
    private readonly ScaleTransform _zoom = new(1, 1);
    private readonly bool _excludeFromCapture;
    private CornerRadius _corners = new(-1);
    private double _radius = -1;
    private readonly double _w;
    private readonly double _h;
    private (bool Capsule, bool Bottom)? _shape;

    /// <param name="source">The closed layer of the island (copied live: its content keeps updating while it is dragged).</param>
    /// <param name="width">Width of that layer in island units (DIP before the island zoom).</param>
    /// <param name="height">Height of that layer in island units.</param>
    /// <param name="excludeFromCapture">Hide the preview from screenshots and recordings (the island's own "show in captures" setting, inverted).</param>
    public IslandGhostWindow(FrameworkElement source, double width, double height, bool excludeFromCapture)
    {
        _excludeFromCapture = excludeFromCapture;
        _w = width;
        _h = height;
        ClickThrough = true;
        UseLayoutRounding = false;
        SnapsToDevicePixels = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        Top = -32000;
        Width = 8;
        Height = 8;
        _view.Fill = new VisualBrush(source) { Stretch = Stretch.None };
        var body = new Grid { Width = width, Height = height, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, LayoutTransform = _zoom };
        body.Children.Add(_plate);
        body.Children.Add(_view);
        Content = body;
    }

    /// <inheritdoc />
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (_excludeFromCapture)
        {
            try
            {
                IslandNative.SetWindowDisplayAffinity(Handle, 0x11u);
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// Puts the preview on the candidate's body rectangle (physical pixels). <paramref name="scale"/> is physical pixels per island unit on the
    /// display it lands on: the content is scaled by it over the window's own DPI, so a window that Windows has not yet re-scaled after crossing
    /// to a display of another DPI still draws at the right size (the next frame sets the bounds again and settles the rest).
    /// </summary>
    public void Place(RECT rect, double scale, bool capsule, bool bottom)
    {
        var w = Math.Max(1, rect.Right - rect.Left);
        var h = Math.Max(1, rect.Bottom - rect.Top);
        SetPhysicalBounds(rect.Left, rect.Top, w, h);
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var k = dpi > 0.01 ? scale / dpi : 1;
        if (Math.Abs(_zoom.ScaleX - k) > 1e-6)
        {
            _zoom.ScaleX = k;
            _zoom.ScaleY = k;
        }

        // the notch is flat against its edge and round on the far side; everything else is a capsule
        var unitsH = h / Math.Max(scale, 0.01);
        var unitsW = w / Math.Max(scale, 0.01);
        var radius = Math.Min(Math.Min(unitsH / 2, 28), unitsW / 2);
        var corners = capsule ? new CornerRadius(radius) : (bottom ? new CornerRadius(radius, radius, 0, 0) : new CornerRadius(0, 0, radius, radius));
        if (corners != _corners)
        {
            _corners = corners;
            _plate.CornerRadius = corners;
        }

        // the copy of the island is cut to the same silhouette as the plate, because a rounded rectangle would round the notch's flat edge
        if ((capsule, bottom) != _shape)
        {
            _shape = (capsule, bottom);
            _view.Clip = IslandSurface.Build(0, 0, _w, _h, capsule, false, bottom);
            _radius = -1;
        }

        var viewRadius = capsule ? radius : 0;
        if (Math.Abs(viewRadius - _radius) > 0.01)
        {
            _radius = viewRadius;
            _view.RadiusX = viewRadius;
            _view.RadiusY = viewRadius;
        }
    }
}
