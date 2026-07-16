using Companion.Core.Packs;
using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Smgp;
using Companion.ViewModels.Services;
using Companion.ViewModels.Wizard;

namespace Companion.Tests.ViewModels;

/// <summary>
/// The SMGP character-creation name field (Mike: "whatever character you pick it should default to You as
/// the driver name in the text box"). In SMGP the player is their OWN driver (the clean-swap, not the seat's
/// historical occupant), so the box seeds to "You" for the player to personalise - regardless of which
/// Level-D seat they pick.
/// </summary>
public sealed class SmgpWizardNameTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-smgp-wiz-").FullName;
    private readonly FakeCareerFactory _factory = new();

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void SmgpCharacterStep_DefaultsTheNameToYou_NotTheChosenSeatDriver()
    {
        var packDir = Path.Combine(_root, "packs", "smgp");
        TestPackBuilder.Write(SmgpLevelDPack(), packDir);

        var wizard = new NewCareerWizardViewModel(
            new CareerEnvironment
            {
                ContentLibrary = TestPackBuilder.Library(),
                LocateInstall = () => null,
                DocumentsDirectory = Path.Combine(_root, "docs"),
                RulesDirectory = ViewModelTestData.RulesDirectory, // loads character rules → Character step
            },
            _factory,
            packSearchRoots: [Path.Combine(_root, "packs")],
            careersDirectory: Path.Combine(_root, "careers"),
            seedSource: new Random(1));

        wizard.SelectedPack = Assert.Single(wizard.Packs, p => Path.GetFileName(p.Directory) == "smgp");
        wizard.NextCommand.Execute(null); // -> Verification
        wizard.ProceedAnyway = true;
        wizard.NextCommand.Execute(null); // -> Character
        Assert.Equal(WizardStep.Character, wizard.Step);
        Assert.NotNull(wizard.Character);
        Assert.Equal("You", wizard.Character!.Name); // identity exists before seat pick

        wizard.NextCommand.Execute(null); // -> SeatPick
        Assert.Equal(WizardStep.SeatPick, wizard.Step);
        Assert.Null(wizard.SelectedSeat); // even SMGP requires an explicit card click
        Assert.False(wizard.NextCommand.CanExecute(null));

        // SMGP offers only Level-D seats; clicking one chooses the car without changing identity.
        var seat = wizard.Seats.First();
        wizard.SelectedSeat = seat;
        Assert.Equal("You", wizard.Character!.Name);            // the player is their own driver, not the seat's
        Assert.NotEqual(seat.DriverName, wizard.Character.Name);
    }

    [Fact]
    public void ProductionEntry_InfersSmgpAndPersistsTheLevel300Character()
    {
        var packDir = Path.Combine(_root, "packs-v2", "smgp");
        TestPackBuilder.Write(SmgpLevelDPack(), packDir);

        var wizard = new NewCareerWizardViewModel(
            new CareerEnvironment
            {
                ContentLibrary = TestPackBuilder.Library(),
                LocateInstall = () => null,
                DocumentsDirectory = Path.Combine(_root, "docs-v2"),
                RulesDirectory = ViewModelTestData.RulesDirectory,
            },
            _factory,
            packSearchRoots: [Path.Combine(_root, "packs-v2")],
            careersDirectory: Path.Combine(_root, "careers-v2"),
            seedSource: new Random(2),
            inferExperienceModeFromPack: true);

        Assert.True(wizard.IsProgressionV2);
        Assert.False(wizard.HasResolvedExperienceMode);
        Assert.Null(wizard.ExperienceMode);
        Assert.Equal("CAREER MODE PENDING", wizard.ExperienceModeLabel);
        wizard.SelectedPack = Assert.Single(wizard.Packs);
        wizard.NextCommand.Execute(null); // -> Verification; parsed pack selects the stable mode
        Assert.True(wizard.HasResolvedExperienceMode);
        Assert.Equal(CareerExperienceModes.Smgp, wizard.ExperienceMode);
        Assert.Equal("SUPER MONACO GP WORLD CHAMPIONSHIP", wizard.ExperienceModeLabel);
        var preview = Assert.IsType<CampaignProgressionPlan>(wizard.ResolvedCampaignPlan);
        Assert.Equal(SmgpRules.CampaignSeasons, wizard.CampaignTotalSeasons);
        Assert.Equal(SmgpRules.CampaignSeasons - 1, wizard.CampaignMasterySeason);
        Assert.Equal(SmgpRules.CampaignSeasons, preview.TotalSeasons);
        Assert.Contains("17 ORDINAL SEASONS", wizard.CampaignCoverageSummary, StringComparison.Ordinal);
        Assert.Contains("MASTERY AFTER SEASON 16", wizard.CampaignPacingSummary, StringComparison.Ordinal);

        wizard.ProceedAnyway = true;
        wizard.NextCommand.Execute(null); // -> Character
        Assert.Equal(WizardStep.Character, wizard.Step);
        Assert.True(wizard.Character!.IsProgressionV2);
        Assert.Equal(30, wizard.Character.RacingDnaCards.Count);
        wizard.Character.SelectedCountry = wizard.Character.CountryOptions.Single(option => option.Code == "BRA");

        wizard.NextCommand.Execute(null); // -> SeatPick
        wizard.SelectedSeat = wizard.Seats.First();
        wizard.NextCommand.Execute(null); // -> Grid
        wizard.NextCommand.Execute(null); // -> Confirm
        wizard.NextCommand.Execute(null); // -> Create

        var request = Assert.IsType<CareerCreationRequest>(_factory.LastRequest);
        Assert.Equal(CareerExperienceModes.Smgp, request.ExperienceMode);
        Assert.True(request.SmgpMode);
        var profile = Assert.IsType<CharacterProfile>(request.Character);
        Assert.Equal(CharacterLevelProgression.Level300Version, profile.ProgressionVersion);
        Assert.Equal(CharacterProfile.CurrentExpectationModelVersion, profile.ExpectationModelVersion);
        Assert.Equal("BRA", profile.CountryCode);
    }

    /// <summary>A minimal 16-round SMGP pack with a LEVEL-D team (the only tier SMGP lets you start in).</summary>
    private static SeasonPack SmgpLevelDPack()
    {
        var basePack = TestPackBuilder.TwoRoundPack();
        return basePack with
        {
            Manifest = basePack.Manifest with { CareerStyle = SmgpRules.CareerStyle },
            Season = basePack.Season with
            {
                Rounds = Enumerable.Range(1, 16)
                    .Select(round => TestPackBuilder.Round(
                        round,
                        new DateOnly(1967, 1, 1).AddDays((round - 1) * 14).ToString("yyyy-MM-dd")))
                    .ToArray(),
            },
            Teams =
            [
                new PackTeam
                {
                    Id = "team.zeroforce", Name = "Zeroforce", CarVehicleIds = [TestPackBuilder.VintageCar],
                    Reliability = 0.85, Prestige = 2, BudgetTier = 2,
                },
            ],
            Drivers =
            [
                TestPackBuilder.Driver("driver.paul_klinger"),
                TestPackBuilder.Driver("driver.kevin_yepes"),
            ],
            Entries =
            [
                TestPackBuilder.Entry("team.zeroforce", "driver.paul_klinger", "1", TestPackBuilder.StockLivery1)
                    with { Rounds = "1-16" },
                TestPackBuilder.Entry("team.zeroforce", "driver.kevin_yepes", "2", TestPackBuilder.StockLivery2)
                    with { Rounds = "1-16" },
            ],
        };
    }
}
