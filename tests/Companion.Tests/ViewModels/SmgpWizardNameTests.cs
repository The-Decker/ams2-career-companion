using Companion.Core.Packs;
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
        wizard.NextCommand.Execute(null); // -> SeatPick
        Assert.Equal(WizardStep.SeatPick, wizard.Step);

        // SMGP offers only Level-D seats; pick one and advance into the character step.
        var seat = wizard.Seats.First();
        wizard.SelectedSeat = seat;
        wizard.NextCommand.Execute(null); // -> Character
        Assert.Equal(WizardStep.Character, wizard.Step);

        Assert.NotNull(wizard.Character);
        Assert.Equal("You", wizard.Character!.Name);            // the player is their own driver, not the seat's
        Assert.NotEqual(seat.DriverName, wizard.Character.Name);
    }

    /// <summary>A minimal SMGP pack with a LEVEL-D team (the only tier SMGP lets you start in), two rounds.</summary>
    private static SeasonPack SmgpLevelDPack()
    {
        var basePack = TestPackBuilder.TwoRoundPack();
        return basePack with
        {
            Manifest = basePack.Manifest with { CareerStyle = SmgpRules.CareerStyle },
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
                TestPackBuilder.Entry("team.zeroforce", "driver.paul_klinger", "1", TestPackBuilder.StockLivery1),
                TestPackBuilder.Entry("team.zeroforce", "driver.kevin_yepes", "2", TestPackBuilder.StockLivery2),
            ],
        };
    }
}
