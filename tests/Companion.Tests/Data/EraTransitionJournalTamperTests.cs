using Companion.Data;

namespace Companion.Tests.Data;

/// <summary>
/// M6 verification (added by the adversarial-verification pass): proves the cross-transition
/// replay byte-compare has teeth on the TRANSITION journal rows themselves — the
/// byte-identical test could in principle pass with a compare that skipped the era.* rows,
/// so tamper one and demand a divergence at exactly that row.
/// </summary>
public class EraTransitionJournalTamperTests
{
    [Fact]
    public void TamperedEraBridgeJournalRowIsADeltaJsonDivergenceWithZeroDataLoss()
    {
        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);
        var (_, season2, _, _) = EraTransitionFixture.PlayTransitionedCareer(db);

        using (var tamper = db.Connection.CreateCommand())
        {
            tamper.CommandText = """
                UPDATE journal SET delta_json = '{"year":1968,"aged":0,"retired":[]}'
                WHERE season_id = @season AND phase = 'era.bridge';
                """;
            tamper.Parameters.AddWithValue("@season", season2);
            Assert.Equal(1, tamper.ExecuteNonQuery());
        }
        var journalBefore = JournalStore.ReadSeason(db, season2);

        var report = ReplayService.Resimulate(
            db, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs());

        Assert.False(report.Identical);
        Assert.NotNull(report.FirstDivergence);
        Assert.Equal("deltaJson", report.FirstDivergence.Reason);
        Assert.Equal(season2, report.FirstDivergence.SeasonId);
        Assert.Equal("""{"year":1968,"aged":0,"retired":[]}""", report.FirstDivergence.StoredDeltaJson);

        // Report-only contract: the transaction rolled back, the tampered journal is intact.
        Assert.Equal(journalBefore, JournalStore.ReadSeason(db, season2));
    }
}
