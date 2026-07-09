using System.IO;
using System.Linq;
using ConditioningControlPanel.Core.Services.Haptics;
using Xunit;

/// <summary>
/// BambiCloud haptics cue-track parsing against a captured CDN fixture
/// (Fixtures/bambicloud-haptics.json) — no network involved.
/// </summary>
public class HapticCueTrackTests
{
    static HapticCueTrack Fixture() =>
        HapticCueTrack.Parse(File.ReadAllText(Path.Combine("Fixtures", "bambicloud-haptics.json")));

    [Fact]
    public void Parses_captured_cdn_track()
    {
        var t = Fixture();
        Assert.Equal(51, t.Cues.Count);
        Assert.Contains("good girl", t.Triggers);
        // real CDN data sometimes omits `stop` (pulse-only cue) — duration
        // must still come out usable for every cue
        Assert.All(t.Cues, c => Assert.True(c.DurationMs >= 50));
    }

    [Fact]
    public void Cues_are_ordered_by_start_time()
    {
        var t = Fixture();
        var starts = t.Cues.Select(c => c.StartMs).ToList();
        Assert.Equal(starts.OrderBy(x => x), starts);
    }

    [Fact]
    public void CuesBetween_catches_cues_in_a_poll_window()
    {
        var t = new HapticCueTrack(new[]
        {
            new HapticCue { StartMs = 1000, StopMs = 1400, Trigger = "bambi" },
            new HapticCue { StartMs = 2000, StopMs = 2300, Trigger = "snap" },
            new HapticCue { StartMs = 9000, StopMs = 9500, Trigger = "good girl" },
        });

        Assert.Empty(t.CuesBetween(0, 999));
        Assert.Single(t.CuesBetween(999, 1500));                 // catches the first
        Assert.Equal(2, t.CuesBetween(500, 2500).Count());       // long tick catches both
        Assert.Empty(t.CuesBetween(2000, 8000));                 // start is exclusive
    }

    [Fact]
    public void Duration_has_a_floor_so_zero_width_cues_still_pulse()
    {
        var c = new HapticCue { StartMs = 100, StopMs = 100 };
        Assert.Equal(50, c.DurationMs);
    }
}
