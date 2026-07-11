using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Core.Models;
using ConditioningControlPanel.Core.Models.Authoring;

namespace ConditioningControlPanel.Core.Services.Audio;

/// <summary>
/// Bridges an authored <see cref="HapticProject"/> (the DAW's composition) onto
/// a live <see cref="HapticMixer"/>: each trigger's authored personality becomes
/// an accent profile that renders that stock pattern's actual curve, and the
/// project's per-trigger escalation is translated into the mixer's additive
/// step/cap. Triggers the project doesn't touch keep the mixer's defaults.
///
/// This is the connective tissue between "author a project" and "the toy plays
/// it" — the cue-map export (HapticProject.ToCueMapJson) provides the timing;
/// this provides the personalities + escalation that shape each firing.
/// </summary>
public static class HapticProjectBinding
{
    /// <summary>Starting ceiling for an authored personality — below 1.0 so escalation has headroom.</summary>
    public const double PersonalityPeak = 0.7;

    /// <summary>An accent profile that renders the given stock pattern's curve.</summary>
    public static AccentProfile PersonalityToProfile(string stockName) =>
        new(Peak: PersonalityPeak, Spike: false, LingerFraction: 0.2, PatternName: stockName);

    /// <summary>Apply a project's per-trigger personalities + escalation to a mixer.</summary>
    public static void Configure(HapticProject project, HapticMixer mixer)
    {
        // One personality per trigger: the first cue (in time) that names a known pattern.
        var personalityByTrigger = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cue in project.Cues.OrderBy(c => c.StartMs))
        {
            if (string.IsNullOrEmpty(cue.Trigger) || personalityByTrigger.ContainsKey(cue.Trigger))
                continue;
            if (!string.IsNullOrEmpty(cue.Personality) &&
                StockHapticPatterns.Names.Contains(cue.Personality))
                personalityByTrigger[cue.Trigger] = cue.Personality;
        }

        foreach (var (trigger, personality) in personalityByTrigger)
            mixer.SetProfile(trigger, PersonalityToProfile(personality));

        // Convert factor escalation → the mixer's additive step/cap, referenced
        // to the trigger's peak: accent = Peak × (1 + PerRep·r), capped at Peak × Max
        //   ⇒ added = Peak × PerRep · r,  cap = Peak × (Max − 1).
        foreach (var (trigger, esc) in project.Escalation)
        {
            var peak = personalityByTrigger.ContainsKey(trigger) ? PersonalityPeak : AccentProfile.Swell.Peak;
            var step = peak * esc.PerRepetition;
            var cap = peak * Math.Max(0.0, esc.Max - 1.0);
            mixer.SetEscalation(trigger, step, cap);
        }
    }
}
