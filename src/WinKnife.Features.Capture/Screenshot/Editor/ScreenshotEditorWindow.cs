// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Capture;

/// <summary>The annotation editor window: tool rail, canvas with zoom, contextual style bar, action cluster and footer chips.</summary>
internal sealed partial class ScreenshotEditorWindow : Window
{
    private readonly ScreenshotService _svc;
    private readonly IAppSettings _app;
    private readonly EditorDocument _doc;
    private readonly Capture _capture;
    private ToolId _tool;
    private string _color;
    private string _stroke;
    private int _textSize;
    private string _arrowStyle;
    private string _sticker;
    private string _blurStyle;
    private int _blurLevel;
    private bool _blurTextOnly;
    private Annotation? _selected;
    private double _zoom = 1;
    private bool _autoFit = true;
    private bool _exported;
    private bool _forceClose;
    private QrReading? _qr;
    private List<OcrLineInfo>? _ocrLines;
    private readonly HashSet<int> _selectedWords = new();

    private readonly Grid _stage = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _backdropPlate = new();
    private readonly Border _cardHost = new();
    private readonly Image _image = new() { Stretch = Stretch.Fill };
    private readonly OverlayCanvas _overlay = new();
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
    private readonly StackPanel _rail = new() { Orientation = Orientation.Vertical };
    private readonly WrapPanel _bar = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _infoText = new();
    private readonly TextBlock _zoomText = new();
    private readonly Dictionary<ToolId, EdButton> _railButtons = new();
    private EdButton _undoBtn = null!;
    private EdButton _redoBtn = null!;
    private EdButton? _qrBtn;
    private StackPanel _cluster = null!;
    private EdButton _fitBtn = null!;
    private EdButton _oneBtn = null!;
    private readonly DispatcherTimer _rerender = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private bool _dirtyRender;

    public ScreenshotEditorWindow(ScreenshotService svc, Capture capture)
    {
        _svc = svc;
        _app = svc.App;
        _capture = capture;
        var image = capture.Image.Format == PixelFormats.Pbgra32 ? capture.Image : new FormatConvertedBitmap(capture.Image, PixelFormats.Pbgra32, null, 0);
        _doc = new EditorDocument(image, capture.Scale)
        {
            Backdrop = BackdropStyle.Parse(_app.Get<string>(K.BackdropStyle)),
            Watermark = WatermarkStyle.Parse(_app.Get<string>(K.WatermarkStyle)),
            Shadows = _app.Get<bool>(K.Shadows),
            SpotlightDim = _app.Get<double>(K.SpotlightDim),
        };
        _color = _app.Get<string>(K.LastColor) ?? "red";
        _stroke = _app.Get<string>(K.LastStroke) ?? "medium";
        _textSize = Math.Clamp(_app.Get<int>(K.LastTextSize), 8, 200);
        _arrowStyle = _app.Get<string>(K.LastArrowStyle) ?? "filled";
        _sticker = _app.Get<string>(K.LastSticker) ?? "check";
        _blurStyle = _app.Get<string>(K.LastBlurStyle) ?? "pixelate";
        _blurLevel = Math.Max(_app.Get<int>(K.LastBlurLevel), 3);
        _blurTextOnly = _app.Get<bool>(K.LastBlurTextOnly);
        var last = ToolCatalog.ByRaw(_app.Get<string>(K.LastTool) ?? "arrow")?.Id ?? ToolId.Arrow;
        _tool = last is ToolId.Select or ToolId.Crop or ToolId.ExpandCanvas or ToolId.Image ? ToolId.Arrow : last;

        Title = Strings.Screenshot.PageTitle;
        Background = Ed.SurfaceBrush;
        Foreground = Ed.Fg;
        FontFamily = Ed.Text;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        SourceInitialized += (_, _) => Ed.DarkTitle(this);
        ComputeInitialSize();
        BuildLayout();
        _svc.ApplyAffinity(this);
        _rerender.Tick += (_, _) =>
        {
            _rerender.Stop();
            if (_dirtyRender)
            {
                _dirtyRender = false;
                RefreshImage();
            }
        };
        Loaded += (_, _) =>
        {
            FitToWindow();
            RefreshAll();
            _ = AnalyzeAsync();
        };
        PreviewKeyDown += OnKeyDown;
        PreviewMouseWheel += OnWheel;
        Closing += OnClosing;
        SizeChanged += (_, _) =>
        {
            if (_autoFit)
            {
                FitToWindow();
            }
        };
    }

    private void ComputeInitialSize()
    {
        var work = SystemParameters.WorkArea;
        var vw = work.Width;
        var vh = work.Height;
        var minW = Math.Min(vw, Math.Min(980, Math.Max(760, vw * 0.76)));
        var minH = Math.Min(vh, Math.Min(680, Math.Max(560, vh * 0.72)));
        var imgW = _doc.PixelWidth / (_doc.Scale <= 0 ? 1 : _doc.Scale);
        var imgH = _doc.PixelHeight / (_doc.Scale <= 0 ? 1 : _doc.Scale);
        var w = Math.Clamp(Math.Min(imgW, imgW) + 96 + 20, minW, Math.Max(minW, vw * 0.9));
        var h = Math.Clamp(imgH + 140 + 40, minH, Math.Max(minH, vh * 0.88));
        MinWidth = minW;
        MinHeight = minH;
        Width = w;
        Height = h;
    }

    // ---- layout ------------------------------------------------------------------------------------------------

    private void BuildLayout()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Background = new LinearGradientBrush(Color.FromArgb(9, 255, 255, 255), Colors.Transparent, 90);

        // top band
        var top = new Grid { Margin = new Thickness(12, 10, 12, 0), Height = 38 };
        var brand = BrandMark.Create(40);
        brand.HorizontalAlignment = HorizontalAlignment.Center;
        brand.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(brand);
        _cluster = new StackPanel { Orientation = Orientation.Horizontal };
        BuildCluster();
        var capsule = Ed.Capsule(_cluster, 12, new Thickness(9, 5, 9, 5));
        capsule.HorizontalAlignment = HorizontalAlignment.Right;
        capsule.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(capsule);
        Grid.SetRow(top, 0);
        root.Children.Add(top);

        // middle
        var mid = new Grid { Margin = new Thickness(10, 6, 0, 0) };
        mid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        mid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var railHost = BuildRail();
        railHost.VerticalAlignment = VerticalAlignment.Top;
        mid.Children.Add(railHost);
        BuildStage();
        Grid.SetColumn(_scroll, 1);
        mid.Children.Add(_scroll);
        Grid.SetRow(mid, 1);
        root.Children.Add(mid);

        // footer
        var footer = new Grid { Margin = new Thickness(12, 6, 12, 10) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(BuildInfoChip());
        var barHost = Ed.Capsule(_bar, 12, new Thickness(12, 8, 12, 8));
        barHost.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(barHost, 1);
        footer.Children.Add(barHost);
        var zoom = BuildZoomChip();
        Grid.SetColumn(zoom, 2);
        footer.Children.Add(zoom);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        Content = root;
    }

    private void BuildCluster()
    {
        _cluster.Children.Clear();
        void Add(UIElement e, bool divider = true)
        {
            if (divider && _cluster.Children.Count > 0)
            {
                _cluster.Children.Add(Ed.Divider());
            }

            _cluster.Children.Add(e);
        }

        Add(new EdButton(G.History, null, Strings.Screenshot.RecentCaptures, () => RecentCapturesWindow.ShowPalette(), width: 24));
        Add(new EdButton(G.Trash, null, Strings.Screenshot.Discard, () => { _forceClose = true; Close(); }, width: 24));
        _undoBtn = new EdButton(G.Undo, null, "Undo", Undo, width: 24);
        _redoBtn = new EdButton(G.Redo, null, "Redo", Redo, width: 24);
        var ur = new StackPanel { Orientation = Orientation.Horizontal };
        ur.Children.Add(_undoBtn);
        ur.Children.Add(_redoBtn);
        Add(ur);
        if (_qr is not null)
        {
            _qrBtn = new EdButton(G.QrCode, null, Strings.Screenshot.QrCode, () => QrResultWindow.Show(_qr, this), width: 24) { Active = true };
            Add(_qrBtn);
        }

        Add(new EdButton(G.Pin, null, "Pin to screen  (Ctrl+P)", PinCurrent, width: 24));
        Add(new EdButton(G.Share, null, "Share", ShareCurrent, width: 24));
        var save = BuildSaveButton();
        Add(save);
        var copy = new EdButton(null, Strings.Screenshot.CopyLabel, "Enter", CopyAndClose, prominent: true, height: 26);
        copy.Margin = new Thickness(8, 0, 0, 0);
        _cluster.Children.Add(copy);
        UpdateUndoState();
    }

    private UIElement BuildSaveButton()
    {
        var host = new StackPanel { Orientation = Orientation.Horizontal };
        var main = new EdButton(null, Strings.Screenshot.SaveLabel, "Ctrl+S", SaveAndClose, height: 26);
        var more = new EdButton(G.ChevronDown, null, null, null, iconSize: 8, width: 18, height: 26);
        more.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            var menu = new ContextMenu();
            var save = new MenuItem { Header = Strings.Screenshot.SaveLabel, InputGestureText = "Ctrl+S" };
            save.Click += (_, _) => SaveAndClose();
            var saveAs = new MenuItem { Header = Strings.Screenshot.SaveAs, InputGestureText = "Ctrl+Shift+S" };
            saveAs.Click += (_, _) => SaveAs();
            menu.Items.Add(save);
            menu.Items.Add(saveAs);
            menu.PlacementTarget = more;
            menu.IsOpen = true;
        };
        host.Children.Add(main);
        host.Children.Add(more);
        return host;
    }

    private Border BuildRail()
    {
        _rail.Children.Clear();
        _railButtons.Clear();
        var order = ToolCatalog.Order(_app);
        for (var i = 0; i < order.Count; i++)
        {
            var tool = order[i];
            var badge = ToolCatalog.BadgeFor(_app, tool, i);
            var btn = new EdButton(tool.Glyph, null, badge.Length > 0 ? $"{tool.Title}  ({badge})" : tool.Title, () => SelectTool(tool.Id), iconSize: 13.5, width: 33, height: 29)
            {
                Margin = new Thickness(0, 1, 0, 1), Active = tool.Id == _tool,
            };
            var holder = new Grid();
            holder.Children.Add(btn);
            if (badge.Length > 0)
            {
                var b = Ed.Label(badge, 8, FontWeights.Bold, tool.Id == _tool ? Ed.Accent : Ed.FgDim);
                b.HorizontalAlignment = HorizontalAlignment.Right;
                b.VerticalAlignment = VerticalAlignment.Top;
                b.Margin = new Thickness(0, 2, 3, 0);
                b.IsHitTestVisible = false;
                b.Opacity = 0.7;
                holder.Children.Add(b);
            }

            _railButtons[tool.Id] = btn;
            _rail.Children.Add(holder);
        }

        _rail.Children.Add(new Border { Height = 1, Width = 22, Background = Ed.PlateStroke, Margin = new Thickness(0, 4, 0, 4) });
        var gear = new EdButton(G.Gear, null, Strings.Screenshot.EditorTools, null, iconSize: 13, width: 33, height: 29);
        gear.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            var pop = new Popup { PlacementTarget = gear, Placement = PlacementMode.Right, StaysOpen = false, AllowsTransparency = true };
            var panel = new ToolOrderPanel(_app, dark: true, headline: true) { Width = 340 };
            panel.Changed += () => Dispatcher.BeginInvoke(RebuildRail);
            pop.Child = new Border { Background = Ed.Plate, BorderBrush = Ed.PlateStroke, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(14), Child = panel, Margin = new Thickness(8) };
            pop.IsOpen = true;
        };
        _rail.Children.Add(gear);
        var host = Ed.Capsule(_rail, 15, new Thickness(5));
        _railHost = host;
        return host;
    }

    private Border? _railHost;

    private void RebuildRail()
    {
        var old = _railHost;
        var parent = (Grid)old!.Parent;
        parent.Children.Remove(old);
        var host = BuildRail();
        host.VerticalAlignment = VerticalAlignment.Top;
        parent.Children.Insert(0, host);
    }

    private Border BuildInfoChip()
    {
        var drag = new EdButton("", null, Strings.Screenshot.DragAndDrop, null, iconSize: 11, width: 26, height: 18) { Cursor = Cursors.SizeAll };
        drag.MouseMove += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragOutImage(drag);
            }
        };
        _infoText.FontSize = 11;
        _infoText.FontWeight = FontWeights.Medium;
        _infoText.Foreground = Ed.FgDim;
        _infoText.VerticalAlignment = VerticalAlignment.Center;
        _infoText.Margin = new Thickness(4, 0, 0, 0);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(drag);
        row.Children.Add(_infoText);
        var chip = Ed.Capsule(row, 12, new Thickness(7, 4, 10, 4));
        chip.VerticalAlignment = VerticalAlignment.Center;
        return chip;
    }

    private Border BuildZoomChip()
    {
        _fitBtn = new EdButton("", null, "Ctrl+0", () => { _autoFit = true; FitToWindow(); }, iconSize: 11, width: 24, height: 22);
        _zoomText.FontSize = 10.5;
        _zoomText.Foreground = Ed.FgDim;
        _zoomText.VerticalAlignment = VerticalAlignment.Center;
        _zoomText.Margin = new Thickness(6, 0, 6, 0);
        _oneBtn = new EdButton(null, "1:1", "Ctrl+1", () => SetZoom(1.0 / _doc.Scale, false), width: 28, height: 22);
        if (_oneBtn.Label is { } l)
        {
            l.FontSize = 10.5;
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_fitBtn);
        row.Children.Add(_zoomText);
        row.Children.Add(_oneBtn);
        var chip = Ed.Capsule(row, 9, new Thickness(4));
        chip.ToolTip = "Ctrl+Wheel · Ctrl+Plus Ctrl+Minus";
        chip.VerticalAlignment = VerticalAlignment.Center;
        return chip;
    }

    private void BuildStage()
    {
        _backdropPlate.CornerRadius = new CornerRadius(6);
        _cardHost.Child = _image;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        _stage.Children.Add(_backdropPlate);
        _stage.Children.Add(_cardHost);
        _stage.Children.Add(_overlay);
        _overlay.Draw = DrawOverlay;
        _overlay.MouseLeftButtonDown += OverlayDown;
        _overlay.MouseMove += OverlayMove;
        _overlay.MouseLeftButtonUp += OverlayUp;
        _overlay.LostMouseCapture += (_, _) => AbortDrag();
        _scroll.Content = _stage;
        _scroll.Margin = new Thickness(6, 0, 6, 0);
        _scroll.Background = Brushes.Transparent;
        _scroll.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is ScrollViewer or Grid && _editingText is not null)
            {
                CommitText();
            }
        };
    }

    // ---- refresh ------------------------------------------------------------------------------------------------

    private void RefreshAll()
    {
        RefreshImage();
        RebuildBar();
        UpdateUndoState();
        UpdateZoomUi();
    }

    private void RequestRender()
    {
        _dirtyRender = true;
        if (!_rerender.IsEnabled)
        {
            _rerender.Start();
        }
    }

    private void RefreshImage()
    {
        var skip = _editingText?.Annotation;
        var flat = EditorRenderer.RenderFlat(_doc, skip);
        _image.Source = flat;
        var w = _doc.PixelWidth;
        var h = _doc.PixelHeight;
        var corner = EditorRenderer.CardCorner(_doc, flat);
        var hasBackdrop = !_doc.Backdrop.IsNone && _doc.Backdrop.CreateBrush(w, h) is not null;
        var m = hasBackdrop ? EditorRenderer.BackdropMargin(_doc.Backdrop, w, h) : 0;
        _image.Width = w;
        _image.Height = h;
        _cardHost.Width = w;
        _cardHost.Height = h;
        _cardHost.Margin = new Thickness(m);
        _cardHost.HorizontalAlignment = HorizontalAlignment.Left;
        _cardHost.VerticalAlignment = VerticalAlignment.Top;
        _cardHost.Clip = corner > 0 ? new RectangleGeometry(new Rect(0, 0, w, h), corner, corner) : null;
        _backdropPlate.Width = w + 2 * m;
        _backdropPlate.Height = h + 2 * m;
        _backdropPlate.HorizontalAlignment = HorizontalAlignment.Left;
        _backdropPlate.VerticalAlignment = VerticalAlignment.Top;
        _backdropPlate.Background = hasBackdrop ? _doc.Backdrop.CreateBrush(w, h) : CheckerBrush();
        _backdropPlate.Effect = null;
        _backdropPlate.Margin = new Thickness(0);
        if (!hasBackdrop)
        {
            _backdropPlate.Width = w;
            _backdropPlate.Height = h;
        }

        _overlay.Width = w;
        _overlay.Height = h;
        _overlay.Margin = new Thickness(m);
        _overlay.HorizontalAlignment = HorizontalAlignment.Left;
        _overlay.VerticalAlignment = VerticalAlignment.Top;
        _stage.Width = w + 2 * m;
        _stage.Height = h + 2 * m;
        _stage.LayoutTransform = new ScaleTransform(_zoom, _zoom);
        RenderOptions.SetBitmapScalingMode(_image, _zoom * (VisualTreeHelper.GetDpi(this).DpiScaleX) >= 2 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        _infoText.Text = $"{w} × {h} px" + (_doc.Scale > 1.001 ? $"  @{_doc.Scale.ToString("0.##", CultureInfo.InvariantCulture)}x" : string.Empty);
        _overlay.InvalidateVisual();
        UpdateZoomUi();
    }

    private static Brush? _checker;

    private static Brush CheckerBrush()
    {
        if (_checker is not null)
        {
            return _checker;
        }

        var dg = new DrawingGroup();
        dg.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(179, 179, 179)), null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        dg.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(230, 230, 230)), null, new GeometryGroup { Children = { new RectangleGeometry(new Rect(0, 0, 8, 8)), new RectangleGeometry(new Rect(8, 8, 8, 8)) } }));
        var brush = new DrawingBrush(dg) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 16, 16), ViewboxUnits = BrushMappingMode.Absolute };
        brush.Freeze();
        _checker = brush;
        return brush;
    }

    private void UpdateUndoState()
    {
        _undoBtn.IsEnabled = _doc.CanUndo;
        _redoBtn.IsEnabled = _doc.CanRedo;
    }

    private void MarkChanged()
    {
        _doc.Dirty = true;
        RequestRender();
        UpdateUndoState();
    }

    // ---- zoom -------------------------------------------------------------------------------------------------------

    private (double W, double H) StageSize()
    {
        var hasBackdrop = !_doc.Backdrop.IsNone && _doc.Backdrop.CreateBrush(_doc.PixelWidth, _doc.PixelHeight) is not null;
        var m = hasBackdrop ? EditorRenderer.BackdropMargin(_doc.Backdrop, _doc.PixelWidth, _doc.PixelHeight) : 0;
        return (_doc.PixelWidth + 2 * m, _doc.PixelHeight + 2 * m);
    }

    private void FitToWindow()
    {
        var (sw, sh) = StageSize();
        var margin = _tool == ToolId.ExpandCanvas ? 64 : 14;
        var availW = Math.Max(50, _scroll.ActualWidth - 2 * margin);
        var availH = Math.Max(50, _scroll.ActualHeight - 2 * margin);
        _zoom = Math.Clamp(Math.Min(Math.Min(availW / sw, availH / sh), 1), 0.05, 3);
        _autoFit = true;
        RefreshImageLayoutOnly();
    }

    private void SetZoom(double zoom, bool keepFit)
    {
        _zoom = Math.Clamp(zoom, 0.05, 3);
        _autoFit = keepFit;
        RefreshImageLayoutOnly();
    }

    private void RefreshImageLayoutOnly()
    {
        _stage.LayoutTransform = new ScaleTransform(_zoom, _zoom);
        RenderOptions.SetBitmapScalingMode(_image, _zoom * VisualTreeHelper.GetDpi(this).DpiScaleX >= 2 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        UpdateZoomUi();
        _overlay.InvalidateVisual();
    }

    private void UpdateZoomUi()
    {
        _zoomText.Text = $"{Math.Round(_zoom * _doc.Scale * 100)}%";
        _fitBtn.Active = _autoFit;
        _oneBtn.Active = Math.Abs(_zoom - 1.0 / _doc.Scale) < 0.0005;
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }

        var delta = Math.Clamp(e.Delta / 120.0 * 24, -24, 24);
        SetZoom(_zoom * (1 + delta * 0.014 * 2), false);
        e.Handled = true;
    }

    // ---- output actions ---------------------------------------------------------------------------------------------------

    private (BitmapSource Image, double Scale) ExportImage(bool withBackdrop = true)
    {
        var img = EditorRenderer.RenderExport(_doc, withBackdrop);
        return Output.ApplyDownscale(_app, img, _doc.Scale);
    }

    private void CopyAndClose()
    {
        CommitText();
        var (img, scale) = ExportImage();
        if (Output.CopyImage(img, scale, _app.Get<bool>(K.CopyImageOnly)))
        {
            QuickToolHud.Show(G.Camera, Strings.Screenshot.Copied);
            Finish();
        }
        else
        {
            CaptureHost.Beep();
        }
    }

    private void SaveAndClose()
    {
        CommitText();
        try
        {
            var (path, _, _) = Output.NextScreenshotPath(_app, DateTime.Now);
            var (img, scale) = ExportImage();
            Output.SavePng(img, path, scale);
            QuickToolHud.Show(G.Camera, string.Format(Strings.Screenshot.SavedFormat, Path.GetFileName(Path.GetDirectoryName(path)) ?? Strings.Desktop));
            Finish();
        }
        catch (Exception ex)
        {
            CaptureHost.Log("editor save failed", ex);
            CaptureHost.Beep();
        }
    }

    private void SaveAs()
    {
        CommitText();
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG image|*.png",
            FileName = Output.DefaultName(Strings.Screenshot.FileNamePrefix, DateTime.Now) + ".png",
            InitialDirectory = Output.BaseFolder(_app.Get<string>(K.SaveFolder)),
        };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var (img, scale) = ExportImage();
            Output.SavePng(img, dlg.FileName, scale);
            QuickToolHud.Show(G.Camera, string.Format(Strings.Screenshot.SavedFormat, Path.GetFileName(Path.GetDirectoryName(dlg.FileName)) ?? string.Empty));
            Finish();
        }
        catch
        {
            CaptureHost.Beep();
        }
    }

    private void PinCurrent()
    {
        CommitText();
        var (img, scale) = ExportImage(withBackdrop: false);
        _svc.Pin(img, scale);
        _exported = true;
        _doc.Dirty = false;
    }

    private void ShareCurrent()
    {
        CommitText();
        try
        {
            var (img, scale) = ExportImage();
            var dir = Path.Combine(Path.GetTempPath(), "WinKnife", "ScreenshotShare-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, Output.DefaultName(Strings.Screenshot.FileNamePrefix, DateTime.Now) + ".png");
            Output.SavePng(img, file, scale);
            if (ShareHelper.Share(new WindowInteropHelper(this).Handle, file, Strings.Screenshot.PageTitle))
            {
                _exported = true;
                _doc.Dirty = false;
            }
        }
        catch (Exception ex)
        {
            CaptureHost.Log("share failed", ex);
            CaptureHost.Beep();
        }
    }

    private void DragOutImage(UIElement source)
    {
        try
        {
            var (img, scale) = ExportImage();
            var dir = Path.Combine(Path.GetTempPath(), "WinKnife", "ScreenshotDrag-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, Output.DefaultName(Strings.Screenshot.FileNamePrefix, DateTime.Now) + ".png");
            Output.SavePng(img, file, scale);
            var data = new DataObject();
            data.SetFileDropList(new System.Collections.Specialized.StringCollection { file });
            data.SetImage(img);
            _exported = true;
            _doc.Dirty = false;
            DragDrop.DoDragDrop(source, data, DragDropEffects.Copy);
        }
        catch
        {
            // drag cancelled
        }
    }

    private void Finish()
    {
        _exported = true;
        _doc.Dirty = false;
        _forceClose = true;
        Close();
    }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_forceClose || !_doc.Dirty || _exported)
        {
            return;
        }

        var result = MessageBox.Show(this, Strings.Screenshot.DiscardMessage, Strings.Screenshot.DiscardTitle, MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (result != MessageBoxResult.OK)
        {
            e.Cancel = true;
        }
    }

    // ---- undo ---------------------------------------------------------------------------------------------------------------

    private void Undo()
    {
        CommitText();
        if (_doc.Undo())
        {
            _selected = null;
            RefreshAll();
        }
    }

    private void Redo()
    {
        CommitText();
        if (_doc.Redo())
        {
            _selected = null;
            RefreshAll();
        }
    }

    // ---- analysis (OCR words, QR) ----------------------------------------------------------------------------------------------

    private async Task AnalyzeAsync()
    {
        var image = _doc.Image;
        try
        {
            var qr = await Task.Run(() => OcrSupport.ReadQr(image));
            if (qr is not null && IsLoaded)
            {
                _qr = qr;
                BuildCluster();
            }
        }
        catch
        {
            // QR is optional
        }

        try
        {
            var lines = await OcrSupport.RecognizeAsync(image);
            if (lines is not null && ReferenceEquals(image, _doc.Image))
            {
                _ocrLines = lines;
                _doc.TextRuns = BuildRuns(lines);
            }
        }
        catch
        {
            // OCR is optional
        }
    }

    private static List<Rect> BuildRuns(List<OcrLineInfo> lines)
    {
        var runs = new List<Rect>();
        foreach (var line in lines)
        {
            Rect? current = null;
            var lastRight = 0.0;
            foreach (var w in line.Words.OrderBy(w => w.Box.X))
            {
                var padX = 0.6 * w.Box.Height;
                var padY = 0.3 * w.Box.Height;
                var box = new Rect(w.Box.X - padX, w.Box.Y - padY, w.Box.Width + 2 * padX, w.Box.Height + 2 * padY);
                if (current is { } c && w.Box.X - lastRight <= 1.5 * w.Box.Height)
                {
                    c.Union(box);
                    current = c;
                }
                else
                {
                    if (current is { } done)
                    {
                        runs.Add(done);
                    }

                    current = box;
                }

                lastRight = w.Box.Right;
            }

            if (current is { } last)
            {
                runs.Add(last);
            }
        }

        return runs;
    }
}

/// <summary>A canvas that draws its chrome through a delegate (selection boxes, crop frame, grips).</summary>
internal sealed class OverlayCanvas : Canvas
{
    public Action<DrawingContext>? Draw { get; set; }

    public OverlayCanvas()
    {
        Background = Brushes.Transparent;
        Cursor = Cursors.Cross;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        Draw?.Invoke(dc);
    }
}
