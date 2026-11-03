// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Maintenance;

/// <summary>14 px rounded check box (checked / unchecked / mixed) in the accent colour.</summary>
internal sealed class CheckMark : FrameworkElement
{
    private bool? _state;

    public CheckMark(bool? isChecked = false, Action<bool>? onToggled = null, double size = 14)
    {
        Width = size;
        Height = size;
        _state = isChecked;
        Cursor = Cursors.Hand;
        VerticalAlignment = VerticalAlignment.Center;
        Focusable = false;
        if (onToggled is not null)
        {
            Toggled += onToggled;
        }

        MouseLeftButtonUp += (_, e) =>
        {
            if (!IsEnabled)
            {
                return;
            }

            _state = _state != true;
            InvalidateVisual();
            Toggled?.Invoke(_state == true);
            e.Handled = true;
        };
        ThemeManager.ThemeChanged += OnTheme;
        Unloaded += (_, _) => ThemeManager.ThemeChanged -= OnTheme;
        IsEnabledChanged += (_, _) => Opacity = IsEnabled ? 1 : 0.45;
    }

    public event Action<bool>? Toggled;

    public bool? IsChecked
    {
        get => _state;
        set
        {
            if (_state != value)
            {
                _state = value;
                InvalidateVisual();
            }
        }
    }

    private void OnTheme(bool _) => Dispatcher.BeginInvoke(InvalidateVisual);

    protected override void OnRender(DrawingContext dc)
    {
        var r = new Rect(0.5, 0.5, ActualWidth - 1, ActualHeight - 1);
        var radius = Math.Max(3, ActualWidth * 0.26);
        if (_state is null or true)
        {
            dc.DrawRoundedRectangle(UiKit.BrushOf(ThemeKeys.Accent), null, r, radius, radius);
            var white = new Pen(UiKit.BrushOf(ThemeKeys.OnAccent), Math.Max(1.4, ActualWidth * 0.12)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            if (_state == true)
            {
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(new Point(ActualWidth * 0.26, ActualHeight * 0.52), false, false);
                    ctx.LineTo(new Point(ActualWidth * 0.43, ActualHeight * 0.70), true, true);
                    ctx.LineTo(new Point(ActualWidth * 0.76, ActualHeight * 0.30), true, true);
                }

                dc.DrawGeometry(null, white, g);
            }
            else
            {
                dc.DrawLine(white, new Point(ActualWidth * 0.28, ActualHeight * 0.5), new Point(ActualWidth * 0.72, ActualHeight * 0.5));
            }
        }
        else
        {
            dc.DrawRoundedRectangle(UiKit.BrushOf(ThemeKeys.ControlFill), new Pen(UiKit.BrushOf(ThemeKeys.TextTertiary), 1), r, radius, radius);
        }
    }
}

/// <summary>Small rotating arc used for "busy" states.</summary>
internal sealed class Spinner : FrameworkElement
{
    private readonly RotateTransform _rotate = new();
    private readonly string _brushKey;

    public Spinner(double size = 14, string brushKey = ThemeKeys.TextSecondary)
    {
        Width = size;
        Height = size;
        _brushKey = brushKey;
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = _rotate;
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalAlignment = HorizontalAlignment.Center;
        Loaded += (_, _) => _rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
        Unloaded += (_, _) => _rotate.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = Math.Max(1, ActualWidth / 2 - ActualWidth * 0.09);
        var pen = new Pen(UiKit.BrushOf(_brushKey), Math.Max(1.4, ActualWidth * 0.12)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var track = new Pen(UiKit.BrushOf(ThemeKeys.Track), pen.Thickness);
        dc.DrawEllipse(null, track, c, radius, radius);
        var start = new Point(c.X, c.Y - radius);
        var end = new Point(c.X + radius, c.Y);
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(new Point(c.X - radius, c.Y), new Size(radius, radius), 0, true, SweepDirection.Clockwise, true, false);
            ctx.ArcTo(end, new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
        }

        dc.DrawGeometry(null, pen, geo);
    }
}

/// <summary>Progress bar: determinate fraction, or an indeterminate sliding capsule (accent @ 72 %).</summary>
internal sealed class ProgressStrip : FrameworkElement
{
    public static readonly DependencyProperty PhaseProperty =
        DependencyProperty.Register(nameof(Phase), typeof(double), typeof(ProgressStrip), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private double? _fraction;

    public ProgressStrip(double? fraction = null, double height = 5)
    {
        Height = height;
        _fraction = fraction;
        MinWidth = 20;
        Loaded += (_, _) => Restart();
        Unloaded += (_, _) => BeginAnimation(PhaseProperty, null);
        ThemeManager.ThemeChanged += OnTheme;
        Unloaded += (_, _) => ThemeManager.ThemeChanged -= OnTheme;
    }

    public double Phase
    {
        get => (double)GetValue(PhaseProperty);
        set => SetValue(PhaseProperty, value);
    }

    /// <summary>Null = indeterminate.</summary>
    public double? Fraction
    {
        get => _fraction;
        set
        {
            _fraction = value;
            Restart();
            InvalidateVisual();
        }
    }

    private void OnTheme(bool _) => Dispatcher.BeginInvoke(InvalidateVisual);

    private void Restart()
    {
        if (_fraction is null && IsLoaded)
        {
            BeginAnimation(PhaseProperty, new DoubleAnimation(-0.35, 1.05, TimeSpan.FromSeconds(1.15)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        else
        {
            BeginAnimation(PhaseProperty, null);
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var radius = ActualHeight / 2;
        dc.DrawRoundedRectangle(UiKit.BrushOf(ThemeKeys.Track), null, new Rect(0, 0, ActualWidth, ActualHeight), radius, radius);
        var accent = UiKit.BrushOf(ThemeKeys.Accent);
        if (_fraction is { } f)
        {
            var w = Math.Max(Math.Min(ActualHeight, ActualWidth), ActualWidth * Math.Clamp(f, 0, 1));
            dc.DrawRoundedRectangle(accent, null, new Rect(0, 0, w, ActualHeight), radius, radius);
            return;
        }

        var width = Math.Max(ActualWidth * 0.34, 36);
        var x = Phase * ActualWidth;
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), radius, radius));
        var brush = accent.Clone();
        brush.Opacity = 0.72;
        dc.DrawRoundedRectangle(brush, null, new Rect(x, 0, width, ActualHeight), radius, radius);
        dc.Pop();
    }
}

/// <summary>Static view helpers shared by every tool.</summary>
internal static class V
{
    public static TextBlock T(string text, double size = 12, FontWeight? weight = null, string brush = ThemeKeys.TextPrimary, bool wrap = false, TextTrimming trim = TextTrimming.None) =>
        UiKit.Text(text, size, weight, brush, wrap, trim);

    public static TextBlock Secondary(string text, double size = 11.5, bool wrap = true) => UiKit.Text(text, size, FontWeights.Normal, ThemeKeys.TextSecondary, wrap);

    public static TextBlock Tertiary(string text, double size = 10.5, bool wrap = true) => UiKit.Text(text, size, FontWeights.Normal, ThemeKeys.TextTertiary, wrap);

    public static TextBlock Orange(string text, double size = 11, bool wrap = true) => UiKit.Text(text, size, FontWeights.Normal, ThemeKeys.Orange, wrap);

    /// <summary>A glyph + text label row.</summary>
    public static StackPanel Label(string glyph, string text, double size = 12, FontWeight? weight = null, string glyphBrush = ThemeKeys.Accent, string textBrush = ThemeKeys.TextPrimary, double gap = 6)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = UiKit.Icon(glyph, size + 1, glyphBrush, weight ?? FontWeights.SemiBold);
        icon.Margin = new Thickness(0, 0, gap, 0);
        row.Children.Add(icon);
        var label = UiKit.Text(text, size, weight ?? FontWeights.Normal, textBrush);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);
        return row;
    }

    /// <summary>Rounded "soft card" (radius 9, padding 11, fill = foreground @ 5 %).</summary>
    public static Border Soft(UIElement? child = null, double padding = 11, double maxWidth = 0)
    {
        var card = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(padding), Child = child };
        card.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
        if (maxWidth > 0)
        {
            card.MaxWidth = maxWidth;
        }

        return card;
    }

    /// <summary>Capsule pill (source badge): accent text on accent 12 %.</summary>
    public static Border Pill(string text, double size = 8.5)
    {
        var pill = new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(5, 1, 5, 1), VerticalAlignment = VerticalAlignment.Center };
        pill.SetResourceReference(Border.BackgroundProperty, ThemeKeys.AccentFill12);
        pill.Child = UiKit.Text(text, size, FontWeights.SemiBold, ThemeKeys.Accent);
        return pill;
    }

    /// <summary>Capsule with a custom colour pair.</summary>
    public static Border Badge(string text, string fore, string fill, double size = 10, Thickness? padding = null)
    {
        var pill = new Border { CornerRadius = new CornerRadius(20), Padding = padding ?? new Thickness(7, 3, 7, 3), VerticalAlignment = VerticalAlignment.Center };
        pill.SetResourceReference(Border.BackgroundProperty, fill);
        pill.Child = UiKit.Text(text, size, FontWeights.SemiBold, fore);
        return pill;
    }

    /// <summary>Text-style accent link button.</summary>
    public static Button Link(string text, Action onClick, double size = 11, bool enabled = true)
    {
        var button = UiKit.Button(text, ButtonKind.Plain, size, null, onClick);
        button.Padding = new Thickness(0);
        button.Foreground = UiKit.BrushOf(ThemeKeys.Accent);
        button.Background = Brushes.Transparent;
        button.IsEnabled = enabled;
        return button;
    }

    /// <summary>Small icon-only plain button.</summary>
    public static Button IconButton(string glyph, string? tooltip, Action onClick, double size = 12, string brush = ThemeKeys.TextSecondary, double box = 22)
    {
        var button = new Button { Width = box, Height = box, Content = UiKit.Icon(glyph, size, brush, FontWeights.SemiBold), ToolTip = tooltip };
        button.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.ButtonPlain);
        button.Padding = new Thickness(0);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>A bordered mini button (10 pt semibold) for list rows.</summary>
    public static Button Mini(string text, Action onClick, string? glyph = null, double size = 10.5)
    {
        var button = UiKit.Button(text, ButtonKind.Bordered, size, glyph, onClick);
        button.Padding = new Thickness(8, 3, 8, 3);
        button.FontWeight = FontWeights.Medium;
        return button;
    }

    /// <summary>Drop-down of strings.</summary>
    public static ComboBox Drop(IReadOnlyList<string> items, int selected, Action<int> onChanged, double width = 0, double fontSize = 12)
    {
        var combo = new ComboBox { ItemsSource = items.ToList(), FontSize = fontSize, VerticalAlignment = VerticalAlignment.Center };
        if (width > 0)
        {
            combo.MinWidth = width;
        }

        combo.SelectedIndex = Math.Clamp(selected, 0, Math.Max(0, items.Count - 1));
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0)
            {
                onChanged(combo.SelectedIndex);
            }
        };
        return combo;
    }

    /// <summary>Rounded single-line text box.</summary>
    public static TextBox Field(string text = "", double width = 0, double fontSize = 12, string? tooltip = null)
    {
        var box = new TextBox { Text = text, FontSize = fontSize, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = tooltip };
        box.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.TextBoxStyle);
        if (width > 0)
        {
            box.Width = width;
        }

        return box;
    }

    /// <summary>Search field with a leading magnifier and a placeholder; returns the box through <paramref name="box"/>.</summary>
    public static Border SearchField(string placeholder, out TextBox box, double fontSize = 12, double radius = 9, double padding = 9)
    {
        var textBox = new TextBox
        {
            FontSize = fontSize,
            Background = Brushes.Transparent,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0, 1, 0, 1),
        };
        textBox.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.TextBoxStyle);
        textBox.Background = Brushes.Transparent;
        textBox.SetResourceReference(Control.ForegroundProperty, ThemeKeys.TextPrimary);
        textBox.SetResourceReference(TextBox.CaretBrushProperty, ThemeKeys.TextPrimary);
        var hint = UiKit.Text(placeholder, fontSize, FontWeights.Normal, ThemeKeys.TextTertiary);
        hint.IsHitTestVisible = false;
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.Margin = new Thickness(1, 0, 0, 0);
        textBox.TextChanged += (_, _) => hint.Visibility = textBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = UiKit.Icon(Glyphs.Search, fontSize, ThemeKeys.TextSecondary);
        icon.Margin = new Thickness(0, 0, 8, 0);
        grid.Children.Add(icon);
        var host = new Grid();
        host.Children.Add(hint);
        host.Children.Add(textBox);
        Grid.SetColumn(host, 1);
        grid.Children.Add(host);

        var border = new Border { CornerRadius = new CornerRadius(radius), Padding = new Thickness(padding), Child = grid };
        border.SetResourceReference(Border.BackgroundProperty, ThemeKeys.ControlFill);
        box = textBox;
        return border;
    }

    /// <summary>Expandable group: header (chevron + content) toggles the body. Collapsed by default.</summary>
    public static StackPanel Disclosure(FrameworkElement header, Func<FrameworkElement> body, bool expanded = false, Action<bool>? onChanged = null, double chevronTop = 4)
    {
        var root = new StackPanel();
        var chevron = UiKit.Icon(expanded ? Glyphs.ChevronDownSmall : Glyphs.ChevronRightSmall, 9, ThemeKeys.TextSecondary, FontWeights.SemiBold);
        chevron.Width = 12;
        chevron.VerticalAlignment = VerticalAlignment.Top;
        chevron.Margin = new Thickness(0, chevronTop, 4, 0);
        var head = new Grid { Cursor = Cursors.Hand, Background = Brushes.Transparent };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.Children.Add(chevron);
        Grid.SetColumn(header, 1);
        head.Children.Add(header);
        var content = new ContentControl { Visibility = Visibility.Collapsed };
        void Apply(bool open)
        {
            chevron.Text = open ? Glyphs.ChevronDownSmall : Glyphs.ChevronRightSmall;
            if (open && content.Content is null)
            {
                content.Content = body();
            }

            content.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        }

        var isOpen = expanded;
        Apply(isOpen);
        head.MouseLeftButtonUp += (_, e) =>
        {
            if (e.Handled)
            {
                return;
            }

            isOpen = !isOpen;
            Apply(isOpen);
            onChanged?.Invoke(isOpen);
        };
        root.Children.Add(head);
        root.Children.Add(content);
        return root;
    }

    /// <summary>Wraps a view so it keeps a minimum height equal to the viewport (vertical centring of empty states).</summary>
    public static StackPanel Centered(double spacing, params UIElement[] children)
    {
        var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var child in children)
        {
            if (child is FrameworkElement fe)
            {
                fe.HorizontalAlignment = HorizontalAlignment.Center;
            }

            column.AddSpaced(child, spacing);
        }

        return column;
    }

    /// <summary>Builds a context menu from (title, action, enabled) items; a null title adds a separator.</summary>
    public static ContextMenu Menu(params (string? Title, Action? Action, bool Enabled)[] items)
    {
        var menu = new ContextMenu();
        foreach (var (title, action, enabled) in items)
        {
            if (title is null)
            {
                menu.Items.Add(new Separator());
                continue;
            }

            var item = new MenuItem { Header = title, IsEnabled = enabled };
            item.Click += (_, _) => action?.Invoke();
            menu.Items.Add(item);
        }

        return menu;
    }

    /// <summary>A ScrollViewer without the horizontal bar.</summary>
    public static ScrollViewer Scroll(UIElement content, double maxHeight = 0)
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = content };
        if (maxHeight > 0)
        {
            scroll.MaxHeight = maxHeight;
        }

        return scroll;
    }

    /// <summary>A horizontal stack.</summary>
    public static StackPanel Row(double spacing, params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < children.Length; i++)
        {
            if (children[i] is FrameworkElement fe)
            {
                fe.Margin = new Thickness(i == 0 ? fe.Margin.Left : fe.Margin.Left + spacing, fe.Margin.Top, fe.Margin.Right, fe.Margin.Bottom);
                fe.VerticalAlignment = VerticalAlignment.Center;
            }

            row.Children.Add(children[i]);
        }

        return row;
    }

    /// <summary>Two-column grid: flexible left, auto right.</summary>
    public static Grid Split(UIElement left, UIElement right, double gap = 8)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(left);
        if (right is FrameworkElement fe)
        {
            fe.Margin = new Thickness(gap, fe.Margin.Top, fe.Margin.Right, fe.Margin.Bottom);
            fe.VerticalAlignment = VerticalAlignment.Center;
        }

        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    /// <summary>Round glyph badge (status icon in the operation card).</summary>
    public static Border Disc(string glyph, string brush, double size = 28, double glyphSize = 12)
    {
        var disc = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), VerticalAlignment = VerticalAlignment.Top };
        var color = (UiKit.BrushOf(brush) as SolidColorBrush)?.Color ?? Colors.Gray;
        disc.Background = new SolidColorBrush(Color.FromArgb(0x24, color.R, color.G, color.B));
        disc.Child = UiKit.Icon(glyph, glyphSize, brush, FontWeights.SemiBold);
        return disc;
    }

    /// <summary>Runs <paramref name="action"/> on the dispatcher of <paramref name="element"/> (or the app) without blocking.</summary>
    public static void Post(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }
}

/// <summary>Fixed-row-height virtualised list: only the rows in view exist as elements.</summary>
internal sealed class VirtualList : Grid
{
    private readonly ScrollViewer _scroll;
    private readonly Canvas _canvas = new();
    private readonly double _rowHeight;
    private Func<int, FrameworkElement> _factory = _ => new Border();
    private int _count;
    private int _first = -1;
    private int _last = -1;

    public VirtualList(double rowHeight)
    {
        _rowHeight = rowHeight;
        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _canvas };
        Children.Add(_scroll);
        _scroll.ScrollChanged += (_, _) => Render(false);
        SizeChanged += (_, _) => Render(true);
    }

    public int Count => _count;

    /// <summary>Resets the content: <paramref name="count"/> rows built by <paramref name="factory"/>.</summary>
    public void SetItems(int count, Func<int, FrameworkElement> factory, bool keepScroll = true)
    {
        var offset = _scroll.VerticalOffset;
        _count = count;
        _factory = factory;
        _canvas.Height = count * _rowHeight;
        if (!keepScroll)
        {
            _scroll.ScrollToVerticalOffset(0);
        }
        else
        {
            _scroll.ScrollToVerticalOffset(offset);
        }

        Render(true);
    }

    private void Render(bool force)
    {
        var viewport = _scroll.ViewportHeight > 0 ? _scroll.ViewportHeight : ActualHeight;
        if (viewport <= 0)
        {
            return;
        }

        var first = Math.Max(0, (int)Math.Floor(_scroll.VerticalOffset / _rowHeight) - 1);
        var last = Math.Min(_count - 1, (int)Math.Ceiling((_scroll.VerticalOffset + viewport) / _rowHeight) + 1);
        if (!force && first == _first && last == _last)
        {
            return;
        }

        _first = first;
        _last = last;
        _canvas.Children.Clear();
        var width = _scroll.ViewportWidth > 0 ? _scroll.ViewportWidth : ActualWidth;
        for (var i = first; i <= last; i++)
        {
            var row = _factory(i);
            row.Width = width;
            row.Height = _rowHeight;
            Canvas.SetTop(row, i * _rowHeight);
            _canvas.Children.Add(row);
        }
    }
}
