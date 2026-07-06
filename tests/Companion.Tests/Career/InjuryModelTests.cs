using Companion.Core.Character;

namespace Companion.Tests.Career;

/// <summary>The opt-in season-end injury math (character depth 6): a fragile driver's hazard climbs;
/// only a character carrying an injury-stream perk is exposed to the roll.</summary>
public sealed class InjuryModelTests
{
    private static CharacterRules Rules() => CharacterRules.Parse(CareerTestData.ReadRules("perks.json"));

    [Fact]
    public void Hazard_RisesAsTheDriverGetsMoreFragile()
    {
        var neutral = PlayerPerkModifiers.Identity;
        double durable = InjuryModel.Hazard(0.75, neutral);
        double average = InjuryModel.Hazard(0.50, neutral);
        double fragile = InjuryModel.Hazard(0.20, neutral);

        Assert.True(durable < average);
        Assert.True(average < fragile);
        Assert.Equal(0.10, average, 6); // a 0.5-durability neutral driver sits at the base
        Assert.InRange(fragile, 0.0, 0.85);
    }

    [Fact]
    public void Hazard_AccountsForInjuryPerkModifiers()
    {
        // A durability-cutting perk (glass_cannon: durabilityDelta −0.20) makes the same driver
        // materially more injury-prone than the perk-free baseline.
        var fragilePerk = PlayerPerkModifiers.Identity with { InjuryDurabilityDelta = -0.20, InjuryBaseAdd = 0.06 };
        Assert.True(InjuryModel.Hazard(0.40, fragilePerk) > InjuryModel.Hazard(0.40, PlayerPerkModifiers.Identity));
    }

    [Fact]
    public void HasInjuryPerk_DetectsInjuryStreamPerks()
    {
        var rules = Rules();
        CharacterProfile With(params string[] perks) => new()
        {
            Stats = new Dictionary<string, double>(StringComparer.Ordinal),
            PerkIds = perks,
        };

        Assert.True(InjuryModel.HasInjuryPerk(With("glass_cannon"), rules));   // stream: injury
        Assert.False(InjuryModel.HasInjuryPerk(With("engineers_favorite"), rules)); // stream: none
        Assert.False(InjuryModel.HasInjuryPerk(With(), rules));
    }
}
