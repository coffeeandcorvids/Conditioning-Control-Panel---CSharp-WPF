using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Services.Audio;
using LibVLC = LibVLCSharp.Shared;

namespace ConditioningControlPanel.Shell.Effects;

/// <summary>
/// IAudioPlayer over LibVLC (system libvlc 3.x — present on the Pi via apt).
/// Handles both local files and URLs (BambiCloud CDN mp3s) with no extra
/// plumbing; VLC does its own buffering/network handling.
/// </summary>
internal sealed class VlcAudioPlayer : IAudioPlayer, IDisposable
{
    private readonly LibVLC.LibVLC _vlc;
    private readonly LibVLC.MediaPlayer _mp;
    private int _volume = 80;

    public event EventHandler? Ended;
    public event EventHandler<string>? Error;

    public VlcAudioPlayer()
    {
        LibVLC.Core.Initialize();
        _vlc = new LibVLC.LibVLC("--no-video");
        _mp = new LibVLC.MediaPlayer(_vlc) { Volume = _volume };
        _mp.EndReached += (_, _) => Ended?.Invoke(this, EventArgs.Empty);
        _mp.EncounteredError += (_, _) => Error?.Invoke(this, "playback error");
    }

    public void Play(string pathOrUrl)
    {
        try
        {
            var kind = Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var uri) && !uri.IsFile
                ? LibVLC.FromType.FromLocation
                : LibVLC.FromType.FromPath;
            using var media = new LibVLC.Media(_vlc, pathOrUrl, kind);
            _mp.Play(media);
            _mp.Volume = _volume;
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, ex.Message);
        }
    }

    public void Pause()  { if (_mp.CanPause) _mp.Pause(); }
    public void Resume() { if (!_mp.IsPlaying) _mp.Play(); }
    public void Stop()   => _mp.Stop();

    public int Volume
    {
        get => _volume;
        set { _volume = Math.Clamp(value, 0, 100); _mp.Volume = _volume; }
    }

    public bool IsPlaying  => _mp.IsPlaying;
    public long PositionMs => Math.Max(0, _mp.Time);
    public long DurationMs => Math.Max(0, _mp.Length);

    /// <summary>Jump the current item to a position (ms). No-op if nothing's loaded.</summary>
    public void SeekMs(long ms) { try { _mp.Time = Math.Max(0, ms); } catch { } }

    /// <summary>Parse a clip's length without playing it (for Load Audio → set project duration).</summary>
    public async Task<long> ProbeDurationMsAsync(string pathOrUrl)
    {
        try
        {
            var isUrl = Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var uri) && !uri.IsFile;
            var kind = isUrl ? LibVLC.FromType.FromLocation : LibVLC.FromType.FromPath;
            using var media = new LibVLC.Media(_vlc, pathOrUrl, kind);
            await media.Parse(isUrl ? LibVLC.MediaParseOptions.ParseNetwork : LibVLC.MediaParseOptions.ParseLocal, 5000);
            return Math.Max(0, media.Duration);
        }
        catch { return 0; }
    }

    public void Dispose()
    {
        try { _mp.Stop(); } catch { }
        _mp.Dispose();
        _vlc.Dispose();
    }
}
