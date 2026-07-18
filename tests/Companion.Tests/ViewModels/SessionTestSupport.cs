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
    /// the test output, the same files the published exe ships beside itself.</summary>
    public static string RulesDirectory => Path.Combine(AppContext.BaseDirectory, "Fixtures", "rules");

    public static CareerEnvironment Environment(
        string documentsDirectory,
        string? installDirectory = null,
        Ams2ContentLibrary? library = null,
        string? historyDirectory = null) => new()
    {
        ContentLibrary = library ?? RealLibrary.Value,
        LocateInstall = () => installDirectory is null
            ? null
            : new Ams2Installation { InstallDirectory = installDirectory },
        DocumentsDirectory = documentsDirectory,
        RulesDirectory = RulesDirectory,
        HistoryDirectory = historyDirectory,
    };

    /// <summary>A library that knows nothing, every class/track/vehicle check fails.</summary>
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

    /// <summary>The era-skin override table surfaced through the
    /// <see cref="ICareerSession.EraThemeOverrides"/> seam (null by default = every era resolves
    /// from the built-in table). Lets a test drive the era-skin bind contract with a catalog.</summary>
    public Companion.Core.Career.EraThemeCatalog? EraOverrides { get; set; }

    public Companion.Core.Career.EraThemeCatalog? EraThemeOverrides() => EraOverrides;

    public Queue<StageOutcome> StageOutcomes { get; } = new();

    public List<ResultDraft> Applied { get; } = [];

    public BriefingModel? CurrentBriefing() => Briefing;

    /// <summary>The SMGP Paddock projection surfaced to the driver/team-preview tab (null by default,
    /// so the hub adds no Paddock tab).</summary>
    public SmgpPaddockModel? Paddock { get; set; }

    public SmgpPaddockModel? SmgpPaddock() => Paddock;

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

    /// <summary>The confirm model this fake previews (null = the default empty one), lets a test
    /// drive the confirm screen's points / movement name rendering.</summary>
    public ConfirmModel? PreviewModel { get; set; }

    public ConfirmModel Preview(ResultDraft draft) => PreviewModel ?? new()
    {
        RoundPoints = [],
        Movements = [],
        Headline = "fake headline",
    };

    /// <summary>When set, an Apply flips <see cref="Summary"/>.SeasonComplete true, so a wiring test
    /// can drive the shell into the season-complete branch (season review / campaign finale).</summary>
    public bool CompletesSeasonOnApply { get; set; }

    public void Apply(ResultDraft draft)
    {
        Applied.Add(draft);
        if (CompletesSeasonOnApply)
            Summary = Summary with { SeasonComplete = true };
    }

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

    public int CharacterDossierReadCount { get; private set; }

    /// <summary>Points the review's development block shows as available.</summary>
    public int Cp { get; set; }

    /// <summary>Development spends recorded through the seam, in order.</summary>
    public List<CharacterSpend> Spends { get; } = [];

    /// <summary>Perks the review's development block offers for purchase.</summary>
    public List<PurchasablePerk> Buyable { get; } = [];

    public CharacterDossier? CharacterDossier()
    {
        CharacterDossierReadCount++;
        return Dossier;
    }

    public SkillTreeSnapshot? Tree { get; set; }

    public int SkillTreeReadCount { get; private set; }

    public SkillTreeSnapshot? SkillTree()
    {
        SkillTreeReadCount++;
        return Tree ?? SkillTreeSnapshot.Empty;
    }

    public Func<IReadOnlyList<string>, SkillPlanPreview>? SkillPlanPreviewer { get; set; }

    public Exception? ApplySkillPlanThrows { get; set; }

    public List<IReadOnlyList<string>> AppliedSkillPlans { get; } = [];

    public SkillPlanPreview PreviewSkillPlan(IReadOnlyList<string> orderedNodeIds) =>
        SkillPlanPreviewer?.Invoke(orderedNodeIds)
        ?? throw new NotSupportedException("No fake skill-plan preview is configured.");

    public void ApplySkillPlan(IReadOnlyList<string> orderedNodeIds)
    {
        if (ApplySkillPlanThrows is not null)
            throw ApplySkillPlanThrows;
        AppliedSkillPlans.Add(orderedNodeIds.ToArray());
    }

    public SkillResetPreview? SkillResetQuote { get; set; }

    public Exception? ApplySkillResetThrows { get; set; }

    public int AppliedSkillResetCount { get; private set; }

    public SkillResetPreview? PreviewSkillReset() => SkillResetQuote;

    public void ApplySkillReset()
    {
        if (ApplySkillResetThrows is not null)
            throw ApplySkillResetThrows;
        AppliedSkillResetCount++;
    }

    public int RespecTokenCount { get; set; }

    public int RespecTokenReadCount { get; private set; }

    public int RespecTokensAvailable()
    {
        RespecTokenReadCount++;
        return RespecTokenCount;
    }

    public List<string> Respecs { get; } = [];

    public void RespecNode(string nodeId)
    {
        Respecs.Add(nodeId);
        if (RespecTokenCount > 0)
            RespecTokenCount--;
    }

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

    /// <summary>The currently-named SMGP rival's driver id, surfaced to the standings highlight (null = none).</summary>
    public string? SmgpRivalDriverId { get; set; }

    public string? CurrentSmgpRivalDriverId() => SmgpRivalDriverId;

    /// <summary>The team the player drives for, surfaced to the Driver dossier.</summary>
    public string? TeamName { get; set; }

    public int PlayerTeamNameReadCount { get; private set; }

    public string? PlayerTeamName()
    {
        PlayerTeamNameReadCount++;
        return TeamName;
    }

    // ---------- SMGP promotion / demotion (3c-2 / 3c-3) ----------

    /// <summary>The pending-offer promotion screen this fake surfaces after a round (null = none).
    /// Cleared when <see cref="ResolveSmgpOffer"/> answers it, mirroring the real session.</summary>
    public SmgpPromotionModel? Promotion { get; set; }

    public SmgpPromotionModel? CurrentSmgpPromotion() => Promotion;

    /// <summary>The demotion screen this fake surfaces (null = none), returned by
    /// <see cref="CurrentSmgpDemotion"/> regardless of the previous team, for wiring tests.</summary>
    public SmgpPromotionModel? Demotion { get; set; }

    public SmgpPromotionModel? CurrentSmgpDemotion(string? previousTeamId) => Demotion;

    /// <summary>The 17-season campaign finale this fake surfaces once the season completes (null = none),
    /// returned by <see cref="SmgpFinale"/>, for the finale wiring tests.</summary>
    public SmgpFinaleModel? Finale { get; set; }

    public SmgpFinaleModel? SmgpFinale() => Finale;

    /// <summary>The player's smgp team id captured before an apply (null outside the mode).</summary>
    public string? SmgpTeamId { get; set; }

    public string? CurrentSmgpTeamId() => SmgpTeamId;

    /// <summary>Every ResolveSmgpOffer decision, in order, the promotion screen's accept/decline.</summary>
    public List<bool> ResolvedOffers { get; } = [];

    public void ResolveSmgpOffer(bool accept)
    {
        ResolvedOffers.Add(accept);
        Promotion = null;
    }

    /// <summary>The Dynasty economy dashboard the fake serves (null = not an economy career).</summary>
    public DynastyEconomyDashboard? Economy { get; set; }

    public DynastyEconomyDashboard? EconomyDashboard() => Economy;

    /// <summary>Every accepted economy decision, in order.</summary>
    public List<Companion.Core.Dynasty.DynastyEconomyDecision> EconomyDecisions { get; } = [];

    /// <summary>When set, DeclareEconomyDecision throws it instead of recording, the
    /// injectable-throw pattern for error-path tests.</summary>
    public Exception? DeclareEconomyDecisionThrows { get; set; }

    public void DeclareEconomyDecision(Companion.Core.Dynasty.DynastyEconomyDecision decision)
    {
        if (DeclareEconomyDecisionThrows is not null)
            throw DeclareEconomyDecisionThrows;
        EconomyDecisions.Add(decision);
    }

    public int AvailableCharacterCpReadCount { get; private set; }

    public int AvailableCharacterCp()
    {
        AvailableCharacterCpReadCount++;
        return Cp;
    }

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
