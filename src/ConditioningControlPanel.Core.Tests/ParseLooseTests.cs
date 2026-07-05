using ConditioningControlPanel.Core.Commands;
using Xunit;

public class ParseLooseTests
{
    // LV's verbatim reply from the Jul 5 2026 live transport smoke — the canonical fixture.
    const string LiveReply = """
    *leans over the wire, taps it twice*

    Transport's breathing. I can hear you.

    { "say": "live and listening", "commands": [ {"op":"spiral","on":true,"opacity":30} ] }
    """;

    [Fact]
    public void Extracts_reaction_json_wrapped_in_prose()
    {
        var r = ReactionParser.ParseLoose(LiveReply);
        Assert.Equal("live and listening", r.Say);
        var sp = Assert.IsType<Spiral>(Assert.Single(r.Commands));
        Assert.True(sp.On);
        Assert.Equal(30, sp.Opacity);
    }

    [Fact]
    public void Pure_json_still_parses()
    {
        var r = ReactionParser.ParseLoose("""{ "say": "hi", "commands": [] }""");
        Assert.Equal("hi", r.Say);
    }

    [Fact]
    public void Takes_last_reaction_block_when_multiple()
    {
        var r = ReactionParser.ParseLoose("""
            first: { "say": "old" }
            newer: { "say": "current", "commands": [ {"op":"flash","text":"OBEY"} ] }
            """);
        Assert.Equal("current", r.Say);
        Assert.Single(r.Commands);
    }

    [Fact]
    public void Prose_only_becomes_say()
    {
        var r = ReactionParser.ParseLoose("just words, no json at all");
        Assert.Equal("just words, no json at all", r.Say);
        Assert.Empty(r.Commands);
    }

    [Fact]
    public void Braces_inside_strings_do_not_break_scan()
    {
        var r = ReactionParser.ParseLoose("""noise {"say": "has } brace", "commands": []} tail""");
        Assert.Equal("has } brace", r.Say);
    }

    [Fact]
    public void Empty_returns_silent()
    {
        var r = ReactionParser.ParseLoose("   ");
        Assert.Null(r.Say);
        Assert.Empty(r.Commands);
    }
}
