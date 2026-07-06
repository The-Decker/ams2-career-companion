using Companion.Core.Career;

namespace Companion.Core.Character;

/// <summary>
/// Turns a character's chosen perks into the identity-defaulting <see cref="PlayerPerkModifiers"/>
/// the sim reads — a PURE function of the journaled <c>{perkIds}</c>, computed once and threaded
/// into each call site, so the fold reproduces it exactly (docs/dev/character-system.md §6.1). Each
/// effect's <c>lever</c>/<c>target</c> names exactly one modifier field; unconditional effects are
/// summed into the field, round-conditional ones are carried through for the fold to apply. An empty
/// perk list resolves to <see cref="PlayerPerkModifiers.Identity"/>.
/// </summary>
public static class PerkResolver
{
    public static PlayerPerkModifiers Resolve(IReadOnlyList<string> perkIds, CharacterRules rules)
    {
        if (perkIds.Count == 0)
            return PlayerPerkModifiers.Identity;

        var talent = new Dictionary<string, double>(StringComparer.Ordinal);
        var xp = new Dictionary<string, double>(StringComparer.Ordinal);
        var conditional = new List<ConditionalPerkEffect>();

        double weight = 0, power = 0, drag = 0;
        double opiRetention = OpiMath.Retention, opiGain = 1.0, errorBlame = 1.0, blameFloor = 0.0;
        double repRound = 1.0, repSeason = 1.0, marketability = 0.5, underdogLow = 0.0, topTier = 1.0;
        double anchorAlpha = PaceAnchorMath.Alpha;
        double peakShift = 0.0, riseMult = 1.0, declineAccel = 1.0;
        double offerExp = 1.0, salaryAsk = 1.0, ageRisk = 1.0, payBu = 0.0, salaryOffer = 1.0;
        int repFloorRelax = 0, statPointsPerLevel = 0;
        double injuryDurability = 0.0, injuryBase = 0.0;

        foreach (string perkId in perkIds)
        {
            var perk = rules.PerkById(perkId);
            foreach (var effect in perk.Effects)
            {
                if (effect.Condition is not null)
                {
                    conditional.Add(new ConditionalPerkEffect(
                        effect.Lever, effect.Target, effect.Magnitude, effect.Condition));
                    continue;
                }

                double m = effect.Magnitude;
                switch (effect.Lever)
                {
                    case "statDelta" when effect.Target is { } rating:
                        talent[rating] = talent.GetValueOrDefault(rating) + m;
                        break;
                    case "carScalar":
                        switch (effect.Target)
                        {
                            case "weight": weight += m; break;
                            case "power": power += m; break;
                            case "drag": drag += m; break;
                        }
                        break;
                    case "opiRetention":
                        if (effect.Target == "gainSide") opiGain += m; else opiRetention += m;
                        break;
                    case "opiErrorBlame":
                        if (effect.Target == "floorBlend") blameFloor += m; else errorBlame += m;
                        break;
                    case "reputationGainRate":
                        if (effect.Target is "round" or "both") repRound += m;
                        if (effect.Target is "season" or "both") repSeason += m;
                        break;
                    case "underdogMultiplier":
                        if (effect.Target == "topTierMult") topTier += m; else underdogLow += m;
                        break;
                    case "marketability":
                        marketability += m;
                        break;
                    case "paceAnchorAlpha":
                        anchorAlpha += m;
                        break;
                    case "agingCurve":
                        switch (effect.Target)
                        {
                            case "peakShift": peakShift += m; break;
                            case "riseMult": riseMult += m; break;
                            case "declineAccelMult": declineAccel += m; break;
                        }
                        break;
                    case "offerWeight":
                        switch (effect.Target)
                        {
                            case "experience": offerExp += m; break;
                            case "salaryAsk": salaryAsk += m; break;
                            case "ageRisk": ageRisk += m; break;
                            case "repFloorRelax": repFloorRelax += (int)Math.Round(m); break;
                        }
                        break;
                    case "income":
                        if (effect.Target == "salaryOfferMult") salaryOffer += m; else payBu += m;
                        break;
                    case "injuryHazard":
                        if (effect.Target == "durabilityDelta") injuryDurability += m;
                        else injuryBase += m; // baseAdd (unconditional perErrorAdd folds in here too)
                        break;
                    case "xpRate":
                        string cause = effect.Target ?? "all";
                        xp[cause] = xp.GetValueOrDefault(cause, 1.0) + m;
                        break;
                    case "statPoints":
                        if (effect.Target == "perLevel") statPointsPerLevel += (int)Math.Round(m);
                        break;
                }
            }
        }

        return new PlayerPerkModifiers
        {
            TalentDeltas = talent,
            WeightScalarDelta = weight,
            PowerScalarDelta = power,
            DragScalarDelta = drag,
            OpiRetention = opiRetention,
            OpiGainScale = opiGain,
            ErrorBlameScale = errorBlame,
            BlameFloorBlend = blameFloor,
            RepRoundMult = repRound,
            RepSeasonMult = repSeason,
            Marketability = marketability,
            UnderdogLowTierBonus = underdogLow,
            TopTierRepMult = topTier,
            AnchorAlpha = anchorAlpha,
            PeakShift = peakShift,
            RiseMult = riseMult,
            DeclineAccelMult = declineAccel,
            OfferExperienceMult = offerExp,
            SalaryAskMult = salaryAsk,
            AgeRiskMult = ageRisk,
            RepFloorRelaxTiers = repFloorRelax,
            PayBudgetBu = payBu,
            SalaryOfferMult = salaryOffer,
            XpMults = xp,
            InjuryDurabilityDelta = injuryDurability,
            InjuryBaseAdd = injuryBase,
            StatPointsPerLevelBonus = statPointsPerLevel,
            Conditional = conditional,
        };
    }
}
