using Companion.Core.Career;
using Companion.Core.Determinism;
using Companion.Core.Packs;
using Companion.Data;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Services;

namespace Companion.Tests.Data;

/// <summary>
/// Determinism gate for the STAGING-ONLY per-race form overlay (<see cref="SeasonDefinition.DriverForm"/>):
/// a career on a pack that carries per-round form deltas re-simulates BYTE-IDENTICALLY. Form is read
/// only when the app writes the AMS2 custom-AI file (<c>GridStager.Build</c>); the resolver, fold,
/// scoring engine and f1db oracle never touch it, so it can never move a replayed result. This proves
/// the "sim-inert" contract: adding form to a pack changes what AMS2 shows on track, nothing the sim
/// scores.
/// </summary>
public sealed class FormFoldDeterminismTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-form-fold-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void CareerWithPerRaceForm_ReplaysByteIdentically()
    {
        string packDirectory = Path.Combine(_root, "pack");
        var basePack = TestPackBuilder.TwoRoundPack();
        // Author a per-race form overlay: both seats get a nudge in round 1, one in round 2. These
        // deltas are large enough that, were they to leak into the resolved grid, the fold would
        // diverge — so a byte-identical replay proves the fold never reads them.
        var formPack = basePack with
        {
            Season = basePack.Season with
            {
                DriverForm = new Dictionary<int, IReadOnlyDictionary<string, PackDriverForm>>
                {
                    [1] = new Dictionary<string, PackDriverForm>
                    {
                        ["driver.brabham"] = new() { RaceSkill = 0.07, QualifyingSkill = 0.05 },
                        ["driver.hulme"] = new() { RaceSkill = -0.06, QualifyingSkill = -0.04 },
                    },
                    [2] = new Dictionary<string, PackDriverForm>
                    {
                        ["driver.brabham"] = new() { RaceSkill = -0.05, QualifyingSkill = -0.03 },
                    },
                },
            },
        };
        TestPackBuilder.Write(formPack, packDirectory);
        string careerPath = Path.Combine(_root, "careers", "form.ams2career");

        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());

        const string playerId = "driver.hulme";
        const long seed = 20260707;
        SeasonPack pack;

        using (var session = CareerSessionService.CreateCareer(
                   new CareerCreationRequest
                   {
                       PackDirectory = packDirectory,
                       CareerFilePath = careerPath,
                       CareerName = "Form Career",
                       MasterSeed = seed,
                       PlayerLiveryName = TestPackBuilder.StockLivery2,
                   },
                   environment))
        {
            pack = session.Pack;

            // The form overlay round-tripped through season.json and was pinned with the pack.
            Assert.NotNull(pack.Season.DriverForm);
            Assert.Equal(2, pack.Season.DriverForm!.Count);

            for (int round = 0; round < 2; round++)
            {
                var seats = session.CurrentGrid();
                session.Apply(new ResultDraft
                {
                    Classified = seats.Select(s => s.DriverId).ToList(),
                    DidNotFinish = new Dictionary<string, string>(),
                    Disqualified = [],
                });
            }
            Assert.NotNull(session.SeasonReview());
        }

        using var db = CareerDatabase.Open(careerPath);
        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);

        var inputs = new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = playerId,
            PlayerAge = 30,
            CharacterRules = rules.Character,
        };

        var report = ReplayService.Resimulate(db, pack, unchecked((ulong)seed), inputs);
        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
    }
}
