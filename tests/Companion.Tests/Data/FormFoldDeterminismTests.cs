using Companion.Core.Career;
using Companion.Core.Determinism;
using Companion.Core.Packs;
using Companion.Data;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Services;

namespace Companion.Tests.Data;

/// <summary>
/// OFF-path determinism gate for per-race form (<see cref="SeasonDefinition.DriverForm"/>): a
/// PRE-Phase-3 career, one that is NOT <see cref="Companion.Core.Career.PlayerCareerState.FormAware"/>
/// (the default; the request here does not opt in), folds form-inert and re-simulates BYTE-IDENTICALLY,
/// even on a pack that ships form deltas. The fold never reads DriverForm for such a career, so it can
/// never move a replayed result; form only changes what AMS2 shows on track (via <c>GridStager.Build</c>).
/// This is what keeps EXISTING careers on the form-carrying bundled packs byte-identical. The sibling
/// <see cref="FormReactiveFoldDeterminismTests"/> proves the ON path (a FormAware career reacts + still
/// replays byte-identically).
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
        // diverge, so a byte-identical replay proves the fold never reads them.
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
