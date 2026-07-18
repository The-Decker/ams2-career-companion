using System.Globalization;
using System.Text.Json;
using System.IO;
using Companion.Ams2;
using Companion.Ams2.CustomAi;
using Companion.Ams2.Grid;
using Companion.Ams2.Preflight;
using Companion.Ams2.Scenarios;
using Companion.Ams2.Skins;
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
/// - The current round is derived state, max(applied round) + 1, so Apply advancing the
///   season needs no extra bookkeeping row.
/// - Staging is backup-first and aborts (Success=false) on any preflight ERROR; a missing
///   AMS2 install degrades to a failed outcome with a clear message, never a crash.
/// </summary>
public sealed partial class CareerSessionService : ICareerSession, IForceStaging, IExplicitGridApply, IAms2GameLaunch, IAiFileRestore, IDisposable
{
    /// <summary><see cref="BaselineSource"/> value: the pinned baseline is pack-authored.</summary>
    public const string BaselineSourcePack = "pack";

    /// <summary><see cref="BaselineSource"/> value: the pinned baseline imported the user's
    /// installed AI file at creation (NAMeS-first, locked decision #7).</summary>
    public const string BaselineSourceInstalledAiFile = "installedAiFile";

    private readonly CareerDatabase _database;
    private readonly CareerEnvironment _environment;
    private readonly long _seasonId;
    /// <summary>The CURRENT season's real calendar year (from its season record). Equals
    /// <c>Pack.Season.Year</c> for every ordinary season, but runs ahead of it for a CARRYOVER
    /// season, the same car reused for a later year, so this, not the pack's nominal year, is the
    /// season's year for display and for computing the next year.</summary>
    private readonly int _seasonYear;
    private readonly int _firstSeasonYear;
    /// <summary>The 1-based ordinal of the current season within the career (season 1, 2, … 17) —
    /// the position of <see cref="_seasonId"/> in the id-ordered season list. Drives the SMGP calendar
    /// variety, the per-season DNQ re-roll, and the "SEASON n / 17" campaign display. DISPLAY/PINNED —
    /// never a fold input (the fold keys off the round, not the ordinal).</summary>
    private readonly int _seasonOrdinal;
    private readonly string _careerName;
    private readonly string _playerLiveryName;
    private readonly string _playerDriverId;
    private readonly int _playerFirstSeasonAge;
    private readonly SeasonScoringDefinition _scoringDefinition;
    /// <summary>The authored SMGP car behind each fixed AMS2 livery. Captured from the pinned pack
    /// before season-to-season driver reshuffles so display art follows the physical car/seat rather
    /// than whichever driver currently occupies it. Display-only; never enters staging or the fold.</summary>
    private readonly IReadOnlyDictionary<string, string> _gridCarArtKeyByLivery;

    /// <summary>The career-wide mortality mode (Off / Normal / Hardcore), read from the <c>career</c>
    /// table at create/open. Gates the Normal-only save &amp; reload surface. (Slice 1.)</summary>
    private readonly MortalityMode _mortality;

    /// <summary>Set once a Hardcore death has physically DELETED this career's file (Slice 3): the session
    /// is then spent, the shell must show the permadeath screen and never touch the session again.</summary>
    private bool _careerFileDeleted;

    /// <summary>The death-screen model (Slice 5), memoised on first read after a death, and, on a Hardcore
    /// death, captured just BEFORE the file is deleted so it renders with no DB. Null while the driver
    /// lives. Immutable once the driver is dead (a dead career takes no more rounds).</summary>
    private DeathScreenModel? _deathScreen;

    /// <summary>The bankruptcy game-over model (economy §7), memoised on first read after the team
    /// folds. Bankruptcy never deletes the career file, so it always builds live from the intact DB.
    /// Null while solvent (and for every non-economy career).</summary>
    private BankruptcyScreenModel? _bankruptcyScreen;

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
        string baselineSource,
        MortalityMode mortality)
    {
        BaselineSource = baselineSource;
        _mortality = mortality;
        _database = database;
        _environment = environment;
        CareerFilePath = careerFilePath;
        _seasonId = seasonId;
        _gridCarArtKeyByLivery = string.Equals(
                pack.Manifest.CareerStyle,
                Companion.Core.Smgp.SmgpRules.CareerStyle,
                StringComparison.Ordinal)
            ? pack.Entries
                .GroupBy(entry => entry.Ams2LiveryName, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().DriverId,
                    StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        // The season's real year (carryover-aware): the season row exists by now on both the
        // create and open paths. Fall back to the pack year only if it is somehow absent. Capture
        // the FIRST season's year too, so the driver's current age = created age + seasons since.
        var allSeasons = CareerStore.ReadSeasons(database);
        // SMGP season 2+ can run a seeded per-season calendar (venues shuffled, fresh weather;
        // Monaco stays the finale). Race names and pre-race condition inputs can reach derived rows,
        // so this is a real fold input: the default-omitted start-state gate selects the SAME pure
        // transform on live and replay. Legacy careers without the gate keep the authored calendar.
        int seasonOrdinal = 1;
        int seasonIndex = 0;
        for (int i = 0; i < allSeasons.Count; i++)
            if (allSeasons[i].Id == seasonId) { seasonIndex = i; seasonOrdinal = i + 1; break; }
        _seasonOrdinal = seasonOrdinal;
        var startSmgp = StateStore.ReadPlayerState(database, seasonId, StateStore.StageStart)?.Smgp;
        var variedPack = startSmgp?.PerSeasonVariety == true
            ? Companion.Core.Smgp.SmgpSeasonVariety.ForSeason(pack, seasonOrdinal, masterSeed)
            : pack;
        if (startSmgp?.StandingsReshuffle == true && seasonIndex > 0 &&
            PreviousFinalStandings(database, allSeasons[seasonIndex - 1], pack) is { } previousFinal)
        {
            variedPack = Companion.Core.Smgp.SmgpGridReshuffle.ForNextSeason(
                variedPack, previousFinal, startSmgp.CurrentSeatLivery);
        }
        // Per-season DNQ RE-ROLL (17-season campaign): a career gated on SmgpState.PerSeasonDnq re-rolls
        // its backmarker DNQ field for season 2+ (each season a fresh seeded field, so the rotation is not
        // frozen to season 1's pinned roll). This is a FOLD INPUT, the runtime Pack is what the live fold
        // (Apply / PreviewFold) resolves the grid from, so the SAME transform is applied to the pinned
        // pack on the replay path (ReplayService.ResimulateCore), keyed by the same ordinal, and live and
        // replay agree by construction. Season 1 / legacy (flag omitted) / non-DNQ careers no-op, so they
        // stay byte-identical. The gate is read from this season's START state.
        Pack = startSmgp?.PerSeasonDnq == true
            ? Companion.Core.Smgp.SmgpDnqField.ForSeason(variedPack, seasonOrdinal, unchecked((ulong)masterSeed))
            : variedPack;
        _seasonYear = allSeasons.FirstOrDefault(s => s.Id == seasonId)?.Year ?? pack.Season.Year;
        _firstSeasonYear = allSeasons.Count > 0 ? allSeasons[0].Year : _seasonYear;
        _careerName = careerName;
        MasterSeed = masterSeed;
        _playerLiveryName = playerLiveryName;
        _playerDriverId = playerDriverId;
        _playerFirstSeasonAge = playerFirstSeasonAge;
        // The scoring definition's round domain is CHAMPIONSHIP rounds (same resolution the
        // structural validator checks): best-N segments and engine round numbers use the
        // championship ordinal, not the calendar position, when the calendar mixes in
        // non-championship events. ChampionshipCalendar is the ONE shared mapping, the
        // unified fold (ReplayService) resolves through the same code.
        _scoringDefinition = ChampionshipCalendar.ResolveScoring(pack);
    }

    private static StandingsSnapshot? PreviousFinalStandings(
        CareerDatabase database,
        SeasonRecord previousSeason,
        SeasonPack pack)
    {
        if (!string.Equals(previousSeason.PackId, pack.Manifest.PackId, StringComparison.Ordinal))
            return null;
        var results = ResultStore.ReadSeasonResults(database, previousSeason.Id)
            .Where(stored => ChampionshipCalendar.IsChampionshipRound(pack, stored.Round))
            .Select(stored => stored.ToRoundResult())
            .ToList();
        return results.Count == 0
            ? null
            : StandingsEngine.ComputeSeason(ChampionshipCalendar.ResolveScoring(pack), results).Final;
    }

    /// <summary>1-based position of a championship calendar round among championship rounds
    /// only, the round number the scoring engine and best-N segments operate on.</summary>
    private int ChampionshipOrdinal(int calendarRound) =>
        ChampionshipCalendar.Ordinal(Pack, calendarRound);

    // ---------- create / open ----------

    public static CareerSessionService CreateCareer(CareerCreationRequest request, CareerEnvironment environment)
    {
        var files = SeasonPackFiles.Read(request.PackDirectory);
        var pack = files.Parse();
        string selectedSourceSha256 = PinnedPackEnvelope.Sha256Of(files.ToPinnedEnvelope().ToBytes());

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

        // OPT-IN alternate mod tracks (Mike's rule): swap each round with a track.alternate to that
        // alternate ONLY when the player opted in AND every required mod is installed, otherwise the
        // season keeps its base/DLC defaults (no mod dependency). The TRANSFORMED season.json is
        // pinned, so the fold reads the alternates and replays stay byte-identical (no seed).
        if (request.UseAlternateTracks &&
            AlternateTrackTransform.HasAlternates(pack) &&
            AlternateTrackPreflight.CanApplyAlternates(
                pack, environment.ContentLibrary, environment.LocateInstall()?.InstallDirectory))
        {
            files = files with { SeasonJson = AlternateTrackTransform.ApplyToSeasonJson(files.SeasonJson) };
            pack = files.Parse();
        }

        // OPT-IN modded field (v1.4): append the mod's extra grid entries + bump the round grid
        // sizes ONLY when the player opted in AND the required car mod is installed, otherwise the
        // base field is pinned (no mod dependency). The TRANSFORMED files are pinned, so the fold
        // fields the fuller grid and replays stay byte-identical (no seed).
        if (request.UseModdedField &&
            pack.Manifest.ModdedField is { } moddedField &&
            ModdedFieldTransform.HasModdedField(pack) &&
            ModdedVehiclePreflight.CanApplyModdedField(
                pack, environment.ContentLibrary, environment.LocateInstall()?.InstallDirectory))
        {
            var trackCaps = environment.ContentLibrary.Tracks
                .ToDictionary(t => t.Key, t => t.Value.MaxAiParticipants, StringComparer.Ordinal);
            files = files with
            {
                EntriesJson = ModdedFieldTransform.ApplyToEntriesJson(files.EntriesJson, moddedField),
                SeasonJson = ModdedFieldTransform.ApplyToSeasonJson(files.SeasonJson, moddedField, trackCaps),
            };
            pack = files.Parse();
        }

        // SMGP DYNAMIC DNQ (Mike: "a random generator should determine the bottom 8 ... who stays and
        // who goes"): the field's DNQ tail is a SEEDED PER-CAREER roll, not the pack's baked default, a
        // random generator picks which backmarkers make each round's grid, so the rotation differs per
        // playthrough (revealed race by race via the starting-grid DNQ strip). Generated here at creation
        // and pinned into season.json, so the fold reads the seeded starters and replays stay byte-
        // identical (no fold change, no seed threading); existing careers keep their pinned starters.
        if (string.Equals(pack.Manifest.CareerStyle, Companion.Core.Smgp.SmgpRules.CareerStyle, StringComparison.Ordinal) &&
            Companion.Core.Smgp.SmgpDnqField.HasDnqField(pack))
        {
            var starters = Companion.Core.Smgp.SmgpDnqField.Generate(pack, unchecked((ulong)request.MasterSeed));
            files = files with
            {
                SeasonJson = Companion.Core.Smgp.SmgpDnqField.ApplyToSeasonJson(files.SeasonJson, starters),
            };
            pack = files.Parse();
        }

        string playerDriverId = ResolvePlayerDriverId(pack, request.PlayerLiveryName);

        // An explicit v2 mode freezes its entire bounded horizon now. The selected pack includes
        // every creation transform above; future packs are parsed and pre-pinned before the DB opens.
        var selectedPin = PreparedCampaignPack.From(files, pack, selectedSourceSha256);
        var campaign = CampaignCreationPlanner.Prepare(request, environment, selectedPin);

        string? directory = Path.GetDirectoryName(request.CareerFilePath);
        if (directory is { Length: > 0 })
            Directory.CreateDirectory(directory);

        // CreateNew is the ownership boundary. Two concurrent creators cannot both reserve this
        // path, and failure cleanup below deletes only the file this call successfully reserved.
        ReserveCareerFile(request.CareerFilePath);
        bool ownsCareerFile = true;

        CareerDatabase? database = null;
        bool creationCommitted = false;
        try
        {
            database = CareerDatabase.Open(request.CareerFilePath);
            string nowUtc = environment.Clock.GetUtcNow().UtcDateTime.ToString("O");
            long seasonId;

            using (var transaction = database.Connection.BeginTransaction())
            {
                Execute(database.Connection, transaction,
                    "INSERT INTO career (id, name, created_utc, master_seed, app_version, mortality_mode) " +
                    "VALUES (1, @name, @utc, @seed, @version, @mortality);",
                    ("@name", request.CareerName), ("@utc", nowUtc),
                    ("@seed", request.MasterSeed), ("@version", AppVersion),
                    ("@mortality", (int)request.Mortality));

                foreach (var pin in campaign?.DistinctPacks ?? [selectedPin])
                {
                    CareerStore.PinPackEnvelope(
                        database,
                        pin.Pack.Manifest.PackId,
                        pin.Pack.Manifest.Version,
                        pin.EnvelopeBytes,
                        nowUtc,
                        transaction);
                }

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
                    string characterDelta = campaign is null
                        // Keep the legacy v0/v1 INPUT payload byte-for-byte unchanged.
                        ? JsonSerializer.Serialize(
                            new { name = character.Name, stats = character.Stats, perkIds = character.PerkIds, cpUnspent = character.CpUnspent },
                            CoreJson.Options)
                        : JsonSerializer.Serialize(campaign.CharacterInput, CoreJson.Options);
                    Execute(database.Connection, transaction,
                        "INSERT INTO journal (utc, season_id, round, phase, entity, delta_json, cause) " +
                        "VALUES (@utc, @season, NULL, @phase, 'player', @delta, 'career-created');",
                        ("@utc", nowUtc), ("@season", seasonId),
                        ("@phase", JournalPhases.PlayerCharacter),
                        ("@delta", characterDelta));
                }

                // The chosen season field (v0.6.0): journal the creation INPUT row (provenance-excluded
                // from replay; its data rides in the start state) when the player narrowed the grid.
                if (request.GridSelection is { IncludesEverything: false } selection)
                {
                    Execute(database.Connection, transaction,
                        "INSERT INTO journal (utc, season_id, round, phase, entity, delta_json, cause) " +
                        "VALUES (@utc, @season, NULL, @phase, 'player', @delta, 'career-created');",
                        ("@utc", nowUtc), ("@season", seasonId),
                        ("@phase", JournalPhases.PlayerGridSelection),
                        ("@delta", JsonSerializer.Serialize(
                            new { includedLiveries = selection.IncludedLiveries }, CoreJson.Options)));
                }

                SeedStartStates(
                    database, seasonId, pack, playerDriverId, request.PlayerLiveryName,
                    request.Character, request.GridSelection, request.FormAware,
                    request.SmgpMode || request.ExperienceMode == CareerExperienceModes.Smgp,
                    request.Mortality,
                    // The bounded modes carry their mode on the plan; a pure-racing Passport has
                    // no plan, so the request's explicit mode is the fallback (null for legacy).
                    campaign?.Plan?.Mode ?? request.ExperienceMode,
                    request.PlayerDisplayName,
                    request.PlayerCountryCode,
                    campaign?.Plan,
                    // The Dynasty owner economy: both halves of the gate, the pinned campaign mode
                    // AND the creation opt-in. The rules are resolved only for an opted-in Dynasty
                    // career, so a rules-directory-free caller (most tests) never touches them; an
                    // economy career on an install missing the tables fails HERE with a clear
                    // message rather than seeding an economy it cannot fold.
                    request.DynastyEconomy && campaign?.Plan?.Mode == CareerExperienceModes.GrandPrixDynasty
                        ? environment.Rules.DynastyEconomy
                            ?? throw new InvalidOperationException(
                                "The Dynasty owner economy is unavailable, data\\rules\\dynasty\\economy.json " +
                                "is missing from this install. Reinstall the app's data folder to run an " +
                                "economy career.")
                        : null,
                    transaction);

                transaction.Commit();
                creationCommitted = true;
            }

            var session = new CareerSessionService(
                database, environment, pack, request.CareerFilePath, seasonId,
                request.CareerName, request.MasterSeed, request.PlayerLiveryName, playerDriverId,
                // A real character sets its OWN first-season age; without one (or a legacy character)
                // the age falls back to the historical driver whose seat was taken.
                request.Character?.Age ?? PlayerAgeIn(pack, playerDriverId),
                import is null ? BaselineSourcePack : BaselineSourceInstalledAiFile,
                request.Mortality);
            // Normal careers autosave the fresh season's start point (best-effort) so a death always
            // has a very recent restore point (character-death plan §4). No-op for Off/Hardcore.
            session.TryAutosaveSeasonStart();
            return session;
        }
        catch
        {
            database?.Dispose();
            if (ownsCareerFile && !creationCommitted)
                DeleteIncompleteCareerArtifacts(request.CareerFilePath);
            throw;
        }
    }

    private static void ReserveCareerFile(string careerFilePath)
    {
        try
        {
            using var reservation = new FileStream(
                careerFilePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
        }
        catch (IOException) when (File.Exists(careerFilePath))
        {
            throw new InvalidOperationException(
                $"'{careerFilePath}' already exists, open it instead of creating over it.");
        }
    }

    private static void DeleteIncompleteCareerArtifacts(string careerFilePath)
    {
        foreach (string path in new[] { careerFilePath, careerFilePath + "-wal", careerFilePath + "-shm" })
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Preserve the creation exception. A later gallery cleanup can surface a rare
                // locked artifact, but the original validation/storage failure remains primary.
            }
        }
    }

    /// <summary>Opens a career into its CURRENT season, the LATEST season row, so a
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
                    "SELECT name, master_seed, mortality_mode FROM career WHERE id = 1;",
                    r => (Name: r.GetString(0), Seed: r.GetInt64(1), Mortality: r.GetInt32(2)))
                .SingleOrDefault(defaultValue: default);
            if (careerRow.Name is null)
                throw new InvalidOperationException($"'{careerFilePath}' has no career row, not a career file?");
            (string careerName, long masterSeed, int mortalityRaw) = careerRow;
            // The mortality_mode column is guaranteed present by the migration that ran during Open.
            var mortality = (MortalityMode)mortalityRaw;

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

            // A real character carries its OWN first-season age (in the first season's start state);
            // a legacy/no-character career has none and falls back to the seat driver's age below.
            int? characterAge = StateStore.ReadPlayerState(database, first.Id, StateStore.StageStart)?.Character?.Age;

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
                playerFirstSeasonAge = characterAge ?? PlayerAgeIn(pack, playerDriverId);
            }
            else
            {
                // Era-transitioned career: the player's seat in the CURRENT season comes from
                // the transition plan's start state (CareerStore.StartNextSeason persisted it);
                // the driver id is the new pack's entry behind that livery, the seat the
                // player took over, exactly like the wizard's season-1 seat pick.
                var start = StateStore.ReadPlayerState(database, latest.Id, StateStore.StageStart)
                    ?? throw new InvalidOperationException(
                        $"Season {latest.Id} ({latest.Year}) has no start-of-season player state, " +
                        "the career file is structurally inconsistent.");
                playerLiveryName = start.LiveryName
                    ?? throw new InvalidOperationException(
                        $"Season {latest.Id} ({latest.Year})'s player state has no seat livery, " +
                        "the era transition that started it was incomplete.");
                playerDriverId = ResolvePlayerDriverId(pack, playerLiveryName);
                baselineSource = BaselineSourcePack; // transition seasons pin pack-authored data

                // The season-end pipeline ages the player by year distance from the FIRST
                // season (ReplaySimInputs.PlayerAge contract), derive the anchor age from
                // the first season's pinned pack and the wizard's seat pick.
                var firstPack = PinnedPackEnvelope.LoadSeasonPack(
                    CareerStore.ReadPinnedPack(database, first.PackId, first.PackVersion).PackJson);
                playerFirstSeasonAge = characterAge ?? PlayerAgeIn(firstPack, delta.PlayerDriverId);
            }

            var session = new CareerSessionService(
                database, environment, pack, careerFilePath, latest.Id,
                careerName, masterSeed, playerLiveryName, playerDriverId,
                playerFirstSeasonAge, baselineSource, mortality);
            // A freshly-started season (e.g. just transitioned into) autosaves its start once for
            // Normal careers; gated so opening a mid-season career or a non-Normal one is a no-op.
            session.TryAutosaveSeasonStart();
            return session;
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

    /// <summary>Seeds the season's stage-'start' sim-input states, the wizard facts the fold
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
        Companion.Core.Grid.GridSelection? gridSelection,
        bool formAware,
        bool smgpMode,
        MortalityMode mortality,
        string? experienceMode,
        string? customDisplayName,
        string? customCountryCode,
        CampaignProgressionPlan? campaignProgressionPlan,
        Companion.Core.Dynasty.DynastyEconomyRules? economyRules,
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
            ExperienceMode = experienceMode,
            // The pure-racing identity field (null = the seat's authored driver name shows, and
            // every pre-feature save stays byte-identical): NOT a character, just a display name.
            CustomDisplayName = customDisplayName,
            CustomCountryCode = customCountryCode,
            CampaignProgressionPlan = campaignProgressionPlan,
            // The chosen season field (null = whole pack → byte-identical). Carried forward each
            // round; the fold resolves the grid to exactly this field.
            GridSelection = gridSelection is { IncludesEverything: false } ? gridSelection : null,
            // Ratings Phase 3: form-reactive fold, on for new careers (false = byte-identical). Carried
            // forward each round via record `with`, so a multi-season career stays form-aware and its
            // re-derived start states match, no rollover/transition change needed.
            FormAware = formAware,
            // The SMGP replica mode (M3): both halves of the gate, the pack's declared style AND the
            // creation opt-in, or null, which serializes to nothing (WhenWritingNull) so every other
            // career is byte-identical. The player starts in the wizard-picked car.
            Smgp = smgpMode && string.Equals(
                    pack.Manifest.CareerStyle, Companion.Core.Smgp.SmgpRules.CareerStyle, StringComparison.Ordinal)
                // TwoPhasePromotion (3c-2): new smgp careers DEFER a two-wins offer to the post-race
                // promotion screen; omitted-when-false so every pre-3c-2 career keeps the inline path.
                // PerSeasonDnq (17-season campaign): a DNQ pack re-rolls its backmarker field every season
                // 2+; omitted-when-false so every pre-change career keeps the single pinned field. Both are
                // per-career gates seeded at creation, carried across rollover, and replay-verified.
                ? new Companion.Core.Smgp.SmgpState
                {
                    CurrentSeatLivery = playerLiveryName,
                    TwoPhasePromotion = true,
                    PerSeasonDnq = Companion.Core.Smgp.SmgpDnqField.HasDnqField(pack),
                    PerSeasonVariety = true,
                    StandingsReshuffle = true,
                }
                : null,
            // The mortality mode (character death & injury, Slice 1): Off (default) serializes to
            // nothing (WhenWritingDefault) so a non-opted career's start blob is byte-identical;
            // Normal/Hardcore are carried forward each round via record `with`, exactly like FormAware.
            Mortality = mortality,
            // The Dynasty owner economy: the caller resolved rules ONLY for an opted-in Dynasty
            // career, so a non-null value IS the gate (mode + opt-in). The opening balance is
            // pinned here from the starting team's tier, era-scaled, start state is INPUT, so a
            // later rules edit never rewrites an existing career's opening funds. Null serializes
            // to nothing (WhenWritingNull): every other career's start blob is byte-identical.
            Economy = economyRules is null
                ? null
                : new Companion.Core.Dynasty.DynastyEconomyState
                {
                    Version = economyRules.SchemaVersion,
                    Balance = economyRules.StartingFunds(playerTier, year),
                },
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
        // SMGP clean-swap model: the player is their OWN distinct driver, NOT the authored occupant of
        // the car they pick, so that occupant is a real AI who benches while the player holds the seat
        // and RETURNS the moment the player swaps to another car (Mike: "the original driver should come
        // back to that car i just left"). Every non-SMGP career keeps impersonating the seat's driver.
        if (string.Equals(pack.Manifest.CareerStyle, Companion.Core.Smgp.SmgpRules.CareerStyle, StringComparison.Ordinal))
            return RoundGridResolver.SyntheticPlayerDriverId;

        var entry = pack.Entries.FirstOrDefault(e =>
            string.Equals(e.Ams2LiveryName, playerLiveryName, StringComparison.Ordinal));
        if (entry is not null)
            return entry.DriverId;

        var guest = pack.Season.Rounds
            .SelectMany(r => r.GuestEntries)
            .FirstOrDefault(g => string.Equals(g.Ams2LiveryName, playerLiveryName, StringComparison.Ordinal));
        if (guest is not null)
            return guest.DriverId;

        // Player-as-own-entrant: a livery matching no pack entry is a custom/non-standard skin the player
        // chose. Instead of refusing, give them a stable synthetic driver id, the resolver seats them as
        // their own independent entrant, so a custom skin works and the career never dead-ends. Matches
        // RoundGridResolver's synthetic-seat branch (same id) so creation, staging and the fold agree.
        return RoundGridResolver.SyntheticPlayerDriverId;
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
                SeasonYear = _seasonYear,
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
    /// player states (never recomputed here, the fold is the one source of truth).</summary>
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
    /// before any round), the one source of the current character, level and XP.</summary>
    private PlayerCareerState? CurrentPlayerState()
    {
        // Once the season-end pipeline has run, its stage=end row owns the final XP/Level and
        // injury state. Development happens on the review screen, so tree gates and milestone
        // tokens must read that state rather than the last per-round snapshot.
        if (SeasonComplete &&
            StateStore.ReadPlayerState(_database, _seasonId, StateStore.StageEnd) is { } ended)
            return ended;
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
        // The driver's current age: their real created age advanced by the seasons played. Null for a
        // legacy character with no authored age (we never present the borrowed historical age as theirs).
        int? currentAge = character.Age is { } startAge
            ? startAge + (_seasonYear - _firstSeasonYear)
            : null;
        var rules = _environment.Rules.Character;
        var projectedCharacter = CharacterProgress.ApplyRespecs(character, PendingRespecs());
        if (character.ProgressionVersion == CharacterLevelProgression.Level300Version)
        {
            var projectedPlayer = ProjectConfirmedSkillDevelopment(
                player with { Character = projectedCharacter }, PendingSkillDevelopment());
            projectedCharacter = projectedPlayer.Character!;
        }
        var dossier = Companion.Core.Character.CharacterDossier.Build(
            projectedCharacter, player.Level, player.Xp, rules, currentAge,
            player.RaceSuspensionRemaining, player.SeasonEndingInjury, player.Deceased,
            progressionYear: Pack.Season.Year,
            campaignProgressionPlan: player.CampaignProgressionPlan,
            completedSeasons: player.SeasonsCompleted,
            masterySkills: _environment.Rules.MasterySkills,
            racingDnaCatalog: _environment.Rules.RacingDna);
        // Reflect spends made this season but not yet applied at a transition.
        int pending = character.ProgressionVersion == CharacterLevelProgression.Level300Version
            ? 0
            : PendingSpends().Sum(s => s.Cost);
        return pending == 0 ? dossier : dossier with { CpUnspent = Math.Max(0, dossier.CpUnspent - pending) };
    }

    /// <summary>Between-season spends journaled this (not-yet-transitioned) season, pending until
    /// sign-and-continue applies them to the next season's character.</summary>
    private IReadOnlyList<CharacterSpend> PendingSpends() =>
        ReplayService.ReadCharacterSpends(_database, _seasonId);

    private IReadOnlyList<CharacterRespec> PendingRespecs() =>
        ReplayService.ReadCharacterRespecs(_database, _seasonId);

    private IReadOnlyList<CharacterSkillDevelopmentAction> PendingSkillDevelopment(
        SqliteTransaction? transaction = null) =>
        ReplayService.ReadCharacterSkillDevelopment(_database, _seasonId, transaction);

    /// <summary>Returns the persisted review state only when this session still owns the latest,
    /// fully-folded season. In particular, an unresolved final SMGP promotion can have every raw
    /// round present while the season is not complete and therefore cannot accept development.</summary>
    private PlayerCareerState SkillPlanReviewState(SqliteTransaction? transaction)
    {
        object? latestValue = Scalar(
            _database.Connection,
            transaction,
            "SELECT id FROM season ORDER BY id DESC LIMIT 1;");
        if (latestValue is null || Convert.ToInt64(latestValue) != _seasonId)
            throw new InvalidOperationException("This season is no longer the active career review.");

        string? status = Convert.ToString(Scalar(
            _database.Connection,
            transaction,
            "SELECT status FROM season WHERE id = @season;",
            ("@season", _seasonId)), CultureInfo.InvariantCulture);
        if (!string.Equals(status, SeasonStatus.Complete, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Skill plans can only be confirmed from a completed season review.");
        }

        return StateStore.ReadPlayerState(
                   _database, _seasonId, StateStore.StageEnd, transaction)
               ?? throw new InvalidOperationException(
                   "The completed season has no persisted end-state character to develop.");
    }

    private static MasteryProgressionFacts SkillPlanFacts(PlayerCareerState player)
    {
        var character = player.Character
            ?? throw new InvalidOperationException("This career has no character to develop.");
        if (character.ProgressionVersion != CharacterLevelProgression.Level300Version)
            throw new InvalidOperationException("Atomic skill plans require a progression-v2 character.");
        var campaign = player.CampaignProgressionPlan
            ?? throw new InvalidOperationException(
                "Progression-v2 development requires its pinned campaign progression plan.");
        campaign.Validate();
        int available = CharacterProgressionV2Math.SkillPoints(
            player.Level,
            player.SeasonsCompleted,
            campaign.MasterySeason,
            character.SkillPointsSpent).Available;
        return new MasteryProgressionFacts(
            player.Level,
            available,
            player.SeasonsCompleted >= campaign.MasterySeason);
    }

    private PlayerCareerState ProjectConfirmedSkillDevelopment(
        PlayerCareerState player,
        IReadOnlyList<CharacterSkillDevelopmentAction> actions,
        SqliteTransaction? transaction = null)
    {
        if (actions.Count == 0)
            return player;
        if (ReplayService.ReadCharacterSpends(_database, _seasonId, transaction).Count > 0)
        {
            throw new InvalidOperationException(
                "This pre-release v2 save contains legacy player.statSpend inputs. " +
                "They cannot be mixed with atomic mastery plans without a deterministic migration.");
        }
        return CharacterSkillDevelopmentTransition.Apply(
            player,
            actions,
            _environment.Rules.Character,
            _environment.Rules.MasterySkills);
    }

    private static SkillTreeSnapshot MarkPending(
        SkillTreeSnapshot tree,
        IEnumerable<string> pendingIds)
    {
        var pending = pendingIds.ToHashSet(StringComparer.Ordinal);
        if (pending.Count == 0)
            return tree;
        return tree with
        {
            Branches = tree.Branches.Select(branch => branch with
            {
                Nodes = branch.Nodes.Select(node => pending.Contains(node.Id)
                    ? node with { State = SkillNodeState.Pending, LockReason = "" }
                    : node).ToArray(),
            }).ToArray(),
        };
    }

    private static IEnumerable<CharacterSkillPlanEntry> ActivePendingSkillEntries(
        IReadOnlyList<CharacterSkillDevelopmentAction> actions)
    {
        int start = 0;
        for (int index = actions.Count - 1; index >= 0; index--)
        {
            if (actions[index] is CharacterSkillResetAction)
            {
                start = index + 1;
                break;
            }
        }

        return actions.Skip(start)
            .OfType<CharacterSkillPlanAction>()
            .SelectMany(action => action.Input.Entries);
    }

    public SkillPlanPreview PreviewSkillPlan(IReadOnlyList<string> orderedNodeIds)
    {
        ArgumentNullException.ThrowIfNull(orderedNodeIds);
        var player = SkillPlanReviewState(transaction: null);
        var confirmed = PendingSkillDevelopment();
        var projected = ProjectConfirmedSkillDevelopment(player, confirmed);
        var facts = SkillPlanFacts(projected);
        var input = MasterySkillPlan.Prepare(
            projected.Character!, orderedNodeIds, facts, _environment.Rules.MasterySkills);
        var after = MasterySkillPlan.Apply(
            projected.Character!, input, facts, _environment.Rules.MasterySkills);
        int availableAfter = checked(facts.AvailableSkillPoints - input.TotalCost);
        var tree = MasterySkillGraph.Build(
            after,
            projected.Level,
            availableAfter,
            _environment.Rules.MasterySkills,
            facts.MasteryCheckpointComplete);
        var pendingIds = ActivePendingSkillEntries(confirmed).Select(entry => entry.NodeId)
            .Concat(input.Entries.Select(entry => entry.NodeId));
        return new SkillPlanPreview
        {
            Input = input,
            ProjectedTree = MarkPending(tree, pendingIds),
            SkillPointsAfterPlan = availableAfter,
        };
    }

    public void ApplySkillPlan(IReadOnlyList<string> orderedNodeIds)
    {
        ArgumentNullException.ThrowIfNull(orderedNodeIds);
        using var transaction = _database.Connection.BeginTransaction();
        var player = SkillPlanReviewState(transaction);
        var confirmed = PendingSkillDevelopment(transaction);
        var projected = ProjectConfirmedSkillDevelopment(player, confirmed, transaction);
        var facts = SkillPlanFacts(projected);
        var input = MasterySkillPlan.Prepare(
            projected.Character!, orderedNodeIds, facts, _environment.Rules.MasterySkills);

        JournalStore.Append(
            _database,
            _seasonId,
            round: null,
            new JournalEvent
            {
                Phase = JournalPhases.PlayerSkillPlan,
                Entity = "player",
                DeltaJson = JsonSerializer.Serialize(input, CoreJson.Options),
                Cause = "development",
            },
            NowUtc(),
            transaction);
        transaction.Commit();
    }

    public SkillResetPreview? PreviewSkillReset()
    {
        if (_environment.RulesDirectory is null || !SeasonComplete)
            return null;
        var current = CurrentPlayerState();
        if (current?.Character?.ProgressionVersion != CharacterLevelProgression.Level300Version)
            return null;

        var player = SkillPlanReviewState(transaction: null);
        var projected = ProjectConfirmedSkillDevelopment(
            player, PendingSkillDevelopment());
        return CharacterSkillReset.Preview(
            projected,
            _environment.Rules.Character,
            _environment.Rules.MasterySkills);
    }

    public void ApplySkillReset()
    {
        using var transaction = _database.Connection.BeginTransaction();
        var player = SkillPlanReviewState(transaction);
        var projected = ProjectConfirmedSkillDevelopment(
            player, PendingSkillDevelopment(transaction), transaction);
        var input = CharacterSkillReset.Prepare(
            projected,
            _environment.Rules.Character,
            _environment.Rules.MasterySkills);

        JournalStore.Append(
            _database,
            _seasonId,
            round: null,
            new JournalEvent
            {
                Phase = JournalPhases.PlayerSkillReset,
                Entity = "player",
                DeltaJson = JsonSerializer.Serialize(input, CoreJson.Options),
                Cause = "development",
            },
            NowUtc(),
            transaction);
        transaction.Commit();
    }

    private SkillTreeSnapshot? _skillTreeCache;
    private ProjectionFingerprint _skillTreeFingerprint;
    private string _skillTreePendingKey = "";

    /// <summary>The rules-backed skill-tree snapshot, including this season's pending development
    /// inputs so a just-bought node immediately projects as owned. Memoized behind the same
    /// stored-state fingerprint the newsroom uses (plus the pending-skill key), so repeated
    /// dossier opens and no-op refreshes skip the full 209-node re-projection.</summary>
    public SkillTreeSnapshot? SkillTree()
    {
        if (_environment.RulesDirectory is null)
            return null;
        var player = CurrentPlayerState();
        if (player?.Character is not { } character)
            return null;
        var rules = _environment.Rules.Character;

        string pendingKey = PendingSkillDevelopment().Count + "/" + PendingRespecs().Count + "/" + PendingSpends().Count;
        var fingerprint = ProjectionFingerprint.Read(_database);
        if (_skillTreeCache is not null &&
            fingerprint == _skillTreeFingerprint &&
            pendingKey == _skillTreePendingKey)
        {
            return _skillTreeCache;
        }

        SkillTreeSnapshot? result;

        if (character.ProgressionVersion == CharacterLevelProgression.Level300Version)
        {
            var actions = PendingSkillDevelopment();
            var projectedPlayer = ProjectConfirmedSkillDevelopment(player, actions);
            var facts = SkillPlanFacts(projectedPlayer);
            var tree = MasterySkillGraph.Build(
                projectedPlayer.Character!,
                projectedPlayer.Level,
                facts.AvailableSkillPoints,
                _environment.Rules.MasterySkills,
                facts.MasteryCheckpointComplete);
            result = MarkPending(
                tree,
                ActivePendingSkillEntries(actions)
                    .Select(entry => entry.NodeId));
        }
        else
        {
            var projected = CharacterProgress.ApplyRespecs(character, PendingRespecs());
            projected = CharacterProgress.ApplyAll(projected, PendingSpends(), rules);
            result = Companion.Core.Character.SkillTree.Build(
                projected, player.Level, AvailableCharacterCp(), rules);
        }

        _skillTreeCache = result;
        _skillTreeFingerprint = fingerprint;
        _skillTreePendingKey = pendingKey;
        return result;
    }

    /// <summary>Version-selected development points available right now: the folded pool minus this
    /// season's pending spends. 0 when the career has no character.</summary>
    public int AvailableCharacterCp()
    {
        var player = CurrentPlayerState();
        if (_environment.RulesDirectory is null || player?.Character is not { } character)
            return 0;
        if (character.ProgressionVersion == CharacterLevelProgression.Level300Version)
        {
            var projectedPlayer = ProjectConfirmedSkillDevelopment(
                player, PendingSkillDevelopment());
            return SkillPlanFacts(projectedPlayer).AvailableSkillPoints;
        }
        var projected = CharacterProgress.ApplyRespecs(character, PendingRespecs());
        int available = CharacterProgress.AvailableSkillPoints(
            projected,
            player.Level,
            _environment.Rules.Character,
            player.SeasonsCompleted,
            player.CampaignProgressionPlan);
        int afterPending = available - PendingSpends().Sum(s => s.Cost);
        return afterPending;
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
        if (character.ProgressionVersion == CharacterLevelProgression.Level300Version)
        {
            throw new InvalidOperationException(
                "Progression-v2 development uses PreviewSkillPlan and ApplySkillPlan.");
        }
        var rules = _environment.Rules.Character;
        var pendingSpends = PendingSpends();
        var projectedCharacter = CharacterProgress.ApplyRespecs(character, PendingRespecs());
        projectedCharacter = CharacterProgress.ApplyAll(projectedCharacter, pendingSpends, rules);
        string journalTarget = spend.Target;

        // Derive the AUTHORITATIVE cost from the rules and never trust the caller's Cost. Otherwise a
        // crafted spend could buy a costed perk for 0 (or mint points with a negative cost), and
        // because the row is provenance-excluded it would replay byte-for-byte, baking the exploit
        // permanently into the career. The journaled row carries the derived cost, not spend.Cost.
        int cost;
        if (spend.Kind == "stat")
        {
            var statNode = rules.SkillTree.TryGetStatNode(spend.Target);
            if (statNode is null && character.ProgressionVersion >= 1 && IsKnownStat(rules, spend.Target))
            {
                // The legacy Season Review command still names a raw stat. Canonicalize it to the
                // next authored node so new careers retain durable node ownership in the same
                // player.statSpend journal shape. Old profiles keep their historical raw-stat path.
                statNode = rules.SkillTree.StatNodes
                    .Where(node => string.Equals(node.Stat, spend.Target, StringComparison.Ordinal))
                    .OrderBy(node => node.Tier)
                    .ThenBy(node => node.UnlockLevel)
                    .FirstOrDefault(node => !projectedCharacter.SkillNodeIds.Contains(
                        node.Id, StringComparer.Ordinal));
                if (statNode is null)
                    throw new InvalidOperationException("Every authored raise for that stat is already owned.");
                journalTarget = statNode.Id;
            }

            string statId = statNode?.Stat ?? spend.Target;
            if (!IsKnownStat(rules, statId))
                throw new InvalidOperationException($"Unknown stat '{spend.Target}'.");
            if (statNode is not null)
            {
                var treeNode = Companion.Core.Character.SkillTree.Build(
                        projectedCharacter, player.Level, AvailableCharacterCp(), rules)
                    .Branches.SelectMany(branch => branch.Nodes)
                    .Single(node => string.Equals(node.Id, statNode.Id, StringComparison.Ordinal));
                if (treeNode.State != SkillNodeState.Unlockable)
                    throw new InvalidOperationException(
                        treeNode.State == SkillNodeState.Owned ? "That skill node is already owned." : treeNode.LockReason);
            }

            double step = rules.Levels.LevelGrants.StatStepValue;
            var mods = PerkResolver.Resolve(projectedCharacter, rules);
            // One-Trick Pony's lockToOne freezes every talent stat except the one that owns the
            // chosen specialism rating, you can only ever develop your single weapon.
            if (mods.LockedFlavorRating is { } locked)
            {
                bool isTalent = rules.Stats.TalentStats.Any(s =>
                    string.Equals(s.Id, statId, StringComparison.Ordinal));
                bool ownsLocked = rules.Stats.TalentStats.Any(s =>
                    string.Equals(s.Id, statId, StringComparison.Ordinal)
                    && s.MapsTo.Contains(locked, StringComparer.Ordinal));
                if (isTalent && !ownsLocked)
                    throw new InvalidOperationException(
                        "One-Trick Pony locks development to your single specialism, that stat is frozen.");
            }
            // A statPoints/softCap perk (iron_constitution) lowers the in-career raise ceiling.
            double cap = rules.Levels.LevelGrants.StatCapPerRating + mods.StatSoftCapDelta;
            double current = projectedCharacter.Stat(statId);
            if (current + step > cap + 1e-9)
                throw new InvalidOperationException("That stat is already at its maximum.");
            cost = statNode?.Cost ?? rules.Levels.LevelGrants.StatStepCpCost;
        }
        else if (spend.Kind == "perk")
        {
            if (!rules.TryGetPerk(spend.Target, out var perk))
                throw new InvalidOperationException($"Unknown perk '{spend.Target}'.");
            if (projectedCharacter.ProgressionVersion == CharacterLevelProgression.Level300Version &&
                PlayerCarScalarPolicy.HasConditionalCarScalar(perk))
            {
                throw new InvalidOperationException(
                    "That skill has conditional player-car physics and is unavailable until its " +
                    "pre-race condition is persisted for both AMS2 staging and replay.");
            }
            // Between-season development is spend-only: a drawback (<=0-cost) perk is a creation-time
            // identity choice, not something you buy mid-career (and letting one refund CP would break
            // the earned-points model).
            if (perk.Cost <= 0)
                throw new InvalidOperationException("That perk can only be chosen at creation.");
            var treeNode = Companion.Core.Character.SkillTree.Build(
                    projectedCharacter, player.Level, AvailableCharacterCp(), rules)
                .Branches.SelectMany(branch => branch.Nodes)
                .Single(node => string.Equals(node.Id, perk.Id, StringComparison.Ordinal));
            if (treeNode.State != SkillNodeState.Unlockable)
                throw new InvalidOperationException(
                    treeNode.State == SkillNodeState.Owned ? "Your driver already has that perk." : treeNode.LockReason);
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
                new { kind = spend.Kind, target = journalTarget, cost }, CoreJson.Options)));
        transaction.Commit();
    }

    /// <summary>True when the id names a real talent or meta stat (guards a development spend against
    /// a crafted target that would otherwise inject a phantom entry into the stat map).</summary>
    private static bool IsKnownStat(CharacterRules rules, string statId) =>
        rules.Stats.TalentStats.Any(s => string.Equals(s.Id, statId, StringComparison.Ordinal))
        || rules.Stats.MetaStats.Any(s => string.Equals(s.Id, statId, StringComparison.Ordinal));

    /// <summary>The perks the driver can buy right now: positive-cost, not already owned or pending,
    /// and affordable from the current pool, cheapest first. Empty with no character or no points.</summary>
    public IReadOnlyList<PurchasablePerk> PurchasablePerks()
    {
        var player = CurrentPlayerState();
        if (_environment.RulesDirectory is null || player?.Character is not { } character)
            return [];
        if (character.ProgressionVersion == CharacterLevelProgression.Level300Version)
            return [];
        int available = AvailableCharacterCp();
        if (available <= 0)
            return [];

        var rules = _environment.Rules.Character;
        var projected = CharacterProgress.ApplyRespecs(character, PendingRespecs());
        projected = CharacterProgress.ApplyAll(projected, PendingSpends(), rules);
        var unlockable = Companion.Core.Character.SkillTree.Build(projected, player.Level, available, rules)
            .Branches.SelectMany(branch => branch.Nodes)
            .Where(node => node.Kind == "perk" && node.State == SkillNodeState.Unlockable)
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);

        return rules.Perks
            .Where(p => unlockable.Contains(p.Id))
            .Where(p => character.ProgressionVersion != CharacterLevelProgression.Level300Version ||
                        !PlayerCarScalarPolicy.HasConditionalCarScalar(p))
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

    public int RespecTokensAvailable()
    {
        if (_environment.RulesDirectory is null)
            return 0;
        var player = CurrentPlayerState();
        if (player?.Character is null)
            return 0;
        if (player.Character.ProgressionVersion == CharacterLevelProgression.Level300Version)
            return 0;
        var rules = _environment.Rules.Character;
        int spent = JournalStore.ReadAll(_database).Count(row =>
            string.Equals(row.Phase, JournalPhases.PlayerRespec, StringComparison.Ordinal));
        return CharacterRespecMath.AvailableTokens(player.Level, spent, rules);
    }

    public void RespecNode(string nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        if (_environment.RulesDirectory is null)
            throw new InvalidOperationException("No character rules are loaded.");
        var player = CurrentPlayerState();
        if (player?.Character is not { } character)
            throw new InvalidOperationException("This career has no character to respec.");
        if (character.ProgressionVersion == CharacterLevelProgression.Level300Version)
        {
            throw new InvalidOperationException(
                "Version-2 skill resets spend XP; legacy milestone-token respecs are not supported.");
        }
        if (RespecTokensAvailable() <= 0)
            throw new InvalidOperationException("No respec token is available.");

        var rules = _environment.Rules.Character;
        if (!rules.TryGetPerk(nodeId, out var perk) || perk.Cost <= 0)
            throw new InvalidOperationException("Only an earned, positive-cost perk can be respecced.");
        if (!character.PerkIds.Contains(nodeId, StringComparer.Ordinal))
            throw new InvalidOperationException("That perk is not owned.");
        var creationPerks = character.CreationPerkIds ?? character.PerkIds;
        if (rules.Respec.PerksLockedAtCreation && creationPerks.Contains(nodeId, StringComparer.Ordinal))
            throw new InvalidOperationException("Perks chosen at creation are locked.");
        if (PendingRespecs().Any(input => string.Equals(input.NodeId, nodeId, StringComparison.Ordinal)))
            throw new InvalidOperationException("That perk is already pending respec.");

        var input = new CharacterRespec { NodeId = nodeId, Refund = perk.Cost };
        using var transaction = _database.Connection.BeginTransaction();
        Execute(_database.Connection, transaction,
            "INSERT INTO journal (utc, season_id, round, phase, entity, delta_json, cause) " +
            "VALUES (@utc, @season, NULL, @phase, 'player', @delta, 'development');",
            ("@utc", NowUtc()), ("@season", _seasonId),
            ("@phase", JournalPhases.PlayerRespec),
            ("@delta", JsonSerializer.Serialize(input, CoreJson.Options)));
        transaction.Commit();
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

    /// <summary>The whole season's spoiler-free track schedule (Calendar lens) from the pinned pack +
    /// library: each round's driven AMS2 track (after any applied alternate, the pinned pack carries
    /// it), whether it is the real venue / a base stand-in / an applied mod alternate, and any alternate
    /// that exists but was not enabled.</summary>
    public IReadOnlyList<SeasonScheduleEntry> SeasonSchedule()
    {
        var tracks = _environment.ContentLibrary.Tracks;
        string TrackName(string id) => tracks.TryGetValue(id, out var t) && t.TrackName is { Length: > 0 } n ? n : id;

        // Round-detail lookups (Task 3.3): progress marker + the per-round DNQ field.
        int maxApplied = MaxAppliedRound;
        var driverById = new Dictionary<string, PackDriver>(StringComparer.Ordinal);
        foreach (var d in Pack.Drivers) driverById.TryAdd(d.Id, d);
        var teamNameById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var t in Pack.Teams) teamNameById.TryAdd(t.Id, t.Name);

        // Player availability per round (SMGP-300): applied rounds read the stored envelope's
        // PlayerDidNotStart flag, an injury absence is never rewritten as participation, and
        // future rounds project the ACTIVE suspension, so "you will miss the next 2 rounds" is
        // visible on the calendar before it happens.
        var satOutRounds = new HashSet<int>();
        foreach (var stored in ResultStore.ReadSeasonResults(_database, _seasonId))
        {
            if (stored.ToEnvelope().PlayerDidNotStart)
                satOutRounds.Add(stored.Round);
        }
        var availabilityState = CurrentPlayerState();
        int projectedMisses = availabilityState?.Deceased == true || availabilityState?.SeasonEndingInjury == true
            ? int.MaxValue
            : availabilityState?.RaceSuspensionRemaining ?? 0;
        int missesAssigned = 0;

        var schedule = new List<SeasonScheduleEntry>(Pack.Season.Rounds.Count);
        foreach (var round in Pack.Season.Rounds)
        {
            // The backmarkers the pack PINNED out of this round's grid (SMGP's per-race DNQ field),
            // fastest-first. Only entries actually ENTERED this round (whose rounds-range covers it) and
            // de-duplicated by driver id, mirrors RoundGridResolver's covering-entry rule, so a partial-season
            // entrant or a mid-season team switch (historical packs) never leaks a phantom/duplicate DNQ row.
            IReadOnlyList<ScheduleDnqEntry> dnq = round.Grid is { StarterDriverIds.Count: > 0 } grid
                ? Pack.Entries
                    .Where(e => RoundsRange.TryParse(e.Rounds, out var rr) && rr.Contains(round.Round))
                    .Where(e => !grid.StarterDriverIds.Contains(e.DriverId, StringComparer.Ordinal))
                    .GroupBy(e => e.DriverId, StringComparer.Ordinal)
                    .Select(g => g.First())
                    .OrderByDescending(e => driverById.TryGetValue(e.DriverId, out var dd) ? dd.Ratings.QualifyingSkill : 0.0)
                    .Select(e => new ScheduleDnqEntry(
                        driverById.TryGetValue(e.DriverId, out var dn) ? dn.Name : e.DriverId,
                        teamNameById.GetValueOrDefault(e.TeamId, e.TeamId),
                        e.Number))
                    .ToList()
                : [];
            // The REAL (historical) circuit per round, the shared lookup rule (authored
            // per-round history pointer, else pack year + same round; carryover-stable), so the
            // calendar shows the ORIGINAL venue's map + facts, never the stand-in's, even on a
            // pack whose calendar runs a non-historical order (the SMGP replica).
            HistoricalCircuit? circuit = HistoricalCircuitLookup.ForRound(Pack, round.Round, HistoricalSeason);
            var track = round.Track;
            bool alternateApplied = track.Alternate is { } appliedAlt && string.Equals(track.Id, appliedAlt.Id, StringComparison.Ordinal);
            var kind = alternateApplied ? SeasonTrackKind.Alternate
                : track.IsPlaceholder ? SeasonTrackKind.StandIn
                : SeasonTrackKind.RealVenue;

            // An alternate that exists but is NOT the driven track (tick off / mod missing at creation).
            string? unusedAlternate = track.Alternate is { } alt && !alternateApplied ? TrackName(alt.Id) : null;

            SchedulePlayerStatus playerStatus;
            if (round.Round <= maxApplied)
            {
                playerStatus = satOutRounds.Contains(round.Round)
                    ? SchedulePlayerStatus.SatOut
                    : SchedulePlayerStatus.Raced;
            }
            else if (missesAssigned < projectedMisses)
            {
                playerStatus = SchedulePlayerStatus.WillMiss;
                missesAssigned++;
            }
            else
            {
                playerStatus = SchedulePlayerStatus.Upcoming;
            }

            schedule.Add(new SeasonScheduleEntry
            {
                Round = round.Round,
                Name = round.Name,
                Date = round.Date,
                RealVenue = track.RealVenue is { Length: > 0 } venue ? venue : TrackName(track.Id),
                Ams2TrackName = TrackName(track.Id),
                TrackId = track.Id,
                Laps = round.Laps,
                Kind = kind,
                UnusedAlternateName = unusedAlternate,
                CircuitLayoutId = circuit?.LayoutId ?? "",
                CircuitCaption = CircuitCaptions.Compose(circuit),
                CircuitHistory = circuit?.History ?? "",
                CircuitFacts = circuit?.Facts ?? [],
                // Task 3.3 round detail (clickable): championship flag, grid size + DNQ field, weather,
                // setup note, opponents, and the round's progress through the season.
                Championship = round.Championship,
                GridSize = round.Grid?.Size,
                Dnq = dnq,
                WeatherLabel = string.Join(" / ", round.SetupGuide?.Session.WeatherSlots ?? []),
                SetupNote = round.SetupGuide?.Notes ?? "",
                Opponents = round.SetupGuide?.Session.Opponents,
                Status = round.Round <= maxApplied ? SeasonRoundStatus.Done
                    : round.Round == maxApplied + 1 ? SeasonRoundStatus.Next
                    : SeasonRoundStatus.Upcoming,
                PlayerStatus = playerStatus,
            });
        }
        return schedule;
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
            // Form-on so the fallback advice matches the fold's field (the primary path returns the
            // folded state.RecommendedSlider above; this legacy branch is edge-only).
            var grid = ResolveGrid(CurrentRoundNumber, applyWeekendForm: CurrentFormAware());
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
        // Show the player's display name on their seat, not the historical driver they took over (nor,
        // for the SMGP clean-swap player, the BENCHED AI whose car they hold). Display only, the seat's
        // DriverId (what results score under) and the staged AMS2 file (bound by livery) are untouched,
        // so nothing about the sim or the AI file changes.
        return PlayerDisplayName() is { } name
            ? seats.Select(s => s.IsPlayer ? s with { DriverName = name } : s).ToList()
            : seats;
    }

    /// <summary>Display-only authored nationality. The profile is the same folded start/current
    /// identity replay reads; no grid, rating, or staging input is derived from this accessor.</summary>
    public string? CurrentPlayerCountryCode() =>
        CurrentCharacterProfile()?.CountryCode ?? CurrentPlayerState()?.CustomCountryCode;

    /// <summary>Display-only car-art identity for a fixed SMGP livery. The lookup is captured from
    /// the pinned authored pack before runtime driver reshuffles, so promotions and later seasons
    /// retain the correct physical car. Null outside SMGP or for an unknown/custom livery.</summary>
    public string? GridCarArtKeyForLivery(string ams2LiveryName) =>
        _gridCarArtKeyByLivery.GetValueOrDefault(ams2LiveryName);

    /// <summary>The sim's expected finish for the player this round, resolved EXACTLY as the fold's
    /// grid resolution (2-arg resolve with the player seat + character patch), so the Setup Gamble
    /// briefing shows the same number the bet is staked against. Null when the season is complete or
    /// the player has no seat this round.</summary>
    public int? CurrentExpectedFinish()
    {
        if (SeasonComplete)
            return null;
        GridPlan grid;
        try
        {
            // The same resolve as the fold's, including the SMGP seat swaps via ResolveGrid's
            // path, so the expectation shown is the expectation scored.
            grid = ResolveGrid(CurrentRoundNumber, applyWeekendForm: CurrentFormAware());
        }
        catch (InvalidOperationException)
        {
            return null; // the player's livery matches no entry this round
        }
        int playerIndex = SeatStrengthModel.PlayerSeatIndex(grid);
        var player = CurrentPlayerState();
        int modelVersion = player?.Character?.ExpectationModelVersion ?? 0;
        double priorOpi = player?.Opi ?? 0.0;
        var teamTiers = StateStore.ReadTeamStates(_database, _seasonId, StateStore.StageStart)
            .ToDictionary(team => team.TeamId, team => team.Tier, StringComparer.Ordinal);
        return playerIndex < 0
            ? null
            : SeatStrengthModel.ExpectedFinish(
                grid, playerIndex, priorOpi, modelVersion, teamTiers,
                CurrentDynastyDevelopmentStrength(player));
    }

    /// <summary>The Dynasty development strength the NEXT fold will score with (economy §6):
    /// the folded level PLUS any pending buy-development decisions declared for the current
    /// round, the fold applies those at its top, so the briefing's expected finish stays
    /// contractually equal to the number the Setup Gamble is staked against. 0 for every
    /// non-economy career (and after bankruptcy).</summary>
    private double CurrentDynastyDevelopmentStrength(PlayerCareerState? player)
    {
        if (player?.Economy is not { Bankrupt: false } economy
            || _environment.RulesDirectory is null)
        {
            return 0.0;
        }
        var rules = _environment.Rules.DynastyEconomy;
        int pendingBuys = 0;
        if (ReplayService.ReadEconomyDecisions(_database, _seasonId)
            .TryGetValue(CurrentRoundNumber, out var pending))
        {
            pendingBuys = pending.Count(d =>
                d.Kind == Companion.Core.Dynasty.DynastyEconomyDecisionKind.BuyDevelopment);
        }
        return rules.Development.StrengthPerLevel * (economy.DevelopmentLevel + pendingBuys);
    }

    /// <summary>What skin every car on the current round's grid will show, the read-only skin
    /// picture the briefing's Skins panel renders (player-own-car crib + per-AI-car status). Reads
    /// the resolved grid, the installed skin-override scan and the installed NAMeS file; writes
    /// nothing. The player's seat shows the character name (matching <see cref="CurrentGrid"/>), so
    /// the "your car" crib names the driver the player actually is. Empty when the season is complete,
    /// the player's livery matches no entry this round, or no install is found.</summary>
    public SkinAssignmentPlan CurrentSkinAssignments()
    {
        if (SeasonComplete)
            return SkinAssignmentPlan.Empty;

        GridPlan plan;
        try
        {
            plan = ResolveGrid(CurrentRoundNumber);
        }
        catch (InvalidOperationException)
        {
            return SkinAssignmentPlan.Empty;
        }

        var installation = _environment.LocateInstall();
        var scan = _environment.ScanInstalledLiveries(installation);
        var aiNames = _environment.ScanInstalledAiNames(installation, plan.Ams2Class);

        var result = SkinAssignmentResolver.Resolve(plan, scan.Liveries, _environment.ContentLibrary, aiNames);

        // Show the player's display name on their car (as CurrentGrid does), display only,
        // the livery NAME (the binding + what they pick in-game) is untouched.
        if (PlayerDisplayName() is { } name && result.Assignments.Any(a => a.IsPlayer))
        {
            var patched = result.Assignments
                .Select(a => a.IsPlayer ? a with { DriverName = name } : a)
                .ToList();
            result = result with { Assignments = patched };
        }

        return result;
    }

    /// <summary>Switches an inactive placeholder livery ON in its community override file. Finds the
    /// installed placeholder for the name (preferring one in this class's vehicle folders), then
    /// assigns it the next free slot via <see cref="LiveryOverrideWriter"/> (backup-first). Writes
    /// only that community skin file; never the career DB, so the sim/fold/oracle are untouched.</summary>
    public LiveryActivationResult ActivateLivery(string liveryName)
    {
        var installation = _environment.LocateInstall();
        if (installation is null)
            return LiveryActivationResult.Failed(
                "No AMS2 installation was found, cannot locate the skin files to activate.");

        var scan = _environment.ScanInstalledLiveries(installation);
        var classFolders = _environment.ContentLibrary.Vehicles.Values
            .Where(v => string.Equals(v.VehicleClass, Pack.Season.Ams2Class, StringComparison.Ordinal))
            .Select(v => v.Dir)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = scan.Liveries
            .Where(l => string.Equals(l.Name, liveryName, StringComparison.Ordinal) && !l.IsActive)
            .ToList();
        // Prefer a placeholder in THIS class's folder; fall back to any (the content library can be
        // stale and not know a class's folders, the scan is the ground truth either way).
        var chosen = candidates.FirstOrDefault(l => classFolders.Contains(l.VehicleFolder))
            ?? candidates.FirstOrDefault();

        if (chosen is null)
            return LiveryActivationResult.Failed(
                $"“{liveryName}” has no installed inactive placeholder to activate (it may already be active).");

        // Cap the slot at the class's livery limit (custom slots run 51..(50+cap)) so we never write
        // a slot AMS2 would silently ignore. Unknown cap → no ceiling (best effort).
        int? maxSlot = _environment.ContentLibrary.LiveryCaps.TryGetValue(Pack.Season.Ams2Class, out int cap)
            ? LiveryOverrideWriter.FirstCustomSlot + cap - 1
            : null;

        return LiveryOverrideWriter.ActivateInFile(
            chosen.SourceFile, liveryName, _environment.Clock.GetUtcNow(), slot: null, maxSlot: maxSlot);
    }

    /// <summary>The library set this pack declares via <c>pack.json skinSeason</c>, with the
    /// install's Overrides root, null when undeclared, unknown to the library, or no install.</summary>
    private (SkinSeasonSet Set, string OverridesRoot)? DeclaredSkinSeason()
    {
        var set = _environment.SkinSeasons.Get(Pack.Manifest.SkinSeason);
        if (set is null)
            return null;
        var installation = _environment.LocateInstall();
        if (installation is null)
            return null;
        return (set, installation.InstallOverridesDirectory);
    }

    /// <summary>Read-only Skin Season Manager status for this pack's declared season (Skins tab
    /// panel). Null when the pack declares none / no install.</summary>
    public SkinSeasonStatus? CurrentSkinSeasonStatus()
    {
        if (DeclaredSkinSeason() is not { } declared)
            return null;
        return SkinSeasonManager.Inspect(declared.Set, _environment.SkinSeasons, declared.OverridesRoot);
    }

    /// <summary>Switches the install onto this pack's declared skin season (backup-first,
    /// all-or-nothing; unrecognized user files hold the swap behind the force gate). Skin pointer
    /// files only, the career DB / sim / oracle are never touched.</summary>
    public SkinSeasonApplyResult ActivateSkinSeason(bool force = false)
    {
        if (DeclaredSkinSeason() is not { } declared)
            return new SkinSeasonApplyResult
            {
                Success = false,
                Applied = 0,
                Message = "This pack declares no managed skin season (or no AMS2 install was found).",
            };
        return SkinSeasonManager.Activate(
            declared.Set, _environment.SkinSeasons, declared.OverridesRoot, force,
            _environment.Clock.GetUtcNow());
    }

    /// <inheritdoc />
    public IReadOnlyList<SkinSetOwnershipStatus> SkinOwnership()
    {
        if (_environment.LocateInstall() is null)
            return [];
        var roots = OverrideRoots();
        return _environment.SkinSeasons.Sets.Values
            .Select(set => ModOwnership.Inspect(set, roots))
            .Where(status => status is not null)
            .OrderBy(status => status!.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray()!;
    }

    /// <inheritdoc />
    public SkinOwnershipCaptureResult CaptureSkinOwnership()
    {
        var owned = OwnedSets();
        if (owned.Count == 0)
        {
            return new SkinOwnershipCaptureResult
            {
                Success = false,
                Captured = [],
                Errors = [],
                Message = "No app-owned skin sets (no ownership.json manifests in the skin-season library).",
            };
        }

        var roots = OverrideRoots();
        var captured = new List<string>();
        var errors = new List<string>();
        foreach (var set in owned)
        {
            var result = ModOwnership.Capture(
                set, roots, ModOwnership.VaultDirectoryFor(_environment.DocumentsDirectory, set.Key));
            captured.AddRange(result.Captured);
            errors.AddRange(result.Errors);
        }

        return new SkinOwnershipCaptureResult
        {
            Success = errors.Count == 0,
            Captured = captured,
            Errors = errors,
            Message = errors.Count == 0
                ? $"Captured {captured.Count} payload folder(s) into the app vault, the mod files are safe now."
                : $"Captured {captured.Count} folder(s), {errors.Count} failed.",
        };
    }

    /// <inheritdoc />
    public SkinOwnershipRepairResult RepairSkinOwnership()
    {
        var owned = OwnedSets();
        if (owned.Count == 0)
        {
            return new SkinOwnershipRepairResult
            {
                Success = false,
                Repaired = [],
                Skipped = [],
                Errors = [],
                Backups = [],
                Message = "No app-owned skin sets (no ownership.json manifests in the skin-season library).",
            };
        }

        var roots = OverrideRoots();
        var repaired = new List<string>();
        var skipped = new List<string>();
        var errors = new List<string>();
        var backups = new List<string>();
        foreach (var set in owned)
        {
            var result = ModOwnership.Repair(
                set, roots, ModOwnership.VaultDirectoryFor(_environment.DocumentsDirectory, set.Key),
                _environment.Clock.GetUtcNow());
            repaired.AddRange(result.Repaired);
            skipped.AddRange(result.Skipped);
            errors.AddRange(result.Errors);
            backups.AddRange(result.Backups);
        }

        return new SkinOwnershipRepairResult
        {
            Success = errors.Count == 0,
            Repaired = repaired,
            Skipped = skipped,
            Errors = errors,
            Backups = backups,
            Message = (errors.Count, repaired.Count) switch
            {
                (0, 0) => "Every app-owned mod payload is intact.",
                (0, _) => $"Repaired {repaired.Count} model(s) from the app vault, previous files backed up.",
                (_, 0) => $"Nothing could be repaired, {errors.Count} problem(s).",
                _ => $"Repaired {repaired.Count} model(s), {errors.Count} failed.",
            },
        };
    }

    /// <summary>Every library set carrying an ownership manifest, or empty when the ownership
    /// feature covers nothing (or there is no install to own files against).</summary>
    private List<SkinSeasonSet> OwnedSets() =>
        _environment.LocateInstall() is null
            ? []
            : _environment.SkinSeasons.Sets.Values
                .Where(set => set.Ownership is not null)
                .OrderBy(set => set.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

    /// <summary>The override roots ownership inspects/repairs against (install-side first, the
    /// Documents-side second), empty when no install is found.</summary>
    private IReadOnlyList<string> OverrideRoots() =>
        _environment.LocateInstall() is { } installation
            ? LiveryOverrideScanner.CandidateOverrideRoots(
                installation.InstallDirectory, _environment.DocumentsDirectory)
            : [];

    /// <summary>The per-seat cosmetic staging overrides this season still carries (v4
    /// staging_override table, written by the retired grid editor; the Grid Preview now surfaces
    /// a count + clear affordance). Read-only projection over a non-journaled table, the sim
    /// never sees it.</summary>
    public IReadOnlyDictionary<string, SeatStagingOverride> SeatStagingOverrides() =>
        StagingOverrideStore.Read(_database, _seasonId);

    /// <summary>Persists one seat's rename/rebind (empty clears it). Applied at the next stage to the
    /// custom-AI file only; the journal/fold are untouched, so replay stays byte-identical.</summary>
    public void SetSeatStagingOverride(string liveryKey, SeatStagingOverride seatOverride) =>
        StagingOverrideStore.Set(_database, _seasonId, liveryKey, seatOverride);

    /// <summary>The player's driver id + display name for name-rendering screens, or null when there is
    /// no name to show (a real-driver career with no character name, the call site falls back to the
    /// seat's authored driver).</summary>
    public (string DriverId, string DisplayName)? PlayerIdentity() =>
        PlayerDisplayName() is { } name ? (_playerDriverId, name) : null;

    /// <summary>The player-facing name for the grid card / news / standings / review screens: their
    /// chosen character name, or, for a distinct-driver player (the SMGP clean-swap / own-entrant
    /// model, whose synthetic id is NOT in pack.Drivers and whose occupied car still carries the BENCHED
    /// AI's name), a stable default so the player never renders as that AI (or the raw id). Null for a
    /// real-driver career with no character name, so callers keep the seat's authored driver (the
    /// historical driver the player wears), every non-distinct career stays display-identical. A
    /// pure-racing <see cref="PlayerCareerState.CustomDisplayName"/> (Racing Passport's one
    /// identity field) resolves ahead of the authored name when the player chose one.</summary>
    private string? PlayerDisplayName() =>
        CharacterName() ?? CurrentPlayerState()?.CustomDisplayName ?? (IsDistinctDriverPlayer ? PlayerDefaultName : null);

    /// <summary>The player races as their OWN distinct entrant (SMGP clean-swap, or a custom-livery
    /// own-entrant) rather than impersonating a pack driver, so their id is the synthetic one.</summary>
    private bool IsDistinctDriverPlayer =>
        string.Equals(_playerDriverId, RoundGridResolver.SyntheticPlayerDriverId, StringComparison.Ordinal);

    /// <summary>Shown for a distinct-driver player who left the wizard's pre-seeded name blank.</summary>
    private const string PlayerDefaultName = "You";

    /// <summary>The name of the team the player currently drives for, or null when unknown.</summary>
    public string? PlayerTeamName()
    {
        string? teamId = CurrentPlayerState()?.CurrentTeamId;
        if (string.IsNullOrEmpty(teamId))
            return null;
        return Pack.Teams.FirstOrDefault(t => string.Equals(t.Id, teamId, StringComparison.Ordinal))?.Name ?? teamId;
    }

    public CarSpecCardViewModel? PlayerCarSpec()
    {
        if (_environment.RulesDirectory is null)
            return null;
        string? teamId = CurrentPlayerState()?.CurrentTeamId;
        if (string.IsNullOrEmpty(teamId))
            return null;
        string? vehicle = Pack.Teams
            .FirstOrDefault(t => string.Equals(t.Id, teamId, StringComparison.Ordinal))
            ?.CarVehicleIds.FirstOrDefault();
        var catalog = _environment.Rules.CarSpecs;
        return CarSpecCardViewModel.From(catalog.For(teamId, vehicle), catalog.BarMax);
    }

    /// <summary>Resolves the round grid, marking the player's seat when their entry covers
    /// this round (an entry's rounds range may exclude it, then the grid is all-AI). When the
    /// career carries a character, the player seat is patched from it here too, so the STAGED
    /// AMS2 file gets the perk car scalars and the briefing's expectation matches the fold (the
    /// fold applies the identical patch). A character-free career resolves an unchanged grid.</summary>
    /// <param name="applyWeekendForm">Ratings Phase 3: overlay the round's per-race form (a FormAware
    /// career). ONLY the "sim's view" resolves opt in (the shown expected finish + slider), so they
    /// equal what the fold scored. STAGING keeps this false, <c>GridStager.Build</c> applies the same
    /// nudge to the written AMS2 file, so applying it here too would double-count.</param>
    private GridPlan ResolveGrid(
        int round,
        bool applyWeekendForm = false,
        bool applyPlayerCharacter = true)
    {
        // Resolve with the career's chosen field (v0.6.0) so the DISPLAY + staged AI file match what
        // the fold scores. Null selection = whole pack (byte-identical).
        // ALWAYS seat the player, even on a round their car did not qualify (1988 pre-qualifying: a
        // driver like Coloni's Tarquini DNQs some rounds; the resolver seats them from their own entry
        // and CapToGridSize drops the slowest peer to hold AMS2's hardcoded grid size), and even on a
        // custom/non-standard livery that matches no pack entry (the resolver seats them as their own
        // synthetic entrant). This mirrors the fold + CurrentExpectedFinish, which seat the player
        // unconditionally, so the staged AMS2 file matches what the sim scores. Existing careers on a
        // qualifying pack livery resolve byte-identically.
        // SMGP (M3): the latest folded mode state reseats swapped drivers, display, staging and
        // the fold all read the same swaps, so the car the player sees IS the car the sim scores
        // and the staged AMS2 file names the reseated drivers. Null outside the mode.
        var smgp = CurrentSmgpState();

        // SMGP CLEAN-SWAP model (Mike): a career created with a DISTINCT player driver id seats the
        // player DIRECTLY on their current car (smgp.CurrentSeatLivery) as their own driver, the car's
        // authored AI benches, everyone else keeps their home seat, and the fresh resolve is the whole
        // truth (no AI seat overrides, no cascade). The vacated car reverts to its authored AI for free.
        // Pre-change SMGP careers (player id == a pack driver) keep the old override path below.
        if (smgp is not null &&
            string.Equals(_playerDriverId, RoundGridResolver.SyntheticPlayerDriverId, StringComparison.Ordinal))
        {
            return RoundGridResolver.Resolve(Pack, round,
                new PlayerSeat
                {
                    Ams2LiveryName = smgp.CurrentSeatLivery,
                    DriverId = _playerDriverId,
                    Character = applyPlayerCharacter ? CurrentCharacterPatch(round) : null,
                },
                CurrentGridSelection(), applyWeekendForm: applyWeekendForm);
        }

        return RoundGridResolver.Resolve(Pack, round,
            new PlayerSeat
            {
                Ams2LiveryName = _playerLiveryName,
                Character = applyPlayerCharacter ? CurrentCharacterPatch(round) : null,
            },
            CurrentGridSelection(), applyWeekendForm: applyWeekendForm,
            seatOverrides: smgp?.AiSeatOverrides is { Count: > 0 } overrides ? overrides : null,
            playerSeatOverride: smgp?.CurrentSeatLivery);
    }

    /// <summary>The pending two-wins offer awaiting the promotion screen's decision (3c-2), or null.
    /// Reads the latest folded state, so it appears the moment a round's battle deferred an offer and
    /// clears the moment <see cref="ResolveSmgpOffer"/> answers it.</summary>
    public Companion.Core.Smgp.SmgpPendingOffer? CurrentSmgpPendingOffer() =>
        CurrentSmgpState()?.PendingSwap;

    /// <summary>Resolve the pending offer on the last folded round (3c-2), the promotion screen's
    /// accept/decline. Delegates to the atomic <see cref="ReplayService.ResolveSmgpOffer"/>.</summary>
    public void ResolveSmgpOffer(bool accept)
    {
        int round = MaxAppliedRound;
        if (round <= 0 || CurrentSmgpState()?.PendingSwap is null)
            throw new InvalidOperationException(
                "There is no pending seat-swap offer to resolve on this career.");
        ReplayService.ResolveSmgpOffer(_database, _seasonId, Pack, round, accept, NowUtc());

        // The offer is now cleared: if it was deferred on the season's FINAL round, season end was
        // held back (EnsureSeasonEnd), fold it now, on the resolved seat, matching replay's order.
        if (SeasonComplete)
            EnsureSeasonEnd();
    }

    /// <summary>The player's current SMGP team id (follows seat swaps), or null outside the mode —
    /// the shell captures it before applying a round to detect a forced demotion afterwards.</summary>
    public string? CurrentSmgpTeamId() =>
        CurrentSmgpState() is { } state ? TeamOfLivery(state.CurrentSeatLivery) : null;

    /// <summary>The rival named in the most-recently applied round (its stored <c>SmgpRival</c> call), or
    /// null. A pure read over the persisted envelopes, never a fold input. The named rival is a per-round
    /// choice; this surfaces the LATEST for the season standings' "your rival" highlight.</summary>
    public string? CurrentSmgpRivalDriverId() =>
        ResultStore.ReadSeasonResults(_database, _seasonId)
            .OrderBy(r => r.Round)
            .LastOrDefault()?
            .ToEnvelope().SmgpRival?.RivalDriverId;

    /// <summary>The promotion screen (3c-3): a pending two-wins offer's new-team story. Null when no
    /// offer is pending (or outside the mode).</summary>
    public SmgpPromotionModel? CurrentSmgpPromotion() =>
        CurrentSmgpPendingOffer() is { } offer
            ? BuildPromotion(SmgpPromotionKind.PromotionOffer, offer.OfferedSeat, offer.RivalDriverId)
            : null;

    /// <summary>The demotion screen (3c-3): shown when the last applied round forced the player down a
    /// tier, the smgp team moved away from <paramref name="previousTeamId"/> with NO pending offer
    /// (a promotion is deferred, so a team change without one is a forfeit / lost title defense).</summary>
    public SmgpPromotionModel? CurrentSmgpDemotion(string? previousTeamId)
    {
        if (CurrentSmgpState() is not { } state || CurrentSmgpPendingOffer() is not null)
            return null;
        string? teamNow = TeamOfLivery(state.CurrentSeatLivery);
        return teamNow is not null && !string.Equals(teamNow, previousTeamId, StringComparison.Ordinal)
            ? BuildPromotion(SmgpPromotionKind.Demotion, state.CurrentSeatLivery, rivalDriverId: null)
            : null;
    }

    /// <summary>The team id a livery belongs to (its first authored entry), or null.</summary>
    private string? TeamOfLivery(string livery) =>
        Pack.Entries.FirstOrDefault(e => string.Equals(e.Ams2LiveryName, livery, StringComparison.Ordinal))?.TeamId;

    /// <summary>Builds the promotion/demotion screen model from the target seat + the team-profiles
    /// catalog (3c-1). Display-only; absent art / unauthored profile simply collapse their fields.</summary>
    private SmgpPromotionModel BuildPromotion(SmgpPromotionKind kind, string seatLivery, string? rivalDriverId)
    {
        var entry = Pack.Entries.FirstOrDefault(e => string.Equals(e.Ams2LiveryName, seatLivery, StringComparison.Ordinal));
        string? teamId = entry?.TeamId;
        string teamName = Pack.Teams.FirstOrDefault(t => string.Equals(t.Id, teamId, StringComparison.Ordinal))?.Name
            ?? teamId ?? "";
        string shortId = teamId is not null && teamId.StartsWith("team.", StringComparison.Ordinal)
            ? teamId["team.".Length..] : teamId ?? "";
        var profile = teamId is not null ? _environment.Rules.SmgpTeamProfiles.ForTeam(teamId) : null;
        string? rivalName = rivalDriverId is not null
            ? Pack.Drivers.FirstOrDefault(d => string.Equals(d.Id, rivalDriverId, StringComparison.Ordinal))?.Name ?? rivalDriverId
            : null;

        return new SmgpPromotionModel
        {
            Kind = kind,
            Headline = kind == SmgpPromotionKind.PromotionOffer
                ? $"AN OFFER FROM {teamName.ToUpperInvariant()}"
                : $"RELEGATED TO {teamName.ToUpperInvariant()}",
            TeamName = teamName,
            TeamPhotoKey = shortId,
            PlayerImageKey = Companion.ViewModels.Wizard.GridSeatChoice.PlayerImageKey(teamId ?? ""),
            CarKey = entry?.DriverId,
            Motto = profile?.Motto is { Length: > 0 } motto ? motto : null,
            History = profile?.History ?? [],
            Quotes = profile?.Quotes ?? [],
            RivalName = rivalName,
        };
    }

    /// <summary>The locked 17-season campaign FINALE (Mike's "final final screen"), or null. Non-null
    /// ONLY when the current SMGP season is a COMPLETED campaign summit, the player reached the end of
    /// all <see cref="Companion.Core.Smgp.SmgpRules.CampaignSeasons"/> seasons without the career ending
    /// on the D-floor. A champion-of-all-17 run is flagged flawless (revealing <c>ultimate.jpg</c> over
    /// <c>special.jpg</c>). Pure DISPLAY-ONLY read over folded state (season ordinal + Titles + CareerOver):
    /// no fold change, no journal row, no seed, the byte-identical replay gate is untouched. The secret
    /// hero key is emitted ONLY here and ONLY when unlocked, so the art is unreachable everywhere else.</summary>
    public SmgpFinaleModel? SmgpFinale()
    {
        if (!string.Equals(Pack.Manifest.CareerStyle, Companion.Core.Smgp.SmgpRules.CareerStyle, StringComparison.Ordinal))
            return null;
        // The current season's title is banked by SeasonEndPipeline in stage=end, AFTER the final
        // round state. CurrentPlayerState() deliberately prefers that authoritative end state once
        // the season is complete, so a perfect season-17 run sees all 17 titles and unlocks ultimate.jpg.
        // Its normal round/start fallbacks keep an older completed career without stage=end readable.
        if (!SeasonComplete || CurrentPlayerState()?.Smgp is not { } state)
            return null;
        if (!Companion.Core.Smgp.SmgpRules.CampaignComplete(_seasonOrdinal, state.CareerOver))
            return null;

        bool flawless = Companion.Core.Smgp.SmgpRules.CampaignFlawless(_seasonOrdinal, state.Titles, state.CareerOver);
        return new SmgpFinaleModel
        {
            Headline = flawless ? "THE FLAWLESS EMPEROR" : "SEVENTEEN SEASONS CONQUERED",
            Subhead = flawless
                ? "Champion of every season across the whole SEGA world. No one has driven what you have driven."
                : "You went the distance, all seventeen seasons survived. The replica is truly beaten.",
            IsFlawless = flawless,
            HeroImageKey = flawless ? "ultimate" : "special",
            Record =
            [
                $"{_seasonOrdinal} SEASONS CONQUERED",
                $"{state.Titles} {(state.Titles == 1 ? "CHAMPIONSHIP" : "CHAMPIONSHIPS")}",
            ],
        };
    }

    /// <summary>The SMGP mode's LATEST folded state, the last folded round's, else the season
    /// start's, or null outside the mode. Deliberately NOT cached: every folded round can move
    /// seats, and the next round's grid must show them.</summary>
    private Companion.Core.Smgp.SmgpState? CurrentSmgpState()
    {
        int lastRound = MaxAppliedRound;
        if (lastRound > 0 &&
            StateStore.ReadRoundPlayerState(_database, _seasonId, lastRound) is { } folded)
            return folded.Player.Smgp;
        return StateStore.ReadPlayerState(_database, _seasonId, StateStore.StageStart)?.Smgp;
    }

    /// <summary>The SMGP briefing panel's data (M3 slice 5): the game's round header, D.P.
    /// readout, the pit-crew line, the forced title-defense challenger and every namable rival
    /// on this round's (swap-aware) grid. Null outside the mode or once the season is complete —
    /// the panel never renders for a normal career. Vocabulary per docs/dev/smgp-design.md.</summary>
    public SmgpBriefingModel? CurrentSmgpBriefing()
    {
        if (CurrentSmgpState() is not { } state)
            return null;

        // A floor knock-out on the authored final round is both SeasonComplete and terminal. Keep
        // projecting the existing briefing bind so reopening that career still reaches the fired ending;
        // ordinary completed SMGP seasons remain briefing-free and proceed to review/finale as before.
        if (SeasonComplete)
        {
            if (!state.CareerOver)
                return null;

            int terminalRound = Math.Max(1, MaxAppliedRound);
            var terminalPackRound = Pack.Season.Rounds.FirstOrDefault(r => r.Round == terminalRound);
            return new SmgpBriefingModel
            {
                RoundHeader = $"{(terminalPackRound?.Name ?? $"Round {terminalRound}").ToUpperInvariant()} · ROUND {terminalRound}",
                SeasonLine = "SEASON  -",
                CareerLine = "",
                AdviceLine = _environment.Rules.SmgpPitCrewAdvice.Line(
                    terminalPackRound?.Name ?? "", QuoteSeed(terminalPackRound?.Name ?? "round", terminalRound)),
                Titles = state.Titles,
                SeasonOrdinal = _seasonOrdinal,
                SeasonsTotal = Companion.Core.Smgp.SmgpRules.CampaignSeasons,
                CareerOver = true,
                ForcedChallengerDriverId = null,
                Rivals = [],
            };
        }

        int round = CurrentRoundNumber;
        var packRound = Pack.Season.Rounds.FirstOrDefault(r => r.Round == round);
        var seats = ResolveGrid(round).Seats;
        var teamsById = Pack.Teams.ToDictionary(t => t.Id, StringComparer.Ordinal);
        // Your own TEAMMATE is never a namable rival (a two-car team's sister seat): beating him
        // twice would "offer" you your own team, and losing would forfeit your car to him.
        var playerSeatSeat = seats.FirstOrDefault(s => s.IsPlayer);
        string? playerTeamId = playerSeatSeat?.TeamId;
        // The CHALLENGE-TIER rule (Mike): you may only name a rival in the tier directly ABOVE you
        // (the seat you climb toward) or ANY tier below, never two tiers up, never your own tier.
        char? playerTier = playerSeatSeat is null ? null
            : Companion.Core.Smgp.SmgpRules.Tier(
                teamsById.TryGetValue(playerSeatSeat.TeamId, out var pt) ? pt.Prestige : 3);
        string? forcedChallenger = Companion.Core.Smgp.SmgpSchedule.ForcedChallenger(Pack, state, round);

        // The rival dossier gets each rival's player-vs-them head-to-head (Task 3.2), from the same
        // career-wide pass the Paddock uses. Profiles supply gendered pronouns for the copy (Mika is female).
        var depth = BuildDriverDepthIndex();
        var profiles = _environment.Rules.SmgpDriverProfiles;

        var rivals = new List<SmgpRivalOption>();
        foreach (var seat in seats)
        {
            if (seat.IsPlayer ||
                string.Equals(seat.TeamId, playerTeamId, StringComparison.Ordinal))
                continue;
            teamsById.TryGetValue(seat.TeamId, out var team);
            char rivalTier = Companion.Core.Smgp.SmgpRules.Tier(team?.Prestige ?? 3);
            // Tier gate, but the forced title-defense challenger is always namable regardless.
            bool isForced = string.Equals(seat.DriverId, forcedChallenger, StringComparison.Ordinal);
            if (!isForced && playerTier is { } ptier &&
                !Companion.Core.Smgp.SmgpRules.CanChallenge(ptier, rivalTier))
                continue;
            string? vehicle = team?.CarVehicleIds.FirstOrDefault();
            // The MACHINE block reads the car's DISPLAY name from the extracted library
            // ("F-Classic Gen3 M4"), never the raw vehicle id.
            string machine = vehicle is null
                ? seat.TeamName.ToUpperInvariant()
                : _environment.ContentLibrary.Vehicles.TryGetValue(vehicle, out var v) &&
                  (v.Name ?? v.VehicleName) is { Length: > 0 } displayName
                    ? displayName
                    : vehicle;
            var tally = state.TallyFor(seat.DriverId);
            rivals.Add(new SmgpRivalOption
            {
                DriverId = seat.DriverId,
                DriverName = seat.DriverName,
                TeamId = seat.TeamId,
                TeamName = seat.TeamName,
                MachineLine = machine + (team?.Performance.PowerScalar is { } power && power != 1.0
                    ? $" · POWER ×{power.ToString("0.###", CultureInfo.InvariantCulture)}"
                    : ""),
                CarSpec = CarSpecCardViewModel.From(
                    _environment.Rules.CarSpecs.For(seat.TeamId, vehicle),
                    _environment.Rules.CarSpecs.BarMax),
                // Their line varies by WHO they are and WHERE the ladder stands (first meeting, you a win
                // up, or them a win up). Display-only, so a per-round seed just keeps it stable on re-open.
                Quote = RivalQuote(seat.DriverId, tally, round),
                OfferOnWin = tally.PlayerStreak == 1,
                ForfeitOnLoss = tally.RivalStreak == 1,
                // Gendered pronouns for the naming copy (Mika = female → "her"). Default he/him.
                Pronouns = profiles.ForDriver(seat.DriverId)?.Pronouns ?? Companion.Core.Smgp.SmgpPronouns.Default,
                // The rival's ladder CLASS for the (coloured) picker dropdown, so you know who you can climb toward.
                Tier = rivalTier.ToString(),
                TierLabel = $"CLASS {rivalTier}",
                TierColorHex = TierColorHex(rivalTier),
                // Player-vs-this-rival head-to-head for the dossier (Task 3.2), null before they have met.
                HeadToHead = depth.GetValueOrDefault(seat.DriverId)?.HeadToHead,
            });
        }

        // The player's live readout (replaces the old "D.P." points abbreviation): their season standing
        // plus the career record they are building from zero (the AI carry their pre-history).
        var playerStanding = CurrentStandings()?.Drivers
            .FirstOrDefault(d => string.Equals(d.DriverId, _playerDriverId, StringComparison.Ordinal));
        var (playerCareer, playerSeason) = BuildDriverStats(
            _playerDriverId, isPlayer: true,
            AccrueSeasonCounts().GetValueOrDefault(_playerDriverId) ?? Companion.Core.Smgp.SmgpAccruedStats.Empty,
            playerStanding, seasonComplete: false, champion: null, baseline: null,
            prior: PriorSeasonsSmgpTotals().GetValueOrDefault(_playerDriverId));
        string seasonLine = playerSeason is { Position: { } pos } ps
            ? $"SEASON  P{pos} · {ps.Points} PTS"
            : "SEASON  -";
        string careerLine = FormatCareerLine(playerCareer);

        // A forced challenger who is not actually on this round's grid (his introduction could
        // not resolve) cannot be battled, surface a free pick instead of a lock on nothing.
        if (forcedChallenger is not null &&
            !rivals.Any(r => string.Equals(r.DriverId, forcedChallenger, StringComparison.Ordinal)))
            forcedChallenger = null;

        return new SmgpBriefingModel
        {
            RoundHeader = $"{(packRound?.Name ?? $"Round {round}").ToUpperInvariant()} · ROUND {round}",
            SeasonLine = seasonLine,
            CareerLine = careerLine,
            AdviceLine = _environment.Rules.SmgpPitCrewAdvice.Line(
                packRound?.Name ?? "", QuoteSeed(packRound?.Name ?? "round", round)),
            Titles = state.Titles,
            SeasonOrdinal = _seasonOrdinal,
            SeasonsTotal = Companion.Core.Smgp.SmgpRules.CampaignSeasons,
            CareerOver = state.CareerOver,
            ForcedChallengerDriverId = forcedChallenger,
            Rivals = rivals,
        };
    }

    /// <summary>The SMGP Paddock lens (driver/team-preview tab): every grid driver as a card (bio +
    /// predetermined career stats + team) and every team as a card (motto + history + quotes + roster),
    /// joined from the pinned pack roster and the SMGP reference data. DISPLAY-ONLY. Null outside the
    /// SMGP mode or when no rules folder is loaded (the hub then never adds the tab).</summary>
    public SmgpPaddockModel? SmgpPaddock()
    {
        if (_environment.RulesDirectory is null ||
            !string.Equals(Pack.Manifest.CareerStyle, Companion.Core.Smgp.SmgpRules.CareerStyle, StringComparison.Ordinal))
            return null;

        var profiles = _environment.Rules.SmgpDriverProfiles;
        var stats = _environment.Rules.SmgpDriverStats;
        var teamProfiles = _environment.Rules.SmgpTeamProfiles;

        var teamsById = new Dictionary<string, PackTeam>(StringComparer.Ordinal);
        foreach (var t in Pack.Teams)
            teamsById.TryAdd(t.Id, t);

        // A driver's authored seat (team + number) from entries.json, one entry per driver.
        var entryByDriver = new Dictionary<string, PackEntry>(StringComparer.Ordinal);
        foreach (var e in Pack.Entries)
            entryByDriver.TryAdd(e.DriverId, e);

        // Live stats: per-driver counts accrued from this season's folded results + the current
        // standings for points/position. The player builds their record from zero; the AI carry their
        // predetermined baseline forward. Display-only (a projection over the fold, like the standings).
        var accrued = AccrueSeasonCounts();
        // The cross-season rollup, computed ONCE for the whole paddock (each card just looks its driver
        // up), so a 17-season career card spans every season, not just the current one.
        var prior = PriorSeasonsSmgpTotals();
        // Per-driver depth (head-to-head vs the player, per-venue bests, recent form), one pass over the
        // whole career, looked up per AI card. Display-only.
        var depth = BuildDriverDepthIndex();
        var standings = CurrentStandings();
        bool complete = SeasonComplete;
        string? champion = complete
            ? standings?.Drivers.FirstOrDefault(d => d.Position == 1)?.DriverId
            : null;

        SmgpDriverCard BuildCard(
            string driverId, string name, string teamId, string teamName, string? number,
            string portraitKey, string carKey, bool isPlayer,
            Companion.Core.Smgp.SmgpDriverProfile? profile, int prestige)
        {
            var (career, season) = BuildDriverStats(
                driverId, isPlayer,
                accrued.GetValueOrDefault(driverId) ?? Companion.Core.Smgp.SmgpAccruedStats.Empty,
                standings?.Drivers.FirstOrDefault(s => string.Equals(s.DriverId, driverId, StringComparison.Ordinal)),
                complete, champion,
                isPlayer ? null : stats.ForDriver(driverId),
                prior.GetValueOrDefault(driverId));
            var d = isPlayer ? null : depth.GetValueOrDefault(driverId);
            return new SmgpDriverCard
            {
                DriverId = driverId, Name = name, TeamId = teamId, TeamName = teamName, Number = number,
                PortraitKey = portraitKey, CarKey = carKey,
                Epithet = isPlayer ? "YOU" : profile?.Epithet ?? "",
                Bio = isPlayer ? BuildPlayerBio(name, teamName, career) : profile?.Bio ?? [],
                Quotes = isPlayer ? [] : profile?.Quotes ?? [],
                IsPlayer = isPlayer, Career = career, Season = season, Prestige = prestige,
                // Slice 2 depth (AI drivers only): head-to-head vs the player, per-venue bests, recent form.
                HeadToHead = d?.HeadToHead,
                PerTrackBest = d?.PerTrackBest ?? [],
                FormRecent = d?.FormRecent ?? [],
                // Slice 1 (player narrative) fills Timeline + NarrativeIntro; AI cards leave them empty.
            };
        }

        var drivers = new List<SmgpDriverCard>(Pack.Drivers.Count + 1);
        foreach (var d in Pack.Drivers)
        {
            entryByDriver.TryGetValue(d.Id, out var entry);
            PackTeam? team = entry is not null ? teamsById.GetValueOrDefault(entry.TeamId) : null;
            var profile = profiles.ForDriver(d.Id);
            drivers.Add(BuildCard(
                d.Id, profile?.Name is { Length: > 0 } n ? n : d.Name,
                team?.Id ?? entry?.TeamId ?? "", team?.Name ?? "", entry?.Number,
                d.Id, d.Id, isPlayer: false, profile, team?.Prestige ?? 0));
        }

        // Most-storied first: top houses lead, then the biggest career.
        var orderedDrivers = drivers
            .OrderByDescending(c => c.Prestige)
            .ThenByDescending(c => c.Career?.Points ?? 0)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .ToList();

        // The player LEADS the roster, their own card, built from their current seat + live record.
        var playerSeat = complete ? null : CurrentGrid().FirstOrDefault(s => s.IsPlayer);
        string playerTeamId = playerSeat?.TeamId ?? CurrentSmgpTeamId() ?? "";
        var playerTeam = teamsById.GetValueOrDefault(playerTeamId);
        string? playerLivery = playerSeat?.Ams2LiveryName ?? CurrentSmgpState()?.CurrentSeatLivery;
        string? playerCarArtId = playerLivery is null ? null
            : Pack.Entries.FirstOrDefault(e => string.Equals(e.Ams2LiveryName, playerLivery, StringComparison.Ordinal))?.DriverId;
        var playerCard = BuildCard(
            _playerDriverId, PlayerDisplayName() ?? PlayerDefaultName, playerTeamId, playerTeam?.Name ?? "",
            null, Companion.ViewModels.Wizard.GridSeatChoice.PlayerImageKey(playerTeamId),
            playerCarArtId ?? _playerDriverId, isPlayer: true, null, playerTeam?.Prestige ?? 0);
        // The evolving narrative (Slice 1): the milestone timeline + a live intro, from the folded career.
        var (timeline, intro) = BuildPlayerTimeline(playerCard.Career, playerCard.Season, playerCard.TeamName);
        orderedDrivers.Insert(0, playerCard with { Timeline = timeline, NarrativeIntro = intro });

        // The sponsor board (Mike's Paddock Sponsors tab; seed of Tycoon mode): every authored sponsor as
        // a card, its backed-team ids resolved to display names from the pack. Empty when none authored.
        // Built BEFORE the teams so each team card can cross-reference the sponsors that back it.
        string TeamName(string teamId) =>
            teamsById.TryGetValue(teamId, out var t) ? t.Name : teamProfiles.ForTeam(teamId)?.Name ?? teamId;
        var sponsors = _environment.Rules.SmgpSponsors.All
            .Select(s => new SmgpSponsorCard
            {
                Id = s.Id,
                Name = s.Name,
                Industry = s.Industry,
                Tier = s.Tier,
                Tagline = s.Tagline,
                Story = s.Story,
                BrandColorHex = s.BrandColorHex,
                LogoKey = s.Id.StartsWith("sponsor.", StringComparison.Ordinal) ? s.Id["sponsor.".Length..] : s.Id,
                FoundedFlavor = s.FoundedFlavor,
                TeamIds = s.Teams,
                TeamNames = s.Teams.Select(TeamName).ToList(),
            })
            .ToList();

        var teams = new List<SmgpTeamCard>(Pack.Teams.Count);
        foreach (var t in Pack.Teams)
        {
            var tp = teamProfiles.ForTeam(t.Id);
            var roster = orderedDrivers
                .Where(c => string.Equals(c.TeamId, t.Id, StringComparison.Ordinal))
                .Select(c => new SmgpTeamRosterLine
                {
                    DriverId = c.DriverId,
                    Name = c.Name,
                    IsPlayer = c.IsPlayer,
                    SeasonLine = SeasonLineOf(c.Season),
                    CareerLine = CareerTallies(c.Career),
                })
                .ToList();
            teams.Add(new SmgpTeamCard
            {
                TeamId = t.Id,
                Name = tp?.Name is { Length: > 0 } tn ? tn : t.Name,
                Motto = tp?.Motto ?? "",
                LogoKey = t.Id,
                History = tp?.History ?? [],
                Quotes = tp?.Quotes ?? [],
                DriverNames = roster.Select(r => r.Name).ToList(),
                Prestige = t.Prestige,
                // Slice 3 depth: tier label, accent colour, the live roster, and the sponsors that back it.
                Tier = $"Level {Companion.Core.Smgp.SmgpRules.Tier(t.Prestige)}",
                PaletteHex = Companion.ViewModels.Shell.TeamPalette.For(t.Id),
                Roster = roster,
                Sponsors = sponsors
                    .Where(s => s.TeamIds.Contains(t.Id, StringComparer.Ordinal))
                    .Select(s => new SmgpTeamSponsorRef
                    {
                        Id = s.Id, Name = s.Name, Tier = s.Tier, BrandColorHex = s.BrandColorHex,
                    })
                    .ToList(),
            });
        }

        var orderedTeams = teams
            .OrderByDescending(c => c.Prestige)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .ToList();

        // A rotating "paddock rumor" line (Task 4), seeded off the applied-round count so it changes as the
        // career progresses yet stays stable on a re-open. DISPLAY-ONLY flavour, grounded in real grid facts.
        string leaderName = standings?.Drivers.FirstOrDefault(d => d.Position == 1) is { } lead
            ? DriverDisplayName(lead.DriverId) : "";
        var rumorTokens = DispatchTokens(PlayerDisplayName() ?? PlayerDefaultName, playerTeam?.Name ?? "",
            rival: "", venue: "", season: _seasonOrdinal, number: 0, subject: "", other: "",
            leader: leaderName, benchmark: DriverDisplayName(ResolveBenchmarkDriverId()));
        var rumorStream = new StreamFactory(MasterSeedU)
            .CreateStream(DispatchStream, _seasonOrdinal, standings?.AfterRound ?? 0, "rumor");
        string rumor = _environment.Rules.SmgpDispatchCorpus.Rumor(rumorTokens, rumorStream);

        return new SmgpPaddockModel
        {
            Drivers = orderedDrivers, Teams = orderedTeams, Sponsors = sponsors, PaddockRumor = rumor,
        };
    }

    /// <summary>The Tycoon Team Mode read-only DATA SPINE (Task 5): the player's team dashboard + every team
    /// ranked as the competitive landscape + a flavour team-of-the-season seed. Builds on <see cref="SmgpPaddock"/>
    /// (reusing its team cards' roster/sponsors/tier/history) and the live standings for a DERIVED constructors'
    /// ranking (SMGP is driver-focused, so team points = its drivers' counted points summed, a display read,
    /// not an official constructors' title). Pure DISPLAY-ONLY: no fold mechanics, so replay stays byte-identical.
    /// Null outside the mode.</summary>
    public SmgpTeamDashboard? SmgpTeamDashboard()
    {
        var paddock = SmgpPaddock();
        if (paddock is null || paddock.Teams.Count == 0)
            return null;

        // The player's current team id (live seat, else the folded SMGP team).
        string? playerTeamId = (SeasonComplete ? null : CurrentGrid().FirstOrDefault(s => s.IsPlayer)?.TeamId)
            ?? CurrentSmgpTeamId();

        // DERIVED constructors' points: sum each team's drivers' counted points from the live standings. The
        // player's synthetic driver maps to their current team; every AI driver via its pack entry.
        var standings = CurrentStandings();
        var driverTeam = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var e in Pack.Entries)
            driverTeam.TryAdd(e.DriverId, e.TeamId);
        if (!string.IsNullOrEmpty(playerTeamId))
            driverTeam[_playerDriverId] = playerTeamId;

        var teamPoints = new Dictionary<string, int>(StringComparer.Ordinal);
        if (standings is not null)
            foreach (var d in standings.Drivers)
                if (driverTeam.TryGetValue(d.DriverId, out var tid))
                    teamPoints[tid] = teamPoints.GetValueOrDefault(tid) + (int)Math.Round(d.CountedPoints.ToDouble());

        // Rank every team for the constructors' position: points desc, then prestige desc, then name.
        var ranked = paddock.Teams
            .OrderByDescending(t => teamPoints.GetValueOrDefault(t.TeamId))
            .ThenByDescending(t => t.Prestige)
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .ToList();
        var champPos = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < ranked.Count; i++)
            champPos[ranked[i].TeamId] = i + 1;

        // Prestige rank (for the over-achiever flavour): the highest-prestige team is rank 1.
        var byPrestige = paddock.Teams
            .OrderByDescending(t => t.Prestige)
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .ToList();
        var prestigeRank = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < byPrestige.Count; i++)
            prestigeRank[byPrestige[i].TeamId] = i + 1;

        SmgpTeamDashboardEntry Entry(SmgpTeamCard c) => new()
        {
            TeamId = c.TeamId,
            Name = c.Name,
            IsPlayerTeam = !string.IsNullOrEmpty(playerTeamId)
                && string.Equals(c.TeamId, playerTeamId, StringComparison.Ordinal),
            Prestige = c.Prestige,
            Tier = c.Tier,
            PaletteHex = c.PaletteHex,
            Motto = c.Motto,
            LogoKey = c.LogoKey,
            History = c.History,
            Roster = c.Roster,
            Sponsors = c.Sponsors,
            ChampionshipPosition = standings is null ? null : champPos.GetValueOrDefault(c.TeamId),
            ChampionshipPoints = teamPoints.GetValueOrDefault(c.TeamId),
            BudgetTier = BudgetTierFlavour(c.Prestige),
        };

        var entries = ranked.Select(Entry).ToList();
        var playerEntry = entries.FirstOrDefault(e => e.IsPlayerTeam) ?? entries[0];

        // FLAVOUR "team of the season" (seed of the future economy): the biggest over-achiever vs its prestige
        // budget (a constructors' position better than its prestige rank), else the constructors' leader. Only
        // once a round has scored. Clearly labelled flavour, there is no real budget model yet.
        SmgpTeamOfSeasonFlavour? tos = null;
        if (teamPoints.Values.Any(p => p > 0))
        {
            var best = ranked
                .Select(t => (Team: t, Over: prestigeRank[t.TeamId] - champPos[t.TeamId],
                    Pts: teamPoints.GetValueOrDefault(t.TeamId)))
                .OrderByDescending(x => x.Over)
                .ThenByDescending(x => x.Pts)
                .ThenBy(x => x.Team.Name, StringComparer.Ordinal)
                .First();
            bool overachiever = best.Over > 0;
            tos = new SmgpTeamOfSeasonFlavour
            {
                TeamId = best.Team.TeamId,
                Name = best.Team.Name,
                PaletteHex = best.Team.PaletteHex,
                Headline = overachiever ? "OVERACHIEVER OF THE SEASON" : "TEAM OF THE SEASON",
                Note = overachiever
                    ? string.Create(CultureInfo.InvariantCulture, $"{best.Team.Name} are punching above their {best.Team.Tier} budget - P{champPos[best.Team.TeamId]} in the constructors' running on {best.Pts} pts. (Flavour only; no economy model yet.)")
                    : string.Create(CultureInfo.InvariantCulture, $"{best.Team.Name} lead the constructors' running on {best.Pts} pts, as their {best.Team.Tier} standing suggests. (Flavour only; no economy model yet.)"),
            };
        }

        return new SmgpTeamDashboard
        {
            PlayerTeam = playerEntry,
            Teams = entries,
            TeamOfSeason = tos,
        };
    }

    /// <summary>A FLAVOUR budget-tier label from a team's prestige, the seed of the future Tycoon economy,
    /// NOT a real budget number.</summary>
    private static string BudgetTierFlavour(int prestige) => prestige switch
    {
        >= 5 => "Blue-chip",
        4 => "Well-backed",
        3 => "Mid-budget",
        _ => "Shoestring",
    };

    /// <summary>The player's OWN Paddock bio, the one card that is not authored but generated live, so
    /// it reflects who you are RIGHT NOW: your team, how far into the 17-season campaign you are, and the
    /// record you have built from zero (honestly - a blank sheet reads as a blank sheet). Grows with the
    /// career. ASCII punctuation, matching the authored bios. Display-only.</summary>
    private IReadOnlyList<string> BuildPlayerBio(string name, string teamName, SmgpCareerStats career)
    {
        string where = string.IsNullOrWhiteSpace(teamName) ? "the SEGA world" : $"{teamName}";
        string season = $"Season {_seasonOrdinal} of {Companion.Core.Smgp.SmgpRules.CampaignSeasons}";

        string p1 = career.Starts == 0
            ? $"{name} arrives with everything still to prove - no wins, no history, just a seat at {where} and a season ahead. Where every other name on the grid carries years of it, you carry nothing but the number on your car. {season} of the long climb begins, and the whole field is a stranger."
            : $"{name} races for {where}, and unlike everyone around you, your story starts the day you arrived - the world only began counting your results then. {season} into the climb, you are building a name the only way this place allows: one result at a time, from zero.";

        var earned = new List<string>();
        if (career.Titles > 0) earned.Add(Count(career.Titles, "title", "titles"));
        if (career.Wins > 0) earned.Add(Count(career.Wins, "win", "wins"));
        if (career.Poles > 0) earned.Add(Count(career.Poles, "pole", "poles"));
        if (career.Podiums > 0) earned.Add(Count(career.Podiums, "podium", "podiums"));
        string p2 = earned.Count == 0
            ? $"The sheet is still blank - {Count(career.Starts, "start", "starts")}, and the first podium not yet taken. In this world a reputation is never handed over; it is prised loose one race at a time, and yours is still all ahead of you."
            : $"So far you have earned {Join(earned)} across {Count(career.Starts, "start", "starts")}. Every line of it is yours alone - no inheritance, no pre-history, just what you have wrestled from the grid since you turned up.";

        string p3 = "Above it all sits A. Senna and Madonna's crown - untouchable, the benchmark the entire grid is measured against and the seat the boldest insurgents swear they will take. Seventeen seasons stand between you and the final reckoning. Go the distance and the SEGA world remembers your name; conquer every one of them and it never forgets it.";

        return [p1, p2, p3];

        static string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";
        static string Join(List<string> parts) => parts.Count == 1
            ? parts[0]
            : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1];
    }

    /// <summary>A driver's totals accrued across ALL COMPLETED PRIOR seasons of the career (everything
    /// before the current season), added on top of the predetermined baseline and the live current-season
    /// tally so the SMGP career card spans the whole 17-season campaign. Display-only; a plain value with
    /// default = all-zeros (the reused per-projection Empty).</summary>
    private readonly record struct SmgpPriorTotals(
        int Starts, int Wins, int Podiums, int Poles, int Top5s, int Points, int Titles);

    /// <summary>Rolls up every COMPLETED PRIOR season's SMGP counts + points + titles per driver, the
    /// term that makes the career card span all seasons (the current season stays the live delta added by
    /// <see cref="BuildDriverStats"/>). Mirrors <see cref="CareerTimeline"/>'s per-season loop: each prior
    /// season rehydrates its pinned pack + scoring, re-reads the stored envelopes, and re-derives counts
    /// (<see cref="Companion.Core.Smgp.SmgpLiveStats"/>) + final standings (<see cref="StandingsEngine"/>).
    /// A pure read over the immutable results, never a fold input, never persisted. The player's
    /// per-season (possibly synthetic) id is normalized onto <c>_playerDriverId</c> so their prior seasons
    /// land on their own card. Compute ONCE per projection and pass the result into BuildDriverStats.</summary>
    private IReadOnlyDictionary<string, SmgpPriorTotals> PriorSeasonsSmgpTotals()
    {
        var totals = new Dictionary<string, SmgpPriorTotals>(StringComparer.Ordinal);
        foreach (var season in CareerStore.ReadSeasons(_database))
        {
            if (season.Id == _seasonId)
                continue; // the current season is the live delta, added separately by BuildDriverStats

            var seasonPack = SeasonPackFor(season);
            string seasonPlayerId = PlayerDriverIdFor(season, seasonPack);
            string Key(string id) =>
                string.Equals(id, seasonPlayerId, StringComparison.Ordinal) ? _playerDriverId : id;

            // Counts over every round's primary race + pole (like the current-season AccrueSeasonCounts).
            var rounds = new List<(IReadOnlyList<Companion.Core.Scoring.ClassifiedEntry> Race, string? Pole)>();
            var stored = ResultStore.ReadSeasonResults(_database, season.Id);
            foreach (var s in stored)
            {
                var envelope = s.ToEnvelope();
                var race = envelope.Result.Sessions.FirstOrDefault(x => x.Kind == Companion.Core.Scoring.SessionKind.Race)
                    ?? envelope.Result.Sessions.FirstOrDefault();
                if (race is null)
                    continue;
                string? pole = envelope.QualifyingOrder is { Count: > 0 } q ? q[0] : null;
                rounds.Add((race.Entries, pole));
            }
            foreach (var (id, c) in Companion.Core.Smgp.SmgpLiveStats.Accrue(rounds))
            {
                string key = Key(id);
                var t = totals.GetValueOrDefault(key);
                totals[key] = t with
                {
                    Starts = t.Starts + c.Starts,
                    Wins = t.Wins + c.Wins,
                    Podiums = t.Podiums + c.Podiums,
                    Poles = t.Poles + c.Poles,
                    Top5s = t.Top5s + c.Top5s,
                };
            }

            // Standings over CHAMPIONSHIP rounds → points per driver + the champion's title (matches the
            // current-season standings path and CareerTimeline's per-season scoring).
            var champRounds = stored
                .Where(r => seasonPack.Season.Rounds.FirstOrDefault(rd => rd.Round == r.Round)?.Championship ?? false)
                .Select(r => r.ToRoundResult())
                .ToList();
            if (champRounds.Count == 0)
                continue;
            var scoring = ChampionshipCalendar.ResolveScoring(seasonPack);
            var snapshot = StandingsEngine.ComputeSeason(scoring, champRounds).Snapshots[^1];
            foreach (var d in snapshot.Drivers)
            {
                string key = Key(d.DriverId);
                var t = totals.GetValueOrDefault(key);
                totals[key] = t with
                {
                    Points = t.Points + (int)Math.Round(d.CountedPoints.ToDouble()),
                    Titles = t.Titles + (d.Position == 1 ? 1 : 0),
                };
            }
        }
        return totals;
    }

    /// <summary>Per-driver COUNTS (wins/poles/podiums/top-5s/starts) accrued from THIS season's folded
    /// results, display-only, re-read from the raw envelopes like the standings. Empty before any round
    /// is scored. The all-seasons total adds <see cref="PriorSeasonsSmgpTotals"/> on top of this.</summary>
    private IReadOnlyDictionary<string, Companion.Core.Smgp.SmgpAccruedStats> AccrueSeasonCounts()
    {
        var rounds = new List<(IReadOnlyList<Companion.Core.Scoring.ClassifiedEntry> Race, string? Pole)>();
        foreach (var stored in ResultStore.ReadSeasonResults(_database, _seasonId))
        {
            var envelope = stored.ToEnvelope();
            var race = envelope.Result.Sessions.FirstOrDefault(s => s.Kind == Companion.Core.Scoring.SessionKind.Race)
                ?? envelope.Result.Sessions.FirstOrDefault();
            if (race is null)
                continue;
            string? pole = envelope.QualifyingOrder is { Count: > 0 } q ? q[0] : null;
            rounds.Add((race.Entries, pole));
        }
        return Companion.Core.Smgp.SmgpLiveStats.Accrue(rounds);
    }

    /// <summary>Builds a driver's all-time career totals and this-season tally from the accrued counts +
    /// the current standings. The player (<paramref name="isPlayer"/>) has no baseline, they build from
    /// zero; an AI driver's baseline is their predetermined pre-history. A completed season adds a title
    /// to its champion. Season points/position come from the live standings.</summary>
    private (SmgpCareerStats Career, SmgpSeasonStats? Season) BuildDriverStats(
        string driverId, bool isPlayer,
        Companion.Core.Smgp.SmgpAccruedStats accrued,
        Companion.Core.Scoring.DriverStanding? standing,
        bool seasonComplete, string? champion,
        Companion.Core.Smgp.SmgpDriverStatLine? baseline,
        SmgpPriorTotals prior = default)
    {
        int seasonPoints = standing is not null ? (int)Math.Round(standing.CountedPoints.ToDouble()) : 0;
        bool wonTitle = seasonComplete && string.Equals(driverId, champion, StringComparison.Ordinal);

        // All-time = predetermined baseline (AI only) + every COMPLETED PRIOR season of THIS career + the
        // live current season. For a 17-season campaign the career card thus spans all seasons, not just
        // the current one. The player has no baseline (they build from zero).
        var career = new SmgpCareerStats
        {
            Starts = (isPlayer ? 0 : baseline?.CareerStarts ?? 0) + prior.Starts + accrued.Starts,
            Wins = (isPlayer ? 0 : baseline?.CareerWins ?? 0) + prior.Wins + accrued.Wins,
            Podiums = (isPlayer ? 0 : baseline?.CareerPodiums ?? 0) + prior.Podiums + accrued.Podiums,
            Poles = (isPlayer ? 0 : baseline?.CareerPoles ?? 0) + prior.Poles + accrued.Poles,
            Top5s = (isPlayer ? 0 : baseline?.CareerTop5s ?? 0) + prior.Top5s + accrued.Top5s,
            Points = (isPlayer ? 0 : baseline?.CareerPoints ?? 0) + prior.Points + seasonPoints,
            Titles = (isPlayer ? 0 : baseline?.Championships ?? 0) + prior.Titles + (wonTitle ? 1 : 0),
        };

        // No season card until something has happened this season (no round scored and no standing).
        SmgpSeasonStats? season = standing is null && accrued.Starts == 0
            ? null
            : new SmgpSeasonStats
            {
                Position = standing?.Position,
                Points = seasonPoints,
                Wins = accrued.Wins,
                Poles = accrued.Poles,
                Podiums = accrued.Podiums,
                Top5s = accrued.Top5s,
                Starts = accrued.Starts,
            };

        return (career, season);
    }

    /// <summary>The notable non-zero career tallies (titles · wins · podiums · poles) as a bare one-liner,
    /// or empty when there is nothing to show yet. Shared by the rival readout and the team roster.</summary>
    private static string CareerTallies(SmgpCareerStats? career)
    {
        if (career is null)
            return "";
        var parts = new List<string>();
        var ci = CultureInfo.InvariantCulture;
        if (career.Titles > 0) parts.Add(string.Create(ci, $"{career.Titles} {(career.Titles == 1 ? "TITLE" : "TITLES")}"));
        if (career.Wins > 0) parts.Add(string.Create(ci, $"{career.Wins} {(career.Wins == 1 ? "WIN" : "WINS")}"));
        if (career.Poles > 0) parts.Add(string.Create(ci, $"{career.Poles} {(career.Poles == 1 ? "POLE" : "POLES")}"));
        if (career.Podiums > 0) parts.Add(string.Create(ci, $"{career.Podiums} {(career.Podiums == 1 ? "PODIUM" : "PODIUMS")}"));
        return string.Join(" · ", parts);
    }

    /// <summary>The player's career one-liner for the rival readout, the notable non-zero tallies
    /// (titles, wins, podiums, poles), or empty when they have nothing to show yet.</summary>
    private static string FormatCareerLine(SmgpCareerStats career) =>
        CareerTallies(career) is { Length: > 0 } tallies ? "CAREER  " + tallies : "";

    /// <summary>A driver's this-season roster line, "P3 · 18 PTS", or "-" before the standing computes.</summary>
    private static string SeasonLineOf(SmgpSeasonStats? season) =>
        season is { Position: { } pos }
            ? string.Create(CultureInfo.InvariantCulture, $"P{pos} · {season.Points} PTS")
            : "-";

    /// <summary>How many recent races the FormRecent trend keeps.</summary>
    private const int FormWindow = 6;

    /// <summary>The ladder-class accent colour for the rival picker's coloured CLASS chip (A gold … D slate).
    /// Vivid, distinct, mid-tone so it reads on both the light and dark themes. Display-only.</summary>
    private static string TierColorHex(char tier) => tier switch
    {
        'A' => "#E8B900", // gold, the top house
        'B' => "#2E77D0", // blue
        'C' => "#1FA85B", // green
        _ => "#8792A0",   // slate, the floor (D)
    };

    /// <summary>The venue label a per-track record keys on: the round's own name, for the SMGP mode that
    /// is the short arcade venue ("Monaco", "San Marino"), which reads better on a compact card than the
    /// long formal <see cref="PackTrackRef.RealVenue"/>. Always present (a required field) and stable across
    /// seasons (the calendar never changes), so it is a sound per-track key.</summary>
    private static string VenueLabel(PackRound round) => round.Name;

    /// <summary>The finishing position of one driver in a race classification, or null when they were
    /// not classified (retired / disqualified / DNF).</summary>
    private static int? FinishPosition(IReadOnlyList<Companion.Core.Scoring.ClassifiedEntry> entries, string driverId)
    {
        var line = entries.FirstOrDefault(e => string.Equals(e.DriverId, driverId, StringComparison.Ordinal));
        return line is { Status: FinishStatus.Classified, Position: { } p } ? p : null;
    }

    /// <summary>The three per-driver depth projections a card carries (head-to-head vs the player, per-venue
    /// bests, recent form). Built together in one pass by <see cref="BuildDriverDepthIndex"/>.</summary>
    private sealed record SmgpDriverDepth
    {
        public required SmgpHeadToHead HeadToHead { get; init; }
        public required IReadOnlyList<SmgpTrackBest> PerTrackBest { get; init; }
        public required IReadOnlyList<int?> FormRecent { get; init; }
    }

    /// <summary>A mutable per-driver accumulator (career-wide) used only while building the depth index.</summary>
    private sealed class DepthAccumulator
    {
        public int RacesMet;
        public int PlayerAhead;
        public int DriverAhead;
        public int? PlayerBestTogether;
        public string? BestTogetherVenue;
        public readonly Dictionary<string, int> DriverBestByVenue = new(StringComparer.Ordinal);
        public readonly List<int?> Form = [];
    }

    /// <summary>The player-vs-every-AI head-to-head, each AI's per-venue best finish, and their recent form,
    /// accumulated in ONE pass over every scored race of the WHOLE career (all seasons incl. the current).
    /// A pure display-only projection over the immutable results, never a fold input. Keyed by the AI's
    /// (stable, pinned-pack) driver id; the player is skipped each season by that season's
    /// <see cref="PlayerDriverIdFor"/>. The player's own best finish per venue is tracked globally so a
    /// track card can compare. The live SMGP battle streak comes from the current folded state's tally.
    /// Computed ONCE per projection and looked up per driver card.</summary>
    private IReadOnlyDictionary<string, SmgpDriverDepth> BuildDriverDepthIndex()
    {
        var acc = new Dictionary<string, DepthAccumulator>(StringComparer.Ordinal);
        var playerBestByVenue = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var season in CareerStore.ReadSeasons(_database))
        {
            var seasonPack = SeasonPackFor(season);
            string seasonPlayerId = PlayerDriverIdFor(season, seasonPack);
            var venueByRound = seasonPack.Season.Rounds
                .ToDictionary(r => r.Round, VenueLabel);

            foreach (var stored in ResultStore.ReadSeasonResults(_database, season.Id).OrderBy(r => r.Round))
            {
                var envelope = stored.ToEnvelope();
                var race = envelope.Result.Sessions.FirstOrDefault(s => s.Kind == Companion.Core.Scoring.SessionKind.Race)
                    ?? envelope.Result.Sessions.FirstOrDefault();
                if (race is null)
                    continue;
                string venue = venueByRound.GetValueOrDefault(stored.Round) ?? $"Round {stored.Round}";

                int? playerPos = FinishPosition(race.Entries, seasonPlayerId);
                if (playerPos is { } pbest)
                    playerBestByVenue[venue] = playerBestByVenue.TryGetValue(venue, out var pcur) ? Math.Min(pcur, pbest) : pbest;

                foreach (var entry in race.Entries)
                {
                    if (string.Equals(entry.DriverId, seasonPlayerId, StringComparison.Ordinal))
                        continue;
                    if (!acc.TryGetValue(entry.DriverId, out var a))
                        acc[entry.DriverId] = a = new DepthAccumulator();

                    int? drvPos = entry.Status == FinishStatus.Classified ? entry.Position : null;
                    a.Form.Add(drvPos);
                    if (drvPos is { } db)
                        a.DriverBestByVenue[venue] = a.DriverBestByVenue.TryGetValue(venue, out var dcur) ? Math.Min(dcur, db) : db;

                    // Head-to-head counts only a race BOTH were classified in (a fair comparison).
                    if (playerPos is { } pp && drvPos is { } dp)
                    {
                        a.RacesMet++;
                        if (pp < dp) a.PlayerAhead++;
                        else if (dp < pp) a.DriverAhead++;
                        if (a.PlayerBestTogether is null || pp < a.PlayerBestTogether)
                        {
                            a.PlayerBestTogether = pp;
                            a.BestTogetherVenue = venue;
                        }
                    }
                }
            }
        }

        var state = CurrentSmgpState();
        var result = new Dictionary<string, SmgpDriverDepth>(StringComparer.Ordinal);
        foreach (var (id, a) in acc)
        {
            var tally = state?.TallyFor(id) ?? Companion.Core.Smgp.SmgpBattleTally.Empty;
            var perTrack = a.DriverBestByVenue
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new SmgpTrackBest
                {
                    Venue = kv.Key,
                    DriverBest = kv.Value,
                    PlayerBest = playerBestByVenue.TryGetValue(kv.Key, out var pb) ? pb : null,
                })
                .ToList();
            result[id] = new SmgpDriverDepth
            {
                HeadToHead = new SmgpHeadToHead
                {
                    RacesMet = a.RacesMet,
                    PlayerAhead = a.PlayerAhead,
                    DriverAhead = a.DriverAhead,
                    PlayerBestTogether = a.PlayerBestTogether,
                    BestTogetherVenue = a.BestTogetherVenue,
                    PlayerStreak = tally.PlayerStreak,
                    DriverStreak = tally.RivalStreak,
                },
                PerTrackBest = perTrack,
                FormRecent = a.Form.Count > FormWindow
                    ? a.Form.GetRange(a.Form.Count - FormWindow, FormWindow)
                    : a.Form,
            };
        }
        return result;
    }

    /// <summary>The (team name, prestige) a livery belongs to in a given season's pack, from its authored
    /// entry (else a guest entry, via <see cref="PlayerTeamId"/>), mapped to the pack's team. ("", 0) when
    /// unknown. Season-parameterized (unlike <see cref="TeamOfLivery"/>, which is bound to the current pack)
    /// so a PRIOR season's seat resolves against that season's pinned roster.</summary>
    private static (string TeamName, int Prestige) TeamOfLiveryInPack(SeasonPack pack, string? livery)
    {
        if (string.IsNullOrEmpty(livery))
            return ("", 0);
        string? teamId = PlayerTeamId(pack, livery);
        if (teamId is null)
            return ("", 0);
        var team = pack.Teams.FirstOrDefault(t => string.Equals(t.Id, teamId, StringComparison.Ordinal));
        return (team?.Name ?? teamId, team?.Prestige ?? 0);
    }

    /// <summary>The player's one-line live narrative intro above the timeline, where they stand RIGHT NOW.</summary>
    private string BuildNarrativeIntro(SmgpCareerStats? career, SmgpSeasonStats? season, string teamName)
    {
        string where = string.IsNullOrWhiteSpace(teamName) ? "the SEGA world" : teamName;
        string standing = season is { Position: { } pos }
            ? string.Create(CultureInfo.InvariantCulture, $"P{pos}, {season.Points} pts this season")
            : "yet to score this season";
        string tallies = career is not null && CareerTallies(career) is { Length: > 0 } t ? $" · {t}" : "";
        return string.Create(CultureInfo.InvariantCulture,
            $"Season {_seasonOrdinal} of {Companion.Core.Smgp.SmgpRules.CampaignSeasons} · " +
            $"racing for {where} · {standing}{tallies}");
    }

    /// <summary>The player's evolving career TIMELINE (Task 2): an ordered list of milestone beats, arrived,
    /// first start/points/pole/podium/win, each promotion + demotion, each title, rivalries won/lost, floor
    /// near-misses, season progress and the finale, detected by <see cref="Companion.Core.Smgp.SmgpCareerBeats"/>
    /// from the shaped folded state. A pure display-only projection over the immutable results (mirrors
    /// <see cref="CareerTimeline"/>'s per-season loop): each season rehydrates its pinned pack + scoring, reads
    /// the stored results, the SMGP seat sequence (<c>Smgp.CurrentSeatLivery</c>, NEVER the pinned
    /// <c>LiveryName</c>), and the journaled battle triggers. Grows with the career; empty before the first
    /// round. Returns the ordered beats plus a one-line live intro.</summary>
    private (IReadOnlyList<Companion.Core.Smgp.SmgpCareerBeat> Beats, string Intro) BuildPlayerTimeline(
        SmgpCareerStats? playerCareer, SmgpSeasonStats? playerSeason, string playerTeamName)
    {
        var beats = Companion.Core.Smgp.SmgpCareerBeats.Detect(BuildSmgpNarrativeSeasons());
        return (beats, BuildNarrativeIntro(playerCareer, playerSeason, playerTeamName));
    }

    /// <summary>Shapes the per-season/per-round facts the milestone detector AND the living-world dispatch
    /// feed both read (Task 2 timeline + Task 4 dispatches), one walk over the career's seasons rehydrating
    /// each pinned pack + scoring, reading the stored results, the SMGP seat sequence and the journaled battle
    /// triggers. A pure display-only projection over the immutable results; never a fold input.</summary>
    private IReadOnlyList<Companion.Core.Smgp.SmgpNarrativeSeason> BuildSmgpNarrativeSeasons()
    {
        var seasonsInput = new List<Companion.Core.Smgp.SmgpNarrativeSeason>();
        int ordinal = 0;
        foreach (var season in CareerStore.ReadSeasons(_database))
        {
            ordinal++;
            var seasonPack = SeasonPackFor(season);
            string pid = PlayerDriverIdFor(season, seasonPack);
            var venueByRound = seasonPack.Season.Rounds.ToDictionary(r => r.Round, VenueLabel);
            var driverNames = seasonPack.Drivers.ToDictionary(d => d.Id, d => d.Name, StringComparer.Ordinal);

            // The season START seat (catches a between-seasons promotion / lost-defense drop). The SMGP seat
            // rides Smgp.CurrentSeatLivery, NEVER the pinned LiveryName (which never moves on a seat change).
            var startState = StateStore.ReadPlayerState(_database, season.Id, StateStore.StageStart);
            string? startLivery = startState?.Smgp?.CurrentSeatLivery ?? startState?.LiveryName;
            var (startTeamName, startPrestige) = TeamOfLiveryInPack(seasonPack, startLivery);

            // Per-round folded SMGP state (seat / floor losses / career-over), keyed by round.
            var smgpByRound = new Dictionary<int, Companion.Core.Smgp.SmgpState>();
            foreach (var (rnd, rps) in StateStore.ReadRoundPlayerStates(_database, season.Id))
                if (rps.Player.Smgp is { } sm)
                    smgpByRound[rnd] = sm;

            // The season's journal, read once and walked for both the battle triggers and the accidents below.
            var journalRows = JournalStore.ReadSeason(_database, season.Id);

            // Journaled battle triggers → the round a two-wins offer was earned / a two-losses forfeit
            // happened. Read from the journal (NOT the streak, which resets to 0 in the same triggering fold);
            // title-defense rows carry trigger "none" and are skipped.
            var wonByRound = new Dictionary<int, string>();
            var lostByRound = new Dictionary<int, string>();
            var wonIdByRound = new Dictionary<int, string>();
            var lostIdByRound = new Dictionary<int, string>();
            foreach (var row in journalRows)
            {
                if (!string.Equals(row.Phase, JournalPhases.SmgpBattle, StringComparison.Ordinal) || row.Round is not { } jr)
                    continue;
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(row.DeltaJson);
                    var root = doc.RootElement;
                    string? trigger = root.TryGetProperty("trigger", out var tv) ? tv.GetString() : null;
                    string? rival = root.TryGetProperty("rival", out var rvv) ? rvv.GetString() : null;
                    string rivalName = rival is not null ? driverNames.GetValueOrDefault(rival, rival) : "a rival";
                    if (string.Equals(trigger, "seatSwapOfferToPlayer", StringComparison.Ordinal))
                    {
                        wonByRound[jr] = rivalName;
                        if (rival is { Length: > 0 }) wonIdByRound[jr] = rival;
                    }
                    else if (string.Equals(trigger, "playerSeatForfeit", StringComparison.Ordinal))
                    {
                        lostByRound[jr] = rivalName;
                        if (rival is { Length: > 0 }) lostIdByRound[jr] = rival;
                    }
                }
                catch (System.Text.Json.JsonException)
                {
                    // A malformed delta cell never breaks the display timeline.
                }
            }

            // Journaled player accidents → the round an INJURING/FATAL accident happened (character death &
            // injury §6). Only injuring outcomes drive a Setback dispatch; a survived accident ("none") is
            // skipped. Reuses the journal already read above (same DERIVED player.accident row set).
            var accidentByRound = new Dictionary<int, string>();
            var accidentMissByRound = new Dictionary<int, int>();
            foreach (var row in journalRows)
            {
                if (!string.Equals(row.Phase, JournalPhases.PlayerAccident, StringComparison.Ordinal) || row.Round is not { } ar)
                    continue;
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(row.DeltaJson);
                    var root = doc.RootElement;
                    string? outcome = root.TryGetProperty("outcome", out var ov) ? ov.GetString() : null;
                    if (outcome is "minorInjury" or "seasonEnding" or "death")
                    {
                        accidentByRound[ar] = outcome;
                        if (root.TryGetProperty("missRaces", out var mv) && mv.ValueKind == System.Text.Json.JsonValueKind.Number)
                            accidentMissByRound[ar] = mv.GetInt32();
                    }
                }
                catch (System.Text.Json.JsonException)
                {
                    // A malformed delta cell never breaks the display timeline.
                }
            }

            // Standings snapshots (per championship round, in order) → the player's cumulative counted points
            // (first-points beat) + the season champion (title beat).
            var stored = ResultStore.ReadSeasonResults(_database, season.Id).OrderBy(r => r.Round).ToList();
            var champStored = stored
                .Where(r => seasonPack.Season.Rounds.FirstOrDefault(rd => rd.Round == r.Round)?.Championship ?? false)
                .ToList();
            var scoring = ChampionshipCalendar.ResolveScoring(seasonPack);
            IReadOnlyList<StandingsSnapshot> snapshots = champStored.Count > 0
                ? StandingsEngine.ComputeSeason(scoring, champStored.Select(r => r.ToRoundResult()).ToList()).Snapshots
                : [];
            var playerPointsAfter = new Dictionary<int, double>();
            for (int i = 0; i < snapshots.Count; i++)
            {
                var ps = snapshots[i].Drivers.FirstOrDefault(d => string.Equals(d.DriverId, pid, StringComparison.Ordinal));
                // GrossPoints (monotonic) for first-points detection, CountedPoints can be trimmed by
                // dropped-scores rules late season, so its first non-zero snapshot is not the true first.
                playerPointsAfter[champStored[i].Round] = ps?.GrossPoints.ToDouble() ?? 0.0;
            }
            var finalSnapshot = snapshots.Count > 0 ? snapshots[^1] : null;
            bool complete = string.Equals(season.Status, SeasonStatus.Complete, StringComparison.Ordinal);
            bool playerChampion = complete
                && finalSnapshot?.Drivers.FirstOrDefault(d => d.Position == 1)?.DriverId is { } champ
                && string.Equals(champ, pid, StringComparison.Ordinal);

            // Career-over + banked titles from the season's LAST folded state (else its start state). Titles
            // bank at the rollover into the NEXT season, so at completion add this season's title if won.
            var lastSmgp = smgpByRound.Count > 0
                ? smgpByRound[smgpByRound.Keys.Max()]
                : startState?.Smgp;
            bool careerOver = lastSmgp?.CareerOver ?? false;
            int titles = (lastSmgp?.Titles ?? 0) + (playerChampion ? 1 : 0);
            bool campaignComplete = complete && Companion.Core.Smgp.SmgpRules.CampaignComplete(ordinal, careerOver);
            bool campaignFlawless = complete && Companion.Core.Smgp.SmgpRules.CampaignFlawless(ordinal, titles, careerOver);

            double running = 0.0;
            var rounds = new List<Companion.Core.Smgp.SmgpNarrativeRound>();
            foreach (var s in stored)
            {
                var envelope = s.ToEnvelope();
                var race = envelope.Result.Sessions.FirstOrDefault(x => x.Kind == Companion.Core.Scoring.SessionKind.Race)
                    ?? envelope.Result.Sessions.FirstOrDefault();
                int? finish = race is not null ? FinishPosition(race.Entries, pid) : null;
                bool pole = envelope.QualifyingOrder is { Count: > 0 } q && string.Equals(q[0], pid, StringComparison.Ordinal);
                if (playerPointsAfter.TryGetValue(s.Round, out var pts))
                    running = pts;
                var smgp = smgpByRound.GetValueOrDefault(s.Round);
                string seatLivery = smgp?.CurrentSeatLivery ?? startLivery ?? "";
                var (seatTeamName, seatPrestige) = TeamOfLiveryInPack(seasonPack, seatLivery);
                rounds.Add(new Companion.Core.Smgp.SmgpNarrativeRound
                {
                    Venue = venueByRound.GetValueOrDefault(s.Round) ?? $"Round {s.Round}",
                    Round = s.Round,
                    Finish = finish,
                    Pole = pole,
                    ScoredPointsCumulative = running > 0,
                    SeatTeamName = seatTeamName,
                    SeatPrestige = seatPrestige,
                    RivalryWonOver = wonByRound.GetValueOrDefault(s.Round),
                    RivalryLostTo = lostByRound.GetValueOrDefault(s.Round),
                    RivalryWonOverId = wonIdByRound.GetValueOrDefault(s.Round),
                    RivalryLostToId = lostIdByRound.GetValueOrDefault(s.Round),
                    FloorLosses = smgp?.FloorLosses ?? 0,
                    CareerOver = smgp?.CareerOver ?? false,
                    AccidentOutcome = accidentByRound.GetValueOrDefault(s.Round),
                    AccidentMissRaces = accidentMissByRound.GetValueOrDefault(s.Round),
                });
            }

            seasonsInput.Add(new Companion.Core.Smgp.SmgpNarrativeSeason
            {
                Ordinal = ordinal,
                StartSeatTeamName = startTeamName,
                StartSeatPrestige = startPrestige,
                Rounds = rounds,
                Complete = complete,
                PlayerChampion = playerChampion,
                CampaignComplete = campaignComplete,
                CampaignFlawless = campaignFlawless,
            });
        }

        return seasonsInput;
    }

    /// <summary>The named RNG subsystem for the living-world dispatch feed, a DISPLAY-ONLY stream (never a
    /// fold input), so it needs no <see cref="CareerStreams"/> registration; it just keys deterministic
    /// corpus selection off the master seed so the same career shows the same stories on every open.</summary>
    private const string DispatchStream = "smgp-dispatch";

    /// <summary>The SMGP "living world" dispatch feed (Task 4): the reactive in-world news the career writes
    /// as it unfolds. Two sources, both pure projections over the folded results: the player's own milestones
    /// (<see cref="Companion.Core.Smgp.SmgpCareerBeats"/>) and the AI-world stories around them
    /// (<see cref="Companion.Core.Smgp.SmgpWorldStories"/>), each voiced through the dispatch corpus with a
    /// deterministic per-(season, round) stream. Newest first. Empty outside the mode. Never a fold input, so
    /// replay stays byte-identical (the bodies are DERIVED from the seed, never stored).</summary>
    public IReadOnlyList<Companion.Core.Smgp.SmgpDispatch> SmgpDispatches()
    {
        if (_environment.RulesDirectory is null ||
            !string.Equals(Pack.Manifest.CareerStyle, Companion.Core.Smgp.SmgpRules.CareerStyle, StringComparison.Ordinal))
            return [];

        var corpus = _environment.Rules.SmgpDispatchCorpus;
        var factory = new StreamFactory(MasterSeedU);
        string playerName = PlayerDisplayName() ?? PlayerDefaultName;
        string playerTeam = CurrentPlayerTeamName();
        string benchmarkId = ResolveBenchmarkDriverId();
        string benchmarkName = DriverDisplayName(benchmarkId);
        string leaderName = CurrentStandings()?.Drivers.FirstOrDefault(d => d.Position == 1) is { } ld
            ? DriverDisplayName(ld.DriverId) : "";

        var dispatches = new List<Companion.Core.Smgp.SmgpDispatch>();
        // Two sequence ranges so that within one round the PLAYER's own milestones sort ahead of the ambient
        // world stories in the newest-first feed (milestones take the high range → first after the reverse).
        int milestoneSeq = 1_000_000, worldSeq = 0;

        // --- Player milestone dispatches (reuse the Task-2 beat detector; render a news body per beat) ---
        foreach (var beat in Companion.Core.Smgp.SmgpCareerBeats.Detect(BuildSmgpNarrativeSeasons()))
        {
            var (key, kind) = MapBeatToDispatch(beat);
            if (key is null)
                continue;
            string venue = VenueFromWhenLabel(beat.WhenLabel);
            string rivalName = beat.SubjectId is { Length: > 0 } rid ? DriverDisplayName(rid) : "";
            var tokens = DispatchTokens(playerName, playerTeam, rivalName, venue, beat.Season, 0,
                subject: "", other: "", leader: leaderName, benchmark: benchmarkName);
            var stream = factory.CreateStream(DispatchStream, beat.Season, beat.Round, key + "|" + beat.SubjectId);
            string body = corpus.Render(key, tokens, stream, fallback: beat.Detail);

            // Escalating RIVAL VOICE: a rivalry dispatch speaks in the rival's own (mood-aware) words.
            if (beat.Kind == Companion.Core.Smgp.SmgpBeatKind.RivalryEarned && beat.SubjectId is { Length: > 0 } wonId)
                body = AppendRivalVoice(body, wonId, rivalName, Companion.Core.Smgp.SmgpRivalMood.PlayerLeads, beat.Round);
            else if (beat.Kind == Companion.Core.Smgp.SmgpBeatKind.RivalryLost && beat.SubjectId is { Length: > 0 } lostId)
                body = AppendRivalVoice(body, lostId, rivalName, Companion.Core.Smgp.SmgpRivalMood.RivalLeads, beat.Round);

            dispatches.Add(new Companion.Core.Smgp.SmgpDispatch
            {
                WhenLabel = beat.WhenLabel, Kind = kind, Headline = beat.Headline, Body = body,
                DriverArtKey = beat.SubjectId, TeamArtKey = "",
                SortSeason = beat.Season, SortRound = beat.Round, SortSeq = milestoneSeq++,
            });
        }

        // --- AI-world dispatches (a rival's streak, the benchmark, leader/second-place turnover, tightening) ---
        foreach (var story in Companion.Core.Smgp.SmgpWorldStories.Detect(BuildSmgpWorldRounds(), _playerDriverId, benchmarkId))
        {
            var (key, kind, headline) = MapWorldStory(story);
            var tokens = DispatchTokens(playerName, playerTeam, rival: "", venue: story.Venue, season: story.Season,
                number: story.Number, subject: story.SubjectName, other: story.OtherName,
                leader: leaderName, benchmark: benchmarkName);
            var stream = factory.CreateStream(DispatchStream, story.Season, story.Round, key + "|" + story.SubjectId);
            string body = corpus.Render(key, tokens, stream, fallback: headline);
            dispatches.Add(new Companion.Core.Smgp.SmgpDispatch
            {
                WhenLabel = $"Season {story.Season} · {story.Venue}", Kind = kind, Headline = headline, Body = body,
                DriverArtKey = story.SubjectId, TeamArtKey = story.SubjectTeamId,
                SortSeason = story.Season, SortRound = story.Round, SortSeq = worldSeq++,
            });
        }

        // Chronological, then newest first, a stable, deterministic order.
        dispatches.Sort((a, b) =>
        {
            int c = a.SortSeason.CompareTo(b.SortSeason);
            if (c != 0) return c;
            c = a.SortRound.CompareTo(b.SortRound);
            return c != 0 ? c : a.SortSeq.CompareTo(b.SortSeq);
        });
        dispatches.Reverse();
        return dispatches;
    }

    /// <summary>Maps a milestone beat to its dispatch corpus key + kind. Returns a null key for a beat that
    /// gets no dispatch (none today, every kind maps, but the null keeps the switch total).</summary>
    private static (string? Key, Companion.Core.Smgp.SmgpDispatchKind Kind) MapBeatToDispatch(
        Companion.Core.Smgp.SmgpCareerBeat beat) => beat.Kind switch
    {
        Companion.Core.Smgp.SmgpBeatKind.Arrived => ("milestone.arrived", Companion.Core.Smgp.SmgpDispatchKind.Milestone),
        Companion.Core.Smgp.SmgpBeatKind.FirstStart => ("milestone.first-start", Companion.Core.Smgp.SmgpDispatchKind.Milestone),
        Companion.Core.Smgp.SmgpBeatKind.FirstPoints => ("milestone.first-points", Companion.Core.Smgp.SmgpDispatchKind.Milestone),
        Companion.Core.Smgp.SmgpBeatKind.FirstTop5 => ("milestone.first-top5", Companion.Core.Smgp.SmgpDispatchKind.Milestone),
        Companion.Core.Smgp.SmgpBeatKind.FirstPole => ("milestone.first-pole", Companion.Core.Smgp.SmgpDispatchKind.Milestone),
        Companion.Core.Smgp.SmgpBeatKind.FirstPodium => ("milestone.first-podium", Companion.Core.Smgp.SmgpDispatchKind.Milestone),
        Companion.Core.Smgp.SmgpBeatKind.FirstWin => ("milestone.first-win", Companion.Core.Smgp.SmgpDispatchKind.Milestone),
        Companion.Core.Smgp.SmgpBeatKind.SeasonMilestone => ("milestone.season", Companion.Core.Smgp.SmgpDispatchKind.SeasonDigest),
        Companion.Core.Smgp.SmgpBeatKind.Promotion => ("milestone.promotion", Companion.Core.Smgp.SmgpDispatchKind.Milestone),
        Companion.Core.Smgp.SmgpBeatKind.Title => ("milestone.title", Companion.Core.Smgp.SmgpDispatchKind.Milestone),
        Companion.Core.Smgp.SmgpBeatKind.RivalryEarned => ("milestone.rivalry-won", Companion.Core.Smgp.SmgpDispatchKind.Milestone),
        Companion.Core.Smgp.SmgpBeatKind.Finale => ("milestone.finale", Companion.Core.Smgp.SmgpDispatchKind.Milestone),
        Companion.Core.Smgp.SmgpBeatKind.RivalryLost => ("setback.rivalry-lost", Companion.Core.Smgp.SmgpDispatchKind.Setback),
        Companion.Core.Smgp.SmgpBeatKind.NearMiss => ("setback.near-miss", Companion.Core.Smgp.SmgpDispatchKind.Setback),
        // Character death & injury (§6): an accident's setback voiced in the living-world feed.
        Companion.Core.Smgp.SmgpBeatKind.Injured => ("setback.injured", Companion.Core.Smgp.SmgpDispatchKind.Setback),
        Companion.Core.Smgp.SmgpBeatKind.SeasonEndingInjury => ("setback.season-ending-injury", Companion.Core.Smgp.SmgpDispatchKind.Setback),
        Companion.Core.Smgp.SmgpBeatKind.Died => ("setback.died", Companion.Core.Smgp.SmgpDispatchKind.Setback),
        // The floor kicking the player OUT reuses the Demotion kind but a distinct "out" headline.
        Companion.Core.Smgp.SmgpBeatKind.Demotion => beat.Headline.Contains("OUT OF", StringComparison.Ordinal)
            ? ("setback.career-over", Companion.Core.Smgp.SmgpDispatchKind.Setback)
            : ("setback.demotion", Companion.Core.Smgp.SmgpDispatchKind.Setback),
        _ => (null, Companion.Core.Smgp.SmgpDispatchKind.Milestone),
    };

    /// <summary>Maps a world story to its dispatch corpus key + kind + a synthesised arcade headline (also
    /// the render fallback when the corpus lacks the key).</summary>
    private static (string Key, Companion.Core.Smgp.SmgpDispatchKind Kind, string Headline) MapWorldStory(
        Companion.Core.Smgp.SmgpWorldStory s)
    {
        var ci = CultureInfo.InvariantCulture;
        return s.Kind switch
        {
            Companion.Core.Smgp.SmgpWorldStoryKind.RivalStreak => ("world.rival-streak",
                Companion.Core.Smgp.SmgpDispatchKind.RivalWatch,
                string.Create(ci, $"{s.SubjectName.ToUpperInvariant()}, {s.Number} IN A ROW")),
            Companion.Core.Smgp.SmgpWorldStoryKind.Benchmark => ("world.benchmark",
                Companion.Core.Smgp.SmgpDispatchKind.RivalWatch, "THE BENCHMARK"),
            Companion.Core.Smgp.SmgpWorldStoryKind.LeaderChange => ("world.leader-change",
                Companion.Core.Smgp.SmgpDispatchKind.TitleRace, "NEW CHAMPIONSHIP LEADER"),
            Companion.Core.Smgp.SmgpWorldStoryKind.TitleTightens => ("world.title-tightens",
                Companion.Core.Smgp.SmgpDispatchKind.TitleRace, "TITLE RACE TIGHTENS"),
            Companion.Core.Smgp.SmgpWorldStoryKind.StandingsMove => ("world.standings-move",
                Companion.Core.Smgp.SmgpDispatchKind.TitleRace,
                string.Create(ci, $"{s.SubjectName.ToUpperInvariant()} TAKES SECOND")),
            _ => ("", Companion.Core.Smgp.SmgpDispatchKind.TitleRace, ""),
        };
    }

    /// <summary>The dispatch corpus token dictionary, every slot a template might name, with a neutral
    /// fallback for the ones a given dispatch does not carry (so a body never prints a raw token).</summary>
    private static IReadOnlyDictionary<string, string> DispatchTokens(
        string player, string team, string rival, string venue, int season, int number,
        string subject, string other, string leader, string benchmark)
    {
        var ci = CultureInfo.InvariantCulture;
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["player"] = string.IsNullOrEmpty(player) ? "the newcomer" : player,
            ["team"] = string.IsNullOrEmpty(team) ? "the team" : team,
            ["rival"] = string.IsNullOrEmpty(rival) ? "the rival" : rival,
            ["venue"] = string.IsNullOrEmpty(venue) ? "the World Championship" : venue,
            ["season"] = season.ToString(ci),
            ["number"] = number.ToString(ci),
            ["subject"] = string.IsNullOrEmpty(subject) ? "a driver" : subject,
            ["other"] = string.IsNullOrEmpty(other) ? "the field" : other,
            ["leader"] = string.IsNullOrEmpty(leader) ? "the leader" : leader,
            ["benchmark"] = string.IsNullOrEmpty(benchmark) ? "the benchmark" : benchmark,
        };
    }

    /// <summary>Appends the rival's OWN mood-aware trash-talk to a rivalry dispatch (surfacing the escalating
    /// rival voice reactively). Uses the same per-round quote seed as the briefing, so it is stable.</summary>
    private string AppendRivalVoice(
        string body, string rivalId, string rivalName, Companion.Core.Smgp.SmgpRivalMood mood, int round)
    {
        string line = _environment.Rules.SmgpRivalQuotes.Line(rivalId, mood, QuoteSeed(rivalId, round));
        if (string.IsNullOrWhiteSpace(line))
            return body;
        string who = string.IsNullOrWhiteSpace(rivalName) ? "The rival" : rivalName;
        return $"{body} {who}: \"{line}\"";
    }

    /// <summary>The venue part of a beat's "Season n · Venue" WhenLabel, or empty for a season-level label.</summary>
    private static string VenueFromWhenLabel(string whenLabel)
    {
        const string sep = " · ";
        int i = whenLabel.IndexOf(sep, StringComparison.Ordinal);
        return i >= 0 ? whenLabel[(i + sep.Length)..] : "";
    }

    /// <summary>A driver's display name against the CURRENT pack: the player's chosen name for the player id,
    /// else the authored profile name, else the pack roster name, else the id.</summary>
    private string DriverDisplayName(string driverId)
    {
        if (string.IsNullOrEmpty(driverId))
            return "";
        if (string.Equals(driverId, _playerDriverId, StringComparison.Ordinal))
            return PlayerDisplayName() ?? PlayerDefaultName;
        return _environment.Rules.SmgpDriverProfiles.ForDriver(driverId)?.Name is { Length: > 0 } n
            ? n
            : Pack.Drivers.FirstOrDefault(d => string.Equals(d.Id, driverId, StringComparison.Ordinal))?.Name ?? driverId;
    }

    /// <summary>The player's current team name (from the live seat, else the folded SMGP team), or empty.</summary>
    private string CurrentPlayerTeamName()
    {
        string? teamId = (SeasonComplete ? null : CurrentGrid().FirstOrDefault(s => s.IsPlayer)?.TeamId)
            ?? CurrentSmgpTeamId();
        return teamId is null ? ""
            : Pack.Teams.FirstOrDefault(t => string.Equals(t.Id, teamId, StringComparison.Ordinal))?.Name ?? "";
    }

    /// <summary>The always-OP benchmark driver (A. Senna / Madonna #1): the AI on the highest-prestige team,
    /// ties broken by driver id so it is deterministic. Empty when no non-player entry resolves.</summary>
    private string ResolveBenchmarkDriverId()
    {
        var prestige = Pack.Teams.ToDictionary(t => t.Id, t => t.Prestige, StringComparer.Ordinal);
        string best = "";
        int bestPrestige = int.MinValue;
        foreach (var e in Pack.Entries.OrderBy(e => e.DriverId, StringComparer.Ordinal))
        {
            if (string.Equals(e.DriverId, _playerDriverId, StringComparison.Ordinal))
                continue;
            int p = prestige.GetValueOrDefault(e.TeamId, 0);
            if (p > bestPrestige)
            {
                bestPrestige = p;
                best = e.DriverId;
            }
        }
        return best;
    }

    /// <summary>Shapes each scored CHAMPIONSHIP round of the whole career into the grid-level facts the
    /// world-story detector reads, the race winner and the championship order after the round. Mirrors
    /// <see cref="BuildSmgpNarrativeSeasons"/>'s per-season loop (rehydrate pack + scoring, re-read stored
    /// results). Each driver id is normalized onto <c>_playerDriverId</c> when it is that season's player,
    /// so the player is excluded consistently as a story subject. A pure read over immutable results.</summary>
    private IReadOnlyList<Companion.Core.Smgp.SmgpWorldRound> BuildSmgpWorldRounds()
    {
        var result = new List<Companion.Core.Smgp.SmgpWorldRound>();
        int ordinal = 0;
        foreach (var season in CareerStore.ReadSeasons(_database))
        {
            ordinal++;
            var pack = SeasonPackFor(season);
            string seasonPlayerId = PlayerDriverIdFor(season, pack);
            string Key(string id) =>
                string.Equals(id, seasonPlayerId, StringComparison.Ordinal) ? _playerDriverId : id;
            var venueByRound = pack.Season.Rounds.ToDictionary(r => r.Round, VenueLabel);
            var entryTeam = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var e in pack.Entries)
                entryTeam.TryAdd(e.DriverId, e.TeamId);

            var stored = ResultStore.ReadSeasonResults(_database, season.Id).OrderBy(r => r.Round).ToList();
            var champStored = stored
                .Where(r => pack.Season.Rounds.FirstOrDefault(rd => rd.Round == r.Round)?.Championship ?? false)
                .ToList();
            if (champStored.Count == 0)
                continue;
            var scoring = ChampionshipCalendar.ResolveScoring(pack);
            var snapshots = StandingsEngine.ComputeSeason(scoring, champStored.Select(r => r.ToRoundResult()).ToList()).Snapshots;
            var snapByRound = snapshots.ToDictionary(s => s.AfterRound);

            string WorldName(string origId) => string.Equals(origId, seasonPlayerId, StringComparison.Ordinal)
                ? PlayerDisplayName() ?? PlayerDefaultName
                : _environment.Rules.SmgpDriverProfiles.ForDriver(origId)?.Name is { Length: > 0 } n
                    ? n
                    : pack.Drivers.FirstOrDefault(d => string.Equals(d.Id, origId, StringComparison.Ordinal))?.Name ?? origId;

            int index = 0;
            foreach (var r in champStored)
            {
                index++;
                var envelope = r.ToEnvelope();
                var race = envelope.Result.Sessions.FirstOrDefault(x => x.Kind == Companion.Core.Scoring.SessionKind.Race)
                    ?? envelope.Result.Sessions.FirstOrDefault();
                string? winnerOrig = race?.Entries
                    .FirstOrDefault(e => e.Status == FinishStatus.Classified && e.Position == 1)?.DriverId;

                var standings = new List<Companion.Core.Smgp.SmgpWorldStanding>();
                if (snapByRound.TryGetValue(r.Round, out var snap))
                    foreach (var d in snap.Drivers.OrderBy(d => d.Position ?? int.MaxValue))
                        standings.Add(new Companion.Core.Smgp.SmgpWorldStanding
                        {
                            Position = d.Position ?? 0,
                            DriverId = Key(d.DriverId),
                            Name = WorldName(d.DriverId),
                            Points = (int)Math.Round(d.CountedPoints.ToDouble()),
                            TeamId = entryTeam.GetValueOrDefault(d.DriverId, ""),
                        });

                result.Add(new Companion.Core.Smgp.SmgpWorldRound
                {
                    Season = ordinal,
                    Round = r.Round,
                    Venue = venueByRound.GetValueOrDefault(r.Round) ?? $"Round {r.Round}",
                    RoundIndex = index,
                    SeasonRounds = champStored.Count,
                    WinnerId = winnerOrig is null ? null : Key(winnerOrig),
                    WinnerName = winnerOrig is null ? "" : WorldName(winnerOrig),
                    WinnerTeamId = winnerOrig is not null ? entryTeam.GetValueOrDefault(winnerOrig, "") : "",
                    Standings = standings,
                });
            }
        }
        return result;
    }

    /// <summary>The rival's dossier line: his OWN words for the ladder state, a first challenge,
    /// the player one win up (the seat in sight), or him one win up. Data-driven
    /// (<c>data/rules/smgp/rival-quotes.json</c>); the deadpan default when no rules folder or no
    /// authored line. Display-only, so a per-round seed just keeps the same line on a re-open.</summary>
    private string RivalQuote(string driverId, Companion.Core.Smgp.SmgpBattleTally tally, int round)
    {
        if (_environment.RulesDirectory is null)
            return Companion.Core.Smgp.SmgpRivalQuotes.Default;

        var mood = tally.PlayerStreak >= 1 ? Companion.Core.Smgp.SmgpRivalMood.PlayerLeads
            : tally.RivalStreak >= 1 ? Companion.Core.Smgp.SmgpRivalMood.RivalLeads
            : Companion.Core.Smgp.SmgpRivalMood.First;

        return _environment.Rules.SmgpRivalQuotes.Line(driverId, mood, QuoteSeed(driverId, round));
    }

    /// <summary>A stable FNV-1a hash over (driver id, round), picks a line without wobbling when the
    /// same briefing is re-opened. The quote is never a fold input, so this seed carries no weight
    /// beyond display.</summary>
    private static uint QuoteSeed(string driverId, int round)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char c in driverId) { h ^= c; h *= 16777619; }
            h ^= (uint)round;
            h *= 16777619;
            return h;
        }
    }

    private Companion.Core.Grid.GridSelection? _gridSelection;
    private bool _gridSelectionResolved;

    /// <summary>The career's chosen season field (v0.6.0), read once from the start state and cached.
    /// Null for a whole-pack career (byte-identical). Both the display resolve and the fold read the
    /// SAME selection, so the grid the player sees, stages, and scores are one and the same.</summary>
    private Companion.Core.Grid.GridSelection? CurrentGridSelection()
    {
        if (_gridSelectionResolved)
            return _gridSelection;
        _gridSelectionResolved = true;
        _gridSelection = StateStore.ReadPlayerState(_database, _seasonId, StateStore.StageStart)?.GridSelection;
        return _gridSelection;
    }

    private bool? _formAware;

    /// <summary>Whether this career is form-reactive (Ratings Phase 3), read once from the start state
    /// and cached (it never changes over a career). Drives the DISPLAY resolves that must equal what
    /// the fold scored, the expected finish the Setup Gamble stakes against, and the recommended
    /// slider, so the number shown matches the number folded. Staging keeps form OFF here because
    /// <c>GridStager.Build</c> applies the same nudge to the written file (no double-apply); false for
    /// a pre-Phase-3 career ⇒ byte-identical.</summary>
    private bool CurrentFormAware() =>
        _formAware ??= StateStore.ReadPlayerState(_database, _seasonId, StateStore.StageStart)?.FormAware ?? false;

    private CharacterProfile? _characterProfile;
    private bool _characterProfileResolved;

    private CharacterProfile? CurrentCharacterProfile()
    {
        if (!_characterProfileResolved)
        {
            var start = StateStore.ReadPlayerState(
                _database, _seasonId, StateStore.StageStart);
            _characterProfile = start?.Character;

            // Version 1 had no fallback for packs whose team performance scalars were all neutral,
            // which let a Level-D SMGP car inherit a front-running P3 benchmark. An UNSTARTED career
            // has no derived race rows to preserve, so upgrade its input profile in place. Once any
            // result exists the version is immutable and replay keeps the exact historical formula.
            if (start is not null &&
                _characterProfile?.ProgressionVersion == CharacterLevelProgression.Level300Version &&
                _characterProfile.ExpectationModelVersion == SeatStrengthModel.TeamAndPerformanceVersion &&
                MaxAppliedRound == 0)
            {
                _characterProfile = _characterProfile with
                {
                    ExpectationModelVersion = CharacterProfile.CurrentExpectationModelVersion,
                };
                StateStore.UpsertPlayerState(
                    _database,
                    _seasonId,
                    StateStore.StageStart,
                    start with { Character = _characterProfile });
            }
            _characterProfileResolved = true;
        }
        return _characterProfile;
    }

    private bool CurrentCharacterRequiresRoundConditions()
    {
        var character = CurrentCharacterProfile();
        return character?.ProgressionVersion == CharacterLevelProgression.Level300Version
            && _environment.RulesDirectory is not null
            && PlayerCarScalarPolicy.RequiresRoundConditions(
                character,
                _environment.Rules.Character,
                _environment.Rules.MasterySkills);
    }

    private PlayerRoundConditionsInput? RoundConditionsForGrid(int round)
    {
        var stored = PlayerRoundConditionsStore.ReadRound(_database, _seasonId, Pack, round);
        if (stored is not null)
            return stored;
        if (!CurrentCharacterRequiresRoundConditions())
            return null;

        bool? inferredWet = PlayerRoundConditions.TryInferIsWet(Pack, round);
        if (inferredWet is null)
            throw new InvalidOperationException(
                $"Round {round} has mixed, dynamic, missing, or unknown race weather. " +
                "Declare whether the player-car setup is wet or dry before staging this grid.");
        return PlayerRoundConditions.Prepare(Pack, round, inferredWet.Value);
    }

    private PlayerRoundConditionsInput? EnsureRoundConditionsPersisted(int round)
    {
        if (!CurrentCharacterRequiresRoundConditions())
            return PlayerRoundConditionsStore.ReadRound(_database, _seasonId, Pack, round);
        var prepared = RoundConditionsForGrid(round)
            ?? throw new InvalidOperationException(
                $"Round {round} needs a pre-race player-car weather declaration.");
        return PlayerRoundConditionsStore.Declare(
            _database, _seasonId, Pack, prepared, NowUtc());
    }

    public bool? CurrentRoundIsWet()
    {
        if (SeasonComplete)
            return null;
        int round = CurrentRoundNumber;
        return PlayerRoundConditionsStore.ReadRound(_database, _seasonId, Pack, round)?.IsWet
            ?? PlayerRoundConditions.TryInferIsWet(Pack, round);
    }

    public bool CurrentRoundNeedsWeatherDeclaration()
    {
        if (SeasonComplete || !CurrentCharacterRequiresRoundConditions())
            return false;
        int round = CurrentRoundNumber;
        return PlayerRoundConditionsStore.ReadRound(_database, _seasonId, Pack, round) is null
            && PlayerRoundConditions.TryInferIsWet(Pack, round) is null;
    }

    public void DeclareCurrentRoundWeather(bool isWet)
    {
        if (SeasonComplete)
            throw new InvalidOperationException("The season is complete, there is no round to declare.");
        var character = CurrentCharacterProfile();
        if (character?.ProgressionVersion != CharacterLevelProgression.Level300Version)
            throw new InvalidOperationException(
                "Pre-race player-car weather declarations belong to progression-v2 characters.");
        PlayerRoundConditionsStore.Declare(
            _database,
            _seasonId,
            Pack,
            PlayerRoundConditions.Prepare(Pack, CurrentRoundNumber, isWet),
            NowUtc());
    }

    /// <summary>The career's character resolved for one exact round. The profile is season-stable,
    /// but wet/dry and long/short CAR effects are not, so only the profile is cached; modifiers are
    /// re-resolved from the typed round facts on every grid projection.</summary>
    private PlayerCharacterPatch? CurrentCharacterPatch(int round)
    {
        var character = CurrentCharacterProfile();
        if (character is null || _environment.RulesDirectory is null)
            return null;

        var rules = _environment.Rules.Character;
        var conditions = RoundConditionsForGrid(round);
        var active = conditions is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(PlayerRoundConditions.ActiveConditions(conditions), StringComparer.Ordinal);
        if (_environment.Rules.AgingCurves.TryForYear(Pack.Season.Year) is { } curve)
        {
            int age = _playerFirstSeasonAge + (_seasonYear - _firstSeasonYear);
            if (age < curve.PeakAgeStart)
            {
                active.Add("ageLtPeak");
                active.Add("ageBeforePeak");
            }
            else
            {
                active.Add("ageGtePeak");
                active.Add("ageAtOrAfterPeak");
            }
        }

        return new PlayerCharacterPatch
        {
            Profile = character,
            Modifiers = CharacterModifierResolver.Resolve(
                character, rules, _environment.Rules.MasterySkills, active),
            Rules = rules,
            MasterySkills = _environment.Rules.MasterySkills,
            RoundConditions = conditions,
        };
    }

    /// <summary>The player's chosen character name for this season, or null when there is none, the
    /// display identity the news/standings use instead of the historical driver they replaced.</summary>
    private string? CharacterName()
    {
        string? name = StateStore.ReadPlayerState(_database, _seasonId, StateStore.StageStart)?.Character?.Name;
        return string.IsNullOrEmpty(name) ? null : name;
    }

    // ---------- staging ----------

    /// <summary>
    /// OS/display-only handoff to the machine launcher configured by <see cref="CareerEnvironment"/>.
    /// Staging remains a separate explicit step in the briefing ViewModel, so a launch is never
    /// attempted after a failed or unconfirmed stage.
    /// </summary>
    public Ams2LaunchResult LaunchAms2() => _environment.Ams2Launcher.Launch();

    public StageOutcome StageCurrentGrid() => StageCurrentGrid(force: false);

    /// <summary>Explicit "apply this grid to AMS2": ALWAYS writes an app-marked file (backup-first),
    /// bypassing the diff-aware no-op and the community-file gate, so a grid the user chose is
    /// verifiable on disk (the AMS2 diagnosis found the default flow wrote 0 bytes, which is why
    /// "nothing changes"). Binds every AI driver to a REAL base-game livery for the class so AMS2
    /// accepts the file and shows the drivers (confirmed in-game). This is the "Apply grid to AMS2"
    /// action.</summary>
    public StageOutcome ApplyGridToAms2() =>
        StageCurrentGrid(force: true, alwaysWrite: true, baseGameLiveries: true);

    /// <summary>Activates the round's community liveries by replaying the pack's scenario .bat swaps —
    /// the community skin packs rotate which liveries are on the grid PER RACE by copying a round
    /// variant over each vehicle model's active override file. Discovers the .bat in the AMS2 root
    /// (the one that manages this class's custom-AI file) and applies the round's swaps backup-first.
    /// Skin files only, never the career DB, so the sim/fold/oracle are untouched. Null when there is
    /// no install, no matching .bat, or the .bat has no entry for this round.</summary>
    /// <summary>
    /// When the player's chosen car is a "bubble" livery outside this round's active pool (1988 was a
    /// pre-qualifying year, so a Coloni/Eurobrun DNQs some rounds), graft its skin into the SLOWEST
    /// same-model qualifier's slot so the player can pick their own car in-game with its real paint. The
    /// grid-seating already put the player on track; this only makes the SKIN show. Skin override files
    /// only (backup-first, and the active model file is regenerated from the pack's per-round variant on
    /// every stage), so the career DB / sim / oracle are never touched. Best-effort: any problem is a
    /// silent no-op. Returns a status line when a graft happened, else null.
    /// </summary>
    private string? ActivatePlayerBubbleCar(Ams2Installation installation, GridPlan plan)
    {
        try
        {
            // Already in the round's LIVE active pool (a qualifier, or a bubble car on a round it
            // qualified)? Only the model's active <model>.xml counts, a bubble car active in some OTHER
            // round's variant file must NOT look "already active", or the graft wrongly skips it.
            var scan = _environment.ScanInstalledLiveries(installation);
            if (scan.Liveries.Any(l => l.IsActive && IsLiveActiveFile(l) &&
                    string.Equals(l.Name, _playerLiveryName, StringComparison.Ordinal)))
                return null;

            // raceSkill by livery, from the pinned pack (every car, so the true slowest same-model peer
            // is considered even if the grid cap dropped it from the seated field).
            var driverSkill = Pack.Drivers.ToDictionary(d => d.Id, d => d.Ratings.RaceSkill, StringComparer.Ordinal);
            var skillByLivery = Pack.Entries
                .Where(e => driverSkill.ContainsKey(e.DriverId))
                .GroupBy(e => e.Ams2LiveryName, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => driverSkill[g.First().DriverId], StringComparer.Ordinal);

            var modelDirs = _environment.ContentLibrary.Vehicles.Values
                .Where(v => string.Equals(v.VehicleClass, plan.Ams2Class, StringComparison.Ordinal))
                .Select(v => v.Dir)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var dir in modelDirs)
            {
                string folder = Path.Combine(installation.InstallOverridesDirectory, dir);
                string activePath = Path.Combine(folder, dir + ".xml");
                if (!File.Exists(activePath))
                    continue;

                // A variant of THIS model that carries the player's car (so this is the player's model).
                string? sourcePath = Directory.EnumerateFiles(folder, dir + "_*.xml")
                    .FirstOrDefault(f => BubbleCarGraft.BlockGroups(File.ReadAllLines(f))
                        .Any(g => string.Equals(g.Name, _playerLiveryName, StringComparison.Ordinal)));
                if (sourcePath is null)
                    continue;

                string activeXml = File.ReadAllText(activePath);
                // Displace the slowest SAME-MODEL active qualifier, a seated peer, never the player.
                string? displace = BubbleCarGraft.ActiveNames(activeXml)
                    .Where(n => skillByLivery.ContainsKey(n) &&
                                !string.Equals(n, _playerLiveryName, StringComparison.Ordinal))
                    .OrderBy(n => skillByLivery[n])
                    .FirstOrDefault();
                if (displace is null)
                    continue;

                string? grafted = BubbleCarGraft.Graft(
                    activeXml, File.ReadAllText(sourcePath), _playerLiveryName, displace);
                if (grafted is null)
                    continue;

                ScenarioApplier.BackUp(activePath, _environment.Clock.GetUtcNow());
                File.WriteAllText(activePath, grafted);
                return $"Your car ({_playerLiveryName}) took {displace}'s grid slot for this race, " +
                       "its real skin is now active (pick it on the car-select screen).";
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>True when a scanned livery lives in the model's LIVE active override file
    /// (<c>&lt;model&gt;.xml</c>) rather than a per-round variant (<c>&lt;model&gt;_Round.xml</c>) or a
    /// timestamped backup. The scanner reads EVERY file, but only the active file is the pool AMS2 shows;
    /// without this filter a bubble car active in another round's variant looks "already active", so both
    /// the graft and the smart binding wrongly skip / keep it.</summary>
    private static bool IsLiveActiveFile(InstalledLivery livery) =>
        string.Equals(Path.GetFileNameWithoutExtension(livery.SourceFile), livery.VehicleFolder,
            StringComparison.OrdinalIgnoreCase);

    private ScenarioApplyResult? ApplyScenarioForRound(int round)
    {
        var installation = _environment.LocateInstall();
        if (installation is null)
            return null;
        string root = installation.InstallDirectory;

        // The selector's season sub-menu for THIS pack's year (":1996"). A class's selector can serve
        // several years off one bat ([F1_1996-1997]…FV10G1 has both :1996 and :1997), so the year both
        // picks the right bat and scopes BatScenarioReader to the right menu, without it a 1997 career
        // would read 1996's rounds (or none, when the default ":1988" menu is absent).
        string seasonLabel = ":" + Pack.Season.Year.ToString(CultureInfo.InvariantCulture);

        string? batPath;
        try
        {
            var managingClass = Directory.EnumerateFiles(root, "*.bat", SearchOption.TopDirectoryOnly)
                .Where(f => BatManagesClass(f, Pack.Season.Ams2Class))
                .ToList();
            // Prefer a bat that actually carries this season's menu; fall back to the first that manages
            // the class (older single-season selectors whose menu label is not the bare year still parse
            //, an absent menu yields no swaps and the caller falls back to the variant binder).
            batPath = managingClass.FirstOrDefault(f => BatHasSeasonLabel(f, seasonLabel))
                ?? managingClass.FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        if (batPath is null)
            return null;

        try
        {
            var map = BatScenarioReader.Parse(File.ReadAllText(batPath), seasonLabel);
            return map.TryGetValue(round, out var swaps)
                ? ScenarioApplier.Apply(root, swaps, _environment.Clock.GetUtcNow())
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>True when the batch file manages this vehicle class, it references the class's
    /// custom-AI file name (e.g. "F-Classic_Gen2"), so it is the scenario selector for this class
    /// regardless of its own arbitrary filename.</summary>
    private static bool BatManagesClass(string batPath, string vehicleClass)
    {
        try
        {
            return File.ReadAllText(batPath).Contains(vehicleClass, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>True when the selector has a season sub-menu labelled <paramref name="seasonLabel"/>
    /// (e.g. <c>:1996</c>), how a multi-year selector like <c>[F1_1996-1997]…</c> is disambiguated to
    /// the career's year before parsing.</summary>
    private static bool BatHasSeasonLabel(string batPath, string seasonLabel)
    {
        try
        {
            foreach (var line in File.ReadLines(batPath))
                if (line.Trim().Equals(seasonLabel, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // The per-round staging buttons (Briefing "Stage" / "Stage anyway") ALSO bind to real base-game
    // liveries so the written file is guaranteed to load in AMS2, same as the Skins-tab "Stage grid
    // into AMS2" action. Without this, the per-round path wrote the pack's community-skin livery names
    // (e.g. "1988 Lotus #1 - N. Piquet"); if the player has not installed those skins AMS2 silently
    // reverts the whole class to stock and shows nothing. Cosmetic staging only; the resolved grid the
    // sim scores is untouched, so the fold/oracle stay byte-identical. (A later refinement can prefer an
    // INSTALLED community-skin name per seat and fall back to the base-game floor otherwise.)
    public StageOutcome StageCurrentGrid(bool force) => StageCurrentGrid(force, alwaysWrite: false, baseGameLiveries: true);

    public StageOutcome StageCurrentGrid(bool force, bool alwaysWrite, bool baseGameLiveries = false)
    {
        var messages = new List<string>();

        if (SeasonComplete)
            return Failed(messages, "The season is complete, there is no round left to stage.");

        int roundNumber = CurrentRoundNumber;
        var packRound = RoundByNumber(roundNumber);

        GridPlan plan;
        try
        {
            // Commit the versioned player-car condition INPUT before the first AMS2 filesystem
            // action. A failed/force-gated stage leaves the same immutable declaration behind, so
            // retrying is idempotent and can never restage different conditional physics.
            EnsureRoundConditionsPersisted(roundNumber);
            plan = ResolveGrid(roundNumber);
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or InvalidDataException or JsonException)
        {
            return Failed(messages, ex.Message);
        }

        // STAGING-ONLY per-race form overlay: nudge each driver's staged pace ratings toward that
        // weekend's historical form (f1db-derived). Read only here (never the resolver/fold), so a
        // career carrying form re-simulates byte-identically. Absent on a pack => null => no nudge.
        var roundForm = Pack.Season.DriverForm?.GetValueOrDefault(roundNumber);
        var file = GridStager.Build(plan,
            $"{Pack.Manifest.Name} | Round {roundNumber}: {packRound.Name} | seed {MasterSeed}",
            roundForm);

        // Guaranteed-load binding: rebind each AI driver onto a REAL base-game livery for the class
        // (the game ships these names, from official-liveries.json, so it accepts the file and
        // shows the drivers) instead of the pack's community-skin names the install may not have.
        // Cosmetic staging only; never touches the resolved grid the sim scores. Opt-in via the
        // explicit "Apply grid to AMS2" action.
        if (baseGameLiveries)
        {
            // FIRST, make sure the pack's declared SKIN SEASON owns the car models (pack.json
            // skinSeason → the Skin Season Manager): conflicting season packs share the same
            // override pointer file (1983↔1985, 1990↔SMGP…), so staging on the wrong season would
            // show the other year's cars. Backup-first, all-or-nothing; unrecognized user files
            // hold the swap behind the same force gate as the AI file. No declared season → no-op.
            if (DeclaredSkinSeason() is { } declaredSeason)
            {
                var seasonStatus = SkinSeasonManager.Inspect(
                    declaredSeason.Set, _environment.SkinSeasons, declaredSeason.OverridesRoot);
                if (!seasonStatus.IsFullyActive)
                {
                    var swap = SkinSeasonManager.Activate(
                        declaredSeason.Set, _environment.SkinSeasons, declaredSeason.OverridesRoot,
                        force, _environment.Clock.GetUtcNow());
                    if (swap.Applied > 0 || !swap.Success)
                        messages.Add(swap.Message);
                }
            }

            // Then, activate THIS round's community liveries the way the pack's scenario .bat does:
            // swap each vehicle model's active override file to the round's variant (backup-first). A
            // pre-qualifying season rotates which liveries are on the grid per race; without this the
            // app wrote the custom-AI file but AMS2's livery pool stayed on the wrong variant and
            // stock-filled the cars our file didn't happen to match. After this the round's real skins
            // are ACTIVE, so the smart binding below keeps them. No scenario .bat (other packs) → no-op.
            var scenario = ApplyScenarioForRound(roundNumber);
            if (scenario is { AnyApplied: true })
                messages.Add(
                    $"Activated this race's liveries, {scenario.Applied} vehicle model(s) switched to the " +
                    "round's skins (your previous set was backed up).");

            // The same per-race swap for packs WITHOUT a scenario .bat: the big skin packs ship
            // per-race CHANGE-POINT variants for manual copying (formula_classic_g4m1_03Imola.xml
            // = the grid from Imola on), anchor them to the calendar and bind the set in force
            // at this round automatically (backup-first). Ships no variants → no-op.
            if (scenario is null or { AnyApplied: false } &&
                _environment.LocateInstall() is { } variantInstall)
            {
                var variantModelDirs = _environment.ContentLibrary.Vehicles.Values
                    .Where(v => string.Equals(v.VehicleClass, plan.Ams2Class, StringComparison.Ordinal))
                    .Select(v => v.Dir)
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                var calendar = Pack.Season.Rounds
                    .Select(r => new VariantOverrideBinder.CalendarRound(r.Round, r.Name, r.Track.RealVenue))
                    .ToList();
                var bound = VariantOverrideBinder.BindRound(
                    variantInstall.InstallOverridesDirectory, variantModelDirs, roundNumber,
                    calendar, Pack.Season.Year,
                    DeclaredSkinSeason()?.Set.ModelXml, _environment.Clock.GetUtcNow());
                if (bound.AnyChanged)
                    messages.Add(
                        $"Bound this race's livery variants, {bound.Swapped} vehicle model(s) switched to the " +
                        $"round's skins{(bound.Restored > 0 ? $", {bound.Restored} restored to the season base" : "")} " +
                        "(previous files backed up).");
            }

            // ACTIVE-SET activation (the 1985-style packs): a fixed slot budget with alternates
            // kept inside one giant comment. Do the pack's own documented copy-paste procedure
            // automatically, lift the alternates this round's grid needs into the slots of cars
            // the round does not field (backup-first, the comment and its alternates preserved).
            // Files without commented alternates are naturally untouched.
            if (_environment.LocateInstall() is { } activeSetInstall)
            {
                var gridLiveries = plan.Seats.Select(s => s.Ams2LiveryName)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                int? activeSetMaxSlot =
                    _environment.ContentLibrary.LiveryCaps.TryGetValue(plan.Ams2Class, out int activeSetCap)
                        ? LiveryOverrideWriter.FirstCustomSlot + activeSetCap - 1
                        : null;
                var activeSetModelDirs = _environment.ContentLibrary.Vehicles.Values
                    .Where(v => string.Equals(v.VehicleClass, plan.Ams2Class, StringComparison.Ordinal))
                    .Select(v => v.Dir)
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                int activatedTotal = 0;
                foreach (var dir in activeSetModelDirs)
                {
                    string activeSetPath = Path.Combine(
                        activeSetInstall.InstallOverridesDirectory, dir, dir + ".xml");
                    if (!File.Exists(activeSetPath))
                        continue;
                    try
                    {
                        var (activeNames, altNames) =
                            ActiveSetRewriter.AvailableNames(File.ReadAllText(activeSetPath));
                        if (altNames.Count == 0)
                            continue; // no commented alternates, not a 1985-style file
                        var wanted = gridLiveries
                            .Where(n => activeNames.Contains(n, StringComparer.Ordinal) ||
                                        altNames.Contains(n, StringComparer.Ordinal))
                            .ToList();
                        if (wanted.Count == 0)
                            continue;
                        var setResult = ActiveSetRewriter.Apply(
                            activeSetPath, wanted, activeSetMaxSlot, _environment.Clock.GetUtcNow());
                        if (setResult.Changed)
                            activatedTotal += setResult.Activated;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // Best-effort cosmetic pass, never blocks staging.
                    }
                }
                if (activatedTotal > 0)
                    messages.Add(
                        $"Activated {activatedTotal} alternate livery(ies) for this round's grid, " +
                        "slots swapped from the pack's alternates list (previous files backed up).");
            }

            // FIXED FULL-SET SKIN ACTIVATION (SMGP). AMS2 loads a car model's custom liveries ONCE, at
            // launch, and only the active (numeric-slot) ones, so a per-race rotation (park the
            // non-qualifiers, switch the round's in) breaks: the just-switched-on skins aren't in the
            // pool AMS2 already loaded, so those cars pool-fill with random stock drivers, and it takes
            // a full game restart every round to fix. Instead, activate EVERY SMGP livery that fits each
            // model's slot cap, ONCE, parking nothing, so the active set is STABLE: AMS2 loads it at
            // launch and every car that fits stays painted, no per-round restart. (The pre-qualifying
            // field is display-only anyway; whatever fits the cap is always active.) Cars beyond a
            // model's cap keep a base-game livery. SMGP-only; runs before the smart binder's scan.
            if (IsSmgpPack && _environment.LocateInstall() is { } smgpInstall)
            {
                var packLiveries = Pack.Entries
                    .Select(e => e.Ams2LiveryName)
                    .ToHashSet(StringComparer.Ordinal);
                int? smgpMaxSlot =
                    _environment.ContentLibrary.LiveryCaps.TryGetValue(plan.Ams2Class, out int smgpCap)
                        ? LiveryOverrideWriter.FirstCustomSlot + smgpCap - 1
                        : null;
                var smgpModelDirs = _environment.ContentLibrary.Vehicles.Values
                    .Where(v => string.Equals(v.VehicleClass, plan.Ams2Class, StringComparison.Ordinal))
                    .Select(v => v.Dir)
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                // roundLiveries == packLiveries → activate every inactive SMGP livery, park none.
                var activation = RoundLiveryActivator.ApplyRound(
                    smgpInstall.InstallOverridesDirectory, smgpModelDirs, packLiveries, packLiveries,
                    smgpMaxSlot, _environment.Clock.GetUtcNow());
                if (activation.AnyChanged)
                    messages.Add(
                        $"Activated {activation.Activated} SMGP skin(s) as a fixed set across " +
                        $"{activation.ModelsChanged} car model(s), every livery that fits is now switched on " +
                        "(previous files backed up). Fully close and reopen AMS2 (launch it DIRECTLY, not through a " +
                        "mod manager) so it loads the active skins; after that they stay put with no per-round restart.");
                if (activation.Skipped.Count > 0)
                    messages.Add(
                        $"Note: {activation.Skipped.Count} skin(s) exceed AMS2's per-model livery limit and will show " +
                        $"a base-game car: {string.Join(", ", activation.Skipped.Take(8))}.");
            }

            // If the player picked a "bubble" car outside this round's active pool (1988 pre-qualifying:
            // a Coloni/Eurobrun DNQs some rounds), graft its skin into the slowest same-model qualifier's
            // slot so the player can pick THEIR car in-game with its real paint. The grid-seating already
            // put them on track; this activates the skin. The scan below then keeps the now-active livery.
            var bubbleInstall = _environment.LocateInstall();
            if (bubbleInstall is not null)
            {
                string? bubble = ActivatePlayerBubbleCar(bubbleInstall, plan);
                if (bubble is not null)
                    messages.Add(bubble);
            }

            // Smart binding: keep a driver's real community livery where that skin is INSTALLED AND
            // ACTIVE on disk (real historical paint) and floor every other car onto a base-game livery
            // the game always ships (guaranteed load). So an installed 1988 skin pack shows its real
            // liveries while a car whose skin the player has not installed still loads instead of
            // reverting the whole class to stock. A class spans several vehicle models (e.g.
            // formula_classic_g2m1/2/3 + mclaren_mp44), each with its own Overrides folder, union them.
            var skinScan = _environment.ScanInstalledLiveries(_environment.LocateInstall());
            var classFolders = _environment.ContentLibrary.Vehicles.Values
                .Where(v => string.Equals(v.VehicleClass, plan.Ams2Class, StringComparison.Ordinal))
                .Select(v => v.Dir)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var installedActive = skinScan.Liveries
                .Where(l => l.IsActive && IsLiveActiveFile(l) &&
                            (classFolders.Count == 0 || classFolders.Contains(l.VehicleFolder)))
                .Select(l => l.Name)
                .ToHashSet(StringComparer.Ordinal);

            int baseSeats = file.Drivers.Count(d => d.Tracks.Count == 0);
            int keptCommunity = file.Drivers.Count(d => d.Tracks.Count == 0 && installedActive.Contains(d.LiveryName));
            var rebound = BaseGameLiveryBinder.RebindToBaseGame(file, _environment.ContentLibrary, installedActive);
            if (!ReferenceEquals(rebound, file))
            {
                file = rebound;
                messages.Add(
                    keptCommunity == 0
                        ? $"Bound the grid to real base-game {file.VehicleClass} liveries so AMS2 loads it " +
                          "and shows the drivers. Car paint is the game's default; install the matching " +
                          "community skins to see historical liveries."
                        : keptCommunity >= baseSeats
                            ? $"All {baseSeats} cars are on installed community skins, AMS2 will show the real liveries."
                            : $"Kept {keptCommunity} installed community skin(s) and bound the other " +
                              $"{baseSeats - keptCommunity} to base-game {file.VehicleClass} liveries so every car loads.");
            }

            // Zero-stock: name every LIVE-active community livery. A car whose livery is active in the
            // pool but which the grid CAP dropped (or a peer whose slot a bubble-car graft took) would
            // otherwise leave AMS2 stock-filling that slot with a made-up driver. Add it from the full
            // (uncapped) field so every visible car shows its real driver. Cosmetic, the sim always
            // scores the capped grid; these extra names ride the AMS2 file only.
            try
            {
                var fullField = RoundGridResolver.Resolve(Pack, roundNumber,
                    new PlayerSeat { Ams2LiveryName = _playerLiveryName, Character = CurrentCharacterPatch(roundNumber) },
                    CurrentGridSelection(), capToGridSize: false);
                var byLivery = fullField.Seats
                    .GroupBy(s => s.Ams2LiveryName, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => GridStager.SeatToDriver(g.First()), StringComparer.Ordinal);
                var have = file.Drivers.Select(d => d.LiveryName).ToHashSet(StringComparer.Ordinal);
                var extra = installedActive
                    .Where(n => !have.Contains(n) && byLivery.ContainsKey(n))
                    .Select(n => byLivery[n]).ToList();
                if (extra.Count > 0)
                {
                    file = file with { Drivers = file.Drivers.Concat(extra).ToList() };
                    messages.Add(
                        $"Named {extra.Count} more active livery{(extra.Count == 1 ? "" : "s")} so no car on " +
                        "the grid shows a stock/made-up driver.");
                }
            }
            catch (InvalidOperationException) { /* best-effort cosmetic pass, never blocks staging */ }

            // SMGP full-coverage naming (the g3m2/g3m4 pool-fill fix). AMS2's grid generator picks
            // base-livery SLOTS per model on its own; our override + custom-AI files only paint/name a
            // slot it happens to pick, so ANY slot we do not name shows a STOCK/made-up driver. Name
            // EVERY livery it can field: (1) all 34 SMGP customs, incl. the ones the per-race DNQ
            // dropped (ignoreStarters enumerates the whole covering field), and (2) every base-game
            // class livery, mapped to an SMGP driver identity (weakest-first, so a base-paint slot reads
            // as a backmarker, not a stranger). Cosmetic staging ONLY, the sim always scores the capped
            // grid, and the f1db oracle + byte-identical replay never see it. Base-slot cars keep default
            // PAINT until their slots are overridden (phase 2), but no car ever shows a stock driver.
            if (IsSmgpPack)
            {
                try
                {
                    var smgpField = RoundGridResolver.Resolve(Pack, roundNumber, playerSeat: null,
                        CurrentGridSelection(), capToGridSize: false, ignoreStarters: true);
                    var identitiesWeakestFirst = smgpField.Seats
                        .OrderBy(s => s.Ratings.RaceSkill)
                        .Select(GridStager.SeatToDriver)
                        .ToList();
                    var named = file.Drivers.Select(d => d.LiveryName).ToHashSet(StringComparer.Ordinal);
                    var addSmgp = new List<CustomAiDriver>();
                    // (1) every SMGP custom the file does not already name (the DNQ'd cars).
                    foreach (var driver in identitiesWeakestFirst)
                        if (named.Add(driver.LiveryName))
                            addSmgp.Add(driver);
                    // (2) every base-game class livery, mapped to an SMGP identity so no stock name shows.
                    if (identitiesWeakestFirst.Count > 0 &&
                        _environment.ContentLibrary.OfficialLiveries.TryGetValue(plan.Ams2Class, out var official))
                    {
                        int i = 0;
                        foreach (var baseName in official.Select(l => l.Name)
                                     .Where(n => !string.IsNullOrWhiteSpace(n))
                                     .Distinct(StringComparer.Ordinal))
                        {
                            if (!named.Add(baseName))
                                continue;
                            addSmgp.Add(identitiesWeakestFirst[i % identitiesWeakestFirst.Count] with { LiveryName = baseName });
                            i++;
                        }
                    }
                    if (addSmgp.Count > 0)
                    {
                        file = file with { Drivers = file.Drivers.Concat(addSmgp).ToList() };
                        messages.Add(
                            $"SMGP: named every livery AMS2 can field ({addSmgp.Count} more, incl. base-game " +
                            "slots) so no grid car shows a stock driver. Cars on base-game slots keep the " +
                            "default paint until those skins are added.");
                    }
                }
                catch (InvalidOperationException) { /* best-effort cosmetic pass, never blocks staging */ }
            }
        }

        Ams2Installation? installation = _environment.LocateInstall();
        if (installation is null)
            return Failed(messages,
                "No AMS2 installation was found, nothing was staged. Verify the game is installed " +
                "(or configure the install folder) and try again.");

        // ONE aggregate line for the livery scan; the per-file unreadable list rides along
        // as collapsed details, never as a wall of warning rows.
        var scan = _environment.ScanInstalledLiveries(installation);
        if (scan.FilesScanned > 0)
            messages.Add(scan.Summary);
        var details = scan.UnreadableFiles;

        // PRIMARY name authority: the user's installed CustomAIDrivers class file. A name it
        // defines is valid whatever the skin state, no false "won't bind" warning.
        var installedAiNames = _environment.ScanInstalledAiNames(installation, file.VehicleClass);

        var preflight = GridStager.Preflight(
            file, _environment.ContentLibrary, scan.Liveries, plan.TrackId, plan.Seats.Count, installedAiNames);
        messages.AddRange(preflight.Issues.Select(i => $"{i.Severity}: {i.Message}"));

        if (preflight.HasErrors)
        {
            messages.Add("Staging aborted, fix the preflight errors above and stage again.");
            return new StageOutcome { Success = false, Messages = messages, Details = details };
        }

        try
        {
            // NAMeS-primary staging ("found before overwritten"): the installed class file is
            // read before any write and, for every seat it already defines, its name + base
            // ratings win, only this round's / the career's delta (measured against the pinned
            // pack's own driver baseline) is applied over them.
            var result = GridStager.StageOrRefuse(
                file, installation.CustomAiDriversDirectory, _environment.Clock.GetUtcNow(), force,
                PackBaselineByLivery(),
                // The grid editor's cosmetic per-seat overrides (rename / rebind livery), applied to
                // the staged file only, never the resolved grid the sim scores.
                StagingOverrideStore.Read(_database, _seasonId),
                alwaysWrite);

            if (result.RequiresForce)
            {
                // The community-file force gate is an EXPECTED choice, not a failure: the
                // briefing renders this outcome as an informational (amber) banner with the
                // "Overwrite anyway (backup first)" escape hatch.
                messages.Add(
                    $"Your installed {file.VehicleClass}.xml differs from this round's grid " +
                    "(community NAMeS file). Your installed names/AI are kept, only this round's " +
                    "grid selection and your career changes are applied. 'Overwrite anyway' takes a " +
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
    /// or career effect) keyed by <c>ams2LiveryName</c>, the delta reference for NAMeS-primary
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
    /// (no trackForm, no aiOverrides, no career drift), the reference the staging merge diffs
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
        SetupDownforce = driver.Ratings.SetupDownforce,
        SetupDownforceRandomness = driver.Ratings.SetupDownforceRandomness,
    };

    // ---------- season-end restore (IAiFileRestore) ----------

    /// <summary>Re-backup the current class XML first, then restore the pre-season original:
    /// the newest backup NOT generated by this app (the user's own file, snapshotted before
    /// the first divergent write). When every backup is app-generated, the newest backup is
    /// used. Restore never destroys state, the pre-restore file is always in the backups.</summary>
    public RestoreOutcome RestoreOriginalAiFile()
    {
        var messages = new List<string>();

        var installation = _environment.LocateInstall();
        if (installation is null)
        {
            messages.Add("No AMS2 installation was found, there is nothing to restore.");
            return new RestoreOutcome { Success = false, Messages = messages };
        }

        string vehicleClass = Pack.Season.Ams2Class;
        var backup = new CustomAiBackup(installation.CustomAiDriversDirectory);
        var backups = backup.ListBackups(vehicleClass);
        if (backups.Count == 0)
        {
            messages.Add(
                $"No backup of {vehicleClass}.xml exists, the app never overwrote it, " +
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
        if (_careerFileDeleted)
            throw new InvalidOperationException(
                "This career has ended - the driver was killed (Hardcore) and the save was deleted.");
        var beforePlayer = CurrentPlayerState();
        if (beforePlayer?.Deceased == true)
            throw new InvalidOperationException(
                "The driver has died - the career is over. In Normal mode, restore a save to continue.");
        if (beforePlayer?.Smgp?.CareerOver == true)
            throw new InvalidOperationException(
                "The SMGP career is over - a rival took the last seat at the LEVEL D floor.");
        // The Dynasty bankruptcy floor is terminal too (economy §7), a folded team takes no more
        // rounds, exactly like the two guards above.
        if (beforePlayer?.Economy?.Bankrupt == true)
            throw new InvalidOperationException(
                "The team is bankrupt, the Dynasty is over. Restore a save to continue, if one exists.");
        if (SeasonComplete)
            throw new InvalidOperationException("The season is complete, there is no round to score.");
        // The inverse of AutoSimulateRound's fit-check: an injured driver's round is auto-simulated,
        // never entered manually. The hub already routes to the sit-out screen; this makes the rule
        // hold for EVERY caller, so no path can score an injured player or stall the healing
        // countdown (character death & injury §5).
        if (beforePlayer is not null
            && (beforePlayer.RaceSuspensionRemaining > 0 || beforePlayer.SeasonEndingInjury))
            throw new InvalidOperationException(
                "The driver is injured, this round is auto-simulated. Continue from the sit-out screen.");

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
        // Resolve every condition read before opening the rollback transaction. These helpers read
        // the character/start state and the provenance journal; issuing either command without the
        // ambient transaction after BeginTransaction makes Microsoft.Data.Sqlite reject the preview.
        // Declare below revalidates the prepared input inside the transaction before staging it.
        var preparedRoundConditions = CurrentCharacterRequiresRoundConditions()
            ? RoundConditionsForGrid(roundNumber)
                ?? throw new InvalidOperationException(
                    $"Round {roundNumber} needs a pre-race player-car weather declaration.")
            : null;

        using var transaction = _database.Connection.BeginTransaction();
        if (preparedRoundConditions is not null)
        {
            PlayerRoundConditionsStore.Declare(
                _database, _seasonId, Pack, preparedRoundConditions, nowUtc, transaction);
        }
        ResultStore.Append(
            _database, _seasonId, roundNumber,
            JsonSerializer.Serialize(envelope, CoreJson.Options), nowUtc, "manual", transaction);
        var fold = ReplayService.FoldRound(
            _database, _seasonId, Pack, MasterSeedU, SimInputs(), roundNumber, nowUtc, transaction);
        transaction.Rollback();
        return fold;
    }

    /// <summary>The live path: store the round's raw-result ENVELOPE and run the unified fold
    ///, one atomic unit via <see cref="ReplayService.ImportAndFoldRound"/>, so a stored raw
    /// result can never exist without its fold (docs/dev/m5-fix-integration.md step 3). When
    /// the final round lands, the season-end pipeline runs off the folded player state.</summary>
    public void Apply(ResultDraft draft)
    {
        if (_careerFileDeleted)
            throw new InvalidOperationException(
                "This career has ended, the driver was killed (Hardcore) and the save was deleted.");
        // A dead driver takes no more rounds, terminal, like the SMGP CareerOver floor. (Slice 3.)
        var beforePlayer = CurrentPlayerState();
        if (beforePlayer?.Deceased == true)
            throw new InvalidOperationException(
                "The driver has died, the career is over. In Normal mode, restore a save to continue.");
        // The SMGP LEVEL-D floor knock-out is terminal too, a career kicked out of F1 SMGP takes no more
        // rounds. Previously CareerOver only suppressed the battle re-fold + season-end rollover, so a floored
        // player could still enter results; this closes that (SmgpState.CareerOver's own doc promised it).
        if (beforePlayer?.Smgp?.CareerOver == true)
            throw new InvalidOperationException(
                "The SMGP career is over, a rival took the last seat at the LEVEL D floor.");
        // The Dynasty bankruptcy floor is terminal too (economy §7), a folded team takes no more
        // rounds. All three guard sites (Preview/Apply/AutoSimulateRound) repeat this chain: a new
        // terminal state that skips one leaves a scoring hole (the SMGP floor's original bug).
        if (beforePlayer?.Economy?.Bankrupt == true)
            throw new InvalidOperationException(
                "The team is bankrupt, the Dynasty is over. Restore a save to continue, if one exists.");
        if (SeasonComplete)
            throw new InvalidOperationException("The season is complete, there is no round to apply.");
        // Same availability gate as Preview: an injured driver's round only ever folds through
        // AutoSimulateRound (which requires the inverse), so manual scoring of an unfit player is
        // impossible at the service layer, not merely unreachable in the shipped UI.
        if (beforePlayer is not null
            && (beforePlayer.RaceSuspensionRemaining > 0 || beforePlayer.SeasonEndingInjury))
            throw new InvalidOperationException(
                "The driver is injured, this round is auto-simulated. Continue from the sit-out screen.");

        int roundNumber = CurrentRoundNumber;
        var packRound = RoundByNumber(roundNumber);
        ValidateDraft(draft, roundNumber);

        // A caller may enter a result without pressing the stage button. Persist the same pure
        // candidate now (after draft validation, before the raw-result transaction); ambiguous
        // weather still requires the explicit declaration seam and fails closed.
        EnsureRoundConditionsPersisted(roundNumber);

        var envelope = BuildEnvelope(draft, roundNumber, packRound);
        string nowUtc = NowUtc();

        // App-level provenance row about the entry EVENT (excluded from the replay byte-compare
        // like every provenance row), appended INSIDE the fold's atomic transaction, so a crash
        // can never leave a folded round whose provenance (the news journal's dispatch source)
        // silently vanished.
        var journalDelta = new ResultAppliedDelta
        {
            Round = roundNumber,
            RoundName = packRound.Name,
            WinnerDriverId = draft.Classified.Count > 0 ? draft.Classified[0] : null,
            ClassifiedCount = draft.Classified.Count,
            DnfCount = draft.DidNotFinish.Count,
            DsqCount = draft.Disqualified.Count,
        };
        ReplayService.ImportAndFoldRound(
            _database, _seasonId, Pack, MasterSeedU, SimInputs(), roundNumber, envelope, nowUtc,
            withTransaction: transaction => JournalStore.Append(_database, _seasonId, roundNumber,
                new JournalEvent
                {
                    Phase = DataJournalPhases.ResultProvenance,
                    Entity = "round",
                    DeltaJson = JsonSerializer.Serialize(journalDelta, CoreJson.Options),
                    Cause = "result-entered",
                },
                nowUtc,
                transaction));

        // Character death & injury (Slice 3): detect a REAL Deceased transition this round. A Hardcore
        // death is the ONE destructive file op, physically delete the career file + every snapshot,
        // guarded hard (only Hardcore, only on a genuine alive→dead transition, and NEVER on replay,
        // which runs ReplayService directly and never this path). The session is then spent.
        bool justDied = beforePlayer?.Deceased != true && CurrentPlayerState()?.Deceased == true;
        if (justDied && _mortality == MortalityMode.Hardcore)
        {
            // Capture the death screen (Slice 5) from the intact DB BEFORE it is destroyed, the permadeath
            // screen renders from this with no session/DB access (the DeathScreenHandoffTests contract).
            if (CurrentPlayerState() is { } deadPlayer)
                _deathScreen = BuildDeathScreen(deadPlayer);
            _database.Dispose();
            SaveSlotStore.DeleteCareerAndAllSaves(CareerFilePath);
            _careerFileDeleted = true;
            return; // the file is gone, the season-end pipeline must not run against it
        }

        // A dead driver (Normal) banks no title and rolls no offers, the season does not "complete";
        // the death screen (Slice 5) offers a restore instead. A team that JUST went bankrupt
        // (economy §7) is suppressed the same way: a folded team banks no title and rolls no
        // offers; the bankruptcy screen (built lazily, the file survives) takes over.
        bool justWentBankrupt = beforePlayer?.Economy?.Bankrupt != true
            && CurrentPlayerState()?.Economy?.Bankrupt == true;
        if (SeasonComplete && !justDied && !justWentBankrupt)
            EnsureSeasonEnd();
    }

    /// <summary>The envelope stored as the round's raw payload: the mapped classification
    /// plus the unre-derivable round context, the slider actually driven (the draft's value,
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
        var roundConditions = PlayerRoundConditionsStore.ReadRound(
            _database, _seasonId, Pack, roundNumber)
            ?? (CurrentCharacterRequiresRoundConditions() ? RoundConditionsForGrid(roundNumber) : null);
        if (roundConditions is not null && draft.IsWet != roundConditions.IsWet)
            throw new InvalidOperationException(
                $"Round {roundNumber} was staged as " +
                $"{(roundConditions.IsWet ? "wet" : "dry")}; the result cannot change " +
                "the player-car physics after the race.");
        return new RoundResultEnvelope
        {
            Result = ToRoundResult(draft, roundNumber, packRound),
            SliderUsed = Math.Clamp(slider, DifficultyModel.MinSlider, DifficultyModel.MaxSlider),
            PlayerDnfCause = PlayerDnfCauseFrom(draft),
            PlayerAccidentSeverity = PlayerAccidentSeverityFrom(draft),
            QualifyingOrder = draft.QualifyingOrder,
            IsWet = roundConditions?.IsWet ?? draft.IsWet,
            CalledShot = draft.CalledShot,
            SmgpRival = draft.SmgpRival,
            AiDnfCauses = AiDnfCausesFrom(draft),
        };
    }

    /// <summary>The AI drivers' raw DNF cause letters (v9, capture-only, the fold never reads
    /// them; display readers name AI retirements). Null when no AI retired with a cause, so
    /// such rounds serialize exactly as before.</summary>
    private IReadOnlyDictionary<string, string>? AiDnfCausesFrom(ResultDraft draft)
    {
        Dictionary<string, string>? causes = null;
        foreach (var (driverId, reason) in draft.DidNotFinish)
        {
            if (string.Equals(driverId, _playerDriverId, StringComparison.Ordinal)
                || reason.Length == 0)
            {
                continue;
            }
            (causes ??= new Dictionary<string, string>(StringComparer.Ordinal))[driverId] = reason;
        }
        return causes;
    }

    /// <summary>The player's chosen accident severity, captured ONLY for the player's OWN accident ("a")
    /// DNF (Slice 2, §3.1). Null for every other outcome, so a non-accident round stores no severity and
    /// stays byte-identical. The value comes from the draft (the result screen defaults it to Medium when
    /// an accident is marked). Nothing folds it yet, Slice 3 rolls the d500 off it.</summary>
    private AccidentSeverity? PlayerAccidentSeverityFrom(ResultDraft draft) =>
        draft.DidNotFinish.TryGetValue(_playerDriverId, out string? reason) && reason == "a"
            ? draft.PlayerAccidentSeverity
            : null;

    /// <summary>Maps the result screen's reason for the PLAYER onto the sim's blame model:
    /// m(echanical) = no blame, a(ccident) = driver error, o(ther) = the fold's no-blame
    /// default UNLESS the custom detail was flagged as the driver's fault, in which case it is
    /// driver error. The custom free text does not change blame on its own, only the explicit
    /// attribution flag does, so "default custom-other = no-blame" holds (mandate M5 rule).</summary>
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
    /// season's age per the <see cref="ReplaySimInputs"/> contract, the season-end pipeline
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
            MasterySkills = rules.MasterySkills,
            DynastyEconomy = rules.DynastyEconomy,
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
        // A bankrupt team banks no title and rolls no offers (economy §7), the single chokepoint
        // that keeps every caller (Apply, AutoSimulateRound, SeasonReview) from resurrecting the
        // season end the fatal settlement suppressed. The bankruptcy takeover owns the ending.
        if (CurrentPlayerState()?.Economy?.Bankrupt == true)
            return;
        // Two-phase (3c-2): a promotion offer deferred by the final round MUST be resolved on the
        // screen BEFORE season end folds, replay resolves it inline (inside the round fold, ahead of
        // season end), so running season end now (old seat, wrong journal order) would diverge on
        // re-simulate. Hold season end until ResolveSmgpOffer clears the pending offer.
        if (CurrentSmgpPendingOffer() is not null)
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
            SeasonYear = _seasonYear,
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

    /// <summary>Accepts one offer (a player CHOICE, replay re-applies it, never derives it)
    /// and journals it as a provenance row so the replay byte-compare is unaffected.</summary>
    public void AcceptOffer(string teamId)
    {
        var offers = StateStore.ReadOffers(_database, _seasonId);
        if (!offers.Any(o => string.Equals(o.Terms.TeamId, teamId, StringComparison.Ordinal)))
            throw new InvalidOperationException(
                $"Team '{teamId}' made no offer this season, only extended offers can be accepted.");

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

    /// <summary>Returns the exact bounded v2 plan only when both sides of the locked gate are active.
    /// Per the compatibility contract, progression below v2 OR an absent mode takes the byte-stable
    /// legacy path. Once both are present, a missing/mismatched plan fails closed.</summary>
    private CampaignProgressionPlan? ActiveBoundedCampaign()
    {
        var player = CurrentPlayerState();
        int progressionVersion = player?.Character?.ProgressionVersion ?? 0;
        if (progressionVersion < CharacterLevelProgression.Level300Version ||
            player?.ExperienceMode is null)
        {
            return null;
        }
        if (progressionVersion != CharacterLevelProgression.Level300Version)
            throw new NotSupportedException(
                $"Character progression version {progressionVersion} is not supported by this build.");
        if (!CareerExperienceModes.IsBoundedCampaign(player.ExperienceMode))
            throw new InvalidOperationException(
                $"Experience mode '{player.ExperienceMode}' cannot run in a bounded career session.");

        var plan = player.CampaignProgressionPlan
            ?? throw new InvalidDataException("The v2 career has an experience mode but no campaign plan.");
        plan.Validate();
        if (!string.Equals(plan.Mode, player.ExperienceMode, StringComparison.Ordinal))
            throw new InvalidDataException("The stored experience mode and campaign-plan mode do not match.");
        if (_seasonOrdinal < 1 || _seasonOrdinal > plan.PinnedSeasonSequence.Count)
            throw new InvalidDataException(
                $"Season ordinal {_seasonOrdinal} is outside the campaign's {plan.TotalSeasons}-season plan.");

        var current = plan.PinnedSeasonSequence[_seasonOrdinal - 1];
        if (!string.Equals(current.PackId, Pack.Manifest.PackId, StringComparison.Ordinal) ||
            !string.Equals(current.PackVersion, Pack.Manifest.Version, StringComparison.Ordinal) ||
            current.Year != _seasonYear)
        {
            throw new InvalidDataException(
                "The open season does not match its pinned campaign-plan occurrence.");
        }
        _ = LoadPlannedPack(plan, current);
        return plan;
    }

    private SeasonPack LoadPlannedPack(
        CampaignProgressionPlan plan,
        PinnedCampaignSeason occurrence)
    {
        var pinned = CareerStore.ReadPinnedPack(
            _database, occurrence.PackId, occurrence.PackVersion);
        if (!string.Equals(pinned.Sha256, occurrence.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Campaign plan hash {occurrence.Sha256} does not match pinned pack hash {pinned.Sha256} " +
                $"for {occurrence.PackId} {occurrence.PackVersion}.");
        var pack = PinnedPackEnvelope.LoadSeasonPack(pinned.PackJson);
        if (!string.Equals(pack.Manifest.PackId, occurrence.PackId, StringComparison.Ordinal) ||
            !string.Equals(pack.Manifest.Version, occurrence.PackVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The pinned pack bytes do not match their campaign-plan identity.");
        }
        if (plan.Mode == CareerExperienceModes.GrandPrixDynasty && pack.Season.Year != occurrence.Year)
            throw new InvalidDataException("A Dynasty plan occurrence does not match its pinned pack year.");
        return pack;
    }

    /// <summary>The next season the career rolls into. Historical careers advance one calendar
    /// year at a time, changing only into ordinary historical packs. SMGP carries its pinned pack
    /// through campaign season 17 and then terminates. Null while the season is in progress or at
    /// the completed SMGP campaign summit.</summary>
    public NextSeasonInfo? NextSeason()
    {
        if (!SeasonComplete)
            return null;

        // A Racing Passport career IS one complete faithful season: there is no next season to
        // offer, no discovery, and no rollover target. The final review is the whole arc.
        if (CurrentPlayerState()?.ExperienceMode == CareerExperienceModes.RacingPassport)
            return null;

        if (ActiveBoundedCampaign() is { } campaign)
        {
            if (_seasonOrdinal >= campaign.PinnedSeasonSequence.Count)
                return null;

            var occurrence = campaign.PinnedSeasonSequence[_seasonOrdinal];
            var nextPack = LoadPlannedPack(campaign, occurrence);
            bool carryover = string.Equals(
                    occurrence.PackId, Pack.Manifest.PackId, StringComparison.Ordinal) &&
                string.Equals(
                    occurrence.PackVersion, Pack.Manifest.Version, StringComparison.Ordinal);
            return new NextSeasonInfo
            {
                IsCarryover = carryover,
                PackDirectory = "",
                PackId = occurrence.PackId,
                PackName = nextPack.Manifest.Name,
                SeasonYear = occurrence.Year,
                BridgedYears = occurrence.Year <= _seasonYear + 1
                    ? []
                    : Enumerable.Range(_seasonYear + 1, occurrence.Year - _seasonYear - 1).ToArray(),
            };
        }

        return PackDiscovery.PlanNextSeason(
            Pack.Manifest,
            _seasonYear,
            _seasonOrdinal,
            PackDiscovery.Discover(_environment.ResolvePackSearchRoots()));
    }

    /// <summary>Signs the accepted offer into the discovered next pack: builds the era
    /// transition plan from the persisted season-end states, the exact inputs replay's
    /// transition verification re-derives, so live and replay agree by construction, and
    /// starts the new season atomically via <see cref="CareerStore.StartNextSeason"/>.
    /// This session keeps pointing at the finished season afterwards; the shell reopens the
    /// career file, which now lands in the new season (<see cref="OpenCareer"/>).</summary>
    public void StartNextSeason(string teamId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamId);
        if (!SeasonComplete)
            throw new InvalidOperationException(
                "The season is not complete, finish every round before signing for the next era.");
        if (CurrentPlayerState()?.ExperienceMode == CareerExperienceModes.RacingPassport)
            throw new InvalidOperationException(
                "A Racing Passport career is one complete faithful season, there is no next season to sign for.");
        EnsureSeasonEnd();

        var campaign = ActiveBoundedCampaign();

        var next = NextSeason()
            ?? throw new InvalidOperationException(
                "This career has no next season, the campaign is complete.");

        var accepted = StateStore.ReadOffers(_database, _seasonId).FirstOrDefault(o => o.Accepted);
        if (accepted is null)
            throw new InvalidOperationException(
                "No offer is accepted, accept an offer letter before signing for the next season.");
        if (!string.Equals(accepted.Terms.TeamId, teamId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"The accepted offer is from '{accepted.Terms.TeamId}', not '{teamId}', " +
                "sign the offer you accepted.");

        var driversEnd = StateStore.ReadDriverStates(_database, _seasonId, StateStore.StageEnd);
        var teamsEnd = StateStore.ReadTeamStates(_database, _seasonId, StateStore.StageEnd);
        var playerEnd = StateStore.ReadPlayerState(_database, _seasonId, StateStore.StageEnd)
            ?? throw new InvalidOperationException(
                "The season-end player state is missing, the season-end pipeline never ran.");
        var simInputs = SimInputs();
        // The spends journaled this season develop the character as it rolls into next year —
        // applied identically on the live and replay paths so the evolving driver re-derives.
        var spends = ReplayService.ReadCharacterSpends(_database, _seasonId);
        var respecs = ReplayService.ReadCharacterRespecs(_database, _seasonId);
        var skillDevelopment = ReplayService.ReadCharacterSkillDevelopment(_database, _seasonId);

        if (next.IsCarryover)
        {
            // Same car, next year: seat the player at the accepted team's seat in THIS pack, then
            // roll the (already aged / retired / market-filled) end states forward. Same-pack, so
            // replay re-derives through SeasonRollover, byte-identical by construction.
            string? livery = EraTransition.ResolveSeatLivery(Pack, teamId) ?? playerEnd.LiveryName;
            var startStates = SeasonRollover.Derive(
                playerEnd, driversEnd, teamsEnd, teamId, livery,
                spends, simInputs.CharacterRules, respecs,
                skillPlans: null,
                masterySkills: simInputs.MasterySkills,
                skillDevelopment: skillDevelopment);
            CareerStore.StartCarryoverSeason(
                _database, startStates, next.SeasonYear,
                Pack.Manifest.PackId, Pack.Manifest.Version, NowUtc());
            return;
        }

        // A real era changeover into a later-year pack. V2 reads only the blob pre-pinned at
        // creation; legacy careers retain their existing discovery + disk-read path.
        PinnedCampaignSeason? plannedOccurrence = campaign is null
            ? null
            : campaign.PinnedSeasonSequence[_seasonOrdinal];
        var toPack = plannedOccurrence is null
            ? SeasonPackFiles.Read(next.PackDirectory).Parse()
            : LoadPlannedPack(campaign!, plannedOccurrence);
        var plan = EraTransition.Build(
            Pack, toPack, driversEnd, teamsEnd, playerEnd, accepted.Terms,
            new StreamFactory(MasterSeedU), _environment.Rules.AgingCurves,
            simInputs.CanonRetirements, spends, simInputs.CharacterRules,
            // Drive the boundary off the real SEASON years: after a carryover the finished season's
            // year runs ahead of this pack's nominal year, and the transition must start from it.
            fromYearOverride: _seasonYear, toYearOverride: next.SeasonYear,
            respecs: respecs,
            skillPlans: null,
            masterySkills: simInputs.MasterySkills,
            skillDevelopment: skillDevelopment);
        if (plan.ValidationErrors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", plan.ValidationErrors));

        if (plannedOccurrence is null)
        {
            CareerStore.StartNextSeason(_database, plan, toPack, NowUtc());
        }
        else
        {
            CareerStore.StartNextPinnedSeason(
                _database,
                plan,
                plannedOccurrence.PackVersion,
                plannedOccurrence.Sha256,
                NowUtc());
        }
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
    /// projection over the folded journal + standings snapshots, no new persistence, and the
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
        // nor the rules data, a quiet paddock does no work and forces no rules load.
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
            else if (string.Equals(row.Entity, "season", StringComparison.Ordinal)
                     && string.Equals(row.Cause, "season-digest", StringComparison.Ordinal))
            {
                // The season-in-review article: the season-digest headline (round-less) hangs a
                // period-voiced "champion crowned / final standing" body. Read-side only, the body
                // is derived deterministically from the seed + final standings, nothing is folded.
                snapshotsByOrdinal ??= AllSnapshots().ToDictionary(s => s.AfterRound);
                articles ??= _environment.Rules.NewsArticles;
                if (SeasonDigestFacts(snapshotsByOrdinal) is { } seasonFacts)
                    body = NewsArticleComposer.Compose(articles, seasonFacts, MasterSeedU, "season") ?? "";
            }

            dispatches.Add(new NewsDispatch
            {
                Headline = text,
                SeasonYear = _seasonYear,
                Round = row.Round,
                Kind = row.Round is null ? "season" : "race",
                WhyText = why,
                Body = body,
            });
        }

        dispatches.Reverse(); // newest first
        return dispatches;
    }

    /// <summary>The SMGP replica mode routes its news to the fictional-world "smgp" corpus (the
    /// SEGA universe, its own teams, the rival ladder, the D.P. readout) rather than the
    /// historical 1990s outlet the 1990 career year would otherwise select. Null for every other
    /// career, which resolves its era by year as before.</summary>
    private string? SmgpNewsEra => IsSmgpPack ? Companion.Core.Smgp.SmgpRules.CareerStyle : null;

    /// <summary>True for the SMGP replica mode (pack manifest careerStyle == "smgp"), gates the
    /// per-race skin activation, the rival panel, the world almanac, and the news outlet.</summary>
    private bool IsSmgpPack => string.Equals(
        Pack.Manifest.CareerStyle, Companion.Core.Smgp.SmgpRules.CareerStyle, StringComparison.Ordinal);

    /// <summary>Projects one race round's facts for the news grammar from already-folded state:
    /// the player's expected/actual finish + cause (the <c>race.result</c> row), the field's
    /// winner + size (the raw envelope), and the player's championship standing after the round
    /// (the snapshot for its championship ordinal). Every fact is optional, a template only
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
            // A malformed delta simply leaves the finish facts null, the body degrades.
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
            Year = _seasonYear,
            PreferredEra = SmgpNewsEra,
            Round = round,
            RaceName = packRound?.Name ?? grid.RoundName,
            PlayerName = PlayerDisplayName() ?? playerSeat?.DriverName ?? _playerDriverId,
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
    /// driver table then the raw id, so a name is always available for {winner}/{champLeader}.</summary>
    /// <summary>Projects the season's closing facts for the <c>season.digest</c> article from the
    /// final standings snapshot: who took the title, and where the player finished the year. The
    /// cause splits the corpus into a celebratory <c>player-champion</c> body and a reflective
    /// <c>season-complete</c> one. Only the season-neutral tokens are populated
    /// ({player} {team} {year} {champLeader} {champPosition}); race-only slots stay null so a
    /// season template that never names them still fills cleanly. Null when there is no scored
    /// standing yet (an unraced season carries no digest).</summary>
    private NewsFacts? SeasonDigestFacts(IReadOnlyDictionary<int, StandingsSnapshot> snapshotsByOrdinal)
    {
        if (snapshotsByOrdinal.Count == 0)
            return null;
        var final = snapshotsByOrdinal[snapshotsByOrdinal.Keys.Max()];
        var champion = final.Drivers.FirstOrDefault(d => d.Position == 1);
        if (champion is null)
            return null;

        var grid = ResolveGrid(Pack.Season.Rounds.Max(r => r.Round));
        var playerSeat = grid.Seats.FirstOrDefault(s =>
            string.Equals(s.DriverId, _playerDriverId, StringComparison.Ordinal));
        bool playerIsChampion = string.Equals(champion.DriverId, _playerDriverId, StringComparison.Ordinal);
        string playerName = PlayerDisplayName() ?? playerSeat?.DriverName ?? _playerDriverId;
        int? playerPosition = final.Drivers
            .FirstOrDefault(d => string.Equals(d.DriverId, _playerDriverId, StringComparison.Ordinal))
            ?.Position;

        return new NewsFacts
        {
            Phase = "season.digest",
            Cause = playerIsChampion ? "player-champion" : "season-complete",
            Year = _seasonYear,
            PreferredEra = SmgpNewsEra,
            Round = 0,
            PlayerName = playerName,
            TeamName = playerSeat?.TeamName ?? "",
            // {champLeader} is the crowned champion; when the player wins, it is the player's own name.
            ChampionshipLeaderName = playerIsChampion ? playerName : DriverDisplayName(grid, champion.DriverId),
            ChampionshipPosition = playerPosition,
            PlayerLeadsChampionship = playerIsChampion,
        };
    }

    private string DriverDisplayName(GridPlan grid, string driverId)
    {
        // The distinct-driver player (SMGP clean-swap) sits in a car whose seat still carries the BENCHED
        // occupant's authored name, and the synthetic id isn't in pack.Drivers, so resolving it the
        // normal way would credit the AI the player displaced (a player win reported under "Ayrton Senna").
        // Render the player's own name instead. Every real-driver id resolves exactly as before.
        if (string.Equals(driverId, RoundGridResolver.SyntheticPlayerDriverId, StringComparison.Ordinal))
            return PlayerDisplayName() ?? PlayerDefaultName;
        return grid.Seats.FirstOrDefault(s => string.Equals(s.DriverId, driverId, StringComparison.Ordinal))?.DriverName
            ?? Pack.Drivers.FirstOrDefault(d => string.Equals(d.Id, driverId, StringComparison.Ordinal))?.Name
            ?? driverId;
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
                return expected is { } exd ? $"You retired, the car was expected to finish P{exd}." : "You retired.";

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
    ///, no new persistence, and DETERMINISTIC: rows are read in journal <c>seq</c> order and every
    /// comparison is <see cref="StringComparer.Ordinal"/>, so the chain is byte-stable and identical
    /// on replay. The ordered-row shape is the format designed to accept perk/stat rows later
    /// (decision 5); it generalises the thin <see cref="WhyFromResult"/> chip into a full breakdown.</summary>
    public JournalChain JournalFor(string entity, int? round = null) =>
        BuildJournalChain(entity, round, _seasonId, Pack, _playerDriverId);

    /// <summary>The season-scoped walk (career-hub-design.md §4/§5, decision 18): resolves the season
    /// row whose year is <paramref name="seasonYear"/> and runs the SAME projection as the current-season
    /// <see cref="JournalFor(string,int?)"/> over THAT season's journal, resolving the entity name and
    /// player seat against that season's pinned pack, so a finished earlier season's numbers read
    /// correctly. When the year is the current season this is byte-identical to the current-season walk.
    /// No matching season year returns the empty chain (a graceful no-op, never a throw). A DISTINCT
    /// name (not a <see cref="JournalFor(string,int?)"/> overload) keeps int-literal round callers on
    /// the current-season walk.</summary>
    public JournalChain JournalForSeason(string entity, int seasonYear, int? round = null)
    {
        if (string.IsNullOrEmpty(entity))
            return JournalChain.Empty;

        // First (oldest) season row matching the year, years are unique per career in v1, but
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
    ///, no new persistence, and DETERMINISTIC: rows are read in journal <c>seq</c> order and every
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

        // Ordered by seq already (ReadSeason's ORDER BY seq), the deterministic walk order.
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
                        ? (expected is { } ex ? $"Retired, the car was expected to finish P{ex}." : "Retired.")
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
                            ? "Reputation moved this round, you beat your teammate."
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
                        ? $"Pace anchor recalibrated, next round's suggested Opponent Skill {s}%."
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
            return $"Why, {who}, {seasonPack.Season.Year}";

        // For a single-round player walk, lead with the finishing position if the race.result row
        // carries one, the number the user most likely clicked.
        var resultRow = rows.FirstOrDefault(x =>
            string.Equals(x.Phase, JournalPhases.RaceResult, StringComparison.Ordinal));
        if (resultRow is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(resultRow.DeltaJson);
                var el = doc.RootElement;
                if (el.TryGetProperty("dnf", out var d) && d.ValueKind == JsonValueKind.True)
                    return $"Why DNF, {who}, Round {r}";
                if (IntOrNull(el, "actualFinish") is { } actual)
                    return $"Why P{actual}, {who}, Round {r}";
            }
            catch (JsonException)
            {
                // Fall through to the plain round title.
            }
        }
        return $"Why, {who}, Round {r}";
    }

    /// <summary>Resolves a journal entity id to a display name: the player's own seat, else the
    /// season pack's driver or team table, else the raw id, so the inspector never shows a bare id.
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
    /// covers a player round, else empty, the rows carry the rest. Reuses the shipped
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
                    ? $"You retired, the car was expected to finish P{expected}."
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
    /// folded player states and journal every other lens reads, no new persistence, and
    /// re-derivable byte-identically. A multi-season career (M6 era transitions) walks every
    /// <c>season</c> row in career order, resolving each season's pinned pack, player seat,
    /// standings and headlines independently.</summary>
    private IHistoricalSeasonStore? _historicalSeasons;

    /// <summary>The real historical results for a season (f1db-derived, CC BY 4.0), loaded on demand
    /// from the app-shipped <c>data/history/&lt;year&gt;.json</c> and cached. Read-only reference
    /// content the History tab shows next to the player's own career, the sim/fold never reads it.</summary>
    public HistoricalSeason? HistoricalSeason(int year) =>
        (_historicalSeasons ??= new HistoricalSeasonStore(_environment.HistoryDirectory)).ForYear(year);

    /// <summary>The SMGP-universe "What Really Happened" almanac, the SEGA world's own legend of every
    /// circuit on THIS season's calendar (venue-keyed off <see cref="Pack"/>, so season 2+ variety still
    /// resolves each place), each unlocked once the player has raced the venue. A circuit reveals when
    /// its round position is at or below the current season's applied rounds, and every circuit stays
    /// unlocked once ANY season is complete (by then the player has raced them all). Read-only reference
    /// content, the sim/fold never reads it. Null for non-SMGP packs and when no almanac is shipped.</summary>
    public SmgpWorldHistory? SmgpWorldHistory()
    {
        if (!string.Equals(Pack.Manifest.CareerStyle, Companion.Core.Smgp.SmgpRules.CareerStyle, StringComparison.Ordinal))
            return null;
        var almanac = _environment.Rules.SmgpWhatReallyHappened;
        if (almanac.Venues.Count == 0)
            return null; // no data shipped → hide the panel entirely (never render 16 empty rows)

        // A circuit unlocks once the player has raced it. Within the current season that is its round
        // position <= applied rounds; once any season is complete every venue has been visited, so the
        // whole almanac stays unlocked forever after (a permanent world reference from season 2 on).
        bool allRevealed = CareerStore.ReadSeasons(_database)
            .Any(s => string.Equals(s.Status, SeasonStatus.Complete, StringComparison.Ordinal));
        int roundsApplied = ResultStore.ReadSeasonResults(_database, _seasonId)
            .Count(r => Pack.Season.Rounds.Any(round => round.Round == r.Round && round.Championship));

        var races = Pack.Season.Rounds
            .Select(round =>
            {
                var lore = almanac.ForVenue(round.Name);
                return new SmgpWorldRace
                {
                    Round = round.Round,
                    VenueName = round.Name,
                    IsRevealed = allRevealed || round.Round <= roundsApplied,
                    Title = lore?.Title ?? "",
                    Circuit = lore?.Circuit ?? "",
                    Champion = lore?.Champion ?? "",
                    Body = lore?.Body ?? [],
                    Notes = lore?.Notes ?? [],
                };
            })
            .ToList();
        return new SmgpWorldHistory { Races = races };
    }

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
            var venueByRound = seasonPack.Season.Rounds.ToDictionary(r => r.Round, VenueLabel);

            var storedResults = ResultStore.ReadSeasonResults(_database, season.Id)
                .Where(r => seasonPack.Season.Rounds
                    .FirstOrDefault(round => round.Round == r.Round)?.Championship ?? false)
                .ToList();
            int roundsApplied = storedResults.Count;
            int championshipRounds = seasonPack.Season.Rounds.Count(r => r.Championship);

            // Per-race player classification (finishing position + status), the records-book
            // source. Ordered by round so streaks are chronological across seasons.
            foreach (var stored in storedResults.OrderBy(r => r.Round))
            {
                var race = stored.ToRoundResult().Sessions
                    .FirstOrDefault(s => s.Kind == SessionKind.Race);
                var line = race?.Entries.FirstOrDefault(e => string.Equals(e.DriverId, playerDriverId, StringComparison.Ordinal));
                races.Add(new PlayerRace(
                    line is { Status: FinishStatus.Classified, Position: { } p } ? p : null));
            }

            // The per-round standings snapshots (championship rounds in order), reused for both the final
            // standings AND the per-round breakdown (Task 3.3), so there is only ONE scoring pass.
            var orderedStored = storedResults.OrderBy(r => r.Round).ToList();
            IReadOnlyList<StandingsSnapshot> snapshots = orderedStored.Count == 0
                ? []
                : StandingsEngine.ComputeSeason(scoring, orderedStored.Select(r => r.ToRoundResult()).ToList()).Snapshots;
            StandingsSnapshot? finalSnapshot = snapshots.Count > 0 ? snapshots[^1] : null;

            // The player's own per-round story this season: their finish, the rival they NAMED that round +
            // how the duel went, the leader after the round and the player's running points.
            var roundLines = new List<CareerSeasonRoundLine>(orderedStored.Count);
            for (int i = 0; i < orderedStored.Count; i++)
            {
                var stored = orderedStored[i];
                var envelope = stored.ToEnvelope();
                var race = envelope.Result.Sessions.FirstOrDefault(s => s.Kind == SessionKind.Race);
                var entries = race?.Entries ?? [];
                string? rivalId = envelope.SmgpRival?.RivalDriverId;
                var snap = i < snapshots.Count ? snapshots[i] : finalSnapshot;
                var leader = snap?.Drivers.FirstOrDefault(d => d.Position == 1);
                var playerRow = snap?.Drivers.FirstOrDefault(d => string.Equals(d.DriverId, playerDriverId, StringComparison.Ordinal));
                roundLines.Add(new CareerSeasonRoundLine
                {
                    Round = stored.Round,
                    Venue = venueByRound.GetValueOrDefault(stored.Round) ?? $"Round {stored.Round}",
                    PlayerFinish = FinishPosition(entries, playerDriverId),
                    RivalName = rivalId is null ? null : driverNames.GetValueOrDefault(rivalId, rivalId),
                    RivalFinish = rivalId is null ? null : FinishPosition(entries, rivalId),
                    ChampionAfter = leader is null ? null : driverNames.GetValueOrDefault(leader.DriverId, leader.DriverId),
                    PlayerPointsAfter = playerRow?.CountedPoints.ToDouble() ?? 0.0,
                });
            }
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
            var startState = StateStore.ReadPlayerState(_database, season.Id, StateStore.StageStart);

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
                RoundLines = roundLines,
                PlayerLevelAtStart = startState?.HasCharacter == true ? startState.Level : null,
                PlayerLevelAtEnd = endState?.HasCharacter == true ? endState.Level : null,
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
        var teamByDriver = ResolveGrid(roundNumber).Seats
            .ToDictionary(s => s.DriverId, s => s.TeamId, StringComparer.Ordinal);

        // Race 0 is the draft's own classification; a two-race weekend adds the rest (Increment 2).
        int raceCount = 1 + (draft.AdditionalRaces?.Count ?? 0);
        bool perSession = raceCount > 1;
        var weekendRaces = packRound.Weekend?.Races;

        var sessions = new List<SessionResult>(raceCount)
        {
            // Bind a per-session table ONLY when the round actually scores per session, a
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
    /// weekend (<c>null</c> when the round runs no weekend or leaves the race's table unset, then
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
        var gridDrivers = ResolveGrid(roundNumber).Seats
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

    /// <summary>Every distinct driver named across all of the round's races, one entry per driver
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

    // ---------- character death & injury: mortality mode + Normal save/reload (Slice 1) ----------

    /// <inheritdoc />
    public MortalityMode Mortality => _mortality;

    /// <inheritdoc />
    public bool SavesEnabled => _mortality == MortalityMode.Normal;

    /// <inheritdoc />
    public PlayerMortalityStatus PlayerMortality()
    {
        // After a Hardcore death the file is gone, report the terminal status WITHOUT touching the DB.
        if (_careerFileDeleted)
            return new PlayerMortalityStatus
            {
                Mode = _mortality,
                Deceased = true,
                SeasonEndingInjury = false,
                RaceSuspensionRemaining = 0,
                CareerFileDeleted = true,
            };

        var player = CurrentPlayerState();
        return new PlayerMortalityStatus
        {
            Mode = _mortality,
            Deceased = player?.Deceased ?? false,
            SeasonEndingInjury = player?.SeasonEndingInjury ?? false,
            RaceSuspensionRemaining = player?.RaceSuspensionRemaining ?? 0,
            CareerFileDeleted = false,
        };
    }

    /// <inheritdoc />
    public DeathScreenModel? DeathScreen()
    {
        // Hardcore permadeath: the file is gone, return the snapshot captured just before deletion. DB-free
        // by construction, mirroring the PlayerMortality guard (the shell must not touch the DB here).
        if (_careerFileDeleted)
            return _deathScreen;

        var player = CurrentPlayerState();
        if (player?.Deceased != true)
            return null;
        // Normal (or a not-yet-deleted) death: build live from the intact DB, this also covers reopening a
        // dead-but-restorable Normal career. Memoised: a dead career takes no more rounds, so it never changes.
        return _deathScreen ??= BuildDeathScreen(player);
    }

    /// <summary>Assemble the death-screen model from the folded journal + state: the fatal
    /// <c>player.accident</c> row (severity + venue), the whole career record, and (Normal) the restore
    /// slots. A pure read, no fold change, no new persistence, so replay stays byte-identical. Called with
    /// the DB still open (live, or on the Hardcore path just before the file is deleted).</summary>
    private DeathScreenModel BuildDeathScreen(PlayerCareerState player)
    {
        AccidentSeverity? severity = null;
        int? fatalRound = null;
        string? venue = null;

        // The fatal accident is always in the current season (a death is terminal, the career never advances
        // past it). Take the last death-outcome player.accident row, mirroring the headline read-back pattern.
        var fatal = JournalStore.ReadSeason(_database, _seasonId)
            .LastOrDefault(r =>
                string.Equals(r.Phase, JournalPhases.PlayerAccident, StringComparison.Ordinal) &&
                IsFatalAccidentDelta(r.DeltaJson));
        if (fatal is not null)
        {
            fatalRound = fatal.Round;
            severity = AccidentSeverityFromDelta(fatal.DeltaJson);
            if (fatal.Round is { } rn)
                venue = VenueForRound(rn);
        }

        var timeline = CareerTimeline();
        string name = player.Character?.Name is { Length: > 0 } n ? n : PlayerDisplayName() ?? "The driver";
        int? age = player.Character?.Age is { } startAge ? startAge + (_seasonYear - _firstSeasonYear) : null;

        return DeathScreenModel.Build(
            _mortality, name, age, severity, venue, fatalRound,
            timeline.Records, timeline.Seasons, SaveSlots());
    }

    /// <inheritdoc />
    public BankruptcyScreenModel? BankruptcyScreen()
    {
        if (_careerFileDeleted)
            return null; // a Hardcore death outranks the ledger, the death screen owns the ending
        var player = CurrentPlayerState();
        if (player?.Economy?.Bankrupt != true)
            return null;
        // Memoised: a bankrupt career takes no more rounds, so it never changes.
        return _bankruptcyScreen ??= BuildBankruptcyScreen(player);
    }

    /// <summary>Assemble the bankruptcy-screen model from the folded journal + state: the terminal
    /// <c>economy.bankruptcy</c> row (round, final balance, the grace that ran out), the whole
    /// career record, and any restore slots. A pure read, no fold change, no new persistence,
    /// so replay stays byte-identical. Mirrors <see cref="BuildDeathScreen"/>.</summary>
    private BankruptcyScreenModel BuildBankruptcyScreen(PlayerCareerState player)
    {
        int? round = null;
        string balance = player.Economy!.Balance.ToString();
        int deficitRounds = player.Economy.DeficitRounds;
        int graceRounds = 0;
        var terminal = JournalStore.ReadSeason(_database, _seasonId)
            .LastOrDefault(r => string.Equals(
                r.Phase, JournalPhases.EconomyBankruptcy, StringComparison.Ordinal));
        if (terminal is not null)
        {
            round = terminal.Round;
            try
            {
                using var doc = JsonDocument.Parse(terminal.DeltaJson);
                if (doc.RootElement.TryGetProperty("balance", out var b) && b.ValueKind == JsonValueKind.String)
                    balance = b.GetString() ?? balance;
                if (doc.RootElement.TryGetProperty("deficitRounds", out var d) && d.ValueKind == JsonValueKind.Number)
                    deficitRounds = d.GetInt32();
                if (doc.RootElement.TryGetProperty("graceRounds", out var g) && g.ValueKind == JsonValueKind.Number)
                    graceRounds = g.GetInt32();
            }
            catch (JsonException) { }
        }

        var timeline = CareerTimeline();
        string name = player.Character?.Name is { Length: > 0 } n ? n : PlayerDisplayName() ?? "The owner-driver";
        string teamName = Pack.Teams
            .FirstOrDefault(t => string.Equals(t.Id, player.CurrentTeamId, StringComparison.Ordinal))
            ?.Name ?? player.CurrentTeamId ?? "the team";

        return new BankruptcyScreenModel
        {
            DriverName = name,
            TeamName = teamName,
            Year = _seasonYear,
            Round = round,
            FinalBalance = balance,
            DeficitRounds = deficitRounds,
            GraceRounds = graceRounds,
            Record = timeline.Records,
            Seasons = timeline.Seasons,
            RestoreSlots = SaveSlots(),
        };
    }

    /// <summary>The venue label for a round of the CURRENT season pack, the real historical venue when the
    /// track carries one, else the round's display name.</summary>
    private string? VenueForRound(int round)
    {
        var packRound = Pack.Season.Rounds.FirstOrDefault(r => r.Round == round);
        if (packRound is null)
            return null;
        return packRound.Track.RealVenue is { Length: > 0 } realVenue ? realVenue : packRound.Name;
    }

    /// <summary>True when a <c>player.accident</c> delta records a fatal (death) outcome.</summary>
    private static bool IsFatalAccidentDelta(string deltaJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(deltaJson);
            return doc.RootElement.TryGetProperty("outcome", out var o) &&
                o.ValueKind == JsonValueKind.String &&
                string.Equals(o.GetString(), "death", StringComparison.Ordinal);
        }
        catch (JsonException) { return false; }
    }

    /// <summary>Read the accident severity (camelCase enum string) back off a <c>player.accident</c> delta.</summary>
    private static AccidentSeverity? AccidentSeverityFromDelta(string deltaJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(deltaJson);
            if (doc.RootElement.TryGetProperty("severity", out var s) && s.ValueKind == JsonValueKind.String &&
                Enum.TryParse<AccidentSeverity>(s.GetString(), ignoreCase: true, out var severity))
                return severity;
        }
        catch (JsonException) { }
        return null;
    }

    /// <inheritdoc />
    public SitOutStatus? CurrentSitOut()
    {
        if (_careerFileDeleted || SeasonComplete)
            return null;
        var player = CurrentPlayerState();
        if (player is null || player.Deceased)
            return null;
        if (player.SeasonEndingInjury)
            return new SitOutStatus
            {
                RaceSuspensionRemaining = 0,
                SeasonEnding = true,
                Headline = "SEASON OVER, recovering",
            };
        if (player.RaceSuspensionRemaining > 0)
            return new SitOutStatus
            {
                RaceSuspensionRemaining = player.RaceSuspensionRemaining,
                SeasonEnding = false,
                Headline = $"INJURED, auto-simulating round ({player.RaceSuspensionRemaining} remaining)",
            };
        return null;
    }

    /// <inheritdoc />
    public RoundProgressionSummary? RoundProgression(int round)
    {
        if (CurrentPlayerState()?.HasCharacter != true)
            return null;

        // Walk this season's journaled player.xp rows in fold order: the level BEFORE the round is
        // the last journaled level ahead of it (or the season-start snapshot), the movement is the
        // row's own from→to, the fold's audit record, never recomputed.
        var seasonStart = StateStore.ReadPlayerState(_database, _seasonId, StateStore.StageStart);
        int previousLevel = seasonStart?.Level > 0 ? seasonStart.Level : 1;
        foreach (var row in JournalStore.ReadSeason(_database, _seasonId))
        {
            if (!string.Equals(row.Phase, JournalPhases.PlayerXp, StringComparison.Ordinal)
                || row.Round is not { } xpRound)
            {
                continue;
            }

            int levelAfter = ReadIntProperty(row.DeltaJson, "level");
            if (xpRound == round)
            {
                long from = ReadLongProperty(row.DeltaJson, "from");
                long to = ReadLongProperty(row.DeltaJson, "to");
                return new RoundProgressionSummary
                {
                    Round = round,
                    XpGained = to - from,
                    LevelBefore = previousLevel,
                    LevelAfter = levelAfter > 0 ? levelAfter : previousLevel,
                    SkillPointsAvailable = CharacterDossier()?.CpUnspent ?? 0,
                };
            }

            if (levelAfter > 0)
                previousLevel = levelAfter;
        }

        return null;
    }

    /// <inheritdoc />
    public IReadOnlyList<CampaignTimelineEntry> CampaignTimeline()
    {
        var cards = CareerTimeline().Seasons;
        var plan = CurrentPlayerState()?.CampaignProgressionPlan;
        int horizon = IsSmgpPack
            ? Companion.Core.Smgp.SmgpRules.CampaignSeasons
            : plan?.TotalSeasons ?? cards.Count;
        horizon = Math.Max(horizon, cards.Count);
        if (horizon == 0)
            return [];

        // Sat-out rounds per season come from the (cached) event spine, the timeline never
        // rewrites an injury absence as participation.
        var satOutOrdinals = NewsroomEvents()
            .Where(e => e.Kind == Companion.Core.Newsroom.NewsEventKind.SatOutRound)
            .Select(e => e.SeasonOrdinal)
            .ToHashSet();

        // A season's authored identity (title/era) is revealed ONLY once it is reached: the current
        // season and completed ones carry it, LOCKED future seasons carry NOTHING, no title, no
        // era, no year, so no surface can spoil what is coming. The campaign is never previewed
        // ahead of the player; they meet each season when they arrive at it.
        var lore = IsSmgpPack ? _environment.Rules?.SmgpSeasonLore : null;

        // Dynasty is the deliberate opposite of the SMGP spoiler rule: historical years are
        // real-world known, so a LOCKED future season carries its pack-level preview (year, era,
        // series, venues, teams). The gate is about playing in order, not hiding (Piece 1).
        bool isDynasty = plan?.Mode == CareerExperienceModes.GrandPrixDynasty;
        IReadOnlyList<CampaignSeasonPreview>? previews =
            isDynasty ? DynastySeasonPreviews(plan!) : null;

        var entries = new List<CampaignTimelineEntry>(horizon);
        for (int ordinal = 1; ordinal <= horizon; ordinal++)
        {
            if (ordinal <= cards.Count)
            {
                var card = cards[ordinal - 1];
                var seasonLore = lore?.ForOrdinal(ordinal);
                entries.Add(new CampaignTimelineEntry
                {
                    Ordinal = ordinal,
                    State = card.IsComplete ? CampaignSeasonState.Completed : CampaignSeasonState.Current,
                    Year = card.SeasonYear,
                    Title = seasonLore?.Title ?? "",
                    Era = seasonLore?.Era ?? "",
                    PlayerPosition = card.PlayerPosition,
                    PlayerChampion = card.PlayerIsChampion,
                    MissedRounds = satOutOrdinals.Contains(ordinal),
                });
            }
            else
            {
                // Locked/future season. SMGP: an anonymous placeholder only, its identity stays
                // hidden until the player reaches it (no title, era, or year leaks the future).
                // Dynasty: the pack-level preview shows what history already knows is coming.
                entries.Add(new CampaignTimelineEntry
                {
                    Ordinal = ordinal,
                    State = CampaignSeasonState.Locked,
                    Preview = previews is not null && ordinal - 1 < previews.Count
                        ? previews[ordinal - 1]
                        : null,
                });
            }
        }

        // The Formula Junior prologue slot heads a Dynasty timeline that starts at the beginning
        // of the playable timeline: a labelled pre-championship stretch, shown as coming-soon
        // until content exists, so the timeline starts where the story should even though play
        // starts at 1967 (Piece 1). Synthetic ordinal 0; never playable.
        if (isDynasty && plan!.StartYear <= 1967)
        {
            entries.Insert(0, new CampaignTimelineEntry
            {
                Ordinal = 0,
                State = CampaignSeasonState.Locked,
                Title = "Formula Junior → 1967",
                Era = "Pre-championship prologue, coming soon",
                IsPrologue = true,
            });
        }

        return entries;
    }

    private IReadOnlyList<CampaignSeasonPreview>? _dynastySeasonPreviews;

    /// <summary>The pack-level previews of a Dynasty plan's pinned seasons, built once per
    /// session from the pre-pinned bytes (immutable by construction, so no invalidation is
    /// needed). Display-only; the previews never become a play path.</summary>
    private IReadOnlyList<CampaignSeasonPreview> DynastySeasonPreviews(CampaignProgressionPlan plan)
    {
        if (_dynastySeasonPreviews is { } cached && cached.Count == plan.TotalSeasons)
            return cached;

        var previews = new List<CampaignSeasonPreview>(plan.TotalSeasons);
        foreach (var occurrence in plan.PinnedSeasonSequence)
        {
            var pack = LoadPlannedPack(plan, occurrence);
            var rounds = pack.Season.Rounds.Where(r => r.Championship).ToArray();
            previews.Add(new CampaignSeasonPreview
            {
                Year = pack.Season.Year,
                SeriesName = pack.Season.SeriesName,
                EraLabel = $"{pack.Season.Year / 10 * 10}s",
                RoundCount = rounds.Length,
                Venues = rounds.Select(r => r.Track.RealVenue ?? r.Name).ToArray(),
                Teams = pack.Teams.Select(t => t.Name).ToArray(),
            });
        }

        _dynastySeasonPreviews = previews;
        return previews;
    }

    /// <inheritdoc />
    public Companion.Core.Smgp.SmgpSeasonLoreEntry? CurrentSeasonLore()
    {
        if (!IsSmgpPack || _environment.Rules?.SmgpSeasonLore is not { IsEmpty: false } lore)
            return null;
        // Reconcile the authored lore with the player's REAL seat: fill {playerTeam} with the team
        // they are actually on, then drop any line narrating the driver they replaced (that driver
        // is benched, off the grid, so the lore must not show them racing). Both are per-career
        // display projections; the authored lore is untouched.
        return lore.ForOrdinal(CareerStore.ReadSeasons(_database).Count)
            ?.WithPlayerTeam(CurrentPlayerTeamName())
            .WithoutReplacedDriver(ReplacedDriverSurname());
    }

    /// <inheritdoc />
    public Companion.Core.Career.EraThemeCatalog? EraThemeOverrides() =>
        _environment.RulesDirectory is null ? null : _environment.Rules.EraThemes;

    /// <summary>The surname of the canon driver whose car the player currently occupies (they were
    /// benched when the player took the seat), or null when the seat maps to no authored driver.
    /// Used to keep the season lore from narrating a driver the player replaced.</summary>
    private string? ReplacedDriverSurname()
    {
        string? livery = CurrentSmgpState()?.CurrentSeatLivery;
        if (string.IsNullOrEmpty(livery))
            return null;
        var entry = Pack.Entries.FirstOrDefault(e =>
            string.Equals(e.Ams2LiveryName, livery, StringComparison.Ordinal));
        if (entry is null)
            return null;
        string? name = Pack.Drivers
            .FirstOrDefault(d => string.Equals(d.Id, entry.DriverId, StringComparison.Ordinal))?.Name;
        if (string.IsNullOrWhiteSpace(name))
            return null;
        // "Peter Klinger" → "Klinger"; matching by surname covers "Klinger" / "P. Klinger" / the full name.
        int space = name.LastIndexOf(' ');
        return space >= 0 ? name[(space + 1)..] : name;
    }

    /// <inheritdoc />
    public IReadOnlyList<InjuryHistoryEntry> InjuryHistory()
    {
        var entries = new List<InjuryHistoryEntry>();
        int ordinal = 0;
        foreach (var season in CareerStore.ReadSeasons(_database))
        {
            ordinal++;
            foreach (var row in JournalStore.ReadSeason(_database, season.Id))
            {
                if (!string.Equals(row.Phase, JournalPhases.PlayerAccident, StringComparison.Ordinal)
                    || row.Round is not { } round)
                {
                    continue;
                }

                string outcome = ReadStringProperty(row.DeltaJson, "outcome");
                if (outcome is not ("minorInjury" or "seasonEnding" or "death"))
                {
                    continue;
                }

                int miss = ReadIntProperty(row.DeltaJson, "missRaces");
                entries.Add(new InjuryHistoryEntry
                {
                    SeasonOrdinal = ordinal,
                    SeasonYear = season.Year,
                    Round = round,
                    Outcome = outcome,
                    MissRaces = miss,
                    Label = outcome switch
                    {
                        "minorInjury" => miss == 1 ? "Injured, missed 1 race" : $"Injured, missed {miss} races",
                        "seasonEnding" => "Season-ending injury",
                        _ => "Fatal accident",
                    },
                    Description = InjuryFlavor.Describe(outcome, ordinal, round),
                });
            }
        }

        return entries;
    }

    /// <inheritdoc />
    public void AutoSimulateRound()
    {
        if (_careerFileDeleted)
            throw new InvalidOperationException("This career has ended, the driver was killed (Hardcore).");
        var player = CurrentPlayerState();
        if (player?.Deceased == true)
            throw new InvalidOperationException("The driver has died, the career is over.");
        if (player?.Smgp?.CareerOver == true)
            throw new InvalidOperationException(
                "The SMGP career is over, a rival took the last seat at the LEVEL D floor.");
        // The Dynasty bankruptcy floor is terminal here too, the third of the three guard sites.
        if (player?.Economy?.Bankrupt == true)
            throw new InvalidOperationException(
                "The team is bankrupt, the Dynasty is over. Restore a save to continue, if one exists.");
        if (SeasonComplete)
            throw new InvalidOperationException("The season is complete, there is no round to simulate.");
        if (player is null || (player.RaceSuspensionRemaining == 0 && !player.SeasonEndingInjury))
            throw new InvalidOperationException("The driver is fit, enter this round's result manually.");

        var beforeEconomy = player?.Economy;
        int roundNumber = CurrentRoundNumber;
        var packRound = RoundByNumber(roundNumber);
        // The AI field races the round the injured player sits out, a deterministic classification with
        // the player EXCLUDED (DNS). The order is generated now and STORED (so the championship advances
        // for the AI); the fold reads the PlayerDidNotStart flag to keep the player OPI-neutral + heal.
        var aiOrder = AutoRaceModel.ClassifiedOrder(
            ResolveGrid(roundNumber, applyPlayerCharacter: false).Seats,
            MasterSeedU, Pack.Season.Year, roundNumber);
        var draft = new ResultDraft
        {
            Classified = aiOrder,
            DidNotFinish = new Dictionary<string, string>(StringComparer.Ordinal),
            Disqualified = [],
        };
        var envelope = new RoundResultEnvelope
        {
            Result = ToRoundResult(draft, roundNumber, packRound),
            PlayerDidNotStart = true,
            SliderUsed = ReplayService.NeutralSlider,
        };
        string nowUtc = NowUtc();
        // The provenance row joins the fold's atomic transaction (same contract as Apply): a
        // crash can never leave an auto-simulated round without its journal-feed dispatch row.
        ReplayService.ImportAndFoldRound(
            _database, _seasonId, Pack, MasterSeedU, SimInputs(), roundNumber, envelope, nowUtc,
            withTransaction: transaction => JournalStore.Append(_database, _seasonId, roundNumber,
                new JournalEvent
                {
                    Phase = DataJournalPhases.ResultProvenance,
                    Entity = "round",
                    DeltaJson = JsonSerializer.Serialize(
                        new ResultAppliedDelta
                        {
                            Round = roundNumber,
                            RoundName = packRound.Name,
                            WinnerDriverId = aiOrder.Count > 0 ? aiOrder[0] : null,
                            ClassifiedCount = aiOrder.Count,
                            DnfCount = 0,
                            DsqCount = 0,
                        },
                        CoreJson.Options),
                    Cause = "auto-simulated",
                },
                nowUtc,
                transaction));

        // The sat-out round still settles the books (SettleEconomy runs on the DNS path), so it CAN
        // fold the team, a Dynasty owner earning nothing while injured marches into the deficit
        // floor. Suppress the season end on that fatal settlement exactly as Apply does (a folded
        // team banks no title and rolls no offers); the bankruptcy takeover then owns the ending.
        bool justWentBankrupt = beforeEconomy?.Bankrupt != true
            && CurrentPlayerState()?.Economy?.Bankrupt == true;
        if (SeasonComplete && !justWentBankrupt)
            EnsureSeasonEnd();
    }

    /// <inheritdoc />
    public IReadOnlyList<SaveSlotInfo> SaveSlots() =>
        SavesEnabled ? SaveSlotStore.List(CareerFilePath) : [];

    /// <inheritdoc />
    public SaveSlotInfo SaveToSlot(string label)
    {
        if (!SavesEnabled)
            throw new InvalidOperationException(
                $"Saving is available only in Normal mode, this career is {_mortality}.");

        string chosenLabel = string.IsNullOrWhiteSpace(label)
            ? $"Season {_seasonYear} · Round {CurrentRoundNumber}"
            : label.Trim();
        // Snapshot from the LIVE connection so committed WAL data is captured too.
        return SaveSlotStore.Save(
            _database.Connection, CareerFilePath, NextManualSlotId(), chosenLabel,
            _seasonYear, CurrentRoundNumber, NowUtc(), isAutosave: false);
    }

    /// <inheritdoc />
    public void RestoreSlot(string slotId)
    {
        if (!SavesEnabled)
            throw new InvalidOperationException(
                $"Restoring is available only in Normal mode, this career is {_mortality}.");
        // Validate the target BEFORE tearing down the live DB, so an unknown/stale slot id fails cleanly
        // without spending the session (matching the disabled-mode guard's fail-without-side-effects).
        if (!SaveSlotStore.SnapshotExists(CareerFilePath, slotId))
            throw new InvalidOperationException($"Save slot '{slotId}' has no snapshot to restore.");

        // Release the working file (Dispose clears the pool, so the file becomes replaceable), then
        // swap the snapshot in. THIS SESSION IS SPENT afterwards, the shell must reopen the career
        // file to land at the restored point (the same reopen contract as an era transition).
        _database.Dispose();
        SaveSlotStore.Restore(CareerFilePath, slotId);
    }

    /// <inheritdoc />
    public void DeleteSlot(string slotId)
    {
        if (!SavesEnabled)
            return;
        SaveSlotStore.Delete(CareerFilePath, slotId);
    }

    /// <summary>Best-effort autosave of a FRESH season's start (Normal only): snapshots the season
    /// start point exactly once, so a death always has a recent restore point (character-death plan
    /// §4). Gated on Normal mode + zero applied rounds this season + no existing autosave for it, so a
    /// mid-season or non-Normal open is a no-op and reopening never duplicates. A snapshot hiccup must
    /// never break creating/opening a career, so failures are swallowed.</summary>
    private void TryAutosaveSeasonStart()
    {
        if (_mortality != MortalityMode.Normal || MaxAppliedRound > 0)
            return;

        string slotId = $"autosave-season-{_seasonOrdinal}";
        try
        {
            if (SaveSlotStore.List(CareerFilePath).Any(s => string.Equals(s.SlotId, slotId, StringComparison.Ordinal)))
                return;
            SaveSlotStore.Save(
                _database.Connection, CareerFilePath, slotId,
                $"Season {_seasonYear} start", _seasonYear, CurrentRoundNumber, NowUtc(),
                isAutosave: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Autosave is best-effort, never let a snapshot failure abort create/open.
        }
    }

    /// <summary>The next free <c>manual-NNN</c> slot id (a stable, deterministic ordinal, no clock or
    /// RNG in the id, so tests are reproducible).</summary>
    private string NextManualSlotId()
    {
        int max = 0;
        foreach (var slot in SaveSlotStore.List(CareerFilePath))
        {
            const string prefix = "manual-";
            if (!slot.IsAutosave &&
                slot.SlotId.StartsWith(prefix, StringComparison.Ordinal) &&
                int.TryParse(slot.SlotId.AsSpan(prefix.Length), out int n))
            {
                max = Math.Max(max, n);
            }
        }
        return $"manual-{max + 1:D3}";
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
