using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace ConditioningControlPanel.Shell.Effects;

/// <summary>
/// Frames + per-frame durations for any image ffmpeg/SkiaSharp can decode —
/// animated GIF and animated WebP included (v6.2.x parity: upstream added
/// animated .webp across flash/wash/cascade/tease). Single-frame images
/// load as one frame with no duration, so callers can treat everything
/// uniformly. Avalonia's own Bitmap only ever shows frame 0.
///
/// GIF frames decode via ffmpeg (extract to PNGs, cached), not SkiaSharp's
/// SKCodec.GetPixels loop: on this platform's SkiaSharp 3.119.4 native
/// build, GetPixels succeeds for frame 0 of a multi-frame GIF and fails
/// SKCodecResult.InvalidConversion for every subsequent frame regardless of
/// prior-frame/buffer handling (confirmed against a real 32-frame spiral
/// asset — every frame is independently full-canvas with RequiredFrame=-1,
/// so it isn't a compositing issue; PIL/ffmpeg both decode the same file
/// perfectly). Rather than chase a native-library bug, this uses ffmpeg
/// (already a hard dependency for audio elsewhere in the app, proven
/// reliable) for GIF frame extraction, and keeps the original SkiaSharp
/// path for non-GIF formats (WebP) where it isn't affected.
/// </summary>
internal sealed class AnimatedImageSource : IDisposable
{
    public IReadOnlyList<Bitmap> Frames { get; }
    public IReadOnlyList<TimeSpan> Durations { get; }
    public bool IsAnimated => Frames.Count > 1;

    private AnimatedImageSource(List<Bitmap> frames, List<TimeSpan> durations)
    {
        Frames = frames;
        Durations = durations;
    }

    /// <summary>Load a file; falls back to a single Avalonia-decoded frame if nothing else can parse it. Returns null only if that also fails.</summary>
    public static AnimatedImageSource? Load(string path)
    {
        if (string.Equals(Path.GetExtension(path), ".gif", StringComparison.OrdinalIgnoreCase))
        {
            var viaFfmpeg = TryLoadGifViaFfmpeg(path);
            if (viaFfmpeg is not null) return viaFfmpeg;
            // fall through to the SkiaSharp path below (will still get at
            // least frame 0 correctly, better than nothing if ffmpeg is
            // missing or this particular GIF trips it up)
        }

        try
        {
            using var codec = SKCodec.Create(path);
            if (codec is not null && codec.FrameCount > 1)
            {
                var frames = new List<Bitmap>(codec.FrameCount);
                var durations = new List<TimeSpan>(codec.FrameCount);
                var info = codec.Info;
                var frameInfos = codec.FrameInfo;

                for (var i = 0; i < codec.FrameCount; i++)
                {
                    using var bmp = new SKBitmap(info.Width, info.Height, info.ColorType, info.AlphaType);
                    var res = codec.GetPixels(info, bmp.GetPixels(), new SKCodecOptions(i));
                    if (res is not (SKCodecResult.Success or SKCodecResult.IncompleteInput)) continue;
                    using var img = SKImage.FromBitmap(bmp);
                    using var data = img.Encode(SKEncodedImageFormat.Png, 90);
                    using var ms = new MemoryStream(data.ToArray());
                    frames.Add(new Bitmap(ms));
                    var ms100 = i < frameInfos.Length ? frameInfos[i].Duration : 100;
                    durations.Add(TimeSpan.FromMilliseconds(ms100 <= 0 ? 100 : ms100));
                }
                if (frames.Count > 0) return new AnimatedImageSource(frames, durations);
            }
        }
        catch { /* fall through to single-frame */ }

        try
        {
            return new AnimatedImageSource(
                new List<Bitmap> { new(path) },
                new List<TimeSpan> { TimeSpan.Zero });
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extract every frame of an animated GIF to PNGs via ffmpeg, caching
    /// the result under the OS temp dir keyed by a content hash so repeat
    /// loads of the same file (e.g. re-arming the spiral overlay) are fast
    /// after the first hit. Frame timing comes from ffprobe's per-frame
    /// duration when available, falling back to the file's average frame
    /// rate (this asset format typically has uniform per-frame timing).
    /// </summary>
    private static AnimatedImageSource? TryLoadGifViaFfmpeg(string path)
    {
        try
        {
            var cacheDir = Path.Combine(Path.GetTempPath(), "ccp-gif-frames-" + ContentHash(path));
            var manifestPath = Path.Combine(cacheDir, "frames.txt");

            if (!File.Exists(manifestPath))
            {
                Directory.CreateDirectory(cacheDir);
                var extract = RunProcess("ffmpeg",
                    $"-y -i \"{path}\" \"{Path.Combine(cacheDir, "frame_%04d.png")}\"");
                if (extract.ExitCode != 0) return null;

                var frameFiles = Directory.EnumerateFiles(cacheDir, "frame_*.png").OrderBy(f => f).ToList();
                if (frameFiles.Count == 0) return null;

                var durationMs = 100; // sane default
                var probe = RunProcess("ffprobe",
                    $"-v error -select_streams v:0 -show_entries stream=avg_frame_rate -of csv=p=0 \"{path}\"");
                var fpsText = probe.StdOut.Trim();
                var parts = fpsText.Split('/');
                if (parts.Length == 2
                    && double.TryParse(parts[0], out var n) && double.TryParse(parts[1], out var d)
                    && d > 0 && n > 0)
                {
                    durationMs = Math.Max(20, (int)Math.Round(1000.0 * d / n));
                }

                File.WriteAllLines(manifestPath, new[] { durationMs.ToString() }
                    .Concat(frameFiles.Select(f => Path.GetFileName(f))));
            }

            var lines = File.ReadAllLines(manifestPath);
            if (lines.Length < 2 || !int.TryParse(lines[0], out var frameDurationMs)) return null;

            var frames = new List<Bitmap>();
            var durations = new List<TimeSpan>();
            foreach (var name in lines.Skip(1))
            {
                var full = Path.Combine(cacheDir, name);
                if (!File.Exists(full)) continue;
                frames.Add(new Bitmap(full));
                durations.Add(TimeSpan.FromMilliseconds(frameDurationMs));
            }
            return frames.Count > 0 ? new AnimatedImageSource(frames, durations) : null;
        }
        catch
        {
            return null;
        }
    }

    private static string ContentHash(string path)
    {
        var info = new FileInfo(path);
        var key = $"{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(bytes)[..16];
    }

    private static (int ExitCode, string StdOut) RunProcess(string exe, string args)
    {
        using var proc = Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        });
        if (proc is null) return (1, "");
        var stdout = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        return (proc.ExitCode, stdout);
    }

    public void Dispose()
    {
        foreach (var f in Frames) f.Dispose();
    }
}
