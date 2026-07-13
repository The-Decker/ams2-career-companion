using Companion.Core.Character;
using Companion.Tests.Career;
using Companion.ViewModels.Services;
using Companion.ViewModels.Wizard;

namespace Companion.Tests.ViewModels;

/// <summary>The wizard's character-creation step: the pure <see cref="CharacterViewModel"/> (archetype
/// presets, perk toggling, live CP validity, profile building) and its integration into the wizard
/// flow (the step appears when character rules are loaded and writes the character into the request).</summary>
public sealed class CharacterWizardTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-char-wizard-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static CharacterRules Rules() => CharacterRules.Parse(CareerTestData.ReadRules("perks.json"));

    // ---------- the viewmodel ----------

    [Fact]
    public void DefaultsToTheFirstArchetype_AsACompleteValidBuild()
    {
        var vm = new CharacterViewModel(Rules());
        var first = vm.Archetypes[0];

        Assert.NotNull(vm.SelectedArchetype);
        Assert.True(vm.IsValid);
        Assert.Null(vm.Invalidity);

        // Stats and perks came from the preset.
        Assert.Equal(first.StartStats["pace"], vm.Stats.Single(s => s.Id == "pace").Value, 6);
        Assert.Equal(
            first.PerkIds.OrderBy(x => x, StringComparer.Ordinal),
            vm.Perks.Where(p => p.IsSelected).Select(p => p.Id).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(first.PerkIds.Sum(id => Rules().PerkById(id).Cost), vm.NetCpSpend);
    }

    [Fact]
    public void Name_PreFillsFromTheSeatDriver_AndBuildProfileCarriesTheEditedName()
    {
        var vm = new CharacterViewModel(Rules(), "Denny Hulme");
        Assert.Equal("Denny Hulme", vm.Name); // pre-filled with the seat's historical driver

        vm.Name = "  Ayrton da Silva  ";
        Assert.Equal("Ayrton da Silva", vm.BuildProfile().Name); // trimmed, the player's own identity
    }

    [Fact]
    public void TogglePerk_MovesTheNetCpSpend()
    {
        var vm = new CharacterViewModel(Rules());
        int before = vm.NetCpSpend;
        var perk = vm.Perks.First(p => !p.IsSelected && p.Cost > 0);

        vm.TogglePerkCommand.Execute(perk);

        Assert.True(perk.IsSelected);
        Assert.Equal(before + perk.Cost, vm.NetCpSpend);
    }

    [Fact]
    public void SelectingAnArchetype_ReplacesStatsAndPerks()
    {
        var vm = new CharacterViewModel(Rules());
        var target = vm.Archetypes.First(a => a.Id == "rain_master");

        vm.SelectedArchetype = target;

        Assert.Equal(target.StartStats["adaptability"], vm.Stats.Single(s => s.Id == "adaptability").Value, 6);
        Assert.Equal(
            target.PerkIds.OrderBy(x => x, StringComparer.Ordinal),
            vm.Perks.Where(p => p.IsSelected).Select(p => p.Id).OrderBy(x => x, StringComparer.Ordinal));
        Assert.True(vm.IsValid);
    }

    [Fact]
    public void MaxingEveryStat_ExceedsTheTalentCap_AndIsInvalid()
    {
        var vm = new CharacterViewModel(Rules());
        foreach (var s in vm.Stats.Concat(vm.MetaStats))
            s.Value = 0.85;

        Assert.True(vm.StatTotal > vm.StatCap);
        Assert.False(vm.StatsWithinCap);
        Assert.False(vm.IsValid);
        Assert.NotNull(vm.Invalidity);
        Assert.Contains("talent", vm.Invalidity);
    }

    [Fact]
    public void RedistributingTalentWithinTheCap_StaysValid()
    {
        var vm = new CharacterViewModel(Rules());
        // Floor everything, then pour the freed talent into two stats — a real specialist, under cap.
        foreach (var s in vm.Stats.Concat(vm.MetaStats))
            s.Value = 0.15;
        vm.Stats.First(s => s.Id == "pace").Value = 0.85;
        vm.Stats.First(s => s.Id == "oneLap").Value = 0.85;

        Assert.True(vm.StatsWithinCap);
        Assert.True(vm.IsValid); // the default archetype's perks are in budget
        Assert.Equal(0.85, vm.BuildProfile().Stat("pace"), 6);
    }

    [Fact]
    public void OverspendingEveryPositivePerk_IsInvalid()
    {
        var vm = new CharacterViewModel(Rules());
        foreach (var perk in vm.Perks.Where(p => p.Cost > 0 && !p.IsSelected).ToList())
            vm.TogglePerkCommand.Execute(perk);

        Assert.True(vm.NetCpSpend > vm.Budget);
        Assert.False(vm.IsValid);
        Assert.NotNull(vm.Invalidity);
    }

    [Fact]
    public void CarryingMorePerksThanTheCountCap_IsInvalid()
    {
        var vm = new CharacterViewModel(Rules());
        Assert.NotNull(vm.MaxPerks); // the shipped rules cap the perk count

        // Clear the preset, then select one MORE than the cap allows — using only zero-cost perks so
        // the CP net stays in budget and ONLY the count cap can fail the build.
        foreach (var selected in vm.Perks.Where(p => p.IsSelected).ToList())
            vm.TogglePerkCommand.Execute(selected);
        foreach (var perk in vm.Perks.Where(p => p.Cost == 0).Take(vm.MaxPerks!.Value + 1))
            vm.TogglePerkCommand.Execute(perk);

        Assert.Equal(vm.MaxPerks!.Value + 1, vm.SelectedPerkCount);
        Assert.True(vm.PerksInBudget);      // net is in budget — only the COUNT cap fails
        Assert.False(vm.PerksWithinCount);
        Assert.False(vm.IsValid);
        Assert.Contains("at most", vm.Invalidity);
    }

    [Fact]
    public void BuildProfile_CarriesTheDriverAge_ClampedToTheCreationBand()
    {
        var vm = new CharacterViewModel(Rules());
        Assert.Equal(CharacterViewModel.DefaultAge, vm.Age); // a sensible rookie default

        vm.Age = 19;
        Assert.Equal(19, vm.BuildProfile().Age);

        vm.Age = 99; // out of the creation band
        Assert.Equal(CharacterViewModel.MaxAge, vm.BuildProfile().Age);

        vm.Age = 3;
        Assert.Equal(CharacterViewModel.MinAge, vm.BuildProfile().Age);
    }

    [Fact]
    public void StatSlider_ClampsToTheCreationBand()
    {
        var pace = new CharacterViewModel(Rules()).Stats.Single(s => s.Id == "pace");

        pace.Value = 0.99;
        Assert.Equal(0.85, pace.Value, 6);
        pace.Value = 0.0;
        Assert.Equal(0.15, pace.Value, 6);
    }

    [Fact]
    public void BuildProfile_CarriesTheSelectedStatsPerksAndUnspentCp()
    {
        var vm = new CharacterViewModel(Rules());
        var profile = vm.BuildProfile();

        Assert.Equal(
            vm.Perks.Where(p => p.IsSelected).Select(p => p.Id),
            profile.PerkIds);
        Assert.Equal(vm.Stats.Single(s => s.Id == "pace").Value, profile.Stat("pace"), 6);
        Assert.Contains("marketability", profile.Stats.Keys); // meta stats included
        Assert.Equal(vm.RemainingCp, profile.CpUnspent);
    }

    [Fact]
    public void PerkCategories_ExposeFriendlyDisplayNames_NotRawIds()
    {
        var vm = new CharacterViewModel(Rules());
        var era = vm.PerkCategories.Single(c => c.Name == "era");
        Assert.Equal("Era-flavor", era.DisplayName); // was the raw "era" on the shelf header
        Assert.All(vm.PerkCategories, c => Assert.False(string.IsNullOrWhiteSpace(c.DisplayName)));
    }

    [Fact]
    public void MetaStatSlider_RangesBeyondTheTalentBand()
    {
        // Meta stats (marketability/durability) have no rating analog, so they range over the full
        // authored band (0–1), not the talent 0.15–0.85 clamp.
        var marketability = new CharacterViewModel(Rules()).MetaStats.Single(s => s.Id == "marketability");
        marketability.Value = 0.95;
        Assert.Equal(0.95, marketability.Value, 6); // not clamped down to the talent ceiling
    }

    [Fact]
    public void TalentStatSlider_ExposesItsWrittenRatingPreview()
    {
        var pace = new CharacterViewModel(Rules()).Stats.Single(s => s.Id == "pace");
        pace.Value = 0.50;
        // writeBase 0.35 + writeSpan 0.55 * 0.50 = 0.625 → "race pace 0.63".
        Assert.Contains("race pace", pace.WrittenPreview);
        Assert.Contains("0.63", pace.WrittenPreview);
    }

    [Fact]
    public void OneTrick_RevealsTheSpecialismPicker_AndBuildProfileRecordsTheChosenFlavor()
    {
        var vm = new CharacterViewModel(Rules());
        // Start from a clean slate so only one_trick drives IsOneTrickSelected.
        foreach (var selected in vm.Perks.Where(p => p.IsSelected).ToList())
            vm.TogglePerkCommand.Execute(selected);
        Assert.False(vm.IsOneTrickSelected);
        Assert.Null(vm.BuildProfile().ChosenFlavor); // no one_trick → no recorded flavor

        vm.TogglePerkCommand.Execute(vm.Perks.Single(p => p.Id == "one_trick"));
        Assert.True(vm.IsOneTrickSelected);

        vm.ChosenFlavor = vm.EligibleFlavors.Single(f => f.Field == "tyreManagement");
        Assert.Equal("tyreManagement", vm.BuildProfile().ChosenFlavor);

        // raceSkill (the auto-taxed pace lever) is deliberately NOT an eligible specialism.
        Assert.DoesNotContain(vm.EligibleFlavors, f => f.Field == "raceSkill");
    }

    [Fact]
    public void RandomBuild_ProducesAValidInBudgetCharacter_AndVariesAcrossRolls()
    {
        var vm = new CharacterViewModel(Rules());

        vm.RandomBuildCommand.Execute(null);
        Assert.True(vm.IsValid, vm.Invalidity);
        Assert.Null(vm.SelectedArchetype); // the rolled spread is no longer a preset

        // A second roll is also valid and (deterministically) differs from the first.
        var firstStats = vm.Stats.Select(s => s.Value).ToList();
        vm.RandomBuildCommand.Execute(null);
        Assert.True(vm.IsValid, vm.Invalidity);
        var secondStats = vm.Stats.Select(s => s.Value).ToList();
        Assert.NotEqual(firstStats, secondStats);
    }

    // ---------- wizard integration ----------

    [Fact]
    public void RenamingTheDriver_ReplacesTheSeatDriver_OnThePlayersOwnGridCard()
    {
        // Mike's bug: changing the driver name left the seat's original driver on the "YOU" card.
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), Path.Combine(_root, "packs", "pack"));
        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());
        var wizard = new NewCareerWizardViewModel(
            environment, new FakeCareerFactory(),
            packSearchRoots: [Path.Combine(_root, "packs")],
            careersDirectory: Path.Combine(_root, "careers"),
            seedSource: new Random(9));

        wizard.SelectedPack = Assert.Single(wizard.Packs);
        wizard.NextCommand.Execute(null);                 // -> Verification
        if (wizard.HasWarnings) wizard.ProceedAnyway = true;
        wizard.NextCommand.Execute(null);                 // -> Character
        Assert.Equal(WizardStep.Character, wizard.Step);
        wizard.Character!.Name = "Renamed Driver";        // identity exists before the car pick
        wizard.NextCommand.Execute(null);                 // -> SeatPick
        var seat = wizard.Seats.First(s => s.LiveryName == TestPackBuilder.StockLivery2);
        wizard.SelectedSeat = seat;
        wizard.NextCommand.Execute(null);                 // -> Grid (choices built on entry)

        // The player REPLACES the seat's driver on their own (locked) card — new name, not the AI's.
        var you = Assert.Single(wizard.GridChoices, c => c.IsLocked);
        Assert.Equal("Renamed Driver", you.DriverName);
        Assert.NotEqual(seat.DriverName, you.DriverName);
        // Every other card keeps its own driver.
        Assert.All(wizard.GridChoices.Where(c => !c.IsLocked), c => Assert.NotEqual("Renamed Driver", c.DriverName));
    }

    [Fact]
    public void Wizard_WithRules_ShowsTheCharacterStep_AndWritesTheCharacterIntoTheRequest()
    {
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), Path.Combine(_root, "packs", "pack"));
        var factory = new FakeCareerFactory();
        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());
        var wizard = new NewCareerWizardViewModel(
            environment, factory,
            packSearchRoots: [Path.Combine(_root, "packs")],
            careersDirectory: Path.Combine(_root, "careers"),
            seedSource: new Random(9));

        Assert.True(wizard.HasCharacterStep);

        wizard.SelectedPack = Assert.Single(wizard.Packs);
        wizard.NextCommand.Execute(null);                 // -> Verification
        if (wizard.HasWarnings) wizard.ProceedAnyway = true;
        wizard.NextCommand.Execute(null);                 // -> Character (rules loaded)
        Assert.Equal(WizardStep.Character, wizard.Step);
        Assert.NotNull(wizard.Character);
        Assert.Equal("You", wizard.Character!.Name);      // seat-independent default

        wizard.BackCommand.Execute(null);
        Assert.Equal(WizardStep.Verification, wizard.Step);
        wizard.NextCommand.Execute(null);
        Assert.Equal(WizardStep.Character, wizard.Step);

        wizard.Character.Name = "Chosen Driver";
        wizard.NextCommand.Execute(null);                 // -> SeatPick
        wizard.BackCommand.Execute(null);
        Assert.Equal(WizardStep.Character, wizard.Step);
        Assert.Equal("Chosen Driver", wizard.Character.Name); // back preserves the authored identity
        wizard.NextCommand.Execute(null);                 // -> SeatPick again
        var seat = wizard.Seats.First(s => s.LiveryName == TestPackBuilder.StockLivery2);
        wizard.SelectedSeat = seat;
        Assert.Equal(GridSeatChoice.PlayerImageKey(seat.TeamId), wizard.PlayerImageKey);

        wizard.NextCommand.Execute(null);                 // -> Grid (whole field by default)
        Assert.Equal(WizardStep.Grid, wizard.Step);
        wizard.NextCommand.Execute(null);                 // -> Confirm
        Assert.Equal(WizardStep.Confirm, wizard.Step);
        wizard.NextCommand.Execute(null);                 // Create

        var request = factory.LastRequest!;
        Assert.NotNull(request.Character);
        Assert.Equal("Chosen Driver", request.Character!.Name); // the pre-seat identity reached creation
        // The default archetype's perks came through (profile lists them in perks.json order — a
        // deterministic order, so compared as a set here).
        Assert.Equal(
            wizard.Archetypes()[0].PerkIds.OrderBy(x => x, StringComparer.Ordinal),
            request.Character.PerkIds.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Contains("pace", request.Character.Stats.Keys);
    }
}

file static class CharacterWizardTestExtensions
{
    /// <summary>The wizard's character archetypes (the step is built lazily; this reaches them for
    /// the assertion once the character step exists).</summary>
    public static IReadOnlyList<Archetype> Archetypes(this NewCareerWizardViewModel wizard) =>
        wizard.Character!.Archetypes;
}
