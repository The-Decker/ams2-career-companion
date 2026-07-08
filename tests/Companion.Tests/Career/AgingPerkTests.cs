using Companion.Core.Career;
using Companion.Core.Character;

namespace Companion.Tests.Career;

/// <summary>
/// The veteran-aging perks (agingCurve peakShift / declineAccelMult) shift the player's age penalty in
/// the OFFER market — resolved onto <see cref="PlayerPerkModifiers"/> and consumed by
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
        // late_bloomer: agingCurve peakShift +3 — the player's peak (and its age penalty) shifts later.
        Assert.Equal(3.0, PerkResolver.Resolve(["late_bloomer"], rules).PeakShift, 6);
        // iron_constitution: agingCurve declineAccelMult −0.50 → the multiplier is 0.50 (a gentler tail).
        Assert.Equal(0.50, PerkResolver.Resolve(["iron_constitution"], rules).DeclineAccelMult, 6);
        // ...and it also carries peakShift +3 (a long, late plateau).
        Assert.Equal(3.0, PerkResolver.Resolve(["iron_constitution"], rules).PeakShift, 6);
        // ...and its statPoints softCap drawback lowers the in-career stat-raise ceiling by 0.10.
        Assert.Equal(-0.10, PerkResolver.Resolve(["iron_constitution"], rules).StatSoftCapDelta, 6);
    }
}
