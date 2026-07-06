using Companion.Core.Career;

namespace Companion.Core.Character;

/// <summary>
/// The small, identity-defaulting bundle of coefficients a character's perks patch onto the
/// deterministic sim (docs/dev/character-system.md §6.1). Every field defaults to the exact
/// current behaviour, so a career with NO character (or the resolver over an empty perk list)
/// yields <see cref="Identity"/> and the sim is byte-identical — this is what lets the modifier be
/// threaded into the pure functions as an optional parameter without perturbing existing careers or
/// the f1db oracle. Unconditional perk effects are pre-aggregated into the scalar fields;
/// round-conditional effects (wet/long-race/etc.) are carried in <see cref="Conditional"/> for the
/// fold to evaluate per round.
/// </summary>
public sealed record PlayerPerkModifiers
{
    // ---- talent ratings + car scalars (additive deltas at grid resolve) ----

    /// <summary>Additive deltas onto the player seat's <c>PackDriverRatings</c> fields, keyed by
    /// field name (raceSkill, qualifyingSkill, …). Absent field = no delta.</summary>
    public IReadOnlyDictionary<string, double> TalentDeltas { get; init; } = EmptyDeltas;

    public double WeightScalarDelta { get; init; }
    public double PowerScalarDelta { get; init; }
    public double DragScalarDelta { get; init; }

    // ---- OPI (player-local) ----

    public double OpiRetention { get; init; } = OpiMath.Retention;
    public double OpiGainScale { get; init; } = 1.0;
    public double ErrorBlameScale { get; init; } = 1.0;
    public double BlameFloorBlend { get; init; }

    // ---- reputation ----

    public double RepRoundMult { get; init; } = 1.0;
    public double RepSeasonMult { get; init; } = 1.0;
    public double Marketability { get; init; } = 0.5;
    public double UnderdogLowTierBonus { get; init; }
    public double TopTierRepMult { get; init; } = 1.0;

    // ---- pace anchor (player-local) ----

    public double AnchorAlpha { get; init; } = PaceAnchorMath.Alpha;

    // ---- aging (player-local curve override) ----

    public double PeakShift { get; init; }
    public double RiseMult { get; init; } = 1.0;
    public double DeclineAccelMult { get; init; } = 1.0;

    // ---- offers / economy ----

    public double OfferExperienceMult { get; init; } = 1.0;
    public double SalaryAskMult { get; init; } = 1.0;
    public double AgeRiskMult { get; init; } = 1.0;
    public int RepFloorRelaxTiers { get; init; }
    public double PayBudgetBu { get; init; }
    public double SalaryOfferMult { get; init; } = 1.0;

    // ---- XP (per-cause multipliers; absent cause = ×1.0) ----

    public IReadOnlyDictionary<string, double> XpMults { get; init; } = EmptyDeltas;

    // ---- injury (opt-in; only live for a character carrying an injury-stream perk) ----

    public double InjuryDurabilityDelta { get; init; }
    public double InjuryBaseAdd { get; init; }

    // ---- level grants ----

    public int StatPointsPerLevelBonus { get; init; }

    /// <summary>Round-conditional effects the fold applies only when the round meets the condition
    /// (wetRound, longRace, driverErrorDnf, …), so they can't be min-maxed into an unconditional
    /// expectation gain.</summary>
    public IReadOnlyList<ConditionalPerkEffect> Conditional { get; init; } = [];

    private static readonly IReadOnlyDictionary<string, double> EmptyDeltas =
        new Dictionary<string, double>(StringComparer.Ordinal);

    /// <summary>The no-op modifier: the sim behaves exactly as it does with no character system.</summary>
    public static PlayerPerkModifiers Identity { get; } = new();

    /// <summary>The resolved talent delta for one rating field (0 when the perks don't touch it).</summary>
    public double TalentDelta(string ratingField) => TalentDeltas.GetValueOrDefault(ratingField);

    /// <summary>The resolved XP multiplier for a cause key (1.0 when the perks don't touch it).</summary>
    public double XpMult(string cause) => XpMults.TryGetValue(cause, out double m) ? m : 1.0;
}

/// <summary>A perk effect whose application waits on a round condition — carried verbatim from the
/// perk so the fold can evaluate the condition against the round (weather, distance, DNF cause) and
/// then apply the same <c>lever/target/magnitude</c> mapping the unconditional effects use.</summary>
public readonly record struct ConditionalPerkEffect(
    string Lever, string? Target, double Magnitude, string Condition);
