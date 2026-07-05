using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace ConditioningControlPanel.Shell.Effects;

internal sealed class LockCardWindow : Window
{
    private readonly TextBlock _phrase = new();
    private readonly DispatcherTimer _timer = new();

    public LockCardWindow()
    {
        WindowDecorations = global::Avalonia.Controls.WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        Background = new SolidColorBrush(Color.Parse("#080810"));
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        PointerPressed += (_, _) => Close();
        _phrase.Foreground = new SolidColorBrush(Color.Parse("#FF69B4"));
        _phrase.FontSize = 68;
        _phrase.FontWeight = FontWeight.Bold;
        _phrase.TextAlignment = TextAlignment.Center;
        _phrase.TextWrapping = TextWrapping.Wrap;
        _phrase.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        _phrase.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        _phrase.Margin = new Thickness(80);
        _phrase.Effect = new DropShadowEffect { Color = Color.Parse("#FF1493"), BlurRadius = 22, Opacity = .9 };
        Content = _phrase;
        _timer.Tick += (_, _) => Close();
    }

    public void ShowCard(PixelRect bounds, string text, TimeSpan duration)
    {
        Position = new PixelPoint(bounds.X, bounds.Y);
        Width = bounds.Width;
        Height = bounds.Height;
        _phrase.Text = text;
        _timer.Stop();
        _timer.Interval = duration;
        Show();
        Activate();
        _timer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }
}
