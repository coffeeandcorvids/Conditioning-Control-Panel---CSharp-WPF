using System;

namespace ConditioningControlPanel.Core.Services.Chaos;

/// <summary>Static setup for one chaos run.</summary>
public sealed record ChaosRunConfig(
    int DurationMs,                 // run length; RunIntensity = elapsed / duration (0→1)
    double BaseSpawnPerSec = 1.0,   // spawn cadence at intensity 0
    double MaxSpawnPerSec = 4.0,    // spawn cadence at intensity 1 (+ manual escalation)
    int Seed = 0                    // deterministic jitter (0 = fixed)
);

/// <summary>
/// The engine half of the AI-driven chaos surface ("Down the Rabbit Hole") — the
/// mechanics-agnostic run loop. It owns ONLY the run lifecycle, the RunIntensity
/// escalation, and the spawn *cadence* (WHEN a bubble should appear, climbing with
/// intensity + any AI-driven escalation). It knows nothing about WHAT spawns:
/// bubble types, payloads, defuse rules and scoring are the hypno-mechanics layer's
/// (LV's), plugged in above this.
///
/// ── The integration seam ──
///   engine (here):  Start/Stop, Tick(dt) → RunIntensity + how many spawns are due,
///                   Escalate() for AI-driven intensity floor, RequestSpawn() for a
///                   manual DJ-command bubble.
///   mechanics (LV): given "spawn now", choose the bubble type + payload; score,
///                   defuse, currencies; feed detected-depth back as Escalate().
///
/// Pure + deterministic (seeded) so it unit-tests without a screen or a clock.
/// </summary>
public sealed class ChaosRunEngine
{
    private readonly ChaosRunConfig _cfg;
    private readonly Random _rng;
    private double _spawnCredit;        // fractional spawns carried between ticks
    private double _escalationFloor;    // AI-driven extra intensity, 0..1
    private int _pendingManualSpawns;   // DJ-command "spawn" requests awaiting a Tick

    public ChaosRunEngine(ChaosRunConfig cfg)
    {
        _cfg = cfg;
        _rng = new Random(cfg.Seed);
    }

    public bool Running { get; private set; }
    public long ElapsedMs { get; private set; }

    /// <summary>Run progress 0→1 (elapsed/duration), floored by any AI escalation.</summary>
    public double RunIntensity
    {
        get
        {
            var t = _cfg.DurationMs <= 0 ? 0.0 : (double)ElapsedMs / _cfg.DurationMs;
            return Math.Clamp(Math.Max(t, _escalationFloor), 0.0, 1.0);
        }
    }

    /// <summary>Current spawn cadence in bubbles/sec at the present intensity.</summary>
    public double SpawnRatePerSec =>
        _cfg.BaseSpawnPerSec + (_cfg.MaxSpawnPerSec - _cfg.BaseSpawnPerSec) * RunIntensity;

    public void Start() { Running = true; ElapsedMs = 0; _spawnCredit = 0; _escalationFloor = 0; _pendingManualSpawns = 0; }
    public void Stop()  { Running = false; }

    /// <summary>
    /// AI-drive: raise the intensity floor (e.g. the daemon detected she dropped).
    /// Additive, clamped; decays back toward the natural curve via <see cref="Relax"/>.
    /// </summary>
    public void Escalate(double amount) =>
        _escalationFloor = Math.Clamp(_escalationFloor + amount, 0.0, 1.0);

    /// <summary>Ease the AI escalation floor back down (call on a slow cadence).</summary>
    public void Relax(double amount) =>
        _escalationFloor = Math.Clamp(_escalationFloor - Math.Abs(amount), 0.0, 1.0);

    /// <summary>AI-drive: force a bubble on the next tick regardless of cadence (DJ "spawn").</summary>
    public void RequestSpawn(int count = 1) => _pendingManualSpawns += Math.Max(0, count);

    /// <summary>
    /// Advance the run by <paramref name="dtMs"/> and return how many bubbles should
    /// spawn this tick — cadence spawns (accumulated from the escalating rate, with a
    /// little seeded jitter) plus any pending manual DJ spawns. Ends the run when the
    /// duration elapses. No-op (0) while stopped.
    /// </summary>
    public int Tick(int dtMs)
    {
        if (!Running || dtMs <= 0) return 0;

        ElapsedMs += dtMs;

        _spawnCredit += SpawnRatePerSec * (dtMs / 1000.0);
        var cadence = (int)_spawnCredit;
        _spawnCredit -= cadence;
        // ±15% seeded jitter on a whole extra/held bubble, so runs aren't metronomic
        if (cadence > 0 && _rng.NextDouble() < 0.15) cadence += _rng.Next(0, 2);

        var manual = _pendingManualSpawns;
        _pendingManualSpawns = 0;

        if (ElapsedMs >= _cfg.DurationMs) Running = false;

        return cadence + manual;
    }
}
