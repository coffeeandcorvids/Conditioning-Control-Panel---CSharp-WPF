using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Agents;
using ConditioningControlPanel.Core.Commands;
using ConditioningControlPanel.Core.Events;
using ConditioningControlPanel.Core.Gamification;
using ConditioningControlPanel.Core.Services.Flash;
using ConditioningControlPanel.Core.Services.Subliminal;
using ConditioningControlPanel.Core.Services.Session;
using ConditioningControlPanel.Core.Settings;
using ConditioningControlPanel.Shell.Controls;
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

    // ── Overlay pool ─────────────────────────────────────────────────────
    private const int FlashPoolSize = 6;
    private readonly FlashOverlayWindow[]    _flashPool;
    private int _flashPoolIdx;
    private readonly SubliminalOverlayWindow _subOverlay;

    // ── Spiral animation ─────────────────────────────────────────────────
    private double _angle;
    private readonly DispatcherTimer _spin = new() { Interval = TimeSpan.FromMilliseconds(33) };

    // ── Nav state ────────────────────────────────────────────────────────
    private Control? _currentView;
    private Control? _activeDetailPanel;

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

        _executor = new ReactionExecutor(this);

        _flashPool = Enumerable.Range(0, FlashPoolSize)
            .Select(_ => new FlashOverlayWindow()).ToArray();
        _subOverlay = new SubliminalOverlayWindow();

        // spiral (logo + live stage)
        LogoSpiral.RenderTransformOrigin = Avalonia.RelativePoint.Center;
        Spiral.RenderTransformOrigin     = Avalonia.RelativePoint.Center;
        _spin.Tick += (_, _) =>
        {
            _angle = (_angle + 4) % 360;
            LogoSpiral.RenderTransform = new RotateTransform(_angle);
            Spiral.RenderTransform     = new RotateTransform(_angle);
        };
        _spin.Start(); // logo always spins on the home mosaic

        // ── Nav buttons ──────────────────────────────────────────────────
        var navMap = new Dictionary<Button, Control>
        {
            [NavHome]         = ViewHome,
            [NavSessions]     = ViewSessions,
            [NavPresets]      = ViewPresets,
            [NavQuests]       = ViewQuests,
            [NavAchievements] = ViewAchievements,
            [NavCompanion]    = ViewCompanion,
            [NavLive]         = ViewLive,
        };
        foreach (var (btn, view) in navMap)
            btn.Click += (_, _) => NavigateTo(btn, view, navMap.Keys.ToList());

        _currentView = ViewHome;

        // ── Bottom bar ───────────────────────────────────────────────────
        BtnStart.Click += (_, _) => ToggleSession();
        BtnPause.Click += (_, _) => { if (_session.State == SessionState.Running) _session.Pause(); else _session.Resume(); };
        BtnSave.Click  += (_, _) => { SaveAll(); Chip("💾 saved"); };
        BtnExit.Click  += (_, _) => Close();

        // ── Left rail quick-toggles ───────────────────────────────────────
        RailFlash.Click   += (_, _) => { _settings.FlashEnabled   = !_settings.FlashEnabled;   _flash.UpdateSettings(BuildFlashConfig()); UpdateRail(); };
        RailSub.Click     += (_, _) => { _settings.SubliminalEnabled = !_settings.SubliminalEnabled; _sub.UpdateSettings(BuildSubConfig()); UpdateRail(); };
        RailSpiral.Click  += (_, _) => SetSpiral(!Spiral.IsVisible);
        RailFog.Click     += (_, _) => Fog.IsVisible = !Fog.IsVisible;
        RailLock.Click    += (_, _) => Fire(new KeywordTriggered("lock")).GetAwaiter();
        RailHaptics.Click += (_, _) => Chip("💗 haptics (wiring next)");

        // ── Mosaic card clicks → navigate to feature detail ───────────────
        CardFlash.CardClicked      += (_, _) => OpenDetail(DetailFlash);
        CardSubliminal.CardClicked += (_, _) => OpenDetail(DetailSub);
        CardSpiral.CardClicked     += (_, _) => OpenDetail(DetailSpiral);
        CardPinkFog.CardClicked    += (_, _) => OpenDetail(DetailFog);
        CardHaptics.CardClicked    += (_, _) => OpenDetail(DetailHaptics);
        CardLockCard.CardClicked   += (_, _) => OpenDetail(DetailLockCard);
        CardVisuals.CardClicked    += (_, _) => OpenDetail(DetailFlash); // shares Flash detail for now
        // non-wired cards just show placeholder for now
        CardVideo.CardClicked      += (_, _) => Chip("🎬 Video — porting LibVLCSharp.Avalonia next");
        CardMindWipe.CardClicked   += (_, _) => Chip("🧠 Mind Wipe — alpha blur overlay coming next");
        CardBubblePop.CardClicked  += (_, _) => Chip("🫧 Bubble Pop — minigame coming next");
        CardBouncingText.CardClicked += (_, _) => Chip("✨ Bouncing Text — coming next");
        CardSystem.CardClicked     += (_, _) => OpenDetail(null); // detail = empty, show companion settings
        BtnDetailBack.Click        += (_, _) => CloseDetail();

        // ── Mosaic card toggle buttons ────────────────────────────────────
        CardFlash.ToggleChanged     += (_, on) => { _settings.FlashEnabled = on; _flash.UpdateSettings(BuildFlashConfig()); };
        CardSubliminal.ToggleChanged += (_, on) => { _settings.SubliminalEnabled = on; _sub.UpdateSettings(BuildSubConfig()); };
        CardSpiral.ToggleChanged    += (_, on) => SetSpiral(on);
        CardPinkFog.ToggleChanged   += (_, on) => Fog.IsVisible = on;

        // ── Sessions view ─────────────────────────────────────────────────
        BtnSessionStart.Click += (_, _) => ToggleSession();
        BtnSessionPause.Click += (_, _) => { if (_session.State == SessionState.Running) _session.Pause(); else _session.Resume(); };
        BtnSessionStop.Click  += (_, _) => _session.Stop();

        // ── Live view ─────────────────────────────────────────────────────
        BtnSpiral.Click   += async (_, _) => await Fire(new KeywordTriggered("spiral"));
        BtnLock.Click     += async (_, _) => await Fire(new LockScreenResult("good girls don't think", 1, 3));
        BtnVideo.Click    += async (_, _) => await Fire(new VideoCompleted("bimbodoll_trancetone.mp4"));
        BtnPresence.Click += async (_, _) => await Fire(new PresenceDetected("Discord", "chat"));
        BtnSend.Click     += async (_, _) =>
        {
            var t = TxtMsg.Text;
            if (!string.IsNullOrWhiteSpace(t)) { TxtMsg.Text = ""; await Fire(new UserMessage(t!)); }
        };

        // ── Feature detail controls ───────────────────────────────────────
        ChkFlashEnabled.IsCheckedChanged += (_, _) => ApplyFlash();
        SldFlashFreq.PropertyChanged   += (_, e) => { if (e.Property.Name == "Value") { TxtFlashFreq.Text = $"{(int)SldFlashFreq.Value}s"; ApplyFlash(); } };
        SldFlashDur.PropertyChanged    += (_, e) => { if (e.Property.Name == "Value") { TxtFlashDur.Text  = $"{(int)SldFlashDur.Value}ms"; ApplyFlash(); } };
        SldFlashAmount.PropertyChanged += (_, e) => { if (e.Property.Name == "Value") { TxtFlashAmount.Text = $"{(int)SldFlashAmount.Value}"; ApplyFlash(); } };
        BtnFlashNow.Click += (_, _) => _flash.TriggerNow();

        ChkSubEnabled.IsCheckedChanged += (_, _) => ApplySub();
        SldSubFreq.PropertyChanged += (_, e) => { if (e.Property.Name == "Value") { TxtSubFreq.Text = $"{(int)SldSubFreq.Value}s"; ApplySub(); } };
        SldSubDur.PropertyChanged  += (_, e) => { if (e.Property.Name == "Value") { TxtSubDur.Text  = $"{(int)SldSubDur.Value}ms"; ApplySub(); } };
        BtnSubNow.Click += (_, _) => _sub.TriggerNow();

        BtnSpiralToggle.Click += (_, _) => SetSpiral(!Spiral.IsVisible);
        BtnFogToggle.Click    += (_, _) => { Fog.IsVisible = !Fog.IsVisible; ChkFogEnabled.IsChecked = Fog.IsVisible; };
        BtnTriggerLock.Click  += (_, _) => Chip($"🔒 {TxtLockPhrase.Text ?? "good girls don't think"}");

        BtnSaveCompanion.Click += (_, _) =>
        {
            _settings.LettaBaseUrl = TxtLettaUrl.Text;
            _settings.LettaAgentId = TxtAgentId.Text;
            SaveAll();
            Chip("💾 companion settings saved");
        };

        LoadUiFromSettings();
        RefreshGameStats();
        UpdateRail();
    }

    // ── Navigation ────────────────────────────────────────────────────────

    private void NavigateTo(Button active, Control view, List<Button> allNav)
    {
        foreach (var btn in allNav)
            btn.Classes.Set("navActive", btn == active);

        if (_currentView != null) _currentView.IsVisible = false;
        _currentView = view;
        view.IsVisible = true;
        ViewFeatureDetail.IsVisible = false; // close any detail panel
    }

    private void OpenDetail(Control? panel)
    {
        // hide home mosaic, show detail view
        ViewHome.IsVisible = false;

        // hide all sub-panels, show requested one
        foreach (var p in new Control[]{ DetailFlash, DetailSub, DetailSpiral, DetailFog, DetailHaptics, DetailLockCard })
            p.IsVisible = false;
        if (panel != null) panel.IsVisible = true;

        ViewFeatureDetail.IsVisible = true;
    }

    private void CloseDetail()
    {
        ViewFeatureDetail.IsVisible = false;
        if (_currentView == ViewHome || _currentView == null)
            ViewHome.IsVisible = true;
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
            switch (e.State)
            {
                case SessionState.Running:
                    StatusPill.Text = "● RUNNING"; StatusPill.Foreground = new SolidColorBrush(Color.Parse("#3EE87A"));
                    TxtSessionStatus.Text = "● RUNNING";
                    BtnStart.Content = "■ STOP"; BtnSessionStart.Content = "■ Stop";
                    BtnPause.IsEnabled = true; BtnSessionPause.IsEnabled = true; BtnSessionStop.IsEnabled = true;
                    break;
                case SessionState.Paused:
                    StatusPill.Text = "⏸ PAUSED"; StatusPill.Foreground = new SolidColorBrush(Color.Parse("#FF5CA8"));
                    TxtSessionStatus.Text = "⏸ PAUSED";
                    BtnPause.Content = "▶"; BtnSessionPause.Content = "▶ Resume";
                    break;
                case SessionState.Idle:
                    StatusPill.Text = "● IDLE"; StatusPill.Foreground = new SolidColorBrush(Color.Parse("#6E6E88"));
                    TxtSessionStatus.Text = "● IDLE"; TxtSessionTimer.Text = "00:00:00";
                    BtnStart.Content = "▶ START"; BtnSessionStart.Content = "▶ Start";
                    BtnPause.Content = "⏸"; BtnSessionPause.Content = "⏸ Pause";
                    BtnPause.IsEnabled = false; BtnSessionPause.IsEnabled = false; BtnSessionStop.IsEnabled = false;
                    RefreshGameStats();
                    break;
            }
        });
    }

    private void OnSessionProgress(object? _, SessionProgressArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var t = e.Elapsed;
            var ts = $"{(int)t.TotalHours:D2}:{t.Minutes:D2}:{t.Seconds:D2}";
            SessionTimer.Text    = ts;
            TxtSessionTimer.Text = ts;
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

    // ── Settings apply ────────────────────────────────────────────────────

    private void ApplyFlash()
    {
        _settings.FlashEnabled     = ChkFlashEnabled.IsChecked ?? true;
        _settings.FlashFrequency   = (int)SldFlashFreq.Value;
        _settings.FlashDurationMs  = (int)SldFlashDur.Value;
        _settings.FlashAmount      = (int)SldFlashAmount.Value;
        _settings.FlashImagesPath  = TxtFlashPath.Text ?? _settings.FlashImagesPath;
        _flash.UpdateSettings(BuildFlashConfig());
        CardFlash.IsEnabledFeature = _settings.FlashEnabled;
        UpdateRail();
    }

    private void ApplySub()
    {
        _settings.SubliminalEnabled    = ChkSubEnabled.IsChecked ?? true;
        _settings.SubliminalFrequency  = (int)SldSubFreq.Value;
        _settings.SubliminalDurationMs = (int)SldSubDur.Value;
        if (!string.IsNullOrWhiteSpace(TxtPhrases.Text))
            _settings.SubliminalPhrases = TxtPhrases.Text!.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        _sub.UpdateSettings(BuildSubConfig());
        CardSubliminal.IsEnabledFeature = _settings.SubliminalEnabled;
        UpdateRail();
    }

    private void UpdateRail()
    {
        RailFlash.Background = new SolidColorBrush(_settings.FlashEnabled
            ? Color.Parse("#1FB85A") : Color.Parse("#2E2E4A"));
        RailSub.Background = new SolidColorBrush(_settings.SubliminalEnabled
            ? Color.Parse("#1FB85A") : Color.Parse("#2E2E4A"));
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

        TxtLettaUrl.Text           = _settings.LettaBaseUrl ?? "http://localhost:8283";
        TxtAgentId.Text            = _settings.LettaAgentId ?? "";

        // mosaic card states
        CardFlash.IsEnabledFeature     = _settings.FlashEnabled;
        CardSubliminal.IsEnabledFeature = _settings.SubliminalEnabled;
    }

    private void RefreshGameStats()
    {
        var xpNext = LevelCurve.XpForLevel(_settings.Level + 1);
        XpChip.Text          = $"Lv {_settings.Level} · {_settings.Xp} XP";
        TxtLevel.Text        = $"{_settings.Level}";
        TxtXp.Text           = $"{_settings.Xp}";
        TxtSessionCount.Text = $"{_settings.TotalSessions}";
        var tot = TimeSpan.FromMilliseconds(_settings.TotalTimeMs);
        TxtTotalTime.Text    = $"{(int)tot.TotalHours}h {tot.Minutes}m";
    }

    private void SaveAll()
    {
        _settings.LettaBaseUrl = TxtLettaUrl.Text;
        _settings.LettaAgentId = TxtAgentId.Text;
        _settings.FlashImagesPath = TxtFlashPath.Text ?? _settings.FlashImagesPath;
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
            case Say s:
                SayLog.Text = "🖤 " + s.Text + "\n" + SayLog.Text;
                break;
            case Spiral sp:
                SetSpiral(sp.On);
                break;
            case Flash f:
                DoFlashText(f.Text);
                if (f.Text.Length < 40) _flash.TriggerNow(1, 2500);
                break;
            case PinkFog pf:
                Fog.IsVisible = pf.On;
                break;
            case LockCard l:
                DoFlashText(l.Sentence);
                Chip($"🔒 {l.Sentence}");
                break;
            case Haptics h:
                Chip($"💗 haptics {(h.Intensity?.ToString() ?? h.Pattern ?? "pulse")}");
                break;
        }
        return Task.CompletedTask;
    }

    // ── Spiral / stage ────────────────────────────────────────────────────

    private void SetSpiral(bool on)
    {
        Spiral.IsVisible = on;
        CardSpiral.IsEnabledFeature = on;
        if (on) _spin.Start(); else { if (!ViewHome.IsVisible) _spin.Stop(); }
    }

    private void DoFlashText(string text)
    {
        FlashText.Text = text;
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
        _session.Dispose();
        _flash.Dispose();
        _sub.Dispose();
        foreach (var w in _flashPool) try { w.Close(); } catch { }
        try { _subOverlay.Close(); } catch { }
        base.OnClosed(e);
    }
}
