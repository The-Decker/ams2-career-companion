using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Companion.Core.Character;
using Companion.Core.Json;

namespace Companion.Tests.Career;

public sealed class MasterySkillCatalogTests
{
    private static string Json() => CareerTestData.ReadRules("mastery-skills-v2.json");
    private static CharacterRules LegacyRules() => CharacterRules.Parse(CareerTestData.ReadRules("perks.json"));
    private static RacingDnaCatalog Dna(CharacterRules rules) =>
        RacingDnaCatalog.Parse(CareerTestData.ReadRules("racing-dna-v2.json"), rules);
    private static MasterySkillCatalog Catalog()
    {
        var rules = LegacyRules();
        return MasterySkillCatalog.Parse(Json(), rules, Dna(rules));
    }

    [Fact]
    public void ShippedCatalog_HasTheExactWaveOneShapeAndBudget()
    {
        var catalog = Catalog();

        Assert.Equal(1, catalog.SchemaVersion);
        Assert.Equal(2, catalog.ProgressionVersion);
        Assert.Equal(499, catalog.MaximumSkillPoints);
        Assert.Equal(285, catalog.MasteryOverrideLevel);
        Assert.Equal(1, catalog.SkillResetPolicy.Version);
        Assert.Equal(500, catalog.SkillResetPolicy.MinimumBaseXp);
        Assert.Equal(1, catalog.SkillResetPolicy.CumulativeXpNumerator);
        Assert.Equal(20, catalog.SkillResetPolicy.CumulativeXpDenominator);
        Assert.Equal(50, catalog.SkillResetPolicy.RoundUpXp);
        Assert.Equal(1, catalog.SkillResetPolicy.RepeatCostIncrement);
        Assert.Equal(MasterySkillCatalog.RequiredFamilies, catalog.FamilyOrder);
        Assert.Equal(MasterySkillCatalog.RequiredAggregateClamps.Keys.Order(StringComparer.Ordinal),
            catalog.AggregateClamps.Keys.Order(StringComparer.Ordinal));
        foreach (var (lever, range) in MasterySkillCatalog.RequiredAggregateClamps)
            Assert.Equal(range, catalog.AggregateClamps[lever]);
        Assert.Equal(0.05, catalog.CarAudit.ComposedAdvantageMaximum);
        Assert.Equal(90, catalog.Skills.Count);
        Assert.Equal(90, catalog.Skills.Select(node => node.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(280, catalog.TotalSkillCost);
        Assert.Equal(232, catalog.OrdinaryMaximumSkillCost);
        Assert.Equal(119, catalog.MaximumAttributeCost);
        Assert.Equal(399, catalog.MaximumCompleteCost);

        foreach (string family in catalog.FamilyOrder)
        {
            var nodes = catalog.Skills.Where(node => node.Family == family).ToArray();
            Assert.Equal(10, nodes.Length);
            Assert.Equal(Enumerable.Range(1, 10), nodes.OrderBy(node => node.Order).Select(node => node.Order));
            for (int tier = 1; tier <= 5; tier++)
                Assert.Equal(2, nodes.Count(node => node.Tier == tier));
            Assert.All(nodes.Where(node => node.Tier == 5), node =>
                Assert.Equal(family + ".capstone", node.ExclusiveGroup));
        }

        Assert.All(catalog.Skills, node =>
        {
            Assert.NotEmpty(node.Benefits);
            Assert.NotEmpty(node.Drawbacks);
            Assert.NotEmpty(node.Effects);
            Assert.All(node.Effects, effect => Assert.NotNull(effect.Classification));
        });
    }

    [Fact]
    public void AttributeRails_CoverAllSevenStatsWithStableSequentialNodes()
    {
        var catalog = Catalog();
        var rules = LegacyRules();
        string[] stats = rules.Stats.TalentStats.Select(stat => stat.Id)
            .Concat(rules.Stats.MetaStats.Select(stat => stat.Id))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(stats, catalog.AttributeRails.Select(rail => rail.Stat).Order(StringComparer.Ordinal));
        Assert.Equal(7 * 17, catalog.AttributeNodes.Count);
        Assert.All(catalog.AttributeRails, rail =>
        {
            Assert.Equal(0.05, rail.StepValue);
            Assert.Equal(0.99, rail.CapValue);
            Assert.Equal(17, rail.StepCount);
            Assert.Equal(1, rail.CostPerStep);
            var nodes = catalog.AttributeNodes.Where(node => node.RailId == rail.Id)
                .OrderBy(node => node.Order).ToArray();
            Assert.Equal(17, nodes.Length);
            Assert.Empty(nodes[0].Requires);
            for (int index = 1; index < nodes.Length; index++)
                Assert.Equal([nodes[index - 1].Id], nodes[index].Requires);
        });
    }

    [Fact]
    public void CatalogMembership_NeverCollidesWithTheImmutableLegacyGraph()
    {
        var catalog = Catalog();
        var rules = LegacyRules();
        var legacy = rules.Perks.Select(perk => perk.Id)
            .Concat(rules.SkillTree.StatNodes.Select(node => node.Id))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(42, rules.Perks.Count);
        Assert.Equal(15, rules.SkillTree.StatNodes.Count);
        Assert.DoesNotContain(catalog.Skills, skill => legacy.Contains(skill.Id));
        Assert.DoesNotContain(catalog.AttributeNodes, node => legacy.Contains(node.Id));
    }

    [Fact]
    public void Parse_NormalizesNodeAndPrerequisiteOrderDeterministically()
    {
        var expected = Catalog();
        var root = JsonNode.Parse(Json())!.AsObject();
        var nodes = root["nodes"]!.AsArray();
        var reversedNodes = nodes.Reverse().Select(node => node!.DeepClone()).ToArray();
        nodes.Clear();
        foreach (var node in reversedNodes)
        {
            var requires = node!["requires"]!.AsArray();
            var reversedRequires = requires.Reverse().Select(required => required!.DeepClone()).ToArray();
            requires.Clear();
            foreach (var required in reversedRequires)
                requires.Add(required);
            nodes.Add(node);
        }
        var rules = LegacyRules();
        var actual = MasterySkillCatalog.Parse(root.ToJsonString(), rules, Dna(rules));

        Assert.Equal(expected.Skills.Select(node => node.Id), actual.Skills.Select(node => node.Id));
        Assert.Equal(
            expected.Skills.Select(node => string.Join('|', node.Requires)),
            actual.Skills.Select(node => string.Join('|', node.Requires)));
        Assert.Equal(expected.AttributeNodes.Select(node => node.Id), actual.AttributeNodes.Select(node => node.Id));
    }

    [Fact]
    public void ShippedCatalog_HasAnExplicitSemanticSnapshotHash()
    {
        string hash = CanonicalHash(JsonNode.Parse(Json())!);

        Assert.Equal("dff1eec0dd718fc9fb05255d8c04d73280f0d810f0bfe663dd8ce03a3ba81c0b", hash);
    }

    [Fact]
    public void LegacyFortyTwoPerksAndFifteenStatNodes_HaveIndependentSemanticHashes()
    {
        var root = JsonNode.Parse(CareerTestData.ReadRules("perks.json"))!.AsObject();
        var perkView = new JsonObject { ["perks"] = root["perks"]!.DeepClone() };
        var statView = new JsonObject
        {
            ["statNodes"] = root["skillTree"]!["statNodes"]!.DeepClone(),
        };

        Assert.Equal("5e920d65c314c554c77868a4c170093dd62a4bead4419bab94a8da40429a0baa",
            CanonicalHash(perkView));
        Assert.Equal("9e727958b5a2cdead7032e99e30145fcea2db747f59cc4ac94be0da0e24a30de",
            CanonicalHash(statView));
    }

    [Theory]
    [InlineData("legacyCollision")]
    [InlineData("badGate")]
    [InlineData("crossFamily")]
    [InlineData("badExclusiveGroup")]
    [InlineData("badEffectClassification")]
    [InlineData("badEffectTarget")]
    [InlineData("badEffectCondition")]
    [InlineData("inertEffect")]
    [InlineData("missingMagnitude")]
    [InlineData("badOperation")]
    [InlineData("excessiveMagnitude")]
    [InlineData("teamCarOverCap")]
    [InlineData("badCarAudit")]
    [InlineData("missingResetPolicy")]
    [InlineData("badResetPolicy")]
    [InlineData("unknownRootMember")]
    [InlineData("unknownNodeMember")]
    [InlineData("misspelledEffectCondition")]
    public void Parse_RejectsCatalogDriftThatWouldChangeGraphOrMechanics(string mutation)
    {
        var root = JsonNode.Parse(Json())!.AsObject();
        var nodes = root["nodes"]!.AsArray();
        switch (mutation)
        {
            case "legacyCollision":
                nodes[0]!["id"] = "rain_man";
                break;
            case "badGate":
                nodes[0]!["unlockLevel"] = 2;
                break;
            case "crossFamily":
                nodes[2]!["requires"]![0] = nodes[10]!["id"]!.GetValue<string>();
                break;
            case "badExclusiveGroup":
                nodes[8]!["exclusiveGroup"] = "wrong.capstone";
                break;
            case "badEffectClassification":
                nodes[0]!["effects"]![0]!["classification"] = "car";
                break;
            case "badEffectTarget":
                nodes[0]!["effects"]![0]!["target"] = "vehicleReliability";
                break;
            case "badEffectCondition":
                nodes[0]!["effects"]![0]!["condition"] = "currentDate";
                break;
            case "inertEffect":
                nodes[0]!["effects"]![0]!["magnitude"] = 0;
                break;
            case "missingMagnitude":
                nodes[0]!["effects"]![0]!.AsObject().Remove("magnitude");
                break;
            case "badOperation":
                nodes[0]!["effects"]![0]!["operation"] = "multiply";
                break;
            case "excessiveMagnitude":
                nodes[0]!["effects"]![0]!["magnitude"] = 0.31;
                break;
            case "teamCarOverCap":
                var teamPlayer = nodes.Single(node => node!["id"]!.GetValue<string>() == "v2_team_player")!;
                teamPlayer["effects"]![0]!["magnitude"] = 0.02;
                break;
            case "badCarAudit":
                root["carAudit"]!["teamCrossBranchMaximum"] = 0.020;
                break;
            case "missingResetPolicy":
                root.Remove("skillResetPolicy");
                break;
            case "badResetPolicy":
                root["skillResetPolicy"]!["roundUpXp"] = 0;
                break;
            case "unknownRootMember":
                root["unexpected"] = true;
                break;
            case "unknownNodeMember":
                nodes[0]!["unexpected"] = true;
                break;
            case "misspelledEffectCondition":
                var rainMan = nodes.Single(node =>
                    node!["id"]!.GetValue<string>() == "v2_rain_man")!;
                var wetPower = rainMan["effects"]!.AsArray().Single(effect =>
                    effect!["target"]!.GetValue<string>() == "power" &&
                    effect["condition"]!.GetValue<string>() == "wetRound")!.AsObject();
                wetPower.Remove("condition");
                wetPower["conditon"] = "wetRound";
                break;
        }

        var rules = LegacyRules();
        Assert.Throws<JsonException>(() =>
            MasterySkillCatalog.Parse(root.ToJsonString(), rules, Dna(rules)));
    }

    [Fact]
    public void EveryLegalWeatherDistanceContextStaysInsideTheAms2ScalarBoundary()
    {
        var catalog = Catalog();
        foreach (string weather in new[] { "wetRound", "dryRound" })
        foreach (string distance in new[] { "longRace", "shortRace" })
        {
            var totals = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["weight"] = 1.0,
                ["power"] = 1.0,
                ["drag"] = 1.0,
            };
            foreach (var effect in catalog.Skills.SelectMany(node => node.Effects).Where(effect =>
                         effect.Classification == CharacterEffectClass.Car &&
                         (effect.Condition is null || effect.Condition == weather || effect.Condition == distance)))
            {
                Assert.Equal(MasteryEffectOperation.Add, effect.Operation);
                totals[effect.Target!] += effect.Magnitude;
            }

            Assert.All(totals.Values, value => Assert.InRange(value, 0.900, 1.100));
        }
    }

    [Fact]
    public void TeamUnconditionalCarPathHonorsItsAuthoredAdvantageCeiling()
    {
        var teamEffects = Catalog().Skills
            .Where(node => node.Family == "team")
            .SelectMany(node => node.Effects)
            .Where(effect => effect.Classification == CharacterEffectClass.Car && effect.Condition is null)
            .ToArray();
        double advantage = teamEffects.Sum(effect => effect.Target switch
        {
            "power" => effect.Magnitude,
            "drag" or "weight" => -effect.Magnitude,
            _ => 0.0,
        });

        Assert.InRange(advantage, double.NegativeInfinity,
            Catalog().CarAudit.TeamCrossBranchMaximum + 1e-12);
    }

    [Fact]
    public void EveryDnaTraitStackPlusTheCompleteMasteryBuildStaysInsideTheFrozenCarEnvelope()
    {
        var rules = LegacyRules();
        var dna = Dna(rules);
        var catalog = Catalog();
        var extrema = new Dictionary<string, (double Min, double Max)>(StringComparer.Ordinal)
        {
            ["weight"] = (double.PositiveInfinity, double.NegativeInfinity),
            ["power"] = (double.PositiveInfinity, double.NegativeInfinity),
            ["drag"] = (double.PositiveInfinity, double.NegativeInfinity),
        };
        double minAdvantage = double.PositiveInfinity;
        double maxAdvantage = double.NegativeInfinity;
        int scenarios = 0;

        foreach (var identity in dna.Definitions)
        foreach (string weather in new[] { "wetRound", "dryRound" })
        foreach (string distance in new[] { "longRace", "shortRace" })
        {
            scenarios++;
            var conditions = new HashSet<string>([weather, distance], StringComparer.Ordinal);
            var legacy = PerkResolver.Resolve(identity.StartingTraitIds, rules, conditions);
            var mastery = catalog.Skills.SelectMany(node => node.Effects)
                .Where(effect => effect.Classification == CharacterEffectClass.Car &&
                                 (effect.Condition is null || conditions.Contains(effect.Condition)))
                .GroupBy(effect => effect.Target!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Sum(effect => effect.Magnitude), StringComparer.Ordinal);
            var values = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["weight"] = 1.0 + legacy.WeightScalarDelta + mastery.GetValueOrDefault("weight"),
                ["power"] = 1.0 + legacy.PowerScalarDelta + mastery.GetValueOrDefault("power"),
                ["drag"] = 1.0 + legacy.DragScalarDelta + mastery.GetValueOrDefault("drag"),
            };

            foreach (var (axis, value) in values)
            {
                Assert.InRange(value, catalog.AggregateClamps["carScalar"][0], catalog.AggregateClamps["carScalar"][1]);
                var prior = extrema[axis];
                extrema[axis] = (Math.Min(prior.Min, value), Math.Max(prior.Max, value));
            }
            double advantage = values["power"] - values["weight"] - values["drag"] + 1.0;
            minAdvantage = Math.Min(minAdvantage, advantage);
            maxAdvantage = Math.Max(maxAdvantage, advantage);
        }

        Assert.Equal(30 * 4, scenarios);
        Assert.InRange(minAdvantage, catalog.CarAudit.ComposedAdvantageMinimum, catalog.CarAudit.ComposedAdvantageMaximum);
        Assert.InRange(maxAdvantage, catalog.CarAudit.ComposedAdvantageMinimum, catalog.CarAudit.ComposedAdvantageMaximum);
        Assert.Equal(1.019, extrema["weight"].Min, 3);
        Assert.Equal(1.043, extrema["weight"].Max, 3);
        Assert.Equal(0.926, extrema["power"].Min, 3);
        Assert.Equal(1.036, extrema["power"].Max, 3);
        Assert.Equal(0.944, extrema["drag"].Min, 3);
        Assert.Equal(1.014, extrema["drag"].Max, 3);
    }

    [Fact]
    public void ExhaustiveOrdinaryFamilySubsetsConfirmThe232PointPreMasteryCeiling()
    {
        var catalog = Catalog();
        int ordinaryMaximum = 0;
        foreach (string family in catalog.FamilyOrder)
        {
            var nodes = catalog.Skills.Where(node => node.Family == family)
                .OrderBy(node => node.Order).ToArray();
            int familyMaximum = 0;
            for (int mask = 0; mask < 1 << nodes.Length; mask++)
            {
                var owned = nodes.Where((_, index) => (mask & (1 << index)) != 0)
                    .Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
                bool closed = nodes.Where(node => owned.Contains(node.Id))
                    .All(node => node.Requires.All(owned.Contains));
                bool exclusive = nodes.Where(node => owned.Contains(node.Id) && node.ExclusiveGroup is not null)
                    .GroupBy(node => node.ExclusiveGroup, StringComparer.Ordinal)
                    .All(group => group.Count() <= 1);
                if (!closed || !exclusive)
                    continue;
                familyMaximum = Math.Max(
                    familyMaximum,
                    nodes.Where(node => owned.Contains(node.Id)).Sum(node => node.Cost));
            }
            ordinaryMaximum += familyMaximum;
        }

        Assert.Equal(232, ordinaryMaximum);
        Assert.Equal(catalog.OrdinaryMaximumSkillCost, ordinaryMaximum);
    }

    private static string CanonicalHash(JsonNode node)
    {
        string canonical = node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
