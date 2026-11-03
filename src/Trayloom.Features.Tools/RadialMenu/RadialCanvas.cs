// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Trayloom.Core;
using Trayloom.Core.Ui;

namespace Trayloom.Features.Tools;

/// <summary>Edit operations on wheel items shared by the canvas and the list (they open the editor sheet).</summary>
internal static class RadialItemOps
{
    public static void Edit(FrameworkElement anchor, IContributionServices ctx, string itemId)
    {
        var profile = RadialState.Selected(ctx);
        var level = RadialState.Level(profile);
        var index = level.FindIndex(i => i.Id == itemId);
        if (index < 0)
        {
            return;
        }

        var working = level[index].Clone();
        var rootLevel = RadialState.Trail.Count == 0;
        var result = RadialEditor.Show(Window.GetWindow(anchor), ctx, working, isNew: false, rootLevel);
        switch (result)
        {
            case RadialEditorResult.Save or RadialEditorResult.DrillIn:
                RadialState.MutateLevel(ctx, list =>
                {
                    var i = list.FindIndex(x => x.Id == itemId);
                    if (i >= 0)
                    {
                        list[i] = working;
                    }
                });
                if (result == RadialEditorResult.DrillIn)
                {
                    DrillInto(ctx, itemId);
                }

                break;
            case RadialEditorResult.Remove:
                Remove(ctx, itemId);
                break;
        }
    }

    public static void Add(FrameworkElement anchor, IContributionServices ctx)
    {
        var profile = RadialState.Selected(ctx);
        if (RadialState.Level(profile).Count >= RadialModel.MaxItems)
        {
            return;
        }

        var working = new RadialItem { Kind = RadialKinds.App };
        var rootLevel = RadialState.Trail.Count == 0;
        var result = RadialEditor.Show(Window.GetWindow(anchor), ctx, working, isNew: true, rootLevel);
        if (result is RadialEditorResult.Save or RadialEditorResult.DrillIn)
        {
            RadialState.MutateLevel(ctx, list =>
            {
                if (list.Count < RadialModel.MaxItems)
                {
                    list.Add(working);
                }
            });
            if (working.Kind == RadialKinds.Submenu)
            {
                DrillInto(ctx, working.Id);
            }
        }
    }

    public static void Remove(IContributionServices ctx, string itemId) =>
        RadialState.MutateLevel(ctx, list => list.RemoveAll(i => i.Id == itemId));

    public static void DrillInto(IContributionServices ctx, string submenuId)
    {
        RadialState.Trail.Add(submenuId);
        RadialState.Raise(RadialChange.Items);
    }

    public static void Back()
    {
        if (RadialState.Trail.Count > 0)
        {
            RadialState.Trail.RemoveAt(RadialState.Trail.Count - 1);
            RadialState.Raise(RadialChange.Items);
        }
    }

    /// <summary>Preset whose actions Reset restores (null when the wheel came from no known preset).</summary>
    public static string? ResetPreset(RadialProfile profile)
    {
        if (profile.Preset is { } p && RadialModel.PresetIds.Contains(p))
        {
            return p;
        }

        return RadialModel.PresetIds.FirstOrDefault(id =>
            RadialModel.PresetTitle(id).Equals(profile.Name, StringComparison.OrdinalIgnoreCase) || id.Equals(profile.Name, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>The interactive miniature of the wheel in Settings (spec 3.10): click to edit, drag to swap, always dark.</summary>
internal sealed class RadialVisualCanvas : Border
{
    private const double Diameter = 250;
    private const double Ring = 92;
    private const double HubSize = 68;
    private const double Dead = 36;
    private const double ChipSize = 44;
    private const double CenterX = 150;
    private const double CenterY = 148;

    private readonly IContributionServices _ctx;
    private readonly Canvas _stage = new() { Width = 300, Height = 296, Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 34, 0, 0) };
    private readonly Grid _header = new() { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(14, 10, 14, 0) };
    private readonly List<RadialItem> _items = [];
    private readonly List<(Grid Root, Ellipse Fill, Border Icon, ScaleTransform Scale, RadialItem Item)> _chips = [];
    private readonly System.Windows.Shapes.Path _wedge = new() { IsHitTestVisible = false };
    private readonly ContentControl _hub = new() { Width = HubSize, Height = HubSize, IsHitTestVisible = false };
    private readonly Ellipse _glow = new() { Width = 300, Height = 300, IsHitTestVisible = false };
    private Color _color;
    private bool _hasPreset;
    private int? _hover;
    private int? _pressIndex;
    private bool _hubPress;
    private Point _pressPoint;
    private bool _dragging;
    private int? _dragTarget;
    private Grid? _floating;
    private string? _submenuName;

    public RadialVisualCanvas(IContributionServices ctx)
    {
        _ctx = ctx;
        Height = 330;
        CornerRadius = new CornerRadius(14);
        ClipToBounds = true;
        BorderThickness = new Thickness(0.8);
        BorderBrush = new SolidColorBrush(Color.FromArgb(ThemeManager.IsLight ? (byte)0x1F : (byte)0x24, 255, 255, 255));
        Background = new SolidColorBrush(Color.FromRgb(0x14, 0x17, 0x1C));
        Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 4, Direction = 270, Opacity = ThemeManager.IsLight ? 0.08 : 0.28, Color = Colors.Black };
        var layers = new Grid();
        layers.Children.Add(new Border
        {
            Background = new LinearGradientBrush([new GradientStop(Color.FromArgb(0x66, 255, 255, 255), 0), new GradientStop(Color.FromArgb(0x29, 255, 255, 255), 0.5), new GradientStop(Colors.Black, 1)], new Point(0, 0), new Point(1, 1)) { Opacity = 0.4 },
            IsHitTestVisible = false,
        });
        layers.Children.Add(_stage);
        layers.Children.Add(_header);
        Child = layers;
        SizeChanged += (_, e) => Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 14, 14);
        _stage.MouseMove += (_, e) => OnMove(e.GetPosition(_stage));
        _stage.MouseLeave += (_, _) =>
        {
            if (!_dragging)
            {
                _hover = null;
                UpdateVisual();
            }
        };
        _stage.MouseLeftButtonDown += OnDown;
        _stage.MouseLeftButtonUp += OnUp;
        _stage.MouseRightButtonUp += OnRightClick;
        Refresh();
        Loaded += (_, _) => RadialState.Changed += OnChanged;
        Unloaded += (_, _) => RadialState.Changed -= OnChanged;
    }

    private void OnChanged(RadialChange change)
    {
        if (change is RadialChange.Items or RadialChange.Profiles)
        {
            Dispatcher.BeginInvoke(Refresh);
        }
    }

    /// <summary>Rebuilds everything from the stored profile.</summary>
    public void Refresh()
    {
        var profile = RadialState.Selected(_ctx);
        _items.Clear();
        _items.AddRange(RadialState.Level(profile).Select(i => i.Clone()));
        _color = RadialColors.Resolve(profile.Color, light: false);
        _submenuName = RadialState.SubmenuName(profile);
        _hasPreset = _submenuName is null && RadialItemOps.ResetPreset(profile) is not null;
        _hover = null;
        _dragging = false;
        _dragTarget = null;
        _floating = null;
        BuildHeader();
        BuildStage();
        UpdateVisual();
    }

    private void BuildHeader()
    {
        _header.Children.Clear();
        _header.ColumnDefinitions.Clear();
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (_submenuName is { } name)
        {
            var back = new Button { Cursor = Cursors.Hand, ToolTip = Loc.T("Back"), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var chev = new TextBlock { Text = "", FontFamily = UiKit.IconFont, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(_color), VerticalAlignment = VerticalAlignment.Center };
            var label = new TextBlock { Text = name, FontSize = 12, FontWeight = FontWeights.Medium, Foreground = new SolidColorBrush(_color), Margin = new Thickness(5, 0, 0, 0) };
            row.Children.Add(chev);
            row.Children.Add(label);
            back.Content = row;
            back.Template = FlatButton();
            back.Click += (_, _) => RadialItemOps.Back();
            _header.Children.Add(back);
        }
        else
        {
            _header.Children.Add(new TextBlock { Text = Loc.T("RADIAL MENU"), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromArgb(153, 255, 255, 255)) });
        }

        if (_hasPreset)
        {
            var reset = new Button { Content = new TextBlock { Text = Loc.T("Reset"), FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(179, 255, 255, 255)) }, Cursor = Cursors.Hand, Template = FlatButton(), Padding = new Thickness(6, 2, 6, 2) };
            reset.Click += (_, _) =>
            {
                if (RadialDialogs.Confirm(Window.GetWindow(this), Loc.T("Reset actions"), Loc.T("Restore the default actions for this profile? Custom actions will be replaced."), Loc.T("Reset actions"), true))
                {
                    RadialState.Mutate(_ctx, (_, p) =>
                    {
                        if (RadialItemOps.ResetPreset(p) is { } preset)
                        {
                            p.Items = RadialModel.MakeItems(preset);
                            RadialState.Trail.Clear();
                        }
                    }, RadialChange.Items);
                }
            };
            Grid.SetColumn(reset, 1);
            _header.Children.Add(reset);
        }
    }

    private static ControlTemplate FlatButton()
    {
        var template = new ControlTemplate(typeof(Button));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        template.VisualTree = presenter;
        return template;
    }

    private void BuildStage()
    {
        _stage.Children.Clear();
        _chips.Clear();
        _glow.Fill = new RadialGradientBrush(Color.FromArgb(38, _color.R, _color.G, _color.B), Colors.Transparent) { RadiusX = 0.5, RadiusY = 0.5 };
        Canvas.SetLeft(_glow, CenterX - 150);
        Canvas.SetTop(_glow, CenterY - 150);
        _stage.Children.Add(_glow);
        var plate = new Ellipse
        {
            Width = Diameter,
            Height = Diameter,
            Fill = new SolidColorBrush(Color.FromArgb(107, 0, 0, 0)),
            Stroke = new SolidColorBrush(Color.FromArgb(31, 255, 255, 255)),
            StrokeThickness = 0.8,
            IsHitTestVisible = false,
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 4, Direction = 270, Opacity = 0.4, Color = Colors.Black },
        };
        Canvas.SetLeft(plate, CenterX - Diameter / 2);
        Canvas.SetTop(plate, CenterY - Diameter / 2);
        _stage.Children.Add(plate);
        var n = _items.Count;
        if (n > 1)
        {
            for (var i = 0; i < n; i++)
            {
                var a = 2 * Math.PI * (i + 0.5) / n;
                _stage.Children.Add(new Line
                {
                    X1 = CenterX + Math.Sin(a) * Dead,
                    Y1 = CenterY - Math.Cos(a) * Dead,
                    X2 = CenterX + Math.Sin(a) * 123,
                    Y2 = CenterY - Math.Cos(a) * 123,
                    Stroke = new SolidColorBrush(Color.FromArgb(23, 255, 255, 255)),
                    StrokeThickness = 1,
                    IsHitTestVisible = false,
                });
            }
        }

        _stage.Children.Add(_wedge);
        for (var i = 0; i < n; i++)
        {
            var scale = new ScaleTransform(1, 1);
            var fill = new Ellipse { Width = ChipSize, Height = ChipSize, Fill = new SolidColorBrush(_color), Opacity = 0 };
            var baseCircle = new Ellipse
            {
                Width = ChipSize,
                Height = ChipSize,
                Fill = new SolidColorBrush(Color.FromRgb(0x2E, 0x30, 0x38)),
                Stroke = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)),
                StrokeThickness = 0.8,
                Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 2, Direction = 270, Opacity = 0.35, Color = Colors.Black },
            };
            var iconHost = new Border { Width = ChipSize, Height = ChipSize, Child = Icon(_items[i], 0.9) };
            var root = new Grid { Width = ChipSize, Height = ChipSize, IsHitTestVisible = false, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale };
            root.Children.Add(baseCircle);
            root.Children.Add(fill);
            root.Children.Add(iconHost);
            var (x, y) = RadialGeometry.ChipOffset(i, n, Ring);
            Canvas.SetLeft(root, CenterX + x - ChipSize / 2);
            Canvas.SetTop(root, CenterY + y - ChipSize / 2);
            _stage.Children.Add(root);
            _chips.Add((root, fill, iconHost, scale, _items[i]));
        }

        var hubPlate = new Ellipse
        {
            Width = HubSize,
            Height = HubSize,
            Fill = new SolidColorBrush(Color.FromRgb(0x1F, 0x21, 0x29)),
            Stroke = new SolidColorBrush(Color.FromArgb(41, 255, 255, 255)),
            StrokeThickness = 0.8,
            IsHitTestVisible = false,
            Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 2, Direction = 270, Opacity = 0.3, Color = Colors.Black },
        };
        Canvas.SetLeft(hubPlate, CenterX - HubSize / 2);
        Canvas.SetTop(hubPlate, CenterY - HubSize / 2);
        Canvas.SetLeft(_hub, CenterX - HubSize / 2);
        Canvas.SetTop(_hub, CenterY - HubSize / 2);
        _stage.Children.Add(hubPlate);
        _stage.Children.Add(_hub);
    }

    private static FrameworkElement Icon(RadialItem item, double alpha) =>
        RadialWheelWindow.MakeIcon(item, light: false, null, 16, 26);

    // ------------------------------------------------------------------ interaction

    private int? SliceAt(Point p, bool ignoreDead)
    {
        var dx = p.X - CenterX;
        var dy = CenterY - p.Y;
        return RadialGeometry.SliceAt(dx, dy, _items.Count, Dead, ignoreDead);
    }

    private void OnMove(Point p)
    {
        if (_pressIndex is not null && !_dragging && (p - _pressPoint).Length > 6)
        {
            _dragging = true;
            _stage.CaptureMouse();
            var item = _items[_pressIndex.Value];
            _floating = new Grid { Width = ChipSize, Height = ChipSize, IsHitTestVisible = false, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1.15, 1.15) };
            _floating.Children.Add(new Ellipse { Width = ChipSize, Height = ChipSize, Fill = new SolidColorBrush(_color), Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 4, Direction = 270, Opacity = 0.6, Color = _color } });
            var icon = RadialWheelWindow.MakeIcon(item, false, null, 16, 26);
            if (icon is TextBlock tb)
            {
                tb.Foreground = Brushes.White;
            }

            _floating.Children.Add(icon);
            _stage.Children.Add(_floating);
        }

        if (_dragging && _floating is not null)
        {
            Canvas.SetLeft(_floating, p.X - ChipSize / 2);
            Canvas.SetTop(_floating, p.Y - ChipSize / 2);
            _dragTarget = SliceAt(p, ignoreDead: true);
        }
        else
        {
            var dist = Math.Sqrt(Math.Pow(p.X - CenterX, 2) + Math.Pow(p.Y - CenterY, 2));
            _hover = dist <= 140 ? SliceAt(p, ignoreDead: false) : null;
        }

        UpdateVisual();
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(_stage);
        _pressPoint = p;
        var dist = Math.Sqrt(Math.Pow(p.X - CenterX, 2) + Math.Pow(p.Y - CenterY, 2));
        _hubPress = dist < Dead || _items.Count == 0;
        _pressIndex = _hubPress ? null : SliceAt(p, ignoreDead: false);
        if (dist > 140)
        {
            _pressIndex = null;
            _hubPress = false;
        }

        _stage.CaptureMouse();
        e.Handled = true;
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(_stage);
        var wasDragging = _dragging;
        var from = _pressIndex;
        var target = _dragTarget;
        var hub = _hubPress;
        _stage.ReleaseMouseCapture();
        _pressIndex = null;
        _hubPress = false;
        _dragging = false;
        _dragTarget = null;
        if (_floating is not null)
        {
            _stage.Children.Remove(_floating);
            _floating = null;
        }

        if (wasDragging && from is { } f)
        {
            if (target is { } t && t != f && t < _items.Count)
            {
                var a = _items[f].Id;
                var b = _items[t].Id;
                RadialState.MutateLevel(_ctx, list =>
                {
                    var ia = list.FindIndex(x => x.Id == a);
                    var ib = list.FindIndex(x => x.Id == b);
                    if (ia >= 0 && ib >= 0)
                    {
                        (list[ia], list[ib]) = (list[ib], list[ia]);
                    }
                });
            }
            else
            {
                UpdateVisual();
            }

            return;
        }

        var dist = Math.Sqrt(Math.Pow(p.X - CenterX, 2) + Math.Pow(p.Y - CenterY, 2));
        if (hub && dist < Dead + 6)
        {
            if (_submenuName is not null)
            {
                RadialItemOps.Back();
            }
            else if (_items.Count == 0)
            {
                RadialItemOps.Add(this, _ctx);
            }

            return;
        }

        if (from is { } idx && idx < _items.Count)
        {
            RadialItemOps.Edit(this, _ctx, _items[idx].Id);
        }
    }

    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(_stage);
        var idx = SliceAt(p, ignoreDead: false);
        if (idx is not { } i || i >= _items.Count)
        {
            return;
        }

        var item = _items[i];
        var menu = new ContextMenu();
        var edit = new MenuItem { Header = Loc.T("Radial menu") };
        edit.Click += (_, _) => RadialItemOps.Edit(this, _ctx, item.Id);
        menu.Items.Add(edit);
        if (item.Kind == RadialKinds.Submenu)
        {
            var drill = new MenuItem { Header = Loc.T("Edit actions") };
            drill.Click += (_, _) => RadialItemOps.DrillInto(_ctx, item.Id);
            menu.Items.Add(drill);
        }

        menu.Items.Add(new Separator());
        var remove = new MenuItem { Header = Loc.T("Remove") };
        remove.Click += (_, _) => RadialItemOps.Remove(_ctx, item.Id);
        menu.Items.Add(remove);
        menu.IsOpen = true;
        e.Handled = true;
    }

    // ------------------------------------------------------------------ drawing

    private void UpdateVisual()
    {
        var active = _dragging ? _dragTarget : _hover;
        for (var i = 0; i < _chips.Count; i++)
        {
            var chip = _chips[i];
            var on = i == active;
            chip.Fill.Opacity = on ? 1 : 0;
            var s = !_dragging && on ? 1.08 : 1;
            chip.Scale.ScaleX = chip.Scale.ScaleY = s;
            chip.Root.Opacity = _dragging && _pressIndex == i ? 0.25 : 1;
            if (chip.Icon.Child is TextBlock tb)
            {
                tb.Foreground = on ? Brushes.White : new SolidColorBrush(Color.FromArgb(230, 255, 255, 255));
            }
        }

        // wedge
        if (active is { } a && _items.Count > 0)
        {
            var step = 360.0 / _items.Count;
            var a0 = (a * step - step / 2 - 90) * Math.PI / 180;
            var a1 = (a * step + step / 2 - 90) * Math.PI / 180;
            Point P(double r, double ang) => new(CenterX + r * Math.Cos(ang), CenterY + r * Math.Sin(ang));
            var fig = new PathFigure { StartPoint = P(Dead, a0), IsClosed = true };
            fig.Segments.Add(new LineSegment(P(123, a0), true));
            fig.Segments.Add(new ArcSegment(P(123, a1), new Size(123, 123), 0, step > 180, SweepDirection.Clockwise, true));
            fig.Segments.Add(new LineSegment(P(Dead, a1), true));
            fig.Segments.Add(new ArcSegment(P(Dead, a0), new Size(Dead, Dead), 0, step > 180, SweepDirection.Counterclockwise, true));
            var geo = new PathGeometry();
            geo.Figures.Add(fig);
            _wedge.Data = geo;
            _wedge.Fill = new SolidColorBrush(Color.FromArgb(_dragging ? (byte)82 : (byte)56, _color.R, _color.G, _color.B));
        }
        else
        {
            _wedge.Data = Geometry.Empty;
        }

        _hub.Content = HubContent(active);
    }

    private FrameworkElement HubContent(int? active)
    {
        var white = Brushes.White;
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        TextBlock T(string text, double size, FontWeight w, Brush fg, int lines = 2)
        {
            return new TextBlock { Text = text, FontSize = size, FontWeight = w, Foreground = fg, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = size * 1.35 * lines, MaxWidth = 58, HorizontalAlignment = HorizontalAlignment.Center };
        }

        TextBlock G(string glyph, double size) => new() { Text = glyph, FontFamily = UiKit.IconFont, FontSize = size, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(_color), HorizontalAlignment = HorizontalAlignment.Center };
        var dim = new SolidColorBrush(Color.FromArgb(179, 255, 255, 255));
        if (_dragging && _dragTarget is { } t && t < _items.Count)
        {
            stack.Children.Add(G("", 13));
            stack.Children.Add(T(RadialModel.DisplayName(_items[t]), 9, FontWeights.SemiBold, white));
        }
        else if (active is { } h && h < _items.Count)
        {
            stack.Children.Add(T(RadialModel.DisplayName(_items[h]), 10, FontWeights.SemiBold, white));
            stack.Children.Add(T(RadialKinds.Label(_items[h].Kind), 8, FontWeights.Medium, new SolidColorBrush(Color.FromArgb(153, 255, 255, 255)), 1));
        }
        else if (_submenuName is not null)
        {
            stack.Children.Add(G("", 12));
            stack.Children.Add(T(Loc.T("Back"), 9, FontWeights.Medium, dim, 1));
        }
        else if (_items.Count == 0)
        {
            stack.Children.Add(G("", 14));
            stack.Children.Add(T(Loc.T("Add action"), 8, FontWeights.Medium, dim, 1));
        }
        else
        {
            var mark = BrandMark.Create(28);
            mark.Fill = new SolidColorBrush(Color.FromArgb(217, 255, 255, 255));
            mark.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(mark);
        }

        return stack;
    }
}
