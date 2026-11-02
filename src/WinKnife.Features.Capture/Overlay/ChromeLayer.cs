// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using WinKnife.Core.Ui;
using WinKnife.Core;

namespace WinKnife.Features.Capture;

/// <summary>The clickable chrome of the selection overlay: guide bar / mode bar, key chips, "Full screen" pill, audio pill.</summary>
internal sealed class ChromeLayer : Grid
{
    private static readonly Brush Glass = new SolidColorBrush(Color.FromArgb(0xE6, 0x1F, 0x20, 0x24));
    private static readonly Brush Stroke = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255));
    private static readonly Brush Fg = new SolidColorBrush(Color.FromArgb(0xEB, 255, 255, 255));
    private static readonly Brush Sub = new SolidColorBrush(Color.FromArgb(0xA6, 255, 255, 255));
    private static readonly Brush ChipFill = new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255));
    private readonly CaptureSession _s;
    private readonly MonitorInfo _m;
    private readonly Grid _scaled = new();
    private readonly Border _pill = new();
    private readonly Border _bottom = new();
    private readonly StackPanel _bottomHost = new() { HorizontalAlignment = HorizontalAlignment.Center };

    public ChromeLayer(CaptureSession session, MonitorInfo monitor)
    {
        _s = session;
        _m = monitor;
        IsHitTestVisible = true;
        Width = monitor.Bounds.Width;
        Height = monitor.Bounds.Height;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        _scaled.LayoutTransform = new ScaleTransform(monitor.Scale, monitor.Scale);
        _scaled.HorizontalAlignment = HorizontalAlignment.Stretch;
        _scaled.VerticalAlignment = VerticalAlignment.Stretch;
        // Hit testing: only the visible bars react; empty areas let the mouse reach the selection view below.
        _scaled.Background = null;
        _bottom.Child = _bottomHost;
        _bottom.HorizontalAlignment = HorizontalAlignment.Center;
        _bottom.VerticalAlignment = VerticalAlignment.Bottom;
        _bottom.Margin = new Thickness(0, 0, 0, 32);
        _pill.HorizontalAlignment = HorizontalAlignment.Center;
        _pill.VerticalAlignment = VerticalAlignment.Top;
        _pill.Margin = new Thickness(0, 12, 0, 0);
        _scaled.Children.Add(_pill);
        _scaled.Children.Add(_bottom);
        Children.Add(_scaled);
        Cursor = Cursors.Arrow;
    }

    public void Refresh()
    {
        var p = _s.Pointer;
        var onThis = p.X >= _m.Bounds.X && p.Y >= _m.Bounds.Y && p.X < _m.Bounds.X + _m.Bounds.Width && p.Y < _m.Bounds.Y + _m.Bounds.Height;
        var show = onThis && !_s.Dragging && !_s.Pending;
        _scaled.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show)
        {
            return;
        }

        BuildPill();
        BuildBottom();
    }

    // ---- full screen pill -----------------------------------------------------------------------------------

    private void BuildPill()
    {
        var visible = _s.Tool == CaptureTool.Screenshot && !_s.Scrolling && !_s.ScrollingOnly;
        _pill.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible)
        {
            return;
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Icon("", 13, Fg));
        row.Children.Add(Label(Loc.T("Full screen"), 13, FontWeights.SemiBold, Fg, new Thickness(7, 0, 0, 0)));
        _pill.Child = row;
        _pill.Height = 32;
        _pill.MinHeight = 32;
        _pill.CornerRadius = new CornerRadius(16);
        _pill.Background = Glass;
        _pill.BorderBrush = Stroke;
        _pill.BorderThickness = new Thickness(1);
        _pill.Cursor = Cursors.Arrow;
        _pill.ToolTip = Strings.Screenshot.CaptureWholeScreen;
        _pill.Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 5, Direction = 270, Opacity = 0.2 };
        _pill.MouseLeftButtonDown -= PillClick;
        _pill.MouseLeftButtonDown += PillClick;
        _pill.MouseEnter -= PillEnter;
        _pill.MouseEnter += PillEnter;
        _pill.MouseLeave -= PillLeave;
        _pill.MouseLeave += PillLeave;
        _pill.RenderTransformOrigin = new Point(0.5, 0.5);
    }

    private void PillEnter(object sender, MouseEventArgs e)
    {
        _pill.RenderTransform = new ScaleTransform(1.02, 1.02);
        _pill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x57, 255, 255, 255));
    }

    private void PillLeave(object sender, MouseEventArgs e)
    {
        _pill.RenderTransform = null;
        _pill.BorderBrush = Stroke;
    }

    private void PillClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _s.CaptureMonitor(_m);
    }

    // ---- bottom bar -------------------------------------------------------------------------------------------

    private void BuildBottom()
    {
        _bottomHost.Children.Clear();
        var tools = _s.ToolsAvailable().ToList();
        var menu = _s.ShowMenu && tools.Count > 0;
        _bottomHost.Children.Add(menu ? BuildModeBar(tools) : BuildGuide());
        if (_s.Tool == CaptureTool.Recording)
        {
            var audio = BuildAudioPill();
            audio.Margin = new Thickness(0, 8, 0, 0);
            _bottomHost.Children.Add(audio);
        }
    }

    private string Subtitle()
    {
        if (_s.ScrollingOnly || _s.Scrolling)
        {
            return Strings.Screenshot.ScrollingCaption;
        }

        return _s.Tool switch
        {
            CaptureTool.Recording => $"{Strings.Recorder.SelectionPurpose}  ·  {Strings.Screenshot.HintDrag}  ·  {Strings.Screenshot.HintClick}",
            CaptureTool.Text => Strings.Ocr.Caption,
            CaptureTool.Color => Strings.Color.Caption,
            _ => $"{Strings.Screenshot.HintDrag}  ·  {Strings.Screenshot.HintClick}  ·  " + (_s.Scrolling ? Strings.Screenshot.ScrollingOn : Strings.Screenshot.ScrollingHint),
        };
    }

    private List<UIElement> Chips()
    {
        var chips = new List<UIElement>();
        var tool = _s.Tool;
        if (tool != CaptureTool.Color)
        {
            if (!_s.Scrolling && !_s.ScrollingOnly)
            {
                chips.Add(Chip("", "↩"));
            }

            if (tool == CaptureTool.Screenshot && !_s.ScrollingOnly)
            {
                chips.Add(Chip(G.Stack, _s.Scrolling ? Loc.T("S on") : "S"));
            }
        }

        chips.Add(Chip(G.Zoom, _s.LoupeOn ? Loc.T("Z on") : "Z"));
        if (_s.LoupeOn)
        {
            chips.Add(Chip(G.Copy, "C"));
        }

        if (tool != CaptureTool.Color && _s.RepeatAvailable(_m.Index))
        {
            chips.Add(Chip("", "R"));
        }

        chips.Add(Chip(G.Close, "esc"));
        return chips;
    }

    private FrameworkElement BuildGuide()
    {
        var width = Math.Min(680, Math.Max(280, _m.Bounds.Width / _m.Scale - 32));
        var grid = new Grid { Width = width, Height = 72 };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        var icon = new Border { Width = 28, Height = 28, Child = Icon(G.Camera, 17, Sub) };
        row.Children.Add(icon);
        var title = _s.ScrollingOnly ? Strings.Screenshot.ScrollingTitle : _s.Tool == CaptureTool.Recording ? Strings.Recorder.SelectionPurpose : Strings.Screenshot.HintDrag;
        var texts = new StackPanel { Margin = new Thickness(8, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Label(title, 13, FontWeights.SemiBold, Fg, default));
        var sub = _s.Tool == CaptureTool.Recording ? $"{Strings.Screenshot.HintDrag}  ·  {Strings.Screenshot.HintClick}" : _s.ScrollingOnly ? Strings.Screenshot.ScrollingSelectionHint : Subtitle().Replace(Strings.Screenshot.HintDrag + "  ·  ", string.Empty);
        texts.Children.Add(Label(sub, 11, FontWeights.Normal, Sub, default));
        row.Children.Add(texts);
        var chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var c in Chips())
        {
            ((FrameworkElement)c).Margin = new Thickness(4, 0, 0, 0);
            chips.Children.Add(c);
        }

        var dock = new DockPanel();
        DockPanel.SetDock(chips, Dock.Right);
        dock.Children.Add(chips);
        dock.Children.Add(row);
        dock.Margin = new Thickness(0, 0, 14, 0);
        grid.Children.Add(dock);
        return Card(grid, 16);
    }

    private FrameworkElement BuildModeBar(List<CaptureTool> tools)
    {
        var outer = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
        foreach (var tool in tools)
        {
            var selected = tool == _s.Tool;
            var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var top = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var digit = new Border
            {
                Width = 19, Height = 19, CornerRadius = new CornerRadius(5),
                Background = selected ? UiKit.BrushOf(ThemeKeys.Accent) : ChipFill,
                Child = Label(((int)tool).ToString(), 10.5, FontWeights.Bold, Brushes.White, default, center: true),
            };
            top.Children.Add(digit);
            var icon = Icon(CaptureTools.Glyph(tool), 13, Fg);
            icon.Margin = new Thickness(7, 0, 0, 0);
            top.Children.Add(icon);
            content.Children.Add(top);
            var name = Label(CaptureTools.Title(tool), 10, FontWeights.SemiBold, Fg, new Thickness(0, 3, 0, 0), center: true);
            content.Children.Add(name);
            var button = new Border
            {
                Width = 116, Height = 48, CornerRadius = new CornerRadius(10), Child = content, Cursor = Cursors.Arrow,
                Background = selected ? new SolidColorBrush(Color.FromArgb(0x29, 10, 132, 255)) : Brushes.Transparent,
                BorderBrush = selected ? new SolidColorBrush(Color.FromArgb(0x7A, 10, 132, 255)) : Brushes.Transparent,
                BorderThickness = new Thickness(1),
                ToolTip = $"{CaptureTools.Title(tool)} ({(int)tool})",
                Margin = new Thickness(1),
            };
            var captured = tool;
            button.MouseEnter += (_, _) =>
            {
                if (captured != _s.Tool)
                {
                    button.Background = new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255));
                }
            };
            button.MouseLeave += (_, _) =>
            {
                if (captured != _s.Tool)
                {
                    button.Background = Brushes.Transparent;
                }
            };
            button.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                _s.SetTool(captured);
            };
            bar.Children.Add(button);
        }

        var barCard = new Border { CornerRadius = new CornerRadius(14), Background = Glass, BorderBrush = Stroke, BorderThickness = new Thickness(1), Child = bar, HorizontalAlignment = HorizontalAlignment.Center };
        barCard.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 7, Direction = 270, Opacity = 0.22 };
        outer.Children.Add(barCard);

        var hint = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        hint.Children.Add(Label(Subtitle(), 10.5, FontWeights.Medium, Sub, default));
        hint.Children.Add(Chip("", "1–4", margin: new Thickness(10, 0, 0, 0)));
        foreach (var c in Chips())
        {
            ((FrameworkElement)c).Margin = new Thickness(4, 0, 0, 0);
            hint.Children.Add(c);
        }

        var hintCard = new Border { Height = 38, CornerRadius = new CornerRadius(19), Background = Glass, BorderBrush = Stroke, BorderThickness = new Thickness(1), Child = hint, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        outer.Children.Add(hintCard);
        return outer;
    }

    private FrameworkElement BuildAudioPill()
    {
        var app = _s.Services.App;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
        row.Children.Add(AudioToggle(G.Speaker, Loc.T("PC sound"), K.RecSystemAudio, app));
        row.Children.Add(AudioToggle(G.Mic, Loc.T("Microphone"), K.RecMicrophone, app));
        var pill = new Border { CornerRadius = new CornerRadius(18), Background = Glass, BorderBrush = Stroke, BorderThickness = new Thickness(1), Child = row, HorizontalAlignment = HorizontalAlignment.Center };
        pill.Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 4, Direction = 270, Opacity = 0.16 };
        return pill;
    }

    private UIElement AudioToggle(string glyph, string title, string key, WinKnife.Core.IAppSettings app)
    {
        var on = app.Get<bool>(key);
        var content = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(Icon(glyph, 12, on ? Brushes.White : Sub));
        content.Children.Add(Label(title, 11.5, FontWeights.SemiBold, on ? Brushes.White : Sub, new Thickness(6, 0, 0, 0)));
        var b = new Border
        {
            Height = 28, CornerRadius = new CornerRadius(14), Child = content, Margin = new Thickness(2), Cursor = Cursors.Arrow,
            Background = on ? UiKit.BrushOf(ThemeKeys.Accent) : ChipFill,
        };
        b.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            app.Set(key, !app.Get<bool>(key));
            _s.Invalidate();
        };
        return b;
    }

    // ---- helpers ----------------------------------------------------------------------------------------------

    private static Border Card(UIElement child, double radius) => new()
    {
        CornerRadius = new CornerRadius(radius), Background = Glass, BorderBrush = Stroke, BorderThickness = new Thickness(1), Child = child,
        Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 7, Direction = 270, Opacity = 0.22 },
    };

    private static Border Chip(string glyph, string label, Thickness? margin = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Icon(glyph, 10, Sub));
        row.Children.Add(Label(label, 10, FontWeights.SemiBold, Sub, new Thickness(5, 0, 0, 0)));
        return new Border { Height = 26, CornerRadius = new CornerRadius(7), Background = ChipFill, Child = row, Margin = margin ?? default, VerticalAlignment = VerticalAlignment.Center };
    }

    private static TextBlock Icon(string glyph, double size, Brush brush) => new()
    {
        Text = glyph, FontFamily = UiKit.IconFont, FontSize = size, Foreground = brush, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
    };

    private static TextBlock Label(string text, double size, FontWeight weight, Brush brush, Thickness margin, bool center = false) => new()
    {
        Text = text, FontSize = size, FontWeight = weight, Foreground = brush, Margin = margin, VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = center ? HorizontalAlignment.Center : HorizontalAlignment.Left, FontFamily = UiKit.TextFont, TextTrimming = TextTrimming.CharacterEllipsis,
    };
}
