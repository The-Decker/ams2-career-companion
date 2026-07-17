using Companion.Core.Career;
using Companion.Tests.Career;

namespace Companion.Tests.Dynasty;

/// <summary>The competitiveness feedback (economy §6): the Dynasty development bonus lifts the
/// PLAYER's expected finish through the existing versioned expectation channel, the 0.0 default
/// reproduces every shipped formula bit-exactly, and the breakdown explains the term.</summary>
public sealed class DynastyDevelopmentExpectationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ZeroBonus_IsBitExactWithTheShippedOverload(int modelVersion)
    {
        var grid = CareerTestData.PlayerGrid();
        int index = SeatStrengthModel.PlayerSeatIndex(grid);
        Assert.Equal(
            SeatStrengthModel.ExpectedFinish(grid, index, priorOpi: 1.2, modelVersion),
            SeatStrengthModel.ExpectedFinish(grid, index, 1.2, modelVersion, null, 0.0));
    }

    /// <summary>The player behind an identical teammate on grid order: the tie-break costs them
    /// a place until development breaks the tie.</summary>
    private static Companion.Core.Grid.GridPlan LateSeatGrid() => CareerTestData.Grid(
        CareerTestData.Seat("driver.fast", "team.top", 0.85, power: 1.02, weight: 0.98, reliability: 0.95),
        CareerTestData.Seat("driver.mate", "team.mid", 0.70),
        CareerTestData.Seat(CareerTestData.PlayerDriverId, "team.mid", 0.70,
            isPlayer: true, livery: CareerTestData.PlayerLivery),
        CareerTestData.Seat("driver.slow", "team.min", 0.55, power: 0.97, weight: 1.02, reliability: 0.82));

    [Fact]
    public void DevelopmentBonus_LiftsThePlayerPastAnEqualTeammate()
    {
        var grid = LateSeatGrid();
        int index = SeatStrengthModel.PlayerSeatIndex(grid);

        int baseline = SeatStrengthModel.ExpectedFinish(grid, index, 0.0, 0);
        int developed = SeatStrengthModel.ExpectedFinish(grid, index, 0.0, 0, null, 0.03);

        Assert.Equal(3, baseline);
        Assert.Equal(2, developed);
    }

    [Fact]
    public void EnoughDevelopment_OutranksTheFasterCar()
    {
        var grid = CareerTestData.PlayerGrid();
        int index = SeatStrengthModel.PlayerSeatIndex(grid);

        // The fast seat carries a better car AND a better driver; a maxed development programme
        // (economy.json: 8 levels × 0.015 = 0.12) closes most of it, a huge bonus closes all.
        int maxed = SeatStrengthModel.ExpectedFinish(grid, index, 0.0, 0, null, 0.30);
        Assert.Equal(1, maxed);
    }

    [Fact]
    public void Breakdown_ExposesTheDevelopmentTerm()
    {
        var grid = CareerTestData.PlayerGrid();
        int index = SeatStrengthModel.PlayerSeatIndex(grid);

        var plain = SeatStrengthModel.Breakdown(grid, index, 0.0, 0);
        var developed = SeatStrengthModel.Breakdown(grid, index, 0.0, 0, null, 0.045);

        Assert.Equal(0.0, plain.DevelopmentAdjustment);
        Assert.Equal(0.045, developed.DevelopmentAdjustment);
        Assert.Equal(plain.TotalStrength + 0.045, developed.TotalStrength, 12);
        Assert.True(developed.ExpectedFinish <= plain.ExpectedFinish);
    }

    [Fact]
    public void RoundUpdate_ScoresAgainstTheDevelopedExpectation()
    {
        var grid = LateSeatGrid();
        var player = new PlayerCareerState
        {
            Reputation = 40.0,
            CurrentTeamId = "team.mid",
            LiveryName = CareerTestData.PlayerLivery,
        };

        RoundUpdateContext Context(double developmentStrength) => new()
        {
            Grid = grid,
            Player = player,
            PlayerTeamTier = 3,
            PlayerFinish = 2,
            HasTeammate = true,
            TeammateFinish = 3,
            SliderUsed = 95.0,
            PointsPositions = 6,
            Streams = new Companion.Core.Determinism.StreamFactory(42),
            IsChampionshipRound = true,
            IsPrimaryRace = true,
            DynastyDevelopmentStrength = developmentStrength,
        };

        var plain = RoundUpdate.Apply(Context(0.0));
        var developed = RoundUpdate.Apply(Context(0.03));

        Assert.Equal(3, plain.ExpectedFinish);
        Assert.Equal(2, developed.ExpectedFinish);
        // A P2 finish overperforms a P3 expectation but merely meets a P2 one — the developed
        // car raises the bar, so the same result banks less OPI.
        Assert.True(developed.Player.Opi < plain.Player.Opi);
    }
}
