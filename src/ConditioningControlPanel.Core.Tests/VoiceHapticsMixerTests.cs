using System;
using System.Linq;
using ConditioningControlPanel.Core.Services.Audio;
using ConditioningControlPanel.Core.Services.Haptics;
using Xunit;

public class EnvelopeTests
{
    static float[] Sine(int sampleRate, int ms, double amplitude, double hz = 220)
    {
        var n = sampleRate * ms / 1000;
        var s = new float[n];
        for (var i = 0; i < n; i++)
            s[i] = (float)(amplitude * Math.Sin(2 * Math.PI * hz * i / sampleRate));
        return s;
    }

    [Fact]
    public void Loud_and_quiet_sections_come_out_proportional()
    {
        // 200ms loud, 200ms quiet — envelope should track the contour
        var loud = Sine(16000, 200, 1.0);
        var quiet = Sine(16000, 200, 0.25);
        var pcm = loud.Concat(quiet).ToArray();

        var env = Envelope.FromPcm(pcm, 16000, hopMs: 50);

        Assert.Equal(8, env.Intensities.Count);
        Assert.Equal(1.0f, env.Intensities.Take(4).Max(), 2);
        Assert.All(env.Intensities.Skip(4), v => Assert.InRange(v, 0.15f, 0.35f));
    }

    [Fact]
    public void Silence_yields_zeros_not_NaN()
    {
        var env = Envelope.FromPcm(new float[16000], 16000);
        Assert.All(env.Intensities, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void At_reads_by_time_and_zero_beyond_the_clip()
    {
        var env = Envelope.FromPcm(Sine(16000, 300, 1.0), 16000, hopMs: 50);
        Assert.True(env.At(100) > 0.9f);
        Assert.Equal(0f, env.At(5000));
        Assert.Equal(0f, env.At(-10));
    }

    [Fact]
    public void Slice_renormalizes_a_quiet_word_to_full_range()
    {
        var loud = Sine(16000, 200, 1.0);
        var quiet = Sine(16000, 200, 0.2);
        var env = Envelope.FromPcm(loud.Concat(quiet).ToArray(), 16000, hopMs: 50);

        var word = env.Slice(200, 400);           // the quiet word
        Assert.Equal(1.0f, word.Max(), 2);        // renormalized to its own peak
    }
}

public class HapticMixerTests
{
    static HapticCue Cue(string trigger, bool snap = false) =>
        new() { StartMs = 1000, StopMs = 1500, Trigger = trigger, Snap = snap };

    [Fact]
    public void Base_layer_is_envelope_times_ramp_times_share()
    {
        var mixer = new HapticMixer { SceneRamp = 0.5, BaseLevel = 0.4 };
        Assert.Equal(0.2, mixer.Base(1.0f), 3);
        Assert.Equal(0.0, mixer.Base(0.0f), 3);
    }

    [Fact]
    public void Repetition_escalates_the_same_trigger_up_to_the_cap()
    {
        var mixer = new HapticMixer { RepetitionStep = 0.05, RepetitionCap = 0.10 };
        mixer.SetProfile("good girl", new AccentProfile(0.70, Spike: false, LingerFraction: 0));

        var first = mixer.Accent(Cue("good girl"));
        var second = mixer.Accent(Cue("good girl"));
        AccentPlan fifth = default;
        for (var i = 0; i < 3; i++) fifth = mixer.Accent(Cue("good girl"));

        Assert.Equal(0.70, first.Peak, 2);
        Assert.Equal(0.75, second.Peak, 2);
        Assert.Equal(0.80, fifth.Peak, 2);       // capped at +0.10
        Assert.Equal(5, fifth.Repetition);
    }

    [Fact]
    public void Scene_ramp_scales_accents_too()
    {
        var mixer = new HapticMixer { SceneRamp = 0.5 };
        mixer.SetProfile("drop", new AccentProfile(0.8, Spike: true, LingerFraction: 0));
        Assert.Equal(0.4, mixer.Accent(Cue("drop")).Peak, 2);
    }

    [Fact]
    public void Unknown_triggers_fall_back_by_snap_flag()
    {
        var mixer = new HapticMixer();
        Assert.True(mixer.Accent(Cue("mystery", snap: true)).Spike);
        Assert.False(mixer.Accent(Cue("mystery", snap: false)).Spike);
    }

    [Fact]
    public void Swell_lingers_past_the_word_spike_does_not()
    {
        var mixer = new HapticMixer();
        mixer.SetProfile("good girl", AccentProfile.Swell);   // linger 0.5
        mixer.SetProfile("snap", AccentProfile.Snap);

        Assert.Equal(750, mixer.Accent(Cue("good girl")).DurationMs);  // 500ms word * 1.5
        Assert.Equal(500, mixer.Accent(Cue("snap")).DurationMs);
    }

    [Fact]
    public void ResetCounts_starts_escalation_over()
    {
        var mixer = new HapticMixer();
        mixer.SetProfile("good girl", new AccentProfile(0.7, false, 0));
        mixer.Accent(Cue("good girl"));
        mixer.Accent(Cue("good girl"));
        mixer.ResetCounts();
        Assert.Equal(1, mixer.Accent(Cue("good girl")).Repetition);
    }

    [Fact]
    public void Spike_shape_attacks_instantly_and_decays()
    {
        var curve = HapticMixer.ShapeAccent(new AccentPlan(1.0, 500, Spike: true, 1), Array.Empty<float>());
        Assert.Equal(1.0f, curve[0], 2);
        Assert.True(curve[^1] < 0.1f);
        // monotonically non-increasing
        for (var i = 1; i < curve.Length; i++) Assert.True(curve[i] <= curve[i - 1] + 0.001f);
    }

    [Fact]
    public void Swell_shape_rises_holds_and_releases()
    {
        var curve = HapticMixer.ShapeAccent(new AccentPlan(1.0, 1000, Spike: false, 1), Array.Empty<float>());
        Assert.True(curve[0] < 0.2f);                    // rises from low
        Assert.True(curve[curve.Length / 2] > 0.9f);     // holds high mid-word
        Assert.True(curve[^1] < 0.2f);                   // releases
    }

    [Fact]
    public void Word_envelope_textures_the_accent_shape()
    {
        var flat = HapticMixer.ShapeAccent(new AccentPlan(1.0, 1000, Spike: false, 1), Array.Empty<float>());
        var textured = HapticMixer.ShapeAccent(new AccentPlan(1.0, 1000, Spike: false, 1),
            new float[] { 0.1f, 0.9f, 0.1f, 0.9f, 0.1f });
        Assert.NotEqual(flat, textured);
    }
}
