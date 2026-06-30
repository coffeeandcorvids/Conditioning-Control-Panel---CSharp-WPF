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

    public SessionState State => _state;
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

    public void Start()
    {
        if (_state == SessionState.Running) return;
        if (_state == SessionState.Paused)  { Resume(); return; }

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
                ProgressUpdated?.Invoke(this, new SessionProgressArgs(Elapsed));
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
