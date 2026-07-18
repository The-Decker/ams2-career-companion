using Companion.Core.Career;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.Data;
using Companion.Tests.Career;

namespace Companion.Tests.Data;

/// <summary>
/// A synthetic 3-round mini-season for the persistence + replay tests: the same 1967-style
/// world as <see cref="CareerTestData"/> (three teams across the tier range, six drivers,
/// constructors championship, real career rules files) but with a third round and per-round
/// varied finishing orders, plus helpers that play the season through the Data layer exactly
/// the way the app's live path does.
/// </summary>
internal static class DataCareerFixture
{
    public const string PlayerDriverId = "driver.p";
    public const string PlayerLivery = "Mid #4";
    public const ulong MasterSeed = 777;
    public const string Utc = "2026-07-02T00:00:00Z";

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
            Ratings = CareerTestData.Ratings(race, quali),
        };

        PackEntry Entry(string teamId, string driverId, string number, string livery) => new()
        {
            TeamId = teamId,
            DriverId = driverId,
            Number = number,
            Rounds = "1-3",
            Ams2LiveryName = livery,
        };

        PackRound Round(int round) => new()
        {
            Round = round,
            Name = $"Replay Grand Prix {round}",
            Date = $"1967-0{round}-01",
            Track = new PackTrackRef { Id = "kyalami_historic" },
            Laps = 10,
        };

        return new SeasonPack
        {
            Manifest = new PackManifest
            {
                PackId = "replay-test-pack",
                Name = "Replay Test Pack",
                Version = "1.0.0",
                FormatVersion = 1,
            },
            Season = new SeasonDefinition
            {
                Year = 1967,
                SeriesName = "Replay Test Series",
                Ams2Class = "F-Vintage_Gen1",
                PointsSystem = new CatalogSeason
                {
                    RacePoints = [new(9), new(6), new(4), new(3), new(2), new(1)],
                    Constructors = new CatalogConstructors { BestCarOnly = false },
                },
                Rounds = [Round(1), Round(2), Round(3)],
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

    /// <summary>Three rounds with different finishing orders (so every round moves the
    /// standings): the minnow team still overachieves and the player lands on the podium.</summary>
    public static IReadOnlyList<RoundResult> Rounds()
    {
        var teamsByDriver = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["driver.a"] = "team.top",
            ["driver.b"] = "team.top",
            [PlayerDriverId] = "team.mid",
            ["driver.canon_next"] = "team.mid",
            ["driver.old"] = "team.min",
            ["driver.canon_now"] = "team.min",
        };

        RoundResult Round(int round, params string[] order) => new()
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
                        ConstructorId = teamsByDriver[driverId],
                        Position = i + 1,
                    }).ToList(),
                },
            ],
        };

        return
        [
            Round(1, "driver.old", "driver.canon_now", PlayerDriverId, "driver.canon_next", "driver.a", "driver.b"),
            Round(2, "driver.canon_now", PlayerDriverId, "driver.old", "driver.a", "driver.b", "driver.canon_next"),
            Round(3, PlayerDriverId, "driver.old", "driver.canon_now", "driver.b", "driver.canon_next", "driver.a"),
        ];
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

    public static PlayerCareerState PlayerStart() => new()
    {
        Reputation = 40.0,
        Opi = 1.2,
        PaceAnchor = 92.0,
        SeasonsCompleted = 1,
        CurrentTeamId = "team.mid",
        LiveryName = PlayerLivery,
    };

    public static ReplaySimInputs Inputs() => new()
    {
        AgingCurves = CareerTestData.LoadAgingCurves(),
        Archetypes = CareerTestData.LoadArchetypes(),
        Headlines = CareerTestData.LoadHeadlines(),
        PlayerDriverId = PlayerDriverId,
        PlayerAge = 27,
        CanonRetirements = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["driver.canon_now"] = 1967,
            ["driver.canon_next"] = 1968,
        },
        FreeAgents =
        [
            new SeatCandidate { DriverId = "driver.fa1", RaceSkill = 0.70, Age = 28 },
            new SeatCandidate { DriverId = "driver.fa2", RaceSkill = 0.60, Age = 30, PayBudgetBu = 20.0 },
        ],
        PlayerName = "Pat Player",
    };

    /// <summary>Creates the career, pins the pack, starts the 1967 season, and seeds the
    /// start-of-season states, everything a new-career wizard would do.</summary>
    public static (long SeasonId, SeasonPack Pack) SetupCareer(CareerDatabase db)
    {
        var pack = Pack();
        CareerStore.CreateCareer(db, "Replay Test Career", MasterSeed, "0.5.0-test", Utc);
        CareerStore.PinPack(db, pack, Utc);
        long seasonId = CareerStore.StartSeason(db, 1967, pack.Manifest.PackId, pack.Manifest.Version);
        StateStore.UpsertDriverStates(db, seasonId, StateStore.StageStart, DriverStates());
        StateStore.UpsertTeamStates(db, seasonId, StateStore.StageStart, TeamStates());
        StateStore.UpsertPlayerState(db, seasonId, StateStore.StageStart, PlayerStart());
        return (seasonId, pack);
    }

    /// <summary>Wraps a synthesized round result in the versioned envelope the result screen
    /// produces: an explicit slider (varied per round) and no player DNF (everyone finishes
    /// in the fixture rounds).</summary>
    public static RoundResultEnvelope Envelope(RoundResult round) => new()
    {
        Result = round,
        SliderUsed = 90.0 + round.Round,
    };

    /// <summary>Plays the season the way the live app path does: every round through the
    /// unified fold (raw import + standings + player round update, atomically), then season
    /// end through the shared pipeline consuming the final round's folded player state.</summary>
    public static SeasonEndResult PlaySeason(CareerDatabase db, long seasonId, SeasonPack pack)
    {
        foreach (var round in Rounds())
        {
            ReplayService.ImportAndFoldRound(
                db, seasonId, pack, MasterSeed, Inputs(), round.Round, Envelope(round), Utc);
        }
        return ReplayService.RunSeasonEnd(db, seasonId, pack, MasterSeed, Inputs(), Utc);
    }
}
