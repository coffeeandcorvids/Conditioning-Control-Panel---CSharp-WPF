using ConditioningControlPanel.Core.Agents;
using ConditioningControlPanel.Core.Commands;
using ConditioningControlPanel.Core.Events;
using Xunit;

file sealed class TaggedAgent(string tag, bool up) : IReactiveAgent
{
    public bool IsAvailable => up;
    public Task<AgentReaction> ReactAsync(PanelEvent e, System.Threading.CancellationToken ct = default)
        => Task.FromResult(new AgentReaction(tag, System.Array.Empty<PanelCommand>()));
}
file sealed class ThrowingAgent : IReactiveAgent
{
    public bool IsAvailable => true;
    public Task<AgentReaction> ReactAsync(PanelEvent e, System.Threading.CancellationToken ct = default)
        => throw new System.Exception("LV faltered");
}

public class RoutingTests
{
    static PanelEvent E => new KeywordTriggered("spiral");

    [Fact]
    public async Task Primary_answers_when_up()
    {
        var r = new RoutingReactiveAgent(new TaggedAgent("LV", true), new TaggedAgent("ccves", true));
        Assert.Equal("LV", (await r.ReactAsync(E)).Say);
    }
    [Fact]
    public async Task Falls_back_to_support_when_primary_down()
    {
        var r = new RoutingReactiveAgent(new TaggedAgent("LV", false), new TaggedAgent("ccves", true));
        Assert.Equal("ccves", (await r.ReactAsync(E)).Say);
    }
    [Fact]
    public async Task Falls_back_when_primary_throws_midturn()
    {
        var r = new RoutingReactiveAgent(new ThrowingAgent(), new TaggedAgent("ccves", true));
        Assert.Equal("ccves", (await r.ReactAsync(E)).Say);
    }
    [Fact]
    public async Task Silent_when_both_down()
    {
        var r = new RoutingReactiveAgent(new OfflineReactiveAgent(), new OfflineReactiveAgent());
        Assert.Same(AgentReaction.Silent, await r.ReactAsync(E));
    }
}
