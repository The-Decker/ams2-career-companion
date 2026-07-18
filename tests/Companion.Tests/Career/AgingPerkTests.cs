using Companion.Core.Career;
using Companion.Core.Character;

namespace Companion.Tests.Career;

/// <summary>
/// The veteran-aging perks (agingCurve peakShift / declineAccelMult) shift the player's age penalty in
/// the OFFER market, resolved onto <see cref="PlayerPerkModifiers"/> and consumed by
/// <see cref="SeasonEndPipeline.PlayerAgeRisk"/>. The player's on-track ratings deliberately do not age
/// (the sim's self-balancer would make a rating decline an easier rep bar, not a penalty).
/// </summary>
public sealed class AgingPerkTests
{
    private static CharacterRules Rules() => CharacterRules.Parse(CareerTestData.ReadRules("perks.json"));

    [Fact]
    public void PlayerAgeRisk_VeteranPerks_DelayAndSoftenTheAgePenalty()
    {
        // Baseline: at 33 with the era peak ending at 30, ageRisk = max(0, 34 − 30) = 4.
        Assert.Equal(4.0, SeasonEndPipeline.PlayerAgeRisk(33, 30, null), 6);
        Assert.Equal(4.0, SeasonEndPipeline.PlayerAgeRisk(33, 30, PlayerPerkModifiers.Identity), 6);

        // peakShift +3 (late_bloomer / wonderkid): the penalty starts 3 years later → max(0, 34 − 33) = 1.
        Assert.Equal(1.0, SeasonEndPipeline.PlayerAgeRisk(33, 30, new PlayerPerkModifiers { PeakShift = 3.0 }), 6);

        // declineAccelMult 0.6 (iron_constitution, −0.40): the penalty grows 40% more gently → 4 × 0.6 = 2.4.
        Assert.Equal(2.4, SeasonEndPipeline.PlayerAgeRisk(33, 30, new PlayerPerkModifiers { DeclineAccelMult = 0.6 }), 6);

        // Below the peak: no age penalty regardless.
        Assert.Equal(0.0, SeasonEndPipeline.PlayerAgeRisk(28, 30, null), 6);
    }

    [Fact]
    public void VeteranPerks_ResolveTheAgingOverride()
    {
        var rules = Rules();
        // late_bloomer: agingCurve peakShift +3, the player's peak (and its age penalty) shifts later.
        Assert.Equal(3.0, PerkResolver.Resolve(["late_bloomer"], rules).PeakShift, 6);
        // iron_constitution: agingCurve declineAccelMult −0.50 → the multiplier is 0.50 (a gentler tail).
        Assert.Equal(0.50, PerkResolver.Resolve(["iron_constitution"], rules).DeclineAccelMult, 6);
        // ...and it also carries peakShift +3 (a long, late plateau).
        Assert.Equal(3.0, PerkResolver.Resolve(["iron_constitution"], rules).PeakShift, 6);
        // ...and its statPoints softCap drawback lowers the in-career stat-raise ceiling by 0.10.
        Assert.Equal(-0.10, PerkResolver.Resolve(["iron_constitution"], rules).StatSoftCapDelta, 6);
    }

    [Fact]
    public void DurabilityAgeShift_CourtsToughDriversLonger_AndFragileOnesShorter()
    {
        // §2.2: round(6·(durability−0.5)). A neutral 0.5 (and every character-free career) shifts
        // nothing; a tough 1.0 is courted as if 3 years younger, a fragile 0.0 as if 3 older.
        Assert.Equal(0, SeasonEndPipeline.DurabilityAgeShift(0.5));
        Assert.Equal(3, SeasonEndPipeline.DurabilityAgeShift(1.0));
        Assert.Equal(-3, SeasonEndPipeline.DurabilityAgeShift(0.0));
        Assert.Equal(2, SeasonEndPipeline.DurabilityAgeShift(0.8));   // round(1.8)
        Assert.Equal(-2, SeasonEndPipeline.DurabilityAgeShift(0.25)); // round(-1.5) away from zero

        // The shift bites the offer-market age risk: a tough veteran faces LESS age penalty than a
        // fragile one at the same real age (a lower risk means a higher offer score).
        int peakEnd = 30;
        double toughRisk = SeasonEndPipeline.PlayerAgeRisk(36 - SeasonEndPipeline.DurabilityAgeShift(0.9), peakEnd, null);
        double fragileRisk = SeasonEndPipeline.PlayerAgeRisk(36 - SeasonEndPipeline.DurabilityAgeShift(0.1), peakEnd, null);
        Assert.True(toughRisk < fragileRisk);
    }
}
