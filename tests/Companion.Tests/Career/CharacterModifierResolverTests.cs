using System.Text.Json;
using Companion.Core.Character;
using Companion.Core.Json;

namespace Companion.Tests.Career;

public sealed class CharacterModifierResolverTests
{
    private static CharacterRules Rules() =>
        CharacterRules.Parse(CareerTestData.ReadRules("perks.json"));

    private static MasterySkillCatalog Catalog(CharacterRules rules) =>
        MasterySkillCatalog.Parse(
            CareerTestData.ReadRules("mastery-skills-v2.json"),
            rules,
            RacingDnaCatalog.Parse(CareerTestData.ReadRules("racing-dna-v2.json"), rules));

    private static CharacterProfile Profile(
        IReadOnlyList<string>? acquired,
        IReadOnlyList<string>? perks = null,
        double marketability = 0.5,
        string? chosenFlavor = "wetSkill",
        int effectsVersion = CharacterProfile.CurrentMasteryEffectsVersion) => new()
        {
            Stats = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["marketability"] = marketability,
            },
            PerkIds = perks ?? [],
            ProgressionVersion = CharacterLevelProgression.Level300Version,
            AcquiredSkillIds = acquired,
            ChosenFlavor = chosenFlavor,
            MasteryEffectsVersion = effectsVersion,
        };

    [Fact]
    public void VersionZero_IsTheExactLegacyResolution_EvenWhenMasteryIdsAlreadyExist()
    {
        CharacterRules rules = Rules();
        var profile = Profile(["pace_rhythm"], effectsVersion: 0);

        Assert.Same(
            PlayerPerkModifiers.Identity,
            CharacterModifierResolver.Resolve(profile, rules, masterySkills: null));

        profile = Profile(["pace_rhythm"], perks: ["test_driver"], effectsVersion: 0);
        var expected = PerkResolver.Resolve(profile, rules);
        var actual = CharacterModifierResolver.Resolve(profile, rules, Catalog(rules));

        Assert.Equal(Snapshot(expected), Snapshot(actual));
        Assert.Equal(0.0, actual.TalentDelta("raceSkill"));

        var marketability = CharacterModifierResolver.Resolve(
            Profile([], marketability: 0.85, effectsVersion: 0), rules, masterySkills: null);
        Assert.Equal(0.5, marketability.Marketability, 12);
    }

    [Fact]
    public void VersionOne_WithNoOwnedMasterySkills_SeedsProfileMarketabilityWithoutMasteryRates()
    {
        CharacterRules rules = Rules();
        var profile = Profile([], perks: ["sponsor_magnet"], marketability: 0.65);
        PlayerPerkModifiers legacy = PerkResolver.Resolve(profile, rules);

        PlayerPerkModifiers resolved = CharacterModifierResolver.Resolve(
            profile,
            rules,
            masterySkills: null);

        Assert.Equal(0.65 + legacy.Marketability - 0.5, resolved.Marketability, 12);
        Assert.Equal(0, resolved.MasteryEffectsVersion);
        Assert.Equal(legacy.RepRoundMult, resolved.RepRoundMult, 12);
        Assert.Equal(legacy.RepSeasonMult, resolved.RepSeasonMult, 12);
    }

    [Fact]
    public void ActiveMasteryState_RejectsUnsupportedVersionMissingCatalogUnknownAndDuplicateIds()
    {
        CharacterRules rules = Rules();
        MasterySkillCatalog catalog = Catalog(rules);

        Assert.Throws<InvalidOperationException>(() =>
            CharacterModifierResolver.Resolve(
                Profile([], effectsVersion: CharacterProfile.CurrentMasteryEffectsVersion + 1),
                rules,
                catalog));
        Assert.Throws<InvalidOperationException>(() =>
            CharacterModifierResolver.Resolve(
                Profile(["pace_rhythm"]) with { ProgressionVersion = CharacterLevelProgression.EraCappedVersion },
                rules,
                catalog));
        Assert.Throws<InvalidOperationException>(() =>
            CharacterModifierResolver.Resolve(Profile(["pace_rhythm"]), rules, masterySkills: null));
        Assert.Throws<InvalidOperationException>(() =>
            CharacterModifierResolver.Resolve(Profile(["not_a_mastery_skill"]), rules, catalog));
        Assert.Throws<InvalidOperationException>(() =>
            CharacterModifierResolver.Resolve(Profile(["pace_rhythm", "pace_rhythm"]), rules, catalog));
    }

    [Fact]
    public void BasicPaceAndPhysicalNodes_MapTalentAndCarEffects()
    {
        CharacterRules rules = Rules();
        MasterySkillCatalog catalog = Catalog(rules);

        var pace = CharacterModifierResolver.Resolve(Profile(["pace_rhythm"]), rules, catalog);
        Assert.Equal(CharacterProfile.CurrentMasteryEffectsVersion, pace.MasteryEffectsVersion);
        Assert.Equal(0.03, pace.TalentDelta("raceSkill"), 12);
        Assert.Equal(-0.02, pace.TalentDelta("qualifyingSkill"), 12);
        Assert.Equal(0.5, pace.Marketability, 12);

        var physical = CharacterModifierResolver.Resolve(
            Profile(["physical_core_strength"]), rules, catalog);
        Assert.Equal(0.06, physical.TalentDelta("stamina"), 12);
        Assert.Equal(0.002, physical.WeightScalarDelta, 12);
        Assert.Equal(0.0, physical.PowerScalarDelta);
        Assert.Equal(0.0, physical.DragScalarDelta);
    }

    [Fact]
    public void ConditionalEffects_AreDormantUntilExplicitFactsOrLegacyAliasesArePresent()
    {
        CharacterRules rules = Rules();
        MasterySkillCatalog catalog = Catalog(rules);

        var youngDormant = CharacterModifierResolver.Resolve(
            Profile(["mental_early_brilliance"]), rules, catalog);
        Assert.Equal(0.0, youngDormant.TalentDelta("raceSkill"));
        Assert.Equal(0.0, youngDormant.TalentDelta("qualifyingSkill"));

        var young = CharacterModifierResolver.Resolve(
            Profile(["mental_early_brilliance"]),
            rules,
            catalog,
            new HashSet<string>(["ageLtPeak"], StringComparer.Ordinal));
        Assert.Equal(0.05, young.TalentDelta("raceSkill"), 12);
        Assert.Equal(0.05, young.TalentDelta("qualifyingSkill"), 12);

        var veteran = CharacterModifierResolver.Resolve(
            Profile(["mental_early_brilliance"]),
            rules,
            catalog,
            new HashSet<string>(["ageGtePeak"], StringComparer.Ordinal));
        Assert.Equal(-0.02, veteran.TalentDelta("raceSkill"), 12);
        Assert.Equal(0.02, veteran.TalentDelta("qualifyingSkill"), 12);

        var lowTier = CharacterModifierResolver.Resolve(
            Profile(["v2_underdog_hero"]),
            rules,
            catalog,
            new HashSet<string>(["tierLte2"], StringComparer.Ordinal));
        Assert.Equal(0.25, lowTier.LowTierOfferWeightBonus, 12);

        var highTier = CharacterModifierResolver.Resolve(
            Profile(["v2_underdog_hero"]),
            rules,
            catalog,
            new HashSet<string>(["tierGte4"], StringComparer.Ordinal));
        Assert.Equal(0.85, highTier.RepRoundGainMult, 12);
        Assert.Equal(0.85, highTier.RepSeasonMult, 12);

        // Neither a high tier nor any other inferred fact is a works-team/era-transition fact.
        var inferredWorks = CharacterModifierResolver.Resolve(
            Profile(["business_commercial_titan"]),
            rules,
            catalog,
            new HashSet<string>(["tierGte4"], StringComparer.Ordinal));
        Assert.Equal(0.5, inferredWorks.Marketability, 12);
        var explicitWorks = CharacterModifierResolver.Resolve(
            Profile(["business_commercial_titan"]),
            rules,
            catalog,
            new HashSet<string>(["worksTeam"], StringComparer.Ordinal));
        Assert.Equal(0.35, explicitWorks.Marketability, 12);

        var noEraTransition = CharacterModifierResolver.Resolve(
            Profile(["v2_adaptable"]), rules, catalog);
        Assert.Equal(0.0, noEraTransition.TalentDelta("raceSkill"));
        var eraTransition = CharacterModifierResolver.Resolve(
            Profile(["v2_adaptable"]),
            rules,
            catalog,
            new HashSet<string>(["eraTransition"], StringComparer.Ordinal));
        Assert.Equal(0.03, eraTransition.TalentDelta("raceSkill"), 12);
    }

    [Fact]
    public void ChosenFlavorEffect_RequiresAndUsesThePersistedChoice()
    {
        CharacterRules rules = Rules();
        MasterySkillCatalog catalog = Catalog(rules);

        Assert.Throws<InvalidOperationException>(() =>
            CharacterModifierResolver.Resolve(
                Profile(["signature_focus"], chosenFlavor: null), rules, catalog));
        Assert.Throws<InvalidOperationException>(() =>
            CharacterModifierResolver.Resolve(
                Profile(["signature_focus"], chosenFlavor: "notAWritableRating"), rules, catalog));

        var resolved = CharacterModifierResolver.Resolve(
            Profile(["signature_focus"], chosenFlavor: "tyreManagement"), rules, catalog);
        Assert.Equal(0.08, resolved.TalentDelta("tyreManagement"), 12);
        Assert.Equal(0.0, resolved.TalentDelta("wetSkill"));
        Assert.Equal(-0.05, resolved.StatSoftCapDelta, 12);
    }

    [Fact]
    public void MasteryOnlyConditions_DoNotWakeLegacyConditionalEffects()
    {
        CharacterRules rules = Rules();
        MasterySkillCatalog catalog = Catalog(rules);
        var profile = Profile(["v2_underdog_hero"], perks: ["sponsor_magnet"]);
        PlayerPerkModifiers legacy = PerkResolver.Resolve(profile, rules);

        PlayerPerkModifiers resolved = CharacterModifierResolver.Resolve(
            profile,
            rules,
            catalog,
            activeConditions: null,
            masteryConditions: new HashSet<string>(["tierAtLeast4"], StringComparer.Ordinal));

        Assert.Equal(legacy.Marketability, resolved.Marketability, 12);
        Assert.Equal(0.85, resolved.RepSeasonMult, 12);
    }

    [Fact]
    public void EveryShippedEffect_ResolvesFromItsCanonicalSingleNodeProfile()
    {
        CharacterRules rules = Rules();
        MasterySkillCatalog catalog = Catalog(rules);
        Assert.Equal(90, catalog.Skills.Count);
        Assert.Equal(313, catalog.Skills.Sum(skill => skill.Effects.Count));

        foreach (MasterySkillDefinition skill in catalog.Skills)
        {
            var conditions = skill.Effects
                .Select(effect => effect.Condition)
                .Where(condition => condition is not null)
                .Select(condition => condition!)
                .ToHashSet(StringComparer.Ordinal);

            PlayerPerkModifiers resolved = CharacterModifierResolver.Resolve(
                Profile([skill.Id], chosenFlavor: "fuelManagement"),
                rules,
                catalog,
                conditions);

            Assert.NotNull(resolved);
        }
    }

    [Fact]
    public void AddAndMultiplyEffects_ComposeAroundLegacyInCatalogOrderAndNormalizeCauseKeys()
    {
        CharacterRules rules = Rules();
        MasterySkillCatalog catalog = Catalog(rules);
        string[] acquired =
        [
            "business_paddock_fixture",
            "team_franchise_driver",
            "business_contract_negotiator",
            "v2_cheap_contract",
            "v2_student_of_the_craft",
            "mental_adversity_engine",
            "pace_race_program",
        ];
        var profile = Profile(
            acquired,
            perks: ["test_driver", "sponsor_magnet", "qualifying_specialist"]);

        PlayerPerkModifiers resolved = CharacterModifierResolver.Resolve(profile, rules, catalog);
        PlayerPerkModifiers reversed = CharacterModifierResolver.Resolve(
            profile with { AcquiredSkillIds = acquired.Reverse().ToArray() }, rules, catalog);

        Assert.Equal(Snapshot(resolved), Snapshot(reversed));
        Assert.Equal((0.85 + 0.30) * 1.30, resolved.OfferExperienceMult, 12);
        Assert.Equal((1.10 - 0.40) * 1.25 * 1.40, resolved.SalaryAskMult, 12);
        Assert.Equal(0.90 * 0.95 * 1.25, resolved.XpMult("finishVsExpected"), 12);
        Assert.Equal(1.50 * 1.25, resolved.XpMult("dnfMechanical"), 12);
        Assert.Equal(1.0, resolved.XpMult("mechanicalDnf"));
        Assert.Equal(0.0, resolved.RoundXpFloorMultiplier, 12);
    }

    [Fact]
    public void ResolverAppliesItsAggregateClampsOnce_AndLeavesConsumerCombinationsSeparate()
    {
        CharacterRules rules = Rules();
        MasterySkillCatalog catalog = Catalog(rules);
        string[] allExceptDoublingJourneyman = catalog.Skills
            .Where(skill => skill.Id != "v2_journeyman")
            .Select(skill => skill.Id)
            .ToArray();
        var allConditions = catalog.Skills.SelectMany(skill => skill.Effects)
            .Select(effect => effect.Condition)
            .Where(condition => condition is not null)
            .Select(condition => condition!)
            .ToHashSet(StringComparer.Ordinal);
        var profile = Profile(
            allExceptDoublingJourneyman,
            perks: ["ice_in_the_veins", "safe_hands", "ironman", "iron_constitution", "sponsor_magnet"],
            marketability: 0.0);

        PlayerPerkModifiers resolved = CharacterModifierResolver.Resolve(
            profile, rules, catalog, allConditions);

        Assert.Equal(0.65, resolved.OpiRetention, 12);
        Assert.Equal(0.60, resolved.AnchorAlpha, 12);
        Assert.Equal(0.0, resolved.Marketability, 12);
        Assert.Equal(1.75, resolved.SalaryAskMult, 12);
        Assert.Equal(0.50, resolved.SalaryOfferMult, 12);
        Assert.Equal(0.25, resolved.AgeRiskMult, 12);
        Assert.Equal(0.10, resolved.DeclineAccelMult, 12);
        Assert.Equal(0.70, resolved.ErrorBlameScale, 12);
        Assert.Equal(0.60, resolved.BlameFloorBlend, 12);
        Assert.Equal(5.0, resolved.PayBudgetBu, 12);
        Assert.Equal(1, resolved.RepFloorRelaxTiers);

        var marketUpper = CharacterModifierResolver.Resolve(
            Profile(["media_global_icon"], marketability: 1.0), rules, catalog);
        Assert.Equal(1.0, marketUpper.Marketability, 12);

        // Reputation, round XP, and CAR remain as distinct components. Their consumers combine
        // these with sign/cause/base-scalar facts and apply the corresponding final clamp once.
        var all = CharacterModifierResolver.Resolve(
            Profile(catalog.Skills.Select(skill => skill.Id).ToArray()),
            rules,
            catalog,
            allConditions);
        Assert.True(all.RepRoundSignedMult > catalog.AggregateClamps["reputationMultiplier"][1]);
        Assert.True(all.RepSeasonMult > catalog.AggregateClamps["reputationMultiplier"][1]);
        Assert.True(all.XpMult("dnfMechanical") > catalog.AggregateClamps["roundXpMultiplier"][1]);
        Assert.Equal(0.0, all.RoundXpFloorMultiplier, 12);
        Assert.NotEqual(0.0, all.WeightScalarDelta);
    }

    [Fact]
    public void MasteryInjuryRisk_DistinguishesRiskDrawbacksFromProtectionOnly()
    {
        CharacterRules rules = Rules();
        MasterySkillCatalog catalog = Catalog(rules);

        var protection = CharacterModifierResolver.Resolve(
            Profile(["physical_recovery_habits"]), rules, catalog);
        var risk = CharacterModifierResolver.Resolve(
            Profile(["racecraft_switchback_school"]), rules, catalog);

        Assert.False(protection.MasteryInjuryRisk);
        Assert.True(risk.MasteryInjuryRisk);
    }

    private static string Snapshot(PlayerPerkModifiers modifiers) =>
        JsonSerializer.Serialize(modifiers, CoreJson.Options);
}
