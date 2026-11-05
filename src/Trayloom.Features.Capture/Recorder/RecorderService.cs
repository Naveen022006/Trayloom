// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Trayloom.Core;

namespace Trayloom.Features.Capture;

/// <summary>Recording state machine: pick, countdown, record (pause / resume), stop, then editor or direct save.</summary>
internal sealed class RecorderService
{
    private readonly IFeatureContext _ctx;
    private readonly object _gate = new();
    private readonly List<(long Start, long End)> _pauses = new();
    private readonly List<RecorderEditorWindow> _editors = new();
    private RegionPick? _pick;
    private WgcCapture? _cap;
    private VideoWriter? _writer;
    private WasapiCapture? _sys;
    private WasapiCapture? _mic;
    private PointerRecorder? _pointerRec;
    private Take? _take;
    private RecorderPill? _pill;
    private RegionGuideWindow? _guide;
    private DispatcherTimer? _timer;
    private int _generation;
    private bool _preparing;
    private bool _finishing;
    private bool _paused;
    private long _originQpc;
    private bool _haveOrigin;
    private byte[]? _lastFrame;
    private long _startQpc;
    private long _pauseStartQpc;
    private int _fps;
    private string? _stopReason;
    private bool _opensEditor;
    private Task? _finishTask;
    private volatile int _exitMode; // 0 normal, 1 quitting (save the take to the folder), 2 session ending (keep it for recovery)
    private static long _freq;

    public RecorderService(IFeatureContext ctx)
    {
        _ctx = ctx;
        N.QueryPerformanceFrequency(out _freq);
        Take.Sweep(Array.Empty<string>());
        SystemEvents.SessionEnding += OnSessionEnding;
        if (Take.Recoverable(Array.Empty<string>()).Count > 0)
        {
            _ctx.Notifier.Show(Loc.T("A recording wasn’t saved"), Loc.T("Open Screen recording in Settings to recover it."), NotificationLevel.Info);
        }
    }

    /// <summary>Folders of the takes that are being recorded or edited right now (never swept, never offered for recovery).</summary>
    public IReadOnlyList<string> OwnedFolders()
    {
        var owned = _editors.Select(e => e.Take.Folder).ToList();
        if (_take is { } t)
        {
            owned.Add(t.Folder);
        }

        return owned;
    }

    /// <summary>Opens a take that an earlier run left behind in the editor; false when it cannot be read.</summary>
    public bool OpenRecovered(string folder)
    {
        if (_editors.Any(e => string.Equals(e.Take.Folder, folder, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (Take.Load(folder) is not { } take)
        {
            CaptureHost.Beep();
            return false;
        }

        OpenEditor(take);
        return true;
    }

    /// <summary>
    /// Stops a running recording and waits (up to <paramref name="budget"/>) until the take is closed and saved to the recordings folder,
    /// so quitting or uninstalling never loses it. True when nothing is left unfinished.
    /// </summary>
    public async Task<bool> StopAndFinishAsync(TimeSpan budget)
    {
        if (!IsRecording && !_finishing)
        {
            return true;
        }

        if (IsRecording)
        {
            _exitMode = 1;
            Stop(null);
        }
        else
        {
            _generation++; // a recording that is already finishing is kept, not opened in an editor during the quit
        }

        var task = _finishTask;
        if (task is null)
        {
            return !IsRecording;
        }

        var done = await Task.WhenAny(task, Task.Delay(budget)) == task;
        if (!done)
        {
            CaptureHost.Log("recording not finished before exit; kept for recovery");
        }

        return done;
    }

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e)
    {
        try
        {
            if (!IsRecording && !_finishing)
            {
                return;
            }

            CaptureHost.Ui.Invoke(() =>
            {
                if (IsRecording)
                {
                    _exitMode = 2;
                    Stop(null);
                }
            }, DispatcherPriority.Send, CancellationToken.None, TimeSpan.FromSeconds(2));
            _finishTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            CaptureHost.Log("finishing the recording at sign-out failed", ex);
        }
    }

    public bool IsRecording { get; private set; }

    public bool IsPaused => _paused;

    public int ElapsedSeconds { get; private set; }

    public event Action? StateChanged;

    private static long Qpc()
    {
        N.QueryPerformanceCounter(out var v);
        return v;
    }

    private static long ToTicks(long qpc) => (long)(qpc * 10_000_000.0 / _freq);

    private double ElapsedAt(long qpcNow)
    {
        double paused = 0;
        lock (_gate)
        {
            foreach (var (s, e) in _pauses)
            {
                paused += e - s;
            }

            if (_paused)
            {
                paused += qpcNow - _pauseStartQpc;
            }
        }

        return (qpcNow - _startQpc - paused) / (double)_freq;
    }

    // ---- entry ------------------------------------------------------------------------------------------------

    public bool CanPrepare()
    {
        if (_finishing)
        {
            return false;
        }

        Directory.CreateDirectory(Take.Root);
        if (RecorderSupport.FreeBytes(Take.Root) < RecorderSupport.StartFreeBytes)
        {
            MessageBox.Show(Strings.Recorder.NoSpaceMessage, Strings.Recorder.NoSpaceTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    public void Toggle()
    {
        if (StopOrCancel())
        {
            return;
        }

        ScreenCapture.Start(CaptureTool.Recording, false);
    }

    public void ShortcutPressed()
    {
        if (StopOrCancel())
        {
            return;
        }

        ScreenCapture.Start(CaptureTool.Recording, true);
    }

    private bool StopOrCancel()
    {
        if (_finishing)
        {
            return true;
        }

        if (IsRecording)
        {
            Stop(null);
            return true;
        }

        if (_preparing)
        {
            CancelPreparation();
            return true;
        }

        return false;
    }

    private void CancelPreparation()
    {
        _generation++;
        _preparing = false;
        QuickToolHud.Dismiss();
        CloseSurfaces();
        CaptureHost.SessionOnScreen = false;
        StateChanged?.Invoke();
    }

    public void OnRegion(RegionPick pick)
    {
        if (IsRecording || _preparing)
        {
            return;
        }

        if (!WgcCapture.IsSupported)
        {
            QuickToolHud.Show(G.Record, Strings.Recorder.RecordFailed);
            return;
        }

        _pick = pick;
        _preparing = true;
        var gen = ++_generation;
        if (pick.Window == 0)
        {
            _guide = new RegionGuideWindow(pick.Monitor, pick.PixelRect);
            _guide.Present();
        }

        var delay = RecorderSupport.SanitizedDelay(_ctx.App.Get<int>(K.RecCountdown));
        StateChanged?.Invoke();
        if (delay == 0)
        {
            Begin(gen);
            return;
        }

        CaptureHost.SessionOnScreen = true;
        var remaining = delay;
        QuickToolHud.ShowCountdown(remaining);
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        t.Tick += (_, _) =>
        {
            if (gen != _generation)
            {
                t.Stop();
                return;
            }

            remaining--;
            if (remaining <= 0)
            {
                t.Stop();
                CaptureHost.SessionOnScreen = false;
                QuickToolHud.Dismiss();
                CaptureHost.Ui.BeginInvoke(DispatcherPriority.Background, () => Begin(gen));
                return;
            }

            QuickToolHud.ShowCountdown(remaining);
        };
        t.Start();
    }

    // ---- recording ----------------------------------------------------------------------------------------------

    private void Begin(int gen)
    {
        if (gen != _generation || _pick is not { } pick)
        {
            return;
        }

        try
        {
            _fps = RecorderSupport.SanitizedFps(_ctx.App.Get<int>(K.RecFrameRate));
            _take = Take.Create();
            _pauses.Clear();
            _paused = false;
            _haveOrigin = false;
            _lastFrame = null;
            _stopReason = null;
            _cap = new WgcCapture(pick, _fps);
            var w = _cap.Width;
            var h = _cap.Height;
            _take.Width = w;
            _take.Height = h;
            _take.Fps = _fps;
            _writer = new VideoWriter(_take.VideoPath, w, h, _fps, RecorderSupport.AverageBitRate(w, h, _fps, "high"), false);
            _writer.Begin();

            _pill = new RecorderPill(TogglePause, () => Stop(null));
            _pill.Present(pick.Monitor);

            _startQpc = Qpc();
            if (_ctx.App.Get<bool>(K.RecSystemAudio))
            {
                _sys = TrySystemAudio(_take.SystemWav, pick);
            }

            if (_ctx.App.Get<bool>(K.RecMicrophone))
            {
                _mic = TryAudio(_take.MicWav, false, true);
            }

            _pointerRec = new PointerRecorder(_ctx.Hooks, PointerRegion(pick), EventTime);
            _cap.FrameReady += OnFrame;
            _cap.Closed += () => CaptureHost.Ui.BeginInvoke(() => Stop(null));
            Thread.Sleep(120); // let the selection overlay leave
            _startQpc = Qpc();
            _cap.Start();
            _pointerRec.Start();
            IsRecording = true;
            _preparing = false;
            ElapsedSeconds = 0;
            N.SetThreadExecutionState(N.ES_CONTINUOUS | N.ES_SYSTEM_REQUIRED | N.ES_DISPLAY_REQUIRED);
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) => Tick();
            _timer.Start();
            StateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            CaptureHost.Log("recording failed to start", ex);
            AbortStart();
            QuickToolHud.Show(G.Record, Strings.Recorder.RecordFailed);
        }
    }

    /// <summary>The recorded rectangle in virtual-screen pixels: the picked area, or the window's visible frame.</summary>
    private static Int32Rect PointerRegion(RegionPick pick)
    {
        if (pick.Window != 0 && N.DwmGetWindowAttribute(pick.Window, N.DWMWA_EXTENDED_FRAME_BOUNDS, out N.RECT r, System.Runtime.InteropServices.Marshal.SizeOf<N.RECT>()) == 0 && r.Width > 0 && r.Height > 0)
        {
            return new Int32Rect(r.Left, r.Top, r.Width, r.Height);
        }

        return pick.Screen;
    }

    /// <summary>Recording seconds of a QPC instant: null before the first frame and inside a pause.</summary>
    private double? EventTime(long qpc)
    {
        lock (_gate)
        {
            if (!_haveOrigin || qpc < _originQpc || (_paused && qpc >= _pauseStartQpc))
            {
                return null;
            }

            long pausedBefore = 0;
            foreach (var (s, e) in _pauses)
            {
                if (qpc >= s && qpc < e)
                {
                    return null;
                }

                if (qpc >= e)
                {
                    pausedBefore += e - s;
                }
            }

            return (qpc - _originQpc - pausedBefore) / (double)_freq;
        }
    }

    /// <summary>
    /// System sound. A window recording follows the picture (process loopback of the window's app and its children); a whole-screen
    /// recording leaves Trayloom's own sound out. Both fall back to plain endpoint loopback where process loopback is not available.
    /// </summary>
    private static WasapiCapture? TrySystemAudio(string path, RegionPick pick)
    {
        if (ProcessLoopbackActivator.IsAvailable)
        {
            var target = pick.Window != 0 && pick.WindowProcessId > 0
                ? new ProcessLoopbackTarget(pick.WindowProcessId, true)
                : new ProcessLoopbackTarget(Environment.ProcessId, false);
            try
            {
                var cap = new WasapiCapture(path, true, target);
                cap.Start();
                return cap;
            }
            catch (Exception ex)
            {
                CaptureHost.Log("process loopback unavailable, using endpoint loopback", ex);
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    // ignore
                }
            }
        }

        return TryAudio(path, true, false);
    }

    private static WasapiCapture? TryAudio(string path, bool loopback, bool isMic)
    {
        try
        {
            var cap = new WasapiCapture(path, loopback);
            cap.Start();
            return cap;
        }
        catch (Exception ex)
        {
            CaptureHost.Log("audio unavailable", ex);
            try
            {
                File.Delete(path);
            }
            catch
            {
                // ignore
            }

            if (isMic)
            {
                QuickToolHud.Show("", Strings.Recorder.MicUnavailable);
            }

            return null;
        }
    }

    private void AbortStart()
    {
        _preparing = false;
        IsRecording = false;
        // each part in its own try: one that throws must not leave the audio files or the capture open
        Quietly(() => _cap?.Dispose());
        Quietly(() => _writer?.Cancel());
        Quietly(() => _sys?.Stop());
        Quietly(() => _mic?.Stop());
        Quietly(() => _pointerRec?.Stop());
        _cap = null;
        _writer = null;
        _sys = _mic = null;
        _pointerRec = null;
        CloseSurfaces();
        _take?.Delete();
        _take = null;
        CaptureHost.SessionOnScreen = false;
        StateChanged?.Invoke();
    }

    private static void Quietly(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // best effort
        }
    }

    private void OnFrame(CapturedFrame frame)
    {
        var writer = _writer;
        if (writer is null || _finishing)
        {
            return;
        }

        var q = (long)(frame.Time.Ticks * (double)_freq / 10_000_000.0);
        long mapped;
        lock (_gate)
        {
            if (!_haveOrigin)
            {
                _originQpc = q;
                _haveOrigin = true;
            }

            if (_paused && q >= _pauseStartQpc)
            {
                return;
            }

            long pausedBefore = 0;
            foreach (var (s, e) in _pauses)
            {
                if (q >= s && q < e)
                {
                    return;
                }

                if (q >= e)
                {
                    pausedBefore += e - s;
                }
            }

            mapped = q - _originQpc - pausedBefore;
        }

        var ticks = ToTicks(Math.Max(0, mapped));
        _lastFrame = frame.Bgra;
        writer.TryAdd(new VideoFrame(frame.Bgra, TimeSpan.FromTicks(ticks)));
    }

    private void Tick()
    {
        var now = Qpc();
        ElapsedSeconds = (int)Math.Floor(Math.Max(0, ElapsedAt(now)));
        _pill?.SetState(ElapsedSeconds, _paused);
        StateChanged?.Invoke();
        if (ElapsedSeconds % 2 == 0 && _take is { } take)
        {
            var folder = take.Folder;
            _ = Task.Run(() => RecorderSupport.FreeBytes(folder)).ContinueWith(t =>
            {
                if (t.Result < RecorderSupport.ContinueFreeBytes && IsRecording && !_finishing && ReferenceEquals(_take, take))
                {
                    CaptureHost.Ui.BeginInvoke(() => Stop(Strings.Recorder.StoppedNoSpace));
                }
            });
        }
    }

    private void TogglePause()
    {
        if (!IsRecording || _finishing)
        {
            return;
        }

        var now = Qpc();
        lock (_gate)
        {
            if (!_paused)
            {
                _paused = true;
                _pauseStartQpc = now;
            }
            else
            {
                _pauses.Add((_pauseStartQpc, now));
                _paused = false;
            }
        }

        _sys?.SetPaused(_paused);
        _mic?.SetPaused(_paused);
        _pill?.SetState(ElapsedSeconds, _paused);
        StateChanged?.Invoke();
    }

    public void Stop(string? reason)
    {
        if (!IsRecording || _finishing)
        {
            return;
        }

        _finishing = true;
        _stopReason = reason;
        _timer?.Stop();
        var take = _take!;
        var stopQpc = Qpc();
        lock (_gate)
        {
            if (_paused)
            {
                _pauses.Add((_pauseStartQpc, stopQpc));
                _paused = false;
            }
        }

        var duration = Math.Max(0.2, ElapsedAt(stopQpc));
        _pointerRec?.Stop();
        N.SetThreadExecutionState(N.ES_CONTINUOUS);
        CloseSurfaces();
        _cap?.Dispose();
        var writer = _writer!;
        // hold the last picture until the stop instant so a still screen ends as long as it felt
        if (_lastFrame is { } last)
        {
            writer.TryAdd(new VideoFrame(last, TimeSpan.FromSeconds(duration)), block: true);
        }

        writer.Complete();
        IsRecording = false;
        StateChanged?.Invoke();

        // settings are read here, on the UI thread; the file work below never touches it
        var s = _ctx.App;
        _opensEditor = _exitMode == 0 && _stopReason is null && s.Get<bool>(K.RecOpenEditor);
        take.Document = new EditDocument
        {
            Quality = RecorderSupport.SanitizedQuality(s.Get<string>(K.RecQuality)),
            KeepsSystemAudio = s.Get<bool>(K.RecSystemAudio),
            GifSize = s.Get<string>(K.RecGifSize) ?? "medium",
            GifFrameRate = s.Get<int>(K.RecGifFps),
            ZoomEnabled = _opensEditor && s.Get<bool>(K.RecAutoZoom),
            ShowsPointer = _opensEditor,
        };
        var dest = Output.UniquePath(Output.BaseFolder(s.Get<string>(K.RecSaveFolder)), RecorderSupport.DefaultName(DateTime.Now), ".mp4");
        _finishTask = FinishAsync(take, writer, duration, _generation, _sys, _mic, _pointerRec, dest);
    }

    /// <summary>
    /// Closes the audio and video files and writes the take's index. Nothing in here waits for the UI thread (every await is
    /// <c>ConfigureAwait(false)</c>), so quitting can wait on this task without a deadlock.
    /// </summary>
    private async Task FinishAsync(Take take, VideoWriter writer, double duration, int gen, WasapiCapture? sys, WasapiCapture? mic, PointerRecorder? pointer, string dest)
    {
        try
        {
            await Task.Run(() =>
            {
                sys?.Stop();
                mic?.Stop();
                try
                {
                    pointer?.Stop();
                    pointer?.Save(take.Folder);
                }
                catch (Exception ex)
                {
                    CaptureHost.Log("saving the pointer track failed", ex);
                }
            }).ConfigureAwait(false);
            if (writer.Completion is { } c)
            {
                await c.ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            CaptureHost.Log("finishing take failed", ex);
        }

        _cap = null;
        _writer = null;
        _sys = _mic = null;
        _pointerRec = null;
        take.Duration = duration;
        var ok = writer.Failure is null && File.Exists(take.VideoPath) && new FileInfo(take.VideoPath).Length > 0;
        var exit = _exitMode;
        if (ok)
        {
            // meta.json marks the take as finished: from here on it can be recovered if nothing else happens to it
            take.SaveDocument();
        }
        else
        {
            CaptureHost.Log("take failed: " + writer.Failure);
            take.Delete();
        }

        if (ok && exit == 1)
        {
            try
            {
                await ExportToFolderAsync(take, dest).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                CaptureHost.Log("saving the recording before exit failed; kept for recovery", ex);
            }
        }

        // BeginInvoke, never a wait: Shutdown blocks the UI thread on this very task
        _ = CaptureHost.Ui.BeginInvoke(() =>
        {
            _finishing = false;
            _take = null;
            CaptureHost.SessionOnScreen = false;
            if (!ok)
            {
                if (exit == 0)
                {
                    QuickToolHud.Show(G.Record, Strings.Recorder.RecordFailed);
                }

                return;
            }

            if (exit == 0 && gen == _generation)
            {
                Deliver(take);
            }
        });
    }

    private void Deliver(Take take)
    {
        if (_opensEditor)
        {
            // the first set of zooms comes straight from the clicks, once; after that they belong to the person
            take.Document.RestoreAutomaticZooms(take.Pointer.Clicks, take.TypingTimes, take.Duration);
            take.Document.ZoomsGenerated = true;
            OpenEditor(take);
            return;
        }

        _ = DirectSaveAsync(take, _stopReason);
    }

    public void OpenEditor(Take take)
    {
        var editor = new RecorderEditorWindow(this, take);
        _editors.Add(editor);
        editor.Closed += (_, _) =>
        {
            _editors.Remove(editor);
            Take.Sweep(OwnedFolders());
        };
        editor.Show();
        editor.Activate();
    }

    /// <summary>Renders the take into the recordings folder, records it in the history and removes the take. Safe off the UI thread.</summary>
    private async Task ExportToFolderAsync(Take take, string dest)
    {
        await RecorderExporter.ExportMp4Async(take, dest, null, CancellationToken.None).ConfigureAwait(false);
        take.MarkExported();
        RecordInHistory(dest, take);
        take.Delete();
    }

    private async Task DirectSaveAsync(Take take, string? reason)
    {
        try
        {
            var dest = Output.UniquePath(Output.BaseFolder(_ctx.App.Get<string>(K.RecSaveFolder)), RecorderSupport.DefaultName(DateTime.Now), ".mp4");
            await RecorderExporter.ExportMp4Async(take, dest, null, CancellationToken.None);
            take.MarkExported();
            RecordInHistory(dest, take);
            QuickToolHud.Show(G.Record, reason ?? string.Format(Strings.Recorder.SavedFormat, Path.GetFileName(Path.GetDirectoryName(dest)) ?? Strings.Desktop));
            take.Delete();
        }
        catch (Exception ex)
        {
            CaptureHost.Log("direct save failed", ex);
            CaptureHost.Beep();
            QuickToolHud.Show(G.Record, Strings.Recorder.RecordFailed);
            OpenEditor(take);
        }
    }

    public void RecordInHistory(string path, Take take)
    {
        _ = Task.Run(async () =>
        {
            System.Windows.Media.Imaging.BitmapSource? thumb = null;
            try
            {
                thumb = await RecorderExporter.FrameAtAsync(take.VideoPath, TimeSpan.Zero, 360);
            }
            catch
            {
                // thumbnail is optional
            }

            RecentCaptureService.Instance.RecordRecording(path, thumb);
        });
    }

    private void CloseSurfaces()
    {
        _pill?.Close();
        _pill = null;
        _guide?.Close();
        _guide = null;
    }

    public void Shutdown()
    {
        _generation++;
        SystemEvents.SessionEnding -= OnSessionEnding;
        if (IsRecording)
        {
            _exitMode = 1;
            Stop(null);
        }

        if (_finishTask is { IsCompleted: false } finishing && !finishing.Wait(TimeSpan.FromSeconds(15)))
        {
            CaptureHost.Log("recording not finished before exit; kept for recovery");
        }

        _preparing = false;
        CloseSurfaces();
        foreach (var e in _editors.ToList())
        {
            e.ForceClose();
        }
    }
}
