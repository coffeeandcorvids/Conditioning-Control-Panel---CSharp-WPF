using ConditioningControlPanel.Core.Ai;
using Xunit;

public class VesperRouterTests
{
    [Fact]
    public async Task Router_stub_satisfies_contract_and_reports_unwired()
    {
        IAiBackend backend = new VesperRouterBackend();
        Assert.False(backend.IsAvailable);                       // not wired yet, honest
        var r = await backend.GetReplyExAsync("hi");
        Assert.False(r.IsAiGenerated);                            // fallback path, not fake-AI
        Assert.Null(await backend.GetKeywordCommentAsync("spiral"));
    }
}
