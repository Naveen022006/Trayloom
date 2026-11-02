// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using WinKnife.Core;
using WinKnife.Core.Native;
using WinKnife.Core.Ui;
using static WinKnife.Core.Native.NativeConstants;

namespace WinKnife.Features.Tools;

/// <summary>A floating round mirror: shows your webcam in a small always-on-top window.</summary>
public sealed class CameraPreviewFeature : ToolFeatureBase
{
    private CameraWindow? _window;
    private MediaCapture? _capture;
    private MediaFrameReader? _reader;
    private int _frameBusy;
    private int _generation;

    public override string Id => "camera-preview";

    public override string Name => "Camera preview";

    public override string Description => "Check how you look with a floating round mirror from your webcam, before a call.";

    public override IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("toggle", _window is null ? "Show camera preview" : "Hide camera preview", () => Safe(() => _ = ToggleAsync()), ""),
    ];

    protected override void OnStart() => BindHotkey("hotkey", Hotkey.Parse("Ctrl+Alt+Shift+K"), () => _ = ToggleAsync());

    protected override void OnStop()
    {
        CloseAll();
    }

    public override FrameworkElement? CreateSettingsView()
    {
        if (Ctx is null)
        {
            return null;
        }

        return new SettingsViewBuilder(Ctx.Settings)
            .AddHotkey("hotkey", "Show or hide the preview", Hotkey.Parse("Ctrl+Alt+Shift+K"))
            .AddToggle("mirror", "Mirror the image", true, "Flip horizontally like a real mirror.")
            .AddChoice(
                "size",
                "Size",
                new Choice<int>[] { new(160, "Small"), new(240, "Medium"), new(360, "Large"), new(520, "Extra large") },
                240,
                "You can also scroll over the preview to resize it, and drag it anywhere.")
            .AddNote("The preview needs camera access for desktop apps: Windows Settings > Privacy & security > Camera > Let desktop apps access your camera. The camera is only on while the preview is visible.")
            .Build();
    }

    protected override void OnSettingChangedUi(string key)
    {
        if (_window is not null && key is "mirror" or "size")
        {
            _window.ApplySettings(Ctx.Settings.Get("mirror", true), Ctx.Settings.Get("size", 240));
        }
    }

    private async Task ToggleAsync()
    {
        if (_window is not null)
        {
            CloseAll();
            return;
        }

        var generation = ++_generation;
        var window = new CameraWindow(Ctx.Settings.Get("mirror", true), Ctx.Settings.Get("size", 240), size => Ctx.Settings.Set("size", size));
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_window, window))
            {
                CloseAll();
            }
        };
        _window = window;
        window.Show();
        try
        {
            await StartCaptureAsync(window, generation);
        }
        catch (UnauthorizedAccessException)
        {
            CloseAll();
            Ctx.Notifier.Show(Name, "Camera access is blocked. Turn on Settings > Privacy & security > Camera > Let desktop apps access your camera.", NotificationLevel.Warning);
        }
        catch (Exception ex)
        {
            CloseAll();
            Ctx.Log.Warn("Camera failed: " + ex.Message);
            Ctx.Notifier.Show(Name, "The camera could not be started: " + ex.Message, NotificationLevel.Warning);
        }
    }

    private async Task StartCaptureAsync(CameraWindow window, int generation)
    {
        var groups = await MediaFrameSourceGroup.FindAllAsync();
        var group = groups.FirstOrDefault(g => g.SourceInfos.Any(i => i.SourceKind == MediaFrameSourceKind.Color));
        if (group is null)
        {
            throw new InvalidOperationException("No camera was found.");
        }

        var capture = new MediaCapture();
        await capture.InitializeAsync(new MediaCaptureInitializationSettings
        {
            SourceGroup = group,
            SharingMode = MediaCaptureSharingMode.SharedReadOnly,
            StreamingCaptureMode = StreamingCaptureMode.Video,
            MemoryPreference = MediaCaptureMemoryPreference.Cpu,
        });

        if (generation != _generation || _window != window)
        {
            capture.Dispose();
            return;
        }

        var source = capture.FrameSources.Values.First(s => s.Info.SourceKind == MediaFrameSourceKind.Color);
        var reader = await capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
        reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
        reader.FrameArrived += OnFrameArrived;
        _capture = capture;
        _reader = reader;
        var status = await reader.StartAsync();
        if (status != MediaFrameReaderStartStatus.Success)
        {
            throw new InvalidOperationException("The camera is in use or unavailable (" + status + ").");
        }
    }

    private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        if (Interlocked.Exchange(ref _frameBusy, 1) == 1)
        {
            return; // previous frame still being drawn: drop this one
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

    private void CloseAll()
    {
        _generation++;
        var window = _window;
        var reader = _reader;
        var capture = _capture;
        _window = null;
        _reader = null;
        _capture = null;
        if (reader is not null)
        {
            reader.FrameArrived -= OnFrameArrived;
        }

        // Stop the camera (turns the privacy light off) without blocking the UI thread.
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
        window?.Close();
    }
}

internal sealed class CameraWindow : Window
{
    private readonly Image _image = new() { Stretch = Stretch.UniformToFill, RenderTransformOrigin = new Point(0.5, 0.5) };
    private readonly ScaleTransform _flip = new(-1, 1);
    private readonly Ellipse _ring = new() { StrokeThickness = 3 };
    private readonly Action<int> _saveSize;
    private WriteableBitmap? _bitmap;
    private int _size;

    public CameraWindow(bool mirror, int size, Action<int> saveSize)
    {
        _saveSize = saveSize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Title = "Camera preview";
        WindowStartupLocation = WindowStartupLocation.Manual;
        var work = SystemParameters.WorkArea;
        Left = work.Right - size - 24;
        Top = work.Bottom - size - 24;

        _image.RenderTransform = _flip;
        _ring.Stroke = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));
        var disc = new Grid();
        disc.Children.Add(new Ellipse { Fill = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1C)) });
        var host = new Border { Child = _image };
        disc.Children.Add(host);
        disc.Children.Add(_ring);
        disc.SizeChanged += (_, _) =>
        {
            var r = disc.ActualWidth / 2;
            host.Clip = new EllipseGeometry(new Point(r, disc.ActualHeight / 2), r - 1, r - 1);
        };
        disc.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.45 };
        var frame = new Grid { Margin = new Thickness(10) };
        frame.Children.Add(disc);
        Content = frame;

        var menu = new ContextMenu();
        var close = new MenuItem { Header = "Close preview" };
        close.Click += (_, _) => Close();
        menu.Items.Add(close);
        ContextMenu = menu;

        ApplySettings(mirror, size);
        MouseLeftButtonDown += (_, e) =>
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // button released during the call
            }
        };
        MouseWheel += (_, e) =>
        {
            var next = Math.Clamp(_size + (e.Delta > 0 ? 24 : -24), 120, 640);
            ApplySettings(_flip.ScaleX < 0, next);
            _saveSize(next);
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
    }

    public void ApplySettings(bool mirror, int size)
    {
        _flip.ScaleX = mirror ? -1 : 1;
        _size = size;
        Width = size + 20;
        Height = size + 20;
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
}
