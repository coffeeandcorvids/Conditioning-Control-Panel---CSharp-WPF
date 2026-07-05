using System.Text.Json;
using ConditioningControlPanel.Core.Models;
using Xunit;

public class TimelineTests
{
    static TimelineSession BuildSession()
    {
        var s = new TimelineSession { Name = "Test Descent", DurationMinutes = 30 };
        var spiral = s.AddStartEvent("spiral", 0);
        s.AddStopEvent(spiral, 10);
        s.AddStartEvent("pink_filter", 5);
        return s;
    }

    [Fact]
    public void Start_stop_events_pair_and_query()
    {
        var s = BuildSession();
        Assert.True(s.HasFeature("spiral"));
        Assert.True(s.HasFeature("pink_filter"));
        var start = Assert.Single(s.GetStartEvents("spiral"));
        var stop = s.GetPairedStopEvent(start);
        Assert.NotNull(stop);
        Assert.Equal(10, stop!.Minute);
        Assert.Equal(10, s.GetLastSegmentEndMinute("spiral"));
    }

    [Fact]
    public void Xp_and_difficulty_compute()
    {
        var s = BuildSession();
        Assert.True(s.CalculateXP() > 0);
        _ = s.CalculateDifficulty(); // must not throw with partial feature set
    }

    [Fact]
    public void Session_round_trips_through_json()
    {
        var s = BuildSession();
        var json = JsonSerializer.Serialize(s);
        var back = JsonSerializer.Deserialize<TimelineSession>(json);
        Assert.NotNull(back);
        Assert.Equal(s.Name, back!.Name);
        Assert.Equal(s.Events.Count, back.Events.Count);
        Assert.Equal(s.GetLastSegmentEndMinute("spiral"), back.GetLastSegmentEndMinute("spiral"));
    }

    [Fact]
    public void RemoveEvent_drops_paired_stop()
    {
        var s = BuildSession();
        var start = Assert.Single(s.GetStartEvents("spiral"));
        s.RemoveEvent(start);
        Assert.False(s.HasFeature("spiral"));
    }
}
