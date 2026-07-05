namespace ConditioningControlPanel.Core.Agents.Letta;

/// <summary>Config for the Letta wire. Key is SUPPLIED by the host (from letta-server.env) — never hardcoded here.</summary>
/// <remarks>
/// ConversationId: the panel gets its OWN conversation on LV's agent (isolated from her
/// Discord threads; created Jul 5 2026 as "CCP Panel — Vesper control surface"). "default"
/// also works — the API then requires agent_id in the body — but a dedicated conversation
/// keeps panel traffic out of her main context.
/// </remarks>
public sealed record LettaConfig(string AgentId, string ApiKey, string BaseUrl = "https://api.letta.com", string ConversationId = "default")
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(AgentId) && !string.IsNullOrWhiteSpace(ApiKey);
}
