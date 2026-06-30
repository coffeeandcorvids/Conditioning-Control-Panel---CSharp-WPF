using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Services.Subliminal;

namespace ConditioningControlPanel.Shell.Overlays;

/// <summary>
/// Full-screen transparent overlay for subliminal text flashes. Click-through (IsHitTestVisible=False).
/// One instance lives for the duration of the session — text is swapped per show, window stays open.
/// </summary>
public partial class SubliminalOverlayWindow : Window
{
    private DispatcherTimer? _hideTimer;

    public SubliminalOverlayWindow()
    {
        InitializeComponent();
    }

    /// Cover a screen and flash the phrase per the event args.
    public void ShowSubliminal(Rect screenBounds, SubliminalEventArgs args)
    {
        Position = new PixelPoint((int)screenBounds.X, (int)screenBounds.Y);
        Width    = screenBounds.Width;
        Height   = screenBounds.Height;

        SubText.Text     = args.Phrase;
        SubText.FontSize = args.FontSize;
        Opacity          = args.Opacity / 100.0;

        Show();

        _hideTimer?.Stop();
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(args.DurationMs) };
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); Hide(); };
        _hideTimer.Start();
    }
}
