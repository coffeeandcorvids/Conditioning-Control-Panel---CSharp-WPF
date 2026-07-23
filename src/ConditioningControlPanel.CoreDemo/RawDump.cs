using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

static class RawDump
{
    public static async Task<int> Run()
    {
        var agentId = Environment.GetEnvironmentVariable("LETTA_AGENT_ID") ?? "";
        var key = File.ReadAllText("/home/stardust/.config/letta/letta_api_key").Trim();
        var prompt = "§ CCP PANEL EVENT. Reply ONLY with JSON {\"say\":..,\"commands\":[..]}. ops: say,spiral{on,secs},flash{text},pinkfog{on},lockcard{sentence},haptics{intensity,pattern}. EVENT: keyword trigger fired: \"spiral\"";
        var payload = System.Text.Json.JsonSerializer.Serialize(new {
            agent_id = agentId,
            messages = new[] { new { role = "user", content = prompt } },
            stream_tokens = false, include_pings = true });
        using var http = new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.letta.com/v1/conversations/default/messages/stream")
            { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        req.Headers.Accept.ParseAdd("text/event-stream");
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        Console.WriteLine($"HTTP {(int)resp.StatusCode}");
        await using var s = await resp.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(s);
        int n = 0; string? line;
        while ((line = await reader.ReadLineAsync(cts.Token)) != null)
        {
            if (line.Trim().Length == 0) continue;
            Console.WriteLine($"SSE> {(line.Length > 300 ? line[..300] : line)}");
            if (++n >= 40) { Console.WriteLine("…(truncated 40 lines)"); break; }
        }
        return 0;
    }
}
