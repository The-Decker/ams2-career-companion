using Companion.Core.Career;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.Data;
using Companion.ViewModels.Review;
using Companion.ViewModels.Services;
using Companion.ViewModels.Shell;

namespace Companion.Tests.ViewModels;

/// <summary>
/// The M6 sign-and-continue flow proven END TO END on the REAL session service with
/// synthetic era packs (a 1967-style two-round season transitioning into a 1969-style pack,
/// so 1968 is a bridged gap year): next-pack discovery follows the v1 smallest-later-year
/// rule, signing executes EraTransition + CareerStore.StartNextSeason, reopening the career
/// lands in the NEW season's round 1 (the MRU/continue contract), the transitioned season
/// folds results normally, the plan's validation errors surface on the review screen, and
/// the no-next-pack state explains what season packs are.
/// </summary>
public sealed class EraSignAndContinueTests : IDisposable
{
    private const string PlayerLivery = "Mid #4";
    private const string Season2Livery = "Next69 #4";

    private readonly string _root = Directory.CreateTempSubdirectory("companion-era-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // SQLite WAL sidecars can outlive the connection briefly on Windows.
        }
    }

    private string PacksRoot => Path.Combine(_root, "packs");

    private string CareerPath => Path.Combine(_root, "career.ams2career");

    private CareerEnvironment Environment() => new()
    {
        ContentLibrary = TestPackBuilder.Library(),
        LocateInstall = static () => null,
        DocumentsDirectory = Path.Combine(_root, "docs"),
        RulesDirectory = ViewModelTestData.RulesDirectory,
        PackSearchRoots = () => [PacksRoot],
    };

    // ---------- synthetic era packs ----------

    /// <summary>The 1967-style source season: two teams across the tier range, four drivers
    /// with Born years (the transition ages them through the 1968 gap), two rounds.</summary>
    private static SeasonPack FromPack1967() => new()
    {
        Manifest = new PackManifest
        {
            PackId = "era-test-1967",
            Name = "Era Test 1967",
            Version = "1.0.0",
            FormatVersion = 1,
        },
        Season = new SeasonDefinition
        {
            Year = 1967,
            SeriesName = "Era Test Series",
            Ams2Class = TestPackBuilder.VintageClass,
            PointsSystem = new CatalogSeason
            {
                RacePoints = [new(9), new(6), new(4), new(3), new(2), new(1)],
                Constructors = new CatalogConstructors { BestCarOnly = false },
            },
            Rounds =
            [
                TestPackBuilder.Round(1, "1967-01-02"),
                TestPackBuilder.Round(2, "1967-05-07"),
            ],
        },
        Teams =
        [
            Team("team.apex", "Apex Racing", tier: 5),
            Team("team.mid", "Mid Racing", tier: 3),
        ],
        Drivers =
        [
            Driver("driver.a", born: 1938, race: 0.85, quali: 0.85),
            Driver("driver.b", born: 1941, race: 0.78, quali: 0.77),
            Driver("driver.p", born: 1940, race: 0.72, quali: 0.72), // the player's seat
            Driver("driver.d", born: 1943, race: 0.66, quali: 0.67),
        ],
        Entries =
        [
            Entry("team.apex", "driver.a", "1", "Apex #1"),
            Entry("team.apex", "driver.b", "2", "Apex #2"),
            Entry("team.mid", "driver.p", "4", PlayerLivery),
            Entry("team.mid", "driver.d", "5", "Mid #5"),
        ],
    };

    /// <summary>A 1969-style target pack whose team list is chosen per test: the accepted
    /// team's lineage carries (or is deliberately missing for the validation-error test).
    /// driver.a carries across; driver.next is the seat the player takes at
    /// <paramref name="playerTeamId"/> when it is present.</summary>
    private static SeasonPack ToPack(int year, string packId, params string[] teamIds)
    {
        var teams = new List<PackTeam>();
        var drivers = new List<PackDriver>
        {
            Driver("driver.a", born: 1938, race: 0.83, quali: 0.84),
            Driver("driver.next", born: 1946, race: 0.70, quali: 0.71),
            Driver("driver.new_era", born: 1945, race: 0.68, quali: 0.68),
        };
        var entries = new List<PackEntry>();

        for (int i = 0; i < teamIds.Length; i++)
        {
            teams.Add(Team(teamIds[i], $"{teamIds[i]} Mk2", tier: 3));
            // First team gets driver.next ("Next69 #4" — the seat the player takes when this
            // is the accepted team), the rest spread over the remaining carried/new drivers.
            string driverId = i == 0 ? "driver.next" : i == 1 ? "driver.a" : "driver.new_era";
            string livery = i == 0 ? Season2Livery : $"Rest69 #{i + 10}";
            entries.Add(Entry(teamIds[i], driverId, (i + 1).ToString(), livery));
        }

        return new SeasonPack
        {
            Manifest = new PackManifest
            {
                PackId = packId,
                Name = $"Era Test {year}",
                Version = "1.0.0",
                FormatVersion = 1,
            },
            Season = new SeasonDefinition
            {
                Year = year,
                SeriesName = "Era Test Series Mk2",
                Ams2Class = TestPackBuilder.VintageClass,
                PointsSystem = new CatalogSeason
                {
                    RacePoints = [new(9), new(6), new(4), new(3), new(2), new(1)],
                    Constructors = new CatalogConstructors { BestCarOnly = false },
                },
                Rounds =
                [
                    TestPackBuilder.Round(1, $"{year}-01-02"),
                    TestPackBuilder.Round(2, $"{year}-05-07"),
                ],
            },
            Teams = teams,
            Drivers = drivers,
            Entries = entries,
        };
    }

    private static PackTeam Team(string id, string name, int tier) => new()
    {
        Id = id,
        Name = name,
        CarVehicleIds = [TestPackBuilder.VintageCar],
        Reliability = 0.9,
        BudgetTier = tier,
    };

    private static PackDriver Driver(string id, int born, double race, double quali) =>
        TestPackBuilder.Driver(id) with
        {
            Born = born,
            Ratings = TestPackBuilder.Driver(id).Ratings with
            {
                RaceSkill = race,
                QualifyingSkill = quali,
            },
        };

    private static PackEntry Entry(string teamId, string driverId, string number, string livery) => new()
    {
        TeamId = teamId,
        DriverId = driverId,
        Number = number,
        Rounds = "1-2",
        Ams2LiveryName = livery,
    };

    // ---------- helpers ----------

    /// <summary>Creates the career on the 1967 pack (written into the packs root) and plays
    /// every round through the REAL Apply path (grid order = finishing order).</summary>
    private CareerSessionService CreateAndPlaySeason()
    {
        var fromPack = FromPack1967();
        string fromDirectory = Path.Combine(PacksRoot, fromPack.Manifest.PackId);
        TestPackBuilder.Write(fromPack, fromDirectory);

        var session = CareerSessionService.CreateCareer(new CareerCreationRequest
        {
            PackDirectory = fromDirectory,
            CareerFilePath = CareerPath,
            CareerName = "Era Career",
            MasterSeed = 20260703,
            PlayerLiveryName = PlayerLivery,
        }, Environment());

        while (!session.Summary.SeasonComplete)
        {
            var grid = session.CurrentGrid();
            Assert.NotEmpty(grid);
            session.Apply(new ResultDraft
            {
                Classified = grid.Select(s => s.DriverId).ToList(),
                DidNotFinish = new Dictionary<string, string>(),
                Disqualified = [],
            });
        }
        return session;
    }

    // ---------- the tests ----------

    [Fact]
    public void SignAndContinue_HappyPath_BridgesTheGapYear_AndReopensIntoTheNewSeason()
    {
        string acceptedTeam;
        using (var session = CreateAndPlaySeason())
        {
            // Discovery needs a completed season AND a later pack: nothing yet.
            Assert.Null(((ICareerSession)session).NextSeason());

            var review = session.SeasonReview();
            Assert.NotNull(review);
            Assert.NotEmpty(review.Offers);
            acceptedTeam = review.Offers[0].TeamId;

            // Two later packs installed: the v1 rule picks the SMALLEST later year (1969,
            // not 1974), and the skipped 1968 shows up as a bridged year.
            TestPackBuilder.Write(
                ToPack(1969, "era-test-1969", acceptedTeam, "team.fresh"),
                Path.Combine(PacksRoot, "era-test-1969"));
            TestPackBuilder.Write(
                ToPack(1974, "era-test-1974", acceptedTeam, "team.fresh"),
                Path.Combine(PacksRoot, "era-test-1974"));

            var next = ((ICareerSession)session).NextSeason();
            Assert.NotNull(next);
            Assert.Equal("era-test-1969", next.PackId);
            Assert.Equal(1969, next.SeasonYear);
            Assert.Equal(new[] { 1968 }, next.BridgedYears);

            // The review screen drives the whole flow: accept, then sign.
            var vm = new SeasonReviewViewModel(session);
            Assert.True(vm.HasNextSeason);
            Assert.Equal("Sign & start 1969", vm.SignButtonText);
            Assert.Equal("1968 has no pack — your career bridges through it.", vm.BridgeNote);
            Assert.False(vm.SignAndContinueCommand.CanExecute(null)); // no acceptance yet

            vm.AcceptOfferCommand.Execute(vm.Offers.First(o => o.TeamId == acceptedTeam));
            Assert.True(vm.SignAndContinueCommand.CanExecute(null));

            bool signed = false;
            vm.SeasonSigned += (_, _) => signed = true;
            vm.SignAndContinueCommand.Execute(null);
            Assert.True(signed);
            Assert.Null(vm.TransitionError);
        }

        // The career file now has two seasons and the era.transition journal rows.
        using (var db = CareerDatabase.Open(CareerPath))
        {
            var seasons = CareerStore.ReadSeasons(db);
            Assert.Equal(2, seasons.Count);
            Assert.Equal(1967, seasons[0].Year);
            Assert.Equal(SeasonStatus.Complete, seasons[0].Status);
            Assert.Equal(1969, seasons[1].Year);
            Assert.Equal(SeasonStatus.Active, seasons[1].Status);

            var journal = JournalStore.ReadSeason(db, seasons[1].Id);
            Assert.Contains(journal, r => r.Phase == DataJournalPhases.EraTransition);
            var bridge = journal.Single(r => r.Phase == JournalPhases.EraBridge);
            Assert.Contains("1968", bridge.DeltaJson); // the gap year aged through
        }

        // Reopen = the MRU/continue path: the career opens into the LATEST season, at its
        // round 1 briefing, with the player in the seat the transition resolved.
        using var reopened = CareerSessionService.OpenCareer(CareerPath, Environment());
        var summary = reopened.Summary;
        Assert.Equal(1969, summary.SeasonYear);
        Assert.Equal("Era Test Series Mk2", summary.SeriesName);
        Assert.False(summary.SeasonComplete);
        Assert.Equal(1, summary.CurrentRound);
        Assert.Equal(2, summary.RoundCount);
        Assert.Equal(Season2Livery, summary.PlayerLiveryName);
        Assert.Equal("driver.next", summary.PlayerDriverId);

        var briefing = reopened.CurrentBriefing();
        Assert.NotNull(briefing);
        Assert.Equal(1, briefing.Round.Round);

        // Home over the reopened session lands on the new season's round-1 briefing and
        // headlines the year.
        using var home = new HomeViewModel(reopened);
        Assert.True(home.IsBriefingState);
        Assert.Equal("1969", home.SeasonYearText);
        Assert.Equal("Round 1 of 2", home.RoundText);

        // The transitioned season plays through the SAME fold as season 1.
        var grid = reopened.CurrentGrid();
        Assert.Contains(grid, s => s.IsPlayer && s.DriverId == "driver.next");
        reopened.Apply(new ResultDraft
        {
            Classified = grid.Select(s => s.DriverId).ToList(),
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
        });
        Assert.NotNull(reopened.Summary.Reputation);
        Assert.Equal(2, reopened.Summary.CurrentRound);
    }

    [Fact]
    public void Sign_WhenTheAcceptedTeamIsMissingFromTheNextPack_SurfacesThePlansValidationError()
    {
        using (var session = CreateAndPlaySeason())
        {
            var review = session.SeasonReview();
            Assert.NotNull(review);
            string acceptedTeam = review.Offers[0].TeamId;
            session.AcceptOffer(acceptedTeam);

            // The next pack deliberately lacks the accepted team's lineage.
            TestPackBuilder.Write(
                ToPack(1969, "era-test-1969", "team.somebody_else"),
                Path.Combine(PacksRoot, "era-test-1969"));

            var vm = new SeasonReviewViewModel(session);
            Assert.True(vm.HasNextSeason);
            Assert.True(vm.SignAndContinueCommand.CanExecute(null));

            bool signed = false;
            vm.SeasonSigned += (_, _) => signed = true;
            vm.SignAndContinueCommand.Execute(null);

            // The plan's validation error (EraTransition) reaches the screen; no navigation.
            Assert.False(signed);
            Assert.NotNull(vm.TransitionError);
            Assert.Contains($"team '{acceptedTeam}'", vm.TransitionError);
            Assert.Contains("does not exist in", vm.TransitionError);
        }

        // Nothing was started: the career still has exactly its 1967 season.
        using var db = CareerDatabase.Open(CareerPath);
        var seasons = CareerStore.ReadSeasons(db);
        Assert.Single(seasons);
        Assert.Equal(1967, seasons[0].Year);
    }

    [Fact]
    public void NoNextPack_ReviewExplainsWhatPacksAreAndWhereTheyGo()
    {
        using var session = CreateAndPlaySeason();
        var review = session.SeasonReview();
        Assert.NotNull(review);
        session.AcceptOffer(review.Offers[0].TeamId);

        Assert.Null(((ICareerSession)session).NextSeason()); // the packs root only has 1967

        var vm = new SeasonReviewViewModel(session);
        Assert.False(vm.HasNextSeason);
        Assert.True(vm.OfferAccepted);
        Assert.False(vm.CanSign);
        Assert.False(vm.SignAndContinueCommand.CanExecute(null));
        Assert.Null(vm.BridgeNote);
        Assert.Contains("season pack", vm.EraTransitionText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AMS2CareerCompanion\\Packs", vm.EraTransitionText);

        // The session-level guard matches the screen's state.
        var ex = Assert.Throws<InvalidOperationException>(
            () => ((ICareerSession)session).StartNextSeason(review.Offers[0].TeamId));
        Assert.Contains("No next season pack", ex.Message);
    }
}
