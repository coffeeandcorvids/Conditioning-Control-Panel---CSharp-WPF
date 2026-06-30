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

    // ── Animations ───────────────────────────────────────────────────────
    private double _angle;
    private readonly DispatcherTimer _spin = new() { Interval = TimeSpan.FromMilliseconds(33) };

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

        // logo spiral spins continuously on dashboard
        LogoSpiral.RenderTransformOrigin = Avalonia.RelativePoint.Center;
        Spiral.RenderTransformOrigin     = Avalonia.RelativePoint.Center;
        _spin.Tick += (_, _) =>
        {
            _angle = (_angle + 3) % 360;
            LogoSpiral.RenderTransform = new RotateTransform(_angle);
            Spiral.RenderTransform     = new RotateTransform(_angle);
        };
        _spin.Start();

        // ── Custom window chrome (SystemDecorations=None) ─────────────
        BtnMinimize.Click += (_, _) => WindowState = WindowState.Minimized;
        BtnClose.Click    += (_, _) => Close();

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
            [NavQuests]       = ViewQuests,
            [NavEnhancements] = ViewEnhancements,
            [NavDeeper]       = ViewDeeper,
            [NavAssets]       = ViewAssets,
        };
        foreach (var (btn, view) in primaryViews)
            btn.Click += (_, _) => SwitchPrimary(btn, view, primaryViews.Keys.ToList());

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
        CardFlash.CardClicked      += (_, _) => ShowDetail("⚡ Flash Images",      "Configure flash image settings on the right ↓");
        CardSubliminal.CardClicked += (_, _) => ShowDetail("💬 Subliminals",       "Configure subliminal text settings on the right ↓");
        CardSpiral.CardClicked     += (_, _) => { SetSpiral(!Spiral.IsVisible); Chip(Spiral.IsVisible ? "🌀 spiral ON" : "🌀 spiral OFF"); };
        CardPinkFog.CardClicked    += (_, _) => { Fog.IsVisible = !Fog.IsVisible; Chip(Fog.IsVisible ? "🌸 fog ON" : "🌸 fog OFF"); };
        CardHaptics.CardClicked    += (_, _) => ShowDetail("💗 Haptics",           "Buttplug.io / Lovense — wiring next");
        CardLockCard.CardClicked   += (_, _) => ShowDetail("🔒 Lock Card",         "Trigger a lock card phrase");
        CardVideo.CardClicked      += (_, _) => Chip("🎬 Video — LibVLCSharp.Avalonia porting next");
        CardVisuals.CardClicked    += (_, _) => ShowDetail("👁 Visuals",           "Visual overlay settings");
        CardMindWipe.CardClicked   += (_, _) => Chip("🧠 Mind Wipe — blur overlay coming next");
        CardBubblePop.CardClicked  += (_, _) => Chip("🫧 Bubble Pop — minigame coming next");
        CardBouncingText.CardClicked += (_, _) => Chip("✨ Bouncing Text — coming next");
        CardSystem.CardClicked     += (_, _) => ShowDetail("⚙ System",            "System settings");

        // mosaic card toggles
        CardFlash.ToggleChanged     += (_, on) => { _settings.FlashEnabled = on; _flash.UpdateSettings(BuildFlashConfig()); };
        CardSubliminal.ToggleChanged += (_, on) => { _settings.SubliminalEnabled = on; _sub.UpdateSettings(BuildSubConfig()); };
        CardSpiral.ToggleChanged    += (_, on) => SetSpiral(on);
        CardPinkFog.ToggleChanged   += (_, on) => Fog.IsVisible = on;

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
        BtnFogToggle.Click    += (_, _) => Fog.IsVisible = !Fog.IsVisible;
        BtnTriggerLock.Click  += (_, _) => { var ph = TxtLockPhrase.Text ?? "good girls don't think"; DoFlashText(ph); Chip($"🔒 {ph}"); };

        BtnSaveCompanion.Click += (_, _) =>
        {
            _settings.LettaBaseUrl = TxtLettaUrl.Text;
            _settings.LettaAgentId = TxtAgentId.Text;
            SaveAll(); Chip("💾 companion settings saved");
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
        RefreshGameStats();
    }

    // ── Navigation ────────────────────────────────────────────────────────

    private Control? _activePrimary;
    private Control? _activeSecondary;

    private void SwitchPrimary(Button active, Control view, List<Button> allPrimary)
    {
        // clear secondary selection
        foreach (var btn in new[]{ Nav2Achievements, Nav2Leaderboard, Nav2Companion, Nav2Profile, Nav2Lab, Nav2Live })
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
        foreach (var btn in new[]{ Nav2Achievements, Nav2Leaderboard, Nav2Companion, Nav2Profile, Nav2Lab, Nav2Live })
            btn.Classes.Set("navPillActive", btn == active);
        view.IsVisible = true;
        _activeSecondary = view;
    }

    private void HideAllViews()
    {
        foreach (var v in new Control[]{ ViewDashboard, ViewPresets, ViewQuests, ViewEnhancements,
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

        TxtLettaUrl.Text           = _settings.LettaBaseUrl ?? "http://localhost:8283";
        TxtAgentId.Text            = _settings.LettaAgentId ?? "";

        CardFlash.IsEnabledFeature      = _settings.FlashEnabled;
        CardSubliminal.IsEnabledFeature = _settings.SubliminalEnabled;
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
            case Spiral sp:  SetSpiral(sp.On); break;
            case Flash f:    DoFlashText(f.Text); if (f.Text.Length < 40) _flash.TriggerNow(1, 2500); break;
            case PinkFog pf: Fog.IsVisible = pf.On; break;
            case LockCard l: DoFlashText(l.Sentence); Chip($"🔒 {l.Sentence}"); break;
            case Haptics h:  Chip($"💗 haptics {(h.Intensity?.ToString() ?? h.Pattern ?? "pulse")}"); break;
        }
        return Task.CompletedTask;
    }

    private void SetSpiral(bool on)
    {
        Spiral.IsVisible            = on;
        CardSpiral.IsEnabledFeature = on;
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
        _session.Dispose();
        _flash.Dispose();
        _sub.Dispose();
        foreach (var w in _flashPool) try { w.Close(); } catch { }
        try { _subOverlay.Close(); } catch { }
        base.OnClosed(e);
    }
}
