using Companion.Core.Numerics;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.ViewModels.Confirm;
using Companion.ViewModels.Review;
using Companion.ViewModels.Services;
using Companion.ViewModels.Standings;

namespace Companion.Tests.ViewModels;

/// <summary>
/// Constructor historical names (fix round, user direction): everywhere constructors appear
/// they resolve to the pack's teams.json name ("Brabham-Repco") with id fallback. Career-mode
/// constructor standings carry the PACK TEAM ID (the grid seats' TeamId feeds the engine's
/// ConstructorId, verified here against a real career round), not an f1db-style
/// chassis+engine key, so the teams.json lookup is the correct resolution. Community packs
/// author their own names, nothing is hardcoded.
/// </summary>
public sealed class ConstructorNamesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-ctor-names-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // ---------- the shared resolver ----------

    private static SeasonPack NamedPack() => TestPackBuilder.TwoRoundPack() with
    {
        Drivers =
        [
            TestPackBuilder.Driver("driver.brabham") with { Name = "Jack Brabham" },
            TestPackBuilder.Driver("driver.hulme") with { Name = "Denny Hulme" },
        ],
    };

    [Fact]
    public void Resolver_DriverThenTeamThenRawId()
    {
        var resolve = PackDisplayNames.ResolverFor(NamedPack());

        Assert.Equal("Jack Brabham", resolve("driver.brabham"));       // drivers.json name
        Assert.Equal("Brabham-Repco", resolve("team.brabham"));        // teams.json historical name
        Assert.Equal("team.unknown", resolve("team.unknown"));         // id fallback
        Assert.Equal("who?", resolve("who?"));
    }

    // ---------- career mode: what key do constructor standings actually carry? ----------

    [Fact]
    public void CareerConstructorStandings_CarryPackTeamIds_AndResolveToTeamsJsonNames()
    {
        var environment = ViewModelTestData.Environment(Path.Combine(_root, "docs"));
        using var session = CareerSessionService.CreateCareer(new CareerCreationRequest
        {
            PackDirectory = ViewModelTestData.RealPackDirectory,
            CareerFilePath = Path.Combine(_root, "career.ams2career"),
            CareerName = "Names 1967",
            MasterSeed = 7,
            PlayerLiveryName = "Brabham-Repco #2 D. Hulme",
        }, environment);

        var gridOrder = session.CurrentGrid().Select(s => s.DriverId).ToList();
        session.Apply(new ResultDraft
        {
            Classified = gridOrder,
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
        });

        var standings = session.CurrentStandings();
        Assert.NotNull(standings);
        Assert.NotNull(standings.Constructors);
        Assert.NotEmpty(standings.Constructors);

        // The key IS the pack teamId (grid seat TeamId → engine ConstructorId), not a
        // chassis+engine composite, so teams.json resolves every single one.
        var teamsById = session.Pack.Teams.ToDictionary(t => t.Id, StringComparer.Ordinal);
        Assert.All(standings.Constructors, c => Assert.Contains(c.ConstructorId, teamsById));

        // And the standings screen shows the pack-authored historical names.
        var vm = new StandingsViewModel(session.AllSnapshots(), session.Pack);
        var brabham = vm.ConstructorRows.Single(r => r.CompetitorId == "team.brabham");
        Assert.Equal("Brabham-Repco", brabham.DisplayName);
        Assert.All(vm.ConstructorRows, r => Assert.False(
            r.DisplayName.StartsWith("team.", StringComparison.Ordinal),
            $"constructor row '{r.CompetitorId}' leaked its raw id"));
    }

    // ---------- standings tab: id fallback for community packs ----------

    [Fact]
    public void StandingsConstructorRow_UnknownTeamId_FallsBackToTheRawId()
    {
        var snapshot = Snapshot(("team.brabham", 1, 9), ("team.ghost", 2, 6));
        var vm = new StandingsViewModel([snapshot], NamedPack());

        Assert.Equal("Brabham-Repco",
            vm.ConstructorRows.Single(r => r.CompetitorId == "team.brabham").DisplayName);
        Assert.Equal("team.ghost",
            vm.ConstructorRows.Single(r => r.CompetitorId == "team.ghost").DisplayName);
    }

    // ---------- confirm movements ----------

    [Fact]
    public void ConfirmRows_ResolveConstructorKeysThroughThePackResolver()
    {
        // The Home shell hands ConfirmViewModel the shared pack resolver, so a movement or
        // points row keyed by a constructor resolves exactly like a driver row.
        var model = new ConfirmModel
        {
            RoundPoints = [("driver.brabham", new Rational(9)), ("team.brabham", new Rational(9))],
            Movements = [("team.brabham", 2, 1), ("driver.hulme", null, 2)],
            Headline = "Brabham strikes first",
        };

        var vm = new ConfirmViewModel(
            model, onApply: () => { }, onBack: () => { },
            displayName: PackDisplayNames.ResolverFor(NamedPack()));

        Assert.Equal("Jack Brabham", vm.RoundPoints[0].DisplayName);
        Assert.Equal("Brabham-Repco", vm.RoundPoints[1].DisplayName);
        Assert.Equal("Brabham-Repco", vm.Movements[0].DisplayName);
        Assert.Equal("Denny Hulme", vm.Movements[1].DisplayName);
    }

    // ---------- season review: final standings block ----------

    [Fact]
    public void SeasonReviewFinalStandings_ShowTeamsJsonNames()
    {
        var session = new FakeCareerSession { Pack = NamedPack() };
        session.Snapshots.Add(Snapshot(("team.brabham", 1, 9)));

        var review = new SeasonReviewViewModel(session);

        var row = Assert.Single(review.FinalStandings.ConstructorRows);
        Assert.Equal("team.brabham", row.CompetitorId);
        Assert.Equal("Brabham-Repco", row.DisplayName);
    }

    // ---------- player character name on the standings + round matrix ----------

    [Fact]
    public void DriverStandingsAndMatrix_ShowThePlayerCharacterName_NotTheHistoricalDriver()
    {
        var session = new FakeCareerSession { Pack = NamedPack() };
        session.Snapshots.Add(Snapshot(("team.brabham", 1, 9))); // the snapshot's driver is driver.brabham
        session.Identity = ("driver.brabham", "Kobra Fleetworks"); // the player seated that livery

        var vm = new StandingsViewModel(session.AllSnapshots(), session.Pack, session: session);

        // Drivers tab: the player's row shows the character name, not "Jack Brabham".
        Assert.Equal("Kobra Fleetworks",
            vm.DriverRows.Single(r => r.CompetitorId == "driver.brabham").DisplayName);
        // Round matrix: same override (both read the one driver-name map).
        Assert.Equal("Kobra Fleetworks",
            vm.MatrixRows.Single(r => r.DriverId == "driver.brabham").DisplayName);

        // Without an identity (no character), the historical name is shown, exactly as before.
        var plain = new StandingsViewModel(session.AllSnapshots(), session.Pack);
        Assert.Equal("Jack Brabham",
            plain.DriverRows.Single(r => r.CompetitorId == "driver.brabham").DisplayName);
    }

    // ---------- snapshot fixture ----------

    private static StandingsSnapshot Snapshot(params (string TeamId, int Position, int Points)[] constructors) => new()
    {
        AfterRound = 1,
        Drivers =
        [
            new DriverStanding
            {
                DriverId = "driver.brabham",
                Position = 1,
                GrossPoints = new Rational(9),
                CountedPoints = new Rational(9),
                RoundScores = [new RoundScore { Round = 1, Points = new Rational(9) }],
                Dropped = [],
            },
        ],
        Constructors = constructors
            .Select(c => new ConstructorStanding
            {
                ConstructorId = c.TeamId,
                Position = c.Position,
                GrossPoints = new Rational(c.Points),
                CountedPoints = new Rational(c.Points),
                RoundScores = [new RoundScore { Round = 1, Points = new Rational(c.Points) }],
                Dropped = [],
            })
            .ToArray(),
    };
}
