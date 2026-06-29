using ConditioningControlPanel.Core.Ai;
using ConditioningControlPanel.Core.Events;
using Xunit;

file sealed class RecordingBackend : IAiBackend
{
    public string? LastCall;
    public bool IsAvailable => true;
    public int DailyRequestsRemaining => 99;
    public Task<string> GetReplyAsync(string i, bool u = false) { LastCall = "reply"; return Task.FromResult("hi"); }
    public Task<AiReplyResult> GetReplyExAsync(string i, bool u = false) { LastCall = "replyex"; return Task.FromResult(new AiReplyResult("hi", true)); }
    public Task<string?> GetAwarenessReactionAsync(string n, string c, string s = "", string p = "") { LastCall = "awareness"; return Task.FromResult<string?>("seen"); }
    public Task<string?> GetStillOnReactionAsync(string d, string c, TimeSpan t) { LastCall = "stillon"; return Task.FromResult<string?>("still"); }
    public Task<string?> GetKeywordCommentAsync(string k, string? t = null) { LastCall = "keyword"; return Task.FromResult<string?>("kw"); }
    public Task<string?> GetLockScreenReactionAsync(string s, int m, int a, string? t = null) { LastCall = "lock"; return Task.FromResult<string?>("lock"); }
    public Task<string?> GetVideoDoneReactionAsync(string title, string? t = null) { LastCall = "video"; return Task.FromResult<string?>("vid"); }
}

public class EventRouterTests
{
    [Theory]
    [InlineData("keyword")]
    [InlineData("lock")]
    [InlineData("video")]
    [InlineData("awareness")]
    [InlineData("stillon")]
    public async Task Routes_each_event_to_the_matching_agent_call(string expected)
    {
        var fake = new RecordingBackend();
        var router = new EventRouter(fake);
        PanelEvent e = expected switch
        {
            "keyword"   => new KeywordTriggered("spiral"),
            "lock"      => new LockScreenResult("obey", 0, 3),
            "video"     => new VideoCompleted("trance.mp4"),
            "awareness" => new PresenceDetected("Discord", "chat"),
            _           => new StillOn("YouTube", "video", TimeSpan.FromMinutes(5)),
        };
        await router.DispatchAsync(e);
        Assert.Equal(expected, fake.LastCall);
    }

    [Fact]
    public async Task Silent_agent_returns_null()
    {
        var router = new EventRouter(new VesperRouterBackend()); // stub: unwired => silent
        Assert.Null(await router.DispatchAsync(new KeywordTriggered("spiral")));
    }
}
