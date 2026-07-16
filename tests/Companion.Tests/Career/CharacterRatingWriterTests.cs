using Companion.Core.Character;
using Companion.Core.Packs;

namespace Companion.Tests.Career;

/// <summary>The pure talent-stat + perk-delta writer onto the player seat's ratings. Talent stats
/// overwrite their mapped fields; perk deltas add on top; everything clamps to 0..1.</summary>
public sealed class CharacterRatingWriterTests
{
    private static CharacterRules Rules() => CharacterRules.Parse(CareerTestData.ReadRules("perks.json"));

    private static PackDriverRatings Base() => new()
    {
        RaceSkill = 0.70,
        QualifyingSkill = 0.70,
        Aggression = 0.50,
        Defending = 0.50,
        Consistency = 0.70,
        StartReactions = 0.80,
        WetSkill = 0.60,
        TyreManagement = 0.60,
        AvoidanceOfMistakes = 0.60,
    };

    private static CharacterProfile Profile(
        IReadOnlyList<string>? perkIds = null,
        double pace = 0.5, double oneLap = 0.5, double craft = 0.5,
        double racecraft = 0.5, double adaptability = 0.5) => new()
    {
        Stats = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["pace"] = pace, ["oneLap"] = oneLap, ["craft"] = craft,
            ["racecraft"] = racecraft, ["adaptability"] = adaptability,
            ["marketability"] = 0.5, ["durability"] = 0.5,
        },
        PerkIds = perkIds ?? [],
    };

    [Fact]
    public void Apply_TalentStats_OverwriteTheMappedRatings()
    {
        var rules = Rules();
        var profile = Profile(pace: 0.5, oneLap: 0.85);
        var mods = PerkResolver.Resolve(profile.PerkIds, rules);

        var r = CharacterRatingWriter.Apply(Base(), profile, rules, mods);

        // pace 0.5 → raceSkill 0.35 + 0.55*0.5 = 0.625; oneLap 0.85 → 0.35 + 0.55*0.85 = 0.8175.
        Assert.Equal(0.625, r.RaceSkill, 6);
        Assert.Equal(0.8175, r.QualifyingSkill, 6);
        // A two-field talent stat writes both mapped ratings from the same stat value.
        // adaptability 0.5 → wetSkill & tyreManagement = 0.625.
        Assert.Equal(0.625, r.WetSkill!.Value, 6);
        Assert.Equal(0.625, r.TyreManagement!.Value, 6);
    }

    [Fact]
    public void Apply_PerkDeltas_AddOntoTheWrittenTalentValue()
    {
        var rules = Rules();
        // sunday_driver: raceSkill +0.06 / qualifyingSkill -0.06, on top of the pace/oneLap writes.
        var profile = Profile(["sunday_driver"], pace: 0.5, oneLap: 0.5);
        var mods = PerkResolver.Resolve(profile.PerkIds, rules);

        var r = CharacterRatingWriter.Apply(Base(), profile, rules, mods);

        Assert.Equal(0.685, r.RaceSkill, 6);       // 0.625 + 0.06
        Assert.Equal(0.565, r.QualifyingSkill, 6); // 0.625 − 0.06
    }

    [Fact]
    public void Apply_PerkDeltaOnAnUnmappedField_AddsOntoTheBaseRating()
    {
        var rules = Rules();
        // engineers_favorite touches startReactions (-0.05), which no talent stat maps.
        var profile = Profile(["engineers_favorite"]);
        var mods = PerkResolver.Resolve(profile.PerkIds, rules);

        var r = CharacterRatingWriter.Apply(Base(), profile, rules, mods);

        Assert.Equal(0.75, r.StartReactions!.Value, 6); // base 0.80 − 0.05
    }

    [Fact]
    public void Apply_ClampsWrittenRatingsIntoTheValidRange()
    {
        var rules = Rules();
        // hard_charger: aggression +0.10 on top of a maxed racecraft write would exceed 1.0.
        var profile = Profile(["hard_charger"], racecraft: 0.85);
        var mods = PerkResolver.Resolve(profile.PerkIds, rules);

        var r = CharacterRatingWriter.Apply(Base(), profile, rules, mods);

        // racecraft 0.85 → aggression written 0.8175, +0.10 = 0.9175 (in range, not clamped);
        // prove the clamp holds by construction: no rating leaves [0,1].
        foreach (var (_, value) in r.Enumerate())
            Assert.InRange(value, 0.0, 1.0);
        Assert.Equal(0.9175, r.Aggression!.Value, 6);
    }

    [Fact]
    public void Apply_RequiredRatingsAreAlwaysWritten_EvenIfTheStatMapReordered()
    {
        var rules = Rules();
        var r = CharacterRatingWriter.Apply(Base(), Profile(pace: 0.2, oneLap: 0.2), rules,
            PlayerPerkModifiers.Identity);

        // raceSkill/qualifyingSkill are non-null required fields and are always written by pace/oneLap.
        Assert.Equal(0.35 + 0.55 * 0.2, r.RaceSkill, 6);
        Assert.Equal(0.35 + 0.55 * 0.2, r.QualifyingSkill, 6);
    }

    [Fact]
    public void Apply_RejectsAnUnknownModifierRatingInsteadOfSilentlyDiscardingIt()
    {
        var modifiers = PlayerPerkModifiers.Identity with
        {
            TalentDeltas = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["notAWritableRating"] = 0.10,
            },
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            CharacterRatingWriter.Apply(Base(), Profile(), Rules(), modifiers));

        Assert.Contains("unknown writable rating", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromArchetype_BuildsAResolvableInBudgetCharacter()
    {
        var rules = Rules();
        var prodigy = rules.Creation.Archetypes.Single(a => a.Id == "prodigy");
        var profile = CharacterProfile.FromArchetype(prodigy);

        Assert.Equal(prodigy.PerkIds, profile.PerkIds);
        Assert.Equal(0.62, profile.Stat("pace"), 6);
        Assert.Equal(0.70, profile.Stat("marketability"), 6);

        // The whole pipeline runs end to end without throwing and stays in range.
        var mods = PerkResolver.Resolve(profile.PerkIds, rules);
        var r = CharacterRatingWriter.Apply(Base(), profile, rules, mods);
        foreach (var (_, value) in r.Enumerate())
            Assert.InRange(value, 0.0, 1.0);
    }
}
