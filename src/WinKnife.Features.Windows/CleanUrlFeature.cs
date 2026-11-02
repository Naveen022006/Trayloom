// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text;
using System.Windows;
using WinKnife.Core;
using WinKnife.Core.Ui;

namespace WinKnife.Features.WindowTools;

/// <summary>Removes tracking parameters (utm_*, fbclid, gclid, ...) from links as soon as they are copied.</summary>
public sealed class CleanUrlFeature : IFeature
{
    private IFeatureContext? _context;
    private IDisposable? _subscription;

    public string Id => "clean-url";

    public string Name => "Clean URL";

    public string Description => "Strip tracking parameters such as utm_*, fbclid and gclid from links the moment you copy them.";

    public FeatureGroup Group => FeatureGroup.ClipboardFiles;

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new FeatureAction("clean-clipboard", "Clean link on clipboard", CleanNow, ""),
    ];

    public void Start(IFeatureContext context)
    {
        _context = context;
        _subscription = ClipboardMonitor.Subscribe(OnClipboardChanged);
    }

    public void Stop()
    {
        _subscription?.Dispose();
        _subscription = null;
        _context = null;
    }

    public FrameworkElement? CreateSettingsView()
    {
        if (_context is null)
        {
            return null;
        }

        return new SettingsViewBuilder(_context.Settings)
            .AddToggle("notify", "Show a notification when a link was cleaned", false)
            .AddText("customParameters", "Extra parameters to remove", string.Empty, "Comma separated names removed from links on every site, for example: ref, campaign_id.")
            .AddText("siteParameters", "Extra parameters per site", string.Empty, "Comma separated host|name pairs, for example: example.com|trk, shop.example|aff.")
            .AddText("disabledParameters", "Parameters to keep", string.Empty, "Built-in names to leave alone, as host|name (empty host = every site), for example: youtube.com|si, |utm_*, |fbclid.")
            .AddNote("Only links where something was removed are rewritten, as plain text. Copies containing images, files, passwords or several items are never touched.")
            .Build();
    }

    private UrlRules LoadRules()
    {
        var settings = _context!.Settings;
        return UrlRules.Parse(
            settings.Get("customParameters", string.Empty),
            settings.Get("siteParameters", string.Empty),
            settings.Get("disabledParameters", string.Empty));
    }

    private async void OnClipboardChanged()
    {
        if (_context is null)
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

            TryCleanAutomatically(sequence);
        }
        catch (Exception ex)
        {
            _context?.Log.Error("Clean URL failed", ex);
        }
    }

    private void TryCleanAutomatically(uint sequence)
    {
        if (ClipboardIO.IsOwnWrite() || ClipboardIO.IsSensitive() || HasNonTextContent())
        {
            return;
        }

        var text = ClipboardIO.ReadText();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 8192)
        {
            return;
        }

        var result = UrlCleaner.Clean(text, LoadRules());
        if (result is null || result.Removed.Count == 0)
        {
            return;
        }

        // The rewrite drops HTML flavours; only do it when they add nothing but formatting to the link.
        if (ClipboardIO.IsAvailable(ClipFormats.Html))
        {
            var html = ReadHtmlFragment();
            if (html is null || !UrlCleaner.HtmlAddsNothing(html, text))
            {
                return;
            }
        }

        // Narrow the window in which another program may have copied something new since we read.
        if (WinApi.GetClipboardSequenceNumber() != sequence)
        {
            return;
        }

        // Not tagged as a WinKnife write: the clipboard history should record the cleaned link.
        if (!ClipboardIO.WriteText(result.Url, markOwn: false))
        {
            return;
        }

        _context!.Log.Info($"Cleaned link, removed: {string.Join(", ", result.Removed)}");
        if (_context.Settings.Get("notify", false))
        {
            _context.Notifier.Show("Link cleaned", $"Removed {string.Join(", ", result.Removed)}");
        }
    }

    private void CleanNow()
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        var text = ClipboardIO.ReadText();
        var result = text is null ? null : UrlCleaner.Clean(text, LoadRules());
        if (result is null)
        {
            context.Notifier.Show("Clean URL", "The clipboard does not hold a web link.");
        }
        else if (result.Removed.Count == 0)
        {
            context.Notifier.Show("Clean URL", "Nothing to remove from this link.");
        }
        else if (ClipboardIO.WriteText(result.Url, markOwn: false))
        {
            context.Notifier.Show("Link cleaned", $"Removed {string.Join(", ", result.Removed)}");
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
