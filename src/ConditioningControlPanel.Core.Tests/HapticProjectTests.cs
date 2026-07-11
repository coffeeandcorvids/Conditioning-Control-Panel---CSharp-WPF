using System.Linq;
using ConditioningControlPanel.Core.Models.Authoring;
using ConditioningControlPanel.Core.Services.Haptics;
using Xunit;

/// <summary>
/// The Haptics DAW authoring model (task #18): editing ops, envelope/escalation
/// evaluation, validation, and — the load-bearing one — that a project exports
/// to cue-map JSON the shipped runtime (HapticCueTrack) actually plays.
/// </summary>
public class HapticProjectTests
{
    static HapticProject Sample()
    {
        var p = new HapticProject { AudioRef = "clip.mp3", DurationMs = 10_000 };
        p.AddCue(500, 800, "good girl", snap: true, personality: "");
        p.AddCue(200, 400, "snap");
        p.AddCue(3000, 3100, "good girl");
        return p;
    }

    [Fact]
    public void AddCue_clamps_to_audio_window_and_keeps_sorted()
    {
        var p = new HapticProject { DurationMs = 1000 };
        p.AddCue(-50, 200, "a");          // start clamps to 0
        p.AddCue(900, 5000, "b");         // stop clamps to duration
        Assert.Equal(0, p.Cues[0].StartMs);
        Assert.Equal(1000, p.Cues[1].StopMs);
        Assert.True(p.Cues[0].StartMs <= p.Cues[1].StartMs); // sorted
    }

    [Fact]
    public void AddCue_swaps_reversed_bounds()
    {
        var p = new HapticProject { DurationMs = 10_000 };
        var c = p.AddCue(800, 500, "x");
        Assert.Equal(500, c.StartMs);
        Assert.Equal(800, c.StopMs);
    }

    [Fact]
    public void MoveCue_preserves_duration_and_clamps_to_end()
    {
        var p = new HapticProject { DurationMs = 1000 };
        var c = p.AddCue(100, 300, "a");   // 200ms wide
        p.MoveCue(c, 950);                  // would run past end
        Assert.Equal(200, c.DurationMs);
        Assert.Equal(1000, c.StopMs);
        Assert.Equal(800, c.StartMs);
    }

    [Fact]
    public void MoveCue_clamps_negative_start_to_zero()
    {
        var p = new HapticProject { DurationMs = 1000 };
        var c = p.AddCue(400, 600, "a");
        p.MoveCue(c, -100);
        Assert.Equal(0, c.StartMs);
        Assert.Equal(200, c.StopMs);
    }

    [Fact]
    public void RemoveCue_and_distinct_triggers()
    {
        var p = Sample();
        Assert.Equal(3, p.Cues.Count);
        Assert.Equal(2, p.Triggers.Count);            // "good girl" + "snap"
        Assert.Contains("good girl", p.Triggers);
        var target = p.Cues.First(c => c.Trigger == "snap");
        Assert.True(p.RemoveCue(target));
        Assert.DoesNotContain("snap", p.Triggers);
    }

    [Fact]
    public void EnvelopeAmount_empty_is_flat_one()
    {
        var p = new HapticProject();
        Assert.Equal(1.0, p.EnvelopeAmountAt(1234));
    }

    [Fact]
    public void EnvelopeAmount_interpolates_and_holds_at_ends()
    {
        var p = new HapticProject { BaseAmount = 1.0 };
        p.Envelope.Add(new EnvelopePoint { TimeMs = 1000, Amount = 0.2 });
        p.Envelope.Add(new EnvelopePoint { TimeMs = 3000, Amount = 0.8 });
        Assert.Equal(0.2, p.EnvelopeAmountAt(500));    // before first: hold
        Assert.Equal(0.8, p.EnvelopeAmountAt(9000));   // after last: hold
        Assert.Equal(0.5, p.EnvelopeAmountAt(2000), 3); // midpoint interp
    }

    [Fact]
    public void EffectiveBase_multiplies_baseAmount_by_envelope()
    {
        var p = new HapticProject { BaseAmount = 0.5 };
        p.Envelope.Add(new EnvelopePoint { TimeMs = 0, Amount = 0.4 });
        p.Envelope.Add(new EnvelopePoint { TimeMs = 1000, Amount = 0.4 });
        Assert.Equal(0.2, p.EffectiveBaseAt(500), 6);  // 0.5 * 0.4
    }

    [Fact]
    public void Escalation_absent_is_unity_present_ramps_and_caps()
    {
        var p = new HapticProject();
        Assert.Equal(1.0, p.EscalationFactorAt("good girl", 4)); // no curve
        p.Escalation["good girl"] = new TriggerEscalation { PerRepetition = 0.25, Max = 1.6 };
        Assert.Equal(1.0, p.EscalationFactorAt("good girl", 0)); // first firing = unity
        Assert.Equal(1.5, p.EscalationFactorAt("good girl", 2), 6);
        Assert.Equal(1.6, p.EscalationFactorAt("good girl", 10), 6); // capped
        Assert.Equal(1.0, p.EscalationFactorAt("GOOD GIRL", 0));      // case-insensitive key
    }

    [Fact]
    public void Validate_flags_bad_cues_and_unknown_personality()
    {
        var p = new HapticProject { DurationMs = 1000 };
        p.Cues.Add(new AuthoredCue { StartMs = 100, StopMs = 50, Trigger = "" });        // reversed + no trigger
        p.Cues.Add(new AuthoredCue { StartMs = 200, StopMs = 300, Trigger = "x", Personality = "not-a-real-pattern" });
        var issues = p.Validate();
        Assert.Contains(issues, i => i.Contains("no trigger"));
        Assert.Contains(issues, i => i.Contains("ends before it starts"));
        Assert.Contains(issues, i => i.Contains("unknown personality"));
    }

    [Fact]
    public void Validate_clean_project_has_no_issues()
    {
        var p = Sample();
        Assert.Empty(p.Validate());
    }

    // ── the load-bearing one: DAW output plays in the shipped runtime ──
    [Fact]
    public void Exports_cuemap_that_the_runtime_parses_and_matches()
    {
        var p = Sample();
        var json = p.ToCueMapJson();

        var track = HapticCueTrack.Parse(json);          // the shipped runtime parser
        Assert.Equal(3, track.Cues.Count);
        Assert.Equal(200, track.Cues[0].StartMs);        // start-sorted on export
        Assert.Contains("good girl", track.Triggers);
        Assert.Contains("snap", track.Triggers);
        var gg = track.Cues.First(c => c.StartMs == 500);
        Assert.True(gg.Snap);
    }

    [Fact]
    public void Roundtrips_through_cuemap_import()
    {
        var original = Sample();
        var reloaded = HapticProject.FromCueMap(original.ToCueMapJson(), "clip.mp3", 10_000);
        Assert.Equal(original.Cues.Count, reloaded.Cues.Count);
        Assert.Equal(original.Cues.Select(c => c.StartMs), reloaded.Cues.Select(c => c.StartMs));
        Assert.Equal(original.Cues.Select(c => c.Trigger), reloaded.Cues.Select(c => c.Trigger));
    }

    [Fact]
    public void Roundtrips_full_project_through_json()
    {
        var p = Sample();
        p.BaseAmount = 0.7;
        p.Envelope.Add(new EnvelopePoint { TimeMs = 500, Amount = 0.3 });
        p.Escalation["snap"] = new TriggerEscalation { PerRepetition = 0.2, Max = 1.8 };

        var back = HapticProject.FromJson(p.ToJson());
        Assert.Equal(p.AudioRef, back.AudioRef);
        Assert.Equal(p.DurationMs, back.DurationMs);
        Assert.Equal(0.7, back.BaseAmount, 6);
        Assert.Equal(p.Cues.Count, back.Cues.Count);
        Assert.Single(back.Envelope);
        Assert.True(back.Escalation.ContainsKey("snap"));
        Assert.Equal(1.8, back.Escalation["snap"].Max, 6);
    }
}
