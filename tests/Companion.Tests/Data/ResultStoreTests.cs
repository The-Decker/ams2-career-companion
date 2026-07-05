using System.Text.Json;
using Companion.Core.Career;
using Companion.Core.Json;
using Companion.Data;

namespace Companion.Tests.Data;

public class ResultStoreTests
{
    private static (CareerDatabase Db, long SeasonId) Setup(TempDb tmp)
    {
        var db = CareerDatabase.Open(tmp.Path);
        var (seasonId, _) = DataCareerFixture.SetupCareer(db);
        return (db, seasonId);
    }

    [Fact]
    public void FirstImportInsertsWithoutJournalNoise()
    {
        using var tmp = new TempDb();
        var (db, seasonId) = Setup(tmp);
        using var _ = db;

        var import = ResultStore.Append(db, seasonId, 1, """{"round":1}""", DataCareerFixture.Utc);

        Assert.False(import.ReImported);
        Assert.False(import.PayloadChanged);
        Assert.Empty(JournalStore.ReadSeason(db, seasonId));
        Assert.Equal("""{"round":1}""", ResultStore.ReadSeasonResults(db, seasonId)[0].PayloadJson);
    }

    [Fact]
    public void ReImportingTheSamePayloadIsIdempotentAndJournaled()
    {
        using var tmp = new TempDb();
        var (db, seasonId) = Setup(tmp);
        using var _ = db;

        ResultStore.Append(db, seasonId, 1, """{"round":1}""", DataCareerFixture.Utc);
        var reimport = ResultStore.Append(db, seasonId, 1, """{"round":1}""", "2026-07-03T00:00:00Z");

        Assert.True(reimport.ReImported);
        Assert.False(reimport.PayloadChanged);

        // Still exactly one stored result for the round — idempotent on (season, round).
        var results = ResultStore.ReadSeasonResults(db, seasonId);
        Assert.Single(results);
        Assert.Equal("""{"round":1}""", results[0].PayloadJson);
        Assert.Equal("2026-07-03T00:00:00Z", results[0].EnteredUtc);

        // And the journal records the re-import as an audit row.
        var row = Assert.Single(JournalStore.ReadSeason(db, seasonId));
        Assert.Equal(DataJournalPhases.ImportResult, row.Phase);
        Assert.Equal("re-import", row.Cause);
        Assert.Equal(1, row.Round);
        Assert.Contains("\"changed\":false", row.DeltaJson);
    }

    [Fact]
    public void ReImportingACorrectedPayloadReplacesTheStoredBytes()
    {
        using var tmp = new TempDb();
        var (db, seasonId) = Setup(tmp);
        using var _ = db;

        ResultStore.Append(db, seasonId, 2, """{"round":2,"winner":"a"}""", DataCareerFixture.Utc);
        var reimport = ResultStore.Append(
            db, seasonId, 2, """{"round":2,"winner":"b"}""", DataCareerFixture.Utc, source: "corrected");

        Assert.True(reimport.ReImported);
        Assert.True(reimport.PayloadChanged);

        var stored = Assert.Single(ResultStore.ReadSeasonResults(db, seasonId));
        Assert.Equal("""{"round":2,"winner":"b"}""", stored.PayloadJson);
        Assert.Equal("corrected", stored.Source);

        var row = Assert.Single(JournalStore.ReadSeason(db, seasonId));
        Assert.Contains("\"changed\":true", row.DeltaJson);
    }

    [Fact]
    public void ResultsReadBackInRoundOrderRegardlessOfImportOrder()
    {
        using var tmp = new TempDb();
        var (db, seasonId) = Setup(tmp);
        using var _ = db;

        ResultStore.Append(db, seasonId, 3, """{"round":3}""", DataCareerFixture.Utc);
        ResultStore.Append(db, seasonId, 1, """{"round":1}""", DataCareerFixture.Utc);
        ResultStore.Append(db, seasonId, 2, """{"round":2}""", DataCareerFixture.Utc);

        Assert.Equal(new[] { 1, 2, 3 }, ResultStore.ReadSeasonResults(db, seasonId).Select(r => r.Round));
    }

    // ---------- versioned envelope ----------

    [Fact]
    public void EnvelopePayloadRoundTripsSliderAndDnfContext()
    {
        using var tmp = new TempDb();
        var (db, seasonId) = Setup(tmp);
        using var _ = db;

        var envelope = new RoundResultEnvelope
        {
            Result = DataCareerFixture.Rounds()[0],
            SliderUsed = 96.0,
            PlayerDnfCause = DnfCause.DriverError,
        };
        ResultStore.Append(
            db, seasonId, 1,
            JsonSerializer.Serialize(envelope, CoreJson.Options),
            DataCareerFixture.Utc);

        var stored = ResultStore.ReadSeasonResults(db, seasonId)[0].ToEnvelope();
        Assert.Equal(RoundResultEnvelope.CurrentVersion, stored.Version);
        Assert.Equal(96.0, stored.SliderUsed);
        Assert.Equal(DnfCause.DriverError, stored.PlayerDnfCause);
        Assert.Equal(
            JsonSerializer.Serialize(DataCareerFixture.Rounds()[0], CoreJson.Options),
            JsonSerializer.Serialize(stored.Result, CoreJson.Options));
    }

    [Fact]
    public void LegacyBareRoundResultPayloadReadsWithDefaults()
    {
        using var tmp = new TempDb();
        var (db, seasonId) = Setup(tmp);
        using var _ = db;

        // A pre-envelope career stored the RoundResult directly — it must read as a
        // version-1 envelope with unknown slider and DNF cause.
        var round = DataCareerFixture.Rounds()[0];
        ResultStore.Append(
            db, seasonId, 1,
            JsonSerializer.Serialize(round, CoreJson.Options),
            DataCareerFixture.Utc);

        var stored = ResultStore.ReadSeasonResults(db, seasonId)[0];
        var envelope = stored.ToEnvelope();
        Assert.Equal(1, envelope.Version);
        Assert.Null(envelope.SliderUsed);
        Assert.Null(envelope.PlayerDnfCause);
        string original = JsonSerializer.Serialize(round, CoreJson.Options);
        Assert.Equal(original, JsonSerializer.Serialize(envelope.Result, CoreJson.Options));
        Assert.Equal(original, JsonSerializer.Serialize(stored.ToRoundResult(), CoreJson.Options));
    }
}
