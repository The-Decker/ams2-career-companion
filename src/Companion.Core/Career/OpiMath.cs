using Companion.Core.Character;

namespace Companion.Core.Career;

/// <summary>
/// Overperformance index per the contract: <c>OPI ← 0.8·OPI + 0.2·(expectedFinish −
/// actualFinish)</c>, DNF-cause aware — mechanical DNFs score as the expected finish (no
/// blame, zero delta), driver-error DNFs as the grid size (full blame).
///
/// A player's character perks patch this via an optional <see cref="PlayerPerkModifiers"/>: a null
/// modifier (every non-character career) reproduces the exact contract above, so existing careers
/// and replay are byte-identical (docs/dev/character-system.md §6.1).
/// </summary>
public static class OpiMath
{
    public const double Retention = 0.8;
    public const double Gain = 0.2;

    /// <summary>The finish position OPI charges the player with. Classified finishes use the
    /// classification; DNFs substitute per cause. Perks scale ONLY the driver-error blame
    /// (a knife-edge car blames the driver more; a calm head softens it) — a null modifier keeps
    /// the driver-error finish at the grid size exactly.</summary>
    public static double EffectiveFinish(
        double expectedFinish, int? actualFinish, DnfCause? dnfCause, int gridSize,
        PlayerPerkModifiers? mods = null)
    {
        if (actualFinish is { } finish)
        {
            if (finish < 1)
                throw new ArgumentOutOfRangeException(nameof(actualFinish), "Positions are 1-based.");
            return finish;
        }

        return dnfCause switch
        {
            DnfCause.Mechanical => expectedFinish,
            DnfCause.DriverError => DriverErrorFinish(expectedFinish, gridSize, mods),
            _ => throw new ArgumentException(
                "A round outcome is either a classified position or a DNF with a cause.",
                nameof(dnfCause)),
        };
    }

    /// <summary>Full blame (the grid size) by default; <c>errorBlameScale</c> scales the blame
    /// beyond the expected finish and <c>blameFloorBlend</c> softens it back toward expected.</summary>
    private static double DriverErrorFinish(double expectedFinish, int gridSize, PlayerPerkModifiers? mods)
    {
        if (mods is null)
            return gridSize;
        double blamed = expectedFinish + mods.ErrorBlameScale * (gridSize - expectedFinish);
        return blamed + mods.BlameFloorBlend * (expectedFinish - blamed);
    }

    public static double Update(
        double opi, double expectedFinish, double effectiveFinish, PlayerPerkModifiers? mods = null)
    {
        if (mods is null)
            return Retention * opi + Gain * (expectedFinish - effectiveFinish);
        return mods.OpiRetention * opi + Gain * mods.OpiGainScale * (expectedFinish - effectiveFinish);
    }
}
