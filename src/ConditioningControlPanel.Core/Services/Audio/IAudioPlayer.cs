using System;

namespace ConditioningControlPanel.Core.Services.Audio;

/// <summary>
/// Minimal audio playback surface the Core can drive without knowing the
/// backend (Shell implements it with LibVLC). Positions are milliseconds
/// into the current item.
/// </summary>
public interface IAudioPlayer
{
    /// <summary>Start playing a local path or URL, replacing whatever is playing.</summary>
    void Play(string pathOrUrl);
    void Pause();
    void Resume();
    void Stop();

    /// <summary>0–100.</summary>
    int Volume { get; set; }
    bool IsPlaying { get; }
    long PositionMs { get; }
    long DurationMs { get; }

    /// <summary>Raised when the current item finishes on its own.</summary>
    event EventHandler? Ended;
    /// <summary>Raised when playback fails (bad URL, decode error).</summary>
    event EventHandler<string>? Error;
}
