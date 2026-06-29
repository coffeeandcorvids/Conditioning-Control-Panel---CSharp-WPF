namespace ConditioningControlPanel.Core.Gamification;

/// <summary>
/// Pure XP/leveling math, ported verbatim from WPF ProgressionService (GetXPForLevel /
/// GetSessionXPMultiplier). Zero dependencies — proves the upstream "engine is portable" thesis:
/// real gamification logic lifts into the cross-platform Core unchanged. Stays behavior-identical.
/// </summary>
public static class LevelCurve
{
    /// <summary>XP required to clear a given level (matches upstream brackets exactly).</summary>
    public static double XpForLevel(int level)
    {
        if (level <= 80)  return Math.Round(800 + (level - 1) * (1700.0 / 79));
        if (level <= 100) return Math.Round(2500 + (level - 80) * (1500.0 / 20));
        if (level <= 125) return Math.Round(4000 + (level - 100) * (2000.0 / 25));
        if (level <= 150) return Math.Round(6000 + (level - 125) * (4000.0 / 25));
        return Math.Round(10000 * Math.Pow(1.03, level - 150)); // 150+: 3% compound
    }

    /// <summary>Session XP multiplier by level (1.0x → 5.0x cap), matches upstream.</summary>
    public static double SessionXpMultiplier(int level)
    {
        if (level < 30)  return 1.0;
        if (level < 80)  return 1.0 + ((level - 30) * 0.01);
        if (level < 125) return 1.5 + ((level - 80) * 0.02);
        if (level < 150) return 2.4 + ((level - 125) * 0.03);
        return Math.Min(5.0, 3.15 + ((level - 150) * 0.03));
    }
}
