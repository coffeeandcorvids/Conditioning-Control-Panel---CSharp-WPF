using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace ConditioningControlPanel.Shell.Effects;

internal sealed class BouncingTextWindow : Window
{
    private readonly Canvas _canvas = new();
    private readonly TextBlock _text = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Random _rng = new();
    private IReadOnlyList<string> _phrases = Array.Empty<string>();
    private double _x, _y, _dx = 3.5, _dy = 2.8;
    private int _ticks;
    private bool _clickThroughApplied;

    public BouncingTextWindow()
    {
        WindowDecorations = global::Avalonia.Controls.WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        // Visual-only overlay: never take focus or hit-tests, so it can't steal
        // clicks. Matches OverlayEffectWindow; the OS-level click-through is
        // applied after Show() (native handle must exist first).
        Focusable = false;
        IsHitTestVisible = false;
        Content = _canvas;
        _text.Foreground = new SolidColorBrush(Color.Parse("#FF69B4"));
        _text.FontSize = 56;
        _text.FontWeight = FontWeight.Bold;
        _text.Text = "OBEY";
        _text.Effect = new DropShadowEffect { Color = Color.Parse("#FF1493"), BlurRadius = 18, Opacity = .95 };
        _canvas.Children.Add(_text);
        _timer.Tick += (_, _) => Tick();
    }

    public void Start(PixelRect bounds, IReadOnlyList<string> phrases)
    {
        Position = new PixelPoint(bounds.X, bounds.Y);
        Width = bounds.Width;
        Height = bounds.Height;
        _phrases = phrases.Count > 0 ? phrases : new[] { "OBEY", "DROP", "GOOD GIRL", "DON'T THINK" };
        _text.Text = _phrases[_rng.Next(_phrases.Count)].ToUpperInvariant();
        _x = bounds.Width * .25;
        _y = bounds.Height * .25;
        Show();
        // Real OS-level click-through, once the native handle exists (best-effort;
        // no-op on backends without it — the overlay still shows either way).
        if (!_clickThroughApplied)
            _clickThroughApplied = X11InputTransparency.TryMakeClickThrough(this);
        _timer.Start();
    }

    public void Stop() => Hide();

    private void Tick()
    {
        _ticks++;
        if (_ticks % 180 == 0 && _phrases.Count > 0) _text.Text = _phrases[_rng.Next(_phrases.Count)].ToUpperInvariant();
        _x += _dx; _y += _dy;
        var maxX = Math.Max(10, Bounds.Width - 360);
        var maxY = Math.Max(10, Bounds.Height - 90);
        if (_x < 0 || _x > maxX) _dx *= -1;
        if (_y < 0 || _y > maxY) _dy *= -1;
        Canvas.SetLeft(_text, Math.Clamp(_x, 0, maxX));
        Canvas.SetTop(_text, Math.Clamp(_y, 0, maxY));
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }
}
