using Companion.Ams2.Skins;
using Companion.Core.Grid;
using Companion.ViewModels.Hub;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>The read-only, selectable pre-qualifying Grid Preview contract.</summary>
public sealed class SkinsViewModelTests
{
    [Fact]
    public void ProjectsStatusIdentityAndAllFourPreviewAssetKeys()
    {
        var session = new FakeCareerSession
        {
            SkinPlan = Plan(
                Assign(
                    "A. Custom", "Team A", "Livery A", SkinStatus.CustomSkin,
                    folder: "formula_a", driverId: "driver.custom", teamId: "team.a",
                    number: "7", skinSlot: "61"),
                Assign(
                    "Player", "Brabham-Repco", TestPackBuilder.StockLivery2, SkinStatus.NameOnly,
                    isPlayer: true, driverId: RoundGridResolver.SyntheticPlayerDriverId,
                    teamId: "team.brabham", number: "2")),
        };

        var vm = new SkinsViewModel(session);

        Assert.Equal(2, vm.Cars.Count);
        var custom = vm.Cars[0];
        Assert.Equal(SkinTone.Good, custom.Tone);
        Assert.Equal("Custom skin", custom.StatusLabel);
        Assert.Contains("formula_a", custom.Detail);
        Assert.Equal("driver.custom", custom.PortraitKey);
        Assert.Equal("driver.custom", custom.CarKey);
        Assert.Equal(custom.CarKey, custom.TopCarKey);
        Assert.Equal("team.a", custom.TeamLogoKey);

        var player = vm.Cars[1];
        Assert.Equal("player.brabham", player.PortraitKey);
        Assert.Equal("driver.hulme", player.CarKey);
        Assert.Equal("team.brabham", player.TeamLogoKey);
        Assert.Same(player, vm.SelectedCar);
        Assert.Equal("CAR 2 OF 2", vm.SelectedPositionLabel);
    }

    [Fact]
    public void PreviousAndNextCycleThroughEveryGridCarWithoutAnEndpoint()
    {
        var vm = new SkinsViewModel(new FakeCareerSession
        {
            SkinPlan = Plan(
                Assign("One", "A", "L1", SkinStatus.CustomSkin, driverId: "driver.one"),
                Assign("Two", "B", "L2", SkinStatus.CustomSkin, isPlayer: true, driverId: "driver.two"),
                Assign("Three", "C", "L3", SkinStatus.StockDefault, driverId: "driver.three")),
        });

        Assert.Equal("Two", vm.SelectedCar!.DriverName);

        vm.NextCarCommand.Execute(null);
        Assert.Equal("Three", vm.SelectedCar.DriverName);
        vm.NextCarCommand.Execute(null);
        Assert.Equal("One", vm.SelectedCar.DriverName);

        vm.PreviousCarCommand.Execute(null);
        Assert.Equal("Three", vm.SelectedCar.DriverName);
    }

    [Fact]
    public void CurrentRoundDnqIsProjectedAsAQuietSeparateGroup()
    {
        var session = new FakeCareerSession
        {
            SkinPlan = Plan(Assign("Starter", "Grid Team", "Grid Livery", SkinStatus.CustomSkin)),
        };
        session.ScheduleEntries.Add(new SeasonScheduleEntry
        {
            Round = 1,
            Name = "Round 1",
            Date = "1988-01-01",
            RealVenue = "Monaco",
            Ams2TrackName = "Azure",
            Laps = 40,
            Kind = SeasonTrackKind.RealVenue,
            Dnq =
            [
                new ScheduleDnqEntry("Paul White", "Blanche", "10"),
                new ScheduleDnqEntry("Paul Klinger", "Zeroforce", "32"),
            ],
        });

        var vm = new SkinsViewModel(session);

        Assert.True(vm.HasDnq);
        Assert.Equal("DID NOT QUALIFY  •  2", vm.DnqHeader);
        Assert.Equal("#10", vm.DidNotQualify[0].NumberLabel);
        Assert.Equal("Paul White", vm.DidNotQualify[0].DriverName);
        Assert.Contains("2 missed the cut", vm.Summary);
    }

    [Fact]
    public void RefreshIsReadOnlyAndDoesNotTouchLegacyWorkshopMutationSeams()
    {
        var session = new FakeCareerSession
        {
            SkinPlan = Plan(Assign("Starter", "Team", "Livery", SkinStatus.InstalledInactive)),
        };
        session.Overrides["Livery"] = new SeatStagingOverride { DriverName = "Existing override" };
        session.StageOutcomes.Enqueue(new StageOutcome { Success = true, Messages = ["unused"] });

        var vm = new SkinsViewModel(session);
        vm.Refresh();

        Assert.Empty(session.ActivatedLiveries);
        Assert.Equal("Existing override", session.Overrides["Livery"].DriverName);
        Assert.Single(session.StageOutcomes);

        string[] forbiddenMembers =
        [
            "Editors",
            "SelectedEditor",
            "ActivateLiveryCommand",
            "StageGridCommand",
            "ForceStageGridCommand",
            "CopyPlayerLiveryCommand",
            "CopyLiveryCommand",
        ];
        var publicMembers = typeof(SkinsViewModel).GetMembers().Select(member => member.Name).ToHashSet();
        Assert.All(forbiddenMembers, name => Assert.DoesNotContain(name, publicMembers));
    }

    [Fact]
    public void EmptyPlanHasNoSelectionOrDnq()
    {
        var vm = new SkinsViewModel(new FakeCareerSession
        {
            SkinPlan = SkinAssignmentPlan.Empty,
        });

        Assert.Empty(vm.Cars);
        Assert.Null(vm.SelectedCar);
        Assert.False(vm.HasSelectedCar);
        Assert.False(vm.HasDnq);
        Assert.Equal("", vm.GridLabel);
    }

    private static SkinAssignment Assign(
        string driver,
        string team,
        string livery,
        SkinStatus status,
        bool isPlayer = false,
        string? folder = null,
        string? nearMiss = null,
        string driverId = "",
        string teamId = "",
        string? number = null,
        string skinSlot = "") => new()
    {
        DriverId = driverId,
        DriverName = driver,
        TeamId = teamId,
        TeamName = team,
        Number = number,
        SkinSlot = skinSlot,
        LiveryName = livery,
        IsPlayer = isPlayer,
        Status = status,
        VehicleFolder = folder,
        NearMiss = nearMiss,
    };

    private static SkinAssignmentPlan Plan(params SkinAssignment[] assignments) => new()
    {
        Ams2Class = "F-Classic_Gen3",
        Assignments = assignments,
    };
}
