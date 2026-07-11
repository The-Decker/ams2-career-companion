using System.Text.Json.Serialization;
using Companion.Core.Character;

namespace Companion.Core.Career;

/// <summary>Why a player DNF'd, for OPI blame assignment (docs/dev/career-sim.md, Player model).</summary>
public enum DnfCause
{
    /// <summary>The car broke — no blame: scores as the expected finish.</summary>
    Mechanical,

    /// <summary>The driver binned it — full blame: scores as the grid size.</summary>
    DriverError,
}

/// <summary>Per-AI-driver career state, folded from the journal. Ratings are stored as
/// drift DELTAS against the pinned pack baseline so the raw pack stays immutable.</summary>
public sealed record DriverCareerState
{
    /// <summary>Lineage id, stable across era packs ("driver.j_clark").</summary>
    public required string DriverId { get; init; }

    /// <summary>Age in the season this state describes.</summary>
    public required int Age { get; init; }

    /// <summary>Cumulative raceSkill drift vs the pack baseline (aging + form shocks).</summary>
    public double RaceSkillDelta { get; init; }

    /// <summary>Cumulative qualifyingSkill drift vs the pack baseline.</summary>
    public double QualifyingSkillDelta { get; init; }

    /// <summary>Short-term form, reserved for the per-round `form` stream (v1: carried, not
    /// consumed by the season-end pipeline).</summary>
    public double Form { get; init; }

    public bool Retired { get; init; }
}

/// <summary>Per-team career state.</summary>
public sealed record TeamCareerState
{
    /// <summary>Team id within the current pack (equals the lineage id in v1 packs).</summary>
    public required string TeamId { get; init; }

    /// <summary>Lineage id, stable across era packs ("team.lotus") — the M6 era-transition key.</summary>
    public required string LineageId { get; init; }

    /// <summary>Budget tier 1–5; 5 is the richest (tier drives scalar bands, salary bands,
    /// and expectations).</summary>
    public required int Tier { get; init; }
}

/// <summary>The player's career state.</summary>
public sealed record PlayerCareerState
{
    /// <summary>Reputation 0–100.</summary>
    public double Reputation { get; init; }

    /// <summary>Overperformance index: EWMA of (expectedFinish − actualFinish).</summary>
    public double Opi { get; init; }

    /// <summary>Pace anchor: EWMA (α=0.3) of the player's implied pace in Opponent Skill
    /// slider percent. 0 means "not yet calibrated" — the first round seeds it directly.</summary>
    public double PaceAnchor { get; init; }

    /// <summary>Qualifying (one-lap) pace anchor: EWMA (α=0.3) of the player's implied one-lap
    /// pace, calibrated from the qualifying order on weekend rounds. 0 = not yet calibrated;
    /// single-race careers never set it. (Increment 2.)</summary>
    public double QualifyingAnchor { get; init; }

    public int SeasonsCompleted { get; init; }

    public string? CurrentTeamId { get; init; }

    /// <summary>EXACT ams2LiveryName of the player's seat — identifies which pack entry the
    /// player occupies (that entry is excluded from the AI seat market).</summary>
    public string? LiveryName { get; init; }

    // ---- Character system (Increment 4a) ----
    // All default/null for a career created before the character system. Each is omitted from the
    // serialized state blob when default (WhenWritingDefault), so a character-free career's
    // player_state is BYTE-IDENTICAL to today's — the character layer perturbs nothing until a
    // character is actually created (docs/dev/character-system.md §9).

    /// <summary>The player's authored character (seven stats + perk ids + unspent CP), or null for
    /// a pre-character career. Written once at creation (folded from the <c>player.character</c>
    /// INPUT row); <see cref="CharacterProfile.CpUnspent"/> updates as CP are spent between seasons.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public CharacterProfile? Character { get; init; }

    /// <summary>Current character level (1-based; 0 = no character). Derived from <see cref="Xp"/>
    /// via the XP curve and journaled on each level-up.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Level { get; init; }

    /// <summary>Total accumulated character XP (a pure function of journaled results).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long Xp { get; init; }

    /// <summary>Within-season injury load banked from the driver-error-DNF injury perks (glass_cannon /
    /// hot_head <c>perErrorAdd</c>): each driver-error DNF adds its perk's per-error contribution, and
    /// the season-end injury roll reads the total ON TOP of the base hazard, then resets it to 0 so it
    /// never compounds across seasons. 0 for any career without a perErrorAdd injury perk ⇒ the
    /// player_state blob is byte-identical (WhenWritingDefault omits it). (Task #18.)</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double SeasonInjuryLoad { get; init; }

    /// <summary>True once a character has been created for this career.</summary>
    [JsonIgnore]
    public bool HasCharacter => Character is not null;

    // ---- Chosen grid (v0.6.0 "choose the entire grid") ----

    /// <summary>The season field the player chose at creation (the liveries on the grid), or null
    /// for the whole-pack field. A creation-time deterministic INPUT seeded into the season start
    /// state and carried forward each round; the fold resolves the grid to exactly this field so the
    /// sim scores the chosen grid, not the canonical one. Omitted when null (WhenWritingDefault), so
    /// a career that chose the whole field is byte-identical to one made before this feature.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Companion.Core.Grid.GridSelection? GridSelection { get; init; }

    // ---- Form-reactive sim (Ratings Phase 3) ----

    /// <summary>True for a career created with Ratings Phase 3: the FOLD's grid resolution reacts to
    /// the pack's per-race <see cref="Companion.Core.Packs.SeasonDefinition.DriverForm"/>, so the
    /// player's expected finish / OPI / pace anchor shift when a RIVAL is hot that weekend. A
    /// creation-time deterministic capability seeded into the season start state and carried forward
    /// each round (record <c>with</c>); the form VALUES come only from the pinned pack, never the save.
    /// Omitted when false (WhenWritingDefault) so a pre-Phase-3 career — including existing careers on
    /// packs that already ship DriverForm — is byte-identical and folds form-inert forever.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool FormAware { get; init; }

    // ---- SMGP replica mode (M3) ----

    /// <summary>The SMGP replica mode's folded state (rival tallies, the player's current car,
    /// seat-swap displacements, titles, the Zeroforce game-over flag), or null for every career
    /// outside the mode. Seeded at creation ONLY when the pack declares <c>careerStyle "smgp"</c>
    /// AND the creation request opted in — mirroring <see cref="FormAware"/> — and carried forward
    /// each round via record <c>with</c>, so rollover/season-end re-derive it identically. Omitted
    /// when null (WhenWritingNull): every existing career's player_state blob is byte-identical
    /// and the whole mode stays inert.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Companion.Core.Smgp.SmgpState? Smgp { get; init; }
}

/// <summary>A driver available to the AI seat market (free agents / journeymen the caller
/// authors or carries between seasons). Pay budget is in Budget Units per season —
/// era-correct pay-driver seats are first-class.</summary>
public sealed record SeatCandidate
{
    public required string DriverId { get; init; }

    public required double RaceSkill { get; init; }

    public required int Age { get; init; }

    /// <summary>Sponsorship money the driver brings, in BU/season (0 = pure merit hire).</summary>
    public double PayBudgetBu { get; init; }

    /// <summary>Reputation 0–100 in the seat market's eyes. Pack drivers entering the pool
    /// (season carryover, wizard-authored journeymen) default via
    /// <see cref="DefaultReputation"/> from the budget tier of the team they last drove for;
    /// unknown outsiders start at 0.</summary>
    public double Reputation { get; init; }

    /// <summary>Tier-derived default reputation: 15 per budget tier (tier 1 minnow driver
    /// ⇒ 15, tier 5 works driver ⇒ 75) — consistent with the offer rep floors (30/50/70).</summary>
    public static double DefaultReputation(int budgetTier) => 15.0 * Math.Clamp(budgetTier, 1, 5);
}

/// <summary>A season-end offer letter to the player.</summary>
public sealed record PlayerOffer
{
    public required string TeamId { get; init; }

    public required int Tier { get; init; }

    /// <summary>Offered salary in Budget Units per season.</summary>
    public required double SalaryBu { get; init; }

    /// <summary>The archetype-weighted score that ranked this offer (kept for the "why?" inspector).</summary>
    public required double Score { get; init; }
}
