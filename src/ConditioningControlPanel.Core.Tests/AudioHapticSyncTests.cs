using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Core.Services.Audio;
using ConditioningControlPanel.Core.Services.Haptics;
using Xunit;

public class AudioHapticSyncTests
{
    private sealed class FakePlayer : IAudioPlayer
    {
        public long PositionMs { get; set; }
        public long DurationMs { get; set; } = 60_000;
        public int Volume { get; set; } = 80;
        public bool IsPlaying { get; set; } = true;
#pragma warning disable CS0067
        public event EventHandler? Ended;
        public event EventHandler<string>? Error;
#pragma warning restore CS0067
        public void Play(string pathOrUrl) { }
        public void Pause() { }
        public void Resume() { }
        public void Stop() { }
    }

    static HapticCueTrack Track() => new(new[]
    {
        new HapticCue { StartMs = 1000, StopMs = 1400, Trigger = "bambi" },
        new HapticCue { StartMs = 2000, StopMs = 2600, Trigger = "good girl" },
        new HapticCue { StartMs = 5000, StopMs = 5300, Trigger = "snap" },
    });

    static (AudioHapticSync sync, FakePlayer player, List<string> fired) Rig()
    {
        var player = new FakePlayer();
        var sync = new AudioHapticSync(player);
        var fired = new List<string>();
        sync.CueDue += (_, c) => fired.Add(c.Trigger);
        sync.Start(Track());
        return (sync, player, fired);
    }

    [Fact]
    public void Fires_cues_as_playback_crosses_them()
    {
        var (sync, player, fired) = Rig();

        player.PositionMs = 500;  sync.Tick();
        Assert.Empty(fired);

        player.PositionMs = 1200; sync.Tick();
        Assert.Equal(new[] { "bambi" }, fired);

        player.PositionMs = 2100; sync.Tick();
        Assert.Equal(new[] { "bambi", "good girl" }, fired);
    }

    [Fact]
    public void A_long_gap_between_polls_still_fires_every_cue_inside_it()
    {
        var (sync, player, fired) = Rig();
        player.PositionMs = 6000; sync.Tick();   // one poll jumps past all three
        Assert.Equal(new[] { "bambi", "good girl", "snap" }, fired);
    }

    [Fact]
    public void Seeking_backwards_resets_without_replaying_history()
    {
        var (sync, player, fired) = Rig();
        player.PositionMs = 3000; sync.Tick();
        fired.Clear();

        player.PositionMs = 500;  sync.Tick();   // rewind
        Assert.Empty(fired);

        player.PositionMs = 1500; sync.Tick();   // bambi crossed again after rewind
        Assert.Equal(new[] { "bambi" }, fired);
    }

    [Fact]
    public void Stop_ends_the_sync_and_clears_the_track()
    {
        var (sync, player, fired) = Rig();
        sync.Stop();
        Assert.False(sync.Running);
        Assert.Null(sync.Track);
        player.PositionMs = 6000; sync.Tick();
        Assert.Empty(fired);
    }

    [Fact]
    public void Starting_a_new_track_replaces_the_old_window()
    {
        var (sync, player, fired) = Rig();
        player.PositionMs = 1200; sync.Tick();
        fired.Clear();

        player.PositionMs = 0;
        sync.Start(new HapticCueTrack(new[]
        {
            new HapticCue { StartMs = 100, StopMs = 300, Trigger = "sleep" },
        }));
        player.PositionMs = 200; sync.Tick();
        Assert.Equal(new[] { "sleep" }, fired);
    }
}
