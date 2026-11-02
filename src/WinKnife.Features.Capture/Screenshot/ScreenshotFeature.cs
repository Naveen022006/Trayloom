// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using WinKnife.Core;

namespace WinKnife.Features.Capture;

/// <summary>Screenshot: captures an area, window or screen and annotates it. Hotkeys come from the shortcut roles (opt-in).</summary>
public sealed class ScreenshotFeature : IFeature
{
    private ScreenshotService? _service;

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
        context.App.Register("panelUtilityShow.recentCaptures", false);
        context.Shortcuts.Bind("screenshot", () => _service?.CaptureNow(true));
        context.Shortcuts.Bind("screenshotFullScreen", () => _service?.CaptureFullScreen());
        context.Shortcuts.Bind("screenshotLastCapture", () => _service?.EditLatest());
        context.Shortcuts.Bind("screenshotClipboard", () => _service?.EditClipboard());
        context.Shortcuts.Bind("recentCaptures", RecentCapturesWindow.ShowPalette);
        PurgeOldTemp();
    }

    public void Stop()
    {
        _service?.Stop();
        if (ReferenceEquals(CaptureHost.Screenshot, _service))
        {
            CaptureHost.Screenshot = null;
        }

        _service = null;
    }

    private static void PurgeOldTemp()
    {
        try
        {
            var root = Path.Combine(Path.GetTempPath(), "WinKnife");
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
