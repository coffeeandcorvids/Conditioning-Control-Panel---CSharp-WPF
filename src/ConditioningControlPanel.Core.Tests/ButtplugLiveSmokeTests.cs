using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Services.Haptics;
using Xunit;

/// <summary>
/// Live smoke against a real Intiface Central server (ws://127.0.0.1:12345).
/// Gated behind INTIFACE_LIVE=1 so CI/normal runs skip it. Per the Jun 29 wire
/// lesson: success criteria are explicit — a refused/failed websocket is a FAIL,
/// while "server reachable but no toy powered on" is a PASS (the wire itself is
/// what we're proving; hardware presence is Star's side).
/// </summary>
public class ButtplugLiveSmokeTests
{
    [Fact]
    public async Task Connects_to_intiface_server()
    {
        if (Environment.GetEnvironmentVariable("INTIFACE_LIVE") != "1")
            return; // not a live run

        var provider = new ButtplugProvider();
        string? lastError = null;
        provider.Error += (_, e) => lastError = e;

        var ok = await provider.ConnectAsync();

        if (ok)
        {
            // Full success: server + at least one device.
            Assert.True(provider.IsConnected);
            Assert.NotEmpty(provider.ConnectedDevices);
            await provider.StopAsync();
            await provider.DisconnectAsync();
            return;
        }

        // No device is acceptable for the wire smoke; a transport failure is not.
        Assert.NotNull(lastError);
        Assert.Contains("No devices found", lastError);
    }
}
