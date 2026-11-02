// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WinKnife.Features.Island;

/// <summary>Accent of an action tile (spec Part C 2.3).</summary>
internal enum TileAccent
{
    None,
    Selection,
    Alert,
    Awake,
    Timer,
}

internal static class PageUi
{
    private static ControlTemplate? _flat;

    /// <summary>A borderless transparent text box (the island draws its own field chrome).</summary>
    public static TextBox FlatTextBox(double size = 12)
    {
        _flat ??= (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
            "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TargetType=\"TextBox\"><ScrollViewer x:Name=\"PART_ContentHost\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Focusable=\"False\" Background=\"Transparent\"/></ControlTemplate>");
        return new TextBox
        {
            Template = _flat,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = D.Text,
            CaretBrush = Brushes.White,
            SelectionBrush = D.White(0.35),
            FontSize = size,
            FontFamily = D.Font,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0),
        };
    }

    public static Border Surface(double radius = 14, double alpha = 0.065) => new() { CornerRadius = new CornerRadius(radius), Background = D.White(alpha) };

    /// <summary>The 76x74 shortcut tile (<c>NotchActionTile</c>): 40 circle icon + label.</summary>
    public static HoverButton ActionTile(string glyph, string title, Action onClick, TileAccent accent = TileAccent.None, string? reading = null, string? compactTitle = null, double width = 76, double height = 74)
    {
        var tile = new HoverButton(onClick) { Width = width, Height = height, CornerRadius = new CornerRadius(14), Padding = new Thickness(4, 0, 4, 0) };
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Brush fill = D.White(0.075);
        Brush fg = D.White(0.85);
        switch (accent)
        {
            case TileAccent.Selection:
                fill = Brushes.White;
                fg = Brushes.Black;
                break;
            case TileAccent.Alert:
                fill = D.Red;
                fg = Brushes.White;
                break;
            case TileAccent.Awake:
                fill = D.Yellow;
                fg = Brushes.Black;
                break;
            case TileAccent.Timer:
                fill = D.Orange;
                fg = Brushes.Black;
                break;
        }

        var circle = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Background = fill, HorizontalAlignment = HorizontalAlignment.Center };
        var icon = D.I(glyph, 17, fg);
        icon.FontWeight = FontWeights.Medium;
        circle.Child = icon;
        stack.Children.Add(circle);
        var label = new StackPanel { Height = 28, Margin = new Thickness(0, 6, 0, 0) };
        if (reading is not null)
        {
            var r = D.Digits(reading, 11, FontWeights.SemiBold);
            r.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            r.LineHeight = 13;
            r.TextAlignment = TextAlignment.Center;
            r.TextTrimming = TextTrimming.CharacterEllipsis;
            label.Children.Add(r);
            var t = D.T(compactTitle ?? title, 11, FontWeights.Medium, D.White(0.7), false, TextTrimming.CharacterEllipsis);
            t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            t.LineHeight = 13;
            t.TextAlignment = TextAlignment.Center;
            label.Children.Add(t);
        }
        else
        {
            var t = D.T(title, 11, FontWeights.Medium, D.Text, true, TextTrimming.CharacterEllipsis);
            t.TextAlignment = TextAlignment.Center;
            t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            t.LineHeight = 13;
            t.MaxHeight = 28;
            label.Children.Add(t);
        }

        stack.Children.Add(label);
        tile.Child = stack;
        D.Tip(tile, title);
        return tile;
    }

    /// <summary>A horizontally scrolling rail (or a wrapped grid when everything fits) of equally sized items.</summary>
    public static FrameworkElement Rail(IReadOnlyList<FrameworkElement> items, double itemW, double itemH, double spacing, double width, double height, double rowSpacing = 8)
    {
        if (items.Count == 0)
        {
            return new Grid();
        }

        var cols = Math.Max(1, (int)Math.Floor((width + spacing) / (itemW + spacing)));
        var rows = Math.Max(1, (int)Math.Floor((height + rowSpacing) / (itemH + rowSpacing)));
        if (items.Count <= cols * rows)
        {
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var it in items)
            {
                it.Margin = new Thickness(spacing / 2, rowSpacing / 2, spacing / 2, rowSpacing / 2);
                wrap.Children.Add(it);
            }

            return wrap;
        }

        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var grid = new WrapPanel { Orientation = Orientation.Vertical, Height = (rows * (itemH + rowSpacing)) };
        foreach (var it in items)
        {
            it.Margin = new Thickness(0, 0, spacing, rowSpacing);
            grid.Children.Add(it);
        }

        scroll.Content = grid;
        scroll.PreviewMouseWheel += (s, e) =>
        {
            scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset - e.Delta);
            e.Handled = true;
        };
        return scroll;
    }

    /// <summary>A vertical scroll viewer with hidden bars and mouse wheel support.</summary>
    public static ScrollViewer VScroll(FrameworkElement content)
    {
        var s = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = content };
        s.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), ScrollBarStyle());
        return s;
    }

    private static Style ScrollBarStyle()
    {
        var xaml = "<Style xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"ScrollBar\">"
            + "<Setter Property=\"Width\" Value=\"4\"/><Setter Property=\"Opacity\" Value=\"0.5\"/><Setter Property=\"Background\" Value=\"Transparent\"/>"
            + "<Setter Property=\"Template\"><Setter.Value><ControlTemplate TargetType=\"ScrollBar\"><Grid Background=\"Transparent\"><Track x:Name=\"PART_Track\" IsDirectionReversed=\"True\"><Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType=\"Thumb\"><Border Background=\"#55FFFFFF\" CornerRadius=\"2\"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb></Track></Grid></ControlTemplate></Setter.Value></Setter>"
            + "</Style>";
        return (Style)System.Windows.Markup.XamlReader.Parse(xaml);
    }

    public static FrameworkElement Spinner(double size = 14)
    {
        var arc = new System.Windows.Shapes.Path
        {
            Stroke = D.White(0.7),
            StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Data = Geometry.Parse($"M {size / 2},1 A {(size / 2) - 1},{(size / 2) - 1} 0 1 1 {(size / 2) - 0.1},1"),
            Width = size,
            Height = size,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(),
        };
        var holder = new Grid { Width = size, Height = size };
        holder.Children.Add(arc);
        var anim = new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever };
        holder.Loaded += (_, _) => ((RotateTransform)arc.RenderTransform).BeginAnimation(RotateTransform.AngleProperty, anim);
        holder.Unloaded += (_, _) => ((RotateTransform)arc.RenderTransform).BeginAnimation(RotateTransform.AngleProperty, null);
        return holder;
    }

    /// <summary>Plays a short pulse used for "copied" check marks.</summary>
    public static void After(Dispatcher d, double seconds, Action action)
    {
        var t = new DispatcherTimer(DispatcherPriority.Normal, d) { Interval = TimeSpan.FromSeconds(seconds) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            action();
        };
        t.Start();
    }

    /// <summary>Icon + label menu button that pops a dark menu (<c>NotchBandMenuLabel</c>).</summary>
    public static HoverButton BandMenu(string label, string? glyph, Func<IEnumerable<IslandMenu.Item>> items, Func<IDisposable?> hold, string? tooltip = null, double maxWidth = 150)
    {
        HoverButton? b = null;
        b = new HoverButton(() => IslandMenu.Show(b!, items(), hold)) { Height = 28, CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 0, 7, 0), Lifts = false };
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (glyph is not null)
        {
            var g = D.I(glyph, 12, D.White(0.85));
            g.Margin = new Thickness(0, 0, 6, 0);
            row.Children.Add(g);
        }

        var t = D.T(label, 12, FontWeights.Medium, D.White(0.85), false, TextTrimming.CharacterEllipsis);
        t.MaxWidth = maxWidth;
        row.Children.Add(t);
        var chev = D.I(G.ChevronDown, 9, D.White(0.6));
        chev.Margin = new Thickness(5, 0, 0, 0);
        row.Children.Add(chev);
        b.Child = row;
        if (tooltip is not null)
        {
            D.Tip(b, tooltip);
        }

        return b;
    }
}
