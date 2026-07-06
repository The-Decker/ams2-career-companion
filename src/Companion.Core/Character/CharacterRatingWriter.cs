using Companion.Core.Packs;

namespace Companion.Core.Character;

/// <summary>
/// Writes a character's talent stats and perk deltas onto the player seat's
/// <see cref="PackDriverRatings"/> — the pure last step of the grid-resolve merge chain
/// (pack baseline → track form → round overrides → <b>+ character</b>). Each of the five talent
/// stats OVERWRITES its mapped rating field(s) with <c>writeBase + writeSpan·stat</c>; then the
/// perks' unconditional <see cref="PlayerPerkModifiers.TalentDeltas"/> are ADDED (to a written
/// field, or onto the field's existing value); everything is clamped to 0..1. Car-scalar deltas act
/// on the seat, not the ratings, so they are applied separately at the seat level.
///
/// This helper is pure and, until the grid resolver invokes it for a career that HAS a character,
/// nothing calls it — so existing careers and the f1db oracle are byte-identical.
/// </summary>
public static class CharacterRatingWriter
{
    /// <summary>The neutral value a perk delta is applied onto when it touches a rating the seat
    /// has not set and no talent stat writes (a character with the trait is assumed mid-grid).</summary>
    private const double NeutralBaseline = 0.5;

    public static PackDriverRatings Apply(
        PackDriverRatings baseRatings,
        CharacterProfile profile,
        CharacterRules rules,
        PlayerPerkModifiers mods)
    {
        var result = baseRatings;
        var touched = new HashSet<string>(StringComparer.Ordinal);

        // 1. Talent stats overwrite their mapped rating field(s).
        foreach (var stat in rules.Stats.TalentStats)
        {
            if (!profile.Stats.TryGetValue(stat.Id, out double value))
                continue;
            double written = stat.WrittenRating(value);
            foreach (string field in stat.MapsTo)
            {
                result = SetField(result, field, written);
                touched.Add(field);
            }
        }

        // 2. Perk talent deltas add onto the written value (or the field's existing/neutral value).
        foreach (var (field, delta) in mods.TalentDeltas)
        {
            double current = GetField(result, field) ?? NeutralBaseline;
            result = SetField(result, field, current + delta);
            touched.Add(field);
        }

        // 3. Clamp every field the character touched to the valid rating range.
        foreach (string field in touched)
        {
            if (GetField(result, field) is { } value)
                result = SetField(result, field, Math.Clamp(value, 0.0, 1.0));
        }

        return result;
    }

    private static double? GetField(PackDriverRatings r, string field) => field switch
    {
        "raceSkill" => r.RaceSkill,
        "qualifyingSkill" => r.QualifyingSkill,
        "aggression" => r.Aggression,
        "defending" => r.Defending,
        "stamina" => r.Stamina,
        "consistency" => r.Consistency,
        "startReactions" => r.StartReactions,
        "wetSkill" => r.WetSkill,
        "tyreManagement" => r.TyreManagement,
        "avoidanceOfMistakes" => r.AvoidanceOfMistakes,
        "blueFlagConceding" => r.BlueFlagConceding,
        "weatherTyreChanges" => r.WeatherTyreChanges,
        "avoidanceOfForcedMistakes" => r.AvoidanceOfForcedMistakes,
        "fuelManagement" => r.FuelManagement,
        _ => null,
    };

    private static PackDriverRatings SetField(PackDriverRatings r, string field, double value) => field switch
    {
        "raceSkill" => r with { RaceSkill = value },
        "qualifyingSkill" => r with { QualifyingSkill = value },
        "aggression" => r with { Aggression = value },
        "defending" => r with { Defending = value },
        "stamina" => r with { Stamina = value },
        "consistency" => r with { Consistency = value },
        "startReactions" => r with { StartReactions = value },
        "wetSkill" => r with { WetSkill = value },
        "tyreManagement" => r with { TyreManagement = value },
        "avoidanceOfMistakes" => r with { AvoidanceOfMistakes = value },
        "blueFlagConceding" => r with { BlueFlagConceding = value },
        "weatherTyreChanges" => r with { WeatherTyreChanges = value },
        "avoidanceOfForcedMistakes" => r with { AvoidanceOfForcedMistakes = value },
        "fuelManagement" => r with { FuelManagement = value },
        _ => r, // an unknown field name is ignored (the load-time validator owns rejecting them)
    };
}
