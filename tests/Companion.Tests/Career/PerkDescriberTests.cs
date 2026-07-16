using Companion.Core.Character;

namespace Companion.Tests.Career;

/// <summary>The plain-language perk describer (character depth 5): turns machine-readable effects
/// into human phrases for the creator + dossier, so a build is legible, not opaque numbers.</summary>
public sealed class PerkDescriberTests
{
    private static CharacterRules Rules() => CharacterRules.Parse(CareerTestData.ReadRules("perks.json"));

    [Fact]
    public void Describe_MapsCommonLeversToFriendlyPhrases()
    {
        Assert.Equal("Stronger race pace",
            PerkDescriber.Describe(new PerkEffect { Kind = "benefit", Lever = "statDelta", Target = "raceSkill", Magnitude = 0.06 }));
        Assert.Equal("Weaker one-lap pace",
            PerkDescriber.Describe(new PerkEffect { Kind = "drawback", Lever = "statDelta", Target = "qualifyingSkill", Magnitude = -0.06 }));
        Assert.Equal("Faster car (real pace)",
            PerkDescriber.Describe(new PerkEffect { Kind = "benefit", Lever = "carScalar", Target = "power", Magnitude = 0.012 }));
        Assert.Equal("Faster car (real pace)",
            PerkDescriber.Describe(new PerkEffect { Kind = "benefit", Lever = "carScalar", Target = "weight", Magnitude = -0.01 }));
        Assert.Equal("Mistakes punished harder",
            PerkDescriber.Describe(new PerkEffect { Kind = "drawback", Lever = "opiErrorBlame", Target = "scale", Magnitude = 0.1 }));
        Assert.Equal("Higher injury risk",
            PerkDescriber.Describe(new PerkEffect { Kind = "drawback", Lever = "injuryHazard", Target = "durabilityDelta", Magnitude = -0.2 }));
    }

    [Fact]
    public void Describe_AppendsAConditionClause()
    {
        var effect = new PerkEffect
        {
            Kind = "benefit", Lever = "statDelta", Target = "wetSkill", Magnitude = 0.3, Condition = "wetRound",
        };

        Assert.Equal("Stronger wet-weather pace — in the wet", PerkDescriber.Describe(effect));

        var line = PerkDescriber.DescribeLine(effect);
        Assert.Equal("Stronger wet-weather pace — in the wet", line.Text);
        Assert.Equal("wetRound", line.Condition);
        Assert.True(line.IsConditional);
    }

    [Theory]
    [InlineData("statDelta", CharacterEffectClass.Expectation, "EXPECTATION")]
    [InlineData("carScalar", CharacterEffectClass.Car, "CAR")]
    [InlineData("opiRetention", CharacterEffectClass.Career, "CAREER")]
    public void DescribeLine_MapsAbsentClassificationByLegacyLever(
        string lever,
        CharacterEffectClass expected,
        string expectedLabel)
    {
        var line = PerkDescriber.DescribeLine(new PerkEffect
        {
            Kind = "benefit",
            Lever = lever,
            Target = lever == "carScalar" ? "power" : "gainSide",
            Magnitude = 0.1,
            Note = "Fallback",
        });

        Assert.Equal(expected, line.Classification);
        Assert.Equal(expectedLabel, line.ClassificationLabel);
        Assert.False(line.IsConditional);
    }

    [Fact]
    public void DescribeLine_AuthoredClassificationOverridesLegacyMapping()
    {
        var line = PerkDescriber.DescribeLine(new PerkEffect
        {
            Kind = "benefit",
            Lever = "statDelta",
            Target = "raceSkill",
            Magnitude = 0.1,
            Classification = CharacterEffectClass.Career,
        });

        Assert.Equal(CharacterEffectClass.Career, line.Classification);
        Assert.Equal("CAREER", line.ClassificationLabel);
        Assert.Equal("Stronger race pace", line.Text);
    }

    [Fact]
    public void EveryShippedPerk_HasAtLeastOneBenefitAndDrawbackPhrase()
    {
        // The balance audit already guarantees each perk carries a benefit AND a drawback effect;
        // the describer must turn each into a non-empty phrase so no perk reads as blank in the UI.
        var rules = Rules();
        foreach (var perk in rules.Perks)
        {
            var effects = PerkDescriber.Effects(perk);
            var benefits = PerkDescriber.Benefits(perk);
            var drawbacks = PerkDescriber.Drawbacks(perk);

            Assert.NotEmpty(benefits);
            Assert.NotEmpty(drawbacks);
            Assert.Equal(
                effects.Where(line => line.Kind == "benefit").Select(line => line.Text),
                benefits);
            Assert.Equal(
                effects.Where(line => line.Kind == "drawback").Select(line => line.Text),
                drawbacks);
        }
    }
}
