// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.Capture;

/// <summary>Screen recording: Windows.Graphics.Capture + Media Foundation H.264, WASAPI audio, editor with trim, blur lane and GIF export.</summary>
public sealed class ScreenRecorderFeature : IFeature
{
    private RecorderService? _service;
    private IContributionServices? _context;
    private RecordingStateAdapter? _state;

    public string Id => "screen-recorder";

    public IReadOnlyList<FeatureAction> Actions =>
    [
        new("toggle", _service?.IsRecording == true ? Strings.Recorder.Stop : Strings.Recorder.RecordNow, () => _service?.Toggle(), _service?.IsRecording == true ? G.Stop : G.Record),
    ];

    public void Start(IFeatureContext context)
    {
        CaptureHost.Attach(context);
        _context = context;
        _service = new RecorderService(context);
        CaptureHost.Recorder = _service;
        _state = new RecordingStateAdapter(_service);
        context.Services.Provide<IRecordingState>(_state);
        context.Shortcuts.Bind("screenRecorder", () => _service?.ShortcutPressed());
        if (!context.Runtime.IsAvailable("screenshot"))
        {
            context.Shortcuts.Bind("recentCaptures", RecentCapturesWindow.ShowPalette);
        }
    }

    public void Stop()
    {
        if (_state is not null)
        {
            _context?.Services.Withdraw<IRecordingState>(_state);
            _state.Dispose();
            _state = null;
        }

        _service?.Shutdown();
        if (ReferenceEquals(CaptureHost.Recorder, _service))
        {
            CaptureHost.Recorder = null;
        }

        _service = null;
        _context = null;
    }
}

/// <summary>Publishes the recorder's state to other projects (Quick panel recording tile).</summary>
internal sealed class RecordingStateAdapter : IRecordingState, IDisposable
{
    private readonly RecorderService _service;

    public RecordingStateAdapter(RecorderService service)
    {
        _service = service;
        _service.StateChanged += Forward;
    }

    public bool IsRecording => _service.IsRecording;

    public TimeSpan Elapsed => TimeSpan.FromSeconds(_service.ElapsedSeconds);

    public event Action? Changed;

    public void Toggle() => _service.Toggle();

    public Task<bool> StopAndFinishAsync(TimeSpan budget) => _service.StopAndFinishAsync(budget);

    public void Dispose() => _service.StateChanged -= Forward;

    private void Forward() => Changed?.Invoke();
}
