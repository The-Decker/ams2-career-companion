using Companion.Ams2.Skins;
using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.ViewModels.Hub;
using Companion.ViewModels.Services;

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
                Assign(
                    "A. Custom", "Team A", "Livery A", SkinStatus.CustomSkin,
                    folder: "brabham_bt26", driverId: "driver.custom", teamId: "team.a", skinSlot: "61"),
                Assign("B. Stock", "Team B", "Livery B", SkinStatus.StockDefault),
                Assign("C. NameOnly", "Team C", "Livery C", SkinStatus.NameOnly),
                Assign("D. Bogus", "Team D", "Livery D", SkinStatus.Unbound, nearMiss: "Livery d")),
        };

        var vm = new SkinsViewModel(session);

        Assert.Equal(4, vm.Cars.Count);
        Assert.Equal("driver.custom", vm.Cars[0].DriverId);
        Assert.Equal("team.a", vm.Cars[0].TeamId);
        Assert.Equal("61", vm.Cars[0].SkinSlot);
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
                    Assign("K. Acheson", "Skoal", "Skoal #10", SkinStatus.InstalledInactive, folder: "model.a"),
                    Assign("A. Senna", "McLaren", "McLaren #1", SkinStatus.CustomSkin, folder: "model.a"),
                ],
                ActiveLiveries = ["McLaren #1", "Ferrari #27", "Williams #5", "Vanilla Stock #2"],
                ActiveCustomLiveryModels = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["McLaren #1"] = "model.a",
                    ["Ferrari #27"] = "model.a",
                    ["Williams #5"] = "model.b",
                },
            },
        };

        var vm = new SkinsViewModel(session);

        Assert.Equal(2, vm.Editors.Count);
        var skoal = vm.Editors[0];
        Assert.Equal("Skoal #10", skoal.LiveryKey);
        Assert.Equal("K. Acheson", skoal.DriverName);
        Assert.Equal("Skoal #10", skoal.SelectedLivery);      // defaults to its own livery
        Assert.Null(skoal.ReplacementSelection);
        Assert.Equal(["Ferrari #27", "McLaren #1"], skoal.LiveryOptions);
        Assert.DoesNotContain("Vanilla Stock #2", skoal.LiveryOptions);
        Assert.DoesNotContain("Williams #5", skoal.LiveryOptions);
        Assert.Same(skoal, vm.SelectedEditor);                 // no player seat => first seat
    }

    [Fact]
    public void PreviewContract_SelectsPlayerAndTracksKnownAndUnknownReplacementArt()
    {
        var pack = TestPackBuilder.TwoRoundPack();
        var session = new FakeCareerSession
        {
            Pack = pack,
            SkinPlan = new SkinAssignmentPlan
            {
                Ams2Class = TestPackBuilder.VintageClass,
                Assignments =
                [
                    Assign(
                        "Jack Brabham", "Brabham-Repco", TestPackBuilder.StockLivery1,
                        SkinStatus.CustomSkin,
                        driverId: "driver.brabham", teamId: "team.brabham",
                        number: "1", skinSlot: "51"),
                    Assign(
                        "Nova Reyes", "Brabham-Repco", TestPackBuilder.StockLivery2,
                        SkinStatus.CustomSkin,
                        isPlayer: true,
                        driverId: RoundGridResolver.SyntheticPlayerDriverId, teamId: "team.brabham",
                        number: "2", skinSlot: "52"),
                ],
                ActiveLiveries =
                [
                    TestPackBuilder.StockLivery1,
                    TestPackBuilder.StockLivery2,
                    "Community Unknown #9",
                ],
                ActiveLiverySlots = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [TestPackBuilder.StockLivery1] = "51",
                    [TestPackBuilder.StockLivery2] = "52",
                    ["Community Unknown #9"] = "63",
                },
                ActiveCustomLiveryModels = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [TestPackBuilder.StockLivery1] = TestPackBuilder.VintageCar,
                    [TestPackBuilder.StockLivery2] = TestPackBuilder.VintageCar,
                    ["Community Unknown #9"] = TestPackBuilder.VintageCar,
                },
            },
        };

        var vm = new SkinsViewModel(session);

        var editor = Assert.IsType<SeatEditor>(vm.SelectedEditor);
        Assert.Equal(TestPackBuilder.StockLivery2, editor.LiveryKey);
        Assert.Equal(RoundGridResolver.SyntheticPlayerDriverId, editor.OriginalPreview.DriverId);
        Assert.Equal("player.brabham", editor.OriginalPreview.PortraitKey);
        Assert.Equal("driver.hulme", editor.OriginalPreview.CarKey);
        Assert.Equal("driver.hulme", editor.OriginalPreview.TopCarKey);
        Assert.Equal("52", editor.OriginalPreview.SkinSlot);
        Assert.Equal(TestPackBuilder.VintageCar, editor.OriginalPreview.VehicleModel);

        Assert.Equal("player.brabham", vm.PlayerPortraitKey);
        Assert.Equal("driver.hulme", vm.PlayerCarKey);
        Assert.Equal("driver.hulme", vm.PlayerTopCarKey);
        Assert.Equal("52", vm.PlayerSkinSlot);
        Assert.Equal("2", vm.PlayerCarNumber);

        var changed = new List<string>();
        editor.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is { } name)
                changed.Add(name);
        };

        editor.SelectedLivery = TestPackBuilder.StockLivery1;

        Assert.True(editor.IsReplacement);
        Assert.Equal("driver.brabham", editor.SelectedPreview.DriverId);
        Assert.Equal("team.brabham", editor.SelectedPreview.TeamId);
        Assert.Equal("1", editor.SelectedPreview.CarNumber);
        Assert.Equal("51", editor.SelectedPreview.SkinSlot);
        Assert.Equal("driver.brabham", editor.SelectedPreview.CarKey);
        Assert.Equal("driver.brabham", editor.SelectedPreview.TopCarKey);
        Assert.Contains(nameof(SeatEditor.SelectedPreview), changed);
        Assert.Contains(nameof(SeatEditor.IsReplacement), changed);

        editor.SelectedLivery = "Community Unknown #9";

        Assert.True(editor.IsReplacement);
        Assert.Equal("Community Unknown #9", editor.SelectedPreview.LiveryName);
        Assert.Equal("", editor.SelectedPreview.DriverId);
        Assert.Equal("", editor.SelectedPreview.TeamId);
        Assert.Equal("", editor.SelectedPreview.SkinSlot);
        Assert.Null(editor.SelectedPreview.PortraitKey);
        Assert.Null(editor.SelectedPreview.CarKey);
        Assert.Null(editor.SelectedPreview.TopCarKey);
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

        vm.Editors[0].ReplacementSelection = "Ferrari #27";

        Assert.Equal("Ferrari #27", vm.Editors[0].SelectedLivery);
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
        Assert.Equal("Ferrari #27", vm.Editors[0].ReplacementSelection);
    }

    [Fact]
    public void LegacyVanillaOrCrossModelOverride_IsClearedButDriverRenameSurvives()
    {
        var session = Session("Skoal #10", "K. Acheson", active: ["Ferrari #27"]);
        session.SkinPlan = session.SkinPlan with
        {
            ActiveLiveries = ["Ferrari #27", "Vanilla Stock #2", "Wrong Model #5"],
            ActiveCustomLiveryModels = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Skoal #10"] = "model.a",
                ["Ferrari #27"] = "model.a",
                ["Wrong Model #5"] = "model.b",
            },
        };
        session.Overrides["Skoal #10"] = new SeatStagingOverride
        {
            DriverName = "Renamed",
            LiveryName = "Vanilla Stock #2",
        };

        var vm = new SkinsViewModel(session);

        var editor = Assert.Single(vm.Editors);
        Assert.Equal("Skoal #10", editor.SelectedLivery);
        Assert.Null(editor.ReplacementSelection);
        Assert.Equal("Renamed", session.Overrides["Skoal #10"].DriverName);
        Assert.Null(session.Overrides["Skoal #10"].LiveryName);
        Assert.DoesNotContain("Vanilla Stock #2", editor.LiveryOptions);
        Assert.DoesNotContain("Wrong Model #5", editor.LiveryOptions);
    }

    [Fact]
    public void StageGrid_PushesThroughTheSeam_AndShowsTheOutcome()
    {
        var session = Session("Skoal #10", "K. Acheson");
        session.StageOutcomes.Enqueue(new StageOutcome
        {
            Success = true, WrittenPath = @"C:\...\F-Retro_Gen3.xml", Messages = ["staged"],
        });
        var vm = new SkinsViewModel(session);

        vm.StageGridCommand.Execute(null);

        Assert.True(vm.StageSucceeded);
        Assert.Contains("set up for this race", vm.StageBanner);
    }

    [Fact]
    public void StageGrid_CommunityFileGate_ShowsStageAnyway()
    {
        var session = Session("Skoal #10", "K. Acheson");
        session.StageOutcomes.Enqueue(new StageOutcome
        {
            Success = false, BlockedByForceGate = true, Messages = ["community file"],
        });
        var vm = new SkinsViewModel(session);

        vm.StageGridCommand.Execute(null);

        Assert.True(vm.StageBlocked);
        Assert.False(vm.StageSucceeded);
        Assert.Contains("Overwrite anyway", vm.StageBanner);
    }

    private static FakeCareerSession Session(string livery, string driver, string[]? active = null) => new()
    {
        SkinPlan = new SkinAssignmentPlan
        {
            Ams2Class = "F-Retro_Gen3",
            Assignments = [Assign(driver, "Team", livery, SkinStatus.CustomSkin, folder: "model.a")],
            ActiveLiveries = active ?? [],
            ActiveCustomLiveryModels = (active ?? [])
                .Append(livery)
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(name => name, _ => "model.a", StringComparer.Ordinal),
        },
    };

    // ---------- helpers ----------

    private static SkinAssignment Assign(
        string driver, string team, string livery, SkinStatus status,
        bool isPlayer = false, string? folder = null, string? nearMiss = null,
        string driverId = "", string teamId = "", string? number = null, string skinSlot = "") => new()
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
        Ams2Class = "F-Vintage_Gen2",
        Assignments = assignments,
    };
}
