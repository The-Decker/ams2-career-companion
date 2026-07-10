using System.Text.Json;
using Companion.Core.Grid;
using Companion.Core.Json;
using Companion.Core.Packs;
using Companion.Tests.ViewModels;

namespace Companion.Tests.Grid;

/// <summary>
/// M3 slice 3 — <see cref="RoundGridResolver"/>'s SMGP seat overrides, pure. A move transplants
/// the DRIVER side onto the target CAR; only closed permutations apply (anything else resolves
/// verbatim); the default arguments resolve byte-identically to a call without them.
/// </summary>
public sealed class SmgpSeatOverrideTests
{
    private const string SeatA = "Stock Livery #1";
    private const string SeatB = "Stock Livery #2";
    private const string SeatC = "Stock Livery #3";
    private const string SeatD = "Stock Livery #4";

    /// <summary>Four one-driver teams, entries in ladder order (the battle-fold pack's shape).</summary>
    private static SeasonPack LadderPack()
    {
        var basePack = TestPackBuilder.TwoRoundPack();
        return basePack with
        {
            Teams =
            [
                Team("team.a", 5), Team("team.b", 4), Team("team.c", 3), Team("team.d", 2),
            ],
            Drivers =
            [
                TestPackBuilder.Driver("driver.a"),
                TestPackBuilder.Driver("driver.b"),
                TestPackBuilder.Driver("driver.c"),
                TestPackBuilder.Driver("driver.d"),
            ],
            Entries =
            [
                TestPackBuilder.Entry("team.a", "driver.a", "1", SeatA),
                TestPackBuilder.Entry("team.b", "driver.b", "2", SeatB),
                TestPackBuilder.Entry("team.c", "driver.c", "3", SeatC),
                TestPackBuilder.Entry("team.d", "driver.d", "4", SeatD),
            ],
        };
    }

    private static PackTeam Team(string id, int prestige) => new()
    {
        Id = id,
        Name = id,
        CarVehicleIds = [TestPackBuilder.VintageCar],
        Reliability = 0.93,
        Prestige = prestige,
        BudgetTier = prestige,
    };

    [Fact]
    public void AClosedThreeSeatChain_MovesDrivers_AndKeepsCarsPut()
    {
        var grid = RoundGridResolver.Resolve(
            LadderPack(), 1, new PlayerSeat { Ams2LiveryName = SeatC },
            seatOverrides: new Dictionary<string, string>
            {
                ["driver.a"] = SeatD,
                ["driver.d"] = SeatC,
            },
            playerSeatOverride: SeatA);

        var player = grid.Seats.Single(s => s.IsPlayer);
        Assert.Equal(SeatA, player.Ams2LiveryName);
        Assert.Equal("driver.c", player.DriverId);
        Assert.Equal("team.a", player.TeamId);

        var rival = grid.Seats.Single(s => s.DriverId == "driver.a");
        Assert.Equal(SeatD, rival.Ams2LiveryName);
        Assert.Equal("team.d", rival.TeamId);
        Assert.False(rival.IsPlayer);

        var displaced = grid.Seats.Single(s => s.DriverId == "driver.d");
        Assert.Equal(SeatC, displaced.Ams2LiveryName);
        Assert.Equal("team.c", displaced.TeamId);

        // The uninvolved seat is untouched, and exactly one seat is the player's.
        var bystander = grid.Seats.Single(s => s.DriverId == "driver.b");
        Assert.Equal(SeatB, bystander.Ams2LiveryName);
        Assert.Single(grid.Seats, s => s.IsPlayer);
        Assert.Equal(4, grid.Seats.Count);
    }

    [Fact]
    public void ABrokenChain_ResolvesVerbatim()
    {
        // driver.a moves to Seat D but nothing backfills Seat A: NOT a permutation — applying it
        // would field driver.a twice. The resolver must return the plain grid.
        var grid = RoundGridResolver.Resolve(
            LadderPack(), 1, new PlayerSeat { Ams2LiveryName = SeatC },
            seatOverrides: new Dictionary<string, string> { ["driver.a"] = SeatD });

        Assert.Equal(SeatA, grid.Seats.Single(s => s.DriverId == "driver.a").Ams2LiveryName);
        Assert.Equal(SeatD, grid.Seats.Single(s => s.DriverId == "driver.d").Ams2LiveryName);
    }

    [Fact]
    public void DefaultArguments_ResolveByteIdentically()
    {
        var pack = LadderPack();
        var playerSeat = new PlayerSeat { Ams2LiveryName = SeatC };

        string plain = JsonSerializer.Serialize(
            RoundGridResolver.Resolve(pack, 1, playerSeat), CoreJson.Options);
        string defaulted = JsonSerializer.Serialize(
            RoundGridResolver.Resolve(pack, 1, playerSeat, seatOverrides: null, playerSeatOverride: null),
            CoreJson.Options);
        string samePlace = JsonSerializer.Serialize(
            RoundGridResolver.Resolve(pack, 1, playerSeat, playerSeatOverride: SeatC),
            CoreJson.Options);

        Assert.Equal(plain, defaulted);
        Assert.Equal(plain, samePlace); // the player already sits there — an identity move
    }
}
