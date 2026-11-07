// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Media;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Trayloom.Core;

namespace Trayloom.Features.Island;

/// <summary>The pure rules of the timer alarm (spec 10a): when it rings, when it can be snoozed and how loud it is.</summary>
internal static class TimerAlarmRules
{
    /// <summary>A repeating alarm stops by itself after this long, so a forgotten one never rings for ever.</summary>
    public const int MaxRingSeconds = 60;

    /// <summary>The stopwatch never ends, so it never rings; every other phase does when the alarm is on.</summary>
    public static bool ShouldRing(TimerPhase phase, bool enabled) => enabled && phase != TimerPhase.Stopwatch;

    /// <summary>Only a countdown that has finished can be extended.</summary>
    public static bool CanSnooze(TimerMode mode, bool completed) => completed && mode != TimerMode.Stopwatch;

    /// <summary>Snoozing a finished focus reopens it, so the session it counted must not count until it really ends.</summary>
    public static int FocusesAfterSnooze(int completedFocuses, TimerPhase phase) => phase == TimerPhase.Focus ? Math.Max(0, completedFocuses - 1) : completedFocuses;

    /// <summary>The stored 0..100 volume as a gain.</summary>
    public static float Volume(int raw) => Math.Clamp(raw, 0, 100) / 100f;
}

/// <summary>The alarm sounds: two generated tones and the Windows alarm files that exist on this machine.</summary>
internal static class AlarmSounds
{
    public const string Default = "classic";

    private const string Gentle = "gentle";
    private const long MaxFileBytes = 5L * 1024 * 1024;
    private const int SampleRate = 44100;
    private static readonly Regex s_windowsId = new("^alarm(0[1-9]|10)$", RegexOptions.CultureInvariant);
    private static readonly Dictionary<string, (float[] Data, WaveFormat Format)> s_cache = [];

    /// <summary>The sounds to offer: the generated ones, then each Windows alarm file that is present.</summary>
    public static IReadOnlyList<(string Id, string Label)> Available()
    {
        var list = new List<(string Id, string Label)> { (Default, Loc.T("Classic beep")), (Gentle, Loc.T("Gentle chime")) };
        for (var n = 1; n <= 10; n++)
        {
            var id = "alarm" + n.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
            var file = FileFor(id);
            if (file is not null && File.Exists(file))
            {
                list.Add((id, Loc.F("Windows alarm {0}", n)));
            }
        }

        return list;
    }

    /// <summary>The Windows media file of an id such as "alarm03", or null for any other id.</summary>
    public static string? FileFor(string id)
    {
        if (!s_windowsId.IsMatch(id))
        {
            return null;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "A" + id[1..] + ".wav");
    }

    /// <summary>An unknown id, or a Windows alarm whose file is gone, becomes the default.</summary>
    public static string Resolve(string? id, Func<string, bool> fileExists)
    {
        if (id is Default or Gentle)
        {
            return id;
        }

        if (id is not null && FileFor(id) is { } file && fileExists(file))
        {
            return id;
        }

        return Default;
    }

    /// <summary>Mono samples of a generated tone, peak at most 0.8. The same input always gives the same samples.</summary>
    public static float[] Generate(string id, int sampleRate) => id == Gentle ? GenerateGentle(sampleRate) : GenerateClassic(sampleRate);

    private static float[] GenerateClassic(int rate)
    {
        var beep = rate * 120 / 1000;
        var fade = Math.Max(1, rate * 5 / 1000);
        var gap = rate * 80 / 1000;
        var tail = rate * 900 / 1000;
        var data = new float[4 * (beep + gap) + tail];
        for (var b = 0; b < 4; b++)
        {
            var start = b * (beep + gap);
            for (var i = 0; i < beep; i++)
            {
                // A short fade at both ends keeps the beep from clicking.
                var env = Math.Min(1.0, Math.Min(i, beep - 1 - i) / (double)fade);
                data[start + i] = (float)(0.7 * env * Math.Sin(2 * Math.PI * 880 * i / rate));
            }
        }

        return data;
    }

    private static float[] GenerateGentle(int rate)
    {
        var note = rate * 350 / 1000;
        var gap = rate * 150 / 1000;
        var tail = rate * 1200 / 1000;
        var data = new float[2 * note + 2 * gap + tail];
        var freqs = new[] { 660.0, 880.0 };
        for (var n = 0; n < 2; n++)
        {
            var start = n * (note + gap);
            for (var i = 0; i < note; i++)
            {
                var t = i / (double)rate;
                var tone = Math.Sin(2 * Math.PI * freqs[n] * t) + 0.4 * Math.Sin(4 * Math.PI * freqs[n] * t);
                // The decay is the chime; the 5 ms attack removes the click at the start.
                var env = Math.Exp(-t / 0.25) * Math.Min(1.0, i / (double)Math.Max(1, rate * 5 / 1000));
                data[start + i] = (float)(0.5 * env * tone);
            }
        }

        return data;
    }

    /// <summary>The samples and format of a sound, cached per id. A Windows file is read whole and refused when it is over 5 MB.</summary>
    public static (float[] Data, WaveFormat Format) Load(string id, ILog log)
    {
        lock (s_cache)
        {
            if (s_cache.TryGetValue(id, out var cached))
            {
                return cached;
            }
        }

        (float[] Data, WaveFormat Format) loaded;
        var generated = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1);
        if (FileFor(id) is { } file)
        {
            try
            {
                if (new FileInfo(file).Length > MaxFileBytes)
                {
                    throw new InvalidDataException("alarm file too large");
                }

                using var reader = new AudioFileReader(file);
                var all = new List<float>((int)Math.Min(reader.Length / 4, int.MaxValue / 2));
                var buffer = new float[16384];
                int read;
                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    all.AddRange(new ArraySegment<float>(buffer, 0, read));
                }

                loaded = (all.ToArray(), WaveFormat.CreateIeeeFloatWaveFormat(reader.WaveFormat.SampleRate, reader.WaveFormat.Channels));
            }
            catch (Exception ex)
            {
                log.Warn($"Timer alarm: could not read {id}, using the default ({ex.GetType().Name})");
                loaded = (Generate(Default, SampleRate), generated);
            }
        }
        else
        {
            loaded = (Generate(id, SampleRate), generated);
        }

        lock (s_cache)
        {
            s_cache[id] = loaded;
        }

        return loaded;
    }
}

/// <summary>Plays a cached buffer, optionally for ever; the playback chain asks for more until it returns 0.</summary>
internal sealed class LoopingSamples : ISampleProvider
{
    private readonly float[] _data;
    private int _pos;

    public LoopingSamples(float[] data, WaveFormat format)
    {
        _data = data;
        WaveFormat = format;
    }

    public WaveFormat WaveFormat { get; }

    /// <summary>When set, the buffer wraps instead of ending.</summary>
    public bool Loop { get; set; }

    /// <summary>True once a non-looping buffer has been fully consumed.</summary>
    public bool Ended => !Loop && _pos >= _data.Length;

    public int Read(float[] buffer, int offset, int count)
    {
        if (_data.Length == 0)
        {
            return 0;
        }

        var written = 0;
        while (written < count)
        {
            if (_pos >= _data.Length)
            {
                if (!Loop)
                {
                    break;
                }

                _pos = 0;
            }

            var n = Math.Min(count - written, _data.Length - _pos);
            Array.Copy(_data, _pos, buffer, offset + written, n);
            _pos += n;
            written += n;
        }

        return written;
    }
}

/// <summary>
/// Plays one alarm sound on the default output device. If no device can be opened it falls back to the system sound every 2 s.
/// <see cref="Changed"/> is raised on the UI thread when playback ends or is stopped, never when it starts.
/// </summary>
internal sealed class TimerAlarm : IDisposable
{
    private static TimerAlarm? s_preview;

    private readonly Dispatcher _ui;
    private readonly ILog _log;
    private readonly DispatcherTimer _cap;
    private readonly DispatcherTimer _fallback;
    private WaveOutEvent? _out;
    private LoopingSamples? _samples;
    private int _generation;
    private bool _loop;

    public TimerAlarm(Dispatcher ui, ILog log)
    {
        _ui = ui;
        _log = log;
        _cap = new DispatcherTimer(DispatcherPriority.Normal, ui);
        _cap.Tick += (_, _) => Finish();
        _fallback = new DispatcherTimer(DispatcherPriority.Normal, ui) { Interval = TimeSpan.FromSeconds(2) };
        _fallback.Tick += (_, _) => FallbackTick();
    }

    public bool IsPlaying { get; private set; }

    /// <summary>Raised on the UI thread after playback has ended or was stopped.</summary>
    public event Action? Changed;

    /// <summary>Starts a sound, replacing any that is playing. <paramref name="cap"/> ends it even when it loops.</summary>
    public void Play(string soundId, float volume, bool loop, TimeSpan cap)
    {
        Halt();
        _loop = loop;
        var id = AlarmSounds.Resolve(soundId, File.Exists);
        IsPlaying = true;
        var useFallback = false;
        try
        {
            var (data, format) = AlarmSounds.Load(id, _log);
            _samples = new LoopingSamples(data, format) { Loop = loop };
            var gain = new VolumeSampleProvider(_samples) { Volume = Math.Clamp(volume, 0f, 1f) };
            var output = new WaveOutEvent();
            var generation = ++_generation;
            output.PlaybackStopped += (_, _) => _ui.BeginInvoke(() =>
            {
                if (generation == _generation && IsPlaying)
                {
                    Finish();
                }
            });
            output.Init(new SampleToWaveProvider16(gain));
            output.Play();
            _out = output;
        }
        catch (Exception ex)
        {
            // No output device (or a driver failure): the old system sound keeps the alarm audible.
            _log.Warn($"Timer alarm: no audio output, using the system sound ({ex.GetType().Name})");
            ReleaseOutput();
            useFallback = true;
            FallbackTick();
            _fallback.Start();
        }

        // One system sound has no natural end to wait for, so a single pass is capped at the time it takes.
        _cap.Interval = useFallback && !loop ? TimeSpan.FromSeconds(2) : (cap > TimeSpan.Zero ? cap : TimeSpan.FromSeconds(1));
        _cap.Start();
    }

    /// <summary>Stops the sound. Raises <see cref="Changed"/> only if something was playing.</summary>
    public void Stop()
    {
        if (!IsPlaying)
        {
            return;
        }

        Finish();
    }

    public void Dispose()
    {
        Halt();
        IsPlaying = false;
    }

    /// <summary>Plays one pass of a sound for at most 4 s so the user can choose; a second call restarts it.</summary>
    public static void Preview(string soundId, float volume, ILog log)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        s_preview ??= new TimerAlarm(dispatcher, log);
        s_preview.Play(soundId, volume, false, TimeSpan.FromSeconds(4));
    }

    private void Finish()
    {
        Halt();
        IsPlaying = false;
        Changed?.Invoke();
    }

    private void Halt()
    {
        _cap.Stop();
        _fallback.Stop();
        ReleaseOutput();
    }

    private void ReleaseOutput()
    {
        _generation++;
        var output = _out;
        _out = null;
        _samples = null;
        if (output is null)
        {
            return;
        }

        try
        {
            output.Stop();
            output.Dispose();
        }
        catch (Exception ex)
        {
            _log.Warn($"Timer alarm: closing the output failed ({ex.GetType().Name})");
        }
    }

    private void FallbackTick()
    {
        try
        {
            SystemSounds.Asterisk.Play();
        }
        catch
        {
            // A missing system sound is not worth more than silence.
        }

        if (!_loop)
        {
            _fallback.Stop();
        }
    }
}
