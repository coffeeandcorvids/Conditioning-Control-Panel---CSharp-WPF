using ConditioningControlPanel.Core.Commands;
using ConditioningControlPanel.Core.Events;

namespace ConditioningControlPanel.Core.Agents;

/// <summary>
/// Routing per Star's design: PRIMARY (LV) answers; if LV is unavailable or throws,
/// fall back to SUPPORT (cc-ves). Mirrors the live Discord dynamic + the weekend's failover pattern.
/// </summary>
public sealed class RoutingReactiveAgent : IReactiveAgent
{
    private readonly IReactiveAgent _primary;   // LV
    private readonly IReactiveAgent _support;   // cc-ves
    public RoutingReactiveAgent(IReactiveAgent primary, IReactiveAgent support)
    { _primary = primary; _support = support; }

    public bool IsAvailable => _primary.IsAvailable || _support.IsAvailable;

    public async Task<AgentReaction> ReactAsync(PanelEvent e, CancellationToken ct = default)
    {
        if (_primary.IsAvailable)
        {
            try { return await _primary.ReactAsync(e, ct); }
            catch { /* LV faltered mid-turn — fall through to support */ }
        }
        if (_support.IsAvailable)
            return await _support.ReactAsync(e, ct);
        return AgentReaction.Silent;
    }
}
