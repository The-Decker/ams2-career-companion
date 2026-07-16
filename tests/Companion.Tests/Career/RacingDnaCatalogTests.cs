using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Companion.Core.Character;

namespace Companion.Tests.Career;

public sealed class RacingDnaCatalogTests
{
    private static CharacterRules CharacterRules() =>
        Companion.Core.Character.CharacterRules.Parse(CareerTestData.ReadRules("perks.json"));

    private static string CatalogJson() => CareerTestData.ReadRules("racing-dna-v2.json");

    private static RacingDnaCatalog Catalog() =>
        RacingDnaCatalog.Parse(CatalogJson(), CharacterRules());

    [Fact]
    public void ShippedCatalog_HasThirtyVersionedZeroCostIdentitiesWithLockedFamilyCoverage()
    {
        var catalog = Catalog();

        Assert.Equal(1, catalog.SchemaVersion);
        Assert.Equal(CharacterLevelProgression.Level300Version, catalog.ProgressionVersion);
        Assert.Equal(6, catalog.CreationBudget.TraitBudget);
        Assert.Equal(30, catalog.Definitions.Count);
        Assert.Equal(30, catalog.Definitions.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(catalog.Definitions, definition => Assert.Equal(1, definition.Version));

        var coverage = catalog.Definitions
            .GroupBy(definition => definition.PrimaryFamily, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        Assert.Equal(4, coverage["pace"]);
        Assert.Equal(4, coverage["racecraft"]);
        Assert.Equal(4, coverage["mental"]);
        foreach (string family in new[] { "physical", "business", "weather", "team", "media", "era" })
            Assert.Equal(3, coverage[family]);

        foreach (var definition in catalog.Definitions)
        {
            Assert.NotEmpty(definition.PersistentEffects);
            Assert.NotEmpty(definition.TradeoffEffects);
            double net = definition.PersistentEffects.Sum(effect => effect.BalanceValue) +
                         definition.TradeoffEffects.Sum(effect => effect.BalanceValue);
            Assert.InRange(net, -0.5, 0.5);
            Assert.DoesNotContain(
                definition.PersistentEffects.Concat(definition.TradeoffEffects),
                effect => effect.Classification == CharacterEffectClass.Car);
        }
    }

    [Fact]
    public void ThirteenPreservedDnaPresets_MatchLegacyArchetypesExactly()
    {
        var rules = CharacterRules();
        var catalog = RacingDnaCatalog.Parse(CatalogJson(), rules);

        foreach (var archetype in rules.Creation.Archetypes)
        {
            var dna = catalog.Get($"dna_{archetype.Id}", 1);
            Assert.Equal(archetype.StartStats, dna.StartingStats);
            Assert.Equal(archetype.StartMeta, dna.StartingMeta);
            Assert.Equal(archetype.PerkIds, dna.StartingTraitIds);
        }
    }

    [Fact]
    public void ExactVersionLookup_NeverFallsForward()
    {
        var catalog = Catalog();

        Assert.Equal("The Prodigy", catalog.Get("dna_prodigy", 1).Name);
        Assert.False(catalog.TryGet("dna_prodigy", 2, out _));
        Assert.Throws<KeyNotFoundException>(() => catalog.Get("dna_prodigy", 2));
    }

    [Fact]
    public void ShippedVersionOneCatalog_HasAnExplicitImmutableSnapshotHash()
    {
        // The loose rules file is not read by any fold yet. This guard makes every future balance
        // edit to an already-addressable (id, version) definition deliberate: preserve v1 and add a
        // new version rather than silently changing an old career's identity.
        string canonical = JsonNode.Parse(CatalogJson())!.ToJsonString(
            new JsonSerializerOptions { WriteIndented = false });
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

        Assert.Equal("2baf975d2f534c5de50cbe9114ebbfcc87b5827eb739a22fad28484511512d6e", hash);
    }

    [Fact]
    public void ChoiceBearingIdentity_RequiresAndRestrictsItsJournaledChoice()
    {
        var catalog = Catalog();
        var definition = catalog.Get("dna_circuit_specialist", 1);

        var valid = Profile(definition, "technical");
        Assert.Same(definition, catalog.ValidateCreation(valid));
        Assert.Throws<InvalidOperationException>(() => catalog.ValidateCreation(valid with { RacingDnaChoice = null }));
        Assert.Throws<InvalidOperationException>(() => catalog.ValidateCreation(valid with { RacingDnaChoice = "oval" }));

        var noChoice = catalog.Get("dna_prodigy", 1);
        Assert.Throws<InvalidOperationException>(() =>
            catalog.ValidateCreation(Profile(noChoice, "technical")));
    }

    [Fact]
    public void DynamicChoice_RequiresAStableValueWithoutPretendingToKnowTheModeRoster()
    {
        var catalog = Catalog();
        var duelist = catalog.Get("dna_duelist", 1);

        Assert.Same(duelist, catalog.ValidateCreation(Profile(duelist, "driver.senna")));
        Assert.Throws<InvalidOperationException>(() => catalog.ValidateCreation(Profile(duelist, null)));
    }

    [Fact]
    public void Parse_RejectsDuplicateCompositeIdentityAndUnbalancedEffects()
    {
        var duplicate = JsonNode.Parse(CatalogJson())!.AsObject();
        var definitions = duplicate["definitions"]!.AsArray();
        definitions.Add(definitions[0]!.DeepClone());
        Assert.Throws<JsonException>(() =>
            RacingDnaCatalog.Parse(duplicate.ToJsonString(), CharacterRules()));

        var unbalanced = JsonNode.Parse(CatalogJson())!.AsObject();
        unbalanced["definitions"]![0]!["tradeoffEffects"]![0]!["balanceValue"] = -0.1;
        Assert.Throws<JsonException>(() =>
            RacingDnaCatalog.Parse(unbalanced.ToJsonString(), CharacterRules()));
    }

    [Fact]
    public void ValidateCreation_RejectsUnknownDefinitionAndOutOfBudgetAdvancedBuild()
    {
        var catalog = Catalog();
        var definition = catalog.Get("dna_prodigy", 1);
        var valid = Profile(definition, null);

        Assert.Throws<InvalidOperationException>(() =>
            catalog.ValidateCreation(valid with { RacingDnaVersion = 99 }));

        var excessive = valid.CreationBaseline! with
        {
            Stats = valid.CreationBaseline.Stats.ToDictionary(
                pair => pair.Key,
                _ => 0.85,
                StringComparer.Ordinal),
            Meta = valid.CreationBaseline.Meta.ToDictionary(
                pair => pair.Key,
                _ => 1.0,
                StringComparer.Ordinal),
        };
        Assert.Throws<InvalidOperationException>(() =>
            catalog.ValidateCreation(valid with { CreationBaseline = excessive }));
    }

    private static CharacterProfile Profile(RacingDnaDefinition definition, string? choice)
    {
        var combined = definition.StartingStats
            .Concat(definition.StartingMeta)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        return new CharacterProfile
        {
            Stats = combined,
            PerkIds = definition.StartingTraitIds.ToArray(),
            CreationPerkIds = definition.StartingTraitIds.ToArray(),
            ProgressionVersion = CharacterLevelProgression.Level300Version,
            RacingDnaId = definition.Id,
            RacingDnaVersion = definition.Version,
            RacingDnaChoice = choice,
            CreationBaseline = new CharacterCreationBaseline
            {
                Stats = new Dictionary<string, double>(definition.StartingStats, StringComparer.Ordinal),
                Meta = new Dictionary<string, double>(definition.StartingMeta, StringComparer.Ordinal),
                TraitIds = definition.StartingTraitIds.ToArray(),
            },
        };
    }
}
