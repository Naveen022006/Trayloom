// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinKnife.Features.Maintenance;

/// <summary>Information about a source image: pixel size after EXIF orientation.</summary>
internal sealed record ImageInfo(int Width, int Height, int Orientation);

/// <summary>The image tool: decode (WIC), resize/fit/fill, background, watermark, encode JPEG/PNG/HEIC/PDF (spec 7.5.5/7.5.6).</summary>
internal static class MediaImage
{
    private const int MaxSide = 20_000;
    private const long MaxArea = 8192L * 8192L;

    public static ImageInfo? ReadInfo(string path)
    {
        try
        {
            var decoder = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            var orientation = OrientationOf(frame);
            var swap = orientation is >= 5 and <= 8;
            return new ImageInfo(swap ? frame.PixelHeight : frame.PixelWidth, swap ? frame.PixelWidth : frame.PixelHeight, orientation);
        }
        catch
        {
            return null;
        }
    }

    private static int OrientationOf(BitmapSource frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata metadata)
            {
                foreach (var query in new[] { "System.Photo.Orientation", "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" })
                {
                    if (metadata.ContainsQuery(query) && metadata.GetQuery(query) is { } value && int.TryParse(value.ToString(), out var o) && o is >= 1 and <= 8)
                    {
                        return o;
                    }
                }
            }
        }
        catch
        {
            // no metadata
        }

        return 1;
    }

    private static BitmapSource Oriented(BitmapSource source, int orientation)
    {
        Transform? transform = orientation switch
        {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => Group(new RotateTransform(90), new ScaleTransform(-1, 1)),
            6 => new RotateTransform(90),
            7 => Group(new RotateTransform(270), new ScaleTransform(-1, 1)),
            8 => new RotateTransform(270),
            _ => null,
        };
        if (transform is null)
        {
            return source;
        }

        var result = new TransformedBitmap(source, transform);
        result.Freeze();
        return result;

        static Transform Group(params Transform[] items)
        {
            var group = new TransformGroup();
            foreach (var item in items)
            {
                group.Children.Add(item);
            }

            return group;
        }
    }

    /// <summary>Target output size for the resize mode (spec: sizes rounded and at least 1).</summary>
    public static (int W, int H) TargetSize(ImageOptions o, int width, int height)
    {
        int W, H;
        switch (o.ResizeKind)
        {
            case "none":
                (W, H) = (width, height);
                break;
            case "width":
                W = o.ResizeWidth;
                H = (int)Math.Round((double)o.ResizeWidth * height / width);
                break;
            case "height":
                H = o.ResizeHeight;
                W = (int)Math.Round((double)o.ResizeHeight * width / height);
                break;
            case "exact":
                (W, H) = (o.ResizeWidth, o.ResizeHeight);
                break;
            default:
            {
                var scale = Math.Min(1.0, (double)o.MaxDimension / Math.Max(width, height));
                W = (int)Math.Round(width * scale);
                H = (int)Math.Round(height * scale);
                break;
            }
        }

        return (Math.Max(1, W), Math.Max(1, H));
    }

    public static bool IsSafe(int w, int h) => w <= MaxSide && h <= MaxSide && (long)w * h <= MaxArea;

    private static BitmapSource Load(string path, out int orientation)
    {
        BitmapDecoder decoder;
        try
        {
            decoder = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        }
        catch (Exception)
        {
            throw new MediaException(MediaFailureKind.Unsupported);
        }

        var frame = decoder.Frames[0];
        orientation = OrientationOf(frame);
        frame.Freeze();
        return Oriented(frame, orientation);
    }

    /// <summary>Processes one image into <paramref name="output"/> (the exact file).</summary>
    public static (long OriginalBytes, long OutputBytes) Process(string input, ImageOptions o, string output, bool overwrite, int index, CancellationToken ct)
    {
        var original = new FileInfo(input).Length;
        var source = Load(input, out _);
        ct.ThrowIfCancellationRequested();
        var (outW, outH) = TargetSize(o, source.PixelWidth, source.PixelHeight);
        if (!IsSafe(outW, outH))
        {
            throw new MediaException(MediaFailureKind.ImageTooLarge);
        }

        var logo = (o.WatermarkKind is "logo" or "textAndLogo") ? LoadLogo(o.WatermarkLogoPath) : null;
        if ((o.WatermarkKind is "logo" or "textAndLogo") && logo is null)
        {
            throw new MediaException(MediaFailureKind.WatermarkUnavailable);
        }

        var rendered = Render(source, o, outW, outH, logo);
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var temp = StagedOutput.TempFor(output);
        try
        {
            Encode(rendered, o, temp, input);
            StagedOutput.Commit(temp, output, overwrite, ct);
        }
        catch
        {
            StagedOutput.TryDelete(temp);
            throw;
        }

        if (o.PreserveModificationDate)
        {
            try
            {
                File.SetLastWriteTimeUtc(output, File.GetLastWriteTimeUtc(input));
            }
            catch
            {
                // cosmetic
            }
        }

        return (original, new FileInfo(output).Length);
    }

    private static BitmapSource? LoadLogo(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            var decoder = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            frame.Freeze();
            return frame;
        }
        catch
        {
            return null;
        }
    }

    private static bool FlatFormat(string format) => format is "jpeg" or "pdf";

    /// <summary>Draws the image on a canvas of the target size with the background and watermark.</summary>
    public static BitmapSource Render(BitmapSource source, ImageOptions o, int outW, int outH, BitmapSource? logo)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        RenderOptions.SetEdgeMode(visual, EdgeMode.Unspecified);
        using (var dc = visual.RenderOpen())
        {
            Brush? background = o.Background switch { "white" => Brushes.White, "black" => Brushes.Black, _ => FlatFormat(o.Format) ? Brushes.White : null };
            if (background is not null)
            {
                dc.DrawRectangle(background, null, new Rect(0, 0, outW, outH));
            }

            Rect target;
            if (o.ResizeKind == "exact" && o.ExactMode is "fit" or "fill")
            {
                var sx = (double)outW / source.PixelWidth;
                var sy = (double)outH / source.PixelHeight;
                var scale = o.ExactMode == "fit" ? Math.Min(sx, sy) : Math.Max(sx, sy);
                var w = source.PixelWidth * scale;
                var h = source.PixelHeight * scale;
                target = new Rect((outW - w) / 2, (outH - h) / 2, w, h);
                dc.PushClip(new RectangleGeometry(new Rect(0, 0, outW, outH)));
                dc.DrawImage(source, target);
                dc.Pop();
            }
            else
            {
                dc.DrawImage(source, new Rect(0, 0, outW, outH));
            }

            DrawWatermark(dc, o, outW, outH, logo);
        }

        var bitmap = new RenderTargetBitmap(outW, outH, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static void DrawWatermark(DrawingContext dc, ImageOptions o, int canvasW, int canvasH, BitmapSource? logo)
    {
        var showText = o.WatermarkKind is "text" or "textAndLogo" && !string.IsNullOrWhiteSpace(o.WatermarkText);
        var showLogo = logo is not null && o.WatermarkKind is "logo" or "textAndLogo";
        if (!showText && !showLogo)
        {
            return;
        }

        var side = Math.Min(canvasW, canvasH);
        var gap = Math.Max(6, side * 0.015);
        var fontSize = Math.Max(12, Math.Min(96, side * 0.045));
        var opacity = Math.Clamp(o.WatermarkOpacity, 0.1, 1);
        FormattedText? text = null;
        var drawSize = fontSize;
        var face = new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        if (showText)
        {
            text = new FormattedText(o.WatermarkText, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), fontSize, Brushes.White, 1.0);
        }

        var logoSide = showLogo ? Math.Max(12, side * o.WatermarkScale) : 0;
        double logoW = 0, logoH = 0;
        if (showLogo && logo is not null)
        {
            var fit = Math.Min(logoSide / logo.PixelWidth, logoSide / logo.PixelHeight);
            logoW = logo.PixelWidth * fit;
            logoH = logo.PixelHeight * fit;
        }

        var margin = o.WatermarkMargin;
        var textW = text?.Width ?? 0;
        var contentW = logoW + (showLogo && showText ? gap : 0) + textW;
        var shrink = 1.0;
        if (contentW > canvasW - 2 * margin && contentW > 0)
        {
            shrink = Math.Max(0.1, (canvasW - 2 * margin) / contentW);
            if (text is not null)
            {
                drawSize = Math.Max(8, fontSize * shrink);
                text = new FormattedText(o.WatermarkText, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, drawSize, Brushes.White, 1.0);
            }

            logoW *= shrink;
            logoH *= shrink;
            textW = text?.Width ?? 0;
            contentW = logoW + (showLogo && showText ? gap : 0) + textW;
        }

        var contentH = Math.Max(logoH, text?.Height ?? 0);
        var maxX = Math.Max(margin, canvasW - contentW - margin);
        var maxY = Math.Max(margin, canvasH - contentH - margin);
        double x, y;
        switch (o.WatermarkPosition)
        {
            case "topLeft": (x, y) = (margin, margin); break;
            case "topRight": (x, y) = (maxX, margin); break;
            case "bottomLeft": (x, y) = (margin, maxY); break;
            case "center": (x, y) = (Math.Max(margin, (canvasW - contentW) / 2), Math.Max(margin, (canvasH - contentH) / 2)); break;
            default: (x, y) = (maxX, maxY); break;
        }

        if (showLogo && logo is not null)
        {
            dc.PushOpacity(opacity);
            dc.DrawImage(logo, new Rect(x, y + (contentH - logoH) / 2, logoW, logoH));
            dc.Pop();
            x += logoW + (showText ? gap : 0);
        }

        if (text is not null)
        {
            var textY = y + (contentH - text.Height) / 2;
            var shadow = new FormattedText(o.WatermarkText, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, drawSize, new SolidColorBrush(Color.FromArgb((byte)(255 * 0.45 * opacity), 0, 0, 0)), 1.0);
            dc.DrawText(shadow, new Point(x, textY + 1));
            text.SetForegroundBrush(new SolidColorBrush(Color.FromArgb((byte)(255 * opacity), 255, 255, 255)));
            dc.DrawText(text, new Point(x, textY));
        }
    }


    // ------------------------------------------------------------------ encode

    private static void Encode(BitmapSource rendered, ImageOptions o, string path, string input)
    {
        var quality = (int)Math.Clamp(Math.Round(o.Quality * 100), 10, 100);
        switch (o.Format)
        {
            case "png":
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(WithMetadata(rendered, input, o, "png"));
                using var stream = File.Create(path);
                encoder.Save(stream);
                break;
            }

            case "pdf":
            {
                var jpeg = JpegBytes(rendered, quality, input, o);
                File.WriteAllBytes(path, PdfDocument.SingleImage(jpeg, rendered.PixelWidth, rendered.PixelHeight));
                break;
            }

            case "heic":
                EncodeHeic(rendered, quality, path);
                break;
            default:
                File.WriteAllBytes(path, JpegBytes(rendered, quality, input, o));
                break;
        }
    }

    private static byte[] JpegBytes(BitmapSource rendered, int quality, string input, ImageOptions o)
    {
        BitmapSource flat = rendered;
        if (rendered.Format != PixelFormats.Bgr32)
        {
            // flatten any remaining alpha onto white
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, rendered.PixelWidth, rendered.PixelHeight));
                dc.DrawImage(rendered, new Rect(0, 0, rendered.PixelWidth, rendered.PixelHeight));
            }

            var target = new RenderTargetBitmap(rendered.PixelWidth, rendered.PixelHeight, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);
            flat = new FormatConvertedBitmap(target, PixelFormats.Bgr32, null, 0);
        }

        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(WithMetadata(flat, input, o, "jpeg"));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Copies the source metadata (minus orientation) when "Remove metadata" is off and the container matches.</summary>
    private static BitmapFrame WithMetadata(BitmapSource image, string input, ImageOptions o, string container)
    {
        if (o.StripMetadata)
        {
            return BitmapFrame.Create(image);
        }

        try
        {
            var ext = Path.GetExtension(input).ToLowerInvariant();
            var sameContainer = container == "jpeg" ? ext is ".jpg" or ".jpeg" : ext == ".png";
            if (sameContainer)
            {
                var decoder = BitmapDecoder.Create(new Uri(input), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (decoder.Frames[0].Metadata is BitmapMetadata metadata)
                {
                    var clone = metadata.Clone();
                    if (container == "jpeg")
                    {
                        try
                        {
                            clone.SetQuery("/app1/ifd/{ushort=274}", (ushort)1);
                        }
                        catch
                        {
                            // no orientation tag
                        }
                    }

                    return BitmapFrame.Create(image, null, clone, null);
                }
            }
        }
        catch
        {
            // fall back to a bare frame
        }

        return BitmapFrame.Create(image);
    }

    private static void EncodeHeic(BitmapSource rendered, int quality, string path)
    {
        try
        {
            var bgra = new FormatConvertedBitmap(rendered, PixelFormats.Bgra32, null, 0);
            var stride = bgra.PixelWidth * 4;
            var pixels = new byte[stride * bgra.PixelHeight];
            bgra.CopyPixels(pixels, stride, 0);
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var properties = new Windows.Graphics.Imaging.BitmapPropertySet
            {
                ["ImageQuality"] = new Windows.Graphics.Imaging.BitmapTypedValue(quality / 100.0f, Windows.Foundation.PropertyType.Single),
            };
            var encoder = Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.HeifEncoderId, stream, properties).AsTask().GetAwaiter().GetResult();
            encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Straight, (uint)bgra.PixelWidth, (uint)bgra.PixelHeight, 96, 96, pixels);
            encoder.FlushAsync().AsTask().GetAwaiter().GetResult();
            stream.Seek(0);
            using var reader = stream.AsStreamForRead();
            using var file = File.Create(path);
            reader.CopyTo(file);
        }
        catch (Exception)
        {
            throw new MediaException(MediaFailureKind.Message, "HEIC needs the HEIF and HEVC extensions from the Microsoft Store.");
        }
    }

    /// <summary>True when the HEIF encoder is installed.</summary>
    public static bool HeicAvailable()
    {
        try
        {
            return Windows.Graphics.Imaging.BitmapEncoder.GetEncoderInformationEnumerator().Any(e => e.CodecId == Windows.Graphics.Imaging.BitmapEncoder.HeifEncoderId);
        }
        catch
        {
            return false;
        }
    }

    // ------------------------------------------------------------------ batch

    public static async Task<MediaResult> RunAsync(IReadOnlyList<string> inputs, ImageOptions o, string? singleOutput, bool singleManual, string? outputDirectory, bool subfolder, Action<double> progress, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        return await Task.Run(() =>
        {
            var items = new List<ImageBatchItem>();
            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < inputs.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var input = inputs[i];
                string? output = null;
                try
                {
                    if (inputs.Count == 1 && singleOutput is not null)
                    {
                        output = singleOutput;
                    }
                    else
                    {
                        var info = ReadInfo(input);
                        var (w, h) = info is null ? (0, 0) : TargetSize(o, info.Width, info.Height);
                        var name = MediaNaming.ExpandPattern(o.RenamePattern, input, i + 1, w, h, o.Format);
                        var folder = outputDirectory ?? Path.GetDirectoryName(input)!;
                        if (subfolder)
                        {
                            folder = Path.Combine(folder, "Converted");
                        }

                        output = MediaNaming.Unique(folder, name, MediaNaming.ExtensionOf(o.Format), reserved);
                    }

                    if (MediaNaming.SameFile(input, output))
                    {
                        throw new MediaException(MediaFailureKind.SameOutput);
                    }

                    var (original, bytes) = Process(input, o, output, inputs.Count == 1 && singleManual, i + 1, ct);
                    items.Add(new ImageBatchItem(input, output, null, original, bytes));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (inputs.Count > 1)
                {
                    items.Add(new ImageBatchItem(input, null, ex is MediaException me ? me.Text : ex.Message, 0, 0));
                }

                progress((i + 1.0) / inputs.Count);
            }

            var ok = items.Where(x => x.Output is not null).ToList();
            if (ok.Count == 0)
            {
                throw new MediaException(MediaFailureKind.Message, items.FirstOrDefault()?.Error);
            }

            return new MediaResult
            {
                Tool = MediaTool.Image,
                Input = inputs[0],
                Outputs = ok.Select(x => x.Output!).ToList(),
                OriginalBytes = ok.Sum(x => x.OriginalBytes),
                OutputBytes = ok.Sum(x => x.OutputBytes),
                Elapsed = watch.Elapsed,
                Batch = items,
            };
        }, ct).ConfigureAwait(false);
    }
}

/// <summary>Minimal one-page PDF writer embedding a JPEG (DCTDecode); 1 pixel = 1 point.</summary>
internal static class PdfDocument
{
    public static byte[] SingleImage(byte[] jpeg, int width, int height)
    {
        using var stream = new MemoryStream();
        var offsets = new List<long>();
        void Write(string text) => stream.Write(Encoding.ASCII.GetBytes(text));
        void Object(int number, string body)
        {
            offsets.Add(stream.Position);
            Write($"{number} 0 obj\n{body}\nendobj\n");
        }

        Write("%PDF-1.4\n%âãÏÓ\n");
        Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Object(3, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] /Resources << /XObject << /Im0 5 0 R >> >> /Contents 4 0 R >>");
        var content = $"q {width} 0 0 {height} 0 0 cm /Im0 Do Q";
        Object(4, $"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
        offsets.Add(stream.Position);
        Write($"5 0 obj\n<< /Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>\nstream\n");
        stream.Write(jpeg);
        Write("\nendstream\nendobj\n");
        var xref = stream.Position;
        Write($"xref\n0 6\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            Write($"{offset:D10} 00000 n \n");
        }

        Write($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return stream.ToArray();
    }
}

/// <summary>OCR on <c>Windows.Media.Ocr</c> (spec 7.5.6).</summary>
internal static class MediaText
{
    public static async Task<MediaResult> RunAsync(string input, string output, bool outputManual, bool accurate, Action<double> progress, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        progress(0.2);
        var engine = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"))
            ?? Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new MediaException(MediaFailureKind.Message, "Install the OCR language pack in Settings > Time & language > Language & region.");
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(input).AsTask(ct).ConfigureAwait(false);
        using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read).AsTask(ct).ConfigureAwait(false);
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream).AsTask(ct).ConfigureAwait(false);
        var width = (int)decoder.PixelWidth;
        var height = (int)decoder.PixelHeight;
        if (!MediaImage.IsSafe(width, height))
        {
            throw new MediaException(MediaFailureKind.ImageTooLarge);
        }

        var max = (int)Windows.Media.Ocr.OcrEngine.MaxImageDimension;
        var scale = 1.0;
        if (Math.Max(width, height) > max)
        {
            scale = (double)max / Math.Max(width, height);
        }
        else if (accurate && Math.Max(width, height) < 1000)
        {
            scale = Math.Min(2.0, (double)max / Math.Max(width, height));
        }

        var transform = new Windows.Graphics.Imaging.BitmapTransform { ScaledWidth = (uint)Math.Max(1, width * scale), ScaledHeight = (uint)Math.Max(1, height * scale), InterpolationMode = Windows.Graphics.Imaging.BitmapInterpolationMode.Fant };
        using var bitmap = await decoder.GetSoftwareBitmapAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied, transform, Windows.Graphics.Imaging.ExifOrientationMode.RespectExifOrientation, Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage).AsTask(ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var recognized = await engine.RecognizeAsync(bitmap).AsTask(ct).ConfigureAwait(false);
        var text = string.Join("\n", recognized.Lines.Select(l => l.Text));
        progress(0.9);
        long bytes = Encoding.UTF8.GetByteCount(text);
        if (!string.IsNullOrEmpty(output))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var temp = StagedOutput.TempFor(output);
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            StagedOutput.Commit(temp, output, outputManual, ct);
            bytes = new FileInfo(output).Length;
        }

        return new MediaResult
        {
            Tool = MediaTool.Text,
            Input = input,
            Outputs = string.IsNullOrEmpty(output) ? [] : [output],
            OriginalBytes = new FileInfo(input).Length,
            OutputBytes = bytes,
            Elapsed = watch.Elapsed,
            Text = text,
        };
    }
}
