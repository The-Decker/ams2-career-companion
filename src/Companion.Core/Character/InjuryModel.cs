namespace Companion.Core.Character;

/// <summary>
/// The opt-in season-end injury roll (character depth 6): a fragile driver — low durability, and the
/// perks that trade safety for speed — risks a season-end injury that sets their standing back
/// (OPI-neutral; it never touches a finishing position). Only a character carrying an injury-stream
/// perk rolls, so a default career adds no new draws and stays byte-replayable (docs §6.2). Pure
/// math; the season-end pipeline supplies the seeded stream and journals the outcome.
/// </summary>
public static class InjuryModel
{
    /// <summary>The reputation a season-end injury costs — a real setback (offers hinge on it) that
    /// makes a fragile, refund-heavy build a genuine gamble, not free CP.</summary>
    public const double RepPenalty = 8.0;

    /// <summary>The season-end injury hazard, 0..1: a neutral driver sits at 0.10, a fragile one
    /// (low durability, a durability-cutting perk, a dangerous style) climbs toward the cap.</summary>
    public static double Hazard(double durabilityStat, PlayerPerkModifiers mods)
    {
        double durability = durabilityStat + mods.InjuryDurabilityDelta;
        return Math.Clamp(0.10 + (0.5 - durability) * 0.4 + mods.InjuryBaseAdd, 0.0, 0.85);
    }

    /// <summary>True when the character carries any injury-stream perk — the roll auto-enables for
    /// exactly those characters, so nothing else consumes the injury stream.</summary>
    public static bool HasInjuryPerk(CharacterProfile character, CharacterRules rules) =>
        character.PerkIds.Any(id => rules.TryGetPerk(id, out var perk) && perk.Stream == "injury");
}
