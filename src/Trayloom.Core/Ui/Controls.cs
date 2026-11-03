// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Trayloom.Core.Ui;

/// <summary>Size of a <see cref="MacSwitch"/> (spec: regular 40x22, small 34x19, mini 28x16).</summary>
public enum SwitchSize
{
    Regular,
    Small,
    Mini,
}

/// <summary>Mac-style on/off switch: accent track when on, white thumb. Bind <see cref="IsOn"/> / <see cref="Toggled"/>.</summary>
public sealed class MacSwitch : CheckBox
{
    /// <summary>Creates a switch.</summary>
    public MacSwitch(bool isOn = false, SwitchSize size = SwitchSize.Regular, Action<bool>? onToggled = null)
    {
        SetResourceReference(StyleProperty, size switch
        {
            SwitchSize.Small => ThemeKeys.SwitchSmallStyle,
            SwitchSize.Mini => ThemeKeys.SwitchMiniStyle,
            _ => ThemeKeys.SwitchStyle,
        });
        VerticalAlignment = VerticalAlignment.Center;
        IsChecked = isOn;
        if (onToggled is not null)
        {
            Toggled += onToggled;
        }

        Checked += (_, _) => Toggled?.Invoke(true);
        Unchecked += (_, _) => Toggled?.Invoke(false);
    }

    /// <summary>Raised after the user (or code) changed the state.</summary>
    public event Action<bool>? Toggled;

    /// <summary>Current state (setting it raises <see cref="Toggled"/>).</summary>
    public bool IsOn
    {
        get => IsChecked == true;
        set => IsChecked = value;
    }
}

/// <summary>
/// Segmented control (SwiftUI <c>.segmented</c> picker): a rounded track with the selected item as an accent pill.
/// Items are text labels.
/// </summary>
public sealed class SegmentedControl : Border
{
    private readonly List<string> _items;
    private readonly List<Border> _cells = [];
    private readonly bool _stretch;
    private int _selected = -1;

    /// <summary>Creates the control.</summary>
    /// <param name="items">Labels.</param>
    /// <param name="selectedIndex">Initially selected index.</param>
    /// <param name="stretch">Fill the available width with equal cells (otherwise size to content).</param>
    /// <param name="fontSize">Label size.</param>
    public SegmentedControl(IEnumerable<string> items, int selectedIndex = 0, bool stretch = false, double fontSize = 13)
    {
        _items = [.. items];
        _stretch = stretch;
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(2);
        this.Themed(BackgroundProperty, ThemeKeys.SettingsControl);
        HorizontalAlignment = stretch ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;

        var grid = new Grid();
        for (var i = 0; i < _items.Count; i++)
        {
            var index = i;
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = stretch ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            var cell = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(stretch ? 12 : 18, 4, stretch ? 12 : 18, 4),
                Cursor = Cursors.Hand,
                Child = UiKit.Text(_items[i], fontSize, FontWeights.Normal, ThemeKeys.TextPrimary, wrap: stretch),
            };
            ((TextBlock)cell.Child).HorizontalAlignment = HorizontalAlignment.Center;
            ((TextBlock)cell.Child).TextAlignment = TextAlignment.Center;
            ((TextBlock)cell.Child).VerticalAlignment = VerticalAlignment.Center;
            cell.MouseLeftButtonUp += (_, _) => SelectedIndex = index;
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
            _cells.Add(cell);
        }

        Child = grid;
        SelectedIndex = selectedIndex;
    }

    /// <summary>Raised when the selection changed.</summary>
    public event Action<int>? SelectionChanged;

    /// <summary>The selected index.</summary>
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (value == _selected || value < 0 || value >= _items.Count)
            {
                return;
            }

            _selected = value;
            for (var i = 0; i < _cells.Count; i++)
            {
                var on = i == value;
                if (on)
                {
                    _cells[i].SetResourceReference(BackgroundProperty, ThemeKeys.Accent);
                }
                else
                {
                    _cells[i].Background = Brushes.Transparent;
                }

                ((TextBlock)_cells[i].Child).SetResourceReference(TextBlock.ForegroundProperty, on ? ThemeKeys.OnAccent : ThemeKeys.TextPrimary);
            }

            SelectionChanged?.Invoke(value);
        }
    }

    /// <summary>True when the control fills the width.</summary>
    public bool IsStretched => _stretch;
}

/// <summary>Look of a <see cref="BadgePill"/>.</summary>
public enum BadgeStyle
{
    /// <summary>White bold 8 pt on accent ("BETA").</summary>
    Accent,

    /// <summary>Orange bold 9 pt on orange 14 % ("EXPERIMENTAL").</summary>
    Orange,

    /// <summary>Secondary bold 9.5 pt on primary 8 % ("Hidden").</summary>
    Neutral,

    /// <summary>Green text on green 13 % (status pills).</summary>
    Green,
}

/// <summary>Capsule badge: BETA / EXPERIMENTAL / Hidden / pressure pills.</summary>
public sealed class BadgePill : Border
{
    /// <summary>Creates the badge (text is shown uppercase for Accent and Orange).</summary>
    public BadgePill(string text, BadgeStyle style = BadgeStyle.Accent)
    {
        CornerRadius = new CornerRadius(20);
        VerticalAlignment = VerticalAlignment.Center;
        var (size, fill, fore, padH, padV) = style switch
        {
            BadgeStyle.Orange => (9.0, ThemeKeys.OrangeFill, ThemeKeys.Orange, 7.0, 3.0),
            BadgeStyle.Neutral => (9.5, ThemeKeys.ControlFill, ThemeKeys.TextSecondary, 7.0, 3.0),
            BadgeStyle.Green => (9.5, ThemeKeys.TileFill, ThemeKeys.Green, 7.0, 3.0),
            _ => (8.0, ThemeKeys.Accent, ThemeKeys.OnAccent, 5.0, 1.5),
        };
        Padding = new Thickness(padH, padV, padH, padV);
        this.Themed(BackgroundProperty, fill);
        var label = style is BadgeStyle.Accent or BadgeStyle.Orange ? text.ToUpperInvariant() : text;
        Child = UiKit.Text(label, size, FontWeights.Bold, fore);
    }
}

/// <summary>Rounded keycap chip showing a shortcut (9 pt semibold, tertiary on primary 6 %).</summary>
public sealed class ShortcutChip : Border
{
    /// <summary>Creates the chip.</summary>
    public ShortcutChip(string text)
    {
        CornerRadius = new CornerRadius(5);
        Padding = new Thickness(6, 2, 6, 2);
        VerticalAlignment = VerticalAlignment.Center;
        this.Themed(BackgroundProperty, ThemeKeys.ControlFill);
        Child = UiKit.Text(text, 10, FontWeights.SemiBold, ThemeKeys.TextTertiary);
    }
}

/// <summary>Option chip (keep-awake durations, monitor intervals): selected = accent fill with white text.</summary>
public sealed class PanelChip : Border
{
    private readonly TextBlock _label;
    private bool _selected;

    /// <summary>Creates the chip.</summary>
    public PanelChip(string text, Action? onClick = null, bool selected = false)
    {
        CornerRadius = new CornerRadius(11);
        MinHeight = 22;
        Padding = new Thickness(8, 0, 8, 0);
        Cursor = Cursors.Hand;
        _label = UiKit.Text(text, 11, FontWeights.Medium, ThemeKeys.TextPrimary);
        _label.VerticalAlignment = VerticalAlignment.Center;
        Child = _label;
        MouseLeftButtonUp += (_, _) => onClick?.Invoke();
        IsSelected = selected;
    }

    /// <summary>Selected chips fill with the accent colour.</summary>
    public bool IsSelected
    {
        get => _selected;
        set
        {
            _selected = value;
            this.Themed(BackgroundProperty, value ? ThemeKeys.Accent : ThemeKeys.ControlFill);
            _label.SetResourceReference(TextBlock.ForegroundProperty, value ? ThemeKeys.OnAccent : ThemeKeys.TextPrimary);
        }
    }
}

/// <summary>A small (i) button that opens a popover with arbitrary content (energy / permission metadata in the hub).</summary>
public sealed class InfoPopoverButton : Button
{
    private readonly Popup _popup;

    /// <summary>Creates the button; <paramref name="content"/> is created lazily on each open.</summary>
    public InfoPopoverButton(Func<UIElement> content, string? tooltip = null, string glyph = Glyphs.Info)
    {
        this.Themed(StyleProperty, ThemeKeys.ButtonPlain);
        Padding = new Thickness(3);
        Content = UiKit.Icon(glyph, 11, ThemeKeys.TextTertiary);
        VerticalAlignment = VerticalAlignment.Center;
        ToolTip = tooltip;
        _popup = new Popup
        {
            PlacementTarget = this,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
        };
        Click += (_, _) =>
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14),
                Margin = new Thickness(8),
                BorderThickness = new Thickness(0.7),
                Child = content(),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.35 },
            };
            card.SetResourceReference(Border.BackgroundProperty, ThemeKeys.PanelBase);
            card.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.CardBorder);
            _popup.Child = card;
            _popup.IsOpen = !_popup.IsOpen;
        };
    }
}

/// <summary>Uppercase 10 pt semibold section title ("KEEP AWAKE") with optional trailing element (edit button).</summary>
public sealed class SectionTitleRow : Grid
{
    /// <summary>Creates the title row.</summary>
    public SectionTitleRow(string title, UIElement? trailing = null)
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = UiKit.Text(title.ToUpperInvariant(), 10.5, FontWeights.SemiBold, ThemeKeys.TextSecondary);
        text.VerticalAlignment = VerticalAlignment.Center;
        Children.Add(text);
        if (trailing is not null)
        {
            SetColumn(trailing, 1);
            Children.Add(trailing);
        }
    }
}

/// <summary>Horizontal bar used for usage percentages (height 5, capsule). <see cref="Value"/> is 0..1.</summary>
public sealed class UsageBar : FrameworkElement
{
    private double _value;
    private string _fillKey = ThemeKeys.Accent;

    /// <summary>Creates a bar.</summary>
    public UsageBar(double value = 0, string fillKey = ThemeKeys.Accent, double height = 5)
    {
        Height = height;
        _value = value;
        _fillKey = fillKey;
        MinWidth = 20;
    }

    /// <summary>Fill fraction 0..1.</summary>
    public double Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0, 1);
            InvalidateVisual();
        }
    }

    /// <summary>Theme brush key of the fill.</summary>
    public string FillKey
    {
        get => _fillKey;
        set
        {
            _fillKey = value;
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        var r = ActualHeight / 2;
        dc.DrawRoundedRectangle(UiKit.BrushOf(ThemeKeys.Track), null, new Rect(0, 0, ActualWidth, ActualHeight), r, r);
        var w = Math.Max(Math.Min(3, ActualWidth), ActualWidth * _value);
        dc.DrawRoundedRectangle(UiKit.BrushOf(_fillKey), null, new Rect(0, 0, w, ActualHeight), r, r);
    }
}

/// <summary>History line graph (height 30 in cards, 38 in detail views): stroke 1.5, area gradient, optional ceiling label.</summary>
public sealed class Sparkline : FrameworkElement
{
    private IReadOnlyList<double> _values = [];
    private double _ceiling = 100;
    private string _lineKey = ThemeKeys.Accent;

    /// <summary>Creates a sparkline.</summary>
    public Sparkline(string lineKey = ThemeKeys.Accent, double height = 30, double ceiling = 100)
    {
        Height = height;
        _lineKey = lineKey;
        _ceiling = ceiling;
        ThemeManager.ThemeChanged += _ => Dispatcher.BeginInvoke(InvalidateVisual);
    }

    /// <summary>Sets the samples (oldest first) and the scale ceiling.</summary>
    public void SetValues(IReadOnlyList<double> values, double? ceiling = null)
    {
        _values = values;
        if (ceiling is not null)
        {
            _ceiling = Math.Max(ceiling.Value, 1e-9);
        }

        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        var baseline = new Pen(UiKit.BrushOf(ThemeKeys.TextTertiary), 1) { DashStyle = DashStyles.Solid };
        dc.DrawLine(baseline, new Point(0, h - 0.5), new Point(w, h - 0.5));
        if (_values.Count < 2)
        {
            return;
        }

        var line = UiKit.BrushOf(_lineKey);
        var color = (line as SolidColorBrush)?.Color ?? Colors.Gray;
        var step = w / (_values.Count - 1);
        var points = _values.Select((v, i) => new Point(i * step, h - Math.Clamp(v / _ceiling, 0, 1) * (h - 2) - 1)).ToList();
        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(0, h), true, true);
            foreach (var p in points)
            {
                ctx.LineTo(p, true, false);
            }

            ctx.LineTo(new Point(w, h), true, false);
        }

        area.Freeze();
        var fill = new LinearGradientBrush(Color.FromArgb(0x29, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B), 90);
        dc.DrawGeometry(fill, null, area);
        var path = new StreamGeometry();
        using (var ctx = path.Open())
        {
            ctx.BeginFigure(points[0], false, false);
            ctx.PolyLineTo(points.Skip(1).ToList(), true, true);
        }

        path.Freeze();
        dc.DrawGeometry(null, new Pen(line, 1.5) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, path);
    }
}
