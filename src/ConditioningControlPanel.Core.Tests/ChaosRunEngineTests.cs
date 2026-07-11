using ConditioningControlPanel.Core.Services.Chaos;
using Xunit;

/// <summary>
/// The chaos run-loop engine (task #16, AI-driven reframe): run lifecycle,
/// RunIntensity escalation, spawn cadence, and the AI-drive hooks
/// (Escalate/Relax/RequestSpawn). Deterministic — no screen, no clock.
/// </summary>
public class ChaosRunEngineTests
{
    static ChaosRunEngine Engine(int durationMs = 1000, double baseRate = 1.0, double maxRate = 4.0) =>
        new(new ChaosRunConfig(durationMs, baseRate, maxRate, Seed: 0));

    [Fact]
    public void RunIntensity_progresses_zero_to_one_and_ends()
    {
        var e = Engine(1000);
        e.Start();
        Assert.Equal(0.0, e.RunIntensity, 3);

        e.Tick(500);
        Assert.Equal(0.5, e.RunIntensity, 2);

        e.Tick(500);
        Assert.Equal(1.0, e.RunIntensity, 3);
        Assert.False(e.Running);            // run ends at duration
    }

    [Fact]
    public void Stopped_engine_ticks_do_nothing()
    {
        var e = Engine();
        Assert.Equal(0, e.Tick(100));       // never started
        Assert.Equal(0, e.ElapsedMs);
    }

    [Fact]
    public void Spawn_rate_climbs_with_intensity()
    {
        var e = Engine(1000, baseRate: 1.0, maxRate: 4.0);
        e.Start();
        var atZero = e.SpawnRatePerSec;     // ~base
        e.Tick(900);
        var nearEnd = e.SpawnRatePerSec;    // ~toward max
        Assert.Equal(1.0, atZero, 3);
        Assert.True(nearEnd > atZero);
    }

    [Fact]
    public void Escalate_raises_the_intensity_floor_and_rate()
    {
        var e = Engine(10_000, baseRate: 1.0, maxRate: 5.0);
        e.Start();                          // barely into a long run → low natural intensity
        var before = e.SpawnRatePerSec;
        e.Escalate(0.5);
        Assert.True(e.RunIntensity >= 0.5);
        Assert.True(e.SpawnRatePerSec > before);
    }

    [Fact]
    public void Relax_eases_the_floor_back_down()
    {
        var e = Engine(10_000);
        e.Start();
        e.Escalate(0.6);
        e.Relax(0.4);
        Assert.Equal(0.2, e.RunIntensity, 2);   // floor 0.6 → 0.2, natural ~0
    }

    [Fact]
    public void RequestSpawn_forces_bubbles_next_tick()
    {
        var e = Engine(10_000, baseRate: 0.0, maxRate: 0.0); // no cadence spawns at all
        e.Start();
        e.RequestSpawn(3);
        Assert.Equal(3, e.Tick(1));         // only the manual ones
        Assert.Equal(0, e.Tick(1));         // consumed
    }

    [Fact]
    public void Total_spawns_track_the_cadence()
    {
        var e = Engine(1000, baseRate: 10.0, maxRate: 10.0); // flat 10/sec
        e.Start();
        var total = 0;
        for (var i = 0; i < 10; i++) total += e.Tick(100);   // 1s in 100ms steps
        Assert.InRange(total, 8, 15);        // ~10 + small seeded jitter
    }
}
