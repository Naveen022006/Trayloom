// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Trayloom.Features.Island;

/// <summary>Artwork of streaming rows: host allow-list, size-capped download, decode off the UI thread, LRU of frozen images.</summary>
internal static class StreamArt
{
    private const int MaxBytes = 2 * 1024 * 1024;
    private const int MaxEntries = 300;

    private static readonly object Gate = new();
    private static readonly Dictionary<string, LinkedListNode<(string Url, ImageSource Image)>> Cache = new(StringComparer.Ordinal);
    private static readonly LinkedList<(string Url, ImageSource Image)> Order = new();
    private static readonly HashSet<string> Failed = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, List<Action<ImageSource>>> Waiting = new(StringComparer.Ordinal);
    private static HttpClient? s_http;

    /// <summary>True for https addresses on the hosts Spotify and YouTube serve artwork from.</summary>
    public static bool Allowed(string? url)
    {
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        var host = uri.IdnHost.ToLowerInvariant();
        return host.EndsWith(".scdn.co", StringComparison.Ordinal)
            || host.EndsWith(".spotifycdn.com", StringComparison.Ordinal)
            || host == "i.ytimg.com"
            || host.EndsWith(".ggpht.com", StringComparison.Ordinal);
    }

    /// <summary>
    /// Gives the image for <paramref name="url"/> to <paramref name="done"/> on the UI thread, now when cached. Disallowed addresses,
    /// failures and oversized files never call back (and a failure is remembered, so it is not retried).
    /// </summary>
    public static void Load(string url, Action<ImageSource> done)
    {
        if (!Allowed(url))
        {
            return;
        }

        lock (Gate)
        {
            if (Failed.Contains(url))
            {
                return;
            }

            if (Cache.TryGetValue(url, out var node))
            {
                Order.Remove(node);
                Order.AddFirst(node);
                var image = node.Value.Image;
                Deliver(() => done(image));
                return;
            }

            if (Waiting.TryGetValue(url, out var list))
            {
                list.Add(done);
                return;
            }

            Waiting[url] = [done];
        }

        _ = Task.Run(() => Fetch(url));
    }

    private static async Task Fetch(string url)
    {
        ImageSource? image = null;
        try
        {
            s_http ??= ApiHttp.Create();
            using var timeout = ApiHttp.Timeout(CancellationToken.None);
            using var response = await s_http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode && !(response.Content.Headers.ContentLength > MaxBytes))
            {
                var bytes = await ReadCapped(response, timeout.Token).ConfigureAwait(false);
                if (bytes is not null)
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 64;
                    bitmap.StreamSource = new MemoryStream(bytes);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    image = bitmap;
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or NotSupportedException or OperationCanceledException or FileFormatException or InvalidOperationException)
        {
            image = null;
        }

        List<Action<ImageSource>> callbacks;
        lock (Gate)
        {
            Waiting.Remove(url, out callbacks!);
            if (image is null)
            {
                Failed.Add(url);
            }
            else
            {
                Cache[url] = Order.AddFirst((url, image));
                while (Order.Count > MaxEntries)
                {
                    var last = Order.Last!;
                    Order.RemoveLast();
                    Cache.Remove(last.Value.Url);
                }
            }
        }

        if (image is not null)
        {
            var final = image;
            Deliver(() =>
            {
                foreach (var callback in callbacks)
                {
                    callback(final);
                }
            });
        }
    }

    private static async Task<byte[]?> ReadCapped(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static void Deliver(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }
}
