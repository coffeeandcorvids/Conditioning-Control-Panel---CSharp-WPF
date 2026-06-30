using ConditioningControlPanel.Core.Settings;

namespace ConditioningControlPanel.Core.Services.Flash;

/// <summary>
/// Portable flash scheduler: maintains a timer, picks random images from the assets folder,
/// and raises FlashReady for the UI layer to render however it likes.
/// Zero WPF / NAudio deps — pure .NET 8.
/// </summary>
public sealed class FlashScheduler : IFlashService
{
    private FlashConfig _cfg;
    private readonly Random _rng = new();
    private List<string> _imageCache = [];
    private DateTime _imageCacheTime = DateTime.MinValue;
    private const int CacheExpirySecs = 60;

    private PeriodicTimer? _timer;
    private Task?           _timerTask;
    private CancellationTokenSource? _cts;
    private bool _running;
    private bool _disposed;

    public bool IsRunning => _running;
    public event EventHandler<FlashEventArgs>? FlashReady;

    public FlashScheduler(FlashConfig cfg) => _cfg = cfg;

    public void Start()
    {
        if (_running || !_cfg.Enabled) return;
        _running = true;
        RefreshAssets();
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

    public void TriggerNow(int? amount = null, int? durationMs = null)
    {
        var paths = PickImages(amount ?? _cfg.Amount);
        if (paths.Count == 0) return;
        FlashReady?.Invoke(this, new FlashEventArgs
        {
            ImagePaths   = paths,
            DurationMs   = durationMs ?? _cfg.DurationMs,
            Opacity      = _cfg.Opacity,
            SizeFraction = _cfg.SizeFraction,
        });
    }

    public void TriggerWithImage(string imagePath, int durationMs)
    {
        if (!File.Exists(imagePath)) { TriggerNow(1, durationMs); return; }
        FlashReady?.Invoke(this, new FlashEventArgs
        {
            ImagePaths   = [imagePath],
            DurationMs   = durationMs,
            Opacity      = _cfg.Opacity,
            SizeFraction = _cfg.SizeFraction,
        });
    }

    public void RefreshAssets() => RefreshCache(force: true);

    public void UpdateSettings(FlashConfig cfg)
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
                if (!_cfg.Enabled) continue;
                RefreshCache();
                TriggerNow();
            }
        }
        catch (OperationCanceledException) { /* normal stop */ }
    }

    private List<string> PickImages(int count)
    {
        RefreshCache();
        if (_imageCache.Count == 0) return [];
        var result = new List<string>(count);
        for (int i = 0; i < count; i++)
            result.Add(_imageCache[_rng.Next(_imageCache.Count)]);
        return result;
    }

    private void RefreshCache(bool force = false)
    {
        if (!force && (DateTime.UtcNow - _imageCacheTime).TotalSeconds < CacheExpirySecs)
            return;
        try
        {
            var path = PanelSettings.ExpandPath(_cfg.ImagesPath);
            if (!Directory.Exists(path)) { _imageCache = []; return; }
            _imageCache = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".gif",  StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch { _imageCache = []; }
        _imageCacheTime = DateTime.UtcNow;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _cts?.Dispose();
    }
}
