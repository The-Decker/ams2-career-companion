using Companion.Core.Determinism;
using Companion.Core.Grid;

namespace Companion.Core.Career;

/// <summary>
/// A DETERMINISTIC field-result generator for an auto-simulated round (character death &amp; injury §5).
/// AMS2 cannot spectate a single-player race, so a round the injured player must sit out is simulated by
/// the app: rank every NON-player seat by its resolved <see cref="SeatStrengthModel.Strength"/> plus a
/// seeded ±jitter (race-day variance), highest first, ties broken by driver id, a pure function of
/// (masterSeed, year, round) + the resolved grid, so it re-derives byte-identically. The player is
/// EXCLUDED (they did not start). Deliberately a reusable generator, the seed of a future "simulate a
/// race I don't want to drive" feature.
/// </summary>
public static class AutoRaceModel
{
    /// <summary>±race-day noise added on top of pure seat strength before ranking.</summary>
    public const double JitterMagnitude = 0.25;

    /// <summary>The non-player field's finishing ORDER (driver ids, winner first) for a skipped round.
    /// The player seat (<see cref="GridSeat.IsPlayer"/>) is excluded, they sat it out (DNS).</summary>
    public static IReadOnlyList<string> ClassifiedOrder(
        IReadOnlyList<GridSeat> seats, ulong masterSeed, int year, int round)
    {
        var factory = new StreamFactory(masterSeed);
        return seats
            .Where(s => !s.IsPlayer)
            .Select(s => (
                s.DriverId,
                Effective: SeatStrengthModel.Strength(s) + Jitter(factory, year, round, s.DriverId)))
            .OrderByDescending(t => t.Effective)
            .ThenBy(t => t.DriverId, StringComparer.Ordinal)
            .Select(t => t.DriverId)
            .ToList();
    }

    private static double Jitter(StreamFactory factory, int year, int round, string driverId) =>
        (factory.CreateStream(CareerStreams.AutoRace, year, round, driverId).NextDouble() * 2.0 - 1.0)
        * JitterMagnitude;
}
