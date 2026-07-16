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
    /// <summary>The rating One-Trick Pony's specialism defaults to when a profile carries the perk
    /// but named no chosen flavor (a legacy character created before the pick existed). A fixed
    /// constant so every call site — live and replay — resolves the same delta byte-for-byte.</summary>
    public const string DefaultChosenFlavor = "wetSkill";

    /// <summary>The closed set of rating fields a persisted chosen-flavor input may target. Pace's
    /// automatic <c>raceSkill</c> lever is intentionally excluded; these are the same ten choices
    /// exposed by character creation and all are writable by <see cref="CharacterRatingWriter"/>.</summary>
    public static IReadOnlyList<string> EligibleChosenFlavors { get; } = Array.AsReadOnly(
        [
            "wetSkill", "tyreManagement", "qualifyingSkill", "aggression", "defending",
            "avoidanceOfMistakes", "consistency", "startReactions", "fuelManagement", "stamina",
        ]);

    private static readonly IReadOnlySet<string> EligibleChosenFlavorSet =
        EligibleChosenFlavors.ToHashSet(StringComparer.Ordinal);

    public static bool IsEligibleChosenFlavor(string? value) =>
        value is not null && EligibleChosenFlavorSet.Contains(value);

    /// <summary>Resolve straight from a character profile — threads the profile's chosen flavor so
    /// One-Trick Pony's <c>chosenFlavor</c>/<c>lockToOne</c> bind to the rating the player picked.
    /// Prefer this overload wherever a full <see cref="CharacterProfile"/> is in hand.</summary>
    public static PlayerPerkModifiers Resolve(
        CharacterProfile character,
        CharacterRules rules,
        IReadOnlySet<string>? activeConditions = null) =>
        Resolve(character.PerkIds, rules, activeConditions, character.ChosenFlavor);

    public static PlayerPerkModifiers Resolve(
        IReadOnlyList<string> perkIds,
        CharacterRules rules,
        IReadOnlySet<string>? activeConditions = null,
        string? chosenFlavor = null)
    {
        if (perkIds.Count == 0)
            return PlayerPerkModifiers.Identity;

        // One-Trick Pony's chosenFlavor/lockToOne both bind to this one rating (the player's pick,
        // or a fixed deterministic default for a legacy profile that named none).
        string flavor = chosenFlavor ?? DefaultChosenFlavor;
        string? lockedFlavor = null;

        var talent = new Dictionary<string, double>(StringComparer.Ordinal);
        var xp = new Dictionary<string, double>(StringComparer.Ordinal);
        var conditional = new List<ConditionalPerkEffect>();

        double weight = 0, power = 0, drag = 0;
        double opiRetention = OpiMath.Retention, opiGain = 1.0, errorBlame = 1.0, blameFloor = 0.0;
        double repRound = 1.0, repSeason = 1.0, marketability = 0.5, underdogLow = 0.0, topTier = 1.0;
        double anchorAlpha = PaceAnchorMath.Alpha;
        double peakShift = 0.0, declineAccel = 1.0;
        double offerExp = 1.0, salaryAsk = 1.0, ageRisk = 1.0, payBu = 0.0, salaryOffer = 1.0;
        int repFloorRelax = 0, statPointsPerLevel = 0;
        double injuryDurability = 0.0, injuryBase = 0.0, statSoftCap = 0.0;

        // One lever→accumulator mapping, reused by both unconditional effects and the conditional
        // effects whose condition holds for this round (activeConditions) — so a fired conditional
        // stacks onto exactly the same field its unconditional twin would.
        void ApplyEffect(string lever, string? target, double m)
        {
            switch (lever)
            {
                case "statDelta" when target is { } rating:
                    // "chosenFlavor" is the One-Trick Pony placeholder — resolve it to the concrete
                    // rating the player's specialism owns before it lands on a talent field.
                    string field = rating == "chosenFlavor" ? flavor : rating;
                    talent[field] = talent.GetValueOrDefault(field) + m;
                    break;
                case "carScalar":
                    switch (target)
                    {
                        case "weight": weight += m; break;
                        case "power": power += m; break;
                        case "drag": drag += m; break;
                    }
                    break;
                case "opiRetention":
                    if (target == "gainSide") opiGain += m; else opiRetention += m;
                    break;
                case "opiErrorBlame":
                    if (target == "floorBlend") blameFloor += m; else errorBlame += m;
                    break;
                case "reputationGainRate":
                    if (target is "round" or "both") repRound += m;
                    if (target is "season" or "both") repSeason += m;
                    break;
                case "underdogMultiplier":
                    if (target == "topTierMult") topTier += m; else underdogLow += m;
                    break;
                case "marketability":
                    marketability += m;
                    break;
                case "paceAnchorAlpha":
                    anchorAlpha += m;
                    break;
                case "agingCurve":
                    switch (target)
                    {
                        case "peakShift": peakShift += m; break;
                        case "declineAccelMult": declineAccel += m; break;
                    }
                    break;
                case "offerWeight":
                    switch (target)
                    {
                        case "experience": offerExp += m; break;
                        case "salaryAsk": salaryAsk += m; break;
                        case "ageRisk": ageRisk += m; break;
                        case "repFloorRelax": repFloorRelax += (int)Math.Round(m); break;
                    }
                    break;
                case "income":
                    if (target == "salaryOfferMult") salaryOffer += m; else payBu += m;
                    break;
                case "injuryHazard":
                    if (target == "durabilityDelta") injuryDurability += m;
                    else injuryBase += m; // baseAdd (unconditional perErrorAdd folds in here too)
                    break;
                case "xpRate":
                    string cause = target ?? "all";
                    xp[cause] = xp.GetValueOrDefault(cause, 1.0) + m;
                    break;
                case "statPoints":
                    if (target == "perLevel") statPointsPerLevel += (int)Math.Round(m);
                    else if (target == "softCap") statSoftCap += m;
                    else if (target == "lockToOne") lockedFlavor = flavor;
                    break;
            }
        }

        foreach (string perkId in perkIds)
        {
            var perk = rules.PerkById(perkId);
            foreach (var effect in perk.Effects)
            {
                if (effect.Condition is not null)
                {
                    // Fold the conditional effect in ONLY when the fold says its condition holds this
                    // round; otherwise carry it (dormant) so a caller with no round context is
                    // byte-identical to before.
                    if (activeConditions is not null && activeConditions.Contains(effect.Condition))
                        ApplyEffect(effect.Lever, effect.Target, effect.Magnitude);
                    else
                        conditional.Add(new ConditionalPerkEffect(
                            effect.Lever, effect.Target, effect.Magnitude, effect.Condition));
                    continue;
                }

                ApplyEffect(effect.Lever, effect.Target, effect.Magnitude);
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
            StatSoftCapDelta = statSoftCap,
            LockedFlavorRating = lockedFlavor,
            Conditional = conditional,
        };
    }
}
