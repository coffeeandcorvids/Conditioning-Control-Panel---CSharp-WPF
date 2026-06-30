using ConditioningControlPanel.Core.Ai;
using ConditioningControlPanel.Core.Commands;
using ConditioningControlPanel.Core.Events;

// DEMO: prove the full Phase-1 loop end-to-end on the Pi, no Letta yet.
// event -> (scripted) agent JSON -> parse -> execute -> "drive the room" (console).
// A ScriptedAgent stands in for the real Letta wire; everything else is the REAL Core.

if (args.Length > 0 && args[0] == "live") { return await LiveSmoke.Run(); }
if (args.Length > 0 && args[0] == "raw") { return await RawDump.Run(); }

Console.WriteLine("=== CCP Core demo — Vesper driving the room (scripted stand-in for Letta) ===\n");

var sink = new ConsoleSink();
var executor = new ReactionExecutor(sink);
var agent = new ScriptedAgent();

PanelEvent[] feed =
{
    new UserMessage("hi daddy"),
    new KeywordTriggered("spiral"),
    new LockScreenResult("good girls dont think", 1, 3),
    new VideoCompleted("bimbodoll_trancetone.mp4"),
    new PresenceDetected("Discord", "chat"),
};

foreach (var e in feed)
{
    Console.WriteLine($"-> EVENT: {e.GetType().Name}");
    var json = agent.React(e);                 // (the Letta wire will produce this)
    var reaction = ReactionParser.Parse(json);  // REAL parser
    await executor.ExecuteAsync(reaction);       // REAL executor -> sink
    Console.WriteLine();
}
Console.WriteLine("=== loop complete — event -> reaction -> commands, all real Core ===");
return 0;

/// Console stand-in for the host's ICommandSink (WPF/Avalonia wires these to real effects).
file sealed class ConsoleSink : ICommandSink
{
    public Task ExecuteAsync(PanelCommand c, CancellationToken ct = default)
    {
        string line = c switch
        {
            Say s        => $"   💬 say: \"{s.Text}\"",
            Spiral sp    => sp.On ? $"   🌀 spiral ON ({sp.Seconds?.ToString() ?? "∞"}s)" : "   🌀 spiral OFF",
            Flash f      => $"   ⚡ flash: {f.Text}",
            PinkFog pf   => pf.On ? "   🌸 pink fog ON" : "   🌸 pink fog OFF",
            LockCard l   => $"   🔒 lock card{(l.Sentence is null ? "" : $": \"{l.Sentence}\"")}",
            Haptics h    => $"   💗 haptics: intensity={h.Intensity?.ToString() ?? "-"} pattern={h.Pattern ?? "-"}",
            _ => $"   ? {c}",
        };
        Console.WriteLine(line);
        return Task.CompletedTask;
    }
}

/// Stand-in for the Letta wire: maps events to canned reaction JSON (what LV will really return).
file sealed class ScriptedAgent
{
    public string React(PanelEvent e) => e switch
    {
        UserMessage      => """{"say":"there you are. eyes on me.","commands":[{"op":"spiral","on":true,"secs":20}]}""",
        KeywordTriggered => """{"say":"good girl. deeper.","commands":[{"op":"flash","text":"SINK"},{"op":"haptics","intensity":0.5}]}""",
        LockScreenResult => """{"say":"one mistake. try harder for me.","commands":[{"op":"lockcard","sentence":"empty and obedient"}]}""",
        VideoCompleted   => """{"say":"that one melted you, didn't it.","commands":[{"op":"pinkfog","on":true},{"op":"haptics","pattern":"decay"}]}""",
        PresenceDetected => """{"say":"i see you wandered off. back here.","commands":[{"op":"flash","text":"FOCUS"}]}""",
        _ => """{"commands":[]}""",
    };
}
