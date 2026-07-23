using System.Net.Http;
using ConditioningControlPanel.Core.Agents.Letta;
using ConditioningControlPanel.Core.Events;

static class LiveSmoke
{
    public static async Task<int> Run()
    {
        var agentId = Environment.GetEnvironmentVariable("LETTA_AGENT_ID") ?? "";
        var keyPath = "/home/stardust/.config/letta/letta_api_key";
        var key = File.Exists(keyPath) ? File.ReadAllText(keyPath).Trim() : "";
        if (string.IsNullOrWhiteSpace(agentId) || string.IsNullOrWhiteSpace(key))
        { Console.WriteLine("STOP: missing agent id or key"); return 2; }

        Console.WriteLine($"LIVE smoke → agent {agentId[..Math.Min(18, agentId.Length)]}…, one minimal event, 90s timeout\n");
        using var http = new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        var agent = new LettaReactiveAgent(http, new LettaConfig(agentId, key));
        Console.WriteLine($"IsAvailable: {agent.IsAvailable}");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(95));
        try
        {
            var r = await agent.ReactAsync(new KeywordTriggered("spiral"), cts.Token);
            Console.WriteLine($"\n✅ PIPE ALIVE — LV replied.  [RAW DEBUG: say='{r.Say}' cmds={r.Commands.Count}]");
            Console.WriteLine($"   say: {(r.Say ?? "(none)")}");
            Console.WriteLine($"   commands: {r.Commands.Count}");
            foreach (var c in r.Commands) Console.WriteLine($"     - {c}");
            if (r.Say is null && r.Commands.Count == 0)
                Console.WriteLine("   (silent/empty — pipe worked but LV returned nothing parseable; check envelope)");
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine($"\n⚠️ STOP — {e.GetType().Name}: {e.Message}");
            return 1;
        }
    }
}
