using Companion.Ams2.Skins;
using Companion.Core.Packs;
using Companion.ViewModels.Hub;

namespace Companion.Tests.ViewModels;

/// <summary>The Skins lens viewmodel: projects the session's read-only skin picture into legible
/// per-car rows plus the player's-own-car crib, and copies the exact livery NAME to pick in-game.</summary>
public sealed class SkinsViewModelTests
{
    [Fact]
    public void ProjectsEachStatusIntoARowWithLabelAndTone()
    {
        var session = new FakeCareerSession
        {
            SkinPlan = Plan(
                Assign("A. Custom", "Team A", "Livery A", SkinStatus.CustomSkin, folder: "brabham_bt26"),
                Assign("B. Stock", "Team B", "Livery B", SkinStatus.StockDefault),
                Assign("C. NameOnly", "Team C", "Livery C", SkinStatus.NameOnly),
                Assign("D. Bogus", "Team D", "Livery D", SkinStatus.Unbound, nearMiss: "Livery d")),
        };

        var vm = new SkinsViewModel(session);

        Assert.Equal(4, vm.Cars.Count);
        Assert.Equal(SkinTone.Good, vm.Cars[0].Tone);
        Assert.Equal("Custom skin", vm.Cars[0].StatusLabel);
        Assert.Contains("brabham_bt26", vm.Cars[0].Detail);
        Assert.Equal(SkinTone.Neutral, vm.Cars[1].Tone);
        Assert.Equal(SkinTone.Neutral, vm.Cars[2].Tone);
        Assert.Equal(SkinTone.Warn, vm.Cars[3].Tone);
        Assert.Contains("Livery d", vm.Cars[3].Detail); // near-miss surfaced
        Assert.True(vm.HasUnbound);
        Assert.True(vm.HasMissingSkins);
    }

    [Fact]
    public void SurfacesThePlayerCarCrib()
    {
        var session = new FakeCareerSession
        {
            SkinPlan = Plan(
                Assign("Rival", "Team A", "Rival Livery", SkinStatus.CustomSkin),
                Assign("Nova Reyes", "Team B", "Player Livery", SkinStatus.CustomSkin, isPlayer: true)),
        };

        var vm = new SkinsViewModel(session);

        Assert.True(vm.HasPlayerCar);
        Assert.Equal("Player Livery", vm.PlayerLiveryName);
        Assert.Equal("Nova Reyes", vm.PlayerDriverName);
        Assert.Null(vm.PlayerStatusNote); // custom skin installed → no "will look default" caveat
    }

    [Fact]
    public void PlayerCarWithoutSkin_WarnsItWillLookDefault()
    {
        var session = new FakeCareerSession
        {
            SkinPlan = Plan(Assign("You", "Team B", "Player Livery", SkinStatus.NameOnly, isPlayer: true)),
        };

        var vm = new SkinsViewModel(session);

        Assert.True(vm.HasPlayerCar);
        Assert.NotNull(vm.PlayerStatusNote);
    }

    [Fact]
    public void NoPlayerSeat_HasNoCrib()
    {
        var session = new FakeCareerSession
        {
            SkinPlan = Plan(Assign("Only AI", "Team A", "AI Livery", SkinStatus.CustomSkin)),
        };

        var vm = new SkinsViewModel(session);

        Assert.False(vm.HasPlayerCar);
        Assert.Null(vm.PlayerLiveryName);
    }

    [Fact]
    public void CopyPlayerLivery_RaisesCopyRequestedWithTheExactName()
    {
        var session = new FakeCareerSession
        {
            SkinPlan = Plan(Assign("You", "Team B", "Exact Livery #7", SkinStatus.CustomSkin, isPlayer: true)),
        };
        var vm = new SkinsViewModel(session);

        string? copied = null;
        vm.CopyRequested += (_, text) => copied = text;
        vm.CopyPlayerLiveryCommand.Execute(null);

        Assert.Equal("Exact Livery #7", copied);
    }

    [Fact]
    public void SurfacesRequiredSkinPacksFromTheManifest()
    {
        var pack = TestPackBuilder.TwoRoundPack();
        pack = pack with
        {
            Manifest = pack.Manifest with
            {
                Requires = new PackRequirements
                {
                    SkinPacks = [new PackSkinPackRequirement
                    {
                        Name = "F1 1969 Season (Alain Fry)",
                        Url = "https://overtake.gg/example",
                        OverridesFolder = "F1_Season_1969",
                    }],
                },
            },
        };

        var session = new FakeCareerSession
        {
            Pack = pack,
            SkinPlan = Plan(Assign("Default Car", "Team", "Livery", SkinStatus.NameOnly)),
        };

        var vm = new SkinsViewModel(session);

        var skinPack = Assert.Single(vm.RequiredSkinPacks);
        Assert.Equal("F1 1969 Season (Alain Fry)", skinPack.Name);
        Assert.Equal("F1_Season_1969", skinPack.OverridesFolder);
    }

    [Fact]
    public void EmptyPlan_ShowsNoCarsAndNoCrib()
    {
        var vm = new SkinsViewModel(new FakeCareerSession { SkinPlan = SkinAssignmentPlan.Empty });

        Assert.Empty(vm.Cars);
        Assert.False(vm.HasPlayerCar);
        Assert.False(vm.HasUnbound);
    }

    // ---------- helpers ----------

    private static SkinAssignment Assign(
        string driver, string team, string livery, SkinStatus status,
        bool isPlayer = false, string? folder = null, string? nearMiss = null) => new()
    {
        DriverName = driver,
        TeamName = team,
        LiveryName = livery,
        IsPlayer = isPlayer,
        Status = status,
        VehicleFolder = folder,
        NearMiss = nearMiss,
    };

    private static SkinAssignmentPlan Plan(params SkinAssignment[] assignments) => new()
    {
        Ams2Class = "F-Vintage_Gen2",
        Assignments = assignments,
    };
}
