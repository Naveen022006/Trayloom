// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Trayloom.Features.Tools;

/// <summary>"Fetch Website Icon": downloads /favicon.ico once (explicit button press only), resamples it to 64x64 PNG (spec 3.9).</summary>
internal static class RadialFavicon
{
    private const int MaxBytes = 2 * 1024 * 1024;

    /// <summary>Returns the base64 PNG, or null when no usable icon was found. Call from the UI thread.</summary>
    public static async Task<string?> FetchAsync(string normalizedUrl)
    {
        try
        {
            if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host))
            {
                return null;
            }

            var origin = new UriBuilder(uri.Scheme, uri.Host, uri.IsDefaultPort ? -1 : uri.Port) { Path = "/favicon.ico" }.Uri;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var bytes = await Task.Run(() => DownloadAsync(origin, cts.Token));
            if (bytes is null)
            {
                return null;
            }

            return Render(bytes);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<byte[]?> DownloadAsync(Uri start, CancellationToken token)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var current = start;
        for (var hop = 0; hop < 5; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.TryAddWithoutValidation("Accept", "image/*");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            var code = (int)response.StatusCode;
            if (code is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                if (next.Scheme != start.Scheme || next.Host != start.Host || next.Port != start.Port)
                {
                    return null;
                }

                current = next;
                continue;
            }

            if (code is < 200 or > 299)
            {
                return null;
            }

            if (response.Content.Headers.ContentLength is > MaxBytes)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var ms = new MemoryStream();
            var buffer = new byte[16384];
            int read;
            while ((read = await stream.ReadAsync(buffer, token)) > 0)
            {
                ms.Write(buffer, 0, read);
                if (ms.Length > MaxBytes)
                {
                    return null;
                }
            }

            return ms.Length > 0 ? ms.ToArray() : null;
        }

        return null;
    }

    private static string? Render(byte[] data)
    {
        BitmapDecoder decoder;
        try
        {
            decoder = BitmapDecoder.Create(new MemoryStream(data), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        }
        catch
        {
            return null;
        }

        var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth * f.PixelHeight).FirstOrDefault();
        if (frame is null || frame.PixelWidth == 0 || frame.PixelHeight == 0 || frame.PixelWidth > 4096 || frame.PixelHeight > 4096
            || (long)frame.PixelWidth * frame.PixelHeight > 16_777_216)
        {
            return null;
        }

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(frame, new Rect(0, 0, 64, 64));
        }

        var target = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.Length > 65536 ? null : Convert.ToBase64String(output.ToArray());
    }
}
