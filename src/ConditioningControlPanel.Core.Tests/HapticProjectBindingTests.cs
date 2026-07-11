using System.Linq;
using ConditioningControlPanel.Core.Models.Authoring;
using ConditioningControlPanel.Core.Services.Audio;
using ConditioningControlPanel.Core.Services.Haptics;
using Xunit;

/// <summary>
/// The DAW→mixer bridge (task #18): an authored project's per-trigger
/// personalities render as the real stock-pattern curve, and its factor
/// escalation drives the mixer's per-trigger escalation — without disturbing
/// the mixer's global defaults for untouched triggers.
/// </summary>
public class HapticProjectBindingTests
{
    static HapticCue Cue(string trigger, bool snap = false) =>
        new() { StartMs = 0, StopMs = 400, Trigger = trigger, Snap = snap };

    [Fact]
    public void PersonalityToProfile_carries_pattern_name()
    {
        var p = HapticProjectBinding.PersonalityToProfile("Pulse");
        Assert.Equal("Pulse", p.PatternName);
        Assert.Equal(HapticProjectBinding.PersonalityPeak, p.Peak, 6);
    }

    [Fact]
    public void Configure_gives_each_trigger_its_authored_personality()
    {
        var proj = new HapticProject { DurationMs = 5000 };
        proj.AddCue(100, 300, "good girl", personality: "Steady");
        proj.AddCue(1000, 1200, "snap", personality: "Pulse");

        var mixer = new HapticMixer();
        HapticProjectBinding.Configure(proj, mixer);

        Assert.Equal("Steady", mixer.Accent(Cue("good girl")).PatternName);
        Assert.Equal("Pulse", mixer.Accent(Cue("snap")).PatternName);
        // a trigger the project never named keeps the mixer default (no pattern)
        Assert.Null(mixer.Accent(Cue("unmapped")).PatternName);
    }

    [Fact]
    public void ShapeAccent_renders_the_authored_pattern_curve()
    {
        // "Steady" = flat 1.0 → every hop equals peak; "Pulse" = 0→1→0 → mid > ends.
        var steady = HapticMixer.ShapeAccent(
            new AccentPlan(Peak: 0.8, DurationMs: 500, Spike: false, Repetition: 1, PatternName: "Steady"),
            System.Array.Empty<float>());
        Assert.All(steady, v => Assert.Equal(0.8f, v, 2));

        var pulse = HapticMixer.ShapeAccent(
            new AccentPlan(Peak: 1.0, DurationMs: 500, Spike: false, Repetition: 1, PatternName: "Pulse"),
            System.Array.Empty<float>());
        var mid = pulse[pulse.Length / 2];
        Assert.True(mid > pulse[0] && mid > pulse[^1]); // bump in the middle
    }

    [Fact]
    public void ShapeAccent_without_pattern_keeps_synthetic_shape()
    {
        var spike = HapticMixer.ShapeAccent(
            new AccentPlan(0.9, 500, Spike: true, Repetition: 1), System.Array.Empty<float>());
        Assert.True(spike[0] > spike[^1]); // hard hit, fast falloff — unchanged behavior
    }

    [Fact]
    public void Per_trigger_escalation_beats_the_global_rate()
    {
        var proj = new HapticProject { DurationMs = 5000 };
        proj.AddCue(0, 300, "gg", personality: "Steady");
        proj.Escalation["gg"] = new TriggerEscalation { PerRepetition = 0.2, Max = 1.5 };

        var mixer = new HapticMixer();
        HapticProjectBinding.Configure(proj, mixer);

        // configured trigger: two firings, measure the jump
        var gg1 = mixer.Accent(Cue("gg")).Peak;
        var gg2 = mixer.Accent(Cue("gg")).Peak;

        // an untouched trigger falls back to the global step (0.05)
        var other1 = mixer.Accent(Cue("other")).Peak;
        var other2 = mixer.Accent(Cue("other")).Peak;

        Assert.True(gg2 > gg1);                          // it escalates
        Assert.True((gg2 - gg1) > (other2 - other1));    // faster than global default
    }

    [Fact]
    public void Configure_ignores_unknown_personalities()
    {
        var proj = new HapticProject { DurationMs = 2000 };
        proj.AddCue(0, 200, "x", personality: "not-a-pattern");

        var mixer = new HapticMixer();
        HapticProjectBinding.Configure(proj, mixer);
        Assert.Null(mixer.Accent(Cue("x")).PatternName); // stayed on default
    }
}
