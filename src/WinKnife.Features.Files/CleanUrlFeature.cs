// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text;
using WinKnife.Core;

namespace WinKnife.Features.Files;

/// <summary>What the Clean URL settings page and panel ask of the running feature.</summary>
internal interface ICleanUrlHost
{
    bool IsRunning { get; }

    /// <summary>Names the last automatic clean took out (empty until one happened).</summary>
    IReadOnlyList<string> LastRemoved { get; }

    event Action? Changed;

    /// <summary>Writes a cleaned link to the clipboard (marked as WinKnife's own write). Returns false when the clipboard is busy.</summary>
    bool CopyCleaned(string url);
}

/// <summary>Removes tracking parameters (utm_*, fbclid, gclid, ...) from links as soon as they are copied.</summary>
public sealed class CleanUrlFeature : IFeature, ICleanUrlHost
{
    private IFeatureContext? _context;
    private IDisposable? _subscription;
    private List<string> _lastRemoved = [];

    public string Id => "clean-url";

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("clean-clipboard", "Clean the copied link", CleanNow, ""),
    ];

    public event Action? Changed;

    public bool IsRunning => _context is not null && _context.App.Get<bool>("urlCleanerEnabled");

    public IReadOnlyList<string> LastRemoved => _lastRemoved;

    internal static void RegisterDefaults(IAppSettings app)
    {
        app.Register("urlCleanerEnabled", false);
        app.Register("urlCleanerCustomParameters", string.Empty);
        app.Register("urlCleanerSiteParameters", string.Empty);
        app.Register("urlCleanerDisabledParameters", string.Empty);
        app.Register("panelUtilityURLCleaner", true);
    }

    public void Start(IFeatureContext context)
    {
        _context = context;
        RegisterDefaults(context.App);
        _subscription = ClipboardMonitor.Subscribe(OnClipboardChanged);
        context.App.Changed += OnSettingChanged;
        Hub.CleanUrl = this;
    }

    public void Stop()
    {
        if (_context is not null)
        {
            _context.App.Changed -= OnSettingChanged;
        }

        if (ReferenceEquals(Hub.CleanUrl, this))
        {
            Hub.CleanUrl = null;
        }

        _subscription?.Dispose();
        _subscription = null;
        _context = null;
    }

    private void OnSettingChanged(string key)
    {
        if (key == "urlCleanerEnabled")
        {
            _context?.Ui.BeginInvoke(() => Changed?.Invoke());
        }
    }

    public bool CopyCleaned(string url) => ClipboardIO.WriteText(url, markOwn: false);

    private async void OnClipboardChanged()
    {
        if (_context is null || !_context.App.Get<bool>("urlCleanerEnabled"))
        {
            return;
        }

        var sequence = WinApi.GetClipboardSequenceNumber();
        try
        {
            // Let the source application finish writing (and any follow-up changes settle) before reading.
            await Task.Delay(40);
            if (_context is null || WinApi.GetClipboardSequenceNumber() != sequence)
            {
                return;
            }

            var result = await Task.Run(() => TryCleanAutomatically(sequence));
            if (result is { Count: > 0 })
            {
                _lastRemoved = result;
                Changed?.Invoke();
            }
        }
        catch (Exception ex)
        {
            _context?.Log.Error("Clean URL failed", ex);
        }
    }

    /// <summary>Background thread: returns the removed names when the clipboard link was rewritten.</summary>
    private List<string>? TryCleanAutomatically(uint sequence)
    {
        var context = _context;
        if (context is null || ClipboardIO.IsOwnWrite() || ClipboardIO.IsSensitive() || HasNonTextContent())
        {
            return null;
        }

        var text = ClipboardIO.ReadText();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 8192)
        {
            return null;
        }

        var result = UrlCleaner.Clean(text, UrlRuleCatalog.Load(context.App));
        if (result is null || result.Removed.Count == 0)
        {
            return null;
        }

        // The rewrite drops HTML flavours; only do it when they add nothing but formatting to the link.
        if (ClipboardIO.IsAvailable(ClipFormats.Html))
        {
            var html = ReadHtmlFragment();
            if (html is null || !UrlCleaner.HtmlAddsNothing(html, text))
            {
                return null;
            }
        }

        // Narrow the window in which another program may have copied something new since we read.
        if (WinApi.GetClipboardSequenceNumber() != sequence)
        {
            return null;
        }

        // Not tagged as a WinKnife write: the clipboard history should record the cleaned link once.
        if (!ClipboardIO.WriteText(result.Url, markOwn: false))
        {
            return null;
        }

        context.Log.Info($"Cleaned link, removed: {string.Join(", ", result.Removed)}");
        return [.. result.Removed];
    }

    private void CleanNow()
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        var text = ClipboardIO.ReadText();
        var result = text is null ? null : UrlCleaner.Clean(text, UrlRuleCatalog.Load(context.App));
        if (result is null)
        {
            context.Notifier.Show(Strings.CleanUrl.Name, Strings.CleanUrl.NoUrl);
        }
        else if (result.Removed.Count == 0)
        {
            context.Notifier.Show(Strings.CleanUrl.Name, Strings.CleanUrl.NoChange);
        }
        else if (ClipboardIO.WriteText(result.Url, markOwn: false))
        {
            _lastRemoved = [.. result.Removed];
            Changed?.Invoke();
            context.Notifier.Show(Strings.CleanUrl.Name, string.Format(Strings.CleanUrl.RemovedFormat, string.Join(", ", result.Removed)));
        }
    }

    private static bool HasNonTextContent() =>
        ClipboardIO.IsAvailable(ClipFormats.HDrop)
        || ClipboardIO.IsAvailable(ClipFormats.Dib)
        || ClipboardIO.IsAvailable(ClipFormats.DibV5)
        || ClipboardIO.IsAvailable(ClipFormats.Bitmap)
        || ClipboardIO.IsAvailable(ClipFormats.Png)
        || ClipboardIO.IsAvailable(ClipFormats.EnhMetafile)
        || ClipboardIO.IsAvailable(ClipFormats.FileGroupDescriptorW)
        || ClipboardIO.IsAvailable(ClipFormats.ShellIdList);

    /// <summary>The HTML fragment of the CF_HTML payload (without its offsets header).</summary>
    private static string? ReadHtmlFragment()
    {
        var bytes = ClipboardIO.ReadFormat(ClipFormats.Html);
        if (bytes is null || bytes.Length > 256 * 1024)
        {
            return null;
        }

        var html = Encoding.UTF8.GetString(bytes).TrimEnd('\0');
        const string startMarker = "<!--StartFragment-->";
        const string endMarker = "<!--EndFragment-->";
        var start = html.IndexOf(startMarker, StringComparison.Ordinal);
        var end = html.IndexOf(endMarker, StringComparison.Ordinal);
        if (start >= 0 && end > start)
        {
            return html[(start + startMarker.Length)..end];
        }

        var firstTag = html.IndexOf('<');
        return firstTag >= 0 ? html[firstTag..] : string.Empty;
    }
}
