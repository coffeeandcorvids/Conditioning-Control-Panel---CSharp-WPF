using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ConditioningControlPanel.Shell.Effects;

internal static class EffectAssetPaths
{
    public static string? FindRepoAsset(string relative)
    {
        foreach (var root in Roots())
        {
            var p = Path.Combine(root, relative);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    public static IReadOnlyList<string> FindAudioFiles(params string[] relativeDirs)
    {
        var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".ogg" };
        var found = new List<string>();
        foreach (var root in Roots())
        foreach (var rel in relativeDirs)
        {
            var dir = Path.Combine(root, rel);
            if (!Directory.Exists(dir)) continue;
            found.AddRange(Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                .Where(f => exts.Contains(Path.GetExtension(f))));
        }
        return found.Distinct().ToList();
    }

    private static IEnumerable<string> Roots()
    {
        yield return Environment.CurrentDirectory;
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null)
        {
            yield return d.FullName;
            d = d.Parent;
        }
    }
}
