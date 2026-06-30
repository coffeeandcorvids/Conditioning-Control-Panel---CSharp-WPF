using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Media;
using ConditioningControlPanel.Core.Agents;
using ConditioningControlPanel.Core.Commands;
using ConditioningControlPanel.Core.Events;

namespace ConditioningControlPanel.Shell;

public partial class MainWindow : Window, ICommandSink
{
    private readonly IReactiveAgent _agent = new ScriptedReactiveAgent(); // swap → LettaReactiveAgent once wired
    private readonly ReactionExecutor _executor;
    private double _angle;
    private readonly DispatcherTimer _spin = new() { Interval = TimeSpan.FromMilliseconds(33) };

    public MainWindow()
    {
        InitializeComponent();
        _executor = new ReactionExecutor(this);
        Spiral.RenderTransformOrigin = Avalonia.RelativePoint.Center;
        _spin.Tick += (_, _) => { _angle = (_angle + 4) % 360; Spiral.RenderTransform = new RotateTransform(_angle); };

        BtnSpiral.Click   += async (_, _) => await Fire(new KeywordTriggered("spiral"));
        BtnLock.Click     += async (_, _) => await Fire(new LockScreenResult("good girls dont think", 1, 3));
        BtnVideo.Click    += async (_, _) => await Fire(new VideoCompleted("bimbodoll_trancetone.mp4"));
        BtnPresence.Click += async (_, _) => await Fire(new PresenceDetected("Discord", "chat"));
        BtnSend.Click     += async (_, _) =>
        {
            var t = TxtMsg.Text;
            if (!string.IsNullOrWhiteSpace(t)) { TxtMsg.Text = ""; await Fire(new UserMessage(t!)); }
        };
    }

    private async Task Fire(PanelEvent e)
    {
        var reaction = await _agent.ReactAsync(e);
        await _executor.ExecuteAsync(reaction);
    }

    // ICommandSink — the panel performing what the agent returned (this IS "Vesper drives the room")
    public Task ExecuteAsync(PanelCommand command, CancellationToken ct = default)
    {
        switch (command)
        {
            case Say s:      SayLog.Text = "🖤 " + s.Text + "\n" + SayLog.Text; break;
            case Spiral sp:  SetSpiral(sp.On); break;
            case Flash f:    DoFlash(f.Text); break;
            case PinkFog pf: Fog.IsVisible = pf.On; break;
            case LockCard l: Chip($"🔒 lock card: {l.Sentence}"); break;
            case Haptics h:  Chip($"💗 haptics {(h.Intensity?.ToString() ?? h.Pattern ?? "pulse")}"); break;
        }
        return Task.CompletedTask;
    }

    private void SetSpiral(bool on)
    {
        Spiral.IsVisible = on;
        if (on) _spin.Start(); else _spin.Stop();
    }

    private void DoFlash(string text)
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
}
