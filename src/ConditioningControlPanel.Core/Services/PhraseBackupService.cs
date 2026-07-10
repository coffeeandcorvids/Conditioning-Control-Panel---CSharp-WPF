using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using ConditioningControlPanel.Core.Settings;

namespace ConditioningControlPanel.Core.Services;

/// <summary>
/// Exports/imports the user's editable phrase pools (subliminals, lock-card
/// phrases, bouncing text, trigger keywords) to a standalone
/// <c>.ccpphrases.json</c> file — a safety net so an update, a corrupt
/// settings file, or a move to a new machine can never permanently cost
/// someone the phrases they wrote. Ported from upstream v6.2.9 (MIT),
/// adapted to PanelSettings + System.Text.Json; pools are captured BY
/// PROPERTY NAME so renames can't silently drop them and unknown members
/// in old backups are skipped, never fatal.
/// </summary>
public sealed class PhraseBackupService
{
    public const string Schema = "ccp-phrases/v1";

    /// <summary>PanelSettings properties that hold user-editable phrase content.</summary>
    internal static readonly string[] PhraseProperties =
    {
        nameof(PanelSettings.SubliminalPhrases),
        nameof(PanelSettings.LockCardPhrases),
        nameof(PanelSettings.BouncingTextPhrases),
        nameof(PanelSettings.Keywords),
    };

    /// <summary>Suggested file name for an export dialog.</summary>
    public static string GetExportFileName()
        => $"ccp-phrases-{DateTime.Now:yyyyMMdd}.ccpphrases.json";

    /// <summary>Writes the current phrase pools to <paramref name="filePath"/>. Returns the entry count written.</summary>
    public int Export(PanelSettings settings, string filePath)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var phrases = new JsonObject();
        foreach (var name in PhraseProperties)
        {
            var value = Pool(settings, name);
            if (value is { Count: > 0 })
                phrases[name] = new JsonArray(value.Select(v => (JsonNode)v!).ToArray());
        }

        var backup = new JsonObject
        {
            ["schema"] = Schema,
            ["exported_at"] = DateTime.UtcNow.ToString("o"),
            ["phrases"] = phrases,
        };

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(filePath, backup.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        return CountEntries(phrases);
    }

    /// <summary>Checks that <paramref name="filePath"/> is a phrase backup this app understands.</summary>
    public bool Validate(string filePath, out string error)
    {
        error = "";
        try
        {
            if (!File.Exists(filePath)) { error = "File not found"; return false; }
            var obj = JsonNode.Parse(File.ReadAllText(filePath))?.AsObject();
            var schema = obj?["schema"]?.GetValue<string>();
            if (schema != Schema) { error = $"Unrecognized file (schema '{schema}')"; return false; }
            if (obj?["phrases"] is not JsonObject p || p.Count == 0) { error = "No phrases in file"; return false; }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Applies the phrase pools from a backup onto <paramref name="settings"/> in place,
    /// REPLACING each pool (an import is a restore, not a merge). Only whitelisted phrase
    /// properties are applied — a crafted file can never populate arbitrary settings.
    /// Unknown or malformed members are skipped, never fatal. Returns entries applied.
    /// Caller persists (settings.Save()) and refreshes UI.
    /// </summary>
    public int Import(PanelSettings settings, string filePath)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var obj = JsonNode.Parse(File.ReadAllText(filePath))?.AsObject()
            ?? throw new InvalidOperationException("File is not a CCP phrase backup.");
        if (obj["schema"]?.GetValue<string>() != Schema)
            throw new InvalidOperationException("File is not a CCP phrase backup.");
        if (obj["phrases"] is not JsonObject phrases)
            throw new InvalidOperationException("Backup contains no phrases.");

        var applied = 0;
        foreach (var name in PhraseProperties)
        {
            if (phrases[name] is not JsonArray arr) continue;
            var list = arr.Select(n => n?.GetValue<string>())
                          .Where(s => !string.IsNullOrWhiteSpace(s))
                          .Select(s => s!.Trim())
                          .ToList();
            if (list.Count == 0) continue;

            var prop = typeof(PanelSettings).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (prop?.PropertyType != typeof(List<string>)) continue;
            prop.SetValue(settings, list);
            applied += list.Count;
        }
        return applied;
    }

    private static List<string>? Pool(PanelSettings settings, string name) =>
        typeof(PanelSettings).GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(settings) as List<string>;

    private static int CountEntries(JsonObject phrases) =>
        phrases.Sum(p => p.Value is JsonArray a ? a.Count : 0);
}
