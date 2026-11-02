// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using WinKnife.Core;

namespace WinKnife.Features.Capture;

/// <summary>Color picker: grab the color of any pixel and copy it as HEX, RGB, HSL or SwiftUI-style text.</summary>
public sealed class ColorPickerFeature : IFeature
{
    private ColorSamplerService? _service;

    public string Id => "color-picker";

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new("pick", Strings.Color.PickNow, () => _service?.Pick(), G.Eyedropper),
    ];

    public void Start(IFeatureContext context)
    {
        CaptureHost.Attach(context);
        _service = new ColorSamplerService(context);
        CaptureHost.ColorSampler = _service;
        context.Shortcuts.Bind("colorPicker", () => _service?.Pick(true));
    }

    public void Stop()
    {
        if (ReferenceEquals(CaptureHost.ColorSampler, _service))
        {
            CaptureHost.ColorSampler = null;
        }

        _service = null;
    }
}
