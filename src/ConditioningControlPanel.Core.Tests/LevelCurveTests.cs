using ConditioningControlPanel.Core.Gamification;
using Xunit;

public class LevelCurveTests
{
    [Theory]
    [InlineData(1, 800)]      // bracket start
    [InlineData(80, 2500)]    // end of easy bracket
    [InlineData(100, 4000)]   // bracket boundary
    [InlineData(125, 6000)]   // bracket boundary
    [InlineData(150, 10000)]  // start of compound
    [InlineData(151, 10300)]  // 10000 * 1.03
    public void XpForLevel_matches_upstream_at_boundaries(int level, double expected)
        => Assert.Equal(expected, LevelCurve.XpForLevel(level));

    [Theory]
    [InlineData(10, 1.0)]                 // below 30 → flat
    [InlineData(300, 5.0)]                // hard cap
    public void SessionMultiplier_floor_and_cap(int level, double expected)
        => Assert.Equal(expected, LevelCurve.SessionXpMultiplier(level));

    [Fact]
    public void Curve_is_monotonic_increasing()
    {
        for (int l = 2; l <= 400; l++)
            Assert.True(LevelCurve.XpForLevel(l) >= LevelCurve.XpForLevel(l - 1));
    }
}
