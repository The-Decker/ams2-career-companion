using Companion.Core.Career;

namespace Companion.Tests.Career;

public class SeatStrengthTests
{
    [Fact]
    public void FasterCarBeatsBetterDriverInATierGap()
    {
        // Tier-5 car (1.02/0.98) with a modest driver vs neutral car with a great driver:
        // the car weighting must dominate — "your tier-4 car really is slower".
        var strongCar = CareerTestData.Seat("d1", "t1", raceSkill: 0.70, power: 1.02, weight: 0.98);
        var strongDriver = CareerTestData.Seat("d2", "t2", raceSkill: 0.85);
        Assert.True(
            SeatStrengthModel.Strength(strongCar) > SeatStrengthModel.Strength(strongDriver));
    }

    [Fact]
    public void ExpectedFinishRanksByStrength()
    {
        var grid = CareerTestData.PlayerGrid();
        // Seats: fast car+driver, player (neutral), equal teammate, slow car.
        Assert.Equal(1, SeatStrengthModel.ExpectedFinish(grid, 0));
        Assert.Equal(2, SeatStrengthModel.ExpectedFinish(grid, 1));
        Assert.Equal(4, SeatStrengthModel.ExpectedFinish(grid, 3));
    }

    [Fact]
    public void TiesResolveByGridOrder()
    {
        var grid = CareerTestData.PlayerGrid();
        // Player (index 1) and teammate (index 2) are identical seats: earlier index wins.
        Assert.Equal(2, SeatStrengthModel.ExpectedFinish(grid, 1));
        Assert.Equal(3, SeatStrengthModel.ExpectedFinish(grid, 2));
    }

    [Fact]
    public void ExpectedPlayerFinishFindsThePlayerSeat()
    {
        Assert.Equal(2, SeatStrengthModel.ExpectedPlayerFinish(CareerTestData.PlayerGrid()));
    }

    [Fact]
    public void GridWithoutPlayerSeatIsRejected()
    {
        var grid = CareerTestData.Grid(CareerTestData.Seat("d1", "t1", 0.7));
        Assert.Throws<InvalidOperationException>(() => SeatStrengthModel.ExpectedPlayerFinish(grid));
    }

    [Fact]
    public void ReliabilityBreaksOtherwiseEqualSeats()
    {
        var reliable = CareerTestData.Seat("d1", "t1", 0.7, reliability: 0.95);
        var fragile = CareerTestData.Seat("d2", "t2", 0.7, reliability: 0.80);
        Assert.True(SeatStrengthModel.Strength(reliable) > SeatStrengthModel.Strength(fragile));
    }

    [Fact]
    public void VersionZeroIsTheExactLegacyFormulaRegardlessOfOpi()
    {
        var grid = CareerTestData.PlayerGrid();
        int playerIndex = SeatStrengthModel.PlayerSeatIndex(grid);
        var playerSeat = grid.Seats[playerIndex];

        Assert.Equal(
            SeatStrengthModel.Strength(playerSeat),
            SeatStrengthModel.Strength(playerSeat, priorOpi: 99.0, modelVersion: 0));
        Assert.Equal(
            SeatStrengthModel.ExpectedFinish(grid, playerIndex),
            SeatStrengthModel.ExpectedFinish(grid, playerIndex, priorOpi: -99.0, modelVersion: 0));
    }

    [Fact]
    public void TeamAndPerformanceBreakdownKeepsTheAuthoredWeightsAndClampsHistory()
    {
        var grid = CareerTestData.PlayerGrid();
        int playerIndex = SeatStrengthModel.PlayerSeatIndex(grid);

        var breakdown = SeatStrengthModel.Breakdown(
            grid, playerIndex, priorOpi: 99.0,
            modelVersion: SeatStrengthModel.TeamAndPerformanceVersion);

        Assert.Equal(SeatStrengthModel.TeamAndPerformanceVersion, breakdown.ModelVersion);
        Assert.Equal(0.5, breakdown.BaseCarScore, 12);
        Assert.Equal(3, breakdown.TeamTier);
        Assert.False(breakdown.UsesTeamTierFallback);
        Assert.Equal(0.0, breakdown.TeamTierAdjustment, 12);
        Assert.Equal(0.5, breakdown.CarScore, 12);
        Assert.Equal(0.3, breakdown.CarContribution, 12);
        Assert.Equal(0.9, breakdown.Reliability, 12);
        Assert.Equal(0.09, breakdown.ReliabilityContribution, 12);
        Assert.Equal(0.7, breakdown.BaseRaceSkill, 12);
        Assert.Equal(99.0, breakdown.PriorOpi, 12);
        Assert.Equal(0.1, breakdown.PerformanceAdjustment, 12);
        Assert.Equal(0.8, breakdown.AdjustedRaceSkill, 12);
        Assert.Equal(0.24, breakdown.DriverContribution, 12);
        Assert.Equal(0.63, breakdown.TotalStrength, 12);
        Assert.Equal(0.39, breakdown.TeamContribution, 12);
        Assert.Equal(
            SeatStrengthModel.ExpectedFinish(
                grid, playerIndex, 99.0, SeatStrengthModel.TeamAndPerformanceVersion),
            breakdown.ExpectedFinish);
    }

    [Fact]
    public void PerformanceHistoryCannotOverturnAClearTeamTierGap()
    {
        var strongTeamPlayer = CareerTestData.Seat(
            "player", "strong", raceSkill: 0.50, power: 1.02, weight: 0.98,
            reliability: 0.90, isPlayer: true);
        var weakTeamStar = CareerTestData.Seat(
            "star", "weak", raceSkill: 1.0, reliability: 0.90);
        var grid = CareerTestData.Grid(strongTeamPlayer, weakTeamStar);

        Assert.Equal(
            1,
            SeatStrengthModel.ExpectedPlayerFinish(
                grid, priorOpi: -5.0,
                modelVersion: SeatStrengthModel.TeamAndPerformanceVersion));
    }

    [Fact]
    public void VersionOneUsesPriorPerformanceButDoesNotAdjustOpponents()
    {
        var grid = CareerTestData.PlayerGrid();
        int playerIndex = SeatStrengthModel.PlayerSeatIndex(grid);

        Assert.Equal(2, SeatStrengthModel.ExpectedFinish(grid, playerIndex));
        Assert.Equal(
            3,
            SeatStrengthModel.ExpectedFinish(
                grid, playerIndex, priorOpi: -5.0,
                modelVersion: SeatStrengthModel.TeamAndPerformanceVersion));
        Assert.Equal(
            2,
            SeatStrengthModel.ExpectedFinish(
                grid, playerIndex, priorOpi: 5.0,
                modelVersion: SeatStrengthModel.TeamAndPerformanceVersion));
    }

    [Fact]
    public void UnknownExpectationVersionIsRejected()
    {
        var grid = CareerTestData.PlayerGrid();
        int playerIndex = SeatStrengthModel.PlayerSeatIndex(grid);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SeatStrengthModel.ExpectedFinish(grid, playerIndex, priorOpi: 0.0, modelVersion: 3));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SeatStrengthModel.Breakdown(grid, playerIndex, priorOpi: 0.0, modelVersion: -1));
    }

    [Fact]
    public void TierFallbackKeepsAProdigyInALevelDCarBehindLevelsABC()
    {
        var levelA = CareerTestData.Seat(
            "a", "team.a", raceSkill: 0.90, reliability: 0.92);
        var levelB = CareerTestData.Seat(
            "b", "team.b", raceSkill: 0.85, reliability: 0.88);
        var levelC = CareerTestData.Seat(
            "c", "team.c", raceSkill: 0.80, reliability: 0.84);
        var levelDPlayer = CareerTestData.Seat(
            "player", "team.d", raceSkill: 0.771, power: 1.012,
            reliability: 0.80, isPlayer: true);
        var levelDOpponent = CareerTestData.Seat(
            "d", "team.d2", raceSkill: 0.79, reliability: 0.80);
        var grid = CareerTestData.Grid(levelA, levelB, levelC, levelDPlayer, levelDOpponent);
        int playerIndex = SeatStrengthModel.PlayerSeatIndex(grid);
        var tiers = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["team.a"] = 5,
            ["team.b"] = 4,
            ["team.c"] = 3,
            ["team.d"] = 2,
            ["team.d2"] = 2,
        };

        Assert.Equal(
            1,
            SeatStrengthModel.ExpectedFinish(
                grid, playerIndex, priorOpi: 0.0,
                modelVersion: SeatStrengthModel.TeamAndPerformanceVersion));
        Assert.Equal(
            4,
            SeatStrengthModel.ExpectedFinish(
                grid, playerIndex, priorOpi: 0.0,
                modelVersion: SeatStrengthModel.TierFallbackVersion,
                teamTiers: tiers));

        SeatExpectationBreakdown breakdown = SeatStrengthModel.Breakdown(
            grid, playerIndex, priorOpi: 0.0,
            modelVersion: SeatStrengthModel.TierFallbackVersion,
            teamTiers: tiers);
        Assert.True(breakdown.UsesTeamTierFallback);
        Assert.Equal(2, breakdown.TeamTier);
        Assert.Equal(0.62, breakdown.BaseCarScore, 12);
        Assert.Equal(-0.2, breakdown.TeamTierAdjustment, 12);
        Assert.Equal(0.42, breakdown.CarScore, 12);
    }
}
