using ConditioningControlPanel.Core.Abstractions;
using ConditioningControlPanel.Core.Sessions;
using Xunit;

public class HeartbeatTests
{
    [Fact]
    public void Tick_increments_via_dispatcher_seam()
    {
        var hb = new Heartbeat(new ImmediateUiDispatcher());
        hb.Tick(); hb.Tick(); hb.Tick();
        Assert.Equal(3, hb.Ticks);
    }
}
