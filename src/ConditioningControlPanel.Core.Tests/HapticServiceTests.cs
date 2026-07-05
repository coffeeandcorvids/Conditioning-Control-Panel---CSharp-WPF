using System.Threading.Tasks;
using ConditioningControlPanel.Core.Services;
using ConditioningControlPanel.Core.Services.Haptics;
using ConditioningControlPanel.Core.Settings;
using Xunit;

public class HapticServiceTests
{
    static HapticService NewConnectedService(out HapticSettings settings)
    {
        settings = new HapticSettings { Provider = HapticProviderType.Mock };
        return new HapticService(settings);
    }

    [Fact]
    public async Task Connect_with_mock_provider_reports_connected_and_device()
    {
        var svc = NewConnectedService(out _);
        var ok = await svc.ConnectAsync();
        Assert.True(ok);
        Assert.True(svc.IsConnected);
        Assert.Contains("Mock Device (Vibrate)", svc.ConnectedDevices);
        svc.Dispose();
    }

    [Fact]
    public async Task Connect_auto_enables_haptics()
    {
        var svc = NewConnectedService(out var settings);
        settings.Enabled = false;
        await svc.ConnectAsync();
        Assert.True(settings.Enabled);
        svc.Dispose();
    }

    [Fact]
    public async Task Trigger_respects_master_enabled_gate()
    {
        var svc = NewConnectedService(out var settings);
        await svc.ConnectAsync();
        settings.Enabled = false;

        await svc.TriggerAsync("FlashDisplay", 0.8, 200);
        // Master gate off → the provider must never have been driven.
        // (Mock records every vibrate call; auto-enable already cleared by us above.)
        var mock = (MockHapticProvider?)typeof(HapticService)
            .GetField("_mockProvider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(svc);
        Assert.NotNull(mock);
        Assert.Empty(mock!.History);
        svc.Dispose();
    }

    [Fact]
    public async Task Trigger_drives_provider_when_enabled()
    {
        var svc = NewConnectedService(out var settings);
        await svc.ConnectAsync();
        settings.FlashDisplayEnabled = true;

        await svc.TriggerAsync("FlashDisplay", 0.8, 150);
        var mock = (MockHapticProvider?)typeof(HapticService)
            .GetField("_mockProvider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(svc);
        Assert.NotNull(mock);
        Assert.NotEmpty(mock!.History);
        svc.Dispose();
    }

    [Fact]
    public async Task Disconnect_reports_not_connected()
    {
        var svc = NewConnectedService(out _);
        await svc.ConnectAsync();
        await svc.DisconnectAsync();
        Assert.False(svc.IsConnected);
        svc.Dispose();
    }
}
