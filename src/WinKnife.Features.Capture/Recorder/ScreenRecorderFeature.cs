// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using WinKnife.Core;

namespace WinKnife.Features.Capture;

/// <summary>Screen recording: Windows.Graphics.Capture + Media Foundation H.264, WASAPI audio, editor with trim, blur lane and GIF export.</summary>
public sealed class ScreenRecorderFeature : IFeature
{
    private RecorderService? _service;

    public string Id => "screen-recorder";

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new("toggle", _service?.IsRecording == true ? Strings.Recorder.Stop : Strings.Recorder.RecordNow, () => _service?.Toggle(), _service?.IsRecording == true ? G.Stop : G.Record),
    ];

    public void Start(IFeatureContext context)
    {
        CaptureHost.Attach(context);
        context.App.Register("panelUtilityShow.recentCaptures", false);
        _service = new RecorderService(context);
        CaptureHost.Recorder = _service;
        context.Shortcuts.Bind("screenRecorder", () => _service?.ShortcutPressed());
        if (!context.Runtime.IsAvailable("screenshot"))
        {
            context.Shortcuts.Bind("recentCaptures", RecentCapturesWindow.ShowPalette);
        }
    }

    public void Stop()
    {
        _service?.Shutdown();
        if (ReferenceEquals(CaptureHost.Recorder, _service))
        {
            CaptureHost.Recorder = null;
        }

        _service = null;
    }
}
