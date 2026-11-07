// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.FaceAnalysis;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

namespace Trayloom.Features.WindowTools.AwayMode;

/// <summary>
/// Notices a face in front of the PC while the person is away (spec 06 section 4.7). Face <i>detection</i> only: a frame is checked in
/// memory and disposed at once; nothing is written to disk, kept, logged or sent anywhere. The camera is opened in the shared
/// read-only mode so Trayloom's camera preview and the island mirror can use it at the same time.
/// </summary>
internal sealed class AwayFaceWatcher : IDisposable
{
    /// <summary>Samples are taken this often; during a quick look twice a second.</summary>
    private const int NormalIntervalMs = 3000;
    private const int QuickIntervalMs = 500;

    /// <summary>A face must be at least this tall (share of the frame height) to count: someone at the screen, not a poster across the room.</summary>
    private const double MinFaceHeight = 0.12;

    private const int NoFrameTimeoutMs = 10000;

    /// <summary>What <see cref="StartAsync"/> returns when a stop came while the camera was opening: not a failure, nothing is left on.</summary>
    public const string Cancelled = "cancelled";

    private readonly ILog _log;
    private readonly object _gate = new();

    /// <summary>One start at a time: two overlapping starts would open two captures and the first would never be closed (light stays on).</summary>
    private readonly SemaphoreSlim _startGate = new(1, 1);

    /// <summary>Bumped by every stop, so a start that was opening the camera at that moment closes it again instead of finishing.</summary>
    private int _generation;
    private MediaCapture? _capture;
    private MediaFrameReader? _reader;
    private FaceDetector? _detector;
    private Timer? _watchdog;
    private long _due;
    private long _lastFrame;
    private volatile bool _quick;
    private volatile int _normalMs = NormalIntervalMs;
    private int _busy;
    private bool _running;
    private byte[]? _previousGrid;
    private long _previousGridAt;

    public AwayFaceWatcher(ILog log)
    {
        _log = log;
    }

    /// <summary>Raised on a worker thread with <c>true</c> when a sample saw a face, <c>false</c> when it did not.</summary>
    public event Action<bool>? Sample;

    /// <summary>
    /// Raised on a worker thread with what a sample showed (face, movement, usable picture) while <see cref="MeasureMotion"/> is on.
    /// Only these four flags leave the watcher; the picture itself never does.
    /// </summary>
    public event Action<AwayObservation>? Observed;

    /// <summary>Raised on a worker thread when the camera failed (the controller then falls back to input).</summary>
    public event Action<string>? Failed;

    /// <summary>True while the presence watch needs movement too, not only faces (the camera looks for someone sitting at the PC).</summary>
    public bool MeasureMotion { get; set; }

    /// <summary>True between a successful start and the stop.</summary>
    public bool IsRunning => _running;

    /// <summary>Developer switch: no <c>MediaCapture</c>, samples only come from <c>simulate-face</c>.</summary>
    public static bool NoCamera => Environment.GetEnvironmentVariable("TRAYLOOM_AWAY_NO_CAMERA") == "1";

    /// <summary>False when this PC cannot detect faces at all.</summary>
    public static bool IsSupported
    {
        get
        {
            try
            {
                return FaceDetector.IsSupported;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>The camera permission for desktop apps.</summary>
    public static DeviceAccessStatus Access
    {
        get
        {
            try
            {
                return DeviceAccessInformation.CreateFromDeviceClass(DeviceClass.VideoCapture).CurrentStatus;
            }
            catch (Exception)
            {
                return DeviceAccessStatus.Unspecified;
            }
        }
    }

    /// <summary>True when the camera permission is off (the switch is disabled with the reason).</summary>
    public static bool AccessDenied => Access is DeviceAccessStatus.DeniedByUser or DeviceAccessStatus.DeniedBySystem;

    /// <summary>The cameras on this PC (id and name), front camera first.</summary>
    public static async Task<IReadOnlyList<(string Id, string Name)>> ListCamerasAsync()
    {
        var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture).AsTask();
        return [.. devices.OrderBy(d => d.EnclosureLocation?.Panel == Windows.Devices.Enumeration.Panel.Front ? 0 : 1).Select(d => (d.Id, d.Name))];
    }

    /// <summary>The camera to use: the chosen one, else the front camera, else the first whose name has no "IR", else the first.</summary>
    private static async Task<string?> ChooseAsync(string preferredId)
    {
        var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture).AsTask();
        if (devices.Count == 0)
        {
            return null;
        }

        return devices.FirstOrDefault(d => d.Id == preferredId && preferredId.Length > 0)?.Id
            ?? devices.FirstOrDefault(d => d.EnclosureLocation?.Panel == Windows.Devices.Enumeration.Panel.Front)?.Id
            ?? devices.FirstOrDefault(d => !d.Name.Contains("IR", StringComparison.Ordinal))?.Id
            ?? devices[0].Id;
    }

    /// <summary>
    /// Starts the camera. Returns null when it runs (or already ran), <see cref="Cancelled"/> when a stop came while it was opening, and
    /// otherwise the reason it could not start; nothing is raised in either case.
    /// </summary>
    public async Task<string?> StartAsync(string cameraId)
    {
        await _startGate.WaitAsync();
        try
        {
            return await StartCoreAsync(cameraId);
        }
        finally
        {
            _startGate.Release();
        }
    }

    private async Task<string?> StartCoreAsync(string cameraId)
    {
        if (_running)
        {
            return null;
        }

        var generation = Volatile.Read(ref _generation);
        if (NoCamera)
        {
            _running = true;
            _log.Info("[away] camera: started (simulated samples only)");
            return null;
        }

        try
        {
            if (!IsSupported)
            {
                return "face detection unsupported";
            }

            if (AccessDenied)
            {
                return "access denied";
            }

            var id = await ChooseAsync(cameraId);
            if (id is null)
            {
                return "no camera";
            }

            _detector ??= await FaceDetector.CreateAsync().AsTask();
            var capture = new MediaCapture();
            await capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                VideoDeviceId = id,
                SharingMode = MediaCaptureSharingMode.SharedReadOnly,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
            }).AsTask();

            var source = capture.FrameSources.Values.FirstOrDefault(s => s.Info.SourceKind == MediaFrameSourceKind.Color);
            if (source is null)
            {
                capture.Dispose();
                return "no color source";
            }

            var nv12 = source.SupportedFormats.Any(f => string.Equals(f.Subtype, MediaEncodingSubtypes.Nv12, StringComparison.OrdinalIgnoreCase));
            var bgra = source.SupportedFormats.Any(f => string.Equals(f.Subtype, MediaEncodingSubtypes.Bgra8, StringComparison.OrdinalIgnoreCase));
            var subtype = nv12 ? MediaEncodingSubtypes.Nv12 : bgra ? MediaEncodingSubtypes.Bgra8 : null;
            var reader = subtype is null
                ? await capture.CreateFrameReaderAsync(source).AsTask()
                : await capture.CreateFrameReaderAsync(source, subtype).AsTask();
            reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
            reader.FrameArrived += OnFrame;

            lock (_gate)
            {
                _capture = capture;
                _reader = reader;
                _lastFrame = Environment.TickCount64;
                _due = 0;

                // Created with the capture so every teardown, including the one of a cancelled start, disposes it too.
                _watchdog = new Timer(_ => CheckFrames(), null, 2000, 2000);
            }

            var status = await reader.StartAsync().AsTask();
            if (status != MediaFrameReaderStartStatus.Success)
            {
                Teardown();
                return "reader " + status;
            }

            if (Volatile.Read(ref _generation) != generation)
            {
                // The stop found nothing to close yet; the camera opened after it, so it is closed here and the light goes off.
                Teardown();
                return Cancelled;
            }

            _running = true;
            var format = source.CurrentFormat;
            _log.Info($"[away] camera: started ({(subtype ?? format.Subtype)} {format.VideoFormat?.Width}x{format.VideoFormat?.Height})");
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            Teardown();
            return "access denied";
        }
        catch (Exception ex)
        {
            Teardown();
            return $"{ex.GetType().Name} 0x{ex.HResult:X8}";
        }
    }

    /// <summary>Samples twice a second for a short while (an input event while a face is expected).</summary>
    public void SetQuickLook(bool on)
    {
        _quick = on;
        Interlocked.Exchange(ref _due, 0);
    }

    /// <summary>Changes how often a frame is looked at (the presence watch looks every second, then every few seconds once a face is seen).</summary>
    public void SetInterval(int ms)
    {
        _normalMs = Math.Max(QuickIntervalMs, ms);
        Interlocked.Exchange(ref _due, 0);
    }

    /// <summary>Turns the camera off. Safe to call repeatedly.</summary>
    public void Stop(string reason)
    {
        Interlocked.Increment(ref _generation);
        var wasRunning = _running;
        _running = false;
        _quick = false;
        MeasureMotion = false;
        _normalMs = NormalIntervalMs;
        Teardown();
        if (wasRunning)
        {
            _log.Info($"[away] camera: stopped ({reason})");
        }
    }

    public void Dispose() => Stop("stop");

    /// <summary>Feeds a sample as if the camera had produced it (developer action <c>simulate-face</c>).</summary>
    public void Feed(bool positive) => Sample?.Invoke(positive);

    /// <summary>Feeds an observation as if the camera had produced it (developer actions <c>simulate-absence</c> and friends).</summary>
    public void FeedObservation(AwayObservation observation) => Observed?.Invoke(observation);

    private void Teardown()
    {
        MediaFrameReader? reader;
        MediaCapture? capture;
        Timer? watchdog;
        lock (_gate)
        {
            reader = _reader;
            capture = _capture;
            watchdog = _watchdog;
            _reader = null;
            _capture = null;
            _watchdog = null;

            // The brightness thumbnail of the last sample goes with the camera.
            _previousGrid = null;
            _previousGridAt = 0;
        }

        try
        {
            watchdog?.Dispose();
            if (reader is not null)
            {
                reader.FrameArrived -= OnFrame;
                reader.StopAsync().AsTask().Wait(2000);
                reader.Dispose();
            }

            capture?.Dispose();
        }
        catch (Exception ex)
        {
            _log.Info($"[away] camera: teardown {ex.GetType().Name} 0x{ex.HResult:X8}");
        }
    }

    private void CheckFrames()
    {
        if (_running && !NoCamera && Environment.TickCount64 - Interlocked.Read(ref _lastFrame) > NoFrameTimeoutMs)
        {
            _running = false;
            Teardown();
            Failed?.Invoke("no frames");
        }
    }

    private void OnFrame(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        // Frames that arrive before the next due time are dropped without being looked at.
        using var reference = sender.TryAcquireLatestFrame();
        if (reference is null)
        {
            return;
        }

        Interlocked.Exchange(ref _lastFrame, Environment.TickCount64);
        var now = Environment.TickCount64;
        if (now < Interlocked.Read(ref _due) || Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _due, now + (_quick ? QuickIntervalMs : _normalMs));
        var bitmap = reference.VideoMediaFrame?.SoftwareBitmap;
        if (bitmap is null)
        {
            Interlocked.Exchange(ref _busy, 0);
            return;
        }

        // The bitmap belongs to the frame, which is disposed when this handler returns: copy it for the asynchronous check.
        var copy = SoftwareBitmap.Copy(bitmap);
        AwayGuard.FireAndLog(() => CheckAsync(copy), _log, "face check");
    }

    private async Task CheckAsync(SoftwareBitmap bitmap)
    {
        SoftwareBitmap? converted = null;
        try
        {
            var detector = _detector;
            if (detector is null)
            {
                return;
            }

            var measure = MeasureMotion;
            var candidate = bitmap;
            if (measure)
            {
                // Gray8 is accepted by the face detector and is also what the brightness grid needs.
                if (bitmap.BitmapPixelFormat != BitmapPixelFormat.Gray8)
                {
                    converted = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Gray8);
                    candidate = converted;
                }
            }
            else if (!FaceDetector.IsBitmapPixelFormatSupported(candidate.BitmapPixelFormat))
            {
                converted = SoftwareBitmap.Convert(candidate, BitmapPixelFormat.Gray8);
                candidate = converted;
            }

            var faces = await detector.DetectFacesAsync(candidate).AsTask();
            var height = candidate.PixelHeight;
            var positive = faces.Any(f => height > 0 && f.FaceBox.Height >= MinFaceHeight * height);
            if (!measure)
            {
                // The presence watch looks once a second: no line per sample there.
                if (positive)
                {
                    _log.Info("[away] camera: face seen");
                }

                Sample?.Invoke(positive);
                return;
            }

            Observed?.Invoke(Observe(candidate, positive));
        }
        finally
        {
            converted?.Dispose();
            bitmap.Dispose();
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    /// <summary>
    /// Reduces a gray frame to a 64 x 48 brightness grid, compares it with the previous one (when that is recent enough) and keeps only
    /// the new grid. The grid is 3 KB of block averages; it is never written, logged or sent.
    /// </summary>
    private AwayObservation Observe(SoftwareBitmap gray, bool face)
    {
        var width = gray.PixelWidth;
        var height = gray.PixelHeight;
        int start;
        int stride;
        using (var buffer = gray.LockBuffer(BitmapBufferAccessMode.Read))
        {
            var plane = buffer.GetPlaneDescription(0);
            start = plane.StartIndex;
            stride = plane.Stride;
        }

        var copy = new Windows.Storage.Streams.Buffer((uint)(start + (stride * height)));
        gray.CopyToBuffer(copy);
        var bytes = new byte[copy.Length];
        using (var reader = DataReader.FromBuffer(copy))
        {
            reader.ReadBytes(bytes);
        }

        var grid = AwayPresenceMath.Grid(bytes, start, width, height, stride);
        var usable = AwayPresenceMath.Usable(AwayPresenceMath.Stats(grid));
        var now = Environment.TickCount64;
        var previous = _previousGrid;
        var motionKnown = previous is not null && AwayPresenceMath.Comparable(now - _previousGridAt);
        var motion = motionKnown && AwayPresenceMath.Motion(previous!, grid);
        _previousGrid = grid;
        _previousGridAt = now;
        return new AwayObservation(face, motion, usable, motionKnown);
    }
}
