// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Trayloom.Core;
using Trayloom.Core.Native;

namespace Trayloom.Features.Island;

/// <summary>What the YouTube Music page reports about the song it plays (read from the page's media session and its video element).</summary>
internal sealed record HostedReading(string Title, string Artist, string ArtworkUrl, bool Paused, double Position, double Duration, bool CanNext, bool CanPrevious);

/// <summary>The saved place of the YouTube Music player window ("left,top,width,height" in device-independent pixels).</summary>
internal static class YouTubePlayerBounds
{
    /// <summary>Reads the saved text; false for anything that is not four finite numbers with a positive size.</summary>
    public static bool TryParse(string? text, out double left, out double top, out double width, out double height)
    {
        left = top = width = height = 0;
        var parts = (text ?? "").Split(',');
        if (parts.Length != 4)
        {
            return false;
        }

        var values = new double[4];
        for (var i = 0; i < 4; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || !double.IsFinite(values[i]))
            {
                return false;
            }
        }

        (left, top, width, height) = (values[0], values[1], values[2], values[3]);
        return width > 0 && height > 0;
    }

    /// <summary>The text to save.</summary>
    public static string Format(double left, double top, double width, double height) =>
        string.Join(',', new[] { left, top, width, height }.Select(v => Math.Round(v).ToString(CultureInfo.InvariantCulture)));
}

/// <summary>
/// Trayloom's own YouTube Music player: an ordinary, visible window that hosts the YouTube Music web player (WebView2), so a song chosen in
/// the island plays here instead of in a browser. The page reports its song and state about once a second and is commanded through script
/// (<c>video.play()</c>, the player bar's own buttons); <see cref="PlaybackService.SetHostedPlayer"/> turns that into a player of the island.
/// Closing the window ends playback. No account is needed or asked for.
/// </summary>
internal sealed class YouTubePlayerWindow : Window, IVirtualPlayer
{
    private const string PollScript =
        "(function(){var v=document.querySelector('video');var m=navigator.mediaSession&&navigator.mediaSession.metadata;var a='';"
        + "if(m&&m.artwork&&m.artwork.length){a=m.artwork[m.artwork.length-1].src||'';}"
        + "var n=document.querySelector('ytmusic-player-bar .next-button');var p=document.querySelector('ytmusic-player-bar .previous-button');"
        + "return JSON.stringify({title:m?(m.title||''):'',artist:m?(m.artist||''):'',art:a,has:!!(v&&v.currentSrc),"
        + "paused:v?v.paused:true,pos:v?v.currentTime:0,dur:(v&&isFinite(v.duration))?v.duration:0,next:!!n,prev:!!p});})()";

    private const string UserDataFolderName = "WebView2";

    private readonly IslandCore _core;
    private readonly WebView2 _web = new() { DefaultBackgroundColor = System.Drawing.Color.Black };
    private readonly DispatcherTimer _poll = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _saveDelay = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(800) };
    private string? _pending;
    private bool _ready;
    private bool _polling;
    private bool _closed;

    public YouTubePlayerWindow(IslandCore core)
    {
        _core = core;
        Title = "YouTube Music";
        Width = 960;
        Height = 640;
        MinWidth = 480;
        MinHeight = 360;
        Background = Brushes.Black;
        Content = _web;
        ApplyBounds();
        _poll.Tick += (_, _) => PollNow();
        _saveDelay.Tick += (_, _) =>
        {
            _saveDelay.Stop();
            SaveBounds();
        };
        SizeChanged += (_, _) => ScheduleSave();
        LocationChanged += (_, _) => ScheduleSave();
        Loaded += async (_, _) => await InitializeAsync();
        Closing += (_, _) =>
        {
            SaveBounds();
            _poll.Stop();
            _saveDelay.Stop();

            // the page stops before the window goes: closing the window ends the music
            try
            {
                _web.CoreWebView2?.Navigate("about:blank");
            }
            catch
            {
                // the view is already gone
            }
        };
        Closed += (_, _) =>
        {
            _closed = true;
            _core.Playback.SetHostedPlayer(null, null);
            try
            {
                _web.Dispose();
            }
            catch
            {
                // nothing left to release
            }

            Ended?.Invoke();
        };
    }

    /// <summary>Raised when the window has closed.</summary>
    public event Action? Ended;

    /// <summary>Loads a YouTube Music address (https, music.youtube.com only) and brings the window forward.</summary>
    public void Load(string url)
    {
        if (!YouTubePlayerHost.IsAllowed(url))
        {
            return;
        }

        _pending = url;
        if (_ready)
        {
            NavigatePending();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        Activate();
    }

    public void Play() => Run("(function(){var v=document.querySelector('video');if(v){v.play();}})()");

    public void Pause() => Run("(function(){var v=document.querySelector('video');if(v){v.pause();}})()");

    public void Next() => Run("(function(){var b=document.querySelector('ytmusic-player-bar .next-button');if(b){b.click();}})()");

    public void Previous() => Run("(function(){var b=document.querySelector('ytmusic-player-bar .previous-button');if(b){b.click();}})()");

    public void Seek(double seconds)
    {
        if (double.IsFinite(seconds))
        {
            Run("(function(){var v=document.querySelector('video');if(v){v.currentTime=" + Math.Max(0, seconds).ToString("0.###", CultureInfo.InvariantCulture) + ";}})()");
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Trayloom", UserDataFolderName);
            Directory.CreateDirectory(folder);

            // a song picked in the island starts without a click inside the page
            var options = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required" };
            var env = await CoreWebView2Environment.CreateAsync(null, folder, options);
            await _web.EnsureCoreWebView2Async(env);
            if (_closed)
            {
                return;
            }

            var core = _web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.NewWindowRequested += (_, e) =>
            {
                // links that want a window of their own stay in this one when they are YouTube Music's, and are dropped otherwise
                e.Handled = true;
                if (YouTubePlayerHost.IsAllowed(e.Uri))
                {
                    core.Navigate(e.Uri);
                }
            };
            _ready = true;
            NavigatePending();
            _poll.Start();
        }
        catch (Exception ex)
        {
            _core.Ctx.Log.Warn($"[island] YouTube Music player could not start: {ex.Message}");
        }
    }

    private void NavigatePending()
    {
        if (_pending is not { } url || _web.CoreWebView2 is null)
        {
            return;
        }

        _pending = null;
        try
        {
            _web.CoreWebView2.Navigate(url);
        }
        catch (Exception ex)
        {
            _core.Ctx.Log.Warn($"[island] YouTube Music player could not open the page: {ex.Message}");
        }
    }

    private async void Run(string script)
    {
        try
        {
            if (_ready && !_closed && _web.CoreWebView2 is { } core)
            {
                await core.ExecuteScriptAsync(script);

                // the page answers a moment later: the island's state follows without waiting for the next tick
                await Task.Delay(250);
                PollNow();
            }
        }
        catch (Exception ex)
        {
            _core.Ctx.Log.Warn($"[island] YouTube Music command failed: {ex.Message}");
        }
    }

    /// <summary>Reads the page's song and state and gives them to the island (one read at a time).</summary>
    private async void PollNow()
    {
        if (!_ready || _closed || _polling || _web.CoreWebView2 is not { } core)
        {
            return;
        }

        _polling = true;
        try
        {
            var raw = await core.ExecuteScriptAsync(PollScript);
            if (_closed)
            {
                return;
            }

            var json = JsonSerializer.Deserialize<string>(raw);
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var title = Text(root, "title");
            var has = root.TryGetProperty("has", out var h) && h.ValueKind == JsonValueKind.True;
            if (title.Length == 0 && !has)
            {
                // nothing loaded yet (the home page): there is no song to show
                _core.Playback.SetHostedPlayer(null, null);
                return;
            }

            var reading = new HostedReading(
                title,
                Text(root, "artist"),
                Text(root, "art"),
                !root.TryGetProperty("paused", out var p) || p.ValueKind != JsonValueKind.False,
                Number(root, "pos"),
                Number(root, "dur"),
                root.TryGetProperty("next", out var n) && n.ValueKind == JsonValueKind.True,
                root.TryGetProperty("prev", out var pr) && pr.ValueKind == JsonValueKind.True);
            _core.Playback.SetHostedPlayer(this, reading);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or System.Runtime.InteropServices.COMException or ObjectDisposedException)
        {
            // the page is between two documents: the next tick reads it again
        }
        finally
        {
            _polling = false;
        }
    }

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";

    private static double Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && double.IsFinite(d) ? d : 0;

    // ---------------------------------------------------------------- place

    /// <summary>The saved place, kept inside a monitor's work area; the middle of the screen without one.</summary>
    private void ApplyBounds()
    {
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (!YouTubePlayerBounds.TryParse(_core.App.Get<string>(K.YouTubePlayerBounds), out var left, out var top, out var width, out var height))
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var saved = new RECT
        {
            Left = (int)(left * dpi.DpiScaleX),
            Top = (int)(top * dpi.DpiScaleY),
            Right = (int)((left + width) * dpi.DpiScaleX),
            Bottom = (int)((top + height) * dpi.DpiScaleY),
        };
        var safe = WindowPlacementMath.Sanitize(saved, WindowPlacementMath.GetMonitors());
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = safe.Left / dpi.DpiScaleX;
        Top = safe.Top / dpi.DpiScaleY;
        Width = (safe.Right - safe.Left) / dpi.DpiScaleX;
        Height = (safe.Bottom - safe.Top) / dpi.DpiScaleY;
    }

    private void ScheduleSave()
    {
        _saveDelay.Stop();
        _saveDelay.Start();
    }

    private void SaveBounds()
    {
        if (WindowState != WindowState.Normal)
        {
            return;
        }

        _core.App.Set(K.YouTubePlayerBounds, YouTubePlayerBounds.Format(Left, Top, Width, Height));
    }
}

/// <summary>Owns the YouTube Music player window of one island: opens it, reuses it, and closes it with the island.</summary>
internal sealed class YouTubePlayerHost : IDisposable
{
    private readonly IslandCore _core;
    private YouTubePlayerWindow? _window;
    private bool _runtimeKnown;

    public YouTubePlayerHost(IslandCore core)
    {
        _core = core;
    }

    /// <summary>The Microsoft Edge WebView2 runtime is installed (checked again until it is found).</summary>
    public bool RuntimeAvailable
    {
        get
        {
            if (_runtimeKnown)
            {
                return true;
            }

            try
            {
                _runtimeKnown = !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
            }
            catch (Exception ex)
            {
                _core.Ctx.Log.Info($"[island] WebView2 runtime not available: {ex.GetType().Name}");
                _runtimeKnown = false;
            }

            return _runtimeKnown;
        }
    }

    /// <summary>Plays <paramref name="url"/> in the player window (created on first use); false when the runtime is missing.</summary>
    public bool Open(string url)
    {
        if (!IsAllowed(url) || !RuntimeAvailable)
        {
            return false;
        }

        if (_window is null)
        {
            var window = new YouTubePlayerWindow(_core);
            window.Ended += () =>
            {
                if (ReferenceEquals(_window, window))
                {
                    _window = null;
                }
            };
            _window = window;
        }

        _window.Load(url);
        return true;
    }

    /// <summary>Brings the player window forward (nothing when it is not open).</summary>
    public void Show()
    {
        if (_window is { } window)
        {
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            window.Show();
            window.Activate();
        }
    }

    /// <summary>Only https addresses on YouTube Music itself are loaded in the player.</summary>
    public static bool IsAllowed(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(uri.UserInfo)
        && (uri.IdnHost.Equals("music.youtube.com", StringComparison.OrdinalIgnoreCase) || uri.IdnHost.Equals("consent.youtube.com", StringComparison.OrdinalIgnoreCase));

    public void Dispose()
    {
        var window = _window;
        _window = null;
        if (window is not null)
        {
            try
            {
                window.Close();
            }
            catch
            {
                // already closed
            }
        }
    }
}
