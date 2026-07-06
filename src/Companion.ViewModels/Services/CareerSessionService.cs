using System.Globalization;
using System.Text.Json;
using Companion.Ams2;
using Companion.Ams2.CustomAi;
using Companion.Ams2.Grid;
using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Determinism;
using Companion.Core.Grid;
using Companion.Core.Json;
using Companion.Core.News;
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

                // The player's character (Increment 4a): journal the creation INPUT row (the Why?
                // inspector reads it; replay excludes it as provenance) and seed it into the start
                // player state, which is where the fold reads it deterministically.
                if (request.Character is { } character)
                {
                    Execute(database.Connection, transaction,
                        "INSERT INTO journal (utc, season_id, round, phase, entity, delta_json, cause) " +
                        "VALUES (@utc, @season, NULL, @phase, 'player', @delta, 'career-created');",
                        ("@utc", nowUtc), ("@season", seasonId),
                        ("@phase", JournalPhases.PlayerCharacter),
                        ("@delta", JsonSerializer.Serialize(
                            new { name = character.Name, stats = character.Stats, perkIds = character.PerkIds, cpUnspent = character.CpUnspent },
                            CoreJson.Options)));
                }

                SeedStartStates(
                    database, seasonId, pack, playerDriverId, request.PlayerLiveryName,
                    request.Character, transaction);

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
        CharacterProfile? character,
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
            // The character (null for a character-free career, which stays byte-identical): a fresh
            // character starts at level 1 with 0 XP.
            Character = character,
            Level = character is null ? 0 : 1,
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

    /// <summary>The latest folded player state (the most recent round's, or the season-start state
    /// before any round) — the one source of the current character, level and XP.</summary>
    private PlayerCareerState? CurrentPlayerState()
    {
        var states = StateStore.ReadRoundPlayerStates(_database, _seasonId);
        return states.Count > 0
            ? states[^1].State.Player
            : StateStore.ReadPlayerState(_database, _seasonId, StateStore.StageStart);
    }

    /// <summary>The Driver dossier projection (character depth 3), or null when the career has no
    /// character or no character rules are loaded.</summary>
    public Companion.Core.Character.CharacterDossier? CharacterDossier()
    {
        if (_environment.RulesDirectory is null)
            return null;
        var player = CurrentPlayerState();
        if (player?.Character is not { } character)
            return null;
        var dossier = Companion.Core.Character.CharacterDossier.Build(
            character, player.Level, player.Xp, _environment.Rules.Character);
        // Reflect spends made this season but not yet applied at a transition.
        int pending = PendingSpends().Sum(s => s.Cost);
        return pending == 0 ? dossier : dossier with { CpUnspent = Math.Max(0, dossier.CpUnspent - pending) };
    }

    /// <summary>Between-season spends journaled this (not-yet-transitioned) season — pending until
    /// sign-and-continue applies them to the next season's character.</summary>
    private IReadOnlyList<CharacterSpend> PendingSpends() =>
        ReplayService.ReadCharacterSpends(_database, _seasonId);

    /// <summary>Character points available to spend right now: the folded pool minus this season's
    /// pending spends. 0 when the career has no character.</summary>
    public int AvailableCharacterCp()
    {
        var player = CurrentPlayerState();
        if (_environment.RulesDirectory is null || player?.Character is not { } character)
            return 0;
        return CharacterProgress.AvailableCp(character, player.Level, _environment.Rules.Character)
               - PendingSpends().Sum(s => s.Cost);
    }

    /// <summary>Records a between-season development spend (character depth 4): raise a stat one step
    /// or add a perk, charged to the available pool and journaled (applied at the next season's
    /// transition, re-derived identically on replay). Throws when there is no character, the spend is
    /// unaffordable, or the perk is already held.</summary>
    public void SpendCharacterPoint(CharacterSpend spend)
    {
        if (_environment.RulesDirectory is null)
            throw new InvalidOperationException("No character rules are loaded.");
        var player = CurrentPlayerState();
        if (player?.Character is not { } character)
            throw new InvalidOperationException("This career has no character to develop.");
        var rules = _environment.Rules.Character;

        // Derive the AUTHORITATIVE cost from the rules and never trust the caller's Cost. Otherwise a
        // crafted spend could buy a costed perk for 0 (or mint points with a negative cost), and
        // because the row is provenance-excluded it would replay byte-for-byte — baking the exploit
        // permanently into the career. The journaled row carries the derived cost, not spend.Cost.
        int cost;
        if (spend.Kind == "stat")
        {
            if (!IsKnownStat(rules, spend.Target))
                throw new InvalidOperationException($"Unknown stat '{spend.Target}'.");
            double step = rules.Levels.LevelGrants.StatStepValue;
            double cap = rules.Levels.LevelGrants.StatCapPerRating;
            int pendingSteps = PendingSpends().Count(s => s.Kind == "stat"
                && string.Equals(s.Target, spend.Target, StringComparison.Ordinal));
            double current = character.Stat(spend.Target) + (pendingSteps * step);
            if (current + step > cap + 1e-9)
                throw new InvalidOperationException("That stat is already at its maximum.");
            cost = rules.Levels.LevelGrants.StatStepCpCost;
        }
        else if (spend.Kind == "perk")
        {
            if (!rules.TryGetPerk(spend.Target, out var perk))
                throw new InvalidOperationException($"Unknown perk '{spend.Target}'.");
            // Between-season development is spend-only: a drawback (<=0-cost) perk is a creation-time
            // identity choice, not something you buy mid-career (and letting one refund CP would break
            // the earned-points model).
            if (perk.Cost <= 0)
                throw new InvalidOperationException("That perk can only be chosen at creation.");
            bool owned = character.PerkIds.Contains(spend.Target, StringComparer.Ordinal)
                || PendingSpends().Any(s => s.Kind == "perk"
                    && string.Equals(s.Target, spend.Target, StringComparison.Ordinal));
            if (owned)
                throw new InvalidOperationException("Your driver already has that perk.");
            cost = perk.Cost;
        }
        else
        {
            throw new InvalidOperationException($"Unknown development spend kind '{spend.Kind}'.");
        }

        if (cost > AvailableCharacterCp())
            throw new InvalidOperationException(
                $"That costs {cost} points, but only {AvailableCharacterCp()} are available.");

        using var transaction = _database.Connection.BeginTransaction();
        Execute(_database.Connection, transaction,
            "INSERT INTO journal (utc, season_id, round, phase, entity, delta_json, cause) " +
            "VALUES (@utc, @season, NULL, @phase, 'player', @delta, 'development');",
            ("@utc", NowUtc()), ("@season", _seasonId),
            ("@phase", JournalPhases.PlayerStatSpend),
            ("@delta", JsonSerializer.Serialize(
                new { kind = spend.Kind, target = spend.Target, cost }, CoreJson.Options)));
        transaction.Commit();
    }

    /// <summary>True when the id names a real talent or meta stat (guards a development spend against
    /// a crafted target that would otherwise inject a phantom entry into the stat map).</summary>
    private static bool IsKnownStat(CharacterRules rules, string statId) =>
        rules.Stats.TalentStats.Any(s => string.Equals(s.Id, statId, StringComparison.Ordinal))
        || rules.Stats.MetaStats.Any(s => string.Equals(s.Id, statId, StringComparison.Ordinal));

    /// <summary>The perks the driver can buy right now: positive-cost, not already owned or pending,
    /// and affordable from the current pool — cheapest first. Empty with no character or no points.</summary>
    public IReadOnlyList<PurchasablePerk> PurchasablePerks()
    {
        var player = CurrentPlayerState();
        if (_environment.RulesDirectory is null || player?.Character is not { } character)
            return [];
        int available = AvailableCharacterCp();
        if (available <= 0)
            return [];

        var rules = _environment.Rules.Character;
        var owned = new HashSet<string>(character.PerkIds, StringComparer.Ordinal);
        foreach (var spend in PendingSpends())
            if (spend.Kind == "perk")
                owned.Add(spend.Target);

        return rules.Perks
            .Where(p => p.Cost > 0 && p.Cost <= available && !owned.Contains(p.Id))
            .OrderBy(p => p.Cost)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => new PurchasablePerk
            {
                Id = p.Id,
                Name = p.Name,
                Category = p.Category,
                Cost = p.Cost,
                Benefits = PerkDescriber.Benefits(p),
                Drawbacks = PerkDescriber.Drawbacks(p),
            })
            .ToList();
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

    public IReadOnlyList<GridSeat> CurrentGrid()
    {
        if (SeasonComplete)
            return [];
        var seats = ResolveGrid(CurrentRoundNumber).Seats;
        // Show the player's chosen character name on their seat, not the historical driver they took
        // over. Display only — the seat's DriverId (what results score under) and the staged AMS2 file
        // (bound by livery) are untouched, so nothing about the sim or the AI file changes.
        return CharacterName() is { } name
            ? seats.Select(s => s.IsPlayer ? s with { DriverName = name } : s).ToList()
            : seats;
    }

    /// <summary>The player's driver id + character name for name-rendering screens, or null when the
    /// career has no named character.</summary>
    public (string DriverId, string DisplayName)? PlayerIdentity() =>
        CharacterName() is { } name ? (_playerDriverId, name) : null;

    /// <summary>Resolves the round grid, marking the player's seat when their entry covers
    /// this round (an entry's rounds range may exclude it — then the grid is all-AI). When the
    /// career carries a character, the player seat is patched from it here too — so the STAGED
    /// AMS2 file gets the perk car scalars and the briefing's expectation matches the fold (the
    /// fold applies the identical patch). A character-free career resolves an unchanged grid.</summary>
    private GridPlan ResolveGrid(int round)
    {
        var plan = RoundGridResolver.Resolve(Pack, round);
        if (plan.Seats.Any(s => string.Equals(s.Ams2LiveryName, _playerLiveryName, StringComparison.Ordinal)))
            plan = RoundGridResolver.Resolve(Pack, round,
                new PlayerSeat { Ams2LiveryName = _playerLiveryName, Character = CurrentCharacterPatch() });
        return plan;
    }

    private PlayerCharacterPatch? _characterPatch;
    private bool _characterPatchResolved;

    /// <summary>The career's character resolved into a grid patch, or null when it has no character
    /// (or no character rules are loaded). Invariant across the career (perk ids + stats are fixed
    /// at creation), so it is computed once and cached.</summary>
    private PlayerCharacterPatch? CurrentCharacterPatch()
    {
        if (_characterPatchResolved)
            return _characterPatch;
        _characterPatchResolved = true;

        var character = StateStore.ReadPlayerState(_database, _seasonId, StateStore.StageStart)?.Character;
        if (character is not null && _environment.RulesDirectory is not null)
        {
            var rules = _environment.Rules.Character;
            _characterPatch = new PlayerCharacterPatch
            {
                Profile = character,
                Modifiers = PerkResolver.Resolve(character.PerkIds, rules),
                Rules = rules,
            };
        }
        return _characterPatch;
    }

    /// <summary>The player's chosen character name for this season, or null when there is none — the
    /// display identity the news/standings use instead of the historical driver they replaced.</summary>
    private string? CharacterName()
    {
        string? name = StateStore.ReadPlayerState(_database, _seasonId, StateStore.StageStart)?.Character?.Name;
        return string.IsNullOrEmpty(name) ? null : name;
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
            // Sum every score this round: one for a single race, one per race for a two-race
            // weekend scored per session (Increment 2). A single-race round is unchanged.
            var points = standing?.RoundScores
                .Where(s => s.Round == scoredRound)
                .Aggregate(Rational.Zero, (acc, s) => acc + s.Points) ?? Rational.Zero;
            roundPoints.Add((driverId, points));
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
    /// <summary>The current round's weekend structure (null = single race). Additive read over
    /// the pinned pack round; every bundled pack reports null. (Increment 2.)</summary>
    public PackWeekend? CurrentWeekend() =>
        SeasonComplete ? null : RoundByNumber(CurrentRoundNumber).Weekend;

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
            QualifyingOrder = draft.QualifyingOrder,
            IsWet = draft.IsWet,
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
            CharacterRules = rules.Character,
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

        var simInputs = SimInputs();
        var plan = EraTransition.Build(
            Pack, toPack, driversEnd, teamsEnd, playerEnd, accepted.Terms,
            new StreamFactory(MasterSeedU), _environment.Rules.AgingCurves,
            simInputs.CanonRetirements,
            // Between-season development (character depth 4): the spends journaled this season are
            // applied to the character as it moves into the next season, re-derived identically on replay.
            ReplayService.ReadCharacterSpends(_database, _seasonId),
            simInputs.CharacterRules);
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
    /// newest first. Each race dispatch carries the Why? chip's plain sentence AND a full
    /// period-voiced <see cref="NewsDispatch.Body"/> generated by the data-driven article
    /// grammar (<see cref="NewsArticleBank"/>) from that round's facts. Pure read-only
    /// projection over the folded journal + standings snapshots — no new persistence, and the
    /// body is DETERMINISTIC (seeded on <c>masterSeed</c> + the round), so the same career
    /// renders byte-identical articles on every call and on replay.</summary>
    public IReadOnlyList<NewsDispatch> ReadFeed()
    {
        var rows = JournalStore.ReadSeason(_database, _seasonId);

        // Index each round's player race.result ROW (delta + cause) so a headline can explain
        // itself (Why? chip) and seed the generative article body.
        var resultByRound = rows
            .Where(r => string.Equals(r.Phase, JournalPhases.RaceResult, StringComparison.Ordinal)
                        && string.Equals(r.Entity, "player", StringComparison.Ordinal)
                        && r.Round is not null)
            .GroupBy(r => r.Round!.Value)
            .ToDictionary(g => g.Key, g => g.Last());

        // Richer race facts the thin race.result row lacks: winner + field size (from the raw
        // envelope) and the player's championship position/delta/leader (from the snapshots).
        // Materialized lazily so an empty feed (no headline rows) touches neither the raw store
        // nor the rules data — a quiet paddock does no work and forces no rules load.
        Dictionary<int, StoredRoundResult>? rawByRound = null;
        Dictionary<int, StandingsSnapshot>? snapshotsByOrdinal = null;
        NewsArticleBank? articles = null;

        var dispatches = new List<NewsDispatch>();
        foreach (var row in rows)
        {
            if (!string.Equals(row.Phase, JournalPhases.Headline, StringComparison.Ordinal))
                continue;
            if (HeadlineText(row.DeltaJson) is not { } text)
                continue;

            string why = "";
            string body = "";
            if (row.Round is { } round && resultByRound.TryGetValue(round, out var resultRow))
            {
                why = WhyFromResult(resultRow.DeltaJson);
                rawByRound ??= ResultStore.ReadSeasonResults(_database, _seasonId)
                    .ToDictionary(r => r.Round, r => r);
                snapshotsByOrdinal ??= AllSnapshots().ToDictionary(s => s.AfterRound);
                articles ??= _environment.Rules.NewsArticles;
                var facts = NewsFactsFor(round, resultRow, rawByRound, snapshotsByOrdinal);
                body = NewsArticleComposer.Compose(articles, facts, MasterSeedU) ?? "";
            }

            dispatches.Add(new NewsDispatch
            {
                Headline = text,
                SeasonYear = Pack.Season.Year,
                Round = row.Round,
                Kind = row.Round is null ? "season" : "race",
                WhyText = why,
                Body = body,
            });
        }

        dispatches.Reverse(); // newest first
        return dispatches;
    }

    /// <summary>Projects one race round's facts for the news grammar from already-folded state:
    /// the player's expected/actual finish + cause (the <c>race.result</c> row), the field's
    /// winner + size (the raw envelope), and the player's championship standing after the round
    /// (the snapshot for its championship ordinal). Every fact is optional — a template only
    /// fills the slots it names, so a thin round still yields a complete body.</summary>
    private NewsFacts NewsFactsFor(
        int round,
        JournalRow resultRow,
        IReadOnlyDictionary<int, StoredRoundResult> rawByRound,
        IReadOnlyDictionary<int, StandingsSnapshot> snapshotsByOrdinal)
    {
        var packRound = Pack.Season.Rounds.FirstOrDefault(r => r.Round == round);
        var grid = ResolveGrid(round);
        var playerSeat = grid.Seats.FirstOrDefault(s =>
            string.Equals(s.DriverId, _playerDriverId, StringComparison.Ordinal));

        int? expected = null;
        int? actual = null;
        bool dnf = false;
        try
        {
            using var document = JsonDocument.Parse(resultRow.DeltaJson);
            var rootEl = document.RootElement;
            if (rootEl.TryGetProperty("expectedFinish", out var e) && e.ValueKind == JsonValueKind.Number)
                expected = e.GetInt32();
            if (rootEl.TryGetProperty("actualFinish", out var a) && a.ValueKind == JsonValueKind.Number)
                actual = a.GetInt32();
            dnf = rootEl.TryGetProperty("dnf", out var d) && d.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            // A malformed delta simply leaves the finish facts null — the body degrades.
        }

        // Winner + field size from the raw envelope's race classification.
        string? winnerName = null;
        int? fieldSize = null;
        if (rawByRound.TryGetValue(round, out var raw))
        {
            var race = raw.ToRoundResult().Sessions
                .FirstOrDefault(s => s.Kind == SessionKind.Race);
            if (race is not null)
            {
                fieldSize = race.Entries.Count;
                string? winnerId = race.Entries
                    .Where(en => en.Status == FinishStatus.Classified && en.Position == 1)
                    .Select(en => en.DriverId)
                    .FirstOrDefault();
                winnerName = winnerId is null ? null : DriverDisplayName(grid, winnerId);
            }
        }

        // Championship standing after this round, keyed by its championship ordinal (snapshots
        // are ordinal-indexed; non-championship rounds simply have no snapshot facts).
        int? champPosition = null;
        int? champDelta = null;
        string? leaderName = null;
        bool playerLeads = false;
        if (packRound?.Championship ?? false)
        {
            int ordinal = ChampionshipOrdinal(round);
            if (snapshotsByOrdinal.TryGetValue(ordinal, out var snapshot))
            {
                champPosition = snapshot.Drivers
                    .FirstOrDefault(dr => string.Equals(dr.DriverId, _playerDriverId, StringComparison.Ordinal))
                    ?.Position;
                if (snapshotsByOrdinal.TryGetValue(ordinal - 1, out var prev))
                {
                    int? prevPos = prev.Drivers
                        .FirstOrDefault(dr => string.Equals(dr.DriverId, _playerDriverId, StringComparison.Ordinal))
                        ?.Position;
                    // Positive delta = climbed (a smaller position number).
                    if (champPosition is { } now && prevPos is { } was)
                        champDelta = was - now;
                }
                var leader = snapshot.Drivers.FirstOrDefault(dr => dr.Position == 1);
                leaderName = leader is null ? null : DriverDisplayName(grid, leader.DriverId);
                playerLeads = leader is not null
                    && string.Equals(leader.DriverId, _playerDriverId, StringComparison.Ordinal);
            }
        }

        return new NewsFacts
        {
            Phase = resultRow.Phase,
            Cause = resultRow.Cause,
            Year = Pack.Season.Year,
            Round = round,
            RaceName = packRound?.Name ?? grid.RoundName,
            PlayerName = CharacterName() ?? playerSeat?.DriverName ?? _playerDriverId,
            TeamName = playerSeat?.TeamName ?? "",
            PlayerFinish = actual,
            ExpectedFinish = expected,
            Dnf = dnf,
            WinnerName = winnerName,
            FieldSize = fieldSize,
            ChampionshipPosition = champPosition,
            ChampionshipDelta = champDelta,
            ChampionshipLeaderName = leaderName,
            PlayerLeadsChampionship = playerLeads,
        };
    }

    /// <summary>Display name for a driver id in a resolved grid, falling back to the pack's
    /// driver table then the raw id — so a name is always available for {winner}/{champLeader}.</summary>
    private string DriverDisplayName(GridPlan grid, string driverId) =>
        grid.Seats.FirstOrDefault(s => string.Equals(s.DriverId, driverId, StringComparison.Ordinal))?.DriverName
        ?? Pack.Drivers.FirstOrDefault(d => string.Equals(d.Id, driverId, StringComparison.Ordinal))?.Name
        ?? driverId;

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

    // ---------- "Why?" inspector (Increment 3, decisions 4 + 5) ----------

    /// <summary>The clickable-everywhere "Why?" inspector's causal chain (career-hub-design.md §5):
    /// walks this season's journal rows for <paramref name="entity"/> (narrowed to
    /// <paramref name="round"/> when given) and projects each relevant row's <c>deltaJson</c> into a
    /// labelled contribution row. Pure read-only projection over <see cref="JournalStore.ReadSeason"/>
    /// — no new persistence — and DETERMINISTIC: rows are read in journal <c>seq</c> order and every
    /// comparison is <see cref="StringComparer.Ordinal"/>, so the chain is byte-stable and identical
    /// on replay. The ordered-row shape is the format designed to accept perk/stat rows later
    /// (decision 5); it generalises the thin <see cref="WhyFromResult"/> chip into a full breakdown.</summary>
    public JournalChain JournalFor(string entity, int? round = null) =>
        BuildJournalChain(entity, round, _seasonId, Pack, _playerDriverId);

    /// <summary>The season-scoped walk (career-hub-design.md §4/§5, decision 18): resolves the season
    /// row whose year is <paramref name="seasonYear"/> and runs the SAME projection as the current-season
    /// <see cref="JournalFor(string,int?)"/> over THAT season's journal — resolving the entity name and
    /// player seat against that season's pinned pack, so a finished earlier season's numbers read
    /// correctly. When the year is the current season this is byte-identical to the current-season walk.
    /// No matching season year returns the empty chain (a graceful no-op, never a throw). A DISTINCT
    /// name (not a <see cref="JournalFor(string,int?)"/> overload) keeps int-literal round callers on
    /// the current-season walk.</summary>
    public JournalChain JournalForSeason(string entity, int seasonYear, int? round = null)
    {
        if (string.IsNullOrEmpty(entity))
            return JournalChain.Empty;

        // First (oldest) season row matching the year — years are unique per career in v1, but
        // guard deterministically anyway (ReadSeasons is ordered by id, oldest first).
        var season = CareerStore.ReadSeasons(_database)
            .FirstOrDefault(s => s.Year == seasonYear);
        if (season is null)
            return JournalChain.Empty;

        var seasonPack = SeasonPackFor(season);
        string playerDriverId = PlayerDriverIdFor(season, seasonPack);
        return BuildJournalChain(entity, round, season.Id, seasonPack, playerDriverId);
    }

    /// <summary>The one shared "Why?" projection both the current-season and season-scoped paths run
    /// (so they cannot drift): walks <paramref name="seasonId"/>'s journal rows for
    /// <paramref name="entity"/> (narrowed to <paramref name="round"/> when given), projecting each into
    /// a labelled contribution row. Pure read-only projection over <see cref="JournalStore.ReadSeason"/>
    /// — no new persistence — and DETERMINISTIC: rows are read in journal <c>seq</c> order and every
    /// comparison is <see cref="StringComparer.Ordinal"/>, so the chain is byte-stable and identical on
    /// replay. <paramref name="seasonPack"/> + <paramref name="playerDriverId"/> resolve the panel title
    /// and entity name against THAT season's pinned pack. The ordered-row shape is the format designed to
    /// accept perk/stat rows later (decision 5); it generalises the thin <see cref="WhyFromResult"/> chip
    /// into a full breakdown.</summary>
    private JournalChain BuildJournalChain(
        string entity, int? round, long seasonId, SeasonPack seasonPack, string playerDriverId)
    {
        if (string.IsNullOrEmpty(entity))
            return JournalChain.Empty;

        // Ordered by seq already (ReadSeason's ORDER BY seq) — the deterministic walk order.
        var rows = JournalStore.ReadSeason(_database, seasonId)
            .Where(r => string.Equals(r.Entity, entity, StringComparison.Ordinal)
                        && !DataJournalPhases.IsProvenance(r.Phase)
                        && (round is null || r.Round == round))
            .ToList();

        var contributions = new List<JournalContribution>(rows.Count);
        foreach (var row in rows)
        {
            if (ContributionFor(row) is { } contribution)
                contributions.Add(contribution);
        }

        if (contributions.Count == 0)
            return JournalChain.Empty;

        return new JournalChain
        {
            Entity = entity,
            Round = round,
            Title = InspectorTitle(entity, round, rows, seasonPack, playerDriverId),
            Contributions = contributions,
            Summary = InspectorSummary(entity, rows),
        };
    }

    /// <summary>Projects one journal row into a labelled contribution row, or null when the row's
    /// phase carries no inspector-relevant number. Every branch reads a known sim delta shape
    /// (see <c>RoundUpdate</c>/<c>SeasonEndPipeline</c>); an unreadable delta degrades to a bare
    /// labelled row rather than throwing, so a future phase never breaks the walk.</summary>
    private static JournalContribution? ContributionFor(JournalRow row)
    {
        JsonElement root;
        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(row.DeltaJson);
            root = document.RootElement;
        }
        catch (JsonException)
        {
            document?.Dispose();
            return null;
        }

        using (document)
        {
            switch (row.Phase)
            {
                case JournalPhases.RaceResult:
                {
                    int? expected = IntOrNull(root, "expectedFinish");
                    int? actual = IntOrNull(root, "actualFinish");
                    // A DNF serialises its cause as a camelCase enum STRING (e.g. "mechanical"),
                    // or true on legacy rows; a completed race carries null. Either non-null form
                    // is a retirement.
                    bool dnf = root.TryGetProperty("dnf", out var d)
                               && d.ValueKind is JsonValueKind.True or JsonValueKind.String;
                    string detail = dnf
                        ? (expected is { } ex ? $"Retired — the car was expected to finish P{ex}." : "Retired.")
                        : (actual is { } a && expected is { } e
                            ? $"Finished P{a} against an expected P{e}."
                            : "Race result recorded.");
                    return new JournalContribution
                    {
                        Label = "Expected finish",
                        Detail = detail,
                        Value = dnf ? "DNF" : (actual is { } av ? $"P{av}" : null),
                        SourceSeq = row.Seq,
                    };
                }

                case JournalPhases.PlayerOpi:
                    // Per-round OPI rows carry {from,to}; the season-final row carries {value}.
                    if (FromTo(root) is { } opi)
                        return NumericRow("OPI", opi.From, opi.To, row.Seq, "0.00", signed: true);
                    return DoubleOrNull(root, "value") is { } opiValue
                        ? new JournalContribution
                        {
                            Label = "OPI",
                            Detail = "Season-final overperformance index.",
                            Value = Signed(opiValue, "0.00"),
                            SourceSeq = row.Seq,
                        }
                        : null;

                case JournalPhases.PlayerReputation:
                {
                    var ft = FromTo(root);
                    if (ft is not { } rep)
                        return null;
                    bool beatTeammate = root.TryGetProperty("beatTeammate", out var bt)
                                        && bt.ValueKind == JsonValueKind.True;
                    int? champPos = IntOrNull(root, "championshipPosition");
                    string detail = champPos is { } cp
                        ? $"Season-final reputation (championship P{cp})."
                        : beatTeammate
                            ? "Reputation moved this round — you beat your teammate."
                            : "Reputation moved this round.";
                    return NumericRow("Reputation", rep.From, rep.To, row.Seq, "0.#", signed: false, detail);
                }

                case JournalPhases.PlayerPaceAnchor:
                {
                    var ft = FromTo(root);
                    if (ft is not { } anchor)
                        return null;
                    int? slider = IntOrNull(root, "recommendedSlider");
                    string detail = slider is { } s
                        ? $"Pace anchor recalibrated — next round's suggested Opponent Skill {s}%."
                        : "Pace anchor recalibrated from your race pace.";
                    return NumericRow("Pace anchor", anchor.From, anchor.To, row.Seq, "0.###", signed: false, detail);
                }

                case JournalPhases.PlayerQualiAnchor:
                {
                    var ft = FromTo(root);
                    if (ft is not { } quali)
                        return null;
                    int? qualiPos = IntOrNull(root, "qualiPosition");
                    string detail = qualiPos is { } p
                        ? $"Qualifying anchor recalibrated from a P{p} grid slot."
                        : "Qualifying anchor recalibrated.";
                    return NumericRow("Qualifying anchor", quali.From, quali.To, row.Seq, "0.###", signed: false, detail);
                }

                case JournalPhases.PlayerExperience:
                    return new JournalContribution
                    {
                        Label = "Seasons completed",
                        Detail = "A season was added to your record.",
                        Value = IntOrNull(root, "to")?.ToString(CultureInfo.InvariantCulture),
                        SourceSeq = row.Seq,
                    };

                case JournalPhases.Championship:
                    return new JournalContribution
                    {
                        Label = "Championship",
                        Detail = StringOrNull(root, "points") is { } pts
                            ? $"Final championship standing on {pts} points."
                            : "Final championship standing.",
                        Value = IntOrNull(root, "position") is { } pos ? $"P{pos}" : null,
                        SourceSeq = row.Seq,
                    };

                case JournalPhases.OfferExtended:
                    return new JournalContribution
                    {
                        Label = "Contract offer",
                        Detail = $"A tier-{IntOrNull(root, "tier")?.ToString(CultureInfo.InvariantCulture) ?? "?"} " +
                                 "seat was offered for next season.",
                        Value = DoubleOrNull(root, "score") is { } sc ? sc.ToString("0.##", CultureInfo.InvariantCulture) : null,
                        SourceSeq = row.Seq,
                    };

                case JournalPhases.TeamTier:
                {
                    int? from = IntOrNull(root, "from");
                    int? to = IntOrNull(root, "to");
                    return new JournalContribution
                    {
                        Label = "Budget tier",
                        Detail = from is { } f && to is { } t
                            ? $"Team budget tier moved {f} → {t} on the season's results."
                            : "Team budget tier reassessed.",
                        Value = to is { } tv ? tv.ToString(CultureInfo.InvariantCulture) : null,
                        SourceSeq = row.Seq,
                    };
                }

                case JournalPhases.DriverAging:
                    return new JournalContribution
                    {
                        Label = "Aging",
                        Detail = "Skills drifted with age between seasons.",
                        Value = IntOrNull(root, "age") is { } age ? $"age {age}" : null,
                        SourceSeq = row.Seq,
                    };

                case JournalPhases.Retirement:
                    return new JournalContribution
                    {
                        Label = "Retirement",
                        Detail = $"Retired from the grid ({row.Cause}).",
                        Value = null,
                        SourceSeq = row.Seq,
                    };

                case JournalPhases.Headline:
                    return StringOrNull(root, "text") is { } text
                        ? new JournalContribution
                        {
                            Label = "Headline",
                            Detail = text,
                            Value = null,
                            SourceSeq = row.Seq,
                        }
                        : null;

                default:
                    return null;
            }
        }
    }

    /// <summary>A {from,to} numeric contribution row: the value is the new state ("to"), the detail
    /// carries the movement. <paramref name="signed"/> renders the value with an explicit sign
    /// (OPI); otherwise absolute (reputation/anchors).</summary>
    private static JournalContribution NumericRow(
        string label, double from, double to, long seq, string format, bool signed, string? detail = null)
    {
        double delta = Math.Round(to - from, 4);
        string movement = delta > 0 ? $"+{delta.ToString(format, CultureInfo.InvariantCulture)}"
            : delta < 0 ? delta.ToString(format, CultureInfo.InvariantCulture)
            : "no change";
        return new JournalContribution
        {
            Label = label,
            Detail = detail ?? $"{label} moved {from.ToString(format, CultureInfo.InvariantCulture)} → " +
                     $"{to.ToString(format, CultureInfo.InvariantCulture)} ({movement}).",
            Value = signed ? Signed(to, format) : to.ToString(format, CultureInfo.InvariantCulture),
            SourceSeq = seq,
        };
    }

    private static string Signed(double value, string format) =>
        value > 0 ? $"+{value.ToString(format, CultureInfo.InvariantCulture)}"
            : value.ToString(format, CultureInfo.InvariantCulture);

    private static (double From, double To)? FromTo(JsonElement root) =>
        DoubleOrNull(root, "from") is { } from && DoubleOrNull(root, "to") is { } to
            ? (from, to)
            : null;

    private static int? IntOrNull(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number ? el.GetInt32() : null;

    private static double? DoubleOrNull(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number ? el.GetDouble() : null;

    private static string? StringOrNull(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    /// <summary>The inspector panel header: names the entity and, when the walk is a single round,
    /// the player's finish that round (the headline number the click walked back from). The season's
    /// own pinned pack + player driver id resolve the name + year, so a finished season titles
    /// correctly (not against the current season).</summary>
    private string InspectorTitle(
        string entity, int? round, IReadOnlyList<JournalRow> rows, SeasonPack seasonPack, string playerDriverId)
    {
        string who = InspectorEntityName(entity, seasonPack, playerDriverId);
        if (round is not { } r)
            return $"Why — {who}, {seasonPack.Season.Year}";

        // For a single-round player walk, lead with the finishing position if the race.result row
        // carries one — the number the user most likely clicked.
        var resultRow = rows.FirstOrDefault(x =>
            string.Equals(x.Phase, JournalPhases.RaceResult, StringComparison.Ordinal));
        if (resultRow is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(resultRow.DeltaJson);
                var el = doc.RootElement;
                if (el.TryGetProperty("dnf", out var d) && d.ValueKind == JsonValueKind.True)
                    return $"Why DNF — {who}, Round {r}";
                if (IntOrNull(el, "actualFinish") is { } actual)
                    return $"Why P{actual} — {who}, Round {r}";
            }
            catch (JsonException)
            {
                // Fall through to the plain round title.
            }
        }
        return $"Why — {who}, Round {r}";
    }

    /// <summary>Resolves a journal entity id to a display name: the player's own seat, else the
    /// season pack's driver or team table, else the raw id — so the inspector never shows a bare id.
    /// The season's pinned pack + player driver id keep a finished season's names correct (that
    /// season's seat, that season's roster).</summary>
    private static string InspectorEntityName(string entity, SeasonPack seasonPack, string playerDriverId)
    {
        if (string.Equals(entity, "player", StringComparison.Ordinal)
            || string.Equals(entity, playerDriverId, StringComparison.Ordinal))
            return "You";
        var driver = seasonPack.Drivers.FirstOrDefault(d => string.Equals(d.Id, entity, StringComparison.Ordinal));
        if (driver is not null)
            return driver.Name;
        var team = seasonPack.Teams.FirstOrDefault(t => string.Equals(t.Id, entity, StringComparison.Ordinal));
        return team?.Name ?? entity;
    }

    /// <summary>The chain's plain-language summary: the player's race.result sentence when the walk
    /// covers a player round, else empty — the rows carry the rest. Reuses the shipped
    /// <see cref="WhyFromResult"/> chip for a classified finish, and adds the retirement sentence the
    /// chip omits (the chip predates the enum-string DNF cause), so a DNF walk still reads plainly
    /// without changing the existing News chip's output.</summary>
    private static string InspectorSummary(string entity, IReadOnlyList<JournalRow> rows)
    {
        if (!string.Equals(entity, "player", StringComparison.Ordinal))
            return "";
        var resultRow = rows.LastOrDefault(x =>
            string.Equals(x.Phase, JournalPhases.RaceResult, StringComparison.Ordinal));
        if (resultRow is null)
            return "";

        string why = WhyFromResult(resultRow.DeltaJson);
        if (why.Length > 0)
            return why;

        // WhyFromResult returns "" for a DNF whose cause is an enum string; render it here.
        try
        {
            using var document = JsonDocument.Parse(resultRow.DeltaJson);
            var root = document.RootElement;
            bool dnf = root.TryGetProperty("dnf", out var d)
                       && d.ValueKind is JsonValueKind.True or JsonValueKind.String;
            if (dnf)
                return IntOrNull(root, "expectedFinish") is { } expected
                    ? $"You retired — the car was expected to finish P{expected}."
                    : "You retired.";
        }
        catch (JsonException)
        {
            // Fall through to the empty summary.
        }
        return "";
    }

    // ---------- history / scrapbook (Increment 3) ----------

    /// <summary>The total-recall History projection (career-hub-design.md §4 / decision 18): a
    /// lineage-aware card per season plus a records book aggregated across every season's
    /// per-race classification. Pure read-only projection over the SAME stored raw results,
    /// folded player states and journal every other lens reads — no new persistence, and
    /// re-derivable byte-identically. A multi-season career (M6 era transitions) walks every
    /// <c>season</c> row in career order, resolving each season's pinned pack, player seat,
    /// standings and headlines independently.</summary>
    public CareerTimeline CareerTimeline()
    {
        var seasons = CareerStore.ReadSeasons(_database);
        if (seasons.Count == 0)
            return Companion.ViewModels.Services.CareerTimeline.Empty;

        var cards = new List<CareerSeasonCard>(seasons.Count);
        var races = new List<PlayerRace>(); // every applied race, oldest first, for the records book
        double totalPoints = 0.0;           // player's counted points, summed over every season

        foreach (var season in seasons)
        {
            var seasonPack = SeasonPackFor(season);
            string playerDriverId = PlayerDriverIdFor(season, seasonPack);
            var driverNames = seasonPack.Drivers.ToDictionary(d => d.Id, d => d.Name, StringComparer.Ordinal);
            var scoring = ChampionshipCalendar.ResolveScoring(seasonPack);

            var storedResults = ResultStore.ReadSeasonResults(_database, season.Id)
                .Where(r => seasonPack.Season.Rounds
                    .FirstOrDefault(round => round.Round == r.Round)?.Championship ?? false)
                .ToList();
            int roundsApplied = storedResults.Count;
            int championshipRounds = seasonPack.Season.Rounds.Count(r => r.Championship);

            // Per-race player classification (finishing position + status) — the records-book
            // source. Ordered by round so streaks are chronological across seasons.
            foreach (var stored in storedResults.OrderBy(r => r.Round))
            {
                var race = stored.ToRoundResult().Sessions
                    .FirstOrDefault(s => s.Kind == SessionKind.Race);
                var line = race?.Entries.FirstOrDefault(e => string.Equals(e.DriverId, playerDriverId, StringComparison.Ordinal));
                races.Add(new PlayerRace(
                    line is { Status: FinishStatus.Classified, Position: { } p } ? p : null));
            }

            // Final standings (from the stored results): the player's position + the champion.
            StandingsSnapshot? finalSnapshot = storedResults.Count == 0
                ? null
                : StandingsEngine.ComputeSeason(scoring, storedResults.Select(r => r.ToRoundResult()).ToList()).Snapshots[^1];
            var playerStanding = finalSnapshot?.Drivers
                .FirstOrDefault(d => string.Equals(d.DriverId, playerDriverId, StringComparison.Ordinal));
            int? playerPosition = playerStanding?.Position;
            if (playerStanding is not null)
                totalPoints += playerStanding.CountedPoints.ToDouble();
            var champion = finalSnapshot?.Drivers.FirstOrDefault(d => d.Position == 1);
            string? championName = champion is null
                ? null
                : driverNames.GetValueOrDefault(champion.DriverId, champion.DriverId);
            bool playerIsChampion = champion is not null
                && string.Equals(champion.DriverId, playerDriverId, StringComparison.Ordinal);

            bool complete = string.Equals(season.Status, SeasonStatus.Complete, StringComparison.Ordinal);
            var endState = complete ? StateStore.ReadPlayerState(_database, season.Id, StateStore.StageEnd) : null;

            var headlines = JournalStore.ReadSeason(_database, season.Id)
                .Where(r => string.Equals(r.Phase, JournalPhases.Headline, StringComparison.Ordinal))
                .Select(r => HeadlineText(r.DeltaJson))
                .OfType<string>()
                .ToList();

            cards.Add(new CareerSeasonCard
            {
                SeasonYear = season.Year,
                PlayerPosition = playerPosition,
                RoundsApplied = roundsApplied,
                RoundCount = championshipRounds,
                IsComplete = complete,
                FinalReputation = endState?.Reputation,
                FinalOpi = endState?.Opi,
                ChampionName = championName,
                PlayerIsChampion = playerIsChampion,
                Headlines = headlines,
            });
        }

        return new CareerTimeline
        {
            Seasons = cards,
            Records = AggregateRecords(cards, races, totalPoints),
        };
    }

    /// <summary>One applied race from the player's viewpoint: the finishing position when the
    /// player was classified, else null (retired / disqualified / not classified).</summary>
    private readonly record struct PlayerRace(int? Position);

    /// <summary>Rolls the per-race classification and per-season cards into the career records
    /// book: best finish, win/podium counts + longest streaks, total points, championships and
    /// seasons raced.</summary>
    private static CareerRecordsBook AggregateRecords(
        IReadOnlyList<CareerSeasonCard> cards, IReadOnlyList<PlayerRace> races, double totalPoints)
    {
        int? bestFinish = null;
        int wins = 0, podiums = 0;
        int winStreak = 0, podiumStreak = 0, longestWinStreak = 0, longestPodiumStreak = 0;

        foreach (var race in races)
        {
            if (race.Position is { } pos)
            {
                if (bestFinish is null || pos < bestFinish)
                    bestFinish = pos;
                if (pos == 1)
                    wins++;
                if (pos <= 3)
                    podiums++;
            }

            bool isWin = race.Position == 1;
            bool isPodium = race.Position is { } p && p <= 3;
            winStreak = isWin ? winStreak + 1 : 0;
            podiumStreak = isPodium ? podiumStreak + 1 : 0;
            longestWinStreak = Math.Max(longestWinStreak, winStreak);
            longestPodiumStreak = Math.Max(longestPodiumStreak, podiumStreak);
        }

        int seasonsRaced = cards.Count(c => c.RoundsApplied > 0);
        int championships = cards.Count(c => c.PlayerIsChampion);

        return new CareerRecordsBook
        {
            BestFinish = bestFinish,
            Wins = wins,
            Podiums = podiums,
            TotalPoints = totalPoints,
            Championships = championships,
            SeasonsRaced = seasonsRaced,
            LongestWinStreak = longestWinStreak,
            LongestPodiumStreak = longestPodiumStreak,
        };
    }

    /// <summary>The pinned pack a career season simulates against: the CURRENT season reuses the
    /// already-loaded <see cref="Pack"/>; older seasons rehydrate from their own pinned blob.</summary>
    private SeasonPack SeasonPackFor(SeasonRecord season) =>
        season.Id == _seasonId
            ? Pack
            : PinnedPackEnvelope.LoadSeasonPack(
                CareerStore.ReadPinnedPack(_database, season.PackId, season.PackVersion).PackJson);

    /// <summary>The player's driver id in a given season: the current session already knows its
    /// own; an older season resolves the seat from that season's start-state livery (the seat the
    /// player took), falling back to the current id when a legacy state omits it.</summary>
    private string PlayerDriverIdFor(SeasonRecord season, SeasonPack seasonPack)
    {
        if (season.Id == _seasonId)
            return _playerDriverId;

        string? livery = StateStore.ReadPlayerState(_database, season.Id, StateStore.StageStart)?.LiveryName;
        if (livery is { Length: > 0 })
        {
            try
            {
                return ResolvePlayerDriverId(seasonPack, livery);
            }
            catch (InvalidOperationException)
            {
                // A livery the season's pack no longer offers: fall through to the current id.
            }
        }
        return _playerDriverId;
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

        // Race 0 is the draft's own classification; a two-race weekend adds the rest (Increment 2).
        int raceCount = 1 + (draft.AdditionalRaces?.Count ?? 0);
        bool perSession = raceCount > 1;
        var weekendRaces = packRound.Weekend?.Races;

        var sessions = new List<SessionResult>(raceCount)
        {
            // Bind a per-session table ONLY when the round actually scores per session — a
            // single race keeps the null table so its per-round selection (incl. the 1961
            // constructors override) is byte-identical to the shipped path.
            RaceSession(draft.Classified, draft.DidNotFinish, draft.Disqualified, teamByDriver,
                perSession ? PerSessionTable(weekendRaces, 0) : null),
        };
        if (draft.AdditionalRaces is { Count: > 0 } extras)
        {
            for (int i = 0; i < extras.Count; i++)
                sessions.Add(RaceSession(extras[i].Classified, extras[i].DidNotFinish, extras[i].Disqualified,
                    teamByDriver, PerSessionTable(weekendRaces, i + 1)));
        }

        return new RoundResult
        {
            Round = ChampionshipOrdinal(roundNumber),
            CountsForConstructors = rules.CountsForConstructors,
            PointsFactor = rules.PointsFactor,
            AlternateRaceTableId = rules.AlternateRaceTableId,
            PerSessionScoring = perSession,
            Sessions = sessions,
        };
    }

    /// <summary>The authored points table for race index <paramref name="index"/> from the round's
    /// weekend (<c>null</c> when the round runs no weekend or leaves the race's table unset — then
    /// the engine's per-round selection applies).</summary>
    private static string? PerSessionTable(IReadOnlyList<PackWeekendRace>? races, int index) =>
        races is not null && index < races.Count ? races[index].PointsTable : null;

    /// <summary>Maps one race's classification onto the engine's session shape: classified drivers
    /// in list order (index 0 = P1), DNF → Retired, DSQ → Disqualified, constructors from the round
    /// grid's seats, plus the authored per-session points table (null = the engine's default).</summary>
    private static SessionResult RaceSession(
        IReadOnlyList<string> classified,
        IReadOnlyDictionary<string, string> didNotFinish,
        IReadOnlyList<string> disqualified,
        IReadOnlyDictionary<string, string> teamByDriver,
        string? pointsTableId)
    {
        var entries = new List<ClassifiedEntry>();
        for (int i = 0; i < classified.Count; i++)
        {
            entries.Add(new ClassifiedEntry
            {
                DriverId = classified[i],
                ConstructorId = teamByDriver.GetValueOrDefault(classified[i]),
                Position = i + 1,
                Status = FinishStatus.Classified,
            });
        }
        foreach (string driverId in didNotFinish.Keys)
        {
            entries.Add(new ClassifiedEntry
            {
                DriverId = driverId,
                ConstructorId = teamByDriver.GetValueOrDefault(driverId),
                Status = FinishStatus.Retired,
            });
        }
        foreach (string driverId in disqualified)
        {
            entries.Add(new ClassifiedEntry
            {
                DriverId = driverId,
                ConstructorId = teamByDriver.GetValueOrDefault(driverId),
                Status = FinishStatus.Disqualified,
            });
        }

        return new SessionResult { Kind = SessionKind.Race, Entries = entries, PointsTableId = pointsTableId };
    }

    private void ValidateDraft(ResultDraft draft, int roundNumber)
    {
        var gridDrivers = RoundGridResolver.Resolve(Pack, roundNumber).Seats
            .Select(s => s.DriverId)
            .ToHashSet(StringComparer.Ordinal);

        // Each race is validated on its own: a driver legitimately appears in EVERY race of a
        // two-race weekend, so duplicate detection is per race, not across the round.
        ValidateRace(draft.Classified, draft.DidNotFinish.Keys, draft.Disqualified, gridDrivers, roundNumber);
        if (draft.AdditionalRaces is { } extras)
        {
            foreach (var race in extras)
                ValidateRace(race.Classified, race.DidNotFinish.Keys, race.Disqualified, gridDrivers, roundNumber);
        }
    }

    private static void ValidateRace(
        IReadOnlyList<string> classified,
        IEnumerable<string> didNotFinish,
        IReadOnlyList<string> disqualified,
        HashSet<string> gridDrivers,
        int roundNumber)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string driverId in classified.Concat(didNotFinish).Concat(disqualified))
        {
            if (!seen.Add(driverId))
                throw new ArgumentException(
                    $"Driver '{driverId}' appears more than once in the round-{roundNumber} result draft.",
                    nameof(classified));
            if (!gridDrivers.Contains(driverId))
                throw new ArgumentException(
                    $"Driver '{driverId}' is not in the round-{roundNumber} grid.", nameof(classified));
        }
    }

    /// <summary>Every distinct driver named across all of the round's races — one entry per driver
    /// for the confirm screen's round-points list, even when they appear in both races.</summary>
    private static IEnumerable<string> DraftParticipants(ResultDraft draft) =>
        draft.Classified
            .Concat(draft.DidNotFinish.Keys)
            .Concat(draft.Disqualified)
            .Concat(draft.AdditionalRaces?
                .SelectMany(r => r.Classified.Concat(r.DidNotFinish.Keys).Concat(r.Disqualified))
                ?? [])
            .Distinct(StringComparer.Ordinal);

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
