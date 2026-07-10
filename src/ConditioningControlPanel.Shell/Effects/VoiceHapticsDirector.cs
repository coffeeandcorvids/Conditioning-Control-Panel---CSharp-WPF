using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Services;
using ConditioningControlPanel.Core.Services.Audio;
using ConditioningControlPanel.Core.Services.Haptics;

namespace ConditioningControlPanel.Shell.Effects;

/// <summary>
/// Plays the voice-haptics mixer live (docs/VOICE-HAPTICS-DESIGN.md):
///
///     toy = envelope × scene_ramp + accent(trigger, repetition, phase)
///
/// While a track with a decoded envelope plays, the toy continuously follows
/// the voice's own pressure curve (base layer). When a trigger cue fires, its
/// accent ducks the base and takes the toy for its window (sidechain-for-
/// touch), then the base resumes. No envelope (uncached/undecodable audio) →
/// accents still fire with synthetic shapes; the base layer just stays quiet.
/// </summary>
internal sealed class VoiceHapticsDirector : IDisposable
{
    private readonly IAudioPlayer _audio;
    private readonly HapticService _haptics;
    private readonly HapticMixer _mixer = new();
    private readonly DispatcherTimer _baseTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    private Envelope? _envelope;
    private DateTime _accentUntil = DateTime.MinValue;
    private double _lastBaseSent = -1;

    public VoiceHapticsDirector(IAudioPlayer audio, HapticService haptics)
    {
        _audio = audio;
        _haptics = haptics;
        _mixer.SetProfile("snap", AccentProfile.Snap);
        _baseTimer.Tick += (_, _) => _ = BaseTickAsync();
        _baseTimer.Start();
    }

    /// <summary>Live scene intensity, 0..1 (session ramps feed this; 1.0 = no session).</summary>
    public double SceneRamp
    {
        get => _mixer.SceneRamp;
        set => _mixer.SceneRamp = Math.Clamp(value, 0.0, 1.0);
    }

    public bool HasEnvelope => _envelope != null;

    /// <summary>New track started: decode its local file for the base layer (null path = no envelope).</summary>
    public async Task LoadTrackAsync(string? localAudioPath)
    {
        _envelope = null;
        _mixer.ResetCounts();
        if (localAudioPath is null) return;
        var pcm = await FfmpegAudioDecoder.DecodeMonoPcmAsync(localAudioPath);
        if (pcm is { Length: > 0 })
            _envelope = Envelope.FromPcm(pcm, FfmpegAudioDecoder.SampleRate);
    }

    /// <summary>A trigger cue fired — accent layer takes the toy for its window.</summary>
    public async Task OnCueAsync(HapticCue cue)
    {
        try
        {
            if (!_haptics.IsConnected && !await _haptics.ConnectAsync()) return;

            var plan = _mixer.Accent(cue);
            var wordEnvelope = _envelope?.Slice(cue.StartMs, cue.StopMs > cue.StartMs ? cue.StopMs : cue.StartMs + cue.DurationMs)
                               ?? Array.Empty<float>();
            var curve = HapticMixer.ShapeAccent(plan, wordEnvelope);

            _accentUntil = DateTime.UtcNow.AddMilliseconds(plan.DurationMs);
            await _haptics.SetSyncPatternAsync(curve, plan.DurationMs);
        }
        catch { /* cue misses must never interrupt audio */ }
    }

    /// <summary>Stop everything haptic (panic, track end).</summary>
    public void Quiet()
    {
        _envelope = null;
        _accentUntil = DateTime.MinValue;
        _lastBaseSent = -1;
    }

    private async Task BaseTickAsync()
    {
        try
        {
            if (_envelope is null || !_audio.IsPlaying) return;
            if (DateTime.UtcNow < _accentUntil) return;          // accent owns the toy right now
            if (!_haptics.IsConnected) return;                   // base layer never force-connects

            var level = _mixer.Base(_envelope.At(_audio.PositionMs));
            if (Math.Abs(level - _lastBaseSent) < 0.02) return;  // don't spam identical updates
            _lastBaseSent = level;
            await _haptics.LiveIntensityUpdateAsync(level);
        }
        catch { /* base layer is best-effort */ }
    }

    public void Dispose()
    {
        _baseTimer.Stop();
        Quiet();
    }
}
