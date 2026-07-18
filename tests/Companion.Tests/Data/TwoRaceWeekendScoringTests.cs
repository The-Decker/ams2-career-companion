using Companion.Core.Numerics;
using Companion.Data;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Services;

namespace Companion.Tests.Data;

/// <summary>
/// Increment 2 (slice A), the data/scoring layer for an authored TWO-race weekend, end-to-end
/// through the real session service. A <see cref="ResultDraft"/> with an additional race maps onto
/// a two-session <c>RoundResult</c> with <c>PerSessionScoring</c> set and the per-session points
/// tables bound from the pack's <c>weekend.races</c>, so each race scores on its own table and both
/// count toward the standings. (The per-session FOLD, independent OPI/rep per race, is a later
/// slice; this pins the scoring + persistence.)
/// </summary>
public sealed class TwoRaceWeekendScoringTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-two-race-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // SQLite WAL sidecars can outlive the connection briefly on Windows.
        }
    }

    [Fact]
    public void TwoRaceWeekend_ScoresEachRaceOnItsOwnTable_AndPersistsTheTwoSessions()
    {
        string packDirectory = Path.Combine(_root, "pack");
        TestPackBuilder.Write(TestPackBuilder.TwoRaceWeekendPack(), packDirectory);
        string careerPath = Path.Combine(_root, "careers", "two-race.ams2career");

        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());

        // Round 1 weekend: FEATURE (primary table 9-6-4-3-2-1) then SPRINT (sprint table 8-6-4-3-2-1).
        // Feature:  Hulme P1 (9), Brabham P2 (6).   Sprint:  Brabham P1 (8), Hulme P2 (6).
        // Per session, summed: Hulme = 9 + 6 = 15,  Brabham = 6 + 8 = 14.
        var draft = new ResultDraft
        {
            Classified = ["driver.hulme", "driver.brabham"],
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
            AdditionalRaces =
            [
                new ExtraRaceResult { Classified = ["driver.brabham", "driver.hulme"] },
            ],
        };

        using (var session = CareerSessionService.CreateCareer(
                   new CareerCreationRequest
                   {
                       PackDirectory = packDirectory,
                       CareerFilePath = careerPath,
                       CareerName = "Two-Race Weekend",
                       MasterSeed = 99,
                       PlayerLiveryName = TestPackBuilder.StockLivery2, // driver.hulme
                   },
                   environment))
        {
            // The confirm screen sums both races' points per driver.
            var preview = session.Preview(draft);
            Assert.Equal(new Rational(15), preview.RoundPoints.Single(p => p.DriverId == "driver.hulme").Points);
            Assert.Equal(new Rational(14), preview.RoundPoints.Single(p => p.DriverId == "driver.brabham").Points);

            session.Apply(draft);

            // Standings after the two-race round reflect the per-session totals.
            var standings = session.CurrentStandings();
            Assert.NotNull(standings);
            var hulme = standings.Drivers.Single(d => d.DriverId == "driver.hulme");
            var brabham = standings.Drivers.Single(d => d.DriverId == "driver.brabham");
            Assert.Equal(new Rational(15), hulme.CountedPoints);
            Assert.Equal(new Rational(14), brabham.CountedPoints);
            Assert.Equal(1, hulme.Position);
            Assert.Equal(2, brabham.Position);

            // The two per-session scores are kept distinct on the winner's card (sub-keyed by session).
            Assert.Equal(2, hulme.RoundScores.Count(s => s.SessionIndex is not null));
        }

        // The raw envelope persisted BOTH races as a per-session-scored two-session round, with the
        // sprint race bound to the sprint table.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        var result = ResultStore.ReadSeasonResults(db, seasonId)[0].ToEnvelope().Result;

        Assert.True(result.PerSessionScoring);
        Assert.Equal(2, result.Sessions.Count);
        Assert.Null(result.Sessions[0].PointsTableId);      // feature → the round's default (primary)
        Assert.Equal("sprint", result.Sessions[1].PointsTableId);
    }
}
