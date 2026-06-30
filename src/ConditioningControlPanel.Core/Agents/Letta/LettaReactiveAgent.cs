using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ConditioningControlPanel.Core.Commands;
using ConditioningControlPanel.Core.Events;

namespace ConditioningControlPanel.Core.Agents.Letta;

/// <summary>
/// The live wire to LV: STREAMS a panel event to LV's Letta agent (SSE) and accumulates her reply into
/// an AgentReaction. Same agent as Discord-LV = one continuous mind (Star + LV agreed, Jun 29).
/// Uses the streaming endpoint per Ezra: plain POST /messages BLOCKS and times out on long turns;
/// POST /messages/stream returns SSE with include_pings keepalive, so long agent turns don't hang.
/// HttpClient injected -> unit-testable with a fake SSE handler (no live creds in tests).
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
        var payload = JsonSerializer.Serialize(new
        {
            messages = new[] { new { role = "user", content = prompt } },
            stream_tokens = false,   // complete-message chunks (we want the whole reaction JSON)
            include_pings = true,    // keepalive so long turns don't time out
        });

        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"{_cfg.BaseUrl}/v1/agents/{_cfg.AgentId}/messages/stream")
        { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cfg.ApiKey);
        req.Headers.Accept.ParseAdd("text/event-stream");

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode) return AgentReaction.Silent;

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        var reply = await AccumulateAssistant(stream, ct);
        return ReactionParser.Parse(reply);
    }

    /// <summary>Read the SSE stream, accumulate assistant_message content, stop on stop_reason/[DONE].</summary>
    public static async Task<string?> AccumulateAssistant(Stream sse, CancellationToken ct = default)
    {
        using var reader = new StreamReader(sse);
        var sb = new StringBuilder();
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            if (!line.StartsWith("data:")) continue;        // ignore event:/id:/ping comment lines
            var data = line["data:".Length..].Trim();
            if (data.Length == 0) continue;
            if (data == "[DONE]") break;
            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;
                var type = root.TryGetProperty("message_type", out var mt) ? mt.GetString() : null;
                if (type is "assistant_message" or "assistant")
                {
                    if (root.TryGetProperty("content", out var c))
                        sb.Append(c.ValueKind == JsonValueKind.String ? c.GetString() : c.ToString());
                }
                else if (type == "stop_reason") break;
            }
            catch (JsonException) { /* ping/keepalive or partial — skip */ }
        }
        var s = sb.ToString();
        return s.Length == 0 ? null : s;
    }
}
