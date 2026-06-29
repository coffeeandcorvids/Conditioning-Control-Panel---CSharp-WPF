namespace ConditioningControlPanel.Core.Ai;

/// <summary>Source of a moderation refusal (mirrors WPF ModerationRefusalInfo, portable subset).</summary>
public enum RefusalSource { None, Input, Output }

public sealed record ModerationRefusalInfo(RefusalSource Source, string? Message = null);

/// <summary>
/// Typed AI reply. IsAiGenerated=false for any fallback/login-required/circuit-broken path;
/// Refusal populated when a moderation guard blocks. (Lifted from WPF AiReplyResult.)
/// </summary>
public sealed record AiReplyResult(string Text, bool IsAiGenerated, ModerationRefusalInfo? Refusal = null);
