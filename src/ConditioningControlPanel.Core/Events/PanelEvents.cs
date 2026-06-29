namespace ConditioningControlPanel.Core.Events;

/// <summary>
/// The events the lightweight shell forwards to the Vesper agents (LV controller / cc-ves support).
/// These are the panel→agent signals; the agents decide how to react. Portable, transport-agnostic —
/// the wire (how these reach Letta) is the open design; the SHAPE of what's sent is defined here.
/// Mirrors the reaction surface in IAiBackend so every event has a home.
/// </summary>
public abstract record PanelEvent(DateTimeOffset At)
{
    protected PanelEvent() : this(DateTimeOffset.UtcNow) { }
}

/// <summary>User typed/spoke to the panel chat.</summary>
public sealed record UserMessage(string Text) : PanelEvent;

/// <summary>A watched keyword/trigger fired (→ GetKeywordCommentAsync).</summary>
public sealed record KeywordTriggered(string Keyword, string? PromptTemplate = null) : PanelEvent;

/// <summary>Lock-card screen completed (→ GetLockScreenReactionAsync).</summary>
public sealed record LockScreenResult(string Sentence, int Mistakes, int Amount) : PanelEvent;

/// <summary>A video/media item finished (→ GetVideoDoneReactionAsync).</summary>
public sealed record VideoCompleted(string Title) : PanelEvent;

/// <summary>Awareness/attention detector saw a named app/site (→ GetAwarenessReactionAsync).</summary>
public sealed record PresenceDetected(string DetectedName, string Category, string ServiceName = "", string PageTitle = "") : PanelEvent;

/// <summary>Subject is still on the same thing after a while (→ GetStillOnReactionAsync).</summary>
public sealed record StillOn(string DisplayName, string Category, TimeSpan Duration) : PanelEvent;
