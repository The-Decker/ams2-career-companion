using Companion.Core.Career;
using Companion.Core.Packs;
using Companion.Data;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Debug;
using Companion.ViewModels.Services;

namespace Companion.Tests.Data;

/// <summary>
/// FOLD SAFETY (build brief §4/§7): a career created and advanced entirely through the debug menu's
/// Tier-1 helpers routes through the SAME provenance-excluded INPUT seams the normal app uses
/// (<c>CareerCreationRequest</c> → create, then <c>Apply</c> / <c>ApplySkillPlan</c> / offers /
/// <c>StartNextSeason</c>). It must therefore resimulate byte-identical — proving the debug menu
/// creates honest saves and never pokes derived state.
/// </summary>
public sealed class DebugCareerResimTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-debug-resim-").FullName;
    private string PacksRoot => Path.Combine(_root, "packs");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void DebugCreatedDynastyCareer_ResimulatesByteIdentical()
    {
        const long seed = 20260717;
        WritePack(1967);
        WritePack(1969);
        WritePack(2020);
        var environment = Environment();

        string careerPath = Path.Combine(_root, "careers", "debug.ams2career");
        var request = DebugCareerFactory.BuildRequest(
            Path.Combine(PacksRoot, "1967"), careerPath,
            CareerExperienceModes.GrandPrixDynasty, seed);

        using (var session = CareerSessionService.CreateCareer(request, environment))
        {
            // Advance a whole season through the real Apply INPUT.
            int rounds = DebugCareerFactory.FastForwardToSeasonEnd(session);
            Assert.True(rounds >= 1);
            Assert.NotNull(session.SeasonReview());

            // Spend a Skill Point through the real ApplySkillPlan INPUT.
            Assert.True(session.AvailableCharacterCp() > 0);
            string? node = DebugCareerFactory.TrySpendOneSkillPoint(session);
            Assert.NotNull(node);

            // Sign into the next season through the real offer + transition INPUTs.
            var review = Assert.IsType<SeasonReviewModel>(session.SeasonReview());
            string teamId = review.Offers[0].TeamId;
            session.AcceptOffer(teamId);
            session.StartNextSeason(teamId);
        }

        using var db = CareerDatabase.Open(careerPath);
        var rules = environment.Rules;
        var report = ReplayService.Resimulate(db, unchecked((ulong)seed), new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = "driver.hulme",
            PlayerAge = 22,
            CharacterRules = rules.Character,
            MasterySkills = rules.MasterySkills,
        });

        Assert.True(
            report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} " +
            $"stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
    }

    [Fact]
    public void DebugCreatedSmgpCareer_JumpedToSeasonThree_ResimulatesByteIdentical()
    {
        const long seed = 20260717;
        string packDirectory = Path.Combine(PacksRoot, "smgp-ladder");
        TestPackBuilder.Write(SmgpLadderPack(), packDirectory);
        var environment = Environment(FiveSeatLibrary());

        string careerPath = Path.Combine(_root, "careers", "debug-smgp.ams2career");
        var request = DebugCareerFactory.BuildRequest(
            packDirectory, careerPath, CareerExperienceModes.Smgp, seed, playerLivery: PlayerSeat);

        ICareerSession session = CareerSessionService.CreateCareer(request, environment);
        try
        {
            session = DebugCareerFactory.AdvanceSmgpToSeason(session, 3, old =>
            {
                (old as IDisposable)?.Dispose();
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                return CareerSessionService.OpenCareer(careerPath, environment);
            }, out string note);

            // Reached the target through real INPUT seams only, with no early-stop note.
            Assert.Equal("", note);
            Assert.Equal(3, session.CurrentSmgpBriefing()?.SeasonOrdinal);
        }
        finally
        {
            (session as IDisposable)?.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }

        using var db = CareerDatabase.Open(careerPath);
        var rules = environment.Rules;
        var report = ReplayService.Resimulate(db, unchecked((ulong)seed), new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = Companion.Core.Grid.RoundGridResolver.SyntheticPlayerDriverId,
            PlayerAge = 22,
            CharacterRules = rules.Character,
            MasterySkills = rules.MasterySkills,
        });

        Assert.True(
            report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} " +
            $"stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
        Assert.True(report.ComparedRows > 0);
    }

    [Fact]
    public void DebugContinuedDynastyCareer_AdvancesTwoSeasonEnds_ResimulatesByteIdentical()
    {
        const long seed = 20260717;
        WritePack(1967);
        WritePack(1969);
        WritePack(2020);
        var environment = Environment();

        string careerPath = Path.Combine(_root, "careers", "debug-continue.ams2career");
        var request = DebugCareerFactory.BuildRequest(
            Path.Combine(PacksRoot, "1967"), careerPath,
            CareerExperienceModes.GrandPrixDynasty, seed);

        // Mirror the menu's AdvanceSeason click TWICE, reopening between clicks exactly like the
        // shell does (superseded sessions stay open meanwhile, as the hub's would).
        var sessions = new List<ICareerSession>();
        try
        {
            var session = CareerSessionService.CreateCareer(request, environment);
            sessions.Add(session);

            // Click 1 (mid-season): plays season 1 out to its end.
            DebugCareerFactory.AdvanceToNextSeasonEnd(session, out string note);
            Assert.Equal("", note);
            session = CareerSessionService.OpenCareer(careerPath, environment);
            sessions.Add(session);
            DebugCareerFactory.FinishSeason(session); // no-op at an end
            Assert.True(session.Summary.SeasonComplete);
            Assert.Equal(1967, session.Summary.SeasonYear);

            // Click 2 (season end): signs into the next era and plays THAT season out too.
            DebugCareerFactory.AdvanceToNextSeasonEnd(session, out note);
            Assert.Equal("", note);
            session = CareerSessionService.OpenCareer(careerPath, environment);
            sessions.Add(session);
            DebugCareerFactory.FinishSeason(session);
            Assert.True(session.Summary.SeasonComplete);
            Assert.Equal(1969, session.Summary.SeasonYear);
        }
        finally
        {
            foreach (var s in sessions)
                (s as IDisposable)?.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }

        using var db = CareerDatabase.Open(careerPath);
        Assert.Equal(2, CareerStore.ReadSeasons(db).Count);
        var rules = environment.Rules;
        var report = ReplayService.Resimulate(db, unchecked((ulong)seed), new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = "driver.hulme",
            PlayerAge = 22,
            CharacterRules = rules.Character,
            MasterySkills = rules.MasterySkills,
        });

        Assert.True(
            report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} " +
            $"stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
        Assert.True(report.ComparedRows > 0);
    }

    // ---------- SMGP scaffolding (mirrors SmgpMultiSeasonDnqTests' ladder shape) ----------
    private const string PlayerSeat = "Stock Livery #3"; // team.c — a midfield start on the ladder

    /// <summary>Five one-driver teams down the ladder over the REQUIRED 16-round replica season
    /// (the bounded SMGP campaign plan rejects any other round count), each round capping the grid
    /// at 4 — the minimal SMGP-shape pack a real SMGP career can be created from.</summary>
    private static SeasonPack SmgpLadderPack()
    {
        var basePack = TestPackBuilder.TwoRoundPack();
        var grid = new PackRoundGrid
        {
            Size = 4,
            StarterDriverIds = ["driver.a", "driver.b", "driver.c", "driver.d"],
        };
        var template = basePack.Season.Rounds[0] with { Grid = grid };
        return basePack with
        {
            Manifest = basePack.Manifest with
            {
                PackId = "smgp-ladder",
                Name = "SMGP Ladder",
                CareerStyle = Companion.Core.Smgp.SmgpRules.CareerStyle,
            },
            Teams =
            [
                SmgpTeam("team.a", 5), SmgpTeam("team.b", 4), SmgpTeam("team.c", 3),
                SmgpTeam("team.d", 2), SmgpTeam("team.e", 3),
            ],
            Drivers =
            [
                TestPackBuilder.Driver("driver.a"), TestPackBuilder.Driver("driver.b"),
                TestPackBuilder.Driver("driver.c"), TestPackBuilder.Driver("driver.d"),
                TestPackBuilder.Driver("driver.e"),
            ],
            Entries = new[]
            {
                TestPackBuilder.Entry("team.a", "driver.a", "1", "Stock Livery #1"),
                TestPackBuilder.Entry("team.b", "driver.b", "2", "Stock Livery #2"),
                TestPackBuilder.Entry("team.c", "driver.c", "3", PlayerSeat),
                TestPackBuilder.Entry("team.d", "driver.d", "4", "Stock Livery #4"),
                TestPackBuilder.Entry("team.e", "driver.e", "5", "Stock Livery #5"),
            }.Select(entry => entry with { Rounds = "1-16" }).ToArray(),
            Season = basePack.Season with
            {
                Year = 1990,
                Rounds = Enumerable.Range(1, 16).Select(round => template with
                {
                    Round = round,
                    Name = round == 16 ? "Monaco" : $"Campaign Round {round}",
                    Date = $"1990-01-{round:00}",
                }).ToArray(),
            },
        };
    }

    private static PackTeam SmgpTeam(string id, int prestige) => new()
    {
        Id = id,
        Name = id,
        CarVehicleIds = [TestPackBuilder.VintageCar],
        Reliability = 0.93,
        Prestige = prestige,
        BudgetTier = prestige,
    };

    private static Companion.Ams2.ContentLibrary.Ams2ContentLibrary FiveSeatLibrary()
    {
        var library = TestPackBuilder.Library();
        return new()
        {
            ExtractedFrom = library.ExtractedFrom,
            Classes = library.Classes,
            Vehicles = library.Vehicles,
            Tracks = library.Tracks,
            Liveries = new Dictionary<string, Companion.Ams2.ContentLibrary.Ams2LiveryClassEntry>(
                StringComparer.Ordinal)
            {
                [TestPackBuilder.VintageClass] = new()
                {
                    Name = TestPackBuilder.VintageClass,
                    StockLib1563 =
                        ["Stock Livery #1", "Stock Livery #2", PlayerSeat, "Stock Livery #4", "Stock Livery #5"],
                },
            },
        };
    }

    private CareerEnvironment Environment(
        Companion.Ams2.ContentLibrary.Ams2ContentLibrary? library = null)
    {
        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "documents"),
            library: library ?? TestPackBuilder.Library());
        environment.PackSearchRoots = () => [PacksRoot];
        return environment;
    }

    private void WritePack(int year) =>
        TestPackBuilder.Write(SyntheticPack(year), Path.Combine(PacksRoot, year.ToString()));

    private static SeasonPack SyntheticPack(int year)
    {
        var pack = TestPackBuilder.TwoRoundPack();
        return pack with
        {
            Manifest = pack.Manifest with { PackId = $"dynasty-{year}", Name = $"Synthetic {year}" },
            Season = pack.Season with
            {
                Year = year,
                SeriesName = $"Synthetic Championship {year}",
                Rounds = [TestPackBuilder.Round(1, $"{year}-01-02"), TestPackBuilder.Round(2, $"{year}-05-07")],
            },
        };
    }
}
