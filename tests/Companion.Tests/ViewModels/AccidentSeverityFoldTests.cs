using Companion.Core.Career;
using Companion.Core.Packs;
using Companion.Data;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// Character death &amp; injury, Slice 2 (accident severity input): the player's Light/Medium/Heavy accident
/// severity is CAPTURED on the round envelope (v7) but NOTHING consumes it yet, so a career that records an
/// accident-with-severity round re-simulates BYTE-IDENTICALLY, the legacy gate holds until Slice 3 folds it.
/// </summary>
public sealed class AccidentSeverityFoldTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-accident-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void AccidentSeverityCareer_CapturesSeverity_AndReplaysByteIdentically()
    {
        string packDirectory = Path.Combine(_root, "pack");
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), packDirectory);
        string careerPath = Path.Combine(_root, "careers", "accident.ams2career");
        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());

        const string playerId = "driver.hulme";
        const long seed = 20260712;
        SeasonPack pack;

        using (var session = CareerSessionService.CreateCareer(
                   new CareerCreationRequest
                   {
                       PackDirectory = packDirectory,
                       CareerFilePath = careerPath,
                       CareerName = "Accident Career",
                       MasterSeed = seed,
                       PlayerLiveryName = TestPackBuilder.StockLivery2,
                   },
                   environment))
        {
            pack = session.Pack;

            // Round 1: the PLAYER retires with a HEAVY accident (severity captured on the envelope).
            var seats = session.CurrentGrid();
            session.Apply(new ResultDraft
            {
                Classified = seats.Where(s => s.DriverId != playerId).Select(s => s.DriverId).ToList(),
                DidNotFinish = new Dictionary<string, string> { [playerId] = "a" },
                Disqualified = [],
                PlayerAccidentSeverity = AccidentSeverity.Heavy,
            });

            // Round 2: an ordinary finish.
            var seats2 = session.CurrentGrid();
            session.Apply(new ResultDraft
            {
                Classified = seats2.Select(s => s.DriverId).ToList(),
                DidNotFinish = new Dictionary<string, string>(),
                Disqualified = [],
            });
            Assert.NotNull(session.SeasonReview());
        }

        // The severity + the accident cause were captured on round 1's stored envelope.
        using (var db = CareerDatabase.Open(careerPath))
        {
            long seasonId = CareerStore.ReadSeasons(db)[0].Id;
            var round1 = ResultStore.ReadSeasonResults(db, seasonId)[0].ToEnvelope();
            Assert.Equal(RoundResultEnvelope.CurrentVersion, round1.Version); // v7
            Assert.Equal(AccidentSeverity.Heavy, round1.PlayerAccidentSeverity);
            Assert.Equal(DnfCause.DriverError, round1.PlayerDnfCause);
        }

        // Nothing folds the severity yet ⇒ the whole career re-simulates byte-identically.
        using (var db = CareerDatabase.Open(careerPath))
        {
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
}
