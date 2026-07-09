using System.IO;
using System.Linq;
using ConditioningControlPanel.Core.Services.BambiCloud;
using Xunit;

/// <summary>
/// Parsing/conversion against a captured api.bambicloud.com/playlists response
/// (Fixtures/bambicloud-playlists.json) — no network involved.
/// </summary>
public class BambiCloudTests
{
    static string Fixture() => File.ReadAllText(Path.Combine("Fixtures", "bambicloud-playlists.json"));

    [Fact]
    public void ParsePlaylists_reads_captured_api_response()
    {
        var lists = BambiCloudClient.ParsePlaylists(Fixture());
        Assert.Equal(3, lists.Count);
        Assert.All(lists, p => Assert.False(string.IsNullOrWhiteSpace(p.Name)));
        Assert.All(lists, p => Assert.NotEmpty(p.Files));
    }

    [Fact]
    public void ToPlaylistDefinition_maps_tracks_with_cdn_urls()
    {
        var lists = BambiCloudClient.ParsePlaylists(Fixture());
        var def = BambiCloudClient.ToPlaylistDefinition(lists[0]);

        Assert.Equal(lists[0].Name.Trim(), def.Name);
        Assert.NotEmpty(def.Tracks);
        Assert.All(def.Tracks, t =>
        {
            Assert.StartsWith("https://", t.Path);
            Assert.False(string.IsNullOrWhiteSpace(t.Title));
        });
    }

    [Fact]
    public void ToPlaylistDefinition_skips_files_without_audio()
    {
        var bc = new BcPlaylist
        {
            Name = "mixed",
            Files =
            {
                new BcFile { Name = "playable", AudioUrl = "https://cdn.example/a.mp3", TrackNum = 2 },
                new BcFile { Name = "broken",   AudioUrl = null },
                new BcFile { Name = "first",    AudioUrl = "https://cdn.example/b.mp3", TrackNum = 1 },
            }
        };
        var def = BambiCloudClient.ToPlaylistDefinition(bc);

        Assert.Equal(2, def.Tracks.Count);
        Assert.Equal("first", def.Tracks[0].Title);     // ordered by trackNum
        Assert.Equal("playable", def.Tracks[1].Title);
    }

    [Fact]
    public void Track_extras_haptics_duration_kind_round_trip_json()
    {
        var lists = BambiCloudClient.ParsePlaylists(Fixture());
        var def = BambiCloudClient.ToPlaylistDefinition(lists[0]);

        var tmp = Path.GetTempFileName();
        try
        {
            def.SaveFile(tmp);
            var loaded = ConditioningControlPanel.Core.Services.Playlist.PlaylistDefinition.LoadFile(tmp);
            Assert.Equal(def.Tracks.Count, loaded.Tracks.Count);
            for (var i = 0; i < def.Tracks.Count; i++)
            {
                Assert.Equal(def.Tracks[i].HapticsPath, loaded.Tracks[i].HapticsPath);
                Assert.Equal(def.Tracks[i].DurationMs,  loaded.Tracks[i].DurationMs);
                Assert.Equal(def.Tracks[i].Kind,        loaded.Tracks[i].Kind);
            }
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Fixture_carries_haptics_urls_for_at_least_one_track()
    {
        // the whole point of going native: haptics patterns ride along with audio
        var lists = BambiCloudClient.ParsePlaylists(Fixture());
        var all = lists.SelectMany(p => p.Files);
        Assert.Contains(all, f => !string.IsNullOrWhiteSpace(f.HapticsUrl));
    }
}
