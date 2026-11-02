// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Collections.Specialized;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinKnife.Core;

namespace WinKnife.Features.Capture;

/// <summary>A finished capture: pixels plus the display scale (pixels per DIP) and the screen rectangle it came from.</summary>
internal sealed record Capture(BitmapSource Image, double Scale, Int32Rect Anchor);

/// <summary>File naming, destination and PNG/clipboard writers shared by Screenshot, Recorder and Pins.</summary>
internal static class Output
{
    private static readonly CultureInfo Posix = CultureInfo.InvariantCulture;

    public static string DesktopFolder() => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    public static string DataRoot() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinKnife");

    /// <summary>Base folder from a "Save to" setting: the folder if it exists, else the Desktop.</summary>
    public static string BaseFolder(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var expanded = Environment.ExpandEnvironmentVariables(configured.Trim());
            if (Directory.Exists(expanded))
            {
                return expanded;
            }
        }

        var desktop = DesktopFolder();
        return Directory.Exists(desktop) ? desktop : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public static string FolderDisplayName(string? configured) =>
        string.IsNullOrWhiteSpace(configured) ? "Desktop" : Path.GetFileName(configured.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : configured;

    // ---- naming patterns ---------------------------------------------------------------------------------

    /// <summary>Expands %year %month %y %mo %d %h %mi %s (longer tokens first).</summary>
    public static string ExpandTokens(string pattern, DateTime now)
    {
        var s = pattern;
        s = s.Replace("%year", now.ToString("yyyy", Posix));
        s = s.Replace("%month", now.ToString("MMMM", new CultureInfo("en-US")));
        s = s.Replace("%mi", now.ToString("mm", Posix));
        s = s.Replace("%mo", now.ToString("MM", Posix));
        s = s.Replace("%y", now.ToString("yy", Posix));
        s = s.Replace("%d", now.ToString("dd", Posix));
        s = s.Replace("%h", now.ToString("HH", Posix));
        s = s.Replace("%s", now.ToString("ss", Posix));
        return s;
    }

    /// <summary>Replaces characters that are not valid in a file name.</summary>
    public static string SafeName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(c is '/' or ':' or '\\' or '*' or '?' or '"' or '<' or '>' or '|' ? '-' : c);
        }

        return sb.ToString().Trim().TrimEnd('.');
    }

    public static string DefaultName(string prefix, DateTime now) =>
        $"{prefix} {now.ToString("yyyy-MM-dd", Posix)} at {now.ToString("HH.mm.ss", Posix)}";

    /// <summary>Replaces the %#... number token with the zero-padded counter.</summary>
    public static string ApplyNumber(string pattern, int number)
    {
        var i = pattern.IndexOf("%#", StringComparison.Ordinal);
        if (i < 0)
        {
            return pattern;
        }

        var j = i + 1;
        while (j < pattern.Length && pattern[j] == '#')
        {
            j++;
        }

        var digits = j - i - 1;
        return pattern[..i] + number.ToString("D" + Math.Max(1, digits), Posix) + pattern[j..];
    }

    /// <summary>Builds the destination folder (with the optional dated subfolder) for a screenshot.</summary>
    public static string ScreenshotFolder(IAppSettings s, DateTime now)
    {
        var folder = BaseFolder(s.Get<string>(K.SaveFolder));
        var sub = (s.Get<string>(K.SaveSubfolder) ?? string.Empty).Trim();
        if (sub.Length == 0)
        {
            return folder;
        }

        var parts = ExpandTokens(sub, now).Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Select(p => SafeName(p)).Where(p => p.Length > 0 && p != "." && p != "..");
        var path = folder;
        foreach (var p in parts)
        {
            path = Path.Combine(path, p);
        }

        try
        {
            Directory.CreateDirectory(path);
            return path;
        }
        catch
        {
            return folder;
        }
    }

    /// <summary>The unique target path for the next screenshot; consumes a counter value when the pattern has %#.</summary>
    public static (string Path, bool ConsumedNumber, int Number) NextScreenshotPath(IAppSettings s, DateTime now)
    {
        var folder = ScreenshotFolder(s, now);
        var pattern = (s.Get<string>(K.FileNamePattern) ?? string.Empty).Trim();
        string name;
        var consumed = false;
        var number = 0;
        if (pattern.Length == 0)
        {
            name = DefaultName(Strings.Screenshot.FileNamePrefix, now);
        }
        else
        {
            var expanded = ExpandTokens(pattern, now);
            if (expanded.Contains("%#", StringComparison.Ordinal))
            {
                number = Math.Max(0, s.Get<int>(K.FileNumberNext));
                expanded = ApplyNumber(expanded, number);
                consumed = true;
                s.Set(K.FileNumberNext, number + 1);
            }

            name = SafeName(expanded);
            if (name.Length == 0)
            {
                name = DefaultName(Strings.Screenshot.FileNamePrefix, now);
            }
        }

        return (UniquePath(folder, name, ".png"), consumed, number);
    }

    public static string UniquePath(string folder, string name, string ext)
    {
        var path = Path.Combine(folder, name + ext);
        for (var i = 2; File.Exists(path) && i <= 9999; i++)
        {
            path = Path.Combine(folder, $"{name} {i}{ext}");
        }

        return path;
    }

    // ---- PNG / clipboard ---------------------------------------------------------------------------------

    public static byte[] ToPng(BitmapSource bitmap, double scale = 1)
    {
        var src = scale > 0 && Math.Abs(bitmap.DpiX - 96 * scale) > 0.01 ? WithDpi(bitmap, 96 * scale) : bitmap;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(src));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    public static BitmapSource WithDpi(BitmapSource bitmap, double dpi)
    {
        var px = Pixels.Get(bitmap);
        return Pixels.Make(px, bitmap.PixelWidth, bitmap.PixelHeight, dpi, dpi);
    }

    public static void SavePng(BitmapSource bitmap, string path, double scale = 1)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
        File.WriteAllBytes(tmp, ToPng(bitmap, scale));
        File.Move(tmp, path, true);
        MarkAsCapture(path);
    }

    private static void MarkAsCapture(string path)
    {
        try
        {
            File.WriteAllText(path + ":WinKnife.ScreenCapture", "1");
        }
        catch
        {
            // best effort (non-NTFS volumes)
        }
    }

    /// <summary>Downscales high-DPI captures to their logical size when the user asked for 1x.</summary>
    public static (BitmapSource Image, double Scale) ApplyDownscale(IAppSettings s, BitmapSource image, double scale)
    {
        if (!s.Get<bool>(K.Downscale) || scale <= 1.001)
        {
            return (image, scale);
        }

        var w = Math.Max(1, (int)Math.Round(image.PixelWidth / scale));
        var h = Math.Max(1, (int)Math.Round(image.PixelHeight / scale));
        var tb = new TransformedBitmap(image, new ScaleTransform((double)w / image.PixelWidth, (double)h / image.PixelHeight));
        RenderOptions.SetBitmapScalingMode(tb, BitmapScalingMode.Fant);
        var px = Pixels.Get(tb);
        return (Pixels.Make(px, tb.PixelWidth, tb.PixelHeight), 1);
    }

    private static string CopiedFolder() => Path.Combine(DataRoot(), "Cache", "Copied Screenshots");

    /// <summary>Puts an image on the clipboard: PNG + DIB (+ a file reference unless image-only).</summary>
    public static bool CopyImage(BitmapSource image, double scale, bool imageOnly, string? namePrefix = null)
    {
        try
        {
            var data = new DataObject();
            data.SetData("PNG", new MemoryStream(ToPng(image, scale)));
            data.SetImage(image);
            data.SetData("WinKnife.Source", "1");
            if (!imageOnly)
            {
                var folder = CopiedFolder();
                Directory.CreateDirectory(folder);
                var file = UniquePath(folder, DefaultName(namePrefix ?? Strings.Screenshot.FileNamePrefix, DateTime.Now), ".png");
                File.WriteAllBytes(file, ToPng(image, scale));
                data.SetFileDropList(new StringCollection { file });
                PruneCopied(folder, file);
            }

            SetClipboardRetry(data);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void SetClipboardRetry(IDataObject data)
    {
        for (var i = 0; i < 6; i++)
        {
            try
            {
                Clipboard.SetDataObject(data, true);
                return;
            }
            catch (COMException)
            {
                Thread.Sleep(25);
            }
        }

        Clipboard.SetDataObject(data, true);
    }

    public static bool CopyText(string text)
    {
        try
        {
            var data = new DataObject();
            data.SetText(text);
            data.SetData("WinKnife.Source", "1");
            SetClipboardRetry(data);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void PruneCopied(string folder, string keep)
    {
        try
        {
            var files = new DirectoryInfo(folder).GetFiles().OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            long total = 0;
            var count = 0;
            foreach (var f in files)
            {
                count++;
                total += f.Length;
                if (string.Equals(f.FullName, keep, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (f.LastWriteTimeUtc < DateTime.UtcNow.AddHours(-24) || count > 100 || total > 256L * 1024 * 1024)
                {
                    try
                    {
                        f.Delete();
                    }
                    catch
                    {
                        // in use
                    }
                }
            }
        }
        catch
        {
            // cache housekeeping only
        }
    }

    /// <summary>Moves a file to the Recycle Bin.</summary>
    public static void Recycle(string path)
    {
        try
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }
        catch
        {
            // already gone
        }
    }

    public static BitmapSource? LoadImage(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public static double ScaleOf(BitmapSource bmp)
    {
        var s = bmp.DpiX / 96.0;
        return s is >= 0.5 and <= 4 ? s : 1;
    }

    public static BitmapSource Thumbnail(BitmapSource image, int maxSide)
    {
        var longest = Math.Max(image.PixelWidth, image.PixelHeight);
        if (longest <= maxSide)
        {
            return image;
        }

        var f = (double)maxSide / longest;
        var tb = new TransformedBitmap(image, new ScaleTransform(f, f));
        var px = Pixels.Get(tb);
        return Pixels.Make(px, tb.PixelWidth, tb.PixelHeight);
    }
}
