using System.Text.Json;
using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Json;

namespace Companion.Tests.Career;

/// <summary>The pure between-season spend model (character depth 4): available CP = creation + level
/// grants − spent; a spend raises a stat or adds a perk and charges CpSpent.</summary>
public sealed class CharacterSpendTests
{
    private const string PackHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static CharacterRules Rules() => CharacterRules.Parse(CareerTestData.ReadRules("perks.json"));

    private static CampaignProgressionPlan Plan() => CampaignProgressionPlan.CreateSmgp(
        new PinnedCampaignSeason
        {
            PackId = "smgp-1",
            PackVersion = "1.0.0",
            Sha256 = PackHash,
            Year = 1990,
            ChampionshipRoundCount = 16,
        });

    private static CharacterProfile Character(int creationCp = 3, int spent = 0) => new()
    {
        Name = "Dev",
        Stats = new Dictionary<string, double>(StringComparer.Ordinal) { ["pace"] = 0.60 },
        PerkIds = ["sunday_driver"],
        CpUnspent = creationCp,
        CpSpent = spent,
    };

    [Fact]
    public void AvailableCp_IsCreationPlusLevelGrantsMinusSpent()
    {
        var rules = Rules();
        int perLevel = rules.Levels.LevelGrants.CharacterPointsPerLevel; // 3 in the shipped rules

        Assert.Equal(3, CharacterProgress.AvailableCp(Character(creationCp: 3), level: 1, rules)); // no grants at L1
        Assert.Equal(3 + perLevel * 4, CharacterProgress.AvailableCp(Character(creationCp: 3), level: 5, rules));
        Assert.Equal(3 + perLevel * 4 - 5, CharacterProgress.AvailableCp(Character(creationCp: 3, spent: 5), level: 5, rules));
    }

    [Theory]
    [InlineData(CharacterLevelProgression.LegacyVersion)]
    [InlineData(CharacterLevelProgression.EraCappedVersion)]
    public void AvailableSkillPoints_LegacyVersionsUseTheExactLegacyFormula(int progressionVersion)
    {
        var rules = Rules();
        var character = Character(creationCp: 7, spent: 5) with { ProgressionVersion = progressionVersion };

        Assert.Equal(
            CharacterProgress.AvailableCp(character, level: 5, rules),
            CharacterProgress.AvailableSkillPoints(
                character,
                level: 5,
                rules,
                completedSeasons: int.MinValue,
                campaignProgressionPlan: null));
    }

    [Fact]
    public void AvailableSkillPoints_VersionTwoUsesLevelAndSeasonGatesAndIgnoresLegacyCpFields()
    {
        var rules = Rules();
        var character = Character(creationCp: 999, spent: 888) with
        {
            ProgressionVersion = CharacterLevelProgression.Level300Version,
            SkillPointsSpent = 5,
        };
        var plan = Plan();

        Assert.Equal(244, CharacterProgress.AvailableSkillPoints(
            character, level: 300, rules, completedSeasons: 8, plan));
        Assert.Equal(160, CharacterProgress.AvailableSkillPoints(
            character, level: 100, rules, completedSeasons: 16, plan));
        Assert.Equal(0, CharacterProgress.AvailableSkillPoints(
            character, level: 300, rules, completedSeasons: 0, plan));
    }

    [Fact]
    public void AvailableSkillPoints_VersionTwoRequiresAValidPinnedPlan()
    {
        var rules = Rules();
        var character = Character() with
        {
            ProgressionVersion = CharacterLevelProgression.Level300Version,
        };

        Assert.Throws<InvalidOperationException>(() => CharacterProgress.AvailableSkillPoints(
            character, level: 2, rules, completedSeasons: 1, campaignProgressionPlan: null));
        Assert.Throws<InvalidOperationException>(() => CharacterProgress.AvailableSkillPoints(
            character, level: 2, rules, completedSeasons: 1, Plan() with { MasterySeason = 15 }));
    }

    [Fact]
    public void AvailableSkillPoints_RejectsUnknownProgressionVersion()
    {
        var character = Character() with { ProgressionVersion = 999 };

        Assert.Throws<NotSupportedException>(() => CharacterProgress.AvailableSkillPoints(
            character, level: 1, Rules(), completedSeasons: 0, campaignProgressionPlan: null));
    }

    [Fact]
    public void Apply_StatStep_RaisesTheStatAndChargesCp()
    {
        var rules = Rules();
        double step = rules.Levels.LevelGrants.StatStepValue; // 0.05

        var after = CharacterProgress.Apply(Character(), CharacterSpend.Stat("pace", cost: 1), rules);

        Assert.Equal(0.60 + step, after.Stat("pace"), 6);
        Assert.Equal(1, after.CpSpent);
    }

    [Fact]
    public void Apply_VersionTwoChargesSkillPointsSpentAndLeavesLegacyCpFieldsAlone()
    {
        var character = Character(creationCp: 11, spent: 7) with
        {
            ProgressionVersion = CharacterLevelProgression.Level300Version,
            SkillPointsSpent = 2,
        };

        var after = CharacterProgress.Apply(
            character,
            CharacterSpend.Stat("raise_pace_1", cost: 3),
            Rules());

        Assert.Equal(7, after.CpSpent);
        Assert.Equal(5, after.SkillPointsSpent);
        Assert.Equal(0.65, after.Stat("pace"), 6);
        Assert.Equal(["raise_pace_1"], after.SkillNodeIds);
    }

    [Fact]
    public void Apply_VersionOneStillChargesCpSpentOnly()
    {
        var character = Character(spent: 2) with
        {
            ProgressionVersion = CharacterLevelProgression.EraCappedVersion,
            SkillPointsSpent = 9,
        };

        var after = CharacterProgress.Apply(
            character,
            CharacterSpend.Stat("pace", cost: 2),
            Rules());

        Assert.Equal(4, after.CpSpent);
        Assert.Equal(9, after.SkillPointsSpent);
    }

    [Fact]
    public void Apply_StatTreeNode_RaisesItsMappedStatAndRecordsNodeOwnership()
    {
        var rules = Rules();

        var after = CharacterProgress.Apply(
            Character(), CharacterSpend.Stat("raise_pace_1", cost: 1), rules);

        Assert.Equal(0.65, after.Stat("pace"), 6);
        Assert.Equal(["raise_pace_1"], after.SkillNodeIds);
    }

    [Fact]
    public void AvailableCp_ConsumesThePerLevelModifier()
    {
        var rules = Rules();
        var basePerk = rules.Perks.Single(perk => perk.Id == "sunday_driver");
        var bonusPerk = basePerk with
        {
            Id = "bonus_per_level",
            Effects =
            [
                new PerkEffect
                {
                    Kind = "benefit", Lever = "statPoints", Target = "perLevel",
                    Magnitude = 1, CpEquivalent = 1,
                },
            ],
        };
        rules = CharacterRules.Parse(JsonSerializer.Serialize(
            rules with { Perks = rules.Perks.Append(bonusPerk).ToList() }, CoreJson.Options));
        var character = Character() with { PerkIds = ["bonus_per_level"] };

        Assert.Equal(3 + (3 + 1) * 4, CharacterProgress.AvailableCp(character, level: 5, rules));
    }

    [Fact]
    public void Apply_StatStep_CanDevelopBeyondTheCreationCap_UpToStatCapPerRating()
    {
        var rules = Rules();
        var maxed = Character() with
        {
            Stats = new Dictionary<string, double>(StringComparer.Ordinal) { ["pace"] = rules.Levels.LevelGrants.StatCapPerRating },
        };

        var after = CharacterProgress.Apply(maxed, CharacterSpend.Stat("pace", 1), rules);
        Assert.Equal(rules.Levels.LevelGrants.StatCapPerRating, after.Stat("pace"), 6); // clamped, not exceeded
    }

    [Fact]
    public void Apply_Perk_AddsItOnce_AndChargesCp()
    {
        var rules = Rules();

        var after = CharacterProgress.Apply(Character(), CharacterSpend.Perk("rain_man", cost: 1), rules);
        Assert.Contains("rain_man", after.PerkIds);
        Assert.Equal(1, after.CpSpent);

        // Adding a perk the driver already has is a no-op on the list (still charged by the caller's cost).
        var again = CharacterProgress.Apply(after, CharacterSpend.Perk("rain_man", 0), rules);
        Assert.Equal(after.PerkIds.Count, again.PerkIds.Count);
    }

    [Fact]
    public void ApplyAll_AppliesASequence()
    {
        var rules = Rules();
        var after = CharacterProgress.ApplyAll(Character(), new[]
        {
            CharacterSpend.Stat("pace", 1),
            CharacterSpend.Stat("pace", 1),
            CharacterSpend.Perk("rain_man", 1),
        }, rules);

        Assert.Equal(0.70, after.Stat("pace"), 6); // 0.60 + 2 steps
        Assert.Contains("rain_man", after.PerkIds);
        Assert.Equal(3, after.CpSpent);
    }

    [Fact]
    public void RespecTokens_ArriveAtMilestones_AndAreConsumed()
    {
        var rules = Rules();

        Assert.Equal(0, CharacterRespecMath.AvailableTokens(level: 4, used: 0, rules));
        Assert.Equal(1, CharacterRespecMath.AvailableTokens(level: 5, used: 0, rules));
        Assert.Equal(0, CharacterRespecMath.AvailableTokens(level: 5, used: 1, rules));
        Assert.Equal(1, CharacterRespecMath.AvailableTokens(level: 10, used: 1, rules));
    }

    [Fact]
    public void ApplyRespecs_RemovesThePerkAndRefundsItsSpend()
    {
        var character = Character(spent: 2) with
        {
            PerkIds = ["sunday_driver", "rain_man"],
            CreationPerkIds = ["sunday_driver"],
        };

        var after = CharacterProgress.ApplyRespecs(character,
            [new CharacterRespec { NodeId = "rain_man", Refund = 1 }]);

        Assert.Equal(["sunday_driver"], after.PerkIds);
        Assert.Equal(1, after.CpSpent);
    }

    [Fact]
    public void ApplyRespecs_VersionTwoRejectsLegacyTokenMutation()
    {
        var character = Character(spent: 2) with
        {
            ProgressionVersion = CharacterLevelProgression.Level300Version,
            PerkIds = ["sunday_driver", "rain_man"],
            SkillPointsSpent = 2,
        };

        Assert.Throws<InvalidOperationException>(() => CharacterProgress.ApplyRespecs(
            character,
            [new CharacterRespec { NodeId = "rain_man", Refund = 1 }]));
    }
}
