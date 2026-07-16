using Companion.Core.Character;

namespace Companion.Core.Grid;

/// <summary>AMS2's verified legal range for player-applicable weight, power and drag scalars.
/// Version-2 composition uses this one policy for both expected-finish math and staged physics.</summary>
public static class PlayerCarScalarPolicy
{
    public const double Minimum = 0.900;
    public const double Maximum = 1.100;

    public static double Compose(double baseScalar, double additiveDelta)
    {
        if (!double.IsFinite(baseScalar) || !double.IsFinite(additiveDelta))
            throw new InvalidOperationException("Player-car scalar inputs must be finite.");
        double composed = baseScalar + additiveDelta;
        if (!double.IsFinite(composed))
            throw new InvalidOperationException("Composed player-car scalar must be finite.");
        return Math.Clamp(composed, Minimum, Maximum);
    }

    /// <summary>Whether this authored build needs a persisted round-condition input before its
    /// player-car scalars may be resolved. Unknown perk ids fail closed at this boundary.</summary>
    public static bool RequiresRoundConditions(
        CharacterProfile character,
        CharacterRules rules,
        MasterySkillCatalog? masterySkills = null)
    {
        bool required = false;
        foreach (string perkId in character.PerkIds)
        {
            if (!rules.TryGetPerk(perkId, out var perk))
                throw new InvalidOperationException(
                    $"The v2 character references unknown perk '{perkId}'.");
            foreach (var effect in perk.Effects.Where(effect =>
                         string.Equals(effect.Lever, "carScalar", StringComparison.Ordinal) &&
                         !string.IsNullOrWhiteSpace(effect.Condition)))
            {
                if (!IsSupportedRoundCondition(effect.Condition!))
                    throw new InvalidOperationException(
                        $"Perk '{perkId}' uses unsupported conditional player-car physics " +
                        $"token '{effect.Condition}'. Add it to a versioned round-condition contract first.");
                required = true;
            }
        }

        // Mastery ownership predates mechanical mastery effects. Version zero must therefore be
        // completely inert: do not require, inspect, or validate a catalog merely because an old
        // profile already carries acquired ids.
        if (character.MasteryEffectsVersion == 0)
            return required;
        if (character.MasteryEffectsVersion != CharacterProfile.CurrentMasteryEffectsVersion)
            throw new InvalidOperationException(
                $"Character mastery-effects version {character.MasteryEffectsVersion} is not supported.");
        if (character.AcquiredSkillIds is not { Count: > 0 } acquiredIds)
            return required;
        if (masterySkills is null)
            throw new InvalidOperationException(
                "Active mastery effects require the versioned mastery-skill catalog.");

        var owned = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? skillId in acquiredIds)
        {
            if (string.IsNullOrWhiteSpace(skillId) || !masterySkills.TryGetSkill(skillId, out var skill))
                throw new InvalidOperationException(
                    $"Character owns unknown mastery skill '{skillId ?? "<null>"}'.");
            if (!owned.Add(skillId))
                throw new InvalidOperationException(
                    $"Character repeats mastery skill '{skillId}'.");

            foreach (var effect in skill.Effects.Where(effect =>
                         string.Equals(effect.Lever, "carScalar", StringComparison.Ordinal) &&
                         !string.IsNullOrWhiteSpace(effect.Condition)))
            {
                if (!IsSupportedRoundCondition(effect.Condition!))
                    throw new InvalidOperationException(
                        $"Mastery skill '{skillId}' uses unsupported conditional player-car physics " +
                        $"token '{effect.Condition}'. Add it to a versioned round-condition contract first.");
                required = true;
            }
        }
        return required;
    }

    /// <summary>Fail-closed staging gate for progression v2. Conditional CAR effects are legal only
    /// when the caller supplies the typed input that was persisted for this exact pinned round.</summary>
    public static void EnsureStagingCompatible(
        CharacterProfile character,
        CharacterRules rules,
        PlayerRoundConditionsInput? conditions,
        Companion.Core.Packs.SeasonPack pack,
        int round,
        MasterySkillCatalog? masterySkills = null)
    {
        if (!RequiresRoundConditions(character, rules, masterySkills))
            return;
        if (conditions is null)
            throw new InvalidOperationException(
                "This v2 character has conditional player-car physics, but the current round has " +
                "no persisted pre-race conditions. Declare wet or dry before staging the grid.");
        PlayerRoundConditions.Validate(conditions, pack, round);
    }

    public static bool HasConditionalCarScalar(Perk perk) =>
        perk.Effects.Any(effect =>
            string.Equals(effect.Lever, "carScalar", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(effect.Condition));

    private static bool IsSupportedRoundCondition(string condition) =>
        condition is "wetRound" or "dryRound" or "longRace" or "shortRace";
}
