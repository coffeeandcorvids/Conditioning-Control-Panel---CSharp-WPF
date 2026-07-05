using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Core.Commands;
using ConditioningControlPanel.Core.Services.Playlist;
using Xunit;

public class PlaylistTests
{
    static PlaylistDefinition ThreeTracks() => new()
    {
        Name = "descent",
        Tracks = new List<PlaylistTrack>
        {
            new() { Path = "/a.mp3", Title = "Sink" },
            new() { Path = "/b.mp3", Title = "Deeper" },
            new() { Path = "/c.mp3", Title = "Gone" },
        }
    };

    [Fact]
    public void Load_autostarts_first_track_and_next_prev_walk()
    {
        var e = new PlaylistEngine(seed: 1);
        var seen = new List<string>();
        e.TrackChanged += (_, t) => seen.Add(t.Title);

        e.Load(ThreeTracks());
        Assert.Equal("Sink", e.CurrentTrack!.Title);
        e.Next(); e.Next();
        Assert.Equal("Gone", e.CurrentTrack!.Title);
        e.Prev();
        Assert.Equal("Deeper", e.CurrentTrack!.Title);
        Assert.Equal(new[] { "Sink", "Deeper", "Gone", "Deeper" }, seen);
    }

    [Fact]
    public void Repeat_all_wraps_repeat_off_ends()
    {
        var e = new PlaylistEngine(seed: 1) { Repeat = RepeatMode.All };
        e.Load(ThreeTracks());
        e.Next(); e.Next();
        Assert.NotNull(e.Next()); // wraps
        Assert.Equal("Sink", e.CurrentTrack!.Title);

        var ended = false;
        var e2 = new PlaylistEngine(seed: 1) { Repeat = RepeatMode.Off };
        e2.PlaylistEnded += (_, _) => ended = true;
        e2.Load(ThreeTracks());
        e2.Next(); e2.Next();
        Assert.Null(e2.Next());
        Assert.True(ended);
    }

    [Fact]
    public void Shuffle_plays_every_track_once_per_pass()
    {
        var e = new PlaylistEngine(seed: 42) { Repeat = RepeatMode.All };
        e.Load(ThreeTracks(), autoStart: false);
        e.SetShuffle(true);
        var titles = new List<string>();
        for (var i = 0; i < 3; i++) titles.Add(e.Next()!.Title);
        Assert.Equal(3, titles.Distinct().Count());
    }

    [Fact]
    public void JumpTo_matches_title_case_insensitive()
    {
        var e = new PlaylistEngine(seed: 1);
        e.Load(ThreeTracks());
        var t = e.JumpTo("deeper");
        Assert.Equal("Deeper", t!.Title);
        Assert.Null(e.JumpTo("nope"));
    }

    [Fact]
    public void Parser_handles_playlist_op()
    {
        var r = ReactionParser.Parse("""{ "commands": [ {"op":"playlist","do":"jump","arg":"deeper"} ] }""");
        var c = Assert.IsType<PlaylistOp>(Assert.Single(r.Commands));
        Assert.Equal("jump", c.Do);
        Assert.Equal("deeper", c.Arg);
    }
}
