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

    /// <summary>One row per bridged gap year of an era transition (M6): the aging +
    /// retirement pass nobody played, keyed with the bridged year.</summary>
    public const string EraBridge = "era.bridge";

    /// <summary>An entity (team or driver) that does not reach the next era pack.</summary>
    public const string EraDeparted = "era.departed";

    /// <summary>The Budget-Unit rescale note across an era boundary (v1: identity — the
    /// seam for the Phase-2 economy).</summary>
    public const string EraEconomy = "era.economy";
}
