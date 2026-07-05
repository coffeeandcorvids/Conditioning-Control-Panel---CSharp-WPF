using System.Text.Json;

namespace ConditioningControlPanel.Core.Commands;

/// <summary>
/// Parses the agent's JSON payload into an AgentReaction. Portable (System.Text.Json).
/// Shape: { "say": "good girl", "commands": [ {"op":"spiral","on":true,"secs":30}, {"op":"haptics","intensity":0.6} ] }
/// Tolerant: unknown ops are skipped (forward-compatible as the vocabulary grows); bad JSON => Silent.
/// </summary>
public static class ReactionParser
{
    public static AgentReaction Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return AgentReaction.Silent;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string? say = root.TryGetProperty("say", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            var cmds = new List<PanelCommand>();
            if (root.TryGetProperty("commands", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var e in arr.EnumerateArray())
                {
                    var op = e.TryGetProperty("op", out var o) ? o.GetString() : null;
                    PanelCommand? c = op switch
                    {
                        "say"     => new Say(Str(e, "text") ?? ""),
                        "spiral"  => new Spiral(Bool(e, "on"), Int(e, "secs")),
                        "flash"   => new Flash(Str(e, "text") ?? ""),
                        "pinkfog" => new PinkFog(Bool(e, "on")),
                        "lockcard"=> new LockCard(Str(e, "sentence")),
                        "haptics" => new Haptics(Dbl(e, "intensity"), Str(e, "pattern")),
                        "playlist"=> new PlaylistOp(Str(e, "do") ?? "next", Str(e, "arg")),
                        _ => null, // unknown op: skip, stay forward-compatible
                    };
                    if (c != null) cmds.Add(c);
                }
            return new AgentReaction(say, cmds);
        }
        catch (JsonException) { return AgentReaction.Silent; }
    }

    static string? Str(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    static bool Bool(JsonElement e, string k) => e.TryGetProperty(k, out var v) && (v.ValueKind == JsonValueKind.True);
    static int? Int(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.TryGetInt32(out var i) ? i : null;
    static double? Dbl(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.TryGetDouble(out var d) ? d : null;
}
