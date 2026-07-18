using System.Text.Json;
using Companion.Core.Character;
using Companion.Core.Json;

namespace Companion.Tests.Career;

/// <summary>The Core parser + model for the shipped driver-character rules (perks.json). The CI
/// balance audit (<see cref="PerkBalanceAuditTests"/>) proves the DATA is balanced; these prove the
/// CODE parses it into the typed model the sim consumes, and that the load-time integrity gate
/// (<see cref="CharacterRules.Validate"/>) catches structural corruption.</summary>
public sealed class CharacterRulesTests
{
    private static CharacterRules Shipped() => CharacterRules.Parse(CareerTestData.ReadRules("perks.json"));

    [Fact]
    public void Parse_TheShippedFile_YieldsTheDocumentedShape()
    {
        var rules = Shipped();

        Assert.Equal(2, rules.Version);
        Assert.Equal(42, rules.Perks.Count);
        Assert.Equal(13, rules.Creation.Archetypes.Count);
        Assert.Equal(6, rules.CharacterPoints.CreationBudget);
        Assert.Equal(3, rules.CharacterPoints.MaxRefundHeadroom);
        Assert.Equal(9, rules.CharacterPoints.MaxNetSpend);
        Assert.Equal(5, rules.CharacterPoints.MaxPerks);
        Assert.Equal(3, rules.Levels.LevelGrants.CharacterPointsPerLevel);
        Assert.Equal(5, rules.Stats.TalentStats.Count);
        Assert.Equal(2, rules.Stats.MetaStats.Count);
        Assert.Equal(9, rules.SkillTree.BranchOrder.Count);
        Assert.Equal(["physical", "business", "media"], rules.SkillTree.MetaBranches);
        Assert.Equal(15, rules.SkillTree.StatNodes.Count);
        Assert.Equal(5, rules.Levels.LevelGrants.MilestoneEveryLevels);
        Assert.Equal(1, rules.Respec.RespecTokenGrantsPerMilestone);

        var lateBraker = rules.PerkById("late_braker");
        Assert.Equal(2, lateBraker.Tier);
        Assert.Equal(6, lateBraker.UnlockLevel);
        Assert.Equal(["engineers_favorite"], lateBraker.Requires);
    }

    [Fact]
    public void PerkById_ResolvesAKnownPerk_AndReportsAnUnknownOne()
    {
        var rules = Shipped();

        var rainMan = rules.PerkById("rain_man");
        Assert.Equal("weather", rainMan.Category);
        Assert.Contains(rainMan.Effects, e => e.Kind == "benefit");
        Assert.Contains(rainMan.Effects, e => e.Kind == "drawback");

        Assert.False(rules.TryGetPerk("no_such_perk", out _));
    }

    [Fact]
    public void PerkEffect_ClassificationIsOptionalAndSerializesAsCamelCase()
    {
        var legacy = JsonSerializer.Deserialize<PerkEffect>(
            """{ "kind":"benefit", "lever":"statDelta" }""", CoreJson.Options)!;

        Assert.Null(legacy.Classification);
        Assert.DoesNotContain(
            "\"classification\"",
            JsonSerializer.Serialize(legacy, CoreJson.Options),
            StringComparison.Ordinal);

        var authored = JsonSerializer.Deserialize<PerkEffect>(
            """{ "kind":"benefit", "lever":"statDelta", "classification":"car" }""",
            CoreJson.Options)!;

        Assert.Equal(CharacterEffectClass.Car, authored.Classification);
        Assert.Contains(
            "\"classification\": \"car\"",
            JsonSerializer.Serialize(authored, CoreJson.Options),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TalentStat_MapsToRatingsAndComputesTheWrittenValue()
    {
        var rules = Shipped();
        var pace = rules.Stats.TalentStats.Single(s => s.Id == "pace");

        Assert.Equal(["raceSkill"], pace.MapsTo);
        // writtenRating = writeBase + writeSpan*stat = 0.35 + 0.55*0.5 (before perk deltas / clamp).
        Assert.Equal(0.625, pace.WrittenRating(0.5), 6);
        Assert.Equal(0.8175, pace.WrittenRating(0.85), 6);

        // The two-field talent stat exists and carries both target ratings.
        var adaptability = rules.Stats.TalentStats.Single(s => s.Id == "adaptability");
        Assert.Equal(["wetSkill", "tyreManagement"], adaptability.MapsTo);
    }

    [Fact]
    public void XpCurve_IsGeometricAndStrictlyIncreasing()
    {
        var curve = Shipped().Levels.XpCurve;

        Assert.Equal(0, curve.XpForLevel(1));            // level 1 is the start, no cost
        Assert.Equal(100, curve.XpForLevel(2));          // baseXpToLevel2
        Assert.Equal(135, curve.XpForLevel(3));          // round(100 * 1.35)
        Assert.True(curve.XpForLevel(4) > curve.XpForLevel(3));

        // Cumulative level lookup: below the level-2 threshold is still level 1; at it, level 2.
        Assert.Equal(1, curve.LevelForTotalXp(99));
        Assert.Equal(2, curve.LevelForTotalXp(100));
        Assert.Equal(2, curve.LevelForTotalXp(234));     // 100 + 135 = 235 to reach level 3
        Assert.Equal(3, curve.LevelForTotalXp(235));
        Assert.Equal(curve.MaxLevel, curve.LevelForTotalXp(long.MaxValue));

        var levels = Shipped().Levels;
        Assert.Equal(26, levels.LevelForTotalXp(long.MaxValue, 1967, useEraSoftCap: true));
        Assert.Equal(30, levels.LevelForTotalXp(long.MaxValue, 1967, useEraSoftCap: false));
        Assert.Equal(30, levels.LevelForTotalXp(long.MaxValue, 2022, useEraSoftCap: true));
    }

    [Fact]
    public void EveryArchetypePerkIdResolvesToARealPerk()
    {
        var rules = Shipped();
        foreach (var archetype in rules.Creation.Archetypes)
        {
            foreach (string perkId in archetype.PerkIds)
                Assert.True(rules.TryGetPerk(perkId, out _),
                    $"Archetype '{archetype.Id}' references unknown perk '{perkId}'.");
            Assert.Contains("pace", archetype.StartStats.Keys);
            Assert.Contains("marketability", archetype.StartMeta.Keys);
        }
    }

    [Fact]
    public void Validate_RejectsAnArchetypeReferencingAMissingPerk()
    {
        // A minimal but structurally complete file whose one archetype names a perk that isn't defined.
        const string json = """
        {
          "version": 2,
          "characterPoints": { "creationBudget": 10, "minBudgetAfterSpend": 0, "maxRefundHeadroom": 6 },
          "stats": {
            "talentStats": [ { "id": "pace", "mapsTo": ["raceSkill"], "writeBase": 0.35, "writeSpan": 0.55 } ],
            "metaStats": [ { "id": "marketability", "default": 0.5 } ]
          },
          "levels": {
            "xpCurve": { "baseXpToLevel2": 100, "growth": 1.35, "maxLevel": 30 },
            "xpSources": { "perRound": {}, "perSeason": {} },
            "levelGrants": {}
          },
          "creation": { "archetypes": [ { "id": "x", "name": "X", "startStats": {"pace":0.5}, "startMeta": {"marketability":0.5}, "perkIds": ["ghost_perk"] } ] },
          "perks": [ { "id": "real_perk", "name": "Real", "category": "pace", "cost": 0, "effects": [] } ]
        }
        """;

        var ex = Assert.Throws<JsonException>(() => CharacterRules.Parse(json));
        Assert.Contains("ghost_perk", ex.Message);
    }

    [Fact]
    public void Parse_ToleratesCommentsAndTrailingCommas()
    {
        const string json = """
        {
          // a hand-authored file may carry comments
          "version": 2,
          "characterPoints": { "creationBudget": 10, "minBudgetAfterSpend": 0, "maxRefundHeadroom": 6 },
          "stats": {
            "talentStats": [ { "id": "pace", "mapsTo": ["raceSkill"] }, ],
            "metaStats": [ { "id": "marketability", "default": 0.5 }, ]
          },
          "levels": {
            "xpCurve": { "baseXpToLevel2": 100, "growth": 1.35, "maxLevel": 30 },
            "xpSources": { "perRound": {}, "perSeason": {} },
            "levelGrants": {}
          },
          "creation": { "archetypes": [] },
          "perks": [ { "id": "p", "name": "P", "category": "pace", "cost": 0, "effects": [] }, ]
        }
        """;

        var rules = CharacterRules.Parse(json);
        Assert.Single(rules.Perks);
    }

    [Fact]
    public void Validate_RejectsUnknownSkillTreeDependency()
    {
        string json = CareerTestData.ReadRules("perks.json")
            .Replace("\"requires\": [\"engineers_favorite\"]",
                "\"requires\": [\"ghost_perk\"]", StringComparison.Ordinal);

        var ex = Assert.Throws<JsonException>(() => CharacterRules.Parse(json));
        Assert.Contains("ghost_perk", ex.Message);
    }

    [Fact]
    public void Validate_RejectsNonMonotonicDependencyTier()
    {
        string json = CareerTestData.ReadRules("perks.json")
            .Replace("\"branch\": \"pace\", \"tier\": 2, \"unlockLevel\": 6, \"requires\": [\"engineers_favorite\"]",
                "\"branch\": \"pace\", \"tier\": 1, \"unlockLevel\": 6, \"requires\": [\"engineers_favorite\"]",
                StringComparison.Ordinal);

        var ex = Assert.Throws<JsonException>(() => CharacterRules.Parse(json));
        Assert.Contains("tier must exceed", ex.Message);
    }
}
