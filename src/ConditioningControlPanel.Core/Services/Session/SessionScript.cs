using System;
using ConditioningControlPanel.Core.Models;

namespace ConditioningControlPanel.Core.Services.Session;

/// <summary>
/// What the panel should look like at one instant of a scripted session.
/// Opacities are percent; -1 frequency means "feature not active".
/// </summary>
public readonly record struct SessionMoment(
    bool SpiralOn,   int SpiralOpacity,
    bool PinkOn,     int PinkOpacity,
    bool FlashOn,    int FlashOpacity, int FlashPerHour,
    bool SubliminalOn,
    bool BouncingTextOn);

/// <summary>
/// Pure evaluator for session scripts — the upstream sessions carry
/// start/end minutes and start→end ramp values per feature, but the WPF
/// engine applied them imperatively (and v6.2.x had to fix ramps clobbering
/// live opacity). Here the whole thing is a pure function of elapsed time,
/// so it's trivially testable and the host just applies diffs.
/// </summary>
public static class SessionScript
{
    /// <summary>Evaluate the scripted state at <paramref name="elapsed"/> into a session of <paramref name="durationMinutes"/>.</summary>
    public static SessionMoment MomentAt(SessionSettings s, int durationMinutes, TimeSpan elapsed)
    {
        var m = Math.Max(0, elapsed.TotalMinutes);

        var spiralOn = s.SpiralEnabled && InWindow(m, s.SpiralStartMinute, s.SpiralEndMinute, durationMinutes);
        var pinkOn   = s.PinkFilterEnabled && InWindow(m, s.PinkFilterStartMinute, s.PinkFilterEndMinute, durationMinutes);
        var flashOn  = s.FlashEnabled && InWindow(m, s.FlashStartMinute, s.FlashEndMinute, durationMinutes);
        var subOn    = s.SubliminalEnabled && InWindow(m, s.SubliminalStartMinute, s.SubliminalEndMinute, durationMinutes);
        var bounceOn = s.BouncingTextEnabled && InWindow(m, s.BouncingTextStartMinute, s.BouncingTextEndMinute, durationMinutes);

        return new SessionMoment(
            SpiralOn: spiralOn,
            SpiralOpacity: spiralOn
                ? Lerp(s.SpiralOpacity, s.SpiralOpacityEnd, Progress(m, s.SpiralStartMinute, s.SpiralEndMinute, durationMinutes))
                : 0,
            PinkOn: pinkOn,
            PinkOpacity: pinkOn
                ? Lerp(s.PinkFilterStartOpacity, s.PinkFilterEndOpacity, Progress(m, s.PinkFilterStartMinute, s.PinkFilterEndMinute, durationMinutes))
                : 0,
            FlashOn: flashOn,
            FlashOpacity: flashOn
                ? Lerp(s.FlashOpacity, s.FlashOpacityEnd, Progress(m, s.FlashStartMinute, s.FlashEndMinute, durationMinutes))
                : 0,
            FlashPerHour: flashOn
                ? Lerp(s.FlashPerHour, s.FlashPerHourEnd, Progress(m, s.FlashStartMinute, s.FlashEndMinute, durationMinutes))
                : -1,
            SubliminalOn: subOn,
            BouncingTextOn: bounceOn);
    }

    /// <summary>A feature window; end of -1 means "runs to session end".</summary>
    private static bool InWindow(double minute, int startMin, int endMin, int durationMinutes)
    {
        var end = endMin < 0 ? durationMinutes : endMin;
        return minute >= startMin && minute < end;
    }

    /// <summary>0..1 progress through the feature's own window (not the whole session).</summary>
    private static double Progress(double minute, int startMin, int endMin, int durationMinutes)
    {
        var end = (double)(endMin < 0 ? durationMinutes : endMin);
        if (end <= startMin) return 1.0;
        return Math.Clamp((minute - startMin) / (end - startMin), 0.0, 1.0);
    }

    private static int Lerp(int from, int to, double t) => (int)Math.Round(from + (to - from) * t);
}
