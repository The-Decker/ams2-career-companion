using System.Text;
using System.Text.Json;
using Companion.Ams2;
using Companion.Ams2.Grid;
using Companion.Core.Grid;
using Companion.Core.Json;
using Companion.Core.Numerics;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.Data;
using Microsoft.Data.Sqlite;

namespace Companion.ViewModels.Services;

/// <summary>
/// v1 implementation of the <see cref="ICareerSession"/> seam over CareerDatabase v1 tables,
/// the pinned season pack, <see cref="StandingsEngine"/> and the grid pipeline.
///
/// Invariants:
/// - The pack is pinned at creation (all five JSON parts in one hashed envelope blob);
///   opening a career rehydrates from the pinned bytes, never the mutable pack folder.
/// - Raw result payloads are the source of truth: standings are recomputed from
///   round_result_raw on every query, never cached.
/// - The current round is derived state — max(applied round) + 1 — so Apply advancing the
///   season needs no extra bookkeeping row.
/// - Staging is backup-first and aborts (Success=false) on any preflight ERROR; a missing
///   AMS2 install degrades to a failed outcome with a clear message, never a crash.
/// </summary>
public sealed class CareerSessionService : ICareerSession, IForceStaging, IDisposable
{
    private readonly CareerDatabase _database;
    private readonly CareerEnvironment _environment;
    private readonly long _seasonId;
    private readonly string _careerName;
    private readonly string _playerLiveryName;
    private readonly string _playerDriverId;
    private readonly SeasonScoringDefinition _scoringDefinition;

    public SeasonPack Pack { get; }

    public string CareerFilePath { get; }

    public long MasterSeed { get; }

    private CareerSessionService(
        CareerDatabase database,
        CareerEnvironment environment,
        SeasonPack pack,
        string careerFilePath,
        long seasonId,
        string careerName,
        long masterSeed,
        string playerLiveryName,
        string playerDriverId)
    {
        _database = database;
        _environment = environment;
        Pack = pack;
        CareerFilePath = careerFilePath;
        _seasonId = seasonId;
        _careerName = careerName;
        MasterSeed = masterSeed;
        _playerLiveryName = playerLiveryName;
        _playerDriverId = playerDriverId;
        // The scoring definition's round domain is CHAMPIONSHIP rounds (same resolution the
        // structural validator checks): best-N segments and engine round numbers use the
        // championship ordinal, not the calendar position, when the calendar mixes in
        // non-championship events.
        _scoringDefinition = pack.Season.PointsSystem.ResolveScoringDefinition(
            pack.Season.Rounds.Count(r => r.Championship));
    }

    /// <summary>1-based position of a championship calendar round among championship rounds
    /// only — the round number the scoring engine and best-N segments operate on.</summary>
    private int ChampionshipOrdinal(int calendarRound) =>
        Pack.Season.Rounds.Count(r => r.Championship && r.Round <= calendarRound);

    // ---------- create / open ----------

    public static CareerSessionService CreateCareer(CareerCreationRequest request, CareerEnvironment environment)
    {
        var files = SeasonPackFiles.Read(request.PackDirectory);
        var pack = files.Parse();
        string playerDriverId = ResolvePlayerDriverId(pack, request.PlayerLiveryName);

        if (File.Exists(request.CareerFilePath))
            throw new InvalidOperationException(
                $"'{request.CareerFilePath}' already exists — open it instead of creating over it.");

        string? directory = Path.GetDirectoryName(request.CareerFilePath);
        if (directory is { Length: > 0 })
            Directory.CreateDirectory(directory);

        var envelope = PinnedPackEnvelope.From(files);
        byte[] envelopeBytes = envelope.ToBytes();
        string sha256 = PinnedPackEnvelope.Sha256Of(envelopeBytes);

        var database = CareerDatabase.Open(request.CareerFilePath);
        try
        {
            string nowUtc = environment.Clock.GetUtcNow().UtcDateTime.ToString("O");
            long seasonId;

            using (var transaction = database.Connection.BeginTransaction())
            {
                Execute(database.Connection, transaction,
                    "INSERT INTO career (id, name, created_utc, master_seed, app_version) " +
                    "VALUES (1, @name, @utc, @seed, @version);",
                    ("@name", request.CareerName), ("@utc", nowUtc),
                    ("@seed", request.MasterSeed), ("@version", AppVersion));

                Execute(database.Connection, transaction,
                    "INSERT INTO pinned_pack (pack_id, version, sha256, pack_json, pinned_utc) " +
                    "VALUES (@id, @packVersion, @sha, @blob, @utc);",
                    ("@id", pack.Manifest.PackId), ("@packVersion", pack.Manifest.Version),
                    ("@sha", sha256), ("@blob", envelopeBytes), ("@utc", nowUtc));

                Execute(database.Connection, transaction,
                    "INSERT INTO season (year, pack_id, pack_version, status) " +
                    "VALUES (@year, @id, @packVersion, 'active');",
                    ("@year", pack.Season.Year), ("@id", pack.Manifest.PackId),
                    ("@packVersion", pack.Manifest.Version));
                seasonId = (long)Scalar(database.Connection, transaction, "SELECT last_insert_rowid();")!;

                var delta = new CareerCreatedDelta
                {
                    CareerName = request.CareerName,
                    PackId = pack.Manifest.PackId,
                    PackVersion = pack.Manifest.Version,
                    PackDirectory = request.PackDirectory,
                    PlayerLiveryName = request.PlayerLiveryName,
                    PlayerDriverId = playerDriverId,
                    MasterSeed = request.MasterSeed,
                };
                Execute(database.Connection, transaction,
                    "INSERT INTO journal (utc, season_id, round, phase, entity, delta_json, cause) " +
                    "VALUES (@utc, @season, NULL, 'career', 'career', @delta, 'career-created');",
                    ("@utc", nowUtc), ("@season", seasonId),
                    ("@delta", JsonSerializer.Serialize(delta, CoreJson.Options)));

                transaction.Commit();
            }

            return new CareerSessionService(
                database, environment, pack, request.CareerFilePath, seasonId,
                request.CareerName, request.MasterSeed, request.PlayerLiveryName, playerDriverId);
        }
        catch
        {
            database.Dispose();
            throw;
        }
    }

    public static CareerSessionService OpenCareer(string careerFilePath, CareerEnvironment environment)
    {
        if (!File.Exists(careerFilePath))
            throw new FileNotFoundException($"Career file '{careerFilePath}' does not exist.", careerFilePath);

        var database = CareerDatabase.Open(careerFilePath);
        try
        {
            var careerRow = Query(database.Connection,
                    "SELECT name, master_seed FROM career WHERE id = 1;",
                    r => (Name: r.GetString(0), Seed: r.GetInt64(1)))
                .SingleOrDefault(defaultValue: default);
            if (careerRow.Name is null)
                throw new InvalidOperationException($"'{careerFilePath}' has no career row — not a career file?");
            (string careerName, long masterSeed) = careerRow;

            var seasonRow = Query(database.Connection,
                    "SELECT id, pack_id, pack_version FROM season ORDER BY id LIMIT 1;",
                    r => (Id: r.GetInt64(0), PackId: r.GetString(1), PackVersion: r.GetString(2)))
                .SingleOrDefault(defaultValue: default);
            if (seasonRow.PackId is null)
                throw new InvalidOperationException($"'{careerFilePath}' has no season row.");
            (long seasonId, string packId, string packVersion) = seasonRow;

            var pinnedRow = Query(database.Connection,
                    "SELECT pack_json, sha256 FROM pinned_pack WHERE pack_id = @id AND version = @packVersion;",
                    r => (Blob: r.GetFieldValue<byte[]>(0), Sha: r.GetString(1)),
                    ("@id", packId), ("@packVersion", packVersion))
                .SingleOrDefault(defaultValue: default);
            if (pinnedRow.Blob is null)
                throw new InvalidOperationException(
                    $"'{careerFilePath}' references pinned pack {packId} {packVersion}, which is missing.");

            if (!string.Equals(PinnedPackEnvelope.Sha256Of(pinnedRow.Blob), pinnedRow.Sha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Pinned pack {packId} {packVersion} in '{careerFilePath}' fails its integrity hash — " +
                    "the career file is corrupt.");

            var pack = PinnedPackEnvelope.FromBytes(pinnedRow.Blob).Parse();

            string? deltaJson = Query(database.Connection,
                    "SELECT delta_json FROM journal WHERE phase = 'career' AND entity = 'career' ORDER BY seq LIMIT 1;",
                    r => r.GetString(0))
                .FirstOrDefault();
            if (deltaJson is null)
                throw new InvalidOperationException($"'{careerFilePath}' has no career-created journal row.");
            var delta = JsonSerializer.Deserialize<CareerCreatedDelta>(deltaJson, CoreJson.Options)
                ?? throw new InvalidOperationException("Career-created journal row deserialized to null.");

            return new CareerSessionService(
                database, environment, pack, careerFilePath, seasonId,
                careerName, masterSeed, delta.PlayerLiveryName, delta.PlayerDriverId);
        }
        catch
        {
            database.Dispose();
            throw;
        }
    }

    private static string ResolvePlayerDriverId(SeasonPack pack, string playerLiveryName)
    {
        var entry = pack.Entries.FirstOrDefault(e =>
            string.Equals(e.Ams2LiveryName, playerLiveryName, StringComparison.Ordinal));
        if (entry is not null)
            return entry.DriverId;

        var guest = pack.Season.Rounds
            .SelectMany(r => r.GuestEntries)
            .FirstOrDefault(g => string.Equals(g.Ams2LiveryName, playerLiveryName, StringComparison.Ordinal));
        if (guest is not null)
            return guest.DriverId;

        throw new InvalidOperationException(
            $"Player livery '{playerLiveryName}' is not an entry of pack {pack.Manifest.PackId} — " +
            "the seat pick must offer pack entries only.");
    }

    // ---------- summary / round state ----------

    public CareerSummary Summary
    {
        get
        {
            bool complete = SeasonComplete;
            var standings = CurrentStandings();
            return new CareerSummary
            {
                CareerName = _careerName,
                SeasonYear = Pack.Season.Year,
                SeriesName = Pack.Season.SeriesName,
                CurrentRound = complete ? RoundCount : CurrentRoundNumber,
                RoundCount = RoundCount,
                PlayerDriverId = _playerDriverId,
                PlayerLiveryName = _playerLiveryName,
                PlayerPosition = standings?.Drivers
                    .FirstOrDefault(d => d.DriverId == _playerDriverId)?.Position,
                SeasonComplete = complete,
            };
        }
    }

    private int RoundCount => Pack.Season.Rounds.Count;

    /// <summary>1-based number of the round currently being played (last applied + 1).</summary>
    public int CurrentRoundNumber => MaxAppliedRound + 1;

    private bool SeasonComplete => MaxAppliedRound >= RoundCount;

    private int MaxAppliedRound
    {
        get
        {
            object? value = Scalar(_database.Connection, null,
                "SELECT COALESCE(MAX(round), 0) FROM round_result_raw WHERE season_id = @season;",
                ("@season", _seasonId));
            return Convert.ToInt32(value);
        }
    }

    private PackRound RoundByNumber(int round) =>
        Pack.Season.Rounds.FirstOrDefault(r => r.Round == round)
        ?? throw new InvalidOperationException(
            $"Round {round} is not on the {Pack.Manifest.PackId} calendar.");

    // ---------- briefing / grid ----------

    public BriefingModel? CurrentBriefing()
    {
        if (SeasonComplete)
            return null;
        return BriefingComposer.Compose(Pack, RoundByNumber(CurrentRoundNumber), _environment.ContentLibrary);
    }

    public IReadOnlyList<GridSeat> CurrentGrid() =>
        SeasonComplete ? [] : ResolveGrid(CurrentRoundNumber).Seats;

    /// <summary>Resolves the round grid, marking the player's seat when their entry covers
    /// this round (an entry's rounds range may exclude it — then the grid is all-AI).</summary>
    private GridPlan ResolveGrid(int round)
    {
        var plan = RoundGridResolver.Resolve(Pack, round);
        if (plan.Seats.Any(s => string.Equals(s.Ams2LiveryName, _playerLiveryName, StringComparison.Ordinal)))
            plan = RoundGridResolver.Resolve(Pack, round, new PlayerSeat { Ams2LiveryName = _playerLiveryName });
        return plan;
    }

    // ---------- staging ----------

    public StageOutcome StageCurrentGrid() => StageCurrentGrid(force: false);

    public StageOutcome StageCurrentGrid(bool force)
    {
        var messages = new List<string>();

        if (SeasonComplete)
            return Failed(messages, "The season is complete — there is no round left to stage.");

        int roundNumber = CurrentRoundNumber;
        var packRound = RoundByNumber(roundNumber);

        GridPlan plan;
        try
        {
            plan = ResolveGrid(roundNumber);
        }
        catch (InvalidOperationException ex)
        {
            return Failed(messages, ex.Message);
        }

        var file = GridStager.Build(plan,
            $"{Pack.Manifest.Name} | Round {roundNumber}: {packRound.Name} | seed {MasterSeed}");

        Ams2Installation? installation = _environment.LocateInstall();
        if (installation is null)
            return Failed(messages,
                "No AMS2 installation was found — nothing was staged. Verify the game is installed " +
                "(or configure the install folder) and try again.");

        var (installedLiveries, scanWarnings) = _environment.ScanInstalledLiveries(installation);
        messages.AddRange(scanWarnings.Select(w => $"Warning: livery scan: {w}"));

        var preflight = GridStager.Preflight(
            file, _environment.ContentLibrary, installedLiveries, plan.TrackId, plan.Seats.Count);
        messages.AddRange(preflight.Issues.Select(i => $"{i.Severity}: {i.Message}"));

        if (preflight.HasErrors)
        {
            messages.Add("Staging aborted — fix the preflight errors above and stage again.");
            return new StageOutcome { Success = false, Messages = messages };
        }

        try
        {
            var result = GridStager.Stage(
                file, installation.CustomAiDriversDirectory, _environment.Clock.GetUtcNow(), force);
            messages.Add(result.Report);
            return new StageOutcome
            {
                Success = true,
                WrittenPath = result.WrittenPath,
                BackupPath = result.BackupPath,
                Messages = messages,
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return Failed(messages, ex.Message);
        }
    }

    private static StageOutcome Failed(List<string> messages, string message)
    {
        messages.Add(message);
        return new StageOutcome { Success = false, Messages = messages };
    }

    // ---------- results ----------

    public ConfirmModel Preview(ResultDraft draft)
    {
        if (SeasonComplete)
            throw new InvalidOperationException("The season is complete — there is no round to score.");

        int roundNumber = CurrentRoundNumber;
        var packRound = RoundByNumber(roundNumber);
        ValidateDraft(draft, roundNumber);

        var storedResults = StoredRoundResults();
        var before = storedResults.Count == 0
            ? null
            : StandingsEngine.ComputeSeason(_scoringDefinition, storedResults).Snapshots[^1];

        if (!packRound.Championship)
        {
            // Non-championship event: results are recorded but never scored.
            return new ConfirmModel
            {
                RoundPoints = DraftParticipants(draft)
                    .Select(driverId => (driverId, Rational.Zero)).ToList(),
                Movements = [],
                Headline = Headline(draft, packRound),
            };
        }

        var scored = new List<RoundResult>(storedResults) { ToRoundResult(draft, roundNumber, packRound) };
        var after = StandingsEngine.ComputeSeason(_scoringDefinition, scored).Snapshots[^1];

        int scoredRound = ChampionshipOrdinal(roundNumber);
        var roundPoints = new List<(string DriverId, Rational Points)>();
        foreach (string driverId in DraftParticipants(draft))
        {
            var standing = after.Drivers.FirstOrDefault(d => d.DriverId == driverId);
            var score = standing?.RoundScores.FirstOrDefault(s => s.Round == scoredRound);
            roundPoints.Add((driverId, score?.Points ?? Rational.Zero));
        }

        var movements = after.Drivers
            .Where(d => d.Position is not null)
            .OrderBy(d => d.Position!.Value)
            .ThenBy(d => d.DriverId, StringComparer.Ordinal)
            .Select(d => (
                d.DriverId,
                From: before?.Drivers.FirstOrDefault(p => p.DriverId == d.DriverId)?.Position,
                To: d.Position))
            .ToList();

        return new ConfirmModel
        {
            RoundPoints = roundPoints,
            Movements = movements,
            Headline = Headline(draft, packRound),
        };
    }

    public void Apply(ResultDraft draft)
    {
        if (SeasonComplete)
            throw new InvalidOperationException("The season is complete — there is no round to apply.");

        int roundNumber = CurrentRoundNumber;
        var packRound = RoundByNumber(roundNumber);
        ValidateDraft(draft, roundNumber);

        string payload = JsonSerializer.Serialize(draft, CoreJson.Options);
        string nowUtc = _environment.Clock.GetUtcNow().UtcDateTime.ToString("O");

        var journalDelta = new ResultAppliedDelta
        {
            Round = roundNumber,
            RoundName = packRound.Name,
            WinnerDriverId = draft.Classified.Count > 0 ? draft.Classified[0] : null,
            ClassifiedCount = draft.Classified.Count,
            DnfCount = draft.DidNotFinish.Count,
            DsqCount = draft.Disqualified.Count,
        };

        using var transaction = _database.Connection.BeginTransaction();
        Execute(_database.Connection, transaction,
            "INSERT INTO round_result_raw (season_id, round, entered_utc, source, payload_json) " +
            "VALUES (@season, @round, @utc, 'manual', @payload);",
            ("@season", _seasonId), ("@round", roundNumber), ("@utc", nowUtc),
            ("@payload", Encoding.UTF8.GetBytes(payload)));
        Execute(_database.Connection, transaction,
            "INSERT INTO journal (utc, season_id, round, phase, entity, delta_json, cause) " +
            "VALUES (@utc, @season, @round, 'result', 'round', @delta, 'result-entered');",
            ("@utc", nowUtc), ("@season", _seasonId), ("@round", roundNumber),
            ("@delta", JsonSerializer.Serialize(journalDelta, CoreJson.Options)));
        transaction.Commit();
    }

    // ---------- standings ----------

    public StandingsSnapshot? CurrentStandings()
    {
        var snapshots = AllSnapshots();
        return snapshots.Count == 0 ? null : snapshots[^1];
    }

    public IReadOnlyList<StandingsSnapshot> AllSnapshots()
    {
        var results = StoredRoundResults();
        if (results.Count == 0)
            return [];
        return StandingsEngine.ComputeSeason(_scoringDefinition, results).Snapshots;
    }

    /// <summary>Every stored championship-round result, recomputed from the raw payloads
    /// (results are the source of truth; there is no cached standings state).</summary>
    private List<RoundResult> StoredRoundResults()
    {
        var rows = Query(_database.Connection,
            "SELECT round, payload_json FROM round_result_raw WHERE season_id = @season ORDER BY round;",
            r => (Round: r.GetInt32(0), Payload: r.GetFieldValue<byte[]>(1)),
            ("@season", _seasonId));

        var results = new List<RoundResult>(rows.Count);
        foreach (var (roundNumber, payload) in rows)
        {
            var packRound = RoundByNumber(roundNumber);
            if (!packRound.Championship)
                continue;

            var draft = JsonSerializer.Deserialize<ResultDraft>(payload, CoreJson.Options)
                ?? throw new InvalidOperationException($"Round {roundNumber} raw payload deserialized to null.");
            results.Add(ToRoundResult(draft, roundNumber, packRound));
        }
        return results;
    }

    /// <summary>Maps a result draft onto the engine's round-result shape: classified drivers
    /// in list order (index 0 = P1), DNF → Retired, DSQ → Disqualified, constructors from the
    /// round grid's seats, and the round's rule overrides resolved through the pack's
    /// CatalogSeason (half points, constructors exclusion, alternate tables).</summary>
    private RoundResult ToRoundResult(ResultDraft draft, int roundNumber, PackRound packRound)
    {
        var rules = RoundRuleResolver.Resolve(Pack.Season.PointsSystem, packRound);
        var teamByDriver = RoundGridResolver.Resolve(Pack, roundNumber).Seats
            .ToDictionary(s => s.DriverId, s => s.TeamId, StringComparer.Ordinal);

        var entries = new List<ClassifiedEntry>();
        for (int i = 0; i < draft.Classified.Count; i++)
        {
            entries.Add(new ClassifiedEntry
            {
                DriverId = draft.Classified[i],
                ConstructorId = teamByDriver.GetValueOrDefault(draft.Classified[i]),
                Position = i + 1,
                Status = FinishStatus.Classified,
            });
        }
        foreach (string driverId in draft.DidNotFinish.Keys)
        {
            entries.Add(new ClassifiedEntry
            {
                DriverId = driverId,
                ConstructorId = teamByDriver.GetValueOrDefault(driverId),
                Status = FinishStatus.Retired,
            });
        }
        foreach (string driverId in draft.Disqualified)
        {
            entries.Add(new ClassifiedEntry
            {
                DriverId = driverId,
                ConstructorId = teamByDriver.GetValueOrDefault(driverId),
                Status = FinishStatus.Disqualified,
            });
        }

        return new RoundResult
        {
            Round = ChampionshipOrdinal(roundNumber),
            CountsForConstructors = rules.CountsForConstructors,
            PointsFactor = rules.PointsFactor,
            AlternateRaceTableId = rules.AlternateRaceTableId,
            Sessions = [new SessionResult { Kind = SessionKind.Race, Entries = entries }],
        };
    }

    private void ValidateDraft(ResultDraft draft, int roundNumber)
    {
        var gridDrivers = RoundGridResolver.Resolve(Pack, roundNumber).Seats
            .Select(s => s.DriverId)
            .ToHashSet(StringComparer.Ordinal);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string driverId in DraftParticipants(draft))
        {
            if (!seen.Add(driverId))
                throw new ArgumentException(
                    $"Driver '{driverId}' appears more than once in the round-{roundNumber} result draft.",
                    nameof(draft));
            if (!gridDrivers.Contains(driverId))
                throw new ArgumentException(
                    $"Driver '{driverId}' is not in the round-{roundNumber} grid.", nameof(draft));
        }
    }

    private static IEnumerable<string> DraftParticipants(ResultDraft draft) =>
        draft.Classified.Concat(draft.DidNotFinish.Keys).Concat(draft.Disqualified);

    /// <summary>Static headline template — M5's news engine replaces this.</summary>
    private string Headline(ResultDraft draft, PackRound packRound)
    {
        if (draft.Classified.Count == 0)
            return $"The {packRound.Name} ends without a classified winner";

        string winnerId = draft.Classified[0];
        string winnerName = Pack.Drivers.FirstOrDefault(d => d.Id == winnerId)?.Name ?? winnerId;
        return $"{winnerName} wins the {packRound.Name}";
    }

    // ---------- plumbing ----------

    private static string AppVersion =>
        typeof(CareerSessionService).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    private static void Execute(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }

    private static object? Scalar(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return command.ExecuteScalar();
    }

    private static List<T> Query<T>(
        SqliteConnection connection,
        string sql,
        Func<SqliteDataReader, T> map,
        params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);

        using var reader = command.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read())
            rows.Add(map(reader));
        return rows;
    }

    public void Dispose() => _database.Dispose();

    private sealed record CareerCreatedDelta
    {
        public required string CareerName { get; init; }
        public required string PackId { get; init; }
        public required string PackVersion { get; init; }
        public required string PackDirectory { get; init; }
        public required string PlayerLiveryName { get; init; }
        public required string PlayerDriverId { get; init; }
        public required long MasterSeed { get; init; }
    }

    private sealed record ResultAppliedDelta
    {
        public required int Round { get; init; }
        public required string RoundName { get; init; }
        public required string? WinnerDriverId { get; init; }
        public required int ClassifiedCount { get; init; }
        public required int DnfCount { get; init; }
        public required int DsqCount { get; init; }
    }
}
