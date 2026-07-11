using Companion.Ams2.Skins;
using Companion.Core.Character;
using Companion.Core.Grid;
using Companion.Core.Numerics;
using Companion.Core.Packs;
using Companion.Core.Scoring;

namespace Companion.ViewModels.Services;

/// <summary>
/// The app's only gateway to career state (docs/dev/app-shell.md "Services seam").
/// v1 backs this with CareerDatabase + StandingsEngine + packs + Grid; the M5 career sim
/// (OPI/reputation updates, season-end offers, real headlines) extends the implementation —
/// additively, without redesigning this interface.
/// </summary>
public interface ICareerSession
{
    CareerSummary Summary { get; }

    /// <summary>Briefing data for the current round (null when the season is complete).</summary>
    BriefingModel? CurrentBriefing();

    /// <summary>Stage the current round's generated grid into the AMS2 install
    /// (backup-first). Returns the staging outcome for the briefing banner.</summary>
    StageOutcome StageCurrentGrid();

    /// <summary>The current round's seats, in grid order, for the result-entry screen.</summary>
    IReadOnlyList<GridSeat> CurrentGrid();

    /// <summary>The sim's expected finishing position for the player this round (1-based), computed
    /// from the resolved grid exactly as the fold does — so the Setup Gamble briefing frames a call
    /// against the same number the bet is later staked on. Null when the season is complete or the
    /// player has no seat this round. Additive default so fakes without it compile. (Setup Gamble, 4b.)</summary>
    int? CurrentExpectedFinish() => null;

    /// <summary>The SMGP briefing panel's data for the current round (M3 slice 5), or null —
    /// outside the mode, or when the season is complete. Additive default so fakes compile.</summary>
    SmgpBriefingModel? CurrentSmgpBriefing() => null;

    /// <summary>The current round's race-weekend structure (practice/qualifying + 1–2 races),
    /// or null when the round runs today's single race. Additive default — sessions without
    /// weekend support (and every single-race round) report "no weekend". (Increment 2.)</summary>
    PackWeekend? CurrentWeekend() => null;

    /// <summary>What skin every car on this round's grid will show in AMS2 — the read-only
    /// resolution of the driver → <c>livery_name</c> → installed livery NAME → skin chain
    /// (correlated against the installed skin overrides + NAMeS file + stock library). Powers
    /// the briefing's Skins panel: the player's-own-car "pick this livery in-game" crib and the
    /// per-AI-car skin picture. Pure read-only projection — writes nothing, never touches the
    /// user's community files. Additive default: an empty plan, so existing fakes compile.</summary>
    SkinAssignmentPlan CurrentSkinAssignments() => SkinAssignmentPlan.Empty;

    /// <summary>Switches an installed-but-inactive livery (a "##" placeholder) ON for this class by
    /// assigning it a real slot in the community override XML — the fix for "the skin is installed
    /// but AMS2 doesn't show it" (e.g. 1985 Skoal #10). The one place the app writes a COMMUNITY skin
    /// file: it snapshots the file first (timestamped backup) and makes a minimal in-place edit, only
    /// on this explicit user action. Does NOT touch the career journal/fold, so the sim is unaffected.
    /// Additive default: a clear "not supported" failure so existing fakes compile.</summary>
    LiveryActivationResult ActivateLivery(string liveryName) =>
        LiveryActivationResult.Failed("This career session cannot activate liveries.");

    /// <summary>The Skin Season Manager's view of this pack's declared skin season
    /// (<c>pack.json skinSeason</c>): per car model, whether the install's active override pointer
    /// is this season's, another known season's, a per-race variant, or unrecognized. Null when the
    /// pack declares no season, the library has no such set, or there is no install. Read-only —
    /// powers the Skins tab's season panel. Additive default so existing fakes compile.</summary>
    SkinSeasonStatus? CurrentSkinSeasonStatus() => null;

    /// <summary>Switches the install onto this pack's declared skin season: writes each car model's
    /// season pointer XML over the active one, backup-first (all-or-nothing per set; an
    /// unrecognized user file refuses without <paramref name="force"/> — the AI-file contract).
    /// Skin files only — never the career DB / sim / oracle. Additive default: a clear
    /// "not supported" failure so existing fakes compile.</summary>
    SkinSeasonApplyResult ActivateSkinSeason(bool force = false) => new()
    {
        Success = false,
        Applied = 0,
        Message = "This career session cannot switch skin seasons.",
    };

    /// <summary>The grid editor's current per-seat COSMETIC overrides for this season, keyed by the
    /// seat's original <c>ams2LiveryName</c>: a custom driver name and/or a rebound livery, applied
    /// only to the staged custom-AI file (never the sim). Empty default so existing fakes compile.</summary>
    IReadOnlyDictionary<string, SeatStagingOverride> SeatStagingOverrides() =>
        new Dictionary<string, SeatStagingOverride>(StringComparer.Ordinal);

    /// <summary>Saves one seat's grid-editor override (rename / rebind livery), keyed by its original
    /// livery; an empty override clears it. Persisted per season OUTSIDE the journal, so the sim/fold
    /// stay byte-identical. Applied at the next stage. Additive default: a no-op so fakes compile.</summary>
    void SetSeatStagingOverride(string liveryKey, SeatStagingOverride seatOverride) { }

    /// <summary>Score a draft without committing — feeds the confirm screen.</summary>
    ConfirmModel Preview(ResultDraft draft);

    /// <summary>Persist the result (raw payload + journal), advance to the next round.</summary>
    void Apply(ResultDraft draft);

    /// <summary>Standings after the most recently applied round (null before round 1).</summary>
    StandingsSnapshot? CurrentStandings();

    /// <summary>Every per-round snapshot so far, for the round matrix.</summary>
    IReadOnlyList<StandingsSnapshot> AllSnapshots();

    /// <summary>Era-skinned news dispatches for the career so far, newest first — a read-only
    /// projection over the journal. Increment 1 re-renders the existing <c>news.headline</c>
    /// rows; the generative multi-slot article grammar is a later slice. Additive default:
    /// sessions without a news projection report an empty feed, so existing fakes compile.</summary>
    IReadOnlyList<NewsDispatch> ReadFeed() => [];

    /// <summary>The total-recall History/Scrapbook projection (career-hub-design.md §4/decision
    /// 18): one lineage-aware card per season in the career — its year, the player's final
    /// championship position, final reputation/OPI, the drivers' champion, and the season's key
    /// headlines — plus an aggregate records book (best finish, wins, podiums, points, seasons)
    /// rolled up across every season. Pure read-only projection over the same stored results,
    /// folded player states and journal the other lenses read — re-derivable byte-identically,
    /// no new persistence. Additive default: sessions without the projection report an empty
    /// timeline, so existing fakes compile. (Increment 3.)</summary>
    CareerTimeline CareerTimeline() => Services.CareerTimeline.Empty;

    /// <summary>The REAL historical results of a season (f1db-derived, CC BY 4.0) — "what really
    /// happened" reference content the History tab shows ALONGSIDE the player's own (diverged) career
    /// for the same year, clearly separated. Null when no history is shipped for that year. Pure
    /// read-only reference: the sim/fold never scores it, so it can never affect a replayed result.
    /// Additive default: sessions without it report null, so existing fakes compile.</summary>
    HistoricalSeason? HistoricalSeason(int year) => null;

    /// <summary>The SMGP-universe "What Really Happened" almanac — the History tab's FICTIONAL-world
    /// counterpart to <see cref="HistoricalSeason"/>. A replica (SMGP) career is a made-up SEGA world,
    /// so it never gets the real-F1 documents; instead each circuit carries the SEGA world's OWN legend,
    /// unlocked once the player has finished that race. Venue-keyed (so season 2+ calendar variety still
    /// resolves each place), display-only reference — the sim/fold never reads it. Null for every
    /// non-SMGP career and when no almanac data is shipped. Additive default: null, so existing fakes
    /// compile.</summary>
    SmgpWorldHistory? SmgpWorldHistory() => null;

    /// <summary>The clickable-everywhere "Why?" inspector (career-hub-design.md §5, decisions 4 +
    /// 5): walks the append-only journal rows that produced a number the hub shows and returns them
    /// as an ordered plain-language contribution breakdown. <paramref name="entity"/> is the journal
    /// <c>entity</c> to walk (e.g. <c>"player"</c>, a driver id, a constructor id, a team id);
    /// <paramref name="round"/> narrows to a single round when given, else the whole season's rows
    /// for that entity are chained (oldest first). Pure read-only projection over the SAME journal
    /// the news feed and replay byte-check read — no new persistence, and deterministic (ordered by
    /// journal <c>seq</c>). The breakdown is an ORDERED LIST of labelled rows, not a single string,
    /// so it accepts perk/stat contribution rows later (decision 5) with no format change. Additive
    /// default: sessions without the projection report an empty chain, so existing fakes compile.</summary>
    JournalChain JournalFor(string entity, int? round = null) => JournalChain.Empty;

    /// <summary>The season-scoped "Why?" inspector: the same walk as
    /// <see cref="JournalFor(string,int?)"/>, but over the season whose year is
    /// <paramref name="seasonYear"/> rather than the CURRENT season — so a History card for ANY
    /// completed season can open the inspector for that season's numbers (final standing, champion,
    /// records), not just the current one (career-hub-design.md §4/§5, decision 18 "total recall").
    /// Resolves the season row for the year in the same career file, then runs the identical read-only
    /// projection over THAT season's journal — deterministic (journal <c>seq</c> order, ordinal
    /// comparisons), pure, no new persistence. When no season matches the year the chain is empty
    /// (a graceful no-op, never a throw). A DISTINCT name (not a <see cref="JournalFor(string,int?)"/>
    /// overload) so an int-literal round can never bind here by mistake. Additive default: sessions
    /// without the projection report an empty chain, so every existing fake compiles unchanged.</summary>
    JournalChain JournalForSeason(string entity, int seasonYear, int? round = null) => JournalChain.Empty;

    /// <summary>Recommended Opponent Skill slider (70–120) for the CURRENT round, from the
    /// last folded round's pace anchor. Null before the anchor calibrates (fresh careers).
    /// Shown in the briefing and prefilled on the result screen — never auto-applied.</summary>
    int? CurrentSliderRecommendation();

    /// <summary>The season review + offers screen data; null until the season is complete.
    /// The first call after the final round runs the season-end pipeline (offers scored from
    /// the final round's FOLDED player state) if it has not run yet.</summary>
    SeasonReviewModel? SeasonReview();

    /// <summary>Accepts one offer letter (at most one acceptance per season — a new choice
    /// clears the previous one) and journals the choice. Throws when the team made no offer.</summary>
    void AcceptOffer(string teamId);

    /// <summary>The next era pack for sign-and-continue (M6). Null while the season is
    /// incomplete or when no discovered pack has a season year greater than the current one.
    /// v1 rule: the pack with the SMALLEST year strictly greater than the current season year
    /// wins; the years in between are bridged, never blocked. Additive member — sessions
    /// without era-transition support report "no next pack".</summary>
    NextSeasonInfo? NextSeason() => null;

    /// <summary>Signs the ACCEPTED offer into the next era pack: builds the
    /// <c>EraTransition</c> plan from the completed season's persisted end states and starts
    /// the new season via <c>CareerStore.StartNextSeason</c>. <paramref name="teamId"/> must
    /// be the accepted offer's team. Throws <see cref="InvalidOperationException"/> carrying
    /// the plan's validation errors (e.g. the accepted team missing from the new pack) — the
    /// review screen surfaces them. After success THIS session still points at the finished
    /// season: reopen the career file to land in the new season. Additive member.</summary>
    void StartNextSeason(string teamId) => throw new NotSupportedException(
        "This career session does not support era transitions.");

    /// <summary>The player's driver id + chosen character name for this season, so screens that
    /// render driver names (grid / result entry, standings, round matrix) show the character on the
    /// player's row instead of the historical driver whose seat they took. Null when the career has
    /// no named character (then the historical name shows, as before). Additive default: null.</summary>
    (string DriverId, string DisplayName)? PlayerIdentity() => null;

    /// <summary>The name of the team the player currently drives for (from the folded player state's
    /// current team), or null when unknown. Additive default: null.</summary>
    string? PlayerTeamName() => null;

    /// <summary>The player's driver dossier (character depth 3): name, the seven stats, the chosen
    /// perks with what they do, and progression (level + XP toward the next), projected from the
    /// current folded player state + the character rules. Null for a career with no character (or no
    /// character rules loaded) — so the hub shows the Driver tab only when there is a driver to show.
    /// Pure read-only projection, re-derivable, no new persistence. Additive default: null, so every
    /// existing fake compiles.</summary>
    CharacterDossier? CharacterDossier() => null;

    /// <summary>Character points the driver has available to spend on between-season development
    /// (character depth 4): creation leftover + level grants − already spent, minus this season's
    /// pending spends. 0 for a career with no character. Additive default: 0.</summary>
    int AvailableCharacterCp() => 0;

    /// <summary>Records a between-season development spend — raise a stat one step or add a perk,
    /// journaled and applied at the next season's transition (re-derived identically on replay).
    /// Additive default throws, so a session without character support says so.</summary>
    void SpendCharacterPoint(CharacterSpend spend) => throw new NotSupportedException(
        "This career session does not support character development.");

    /// <summary>The perks the driver can BUY with banked points right now (character depth 4): each
    /// positive-cost perk not already owned (or pending) that the player can currently afford, with
    /// its real cost and plain-language benefits/drawbacks. Empty for a career with no character or no
    /// points to spend. Additive default: empty.</summary>
    IReadOnlyList<PurchasablePerk> PurchasablePerks() => [];

    /// <summary>The whole season's TRACK schedule, up front and spoiler-free (the Calendar lens): one
    /// entry per round with its real venue, the ACTUAL AMS2 track that will be driven (after any opt-in
    /// alternate swap, since the pinned pack carries it), and whether that track is the real venue, a
    /// base stand-in, or an applied mod alternate — plus, when an alternate exists that was NOT enabled,
    /// its name so the player sees what they could have raced. Pure read-only projection of the pinned
    /// pack + content library; no results, so nothing is hidden. Additive default: empty.</summary>
    IReadOnlyList<SeasonScheduleEntry> SeasonSchedule() => [];

    SeasonPack Pack { get; }
}

/// <summary>How a round's driven AMS2 track relates to its real historical venue.</summary>
public enum SeasonTrackKind
{
    /// <summary>The AMS2 track IS the round's real venue.</summary>
    RealVenue,

    /// <summary>A base/DLC stand-in (the real venue isn't in AMS2) — a labelled placeholder.</summary>
    StandIn,

    /// <summary>An opt-in community MOD alternate the player enabled at career creation.</summary>
    Alternate,
}

/// <summary>One round of the season's track schedule (the Calendar lens) — spoiler-free, all known
/// from the pinned pack the moment the career starts.</summary>
public sealed record SeasonScheduleEntry
{
    public required int Round { get; init; }
    public required string Name { get; init; }
    public required string Date { get; init; }
    /// <summary>The historical venue's name (always on record, even for a stand-in).</summary>
    public required string RealVenue { get; init; }
    /// <summary>The AMS2 track actually driven this round (its library display name).</summary>
    public required string Ams2TrackName { get; init; }
    public required int Laps { get; init; }
    public required SeasonTrackKind Kind { get; init; }
    /// <summary>When the round has an alternate that is NOT the driven track (the player didn't enable
    /// alternates, or a required mod was missing) — the alternate's display name, so the schedule can
    /// note "alternate available: …". Null when no unused alternate.</summary>
    public string? UnusedAlternateName { get; init; }

    /// <summary>The REAL (historical) circuit's map layout id — the ORIGINAL venue's shape, NOT the
    /// stand-in track's. Keys the shipped circuit-map SVG. Empty when no history is shipped for the
    /// year. (The expandable calendar card shows the original circuit + facts.)</summary>
    public string CircuitLayoutId { get; init; } = "";

    /// <summary>The original circuit's one-line caption (name · place · km · turns · direction).</summary>
    public string CircuitCaption { get; init; } = "";

    /// <summary>A brief, data-grounded history of the original circuit. Empty when unknown.</summary>
    public string CircuitHistory { get; init; } = "";

    /// <summary>Era-capped fun facts about the original circuit (data-grounded, spoiler-free).
    /// Empty when none are shipped.</summary>
    public IReadOnlyList<string> CircuitFacts { get; init; } = [];
}

/// <summary>One perk offered on the season-review development block: what it is, what it costs, and —
/// in plain language — what it does, for a Buy button that spends banked points (character depth 4).</summary>
public sealed record PurchasablePerk
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required int Cost { get; init; }
    public required IReadOnlyList<string> Benefits { get; init; }
    public required IReadOnlyList<string> Drawbacks { get; init; }
}

/// <summary>The discovered next era pack for the season review's sign-and-continue block.</summary>
public sealed record NextSeasonInfo
{
    public required string PackDirectory { get; init; }

    public required string PackId { get; init; }

    public required string PackName { get; init; }

    public required int SeasonYear { get; init; }

    /// <summary>True when this is a CARRYOVER: no dedicated pack exists for <see cref="SeasonYear"/>,
    /// so the career reuses the CURRENT car/liveries (the same pinned pack) for one more year — the
    /// grid having aged, retired, and refilled at season end. False for a real era CHANGEOVER into a
    /// later-year pack (<see cref="PackDirectory"/> then points at that pack on disk; for a carryover
    /// it is empty, since the pinned pack is reused, not re-read).</summary>
    public bool IsCarryover { get; init; }

    /// <summary>The years between the finished season and the next pack (ascending, empty
    /// for consecutive years). With year-by-year carryover this is always empty — every year is
    /// played, either on a dedicated pack or as a carryover — so no year is silently bridged.</summary>
    public required IReadOnlyList<int> BridgedYears { get; init; }
}

public sealed record CareerSummary
{
    public required string CareerName { get; init; }
    public required int SeasonYear { get; init; }
    public required string SeriesName { get; init; }
    public required int CurrentRound { get; init; }
    public required int RoundCount { get; init; }
    public required string PlayerDriverId { get; init; }
    public required string PlayerLiveryName { get; init; }
    /// <summary>Championship position after the last applied round; null before round 1.</summary>
    public int? PlayerPosition { get; init; }
    public bool SeasonComplete { get; init; }

    /// <summary>Reputation after the last FOLDED round (0–100); null before round 1.</summary>
    public double? Reputation { get; init; }

    /// <summary>Overperformance index after the last folded round; null before round 1.</summary>
    public double? Opi { get; init; }

    /// <summary>Reputation movement of the last folded round (vs the round before it, or the
    /// season-start state after round 1); null when no trend exists yet.</summary>
    public double? ReputationDelta { get; init; }

    /// <summary>OPI movement of the last folded round; null when no trend exists yet.</summary>
    public double? OpiDelta { get; init; }
}

public sealed record BriefingModel
{
    public required PackRound Round { get; init; }
    /// <summary>Real venue name; equals the track name unless the round is a placeholder.</summary>
    public required string VenueDisplayName { get; init; }
    public required bool IsPlaceholder { get; init; }
    /// <summary>Ordered label/value pairs, each rendered with a copy button — the exact
    /// in-game strings (track, class, laps, date, time, weather, opponents).</summary>
    public required IReadOnlyList<CopyableSetting> Settings { get; init; }
    public string? SetupNotes { get; init; }
    /// <summary>Set after staging; the file watcher monitors this path.</summary>
    public string? StagedFilePath { get; init; }

    /// <summary>The difficulty recommendation for this round (70–120 Opponent Skill percent),
    /// from the folded pace anchor. Null before the anchor calibrates.</summary>
    public int? RecommendedSlider { get; init; }

    /// <summary>Advisory fuel-and-distance guidance for this round (the car's one-tank range vs the
    /// race length, plus the AMS2 "set your own fuel" gotcha), shown as an advisory panel like the
    /// difficulty recommendation — NOT a tick row. Null when no per-class fuel profile applies.</summary>
    public string? FuelNote { get; init; }
}

/// <summary>One in-game setting for the briefing checklist. <see cref="Section"/> groups the row
/// under a Race-Day heading ("Event", "Practice", "Qualifying", "Race", "Rules"); empty = ungrouped.
/// Display-only grouping — the checklist tick is keyed by (section, label) so identical labels in
/// different sessions (e.g. "Weather slot 1") never collide.</summary>
public sealed record CopyableSetting(string Label, string Value)
{
    public string Section { get; init; } = "";
}

/// <summary>One news-feed item (hub News tab / dock). A resolved period headline plus the
/// plain-language "why" (the journal delta as a sentence, for the Why? chip) and the era it
/// belongs to. <see cref="Body"/> is the expanded article shown when the ticker item is
/// clicked; empty means "the headline is the whole story" for now.</summary>
public sealed record NewsDispatch
{
    public required string Headline { get; init; }
    public required int SeasonYear { get; init; }

    /// <summary>The round this dispatch belongs to; null for season-level items.</summary>
    public int? Round { get; init; }

    /// <summary>Journal kind (e.g. "race", "season", "offer") — drives filtering + chrome.</summary>
    public string Kind { get; init; } = "race";

    /// <summary>The Why? chip's plain sentence, derived from the source journal row's delta.</summary>
    public string WhyText { get; init; } = "";

    /// <summary>The expanded period article; empty until the generative grammar slice lands.</summary>
    public string Body { get; init; } = "";
}

/// <summary>The History/Scrapbook projection: the per-season lineage of cards plus the aggregate
/// records book (decision 18, "total recall"). A pure read model — no session coupling — so the
/// History view-model can be built and tested from a plain value.</summary>
public sealed record CareerTimeline
{
    /// <summary>Empty timeline (the seam default, and a fresh career before its first season
    /// has any applied round).</summary>
    public static readonly CareerTimeline Empty = new();

    /// <summary>One card per season in the career, oldest season first (the lineage order).
    /// A season with no applied round yet still appears — its result fields read "in progress".</summary>
    public IReadOnlyList<CareerSeasonCard> Seasons { get; init; } = [];

    /// <summary>Career-spanning bests/streaks/milestones aggregated across every season's
    /// per-round snapshots.</summary>
    public CareerRecordsBook Records { get; init; } = CareerRecordsBook.Empty;

    public bool IsEmpty => Seasons.Count == 0;
}

/// <summary>One season's scrapbook card: the year, the player's final standing, final folded
/// reputation/OPI, the drivers' champion, and the season's key headlines.</summary>
public sealed record CareerSeasonCard
{
    public required int SeasonYear { get; init; }

    /// <summary>The player's final championship position, or null when unclassified / the season
    /// has no applied round yet.</summary>
    public int? PlayerPosition { get; init; }

    /// <summary>How many championship rounds have an applied result in this season.</summary>
    public required int RoundsApplied { get; init; }

    public required int RoundCount { get; init; }

    /// <summary>True once every championship round of the season has a result — the season is in
    /// the record books. False = still in progress (the current season, mid-run).</summary>
    public bool IsComplete { get; init; }

    /// <summary>Final reputation after the season-end pipeline (null before the season completes
    /// or when no folded state exists).</summary>
    public double? FinalReputation { get; init; }

    /// <summary>Final overperformance index after the season-end pipeline; null as above.</summary>
    public double? FinalOpi { get; init; }

    /// <summary>The drivers' champion's display name (P1 in the final snapshot); null before any
    /// round is applied.</summary>
    public string? ChampionName { get; init; }

    /// <summary>True when the player IS the drivers' champion — the card's crowning line.</summary>
    public bool PlayerIsChampion { get; init; }

    /// <summary>The season's journaled headlines in story order — the archived dispatches.</summary>
    public IReadOnlyList<string> Headlines { get; init; } = [];
}

/// <summary>The SMGP-universe "What Really Happened" almanac projection: the SEGA world's own legend
/// of every circuit on the CURRENT season's calendar (venue-keyed, so season 2+ variety still resolves
/// each place), each unlocked once the player has raced it. A pure read model — no session coupling —
/// so the History view-model is built and tested from a plain value. Display-only reference: the
/// sim/fold never reads it.</summary>
public sealed record SmgpWorldHistory
{
    /// <summary>Every venue on the calendar, in the current season's round order.</summary>
    public IReadOnlyList<SmgpWorldRace> Races { get; init; } = [];

    /// <summary>How many circuits the player has unlocked so far.</summary>
    public int RevealedCount => Races.Count(r => r.IsRevealed);

    public bool IsEmpty => Races.Count == 0;
}

/// <summary>One circuit's entry in the SMGP-universe almanac: SEALED (a spoiler-free teaser) until the
/// player finishes that round, then the SEGA world's full legend of the place (title, circuit
/// character, the champion of record, the story, and lore notes).</summary>
public sealed record SmgpWorldRace
{
    public required int Round { get; init; }

    /// <summary>The venue name ("San Marino", "Monaco") — the almanac lookup key.</summary>
    public required string VenueName { get; init; }

    /// <summary>True once the player has raced this venue — the legend is unlocked.</summary>
    public required bool IsRevealed { get; init; }

    /// <summary>A bold arcade headline for this circuit's legend; empty when unauthored.</summary>
    public string Title { get; init; } = "";

    /// <summary>One line naming this world's circuit character/nickname; empty when unauthored.</summary>
    public string Circuit { get; init; } = "";

    /// <summary>The champion of record — "who the world remembers ruling here"; empty when unauthored.</summary>
    public string Champion { get; init; } = "";

    /// <summary>The circuit's SMGP-world legend, in paragraphs.</summary>
    public IReadOnlyList<string> Body { get; init; } = [];

    /// <summary>Punchy one-line lore bullets.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}

/// <summary>Career-spanning records: bests, counts and totals aggregated from every season's
/// per-round standings snapshots (wins/podiums/points/best finish/seasons).</summary>
public sealed record CareerRecordsBook
{
    public static readonly CareerRecordsBook Empty = new();

    /// <summary>The player's best (numerically lowest) single-race finishing position across the
    /// whole career; null before any round is applied.</summary>
    public int? BestFinish { get; init; }

    /// <summary>Race wins (finishes classified P1) across the career.</summary>
    public int Wins { get; init; }

    /// <summary>Podiums (finishes classified P1–P3) across the career.</summary>
    public int Podiums { get; init; }

    /// <summary>Total championship points the player has scored across every season (counted
    /// points of the final snapshot of each season, summed).</summary>
    public double TotalPoints { get; init; }

    /// <summary>Drivers' championships won (seasons the player finished P1).</summary>
    public int Championships { get; init; }

    /// <summary>Seasons the player has started (has at least one applied round).</summary>
    public int SeasonsRaced { get; init; }

    /// <summary>The longest streak of consecutive race wins across the career.</summary>
    public int LongestWinStreak { get; init; }

    /// <summary>The longest streak of consecutive podium finishes across the career.</summary>
    public int LongestPodiumStreak { get; init; }
}

/// <summary>The "Why?" inspector's causal chain for one entity (career-hub-design.md §5): the
/// title of the thing being explained, the entity + optional round it was walked for, an ORDERED
/// list of labelled contribution rows (the journal rows that produced the number, oldest first),
/// and a plain-language summary sentence. A pure read model — no session coupling — so the
/// inspector view-model is built and tested from a plain value. The ordered-row shape is the
/// format designed to accept perk/stat rows later (decision 5) without changing the seam.</summary>
public sealed record JournalChain
{
    /// <summary>The empty chain (the seam default, and any entity with no journal rows).</summary>
    public static readonly JournalChain Empty = new();

    /// <summary>The journal entity these contributions were walked for (e.g. <c>"player"</c>, a
    /// driver id, a constructor/team id). Empty on <see cref="Empty"/>.</summary>
    public string Entity { get; init; } = "";

    /// <summary>The round the chain was narrowed to, or null for a whole-season chain.</summary>
    public int? Round { get; init; }

    /// <summary>A human-readable title for the inspector panel header (e.g. "Why P2 — Round 3").</summary>
    public string Title { get; init; } = "";

    /// <summary>The contribution rows that produced the number, in journal <c>seq</c> order
    /// (oldest first) — the walk-back the inspector renders top to bottom.</summary>
    public IReadOnlyList<JournalContribution> Contributions { get; init; } = [];

    /// <summary>A one-line plain-language summary of the chain (the Why? chip's sentence for the
    /// most telling row), empty when nothing explanatory was found.</summary>
    public string Summary { get; init; } = "";

    public bool IsEmpty => Contributions.Count == 0;
}

/// <summary>One labelled row of a <see cref="JournalChain"/>: a short <see cref="Label"/> naming the
/// contribution (e.g. "Expected finish", "Reputation", "Pace anchor", or — when the character layer
/// ships — "tier-4 car", "Pace", "Rain Man"), an optional longer <see cref="Detail"/> sentence, an
/// optional signed/absolute <see cref="Value"/> string for the number itself (e.g. "P8", "−3",
/// "+2 (wet)"), and the source journal <see cref="SourceSeq"/> for provenance. The nullable Value
/// keeps a purely narrative row (a headline, a note) valid alongside a numeric contribution.</summary>
public sealed record JournalContribution
{
    public required string Label { get; init; }

    /// <summary>A longer plain-language detail for the row; empty when the label + value say it all.</summary>
    public string Detail { get; init; } = "";

    /// <summary>The contribution's number as display text (e.g. "P8", "−3", "42.5", "+2 (wet)"),
    /// or null for a narrative row that carries no number.</summary>
    public string? Value { get; init; }

    /// <summary>The journal <c>seq</c> this row was projected from — the provenance anchor and the
    /// deterministic sort key. 0 for a synthesised row that has no single source.</summary>
    public long SourceSeq { get; init; }
}

public sealed record StageOutcome
{
    public required bool Success { get; init; }
    public string? WrittenPath { get; init; }
    public string? BackupPath { get; init; }

    /// <summary>True when staging wrote NOTHING because the installed class XML already
    /// matches the round's generated grid (NAMeS-first diff-aware staging). Success is also
    /// true; <see cref="WrittenPath"/> points at the installed file satisfying the round.</summary>
    public bool NoOpAlreadyMatches { get; init; }

    /// <summary>True when staging wrote nothing ONLY because the installed class XML is the
    /// user's own community file (no generated marker) and staging over it requires the
    /// explicit "Stage anyway" choice. Success is false, but this is an EXPECTED gate, not a
    /// failure — the briefing shows an informational (amber) banner, never a red one.</summary>
    public bool BlockedByForceGate { get; init; }

    public required IReadOnlyList<string> Messages { get; init; }

    /// <summary>Per-file detail lines behind <see cref="Messages"/>' aggregate summaries
    /// (e.g. the livery scan's unreadable files) — shown collapsed behind an expander,
    /// never as a wall of rows.</summary>
    public IReadOnlyList<string> Details { get; init; } = [];
}

/// <summary>What the result-entry screen produces. Positions are implied by list order.</summary>
public sealed record ResultDraft
{
    /// <summary>Driver ids in finishing order (index 0 = P1).</summary>
    public required IReadOnlyList<string> Classified { get; init; }

    /// <summary>Driver id → one-letter DNF reason ("m" mechanical, "a" accident, "o" other).
    /// The stable machine seam: the letter alone is enough for the fold's blame model and for
    /// every existing consumer. Free-text customisation of "o" (and driver-error attribution)
    /// rides alongside in <see cref="DidNotFinishDetail"/> — this map never carries anything
    /// but m/a/o.</summary>
    public required IReadOnlyDictionary<string, string> DidNotFinish { get; init; }

    public required IReadOnlyList<string> Disqualified { get; init; }

    /// <summary>Optional per-DNF custom detail, additive over <see cref="DidNotFinish"/> — the
    /// keys are a subset of that map's keys. Present for a customised "Other" (e.g.
    /// "Engine fire", "Spun off"); absent drivers keep the plain letter meaning. The
    /// <see cref="DnfDetail.DriverAttributed"/> flag lets a custom "other" opt IN to
    /// driver-error blame (default: no blame, matching bare "o"). Older producers omit this
    /// map entirely; consumers must treat a missing key as "no detail".</summary>
    public IReadOnlyDictionary<string, DnfDetail> DidNotFinishDetail { get; init; } =
        new Dictionary<string, DnfDetail>(StringComparer.Ordinal);

    /// <summary>Optional free-text DSQ reason per disqualified driver (e.g. "Underweight",
    /// "Illegal wing"). Keys are a subset of <see cref="Disqualified"/>; absence means no
    /// stated reason. Additive — older producers omit it.</summary>
    public IReadOnlyDictionary<string, string> DisqualifiedDetail { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The in-game Opponent Skill slider the round was actually driven at (asked on
    /// the result screen, prefilled with the last recommendation, editable 70–120). Stored in
    /// the round's raw-result envelope. Null falls back to the current recommendation.</summary>
    public double? SliderUsed { get; init; }

    /// <summary>Whether the round was run in the wet (asked on the result screen). Feeds the
    /// weather-conditional perks (Rain Man, Sunshine Specialist); defaults dry. Stored in the raw
    /// envelope; a character-free career never reads it.</summary>
    public bool IsWet { get; init; }

    /// <summary>The Setup Gamble (called shot) the player committed to before the race — a finishing
    /// position (1-based) they bet on beating. Null = no bet. Stored in the raw envelope; the fold
    /// only resolves it when it is a real gamble (called better than the sim's expected finish), so a
    /// round with no call — or a call no bolder than expected — folds exactly as before. (4b.)</summary>
    public int? CalledShot { get; init; }

    /// <summary>The qualifying order for this round (driver ids, pole first), when the pack's
    /// weekend ran a qualifying session (Increment 2). Null = no qualifying. Stored verbatim in
    /// the raw envelope; never scored. Older producers omit it.</summary>
    public IReadOnlyList<string>? QualifyingOrder { get; init; }

    /// <summary>The SMGP replica mode's rival declaration for this round (M3): who the player
    /// named (or was force-challenged by) and, when the battle triggers a seat-swap offer, the
    /// player's answer. Null = no rival this round — every non-smgp career and every declined
    /// prompt. Stored verbatim in the raw envelope; the fold derives the battle from the result.</summary>
    public Companion.Data.SmgpRivalCall? SmgpRival { get; init; }

    /// <summary>Additional race classifications for an authored TWO-race weekend (Increment 2): the
    /// PRIMARY race is this draft's own <see cref="Classified"/>/<see cref="DidNotFinish"/>/
    /// <see cref="Disqualified"/> (race index 0); each entry here is a further race (index 1…),
    /// scored on its own points table per the pack's <c>weekend.races</c>. Null/empty = today's
    /// single race, so the round scores + folds exactly as before. Older producers omit it.</summary>
    public IReadOnlyList<ExtraRaceResult>? AdditionalRaces { get; init; }
}

/// <summary>The SMGP briefing panel's data (M3 slice 5): the game's round header, the D.P.
/// readout, the pit-crew line, the forced challenger (title-defense rounds) and every namable
/// rival with its dossier facts. Null outside the mode (the panel never renders). Vocabulary
/// strictly per docs/dev/smgp-design.md — nothing invented.</summary>
public sealed record SmgpBriefingModel
{
    /// <summary>The game's Course Select header — "SAN MARINO · ROUND 1".</summary>
    public required string RoundHeader { get; init; }

    /// <summary>The player's points, the game's abbreviation — "12 D.P."</summary>
    public required string PointsLine { get; init; }

    /// <summary>The pit-crew advice line (the manual's own words).</summary>
    public required string AdviceLine { get; init; }

    /// <summary>Championships won in the mode so far (two = the replica is beaten).</summary>
    public required int Titles { get; init; }

    /// <summary>The Zeroforce game-over state — the panel shows it instead of a rival pick.</summary>
    public required bool CareerOver { get; init; }

    /// <summary>The title-defense challenger forced on the player this round, or null for a
    /// free pick. When set, the pick is locked to him.</summary>
    public string? ForcedChallengerDriverId { get; init; }

    /// <summary>Every AI driver on this round's grid, in grid order — any of them can be named.</summary>
    public required IReadOnlyList<SmgpRivalOption> Rivals { get; init; }
}

/// <summary>One namable rival: the dossier card's facts (docs/dev/smgp-design.md — team banner,
/// MACHINE block, portrait slot, a deadpan quote) plus the two-wins ladder telegraphs.</summary>
public sealed record SmgpRivalOption
{
    public required string DriverId { get; init; }

    public required string DriverName { get; init; }

    public required string TeamId { get; init; }

    public required string TeamName { get; init; }

    /// <summary>The MACHINE block line (the car, from the pack).</summary>
    public required string MachineLine { get; init; }

    /// <summary>The rival's deadpan one-liner (the game's own vocabulary).</summary>
    public required string Quote { get; init; }

    /// <summary>Beat him once more (without losing) and "you may get an offer to join his
    /// team!" — the panel telegraphs it and asks for the standing answer.</summary>
    public required bool OfferOnWin { get; init; }

    /// <summary>Lose to him once more and he is offered YOUR seat.</summary>
    public required bool ForfeitOnLoss { get; init; }
}

/// <summary>One additional race's classification in a two-race weekend (<see cref="ResultDraft.AdditionalRaces"/>),
/// beyond the primary race the draft itself carries. Positions are implied by <see cref="Classified"/>
/// order (index 0 = P1), exactly like the primary race. Scoring inputs only — its points come from the
/// race's own table (per the pack weekend); the per-race DNF blame + per-session fold land in a later
/// Increment-2 slice.</summary>
public sealed record ExtraRaceResult
{
    /// <summary>Driver ids in finishing order (index 0 = P1).</summary>
    public required IReadOnlyList<string> Classified { get; init; }

    /// <summary>Driver id → one-letter DNF reason ("m"/"a"/"o"), same stable seam as the primary race.</summary>
    public IReadOnlyDictionary<string, string> DidNotFinish { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public IReadOnlyList<string> Disqualified { get; init; } = [];
}

/// <summary>A customised DNF cause carried beside the one-letter code in
/// <see cref="ResultDraft.DidNotFinishDetail"/>: free text plus whether the cause is the
/// driver's fault. <see cref="DriverAttributed"/> only re-colours the sim's blame model for a
/// custom "other" — 'm'/'a' keep their fixed meaning (mechanical = no blame, accident =
/// driver error) whatever this flag says.</summary>
public sealed record DnfDetail
{
    /// <summary>Free-text cause shown in the UI and journalled (e.g. "Engine fire"). May be
    /// empty when only the attribution matters.</summary>
    public string Text { get; init; } = "";

    /// <summary>True when the user marked this custom "other" cause as the driver's fault, so
    /// the OPI DNF-cause rule treats it as driver-error rather than the no-blame default.</summary>
    public bool DriverAttributed { get; init; }
}

public sealed record ConfirmModel
{
    /// <summary>Per-driver points earned this round (round contribution only).</summary>
    public required IReadOnlyList<(string DriverId, Rational Points)> RoundPoints { get; init; }

    /// <summary>Standings movement: driver, previous position (null at round 1), new position.</summary>
    public required IReadOnlyList<(string DriverId, int? From, int? To)> Movements { get; init; }

    public required string Headline { get; init; }
}
