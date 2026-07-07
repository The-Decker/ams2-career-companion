using Companion.ViewModels.Services;
using Companion.ViewModels.Wizard;

namespace Companion.Tests.ViewModels;

/// <summary>
/// New-career wizard step gating (app-shell contract): verification errors BLOCK advancing
/// no matter what; proceed-anyway only flips warnings; the clean path flows season pick →
/// verification → seat pick → confirm → create with a random-default seed and rules-summary
/// strings from the pack's CatalogSeason.
/// </summary>
public sealed class WizardGatingTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-wizard-").FullName;
    private readonly FakeCareerFactory _factory = new();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string PacksRoot => Path.Combine(_root, "packs");

    private NewCareerWizardViewModel Wizard()
    {
        var environment = new CareerEnvironment
        {
            ContentLibrary = TestPackBuilder.Library(),
            LocateInstall = () => null, // no install: livery scan degrades gracefully
            DocumentsDirectory = Path.Combine(_root, "docs"),
        };
        return new NewCareerWizardViewModel(
            environment,
            _factory,
            packSearchRoots: [PacksRoot],
            careersDirectory: Path.Combine(_root, "careers"),
            seedSource: new Random(1234));
    }

    private void WritePack(string folderName, Companion.Core.Packs.SeasonPack pack) =>
        TestPackBuilder.Write(pack, Path.Combine(PacksRoot, folderName));

    private static void SelectPack(NewCareerWizardViewModel wizard, string folderName)
    {
        wizard.SelectedPack = Assert.Single(
            wizard.Packs, p => Path.GetFileName(p.Directory) == folderName);
    }

    // ---------- discovery ----------

    [Fact]
    public void Discovery_ListsPackFoldersFromTheSearchRoots()
    {
        WritePack("alpha", TestPackBuilder.TwoRoundPack());
        WritePack("beta", TestPackBuilder.TwoRoundPack());
        Directory.CreateDirectory(Path.Combine(PacksRoot, "not-a-pack")); // no pack.json

        var wizard = Wizard();

        Assert.Equal(["alpha", "beta"], wizard.Packs.Select(p => Path.GetFileName(p.Directory)));
        Assert.All(wizard.Packs, p => Assert.Null(p.LoadError));
        Assert.Equal("Test Pack (1.0.0)", wizard.Packs[0].DisplayName);
    }

    // ---------- gating: errors block, always ----------

    [Fact]
    public void Verification_WithErrors_BlocksNext_EvenWithProceedAnyway()
    {
        // Unknown team reference = structural ERROR.
        WritePack("broken", TestPackBuilder.TwoRoundPack(secondTeamId: "team.ghost"));
        var wizard = Wizard();

        Assert.False(wizard.CanGoNext); // nothing selected yet
        SelectPack(wizard, "broken");
        Assert.True(wizard.CanGoNext);

        wizard.NextCommand.Execute(null);
        Assert.Equal(WizardStep.Verification, wizard.Step);
        Assert.True(wizard.HasErrors);
        Assert.False(wizard.CanGoNext);

        wizard.NextCommand.Execute(null);
        Assert.Equal(WizardStep.Verification, wizard.Step); // still blocked

        wizard.ProceedAnyway = true; // proceed-anyway must NOT unlock errors
        Assert.False(wizard.CanGoNext);
        wizard.NextCommand.Execute(null);
        Assert.Equal(WizardStep.Verification, wizard.Step);
    }

    // ---------- gating: warnings need an explicit proceed-anyway ----------

    [Fact]
    public void Verification_WithWarningsOnly_RequiresProceedAnyway()
    {
        // A livery that is neither installed nor a known stock name = content WARNING.
        WritePack("warn", TestPackBuilder.TwoRoundPack(secondLivery: "Ghost Livery #99"));
        var wizard = Wizard();

        SelectPack(wizard, "warn");
        wizard.NextCommand.Execute(null);

        Assert.Equal(WizardStep.Verification, wizard.Step);
        Assert.False(wizard.HasErrors);
        Assert.True(wizard.HasWarnings);
        Assert.Contains(wizard.VerificationItems, i => !i.IsError && i.Message.Contains("Ghost Livery #99"));

        Assert.False(wizard.CanGoNext);
        wizard.NextCommand.Execute(null);
        Assert.Equal(WizardStep.Verification, wizard.Step); // blocked without consent

        wizard.ProceedAnyway = true;
        Assert.True(wizard.CanGoNext);
        wizard.NextCommand.Execute(null);
        Assert.Equal(WizardStep.SeatPick, wizard.Step);
    }

    // ---------- the clean path, all four steps ----------

    [Fact]
    public void CleanPack_FlowsThroughAllFourStepsAndCreates()
    {
        WritePack("clean", TestPackBuilder.TwoRoundPack());
        var wizard = Wizard();

        CareerCreatedEventArgs? created = null;
        wizard.CareerCreated += (_, e) => created = e;

        // a: season pick
        SelectPack(wizard, "clean");
        wizard.NextCommand.Execute(null);

        // b: verification — a fully consistent pack has no findings at all.
        Assert.Equal(WizardStep.Verification, wizard.Step);
        Assert.Empty(wizard.VerificationItems);
        Assert.True(wizard.CanGoNext);
        wizard.NextCommand.Execute(null);

        // c: seat pick — pack entries with ratings and team tier/reliability.
        Assert.Equal(WizardStep.SeatPick, wizard.Step);
        Assert.Equal(2, wizard.Seats.Count);
        var seat = wizard.Seats[1];
        Assert.Equal(TestPackBuilder.StockLivery2, seat.LiveryName);
        Assert.Equal("driver.hulme", seat.DriverId);
        Assert.Equal(0.8, seat.RaceSkill);
        Assert.Equal(0.85, seat.QualifyingSkill);
        Assert.Equal(5, seat.TeamTier);
        Assert.Equal(4, seat.Prestige);
        Assert.Equal(0.93, seat.Reliability);
        Assert.Equal("1-2", seat.Rounds);

        Assert.False(wizard.CanGoNext); // no seat selected yet
        wizard.SelectedSeat = seat;
        wizard.NextCommand.Execute(null);

        // choose the grid (v0.6.0): defaults to the whole field, so a single Next continues.
        Assert.Equal(WizardStep.Grid, wizard.Step);
        Assert.True(wizard.GridChoices.Count > 0 && wizard.GridChoices.All(c => c.IsIncluded));
        wizard.NextCommand.Execute(null);

        // d: confirm — defaults and rules summary.
        Assert.Equal(WizardStep.Confirm, wizard.Step);
        Assert.Equal("Test Championship 1967", wizard.CareerName);
        Assert.True(long.TryParse(wizard.MasterSeedText, out long defaultSeed)); // random default
        Assert.Contains("Points: 9-6-4-3-2-1", wizard.RulesSummary);
        Assert.Contains("Drivers count their best 2 results", wizard.RulesSummary);
        Assert.Contains("Shared drives score no points", wizard.RulesSummary);
        Assert.Contains(
            "Constructors: only the best-placed car scores (same dropped-results rule as drivers)",
            wizard.RulesSummary);

        wizard.MasterSeedText = "777"; // seed stays editable
        wizard.CareerName = "My 1967";
        Assert.True(wizard.CanGoNext);
        wizard.NextCommand.Execute(null);

        Assert.NotNull(created);
        Assert.Same(_factory.Session, created.Session);
        var request = _factory.LastRequest;
        Assert.NotNull(request);
        Assert.Equal("My 1967", request.CareerName);
        Assert.Equal(777, request.MasterSeed);
        Assert.Equal(TestPackBuilder.StockLivery2, request.PlayerLiveryName);
        Assert.Equal(Path.Combine(PacksRoot, "clean"), request.PackDirectory);
        Assert.EndsWith(".ams2career", request.CareerFilePath);
        Assert.StartsWith(Path.Combine(_root, "careers"), request.CareerFilePath);
        Assert.NotEqual(0, defaultSeed); // Random(1234) never yields 0 here; documents the default was real
    }

    // ---------- choose the grid (v0.6.0) ----------

    private NewCareerWizardViewModel WizardAtGrid(string folderName)
    {
        // A three-seat pack so excluding one leaves a valid field (player + one AI).
        var basePack = TestPackBuilder.TwoRoundPack();
        var pack3 = basePack with
        {
            Drivers = basePack.Drivers.Append(TestPackBuilder.Driver("driver.clark")).ToList(),
            Entries = basePack.Entries
                .Append(TestPackBuilder.Entry("team.brabham", "driver.clark", "3", "Stock Livery #3")).ToList(),
        };
        WritePack(folderName, pack3);
        var wizard = Wizard();
        SelectPack(wizard, folderName);
        wizard.NextCommand.Execute(null); // -> Verification
        wizard.ProceedAnyway = true;      // "Stock Livery #3" is unknown to the test library → a warning
        wizard.NextCommand.Execute(null); // -> SeatPick
        wizard.SelectedSeat = wizard.Seats.First(s => s.LiveryName == TestPackBuilder.StockLivery2);
        wizard.NextCommand.Execute(null); // -> Grid
        return wizard;
    }

    private static void AdvanceToCreate(NewCareerWizardViewModel wizard)
    {
        while (wizard.Step != WizardStep.Confirm)
            wizard.NextCommand.Execute(null);
        wizard.CareerName = "Grid Test";
        wizard.NextCommand.Execute(null); // Create
    }

    [Fact]
    public void GridStep_ExcludingASeat_WritesTheChosenFieldIntoTheRequest()
    {
        var wizard = WizardAtGrid("grid-exclude");
        Assert.Equal(WizardStep.Grid, wizard.Step);
        Assert.Equal(3, wizard.GridChoices.Count);

        var player = wizard.GridChoices.Single(c => c.LiveryName == TestPackBuilder.StockLivery2);
        Assert.True(player is { IsLocked: true, IsIncluded: true }); // player is locked on

        // The AI-opponent count is the field minus the player's own car — the exact number to type
        // into AMS2, so the player never does the "minus one" themselves.
        Assert.Equal(2, wizard.AiOpponentCount); // 3 cars in, minus the player

        wizard.GridChoices.Single(c => c.LiveryName == "Stock Livery #3").IsIncluded = false;
        Assert.Equal(2, wizard.IncludedCount);
        Assert.Equal(1, wizard.AiOpponentCount); // 2 cars in, minus the player
        Assert.True(wizard.CanGoNext);

        AdvanceToCreate(wizard);

        var selection = _factory.LastRequest!.GridSelection;
        Assert.NotNull(selection);
        Assert.Contains(TestPackBuilder.StockLivery2, selection!.IncludedLiveries!);
        Assert.DoesNotContain("Stock Livery #3", selection.IncludedLiveries!);
    }

    [Fact]
    public void GridStep_WholeFieldIncluded_LeavesTheRequestSelectionNull()
    {
        var wizard = WizardAtGrid("grid-all");
        Assert.True(wizard.GridChoices.All(c => c.IsIncluded));

        AdvanceToCreate(wizard);

        Assert.Null(_factory.LastRequest!.GridSelection); // whole pack → identity, byte-identical
    }

    [Fact]
    public void GridStep_BlocksNext_BelowTwoCars()
    {
        var wizard = WizardAtGrid("grid-block");
        // Exclude everything except the locked player → 1 car → a race needs at least two.
        foreach (var c in wizard.GridChoices.Where(c => !c.IsLocked))
            c.IsIncluded = false;

        Assert.Equal(1, wizard.IncludedCount);
        Assert.False(wizard.CanGoNext);
    }

    // ---------- back navigation ----------

    [Fact]
    public void Back_WalksTowardSeasonPickAndStopsThere()
    {
        WritePack("clean", TestPackBuilder.TwoRoundPack());
        var wizard = Wizard();

        SelectPack(wizard, "clean");
        wizard.NextCommand.Execute(null);
        Assert.Equal(WizardStep.Verification, wizard.Step);

        wizard.BackCommand.Execute(null);
        Assert.Equal(WizardStep.SeasonPick, wizard.Step);

        wizard.BackCommand.Execute(null); // already at the first step
        Assert.Equal(WizardStep.SeasonPick, wizard.Step);
    }
}
