// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using WinKnife.Core;

namespace WinKnife.Features.Capture;

/// <summary>The four tools that share the selection overlay (digit keys 1-4).</summary>
internal enum CaptureTool
{
    Screenshot = 1,
    Recording = 2,
    Text = 3,
    Color = 4,
}

internal static class CaptureTools
{
    public static string Title(CaptureTool t) => t switch
    {
        CaptureTool.Screenshot => Strings.Tools.Screenshot,
        CaptureTool.Recording => Strings.Tools.Recording,
        CaptureTool.Text => Strings.Tools.Text,
        _ => Strings.Tools.Color,
    };

    public static string Glyph(CaptureTool t) => t switch
    {
        CaptureTool.Screenshot => G.Camera,
        CaptureTool.Recording => G.Record,
        CaptureTool.Text => G.TextScan,
        _ => G.Eyedropper,
    };

    public static string FeatureId(CaptureTool t) => t switch
    {
        CaptureTool.Screenshot => "screenshot",
        CaptureTool.Recording => "screen-recorder",
        CaptureTool.Text => "screen-ocr",
        _ => "color-picker",
    };

    public static string Anchor(CaptureTool t) => t switch
    {
        CaptureTool.Screenshot => "screenshot",
        CaptureTool.Recording => "screenRecorder",
        CaptureTool.Text => "screenOCR",
        _ => "colorPicker",
    };

    public static string RoleId(CaptureTool t) => t switch
    {
        CaptureTool.Screenshot => "screenshot",
        CaptureTool.Recording => "screenRecorder",
        CaptureTool.Text => "screenOCR",
        _ => "colorPicker",
    };

    public static string EnabledKey(CaptureTool t) => t switch
    {
        CaptureTool.Screenshot => K.ScreenshotShortcutEnabled,
        CaptureTool.Recording => K.RecorderShortcutEnabled,
        CaptureTool.Text => K.OcrShortcutEnabled,
        _ => K.ColorShortcutEnabled,
    };

    public static string ShowMenuKey(CaptureTool t) => t switch
    {
        CaptureTool.Screenshot => K.ShowMenuScreenshot,
        CaptureTool.Recording => K.ShowMenuRecorder,
        CaptureTool.Text => K.ShowMenuOcr,
        _ => K.ShowMenuColor,
    };

    public static IEnumerable<CaptureTool> Installed(IContributionServices s) =>
        Enum.GetValues<CaptureTool>().Where(t => s.Runtime.IsAvailable(FeatureId(t)));
}

/// <summary>A picked area or window, resolved once (a moved window keeps the region it had).</summary>
/// <param name="Monitor">Monitor holding the area.</param>
/// <param name="PixelRect">Rectangle in monitor pixels (top-left of the monitor = 0,0).</param>
/// <param name="Screen">The same rectangle in virtual-screen pixels.</param>
/// <param name="Window">Window handle when a window was clicked, else 0.</param>
internal sealed record RegionPick(MonitorInfo Monitor, Int32Rect PixelRect, Int32Rect Screen, nint Window, int WindowProcessId);

internal abstract record CaptureOutcome;

internal sealed record CancelledOutcome : CaptureOutcome;

internal sealed record PixelsOutcome(Capture Capture, CaptureTool Tool) : CaptureOutcome;

internal sealed record GeometryOutcome(RegionPick Pick, CaptureTool Tool, bool Scrolling) : CaptureOutcome;

internal sealed record ColorOutcome(ColorValue Color, int X, int Y) : CaptureOutcome;

internal sealed class CaptureRequest
{
    public CaptureTool Tool { get; set; } = CaptureTool.Screenshot;

    /// <summary>Shows the mode bar with the installed tools (the unified chooser).</summary>
    public bool ShowMenu { get; set; } = true;

    /// <summary>Opens already in scrolling mode (the "Scrolling capture" button).</summary>
    public bool ScrollingOnly { get; set; }

    public Action<CaptureOutcome> Done { get; set; } = _ => { };
}
