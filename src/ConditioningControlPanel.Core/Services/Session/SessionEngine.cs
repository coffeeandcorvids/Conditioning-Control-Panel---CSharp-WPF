using ConditioningControlPanel.Core.Services.Flash;
using ConditioningControlPanel.Core.Services.Subliminal;
using ConditioningControlPanel.Core.Settings;

namespace ConditioningControlPanel.Core.Services.Session;

/// <summary>
/// Central coordinator: owns the session lifecycle (Start/Stop/Pause), drives Flash + Subliminal
/// services, fires progress events, and updates gamification. No UI dep — pure .NET 8.
///
/// Our version replaces the upstream OpenRouter AI backend with Letta/Vesper routing; the panel
/// itself is a lightweight shell that LV drives. Session phases mirror upstream's phase model
/// but drop the Patreon/premium gating (all capabilities unlocked by construction).
/// </summary>
public sealed class SessionEngine : IDisposable
{
    // ── Events ────────────────────────────────────────────────────────────────
    public event EventHandler<SessionStateChangedArgs>? StateChanged;
    public event EventHandler<SessionProgressArgs>?     ProgressUpdated;
    public event EventHandler?                          SessionCompleted;

    /// <summary>Raised while a scripted session runs, whenever the evaluated moment
    /// changes — feature windows opening/closing, ramp values moving.</summary>
    public event EventHandler<SessionMoment>?           MomentChanged;

    // ── Services ──────────────────────────────────────────────────────────────
    private readonly IFlashService       _flash;
    private readonly ISubliminalService  _subliminal;
    private readonly PanelSettings       _settings;

    // ── State ─────────────────────────────────────────────────────────────────
    private SessionState   _state = SessionState.Idle;
    private DateTime       _startedAt;
    private TimeSpan       _pauseAccum;
    private DateTime       _pausedAt;
    private CancellationTokenSource? _cts;
    private Task?          _progressLoop;
    private bool           _disposed;
    private Models.Session? _script;
    private SessionMoment? _lastMoment;

    public SessionState State => _state;

    /// <summary>The scripted session currently driving the engine, if any.</summary>
    public Models.Session? Script => _script;
    public TimeSpan     Elapsed => _state == SessionState.Running
        ? (DateTime.UtcNow - _startedAt) - _pauseAccum
        : (_state == SessionState.Paused ? (_pausedAt - _startedAt) - _pauseAccum : TimeSpan.Zero);

    public SessionEngine(IFlashService flash, ISubliminalService subliminal, PanelSettings settings)
    {
        _flash      = flash;
        _subliminal = subliminal;
        _settings   = settings;
    }

    // ── Public control ────────────────────────────────────────────────────────

    /// <summary>Start a session; pass a <paramref name="script"/> to run its feature
    /// windows and ramps (evaluated per tick, surfaced via <see cref="MomentChanged"/>).</summary>
    public void Start(Models.Session? script = null)
    {
        if (_state == SessionState.Running) return;
        if (_state == SessionState.Paused)  { Resume(); return; }

        _script     = script;
        _lastMoment = null;
        _startedAt  = DateTime.UtcNow;
        _pauseAccum = TimeSpan.Zero;
        _cts        = new CancellationTokenSource();

        _flash.Start();
        _subliminal.Start();

        SetState(SessionState.Running);
        _progressLoop = ProgressLoopAsync(_cts.Token);
    }

    public void Pause()
    {
        if (_state != SessionState.Running) return;
        _pausedAt = DateTime.UtcNow;
        _flash.Stop();
        _subliminal.Stop();
        SetState(SessionState.Paused);
    }

    public void Resume()
    {
        if (_state != SessionState.Paused) return;
        _pauseAccum += DateTime.UtcNow - _pausedAt;
        _flash.Start();
        _subliminal.Start();
        SetState(SessionState.Running);
    }

    public void Stop()
    {
        if (_state == SessionState.Idle) return;
        _cts?.Cancel();
        _flash.Stop();
        _subliminal.Stop();
        RecordSession();
        if (_script != null)
        {
            // close any feature windows the script opened
            MomentChanged?.Invoke(this, default);
            _script = null;
            _lastMoment = null;
        }
        SetState(SessionState.Idle);
        SessionCompleted?.Invoke(this, EventArgs.Empty);
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private async Task ProgressLoopAsync(CancellationToken ct)
    {
        using var ticker = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await ticker.WaitForNextTickAsync(ct))
            {
                if (_state != SessionState.Running) continue;
                var elapsed = Elapsed;
                ProgressUpdated?.Invoke(this, new SessionProgressArgs(elapsed));

                if (_script is { } script)
                {
                    if (elapsed.TotalMinutes >= script.DurationMinutes) { Stop(); return; }
                    var moment = SessionScript.MomentAt(script.Settings, script.DurationMinutes, elapsed);
                    if (moment != _lastMoment)
                    {
                        _lastMoment = moment;
                        MomentChanged?.Invoke(this, moment);
                    }
                }
            }
        }
        catch (OperationCanceledException) { /* normal */ }
    }

    private void SetState(SessionState s)
    {
        _state = s;
        StateChanged?.Invoke(this, new SessionStateChangedArgs(s));
    }

    private void RecordSession()
    {
        var elapsed = Elapsed;
        _settings.TotalSessions++;
        _settings.TotalTimeMs += (long)elapsed.TotalMilliseconds;
        // XP: 10 per minute, capped at 60 mins to avoid AFK farming
        var xp = (int)Math.Min(elapsed.TotalMinutes, 60) * 10;
        _settings.Xp += xp;
        // Level up? Use LevelCurve from gamification
        _settings.Save();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_state != SessionState.Idle) Stop();
        _cts?.Dispose();
    }
}

public enum SessionState { Idle, Running, Paused }

public sealed class SessionStateChangedArgs(SessionState state) : EventArgs
{
    public SessionState State { get; } = state;
}

public sealed class SessionProgressArgs(TimeSpan elapsed) : EventArgs
{
    public TimeSpan Elapsed { get; } = elapsed;
}
