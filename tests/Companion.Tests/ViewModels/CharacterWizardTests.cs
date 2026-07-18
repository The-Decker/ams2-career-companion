using Companion.Core.Character;
using Companion.Core.Career;
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

    private static RacingDnaCatalog DnaCatalog() =>
        RacingDnaCatalog.Parse(CareerTestData.ReadRules("racing-dna-v2.json"), Rules());

    private static MasterySkillCatalog MasteryCatalog(
        CharacterRules rules,
        RacingDnaCatalog racingDna) =>
        MasterySkillCatalog.Parse(
            CareerTestData.ReadRules("mastery-skills-v2.json"), rules, racingDna);

    private static void SelectCountry(CharacterViewModel vm, string code = "USA") =>
        vm.SelectedCountry = vm.CountryOptions.Single(option => option.Code == code);

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
        // Floor everything, then pour the freed talent into two stats, a real specialist, under cap.
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

        // Clear the preset, then select one MORE than the cap allows, using only zero-cost perks so
        // the CP net stays in budget and ONLY the count cap can fail the build.
        foreach (var selected in vm.Perks.Where(p => p.IsSelected).ToList())
            vm.TogglePerkCommand.Execute(selected);
        foreach (var perk in vm.Perks.Where(p => p.Cost == 0).Take(vm.MaxPerks!.Value + 1))
            vm.TogglePerkCommand.Execute(perk);

        Assert.Equal(vm.MaxPerks!.Value + 1, vm.SelectedPerkCount);
        Assert.True(vm.PerksInBudget);      // net is in budget, only the COUNT cap fails
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
        Assert.Equal(CharacterLevelProgression.EraCappedVersion, profile.ProgressionVersion);
        Assert.Null(profile.RacingDnaId);
        Assert.Null(profile.CreationBaseline);
    }

    [Fact]
    public void VersionTwoEditor_PublishesThirtyCardsAndAppliesTheAuthoredPreset()
    {
        var catalog = DnaCatalog();
        var vm = new CharacterViewModel(
            Rules(), "You", catalog, progressionVersion: CharacterLevelProgression.Level300Version);
        SelectCountry(vm);

        Assert.True(vm.IsProgressionV2);
        Assert.Equal(30, vm.RacingDnaCards.Count);
        Assert.Equal(catalog.Definitions.Select(definition => definition.Id), vm.RacingDnaCards.Select(card => card.Id));
        Assert.Equal("dna_prodigy", vm.SelectedRacingDna!.Id);
        Assert.Null(vm.SelectedArchetype);
        Assert.True(vm.IsValid, vm.Invalidity);

        var definition = catalog.Get("dna_prodigy", 1);
        Assert.Equal(definition.StartingStats["pace"], vm.Stats.Single(stat => stat.Id == "pace").Value, 6);
        Assert.Equal(
            definition.StartingTraitIds.OrderBy(id => id, StringComparer.Ordinal),
            vm.Perks.Where(perk => perk.IsSelected).Select(perk => perk.Id).OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal("Pace", vm.SelectedRacingDna.PrimaryFamilyLabel);
        Assert.Equal("Media", vm.SelectedRacingDna.SecondaryFamilyLabel);
        Assert.Equal("Pace / Media", vm.SelectedRacingDna.FamilyLine);
        Assert.Equal(definition.StartingTraitIds, vm.SelectedRacingDna.StartingTraitIds);
        Assert.Equal(
            definition.StartingTraitIds.Select(id => Rules().PerkById(id).Name),
            vm.SelectedRacingDna.StartingTraitNames);
        Assert.Equal(
            definition.PersistentEffects.Select(effect => effect.Summary),
            vm.SelectedRacingDna.PersistentEffectSummaries);
        Assert.Equal(
            definition.TradeoffEffects.Select(effect => effect.Summary),
            vm.SelectedRacingDna.TradeoffEffectSummaries);
        Assert.All(vm.Stats, stat =>
        {
            IReadOnlyList<double> range = catalog.CreationBudget.TalentRanges[stat.Id];
            Assert.Equal(range[0], stat.Min);
            Assert.Equal(range[1], stat.Max);
        });
        Assert.All(vm.MetaStats, stat =>
        {
            IReadOnlyList<double> range = catalog.CreationBudget.MetaRanges[stat.Id];
            Assert.Equal(range[0], stat.Min);
            Assert.Equal(range[1], stat.Max);
        });
    }

    [Fact]
    public void VersionTwoCountryPicker_UsesCompleteOfflineFlagCatalogAndPersistsWithoutChangingRandomBuild()
    {
        const long seed = 20260713;
        var first = new CharacterViewModel(
            Rules(), "Country Driver", DnaCatalog(),
            progressionVersion: CharacterLevelProgression.Level300Version,
            masterSeed: seed);
        var second = new CharacterViewModel(
            Rules(), "Country Driver", DnaCatalog(),
            progressionVersion: CharacterLevelProgression.Level300Version,
            masterSeed: seed);

        Assert.Equal(200, first.CountryOptions.Count);
        Assert.Equal(200, first.CountryOptions.Select(option => option.Code).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(200, first.CountryOptions.Select(option => option.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(200, first.CountryOptions.Select(option => option.FlagKey).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            first.CountryOptions.OrderBy(option => option.Name, StringComparer.Ordinal).Select(option => option.Code),
            first.CountryOptions.Select(option => option.Code));
        Assert.All(first.CountryOptions, option =>
        {
            Assert.False(string.IsNullOrWhiteSpace(option.Name));
            Assert.StartsWith("country.", option.FlagKey, StringComparison.Ordinal);
            Assert.Same(option, CharacterCountryCatalog.Find(option.Code));
        });
        string[] previouslyShippedCodes =
            ["AND", "AUT", "BEL", "BRA", "CAN", "DEU", "ESP", "FIN", "FRA", "GBR", "ITA", "JPN", "SWE", "USA"];
        Assert.All(previouslyShippedCodes, code =>
            Assert.Contains(first.CountryOptions, option => option.Code == code));
        Assert.Equal("country.england", CharacterCountryCatalog.Find("ENG")?.FlagKey);
        Assert.Equal("country.scotland", CharacterCountryCatalog.Find("SCO")?.FlagKey);
        Assert.Equal("country.hongkong", CharacterCountryCatalog.Find("HKG")?.FlagKey);
        Assert.Equal("country.kosovo", CharacterCountryCatalog.Find("XKX")?.FlagKey);
        Assert.Equal("country.palestine", CharacterCountryCatalog.Find("PSE")?.FlagKey);
        Assert.Equal("country.taiwan", CharacterCountryCatalog.Find("TWN")?.FlagKey);
        Assert.Equal("country.united_kingdom", CharacterCountryCatalog.Find("GBR")?.FlagKey);
        Assert.False(first.IsCountryValid);
        Assert.Contains("country", first.Invalidity!, StringComparison.OrdinalIgnoreCase);

        first.SelectedCountry = new CharacterCountryOption("ZZZ", "Unknown", "driver.unknown");
        Assert.False(first.IsCountryValid);
        Assert.Throws<InvalidOperationException>(() => first.BuildVersionTwoProfile());

        SelectCountry(first, "BRA");
        SelectCountry(second, "USA");
        Assert.True(first.IsCountryValid);
        Assert.Equal("BRAZIL", first.CountrySummary);

        first.RandomBuildCommand.Execute(null);
        second.RandomBuildCommand.Execute(null);
        CharacterProfile brazil = first.BuildVersionTwoProfile();
        CharacterProfile usa = second.BuildVersionTwoProfile();

        Assert.Equal("BRA", brazil.CountryCode);
        Assert.Equal("USA", usa.CountryCode);
        Assert.Equal(brazil with { CountryCode = null }, usa with { CountryCode = null });
        Assert.Equal("BRA", first.SelectedCountry!.Code);
        Assert.Equal("USA", second.SelectedCountry!.Code);
    }

    [Fact]
    public void VersionTwoCountryPicker_RejectsAmbiguousDuplicateNamesAndFlagKeys()
    {
        Assert.Throws<ArgumentException>(() => new CharacterViewModel(
            Rules(), "Country Driver", DnaCatalog(),
            progressionVersion: CharacterLevelProgression.Level300Version,
            countryOptions:
            [
                new("AAA", "Alpha", "country.alpha"),
                new("AAB", "Alpha", "country.beta"),
            ]));

        Assert.Throws<ArgumentException>(() => new CharacterViewModel(
            Rules(), "Country Driver", DnaCatalog(),
            progressionVersion: CharacterLevelProgression.Level300Version,
            countryOptions:
            [
                new("AAA", "Alpha", "country.alpha"),
                new("AAB", "Beta", "country.alpha"),
            ]));
    }

    [Fact]
    public void VersionTwoEditor_SeparatesFortyTwoCreationTraitsFromStableNinetySkillPreview()
    {
        var rules = Rules();
        var racingDna = RacingDnaCatalog.Parse(
            CareerTestData.ReadRules("racing-dna-v2.json"), rules);
        var mastery = MasteryCatalog(rules, racingDna);
        var vm = new CharacterViewModel(
            rules, "You", racingDna,
            progressionVersion: CharacterLevelProgression.Level300Version,
            masterySkillCatalog: mastery);
        SelectCountry(vm);

        Assert.Equal(42, vm.CreationTraitCount);
        (string Name, int Count)[] expectedTraitCounts =
        [
            ("pace", 7),
            ("racecraft", 5),
            ("physical", 5),
            ("mental", 5),
            ("business", 5),
            ("weather", 4),
            ("team", 4),
            ("media", 3),
            ("era", 4),
        ];
        Assert.Equal(
            expectedTraitCounts,
            vm.PerkCategories.Select(category => (category.Name, category.Perks.Count)));

        Assert.Equal(9, vm.MasteryPreviewFamilies.Count);
        Assert.Equal(mastery.FamilyOrder, vm.MasteryPreviewFamilies.Select(family => family.Id));
        Assert.All(vm.MasteryPreviewFamilies, family => Assert.Equal(10, family.Skills.Count));
        Assert.Equal(90, vm.MasteryPreviewSkillCount);
        Assert.Equal(
            mastery.Skills.Select(skill => skill.Id),
            vm.MasteryPreviewFamilies.SelectMany(family => family.Skills).Select(skill => skill.Id));
        Assert.All(vm.MasteryPreviewFamilies.SelectMany(family => family.Skills), skill =>
        {
            Assert.False(string.IsNullOrWhiteSpace(skill.Description));
            Assert.Equal(skill.RequiresIds.Count, skill.RequiresLabels.Count);
            Assert.NotEmpty(skill.Benefits);
            Assert.NotEmpty(skill.Drawbacks);
            Assert.NotEmpty(skill.Effects);
            Assert.All(skill.Effects, effect =>
                Assert.Contains(effect.ClassificationLabel, new[] { "EXPECTATION", "CAREER", "CAR" }));
        });

        Assert.Equal(7, vm.MasteryPreviewAttributeRailCount);
        Assert.Equal(119, vm.MasteryPreviewAttributeNodeCount);
        Assert.Equal(MasterySkillCatalog.DraftAttributeCost, vm.MasteryPreviewAttributeCost);
        Assert.Equal(
            mastery.AttributeRails.Select(rail => rail.Id),
            vm.MasteryPreviewAttributeRails.Select(rail => rail.Id));
        var liveAttributeNodes = MasterySkillGraph.Build(
                vm.BuildVersionTwoProfile(),
                CharacterLevelProgression.Level300Max,
                MasterySkillCatalog.SkillPointsMaximum,
                mastery,
                masteryCheckpointComplete: true)
            .Branches.SelectMany(branch => branch.Nodes)
            .Where(node => string.Equals(
                node.Kind,
                CharacterSkillPlanEntry.AttributeKind,
                StringComparison.Ordinal))
            .ToDictionary(node => node.Id, StringComparer.Ordinal);
        Assert.Equal(119, liveAttributeNodes.Count);
        Assert.All(vm.MasteryPreviewAttributeRails, rail =>
        {
            Assert.Equal(17, rail.StepCount);
            Assert.Equal(17, rail.Nodes.Count);
            Assert.Equal(rail.StepCount * rail.CostPerStep, rail.TotalCost);
            CharacterEffectClass expectedClassification = rules.Stats.TalentStats.Any(stat =>
                string.Equals(stat.Id, rail.StatId, StringComparison.Ordinal))
                ? CharacterEffectClass.Expectation
                : CharacterEffectClass.Career;
            Assert.All(rail.Nodes, node =>
            {
                Assert.Equal(node.RequiresIds.Count, node.RequiresLabels.Count);
                var effect = Assert.Single(node.Effects);
                Assert.Equal("benefit", effect.Kind);
                Assert.Equal(expectedClassification, effect.Classification);
                Assert.Equal(
                    PerkDescriber.ClassificationLabel(expectedClassification),
                    effect.ClassificationLabel);
                Assert.Equal(
                    $"+{node.StepValue:0.00} {rail.Name} (up to {node.CapValue:0.00})",
                    effect.Text);
                Assert.Equal(liveAttributeNodes[node.Id].Effects, node.Effects);
            });
        });
        Assert.Equal(
            85,
            vm.MasteryPreviewAttributeRails.SelectMany(rail => rail.Nodes)
                .Count(node => Assert.Single(node.Effects).Classification == CharacterEffectClass.Expectation));
        Assert.Equal(
            34,
            vm.MasteryPreviewAttributeRails.SelectMany(rail => rail.Nodes)
                .Count(node => Assert.Single(node.Effects).Classification == CharacterEffectClass.Career));
        Assert.DoesNotContain(
            vm.MasteryPreviewAttributeRails.SelectMany(rail => rail.Nodes).SelectMany(node => node.Effects),
            effect => effect.Classification == CharacterEffectClass.Car);
        Assert.All(vm.Perks, trait => Assert.NotEmpty(trait.Effects));
    }

    [Fact]
    public void VersionTwoEditor_PublishesTruthfulExpectationAndCompleteBuildStatus()
    {
        var rules = Rules();
        var racingDna = RacingDnaCatalog.Parse(
            CareerTestData.ReadRules("racing-dna-v2.json"), rules);
        var mastery = MasteryCatalog(rules, racingDna);
        var vm = new CharacterViewModel(
            rules, "You", racingDna,
            progressionVersion: CharacterLevelProgression.Level300Version,
            masterySkillCatalog: mastery);
        SelectCountry(vm);

        Assert.Equal(100, vm.ExpectedPerformanceComponents.Sum(component => component.Percent));
        Assert.Equal([60, 30, 10], vm.ExpectedPerformanceComponents.Select(component => component.Percent));
        Assert.Contains("team sets most", vm.ExpectedPerformanceBasisSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("consistent performance", vm.ExpectedPerformanceCalibrationSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Pace / Media", vm.RacingDnaSummary, StringComparison.Ordinal);
        Assert.Contains("5 DRIVING + 2 CAREER ATTRIBUTES", vm.AttributeBuildSummary, StringComparison.Ordinal);
        Assert.Contains("42 AVAILABLE", vm.CreationTraitSummary, StringComparison.Ordinal);
        Assert.Contains("DO NOT CARRY", vm.CreationBudgetSummary, StringComparison.Ordinal);
        Assert.Contains("LEVEL 300", vm.ProgressionSummary, StringComparison.Ordinal);
        Assert.Contains("499 TOTAL SKILL POINTS", vm.ProgressionSummary, StringComparison.Ordinal);
        Assert.Contains("90 SKILLS / 9 FAMILIES", vm.ProgressionSummary, StringComparison.Ordinal);
        Assert.Contains("119 ATTRIBUTE STEPS / 7 PATHS", vm.ProgressionSummary, StringComparison.Ordinal);
        Assert.All(vm.Stats.Concat(vm.MetaStats), stat =>
        {
            Assert.False(string.IsNullOrWhiteSpace(stat.CreationGuidance));
            Assert.Contains("not", stat.CreationGuidance, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Equal("COST 1", vm.Perks.First(perk => perk.Cost == 1).CreationPointLabel);
        Assert.Equal("NO COST", vm.Perks.First(perk => perk.Cost == 0).CreationPointLabel);
        Assert.StartsWith("REFUND ", vm.Perks.First(perk => perk.Cost < 0).CreationPointLabel,
            StringComparison.Ordinal);
        Assert.Empty(vm.ValidationIssues);
        Assert.StartsWith("BUILD READY", vm.BuildReadinessSummary, StringComparison.Ordinal);

        foreach (var trait in vm.Perks)
            trait.IsSelected = true;

        Assert.False(vm.IsValid);
        Assert.True(vm.ValidationIssues.Count >= 2);
        Assert.Equal(vm.ValidationIssues[0], vm.Invalidity);
        Assert.StartsWith("BUILD INCOMPLETE", vm.BuildReadinessSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyEditor_KeepsTheOptionalMasteryPreviewEmpty()
    {
        var rules = Rules();
        var racingDna = RacingDnaCatalog.Parse(
            CareerTestData.ReadRules("racing-dna-v2.json"), rules);
        var mastery = MasteryCatalog(rules, racingDna);
        var vm = new CharacterViewModel(rules, masterySkillCatalog: mastery);

        Assert.False(vm.IsProgressionV2);
        Assert.Equal(42, vm.CreationTraitCount);
        Assert.Empty(vm.MasteryPreviewFamilies);
        Assert.Equal(0, vm.MasteryPreviewSkillCount);
        Assert.Empty(vm.MasteryPreviewAttributeRails);
        Assert.Equal(0, vm.MasteryPreviewAttributeRailCount);
        Assert.Equal(0, vm.MasteryPreviewAttributeNodeCount);
    }

    [Fact]
    public void VersionTwoMasteryPreview_CannotCreateStartingAcquisitions()
    {
        var rules = Rules();
        var racingDna = RacingDnaCatalog.Parse(
            CareerTestData.ReadRules("racing-dna-v2.json"), rules);
        var mastery = MasteryCatalog(rules, racingDna);
        var vm = new CharacterViewModel(
            rules, "Preview Driver", racingDna,
            progressionVersion: CharacterLevelProgression.Level300Version,
            masterySkillCatalog: mastery);
        SelectCountry(vm);

        Assert.Equal(90, vm.MasteryPreviewSkillCount);
        _ = vm.MasteryPreviewFamilies.SelectMany(family => family.Skills).ToArray();

        CharacterProfile profile = vm.BuildVersionTwoProfile();
        Assert.Equal(CharacterProfile.CurrentExpectationModelVersion, profile.ExpectationModelVersion);
        Assert.Empty(profile.AcquiredSkillIds ?? []);
        Assert.Empty(profile.AcquiredAttributeNodeIds ?? []);
        Assert.Equal(0, profile.SkillPointsSpent);
        Assert.Equal(profile.PerkIds, profile.CreationBaseline!.TraitIds);
        Assert.Equal(profile.CreationPerkIds, profile.CreationBaseline.TraitIds);
    }

    [Fact]
    public void VersionTwoEditor_RejectsABlankDriverNameBeforeCreation()
    {
        var vm = new CharacterViewModel(
            Rules(), "You", DnaCatalog(),
            progressionVersion: CharacterLevelProgression.Level300Version);
        SelectCountry(vm);

        vm.Name = "   ";

        Assert.False(vm.IsDriverNameValid);
        Assert.False(vm.IsValid);
        Assert.Equal("Enter your driver's name.", vm.ValidationIssues[0]);
        Assert.Throws<InvalidOperationException>(() => vm.BuildVersionTwoProfile());

        vm.Name = "  Valid Driver  ";
        Assert.True(vm.IsDriverNameValid);
        Assert.True(vm.IsValid, vm.Invalidity);
        Assert.Equal("Valid Driver", vm.BuildVersionTwoProfile().Name);
    }

    [Fact]
    public void VersionTwoStaticChoice_IsExplicitAndPersistsInTheLosslessProfile()
    {
        var vm = new CharacterViewModel(
            Rules(), "DNA Driver", DnaCatalog(),
            progressionVersion: CharacterLevelProgression.Level300Version);
        SelectCountry(vm);
        vm.SelectedRacingDna = vm.RacingDnaCards.Single(card => card.Id == "dna_circuit_specialist");

        Assert.True(vm.HasRacingDnaChoice);
        Assert.True(vm.IsRacingDnaChoiceRequired);
        Assert.Contains("circuit family", vm.RacingDnaChoicePrompt!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            ["street", "power", "highSpeed", "technical"],
            vm.RacingDnaChoiceOptions.Select(option => option.Value));
        Assert.False(vm.IsRacingDnaChoiceValid);
        Assert.False(vm.IsValid);

        vm.RacingDnaChoiceValue = "technical";
        Assert.True(vm.IsRacingDnaChoiceValid);
        Assert.True(vm.IsValid, vm.Invalidity);

        var profile = vm.BuildVersionTwoProfile();
        Assert.Equal(CharacterLevelProgression.Level300Version, profile.ProgressionVersion);
        Assert.Equal(CharacterProfile.CurrentMasteryEffectsVersion, profile.MasteryEffectsVersion);
        Assert.Equal(CharacterProfile.CurrentExpectationModelVersion, profile.ExpectationModelVersion);
        Assert.Equal("dna_circuit_specialist", profile.RacingDnaId);
        Assert.Equal(1, profile.RacingDnaVersion);
        Assert.Equal("technical", profile.RacingDnaChoice);
        Assert.Equal(0, profile.CpUnspent);
        Assert.Equal(0, profile.CpSpent);
        Assert.Equal(0, profile.SkillPointsSpent);
        Assert.NotNull(profile.CreationBaseline);
        Assert.Equal(vm.Stats.Select(stat => stat.Id), profile.CreationBaseline!.Stats.Keys);
        Assert.Equal(vm.MetaStats.Select(stat => stat.Id), profile.CreationBaseline.Meta.Keys);
        Assert.Equal(profile.PerkIds, profile.CreationBaseline.TraitIds);
        Assert.Equal(profile.CreationPerkIds, profile.CreationBaseline.TraitIds);
        Assert.Equal(profile.ChosenFlavor, profile.CreationBaseline.ChosenFlavor);
    }

    [Fact]
    public void VersionTwoRandomBuild_UsesMasterSeedAndOrdinalAndProjectsACompleteProfile()
    {
        const long seed = 20260713;
        var catalog = DnaCatalog();
        var context = new RacingDnaChoiceContext(
            [new("driver.prost", "Prost"), new("driver.senna", "Senna")],
            [new("BRA", "Brazil"), new("GBR", "United Kingdom")]);
        var first = new CharacterViewModel(
            Rules(), "  Random Driver  ", catalog, context,
            CharacterLevelProgression.Level300Version, seed);
        var second = new CharacterViewModel(
            Rules(), "  Random Driver  ", catalog, context,
            CharacterLevelProgression.Level300Version, seed);
        SelectCountry(first, "BRA");
        SelectCountry(second, "BRA");

        for (int ordinal = 0; ordinal < 4; ordinal++)
        {
            first.RandomBuildCommand.Execute(null);
            second.RandomBuildCommand.Execute(null);
            var firstProfile = first.BuildVersionTwoProfile();
            var secondProfile = second.BuildVersionTwoProfile();

            Assert.Equal(firstProfile, secondProfile);
            Assert.Equal(CharacterLevelProgression.Level300Version, firstProfile.ProgressionVersion);
            Assert.Equal(CharacterProfile.CurrentMasteryEffectsVersion, firstProfile.MasteryEffectsVersion);
            Assert.Equal(CharacterProfile.CurrentExpectationModelVersion, firstProfile.ExpectationModelVersion);
            Assert.NotNull(firstProfile.CreationBaseline);
            catalog.ValidateCreation(firstProfile);

            var pure = RacingDnaRandomBuild.Create(
                catalog, unchecked((ulong)seed), ordinal,
                new RacingDnaRandomContext
                {
                    EligibleRivalDriverIds = ["driver.prost", "driver.senna"],
                    NationalityAffinities = ["BRA", "GBR"],
                },
                "Random Driver", CharacterViewModel.DefaultAge);
            Assert.Equal(pure.RacingDnaId, firstProfile.RacingDnaId);
            Assert.Equal(pure.RacingDnaVersion, firstProfile.RacingDnaVersion);
            Assert.Equal(pure.RacingDnaChoice, firstProfile.RacingDnaChoice);
            Assert.Equal(pure.Stats, firstProfile.Stats);
            Assert.Equal(
                pure.PerkIds.OrderBy(id => id, StringComparer.Ordinal),
                firstProfile.PerkIds.OrderBy(id => id, StringComparer.Ordinal));
        }
    }

    [Fact]
    public void VersionTwoDynamicChoice_UsesOnlyCallerSuppliedStableIds()
    {
        var context = new RacingDnaChoiceContext(
            RivalDrivers:
            [
                new RacingDnaChoiceOption("driver.prost", "Alain Prost"),
                new RacingDnaChoiceOption("driver.senna", "Ayrton Senna"),
            ],
            Nationalities:
            [
                new RacingDnaChoiceOption("BRA", "Brazil"),
                new RacingDnaChoiceOption("GBR", "United Kingdom"),
            ]);
        var vm = new CharacterViewModel(
            Rules(), "DNA Driver", DnaCatalog(), context,
            CharacterLevelProgression.Level300Version);
        SelectCountry(vm);
        vm.SelectedRacingDna = vm.RacingDnaCards.Single(card => card.Id == "dna_duelist");

        Assert.Equal(
            ["driver.prost", "driver.senna"],
            vm.RacingDnaChoiceOptions.Select(option => option.Value));
        vm.RacingDnaChoiceValue = "driver.unknown";
        Assert.False(vm.IsRacingDnaChoiceValid);
        vm.RacingDnaChoiceValue = "driver.senna";
        Assert.True(vm.IsRacingDnaChoiceValid);
        Assert.Equal("driver.senna", vm.BuildVersionTwoProfile().RacingDnaChoice);

        vm.SelectedRacingDna = vm.RacingDnaCards.Single(card => card.Id == "dna_national_hero");
        Assert.Equal(["BRA", "GBR"], vm.RacingDnaChoiceOptions.Select(option => option.Value));
    }

    [Fact]
    public void VersionTwoEditor_RejectsMissingDynamicContextAndMalformedChoiceContext()
    {
        var vm = new CharacterViewModel(
            Rules(), "DNA Driver", DnaCatalog(),
            progressionVersion: CharacterLevelProgression.Level300Version);
        vm.SelectedRacingDna = vm.RacingDnaCards.Single(card => card.Id == "dna_duelist");
        vm.RacingDnaChoiceValue = "driver.senna";
        Assert.Empty(vm.RacingDnaChoiceOptions);
        Assert.False(vm.IsRacingDnaChoiceValid);

        var duplicate = new RacingDnaChoiceContext(
            [
                new RacingDnaChoiceOption("driver.senna", "Senna"),
                new RacingDnaChoiceOption("driver.senna", "A. Senna"),
            ],
            []);
        Assert.Throws<ArgumentException>(() => new CharacterViewModel(
            Rules(), "DNA Driver", DnaCatalog(), duplicate,
            CharacterLevelProgression.Level300Version));
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

        // The player REPLACES the seat's driver on their own (locked) card, new name, not the AI's.
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
        Assert.Null(request.ExperienceMode);
        Assert.Equal(CharacterLevelProgression.EraCappedVersion, request.Character!.ProgressionVersion);
        Assert.Equal("Chosen Driver", request.Character!.Name); // the pre-seat identity reached creation
        // The default archetype's perks came through (profile lists them in perks.json order, a
        // deterministic order, so compared as a set here).
        Assert.Equal(
            wizard.Archetypes()[0].PerkIds.OrderBy(x => x, StringComparer.Ordinal),
            request.Character.PerkIds.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Contains("pace", request.Character.Stats.Keys);
    }

    [Fact]
    public void Wizard_ExplicitDynastyEmitsModeAndCompleteVersionTwoProfileAtomically()
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
            seedSource: new Random(9),
            experienceMode: CareerExperienceModes.GrandPrixDynasty);

        Assert.True(wizard.IsProgressionV2);
        Assert.Equal(CareerExperienceModes.GrandPrixDynasty, wizard.ExperienceMode);
        Assert.Equal("GRAND PRIX DYNASTY", wizard.ExperienceModeLabel);
        wizard.SelectedPack = Assert.Single(wizard.Packs);
        wizard.NextCommand.Execute(null);
        if (wizard.HasWarnings) wizard.ProceedAnyway = true;
        wizard.NextCommand.Execute(null);
        Assert.True(wizard.Character!.IsProgressionV2);
        Assert.Equal(30, wizard.Character.RacingDnaCards.Count);
        Assert.Equal(90, wizard.Character.MasteryPreviewSkillCount);
        Assert.Contains("LEVEL 300", wizard.CampaignPacingSummary, StringComparison.Ordinal);
        Assert.Contains("499 CAREER SP", wizard.CampaignPacingSummary, StringComparison.Ordinal);
        Assert.Contains("DNA", wizard.CharacterCreationSummary, StringComparison.Ordinal);
        SelectCountry(wizard.Character, "GBR");
        wizard.Character.Name = "V2 Driver";

        // Re-entering verification for the same pack must not erase authored DNA/customization.
        wizard.BackCommand.Execute(null);
        wizard.NextCommand.Execute(null);
        Assert.Equal("V2 Driver", wizard.Character.Name);

        wizard.NextCommand.Execute(null);
        wizard.SelectedSeat = wizard.Seats.First(seat => seat.LiveryName == TestPackBuilder.StockLivery2);
        wizard.NextCommand.Execute(null); // -> Grid
        wizard.NextCommand.Execute(null); // -> Confirm

        var notifications = new List<string?>();
        wizard.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        wizard.Character.Name = " ";
        Assert.False(wizard.Character.IsValid);
        Assert.False(wizard.CanCreate);
        Assert.Contains(nameof(NewCareerWizardViewModel.CanCreate), notifications);
        notifications.Clear();
        wizard.Character.Name = "V2 Driver";
        Assert.True(wizard.Character.IsValid, wizard.Character.Invalidity);
        Assert.True(wizard.CanCreate);
        Assert.Contains(nameof(NewCareerWizardViewModel.CanCreate), notifications);

        wizard.NextCommand.Execute(null); // Create

        var request = Assert.IsType<CareerCreationRequest>(factory.LastRequest);
        Assert.Equal(CareerExperienceModes.GrandPrixDynasty, request.ExperienceMode);
        var profile = Assert.IsType<CharacterProfile>(request.Character);
        Assert.Equal(CharacterLevelProgression.Level300Version, profile.ProgressionVersion);
        Assert.Equal(CharacterProfile.CurrentExpectationModelVersion, profile.ExpectationModelVersion);
        Assert.Equal("GBR", profile.CountryCode);
        Assert.Equal("dna_prodigy", profile.RacingDnaId);
        Assert.Equal(1, profile.RacingDnaVersion);
        Assert.NotNull(profile.CreationBaseline);
        Assert.Equal(0, profile.CpUnspent);
        Assert.Equal(0, profile.SkillPointsSpent);
    }

    [Fact]
    public void Wizard_DuelistCannotChooseTheDriverWhoseSeatTheyReplace()
    {
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), Path.Combine(_root, "packs", "pack"));
        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());
        var wizard = new NewCareerWizardViewModel(
            environment, new FakeCareerFactory(),
            packSearchRoots: [Path.Combine(_root, "packs")],
            careersDirectory: Path.Combine(_root, "careers"),
            seedSource: new Random(9),
            experienceMode: CareerExperienceModes.GrandPrixDynasty);

        wizard.SelectedPack = Assert.Single(wizard.Packs);
        wizard.NextCommand.Execute(null);
        if (wizard.HasWarnings) wizard.ProceedAnyway = true;
        wizard.NextCommand.Execute(null);
        SelectCountry(wizard.Character!);
        wizard.Character!.SelectedRacingDna = wizard.Character.RacingDnaCards.Single(card => card.Id == "dna_duelist");
        wizard.Character.RacingDnaChoiceValue = "driver.hulme";
        Assert.True(wizard.Character.IsValid, wizard.Character.Invalidity);
        wizard.NextCommand.Execute(null);

        wizard.SelectedSeat = wizard.Seats.Single(seat => seat.DriverId == "driver.hulme");
        Assert.NotNull(wizard.RacingDnaContextError);
        Assert.False(wizard.CanGoNext);

        wizard.SelectedSeat = wizard.Seats.Single(seat => seat.DriverId == "driver.brabham");
        Assert.Null(wizard.RacingDnaContextError);
        Assert.True(wizard.CanGoNext);
    }

    [Fact]
    public void Wizard_DynamicRivalContext_ContainsOnlyDriversWithCampaignEntries()
    {
        var basePack = TestPackBuilder.TwoRoundPack();
        var pack = basePack with
        {
            Drivers = [.. basePack.Drivers, TestPackBuilder.Driver("driver.catalog-only")],
        };
        TestPackBuilder.Write(pack, Path.Combine(_root, "packs", "pack"));
        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());
        var wizard = new NewCareerWizardViewModel(
            environment, new FakeCareerFactory(),
            packSearchRoots: [Path.Combine(_root, "packs")],
            careersDirectory: Path.Combine(_root, "careers"),
            seedSource: new Random(9),
            experienceMode: CareerExperienceModes.GrandPrixDynasty);

        wizard.SelectedPack = Assert.Single(wizard.Packs);
        wizard.NextCommand.Execute(null);
        if (wizard.HasWarnings) wizard.ProceedAnyway = true;
        wizard.NextCommand.Execute(null);
        wizard.Character!.SelectedRacingDna = wizard.Character.RacingDnaCards
            .Single(card => card.Id == "dna_duelist");

        Assert.Equal(
            basePack.Entries.Select(entry => entry.DriverId).OrderBy(id => id, StringComparer.Ordinal),
            wizard.Character.RacingDnaChoiceOptions.Select(option => option.Value));
        Assert.DoesNotContain(
            wizard.Character.RacingDnaChoiceOptions,
            option => option.Value == "driver.catalog-only");
    }

    [Fact]
    public void Wizard_InvalidVersionTwoSeed_DisablesRandomBuildAndValidSeedRestartsAtOrdinalZero()
    {
        const long replacementSeed = 1234567;
        var pack = TestPackBuilder.TwoRoundPack();
        TestPackBuilder.Write(pack, Path.Combine(_root, "packs", "pack"));
        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());
        var wizard = new NewCareerWizardViewModel(
            environment, new FakeCareerFactory(),
            packSearchRoots: [Path.Combine(_root, "packs")],
            careersDirectory: Path.Combine(_root, "careers"),
            seedSource: new Random(9),
            experienceMode: CareerExperienceModes.GrandPrixDynasty);

        wizard.SelectedPack = Assert.Single(wizard.Packs);
        wizard.NextCommand.Execute(null);
        if (wizard.HasWarnings) wizard.ProceedAnyway = true;
        wizard.NextCommand.Execute(null);
        var character = Assert.IsType<CharacterViewModel>(wizard.Character);
        SelectCountry(character);
        Assert.True(character.RandomBuildCommand.CanExecute(null));
        character.RandomBuildCommand.Execute(null);

        wizard.MasterSeedText = "not-a-seed";
        Assert.False(character.RandomBuildCommand.CanExecute(null));
        var beforeDisabledExecute = character.BuildVersionTwoProfile();
        character.RandomBuildCommand.Execute(null);
        Assert.Equal(beforeDisabledExecute, character.BuildVersionTwoProfile());

        wizard.MasterSeedText = replacementSeed.ToString();
        Assert.True(character.RandomBuildCommand.CanExecute(null));
        character.RandomBuildCommand.Execute(null);
        var actual = character.BuildVersionTwoProfile();
        var expected = RacingDnaRandomBuild.Create(
            DnaCatalog(), unchecked((ulong)replacementSeed), 0,
            new RacingDnaRandomContext
            {
                EligibleRivalDriverIds = pack.Entries
                    .Select(entry => entry.DriverId)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToArray(),
                NationalityAffinities = [],
            },
            "You", CharacterViewModel.DefaultAge);
        Assert.Equal(expected.RacingDnaId, actual.RacingDnaId);
        Assert.Equal(expected.RacingDnaChoice, actual.RacingDnaChoice);
        Assert.Equal(expected.Stats, actual.Stats);
    }

    [Fact]
    public void Wizard_VersionTwoRandomBuild_JournalsTheFinalNormalizedProfileWithoutRedrawing()
    {
        const long seed = 20260713;
        // The minimal pack intentionally has no Country metadata. Random Build must still work by
        // excluding only the nationality-dependent DNA from this roll's eligible definitions.
        var pack = TestPackBuilder.TwoRoundPack();
        TestPackBuilder.Write(pack, Path.Combine(_root, "packs", "pack"));
        var factory = new FakeCareerFactory();
        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());
        var wizard = new NewCareerWizardViewModel(
            environment, factory,
            packSearchRoots: [Path.Combine(_root, "packs")],
            careersDirectory: Path.Combine(_root, "careers"),
            seedSource: new Random(9),
            experienceMode: CareerExperienceModes.GrandPrixDynasty);

        wizard.SelectedPack = Assert.Single(wizard.Packs);
        wizard.NextCommand.Execute(null);
        if (wizard.HasWarnings) wizard.ProceedAnyway = true;
        wizard.NextCommand.Execute(null);
        Assert.Equal(WizardStep.Character, wizard.Step);
        Assert.True(long.TryParse(wizard.MasterSeedText, out _));

        wizard.MasterSeedText = seed.ToString();
        wizard.Character!.Name = "Randomized Driver";
        SelectCountry(wizard.Character, "BRA");
        Assert.True(wizard.Character.RandomBuildCommand.CanExecute(null));
        wizard.Character.RandomBuildCommand.Execute(null);
        var normalized = wizard.Character.BuildVersionTwoProfile();

        wizard.NextCommand.Execute(null);
        string? forbiddenSeatDriver = normalized.RacingDnaId == "dna_duelist"
            ? normalized.RacingDnaChoice
            : null;
        wizard.SelectedSeat = wizard.Seats.First(seat => seat.DriverId != forbiddenSeatDriver);
        wizard.NextCommand.Execute(null);
        wizard.NextCommand.Execute(null);
        wizard.NextCommand.Execute(null);

        var request = Assert.IsType<CareerCreationRequest>(factory.LastRequest);
        Assert.Equal(seed, request.MasterSeed);
        Assert.Equal(normalized, request.Character);
        Assert.Equal(CharacterProfile.CurrentMasteryEffectsVersion, request.Character!.MasteryEffectsVersion);
        Assert.NotEqual("dna_national_hero", request.Character.RacingDnaId);
    }

    [Fact]
    public void SingleCareerWizardRejectsPassportModeUntilItsContainerExists()
    {
        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());

        Assert.Throws<ArgumentException>(() => new NewCareerWizardViewModel(
            environment, new FakeCareerFactory(),
            experienceMode: CareerExperienceModes.RacingPassport));
    }
}

file static class CharacterWizardTestExtensions
{
    /// <summary>The wizard's character archetypes (the step is built lazily; this reaches them for
    /// the assertion once the character step exists).</summary>
    public static IReadOnlyList<Archetype> Archetypes(this NewCareerWizardViewModel wizard) =>
        wizard.Character!.Archetypes;
}
