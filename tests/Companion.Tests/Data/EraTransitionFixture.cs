using Companion.Core.Career;
using Companion.Core.Determinism;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.Data;
using Companion.Tests.Career;

namespace Companion.Tests.Data;

/// <summary>
/// The era-transition career for the Data tests: season 1 is <see cref="DataCareerFixture"/>'s
/// 1967 mini-season, season 2 is a synthetic 1969 pack (so 1968 is a bridged gap year).
/// Lineages: team.top and team.mid carry, team.min departs, team.new arrives; driver.a
/// carries, driver.b is renamed to driver.bob (does not carry), the player keeps driver.p.
/// Helpers play the whole transitioned career exactly the way the app's live path would:
/// season 1 through the unified fold + season end, offer accepted, plan built through
/// <see cref="EraTransition"/>, season 2 started via <see cref="CareerStore.StartNextSeason"/>
/// and played through the same fold + season end against ITS OWN pack.
/// </summary>
internal static class EraTransitionFixture
{
    public const string Utc2 = "2026-07-03T00:00:00Z";
    public const string AcceptedTeam = "team.mid";
    public const string Season2PlayerLivery = "Mid69 #4";

    public static SeasonPack ToPack()
    {
        PackTeam Team(string id, string name, int tier) => new()
        {
            Id = id,
            Name = name,
            CarVehicleIds = ["formula_vintage_g2m1"],
            Reliability = 0.9,
            BudgetTier = tier,
        };

        PackDriver Driver(string id, string name, int born, double race, double quali) => new()
        {
            Id = id,
            Name = name,
            Country = "TST",
            Born = born,
            Ratings = CareerTestData.Ratings(race, quali),
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
            Name = $"Transition Grand Prix {round}",
            Date = $"1969-0{round}-01",
            Track = new PackTrackRef { Id = "kyalami_historic" },
            Laps = 10,
        };

        return new SeasonPack
        {
            Manifest = new PackManifest
            {
                PackId = "replay-test-pack-1969",
                Name = "Replay Test Pack 1969",
                Version = "1.0.0",
                FormatVersion = 1,
            },
            Season = new SeasonDefinition
            {
                Year = 1969,
                SeriesName = "Replay Test Series Mk2",
                Ams2Class = "F-Vintage_Gen2",
                PointsSystem = new CatalogSeason
                {
                    RacePoints = [new(9), new(6), new(4), new(3), new(2), new(1)],
                    Constructors = new CatalogConstructors { BestCarOnly = false },
                },
                Rounds = [Round(1), Round(2)],
            },
            Teams =
            [
                Team("team.top", "Top Team Mk2", 5),
                Team("team.mid", "Mid Team Mk2", 3),
                Team("team.new", "New Era Racing", 2),
            ],
            Drivers =
            [
                Driver("driver.a", "Alan Apex", 1941, 0.82, 0.83),
                Driver("driver.p", "Pat Player", 1940, 0.72, 0.72),
                Driver("driver.bob", "Bob Brakes", 1943, 0.76, 0.75),
                Driver("driver.young", "Yves Young", 1948, 0.68, 0.69),
                Driver("driver.new_guy", "Nino New", 1945, 0.66, 0.66),
            ],
            Entries =
            [
                Entry("team.top", "driver.a", "1", "Top69 #1"),
                Entry("team.top", "driver.bob", "2", "Top69 #2"),
                Entry("team.mid", "driver.p", "4", Season2PlayerLivery),
                Entry("team.mid", "driver.young", "5", "Mid69 #5"),
                Entry("team.new", "driver.new_guy", "7", "New69 #7"),
            ],
        };
    }

    /// <summary>Two 1969 rounds with different finishing orders; the player podiums.</summary>
    public static IReadOnlyList<RoundResult> Season2Rounds()
    {
        var teamsByDriver = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["driver.a"] = "team.top",
            ["driver.bob"] = "team.top",
            [DataCareerFixture.PlayerDriverId] = "team.mid",
            ["driver.young"] = "team.mid",
            ["driver.new_guy"] = "team.new",
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
            Round(1, "driver.a", DataCareerFixture.PlayerDriverId, "driver.bob", "driver.young", "driver.new_guy"),
            Round(2, DataCareerFixture.PlayerDriverId, "driver.a", "driver.young", "driver.bob", "driver.new_guy"),
        ];
    }

    /// <summary>Builds the transition plan exactly the way the live sign-and-continue flow
    /// would: from the season-1 pipeline output, the accepted team.mid offer, and the same
    /// master seed + rules data replay receives.</summary>
    public static TransitionPlan BuildPlan(SeasonEndResult season1End, SeasonPack toPack)
    {
        var offer = season1End.Offers.Single(o => o.TeamId == AcceptedTeam);
        return EraTransition.Build(
            DataCareerFixture.Pack(), toPack, season1End, season1End.Player, offer,
            new StreamFactory(DataCareerFixture.MasterSeed),
            CareerTestData.LoadAgingCurves(),
            DataCareerFixture.Inputs().CanonRetirements);
    }

    /// <summary>Plays the whole transitioned career: season 1 (1967 pack), the accepted
    /// offer, StartNextSeason into the 1969 pack (bridging 1968), and — unless
    /// <paramref name="playSeason2"/> is false — season 2's rounds and season end.</summary>
    public static (long Season1, long Season2, SeasonPack ToPack, TransitionPlan Plan)
        PlayTransitionedCareer(CareerDatabase db, bool playSeason2 = true)
    {
        var (season1, fromPack) = DataCareerFixture.SetupCareer(db);
        var season1End = DataCareerFixture.PlaySeason(db, season1, fromPack);

        Assert.Contains(season1End.Offers, o => o.TeamId == AcceptedTeam);
        StateStore.SetOfferAccepted(db, season1, AcceptedTeam);

        var toPack = ToPack();
        var plan = BuildPlan(season1End, toPack);
        Assert.Empty(plan.ValidationErrors);
        long season2 = CareerStore.StartNextSeason(db, plan, toPack, Utc2);

        if (playSeason2)
        {
            foreach (var round in Season2Rounds())
            {
                ReplayService.ImportAndFoldRound(
                    db, season2, toPack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs(),
                    round.Round, DataCareerFixture.Envelope(round), Utc2);
            }
            ReplayService.RunSeasonEnd(
                db, season2, toPack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs(), Utc2);
        }

        return (season1, season2, toPack, plan);
    }
}
