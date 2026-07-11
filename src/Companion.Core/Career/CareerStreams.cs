namespace Companion.Core.Career;

/// <summary>The named RNG streams of the career sim (docs/dev/career-sim.md, Determinism).
/// One stream per (subsystem, year, round, entity); these are the only subsystem names the
/// sim may consume. String values are part of the save format — never rename.</summary>
public static class CareerStreams
{
    public const string Offers = "offers";
    public const string Aging = "aging";
    public const string Retirement = "retirement";
    public const string Form = "form";
    public const string Events = "events";
    public const string Headlines = "headlines";
    public const string TierDrift = "tier-drift";

    /// <summary>The opt-in season-end injury roll (character depth 6): drawn ONLY for a character
    /// carrying an injury-stream perk, so a default career consumes zero new draws and stays
    /// replay-compatible with pre-character saves. Keyed (injury, year, 0, "player").</summary>
    public const string Injury = "injury";
}

/// <summary>Journal phase names emitted by the career sim. Part of the save format (the news
/// feed, "why?" inspector, and headline template bank key on them) — never rename.</summary>
public static class JournalPhases
{
    public const string Championship = "season.championship";
    public const string PlayerOpi = "player.opi";
    public const string PlayerReputation = "player.reputation";

    /// <summary>The player's character, written ONCE at creation (round = null) — an INPUT the
    /// round fold never regenerates, so it is provenance-excluded from the replay byte-compare
    /// while its data rides in the start player state. (Increment 4a.)</summary>
    public const string PlayerCharacter = "player.character";

    /// <summary>The player's chosen season field (v0.6.0 "choose the entire grid"), written ONCE at
    /// creation (round = null) — an INPUT the round fold never regenerates, so it is
    /// provenance-excluded from the byte-compare while its data rides in the start player state.</summary>
    public const string PlayerGridSelection = "player.gridSelection";

    /// <summary>A between-season character-development spend (character depth 4): raise a stat or
    /// add a perk. A player-choice INPUT (round = null) applied at the next season's transition and
    /// re-applied identically on replay, so it is provenance-excluded from the byte-compare like
    /// <see cref="PlayerCharacter"/>.</summary>
    public const string PlayerStatSpend = "player.statSpend";

    /// <summary>The player's character XP update for the round — a DERIVED fold output (a pure
    /// function of the result), emitted only for a character career after the reputation row, so a
    /// pre-character career's journal sequence is unchanged. (Increment 4a.)</summary>
    public const string PlayerXp = "player.xp";

    /// <summary>A resolved Setup Gamble (called shot): a DERIVED row emitted only when the player
    /// called a finish better than the sim expected (the call rides the raw result envelope). It
    /// stakes reputation (and XP for a character) on hitting the call. Absent for every round without
    /// a real gamble, so an ordinary career's journal sequence is unchanged. (Setup Gamble, 4b.)</summary>
    public const string PlayerCall = "player.call";

    /// <summary>A season-end injury (character depth 6): a DERIVED row emitted only when a
    /// character carrying an injury-stream perk fails the season-end injury roll. OPI-neutral — a
    /// setback to standing, never a finishing position. Absent for every other career, so their
    /// journal sequence is unchanged.</summary>
    public const string PlayerInjury = "player.injury";

    /// <summary>The player's SeasonsCompleted increment at season end (journal/state parity:
    /// every state change is a journal row).</summary>
    public const string PlayerExperience = "player.experience";
    public const string PlayerPaceAnchor = "player.paceAnchor";

    /// <summary>The player's qualifying (one-lap) anchor update — emitted ONLY on rounds that ran
    /// a qualifying session, so single-race careers never carry it and stay byte-identical. (Inc 2.)</summary>
    public const string PlayerQualiAnchor = "player.qualiAnchor";
    public const string RaceResult = "race.result";
    public const string DriverAging = "driver.aging";
    public const string Retirement = "driver.retirement";
    public const string RetirementForeshadow = "driver.retirement.foreshadow";
    public const string SeatMarket = "seat.market";
    public const string OfferExtended = "offer.extended";
    public const string TeamTier = "team.tier";
    public const string Headline = "news.headline";

    /// <summary>An SMGP rival battle's resolution (M3, careerStyle "smgp"): a DERIVED row emitted
    /// only when the round's raw envelope stored a rival call AND the career carries the mode's
    /// folded state — every other career's journal sequence is unchanged. Carries both finishes,
    /// the outcome, the post-battle streaks and what they triggered (the Why? inspector's view of
    /// the two-wins ladder).</summary>
    public const string SmgpBattle = "smgp.battle";

    /// <summary>An SMGP seat reassignment (M3): the accepted swap / forfeit displacement chain —
    /// who now drives what. Emitted only when a battle actually moved seats.</summary>
    public const string SmgpSeat = "smgp.seat";

    /// <summary>The SMGP season-end fold (M3 slice 4): the championship title increment + the
    /// Madonna title-defense arming (champion), or the between-seasons streak reset (everyone
    /// else, only when something actually reset). Absent for every non-smgp career.</summary>
    public const string SmgpTitle = "smgp.title";

    /// <summary>One row per bridged gap year of an era transition (M6): the aging +
    /// retirement pass nobody played, keyed with the bridged year.</summary>
    public const string EraBridge = "era.bridge";

    /// <summary>An entity (team or driver) that does not reach the next era pack.</summary>
    public const string EraDeparted = "era.departed";

    /// <summary>The Budget-Unit rescale note across an era boundary (v1: identity — the
    /// seam for the Phase-2 economy).</summary>
    public const string EraEconomy = "era.economy";
}
