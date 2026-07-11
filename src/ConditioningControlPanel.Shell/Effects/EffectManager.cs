using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Settings;

namespace ConditioningControlPanel.Shell.Effects;

internal sealed class EffectManager : IDisposable
{
    private readonly Window _owner;
    private readonly PanelSettings _settings;
    private readonly Random _rng = new();
    private readonly DispatcherTimer _bubbleTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly DispatcherTimer _lockCardTimer = new();
    private readonly DispatcherTimer _mindWipeTimer = new();
    private readonly DispatcherTimer _mindWipeLoopTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly List<BubbleWindow> _bubbles = new();
    private readonly Dictionary<string, OverlayEffectWindow> _overlays = new();
    private readonly Dictionary<string, BouncingTextWindow> _bouncing = new();
    private readonly List<LockCardWindow> _lockCards = new();

    public bool BubblePopRunning { get; private set; }
    public bool BouncingTextRunning { get; private set; }
    public bool PinkFilterOn { get; private set; }
    public bool SpiralOn { get; private set; }
    public bool LockCardSchedulerRunning { get; private set; }
    public bool MindWipeSchedulerRunning { get; private set; }
    public bool MindWipeLoopRunning { get; private set; }

    public EffectManager(Window owner, PanelSettings settings)
    {
        _owner = owner;
        _settings = settings;
        _bubbleTimer.Tick += (_, _) => SpawnBubbleBurst(1);
        _lockCardTimer.Tick += (_, _) => { ShowLockCardRepeat(); ScheduleNextLockCard(); };
        _mindWipeTimer.Tick += (_, _) => { TriggerMindWipe(); ScheduleNextMindWipe(); };
        _mindWipeLoopTimer.Tick += (_, _) => TriggerMindWipe();
        _owner.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            if (!PinkFilterOn && !SpiralOn) return;
            e.Handled = true;
            ClearFullscreenOverlays();
        };
    }

    public void SetPinkFilter(bool on)
    {
        PinkFilterOn = on;
        foreach (var w in OverlayWindows()) w.SetPink(on, (byte)Math.Clamp(_settings.PinkFilterOpacity, 0, 180));
    }

    public void SetSpiral(bool on)
    {
        SpiralOn = on;
        foreach (var w in OverlayWindows()) w.SetSpiral(on, _settings.SpiralPath, _settings.SpiralOpacity);
    }

    public void ClearFullscreenOverlays()
    {
        PinkFilterOn = false;
        SpiralOn = false;
        foreach (var w in OverlayWindows())
        {
            w.SetPink(false);
            w.SetSpiral(false, _settings.SpiralPath, _settings.SpiralOpacity);
        }
    }

    public void SetBubblePop(bool on)
    {
        BubblePopRunning = on;
        if (on)
        {
            _bubbleTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(_settings.BubblePopIntervalSeconds, 1, 20));
            SpawnBubbleBurst(5);
            _bubbleTimer.Start();
        }
        else
        {
            _bubbleTimer.Stop();
            foreach (var b in _bubbles.ToList()) SafeClose(b);
            _bubbles.Clear();
        }
    }

    public void SetBubbleIntervalSeconds(int seconds)
    {
        _settings.BubblePopIntervalSeconds = Math.Clamp(seconds, 1, 20);
        _bubbleTimer.Interval = TimeSpan.FromSeconds(_settings.BubblePopIntervalSeconds);
    }

    public void SetBubbleFrequencyPerHour(int perHour)
    {
        _settings.BubblePopFrequencyPerHour = Math.Clamp(perHour, 1, 180);
        var seconds = Math.Max(1, (int)Math.Round(3600.0 / _settings.BubblePopFrequencyPerHour));
        SetBubbleIntervalSeconds(Math.Clamp(seconds, 1, 20));
    }

    public void UpdatePinkOpacity(int alpha)
    {
        _settings.PinkFilterOpacity = Math.Clamp(alpha, 0, 180);
        if (PinkFilterOn) SetPinkFilter(true);
    }

    public void SpawnBubbleBurst(int count)
    {
        var bubble = EffectAssetPaths.FindRepoAsset("ConditioningControlPanel/Resources/bubble.png")
            ?? EffectAssetPaths.FindRepoAsset("ConditioningControlPanel/Resources/Modassets/drone/bubble.png");
        foreach (var screen in Screens())
        for (var i = 0; i < count; i++)
        {
            if (_bubbles.Count > 36) return;
            var w = new BubbleWindow(bubble, screen, _rng);
            w.Popped += (_, _) => { _settings.Xp += 2; _settings.Save(); };
            w.Closed += (_, _) => _bubbles.Remove(w);
            _bubbles.Add(w);
            w.Start();
        }
    }

    public void SetBouncingText(bool on)
    {
        BouncingTextRunning = on;
        if (on)
        {
            foreach (var w in BouncingWindows()) w.Start(ScreenFor(w), _settings.BouncingTextPhrases);
        }
        else foreach (var w in _bouncing.Values) w.Stop();
    }

    public void ShowLockCard(string? phrase = null)
    {
        var text = string.IsNullOrWhiteSpace(phrase)
            ? (_settings.LockCardPhrases.Count > 0 ? _settings.LockCardPhrases[_rng.Next(_settings.LockCardPhrases.Count)] : "GOOD GIRLS DON'T THINK")
            : phrase!;
        foreach (var screen in Screens())
        {
            var w = new LockCardWindow();
            w.Closed += (_, _) => _lockCards.Remove(w);
            _lockCards.Add(w);
            w.ShowCard(screen, text.ToUpperInvariant(), TimeSpan.FromSeconds(_settings.LockCardDurationSeconds));
        }
    }

    public void ShowLockCardRepeat(string? phrase = null)
    {
        var repeats = Math.Clamp(_settings.LockCardRepeats, 1, 20);
        var text = string.IsNullOrWhiteSpace(phrase)
            ? (_settings.LockCardPhrases.Count > 0 ? _settings.LockCardPhrases[_rng.Next(_settings.LockCardPhrases.Count)] : "GOOD GIRLS DON'T THINK")
            : phrase!;
        ShowLockCard(repeats > 1 ? string.Join("   •   ", Enumerable.Repeat(text, repeats)) : text);
    }

    public void SetLockCardScheduler(bool on)
    {
        LockCardSchedulerRunning = on && _settings.LockCardEnabled;
        if (LockCardSchedulerRunning) ScheduleNextLockCard();
        else _lockCardTimer.Stop();
    }

    public void ScheduleNextLockCard()
    {
        if (!LockCardSchedulerRunning) return;
        var perHour = Math.Clamp(_settings.LockCardFrequencyPerHour, 1, 60);
        var baseSeconds = 3600.0 / perHour;
        var jitter = baseSeconds * 0.30 * (_rng.NextDouble() * 2 - 1);
        _lockCardTimer.Interval = TimeSpan.FromSeconds(Math.Max(20, baseSeconds + jitter));
        _lockCardTimer.Start();
    }

    public void TriggerMindWipe()
    {
        var files = EffectAssetPaths.FindAudioFiles(
            "ConditioningControlPanel/Resources/sounds/mindwipe",
            "ConditioningControlPanel/Resources/sounds/chaos",
            "ConditioningControlPanel/Resources/sub_audio",
            "ConditioningControlPanel/Resources/sounds");
        var custom = _settings.MindWipeAudioPath;
        var file = (!string.IsNullOrWhiteSpace(custom) && System.IO.File.Exists(custom)) ? custom : null;
        file ??= files.FirstOrDefault(f => f.Contains("mind", StringComparison.OrdinalIgnoreCase))
                   ?? files.FirstOrDefault(f => f.Contains("RESET", StringComparison.OrdinalIgnoreCase))
                   ?? files.FirstOrDefault(f => f.Contains("SNAP", StringComparison.OrdinalIgnoreCase))
                   ?? files.FirstOrDefault();
        if (file is null) return;
        TryPlay(file);
    }

    public void SetMindWipeScheduler(bool on)
    {
        MindWipeSchedulerRunning = on && _settings.MindWipeEnabled;
        if (MindWipeSchedulerRunning) ScheduleNextMindWipe();
        else _mindWipeTimer.Stop();
    }

    public void ScheduleNextMindWipe()
    {
        if (!MindWipeSchedulerRunning) return;
        var perHour = Math.Clamp(_settings.MindWipeFrequencyPerHour, 1, 180);
        var baseSeconds = 3600.0 / perHour;
        var jitter = baseSeconds * 0.25 * (_rng.NextDouble() * 2 - 1);
        _mindWipeTimer.Interval = TimeSpan.FromSeconds(Math.Max(10, baseSeconds + jitter));
        _mindWipeTimer.Start();
    }

    public void SetMindWipeLoop(bool on)
    {
        MindWipeLoopRunning = on && _settings.MindWipeEnabled;
        if (MindWipeLoopRunning)
        {
            TriggerMindWipe();
            _mindWipeLoopTimer.Start();
        }
        else _mindWipeLoopTimer.Stop();
    }

    private static void TryPlay(string file)
    {
        foreach (var cmd in new[] { "paplay", "pw-play", "aplay", "mpg123", "ffplay" })
        {
            try
            {
                var args = cmd switch
                {
                    "ffplay" => $"-nodisp -autoexit -loglevel quiet {Quote(file)}",
                    "mpg123" => $"-q {Quote(file)}",
                    _ => Quote(file)
                };
                Process.Start(new ProcessStartInfo(cmd, args) { UseShellExecute = false, CreateNoWindow = true });
                return;
            }
            catch { }
        }
    }

    private IEnumerable<OverlayEffectWindow> OverlayWindows()
    {
        foreach (var screen in Screens())
        {
            var key = $"{screen.X},{screen.Y},{screen.Width},{screen.Height}";
            if (!_overlays.TryGetValue(key, out var w))
            {
                w = new OverlayEffectWindow();
                // No longer wires its own ESC handler: the overlay is
                // Focusable=false (part of the click-through fix) so it can
                // never receive key events anyway. MainWindow's global ESC
                // -> PanicClearAll already clears these overlays.
                w.SetBounds(screen);
                _overlays[key] = w;
            }
            yield return w;
        }
    }

    private IEnumerable<BouncingTextWindow> BouncingWindows()
    {
        foreach (var screen in Screens())
        {
            var key = $"{screen.X},{screen.Y},{screen.Width},{screen.Height}";
            if (!_bouncing.TryGetValue(key, out var w))
            {
                w = new BouncingTextWindow();
                w.Position = new PixelPoint(screen.X, screen.Y);
                _bouncing[key] = w;
            }
            yield return w;
        }
    }

    private PixelRect ScreenFor(Window w)
    {
        var match = Screens().FirstOrDefault(s => s.X == w.Position.X && s.Y == w.Position.Y);
        return match == default ? Screens().First() : match;
    }

    private IReadOnlyList<PixelRect> Screens()
        => _owner.Screens.All.Select(s => s.Bounds).DefaultIfEmpty(new PixelRect(0, 0, 1920, 1080)).ToList();

    private static string Quote(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";

    private static void SafeClose(Window w) { try { w.Close(); } catch { } }

    public void Dispose()
    {
        _bubbleTimer.Stop();
        _lockCardTimer.Stop();
        _mindWipeTimer.Stop();
        _mindWipeLoopTimer.Stop();
        foreach (var w in _overlays.Values) SafeClose(w);
        foreach (var w in _bouncing.Values) SafeClose(w);
        foreach (var w in _bubbles.ToList()) SafeClose(w);
        foreach (var w in _lockCards.ToList()) SafeClose(w);
    }
}
