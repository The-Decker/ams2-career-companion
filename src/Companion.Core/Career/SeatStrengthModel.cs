using Companion.Core.Grid;

namespace Companion.Core.Career;

/// <summary>
/// Pure explanation of the player's expected-finish strength. The contribution fields add to
/// <see cref="TotalStrength"/> and make the model legible without duplicating its arithmetic in a
/// view model. <see cref="PerformanceAdjustment"/> is applied to race skill before the 30% driver
/// weight; it is zero for the legacy model.
/// </summary>
public sealed record SeatExpectationBreakdown
{
    public required int ModelVersion { get; init; }

    public required int ExpectedFinish { get; init; }

    /// <summary>The scalar-authored car score before any team-tier fallback.</summary>
    public required double BaseCarScore { get; init; }

    /// <summary>The folded team tier used when the pack has no authored car hierarchy.</summary>
    public required int TeamTier { get; init; }

    public required bool UsesTeamTierFallback { get; init; }

    public required double TeamTierAdjustment { get; init; }

    /// <summary>The effective car score after the optional team-tier fallback.</summary>
    public required double CarScore { get; init; }

    public required double CarContribution { get; init; }

    public required double Reliability { get; init; }

    public required double ReliabilityContribution { get; init; }

    public required double BaseRaceSkill { get; init; }

    /// <summary>The already-folded performance history used for this expectation.</summary>
    public required double PriorOpi { get; init; }

    public required double PerformanceAdjustment { get; init; }

    public required double AdjustedRaceSkill { get; init; }

    public required double DriverContribution { get; init; }

    public required double TotalStrength { get; init; }

    /// <summary>The Dynasty car-development bonus added to the player's total strength
    /// (docs/dev/dynasty-tycoon-economy.md §6). 0 for every non-economy career.</summary>
    public double DevelopmentAdjustment { get; init; }

    /// <summary>The team-controlled car and reliability contribution (70% of the model).</summary>
    public double TeamContribution => CarContribution + ReliabilityContribution;
}

/// <summary>
/// Car+driver strength ranking over a resolved <see cref="GridPlan"/> — the source of
/// "expected finish" for OPI and the pace anchor. Car strength comes from the team's tier
/// scalar band (power/weight/drag, 1.0-neutral, authored per era) plus reliability; driver
/// strength is the seat's merged raceSkill. The car is weighted heavier than the driver
/// ("your tier-4 car really is slower" — PLAN.md).
/// </summary>
public static class SeatStrengthModel
{
    public const int TeamAndPerformanceVersion = 1;

    /// <summary>
    /// Keeps the version-1 folded-performance adjustment and, when a pack authors every AI car at
    /// the same neutral scalar, uses the season's folded team tiers as the missing car hierarchy.
    /// </summary>
    public const int TierFallbackVersion = 2;

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

    /// <summary>
    /// Versioned player-seat strength. Version 0 is the shipped formula exactly. Versions 1 and 2
    /// adjust the 30% driver component from folded performance history. Version 2 can additionally
    /// apply a team-tier fallback to the 60% car component when the pack omitted a car hierarchy.
    /// </summary>
    public static double Strength(
        GridSeat seat,
        double priorOpi,
        int modelVersion,
        int teamTier = 3,
        bool useTeamTierFallback = false) =>
        modelVersion switch
        {
            0 => Strength(seat),
            TeamAndPerformanceVersion =>
                0.6 * CarScore(seat)
                + 0.3 * AdjustedRaceSkill(seat, priorOpi)
                + 0.1 * seat.Reliability,
            TierFallbackVersion =>
                0.6 * EffectiveCarScore(seat, teamTier, useTeamTierFallback)
                + 0.3 * AdjustedRaceSkill(seat, priorOpi)
                + 0.1 * seat.Reliability,
            _ => throw UnsupportedModelVersion(modelVersion),
        };

    /// <summary>Expected finish of one seat: its 1-based rank by <see cref="Strength(GridSeat)"/>
    /// among every seat of the round's grid. Ties resolve by grid order (earlier seat ranks better),
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

    /// <summary>
    /// Versioned expected finish for the player seat. Only that seat receives the performance
    /// adjustment. Version 2 also applies the same folded team-tier fallback to every opponent when
    /// the pack's non-player cars are all neutral, so a Level-D car cannot rank like a front-runner.
    /// <paramref name="playerStrengthBonus"/> is the Dynasty car-development term (economy §6),
    /// added to the PLAYER's strength only in every version arm; the 0.0 default reproduces every
    /// shipped formula exactly, so non-economy careers are byte-identical.
    /// </summary>
    public static int ExpectedFinish(
        GridPlan grid,
        int seatIndex,
        double priorOpi,
        int modelVersion,
        IReadOnlyDictionary<string, int>? teamTiers = null,
        double playerStrengthBonus = 0.0)
    {
        if (seatIndex < 0 || seatIndex >= grid.Seats.Count)
            throw new ArgumentOutOfRangeException(nameof(seatIndex));

        bool useTierFallback =
            modelVersion == TierFallbackVersion && NeedsTeamTierFallback(grid, seatIndex);
        GridSeat player = grid.Seats[seatIndex];
        double strength = Strength(
            player,
            priorOpi,
            modelVersion,
            TeamTier(player, teamTiers),
            useTierFallback) + playerStrengthBonus;
        int rank = 1;
        for (int i = 0; i < grid.Seats.Count; i++)
        {
            if (i == seatIndex)
                continue;
            GridSeat opponent = grid.Seats[i];
            double other = modelVersion == TierFallbackVersion
                ? Strength(
                    opponent,
                    0.0,
                    modelVersion,
                    TeamTier(opponent, teamTiers),
                    useTierFallback)
                : Strength(opponent);
            if (other > strength || (other == strength && i < seatIndex))
                rank++;
        }
        return rank;
    }

    /// <summary>Returns the exact components used by the versioned expected-finish ranker.</summary>
    public static SeatExpectationBreakdown Breakdown(
        GridPlan grid,
        int seatIndex,
        double priorOpi,
        int modelVersion,
        IReadOnlyDictionary<string, int>? teamTiers = null,
        double playerStrengthBonus = 0.0)
    {
        if (seatIndex < 0 || seatIndex >= grid.Seats.Count)
            throw new ArgumentOutOfRangeException(nameof(seatIndex));

        GridSeat seat = grid.Seats[seatIndex];
        double baseCarScore = CarScore(seat);
        int teamTier = TeamTier(seat, teamTiers);
        bool useTierFallback =
            modelVersion == TierFallbackVersion && NeedsTeamTierFallback(grid, seatIndex);
        double tierAdjustment = useTierFallback ? TeamTierAdjustment(teamTier) : 0.0;
        double carScore = EffectiveCarScore(seat, teamTier, useTierFallback);
        double performanceAdjustment = modelVersion switch
        {
            0 => 0.0,
            TeamAndPerformanceVersion => PerformanceAdjustment(priorOpi),
            TierFallbackVersion => PerformanceAdjustment(priorOpi),
            _ => throw UnsupportedModelVersion(modelVersion),
        };
        double adjustedRaceSkill = modelVersion == 0
            ? seat.Ratings.RaceSkill
            : Math.Clamp(seat.Ratings.RaceSkill + performanceAdjustment, 0.0, 1.0);
        double carContribution = 0.6 * carScore;
        double reliabilityContribution = 0.1 * seat.Reliability;
        double driverContribution = 0.3 * adjustedRaceSkill;

        return new SeatExpectationBreakdown
        {
            ModelVersion = modelVersion,
            ExpectedFinish = ExpectedFinish(
                grid, seatIndex, priorOpi, modelVersion, teamTiers, playerStrengthBonus),
            BaseCarScore = baseCarScore,
            TeamTier = teamTier,
            UsesTeamTierFallback = useTierFallback,
            TeamTierAdjustment = tierAdjustment,
            CarScore = carScore,
            CarContribution = carContribution,
            Reliability = seat.Reliability,
            ReliabilityContribution = reliabilityContribution,
            BaseRaceSkill = seat.Ratings.RaceSkill,
            PriorOpi = priorOpi,
            PerformanceAdjustment = performanceAdjustment,
            AdjustedRaceSkill = adjustedRaceSkill,
            DriverContribution = driverContribution,
            TotalStrength = carContribution + driverContribution + reliabilityContribution
                + playerStrengthBonus,
            DevelopmentAdjustment = playerStrengthBonus,
        };
    }

    /// <summary>Expected finish of the player's seat (the grid must contain one).</summary>
    public static int ExpectedPlayerFinish(GridPlan grid)
    {
        int index = PlayerSeatIndex(grid);
        return ExpectedFinish(grid, index);
    }

    public static int ExpectedPlayerFinish(
        GridPlan grid,
        double priorOpi,
        int modelVersion,
        IReadOnlyDictionary<string, int>? teamTiers = null,
        double playerStrengthBonus = 0.0)
    {
        int index = PlayerSeatIndex(grid);
        return ExpectedFinish(grid, index, priorOpi, modelVersion, teamTiers, playerStrengthBonus);
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

    private static double PerformanceAdjustment(double priorOpi) =>
        0.02 * Math.Clamp(priorOpi, -5.0, 5.0);

    private static double AdjustedRaceSkill(GridSeat seat, double priorOpi) =>
        Math.Clamp(seat.Ratings.RaceSkill + PerformanceAdjustment(priorOpi), 0.0, 1.0);

    /// <summary>
    /// A neutral-scalar pack has not authored a meaningful car ladder. Ignore the player because a
    /// character perk may legitimately move only their scalar away from neutral; the untouched AI
    /// field is the reliable signal that the pack needs the folded team-tier fallback.
    /// </summary>
    private static bool NeedsTeamTierFallback(GridPlan grid, int playerIndex)
    {
        bool foundOpponent = false;
        for (int i = 0; i < grid.Seats.Count; i++)
        {
            if (i == playerIndex)
                continue;
            foundOpponent = true;
            if (Math.Abs(CarScore(grid.Seats[i]) - 0.5) > 1e-9)
                return false;
        }
        return foundOpponent;
    }

    private static int TeamTier(
        GridSeat seat,
        IReadOnlyDictionary<string, int>? teamTiers) =>
        teamTiers is not null && teamTiers.TryGetValue(seat.TeamId, out int tier)
            ? Math.Clamp(tier, 1, 5)
            : 3;

    /// <summary>
    /// Tier 3 is the neutral car. Each folded tier is two tenths of normalized car score, making
    /// the 60% car component decisive without erasing driver skill inside the same level.
    /// </summary>
    private static double TeamTierAdjustment(int teamTier) =>
        0.2 * (Math.Clamp(teamTier, 1, 5) - 3);

    private static double EffectiveCarScore(
        GridSeat seat,
        int teamTier,
        bool useTeamTierFallback) =>
        Math.Clamp(
            CarScore(seat) + (useTeamTierFallback ? TeamTierAdjustment(teamTier) : 0.0),
            0.0,
            1.0);

    private static ArgumentOutOfRangeException UnsupportedModelVersion(int modelVersion) =>
        new(
            nameof(modelVersion),
            modelVersion,
            $"Unsupported seat-expectation model version {modelVersion}; supported versions are 0, " +
            $"{TeamAndPerformanceVersion}, and {TierFallbackVersion}.");
}
