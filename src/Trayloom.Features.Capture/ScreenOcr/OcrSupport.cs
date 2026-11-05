// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using Trayloom.Core;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using ZXing;
using ZXing.Common;

namespace Trayloom.Features.Capture;

internal sealed record OcrWordInfo(string Text, Rect Box);

internal sealed record OcrLineInfo(string Text, List<OcrWordInfo> Words, Rect Box);

internal sealed record QrReading(string Payload, string? Url);

/// <summary>Windows.Media.Ocr text recognition, reading-order joining and ZXing-based QR detection.</summary>
internal static class OcrSupport
{
    private const long MaxPixels = 24_000_000;

    public static bool HasEngine()
    {
        try
        {
            return OcrEngine.AvailableRecognizerLanguages.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<OcrEngine> Engines()
    {
        var seen = new HashSet<string>();
        OcrEngine? profile = null;
        try
        {
            profile = OcrEngine.TryCreateFromUserProfileLanguages();
        }
        catch
        {
            // not available
        }

        if (profile is not null && seen.Add(profile.RecognizerLanguage.LanguageTag))
        {
            yield return profile;
        }

        OcrEngine? en = null;
        try
        {
            en = OcrEngine.TryCreateFromLanguage(new Language("en-US"));
        }
        catch
        {
            // not installed
        }

        if (en is not null && seen.Add(en.RecognizerLanguage.LanguageTag))
        {
            yield return en;
        }

        IReadOnlyList<Language> available;
        try
        {
            available = OcrEngine.AvailableRecognizerLanguages;
        }
        catch
        {
            yield break;
        }

        foreach (var lang in available)
        {
            if (seen.Add(lang.LanguageTag) && OcrEngine.TryCreateFromLanguage(lang) is { } e)
            {
                yield return e;
            }
        }
    }

    /// <summary>Recognises text; null when no OCR language is installed. Lines and words carry image-pixel boxes.</summary>
    public static async Task<List<OcrLineInfo>?> RecognizeAsync(BitmapSource image, CancellationToken ct = default)
    {
        BitmapSource src = image;
        var pixels = (long)src.PixelWidth * src.PixelHeight;
        if (pixels > MaxPixels)
        {
            var f = Math.Sqrt((double)MaxPixels / pixels);
            src = Output.Thumbnail(src, (int)(Math.Max(src.PixelWidth, src.PixelHeight) * f));
        }

        var upscale = 1;
        if (src.PixelHeight < 60)
        {
            upscale = 3;
        }
        else if (src.PixelHeight < 140)
        {
            upscale = 2;
        }

        var factor = (double)src.PixelWidth / image.PixelWidth / upscale;
        var attempts = new List<(BitmapSource Img, int Up)> { (src, 1) };
        if (upscale > 1)
        {
            attempts.Insert(0, (Upscale(src, upscale), upscale));
        }

        var any = false;
        List<OcrLineInfo>? best = null;
        foreach (var engine in Engines())
        {
            any = true;
            foreach (var (img, up) in attempts)
            {
                ct.ThrowIfCancellationRequested();
                var maxDim = (int)OcrEngine.MaxImageDimension;
                var work = img;
                var shrink = 1.0;
                if (Math.Max(work.PixelWidth, work.PixelHeight) > maxDim)
                {
                    shrink = (double)maxDim / Math.Max(work.PixelWidth, work.PixelHeight);
                    work = Output.Thumbnail(work, maxDim);
                }

                var bytes = Pixels.Get(work);
                using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(bytes.AsBuffer(), BitmapPixelFormat.Bgra8, work.PixelWidth, work.PixelHeight, BitmapAlphaMode.Premultiplied);
                var result = await engine.RecognizeAsync(bitmap);
                var lines = new List<OcrLineInfo>();
                var toImage = (1.0 / shrink) / up * (image.PixelWidth == 0 ? 1 : (double)image.PixelWidth / src.PixelWidth * src.PixelWidth / image.PixelWidth);
                _ = factor;
                var back = (double)image.PixelWidth / work.PixelWidth;
                foreach (var line in result.Lines)
                {
                    var words = line.Words.Select(w => new OcrWordInfo(w.Text, new Rect(w.BoundingRect.X * back, w.BoundingRect.Y * back, w.BoundingRect.Width * back, w.BoundingRect.Height * back))).ToList();
                    if (words.Count == 0 || string.IsNullOrWhiteSpace(line.Text))
                    {
                        continue;
                    }

                    var box = words[0].Box;
                    foreach (var w in words)
                    {
                        box.Union(w.Box);
                    }

                    lines.Add(new OcrLineInfo(TightScript(line.Text), words, box));
                }

                _ = toImage;
                if (lines.Count > 0)
                {
                    return lines;
                }

                best ??= lines;
            }
        }

        return any ? best ?? new List<OcrLineInfo>() : null;
    }

    private static BitmapSource Upscale(BitmapSource src, int factor)
    {
        var tb = new TransformedBitmap(src, new System.Windows.Media.ScaleTransform(factor, factor));
        return Pixels.Make(Pixels.Get(tb), tb.PixelWidth, tb.PixelHeight);
    }

    private static bool IsTight(int cp) =>
        cp is >= 0x3000 and <= 0x312F or >= 0x3190 and <= 0x9FFF or >= 0xF900 and <= 0xFAFF or >= 0xFF01 and <= 0xFF9F or >= 0x20000 and <= 0x2FA1F;

    private static string TightScript(string text)
    {
        // Windows OCR puts spaces between CJK characters; remove them.
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == ' ' && i > 0 && i < text.Length - 1 && IsTight(char.ConvertToUtf32(text, SafeIndex(text, i - 1))) && IsTight(char.ConvertToUtf32(text, SafeIndex(text, i + 1))))
            {
                continue;
            }

            sb.Append(text[i]);
        }

        return sb.ToString();
    }

    private static int SafeIndex(string s, int i) => char.IsLowSurrogate(s[i]) && i > 0 ? i - 1 : i;

    /// <summary>Joins recognised lines in reading order: rows (0.5 bucket tolerance) top to bottom, then left to right.</summary>
    public static string JoinLines(IEnumerable<OcrLineInfo> lines, double imageHeight, double imageWidth, bool removeLineBreaks)
    {
        var ordered = lines.Where(l => !string.IsNullOrWhiteSpace(l.Text))
            .Select(l => (Line: l, Row: l.Box.Y + l.Box.Height / 2, X: l.Box.X))
            .OrderBy(t => t.Row / Math.Max(1, imageHeight) * 50)
            .ToList();
        // stable row grouping: rows whose buckets differ by less than 0.5 are one visual row
        var rows = new List<List<(OcrLineInfo Line, double Row, double X)>>();
        foreach (var t in ordered)
        {
            var bucket = t.Row / Math.Max(1, imageHeight) * 50;
            if (rows.Count > 0 && Math.Abs(bucket - rows[^1][0].Row / Math.Max(1, imageHeight) * 50) < 0.5)
            {
                rows[^1].Add(t);
            }
            else
            {
                rows.Add([t]);
            }
        }

        var sorted = rows.Select(r => string.Join(" ", r.OrderBy(t => t.X).Select(t => t.Line.Text.Trim()))).ToList();
        if (!removeLineBreaks)
        {
            return string.Join("\n", sorted);
        }

        var sb = new StringBuilder();
        foreach (var line in sorted)
        {
            if (line.Length == 0)
            {
                continue;
            }

            if (sb.Length > 0)
            {
                var last = char.ConvertToUtf32(sb.ToString(), SafeIndex(sb.ToString(), sb.Length - 1));
                var first = char.ConvertToUtf32(line, 0);
                if (!(IsTight(last) && IsTight(first)))
                {
                    sb.Append(' ');
                }
            }

            sb.Append(line);
        }

        return sb.ToString();
    }

    // ---- QR ------------------------------------------------------------------------------------------------------

    /// <summary>Reads 2D matrix codes (QR, Aztec, Data Matrix, PDF417). 1D barcodes are deliberately ignored.</summary>
    public static QrReading? ReadQr(BitmapSource image)
    {
        try
        {
            var src = image;
            if ((long)src.PixelWidth * src.PixelHeight > MaxPixels)
            {
                src = Output.Thumbnail(src, 6000);
            }

            var bytes = Pixels.Get(src);
            var lum = new RGBLuminanceSource(bytes, src.PixelWidth, src.PixelHeight, RGBLuminanceSource.BitmapFormat.BGRA32);
            var reader = new BarcodeReaderGeneric
            {
                AutoRotate = true,
                Options = new DecodingOptions
                {
                    TryHarder = true,
                    TryInverted = true,
                    PossibleFormats = [BarcodeFormat.QR_CODE, BarcodeFormat.AZTEC, BarcodeFormat.DATA_MATRIX, BarcodeFormat.PDF_417],
                },
            };
            var results = reader.DecodeMultiple(lum);
            if (results is null || results.Length == 0)
            {
                return null;
            }

            var coded = results.Where(r => !string.IsNullOrEmpty(r.Text)).Select(r =>
            {
                var pts = r.ResultPoints ?? [];
                var x = pts.Length > 0 ? pts.Min(p => p.X) : 0;
                var y = pts.Length > 0 ? pts.Average(p => p.Y) : 0;
                return (r.Text, X: (double)x, Y: (double)y);
            }).OrderBy(t => Math.Round(t.Y / Math.Max(1, src.PixelHeight) * 50 * 2) / 2).ThenBy(t => t.X).ToList();
            if (coded.Count == 0)
            {
                return null;
            }

            var payload = string.Join("\n", coded.Select(c => c.Text));
            var url = coded.Count == 1 ? OpenableUrl(coded[0].Text) : null;
            return new QrReading(payload, url);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Only plain http / https links with a host and no user-info or whitespace may be opened (no mailto from a QR code).</summary>
    public static string? OpenableUrl(string payload)
    {
        return SafeLinks.IsWebUrl(payload.Trim(), out var uri, allowMailto: false) ? uri!.ToString() : null;
    }
}
