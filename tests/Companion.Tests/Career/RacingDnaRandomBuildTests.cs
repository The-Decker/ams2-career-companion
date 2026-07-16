using System.Text.Json;
using Companion.Core.Character;
using Companion.Core.Json;

namespace Companion.Tests.Career;

public sealed class RacingDnaRandomBuildTests
{
    private static RacingDnaCatalog Catalog()
    {
        var rules = CharacterRules.Parse(CareerTestData.ReadRules("perks.json"));
        return RacingDnaCatalog.Parse(CareerTestData.ReadRules("racing-dna-v2.json"), rules);
    }

    private static RacingDnaRandomContext Context() => new()
    {
        EligibleRivalDriverIds = ["driver.prost", "driver.senna", "driver.mansell"],
        NationalityAffinities = ["BRA", "FRA", "GBR"],
    };

    [Fact]
    public void SameSeedOrdinalAndContext_ProducesTheSameCompleteProfileAndJson()
    {
        var catalog = Catalog();

        var first = RacingDnaRandomBuild.Create(catalog, 0xC0FFEEUL, 17, Context(), "  Random Driver  ", 23);
        var second = RacingDnaRandomBuild.Create(catalog, 0xC0FFEEUL, 17, Context(), "  Random Driver  ", 23);

        Assert.Equal(first, second);
        Assert.Equal(
            JsonSerializer.Serialize(first, CoreJson.Options),
            JsonSerializer.Serialize(second, CoreJson.Options));
        Assert.Equal("Random Driver", first.Name);
        Assert.Equal(CharacterLevelProgression.Level300Version, first.ProgressionVersion);
        Assert.Equal(CharacterProfile.CurrentMasteryEffectsVersion, first.MasteryEffectsVersion);
        Assert.Equal(CharacterProfile.CurrentExpectationModelVersion, first.ExpectationModelVersion);
        Assert.NotNull(first.CreationBaseline);
        Assert.Equal(0, first.CpUnspent);
        Assert.Equal(0, first.SkillPointsSpent);
        catalog.ValidateCreation(first);
    }

    [Fact]
    public void DeterministicRerollSweep_ReachesAllThirtyAndAlwaysBuildsValidChoices()
    {
        var catalog = Catalog();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var context = Context();

        for (int ordinal = 0; ordinal < 2_000; ordinal++)
        {
            var profile = RacingDnaRandomBuild.Create(
                catalog, 20260713UL, ordinal, context, "Sweep Driver", 23);
            seen.Add(profile.RacingDnaId!);
            var definition = catalog.ValidateCreation(profile);

            switch (definition.Choice?.Kind)
            {
                case null:
                    Assert.Null(profile.RacingDnaChoice);
                    break;
                case RacingDnaChoiceKind.RivalDriverId:
                    Assert.Contains(profile.RacingDnaChoice!, context.EligibleRivalDriverIds);
                    break;
                case RacingDnaChoiceKind.NationalityAffinity:
                    Assert.Contains(profile.RacingDnaChoice!, context.NationalityAffinities);
                    break;
                default:
                    Assert.Contains(profile.RacingDnaChoice!, definition.Choice.Options);
                    break;
            }

            if (profile.PerkIds.Contains("one_trick", StringComparer.Ordinal))
                Assert.Equal(PerkResolver.DefaultChosenFlavor, profile.ChosenFlavor);
        }

        Assert.Equal(
            catalog.Definitions.Select(definition => definition.Id).ToHashSet(StringComparer.Ordinal),
            seen);
    }

    [Fact]
    public void DynamicContextOrder_DoesNotChangeTheRoll()
    {
        var catalog = Catalog();
        var sorted = Context();
        var reversed = new RacingDnaRandomContext
        {
            EligibleRivalDriverIds = sorted.EligibleRivalDriverIds.Reverse().ToArray(),
            NationalityAffinities = sorted.NationalityAffinities.Reverse().ToArray(),
        };

        for (int ordinal = 0; ordinal < 100; ordinal++)
        {
            Assert.Equal(
                RacingDnaRandomBuild.Create(catalog, 42, ordinal, sorted, "Driver", 23),
                RacingDnaRandomBuild.Create(catalog, 42, ordinal, reversed, "Driver", 23));
        }
    }

    [Fact]
    public void MissingNationalityContext_FiltersOnlyTheUnavailableIdentity()
    {
        var catalog = Catalog();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var context = new RacingDnaRandomContext
        {
            EligibleRivalDriverIds = ["driver.senna"],
            NationalityAffinities = [],
        };

        for (int ordinal = 0; ordinal < 2_000; ordinal++)
        {
            var profile = RacingDnaRandomBuild.Create(catalog, 1, ordinal, context, "Driver", 23);
            catalog.ValidateCreation(profile);
            seen.Add(profile.RacingDnaId!);
        }

        var expected = catalog.Definitions
            .Where(definition => definition.Choice?.Kind != RacingDnaChoiceKind.NationalityAffinity)
            .Select(definition => definition.Id)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(expected, seen);
    }

    [Fact]
    public void MalformedDynamicContext_FailsBeforeAnyPartialProfileExists()
    {
        var catalog = Catalog();
        Assert.Throws<ArgumentException>(() => RacingDnaRandomBuild.Create(
            catalog,
            seed: 1,
            rerollOrdinal: 0,
            new RacingDnaRandomContext
            {
                EligibleRivalDriverIds = ["driver.senna", "driver.senna"],
                NationalityAffinities = ["BRA"],
            },
            "Driver",
            23));
        Assert.Throws<ArgumentOutOfRangeException>(() => RacingDnaRandomBuild.Create(
            catalog, 1, -1, Context(), "Driver", 23));
    }

    [Theory]
    [InlineData(3, RacingDnaRandomBuild.MinCreationAge)]
    [InlineData(99, RacingDnaRandomBuild.MaxCreationAge)]
    public void CreationAge_IsClampedToTheSupportedBand(int requested, int expected)
    {
        var profile = RacingDnaRandomBuild.Create(Catalog(), 7, 0, Context(), "Driver", requested);
        Assert.Equal(expected, profile.Age);
    }
}
