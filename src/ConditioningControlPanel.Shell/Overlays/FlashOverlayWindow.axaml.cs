using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Services.Flash;
using ConditioningControlPanel.Shell.Effects;

namespace ConditioningControlPanel.Shell.Overlays;

/// <summary>
/// Topmost transparent window that shows a flash image for a fixed duration then hides itself.
/// One instance is created per concurrent flash slot (pool size: 6). Reused via Show/Hide
/// to avoid the overhead of creating/destroying native windows mid-session.
/// Animated GIF/WebP flashes play through their frames while shown.
/// </summary>
public partial class FlashOverlayWindow : Window
{
    private DispatcherTimer? _hideTimer;
    private DispatcherTimer? _frameTimer;
    private AnimatedImageSource? _anim;
    private int _frame;

    public FlashOverlayWindow()
    {
        InitializeComponent();
    }

    /// Position + populate + show for the given flash args.
    public void ShowFlash(string imagePath, Rect screenBounds, FlashEventArgs args)
    {
        // size = fraction of screen width, preserve aspect
        var w = screenBounds.Width * args.SizeFraction;
        Width  = w;
        Height = w;  // square container; image stretches to Uniform so portrait/landscape both work

        // Centre on screen
        Position = new PixelPoint(
            (int)(screenBounds.X + (screenBounds.Width  - Width)  / 2),
            (int)(screenBounds.Y + (screenBounds.Height - Height) / 2));

        Opacity = args.Opacity / 100.0;

        _frameTimer?.Stop();
        _anim?.Dispose();
        _anim = AnimatedImageSource.Load(imagePath);
        _frame = 0;
        // null = load failed — show anyway as a pink square (conditioning still fires)
        Img.Source = _anim?.Frames[0];

        if (_anim is { IsAnimated: true })
        {
            _frameTimer = new DispatcherTimer { Interval = _anim.Durations[0] };
            _frameTimer.Tick += (_, _) =>
            {
                if (_anim is not { IsAnimated: true }) { _frameTimer?.Stop(); return; }
                _frame = (_frame + 1) % _anim.Frames.Count;
                Img.Source = _anim.Frames[_frame];
                _frameTimer!.Interval = _anim.Durations[_frame];
            };
            _frameTimer.Start();
        }

        Show();

        _hideTimer?.Stop();
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(args.DurationMs) };
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); _frameTimer?.Stop(); Hide(); };
        _hideTimer.Start();
    }
}
