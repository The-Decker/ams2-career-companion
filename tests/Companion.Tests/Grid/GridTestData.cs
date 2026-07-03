using Companion.Core.Packs;
using Companion.Core.Scoring;

namespace Companion.Tests.Grid;

/// <summary>
/// Test data for the grid pipeline: loads the shipped reference packs from the test output
/// (machine-independent — packs\ and Fixtures\ams2 are copied by the csproj) and builds small
/// synthetic packs for the cases the reference data does not exercise (guest entries,
/// trackForm clamping, duplicate liveries).
/// </summary>
internal static class GridTestData
{
    public static string PacksDirectory => Path.Combine(AppContext.BaseDirectory, "packs");

    public static string Ams2DataDirectory => Path.Combine(AppContext.BaseDirectory, "Fixtures", "ams2");

    public static SeasonPack LoadReferencePack(string packId)
    {
        string dir = Path.Combine(PacksDirectory, packId);
        Assert.True(Directory.Exists(dir),
            $"Reference pack folder '{dir}' was not copied to the test output — rebuild tests/Companion.Tests.");

        return PackLoader.Parse(
            Read(dir, "pack.json"),
            Read(dir, "season.json"),
            Read(dir, "teams.json"),
            Read(dir, "drivers.json"),
            Read(dir, "entries.json"));
    }

    private static string Read(string dir, string filePart)
    {
        string path = Path.Combine(dir, filePart);
        Assert.True(File.Exists(path), $"Pack file '{path}' is missing.");
        return File.ReadAllText(path);
    }

    // ---------- synthetic pack building ----------

    public static PackDriverRatings Ratings(double raceSkill = 0.80, double qualifyingSkill = 0.80) => new()
    {
        RaceSkill = raceSkill,
        QualifyingSkill = qualifyingSkill,
        Aggression = 0.50,
        Defending = 0.45,
        Stamina = 0.70,
        Consistency = 0.75,
        StartReactions = 0.85,
        WetSkill = 0.60,
        TyreManagement = 0.65,
        AvoidanceOfMistakes = 0.55,
    };

    public static PackDriver Driver(
        string id,
        string name,
        PackDriverRatings? ratings = null,
        IReadOnlyDictionary<string, double>? trackForm = null) => new()
    {
        Id = id,
        Name = name,
        Country = "TST",
        Ratings = ratings ?? Ratings(),
        TrackForm = trackForm ?? new Dictionary<string, double>(),
    };

    public static PackTeam Team(
        string id,
        string name,
        double reliability = 0.90,
        double weightScalar = 1.0,
        double powerScalar = 1.0,
        double dragScalar = 1.0) => new()
    {
        Id = id,
        Name = name,
        CarVehicleIds = ["formula_vintage_g1m1"],
        Reliability = reliability,
        Performance = new PackTeamPerformance
        {
            WeightScalar = weightScalar,
            PowerScalar = powerScalar,
            DragScalar = dragScalar,
        },
    };

    public static PackEntry Entry(string teamId, string driverId, string number, string rounds, string livery) => new()
    {
        TeamId = teamId,
        DriverId = driverId,
        Number = number,
        Rounds = rounds,
        Ams2LiveryName = livery,
    };

    public static PackRound Round(
        int round,
        string trackId = "kyalami_historic",
        IReadOnlyList<PackGuestEntry>? guestEntries = null,
        IReadOnlyDictionary<string, PackRatingsPatch>? aiOverrides = null,
        PackRoundGrid? grid = null) => new()
    {
        Round = round,
        Name = $"Test Grand Prix {round}",
        Date = "1967-01-02",
        Track = new PackTrackRef { Id = trackId, RealVenue = "Test Venue" },
        Laps = 10,
        Grid = grid,
        GuestEntries = guestEntries ?? [],
        AiOverrides = aiOverrides ?? new Dictionary<string, PackRatingsPatch>(),
    };

    public static PackRoundGrid Grid(int size, params string[] starterDriverIds) => new()
    {
        Size = size,
        StarterDriverIds = starterDriverIds,
    };

    public static SeasonPack Pack(
        IReadOnlyList<PackTeam> teams,
        IReadOnlyList<PackDriver> drivers,
        IReadOnlyList<PackEntry> entries,
        IReadOnlyList<PackRound> rounds) => new()
    {
        Manifest = new PackManifest
        {
            PackId = "test-pack",
            Name = "Synthetic Test Pack",
            Version = "1.0.0",
            FormatVersion = 1,
        },
        Season = new SeasonDefinition
        {
            Year = 1967,
            SeriesName = "Test Series",
            Ams2Class = "F-Vintage_Gen1",
            PointsSystem = new CatalogSeason { RacePoints = [new(9), new(6), new(4), new(3), new(2), new(1)] },
            Rounds = rounds,
        },
        Teams = teams,
        Drivers = drivers,
        Entries = entries,
    };
}
