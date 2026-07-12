using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.ViewModels.Shell;

namespace Companion.Tests.ViewModels;

/// <summary>The starting-grid card projection — in particular the fix for the player's car preview: the
/// SMGP clean-swap player seat is stamped with the synthetic <c>driver.player-entrant</c> id, which has
/// no car art, so the card rendered a blank car. The VM now keys the player's car to the driver whose
/// car they actually took over (passed in), while every other card keys off its own seat driver.</summary>
public sealed class StartingGridViewModelTests
{
    private static readonly PackDriverRatings Ratings = new() { RaceSkill = 0.8, QualifyingSkill = 0.8 };

    private static GridSeat Seat(string id, string team, string livery, bool isPlayer) => new()
    {
        DriverId = id,
        DriverName = isPlayer ? "You" : id,
        TeamId = team,
        TeamName = team,
        Number = "1",
        Ams2LiveryName = livery,
        Ratings = Ratings,
        Reliability = 1.0,
        WeightScalar = 1.0,
        PowerScalar = 1.0,
        DragScalar = 1.0,
        IsPlayer = isPlayer,
    };

    [Fact]
    public void PlayerCard_KeysCarArtToTheTakenOverCar_NotTheSyntheticId()
    {
        var grid = new[]
        {
            Seat("driver.ayrton_senna", "team.madonna", "Madonna #1 A. Senna", isPlayer: false),
            Seat(RoundGridResolver.SyntheticPlayerDriverId, "team.lares", "Lares #23 P. Arai", isPlayer: true),
        };

        var vm = new StartingGridViewModel(
            grid, RoundGridResolver.SyntheticPlayerDriverId, sessionTitle: null,
            conditions: null, playerCarArtDriverId: "driver.park_arai");

        var player = vm.Slots.Single(s => s.IsPlayer);
        Assert.Equal("driver.park_arai", player.CarKey);            // the car they drive, not the synthetic id
        Assert.Equal("player.lares", player.PortraitKey);           // still the team-coloured player image

        var ai = vm.Slots.Single(s => !s.IsPlayer);
        Assert.Equal("driver.ayrton_senna", ai.CarKey);            // every other seat keys off its own driver
    }

    [Fact]
    public void PlayerCard_FallsBackToSeatId_WhenNoTakenOverCar()
    {
        // A custom own-entrant livery matching no pack entry: no car-art driver to borrow, so the card
        // keeps the seat id (still art-less, but no regression versus before the fix).
        var grid = new[] { Seat(RoundGridResolver.SyntheticPlayerDriverId, "team.independent", "My Custom Skin", isPlayer: true) };

        var vm = new StartingGridViewModel(
            grid, RoundGridResolver.SyntheticPlayerDriverId, sessionTitle: null,
            conditions: null, playerCarArtDriverId: null);

        Assert.Equal(RoundGridResolver.SyntheticPlayerDriverId, vm.Slots.Single().CarKey);
    }
}
