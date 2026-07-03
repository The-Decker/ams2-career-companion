namespace Companion.Core.Career;

/// <summary>
/// Overperformance index per the contract: <c>OPI ← 0.8·OPI + 0.2·(expectedFinish −
/// actualFinish)</c>, DNF-cause aware — mechanical DNFs score as the expected finish (no
/// blame, zero delta), driver-error DNFs as the grid size (full blame).
/// </summary>
public static class OpiMath
{
    public const double Retention = 0.8;
    public const double Gain = 0.2;

    /// <summary>The finish position OPI charges the player with. Classified finishes use the
    /// classification; DNFs substitute per cause.</summary>
    public static double EffectiveFinish(double expectedFinish, int? actualFinish, DnfCause? dnfCause, int gridSize)
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
            DnfCause.DriverError => gridSize,
            _ => throw new ArgumentException(
                "A round outcome is either a classified position or a DNF with a cause.",
                nameof(dnfCause)),
        };
    }

    public static double Update(double opi, double expectedFinish, double effectiveFinish) =>
        Retention * opi + Gain * (expectedFinish - effectiveFinish);
}
