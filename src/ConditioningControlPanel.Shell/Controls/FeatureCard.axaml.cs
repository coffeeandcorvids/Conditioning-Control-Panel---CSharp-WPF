using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ConditioningControlPanel.Shell.Controls;

/// <summary>
/// One tile in the 4×4 Velvet Mosaic. Shows feature name, icon, on/off status dot,
/// and a quick toggle button. Matches upstream FeatureCard visual language.
/// </summary>
public partial class FeatureCard : UserControl
{
    // ── Styled properties ─────────────────────────────────────────────────

    public static readonly StyledProperty<string> IconProperty =
        AvaloniaProperty.Register<FeatureCard, string>(nameof(Icon), "✦");
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<FeatureCard, string>(nameof(Label), "Feature");
    public static readonly StyledProperty<bool> IsEnabledFeatureProperty =
        AvaloniaProperty.Register<FeatureCard, bool>(nameof(IsEnabledFeature), false);
    public static readonly StyledProperty<string> ImagePathProperty =
        AvaloniaProperty.Register<FeatureCard, string>(nameof(ImagePath), "");

    public string Icon
    {
        get => GetValue(IconProperty);
        set { SetValue(IconProperty, value); IconText.Text = value; }
    }
    public string Label
    {
        get => GetValue(LabelProperty);
        set { SetValue(LabelProperty, value); LabelText.Text = value; }
    }
    public string ImagePath
    {
        get => GetValue(ImagePathProperty);
        set { SetValue(ImagePathProperty, value); SetImage(value); }
    }
    public bool IsEnabledFeature
    {
        get => GetValue(IsEnabledFeatureProperty);
        set
        {
            SetValue(IsEnabledFeatureProperty, value);
            StatusDot.Fill = new SolidColorBrush(value ? Color.Parse("#3EE87A") : Color.Parse("#2E2E4A"));
            ToggleBtn.Content = value ? "● ON" : "○ OFF";
            ToggleBtn.Background = new SolidColorBrush(value ? Color.Parse("#1FB85A") : Color.Parse("#2E2E4A"));
        }
    }

    /// Raised when the user clicks anywhere on the card to navigate to its detail panel.
    public event EventHandler? CardClicked;
    /// Raised when the toggle button is clicked.
    public event EventHandler<bool>? ToggleChanged;

    public FeatureCard()
    {
        InitializeComponent();
        ToggleBtn.Click += (_, _) =>
        {
            IsEnabledFeature = !IsEnabledFeature;
            ToggleChanged?.Invoke(this, IsEnabledFeature);
        };
        CardBorder.PointerPressed += (_, _) => CardClicked?.Invoke(this, EventArgs.Empty);
    }

    private void SetImage(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var resolved = Resolve(path);
        if (resolved is null) return;
        FeatureImage.Source = new Bitmap(resolved);
    }

    private static string? Resolve(string path)
    {
        if (Path.IsPathRooted(path) && File.Exists(path)) return path;

        foreach (var root in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(root);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, path);
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
        }

        return null;
    }
}
