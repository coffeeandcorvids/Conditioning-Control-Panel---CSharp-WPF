using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Core.Models.Authoring;

namespace ConditioningControlPanel.Core.Services.Haptics;

/// <summary>
/// Persistence for the Haptics DAW (task #18): save/load authoring projects
/// and export the runtime-playable cue map. Deliberately thin — the DAW tab's
/// only durable state is the <see cref="HapticProject"/> document plus the
/// cue-map JSON it emits; the shipped pipeline (HapticCueTrack + AudioHapticSync)
/// plays that cue map with no new runtime code. No UI, no toy, no audio decode.
/// </summary>
public sealed class HapticProjectService
{
    /// <summary>Authoring-project file extension.</summary>
    public const string ProjectExtension = ".hproj";

    /// <summary>Suffix for the exported runtime cue map (e.g. clip.mp3 → clip.haptics.json).</summary>
    public const string CueMapSuffix = ".haptics.json";

    /// <summary>Save the authoring project as JSON, creating the directory if needed.</summary>
    public void Save(HapticProject project, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, project.ToJson());
    }

    /// <summary>Load an authoring project from a .hproj file.</summary>
    public HapticProject Load(string path) => HapticProject.FromJson(File.ReadAllText(path));

    /// <summary>
    /// Write the runtime cue map to <paramref name="outputPath"/> and return it.
    /// The result is exactly what HapticCueTrack.Parse consumes.
    /// </summary>
    public string ExportCueMap(HapticProject project, string outputPath)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(outputPath, project.ToCueMapJson());
        return outputPath;
    }

    /// <summary>The conventional cue-map path sitting beside an audio file.</summary>
    public static string CueMapPathBeside(string audioPath)
    {
        var dir = Path.GetDirectoryName(audioPath) ?? "";
        var name = Path.GetFileNameWithoutExtension(audioPath);
        return Path.Combine(dir, name + CueMapSuffix);
    }

    /// <summary>Enumerate authoring projects in a directory (empty if it doesn't exist).</summary>
    public IReadOnlyList<string> ListProjects(string dir) =>
        Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*" + ProjectExtension).OrderBy(p => p).ToList()
            : new List<string>();
}
