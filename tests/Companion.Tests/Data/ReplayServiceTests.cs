using Companion.Core.Career;
using Companion.Data;

namespace Companion.Tests.Data;

public class ReplayServiceTests
{
    private static (CareerDatabase Db, long SeasonId, Companion.Core.Packs.SeasonPack Pack) PlayedCareer(TempDb tmp)
    {
        var db = CareerDatabase.Open(tmp.Path);
        var (seasonId, pack) = DataCareerFixture.SetupCareer(db);
        DataCareerFixture.PlaySeason(db, seasonId, pack);
        return (db, seasonId, pack);
    }

    [Fact]
    public void MiniSeasonReplaysByteIdentical()
    {
        using var tmp = new TempDb();
        var (db, seasonId, pack) = PlayedCareer(tmp);
        using var _ = db;

        var report = ReplayService.Resimulate(
            db, pack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs());

        Assert.True(report.Identical);
        Assert.Null(report.FirstDivergence);

        // 3 rounds x (6 drivers + 3 constructors) standings rows + the season-end pipeline
        // rows must all have been compared.
        int storedSimRows = JournalStore.ReadSeason(db, seasonId)
            .Count(r => !r.Phase.StartsWith("import.", StringComparison.Ordinal));
        Assert.Equal(storedSimRows, report.ComparedRows);
        Assert.True(report.ComparedRows > 27, $"Expected season-end rows beyond the 27 standings rows, got {report.ComparedRows}.");
    }

    [Fact]
    public void ReplayRebuildsDerivedStateFromRawResults()
    {
        using var tmp = new TempDb();
        var (db, seasonId, pack) = PlayedCareer(tmp);
        using var _ = db;

        var driversBefore = StateStore.ReadDriverStates(db, seasonId, StateStore.StageEnd);
        var teamsBefore = StateStore.ReadTeamStates(db, seasonId, StateStore.StageEnd);
        var playerBefore = StateStore.ReadPlayerState(db, seasonId, StateStore.StageEnd);
        var offersBefore = StateStore.ReadOffers(db, seasonId);
        Assert.NotEmpty(driversBefore);
        Assert.NotEmpty(offersBefore);

        var report = ReplayService.Resimulate(
            db, pack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs());

        Assert.True(report.Identical);
        Assert.Equal(driversBefore, StateStore.ReadDriverStates(db, seasonId, StateStore.StageEnd));
        Assert.Equal(teamsBefore, StateStore.ReadTeamStates(db, seasonId, StateStore.StageEnd));
        Assert.Equal(playerBefore, StateStore.ReadPlayerState(db, seasonId, StateStore.StageEnd));
        Assert.Equal(offersBefore, StateStore.ReadOffers(db, seasonId));
    }

    [Fact]
    public void AcceptedOfferSurvivesResimulation()
    {
        using var tmp = new TempDb();
        var (db, seasonId, pack) = PlayedCareer(tmp);
        using var _ = db;

        string acceptedTeam = StateStore.ReadOffers(db, seasonId)[0].Terms.TeamId;
        StateStore.SetOfferAccepted(db, seasonId, acceptedTeam);

        var report = ReplayService.Resimulate(
            db, pack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs());

        Assert.True(report.Identical);
        var offers = StateStore.ReadOffers(db, seasonId);
        Assert.True(offers.Single(o => o.Terms.TeamId == acceptedTeam).Accepted);
        Assert.Single(offers, o => o.Accepted);
    }

    [Fact]
    public void TamperedJournalRowIsCaughtAsTheFirstDivergence()
    {
        using var tmp = new TempDb();
        var (db, seasonId, pack) = PlayedCareer(tmp);
        using var _ = db;

        // Corrupt one round-2 standings row's delta.
        var victim = JournalStore.ReadSeason(db, seasonId)
            .First(r => r.Phase == DataJournalPhases.RoundStandings && r.Round == 2);
        using (var tamper = db.Connection.CreateCommand())
        {
            tamper.CommandText = "UPDATE journal SET delta_json = @delta WHERE seq = @seq;";
            tamper.Parameters.AddWithValue("@delta", """{"position":99,"points":"999"}""");
            tamper.Parameters.AddWithValue("@seq", victim.Seq);
            tamper.ExecuteNonQuery();
        }

        var report = ReplayService.Resimulate(
            db, pack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs());

        Assert.False(report.Identical);
        var divergence = report.FirstDivergence;
        Assert.NotNull(divergence);
        Assert.Equal(seasonId, divergence.SeasonId);
        Assert.Equal(victim.Seq, divergence.StoredSeq);
        Assert.Equal("deltaJson", divergence.Reason);
        Assert.Equal("""{"position":99,"points":"999"}""", divergence.StoredDeltaJson);
        // The regenerated side carries the truth the raw results refold to.
        Assert.Equal(victim.DeltaJson, divergence.RegeneratedDeltaJson);
    }

    [Fact]
    public void ReImportAuditRowsDoNotBreakReplay()
    {
        using var tmp = new TempDb();
        var (db, seasonId, pack) = PlayedCareer(tmp);
        using var _ = db;

        // Re-import round 2 with the identical payload: adds an import.result audit row,
        // which the byte-compare excludes by contract.
        string payload = ResultStore.ReadSeasonResults(db, seasonId)[1].PayloadJson;
        var reimport = ResultStore.Append(db, seasonId, 2, payload, "2026-07-04T00:00:00Z");
        Assert.True(reimport.ReImported);
        Assert.False(reimport.PayloadChanged);

        var report = ReplayService.Resimulate(
            db, pack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs());

        Assert.True(report.Identical);
    }

    [Fact]
    public void CorrectedReImportShowsUpAsADivergenceUntilResimulated()
    {
        using var tmp = new TempDb();
        var (db, seasonId, pack) = PlayedCareer(tmp);
        using var _ = db;

        // Round 3 gets a corrected result: swap the top two finishers.
        var corrected = DataCareerFixture.Rounds()[2];
        var session = corrected.Sessions[0];
        var swapped = session.Entries.ToList();
        (swapped[0], swapped[1]) = (
            swapped[1] with { Position = 1 },
            swapped[0] with { Position = 2 });
        corrected = corrected with { Sessions = [session with { Entries = swapped }] };

        string payload = System.Text.Json.JsonSerializer.Serialize(
            corrected, Companion.Core.Json.CoreJson.Options);
        var reimport = ResultStore.Append(db, seasonId, 3, payload, "2026-07-04T00:00:00Z");
        Assert.True(reimport.PayloadChanged);

        // The stored journal was folded from the OLD payload — replay must flag it.
        var report = ReplayService.Resimulate(
            db, pack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs());
        Assert.False(report.Identical);
        Assert.NotNull(report.FirstDivergence);
    }

    [Fact]
    public void MasterSeedIsPartOfTheReplayContract()
    {
        using var tmp = new TempDb();
        var (db, seasonId, pack) = PlayedCareer(tmp);
        using var _ = db;

        var report = ReplayService.Resimulate(
            db, pack, DataCareerFixture.MasterSeed + 1, DataCareerFixture.Inputs());

        // Standings rows are seed-independent, but season-end rolls are not: the wrong
        // seed must be detected as a divergence.
        Assert.False(report.Identical);
        Assert.NotNull(report.FirstDivergence);
    }

    [Fact]
    public void SuppliedPackMustMatchThePinnedBytes()
    {
        using var tmp = new TempDb();
        var (db, _, pack) = PlayedCareer(tmp);
        using var _2 = db;

        var mutated = pack with { Manifest = pack.Manifest with { Name = "Not The Pinned Pack" } };
        var ex = Assert.Throws<InvalidOperationException>(() => ReplayService.Resimulate(
            db, mutated, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs()));
        Assert.Contains("pinned", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunSeasonEndRefusesToRunTwice()
    {
        using var tmp = new TempDb();
        var (db, seasonId, pack) = PlayedCareer(tmp);
        using var _ = db;

        Assert.Throws<InvalidOperationException>(() => ReplayService.RunSeasonEnd(
            db, seasonId, pack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs(), DataCareerFixture.Utc));
    }

    [Fact]
    public void ActiveSeasonReplaysItsRoundsWithoutASeasonEnd()
    {
        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);
        var (seasonId, pack) = DataCareerFixture.SetupCareer(db);

        // Import two of three rounds, no season end yet — mid-season replay must hold.
        var soFar = new List<Companion.Core.Scoring.RoundResult>();
        foreach (var round in DataCareerFixture.Rounds().Take(2))
        {
            soFar.Add(round);
            string payload = System.Text.Json.JsonSerializer.Serialize(
                round, Companion.Core.Json.CoreJson.Options);
            ResultStore.Append(db, seasonId, round.Round, payload, DataCareerFixture.Utc);
            JournalStore.AppendMany(
                db, seasonId, round.Round, ReplayService.RoundStandingsEvents(pack, soFar), DataCareerFixture.Utc);
        }

        var report = ReplayService.Resimulate(
            db, pack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs());

        Assert.True(report.Identical);
        Assert.Equal(18, report.ComparedRows); // 2 rounds x (6 drivers + 3 constructors)
        Assert.Empty(StateStore.ReadOffers(db, seasonId));
    }
}
