using System.Text.Json;
using Companion.Ams2;
using Companion.Ams2.CustomAi;
using Companion.Ams2.Grid;
using Companion.Core.Career;
using Companion.Core.Determinism;
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
public sealed class CareerSessionService : ICareerSession, IForceStaging, IAiFileRestore, IDisposable
{
    /// <summary><see cref="BaselineSource"/> value: the pinned baseline is pack-authored.</summary>
    public const string BaselineSourcePack = "pack";

    /// <summary><see cref="BaselineSource"/> value: the pinned baseline imported the user's
    /// installed AI file at creation (NAMeS-first, locked decision #7).</summary>
    public const string BaselineSourceInstalledAiFile = "installedAiFile";

    private readonly CareerDatabase _database;
    private readonly CareerEnvironment _environment;
    private readonly long _seasonId;
    private readonly string _careerName;
    private readonly string _playerLiveryName;
    private readonly string _playerDriverId;
    private readonly int _playerFirstSeasonAge;
    private readonly SeasonScoringDefinition _scoringDefinition;

    public SeasonPack Pack { get; }

    public string CareerFilePath { get; }

    public long MasterSeed { get; }

    /// <summary>Which baseline the pinned pack's drivers.json reflects:
    /// <see cref="BaselineSourcePack"/> or <see cref="BaselineSourceInstalledAiFile"/>.</summary>
    public string BaselineSource { get; }

    private CareerSessionService(
        CareerDatabase database,
        CareerEnvironment environment,
        SeasonPack pack,
        string careerFilePath,
        long seasonId,
        string careerName,
        long masterSeed,
        string playerLiveryName,
        string playerDriverId,
        int playerFirstSeasonAge,
        string baselineSource)
    {
        BaselineSource = baselineSource;
        _database = database;
        _environment = environment;
        Pack = pack;
        CareerFilePath = careerFilePath;
        _seasonId = seasonId;
        _careerName = careerName;
        MasterSeed = masterSeed;
        _playerLiveryName = playerLiveryName;
        _playerDriverId = playerDriverId;
        _playerFirstSeasonAge = playerFirstSeasonAge;
        // The scoring definition's round domain is CHAMPIONSHIP rounds (same resolution the
        // structural validator checks): best-N segments and engine round numbers use the
        // championship ordinal, not the calendar position, when the calendar mixes in
        // non-championship events. ChampionshipCalendar is the ONE shared mapping — the
        // unified fold (ReplayService) resolves through the same code.
        _scoringDefinition = ChampionshipCalendar.ResolveScoring(pack);
    }

    /// <summary>1-based position of a championship calendar round among championship rounds
    /// only — the round number the scoring engine and best-N segments operate on.</summary>
    private int ChampionshipOrdinal(int calendarRound) =>
        ChampionshipCalendar.Ordinal(Pack, calendarRound);

    // ---------- create / open ----------

    public static CareerSessionService CreateCareer(CareerCreationRequest request, CareerEnvironment environment)
    {
        var files = SeasonPackFiles.Read(request.PackDirectory);
        var pack = files.Parse();

        // NAMeS-first baseline import (locked decision #7a): fold the user's installed AI
        // file into drivers.json BEFORE pinning, so the pinned pack IS the imported baseline
        // and the career never depends on the mutable installed file again.
        BaselineImportResult? import = null;
        if (request.CommunityBaselineXml is { Length: > 0 } baselineXml)
        {
            var installed = CommunityAiReader.Parse(baselineXml);
            import = CommunityBaselineImport.Apply(pack, installed);
            files = files with
            {
                DriversJson = JsonSerializer.Serialize(
                    new PackDriversFile { Drivers = import.Drivers }, CoreJson.Options),
            };
            pack = files.Parse();
        }

        string playerDriverId = ResolvePlayerDriverId(pack, request.PlayerLiveryName);

        if (File.Exists(request.CareerFilePath))
            throw new InvalidOperationException(
                $"'{request.CareerFilePath}' already exists — open it instead of creating over it.");

        string? directory = Path.GetDirectoryName(request.CareerFilePath);
        if (directory is { Length: > 0 })
            Directory.CreateDirectory(directory);

        var envelope = files.ToPinnedEnvelope();
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
                    BaselineSource = import is null ? BaselineSourcePack : BaselineSourceInstalledAiFile,
                    BaselineSourcePath = import is null ? null : request.CommunityBaselineSourcePath,
                    BaselineImportedDrivers = import?.ImportedDriverCount ?? 0,
                    BaselinePackOnlyDrivers = import?.PackOnlyDriverCount ?? 0,
                };
                Execute(database.Connection, transaction,
                    "INSERT INTO journal (utc, season_id, round, phase, entity, delta_json, cause) " +
                    "VALUES (@utc, @season, NULL, 'career', 'career', @delta, 'career-created');",
                    ("@utc", nowUtc), ("@season", seasonId),
                    ("@delta", JsonSerializer.Serialize(delta, CoreJson.Options)));

                SeedStartStates(database, seasonId, pack, playerDriverId, request.PlayerLiveryName, transaction);

                transaction.Commit();
            }

            return new CareerSessionService(
                database, environment, pack, request.CareerFilePath, seasonId,
                request.CareerName, request.MasterSeed, request.PlayerLiveryName, playerDriverId,
                PlayerAgeIn(pack, playerDriverId),
                import is null ? BaselineSourcePack : BaselineSourceInstalledAiFile);
        }
        catch
        {
            database.Dispose();
            throw;
        }
    }

    /// <summary>Opens a career into its CURRENT season — the LATEST season row — so a
    /// multi-season career (M6 era transitions) always continues where the player is, never
    /// back in a finished era. Single-season careers open exactly as before.</summary>
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

            var seasons = CareerStore.ReadSeasons(database);
            if (seasons.Count == 0)
                throw new InvalidOperationException($"'{careerFilePath}' has no season row.");
            var first = seasons[0];
            var latest = seasons[^1];

            // Pinned blobs come in two formats: the wizard's five-file envelope (season 1)
            // and CareerStore.PinPack's canonical serialization (era-transition seasons).
            // ReadPinnedPack verifies the sha256; LoadSeasonPack accepts both formats.
            var pack = PinnedPackEnvelope.LoadSeasonPack(
                CareerStore.ReadPinnedPack(database, latest.PackId, latest.PackVersion).PackJson);

            string? deltaJson = Query(database.Connection,
                    "SELECT delta_json FROM journal WHERE phase = 'career' AND entity = 'career' ORDER BY seq LIMIT 1;",
                    r => r.GetString(0))
                .FirstOrDefault();
            if (deltaJson is null)
                throw new InvalidOperationException($"'{careerFilePath}' has no career-created journal row.");
            var delta = JsonSerializer.Deserialize<CareerCreatedDelta>(deltaJson, CoreJson.Options)
                ?? throw new InvalidOperationException("Career-created journal row deserialized to null.");

            string playerLiveryName;
            string playerDriverId;
            string baselineSource;
            int playerFirstSeasonAge;
            if (latest.Id == first.Id)
            {
                // Single-season career: the wizard facts apply verbatim (byte-compat path).
                playerLiveryName = delta.PlayerLiveryName;
                playerDriverId = delta.PlayerDriverId;
                baselineSource = delta.BaselineSource;
                playerFirstSeasonAge = PlayerAgeIn(pack, playerDriverId);
            }
            else
            {
                // Era-transitioned career: the player's seat in the CURRENT season comes from
                // the transition plan's start state (CareerStore.StartNextSeason persisted it);
                // the driver id is the new pack's entry behind that livery — the seat the
                // player took over, exactly like the wizard's season-1 seat pick.
                var start = StateStore.ReadPlayerState(database, latest.Id, StateStore.StageStart)
                    ?? throw new InvalidOperationException(
                        $"Season {latest.Id} ({latest.Year}) has no start-of-season player state — " +
                        "the career file is structurally inconsistent.");
                playerLiveryName = start.LiveryName
                    ?? throw new InvalidOperationException(
                        $"Season {latest.Id} ({latest.Year})'s player state has no seat livery — " +
                        "the era transition that started it was incomplete.");
                playerDriverId = ResolvePlayerDriverId(pack, playerLiveryName);
                baselineSource = BaselineSourcePack; // transition seasons pin pack-authored data

                // The season-end pipeline ages the player by year distance from the FIRST
                // season (ReplaySimInputs.PlayerAge contract) — derive the anchor age from
                // the first season's pinned pack and the wizard's seat pick.
                var firstPack = PinnedPackEnvelope.LoadSeasonPack(
                    CareerStore.ReadPinnedPack(database, first.PackId, first.PackVersion).PackJson);
                playerFirstSeasonAge = PlayerAgeIn(firstPack, delta.PlayerDriverId);
            }

            return new CareerSessionService(
                database, environment, pack, careerFilePath, latest.Id,
                careerName, masterSeed, playerLiveryName, playerDriverId,
                playerFirstSeasonAge, baselineSource);
        }
        catch
        {
            database.Dispose();
            throw;
        }
    }

    /// <summary>The player's age in <paramref name="pack"/>'s season: the Born year of the
    /// driver whose entry the player took (the wizard's replace-a-historical-driver rule),
    /// defaulting to 30 when the pack does not author one.</summary>
    private static int PlayerAgeIn(SeasonPack pack, string playerDriverId) =>
        pack.Season.Year - (
            pack.Drivers.FirstOrDefault(d => string.Equals(d.Id, playerDriverId, StringComparison.Ordinal))
                ?.Born ?? pack.Season.Year - 30);

    /// <summary>Seeds the season's stage-'start' sim-input states — the wizard facts the fold
    /// and season end consume (docs/dev/career-sim.md): AI driver ages from the pack's Born
    /// years, team tiers from the pack's budget tiers, and the player state (tier-derived
    /// starting reputation, uncalibrated OPI/anchor, the chosen seat).</summary>
    private static void SeedStartStates(
        CareerDatabase database,
        long seasonId,
        SeasonPack pack,
        string playerDriverId,
        string playerLiveryName,
        SqliteTransaction transaction)
    {
        int year = pack.Season.Year;
        var aiDrivers = pack.Drivers
            .Where(d => !string.Equals(d.Id, playerDriverId, StringComparison.Ordinal))
            .Select(d => new DriverCareerState
            {
                DriverId = d.Id,
                Age = year - (d.Born ?? year - 30),
            })
            .ToList();
        var teams = pack.Teams
            .Select(t => new TeamCareerState { TeamId = t.Id, LineageId = t.Id, Tier = t.BudgetTier })
            .ToList();

        string? playerTeamId = PlayerTeamId(pack, playerLiveryName);
        int playerTier = pack.Teams
            .FirstOrDefault(t => string.Equals(t.Id, playerTeamId, StringComparison.Ordinal))
            ?.BudgetTier ?? 3;
        var player = new PlayerCareerState
        {
            Reputation = SeatCandidate.DefaultReputation(playerTier),
            Opi = 0.0,
            PaceAnchor = 0.0,
            SeasonsCompleted = 0,
            CurrentTeamId = playerTeamId,
            LiveryName = playerLiveryName,
        };

        StateStore.UpsertDriverStates(database, seasonId, StateStore.StageStart, aiDrivers, transaction);
        StateStore.UpsertTeamStates(database, seasonId, StateStore.StageStart, teams, transaction);
        StateStore.UpsertPlayerState(database, seasonId, StateStore.StageStart, player, transaction);
    }

    private static string? PlayerTeamId(SeasonPack pack, string playerLiveryName) =>
        pack.Entries
            .FirstOrDefault(e => string.Equals(e.Ams2LiveryName, playerLiveryName, StringComparison.Ordinal))
            ?.TeamId
        ?? pack.Season.Rounds
            .SelectMany(r => r.GuestEntries)
            .FirstOrDefault(g => string.Equals(g.Ams2LiveryName, playerLiveryName, StringComparison.Ordinal))
            ?.TeamId;

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
            var (reputation, opi, repDelta, opiDelta) = PlayerTrend();
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
                Reputation = reputation,
                Opi = opi,
                ReputationDelta = repDelta,
                OpiDelta = opiDelta,
            };
        }
    }

    /// <summary>The home header's reputation + OPI trend, read from the FOLDED per-round
    /// player states (never recomputed here — the fold is the one source of truth).</summary>
    private (double? Reputation, double? Opi, double? RepDelta, double? OpiDelta) PlayerTrend()
    {
        var states = StateStore.ReadRoundPlayerStates(_database, _seasonId);
        if (states.Count == 0)
            return (null, null, null, null);

        var last = states[^1].State.Player;
        var previous = states.Count > 1
            ? states[^2].State.Player
            : StateStore.ReadPlayerState(_database, _seasonId, StateStore.StageStart);
        return (
            last.Reputation,
            last.Opi,
            previous is null ? null : last.Reputation - previous.Reputation,
            previous is null ? null : last.Opi - previous.Opi);
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
        var briefing = BriefingComposer.Compose(Pack, RoundByNumber(CurrentRoundNumber), _environment.ContentLibrary);
        return briefing with { RecommendedSlider = CurrentSliderRecommendation() };
    }

    /// <summary>The difficulty recommendation for the current round: the last folded round's
    /// recommendation; when that round predates calibration, the anchor re-projected onto the
    /// current grid. Null before any round folds or before the anchor calibrates.</summary>
    public int? CurrentSliderRecommendation()
    {
        int lastRound = MaxAppliedRound;
        if (lastRound == 0)
            return null;

        // Legacy careers may have applied rounds without folds; degrade to no recommendation.
        var state = StateStore.ReadRoundPlayerState(_database, _seasonId, lastRound);
        if (state is null)
            return null;
        if (state.RecommendedSlider > 0)
            return state.RecommendedSlider;
        if (state.Player.PaceAnchor > 0.0 && !SeasonComplete)
        {
            var grid = ResolveGrid(CurrentRoundNumber);
            return DifficultyModel.RecommendSlider(
                state.Player.PaceAnchor, PaceAnchorMath.MedianAiRaceSkill(grid));
        }
        return null;
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

        // ONE aggregate line for the livery scan; the per-file unreadable list rides along
        // as collapsed details, never as a wall of warning rows.
        var scan = _environment.ScanInstalledLiveries(installation);
        if (scan.FilesScanned > 0)
            messages.Add(scan.Summary);
        var details = scan.UnreadableFiles;

        // PRIMARY name authority: the user's installed CustomAIDrivers class file. A name it
        // defines is valid whatever the skin state — no false "won't bind" warning.
        var installedAiNames = _environment.ScanInstalledAiNames(installation, file.VehicleClass);

        var preflight = GridStager.Preflight(
            file, _environment.ContentLibrary, scan.Liveries, plan.TrackId, plan.Seats.Count, installedAiNames);
        messages.AddRange(preflight.Issues.Select(i => $"{i.Severity}: {i.Message}"));

        if (preflight.HasErrors)
        {
            messages.Add("Staging aborted — fix the preflight errors above and stage again.");
            return new StageOutcome { Success = false, Messages = messages, Details = details };
        }

        try
        {
            // NAMeS-primary staging ("found before overwritten"): the installed class file is
            // read before any write and, for every seat it already defines, its name + base
            // ratings win — only this round's / the career's delta (measured against the pinned
            // pack's own driver baseline) is applied over them.
            var result = GridStager.StageOrRefuse(
                file, installation.CustomAiDriversDirectory, _environment.Clock.GetUtcNow(), force,
                PackBaselineByLivery());

            if (result.RequiresForce)
            {
                // The community-file force gate is an EXPECTED choice, not a failure: the
                // briefing renders this outcome as an informational (amber) banner with the
                // existing "Stage anyway (backup first)" escape hatch.
                messages.Add(
                    $"Your installed {file.VehicleClass}.xml differs from this round's grid " +
                    "(community NAMeS file). Your installed names/AI are kept — only this round's " +
                    "grid selection and your career changes are applied. 'Stage anyway' takes a " +
                    "timestamped backup first.");
                return new StageOutcome
                {
                    Success = false,
                    BlockedByForceGate = true,
                    WrittenPath = null,
                    Messages = messages,
                    Details = details,
                };
            }

            messages.Add(result.Report);
            return new StageOutcome
            {
                Success = true,
                WrittenPath = result.WrittenPath,
                BackupPath = result.BackupPath,
                NoOpAlreadyMatches = result.NoOpAlreadyMatches,
                Messages = messages,
                Details = details,
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return Failed(messages, ex.Message, details);
        }
    }

    private static StageOutcome Failed(
        List<string> messages, string message, IReadOnlyList<string>? details = null)
    {
        messages.Add(message);
        return new StageOutcome { Success = false, Messages = messages, Details = details ?? [] };
    }

    /// <summary>The pinned pack's OWN per-livery driver baseline (before any trackForm/aiOverride
    /// or career effect) keyed by <c>ams2LiveryName</c> — the delta reference for NAMeS-primary
    /// staging (<see cref="GridStager.MergeInstalledPrimary"/>). Each entry's livery maps to a
    /// <see cref="CustomAiDriver"/> carrying that pack driver's raw ratings/name/country, so
    /// staging can tell a genuine round/career change (generated != this baseline) from a stale
    /// pinned value (generated == this baseline) and keep the user's installed value in the
    /// latter case. First entry per livery wins (guest entries fill liveries entries.json omits).</summary>
    private IReadOnlyDictionary<string, CustomAiDriver> PackBaselineByLivery()
    {
        var driversById = Pack.Drivers.ToDictionary(d => d.Id, StringComparer.Ordinal);
        var byLivery = new Dictionary<string, CustomAiDriver>(StringComparer.Ordinal);

        void Add(string liveryName, string driverId)
        {
            if (byLivery.ContainsKey(liveryName))
                return;
            if (driversById.TryGetValue(driverId, out var driver))
                byLivery[liveryName] = ToBaselineDriver(liveryName, driver);
        }

        foreach (var entry in Pack.Entries)
            Add(entry.Ams2LiveryName, entry.DriverId);
        foreach (var guest in Pack.Season.Rounds.SelectMany(r => r.GuestEntries))
            Add(guest.Ams2LiveryName, guest.DriverId);

        return byLivery;
    }

    /// <summary>Maps a pinned-pack driver's raw baseline ratings onto the custom-AI vocabulary
    /// (no trackForm, no aiOverrides, no career drift) — the reference the staging merge diffs
    /// the generated round file against.</summary>
    private static CustomAiDriver ToBaselineDriver(string liveryName, Companion.Core.Packs.PackDriver driver) => new()
    {
        LiveryName = liveryName,
        Name = driver.Name,
        Country = driver.Country,
        RaceSkill = driver.Ratings.RaceSkill,
        QualifyingSkill = driver.Ratings.QualifyingSkill,
        Aggression = driver.Ratings.Aggression,
        Defending = driver.Ratings.Defending,
        Stamina = driver.Ratings.Stamina,
        Consistency = driver.Ratings.Consistency,
        StartReactions = driver.Ratings.StartReactions,
        WetSkill = driver.Ratings.WetSkill,
        TyreManagement = driver.Ratings.TyreManagement,
        AvoidanceOfMistakes = driver.Ratings.AvoidanceOfMistakes,
        BlueFlagConceding = driver.Ratings.BlueFlagConceding,
        WeatherTyreChanges = driver.Ratings.WeatherTyreChanges,
        AvoidanceOfForcedMistakes = driver.Ratings.AvoidanceOfForcedMistakes,
        FuelManagement = driver.Ratings.FuelManagement,
    };

    // ---------- season-end restore (IAiFileRestore) ----------

    /// <summary>Re-backup the current class XML first, then restore the pre-season original:
    /// the newest backup NOT generated by this app (the user's own file, snapshotted before
    /// the first divergent write). When every backup is app-generated, the newest backup is
    /// used. Restore never destroys state — the pre-restore file is always in the backups.</summary>
    public RestoreOutcome RestoreOriginalAiFile()
    {
        var messages = new List<string>();

        var installation = _environment.LocateInstall();
        if (installation is null)
        {
            messages.Add("No AMS2 installation was found — there is nothing to restore.");
            return new RestoreOutcome { Success = false, Messages = messages };
        }

        string vehicleClass = Pack.Season.Ams2Class;
        var backup = new CustomAiBackup(installation.CustomAiDriversDirectory);
        var backups = backup.ListBackups(vehicleClass);
        if (backups.Count == 0)
        {
            messages.Add(
                $"No backup of {vehicleClass}.xml exists — the app never overwrote it, " +
                "so your installed file is already the original.");
            return new RestoreOutcome { Success = false, Messages = messages };
        }

        string original = backups.FirstOrDefault(path => !GridStager.LooksGenerated(path)) ?? backups[0];

        try
        {
            string? currentBackup = backup.BackupIfPresent(vehicleClass, _environment.Clock.GetUtcNow());
            string target = backup.Restore(original, vehicleClass);

            if (currentBackup is not null)
                messages.Add($"Current {vehicleClass}.xml re-backed up to '{currentBackup}'.");
            messages.Add($"Restored '{original}' over '{target}'.");

            return new RestoreOutcome
            {
                Success = true,
                RestoredFromBackupPath = original,
                CurrentFileBackupPath = currentBackup,
                Messages = messages,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            messages.Add(ex.Message);
            return new RestoreOutcome { Success = false, Messages = messages };
        }
    }

    // ---------- results ----------

    public ConfirmModel Preview(ResultDraft draft)
    {
        if (SeasonComplete)
            throw new InvalidOperationException("The season is complete — there is no round to score.");

        int roundNumber = CurrentRoundNumber;
        var packRound = RoundByNumber(roundNumber);
        ValidateDraft(draft, roundNumber);

        // The confirm headline comes from the SAME fold Apply will run: the envelope is
        // staged and folded inside a rolled-back transaction, so the preview is byte-exact
        // against the eventual journal without committing anything.
        var envelope = BuildEnvelope(draft, roundNumber, packRound);
        var fold = PreviewFold(envelope, roundNumber);
        string headline = fold.Headline
            ?? $"The {packRound.Name} is in the books";

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
                Headline = headline,
            };
        }

        var scored = new List<RoundResult>(storedResults) { envelope.Result };
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
            Headline = headline,
        };
    }

    /// <summary>Runs the round's raw-store append + unified fold inside one transaction and
    /// ROLLS IT BACK: the returned fold (headline, events, state) is exactly what Apply will
    /// commit, computed by the same code path, with zero persistence.</summary>
    private RoundFoldResult PreviewFold(RoundResultEnvelope envelope, int roundNumber)
    {
        string nowUtc = NowUtc();
        using var transaction = _database.Connection.BeginTransaction();
        ResultStore.Append(
            _database, _seasonId, roundNumber,
            JsonSerializer.Serialize(envelope, CoreJson.Options), nowUtc, "manual", transaction);
        var fold = ReplayService.FoldRound(
            _database, _seasonId, Pack, MasterSeedU, SimInputs(), roundNumber, nowUtc, transaction);
        transaction.Rollback();
        return fold;
    }

    /// <summary>The live path: store the round's raw-result ENVELOPE and run the unified fold
    /// — one atomic unit via <see cref="ReplayService.ImportAndFoldRound"/>, so a stored raw
    /// result can never exist without its fold (docs/dev/m5-fix-integration.md step 3). When
    /// the final round lands, the season-end pipeline runs off the folded player state.</summary>
    public void Apply(ResultDraft draft)
    {
        if (SeasonComplete)
            throw new InvalidOperationException("The season is complete — there is no round to apply.");

        int roundNumber = CurrentRoundNumber;
        var packRound = RoundByNumber(roundNumber);
        ValidateDraft(draft, roundNumber);

        var envelope = BuildEnvelope(draft, roundNumber, packRound);
        string nowUtc = NowUtc();

        ReplayService.ImportAndFoldRound(
            _database, _seasonId, Pack, MasterSeedU, SimInputs(), roundNumber, envelope, nowUtc);

        // App-level provenance row about the entry EVENT (excluded from the replay
        // byte-compare like every provenance row) — the sim rows come from the fold.
        var journalDelta = new ResultAppliedDelta
        {
            Round = roundNumber,
            RoundName = packRound.Name,
            WinnerDriverId = draft.Classified.Count > 0 ? draft.Classified[0] : null,
            ClassifiedCount = draft.Classified.Count,
            DnfCount = draft.DidNotFinish.Count,
            DsqCount = draft.Disqualified.Count,
        };
        JournalStore.Append(_database, _seasonId, roundNumber,
            new JournalEvent
            {
                Phase = DataJournalPhases.ResultProvenance,
                Entity = "round",
                DeltaJson = JsonSerializer.Serialize(journalDelta, CoreJson.Options),
                Cause = "result-entered",
            },
            nowUtc);

        if (SeasonComplete)
            EnsureSeasonEnd();
    }

    /// <summary>The envelope stored as the round's raw payload: the mapped classification
    /// plus the unre-derivable round context — the slider actually driven (the draft's value,
    /// else the current recommendation, else neutral) and the player's DNF cause.</summary>
    private RoundResultEnvelope BuildEnvelope(ResultDraft draft, int roundNumber, PackRound packRound)
    {
        double slider = draft.SliderUsed
            ?? CurrentSliderRecommendation()
            ?? ReplayService.NeutralSlider;
        return new RoundResultEnvelope
        {
            Result = ToRoundResult(draft, roundNumber, packRound),
            SliderUsed = Math.Clamp(slider, DifficultyModel.MinSlider, DifficultyModel.MaxSlider),
            PlayerDnfCause = PlayerDnfCauseFrom(draft),
        };
    }

    /// <summary>Maps the result screen's reason for the PLAYER onto the sim's blame model:
    /// m(echanical) = no blame, a(ccident) = driver error, o(ther) = the fold's no-blame
    /// default UNLESS the custom detail was flagged as the driver's fault, in which case it is
    /// driver error. The custom free text does not change blame on its own — only the explicit
    /// attribution flag does — so "default custom-other = no-blame" holds (mandate M5 rule).</summary>
    private DnfCause? PlayerDnfCauseFrom(ResultDraft draft) =>
        draft.DidNotFinish.TryGetValue(_playerDriverId, out string? reason)
            ? reason switch
            {
                "m" => DnfCause.Mechanical,
                "a" => DnfCause.DriverError,
                _ => draft.DidNotFinishDetail.TryGetValue(_playerDriverId, out var detail)
                    && detail.DriverAttributed
                        ? DnfCause.DriverError
                        : null,
            }
            : null;

    /// <summary>The rules data + wizard-fixed facts every fold and season end consumes.
    /// Deterministically re-derived from the pinned packs and the app-shipped rules files, so
    /// every session (and replay) constructs the identical inputs. PlayerAge is the FIRST
    /// season's age per the <see cref="ReplaySimInputs"/> contract — the season-end pipeline
    /// offsets it by year distance itself, so era-transitioned seasons age correctly.</summary>
    private ReplaySimInputs SimInputs()
    {
        var rules = _environment.Rules;
        return new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = _playerDriverId,
            PlayerAge = _playerFirstSeasonAge,
        };
    }

    private ulong MasterSeedU => unchecked((ulong)MasterSeed);

    private string NowUtc() => _environment.Clock.GetUtcNow().UtcDateTime.ToString("O");

    // ---------- season end / review ----------

    /// <summary>Runs the season-end pipeline once per season (idempotent): offers scored from
    /// the final round's FOLDED player state, events journaled, derived states persisted, the
    /// season marked complete. Self-heals a career closed between the final Apply and here.</summary>
    private void EnsureSeasonEnd()
    {
        var season = CareerStore.ReadSeasons(_database).FirstOrDefault(s => s.Id == _seasonId)
            ?? throw new InvalidOperationException($"Season {_seasonId} vanished from the career file.");
        if (string.Equals(season.Status, SeasonStatus.Complete, StringComparison.Ordinal))
            return;
        ReplayService.RunSeasonEnd(_database, _seasonId, Pack, MasterSeedU, SimInputs(), NowUtc());
    }

    public SeasonReviewModel? SeasonReview()
    {
        if (!SeasonComplete)
            return null;
        EnsureSeasonEnd();

        var teamsById = Pack.Teams.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var offers = StateStore.ReadOffers(_database, _seasonId);
        var headlines = JournalStore.ReadSeason(_database, _seasonId)
            .Where(r => string.Equals(r.Phase, JournalPhases.Headline, StringComparison.Ordinal))
            .Select(r => HeadlineText(r.DeltaJson))
            .OfType<string>()
            .ToList();
        var finalPlayer = StateStore.ReadPlayerState(_database, _seasonId, StateStore.StageEnd);
        var standings = CurrentStandings();

        return new SeasonReviewModel
        {
            SeasonYear = Pack.Season.Year,
            PlayerPosition = standings?.Drivers
                .FirstOrDefault(d => d.DriverId == _playerDriverId)?.Position,
            FinalReputation = finalPlayer?.Reputation ?? 0.0,
            FinalOpi = finalPlayer?.Opi ?? 0.0,
            Headlines = headlines,
            Offers = offers
                .Select(o => new SeasonOfferModel
                {
                    TeamId = o.Terms.TeamId,
                    TeamName = teamsById.TryGetValue(o.Terms.TeamId, out var team) ? team.Name : o.Terms.TeamId,
                    Tier = o.Terms.Tier,
                    SalaryBu = o.Terms.SalaryBu,
                    Score = o.Terms.Score,
                    Accepted = o.Accepted,
                })
                .ToList(),
            AcceptedTeamId = offers.FirstOrDefault(o => o.Accepted)?.Terms.TeamId,
        };
    }

    /// <summary>Accepts one offer (a player CHOICE — replay re-applies it, never derives it)
    /// and journals it as a provenance row so the replay byte-compare is unaffected.</summary>
    public void AcceptOffer(string teamId)
    {
        var offers = StateStore.ReadOffers(_database, _seasonId);
        if (!offers.Any(o => string.Equals(o.Terms.TeamId, teamId, StringComparison.Ordinal)))
            throw new InvalidOperationException(
                $"Team '{teamId}' made no offer this season — only extended offers can be accepted.");

        StateStore.SetOfferAccepted(_database, _seasonId, teamId);
        JournalStore.Append(_database, _seasonId, round: null,
            new JournalEvent
            {
                Phase = DataJournalPhases.CareerProvenance,
                Entity = "offer",
                DeltaJson = JsonSerializer.Serialize(new { teamId }, CoreJson.Options),
                Cause = "offer-accepted",
            },
            NowUtc());
    }

    // ---------- era transition (M6 sign-and-continue) ----------

    /// <summary>The next era pack per the v1 rule (see <see cref="ICareerSession.NextSeason"/>):
    /// scans the environment's pack search roots and picks the readable pack with the
    /// smallest season year strictly greater than the current season's. Null while the
    /// season is incomplete or when nothing later exists.</summary>
    public NextSeasonInfo? NextSeason()
    {
        if (!SeasonComplete)
            return null;

        var next = PackDiscovery.NextAfter(
            PackDiscovery.Discover(_environment.ResolvePackSearchRoots()), Pack.Season.Year);
        if (next?.Manifest is null || next.SeasonYear is not { } year)
            return null;

        return new NextSeasonInfo
        {
            PackDirectory = next.Directory,
            PackId = next.Manifest.PackId,
            PackName = next.Manifest.Name,
            SeasonYear = year,
            BridgedYears = Enumerable.Range(Pack.Season.Year + 1, year - Pack.Season.Year - 1).ToList(),
        };
    }

    /// <summary>Signs the accepted offer into the discovered next pack: builds the era
    /// transition plan from the persisted season-end states — the exact inputs replay's
    /// transition verification re-derives, so live and replay agree by construction — and
    /// starts the new season atomically via <see cref="CareerStore.StartNextSeason"/>.
    /// This session keeps pointing at the finished season afterwards; the shell reopens the
    /// career file, which now lands in the new season (<see cref="OpenCareer"/>).</summary>
    public void StartNextSeason(string teamId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamId);
        if (!SeasonComplete)
            throw new InvalidOperationException(
                "The season is not complete — finish every round before signing for the next era.");
        EnsureSeasonEnd();

        var accepted = StateStore.ReadOffers(_database, _seasonId).FirstOrDefault(o => o.Accepted);
        if (accepted is null)
            throw new InvalidOperationException(
                "No offer is accepted — accept an offer letter before signing for the next season.");
        if (!string.Equals(accepted.Terms.TeamId, teamId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"The accepted offer is from '{accepted.Terms.TeamId}', not '{teamId}' — " +
                "sign the offer you accepted.");

        var next = NextSeason()
            ?? throw new InvalidOperationException(
                "No next season pack was found — put a later-year season pack in the packs " +
                "folder beside the app or in Documents\\AMS2CareerCompanion\\Packs, then sign again.");
        var toPack = SeasonPackFiles.Read(next.PackDirectory).Parse();

        var driversEnd = StateStore.ReadDriverStates(_database, _seasonId, StateStore.StageEnd);
        var teamsEnd = StateStore.ReadTeamStates(_database, _seasonId, StateStore.StageEnd);
        var playerEnd = StateStore.ReadPlayerState(_database, _seasonId, StateStore.StageEnd)
            ?? throw new InvalidOperationException(
                "The season-end player state is missing — the season-end pipeline never ran.");

        var plan = EraTransition.Build(
            Pack, toPack, driversEnd, teamsEnd, playerEnd, accepted.Terms,
            new StreamFactory(MasterSeedU), _environment.Rules.AgingCurves,
            SimInputs().CanonRetirements);
        if (plan.ValidationErrors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", plan.ValidationErrors));

        CareerStore.StartNextSeason(_database, plan, toPack, NowUtc());
    }

    private static string? HeadlineText(string deltaJson)
    {
        try
        {
            using var document = JsonDocument.Parse(deltaJson);
            return document.RootElement.TryGetProperty("text", out var text) ? text.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The News feed (hub News tab): the season's <c>news.headline</c> journal rows,
    /// newest first, each paired with its round's <c>race.result</c> row so the Why? chip can
    /// state in plain language what produced it. Pure read-only projection over the journal —
    /// re-derivable byte-identically, no new persistence. (Increment 1; the generative
    /// multi-slot article grammar is a later slice.)</summary>
    public IReadOnlyList<NewsDispatch> ReadFeed()
    {
        var rows = JournalStore.ReadSeason(_database, _seasonId);

        // Index each round's player race.result delta so a headline can explain itself.
        var resultByRound = rows
            .Where(r => string.Equals(r.Phase, JournalPhases.RaceResult, StringComparison.Ordinal)
                        && string.Equals(r.Entity, "player", StringComparison.Ordinal)
                        && r.Round is not null)
            .GroupBy(r => r.Round!.Value)
            .ToDictionary(g => g.Key, g => g.Last().DeltaJson);

        var dispatches = new List<NewsDispatch>();
        foreach (var row in rows)
        {
            if (!string.Equals(row.Phase, JournalPhases.Headline, StringComparison.Ordinal))
                continue;
            if (HeadlineText(row.DeltaJson) is not { } text)
                continue;

            string why = row.Round is { } round && resultByRound.TryGetValue(round, out var resultDelta)
                ? WhyFromResult(resultDelta)
                : "";

            dispatches.Add(new NewsDispatch
            {
                Headline = text,
                SeasonYear = Pack.Season.Year,
                Round = row.Round,
                Kind = row.Round is null ? "season" : "race",
                WhyText = why,
            });
        }

        dispatches.Reverse(); // newest first
        return dispatches;
    }

    /// <summary>Turn a <c>race.result</c> delta (expectedFinish / actualFinish / dnf) into the
    /// Why? chip's plain sentence. Empty when the delta cannot be read.</summary>
    private static string WhyFromResult(string deltaJson)
    {
        try
        {
            using var document = JsonDocument.Parse(deltaJson);
            var root = document.RootElement;
            int? expected = root.TryGetProperty("expectedFinish", out var e) && e.ValueKind == JsonValueKind.Number
                ? e.GetInt32() : null;
            bool dnf = root.TryGetProperty("dnf", out var d) && d.ValueKind == JsonValueKind.True;

            if (dnf)
                return expected is { } exd ? $"You retired — the car was expected to finish P{exd}." : "You retired.";

            int? actual = root.TryGetProperty("actualFinish", out var a) && a.ValueKind == JsonValueKind.Number
                ? a.GetInt32() : null;
            if (actual is not { } ac || expected is not { } exp)
                return "";

            string vs = ac < exp ? $"beating your expected P{exp}"
                : ac > exp ? $"below your expected P{exp}"
                : $"matching your expected P{exp}";
            return $"You finished P{ac}, {vs}.";
        }
        catch (JsonException)
        {
            return "";
        }
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

    /// <summary>Every stored championship-round result, re-read from the raw envelopes
    /// (results are the source of truth; there is no cached standings state). The SAME
    /// envelope payload feeds the unified fold, so screen standings and journal standings
    /// can never disagree.</summary>
    private List<RoundResult> StoredRoundResults() =>
        ResultStore.ReadSeasonResults(_database, _seasonId)
            .Where(r => RoundByNumber(r.Round).Championship)
            .Select(r => r.ToRoundResult())
            .ToList();

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

        // Baseline provenance (NAMeS-first). Non-required with defaults so careers created
        // before the feature still deserialize (they are pack-baseline by definition).
        public string BaselineSource { get; init; } = BaselineSourcePack;
        public string? BaselineSourcePath { get; init; }
        public int BaselineImportedDrivers { get; init; }
        public int BaselinePackOnlyDrivers { get; init; }
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
