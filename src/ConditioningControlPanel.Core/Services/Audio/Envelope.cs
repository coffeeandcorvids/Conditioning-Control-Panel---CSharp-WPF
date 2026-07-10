using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Core.Services.Audio;

/// <summary>
/// A normalized intensity curve extracted from audio — the voice's own
/// loudness contour, downsampled to what a toy can follow. The base layer of
/// the voice-haptics mixer (docs/VOICE-HAPTICS-DESIGN.md): not an
/// approximation of vocal cords, the real waveform's RMS envelope.
/// Pure math on PCM floats; decoding lives with the host.
/// </summary>
public sealed class Envelope
{
    /// <summary>Intensity per hop, each 0..1.</summary>
    public IReadOnlyList<float> Intensities { get; }
    public int HopMs { get; }
    public int DurationMs => Intensities.Count * HopMs;

    private Envelope(float[] intensities, int hopMs)
    {
        Intensities = intensities;
        HopMs = hopMs;
    }

    /// <summary>
    /// Extract from mono PCM samples (-1..1). RMS per <paramref name="hopMs"/> window,
    /// normalized so the loudest hop is 1.0. A silent clip yields all zeros.
    /// </summary>
    public static Envelope FromPcm(ReadOnlySpan<float> samples, int sampleRate, int hopMs = 50)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (hopMs <= 0) throw new ArgumentOutOfRangeException(nameof(hopMs));

        var hopSamples = Math.Max(1, sampleRate * hopMs / 1000);
        var hops = Math.Max(1, (samples.Length + hopSamples - 1) / hopSamples);
        var rms = new float[hops];

        for (var h = 0; h < hops; h++)
        {
            var start = h * hopSamples;
            var end = Math.Min(start + hopSamples, samples.Length);
            double sum = 0;
            for (var i = start; i < end; i++) sum += samples[i] * samples[i];
            var n = Math.Max(1, end - start);
            rms[h] = (float)Math.Sqrt(sum / n);
        }

        var peak = rms.Max();
        if (peak > 0)
            for (var h = 0; h < hops; h++) rms[h] /= peak;

        return new Envelope(rms, hopMs);
    }

    /// <summary>Intensity at a moment (0..1); zero outside the clip.</summary>
    public float At(long ms)
    {
        if (ms < 0) return 0;
        var idx = (int)(ms / HopMs);
        return idx < Intensities.Count ? Intensities[idx] : 0;
    }

    /// <summary>
    /// The curve for a window of the clip (e.g. one cue's word), re-normalized to
    /// its own peak so a quiet word still uses the toy's range. Empty window → empty.
    /// </summary>
    public float[] Slice(long fromMs, long toMs, bool renormalize = true)
    {
        var from = Math.Max(0, (int)(fromMs / HopMs));
        var to = Math.Min(Intensities.Count, (int)Math.Ceiling(toMs / (double)HopMs));
        if (to <= from) return Array.Empty<float>();

        var slice = new float[to - from];
        for (var i = 0; i < slice.Length; i++) slice[i] = Intensities[from + i];

        if (renormalize)
        {
            var peak = slice.Max();
            if (peak > 0)
                for (var i = 0; i < slice.Length; i++) slice[i] /= peak;
        }
        return slice;
    }
}
