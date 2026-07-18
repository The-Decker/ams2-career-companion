using Companion.Core.Career;
using Companion.Core.Character;

namespace Companion.Tests.Career;

/// <summary>The identity-default <see cref="PlayerPerkModifiers"/> parameter threaded into the
/// sacred pure sim functions. For every function, a null modifier reproduces the EXACT shipped
/// result (the byte-identical guarantee for character-free careers), and a targeted non-null
/// modifier moves the result in the documented direction.</summary>
public sealed class CharacterModifierThreadingTests
{
    // ---------- OpiMath ----------

    [Fact]
    public void OpiUpdate_NullModifier_IsExactlyTheShippedFormula()
    {
        double shipped = 0.8 * 1.5 + 0.2 * (10 - 4);
        Assert.Equal(shipped, OpiMath.Update(1.5, 10, 4));
        Assert.Equal(OpiMath.Update(1.5, 10, 4), OpiMath.Update(1.5, 10, 4, null));
    }

    [Fact]
    public void OpiUpdate_HigherRetention_HoldsMorePriorOpi()
    {
        var sticky = PlayerPerkModifiers.Identity with { OpiRetention = 0.9 };
        Assert.True(OpiMath.Update(2.0, 5, 5, sticky) > OpiMath.Update(2.0, 5, 5));
    }

    [Fact]
    public void EffectiveFinish_DriverError_NullIsGridSize_BlameScaleAddsBlame()
    {
        Assert.Equal(20, OpiMath.EffectiveFinish(5, null, DnfCause.DriverError, 20));
        Assert.Equal(20, OpiMath.EffectiveFinish(5, null, DnfCause.DriverError, 20, PlayerPerkModifiers.Identity));

        var fragile = PlayerPerkModifiers.Identity with { ErrorBlameScale = 1.10 };
        // expected 5, grid 20 → 5 + 1.1*(20-5) = 21.5 (10% more blame than the grid size).
        Assert.Equal(21.5, OpiMath.EffectiveFinish(5, null, DnfCause.DriverError, 20, fragile), 6);

        // A classified finish and a mechanical DNF are never touched by the modifier.
        Assert.Equal(3, OpiMath.EffectiveFinish(5, 3, null, 20, fragile));
        Assert.Equal(5, OpiMath.EffectiveFinish(5, null, DnfCause.Mechanical, 20, fragile));
    }

    // ---------- PaceAnchorMath ----------

    [Fact]
    public void PaceAnchorUpdate_NullModifier_UsesTheShippedAlpha()
    {
        double shipped = (1.0 - 0.3) * 90 + 0.3 * 95;
        Assert.Equal(shipped, PaceAnchorMath.Update(90, 95));
        Assert.Equal(shipped, PaceAnchorMath.Update(90, 95, null));
        // An uncalibrated anchor still seeds directly, modifier or not.
        var quick = PlayerPerkModifiers.Identity with { AnchorAlpha = 0.45 };
        Assert.Equal(95, PaceAnchorMath.Update(0, 95, quick));
    }

    [Fact]
    public void PaceAnchorUpdate_HigherAlpha_ConvergesFaster()
    {
        var quick = PlayerPerkModifiers.Identity with { AnchorAlpha = 0.45 };
        // Moving toward a higher sample: a bigger alpha lands closer to it.
        Assert.True(PaceAnchorMath.Update(90, 100, quick) > PaceAnchorMath.Update(90, 100));
    }

    // ---------- ReputationMath ----------

    [Fact]
    public void ReputationRoundDelta_NullModifier_IsExactlyTheShippedFormula()
    {
        double shipped = ReputationMath.RoundDelta(10, 3, 3, beatTeammate: true, teamTier: 3);
        Assert.Equal(shipped, ReputationMath.RoundDelta(10, 3, 3, true, 3, null));
    }

    [Fact]
    public void ReputationRoundDelta_HigherMarketability_ScalesTheGain()
    {
        var famous = PlayerPerkModifiers.Identity with { Marketability = 1.0 }; // 1.0 + 0.5*(1.0-0.5) = 1.25×
        double baseline = ReputationMath.RoundDelta(10, 3, 3, false, 3);
        double scaled = ReputationMath.RoundDelta(10, 3, 3, false, 3, famous);
        Assert.Equal(baseline * 1.25, scaled, 6);
    }

    [Fact]
    public void ReputationSeasonDelta_NullModifier_IsExactlyTheShippedFormula()
    {
        double shipped = ReputationMath.SeasonDelta(1, 2);
        Assert.Equal(shipped, ReputationMath.SeasonDelta(1, 2, null));
    }

    [Fact]
    public void UnderdogMultiplier_NullModifier_KeepsTheShippedBand()
    {
        for (int tier = 1; tier <= 5; tier++)
            Assert.Equal(1.0 + 0.25 * (5 - tier), ReputationMath.UnderdogMultiplier(tier));
    }

    // ---------- OfferScore / SalaryOffer ----------

    private static TeamArchetypeCatalog Catalog() =>
        CareerTestData.LoadArchetypes();

    [Fact]
    public void OfferScore_NullModifier_IsExactlyTheShippedFormula()
    {
        var cat = Catalog();
        var arch = cat.ForTeam(3, null);
        double shipped = TeamArchetypeCatalog.OfferScore(arch, 50, 1.2, 3, 5, 2);
        Assert.Equal(shipped, TeamArchetypeCatalog.OfferScore(arch, 50, 1.2, 3, 5, 2, null));
    }

    [Fact]
    public void OfferScore_LowerSalaryAsk_RaisesTheScore()
    {
        var cat = Catalog();
        var arch = cat.ForTeam(3, null);
        var cheap = PlayerPerkModifiers.Identity with { SalaryAskMult = 0.5 }; // asks for less → scores higher
        Assert.True(
            TeamArchetypeCatalog.OfferScore(arch, 50, 1.2, 3, 10, 2, cheap) >
            TeamArchetypeCatalog.OfferScore(arch, 50, 1.2, 3, 10, 2));
    }

    [Fact]
    public void OfferScore_SponsorMoney_RaisesTheScoreForPayDriverTeams()
    {
        // sponsor_magnet's payBudgetBu was a dead lever; OfferScore now reads it, scaled by the
        // team's own pay-driver appetite (PayDriverWeight), exactly like the AI seat market.
        var payTeam = new TeamArchetype
        {
            Weights = new OfferWeights { Rep = 1, Opi = 1, Experience = 1, Salary = 1, AgeRisk = 1 },
            PayDriverWeight = 20,
        };
        var sponsored = PlayerPerkModifiers.Identity with { PayBudgetBu = 2.0 };
        double withMoney = TeamArchetypeCatalog.OfferScore(payTeam, 50, 1.2, 3, 5, 2, sponsored);
        double without = TeamArchetypeCatalog.OfferScore(payTeam, 50, 1.2, 3, 5, 2);
        Assert.True(withMoney > without);
        Assert.Equal(without + 20 * 2.0 / 100.0, withMoney, 6); // PayDriverWeight · payBudget / 100

        // A team that does not value pay drivers (weight 0) is unaffected, no free score.
        var noPay = payTeam with { PayDriverWeight = 0 };
        Assert.Equal(
            TeamArchetypeCatalog.OfferScore(noPay, 50, 1.2, 3, 5, 2),
            TeamArchetypeCatalog.OfferScore(noPay, 50, 1.2, 3, 5, 2, sponsored), 6);
    }

    [Fact]
    public void SalaryOffer_NullModifier_IsExactlyTheShippedBand()
    {
        var cat = Catalog();
        Assert.Equal(cat.SalaryOffer(3, 60), cat.SalaryOffer(3, 60, null));
        var rich = PlayerPerkModifiers.Identity with { SalaryOfferMult = 1.2 };
        Assert.True(cat.SalaryOffer(3, 60, rich) >= cat.SalaryOffer(3, 60));
    }
}
