// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using Trayloom.Core;

namespace Trayloom.Features.Capture;

/// <summary>Utilities-tab row "Screenshot" with the "Recent captures" accessory button.</summary>
public sealed class ScreenshotUtility : IPanelUtility
{
    public string? FeatureId => "screenshot";

    public int Order => 0;

    public string Id => "screenshot";

    public string Title => Strings.Tools.Screenshot;

    public string Glyph => G.Camera;

    public string? Caption => Strings.Screenshot.PanelCaption;

    public string? ShortcutText => CaptureHost.ShortcutText("screenshot");

    public int InvokeDelayMs => 200;

    public string? AccessoryTitle => Strings.Screenshot.RecentCaptures;

    public string? AccessoryGlyph => G.History;

    public string? SettingsDestination => SettingsPages.Join(SettingsPages.ScreenCapture, "screenshot");

    public void Invoke(IPanelContext context) => CaptureHost.Screenshot?.CaptureNow();

    public void OnAccessory(IPanelContext context) => context.HostUtility("recentCaptures");
}

/// <summary>The hosted "Recent captures" panel (reached from the Screenshot / Screen recording rows).</summary>
public sealed class RecentCapturesUtility : IPanelUtility
{
    public IReadOnlyList<string> FeatureIds => ["screenshot", "screen-recorder"];

    public bool HostedOnly => true;

    public int Order => 1;

    public string Id => "recentCaptures";

    public string Title => Strings.Screenshot.RecentCaptures;

    public string Glyph => G.History;

    public string? Caption => null;

    public string? SettingsDestination => SettingsPages.Join(SettingsPages.ScreenCapture, "screenshot");

    public bool ShowsSettingsGear => false;

    public bool HostedKeepsPanelOpen => true;

    public FrameworkElement? CreateHostedView(IPanelContext context) => new RecentCapturesView(false, null, context.CloseDetail);
}

public sealed class ScreenTextUtility : IPanelUtility
{
    public string? FeatureId => "screen-ocr";

    public int Order => 10;

    public string Id => "screenOCR";

    public string Title => Strings.Tools.Text;

    public string Glyph => G.TextScan;

    public string? Caption => Strings.Ocr.Caption;

    public string? ShortcutText => CaptureHost.ShortcutText("screenOCR");

    public int InvokeDelayMs => 200;

    public string? SettingsDestination => SettingsPages.Join(SettingsPages.ScreenCapture, "screenOCR");

    public void Invoke(IPanelContext context) => CaptureHost.Text?.Capture();
}

public sealed class ColorPickerUtility : IPanelUtility
{
    public string? FeatureId => "color-picker";

    public int Order => 11;

    public string Id => "colorPicker";

    public string Title => Strings.Tools.Color;

    public string Glyph => G.Eyedropper;

    public string? Caption => Strings.Color.Caption;

    public string? ShortcutText => CaptureHost.ShortcutText("colorPicker");

    public int InvokeDelayMs => 150;

    public string? SettingsDestination => SettingsPages.Join(SettingsPages.ScreenCapture, "colorPicker");

    public void Invoke(IPanelContext context) => CaptureHost.ColorSampler?.Pick();
}

/// <summary>Utilities-tab row "Screen recording" / "Stop recording" (shows the elapsed time while recording).</summary>
public sealed class ScreenRecorderUtility : IPanelUtility
{
    private static bool Recording => CaptureHost.Recorder?.IsRecording == true;

    public string? FeatureId => "screen-recorder";

    public int Order => 12;

    public string Id => "screenRecorder";

    public string Title => Recording ? Strings.Recorder.Stop : Strings.Tools.Recording;

    public string Glyph => Recording ? G.Stop : G.Record;

    public string? Caption => Recording ? RecorderSupport.ElapsedLabel(CaptureHost.Recorder!.ElapsedSeconds) : Strings.Recorder.PanelCaption;

    public bool CaptionStaysVisible => Recording;

    public string? ShortcutText => CaptureHost.ShortcutText("screenRecorder");

    public int InvokeDelayMs => 200;

    public string? AccessoryTitle => Recording ? null : Strings.Screenshot.RecentCaptures;

    public string? AccessoryGlyph => G.History;

    public string? SettingsDestination => SettingsPages.Join(SettingsPages.ScreenCapture, "screenRecorder");

    public void Invoke(IPanelContext context) => CaptureHost.Recorder?.Toggle();

    public void OnAccessory(IPanelContext context) => context.HostUtility("recentCaptures");

    public event Action? Changed
    {
        add
        {
            if (CaptureHost.Recorder is { } r)
            {
                r.StateChanged += value;
            }
        }

        remove
        {
            if (CaptureHost.Recorder is { } r)
            {
                r.StateChanged -= value;
            }
        }
    }
}
