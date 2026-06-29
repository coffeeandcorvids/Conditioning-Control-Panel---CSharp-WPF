using ConditioningControlPanel.Core.Abstractions;

namespace ConditioningControlPanel.Core.Sessions;

/// <summary>Toy portable service: a tick counter dispatched via the seam (no WPF anywhere).</summary>
public sealed class Heartbeat
{
    private readonly IUiDispatcher _ui;
    public int Ticks { get; private set; }
    public Heartbeat(IUiDispatcher ui) => _ui = ui;
    public void Tick() => _ui.Post(() => Ticks++);
}
