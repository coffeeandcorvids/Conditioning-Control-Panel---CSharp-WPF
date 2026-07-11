using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Core.Models.Authoring;
using ConditioningControlPanel.Core.Services.Haptics;
using Xunit;

/// <summary>
/// Persistence for the Haptics DAW: project save/load, cue-map export (verified
/// against the shipped runtime parser), and project listing. Uses a throwaway
/// temp directory, cleaned up per test.
/// </summary>
public class HapticProjectServiceTests : IDisposable
{
    readonly string _dir;
    readonly HapticProjectService _svc = new();

    public HapticProjectServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ccp-hproj-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    static HapticProject Sample()
    {
        var p = new HapticProject { AudioRef = "clip.mp3", DurationMs = 8000, BaseAmount = 0.6 };
        p.AddCue(500, 800, "good girl", snap: true);
        p.AddCue(2000, 2200, "snap");
        p.Escalation["snap"] = new TriggerEscalation { PerRepetition = 0.2, Max = 1.8 };
        return p;
    }

    [Fact]
    public void Save_then_load_roundtrips_project()
    {
        var path = Path.Combine(_dir, "scene" + HapticProjectService.ProjectExtension);
        var p = Sample();
        _svc.Save(p, path);
        Assert.True(File.Exists(path));

        var back = _svc.Load(path);
        Assert.Equal(p.DurationMs, back.DurationMs);
        Assert.Equal(0.6, back.BaseAmount, 6);
        Assert.Equal(p.Cues.Count, back.Cues.Count);
        Assert.True(back.Escalation.ContainsKey("snap"));
    }

    [Fact]
    public void Save_creates_missing_directories()
    {
        var nested = Path.Combine(_dir, "a", "b", "deep" + HapticProjectService.ProjectExtension);
        _svc.Save(Sample(), nested);
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void ExportCueMap_writes_runtime_playable_json()
    {
        var out_ = Path.Combine(_dir, "clip.haptics.json");
        _svc.ExportCueMap(Sample(), out_);
        Assert.True(File.Exists(out_));

        // the shipped runtime parser must accept it and see the cues
        var track = HapticCueTrack.Parse(File.ReadAllText(out_));
        Assert.Equal(2, track.Cues.Count);
        Assert.Contains("good girl", track.Triggers);
        Assert.Equal(500, track.Cues[0].StartMs); // start-sorted
    }

    [Fact]
    public void CueMapPathBeside_uses_audio_name_and_dir()
    {
        var p = HapticProjectService.CueMapPathBeside(Path.Combine("x", "y", "trance.mp3"));
        Assert.Equal(Path.Combine("x", "y", "trance.haptics.json"), p);
    }

    [Fact]
    public void ListProjects_finds_only_hproj_files_sorted()
    {
        _svc.Save(Sample(), Path.Combine(_dir, "b" + HapticProjectService.ProjectExtension));
        _svc.Save(Sample(), Path.Combine(_dir, "a" + HapticProjectService.ProjectExtension));
        File.WriteAllText(Path.Combine(_dir, "not-a-project.txt"), "nope");

        var found = _svc.ListProjects(_dir).Select(Path.GetFileName).ToList();
        Assert.Equal(new[] { "a" + HapticProjectService.ProjectExtension,
                             "b" + HapticProjectService.ProjectExtension }, found);
    }

    [Fact]
    public void ListProjects_missing_dir_is_empty()
    {
        Assert.Empty(_svc.ListProjects(Path.Combine(_dir, "does-not-exist")));
    }
}
