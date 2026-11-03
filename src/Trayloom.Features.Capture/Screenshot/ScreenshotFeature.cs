// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.Capture;

/// <summary>Screenshot: captures an area, window or screen and annotates it. Hotkeys come from the shortcut roles (opt-in).</summary>
public sealed class ScreenshotFeature : IFeature
{
    private ScreenshotService? _service;
    private IContributionServices? _context;
    private EditorAdapter? _editor;

    public string Id => "screenshot";

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new("capture", "Screenshot: " + Strings.Screenshot.CaptureNow, () => _service?.CaptureNow(), G.Camera),
        new("scrolling", "Screenshot: " + Strings.Screenshot.ScrollingCapture, () => _service?.CaptureScrolling(), G.Stack),
        new("fullscreen", "Screenshot: " + Strings.Screenshot.CaptureWholeScreen, () => _service?.CaptureFullScreen(), G.Camera),
        new("edit-latest", "Screenshot: " + Strings.Screenshot.EditLatest, () => _service?.EditLatest(), G.Edit),
        new("edit-clipboard", "Screenshot: " + Strings.Screenshot.EditClipboard, () => _service?.EditClipboard(), G.Edit),
        new("recent", Strings.Screenshot.RecentCaptures, RecentCapturesWindow.ShowPalette, G.History),
    ];

    public void Start(IFeatureContext context)
    {
        CaptureHost.Attach(context);
        _service = new ScreenshotService(context);
        CaptureHost.Screenshot = _service;
        _context = context;
        _editor = new EditorAdapter(_service);
        context.Services.Provide<IScreenshotEditorService>(_editor);
        context.Shortcuts.Bind("screenshot", () => _service?.CaptureNow(true));
        context.Shortcuts.Bind("screenshotFullScreen", () => _service?.CaptureFullScreen());
        context.Shortcuts.Bind("screenshotLastCapture", () => _service?.EditLatest());
        context.Shortcuts.Bind("screenshotClipboard", () => _service?.EditClipboard());
        context.Shortcuts.Bind("recentCaptures", RecentCapturesWindow.ShowPalette);
        PurgeOldTemp();
    }

    public void Stop()
    {
        if (_editor is not null)
        {
            _context?.Services.Withdraw<IScreenshotEditorService>(_editor);
            _editor = null;
        }

        _context = null;
        _service?.Stop();
        if (ReferenceEquals(CaptureHost.Screenshot, _service))
        {
            CaptureHost.Screenshot = null;
        }

        _service = null;
    }

    private sealed class EditorAdapter(ScreenshotService service) : IScreenshotEditorService
    {
        public void EditClipboardImage() => service.EditClipboard();

        public void EditFile(string path) => service.EditFile(path);
    }

    private static void PurgeOldTemp()
    {
        try
        {
            var root = Path.Combine(Path.GetTempPath(), "Trayloom");
            if (!Directory.Exists(root))
            {
                return;
            }

            foreach (var dir in Directory.EnumerateDirectories(root, "Screenshot*-*"))
            {
                if (Directory.GetCreationTimeUtc(dir) < DateTime.UtcNow.AddHours(-1))
                {
                    Directory.Delete(dir, true);
                }
            }
        }
        catch
        {
            // housekeeping only
        }
    }
}
