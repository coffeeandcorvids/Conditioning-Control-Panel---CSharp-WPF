namespace ConditioningControlPanel.Core.Commands;

/// <summary>What an agent returns for an event: optional spoken line + zero-or-more drive commands.</summary>
public sealed record AgentReaction(string? Say, IReadOnlyList<PanelCommand> Commands)
{
    public static readonly AgentReaction Silent = new(null, Array.Empty<PanelCommand>());
}
