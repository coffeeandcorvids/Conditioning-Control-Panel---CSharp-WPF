using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Services.Flash;

namespace ConditioningControlPanel.Shell.Overlays;

/// <summary>
/// Topmost transparent window that shows a flash image for a fixed duration then hides itself.
/// One instance is created per concurrent flash slot (pool size: 6). Reused via Show/Hide
/// to avoid the overhead of creating/destroying native windows mid-session.
/// </summary>
public partial class FlashOverlayWindow : Window
{
    private DispatcherTimer? _hideTimer;

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

        try
        {
            Img.Source = new Bitmap(imagePath);
        }
        catch
        {
            // image load failed — show anyway as a pink square (conditioning still fires)
            Img.Source = null;
        }

        Show();

        _hideTimer?.Stop();
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(args.DurationMs) };
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); Hide(); };
        _hideTimer.Start();
    }
}
