// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using WinKnife.Core;
using WinKnife.Core.Ui;
using WpfPath = System.Windows.Shapes.Path;

namespace WinKnife.Features.Tools;

internal enum EditorTool
{
    Arrow,
    Box,
    Pen,
    Text,
    Blur,
    Crop,
}

/// <summary>Small annotation editor: arrow, box, pen, text, pixelate and crop with undo, copy and save.</summary>
internal sealed class ScreenshotEditorWindow : Window
{
    private static readonly Color[] Palette =
    [
        Color.FromRgb(0xFF, 0x3B, 0x30), Color.FromRgb(0xFF, 0xCC, 0x00), Color.FromRgb(0x34, 0xC7, 0x59),
        Color.FromRgb(0x0A, 0x84, 0xFF), Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x10, 0x10, 0x10),
    ];

    private static readonly double[] Widths = [3, 6, 10];

    private readonly ScreenshotFeature _feature;
    private readonly Image _image = new();
    private readonly Canvas _canvas = new() { Background = Brushes.Transparent };
    private readonly Grid _stage = new();
    private readonly Dictionary<EditorTool, ToggleButton> _toolButtons = new();
    private readonly List<Action> _undo = new();
    private BitmapSource _base;
    private EditorTool _tool = EditorTool.Arrow;
    private Color _color = Palette[0];
    private double _width = Widths[0];

    private Point _start;
    private bool _drawing;
    private UIElement? _live;
    private TextBox? _editing;
    private Polyline? _pen;

    public ScreenshotEditorWindow(BitmapSource image, ScreenshotFeature feature)
    {
        _feature = feature;
        _base = image;
        Title = "Screenshot";
        SetResourceReference(StyleProperty, ThemeKeys.WindowStyle);
        SourceInitialized += (_, _) => UiKit.ApplyTitleBarTheme(this);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var work = SystemParameters.WorkArea;
        Width = Math.Clamp(image.PixelWidth + 80, 760, work.Width * 0.9);
        Height = Math.Clamp(image.PixelHeight + 140, 520, work.Height * 0.9);
        MinWidth = 700;

        _image.Stretch = Stretch.None;
        _stage.Children.Add(_image);
        _stage.Children.Add(_canvas);
        _canvas.ClipToBounds = true;
        ApplyBase();

        var view = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = _stage, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var host = new Border { Margin = new Thickness(16, 8, 16, 16), Child = view, Cursor = Cursors.Cross };
        host.SetResourceReference(Border.BackgroundProperty, ThemeKeys.Surface);

        var root = new DockPanel();
        var toolbar = BuildToolbar();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        root.Children.Add(host);
        Content = root;

        _canvas.MouseLeftButtonDown += OnDown;
        _canvas.MouseMove += OnMove;
        _canvas.MouseLeftButtonUp += OnUp;
        PreviewKeyDown += OnKey;
        SelectTool(EditorTool.Arrow);
    }

    private FrameworkElement BuildToolbar()
    {
        var bar = new WrapPanel { Margin = new Thickness(16, 12, 16, 4) };

        foreach (var (tool, label) in new[]
        {
            (EditorTool.Arrow, "Arrow"), (EditorTool.Box, "Box"), (EditorTool.Pen, "Pen"),
            (EditorTool.Text, "Text"), (EditorTool.Blur, "Blur"), (EditorTool.Crop, "Crop"),
        })
        {
            var button = new ToggleButton { Content = label, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 4, 6) };
            var captured = tool;
            button.Click += (_, _) => SelectTool(captured);
            _toolButtons[tool] = button;
            bar.Children.Add(button);
        }

        bar.Children.Add(new Border { Width = 12 });
        foreach (var color in Palette)
        {
            var swatch = new Button
            {
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 6, 6),
                Background = new SolidColorBrush(color),
                BorderThickness = new Thickness(2),
                Cursor = Cursors.Hand,
                ToolTip = $"#{color.R:X2}{color.G:X2}{color.B:X2}",
            };
            swatch.Click += (_, _) =>
            {
                _color = color;
                if (_editing is not null)
                {
                    _editing.Foreground = new SolidColorBrush(color);
                }
            };
            bar.Children.Add(swatch);
        }

        bar.Children.Add(new Border { Width = 8 });
        foreach (var (w, label) in new[] { (Widths[0], "S"), (Widths[1], "M"), (Widths[2], "L") })
        {
            var size = new Button { Content = label, Width = 30, Margin = new Thickness(0, 0, 4, 6), ToolTip = "Line thickness / text size" };
            var captured = w;
            size.Click += (_, _) => _width = captured;
            bar.Children.Add(size);
        }

        bar.Children.Add(new Border { Width = 16 });
        bar.Children.Add(Wrap(UiKit.Btn("Undo", Undo, false, "")));
        bar.Children.Add(Wrap(UiKit.Btn("Copy", Copy, true, "")));
        bar.Children.Add(Wrap(UiKit.Btn("Save", SaveDefault, false, "")));
        bar.Children.Add(Wrap(UiKit.Btn("Save as...", SaveAs)));
        return bar;

        static FrameworkElement Wrap(Button b)
        {
            b.Margin = new Thickness(0, 0, 6, 6);
            return b;
        }
    }

    private void SelectTool(EditorTool tool)
    {
        CommitText();
        _tool = tool;
        foreach (var (t, b) in _toolButtons)
        {
            b.IsChecked = t == tool;
        }
    }

    private void ApplyBase()
    {
        _image.Source = _base;
        _image.Width = _base.PixelWidth;
        _image.Height = _base.PixelHeight;
        _stage.Width = _base.PixelWidth;
        _stage.Height = _base.PixelHeight;
        _canvas.Width = _base.PixelWidth;
        _canvas.Height = _base.PixelHeight;
    }

    private Point Clamp(Point p) => new(Math.Clamp(p.X, 0, _canvas.Width), Math.Clamp(p.Y, 0, _canvas.Height));

    // ---- Mouse handling -------------------------------------------------------------------

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        CommitText();
        _start = Clamp(e.GetPosition(_canvas));
        if (_tool == EditorTool.Text)
        {
            BeginText(_start);
            e.Handled = true;
            return;
        }

        _drawing = true;
        _canvas.CaptureMouse();
        var brush = new SolidColorBrush(_color);
        switch (_tool)
        {
            case EditorTool.Arrow:
                _live = new WpfPath { Stroke = brush, Fill = brush, StrokeThickness = _width, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                break;
            case EditorTool.Box:
                _live = new Rectangle { Stroke = brush, StrokeThickness = _width, RadiusX = 2, RadiusY = 2 };
                break;
            case EditorTool.Pen:
                _pen = new Polyline { Stroke = brush, StrokeThickness = _width, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                _pen.Points.Add(_start);
                _live = _pen;
                break;
            case EditorTool.Blur:
            case EditorTool.Crop:
                _live = new Rectangle
                {
                    Stroke = _tool == EditorTool.Crop ? Brushes.White : Brushes.DeepSkyBlue,
                    StrokeThickness = 1.5,
                    StrokeDashArray = [4, 3],
                    Fill = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                };
                break;
        }

        if (_live is not null)
        {
            _canvas.Children.Add(_live);
        }

        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_drawing || _live is null)
        {
            return;
        }

        var p = Clamp(e.GetPosition(_canvas));
        switch (_tool)
        {
            case EditorTool.Arrow:
                ((WpfPath)_live).Data = ArrowGeometry(_start, p, _width);
                break;
            case EditorTool.Pen:
                _pen!.Points.Add(p);
                break;
            default:
                PlaceRect((Rectangle)_live, _start, p);
                break;
        }
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_drawing)
        {
            return;
        }

        _drawing = false;
        _canvas.ReleaseMouseCapture();
        var end = Clamp(e.GetPosition(_canvas));
        var live = _live;
        _live = null;
        _pen = null;
        if (live is null)
        {
            return;
        }

        var tooSmall = (end - _start).Length < 4;
        _canvas.Children.Remove(live);
        if (tooSmall && _tool != EditorTool.Pen)
        {
            return;
        }

        switch (_tool)
        {
            case EditorTool.Arrow:
            case EditorTool.Box:
            case EditorTool.Pen:
                _canvas.Children.Add(live);
                _undo.Add(() => _canvas.Children.Remove(live));
                break;
            case EditorTool.Blur:
                ApplyPixelate(RectFrom(_start, end));
                break;
            case EditorTool.Crop:
                ApplyCrop(RectFrom(_start, end));
                break;
        }
    }

    private static Rect RectFrom(Point a, Point b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static void PlaceRect(Rectangle rect, Point a, Point b)
    {
        var r = RectFrom(a, b);
        Canvas.SetLeft(rect, r.X);
        Canvas.SetTop(rect, r.Y);
        rect.Width = r.Width;
        rect.Height = r.Height;
    }

    private static Geometry ArrowGeometry(Point from, Point to, double thickness)
    {
        var vector = to - from;
        var length = vector.Length;
        var group = new GeometryGroup();
        if (length < 1)
        {
            return group;
        }

        vector.Normalize();
        var head = Math.Min(length * 0.6, 12 + thickness * 3);
        var normal = new Vector(-vector.Y, vector.X);
        var baseCentre = to - vector * head;
        var left = baseCentre + normal * head * 0.5;
        var right = baseCentre - normal * head * 0.5;
        group.Children.Add(new LineGeometry(from, baseCentre));
        var tri = new PathFigure { StartPoint = to, IsClosed = true, IsFilled = true };
        tri.Segments.Add(new LineSegment(left, true));
        tri.Segments.Add(new LineSegment(right, true));
        group.Children.Add(new PathGeometry([tri]));
        return group;
    }

    // ---- Text ----------------------------------------------------------------------------

    private void BeginText(Point at)
    {
        var box = new TextBox
        {
            Foreground = new SolidColorBrush(_color),
            CaretBrush = new SolidColorBrush(_color),
            Background = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)),
            BorderBrush = Brushes.DeepSkyBlue,
            BorderThickness = new Thickness(1),
            FontSize = 14 + _width * 3,
            FontWeight = FontWeights.SemiBold,
            AcceptsReturn = true,
            MinWidth = 60,
            Padding = new Thickness(2),
        };
        Canvas.SetLeft(box, at.X);
        Canvas.SetTop(box, at.Y);
        box.PreviewKeyDown += (_, ke) =>
        {
            if (ke.Key == Key.Escape)
            {
                ke.Handled = true;
                _canvas.Children.Remove(box);
                _editing = null;
            }
            else if (ke.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                ke.Handled = true;
                CommitText();
            }
        };
        _editing = box;
        _canvas.Children.Add(box);
        box.Dispatcher.BeginInvoke(() => box.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void CommitText()
    {
        var box = _editing;
        if (box is null)
        {
            return;
        }

        _editing = null;
        _canvas.Children.Remove(box);
        if (string.IsNullOrWhiteSpace(box.Text))
        {
            return;
        }

        var block = new TextBlock
        {
            Text = box.Text,
            Foreground = box.Foreground,
            FontSize = box.FontSize,
            FontWeight = FontWeights.SemiBold,
            Padding = box.Padding,
        };
        // Light outline keeps text readable on any background.
        block.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = 0.8, Color = Colors.Black };
        Canvas.SetLeft(block, Canvas.GetLeft(box));
        Canvas.SetTop(block, Canvas.GetTop(box));
        _canvas.Children.Add(block);
        _undo.Add(() => _canvas.Children.Remove(block));
    }

    // ---- Pixelate and crop ----------------------------------------------------------------

    private void ApplyPixelate(Rect area)
    {
        var x = (int)Math.Max(0, area.X);
        var y = (int)Math.Max(0, area.Y);
        var w = (int)Math.Min(_base.PixelWidth - x, area.Width);
        var h = (int)Math.Min(_base.PixelHeight - y, area.Height);
        if (w < 4 || h < 4)
        {
            return;
        }

        var converted = new FormatConvertedBitmap(_base, PixelFormats.Bgra32, null, 0);
        var src = new byte[w * h * 4];
        converted.CopyPixels(new Int32Rect(x, y, w, h), src, w * 4, 0);
        var block = Math.Max(6, Math.Min(w, h) / 10);
        var dst = new byte[src.Length];
        for (var by = 0; by < h; by += block)
        {
            for (var bx = 0; bx < w; bx += block)
            {
                long b = 0, g = 0, r = 0, count = 0;
                var ey = Math.Min(by + block, h);
                var ex = Math.Min(bx + block, w);
                for (var yy = by; yy < ey; yy++)
                {
                    for (var xx = bx; xx < ex; xx++)
                    {
                        var i = (yy * w + xx) * 4;
                        b += src[i];
                        g += src[i + 1];
                        r += src[i + 2];
                        count++;
                    }
                }

                for (var yy = by; yy < ey; yy++)
                {
                    for (var xx = bx; xx < ex; xx++)
                    {
                        var i = (yy * w + xx) * 4;
                        dst[i] = (byte)(b / count);
                        dst[i + 1] = (byte)(g / count);
                        dst[i + 2] = (byte)(r / count);
                        dst[i + 3] = 255;
                    }
                }
            }
        }

        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, dst, w * 4);
        bitmap.Freeze();
        var tile = new Image { Source = bitmap, Width = w, Height = h, Stretch = Stretch.Fill };
        Canvas.SetLeft(tile, x);
        Canvas.SetTop(tile, y);
        _canvas.Children.Add(tile);
        _undo.Add(() => _canvas.Children.Remove(tile));
    }

    private void ApplyCrop(Rect area)
    {
        var x = (int)Math.Max(0, area.X);
        var y = (int)Math.Max(0, area.Y);
        var w = (int)Math.Min(_base.PixelWidth - x, area.Width);
        var h = (int)Math.Min(_base.PixelHeight - y, area.Height);
        if (w < 8 || h < 8)
        {
            return;
        }

        var flat = Render();
        var cropped = new CroppedBitmap(flat, new Int32Rect(x, y, w, h));
        cropped.Freeze();

        var oldBase = _base;
        var oldChildren = _canvas.Children.Cast<UIElement>().ToList();
        _canvas.Children.Clear();
        _base = cropped;
        ApplyBase();
        _undo.Add(() =>
        {
            _base = oldBase;
            ApplyBase();
            _canvas.Children.Clear();
            foreach (var child in oldChildren)
            {
                _canvas.Children.Add(child);
            }
        });
    }

    // ---- Output --------------------------------------------------------------------------

    private BitmapSource Render()
    {
        CommitText();
        var w = _base.PixelWidth;
        var h = _base.PixelHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new VisualBrush(_stage) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, w, h));
        }

        var target = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    private void Copy()
    {
        try
        {
            BitmapUtil.CopyToClipboard(Render());
            _feature.Tell("Screenshot copied to the clipboard.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Copy", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveDefault()
    {
        try
        {
            var path = _feature.SaveToFolder(Render());
            _feature.Tell("Saved to " + path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveAs()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "PNG image|*.png|JPEG image|*.jpg",
            FileName = $"WinKnife-{DateTime.Now:yyyyMMdd-HHmmss}",
            InitialDirectory = Directory.Exists(_feature.SaveFolder()) ? _feature.SaveFolder() : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var image = Render();
            if (dialog.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                var encoder = new JpegBitmapEncoder { QualityLevel = 92 };
                encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(image, PixelFormats.Bgr32, null, 0)));
                using var stream = File.Create(dialog.FileName);
                encoder.Save(stream);
            }
            else
            {
                BitmapUtil.SavePng(image, dialog.FileName);
            }

            _feature.Tell("Saved to " + dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Undo()
    {
        if (_editing is not null)
        {
            _canvas.Children.Remove(_editing);
            _editing = null;
            return;
        }

        if (_undo.Count == 0)
        {
            return;
        }

        var last = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        last();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_editing is not null && Keyboard.FocusedElement == _editing)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
        else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            Undo();
        }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            Copy();
        }
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            SaveDefault();
        }
    }
}
