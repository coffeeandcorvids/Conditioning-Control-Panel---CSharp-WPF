namespace ConditioningControlPanel.Core.Services.Subliminal;

/// <summary>
/// Portable subliminal scheduler: picks a random phrase from the pool and fires
/// SubliminalReady for the UI layer to render. Pure .NET 8, no WPF dep.
/// Matches upstream SubliminalService cadence (PeriodicTimer replaces DispatcherTimer).
/// </summary>
public sealed class SubliminalScheduler : ISubliminalService
{
    private SubliminalConfig _cfg;
    private readonly Random _rng = new();

    private PeriodicTimer? _timer;
    private Task?           _timerTask;
    private CancellationTokenSource? _cts;
    private bool _running;
    private bool _disposed;

    public bool IsRunning => _running;
    public event EventHandler<SubliminalEventArgs>? SubliminalReady;

    public SubliminalScheduler(SubliminalConfig cfg) => _cfg = cfg;

    public void Start()
    {
        if (_running || !_cfg.Enabled) return;
        _running = true;
        _cts = new CancellationTokenSource();
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _cfg.FrequencySeconds)));
        _timerTask = RunLoop(_cts.Token);
    }

    public void Stop()
    {
        _running = false;
        _cts?.Cancel();
        _timer?.Dispose();
        _timer = null;
    }

    public void TriggerNow(string? phrase = null)
    {
        var text = phrase ?? Pick();
        if (string.IsNullOrWhiteSpace(text)) return;
        SubliminalReady?.Invoke(this, new SubliminalEventArgs
        {
            Phrase     = text,
            DurationMs = _cfg.DurationMs,
            Opacity    = _cfg.Opacity,
            FontSize   = _cfg.FontSize,
        });
    }

    public void UpdateSettings(SubliminalConfig cfg)
    {
        bool wasRunning = _running;
        if (wasRunning) Stop();
        _cfg = cfg;
        if (wasRunning && cfg.Enabled) Start();
        else if (wasRunning) _running = false;
    }

    // ── internals ────────────────────────────────────────────────────────────

    private async Task RunLoop(CancellationToken ct)
    {
        try
        {
            while (await _timer!.WaitForNextTickAsync(ct))
            {
                if (!_cfg.Enabled || _cfg.Phrases.Count == 0) continue;
                TriggerNow();
            }
        }
        catch (OperationCanceledException) { /* normal stop */ }
    }

    private string Pick()
    {
        if (_cfg.Phrases.Count == 0) return "";
        return _cfg.Phrases[_rng.Next(_cfg.Phrases.Count)];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _cts?.Dispose();
    }
}
