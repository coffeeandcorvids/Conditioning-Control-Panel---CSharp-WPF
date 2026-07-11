using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ConditioningControlPanel.Core.Services.Haptics;

namespace ConditioningControlPanel.Core.Models.Authoring;

/// <summary>
/// The authoring document behind the Haptics DAW tab (task #18) — the
/// composition surface for the mixer equation from VOICE-HAPTICS-DESIGN.md:
///
///     toy(t) = envelope(t) × baseAmount × scene_ramp(t) + accent(trigger, repetition)
///
/// This type owns ONLY the authored composition (pure data + editing/eval
/// logic — no UI, no toy, no audio decode). It exports to the runtime cue-map
/// JSON that the shipped pipeline already plays (HapticCueTrack.Parse), so a
/// project authored here is triggerable by LV mid-scene by name with zero new
/// runtime code. The visual timeline (waveform/lanes/scrub) sits on top of
/// this and is the only part that needs eyes on a screen.
///
/// Everything here is deterministic and unit-tested (HapticProjectTests).
/// </summary>
public sealed class HapticProject
{
    /// <summary>Audio this composition is timed against (path or playlist name).</summary>
    public string AudioRef { get; set; } = "";

    /// <summary>Length of the audio in ms (0 = unknown; disables end-clamping).</summary>
    public long DurationMs { get; set; }

    /// <summary>Global base-layer amount (the voice envelope's overall weight, 0..1+).</summary>
    public double BaseAmount { get; set; } = 1.0;

    /// <summary>Authored trigger-word cue windows.</summary>
    public List<AuthoredCue> Cues { get; set; } = new();

    /// <summary>Base-layer automation lane: (timeMs → multiplier) points, linearly interpolated.</summary>
    public List<EnvelopePoint> Envelope { get; set; } = new();

    /// <summary>Per-trigger repetition escalation (the fifth "good girl" hits harder).</summary>
    public Dictionary<string, TriggerEscalation> Escalation { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    // ── editing operations ────────────────────────────────────────────────

    /// <summary>Add a cue, clamped into the audio window, keeping cues start-sorted.</summary>
    public AuthoredCue AddCue(long startMs, long stopMs, string trigger,
                              bool snap = false, string personality = "")
    {
        if (stopMs < startMs) (startMs, stopMs) = (stopMs, startMs);
        var cue = new AuthoredCue
        {
            StartMs = ClampTime(startMs),
            StopMs = ClampTime(stopMs),
            Trigger = trigger ?? "",
            Snap = snap,
            Personality = personality ?? "",
        };
        Cues.Add(cue);
        SortCues();
        return cue;
    }

    public bool RemoveCue(AuthoredCue cue) => Cues.Remove(cue);

    /// <summary>Slide a cue to a new start, preserving its duration and staying in-bounds.</summary>
    public void MoveCue(AuthoredCue cue, long newStartMs)
    {
        var dur = cue.StopMs - cue.StartMs;
        var start = newStartMs;
        if (DurationMs > 0) start = Math.Min(start, DurationMs - dur);
        start = Math.Max(0, start);
        cue.StartMs = start;
        cue.StopMs = start + dur;
        SortCues();
    }

    public void SortCues() => Cues.Sort((a, b) => a.StartMs.CompareTo(b.StartMs));

    /// <summary>Distinct trigger words in the project (case-insensitive).</summary>
    [JsonIgnore]
    public IReadOnlyList<string> Triggers =>
        Cues.Select(c => c.Trigger).Where(t => !string.IsNullOrEmpty(t))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    // ── evaluation (what the mixer/preview reads) ─────────────────────────

    /// <summary>
    /// Base-layer automation multiplier at a time, linearly interpolated between
    /// points. Empty lane = flat 1.0. Before the first / after the last point,
    /// holds that point's value (no extrapolation).
    /// </summary>
    public double EnvelopeAmountAt(long ms)
    {
        if (Envelope.Count == 0) return 1.0;
        var pts = Envelope.OrderBy(p => p.TimeMs).ToList();
        if (ms <= pts[0].TimeMs) return pts[0].Amount;
        if (ms >= pts[^1].TimeMs) return pts[^1].Amount;
        for (var i = 0; i < pts.Count - 1; i++)
        {
            var a = pts[i]; var b = pts[i + 1];
            if (ms >= a.TimeMs && ms <= b.TimeMs)
            {
                if (b.TimeMs == a.TimeMs) return b.Amount;
                var f = (double)(ms - a.TimeMs) / (b.TimeMs - a.TimeMs);
                return a.Amount + f * (b.Amount - a.Amount);
            }
        }
        return pts[^1].Amount;
    }

    /// <summary>Effective base-layer weight at a time: BaseAmount × envelope automation.</summary>
    public double EffectiveBaseAt(long ms) => BaseAmount * EnvelopeAmountAt(ms);

    /// <summary>
    /// Accent intensity multiplier for the N-th firing of a trigger (0-based).
    /// 1.0 when no escalation is configured; otherwise ramps by the curve, capped.
    /// </summary>
    public double EscalationFactorAt(string trigger, int repetitionIndex)
    {
        if (trigger is null || !Escalation.TryGetValue(trigger, out var curve)) return 1.0;
        return curve.FactorAt(repetitionIndex);
    }

    // ── validation ────────────────────────────────────────────────────────

    /// <summary>Human-readable problems (empty = clean). Used by the editor before export.</summary>
    public IReadOnlyList<string> Validate()
    {
        var issues = new List<string>();
        for (var i = 0; i < Cues.Count; i++)
        {
            var c = Cues[i];
            if (string.IsNullOrWhiteSpace(c.Trigger))
                issues.Add($"Cue #{i} has no trigger word.");
            if (c.StopMs < c.StartMs)
                issues.Add($"Cue #{i} ('{c.Trigger}') ends before it starts.");
            if (c.StartMs < 0)
                issues.Add($"Cue #{i} ('{c.Trigger}') starts before 0.");
            if (DurationMs > 0 && c.StartMs > DurationMs)
                issues.Add($"Cue #{i} ('{c.Trigger}') starts past the end of the audio.");
            if (!string.IsNullOrEmpty(c.Personality) &&
                !StockHapticPatterns.Names.Contains(c.Personality))
                issues.Add($"Cue #{i} ('{c.Trigger}') uses unknown personality '{c.Personality}'.");
        }
        return issues;
    }

    // ── export / import (runtime-compatible) ──────────────────────────────

    private static readonly JsonSerializerOptions CueOpts = new() { WriteIndented = false };
    private static readonly JsonSerializerOptions ProjOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    /// <summary>
    /// Export to the cue-map JSON the shipped pipeline plays (an array of
    /// {start,stop,trigger,snap}). Round-trips through HapticCueTrack.Parse.
    /// </summary>
    public string ToCueMapJson()
    {
        var runtime = Cues.OrderBy(c => c.StartMs).Select(c => c.ToRuntimeCue()).ToList();
        return JsonSerializer.Serialize(runtime, CueOpts);
    }

    /// <summary>Build an editable project from an existing runtime cue map (for re-editing).</summary>
    public static HapticProject FromCueMap(string cueMapJson, string audioRef = "", long durationMs = 0)
    {
        var track = HapticCueTrack.Parse(cueMapJson);
        var proj = new HapticProject { AudioRef = audioRef, DurationMs = durationMs };
        foreach (var c in track.Cues)
            proj.Cues.Add(new AuthoredCue
            {
                StartMs = c.StartMs,
                StopMs = c.StopMs,
                Trigger = c.Trigger,
                Snap = c.Snap,
            });
        proj.SortCues();
        return proj;
    }

    // ── project save / load ───────────────────────────────────────────────

    public string ToJson() => JsonSerializer.Serialize(this, ProjOpts);

    public static HapticProject FromJson(string json) =>
        JsonSerializer.Deserialize<HapticProject>(json) ?? new HapticProject();

    private long ClampTime(long ms)
    {
        if (ms < 0) return 0;
        if (DurationMs > 0 && ms > DurationMs) return DurationMs;
        return ms;
    }
}

/// <summary>An authored cue: a runtime cue window plus its accent personality.</summary>
public sealed class AuthoredCue
{
    public long StartMs { get; set; }
    public long StopMs { get; set; }
    public string Trigger { get; set; } = "";
    public bool Snap { get; set; }

    /// <summary>Stock accent pattern name (StockHapticPatterns.Names); "" = mixer default.</summary>
    public string Personality { get; set; } = "";

    [JsonIgnore] public long DurationMs => Math.Max(0, StopMs - StartMs);

    /// <summary>Strip authoring metadata down to the runtime cue the pipeline consumes.</summary>
    public HapticCue ToRuntimeCue() => new()
    {
        StartMs = StartMs,
        StopMs = StopMs,
        Trigger = Trigger,
        Snap = Snap,
    };
}

/// <summary>A base-layer automation point: multiplier (0..1) at a time.</summary>
public sealed class EnvelopePoint
{
    public long TimeMs { get; set; }
    public double Amount { get; set; } = 1.0;
}

/// <summary>Per-trigger repetition escalation curve (linear ramp, capped).</summary>
public sealed class TriggerEscalation
{
    /// <summary>Added to the factor per repetition (0 = flat).</summary>
    public double PerRepetition { get; set; } = 0.1;

    /// <summary>Ceiling on the factor (never escalates past this).</summary>
    public double Max { get; set; } = 2.0;

    /// <summary>Intensity factor for the N-th firing (0-based). Floors at 1.0.</summary>
    public double FactorAt(int repetitionIndex)
    {
        var rep = Math.Max(0, repetitionIndex);
        var factor = 1.0 + PerRepetition * rep;
        var cap = Max <= 0 ? double.MaxValue : Max;
        return Math.Clamp(factor, 1.0, cap);
    }
}
