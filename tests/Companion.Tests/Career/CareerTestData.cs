using Companion.Core.Career;
using Companion.Core.Determinism;
using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Scoring;

namespace Companion.Tests.Career;

/// <summary>
/// Test data for the career sim: the real career rules files (linked into the test output by
/// the csproj) plus a small synthetic 1967-style pack — three teams across the tier range,
/// six drivers, two rounds, constructors championship — and helpers to build grids, results,
/// and pipeline contexts.
/// </summary>
internal static class CareerTestData
{
    public static string RulesDirectory => Path.Combine(AppContext.BaseDirectory, "Fixtures", "rules");

    public static string ReadRules(string fileName)
    {
        string path = Path.Combine(RulesDirectory, fileName);
        Assert.True(File.Exists(path),
            $"Rules file '{path}' was not copied to the test output — rebuild tests/Companion.Tests.");
        return File.ReadAllText(path);
    }

    public static AgingCurveSet LoadAgingCurves() =>
        AgingCurveSet.Parse(ReadRules("career-aging-curves.json"));

    public static TeamArchetypeCatalog LoadArchetypes() =>
        TeamArchetypeCatalog.Parse(ReadRules("career-team-archetypes.json"));

    public static HeadlineBank LoadHeadlines() =>
        HeadlineBank.Parse(ReadRules("career-headline-templates.json"));

    // ---------- synthetic pack ----------

    public const string PlayerDriverId = "driver.p";
    public const string PlayerLivery = "Mid #4";

    public static PackDriverRatings Ratings(double raceSkill, double qualifyingSkill) => new()
    {
        RaceSkill = raceSkill,
        QualifyingSkill = qualifyingSkill,
        Aggression = 0.5,
        Defending = 0.5,
        Stamina = 0.7,
        Consistency = 0.7,
        StartReactions = 0.8,
        WetSkill = 0.6,
        TyreManagement = 0.6,
        AvoidanceOfMistakes = 0.6,
    };

    public static SeasonPack Pack()
    {
        PackTeam Team(string id, string name, int tier, double power, double weight, double reliability) => new()
        {
            Id = id,
            Name = name,
            CarVehicleIds = ["formula_vintage_g1m1"],
            Reliability = reliability,
            BudgetTier = tier,
            Performance = new PackTeamPerformance { PowerScalar = power, WeightScalar = weight },
        };

        PackDriver Driver(string id, string name, double race, double quali) => new()
        {
            Id = id,
            Name = name,
            Country = "TST",
            Ratings = Ratings(race, quali),
        };

        PackEntry Entry(string teamId, string driverId, string number, string livery) => new()
        {
            TeamId = teamId,
            DriverId = driverId,
            Number = number,
            Rounds = "1-2",
            Ams2LiveryName = livery,
        };

        PackRound Round(int round) => new()
        {
            Round = round,
            Name = $"Test Grand Prix {round}",
            Date = $"1967-0{round}-01",
            Track = new PackTrackRef { Id = "kyalami_historic" },
            Laps = 10,
        };

        return new SeasonPack
        {
            Manifest = new PackManifest
            {
                PackId = "career-test-pack",
                Name = "Career Test Pack",
                Version = "1.0.0",
                FormatVersion = 1,
            },
            Season = new SeasonDefinition
            {
                Year = 1967,
                SeriesName = "Test Series",
                Ams2Class = "F-Vintage_Gen1",
                PointsSystem = new CatalogSeason
                {
                    RacePoints = [new(9), new(6), new(4), new(3), new(2), new(1)],
                    Constructors = new CatalogConstructors { BestCarOnly = false },
                },
                Rounds = [Round(1), Round(2)],
            },
            Teams =
            [
                Team("team.top", "Top Team", 5, 1.02, 0.98, 0.95),
                Team("team.mid", "Mid Team", 3, 1.00, 1.00, 0.90),
                Team("team.min", "Minnow Team", 1, 0.97, 1.02, 0.82),
            ],
            Drivers =
            [
                Driver("driver.a", "Alan Apex", 0.80, 0.82),
                Driver("driver.b", "Bob Brakes", 0.75, 0.74),
                Driver(PlayerDriverId, "Pat Player", 0.70, 0.70),
                Driver("driver.canon_next", "Charlie Canon", 0.72, 0.71),
                Driver("driver.old", "Old Oscar", 0.50, 0.52),
                Driver("driver.canon_now", "Norm Now", 0.60, 0.61),
            ],
            Entries =
            [
                Entry("team.top", "driver.a", "1", "Top #1"),
                Entry("team.top", "driver.b", "2", "Top #2"),
                Entry("team.mid", PlayerDriverId, "4", PlayerLivery),
                Entry("team.mid", "driver.canon_next", "5", "Mid #5"),
                Entry("team.min", "driver.old", "7", "Min #7"),
                Entry("team.min", "driver.canon_now", "8", "Min #8"),
            ],
        };
    }

    /// <summary>Both rounds finish in the same order:
    /// old, canon_now, player, canon_next, a, b — the minnow team wins the constructors
    /// title (overachieves), the top team flops (underachieves), the player is P3.</summary>
    public static IReadOnlyList<RoundResult> Rounds()
    {
        string[] order = ["driver.old", "driver.canon_now", PlayerDriverId, "driver.canon_next", "driver.a", "driver.b"];
        string[] teams = ["team.min", "team.min", "team.mid", "team.mid", "team.top", "team.top"];

        RoundResult Round(int round) => new()
        {
            Round = round,
            Sessions =
            [
                new SessionResult
                {
                    Kind = SessionKind.Race,
                    Entries = order.Select((driverId, i) => new ClassifiedEntry
                    {
                        DriverId = driverId,
                        ConstructorId = teams[i],
                        Position = i + 1,
                    }).ToList(),
                },
            ],
        };

        return [Round(1), Round(2)];
    }

    public static IReadOnlyList<DriverCareerState> DriverStates() =>
    [
        new DriverCareerState { DriverId = "driver.a", Age = 25 },
        new DriverCareerState { DriverId = "driver.b", Age = 26 },
        new DriverCareerState { DriverId = "driver.canon_next", Age = 30 },
        new DriverCareerState { DriverId = "driver.old", Age = 40 },
        new DriverCareerState { DriverId = "driver.canon_now", Age = 35 },
    ];

    public static IReadOnlyList<TeamCareerState> TeamStates() =>
    [
        new TeamCareerState { TeamId = "team.top", LineageId = "team.top", Tier = 5 },
        new TeamCareerState { TeamId = "team.mid", LineageId = "team.mid", Tier = 3 },
        new TeamCareerState { TeamId = "team.min", LineageId = "team.min", Tier = 1 },
    ];

    public static SeasonEndContext Context(
        ulong masterSeed = 42,
        double playerReputation = 40.0,
        IReadOnlyList<DriverCareerState>? drivers = null,
        IReadOnlyList<SeatCandidate>? freeAgents = null,
        bool withHeadlines = true) => new()
    {
        Year = 1967,
        Streams = new StreamFactory(masterSeed),
        Pack = Pack(),
        Rounds = Rounds(),
        PlayerDriverId = PlayerDriverId,
        PlayerAge = 27,
        Player = new PlayerCareerState
        {
            Reputation = playerReputation,
            Opi = 1.2,
            PaceAnchor = 92.0,
            SeasonsCompleted = 1,
            CurrentTeamId = "team.mid",
            LiveryName = PlayerLivery,
        },
        Drivers = drivers ?? DriverStates(),
        Teams = TeamStates(),
        AgingCurves = LoadAgingCurves(),
        Archetypes = LoadArchetypes(),
        Headlines = withHeadlines ? LoadHeadlines() : null,
        CanonRetirements = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["driver.canon_now"] = 1967,
            ["driver.canon_next"] = 1968,
        },
        FreeAgents = freeAgents ??
        [
            new SeatCandidate { DriverId = "driver.fa1", RaceSkill = 0.70, Age = 28 },
            new SeatCandidate { DriverId = "driver.fa2", RaceSkill = 0.60, Age = 30, PayBudgetBu = 20.0 },
        ],
        PlayerName = "Pat Player",
    };

    // ---------- grids ----------

    public static GridSeat Seat(
        string driverId,
        string teamId,
        double raceSkill,
        double power = 1.0,
        double weight = 1.0,
        double drag = 1.0,
        double reliability = 0.9,
        bool isPlayer = false,
        string? livery = null) => new()
    {
        DriverId = driverId,
        DriverName = driverId,
        TeamId = teamId,
        TeamName = teamId,
        Number = "0",
        Ams2LiveryName = livery ?? driverId,
        Ratings = Ratings(raceSkill, raceSkill),
        Reliability = reliability,
        PowerScalar = power,
        WeightScalar = weight,
        DragScalar = drag,
        IsPlayer = isPlayer,
    };

    public static GridPlan Grid(params GridSeat[] seats) => new()
    {
        PackId = "career-test-pack",
        Year = 1967,
        SeriesName = "Test Series",
        Ams2Class = "F-Vintage_Gen1",
        Round = 1,
        RoundName = "Test Grand Prix 1",
        TrackId = "kyalami_historic",
        Seats = seats,
    };

    /// <summary>A four-seat grid with the player on the neutral midfield car: one clearly
    /// faster car+driver, one equal AI teammate, one clearly slower car.</summary>
    public static GridPlan PlayerGrid() => Grid(
        Seat("driver.fast", "team.top", 0.85, power: 1.02, weight: 0.98, reliability: 0.95),
        Seat(PlayerDriverId, "team.mid", 0.70, isPlayer: true, livery: PlayerLivery),
        Seat("driver.mate", "team.mid", 0.70),
        Seat("driver.slow", "team.min", 0.55, power: 0.97, weight: 1.02, reliability: 0.82));
}
