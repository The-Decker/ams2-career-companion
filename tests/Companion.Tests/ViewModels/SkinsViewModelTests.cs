using Companion.Ams2.Skins;
using Companion.Core.Grid;
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

    [Fact]
    public void SurfacesInactiveLiveriesAsActivatable()
    {
        var session = new FakeCareerSession
        {
            SkinPlan = new SkinAssignmentPlan
            {
                Ams2Class = "F-Retro_Gen3",
                Assignments = [Assign("K. Acheson", "Skoal", "Skoal #10", SkinStatus.InstalledInactive)],
                InactiveLiveries = ["Skoal Bandit Formula 1 Team #10", "Another Inactive #7"],
            },
        };

        var vm = new SkinsViewModel(session);

        Assert.True(vm.HasActivatable);
        Assert.Equal(2, vm.ActivatableLiveries.Count);
        Assert.Contains("Skoal Bandit Formula 1 Team #10", vm.ActivatableLiveries);
    }

    [Fact]
    public void ActivateLivery_CallsTheSeam_AndShowsTheOutcome()
    {
        var session = new FakeCareerSession
        {
            SkinPlan = new SkinAssignmentPlan
            {
                Ams2Class = "F-Retro_Gen3",
                Assignments = [],
                InactiveLiveries = ["Skoal Bandit Formula 1 Team #10"],
            },
            ActivationResult = new LiveryActivationResult
            {
                Success = true, Slot = 61, Message = "Activated “Skoal #10” as livery slot 61.",
            },
        };
        var vm = new SkinsViewModel(session);

        vm.ActivateLiveryCommand.Execute("Skoal Bandit Formula 1 Team #10");

        Assert.Equal("Skoal Bandit Formula 1 Team #10", Assert.Single(session.ActivatedLiveries));
        Assert.True(vm.ActivationSucceeded);
        Assert.Contains("slot 61", vm.ActivationBanner);
    }

    [Fact]
    public void ActivateLivery_FailureShowsAmberOutcome()
    {
        var session = new FakeCareerSession
        {
            SkinPlan = new SkinAssignmentPlan
            {
                Ams2Class = "x", Assignments = [], InactiveLiveries = ["Bogus #1"],
            },
            ActivationResult = LiveryActivationResult.Failed("No AMS2 installation was found."),
        };
        var vm = new SkinsViewModel(session);

        vm.ActivateLiveryCommand.Execute("Bogus #1");

        Assert.False(vm.ActivationSucceeded);
        Assert.Contains("No AMS2", vm.ActivationBanner);
    }

    // ---------- grid editor ----------

    [Fact]
    public void BuildsAnEditorPerSeat_WithLiveryOptions()
    {
        var session = new FakeCareerSession
        {
            SkinPlan = new SkinAssignmentPlan
            {
                Ams2Class = "F-Retro_Gen3",
                Assignments =
                [
                    Assign("K. Acheson", "Skoal", "Skoal #10", SkinStatus.InstalledInactive),
                    Assign("A. Senna", "McLaren", "McLaren #1", SkinStatus.CustomSkin),
                ],
                ActiveLiveries = ["McLaren #1", "Ferrari #27", "Williams #5"],
            },
        };

        var vm = new SkinsViewModel(session);

        Assert.Equal(2, vm.Editors.Count);
        var skoal = vm.Editors[0];
        Assert.Equal("Skoal #10", skoal.LiveryKey);
        Assert.Equal("K. Acheson", skoal.DriverName);
        Assert.Equal("Skoal #10", skoal.SelectedLivery);      // defaults to its own livery
        Assert.Contains("Skoal #10", skoal.LiveryOptions);    // own livery selectable
        Assert.Contains("Ferrari #27", skoal.LiveryOptions);  // + active liveries to rebind to
    }

    [Fact]
    public void RenamingADriver_PersistsAsAnOverride()
    {
        var session = Session("Skoal #10", "K. Acheson");
        var vm = new SkinsViewModel(session);

        vm.Editors[0].DriverName = "Mike Kobra";

        Assert.Equal("Mike Kobra", session.Overrides["Skoal #10"].DriverName);
        Assert.Null(session.Overrides["Skoal #10"].LiveryName);
    }

    [Fact]
    public void RebindingALivery_PersistsAsAnOverride()
    {
        var session = Session("Skoal #10", "K. Acheson", active: ["Ferrari #27"]);
        var vm = new SkinsViewModel(session);

        vm.Editors[0].SelectedLivery = "Ferrari #27";

        Assert.Equal("Ferrari #27", session.Overrides["Skoal #10"].LiveryName);
    }

    [Fact]
    public void ResettingTheNameBackToOriginal_ClearsTheOverride()
    {
        var session = Session("Skoal #10", "K. Acheson");
        var vm = new SkinsViewModel(session);

        vm.Editors[0].DriverName = "Mike Kobra";
        Assert.True(session.Overrides.ContainsKey("Skoal #10"));

        vm.Editors[0].DriverName = "K. Acheson"; // back to the pack's driver
        Assert.False(session.Overrides.ContainsKey("Skoal #10"));
    }

    [Fact]
    public void EditorSeedsFromAnExistingSavedOverride()
    {
        var session = Session("Skoal #10", "K. Acheson", active: ["Ferrari #27"]);
        session.Overrides["Skoal #10"] = new SeatStagingOverride { DriverName = "Renamed", LiveryName = "Ferrari #27" };

        var vm = new SkinsViewModel(session);

        Assert.Equal("Renamed", vm.Editors[0].DriverName);
        Assert.Equal("Ferrari #27", vm.Editors[0].SelectedLivery);
    }

    private static FakeCareerSession Session(string livery, string driver, string[]? active = null) => new()
    {
        SkinPlan = new SkinAssignmentPlan
        {
            Ams2Class = "F-Retro_Gen3",
            Assignments = [Assign(driver, "Team", livery, SkinStatus.CustomSkin)],
            ActiveLiveries = active ?? [],
        },
    };

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
