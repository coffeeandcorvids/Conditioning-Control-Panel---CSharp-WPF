using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace ConditioningControlPanel.Shell.Effects;

internal sealed class BubbleWindow : Window
{
    private readonly DispatcherTimer _move = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer _life = new();
    private double _x;
    private double _y;
    private readonly double _dy;

    public event EventHandler? Popped;

    public BubbleWindow(string? bubbleImage, PixelRect screen, Random rng)
    {
        WindowDecorations = global::Avalonia.Controls.WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };

        var size = rng.Next(58, 112);
        Width = size;
        Height = size;
        _x = rng.Next(screen.X + 20, Math.Max(screen.X + 21, screen.X + screen.Width - size - 20));
        _y = screen.Y + screen.Height + rng.Next(5, 70);
        _dy = rng.NextDouble() * 1.8 + 1.1;
        Position = new PixelPoint((int)_x, (int)_y);

        Control visual;
        if (bubbleImage is not null && System.IO.File.Exists(bubbleImage))
        {
            visual = new Image { Source = new Bitmap(bubbleImage), Stretch = Stretch.UniformToFill, Opacity = 0.88 };
        }
        else
        {
            visual = new Border
            {
                CornerRadius = new CornerRadius(size / 2),
                Background = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Color.Parse("#CCFFFFFF"), 0),
                        new GradientStop(Color.Parse("#66FF69B4"), .55),
                        new GradientStop(Color.Parse("#33B47BFF"), 1),
                    }
                },
                BorderBrush = new SolidColorBrush(Color.Parse("#FF69B4")),
                BorderThickness = new Thickness(2)
            };
        }
        Content = visual;

        PointerPressed += (_, _) => Pop();
        _move.Tick += (_, _) =>
        {
            _y -= _dy;
            Position = new PixelPoint((int)_x, (int)_y);
            if (_y < screen.Y - Height - 20) Close();
        };
        _life.Interval = TimeSpan.FromSeconds(rng.Next(9, 16));
        _life.Tick += (_, _) => Close();
    }

    public void Start()
    {
        Show();
        _move.Start();
        _life.Start();
    }

    private void Pop()
    {
        Popped?.Invoke(this, EventArgs.Empty);
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _move.Stop();
        _life.Stop();
        base.OnClosed(e);
    }
}
