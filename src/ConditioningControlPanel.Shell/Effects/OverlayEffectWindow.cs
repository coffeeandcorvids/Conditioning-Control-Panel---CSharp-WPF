using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

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
        // Passive filter overlay: never wants keyboard focus (it has no ESC
        // handler that needs it either -- panic/dismiss is driven from the
        // main window, not this one) and Avalonia itself shouldn't route
        // pointer events into it even before the OS-level click-through
        // (X11InputTransparency) engages.
        Focusable = false;
        IsHitTestVisible = false;

        _pink.Background = new SolidColorBrush(Color.FromArgb(95, 255, 105, 180));
        _pink.IsVisible = false;
        _root.Children.Add(_pink);

        _spiralImage.IsVisible = false;
        _spiralImage.Stretch = Stretch.UniformToFill;
        _spiralImage.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        _spiralImage.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        _spiralImage.RenderTransformOrigin = RelativePoint.Center;
        _root.Children.Add(_spiralImage);

        // animated files (nested-spiral GIFs etc.) carry their own motion — play
        // their frames untouched. Only static single-frame images get the rotation
        // transform, overscaled √2 so the square's corners never sweep into view.
        _gifTimer.Tick += (_, _) =>
        {
            if (_spiralFrames.Count > 1)
            {
                _frameIndex = (_frameIndex + 1) % _spiralFrames.Count;
                _spiralImage.Source = _spiralFrames[_frameIndex];
            }
            else
            {
                _angle = (_angle + 1.25) % 360;
                _spiralImage.RenderTransform = new TransformGroup
                {
                    Children = { new ScaleTransform(1.45, 1.45), new RotateTransform(_angle) }
                };
            }
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

        var anim = AnimatedImageSource.Load(resolved);
        if (anim is null) return;

        foreach (var f in _spiralFrames) f.Dispose();
        _spiralFrames.Clear();
        _frameIndex = 0;
        _spiralFrames.AddRange(anim.Frames);

        _spiralImage.Source = _spiralFrames[0];
        _spiralImage.RenderTransform = null;   // clear stale rotation when swapping static → animated
        _angle = 0;
        _loadedPath = resolved;
    }

    private bool _clickThroughApplied;

    private void EnsureVisibility()
    {
        if (HasAny)
        {
            if (!IsVisible)
            {
                Show();
                // Never steal focus/keyboard input from whatever the user is
                // doing on the desktop -- this window is a passive filter
                // (pink tint / spiral), not something to interact with.
                // The old Activate() call here was almost certainly the
                // dominant cause of "the overlay blocks me from interacting
                // with everything": raising+focusing a fullscreen topmost
                // window steals subsequent clicks/keys until something else
                // reclaims focus.
            }
            Topmost = true;

            // Real OS-level click-through (X11 input-shape), applied once
            // the native window handle exists. Best-effort: if it fails
            // (non-X11 backend, missing SHAPE extension), the overlay still
            // works, it just won't be click-through -- never blocks showing
            // the effect over a failed P/Invoke.
            if (!_clickThroughApplied)
                _clickThroughApplied = X11InputTransparency.TryMakeClickThrough(this);
        }
        else if (IsVisible) Hide();
    }
}
