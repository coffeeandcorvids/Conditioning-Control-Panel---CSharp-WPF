namespace ConditioningControlPanel.Core.Agents.Letta;

/// <summary>Config for the Letta wire. Key is SUPPLIED by the host (from letta-server.env) — never hardcoded here.</summary>
public sealed record LettaConfig(string AgentId, string ApiKey, string BaseUrl = "https://api.letta.com")
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(AgentId) && !string.IsNullOrWhiteSpace(ApiKey);
}
