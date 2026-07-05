using Companion.Core.Grid;

namespace Companion.Core.Career;

/// <summary>
/// Car+driver strength ranking over a resolved <see cref="GridPlan"/> — the source of
/// "expected finish" for OPI and the pace anchor. Car strength comes from the team's tier
/// scalar band (power/weight/drag, 1.0-neutral, authored per era) plus reliability; driver
/// strength is the seat's merged raceSkill. The car is weighted heavier than the driver
/// ("your tier-4 car really is slower" — PLAN.md).
/// </summary>
public static class SeatStrengthModel
{
    /// <summary>Raw car pace from the tier scalar band: power helps, weight and drag hurt.
    /// 1.0 is a fully neutral car; the era-authored band spans roughly 0.94–1.06.</summary>
    public static double CarRating(GridSeat seat) =>
        seat.PowerScalar - seat.WeightScalar - seat.DragScalar + 2.0;

    /// <summary>Car pace normalized to the 0..1 rating scale: the ±0.05-ish scalar band is
    /// amplified ×10 around a 0.5 midpoint so a tier-5 car and a tier-1 car are meaningfully
    /// apart on the same scale as driver ratings.</summary>
    public static double CarScore(GridSeat seat) =>
        Math.Clamp((CarRating(seat) - 1.0) * 10.0 + 0.5, 0.0, 1.0);

    /// <summary>Combined seat strength: 60% car, 30% driver (merged raceSkill), 10% reliability.</summary>
    public static double Strength(GridSeat seat) =>
        0.6 * CarScore(seat) + 0.3 * seat.Ratings.RaceSkill + 0.1 * seat.Reliability;

    /// <summary>Expected finish of one seat: its 1-based rank by <see cref="Strength"/> among
    /// every seat of the round's grid. Ties resolve by grid order (earlier seat ranks better),
    /// so the ranking is total and deterministic.</summary>
    public static int ExpectedFinish(GridPlan grid, int seatIndex)
    {
        if (seatIndex < 0 || seatIndex >= grid.Seats.Count)
            throw new ArgumentOutOfRangeException(nameof(seatIndex));

        double strength = Strength(grid.Seats[seatIndex]);
        int rank = 1;
        for (int i = 0; i < grid.Seats.Count; i++)
        {
            if (i == seatIndex)
                continue;
            double other = Strength(grid.Seats[i]);
            if (other > strength || (other == strength && i < seatIndex))
                rank++;
        }
        return rank;
    }

    /// <summary>Expected finish of the player's seat (the grid must contain one).</summary>
    public static int ExpectedPlayerFinish(GridPlan grid)
    {
        int index = PlayerSeatIndex(grid);
        return ExpectedFinish(grid, index);
    }

    public static int PlayerSeatIndex(GridPlan grid)
    {
        for (int i = 0; i < grid.Seats.Count; i++)
        {
            if (grid.Seats[i].IsPlayer)
                return i;
        }
        throw new InvalidOperationException(
            $"Round {grid.Round} grid of {grid.PackId} has no player seat — resolve it with a PlayerSeat.");
    }
}
