// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace WinKnife.Features.Capture;

/// <summary>
/// Pitch-preserving time stretch (WSOLA: waveform-similarity overlap-add) for interleaved stereo float PCM. Media Foundation has no
/// simple stretcher, so export speed is applied here, on the mixed sidecar audio, after trim and cuts.
/// Window 40 ms, 50 % overlap, +/-10 ms search; the analysis hop follows the speed factor.
/// </summary>
internal static class TimeStretch
{
    private const int Channels = 2;

    /// <summary>Returns audio that plays <paramref name="speed"/> times faster (0.25..4) at the same pitch; the length is frames/speed.</summary>
    public static float[] Stretch(float[] interleaved, double speed, int sampleRate = 48000)
    {
        if (interleaved.Length == 0 || !double.IsFinite(speed) || Math.Abs(speed - 1) < 0.0005)
        {
            return interleaved;
        }

        speed = Math.Clamp(speed, 0.25, 4);
        var frames = interleaved.Length / Channels;
        var window = (int)(0.040 * sampleRate) & ~1;
        var hop = window / 2;
        var tolerance = (int)(0.010 * sampleRate);
        var outFrames = (int)Math.Floor(frames / speed);
        var output = new float[outFrames * Channels];
        if (outFrames <= 0)
        {
            return output;
        }

        // mono copy for the similarity search
        var mono = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            mono[i] = (interleaved[i * 2] + interleaved[i * 2 + 1]) * 0.5f;
        }

        var hann = new float[window];
        for (var i = 0; i < window; i++)
        {
            hann[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / window));
        }

        var analysisHop = hop * speed;
        var chosen = 0;
        var step = Math.Max(1, sampleRate / 12000); // correlate on a decimated grid
        for (var k = 0; ; k++)
        {
            var outPos = k * hop;
            if (outPos >= outFrames)
            {
                break;
            }

            if (k > 0)
            {
                var target = (int)Math.Round(k * analysisHop);
                var natural = chosen + hop; // where the previous grain would continue
                chosen = BestMatch(mono, frames, natural, target, hop, tolerance, step);
            }

            // overlap-add one windowed grain
            for (var i = 0; i < window; i++)
            {
                var o = outPos + i;
                if (o >= outFrames)
                {
                    break;
                }

                var s = chosen + i;
                if (s >= frames)
                {
                    break;
                }

                var w = hann[i];
                output[o * 2] += interleaved[s * 2] * w;
                output[o * 2 + 1] += interleaved[s * 2 + 1] * w;
            }

            if ((int)Math.Round((k + 1) * analysisHop) >= frames + window)
            {
                break;
            }
        }

        return output;
    }

    private static int BestMatch(float[] mono, int frames, int natural, int target, int length, int tolerance, int step)
    {
        if (natural < 0 || natural + length > frames)
        {
            return Math.Clamp(target, 0, Math.Max(0, frames - 1));
        }

        var best = Math.Clamp(target, 0, Math.Max(0, frames - 1));
        var bestScore = double.NegativeInfinity;
        for (var delta = -tolerance; delta <= tolerance; delta += 2)
        {
            var c = target + delta;
            if (c < 0 || c + length > frames)
            {
                continue;
            }

            double score = 0;
            double energy = 1e-9;
            for (var i = 0; i < length; i += step)
            {
                var a = mono[natural + i];
                var b = mono[c + i];
                score += a * b;
                energy += b * b;
            }

            // normalised correlation, with a tiny pull towards the unshifted position so silence does not wander
            var s = score / Math.Sqrt(energy) - Math.Abs(delta) * 1e-7;
            if (s > bestScore)
            {
                bestScore = s;
                best = c;
            }
        }

        return best;
    }
}
