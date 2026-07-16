namespace Companion.Core.Character;

/// <summary>
/// Resolves the immutable character profile into the complete modifier bundle consumed by the
/// deterministic career fold. Legacy perks are always resolved first. Progression-v2 mastery
/// effects are an explicitly versioned overlay, so profiles written before the overlay shipped
/// retain the exact legacy object and behavior even when they already own mastery nodes.
/// </summary>
public static class CharacterModifierResolver
{
    public static PlayerPerkModifiers Resolve(
        CharacterProfile profile,
        CharacterRules legacyRules,
        MasterySkillCatalog? masterySkills,
        IReadOnlySet<string>? activeConditions = null,
        IReadOnlySet<string>? masteryConditions = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(legacyRules);

        // This call deliberately precedes every mastery gate. Besides preserving the established
        // exception/validation behavior, it makes the v0 return the exact object PerkResolver made.
        PlayerPerkModifiers legacy = PerkResolver.Resolve(profile, legacyRules, activeConditions);
        IReadOnlySet<string>? resolvedMasteryConditions = masteryConditions ?? activeConditions;

        if (profile.MasteryEffectsVersion == 0)
            return legacy;
        if (profile.MasteryEffectsVersion != CharacterProfile.CurrentMasteryEffectsVersion)
        {
            throw new InvalidOperationException(
                $"Character mastery-effects version {profile.MasteryEffectsVersion} is not supported.");
        }
        if (profile.ProgressionVersion != CharacterLevelProgression.Level300Version)
        {
            throw new InvalidOperationException(
                "Active mastery effects require a progression-v2 character profile.");
        }

        // The profile's marketability is a first-class v2 meta-stat, not a mastery purchase. The
        // profile gate is the compatibility boundary: old/default-0 profiles retain the exact
        // legacy bundle above, while newly-authored v2 profiles receive their persisted base value
        // immediately. Keep the transient mastery marker at zero until an actual mastery effect is
        // owned so legacy XP/reputation arithmetic does not enter the v2 aggregate-clamp path.
        double marketabilityBase = Math.Clamp(
            profile.Stat("marketability") + (legacy.Marketability - 0.5),
            0.0,
            1.0);

        IReadOnlyList<string>? acquiredIds = profile.AcquiredSkillIds;
        if (acquiredIds is not { Count: > 0 })
            return legacy with { Marketability = marketabilityBase };
        if (masterySkills is null)
            throw new InvalidOperationException("Active mastery effects require the versioned mastery-skill catalog.");

        var owned = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? id in acquiredIds)
        {
            if (string.IsNullOrWhiteSpace(id) || !masterySkills.TryGetSkill(id, out _))
                throw new InvalidOperationException($"Character owns unknown mastery skill '{id ?? "<null>"}'.");
            if (!owned.Add(id))
                throw new InvalidOperationException($"Character repeats mastery skill '{id}'.");
        }

        // Additive and multiplicative effects accumulate independently in catalog order. Applying
        // (legacy + additive sum) * multiplicative product once at the end makes ownership-list
        // ordering irrelevant and gives each catalog aggregate clamp exactly one application.
        var talent = legacy.TalentDeltas.ToDictionary(
            pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var xpProducts = new Dictionary<string, double>(StringComparer.Ordinal);

        double weightAdd = 0.0, powerAdd = 0.0, dragAdd = 0.0;
        double opiRetentionAdd = 0.0, opiGainProduct = 1.0;
        double errorBlameAdd = 0.0, errorFloorAdd = 0.0;
        double repRoundGainProduct = 1.0, repRoundSignedProduct = 1.0, repSeasonProduct = 1.0;
        double marketabilityAdd = 0.0, anchorAlphaAdd = 0.0;
        double peakShiftAdd = 0.0, declineAccelerationAdd = 0.0;
        double offerExperienceAdd = 0.0, offerExperienceProduct = 1.0;
        double lowTierOfferAdd = 0.0, lowTierOfferProduct = 1.0;
        double salaryAskAdd = 0.0, salaryAskProduct = 1.0;
        double ageRiskProduct = 1.0, salaryOfferProduct = 1.0;
        double payBudgetAdd = 0.0, reputationFloorRelaxAdd = 0.0;
        double roundXpFloorProduct = 1.0;
        double injuryDurabilityAdd = 0.0, injuryBaseAdd = 0.0;
        bool masteryInjuryRisk = false;
        double statSoftCapAdd = 0.0;

        foreach (MasterySkillDefinition skill in masterySkills.Skills)
        {
            if (!owned.Contains(skill.Id))
                continue;

            foreach (MasterySkillEffect effect in skill.Effects)
            {
                if (effect.Condition is { } condition && !ConditionIsActive(condition, resolvedMasteryConditions))
                    continue;

                MasteryEffectOperation operation = effect.Operation
                    ?? throw InvalidEffect(skill.Id, effect, "has no operation");
                string? target = effect.Target;
                double magnitude = effect.Magnitude;

                switch (effect.Lever)
                {
                    case "statDelta":
                    {
                        RequireOperation(skill.Id, effect, operation, MasteryEffectOperation.Add);
                        string rating = target
                            ?? throw InvalidEffect(skill.Id, effect, "has no rating target");
                        if (rating == "chosenFlavor")
                        {
                            string? chosenFlavor = profile.ChosenFlavor;
                            rating = PerkResolver.IsEligibleChosenFlavor(chosenFlavor)
                                ? chosenFlavor!
                                : throw new InvalidOperationException(
                                    $"Mastery skill '{skill.Id}' requires a supported persisted chosen flavor.");
                        }
                        talent[rating] = talent.GetValueOrDefault(rating) + magnitude;
                        break;
                    }
                    case "carScalar":
                        RequireOperation(skill.Id, effect, operation, MasteryEffectOperation.Add);
                        switch (target)
                        {
                            case "weight": weightAdd += magnitude; break;
                            case "power": powerAdd += magnitude; break;
                            case "drag": dragAdd += magnitude; break;
                            default: throw InvalidEffect(skill.Id, effect, "has an unknown CAR axis");
                        }
                        break;
                    case "xpRate":
                    {
                        RequireOperation(skill.Id, effect, operation, MasteryEffectOperation.Multiply);
                        string cause = target switch
                        {
                            "round" => "all",
                            "mechanicalDnf" => "dnfMechanical",
                            not null => target,
                            _ => throw InvalidEffect(skill.Id, effect, "has no XP cause"),
                        };
                        xpProducts[cause] = xpProducts.GetValueOrDefault(cause, 1.0) * magnitude;
                        break;
                    }
                    case "reputationRate":
                        RequireOperation(skill.Id, effect, operation, MasteryEffectOperation.Multiply);
                        switch (target)
                        {
                            case "round": repRoundGainProduct *= magnitude; break;
                            case "signedRound": repRoundSignedProduct *= magnitude; break;
                            case "season": repSeasonProduct *= magnitude; break;
                            default: throw InvalidEffect(skill.Id, effect, "has an unknown reputation target");
                        }
                        break;
                    case "offerWeight":
                        switch (target)
                        {
                            case "experience":
                                Accumulate(operation, magnitude, ref offerExperienceAdd, ref offerExperienceProduct);
                                break;
                            case "lowTier":
                                Accumulate(operation, magnitude, ref lowTierOfferAdd, ref lowTierOfferProduct);
                                break;
                            case "salaryAsk":
                                Accumulate(operation, magnitude, ref salaryAskAdd, ref salaryAskProduct);
                                break;
                            default:
                                throw InvalidEffect(skill.Id, effect, "has an unknown offer-weight target");
                        }
                        break;
                    case "roundXpFloor":
                        RequireOperation(skill.Id, effect, operation, MasteryEffectOperation.Multiply);
                        if (target is not ("round" or "finishVsExpected"))
                            throw InvalidEffect(skill.Id, effect, "has an unknown round-XP-floor target");
                        roundXpFloorProduct *= magnitude;
                        break;
                    case "marketability":
                        RequireUntargetedAdd(skill.Id, effect, operation);
                        marketabilityAdd += magnitude;
                        break;
                    case "paceAnchorAlpha":
                        RequireUntargetedAdd(skill.Id, effect, operation);
                        anchorAlphaAdd += magnitude;
                        break;
                    case "injuryDurability":
                        RequireUntargetedAdd(skill.Id, effect, operation);
                        injuryDurabilityAdd += magnitude;
                        break;
                    case "injuryBase":
                        RequireUntargetedAdd(skill.Id, effect, operation);
                        injuryBaseAdd += magnitude;
                        masteryInjuryRisk = true;
                        break;
                    case "salaryAsk":
                        RequireUntargetedMultiply(skill.Id, effect, operation);
                        salaryAskProduct *= magnitude;
                        break;
                    case "salaryOffer":
                        RequireUntargetedMultiply(skill.Id, effect, operation);
                        salaryOfferProduct *= magnitude;
                        break;
                    case "portablePayBudget":
                        RequireUntargetedAdd(skill.Id, effect, operation);
                        payBudgetAdd += magnitude;
                        break;
                    case "reputationFloorTier":
                        RequireUntargetedAdd(skill.Id, effect, operation);
                        // Authored as a negative tier delta (consider one tier lower); the existing
                        // offer model stores the same policy as a positive number of relaxed tiers.
                        reputationFloorRelaxAdd += -magnitude;
                        break;
                    case "ageRisk":
                        RequireUntargetedMultiply(skill.Id, effect, operation);
                        ageRiskProduct *= magnitude;
                        break;
                    case "opiRetention":
                        RequireUntargetedAdd(skill.Id, effect, operation);
                        opiRetentionAdd += magnitude;
                        break;
                    case "opiGainSide":
                        RequireUntargetedMultiply(skill.Id, effect, operation);
                        opiGainProduct *= magnitude;
                        break;
                    case "opiErrorBlame":
                        RequireUntargetedAdd(skill.Id, effect, operation);
                        errorBlameAdd += magnitude;
                        break;
                    case "opiErrorFloorBlend":
                        RequireUntargetedAdd(skill.Id, effect, operation);
                        errorFloorAdd += magnitude;
                        break;
                    case "declineAcceleration":
                        RequireUntargetedAdd(skill.Id, effect, operation);
                        declineAccelerationAdd += magnitude;
                        break;
                    case "peakAgeShift":
                        RequireUntargetedAdd(skill.Id, effect, operation);
                        peakShiftAdd += magnitude;
                        break;
                    case "statSoftCap":
                        RequireUntargetedAdd(skill.Id, effect, operation);
                        statSoftCapAdd += magnitude;
                        break;
                    default:
                        throw InvalidEffect(skill.Id, effect, "uses an unsupported lever");
                }
            }
        }

        var xp = legacy.XpMults.ToDictionary(
            pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach ((string cause, double product) in xpProducts)
            xp[cause] = legacy.XpMult(cause) * product;

        int reputationFloorRelax = (int)Math.Round(
            legacy.RepFloorRelaxTiers + reputationFloorRelaxAdd,
            MidpointRounding.AwayFromZero);

        return legacy with
        {
            MasteryEffectsVersion = CharacterProfile.CurrentMasteryEffectsVersion,
            TalentDeltas = talent,
            WeightScalarDelta = legacy.WeightScalarDelta + weightAdd,
            PowerScalarDelta = legacy.PowerScalarDelta + powerAdd,
            DragScalarDelta = legacy.DragScalarDelta + dragAdd,
            OpiRetention = Clamp(masterySkills, "opiRetention", legacy.OpiRetention + opiRetentionAdd),
            OpiGainScale = legacy.OpiGainScale * opiGainProduct,
            ErrorBlameScale = Clamp(masterySkills, "errorBlameScale", legacy.ErrorBlameScale + errorBlameAdd),
            BlameFloorBlend = Clamp(masterySkills, "errorFloorBlend", legacy.BlameFloorBlend + errorFloorAdd),
            RepRoundGainMult = legacy.RepRoundGainMult * repRoundGainProduct,
            RepRoundSignedMult = legacy.RepRoundSignedMult * repRoundSignedProduct,
            RepSeasonMult = legacy.RepSeasonMult * repSeasonProduct,
            Marketability = Clamp(masterySkills, "marketability", marketabilityBase + marketabilityAdd),
            AnchorAlpha = Clamp(masterySkills, "paceAnchorAlpha", legacy.AnchorAlpha + anchorAlphaAdd),
            PeakShift = legacy.PeakShift + peakShiftAdd,
            DeclineAccelMult = Clamp(
                masterySkills, "declineAcceleration", legacy.DeclineAccelMult + declineAccelerationAdd),
            OfferExperienceMult =
                (legacy.OfferExperienceMult + offerExperienceAdd) * offerExperienceProduct,
            LowTierOfferWeightBonus =
                (legacy.LowTierOfferWeightBonus + lowTierOfferAdd) * lowTierOfferProduct,
            SalaryAskMult = Clamp(
                masterySkills,
                "salaryMultiplier",
                (legacy.SalaryAskMult + salaryAskAdd) * salaryAskProduct),
            AgeRiskMult = Clamp(masterySkills, "ageRiskMultiplier", legacy.AgeRiskMult * ageRiskProduct),
            RepFloorRelaxTiers = Math.Clamp(reputationFloorRelax, 0, 1),
            PayBudgetBu = Clamp(
                masterySkills, "portablePayBudgetBonus", legacy.PayBudgetBu + payBudgetAdd),
            SalaryOfferMult = Clamp(
                masterySkills, "salaryMultiplier", legacy.SalaryOfferMult * salaryOfferProduct),
            XpMults = xp,
            RoundXpFloorMultiplier = legacy.RoundXpFloorMultiplier * roundXpFloorProduct,
            InjuryDurabilityDelta = legacy.InjuryDurabilityDelta + injuryDurabilityAdd,
            InjuryBaseAdd = legacy.InjuryBaseAdd + injuryBaseAdd,
            MasteryInjuryRisk = masteryInjuryRisk,
            StatSoftCapDelta = legacy.StatSoftCapDelta + statSoftCapAdd,
        };
    }

    private static bool ConditionIsActive(string condition, IReadOnlySet<string>? activeConditions)
    {
        if (activeConditions is null)
            return false;
        if (activeConditions.Contains(condition))
            return true;
        return condition switch
        {
            "ageBeforePeak" => activeConditions.Contains("ageLtPeak"),
            "ageAtOrAfterPeak" => activeConditions.Contains("ageGtePeak"),
            "tierAtMost2" => activeConditions.Contains("tierLte2"),
            "tierAtLeast4" => activeConditions.Contains("tierGte4"),
            _ => false,
        };
    }

    private static void Accumulate(
        MasteryEffectOperation operation,
        double magnitude,
        ref double additive,
        ref double product)
    {
        if (operation == MasteryEffectOperation.Add)
            additive += magnitude;
        else if (operation == MasteryEffectOperation.Multiply)
            product *= magnitude;
        else
            throw new InvalidOperationException($"Unsupported mastery effect operation '{operation}'.");
    }

    private static void RequireUntargetedAdd(
        string skillId,
        MasterySkillEffect effect,
        MasteryEffectOperation operation)
    {
        if (effect.Target is not null)
            throw InvalidEffect(skillId, effect, "must not have a target");
        RequireOperation(skillId, effect, operation, MasteryEffectOperation.Add);
    }

    private static void RequireUntargetedMultiply(
        string skillId,
        MasterySkillEffect effect,
        MasteryEffectOperation operation)
    {
        if (effect.Target is not null)
            throw InvalidEffect(skillId, effect, "must not have a target");
        RequireOperation(skillId, effect, operation, MasteryEffectOperation.Multiply);
    }

    private static void RequireOperation(
        string skillId,
        MasterySkillEffect effect,
        MasteryEffectOperation actual,
        MasteryEffectOperation expected)
    {
        if (actual != expected)
            throw InvalidEffect(skillId, effect, $"must use {expected}");
    }

    private static InvalidOperationException InvalidEffect(
        string skillId,
        MasterySkillEffect effect,
        string detail) =>
        new($"Mastery skill '{skillId}' effect '{effect.Lever}' {detail}.");

    private static double Clamp(MasterySkillCatalog catalog, string id, double value)
    {
        if (!catalog.AggregateClamps.TryGetValue(id, out IReadOnlyList<double>? range) || range.Count != 2)
            throw new InvalidOperationException($"Mastery catalog has no valid '{id}' aggregate clamp.");
        return Math.Clamp(value, range[0], range[1]);
    }
}
