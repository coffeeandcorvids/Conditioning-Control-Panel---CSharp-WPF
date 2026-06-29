namespace ConditioningControlPanel.Core.Commands;

/// <summary>
/// The seam the host (WPF today / Avalonia later) implements to actually PERFORM commands —
/// wiring each to the real effect service (Spirals, Flash, fog, LockCard, LovenseProvider).
/// Core stays UI-agnostic; tests use a recording fake.
/// </summary>
public interface ICommandSink
{
    Task ExecuteAsync(PanelCommand command, CancellationToken ct = default);
}

/// <summary>Runs an AgentReaction: speaks (if any) then performs each command in order.</summary>
public sealed class ReactionExecutor
{
    private readonly ICommandSink _sink;
    public ReactionExecutor(ICommandSink sink) => _sink = sink;

    public async Task ExecuteAsync(AgentReaction reaction, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(reaction.Say))
            await _sink.ExecuteAsync(new Say(reaction.Say!), ct);
        foreach (var c in reaction.Commands)
            await _sink.ExecuteAsync(c, ct);
    }
}
