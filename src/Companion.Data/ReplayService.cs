using System.Collections.ObjectModel;
using System.Text.Json;
using Companion.Core.Career;
using Companion.Core.Determinism;
using Companion.Core.Json;
using Companion.Core.Packs;
using Companion.Core.Scoring;

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
    /// (phase, entity, deltaJson, cause, round — seq and utc excluded by contract).</summary>
    public required bool Identical { get; init; }

    public ReplayDivergence? FirstDivergence { get; init; }

    /// <summary>Rows compared up to and including the divergence (all rows when identical).</summary>
    public required int ComparedRows { get; init; }
}

public sealed record ReplayDivergence
{
    public required long SeasonId { get; init; }

    /// <summary>0-based position in the season's sim-row sequence (audit rows excluded).</summary>
    public required int Index { get; init; }

    /// <summary>The stored journal row's seq; null when the stored journal ran out of rows.</summary>
    public long? StoredSeq { get; init; }

    /// <summary>Which field diverged first: phase, entity, deltaJson, cause, round —
    /// or extra-stored-row / missing-stored-row on a length mismatch.</summary>
    public required string Reason { get; init; }

    public string? StoredDeltaJson { get; init; }

    public string? RegeneratedDeltaJson { get; init; }
}

/// <summary>
/// Deterministic re-simulation (docs/dev/career-sim.md, Replay contract): sim state =
/// fold(journal); re-simulate = wipe derived state, replay raw results through the same
/// code + seed. <see cref="RoundStandingsEvents"/> and <see cref="RunSeasonEnd"/> are the
/// ONE fold implementation — the live import path and <see cref="Resimulate"/> both call
/// them, so replay regenerates the stored journal by construction and any divergence means
/// the file was tampered with, the payloads changed, or the engine changed.
/// </summary>
public static class ReplayService
{
    /// <summary>The derived journal rows for the latest of <paramref name="roundsSoFar"/>:
    /// the championship standings as they stand after that round (drivers, then constructors
    /// when the season has that championship) — same delta shape as the season-end
    /// championship rows.</summary>
    public static IReadOnlyList<JournalEvent> RoundStandingsEvents(
        SeasonPack pack, IReadOnlyList<RoundResult> roundsSoFar)
    {
        var scoring = pack.Season.PointsSystem.ResolveScoringDefinition(pack.Season.Rounds.Count);
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
    /// Runs the season-end pipeline against the season's stored start states and raw results,
    /// journals its events (round = NULL), persists the derived 'end' states + offers, and
    /// marks the season complete. The live app path calls this once per season; replay
    /// re-executes the identical code.
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

        var rounds = ResultStore.ReadSeasonResults(db, seasonId)
            .Select(r => r.ToRoundResult())
            .ToList();

        int playerAge = inputs.PlayerAge + (season.Year - seasons[0].Year);
        var context = BuildSeasonEndContext(db, seasonId, season.Year, playerAge, pack, masterSeed, inputs, rounds);
        var result = SeasonEndPipeline.Run(context);

        JournalStore.AppendMany(db, seasonId, round: null, result.Events, utc);
        PersistDerived(db, seasonId, result);
        CareerStore.CompleteSeason(db, seasonId);
        return result;
    }

    /// <summary>
    /// Re-simulates the whole career from its raw results: verifies the supplied pack against
    /// the pinned (hash-checked) bytes, wipes derived state (NOT raw results, NOT pinned
    /// packs, NOT the stored journal, NOT 'start' states — those are inputs), refolds every
    /// season, rebuilds the derived state tables, and byte-compares the regenerated journal
    /// sequence against the stored one (seq/utc excluded; import audit rows excluded).
    /// Accepted-offer flags — a player choice, not derived state — survive the wipe.
    /// </summary>
    public static ReplayReport Resimulate(
        CareerDatabase db, SeasonPack pack, ulong masterSeed, ReplaySimInputs inputs)
    {
        var seasons = CareerStore.ReadSeasons(db);
        if (seasons.Count == 0)
            return new ReplayReport { Identical = true, ComparedRows = 0 };

        VerifyPackIsThePinnedOne(db, pack, seasons);

        var acceptedOffers = ReadAcceptedOffers(db);
        StateStore.WipeDerived(db);

        int comparedRows = 0;
        int firstYear = seasons[0].Year;
        foreach (var season in seasons)
        {
            var rounds = ResultStore.ReadSeasonResults(db, season.Id)
                .Select(r => r.ToRoundResult())
                .ToList();

            // Refold: standings journal rows per round, in round order.
            var regenerated = new List<(int? Round, JournalEvent Event)>();
            for (int i = 0; i < rounds.Count; i++)
            {
                var upTo = rounds.Take(i + 1).ToList();
                foreach (var journalEvent in RoundStandingsEvents(pack, upTo))
                    regenerated.Add((rounds[i].Round, journalEvent));
            }

            // Season end, when it originally ran.
            if (string.Equals(season.Status, SeasonStatus.Complete, StringComparison.Ordinal))
            {
                int playerAge = inputs.PlayerAge + (season.Year - firstYear);
                var context = BuildSeasonEndContext(
                    db, season.Id, season.Year, playerAge, pack, masterSeed, inputs, rounds);
                var result = SeasonEndPipeline.Run(context);
                foreach (var journalEvent in result.Events)
                    regenerated.Add((null, journalEvent));

                PersistDerived(db, season.Id, result);
                foreach (var (offerSeasonId, teamId) in acceptedOffers)
                {
                    if (offerSeasonId == season.Id)
                        StateStore.SetOfferAccepted(db, season.Id, teamId);
                }
            }

            var stored = JournalStore.ReadSeason(db, season.Id)
                .Where(row => !row.Phase.StartsWith(DataJournalPhases.AuditPrefix, StringComparison.Ordinal))
                .ToList();

            var divergence = CompareSeason(season.Id, stored, regenerated, ref comparedRows);
            if (divergence is not null)
                return new ReplayReport
                {
                    Identical = false,
                    FirstDivergence = divergence,
                    ComparedRows = comparedRows,
                };
        }

        return new ReplayReport { Identical = true, ComparedRows = comparedRows };
    }

    // ---------- helpers ----------

    private static SeasonEndContext BuildSeasonEndContext(
        CareerDatabase db,
        long seasonId,
        int year,
        int playerAge,
        SeasonPack pack,
        ulong masterSeed,
        ReplaySimInputs inputs,
        IReadOnlyList<RoundResult> rounds)
    {
        var player = StateStore.ReadPlayerState(db, seasonId, StateStore.StageStart)
            ?? throw new InvalidOperationException(
                $"Season {seasonId} has no start-of-season player state — it was never set up.");

        return new SeasonEndContext
        {
            Year = year,
            Streams = new StreamFactory(masterSeed),
            Pack = pack,
            Rounds = rounds,
            PlayerDriverId = inputs.PlayerDriverId,
            PlayerAge = playerAge,
            Player = player,
            Drivers = StateStore.ReadDriverStates(db, seasonId, StateStore.StageStart),
            Teams = StateStore.ReadTeamStates(db, seasonId, StateStore.StageStart),
            AgingCurves = inputs.AgingCurves,
            Archetypes = inputs.Archetypes,
            Headlines = inputs.Headlines,
            CanonRetirements = inputs.CanonRetirements,
            TeamArchetypeOverrides = inputs.TeamArchetypeOverrides,
            FreeAgents = inputs.FreeAgents,
            PlayerSalaryAskBu = inputs.PlayerSalaryAskBu,
            PlayerName = inputs.PlayerName,
        };
    }

    private static void PersistDerived(CareerDatabase db, long seasonId, SeasonEndResult result)
    {
        StateStore.UpsertDriverStates(db, seasonId, StateStore.StageEnd, result.Drivers);
        StateStore.UpsertTeamStates(db, seasonId, StateStore.StageEnd, result.Teams);
        StateStore.UpsertPlayerState(db, seasonId, StateStore.StageEnd, result.Player);
        StateStore.UpsertOffers(db, seasonId, result.Offers);
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
            byte[] supplied = JsonSerializer.SerializeToUtf8Bytes(pack, CoreJson.Options);
            if (!supplied.AsSpan().SequenceEqual(pinned.PackJson))
                throw new InvalidOperationException(
                    $"The supplied pack differs from the pinned copy of {packId} {version} — " +
                    "replay must run against the exact pinned bytes (load it via ReadPinnedPack).");
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
