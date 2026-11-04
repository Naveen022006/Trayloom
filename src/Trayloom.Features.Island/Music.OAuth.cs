// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Trayloom.Features.Island;

/// <summary>PKCE (RFC 7636) and OAuth state helpers. Pure apart from the random source.</summary>
internal static class Pkce
{
    /// <summary>A code verifier: 64 random bytes as unpadded base64url (86 characters, well inside the 43..128 the RFC allows).</summary>
    public static string CreateVerifier() => Base64Url(RandomNumberGenerator.GetBytes(64));

    /// <summary>The S256 code challenge of <paramref name="verifier"/>: base64url of its SHA-256.</summary>
    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    /// <summary>An unguessable <c>state</c> value: 32 random bytes as unpadded base64url.</summary>
    public static string CreateState() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>Compares two state values in constant time; empty values never match.</summary>
    public static bool StateMatches(string? expected, string? actual)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(actual))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>What an HTTP request line to the loopback redirect asked for.</summary>
internal enum OAuthKind
{
    /// <summary>Not the redirect (another path or method, or no answer in it): answered with 404.</summary>
    Ignore,

    /// <summary>The provider returned an authorization code.</summary>
    Code,

    /// <summary>The provider returned an error (the user refused, for one).</summary>
    Error,
}

/// <summary>The parsed redirect request. Holds secrets while it lives: never log it.</summary>
internal readonly record struct OAuthCallback(OAuthKind Kind, string Code, string State, string Error)
{
    /// <summary>The path the redirect URI points at.</summary>
    public const string CallbackPath = "/callback";

    /// <summary>
    /// Reads a request line such as <c>GET /callback?code=…&amp;state=… HTTP/1.1</c>. Only <c>GET /callback</c> counts; values are
    /// percent-decoded and the first of a repeated key wins. An <c>error</c> beats a <c>code</c>.
    /// </summary>
    public static OAuthCallback Parse(string? requestLine)
    {
        var ignore = new OAuthCallback(OAuthKind.Ignore, "", "", "");
        if (string.IsNullOrWhiteSpace(requestLine))
        {
            return ignore;
        }

        var parts = requestLine.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts[0] != "GET")
        {
            return ignore;
        }

        var target = parts[1];
        var fragment = target.IndexOf('#');
        if (fragment >= 0)
        {
            target = target[..fragment];
        }

        var question = target.IndexOf('?');
        var path = question < 0 ? target : target[..question];
        if (path != CallbackPath || question < 0)
        {
            return ignore;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in target[(question + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=');
            var key = Decode(equals < 0 ? pair : pair[..equals]);
            var value = equals < 0 ? "" : Decode(pair[(equals + 1)..]);
            values.TryAdd(key, value);
        }

        values.TryGetValue("state", out var state);
        if (values.TryGetValue("error", out var error) && error.Length > 0)
        {
            return new OAuthCallback(OAuthKind.Error, "", state ?? "", error);
        }

        if (values.TryGetValue("code", out var code) && code.Length > 0)
        {
            return new OAuthCallback(OAuthKind.Code, code, state ?? "", "");
        }

        return ignore;
    }

    private static string Decode(string text) => Uri.UnescapeDataString(text.Replace('+', ' '));
}

/// <summary>Another program already listens on the redirect port.</summary>
internal sealed class PortBusyException : Exception
{
    public PortBusyException()
        : base("The sign-in port is in use.")
    {
    }
}

/// <summary>
/// The one-shot web server behind <c>http://127.0.0.1:PORT/callback</c>. It binds to the loopback address only (a raw
/// <see cref="TcpListener"/>, because <c>HttpListener</c> needs a URL reservation), answers every other path with 404 and hands the
/// callback to the caller together with a way to answer it; the browser's connection stays open until that answer is written.
/// </summary>
internal sealed class LoopbackRedirect : IAsyncDisposable
{
    private const int MaxHeaderBytes = 8 * 1024;
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(5);

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly List<TcpClient> _clients = [];
    private bool _answered;
    private int _disposed;

    private LoopbackRedirect(TcpListener listener) => _listener = listener;

    /// <summary>The port actually bound (differs from the requested one only for port 0, which tests use).</summary>
    public int LocalPort => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Starts listening on 127.0.0.1:<paramref name="port"/>. Call before the browser opens, so a busy port is known first.</summary>
    /// <exception cref="PortBusyException">The port is taken.</exception>
    public static LoopbackRedirect Start(int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, port)
        {
            // Without exclusive use another process could bind the same port next to us and receive the code instead.
            ExclusiveAddressUse = true,
        };
        try
        {
            listener.Start(4);
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied)
        {
            throw new PortBusyException();
        }

        return new LoopbackRedirect(listener);
    }

    /// <summary>
    /// Waits for the provider's redirect. Returns the parsed request and a function that writes the final page (an HTML document) and
    /// closes the connection. Throws <see cref="TimeoutException"/> after <paramref name="timeout"/> and
    /// <see cref="OperationCanceledException"/> when <paramref name="ct"/> ends or the listener is disposed.
    /// </summary>
    public async Task<(OAuthCallback Result, Func<string, Task> RespondAsync)> WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<(OAuthCallback, Func<string, Task>)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timer = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _stop.Token);
        using var onTimeout = timer.Token.Register(() => tcs.TrySetException(new TimeoutException()));
        using var onCancel = linked.Token.Register(() => tcs.TrySetCanceled(linked.Token));

        // The accept loop ends with the wait; a connection that already carries the callback keeps living on its own.
        using var acceptStop = CancellationTokenSource.CreateLinkedTokenSource(linked.Token, timer.Token);
        var loop = AcceptLoopAsync(tcs, acceptStop.Token);
        try
        {
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            acceptStop.Cancel();
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    /// <summary>The page shown in the browser: a minimal self-contained document; both texts are HTML-encoded here.</summary>
    public static string Page(string title, string message)
    {
        var t = WebUtility.HtmlEncode(title);
        var m = WebUtility.HtmlEncode(message);
        return "<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
            + "<title>" + t + "</title><style>body{font-family:Segoe UI,system-ui,sans-serif;background:#121212;color:#f2f2f2;display:flex;"
            + "align-items:center;justify-content:center;min-height:100vh;margin:0}main{max-width:420px;padding:32px;text-align:center}"
            + "h1{font-size:20px;margin:0 0 12px}p{font-size:15px;line-height:1.5;margin:0;color:#c8c8c8}</style></head>"
            + "<body><main><h1>" + t + "</h1><p>" + m + "</p></main></body></html>";
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        _stop.Cancel();
        try
        {
            _listener.Stop();
        }
        catch (SocketException)
        {
        }

        TcpClient[] open;
        lock (_gate)
        {
            open = [.. _clients];
            _clients.Clear();
        }

        foreach (var client in open)
        {
            client.Dispose();
        }

        _stop.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task AcceptLoopAsync(TaskCompletionSource<(OAuthCallback, Func<string, Task>)> tcs, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException or InvalidOperationException)
            {
                return;
            }

            lock (_gate)
            {
                _clients.Add(client);
            }

            // One task per connection, so a client that never finishes its request cannot block the next one.
            _ = Task.Run(() => HandleAsync(client, tcs));
        }
    }

    private async Task HandleAsync(TcpClient client, TaskCompletionSource<(OAuthCallback, Func<string, Task>)> tcs)
    {
        var keepOpen = false;
        try
        {
            var stream = client.GetStream();
            string? requestLine;
            using (var read = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
            {
                read.CancelAfter(ReadTimeout);
                requestLine = await ReadRequestLineAsync(stream, read.Token).ConfigureAwait(false);
            }

            var parsed = OAuthCallback.Parse(requestLine);
            if (parsed.Kind != OAuthKind.Ignore && TryClaim())
            {
                keepOpen = true;
                if (!tcs.TrySetResult((parsed, html => WriteAsync(client, stream, 200, "OK", html))))
                {
                    // The wait ended meanwhile (timeout or cancel): nobody will answer this connection.
                    keepOpen = false;
                }

                return;
            }

            await WriteAsync(client, stream, 404, "Not Found", Page("Not found", "Nothing here.")).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            // A browser that hangs up early, a stalled request or shutdown: nothing to report, and nothing in them is worth logging.
        }
        finally
        {
            if (!keepOpen)
            {
                Close(client);
            }
        }
    }

    /// <summary>Only the first valid callback is handed out; later ones are answered like any other stray request.</summary>
    private bool TryClaim()
    {
        lock (_gate)
        {
            if (_answered)
            {
                return false;
            }

            _answered = true;
            return true;
        }
    }

    private static async Task<string?> ReadRequestLineAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[MaxHeaderBytes];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), ct).ConfigureAwait(false);
            if (read == 0)
            {
                return null;
            }

            length += read;
            if (buffer.AsSpan(0, length).IndexOf("\r\n\r\n"u8) >= 0)
            {
                var end = buffer.AsSpan(0, length).IndexOf("\r\n"u8);
                return Encoding.ASCII.GetString(buffer, 0, end);
            }
        }

        // 8 KB of headers without an end: not a browser we want to talk to.
        return null;
    }

    private async Task WriteAsync(TcpClient client, NetworkStream stream, int status, string reason, string html)
    {
        try
        {
            var body = Encoding.UTF8.GetBytes(html);
            var head = new StringBuilder()
                .Append("HTTP/1.1 ").Append(status).Append(' ').Append(reason).Append("\r\n")
                .Append("Content-Type: text/html; charset=utf-8\r\n")
                .Append("Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'\r\n")
                .Append("Cache-Control: no-store\r\n")
                .Append("X-Content-Type-Options: nosniff\r\n")
                .Append("Referrer-Policy: no-referrer\r\n")
                .Append("Content-Length: ").Append(body.Length).Append("\r\n")
                .Append("Connection: close\r\n\r\n")
                .ToString();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            timeout.CancelAfter(WriteTimeout);
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head), timeout.Token).ConfigureAwait(false);
            await stream.WriteAsync(body, timeout.Token).ConfigureAwait(false);
            await stream.FlushAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            // The browser went away before it read the page: the sign-in itself already succeeded or failed.
        }
        finally
        {
            Close(client);
        }
    }

    private void Close(TcpClient client)
    {
        lock (_gate)
        {
            _clients.Remove(client);
        }

        try
        {
            client.Dispose();
        }
        catch (SocketException)
        {
        }
    }
}
