// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using WinKnife.Core;

namespace WinKnife.Features.Capture;

/// <summary>Copy text (or QR codes) from anything on screen with Windows.Media.Ocr and ZXing.</summary>
public sealed class ScreenOcrFeature : IFeature
{
    private ScreenTextService? _service;

    public string Id => "screen-ocr";

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new("capture", Strings.Tools.Text, () => _service?.Capture(), G.TextScan),
    ];

    public void Start(IFeatureContext context)
    {
        CaptureHost.Attach(context);
        _service = new ScreenTextService(context);
        CaptureHost.Text = _service;
        context.Shortcuts.Bind("screenOCR", () => _service?.Capture(true));
    }

    public void Stop()
    {
        _service?.Cancel();
        if (ReferenceEquals(CaptureHost.Text, _service))
        {
            CaptureHost.Text = null;
        }

        _service = null;
    }
}
