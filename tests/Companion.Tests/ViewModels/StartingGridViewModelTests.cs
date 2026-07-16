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
            conditions: null, playerCarArtDriverId: "driver.park_arai",
            playerCountryFlagKey: "country.brazil");

        var player = vm.Slots.Single(s => s.IsPlayer);
        Assert.Equal("driver.park_arai", player.CarKey);            // the car they drive, not the synthetic id
        Assert.Equal("player.lares", player.PortraitKey);           // still the team-coloured player image
        Assert.Equal("country.brazil", player.CountryFlagKey);     // player country-keyed Brazil flag

        var ai = vm.Slots.Single(s => !s.IsPlayer);
        Assert.Equal("driver.ayrton_senna", ai.CarKey);            // every other seat keys off its own driver
        Assert.Equal("driver.ayrton_senna", ai.CountryFlagKey);    // AI keeps existing driver-key flag
    }

    [Fact]
    public void SmgpCarArt_FollowsTheFixedLivery_WhenDriversReshuffle()
    {
        var grid = new[]
        {
            // Park moved into Asselin's fixed Madonna #2 car for a later season.
            Seat("driver.park_arai", "team.madonna", "Madonna #2 A. Asselin", isPlayer: false),
            // The synthetic player now occupies Park's authored Lares #23 car.
            Seat(RoundGridResolver.SyntheticPlayerDriverId, "team.lares", "Lares #23 P. Arai", isPlayer: true),
        };
        var carArtByLivery = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Madonna #2 A. Asselin"] = "driver.alain_asselin",
            ["Lares #23 P. Arai"] = "driver.park_arai",
        };

        var vm = new StartingGridViewModel(
            grid, RoundGridResolver.SyntheticPlayerDriverId, sessionTitle: null,
            playerCarArtDriverId: "driver.wrong_runtime_donor",
            carArtKeyByLivery: carArtByLivery);

        Assert.Equal("driver.alain_asselin", vm.Slots.Single(s => !s.IsPlayer).CarKey);
        Assert.Equal("driver.park_arai", vm.Slots.Single(s => s.IsPlayer).CarKey);
    }

    [Fact]
    public void DnqStrip_SurfacesTheNonQualifiers_FastestFirstAsGiven()
    {
        var grid = new[] { Seat("driver.a", "team.x", "Livery A", isPlayer: false) };
        var dnq = new[]
        {
            new StartingGridDnq("Paul White", "Blanche", "10"),
            new StartingGridDnq("Paul Klinger", "Zeroforce", "32"),
        };
        var vm = new StartingGridViewModel(grid, "driver.a", sessionTitle: null,
            conditions: null, playerCarArtDriverId: null, dnq: dnq);

        Assert.True(vm.HasDnq);
        Assert.Equal("DID NOT QUALIFY · 2", vm.DnqHeader);
        Assert.Equal(new[] { "PAUL WHITE", "PAUL KLINGER" }, vm.Dnq.Select(d => d.NameUpper));
    }

    [Fact]
    public void NoDnq_HidesTheStrip()
    {
        var vm = new StartingGridViewModel(
            new[] { Seat("driver.a", "team.x", "Livery A", isPlayer: true) }, "driver.a", sessionTitle: null);

        Assert.False(vm.HasDnq);
        Assert.Empty(vm.Dnq);
        Assert.Null(vm.Slots.Single().CountryFlagKey); // no donor flag for a legacy player
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
