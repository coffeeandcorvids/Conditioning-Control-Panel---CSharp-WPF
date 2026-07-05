using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Serilog;

namespace ConditioningControlPanel.Core.Services.Haptics
{
    /// <summary>
    /// Headless mock provider for testing haptic flows without hardware.
    /// Upstream's WPF version visualized pulses in a toast window; in Core the
    /// visualization seam is the LastVibration/History surface + Serilog output,
    /// which unit tests and the Shell can observe instead.
    /// </summary>
    public class MockHapticProvider : IHapticProvider
    {
        public string Name => "Mock (testing)";
        public bool IsConnected { get; private set; }
        public List<string> ConnectedDevices { get; } = new();

        public event EventHandler<bool>? ConnectionChanged;
        public event EventHandler<string>? DeviceDiscovered;
        public event EventHandler<string>? Error;

        /// <summary>Most recent vibrate call, for tests/UI: (intensity, durationMs).</summary>
        public (double Intensity, int DurationMs)? LastVibration { get; private set; }

        /// <summary>Every vibrate call since connect, newest last.</summary>
        public List<(double Intensity, int DurationMs)> History { get; } = new();

        public Task<bool> ConnectAsync()
        {
            IsConnected = true;
            ConnectedDevices.Clear();
            ConnectedDevices.Add("Mock Device (Vibrate)");
            History.Clear();
            LastVibration = null;
            Log.Information("MockHapticProvider: Connected");
            DeviceDiscovered?.Invoke(this, "Mock Device");
            ConnectionChanged?.Invoke(this, true);
            return Task.FromResult(true);
        }

        public Task DisconnectAsync()
        {
            IsConnected = false;
            ConnectedDevices.Clear();
            Log.Information("MockHapticProvider: Disconnected");
            ConnectionChanged?.Invoke(this, false);
            return Task.CompletedTask;
        }

        public Task VibrateAsync(double intensity, int durationMs)
        {
            if (!IsConnected) return Task.CompletedTask;
            var clamped = Math.Clamp(intensity, 0.0, 1.0);
            LastVibration = (clamped, durationMs);
            History.Add((clamped, durationMs));
            Log.Debug("MockHapticProvider: Vibrate {Intensity:F2} for {Duration}ms", clamped, durationMs);
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            LastVibration = null;
            Log.Debug("MockHapticProvider: Stop");
            return Task.CompletedTask;
        }

        public Task<bool> PingAsync() => Task.FromResult(IsConnected);
    }
}
