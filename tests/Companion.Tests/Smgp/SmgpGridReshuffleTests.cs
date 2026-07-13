using Companion.Core.Numerics;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.Core.Smgp;
using Companion.Tests.ViewModels;

namespace Companion.Tests.Smgp;

public sealed class SmgpGridReshuffleTests
{
    [Fact]
    public void PreviousStandings_AssignBestMovableDriverToBestAvailableCar()
    {
        var source = TestPackBuilder.TwoRoundPack();
        var pack = source with
        {
            Manifest = source.Manifest with { CareerStyle = SmgpRules.CareerStyle },
            Teams =
            [
                Team("team.madonna", 5), Team("team.b", 4), Team("team.c", 3), Team("team.d", 2),
            ],
            Drivers =
            [
                TestPackBuilder.Driver(SmgpGridReshuffle.BenchmarkDriverId),
                TestPackBuilder.Driver("driver.b"), TestPackBuilder.Driver("driver.c"),
                TestPackBuilder.Driver("driver.d"),
            ],
            Entries =
            [
                TestPackBuilder.Entry("team.madonna", SmgpGridReshuffle.BenchmarkDriverId, "1", "Madonna #1"),
                TestPackBuilder.Entry("team.b", "driver.b", "2", "Seat B"),
                TestPackBuilder.Entry("team.c", "driver.c", "3", "Player Seat"),
                TestPackBuilder.Entry("team.d", "driver.d", "4", "Seat D"),
            ],
        };
        var final = new StandingsSnapshot
        {
            AfterRound = 2,
            Drivers =
            [
                Standing("driver.player-entrant", 1, 18),
                Standing(SmgpGridReshuffle.BenchmarkDriverId, 2, 12),
                Standing("driver.d", 3, 8),
                Standing("driver.b", 4, 4),
            ],
        };

        var shuffled = SmgpGridReshuffle.ForNextSeason(pack, final, "Player Seat");

        Assert.Equal(SmgpGridReshuffle.BenchmarkDriverId,
            shuffled.Entries.Single(entry => entry.Ams2LiveryName == "Madonna #1").DriverId);
        Assert.Equal("driver.d",
            shuffled.Entries.Single(entry => entry.Ams2LiveryName == "Seat B").DriverId);
        Assert.Equal("driver.c",
            shuffled.Entries.Single(entry => entry.Ams2LiveryName == "Player Seat").DriverId);
        Assert.Equal("driver.b",
            shuffled.Entries.Single(entry => entry.Ams2LiveryName == "Seat D").DriverId);
    }

    [Fact]
    public void NonSmgpPack_IsUnchanged()
    {
        var pack = TestPackBuilder.TwoRoundPack();
        var final = new StandingsSnapshot { AfterRound = 1, Drivers = [] };
        Assert.Same(pack, SmgpGridReshuffle.ForNextSeason(pack, final, "none"));
    }

    private static PackTeam Team(string id, int prestige) => new()
    {
        Id = id,
        Name = id,
        CarVehicleIds = [TestPackBuilder.VintageCar],
        Prestige = prestige,
        BudgetTier = prestige,
    };

    private static DriverStanding Standing(string driverId, int position, int points) => new()
    {
        DriverId = driverId,
        Position = position,
        GrossPoints = new Rational(points),
        CountedPoints = new Rational(points),
        RoundScores = [],
        Dropped = [],
    };
}
