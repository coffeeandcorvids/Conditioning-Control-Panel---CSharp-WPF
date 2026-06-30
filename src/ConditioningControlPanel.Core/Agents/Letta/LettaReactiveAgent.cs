using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ConditioningControlPanel.Core.Commands;
using ConditioningControlPanel.Core.Events;

namespace ConditioningControlPanel.Core.Agents.Letta;

/// <summary>
/// The live wire to LV: POSTs a panel event (as a prompt) to LV's Letta agent and parses her reply
/// into an AgentReaction. Same agent as Discord-LV = one continuous mind (Star + LV agreed, Jun 29).
/// HttpClient is injected so it's unit-testable with a fake handler (no live creds in tests).
/// NOTE: Letta message request/response shape below is the documented form; verify on first live call.
/// </summary>
public sealed class LettaReactiveAgent : IReactiveAgent
{
    private readonly HttpClient _http;
    private readonly LettaConfig _cfg;
    public LettaReactiveAgent(HttpClient http, LettaConfig cfg) { _http = http; _cfg = cfg; }

    public bool IsAvailable => _cfg.IsConfigured;

    public async Task<AgentReaction> ReactAsync(PanelEvent e, CancellationToken ct = default)
    {
        if (!IsAvailable) return AgentReaction.Silent;

        var prompt = PanelEventPrompt.Build(e);
        var body = JsonSerializer.Serialize(new
        {
            messages = new[] { new { role = "user", content = prompt } }
        });

        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"{_cfg.BaseUrl}/v1/agents/{_cfg.AgentId}/messages")
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cfg.ApiKey);

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return AgentReaction.Silent; // surfaced/logged by caller
        var json = await resp.Content.ReadAsStringAsync(ct);

        var assistantText = ExtractAssistantText(json);
        return ReactionParser.Parse(assistantText);
    }

    /// <summary>Pull the assistant's text out of a Letta /messages response (tolerant of shape).</summary>
    public static string? ExtractAssistantText(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;
            // messages may be top-level array or under "messages"
            JsonElement msgs = root.ValueKind == JsonValueKind.Array ? root
                : root.TryGetProperty("messages", out var m) ? m : default;
            if (msgs.ValueKind != JsonValueKind.Array) return null;
            string? last = null;
            foreach (var msg in msgs.EnumerateArray())
            {
                var type = msg.TryGetProperty("message_type", out var mt) ? mt.GetString() : null;
                if (type is "assistant_message" or "assistant")
                {
                    if (msg.TryGetProperty("content", out var c))
                        last = c.ValueKind == JsonValueKind.String ? c.GetString() : c.ToString();
                    else if (msg.TryGetProperty("text", out var t))
                        last = t.GetString();
                }
            }
            return last;
        }
        catch (JsonException) { return null; }
    }
}
