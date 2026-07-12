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

    /// <summary>The two-wins seat-swap offer awaiting the player's POST-RACE decision on the
    /// promotion screen (3c-2), or null — outside the mode, a legacy (inline-apply) career, or no
    /// offer pending this round. Non-null = show the promotion screen after confirm. Additive
    /// default so fakes compile.</summary>
    Companion.Core.Smgp.SmgpPendingOffer? CurrentSmgpPendingOffer() => null;

    /// <summary>Resolve the pending two-wins offer (3c-2): ACCEPT moves the player into the offered
    /// car (effective from the next round's grid); DECLINE keeps the current seat. Journals the
    /// decision as the provenance-excluded <c>smgp.swap</c> input and re-persists the round it
    /// belongs to, so replay re-derives the outcome byte-identically. Additive default: no-op so
    /// fakes compile.</summary>
    void ResolveSmgpOffer(bool accept) { }

    /// <summary>The full-immersion promotion screen's data (3c-3) when a two-wins offer is pending —
    /// the new team's photo/name/motto/history/quotes + the player image + car preview + accept/
    /// decline. Null outside the mode or when no offer is pending. Additive default so fakes compile.</summary>
    SmgpPromotionModel? CurrentSmgpPromotion() => null;

    /// <summary>The locked 17-season campaign FINALE (the "final final screen") when the current SMGP
    /// season is a COMPLETED campaign summit — reaching the end of all <see cref="Companion.Core.Smgp.SmgpRules.CampaignSeasons"/>
    /// seasons unlocks the secret <c>special.jpg</c>; being champion in all of them unlocks the deeper
    /// <c>ultimate.jpg</c>. Null in every other case (outside the mode, mid-campaign, or a career ended
    /// on the D-floor). Pure DISPLAY-ONLY read over folded state — never a fold input. Additive default
    /// so fakes compile.</summary>
    SmgpFinaleModel? SmgpFinale() => null;

    /// <summary>The SMGP Paddock lens: the whole grid's drivers (bio + predetermined career stats +
    /// team) and teams (motto + history + quotes + roster), for the driver/team-preview rail tab.
    /// DISPLAY-ONLY (reads the pack roster + the SMGP reference data). Null outside the SMGP mode or
    /// when no rules are loaded. Additive default so fakes compile.</summary>
    SmgpPaddockModel? SmgpPaddock() => null;

    /// <summary>The SMGP "living world" DISPATCH feed (Task 4): reactive in-world news stories the career
    /// generates as it unfolds — the player's wins / firsts / promotions / titles / rivalries / setbacks
    /// (from <see cref="Companion.Core.Smgp.SmgpCareerBeats"/>) plus AI-world stories (a rival's win streak,
    /// the A. Senna benchmark, the title race tightening, a standings move — from
    /// <see cref="Companion.Core.Smgp.SmgpWorldStories"/>), voiced through the dispatch corpus. Newest first.
    /// A pure DISPLAY-ONLY projection over the folded results (deterministic body selection off the master
    /// seed) — never a fold input, so replay stays byte-identical. Empty outside the SMGP mode / before any
    /// round. Additive default so fakes compile.</summary>
    IReadOnlyList<Companion.Core.Smgp.SmgpDispatch> SmgpDispatches() => [];

    /// <summary>The TYCOON TEAM MODE read-only DATA SPINE (Task 5) for the reserved top-header team mode: the
    /// player's team dashboard (roster + sponsors + ladder tier + derived constructors' standing + SMGP-world
    /// history) plus the whole grid of teams ranked as the competitive landscape, and a flavour "team of the
    /// season" seed for the future economy. A pure DISPLAY-ONLY projection over the folded results + the SMGP
    /// reference data (builds on <see cref="SmgpPaddock"/> + <see cref="CurrentStandings"/>) — NO team-management
    /// fold mechanics, so it is replay-safe. Null outside the SMGP mode. Additive default so fakes compile.</summary>
    SmgpTeamDashboard? SmgpTeamDashboard() => null;

    /// <summary>The player's SMGP team id right now (its short ladder position follows seat swaps),
    /// captured BEFORE applying a round so the shell can tell whether that round forced a DEMOTION
    /// (a seat move with no pending offer). Null outside the mode. Additive default so fakes compile.</summary>
    string? CurrentSmgpTeamId() => null;

    /// <summary>The driver id of the rival the player NAMED in the most-recently applied round (a per-round
    /// choice, from that round's stored <c>SmgpRival</c> call), or null — every non-SMGP career and any
    /// round where no rival was named. Lets the standings flag "your rival" for highlight. A pure read over
    /// the persisted result envelopes; never a fold input. Additive default so fakes compile.</summary>
    string? CurrentSmgpRivalDriverId() => null;

    /// <summary>The demotion screen's data (3c-3) when the LAST applied round forced the player DOWN
    /// a tier (a two-losses forfeit or a lost title defense) — i.e. the smgp team changed from
    /// <paramref name="previousTeamId"/> with no pending offer. An acknowledge-only screen (a demotion
    /// cannot be declined). Null when no forced move happened. Additive default so fakes compile.</summary>
    SmgpPromotionModel? CurrentSmgpDemotion(string? previousTeamId) => null;

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

    /// <summary>The player's current car spec card (machine/engine/power + ENG-TM-SUS-TIRE-BRA bars),
    /// resolved from the player's team/vehicle via the car-specs catalog; null when there is no
    /// authored spec (or no rules). Display-only. Additive default: null.</summary>
    CarSpecCardViewModel? PlayerCarSpec() => null;

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

    // ---- Character death & injury: mortality mode + Normal save/reload (Slice 1) ----

    /// <summary>The career's mortality mode (Off / Normal / Hardcore), chosen at creation
    /// (docs/dev/character-death-injury.md §2). Off = no injury/death (classic). Normal = injury/death
    /// with the save &amp; reload safety net below. Hardcore = injury/death, no saves, death deletes the
    /// file. Additive default: <see cref="Companion.Core.Career.MortalityMode.Off"/>, so fakes compile.</summary>
    Companion.Core.Career.MortalityMode Mortality => Companion.Core.Career.MortalityMode.Off;

    /// <summary>The player's current mortality/availability status (character death &amp; injury §3.3): the
    /// mode, whether the driver is injured (sitting out N races), out for the season, or deceased, and
    /// whether a Hardcore death has already deleted the career file (the session is then spent — the shell
    /// shows the permadeath screen and must not touch the session again). Additive default: a fit
    /// Off-mode career, so fakes compile.</summary>
    PlayerMortalityStatus PlayerMortality() => new()
    {
        Mode = Companion.Core.Career.MortalityMode.Off,
        Deceased = false,
        SeasonEndingInjury = false,
        RaceSuspensionRemaining = 0,
        CareerFileDeleted = false,
    };

    /// <summary>The rich death-screen projection (character death &amp; injury §6) when the driver has died —
    /// an in-world obituary, the career record, the fatal accident's cause/venue, and (Normal) the
    /// restorable save slots. Null when the driver is alive. Captured before the file is deleted on a
    /// Hardcore death, so it is safe to read after the DB is gone. Additive default: null, so fakes compile.</summary>
    DeathScreenModel? DeathScreen() => null;

    /// <summary>The player's SIT-OUT status when an injury forces the CURRENT round to be auto-simulated
    /// (character death &amp; injury §5), or null when the player races this round normally. The shell shows
    /// the sit-out screen (an "INJURED — auto-simulating" / "SEASON OVER — recovering" banner) with a
    /// single Continue that calls <see cref="AutoSimulateRound"/>. Additive default: null.</summary>
    SitOutStatus? CurrentSitOut() => null;

    /// <summary>Auto-simulates the CURRENT round the injured player must sit out (§5): generates the AI
    /// field deterministically (the player is DNS — OPI-neutral, zero points), folds it, and heals one
    /// race of a minor suspension. AMS2 cannot spectate a single-player race, so an unavailable round is
    /// simulated rather than driven. Throws when the player is fit (enter the result manually), deceased,
    /// or the season is over. Additive default: throws, so fakes compile.</summary>
    void AutoSimulateRound() => throw new NotSupportedException(
        "This career session does not support auto-simulated rounds.");

    /// <summary>True when the FILE-level save &amp; reload surface is available — <c>Normal</c> mode ONLY.
    /// Off (no death to undo) and Hardcore (no saves, ever) both report false. When false, the slot
    /// list is empty and <see cref="SaveToSlot"/>/<see cref="RestoreSlot"/> throw. Additive default:
    /// false, so fakes compile.</summary>
    bool SavesEnabled => false;

    /// <summary>The career's manual + autosave snapshot slots, newest first (Normal only). Each is a
    /// complete, replay-verifiable career-file snapshot taken OUTSIDE the fold, so listing/saving/
    /// restoring never touches the re-simulation contract. Empty when saves are disabled. Additive
    /// default: empty, so fakes compile.</summary>
    IReadOnlyList<Companion.Data.SaveSlotInfo> SaveSlots() => [];

    /// <summary>Snapshots the working career into a NEW manual save slot with the given label and
    /// returns it. Throws when saves are disabled (Off/Hardcore). Additive default: throws, so a
    /// session without save support says so.</summary>
    Companion.Data.SaveSlotInfo SaveToSlot(string label) => throw new NotSupportedException(
        "This career session does not support saving.");

    /// <summary>Restores the career WHOLESALE to a snapshot (reverting every round since) — Normal
    /// only, allowed any time, including to un-do a death. THIS SESSION IS SPENT afterwards: its DB is
    /// closed, so the shell must reopen the career file to continue from the restored point (mirroring
    /// the era-transition reopen contract). Throws when saves are disabled or the slot is unknown.
    /// Additive default: throws, so fakes compile.</summary>
    void RestoreSlot(string slotId) => throw new NotSupportedException(
        "This career session does not support restoring saves.");

    /// <summary>Deletes a save slot (Normal only). A no-op when saves are disabled or the slot is
    /// unknown. Additive default: no-op, so fakes compile.</summary>
    void DeleteSlot(string slotId) { }

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

    // ---- Task 3.3 clickable round-detail (all additive, default so existing initializers are unchanged) ----

    /// <summary>False marks a non-championship event (it scores no points).</summary>
    public bool Championship { get; init; } = true;

    /// <summary>This round's resolved grid size (starters), or null when the pack pins no per-round grid.</summary>
    public int? GridSize { get; init; }

    /// <summary>The backmarkers the pack pinned OUT of this round's grid (SMGP's per-race DNQ field),
    /// fastest-first. Empty when the round runs the full field. Diffed from the PINNED starters (the player
    /// injection happens at resolve time), so it is deterministic + spoiler-free for the calendar.</summary>
    public IReadOnlyList<ScheduleDnqEntry> Dnq { get; init; } = [];

    /// <summary>The round's weather label(s) ("Clear / Light Cloud"), from the setup guide. Empty when none.</summary>
    public string WeatherLabel { get; init; } = "";

    /// <summary>The setup-guide note for the round (the briefing's setup line). Empty when none.</summary>
    public string SetupNote { get; init; } = "";

    /// <summary>The AI opponent count from the setup guide, or null.</summary>
    public int? Opponents { get; init; }

    /// <summary>Progress marker: this round is Done (a result applied), Next (the upcoming round), or a
    /// later Upcoming round — so the calendar can walk the season.</summary>
    public SeasonRoundStatus Status { get; init; } = SeasonRoundStatus.Upcoming;
}

/// <summary>One driver the pack pinned OUT of a round's grid (a DNQ), for the calendar's round detail.</summary>
public sealed record ScheduleDnqEntry(string Name, string TeamName, string? Number);

/// <summary>A calendar round's progress relative to the career: already raced, the next one up, or later.</summary>
public enum SeasonRoundStatus
{
    /// <summary>A result has been applied for this round.</summary>
    Done,

    /// <summary>The next round to race (the current round).</summary>
    Next,

    /// <summary>A later round, not yet reached.</summary>
    Upcoming,
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

/// <summary>The player's mortality/availability snapshot (character death &amp; injury §3.3), a plain value
/// the shell reads after a round to route to the injury / season-over / death screens.</summary>
public sealed record PlayerMortalityStatus
{
    public required Companion.Core.Career.MortalityMode Mode { get; init; }

    /// <summary>The character has died — terminal. In Hardcore the file is (or is about to be) deleted.</summary>
    public required bool Deceased { get; init; }

    /// <summary>Out for the rest of the season with a season-ending injury (returns next year).</summary>
    public required bool SeasonEndingInjury { get; init; }

    /// <summary>Races the driver must still sit out from a minor injury (0 = fit).</summary>
    public required int RaceSuspensionRemaining { get; init; }

    /// <summary>A Hardcore death has physically deleted the career file — the session is spent.</summary>
    public required bool CareerFileDeleted { get; init; }

    /// <summary>Fit to race normally (not injured, season-ended, or deceased).</summary>
    public bool IsFit => !Deceased && !SeasonEndingInjury && RaceSuspensionRemaining == 0;
}

/// <summary>The player's sit-out banner for an auto-simulated (injured) round (character death &amp;
/// injury §5) — a plain value the shell renders with a single Continue.</summary>
public sealed record SitOutStatus
{
    /// <summary>Races the driver still sits out from a minor injury (0 for a season-ending injury).</summary>
    public required int RaceSuspensionRemaining { get; init; }

    /// <summary>True for a season-ending injury (out until next year) vs a countable minor suspension.</summary>
    public required bool SeasonEnding { get; init; }

    /// <summary>The banner headline, e.g. "INJURED — auto-simulating round (2 remaining)" or
    /// "SEASON OVER — recovering".</summary>
    public required string Headline { get; init; }
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

    /// <summary>The player's per-round breakdown of THIS season (Task 3.3) — one line per applied round with
    /// the player's finish, the rival they named that round + the rival's finish, the leader after the round
    /// and the player's running points. Empty for a season with no applied round. Additive display-only.</summary>
    public IReadOnlyList<CareerSeasonRoundLine> RoundLines { get; init; } = [];
}

/// <summary>One applied round in a season's "my career" breakdown for the History screen: the player's own
/// result, the rival they named and how that duel went, and the championship picture after the round.
/// DISPLAY-ONLY — a pure projection over the stored results.</summary>
public sealed record CareerSeasonRoundLine
{
    public required int Round { get; init; }

    /// <summary>The round's venue label.</summary>
    public required string Venue { get; init; }

    /// <summary>The player's finishing position, or null when they did not finish / were not classified.</summary>
    public int? PlayerFinish { get; init; }

    /// <summary>The rival the player NAMED this round (that round's stored call), or null when none named.</summary>
    public string? RivalName { get; init; }

    /// <summary>That named rival's finishing position this round, or null.</summary>
    public int? RivalFinish { get; init; }

    /// <summary>The championship leader's name after this round.</summary>
    public string? ChampionAfter { get; init; }

    /// <summary>The player's cumulative championship points after this round.</summary>
    public double PlayerPointsAfter { get; init; }
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

    /// <summary>How hard the player's OWN accident ("a") DNF was — Light/Medium/Heavy (character death &amp;
    /// injury §3.1). The result screen reveals this picker only for the player's own accident DNF, default
    /// Medium. Stored on the raw envelope (v7); <see cref="CareerSessionService"/> threads it in ONLY when
    /// the player's DNF reason is accident, null otherwise. Nothing consumes it until Slice 3, so a round
    /// carrying it still folds byte-identically. Older producers omit it.</summary>
    public Companion.Core.Career.AccidentSeverity? PlayerAccidentSeverity { get; init; }

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

    /// <summary>The player's live SEASON standing — "SEASON  P3 · 18 PTS" (or "SEASON —" before any
    /// round). Replaces the old "D.P." points abbreviation with the player's real running stats.</summary>
    public required string SeasonLine { get; init; }

    /// <summary>The player's live CAREER record — "CAREER  2 WINS · 1 POLE · 5 TOP-5" (empty until they
    /// have something to show). They build this from zero; the AI carry their pre-history.</summary>
    public required string CareerLine { get; init; }

    /// <summary>The pit-crew advice line (the manual's own words).</summary>
    public required string AdviceLine { get; init; }

    /// <summary>Championships won in the mode so far (two = the replica is beaten).</summary>
    public required int Titles { get; init; }

    /// <summary>The current season's 1-based ordinal within the 17-season grand campaign (season 1 … 17).
    /// Surfaced as "SEASON n / 17". DISPLAY-ONLY.</summary>
    public required int SeasonOrdinal { get; init; }

    /// <summary>The grand campaign length (<see cref="Companion.Core.Smgp.SmgpRules.CampaignSeasons"/> = 17)
    /// — the denominator of "SEASON n / 17".</summary>
    public required int SeasonsTotal { get; init; }

    /// <summary>The Zeroforce game-over state — the panel shows it instead of a rival pick.</summary>
    public required bool CareerOver { get; init; }

    /// <summary>The title-defense challenger forced on the player this round, or null for a
    /// free pick. When set, the pick is locked to him.</summary>
    public string? ForcedChallengerDriverId { get; init; }

    /// <summary>Every AI driver on this round's grid, in grid order — any of them can be named.</summary>
    public required IReadOnlyList<SmgpRivalOption> Rivals { get; init; }
}

/// <summary>The SMGP Paddock lens (driver/team preview tab): the whole grid's drivers and teams as
/// display cards, built from the pack roster + the SMGP reference data (bios, predetermined stats,
/// team profiles). DISPLAY-ONLY — never a fold input.</summary>
public sealed record SmgpPaddockModel
{
    /// <summary>Every driver on the grid, most-storied first (team prestige, then career points).</summary>
    public required IReadOnlyList<SmgpDriverCard> Drivers { get; init; }

    /// <summary>Every team on the grid, highest prestige first.</summary>
    public required IReadOnlyList<SmgpTeamCard> Teams { get; init; }

    /// <summary>The SMGP sponsor board — fictional brands with stories/logos + the teams they back
    /// (the Paddock's Sponsors tab; seed of the future Tycoon mode). Empty when no sponsors are authored.</summary>
    public IReadOnlyList<SmgpSponsorCard> Sponsors { get; init; } = [];

    /// <summary>A rotating "paddock rumor" line for the Paddock (Task 4) — a seeded, DISPLAY-ONLY flavour
    /// line drawn from the dispatch corpus, stable on a re-open and rotating slowly across the career.
    /// Empty when no rumor pool is authored.</summary>
    public string PaddockRumor { get; init; } = "";
}

/// <summary>One sponsor's Paddock card: identity + industry/tier + brand colour + logo key + story + the
/// teams it backs (with names for the roster line). DISPLAY-ONLY — the seed of the future Tycoon mode.</summary>
public sealed record SmgpSponsorCard
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Industry { get; init; }
    /// <summary>Backing tier: title / major / minor / struggling.</summary>
    public required string Tier { get; init; }
    public required string Tagline { get; init; }
    public required IReadOnlyList<string> Story { get; init; }
    /// <summary>Brand colour "#RRGGBB" (accents the card).</summary>
    public required string BrandColorHex { get; init; }
    /// <summary>Logo art key — <c>smgp/sponsors/&lt;id-without-prefix&gt;.png</c> (absent-tolerant).</summary>
    public required string LogoKey { get; init; }
    public required string FoundedFlavor { get; init; }
    /// <summary>The team ids this sponsor backs.</summary>
    public required IReadOnlyList<string> TeamIds { get; init; }
    /// <summary>The backed teams' display names (for the "backs: …" roster line).</summary>
    public required IReadOnlyList<string> TeamNames { get; init; }
}

/// <summary>One driver's Paddock card: identity + team + drop-in art keys + bio + predetermined stats.</summary>
public sealed record SmgpDriverCard
{
    public required string DriverId { get; init; }
    public required string Name { get; init; }
    public required string TeamId { get; init; }
    public required string TeamName { get; init; }
    public required string? Number { get; init; }
    /// <summary>Portrait key — <c>portraits/&lt;driverId&gt;.jpg</c>.</summary>
    public required string PortraitKey { get; init; }
    /// <summary>Car preview key — <c>cars/&lt;driverId&gt;.png</c>.</summary>
    public required string CarKey { get; init; }
    /// <summary>Short ALL-CAPS arcade epithet, or empty when no bio is authored.</summary>
    public required string Epithet { get; init; }
    /// <summary>The ~3-paragraph biography (empty when unauthored).</summary>
    public required IReadOnlyList<string> Bio { get; init; }
    /// <summary>In-character quotes (empty when unauthored).</summary>
    public required IReadOnlyList<string> Quotes { get; init; }
    /// <summary>True for the player's own card — they build their record from zero (no pre-history).</summary>
    public required bool IsPlayer { get; init; }
    /// <summary>All-time career totals: for an AI driver, the predetermined baseline PLUS what they have
    /// accrued since the player arrived; for the player, purely what they have accrued (they start at
    /// zero). Null only when the mode has no stats data at all.</summary>
    public required SmgpCareerStats? Career { get; init; }
    /// <summary>This season's live tally (championship position + points + wins/poles/podiums/top-5s),
    /// or null before any round has been scored this season.</summary>
    public required SmgpSeasonStats? Season { get; init; }
    /// <summary>The driver's team prestige (5 = top house … 2 = the floor) — grouping/order.</summary>
    public required int Prestige { get; init; }

    // ---- Task 2 depth (all additive, default-empty so a card without them still renders) ----

    /// <summary>The PLAYER card's evolving story: an ordered list of career milestone beats (arrived,
    /// first win, a promotion, a title, a rivalry earned…) detected from the folded results + SMGP
    /// state (<see cref="Companion.Core.Smgp.SmgpCareerBeats"/>). Empty for AI drivers. DISPLAY-ONLY —
    /// grows with the career.</summary>
    public IReadOnlyList<Companion.Core.Smgp.SmgpCareerBeat> Timeline { get; init; } = [];

    /// <summary>A short live prose intro reflecting the player's standing RIGHT NOW (the one-line
    /// header above the timeline). Empty for AI drivers / before anything has happened.</summary>
    public string NarrativeIntro { get; init; } = "";

    /// <summary>For an AI driver: the player-vs-this-driver record across the whole career (races met,
    /// who finished ahead, best shared result) plus the live SMGP battle streak. Null on the player's
    /// own card and before they have met on track.</summary>
    public SmgpHeadToHead? HeadToHead { get; init; }

    /// <summary>This driver's best race finish per venue, with the player's best at the same venue for
    /// compare. Empty when no shared history. Ordered by venue name.</summary>
    public IReadOnlyList<SmgpTrackBest> PerTrackBest { get; init; } = [];

    /// <summary>Recent form: this driver's last few race finishes, oldest-first (null = a race they did
    /// not finish / were not classified). Empty before any race. A trend the GUI can sparkline.</summary>
    public IReadOnlyList<int?> FormRecent { get; init; } = [];
}

/// <summary>The player-vs-one-driver head-to-head across the whole career: races they both ran, who
/// finished ahead, the best result the player took when they shared a grid, and the live SMGP battle
/// streak (from <see cref="Companion.Core.Smgp.SmgpState.Tallies"/>). DISPLAY-ONLY.</summary>
public sealed record SmgpHeadToHead
{
    /// <summary>Races both were classified in (a fair ahead/behind comparison needs both finishing).</summary>
    public required int RacesMet { get; init; }

    /// <summary>Of <see cref="RacesMet"/>, how many the player finished ahead of this driver.</summary>
    public required int PlayerAhead { get; init; }

    /// <summary>Of <see cref="RacesMet"/>, how many this driver finished ahead of the player.</summary>
    public required int DriverAhead { get; init; }

    /// <summary>The player's best race finish in a race they both ran (null if never classified together).</summary>
    public int? PlayerBestTogether { get; init; }

    /// <summary>The venue of that best-shared race, e.g. "Monaco" (null when none).</summary>
    public string? BestTogetherVenue { get; init; }

    /// <summary>Current SMGP battle streak in the player's favour this season (consecutive wins over him).</summary>
    public required int PlayerStreak { get; init; }

    /// <summary>Current SMGP battle streak in this driver's favour this season.</summary>
    public required int DriverStreak { get; init; }
}

/// <summary>A driver's best finish at one venue, with the player's best at the same venue for compare.
/// DISPLAY-ONLY.</summary>
public sealed record SmgpTrackBest
{
    public required string Venue { get; init; }

    /// <summary>This driver's best race finish here across the career, or null (never classified here).</summary>
    public int? DriverBest { get; init; }

    /// <summary>The player's best race finish at the same venue, or null.</summary>
    public int? PlayerBest { get; init; }
}

/// <summary>A driver's all-time career totals — the predetermined baseline grown by live results
/// (the player's baseline is zero). DISPLAY-ONLY.</summary>
public sealed record SmgpCareerStats
{
    public required int Starts { get; init; }
    public required int Wins { get; init; }
    public required int Podiums { get; init; }
    public required int Poles { get; init; }
    public required int Top5s { get; init; }
    public required int Points { get; init; }
    public required int Titles { get; init; }
}

/// <summary>A driver's live tally for the CURRENT season, from the folded results. DISPLAY-ONLY.</summary>
public sealed record SmgpSeasonStats
{
    /// <summary>Championship position this season, or null before it computes.</summary>
    public required int? Position { get; init; }
    public required int Points { get; init; }
    public required int Wins { get; init; }
    public required int Poles { get; init; }
    public required int Podiums { get; init; }
    public required int Top5s { get; init; }
    public required int Starts { get; init; }
}

/// <summary>One team's Paddock card: identity + logo + motto/history/quotes + its roster.</summary>
public sealed record SmgpTeamCard
{
    public required string TeamId { get; init; }
    public required string Name { get; init; }
    public required string Motto { get; init; }
    /// <summary>Team logo/icon key — <c>smgp/logos/&lt;teamId&gt;.png</c>.</summary>
    public required string LogoKey { get; init; }
    public required IReadOnlyList<string> History { get; init; }
    public required IReadOnlyList<string> Quotes { get; init; }
    /// <summary>The team's drivers, by name (for the roster line).</summary>
    public required IReadOnlyList<string> DriverNames { get; init; }
    public required int Prestige { get; init; }

    // ---- Task 2 depth (all additive, default so an un-enriched card still renders) ----

    /// <summary>The ladder tier label — "Level A" (top house) … "Level D" (the floor), from the team's
    /// prestige. Empty when unknown.</summary>
    public string Tier { get; init; } = "";

    /// <summary>The team's accent colour "#RRGGBB" (<see cref="Companion.ViewModels.Shell.TeamPalette"/>).</summary>
    public string PaletteHex { get; init; } = "";

    /// <summary>The live roster: each driver with their this-season line + a career one-liner. Empty for
    /// a team with no seated drivers this season.</summary>
    public IReadOnlyList<SmgpTeamRosterLine> Roster { get; init; } = [];

    /// <summary>The sponsors backing this team (cross-referenced from the paddock's sponsor board), so the
    /// GUI can link team ↔ sponsor. Empty when none authored.</summary>
    public IReadOnlyList<SmgpTeamSponsorRef> Sponsors { get; init; } = [];
}

/// <summary>One line of a team's live roster: a seated driver, their this-season standing, and a career
/// one-liner. DISPLAY-ONLY.</summary>
public sealed record SmgpTeamRosterLine
{
    public required string DriverId { get; init; }
    public required string Name { get; init; }
    public required bool IsPlayer { get; init; }

    /// <summary>This season's line, e.g. "P3 · 18 PTS", or "—" before any round is scored.</summary>
    public required string SeasonLine { get; init; }

    /// <summary>A career one-liner, e.g. "12 WINS · 3 TITLES", or empty when nothing to show.</summary>
    public required string CareerLine { get; init; }
}

/// <summary>A sponsor reference on a team card — the minimal identity + brand colour the GUI needs to
/// show a chip and link across to the sponsor board. DISPLAY-ONLY.</summary>
public sealed record SmgpTeamSponsorRef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Tier { get; init; }
    public required string BrandColorHex { get; init; }
}

/// <summary>The TYCOON TEAM MODE read-only DATA SPINE (Task 5): the player's team dashboard plus the whole
/// grid of teams ranked as the competitive landscape, and a flavour "team of the season" seed. A pure
/// DISPLAY-ONLY projection (no fold mechanics yet → replay-safe) — the read foundation the reserved top-header
/// team mode + the future economy build on.</summary>
public sealed record SmgpTeamDashboard
{
    /// <summary>The PLAYER's own team, fully detailed (also present in <see cref="Teams"/>, flagged).</summary>
    public required SmgpTeamDashboardEntry PlayerTeam { get; init; }

    /// <summary>EVERY team, ranked by the derived constructors' standing (then prestige) — the tycoon's
    /// competitive landscape / "grid of teams". The player's team carries <see cref="SmgpTeamDashboardEntry.IsPlayerTeam"/>.</summary>
    public required IReadOnlyList<SmgpTeamDashboardEntry> Teams { get; init; }

    /// <summary>The rival teams — <see cref="Teams"/> without the player's — for a "the field" table. Ranked.</summary>
    public IReadOnlyList<SmgpTeamDashboardEntry> RivalTeams => Teams.Where(t => !t.IsPlayerTeam).ToList();

    /// <summary>A flavour "team of the season" seed (the biggest over-achiever vs its budget, else the
    /// constructors' leader) — clearly labelled flavour, NO real economy yet. Null before any round scores.</summary>
    public SmgpTeamOfSeasonFlavour? TeamOfSeason { get; init; }
}

/// <summary>One team on the tycoon dashboard: identity + ladder tier/palette + the live roster + the sponsors
/// that back it + the SMGP-world history + a DERIVED constructors' standing (the team's drivers' points
/// summed — SMGP is driver-focused, so this is a display read, not an official constructors' title) + a
/// flavour budget-tier label. DISPLAY-ONLY.</summary>
public sealed record SmgpTeamDashboardEntry
{
    public required string TeamId { get; init; }
    public required string Name { get; init; }
    public required bool IsPlayerTeam { get; init; }
    public required int Prestige { get; init; }
    /// <summary>"Level A" (top house) … "Level D" (the floor).</summary>
    public required string Tier { get; init; }
    /// <summary>Team accent colour "#RRGGBB".</summary>
    public required string PaletteHex { get; init; }
    public required string Motto { get; init; }
    /// <summary>Team logo key — <c>smgp/logos/&lt;teamId&gt;.png</c>.</summary>
    public required string LogoKey { get; init; }
    /// <summary>The SMGP-world history — a few paragraphs. Empty when unauthored.</summary>
    public required IReadOnlyList<string> History { get; init; }
    /// <summary>The live roster (each seated driver's season + career line), reusing the paddock's lines.</summary>
    public required IReadOnlyList<SmgpTeamRosterLine> Roster { get; init; }
    /// <summary>The sponsors backing the team (brand colour, tier), from the sponsor board.</summary>
    public required IReadOnlyList<SmgpTeamSponsorRef> Sponsors { get; init; }
    /// <summary>The team's DERIVED constructors' position (its drivers' counted points summed, ranked), or
    /// null before any round is scored.</summary>
    public required int? ChampionshipPosition { get; init; }
    /// <summary>The team's derived constructors' points this season (its drivers' counted points summed).</summary>
    public required int ChampionshipPoints { get; init; }
    /// <summary>A FLAVOUR budget-tier label derived from prestige (Blue-chip … Shoestring) — the seed of the
    /// future economy, NOT a real budget number.</summary>
    public required string BudgetTier { get; init; }
}

/// <summary>The flavour "team of the season" seed (Task 5): the grid's biggest over-achiever relative to its
/// budget (else the constructors' leader). DISPLAY-ONLY, derived from prestige + results — no economy model
/// yet, and clearly labelled as flavour in <see cref="Note"/>.</summary>
public sealed record SmgpTeamOfSeasonFlavour
{
    public required string TeamId { get; init; }
    public required string Name { get; init; }
    public required string PaletteHex { get; init; }
    /// <summary>The arcade banner headline ("OVERACHIEVER OF THE SEASON" / "TEAM OF THE SEASON").</summary>
    public required string Headline { get; init; }
    /// <summary>A flavour sentence, explicitly noting there is no economy model yet.</summary>
    public required string Note { get; init; }
}

/// <summary>Whether the promotion screen is a climb (offer to accept/decline) or a forced drop.</summary>
public enum SmgpPromotionKind
{
    /// <summary>A two-wins offer to move UP into the rival's car — the player accepts or declines.</summary>
    PromotionOffer,

    /// <summary>A forced move DOWN a tier (a two-losses forfeit or a lost title defense) — already
    /// applied; the screen only acknowledges it (no decline).</summary>
    Demotion,
}

/// <summary>The full-immersion promotion / demotion screen's data (3c-3): the new team's photo,
/// name, motto, ~5-paragraph history and quotes, plus the team-coloured player image and the car
/// preview. Built display-only from the folded state + the <see cref="Companion.Core.Smgp.SmgpTeamProfiles"/>
/// catalog (3c-1) — never a fold input. A promotion is accept/decline; a demotion only acknowledges.</summary>
public sealed record SmgpPromotionModel
{
    public required SmgpPromotionKind Kind { get; init; }

    /// <summary>The arcade banner headline — "AN OFFER FROM MADONNA" / "RELEGATED TO ZEROFORCE".</summary>
    public required string Headline { get; init; }

    /// <summary>The new team's display name.</summary>
    public required string TeamName { get; init; }

    /// <summary>The VERY LARGE team photo key — <c>data/ams2/smgp/teams/&lt;key&gt;.jpg</c> (the team
    /// id without its "team." prefix). Absent-tolerant: the view hides the photo until art is dropped.</summary>
    public required string TeamPhotoKey { get; init; }

    /// <summary>The team-coloured player image key — <c>player.&lt;team&gt;</c>.</summary>
    public required string PlayerImageKey { get; init; }

    /// <summary>The car preview key (<c>cars/&lt;driverId&gt;.png</c>) for the new car, or null.</summary>
    public string? CarKey { get; init; }

    /// <summary>The team's one-line motto, or null when unauthored.</summary>
    public string? Motto { get; init; }

    /// <summary>The team's SMGP-world history — a few paragraphs. Empty when unauthored.</summary>
    public IReadOnlyList<string> History { get; init; } = [];

    /// <summary>A few in-character team quotes. Empty when unauthored.</summary>
    public IReadOnlyList<string> Quotes { get; init; } = [];

    /// <summary>The rival the player beat twice to earn the offer (promotion only), or null.</summary>
    public string? RivalName { get; init; }

    /// <summary>True for a promotion offer (the Decline button shows); false for a forced demotion.</summary>
    public bool CanDecline => Kind == SmgpPromotionKind.PromotionOffer;

    /// <summary>The accept/acknowledge button label.</summary>
    public string AcceptLabel => Kind == SmgpPromotionKind.PromotionOffer ? "Take the seat" : "Onwards";
}

/// <summary>The locked 17-season campaign FINALE screen's data (Mike's "final final screen with a
/// special image that has its own name, special.jpg ... no one can access it until you beat all 17").
/// A pure DISPLAY-ONLY projection over folded state (season count + <see cref="Companion.Core.Smgp.SmgpState.Titles"/>
/// + <see cref="Companion.Core.Smgp.SmgpState.CareerOver"/>) — never a fold input, never journaled.
/// The secret hero image is loaded ONLY on this screen and ONLY when the campaign is beaten: the
/// <see cref="HeroImageKey"/> is emitted solely by <see cref="ICareerSession.SmgpFinale"/> when the
/// unlock predicate holds, so no other screen ever binds it and the art stays sealed until earned.</summary>
public sealed record SmgpFinaleModel
{
    /// <summary>The triumphant arcade banner headline — the survivor vs. flawless-emperor register.</summary>
    public required string Headline { get; init; }

    /// <summary>The sub-headline / dedication line under the hero.</summary>
    public required string Subhead { get; init; }

    /// <summary>True for the FLAWLESS run (champion in all 17) — the screen reveals <c>ultimate.jpg</c>
    /// instead of <c>special.jpg</c>.</summary>
    public required bool IsFlawless { get; init; }

    /// <summary>The SECRET hero image key — <c>"special"</c> (completed all 17) or <c>"ultimate"</c>
    /// (champion in all 17). Loaded from <c>data/ams2/smgp/finale/&lt;key&gt;.jpg</c> and emitted ONLY
    /// when unlocked, so the art is unreachable anywhere else in the app. Absent-tolerant: a missing
    /// file simply hides the hero — the UNLOCK is the achievement, the image is the payoff.</summary>
    public required string HeroImageKey { get; init; }

    /// <summary>The campaign record lines (seasons conquered, titles won) shown beside the hero.</summary>
    public IReadOnlyList<string> Record { get; init; } = [];
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

    /// <summary>The rival's arcade car-spec card (machine/engine/power + ENG-TM-SUS-TIRE-BRA bars), or
    /// null when no spec is authored for the car (the card then collapses). Display-only.</summary>
    public CarSpecCardViewModel? CarSpec { get; init; }

    /// <summary>The rival's deadpan one-liner (the game's own vocabulary).</summary>
    public required string Quote { get; init; }

    /// <summary>Beat them once more (without losing) and "you may get an offer to join their
    /// team!" — the panel telegraphs it and asks for the standing answer.</summary>
    public required bool OfferOnWin { get; init; }

    /// <summary>Lose to them once more and they are offered YOUR seat.</summary>
    public required bool ForfeitOnLoss { get; init; }

    /// <summary>The rival's gendered pronoun set for the naming copy (Mika is female → she/her). Defaults to
    /// he/him for every unmarked driver, so existing copy is unchanged for the rest of the grid.</summary>
    public Companion.Core.Smgp.SmgpPronouns Pronouns { get; init; } = Companion.Core.Smgp.SmgpPronouns.Default;

    /// <summary>The rival's ladder CLASS letter ("A".."D") — shown (coloured) in the picker dropdown so you
    /// can see who is above/below you at a glance. Empty when unknown.</summary>
    public string Tier { get; init; } = "";

    /// <summary>The dropdown chip label, "CLASS B".</summary>
    public string TierLabel { get; init; } = "";

    /// <summary>The CLASS chip's accent colour "#RRGGBB" (A gold … D slate).</summary>
    public string TierColorHex { get; init; } = "";

    /// <summary>The player-vs-this-rival head-to-head (races met, who finished ahead, best shared, the live
    /// streak) for the deeper dossier (Task 3.2). Null before they have met on track.</summary>
    public SmgpHeadToHead? HeadToHead { get; init; }
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
