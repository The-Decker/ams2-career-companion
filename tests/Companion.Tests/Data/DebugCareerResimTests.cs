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

    private CareerEnvironment Environment()
    {
        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "documents"),
            library: TestPackBuilder.Library());
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
