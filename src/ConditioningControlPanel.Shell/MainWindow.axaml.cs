using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Agents;
using ConditioningControlPanel.Core.Commands;
using ConditioningControlPanel.Core.Events;
using ConditioningControlPanel.Core.Gamification;
using ConditioningControlPanel.Core.Services;
using ConditioningControlPanel.Core.Services.Flash;
using ConditioningControlPanel.Core.Services.Haptics;
using ConditioningControlPanel.Core.Services.Playlist;
using ConditioningControlPanel.Core.Services.Subliminal;
using ConditioningControlPanel.Core.Services.Session;
using ConditioningControlPanel.Core.Settings;
using ConditioningControlPanel.Shell.Controls;
using ConditioningControlPanel.Shell.Effects;
using ConditioningControlPanel.Shell.Overlays;

namespace ConditioningControlPanel.Shell;

public partial class MainWindow : Window, ICommandSink
{
    // ── Core services ────────────────────────────────────────────────────
    private readonly PanelSettings        _settings;
    private readonly FlashScheduler       _flash;
    private readonly SubliminalScheduler  _sub;
    private readonly SessionEngine        _session;
    private readonly IReactiveAgent       _agent = new ScriptedReactiveAgent();
    private readonly ReactionExecutor     _executor;
    private readonly EffectManager        _effects;
    private string? _lastVideoName;
    private readonly HapticService        _haptics;
    private readonly PlaylistEngine       _playlist = new();
    private readonly VlcAudioPlayer       _audio = new();
    private readonly ConditioningControlPanel.Core.Services.Audio.AudioHapticSync _hapticSync;
    private readonly VoiceHapticsDirector _voiceHaptics;
    private static readonly System.Net.Http.HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };

    // ── Overlay pool ─────────────────────────────────────────────────────
    private const int FlashPoolSize = 6;
    private readonly FlashOverlayWindow[]    _flashPool;
    private int _flashPoolIdx;
    private readonly SubliminalOverlayWindow _subOverlay;

    // ── Animations ───────────────────────────────────────────────────────
    private double _angle;
    private readonly DispatcherTimer _spin = new() { Interval = TimeSpan.FromMilliseconds(33) };

    // ── Marquee ──────────────────────────────────────────────────────────
    private double _marqueeX;
    private double _marqueeSegmentWidth;
    private readonly DispatcherTimer _marqueeTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private TranslateTransform? _marqueeTransform;

    public MainWindow()
    {
        InitializeComponent();

        _settings = PanelSettings.Load();

        _flash = new FlashScheduler(BuildFlashConfig());
        _flash.FlashReady += OnFlashReady;

        _sub = new SubliminalScheduler(BuildSubConfig());
        _sub.SubliminalReady += OnSubliminalReady;

        _session = new SessionEngine(_flash, _sub, _settings);
        _session.StateChanged    += OnSessionStateChanged;
        _session.ProgressUpdated += OnSessionProgress;
        _session.SessionCompleted += (_, _) => Dispatcher.UIThread.Post(RefreshGameStats);
        _session.MomentChanged   += OnSessionMoment;
        PopulateSessionList();

        _executor = new ReactionExecutor(this);
        _effects = new EffectManager(this, _settings);
        // A mandatory video finishing on its own fires the VideoCompleted
        // reaction (so LV can chain a beat off "she watched it"), and clears
        // the panel's video-active chip.
        _effects.VideoFinished += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            Chip("🎬 video ended");
            _ = Fire(new VideoCompleted(_lastVideoName ?? "video"));
        });
        _haptics = new HapticService(new HapticSettings { Provider = HapticProviderType.Buttplug });

        // ── Audio + voice-haptics mixer ────────────────────────────────
        // playlist track changes → play the audio; the director runs the mixer:
        // continuous envelope base layer + trigger-word accents that duck it.
        _voiceHaptics = new VoiceHapticsDirector(_audio, _haptics);
        _hapticSync = new ConditioningControlPanel.Core.Services.Audio.AudioHapticSync(_audio);
        _hapticSync.CueDue += (_, cue) => _ = _voiceHaptics.OnCueAsync(cue);
        _playlist.TrackChanged += (_, track) => Dispatcher.UIThread.Post(() => OnAudioTrackChanged(track));
        _playlist.PlaylistEnded += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            _hapticSync.Stop();
            _audio.Stop();
            Chip("🎵 playlist finished");
        });
        _audio.Ended += (_, _) => Dispatcher.UIThread.Post(() => _playlist.Next());
        _audio.Error += (_, msg) => Dispatcher.UIThread.Post(() => Chip($"🎵 audio: {msg}"));

        _flashPool = Enumerable.Range(0, FlashPoolSize)
            .Select(_ => new FlashOverlayWindow()).ToArray();
        _subOverlay = new SubliminalOverlayWindow();

        // small in-app spiral preview spins continuously on dashboard; fullscreen overlay uses upstream GIF.
        Spiral.RenderTransformOrigin     = Avalonia.RelativePoint.Center;
        _spin.Tick += (_, _) =>
        {
            _angle = (_angle + 3) % 360;
            Spiral.RenderTransform     = new RotateTransform(_angle);
        };
        _spin.Start();

        // Load real CCP logo + start scrolling marquee after layout is ready
        Loaded += (_, _) => { LoadCenterLogo(); InitMarquee(); };

        // ── Custom window chrome (SystemDecorations=None) ─────────────
        BtnMinimize.Click += (_, _) => WindowState = WindowState.Minimized;
        BtnClose.Click    += (_, _) => Close();

        // ESC = panic key: same full clear as the dashboard panic button
        this.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape) PanicClearAll();
        };

        // drag to move (title bar)
        this.PointerPressed += (_, e) =>
        {
            if (e.GetPosition(this).Y < 38) BeginMoveDrag(e);
        };

        // ── Primary nav (row 1) ───────────────────────────────────────
        var primaryViews = new Dictionary<Button, Control>
        {
            [NavDashboard]    = ViewDashboard,
            [NavPresets]      = ViewPresets,
            [NavEnhancements] = ViewEnhancements,
            [NavDeeper]       = ViewDeeper,
            [NavAssets]       = ViewAssets,
        };
        foreach (var (btn, view) in primaryViews)
            btn.Click += (_, _) => SwitchPrimary(btn, view, primaryViews.Keys.ToList());

        // bottom feature strip — route to the real views where they exist,
        // say so plainly where the upstream feature isn't ported yet
        BtnAssetsLibrary.Click += (_, _) => SwitchPrimary(NavAssets, ViewAssets, primaryViews.Keys.ToList());
        BtnScheduler.Click     += (_, _) => SwitchPrimary(NavPresets, ViewPresets, primaryViews.Keys.ToList());
        BtnSessionHistory.Click += (_, _) => Chip("📋 session history lands with the timeline port");
        BtnInputsSensors.Click  += (_, _) => Chip("📡 voice/gaze sensors not ported (Windows-only upstream)");

        // ── Secondary nav (row 2) ──────────────────────────────────────
        var secondaryViews = new Dictionary<Button, Control>
        {
            [Nav2Achievements] = ViewAchievements,
            [Nav2Companion]    = ViewCompanion,
            [Nav2Lab]          = ViewLab,
            [Nav2Live]         = ViewLive,
        };
        // secondary nav overrides the main content area
        foreach (var (btn, view) in secondaryViews)
            btn.Click += (_, _) => SwitchSecondary(btn, view,
                primaryViews.Keys.Concat(secondaryViews.Keys).ToList(),
                primaryViews.Values.Concat(secondaryViews.Values).ToList());

        Nav2Profile.Click += (_, _) =>
        {
            SwitchSecondary(Nav2Profile, ViewAchievements,
                primaryViews.Keys.Concat(secondaryViews.Keys).ToList(),
                primaryViews.Values.Concat(secondaryViews.Values).ToList());
        };

        // ── Bottom bar ────────────────────────────────────────────────
        BtnStart.Click    += (_, _) => ToggleSession();
        BtnStartMenu.Click += (_, _) => Chip("▾ session options — coming next");
        BtnSave.Click     += (_, _) => { SaveAll(); Chip("💾 saved"); };
        BtnExit.Click     += (_, _) => Close();
        BtnFav.Click      += (_, _) => Chip("⭐ favourite sessions coming next");

        // ── Mosaic card clicks → show detail on right ─────────────────
        CardFlash.CardClicked        += (_, _) => ShowFeaturePanel(FeaturePanel.Flash);
        CardVisuals.CardClicked      += (_, _) => ShowFeaturePanel(FeaturePanel.Visuals);
        CardVideo.CardClicked        += (_, _) => ShowFeaturePanel(FeaturePanel.Video);
        CardSubliminal.CardClicked   += (_, _) => ShowFeaturePanel(FeaturePanel.Subliminals);
        CardSpiral.CardClicked       += (_, _) => ShowFeaturePanel(FeaturePanel.Spiral);
        CardLockCard.CardClicked     += (_, _) => ShowFeaturePanel(FeaturePanel.LockCard);
        CardPinkFog.CardClicked      += (_, _) => ShowFeaturePanel(FeaturePanel.PinkFilter);
        CardMindWipe.CardClicked     += (_, _) => ShowFeaturePanel(FeaturePanel.MindWipe);
        CardBubblePop.CardClicked    += (_, _) => ShowFeaturePanel(FeaturePanel.BubblePop);
        CardBouncingText.CardClicked += (_, _) => ShowFeaturePanel(FeaturePanel.BouncingText);
        CardSystem.CardClicked       += (_, _) => ShowFeaturePanel(FeaturePanel.System);
        CardHaptics.CardClicked      += (_, _) => ShowFeaturePanel(FeaturePanel.Haptics);

        // mosaic card toggles — every visible toggle does exactly what the card says
        CardFlash.ToggleChanged     += (_, on) => { _settings.FlashEnabled = on; _flash.UpdateSettings(BuildFlashConfig()); ChkFlashEnabled.IsChecked = on; Chip(on ? "⚡ flash ON" : "⚡ flash OFF"); };
        CardSubliminal.ToggleChanged += (_, on) => { _settings.SubliminalEnabled = on; _sub.UpdateSettings(BuildSubConfig()); ChkSubEnabled.IsChecked = on; Chip(on ? "💬 subliminals ON" : "💬 subliminals OFF"); };
        CardSpiral.ToggleChanged    += (_, on) => { SetSpiral(on); Chip(on ? "🌀 spiral ON" : "🌀 spiral OFF"); };
        CardPinkFog.ToggleChanged   += (_, on) => { SetPinkFilter(on); Chip(on ? "🌸 pink filter ON" : "🌸 pink filter OFF"); };
        CardBubblePop.ToggleChanged += (_, on) =>
        {
            _effects.SetBubblePop(on);
            _settings.BubblePopEnabled = on; _settings.Save();
            Chip(on ? "🫧 bubble pop ON" : "🫧 bubble pop OFF");
        };
        CardBouncingText.ToggleChanged += (_, on) =>
        {
            if (on) ApplyBouncingPhrases();
            _effects.SetBouncingText(on);
            _settings.BouncingTextEnabled = on; _settings.Save();
            Chip(on ? "✨ bouncing text ON" : "✨ bouncing text OFF");
        };
        CardLockCard.ToggleChanged += (_, on) =>
        {
            _effects.SetLockCardScheduler(on);
            Chip(on ? "🔒 lock card scheduler ON" : "🔒 lock card scheduler OFF");
        };
        CardMindWipe.ToggleChanged += (_, on) =>
        {
            _effects.SetMindWipeScheduler(on);
            Chip(on ? "🧠 mind wipe scheduler ON" : "🧠 mind wipe scheduler OFF");
        };

        // ── Right-panel detail controls ───────────────────────────────
        ChkFlashEnabled.IsCheckedChanged += (_, _) => ApplyFlash();
        SldFlashFreq.PropertyChanged   += (_, e) => { if (e.Property.Name == "Value") { TxtFlashFreq.Text = $"{(int)SldFlashFreq.Value}s";   ApplyFlash(); } };
        SldFlashDur.PropertyChanged    += (_, e) => { if (e.Property.Name == "Value") { TxtFlashDur.Text  = $"{(int)SldFlashDur.Value}ms";   ApplyFlash(); } };
        SldFlashAmount.PropertyChanged += (_, e) => { if (e.Property.Name == "Value") { TxtFlashAmount.Text = $"{(int)SldFlashAmount.Value}"; ApplyFlash(); } };
        BtnFlashNow.Click += (_, _) => _flash.TriggerNow();

        ChkSubEnabled.IsCheckedChanged += (_, _) => ApplySub();
        SldSubFreq.PropertyChanged += (_, e) => { if (e.Property.Name == "Value") { TxtSubFreq.Text = $"{(int)SldSubFreq.Value}s";   ApplySub(); } };
        SldSubDur.PropertyChanged  += (_, e) => { if (e.Property.Name == "Value") { TxtSubDur.Text  = $"{(int)SldSubDur.Value}ms";   ApplySub(); } };
        BtnSubNow.Click += (_, _) => _sub.TriggerNow();

        BtnSpiralToggle.Click += (_, _) => SetSpiral(!Spiral.IsVisible);
        BtnFogToggle.Click    += (_, _) => SetPinkFilter(!_effects.PinkFilterOn);
        BtnTriggerLock.Click  += (_, _) => { var ph = TxtLockPhrase.Text ?? "good girls don't think"; _effects.ShowLockCard(ph); Chip($"🔒 {ph}"); };
        SldPinkOpacity.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "Value")
            {
                TxtPinkOpacity.Text = $"{(int)SldPinkOpacity.Value}";
                _effects.UpdatePinkOpacity((int)SldPinkOpacity.Value);
                _settings.Save();
            }
        };
        BtnQuickPanic.Click += (_, _) => PanicClearAll();
        SldMasterVolume.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "Value") _audio.Volume = (int)SldMasterVolume.Value;
        };
        BtnQuickAssets.Click += (_, _) =>
        {
            var root = _settings.AssetsRoot.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            try
            {
                System.IO.Directory.CreateDirectory(root);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(root) { UseShellExecute = true });
            }
            catch (Exception ex) { Chip($"🗂 {ex.Message}"); }
        };

        SldSpiralOpacity.Value = _settings.SpiralOpacity;
        TxtSpiralOpacity.Text  = $"{_settings.SpiralOpacity}%";
        SldSpiralOpacity.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "Value")
            {
                TxtSpiralOpacity.Text = $"{(int)SldSpiralOpacity.Value}%";
                _settings.SpiralOpacity = (int)SldSpiralOpacity.Value;
                _settings.Save();
                if (_effects.SpiralOn) _effects.SetSpiral(true);   // live re-apply
            }
        };
        RescanSpiralAssets();
        CmbSpiralAsset.SelectionChanged += (_, _) =>
        {
            if (CmbSpiralAsset.SelectedItem is SpiralAssetItem item)
                ApplySpiralAssetPath(item.Path);
        };
        BtnSpiralRescan.Click += (_, _) => RescanSpiralAssets();
        BtnSpiralBrowse.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Choose spiral image",
                AllowMultiple = false,
                FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType("Spiral images")
                    { Patterns = new[] { "*.gif", "*.png", "*.webp", "*.jpg" } } }
            });
            var path = files.Count > 0 && files[0].Path.IsAbsoluteUri && files[0].Path.Scheme == "file"
                ? files[0].Path.LocalPath : null;
            if (path is null) return;
            ApplySpiralAssetPath(path);
            RescanSpiralAssets(selectPath: path);
        };
        // ── Mandatory video panel ─────────────────────────────────────
        RescanVideoAssets();
        BtnVideoRescan.Click += (_, _) => RescanVideoAssets();
        BtnVideoPlay.Click += (_, _) =>
        {
            if (CmbVideoAsset.SelectedItem is VideoAssetItem { Path: { } p })
                ExecuteVideoOp(new VideoOp("play", p));
            else
                Chip("🎬 pick a video first");
        };
        BtnVideoStop.Click += (_, _) => ExecuteVideoOp(new VideoOp("stop"));
        BtnVideoBrowse.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Choose video",
                AllowMultiple = false,
                FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType("Video files")
                    { Patterns = new[] { "*.mp4", "*.webm", "*.mkv", "*.mov", "*.avi" } } }
            });
            var path = files.Count > 0 && files[0].Path.IsAbsoluteUri && files[0].Path.Scheme == "file"
                ? files[0].Path.LocalPath : null;
            if (path is null) return;
            ExecuteVideoOp(new VideoOp("play", path));
            RescanVideoAssets(selectPath: path);
        };

        BtnBubbleToggle.Click += (_, _) => ToggleBubblePop();
        BtnBubbleBurst.Click  += (_, _) => _effects.SpawnBubbleBurst(8);
        SldBubbleInterval.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "Value")
            {
                TxtBubbleInterval.Text = $"{(int)SldBubbleInterval.Value}s";
                _effects.SetBubbleIntervalSeconds((int)SldBubbleInterval.Value);
                _settings.Save();
            }
        };
        BtnBouncingToggle.Click += (_, _) => ToggleBouncingText();
        TxtBouncingPhrases.LostFocus += (_, _) => ApplyBouncingPhrases();
        SldLockDuration.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "Value")
            {
                TxtLockDuration.Text = $"{(int)SldLockDuration.Value}s";
                _settings.LockCardDurationSeconds = (int)SldLockDuration.Value;
                _settings.Save();
            }
        };
        BtnMindWipeNow.Click += (_, _) => { _effects.TriggerMindWipe(); Chip("🧠 mind wipe"); };

        SldVisualSize.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "Value")
            {
                TxtVisualSize.Text = $"{(int)SldVisualSize.Value}%";
                _settings.FlashSizeFraction = Math.Clamp(SldVisualSize.Value / 100.0, 0.10, 1.0);
                _flash.UpdateSettings(BuildFlashConfig());
            }
        };
        SldVisualOpacity.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "Value")
            {
                TxtVisualOpacity.Text = $"{(int)SldVisualOpacity.Value}%";
                _settings.FlashOpacity = (int)SldVisualOpacity.Value;
                _flash.UpdateSettings(BuildFlashConfig());
            }
        };
        SldVisualFade.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "Value")
            {
                TxtVisualFade.Text = $"{(int)SldVisualFade.Value}%";
                _settings.FlashFadePercent = (int)SldVisualFade.Value;
            }
        };
        SldVisualDuration.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "Value")
            {
                TxtVisualDuration.Text = $"{(int)SldVisualDuration.Value}s";
                _settings.FlashDurationMs = (int)SldVisualDuration.Value * 1000;
                SldFlashDur.Value = _settings.FlashDurationMs;
                _flash.UpdateSettings(BuildFlashConfig());
            }
        };
        ChkVisualAudio.IsCheckedChanged += (_, _) => { _settings.FlashAudioEnabled = ChkVisualAudio.IsChecked ?? false; };

        SldBubbleFreq.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "Value")
            {
                TxtBubbleFreq.Text = $"{(int)SldBubbleFreq.Value}/h";
                _effects.SetBubbleFrequencyPerHour((int)SldBubbleFreq.Value);
            }
        };
        SldBubbleVolume.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "Value")
            {
                TxtBubbleVolume.Text = $"{(int)SldBubbleVolume.Value}%";
                _settings.BubblePopVolume = (int)SldBubbleVolume.Value;
            }
        };
        SldBubbleSpeed.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "Value")
            {
                TxtBubbleSpeed.Text = $"+{(int)SldBubbleSpeed.Value}%";
                _settings.BubblePopSpeedBoost = (int)SldBubbleSpeed.Value;
            }
        };
        ChkBubbleSolid.IsCheckedChanged += (_, _) => { _settings.BubblePopSolidMode = ChkBubbleSolid.IsChecked ?? true; };

        ChkLockEnabled.IsCheckedChanged += (_, _) => { _settings.LockCardEnabled = ChkLockEnabled.IsChecked ?? true; };
        BtnLockScheduleToggle.Click += (_, _) =>
        {
            _effects.SetLockCardScheduler(!_effects.LockCardSchedulerRunning);
            Chip(_effects.LockCardSchedulerRunning ? "🔒 lock scheduler ON" : "🔒 lock scheduler OFF");
        };
        SldLockFreq.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "Value")
            {
                TxtLockFreq.Text = $"{(int)SldLockFreq.Value}/h";
                _settings.LockCardFrequencyPerHour = (int)SldLockFreq.Value;
                if (_effects.LockCardSchedulerRunning) _effects.ScheduleNextLockCard();
            }
        };
        SldLockRepeats.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "Value")
            {
                TxtLockRepeats.Text = $"{(int)SldLockRepeats.Value}x";
                _settings.LockCardRepeats = (int)SldLockRepeats.Value;
            }
        };
        ChkLockStrict.IsCheckedChanged += (_, _) => { _settings.LockCardStrict = ChkLockStrict.IsChecked ?? false; };

        ChkMindEnabled.IsCheckedChanged += (_, _) => { _settings.MindWipeEnabled = ChkMindEnabled.IsChecked ?? true; };
        BtnMindScheduleToggle.Click += (_, _) =>
        {
            _effects.SetMindWipeScheduler(!_effects.MindWipeSchedulerRunning);
            Chip(_effects.MindWipeSchedulerRunning ? "🧠 mind wipe scheduler ON" : "🧠 mind wipe scheduler OFF");
        };
        BtnMindLoopToggle.Click += (_, _) =>
        {
            _effects.SetMindWipeLoop(!_effects.MindWipeLoopRunning);
            _settings.MindWipeLoop = _effects.MindWipeLoopRunning;
            Chip(_effects.MindWipeLoopRunning ? "🧠 mind wipe loop ON" : "🧠 mind wipe loop OFF");
        };
        SldMindFreq.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "Value")
            {
                TxtMindFreq.Text = $"{(int)SldMindFreq.Value}/h";
                _settings.MindWipeFrequencyPerHour = (int)SldMindFreq.Value;
                if (_effects.MindWipeSchedulerRunning) _effects.ScheduleNextMindWipe();
            }
        };
        SldMindVolume.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "Value")
            {
                TxtMindVolume.Text = $"{(int)SldMindVolume.Value}%";
                _settings.MindWipeVolume = (int)SldMindVolume.Value;
            }
        };

        BtnSaveCompanion.Click += (_, _) =>
        {
            _settings.LettaBaseUrl = TxtLettaUrl.Text;
            _settings.LettaAgentId = TxtAgentId.Text;
            SaveAll(); Chip("💾 companion settings saved");
        };

        BtnPhraseExport.Click += async (_, _) =>
        {
            var file = await StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "Export phrase backup",
                SuggestedFileName = ConditioningControlPanel.Core.Services.PhraseBackupService.GetExportFileName(),
                FileTypeChoices = new[] { new Avalonia.Platform.Storage.FilePickerFileType("CCP phrase backup")
                    { Patterns = new[] { "*.ccpphrases.json" } } }
            });
            var path = file?.Path is { IsAbsoluteUri: true, Scheme: "file" } u ? u.LocalPath : null;
            if (path is null) return;
            try
            {
                var n = new ConditioningControlPanel.Core.Services.PhraseBackupService().Export(_settings, path);
                Chip($"📦 exported {n} phrases → {System.IO.Path.GetFileName(path)}");
            }
            catch (Exception ex) { Chip($"📦 export failed: {ex.Message}"); }
        };
        BtnPhraseImport.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Import phrase backup",
                AllowMultiple = false,
                FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType("CCP phrase backup")
                    { Patterns = new[] { "*.ccpphrases.json", "*.json" } } }
            });
            var path = files.Count > 0 && files[0].Path.IsAbsoluteUri && files[0].Path.Scheme == "file"
                ? files[0].Path.LocalPath : null;
            if (path is null) return;
            var svc = new ConditioningControlPanel.Core.Services.PhraseBackupService();
            if (!svc.Validate(path, out var error)) { Chip($"📦 {error}"); return; }
            try
            {
                var n = svc.Import(_settings, path);
                _settings.Save();
                LoadUiFromSettings();
                _sub.UpdateSettings(BuildSubConfig());
                Chip($"📦 restored {n} phrases");
            }
            catch (Exception ex) { Chip($"📦 import failed: {ex.Message}"); }
        };

        // ── Live tab ──────────────────────────────────────────────────
        BtnSpiral.Click   += async (_, _) => await Fire(new KeywordTriggered("spiral"));
        BtnLock.Click     += async (_, _) => await Fire(new LockScreenResult("good girls don't think", 1, 3));
        BtnVideo.Click    += async (_, _) => await Fire(new VideoCompleted("bimbodoll_trancetone.mp4"));
        BtnPresence.Click += async (_, _) => await Fire(new PresenceDetected("Discord", "chat"));
        BtnSend.Click     += async (_, _) =>
        {
            var t = TxtMsg.Text;
            if (!string.IsNullOrWhiteSpace(t)) { TxtMsg.Text = ""; await Fire(new UserMessage(t!)); }
        };

        // Session control via BtnStart only (sessions view removed from this layout)

        LoadUiFromSettings();
        LoadCenterLogo();
        HideFeaturePanels();
        RefreshGameStats();
    }

    // ── Navigation ────────────────────────────────────────────────────────

    private Control? _activePrimary;
    private Control? _activeSecondary;

    private enum FeaturePanel
    {
        Flash, Visuals, Video, Subliminals, Spiral, LockCard,
        PinkFilter, MindWipe, BubblePop, BouncingText, System, Haptics
    }

    private void SwitchPrimary(Button active, Control view, List<Button> allPrimary)
    {
        // clear secondary selection
        foreach (var btn in new[]{ Nav2Achievements, Nav2Companion, Nav2Profile, Nav2Lab, Nav2Live })
            btn.Classes.Set("navPillActive", false);

        // hide everything
        HideAllViews();
        foreach (var btn in allPrimary) btn.Classes.Set("tabActive", btn == active);
        view.IsVisible = true;
        _activePrimary = view;
    }

    private void SwitchSecondary(Button active, Control view, List<Button> allBtns, List<Control> allViews)
    {
        HideAllViews();
        foreach (var btn in allBtns) btn.Classes.Set("tabActive", false);
        foreach (var btn in new[]{ Nav2Achievements, Nav2Companion, Nav2Profile, Nav2Lab, Nav2Live })
            btn.Classes.Set("navPillActive", btn == active);
        view.IsVisible = true;
        _activeSecondary = view;
    }

    private void HideAllViews()
    {
        foreach (var v in new Control[]{ ViewDashboard, ViewPresets, ViewEnhancements,
                                          ViewDeeper, ViewAssets, ViewAchievements, ViewCompanion,
                                          ViewLab, ViewLive })
            v.IsVisible = false;
    }

    private void ShowDetail(string title, string hint)
    {
        DetailTitle.Text = title;
        DetailHint.Text  = hint;
        // detail always shows in the right panel of Dashboard — make sure Dashboard is visible
        HideAllViews();
        ViewDashboard.IsVisible = true;
    }

    private void ShowFeaturePanel(FeaturePanel panel)
    {
        DashboardRightChrome.IsVisible = false;
        DetailNone.IsVisible = true;
        HideFeaturePanels();

        HideAllViews();
        ViewDashboard.IsVisible = true;

        switch (panel)
        {
            case FeaturePanel.Flash:
                PanelFlash.IsVisible = true;
                ShowDetail("⚡ Flash Images", "Popup images, GIFs, timing, amount, opacity, and source folder.");
                break;
            case FeaturePanel.Visuals:
                PanelVisuals.IsVisible = true;
                ShowDetail("👁 Visuals", "Flash size, opacity, fade, and duration.");
                break;
            case FeaturePanel.Video:
                PanelVideo.IsVisible = true;
                ShowDetail("🎬 Mandatory Video", "Fullscreen LibVLC video, rendered above the spiral/pink overlays. Pick or browse a file, ▶ plays it fullscreen, ESC or ■ dismisses.");
                break;
            case FeaturePanel.Subliminals:
                PanelSubliminals.IsVisible = true;
                ShowDetail("💬 Subliminals", "Text flashes, phrase pool, duration, opacity, and timing.");
                break;
            case FeaturePanel.Spiral:
                PanelOverlay.IsVisible = true;
                ShowDetail("🌀 Spiral Overlay", "Fullscreen animated overlay — toggle, opacity, and asset picker. The card's switch turns it on/off.");
                break;
            case FeaturePanel.LockCard:
                PanelLockCard.IsVisible = true;
                ShowDetail("🔒 Lock Card", "Fullscreen phrase card — phrase, duration, scheduler. 'Trigger now' fires one.");
                break;
            case FeaturePanel.PinkFilter:
                PanelOverlay.IsVisible = true;
                ShowDetail("🌸 Pink Filter", "Fullscreen pink tint — toggle and opacity. The card's switch turns it on/off.");
                break;
            case FeaturePanel.MindWipe:
                PanelMindWipe.IsVisible = true;
                ShowDetail("🧠 Mind Wipe", "Audio mind-wipe — volume, scheduler, loop. 'Trigger now' plays one.");
                break;
            case FeaturePanel.BubblePop:
                PanelBubblePop.IsVisible = true;
                ShowDetail("🫧 Bubble Pop", "Floating clickable bubbles — frequency, volume, speed. 'Toggle bubbles' starts/stops.");
                break;
            case FeaturePanel.BouncingText:
                PanelBouncingText.IsVisible = true;
                ShowDetail("✨ Bouncing Text", "DVD-screensaver text overlay — phrases, speed, size. 'Toggle bouncing text' starts/stops.");
                break;
            case FeaturePanel.System:
                PanelSystem.IsVisible = true;
                ShowDetail("⚙ System", "Assets folder, panic key, startup behavior, and local app settings.");
                break;
            case FeaturePanel.Haptics:
                PanelHaptics.IsVisible = true;
                ShowDetail("💗 Haptics", "Buttplug.io / Lovense controls. Everything unlocked; backend wiring next.");
                break;
        }
    }

    private void HideFeaturePanels()
    {
        foreach (var panel in new Control[]
        {
            PanelFlash, PanelSubliminals, PanelOverlay, PanelBubblePop, PanelBouncingText,
            PanelLockCard, PanelMindWipe, PanelVideo, PanelVisuals, PanelSystem, PanelHaptics
        })
            panel.IsVisible = false;
    }

    private void ToggleBubblePop()
    {
        _effects.SetBubblePop(!_effects.BubblePopRunning);
        _settings.BubblePopEnabled = _effects.BubblePopRunning;
        CardBubblePop.IsEnabledFeature = _effects.BubblePopRunning;
        _settings.Save();
        Chip(_effects.BubblePopRunning ? "🫧 bubble pop ON" : "🫧 bubble pop OFF");
    }

    private void ToggleBouncingText()
    {
        ApplyBouncingPhrases();
        _effects.SetBouncingText(!_effects.BouncingTextRunning);
        _settings.BouncingTextEnabled = _effects.BouncingTextRunning;
        CardBouncingText.IsEnabledFeature = _effects.BouncingTextRunning;
        _settings.Save();
        Chip(_effects.BouncingTextRunning ? "✨ bouncing text ON" : "✨ bouncing text OFF");
    }

    private void ApplyBouncingPhrases()
    {
        if (!string.IsNullOrWhiteSpace(TxtBouncingPhrases.Text))
        {
            _settings.BouncingTextPhrases = TxtBouncingPhrases.Text!
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            _settings.Save();
        }
    }

    // ── Session ───────────────────────────────────────────────────────────

    private void ToggleSession()
    {
        if (_session.State == SessionState.Idle) _session.Start();
        else _session.Stop();
    }

    private void OnSessionStateChanged(object? _, SessionStateChangedArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var running = e.State == SessionState.Running;
            var idle    = e.State == SessionState.Idle;
            BtnStart.Content = running ? "■  STOP" : "▶  START";
            BtnStart.Background = running
                ? new SolidColorBrush(Color.Parse("#E53935"))
                : new LinearGradientBrush
                  {
                      StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                      EndPoint   = new RelativePoint(1, 0, RelativeUnit.Relative),
                      GradientStops = { new GradientStop(Color.Parse("#E84393"), 0), new GradientStop(Color.Parse("#8B5CF6"), 1) }
                  };

            // session pause/stop wired to BtnStart only in this layout

            if (idle) { Dispatcher.UIThread.Post(RefreshGameStats); }
        });
    }

    private void OnSessionProgress(object? _, SessionProgressArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var t = e.Elapsed;
            var ts = $"{(int)t.TotalHours:D2}:{t.Minutes:D2}:{t.Seconds:D2}";
            if (_session.Script is { } s)
            {
                TxtRunningSession.Text = $"{s.Icon} {s.Name} — {ts} / {s.DurationMinutes}m";
                // scene deepens → the whole haptic mix deepens (0.6 → 1.0 across the session)
                _voiceHaptics.SceneRamp = 0.6 + 0.4 * Math.Clamp(t.TotalMinutes / s.DurationMinutes, 0, 1);
            }
            else
            {
                _voiceHaptics.SceneRamp = 1.0;
            }
        });
    }

    // ── Scripted sessions (presets view) ──────────────────────────────────

    private void PopulateSessionList()
    {
        foreach (var session in ConditioningControlPanel.Core.Models.Session.GetAllSessions())
        {
            var s = session;
            var row = new Border
            {
                Classes = { "panel" },
                Padding = new Thickness(10),
            };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var text = new StackPanel { Spacing = 2 };
            text.Children.Add(new TextBlock
            {
                Text = $"{s.Icon} {s.Name}  ·  {s.DurationMinutes}m · {s.Difficulty}",
                FontWeight = Avalonia.Media.FontWeight.Bold,
            });
            text.Children.Add(new TextBlock
            {
                Text = s.Description,
                Classes = { "muted" },
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            });
            Grid.SetColumn(text, 0);
            grid.Children.Add(text);

            var run = new Button { Content = "▶", FontSize = 16, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            run.Click += (_, _) => StartScriptedSession(s);
            Grid.SetColumn(run, 1);
            grid.Children.Add(run);

            row.Child = grid;
            PanelSessionList.Children.Add(row);
        }
    }

    private void StartScriptedSession(ConditioningControlPanel.Core.Models.Session s)
    {
        if (_session.State != SessionState.Idle) _session.Stop();
        _session.Start(s);
        TxtRunningSession.Text = $"{s.Icon} {s.Name} — starting…";
        Chip($"{s.Icon} {s.Name} ({s.DurationMinutes}m)");
    }

    /// <summary>Apply a scripted moment to the live effect surfaces. A default
    /// (all-off) moment arrives when the script ends — closes everything.</summary>
    private void OnSessionMoment(object? _, SessionMoment m)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (m.SpiralOn) _settings.SpiralOpacity = Math.Clamp(m.SpiralOpacity, 5, 50);
            if (_effects.SpiralOn != m.SpiralOn) SetSpiral(m.SpiralOn);
            else if (m.SpiralOn) _effects.SetSpiral(true);           // live opacity re-apply

            if (_effects.PinkFilterOn != m.PinkOn) SetPinkFilter(m.PinkOn);
            if (m.PinkOn) _effects.UpdatePinkOpacity((byte)Math.Clamp(m.PinkOpacity * 255 / 100, 0, 255));

            if (m.FlashOn)
            {
                _settings.FlashOpacity = Math.Clamp(m.FlashOpacity, 5, 100);
                // script speaks per-hour, panel speaks seconds-between-flashes
                _settings.FlashFrequency = Math.Max(1, 3600 / Math.Max(1, m.FlashPerHour));
                _flash.UpdateSettings(BuildFlashConfig());
            }

            TxtRunningMoment.Text = _session.Script is null
                ? ""
                : $"spiral {(m.SpiralOn ? m.SpiralOpacity + "%" : "off")} · pink {(m.PinkOn ? m.PinkOpacity + "%" : "off")}"
                  + $" · flash {(m.FlashOn ? m.FlashOpacity + "% @" + m.FlashPerHour + "/h" : "off")}"
                  + $" · subs {(m.SubliminalOn ? "on" : "off")}";
        });
    }

    // ── Flash overlay ─────────────────────────────────────────────────────

    private void OnFlashReady(object? _, FlashEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var screen = Screens.Primary?.Bounds ?? new PixelRect(0, 0, 1920, 1080);
            var bounds = new Rect(screen.X, screen.Y, screen.Width, screen.Height);
            foreach (var path in e.ImagePaths)
            {
                var w = _flashPool[_flashPoolIdx % FlashPoolSize];
                _flashPoolIdx++;
                w.ShowFlash(path, bounds, e);
            }
        });
    }

    // ── Subliminal overlay ────────────────────────────────────────────────

    private void OnSubliminalReady(object? _, SubliminalEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var screen = Screens.Primary?.Bounds ?? new PixelRect(0, 0, 1920, 1080);
            var bounds = new Rect(screen.X, screen.Y, screen.Width, screen.Height);
            _subOverlay.ShowSubliminal(bounds, e);
        });
    }

    // ── Settings ─────────────────────────────────────────────────────────

    private void ApplyFlash()
    {
        _settings.FlashEnabled    = ChkFlashEnabled.IsChecked ?? true;
        _settings.FlashFrequency  = (int)SldFlashFreq.Value;
        _settings.FlashDurationMs = (int)SldFlashDur.Value;
        _settings.FlashAmount     = (int)SldFlashAmount.Value;
        _settings.FlashImagesPath = TxtFlashPath.Text ?? _settings.FlashImagesPath;
        _flash.UpdateSettings(BuildFlashConfig());
        CardFlash.IsEnabledFeature = _settings.FlashEnabled;
    }

    private void ApplySub()
    {
        _settings.SubliminalEnabled    = ChkSubEnabled.IsChecked ?? true;
        _settings.SubliminalFrequency  = (int)SldSubFreq.Value;
        _settings.SubliminalDurationMs = (int)SldSubDur.Value;
        if (!string.IsNullOrWhiteSpace(TxtPhrases.Text))
            _settings.SubliminalPhrases = TxtPhrases.Text!
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        _sub.UpdateSettings(BuildSubConfig());
        CardSubliminal.IsEnabledFeature = _settings.SubliminalEnabled;
    }

    private FlashConfig BuildFlashConfig() => new(
        _settings.FlashEnabled, _settings.FlashFrequency, _settings.FlashDurationMs,
        _settings.FlashAmount, _settings.FlashOpacity, _settings.FlashSizeFraction, _settings.FlashImagesPath);

    private SubliminalConfig BuildSubConfig() => new(
        _settings.SubliminalEnabled, _settings.SubliminalFrequency, _settings.SubliminalDurationMs,
        _settings.SubliminalOpacity, _settings.SubliminalFontSize, _settings.SubliminalPhrases);

    private void LoadUiFromSettings()
    {
        ChkFlashEnabled.IsChecked  = _settings.FlashEnabled;
        SldFlashFreq.Value         = _settings.FlashFrequency;
        SldFlashDur.Value          = _settings.FlashDurationMs;
        SldFlashAmount.Value       = _settings.FlashAmount;
        TxtFlashPath.Text          = _settings.FlashImagesPath;
        TxtFlashFreq.Text          = $"{_settings.FlashFrequency}s";
        TxtFlashDur.Text           = $"{_settings.FlashDurationMs}ms";
        TxtFlashAmount.Text        = $"{_settings.FlashAmount}";

        ChkSubEnabled.IsChecked    = _settings.SubliminalEnabled;
        SldSubFreq.Value           = _settings.SubliminalFrequency;
        SldSubDur.Value            = _settings.SubliminalDurationMs;
        TxtPhrases.Text            = string.Join(Environment.NewLine, _settings.SubliminalPhrases);
        TxtSubFreq.Text            = $"{_settings.SubliminalFrequency}s";
        TxtSubDur.Text             = $"{_settings.SubliminalDurationMs}ms";

        SldVisualSize.Value        = Math.Clamp(_settings.FlashSizeFraction * 100.0, 10, 100);
        TxtVisualSize.Text         = $"{(int)SldVisualSize.Value}%";
        SldVisualOpacity.Value     = _settings.FlashOpacity;
        TxtVisualOpacity.Text      = $"{_settings.FlashOpacity}%";
        SldVisualFade.Value        = _settings.FlashFadePercent;
        TxtVisualFade.Text         = $"{_settings.FlashFadePercent}%";
        SldVisualDuration.Value    = Math.Clamp(_settings.FlashDurationMs / 1000.0, 1, 15);
        TxtVisualDuration.Text     = $"{(int)SldVisualDuration.Value}s";
        ChkVisualAudio.IsChecked   = _settings.FlashAudioEnabled;

        SldPinkOpacity.Value       = _settings.PinkFilterOpacity;
        TxtPinkOpacity.Text        = $"{_settings.PinkFilterOpacity}";
        SldBubbleFreq.Value        = _settings.BubblePopFrequencyPerHour;
        TxtBubbleFreq.Text         = $"{_settings.BubblePopFrequencyPerHour}/h";
        SldBubbleInterval.Value    = _settings.BubblePopIntervalSeconds;
        TxtBubbleInterval.Text     = $"{_settings.BubblePopIntervalSeconds}s";
        SldBubbleVolume.Value      = _settings.BubblePopVolume;
        TxtBubbleVolume.Text       = $"{_settings.BubblePopVolume}%";
        SldBubbleSpeed.Value       = _settings.BubblePopSpeedBoost;
        TxtBubbleSpeed.Text        = $"+{_settings.BubblePopSpeedBoost}%";
        ChkBubbleSolid.IsChecked   = _settings.BubblePopSolidMode;
        TxtBouncingPhrases.Text    = string.Join(Environment.NewLine, _settings.BouncingTextPhrases);
        ChkLockEnabled.IsChecked   = _settings.LockCardEnabled;
        SldLockFreq.Value          = _settings.LockCardFrequencyPerHour;
        TxtLockFreq.Text           = $"{_settings.LockCardFrequencyPerHour}/h";
        SldLockRepeats.Value       = _settings.LockCardRepeats;
        TxtLockRepeats.Text        = $"{_settings.LockCardRepeats}x";
        ChkLockStrict.IsChecked    = _settings.LockCardStrict;
        SldLockDuration.Value      = _settings.LockCardDurationSeconds;
        TxtLockDuration.Text       = $"{_settings.LockCardDurationSeconds}s";
        ChkMindEnabled.IsChecked   = _settings.MindWipeEnabled;
        SldMindFreq.Value          = _settings.MindWipeFrequencyPerHour;
        TxtMindFreq.Text           = $"{_settings.MindWipeFrequencyPerHour}/h";
        SldMindVolume.Value        = _settings.MindWipeVolume;
        TxtMindVolume.Text         = $"{_settings.MindWipeVolume}%";

        TxtLettaUrl.Text           = _settings.LettaBaseUrl ?? "http://localhost:8283";
        TxtAgentId.Text            = _settings.LettaAgentId ?? "";

        CardFlash.IsEnabledFeature      = _settings.FlashEnabled;
        CardSubliminal.IsEnabledFeature = _settings.SubliminalEnabled;
        CardPinkFog.IsEnabledFeature    = false;
        CardBubblePop.IsEnabledFeature  = _settings.BubblePopEnabled;
        CardBouncingText.IsEnabledFeature = _settings.BouncingTextEnabled;
    }

    private void LoadCenterLogo()
    {
        var logo = EffectAssetPaths.FindRepoAsset("ConditioningControlPanel/Resources/logo.png")
                   ?? EffectAssetPaths.FindRepoAsset("ConditioningControlPanel/Resources/logo2.png");
        if (logo is not null) CenterLogoImage.Source = new Bitmap(logo);
    }

    // ── Scrolling marquee — mirrors upstream's TranslateTransform animation ──
    private void InitMarquee()
    {
        const string separator = "          "; // 10 spaces between reps, like upstream
        var message = _settings.MarqueeMessage.ToUpperInvariant();
        var segment = message + separator + message + separator;

        // Tile enough copies to guarantee seamless fill past the window edge
        MarqueeText.Text = string.Concat(Enumerable.Repeat(segment, 6));

        _marqueeTransform = new TranslateTransform(0, 0);
        MarqueeText.RenderTransform = _marqueeTransform;

        // Measure a single segment width after text is set
        MarqueeText.Measure(new Size(double.PositiveInfinity, 40));
        _marqueeSegmentWidth = MarqueeText.DesiredSize.Width / 6.0; // approx per-segment

        _marqueeX = 0;
        _marqueeTimer.Tick += (_, _) =>
        {
            // 80px/sec at 16ms ≈ 1.28px per tick
            _marqueeX -= 80.0 * 0.016;
            if (_marqueeSegmentWidth > 0 && _marqueeX <= -_marqueeSegmentWidth)
                _marqueeX += _marqueeSegmentWidth;
            _marqueeTransform!.X = _marqueeX;
        };
        _marqueeTimer.Start();
    }

    private void RefreshGameStats()
    {
        TxtLevel.Text        = $"{_settings.Level}";
        TxtXp.Text           = $"{_settings.Xp}";
        TxtSessionCount.Text = $"{_settings.TotalSessions}";
        var tot = TimeSpan.FromMilliseconds(_settings.TotalTimeMs);
        TxtTotalTime.Text    = $"{(int)tot.TotalHours}h {tot.Minutes}m";
    }

    private void SaveAll()
    {
        _settings.LettaBaseUrl    = TxtLettaUrl.Text;
        _settings.LettaAgentId    = TxtAgentId.Text;
        _settings.FlashImagesPath = TxtFlashPath.Text ?? _settings.FlashImagesPath;
        _settings.FlashSizeFraction = Math.Clamp(SldVisualSize.Value / 100.0, 0.10, 1.0);
        _settings.FlashOpacity = (int)SldVisualOpacity.Value;
        _settings.FlashFadePercent = (int)SldVisualFade.Value;
        _settings.FlashDurationMs = (int)SldVisualDuration.Value * 1000;
        _settings.FlashAudioEnabled = ChkVisualAudio.IsChecked ?? false;
        _settings.PinkFilterOpacity = (int)SldPinkOpacity.Value;
        _settings.BubblePopFrequencyPerHour = (int)SldBubbleFreq.Value;
        _settings.BubblePopIntervalSeconds = (int)SldBubbleInterval.Value;
        _settings.BubblePopVolume = (int)SldBubbleVolume.Value;
        _settings.BubblePopSpeedBoost = (int)SldBubbleSpeed.Value;
        _settings.BubblePopSolidMode = ChkBubbleSolid.IsChecked ?? true;
        _settings.LockCardEnabled = ChkLockEnabled.IsChecked ?? true;
        _settings.LockCardFrequencyPerHour = (int)SldLockFreq.Value;
        _settings.LockCardRepeats = (int)SldLockRepeats.Value;
        _settings.LockCardStrict = ChkLockStrict.IsChecked ?? false;
        _settings.LockCardDurationSeconds = (int)SldLockDuration.Value;
        _settings.MindWipeEnabled = ChkMindEnabled.IsChecked ?? true;
        _settings.MindWipeFrequencyPerHour = (int)SldMindFreq.Value;
        _settings.MindWipeVolume = (int)SldMindVolume.Value;
        _settings.MindWipeLoop = _effects.MindWipeLoopRunning;
        ApplyBouncingPhrases();
        if (TxtAssetsRoot?.Text != null) _settings.AssetsRoot = TxtAssetsRoot.Text;
        _settings.Save();
    }

    // ── Agent / ICommandSink ──────────────────────────────────────────────

    private async Task Fire(PanelEvent e)
    {
        var reaction = await _agent.ReactAsync(e);
        await _executor.ExecuteAsync(reaction);
    }

    public Task ExecuteAsync(PanelCommand command, CancellationToken ct = default)
    {
        switch (command)
        {
            case Say s:      SayLog.Text = "🖤 " + s.Text + "\n" + SayLog.Text; break;
            case Spiral sp:  ApplySpiral(sp); break;
            case Flash f:    DoFlashText(f.Text); if (f.Text.Length < 40) _flash.TriggerNow(1, 2500); break;
            case PinkFog pf: SetPinkFilter(pf.On); break;
            case LockCard l: _effects.ShowLockCard(l.Sentence); Chip($"🔒 {l.Sentence}"); break;
            case Haptics h:  _ = ExecuteHapticsAsync(h); break;
            case PlaylistOp pl: ExecutePlaylistOp(pl); break;
            case VideoOp v:  ExecuteVideoOp(v); break;
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// DJ playlist + transport control. The engine owns selection state; TrackChanged
    /// drives real audio out through LibVLC, with the track's BambiCloud cue track
    /// arming trigger-word haptic sync automatically.
    /// </summary>
    private void ExecutePlaylistOp(PlaylistOp pl)
    {
        try
        {
            switch (pl.Do.ToLowerInvariant())
            {
                case "next":      _playlist.Next(); break;
                case "prev":      _playlist.Prev(); break;
                case "pause":     _audio.Pause();  Chip("🎵 paused"); return;
                case "resume":    _audio.Resume(); Chip("🎵 resumed"); return;
                case "stop":      _hapticSync.Stop(); _audio.Stop(); Chip("🎵 stopped"); return;
                case "volume" when pl.Arg != null && int.TryParse(pl.Arg, out var vol):
                    _audio.Volume = vol; SldMasterVolume.Value = _audio.Volume;
                    Chip($"🎵 volume {_audio.Volume}%"); return;
                case "shuffle":   _playlist.SetShuffle(true);  Chip("🎵 shuffle on"); return;
                case "noshuffle": _playlist.SetShuffle(false); Chip("🎵 shuffle off"); return;
                case "jump" when pl.Arg != null: _playlist.JumpTo(pl.Arg); break;
                case "load" when pl.Arg != null:
                    _playlist.Load(PlaylistDefinition.LoadFile(
                        System.IO.Path.Combine(PlaylistsDir(), pl.Arg + ".json")));
                    break;
                case "import" when pl.Arg != null:
                    _ = ImportBambiCloudPlaylist(pl.Arg);   // async; chips on completion
                    return;
                case "browse":
                    _ = BrowseBambiCloudPlaylists();
                    return;
                default: Chip($"🎵 playlist? {pl.Do}"); return;
            }
            Chip($"🎵 {_playlist.CurrentTrack?.Title ?? "(none)"}");
        }
        catch (Exception ex)
        {
            Chip($"🎵 playlist error: {ex.Message}");
        }
    }

    private string PlaylistsDir() => System.IO.Path.Combine(
        Environment.ExpandEnvironmentVariables(_settings.AssetsRoot.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))),
        "playlists");

    private string VideosDir() => System.IO.Path.Combine(
        Environment.ExpandEnvironmentVariables(_settings.AssetsRoot.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))),
        "videos");

    // ── Mandatory video ───────────────────────────────────────────────────

    /// <summary>
    /// video(play:name-or-path) / video(stop). A bare name resolves against
    /// &lt;assets&gt;/videos/ across common container extensions; an explicit
    /// path or URL is used as-is. Fullscreen, above the effect overlays.
    /// </summary>
    private void ExecuteVideoOp(VideoOp v)
    {
        try
        {
            switch (v.Do.ToLowerInvariant())
            {
                case "stop":
                    _effects.StopVideo();
                    Chip("🎬 video stopped");
                    return;
                case "play" when !string.IsNullOrWhiteSpace(v.Arg):
                    var resolved = ResolveVideoPath(v.Arg!);
                    if (resolved is null) { Chip($"🎬 no video: {v.Arg}"); return; }
                    _lastVideoName = System.IO.Path.GetFileName(resolved);
                    _effects.PlayVideo(resolved);
                    Chip($"🎬 ▶ {_lastVideoName}");
                    return;
                default:
                    Chip($"🎬 video? {v.Do}");
                    return;
            }
        }
        catch (Exception ex)
        {
            Chip($"🎬 video error: {ex.Message}");
        }
    }

    private sealed record VideoAssetItem(string Name, string? Path)
    {
        public override string ToString() => Name;
    }

    private void RescanVideoAssets(string? selectPath = null)
    {
        var items = new List<VideoAssetItem>();
        var dir = VideosDir();
        if (System.IO.Directory.Exists(dir))
        {
            foreach (var f in System.IO.Directory.EnumerateFiles(dir).OrderBy(f => f))
            {
                var ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
                if (ext is ".mp4" or ".webm" or ".mkv" or ".mov" or ".avi")
                    items.Add(new(System.IO.Path.GetFileName(f), f));
            }
        }
        if (selectPath != null && System.IO.File.Exists(selectPath) && items.All(i => i.Path != selectPath))
            items.Add(new(System.IO.Path.GetFileName(selectPath) + " (custom)", selectPath));
        if (items.Count == 0)
            items.Add(new("(drop files in assets/videos)", null));

        CmbVideoAsset.ItemsSource  = items;
        CmbVideoAsset.SelectedItem = items.FirstOrDefault(i => i.Path == selectPath) ?? items[0];
    }

    /// <summary>Resolve a video arg to a playable path: explicit local file / URL as-is, else by bare name under the videos dir.</summary>
    private string? ResolveVideoPath(string arg)
    {
        if (Uri.TryCreate(arg, UriKind.Absolute, out var uri) && !uri.IsFile) return arg;   // URL
        if (System.IO.File.Exists(arg)) return arg;                                          // explicit path

        var dir = VideosDir();
        if (System.IO.Path.HasExtension(arg))
        {
            var direct = System.IO.Path.Combine(dir, arg);
            return System.IO.File.Exists(direct) ? direct : null;
        }
        return new[] { ".mp4", ".webm", ".mkv", ".mov", ".avi" }
            .Select(ext => System.IO.Path.Combine(dir, arg + ext))
            .FirstOrDefault(System.IO.File.Exists);
    }

    // ── Audio playback + cue-track sync ───────────────────────────────────

    private void OnAudioTrackChanged(PlaylistTrack track)
    {
        _hapticSync.Stop();
        _voiceHaptics.Quiet();

        // Prefer a cached local copy: VLC reads it faster AND the director can
        // decode it for the envelope base layer. First play of a CDN track
        // streams while the cache fills; envelope kicks in from the next play.
        var local = CachedAudioPathFor(track.Path);
        _audio.Play(local ?? track.Path);
        Chip($"🎵 ▶ {track.Title}{(local != null ? "" : " (caching…)")}");

        _ = _voiceHaptics.LoadTrackAsync(local);
        if (local is null && Uri.TryCreate(track.Path, UriKind.Absolute, out var uri) && !uri.IsFile)
            _ = CacheAudioAsync(uri);

        if (!string.IsNullOrWhiteSpace(track.HapticsPath))
            _ = LoadCueTrackAsync(track.HapticsPath!);
    }

    private string AudioCacheDir() => System.IO.Path.Combine(
        _settings.AssetsRoot.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
        "audio-cache");

    /// <summary>Local path for a track: the file itself if already local, the cached copy if we have one, else null.</summary>
    private string? CachedAudioPathFor(string pathOrUrl)
    {
        if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            var cached = System.IO.Path.Combine(AudioCacheDir(), System.IO.Path.GetFileName(uri.LocalPath));
            return System.IO.File.Exists(cached) ? cached : null;
        }
        return System.IO.File.Exists(pathOrUrl) ? pathOrUrl : null;
    }

    private async Task CacheAudioAsync(Uri uri)
    {
        try
        {
            System.IO.Directory.CreateDirectory(AudioCacheDir());
            var target = System.IO.Path.Combine(AudioCacheDir(), System.IO.Path.GetFileName(uri.LocalPath));
            var tmp = target + ".part";
            await using (var net = await _http.GetStreamAsync(uri))
            await using (var file = System.IO.File.Create(tmp))
                await net.CopyToAsync(file);
            System.IO.File.Move(tmp, target, overwrite: true);
        }
        catch { /* cache miss just means no envelope this pass */ }
    }

    /// <summary>Fetch a haptics cue track (URL or local path), cache URL fetches
    /// under &lt;assets&gt;/haptics-cache/, and arm the sync. Missing/broken cue
    /// files just mean no toy sync for that track — audio keeps playing.</summary>
    private async Task LoadCueTrackAsync(string hapticsPath)
    {
        try
        {
            string json;
            if (Uri.TryCreate(hapticsPath, UriKind.Absolute, out var uri) && !uri.IsFile)
            {
                var cacheDir = System.IO.Path.Combine(
                    _settings.AssetsRoot.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
                    "haptics-cache");
                System.IO.Directory.CreateDirectory(cacheDir);
                var cacheFile = System.IO.Path.Combine(cacheDir, System.IO.Path.GetFileName(uri.LocalPath));
                if (System.IO.File.Exists(cacheFile))
                    json = await System.IO.File.ReadAllTextAsync(cacheFile);
                else
                {
                    json = await _http.GetStringAsync(uri);
                    await System.IO.File.WriteAllTextAsync(cacheFile, json);
                }
            }
            else
            {
                json = await System.IO.File.ReadAllTextAsync(hapticsPath);
            }

            var track = ConditioningControlPanel.Core.Services.Haptics.HapticCueTrack.Parse(json);
            _hapticSync.Start(track);
            Dispatcher.UIThread.Post(() =>
                Chip($"💗 cue track armed — {track.Cues.Count} cues ({string.Join(", ", track.Triggers.Take(4))}…)"));
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => Chip($"💗 no cue sync: {ex.Message}"));
        }
    }


    /// <summary>
    /// playlist(import:&lt;name-or-id&gt;) — pull a public BambiCloud playlist through the
    /// JSON API, save it as a native playlist under &lt;assets&gt;/playlists/, and load it.
    /// Tracks keep their CDN audio + haptics-pattern URLs.
    /// </summary>
    private async Task ImportBambiCloudPlaylist(string query)
    {
        try
        {
            Chip($"🎵 fetching '{query}' from BambiCloud…");
            using var bc = new ConditioningControlPanel.Core.Services.BambiCloud.BambiCloudClient();
            var found = await bc.FindPlaylistAsync(query);
            if (found is null) { Chip($"🎵 no BambiCloud playlist matches '{query}'"); return; }

            var def = ConditioningControlPanel.Core.Services.BambiCloud.BambiCloudClient.ToPlaylistDefinition(found);
            if (def.Tracks.Count == 0) { Chip($"🎵 '{found.Name}' has no playable tracks"); return; }

            System.IO.Directory.CreateDirectory(PlaylistsDir());
            var slug = string.Concat(def.Name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-');
            def.SaveFile(System.IO.Path.Combine(PlaylistsDir(), slug + ".json"));

            _playlist.Load(def);
            Chip($"🎵 imported '{def.Name}' ({def.Tracks.Count} tracks) → {slug}.json");
        }
        catch (Exception ex)
        {
            Chip($"🎵 import failed: {ex.Message}");
        }
    }

    private async Task BrowseBambiCloudPlaylists()
    {
        try
        {
            Chip("🎵 listing BambiCloud playlists…");
            using var bc = new ConditioningControlPanel.Core.Services.BambiCloud.BambiCloudClient();
            var all = await bc.GetPlaylistsAsync();
            var top = string.Join(" · ", all.Take(8).Select(p => $"{p.Name} ({p.Files.Count})"));
            Chip(all.Count == 0 ? "🎵 no playlists returned" : $"🎵 {all.Count} playlists: {top}…");
        }
        catch (Exception ex)
        {
            Chip($"🎵 browse failed: {ex.Message}");
        }
    }

    /// <summary>
    /// haptics(intensity|pattern) → real toy via HapticService (Buttplug/Intiface).
    /// Lazy-connects on first use so the panel runs fine with no Intiface up;
    /// pattern strings map to VibrationMode (pulse/wave/heartbeat/escalate/earthquake).
    /// </summary>
    private async Task ExecuteHapticsAsync(Haptics h)
    {
        try
        {
            if (!_haptics.IsConnected && !await _haptics.ConnectAsync())
            {
                Chip("💗 haptics: no device (Intiface up? toy on?)");
                return;
            }

            if (h.Pattern is { } p && Enum.TryParse<VibrationMode>(p, true, out var mode))
                await _haptics.ApplyVibrationModeAsync(h.Intensity ?? 0.6, 1500, mode);
            else if (h.Intensity is { } i)
                await _haptics.ApplyVibrationModeAsync(i, 800, VibrationMode.Constant);
            else
                await _haptics.ApplyVibrationModeAsync(0.6, 800, VibrationMode.Pulse);

            Chip($"💗 haptics {(h.Pattern ?? (h.Intensity?.ToString("F1") ?? "pulse"))}");
        }
        catch (Exception ex)
        {
            Chip($"💗 haptics error: {ex.Message}");
        }
    }

    /// <summary>Panic: every overlay and ambient effect off, schedulers included.
    /// Reachable from ESC anywhere and the dashboard panic button — identical behaviour.</summary>
    private void PanicClearAll()
    {
        if (_effects.SpiralOn)     SetSpiral(false);
        if (_effects.PinkFilterOn) SetPinkFilter(false);
        if (_effects.BubblePopRunning)          _effects.SetBubblePop(false);
        if (_effects.BouncingTextRunning)       _effects.SetBouncingText(false);
        if (_effects.LockCardSchedulerRunning)  _effects.SetLockCardScheduler(false);
        if (_effects.MindWipeSchedulerRunning)  _effects.SetMindWipeScheduler(false);
        if (_effects.MindWipeLoopRunning)       _effects.SetMindWipeLoop(false);
        if (_effects.VideoPlaying)              _effects.StopVideo();
        _hapticSync.Stop();
        _voiceHaptics.Quiet();
        _audio.Stop();
        _ = _haptics.StopAsync();
        foreach (var card in new[] { CardSpiral, CardPinkFog, CardBubblePop, CardBouncingText, CardLockCard, CardMindWipe })
            card.IsEnabledFeature = false;
        Chip("🛑 everything stopped");
    }

    private void SetSpiral(bool on)
    {
        Spiral.IsVisible            = on;
        CardSpiral.IsEnabledFeature = on;
        _effects.SetSpiral(on);
    }

    private sealed record SpiralAssetItem(string Name, string? Path)
    {
        public override string ToString() => Name;
    }

    private string SpiralAssetsDir()
    {
        var root = _settings.AssetsRoot.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        return System.IO.Path.Combine(root, "spirals");
    }

    private void RescanSpiralAssets(string? selectPath = null)
    {
        var items = new List<SpiralAssetItem> { new("(bundled default)", null) };
        var dir = SpiralAssetsDir();
        if (System.IO.Directory.Exists(dir))
        {
            foreach (var f in System.IO.Directory.EnumerateFiles(dir).OrderBy(f => f))
            {
                var ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
                if (ext is ".gif" or ".png" or ".webp" or ".jpg" or ".jpeg")
                    items.Add(new(System.IO.Path.GetFileName(f), f));
            }
        }
        var target = selectPath ?? _settings.SpiralPath;
        if (target != null && System.IO.File.Exists(target) && items.All(i => i.Path != target))
            items.Add(new(System.IO.Path.GetFileName(target) + " (custom)", target));

        CmbSpiralAsset.ItemsSource  = items;
        CmbSpiralAsset.SelectedItem = items.FirstOrDefault(i => i.Path == target) ?? items[0];
    }

    private void ApplySpiralAssetPath(string? path)
    {
        _settings.SpiralPath = path;
        _settings.Save();
        if (_effects.SpiralOn) _effects.SetSpiral(true);   // live swap
        Chip($"🌀 spiral asset: {(path is null ? "bundled default" : System.IO.Path.GetFileName(path))}");
    }

    /// <summary>
    /// DJ-surface spiral: opacity (clamped to upstream's 5–50%) and asset (a gif name
    /// under &lt;assets&gt;/spirals/) apply to settings before the overlay redraws, so
    /// LV can retheme the spiral mid-scene without touching the settings view.
    /// </summary>
    private void ApplySpiral(Spiral sp)
    {
        if (sp.Opacity is { } o)
            _settings.SpiralOpacity = Math.Clamp(o, 5, 50);
        if (sp.Asset is { } a)
        {
            // accept any spiral asset by bare name — LV picks per scene without
            // knowing the extension (video formats land with the LibVLC layer)
            var dir = SpiralAssetsDir();
            var hit = System.IO.Path.HasExtension(a) && System.IO.File.Exists(System.IO.Path.Combine(dir, a))
                ? System.IO.Path.Combine(dir, a)
                : new[] { ".gif", ".webp", ".png", ".jpg" }
                    .Select(ext => System.IO.Path.Combine(dir, a + ext))
                    .FirstOrDefault(System.IO.File.Exists);
            if (hit != null) _settings.SpiralPath = hit;
            else Chip($"🌀 no spiral asset: {a}");
        }
        SetSpiral(sp.On);
        if (sp.On && (sp.Opacity != null || sp.Asset != null))
            Chip($"🌀 spiral {_settings.SpiralOpacity}%{(sp.Asset != null ? $" · {sp.Asset}" : "")}");
    }

    private void SetPinkFilter(bool on)
    {
        Fog.IsVisible = on;
        CardPinkFog.IsEnabledFeature = on;
        _effects.SetPinkFilter(on);
    }

    private void DoFlashText(string text)
    {
        FlashText.Text      = text;
        FlashText.IsVisible = true;
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
        t.Tick += (_, _) => { FlashText.IsVisible = false; t.Stop(); };
        t.Start();
    }

    private void Chip(string text)
    {
        StatusChip.Text = text;
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        t.Tick += (_, _) => { StatusChip.Text = ""; t.Stop(); };
        t.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveAll();   // sliders/checkboxes mutate _settings live; persist them on exit
        _hapticSync.Dispose();
        _voiceHaptics.Dispose();
        _audio.Dispose();
        _session.Dispose();
        _flash.Dispose();
        _sub.Dispose();
        _effects.Dispose();
        _haptics.Dispose();
        foreach (var w in _flashPool) try { w.Close(); } catch { }
        try { _subOverlay.Close(); } catch { }
        base.OnClosed(e);
    }
}
