namespace ConditioningControlPanel.Core.Services.Subliminal;

/// <summary>Scheduling side of subliminal text flashes — no UI dep.</summary>
public interface ISubliminalService : IDisposable
{
    bool IsRunning { get; }
    event EventHandler<SubliminalEventArgs>? SubliminalReady;

    void Start();
    void Stop();
    void TriggerNow(string? phrase = null);
    void UpdateSettings(SubliminalConfig cfg);
}

public sealed record SubliminalConfig(
    bool          Enabled,
    int           FrequencySeconds,
    int           DurationMs,
    int           Opacity,
    double        FontSize,
    List<string>  Phrases);

public sealed class SubliminalEventArgs : EventArgs
{
    public string Phrase     { get; init; } = "";
    public int    DurationMs { get; init; }
    public int    Opacity    { get; init; }
    public double FontSize   { get; init; }
}
