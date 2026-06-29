using ConditioningControlPanel.Core.Commands;
using ConditioningControlPanel.Core.Events;

namespace ConditioningControlPanel.Core.Agents;

/// <summary>Canned reactions for demo/tests — stands in until the Letta wire lands.</summary>
public sealed class ScriptedReactiveAgent : IReactiveAgent
{
    public bool IsAvailable => true;
    public Task<AgentReaction> ReactAsync(PanelEvent e, CancellationToken ct = default)
    {
        AgentReaction r = e switch
        {
            UserMessage      => new("there you are. eyes on me.", new PanelCommand[]{ new Spiral(true, 20) }),
            KeywordTriggered => new("good girl. deeper.", new PanelCommand[]{ new Flash("SINK"), new Haptics(0.5) }),
            LockScreenResult => new("try harder for me.", new PanelCommand[]{ new LockCard("empty and obedient") }),
            VideoCompleted   => new("that one melted you.", new PanelCommand[]{ new PinkFog(true), new Haptics(Pattern:"decay") }),
            PresenceDetected => new("back here.", new PanelCommand[]{ new Flash("FOCUS") }),
            _ => AgentReaction.Silent,
        };
        return Task.FromResult(r);
    }
}

/// <summary>An agent that's always offline (for routing tests / a downed LV).</summary>
public sealed class OfflineReactiveAgent : IReactiveAgent
{
    public bool IsAvailable => false;
    public Task<AgentReaction> ReactAsync(PanelEvent e, CancellationToken ct = default)
        => Task.FromResult(AgentReaction.Silent);
}
