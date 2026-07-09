using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConditioningControlPanel.Core.Services.Haptics;

/// <summary>One cue: a window (ms into the audio) where a trigger word lands.</summary>
public sealed class HapticCue
{
    [JsonPropertyName("start")]   public long StartMs { get; set; }
    [JsonPropertyName("stop")]    public long StopMs { get; set; }
    [JsonPropertyName("trigger")] public string Trigger { get; set; } = "";
    [JsonPropertyName("snap")]    public bool Snap { get; set; }

    [JsonIgnore] public int DurationMs => (int)Math.Max(50, StopMs - StartMs);
}

/// <summary>
/// A BambiCloud haptics track: an ordered list of trigger-word cue windows
/// synced to an audio file (the `hapticsURL` JSON that rides along with every
/// playlist track). Pure parse + lookup — the host owns time and the toy.
/// </summary>
public sealed class HapticCueTrack
{
    public IReadOnlyList<HapticCue> Cues { get; }

    public HapticCueTrack(IEnumerable<HapticCue> cues) =>
        Cues = cues.OrderBy(c => c.StartMs).ToList();

    public static HapticCueTrack Parse(string json) =>
        new(JsonSerializer.Deserialize<List<HapticCue>>(json) ?? new());

    /// <summary>
    /// Cues whose start lies in (<paramref name="fromMs"/>, <paramref name="toMs"/>] —
    /// call each poll tick with the previous and current playback position and fire
    /// whatever comes back. Robust to ticks longer than a cue.
    /// </summary>
    public IEnumerable<HapticCue> CuesBetween(long fromMs, long toMs) =>
        Cues.Where(c => c.StartMs > fromMs && c.StartMs <= toMs);

    /// <summary>Distinct trigger words on this track, e.g. for a pre-scene readout.</summary>
    public IReadOnlyList<string> Triggers =>
        Cues.Select(c => c.Trigger).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
