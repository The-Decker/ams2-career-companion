using System.Text.Json;
using Companion.Ams2;
using Companion.Ams2.ContentLibrary;
using Companion.Ams2.Skins;
using Companion.Core.Character;
using Companion.Core.Grid;
using Companion.Core.Json;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>Shared fixtures for the session/wizard/briefing viewmodel tests: the REAL
/// extracted content library and f1-1967 pack from test output, environment factories, and
/// an in-memory minimal library + pack builder for controlled failure cases.</summary>
internal static class ViewModelTestData
{
    public static readonly Lazy<Ams2ContentLibrary> RealLibrary = new(() =>
        Ams2ContentLibrary.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ams2")));

    public static string RealPackDirectory =>
        Path.Combine(AppContext.BaseDirectory, "packs", "f1-1967");

    public static SeasonPack RealPack() => SeasonPackFiles.Read(RealPackDirectory).Parse();

    /// <summary>The real career rules data (aging curves, archetypes, headlines) copied into
    /// the test output — the same files the published exe ships beside itself.</summary>
    public static string RulesDirectory => Path.Combine(AppContext.BaseDirectory, "Fixtures", "rules");

    public static CareerEnvironment Environment(
        string documentsDirectory,
        string? installDirectory = null,
        Ams2ContentLibrary? library = null) => new()
    {
        ContentLibrary = library ?? RealLibrary.Value,
        LocateInstall = () => installDirectory is null
            ? null
            : new Ams2Installation { InstallDirectory = installDirectory },
        DocumentsDirectory = documentsDirectory,
        RulesDirectory = RulesDirectory,
    };

    /// <summary>A library that knows nothing — every class/track/vehicle check fails.</summary>
    public static Ams2ContentLibrary EmptyLibrary() => new()
    {
        ExtractedFrom = "in-memory empty test library",
        Classes = new Dictionary<string, Ams2Class>(StringComparer.Ordinal),
        Vehicles = new Dictionary<string, Ams2Vehicle>(StringComparer.Ordinal),
        Tracks = new Dictionary<string, Ams2Track>(StringComparer.Ordinal),
        Liveries = new Dictionary<string, Ams2LiveryClassEntry>(StringComparer.Ordinal),
    };
}

/// <summary>Builds a minimal valid two-round season pack (and its on-disk five-file form)
/// plus a matching in-memory content library, for wizard-gating tests.</summary>
internal static class TestPackBuilder
{
    public const string VintageClass = "F-Vintage_Gen1";
    public const string VintageCar = "formula_vintage_g1m2";
    public const string Track = "kyalami_historic";
    public const string StockLivery1 = "Stock Livery #1";
    public const string StockLivery2 = "Stock Livery #2";

    public static Ams2ContentLibrary Library() => new()
    {
        ExtractedFrom = "in-memory test library",
        Classes = new Dictionary<string, Ams2Class>(StringComparer.Ordinal)
        {
            [VintageClass] = new() { XmlName = VintageClass, Vehicles = [VintageCar] },
        },
        Vehicles = new Dictionary<string, Ams2Vehicle>(StringComparer.Ordinal)
        {
            [VintageCar] = new() { Id = VintageCar, Dir = VintageCar, VehicleClass = VintageClass },
        },
        Tracks = new Dictionary<string, Ams2Track>(StringComparer.Ordinal)
        {
            [Track] = new() { Id = Track, TrackName = "Kyalami Historic", MaxAiParticipants = 20 },
        },
        Liveries = new Dictionary<string, Ams2LiveryClassEntry>(StringComparer.Ordinal)
        {
            [VintageClass] = new() { Name = VintageClass, StockLib1563 = [StockLivery1, StockLivery2] },
        },
    };

    public static SeasonPack TwoRoundPack(
        string secondLivery = StockLivery2,
        string secondTeamId = "team.brabham") => new()
    {
        Manifest = new PackManifest
        {
            PackId = "test-pack",
            Name = "Test Pack",
            Version = "1.0.0",
            FormatVersion = 1,
        },
        Season = new SeasonDefinition
        {
            Year = 1967,
            SeriesName = "Test Championship",
            Ams2Class = VintageClass,
            PointsSystem = new CatalogSeason
            {
                RacePoints = [new(9), new(6), new(4), new(3), new(2), new(1)],
                Constructors = new CatalogConstructors { BestCarOnly = true, BestN = "sameAsDrivers" },
                DriversBestN = new CatalogBestN { WholeSeason = 2 },
            },
            Rounds = [Round(1, "1967-01-02"), Round(2, "1967-05-07")],
        },
        Teams =
        [
            new PackTeam
            {
                Id = "team.brabham",
                Name = "Brabham-Repco",
                CarVehicleIds = [VintageCar],
                Reliability = 0.93,
                Prestige = 4,
                BudgetTier = 5,
            },
        ],
        Drivers = [Driver("driver.brabham"), Driver("driver.hulme")],
        Entries =
        [
            Entry("team.brabham", "driver.brabham", "1", StockLivery1),
            Entry(secondTeamId, "driver.hulme", "2", secondLivery),
        ],
    };

    /// <summary>The two-round pack with round 1 authored as a TWO-race weekend (qualifying + a
    /// feature on the primary table + a sprint on the sprint table), plus a sprint points table so
    /// the per-session "sprint" table resolves. Round 2 stays single-race (Increment 2 mixed shape).</summary>
    public static SeasonPack TwoRaceWeekendPack()
    {
        var basePack = TwoRoundPack();
        var round1 = basePack.Season.Rounds[0] with
        {
            Weekend = new PackWeekend
            {
                Qualifying = new PackWeekendSession { Label = "Qualifying" },
                Races =
                [
                    new PackWeekendRace { Id = "race", Label = "Feature" },              // primary table
                    new PackWeekendRace { Id = "race2", Label = "Sprint", PointsTable = "sprint" },
                ],
            },
        };
        return basePack with
        {
            Season = basePack.Season with
            {
                PointsSystem = basePack.Season.PointsSystem with
                {
                    SprintPoints = [new(8), new(6), new(4), new(3), new(2), new(1)],
                },
                Rounds = [round1, basePack.Season.Rounds[1]],
            },
        };
    }

    public static PackRound Round(int number, string date, int opponents = 1) => new()
    {
        Round = number,
        Name = $"Round {number}",
        Date = date,
        Track = new PackTrackRef { Id = Track },
        Laps = 40,
        SetupGuide = new PackSetupGuide { Session = new PackSessionSettings { Opponents = opponents } },
    };

    public static PackDriver Driver(string id) => new()
    {
        Id = id,
        Name = id,
        Ratings = new PackDriverRatings
        {
            RaceSkill = 0.8,
            QualifyingSkill = 0.85,
            Aggression = 0.5,
            Defending = 0.5,
            Stamina = 0.8,
            Consistency = 0.8,
            StartReactions = 0.8,
            WetSkill = 0.8,
            TyreManagement = 0.8,
            AvoidanceOfMistakes = 0.8,
        },
    };

    public static PackEntry Entry(string teamId, string driverId, string number, string livery) => new()
    {
        TeamId = teamId,
        DriverId = driverId,
        Number = number,
        Rounds = "1-2",
        Ams2LiveryName = livery,
    };

    /// <summary>Writes the pack as its five-file on-disk form (round-trips through the same
    /// CoreJson options the loader parses with).</summary>
    public static void Write(SeasonPack pack, string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "pack.json"),
            JsonSerializer.Serialize(pack.Manifest, CoreJson.Options));
        File.WriteAllText(Path.Combine(directory, "season.json"),
            JsonSerializer.Serialize(pack.Season, CoreJson.Options));
        File.WriteAllText(Path.Combine(directory, "teams.json"),
            JsonSerializer.Serialize(new PackTeamsFile { Teams = pack.Teams }, CoreJson.Options));
        File.WriteAllText(Path.Combine(directory, "drivers.json"),
            JsonSerializer.Serialize(new PackDriversFile { Drivers = pack.Drivers }, CoreJson.Options));
        File.WriteAllText(Path.Combine(directory, "entries.json"),
            JsonSerializer.Serialize(new PackEntriesFile { Entries = pack.Entries }, CoreJson.Options));
    }
}

internal sealed class FakeCareerSession : ICareerSession
{
    public CareerSummary Summary { get; set; } = new()
    {
        CareerName = "Fake Career",
        SeasonYear = 1967,
        SeriesName = "Test Championship",
        CurrentRound = 1,
        RoundCount = 11,
        PlayerDriverId = "driver.hulme",
        PlayerLiveryName = TestPackBuilder.StockLivery2,
    };

    public SeasonPack Pack { get; set; } = TestPackBuilder.TwoRoundPack();

    /// <summary>The season track schedule surfaced to the Calendar lens (empty by default).</summary>
    public List<SeasonScheduleEntry> ScheduleEntries { get; } = [];

    public IReadOnlyList<SeasonScheduleEntry> SeasonSchedule() => ScheduleEntries;

    public BriefingModel? Briefing { get; set; }

    /// <summary>Real historical seasons keyed by year, surfaced through the
    /// <see cref="ICareerSession.HistoricalSeason(int)"/> seam (empty by default = the default-null
    /// seam behaviour). Lets a test drive the circuit lookup with different per-year data.</summary>
    public Dictionary<int, HistoricalSeason> HistoryByYear { get; } = [];

    public HistoricalSeason? HistoricalSeason(int seasonYear) =>
        HistoryByYear.GetValueOrDefault(seasonYear);

    public Queue<StageOutcome> StageOutcomes { get; } = new();

    public List<ResultDraft> Applied { get; } = [];

    public BriefingModel? CurrentBriefing() => Briefing;

    public StageOutcome StageCurrentGrid() =>
        StageOutcomes.Count > 0
            ? StageOutcomes.Dequeue()
            : new StageOutcome { Success = false, Messages = ["no staged outcome queued"] };

    /// <summary>The seats <see cref="CurrentGrid"/> returns (empty by default).</summary>
    public IReadOnlyList<GridSeat> Grid { get; set; } = [];

    public IReadOnlyList<GridSeat> CurrentGrid() => Grid;

    /// <summary>The value <see cref="CurrentExpectedFinish"/> returns (null = no seat/gamble).</summary>
    public int? ExpectedFinish { get; set; }

    public int? CurrentExpectedFinish() => ExpectedFinish;

    public ConfirmModel Preview(ResultDraft draft) => new()
    {
        RoundPoints = [],
        Movements = [],
        Headline = "fake headline",
    };

    public void Apply(ResultDraft draft) => Applied.Add(draft);

    public StandingsSnapshot? CurrentStandings() => Snapshots.Count > 0 ? Snapshots[^1] : null;

    /// <summary>Snapshots returned by <see cref="AllSnapshots"/> (empty by default).</summary>
    public List<StandingsSnapshot> Snapshots { get; } = [];

    public IReadOnlyList<StandingsSnapshot> AllSnapshots() => Snapshots;

    public int? SliderRecommendation { get; set; }

    public int? CurrentSliderRecommendation() => SliderRecommendation;

    public SeasonReviewModel? Review { get; set; }

    public SeasonReviewModel? SeasonReview() => Review;

    public List<string> AcceptedOffers { get; } = [];

    public void AcceptOffer(string teamId) => AcceptedOffers.Add(teamId);

    // ---------- era transition (M6 sign-and-continue) ----------

    public NextSeasonInfo? Next { get; set; }

    public NextSeasonInfo? NextSeason() => Next;

    public List<string> SignedTeams { get; } = [];

    public Exception? StartNextSeasonThrows { get; set; }

    public void StartNextSeason(string teamId)
    {
        if (StartNextSeasonThrows is not null)
            throw StartNextSeasonThrows;
        SignedTeams.Add(teamId);
    }

    // ---------- character development (depth 4) ----------

    /// <summary>The dossier surfaced to the Driver tab / review development block (null = no character).</summary>
    public CharacterDossier? Dossier { get; set; }

    /// <summary>Points the review's development block shows as available.</summary>
    public int Cp { get; set; }

    /// <summary>Development spends recorded through the seam, in order.</summary>
    public List<CharacterSpend> Spends { get; } = [];

    /// <summary>Perks the review's development block offers for purchase.</summary>
    public List<PurchasablePerk> Buyable { get; } = [];

    public CharacterDossier? CharacterDossier() => Dossier;

    // ---------- skins (read-only lens) ----------

    /// <summary>The skin picture the Skins lens projects (empty by default).</summary>
    public SkinAssignmentPlan SkinPlan { get; set; } = SkinAssignmentPlan.Empty;

    public SkinAssignmentPlan CurrentSkinAssignments() => SkinPlan;

    /// <summary>Livery names activated through the seam, in order.</summary>
    public List<string> ActivatedLiveries { get; } = [];

    /// <summary>The result the activator returns (success by default).</summary>
    public LiveryActivationResult ActivationResult { get; set; } =
        new() { Success = true, Slot = 61, Message = "Activated as slot 61." };

    public LiveryActivationResult ActivateLivery(string liveryName)
    {
        ActivatedLiveries.Add(liveryName);
        return ActivationResult;
    }

    /// <summary>The grid-editor overrides this fake persists (rename / rebind), keyed by livery.</summary>
    public Dictionary<string, SeatStagingOverride> Overrides { get; } = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, SeatStagingOverride> SeatStagingOverrides() => Overrides;

    public void SetSeatStagingOverride(string liveryKey, SeatStagingOverride seatOverride)
    {
        if (seatOverride.IsEmpty)
            Overrides.Remove(liveryKey);
        else
            Overrides[liveryKey] = seatOverride;
    }

    /// <summary>The player's driver id + character display name, surfaced to name-rendering screens.</summary>
    public (string DriverId, string DisplayName)? Identity { get; set; }

    public (string DriverId, string DisplayName)? PlayerIdentity() => Identity;

    /// <summary>The team the player drives for, surfaced to the Driver dossier.</summary>
    public string? TeamName { get; set; }

    public string? PlayerTeamName() => TeamName;

    public int AvailableCharacterCp() => Cp;

    public IReadOnlyList<PurchasablePerk> PurchasablePerks() =>
        Buyable.Where(p => p.Cost <= Cp).ToList();

    public void SpendCharacterPoint(CharacterSpend spend)
    {
        if (spend.Cost > Cp)
            throw new InvalidOperationException("unaffordable");
        Spends.Add(spend);
        Cp -= spend.Cost;
        // Mirror the real session: a stat spend bumps the shown value a step so a re-read reflects it.
        if (spend.Kind == "stat" && Dossier is { } dossier)
        {
            var raised = dossier.Stats
                .Select(s => s.Id == spend.Target ? s with { Value = s.Value + 0.02 } : s)
                .ToList();
            Dossier = dossier with { Stats = raised };
        }
        // A bought perk is no longer on offer.
        if (spend.Kind == "perk")
            Buyable.RemoveAll(p => p.Id == spend.Target);
    }
}

internal sealed class FakeCareerFactory : ICareerFactory
{
    public CareerCreationRequest? LastRequest { get; private set; }

    public string? LastOpenedPath { get; private set; }

    public FakeCareerSession Session { get; } = new();

    public ICareerSession Create(CareerCreationRequest request)
    {
        LastRequest = request;
        return Session;
    }

    public ICareerSession Open(string careerFilePath)
    {
        LastOpenedPath = careerFilePath;
        return Session;
    }
}

internal sealed class FakeFileWatcher : IFileWatcher
{
    public string? Watching { get; private set; }

    public event EventHandler<string>? Changed;

    public void Watch(string filePath) => Watching = filePath;

    public void Stop() => Watching = null;

    public void RaiseChanged(string path) => Changed?.Invoke(this, path);
}
