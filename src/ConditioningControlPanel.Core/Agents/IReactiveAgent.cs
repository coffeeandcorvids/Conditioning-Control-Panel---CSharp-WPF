using ConditioningControlPanel.Core.Commands;
using ConditioningControlPanel.Core.Events;

namespace ConditioningControlPanel.Core.Agents;

/// <summary>
/// The agent shape the driven-panel model wants: a PanelEvent in, an AgentReaction out
/// (say + commands). The Letta wire implements this; ScriptedReactiveAgent fakes it for demo/tests.
/// (Cleaner than the text-only IAiBackend lifted from upstream's chat model — this carries commands.)
/// </summary>
public interface IReactiveAgent
{
    bool IsAvailable { get; }
    Task<AgentReaction> ReactAsync(PanelEvent e, CancellationToken ct = default);
}
