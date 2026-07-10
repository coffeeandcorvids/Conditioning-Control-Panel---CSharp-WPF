using System;
using System.Collections.Generic;
using ConditioningControlPanel.Core.Services.Haptics;

namespace ConditioningControlPanel.Core.Services.Audio;

/// <summary>How a trigger word lands on the body — its accent personality.</summary>
public sealed record AccentProfile(
    double Peak,          // 0..1 intensity ceiling for this trigger
    bool Spike,           // true = sharp attack/release (snap), false = swell + linger
    double LingerFraction // extra duration after the word, as a fraction of the cue window
)
{
    public static readonly AccentProfile Snap  = new(0.95, Spike: true,  LingerFraction: 0.0);
    public static readonly AccentProfile Swell = new(0.75, Spike: false, LingerFraction: 0.5);
}

/// <summary>
/// The voice-haptics mixer (docs/VOICE-HAPTICS-DESIGN.md):
///
///     toy = envelope × scene_ramp + accent(trigger, repetition, phase)
///
/// Base layer: the voice's own envelope, scaled by the live scene ramp.
/// Accent layer: trigger cues duck the base and take the toy (sidechain-for-
/// touch), escalating with repetition — the fifth "good girl" lands harder
/// than the first. Pure math; the host feeds it time and plays its output.
/// </summary>
public sealed class HapticMixer
{
    private readonly Dictionary<string, AccentProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _fireCounts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Live scene intensity multiplier, 0..1 (from SessionEngine ramps; 1.0 = no session).</summary>
    public double SceneRamp { get; set; } = 1.0;

    /// <summary>How much each repetition of a trigger raises its accent, up to the cap.</summary>
    public double RepetitionStep { get; set; } = 0.05;
    public double RepetitionCap { get; set; } = 0.25;

    /// <summary>Base-layer share of the toy's range — accents own the headroom above it.</summary>
    public double BaseLevel { get; set; } = 0.45;

    public void SetProfile(string trigger, AccentProfile profile) => _profiles[trigger] = profile;

    /// <summary>Reset repetition memory (new scene / new piece).</summary>
    public void ResetCounts() => _fireCounts.Clear();

    /// <summary>Continuous base layer: envelope value → toy intensity at this moment.</summary>
    public double Base(float envelopeValue) =>
        Math.Clamp(envelopeValue * BaseLevel * SceneRamp, 0.0, 1.0);

    /// <summary>
    /// A cue fired: produce the accent to play over the base. Repetition of the
    /// same trigger escalates the peak; the scene ramp scales the whole accent.
    /// </summary>
    public AccentPlan Accent(HapticCue cue)
    {
        var profile = _profiles.TryGetValue(cue.Trigger, out var p)
            ? p
            : cue.Snap ? AccentProfile.Snap : AccentProfile.Swell;

        var count = _fireCounts.TryGetValue(cue.Trigger, out var c) ? c + 1 : 1;
        _fireCounts[cue.Trigger] = count;

        var escalation = Math.Min((count - 1) * RepetitionStep, RepetitionCap);
        var peak = Math.Clamp((profile.Peak + escalation) * SceneRamp, 0.0, 1.0);
        var durationMs = (int)(cue.DurationMs * (1.0 + profile.LingerFraction));

        return new AccentPlan(peak, durationMs, profile.Spike, count);
    }

    /// <summary>
    /// Shape an accent's intensity curve over its duration, optionally following
    /// the word's own envelope slice (empty slice → synthetic shape).
    /// Spike: instant attack, fast decay. Swell: rise past the voice, linger, release.
    /// </summary>
    public static float[] ShapeAccent(AccentPlan plan, float[] wordEnvelope, int hopMs = 50)
    {
        var hops = Math.Max(2, plan.DurationMs / hopMs);
        var curve = new float[hops];

        for (var i = 0; i < hops; i++)
        {
            var t = i / (double)(hops - 1);
            double shape;
            if (plan.Spike)
            {
                shape = Math.Pow(1.0 - t, 2.2);                       // hard hit, fast falloff
            }
            else
            {
                shape = t < 0.35 ? t / 0.35                            // rise
                      : t < 0.70 ? 1.0                                  // hold
                      : (1.0 - t) / 0.30;                               // linger out
            }

            // let the word's own envelope texture the shape where we have it
            if (wordEnvelope.Length > 0)
            {
                var w = wordEnvelope[Math.Min((int)(t * wordEnvelope.Length), wordEnvelope.Length - 1)];
                shape = 0.6 * shape + 0.4 * w;
            }

            curve[i] = (float)Math.Clamp(shape * plan.Peak, 0.0, 1.0);
        }
        return curve;
    }
}

/// <summary>What the toy should do for one fired accent.</summary>
public readonly record struct AccentPlan(double Peak, int DurationMs, bool Spike, int Repetition);
