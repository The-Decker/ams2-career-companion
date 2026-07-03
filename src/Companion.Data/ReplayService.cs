using System.Collections.ObjectModel;
using System.Text.Json;
using Companion.Core.Career;
using Companion.Core.Determinism;
using Companion.Core.Grid;
using Companion.Core.Json;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Microsoft.Data.Sqlite;

namespace Companion.Data;

/// <summary>
/// The sim inputs that live OUTSIDE the career file: rules data (aging curves, archetypes,
/// headline bank — app-shipped JSON) and the career-setup facts the wizard fixed (player
/// identity, canon retirements, free-agent pool). Replay must receive exactly what the
/// original run received — same inputs + same master seed = byte-identical journal.
/// </summary>
public sealed record ReplaySimInputs
{
    public required AgingCurveSet AgingCurves { get; init; }

    public required TeamArchetypeCatalog Archetypes { get; init; }

    public HeadlineBank? Headlines { get; init; }

    /// <summary>The driver id the player scores under in raw results.</summary>
    public required string PlayerDriverId { get; init; }

    /// <summary>The player's age in the career's FIRST season; later seasons offset by
    /// year distance (the live path and replay share this rule by construction).</summary>
    public required int PlayerAge { get; init; }

    public IReadOnlyDictionary<string, int> CanonRetirements { get; init; } =
        ReadOnlyDictionary<string, int>.Empty;

    public IReadOnlyDictionary<string, string> TeamArchetypeOverrides { get; init; } =
        ReadOnlyDictionary<string, string>.Empty;

    public IReadOnlyList<SeatCandidate> FreeAgents { get; init; } = [];

    public double? PlayerSalaryAskBu { get; init; }

    public string? PlayerName { get; init; }
}

public sealed record ReplayReport
{
    /// <summary>True when every regenerated journal row matched the stored journal
    /// (phase, entity, deltaJson, cause, round — seq and utc excluded by contract) AND the
    /// stored player choices (accepted offers, follow-on season start states) were
    /// consistent with the regenerated world.</summary>
    public required bool Identical { get; init; }

    public ReplayDivergence? FirstDivergence { get; init; }

    /// <summary>Rows compared up to and including the divergence (all rows when identical).</summary>
    public required int ComparedRows { get; init; }
}

public sealed record ReplayDivergence
{
    public required long SeasonId { get; init; }

    /// <summary>0-based position in the season's sim-row sequence (provenance rows excluded).</summary>
    public required int Index { get; init; }

    /// <summary>The stored journal row's seq; null when the stored journal ran out of rows
    /// or the divergence is not a journal-row mismatch.</summary>
    public long? StoredSeq { get; init; }

    /// <summary>Which check diverged first: phase, entity, deltaJson, cause, round,
    /// extra-stored-row / missing-stored-row on a length mismatch, accepted-offer when a
    /// stored accepted offer is absent from the regenerated set, or start-state when a
    /// follow-on season's stored start rows differ from the rollover re-derivation.</summary>
    public required string Reason { get; init; }

    public string? StoredDeltaJson { get; init; }

    public string? RegeneratedDeltaJson { get; init; }
}

/// <summary>What one round's fold produced (journal events aside): the state the briefing
/// and home header consume, plus the confirm screen's headline.</summary>
public sealed record RoundFoldResult
{
    /// <summary>The post-round folded player state, as persisted to round_player_state.</summary>
    public required RoundPlayerState State { get; init; }

    /// <summary>The journal events the fold appended: round standings, then — when the
    /// player raced — race.result, player.opi, player.reputation, player.paceAnchor, and
    /// news.headline when a bank is present.</summary>
    public required IReadOnlyList<JournalEvent> Events { get; init; }

    /// <summary>True when the player's seat was on this grid and the player appears in the
    /// result; false folds the round with the player state carried over unchanged.</summary>
    public required bool PlayerRaced { get; init; }

    public string? Headline { get; init; }

    public int? ExpectedFinish { get; init; }
}

/// <summary>
/// Deterministic re-simulation (docs/dev/career-sim.md, Replay contract): sim state =
/// fold(journal); re-simulate = wipe derived state, replay raw results through the same
/// code + seed. <see cref="FoldRound"/> (per-round standings + player update) and
/// <see cref="RunSeasonEnd"/> are the ONE fold implementation — the live app path calls
/// exactly these, and <see cref="Resimulate"/> re-executes the identical code, so replay
/// regenerates the stored journal by construction and any divergence means the file was
/// tampered with, the payloads changed, or the engine changed. Divergence is report-only:
/// <see cref="Resimulate"/> runs in one transaction and commits nothing unless the whole
/// career replays byte-identically.
/// </summary>
public static class ReplayService
{
    /// <summary>The default Opponent Skill slider assumed when a legacy payload carries no
    /// sliderUsed and no recommendation exists yet (round 1, uncalibrated anchor).</summary>
    public const double NeutralSlider = 100.0;

    /// <summary>The derived journal rows for the latest of <paramref name="roundsSoFar"/>:
    /// the championship standings as they stand after that round (drivers, then constructors
    /// when the season has that championship) — same delta shape as the season-end
    /// championship rows. <paramref name="roundsSoFar"/> must contain CHAMPIONSHIP-round
    /// results only (engine round numbers = championship ordinals), and scoring resolves
    /// over the championship round count via <see cref="ChampionshipCalendar"/> — exactly
    /// the resolution the app's session service scores the standings screen with, so the
    /// journal and the screen can never disagree.</summary>
    public static IReadOnlyList<JournalEvent> RoundStandingsEvents(
        SeasonPack pack, IReadOnlyList<RoundResult> roundsSoFar)
    {
        var scoring = ChampionshipCalendar.ResolveScoring(pack);
        var snapshot = StandingsEngine.ComputeSeason(scoring, roundsSoFar).Final;

        var events = new List<JournalEvent>();
        foreach (var driver in snapshot.Drivers)
        {
            events.Add(new JournalEvent
            {
                Phase = DataJournalPhases.RoundStandings,
                Entity = driver.DriverId,
                DeltaJson = DataJson.Serialize(new
                {
                    position = driver.Position,
                    points = driver.CountedPoints.ToString(),
                }),
                Cause = "standings-after-round",
            });
        }
        if (snapshot.Constructors is { } constructors)
        {
            foreach (var team in constructors)
            {
                events.Add(new JournalEvent
                {
                    Phase = DataJournalPhases.RoundStandings,
                    Entity = team.ConstructorId,
                    DeltaJson = DataJson.Serialize(new
                    {
                        position = team.Position,
                        points = team.CountedPoints.ToString(),
                    }),
                    Cause = "standings-after-round",
                });
            }
        }
        return events;
    }

    /// <summary>
    /// THE per-round fold (docs/dev/m5-fix-integration.md, "unified fold" step 2): recomputes
    /// the round's standings events AND runs the player round update with state folded from
    /// the previous round, journals everything under the round, and persists the post-round
    /// player state as the round's round_player_state row — all in one transaction. Grid,
    /// teammate finish, and expected finish are re-derived from pack + seed + round; the
    /// stored envelope contributes only what is unre-derivable (sliderUsed, DNF cause).
    /// Refuses to fold a round twice — corrections re-import and <see cref="Resimulate"/>.
    /// </summary>
    public static RoundFoldResult FoldRound(
        CareerDatabase db,
        long seasonId,
        SeasonPack pack,
        ulong masterSeed,
        ReplaySimInputs inputs,
        int round,
        string utc,
        SqliteTransaction? transaction = null)
    {
        using var scope = TransactionScope.Enter(db, transaction);
        var tx = scope.Transaction;

        if (StateStore.ReadRoundPlayerState(db, seasonId, round, tx) is not null)
            throw new InvalidOperationException(
                $"Round {round} of season {seasonId} is already folded — corrected results " +
                "go through a re-import plus Resimulate, never a second fold.");

        var upTo = ResultStore.ReadSeasonResults(db, seasonId, tx)
            .Where(r => r.Round <= round)
            .ToList();
        if (upTo.Count == 0 || upTo[^1].Round != round)
            throw new InvalidOperationException(
                $"Season {seasonId} has no stored raw result for round {round} — " +
                "ImportAndFoldRound stores and folds atomically.");

        var previous = PreviousRoundState(db, seasonId, upTo, tx);
        var startTeams = StateStore.ReadTeamStates(db, seasonId, StateStore.StageStart, tx);
        var outcome = ComputeRoundFold(
            pack, masterSeed, inputs, startTeams,
            ChampionshipResults(pack, upTo),
            upTo[^1].ToEnvelope(), round, previous);

        JournalStore.AppendMany(db, seasonId, round, outcome.Events, utc, tx);
        StateStore.InsertRoundPlayerState(db, seasonId, round, outcome.State, tx);

        scope.Complete();
        return new RoundFoldResult
        {
            State = outcome.State,
            Events = outcome.Events,
            PlayerRaced = outcome.PlayerRaced,
            Headline = outcome.Headline,
            ExpectedFinish = outcome.ExpectedFinish,
        };
    }

    /// <summary>
    /// The live path's single entry point for a NEW round result: stores the envelope as the
    /// round's raw payload and runs <see cref="FoldRound"/>, atomically — the raw result and
    /// its fold cannot come apart, so the live path cannot bypass the fold. Re-importing an
    /// already-folded round throws (and rolls back the import): corrections use
    /// <see cref="ResultStore.Append"/> plus <see cref="Resimulate"/>.
    /// </summary>
    public static RoundFoldResult ImportAndFoldRound(
        CareerDatabase db,
        long seasonId,
        SeasonPack pack,
        ulong masterSeed,
        ReplaySimInputs inputs,
        int round,
        RoundResultEnvelope envelope,
        string utc,
        string source = "manual")
    {
        using var transaction = db.Connection.BeginTransaction();
        ResultStore.Append(
            db, seasonId, round, DataJson.Serialize(envelope), utc, source, transaction);
        var fold = FoldRound(db, seasonId, pack, masterSeed, inputs, round, utc, transaction);
        transaction.Commit();
        return fold;
    }

    /// <summary>
    /// Runs the season-end pipeline against the season's stored start states and raw results,
    /// consuming the FINAL ROUND'S FOLDED player state (per-round rep/OPI accrual drives the
    /// offers), journals its events (round = NULL), persists the derived 'end' states +
    /// offers, and marks the season complete — atomically. The live app path calls this once
    /// per season; replay re-executes the identical code.
    /// </summary>
    public static SeasonEndResult RunSeasonEnd(
        CareerDatabase db,
        long seasonId,
        SeasonPack pack,
        ulong masterSeed,
        ReplaySimInputs inputs,
        string utc)
    {
        var seasons = CareerStore.ReadSeasons(db);
        var season = seasons.FirstOrDefault(s => s.Id == seasonId)
            ?? throw new InvalidOperationException($"Season {seasonId} does not exist in this career.");
        if (string.Equals(season.Status, SeasonStatus.Complete, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Season {seasonId} is already complete — re-simulate instead of re-running season end.");

        var stored = ResultStore.ReadSeasonResults(db, seasonId);
        var rounds = ChampionshipResults(pack, stored);

        var player = StartPlayerState(db, seasonId, transaction: null);
        if (stored.Count > 0)
        {
            player = (StateStore.ReadRoundPlayerState(db, seasonId, stored[^1].Round)
                ?? throw new InvalidOperationException(
                    $"Season {seasonId} has imported rounds without folded player state — every " +
                    "round must be applied through FoldRound/ImportAndFoldRound (re-simulate to " +
                    "rebuild round_player_state on refolded careers).")).Player;
        }

        int playerAge = inputs.PlayerAge + (season.Year - seasons[0].Year);
        var context = BuildSeasonEndContext(
            season.Year, playerAge, pack, masterSeed, inputs, rounds, player,
            StateStore.ReadDriverStates(db, seasonId, StateStore.StageStart),
            StateStore.ReadTeamStates(db, seasonId, StateStore.StageStart));
        var result = SeasonEndPipeline.Run(context);

        using var transaction = db.Connection.BeginTransaction();
        JournalStore.AppendMany(db, seasonId, round: null, result.Events, utc, transaction);
        PersistDerived(db, seasonId, result, transaction);
        CareerStore.CompleteSeason(db, seasonId, transaction);
        transaction.Commit();
        return result;
    }

    /// <summary>
    /// Re-simulates the whole career from its raw results: verifies the supplied pack against
    /// the pinned (hash-checked) bytes, then — inside ONE transaction — wipes derived state
    /// (NOT raw results, NOT pinned packs, NOT the stored journal, NOT 'start' states — those
    /// are inputs), refolds every season round by round, re-runs season ends, re-derives
    /// follow-on season start states through <see cref="SeasonRollover"/> and compares them
    /// against the stored rows, and byte-compares the regenerated journal sequence against
    /// the stored one (seq/utc excluded; provenance rows excluded). COMMITS only when the
    /// career is byte-identical; ANY divergence rolls the transaction back, so a divergence
    /// report never costs data. Accepted-offer flags — a player choice, not derived state —
    /// are re-applied when identical; an accepted team missing from the regenerated offer
    /// set is a divergence, never a silent drop.
    /// </summary>
    public static ReplayReport Resimulate(
        CareerDatabase db, SeasonPack pack, ulong masterSeed, ReplaySimInputs inputs)
    {
        var seasons = CareerStore.ReadSeasons(db);
        if (seasons.Count == 0)
            return new ReplayReport { Identical = true, ComparedRows = 0 };

        VerifyPackIsThePinnedOne(db, pack, seasons);

        // Pre-read every input and stored artifact OUTSIDE the write transaction: the
        // regeneration below is pure computation over these plus the pack and seed.
        var acceptedOffers = ReadAcceptedOffers(db);
        var preread = seasons
            .Select(season => new StoredSeason(
                season,
                ResultStore.ReadSeasonResults(db, season.Id),
                JournalStore.ReadSeason(db, season.Id)
                    .Where(row => !DataJournalPhases.IsProvenance(row.Phase))
                    .ToList(),
                StateStore.ReadPlayerState(db, season.Id, StateStore.StageStart),
                StateStore.ReadDriverStates(db, season.Id, StateStore.StageStart),
                StateStore.ReadTeamStates(db, season.Id, StateStore.StageStart)))
            .ToList();

        int comparedRows = 0;
        ReplayDivergence? divergence = null;
        int firstYear = seasons[0].Year;

        using var transaction = db.Connection.BeginTransaction();
        StateStore.WipeDerived(db, transaction);

        SeasonEndResult? previousEnd = null;
        StoredSeason? previousSeason = null;

        foreach (var current in preread)
        {
            var season = current.Season;

            // ---- follow-on seasons: start states must re-derive via the rollover ----
            if (previousSeason is not null)
            {
                divergence = VerifyRolloverStartStates(
                    previousSeason.Season, previousEnd, acceptedOffers, current);
                if (divergence is not null)
                    break;
            }

            // ---- fold the rounds, in round order ----
            var regenerated = new List<(int? Round, JournalEvent Event)>();
            var previousState = new RoundPlayerState
            {
                Player = current.Results.Count > 0 || IsComplete(season)
                    ? current.StartPlayer ?? throw MissingStartState(season.Id)
                    : current.StartPlayer ?? new PlayerCareerState(),
                RecommendedSlider = 0,
            };
            // The fold and season end score CHAMPIONSHIP results only, exactly like the
            // live path (non-championship rounds are folded — player update, carried state —
            // but their classifications never enter the standings engine).
            var soFar = new List<RoundResult>();
            foreach (var stored in current.Results)
            {
                if (ChampionshipCalendar.IsChampionshipRound(pack, stored.Round))
                    soFar.Add(stored.ToRoundResult());
                var outcome = ComputeRoundFold(
                    pack, masterSeed, inputs, current.StartTeams, soFar,
                    stored.ToEnvelope(), stored.Round, previousState);
                foreach (var journalEvent in outcome.Events)
                    regenerated.Add((stored.Round, journalEvent));
                StateStore.InsertRoundPlayerState(db, season.Id, stored.Round, outcome.State, transaction);
                previousState = outcome.State;
            }

            // ---- season end, when it originally ran ----
            SeasonEndResult? seasonEnd = null;
            if (IsComplete(season))
            {
                int playerAge = inputs.PlayerAge + (season.Year - firstYear);
                var context = BuildSeasonEndContext(
                    season.Year, playerAge, pack, masterSeed, inputs, soFar,
                    previousState.Player, current.StartDrivers, current.StartTeams);
                seasonEnd = SeasonEndPipeline.Run(context);
                foreach (var journalEvent in seasonEnd.Events)
                    regenerated.Add((null, journalEvent));
                PersistDerived(db, season.Id, seasonEnd, transaction);
            }

            // ---- byte-compare this season's regenerated sequence against the journal ----
            divergence = CompareSeason(season.Id, current.StoredJournal, regenerated, ref comparedRows);
            if (divergence is not null)
                break;

            // ---- re-apply the player's accepted offer; a missing team is a divergence ----
            foreach (var (offerSeasonId, teamId) in acceptedOffers)
            {
                if (offerSeasonId != season.Id)
                    continue;
                if (seasonEnd is null ||
                    !seasonEnd.Offers.Any(o => string.Equals(o.TeamId, teamId, StringComparison.Ordinal)))
                {
                    divergence = new ReplayDivergence
                    {
                        SeasonId = season.Id,
                        Index = regenerated.Count,
                        Reason = "accepted-offer",
                        StoredDeltaJson = teamId,
                        RegeneratedDeltaJson = seasonEnd is null
                            ? null
                            : string.Join(",", seasonEnd.Offers.Select(o => o.TeamId)),
                    };
                    break;
                }
                StateStore.SetOfferAccepted(db, season.Id, teamId, transaction);
            }
            if (divergence is not null)
                break;

            previousEnd = seasonEnd;
            previousSeason = current;
        }

        if (divergence is null)
        {
            transaction.Commit();
            return new ReplayReport { Identical = true, ComparedRows = comparedRows };
        }

        transaction.Rollback();
        return new ReplayReport
        {
            Identical = false,
            FirstDivergence = divergence,
            ComparedRows = comparedRows,
        };
    }

    // ---------- the round fold computation (one code path for live + replay) ----------

    private sealed record RoundFoldOutcome(
        IReadOnlyList<JournalEvent> Events,
        RoundPlayerState State,
        bool PlayerRaced,
        string? Headline,
        int? ExpectedFinish);

    /// <summary>Pure fold of one round: standings events, then — when the player's seat is
    /// on the grid and the player appears in the result — the RoundUpdate events. Grid,
    /// teammate finish, expected finish, and the points cutoff are re-derived from
    /// pack + seed + round; only sliderUsed and the DNF cause come from the envelope
    /// (defaults: last recommendation / no blame).</summary>
    private static RoundFoldOutcome ComputeRoundFold(
        SeasonPack pack,
        ulong masterSeed,
        ReplaySimInputs inputs,
        IReadOnlyList<TeamCareerState> startTeams,
        IReadOnlyList<RoundResult> roundsSoFar,
        RoundResultEnvelope envelope,
        int round,
        RoundPlayerState previous)
    {
        var events = new List<JournalEvent>(RoundStandingsEvents(pack, roundsSoFar));

        var grid = ResolvePlayerGrid(pack, round, previous.Player.LiveryName);
        var playerOutcome = PlayerOutcome(envelope, inputs.PlayerDriverId);
        if (grid is null || playerOutcome is null)
            return new RoundFoldOutcome(events, previous, PlayerRaced: false, null, null);

        var (finish, dnf) = playerOutcome.Value;
        int playerIndex = SeatStrengthModel.PlayerSeatIndex(grid);
        var playerSeat = grid.Seats[playerIndex];

        var teammateIds = grid.Seats
            .Where(s => !s.IsPlayer && string.Equals(s.TeamId, playerSeat.TeamId, StringComparison.Ordinal))
            .Select(s => s.DriverId)
            .ToHashSet(StringComparer.Ordinal);

        var update = RoundUpdate.Apply(new RoundUpdateContext
        {
            Grid = grid,
            Player = previous.Player,
            PlayerTeamTier = startTeams
                .FirstOrDefault(t => string.Equals(t.TeamId, playerSeat.TeamId, StringComparison.Ordinal))
                ?.Tier ?? 3,
            PlayerFinish = finish,
            PlayerDnf = dnf,
            HasTeammate = teammateIds.Count > 0,
            TeammateFinish = BestTeammateFinish(envelope.Result, teammateIds),
            SliderUsed = envelope.SliderUsed ?? DefaultSlider(previous, grid),
            PointsPositions = PointsPositions(pack, envelope.Result),
            Streams = new StreamFactory(masterSeed),
            Headlines = inputs.Headlines,
            PlayerName = inputs.PlayerName,
        });
        events.AddRange(update.Events);

        return new RoundFoldOutcome(
            events,
            new RoundPlayerState { Player = update.Player, RecommendedSlider = update.RecommendedSlider },
            PlayerRaced: true,
            update.Headline,
            update.ExpectedFinish);
    }

    /// <summary>The round's grid with the player's seat marked, or null when the player has
    /// no seat this round (no livery configured, or the entry's rounds range excludes it) —
    /// then the round folds with the player state carried over unchanged.</summary>
    private static GridPlan? ResolvePlayerGrid(SeasonPack pack, int round, string? liveryName)
    {
        if (liveryName is null)
            return null;
        var plan = RoundGridResolver.Resolve(pack, round);
        if (!plan.Seats.Any(s => string.Equals(s.Ams2LiveryName, liveryName, StringComparison.Ordinal)))
            return null;
        return RoundGridResolver.Resolve(pack, round, new PlayerSeat { Ams2LiveryName = liveryName });
    }

    /// <summary>The player's outcome in the round's race classification, or null when the
    /// player does not appear in it. Envelope defaults per the contract: a retired player
    /// without a stored cause is mechanical (no blame), a disqualified one driver error.</summary>
    private static (int? Finish, DnfCause? Dnf)? PlayerOutcome(
        RoundResultEnvelope envelope, string playerDriverId)
    {
        var race = envelope.Result.Sessions.FirstOrDefault(s => s.Kind == SessionKind.Race);
        var entry = race?.Entries.FirstOrDefault(e =>
            string.Equals(e.DriverId, playerDriverId, StringComparison.Ordinal));
        return entry switch
        {
            null => null,
            { Status: FinishStatus.Classified, Position: { } position } => (position, null),
            { Status: FinishStatus.Retired } => (null, envelope.PlayerDnfCause ?? DnfCause.Mechanical),
            { Status: FinishStatus.Disqualified } => (null, envelope.PlayerDnfCause ?? DnfCause.DriverError),
            { Status: FinishStatus.NotClassified or FinishStatus.DidNotStart or FinishStatus.Excluded } =>
                (null, envelope.PlayerDnfCause ?? DnfCause.Mechanical),
            _ => null,
        };
    }

    private static int? BestTeammateFinish(RoundResult result, IReadOnlyCollection<string> teammateIds)
    {
        var race = result.Sessions.FirstOrDefault(s => s.Kind == SessionKind.Race);
        if (race is null)
            return null;
        int? best = null;
        foreach (var entry in race.Entries)
        {
            if (entry.Status != FinishStatus.Classified || entry.Position is not { } position)
                continue;
            if (!teammateIds.Contains(entry.DriverId))
                continue;
            if (best is null || position < best)
                best = position;
        }
        return best;
    }

    /// <summary>The slider assumed when the envelope carries none (legacy payloads): the
    /// previous round's recommendation; before any recommendation exists, the current
    /// anchor's recommendation against this grid; failing that, the neutral 100%.</summary>
    private static double DefaultSlider(RoundPlayerState previous, GridPlan grid)
    {
        if (previous.RecommendedSlider > 0)
            return previous.RecommendedSlider;
        if (previous.Player.PaceAnchor > 0.0)
            return DifficultyModel.RecommendSlider(
                previous.Player.PaceAnchor, PaceAnchorMath.MedianAiRaceSkill(grid));
        return NeutralSlider;
    }

    /// <summary>How many positions score points this round, from the round's resolved
    /// scoring definition — the alternate (shortened-race) table when the result names one,
    /// else the season's race table.</summary>
    private static int PointsPositions(SeasonPack pack, RoundResult result)
    {
        var system = pack.Season.PointsSystem;
        if (result.AlternateRaceTableId is { } tableId &&
            system.AlternateRaceTables is { } tables &&
            tables.TryGetValue(tableId, out var alternate))
            return alternate.Count;
        return system.RacePoints.Count;
    }

    /// <summary>The engine-facing results among the stored rounds: CHAMPIONSHIP rounds only,
    /// in round order — the same filter the app session applies before computing the
    /// standings screen, so journaled and displayed standings share one input.</summary>
    private static List<RoundResult> ChampionshipResults(
        SeasonPack pack, IEnumerable<StoredRoundResult> stored) =>
        stored
            .Where(r => ChampionshipCalendar.IsChampionshipRound(pack, r.Round))
            .Select(r => r.ToRoundResult())
            .ToList();

    private static RoundPlayerState PreviousRoundState(
        CareerDatabase db, long seasonId, IReadOnlyList<StoredRoundResult> upTo, SqliteTransaction tx)
    {
        if (upTo.Count >= 2)
        {
            int previousRound = upTo[^2].Round;
            return StateStore.ReadRoundPlayerState(db, seasonId, previousRound, tx)
                ?? throw new InvalidOperationException(
                    $"Round {previousRound} of season {seasonId} was imported without the fold — " +
                    "re-simulate to rebuild the per-round player states before folding further rounds.");
        }

        return new RoundPlayerState
        {
            Player = StartPlayerState(db, seasonId, tx),
            RecommendedSlider = 0,
        };
    }

    private static PlayerCareerState StartPlayerState(
        CareerDatabase db, long seasonId, SqliteTransaction? transaction) =>
        StateStore.ReadPlayerState(db, seasonId, StateStore.StageStart, transaction)
        ?? throw MissingStartState(seasonId);

    private static InvalidOperationException MissingStartState(long seasonId) =>
        new($"Season {seasonId} has no start-of-season player state — it was never set up.");

    // ---------- season end + rollover helpers ----------

    private sealed record StoredSeason(
        SeasonRecord Season,
        IReadOnlyList<StoredRoundResult> Results,
        IReadOnlyList<JournalRow> StoredJournal,
        PlayerCareerState? StartPlayer,
        IReadOnlyList<DriverCareerState> StartDrivers,
        IReadOnlyList<TeamCareerState> StartTeams);

    private static bool IsComplete(SeasonRecord season) =>
        string.Equals(season.Status, SeasonStatus.Complete, StringComparison.Ordinal);

    private static SeasonEndContext BuildSeasonEndContext(
        int year,
        int playerAge,
        SeasonPack pack,
        ulong masterSeed,
        ReplaySimInputs inputs,
        IReadOnlyList<RoundResult> rounds,
        PlayerCareerState player,
        IReadOnlyList<DriverCareerState> drivers,
        IReadOnlyList<TeamCareerState> teams) => new()
    {
        Year = year,
        Streams = new StreamFactory(masterSeed),
        Pack = pack,
        Rounds = rounds,
        PlayerDriverId = inputs.PlayerDriverId,
        PlayerAge = playerAge,
        Player = player,
        Drivers = drivers,
        Teams = teams,
        AgingCurves = inputs.AgingCurves,
        Archetypes = inputs.Archetypes,
        Headlines = inputs.Headlines,
        CanonRetirements = inputs.CanonRetirements,
        TeamArchetypeOverrides = inputs.TeamArchetypeOverrides,
        FreeAgents = inputs.FreeAgents,
        PlayerSalaryAskBu = inputs.PlayerSalaryAskBu,
        PlayerName = inputs.PlayerName,
    };

    /// <summary>Re-derives a follow-on season's start states from the previous season's
    /// REGENERATED end states via <see cref="SeasonRollover.Derive"/> (the same function the
    /// live path uses) and compares them against the stored rows. The accepted team is a
    /// player choice read from the stored offers; the livery is a player choice read from
    /// the stored start row. Everything derived must match — a mismatch is a divergence.</summary>
    private static ReplayDivergence? VerifyRolloverStartStates(
        SeasonRecord previousSeason,
        SeasonEndResult? previousEnd,
        IReadOnlyList<(long SeasonId, string TeamId)> acceptedOffers,
        StoredSeason current)
    {
        long seasonId = current.Season.Id;
        if (previousEnd is null)
            throw new InvalidOperationException(
                $"Season {seasonId} follows season {previousSeason.Id}, which never completed — " +
                "the career file is structurally inconsistent.");

        string? acceptedTeam = acceptedOffers
            .Where(a => a.SeasonId == previousSeason.Id)
            .Select(a => a.TeamId)
            .FirstOrDefault();
        if (acceptedTeam is null)
        {
            return new ReplayDivergence
            {
                SeasonId = seasonId,
                Index = 0,
                Reason = "accepted-offer",
                StoredDeltaJson = null,
                RegeneratedDeltaJson =
                    $"season {seasonId} exists but season {previousSeason.Id} has no accepted offer",
            };
        }

        var derived = SeasonRollover.Derive(
            previousEnd.Player, previousEnd.Drivers, previousEnd.Teams,
            acceptedTeam, current.StartPlayer?.LiveryName);

        if (current.StartPlayer is null || derived.Player != current.StartPlayer)
        {
            return StartStateDivergence(seasonId,
                current.StartPlayer, derived.Player);
        }
        if (!derived.Drivers.SequenceEqual(current.StartDrivers))
        {
            return StartStateDivergence(seasonId,
                current.StartDrivers, derived.Drivers);
        }
        if (!derived.Teams.SequenceEqual(current.StartTeams))
        {
            return StartStateDivergence(seasonId,
                current.StartTeams, derived.Teams);
        }
        return null;
    }

    private static ReplayDivergence StartStateDivergence<T>(long seasonId, T? stored, T regenerated) =>
        new()
        {
            SeasonId = seasonId,
            Index = 0,
            Reason = "start-state",
            StoredDeltaJson = stored is null ? null : DataJson.Serialize(stored),
            RegeneratedDeltaJson = DataJson.Serialize(regenerated),
        };

    private static void PersistDerived(
        CareerDatabase db, long seasonId, SeasonEndResult result, SqliteTransaction? transaction)
    {
        StateStore.UpsertDriverStates(db, seasonId, StateStore.StageEnd, result.Drivers, transaction);
        StateStore.UpsertTeamStates(db, seasonId, StateStore.StageEnd, result.Teams, transaction);
        StateStore.UpsertPlayerState(db, seasonId, StateStore.StageEnd, result.Player, transaction);
        StateStore.UpsertOffers(db, seasonId, result.Offers, transaction);
    }

    private static void VerifyPackIsThePinnedOne(
        CareerDatabase db, SeasonPack pack, IReadOnlyList<SeasonRecord> seasons)
    {
        foreach (var (packId, version) in seasons.Select(s => (s.PackId, s.PackVersion)).Distinct())
        {
            if (!string.Equals(packId, pack.Manifest.PackId, StringComparison.Ordinal) ||
                !string.Equals(version, pack.Manifest.Version, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Replay v1 supports a single pinned pack per career; season data references " +
                    $"{packId} {version} but the supplied pack is " +
                    $"{pack.Manifest.PackId} {pack.Manifest.Version}.");

            var pinned = CareerStore.ReadPinnedPack(db, packId, version); // sha256-verified read

            // The pinned blob is either the app's five-file PinnedPackEnvelope or the legacy
            // canonical SeasonPack serialization (CareerStore.PinPack). Both parse to a
            // SeasonPack; the supplied pack must match the pinned one on the CANONICAL form,
            // so a career created by the app wizard verifies exactly like a tool-pinned one.
            SeasonPack pinnedPack;
            try
            {
                pinnedPack = PinnedPackEnvelope.LoadSeasonPack(pinned.PackJson);
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            {
                throw new InvalidDataException(
                    $"The pinned copy of {packId} {version} could not be parsed — " +
                    $"the career file is damaged. ({ex.Message})", ex);
            }

            byte[] supplied = JsonSerializer.SerializeToUtf8Bytes(pack, CoreJson.Options);
            byte[] pinnedCanonical = JsonSerializer.SerializeToUtf8Bytes(pinnedPack, CoreJson.Options);
            if (!supplied.AsSpan().SequenceEqual(pinnedCanonical))
                throw new InvalidOperationException(
                    $"The supplied pack differs from the pinned copy of {packId} {version} — " +
                    "replay must run against the exact pinned pack (load it via " +
                    "PinnedPackEnvelope.LoadSeasonPack / ReadPinnedPack).");
        }
    }

    private static List<(long SeasonId, string TeamId)> ReadAcceptedOffers(CareerDatabase db)
    {
        using var command = db.Command("SELECT season_id, team_id FROM offer WHERE accepted = 1;");
        using var reader = command.ExecuteReader();
        var accepted = new List<(long, string)>();
        while (reader.Read())
            accepted.Add((reader.GetInt64(0), reader.GetString(1)));
        return accepted;
    }

    private static ReplayDivergence? CompareSeason(
        long seasonId,
        IReadOnlyList<JournalRow> stored,
        IReadOnlyList<(int? Round, JournalEvent Event)> regenerated,
        ref int comparedRows)
    {
        int shared = Math.Min(stored.Count, regenerated.Count);
        for (int i = 0; i < shared; i++)
        {
            var row = stored[i];
            var (round, journalEvent) = regenerated[i];
            comparedRows++;

            string? reason =
                !string.Equals(row.Phase, journalEvent.Phase, StringComparison.Ordinal) ? "phase" :
                !string.Equals(row.Entity, journalEvent.Entity, StringComparison.Ordinal) ? "entity" :
                !string.Equals(row.DeltaJson, journalEvent.DeltaJson, StringComparison.Ordinal) ? "deltaJson" :
                !string.Equals(row.Cause, journalEvent.Cause, StringComparison.Ordinal) ? "cause" :
                row.Round != round ? "round" : null;

            if (reason is not null)
                return new ReplayDivergence
                {
                    SeasonId = seasonId,
                    Index = i,
                    StoredSeq = row.Seq,
                    Reason = reason,
                    StoredDeltaJson = row.DeltaJson,
                    RegeneratedDeltaJson = journalEvent.DeltaJson,
                };
        }

        if (stored.Count != regenerated.Count)
            return new ReplayDivergence
            {
                SeasonId = seasonId,
                Index = shared,
                StoredSeq = stored.Count > shared ? stored[shared].Seq : null,
                Reason = stored.Count > shared ? "extra-stored-row" : "missing-stored-row",
                StoredDeltaJson = stored.Count > shared ? stored[shared].DeltaJson : null,
                RegeneratedDeltaJson = regenerated.Count > shared ? regenerated[shared].Event.DeltaJson : null,
            };

        return null;
    }
}
