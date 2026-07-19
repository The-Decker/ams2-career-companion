using Companion.Core.Career;
using Companion.Core.Character;

namespace Companion.Tests.Career;

/// <summary>The pure perk-effects → <see cref="PlayerPerkModifiers"/> resolver. Empty perks resolve
/// to the identity modifier (the byte-identical guarantee); each lever maps to its documented field;
/// round-conditional effects are carried, not pre-applied.</summary>
public sealed class PerkResolverTests
{
    private static CharacterRules Rules() => CharacterRules.Parse(CareerTestData.ReadRules("perks.json"));

    [Fact]
    public void Resolve_NoPerks_IsTheIdentityModifier()
    {
        Assert.Same(PlayerPerkModifiers.Identity, PerkResolver.Resolve([], Rules()));
    }

    [Fact]
    public void Resolve_StatDeltaPerk_MovesTheNamedRatings()
    {
        // sunday_driver: raceSkill +0.06, qualifyingSkill -0.06 (rating mass redistributed).
        var mods = PerkResolver.Resolve(["sunday_driver"], Rules());

        Assert.Equal(0.06, mods.TalentDelta("raceSkill"), 6);
        Assert.Equal(-0.06, mods.TalentDelta("qualifyingSkill"), 6);
        Assert.Equal(0.0, mods.TalentDelta("aggression")); // untouched field
    }

    [Fact]
    public void Resolve_CarScalarAndMarketabilityAndOffer_MapToTheirFields()
    {
        // engineers_favorite: power +0.010, drag -0.008, startReactions -0.05, marketability -0.10.
        var mods = PerkResolver.Resolve(["engineers_favorite"], Rules());

        Assert.Equal(0.010, mods.PowerScalarDelta, 6);
        Assert.Equal(-0.008, mods.DragScalarDelta, 6);
        Assert.Equal(0.0, mods.WeightScalarDelta);
        Assert.Equal(-0.05, mods.TalentDelta("startReactions"), 6);
        Assert.Equal(0.40, mods.Marketability, 6); // 0.5 base − 0.10
    }

    [Fact]
    public void Resolve_PaceAnchorAndOfferWeightAndXp_ApplyOnTheirBases()
    {
        // test_driver: paceAnchorAlpha +0.15, offerWeight experience -0.15, aggression -0.03.
        var mods = PerkResolver.Resolve(["test_driver"], Rules());

        Assert.Equal(0.45, mods.AnchorAlpha, 6);        // 0.3 base + 0.15
        Assert.Equal(0.85, mods.OfferExperienceMult, 6); // 1.0 base − 0.15
        Assert.Equal(-0.03, mods.TalentDelta("aggression"), 6);

        // qualifying_specialist: xpRate finishVsExpected -0.10 → ×0.90.
        var q = PerkResolver.Resolve(["qualifying_specialist"], Rules());
        Assert.Equal(0.90, q.XpMult("finishVsExpected"), 6);
        Assert.Equal(1.0, q.XpMult("win")); // untouched cause defaults to ×1.0
    }

    [Fact]
    public void Resolve_ErrorBlameAndInjury_MapAndCarryTheConditionalEffect()
    {
        // glass_cannon: power +0.012, avoidanceOfMistakes -0.10, opiErrorBlame scale +0.10,
        // injuryHazard durabilityDelta -0.20, injuryHazard perErrorAdd +0.15 @driverErrorDnf.
        var mods = PerkResolver.Resolve(["glass_cannon"], Rules());

        Assert.Equal(0.012, mods.PowerScalarDelta, 6);
        Assert.Equal(-0.10, mods.TalentDelta("avoidanceOfMistakes"), 6);
        Assert.Equal(1.10, mods.ErrorBlameScale, 6);      // 1.0 base + 0.10
        Assert.Equal(-0.20, mods.InjuryDurabilityDelta, 6);

        // The conditional per-error injury effect is carried, NOT folded into a base field.
        var cond = Assert.Single(mods.Conditional);
        Assert.Equal("injuryHazard", cond.Lever);
        Assert.Equal("perErrorAdd", cond.Target);
        Assert.Equal("driverErrorDnf", cond.Condition);
        Assert.Equal(0.15, cond.Magnitude, 6);
    }

    [Fact]
    public void Resolve_WithActiveConditions_FiresOnlyTheMatchingConditionalEffects()
    {
        var rules = Rules();

        // underdog_hero: lowTierBonus +0.25 @tierLte2, topTierMult −0.15 @tierGte4. With no round
        // context BOTH are carried (dormant) and the base fields stay at identity.
        var dormant = PerkResolver.Resolve(["underdog_hero"], rules);
        Assert.Equal(0.0, dormant.UnderdogLowTierBonus);
        Assert.Equal(1.0, dormant.TopTierRepMult);
        Assert.Equal(2, dormant.Conditional.Count);

        // In a tier≤2 seat the low-tier rep bonus FIRES; the tier≥4 drawback stays carried.
        var lowTier = PerkResolver.Resolve(["underdog_hero"], rules, new HashSet<string> { "tierLte2" });
        Assert.Equal(0.25, lowTier.UnderdogLowTierBonus, 6);
        Assert.Equal(1.0, lowTier.TopTierRepMult);
        Assert.Single(lowTier.Conditional); // only the tierGte4 effect remains carried

        // In a tier≥4 seat the drawback FIRES instead.
        var topTier = PerkResolver.Resolve(["underdog_hero"], rules, new HashSet<string> { "tierGte4" });
        Assert.Equal(0.85, topTier.TopTierRepMult, 6); // 1.0 − 0.15
        Assert.Equal(0.0, topTier.UnderdogLowTierBonus);

        // stamina_freak: drag −0.010 @longRace fires on a long race; its always-on stamina delta is
        // unaffected by the round context.
        var longRace = PerkResolver.Resolve(["stamina_freak"], rules, new HashSet<string> { "longRace" });
        Assert.Equal(-0.010, longRace.DragScalarDelta, 6);
        Assert.Equal(0.12, longRace.TalentDelta("stamina"), 6);
        var noContext = PerkResolver.Resolve(["stamina_freak"], rules);
        Assert.Equal(0.0, noContext.DragScalarDelta); // conditional drag dormant without a round
        Assert.Equal(0.12, noContext.TalentDelta("stamina"), 6);
    }

    [Fact]
    public void Resolve_AgeWindowConditions_FireTheFront_OrBackLoadedHalf()
    {
        var rules = Rules();

        // prodigy: raceSkill/qualifyingSkill +0.05 @ageLtPeak, raceSkill −0.02 @ageGtePeak. With no
        // round context both halves are carried (dormant), the talent deltas stay at identity.
        var dormant = PerkResolver.Resolve(["prodigy"], rules);
        Assert.Equal(0.0, dormant.TalentDelta("raceSkill"));
        Assert.Equal(3, dormant.Conditional.Count);

        // Pre-peak (young): the fast-start bonus FIRES, the slump stays carried.
        var young = PerkResolver.Resolve(["prodigy"], rules, new HashSet<string> { "ageLtPeak" });
        Assert.Equal(0.05, young.TalentDelta("raceSkill"), 6);
        Assert.Equal(0.05, young.TalentDelta("qualifyingSkill"), 6);
        Assert.Single(young.Conditional); // only the ageGtePeak slump remains carried

        // Past peak: the slump FIRES instead (−0.02 raceSkill), the youth bonus does not.
        var veteran = PerkResolver.Resolve(["prodigy"], rules, new HashSet<string> { "ageGtePeak" });
        Assert.Equal(-0.02, veteran.TalentDelta("raceSkill"), 6);
        Assert.Equal(0.0, veteran.TalentDelta("qualifyingSkill"));

        // wonderkid: xpRate ageWindow +0.40 @ageLtPeak / −0.25 @ageGtePeak, a blanket XP multiplier.
        var wonderYoung = PerkResolver.Resolve(["wonderkid"], rules, new HashSet<string> { "ageLtPeak" });
        Assert.Equal(1.40, wonderYoung.XpMult("ageWindow"), 6);
        var wonderOld = PerkResolver.Resolve(["wonderkid"], rules, new HashSet<string> { "ageGtePeak" });
        Assert.Equal(0.75, wonderOld.XpMult("ageWindow"), 6);
    }

    [Fact]
    public void Resolve_MultiplePerks_AccumulateAdditively()
    {
        // sunday_driver (raceSkill +0.06 / quali -0.06) + qualifying_specialist (quali +0.08 /
        // raceSkill -0.05): the shared ratings sum.
        var mods = PerkResolver.Resolve(["sunday_driver", "qualifying_specialist"], Rules());

        Assert.Equal(0.01, mods.TalentDelta("raceSkill"), 6);       // 0.06 − 0.05
        Assert.Equal(0.02, mods.TalentDelta("qualifyingSkill"), 6); // −0.06 + 0.08
    }

    [Fact]
    public void Resolve_OneTrick_BindsTheChosenFlavorRatingAndLocksToIt()
    {
        var rules = Rules();

        // one_trick: statDelta "chosenFlavor" +0.30 (benefit) + statPoints "lockToOne" (drawback).
        // The chosenFlavor placeholder resolves to the picked rating, and lockToOne records it as the
        // ONE rating in-career development may raise, both were fully inert before the fix.
        var picked = PerkResolver.Resolve(["one_trick"], rules, activeConditions: null, chosenFlavor: "tyreManagement");
        Assert.Equal(0.30, picked.TalentDelta("tyreManagement"), 6);
        Assert.Equal(0.0, picked.TalentDelta("wetSkill")); // only the picked flavor moves
        Assert.Equal("tyreManagement", picked.LockedFlavorRating);

        // No pick (a legacy profile) falls back to the fixed deterministic default, so live and
        // replay resolve the same delta byte-for-byte.
        var fallback = PerkResolver.Resolve(["one_trick"], rules);
        Assert.Equal(0.30, fallback.TalentDelta(PerkResolver.DefaultChosenFlavor), 6);
        Assert.Equal(PerkResolver.DefaultChosenFlavor, fallback.LockedFlavorRating);

        // The profile overload threads the profile's own ChosenFlavor.
        var profile = new CharacterProfile
        {
            Stats = new Dictionary<string, double>(),
            PerkIds = ["one_trick"],
            ChosenFlavor = "aggression",
        };
        var viaProfile = PerkResolver.Resolve(profile, rules);
        Assert.Equal(0.30, viaProfile.TalentDelta("aggression"), 6);
        Assert.Equal("aggression", viaProfile.LockedFlavorRating);

        // A character WITHOUT one_trick never locks development.
        Assert.Null(PerkResolver.Resolve(["sunday_driver"], rules).LockedFlavorRating);
    }

    [Fact]
    public void Resolve_SponsorMagnet_CarriesThePayBudgetTheOfferMarketReads()
    {
        // sponsor_magnet's headline benefit is income/payBudgetBu +2.0, dead before the fix (no
        // consumer). It must now surface on the modifier so OfferScore's pay term reads it.
        var mods = PerkResolver.Resolve(["sponsor_magnet"], Rules());
        Assert.Equal(2.0, mods.PayBudgetBu, 6);
    }

    [Fact]
    public void Resolve_EveryShippedArchetype_ProducesAResolvableModifier()
    {
        // No archetype names a perk the resolver can't map, resolving each preset never throws and
        // never leaves an identity where the perks clearly move a lever.
        var rules = Rules();
        foreach (var archetype in rules.Creation.Archetypes)
        {
            var mods = PerkResolver.Resolve(archetype.PerkIds, rules);
            Assert.NotNull(mods);
        }
    }
}
