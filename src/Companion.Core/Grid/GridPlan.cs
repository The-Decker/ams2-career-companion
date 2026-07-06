using Companion.Core.Character;
using Companion.Core.Packs;

namespace Companion.Core.Grid;

/// <summary>
/// The player's seat request for a round. v1 supports exactly one mode — replace a historical
/// driver — identified by that entry's exact <c>ams2LiveryName</c> (the same load-bearing
/// string the custom-AI binding uses). The replaced seat keeps its livery, team performance
/// scalars, and reliability; only <see cref="GridSeat.IsPlayer"/> flips, because the entry is
/// still written into the generated file so the scalars apply to the player's car (the AI
/// skill fields are inert while the player drives the livery).
/// </summary>
public sealed record PlayerSeat
{
    /// <summary>EXACT livery display name (case-sensitive) of the entry the player takes over.</summary>
    public required string Ams2LiveryName { get; init; }

    /// <summary>The player's character applied to this seat, or null for a pre-character career.
    /// When present, the resolver patches the seat's ratings and car scalars from it (see
    /// <see cref="PlayerCharacterPatch"/>); when null the seat is exactly what the pack/track/
    /// override chain produced — byte-identical to a character-free career.</summary>
    public PlayerCharacterPatch? Character { get; init; }
}

/// <summary>The player's character, pre-resolved for the grid patch: the authored profile, the
/// perks' resolved <see cref="PlayerPerkModifiers"/>, and the character rules the rating writer
/// needs. Talent stats + perk deltas patch the seat's <see cref="GridSeat.Ratings"/>
/// (<see cref="CharacterRatingWriter"/>); the perks' car-scalar deltas patch weight/power/drag —
/// the one lever that touches the real car (docs/dev/character-system.md §5-6).</summary>
public sealed record PlayerCharacterPatch
{
    public required CharacterProfile Profile { get; init; }
    public required PlayerPerkModifiers Modifiers { get; init; }
    public required CharacterRules Rules { get; init; }
}

/// <summary>
/// The resolved grid for one calendar round: every seat with merged ratings and team physics,
/// in stable order (entries.json order, then the round's guest entries). Produced by
/// <see cref="RoundGridResolver.Resolve"/>; consumed by the Ams2 grid stager, which maps it
/// 1:1 onto a custom-AI file.
/// </summary>
public sealed record GridPlan
{
    public required string PackId { get; init; }

    public required int Year { get; init; }

    public required string SeriesName { get; init; }

    /// <summary>EXACT vehicle-class xmlName — the custom-AI filename base.</summary>
    public required string Ams2Class { get; init; }

    /// <summary>1-based calendar round this plan was resolved for.</summary>
    public required int Round { get; init; }

    public required string RoundName { get; init; }

    /// <summary>The AMS2 track actually driven this round (placeholder-aware: for placeholder
    /// rounds this is the stand-in, which is what preflight must check the AI cap against).</summary>
    public required string TrackId { get; init; }

    public required IReadOnlyList<GridSeat> Seats { get; init; }
}

/// <summary>One resolved seat: a driver in a car with round-final ratings.</summary>
public sealed record GridSeat
{
    public required string DriverId { get; init; }

    public required string DriverName { get; init; }

    /// <summary>Three-letter country code, when the pack provides one.</summary>
    public string? Country { get; init; }

    public required string TeamId { get; init; }

    public required string TeamName { get; init; }

    /// <summary>Race number as displayed; guest entries may not carry one.</summary>
    public string? Number { get; init; }

    /// <summary>EXACT livery display name (case-sensitive) — the custom-AI binding.</summary>
    public required string Ams2LiveryName { get; init; }

    /// <summary>Round-final ratings: pack baseline, plus the driver's trackForm nudge for the
    /// round's track (additive, clamped 0..1), with the round's aiOverrides patch applied last
    /// (absolute per-field values that beat the nudge).</summary>
    public required PackDriverRatings Ratings { get; init; }

    /// <summary>Team reliability — maps to the custom-AI vehicle_reliability parameter.</summary>
    public required double Reliability { get; init; }

    /// <summary>Team performance scalars (1.0 = neutral). Applied to the file only when != 1.0.</summary>
    public required double WeightScalar { get; init; }

    public required double PowerScalar { get; init; }

    public required double DragScalar { get; init; }

    /// <summary>True when the player drives this livery. The seat is still written to the
    /// generated file — the team scalars must apply to the player's car — and the AI skill
    /// fields stay: they are inert while the player is in the car.</summary>
    public bool IsPlayer { get; init; }

    /// <summary>True when the seat came from the round's guestEntries rather than entries.json.</summary>
    public bool IsGuest { get; init; }
}
