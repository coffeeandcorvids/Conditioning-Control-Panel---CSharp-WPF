using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Agents.Letta;
using ConditioningControlPanel.Core.Commands;
using ConditioningControlPanel.Core.Events;
using Xunit;

sealed class CannedHandler : HttpMessageHandler
{
    private readonly string _resp; private readonly HttpStatusCode _code;
    public string? LastRequestBody;
    public CannedHandler(string resp, HttpStatusCode code = HttpStatusCode.OK) { _resp = resp; _code = code; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        LastRequestBody = req.Content is null ? null : await req.Content.ReadAsStringAsync(ct);
        return new HttpResponseMessage(_code) { Content = new StringContent(_resp) };
    }
}

public class LettaWireTests
{
    [Fact]
    public void Unconfigured_is_unavailable()
        => Assert.False(new LettaReactiveAgent(new HttpClient(), new LettaConfig("", "")).IsAvailable);

    [Fact]
    public async Task Parses_LV_reply_into_reaction_and_posts_the_event()
    {
        var letta = """
        {"messages":[
          {"message_type":"reasoning_message","reasoning":"thinking"},
          {"message_type":"assistant_message","content":"{\"say\":\"good girl\",\"commands\":[{\"op\":\"spiral\",\"on\":true,\"secs\":30}]}"}
        ]}
        """;
        var h = new CannedHandler(letta);
        var agent = new LettaReactiveAgent(new HttpClient(h), new LettaConfig("agent-x", "key-x"));
        var r = await agent.ReactAsync(new KeywordTriggered("spiral"));
        Assert.Equal("good girl", r.Say);
        Assert.Equal(new Spiral(true, 30), Assert.Single(r.Commands));
        Assert.Contains("keyword trigger fired", h.LastRequestBody);
    }

    [Fact]
    public async Task Http_failure_is_silent_not_crash()
    {
        var h = new CannedHandler("boom", HttpStatusCode.InternalServerError);
        var agent = new LettaReactiveAgent(new HttpClient(h), new LettaConfig("agent-x", "key-x"));
        Assert.Same(AgentReaction.Silent, await agent.ReactAsync(new VideoCompleted("x.mp4")));
    }

    [Fact]
    public void ExtractAssistantText_pulls_last_assistant_content()
        => Assert.Equal("hello",
            LettaReactiveAgent.ExtractAssistantText("""{"messages":[{"message_type":"assistant_message","content":"hello"}]}"""));
}
