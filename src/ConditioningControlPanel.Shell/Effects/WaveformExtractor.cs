using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Shell.Effects;

/// <summary>
/// Extracts a normalized waveform (peak per bucket, 0..1) from any audio file by
/// shelling out to ffmpeg to decode it to mono 16-bit PCM. Cross-platform, no GPL
/// linkage (ffmpeg runs as a separate process), no NAudio/Windows dependency —
/// which is exactly why we decode this way instead of borrowing a DAW's decoder.
/// </summary>
internal static class WaveformExtractor
{
    /// <summary>
    /// Returns <paramref name="buckets"/> normalized peaks (0..1) across the clip,
    /// or an empty array if ffmpeg is missing / the file can't be decoded.
    /// </summary>
    public static async Task<float[]> ExtractAsync(string path, int buckets = 1500, int sampleRate = 4000)
    {
        if (buckets < 1) buckets = 1;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("-v");  psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-i");  psi.ArgumentList.Add(path);
            psi.ArgumentList.Add("-ac"); psi.ArgumentList.Add("1");
            psi.ArgumentList.Add("-ar"); psi.ArgumentList.Add(sampleRate.ToString());
            psi.ArgumentList.Add("-f");  psi.ArgumentList.Add("s16le");
            psi.ArgumentList.Add("-");

            using var proc = Process.Start(psi);
            if (proc == null) return Array.Empty<float>();

            using var ms = new MemoryStream();
            // drain stdout (PCM) and stderr concurrently so a chatty stderr can't
            // fill its pipe and deadlock the stdout copy
            var pcmTask = proc.StandardOutput.BaseStream.CopyToAsync(ms);
            var errTask = proc.StandardError.ReadToEndAsync();
            await Task.WhenAll(pcmTask, errTask);
            await proc.WaitForExitAsync();

            var bytes = ms.GetBuffer();               // may be longer than Length — bound by sampleCount
            int sampleCount = (int)(ms.Length / 2);
            if (sampleCount <= 0) return Array.Empty<float>();

            var peaks = new float[buckets];
            for (int b = 0; b < buckets; b++)
            {
                long start = (long)b * sampleCount / buckets;
                long end   = (long)(b + 1) * sampleCount / buckets;
                if (end <= start) end = start + 1;

                int max = 0;
                for (long i = start; i < end && i < sampleCount; i++)
                {
                    int s = (short)(bytes[i * 2] | (bytes[i * 2 + 1] << 8));  // little-endian s16
                    int a = s < 0 ? -s : s;
                    if (a > max) max = a;
                }
                peaks[b] = Math.Min(1f, max / 32768f);
            }
            return peaks;
        }
        catch { return Array.Empty<float>(); }
    }
}
