using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;

namespace ConditioningControlPanel.Shell.Effects;

internal sealed class OverlayEffectWindow : Window
{
    private readonly Grid _root = new();
    private readonly Image _spiralImage = new();
    private readonly Border _pink = new();
    private readonly DispatcherTimer _gifTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly List<Bitmap> _spiralFrames = new();
    private int _frameIndex;
    private double _angle;
    private string? _loadedPath;

    public event EventHandler? EscapePressed;

    public OverlayEffectWindow()
    {
        WindowDecorations = global::Avalonia.Controls.WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Content = _root;
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            EscapePressed?.Invoke(this, EventArgs.Empty);
        };

        _pink.Background = new SolidColorBrush(Color.FromArgb(95, 255, 105, 180));
        _pink.IsVisible = false;
        _root.Children.Add(_pink);

        _spiralImage.IsVisible = false;
        _spiralImage.Stretch = Stretch.UniformToFill;
        _spiralImage.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        _spiralImage.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        _spiralImage.RenderTransformOrigin = RelativePoint.Center;
        _root.Children.Add(_spiralImage);

        // rotation is independent of frame animation: static (single-frame) spirals
        // still spin, multi-frame GIFs additionally advance frames
        _gifTimer.Tick += (_, _) =>
        {
            if (_spiralFrames.Count > 1)
            {
                _frameIndex = (_frameIndex + 1) % _spiralFrames.Count;
                _spiralImage.Source = _spiralFrames[_frameIndex];
            }
            _angle = (_angle + 1.25) % 360;
            _spiralImage.RenderTransform = new RotateTransform(_angle);
        };
    }

    public void SetBounds(PixelRect bounds)
    {
        Position = new PixelPoint(bounds.X, bounds.Y);
        Width = bounds.Width;
        Height = bounds.Height;
    }

    public void SetPink(bool on, byte alpha = 95)
    {
        _pink.Background = new SolidColorBrush(Color.FromArgb(alpha, 255, 105, 180));
        _pink.IsVisible = on;
        EnsureVisibility();
    }

    public void SetSpiral(bool on, string? path = null, int opacityPercent = 35)
    {
        if (on) EnsureSpiralLoaded(path);
        _spiralImage.Opacity = Math.Clamp(opacityPercent, 5, 100) / 100.0;
        _spiralImage.IsVisible = on;
        if (on && _spiralFrames.Count > 0) _gifTimer.Start();
        else _gifTimer.Stop();
        EnsureVisibility();
    }

    public bool HasAny => _pink.IsVisible || _spiralImage.IsVisible;

    private void EnsureSpiralLoaded(string? path)
    {
        var resolved = !string.IsNullOrWhiteSpace(path) && File.Exists(path)
            ? path
            : EffectAssetPaths.FindRepoAsset("ConditioningControlPanel/Resources/spirals/spiral.gif")
              ?? EffectAssetPaths.FindRepoAsset("ConditioningControlPanel/Resources/spiral.gif");
        if (resolved is null || resolved == _loadedPath) return;

        foreach (var f in _spiralFrames) f.Dispose();
        _spiralFrames.Clear();
        _frameIndex = 0;

        try
        {
            using var codec = SKCodec.Create(resolved);
            if (codec is null) throw new InvalidOperationException("SKCodec could not load spiral GIF");
            var info = codec.Info;
            var frameCount = Math.Max(1, codec.FrameCount);
            for (var i = 0; i < frameCount; i++)
            {
                using var bitmap = new SKBitmap(info.Width, info.Height, info.ColorType, info.AlphaType);
                var result = codec.GetPixels(info, bitmap.GetPixels(), new SKCodecOptions(i));
                if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput)) continue;
                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 90);
                using var ms = new MemoryStream(data.ToArray());
                _spiralFrames.Add(new Bitmap(ms));
            }
        }
        catch
        {
            _spiralFrames.Clear();
            _spiralFrames.Add(new Bitmap(resolved));
        }

        if (_spiralFrames.Count > 0)
        {
            _spiralImage.Source = _spiralFrames[0];
            _loadedPath = resolved;
        }
    }

    private void EnsureVisibility()
    {
        if (HasAny)
        {
            if (!IsVisible) Show();
            Activate();
            Topmost = true;
        }
        else if (IsVisible) Hide();
    }
}
