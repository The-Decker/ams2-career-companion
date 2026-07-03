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
}
