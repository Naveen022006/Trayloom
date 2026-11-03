// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinKnife.Features.Island;

/// <summary>
/// Choreography of the island's layers around the silhouette spring (spec 5 content transitions). Everything here is stepped from
/// the same frame loop as the silhouette (one clock, elapsed time, never per-tick increments): the open page fades in once the
/// shape has grown about 60 %, scaling from the top centre; on close it leaves fast and the closed strip returns at the end; a
/// page switch cross-fades the old and new page while the height springs; a new closed strip fades in over the old width.
/// </summary>
internal sealed partial class IslandWindow
{
    private enum Choreo
    {
        None,
        Opening,
        Closing,
    }

    private const double RevealDelay = 0.15;
    private const double RevealLength = 0.30;
    private const double RevealTotal = 0.46;
    private const double ScaleFrom = 0.86;
    private const double CloseFade = 0.12;
    private const double CloseTotal = 0.30;
    private const double SwapOut = 0.12;
    private const double SwapIn = 0.16;
    private const double SwapTotal = 0.2;
    private const double StripFade = 0.2;

    private Choreo _choreo;
    private double _choreoT;
    private double _closeFromOpen = 1;
    private FrameworkElement? _swapOld;
    private FrameworkElement? _swapNew;
    private double _swapT;
    private FrameworkElement? _fadeIn;
    private double _fadeT;

    private static double Clamp01(double x) => x < 0 ? 0 : (x > 1 ? 1 : x);

    private static double OutCubic(double x) => 1 - Math.Pow(1 - x, 3);

    private static double OutQuint(double x) => 1 - Math.Pow(1 - x, 5);

    private static double InCubic(double x) => x * x * x;

    /// <summary>Empties the open layer before a fresh open so no page of the previous session shows.</summary>
    private void ResetOpenLayer()
    {
        _swapOld = _swapNew = null;
        _openHost.Children.Clear();
        _quick.Children.Clear();
        _quick.Opacity = 1;
        _openHost.Opacity = 0;
    }

    /// <summary>Puts the new page root in the open layer: instantly, or cross-faded over the page it replaces.</summary>
    private void SwapOpenRoot(FrameworkElement root, bool crossFade)
    {
        EndSwap();
        if (crossFade && _openHost.Children.Count > 0 && !D.ReduceMotion)
        {
            _swapOld = (FrameworkElement)_openHost.Children[^1];
            _swapOld.IsHitTestVisible = false;
            // both pages are rasterised once for the cross-fade: the fade then only blends two textures
            _swapOld.CacheMode = new BitmapCache(_scale);
            root.CacheMode = new BitmapCache(_scale);
            root.Opacity = 0;
            _openHost.Children.Add(root);
            _swapNew = root;
            _swapT = 0;
            StartRendering();
            return;
        }

        _openHost.Children.Clear();
        _openHost.Children.Add(root);
    }

    private void EndSwap()
    {
        if (_swapOld is not null)
        {
            _swapOld.CacheMode = null;
            _openHost.Children.Remove(_swapOld);
        }

        if (_swapNew is not null)
        {
            _swapNew.Opacity = 1;
            _swapNew.CacheMode = null;
        }

        _swapOld = _swapNew = null;
    }

    private void BeginOpening()
    {
        _openHost.IsHitTestVisible = true;
        _closedHost.IsHitTestVisible = false;
        _quick.Opacity = 1;
        if (D.ReduceMotion)
        {
            _choreo = Choreo.None;
            _openHost.Opacity = 1;
            _openHost.RenderTransform = Transform.Identity;
            _closedHost.Opacity = 0;
            return;
        }

        _choreo = Choreo.Opening;
        _choreoT = 0;
        // the page is rasterised once; the fade and the 0.86 to 1 scale then move one texture instead of re-rendering the tree
        _openHost.CacheMode = new BitmapCache(_scale);
        _openScale.ScaleX = _openScale.ScaleY = ScaleFrom;
        _openHost.RenderTransform = _openScale;
        _openHost.Opacity = 0;
        _closedHost.Opacity = 1;
        StartRendering();
    }

    private void BeginClosing(bool immediate)
    {
        _openHost.IsHitTestVisible = false;
        _closedHost.IsHitTestVisible = true;
        if (immediate || D.ReduceMotion)
        {
            _choreo = Choreo.None;
            EndSwap();
            _openHost.CacheMode = null;
            _openHost.Opacity = 0;
            _openHost.RenderTransform = Transform.Identity;
            _openHost.Children.Clear();
            _quick.Children.Clear();
            _quick.Opacity = 1;
            _closedHost.Opacity = _surface.Alpha;
            return;
        }

        _choreo = Choreo.Closing;
        _choreoT = 0;
        _openHost.CacheMode = null;
        _closeFromOpen = Clamp01(_openHost.Opacity);
        _openHost.RenderTransform = Transform.Identity;
        _closedHost.Opacity = 0;
        StartRendering();
    }

    /// <summary>Advances the layer transitions by one frame; true when none is left running.</summary>
    private bool StepChoreography(double dt)
    {
        switch (_choreo)
        {
            case Choreo.Opening:
                _choreoT += dt;
                _openHost.Opacity = OutCubic(Clamp01((_choreoT - RevealDelay) / RevealLength));
                var s = ScaleFrom + ((1 - ScaleFrom) * OutQuint(Clamp01(_choreoT / 0.45)));
                _openScale.ScaleX = _openScale.ScaleY = s;
                _closedHost.Opacity = 1 - InCubic(Clamp01(_choreoT / CloseFade));
                if (_choreoT >= RevealTotal)
                {
                    _choreo = Choreo.None;
                    _openHost.Opacity = 1;
                    _openHost.CacheMode = null;
                    _openHost.RenderTransform = Transform.Identity;
                    _closedHost.Opacity = 0;
                }

                break;
            case Choreo.Closing:
                _choreoT += dt;
                _openHost.Opacity = _closeFromOpen * (1 - InCubic(Clamp01(_choreoT / CloseFade)));
                _quick.Opacity = 1 - Clamp01(_choreoT / CloseFade);
                _closedHost.Opacity = OutCubic(Clamp01((_choreoT - 0.10) / 0.18)) * _surface.Alpha;
                if (_choreoT >= CloseTotal)
                {
                    _choreo = Choreo.None;
                    EndClosing();
                }

                break;
        }

        if (_swapNew is not null)
        {
            _swapT += dt;
            if (_swapOld is not null)
            {
                _swapOld.Opacity = 1 - Clamp01(_swapT / SwapOut);
            }

            _swapNew.Opacity = OutCubic(Clamp01((_swapT - 0.04) / SwapIn));
            if (_swapT >= SwapTotal)
            {
                EndSwap();
            }
        }

        if (_fadeIn is not null)
        {
            _fadeT += dt;
            _fadeIn.Opacity = OutCubic(Clamp01((_fadeT - 0.03) / 0.17));
            if (_fadeT >= StripFade)
            {
                _fadeIn.Opacity = 1;
                _fadeIn = null;
            }
        }

        return _choreo == Choreo.None && _swapNew is null && _fadeIn is null;
    }

    private void EndClosing()
    {
        _openHost.Opacity = 0;
        _closedHost.Opacity = _surface.Alpha;
        if (_mode != IslandMode.Open)
        {
            // the page is gone: release its visuals
            EndSwap();
            _openHost.Children.Clear();
            _quick.Children.Clear();
            _quick.Opacity = 1;
        }
    }

    /// <summary>The silhouette settled: make sure no layer is left half way (also covers a snap).</summary>
    private void FinishChoreography()
    {
        if (_choreo == Choreo.Closing)
        {
            _choreo = Choreo.None;
            EndClosing();
        }
        else if (_choreo == Choreo.Opening)
        {
            _choreo = Choreo.None;
            _openHost.CacheMode = null;
            _openHost.Opacity = 1;
            _openHost.RenderTransform = Transform.Identity;
            _closedHost.Opacity = 0;
        }

        EndSwap();
        _openHost.CacheMode = null;
        if (_fadeIn is not null)
        {
            _fadeIn.Opacity = 1;
            _fadeIn = null;
        }
    }
}
