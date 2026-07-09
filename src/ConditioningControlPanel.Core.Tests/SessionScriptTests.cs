using System;
using ConditioningControlPanel.Core.Models;
using ConditioningControlPanel.Core.Services.Session;
using Xunit;

/// <summary>
/// Ramp/window math for scripted sessions. Includes the v6.2.x regression
/// upstream had to fix: a ramp must not clobber opacity outside its window.
/// </summary>
public class SessionScriptTests
{
    static SessionSettings RampingSpiral() => new()
    {
        SpiralEnabled = true,
        SpiralStartMinute = 10,
        SpiralEndMinute = -1,        // to session end
        SpiralOpacity = 5,
        SpiralOpacityEnd = 30,
    };

    [Fact]
    public void Feature_is_off_before_its_window_opens()
    {
        var m = SessionScript.MomentAt(RampingSpiral(), 60, TimeSpan.FromMinutes(5));
        Assert.False(m.SpiralOn);
        Assert.Equal(0, m.SpiralOpacity);
    }

    [Fact]
    public void Ramp_starts_at_start_value_when_window_opens()
    {
        var m = SessionScript.MomentAt(RampingSpiral(), 60, TimeSpan.FromMinutes(10));
        Assert.True(m.SpiralOn);
        Assert.Equal(5, m.SpiralOpacity);
    }

    [Fact]
    public void Ramp_interpolates_across_the_feature_window_not_the_session()
    {
        // window = minute 10..60 → halfway through window at minute 35
        var m = SessionScript.MomentAt(RampingSpiral(), 60, TimeSpan.FromMinutes(35));
        Assert.True(m.SpiralOn);
        Assert.Equal(18, m.SpiralOpacity);   // 5 + (30-5)*0.5 = 17.5 → rounds to 18
    }

    [Fact]
    public void Feature_closes_at_session_end()
    {
        var m = SessionScript.MomentAt(RampingSpiral(), 60, TimeSpan.FromMinutes(60));
        Assert.False(m.SpiralOn);
    }

    [Fact]
    public void Explicit_end_minute_closes_the_window_early()
    {
        var s = RampingSpiral();
        s.SpiralEndMinute = 20;
        Assert.True(SessionScript.MomentAt(s, 60, TimeSpan.FromMinutes(19)).SpiralOn);
        Assert.False(SessionScript.MomentAt(s, 60, TimeSpan.FromMinutes(20)).SpiralOn);
    }

    [Fact]
    public void Flat_ramp_stays_constant()
    {
        var s = new SessionSettings { PinkFilterEnabled = true, PinkFilterStartOpacity = 25, PinkFilterEndOpacity = 25 };
        Assert.Equal(25, SessionScript.MomentAt(s, 30, TimeSpan.FromMinutes(1)).PinkOpacity);
        Assert.Equal(25, SessionScript.MomentAt(s, 30, TimeSpan.FromMinutes(29)).PinkOpacity);
    }

    [Fact]
    public void Flash_frequency_ramps_alongside_opacity()
    {
        var s = new SessionSettings
        {
            FlashEnabled = true, FlashPerHour = 10, FlashPerHourEnd = 30,
            FlashOpacity = 40, FlashOpacityEnd = 80,
        };
        var m = SessionScript.MomentAt(s, 20, TimeSpan.FromMinutes(10));
        Assert.Equal(20, m.FlashPerHour);
        Assert.Equal(60, m.FlashOpacity);
    }

    [Fact]
    public void Disabled_features_never_report_on()
    {
        var m = SessionScript.MomentAt(new SessionSettings(), 30, TimeSpan.FromMinutes(15));
        Assert.False(m.SpiralOn);
        Assert.False(m.PinkOn);
        Assert.False(m.FlashOn);
        Assert.False(m.SubliminalOn);
        Assert.Equal(-1, m.FlashPerHour);
    }

    [Fact]
    public void BuiltIn_sessions_evaluate_without_throwing_across_their_whole_duration()
    {
        foreach (var s in Session.GetAllSessions())
            for (var min = 0; min <= s.DurationMinutes; min++)
                _ = SessionScript.MomentAt(s.Settings, s.DurationMinutes, TimeSpan.FromMinutes(min));
    }
}
