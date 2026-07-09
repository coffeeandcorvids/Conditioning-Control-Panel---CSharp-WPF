using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace ConditioningControlPanel.Shell.Effects;

/// <summary>
/// Frames + per-frame durations for any image SkiaSharp can decode —
/// animated GIF and animated WebP included (v6.2.x parity: upstream added
/// animated .webp across flash/wash/cascade/tease). Single-frame images
/// load as one frame with no duration, so callers can treat everything
/// uniformly. Avalonia's own Bitmap only ever shows frame 0.
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

    /// <summary>Load a file; falls back to a single Avalonia-decoded frame if Skia can't parse it. Returns null only if nothing can decode it.</summary>
    public static AnimatedImageSource? Load(string path)
    {
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

    public void Dispose()
    {
        foreach (var f in Frames) f.Dispose();
    }
}
