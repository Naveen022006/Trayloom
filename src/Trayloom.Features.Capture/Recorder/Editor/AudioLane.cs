// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Trayloom.Features.Capture;

/// <summary>An audio track row: waveform, volume slider, percent and keep / remove toggle.</summary>
internal sealed class AudioLaneControl : Border
{
    public AudioLaneControl(Color tint, float[] peaks, Func<bool> keeps, Func<double> gain, Action<bool> setKeep, Action<double> setGain)
    {
        CornerRadius = new CornerRadius(7);
        BorderThickness = new Thickness(1);
        var grid = new Grid { Margin = new Thickness(8, 0, 8, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        var wave = new WaveformControl(peaks, tint);
        grid.Children.Add(wave);
        var percent = Ed.Label($"{Math.Round(gain() * 100)}%", 10, FontWeights.Medium, Ed.FgDim);
        percent.HorizontalAlignment = HorizontalAlignment.Right;
        var slider = Ed.NewSlider(0, 1, gain(), 92, v =>
        {
            percent.Text = $"{Math.Round(v * 100)}%";
            setGain(v);
        });
        slider.ToolTip = RS.Volume;
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(percent, 2);
        grid.Children.Add(slider);
        grid.Children.Add(percent);
        var toggle = new EdButton(G.Close, null, Strings.Remove, null, iconSize: 10, width: 18, height: 18);
        void Paint()
        {
            var kept = keeps();
            Background = new SolidColorBrush(Color.FromArgb(kept ? (byte)28 : (byte)10, tint.R, tint.G, tint.B));
            BorderBrush = new SolidColorBrush(Color.FromArgb(kept ? (byte)71 : (byte)26, tint.R, tint.G, tint.B));
            wave.Opacity = kept ? 1 : 0.3;
            slider.IsEnabled = kept;
            if (toggle.Glyph is { } g)
            {
                g.Text = kept ? G.Close : G.Undo;
                g.Foreground = kept ? Ed.Fg : new SolidColorBrush(tint);
            }

            toggle.ToolTip = kept ? Strings.Remove : RS.Restore;
        }

        toggle.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            setKeep(!keeps());
            Paint();
        };
        Grid.SetColumn(toggle, 3);
        grid.Children.Add(toggle);
        Child = grid;
        Paint();
    }
}

internal sealed class WaveformControl : FrameworkElement
{
    private readonly float[] _peaks;
    private readonly Color _tint;

    public WaveformControl(float[] peaks, Color tint)
    {
        _peaks = peaks;
        _tint = tint;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        var brush = new SolidColorBrush(Color.FromArgb(204, _tint.R, _tint.G, _tint.B));
        if (_peaks.Length == 0)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(89, _tint.R, _tint.G, _tint.B)), null, new Rect(0, h / 2, w, 1));
            return;
        }

        var slot = w / _peaks.Length;
        for (var i = 0; i < _peaks.Length; i++)
        {
            var bar = Math.Max(1, _peaks[i] * (h - 8));
            dc.DrawRoundedRectangle(brush, null, new Rect(i * slot + slot * 0.16, (h - bar) / 2, slot * 0.68, bar), 0.7, 0.7);
        }
    }
}
