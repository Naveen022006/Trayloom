// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Trayloom.Features.Island;

/// <summary>The cover artwork element (<c>NotchArtwork</c>): rounded by 19 % of the side or a circle, hairline edge, fallbacks.</summary>
internal sealed class Cover : Border
{
    private readonly double _size;
    private readonly bool _round;

    public Cover(double size, bool round = false)
    {
        _size = size;
        _round = round;
        Width = Height = size;
        CornerRadius = new CornerRadius(round ? size / 2 : size * 0.19);
        Background = Brushes.Black;
        BorderBrush = D.White(round ? 0.14 : 0.12);
        BorderThickness = new Thickness(0.5);
        ClipToBounds = true;
        SnapsToDevicePixels = true;
    }

    public void Set(Playback p, ImageSource? appIcon = null)
    {
        if (p.Artwork is { } art)
        {
            Child = new Image { Source = art, Stretch = Stretch.UniformToFill, Width = _size, Height = _size };
        }
        else if (appIcon is not null)
        {
            Child = new Image { Source = appIcon, Stretch = Stretch.Uniform, Margin = new Thickness(_size * 0.14) };
        }
        else
        {
            Child = D.I(G.Music, _size * 0.3, D.White(0.45));
        }
    }

    public static Cover For(Playback p, double size, bool round = false, ImageSource? appIcon = null)
    {
        var c = new Cover(size, round);
        c.Set(p, appIcon);
        return c;
    }

    public static Brush TintOf(Playback p) => p.Tint is { } t ? new SolidColorBrush(t) : Brushes.White;
}
