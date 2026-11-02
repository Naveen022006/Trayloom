// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using WinKnife.Core;

namespace WinKnife.Features.Maintenance;

/// <summary>Media tools: the catalog entry "media-tools". The UI lives in the contributions.</summary>
public sealed class MediaToolsFeature : IFeature
{
    public string Id => "media-tools";

    private IContributionServices? _context;
    private IMediaToolsService? _service;

    public void Start(IFeatureContext context)
    {
        Host.Attach(context);
        _context = context;
        _service = new MediaToolsServiceImpl();
        context.Services.Provide(_service);
    }

    public void Stop()
    {
        if (_service is not null)
        {
            _context?.Services.Withdraw(_service);
            _service = null;
        }

        _context = null;
        MediaService.Shared.Cancel();
    }

    /// <summary>Selects files in a media tool (shelf, recent captures, screenshot flows) before the workspace opens.</summary>
    /// <param name="paths">Input files.</param>
    /// <param name="tool">0 = Video, 1 = GIF, 2 = Image, 3 = Text.</param>
    public static void OpenWith(IEnumerable<string> paths, int tool) => MediaWorkspaceView.OpenWith(paths, (MediaTool)Math.Clamp(tool, 0, 3));
}

/// <summary>Settings page "Media".</summary>
public sealed class MediaSettingsSection : ISettingsSection
{
    public string? FeatureId => "media-tools";

    public string PageId => SettingsPages.Media;

    public string? Title => "Media";

    public IReadOnlyList<string> Keywords => ["PDF", "GIF", "PNG", "JPEG", "convert", "resize", "watermark", "rename", "profile", "fit", "fill", "crop", "Convert to PDF", "OCR", "Copy text from screen"];

    public int Order => 10;

    public FrameworkElement CreateView(ISettingsContext c)
    {
        Host.Attach(c);
        var view = new MediaWorkspaceView(false);
        view.Margin = new Thickness(0);
        return view;
    }
}

/// <summary>Utilities row "Media": the compact workspace.</summary>
public sealed class MediaUtility : IPanelUtility
{
    public string? FeatureId => "media-tools";

    public int Order => 60;

    public string Id => "media";

    public string Title => "Media";

    public string Glyph => G.Photo;

    public string? Caption => "Compress videos, convert and process images, make GIFs and extract text locally.";

    public string? SettingsDestination => SettingsPages.Media;

    public FrameworkElement? CreateHostedView(IPanelContext c)
    {
        Host.Attach(c);
        return new MediaWorkspaceView(true, c.RequestRelayout, c.SetKeepOpen, c.ClosePanel);
    }
}
