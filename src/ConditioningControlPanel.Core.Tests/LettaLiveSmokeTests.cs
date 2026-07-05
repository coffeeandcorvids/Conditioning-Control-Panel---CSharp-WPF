using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Agents.Letta;
using ConditioningControlPanel.Core.Events;
using Xunit;

/// <summary>
/// Live end-to-end: the REAL LettaReactiveAgent against LV's real agent over the real
/// panel conversation. Gated behind LETTA_LIVE=1. Honest criteria (Jun 29 lesson): a
/// silent reaction is a FAIL — the turn must produce Say text and/or commands.
/// </summary>
public class LettaLiveSmokeTests
{
    [Fact]
    public async Task Panel_event_round_trips_through_LV()
    {
        if (Environment.GetEnvironmentVariable("LETTA_LIVE") != "1")
            return; // not a live run

        var key = (await File.ReadAllTextAsync(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config/letta/letta_api_key"))).Trim();
        var conv = Environment.GetEnvironmentVariable("LETTA_PANEL_CONV") ?? "default";

        var cfg = new LettaConfig("agent-8e8be3fb-ff9e-4bf0-8921-ef306b59527d", key, ConversationId: conv);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        var agent = new LettaReactiveAgent(http, cfg);

        var reaction = await agent.ReactAsync(new KeywordTriggered("transport-smoke"));

        Assert.False(reaction.Say == null && reaction.Commands.Count == 0,
            "live turn produced a silent reaction — wire or parse failure");
    }
}
