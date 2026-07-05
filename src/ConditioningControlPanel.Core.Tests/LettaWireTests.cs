using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Agents.Letta;
using ConditioningControlPanel.Core.Commands;
using ConditioningControlPanel.Core.Events;
using Xunit;

sealed class SseHandler : HttpMessageHandler
{
    private readonly string _sse; private readonly HttpStatusCode _code;
    public string? LastRequestBody; public string? LastUri;
    public SseHandler(string sse, HttpStatusCode code = HttpStatusCode.OK) { _sse = sse; _code = code; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        LastUri = req.RequestUri?.ToString();
        LastRequestBody = req.Content is null ? null : await req.Content.ReadAsStringAsync(ct);
        return new HttpResponseMessage(_code) { Content = new StringContent(_sse, Encoding.UTF8, "text/event-stream") };
    }
}

public class LettaWireTests
{
    const string Sse = """
data: {"message_type":"reasoning_message","reasoning":"thinking"}

data: {"message_type":"assistant_message","content":"{\"say\":\"good girl\",\"commands\":[{\"op\":\"spiral\",\"on\":true,\"secs\":30}]}"}

data: {"message_type":"stop_reason","stop_reason":"end_turn"}

""";

    [Fact]
    public void Unconfigured_is_unavailable()
        => Assert.False(new LettaReactiveAgent(new HttpClient(), new LettaConfig("", "")).IsAvailable);

    [Fact]
    public async Task Streams_LV_reply_into_reaction_and_hits_stream_endpoint()
    {
        var h = new SseHandler(Sse);
        var agent = new LettaReactiveAgent(new HttpClient(h), new LettaConfig("agent-x", "key-x"));
        var r = await agent.ReactAsync(new KeywordTriggered("spiral"));
        Assert.Equal("good girl", r.Say);
        Assert.Equal(new Spiral(true, 30), Assert.Single(r.Commands));
        // proven route (Jul 5 live smoke): the messages POST itself is the SSE stream —
        // no "/stream" suffix, and the body carries streaming:true
        Assert.EndsWith("/v1/conversations/default/messages", h.LastUri);
        Assert.Contains("\"streaming\":true", h.LastRequestBody);
        Assert.Contains("keyword trigger fired", h.LastRequestBody);
        Assert.Contains("include_pings", h.LastRequestBody);
    }

    [Fact]
    public async Task Dedicated_conversation_id_is_used_when_configured()
    {
        var h = new SseHandler(Sse);
        var agent = new LettaReactiveAgent(new HttpClient(h),
            new LettaConfig("agent-x", "key-x", ConversationId: "conv-panel-123"));
        _ = await agent.ReactAsync(new KeywordTriggered("spiral"));
        Assert.EndsWith("/v1/conversations/conv-panel-123/messages", h.LastUri);
    }

    [Fact]
    public async Task Http_failure_is_silent()
    {
        var h = new SseHandler("boom", HttpStatusCode.InternalServerError);
        var agent = new LettaReactiveAgent(new HttpClient(h), new LettaConfig("agent-x", "key-x"));
        Assert.Same(AgentReaction.Silent, await agent.ReactAsync(new VideoCompleted("x.mp4")));
    }

    [Fact]
    public async Task Accumulate_ignores_pings_and_stops_on_stop_reason()
    {
        var sse = "data: {\"message_type\":\"ping\"}\n\ndata: {\"message_type\":\"assistant_message\",\"content\":\"hello\"}\n\ndata: {\"message_type\":\"stop_reason\"}\n\ndata: {\"message_type\":\"assistant_message\",\"content\":\"AFTER-STOP\"}\n\n";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(sse));
        var text = await LettaReactiveAgent.AccumulateAssistant(ms);
        Assert.Equal("hello", text);   // stopped at stop_reason, ignored ping + post-stop content
    }
}
