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
    public const string PlayerPaceAnchor = "player.paceAnchor";
    public const string RaceResult = "race.result";
    public const string DriverAging = "driver.aging";
    public const string Retirement = "driver.retirement";
    public const string RetirementForeshadow = "driver.retirement.foreshadow";
    public const string SeatMarket = "seat.market";
    public const string OfferExtended = "offer.extended";
    public const string TeamTier = "team.tier";
    public const string Headline = "news.headline";
}
