namespace ConditioningControlPanel.Core.Services.Flash;

/// <summary>The scheduling + asset side of flash image display — no UI dep.</summary>
public interface IFlashService : IDisposable
{
    bool IsRunning { get; }
    event EventHandler<FlashEventArgs>? FlashReady;  // raised when the scheduler wants a flash to show

    void Start();
    void Stop();
    /// Fire a one-shot flash right now (e.g. from keyword trigger).
    void TriggerNow(int? amount = null, int? durationMs = null);
    /// Force-flash a specific image path.
    void TriggerWithImage(string imagePath, int durationMs);
    void RefreshAssets();
    void UpdateSettings(FlashConfig cfg);
}

public sealed record FlashConfig(
    bool   Enabled,
    int    FrequencySeconds,
    int    DurationMs,
    int    Amount,
    int    Opacity,          // 0–100
    double SizeFraction,     // fraction of screen width
    string ImagesPath);

public sealed class FlashEventArgs : EventArgs
{
    public IReadOnlyList<string> ImagePaths { get; init; } = [];
    public int  DurationMs  { get; init; }
    public int  Opacity     { get; init; }
    public double SizeFraction { get; init; }
}
