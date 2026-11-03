// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.Capture;

/// <summary>Color picker: opens the chooser on the colour tool; the picked colour is copied in the configured format.</summary>
internal sealed class ColorSamplerService
{
    private readonly IFeatureContext _ctx;

    public ColorSamplerService(IFeatureContext ctx)
    {
        _ctx = ctx;
    }

    public void Pick(bool fromShortcut = false) => ScreenCapture.Start(CaptureTool.Color, fromShortcut);

    public void Received(ColorOutcome outcome)
    {
        var format = _ctx.App.Get<string>(K.ColorFormat) ?? "hex";
        if (format is not ("hex" or "rgb" or "hsl" or "swiftui"))
        {
            format = "hex";
        }

        var text = outcome.Color.ToText(format, _ctx.App.Get<bool>(K.ColorBareHex));
        if (Output.CopyText(text))
        {
            var c = outcome.Color;
            QuickToolHud.Show(G.Eyedropper, text, System.Windows.Media.Color.FromRgb((byte)Math.Round(c.R * 255), (byte)Math.Round(c.G * 255), (byte)Math.Round(c.B * 255)));
        }
        else
        {
            CaptureHost.Beep();
        }
    }
}
