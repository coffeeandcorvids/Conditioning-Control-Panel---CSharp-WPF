using System.Collections.Generic;
using ConditioningControlPanel.Core.Commands;
using Xunit;

file sealed class RecordingSink : ICommandSink
{
    public List<PanelCommand> Performed = new();
    public Task ExecuteAsync(PanelCommand c, System.Threading.CancellationToken ct = default)
    { Performed.Add(c); return Task.CompletedTask; }
}

public class CommandTests
{
    const string Payload = """
    { "say": "good girl", "commands": [
        {"op":"spiral","on":true,"secs":30},
        {"op":"flash","text":"OBEY"},
        {"op":"haptics","intensity":0.6},
        {"op":"wat","weird":1}
    ] }
    """;

    [Fact]
    public void Parses_say_and_known_commands_skips_unknown()
    {
        var r = ReactionParser.Parse(Payload);
        Assert.Equal("good girl", r.Say);
        Assert.Equal(3, r.Commands.Count);                 // 3 known, "wat" skipped
        Assert.Equal(new Spiral(true, 30), r.Commands[0]);
        Assert.Equal(new Flash("OBEY"), r.Commands[1]);
        Assert.Equal(new Haptics(0.6, null), r.Commands[2]);
    }

    [Fact]
    public void Bad_json_is_silent_not_crash()
        => Assert.Same(AgentReaction.Silent, ReactionParser.Parse("}{not json"));

    [Fact]
    public async Task Executor_speaks_then_runs_commands_in_order()
    {
        var sink = new RecordingSink();
        await new ReactionExecutor(sink).ExecuteAsync(ReactionParser.Parse(Payload));
        Assert.Equal(4, sink.Performed.Count);             // Say + 3 commands
        Assert.IsType<Say>(sink.Performed[0]);             // speaks first
        Assert.IsType<Spiral>(sink.Performed[1]);
    }
}
