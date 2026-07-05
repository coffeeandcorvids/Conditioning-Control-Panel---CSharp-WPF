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
///
/// SOLVED Jul 5 2026 (route pulled from the listener bundle, live-proven against LV):
/// the send endpoint IS the stream — POST /v1/conversations/{id}/messages with
/// {"streaming":true} answers in SSE. The Jun 29 "structural hang" was a blocking client
/// waiting for a JSON body on an SSE response, plus a stray "/stream" suffix (404).
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
            messages = new[] { new { type = "message", role = "user", content = prompt } },
            streaming = true,          // THE fix: the POST itself answers in SSE
            stream_tokens = false,     // complete-message chunks (we want the whole reaction JSON)
            include_pings = true,      // keepalive so long turns don't time out
            background = true,         // listener parity (proven shape, Jul 5 live smoke)
            // "default" conversation requires agent_id in the body; dedicated ones don't (harmless either way)
            agent_id = _cfg.AgentId,
        });

        // Content-Type must be EXACTLY "application/json" — StringContent's default
        // "; charset=utf-8" suffix gets this streaming POST tarpitted by the API's WAF
        // (held forever, no error). That one parameter was the entire Jun 29 mystery.
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"{_cfg.BaseUrl}/v1/conversations/{_cfg.ConversationId}/messages")
        { Content = content };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cfg.ApiKey);
        req.Headers.Accept.ParseAdd("text/event-stream");

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode) return AgentReaction.Silent;

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        var reply = await AccumulateAssistant(stream, ct);
        return ReactionParser.ParseLoose(reply);
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
