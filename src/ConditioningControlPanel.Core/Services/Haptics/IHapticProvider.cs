using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Core.Services.Haptics
{
    public enum HapticProviderType
    {
        None,
        Mock,
        Lovense,
        Buttplug
    }

    public interface IHapticProvider
    {
        string Name { get; }
        bool IsConnected { get; }
        List<string> ConnectedDevices { get; }

        event EventHandler<bool>? ConnectionChanged;
        event EventHandler<string>? DeviceDiscovered;
        event EventHandler<string>? Error;

        Task<bool> ConnectAsync();
        Task DisconnectAsync();
        Task VibrateAsync(double intensity, int durationMs);
        Task StopAsync();

        /// <summary>
        /// Verify the device is still reachable. IsConnected can lie when the OS routing
        /// table changes after connect (e.g. user enables a VPN), so call this before any
        /// operation that needs to confirm we can actually talk to the device.
        /// </summary>
        Task<bool> PingAsync();

        /// <summary>
        /// Structured per-device telemetry pulled from the backend (name, vibrate step
        /// resolution, battery). Empty when a provider can only report a bare name
        /// (e.g. the Lovense LAN path). Drives the DAW's device readout and lets the UI
        /// adapt to the connected toy's capabilities.
        /// </summary>
        IReadOnlyList<HapticDeviceInfo> Devices => System.Array.Empty<HapticDeviceInfo>();
    }

    /// <summary>What the UI knows about one connected toy.</summary>
    /// <param name="Name">Device display name, e.g. "Lovense Tenera".</param>
    /// <param name="VibeSteps">Discrete vibration steps (0 = off … N = max); 0 if unknown.</param>
    /// <param name="Battery">Battery level 0.0–1.0, or null if the toy has no battery sensor.</param>
    public sealed record HapticDeviceInfo(string Name, int VibeSteps, double? Battery);
}
