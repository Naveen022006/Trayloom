// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

/// <summary>Strings of the camera preview (Vorssaint <c>CameraPreviewFeatureStrings</c>, enUS, Windows wording).</summary>
internal static class CameraStrings
{
    public static  string Title => Loc.T("Camera preview");
    public static  string PanelCaption => Loc.T("Check how you look before a call");
    public static  string OpenButton => Loc.T("Open preview");
    public static  string CameraMenuLabel => Loc.T("Camera");
    public static  string DeniedMessage => Loc.T("Camera access for WinKnife is turned off in Settings.");
    public static  string NoCamera => Loc.T("No camera detected");
    public static  string Unavailable => Loc.T("The camera could not start. Try opening it again.");
    public static  string StartCamera => Loc.T("Open camera");
    public static  string OpenSettings => Loc.T("Open Settings…");
    public static  string PermissionRequired => Loc.T("Permission required: Camera");
    public static  string NotGranted => Loc.T("Not granted");
}

/// <summary>State of the mirror (Vorssaint <c>CameraPreviewService.State</c>).</summary>
internal enum CameraState
{
    Idle,
    WaitingPermission,
    Starting,
    Running,
    Denied,
    Unavailable,
    NoCamera,
}

/// <summary>Camera helpers shared by the feature, the settings card and the panel row.</summary>
internal static class CameraAccess
{
    /// <summary>True when Windows denies camera access to desktop apps.</summary>
    public static bool IsDenied()
    {
        try
        {
            var status = DeviceAccessInformation.CreateFromDeviceClass(DeviceClass.VideoCapture).CurrentStatus;
            return status is DeviceAccessStatus.DeniedByUser or DeviceAccessStatus.DeniedBySystem;
        }
        catch
        {
            return false;
        }
    }

    public static void OpenPrivacySettings() => Plat.Open("ms-settings:privacy-webcam");
}

/// <summary>The floating 320x240 mirror (view only: no capture code).</summary>
internal sealed class CameraPreviewWindow : Window
{
    public const double CardWidth = 320;
    public const double CardHeight = 240;
    public const double Pad = 12;

    private readonly Border _card = new();
    private readonly Grid _content = new();
    private readonly Image _image = new() { Stretch = Stretch.UniformToFill, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(-1, 1) };
    private readonly Border _picker = new();
    private readonly TextBlock _pickerName = new();
    private readonly Grid _pickerContent = new();
    private WriteableBitmap? _bitmap;
    private IReadOnlyList<(string Id, string Name)> _cameras = [];
    private string? _selected;
    private CameraState _state = CameraState.Idle;
    private bool _hover;

    public CameraPreviewWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Title = CameraStrings.Title;
        UseLayoutRounding = true;
        Width = CardWidth + Pad * 2;
        Height = CardHeight + Pad * 2;
        _card.Width = CardWidth;
        _card.Height = CardHeight;
        _card.CornerRadius = new CornerRadius(14);
        _card.Background = Brushes.Black;
        _card.BorderThickness = new Thickness(1);
        _card.BorderBrush = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255));
        _card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Direction = 270, Opacity = 0.4 };
        _card.Child = _content;
        _card.SizeChanged += (_, e) => _content.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 13, 13);
        var outer = new Grid { Margin = new Thickness(Pad) };
        outer.Children.Add(_card);
        Content = outer;
        BuildPicker();
        _card.MouseEnter += (_, _) => SetHover(true);
        _card.MouseLeave += (_, _) => SetHover(false);
        MouseLeftButtonDown += (_, _) =>
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // released during the call
            }
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                EscapePressed?.Invoke();
            }
        };
        Render();
    }

    public event Action? EscapePressed;

    public event Action<string>? CameraPicked;

    public event Action? OpenSettingsRequested;

    public event Action? RetryRequested;

    public CameraState State => _state;

    /// <summary>Physical-pixel rectangle of the mirror card (outside clicks are measured against it).</summary>
    public Rect CardPhysicalBounds()
    {
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        return new Rect((Left + Pad) * dpi, (Top + Pad) * dpi, CardWidth * dpi, CardHeight * dpi);
    }

    public void SetState(CameraState state)
    {
        _state = state;
        Render();
    }

    public void SetCameras(IReadOnlyList<(string Id, string Name)> cameras, string? selected)
    {
        _cameras = cameras;
        _selected = selected;
        _pickerName.Text = cameras.FirstOrDefault(c => c.Id == selected).Name ?? string.Empty;
        UpdatePicker();
    }

    public void ShowFrame(byte[] bgra, int width, int height)
    {
        if (_bitmap is null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
        {
            _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null);
            _image.Source = _bitmap;
        }

        _bitmap.WritePixels(new Int32Rect(0, 0, width, height), bgra, width * 4, 0);
    }

    private void SetHover(bool hover)
    {
        _hover = hover;
        UpdatePicker();
    }

    private void BuildPicker()
    {
        _pickerName.FontSize = 11;
        _pickerName.Foreground = Brushes.White;
        _pickerName.TextTrimming = TextTrimming.CharacterEllipsis;
        _pickerName.MaxWidth = 190;
        _pickerName.Margin = new Thickness(6, 0, 6, 0);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = "", FontFamily = UiKit.IconFont, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(_pickerName);
        row.Children.Add(new TextBlock { Text = "", FontFamily = UiKit.IconFont, FontSize = 8, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
        _pickerContent.Children.Add(row);
        _picker.Child = _pickerContent;
        _picker.Background = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0));
        _picker.CornerRadius = new CornerRadius(100);
        _picker.Padding = new Thickness(10, 5, 10, 5);
        _picker.HorizontalAlignment = HorizontalAlignment.Center;
        _picker.VerticalAlignment = VerticalAlignment.Bottom;
        _picker.Margin = new Thickness(0, 0, 0, 10);
        _picker.Cursor = Cursors.Hand;
        _picker.Opacity = 0;
        _picker.IsHitTestVisible = false;
        _picker.ToolTip = CameraStrings.CameraMenuLabel;
        _picker.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            var menu = new ContextMenu { PlacementTarget = _picker, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
            foreach (var (id, name) in _cameras)
            {
                var item = new MenuItem { Header = name, IsChecked = id == _selected };
                var captured = id;
                item.Click += (_, _) => CameraPicked?.Invoke(captured);
                menu.Items.Add(item);
            }

            menu.IsOpen = true;
        };
    }

    private void UpdatePicker()
    {
        var show = _hover && _state == CameraState.Running && _cameras.Count > 1;
        _picker.IsHitTestVisible = show;
        _picker.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(150)));
        // The name is dropped when it does not fit: keep icon + chevron only.
        _pickerName.Visibility = _pickerName.Text.Length > 0 && CardWidth - 40 > 60 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Render()
    {
        _content.Children.Clear();
        switch (_state)
        {
            case CameraState.Running:
                _content.Children.Add(_image);
                _content.Children.Add(_picker);
                break;
            case CameraState.Denied:
                _content.Children.Add(StatusBlock("", CameraStrings.DeniedMessage, CameraStrings.OpenSettings, () => OpenSettingsRequested?.Invoke()));
                break;
            case CameraState.Unavailable:
                _content.Children.Add(StatusBlock("", CameraStrings.Unavailable, CameraStrings.StartCamera, () => RetryRequested?.Invoke()));
                break;
            case CameraState.NoCamera:
                _content.Children.Add(StatusBlock("", CameraStrings.NoCamera, null, null));
                break;
            default:
                _content.Children.Add(new CamSpinner { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                break;
        }

        UpdatePicker();
    }

    private static FrameworkElement StatusBlock(string glyph, string message, string? button, Action? click)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(28, 0, 28, 0) };
        stack.Children.Add(new TextBlock { Text = glyph, FontFamily = UiKit.IconFont, FontSize = 26, Foreground = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new TextBlock { Text = message, FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(204, 255, 255, 255)), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 10, 0, 0) });
        if (button is not null)
        {
            var b = new Button
            {
                Content = new TextBlock { Text = button, FontSize = 11, Foreground = Brushes.White },
                Margin = new Thickness(0, 10, 0, 0),
                Padding = new Thickness(12, 4, 12, 4),
                HorizontalAlignment = HorizontalAlignment.Center,
                Cursor = Cursors.Hand,
                Template = RoundedButton(),
            };
            b.Click += (_, _) => click?.Invoke();
            stack.Children.Add(b);
        }

        return stack;
    }

    private static ControlTemplate RoundedButton()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        return template;
    }
}

/// <summary>A small white spinning arc (the "ProgressView small" of the mirror).</summary>
internal sealed class CamSpinner : FrameworkElement
{
    private readonly RotateTransform _rotate = new();

    public CamSpinner()
    {
        Width = 22;
        Height = 22;
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = _rotate;
        Loaded += (_, _) => _rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
        Unloaded += (_, _) => _rotate.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var pen = new Pen(Brushes.White, 2.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(11, 1.5), false, false);
            ctx.ArcTo(new Point(20.5, 11), new Size(9.5, 9.5), 0, false, SweepDirection.Clockwise, true, false);
        }

        dc.DrawGeometry(null, pen, geometry);
    }
}

/// <summary>A floating mirror with your camera (Vorssaint camera preview): 320x240, mirrored, closes on outside interaction.</summary>
public sealed class CameraPreviewFeature : ToolsFeatureBase
{
    private CameraPreviewWindow? _window;
    private MediaCapture? _capture;
    private MediaFrameReader? _reader;
    private DeviceWatcher? _watcher;
    private OutsideClickMonitor? _outside;
    private ForegroundWatcher? _foreground;
    private List<(string Id, string Name)> _cameras = [];
    private string? _selected;
    private int _frameBusy;
    private int _generation;
    private DateTime _permissionResolvedAt = DateTime.MinValue;

    public override string Id => "camera-preview";

    public override IReadOnlyList<FeatureAction> Actions =>
        [new FeatureAction("show", CameraStrings.Title, () => Safe(Toggle), "")];

    protected override void RegisterDefaults(IAppSettings app)
    {
        app.Register("cameraPreviewShortcutEnabled", false);
        app.Register("cameraPreviewDeviceId", string.Empty);
        app.Register("panelUtilityCameraPreview", true);
    }

    protected override void OnStart()
    {
        PadServices.Ctx = Ctx;
        BindRole("cameraPreview", Toggle);
    }

    protected override void OnStop() => Hide();

    private void Toggle()
    {
        if (_window is { IsVisible: true })
        {
            Hide();
        }
        else
        {
            Show();
        }
    }

    private void Show()
    {
        if (!IsRunning)
        {
            return;
        }

        _window ??= CreateWindow();
        var window = _window;
        window.SetState(CameraState.Idle);
        window.SetCameras([], null);

        var area = Plat.Monitor().WorkArea;
        window.Opacity = 0;
        window.Show();
        var dpi = VisualTreeHelper.GetDpi(window).DpiScaleX;
        var width = window.Width * dpi;
        var height = window.Height * dpi;
        var x = Math.Clamp(area.Left + (area.Width - width) / 2, area.Left + 16, Math.Max(area.Left + 16, area.Right - width - 16));
        var y = Math.Max(area.Top + 16, area.Top + 48 - CameraPreviewWindow.Pad * dpi);
        Plat.PlaceWindow(window, x, y, 0, 0);
        window.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(130)));
        window.Activate();

        _outside = new OutsideClickMonitor(Ctx.Hooks, Ctx.Ui, window.CardPhysicalBounds, OnOutside);
        _foreground = new ForegroundWatcher(Ctx.Ui);
        _foreground.ForegroundChanged += OnForeground;
        _ = BeginCaptureAsync();
    }

    private CameraPreviewWindow CreateWindow()
    {
        var window = new CameraPreviewWindow();
        window.EscapePressed += Hide;
        window.OpenSettingsRequested += () =>
        {
            CameraAccess.OpenPrivacySettings();
            Hide();
        };
        window.RetryRequested += () => _ = BeginCaptureAsync();
        window.CameraPicked += id =>
        {
            App.Set("cameraPreviewDeviceId", id);
            _selected = id;
            _window?.SetCameras(_cameras, id);
            _ = StartSessionAsync(++_generation, id);
        };
        return window;
    }

    private void OnOutside()
    {
        if (_window?.State == CameraState.WaitingPermission)
        {
            return;
        }

        Hide();
    }

    private void OnForeground(nint hwnd)
    {
        if (_window is null || _window.State == CameraState.WaitingPermission)
        {
            return;
        }

        if (Plat.ProcessOf(hwnd) == Environment.ProcessId || DateTime.UtcNow - _permissionResolvedAt < TimeSpan.FromSeconds(1))
        {
            return;
        }

        Hide();
    }

    private void Hide()
    {
        _generation++;
        _outside?.Dispose();
        _outside = null;
        if (_foreground is not null)
        {
            _foreground.ForegroundChanged -= OnForeground;
            _foreground.Dispose();
            _foreground = null;
        }

        StopWatcher();
        StopCapture();
        if (_window is { } window)
        {
            window.SetState(CameraState.Idle);
            window.SetCameras([], null);
            window.Hide();
        }

        _cameras = [];
        _selected = null;
    }

    private async Task BeginCaptureAsync()
    {
        var generation = ++_generation;
        if (CameraAccess.IsDenied())
        {
            _window?.SetState(CameraState.Denied);
            return;
        }

        await StartSessionAsync(generation, null);
    }

    private async Task StartSessionAsync(int generation, string? forced)
    {
        var window = _window;
        if (window is null)
        {
            return;
        }

        StopCapture();
        window.SetState(CameraState.Starting);
        StartWatcher();
        try
        {
            _cameras = await ListCamerasAsync();
            if (generation != _generation)
            {
                return;
            }

            if (_cameras.Count == 0)
            {
                window.SetCameras([], null);
                window.SetState(CameraState.NoCamera);
                return;
            }

            var preferred = forced ?? App.Get<string>("cameraPreviewDeviceId");
            _selected = _cameras.Any(c => c.Id == preferred) ? preferred : _cameras[0].Id;
            window.SetCameras(_cameras, _selected);
            await StartCaptureAsync(_selected, generation);
            if (generation == _generation)
            {
                window.SetState(CameraState.Running);
            }
        }
        catch (UnauthorizedAccessException)
        {
            _permissionResolvedAt = DateTime.UtcNow;
            if (generation == _generation)
            {
                window.SetState(CameraState.Denied);
            }
        }
        catch (Exception ex)
        {
            Ctx.Log.Warn("Camera failed: " + ex.Message);
            StopCapture();
            if (generation == _generation)
            {
                window.SetState(CameraState.Unavailable);
            }
        }
    }

    private static async Task<List<(string Id, string Name)>> ListCamerasAsync()
    {
        var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
        return devices.Select(d => (d.Id, d.Name)).ToList();
    }

    private async Task StartCaptureAsync(string deviceId, int generation)
    {
        var capture = new MediaCapture();
        await capture.InitializeAsync(new MediaCaptureInitializationSettings
        {
            VideoDeviceId = deviceId,
            SharingMode = MediaCaptureSharingMode.SharedReadOnly,
            StreamingCaptureMode = StreamingCaptureMode.Video,
            MemoryPreference = MediaCaptureMemoryPreference.Cpu,
        });
        if (generation != _generation || _window is null)
        {
            capture.Dispose();
            return;
        }

        var source = capture.FrameSources.Values.FirstOrDefault(s => s.Info.SourceKind == MediaFrameSourceKind.Color)
            ?? throw new InvalidOperationException("No colour stream.");
        try
        {
            var best = source.SupportedFormats
                .OrderBy(f => Math.Abs((int)f.VideoFormat.Width - 640) + Math.Abs((int)f.VideoFormat.Height - 480) + Math.Abs(f.FrameRate.Numerator / Math.Max(1, f.FrameRate.Denominator) - 30) * 10)
                .FirstOrDefault();
            if (best is not null)
            {
                await source.SetFormatAsync(best);
            }
        }
        catch
        {
            // shared read-only sessions cannot always change the format
        }

        var reader = await capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
        reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
        reader.FrameArrived += OnFrameArrived;
        _capture = capture;
        _reader = reader;
        var status = await reader.StartAsync();
        if (status != MediaFrameReaderStartStatus.Success)
        {
            throw new InvalidOperationException("The camera is unavailable (" + status + ").");
        }
    }

    private void StartWatcher()
    {
        if (_watcher is not null)
        {
            return;
        }

        try
        {
            _watcher = DeviceInformation.CreateWatcher(DeviceClass.VideoCapture);
            void Changed(DeviceWatcher w, object o) => Ctx.Ui.BeginInvoke(() => _ = RefreshDevicesAsync());
            _watcher.Added += Changed;
            _watcher.Removed += Changed;
            _watcher.Start();
        }
        catch
        {
            _watcher = null;
        }
    }

    private void StopWatcher()
    {
        try
        {
            if (_watcher is { Status: DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted })
            {
                _watcher.Stop();
            }
        }
        catch
        {
            // already stopped
        }

        _watcher = null;
    }

    /// <summary>Hot-plug rules: a vanished camera switches to the first remaining one without remembering it; a new camera starts from "no camera".</summary>
    private async Task RefreshDevicesAsync()
    {
        var window = _window;
        if (window is null || !window.IsVisible)
        {
            return;
        }

        var list = await ListCamerasAsync();
        if (_window is null)
        {
            return;
        }

        var previous = _cameras;
        _cameras = list;
        window.SetCameras(list, _selected);
        switch (window.State)
        {
            case CameraState.Running or CameraState.Starting when _selected is not null && list.All(c => c.Id != _selected):
                if (list.Count == 0)
                {
                    StopCapture();
                    window.SetState(CameraState.NoCamera);
                }
                else
                {
                    _selected = list[0].Id;
                    window.SetCameras(list, _selected);
                    await StartSessionAsync(++_generation, _selected);
                }

                break;
            case CameraState.NoCamera when list.Count > 0 && previous.Count == 0:
                await StartSessionAsync(++_generation, null);
                break;
        }
    }

    private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        if (Interlocked.Exchange(ref _frameBusy, 1) == 1)
        {
            return;
        }

        try
        {
            using var reference = sender.TryAcquireLatestFrame();
            var bitmap = reference?.VideoMediaFrame?.SoftwareBitmap;
            if (bitmap is null)
            {
                Volatile.Write(ref _frameBusy, 0);
                return;
            }

            using var converted = bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8 && bitmap.BitmapAlphaMode == BitmapAlphaMode.Premultiplied
                ? SoftwareBitmap.Copy(bitmap)
                : SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            var width = converted.PixelWidth;
            var height = converted.PixelHeight;
            var buffer = new Windows.Storage.Streams.Buffer((uint)(width * height * 4));
            converted.CopyToBuffer(buffer);
            var bytes = buffer.ToArray();
            var window = _window;
            if (window is null)
            {
                Volatile.Write(ref _frameBusy, 0);
                return;
            }

            Ctx.Ui.BeginInvoke(() =>
            {
                try
                {
                    window.ShowFrame(bytes, width, height);
                }
                finally
                {
                    Volatile.Write(ref _frameBusy, 0);
                }
            });
        }
        catch
        {
            Volatile.Write(ref _frameBusy, 0);
        }
    }

    /// <summary>Stops and disposes the capture session (turns the camera light off) without blocking the UI thread.</summary>
    private void StopCapture()
    {
        var reader = _reader;
        var capture = _capture;
        _reader = null;
        _capture = null;
        if (reader is not null)
        {
            reader.FrameArrived -= OnFrameArrived;
        }

        if (reader is null && capture is null)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                if (reader is not null)
                {
                    await reader.StopAsync();
                    reader.Dispose();
                }
            }
            catch
            {
                // already stopped
            }

            try
            {
                capture?.Dispose();
            }
            catch
            {
                // already disposed
            }
        });
    }
}

/// <summary>Quick tools page, "Camera preview" card.</summary>
public sealed class CameraPreviewSettingsSection : ISettingsSection
{
    public string? FeatureId => "camera-preview";

    public string PageId => SettingsPages.QuickTools;

    public string? Anchor => "cameraPreview";

    public string? Title => CameraStrings.Title;

    public IReadOnlyList<string> Keywords => ["webcam", "mirror", "global shortcut"];

    public int Order => 50;

    public FrameworkElement CreateView(ISettingsContext c)
    {
        c.App.Register("cameraPreviewShortcutEnabled", false);
        var card = new SettingsCard(CameraStrings.Title);
        var open = UiKit.Button(CameraStrings.OpenButton, ButtonKind.Bordered, 13, "", () => ActionBridge.Invoke(c, "camera-preview", "show"));
        open.HorizontalAlignment = HorizontalAlignment.Left;
        card.Add(open);
        card.AddCaption(CameraStrings.PanelCaption);
        ShortcutSettingsRows.AddGlobalShortcut(card, c, "cameraPreview", "cameraPreviewShortcutEnabled");
        if (CameraAccess.IsDenied())
        {
            var warn = new StackPanel { Orientation = Orientation.Horizontal };
            warn.Children.Add(UiKit.Icon("", 14, ThemeKeys.Orange));
            var label = UiKit.Text(Loc.T("Camera"), 13);
            label.Margin = new Thickness(8, 0, 0, 0);
            warn.Children.Add(label);
            var grid = new Grid();
            grid.Children.Add(warn);
            var status = UiKit.Text(CameraStrings.NotGranted, 11.5, FontWeights.Normal, ThemeKeys.Orange);
            status.HorizontalAlignment = HorizontalAlignment.Right;
            grid.Children.Add(status);
            card.Add(grid);
            var settings = UiKit.Button(CameraStrings.OpenSettings, ButtonKind.Bordered, 11.5, onClick: CameraAccess.OpenPrivacySettings);
            settings.HorizontalAlignment = HorizontalAlignment.Left;
            card.Add(settings);
        }

        return card;
    }
}

/// <summary>Utilities tab row.</summary>
public sealed class CameraPreviewUtility : IPanelUtility
{
    public string? FeatureId => "camera-preview";

    public string Id => "cameraPreview";

    public string Title => CameraStrings.Title;

    public string Glyph => "";

    public string? Caption => CameraAccess.IsDenied() ? CameraStrings.PermissionRequired : CameraStrings.PanelCaption;

    public bool NeedsAttention => CameraAccess.IsDenied();

    public string? ShortcutText => PadServices.ShortcutOf("cameraPreview");

    public int Order => 140;

    public string? SettingsDestination => "quick-tools#cameraPreview";

    public void Invoke(IPanelContext context) => ActionBridge.Invoke(context, "camera-preview", "show");
}
