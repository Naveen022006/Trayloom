// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace Trayloom.Features.Island;

/// <summary>
/// 7-band level meter of the system output (WASAPI loopback of the default endpoint, 1024-sample Hann FFT at 30 Hz,
/// bands 50-12000 Hz, -60 dB floor, attack 0.55 / decay 0.12 smoothing). Never stores or sends audio.
/// Per-process loopback needs ActivateAudioInterfaceAsync; the endpoint loopback is the documented fallback and is only
/// used while a player is playing.
/// </summary>
internal sealed class AudioLevelService : IDisposable
{
    private const int Fft = 1024;
    private const int RingSize = 8192;
    private static readonly double[] Edges = [50, 120, 250, 500, 1000, 2000, 4500, 12000];

    private readonly float[] _ring = new float[RingSize];
    private readonly float[] _window = new float[Fft];
    private readonly double[] _smooth = new double[7];
    private readonly object _gate = new();
    private WasapiLoopbackCapture? _capture;
    private Timer? _timer;
    private int _write;
    private int _sampleRate = 48000;
    private int _channels = 2;
    private float _peak;
    private double _ceiling = 0.05;
    private DateTime _started;
    private bool _heard;
    private bool _gaveUp;
    private string _gaveUpFor = "";
    private double[]? _levels;
    private bool _running;

    public AudioLevelService()
    {
        for (var i = 0; i < Fft; i++)
        {
            _window[i] = (float)(0.5 * (1 - Math.Cos(2 * Math.PI * i / (Fft - 1))));
        }
    }

    /// <summary>7 band levels (0..1) or null when live levels are unavailable (synthetic motion applies).</summary>
    public double[]? Levels => _levels;

    public event Action<double[]?>? LevelsChanged;

    public bool IsRunning => _running;

    /// <summary>Starts for a play identity; after 8 s of silence it gives up for that identity.</summary>
    public void Start(string identity)
    {
        if (_running)
        {
            return;
        }

        if (_gaveUp && _gaveUpFor == identity)
        {
            return;
        }

        if (_gaveUpFor != identity)
        {
            _gaveUp = false;
        }

        try
        {
            _capture = new WasapiLoopbackCapture();
            _sampleRate = _capture.WaveFormat.SampleRate;
            _channels = Math.Max(1, _capture.WaveFormat.Channels);
            _capture.DataAvailable += OnData;
            _capture.RecordingStopped += (_, _) => { };
            _gaveUpFor = identity;
            _started = DateTime.UtcNow;
            _heard = false;
            _peak = 0;
            Array.Clear(_ring);
            Array.Clear(_smooth);
            _ceiling = 0.05;
            _capture.StartRecording();
            _timer = new Timer(_ => Analyse(), null, 33, 33);
            _running = true;
        }
        catch
        {
            Stop();
        }
    }

    public void Stop()
    {
        _running = false;
        _timer?.Dispose();
        _timer = null;
        try
        {
            if (_capture is not null)
            {
                _capture.DataAvailable -= OnData;
                _capture.StopRecording();
                _capture.Dispose();
            }
        }
        catch
        {
        }

        _capture = null;
        if (_levels is not null)
        {
            _levels = null;
            LevelsChanged?.Invoke(null);
        }
    }

    public void ForgetSilence() => _gaveUp = false;

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (_capture is null || e.BytesRecorded == 0)
        {
            return;
        }

        var format = _capture.WaveFormat;
        if (format.Encoding != WaveFormatEncoding.IeeeFloat && !(format.Encoding == WaveFormatEncoding.Extensible && format.BitsPerSample == 32))
        {
            return;
        }

        if (!Monitor.TryEnter(_gate))
        {
            return;
        }

        try
        {
            var frames = e.BytesRecorded / (4 * _channels);
            var buffer = e.Buffer;
            for (var f = 0; f < frames; f++)
            {
                float sum = 0;
                for (var c = 0; c < _channels; c++)
                {
                    sum += BitConverter.ToSingle(buffer, ((f * _channels) + c) * 4);
                }

                var mono = sum / _channels;
                _ring[_write] = mono;
                _write = (_write + 1) % RingSize;
                var a = Math.Abs(mono);
                if (a > _peak)
                {
                    _peak = a;
                }
            }
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    private void Analyse()
    {
        if (!_running)
        {
            return;
        }

        var data = new Complex[Fft];
        lock (_gate)
        {
            var start = (_write - Fft + RingSize) % RingSize;
            for (var i = 0; i < Fft; i++)
            {
                data[i].X = _ring[(start + i) % RingSize] * _window[i];
                data[i].Y = 0;
            }
        }

        FastFourierTransform.FFT(true, 10, data);
        var raw = new double[7];
        var binWidth = (double)_sampleRate / Fft;
        var usable = Fft / 2;
        for (var b = 0; b < 7; b++)
        {
            var lo = Math.Clamp((int)Math.Floor(Edges[b] / binWidth), 1, usable - 1);
            var hi = Math.Clamp((int)Math.Floor(Edges[b + 1] / binWidth), lo + 1, usable);
            double sum = 0;
            for (var k = lo; k < hi; k++)
            {
                sum += Math.Sqrt((data[k].X * data[k].X) + (data[k].Y * data[k].Y));
            }

            var mean = sum / (hi - lo) / 1.0;
            var db = 20 * Math.Log10(Math.Max(mean, 1e-9));
            raw[b] = Math.Clamp((db + 60) / 60, 0, 1);
        }

        var maxRaw = raw.Max();
        _ceiling = Math.Max(Math.Max(maxRaw, _ceiling * 0.995), 0.05);
        var gain = 0.92 / _ceiling;
        if (_peak * gain > 0.001)
        {
            _heard = true;
        }

        _peak = 0;
        if (!_heard)
        {
            if ((DateTime.UtcNow - _started).TotalSeconds > 8)
            {
                _gaveUp = true;
                Stop();
            }

            return;
        }

        var result = new double[7];
        for (var i = 0; i < 7; i++)
        {
            var target = Math.Clamp(raw[i] * gain, 0, 1);
            var rate = target > _smooth[i] ? 0.55 : 0.12;
            _smooth[i] += (target - _smooth[i]) * rate;
            result[i] = _smooth[i];
        }

        _levels = result;
        LevelsChanged?.Invoke(result);
    }

    public void Dispose() => Stop();
}
