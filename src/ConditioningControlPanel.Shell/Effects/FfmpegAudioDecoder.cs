using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Shell.Effects;

/// <summary>
/// Decodes any audio file ffmpeg can read into mono float PCM for envelope
/// extraction. ffmpeg ships on the Pi; if it's missing we just return null and
/// the haptics fall back to flat accents — never a hard failure.
/// </summary>
internal static class FfmpegAudioDecoder
{
    public const int SampleRate = 16000;

    public static async Task<float[]?> DecodeMonoPcmAsync(string path)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-v error -i \"{path}\" -f f32le -ac 1 -ar {SampleRate} -",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return null;

            using var ms = new MemoryStream();
            await proc.StandardOutput.BaseStream.CopyToAsync(ms);
            await proc.WaitForExitAsync();
            if (proc.ExitCode != 0 || ms.Length < 4) return null;

            var bytes = ms.ToArray();
            var samples = new float[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * 4);
            return samples;
        }
        catch
        {
            return null;
        }
    }
}
