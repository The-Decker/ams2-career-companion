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

    /// <summary>Conditional CAR effects are not safe for v2 until their pre-race condition (most
    /// importantly weather) is persisted before AMS2 staging. Creation/spend paths use this helper,
    /// and the resolver repeats it as a fail-closed guard for hand-authored or corrupt saves.</summary>
    public static void EnsureStagingCompatible(CharacterProfile character, CharacterRules rules)
    {
        foreach (string perkId in character.PerkIds)
        {
            if (!rules.TryGetPerk(perkId, out var perk))
                throw new InvalidOperationException(
                    $"The v2 character references unknown perk '{perkId}'.");
            if (HasConditionalCarScalar(perk))
                throw new InvalidOperationException(
                    $"Perk '{perkId}' has conditional player-car physics. It cannot enter a v2 " +
                    "career until its pre-race condition is persisted for both AMS2 staging and replay.");
        }
    }

    public static bool HasConditionalCarScalar(Perk perk) =>
        perk.Effects.Any(effect =>
            string.Equals(effect.Lever, "carScalar", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(effect.Condition));
}
