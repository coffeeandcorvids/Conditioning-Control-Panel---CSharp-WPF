using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Services.Haptics;

namespace ConditioningControlPanel.Core.Services.Audio;

/// <summary>
/// Fires haptic cues in time with audio playback: polls the player position
/// and raises <see cref="CueDue"/> for every cue whose start falls inside the
/// window since the previous poll — so trigger words in the audio ("bambi",
/// "good girl", "snap"…) hit the toy on the spoken word. Rewinds/seeks
/// backwards reset the window instead of replaying the whole past.
/// The host owns what a cue *does* (HapticService.TriggerAsync etc.).
/// </summary>
public sealed class AudioHapticSync : IDisposable
{
    private readonly IAudioPlayer _player;
    private readonly TimeSpan _pollInterval;
    private CancellationTokenSource? _cts;
    private HapticCueTrack? _track;
    private long _lastPosMs;

    public event EventHandler<HapticCue>? CueDue;

    public bool Running => _cts is { IsCancellationRequested: false };
    public HapticCueTrack? Track => _track;

    public AudioHapticSync(IAudioPlayer player, TimeSpan? pollInterval = null)
    {
        _player = player;
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(150);
    }

    /// <summary>Start syncing the given cue track against the player. Replaces any previous track.</summary>
    public void Start(HapticCueTrack track)
    {
        Stop();
        _track = track;
        _lastPosMs = _player.PositionMs;
        _cts = new CancellationTokenSource();
        _ = PollLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        _track = null;
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                Tick();
                await Task.Delay(_pollInterval, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* normal */ }
    }

    /// <summary>One poll step — internal so tests can drive time by hand.</summary>
    internal void Tick()
    {
        if (_track is null) return;
        var pos = _player.PositionMs;
        if (pos < _lastPosMs)
        {
            // seek backwards / restart: move the window, don't replay history
            _lastPosMs = pos;
            return;
        }
        if (pos == _lastPosMs) return;

        foreach (var cue in _track.CuesBetween(_lastPosMs, pos))
            CueDue?.Invoke(this, cue);
        _lastPosMs = pos;
    }

    public void Dispose() => Stop();
}
