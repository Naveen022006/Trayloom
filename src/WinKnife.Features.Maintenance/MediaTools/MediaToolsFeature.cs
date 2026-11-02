// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.Tools;

internal sealed class MediaJob : INotifyPropertyChanged
{
    private string _status = "Ready";
    private long _after;

    public required string Path { get; init; }

    public string Name => System.IO.Path.GetFileName(Path);

    public long Before { get; init; }

    public long After
    {
        get => _after;
        set
        {
            _after = value;
            Raise();
            Raise(nameof(AfterText));
        }
    }

    public string BeforeText => UiKit.FormatBytes(Before);

    public string AfterText => After > 0 ? UiKit.FormatBytes(After) : string.Empty;

    public string Status
    {
        get => _status;
        set
        {
            _status = value;
            Raise();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal static class FfmpegLocator
{
    public static string? Find()
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = System.IO.Path.Combine(dir.Trim().Trim('"'), "ffmpeg.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // invalid PATH entry
            }
        }

        return null;
    }
}

/// <summary>Compress, resize and convert images (and videos/GIFs when ffmpeg is installed).</summary>
public sealed class MediaToolsFeature : ToolFeatureBase
{
    private MediaToolsWindow? _window;

    public override string Id => "media-tools";

    public override string Name => "Media compress";

    public override string Description => "Shrink, resize and convert photos in bulk; videos and GIFs too when ffmpeg is installed.";

    public override IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("open", "Compress images...", () => Safe(Open), ""),
    ];

    protected override void OnStart() => BindHotkey("hotkey", null, Open);

    protected override void OnStop()
    {
        _window?.Close();
        _window = null;
    }

    public override FrameworkElement? CreateSettingsView()
    {
        if (Ctx is null)
        {
            return null;
        }

        var open = UiKit.Btn("Open media compressor", Open, accent: true);
        open.HorizontalAlignment = HorizontalAlignment.Left;
        var ffmpeg = FfmpegLocator.Find();
        var builder = new SettingsViewBuilder(Ctx.Settings).AddHotkey("hotkey", "Open media compressor", null, "Optional global shortcut.");
        MediaToolsWindow.AddOptions(builder);
        builder.AddNote(ffmpeg is null
            ? "ffmpeg.exe was not found on PATH, so only images can be processed. Install ffmpeg to enable video and GIF compression."
            : "Video and GIF compression is enabled using " + ffmpeg + ".");
        return UiKit.WithExtras(builder.Build(), open);
    }

    private void Open()
    {
        if (_window is null)
        {
            _window = new MediaToolsWindow(Ctx);
            _window.Closed += (_, _) => _window = null;
        }

        UiKit.Raise(_window);
    }
}

internal sealed class MediaToolsWindow : Window
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic" };
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".mkv", ".avi", ".webm", ".m4v", ".gif" };

    private readonly IFeatureContext _ctx;
    private readonly System.Collections.ObjectModel.ObservableCollection<MediaJob> _jobs = new();
    private readonly DataGrid _grid = new();
    private readonly TextBlock _status = UiKit.Muted("Drop images here or use Add files.");
    private readonly ProgressBar _progress = new() { Height = 6, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 8, 0, 0) };
    private readonly string? _ffmpeg = FfmpegLocator.Find();
    private Button _run = null!;
    private CancellationTokenSource? _cancel;
    private Process? _current;

    public MediaToolsWindow(IFeatureContext ctx)
    {
        _ctx = ctx;
        Title = "Media compress";
        Width = 980;
        Height = 640;
        MinWidth = 760;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(StyleProperty, ThemeKeys.WindowStyle);
        SourceInitialized += (_, _) => UiKit.ApplyTitleBarTheme(this);

        _grid.ItemsSource = _jobs;
        _grid.AutoGenerateColumns = false;
        _grid.IsReadOnly = true;
        _grid.SelectionMode = DataGridSelectionMode.Extended;
        _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        _grid.GridLinesVisibility = DataGridGridLinesVisibility.None;
        _grid.Columns.Add(new DataGridTextColumn { Header = "File", Binding = new Binding(nameof(MediaJob.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Before", Binding = new Binding(nameof(MediaJob.BeforeText)), Width = new DataGridLength(80) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "After", Binding = new Binding(nameof(MediaJob.AfterText)), Width = new DataGridLength(80) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new Binding(nameof(MediaJob.Status)), Width = new DataGridLength(190) });
        _grid.AllowDrop = true;
        _grid.Drop += OnDrop;
        AllowDrop = true;
        Drop += OnDrop;
        _grid.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Delete)
            {
                foreach (var job in _grid.SelectedItems.OfType<MediaJob>().ToList())
                {
                    _jobs.Remove(job);
                }
            }
        };

        _run = UiKit.Btn("Process", () => _ = RunAsync(), true, "");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(UiKit.Btn("Add files...", AddFiles, false, ""));
        buttons.Children.Add(UiKit.Btn("Clear", () => _jobs.Clear()));
        buttons.Children.Add(_run);
        buttons.Children.Add(UiKit.Btn("Stop", () =>
        {
            _cancel?.Cancel();
            try
            {
                _current?.Kill(true);
            }
            catch
            {
                // already finished
            }
        }));

        var left = new DockPanel { Margin = new Thickness(0, 0, 16, 0) };
        var bottom = new StackPanel();
        bottom.Children.Add(buttons);
        bottom.Children.Add(_progress);
        bottom.Children.Add(_status);
        DockPanel.SetDock(bottom, Dock.Bottom);
        left.Children.Add(bottom);
        left.Children.Add(_grid);

        var builder = new SettingsViewBuilder(ctx.Settings);
        AddOptions(builder);
        builder.AddNote(_ffmpeg is null
            ? "Videos and GIFs need ffmpeg.exe on PATH (not found), so they are skipped."
            : "ffmpeg found: videos are re-encoded to H.264 MP4, GIFs are optimised.");
        var options = new ScrollViewer { Content = builder.Build(), Width = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        var layout = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(options, Dock.Right);
        layout.Children.Add(options);
        layout.Children.Add(left);
        Content = layout;
        Closed += (_, _) => _cancel?.Cancel();
    }

    public static void AddOptions(SettingsViewBuilder builder)
    {
        builder
            .AddChoice(
                "format",
                "Image format",
                new Choice<string>[] { new("keep", "Keep (BMP/TIFF/WebP become PNG)"), new("jpeg", "JPEG"), new("png", "PNG") },
                "keep")
            .AddIntSlider("quality", "Quality", 30, 100, 80, "JPEG quality, and the video/GIF quality level.")
            .AddChoice(
                "maxSize",
                "Longest side",
                new Choice<int>[] { new(0, "Original size"), new(3840, "3840 px"), new(2560, "2560 px"), new(1920, "1920 px"), new(1280, "1280 px"), new(800, "800 px") },
                0,
                "Larger images are scaled down; smaller ones are never enlarged.")
            .AddText("outputFolder", "Output folder", string.Empty, "Leave empty to save next to each original with a -min suffix. Originals are never overwritten.");
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            AddPaths(files);
        }
    }

    private void AddFiles()
    {
        var dialog = new OpenFileDialog { Multiselect = true, Filter = "Images and videos|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.heic;*.mp4;*.mov;*.mkv;*.avi;*.webm;*.m4v|All files|*.*" };
        if (dialog.ShowDialog(this) == true)
        {
            AddPaths(dialog.FileNames);
        }
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        var skipped = 0;
        foreach (var path in paths.SelectMany(p => Directory.Exists(p) ? Directory.EnumerateFiles(p) : new[] { p }))
        {
            var ext = System.IO.Path.GetExtension(path);
            var supported = ImageExtensions.Contains(ext) || (_ffmpeg is not null && VideoExtensions.Contains(ext));
            if (!supported || _jobs.Any(j => string.Equals(j.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                skipped++;
                continue;
            }

            _jobs.Add(new MediaJob { Path = path, Before = new FileInfo(path).Length });
        }

        _status.Text = skipped > 0 ? $"{_jobs.Count} file(s) queued, {skipped} skipped (unsupported or duplicate)." : $"{_jobs.Count} file(s) queued.";
    }

    private async Task RunAsync()
    {
        var jobs = _jobs.Where(j => j.Status is "Ready" or "Failed" or "Cancelled").ToList();
        if (jobs.Count == 0)
        {
            return;
        }

        var s = _ctx.Settings;
        var options = new MediaOptions(
            s.Get("format", "keep"),
            Math.Clamp(s.Get("quality", 80), 30, 100),
            s.Get("maxSize", 0),
            Environment.ExpandEnvironmentVariables(s.Get("outputFolder", string.Empty).Trim()));
        _cancel = new CancellationTokenSource();
        var token = _cancel.Token;
        _run.IsEnabled = false;
        _progress.Value = 0;
        var done = 0;
        long saved = 0;
        try
        {
            foreach (var job in jobs)
            {
                if (token.IsCancellationRequested)
                {
                    job.Status = "Cancelled";
                    continue;
                }

                job.Status = "Working...";
                var ext = System.IO.Path.GetExtension(job.Path);
                var isVideo = VideoExtensions.Contains(ext) && (_ffmpeg is not null) && (!ImageExtensions.Contains(ext) || ext.Equals(".gif", StringComparison.OrdinalIgnoreCase));
                try
                {
                    string result;
                    if (isVideo)
                    {
                        result = await RunFfmpegAsync(job, options, token);
                    }
                    else
                    {
                        result = await RunOnStaAsync(() => ImageProcessor.Process(job.Path, options, out var after, out var note) ?? throw new InvalidOperationException(note), job);
                    }

                    var outInfo = new FileInfo(result);
                    job.After = outInfo.Exists ? outInfo.Length : 0;
                    saved += Math.Max(0, job.Before - job.After);
                    job.Status = job.After > 0 && job.Before > 0 ? $"Done ({100 - job.After * 100 / job.Before}% smaller)" : "Done";
                    if (job.After >= job.Before && job.Before > 0)
                    {
                        job.Status = "Done (no size gain)";
                    }
                }
                catch (OperationCanceledException)
                {
                    job.Status = "Cancelled";
                }
                catch (SkipException skip)
                {
                    job.Status = skip.Message;
                }
                catch (Exception ex)
                {
                    _ctx.Log.Warn($"Media job failed for {job.Path}: {ex.Message}");
                    job.Status = "Failed: " + ex.Message;
                }

                done++;
                _progress.Value = done / (double)jobs.Count;
            }

            _status.Text = $"Finished {done} file(s); saved {UiKit.FormatBytes(saved)}.";
        }
        finally
        {
            _run.IsEnabled = true;
            _cancel = null;
        }
    }

    private static Task<string> RunOnStaAsync(Func<string> work, MediaJob job)
    {
        var source = new TaskCompletionSource<string>();
        var thread = new Thread(() =>
        {
            try
            {
                source.SetResult(work());
            }
            catch (Exception ex)
            {
                source.SetException(ex);
            }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return source.Task;
    }

    private async Task<string> RunFfmpegAsync(MediaJob job, MediaOptions options, CancellationToken token)
    {
        var input = job.Path;
        var isGif = System.IO.Path.GetExtension(input).Equals(".gif", StringComparison.OrdinalIgnoreCase);
        var extension = isGif ? ".gif" : ".mp4";
        var output = ImageProcessor.OutputPath(input, options.OutputFolder, extension);
        var scale = options.MaxSize > 0
            ? $"scale='if(gt(iw,ih),min({options.MaxSize},iw),-2)':'if(gt(iw,ih),-2,min({options.MaxSize},ih))'"
            : (isGif ? "scale=iw:-1" : "scale=trunc(iw/2)*2:trunc(ih/2)*2");
        string args;
        if (isGif)
        {
            var fps = options.Quality >= 80 ? 15 : 10;
            args = $"-y -i \"{input}\" -vf \"fps={fps},{scale}:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse\" \"{output}\"";
        }
        else
        {
            var crf = (int)Math.Round(18 + (100 - options.Quality) * 0.23, MidpointRounding.AwayFromZero);
            args = $"-y -i \"{input}\" -vf \"{scale}\" -c:v libx264 -crf {crf} -preset medium -c:a aac -b:a 128k -movflags +faststart \"{output}\"";
        }

        var info = new ProcessStartInfo(_ffmpeg!, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        using var process = Process.Start(info)!;
        _current = process;
        var stderr = process.StandardError.ReadToEndAsync();
        _ = process.StandardOutput.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(true);
            }
            catch
            {
                // ignore
            }

            if (File.Exists(output))
            {
                File.Delete(output);
            }

            throw;
        }
        finally
        {
            _current = null;
        }

        if (process.ExitCode != 0)
        {
            var text = await stderr;
            throw new InvalidOperationException("ffmpeg failed: " + (text.Split('\n').LastOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "unknown error"));
        }

        return output;
    }
}

internal sealed class SkipException(string message) : Exception(message);

internal sealed record MediaOptions(string Format, int Quality, int MaxSize, string OutputFolder);

internal static class ImageProcessor
{
    public static string OutputPath(string input, string outputFolder, string extension)
    {
        var folder = string.IsNullOrWhiteSpace(outputFolder) ? System.IO.Path.GetDirectoryName(input)! : outputFolder;
        Directory.CreateDirectory(folder);
        var baseName = System.IO.Path.GetFileNameWithoutExtension(input) + "-min";
        var path = System.IO.Path.Combine(folder, baseName + extension);
        var n = 2;
        while (File.Exists(path))
        {
            path = System.IO.Path.Combine(folder, $"{baseName}-{n++}{extension}");
        }

        return path;
    }

    /// <summary>Re-encodes one image. Must run on an STA thread. Returns the output path.</summary>
    public static string? Process(string input, MediaOptions options, out long after, out string note)
    {
        after = 0;
        note = string.Empty;
        BitmapSource source;
        using (var stream = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            source = frame;
            var rotation = ReadRotation(frame);
            if (rotation != 0)
            {
                source = new TransformedBitmap(source, new RotateTransform(rotation));
            }
        }

        var ext = System.IO.Path.GetExtension(input).ToLowerInvariant();
        var originalIsJpeg = ext is ".jpg" or ".jpeg";
        var outputKind = options.Format switch
        {
            "jpeg" => "jpeg",
            "png" => "png",
            _ => originalIsJpeg ? "jpeg" : "png",
        };

        var longest = Math.Max(source.PixelWidth, source.PixelHeight);
        var resized = false;
        if (options.MaxSize > 0 && longest > options.MaxSize)
        {
            var factor = (double)options.MaxSize / longest;
            source = new TransformedBitmap(source, new ScaleTransform(factor, factor));
            resized = true;
        }

        var keepsFormat = options.Format == "keep";
        BitmapEncoder encoder;
        BitmapSource frameSource;
        string outExt;
        if (outputKind == "jpeg")
        {
            encoder = new JpegBitmapEncoder { QualityLevel = options.Quality };
            frameSource = FlattenOnWhite(source);
            outExt = ".jpg";
        }
        else
        {
            encoder = new PngBitmapEncoder { Interlace = PngInterlaceOption.Off };
            frameSource = source;
            outExt = ".png";
        }

        encoder.Frames.Add(BitmapFrame.Create(frameSource));
        using var buffer = new MemoryStream();
        encoder.Save(buffer);

        var originalSize = new FileInfo(input).Length;
        if (keepsFormat && !resized && ext == outExt && buffer.Length >= originalSize)
        {
            throw new SkipException("Already optimal, kept as is");
        }

        var output = OutputPath(input, options.OutputFolder, outExt);
        File.WriteAllBytes(output, buffer.ToArray());
        after = buffer.Length;
        return output;
    }

    private static int ReadRotation(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata meta && meta.GetQuery("/app1/ifd/{ushort=274}") is ushort orientation)
            {
                return orientation switch
                {
                    3 => 180,
                    6 => 90,
                    8 => 270,
                    _ => 0,
                };
            }
        }
        catch
        {
            // no EXIF orientation
        }

        return 0;
    }

    private static BitmapSource FlattenOnWhite(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var w = converted.PixelWidth;
        var h = converted.PixelHeight;
        var stride = w * 4;
        var pixels = new byte[stride * h];
        converted.CopyPixels(pixels, stride, 0);
        var hasAlpha = false;
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 255)
            {
                hasAlpha = true;
                break;
            }
        }

        if (hasAlpha)
        {
            for (var i = 0; i < pixels.Length; i += 4)
            {
                var a = pixels[i + 3];
                pixels[i] = (byte)((pixels[i] * a + 255 * (255 - a)) / 255);
                pixels[i + 1] = (byte)((pixels[i + 1] * a + 255 * (255 - a)) / 255);
                pixels[i + 2] = (byte)((pixels[i + 2] * a + 255 * (255 - a)) / 255);
                pixels[i + 3] = 255;
            }
        }

        var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        return new FormatConvertedBitmap(result, PixelFormats.Bgr24, null, 0);
    }
}
