using ConditioningControlPanel.Core.Ai;

namespace ConditioningControlPanel.Core.Events;

/// <summary>
/// The functioning heart of the shell: takes a PanelEvent, routes it to the right agent call on an
/// IAiBackend, and returns the agent's reaction (null = agent chose to stay silent). Transport-agnostic
/// — works against the VesperRouterBackend stub today, the real Letta wire tomorrow. No WPF, no UI.
/// </summary>
public sealed class EventRouter
{
    private readonly IAiBackend _agent;
    public EventRouter(IAiBackend agent) => _agent = agent;

    /// <summary>Route one event → agent reaction text (or null if silent/unavailable).</summary>
    public Task<string?> DispatchAsync(PanelEvent e) => e switch
    {
        UserMessage m        => NonNull(_agent.GetReplyAsync(m.Text, isUserMessage: true)),
        KeywordTriggered k   => _agent.GetKeywordCommentAsync(k.Keyword, k.PromptTemplate),
        LockScreenResult l   => _agent.GetLockScreenReactionAsync(l.Sentence, l.Mistakes, l.Amount),
        VideoCompleted v     => _agent.GetVideoDoneReactionAsync(v.Title),
        PresenceDetected p   => _agent.GetAwarenessReactionAsync(p.DetectedName, p.Category, p.ServiceName, p.PageTitle),
        StillOn s            => _agent.GetStillOnReactionAsync(s.DisplayName, s.Category, s.Duration),
        _ => Task.FromResult<string?>(null),
    };

    private static async Task<string?> NonNull(Task<string> t)
    {
        var s = await t;
        return string.IsNullOrEmpty(s) ? null : s;
    }
}
