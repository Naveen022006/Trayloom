// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Trayloom.Core;

namespace Trayloom.Features.Capture;

/// <summary>Shared helpers of the two editor popovers (also used by the recorder editor).</summary>
internal static class PopoverKit
{
    public static readonly double[][] PaletteColors =
    [
        [0.96, 0.26, 0.21], [1.00, 0.58, 0.00], [1.00, 0.80, 0.00], [0.55, 0.86, 0.25], [0.20, 0.78, 0.35], [0.10, 0.74, 0.61], [0.15, 0.78, 0.85], [0.04, 0.52, 1.00], [0.35, 0.34, 0.84], [0.69, 0.32, 0.87],
        [1.00, 0.45, 0.66], [0.91, 0.12, 0.39], [0.55, 0.39, 0.29], [0.11, 0.16, 0.32], [0.05, 0.05, 0.06], [0.25, 0.25, 0.28], [0.55, 0.55, 0.58], [0.85, 0.85, 0.87], [1.00, 1.00, 1.00], [0.99, 0.93, 0.85],
    ];

    public static Color ToColor(double[] c) => Color.FromRgb((byte)Math.Round(Math.Clamp(c[0], 0, 1) * 255), (byte)Math.Round(Math.Clamp(c[1], 0, 1) * 255), (byte)Math.Round(Math.Clamp(c[2], 0, 1) * 255));

    public static Border Cell(UIElement? content, bool selected, double height = 38)
    {
        var b = new Border
        {
            Height = height, CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand, Margin = new Thickness(4),
            BorderBrush = selected ? Ed.Accent : new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), BorderThickness = new Thickness(selected ? 2.5 : 1), ClipToBounds = true,
        };
        if (content is not null)
        {
            b.Child = content;
        }

        return b;
    }

    public static UIElement LabeledSlider(string label, double min, double max, double value, Action<double> changed, bool enabled = true, double labelWidth = 64, string format = "{0:0}%", double scaleForLabel = 100)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3), IsEnabled = enabled, Opacity = enabled ? 1 : 0.4 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        grid.Children.Add(Ed.Label(label, 12, FontWeights.Normal, Ed.Fg));
        var readout = Ed.Label(string.Format(format, value * scaleForLabel), 10.5, FontWeights.Medium, Ed.FgDim);
        readout.HorizontalAlignment = HorizontalAlignment.Right;
        var slider = Ed.NewSlider(min, max, value, double.NaN, v =>
        {
            readout.Text = string.Format(format, v * scaleForLabel);
            changed(v);
        });
        slider.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(readout, 2);
        grid.Children.Add(slider);
        grid.Children.Add(readout);
        return grid;
    }

    public static string? PickImage()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp|All files|*.*" };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SystemParametersInfoW(uint action, uint param, System.Text.StringBuilder value, uint winIni);

    public static string? DesktopWallpaper()
    {
        var sb = new System.Text.StringBuilder(520);
        return SystemParametersInfoW(0x0073, (uint)sb.Capacity, sb, 0) && sb.Length > 0 && File.Exists(sb.ToString()) ? sb.ToString() : null;
    }
}

/// <summary>The "Background" popover: swatch grid, custom colours, saved looks and margin / corners / blur sliders.</summary>
internal sealed class BackdropPopover : StackPanel
{
    private BackdropStyle _style;
    private readonly IAppSettings _app;
    private readonly Action<BackdropStyle> _changed;
    private readonly bool _showSliders;
    private int _activeWell;
    private bool _customGradient;

    public BackdropPopover(BackdropStyle style, IAppSettings app, Action<BackdropStyle> changed, bool showSliders)
    {
        _style = style.Clone();
        _app = app;
        _changed = changed;
        _showSliders = showSliders;
        Width = 292;
        _customGradient = _style.Kind == "gradient";
        Rebuild();
    }

    private List<BackdropStyle> Saved()
    {
        try
        {
            return JsonSerializer.Deserialize<List<BackdropStyle>>(_app.Get<string>(K.BackdropPresets) ?? "[]", new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase })?.Select(s => s.Sanitized()).Where(s => !s.IsNone).ToList() ?? new();
        }
        catch
        {
            return new();
        }
    }

    private void SaveList(List<BackdropStyle> list) =>
        _app.Set(K.BackdropPresets, JsonSerializer.Serialize(list.Take(12).ToList(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

    private void Apply(BackdropStyle s)
    {
        s.Padding = _style.Padding;
        s.CornerRadius = _style.CornerRadius;
        s.Blur = _style.Blur;
        _style = s;
        _changed(_style.Clone());
        Rebuild();
    }

    private void Rebuild()
    {
        Children.Clear();
        var grid = new UniformGrid { Columns = 4 };
        // None
        var none = PopoverKit.Cell(new Grid { Background = Ed.Hover, Children = { Ed.Icon("", 14, Ed.FgDim) } }, _style.IsNone);
        none.ToolTip = Strings.None;
        none.MouseLeftButtonUp += (_, _) => Apply(new BackdropStyle { Kind = "none" });
        grid.Children.Add(none);
        foreach (var p in BackdropStyle.Presets)
        {
            var preset = p;
            var brush = new LinearGradientBrush(PopoverKit.ToColor(p.A), PopoverKit.ToColor(p.B), new Point(0, 0), new Point(1, 1));
            var cell = PopoverKit.Cell(new Border { Background = brush }, _style.Kind == "preset" && _style.PresetId == p.Id);
            cell.ToolTip = p.Label;
            cell.MouseLeftButtonUp += (_, _) => Apply(new BackdropStyle { Kind = "preset", PresetId = preset.Id });
            grid.Children.Add(cell);
        }

        foreach (var saved in Saved())
        {
            var s = saved;
            var cell = PopoverKit.Cell(SwatchOf(s), false);
            cell.ToolTip = Loc.T("Custom");
            cell.MouseLeftButtonUp += (_, _) => Apply(s.Clone());
            var menu = new ContextMenu();
            var remove = new MenuItem { Header = Loc.T("Remove") };
            remove.Click += (_, _) =>
            {
                var list = Saved();
                list.RemoveAll(x => x.ToJson() == s.ToJson());
                SaveList(list);
                Rebuild();
            };
            menu.Items.Add(remove);
            cell.ContextMenu = menu;
            grid.Children.Add(cell);
        }

        if (PopoverKit.DesktopWallpaper() is { } wall)
        {
            var img = Output.LoadImage(wall);
            if (img is not null)
            {
                var cell = PopoverKit.Cell(new Image { Source = img, Stretch = Stretch.UniformToFill }, _style.Kind == "image" && _style.ImagePath == wall);
                cell.ToolTip = Loc.T("Wallpaper");
                cell.MouseLeftButtonUp += (_, _) => Apply(new BackdropStyle { Kind = "image", ImagePath = wall });
                grid.Children.Add(cell);
            }
        }

        var imageCell = PopoverKit.Cell(new Grid { Children = { Ed.Label(Loc.T("Image…"), 11, FontWeights.Medium, Ed.FgDim) } }, _style.Kind == "image" && _style.ImagePath is not null && PopoverKit.DesktopWallpaper() != _style.ImagePath);
        ((Grid)imageCell.Child).Children[0].SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        imageCell.BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
        imageCell.MouseLeftButtonUp += (_, _) =>
        {
            if (PopoverKit.PickImage() is { } path)
            {
                Apply(new BackdropStyle { Kind = "image", ImagePath = path });
            }
        };
        grid.Children.Add(imageCell);
        Children.Add(grid);
        Children.Add(Ed.Divider(1));
        Children[^1].SetValue(WidthProperty, double.NaN);
        ((Border)Children[^1]).Height = 1;
        ((Border)Children[^1]).Width = double.NaN;
        ((Border)Children[^1]).Margin = new Thickness(0, 8, 0, 8);

        // custom section
        var seg = new SegmentedPair(Loc.T("Solid"), Loc.T("Gradient"), _customGradient ? 1 : 0, i =>
        {
            _customGradient = i == 1;
            var c1 = _style.Colors is { Count: > 0 } ? _style.Colors[0] : [0.20, 0.47, 0.96];
            var c2 = _style.Colors is { Count: > 1 } ? _style.Colors[1] : [0.45, 0.83, 0.98];
            Apply(_customGradient ? new BackdropStyle { Kind = "gradient", Colors = [c1, c2] } : new BackdropStyle { Kind = "solid", Colors = [c1] });
        });
        var head = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(seg);
        var wells = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        var colors = _style.Colors ?? [[0.20, 0.47, 0.96]];
        var wellCount = _customGradient ? 2 : 1;
        for (var i = 0; i < wellCount; i++)
        {
            var idx = i;
            var c = i < colors.Count ? colors[i] : [0.45, 0.83, 0.98];
            var well = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(PopoverKit.ToColor(c)), Margin = new Thickness(3), Cursor = Cursors.Hand, BorderBrush = _activeWell == idx ? Ed.Accent : Brushes.Transparent, BorderThickness = new Thickness(2) };
            well.MouseLeftButtonUp += (_, _) =>
            {
                _activeWell = idx;
                Rebuild();
            };
            wells.Children.Add(well);
        }

        var save = new EdButton(G.Add, null, Loc.T("Save background"), () =>
        {
            if (_style.IsNone)
            {
                return;
            }

            var list = Saved();
            if (list.Any(x => x.ToJson() == _style.ToJson()))
            {
                return;
            }

            list.Add(_style.Clone());
            if (list.Count > 12)
            {
                list.RemoveAt(0);
            }

            SaveList(list);
            Rebuild();
        }, width: 24) { IsEnabled = _style.Kind is "solid" or "gradient" or "image" };
        wells.Children.Add(save);
        Grid.SetColumn(wells, 1);
        head.Children.Add(wells);
        Children.Add(head);
        var palette = new UniformGrid { Columns = 10 };
        foreach (var c in PopoverKit.PaletteColors)
        {
            var color = c;
            var dot = new Border { Width = 17, Height = 17, CornerRadius = new CornerRadius(8.5), Background = new SolidColorBrush(PopoverKit.ToColor(c)), BorderBrush = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)), BorderThickness = new Thickness(1), Margin = new Thickness(1.5), Cursor = Cursors.Hand };
            dot.MouseLeftButtonUp += (_, _) =>
            {
                var cols = (_style.Colors ?? [[0.20, 0.47, 0.96], [0.45, 0.83, 0.98]]).Select(x => x.ToArray()).ToList();
                while (cols.Count < 2)
                {
                    cols.Add([0.45, 0.83, 0.98]);
                }

                cols[_activeWell] = color;
                Apply(_customGradient ? new BackdropStyle { Kind = "gradient", Colors = [cols[0], cols[1]] } : new BackdropStyle { Kind = "solid", Colors = [cols[0]] });
            };
            palette.Children.Add(dot);
        }

        Children.Add(palette);
        if (_showSliders)
        {
            var sep = new Border { Height = 1, Background = Ed.PlateStroke, Margin = new Thickness(0, 10, 0, 8) };
            Children.Add(sep);
            Children.Add(PopoverKit.LabeledSlider(Strings.Screenshot.MarginLabel, 0, 1, _style.Padding, v => { _style.Padding = v; _changed(_style.Clone()); }, !_style.IsNone));
            Children.Add(PopoverKit.LabeledSlider(Strings.Screenshot.CornersLabel, 0, 1, _style.CornerRadius, v => { _style.CornerRadius = v; _changed(_style.Clone()); }));
            Children.Add(PopoverKit.LabeledSlider(Strings.Screenshot.BlurLabel, 0, 1, _style.Blur, v => { _style.Blur = v; _changed(_style.Clone()); }, !_style.IsNone));
        }
    }

    private static UIElement SwatchOf(BackdropStyle s)
    {
        var brush = s.CreateBrush(100, 100);
        return new Border { Background = brush ?? Ed.Hover };
    }
}

/// <summary>Two-option segmented control in the editor's dark style.</summary>
internal sealed class SegmentedPair : Border
{
    public SegmentedPair(string a, string b, int selected, Action<int> changed)
    {
        CornerRadius = new CornerRadius(7);
        Background = Ed.Faint;
        Padding = new Thickness(2);
        var row = new UniformGrid { Columns = 2 };
        for (var i = 0; i < 2; i++)
        {
            var idx = i;
            var t = Ed.Label(i == 0 ? a : b, 11.5, FontWeights.Medium, i == selected ? Brushes.White : Ed.FgDim);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            var cell = new Border { Child = t, CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 3, 8, 3), Background = i == selected ? Ed.Accent : Brushes.Transparent, Cursor = Cursors.Hand };
            cell.MouseLeftButtonUp += (_, _) => changed(idx);
            row.Children.Add(cell);
        }

        Child = row;
    }
}

/// <summary>The "Watermark" popover: none / text / image, presets, placement, size, opacity and rotation.</summary>
internal sealed class WatermarkPopover : StackPanel
{
    private WatermarkStyle _style;
    private readonly IAppSettings _app;
    private readonly Action<WatermarkStyle> _changed;

    public WatermarkPopover(WatermarkStyle style, IAppSettings app, Action<WatermarkStyle> changed)
    {
        _style = style.Clone();
        _app = app;
        _changed = changed;
        Width = 292;
        Rebuild();
    }

    private List<WatermarkStyle> Saved()
    {
        try
        {
            return JsonSerializer.Deserialize<List<WatermarkStyle>>(_app.Get<string>(K.WatermarkPresets) ?? "[]", new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) ?? new();
        }
        catch
        {
            return new();
        }
    }

    private void SaveList(List<WatermarkStyle> list) =>
        _app.Set(K.WatermarkPresets, JsonSerializer.Serialize(list.Take(12).ToList(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

    private void Push()
    {
        _changed(_style.Clone());
    }

    private void Rebuild()
    {
        Children.Clear();
        var seg = new Border { CornerRadius = new CornerRadius(7), Background = Ed.Faint, Padding = new Thickness(2), Margin = new Thickness(0, 0, 0, 10) };
        var row = new UniformGrid { Columns = 3 };
        var kinds = new[] { ("none", Strings.None), ("text", Loc.T("Text")), ("image", Loc.T("Image")) };
        foreach (var (kind, title) in kinds)
        {
            var sel = _style.Kind == kind;
            var t = Ed.Label(title, 11.5, FontWeights.Medium, sel ? Brushes.White : Ed.FgDim);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            var cell = new Border { Child = t, CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 3, 8, 3), Background = sel ? Ed.Accent : Brushes.Transparent, Cursor = Cursors.Hand };
            var k = kind;
            cell.MouseLeftButtonUp += (_, _) =>
            {
                _style.Kind = k;
                if (k == "image" && (_style.ImagePath is null || !File.Exists(_style.ImagePath)) && PopoverKit.PickImage() is { } path)
                {
                    _style.ImagePath = path;
                }

                Push();
                Rebuild();
            };
            row.Children.Add(cell);
        }

        seg.Child = row;
        Children.Add(seg);

        if (_style.Kind == "text")
        {
            var box = new TextBox { Text = _style.Text, MaxLength = 120, Background = Ed.Faint, Foreground = Ed.Fg, BorderBrush = Ed.PlateStroke, CaretBrush = Brushes.White, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 0, 8) };
            box.TextChanged += (_, _) =>
            {
                _style.Text = box.Text.Trim();
                Push();
            };
            box.Loaded += (_, _) =>
            {
                if (string.IsNullOrEmpty(box.Text))
                {
                    box.Focus();
                }
            };
            Children.Add(box);
            var dots = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            foreach (var (name, label, c) in Palette.Colors)
            {
                var n = name;
                var dot = new Border { Width = 17, Height = 17, CornerRadius = new CornerRadius(8.5), Background = new SolidColorBrush(c), Margin = new Thickness(3), Cursor = Cursors.Hand, ToolTip = label, BorderBrush = _style.Color == name ? Brushes.White : new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), BorderThickness = new Thickness(_style.Color == name ? 2 : 1) };
                dot.MouseLeftButtonUp += (_, _) =>
                {
                    _style.Color = n;
                    Push();
                    Rebuild();
                };
                dots.Children.Add(dot);
            }

            Children.Add(dots);
        }
        else if (_style.Kind == "image")
        {
            var r = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            r.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            r.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = Ed.Label(string.IsNullOrEmpty(_style.ImagePath) ? Strings.None : Path.GetFileName(_style.ImagePath), 12, FontWeights.Normal, Ed.Fg);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            r.Children.Add(name);
            var choose = new EdButton(null, Strings.Choose, null, () =>
            {
                if (PopoverKit.PickImage() is { } p)
                {
                    _style.ImagePath = p;
                    Push();
                    Rebuild();
                }
            }, height: 24);
            Grid.SetColumn(choose, 1);
            r.Children.Add(choose);
            Children.Add(r);
        }

        // presets
        var saved = Saved();
        if (!_style.IsNone || saved.Count > 0)
        {
            var grid = new UniformGrid { Columns = 4 };
            foreach (var p in saved)
            {
                var preset = p;
                var content = p.Kind == "text"
                    ? (UIElement)new Grid { Children = { new TextBlock { Text = p.Text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Palette.Get(p.Color)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis } } }
                    : new Image { Source = p.ImagePath is null ? null : Output.LoadImage(p.ImagePath), Stretch = Stretch.Uniform, Margin = new Thickness(4) };
                var cell = PopoverKit.Cell(content, p.SameLook(_style));
                cell.Background = new SolidColorBrush(Color.FromArgb(90, 128, 128, 128));
                cell.MouseLeftButtonUp += (_, _) =>
                {
                    _style = preset.Clone();
                    Push();
                    Rebuild();
                };
                var menu = new ContextMenu();
                var remove = new MenuItem { Header = Loc.T("Remove") };
                remove.Click += (_, _) =>
                {
                    var list = Saved();
                    list.RemoveAll(x => x.SameLook(preset));
                    SaveList(list);
                    Rebuild();
                };
                menu.Items.Add(remove);
                cell.ContextMenu = menu;
                grid.Children.Add(cell);
            }

            var canSave = !_style.IsNone && !saved.Any(x => x.SameLook(_style));
            var add = PopoverKit.Cell(new Grid { Children = { Ed.Icon(G.Add, 12, Ed.FgDim) } }, false);
            add.BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
            add.ToolTip = Loc.T("Save watermark");
            add.Opacity = canSave ? 1 : 0.4;
            add.MouseLeftButtonUp += (_, _) =>
            {
                if (!canSave)
                {
                    return;
                }

                var list = Saved();
                list.Add(_style.Clone());
                SaveList(list);
                Rebuild();
            };
            grid.Children.Add(add);
            Children.Add(grid);
        }

        Children.Add(new Border { Height = 1, Background = Ed.PlateStroke, Margin = new Thickness(0, 8, 0, 8) });
        var enabled = _style.Kind != "none";
        var posRow = new Grid { Margin = new Thickness(0, 0, 0, 6), IsEnabled = enabled, Opacity = enabled ? 1 : 0.4 };
        posRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        posRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        posRow.Children.Add(Ed.Label(Loc.T("Position"), 12));
        var pg = new UniformGrid { Columns = 3, Width = 100, HorizontalAlignment = HorizontalAlignment.Left };
        for (var i = 0; i < 9; i++)
        {
            var anchor = WatermarkStyle.Anchors[i];
            var c = new Border { Width = 22, Height = 15, CornerRadius = new CornerRadius(3), Margin = new Thickness(1.5), Background = _style.Anchor == anchor ? new SolidColorBrush(Color.FromArgb(217, 10, 132, 255)) : Ed.Hover, ToolTip = WatermarkStyle.AnchorNames[i], Cursor = Cursors.Hand };
            c.MouseLeftButtonUp += (_, _) =>
            {
                _style.Anchor = anchor;
                Push();
                Rebuild();
            };
            pg.Children.Add(c);
        }

        Grid.SetColumn(pg, 1);
        posRow.Children.Add(pg);
        Children.Add(posRow);
        Children.Add(PopoverKit.LabeledSlider(Loc.T("Size"), 0, 1, _style.Size, v => { _style.Size = v; Push(); }, enabled, 80));
        Children.Add(PopoverKit.LabeledSlider(Loc.T("Opacity"), 0.05, 1, _style.Opacity, v => { _style.Opacity = v; Push(); }, enabled, 80));
        Children.Add(PopoverKit.LabeledSlider(Loc.T("Rotation"), -90, 90, _style.Rotation, v => { _style.Rotation = Math.Round(v); Push(); }, enabled, 80, "{0:0}°", 1));
    }
}
